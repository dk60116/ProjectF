using System;
using System.Collections.Generic;
using UnityEngine;

// Queue insertion, range merging/defer/promotion, owned routing, and block-wake
// dispatch come from production. Legacy movement and external wake sources are
// deterministic doubles; this is not the full Unity frame scheduler.
static partial class WorldChecks
{
    static void WakeScheduling()
    {
        Time.time = 0; Time.frameCount = 0;
        var idle = new TerrainGenerator(102, 1);
        idle.Tick();
        for (int f = 1; f <= 100; f++)
        {
            Time.time = f * .02f; Time.frameCount = f; idle.Tick(); idle.DrainWakes();
            Check(idle.PendingWakes == 0, "empty run cannot generate wake work");
        }
        Check(idle.DirectDispatches == 0, "100 idle ticks require zero direct wake dispatches");
        Time.time = 0; Time.frameCount = 0;
        var polling = new TerrainGenerator(102, 1);
        polling.Tick();
        for (int f = 1; f <= 100; f++)
        {
            Time.time = f * .02f; Time.frameCount = f;
            polling.Tick(); polling.ReplayRemovedPortPolling(); polling.DrainWakes();
        }
        Check(polling.DirectDispatches == 200, "removed polling policy dispatches both idle ports every frame");

        // Full line with a blocked port remains quiet; taking from a packed
        // interior must still let it compact and free an inlet reservation.
        Time.time = 0; Time.frameCount = 0;
        var jam = new TerrainGenerator(12, 1);
        for (int b = 0; b < 12; b++) { jam.Blocks[b].Put(0, b * 2); jam.Blocks[b].Put(1, b * 2 + 1); }
        jam.Tick();
        for (int f = 1; f <= 100; f++)
        {
            Time.time = f * .02f; Time.frameCount = f; jam.Tick(); jam.DrainWakes();
        }
        Check(jam.DirectDispatches == 0 && jam.PendingWakes == 0, "unchanged jam generates no direct or deferred wakes");
        int jamCount = jam.Total;
        jam.Blocks[5].Take(0);
        for (int f = 101; f < 160; f++)
        {
            Time.time = f * .02f; Time.frameCount = f; jam.Tick(); jam.DrainWakes();
        }
        Check(jam.Total == jamCount - 1 && jam.Blocks[0].Id(1) < 0, "middle removal reopens inlet without periodic port wakes");

        Time.time = 0; Time.frameCount = 0;
        var ranges = new TerrainGenerator(102, 1);
        ranges.Tick();
        ranges.WakeRange(0, 2); ranges.WakeRange(0, 2);
        ranges.DrainWakes();
        Check(ranges.DirectDispatches == 1 && ranges.LastDispatched == 0, "coalesced local event wakes only inlet");
        ranges.WakeRange(99, 101); ranges.DrainWakes(); ranges.WakeRange(98, 101);
        ranges.DrainWakes();
        Check(ranges.DirectDispatches == 1 && ranges.DeferredWakes == 1, "later same-frame ranges merge without fan-out");
        Time.frameCount++; ranges.Tick(); ranges.DrainWakes();
        Check(ranges.DirectDispatches == 2 && ranges.LastDispatched == 101 && ranges.PendingWakes == 0, "deferred outlet event is preserved next frame");
        Time.frameCount++; ranges.Tick(); ranges.WakeRange(45, 55); ranges.DrainWakes();
        Check(ranges.DirectDispatches == 2, "interior-only event does not wake remote legacy ports");
        Time.frameCount++; ranges.Tick(); ranges.WakeRange(0, int.MaxValue, true); ranges.DrainWakes();
        Check(ranges.DirectDispatches == 4, "full topology wake reaches both ports");

        // Split runs retain unsupported intermediate legacy blocks. A local
        // event must reach that block without waking the distant line ends.
        ranges.Blocks[50].Put(0, 9001);
        Time.time += 2; Time.frameCount++; ranges.Tick(); ranges.DrainWakes();
        Check(ranges.Runs == 2, "split wake fixture has two runs");
        Time.frameCount++; ranges.Tick();
        int before = ranges.DirectDispatches;
        ranges.WakeRange(49, 51); ranges.DrainWakes();
        Check(ranges.DirectDispatches - before == 3, "local split event dispatches three legacy boundary blocks");
        Check(!ranges.DispatchedThisFrame.Contains(0) && !ranges.DispatchedThisFrame.Contains(101), "split wake stays local");

        // Supply into the back lane and drain only the output front lane so
        // production wake dispatch is required to advance both legacy ports.
        foreach (int direction in new[] { 1, -1 })
        {
            Time.time = 0; Time.frameCount = 0;
            var flow = new TerrainGenerator(12, direction) { SimulateLegacyPorts = true };
            flow.Tick();
            int supplied = 0, consumed = 0;
            for (int f = 1; f <= 4000; f++)
            {
                Time.time = f * .025f; Time.frameCount = f;
                if (supplied < 70 && (supplied < 35 || f > 2200) && flow.Blocks[0].Id(0) < 0)
                {
                    flow.Blocks[0].Put(0, ++supplied);
                    flow.QueueConveyorWake(flow.Blocks[0]);
                }
                flow.Tick(); flow.DrainWakes();
                Block outlet = flow.Blocks[11];
                if ((f < 300 || f > 800) && outlet.Id(1) >= 0 && outlet.Ready(1))
                {
                    Check(outlet.Id(1) == consumed + 1, "scheduled ports preserve FIFO");
                    outlet.Take(1); consumed++; flow.QueueConveyorWake(outlet);
                }
                flow.Tick(); flow.DrainWakes();
                Check(flow.Total + consumed == supplied, "queue-driven ports conserve every item, including duplicate frame ticks");
            }
            Check(consumed == 70, "queue-driven ports resume and drain after downstream blockage");
            Check(flow.PendingWakes == 0, "drained world stops scheduling ports");
        }
        Console.WriteLine("Wake regression: 100 idle ticks / removed port polling 200 dispatches -> current 0; 100 packed ticks / 0 dispatches; local ranges, same-frame deferral, split runs and blocked/idle/resumed ports PASS.");
    }
}

