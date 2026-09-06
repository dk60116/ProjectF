using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

static class Time { public static float time; }
static class Mathf { public static int Min(int x, int y) => Math.Min(x, y); public static int Max(int x, int y) => Math.Max(x, y); }
static class MapObjectTickProfiler { public static bool IsEnabled; }
readonly struct Marker {
    public Scope Auto() => default;
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}
readonly record struct BlockHandle(int Id);
sealed class Block {
    public int Id;
    public bool HandleValid = true, Resolvable = true, Loaded = true, Active = true, ShouldTick = true;
    public bool Progressed, Executed = true;
    public int TickCount;
    public SchedulerFixture Owner;
    public Action<Block> OnTick;
    public bool ShouldTickActiveConveyor() => ShouldTick;
    public bool TickConveyor(float deltaTime, out bool executed) {
        TickCount++;
        Owner.Event(Id);
        OnTick?.Invoke(this);
        executed = Executed;
        return Progressed;
    }
}

abstract partial class SchedulerFixture {
    protected readonly Dictionary<int, Block> blocks = new();
    protected readonly HashSet<BlockHandle> conveyorDirectWakeBlocks = new();
    protected readonly HashSet<BlockHandle> conveyorCornerGroupWakeQueuedBlocks = new();
    protected readonly Dictionary<int, List<BlockHandle>> conveyorCornerGroupWakeBlocksById = new();
    protected readonly HashSet<int> conveyorCornerGroupWakeQueued = new();
    protected readonly Queue<int> conveyorCornerGroupWakeQueue = new();
    protected readonly Dictionary<int, ConveyorCornerGroup> conveyorCornerGroupsById = new();
    protected readonly Dictionary<BlockHandle, ConveyorCornerGroupSlot> conveyorCornerGroupSlots = new();
    protected readonly List<BlockHandle> conveyorCornerGroupTickBlocks = new();
    protected readonly Stack<List<BlockHandle>> conveyorCornerGroupWakeBlockPool = new();
    protected readonly Dictionary<int, ConveyorLineWakeRange> conveyorLineWakeRangesById = new();
    protected readonly Queue<int> conveyorLineWakeQueue = new();
    protected readonly Dictionary<int, ConveyorLineWakeRange> deferredConveyorLineWakeRangesById = new();
    protected readonly Queue<int> deferredConveyorLineWakeQueue = new();
    protected readonly Dictionary<int, ConveyorLineRetryState> conveyorLineRetryStatesById = new();
    protected readonly Dictionary<int, int> conveyorLineRetryAttemptsByDueLineId = new();
    protected int lastActiveConveyorCornerGroupBlocksQueued, lastActiveConveyorCornerGroupBlocksSkipped,
        lastActiveConveyorCornerGroupBlocksSelected, lastActiveConveyorCornerGroupBlocksProcessed,
        lastActiveConveyorDuplicateFrameTicksSkipped, lastActiveConveyorCornerGroupNoProgressRequeuesSkipped,
        lastActiveConveyorLineWakesDroppedByRetryThrottle, lastActiveConveyorDeferredLineWakesDroppedByRetryThrottle,
        lastActiveConveyorLineRetryRangeMerges;
    protected readonly Marker ConveyorCornerGroupCollectMarker = default, ConveyorCornerGroupTickMarker = default;
    public readonly List<int> Trace = new();
    public Action<Block> OnResolve;
    public bool TraceEnabled = true;
    public bool CacheDirty;
    public int BatchDepth;
    public int PoolCount => conveyorCornerGroupWakeBlockPool.Count;
    public int QueuedCorners => conveyorCornerGroupWakeQueue.Count;
    public Block Get(int id) => blocks[id];
    public abstract bool QueueCorner(Block block);
    public abstract bool TickCorner(int group, float deltaTime);
    public abstract void ClearCorner(int group);
    public abstract void ClearAllCorners();
    public abstract bool QueueLine(int id, ConveyorLineWakeRange range);
    public abstract void DeferLine(int id, ConveyorLineWakeRange range);
    public abstract int PromoteLines();
    public void Event(int value) { if (TraceEnabled) Trace.Add(value); }
    public void AddBlock(int id, int group = 1, int slot = 0) {
        blocks[id] = new Block { Id = id, Owner = this };
        SetGroup(id, group, slot);
    }
    public void SetGroup(int id, int group, int slot) {
        conveyorCornerGroupSlots[new BlockHandle(id)] = new ConveyorCornerGroupSlot(group, slot, 8, false);
        if (!conveyorCornerGroupsById.ContainsKey(group)) conveyorCornerGroupsById[group] = new ConveyorCornerGroup(group);
    }
    public void RemoveGroup(int group) => conveyorCornerGroupsById.Remove(group);
    public void MakeDirect(int id) {
        conveyorDirectWakeBlocks.Add(new BlockHandle(id));
        conveyorCornerGroupWakeQueuedBlocks.Remove(new BlockHandle(id));
    }
    public bool PopCorner(float delta = 0.016f) {
        if (conveyorCornerGroupWakeQueue.Count == 0) return false;
        int group = conveyorCornerGroupWakeQueue.Dequeue();
        conveyorCornerGroupWakeQueued.Remove(group);
        return TickCorner(group, delta);
    }
    public void SetRetry(int id, ConveyorLineWakeRange range, float retryTime, int attempts, bool ready) =>
        conveyorLineRetryStatesById[id] = new ConveyorLineRetryState(range, retryTime, attempts, ready);
    public void EraseDeferredRange(int id) => deferredConveyorLineWakeRangesById.Remove(id);
    protected bool TryGetRuntimeBlockHandle(Block block, out BlockHandle handle) {
        handle = new BlockHandle(block?.Id ?? 0);
        return block != null && block.HandleValid;
    }
    protected bool TryGetCachedConveyorCornerGroupSlot(Block block, out int group, out int slot, out int length, out bool cycle) {
        group = -1; slot = -1; length = 0; cycle = false;
        if (CacheDirty || !conveyorCornerGroupSlots.TryGetValue(new BlockHandle(block.Id), out var value)) return false;
        group = value.GroupId; slot = value.SlotIndex; length = value.GroupLength; cycle = value.IsCycle;
        return true;
    }
    protected bool TryResolveLoadedRuntimeBlock(BlockHandle handle, out Block block) {
        if (!blocks.TryGetValue(handle.Id, out block) || !block.Resolvable) return false;
        OnResolve?.Invoke(block);
        return true;
    }
    protected bool IsLoadedRuntimeBlock(Block block) => block.Loaded;
    protected bool IsActiveConveyor(Block block) => block.Active;
    protected void SetConveyorActive(Block block, bool active, bool queue) { block.Active = active; Event(-10000 - block.Id); }
    protected void QueueConveyorWake(Block block) { Event(-20000 - block.Id); QueueCorner(block); }
    protected void QueueConveyorNetworkSleepCheck(Block block) => Event(-30000 - block.Id);
    protected void BeginConveyorRuntimeRefreshBatch() { BatchDepth++; Event(-1); }
    protected void EndConveyorRuntimeRefreshBatch() { BatchDepth--; Event(-2); }
    protected static long BeginConveyorRuntimeSample(bool enabled) => 0;
    protected static void EndConveyorRuntimeSample(bool enabled, string key, string name, long timestamp) { }
    public bool PoolOwnershipValid() {
        var owners = new HashSet<List<BlockHandle>>();
        foreach (var list in conveyorCornerGroupWakeBlocksById.Values) if (!owners.Add(list)) return false;
        foreach (var list in conveyorCornerGroupWakeBlockPool) if (list.Count != 0 || !owners.Add(list)) return false;
        return true;
    }
    public string Snapshot() {
        var s = new StringBuilder();
        s.Append("cornerQueue:").AppendJoin(',', conveyorCornerGroupWakeQueue);
        s.Append(";groupSet:").AppendJoin(',', conveyorCornerGroupWakeQueued.OrderBy(x => x));
        s.Append(";queuedBlocks:").AppendJoin(',', conveyorCornerGroupWakeQueuedBlocks.Select(x => x.Id).OrderBy(x => x));
        s.Append(";direct:").AppendJoin(',', conveyorDirectWakeBlocks.Select(x => x.Id).OrderBy(x => x));
        s.Append(";tickBuffer:").AppendJoin(',', conveyorCornerGroupTickBlocks.Select(x => x.Id));
        foreach (var pair in conveyorCornerGroupWakeBlocksById.OrderBy(p => p.Key))
            s.Append(";pending:").Append(pair.Key).Append(':').AppendJoin(',', pair.Value.Select(x => x.Id));
        s.Append(";lines:").AppendJoin(',', conveyorLineWakeQueue);
        s.Append(";deferred:").AppendJoin(',', deferredConveyorLineWakeQueue);
        foreach (var pair in conveyorLineWakeRangesById.OrderBy(p => p.Key)) s.Append(";range:").Append(pair.Key).Append(':').Append(RangeText(pair.Value));
        foreach (var pair in deferredConveyorLineWakeRangesById.OrderBy(p => p.Key)) s.Append(";defRange:").Append(pair.Key).Append(':').Append(RangeText(pair.Value));
        foreach (var pair in conveyorLineRetryStatesById.OrderBy(p => p.Key))
            s.Append(";retry:").Append(pair.Key).Append(':').Append(RangeText(pair.Value.wakeRange)).Append(':')
                .Append(pair.Value.retryTime.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(pair.Value.attemptCount).Append(':').Append(pair.Value.readyDelay);
        foreach (var pair in conveyorLineRetryAttemptsByDueLineId.OrderBy(p => p.Key)) s.Append(";attempt:").Append(pair.Key).Append(':').Append(pair.Value);
        s.Append(";counters:").AppendJoin(',', new[] { lastActiveConveyorCornerGroupBlocksQueued,
            lastActiveConveyorCornerGroupBlocksSkipped, lastActiveConveyorCornerGroupBlocksSelected,
            lastActiveConveyorCornerGroupBlocksProcessed, lastActiveConveyorDuplicateFrameTicksSkipped,
            lastActiveConveyorCornerGroupNoProgressRequeuesSkipped, lastActiveConveyorLineWakesDroppedByRetryThrottle,
            lastActiveConveyorDeferredLineWakesDroppedByRetryThrottle, lastActiveConveyorLineRetryRangeMerges });
        foreach (var pair in blocks.OrderBy(p => p.Key)) s.Append(";block:").Append(pair.Key).Append(':').Append(pair.Value.Active).Append(':').Append(pair.Value.TickCount);
        s.Append(";trace:").AppendJoin(',', Trace).Append(";batch:").Append(BatchDepth);
        return s.ToString();
    }
    static string RangeText(ConveyorLineWakeRange value) => $"{value.minSlotIndex},{value.maxSlotIndex},{value.fullLine}";
}

static class Checks {
    static int assertions;
    static SchedulerFixture.ConveyorLineWakeRange Range(int min, int max, bool full = false) => new(min, max, full);
    static void Assert(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static void Same(SchedulerFixture old, SchedulerFixture current, string label) {
        string left = old.Snapshot(), right = current.Snapshot();
        Assert(left == right, $"{label}\nLEGACY {left}\nCURRENT {right}");
        Assert(current.PoolOwnershipValid(), label + ": pool aliases an owned or nonempty buffer");
    }
    static void Both(Action<SchedulerFixture> action, SchedulerFixture old, SchedulerFixture current, string label) {
        action(old); action(current); Same(old, current, label);
    }
    static void RetryCases() {
        var ranges = new[] { Range(2, 6), Range(3, 5), Range(0, 4), Range(5, 9), Range(10, 12), Range(0, int.MaxValue, true), Range(-2, -1) };
        float[] times = { 9f, 10f, 11f, float.PositiveInfinity, float.NaN };
        foreach (bool ready in new[] { false, true })
        foreach (bool existing in new[] { false, true })
        foreach (float due in times)
        foreach (var pending in ranges)
        foreach (var incoming in ranges)
        foreach (bool defer in new[] { false, true }) {
            var old = new LegacyScheduler(); var current = new CurrentScheduler();
            Time.time = 10f;
            if (existing) {
                old.SetRetry(7, pending, due, 5, ready); current.SetRetry(7, pending, due, 5, ready);
            }
            if (defer) Both(s => s.DeferLine(7, incoming), old, current, "defer retry combination");
            else {
                Assert(old.QueueLine(7, incoming) == current.QueueLine(7, incoming), "queue result");
                Same(old, current, "queue retry combination");
            }
            Both(s => { s.QueueLine(8, Range(0, 2)); s.DeferLine(9, Range(4, 6)); s.DeferLine(7, Range(5, 10)); }, old, current, "merge multiple pending ranges");
            Assert(old.PromoteLines() == current.PromoteLines(), "promotion count before due");
            Same(old, current, "promotion before due");
            Time.time = 12f;
            Both(s => { s.DeferLine(7, incoming); s.DeferLine(9, incoming); }, old, current, "defer after due");
            Assert(old.PromoteLines() == current.PromoteLines(), "promotion count after due");
            Same(old, current, "promotion after due");
        }
        {
            var old = new LegacyScheduler(); var current = new CurrentScheduler();
            Both(s => { s.QueueLine(-1, Range(0, 2)); s.DeferLine(0, Range(0, 2)); s.DeferLine(3, Range(0, 2)); s.EraseDeferredRange(3); }, old, current, "invalid ids and stale deferred id");
            Assert(old.PromoteLines() == current.PromoteLines(), "stale promotion"); Same(old, current, "stale ranges ignored");
        }
    }
    static void Setup(SchedulerFixture scheduler) {
        for (int i = 1; i <= 8; i++) scheduler.AddBlock(i, i <= 4 ? 1 : 2, (i - 1) % 4);
    }
    static void CornerCases() {
        for (int mode = 0; mode < 11; mode++) {
            var old = new LegacyScheduler(); var current = new CurrentScheduler(); Setup(old); Setup(current);
            Both(s => { foreach (int id in new[] { 3, 1, 4, 2, 1, 5, 8, 6, 7 }) s.QueueCorner(s.Get(id)); }, old, current, "corner enqueue and dedupe");
            void Configure(SchedulerFixture s) {
                switch (mode) {
                    case 0: break;
                    case 1: s.MakeDirect(2); s.QueueCorner(s.Get(2)); break;
                    case 2: s.Get(3).Resolvable = false; break;
                    case 3: s.Get(3).Loaded = false; s.Get(2).ShouldTick = false; break;
                    case 4: s.RemoveGroup(1); break;
                    case 5: s.SetGroup(3, 2, 6); break;
                    case 6: s.Get(4).Active = false; s.Get(1).Executed = false; break;
                    case 7: s.Get(2).Progressed = true; break;
                    case 8:
                        s.OnResolve = b => { s.OnResolve = null; s.QueueCorner(s.Get(4)); s.QueueCorner(s.Get(1)); }; break;
                    case 9:
                        s.Get(4).OnTick = b => { b.OnTick = null; s.QueueCorner(s.Get(1)); s.QueueCorner(s.Get(4)); s.QueueCorner(s.Get(7)); }; break;
                    case 10:
                        s.OnResolve = b => { s.OnResolve = null; s.ClearAllCorners(); s.QueueCorner(s.Get(4)); }; break;
                }
            }
            Both(Configure, old, current, "corner mode setup");
            for (int frame = 0; frame < 4; frame++) {
                Assert(old.PopCorner() == current.PopCorner(), "corner dequeue return"); Same(old, current, $"corner mode {mode} frame {frame}");
            }
            Both(s => s.ClearAllCorners(), old, current, "global corner clear");
            Both(s => { s.SetGroup(1, 1, 9); s.Get(1).HandleValid = true; s.QueueCorner(s.Get(1)); }, old, current, "group id reuse");
            Assert(old.PopCorner() == current.PopCorner(), "reused group result"); Same(old, current, "reused group tick");
        }
        {
            var old = new LegacyScheduler(); var current = new CurrentScheduler(); Setup(old); Setup(current);
            foreach (int id in new[] { 3, 1, 4, 2 }) { old.QueueCorner(old.Get(id)); current.QueueCorner(current.Get(id)); }
            old.PopCorner(); current.PopCorner();
            Assert(current.Trace.Where(x => x > 0).SequenceEqual(new[] { 4, 3, 2, 1 }), "downstream-first stable order");
            Same(old, current, "known sorted order");
            Both(s => { s.QueueCorner(null); s.Get(1).HandleValid = false; s.QueueCorner(s.Get(1)); s.CacheDirty = true; s.QueueCorner(s.Get(2)); }, old, current, "null invalid handle dirty topology");
        }
        foreach (float delta in new[] { 0f, -1f }) {
            var old = new LegacyScheduler(); var current = new CurrentScheduler(); Setup(old); Setup(current);
            Both(s => s.QueueCorner(s.Get(2)), old, current, "invalid delta setup");
            Assert(old.PopCorner(delta) == current.PopCorner(delta), "invalid delta result"); Same(old, current, "invalid delta cleanup");
        }
        {
            var old = new LegacyScheduler(); var current = new CurrentScheduler();
            Both(s => { for (int i = 1; i <= 300; i++) { s.AddBlock(i, i, 0); s.QueueCorner(s.Get(i)); } }, old, current, "300 concurrent groups");
            Both(s => s.ClearAllCorners(), old, current, "pool global clear");
            Assert(current.PoolCount == 256, "pool bound must be 256 buffers");
            Both(s => { for (int i = 1; i <= 300; i++) s.QueueCorner(s.Get(i)); }, old, current, "all groups requeued after pool limit");
            for (int i = 0; i < 300; i++) { Assert(old.PopCorner() == current.PopCorner(), "pool group result"); Same(old, current, "pool group order and contents"); }
        }
    }
    static long MeasureCornerAllocations(SchedulerFixture scheduler) {
        Setup(scheduler); scheduler.TraceEnabled = false;
        void Cycle() { for (int i = 1; i <= 8; i++) scheduler.QueueCorner(scheduler.Get(i)); scheduler.PopCorner(); scheduler.PopCorner(); }
        for (int i = 0; i < 100; i++) Cycle();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) Cycle();
        return GC.GetAllocatedBytesForCurrentThread() - start;
    }
    public static int Main() {
        RetryCases(); CornerCases();
        long oldBytes = MeasureCornerAllocations(new LegacyScheduler());
        long currentBytes = MeasureCornerAllocations(new CurrentScheduler());
        Assert(oldBytes > 0, "allocation baseline must detect discarded lists");
        Assert(currentBytes == 0, "warm corner dispatch must allocate zero managed bytes, got " + currentBytes);
        Console.WriteLine($"PASS {assertions} differential and ownership assertions.");
        Console.WriteLine($"10,000 warm cycles (two groups): legacy {oldBytes:N0} B, current {currentBytes:N0} B.");
        Console.WriteLine("Production queue/retry/corner methods extracted unchanged; scene handles, clock, profiler and block tick boundaries are managed doubles. No engine launched.");
        return 0;
    }
}
