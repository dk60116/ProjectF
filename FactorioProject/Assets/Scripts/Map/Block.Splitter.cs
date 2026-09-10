using System.Collections.Generic;
using UnityEngine;

public partial class Block
{
    private bool TryGetRuntimeSplitterRecord(out ConveyorRuntimeRecord record)
    {
        return TryGetRuntimeConveyorRecord(out record) && record.IsSplitter;
    }

    private bool TryGetRuntimeSplitter(out Spliterbelt splitter)
    {
        if (TryGetRuntimeSplitterRecord(out _))
        {
            splitter = null;
            return false;
        }

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

    private bool TryGetSplitterSuccessor(
        ConveyorRuntimeRecord splitter,
        out Block destination,
        out int laneIndex)
    {
        destination = null;
        laneIndex = -1;
        if (!TryGetSplitterChannels(splitter, out Block left, out Block right))
        {
            return false;
        }

        bool leftReady = left.IsSplitterInputReady();
        bool rightReady = right.IsSplitterInputReady();
        int available = GetSplitterAvailableOutputs(left, right);
        int leftOutputs = leftReady
            ? available & splitter.GetSplitterAllowedOutputMask(left.GetConveyorItemIdAtLane(2))
            : 0;
        int rightOutputs = rightReady
            ? available & splitter.GetSplitterAllowedOutputMask(right.GetConveyorItemIdAtLane(2))
            : 0;
        if (!splitter.TryGetSplitterChannel(coordinate, out int input)
            || !splitter.TrySelectSplitterOutput(
                input,
                leftReady,
                rightReady,
                leftOutputs,
                rightOutputs,
                out int output))
        {
            return false;
        }

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

    private bool TryGetSplitterChannels(
        ConveyorRuntimeRecord splitter,
        out Block left,
        out Block right)
    {
        left = right = null;
        if (splitter == null
            || !TryResolveOwningTerrainGenerator(out TerrainGenerator terrain))
        {
            return false;
        }

        Vector2Int leftCoordinate = default;
        Vector2Int rightCoordinate = default;
        IReadOnlyList<Vector2Int> coordinates = splitter.OccupiedCoordinates;
        for (int i = 0; i < coordinates.Count; i++)
        {
            if (!splitter.TryGetSplitterChannel(coordinates[i], out int channel))
            {
                continue;
            }

            if (channel == 0)
            {
                leftCoordinate = coordinates[i];
            }
            else
            {
                rightCoordinate = coordinates[i];
            }
        }

        return terrain.TryGetLoadedBlock(leftCoordinate, out left)
               && left != null
               && terrain.TryGetLoadedBlock(rightCoordinate, out right)
               && right != null;
    }

    private void CommitSplitterTransfer(int sourceLane, Block destination, int destinationLane)
    {
        if (sourceLane == 2
            && destinationLane == 0
            && destination != null
            && TryGetRuntimeSplitterRecord(out ConveyorRuntimeRecord record)
            && record.TryGetSplitterChannel(coordinate, out int recordInput)
            && record.TryGetSplitterChannel(destination.coordinate, out int recordOutput))
        {
            record.CommitSplitterTransfer(recordInput, recordOutput);
            return;
        }

        if (sourceLane != 2 || destinationLane != 0 || destination == null
            || !TryGetRuntimeSplitter(out Spliterbelt splitter)
            || !splitter.TryGetChannel(coordinate, out int input)
            || !splitter.TryGetChannel(destination.coordinate, out int output))
            return;
        splitter.CommitTransfer(input, output);
    }

    public void WakeSplitterInputs()
    {
        if (TryGetRuntimeSplitterRecord(out ConveyorRuntimeRecord record)
            && TryGetSplitterChannels(record, out Block recordLeft, out Block recordRight))
        {
            recordLeft.ClearConveyorPlanFailureCache(2);
            recordRight.ClearConveyorPlanFailureCache(2);
            recordLeft.WakeConveyorMoveAttempts(true);
            recordRight.WakeConveyorMoveAttempts(true);
            recordLeft.RefreshConveyorActivityRegistration();
            recordRight.RefreshConveyorActivityRegistration();
            return;
        }

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
