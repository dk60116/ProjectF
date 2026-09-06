// Frozen before validation deduplication; exact production method bodies.
using UnityEngine;
partial class LegacyBlock : Block {
public bool TryMoveStraightConveyorDataLaneTo(Block destinationBlock, int sourceLaneIndex, int destinationLaneIndex)
    {
        if (destinationBlock == null
            || !CanUseStraightConveyorLineSimulationStructureOnly()
            || !destinationBlock.CanUseStraightConveyorLineSimulationStructureOnly()
            || !IsValidConveyorLaneIndex(sourceLaneIndex)
            || !destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            || IsConveyorDestinationLaneOccupied(destinationBlock, destinationLaneIndex)
            || !HasConveyorItemAtLane(sourceLaneIndex)
            || GetConveyorPortableObjectAtLane(sourceLaneIndex) != null
            || WasConveyorItemMovedThisFrame(sourceLaneIndex)
            || !IsConveyorItemReadyToMoveAtLane(sourceLaneIndex))
        {
            return false;
        }

        int itemId = GetConveyorItemIdAtLane(sourceLaneIndex);
        if (itemId < 0)
        {
            return false;
        }

        float pathLength = GetConveyorPathSegmentLength(
            sourceLaneIndex,
            destinationBlock,
            destinationLaneIndex,
            false);

        return TryMoveStraightConveyorDataLaneToCached(
            destinationBlock,
            sourceLaneIndex,
            destinationLaneIndex,
            pathLength);
    }
public bool TryMoveStraightConveyorDataLaneToCached(
        Block destinationBlock,
        int sourceLaneIndex,
        int destinationLaneIndex,
        float pathLength)
    {
        bool moved = TryMoveStraightConveyorDataLaneToCachedCore(
            destinationBlock,
            sourceLaneIndex,
            destinationLaneIndex,
            pathLength);
        MapObjectTickProfiler.AddBeltStraightMoveAttempt(moved);
        return moved;
    }
private bool TryMoveStraightConveyorDataLaneToCachedCore(
        Block destinationBlock,
        int sourceLaneIndex,
        int destinationLaneIndex,
        float pathLength)
    {
        if (destinationBlock == null
            || !IsValidConveyorLaneIndex(sourceLaneIndex)
            || !destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            || IsConveyorDestinationLaneOccupied(destinationBlock, destinationLaneIndex)
            || !HasConveyorItemAtLane(sourceLaneIndex)
            || GetConveyorPortableObjectAtLane(sourceLaneIndex) != null
            || WasConveyorItemMovedThisFrame(sourceLaneIndex)
            || !IsConveyorItemReadyToMoveAtLane(sourceLaneIndex))
        {
            return false;
        }

        int itemId = GetConveyorItemIdAtLane(sourceLaneIndex);
        if (itemId < 0)
        {
            return false;
        }

        ConveyorPickupGateState pickupGateState = GetConveyorPickupGateStateAtLane(sourceLaneIndex);
        pickupGateState.MarkSettled();
        Vector3 startWorldPosition = GetConveyorItemVisualWorldPosition(sourceLaneIndex);
        bool hasViaWorldPosition = TryGetConveyorLinearMoveViaWorldPosition(
            sourceLaneIndex,
            destinationBlock,
            destinationLaneIndex,
            startWorldPosition,
            out Vector3 viaWorldPosition);
        if (hasViaWorldPosition)
        {
            Vector3 destinationWorldPosition = destinationBlock.GetConveyorLaneWorldPosition(destinationLaneIndex);
            float viaPathLength =
                Vector3.Distance(startWorldPosition, viaWorldPosition)
                + Vector3.Distance(viaWorldPosition, destinationWorldPosition);
            if (viaPathLength > ConveyorContinuousMotionEpsilon)
            {
                pathLength = viaPathLength;
            }
        }

        ClearConveyorItemAtLane(sourceLaneIndex);
        destinationBlock.SetConveyorItemAtLane(destinationLaneIndex, itemId, null, pickupGateState);
        ConveyorDataMotionState dataMotionState = new ConveyorDataMotionState
        {
            active = true,
            useCornerMotion = false,
            startWorldPosition = startWorldPosition,
            hasViaWorldPosition = hasViaWorldPosition,
            viaWorldPosition = viaWorldPosition,
            destinationLaneIndex = destinationLaneIndex,
            progress = 0f,
            pathLength = pathLength
        };
        destinationBlock.conveyorItemMotionStates[destinationLaneIndex] =
            destinationBlock.InitializeConveyorDataMotionTiming(dataMotionState, 0f);
        destinationBlock.MarkConveyorItemVisualDirty();
        destinationBlock.MarkConveyorItemMovedThisFrame(destinationLaneIndex);
        return true;
    }
public bool CanMoveStraightConveyorDataLaneToCached(
        Block destinationBlock,
        int sourceLaneIndex,
        int destinationLaneIndex)
    {
        return destinationBlock != null
            && IsValidConveyorLaneIndex(sourceLaneIndex)
            && destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            && !IsConveyorDestinationLaneOccupied(destinationBlock, destinationLaneIndex)
            && HasStraightConveyorDataItemAtLane(sourceLaneIndex)
            && !WasConveyorItemMovedThisFrame(sourceLaneIndex)
            && IsConveyorItemReadyToMoveAtLane(sourceLaneIndex);
    }
}
