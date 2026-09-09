using System;
using System.Collections.Generic;

static class Application { public static bool isPlaying = true; }
static class Time { public static int frameCount; public static float time; }
static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
readonly record struct BlockHandle(int Id);

static class Checks
{
    static int assertions;
    internal static void Equal<T>(T actual, T expected, string message) where T : IEquatable<T>
    {
        assertions++;
        if (!actual.Equals(expected)) throw new Exception($"{message}: expected {expected}, actual {actual}");
    }
    public static void Main()
    {
        TerrainGenerator.CheckWakeIntent(); TerrainGenerator.CheckDirectDispatch(); Block.CheckPlanReuse();
        Console.WriteLine($"Slot scheduling: {assertions} assertions PASS; production deferred/direct dispatch and move/sleep cache methods, managed world and planner boundaries.");
    }
}

partial class TerrainGenerator
{
    internal static TerrainGenerator Active;
    int deferredConveyorRuntimeRefreshDepth;
    bool IsConveyorRuntimeRefreshDeferred => deferredConveyorRuntimeRefreshDepth > 0;
    readonly Dictionary<BlockHandle, bool> deferredConveyorNetworkWakeBlocks = new();
    readonly List<KeyValuePair<BlockHandle, bool>> deferredConveyorNetworkWakeBuffer = new();
    readonly Dictionary<BlockHandle, int> conveyorNetworkIds = new();
    readonly Dictionary<int, float> conveyorNetworkRetryTimes = new();
    readonly HashSet<int> conveyorNetworkSleepingIds = new(), conveyorNetworkSleepCheckQueuedIds = new();
    readonly HashSet<BlockHandle> conveyorDirectWakeBlocks = new(), conveyorCornerGroupWakeQueuedBlocks = new();
    readonly HashSet<BlockHandle> conveyorWakeQueued = new(), activeConveyors = new();
    readonly Queue<BlockHandle> conveyorWakeQueue = new();
    readonly Dictionary<BlockHandle, Block> blocks = new();
    int lastActiveConveyorDeferredNetworkWakeSuppressed, lastActiveConveyorDirectWakeInactiveSkips;
    int lastActiveConveyorBlockWakeLineFallbacks, lastActiveConveyorBlockWakeTicks;
    int lastActiveConveyorDuplicateFrameTicksSkipped, lastActiveConveyorBlockNoProgressRequeuesSkipped;
    int wakeCount, debugRefreshes;
    Action<Block> OnQueue;
    sealed class ConveyorLine { public readonly List<Block> Blocks = new(); }
    bool TryGetRuntimeBlockHandle(Block block, out BlockHandle handle) { handle = block.Handle; return blocks.ContainsKey(handle); }
    bool TryResolveLoadedRuntimeBlock(BlockHandle handle, out Block block) => blocks.TryGetValue(handle, out block);
    void EnsureConveyorNetworkCache() { }
    void RefreshSleepAwakeDebugVisualsForNetwork(int id) { debugRefreshes++; }
    void QueueConveyorWake(Block block) { wakeCount++; OnQueue?.Invoke(block); }
    internal void ClearConveyorBlockedLaneWaiter(Block block, int lane) { }
    internal void QueueConveyorVacancyWake(Block block) => QueueConveyorDirectWake(block);
    void ClearStraightConveyorLineRetry(Block block) { }
    bool TryTickStraightConveyorLine(Block block) => false;
    void RemoveActiveConveyorHandle(BlockHandle handle) => activeConveyors.Remove(handle);
    void SetConveyorActive(Block block, bool active, bool queueWake) { if (active) activeConveyors.Add(block.Handle); }
    void QueueConveyorNetworkSleepCheck(Block block) { }
    void FlushDeferredConveyorMoveAttemptWakeArounds() { }
    void FlushDeferredConveyorMoveAttemptWakeFlows() { }
    void FlushDeferredConveyorRuntimeRefreshes() { }
    static void ResolveStraightConveyorLineSlotRange(ConveyorLine line, ref int min, ref int max)
    { min = Math.Max(0, min); max = Math.Min(line.Blocks.Count - 1, max); }
    bool TryResolveConveyorLineBlock(ConveyorLine line, int index, out Block block)
    { block = line.Blocks[index]; return blocks.ContainsKey(block.Handle); }
    Block Add(int id)
    {
        Active = this;
        var block = new Block { Handle = new BlockHandle(id) };
        blocks.Add(block.Handle, block); conveyorNetworkIds.Add(block.Handle, id); return block;
    }
    void Dispatch(Block block)
    {
        QueueConveyorDirectWake(block);
        var handle = conveyorWakeQueue.Dequeue(); conveyorWakeQueued.Remove(handle);
        ProcessQueuedConveyorBlockWake(handle, .016f);
    }
    internal static void CheckWakeIntent()
    {
        // Exhaust every ordering of four refresh-only/real wake requests.
        for (int mask = 0; mask < 16; mask++)
        {
            var world = new TerrainGenerator(); var block = world.Add(1);
            world.conveyorNetworkSleepingIds.Add(1); world.conveyorNetworkRetryTimes[1] = 100;
            world.BeginConveyorRuntimeRefreshBatch(); world.BeginConveyorRuntimeRefreshBatch();
            for (int i = 0; i < 4; i++) world.WakeConveyorNetwork(block, (mask & (1 << i)) != 0);
            world.EndConveyorRuntimeRefreshBatch();
            Checks.Equal(world.wakeCount, 0, "nested batch cannot flush early");
            world.EndConveyorRuntimeRefreshBatch();
            Checks.Equal(world.wakeCount, mask == 0 ? 0 : 1, "merged intent is OR, independent of order");
            Checks.Equal(world.conveyorNetworkSleepingIds.Count, 0, "refresh-only still wakes network state");
            Checks.Equal(world.conveyorNetworkRetryTimes.Count, 0, "refresh-only still clears backoff");
            Checks.Equal(world.debugRefreshes, 1, "sleep visual transition happens once");
        }
        foreach (bool queue in new[] { false, true })
        {
            var world = new TerrainGenerator(); var block = world.Add(1); world.WakeConveyorNetwork(block, queue);
            Checks.Equal(world.wakeCount, queue ? 1 : 0, "non-batched intent unchanged");
        }
        var reentrant = new TerrainGenerator(); var first = reentrant.Add(1); var later = reentrant.Add(2);
        reentrant.OnQueue = _ => { reentrant.OnQueue = null; reentrant.QueueDeferredConveyorNetworkWake(later, true); };
        reentrant.BeginConveyorRuntimeRefreshBatch(); reentrant.WakeConveyorNetwork(first); reentrant.EndConveyorRuntimeRefreshBatch();
        Checks.Equal(reentrant.deferredConveyorNetworkWakeBlocks.Count, 1, "flush preserves a new pending request");
        reentrant.FlushDeferredConveyorNetworkWakes(); Checks.Equal(reentrant.wakeCount, 2, "new request delivered once");
        var stale = new TerrainGenerator(); var removed = stale.Add(1);
        stale.BeginConveyorRuntimeRefreshBatch(); stale.WakeConveyorNetwork(removed);
        stale.blocks.Remove(removed.Handle); stale.EndConveyorRuntimeRefreshBatch();
        Checks.Equal(stale.wakeCount, 0, "removed block cannot receive a stale wake");
        var warm = new TerrainGenerator(); var idle = warm.Add(1);
        for (int i = 0; i < 100; i++) { warm.BeginConveyorRuntimeRefreshBatch(); warm.WakeConveyorNetwork(idle, false); warm.EndConveyorRuntimeRefreshBatch(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            warm.BeginConveyorRuntimeRefreshBatch(); warm.WakeConveyorNetwork(idle, false);
            warm.WakeConveyorNetwork(idle, false); warm.EndConveyorRuntimeRefreshBatch();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Checks.Equal(allocated, 0L, "warmed merge/flush allocates zero bytes");
        Checks.Equal(warm.wakeCount, 0, "refresh-only never generates simulation work");
        Console.WriteLine($"Deferred wake intent: 10,000 warmed batches, {allocated} B allocation, 0 refresh-only enqueues.");
    }
    internal static void CheckDirectDispatch()
    {
        var world = new TerrainGenerator(); var source = world.Add(1); var previous = world.Add(2); var next = world.Add(3);
        source.Previous = previous; source.Next = next; next.Previous = source;
        source.Sleeping = previous.Sleeping = next.Sleeping = true;
        for (int i = 0; i < 100; i++) { Time.frameCount++; world.Dispatch(source); }
        Checks.Equal(source.TickCount, 0, "direct backend selection does not wake blocked items");
        Checks.Equal(source.Sleeping && previous.Sleeping && next.Sleeping, true, "neighbor sleep preserved");
        Checks.Equal(world.lastActiveConveyorDirectWakeInactiveSkips, 100, "unchanged direct work skipped");
        var line = new ConveyorLine(); line.Blocks.Add(source); line.Blocks.Add(previous); line.Blocks.Add(next);
        Checks.Equal(world.QueueStraightConveyorLineRetryWorkDirectFallback(line, 0, 2, null), 0, "item presence alone cannot enqueue fallback");
        // Real vacancy predecessor + waiter notification must resume the source;
        // topology lookup, render registration and item motion are doubles.
        next.HasItem = false; next.WakeConveyorVacatedLanePredecessor(0);
        Checks.Equal(source.Sleeping, false, "vacancy notification clears source sleep");
        Checks.Equal(previous.Sleeping, true, "vacancy does not force unrelated upstream sleep clear");
        source.Progress = true;
        Time.frameCount++; world.Dispatch(source);
        Checks.Equal(source.TickCount, 1, "external readiness resumes direct processing");
        Checks.Equal(world.wakeCount, 1, "progressing block remains scheduled");
        source.Ready = false; source.Motion = true; Time.frameCount++; world.Dispatch(source);
        Checks.Equal(source.TickCount, 2, "pending motion still ticks");
        source.Motion = false; source.Ready = true;
        Checks.Equal(world.QueueStraightConveyorLineRetryWorkDirectFallback(line, 0, 2, null), 1, "only executable fallback retained");
        source.OwnsConveyorTransport = true; world.conveyorWakeQueue.Clear(); world.conveyorWakeQueued.Clear();
        Time.frameCount++; world.Dispatch(source);
        Checks.Equal(source.TickCount, 2, "owned transport bypasses legacy movement");
        Console.WriteLine("Direct dispatcher: 100 blocked requests, 0 ticks and 0 neighbor sleep clears; ready/motion/owned routing PASS.");
    }
}

partial class Block
{
    internal BlockHandle Handle;
    internal Block Previous, Next;
    internal bool Sleeping { get => conveyorRuntimeArrays.LaneBlockedSleepStates[0]; set => conveyorRuntimeArrays.LaneBlockedSleepStates[0] = value; }
    internal bool Motion, Progress, OwnsConveyorTransport;
    internal int TickCount;
    internal bool HasItem = true, Ready = true, PlanPossible, AllowWhenIgnore, CachedFailure;
    internal int MovePlans, QueryPlans, Applied;
    internal int GetRuntimeConveyorItemCount() => HasItem ? 1 : 0;
    // Reproduces the removed dispatch side effect if that call is reintroduced.
    internal void WakeConveyorMoveAttemptsAlongRuntimeFlowImmediate(bool queueWake)
    { foreach (Block block in new[] { this, Previous, Next }) if (block != null) block.Sleeping = false; }
    internal bool TickConveyor(float dt, out bool executed) { executed = true; TickCount++; return Progress; }
    const int ConveyorStackLaneLimit = 1;
    static int conveyorCanMoveGlobalStateVersion = 1, conveyorPlanFailureGlobalStateVersion = 1;
    float nextConveyorMoveAttemptTime;
    readonly HashSet<ConveyorLaneKey> conveyorMoveVisiting = new(), conveyorCanMoveVisiting = new();
    readonly List<ConveyorLaneMove> conveyorPlannedMoves = new();
    ConveyorRuntimeArrays conveyorRuntimeArrays = new();
    sealed class ConveyorRuntimeArrays
    {
        internal readonly bool[] CanMoveCacheValid = new bool[2], CanMoveCacheResults = new bool[2], LaneBlockedSleepStates = new bool[1];
        internal readonly int[] CanMoveCacheFrames = new int[2], CanMoveCacheVersions = new int[2];
        internal readonly float[] NextLaneMoveAttemptTimes = new float[1];
    }
    readonly record struct ConveyorLaneKey(Block Block, int Lane);
    readonly record struct ConveyorLaneMove(int Value);
    ConveyorRuntimeArrays EnsureConveyorRuntimeArrays() => conveyorRuntimeArrays;
    bool IsValidConveyorLaneIndex(int lane) => lane == 0;
    int GetConveyorLaneCount() => 1;
    bool IsConveyorNetworkMoveAttemptThrottled() => false;
    bool IsConveyorStackingEnabled() => true;
    bool HasPortableConveyorMotionStates() => Motion;
    bool HasActiveVirtualConveyorDataMotion() => false;
    bool HasConveyorDataMotionStates() => false;
    bool ShouldThrottleBlockedConveyorMoveAttempts() => false;
    bool IsConveyorLaneMoveAttemptThrottled(int lane) => conveyorRuntimeArrays.NextLaneMoveAttemptTimes[lane] > Time.time;
    bool HasConveyorItemAtLane(int lane) => HasItem;
    bool WasConveyorItemMovedThisFrame(int lane) => false;
    bool IsConveyorItemReadyToMoveAtLane(int lane) => Ready;
    bool IsConveyorItemSettledAtLane(int lane) => Ready;
    bool IsConveyorLaneBlockedSleep(int lane) => conveyorRuntimeArrays.LaneBlockedSleepStates[lane];
    bool IsConveyorLaneCycleBlockedSleep(int lane) => false;
    void ClearConveyorLaneBlockedSleep(int lane) => conveyorRuntimeArrays.LaneBlockedSleepStates[lane] = false;
    void ClearConveyorLaneCycleBlockedSleep(int lane) { }
    bool ShouldKeepSoloConveyorLaneAwake(int lane) => false;
    bool HasCurrentConveyorDestinationBlockedPlanFailure(int lane) => false;
    bool ClearConveyorPlanFailureCache(int lane) { bool had = CachedFailure; CachedFailure = false; return had; }
    void RefreshConveyorActivityRegistration(bool queueWake) { }
    void RefreshSleepAwakeDebugVisuals() { }
    bool CanMoveConveyorLaneDirect(int lane) => Next != null && !Next.HasItem && Ready;
    bool TryGetConveyorSuccessor(int lane, out Block block, out int destinationLane, out bool corner)
    { block = Next; destinationLane = 0; corner = false; return block != null; }
    bool TryGetConveyorPredecessor(int lane, out Block block, out int sourceLane, out bool corner)
    { block = Previous; sourceLane = 0; corner = false; return block != null; }
    bool TryMoveIntoConveyorTransport(int lane, out bool moved, out Block destination, out int to)
    { moved = false; destination = null; to = -1; return false; }
    bool TryMoveConveyorLaneRunToOpenDestination(int lane, bool ignore, out Block destination, out int to)
    { destination = null; to = -1; return false; }
    bool TryGetCachedConveyorPlanFailure(int lane, bool ignore, out float delay) { delay = .08f; return CachedFailure; }
    float GetConveyorBlockedRetryDelay() => .08f;
    bool TryPlanConveyorLaneMove(ConveyorLaneKey root, ConveyorLaneKey current, HashSet<ConveyorLaneKey> visiting,
        List<ConveyorLaneMove> planned, bool ignore, bool markBlockedCycles = true, bool recordPlannedMoves = true,
        bool countPlanCall = true, bool cacheFailures = true)
    {
        if (countPlanCall) MovePlans++; else QueryPlans++;
        bool possible = PlanPossible || (AllowWhenIgnore && ignore);
        if (possible && recordPlannedMoves) planned.Add(new ConveyorLaneMove(1));
        return possible;
    }
    static void ResolvePlannedConveyorMoveDestination(List<ConveyorLaneMove> moves, Block source, int lane, out Block destination, out int to)
    { destination = source; to = lane; }
    void ApplyPlannedConveyorLaneMoves(List<ConveyorLaneMove> moves) { Applied++; InvalidateConveyorCanMoveCaches(); }
    internal static void CheckPlanReuse()
    {
        TerrainGenerator.Active = null;
        Time.time = 0; Time.frameCount = 1;
        var blocked = new Block();
        Checks.Equal(blocked.TryMoveConveyorLaneCore(0, out _, out _, true), false, "blocked planner fails");
        Checks.Equal(blocked.SleepConveyorMoveAttempts(), true, "failed slot sleeps");
        Checks.Equal(blocked.ClearMovableConveyorLaneSleepStates(), false, "same failure cannot wake immediately");
        Checks.Equal(blocked.MovePlans, 1, "one actual plan");
        Checks.Equal(blocked.QueryPlans, 0, "sleep/wake reuse failure without read-only planning");
        Time.frameCount++;
        Checks.Equal(blocked.CanMoveConveyorLane(0, true), false, "frame change reevaluates");
        Checks.Equal(blocked.QueryPlans, 1, "existing frame guard retained");
        blocked.PlanPossible = true; InvalidateConveyorCanMoveCaches();
        Checks.Equal(blocked.ClearMovableConveyorLaneSleepStates(), true, "dependency change wakes slot");
        Checks.Equal(blocked.TryMoveConveyorLaneCore(0, out _, out _, true), true, "changed destination moves");
        Checks.Equal(blocked.Applied, 1, "successful movement applies");
        var modes = new Block { AllowWhenIgnore = true };
        Checks.Equal(modes.TryMoveConveyorLaneCore(0, out _, out _, false), false, "normal mode fails");
        Checks.Equal(modes.CanMoveConveyorLane(0, true), true, "failure cannot poison other throttle mode");
        var cached = new Block { CachedFailure = true };
        Checks.Equal(cached.TryMoveConveyorLaneCore(0, out _, out _, true), false, "cached planner failure retained");
        Checks.Equal(cached.SleepConveyorMoveAttempts(), true, "cached failure sleeps");
        Checks.Equal(cached.MovePlans + cached.QueryPlans, 0, "cached failure needs no extra planning");
        var waiting = new Block { Ready = false, PlanPossible = true };
        Checks.Equal(waiting.TryMoveConveyorLaneCore(0, out _, out _, true), false, "not-ready early exit");
        waiting.Ready = true;
        Checks.Equal(waiting.CanMoveConveyorLane(0, true), true, "early exit is not a cached plan failure");
        var changed = new Block(); changed.TryMoveConveyorLaneCore(0, out _, out _, true);
        changed.PlanPossible = true; InvalidateConveyorCanMoveCaches(false);
        Checks.Equal(changed.CanMoveConveyorLane(0, true), true, "same-frame state invalidation retained");
        var repeated = new Block();
        for (int i = 0; i < 100; i++)
        {
            Time.frameCount++; repeated.ClearConveyorLaneBlockedSleep(0);
            repeated.TryMoveConveyorLaneCore(0, out _, out _, true);
            repeated.SleepConveyorMoveAttempts(); repeated.ClearMovableConveyorLaneSleepStates();
        }
        Checks.Equal(repeated.MovePlans, 100, "100 actual attempts");
        Checks.Equal(repeated.QueryPlans, 0, "100 cycles add zero query plans");
        Console.WriteLine("Failure reuse: 100 actual failed plans, 0 additional sleep/wake query plans; mutation/frame/mode/readiness guards PASS.");
    }
}
