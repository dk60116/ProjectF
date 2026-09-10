using System;
using System.Collections.Generic;
using ProjectF.Conveyors;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

public partial class TerrainGenerator
{
    private static readonly ProfilerMarker BeltJobsBakeMarker = new ProfilerMarker("Belt Jobs.Bake");
    private static readonly ProfilerMarker BeltJobsTickMarker = new ProfilerMarker("Belt Jobs.Tick");
    private static readonly ProfilerMarker BeltJobsPublishMarker = new ProfilerMarker("Belt Jobs.Publish");
    private BeltSimulationBuffers beltJobBuffers;
    private readonly List<(Block block, int lane)> beltJobNodes = new List<(Block, int)>();
    private readonly Dictionary<(Block block, int lane), int> beltJobIndices = new Dictionary<(Block, int), int>();
    private readonly List<int> beltJobGroupIds = new List<int>();
    private readonly List<BeltGroupRange> beltJobRanges = new List<BeltGroupRange>();
    private readonly List<Spliterbelt> beltJobSplitters = new List<Spliterbelt>();
    private readonly Dictionary<Spliterbelt, int> beltJobSplitterIndices = new Dictionary<Spliterbelt, int>();
    private readonly List<ulong> beltJobFilterWords = new List<ulong>();
    private readonly List<BeltSplitterState> beltJobSplitterBuild = new List<BeltSplitterState>();
    private readonly Dictionary<(Block block, int lane), BeltLaneState> beltJobRebuildStates = new Dictionary<(Block, int), BeltLaneState>();
    private readonly Dictionary<(Block block, int lane), (Block block, int lane)> beltJobRebuildOrigins = new Dictionary<(Block, int), (Block, int)>();
    private readonly Dictionary<(Block block, int lane), (Block block, int lane)> beltJobRebuildCursors = new Dictionary<(Block, int), (Block, int)>();
    private readonly Dictionary<(Block block, int lane), BeltPendingWrite> beltJobPending = new Dictionary<(Block, int), BeltPendingWrite>();
    private readonly List<(Block block, int lane)> beltJobPendingOrder = new List<(Block, int)>();
    private readonly HashSet<Block> beltJobPublishedBlocks = new HashSet<Block>();
    private readonly List<Block> beltJobPublishedOrder = new List<Block>();
    private bool beltJobsDirty = true, beltJobsPublishing;
    private long beltSimulationTick;
    private int beltJobRebuildCount, beltJobLastTickMoves, beltJobLastTickChanged, beltJobLastFrameTicks;
    private int beltJobLastRenderedFrame = -1;
    private readonly List<(long tick, Action callback)> beltPlacementCompletions = new List<(long, Action)>();
    private readonly List<Action> beltPlacementReadyCallbacks = new List<Action>();

    private struct BeltPendingWrite { internal bool Replace; internal long Hold; internal BeltSavedLane Restore; }

    public long BeltSimulationTick => beltSimulationTick;
    public int BeltJobGroupCount => beltJobRanges.Count;
    public int BeltJobLaneCount => beltJobNodes.Count;
    // A future lockstep driver supplies ticks/commands explicitly instead of using frame pacing.
    public bool BeltSimulationExternallyClocked { get; set; }

    internal void WakeBeltJobBlock(Block block)
    {
        if (beltJobBuffers == null || block == null) return;
        for (int lane = 0; lane < Block.ConveyorCellItemUnit; lane++)
        {
            int index = block.BeltJobIndex(lane);
            if (index >= 0 && index < beltJobNodes.Count) beltJobBuffers.GroupStates[beltJobGroupIds[index]] = default;
        }
    }

