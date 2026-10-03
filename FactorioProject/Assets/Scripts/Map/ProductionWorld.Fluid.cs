using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;

public sealed partial class ProductionWorld
{
    // One reusable search workspace for the main-thread fluid boundary, not one BFS per entity.
    private readonly Pipe.FluidNetworkSearchContext fluidSearch = new Pipe.FluidNetworkSearchContext();
    private readonly HashSet<InstallationObject> visitedStorages = new HashSet<InstallationObject>();
    private readonly List<InstallationObject> fluidStorages = new List<InstallationObject>();
    private readonly Dictionary<ProductionFacilityInstance, List<InputOutputModule>> nativeProducers = new Dictionary<ProductionFacilityInstance, List<InputOutputModule>>();
    private static readonly Vector2Int[] fluidDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    internal bool TryPortDirection(ProductionFacilityInstance facility, Vector2Int coordinate, out Vector2Int direction)
    {
        direction = default;
        if (!facility.Prototype.TryGetNearestRectGridObjectDirection(facility.Prototype, facility.AnchorCoordinate,
            facility.Placement.quarterTurns, coordinate, out var inward)) return false;
        direction = -inward; return direction != Vector2Int.zero;
    }
    internal bool TryGetFluidReceiver(Vector2Int coordinate, Vector2Int direction, out ProductionFacilityInstance receiver)
    {
        receiver = null;
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
            if (IsPipeInput(owners[i], coordinate) && (direction == Vector2Int.zero
                || TryPortDirection(owners[i], coordinate, out var external) && external == direction))
            { receiver = owners[i]; return true; }
        return false;
    }
    internal void RegisterNativeProducer(ProductionFacilityInstance receiver, InputOutputModule source)
    {
        if (!nativeProducers.TryGetValue(receiver, out var sources)) nativeProducers.Add(receiver, sources = new List<InputOutputModule>(2));
        if (!sources.Contains(source)) sources.Add(source);
    }
    internal void WakeNativeProducers(ProductionFacilityInstance receiver)
    {
        InputOutputModule.NotifyDataFluidStorageChanged();
        if (nativeProducers.TryGetValue(receiver, out var sources))
            for (int i = sources.Count - 1; i >= 0; i--)
                if (sources[i] != null) sources[i].WakeDataFluidOutput(); else sources.RemoveAt(i);
    }
    internal float AvailableInput(ProductionFacilityInstance receiver, Vector2Int port, int item)
    {
        if (!receiver.IsRuntimeActive || receiver.HasActiveWork || !PortAllows(receiver, port, item)) return 0;
        var recipe = receiver.SelectedRecipe;
        if (recipe == null) return 0;
        for (int i = 0; i < recipe.Inputs.Count; i++) if (recipe.Inputs[i].itemId == item)
            return DeterministicSimulationUnits.ToFloat(Math.Max(0, receiver.RequiredFluidUnits(recipe, recipe.Inputs[i].amount) - receiver.FluidUnits(item)));
        return 0;
    }
    internal bool Receive(ProductionFacilityInstance receiver, Vector2Int port, int item, float requested, out float accepted)
    {
        accepted = Math.Min(Math.Max(0, requested), AvailableInput(receiver, port, item));
        long units = DeterministicSimulationUnits.FromFloat(accepted);
        if (units <= 0) { accepted = 0; return false; }
        accepted = DeterministicSimulationUnits.ToFloat(units);
        AddInput(receiver, item, accepted); receiver.Wake(); return true;
    }
    internal bool TryGetOutputDirection(Vector2Int coordinate, out Vector2Int direction)
    {
        direction = default;
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i].OutputCoordinates.Contains(coordinate) && IsPipeOutput(owners[i], coordinate)
                && TryPortDirection(owners[i], coordinate, out direction)) return true;
        return false;
    }
    private static bool IsPipeOutput(ProductionFacilityInstance facility, Vector2Int coordinate) =>
        facility.Prototype.TryGetRectGridBlockTypeAtCoordinate(facility.Prototype, facility.AnchorCoordinate,
            facility.Placement.quarterTurns, coordinate, out var type)
        && (type == InputOutputModule.RectGridBlockType.PipeOutputItem || type == InputOutputModule.RectGridBlockType.DoublePipeOutputItem);
    internal static bool IsPipeInput(ProductionFacilityInstance facility, Vector2Int coordinate) =>
        facility.Prototype.TryGetRectGridBlockTypeAtCoordinate(facility.Prototype, facility.AnchorCoordinate,
            facility.Placement.quarterTurns, coordinate, out var type) && InputOutputModule.AllowsPipeAreaInteraction(type)
        && (InputOutputModule.IsInputItemBlockType(type) || type == InputOutputModule.RectGridBlockType.PipeInput);
    internal bool TryOutputFluidInfo(Vector2Int coordinate, out int fluidId)
    {
        fluidId = -1;
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
        {
            var facility = owners[i];
            if (facility.OutputCoordinates.Contains(coordinate) && IsPipeOutput(facility, coordinate)
                && InputOutputModule.ResolveItemDefinition(facility.OutputItemId)?.isFluid == true)
            { fluidId = facility.OutputItemId; return true; }
        }
        return false;
    }
    internal void AppendFluidSources(Vector2Int coordinate, Vector2Int direction, int distance, Pipe.FluidNetworkSearchContext context)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) return;
        for (int i = 0; i < owners.Count; i++)
        {
            var facility = owners[i];
            if (!facility.OutputCoordinates.Contains(coordinate) || !IsPipeOutput(facility, coordinate)
                || direction != Vector2Int.zero && (!TryPortDirection(facility, coordinate, out var external) || external != direction)) continue;
            if (!context.DataSources.TryGetValue(facility, out var previous) || previous.Distance > distance)
            {
                context.DataSources[facility] = (distance, context.CurrentPump);
                Pipe.RecordPumpDistance(context, context.CurrentPump, distance);
            }
        }
    }
    private bool SearchPort(ProductionFacilityInstance facility, Vector2Int port, bool intake,
        out int fluidId, out float pressure)
    {
        fluidId = -1; pressure = 0;
        if (!TryPortDirection(facility, port, out var external)) return false;
        // Prefer a pipe occupying the authored port; otherwise use the one outside it.
        Vector2Int seed = port;
        bool atPort = PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(port, out _)
            || InputOutputModule.HasRuntimePumpPipePassTowards(port, -external)
            || InputOutputModule.HasRuntimePassiveFluidPassTowards(port, -external);
        if (!atPort) seed += external;
        bool connects = PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(seed, out var pipe)
            && pipe.HasConnectionTowardsAt(seed, -external)
            || InputOutputModule.HasRuntimePumpPipePassTowards(seed, -external)
            || InputOutputModule.HasRuntimePassiveFluidPassTowards(seed, -external)
            || InputOutputModule.TryGetRuntimePipeFluidStorageAtCoordinate(seed, null, false, out _);
        if (!connects) return false;
        fluidSearch.TraverseAsInput = intake;
        Pipe.TryGetNetworkFluidInfoAt(seed, fluidSearch, false, default, true, out fluidId, out _, out pressure);
        return true; // Empty output networks still carry reachable storage endpoints.
    }
    internal void TransferInputFluids(ProductionFacilityInstance facility, ProductionRenderTemplate.Recipe recipe)
    {
        var io = facility.Placement.inputOutputState;
        for (int i = 0; i < recipe.Inputs.Count; i++)
        {
            var ingredient = recipe.Inputs[i];
            if (InputOutputModule.ResolveItemDefinition(ingredient.itemId)?.isFluid != true) continue;
            long missing = facility.RequiredFluidUnits(recipe, ingredient.amount) - facility.FluidUnits(ingredient.itemId);
            if (missing <= 0) continue;
            for (int j = 0; j < io.gridCoordinates.Count && missing > 0; j++)
            {
                var port = io.gridCoordinates[j];
                if (!IsPipeInput(facility, port) || !PortAllows(facility, port, ingredient.itemId) || !SearchPort(facility, port, true, out int item, out float pressure)
                    || item != ingredient.itemId || pressure <= 0) continue;
                float budget = Mathf.Min(DeterministicSimulationUnits.ToFloat(missing), pressure * SimulationTickWorld.FixedSimulationDeltaSeconds);
                CollectStorages(false);
                foreach (var coordinate in fluidSearch.PipeDistances)
                {
                    if (budget <= 0) break;
                    if (!InputOutputModule.TryGetRuntimePipeFluidStorageAtCoordinate(coordinate.Key, null, false, out var storage)
                        || storage == null || !visitedStorages.Add(storage) || storage.StoredFluidItemId != item) continue;
                    fluidSearch.RoutePumps.TryGetValue(coordinate.Key, out var pump);
                    float request = pump != null ? pump.LimitTransferVolume(budget, SimulationTickWorld.FixedSimulationDeltaSeconds) : budget;
                    if (!storage.TryConsumeFluidLiters(item, request, out float accepted) || accepted <= 0) continue;
                    AddInput(facility, item, accepted); missing -= DeterministicSimulationUnits.FromFloat(accepted); budget -= accepted;
                    if (pump != null) pump.RecordTransferredVolume(accepted);
                }
                // Fixed tanks are also discoverable through their body coordinates.
                for (int k = 0; k < fluidStorages.Count && budget > 0; k++)
                {
                    var storage = fluidStorages[k];
                    if (storage == null || !visitedStorages.Add(storage) || storage.StoredFluidItemId != item) continue;
                    fluidSearch.StoragePumps.TryGetValue(storage, out var pump);
                    float request = pump != null ? pump.LimitTransferVolume(budget, SimulationTickWorld.FixedSimulationDeltaSeconds) : budget;
                    if (!storage.TryConsumeFluidLiters(item, request, out float accepted) || accepted <= 0) continue;
                    AddInput(facility, item, accepted); missing -= DeterministicSimulationUnits.FromFloat(accepted); budget -= accepted;
                    if (pump != null) pump.RecordTransferredVolume(accepted);
                }
            }
        }
    }
    private static void AddInput(ProductionFacilityInstance facility, int item, float liters)
    {
        var io = facility.Placement.inputOutputState;
        int index = io.productionInputFluidItemIds.IndexOf(item);
        if (index < 0) { index = io.productionInputFluidItemIds.Count; io.productionInputFluidItemIds.Add(item); io.productionInputFluidUnits.Add(0); }
        long units = DeterministicSimulationUnits.FromFloat(liters);
        if (units > 0) { io.productionInputFluidUnits[index] += units; InputOutputModule.NotifyDataFluidStorageChanged(); }
    }
    private void CollectStorages(bool output)
    {
        visitedStorages.Clear(); fluidStorages.Clear();
        foreach (var coordinate in fluidSearch.PipeDistances)
        {
            if (InputOutputModule.TryGetRuntimePipeFluidStorageAtCoordinate(coordinate.Key, null, output, out var area)) fluidStorages.Add(area);
            fluidSearch.StorageScratch.Clear();
            InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(coordinate.Key, fluidSearch.StorageScratch);
            for (int i = 0; i < fluidSearch.StorageScratch.Count; i++)
                if (fluidSearch.StorageScratch[i] is Fluidtank) fluidStorages.Add(fluidSearch.StorageScratch[i]);
        }
        fluidSearch.StorageScratch.Clear();
    }
    private static bool PortAllows(ProductionFacilityInstance facility, Vector2Int port, int item) =>
        !facility.Prototype.TryGetRectGridBlockPlacementAtCoordinate(facility.Prototype, facility.AnchorCoordinate,
            facility.Placement.quarterTurns, port, out var placement) || placement.itemDefinition == null || placement.itemDefinition.id == item;
    internal void TransferOutputFluid(ProductionFacilityInstance facility, ProductionRenderTemplate.Recipe recipe)
    {
        var io = facility.Placement.inputOutputState;
        float budget = Mathf.Min(DeterministicSimulationUnits.ToFloat(io.productionOutputFluidUnits), recipe.OutputRate * SimulationTickWorld.FixedSimulationDeltaSeconds);
        for (int i = 0; i < facility.OutputCoordinates.Count && budget > 0; i++)
        {
            var port = facility.OutputCoordinates[i];
            if (!IsPipeOutput(facility, port) || !PortAllows(facility, port, recipe.OutputId)
                || !SearchPort(facility, port, false, out int networkFluid, out _)
                || networkFluid >= 0 && networkFluid != recipe.OutputId) continue;
            CollectStorages(true);
            foreach (var coordinate in fluidSearch.PipeDistances)
            {
                if (budget <= 0) break;
                if (InputOutputModule.TryGetRuntimePipeFluidStorageAtCoordinate(coordinate.Key, null, true, out var storage)
                    && storage != null && visitedStorages.Add(storage))
                {
                    fluidSearch.RoutePumps.TryGetValue(coordinate.Key, out var pump);
                    float request = RetainedTransferBudget(budget, coordinate.Value);
                    if (pump != null) request = pump.LimitTransferVolume(request, SimulationTickWorld.FixedSimulationDeltaSeconds);
                    float acceptedDedicated = 0;
                    bool dedicated = storage is InputOutputModule module && module.UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate.Key);
                    float accepted = 0;
                    if (dedicated) ((InputOutputModule)storage).TryAddDedicatedFluidAtRuntimeCoordinate(coordinate.Key, recipe.OutputId, request, MapClimate.CurrentTemperatureCelsius, out acceptedDedicated);
                    else storage.TryAddFluidLiters(recipe.OutputId, request, out accepted);
                    if (dedicated) accepted = acceptedDedicated;
                    DebitOutput(facility, accepted, pump, ref budget);
                }
                // Reach authored data input ports without crossing their facility bodies.
                for (int j = -1; j < fluidDirections.Length && budget > 0; j++)
                {
                    var endpoint = j < 0 ? coordinate.Key : coordinate.Key + fluidDirections[j];
                    if (j >= 0 && PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(coordinate.Key, out var pipe)
                        && !pipe.HasConnectionTowardsAt(coordinate.Key, fluidDirections[j])) continue;
                    if (!observers.TryGetValue(endpoint, out var receivers)) continue;
                    for (int k = 0; k < receivers.Count && budget > 0; k++)
                    {
                        var receiver = receivers[k];
                        if (receiver == facility || receiver.HasActiveWork || !IsPipeInput(receiver, endpoint)
                            || !TryPortDirection(receiver, endpoint, out var direction) || j >= 0 && direction != -fluidDirections[j]) continue;
                        if (j < 0 && PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(endpoint, out var occupyingPipe)
                            && !occupyingPipe.HasConnectionTowardsAt(endpoint, -direction)) continue;
                        var targetRecipe = receiver.SelectedRecipe;
                        if (targetRecipe == null) continue;
                        for (int n = 0; n < targetRecipe.Inputs.Count; n++)
                        {
                            var ingredient = targetRecipe.Inputs[n];
                            if (ingredient.itemId != recipe.OutputId || !PortAllows(receiver, endpoint, recipe.OutputId)) continue;
                            long missing = receiver.RequiredFluidUnits(targetRecipe, ingredient.amount) - receiver.FluidUnits(recipe.OutputId);
                            float accepted = Mathf.Min(RetainedTransferBudget(budget, coordinate.Value), DeterministicSimulationUnits.ToFloat(Math.Max(0, missing)));
                            fluidSearch.RoutePumps.TryGetValue(coordinate.Key, out var pump);
                            if (pump != null) accepted = pump.LimitTransferVolume(accepted, SimulationTickWorld.FixedSimulationDeltaSeconds);
                            AddInput(receiver, recipe.OutputId, accepted); DebitOutput(facility, accepted, pump, ref budget); receiver.Wake();
                        }
                    }
                }
            }
            for (int k = 0; k < fluidStorages.Count && budget > 0; k++)
            {
                var storage = fluidStorages[k];
                if (storage == null || !visitedStorages.Add(storage)) continue;
                Pump pump = null; int distance = 0;
                foreach (var coordinate in storage.RuntimeOccupiedCoordinates)
                    if (fluidSearch.PipeDistances.TryGetValue(coordinate, out distance))
                    { fluidSearch.RoutePumps.TryGetValue(coordinate, out pump); break; }
                float request = RetainedTransferBudget(budget, distance);
                if (pump != null) request = pump.LimitTransferVolume(request, SimulationTickWorld.FixedSimulationDeltaSeconds);
                storage.TryAddFluidLiters(recipe.OutputId, request, out float accepted);
                DebitOutput(facility, accepted, pump, ref budget);
            }
        }
    }
    private static float RetainedTransferBudget(float budget, int distance)
    {
        long available = DeterministicSimulationUnits.FromFloat(budget);
        if (available <= 0) return 0;
        // Retention reduces the rate, but cannot trap a final indivisible simulation unit forever.
        long retained = Math.Max(1, DeterministicSimulationUnits.FromFloat(budget * InstallationObject.FluidPressureRetention(distance)));
        return DeterministicSimulationUnits.ToFloat(Math.Min(available, retained));
    }
    private static void DebitOutput(ProductionFacilityInstance facility, float accepted, Pump pump, ref float budget)
    {
        if (accepted <= 0) return;
        var io = facility.Placement.inputOutputState;
        io.productionOutputFluidUnits = Math.Max(0, io.productionOutputFluidUnits - DeterministicSimulationUnits.FromFloat(accepted));
        budget -= accepted;
        if (pump != null) pump.RecordTransferredVolume(accepted);
    }
}
