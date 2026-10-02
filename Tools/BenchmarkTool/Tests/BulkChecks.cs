using System.Collections;
using ProjectF.Benchmark;
using UnityEngine;

// Engine and scene storage boundaries. Terrain bulk operations and the saved
// installation selection/removal iterator are compiled from production source.
namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int zero => default;
        public static Vector2Int one => new(1, 1);
        public static Vector2Int Min(Vector2Int a, Vector2Int b) => new(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2Int Max(Vector2Int a, Vector2Int b) => new(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
    }
    public readonly record struct Vector3(float x, float y, float z)
    { public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z); }
    public readonly record struct Quaternion(int Turns);
    public class GameObject { public bool Active = true; public bool activeInHierarchy => Active; public void SetActive(bool value) => Active = value; }
    public class Transform
    {
        public Vector3 position, localScale = new(1, 1, 1);
        public Quaternion rotation;
        public void SetPositionAndRotation(Vector3 pos, Quaternion rot) { position = pos; rotation = rot; }
    }
    public static class Mathf
    { public static int Max(int a, int b) => Math.Max(a, b); public static int FloorToInt(float a) => (int)Math.Floor(a); }
}
public static class MapObjectTickManager
{
    public static bool SimulationPaused;
    public static void SetSimulationPaused(bool value) => SimulationPaused = value;
}
public static class UtilityPole
{
    public static int Depth;
    public static void BeginTopologyRefreshBatch() => Depth++;
    public static void EndTopologyRefreshBatch(bool rebuildDirtyTopology) => Depth--;
}
public class InstallationObject
{
    public readonly GameObject gameObject = new();
    public readonly Transform transform = new();
    public bool ExcludeFromTerrainPersistence, Placed;
    public int ItemId, Turns;
    public Vector2Int Anchor;
    public Vector2Int[] Footprint = { default }, Blocking = { default };
    public List<Vector2Int> Occupied = new();
    private static long next;
    public static readonly List<InstallationObject> Live = new();
    public static long ClaimNextPlacementSequence() => ++next;
    public static void CopyActiveInstances(List<InstallationObject> output) => output.AddRange(Live);
    public bool TryGetPlacementRuntime(out Vector2Int anchor, out int turns)
    { anchor = Anchor; turns = Turns; return Placed; }
}
public class ConveyorBelt : InstallationObject { public bool MultipleCells; }
public class Pipe : InstallationObject { }
public class Building : InstallationObject { }
public class RobotArm : InstallationObject { }
public class Train : InstallationObject { }
public class Railload : InstallationObject { }
public partial class InputOutputModule
{
    public readonly GameObject gameObject = new();
    public static readonly HashSet<InputOutputModule> activeRuntimeModules = new();
    private static readonly List<InputOutputModule> runtimeWakeScratch = new();
    public static event Action<InputOutputModule> RuntimePipeTopologyChanged;
    public static int TopologyQueries, GlobalWakes, Invalidations, LocalNotifications;
    public static readonly HashSet<Vector2Int> LocalWakes = new();
    private bool HasRuntimePipeTopologyCoordinates() { TopologyQueries++; return true; }
    private void WakeRuntimeUpdate() => GlobalWakes++;
    private static void InvalidateFluidTopologyCache() => Invalidations++;
    private static void WakeRuntimeModulesAtCoordinates(IReadOnlyList<Vector2Int> coordinates)
    { LocalNotifications++; if (coordinates != null) foreach (var coordinate in coordinates) LocalWakes.Add(coordinate); }
    public static void SimulatePlacementWake() => WakeRuntimeFluidTopologyModules();
    public static void SimulateGridChange(InputOutputModule module) => NotifyRuntimePipeTopologyObservers(module);
    public struct ItemArea { public Vector2Int coordinate; }
    public class PersistentState
    {
        public List<Vector2Int> inputEnergyCoordinates = new(), outputCoordinates = new(), pipeInputCoordinates = new(), gridCoordinates = new(), focusCoordinates = new();
        public List<ItemArea> inputItemAreas = new();
        public PersistentState Clone() => new() { inputEnergyCoordinates = new(inputEnergyCoordinates), outputCoordinates = new(outputCoordinates),
            pipeInputCoordinates = new(pipeInputCoordinates), gridCoordinates = new(gridCoordinates), focusCoordinates = new(focusCoordinates), inputItemAreas = new(inputItemAreas) };
    }
}
namespace ProjectF.Benchmark
{
    public static class BenchmarkRuntime
    {
        public static bool ForceWorking;
        public static int Wakes;
        public static void Wake(InstallationObject obj) => Wakes++;
    }
}
public class ItemDefinition { public int id; public InstallationObject mapObject; }
public class ItemManager { public readonly List<ItemDefinition> ItemDefinitions = new(); }
public class GameManager
{ public static GameManager Instance = new(); public readonly ItemManager ItemManger = new(); public T GetComponent<T>() where T : new() => new(); }
namespace ProjectF.MapObjects
{
    public class StaticMapObjectBatchRenderer
    {
        public long BenchmarkSyncDone => 1;
        public long BenchmarkSyncTotal => 1;
        public IEnumerator PrepareBenchmarkPresentation() { yield return null; }
    }
}
public class InstallationPlacementController
{
    public int FootprintQueries, BlockingQueries, Positions, NormalConfigurations, CachedConfigurations;
    public bool RejectConfiguration;
    public Vector3 GetInstalledObjectWorldPosition(Vector2Int coordinate, InstallationObject source, int turns)
    {
        Positions++;
        return new(coordinate.x + .25f, .2f, coordinate.y + .5f);
    }
    public List<Vector2Int> GetInstalledObjectFootprintCoordinates(Vector2Int anchor, InstallationObject source, int turns)
    { FootprintQueries++; return source.Footprint.Select(c => c + anchor).ToList(); }
    public List<Vector2Int> GetInstalledObjectBlockingCoordinates(Vector2Int anchor, InstallationObject source, int turns)
    { BlockingQueries++; return source.Blocking.Select(c => c + anchor).ToList(); }
    public Quaternion GetInstalledObjectRotation(InstallationObject source, int turns) => new(turns);
    public List<Vector2Int> GetInstalledObjectFocusCoordinates(Vector2Int anchor, ItemDefinition item, int turns) => new();
    public void ConfigureInstalledObjectRuntime(InstallationObject proxy, Vector2Int anchor, int turns)
    { NormalConfigurations++; Configure(proxy, anchor, turns, proxy.Blocking.Select(c => c + anchor).ToList()); }
    public void ConfigureBenchmarkInstalledObjectRuntime(InstallationObject proxy, Vector2Int anchor, int turns, IReadOnlyList<Vector2Int> blocking)
    { CachedConfigurations++; Configure(proxy, anchor, turns, blocking); if (RejectConfiguration) throw new InvalidOperationException("injected config failure"); }
    private void Configure(InstallationObject proxy, Vector2Int anchor, int turns, IReadOnlyList<Vector2Int> blocking)
    { proxy.Anchor = anchor; proxy.Turns = turns; proxy.Placed = true; proxy.Occupied = new(blocking); }
    public bool BindInstalledObjectToFootprintBlocks(InstallationObject installed, Vector2Int coordinate, int turns) => false;
}
public partial class BlockStateStore
{
    public sealed class InstallationSaveState
    {
        public int itemId, conveyorVariantKind = -1, quarterTurns;
        public bool hasStorageKey, hasWorldPose;
        public long placementSequence;
        public Vector2Int anchorCoordinate;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public List<Vector2Int> occupiedCoordinates = new(), utilityPoleConnectedAnchors = new();
        public InputOutputModule.PersistentState inputOutputState;
        public InstallationSaveState Clone()
        {
            var clone = (InstallationSaveState)MemberwiseClone();
            clone.occupiedCoordinates = new(occupiedCoordinates);
            clone.utilityPoleConnectedAnchors = new(utilityPoleConnectedAnchors);
            clone.inputOutputState = inputOutputState?.Clone();
            return clone;
        }
    }
    public readonly Dictionary<Vector2Int, InstallationSaveState> savedInstallationStates = new();
    public int Captures, Registrations, Removes, IndividualPoleScans;
    public bool RejectCapture, RejectRegistration, SlowRemoval;
    public bool TryCaptureInstallationState(InstallationObject proxy, out InstallationSaveState state)
    {
        Captures++;
        if (proxy.gameObject.Active || !proxy.Placed) throw new Exception("template must be configured while inactive");
        state = new() { itemId = proxy.ItemId, quarterTurns = proxy.Turns, conveyorVariantKind = proxy is ConveyorBelt ? 0 : -1,
            anchorCoordinate = proxy.Anchor, occupiedCoordinates = new(proxy.Occupied) };
        if (proxy is RobotArm) state.inputOutputState = new()
        { inputEnergyCoordinates = new() { new(0, 1) }, outputCoordinates = new() { new(0, -1) }, pipeInputCoordinates = new() { new(1, 1) },
            gridCoordinates = new() { default }, focusCoordinates = new() { new(1, 0) }, inputItemAreas = new() { new() { coordinate = new(-1, 0) } } };
        if (proxy is ConveyorBelt { MultipleCells: true }) state.occupiedCoordinates.Add(new(1, 0));
        return !RejectCapture;
    }
    public bool RegisterDataOnlyInstallation(InstallationSaveState state, out InstallationSaveState stored)
    {
        stored = null;
        if (RejectRegistration) return false;
        if (savedInstallationStates.ContainsKey(state.anchorCoordinate)) throw new Exception("duplicate storage key");
        Registrations++; stored = state.Clone(); savedInstallationStates.Add(state.anchorCoordinate, stored); return true;
    }
    public void RemoveInstallation(Vector2Int key, bool removeUtilityPoleReferences = true)
    {
        Removes++; savedInstallationStates.Remove(key);
        if (removeUtilityPoleReferences) { IndividualPoleScans++; RemoveUtilityPoleConnectionReferences(key); }
        if (SlowRemoval)
        {
            // Force a real iterator yield to test disposing halfway through clear.
            long until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 500;
            while (System.Diagnostics.Stopwatch.GetTimestamp() < until) { }
        }
    }
    public void RemoveInstallation(InstallationObject obj) => RemoveInstallation(obj.Anchor);
    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state) => state.anchorCoordinate;
}
public static class MapObjectTickProfiler
{
    private sealed class Scope : IDisposable { public void Dispose() { } }
    public static IDisposable SampleNamed(string category, string owner, string operation) => new Scope();
}
public partial class ConveyorWorld
{
    public sealed class VisualPart { }
    public sealed record Record(BlockStateStore.InstallationSaveState State, ConveyorBelt Prototype, VisualPart[] Parts);
    public readonly Dictionary<Vector2Int, Record> Records = new();
    private int bulkUpdateDepth;
    private bool disposed = false, batchesDirty = true;
    public int Depth => bulkUpdateDepth;
    public int Captures, Rebuilds;
    private void RebuildBatches() { Rebuilds++; batchesDirty = false; }
    public IEnumerator PrepareBenchmarkPresentation() { RebuildBatches(); yield break; }
    public void ClearRecords() { Records.Clear(); batchesDirty = true; }
    public VisualPart[] CaptureVisualParts(ConveyorBelt source, Vector3 position, Quaternion rotation)
    { Captures++; return new[] { new VisualPart() }; }
    public Record Register(BlockStateStore.InstallationSaveState state, ConveyorBelt source, Vector3 pos, Quaternion rot, Vector3 scale, VisualPart[] parts)
    { var record = new Record(state.Clone(), source, parts); Records.Add(state.anchorCoordinate, record); batchesDirty = true; return record; }
}
public class Block
{
    public object MapObject;
    public ConveyorWorld.Record Record;
    public bool HasBridge;
    public readonly object FloorItem = new();
    public bool TryGetRuntimeConveyorRecord(out ConveyorWorld.Record record)
    { record = Record; return Record != null || HasBridge; }
    public void BindRuntimeConveyor(ConveyorWorld.Record record) { Record = record; MapObject = record.Prototype; }
    public void SetMapObject(object value) { MapObject = value; Record = null; }
}
public partial class PipeWorld
{
    private bool disposed = false, bodyDirty = true;
    public TerrainGenerator Owner;
    public int Rebuilds;
    private void RebuildBodyBatches() { Rebuilds++; bodyDirty = false; }
    private void RefreshFluidRecords() { }
    public IEnumerator PrepareBenchmarkPresentation() { RebuildBodyBatches(); yield break; }
    public void MarkDirty() => bodyDirty = true;
}
public class MapSaveData { }
public partial class TerrainGenerator
{
    public readonly Transform transform = new();
    public readonly ConveyorWorld World = new();
    public readonly PipeWorld Pipes;
    public readonly BlockStateStore resourceStateStore = new();
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public float chunkGenerationFrameTimeBudgetMilliseconds = 3;
    public int Created, Released, Reloads, ItemClears, RefreshDepth, Flushes, NetworkDirty;
    public int LiveRegistrations;
    public TerrainGenerator() { Pipes = new() { Owner = this }; }
    private readonly Dictionary<Vector2Int, object> loadedChunks = new();
    private int chunkSize = 16;
    private bool IsChunkStreamingBusy => false;
    private int CompletedChunkGenerationCount;
    public ConveyorWorld EnsureConveyorWorld() => World;
    private PipeWorld EnsurePipeWorld() => Pipes;
    private void EnsureResourceStateStore() { }
    public bool TryGetLoadedBlock(Vector2Int cell, out Block block)
    {
        if (Blocks.TryGetValue(cell, out block)) return true;
        var chunk = new Vector2Int((int)Math.Floor(cell.x / (double)chunkSize), (int)Math.Floor(cell.y / (double)chunkSize));
        if (!loadedChunks.ContainsKey(chunk)) return false;
        block = new(); Blocks.Add(cell, block); return true;
    }
    private bool IsChunkGenerationActive(Vector2Int coord) => false;
    private void EnsureChunkActivationStorageCapacity(int count) { }
    private void QueueChunkGeneration(Vector2Int coord, int size) { loadedChunks[coord] = new(); CompletedChunkGenerationCount++; }
    public int TerrainChunkCount => loadedChunks.Count;
    private void EnsureChunkGenerationProcessing() { }
    private void BeginConveyorRuntimeRefreshBatch() => RefreshDepth++;
    private void EndConveyorRuntimeRefreshBatch() { RefreshDepth--; Flushes++; }
    private void MarkConveyorNetworkDirty() => NetworkDirty++;
    private object CaptureTerrainSaveState() => new();
    private void LoadFromSaveState(object terrain, MapSaveData save) => Reloads++;
    private void CopyLoadedBlocks(List<Block> result) => result.AddRange(Blocks.Values);
    private void ClearAllBeltItems(out int runtime, out int saved) { runtime = saved = 0; ItemClears++; }
    private InstallationObject CreateInstallationObject(InstallationObject source, Transform parent)
    {
        Created++;
        InstallationObject proxy = source switch { ConveyorBelt belt => new ConveyorBelt { MultipleCells = belt.MultipleCells },
            Pipe => new Pipe(), Building => new Building(), RobotArm => new RobotArm(), Train => new Train(), Railload => new Railload(), _ => new InstallationObject() };
        proxy.ItemId = source.ItemId; proxy.Footprint = source.Footprint; proxy.Blocking = source.Blocking;
        return proxy;
    }
    private void ReleaseInstallationObject(InstallationObject instance, InstallationObject source = null)
    { Released++; InstallationObject.Live.Remove(instance); }
    private void BindLoadedBlocksToDataOnlyConveyor(ConveyorWorld.Record record)
    { foreach (var cell in record.State.occupiedCoordinates) Blocks[cell].BindRuntimeConveyor(record); }
    private bool RegisterBoundaryData(BlockStateStore.InstallationSaveState state)
    {
        if (!resourceStateStore.RegisterDataOnlyInstallation(state, out var stored)) return false;
        foreach (var cell in stored.occupiedCoordinates) Blocks[cell].SetMapObject(stored);
        return true;
    }
    private bool RegisterDataOnlyPipeState(BlockStateStore.InstallationSaveState state, Pipe prototype, Vector3 pos, Quaternion rot, Vector3 scale)
    { Pipes.MarkDirty(); return RegisterBoundaryData(state); }
    private bool RegisterDataOnlyBuildingState(BlockStateStore.InstallationSaveState state, Building prototype, Vector3 pos, Quaternion rot, Vector3 scale, out object record)
    { record = null; return RegisterBoundaryData(state); }
    private object RegisterDataOnlyRobotArm(RobotArm prototype, BlockStateStore.InstallationSaveState state)
        => RegisterBoundaryData(state) ? state : null;
    private void RegisterLiveInstallationObject(InstallationObject obj)
    {
        LiveRegistrations++; InstallationObject.Live.Add(obj);
        resourceStateStore.RegisterDataOnlyInstallation(new() { itemId = obj.ItemId, anchorCoordinate = obj.Anchor,
            occupiedCoordinates = new(obj.Occupied), hasWorldPose = true, worldPosition = obj.transform.position,
            worldRotation = obj.transform.rotation }, out _);
    }
}

