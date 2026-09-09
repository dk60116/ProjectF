using UnityEngine;

public partial class Block
{
    // One geometric routing implementation for simulation and lane-group inspection.
    // topologyOnly bypasses item storage/readiness; normal simulation retains its checks.
    private bool TryResolveConveyorSuccessorUncached(
        int sourceLaneIndex,
        out Block destinationBlock,
        out int destinationLaneIndex,
        out bool useCornerMotion,
        bool topologyOnly = false)
    {
        destinationBlock = null;
        destinationLaneIndex = -1;
        useCornerMotion = false;

        if (!(topologyOnly ? IsBeltSplitLane(sourceLaneIndex) : IsValidConveyorLaneIndex(sourceLaneIndex)))
        {
            return false;
        }

        if (TryResolveBelt2FBridgeCenterSuccessor(
                sourceLaneIndex,
                out destinationBlock,
                out destinationLaneIndex, topologyOnly))
        {
            return true;
        }

        if (IsCornerConveyor())
        {
            if (sourceLaneIndex == 0 || sourceLaneIndex == 1)
            {
                if (!TryGetNextConveyorBlock(out destinationBlock))
                {
                    return false;
                }

                Vector3 handoffWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
                if (TryGetConveyorCornerLaneTransition(sourceLaneIndex, out int cornerSourceLaneIndex, out int cornerDestinationLaneIndex, out _)
                    && TryGetCornerConveyorHandoffWorldPosition(cornerSourceLaneIndex, cornerDestinationLaneIndex, out Vector3 resolvedHandoffWorldPosition))
                {
                    handoffWorldPosition = resolvedHandoffWorldPosition;
                }

                return TryGetConveyorHandoffReceiveLaneIndex(
                    this,
                    sourceLaneIndex,
                    destinationBlock,
                    handoffWorldPosition,
                    out destinationLaneIndex, topologyOnly);
            }

            if (!TryGetConveyorCornerLaneCandidates(
                    out int outerSourceLaneIndex,
                    out int outerDestinationLaneIndex,
                    out int innerSourceLaneIndex,
                    out int innerDestinationLaneIndex))
            {
                return false;
            }

            if (sourceLaneIndex == outerSourceLaneIndex)
            {
                destinationBlock = this;
                destinationLaneIndex = outerDestinationLaneIndex;
                useCornerMotion = true;
                return true;
            }

            if (sourceLaneIndex == innerSourceLaneIndex)
            {
                destinationBlock = this;
                destinationLaneIndex = innerDestinationLaneIndex;
                useCornerMotion = true;
                return true;
            }

            return false;
        }

        int frontLaneIndex = ConveyorSingleLineFrontLaneIndex;
        int backLaneIndex = ConveyorSingleLineBackLaneIndex;
        if (!topologyOnly && !TryGetConveyorLaneLayout(out frontLaneIndex, out backLaneIndex))
        {
            return false;
        }

        bool hasSideExitLane = topologyOnly ? HasBeltSplitSideExitLane() : HasConveyorSideExitLane();
        if (sourceLaneIndex == frontLaneIndex && hasSideExitLane)
        {
            destinationBlock = this;
            destinationLaneIndex = ConveyorSideExitLaneIndex;
            return true;
        }

        if (sourceLaneIndex == frontLaneIndex
            || (sourceLaneIndex == ConveyorSideExitLaneIndex && hasSideExitLane))
        {
            if (!TryGetNextConveyorBlock(out destinationBlock))
            {
                return false;
            }

            Vector3 handoffWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
            return TryGetConveyorHandoffReceiveLaneIndex(
                this,
                sourceLaneIndex,
                destinationBlock,
                handoffWorldPosition,
                out destinationLaneIndex, topologyOnly);
        }

        if (sourceLaneIndex == backLaneIndex)
        {
            destinationBlock = this;
            destinationLaneIndex = frontLaneIndex;
            return true;
        }

        return false;
    }

