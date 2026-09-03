using System.Collections.Generic;
using UnityEngine;

internal struct ConveyorCornerContinuation
{
    public bool active;
    public BlockHandle blockHandle;
    public Vector2Int blockCoordinate;
    public int sourceLaneIndex;
    public int destinationLaneIndex;
    public Vector3 startWorldPosition;
    public float startProgress;
    public float pathLength;
    public float durationPathLength;
}

internal struct ConveyorDataMotionState
{
    public bool active;
    public bool useCornerMotion;
    public ConveyorCornerContinuation cornerContinuation;
    public Vector3 startWorldPosition;
    public bool hasViaWorldPosition;
    public Vector3 viaWorldPosition;
    public int sourceLaneIndex;
    public int destinationLaneIndex;
    public float progress;
    public float pathLength;
    public float durationPathLength;
    public float startTime;
    public float duration;
}

internal struct ConveyorPickupGateState
{
    public bool hasGate;
    public bool requiresExit;
    public bool hasExited;
    public float exitRadius;
    public bool isSettled;
    public Vector3 dropOrigin;
    public bool hasOrigin;
    public bool autoPickupBlocked;

    public static ConveyorPickupGateState Settled()
    {
        return new ConveyorPickupGateState
        {
            isSettled = true
        };
    }

    public void MarkDropped(float radius, bool settled, Vector3 origin)
    {
        hasGate = true;
        requiresExit = true;
        hasExited = false;
        exitRadius = Mathf.Max(0f, radius);
        isSettled = settled;
        dropOrigin = origin;
        hasOrigin = true;
        autoPickupBlocked = false;
    }

    public void MarkSettled()
    {
        isSettled = true;
    }

    public void UpdateExitState(Vector3 playerPosition, Vector3 fallbackOrigin)
    {
        if (!requiresExit || hasExited)
        {
            return;
        }

        Vector3 origin = hasOrigin ? dropOrigin : fallbackOrigin;
        Vector3 offset = playerPosition - origin;
        offset.y = 0f;
        if (offset.sqrMagnitude > exitRadius * exitRadius)
        {
            hasExited = true;
        }
    }

    public bool CanPickup(float distanceSqr, float pickupRadiusSqr)
    {
        if (autoPickupBlocked)
        {
            return false;
        }

        if (!requiresExit)
        {
            return true;
        }

        return hasExited && distanceSqr <= pickupRadiusSqr;
    }

    public bool CanManualPickup(float distanceSqr, float pickupRadiusSqr)
    {
        return isSettled && distanceSqr <= pickupRadiusSqr;
    }
}

internal sealed class ConveyorRuntimeArrays
{
    private const int LaneCount = Block.ConveyorCellItemUnit;

    public readonly bool[] CanMoveCacheValid = new bool[LaneCount * 2];
    public readonly int[] CanMoveCacheFrames = new int[LaneCount * 2];
    public readonly int[] CanMoveCacheVersions = new int[LaneCount * 2];
    public readonly bool[] CanMoveCacheResults = new bool[LaneCount * 2];
    public readonly bool[] PlanFailureCacheValid = new bool[LaneCount * 2];
    public readonly float[] PlanFailureCacheUntilTimes = new float[LaneCount * 2];
    public readonly int[] PlanFailureCacheVersions = new int[LaneCount * 2];
    public readonly int[] PlanFailureSourceVersions = new int[LaneCount * 2];
    public readonly BlockHandle[] PlanFailureDestinationBlockHandles = new BlockHandle[LaneCount * 2];
    public readonly int[] PlanFailureDestinationLaneIndices = new int[LaneCount * 2];
    public readonly int[] PlanFailureDestinationVersions = new int[LaneCount * 2];
    public readonly bool[] PlanFailureHasDestination = new bool[LaneCount * 2];
    public readonly float[] NextLaneMoveAttemptTimes = new float[LaneCount];
    public readonly int[] LaneOccupancyVersions = new int[LaneCount];
    public readonly BlockHandle[] CachedSuccessorBlockHandles = new BlockHandle[LaneCount];
    public readonly int[] CachedSuccessorLaneIndices = new int[LaneCount];
    public readonly bool[] CachedSuccessorExists = new bool[LaneCount];
    public readonly bool[] CachedSuccessorUsesCornerMotion = new bool[LaneCount];
    public readonly bool[] LaneBlockedSleepStates = new bool[LaneCount];
    public readonly bool[] LaneCycleBlockedSleepStates = new bool[LaneCount];
    public readonly bool[] LaneSleepAwakeDarkTintStates = new bool[LaneCount];
    public readonly bool[] LaneBeltItemLineDebugStates = new bool[LaneCount];
    public readonly Color32[] LaneBeltItemLineDebugColors = new Color32[LaneCount];
}