internal static class BulkChecks
{
    private static int assertions;
    private static void Require(bool pass, string label)
    { assertions++; if (!pass) throw new Exception(label); }
    private static void Drain(IEnumerator work)
    { using (work as IDisposable) { while (work.MoveNext()) { } } }
    public static void Main()
    {
        Require(!BenchmarkLayout.IsWorkSliceExpired(100, 109, 1000), "slice before 10 ms");
        Require(BenchmarkLayout.IsWorkSliceExpired(100, 110, 1000), "slice at 10 ms");
        Require(!BenchmarkLayout.IsWorkSliceExpired(0, 3333, 333333), "fractional clock threshold rounds up");
        var terrain = new TerrainGenerator();
        var placement = new InstallationPlacementController();
        var source = new ConveyorBelt { ItemId = 1 };
        GameManager.Instance.ItemManger.ItemDefinitions.Add(new() { id = 1, mapObject = source });
        var machineCell = new Vector2Int(300, 300);
        var pipeCell = new Vector2Int(301, 300);
        var machine = new InstallationObject(); var pipe = new Pipe();
        terrain.Blocks.Add(machineCell, new() { MapObject = machine });
        terrain.Blocks.Add(pipeCell, new() { MapObject = pipe, HasBridge = true });
        // Non-belt variant IDs overlap with belts in real saves.
        var machineState = new BlockStateStore.InstallationSaveState { itemId = 2, anchorCoordinate = machineCell,
            utilityPoleConnectedAnchors = new() { new(1, 1), machineCell } };
        terrain.resourceStateStore.savedInstallationStates.Add(machineCell, machineState);
        terrain.resourceStateStore.savedInstallationStates.Add(pipeCell, new() { itemId = 3, conveyorVariantKind = 0, anchorCoordinate = pipeCell });
        object floor = terrain.Blocks[pipeCell].FloorItem;
        long firstSequence = 0;
        foreach (int rings in new[] { 100, 10 })
        {
            using (terrain.BeginBenchmarkPlacementUpdate())
            {
                Require(MapObjectTickManager.SimulationPaused && terrain.World.Depth == 1 && terrain.RefreshDepth == 1, "bulk pauses and defers updates");
                Require(terrain.chunkGenerationFrameTimeBudgetMilliseconds == 10, "terrain generation shares budget");
                Drain(terrain.ClearBenchmarkBelts());
                Require(terrain.resourceStateStore.savedInstallationStates.Count == 2 && terrain.World.Records.Count == 0, "replacement removes old belts without other installations");
                var templates = new Dictionary<(int, int, int, int), TerrainGenerator.BenchmarkInstallationTemplate>();
                for (int ring = 1; ring <= rings; ring++)
                {
                    int length = BenchmarkLayout.RingLength(ring);
                    for (int index = 0; index < length; index++)
                    {
                        var cell = BenchmarkLayout.RingCell(ring, index);
                        var prev = BenchmarkLayout.RingCell(ring, (index + length - 1) % length);
                        var next = BenchmarkLayout.RingCell(ring, (index + 1) % length);
                        var key = (prev.X - cell.X, prev.Y - cell.Y, next.X - cell.X, next.Y - cell.Y);
                        if (!templates.TryGetValue(key, out var template))
                        { template = terrain.CreateBenchmarkConveyorTemplate(placement, source, templates.Count); templates.Add(key, template); }
                        var coordinate = new Vector2Int(cell.X, cell.Y);
                        if (!terrain.Blocks.ContainsKey(coordinate)) terrain.Blocks.Add(coordinate, new());
                        Require(terrain.TryPlaceBenchmarkConveyor(template, coordinate), "direct registration succeeds");
                        terrain.World.SynchronizeForWorldPresentation();
                        var state = terrain.resourceStateStore.savedInstallationStates[coordinate];
                        Require(state.hasWorldPose && state.worldPosition == new Vector3(cell.X + .25f, .2f, cell.Y + .5f), "saved world pose matches grid offset");
                        Require(state.occupiedCoordinates.Count == 1 && state.occupiedCoordinates[0] == coordinate, "persisted footprint matches block");
                        Require(ReferenceEquals(terrain.World.Records[coordinate].Parts, template.VisualParts), "immutable visual parts reused");
                        Require(template.State.anchorCoordinate == Vector2Int.zero && template.State.occupiedCoordinates[0] == Vector2Int.zero, "template state never mutated");
                        if (firstSequence == 0) firstSequence = state.placementSequence;
                    }
                }
                Require(templates.Count == 8 && terrain.World.Records.Count == BenchmarkLayout.BeltCount(rings), "100 ring count and cached orientations");
                Require(terrain.resourceStateStore.savedInstallationStates.Count == BenchmarkLayout.BeltCount(rings) + 2, "saved count matches replacement");
                Require(!terrain.TryPlaceBenchmarkConveyor(templates.Values.First(), machineCell), "cannot overwrite a machine");
                Require(!terrain.TryPlaceBenchmarkConveyor(templates.Values.First(), pipeCell), "cannot overwrite a shared bridge coordinate");
                Require(terrain.World.Rebuilds == (rings == 100 ? 0 : 1), "no renderer rebuild during sliced registration");
            }
            terrain.World.SynchronizeForWorldPresentation();
            terrain.World.SynchronizeForWorldPresentation();
            Require(terrain.World.Rebuilds == (rings == 100 ? 1 : 2), "exactly one rebuild after each bulk update");
            Require(!MapObjectTickManager.SimulationPaused && terrain.World.Depth == 0 && terrain.RefreshDepth == 0, "completion restores scope");
            Require(terrain.chunkGenerationFrameTimeBudgetMilliseconds == 3, "restores chunk budget");
        }
        Require(terrain.Created == 16 && terrain.Released == 16 && terrain.World.Captures == 16, "40400 plus 440 belts require only 8 proxies per job");
        Require(terrain.Reloads == 0 && terrain.ItemClears == 2, "no map reload during replacement");
        Require(ReferenceEquals(terrain.Blocks[machineCell].MapObject, machine) && ReferenceEquals(terrain.Blocks[pipeCell].MapObject, pipe), "preserves machine and underlying pipe");
        Require(ReferenceEquals(terrain.Blocks[pipeCell].FloorItem, floor), "preserves floor items");
        Require(machineState.utilityPoleConnectedAnchors.SequenceEqual(new[] { machineCell }), "batched references remove only deleted anchors");
        Require(terrain.resourceStateStore.IndividualPoleScans == 0, "no save-wide pole scan per belt");
        MapObjectTickManager.SimulationPaused = true;
        var scope = terrain.BeginBenchmarkPlacementUpdate(); scope.Dispose(); scope.Dispose();
        Require(MapObjectTickManager.SimulationPaused && terrain.World.Depth == 0, "prior paused state and repeated dispose");
        MapObjectTickManager.SimulationPaused = false;
        terrain.resourceStateStore.SlowRemoval = true;
        using (terrain.BeginBenchmarkPlacementUpdate())
        {
            var clear = terrain.ClearBenchmarkBelts();
            Require(clear.MoveNext(), "clear yields under costly removal");
            (clear as IDisposable).Dispose();
        }
        Require(terrain.resourceStateStore.savedInstallationStates.Count == 2 && terrain.World.Records.Count == 0, "cancel finishes reset atomically");
        Require(terrain.Blocks.Values.All(block => block.MapObject is not ConveyorBelt), "cancel does not leave orphaned conveyor blocks");
        Require(!MapObjectTickManager.SimulationPaused && terrain.RefreshDepth == 0 && terrain.World.Depth == 0, "cancel restores pause and refresh");
        try
        {
            using (terrain.BeginBenchmarkPlacementUpdate())
                terrain.CreateBenchmarkConveyorTemplate(placement, new ConveyorBelt { ItemId = 1, MultipleCells = true }, 0);
            throw new Exception("invalid footprint accepted");
        }
        catch (InvalidOperationException) { }
        Require(!MapObjectTickManager.SimulationPaused && terrain.World.Depth == 0 && terrain.Created == terrain.Released && UtilityPole.Depth == 0, "failure releases template and scope");
        CheckGridSpawning();
        CheckStreamingGrid();
        CheckFluidTopologyBatch();
        Console.WriteLine($"PASS: {assertions} bulk registration, replacement, preservation and cancellation checks");
    }

