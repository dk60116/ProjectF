using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;

public sealed partial class ProductionWorld
{
    private readonly struct FluidOutputTarget
    {
        internal readonly InstallationObject Storage;
        internal readonly ProductionFacilityInstance Receiver;
        internal readonly Vector2Int Coordinate, SourcePort;
        internal readonly float PressureRetention;
        internal readonly Pump Pump;
        internal FluidOutputTarget(InstallationObject storage, ProductionFacilityInstance receiver, Vector2Int coordinate, int distance, Pump pump, Vector2Int sourcePort)
        { Storage = storage; Receiver = receiver; Coordinate = coordinate; PressureRetention = InstallationObject.FluidPressureRetention(distance); Pump = pump; SourcePort = sourcePort; }
    }
    private sealed class FluidOutputRoute
    {
        internal int TopologyVersion = int.MinValue;
        internal readonly Dictionary<Vector2Int, int> PortFluids = new Dictionary<Vector2Int, int>();
        internal readonly List<FluidSource> Sources = new List<FluidSource>(2);
        internal readonly List<FluidOutputTarget> Targets = new List<FluidOutputTarget>(2);
    }
    private readonly struct FluidSource
    {
        internal readonly Vector2Int Port;
        internal readonly IDataFluidProducer Data;
        internal readonly InputOutputModule Native;
        internal FluidSource(Vector2Int port, IDataFluidProducer data, InputOutputModule native)
        { Port = port; Data = data; Native = native; }
    }
    // Compact endpoint lists are cached, not a complete pipe-search context per installation.
    private readonly Dictionary<ProductionFacilityInstance, FluidOutputRoute> outputRoutes = new Dictionary<ProductionFacilityInstance, FluidOutputRoute>();
    private readonly Dictionary<InstallationObject, HashSet<ProductionFacilityInstance>> storageOutputWaiters = new Dictionary<InstallationObject, HashSet<ProductionFacilityInstance>>();
    private readonly Dictionary<ProductionFacilityInstance, HashSet<ProductionFacilityInstance>> dataOutputWaiters = new Dictionary<ProductionFacilityInstance, HashSet<ProductionFacilityInstance>>();
    internal long FluidRouteSearches { get; private set; }
    internal bool HasFluidOutputTargets(ProductionFacilityInstance facility) => GetFluidOutputRoute(facility).Targets.Count != 0;

