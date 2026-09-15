using System;
using System.Collections.Generic;
using ProjectF.Conveyors;
using Unity.Profiling;
using UnityEngine;

public partial class TerrainGenerator
{
    private static readonly ProfilerMarker BeltJobsBakeMarker = new ProfilerMarker("Belt Jobs.Bake");
    private static readonly ProfilerMarker BeltJobsTickMarker = new ProfilerMarker("Belt Jobs.Tick");
    private static readonly ProfilerMarker BeltJobsScheduleMarker = new ProfilerMarker("Belt Jobs.Schedule");
    private static readonly ProfilerMarker BeltJobsCompleteMarker = new ProfilerMarker("Belt Jobs.Complete");
    private static readonly ProfilerMarker BeltJobsPublishMarker = new ProfilerMarker("Belt Jobs.Publish");
    private readonly BeltSimulationWorld beltSimulation = new BeltSimulationWorld();
    private BeltSimulationBuffers beltJobBuffers => beltSimulation.Buffers;
    private readonly List<BeltLaneId> beltJobNodes = new List<BeltLaneId>();
    // Legacy Block IO/geometry adapter only. Never used as runtime lane identity.
    private readonly List<Block> beltJobViews = new List<Block>();
    private readonly Dictionary<BeltLaneId, int> beltJobIndices = new Dictionary<BeltLaneId, int>();
    private readonly List<int> beltJobGroupIds = new List<int>();
    private readonly List<BeltGroupRange> beltJobRanges = new List<BeltGroupRange>();
    private readonly List<ConveyorRuntimeRecord> beltJobSplitters = new List<ConveyorRuntimeRecord>();
    private readonly Dictionary<ConveyorRuntimeRecord, int> beltJobSplitterIndices =
        new Dictionary<ConveyorRuntimeRecord, int>();
    private readonly List<ulong> beltJobFilterWords = new List<ulong>();
    private readonly List<BeltSplitterState> beltJobSplitterBuild = new List<BeltSplitterState>();
    private readonly Dictionary<BeltLaneId, BeltLaneState> beltJobRebuildStates = new Dictionary<BeltLaneId, BeltLaneState>();
    private readonly Dictionary<BeltLaneId, BeltLaneId> beltJobRebuildOrigins = new Dictionary<BeltLaneId, BeltLaneId>();
    private readonly Dictionary<BeltLaneId, BeltLaneId> beltJobRebuildCursors = new Dictionary<BeltLaneId, BeltLaneId>();
    private readonly Dictionary<BeltLaneId, BeltPendingWrite> beltJobPending = new Dictionary<BeltLaneId, BeltPendingWrite>();
    private readonly List<int> beltJobPendingIndices = new List<int>();
    private readonly List<BeltLaneId> beltJobUnindexedPending = new List<BeltLaneId>();
    private readonly List<(Block block, int lane)> beltJobBuildOrder = new List<(Block, int)>();
    private readonly HashSet<Block> beltJobPublishedBlocks = new HashSet<Block>();
    private readonly List<Block> beltJobPublishedOrder = new List<Block>();
    private bool beltJobsDirty = true, beltJobsPublishing;
    private long beltSimulationTick => beltSimulation.Tick;
    private int beltJobRebuildCount, beltJobLastTickMoves, beltJobLastTickChanged, beltJobLastFrameTicks;
    private int beltJobLastPendingApplied, beltJobLastPendingDiscarded, beltJobLastPublishedBlocks;
    private int beltJobLastRenderedFrame = -1;
    private readonly List<(long tick, Action callback)> beltPlacementCompletions = new List<(long, Action)>();
    private readonly List<Action> beltPlacementReadyCallbacks = new List<Action>();

    private struct BeltPendingWrite
    {
        internal bool Replace;
        internal bool UpdatePickupGate;
        internal long Hold;
        internal BeltSavedLane Restore;
        internal Block LegacySource;
    }

    private static BeltLaneId BeltId(Block block, int lane)
        => new BeltLaneId(block.Coordinate.x, block.Coordinate.y, lane);

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
            Mix(key.X); Mix(key.Y); Mix(key.Lane);
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

