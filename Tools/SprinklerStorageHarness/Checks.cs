using System;
using System.Collections.Generic;

public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
}
public static class MapClimate { public static float CurrentTemperatureCelsius => 20f; }
public static class Application { public static bool isPlaying = true; }
public static class MapObjectTickManager
{
    public const int DefaultSimulationTicksPerSecond = 60;
    public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
}
public class ProjectTree
{
    public float Water;
    public float Capacity = float.MaxValue;
    private bool enabled = true;
    public bool CanAcceptGrowthWater { get => enabled && Capacity - Water > .0001f; set => enabled = value; }
    public bool TryAddGrowthWater(float amount, out float accepted)
    {
        accepted = CanAcceptGrowthWater ? Math.Min(amount, Capacity - Water) : 0;
        if (accepted <= .0001f) { accepted = 0; return false; }
        Water += accepted;
        return true;
    }
}
public class Block { public ProjectTree Tree = new(); public object Resource => Tree; }
public class TerrainGenerator
{
    public static TerrainGenerator Active = new();
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public static TerrainGenerator ResolveActive() => Active;
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
}

public partial class InstallationObject
{
    private static long nextSimulationId;
    private readonly long simulationId = ++nextSimulationId;
    public bool isActiveAndEnabled = true;
    protected int storedFluidItemId = -1;
    protected long storedFluidUnits;
    protected float storedFluidTemperatureCelsius;
    public float FluidStorageCapacityLiters { get; set; } = 300f;
    private long FluidStorageCapacityUnits => DeterministicSimulationUnits.FromFloat(FluidStorageCapacityLiters);
    public long StoredFluidUnits => Math.Max(0L, storedFluidUnits);
    public float StoredFluidLiters => DeterministicSimulationUnits.ToFloat(StoredFluidUnits);
    public int StoredFluidItemId => StoredFluidUnits > 0L ? storedFluidItemId : -1;
    public float AvailableFluidStorageLiters => Math.Max(0f, FluidStorageCapacityLiters - StoredFluidLiters);
    public bool CanStoreFluid => FluidStorageCapacityLiters > 0f;
    public int Changes;
    public void Fill(float liters, int item = 1) { storedFluidUnits = DeterministicSimulationUnits.FromFloat(liters); storedFluidItemId = item; }
    private void NotifyStoredFluidChanged(int item, float liters) { Changes++; }
    public static int CompareSimulationOrder(InstallationObject left, InstallationObject right)
        => ReferenceEquals(left, right) ? 0 : left.simulationId.CompareTo(right.simulationId);
}
public readonly record struct Vector2Int(int x, int y)
{
    public static Vector2Int zero => new(0, 0);
    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    public static Vector2Int operator -(Vector2Int a) => new(-a.x, -a.y);
}
public struct Quaternion { }
public class Pipe : InstallationObject
{
    public Vector2Int? Remote;
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Quaternion rotation, Vector2Int direction) => true;
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote)
    {
        remote = Remote ?? default;
        return Remote.HasValue;
    }
}
public class Fluidtank : InstallationObject
{
    public HashSet<Vector2Int> BlockedDirections = new();
    public bool HasFluidNetworkConnectionTowards(Vector2Int coordinate, Vector2Int direction) => !BlockedDirections.Contains(direction);
}
public partial class InputOutputModule : InstallationObject
{
    protected readonly List<InstallationObject> cachedConnectedFluidSourceStorages = new();
    public readonly List<InstallationObject> Connections = new();
    public int TopologyVersion { get => fluidTopologyVersion; set => fluidTopologyVersion = value; }
    private int fluidTopologyVersion, cachedConnectedFluidSourceStoragesTopologyVersion = -1;
    private readonly Queue<Vector2Int> connectedFluidSearchQueue = new();
    private readonly HashSet<Vector2Int> connectedFluidSearchVisited = new();
    private readonly HashSet<InstallationObject> connectedFluidStorageCandidates = new();
    private readonly List<Vector2Int> connectedFluidSeedCoordinates = new();
    private static readonly Vector2Int[] FluidCardinalDirections = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    public readonly Dictionary<Vector2Int, InstallationObject> Nodes = new();
    public bool UseGraph;
    public int CacheBuilds;
    protected virtual bool UsesConnectedTankNetworkStorage => false;
    public IReadOnlyList<InstallationObject> Sources => GetConnectedFluidSourceStorages();
    private void CollectRuntimePipeAreaCoordinates(List<Vector2Int> coordinates)
    {
        CacheBuilds++;
        if (UseGraph) { coordinates.Add(Vector2Int.zero); return; }
        Nodes.Clear();
        for (int i = 0; i < Connections.Count; i++)
        {
            var coordinate = new Vector2Int(i * 10, 0);
            coordinates.Add(coordinate);
            Nodes.Add(coordinate, Connections[i]);
        }
    }
    private void EnqueueSteamGeneratorPipePassCoordinatesAt(Vector2Int coordinate) { }
    private void EnqueueFluidStoragePipePassCoordinatesAt(Vector2Int coordinate)
        => EnqueueConnectedFluidSearchCoordinate(coordinate);
    private static bool ContainsCoordinate(List<Vector2Int> list, Vector2Int coordinate) => list.Contains(coordinate);
    private bool TryGetConnectedPipeAtCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion rotation)
    {
        Nodes.TryGetValue(coordinate, out var node);
        pipe = node as Pipe; rotation = default;
        return pipe != null;
    }
    private bool TryResolveConnectedFluidSearchStorageAtCoordinate(Vector2Int coordinate, out InstallationObject storage, out bool storageIsPipeArea)
    {
        Nodes.TryGetValue(coordinate, out storage);
        if (storage is Pipe) storage = null;
        storageIsPipeArea = false;
        return storage != null;
    }
    private static bool CanFluidStorageConnectToDirection(InstallationObject storage, Vector2Int coordinate, Vector2Int direction) => false;
    private bool TryGetRuntimePipeAreaExternalDirection(Vector2Int coordinate, out Vector2Int direction) { direction = default; return false; }
    protected virtual bool ShouldAutoPullFluidFromConnectedStorage() => true;
    protected virtual string ResolveObjectInfoStatus(out bool producing) { producing = false; return ""; }
    private float plannedDeltaTime;
    public virtual void ManagedUpdateTick(float deltaTime)
    {
        plannedDeltaTime = deltaTime;
        ApplyManagedUpdateTick();
    }
    public virtual void ApplyManagedUpdateTick() { }
    protected bool TryBeginPlannedModuleApply(out float deltaTime)
    {
        deltaTime = plannedDeltaTime;
        return true;
    }
    protected void ApplyPlannedBaseModuleTick(float deltaTime) { }
}

