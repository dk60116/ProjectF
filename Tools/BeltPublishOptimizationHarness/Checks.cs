using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Application { public static bool isPlaying = true; }
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
}

public readonly record struct BlockHandle(int Id);
public struct BeltLaneState { public int ItemId; public long Remaining; }
public static class MapObjectTickProfiler
{
    public static int ActivityRefreshes;
    public static void AddBeltActivityRefreshCall() => ActivityRefreshes++;
}

public partial class Block
{
    public const int ConveyorCellItemUnit = 4;
    private const int ConveyorStackLaneLimit = 4;
    private int beltJobOccupancyVersion0, beltJobOccupancyVersion1, beltJobOccupancyVersion2, beltJobOccupancyVersion3;
    private OutputBoundary deferredFloorOutput;
    public readonly List<List<int>> floorStacks = new() { new() };
    public event Action<Block> RuntimeItemStackChanged;
    public int IngressChecks, VisualVersions;
    private readonly BeltLaneState[] lanes = new BeltLaneState[ConveyorStackLaneLimit];
    public int Id { get; }
    public int LaneReads;
    public bool Stacking = true, Virtual = true, HasLegacyMotion;
    public Block(int id)
    {
        Id = id;
        for (int i = 0; i < lanes.Length; i++) lanes[i].ItemId = -1;
    }
    public void SetLane(int lane, int itemId, long remaining)
        => lanes[lane] = new BeltLaneState { ItemId = itemId, Remaining = remaining };
    public void ResetReads() => LaneReads = 0;
    public readonly bool[] Bound = { true, true, true, true };
    public int BeltJobIndex(int lane) => Bound[lane] ? Id * 4 + lane : -1;
    public BeltLaneState NativeRead(int lane) => lanes[lane];
    private void IncrementConveyorItemVisualVersion() => VisualVersions++;
    private bool TryTransferOneDroppedFloorObjectToConveyor() { IngressChecks++; floorStacks[0].Clear(); return true; }
    public void PutDeferredFloorItem() => deferredFloorOutput.Count++;
    private bool IsConveyorStackingEnabled() => Stacking;
    private bool ShouldUseVirtualConveyorItemRendering() => Virtual;
    private bool HasNonBeltCpuRenderedConveyorMotionStates() => HasLegacyMotion;
    private bool TryReadBeltJobLane(int lane, out BeltLaneState state)
    {
        LaneReads++;
        state = lanes[lane];
        return true;
    }
}
public struct OutputBoundary { public int Count; }
public sealed class NativeLaneBoundary
{
    public readonly Dictionary<int, Block> Views = new();
    public int Reads;
    public BeltLaneState ReadLane(int index) { Reads++; return Views[index / 4].NativeRead(index % 4); }
}

public partial class TerrainGenerator
{
    public static TerrainGenerator Active;
    private readonly NativeLaneBoundary beltSimulation = new();
    private readonly HashSet<BlockHandle> conveyorItemVisualBlocks = new();
    private readonly HashSet<BlockHandle> conveyorItemVisualDirtyBlocks = new();
    private readonly List<BlockHandle> dynamicConveyorItemVisualBlocks = new();
    private readonly Dictionary<BlockHandle, int> dynamicConveyorItemVisualBlockIndices = new();
    private readonly Dictionary<BlockHandle, int> conveyorItemCountsByBlock = new();
    private readonly HashSet<BlockHandle> persistenceDirtyBlocks = new();
    private bool IsConveyorRuntimeRefreshDeferred;
    private int cachedLoadedConveyorItemCount;
    private int conveyorItemVisualBlockSetVersion;
    private int dynamicConveyorItemVisualBlockSetVersion;
    public int DeferredRefreshes { get; private set; }
    private bool TryGetRuntimeBlockHandle(Block block, out BlockHandle handle)
    {
        handle = new BlockHandle(block?.Id ?? -1);
        return block != null;
    }
    private void QueueDeferredConveyorRuntimeRefresh(Block block) => DeferredRefreshes++;
    private void InvalidateBeltItemLineDebugVisuals(Block block) { }
    public void Publish(Block block, bool refreshActivity)
    {
        Active = this; beltSimulation.Views[block.Id] = block;
        CapturePublishedBeltVisualState(block, out int count, out bool moving);
        block.NotifyBeltJobRuntimePublished();
        block.NotifyBeltJobVisualPublished(count, moving, refreshActivity);
    }
    public void Defer(bool value) => IsConveyorRuntimeRefreshDeferred = value;
    public void ClearDirty() => conveyorItemVisualDirtyBlocks.Clear();
    public bool IsTracked(int id) => conveyorItemVisualBlocks.Contains(new BlockHandle(id));
    public bool IsDynamic(int id) => dynamicConveyorItemVisualBlockIndices.ContainsKey(new BlockHandle(id));
    public bool IsDirty(int id) => conveyorItemVisualDirtyBlocks.Contains(new BlockHandle(id));
    public bool IsPersistenceDirty(int id) => persistenceDirtyBlocks.Contains(new BlockHandle(id));
    public int CountFor(int id) => conveyorItemCountsByBlock.TryGetValue(new BlockHandle(id), out int count) ? count : -1;
    public int TotalItems => cachedLoadedConveyorItemCount;
    public int NativeReads => beltSimulation.Reads;
}