    internal void QueueBeltJobWrite(
        Block block,
        int lane,
        bool replace,
        long hold,
        bool updatePickupGate)
    {
        if (beltJobsPublishing || block == null || lane < 0 || lane >= Block.ConveyorCellItemUnit) return;
        var key = BeltId(block, lane);
        bool alreadyPending = beltJobPending.TryGetValue(key, out BeltPendingWrite pending);
        if (replace) { pending.Hold = 0; pending.Restore = null; }
        pending.LegacySource = block;
        pending.Replace |= replace;
        pending.UpdatePickupGate |= updatePickupGate;
        pending.Hold = Math.Max(pending.Hold, hold);
        beltJobPending[key] = pending;
        if (!alreadyPending) TrackBeltJobPending(key);
    }

    private void SetBeltJobPending(BeltLaneId key, BeltPendingWrite pending)
    {
        bool alreadyPending = beltJobPending.ContainsKey(key);
        beltJobPending[key] = pending;
        if (!alreadyPending) TrackBeltJobPending(key);
    }

    private void TrackBeltJobPending(BeltLaneId key)
    {
        if (beltJobIndices.TryGetValue(key, out int index)) beltJobPendingIndices.Add(index);
        else beltJobUnindexedPending.Add(key);
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
            beltJobLastTickMoves = beltJobLastTickChanged = 0;
            beltJobLastPendingApplied = beltJobLastPendingDiscarded = beltJobLastPublishedBlocks = 0;
            EnsureBeltJobs();
            FlushBeltJobWrites();
            if (beltJobBuffers != null && beltJobRanges.Count > 0)
            {
                // One job iteration per independent transport group. No Unity objects are captured.
                bool profileStages = MapObjectTickProfiler.IsEnabled;
                using (BeltJobsScheduleMarker.Auto())
                {
                    long scheduleStart = profileStages ? MapObjectTickProfiler.BeginSample() : 0L;
                    beltSimulation.Schedule();
                    if (profileStages)
                    {
                        MapObjectTickProfiler.EndNamedSample(
                            "Belt",
                            "BeltJobs",
                            "Belt Jobs Schedule",
                            scheduleStart);
                    }
                }

                using (BeltJobsCompleteMarker.Auto())
                {
                    long completeStart = profileStages ? MapObjectTickProfiler.BeginSample() : 0L;
                    beltSimulation.Complete();
                    if (profileStages)
                    {
                        MapObjectTickProfiler.EndNamedSample(
                            "Belt",
                            "BeltJobs",
                            "Belt Jobs Complete",
                            completeStart);
                    }
                }

                try { PublishBeltJobChanges(); }
                finally { beltSimulation.CommitStep(); }
            }
            if (beltJobBuffers == null || beltJobRanges.Count == 0) beltSimulation.Step();
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
        PromoteOrDiscardUnindexedBeltJobWrites();
        if (beltJobPendingIndices.Count == 0) return;

        // Job indices are baked in stable group/coordinate/lane order. Sorting the
        // small dirty set by index preserves determinism without sorting every lane.
        beltJobPendingIndices.Sort();
        beltJobsPublishing = true;
        try
        {
            for (int pendingIndex = 0; pendingIndex < beltJobPendingIndices.Count; pendingIndex++)
            {
                int index = beltJobPendingIndices[pendingIndex];
                if ((uint)index >= (uint)beltJobNodes.Count)
                {
                    continue;
                }

                var key = beltJobNodes[index];
                if (!beltJobPending.TryGetValue(key, out BeltPendingWrite request))
                {
                    continue;
                }

                BeltLaneState previous = beltJobBuffers.Lanes[index];
                BeltLaneState state = request.Restore != null ? RestoreBeltJobLane(index, request.Restore)
                    : request.LegacySource != null
                        ? request.LegacySource.CaptureBeltJobInput(
                            key.Lane,
                            previous,
                            request.Replace,
                            request.Hold,
                            request.UpdatePickupGate)
                        : previous;
                beltJobBuffers.Lanes[index] = state;
                int group = beltJobGroupIds[index];
                beltJobBuffers.GroupStates[group] = default;
                Block view = beltJobViews[index];
                view?.ReleaseBeltJobLegacyLaneView(key.Lane);
                RecordBeltJobLaneChange(index, previous.ItemId != state.ItemId);
                beltJobPending.Remove(key);
                beltJobLastPendingApplied++;
            }
        }
        finally { beltJobsPublishing = false; }
        beltJobPendingIndices.Clear();
        NotifyBeltJobPublishedBlocks();
    }