    // Explicit diagnostic/checkpoint boundary, not a per-frame rendering call.
    // Includes quantized topology and authoritative state; excludes visual interpolation.
    public ulong ComputeBeltSimulationChecksum()
    {
        EnsureBeltJobs(); FlushBeltJobWrites();
        ulong hash = 14695981039346656037UL;
        void Mix(long value) { unchecked { hash = (hash ^ (ulong)value) * 1099511628211UL; } }
        Mix(beltSimulationTick); Mix(beltJobNodes.Count); Mix(beltJobRanges.Count);
        for (int i = 0; i < beltJobNodes.Count; i++)
        {
            var key = beltJobNodes[i]; BeltLaneState state = beltJobBuffers.Lanes[i];
            BeltLaneTopology route = beltJobBuffers.Topology[i];
            Mix(key.block.Coordinate.x); Mix(key.block.Coordinate.y); Mix(key.lane);
            Mix(state.ItemId); Mix(state.Remaining); Mix(state.Duration); Mix(state.Origin); Mix(state.GateBits);
            Mix(beltJobBuffers.MergeCursor[i]);
            Mix(route.Target); Mix(route.Alternate); Mix(route.Splitter); Mix(route.Paused);
            Mix(route.Duration); Mix(route.AlternateDuration);
        }
        for (int i = 0; i < beltJobSplitters.Count; i++)
        {
            BeltSplitterState splitter = beltJobBuffers.Splitters[i];
            Mix(splitter.NextInput); Mix(splitter.NextOutput); Mix(splitter.FilterOutput); Mix(splitter.WheelMask);
        }
        for (int i = 0; i < beltJobFilterWords.Count; i++) Mix(unchecked((long)beltJobBuffers.FilterBits[i]));
        return hash;
    }

    internal void QueueBeltPlacementCompletion(Action callback, float seconds)
    {
        if (callback == null) return;
        long units = BeltSimulationMath.Seconds(seconds);
        long ticks = Math.Max(1, (units + BeltSimulationJob.TickUnits - 1) / BeltSimulationJob.TickUnits);
        beltPlacementCompletions.Add((beltSimulationTick + ticks, callback));
    }

    internal void QueueBeltJobWrite(Block block, int lane, bool replace, long hold)
    {
        if (beltJobsPublishing || block == null || lane < 0 || lane >= Block.ConveyorCellItemUnit) return;
        var key = (block, lane);
        beltJobPending.TryGetValue(key, out BeltPendingWrite pending);
        if (replace) { pending.Hold = 0; pending.Restore = null; }
        pending.Replace |= replace;
        pending.Hold = Math.Max(pending.Hold, hold);
        beltJobPending[key] = pending;
    }

    private void TickManagedBeltSimulation()
    {
        if (IsConveyorRuntimeRefreshDeferred) return;
        EnsureBeltJobs();
        if (BeltSimulationExternallyClocked) return;
        if (beltJobLastRenderedFrame != Time.frameCount)
        {
            beltJobLastRenderedFrame = Time.frameCount;
            beltJobLastFrameTicks = 0;
        }

        StepBeltSimulation();
        beltJobLastFrameTicks++;
    }

    public void StepBeltSimulation()
    {
        if (!Application.isPlaying || !worldReadyForPresentation || IsConveyorRuntimeRefreshDeferred) return;
        using (BeltJobsTickMarker.Auto())
        {
            EnsureBeltJobs();
            FlushBeltJobWrites();
            beltJobLastTickMoves = beltJobLastTickChanged = 0;
            if (beltJobBuffers != null && beltJobRanges.Count > 0)
            {
                // One job iteration per independent transport group. No Unity objects are captured.
                JobHandle handle = beltJobBuffers.Job.Schedule(beltJobRanges.Count, 1);
                handle.Complete();
                PublishBeltJobChanges();
            }
            beltSimulationTick++;
            beltPlacementReadyCallbacks.Clear();
            for (int i = 0; i < beltPlacementCompletions.Count;)
            {
                if (beltPlacementCompletions[i].tick > beltSimulationTick) { i++; continue; }
                beltPlacementReadyCallbacks.Add(beltPlacementCompletions[i].callback);
                beltPlacementCompletions.RemoveAt(i);
            }
            foreach (Action callback in beltPlacementReadyCallbacks) callback();
            beltPlacementReadyCallbacks.Clear();
        }
    }

    private static int CompareBeltJobKeys((Block block, int lane) a, (Block block, int lane) b)
    {
        int compare = CompareBeltSplitBlocks(a.block, b.block);
        return compare != 0 ? compare : a.lane.CompareTo(b.lane);
    }

