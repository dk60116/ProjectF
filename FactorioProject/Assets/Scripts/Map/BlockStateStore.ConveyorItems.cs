using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    private const int ConveyorStackStateSentinel = -1000000002;
    private const float ConveyorBackgroundSimulationEpsilon = 0.0001f;
    private const int ConveyorBackgroundSimulationPassMultiplier = 64;
    private const int ConveyorBackgroundDefaultSimulationPasses = 256;
    private const float VirtualConveyorLanePathLength = 0.5f;

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

    private readonly Dictionary<Vector2Int, ConveyorItemBlockState> savedConveyorItemStates = new Dictionary<Vector2Int, ConveyorItemBlockState>();
    private readonly Dictionary<ConveyorLaneKey, ConveyorItemLaneSaveState> conveyorSimulationOccupancy = new Dictionary<ConveyorLaneKey, ConveyorItemLaneSaveState>();
    private readonly List<ConveyorSimulationItem> conveyorSimulationItems = new List<ConveyorSimulationItem>();
    private readonly HashSet<Vector2Int> conveyorSimulationDirtyCoordinates = new HashSet<Vector2Int>();

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
        SyncConveyorFloorObjects(worldCoordinate, state);
    }

    public void RemoveConveyorItems(Vector2Int worldCoordinate)
    {
        savedConveyorItemStates.Remove(worldCoordinate);
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
        SyncConveyorFloorObjects(destinationCoordinate, destinationState);
        return true;
    }

    public void SetConveyorItems(Vector2Int worldCoordinate, IReadOnlyList<ConveyorItemLaneSaveState> lanes)
    {
        if (lanes == null || lanes.Count <= 0)
        {
            savedConveyorItemStates.Remove(worldCoordinate);
            return;
        }

        savedConveyorItemStates.TryGetValue(worldCoordinate, out ConveyorItemBlockState existingState);
        ConveyorItemBlockState state = existingState ?? new ConveyorItemBlockState();
        state.lanes.Clear();
        for (int i = 0; i < lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = CloneConveyorLaneState(lanes[i]);
            if (lane == null || lane.itemId < 0 || lane.laneIndex < 0)
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
        state.lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks;
        savedConveyorItemStates[worldCoordinate] = state;
        SyncConveyorFloorObjects(worldCoordinate, state);
    }

    public void SimulateSavedConveyorItems(int maxPassesOverride = -1, ICollection<Vector2Int> dirtyCoordinates = null)
    {
        if (!Application.isPlaying || savedConveyorItemStates.Count <= 0)
        {
            return;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        BuildConveyorSimulationItems(nowTicks);
        if (conveyorSimulationItems.Count <= 0)
        {
            FlushConveyorSimulationDirtyStates();
            CopyConveyorSimulationDirtyCoordinates(dirtyCoordinates);
            ClearConveyorSimulationBuffers();
            return;
        }

        int passLimit = maxPassesOverride > 0
            ? Mathf.Max(1, maxPassesOverride * ConveyorBackgroundSimulationPassMultiplier)
            : ConveyorBackgroundDefaultSimulationPasses;

        for (int passIndex = 0; passIndex < passLimit; passIndex++)
        {
            bool movedAny = false;
            for (int itemIndex = 0; itemIndex < conveyorSimulationItems.Count; itemIndex++)
            {
                ConveyorSimulationItem item = conveyorSimulationItems[itemIndex];
                if (item.lane == null
                    || item.lane.itemId < 0
                    || item.budgetDistance <= ConveyorBackgroundSimulationEpsilon)
                {
                    continue;
                }

                if (!TryMoveConveyorSimulationItem(ref item))
                {
                    continue;
                }

                conveyorSimulationItems[itemIndex] = item;
                movedAny = true;
            }

            if (!movedAny)
            {
                break;
            }
        }

        FlushConveyorSimulationDirtyStates();
        CopyConveyorSimulationDirtyCoordinates(dirtyCoordinates);
        ClearConveyorSimulationBuffers();
    }

    private void BuildConveyorSimulationItems(long nowTicks)
    {
        conveyorSimulationOccupancy.Clear();
        conveyorSimulationItems.Clear();
        conveyorSimulationDirtyCoordinates.Clear();

        foreach (KeyValuePair<Vector2Int, ConveyorItemBlockState> pair in savedConveyorItemStates)
        {
            ConveyorItemBlockState state = pair.Value;
            if (state == null || state.lanes.Count <= 0)
            {
                continue;
            }

            for (int i = 0; i < state.lanes.Count; i++)
            {
                ConveyorItemLaneSaveState lane = state.lanes[i];
                if (lane == null || lane.itemId < 0 || lane.laneIndex < 0)
                {
                    continue;
                }

                conveyorSimulationOccupancy[new ConveyorLaneKey(pair.Key, lane.laneIndex)] = lane;
            }
        }

        foreach (KeyValuePair<Vector2Int, ConveyorItemBlockState> pair in savedConveyorItemStates)
        {
            ConveyorItemBlockState state = pair.Value;
            if (state == null || state.lanes.Count <= 0)
            {
                continue;
            }

            float budgetDistance = ResolveConveyorSimulationBudgetDistance(state, nowTicks);
            state.lastBackgroundSimulationTicks = nowTicks;
            if (budgetDistance <= ConveyorBackgroundSimulationEpsilon)
            {
                continue;
            }

            for (int i = 0; i < state.lanes.Count; i++)
            {
                ConveyorItemLaneSaveState lane = state.lanes[i];
                if (lane == null || lane.itemId < 0 || lane.laneIndex < 0)
                {
                    continue;
                }

                conveyorSimulationItems.Add(new ConveyorSimulationItem
                {
                    coordinate = pair.Key,
                    lane = lane,
                    budgetDistance = budgetDistance
                });
            }
        }
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

    private bool TryAdvanceSavedConveyorLaneMotion(
        ConveyorItemBlockState sourceState,
        ref ConveyorSimulationItem item,
        ConveyorLaneLinkSaveState link,
        out bool motionCompleted)
    {
        motionCompleted = false;
        ConveyorItemLaneSaveState lane = item.lane;
        float budgetDistance = item.budgetDistance;

        if (lane == null || !lane.hasMotion)
        {
            return false;
        }

        float pathLength = ResolveSavedConveyorMotionPathLength(lane);
        if (pathLength <= ConveyorBackgroundSimulationEpsilon)
        {
            pathLength = link != null ? link.pathLength : 0f;
        }

        if (pathLength <= ConveyorBackgroundSimulationEpsilon)
        {
            ClearSavedConveyorMotion(lane);
            conveyorSimulationDirtyCoordinates.Add(item.coordinate);
            motionCompleted = true;
            return true;
        }

        float remainingDistance = pathLength * (1f - Mathf.Clamp01(lane.progress));
        if (budgetDistance + ConveyorBackgroundSimulationEpsilon < remainingDistance)
        {
            lane.progress = Mathf.Clamp01(lane.progress + (budgetDistance / pathLength));
            item.budgetDistance = 0f;
            conveyorSimulationDirtyCoordinates.Add(item.coordinate);
            return true;
        }

        lane.progress = 1f;
        item.budgetDistance = Mathf.Max(0f, budgetDistance - remainingDistance);
        conveyorSimulationDirtyCoordinates.Add(item.coordinate);
        motionCompleted = true;
        return true;
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

    private bool TryMoveConveyorSimulationItem(ref ConveyorSimulationItem item)
    {
        if (item.lane == null
            || item.lane.itemId < 0
            || !TryEnsureConveyorItemState(item.coordinate, out ConveyorItemBlockState sourceState)
            || sourceState == null
            || !TryGetConveyorLaneLink(sourceState, item.lane.laneIndex, out ConveyorLaneLinkSaveState link)
            || link == null
            || link.destinationLaneIndex < 0
            || link.pathLength <= ConveyorBackgroundSimulationEpsilon)
        {
            return false;
        }

        if (item.lane.hasMotion)
        {
            if (!TryAdvanceSavedConveyorLaneMotion(sourceState, ref item, link, out bool motionCompleted))
            {
                return false;
            }

            if (!motionCompleted)
            {
                return true;
            }
        }
        else if (item.budgetDistance + ConveyorBackgroundSimulationEpsilon < link.pathLength)
        {
            BeginSavedConveyorLaneMotion(item.lane, item.coordinate, link, item.budgetDistance / link.pathLength);
            item.budgetDistance = 0f;
            conveyorSimulationDirtyCoordinates.Add(item.coordinate);
            return true;
        }
        else
        {
            item.budgetDistance = Mathf.Max(0f, item.budgetDistance - link.pathLength);
        }

        if (!TryEnsureConveyorItemState(link.destinationCoordinate, out ConveyorItemBlockState destinationState)
            || !IsValidConveyorDestinationLane(destinationState, link.destinationLaneIndex))
        {
            return false;
        }

        ConveyorLaneKey sourceKey = new ConveyorLaneKey(item.coordinate, item.lane.laneIndex);
        ConveyorLaneKey destinationKey = new ConveyorLaneKey(link.destinationCoordinate, link.destinationLaneIndex);
        if (sourceKey.Equals(destinationKey) || conveyorSimulationOccupancy.ContainsKey(destinationKey))
        {
            if (item.lane.hasMotion)
            {
                item.lane.progress = 1f;
                conveyorSimulationDirtyCoordinates.Add(item.coordinate);
            }

            return false;
        }

        conveyorSimulationOccupancy.Remove(sourceKey);
        sourceState.lanes.Remove(item.lane);

        item.lane.laneIndex = link.destinationLaneIndex;
        SetSavedConveyorLaneSettled(item.lane, link.destinationCoordinate);
        destinationState.lanes.Add(item.lane);
        destinationState.laneCount = Mathf.Max(destinationState.laneCount, link.destinationLaneIndex + 1);
        conveyorSimulationOccupancy[destinationKey] = item.lane;

        conveyorSimulationDirtyCoordinates.Add(item.coordinate);
        conveyorSimulationDirtyCoordinates.Add(link.destinationCoordinate);

        item.coordinate = link.destinationCoordinate;
        return true;
    }

    private void BeginSavedConveyorLaneMotion(
        ConveyorItemLaneSaveState lane,
        Vector2Int sourceCoordinate,
        ConveyorLaneLinkSaveState link,
        float progress)
    {
        if (lane == null || link == null)
        {
            return;
        }

        lane.hasMotion = true;
        lane.useCornerMotion = IsVirtualCornerInternalLink(sourceCoordinate, lane.laneIndex, link);
        lane.sourceLaneIndex = lane.laneIndex;
        lane.destinationLaneIndex = link.destinationLaneIndex;
        lane.startWorldPosition = lane.visualWorldPosition;
        lane.hasViaWorldPosition = false;
        lane.viaWorldPosition = default;
        lane.progress = Mathf.Clamp01(progress);
        lane.pathLength = Mathf.Max(ConveyorBackgroundSimulationEpsilon, link.pathLength);
        lane.durationPathLength = lane.pathLength;
        lane.cornerContinuationActive = false;
        lane.cornerContinuationBlockCoordinate = default;
        lane.cornerContinuationSourceLaneIndex = -1;
        lane.cornerContinuationDestinationLaneIndex = -1;
        lane.cornerContinuationStartWorldPosition = default;
        lane.cornerContinuationStartProgress = 0f;
        lane.cornerContinuationPathLength = 0f;
        lane.cornerContinuationDurationPathLength = 0f;
    }

    private bool IsVirtualCornerInternalLink(
        Vector2Int sourceCoordinate,
        int sourceLaneIndex,
        ConveyorLaneLinkSaveState link)
    {
        return link != null
            && link.destinationCoordinate == sourceCoordinate
            && sourceLaneIndex >= 2
            && link.destinationLaneIndex >= 0
            && link.destinationLaneIndex <= 1
            && TryResolveVirtualConveyor(
                sourceCoordinate,
                out ConveyorBelt conveyor,
                out _,
                out _)
            && conveyor != null
            && conveyor.IsCornerVariant;
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
        if (conveyor.IsCornerVariant)
        {
            if (IsCounterClockwiseTurn(inputDirection, outputDirection))
            {
                AddVirtualConveyorLaneLink(state, 3, worldCoordinate, 0, VirtualConveyorLanePathLength);
                AddVirtualConveyorLaneLink(state, 2, worldCoordinate, 1, VirtualConveyorLanePathLength);
            }
            else
            {
                AddVirtualConveyorLaneLink(state, 2, worldCoordinate, 0, VirtualConveyorLanePathLength);
                AddVirtualConveyorLaneLink(state, 3, worldCoordinate, 1, VirtualConveyorLanePathLength);
            }
        }
        else
        {
            AddVirtualConveyorLaneLink(state, 2, worldCoordinate, 0, VirtualConveyorLanePathLength);
            AddVirtualConveyorLaneLink(state, 3, worldCoordinate, 1, VirtualConveyorLanePathLength);
        }

        Vector2Int nextCoordinate = worldCoordinate + outputDirection;
        if (TryResolveVirtualConveyorReceiveLane(nextCoordinate, outputDirection, 0, out int destinationLane0))
        {
            AddVirtualConveyorLaneLink(state, 0, nextCoordinate, destinationLane0, VirtualConveyorLanePathLength);
        }

        if (TryResolveVirtualConveyorReceiveLane(nextCoordinate, outputDirection, 1, out int destinationLane1))
        {
            AddVirtualConveyorLaneLink(state, 1, nextCoordinate, destinationLane1, VirtualConveyorLanePathLength);
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
            || sourceColumnOrdinal < 0
            || !TryResolveVirtualConveyor(
                destinationCoordinate,
                out _,
                out Vector2Int inputDirection,
                out _)
            || inputDirection != -incomingFlowDirection)
        {
            return false;
        }

        destinationLaneIndex = sourceColumnOrdinal == 0 ? 2 : 3;
        return true;
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

        ItemDefinition definition = ResolveVirtualConveyorItemDefinition(state.itemId);
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

    private static ItemDefinition ResolveVirtualConveyorItemDefinition(int itemId)
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

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private static void AddVirtualConveyorLaneLink(
        ConveyorItemBlockState state,
        int sourceLaneIndex,
        Vector2Int destinationCoordinate,
        int destinationLaneIndex,
        float pathLength)
    {
        if (state == null || sourceLaneIndex < 0 || destinationLaneIndex < 0)
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

    private static bool IsCounterClockwiseTurn(Vector2Int inputDirection, Vector2Int outputDirection)
    {
        return (inputDirection.x * outputDirection.y) - (inputDirection.y * outputDirection.x) > 0;
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
        return state != null && laneIndex >= 0 && laneIndex < Mathf.Max(0, state.laneCount);
    }

    private void FlushConveyorSimulationDirtyStates()
    {
        foreach (Vector2Int coordinate in conveyorSimulationDirtyCoordinates)
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

        foreach (Vector2Int coordinate in conveyorSimulationDirtyCoordinates)
        {
            results.Add(coordinate);
        }
    }

    private void ClearConveyorSimulationBuffers()
    {
        conveyorSimulationOccupancy.Clear();
        conveyorSimulationItems.Clear();
        conveyorSimulationDirtyCoordinates.Clear();
    }

    private void SyncConveyorFloorObjects(Vector2Int worldCoordinate, ConveyorItemBlockState state)
    {
        if (state == null || state.lanes.Count <= 0)
        {
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

        return new ConveyorItemLaneSaveState
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
    }
}