internal sealed class BlockRuntimeSimulationState
{
    private sealed class ConveyorLaneStorage
    {
        internal readonly List<int> ItemIds = new List<int>();
        internal readonly List<int> MoveFrames = new List<int>();
        internal readonly List<ConveyorDataMotionState> MotionStates =
            new List<ConveyorDataMotionState>();
        internal readonly List<ConveyorPickupGateState> PickupGateStates =
            new List<ConveyorPickupGateState>();
        internal readonly List<float> MovementHoldUntilTimes = new List<float>();

        internal void Clear()
        {
            ItemIds.Clear();
            MoveFrames.Clear();
            MotionStates.Clear();
            PickupGateStates.Clear();
            MovementHoldUntilTimes.Clear();
        }
    }

    private ConveyorLaneStorage conveyorLaneStorage;

    internal bool HasConveyorLaneStorage => conveyorLaneStorage != null;

    internal bool HasConveyorItems
    {
        get
        {
            if (conveyorLaneStorage == null)
            {
                return false;
            }

            List<int> itemIds = conveyorLaneStorage.ItemIds;
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (itemIds[i] >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal List<int> ConveyorItemIds => EnsureConveyorLaneStorage().ItemIds;
    internal List<int> ConveyorItemMoveFrames => EnsureConveyorLaneStorage().MoveFrames;
    internal List<ConveyorDataMotionState> ConveyorItemMotionStates =>
        EnsureConveyorLaneStorage().MotionStates;
    internal List<ConveyorPickupGateState> ConveyorItemPickupGateStates =>
        EnsureConveyorLaneStorage().PickupGateStates;
    internal List<float> ConveyorItemMovementHoldUntilTimes =>
        EnsureConveyorLaneStorage().MovementHoldUntilTimes;

    internal void ClearConveyorLaneStorage()
    {
        conveyorLaneStorage?.Clear();
    }

    private ConveyorLaneStorage EnsureConveyorLaneStorage()
    {
        return conveyorLaneStorage ??= new ConveyorLaneStorage();
    }

    internal ConveyorRuntimeArrays conveyorRuntimeArrays;
    internal float nextConveyorMoveAttemptTime;
    internal BlockHandle cachedNextConveyorBlockHandle;
    internal bool cachedHasNextConveyorBlock;
    internal bool conveyorConnectionCacheDirty = true;
    internal bool conveyorSuccessorCacheDirty = true;
    internal bool conveyorLaneLayoutCacheDirty = true;
    internal bool cachedConveyorLaneLayoutValid;
    internal int cachedFrontLaneIndex = -1;
    internal int cachedBackLaneIndex = -1;
    internal int conveyorItemVisualVersion;
}

/// <summary>
/// Compatibility facade for cell interaction and rendering. Authoritative belt
/// lane identity, retry state and data-only motion are owned by BlockDataStore.
/// Keeping those values outside the Component prevents a rendered item view from
/// becoming the source of truth for whether a belt lane contains an item.
/// </summary>
public partial class Block
{
    private BlockRuntimeSimulationState runtimeSimulationState;

    private BlockRuntimeSimulationState SimulationState
    {
        get
        {
            if (runtimeSimulationState != null)
            {
                return runtimeSimulationState;
            }

            TerrainGenerator terrain = cachedTerrainGenerator;
            if (runtimeHandle.IsValid
                && (terrain != null || TryResolveOwningTerrainGenerator(out terrain))
                && terrain.TryGetOrCreateBlockRuntimeSimulationState(
                    runtimeHandle,
                    out BlockRuntimeSimulationState storedState))
            {
                runtimeSimulationState = storedState;
                return runtimeSimulationState;
            }

            // Standalone/editor-created Block components do not have a store
            // handle. Preserve their old behaviour without making them part of
            // the runtime world's authoritative state.
            runtimeSimulationState = new BlockRuntimeSimulationState();
            return runtimeSimulationState;
        }
    }

    // These forwarding properties deliberately preserve the existing conveyor
    // implementation while moving ownership out of the MonoBehaviour. They are
    // temporary compatibility boundaries for the later handle-only simulation.
    private List<int> conveyorItemIds => SimulationState.ConveyorItemIds;
    private List<int> conveyorItemMoveFrames => SimulationState.ConveyorItemMoveFrames;
    private List<ConveyorDataMotionState> conveyorItemMotionStates =>
        SimulationState.ConveyorItemMotionStates;
    private List<ConveyorPickupGateState> conveyorItemPickupGateStates =>
        SimulationState.ConveyorItemPickupGateStates;
    private List<float> conveyorItemMovementHoldUntilTimes =>
        SimulationState.ConveyorItemMovementHoldUntilTimes;

    private ConveyorRuntimeArrays conveyorRuntimeArrays
    {
        get => SimulationState.conveyorRuntimeArrays;
        set => SimulationState.conveyorRuntimeArrays = value;
    }

    private float nextConveyorMoveAttemptTime
    {
        get => SimulationState.nextConveyorMoveAttemptTime;
        set => SimulationState.nextConveyorMoveAttemptTime = value;
    }

    private Block cachedNextConveyorBlock
    {
        get
        {
            BlockHandle handle = SimulationState.cachedNextConveyorBlockHandle;
            return TryResolveRuntimeBlock(handle, out Block block)
                    ? block
                    : null;
        }
        set
        {
            BlockHandle handle = value != null ? value.RuntimeHandle : default;
            SimulationState.cachedNextConveyorBlockHandle = handle;
        }
    }

    private bool cachedHasNextConveyorBlock
    {
        get => SimulationState.cachedHasNextConveyorBlock;
        set => SimulationState.cachedHasNextConveyorBlock = value;
    }

    private bool conveyorConnectionCacheDirty
    {
        get => SimulationState.conveyorConnectionCacheDirty;
        set => SimulationState.conveyorConnectionCacheDirty = value;
    }

    private bool conveyorSuccessorCacheDirty
    {
        get => SimulationState.conveyorSuccessorCacheDirty;
        set => SimulationState.conveyorSuccessorCacheDirty = value;
    }

    private bool conveyorLaneLayoutCacheDirty
    {
        get => SimulationState.conveyorLaneLayoutCacheDirty;
        set => SimulationState.conveyorLaneLayoutCacheDirty = value;
    }

    private bool cachedConveyorLaneLayoutValid
    {
        get => SimulationState.cachedConveyorLaneLayoutValid;
        set => SimulationState.cachedConveyorLaneLayoutValid = value;
    }

    private int cachedFrontLaneIndex
    {
        get => SimulationState.cachedFrontLaneIndex;
        set => SimulationState.cachedFrontLaneIndex = value;
    }

    private int cachedBackLaneIndex
    {
        get => SimulationState.cachedBackLaneIndex;
        set => SimulationState.cachedBackLaneIndex = value;
    }

    private int conveyorItemVisualVersion
    {
        get => SimulationState.conveyorItemVisualVersion;
        set => SimulationState.conveyorItemVisualVersion = value;
    }

    internal bool HasRuntimeSimulationState => runtimeSimulationState != null;

    internal void DetachRuntimeSimulationState()
    {
        runtimeSimulationState = null;
    }

    private bool TryResolveRuntimeBlock(BlockHandle handle, out Block block)
    {
        block = null;
        if (!handle.IsValid)
        {
            return false;
        }

        if (runtimeHandle == handle)
        {
            block = this;
            return true;
        }

        TerrainGenerator terrain = cachedTerrainGenerator;
        if (terrain == null && !TryResolveOwningTerrainGenerator(out terrain))
        {
            return false;
        }

        return terrain.TryResolveLoadedBlock(handle, out block) && block != null;
    }

}