    private void FlushBeltJobWrites()
    {
        if (beltJobBuffers == null || beltJobPending.Count == 0) return;
        beltJobPendingOrder.Clear();
        foreach (var entry in beltJobPending) beltJobPendingOrder.Add(entry.Key);
        beltJobPendingOrder.Sort(CompareBeltJobKeys);
        beltJobsPublishing = true;
        try
        {
            foreach (var key in beltJobPendingOrder)
            {
                if (key.block == null) { beltJobPending.Remove(key); continue; }
                if (!beltJobIndices.TryGetValue(key, out int index))
                {
                    if (!key.block.IsRuntimeConveyor) beltJobPending.Remove(key);
                    continue;
                }
                BeltPendingWrite request = beltJobPending[key];
                BeltLaneState state = request.Restore != null ? RestoreBeltJobLane(index, request.Restore)
                    : key.block.CaptureBeltJobInput(key.lane, beltJobBuffers.Lanes[index], request.Replace, request.Hold);
                beltJobBuffers.Lanes[index] = state;
                int group = beltJobGroupIds[index];
                beltJobBuffers.GroupStates[group] = default;
                key.block.PublishBeltJobLane(key.lane, state);
                beltJobPublishedBlocks.Add(key.block);
                beltJobPending.Remove(key);
            }
        }
        finally { beltJobsPublishing = false; }
        beltJobPendingOrder.Clear();
        NotifyBeltJobPublishedBlocks();
    }

