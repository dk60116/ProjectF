using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using ProjectF.Simulation;

// Actual compact output, visible-cell materialization, capacity/count/removal and save-source methods.
public class Bucket { }
public class ItemDefinition
{
    public int id;
    public bool oneItem;
    public static int ResolveStackCapacity(ItemDefinition item, int capacity) => item?.oneItem == true ? 1 : capacity;
    public static int ResolveStackCapacity(ItemManager manager, int id, int capacity) => ResolveStackCapacity(InputOutputModule.ResolveItemDefinition(id), capacity);
    public enum ItemLightMode { None, Light }
    public bool isFluid;
    public object mapObject, portableMesh = new(), portableMat = new();
    public ItemLightMode lightMode;
}
public class ItemManager { }
public class GameManager { public static GameManager Instance; public ItemManager ItemManger; }
public class BoxObject
{
    public int MaximumStoredItemCount = 48;
    public static bool IsRuntimeContentBlock(Block block) => block.BoxContent;
}
namespace ProjectF.Benchmark { public static class BenchmarkRuntime { public static bool ForceWorking; } }
public static partial class SavedCapacityProbe { public static int Resolve(int item, int capacity) => ResolveSavedCenterStackCapacity(item, capacity); }
public partial class IoCapacityProbe
{
    public readonly List<Vector2Int> runtimeOutputCoordinates = new();
    private readonly HashSet<Vector2Int> runtimeAreaVisitedCoordinates = new();
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public readonly HashSet<Vector2Int> SavedBoxes = new();
    private bool IsBenchmarkWorking => ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
    private int RuntimeAreaMaxObjects => 48;
    private static ItemDefinition ResolveItemDefinition(int id) => InputOutputModule.ResolveItemDefinition(id);
    private bool IsFluidItemId(int id) => ResolveItemDefinition(id).isFluid;
    private bool TryResolveRuntimeBlockCenterCapacity(Vector2Int coordinate, out int capacity) { capacity = 48; return SavedBoxes.Contains(coordinate); }
    private bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
    private object ResolveSavedCoordinateMapObject(Vector2Int coordinate) => SavedBoxes.Contains(coordinate) ? new BoxObject() : null;
    public int SavedCapacity(Vector2Int coordinate) => ResolveRuntimeBlockCenterCapacity(coordinate, 1, 48);
    private int GetRuntimeAreaObjectCount(IReadOnlyList<Vector2Int> coordinates) { int count = 0; foreach (var c in coordinates) count += Blocks[c].GetInputAreaCenterItemCount(); return count; }
    private bool RuntimeOutputCoordinateAcceptsItem(Vector2Int coordinate, int item) => true;
    private bool CanAddRuntimeOutputItems(Vector2Int coordinate, int item, int count, out Block block, out bool saved)
    { saved = false; block = Blocks[coordinate]; return block.CanAddInputAreaCenterObjects(count, item); }
    private int GetRuntimeAreaTopItemId(Vector2Int coordinate) => Blocks[coordinate].GetInputAreaCenterItemId();
    public bool Reserve(int count) => TryResolveOutputTarget(1, count, out _);
}
public static partial class InputOutputModule
{
    public static readonly ItemDefinition Ordinary = new() { id = 1 }, Light = new() { id = 2, lightMode = ItemDefinition.ItemLightMode.Light };
    public static int DefinitionLookups;
    public static bool Filter = true;
    public static ItemDefinition ResolveItemDefinition(int id) { DefinitionLookups++; return id < 0 ? null : id == 2 ? Light : Ordinary; }
    public static bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int c, int item) => Filter;
}
public partial class TerrainGenerator
{
    public static TerrainGenerator Active = new();
    public readonly EngineObject gameObject = new();
}
public static class MapObjectTickProfiler
{
    public static bool IsDetailedEnabled => false;
    public static long BeginSample() => Stopwatch.GetTimestamp();
    public static void RecordNamedElapsedTicks(string a, string b, string c, long d) { }
    public static Sample SampleNamed(string a, string b, string c) => default;
    public readonly struct Sample : IDisposable { public void Dispose() { } }
}
namespace ProjectF.Rendering
{
    public class CameraRenderCulling
    {
        public static bool VisibleAll;
        public void Update(object camera) { }
        public bool Intersects(Bounds b) => VisibleAll || b.min.x <= 20 && b.max.x >= -20 && b.min.z <= 20 && b.max.z >= -20;
    }
}
public sealed partial class PortableItemRenderer
{
    public static PortableItemRenderer Current = new();
    public static PortableItemRenderer EnsureFor(EngineObject host) => Current;
    object mainCamera;
    public void Present() => RefreshDeferredOutputPresentation();
}
public class TestItemPool
{
    public static int Created;
    public PortableObject Get(object prefab) { Created++; return new PortableObject(); }
}
public partial class Block
{
    Vector2Int coordinate => Coordinate;
    public const int FloorStackStateSentinel = -1000000003, InputAreaCenterStackStateSentinel = -1000000001, ConveyorStackStateSentinel = -1000000002;
    readonly TerrainGenerator cachedTerrainGenerator = TerrainGenerator.Active;
    readonly TestItemPool floorObjectPool = new();
    readonly object floorObjectPrefab = new();
    public bool IsRuntimeConveyor, Blocked;
    public bool IsRuntimeActive => true;
    bool inputAreaCenterObjectsVisible = true;
    public int Notifications;
    public Block(Vector2Int coordinate = default) { Coordinate = coordinate; inputAreaCenterAnchor.position = WorldPosition + Vector3.up * .2f; }
    bool IsFarmlandFertilizerItem(int item) => false;
    bool BlocksFloorObjectStacking(int item) => Blocked;
    void EnsureFloorObjectsInitialized(bool materialize = true) { if (materialize) MaterializeDeferredOutputs(int.MaxValue); }
    bool ResolveFloorObjectPool() => true;
    void EnsureInputAreaCenterAnchorInitialized() { }
    bool TryInitializePooledPortableObject(PortableObject item, int id) { item.SetTestItem(id); return true; }
    int maxFloorObjectsPerStack = 48, inputAreaCenterMaxObjects = 48;
    public object mapObject;
    public object MapObject => mapObject;
    bool TryGetInstalledItemAreaCapacity(out int capacity) { capacity = 0; return false; }
    static bool IsStackCompatible(List<PortableObject> stack, int item) => stack.Count == 0 || item < 0 || stack[0].ItemId == item;
    static void CleanupPortableStack(List<PortableObject> stack) { }
    static PortableObject GetTopPortableObject(List<PortableObject> stack) => stack.Count == 0 ? null : stack[^1];
    void CleanupConveyorStack() { }
    int GetConveyorLaneCount() => 0;
    bool HasConveyorItemAtLane(int index) => false;
    int GetConveyorItemIdAtLane(int index) => -1;
    static bool ShouldPersistFloorObject(PortableObject item) => item != null;
    void NotifyRuntimeItemStackChanged() => Notifications++;
    void ReleaseFloorObject(PortableObject item) => item.Dispose();
    public bool TryAddConveyorObjectAnimatedAtPlacement(int item, Vector3 reference, Vector3 start, float delay,
        out PortableObject portable, bool forceAnimatedPlacement) { portable = null; return true; }
    public bool TryAddInputAreaCenterObjectAnimated(int id, Vector3 start, float delay, out PortableObject portable)
    {
        portable = floorObjectPool.Get(floorObjectPrefab); portable.SetTestItem(id);
        inputAreaCenterStack.Add(portable); return true;
    }
    public void ClearData() => ClearDeferredOutputs();
}
static class DeferredChecks
{
    static int checks;
    static void Require(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
    public static void Run()
    {
        TestClock.time = 10;
        var batch = new OutputStackBatch();
        Require(batch.TryAppend(1, 10, 0, .3f) && batch.TryAppend(1, 10, .1f, .3f), "Regularly spaced batch coalesces");
        Require(!batch.TryAppend(2, 10, .2f, .3f) && batch.Count == 2, "Mixed fluid/item identities cannot merge");
        batch.TryPeekBottom(10.15f, out float launch, out bool moving);
        Require(moving && launch == 10, "Pending movement retains absolute launch time");
        batch.RemoveBottom(); batch.TryPeekBottom(10.5f, out launch, out moving);
        Require(!moving && Math.Abs(launch - 10.1f) < .0001f, "Next arrival retains its staggered absolute clock");
        var center = new Block(new Vector2Int(100, 100));
        int before = TestItemPool.Created;
        Require(center.TryAddDeferredOutput(1, center.WorldPosition, 0, true, out bool handled) && handled, "Center output commits compactly");
        Require(center.GetInputAreaCenterItemCount(1) == 1 && !center.CanAddInputAreaCenterObjects(48, 1)
            && !center.CanAddInputAreaCenterObjects(1, 3), "Capacity and type checks include pending stock");
        var saved = center.CaptureFloorObjectState();
        Require(saved.Count == 3 && saved[0] == Block.InputAreaCenterStackStateSentinel && saved[1] == 1 && saved[2] == 1
            && TestItemPool.Created == before, "Save captures compact center stock without materialization");
        Require(center.TryConsumeOneInputAreaCenterObject(1, out int id) && id == 1 && center.GetInputAreaCenterItemCount() == 0,
            "Nonvisual consumer preserves existing immediate center consumption semantics");
        InputOutputModule.Filter = false;
        Require(!center.TryAddDeferredOutput(1, center.WorldPosition, 0, true, out handled), "IO filter rejects deferred output");
        InputOutputModule.Filter = true;
        Require(!center.TryAddDeferredOutput(2, center.WorldPosition, 0, true, out handled) && !handled,
            "Individual item lights retain original path");
        Require(InputOutputModule.TryEmitOutputItemToBlock(center, 1, center.WorldPosition, 0, out var emitted)
            && emitted == null && center.GetInputAreaCenterItemCount(1) == 1,
            "Actual machine output boundary commits data while returning no unnecessary item object");
        center.TryConsumeOneInputAreaCenterObject(1, out _);
        Require(InputOutputModule.TryEmitOutputItemToBlock(center, 2, center.WorldPosition, 0, out emitted)
            && emitted != null && emitted.PickupGate.Blocked, "Individual-visual fallback preserves automatic pickup blocking");
        var flight = new Block(new Vector2Int(200, 200));
        flight.TryAddDeferredOutput(1, flight.WorldPosition + Vector3.left * 2, .2f, false, out _);
        TestClock.time = 10.3f; flight.MaterializeDeferredOutputs(int.MaxValue);
        Require(flight.floorStacks[0].Count == 1 && flight.floorStacks[0][0].Moving && !flight.floorStacks[0][0].PickupGate.Settled,
            "Re-entry during flight does not unlock pickup early");
        PortableMoveScheduler.Current.Tick(10.6f);
        Require(flight.floorStacks[0][0].PickupGate.Settled && !flight.floorStacks[0][0].Moving,
            "Landing follows original deadline after late materialization");
        TestClock.time = 11;
        var removed = new Block(new Vector2Int(1000, 1000)); removed.TryAddDeferredOutput(1, removed.WorldPosition, 0, false, out _);
        before = TestItemPool.Created;
        Require(removed.RemoveFloorObjects(1, 5) == 1 && removed.CountFloorObjects(1) == 0 && TestItemPool.Created == before,
            "Bulk removal clears compact items without native creation");
        const int n = 100000;
        var blocks = new Block[n];
        for (int i = 0; i < n; i++) blocks[i] = new Block(new Vector2Int(i % 316 * 4 + 10000, i / 316 * 4 + 10000));
        before = TestItemPool.Created;
        long bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) if (!blocks[i].TryAddDeferredOutput(1, blocks[i].WorldPosition, 0, false, out _)) throw new Exception("Burst rejected");
        watch.Stop(); long coldBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Require(TestItemPool.Created == before && PortableItemRenderer.Current.DeferredOutputItemCount == n,
            "Cold 100k burst creates no item wrappers or movement reservations");
        PortableItemRenderer.Current.Present();
        Require(TestItemPool.Created == before && PortableItemRenderer.Current.LastMaterializedOutputCount == 0,
            "Offscreen 100k output needs no render materialization");
        TestClock.time = 12;
        bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < n; i++) if (!blocks[i].TryAddDeferredOutput(1, blocks[i].WorldPosition, 0, false, out _)) throw new Exception("Repeat burst rejected");
        long repeatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Require(TestItemPool.Created == before && PortableItemRenderer.Current.DeferredOutputItemCount == n * 2
            && repeatedBytes == 0, "Real second output burst accumulates new stock with zero item objects and zero allocations");
        saved = blocks[^1].CaptureFloorObjectState();
        Require(saved.Count == 5 && saved[0] == Block.FloorStackStateSentinel && saved[2] == 2 && saved[3] == 1 && saved[4] == 1,
            "Compact floor stock keeps existing save layout");
        Require(blocks[^1].HasVirtualizableFloorObjectState(), "Compact floor stock remains chunk-virtualizable");
        TestClock.time = 12; ProjectF.Rendering.CameraRenderCulling.VisibleAll = true;
        PortableItemRenderer.Current.Present();
        Require(TestItemPool.Created - before == 2048 && PortableItemRenderer.Current.DeferredOutputItemCount == n * 2 - 2048,
            "Visible burst uses bounded presentation work and retains all remaining stock");
        Require(blocks[0].floorStacks[0][0].PickupGate.Settled, "Re-entry after landing materializes settled stock");
        foreach (var b in blocks) b.ClearData();
        Require(PortableItemRenderer.Current.DeferredOutputItemCount == 0 && PortableItemRenderer.Current.DeferredOutputBlockCount == 0,
            "Map release clears deferred registrations and counters");
        ProjectF.Rendering.CameraRenderCulling.VisibleAll = false;
        ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = true;
        var cachedOutput = new Block(new Vector2Int(50000, 50000));
        InputOutputModule.DefinitionLookups = 0;
        for (int i = 0; i < 1000; i++)
            if (!cachedOutput.TryAddDeferredOutput(1, cachedOutput.WorldPosition, 0, false, out _, InputOutputModule.Ordinary))
                throw new Exception("Cached output rejected");
        Require(InputOutputModule.DefinitionLookups == 0 && cachedOutput.CountFloorObjects(1) == 1000,
            "authoritative cached definition removes repeated lookup during forced output");
        Require(cachedOutput.TryAddDeferredOutput(1, cachedOutput.WorldPosition, 0, false, out _, InputOutputModule.Light)
            && InputOutputModule.DefinitionLookups == 1,
            "a mismatched cached item ID resolves the requested definition instead of changing output behavior");
        cachedOutput.ClearData();
        var unlimited = new Block(new Vector2Int(30000, 30000));
        var floor = new Block(new Vector2Int(30001, 30000));
        for (int i = 0; i < 1000; i++) {
            if (!unlimited.TryAddDeferredOutput(1, unlimited.WorldPosition, 0, true, out _)
                || !floor.TryAddDeferredOutput(1, floor.WorldPosition, 0, false, out _)) throw new Exception("Unlimited output rejected");
        }
        Require(unlimited.GetInputAreaCenterItemCount() == 1000 && floor.CountFloorObjects(1) == 1000,
            "forced work retains 1000 outputs per stack beyond authored capacity");
        Require(unlimited.GetInputAreaCenterCapacity(1) == int.MaxValue && !unlimited.CanAddInputAreaCenterObjects(1, 3),
            "unlimited output preserves item compatibility");
        var io = new IoCapacityProbe(); io.runtimeOutputCoordinates.Add(unlimited.Coordinate); io.Blocks[unlimited.Coordinate] = unlimited;
        Require(io.Reserve(1), "normal producer aggregate reservation accepts an over-capacity forced stack");
        InputOutputModule.Ordinary.oneItem = true;
        Require(unlimited.CanAddInputAreaCenterObjects(2, 1) && SavedCapacityProbe.Resolve(1, int.MaxValue) == int.MaxValue,
            "forced loaded and saved output bypass single-item stack limits");
        var box = new Block { mapObject = new BoxObject() };
        Require(box.GetInputAreaCenterCapacity(1) == 1 && SavedCapacityProbe.Resolve(1, 48) == 1,
            "loaded and saved containers preserve single-item limits");
        var savedBoxCoordinate = new Vector2Int(40000, 40000); io.SavedBoxes.Add(savedBoxCoordinate);
        Require(io.SavedCapacity(savedBoxCoordinate) == 1 && io.SavedCapacity(new Vector2Int(40001, 40000)) == int.MaxValue,
            "producer saved reservations retain container limits while uncapping ground outputs");
        InputOutputModule.Ordinary.oneItem = false;
        Require(box.GetInputAreaCenterCapacity(1) == 48, "forced work leaves container capacity unchanged");
        ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = false;
        Require(!unlimited.CanAddInputAreaCenterObjects(1, 1) && unlimited.GetInputAreaCenterItemCount() == 1000 && !io.Reserve(1),
            "turning force off restores normal limits without discarding accumulated outputs");
        unlimited.ClearData(); floor.ClearData();
        Console.WriteLine($"Deferred output: {checks} checks passed; cold 100k commit {watch.Elapsed.TotalMilliseconds:F1} ms, {coldBytes:N0} bytes, 0 item objects; real second 100k burst: {repeatedBytes} bytes, 0 new item objects (engine boundaries doubled, not a Unity FPS measurement).");
    }
}
