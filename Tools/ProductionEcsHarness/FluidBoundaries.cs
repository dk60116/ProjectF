using System;
using System.Collections.Generic;
using UnityEngine;

// Real production fluid transfer runs against a supplied reachable pipe graph.
// Pump traversal/BFS and native storage internals are isolated engine boundaries.
public class InstallationObject
{
    public long Units, Capacity = long.MaxValue;
    public int Fluid = -1, GenericAdds;
    public float StoredFluidLiters => DeterministicSimulationUnits.ToFloat(Units);
    public float AvailableFluidStorageLiters => DeterministicSimulationUnits.ToFloat(Math.Max(0, Capacity - Units));
    public int StoredFluidItemId => Fluid;
    public readonly List<Vector2Int> RuntimeOccupiedCoordinates = new();
    public static readonly Dictionary<Vector2Int, InstallationObject> Bodies = new();
    public static float FluidPressureRetention(int distance) => Mathf.Pow(.9f, Math.Max(0, distance));
    public static void CollectActiveInstallationsAtRuntimeGridCoordinate(Vector2Int coordinate, List<InstallationObject> output)
    { if (Bodies.TryGetValue(coordinate, out var body)) output.Add(body); }
    public bool TryConsumeFluidLiters(int item, float request, out float accepted)
    {
        long units = item == Fluid ? Math.Min(Units, DeterministicSimulationUnits.FromFloat(request)) : 0;
        Units -= units; accepted = DeterministicSimulationUnits.ToFloat(units); return units > 0;
    }
    public bool TryAddFluidLiters(int item, float request, out float accepted)
    {
        GenericAdds++;
        long units = Fluid < 0 || Fluid == item ? Math.Min(Math.Max(0, Capacity - Units), DeterministicSimulationUnits.FromFloat(request)) : 0;
        Units += units; if (units > 0) Fluid = item;
        accepted = DeterministicSimulationUnits.ToFloat(units); return units > 0;
    }
}
public class Fluidtank : InstallationObject { }
public class Pump
{
    public float Rate = 1;
    public double Accepted;
    private long tick = -1;
    private float used;
    public float LimitTransferVolume(float volume, float delta)
    {
        if (tick != MapObjectTickManager.CurrentSimulationTick) { tick = MapObjectTickManager.CurrentSimulationTick; used = 0; }
        return Math.Min(volume, Math.Max(0, Rate * delta - used));
    }
    public void RecordTransferredVolume(float amount) { used += amount; Accepted += amount; }
    public static float LimitTransportRate(Pump pump, float rate) => Math.Min(pump.Rate, rate);
}
public class PipeRecord
{
    public readonly HashSet<Vector2Int> Directions = new() { Vector2Int.left, Vector2Int.right };
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Vector2Int direction) => Directions.Contains(direction);
}
public class PipeWorld
{
    public static PipeWorld Current = new();
    public readonly Dictionary<Vector2Int, PipeRecord> Pipes = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRecord pipe) => Pipes.TryGetValue(coordinate, out pipe);
}
public static class Pipe
{
    public class FluidNetworkSearchContext
    {
        public bool TraverseAsInput;
        public Pump CurrentPump;
        public readonly Dictionary<Vector2Int, int> PipeDistances = new();
        public readonly Dictionary<Vector2Int, Pump> RoutePumps = new();
        public readonly Dictionary<InstallationObject, Pump> StoragePumps = new();
        public readonly Dictionary<IDataFluidProducer, (int Distance, Pump Pump)> DataSources = new();
        public readonly Dictionary<InputOutputModule, int> OutputSourcePipeDistances = new();
        public readonly List<InstallationObject> StorageScratch = new();
    }
    public static readonly Dictionary<Vector2Int, int> Graph = new();
    public static readonly List<IDataFluidProducer> DataPeers = new();
    public static Pump RoutePump;
    public static int FluidId = -1;
    public static float Pressure = 60;
    public static bool SeedOnlyGraph;
    public static bool TryGetNetworkFluidInfoAt(Vector2Int coordinate, FluidNetworkSearchContext context, bool cache,
        Vector2Int excluded, bool pressure, out int fluid, out float temperature, out float rate)
    {
        context.PipeDistances.Clear(); context.RoutePumps.Clear(); context.StoragePumps.Clear();
        context.DataSources.Clear();
        foreach (var peer in DataPeers) context.DataSources.Add(peer, (0, null));
        if (SeedOnlyGraph)
        {
            if (Graph.TryGetValue(coordinate, out int distance))
            { context.PipeDistances.Add(coordinate, distance); if (RoutePump != null) context.RoutePumps.Add(coordinate, RoutePump); }
        }
        else foreach (var pair in Graph) { context.PipeDistances.Add(pair.Key, pair.Value); if (RoutePump != null) context.RoutePumps.Add(pair.Key, RoutePump); }
        fluid = FluidId; temperature = 15; rate = Pressure; return fluid >= 0;
    }
    public static void RecordPumpDistance(FluidNetworkSearchContext context, Pump pump, int distance) { }
}
public partial class InputOutputModule
{
    public bool isActiveAndEnabled = true;
    public bool TryGetObjectInfoOutput(out int item, out int count, out int capacity, out bool zero)
    { item = 2; count = capacity = 0; zero = true; return true; }
    public static int FluidTopologyVersion = 1;
    public readonly Dictionary<Vector2Int, RectGridBlockType> PortTypes = new();
    public readonly Dictionary<Vector2Int, Vector2Int> ExternalDirections = new();
    public static readonly Dictionary<Vector2Int, InstallationObject> Storages = new();
    public bool Dedicated, RejectDedicated;
    public static int StorageChanges;
    public bool TryGetNearestRectGridObjectDirection(object source, Vector2Int anchor, int turns, Vector2Int port, out Vector2Int inward)
    { bool found = ExternalDirections.TryGetValue(port, out var external); inward = -external; return found; }
    public static bool AllowsPipeAreaInteraction(RectGridBlockType type) => type == RectGridBlockType.PipeInputItem
        || type == RectGridBlockType.PipeOutputItem || type == RectGridBlockType.DoublePipeOutputItem || type == RectGridBlockType.PipeInput || type == RectGridBlockType.DoubleInputItem;
    public static bool IsInputItemBlockType(RectGridBlockType type) => type == RectGridBlockType.InputItem || type == RectGridBlockType.PipeInputItem || type == RectGridBlockType.DoubleInputItem;
    public static bool HasRuntimePumpPipePassTowards(Vector2Int coordinate, Vector2Int direction) => false;
    public static bool HasRuntimePassiveFluidPassTowards(Vector2Int coordinate, Vector2Int direction) => false;
    public static bool TryGetRuntimePipeFluidStorageAtCoordinate(Vector2Int coordinate, object excluded, bool output, out InstallationObject storage) => Storages.TryGetValue(coordinate, out storage);
    public bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int coordinate) => Dedicated;
    public float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(Vector2Int coordinate, int item) => AvailableFluidStorageLiters;
    public bool TryAddDedicatedFluidAtRuntimeCoordinate(Vector2Int coordinate, int item, float volume, float temperature, out float accepted)
    { accepted = 0; return !RejectDedicated && TryAddFluidLiters(item, volume, out accepted); }
    public void WakeDataFluidOutput() { }
    public static void NotifyDataFluidStorageChanged(ProductionFacilityInstance storage)
    {
        storage.FluidStorageStateRevision = ++StorageChanges;
    }
}
public partial class ProductionWorld
{
}