    private void EnsureBeltJobs()
    {
        if (!beltJobsDirty) return;
        using (BeltJobsBakeMarker.Auto())
        {
            beltJobRebuildCount++;
            FlushBeltJobWrites();
            beltJobRebuildStates.Clear(); beltJobRebuildOrigins.Clear(); beltJobRebuildCursors.Clear();
            for (int i = 0; i < beltJobNodes.Count; i++)
            {
                var key = beltJobNodes[i];
                BeltLaneState state = beltJobBuffers.Lanes[i];
                if (state.Origin >= 0 && state.Origin < beltJobNodes.Count)
                {
                    var origin = beltJobNodes[state.Origin];
                    beltJobRebuildOrigins[key] = origin;
                    Vector3 position = origin.block != null ? origin.block.TransportLanePosition(origin.lane) : Vector3.zero;
                    state.StartX = position.x; state.StartY = position.y; state.StartZ = position.z;
                }
                int cursor = beltJobBuffers.MergeCursor[i];
                if (cursor >= 0 && cursor < beltJobNodes.Count) beltJobRebuildCursors[key] = beltJobNodes[cursor];
                state.Origin = -1;
                beltJobRebuildStates[key] = state;
                if (key.block != null) key.block.UnbindBeltJobs();
            }
            beltJobBuffers?.Dispose(); beltJobBuffers = null;
            beltJobNodes.Clear(); beltJobIndices.Clear(); beltJobGroupIds.Clear(); beltJobRanges.Clear();
            beltJobSplitters.Clear(); beltJobSplitterIndices.Clear(); beltJobFilterWords.Clear(); beltJobSplitterBuild.Clear();
            EnsureBeltSplitGroups();
            beltJobPendingOrder.Clear();
            beltJobPendingOrder.AddRange(beltSplitLanes);
            foreach (Block block in beltSplitBlocks)
                for (int lane = 0; lane < Block.ConveyorCellItemUnit; lane++)
                    if (!beltSplitIndices.ContainsKey((block, lane)) && block.HasBeltJobStoredItem(lane))
                        beltJobPendingOrder.Add((block, lane));
            // Native storage is laid out group-first, then by stable coordinate/lane order.
            beltJobPendingOrder.Sort((a, b) =>
            {
                int rootA = GetBeltJobRoot(a);
                int rootB = GetBeltJobRoot(b);
                return rootA != rootB ? rootA.CompareTo(rootB) : CompareBeltJobKeys(a, b);
            });
            int lastRoot = -1;
            foreach (var key in beltJobPendingOrder)
            {
                int root = GetBeltJobRoot(key);
                if (root != lastRoot)
                {
                    beltJobRanges.Add(new BeltGroupRange { Start = beltJobNodes.Count, MaxWaves = 2 });
                    lastRoot = root;
                }
                int group = beltJobRanges.Count - 1;
                BeltGroupRange range = beltJobRanges[group]; range.Count++; beltJobRanges[group] = range;
                beltJobIndices.Add(key, beltJobNodes.Count); beltJobNodes.Add(key); beltJobGroupIds.Add(group);
            }
            beltJobPendingOrder.Clear();
            foreach (Block block in beltSplitBlocks) block.PrepareBeltJobStorage();
            for (int g = 0; g < beltJobRanges.Count; g++)
            {
                BeltGroupRange range = beltJobRanges[g]; range.SplitterStart = beltJobSplitters.Count;
                for (int i = range.Start; i < range.Start + range.Count; i++)
                {
                    var key = beltJobNodes[i];
                    Spliterbelt splitter = key.block.GetBeltJobSplitter(key.lane);
                    if (splitter == null || beltJobSplitterIndices.ContainsKey(splitter)) continue;
                    beltJobSplitterIndices.Add(splitter, beltJobSplitters.Count); beltJobSplitters.Add(splitter);
                    beltJobSplitterBuild.Add(BuildBeltJobSplitter(splitter)); range.SplitterCount++;
                }
                beltJobRanges[g] = range;
            }
            beltJobBuffers = new BeltSimulationBuffers(beltJobNodes.Count, beltJobRanges.Count, beltJobSplitters.Count, beltJobFilterWords.Count);
            for (int i = 0; i < beltJobFilterWords.Count; i++) beltJobBuffers.FilterBits[i] = beltJobFilterWords[i];
            for (int i = 0; i < beltJobSplitterBuild.Count; i++) beltJobBuffers.Splitters[i] = beltJobSplitterBuild[i];
            for (int i = 0; i < beltJobNodes.Count; i++)
            {
                var key = beltJobNodes[i];
                key.block.BindBeltJobLane(this, key.lane, i);
                BeltLaneState state = beltJobRebuildStates.TryGetValue(key, out BeltLaneState old)
                    ? old : key.block.CaptureBeltJobInput(key.lane, BeltLaneState.Empty, true, 0);
                if (beltJobRebuildOrigins.TryGetValue(key, out var origin) && beltJobIndices.TryGetValue(origin, out int originIndex)) state.Origin = originIndex;
                beltJobBuffers.Lanes[i] = state;
                int group = beltJobGroupIds[i];
                beltJobBuffers.MergeCursor[i] = beltJobRebuildCursors.TryGetValue(key, out var cursorKey)
                    && beltJobIndices.TryGetValue(cursorKey, out int cursor) && beltJobGroupIds[cursor] == group
                    ? cursor : beltJobRanges[group].Start;
                beltSplitConnections.Clear(); key.block.AppendBeltJobConnections(key.lane, beltSplitConnections);
                BeltLaneTopology route = new BeltLaneTopology { Target = -1, Alternate = -1, Splitter = -1,
                    Paused = key.block.RuntimeConveyorSpeed <= 0 ? 1 : 0 };
                for (int edge = 0; edge < beltSplitConnections.Count; edge++)
                {
                    var target = beltSplitConnections[edge];
                    if (!beltJobIndices.TryGetValue(target, out int destination)) continue;
                    if (beltJobGroupIds[destination] != group) throw new InvalidOperationException("Belt job edge crosses its group boundary.");
                    long duration = key.block.GetBeltJobDuration(key.lane, target.block, target.lane);
                    if (edge == 0) { route.Target = destination; route.Duration = duration; }
                    else { route.Alternate = destination; route.AlternateDuration = duration; }
                    if (duration > 0)
                    {
                        BeltGroupRange range = beltJobRanges[group];
                        range.MaxWaves = Math.Max(range.MaxWaves, (int)(BeltSimulationJob.TickUnits / duration) + 2);
                        beltJobRanges[group] = range;
                    }
                }
                Spliterbelt splitter = key.block.GetBeltJobSplitter(key.lane);
                if (splitter != null) { route.Splitter = beltJobSplitterIndices[splitter]; route.SplitterInput = splitter.GetChannel(key.block.Coordinate); }
                beltJobBuffers.Topology[i] = route;
            }
            for (int g = 0; g < beltJobRanges.Count; g++) beltJobBuffers.Groups[g] = beltJobRanges[g];
            beltSplitConnections.Clear();
            beltJobRebuildStates.Clear(); beltJobRebuildOrigins.Clear(); beltJobRebuildCursors.Clear();
            beltJobsDirty = false;
            FlushBeltJobWrites();
            // Importing old/materialized items also establishes the presentation cache once.
            beltJobsPublishing = true;
            try
            {
                for (int i = 0; i < beltJobNodes.Count; i++)
                {
                    var key = beltJobNodes[i]; key.block.PublishBeltJobLane(key.lane, beltJobBuffers.Lanes[i]);
                    beltJobPublishedBlocks.Add(key.block);
                }
            }
            finally { beltJobsPublishing = false; }
            NotifyBeltJobPublishedBlocks();
        }
    }

