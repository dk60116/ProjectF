using System.Collections.Generic;
using UnityEngine;

public partial class Block
{
    internal int BeltSplitLaneLimit => ConveyorStackLaneLimit;

    internal bool IsBeltSplitLane(int lane)
    {
        if (lane < 0 || lane >= ConveyorStackLaneLimit || !IsConveyorStackingEnabled()) return false;
        return IsActiveConveyorLaneIndex(lane)
            || (IsBelt2FBridgeLaneIndex(lane) && TryGetBelt2FBridgeCenterBelt(out _))
            || (lane == ConveyorSideExitLaneIndex && HasBeltSplitSideExitLane());
    }

    // A lingering item in a removed side exit must not change static connectivity.
    private bool HasBeltSplitSideExitLane()
    {
        return CanUseConveyorSideExitLane()
            && TryGetNextConveyorBlock(out Block destination)
            && destination != null
            && destination.TryGetConveyorSideHandoffFlow(this, ConveyorSingleLineFrontLaneIndex, out _);
    }

    internal void AppendBeltSplitConnections(int lane, List<(Block block, int lane)> results)
    {
        if (!IsBeltSplitLane(lane)) return;
        if (lane == ConveyorSingleLineBackLaneIndex && TryGetRuntimeSplitter(out Spliterbelt splitter))
        {
            // Both outputs can receive an item, regardless of current fullness/filter/arbitration.
            if (TryGetSplitterChannels(splitter, out Block left, out Block right))
            {
                results.Add((left, ConveyorSingleLineFrontLaneIndex));
                results.Add((right, ConveyorSingleLineFrontLaneIndex));
            }
            return;
        }
        if (TryResolveConveyorSuccessorUncached(lane, out Block destination, out int targetLane, out _, true))
            results.Add((destination, targetLane));
    }

    internal void AppendBeltSplitVisualSegments(int lane, List<Vector3> endpoints)
    {
        Vector3 from = GetConveyorLaneWorldPosition(lane);
        Vector3 to = from + Vector3.forward * 0.12f;
        if (TryResolveConveyorSuccessorUncached(lane, out Block destination, out int targetLane, out _, true))
        {
            Vector3 target = destination.GetConveyorLaneWorldPosition(targetLane);
            to = destination == this ? target : Vector3.MoveTowards(from, target, 0.25f);
        }
        endpoints.Add(from);
        endpoints.Add(to);
    }
}
