using ProjectF.Conveyors;
using UnityEngine;

public partial class Block
{
    internal ConveyorTransportRun ConveyorTransport { get; private set; }
    private int transportBlockIndex, transportFrontLane, transportBackLane;
    private bool conveyorTransportInteractionBoundary;
    private ConveyorTransportRun transportInputRun, transportOutputRun;
    internal bool OwnsConveyorTransport => ConveyorTransport != null && ConveyorTransport.Active;
    internal bool CanOwnConveyorTransport => !conveyorTransportInteractionBoundary
        && ShouldUseVirtualConveyorItemRendering() && !HasRuntimeBelt2FConveyor()
        && CanUseStraightConveyorLineSimulationStructureOnly()
        && !HasStraightConveyorLineFastPathRuntimeBlocker()
        && !HasHeldTransportLane();

    private bool HasHeldTransportLane()
    {
        for (int lane = 0; lane < conveyorItemMovementHoldUntilTimes.Count; lane++)
        {
            if (IsConveyorLaneMovementHeld(lane)) return true;
            ConveyorDataMotionState motion = conveyorItemMotionStates[lane];
            if (motion.active && (motion.useCornerMotion || motion.hasViaWorldPosition || motion.cornerContinuation.active)) return true;
            ConveyorPickupGateState gate = conveyorItemPickupGateStates[lane];
            if (gate.hasGate && !gate.isSettled) return true;
        }
        return false;
    }

    private void OnDisable() { ReleaseConveyorTransport(); }
    internal void ReleaseConveyorTransport(bool interactionBoundary = false)
    {
        if (interactionBoundary && OwnsConveyorTransport) conveyorTransportInteractionBoundary = true;
        ConveyorTransport?.Release();
        if (!interactionBoundary)
        {
            transportInputRun?.Release();
            transportOutputRun?.Release();
        }
    }

    internal void BindTransportPort(ConveyorTransportRun run, bool input)
    {
        if (input) transportInputRun = run;
        else transportOutputRun = run;
    }

    internal void UnbindTransportPort(ConveyorTransportRun run)
    {
        if (transportInputRun == run) transportInputRun = null;
        if (transportOutputRun == run) transportOutputRun = null;
    }

    private void NotifyTransportPortChanged(int lane = -1)
    {
        // Robot arms register only around their interaction cells. The dictionary
        // lookup is empty for ordinary belt cells, while an observed slot change
        // can wake a sleeping pickup or blocked-output arm without periodic scans.
        RobotArm.WakeAroundCoordinate(coordinate);
        if (transportInputRun != null && (lane < 0 || lane == transportInputRun.InletLane)) transportInputRun.NotifyInputChanged();
        if (transportOutputRun != null && (lane < 0 || lane == transportOutputRun.OutletLane)) transportOutputRun.NotifyOutputChanged();
    }

    internal void EnsureConveyorTransportInteractionBoundary()
    {
        if (!IsConveyorStackingEnabled() || conveyorTransportInteractionBoundary)
        {
            return;
        }

        conveyorTransportInteractionBoundary = true;
        ReleaseConveyorTransport();
    }

    internal double GetTransportInputReadyTime(int lane)
    {
        if (!HasConveyorItemAtLane(lane)) return double.PositiveInfinity;
        // Materialized motion belongs to the legacy backend, including the
        // adjacent port. Export before allowing it to enter an owned run.
        if (GetConveyorPortableObjectAtLane(lane) != null)
        {
            transportInputRun?.Release();
            return double.PositiveInfinity;
        }
        double ready = Time.time;
        if (lane < conveyorItemMovementHoldUntilTimes.Count)
            ready = System.Math.Max(ready, conveyorItemMovementHoldUntilTimes[lane]);
        ConveyorDataMotionState motion = conveyorItemMotionStates[lane];
        if (motion.active)
            ready = System.Math.Max(ready, GetConveyorDataMotionCompletionTime(EnsureConveyorDataMotionTiming(motion), Time.time));
        // A newly inserted, already-settled item may still carry this frame's
        // transfer guard. Recheck on the next time step instead of sleeping
        // forever when the cached transfer rejects that transient condition.
        if (WasConveyorItemMovedThisFrame(lane)) ready = System.Math.Max(ready, (double)Time.time + 0.000001);
        return ready;
    }