public static class Checks
{
    private static int assertions;
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }

    public static void Main()
    {
        var world = new TerrainGenerator();
        var block = new Block(7);
        block.SetLane(0, 10, 0);
        world.Publish(block, true);
        Require(world.IsTracked(7) && !world.IsDynamic(7), "settled item must be tracked as static");
        Require(world.CountFor(7) == -1 && world.TotalItems == 0, "native stock must not be mirrored into managed per-block count caches");
        Require(world.IsDirty(7) && !world.IsPersistenceDirty(7), "visual publication cannot perform per-block persistence invalidation");
        Require(block.LaneReads == 0, "publication summary must avoid all managed lane queries");

        world.ClearDirty(); block.ResetReads(); block.SetLane(0, 10, 25);
        world.Publish(block, false);
        Require(world.IsTracked(7) && world.IsDynamic(7) && world.IsDirty(7), "moving item must enter dynamic rendering");
        Require(block.LaneReads == 0, "motion-only publication must avoid managed lane queries");

        world.ClearDirty(); block.ResetReads(); block.SetLane(0, -1, 0);
        world.Publish(block, true);
        Require(!world.IsTracked(7) && !world.IsDynamic(7), "empty block must leave visual tracking");
        Require(world.CountFor(7) == -1 && world.TotalItems == 0 && world.IsDirty(7), "empty block must clear count and stale visuals");

        block.SetLane(1, 11, 0); world.Publish(block, true);
        world.ClearDirty(); block.SetLane(1, -1, 0); world.Defer(true);
        world.Publish(block, true);
        Require(world.IsTracked(7) && world.DeferredRefreshes == 1, "deferred activity refresh must retain current membership");
        Require(world.CountFor(7) == -1 && world.IsDirty(7), "deferred publication refreshes visual state without duplicating native stock");

        var untracked = new Block(8); untracked.SetLane(0, 12, 10);
        untracked.ResetReads(); world.Publish(untracked, false);
        Require(untracked.LaneReads == 0 && world.IsDirty(8), "untracked motion-only publication must avoid unnecessary lane reads");
        Require(block.IngressChecks == 0, "ordinary belt publication must never initialize or normalize floor storage");
        int events = 0; block.RuntimeItemStackChanged += _ => events++;
        world.Publish(block, false);
        Require(events == 1, "real stack event subscribers still receive native changes");
        block.floorStacks[0].Add(42); world.Publish(block, false);
        Require(block.IngressChecks == 1 && block.floorStacks[0].Count == 0, "real floor ingress stays immediate");
        for (int lane = 0; lane < 4; lane++) block.RecordBeltJobLaneChange(lane, true);
        Require(block.GetBeltJobLaneOccupancyVersion(0) == 1 && block.GetBeltJobLaneOccupancyVersion(3) == 1,
            "native lane occupancy versions advance independently without legacy arrays");
        block.RecordBeltJobLaneChange(0, false);
        Require(block.GetBeltJobLaneOccupancyVersion(0) == 1 && block.GetBeltJobLaneOccupancyVersion(-1) == 0,
            "motion-only and invalid lanes cannot change occupancy versions");
        block.Bound[1] = block.Bound[3] = false;
        int reads = world.NativeReads;
        block.ResetReads(); world.Publish(block, false);
        Require(block.LaneReads == 0 && world.NativeReads - reads == 2, "summary reads only bound native lanes without managed slot reads");
        // A synchronized batch exercises the real summary/publication/version methods.
        world = new TerrainGenerator();
        var bulk = new Block[100000];
        for (int i = 0; i < bulk.Length; i++)
        { bulk[i] = new Block(i); bulk[i].Bound[1] = bulk[i].Bound[3] = false; bulk[i].SetLane(0, 10, 25); world.Publish(bulk[i], true); }
        for (int i = 0; i < 1000; i++) { bulk[i].RecordBeltJobLaneChange(0, true); world.Publish(bulk[i], true); }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < bulk.Length; i++)
        { bulk[i].RecordBeltJobLaneChange(0, true); world.Publish(bulk[i], true); }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated; watch.Stop();
        int legacyReads = 0, ingress = 0;
        for (int i = 0; i < bulk.Length; i++) { legacyReads += bulk[i].LaneReads; ingress += bulk[i].IngressChecks; }
        Require(legacyReads == 0 && ingress == 0 && world.TotalItems == 0,
            "100k synchronized publications perform zero legacy reads/floor initialization/count mirroring");
        Require(allocated == 0, $"warmed 100k publication/version updates allocated {allocated} bytes");
        Console.WriteLine($"100k shared native publications: {watch.Elapsed.TotalMilliseconds:F1} ms; {allocated} B; legacy reads={legacyReads}, floor checks={ingress} (engine boundaries doubled)");
        Console.WriteLine($"PASS: {assertions} belt publication visual, persistence, motion and deferred-refresh checks.");
    }
}
