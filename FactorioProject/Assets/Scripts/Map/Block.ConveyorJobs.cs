using ProjectF.Conveyors;
using UnityEngine;

public partial class Block
{
    private TerrainGenerator beltJobOwner;
    private int beltJobLane0 = -1, beltJobLane1 = -1, beltJobLane2 = -1, beltJobLane3 = -1;
    internal bool UsesBeltJobs => Application.isPlaying && TerrainGenerator.Active != null;

    internal int BeltJobIndex(int lane) => lane == 0 ? beltJobLane0 : lane == 1 ? beltJobLane1
        : lane == 2 ? beltJobLane2 : lane == 3 ? beltJobLane3 : -1;

    internal void BindBeltJobLane(TerrainGenerator owner, int lane, int index)
    {
        beltJobOwner = owner;
        if (lane == 0) beltJobLane0 = index;
        else if (lane == 1) beltJobLane1 = index;
        else if (lane == 2) beltJobLane2 = index;
        else if (lane == 3) beltJobLane3 = index;
    }

    internal void UnbindBeltJobs()
    {
        beltJobOwner = null;
        beltJobLane0 = beltJobLane1 = beltJobLane2 = beltJobLane3 = -1;
    }

    private void QueueBeltJobWrite(
        int lane,
        bool replaceItem = false,
        float holdSeconds = 0f,
        bool updatePickupGate = false)
    {
        if (!UsesBeltJobs) return;
        TerrainGenerator.Active.QueueBeltJobWrite(
            this,
            lane,
            replaceItem,
            BeltSimulationMath.Seconds(holdSeconds),
            updatePickupGate);
    }

    internal void PrepareBeltJobStorage()
    {
        ReleaseConveyorTransport();
        EnsureFloorObjectsInitialized();
    }

    internal ConveyorRuntimeRecord GetBeltJobSplitter(int lane)
        => lane == ConveyorSingleLineBackLaneIndex
           && TryGetRuntimeSplitterRecord(out ConveyorRuntimeRecord splitter)
            ? splitter
            : null;

    internal bool HasBeltJobStoredItem(int lane) => IsValidConveyorLaneIndex(lane) && HasConveyorStoredItemAtLane(lane);

    internal void AppendBeltJobConnections(int lane, System.Collections.Generic.List<(Block block, int lane)> results)
    {
        if (IsBeltSplitLane(lane)) { AppendBeltSplitConnections(lane, results); return; }
        // A side approach slot can retain an item after its receiving belt is removed/rotated.
        // Keep that reservation and let it drain via the same geometry as the existing lane API.
        if (TryResolveConveyorSuccessorUncached(lane, out Block destination, out int target, out _))
            results.Add((destination, target));
    }

    internal long GetBeltJobDuration(int lane, Block destination, int targetLane)
    {
        bool corner = destination == this && IsCornerConveyor() && lane == ConveyorSingleLineBackLaneIndex;
        float length = GetConveyorPathSegmentLength(lane, destination, targetLane, corner);
        // Existing handoff timing uses the receiving belt's speed, including mixed tiers.
        return BeltSimulationMath.Duration(length, destination != null ? destination.GetConveyorSpeed() : GetConveyorSpeed());
    }

    internal BeltLaneState CaptureBeltJobInput(
        int lane,
        BeltLaneState previous,
        bool replace,
        long hold,
        bool updatePickupGate)
    {
        BeltLaneState state = previous;
        if (replace)
        {
            int id = IsConveyorStorageLaneIndex(lane) ? conveyorItemIds[lane] : -1;
            if (id < 0) return BeltLaneState.Empty;
            state = new BeltLaneState { ItemId = id, Origin = -1 };
            Vector3 start = GetConveyorLaneWorldPosition(lane);
            if (lane < conveyorItemMotionStates.Count && conveyorItemMotionStates[lane].active)
            {
                ConveyorDataMotionState motion = EnsureConveyorDataMotionTiming(conveyorItemMotionStates[lane]);
                start = motion.startWorldPosition;
                // The request carries a duration; frame time does not advance the native simulation.
                state.Remaining = state.Duration = BeltSimulationMath.Seconds(motion.duration * (1f - motion.progress));
                if (motion.hasViaWorldPosition) state.GateBits |= 64;
            }
            PortableObject portable = GetConveyorPortableObjectAtLane(lane);
            if (portable != null) start = portable.WorldPosition;
            state.StartX = start.x; state.StartY = start.y; state.StartZ = start.z;
        }
        if (state.ItemId < 0) return state;
        if (hold > state.Remaining) state.Remaining = state.Duration = hold;
        if (replace || updatePickupGate)
        {
            ConveyorPickupGateState gate = lane < conveyorItemPickupGateStates.Count
                ? conveyorItemPickupGateStates[lane]
                : ConveyorPickupGateState.Settled();
            state.GateBits = (state.GateBits & 64) | (gate.hasGate ? 1 : 0)
                | (gate.requiresExit ? 2 : 0) | (gate.hasExited ? 4 : 0)
                | (gate.isSettled ? 8 : 0) | (gate.hasOrigin ? 16 : 0)
                | (gate.autoPickupBlocked ? 32 : 0);
            state.DropX = gate.dropOrigin.x;
            state.DropY = gate.dropOrigin.y;
            state.DropZ = gate.dropOrigin.z;
            state.ExitRadius = gate.exitRadius;
        }
        return state;
    }