public partial class TerrainGenerator
{
    private readonly HashSet<int> conveyorLinesTickedThisFrame = new HashSet<int>();
    private readonly HashSet<BlockHandle> conveyorDirectWakeBlocks = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> conveyorCornerGroupWakeQueuedBlocks = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> conveyorWakeQueued = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> activeConveyors = new HashSet<BlockHandle>();
    private readonly Queue<BlockHandle> conveyorWakeQueue = new Queue<BlockHandle>();
    private readonly Queue<int> conveyorLineWakeQueue = new Queue<int>();
    private readonly Queue<int> deferredConveyorLineWakeQueue = new Queue<int>();
    private readonly Dictionary<int, ConveyorLineWakeRange> conveyorLineWakeRangesById = new Dictionary<int, ConveyorLineWakeRange>();
    private readonly Dictionary<int, ConveyorLineWakeRange> deferredConveyorLineWakeRangesById = new Dictionary<int, ConveyorLineWakeRange>();
    private int lastActiveConveyorLineWakesDroppedByRetryThrottle, lastActiveConveyorDeferredLineWakesDroppedByRetryThrottle;
    private int lastActiveConveyorBlockWakeLineFallbacks, lastActiveConveyorBlockWakeTicks, lastActiveConveyorDuplicateFrameTicksSkipped, lastActiveConveyorBlockNoProgressRequeuesSkipped;
    private int lastActiveConveyorDirectWakeInactiveSkips;
    private int wakeFrame = -1;
    internal int DirectDispatches, LastDispatched;
    internal bool SimulateLegacyPorts;
    internal readonly HashSet<int> DispatchedThisFrame = new HashSet<int>();
    internal int DeferredWakes => deferredConveyorLineWakeQueue.Count;
    internal int PendingWakes => conveyorWakeQueue.Count + conveyorLineWakeQueue.Count + DeferredWakes;
    private void BeginFrame()
    {
        if (wakeFrame == Time.frameCount) return;
        wakeFrame = Time.frameCount;
        conveyorLinesTickedThisFrame.Clear(); DispatchedThisFrame.Clear();
        PromoteDeferredConveyorLineWakes();
    }
    internal void WakeRange(int min, int max, bool full = false) => QueueConveyorLineWake(1, new ConveyorLineWakeRange(min, max, full));
    internal void ReplayRemovedPortPolling()
    {
        // Frozen reproduction of the removed two unconditional manager calls.
        foreach (var line in conveyorLines)
            if (line.transportRuns != null)
                foreach (var run in line.transportRuns)
                {
                    QueueConveyorDirectWake(run.Inlet);
                    QueueConveyorDirectWake(run.Outlet);
                }
    }
    internal void QueueConveyorWake(Block block)
    {
        if (!SimulateLegacyPorts || block.OwnsConveyorTransport) return;
        WakeRange(Math.Max(0, block.Index - 2), Math.Min(Blocks.Length - 1, block.Index + 2));
    }
    internal void DrainWakes()
    {
        int budget = 512;
        while (budget-- > 0 && (conveyorLineWakeQueue.Count > 0 || conveyorWakeQueue.Count > 0))
        {
            if (conveyorLineWakeQueue.Count > 0)
            {
                int id = conveyorLineWakeQueue.Dequeue();
                var range = conveyorLineWakeRangesById[id]; conveyorLineWakeRangesById.Remove(id);
                RouteOwnedConveyorLineWake(conveyorLines[0], range);
            }
            else
            {
                BlockHandle handle = conveyorWakeQueue.Dequeue(); conveyorWakeQueued.Remove(handle);
                DirectDispatches++; LastDispatched = handle.Index; DispatchedThisFrame.Add(handle.Index);
                ProcessQueuedConveyorBlockWake(handle, .025f);
            }
        }
        if (budget < 0) throw new Exception("Wake feedback exceeded production-sized frame budget");
    }
    private bool TryGetRuntimeBlockHandle(Block block, out BlockHandle handle) { handle = new BlockHandle(block.Index); return true; }
    private void ClearStraightConveyorLineRetry(Block block) { }
    private bool TryHandleStraightConveyorLineWakeRetry(int id, ConveyorLineWakeRange range, bool ready) => false;
    private void RemoveActiveConveyorHandle(BlockHandle handle) => activeConveyors.Remove(handle);
    private bool TryTickStraightConveyorLine(Block block) => RouteOwnedConveyorLineWake(conveyorLines[0], new ConveyorLineWakeRange(Math.Max(0, block.Index - 2), Math.Min(Blocks.Length - 1, block.Index + 2), false));
    private void SetConveyorActive(Block block, bool active, bool queueWake) { if (active) activeConveyors.Add(new BlockHandle(block.Index)); }
    private void BeginConveyorRuntimeRefreshBatch() { }
    private void EndConveyorRuntimeRefreshBatch() { }
    private void QueueConveyorNetworkSleepCheck(Block block) { }
}

public partial class Block
{
    internal TerrainGenerator World;
    internal int Index => index;
    private int tickFrame = -1;
    internal bool ShouldTickActiveConveyor() => World.SimulateLegacyPorts && RawCount > 0;
    internal bool TickConveyor(float deltaTime, out bool executed)
    {
        executed = tickFrame != Time.frameCount;
        if (!executed) return false;
        tickFrame = Time.frameCount;
        bool moved = false;
        if (Next != null && Next.OwnsConveyorTransport)
        {
            TryMoveIntoConveyorTransport(1, out bool accepted, out _, out _);
            moved |= accepted;
        }
        moved |= TryMoveStraightConveyorDataLaneToCached(this, 0, 1, .5f);
        return moved || (Id(0) >= 0 && !Ready(0)) || (Id(1) >= 0 && !Ready(1));
    }
}