    private FluidOutputRoute GetFluidOutputRoute(ProductionFacilityInstance facility)
    {
        if (!outputRoutes.TryGetValue(facility, out var route)) outputRoutes.Add(facility, route = new FluidOutputRoute());
        if (route.TopologyVersion == InputOutputModule.FluidTopologyVersion) return route;
        RemoveRouteWaiters(facility, route);
        route.Targets.Clear(); route.PortFluids.Clear(); route.Sources.Clear();
        route.TopologyVersion = InputOutputModule.FluidTopologyVersion;
        for (int i = 0; i < facility.OutputCoordinates.Count; i++)
        {
            var port = facility.OutputCoordinates[i];
            if (!IsPipeOutput(facility, port) || !SearchPort(facility, port, false, out int fluidId, out _)) continue;
            route.PortFluids[port] = fluidId;
            foreach (var source in fluidSearch.DataSources.Keys)
            {
                if (ReferenceEquals(source, facility)) continue;
                // Oil peers have a fixed fluid identity. Do not retain every peer in every
                // drill's route when thousands of drills share the same network.
                if (source is ProductionFacilityInstance peer && peer.Template.IsOilDrill
                    && peer.OutputItemId == facility.OutputItemId) continue;
                route.Sources.Add(new FluidSource(port, source, null));
                if (source is ProductionFacilityInstance variableSource) AddDataOutputWaiter(variableSource, facility);
            }
            foreach (var source in fluidSearch.OutputSourcePipeDistances.Keys)
                if (source != null) route.Sources.Add(new FluidSource(port, null, source));
            FluidRouteSearches++;
            CollectStorages(true);
            foreach (var pair in fluidSearch.PipeDistances)
            {
                fluidSearch.RoutePumps.TryGetValue(pair.Key, out var pump);
                if (InputOutputModule.TryGetRuntimePipeFluidStorageAtCoordinate(pair.Key, null, true, out var storage) && storage != null)
                    AddOutputTarget(facility, route, new FluidOutputTarget(storage, null, pair.Key, pair.Value, pump, port));
                for (int j = -1; j < fluidDirections.Length; j++)
                {
                    var endpoint = j < 0 ? pair.Key : pair.Key + fluidDirections[j];
                    if (j >= 0 && PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(pair.Key, out var pipe)
                        && !pipe.HasConnectionTowardsAt(pair.Key, fluidDirections[j])) continue;
                    if (!observers.TryGetValue(endpoint, out var receivers)) continue;
                    for (int k = 0; k < receivers.Count; k++)
                    {
                        var receiver = receivers[k];
                        if (receiver == facility || !IsPipeInput(receiver, endpoint)
                            || !TryPortDirection(receiver, endpoint, out var external) || j >= 0 && external != -fluidDirections[j]) continue;
                        if (j < 0 && PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(endpoint, out var occupying)
                            && !occupying.HasConnectionTowardsAt(endpoint, -external)) continue;
                        AddOutputTarget(facility, route, new FluidOutputTarget(null, receiver, endpoint, pair.Value, pump, port));
                    }
                }
            }
            for (int k = 0; k < fluidStorages.Count; k++)
            {
                var storage = fluidStorages[k];
                if (storage == null) continue;
                foreach (var coordinate in storage.RuntimeOccupiedCoordinates)
                    if (fluidSearch.PipeDistances.TryGetValue(coordinate, out int distance))
                    {
                        fluidSearch.RoutePumps.TryGetValue(coordinate, out var pump);
                        AddOutputTarget(facility, route, new FluidOutputTarget(storage, null, coordinate, distance, pump, port));
                        break;
                    }
            }
        }
        return route;
    }
    private void AddOutputTarget(ProductionFacilityInstance facility, FluidOutputRoute route, FluidOutputTarget target)
    {
        for (int i = 0; i < route.Targets.Count; i++)
        {
            var previous = route.Targets[i];
            // Ordinary tanks are one storage even when exposed by several body cells.
            bool dedicated = target.Storage is InputOutputModule module && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(target.Coordinate);
            if (previous.SourcePort != target.SourcePort) continue;
            if (target.Storage != null && previous.Storage == target.Storage && (!dedicated || previous.Coordinate == target.Coordinate)
                || target.Receiver != null && previous.Receiver == target.Receiver) return;
        }
        route.Targets.Add(target);
        if (target.Storage != null)
        {
            if (!storageOutputWaiters.TryGetValue(target.Storage, out var waiters)) storageOutputWaiters.Add(target.Storage, waiters = new HashSet<ProductionFacilityInstance>());
            waiters.Add(facility);
        }
        else if (target.Receiver != null)
            AddDataOutputWaiter(target.Receiver, facility);
    }
    private void AddDataOutputWaiter(ProductionFacilityInstance dependency, ProductionFacilityInstance facility)
    {
        if (!dataOutputWaiters.TryGetValue(dependency, out var waiters)) dataOutputWaiters.Add(dependency, waiters = new HashSet<ProductionFacilityInstance>());
        waiters.Add(facility);
    }
    private void RemoveDataOutputWaiter(ProductionFacilityInstance dependency, ProductionFacilityInstance facility)
    {
        if (dependency != null && dataOutputWaiters.TryGetValue(dependency, out var waiters))
        { waiters.Remove(facility); if (waiters.Count == 0) dataOutputWaiters.Remove(dependency); }
    }
    private float OutputTargetAvailable(FluidOutputTarget target, int item)
    {
        if (target.Receiver != null) return AvailableInput(target.Receiver, target.Coordinate, item);
        var storage = target.Storage;
        if (storage == null) return 0;
        if (storage is InputOutputModule module && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(target.Coordinate))
            return module.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(target.Coordinate, item);
        return storage.StoredFluidItemId < 0 || storage.StoredFluidItemId == item ? storage.AvailableFluidStorageLiters : 0;
    }
    internal float GetOutputAvailableLiters(ProductionFacilityInstance facility, int item)
    {
        float total = 0;
        var route = GetFluidOutputRoute(facility); var targets = route.Targets;
        for (int i = 0; i < targets.Count; i++)
            if (PortAllows(facility, targets[i].SourcePort, item)
                && (!facility.Template.IsOilDrill || OilRouteAllowsFluid(route, targets[i].SourcePort, item)))
                total += OutputTargetAvailable(targets[i], item);
        return total;
    }
    internal void GetOilOutputState(ProductionFacilityInstance facility, int item, float rate, out float available, out float retention)
    {
        available = retention = 0;
        var route = GetFluidOutputRoute(facility); var targets = route.Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!PortAllows(facility, target.SourcePort, item) || !OilRouteAllowsFluid(route, target.SourcePort, item)) continue;
            float capacity = OutputTargetAvailable(target, item);
            if (capacity <= 0) continue;
            available += capacity;
            float pumpRatio = target.Pump != null && rate > 0 ? Pump.LimitTransportRate(target.Pump, rate) / rate : 1;
            retention = Mathf.Max(retention, target.PressureRetention * pumpRatio);
        }
    }
    internal void TransferOutputFluid(ProductionFacilityInstance facility, ProductionRenderTemplate.Recipe recipe, float requestedLiters = -1)
    {
        var io = facility.Placement.inputOutputState;
        float budget = Mathf.Min(DeterministicSimulationUnits.ToFloat(io.productionOutputFluidUnits),
            requestedLiters >= 0 ? requestedLiters : recipe.OutputRate * SimulationTickWorld.FixedSimulationDeltaSeconds);
        var route = GetFluidOutputRoute(facility);
        // Recipe producers retain their authoritative network identity check. Oil routes
        // validate live endpoint contents below without repeating the graph search each tick.
        if (!facility.Template.IsOilDrill)
            for (int i = 0; i < facility.OutputCoordinates.Count; i++)
            {
                var port = facility.OutputCoordinates[i];
                route.PortFluids[port] = SearchPort(facility, port, false, out int fluid, out _) ? fluid : int.MinValue;
            }
        var targets = route.Targets;
        for (int i = 0; i < targets.Count && budget > 0; i++)
        {
            var target = targets[i];
            if (!PortAllows(facility, target.SourcePort, recipe.OutputId)) continue;
            if (!facility.Template.IsOilDrill && route.PortFluids.TryGetValue(target.SourcePort, out int fluid)
                && fluid != -1 && fluid != recipe.OutputId) continue;
            if (facility.Template.IsOilDrill && !OilRouteAllowsFluid(route, target.SourcePort, recipe.OutputId)) continue;
            float request = Mathf.Min(OutputTargetAvailable(target, recipe.OutputId), RetainedTransferBudget(budget, target.PressureRetention));
            if (target.Pump != null) request = target.Pump.LimitTransferVolume(request, SimulationTickWorld.FixedSimulationDeltaSeconds);
            if (request <= 0) continue;
            float accepted = 0;
            if (target.Receiver != null) Receive(target.Receiver, target.Coordinate, recipe.OutputId, request, out accepted);
            else if (target.Storage is InputOutputModule module && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(target.Coordinate))
                module.TryAddDedicatedFluidAtRuntimeCoordinate(target.Coordinate, recipe.OutputId, request, MapClimate.CurrentTemperatureCelsius, out accepted);
            else if (target.Storage != null) target.Storage.TryAddFluidLiters(recipe.OutputId, request, out accepted);
            DebitOutput(facility, accepted, target.Pump, ref budget);
        }
    }
    private static bool OilRouteAllowsFluid(FluidOutputRoute route, Vector2Int port, int item)
    {
        for (int i = 0; i < route.Sources.Count; i++)
        {
            var source = route.Sources[i];
            if (source.Port != port) continue;
            if (source.Data != null && source.Data.IsRuntimeActive && source.Data.OutputItemId >= 0 && source.Data.OutputItemId != item) return false;
            if (source.Native != null && source.Native.isActiveAndEnabled
                && source.Native.TryGetObjectInfoOutput(out int fluid, out _, out _, out _) && fluid >= 0 && fluid != item) return false;
        }
        var targets = route.Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target.SourcePort != port || target.Storage == null) continue;
            if (target.Storage is InputOutputModule module && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(target.Coordinate)) continue;
            int stored = target.Storage.StoredFluidItemId;
            if (stored >= 0 && stored != item) return false;
        }
        return true;
    }
    internal void WakeFluidOutputStorage(InstallationObject storage)
    {
        if (storage != null && storageOutputWaiters.TryGetValue(storage, out var waiters))
            foreach (var producer in waiters) if (producer.IsRuntimeActive) producer.Wake();
    }
    private void WakeDataFluidProducers(ProductionFacilityInstance receiver)
    {
        if (dataOutputWaiters.TryGetValue(receiver, out var waiters))
            foreach (var producer in waiters) if (producer.IsRuntimeActive) producer.Wake();
    }
    private void RemoveRouteWaiters(ProductionFacilityInstance facility, FluidOutputRoute route)
    {
        for (int i = 0; i < route.Targets.Count; i++)
        {
            var target = route.Targets[i];
            if (target.Storage != null && storageOutputWaiters.TryGetValue(target.Storage, out var storages))
            { storages.Remove(facility); if (storages.Count == 0) storageOutputWaiters.Remove(target.Storage); }
            RemoveDataOutputWaiter(target.Receiver, facility);
        }
        for (int i = 0; i < route.Sources.Count; i++) RemoveDataOutputWaiter(route.Sources[i].Data as ProductionFacilityInstance, facility);
    }
    private void RemoveFluidOutputRoutes(ProductionFacilityInstance facility)
    {
        if (outputRoutes.Remove(facility, out var route)) RemoveRouteWaiters(facility, route);
        dataOutputWaiters.Remove(facility);
    }
}
