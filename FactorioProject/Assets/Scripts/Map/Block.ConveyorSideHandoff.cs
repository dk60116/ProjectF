using ProjectF.Conveyors;
using UnityEngine;

public partial class Block
{
    // Slot 1 is otherwise unused by a flat single-line belt. On a side input it
    // holds the item just before the turn, splitting the long handoff into two
    // normal-spacing reservations. Bridge belts retain their own slot 1.
    private const int ConveyorSideExitLaneIndex = 1;

    private bool TryGetConveyorSideHandoffFlow(
        Block sourceBlock,
        int sourceLaneIndex,
        out Vector2Int destinationFlow)
    {
        destinationFlow = default;
        if (sourceBlock == null
            || sourceBlock == this
            || IsCornerConveyor()
            || !TryGetConveyorFlowDirection(out destinationFlow)
            || !ConveyorSideHandoffPath.IsSideEntry(coordinate - sourceBlock.coordinate, destinationFlow))
        {
            return false;
        }

        if (TryGetRuntimeBelt2F(out _)
            || sourceBlock.TryGetConveyorItemBelt2F(sourceLaneIndex, out _))
        {
            // Use the same terminal-only rule as topology. An upper bridge lane
            // crossing a 1F belt must never become a side input on the lower belt.
            return sourceBlock.CanUseBelt2FTerminalSideHandoff(
                this, -destinationFlow, coordinate - sourceBlock.coordinate);
        }

        return true;
    }

    private Vector3 GetConveyorSideHandoffTurnPosition(
        Vector3 start,
        Vector3 destination,
        int destinationLaneIndex,
        Vector2Int destinationFlow)
    {
        Vector3 turn = ConveyorSideHandoffPath.GetTurnPosition(start, destination, destinationFlow);
        // The turn is on the receiving belt. Match its surface height, including
        // a 2F entry ramp, without moving sideways before reaching its centerline.
        if (TryGetConveyorItemBelt2F(destinationLaneIndex, out ConvayorBelt2F belt2F))
        {
            return belt2F.ApplyPathHeight(turn);
        }

        turn.y = destination.y;
        return turn;
    }

    private bool CanUseConveyorSideExitLane()
    {
        return IsConveyorStackingEnabled()
            && !IsCornerConveyor()
            && !TryGetRuntimeBelt2F(out _)
            && !TryGetBelt2FBridgeCenterBelt(out _);
    }

    private bool HasConveyorSideExitLane()
    {
        if (!CanUseConveyorSideExitLane())
        {
            return false;
        }

        // Keep an occupied approach slot alive if the receiving belt is removed
        // or rotated. It must drain through the new successor, not be discarded
        // by legacy inactive-lane normalization.
        return HasConveyorStoredItemAtLane(ConveyorSideExitLaneIndex)
            || (TryGetNextConveyorBlock(out Block destinationBlock)
                && destinationBlock != null
                && destinationBlock.TryGetConveyorSideHandoffFlow(
                    this, ConveyorSingleLineFrontLaneIndex, out _));
    }
}