    private int GetBeltJobRoot((Block block, int lane) key)
    {
        int index = beltSplitIndices.TryGetValue(key, out int exact) ? exact : beltSplitIndices[(key.block, 0)];
        return beltSplitGraph.Representative(index);
    }

    private BeltSplitterState BuildBeltJobSplitter(Spliterbelt splitter)
    {
        BeltSplitterState state = splitter.CaptureBeltJobRouting();
        state.LeftInput = state.RightInput = state.LeftOutput = state.RightOutput = -1;
        for (int channel = 0; channel < 2; channel++)
        {
            if (!splitter.TryGetChannelCoordinate(channel, out Vector2Int cell) || !TryGetLoadedBlock(cell, out Block block)) continue;
            int input = beltJobIndices.TryGetValue((block, 2), out int back) ? back : -1;
            int output = beltJobIndices.TryGetValue((block, 0), out int front) ? front : -1;
            if (channel == 0) { state.LeftInput = input; state.LeftOutput = output; }
            else { state.RightInput = input; state.RightOutput = output; }
        }
        state.FilterStart = beltJobFilterWords.Count;
        List<ulong> mask = splitter.CaptureItemFilterMaskWords();
        if (splitter.IsItemFilterMaskInitialized && mask != null) beltJobFilterWords.AddRange(mask);
        state.FilterWords = beltJobFilterWords.Count - state.FilterStart;
        return state;
    }

    private void PublishBeltJobChanges()
    {
        using (BeltJobsPublishMarker.Auto())
        {
            beltJobsPublishing = true;
            try
            {
                for (int g = 0; g < beltJobRanges.Count; g++)
                {
                    BeltGroupRange group = beltJobRanges[g];
                    BeltGroupState state = beltJobBuffers.GroupStates[g];
                    beltJobLastTickMoves += state.Moves;
                    beltJobLastTickChanged += state.ChangedCount;
                    for (int c = 0; c < state.ChangedCount; c++)
                    {
                        int index = beltJobBuffers.Changed[group.Start + c]; var key = beltJobNodes[index];
                        key.block.PublishBeltJobLane(key.lane, beltJobBuffers.Lanes[index]);
                        beltJobPublishedBlocks.Add(key.block);
                    }
                    if (state.Moves > 0)
                        for (int s = group.SplitterStart; s < group.SplitterStart + group.SplitterCount; s++)
                            beltJobSplitters[s].ApplyBeltJobRouting(beltJobBuffers.Splitters[s]);
                }
            }
            finally { beltJobsPublishing = false; }
            NotifyBeltJobPublishedBlocks();
        }
    }

    private void NotifyBeltJobPublishedBlocks()
    {
        // All slot mirrors are committed before any observer can inspect them.
        beltJobPublishedOrder.Clear();
        foreach (Block block in beltJobPublishedBlocks) if (block != null) beltJobPublishedOrder.Add(block);
        beltJobPublishedBlocks.Clear();
        beltJobPublishedOrder.Sort(CompareBeltSplitBlocks);
        foreach (Block block in beltJobPublishedOrder) block.NotifyBeltJobPublished();
        beltJobPublishedOrder.Clear();
    }