    internal Vector3 TransportLanePosition(int lane) => GetConveyorLaneWorldPosition(lane);
    private int TransportSlot(int lane) => lane == transportBackLane ? transportBlockIndex * 2
        : lane == transportFrontLane ? transportBlockIndex * 2 + 1 : -1;
    private bool ReadTransportLane(int lane, out ConveyorTransportItem item, out double position)
    {
        item = default; position = 0;
        return OwnsConveyorTransport && ConveyorTransport.Read(TransportSlot(lane), out item, out position);
    }

    internal bool ReadTransportImport(int lane, out ConveyorTransportItem item, out Vector3 position)
    {
        item = new ConveyorTransportItem { Id = GetConveyorStoredItemIdAtLane(lane), Gate = GetConveyorPickupGateStateAtLane(lane) };
        position = GetConveyorItemVisualWorldPosition(lane);
        return item.Id >= 0;
    }

    internal void BindConveyorTransport(ConveyorTransportRun run, int blockIndex, int front, int back)
    {
        // No item identity or motion remains in the legacy storage while owned.
        ClearConveyorStorageLaneRaw(front);
        ClearConveyorStorageLaneRaw(back);
        ConveyorTransport = run; transportBlockIndex = blockIndex;
        transportFrontLane = front; transportBackLane = back;
        MarkConveyorItemVisualDirty();
        RefreshConveyorActivityRegistration(false, false);
    }

    internal void DetachConveyorTransport(ConveyorTransportRun run)
    {
        if (ConveyorTransport != run) return;
        // Carry the shared revision back once when ownership changes. A new
        // run starts at zero and must not reuse a cached occupancy version.
        ConveyorRuntimeArrays arrays = EnsureConveyorRuntimeArrays();
        unchecked
        {
            arrays.LaneOccupancyVersions[transportFrontLane] += run.Revision;
            arrays.LaneOccupancyVersions[transportBackLane] += run.Revision;
            conveyorItemVisualVersion += run.Revision;
        }
        ConveyorTransport = null;
    }

    internal void RestoreTransportItem(int lane, ConveyorTransportItem item, Vector3 position)
    {
        SetConveyorItemAtLane(lane, item.Id, null, item.Gate);
        float distance = Vector3.Distance(position, GetConveyorLaneWorldPosition(lane));
        if (distance > ConveyorContinuousMotionEpsilon)
            conveyorItemMotionStates[lane] = InitializeConveyorDataMotionTiming(new ConveyorDataMotionState {
                active = true, startWorldPosition = position, destinationLaneIndex = lane, pathLength = distance
            }, 0f);
        MarkConveyorItemVisualDirty();
    }

    private bool TryAcceptTransportTransfer(Block source, int sourceLane, int destinationLane)
    {
        if (!OwnsConveyorTransport) return false;
        var item = new ConveyorTransportItem { Id = source.GetConveyorItemIdAtLane(sourceLane), Gate = source.GetConveyorPickupGateStateAtLane(sourceLane) };
        if (item.Id < 0 || !ConveyorTransport.Accept(TransportSlot(destinationLane), item)) return false;
        source.ClearConveyorItemAtLane(sourceLane);
        source.MarkConveyorItemVisualDirty();
        source.RefreshConveyorActivityRegistration(true, false);
        return true;
    }

    private bool TryMoveIntoConveyorTransport(int lane, out bool moved, out Block destination, out int destinationLane)
    {
        moved = false; destination = null; destinationLane = -1;
        if (!TryGetRuntimeNextConveyorBlock(out Block next) || next == null || !next.OwnsConveyorTransport
            || next.ConveyorTransport.Inlet != this || lane != next.ConveyorTransport.InletLane) return false;
        destination = next; destinationLane = next.transportBackLane;
        moved = next.ConveyorTransport.TryScheduledInput();
        return true;
    }

    private ConveyorDataMotionState GetTransportSaveMotion(int lane)
    {
        if (!ReadTransportLane(lane, out _, out double position)) return default;
        Vector3 world = ConveyorTransport.WorldPosition(position);
        float distance = Vector3.Distance(world, GetConveyorLaneWorldPosition(lane));
        return distance <= ConveyorContinuousMotionEpsilon ? default : InitializeConveyorDataMotionTiming(new ConveyorDataMotionState {
            active = true, startWorldPosition = world, destinationLaneIndex = lane, pathLength = distance
        }, 0f);
    }
}