    private static void CheckStreamingGrid()
    {
        var terrain = new TerrainGenerator();
        var placement = new InstallationPlacementController();
        var item = new ItemDefinition { id = 77, mapObject = new Pipe { ItemId = 77 } };
        Require(BenchmarkCommand.TryParse("benchmark spawn 77 100000".Split(' '), out var command, out _), "large grid command");
        var probe = new SpawnProbe();
        var work = probe.Run(terrain, placement, item, command, Vector2Int.zero);
        int firstActiveChunkCount = -1, yields = 0;
        using (work as IDisposable)
            while (work.MoveNext())
            {
                yields++;
                Require(probe.benchmarkTotal == 100000, "target known before preparation ends");
                if (probe.benchmarkDone > 0 && firstActiveChunkCount < 0) firstActiveChunkCount = terrain.TerrainChunkCount;
            }
        Require(probe.benchmarkDone == 100000 && terrain.resourceStateStore.savedInstallationStates.Count == 100000, "streamed grid creates exactly 100000 objects");
        Require(terrain.Blocks.Count == 100000, "no duplicate or missing anchors at row batch boundaries");
        Require(yields > 0 && firstActiveChunkCount > 0 && firstActiveChunkCount < terrain.TerrainChunkCount, "objects start before entire terrain is prepared");
        Require(terrain.BenchmarkTerrainChunksDone == terrain.BenchmarkTerrainChunksTotal, "terrain stage finishes with complete counts");
        Require(!MapObjectTickManager.SimulationPaused && !terrain.IsBenchmarkPlacementInProgress && UtilityPole.Depth == 0, "streamed grid restores scopes");
        var cancelled = new TerrainGenerator();
        var second = new SpawnProbe();
        var cancelledWork = second.Run(cancelled, placement, item, command, Vector2Int.zero);
        Require(cancelledWork.MoveNext(), "large grid cancellation checkpoint");
        ((IDisposable)cancelledWork).Dispose();
        Require(!MapObjectTickManager.SimulationPaused && !cancelled.IsBenchmarkPlacementInProgress && UtilityPole.Depth == 0, "cancelled streaming grid restores scopes");
    }