    private bool TryResolveBelt2FBridgeCenterSuccessor(
        int sourceLaneIndex,
        out Block destinationBlock,
        out int destinationLaneIndex,
        bool topologyOnly = false)
    {
        destinationBlock = null;
        destinationLaneIndex = -1;
        if (!TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F belt2F))
        {
            return false;
        }

        if (sourceLaneIndex == 3)
        {
            destinationBlock = this;
            destinationLaneIndex = 1;
            return topologyOnly ? IsBeltSplitLane(destinationLaneIndex) : IsValidConveyorLaneIndex(destinationLaneIndex);
        }

        if (sourceLaneIndex != 1
            || !belt2F.TryGetOutputDirection(belt2F.transform.rotation, out Vector2Int outputDirection)
            || outputDirection == Vector2Int.zero
            || !TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator)
            || terrainGenerator == null)
        {
            return false;
        }

        Vector2Int nextCoordinate = coordinate + outputDirection;
        if (!terrainGenerator.TryGetLoadedBlock(nextCoordinate, out destinationBlock)
            || destinationBlock == null
            || destinationBlock == this
            || !belt2F.CoversCoordinate(destinationBlock.Coordinate)
            || !destinationBlock.IsConveyorStackingEnabled())
        {
            destinationBlock = null;
            return false;
        }

        Vector3 handoffWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
        return TryGetConveyorHandoffReceiveLaneIndex(
            this,
            sourceLaneIndex,
            destinationBlock,
            handoffWorldPosition,
            out destinationLaneIndex, topologyOnly);
    }

    private static bool TryGetConveyorHandoffReceiveLaneIndex(
        Block sourceBlock,
        int sourceLaneIndex,
        Block destinationBlock,
        Vector3 handoffWorldPosition,
        out int destinationLaneIndex,
        bool topologyOnly = false)
    {
        destinationLaneIndex = -1;
        return destinationBlock != null
            && destinationBlock.TryGetConveyorReceiveLaneIndexForHandoffPosition(
                sourceBlock,
                sourceLaneIndex,
                handoffWorldPosition,
                out destinationLaneIndex, topologyOnly);
    }

    private bool TryGetConveyorReceiveLaneIndexForHandoffPosition(
        Block sourceBlock,
        int sourceLaneIndex,
        Vector3 handoffWorldPosition,
        out int laneIndex,
        bool topologyOnly = false)
    {
        laneIndex = -1;
        if (TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F bridgeBelt2F)
            && sourceBlock != null
            && sourceBlock.TryGetConveyorItemBelt2F(sourceLaneIndex, out ConvayorBelt2F sourceBelt2F)
            && ReferenceEquals(sourceBelt2F, bridgeBelt2F))
        {
            const int bridgeBackLaneIndex = 3;
            laneIndex = bridgeBackLaneIndex;
            return topologyOnly ? IsBeltSplitLane(bridgeBackLaneIndex) : IsValidConveyorLaneIndex(bridgeBackLaneIndex);
        }

        if (IsCornerConveyor())
        {
            if (!topologyOnly) return TryGetPreferredCornerConveyorReceiveLaneIndex(handoffWorldPosition, out laneIndex);
            laneIndex = ConveyorSingleLineBackLaneIndex;
            return IsBeltSplitLane(laneIndex);
        }

        int frontLaneIndex = ConveyorSingleLineFrontLaneIndex;
        int backLaneIndex = ConveyorSingleLineBackLaneIndex;
        if (!topologyOnly && !TryGetConveyorLaneLayout(out frontLaneIndex, out backLaneIndex))
        {
            return false;
        }

        // A side input joins at the center, then follows the receiving belt.
        // Reserving the back slot would pull it against the flow before departure.
        laneIndex = TryGetConveyorSideHandoffFlow(sourceBlock, sourceLaneIndex, out _)
            ? frontLaneIndex
            : backLaneIndex;
        return topologyOnly ? IsBeltSplitLane(laneIndex) : IsValidConveyorLaneIndex(laneIndex);
    }
}