    private void PromoteOrDiscardUnindexedBeltJobWrites()
    {
        int retainedCount = 0;
        for (int i = 0; i < beltJobUnindexedPending.Count; i++)
        {
            var key = beltJobUnindexedPending[i];
            if (!beltJobPending.ContainsKey(key)) continue;
            if (beltJobIndices.TryGetValue(key, out int index))
            {
                beltJobPendingIndices.Add(index);
                continue;
            }

            Block source = beltJobPending[key].LegacySource;
            if (beltJobsDirty && (beltJobPending[key].Restore != null || source != null && source.IsRuntimeConveyor))
            {
                // A newly placed or reconnected lane can receive a hold/restore
                // before its new topology index exists. Preserve it for the bake.
                beltJobUnindexedPending[retainedCount++] = key;
                continue;
            }

            // A write made while topology was dirty is already present in the
            // managed Block state captured by the bake, except for holds/restores
            // retained above. A lane omitted by the completed topology has no native
            // destination and must not remain pending forever.
            beltJobPending.Remove(key);
            beltJobLastPendingDiscarded++;
        }

        if (retainedCount < beltJobUnindexedPending.Count)
        {
            beltJobUnindexedPending.RemoveRange(
                retainedCount,
                beltJobUnindexedPending.Count - retainedCount);
        }
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
                    Block originView = beltJobViews[state.Origin];
                    Vector3 position = originView != null ? originView.TransportLanePosition(origin.Lane)
                        : new Vector3(state.StartX, state.StartY, state.StartZ);
                    state.StartX = position.x; state.StartY = position.y; state.StartZ = position.z;
                }
                int cursor = beltJobBuffers.MergeCursor[i];
                if (cursor >= 0 && cursor < beltJobNodes.Count) beltJobRebuildCursors[key] = beltJobNodes[cursor];
                state.Origin = -1;
                beltJobRebuildStates[key] = state;
                if (beltJobViews[i] != null) beltJobViews[i].UnbindBeltJobs();
            }
            beltJobNodes.Clear(); beltJobViews.Clear(); beltJobIndices.Clear(); beltJobGroupIds.Clear(); beltJobRanges.Clear();
            beltJobSplitters.Clear(); beltJobSplitterIndices.Clear(); beltJobFilterWords.Clear(); beltJobSplitterBuild.Clear();
            EnsureBeltSplitGroups();
            beltJobBuildOrder.Clear();
            beltJobBuildOrder.AddRange(beltSplitLanes);
            foreach (Block block in beltSplitBlocks)
                for (int lane = 0; lane < Block.ConveyorCellItemUnit; lane++)
                    if (!beltSplitIndices.ContainsKey((block, lane))
                        && (beltJobRebuildStates.TryGetValue(BeltId(block, lane), out BeltLaneState detachedState)
                            ? detachedState.ItemId >= 0
                            : block.HasBeltJobStoredItem(lane)))
                        beltJobBuildOrder.Add((block, lane));
            // Native storage is laid out group-first, then by stable coordinate/lane order.
            beltJobBuildOrder.Sort((a, b) =>
            {
                int rootA = GetBeltJobRoot(a);
                int rootB = GetBeltJobRoot(b);
                return rootA != rootB ? rootA.CompareTo(rootB) : CompareBeltJobKeys(a, b);
            });
            int lastRoot = -1;
            foreach (var key in beltJobBuildOrder)
            {
                int root = GetBeltJobRoot(key);
                if (root != lastRoot)
                {
                    beltJobRanges.Add(new BeltGroupRange { Start = beltJobNodes.Count, MaxWaves = 2 });
                    lastRoot = root;
                }
                int group = beltJobRanges.Count - 1;
                BeltGroupRange range = beltJobRanges[group]; range.Count++; beltJobRanges[group] = range;
                BeltLaneId id = BeltId(key.block, key.lane);
                beltJobIndices.Add(id, beltJobNodes.Count); beltJobNodes.Add(id);
                beltJobViews.Add(key.block); beltJobGroupIds.Add(group);
            }
            beltJobBuildOrder.Clear();
            foreach (Block block in beltSplitBlocks) block.PrepareBeltJobStorage();
            for (int g = 0; g < beltJobRanges.Count; g++)
            {
                BeltGroupRange range = beltJobRanges[g]; range.SplitterStart = beltJobSplitters.Count;
                for (int i = range.Start; i < range.Start + range.Count; i++)
                {
                    var key = beltJobNodes[i];
                    ConveyorRuntimeRecord splitter = beltJobViews[i].GetBeltJobSplitter(key.Lane);
                    if (splitter == null || beltJobSplitterIndices.ContainsKey(splitter)) continue;
                    beltJobSplitterIndices.Add(splitter, beltJobSplitters.Count); beltJobSplitters.Add(splitter);
                    beltJobSplitterBuild.Add(BuildBeltJobSplitter(splitter)); range.SplitterCount++;
                }
                beltJobRanges[g] = range;
            }
            beltSimulation.Allocate(beltJobNodes.Count, beltJobRanges.Count, beltJobSplitters.Count, beltJobFilterWords.Count);
            for (int i = 0; i < beltJobFilterWords.Count; i++) beltJobBuffers.FilterBits[i] = beltJobFilterWords[i];
            for (int i = 0; i < beltJobSplitterBuild.Count; i++) beltJobBuffers.Splitters[i] = beltJobSplitterBuild[i];
            for (int i = 0; i < beltJobNodes.Count; i++)
            {
                var key = beltJobNodes[i];
                beltSimulation.AddLane(key);
                Block block = beltJobViews[i];
                block.BindBeltJobLane(this, key.Lane, i);
                BeltLaneState state = beltJobRebuildStates.TryGetValue(key, out BeltLaneState old)
                    ? old : block.CaptureBeltJobInput(key.Lane, BeltLaneState.Empty, true, 0, false);
                if (beltJobRebuildOrigins.TryGetValue(key, out var origin) && beltJobIndices.TryGetValue(origin, out int originIndex)) state.Origin = originIndex;
                beltJobBuffers.Lanes[i] = state;
                int group = beltJobGroupIds[i];
                beltJobBuffers.MergeCursor[i] = beltJobRebuildCursors.TryGetValue(key, out var cursorKey)
                    && beltJobIndices.TryGetValue(cursorKey, out int cursor) && beltJobGroupIds[cursor] == group
                    ? cursor : beltJobRanges[group].Start;
                beltSplitConnections.Clear(); block.AppendBeltJobConnections(key.Lane, beltSplitConnections);
                BeltLaneTopology route = new BeltLaneTopology { Target = -1, Alternate = -1, Splitter = -1,
                    Paused = block.RuntimeConveyorSpeed <= 0 ? 1 : 0 };
                for (int edge = 0; edge < beltSplitConnections.Count; edge++)
                {
                    var target = beltSplitConnections[edge];
                    if (!beltJobIndices.TryGetValue(BeltId(target.block, target.lane), out int destination)) continue;
                    if (beltJobGroupIds[destination] != group) throw new InvalidOperationException("Belt job edge crosses its group boundary.");
                    long duration = block.GetBeltJobDuration(key.Lane, target.block, target.lane);
                    if (edge == 0) { route.Target = destination; route.Duration = duration; }
                    else { route.Alternate = destination; route.AlternateDuration = duration; }
                    if (duration > 0)
                    {
                        BeltGroupRange range = beltJobRanges[group];
                        range.MaxWaves = Math.Max(range.MaxWaves, (int)(BeltSimulationJob.TickUnits / duration) + 2);
                        beltJobRanges[group] = range;
                    }
                }
                ConveyorRuntimeRecord splitter = beltJobViews[i].GetBeltJobSplitter(key.Lane);
                if (splitter != null && splitter.TryGetSplitterChannel(block.Coordinate, out int splitterInput))
                {
                    route.Splitter = beltJobSplitterIndices[splitter];
                    route.SplitterInput = splitterInput;
                }
                beltJobBuffers.Topology[i] = route;
            }
            for (int g = 0; g < beltJobRanges.Count; g++) beltJobBuffers.Groups[g] = beltJobRanges[g];
            beltSimulation.ValidateTopology();
            beltSplitConnections.Clear();
            beltJobRebuildStates.Clear(); beltJobRebuildOrigins.Clear(); beltJobRebuildCursors.Clear();
            beltJobsDirty = false;
            FlushBeltJobWrites();
            // Imported managed items are consumed once. Native lanes remain authoritative;
            // only presentation invalidations cross back to Block after this boundary.
            beltJobsPublishing = true;
            try
            {
                for (int i = 0; i < beltJobNodes.Count; i++)
                {
                    Block view = beltJobViews[i];
                    view?.ReleaseBeltJobLegacyLaneView(beltJobNodes[i].Lane);
                    RecordBeltJobLaneChange(i, false);
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

    private BeltSplitterState BuildBeltJobSplitter(ConveyorRuntimeRecord splitter)
    {
        BeltSplitterState state = splitter.CaptureBeltJobRouting();
        state.LeftInput = state.RightInput = state.LeftOutput = state.RightOutput = -1;
        for (int channel = 0; channel < 2; channel++)
        {
            Vector2Int cell = default;
            bool foundCell = false;
            IReadOnlyList<Vector2Int> coordinates = splitter.OccupiedCoordinates;
            for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
            {
                if (splitter.TryGetSplitterChannel(coordinates[coordinateIndex], out int resolvedChannel)
                    && resolvedChannel == channel)
                {
                    cell = coordinates[coordinateIndex];
                    foundCell = true;
                    break;
                }
            }

            if (!foundCell) continue;
            int input = beltJobIndices.TryGetValue(new BeltLaneId(cell.x, cell.y, 2), out int back) ? back : -1;
            int output = beltJobIndices.TryGetValue(new BeltLaneId(cell.x, cell.y, 0), out int front) ? front : -1;
            if (channel == 0) { state.LeftInput = input; state.LeftOutput = output; }
            else { state.RightInput = input; state.RightOutput = output; }
        }
        state.FilterStart = beltJobFilterWords.Count;
        IReadOnlyList<ulong> mask = splitter.SplitterItemFilterWords;
        if (splitter.HasSplitterItemFilter && mask != null)
        {
            for (int i = 0; i < mask.Count; i++) beltJobFilterWords.Add(mask[i]);
        }
        state.FilterWords = beltJobFilterWords.Count - state.FilterStart;
        return state;
    }

    private void PublishBeltJobChanges()
    {
        using (BeltJobsPublishMarker.Auto())
        {
            bool profileStage = MapObjectTickProfiler.IsEnabled;
            long publishStart = profileStage ? MapObjectTickProfiler.BeginSample() : 0L;
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
                        int index = beltJobBuffers.Changed[group.Start + c];
                        RecordBeltJobLaneChange(index, true);
                    }
                    if (state.Moves > 0)
                        for (int s = group.SplitterStart; s < group.SplitterStart + group.SplitterCount; s++)
                            beltJobSplitters[s].ApplyBeltJobRouting(beltJobBuffers.Splitters[s]);
                }
            }
            finally { beltJobsPublishing = false; }
            long notifyStart = profileStage ? MapObjectTickProfiler.BeginSample() : 0L;
            NotifyBeltJobPublishedBlocks();
            if (profileStage)
            {
                MapObjectTickProfiler.EndNamedSample(
                    "Belt",
                    "BeltJobs",
                    "Belt Jobs Publish Observers",
                    notifyStart);
                MapObjectTickProfiler.EndNamedSample(
                    "Belt",
                    "BeltJobs",
                    "Belt Jobs Publish",
                    publishStart);
            }
        }
    }

    private void RecordBeltJobLaneChange(int index, bool occupancyMayHaveChanged)
    {
        Block view = beltJobViews[index];
        if (view == null) return;
        view.RecordBeltJobLaneChange(beltJobNodes[index].Lane, occupancyMayHaveChanged);
        beltJobPublishedBlocks.Add(view);
    }

    private void NotifyBeltJobPublishedBlocks()
    {
        if (beltJobPublishedBlocks.Count == 0) return;

        // Native lane changes are complete before any observer can inspect them.
        beltJobPublishedOrder.Clear();
        foreach (Block block in beltJobPublishedBlocks) if (block != null) beltJobPublishedOrder.Add(block);
        beltJobPublishedBlocks.Clear();
        beltJobPublishedOrder.Sort(CompareBeltSplitBlocks);
        beltJobLastPublishedBlocks += beltJobPublishedOrder.Count;
        RobotArmWorld.Current?.Wake(beltJobPublishedOrder);
        InputOutputModule.WakeRuntimeModulesForChangedBlocks(beltJobPublishedOrder);
        foreach (Block block in beltJobPublishedOrder) block.NotifyBeltJobPublished(false);
        beltJobPublishedOrder.Clear();
    }

    internal bool TryReadBeltJobLane(Block block, int lane, out BeltLaneState state)
    {
        state = default;
        int index = block.BeltJobIndex(lane);
        if (beltJobBuffers == null || index < 0 || index >= beltJobNodes.Count) return false;
        state = beltJobBuffers.Lanes[index];
        if (!beltJobPending.TryGetValue(BeltId(block, lane), out BeltPendingWrite pending)) return true;
        if (pending.Restore != null)
        {
            state = pending.Restore.State;
            return true;
        }
        if (pending.LegacySource != null)
            state = pending.LegacySource.CaptureBeltJobInput(
                lane,
                state,
                pending.Replace,
                pending.Hold,
                pending.UpdatePickupGate);
        return true;
    }

    internal bool TryGetBeltJobVisualPosition(Block block, int lane, out Vector3 position)
    {
        position = default;
        if (!TryReadBeltJobLane(block, lane, out BeltLaneState state) || state.ItemId < 0) return false;
        double fraction = state.Origin >= 0 && beltJobBuffers.Topology[block.BeltJobIndex(lane)].Paused != 0
            ? 0 : MapObjectTickManager.SimulationInterpolationAlpha;
        float progress = state.Duration > 0 ? Mathf.Clamp01(1f - (float)((state.Remaining - fraction
            * BeltSimulationJob.TickUnits) / state.Duration)) : 1f;
        if (state.Origin >= 0 && state.Origin < beltJobNodes.Count && beltJobViews[state.Origin] != null)
        {
            var origin = beltJobNodes[state.Origin];
            position = beltJobViews[state.Origin].EvaluateBeltJobSegment(origin.Lane, block, lane, progress);
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
        foreach (Block view in beltJobViews) if (view != null) view.UnbindBeltJobs();
        beltSimulation.Dispose();
        beltJobNodes.Clear(); beltJobViews.Clear(); beltJobIndices.Clear(); beltJobGroupIds.Clear(); beltJobRanges.Clear();
        beltJobSplitters.Clear(); beltJobSplitterIndices.Clear(); beltJobFilterWords.Clear(); beltJobSplitterBuild.Clear();
        beltJobPending.Clear(); beltJobPendingIndices.Clear(); beltJobUnindexedPending.Clear();
        beltJobBuildOrder.Clear(); beltJobPublishedBlocks.Clear();
        beltJobPublishedOrder.Clear();
        beltJobRebuildStates.Clear(); beltJobRebuildOrigins.Clear(); beltJobRebuildCursors.Clear();
        beltJobsDirty = true;
        beltJobLastRenderedFrame = -1;
        beltJobRebuildCount = beltJobLastTickMoves = beltJobLastTickChanged = beltJobLastFrameTicks = 0;
        beltJobLastPendingApplied = beltJobLastPendingDiscarded = beltJobLastPublishedBlocks = 0;
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
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "LastTickPendingWritesApplied", beltJobLastPendingApplied);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "LastTickPendingWritesDiscarded", beltJobLastPendingDiscarded);
        MapObjectTickProfiler.AddRuntimeCounter("BeltJobs", "LastTickPublishedBlocks", beltJobLastPublishedBlocks);
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