    // Managed lane lists are only a short-lived command staging area after native ownership.
    internal void ReleaseBeltJobLegacyLaneView(int lane)
    {
        if (!IsConveyorStorageLaneIndex(lane)) return;
        PortableObject portable = GetConveyorPortableObjectAtLane(lane);
        if (portable != null)
        {
            conveyorCornerMotionStates.Remove(portable);
            conveyorLinearMotionStates.Remove(portable);
            conveyorStack[lane] = null;
            ReleaseFloorObject(portable);
        }
        conveyorItemIds[lane] = -1;
        conveyorItemMotionStates[lane] = default;
        conveyorItemMoveFrames[lane] = -1;
        conveyorItemMovementHoldUntilTimes[lane] = 0f;
        conveyorItemPickupGateStates[lane] = default;
    }

    internal void RecordBeltJobLaneChange(int lane, bool occupancyMayHaveChanged)
    {
        if (!occupancyMayHaveChanged || lane < 0 || lane >= ConveyorStackLaneLimit) return;
        int[] versions = EnsureConveyorRuntimeArrays().LaneOccupancyVersions;
        unchecked
        {
            versions[lane]++;
            if (versions[lane] == 0) versions[lane] = 1;
        }
    }

    internal void NotifyBeltJobPublished(
        bool wakeRuntimeDependents = true,
        bool refreshActivity = true)
    {
        // Several lanes in the same block can change during one native tick.
        // Invalidate presentation and observers once without copying lane data.
        MarkBeltJobItemVisualDirty(refreshActivity);
        NotifyRuntimeItemStackChanged(wakeRuntimeDependents, false);
    }

    internal void CaptureBeltJobItemVisualState(out int itemCount, out bool hasDynamicVisuals)
    {
        itemCount = 0;
        hasDynamicVisuals = false;
        if (!Application.isPlaying
            || !IsConveyorStackingEnabled()
            || !ShouldUseVirtualConveyorItemRendering())
        {
            return;
        }

        for (int lane = 0; lane < ConveyorStackLaneLimit; lane++)
        {
            if (!TryReadBeltJobLane(lane, out BeltLaneState state) || state.ItemId < 0) continue;
            itemCount++;
            hasDynamicVisuals |= state.Remaining > 0;
        }

        if (!hasDynamicVisuals)
            hasDynamicVisuals = HasNonBeltCpuRenderedConveyorMotionStates();
    }

    private bool TryReadBeltJobLane(int lane, out BeltLaneState state)
    {
        state = default;
        return beltJobOwner != null && beltJobOwner.TryReadBeltJobLane(this, lane, out state);
    }

    private bool HasBeltJobMotion()
    {
        for (int lane = 0; lane < ConveyorStackLaneLimit; lane++)
            if (TryReadBeltJobLane(lane, out BeltLaneState state) && state.ItemId >= 0 && state.Remaining > 0) return true;
        return false;
    }

    private bool TryGetBeltJobVisualPosition(int lane, out Vector3 position)
    {
        position = default;
        return beltJobOwner != null && beltJobOwner.TryGetBeltJobVisualPosition(this, lane, out position);
    }

    internal int GetBeltJobLaneOccupancyVersion(int lane)
    {
        ConveyorRuntimeArrays runtimeArrays = conveyorRuntimeArrays;
        return runtimeArrays != null
            && lane >= 0
            && lane < runtimeArrays.LaneOccupancyVersions.Length
                ? runtimeArrays.LaneOccupancyVersions[lane]
                : 0;
    }

    internal Vector3 EvaluateBeltJobSegment(int sourceLane, Block destination, int targetLane, float progress)
    {
        Vector3 from = GetConveyorLaneWorldPosition(sourceLane);
        if (destination == this && IsCornerConveyor() && sourceLane == ConveyorSingleLineBackLaneIndex)
            return EvaluateConveyorCornerPathWorldPosition(sourceLane, targetLane, progress);
        Vector3 to = destination.GetConveyorLaneWorldPosition(targetLane);
        if (TryGetConveyorLinearMoveViaWorldPosition(sourceLane, destination, targetLane, from, out Vector3 via))
        {
            float first = Vector3.Distance(from, via), second = Vector3.Distance(via, to);
            float distance = (first + second) * progress;
            return distance <= first ? Vector3.Lerp(from, via, first > 0 ? distance / first : 1f)
                : Vector3.Lerp(via, to, second > 0 ? (distance - first) / second : 1f);
        }
        return Vector3.Lerp(from, to, progress);
    }
}
