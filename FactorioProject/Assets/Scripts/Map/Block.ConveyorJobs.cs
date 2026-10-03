using ProjectF.Conveyors;
using ProjectF.Rendering;
using System.Collections.Generic;
using UnityEngine;

public partial class Block
{
    private TerrainGenerator beltJobOwner;
    private int beltJobLane0 = -1, beltJobLane1 = -1, beltJobLane2 = -1, beltJobLane3 = -1;
    private int beltJobOccupancyVersion0, beltJobOccupancyVersion1, beltJobOccupancyVersion2, beltJobOccupancyVersion3;
    internal int BeltJobPublicationIndex { get; set; } = -1;
    internal bool UsesBeltJobs => Application.isPlaying && TerrainGenerator.Active != null;
    internal bool HasBoundBeltJobLanes => beltJobOwner != null;

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
        BeltJobPublicationIndex = -1;
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
        // Native lanes do not need the legacy runtime arrays just to version occupancy.
        ref int version = ref (lane == 0 ? ref beltJobOccupancyVersion0 : ref (lane == 1
            ? ref beltJobOccupancyVersion1 : ref (lane == 2 ? ref beltJobOccupancyVersion2 : ref beltJobOccupancyVersion3)));
        unchecked
        {
            version++;
            if (version == 0) version = 1;
        }
    }

    internal static void InvalidateBeltJobPublicationCaches() => InvalidateConveyorCanMoveCaches();

    internal void NotifyBeltJobVisualPublished(
        int itemCount,
        bool hasDynamicVisuals,
        bool refreshActivity)
    {
        // Several lanes in the same block can change during one native tick.
        // Invalidate presentation once when the changed chunk is visible.
        IncrementConveyorItemVisualVersion();
        TerrainGenerator.Active?.MarkBeltJobItemVisualDirty(this, refreshActivity, itemCount, hasDynamicVisuals);
    }

    internal bool NotifyBeltJobRuntimePublished()
    {
        // Most native belts have no floor items. Do not initialize/normalize their
        // managed slot storage on every handoff. Real floor ingress stays immediate.
        if (HasBeltJobFloorIngress() && TryTransferOneDroppedFloorObjectToConveyor()) return true;
        if (RuntimeItemStackChanged == null) return false;
        RuntimeItemStackChanged.Invoke(this);
        return true;
    }

    private bool HasBeltJobFloorIngress()
    {
        if (deferredFloorOutput.Count > 0) return true;
        for (int i = 0; i < floorStacks.Count; i++)
            if (floorStacks[i] != null && floorStacks[i].Count > 0) return true;
        return false;
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
        return lane == 0 ? beltJobOccupancyVersion0 : lane == 1 ? beltJobOccupancyVersion1
            : lane == 2 ? beltJobOccupancyVersion2 : lane == 3 ? beltJobOccupancyVersion3 : 0;
    }

    internal Vector3 EvaluateBeltJobSegment(int sourceLane, Block destination, int targetLane, float progress)
    {
        return CaptureBeltJobVisualPath(sourceLane, destination, targetLane).Evaluate(progress);
    }

    internal BeltItemVisualPath CaptureBeltJobVisualPath(int sourceLane, Block destination, int targetLane)
    {
        if (destination == this && IsCornerConveyor() && sourceLane == ConveyorSingleLineBackLaneIndex)
        {
            if (TryGetConveyorCornerArcParameters(sourceLane, targetLane,
                    out Vector2 center, out float start, out float delta, out float radius))
                return BeltItemVisualPath.Arc(
                    BlockLocalToWorld(new Vector3(center.x, GetConveyorLaneHeight(), center.y)), start, delta, radius);
            Vector3 fallback = GetDefaultConveyorLaneWorldPosition(targetLane);
            return BeltItemVisualPath.Line(fallback, fallback);
        }
        Vector3 from = GetConveyorLaneWorldPosition(sourceLane);
        Vector3 to = destination.GetConveyorLaneWorldPosition(targetLane);
        return TryGetConveyorLinearMoveViaWorldPosition(sourceLane, destination, targetLane, from, out Vector3 via)
            ? BeltItemVisualPath.Through(from, via, to)
            : BeltItemVisualPath.Line(from, to);
    }

    internal void CaptureBeltJobVisualSurface(int lane, ref BeltItemVisualPathCache cache)
    {
        cache.RotateOnSurface = HasRuntimeBelt2FConveyor();
        cache.RequiresSurface = cache.RotateOnSurface
            || TryGetConveyorItemBelt2FRecord(lane, out _)
            || TryGetConveyorItemBelt2F(lane, out _);
    }

    internal void AppendDynamicVirtualConveyorItemRenderData(
        List<VirtualConveyorItemRenderData> results, BeltItemVisualPathCache[] pathCaches)
    {
        if (beltJobOwner == null)
        {
            AppendDynamicVirtualConveyorItemRenderData(results);
            return;
        }
        // Native ownership does not require materializing/normalizing legacy floor slots.
        GameManager manager = GameManager.Instance;
        bool showSleep = manager != null && manager.ShowSleepAwake;
        bool showLine = manager != null && manager.ShowBeltItemLine;
        for (int lane = 0; lane < ConveyorStackLaneLimit; lane++)
        {
            if (!beltJobOwner.TryReadBeltJobLaneForRendering(this, lane, out BeltLaneState state) || state.ItemId < 0)
                continue;
            PortableObject portable = GetConveyorPortableObjectAtLane(lane);
            if (portable != null)
            {
                if (portable.IsMovingToTarget) continue;
                ApplyConveyorObjectVirtualRenderingSuppressionIfNeeded(portable);
                if (portable.HasActiveOutline) continue;
            }
            float progress = beltJobOwner.GetBeltJobVisualProgress(this, lane, state);
            BeltItemVisualPath path = beltJobOwner.GetBeltJobVisualPath(this, lane, state, ref pathCaches[lane]);
            Vector3 position = path.End;
            Quaternion rotation = Quaternion.identity;
            if (pathCaches[lane].RequiresSurface)
            {
                position = path.Evaluate(progress);
                if (state.Origin >= 0 || state.Remaining <= 0)
                    position = ConformConveyorItemToBelt2FPath(lane, position);
                if (pathCaches[lane].RotateOnSurface) rotation = GetConveyorItemVisualWorldRotation(lane, position);
                path = default;
            }
            bool useLineColor = TryGetBeltItemLineDebugColorFast(
                beltJobOwner, showLine, lane, out Color32 lineColor);
            results.Add(new VirtualConveyorItemRenderData(
                state.ItemId, position, rotation, RuntimeLayer,
                showSleep && IsConveyorItemSleepAwakeSleeping(lane), useLineColor, lineColor,
                default, path, progress));
        }
    }
}
