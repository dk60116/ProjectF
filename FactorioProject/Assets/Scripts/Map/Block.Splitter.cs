using UnityEngine;

public partial class Block
{
    private bool TryGetRuntimeSplitter(out Spliterbelt splitter)
    {
        splitter = mapObject as Spliterbelt;
        if (splitter != null && splitter.IsRuntimeRootAvailable)
            return true;
        return Spliterbelt.TryFindCoveringBelt(coordinate, out splitter);
    }

    private bool TryGetSplitterSuccessor(Spliterbelt splitter, out Block destination, out int laneIndex)
    {
        destination = null;
        laneIndex = -1;
        if (!TryGetSplitterChannels(splitter, out Block left, out Block right))
            return false;
        bool leftReady = left.IsSplitterInputReady();
        bool rightReady = right.IsSplitterInputReady();
        int available = GetSplitterAvailableOutputs(left, right);
        int leftOutputs = leftReady ? available & splitter.GetAllowedOutputMask(left.GetConveyorItemIdAtLane(2)) : 0;
        int rightOutputs = rightReady ? available & splitter.GetAllowedOutputMask(right.GetConveyorItemIdAtLane(2)) : 0;
        if (!splitter.TrySelectOutput(splitter.GetChannel(coordinate), leftReady, rightReady,
                leftOutputs, rightOutputs, out int output))
            return false;
        destination = output == 0 ? left : right;
        laneIndex = 0;
        return true;
    }

    private static int GetSplitterAvailableOutputs(Block left, Block right)
        => (IsConveyorDestinationLaneOccupied(left, 0) ? 0 : 1)
            | (IsConveyorDestinationLaneOccupied(right, 0) ? 0 : 2);

    private bool IsSplitterInputReady()
    {
        return HasConveyorItemAtLane(2) && !WasConveyorItemMovedThisFrame(2)
            && IsConveyorItemReadyToMoveAtLane(2);
    }

    private bool TryGetSplitterChannels(Spliterbelt splitter, out Block left, out Block right)
    {
        left = right = null;
        return TryResolveOwningTerrainGenerator(out TerrainGenerator terrain)
            && splitter.TryGetChannelCoordinate(0, out Vector2Int leftCoordinate)
            && splitter.TryGetChannelCoordinate(1, out Vector2Int rightCoordinate)
            && terrain.TryGetLoadedBlock(leftCoordinate, out left) && left != null
            && terrain.TryGetLoadedBlock(rightCoordinate, out right) && right != null;
    }

    private void CommitSplitterTransfer(int sourceLane, Block destination, int destinationLane)
    {
        if (sourceLane != 2 || destinationLane != 0 || destination == null
            || !TryGetRuntimeSplitter(out Spliterbelt splitter)
            || !splitter.TryGetChannel(coordinate, out int input)
            || !splitter.TryGetChannel(destination.coordinate, out int output))
            return;
        splitter.CommitTransfer(input, output);
    }

    public void WakeSplitterInputs()
    {
        if (!TryGetRuntimeSplitter(out Spliterbelt splitter)
            || !TryGetSplitterChannels(splitter, out Block left, out Block right))
            return;
        left.ClearConveyorPlanFailureCache(2);
        right.ClearConveyorPlanFailureCache(2);
        left.WakeConveyorMoveAttempts(true);
        right.WakeConveyorMoveAttempts(true);
        left.RefreshConveyorActivityRegistration();
        right.RefreshConveyorActivityRegistration();
    }
}
