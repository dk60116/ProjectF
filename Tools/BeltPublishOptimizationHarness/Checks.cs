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
    private const int ConveyorStackLaneLimit = 4;
    private readonly BeltLaneState[] lanes = new BeltLaneState[ConveyorStackLaneLimit];
    public int Id { get; }
    public int LaneReads { get; private set; }
    public bool Stacking = true, Virtual = true, HasLegacyMotion;
    public Block(int id)
    {
        Id = id;
        for (int i = 0; i < lanes.Length; i++) lanes[i].ItemId = -1;
    }
    public void SetLane(int lane, int itemId, long remaining)
        => lanes[lane] = new BeltLaneState { ItemId = itemId, Remaining = remaining };
    public void ResetReads() => LaneReads = 0;
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

public partial class TerrainGenerator
{
    private readonly HashSet<BlockHandle> conveyorItemVisualBlocks = new();
    private readonly HashSet<BlockHandle> conveyorItemVisualDirtyBlocks = new();
    private readonly List<BlockHandle> dynamicConveyorItemVisualBlocks = new();
    private readonly Dictionary<BlockHandle, int> dynamicConveyorItemVisualBlockIndices = new();
    private readonly Dictionary<BlockHandle, int> conveyorItemCountsByBlock = new();
    private readonly HashSet<BlockHandle> persistenceDirtyBlocks = new();
    private bool persistenceDirtyTrackingReady = true;
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
    public void Publish(Block block, bool refreshActivity) => MarkBeltJobItemVisualDirty(block, refreshActivity);
    public void Defer(bool value) => IsConveyorRuntimeRefreshDeferred = value;
    public void ClearDirty() => conveyorItemVisualDirtyBlocks.Clear();
    public bool IsTracked(int id) => conveyorItemVisualBlocks.Contains(new BlockHandle(id));
    public bool IsDynamic(int id) => dynamicConveyorItemVisualBlockIndices.ContainsKey(new BlockHandle(id));
    public bool IsDirty(int id) => conveyorItemVisualDirtyBlocks.Contains(new BlockHandle(id));
    public bool IsPersistenceDirty(int id) => persistenceDirtyBlocks.Contains(new BlockHandle(id));
    public int CountFor(int id) => conveyorItemCountsByBlock.TryGetValue(new BlockHandle(id), out int count) ? count : -1;
    public int TotalItems => cachedLoadedConveyorItemCount;
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
        Require(world.CountFor(7) == 1 && world.TotalItems == 1, "settled item count must be cached once");
        Require(world.IsDirty(7) && world.IsPersistenceDirty(7), "publication must dirty rendering and persistence");
        Require(block.LaneReads == 4, "publication must capture item count and motion in one lane pass");

        world.ClearDirty(); block.ResetReads(); block.SetLane(0, 10, 25);
        world.Publish(block, false);
        Require(world.IsTracked(7) && world.IsDynamic(7) && world.IsDirty(7), "moving item must enter dynamic rendering");
        Require(block.LaneReads == 4, "motion-only publication must use one lane pass");

        world.ClearDirty(); block.ResetReads(); block.SetLane(0, -1, 0);
        world.Publish(block, true);
        Require(!world.IsTracked(7) && !world.IsDynamic(7), "empty block must leave visual tracking");
        Require(world.CountFor(7) == -1 && world.TotalItems == 0 && world.IsDirty(7), "empty block must clear count and stale visuals");

        block.SetLane(1, 11, 0); world.Publish(block, true);
        world.ClearDirty(); block.SetLane(1, -1, 0); world.Defer(true);
        world.Publish(block, true);
        Require(world.IsTracked(7) && world.DeferredRefreshes == 1, "deferred activity refresh must retain current membership");
        Require(world.CountFor(7) == 0 && world.IsDirty(7), "deferred publication must still refresh current visual state");

        var untracked = new Block(8); untracked.SetLane(0, 12, 10);
        untracked.ResetReads(); world.Publish(untracked, false);
        Require(untracked.LaneReads == 0 && world.IsDirty(8), "untracked motion-only publication must avoid unnecessary lane reads");
        Require(MapObjectTickProfiler.ActivityRefreshes == 3, "only immediate activity refreshes must be counted");
        Console.WriteLine($"PASS: {assertions} belt publication visual, persistence, motion and deferred-refresh checks.");
    }
}
