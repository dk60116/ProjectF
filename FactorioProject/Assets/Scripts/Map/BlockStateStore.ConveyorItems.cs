using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    private const int ConveyorStackStateSentinel = -1000000002;
    private const float ConveyorBackgroundSimulationEpsilon = 0.0001f;
    private const int ConveyorBackgroundSimulationPassMultiplier = 64;
    private const int ConveyorBackgroundDefaultSimulationPasses = 256;
    private const double ConveyorBackgroundTopologyRetrySeconds = 8.0;
    private const float VirtualConveyorLanePathLength = 0.5f;
    private const int ConveyorSingleLineFrontLaneIndex = 0;
    private const int ConveyorSingleLineBackLaneIndex = 2;
    private static readonly Vector2Int[] SavedConveyorExternalWakeOffsets =
    {
        Vector2Int.zero,
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private sealed class ConveyorLaneLinkSaveState
    {
        public int sourceLaneIndex = -1;
        public Vector2Int destinationCoordinate;
        public int destinationLaneIndex = -1;
        public float pathLength;
    }

    private sealed class ConveyorItemBlockState
    {
        public int laneCount;
        public float conveyorSpeed;
        public long lastBackgroundSimulationTicks;
        public bool hasVirtualTopology;
        public readonly List<ConveyorItemLaneSaveState> lanes = new List<ConveyorItemLaneSaveState>();
        public readonly List<ConveyorLaneLinkSaveState> laneLinks = new List<ConveyorLaneLinkSaveState>();
    }

    private readonly struct ConveyorLaneKey : IEquatable<ConveyorLaneKey>
    {
        public readonly Vector2Int coordinate;
        public readonly int laneIndex;

        public ConveyorLaneKey(Vector2Int coordinate, int laneIndex)
        {
            this.coordinate = coordinate;
            this.laneIndex = laneIndex;
        }

        public bool Equals(ConveyorLaneKey other)
        {
            return coordinate == other.coordinate && laneIndex == other.laneIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is ConveyorLaneKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (coordinate.GetHashCode() * 397) ^ laneIndex;
            }
        }
    }

    private struct ConveyorSimulationItem
    {
        public Vector2Int coordinate;
        public ConveyorItemLaneSaveState lane;
        public float budgetDistance;
    }

    private enum ConveyorSimulationMoveResult
    {
        Moved,
        NotReady,
        BlockedByOccupiedDestination,
        InvalidSource,
        InvalidTopology,
        InvalidDestination
    }

    private readonly struct ConveyorScheduledLane
    {
        public readonly ConveyorLaneKey key;
        public readonly long readyTicks;
        public readonly int version;

        public ConveyorScheduledLane(ConveyorLaneKey key, long readyTicks, int version)
        {
            this.key = key;
            this.readyTicks = readyTicks;
            this.version = version;
        }
    }

    private struct ConveyorScheduleState
    {
        public long readyTicks;
        public int version;
        public bool slowRetry;
    }

    private readonly Dictionary<Vector2Int, ConveyorItemBlockState> savedConveyorItemStates = new Dictionary<Vector2Int, ConveyorItemBlockState>();
    private readonly Dictionary<ConveyorLaneKey, ConveyorScheduleState> conveyorScheduleStates = new Dictionary<ConveyorLaneKey, ConveyorScheduleState>();
    private readonly Dictionary<ConveyorLaneKey, List<ConveyorLaneKey>> conveyorBlockedWaitersByDestination = new Dictionary<ConveyorLaneKey, List<ConveyorLaneKey>>();
    private readonly List<ConveyorScheduledLane> conveyorReadyLaneHeap = new List<ConveyorScheduledLane>();
    private readonly Dictionary<ConveyorLaneKey, int> conveyorReadyLaneHeapIndicesByKey = new Dictionary<ConveyorLaneKey, int>();
    private readonly List<ConveyorLaneKey> conveyorSimulationActiveLaneKeys = new List<ConveyorLaneKey>();
    private readonly List<ConveyorLaneKey> conveyorSimulationNextActiveLaneKeys = new List<ConveyorLaneKey>();
    private readonly HashSet<ConveyorLaneKey> conveyorSimulationNextActiveLaneKeySet = new HashSet<ConveyorLaneKey>();
    private readonly HashSet<Vector2Int> conveyorSimulationOccupancyDirtyCoordinates = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, float> conveyorSimulationRemainingBudgetByCoordinate = new Dictionary<Vector2Int, float>();
    private bool conveyorScheduleDirty = true;
    private int conveyorScheduleVersion;
    private int cachedConveyorScheduleSavedBlockCount;
    private int cachedConveyorScheduleSavedItemCount;
    private int conveyorBlockedWaiterCount;

    private static bool IsSavedConveyorActiveLaneIndex(int laneIndex)
    {
        return laneIndex == ConveyorSingleLineFrontLaneIndex
            || laneIndex == ConveyorSingleLineBackLaneIndex;
    }

    private static bool TryNormalizeSavedConveyorLaneIndex(int laneIndex, out int normalizedLaneIndex)
    {
        switch (laneIndex)
        {
            case 0:
            case 1:
                normalizedLaneIndex = ConveyorSingleLineFrontLaneIndex;
                return true;
            case 2:
            case 3:
                normalizedLaneIndex = ConveyorSingleLineBackLaneIndex;
                return true;
            default:
                normalizedLaneIndex = -1;
                return false;
        }
    }

    private static bool TryNormalizeSavedConveyorLaneState(ConveyorItemLaneSaveState lane)
    {
        if (lane == null || !TryNormalizeSavedConveyorLaneIndex(lane.laneIndex, out int normalizedLaneIndex))
        {
            return false;
        }

        lane.laneIndex = normalizedLaneIndex;
        if (lane.sourceLaneIndex >= 0
            && TryNormalizeSavedConveyorLaneIndex(lane.sourceLaneIndex, out int normalizedSourceLaneIndex))
        {
            lane.sourceLaneIndex = normalizedSourceLaneIndex;
        }

        if (lane.destinationLaneIndex >= 0
            && TryNormalizeSavedConveyorLaneIndex(lane.destinationLaneIndex, out int normalizedDestinationLaneIndex))
        {
            lane.destinationLaneIndex = normalizedDestinationLaneIndex;
        }

        if (lane.cornerContinuationSourceLaneIndex >= 0
            && TryNormalizeSavedConveyorLaneIndex(lane.cornerContinuationSourceLaneIndex, out int normalizedContinuationSourceLaneIndex))
        {
            lane.cornerContinuationSourceLaneIndex = normalizedContinuationSourceLaneIndex;
        }

        if (lane.cornerContinuationDestinationLaneIndex >= 0
            && TryNormalizeSavedConveyorLaneIndex(lane.cornerContinuationDestinationLaneIndex, out int normalizedContinuationDestinationLaneIndex))
        {
            lane.cornerContinuationDestinationLaneIndex = normalizedContinuationDestinationLaneIndex;
        }

        return IsSavedConveyorActiveLaneIndex(lane.laneIndex);
    }

    public void SaveConveyorItems(Vector2Int worldCoordinate, Block block)
    {
        if (block == null || !block.IsRuntimeConveyor)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            InvalidateConveyorSchedule();
            return;
        }

        ConveyorItemBlockState state = new ConveyorItemBlockState
        {
            laneCount = Mathf.Max(0, block.GetRuntimeConveyorLaneCount()),
            conveyorSpeed = Mathf.Max(0f, block.RuntimeConveyorSpeed),
            lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks
        };

        block.CaptureConveyorItemSaveStates(state.lanes);
        for (int laneIndex = 0; laneIndex < state.laneCount; laneIndex++)
        {
            if (!block.TryGetRuntimeConveyorLaneLink(
                    laneIndex,
                    out Vector2Int destinationCoordinate,
                    out int destinationLaneIndex,
                    out float pathLength))
            {
                continue;
            }

            state.laneLinks.Add(new ConveyorLaneLinkSaveState
            {
                sourceLaneIndex = laneIndex,
                destinationCoordinate = destinationCoordinate,
                destinationLaneIndex = destinationLaneIndex,
                pathLength = Mathf.Max(ConveyorBackgroundSimulationEpsilon, pathLength)
            });
        }

        TryPopulateVirtualConveyorItemState(worldCoordinate, state);
        savedConveyorItemStates[worldCoordinate] = state;
        InvalidateConveyorSchedule();
        SyncConveyorFloorObjects(worldCoordinate, state);
    }

    public void RemoveConveyorItems(Vector2Int worldCoordinate)
    {
        savedConveyorItemStates.Remove(worldCoordinate);
        InvalidateConveyorSchedule();
    }

    public bool TryGetConveyorItems(Vector2Int worldCoordinate, out List<ConveyorItemLaneSaveState> lanes)
    {
        if (savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState state)
            && state != null
            && state.lanes.Count > 0)
        {
            lanes = CloneConveyorLaneStates(state.lanes);
            return true;
        }

        lanes = null;
        return false;
    }

    public int GetSavedConveyorItemCount()
    {
        if (savedConveyorItemStates.Count <= 0)
        {
            return 0;
        }

        int count = 0;
        List<KeyValuePair<Vector2Int, ConveyorItemBlockState>> savedConveyorItemSnapshot =
            new List<KeyValuePair<Vector2Int, ConveyorItemBlockState>>(savedConveyorItemStates);
        for (int i = 0; i < savedConveyorItemSnapshot.Count; i++)
        {
            count += CountSavedConveyorItems(savedConveyorItemSnapshot[i].Value);
        }

        return count;
    }

    public int GetSavedConveyorItemCount(Vector2Int worldCoordinate)
    {
        return savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState state)
            ? CountSavedConveyorItems(state)
            : 0;
    }

    public bool TryPeekSavedConveyorItem(
        Vector2Int worldCoordinate,
        Predicate<int> itemFilter,
        Vector3 referenceWorldPosition,
        out int itemId,
        out Vector3 itemWorldPosition)
    {
        itemId = -1;
        itemWorldPosition = default;
        if (!TryFindSavedConveyorItemLane(
                worldCoordinate,
                itemFilter,
                referenceWorldPosition,
                out _,
                out ConveyorItemLaneSaveState lane,
                out _,
                out itemWorldPosition))
        {
            return false;
        }

        itemId = lane.itemId;
        return true;
    }

    public bool TryTakeSavedConveyorItem(
        Vector2Int worldCoordinate,
        Predicate<int> itemFilter,
        Vector3 referenceWorldPosition,
        out int itemId)
    {
        itemId = -1;
        if (!TryFindSavedConveyorItemLane(
                worldCoordinate,
                itemFilter,
                referenceWorldPosition,
                out ConveyorItemBlockState state,
                out ConveyorItemLaneSaveState lane,
                out _,
                out _))
        {
            return false;
        }

        itemId = lane.itemId;
        int laneIndex = lane.laneIndex;
        state.lanes.Remove(lane);
        NotifySavedConveyorLaneVacatedExternally(worldCoordinate, laneIndex);
        SyncConveyorFloorObjects(worldCoordinate, state);
        return true;
    }

    public bool CanAddSavedConveyorItem(
        Vector2Int worldCoordinate,
        int itemId,
        Vector3 referenceWorldPosition)
    {
        return itemId >= 0
               && TryFindSavedConveyorPlacementLane(worldCoordinate, referenceWorldPosition, out _, out _, out _);
    }

    public bool TryAddSavedConveyorItem(
        Vector2Int worldCoordinate,
        int itemId,
        Vector3 referenceWorldPosition)
    {
        if (itemId < 0
            || !TryFindSavedConveyorPlacementLane(
                worldCoordinate,
                referenceWorldPosition,
                out ConveyorItemBlockState state,
                out int laneIndex,
                out _))
        {
            return false;
        }

        ConveyorItemLaneSaveState lane = new ConveyorItemLaneSaveState
        {
            laneIndex = laneIndex,
            itemId = itemId
        };
        SetSavedConveyorLaneSettled(lane, worldCoordinate);
        state.lanes.Add(lane);
        state.laneCount = Mathf.Max(state.laneCount, laneIndex + 1);
        state.lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks;
        savedConveyorItemStates[worldCoordinate] = state;
        NotifySavedConveyorLaneOccupiedExternally(worldCoordinate, laneIndex);
        SyncConveyorFloorObjects(worldCoordinate, state);
        return true;
    }

    public bool CanAcceptVirtualConveyorItemHandoff(
        Vector2Int sourceCoordinate,
        Vector2Int flowDirection,
        int sourceColumnOrdinal)
    {
        if (flowDirection == Vector2Int.zero)
        {
            return false;
        }

        Vector2Int destinationCoordinate = sourceCoordinate + flowDirection;
        if (!TryResolveVirtualConveyorReceiveLane(
                destinationCoordinate,
                flowDirection,
                sourceColumnOrdinal,
                out int destinationLaneIndex))
        {
            return false;
        }

        return !savedConveyorItemStates.TryGetValue(destinationCoordinate, out ConveyorItemBlockState destinationState)
            || GetSavedConveyorLaneItemId(destinationState, destinationLaneIndex) < 0;
    }

    public bool TryHandoffConveyorItemToVirtualConveyor(
        Vector2Int sourceCoordinate,
        Vector2Int flowDirection,
        int sourceColumnOrdinal,
        ConveyorItemLaneSaveState laneState,
        out Vector2Int destinationCoordinate,
        out int destinationLaneIndex)
    {
        destinationCoordinate = sourceCoordinate + flowDirection;
        destinationLaneIndex = -1;
        if (laneState == null
            || laneState.itemId < 0
            || flowDirection == Vector2Int.zero
            || !TryResolveVirtualConveyorReceiveLane(
                destinationCoordinate,
                flowDirection,
                sourceColumnOrdinal,
                out destinationLaneIndex)
            || !TryEnsureConveyorItemState(destinationCoordinate, out ConveyorItemBlockState destinationState)
            || !IsValidConveyorDestinationLane(destinationState, destinationLaneIndex)
            || GetSavedConveyorLaneItemId(destinationState, destinationLaneIndex) >= 0)
        {
            return false;
        }

        ConveyorItemLaneSaveState transferredLane = CloneConveyorLaneState(laneState);
        if (transferredLane == null)
        {
            return false;
        }

        transferredLane.laneIndex = destinationLaneIndex;
        SetSavedConveyorLaneSettled(transferredLane, destinationCoordinate);
        destinationState.lanes.Add(transferredLane);
        destinationState.laneCount = Mathf.Max(destinationState.laneCount, destinationLaneIndex + 1);
        if (destinationState.lastBackgroundSimulationTicks <= 0)
        {
            destinationState.lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks;
        }

        savedConveyorItemStates[destinationCoordinate] = destinationState;
        NotifySavedConveyorLaneOccupiedExternally(destinationCoordinate, destinationLaneIndex);
        SyncConveyorFloorObjects(destinationCoordinate, destinationState);
        return true;
    }

    public void SetConveyorItems(Vector2Int worldCoordinate, IReadOnlyList<ConveyorItemLaneSaveState> lanes)
    {
        if (lanes == null || lanes.Count <= 0)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            InvalidateConveyorSchedule();
            return;
        }

        savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState existingState);
        ConveyorItemBlockState state = existingState ?? new ConveyorItemBlockState();
        state.lanes.Clear();
        state.laneCount = 0;
        for (int i = 0; i < lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = CloneConveyorLaneState(lanes[i]);
            if (lane == null
                || lane.itemId < 0
                || !TryNormalizeSavedConveyorLaneState(lane)
                || GetSavedConveyorLaneItemId(state, lane.laneIndex) >= 0)
            {
                continue;
            }

            state.lanes.Add(lane);
            state.laneCount = Mathf.Max(state.laneCount, lane.laneIndex + 1);
        }

        if (state.lanes.Count <= 0)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            InvalidateConveyorSchedule();
            return;
        }

        state.laneCount = Mathf.Clamp(
            Mathf.Max(state.laneCount, GetMaxConveyorLaneIndex(state) + 1),
            0,
            Block.ConveyorCellItemUnit);
        TryPopulateVirtualConveyorItemState(worldCoordinate, state);
        state.lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks;
        savedConveyorItemStates[worldCoordinate] = state;
        InvalidateConveyorSchedule();
        SyncConveyorFloorObjects(worldCoordinate, state);
    }

    public void SimulateSavedConveyorItems(int maxPassesOverride = -1, ICollection<Vector2Int> dirtyCoordinates = null)
    {
        if (!Application.isPlaying || savedConveyorItemStates.Count <= 0)
        {
            return;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        bool profileBackgroundSimulation = MapObjectTickProfiler.IsEnabled;
        PrepareConveyorSimulationCandidates(
            nowTicks,
            profileBackgroundSimulation,
            out int savedBlockCount,
            out int savedItemCount,
            out int candidateCount,
            out int readyHeapSize,
            out int skippedNotReadyCount,
            out int staleScheduleDropCount,
            out int slowRetryCandidateCount);
        if (conveyorSimulationActiveLaneKeys.Count <= 0)
        {
            if (profileBackgroundSimulation)
            {
                MapObjectTickProfiler.AddBackgroundConveyorSimulation(
                    savedBlockCount,
                    savedItemCount,
                    candidateCount,
                    0,
                    0,
                    0,
                    conveyorSimulationOccupancyDirtyCoordinates.Count,
                    0,
                    readyHeapSize,
                    candidateCount,
                    skippedNotReadyCount,
                    CountConveyorBlockedWaiters(),
                    staleScheduleDropCount,
                    slowRetryCandidateCount);
            }

            RebaseConveyorSimulationTicks(nowTicks);
            FlushConveyorSimulationDirtyStates();
            CopyConveyorSimulationDirtyCoordinates(dirtyCoordinates);
            ClearConveyorSimulationBuffers();
            return;
        }

        int passLimit = maxPassesOverride > 0
            ? Mathf.Max(1, maxPassesOverride * ConveyorBackgroundSimulationPassMultiplier)
            : ConveyorBackgroundDefaultSimulationPasses;

        int actualPassCount = 0;
        int moveAttemptCount = 0;
        int moveSuccessCount = 0;
        for (int passIndex = 0; passIndex < passLimit; passIndex++)
        {
            if (passIndex > 0 && conveyorSimulationActiveLaneKeys.Count <= 0)
            {
                break;
            }

            if (profileBackgroundSimulation)
            {
                actualPassCount++;
            }

            bool movedAny = false;
            if (passIndex == 0)
            {
                for (int i = 0; i < conveyorSimulationActiveLaneKeys.Count; i++)
                {
                    movedAny |= TryProcessConveyorSimulationLaneKey(
                        conveyorSimulationActiveLaneKeys[i],
                        nowTicks,
                        profileBackgroundSimulation,
                        ref moveAttemptCount,
                        ref moveSuccessCount);
                }
            }
            else
            {
                for (int i = 0; i < conveyorSimulationActiveLaneKeys.Count; i++)
                {
                    movedAny |= TryProcessConveyorSimulationLaneKey(
                        conveyorSimulationActiveLaneKeys[i],
                        nowTicks,
                        profileBackgroundSimulation,
                        ref moveAttemptCount,
                        ref moveSuccessCount);
                }
            }

            if (!movedAny)
            {
                break;
            }

            PrepareNextConveyorSimulationPass();
        }

        if (profileBackgroundSimulation)
        {
            MapObjectTickProfiler.AddBackgroundConveyorSimulation(
                savedBlockCount,
                savedItemCount,
                candidateCount,
                actualPassCount,
                moveAttemptCount,
                moveSuccessCount,
                conveyorSimulationOccupancyDirtyCoordinates.Count,
                0,
                conveyorReadyLaneHeap.Count,
                candidateCount,
                skippedNotReadyCount,
                CountConveyorBlockedWaiters(),
                staleScheduleDropCount,
                slowRetryCandidateCount);
        }

        RebaseConveyorSimulationTicks(nowTicks);
        FlushConveyorSimulationDirtyStates();
        CopyConveyorSimulationDirtyCoordinates(dirtyCoordinates);
        ClearConveyorSimulationBuffers();
    }

    private void PrepareConveyorSimulationCandidates(
        long nowTicks,
        bool collectProfileCounters,
        out int savedBlockCount,
        out int savedItemCount,
        out int candidateCount,
        out int readyHeapSize,
        out int skippedNotReadyCount,
        out int staleScheduleDropCount,
        out int slowRetryCandidateCount)
    {
        conveyorSimulationActiveLaneKeys.Clear();
        conveyorSimulationNextActiveLaneKeys.Clear();
        conveyorSimulationNextActiveLaneKeySet.Clear();
        conveyorSimulationOccupancyDirtyCoordinates.Clear();
        conveyorSimulationRemainingBudgetByCoordinate.Clear();
        staleScheduleDropCount = 0;
        slowRetryCandidateCount = 0;

        if (conveyorScheduleDirty
            || (conveyorScheduleStates.Count <= 0
                && conveyorBlockedWaiterCount <= 0
                && savedConveyorItemStates.Count > 0))
        {
            RebuildConveyorSchedule(nowTicks);
        }

        while (conveyorReadyLaneHeap.Count > 0)
        {
            ConveyorScheduledLane scheduledLane = conveyorReadyLaneHeap[0];
            if (scheduledLane.readyTicks > nowTicks)
            {
                break;
            }

            PopConveyorReadyLane();
            if (!conveyorScheduleStates.TryGetValue(scheduledLane.key, out ConveyorScheduleState scheduleState)
                || scheduleState.version != scheduledLane.version
                || scheduleState.readyTicks != scheduledLane.readyTicks)
            {
                staleScheduleDropCount++;
                continue;
            }

            conveyorScheduleStates.Remove(scheduledLane.key);
            if (scheduleState.slowRetry)
            {
                slowRetryCandidateCount++;
            }

            conveyorSimulationActiveLaneKeys.Add(scheduledLane.key);
        }

        savedBlockCount = collectProfileCounters ? cachedConveyorScheduleSavedBlockCount : 0;
        savedItemCount = collectProfileCounters ? cachedConveyorScheduleSavedItemCount : 0;
        candidateCount = conveyorSimulationActiveLaneKeys.Count;
        readyHeapSize = conveyorReadyLaneHeap.Count;
        skippedNotReadyCount = Mathf.Max(0, conveyorScheduleStates.Count);
    }

    private void RebuildConveyorSchedule(long nowTicks)
    {
        conveyorScheduleStates.Clear();
        conveyorBlockedWaitersByDestination.Clear();
        conveyorBlockedWaiterCount = 0;
        conveyorReadyLaneHeap.Clear();
        conveyorReadyLaneHeapIndicesByKey.Clear();
        cachedConveyorScheduleSavedBlockCount = 0;
        cachedConveyorScheduleSavedItemCount = 0;

        List<KeyValuePair<Vector2Int, ConveyorItemBlockState>> savedConveyorItemSnapshot =
            new List<KeyValuePair<Vector2Int, ConveyorItemBlockState>>(savedConveyorItemStates);
        for (int snapshotIndex = 0; snapshotIndex < savedConveyorItemSnapshot.Count; snapshotIndex++)
        {
            KeyValuePair<Vector2Int, ConveyorItemBlockState> pair = savedConveyorItemSnapshot[snapshotIndex];
            ConveyorItemBlockState state = pair.Value;
            if (state == null || state.lanes.Count <= 0)
            {
                continue;
            }

            int validItemCount = 0;
            for (int i = 0; i < state.lanes.Count; i++)
            {
                ConveyorItemLaneSaveState lane = state.lanes[i];
                if (lane == null
                    || lane.itemId < 0
                    || !TryNormalizeSavedConveyorLaneState(lane))
                {
                    continue;
                }

                validItemCount++;
                ConveyorLaneKey laneKey = new ConveyorLaneKey(pair.Key, lane.laneIndex);
                ScheduleConveyorLane(laneKey, state, lane, nowTicks, -1f, out _);
            }

            if (validItemCount > 0)
            {
                cachedConveyorScheduleSavedBlockCount++;
                cachedConveyorScheduleSavedItemCount += validItemCount;
            }
        }

        conveyorScheduleDirty = false;
    }

    private bool ScheduleConveyorLane(
        ConveyorLaneKey laneKey,
        ConveyorItemBlockState state,
        ConveyorItemLaneSaveState lane,
        long nowTicks,
        float availableBudgetDistance,
        out long readyTicks)
    {
        readyTicks = nowTicks;
        if (state == null
            || lane == null
            || lane.itemId < 0
            || lane.laneIndex != laneKey.laneIndex)
        {
            RemoveConveyorScheduleState(laneKey);
            return false;
        }

        if (state.conveyorSpeed <= ConveyorBackgroundSimulationEpsilon
            || !TryGetConveyorLaneLink(state, lane.laneIndex, out ConveyorLaneLinkSaveState link)
            || link == null
            || link.pathLength <= ConveyorBackgroundSimulationEpsilon)
        {
            readyTicks = ResolveConveyorTopologyRetryTicks(nowTicks);
            PushConveyorScheduleState(laneKey, readyTicks, true);
            return true;
        }

        float budgetDistance = availableBudgetDistance >= 0f
            ? availableBudgetDistance
            : ResolveConveyorSimulationBudgetDistance(state, nowTicks);
        float requiredDistance = ResolveSavedConveyorLaneBoundaryDistance(lane, link);
        readyTicks = ResolveConveyorLaneReadyTicks(state, nowTicks, budgetDistance, requiredDistance);
        if (readyTicks <= nowTicks
            && TryHandleReadyConveyorLaneDestination(laneKey, link, nowTicks, out readyTicks))
        {
            return true;
        }

        PushConveyorScheduleState(laneKey, readyTicks);
        return true;
    }

    private bool TryHandleReadyConveyorLaneDestination(
        ConveyorLaneKey laneKey,
        ConveyorLaneLinkSaveState link,
        long nowTicks,
        out long readyTicks)
    {
        readyTicks = nowTicks;
        if (link == null)
        {
            return false;
        }

        ConveyorLaneKey destinationKey = new ConveyorLaneKey(link.destinationCoordinate, link.destinationLaneIndex);
        if (laneKey.Equals(destinationKey)
            || !TryEnsureConveyorItemState(link.destinationCoordinate, out ConveyorItemBlockState destinationState)
            || !IsValidConveyorDestinationLane(destinationState, link.destinationLaneIndex))
        {
            readyTicks = ResolveConveyorTopologyRetryTicks(nowTicks);
            PushConveyorScheduleState(laneKey, readyTicks, true);
            return true;
        }

        if (GetSavedConveyorLaneItemId(destinationState, link.destinationLaneIndex) < 0)
        {
            return false;
        }

        readyTicks = long.MaxValue;
        BlockConveyorScheduledLane(laneKey, destinationKey);
        return true;
    }

    private static long ResolveConveyorLaneReadyTicks(
        ConveyorItemBlockState state,
        long nowTicks,
        float budgetDistance,
        float requiredDistance)
    {
        if (state == null
            || state.conveyorSpeed <= ConveyorBackgroundSimulationEpsilon
            || requiredDistance <= ConveyorBackgroundSimulationEpsilon
            || budgetDistance + ConveyorBackgroundSimulationEpsilon >= requiredDistance)
        {
            return nowTicks;
        }

        double remainingSeconds = (requiredDistance - budgetDistance) / state.conveyorSpeed;
        if (remainingSeconds <= ConveyorBackgroundSimulationEpsilon)
        {
            return nowTicks;
        }

        double remainingTicks = remainingSeconds * TimeSpan.TicksPerSecond;
        if (remainingTicks <= 0d)
        {
            return nowTicks;
        }

        if (remainingTicks >= long.MaxValue - nowTicks)
        {
            return long.MaxValue;
        }

        return nowTicks + (long)Math.Ceiling(remainingTicks);
    }

    private static long ResolveConveyorTopologyRetryTicks(long nowTicks)
    {
        double retryTicks = ConveyorBackgroundTopologyRetrySeconds * TimeSpan.TicksPerSecond;
        if (retryTicks >= long.MaxValue - nowTicks)
        {
            return long.MaxValue;
        }

        return nowTicks + Math.Max(1L, (long)Math.Ceiling(retryTicks));
    }

    private void PushConveyorScheduleState(
        ConveyorLaneKey laneKey,
        long readyTicks,
        bool slowRetry = false)
    {
        if (conveyorScheduleStates.TryGetValue(laneKey, out ConveyorScheduleState existingState)
            && existingState.readyTicks == readyTicks
            && existingState.slowRetry == slowRetry
            && conveyorReadyLaneHeapIndicesByKey.TryGetValue(laneKey, out int duplicateIndex)
            && duplicateIndex >= 0
            && duplicateIndex < conveyorReadyLaneHeap.Count
            && conveyorReadyLaneHeap[duplicateIndex].key.Equals(laneKey))
        {
            return;
        }

        ConveyorScheduleState scheduleState = new ConveyorScheduleState
        {
            readyTicks = readyTicks,
            version = ++conveyorScheduleVersion,
            slowRetry = slowRetry
        };
        conveyorScheduleStates[laneKey] = scheduleState;
        PushConveyorReadyLane(new ConveyorScheduledLane(laneKey, readyTicks, scheduleState.version));
    }

    private bool RemoveConveyorScheduleState(ConveyorLaneKey laneKey)
    {
        bool removedState = conveyorScheduleStates.Remove(laneKey);
        bool removedHeap = RemoveConveyorReadyLane(laneKey);
        return removedState || removedHeap;
    }

    private int CountConveyorBlockedWaiters()
    {
        return Math.Max(0, conveyorBlockedWaiterCount);
    }

    private void PushConveyorReadyLane(ConveyorScheduledLane scheduledLane)
    {
        if (conveyorReadyLaneHeapIndicesByKey.TryGetValue(scheduledLane.key, out int existingIndex)
            && existingIndex >= 0
            && existingIndex < conveyorReadyLaneHeap.Count
            && conveyorReadyLaneHeap[existingIndex].key.Equals(scheduledLane.key))
        {
            SetConveyorReadyLaneHeapEntry(existingIndex, scheduledLane);
            FixConveyorReadyLaneHeapAt(existingIndex);
            return;
        }

        conveyorReadyLaneHeap.Add(scheduledLane);
        int index = conveyorReadyLaneHeap.Count - 1;
        conveyorReadyLaneHeapIndicesByKey[scheduledLane.key] = index;
        SiftUpConveyorReadyLane(index);
    }

    private void SiftUpConveyorReadyLane(int index)
    {
        if (index < 0 || index >= conveyorReadyLaneHeap.Count)
        {
            return;
        }

        ConveyorScheduledLane scheduledLane = conveyorReadyLaneHeap[index];
        while (index > 0)
        {
            int parentIndex = (index - 1) / 2;
            if (CompareConveyorScheduledLane(conveyorReadyLaneHeap[parentIndex], scheduledLane) <= 0)
            {
                break;
            }

            SetConveyorReadyLaneHeapEntry(index, conveyorReadyLaneHeap[parentIndex]);
            index = parentIndex;
        }

        SetConveyorReadyLaneHeapEntry(index, scheduledLane);
    }

    private ConveyorScheduledLane PopConveyorReadyLane()
    {
        ConveyorScheduledLane result = conveyorReadyLaneHeap[0];
        RemoveConveyorReadyLaneAt(0);
        return result;
    }

    private bool RemoveConveyorReadyLane(ConveyorLaneKey laneKey)
    {
        if (!conveyorReadyLaneHeapIndicesByKey.TryGetValue(laneKey, out int index)
            || index < 0
            || index >= conveyorReadyLaneHeap.Count
            || !conveyorReadyLaneHeap[index].key.Equals(laneKey))
        {
            conveyorReadyLaneHeapIndicesByKey.Remove(laneKey);
            return false;
        }

        RemoveConveyorReadyLaneAt(index);
        return true;
    }

    private void RemoveConveyorReadyLaneAt(int index)
    {
        if (index < 0 || index >= conveyorReadyLaneHeap.Count)
        {
            return;
        }

        int lastIndex = conveyorReadyLaneHeap.Count - 1;
        ConveyorScheduledLane removedLane = conveyorReadyLaneHeap[index];
        conveyorReadyLaneHeapIndicesByKey.Remove(removedLane.key);
        ConveyorScheduledLane lastLane = conveyorReadyLaneHeap[lastIndex];
        conveyorReadyLaneHeap.RemoveAt(lastIndex);
        if (index == lastIndex)
        {
            return;
        }

        SetConveyorReadyLaneHeapEntry(index, lastLane);
        FixConveyorReadyLaneHeapAt(index);
    }

    private void FixConveyorReadyLaneHeapAt(int index)
    {
        if (index < 0 || index >= conveyorReadyLaneHeap.Count)
        {
            return;
        }

        int parentIndex = (index - 1) / 2;
        if (index > 0 && CompareConveyorScheduledLane(conveyorReadyLaneHeap[index], conveyorReadyLaneHeap[parentIndex]) < 0)
        {
            SiftUpConveyorReadyLane(index);
        }
        else
        {
            SiftDownConveyorReadyLane(index);
        }
    }

    private void SiftDownConveyorReadyLane(int index)
    {
        if (index < 0 || index >= conveyorReadyLaneHeap.Count)
        {
            return;
        }

        ConveyorScheduledLane scheduledLane = conveyorReadyLaneHeap[index];
        while (true)
        {
            int leftIndex = (index * 2) + 1;
            if (leftIndex >= conveyorReadyLaneHeap.Count)
            {
                break;
            }

            int rightIndex = leftIndex + 1;
            int childIndex = rightIndex < conveyorReadyLaneHeap.Count
                && CompareConveyorScheduledLane(conveyorReadyLaneHeap[rightIndex], conveyorReadyLaneHeap[leftIndex]) < 0
                    ? rightIndex
                    : leftIndex;

            if (CompareConveyorScheduledLane(scheduledLane, conveyorReadyLaneHeap[childIndex]) <= 0)
            {
                break;
            }

            SetConveyorReadyLaneHeapEntry(index, conveyorReadyLaneHeap[childIndex]);
            index = childIndex;
        }

        SetConveyorReadyLaneHeapEntry(index, scheduledLane);
    }

    private void SetConveyorReadyLaneHeapEntry(int index, ConveyorScheduledLane scheduledLane)
    {
        conveyorReadyLaneHeap[index] = scheduledLane;
        conveyorReadyLaneHeapIndicesByKey[scheduledLane.key] = index;
    }

    private static int CompareConveyorScheduledLane(ConveyorScheduledLane left, ConveyorScheduledLane right)
    {
        int readyCompare = left.readyTicks.CompareTo(right.readyTicks);
        return readyCompare != 0 ? readyCompare : left.version.CompareTo(right.version);
    }

    private static float ResolveConveyorSimulationBudgetDistance(ConveyorItemBlockState state, long nowTicks)
    {
        if (state == null || state.conveyorSpeed <= ConveyorBackgroundSimulationEpsilon)
        {
            return 0f;
        }

        if (state.lastBackgroundSimulationTicks <= 0)
        {
            return 0f;
        }

        double elapsedSeconds = TimeSpan.FromTicks(Math.Max(0L, nowTicks - state.lastBackgroundSimulationTicks)).TotalSeconds;
        if (elapsedSeconds <= ConveyorBackgroundSimulationEpsilon)
        {
            return 0f;
        }

        return Mathf.Max(0f, (float)Math.Min(elapsedSeconds * state.conveyorSpeed, float.MaxValue));
    }

    private bool TryProcessConveyorSimulationLaneKey(
        ConveyorLaneKey laneKey,
        long nowTicks,
        bool collectProfileCounters,
        ref int moveAttemptCount,
        ref int moveSuccessCount)
    {
        if (!TryGetSavedConveyorLane(laneKey, out ConveyorItemBlockState sourceState, out ConveyorItemLaneSaveState lane)
            || sourceState == null
            || lane == null
            || lane.itemId < 0)
        {
            return false;
        }

        float budgetDistance = ResolveConveyorSimulationBudgetDistance(sourceState, nowTicks);
        ConveyorSimulationItem item = new ConveyorSimulationItem
        {
            coordinate = laneKey.coordinate,
            lane = lane,
            budgetDistance = budgetDistance
        };

        if (item.budgetDistance <= ConveyorBackgroundSimulationEpsilon)
        {
            ScheduleConveyorLane(laneKey, sourceState, lane, nowTicks, budgetDistance, out _);
            return false;
        }

        ConveyorSimulationMoveResult moveResult = TryMoveConveyorSimulationItem(
            ref item,
            out bool vacatedLane,
            out ConveyorLaneKey vacatedLaneKey,
            out ConveyorLaneKey blockedDestinationKey);
        if (collectProfileCounters && IsConveyorMoveAttemptResult(moveResult))
        {
            moveAttemptCount++;
        }

        if (moveResult != ConveyorSimulationMoveResult.Moved)
        {
            HandleFailedConveyorSimulationMove(
                moveResult,
                laneKey,
                sourceState,
                lane,
                nowTicks,
                item.budgetDistance,
                blockedDestinationKey);
            RecordConveyorSimulationRemainingBudget(laneKey.coordinate, item.budgetDistance);
            return false;
        }

        if (collectProfileCounters)
        {
            moveSuccessCount++;
        }

        ConveyorLaneKey movedLaneKey = new ConveyorLaneKey(item.coordinate, item.lane.laneIndex);
        if (item.lane != null
            && item.lane.itemId >= 0
            && item.budgetDistance > ConveyorBackgroundSimulationEpsilon)
        {
            if (TryGetSavedConveyorLane(movedLaneKey, out ConveyorItemBlockState movedState, out ConveyorItemLaneSaveState movedLane)
                && ScheduleConveyorLane(movedLaneKey, movedState, movedLane, nowTicks, item.budgetDistance, out long readyTicks)
                && readyTicks <= nowTicks)
            {
                QueueNextConveyorSimulationLaneKey(movedLaneKey);
            }
        }
        else if (TryGetSavedConveyorLane(movedLaneKey, out ConveyorItemBlockState settledState, out ConveyorItemLaneSaveState settledLane))
        {
            ScheduleConveyorLane(movedLaneKey, settledState, settledLane, nowTicks, item.budgetDistance, out _);
        }

        if (vacatedLane)
        {
            WakeConveyorBlockedWaitersForVacatedLane(vacatedLaneKey, nowTicks);
        }

        RecordConveyorSimulationRemainingBudget(vacatedLaneKey.coordinate, 0f);
        RecordConveyorSimulationRemainingBudget(item.coordinate, item.budgetDistance);
        return true;
    }

    private static bool IsConveyorMoveAttemptResult(ConveyorSimulationMoveResult result)
    {
        return result == ConveyorSimulationMoveResult.Moved
            || result == ConveyorSimulationMoveResult.BlockedByOccupiedDestination;
    }

    private void HandleFailedConveyorSimulationMove(
        ConveyorSimulationMoveResult result,
        ConveyorLaneKey laneKey,
        ConveyorItemBlockState sourceState,
        ConveyorItemLaneSaveState lane,
        long nowTicks,
        float budgetDistance,
        ConveyorLaneKey blockedDestinationKey)
    {
        switch (result)
        {
            case ConveyorSimulationMoveResult.NotReady:
                ScheduleConveyorLane(laneKey, sourceState, lane, nowTicks, budgetDistance, out _);
                break;
            case ConveyorSimulationMoveResult.BlockedByOccupiedDestination:
                BlockConveyorScheduledLane(laneKey, blockedDestinationKey);
                break;
            case ConveyorSimulationMoveResult.InvalidTopology:
            case ConveyorSimulationMoveResult.InvalidDestination:
                if (TryGetSavedConveyorLane(laneKey, out _, out _))
                {
                    PushConveyorScheduleState(
                        laneKey,
                        ResolveConveyorTopologyRetryTicks(nowTicks),
                        true);
                }
                else
                {
                    RemoveConveyorScheduleState(laneKey);
                }

                break;
            case ConveyorSimulationMoveResult.InvalidSource:
                RemoveConveyorScheduleState(laneKey);
                break;
        }
    }

    private void BlockConveyorScheduledLane(ConveyorLaneKey laneKey, ConveyorLaneKey destinationKey)
    {
        AddConveyorBlockedWaiter(destinationKey, laneKey);
        RemoveConveyorScheduleState(laneKey);
    }

    private void AddConveyorBlockedWaiter(ConveyorLaneKey destinationKey, ConveyorLaneKey laneKey)
    {
        if (!conveyorBlockedWaitersByDestination.TryGetValue(destinationKey, out List<ConveyorLaneKey> waiters))
        {
            waiters = new List<ConveyorLaneKey>(1);
            conveyorBlockedWaitersByDestination[destinationKey] = waiters;
        }

        if (!waiters.Contains(laneKey))
        {
            waiters.Add(laneKey);
            conveyorBlockedWaiterCount++;
        }
    }

    private void RemoveConveyorBlockedWaiter(ConveyorLaneKey destinationKey, ConveyorLaneKey laneKey)
    {
        if (!conveyorBlockedWaitersByDestination.TryGetValue(destinationKey, out List<ConveyorLaneKey> waiters))
        {
            return;
        }

        if (waiters.Remove(laneKey))
        {
            conveyorBlockedWaiterCount = Math.Max(0, conveyorBlockedWaiterCount - 1);
        }

        if (waiters.Count <= 0)
        {
            conveyorBlockedWaitersByDestination.Remove(destinationKey);
        }
    }

    private void WakeConveyorBlockedWaitersForVacatedLane(ConveyorLaneKey laneKey, long nowTicks)
    {
        if (!conveyorBlockedWaitersByDestination.TryGetValue(laneKey, out List<ConveyorLaneKey> waiters))
        {
            return;
        }

        for (int i = 0; i < waiters.Count; i++)
        {
            ConveyorLaneKey waiterKey = waiters[i];
            if (!TryGetSavedConveyorLane(waiterKey, out _, out _))
            {
                RemoveConveyorScheduleState(waiterKey);
                continue;
            }

            PushConveyorScheduleState(waiterKey, nowTicks);
            QueueNextConveyorSimulationLaneKey(waiterKey);
        }

        conveyorBlockedWaiterCount = Math.Max(0, conveyorBlockedWaiterCount - waiters.Count);
        conveyorBlockedWaitersByDestination.Remove(laneKey);
    }

    private void QueueNextConveyorSimulationLaneKey(ConveyorLaneKey laneKey)
    {
        if (conveyorSimulationNextActiveLaneKeySet.Add(laneKey))
        {
            conveyorSimulationNextActiveLaneKeys.Add(laneKey);
        }
    }

    private void PrepareNextConveyorSimulationPass()
    {
        conveyorSimulationActiveLaneKeys.Clear();
        conveyorSimulationActiveLaneKeys.AddRange(conveyorSimulationNextActiveLaneKeys);
        conveyorSimulationNextActiveLaneKeys.Clear();
        conveyorSimulationNextActiveLaneKeySet.Clear();
    }

    private static float ResolveSavedConveyorMotionPathLength(ConveyorItemLaneSaveState lane)
    {
        if (lane == null)
        {
            return 0f;
        }

        if (lane.useCornerMotion)
        {
            return lane.durationPathLength > ConveyorBackgroundSimulationEpsilon
                ? lane.durationPathLength
                : lane.pathLength;
        }

        float pathLength = lane.pathLength;
        if (lane.cornerContinuationActive)
        {
            pathLength += Mathf.Max(0f, lane.cornerContinuationPathLength * (1f - Mathf.Clamp01(lane.cornerContinuationStartProgress)));
        }

        return pathLength;
    }

    private static float ResolveSavedConveyorLaneBoundaryDistance(
        ConveyorItemLaneSaveState lane,
        ConveyorLaneLinkSaveState link)
    {
        if (lane == null)
        {
            return 0f;
        }

        if (!lane.hasMotion)
        {
            return link != null ? Mathf.Max(0f, link.pathLength) : 0f;
        }

        float pathLength = ResolveSavedConveyorMotionPathLength(lane);
        if (pathLength <= ConveyorBackgroundSimulationEpsilon)
        {
            pathLength = link != null ? link.pathLength : 0f;
        }

        if (pathLength <= ConveyorBackgroundSimulationEpsilon)
        {
            return 0f;
        }

        return Mathf.Max(0f, pathLength * (1f - Mathf.Clamp01(lane.progress)));
    }

    private ConveyorSimulationMoveResult TryMoveConveyorSimulationItem(
        ref ConveyorSimulationItem item,
        out bool vacatedLane,
        out ConveyorLaneKey vacatedLaneKey,
        out ConveyorLaneKey blockedDestinationKey)
    {
        vacatedLane = false;
        vacatedLaneKey = default;
        blockedDestinationKey = default;

        if (item.lane == null
            || item.lane.itemId < 0
            || !TryEnsureConveyorItemState(item.coordinate, out ConveyorItemBlockState sourceState)
            || sourceState == null)
        {
            return ConveyorSimulationMoveResult.InvalidSource;
        }

        if (!TryGetConveyorLaneLink(sourceState, item.lane.laneIndex, out ConveyorLaneLinkSaveState link)
            || link == null
            || link.destinationLaneIndex < 0
            || link.pathLength <= ConveyorBackgroundSimulationEpsilon)
        {
            return ConveyorSimulationMoveResult.InvalidTopology;
        }

        if (!TryEnsureConveyorItemState(link.destinationCoordinate, out ConveyorItemBlockState destinationState)
            || !IsValidConveyorDestinationLane(destinationState, link.destinationLaneIndex))
        {
            return ConveyorSimulationMoveResult.InvalidDestination;
        }

        ConveyorLaneKey sourceKey = new ConveyorLaneKey(item.coordinate, item.lane.laneIndex);
        ConveyorLaneKey destinationKey = new ConveyorLaneKey(link.destinationCoordinate, link.destinationLaneIndex);
        if (sourceKey.Equals(destinationKey))
        {
            return ConveyorSimulationMoveResult.InvalidTopology;
        }

        float requiredDistance = ResolveSavedConveyorLaneBoundaryDistance(item.lane, link);
        if (item.budgetDistance + ConveyorBackgroundSimulationEpsilon < requiredDistance)
        {
            return ConveyorSimulationMoveResult.NotReady;
        }

        if (GetSavedConveyorLaneItemId(destinationState, link.destinationLaneIndex) >= 0)
        {
            blockedDestinationKey = destinationKey;
            return ConveyorSimulationMoveResult.BlockedByOccupiedDestination;
        }

        item.budgetDistance = Mathf.Max(0f, item.budgetDistance - requiredDistance);
        sourceState.lanes.Remove(item.lane);

        item.lane.laneIndex = link.destinationLaneIndex;
        SetSavedConveyorLaneSettled(item.lane, link.destinationCoordinate);
        destinationState.lanes.Add(item.lane);
        destinationState.laneCount = Mathf.Max(destinationState.laneCount, link.destinationLaneIndex + 1);

        conveyorSimulationOccupancyDirtyCoordinates.Add(item.coordinate);
        conveyorSimulationOccupancyDirtyCoordinates.Add(link.destinationCoordinate);

        item.coordinate = link.destinationCoordinate;
        vacatedLane = true;
        vacatedLaneKey = sourceKey;
        return ConveyorSimulationMoveResult.Moved;
    }

    private bool TryEnsureConveyorItemState(Vector2Int worldCoordinate, out ConveyorItemBlockState state)
    {
        if (savedConveyorItemStates.TryGetValue(worldCoordinate, out state) && state != null)
        {
            if ((!state.hasVirtualTopology || state.laneCount <= 0 || state.laneLinks.Count <= 0)
                && TryPopulateVirtualConveyorItemState(worldCoordinate, state))
            {
                savedConveyorItemStates[worldCoordinate] = state;
            }

            return state.laneCount > 0;
        }

        if (!TryCreateVirtualConveyorItemState(worldCoordinate, out state))
        {
            return false;
        }

        savedConveyorItemStates[worldCoordinate] = state;
        return true;
    }

    private bool TryGetSavedConveyorLane(
        ConveyorLaneKey laneKey,
        out ConveyorItemBlockState state,
        out ConveyorItemLaneSaveState lane)
    {
        lane = null;
        if (!savedConveyorItemStates.TryGetValue(laneKey.coordinate, out state)
            || state == null
            || state.lanes.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < state.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState candidate = state.lanes[i];
            if (candidate == null
                || candidate.itemId < 0
                || candidate.laneIndex != laneKey.laneIndex
                || !TryNormalizeSavedConveyorLaneState(candidate))
            {
                continue;
            }

            lane = candidate;
            return true;
        }

        return false;
    }

    private bool TryFindSavedConveyorItemLane(
        Vector2Int worldCoordinate,
        Predicate<int> itemFilter,
        Vector3 referenceWorldPosition,
        out ConveyorItemBlockState state,
        out ConveyorItemLaneSaveState lane,
        out int laneIndex,
        out Vector3 laneWorldPosition)
    {
        state = null;
        lane = null;
        laneIndex = -1;
        laneWorldPosition = default;
        if (!TryEnsureConveyorItemState(worldCoordinate, out state)
            || state == null
            || state.lanes.Count <= 0)
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        for (int i = 0; i < state.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState candidate = state.lanes[i];
            if (candidate == null
                || candidate.itemId < 0
                || candidate.laneIndex < 0
                || !IsSavedConveyorActiveLaneIndex(candidate.laneIndex)
                || (itemFilter != null && !itemFilter(candidate.itemId)))
            {
                continue;
            }

            Vector3 candidateWorldPosition = ResolveSavedConveyorLaneWorldPosition(worldCoordinate, candidate, candidate.laneIndex);
            Vector3 offset = candidateWorldPosition - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (lane != null && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            lane = candidate;
            laneIndex = candidate.laneIndex;
            laneWorldPosition = candidateWorldPosition;
            bestDistanceSqr = distanceSqr;
        }

        return lane != null;
    }

    private bool TryFindSavedConveyorPlacementLane(
        Vector2Int worldCoordinate,
        Vector3 referenceWorldPosition,
        out ConveyorItemBlockState state,
        out int laneIndex,
        out Vector3 laneWorldPosition)
    {
        state = null;
        laneIndex = -1;
        laneWorldPosition = default;
        if (!TryEnsureConveyorItemState(worldCoordinate, out state)
            || state == null
            || state.laneCount <= 0)
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        for (int candidateLaneIndex = 0; candidateLaneIndex < state.laneCount; candidateLaneIndex++)
        {
            if (!IsSavedConveyorActiveLaneIndex(candidateLaneIndex))
            {
                continue;
            }

            if (GetSavedConveyorLaneItemId(state, candidateLaneIndex) >= 0)
            {
                continue;
            }

            Vector3 candidateWorldPosition = ResolveSavedConveyorLaneWorldPosition(worldCoordinate, null, candidateLaneIndex);
            Vector3 offset = candidateWorldPosition - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (laneIndex >= 0 && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            laneIndex = candidateLaneIndex;
            laneWorldPosition = candidateWorldPosition;
            bestDistanceSqr = distanceSqr;
        }

        return laneIndex >= 0;
    }

    private bool TryCreateVirtualConveyorItemState(Vector2Int worldCoordinate, out ConveyorItemBlockState state)
    {
        state = new ConveyorItemBlockState
        {
            laneCount = Block.ConveyorCellItemUnit,
            lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks
        };

        if (!TryPopulateVirtualConveyorItemState(worldCoordinate, state))
        {
            state = null;
            return false;
        }

        return true;
    }

    private bool TryPopulateVirtualConveyorItemState(Vector2Int worldCoordinate, ConveyorItemBlockState state)
    {
        if (state == null
            || !TryResolveVirtualConveyor(
                worldCoordinate,
                out ConveyorBelt conveyor,
                out Vector2Int inputDirection,
                out Vector2Int outputDirection))
        {
            return false;
        }

        state.laneCount = Mathf.Clamp(
            Mathf.Max(state.laneCount, Block.ConveyorCellItemUnit),
            0,
            Block.ConveyorCellItemUnit);
        state.conveyorSpeed = Mathf.Max(state.conveyorSpeed, conveyor.ConveyorSpeed);
        if (state.lastBackgroundSimulationTicks <= 0)
        {
            state.lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks;
        }

        state.laneLinks.Clear();
        AddVirtualConveyorLaneLink(
            state,
            ConveyorSingleLineBackLaneIndex,
            worldCoordinate,
            ConveyorSingleLineFrontLaneIndex,
            VirtualConveyorLanePathLength);

        Vector2Int nextCoordinate = worldCoordinate + outputDirection;
        if (TryResolveVirtualConveyorReceiveLane(nextCoordinate, outputDirection, 0, out int destinationLane0))
        {
            AddVirtualConveyorLaneLink(
                state,
                ConveyorSingleLineFrontLaneIndex,
                nextCoordinate,
                destinationLane0,
                VirtualConveyorLanePathLength);
        }

        state.hasVirtualTopology = true;
        return state.laneLinks.Count > 0;
    }

    private bool TryResolveVirtualConveyorReceiveLane(
        Vector2Int destinationCoordinate,
        Vector2Int incomingFlowDirection,
        int sourceColumnOrdinal,
        out int destinationLaneIndex)
    {
        destinationLaneIndex = -1;
        if (incomingFlowDirection == Vector2Int.zero
            || sourceColumnOrdinal != 0
            || !TryResolveVirtualConveyor(
                destinationCoordinate,
                out ConveyorBelt destinationConveyor,
                out Vector2Int inputDirection,
                out _)
            || !CanVirtualConveyorReceiveFlow(destinationConveyor, inputDirection, incomingFlowDirection))
        {
            return false;
        }

        destinationLaneIndex = ConveyorSingleLineBackLaneIndex;
        return true;
    }

    private static bool CanVirtualConveyorReceiveFlow(
        ConveyorBelt receiverConveyor,
        Vector2Int receiverInputDirection,
        Vector2Int incomingFlowDirection)
    {
        if (receiverInputDirection == Vector2Int.zero || incomingFlowDirection == Vector2Int.zero)
        {
            return false;
        }

        if (receiverInputDirection == -incomingFlowDirection)
        {
            return true;
        }

        return receiverConveyor != null
            && !receiverConveyor.IsCornerVariant
            && ((receiverInputDirection.x * incomingFlowDirection.x)
                + (receiverInputDirection.y * incomingFlowDirection.y)) == 0;
    }

    private bool TryResolveVirtualConveyor(
        Vector2Int worldCoordinate,
        out ConveyorBelt conveyor,
        out Vector2Int inputDirection,
        out Vector2Int outputDirection)
    {
        conveyor = null;
        inputDirection = Vector2Int.zero;
        outputDirection = Vector2Int.zero;

        if (!TryResolveVirtualConveyorPrototype(
                worldCoordinate,
                out conveyor,
                out Quaternion rotation,
                out _))
        {
            return false;
        }

        return conveyor.TryGetInputDirection(rotation, out inputDirection)
            && conveyor.TryGetOutputDirection(rotation, out outputDirection)
            && inputDirection != Vector2Int.zero
            && outputDirection != Vector2Int.zero;
    }

    private bool TryResolveVirtualConveyorPrototype(
        Vector2Int worldCoordinate,
        out ConveyorBelt conveyor,
        out Quaternion rotation,
        out InstallationSaveState state)
    {
        conveyor = null;
        rotation = Quaternion.identity;
        state = null;

        if (!TryGetInstallationAnchorAtCoordinate(worldCoordinate, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        if (TryGetLiveInstallation(anchorCoordinate, out _, out InstallationSaveState liveState)
            && liveState != null)
        {
            state = liveState;
        }
        else if (!TryGetInstallationState(anchorCoordinate, out state) || state == null)
        {
            return false;
        }

        if (state.occupiedCoordinates != null
            && state.occupiedCoordinates.Count > 0
            && !state.occupiedCoordinates.Contains(worldCoordinate))
        {
            return false;
        }

        ItemDefinition definition = ResolveVirtualConveyorItemDefinition(state.itemId, state);
        if (definition == null || !(definition.mapObject is ConveyorBelt conveyorPrototype))
        {
            return false;
        }

        conveyor = ResolveVirtualConveyorVariant(conveyorPrototype, state.conveyorVariantKind);
        if (conveyor == null)
        {
            return false;
        }

        rotation = ResolveVirtualConveyorRotation(conveyor, state.quarterTurns);
        return true;
    }

    private static ConveyorBelt ResolveVirtualConveyorVariant(ConveyorBelt conveyorPrototype, int conveyorVariantKind)
    {
        if (conveyorPrototype == null)
        {
            return null;
        }

        if (conveyorPrototype is ConvayorBelt2F)
        {
            return conveyorPrototype;
        }

        switch (conveyorVariantKind)
        {
            case 2:
                return conveyorPrototype.ReverseCornerVariantPrefab;
            case 1:
                return conveyorPrototype.CornerVariantPrefab;
            case 0:
                return conveyorPrototype.StraightVariantPrefab;
            default:
                return conveyorPrototype;
        }
    }

    private static Quaternion ResolveVirtualConveyorRotation(ConveyorBelt conveyor, int quarterTurns)
    {
        if (conveyor == null)
        {
            return Quaternion.identity;
        }

        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        int rotationQuarterTurns = (normalizedQuarterTurns + conveyor.PlacementRotationQuarterTurnOffset) % 4;
        return conveyor.transform.rotation * Quaternion.Euler(0f, rotationQuarterTurns * 90f, 0f);
    }

    private static ItemDefinition ResolveVirtualConveyorItemDefinition(int itemId, InstallationSaveState state)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        ItemDefinition definition = ItemDefinitionLookup.ResolveById(definitions, itemId);
        if (!ItemDefinitionLookup.LooksLikeLegacyConveyorBelt2FState(
                itemId,
                definition,
                state?.occupiedCoordinates))
        {
            return definition;
        }

        ItemDefinition belt2FDefinition = ItemDefinitionLookup.ResolveConveyorBelt2F(definitions);
        return belt2FDefinition != null ? belt2FDefinition : definition;
    }

    private static void AddVirtualConveyorLaneLink(
        ConveyorItemBlockState state,
        int sourceLaneIndex,
        Vector2Int destinationCoordinate,
        int destinationLaneIndex,
        float pathLength)
    {
        if (state == null
            || !IsSavedConveyorActiveLaneIndex(sourceLaneIndex)
            || !IsSavedConveyorActiveLaneIndex(destinationLaneIndex))
        {
            return;
        }

        state.laneLinks.Add(new ConveyorLaneLinkSaveState
        {
            sourceLaneIndex = sourceLaneIndex,
            destinationCoordinate = destinationCoordinate,
            destinationLaneIndex = destinationLaneIndex,
            pathLength = Mathf.Max(ConveyorBackgroundSimulationEpsilon, pathLength)
        });
    }

    private static bool TryGetConveyorLaneLink(
        ConveyorItemBlockState state,
        int sourceLaneIndex,
        out ConveyorLaneLinkSaveState link)
    {
        link = null;
        if (state?.laneLinks == null || sourceLaneIndex < 0)
        {
            return false;
        }

        for (int i = 0; i < state.laneLinks.Count; i++)
        {
            ConveyorLaneLinkSaveState candidate = state.laneLinks[i];
            if (candidate != null && candidate.sourceLaneIndex == sourceLaneIndex)
            {
                link = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsValidConveyorDestinationLane(ConveyorItemBlockState state, int laneIndex)
    {
        return state != null
            && IsSavedConveyorActiveLaneIndex(laneIndex)
            && laneIndex >= 0
            && laneIndex < Mathf.Max(0, state.laneCount);
    }

    private void FlushConveyorSimulationDirtyStates()
    {
        foreach (Vector2Int coordinate in conveyorSimulationOccupancyDirtyCoordinates)
        {
            savedConveyorItemStates.TryGetValue(coordinate, out ConveyorItemBlockState state);
            SyncConveyorFloorObjects(coordinate, state);
        }
    }

    private void CopyConveyorSimulationDirtyCoordinates(ICollection<Vector2Int> results)
    {
        if (results == null)
        {
            return;
        }

        foreach (Vector2Int coordinate in conveyorSimulationOccupancyDirtyCoordinates)
        {
            results.Add(coordinate);
        }
    }

    private void RebaseConveyorSimulationTicks(long nowTicks)
    {
        foreach (Vector2Int coordinate in conveyorSimulationOccupancyDirtyCoordinates)
        {
            if (!conveyorSimulationRemainingBudgetByCoordinate.ContainsKey(coordinate))
            {
                conveyorSimulationRemainingBudgetByCoordinate[coordinate] = 0f;
            }
        }

        foreach (KeyValuePair<Vector2Int, float> pair in conveyorSimulationRemainingBudgetByCoordinate)
        {
            if (!savedConveyorItemStates.TryGetValue(pair.Key, out ConveyorItemBlockState state)
                || state == null)
            {
                continue;
            }

            state.lastBackgroundSimulationTicks = ResolveRebasedConveyorSimulationTicks(
                state,
                nowTicks,
                pair.Value);
        }
    }

    private void RecordConveyorSimulationRemainingBudget(Vector2Int coordinate, float remainingBudgetDistance)
    {
        remainingBudgetDistance = Mathf.Max(0f, remainingBudgetDistance);
        if (!conveyorSimulationRemainingBudgetByCoordinate.TryGetValue(coordinate, out float currentBudget)
            || remainingBudgetDistance > currentBudget)
        {
            conveyorSimulationRemainingBudgetByCoordinate[coordinate] = remainingBudgetDistance;
        }
    }

    private static long ResolveRebasedConveyorSimulationTicks(
        ConveyorItemBlockState state,
        long nowTicks,
        float remainingBudgetDistance)
    {
        if (state == null
            || state.lanes.Count <= 0
            || state.conveyorSpeed <= ConveyorBackgroundSimulationEpsilon
            || remainingBudgetDistance <= ConveyorBackgroundSimulationEpsilon)
        {
            return nowTicks;
        }

        double remainingSeconds = remainingBudgetDistance / state.conveyorSpeed;
        if (remainingSeconds <= ConveyorBackgroundSimulationEpsilon)
        {
            return nowTicks;
        }

        double remainingTicks = remainingSeconds * TimeSpan.TicksPerSecond;
        if (remainingTicks <= 0d)
        {
            return nowTicks;
        }

        long clampedRemainingTicks = remainingTicks >= nowTicks
            ? nowTicks
            : (long)remainingTicks;
        return Math.Max(0L, nowTicks - clampedRemainingTicks);
    }

    private void ClearConveyorSimulationBuffers()
    {
        conveyorSimulationActiveLaneKeys.Clear();
        conveyorSimulationNextActiveLaneKeys.Clear();
        conveyorSimulationNextActiveLaneKeySet.Clear();
        conveyorSimulationOccupancyDirtyCoordinates.Clear();
        conveyorSimulationRemainingBudgetByCoordinate.Clear();
    }

    private void InvalidateConveyorSchedule()
    {
        MarkConveyorScheduleDirty();
        conveyorScheduleStates.Clear();
        conveyorBlockedWaitersByDestination.Clear();
        conveyorBlockedWaiterCount = 0;
        conveyorReadyLaneHeap.Clear();
        conveyorReadyLaneHeapIndicesByKey.Clear();
    }

    private void MarkConveyorScheduleDirty()
    {
        conveyorScheduleDirty = true;
        cachedConveyorScheduleSavedBlockCount = 0;
        cachedConveyorScheduleSavedItemCount = 0;
    }

    private void NotifySavedConveyorLaneVacatedExternally(Vector2Int coordinate, int laneIndex)
    {
        ConveyorLaneKey laneKey = new ConveyorLaneKey(coordinate, laneIndex);
        RemoveConveyorScheduleState(laneKey);
        WakeConveyorBlockedWaitersForVacatedLane(laneKey, DateTime.UtcNow.Ticks);
        MarkConveyorScheduleDirty();
        NotifySavedConveyorExternalOccupancyChanged(coordinate);
    }

    private void NotifySavedConveyorLaneOccupiedExternally(Vector2Int coordinate, int laneIndex)
    {
        RemoveConveyorScheduleState(new ConveyorLaneKey(coordinate, laneIndex));
        MarkConveyorScheduleDirty();
        NotifySavedConveyorExternalOccupancyChanged(coordinate);
    }

    private static void NotifySavedConveyorExternalOccupancyChanged(Vector2Int coordinate)
    {
        TerrainGenerator.ResolveActive()?.WakeLoadedConveyorsNearBackgroundConveyorChange(coordinate);

        for (int i = 0; i < SavedConveyorExternalWakeOffsets.Length; i++)
        {
            Vector2Int wakeCoordinate = coordinate + SavedConveyorExternalWakeOffsets[i];
            RobotArm.WakeAroundCoordinate(wakeCoordinate);
            InputOutputModule.WakeRuntimeModulesAtCoordinate(wakeCoordinate);
        }
    }

    private void SyncConveyorFloorObjects(Vector2Int worldCoordinate, ConveyorItemBlockState state)
    {
        if (state == null || state.lanes.Count <= 0)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        int laneCount = Mathf.Clamp(
            Mathf.Max(state.laneCount, GetMaxConveyorLaneIndex(state) + 1),
            0,
            Block.ConveyorCellItemUnit);
        if (laneCount <= 0)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        List<int> itemIds = new List<int>(laneCount + 2)
        {
            ConveyorStackStateSentinel,
            laneCount
        };
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            itemIds.Add(GetSavedConveyorLaneItemId(state, laneIndex));
        }

        SetFloorObjects(worldCoordinate, itemIds);
    }

    private static int GetSavedConveyorLaneItemId(ConveyorItemBlockState state, int laneIndex)
    {
        if (state?.lanes == null)
        {
            return -1;
        }

        for (int i = 0; i < state.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = state.lanes[i];
            if (lane != null && lane.laneIndex == laneIndex)
            {
                return lane.itemId;
            }
        }

        return -1;
    }

    private static int CountSavedConveyorItems(ConveyorItemBlockState state)
    {
        if (state?.lanes == null || state.lanes.Count <= 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < state.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = state.lanes[i];
            if (lane != null && lane.itemId >= 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int GetMaxConveyorLaneIndex(ConveyorItemBlockState state)
    {
        int maxLaneIndex = -1;
        if (state?.lanes == null)
        {
            return maxLaneIndex;
        }

        for (int i = 0; i < state.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = state.lanes[i];
            if (lane != null)
            {
                maxLaneIndex = Mathf.Max(maxLaneIndex, lane.laneIndex);
            }
        }

        return maxLaneIndex;
    }

    private static void SetSavedConveyorLaneSettled(ConveyorItemLaneSaveState lane, Vector2Int coordinate)
    {
        if (lane == null)
        {
            return;
        }

        ClearSavedConveyorMotion(lane);
        lane.visualWorldPosition = new Vector3(coordinate.x, 0.2f, coordinate.y);
    }

    private static Vector3 ResolveSavedConveyorLaneWorldPosition(
        Vector2Int coordinate,
        ConveyorItemLaneSaveState lane,
        int laneIndex)
    {
        if (lane != null && lane.visualWorldPosition != default)
        {
            return lane.visualWorldPosition;
        }

        return new Vector3(coordinate.x, 0.2f, coordinate.y);
    }

    private static void ClearSavedConveyorMotion(ConveyorItemLaneSaveState lane)
    {
        if (lane == null)
        {
            return;
        }

        lane.hasMotion = false;
        lane.useCornerMotion = false;
        lane.sourceLaneIndex = -1;
        lane.destinationLaneIndex = -1;
        lane.startWorldPosition = default;
        lane.hasViaWorldPosition = false;
        lane.viaWorldPosition = default;
        lane.progress = 0f;
        lane.pathLength = 0f;
        lane.durationPathLength = 0f;
        lane.cornerContinuationActive = false;
        lane.cornerContinuationBlockCoordinate = default;
        lane.cornerContinuationSourceLaneIndex = -1;
        lane.cornerContinuationDestinationLaneIndex = -1;
        lane.cornerContinuationStartWorldPosition = default;
        lane.cornerContinuationStartProgress = 0f;
        lane.cornerContinuationPathLength = 0f;
        lane.cornerContinuationDurationPathLength = 0f;
    }

    private static List<ConveyorItemLaneSaveState> CloneConveyorLaneStates(IReadOnlyList<ConveyorItemLaneSaveState> source)
    {
        List<ConveyorItemLaneSaveState> results = new List<ConveyorItemLaneSaveState>(source != null ? source.Count : 0);
        if (source == null)
        {
            return results;
        }

        for (int i = 0; i < source.Count; i++)
        {
            ConveyorItemLaneSaveState clonedState = CloneConveyorLaneState(source[i]);
            if (clonedState != null)
            {
                results.Add(clonedState);
            }
        }

        return results;
    }

    private static ConveyorItemLaneSaveState CloneConveyorLaneState(ConveyorItemLaneSaveState source)
    {
        if (source == null)
        {
            return null;
        }

        ConveyorItemLaneSaveState clonedState = new ConveyorItemLaneSaveState
        {
            laneIndex = source.laneIndex,
            itemId = source.itemId,
            visualWorldPosition = source.visualWorldPosition,
            hasMotion = source.hasMotion,
            useCornerMotion = source.useCornerMotion,
            sourceLaneIndex = source.sourceLaneIndex,
            destinationLaneIndex = source.destinationLaneIndex,
            startWorldPosition = source.startWorldPosition,
            hasViaWorldPosition = source.hasViaWorldPosition,
            viaWorldPosition = source.viaWorldPosition,
            progress = source.progress,
            pathLength = source.pathLength,
            durationPathLength = source.durationPathLength,
            cornerContinuationActive = source.cornerContinuationActive,
            cornerContinuationBlockCoordinate = source.cornerContinuationBlockCoordinate,
            cornerContinuationSourceLaneIndex = source.cornerContinuationSourceLaneIndex,
            cornerContinuationDestinationLaneIndex = source.cornerContinuationDestinationLaneIndex,
            cornerContinuationStartWorldPosition = source.cornerContinuationStartWorldPosition,
            cornerContinuationStartProgress = source.cornerContinuationStartProgress,
            cornerContinuationPathLength = source.cornerContinuationPathLength,
            cornerContinuationDurationPathLength = source.cornerContinuationDurationPathLength
        };
        return TryNormalizeSavedConveyorLaneState(clonedState) ? clonedState : null;
    }
}