    internal bool TryReadBeltJobLane(Block block, int lane, out BeltLaneState state)
    {
        state = default;
        int index = block.BeltJobIndex(lane);
        if (beltJobBuffers == null || index < 0 || index >= beltJobNodes.Count) return false;
        if (beltJobPending.TryGetValue((block, lane), out BeltPendingWrite pending)
            && (pending.Replace || pending.Hold > 0 || pending.Restore != null)) return false;
        state = beltJobBuffers.Lanes[index]; return true;
    }

    internal bool TryGetBeltJobVisualPosition(Block block, int lane, out Vector3 position)
    {
        position = default;
        if (!TryReadBeltJobLane(block, lane, out BeltLaneState state) || state.ItemId < 0) return false;
        double fraction = state.Origin >= 0 && beltJobBuffers.Topology[block.BeltJobIndex(lane)].Paused != 0
            ? 0 : MapObjectTickManager.SimulationInterpolationAlpha;
        float progress = state.Duration > 0 ? Mathf.Clamp01(1f - (float)((state.Remaining - fraction
            * BeltSimulationJob.TickUnits) / state.Duration)) : 1f;
        if (state.Origin >= 0 && state.Origin < beltJobNodes.Count)
        {
            var origin = beltJobNodes[state.Origin];
            position = origin.block.EvaluateBeltJobSegment(origin.lane, block, lane, progress);
        }
        else
        {
            position = Vector3.Lerp(new Vector3(state.StartX, state.StartY, state.StartZ), block.TransportLanePosition(lane), progress);
            if ((state.GateBits & 64) != 0) position.y += Mathf.Sin(progress * Mathf.PI) * 0.35f;
        }
        return true;
    }

    internal bool IsBeltJobLaneSleeping(Block block, int lane)
    {
        int index = block.BeltJobIndex(lane);
        return beltJobBuffers != null && index >= 0 && index < beltJobNodes.Count
            && beltJobBuffers.GroupStates[beltJobGroupIds[index]].Sleeping != 0;
    }

    private void ClearBeltJobs()
    {
        foreach (var node in beltJobNodes) if (node.block != null) node.block.UnbindBeltJobs();
        beltJobBuffers?.Dispose(); beltJobBuffers = null;
        beltJobNodes.Clear(); beltJobIndices.Clear(); beltJobGroupIds.Clear(); beltJobRanges.Clear();
        beltJobSplitters.Clear(); beltJobSplitterIndices.Clear(); beltJobFilterWords.Clear(); beltJobSplitterBuild.Clear();
        beltJobPending.Clear(); beltJobPendingOrder.Clear(); beltJobPublishedBlocks.Clear();
        beltJobPublishedOrder.Clear();
        beltJobRebuildStates.Clear(); beltJobRebuildOrigins.Clear(); beltJobRebuildCursors.Clear();
        beltSimulationTick = 0; beltJobsDirty = true;
        beltJobLastRenderedFrame = -1;
        beltJobRebuildCount = beltJobLastTickMoves = beltJobLastTickChanged = beltJobLastFrameTicks = 0;
        beltPlacementCompletions.Clear(); beltPlacementReadyCallbacks.Clear();
    }

    private void PublishBeltJobRuntimeCounters()
    {
        int sleeping = 0, largest = 0;
        for (int g = 0; g < beltJobRanges.Count; g++)
        {
            if (beltJobBuffers.GroupStates[g].Sleeping != 0) sleeping++;
            largest = Math.Max(largest, beltJobRanges[g].Count);
        }
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "Groups", beltJobRanges.Count);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "Lanes", beltJobNodes.Count);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "LargestGroupLanes", largest);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "SleepingGroups", sleeping);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "ActiveGroups", beltJobRanges.Count - sleeping);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "PendingWrites", beltJobPending.Count);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "LastTickTransfers", beltJobLastTickMoves);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "LastTickChangedLanes", beltJobLastTickChanged);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "FrameTicks", beltJobLastFrameTicks);
        MapObjectTickProfiler.AddRuntimeCounter(
            "BeltJobs",
            "BacklogTicks",
            (float)MapObjectTickManager.SimulationBacklogTicks);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "TopologyRebuilds", beltJobRebuildCount);
    }
}