public partial class ItemInfoDescription
{
    private static string FormatGaugeNumber(float value, bool decimalPlace) => value.ToString(decimalPlace ? "0.0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
    public static string Display(InstallationObject storage, float amount, float capacity) => FormatFluidStorageText(storage, amount, capacity);
}
public partial class Sprinkler : InputOutputModule
{
    private const float WaterEpsilon = .0001f;
    private bool isOperating;
    private int currentWateringTargetCount = 1;
    public float WaterLitersPerSpray = 30;
    public float SprayIntervalSeconds = 2;
    public int Targets
    {
        set
        {
            foreach (var block in TerrainGenerator.Active.Blocks.Values)
                if (block.Tree != null) block.Tree.CanAcceptGrowthWater = value > 0;
        }
    }
    public bool Placed = true;
    public bool Operating => isOperating;
    public int TargetCount => currentWateringTargetCount;
    private readonly HashSet<ProjectTree> wateringTargets = new();
    private readonly List<Vector2Int> sprayCoordinates = new() { new(0, 0), new(1, 0), new(2, 0), new(3, 0) };
    private void EnsureSprayCoordinates() { }
    private void SetOperating(bool operating) { isOperating = operating; }
    private void SetStoredFluid(int item, float amount) => Fill(amount, item);
    private void PullWaterItemsIntoStorage(int item) { }
    private bool TryGetPlacementRuntime(out int coordinate, out int rotation) { coordinate = rotation = 0; return Placed; }
    public static int WaterId = 1;
    private static int ResolveWaterItemId() => WaterId;
    public bool Spray(float liters) => TryConsumeSprayWater(ResolveWaterItemId(), liters);
    public bool AutoPull => ShouldAutoPullFluidFromConnectedStorage();
    public string Status => ResolveObjectInfoStatus(out _);
}

public static class Checks
{
    private static int passed;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        Console.WriteLine("PASS " + label);
        passed++;
    }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .001f;
    private static Fluidtank Tank(float liters, int item = 1, float capacity = 1000)
    {
        var tank = new Fluidtank { FluidStorageCapacityLiters = capacity };
        tank.Fill(liters, item);
        return tank;
    }
    public static void Main()
    {
        var s = new Sprinkler();
        var tank = Tank(60, capacity: 100000);
        s.Connections.Add(tank);
        s.GetWaterStorageInfo(out float stored, out float capacity);
        Check(Near(stored, 60) && Near(capacity, 100300), "UI includes tank water and capacity");
        Check(s.Status == "Ready", "status uses tank water with empty local storage");
        Check(!s.AutoPull, "water is not reserved in the local reservoir by automatic equalization");
        Check(s.Spray(30) && Near(tank.StoredFluidLiters, 30) && Near(s.StoredFluidLiters, 0), "low fill ratio tank supplies a whole spray directly");
        Check(tank.Changes == 1, "withdrawal notifies the actual storage owner");
        s.GetWaterStorageInfo(out stored, out _);
        Check(Near(stored, 30) && s.CacheBuilds == 1, "amounts stay live without rebuilding topology");

        var second = new Sprinkler();
        second.Connections.Add(tank);
        Check(second.Spray(30) && !s.Spray(30) && s.Status == "No water", "two sprinklers cannot double spend shared water");

        var a = Tank(8);
        var b = Tank(12);
        s.Connections.Clear(); s.Connections.Add(a); s.Connections.Add(b); s.TopologyVersion++;
        s.Fill(10);
        Check(s.Spray(30) && Near(a.StoredFluidLiters + b.StoredFluidLiters + s.StoredFluidLiters, 0), "combine local water and multiple tanks exactly once");
        a.Fill(8); b.Fill(12);
        Check(!s.Spray(30) && Near(a.StoredFluidLiters, 8) && Near(b.StoredFluidLiters, 12), "insufficient shared water consumes nothing");
        Check(s.Status == "Ready", "partial supply can water continuously without a full spray reserve");

        b.Fill(100, 2);
        s.GetWaterStorageInfo(out stored, out capacity);
        Check(Near(stored, 8) && Near(capacity, 1300) && !s.Spray(30), "other fluids are excluded from water and usable capacity");
        b.Fill(100); b.isActiveAndEnabled = false;
        Check(!s.Spray(30) && Near(b.StoredFluidLiters, 100), "disabled tanks cannot supply water");
        b.isActiveAndEnabled = true;
        s.Connections.Remove(b); s.TopologyVersion++;
        s.GetWaterStorageInfo(out stored, out capacity);
        Check(Near(stored, 8) && Near(capacity, 1300), "disconnect removes tank water and capacity on cache invalidation");
        s.Connections.Add(b); s.TopologyVersion++;
        Check(s.Spray(30) && Near(a.StoredFluidLiters, 0) && Near(b.StoredFluidLiters, 78), "reconnection immediately restores access");

        var otherMachine = new InstallationObject(); otherMachine.Fill(200);
        s.Connections.Clear(); s.Connections.Add(otherMachine); s.TopologyVersion++;
        Check(!s.Spray(30) && Near(otherMachine.StoredFluidLiters, 200), "other machines are not shared tanks");
        s.Fill(40);
        Check(s.Spray(30) && Near(s.StoredFluidLiters, 10), "standalone and manual input water remain usable");
        s.Fill(0); s.FluidStorageCapacityLiters = 0;
        s.Connections.Clear(); s.Connections.Add(b); s.TopologyVersion++;
        Check(s.Spray(30) && Near(b.StoredFluidLiters, 48), "tank supplies a sprinkler with zero local capacity");
        Sprinkler.WaterId = -1;
        Check(!s.Spray(30) && Near(b.StoredFluidLiters, 48), "missing water definition never consumes arbitrary fluid");
        Sprinkler.WaterId = 1;

        var network = new Sprinkler { UseGraph = true };
        network.Nodes[new(0, 0)] = new Pipe();
        var first = Tank(10, capacity: 100);
        var last = Tank(90, capacity: 100);
        network.Nodes[new(1, 0)] = first;
        network.Nodes[new(2, 0)] = new Pipe();
        network.Nodes[new(3, 0)] = last;
        network.GetWaterStorageInfo(out stored, out capacity);
        Check(Near(stored, 100) && Near(capacity, 500), "traverse the first tank and intervening pipe to the entire tank bank");
        Check(ItemInfoDescription.Display(network, stored, capacity) == "100L / 300L (+200L)", "display separates internal capacity and total connected tank capacity");
        Check(network.Spray(60) && Near(last.StoredFluidLiters, 40), "consume from a tank behind another tank");
        network.Nodes[new(0, 1)] = new Pipe(); network.Nodes[new(1, 1)] = new Pipe();
        network.TopologyVersion++;
        network.GetWaterStorageInfo(out stored, out capacity);
        Check(Near(stored, 40) && Near(capacity, 500), "looped paths count each tank only once, including empty tanks");
        network.Nodes.Remove(new(2, 0)); network.TopologyVersion++;
        network.GetWaterStorageInfo(out stored, out capacity);
        Check(Near(stored, 0) && Near(capacity, 400), "removing an intermediate pipe excludes all downstream tanks");
        network.Nodes[new(2, 0)] = new Pipe(); network.TopologyVersion++;
        first.BlockedDirections.Add(new(1, 0));
        network.GetWaterStorageInfo(out stored, out capacity);
        Check(Near(capacity, 400), "tank traversal respects blocked connector directions");
        first.BlockedDirections.Clear(); network.TopologyVersion++;
        network.Nodes[new(2, 0)] = new Pipe { Remote = new(10, 0) };
        network.Nodes.Remove(new(3, 0));
        network.Nodes[new(10, 0)] = new Pipe(); network.Nodes[new(11, 0)] = last;
        network.GetWaterStorageInfo(out stored, out capacity);
        Check(Near(stored, 40) && Near(capacity, 500), "underground pipe endpoint after a tank reaches the remote tank");
        var ordinaryModule = new InputOutputModule { UseGraph = true };
        foreach (var node in network.Nodes) ordinaryModule.Nodes.Add(node.Key, node.Value);
        Check(ordinaryModule.Sources.Count == 1, "ordinary modules keep their existing transfer boundary");
        Check(ItemInfoDescription.Display(new Fluidtank(), 10, 300) == "10.0 / 300.0 L", "ordinary tank formatting remains unchanged");

        TerrainGenerator.Active = new();
        for (int i = 0; i < 4; i++) TerrainGenerator.Active.Blocks.Add(new(i, 0), new Block());
        var continuous = new Sprinkler();
        var supply = Tank(100);
        continuous.Connections.Add(supply);
        continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 96.25f), "first update consumes elapsed-time water immediately");
        Check(Near(TerrainGenerator.Active.Blocks[new(0, 0)].Tree.Water, .9375f), "plants receive their share of the water consumed in the same update");
        for (int i = 1; i < 8; i++) continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 70), "two seconds use exactly the original per-spray amount");
        Check(Near(TerrainGenerator.Active.Blocks[new(0, 0)].Tree.Water * 4, 30), "plant watering matches total water consumption");
        continuous.Targets = 0;
        continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 66.25f) && continuous.Operating, "whole-range watering continues when no plants need water");
        continuous.Targets = 1;
        continuous.Placed = false;
        continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 66.25f), "unplaced sprinkler consumes no water");
        continuous.Placed = true;
        continuous.ManagedUpdateTick(0);
        Check(Near(supply.StoredFluidLiters, 66.25f), "zero elapsed time consumes no water");
        supply.Fill(1);
        continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 0) && Near(TerrainGenerator.Active.Blocks[new(0, 0)].Tree.Water * 4, 31), "last partial update waters proportionally without discarding remaining supply");
        continuous.ManagedUpdateTick(.25f);
        Check(!continuous.Operating, "empty supply stops operation");
        supply.Fill(100);
        continuous.ManagedUpdateTick(4);
        Check(Near(supply.StoredFluidLiters, 40), "long elapsed update preserves the same consumption rate");
        supply.Fill(100);
        for (int i = 0; i < 40; i++) continuous.ManagedUpdateTick(.05f);
        Check(Near(supply.StoredFluidLiters, 70), "consumption does not depend on tick subdivision");
        TerrainGenerator.Active = null;
        continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 70) && !continuous.Operating, "missing terrain cannot consume water without applying it");

        TerrainGenerator.Active = new();
        var inside = new ProjectTree();
        var outside = new ProjectTree();
        TerrainGenerator.Active.Blocks[new(0, 0)] = new Block { Tree = inside };
        TerrainGenerator.Active.Blocks[new(1, 0)] = new Block { Tree = null };
        TerrainGenerator.Active.Blocks[new(2, 0)] = new Block { Tree = null };
        TerrainGenerator.Active.Blocks[new(3, 0)] = new Block { Tree = null };
        TerrainGenerator.Active.Blocks[new(4, 0)] = new Block { Tree = outside };
        supply.Fill(100);
        continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 96.25f) && Near(inside.Water, .9375f), "a plant receives only the water assigned to its own cell");
        Check(Near(outside.Water, 0) && continuous.TargetCount == 1, "adjacent plant outside the range receives no water and is not counted");
        TerrainGenerator.Active.Blocks[new(0, 0)].Tree = null;
        continuous.ManagedUpdateTick(.25f);
        Check(Near(supply.StoredFluidLiters, 92.5f) && continuous.Operating && continuous.TargetCount == 0, "completely empty range still receives and consumes the full area share");
        TerrainGenerator.Active.Blocks[new(0, 0)].Tree = inside;
        var secondInside = new ProjectTree();
        TerrainGenerator.Active.Blocks[new(2, 0)].Tree = secondInside;
        float beforeFirst = inside.Water;
        continuous.ManagedUpdateTick(.25f);
        Check(Near(inside.Water - beforeFirst, .9375f) && Near(secondInside.Water, .9375f), "each occupied cell receives one fixed coordinate share");
        inside.Capacity = inside.Water + .2f;
        float beforeSecond = secondInside.Water;
        continuous.ManagedUpdateTick(.25f);
        Check(Near(inside.Water, inside.Capacity) && Near(secondInside.Water - beforeSecond, .9375f), "water rejected by a nearly full plant is not pulled into another plant's cell");
        TerrainGenerator.Active.Blocks[new(1, 0)].Tree = secondInside;
        beforeSecond = secondInside.Water;
        continuous.ManagedUpdateTick(.25f);
        Check(continuous.TargetCount == 1 && Near(secondInside.Water - beforeSecond, .9375f), "a plant referenced by multiple cells is counted and supplied only once");
        secondInside.Capacity = secondInside.Water + .1f;
        float beforeSupply = supply.StoredFluidLiters;
        continuous.ManagedUpdateTick(.25f);
        Check(Near(secondInside.Water, secondInside.Capacity) && Near(beforeSupply - supply.StoredFluidLiters, 3.75f), "saturated cells keep the configured whole-range spray consumption without redistribution");
        Console.WriteLine($"{passed} sprinkler storage checks passed.");
    }
}