    private static void CheckGridSpawning()
    {
        foreach (InstallationObject source in new InstallationObject[] { new Pipe(), new Building(), new RobotArm(), new InstallationObject() })
        {
            InstallationObject.Live.Clear();
            source.ItemId = 10;
            source.Footprint = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) };
            source.Blocking = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) };
            var terrain = new TerrainGenerator();
            var placement = new InstallationPlacementController();
            bool data = source is Pipe || source is Building || source is RobotArm;
            const int count = 1000;
            BenchmarkRuntime.ForceWorking = true; BenchmarkRuntime.Wakes = 0;
            using (terrain.BeginBenchmarkPlacementUpdate())
            {
                var template = terrain.CreateBenchmarkInstallationTemplate(placement, source, 0);
                Require(template.BlockingOffsets.Length == 2 && template.FootprintOffsets.Length == 3, "cache distinguishes body from IO area");
                for (int index = 0; index < count; index++)
                {
                    var cell = new Vector2Int(index * 4, 10);
                    foreach (var offset in source.Footprint) terrain.Blocks.Add(cell + offset, new());
                    Require(terrain.TryPlaceBenchmarkInstallation(placement, template, cell), "grid placement succeeds " + source.GetType().Name);
                    Require(!terrain.TryPlaceBenchmarkInstallation(placement, template, cell), "grid cannot replace occupied installation");
                    var saved = terrain.resourceStateStore.savedInstallationStates[cell];
                    Require(saved.occupiedCoordinates.SequenceEqual(source.Blocking.Select(c => c + cell)), "save body shifted and IO excluded");
                    Require(saved.hasWorldPose && saved.worldPosition == new Vector3(cell.x + .25f, .2f, cell.y + .5f), "save preserves grid pose");
                    Require(terrain.Blocks[cell].MapObject != null && terrain.Blocks[cell + new Vector2Int(1, 0)].MapObject != null,
                        "all body blocks bound");
                    Require(terrain.Blocks[cell + new Vector2Int(2, 0)].MapObject == null, "IO area does not become a body cell");
                    if (source is RobotArm)
                    {
                        var io = saved.inputOutputState;
                        Require(io.inputEnergyCoordinates[0] == cell + new Vector2Int(0, 1) && io.outputCoordinates[0] == cell + new Vector2Int(0, -1), "arm energy/output saved coordinates shifted");
                        Require(io.gridCoordinates[0] == cell && io.focusCoordinates[0] == cell + new Vector2Int(1, 0)
                            && io.pipeInputCoordinates[0] == cell + new Vector2Int(1, 1) && io.inputItemAreas[0].coordinate == cell + new Vector2Int(-1, 0), "arm persisted area coordinates shifted");
                        Require(template.State.inputOutputState.gridCoordinates[0] == Vector2Int.zero, "arm template IO remains immutable");
                    }
                    terrain.Pipes.SynchronizeForWorldPresentation();
                }
                Require(terrain.Pipes.Rebuilds == 0, "pipe presentation remains deferred for entire job");
                Require(terrain.resourceStateStore.savedInstallationStates.Count == count, "all grid installations persisted");
                Require(terrain.Created == (data ? 1 : count) && terrain.Released == (data ? 1 : 0), "one proxy per data type and real instances for machines");
                Require(placement.FootprintQueries == 1 && placement.BlockingQueries == (data ? 0 : 1) && placement.Positions == 1, "geometry and pose resolved only once");
                Require(placement.CachedConfigurations == (data ? 0 : count) && placement.NormalConfigurations == (data ? 1 : 0), "normal IO initialization uses cached body for machines");
                Require(terrain.LiveRegistrations == (data ? 0 : count) && BenchmarkRuntime.Wakes == (data ? 0 : count), "force working wakes each live machine");
            }
            terrain.Pipes.SynchronizeForWorldPresentation(); terrain.Pipes.SynchronizeForWorldPresentation();
            Require(terrain.Pipes.Rebuilds == 1 && !terrain.IsBenchmarkPlacementInProgress && !MapObjectTickManager.SimulationPaused && UtilityPole.Depth == 0,
                "presentation refreshes once and normal simulation resumes");
        }
        var failedTerrain = new TerrainGenerator();
        var failedPlacement = new InstallationPlacementController { RejectConfiguration = true };
        var machine = new InstallationObject { ItemId = 20, Footprint = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) },
            Blocking = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) } };
        var anchor = new Vector2Int(12, 12);
        failedTerrain.Blocks.Add(anchor, new()); failedTerrain.Blocks.Add(anchor + new Vector2Int(1, 0), new());
        try
        {
            using (failedTerrain.BeginBenchmarkPlacementUpdate())
            {
                var template = failedTerrain.CreateBenchmarkInstallationTemplate(failedPlacement, machine, 0);
                failedTerrain.TryPlaceBenchmarkInstallation(failedPlacement, template, anchor);
            }
            throw new Exception("configuration failure not propagated");
        }
        catch (InvalidOperationException) { }
        Require(failedTerrain.Blocks.Values.All(block => block.MapObject == null) && failedTerrain.Created == failedTerrain.Released,
            "failure releases instance and every body binding");
        Require(failedTerrain.resourceStateStore.savedInstallationStates.Count == 0 && !failedTerrain.IsBenchmarkPlacementInProgress
            && !MapObjectTickManager.SimulationPaused, "failure leaves no partial save and restores simulation");
        BenchmarkRuntime.ForceWorking = false;
        InstallationObject.Live.Clear();
    }

    private static void CheckFluidTopologyBatch()
    {
        InputOutputModule.activeRuntimeModules.Clear();
        for (int i = 0; i < 200; i++) InputOutputModule.activeRuntimeModules.Add(new());
        InputOutputModule.TopologyQueries = InputOutputModule.GlobalWakes = InputOutputModule.Invalidations = InputOutputModule.LocalNotifications = 0;
        InputOutputModule.LocalWakes.Clear();
        int events = 0;
        Action<InputOutputModule> observer = module => events++;
        InputOutputModule.RuntimePipeTopologyChanged += observer;
        try
        {
            InputOutputModule.BeginRuntimePipeTopologyBatch(); InputOutputModule.BeginRuntimePipeTopologyBatch();
            for (int i = 0; i < 1000; i++)
            {
                InputOutputModule.NotifyRuntimePipeTopologyChanged(new[] { new Vector2Int(i % 64, 0) });
                InputOutputModule.SimulatePlacementWake();
                InputOutputModule.SimulateGridChange(null);
            }
            Require(InputOutputModule.TopologyQueries == 0 && events == 0 && InputOutputModule.Invalidations == 0, "no global fluid scan or event during bulk placement");
            InputOutputModule.EndRuntimePipeTopologyBatch();
            Require(InputOutputModule.TopologyQueries == 0 && events == 0, "nested end preserves deferral");
            InputOutputModule.EndRuntimePipeTopologyBatch();
            Require(InputOutputModule.TopologyQueries == 200 && InputOutputModule.GlobalWakes == 200 && events == 1, "1000 topology changes cause one full scan and notification");
            Require(InputOutputModule.LocalWakes.Count == 64 && InputOutputModule.LocalNotifications == 1 && InputOutputModule.Invalidations == 1, "local coordinates deduplicated and delivered once");
            InputOutputModule.NotifyRuntimePipeTopologyChanged(new[] { new Vector2Int(65, 0) });
            Require(InputOutputModule.TopologyQueries == 400 && events == 2 && InputOutputModule.LocalWakes.Contains(new(65, 0)), "normal placement remains immediate after bulk end");
            InputOutputModule.BeginRuntimePipeTopologyBatch();
            InputOutputModule.NotifyRuntimePipeTopologyChanged(Array.Empty<Vector2Int>());
            InputOutputModule.EndRuntimePipeTopologyBatch();
            Require(InputOutputModule.Invalidations == 3 && InputOutputModule.TopologyQueries == 600 && events == 3, "empty-coordinate removal still invalidates and wakes");
            InputOutputModule.EndRuntimePipeTopologyBatch();
            Require(InputOutputModule.TopologyQueries == 600 && events == 3, "repeated batch end is harmless");
        }
        finally { InputOutputModule.RuntimePipeTopologyChanged -= observer; InputOutputModule.activeRuntimeModules.Clear(); }
    }
}
