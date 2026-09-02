using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    private const int ConveyorStackStateSentinel = -1000000002;
    private const int ConveyorSingleLineFrontLaneIndex = 0;
    private const int ConveyorSingleLineBackLaneIndex = 2;

    private sealed class ConveyorItemBlockState
    {
        public int laneCount;
        public bool useExtendedLaneIndices;
        public readonly List<ConveyorItemLaneSaveState> lanes = new List<ConveyorItemLaneSaveState>();
    }

    private readonly Dictionary<Vector2Int, ConveyorItemBlockState> savedConveyorItemStates =
        new Dictionary<Vector2Int, ConveyorItemBlockState>();

    private static bool IsSavedConveyorActiveLaneIndex(int laneIndex, bool useExtendedLaneIndices = false)
    {
        if (useExtendedLaneIndices)
        {
            return laneIndex >= 0 && laneIndex < Block.ConveyorCellItemUnit;
        }

        return laneIndex == ConveyorSingleLineFrontLaneIndex
            || laneIndex == ConveyorSingleLineBackLaneIndex;
    }

    private static bool TryNormalizeSavedConveyorLaneIndex(
        int laneIndex,
        bool useExtendedLaneIndices,
        out int normalizedLaneIndex)
    {
        if (useExtendedLaneIndices)
        {
            normalizedLaneIndex = laneIndex;
            return laneIndex >= 0 && laneIndex < Block.ConveyorCellItemUnit;
        }

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

    private static bool TryNormalizeSavedConveyorLaneState(
        ConveyorItemLaneSaveState lane,
        bool useExtendedLaneIndices = false)
    {
        if (lane == null
            || !TryNormalizeSavedConveyorLaneIndex(
                lane.laneIndex,
                useExtendedLaneIndices,
                out int normalizedLaneIndex))
        {
            return false;
        }

        lane.laneIndex = normalizedLaneIndex;
        NormalizeOptionalLaneIndex(ref lane.sourceLaneIndex, useExtendedLaneIndices);
        NormalizeOptionalLaneIndex(ref lane.destinationLaneIndex, useExtendedLaneIndices);
        NormalizeOptionalLaneIndex(ref lane.cornerContinuationSourceLaneIndex, useExtendedLaneIndices);
        NormalizeOptionalLaneIndex(ref lane.cornerContinuationDestinationLaneIndex, useExtendedLaneIndices);
        return true;
    }

    private static void NormalizeOptionalLaneIndex(ref int laneIndex, bool useExtendedLaneIndices)
    {
        if (laneIndex >= 0
            && TryNormalizeSavedConveyorLaneIndex(laneIndex, useExtendedLaneIndices, out int normalizedLaneIndex))
        {
            laneIndex = normalizedLaneIndex;
        }
    }

    private static bool HasExtendedConveyorLaneData(IReadOnlyList<ConveyorItemLaneSaveState> lanes)
    {
        if (lanes == null)
        {
            return false;
        }

        for (int i = 0; i < lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = lanes[i];
            if (IsExtendedSavedConveyorLaneIndex(lane?.laneIndex ?? -1)
                || IsExtendedSavedConveyorLaneIndex(lane?.sourceLaneIndex ?? -1)
                || IsExtendedSavedConveyorLaneIndex(lane?.destinationLaneIndex ?? -1)
                || IsExtendedSavedConveyorLaneIndex(lane?.cornerContinuationSourceLaneIndex ?? -1)
                || IsExtendedSavedConveyorLaneIndex(lane?.cornerContinuationDestinationLaneIndex ?? -1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExtendedSavedConveyorLaneIndex(int laneIndex)
    {
        return laneIndex == 1 || laneIndex == 3;
    }

    public void SaveConveyorItems(Vector2Int worldCoordinate, Block block)
    {
        if (block == null || !block.IsRuntimeConveyor)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            return;
        }

        ConveyorItemBlockState state = new ConveyorItemBlockState
        {
            laneCount = Mathf.Max(0, block.GetRuntimeConveyorLaneCount()),
            useExtendedLaneIndices = block.HasRuntimeBelt2FConveyor()
        };
        block.CaptureConveyorItemSaveStates(state.lanes);
        RemoveInvalidOrDuplicateLanes(state);

        if (state.lanes.Count <= 0)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            SyncConveyorFloorObjects(worldCoordinate, null);
            return;
        }

        savedConveyorItemStates[worldCoordinate] = state;
        SyncConveyorFloorObjects(worldCoordinate, state);
    }

    public void RemoveConveyorItems(Vector2Int worldCoordinate)
    {
        savedConveyorItemStates.Remove(worldCoordinate);
    }

    public int RemoveConveyorItemLanes(Vector2Int worldCoordinate, ICollection<int> laneIndices)
    {
        RemoveConveyorItemLanesInternal(worldCoordinate, laneIndices, false, out int removedCount);
        return removedCount;
    }

    public bool TryRemoveConveyorItemLanes(
        Vector2Int worldCoordinate,
        ICollection<int> laneIndices,
        out int removedCount)
    {
        return RemoveConveyorItemLanesInternal(worldCoordinate, laneIndices, true, out removedCount);
    }

    private bool RemoveConveyorItemLanesInternal(
        Vector2Int worldCoordinate,
        ICollection<int> laneIndices,
        bool requireAllLanes,
        out int removedCount)
    {
        removedCount = 0;
        if (laneIndices == null
            || laneIndices.Count <= 0
            || !savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState state)
            || state == null)
        {
            return false;
        }

        if (requireAllLanes)
        {
            foreach (int laneIndex in laneIndices)
            {
                if (laneIndex >= 0 && GetSavedConveyorLaneItemId(state, laneIndex) < 0)
                {
                    return false;
                }
            }
        }

        for (int i = state.lanes.Count - 1; i >= 0; i--)
        {
            ConveyorItemLaneSaveState lane = state.lanes[i];
            if (lane != null && laneIndices.Contains(lane.laneIndex))
            {
                state.lanes.RemoveAt(i);
                removedCount++;
            }
        }

        if (removedCount <= 0)
        {
            return false;
        }

        SyncConveyorFloorObjects(worldCoordinate, state);
        WakeSavedConveyorNeighbors(worldCoordinate);
        return true;
    }

    public bool TryGetConveyorItems(Vector2Int worldCoordinate, out List<ConveyorItemLaneSaveState> lanes)
    {
        if (savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState state)
            && state != null
            && state.lanes.Count > 0)
        {
            lanes = CloneConveyorLaneStates(state.lanes, state.useExtendedLaneIndices);
            return true;
        }

        lanes = null;
        return false;
    }

    public int GetSavedConveyorItemCount()
    {
        int count = 0;
        foreach (KeyValuePair<Vector2Int, ConveyorItemBlockState> pair in savedConveyorItemStates)
        {
            count += CountSavedConveyorItems(pair.Value);
        }

        return count;
    }

    public int GetSavedConveyorItemCount(Vector2Int worldCoordinate)
    {
        return savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState state)
            ? CountSavedConveyorItems(state)
            : 0;
    }

    public bool HasSavedConveyorItemAtLane(Vector2Int worldCoordinate, int laneIndex)
    {
        return laneIndex >= 0
            && savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState state)
            && GetSavedConveyorLaneItemId(state, laneIndex) >= 0;
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
                out _))
        {
            return false;
        }

        itemId = lane.itemId;
        state.lanes.Remove(lane);
        SyncConveyorFloorObjects(worldCoordinate, state);
        WakeSavedConveyorNeighbors(worldCoordinate);
        TerrainGenerator.ResolveActive()?.NotifyConveyorItemRemovedFromBelt();
        return true;
    }

    public bool CanAddSavedConveyorItem(Vector2Int worldCoordinate, int itemId, Vector3 referenceWorldPosition)
    {
        return itemId >= 0
            && TryFindSavedConveyorPlacementLane(worldCoordinate, out _, out _);
    }

    public bool TryAddSavedConveyorItem(Vector2Int worldCoordinate, int itemId, Vector3 referenceWorldPosition)
    {
        if (itemId < 0
            || !TryFindSavedConveyorPlacementLane(
                worldCoordinate,
                out ConveyorItemBlockState state,
                out int laneIndex))
        {
            return false;
        }

        ConveyorItemLaneSaveState lane = new ConveyorItemLaneSaveState
        {
            laneIndex = laneIndex,
            itemId = itemId,
            visualWorldPosition = new Vector3(worldCoordinate.x, 0.2f, worldCoordinate.y)
        };
        state.lanes.Add(lane);
        savedConveyorItemStates[worldCoordinate] = state;
        SyncConveyorFloorObjects(worldCoordinate, state);
        WakeSavedConveyorNeighbors(worldCoordinate);
        TerrainGenerator.ResolveActive()?.NotifyConveyorItemAddedToBelt();
        return true;
    }

    public void SetConveyorItems(Vector2Int worldCoordinate, IReadOnlyList<ConveyorItemLaneSaveState> lanes)
    {
        if (lanes == null || lanes.Count <= 0)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            return;
        }

        ConveyorItemBlockState state = new ConveyorItemBlockState
        {
            useExtendedLaneIndices = HasExtendedConveyorLaneData(lanes)
        };
        for (int i = 0; i < lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = CloneConveyorLaneState(lanes[i], state.useExtendedLaneIndices);
            if (lane == null
                || lane.itemId < 0
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
            return;
        }

        state.laneCount = Mathf.Clamp(
            Mathf.Max(state.laneCount, GetMaxConveyorLaneIndex(state) + 1),
            0,
            Block.ConveyorCellItemUnit);
        savedConveyorItemStates[worldCoordinate] = state;
        SyncConveyorFloorObjects(worldCoordinate, state);
    }

    private static void RemoveInvalidOrDuplicateLanes(ConveyorItemBlockState state)
    {
        HashSet<int> occupiedLaneIndices = new HashSet<int>();
        for (int i = state.lanes.Count - 1; i >= 0; i--)
        {
            ConveyorItemLaneSaveState lane = state.lanes[i];
            if (lane == null
                || lane.itemId < 0
                || !TryNormalizeSavedConveyorLaneState(lane, state.useExtendedLaneIndices)
                || !occupiedLaneIndices.Add(lane.laneIndex))
            {
                state.lanes.RemoveAt(i);
            }
        }
    }

    private bool TryFindSavedConveyorItemLane(
        Vector2Int worldCoordinate,
        Predicate<int> itemFilter,
        Vector3 referenceWorldPosition,
        out ConveyorItemBlockState state,
        out ConveyorItemLaneSaveState lane,
        out Vector3 itemWorldPosition)
    {
        lane = null;
        itemWorldPosition = default;
        if (!savedConveyorItemStates.TryGetValue(worldCoordinate, out state) || state == null)
        {
            return false;
        }

        float bestDistanceSqr = float.PositiveInfinity;
        for (int i = 0; i < state.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState candidate = state.lanes[i];
            if (candidate == null
                || candidate.itemId < 0
                || (itemFilter != null && !itemFilter(candidate.itemId)))
            {
                continue;
            }

            Vector3 candidatePosition = candidate.visualWorldPosition != default
                ? candidate.visualWorldPosition
                : new Vector3(worldCoordinate.x, 0.2f, worldCoordinate.y);
            float distanceSqr = (candidatePosition - referenceWorldPosition).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            lane = candidate;
            itemWorldPosition = candidatePosition;
            bestDistanceSqr = distanceSqr;
        }

        return lane != null;
    }

    private bool TryFindSavedConveyorPlacementLane(
        Vector2Int worldCoordinate,
        out ConveyorItemBlockState state,
        out int laneIndex)
    {
        laneIndex = -1;
        if (!savedConveyorItemStates.TryGetValue(worldCoordinate, out state) || state == null)
        {
            return false;
        }

        int candidateCount = state.useExtendedLaneIndices ? Block.ConveyorCellItemUnit : 2;
        for (int i = 0; i < candidateCount; i++)
        {
            int candidateLaneIndex = state.useExtendedLaneIndices
                ? i
                : i == 0
                    ? ConveyorSingleLineFrontLaneIndex
                    : ConveyorSingleLineBackLaneIndex;
            if (GetSavedConveyorLaneItemId(state, candidateLaneIndex) < 0)
            {
                laneIndex = candidateLaneIndex;
                return true;
            }
        }

        return false;
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
        int[] itemIds = new int[laneCount + 2];
        itemIds[0] = ConveyorStackStateSentinel;
        itemIds[1] = laneCount;
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            itemIds[laneIndex + 2] = GetSavedConveyorLaneItemId(state, laneIndex);
        }

        SetFloorObjectsFromOwnedRawItems(worldCoordinate, itemIds);
    }

    private static void WakeSavedConveyorNeighbors(Vector2Int worldCoordinate)
    {
        RobotArm.WakeAroundCoordinate(worldCoordinate);
        InputOutputModule.WakeRuntimeModulesAtCoordinate(worldCoordinate);
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
        int count = 0;
        if (state?.lanes == null)
        {
            return count;
        }

        for (int i = 0; i < state.lanes.Count; i++)
        {
            if (state.lanes[i]?.itemId >= 0)
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
            if (state.lanes[i] != null)
            {
                maxLaneIndex = Mathf.Max(maxLaneIndex, state.lanes[i].laneIndex);
            }
        }

        return maxLaneIndex;
    }

    private static List<ConveyorItemLaneSaveState> CloneConveyorLaneStates(
        IReadOnlyList<ConveyorItemLaneSaveState> source,
        bool useExtendedLaneIndices = false)
    {
        List<ConveyorItemLaneSaveState> results =
            new List<ConveyorItemLaneSaveState>(source != null ? source.Count : 0);
        if (source == null)
        {
            return results;
        }

        for (int i = 0; i < source.Count; i++)
        {
            ConveyorItemLaneSaveState clonedState = CloneConveyorLaneState(source[i], useExtendedLaneIndices);
            if (clonedState != null)
            {
                results.Add(clonedState);
            }
        }

        return results;
    }

    private static ConveyorItemLaneSaveState CloneConveyorLaneState(
        ConveyorItemLaneSaveState source,
        bool useExtendedLaneIndices = false)
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
        return TryNormalizeSavedConveyorLaneState(clonedState, useExtendedLaneIndices)
            ? clonedState
            : null;
    }
}
