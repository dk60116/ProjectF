using System.Collections.Generic;
using UnityEngine;

public class RailHandcar : Train
{
    private static readonly HashSet<RailHandcar> ActiveRuntimeHandcars = new HashSet<RailHandcar>();

    [SerializeField, Min(0.01f)]
    private float railMoveSpeedMultiplier = 1f;
    [SerializeField, Min(0.02f)]
    private float railMovementSubstepDistance = 0.12f;
    [SerializeField, Min(0.05f)]
    private float railSnapMaxDistance = 0.75f;
    [SerializeField, Min(0.01f)]
    private float railRotationInterpolationSpeed = 10f;
    [SerializeField, Min(0.05f)]
    private float railSearchRadius = 1.25f;
    [SerializeField, Min(0.01f)]
    private float railInputDeadZone = 0.12f;
    [SerializeField, Min(0.05f)]
    private float branchSwitchMaxDistance = 0.7f;
    [SerializeField, Min(0.05f)]
    private float branchSwitchLookAhead = 0.8f;
    [SerializeField, Range(0f, 1f)]
    private float branchSwitchMinInputDot = 0.35f;
    [SerializeField, Min(0.05f)]
    private float railConnectionMaxDistance = 0.08f;
    [SerializeField, Min(0.05f)]
    private float railConnectionLookAhead = 0.5f;
    [SerializeField, Min(0.001f)]
    private float internalConnectionMaxDistance = 0.015f;
    [SerializeField, Min(0.05f)]
    private float stationDockSearchRadius = 2f;
    [SerializeField, Min(0.05f)]
    private float stationDockCaptureDistance = 0.65f;
    [SerializeField, Min(0.01f)]
    private float stationDockSpeed = 0.7f;
    [SerializeField, Min(0.001f)]
    private float stationDockCompleteDistance = 0.01f;

    private const int RailNetworkAdvanceMaxHops = 16;
    private const float MinRailConnectionMaxDistance = 0.01f;
    private const float MinInternalConnectionMaxDistance = 0.001f;
    private const float MinRailMovementSubstepDistance = 0.02f;
    private const float RailDirectionReferenceDeadZone = 0.05f;
    private const float RailDebugBlockedProbeDistance = 0.12f;
    private const float RailDebugBlockedDistanceEpsilon = 0.01f;
    private const float BranchSwitchCurrentScoreTolerance = 0.05f;
    private const float BranchInternalOverlapMinTangentDot = 0.8f;
    private const float BranchInternalProgressMin = 0.03f;
    private const float BranchExtendedLookAheadMultiplier = 4f;
    private const float BranchRouteLockDistanceMultiplier = 2f;
    private const float BranchRouteLockMinDistance = 2f;
    private const float ConsistPathSampleDistanceEpsilon = 0.001f;
    private const float ConsistPathDirectionMinDot = 0.35f;
    private const int MaxPushPropagationDepth = 4;
    private const float PushConsistContactPadding = 0.08f;
    private const float PushConsistLateralPadding = 0.08f;
    private const float PushConsistGapTolerance = 0.04f;
    private const float PushConsistRetainedContactPadding = 0.12f;
    private const float PushConsistRetainedLateralPadding = 0.12f;
    private const float PushConsistBranchReleaseDistance = 0.28f;
    private const int PushConsistSessionRetainFrameCount = 18;
    private const float PushConsistPreferredContactScoreBias = 0.25f;
    private const float PushConsistKnownGroupScoreBias = 0.05f;
    private const int MaxRailMovementSubsteps = 32;
    private const float StationDockRailCoordinateSampleMaxDistance = 0.8f;
    private const float RailConnectionBridgePointTolerance = 0.002f;
    private const float RailConnectionBridgeMaxProgressPerFrame = 0.5f;

    private readonly List<InstallationObject> railSearchScratch = new List<InstallationObject>(16);
    private readonly List<InstallationObject> stationSearchScratch = new List<InstallationObject>(16);
    private readonly List<InstallationObject> stationRailSearchScratch = new List<InstallationObject>(8);
    private readonly List<Railload> railCandidateScratch = new List<Railload>(8);
    private readonly List<Trainstation> stationCandidateScratch = new List<Trainstation>(4);
    private readonly List<Train> activeTrainScratch = new List<Train>(16);
    private readonly List<Train> connectedTrainGroupScratch = new List<Train>(8);
    private readonly Queue<Train> connectedTrainGroupQueue = new Queue<Train>(8);
    private readonly HashSet<Train> connectedTrainGroupVisited = new HashSet<Train>();
    private readonly HashSet<Train> currentMovementLoadTrains = new HashSet<Train>();
    private readonly List<ConnectedTrainRailMove> connectedTrainRailMoveScratch = new List<ConnectedTrainRailMove>(8);
    private readonly List<ConnectedTrainRailMove> connectedTrainOrderScratch = new List<ConnectedTrainRailMove>(8);
    private readonly List<ConsistPathSample> consistPathTape = new List<ConsistPathSample>(64);
    private readonly List<ConsistPathSample> leaderPathFrameScratch = new List<ConsistPathSample>(8);
    private readonly List<ConsistPathSample> initialConsistSegmentScratch = new List<ConsistPathSample>(8);
    private readonly List<InitialConsistPathRouteNode> initialConsistPathRouteNodes = new List<InitialConsistPathRouteNode>(32);
    private readonly Queue<int> initialConsistPathRouteQueue = new Queue<int>(32);
    private readonly List<RailSample> railConnectionSampleScratch = new List<RailSample>(8);
    private readonly List<Train> consistPathTrainOrder = new List<Train>(8);
    private readonly List<float> consistPathFollowOffsets = new List<float>(8);
    private readonly List<float> actualConsistDistanceScratch = new List<float>(8);
    private readonly List<PushConsistPathSession> pushConsistPathSessions = new List<PushConsistPathSession>(4);

    private Vector2 currentFacingTangent;
    private Vector2 lastRailTravelDirection;
    private Railload lockedBranchRail;
    private float lockedBranchRailDistanceRemaining;
    private float currentMovementLoadSpeedMultiplier = 1f;
    private Train consistPathLeader;
    private Vector2 consistPathTravelDirection;
    private float consistPathEndDistance;
    public Train CurrentRailDebugPowerSourceTrain =>
        consistPathLeader != null && consistPathLeader.gameObject.activeInHierarchy
            ? consistPathLeader
            : this;

    public static void CollectActiveRuntimeHandcars(ICollection<RailHandcar> results)
    {
        if (results == null || ActiveRuntimeHandcars.Count <= 0)
        {
            return;
        }

        foreach (RailHandcar handcar in ActiveRuntimeHandcars)
        {
            if (handcar == null
                || !handcar.gameObject.activeInHierarchy
                || !handcar.TryGetPlacementRuntime(out _, out _))
            {
                continue;
            }

            results.Add(handcar);
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveRuntimeHandcars.Add(this);
    }

    protected override void OnDisable()
    {
        ActiveRuntimeHandcars.Remove(this);
        ResetRailPlacementState();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ActiveRuntimeHandcars.Remove(this);
        ResetRailPlacementState();
        base.PrepareForPool();
    }

    public void ResetRailPlacementState()
    {
        currentFacingTangent = Vector2.zero;
        lastRailTravelDirection = Vector2.zero;
        ClearLockedBranchRail();
        ClearCurrentMovementLoadTracking();
    }

    private void BeginCurrentMovementLoadTracking()
    {
        ClearCurrentMovementLoadTracking();
        if (!UsesTrainLoadSpeedReduction)
        {
            return;
        }

        RegisterConnectedTrainGroupForCurrentMovementLoad(this, this);
        RegisterRetainedPushConsistTrainsForCurrentMovementLoad();
    }

    private void ClearCurrentMovementLoadTracking()
    {
        currentMovementLoadTrains.Clear();
        currentMovementLoadSpeedMultiplier = 1f;
    }

    private float ResolveCurrentTrainLoadSpeedMultiplier()
    {
        if (!UsesTrainLoadSpeedReduction)
        {
            return 1f;
        }

        float multiplier = 1f;
        CollectConnectedTrainGroupForMovement(this);
        for (int i = 0; i < connectedTrainGroupScratch.Count; i++)
        {
            Train train = connectedTrainGroupScratch[i];
            if (train == null || train == this)
            {
                continue;
            }

            multiplier *= train.TrainLoadSpeedMultiplier;
        }

        if (pushConsistPathSessions.Count > 0)
        {
            PruneStalePushConsistPathSessions();
            for (int i = 0; i < pushConsistPathSessions.Count; i++)
            {
                PushConsistPathSession session = pushConsistPathSessions[i];
                if (session == null || session.BranchReleaseCompleted)
                {
                    continue;
                }

                for (int trainIndex = 0; trainIndex < session.TrainOrder.Count; trainIndex++)
                {
                    Train train = session.TrainOrder[trainIndex];
                    if (train == null
                        || train == this
                        || !connectedTrainGroupVisited.Add(train))
                    {
                        continue;
                    }

                    multiplier *= train.TrainLoadSpeedMultiplier;
                }
            }
        }

        return Mathf.Clamp(multiplier, 0.01f, 1f);
    }

    private float RegisterConnectedTrainGroupForCurrentMovementLoad(Train startTrain, Train excludedTrain)
    {
        if (!UsesTrainLoadSpeedReduction || startTrain == null)
        {
            return 1f;
        }

        CollectConnectedTrainGroupForMovement(startTrain);
        float groupMultiplier = 1f;
        for (int i = 0; i < connectedTrainGroupScratch.Count; i++)
        {
            Train train = connectedTrainGroupScratch[i];
            if (train == null || train == excludedTrain)
            {
                continue;
            }

            float trainMultiplier = train.TrainLoadSpeedMultiplier;
            groupMultiplier *= trainMultiplier;
            RegisterTrainForCurrentMovementLoad(train, trainMultiplier);
        }

        return Mathf.Clamp(groupMultiplier, 0.01f, 1f);
    }

    private void RegisterPreparedTrainMovesForCurrentMovementLoad()
    {
        if (!UsesTrainLoadSpeedReduction)
        {
            return;
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            RegisterTrainForCurrentMovementLoad(connectedTrainRailMoveScratch[i].Train);
        }
    }

    private void RegisterRetainedPushConsistTrainsForCurrentMovementLoad()
    {
        if (!UsesTrainLoadSpeedReduction || pushConsistPathSessions.Count <= 0)
        {
            return;
        }

        PruneStalePushConsistPathSessions();
        for (int i = 0; i < pushConsistPathSessions.Count; i++)
        {
            PushConsistPathSession session = pushConsistPathSessions[i];
            if (session == null || session.BranchReleaseCompleted)
            {
                continue;
            }

            for (int trainIndex = 0; trainIndex < session.TrainOrder.Count; trainIndex++)
            {
                RegisterTrainForCurrentMovementLoad(session.TrainOrder[trainIndex]);
            }
        }
    }

    private void RegisterTrainForCurrentMovementLoad(Train train)
    {
        RegisterTrainForCurrentMovementLoad(train, train != null ? train.TrainLoadSpeedMultiplier : 1f);
    }

    private void RegisterTrainForCurrentMovementLoad(Train train, float trainMultiplier)
    {
        if (train == null || train == this || !currentMovementLoadTrains.Add(train))
        {
            return;
        }

        currentMovementLoadSpeedMultiplier = Mathf.Clamp(
            currentMovementLoadSpeedMultiplier * Mathf.Clamp(trainMultiplier, 0.01f, 1f),
            0.01f,
            1f);
    }

    protected struct RailSample
    {
        public Railload Rail;
        public float DistanceAlongPath;
        public Vector2 Point;
        public Vector2 Tangent;
        public float SqrDistance;
    }

    private struct ConnectedTrainRailMove
    {
        public Train Train;
        public RailSample StartSample;
        public RailSample TargetSample;
        public Vector2 StartFacingTangent;
        public float TraveledDistance;
        public float FollowOffset;
    }

    private struct ConsistPathSample
    {
        public RailSample Sample;
        public float Distance;
    }

    private struct InitialConsistPathRouteNode
    {
        public RailSample Sample;
        public Vector2 TravelDirection;
        public int ParentIndex;
        public float PathDistance;
    }

    private struct PushContactInfo
    {
        public Train Train;
        public RailSample Sample;
        public Vector2 FacingTangent;
        public Vector2 PushTravelDirection;
        public float GapDistance;
        public float DesiredSpacing;
        public bool ReleaseAfterMove;
    }

    private struct ConsistPathStateSnapshot
    {
        public List<ConsistPathSample> PathSamples;
        public List<Train> TrainOrder;
        public List<float> FollowOffsets;
        public Train Leader;
        public Vector2 TravelDirection;
        public float EndDistance;
    }

    private sealed class PushConsistPathSession
    {
        public readonly List<ConsistPathSample> PathSamples = new List<ConsistPathSample>(64);
        public readonly List<Train> TrainOrder = new List<Train>(8);
        public readonly List<float> FollowOffsets = new List<float>(8);
        public Train Leader;
        public Train PreferredContactTrain;
        public Vector2 TravelDirection;
        public float EndDistance;
        public float BranchReleaseDistanceRemaining;
        public bool BranchReleaseCompleted;
        public int LastUsedFrame;
    }

    public override float EffectiveVehicleMaxSpeed => UsesTrainLoadSpeedReduction
        ? Mathf.Max(0.01f, VehicleMaxSpeed * Mathf.Min(
            ResolveCurrentTrainLoadSpeedMultiplier(),
            Mathf.Clamp(currentMovementLoadSpeedMultiplier, 0.01f, 1f)))
        : base.EffectiveVehicleMaxSpeed;

    protected virtual bool UsesTrainLoadSpeedReduction => true;

    public override void ApplyPlacedRailSample(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        Vector2 resolvedFacingTangent = ResolveAlignedRailTangent(
            facingTangent,
            currentFacingTangent,
            ResolveReferenceFacing());
        base.ApplyPlacedRailSample(rail, distanceAlongPath, railPoint, resolvedFacingTangent);

        Physics.SyncTransforms();
    }

    public override bool TryApplyRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        Vector2 resolvedFacingTangent = ResolveAlignedRailTangent(
            facingTangent,
            currentFacingTangent,
            ResolveReferenceFacing());
        if (!base.TryApplyRailPose(rail, distanceAlongPath, railPoint, resolvedFacingTangent))
        {
            return false;
        }

        SyncRailFacingTangent(resolvedFacingTangent, true);
        return true;
    }

    public bool TryApplyExplicitRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (!base.TryApplyRailPose(rail, distanceAlongPath, railPoint, facingTangent))
        {
            return false;
        }

        SyncRailFacingTangent(facingTangent, true);
        return true;
    }

    public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
        BeginCurrentMovementLoadTracking();
        Vector2 inputVector = new Vector2(worldMoveDirection.x, worldMoveDirection.z);
        float inputMagnitude = Mathf.Clamp01(inputVector.magnitude);
        bool hasInput = inputMagnitude > railInputDeadZone;
        Vector2 inputDirection = hasInput ? inputVector / inputMagnitude : Vector2.zero;
        Vector2 currentPoint = new Vector2(transform.position.x, transform.position.z);
        float maxSqrDistance = railSnapMaxDistance * railSnapMaxDistance;

        Vector2 sampleSearchDirection = hasInput ? inputDirection : ResolveCoastTravelDirection();
        if (sampleSearchDirection.sqrMagnitude <= 0.0001f)
        {
            sampleSearchDirection = Vector2.up;
        }

        sampleSearchDirection.Normalize();
        if (!TryResolveCurrentRailSample(
                currentPoint,
                sampleSearchDirection,
                maxSqrDistance,
                out RailSample currentSample))
        {
            ClearLockedBranchRail();
            ResetVehicleMotion();
            return;
        }

        Vector2 facingReference = hasInput
            ? ResolveReferenceFacing()
            : ResolveCoastFacingDirection();
        Vector2 currentFacing = ResolveFacingTangent(currentSample.Tangent, facingReference);
        float inputAxis = ResolveRailInputAxis(
            hasInput,
            inputDirection,
            inputMagnitude,
            currentFacing,
            currentSample);
        bool isPushingByInput = hasInput && HasConnectedTrainAhead(inputDirection);
        bool switchedToBranch = false;
        if (hasInput
            && !isPushingByInput
            && TryFindBranchRailSample(currentSample, inputDirection, out RailSample branchSample))
        {
            currentSample = branchSample;
            currentFacing = ResolveBranchFacingTangent(currentSample.Tangent, inputDirection);
            inputAxis = ResolveRailInputAxis(
                hasInput,
                inputDirection,
                inputMagnitude,
                currentFacing,
                currentSample);
            switchedToBranch = true;
            LockBranchRail(branchSample.Rail);
        }

        if (ShouldReleaseForeignPushForReverseInput(inputAxis, CurrentVehicleSignedSpeed))
        {
            ClearPushConsistPathSessions();
            ResetVehicleMotion();
        }

        float effectiveMaxSpeed = EffectiveVehicleMaxSpeed;
        float signedSpeed = UpdateVehicleSignedSpeed(inputAxis, deltaTime, effectiveMaxSpeed);
        if (Mathf.Abs(signedSpeed) <= 0.0001f)
        {
            if (!hasInput && TryApplyIdleDocking(currentSample, currentFacing, deltaTime))
            {
                return;
            }

            ApplyRailPose(currentSample, currentFacing, deltaTime, true);
            return;
        }

        float signedStep = signedSpeed
                           * Mathf.Max(0.01f, railMoveSpeedMultiplier)
                           * Mathf.Max(0f, deltaTime);
        signedStep = AdjustDrivenSignedStep(
            currentSample,
            currentFacing,
            hasInput,
            inputDirection,
            deltaTime,
            signedStep);
        if (Mathf.Abs(signedStep) <= 0.0001f)
        {
            if (!hasInput && TryApplyIdleDocking(currentSample, currentFacing, deltaTime))
            {
                return;
            }

            ApplyRailPose(currentSample, currentFacing, deltaTime, true);
            return;
        }

        bool moved = MoveConnectedTrainGroup(
            currentSample,
            currentFacing,
            signedStep,
            deltaTime,
            hasInput,
            inputDirection,
            switchedToBranch);
        ClampCurrentVehicleSignedSpeed(effectiveMaxSpeed);
        if (!moved)
        {
            ClearLockedBranchRail();
            ResetVehicleMotion();
        }
    }

    protected virtual float AdjustDrivenSignedStep(
        RailSample currentSample,
        Vector2 currentFacing,
        bool hasInput,
        Vector2 inputDirection,
        float deltaTime,
        float signedStep)
    {
        return signedStep;
    }

    private bool TryApplyIdleDocking(
        RailSample currentSample,
        Vector2 currentFacing,
        float deltaTime)
    {
        if (TryApplyStationDocking(currentSample, currentFacing, deltaTime))
        {
            return true;
        }

        return TryApplyCustomIdleDocking(currentSample, currentFacing, deltaTime);
    }

    protected virtual bool TryApplyCustomIdleDocking(
        RailSample currentSample,
        Vector2 currentFacing,
        float deltaTime)
    {
        return false;
    }

    private bool TryApplyStationDocking(
        RailSample currentSample,
        Vector2 currentFacing,
        float deltaTime)
    {
        if (deltaTime <= 0f
            || currentSample.Rail == null
            || !TryFindStationDockSample(
                currentSample,
                out RailSample dockSample,
                out float signedPathDelta))
        {
            return false;
        }

        return TryApplyDockingToSample(
            currentSample,
            currentFacing,
            dockSample,
            signedPathDelta,
            deltaTime);
    }

    protected bool TryApplyDockingToSample(
        RailSample currentSample,
        Vector2 currentFacing,
        RailSample dockSample,
        float signedPathDelta,
        float deltaTime,
        bool preserveCoastTravelDirection = false,
        bool snapToDock = false)
    {
        float remainingDistance = Mathf.Abs(signedPathDelta);
        bool shouldSnapToDock = snapToDock || remainingDistance <= ResolveDockCompleteDistance();
        if (shouldSnapToDock && ConnectedTrains.Count <= 0)
        {
            ApplyRailPose(dockSample, currentFacing, deltaTime, true);
            return true;
        }

        Vector2 travelDirection = signedPathDelta >= 0f
            ? currentSample.Tangent
            : -currentSample.Tangent;
        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            travelDirection = dockSample.Point - currentSample.Point;
        }

        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        travelDirection.Normalize();
        float dockStep = shouldSnapToDock
            ? remainingDistance
            : Mathf.Min(
                remainingDistance,
                Mathf.Max(0.01f, stationDockSpeed) * Mathf.Max(0f, deltaTime));
        if (dockStep <= 0.0001f)
        {
            return shouldSnapToDock;
        }

        return TryMoveConnectedTrainGroupForDocking(
            currentSample,
            currentFacing,
            travelDirection,
            dockStep,
            deltaTime,
            preserveCoastTravelDirection);
    }

    private bool TryMoveConnectedTrainGroupForDocking(
        RailSample currentSample,
        Vector2 currentFacing,
        Vector2 travelDirection,
        float dockStep,
        float deltaTime,
        bool preserveCoastTravelDirection)
    {
        if (travelDirection.sqrMagnitude <= 0.0001f || dockStep <= 0.0001f)
        {
            return false;
        }

        travelDirection.Normalize();
        Vector2 previousLastRailTravelDirection = lastRailTravelDirection;
        lastRailTravelDirection = travelDirection;
        bool moved = MoveConnectedTrainGroup(
            currentSample,
            currentFacing,
            dockStep,
            deltaTime,
            false,
            Vector2.zero,
            false);
        if (moved)
        {
            if (preserveCoastTravelDirection)
            {
                lastRailTravelDirection = previousLastRailTravelDirection;
            }

            return true;
        }

        lastRailTravelDirection = previousLastRailTravelDirection;
        return false;
    }

    protected float ResolveDockSearchRadius()
    {
        return Mathf.Max(0.05f, stationDockSearchRadius);
    }

    protected float ResolveDockCaptureDistance()
    {
        return Mathf.Max(
            Mathf.Max(0.05f, stationDockCaptureDistance),
            ResolveDockCompleteDistance());
    }

    protected float ResolveDockCompleteDistance()
    {
        return Mathf.Max(0.001f, stationDockCompleteDistance);
    }

    public bool TryGetPreferredRouteTravelDirection(out Vector2 direction)
    {
        direction = ResolveCoastTravelDirection();
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        return true;
    }

    public float GetBranchPreviewDistance()
    {
        return ResolveBranchLookAheadDistance();
    }

    public bool TryGetRailDockDistanceAtCoordinate(
        Vector2Int railCoordinate,
        out float remainingDistance)
    {
        if (!TryGetRailDockDeltaAtCoordinate(railCoordinate, out float signedPathDelta))
        {
            remainingDistance = 0f;
            return false;
        }

        remainingDistance = Mathf.Abs(signedPathDelta);
        return true;
    }

    public bool TryGetRailDockDeltaAtCoordinate(
        Vector2Int railCoordinate,
        out float signedPathDelta)
    {
        signedPathDelta = 0f;
        if (!TryGetCurrentRailPose(out Railload currentRail, out float currentDistanceAlongPath, out _, out _)
            || currentRail == null
            || !TryFindRailDockSampleAtCoordinate(railCoordinate, currentRail, out RailSample dockSample))
        {
            return false;
        }

        signedPathDelta = dockSample.DistanceAlongPath - currentDistanceAlongPath;
        return true;
    }

    public bool TryDockConnectedTrainGroupAtRailCoordinate(
        Vector2Int railCoordinate,
        float deltaTime,
        bool snapToDock)
    {
        if (!TryGetCurrentRailPose(
                out Railload currentRail,
                out float currentDistanceAlongPath,
                out Vector2 currentPoint,
                out Vector2 currentTangent)
            || currentRail == null
            || !TryFindRailDockSampleAtCoordinate(
                railCoordinate,
                currentRail,
                out RailSample dockSample))
        {
            return false;
        }

        RailSample currentSample = new RailSample
        {
            Rail = currentRail,
            DistanceAlongPath = currentDistanceAlongPath,
            Point = currentPoint,
            Tangent = currentTangent.sqrMagnitude > 0.0001f
                ? currentTangent
                : dockSample.Tangent,
            SqrDistance = 0f
        };
        Vector2 facingTangent = ResolveFacingTangent(
            currentSample.Tangent,
            currentTangent.sqrMagnitude > 0.0001f
                ? currentTangent
                : ResolveReferenceFacing());
        if (facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float signedPathDelta = dockSample.DistanceAlongPath - currentDistanceAlongPath;
        return TryApplyDockingToSample(
            currentSample,
            facingTangent,
            dockSample,
            signedPathDelta,
            deltaTime,
            true,
            snapToDock);
    }

    protected bool TryFindRailDockSampleAtCoordinate(
        Vector2Int railCoordinate,
        Railload currentRail,
        out RailSample dockSample)
    {
        return TryFindStationRailDockSample(railCoordinate, currentRail, out dockSample);
    }

    private bool TryFindStationDockSample(
        RailSample currentSample,
        out RailSample dockSample,
        out float signedPathDelta)
    {
        dockSample = default;
        signedPathDelta = 0f;
        if (currentSample.Rail == null)
        {
            return false;
        }

        CollectStationDockCandidates(currentSample.Point);
        float captureDistance = Mathf.Max(
            Mathf.Max(0.05f, stationDockCaptureDistance),
            Mathf.Max(0.001f, stationDockCompleteDistance));
        float captureSqrDistance = captureDistance * captureDistance;
        float bestScore = float.MaxValue;
        bool found = false;
        for (int i = 0; i < stationCandidateScratch.Count; i++)
        {
            Trainstation station = stationCandidateScratch[i];
            if (station == null
                || !station.TryGetRailCoordinate(out Vector2Int railCoordinate)
                || !TryFindStationRailDockSample(
                    railCoordinate,
                    currentSample.Rail,
                    out RailSample candidateSample))
            {
                continue;
            }

            float candidatePathDelta = candidateSample.DistanceAlongPath - currentSample.DistanceAlongPath;
            float candidatePathDistance = Mathf.Abs(candidatePathDelta);
            float candidateSqrDistance = (candidateSample.Point - currentSample.Point).sqrMagnitude;
            if (candidatePathDistance > captureDistance
                || candidateSqrDistance > captureSqrDistance)
            {
                continue;
            }

            float score = candidatePathDistance + candidateSqrDistance * 0.25f;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            dockSample = candidateSample;
            signedPathDelta = candidatePathDelta;
            found = true;
        }

        stationCandidateScratch.Clear();
        stationSearchScratch.Clear();
        stationRailSearchScratch.Clear();
        return found;
    }

    private void CollectStationDockCandidates(Vector2 point)
    {
        stationCandidateScratch.Clear();
        int searchCells = Mathf.CeilToInt(Mathf.Max(0.05f, stationDockSearchRadius));
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(point.x),
            Mathf.RoundToInt(point.y));

        for (int offsetY = -searchCells; offsetY <= searchCells; offsetY++)
        {
            for (int offsetX = -searchCells; offsetX <= searchCells; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                stationSearchScratch.Clear();
                InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                    coordinate,
                    stationSearchScratch);

                for (int i = 0; i < stationSearchScratch.Count; i++)
                {
                    if (stationSearchScratch[i] is not Trainstation station
                        || stationCandidateScratch.Contains(station))
                    {
                        continue;
                    }

                    stationCandidateScratch.Add(station);
                }
            }
        }

        stationSearchScratch.Clear();
    }

    private bool TryFindStationRailDockSample(
        Vector2Int railCoordinate,
        Railload currentRail,
        out RailSample dockSample)
    {
        dockSample = default;
        if (currentRail == null)
        {
            return false;
        }

        Vector2 stationRailPoint = new Vector2(railCoordinate.x, railCoordinate.y);
        float maxSqrDistance = StationDockRailCoordinateSampleMaxDistance
                               * StationDockRailCoordinateSampleMaxDistance;
        float bestSqrDistance = float.MaxValue;
        bool found = false;

        stationRailSearchScratch.Clear();
        InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
            railCoordinate,
            stationRailSearchScratch);
        for (int i = 0; i < stationRailSearchScratch.Count; i++)
        {
            if (stationRailSearchScratch[i] is not Railload rail
                || rail != currentRail
                || !rail.TryFindNearestRenderedPathSample(
                    stationRailPoint,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent,
                    out float sqrDistance)
                || sqrDistance > maxSqrDistance
                || sqrDistance >= bestSqrDistance)
            {
                continue;
            }

            bestSqrDistance = sqrDistance;
            dockSample.Rail = rail;
            dockSample.DistanceAlongPath = distanceAlongPath;
            dockSample.Point = pathPoint;
            dockSample.Tangent = tangent;
            dockSample.SqrDistance = sqrDistance;
            found = true;
        }

        if (currentRail.TryFindNearestRenderedPathSample(
                stationRailPoint,
                out float fallbackDistanceAlongPath,
                out Vector2 fallbackPathPoint,
                out Vector2 fallbackTangent,
                out float fallbackSqrDistance)
            && fallbackSqrDistance <= maxSqrDistance
            && fallbackSqrDistance < bestSqrDistance)
        {
            dockSample.Rail = currentRail;
            dockSample.DistanceAlongPath = fallbackDistanceAlongPath;
            dockSample.Point = fallbackPathPoint;
            dockSample.Tangent = fallbackTangent;
            dockSample.SqrDistance = fallbackSqrDistance;
            found = true;
        }

        stationRailSearchScratch.Clear();
        return found;
    }

    protected virtual float ResolveRailInputAxis(
        bool hasInput,
        Vector2 inputDirection,
        float inputMagnitude,
        Vector2 facing,
        RailSample currentSample)
    {
        if (!hasInput
            || inputDirection.sqrMagnitude <= 0.0001f
            || facing.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        float inputAxis = Vector2.Dot(inputDirection, facing.normalized) * Mathf.Clamp01(inputMagnitude);
        return Mathf.Abs(inputAxis) <= railInputDeadZone ? 0f : inputAxis;
    }

    public bool TryGetRailDebugDirection(out Vector3 worldPosition, out Vector3 worldDirection)
    {
        worldPosition = transform.position;
        Vector2 direction = ResolveReferenceFacing();
        if (Mathf.Abs(CurrentVehicleSignedSpeed) > 0.001f)
        {
            direction *= Mathf.Sign(CurrentVehicleSignedSpeed);
        }

        worldDirection = new Vector3(direction.x, 0f, direction.y);
        if (worldDirection.sqrMagnitude <= 0.0001f)
        {
            worldDirection = transform.forward;
            worldDirection.y = 0f;
        }

        if (worldDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        worldDirection.Normalize();
        return true;
    }

    public bool IsRailDebugDirectionBlocked(Vector3 worldDirection)
    {
        Vector2 direction = new Vector2(worldDirection.x, worldDirection.z);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        Vector2 currentPoint = new Vector2(transform.position.x, transform.position.z);
        float maxSqrDistance = railSnapMaxDistance * railSnapMaxDistance;
        if (!TryGetStoredRailSample(currentPoint, maxSqrDistance, out RailSample currentSample)
            && !TryFindBestRailSample(currentPoint, direction, maxSqrDistance, out currentSample))
        {
            return true;
        }

        float probeDistance = Mathf.Max(0.01f, RailDebugBlockedProbeDistance);
        if (!TryAdvanceAlongRailNetwork(
                currentSample,
                direction,
                probeDistance,
                out _,
                out float traveledDistance))
        {
            return true;
        }

        return traveledDistance + RailDebugBlockedDistanceEpsilon < probeDistance;
    }

    private bool TryGetStoredRailSample(Vector2 currentPoint, float maxSqrDistance, out RailSample sample)
    {
        sample = default;
        if (!TryGetCurrentRailSample(
                currentPoint,
                maxSqrDistance,
                out Railload rail,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent,
                out _))
        {
            return false;
        }

        if (!rail.TrySampleRenderedPath(distanceAlongPath, out _, out Vector2 sampledTangent))
        {
            return false;
        }

        sample.Rail = rail;
        sample.DistanceAlongPath = distanceAlongPath;
        sample.Point = pathPoint;
        sample.Tangent = tangent.sqrMagnitude > 0.0001f ? tangent : sampledTangent;
        sample.SqrDistance = (currentPoint - pathPoint).sqrMagnitude;
        return sample.SqrDistance <= maxSqrDistance;
    }

    private bool TryResolveCurrentRailSample(
        Vector2 currentPoint,
        Vector2 travelDirection,
        float maxSqrDistance,
        out RailSample sample)
    {
        bool hasStoredSample = TryGetStoredRailSample(
            currentPoint,
            maxSqrDistance,
            out RailSample storedSample);
        if (hasStoredSample && IsRailConnectionBridgeSample(storedSample))
        {
            sample = storedSample;
            return true;
        }

        if (TryFindLockedBranchRailSample(
                currentPoint,
                travelDirection,
                maxSqrDistance,
                out sample))
        {
            return true;
        }

        if (hasStoredSample)
        {
            sample = storedSample;
            return true;
        }

        return TryFindBestRailSample(
            currentPoint,
            travelDirection,
            maxSqrDistance,
            out sample);
    }

    private void LockBranchRail(Railload rail)
    {
        if (rail == null)
        {
            return;
        }

        lockedBranchRail = rail;
        lockedBranchRailDistanceRemaining = ResolveBranchRouteLockDistance();
    }

    private void ClearLockedBranchRail()
    {
        lockedBranchRail = null;
        lockedBranchRailDistanceRemaining = 0f;
    }

    private float ResolveBranchRouteLockDistance()
    {
        return Mathf.Max(
            BranchRouteLockMinDistance,
            ResolveBranchLookAheadDistance() * BranchRouteLockDistanceMultiplier);
    }

    private bool TryFindLockedBranchRailSample(
        Vector2 currentPoint,
        Vector2 travelDirection,
        float maxSqrDistance,
        out RailSample sample)
    {
        sample = default;
        if (lockedBranchRail == null || lockedBranchRailDistanceRemaining <= 0f)
        {
            ClearLockedBranchRail();
            return false;
        }

        if (!lockedBranchRail.TryFindNearestRenderedPathSample(
                currentPoint,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent,
                out float sqrDistance))
        {
            ClearLockedBranchRail();
            return false;
        }

        float maxRetainDistance = Mathf.Max(railSnapMaxDistance, ResolveRailConnectionMaxDistance());
        float maxRetainSqrDistance = Mathf.Max(maxSqrDistance, maxRetainDistance * maxRetainDistance);
        if (sqrDistance > maxRetainSqrDistance)
        {
            ClearLockedBranchRail();
            return false;
        }

        sample.Rail = lockedBranchRail;
        sample.DistanceAlongPath = distanceAlongPath;
        sample.Point = pathPoint;
        sample.Tangent = tangent;
        sample.SqrDistance = sqrDistance;
        return CanContinueAlongRailSample(sample, travelDirection);
    }

    private void UpdateLockedBranchRailAfterSubstep(float appliedDistance)
    {
        if (lockedBranchRail == null)
        {
            return;
        }

        lockedBranchRailDistanceRemaining -= Mathf.Max(0f, appliedDistance);
        if (lockedBranchRailDistanceRemaining <= 0f)
        {
            ClearLockedBranchRail();
            return;
        }

        Vector2 currentPoint = new Vector2(transform.position.x, transform.position.z);
        float maxRetainDistance = Mathf.Max(railSnapMaxDistance, ResolveRailConnectionMaxDistance());
        if (!lockedBranchRail.TryFindNearestRenderedPathSample(
                currentPoint,
                out _,
                out _,
                out _,
                out float sqrDistance)
            || sqrDistance > maxRetainDistance * maxRetainDistance)
        {
            ClearLockedBranchRail();
        }
    }

    private bool CanContinueAlongRailSample(RailSample sample, Vector2 travelDirection)
    {
        if (sample.Rail == null
            || travelDirection.sqrMagnitude <= 0.0001f
            || !sample.Rail.TryGetRenderedPathLength(out float pathLength))
        {
            return true;
        }

        Vector2 pathTangent = ResolveRailPathTangent(sample);
        if (pathTangent.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        float travelDot = Vector2.Dot(travelDirection.normalized, pathTangent);
        if (Mathf.Abs(travelDot) <= RailDirectionReferenceDeadZone)
        {
            return true;
        }

        return travelDot > 0f
            ? sample.DistanceAlongPath < pathLength - ConsistPathSampleDistanceEpsilon
            : sample.DistanceAlongPath > ConsistPathSampleDistanceEpsilon;
    }

    private bool TryAdvanceAlongRailNetwork(
        RailSample startSample,
        Vector2 travelDirection,
        float moveDistance,
        out RailSample targetSample,
        out float traveledDistance,
        List<ConsistPathSample> pathSamples = null,
        float pathStartDistance = 0f,
        bool usePreferredConnectedRailTravelDirection = false,
        bool allowRailConnectionBridgeInterpolation = false)
    {
        targetSample = startSample;
        traveledDistance = 0f;
        float remainingDistance = Mathf.Max(0f, moveDistance);
        AddConsistPathSample(pathSamples, pathStartDistance, startSample);
        if (remainingDistance <= 0.0001f)
        {
            return true;
        }

        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        RailSample currentSample = startSample;
        travelDirection.Normalize();
        for (int hop = 0; hop < RailNetworkAdvanceMaxHops; hop++)
        {
            if (currentSample.Rail == null
                || !currentSample.Rail.TryGetRenderedPathLength(out float pathLength))
            {
                return false;
            }

            Vector2 pathTangent = ResolveRailPathTangent(currentSample);
            if (pathTangent.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float travelDot = Vector2.Dot(travelDirection, pathTangent);
            if (Mathf.Abs(travelDot) <= 0.0001f)
            {
                travelDot = 1f;
            }

            float directionSign = Mathf.Sign(travelDot);
            float availableDistance = directionSign > 0f
                ? pathLength - currentSample.DistanceAlongPath
                : currentSample.DistanceAlongPath;

            if (remainingDistance <= availableDistance + 0.0001f)
            {
                float targetDistance = Mathf.Clamp(
                    currentSample.DistanceAlongPath + directionSign * remainingDistance,
                    0f,
                    pathLength);
                traveledDistance += remainingDistance;
                if (!TryCreateRailSampleAtDistance(
                        currentSample.Rail,
                        targetDistance,
                        out targetSample))
                {
                    return false;
                }

                AddConsistPathSample(pathSamples, pathStartDistance + traveledDistance, targetSample);
                return true;
            }

            float endpointDistance = directionSign > 0f ? pathLength : 0f;
            if (!TryCreateRailSampleAtDistance(
                    currentSample.Rail,
                    endpointDistance,
                    out RailSample endpointSample))
            {
                return false;
            }

            targetSample = endpointSample;
            float consumedDistance = Mathf.Max(0f, availableDistance);
            traveledDistance += consumedDistance;
            remainingDistance -= consumedDistance;
            AddConsistPathSample(pathSamples, pathStartDistance + traveledDistance, endpointSample);
            if (remainingDistance <= 0.0001f)
            {
                return true;
            }

            Vector2 endpointPathTangent = ResolveRailPathTangent(endpointSample);
            Vector2 exitDirection = directionSign > 0f ? endpointPathTangent : -endpointPathTangent;
            if (exitDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            exitDirection.Normalize();
            RailSample connectedSample = default;
            if (!TryFindConnectedRailSample(
                    endpointSample,
                    exitDirection,
                    currentSample.Rail,
                    out connectedSample))
            {
                return false;
            }

            float connectionPathDistance = ResolveRailTransitionMovementDistance(
                endpointSample,
                connectedSample);
            if (connectionPathDistance > 0.0001f)
            {
                float consumedConnectionDistance = 0f;
                float remainingConnectionDistance = connectionPathDistance;
                if (allowRailConnectionBridgeInterpolation)
                {
                    float bridgeProgress = ResolveRailConnectionBridgeProgress(
                        currentSample,
                        endpointSample,
                        connectedSample,
                        connectionPathDistance);
                    remainingConnectionDistance = Mathf.Max(0f, connectionPathDistance - bridgeProgress);
                    consumedConnectionDistance = Mathf.Min(
                        Mathf.Min(remainingDistance, remainingConnectionDistance),
                        ResolveRailConnectionBridgeMaxAdvanceDistance(connectionPathDistance));
                    if (consumedConnectionDistance + ConsistPathSampleDistanceEpsilon < remainingConnectionDistance)
                    {
                        float nextBridgeProgress = bridgeProgress + consumedConnectionDistance;
                        if (TryCreateRailConnectionBridgeSample(
                                endpointSample,
                                connectedSample,
                                connectionPathDistance,
                                nextBridgeProgress,
                                out targetSample))
                        {
                            remainingDistance = 0f;
                            traveledDistance += consumedConnectionDistance;
                            AddConsistPathSample(pathSamples, pathStartDistance + traveledDistance, targetSample);
                            return true;
                        }
                    }
                }

                if (consumedConnectionDistance <= 0f)
                {
                    consumedConnectionDistance = Mathf.Min(remainingDistance, remainingConnectionDistance);
                }

                remainingDistance -= consumedConnectionDistance;
                traveledDistance += consumedConnectionDistance;
            }

            currentSample = connectedSample;
            AddConsistPathSample(pathSamples, pathStartDistance + traveledDistance, currentSample);
            if (!usePreferredConnectedRailTravelDirection
                || !TryResolvePreferredConnectedRailTravelDirection(
                    endpointSample,
                    exitDirection,
                    currentSample,
                    out travelDirection))
            {
                travelDirection = ResolveFacingTangent(currentSample.Tangent, exitDirection);
            }
        }

        targetSample = currentSample;
        return false;
    }

    private static float ResolveRailConnectionBridgeMaxAdvanceDistance(float connectionPathDistance)
    {
        return Mathf.Max(
            ConsistPathSampleDistanceEpsilon * 2f,
            connectionPathDistance * RailConnectionBridgeMaxProgressPerFrame);
    }

    private static float ResolveRailConnectionBridgeProgress(
        RailSample currentSample,
        RailSample endpointSample,
        RailSample connectedSample,
        float connectionPathDistance)
    {
        if (connectionPathDistance <= 0.0001f)
        {
            return connectionPathDistance;
        }

        Vector2 bridgeDelta = connectedSample.Point - endpointSample.Point;
        float bridgePointDistance = bridgeDelta.magnitude;
        if (bridgePointDistance <= 0.0001f)
        {
            return connectionPathDistance;
        }

        Vector2 bridgeDirection = bridgeDelta / bridgePointDistance;
        float pointProgress = Vector2.Dot(currentSample.Point - endpointSample.Point, bridgeDirection);
        float normalizedProgress = Mathf.Clamp01(pointProgress / bridgePointDistance);
        return normalizedProgress * connectionPathDistance;
    }

    private static bool TryCreateRailConnectionBridgeSample(
        RailSample endpointSample,
        RailSample connectedSample,
        float connectionPathDistance,
        float bridgeProgress,
        out RailSample bridgeSample)
    {
        bridgeSample = default;
        if (endpointSample.Rail == null
            || connectedSample.Rail == null
            || connectionPathDistance <= 0.0001f)
        {
            return false;
        }

        float t = Mathf.Clamp01(bridgeProgress / connectionPathDistance);
        Vector2 bridgePoint = Vector2.Lerp(endpointSample.Point, connectedSample.Point, t);
        Vector2 bridgeDirection = connectedSample.Point - endpointSample.Point;
        if (bridgeDirection.sqrMagnitude <= 0.0001f)
        {
            bridgeDirection = connectedSample.Tangent.sqrMagnitude > 0.0001f
                ? connectedSample.Tangent
                : endpointSample.Tangent;
        }

        if (bridgeDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        bridgeDirection.Normalize();
        bridgeSample = new RailSample
        {
            Rail = t >= 1f - ConsistPathSampleDistanceEpsilon
                ? connectedSample.Rail
                : endpointSample.Rail,
            DistanceAlongPath = t >= 1f - ConsistPathSampleDistanceEpsilon
                ? connectedSample.DistanceAlongPath
                : endpointSample.DistanceAlongPath,
            Point = bridgePoint,
            Tangent = bridgeDirection,
            SqrDistance = 0f
        };
        return true;
    }

    protected virtual bool TryResolvePreferredConnectedRailTravelDirection(
        RailSample endpointSample,
        Vector2 exitDirection,
        RailSample connectedSample,
        out Vector2 travelDirection)
    {
        travelDirection = Vector2.zero;
        return false;
    }

    private static Vector2 ResolveRailPathTangent(RailSample sample)
    {
        if (sample.Rail != null
            && sample.Rail.TrySampleRenderedPath(
                sample.DistanceAlongPath,
                out _,
                out Vector2 tangent)
            && tangent.sqrMagnitude > 0.0001f)
        {
            return tangent.normalized;
        }

        return sample.Tangent.sqrMagnitude > 0.0001f
            ? sample.Tangent.normalized
            : Vector2.zero;
    }

    private bool MoveConnectedTrainGroup(
        RailSample currentSample,
        Vector2 currentFacing,
        float signedStep,
        float deltaTime,
        bool hasInput,
        Vector2 inputDirection,
        bool forceConsistRouteLock)
    {
        float requestedDistance = Mathf.Abs(signedStep);
        if (requestedDistance <= 0.0001f)
        {
            return false;
        }

        Vector2 travelDirection = !hasInput && lastRailTravelDirection.sqrMagnitude > 0.0001f
            ? lastRailTravelDirection
            : signedStep >= 0f
                ? currentFacing
                : -currentFacing;
        if (travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        RailSample stepSample = currentSample;
        Vector2 stepFacing = currentFacing;
        bool lockRouteOnSubstep = forceConsistRouteLock;
        float remainingDistance = requestedDistance;
        float maxSubstepDistance = ResolveRailMovementSubstepDistance(requestedDistance);
        int substepBudget = Mathf.Max(
            1,
            Mathf.CeilToInt(requestedDistance / Mathf.Max(MinRailMovementSubstepDistance, maxSubstepDistance)))
            + 2;
        bool movedAny = false;
        for (int substepIndex = 0;
             substepIndex < substepBudget && remainingDistance > 0.0001f;
             substepIndex++)
        {
            travelDirection = ResolveRailMovementSubstepTravelDirection(hasInput, signedStep, stepFacing);
            if (travelDirection.sqrMagnitude <= 0.0001f)
            {
                break;
            }

            travelDirection.Normalize();
            bool allowForeignPushTransfer = ShouldAllowForeignPushTransfer(
                hasInput,
                inputDirection,
                travelDirection);
            if (!allowForeignPushTransfer)
            {
                ClearPushConsistPathSessions();
            }

            float substepDistance = Mathf.Min(remainingDistance, maxSubstepDistance);
            float substepDeltaTime = deltaTime * (substepDistance / requestedDistance);
            if (!TryMoveConnectedTrainGroupSubstep(
                    stepSample,
                    stepFacing,
                    travelDirection,
                    substepDistance,
                    substepDeltaTime,
                    hasInput,
                    inputDirection,
                    allowForeignPushTransfer,
                    lockRouteOnSubstep,
                    out float appliedDistance))
            {
                break;
            }

            lockRouteOnSubstep = false;
            appliedDistance = Mathf.Min(substepDistance, Mathf.Max(0f, appliedDistance));
            if (appliedDistance <= 0.0001f)
            {
                break;
            }

            movedAny = true;
            remainingDistance = Mathf.Max(0f, remainingDistance - appliedDistance);
            UpdateLockedBranchRailAfterSubstep(appliedDistance);
            if (!TryRefreshCurrentRailPoseAfterSubstep(
                    stepFacing,
                    travelDirection,
                    railSnapMaxDistance * railSnapMaxDistance,
                    out stepSample,
                    out stepFacing))
            {
                lastRailTravelDirection = travelDirection;
                break;
            }

            lastRailTravelDirection = ResolveFacingTangentWithFallback(
                stepSample.Tangent,
                travelDirection,
                travelDirection);
            if (IsRailConnectionBridgeSample(stepSample))
            {
                break;
            }
        }

        if (movedAny)
        {
            return true;
        }

        return false;
    }

    private bool TryMoveConnectedTrainGroupSubstep(
        RailSample currentSample,
        Vector2 currentFacing,
        Vector2 travelDirection,
        float requestedDistance,
        float deltaTime,
        bool hasInput,
        Vector2 inputDirection,
        bool allowForeignPushTransfer,
        bool forceConsistRouteLock,
        out float appliedDistance)
    {
        appliedDistance = 0f;
        return TryMoveTrainGroupInternal(
            this,
            currentSample,
            currentFacing,
            travelDirection,
            requestedDistance,
            deltaTime,
            hasInput,
            inputDirection,
            allowForeignPushTransfer,
            !hasInput,
            forceConsistRouteLock,
            0,
            out appliedDistance);
    }

    private float ResolveRailMovementSubstepDistance(float requestedDistance)
    {
        float configuredDistance = Mathf.Max(
            MinRailMovementSubstepDistance,
            railMovementSubstepDistance);
        float contactLimit = Mathf.Max(
            MinRailMovementSubstepDistance,
            PushConsistBranchReleaseDistance * 0.5f);
        float railLimit = Mathf.Max(
            MinRailMovementSubstepDistance,
            Mathf.Min(branchSwitchLookAhead, railConnectionLookAhead) * 0.5f);
        float targetDistance = Mathf.Min(configuredDistance, contactLimit, railLimit);
        if (requestedDistance <= targetDistance)
        {
            return requestedDistance;
        }

        int substepCount = Mathf.Clamp(
            Mathf.CeilToInt(requestedDistance / targetDistance),
            1,
            MaxRailMovementSubsteps);
        return requestedDistance / substepCount;
    }

    private Vector2 ResolveRailMovementSubstepTravelDirection(
        bool hasInput,
        float signedStep,
        Vector2 currentFacing)
    {
        if (!hasInput && lastRailTravelDirection.sqrMagnitude > 0.0001f)
        {
            return lastRailTravelDirection;
        }

        return signedStep >= 0f ? currentFacing : -currentFacing;
    }

    private bool TryRefreshCurrentRailPoseAfterSubstep(
        Vector2 previousFacing,
        Vector2 travelDirection,
        float maxSqrDistance,
        out RailSample currentSample,
        out Vector2 currentFacing)
    {
        currentSample = default;
        currentFacing = previousFacing;
        Vector2 currentPoint = new Vector2(transform.position.x, transform.position.z);
        Vector2 searchDirection = travelDirection.sqrMagnitude > 0.0001f
            ? travelDirection
            : previousFacing;
        if (searchDirection.sqrMagnitude <= 0.0001f)
        {
            searchDirection = Vector2.up;
        }

        if (!TryResolveCurrentRailSample(
                currentPoint,
                searchDirection,
                maxSqrDistance,
                out currentSample))
        {
            return false;
        }

        Vector2 facingReference = previousFacing.sqrMagnitude > 0.0001f
            ? previousFacing
            : ResolveReferenceFacing();
        currentFacing = ResolveFacingTangent(currentSample.Tangent, facingReference);
        return currentFacing.sqrMagnitude > 0.0001f;
    }

    private void PrepareConnectedTrainMovesForTravel(
        Train drivenTrain,
        Vector2 travelDirection,
        bool keepCurrentOrder)
    {
        if (keepCurrentOrder)
        {
            return;
        }

        if (!TryMovePushedConsistEndpointToFront(drivenTrain, travelDirection))
        {
            MoveDrivenTrainToFront(drivenTrain);
        }

        OrderConnectedTrainMovesFromLeader(travelDirection);
    }

    private bool TryMoveTrainGroupInternal(
        Train drivenTrain,
        RailSample drivenSample,
        Vector2 drivenFacing,
        Vector2 travelDirection,
        float requestedDistance,
        float deltaTime,
        bool hasInput,
        Vector2 inputDirection,
        bool allowPushPropagation,
        bool preserveConsistOrder,
        bool forceConsistRouteLock,
        int pushDepth,
        out float appliedDrivenDistance)
    {
        appliedDrivenDistance = 0f;
        if (drivenTrain == null
            || drivenSample.Rail == null
            || requestedDistance < 0f
            || travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        travelDirection.Normalize();
        float maxSqrDistance = railSnapMaxDistance * railSnapMaxDistance;
        if (!TryPopulateConnectedTrainMovesForDrivenTrain(
                drivenTrain,
                drivenSample,
                drivenFacing,
                travelDirection,
                maxSqrDistance,
                preserveConsistOrder))
        {
            ResetConsistPathTape();
            ClearConnectedTrainMovementScratch();
            return false;
        }

        float moveDistance = requestedDistance;
        if (pushDepth < MaxPushPropagationDepth
            && connectedTrainRailMoveScratch.Count > 0
            && TryFindPushContactInfo(
                connectedTrainRailMoveScratch[0],
                travelDirection,
                maxSqrDistance,
                out PushContactInfo contactInfo))
        {
            float foreignDrivenDistance = 0f;
            if (allowPushPropagation)
            {
                ConsistPathStateSnapshot pathSnapshot = CaptureConsistPathState();
                PushConsistPathSession pushSession = GetOrCreatePushConsistPathSession(contactInfo.Train);
                float foreignRequestDistance = requestedDistance;
                if (contactInfo.ReleaseAfterMove)
                {
                    if (pushSession.BranchReleaseDistanceRemaining <= 0.0001f)
                    {
                        pushSession.BranchReleaseDistanceRemaining = PushConsistBranchReleaseDistance;
                        pushSession.BranchReleaseCompleted = false;
                    }

                    foreignRequestDistance = Mathf.Min(
                        requestedDistance,
                        pushSession.BranchReleaseDistanceRemaining);
                }

                foreignRequestDistance *= RegisterConnectedTrainGroupForCurrentMovementLoad(contactInfo.Train, this);
                RestoreConsistPathState(pushSession);
                Vector2 pushedTravelDirection = contactInfo.PushTravelDirection.sqrMagnitude > 0.0001f
                    ? contactInfo.PushTravelDirection.normalized
                    : travelDirection;
                bool movedForeignConsist = TryMoveTrainGroupInternal(
                    contactInfo.Train,
                    contactInfo.Sample,
                    contactInfo.FacingTangent,
                    pushedTravelDirection,
                    foreignRequestDistance,
                    deltaTime,
                    false,
                    Vector2.zero,
                    true,
                    true,
                    false,
                    pushDepth + 1,
                    out foreignDrivenDistance);
                if (movedForeignConsist)
                {
                    SaveConsistPathState(pushSession, contactInfo.Train);
                    if (contactInfo.ReleaseAfterMove)
                    {
                        pushSession.BranchReleaseDistanceRemaining = Mathf.Max(
                            0f,
                            pushSession.BranchReleaseDistanceRemaining - foreignDrivenDistance);
                        if (pushSession.BranchReleaseDistanceRemaining <= 0.0001f)
                        {
                            MarkPushConsistBranchReleaseCompleted(pushSession, contactInfo.Train);
                        }
                    }
                    else
                    {
                        pushSession.BranchReleaseDistanceRemaining = 0f;
                        pushSession.BranchReleaseCompleted = false;
                    }
                }
                else
                {
                    if (HasSavedPushConsistPathState(pushSession))
                    {
                        pushSession.LastUsedFrame = Time.frameCount;
                    }
                    else
                    {
                        ClearPushConsistPathSession(pushSession, contactInfo.Train);
                    }
                }

                RestoreConsistPathState(pathSnapshot);
            }
            else
            {
                ClearPushConsistPathSession(FindPushConsistPathSessionContaining(contactInfo.Train), contactInfo.Train);
            }

            moveDistance = Mathf.Min(
                requestedDistance,
                Mathf.Max(
                    0f,
                    foreignDrivenDistance + contactInfo.GapDistance - contactInfo.DesiredSpacing));

            if (!TryPopulateConnectedTrainMovesForDrivenTrain(
                    drivenTrain,
                    drivenSample,
                    drivenFacing,
                    travelDirection,
                    maxSqrDistance,
                    preserveConsistOrder))
            {
                ResetConsistPathTape();
                ClearConnectedTrainMovementScratch();
                return false;
            }
        }

        return TryApplyPreparedConnectedTrainMoves(
            drivenTrain,
            drivenFacing,
            travelDirection,
            moveDistance,
            deltaTime,
            hasInput,
            inputDirection,
            forceConsistRouteLock,
            out appliedDrivenDistance);
    }

    private static bool ShouldAllowForeignPushTransfer(
        bool hasInput,
        Vector2 inputDirection,
        Vector2 travelDirection)
    {
        if (!hasInput
            || inputDirection.sqrMagnitude <= 0.0001f
            || travelDirection.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Vector2.Dot(inputDirection.normalized, travelDirection.normalized)
               >= -RailDirectionReferenceDeadZone;
    }

    private bool ShouldReleaseForeignPushForReverseInput(float inputAxis, float currentSignedSpeed)
    {
        if (pushConsistPathSessions.Count <= 0
            || Mathf.Abs(inputAxis) <= railInputDeadZone
            || Mathf.Abs(currentSignedSpeed) <= 0.0001f)
        {
            return false;
        }

        PruneStalePushConsistPathSessions();
        return pushConsistPathSessions.Count > 0
               && Mathf.Sign(inputAxis) != Mathf.Sign(currentSignedSpeed);
    }

    private bool TryPopulateConnectedTrainMovesForDrivenTrain(
        Train drivenTrain,
        RailSample drivenSample,
        Vector2 drivenFacing,
        Vector2 travelDirection,
        float maxSqrDistance,
        bool preserveConsistOrder)
    {
        CollectConnectedTrainGroupForMovement(drivenTrain);
        connectedTrainRailMoveScratch.Clear();
        leaderPathFrameScratch.Clear();
        bool hasUnresolvedConnectedTrain = false;
        for (int i = 0; i < connectedTrainGroupScratch.Count; i++)
        {
            Train train = connectedTrainGroupScratch[i];
            if (train == null)
            {
                continue;
            }

            RailSample startSample = train == drivenTrain
                ? drivenSample
                : default;
            Vector2 startFacingTangent = train == drivenTrain
                ? drivenFacing
                : Vector2.zero;
            if (train != drivenTrain
                && !TryResolveRailSampleForTrain(
                    train,
                    travelDirection,
                    maxSqrDistance,
                    out startSample,
                    out startFacingTangent))
            {
                hasUnresolvedConnectedTrain = true;
                break;
            }

            connectedTrainRailMoveScratch.Add(new ConnectedTrainRailMove
            {
                Train = train,
                StartSample = startSample,
                TargetSample = startSample,
                StartFacingTangent = startFacingTangent,
                TraveledDistance = 0f,
                FollowOffset = 0f
            });
        }

        if (hasUnresolvedConnectedTrain || connectedTrainRailMoveScratch.Count <= 0)
        {
            return false;
        }

        bool keptRememberedOrder = preserveConsistOrder
                                   && TryApplyRememberedConsistOrder(travelDirection);
        PrepareConnectedTrainMovesForTravel(drivenTrain, travelDirection, keptRememberedOrder);
        return true;
    }

    private bool TryApplyPreparedConnectedTrainMoves(
        Train drivenTrain,
        Vector2 drivenFacing,
        Vector2 travelDirection,
        float requestedDistance,
        float deltaTime,
        bool hasInput,
        Vector2 inputDirection,
        bool forceConsistRouteLock,
        out float appliedDrivenDistance)
    {
        appliedDrivenDistance = 0f;
        Vector2 routeLeaderTravelDirection = ResolveRouteLeaderTravelDirection(drivenTrain, travelDirection);
        bool switchedRouteLeaderToBranch = TrySwitchRouteLeaderToInputBranch(
            hasInput,
            inputDirection,
            routeLeaderTravelDirection);
        routeLeaderTravelDirection = ResolveRouteLeaderTravelDirection(drivenTrain, travelDirection);
        bool shouldLockConsistRoute = forceConsistRouteLock || switchedRouteLeaderToBranch;
        if (shouldLockConsistRoute
            && !TryLockConsistToRouteLeaderPath(routeLeaderTravelDirection))
        {
            ResetConsistPathTape();
        }

        if (!EnsureConsistPathTape(routeLeaderTravelDirection))
        {
            ResetConsistPathTape();
            ClearConnectedTrainMovementScratch();
            return false;
        }

        ConnectedTrainRailMove routeLeaderMove = connectedTrainRailMoveScratch[0];
        float leaderStartPathDistance = consistPathEndDistance;
        leaderPathFrameScratch.Clear();
        bool advancedLeader = TryAdvanceAlongRailNetwork(
                routeLeaderMove.StartSample,
                routeLeaderTravelDirection,
                requestedDistance,
                out RailSample leaderTargetSample,
                out float leaderTraveledDistance,
                leaderPathFrameScratch,
                leaderStartPathDistance,
                hasInput,
                true);
        if (!advancedLeader && leaderTraveledDistance <= 0.0001f)
        {
            ClearConnectedTrainMovementScratch();
            return false;
        }

        routeLeaderMove.TargetSample = leaderTargetSample;
        routeLeaderMove.TraveledDistance = leaderTraveledDistance;
        connectedTrainRailMoveScratch[0] = routeLeaderMove;

        int restorePathSampleCount = consistPathTape.Count;
        ConsistPathSample restoreLastPathSample = restorePathSampleCount > 0
            ? consistPathTape[restorePathSampleCount - 1]
            : default;
        float restorePathEndDistance = consistPathEndDistance;
        AppendConsistPathFrame(leaderPathFrameScratch);
        float leaderEndPathDistance = leaderStartPathDistance + leaderTraveledDistance;
        consistPathEndDistance = leaderEndPathDistance;
        if (leaderTraveledDistance > 0.0001f)
        {
            TryApplyActualConnectedTrainFollowOffsets(
                routeLeaderTravelDirection,
                false,
                out _,
                leaderTraveledDistance);
        }

        float maxFollowOffset = 0f;
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            maxFollowOffset = Mathf.Max(maxFollowOffset, connectedTrainRailMoveScratch[i].FollowOffset);
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            ConnectedTrainRailMove railMove = connectedTrainRailMoveScratch[i];
            if (railMove.Train == null)
            {
                continue;
            }

            RailSample targetSample = railMove.TargetSample;
            if (leaderTraveledDistance <= 0.0001f)
            {
                targetSample = railMove.StartSample;
            }
            else if (i > 0)
            {
                float targetPathDistance = leaderEndPathDistance - railMove.FollowOffset;
                if (!TrySampleConsistPathTape(targetPathDistance, out targetSample, false))
                {
                    RestoreConsistPathTape(
                        restorePathSampleCount,
                        restoreLastPathSample,
                        restorePathEndDistance);
                    ClearConnectedTrainMovementScratch();
                    return false;
                }
            }

            railMove.TargetSample = targetSample;
            connectedTrainRailMoveScratch[i] = railMove;
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            ConnectedTrainRailMove railMove = connectedTrainRailMoveScratch[i];
            if (railMove.Train == null)
            {
                continue;
            }

            Vector2 facingFallback = railMove.Train == drivenTrain
                ? drivenFacing
                : routeLeaderTravelDirection;
            Vector2 facingTangent = ResolveTrainFacingFromOwnOrientation(
                railMove,
                facingFallback);
            ApplyConnectedTrainRailPose(railMove.Train, railMove.TargetSample, facingTangent, deltaTime);
            RotateConnectedTrainWheels(
                railMove.Train,
                EstimateSignedRailSampleMoveDistance(
                    railMove.Train,
                    railMove.StartSample,
                    railMove.TargetSample));
        }

        int drivenIndex = FindConnectedTrainMoveIndex(drivenTrain);
        if (drivenIndex >= 0)
        {
            ConnectedTrainRailMove drivenMove = connectedTrainRailMoveScratch[drivenIndex];
            float estimatedDrivenDistance = Mathf.Abs(
                EstimateSignedRailSampleMoveDistance(
                    drivenMove.Train,
                    drivenMove.StartSample,
                    drivenMove.TargetSample));
            appliedDrivenDistance = Mathf.Max(
                estimatedDrivenDistance,
                Mathf.Min(requestedDistance, leaderTraveledDistance));
        }

        RegisterPreparedTrainMovesForCurrentMovementLoad();
        RememberConsistPathOrder(routeLeaderTravelDirection);
        TrimConsistPathTape(leaderEndPathDistance - maxFollowOffset - ResolveConsistPathTrimPadding());
        Physics.SyncTransforms();
        ClearConnectedTrainMovementScratch();
        return true;
    }

    private ConsistPathStateSnapshot CaptureConsistPathState()
    {
        return new ConsistPathStateSnapshot
        {
            PathSamples = new List<ConsistPathSample>(consistPathTape),
            TrainOrder = new List<Train>(consistPathTrainOrder),
            FollowOffsets = new List<float>(consistPathFollowOffsets),
            Leader = consistPathLeader,
            TravelDirection = consistPathTravelDirection,
            EndDistance = consistPathEndDistance
        };
    }

    private void RestoreConsistPathState(ConsistPathStateSnapshot snapshot)
    {
        consistPathTape.Clear();
        if (snapshot.PathSamples != null)
        {
            consistPathTape.AddRange(snapshot.PathSamples);
        }

        consistPathTrainOrder.Clear();
        if (snapshot.TrainOrder != null)
        {
            consistPathTrainOrder.AddRange(snapshot.TrainOrder);
        }

        consistPathFollowOffsets.Clear();
        if (snapshot.FollowOffsets != null)
        {
            consistPathFollowOffsets.AddRange(snapshot.FollowOffsets);
        }

        leaderPathFrameScratch.Clear();
        initialConsistSegmentScratch.Clear();
        consistPathLeader = snapshot.Leader;
        consistPathTravelDirection = snapshot.TravelDirection;
        consistPathEndDistance = snapshot.EndDistance;
    }

    private void RestoreConsistPathState(PushConsistPathSession session)
    {
        if (session == null || session.PathSamples.Count <= 0)
        {
            ResetConsistPathTape();
            return;
        }

        consistPathTape.Clear();
        consistPathTape.AddRange(session.PathSamples);
        consistPathTrainOrder.Clear();
        consistPathTrainOrder.AddRange(session.TrainOrder);
        consistPathFollowOffsets.Clear();
        consistPathFollowOffsets.AddRange(session.FollowOffsets);
        leaderPathFrameScratch.Clear();
        initialConsistSegmentScratch.Clear();
        consistPathLeader = session.Leader;
        consistPathTravelDirection = session.TravelDirection;
        consistPathEndDistance = session.EndDistance;
    }

    private void SaveConsistPathState(PushConsistPathSession session, Train preferredContactTrain)
    {
        if (session == null)
        {
            return;
        }

        session.PathSamples.Clear();
        session.PathSamples.AddRange(consistPathTape);
        session.TrainOrder.Clear();
        session.TrainOrder.AddRange(consistPathTrainOrder);
        session.FollowOffsets.Clear();
        session.FollowOffsets.AddRange(consistPathFollowOffsets);
        session.Leader = consistPathLeader;
        if (!CanKeepPreferredPushContact(session, preferredContactTrain))
        {
            session.PreferredContactTrain = preferredContactTrain;
        }

        session.TravelDirection = consistPathTravelDirection;
        session.EndDistance = consistPathEndDistance;
        session.LastUsedFrame = Time.frameCount;
    }

    private static bool CanKeepPreferredPushContact(
        PushConsistPathSession session,
        Train newPreferredContactTrain)
    {
        if (session == null || session.PreferredContactTrain == null)
        {
            return false;
        }

        if (!session.PreferredContactTrain.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (session.PreferredContactTrain == newPreferredContactTrain)
        {
            return true;
        }

        for (int i = 0; i < session.TrainOrder.Count; i++)
        {
            if (session.TrainOrder[i] == session.PreferredContactTrain)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearPushConsistPathSession(PushConsistPathSession session, Train preferredContactTrain)
    {
        if (session == null)
        {
            return;
        }

        session.PathSamples.Clear();
        session.TrainOrder.Clear();
        session.FollowOffsets.Clear();
        session.Leader = null;
        session.PreferredContactTrain = preferredContactTrain;
        session.TravelDirection = Vector2.zero;
        session.EndDistance = 0f;
        session.BranchReleaseDistanceRemaining = 0f;
        session.BranchReleaseCompleted = false;
        session.LastUsedFrame = Time.frameCount;
    }

    private void MarkPushConsistBranchReleaseCompleted(PushConsistPathSession session, Train preferredContactTrain)
    {
        if (session == null)
        {
            return;
        }

        session.PathSamples.Clear();
        session.TrainOrder.Clear();
        session.FollowOffsets.Clear();
        session.Leader = null;
        session.PreferredContactTrain = preferredContactTrain;
        session.TravelDirection = Vector2.zero;
        session.EndDistance = 0f;
        session.BranchReleaseDistanceRemaining = 0f;
        session.BranchReleaseCompleted = true;
        session.LastUsedFrame = Time.frameCount;
    }

    private static bool HasSavedPushConsistPathState(PushConsistPathSession session)
    {
        return session != null
               && session.PathSamples.Count > 0
               && session.TrainOrder.Count > 0
               && session.FollowOffsets.Count == session.TrainOrder.Count;
    }

    private void ClearPushConsistPathSessions()
    {
        pushConsistPathSessions.Clear();
    }

    private PushConsistPathSession GetOrCreatePushConsistPathSession(Train contactTrain)
    {
        PushConsistPathSession session = FindPushConsistPathSessionContaining(contactTrain);
        if (session != null)
        {
            session.LastUsedFrame = Time.frameCount;
            return session;
        }

        session = new PushConsistPathSession
        {
            PreferredContactTrain = contactTrain,
            LastUsedFrame = Time.frameCount
        };
        pushConsistPathSessions.Add(session);
        return session;
    }

    private PushConsistPathSession FindPushConsistPathSessionContaining(Train train)
    {
        if (train == null)
        {
            return null;
        }

        for (int i = 0; i < pushConsistPathSessions.Count; i++)
        {
            PushConsistPathSession session = pushConsistPathSessions[i];
            if (session == null || session.BranchReleaseCompleted)
            {
                continue;
            }

            if (session.Leader == train || session.PreferredContactTrain == train)
            {
                return session;
            }

            for (int orderIndex = 0; orderIndex < session.TrainOrder.Count; orderIndex++)
            {
                if (session.TrainOrder[orderIndex] == train)
                {
                    return session;
                }
            }
        }

        return null;
    }

    private void PruneStalePushConsistPathSessions()
    {
        int currentFrame = Time.frameCount;
        for (int i = pushConsistPathSessions.Count - 1; i >= 0; i--)
        {
            PushConsistPathSession session = pushConsistPathSessions[i];
            if (session == null
                || currentFrame - session.LastUsedFrame > PushConsistSessionRetainFrameCount)
            {
                pushConsistPathSessions.RemoveAt(i);
            }
        }
    }

    private bool HasConnectedTrainAhead(Vector2 travelDirection)
    {
        if (travelDirection.sqrMagnitude <= 0.0001f || ConnectedTrains.Count <= 0)
        {
            return false;
        }

        travelDirection.Normalize();
        Vector2 currentPoint = new Vector2(transform.position.x, transform.position.z);
        float maxSqrDistance = railSnapMaxDistance * railSnapMaxDistance;
        if (TryGetStoredRailSample(currentPoint, maxSqrDistance, out RailSample currentSample)
            || TryFindBestRailSample(currentPoint, travelDirection, maxSqrDistance, out currentSample))
        {
            ConnectedTrainRailMove currentMove = new ConnectedTrainRailMove
            {
                Train = this,
                StartSample = currentSample,
                TargetSample = currentSample,
                StartFacingTangent = ResolveFacingTangent(currentSample.Tangent, travelDirection),
                TraveledDistance = 0f,
                FollowOffset = 0f
            };

            foreach (Train train in ConnectedTrains)
            {
                if (train == null
                    || !train.gameObject.activeInHierarchy
                    || !TryResolveRailSampleForTrain(
                        train,
                        travelDirection,
                        maxSqrDistance,
                        out RailSample trainSample,
                        out Vector2 trainFacing))
                {
                    continue;
                }

                ConnectedTrainRailMove trainMove = new ConnectedTrainRailMove
                {
                    Train = train,
                    StartSample = trainSample,
                    TargetSample = trainSample,
                    StartFacingTangent = trainFacing,
                    TraveledDistance = 0f,
                    FollowOffset = 0f
                };

                if (TryEstimateDirectFrontRailGapDistance(
                    currentMove,
                    trainMove,
                    travelDirection,
                    out _))
                {
                    return true;
                }
            }
        }

        float minAheadDistance = Mathf.Max(0.01f, ConnectionCenterDistance * 0.25f);
        foreach (Train train in ConnectedTrains)
        {
            if (train == null || !train.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector2 trainPoint = new Vector2(train.transform.position.x, train.transform.position.z);
            if (Vector2.Dot(trainPoint - currentPoint, travelDirection) > minAheadDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindPushContactInfo(
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        float maxSqrDistance,
        out PushContactInfo contactInfo)
    {
        contactInfo = default;
        if (frontMove.Train == null
            || frontMove.StartSample.Rail == null
            || travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        travelDirection.Normalize();
        PruneStalePushConsistPathSessions();
        float bestScore = float.MaxValue;
        float bestProjection = float.MaxValue;
        Vector2 frontReferenceDirection = ResolveFacingTangentWithFallback(
            frontMove.StartSample.Tangent,
            travelDirection,
            travelDirection);

        if (TryFindRetainedPushContactInfo(
                frontMove,
                frontReferenceDirection,
                maxSqrDistance,
                out contactInfo))
        {
            return true;
        }

        activeTrainScratch.Clear();
        Train.CollectActiveRuntimeTrains(activeTrainScratch);
        for (int i = 0; i < activeTrainScratch.Count; i++)
        {
            Train candidateTrain = activeTrainScratch[i];
            PushConsistPathSession knownSession = FindPushConsistPathSessionContaining(candidateTrain);
            if (candidateTrain == null
                || (knownSession != null && knownSession.BranchReleaseCompleted)
                || ShouldDeferToRetainedPreferredPushContact(
                    knownSession,
                    candidateTrain,
                    frontReferenceDirection)
                || IsTrainInMovementScratch(candidateTrain)
                || AreTrainsDirectlyConnected(frontMove.Train, candidateTrain)
                || !TryScorePushContact(
                    frontMove,
                    candidateTrain,
                    frontReferenceDirection,
                    maxSqrDistance,
                    PushConsistContactPadding,
                    PushConsistLateralPadding,
                    PushConsistContactPadding,
                    out float score,
                    out float projection)
                || !TryResolveRailSampleForTrain(
                    candidateTrain,
                    frontReferenceDirection,
                    maxSqrDistance,
                    out RailSample candidateSample,
                    out Vector2 candidateFacing))
            {
                continue;
            }

            float adjustedScore = score;
            if (knownSession != null)
            {
                adjustedScore -= knownSession.PreferredContactTrain == candidateTrain
                    ? PushConsistPreferredContactScoreBias
                    : PushConsistKnownGroupScoreBias;
            }

            if (adjustedScore > bestScore + 0.0001f)
            {
                continue;
            }

            if (Mathf.Abs(adjustedScore - bestScore) <= 0.0001f && projection >= bestProjection)
            {
                continue;
            }

            ConnectedTrainRailMove candidateMove = new ConnectedTrainRailMove
            {
                Train = candidateTrain,
                StartSample = candidateSample,
                TargetSample = candidateSample,
                StartFacingTangent = candidateFacing,
                TraveledDistance = 0f,
                FollowOffset = 0f
            };
            float desiredSpacing = ResolveDesiredConsistPairSpacing(frontMove.Train, candidateTrain);
            if (!TryEstimateForwardRailGapDistance(
                    frontMove,
                    candidateMove,
                    frontReferenceDirection,
                    out float gapDistance)
                || gapDistance > desiredSpacing + PushConsistContactPadding)
            {
                if (knownSession != null && knownSession.BranchReleaseCompleted)
                {
                    continue;
                }

                gapDistance = Mathf.Max(0f, projection);
                if (gapDistance > desiredSpacing + PushConsistContactPadding)
                {
                    continue;
                }

                contactInfo = new PushContactInfo
                {
                    Train = candidateTrain,
                    Sample = candidateSample,
                    FacingTangent = candidateFacing,
                    PushTravelDirection = ResolveFacingTangentWithFallback(
                        candidateSample.Tangent,
                        frontReferenceDirection,
                        frontReferenceDirection),
                    GapDistance = gapDistance,
                    DesiredSpacing = desiredSpacing,
                    ReleaseAfterMove = true
                };
                bestScore = adjustedScore;
                bestProjection = projection;
                continue;
            }

            bestScore = adjustedScore;
            bestProjection = projection;
            contactInfo = new PushContactInfo
            {
                Train = candidateTrain,
                Sample = candidateSample,
                FacingTangent = candidateFacing,
                PushTravelDirection = ResolveFacingTangentWithFallback(
                    candidateSample.Tangent,
                    frontReferenceDirection,
                    frontReferenceDirection),
                GapDistance = gapDistance,
                DesiredSpacing = desiredSpacing,
                ReleaseAfterMove = false
            };
        }

        activeTrainScratch.Clear();
        return contactInfo.Train != null;
    }

    private bool TryFindRetainedPushContactInfo(
        ConnectedTrainRailMove frontMove,
        Vector2 frontReferenceDirection,
        float maxSqrDistance,
        out PushContactInfo contactInfo)
    {
        contactInfo = default;
        if (frontReferenceDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float bestPreferredScore = float.MaxValue;
        for (int i = 0; i < pushConsistPathSessions.Count; i++)
        {
            PushConsistPathSession session = pushConsistPathSessions[i];
            if (session == null || session.BranchReleaseCompleted)
            {
                continue;
            }

            if (!IsPushConsistPathSessionDirectionCompatible(session, frontReferenceDirection))
            {
                continue;
            }

            if (TryBuildRetainedPushContactInfo(
                    frontMove,
                    session.PreferredContactTrain,
                    frontReferenceDirection,
                    maxSqrDistance,
                    out PushContactInfo preferredInfo,
                    out float preferredScore)
                && preferredScore < bestPreferredScore)
            {
                bestPreferredScore = preferredScore;
                contactInfo = preferredInfo;
            }
        }

        if (contactInfo.Train != null)
        {
            return true;
        }

        return false;
    }

    private static bool IsPushConsistPathSessionDirectionCompatible(
        PushConsistPathSession session,
        Vector2 referenceDirection)
    {
        if (session == null
            || session.TravelDirection.sqrMagnitude <= 0.0001f
            || referenceDirection.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Vector2.Dot(session.TravelDirection.normalized, referenceDirection.normalized)
               >= -RailDirectionReferenceDeadZone;
    }

    private static bool ShouldDeferToRetainedPreferredPushContact(
        PushConsistPathSession session,
        Train candidateTrain,
        Vector2 referenceDirection)
    {
        if (!HasSavedPushConsistPathState(session)
            || session.BranchReleaseCompleted
            || candidateTrain == null
            || session.PreferredContactTrain == null
            || session.PreferredContactTrain == candidateTrain
            || !session.PreferredContactTrain.gameObject.activeInHierarchy)
        {
            return false;
        }

        return IsPushConsistPathSessionDirectionCompatible(session, referenceDirection);
    }

    private bool TryBuildRetainedPushContactInfo(
        ConnectedTrainRailMove frontMove,
        Train candidateTrain,
        Vector2 frontReferenceDirection,
        float maxSqrDistance,
        out PushContactInfo contactInfo,
        out float score)
    {
        contactInfo = default;
        score = float.MaxValue;
        if (frontMove.Train == null
            || candidateTrain == null
            || !candidateTrain.gameObject.activeInHierarchy
            || IsTrainInMovementScratch(candidateTrain)
            || AreTrainsDirectlyConnected(frontMove.Train, candidateTrain)
            || !TryResolveRailSampleForTrain(
                candidateTrain,
                frontReferenceDirection,
                maxSqrDistance,
                out RailSample candidateSample,
                out Vector2 candidateFacing))
        {
            return false;
        }

        Vector2 normalizedReferenceDirection = frontReferenceDirection.normalized;
        ConnectedTrainRailMove candidateMove = new ConnectedTrainRailMove
        {
            Train = candidateTrain,
            StartSample = candidateSample,
            TargetSample = candidateSample,
            StartFacingTangent = candidateFacing,
            TraveledDistance = 0f,
            FollowOffset = 0f
        };

        Vector2 delta = candidateSample.Point - frontMove.StartSample.Point;
        float projection = Vector2.Dot(delta, normalizedReferenceDirection);
        if (projection < -PushConsistRetainedContactPadding)
        {
            return false;
        }

        float lateralDistance = Mathf.Abs(
            (normalizedReferenceDirection.x * delta.y) - (normalizedReferenceDirection.y * delta.x));
        float maxLateralDistance = Mathf.Max(
            frontMove.Train.ConnectionMaxLateralDistance,
            candidateTrain.ConnectionMaxLateralDistance) + PushConsistRetainedLateralPadding;
        if (lateralDistance > maxLateralDistance)
        {
            return false;
        }

        float desiredSpacing = ResolveDesiredConsistPairSpacing(frontMove.Train, candidateTrain);
        bool releaseAfterMove = false;
        bool hasRailGap = TryEstimateForwardRailGapDistance(
                frontMove,
                candidateMove,
                normalizedReferenceDirection,
                out float gapDistance);
        float maxRetainedGap = desiredSpacing + PushConsistRetainedContactPadding;
        if (!hasRailGap || gapDistance > maxRetainedGap)
        {
            gapDistance = Mathf.Max(0f, projection);
            if (gapDistance > maxRetainedGap)
            {
                return false;
            }

            releaseAfterMove = true;
        }

        score = Mathf.Abs(gapDistance - desiredSpacing) + lateralDistance * 2f;
        contactInfo = new PushContactInfo
        {
            Train = candidateTrain,
            Sample = candidateSample,
            FacingTangent = candidateFacing,
            PushTravelDirection = ResolveFacingTangentWithFallback(
                candidateSample.Tangent,
                frontReferenceDirection,
                frontReferenceDirection),
            GapDistance = gapDistance,
            DesiredSpacing = desiredSpacing,
            ReleaseAfterMove = releaseAfterMove
        };
        return true;
    }

    private bool TryScorePushContact(
        ConnectedTrainRailMove anchorMove,
        Train candidateTrain,
        Vector2 preferredDirection,
        float maxSqrDistance,
        float contactPadding,
        float lateralPadding,
        float behindPadding,
        out float score,
        out float projection)
    {
        score = float.MaxValue;
        projection = 0f;
        if (anchorMove.Train == null
            || candidateTrain == null
            || anchorMove.StartSample.Rail == null
            || preferredDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector2 anchorDirection = ResolveFacingTangentWithFallback(
            anchorMove.StartSample.Tangent,
            preferredDirection,
            preferredDirection);
        if (anchorDirection.sqrMagnitude <= 0.0001f
            || !TryResolveRailSampleForTrain(
                candidateTrain,
                anchorDirection,
                maxSqrDistance,
                out RailSample candidateSample,
                out _))
        {
            return false;
        }

        Vector2 delta = candidateSample.Point - anchorMove.StartSample.Point;
        projection = Vector2.Dot(delta, anchorDirection);
        float desiredSpacing = ResolveDesiredConsistPairSpacing(anchorMove.Train, candidateTrain);
        float maxContactDistance = desiredSpacing + contactPadding;
        if (projection < -behindPadding || projection > maxContactDistance)
        {
            return false;
        }

        float lateralDistance = Mathf.Abs((anchorDirection.x * delta.y) - (anchorDirection.y * delta.x));
        float maxLateralDistance = Mathf.Max(
            anchorMove.Train.ConnectionMaxLateralDistance,
            candidateTrain.ConnectionMaxLateralDistance) + lateralPadding;
        if (lateralDistance > maxLateralDistance)
        {
            return false;
        }

        Vector2 candidateDirection = candidateSample.Tangent.sqrMagnitude > 0.0001f
            ? candidateSample.Tangent.normalized
            : anchorDirection;
        float tangentDot = Mathf.Abs(Vector2.Dot(anchorDirection, candidateDirection));
        float minForwardDot = Mathf.Min(
            anchorMove.Train.ConnectionMinForwardDot,
            candidateTrain.ConnectionMinForwardDot);
        if (tangentDot < minForwardDot)
        {
            return false;
        }

        score = Mathf.Abs(projection - desiredSpacing) + lateralDistance * 2f - tangentDot * 0.1f;
        return true;
    }

    private bool IsTrainInMovementScratch(Train train)
    {
        if (train == null)
        {
            return false;
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            if (connectedTrainRailMoveScratch[i].Train == train)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryMovePushedConsistEndpointToFront(Train drivenTrain, Vector2 travelDirection)
    {
        if (travelDirection.sqrMagnitude <= 0.0001f || connectedTrainRailMoveScratch.Count <= 1)
        {
            return false;
        }

        travelDirection.Normalize();
        int drivenIndex = FindConnectedTrainMoveIndex(drivenTrain);
        if (drivenIndex < 0)
        {
            return false;
        }

        ConnectedTrainRailMove drivenMove = connectedTrainRailMoveScratch[drivenIndex];
        int frontNeighborIndex = FindDirectFrontNeighborMoveIndex(
            drivenMove,
            drivenIndex,
            travelDirection);
        if (frontNeighborIndex < 0)
        {
            return false;
        }

        int endpointIndex = FindConsistEndpointMoveIndex(drivenIndex, frontNeighborIndex);
        MoveConnectedTrainMoveToFront(endpointIndex);
        return true;
    }

    private int FindConnectedTrainMoveIndex(Train train)
    {
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            if (connectedTrainRailMoveScratch[i].Train == train)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindDirectFrontNeighborMoveIndex(
        ConnectedTrainRailMove drivenMove,
        int drivenIndex,
        Vector2 travelDirection)
    {
        int bestRailIndex = -1;
        float bestRailGapDistance = float.MaxValue;
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            if (i == drivenIndex)
            {
                continue;
            }

            ConnectedTrainRailMove candidateMove = connectedTrainRailMoveScratch[i];
            if (!TryEstimateDirectFrontRailGapDistance(
                drivenMove,
                candidateMove,
                travelDirection,
                out float gapDistance)
                || gapDistance >= bestRailGapDistance)
            {
                continue;
            }

            bestRailGapDistance = gapDistance;
            bestRailIndex = i;
        }

        if (bestRailIndex >= 0)
        {
            return bestRailIndex;
        }

        int bestIndex = -1;
        float bestProjection = Mathf.Max(0.01f, ConnectionCenterDistance * 0.25f);
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            if (i == drivenIndex)
            {
                continue;
            }

            ConnectedTrainRailMove candidateMove = connectedTrainRailMoveScratch[i];
            if (!AreTrainsMovementAdjacent(drivenMove.Train, candidateMove.Train))
            {
                continue;
            }

            float projection = Vector2.Dot(
                candidateMove.StartSample.Point - drivenMove.StartSample.Point,
                travelDirection);
            if (projection <= bestProjection)
            {
                continue;
            }

            bestProjection = projection;
            bestIndex = i;
        }

        return bestIndex;
    }

    private bool TryEstimateDirectFrontRailGapDistance(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        out float gapDistance)
    {
        gapDistance = 0f;
        if (!AreTrainsMovementAdjacent(backMove.Train, frontMove.Train)
            || !TryEstimateForwardRailGapDistance(
                backMove,
                frontMove,
                travelDirection,
                out gapDistance))
        {
            return false;
        }

        float maxGapDistance = ResolveMaxDirectConnectedRailGapDistance(backMove, frontMove);
        return gapDistance <= maxGapDistance + ConsistPathSampleDistanceEpsilon;
    }

    private float ResolveMaxDirectConnectedRailGapDistance(
        ConnectedTrainRailMove first,
        ConnectedTrainRailMove second)
    {
        float snapDistance = 0f;
        if (first.Train != null)
        {
            snapDistance = Mathf.Max(snapDistance, first.Train.ConnectionSnapMaxDistance);
        }

        if (second.Train != null)
        {
            snapDistance = Mathf.Max(snapDistance, second.Train.ConnectionSnapMaxDistance);
        }

        return ResolveDesiredConsistPairSpacing(first, second)
               + Mathf.Max(snapDistance, railSnapMaxDistance)
               + PushConsistGapTolerance;
    }

    private int FindConsistEndpointMoveIndex(int previousIndex, int currentIndex)
    {
        int endpointIndex = currentIndex;
        for (int hop = 0; hop < connectedTrainRailMoveScratch.Count; hop++)
        {
            int nextIndex = FindNextDirectConnectedMoveIndex(previousIndex, currentIndex);
            if (nextIndex < 0)
            {
                return endpointIndex;
            }

            previousIndex = currentIndex;
            currentIndex = nextIndex;
            endpointIndex = currentIndex;
        }

        return endpointIndex;
    }

    private int FindNextDirectConnectedMoveIndex(int previousIndex, int currentIndex)
    {
        if (currentIndex < 0 || currentIndex >= connectedTrainRailMoveScratch.Count)
        {
            return -1;
        }

        ConnectedTrainRailMove currentMove = connectedTrainRailMoveScratch[currentIndex];
        int bestIndex = -1;
        float bestDistance = -1f;
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            if (i == currentIndex || i == previousIndex)
            {
                continue;
            }

            ConnectedTrainRailMove candidateMove = connectedTrainRailMoveScratch[i];
            if (!AreTrainsMovementAdjacent(currentMove.Train, candidateMove.Train))
            {
                continue;
            }

            float sqrDistance = (candidateMove.StartSample.Point - currentMove.StartSample.Point).sqrMagnitude;
            if (sqrDistance <= bestDistance)
            {
                continue;
            }

            bestDistance = sqrDistance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private void MoveConnectedTrainMoveToFront(int moveIndex)
    {
        if (moveIndex <= 0 || moveIndex >= connectedTrainRailMoveScratch.Count)
        {
            return;
        }

        ConnectedTrainRailMove move = connectedTrainRailMoveScratch[moveIndex];
        connectedTrainRailMoveScratch.RemoveAt(moveIndex);
        connectedTrainRailMoveScratch.Insert(0, move);
    }

    private void MoveDrivenTrainToFront()
    {
        MoveDrivenTrainToFront(this);
    }

    private void MoveDrivenTrainToFront(Train drivenTrain)
    {
        if (connectedTrainRailMoveScratch.Count <= 1)
        {
            return;
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            if (connectedTrainRailMoveScratch[i].Train != drivenTrain)
            {
                continue;
            }

            if (i > 0)
            {
                ConnectedTrainRailMove drivenMove = connectedTrainRailMoveScratch[i];
                connectedTrainRailMoveScratch[i] = connectedTrainRailMoveScratch[0];
                connectedTrainRailMoveScratch[0] = drivenMove;
            }

            return;
        }
    }

    private void OrderConnectedTrainMovesFromLeader(Vector2 travelDirection)
    {
        if (connectedTrainRailMoveScratch.Count <= 2)
        {
            return;
        }

        connectedTrainOrderScratch.Clear();
        ConnectedTrainRailMove leaderMove = connectedTrainRailMoveScratch[0];
        connectedTrainOrderScratch.Add(leaderMove);

        while (connectedTrainOrderScratch.Count < connectedTrainRailMoveScratch.Count)
        {
            ConnectedTrainRailMove previousMove = connectedTrainOrderScratch[connectedTrainOrderScratch.Count - 1];
            int bestIndex = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
            {
                ConnectedTrainRailMove candidateMove = connectedTrainRailMoveScratch[i];
                if (candidateMove.Train == null
                    || IsTrainAlreadyOrdered(candidateMove.Train))
                {
                    continue;
                }

                bool isDirectlyConnected = AreTrainsMovementAdjacent(previousMove.Train, candidateMove.Train);
                float score;
                if (TryEstimateDirectFrontRailGapDistance(
                    candidateMove,
                    previousMove,
                    travelDirection,
                    out float railGapDistance))
                {
                    score = Mathf.Abs(ResolveDesiredConsistPairSpacing(previousMove, candidateMove) - railGapDistance);
                }
                else
                {
                    Vector2 delta = candidateMove.StartSample.Point - previousMove.StartSample.Point;
                    float behindDistance = travelDirection.sqrMagnitude > 0.0001f
                        ? Mathf.Max(0f, -Vector2.Dot(delta, travelDirection.normalized))
                        : 0f;
                    float lateralDistance = delta.sqrMagnitude - behindDistance * behindDistance;
                    score = (isDirectlyConnected ? 0f : 1000f)
                            + lateralDistance
                            + Mathf.Abs(EstimateConsistSampleDistance(previousMove, candidateMove) - behindDistance) * 0.25f;
                }

                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestIndex = i;
            }

            if (bestIndex < 0)
            {
                break;
            }

            connectedTrainOrderScratch.Add(connectedTrainRailMoveScratch[bestIndex]);
        }

        if (connectedTrainOrderScratch.Count != connectedTrainRailMoveScratch.Count)
        {
            connectedTrainOrderScratch.Clear();
            return;
        }

        connectedTrainRailMoveScratch.Clear();
        for (int i = 0; i < connectedTrainOrderScratch.Count; i++)
        {
            connectedTrainRailMoveScratch.Add(connectedTrainOrderScratch[i]);
        }

        connectedTrainOrderScratch.Clear();
    }

    private bool TryApplyRememberedConsistOrder(Vector2 travelDirection)
    {
        if (!CanReuseRememberedConsistOrder(travelDirection))
        {
            return false;
        }

        connectedTrainOrderScratch.Clear();
        for (int i = 0; i < consistPathTrainOrder.Count; i++)
        {
            Train rememberedTrain = consistPathTrainOrder[i];
            if (rememberedTrain == null || IsTrainAlreadyOrdered(rememberedTrain))
            {
                connectedTrainOrderScratch.Clear();
                return false;
            }

            int moveIndex = FindConnectedTrainMoveIndex(rememberedTrain);
            if (moveIndex < 0)
            {
                connectedTrainOrderScratch.Clear();
                return false;
            }

            ConnectedTrainRailMove railMove = connectedTrainRailMoveScratch[moveIndex];
            railMove.FollowOffset = consistPathFollowOffsets[i];
            connectedTrainOrderScratch.Add(railMove);
        }

        if (connectedTrainOrderScratch.Count != connectedTrainRailMoveScratch.Count)
        {
            connectedTrainOrderScratch.Clear();
            return false;
        }

        connectedTrainRailMoveScratch.Clear();
        for (int i = 0; i < connectedTrainOrderScratch.Count; i++)
        {
            connectedTrainRailMoveScratch.Add(connectedTrainOrderScratch[i]);
        }

        connectedTrainOrderScratch.Clear();
        return true;
    }

    private bool CanReuseRememberedConsistOrder(Vector2 travelDirection)
    {
        if (consistPathTape.Count <= 0
            || connectedTrainRailMoveScratch.Count <= 0
            || consistPathTrainOrder.Count != connectedTrainRailMoveScratch.Count
            || consistPathFollowOffsets.Count != connectedTrainRailMoveScratch.Count)
        {
            return false;
        }

        if (travelDirection.sqrMagnitude <= 0.0001f
            || consistPathTravelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        return Vector2.Dot(consistPathTravelDirection.normalized, travelDirection.normalized)
               >= ConsistPathDirectionMinDot;
    }

    private bool IsTrainAlreadyOrdered(Train train)
    {
        for (int i = 0; i < connectedTrainOrderScratch.Count; i++)
        {
            if (connectedTrainOrderScratch[i].Train == train)
            {
                return true;
            }
        }

        return false;
    }

    private bool AreTrainsMovementAdjacent(Train first, Train second)
    {
        return AreTrainsDirectlyConnected(first, second);
    }

    private static bool AreTrainsDirectlyConnected(Train first, Train second)
    {
        if (first == null || second == null)
        {
            return false;
        }

        foreach (Train connectedTrain in first.ConnectedTrains)
        {
            if (connectedTrain == second)
            {
                return true;
            }
        }

        return false;
    }

    private bool TrySwitchRouteLeaderToInputBranch(
        bool hasInput,
        Vector2 inputDirection,
        Vector2 travelDirection)
    {
        if (!hasInput
            || inputDirection.sqrMagnitude <= 0.0001f
            || connectedTrainRailMoveScratch.Count <= 0)
        {
            return false;
        }

        ConnectedTrainRailMove leaderMove = connectedTrainRailMoveScratch[0];
        if (leaderMove.Train == this
            || !TryFindBranchRailSample(leaderMove.StartSample, inputDirection, out RailSample branchSample))
        {
            return false;
        }

        leaderMove.StartSample = branchSample;
        leaderMove.TargetSample = branchSample;
        leaderMove.StartFacingTangent = ResolveFollowerFacingTangent(
            branchSample.Tangent,
            leaderMove.StartFacingTangent,
            travelDirection);
        connectedTrainRailMoveScratch[0] = leaderMove;
        return true;
    }

    private static Vector2 ResolveTrainFacingFromOwnOrientation(
        ConnectedTrainRailMove railMove,
        Vector2 fallbackFacing)
    {
        Vector2 referenceFacing = railMove.StartFacingTangent.sqrMagnitude > 0.0001f
            ? railMove.StartFacingTangent
            : fallbackFacing;
        if (referenceFacing.sqrMagnitude <= 0.0001f)
        {
            referenceFacing = railMove.TargetSample.Tangent;
        }

        return ResolveFollowerFacingTangent(
            railMove.TargetSample.Tangent,
            railMove.StartFacingTangent,
            referenceFacing);
    }

    private Vector2 ResolveRouteLeaderTravelDirection(Train drivenTrain, Vector2 fallbackDirection)
    {
        if (connectedTrainRailMoveScratch.Count <= 0)
        {
            return fallbackDirection.sqrMagnitude > 0.0001f
                ? fallbackDirection.normalized
                : Vector2.zero;
        }

        ConnectedTrainRailMove leaderMove = connectedTrainRailMoveScratch[0];
        if (leaderMove.Train == drivenTrain && fallbackDirection.sqrMagnitude > 0.0001f)
        {
            return fallbackDirection.normalized;
        }

        if (connectedTrainRailMoveScratch.Count > 1
            && TryResolveInitialConsistSegmentDirection(
                connectedTrainRailMoveScratch[1],
                leaderMove,
                fallbackDirection,
                out Vector2 leaderTravelDirection)
            && leaderTravelDirection.sqrMagnitude > 0.0001f)
        {
            return AlignDirectionWithReference(leaderTravelDirection, fallbackDirection);
        }

        Vector2 leaderReferenceDirection = leaderMove.StartFacingTangent.sqrMagnitude > 0.0001f
            ? leaderMove.StartFacingTangent
            : fallbackDirection;
        return AlignDirectionWithReference(
            ResolveFacingTangentWithFallback(
            leaderMove.StartSample.Tangent,
            fallbackDirection,
            leaderReferenceDirection),
            fallbackDirection);
    }

    private static Vector2 AlignDirectionWithReference(Vector2 direction, Vector2 referenceDirection)
    {
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return referenceDirection.sqrMagnitude > 0.0001f
                ? referenceDirection.normalized
                : Vector2.zero;
        }

        direction.Normalize();
        if (referenceDirection.sqrMagnitude > 0.0001f
            && Vector2.Dot(direction, referenceDirection.normalized) < -RailDirectionReferenceDeadZone)
        {
            direction = -direction;
        }

        return direction;
    }

    private static float EstimateConsistSampleDistance(
        ConnectedTrainRailMove first,
        ConnectedTrainRailMove second)
    {
        if (first.StartSample.Rail != null
            && first.StartSample.Rail == second.StartSample.Rail)
        {
            float railDistance = Mathf.Abs(
                first.StartSample.DistanceAlongPath - second.StartSample.DistanceAlongPath);
            if (railDistance > 0.0001f)
            {
                return railDistance;
            }
        }

        float pointDistance = Vector2.Distance(first.StartSample.Point, second.StartSample.Point);
        if (pointDistance > 0.0001f)
        {
            return pointDistance;
        }

        if (first.Train != null && second.Train != null)
        {
            return (first.Train.ConnectionCenterDistance + second.Train.ConnectionCenterDistance) * 0.5f;
        }

        return 0.05f;
    }

    private bool TryBuildInitialConsistPathSegment(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        float pathStartDistance,
        out float pathEndDistance)
    {
        pathEndDistance = pathStartDistance;
        if (backMove.StartSample.Rail == null
            || frontMove.StartSample.Rail == null
            || !TryResolveInitialConsistSegmentDirection(
                backMove,
                frontMove,
                travelDirection,
                out Vector2 initialTravelDirection))
        {
            return false;
        }

        if (TryCommitInitialConsistPathSegmentInDirection(
                backMove,
                frontMove,
                initialTravelDirection,
                pathStartDistance,
                out pathEndDistance))
        {
            return true;
        }

        if (TryCommitInitialConsistPathSegmentWithRouteSearch(
                backMove,
                frontMove,
                initialTravelDirection,
                pathStartDistance,
                out pathEndDistance))
        {
            return true;
        }

        if (TryCommitInitialConsistPathSegmentInDirection(
                backMove,
                frontMove,
                -initialTravelDirection,
                pathStartDistance,
                out pathEndDistance))
        {
            return true;
        }

        if (TryCommitInitialConsistPathSegmentWithRouteSearch(
                backMove,
                frontMove,
                -initialTravelDirection,
                pathStartDistance,
                out pathEndDistance))
        {
            return true;
        }

        pathEndDistance = pathStartDistance;
        return false;
    }

    private bool TryCommitInitialConsistPathSegmentInDirection(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        float pathStartDistance,
        out float pathEndDistance)
    {
        initialConsistSegmentScratch.Clear();
        if (TryBuildInitialConsistPathSegmentInDirection(
                backMove,
                frontMove,
                travelDirection,
                pathStartDistance,
                initialConsistSegmentScratch,
                out pathEndDistance))
        {
            AppendConsistPathFrame(initialConsistSegmentScratch);
            initialConsistSegmentScratch.Clear();
            return true;
        }

        initialConsistSegmentScratch.Clear();
        return false;
    }

    private bool TryCommitInitialConsistPathSegmentWithRouteSearch(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        float pathStartDistance,
        out float pathEndDistance)
    {
        initialConsistSegmentScratch.Clear();
        if (TryBuildInitialConsistPathSegmentWithRouteSearch(
                backMove,
                frontMove,
                travelDirection,
                pathStartDistance,
                initialConsistSegmentScratch,
                out pathEndDistance))
        {
            AppendConsistPathFrame(initialConsistSegmentScratch);
            initialConsistSegmentScratch.Clear();
            return true;
        }

        initialConsistSegmentScratch.Clear();
        return false;
    }

    private bool TryBuildInitialConsistPathSegmentWithRouteSearch(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 initialTravelDirection,
        float pathStartDistance,
        List<ConsistPathSample> pathSamples,
        out float pathEndDistance)
    {
        pathEndDistance = pathStartDistance;
        if (backMove.StartSample.Rail == null
            || frontMove.StartSample.Rail == null
            || initialTravelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        initialConsistPathRouteNodes.Clear();
        initialConsistPathRouteQueue.Clear();
        float maxSearchDistance = ResolveInitialConsistSegmentSearchDistance(backMove, frontMove) * 2f;
        AddInitialConsistPathRouteNode(
            backMove.StartSample,
            initialTravelDirection.normalized,
            -1,
            pathStartDistance);
        initialConsistPathRouteQueue.Enqueue(0);

        int maxNodeCount = RailNetworkAdvanceMaxHops * 6;
        while (initialConsistPathRouteQueue.Count > 0
               && initialConsistPathRouteNodes.Count < maxNodeCount)
        {
            int currentIndex = initialConsistPathRouteQueue.Dequeue();
            InitialConsistPathRouteNode currentNode = initialConsistPathRouteNodes[currentIndex];
            if (TryAddInitialConsistPathTargetRouteNode(
                    currentIndex,
                    currentNode,
                    frontMove.StartSample,
                    pathStartDistance,
                    maxSearchDistance,
                    out int targetIndex))
            {
                RebuildInitialConsistPathSamples(targetIndex, pathSamples);
                pathEndDistance = initialConsistPathRouteNodes[targetIndex].PathDistance;
                ClearInitialConsistPathRouteScratch();
                return true;
            }

            if (TryAddInitialConsistPathInternalTargetRouteNode(
                    currentIndex,
                    currentNode,
                    frontMove.StartSample,
                    pathStartDistance,
                    maxSearchDistance,
                    out targetIndex))
            {
                RebuildInitialConsistPathSamples(targetIndex, pathSamples);
                pathEndDistance = initialConsistPathRouteNodes[targetIndex].PathDistance;
                ClearInitialConsistPathRouteScratch();
                return true;
            }

            if (currentNode.Sample.Rail == null
                || !currentNode.Sample.Rail.TryGetRenderedPathLength(out float pathLength))
            {
                continue;
            }

            TryEnqueueInitialConsistPathEndpointRouteNode(
                currentIndex,
                currentNode,
                pathLength,
                pathStartDistance,
                maxSearchDistance);
            TryEnqueueInitialConsistPathConnectionRouteNodes(
                currentIndex,
                currentNode,
                pathLength,
                pathStartDistance,
                maxSearchDistance);
        }

        ClearInitialConsistPathRouteScratch();
        return false;
    }

    private bool TryAddInitialConsistPathTargetRouteNode(
        int currentIndex,
        InitialConsistPathRouteNode currentNode,
        RailSample targetSample,
        float pathStartDistance,
        float maxSearchDistance,
        out int targetIndex)
    {
        targetIndex = -1;
        if (currentNode.Sample.Rail == null || currentNode.Sample.Rail != targetSample.Rail)
        {
            return false;
        }

        if (!TryGetDistanceToTargetOnCurrentRail(
                currentNode.Sample,
                targetSample,
                currentNode.TravelDirection,
                out float distanceToTarget))
        {
            return false;
        }

        float targetPathDistance = currentNode.PathDistance + distanceToTarget;
        if (targetPathDistance - pathStartDistance
            > maxSearchDistance + ConsistPathSampleDistanceEpsilon)
        {
            return false;
        }

        targetIndex = AddInitialConsistPathRouteNode(
            targetSample,
            currentNode.TravelDirection,
            currentIndex,
            targetPathDistance);
        return true;
    }

    private bool TryAddInitialConsistPathInternalTargetRouteNode(
        int currentIndex,
        InitialConsistPathRouteNode currentNode,
        RailSample targetSample,
        float pathStartDistance,
        float maxSearchDistance,
        out int targetIndex)
    {
        targetIndex = -1;
        if (currentNode.Sample.Rail == null
            || targetSample.Rail == null
            || currentNode.Sample.Rail == targetSample.Rail
            || currentNode.TravelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float maxConnectionDistance = ResolveRailConnectionMaxDistance();
        if (!currentNode.Sample.Rail.TryFindNearestRenderedPathSample(
                targetSample.Point,
                out float bridgeDistanceAlongPath,
                out Vector2 bridgePoint,
                out Vector2 bridgeTangent,
                out float bridgeSqrDistance)
            || bridgeSqrDistance > maxConnectionDistance * maxConnectionDistance)
        {
            return false;
        }

        if (!AreInternalConsistBridgeTangentsAligned(
                bridgeTangent,
                targetSample.Tangent,
                currentNode.TravelDirection))
        {
            return false;
        }

        RailSample bridgeSample = new RailSample
        {
            Rail = currentNode.Sample.Rail,
            DistanceAlongPath = bridgeDistanceAlongPath,
            Point = bridgePoint,
            Tangent = bridgeTangent,
            SqrDistance = bridgeSqrDistance
        };
        if (!TryGetDistanceToTargetOnCurrentRail(
                currentNode.Sample,
                bridgeSample,
                currentNode.TravelDirection,
                out float distanceToBridge))
        {
            return false;
        }

        float bridgePathDistance = currentNode.PathDistance + distanceToBridge;
        float targetPathDistance = bridgePathDistance + ResolveRailTransitionMovementDistance(
            bridgeSample,
            targetSample);
        if (targetPathDistance - pathStartDistance
            > maxSearchDistance + ConsistPathSampleDistanceEpsilon)
        {
            return false;
        }

        int parentIndex = currentIndex;
        if (distanceToBridge > ConsistPathSampleDistanceEpsilon)
        {
            parentIndex = AddInitialConsistPathRouteNode(
                bridgeSample,
                currentNode.TravelDirection,
                currentIndex,
                bridgePathDistance);
        }

        Vector2 targetTravelDirection = ResolveFacingTangentWithFallback(
            targetSample.Tangent,
            currentNode.TravelDirection,
            currentNode.TravelDirection);
        if (HasInitialConsistPathRouteNode(targetSample, targetTravelDirection))
        {
            return false;
        }

        targetIndex = AddInitialConsistPathRouteNode(
            targetSample,
            targetTravelDirection,
            parentIndex,
            targetPathDistance);
        return true;
    }

    private static bool AreInternalConsistBridgeTangentsAligned(
        Vector2 bridgeTangent,
        Vector2 targetTangent,
        Vector2 travelDirection)
    {
        if (bridgeTangent.sqrMagnitude <= 0.0001f
            || targetTangent.sqrMagnitude <= 0.0001f
            || travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector2 normalizedTravelDirection = travelDirection.normalized;
        Vector2 alignedBridgeTangent = bridgeTangent.normalized;
        if (Vector2.Dot(alignedBridgeTangent, normalizedTravelDirection) < 0f)
        {
            alignedBridgeTangent = -alignedBridgeTangent;
        }

        Vector2 alignedTargetTangent = targetTangent.normalized;
        if (Vector2.Dot(alignedTargetTangent, normalizedTravelDirection) < 0f)
        {
            alignedTargetTangent = -alignedTargetTangent;
        }

        return Vector2.Dot(alignedBridgeTangent, alignedTargetTangent)
               >= BranchInternalOverlapMinTangentDot;
    }

    private void TryEnqueueInitialConsistPathEndpointRouteNode(
        int currentIndex,
        InitialConsistPathRouteNode currentNode,
        float pathLength,
        float pathStartDistance,
        float maxSearchDistance)
    {
        float travelDot = Vector2.Dot(currentNode.TravelDirection, currentNode.Sample.Tangent);
        if (Mathf.Abs(travelDot) <= 0.0001f)
        {
            return;
        }

        float directionSign = Mathf.Sign(travelDot);
        float endpointDistance = directionSign > 0f ? pathLength : 0f;
        float endpointTravelDistance = directionSign > 0f
            ? pathLength - currentNode.Sample.DistanceAlongPath
            : currentNode.Sample.DistanceAlongPath;
        if (endpointTravelDistance <= ConsistPathSampleDistanceEpsilon
            || Mathf.Abs(currentNode.Sample.DistanceAlongPath - endpointDistance)
            <= ConsistPathSampleDistanceEpsilon
            || !TryCreateRailSampleAtDistance(
                currentNode.Sample.Rail,
                endpointDistance,
                out RailSample endpointSample))
        {
            return;
        }

        Vector2 endpointTravelDirection = directionSign > 0f
            ? endpointSample.Tangent
            : -endpointSample.Tangent;
        if (endpointTravelDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        endpointTravelDirection.Normalize();
        float endpointPathDistance = currentNode.PathDistance + Mathf.Max(0f, endpointTravelDistance);
        if (endpointPathDistance - pathStartDistance
            > maxSearchDistance + ConsistPathSampleDistanceEpsilon
            || HasInitialConsistPathRouteNode(endpointSample, endpointTravelDirection))
        {
            return;
        }

        int endpointIndex = AddInitialConsistPathRouteNode(
            endpointSample,
            endpointTravelDirection,
            currentIndex,
            endpointPathDistance);
        initialConsistPathRouteQueue.Enqueue(endpointIndex);
    }

    private void TryEnqueueInitialConsistPathConnectionRouteNodes(
        int currentIndex,
        InitialConsistPathRouteNode currentNode,
        float pathLength,
        float pathStartDistance,
        float maxSearchDistance)
    {
        bool atEndEndpoint = Mathf.Abs(currentNode.Sample.DistanceAlongPath - pathLength)
                             <= ConsistPathSampleDistanceEpsilon;
        bool atStartEndpoint = currentNode.Sample.DistanceAlongPath <= ConsistPathSampleDistanceEpsilon;
        if (!atEndEndpoint && !atStartEndpoint)
        {
            return;
        }

        Vector2 exitDirection = currentNode.TravelDirection;
        if (exitDirection.sqrMagnitude <= 0.0001f
            || !TryCollectConnectedRailSamples(
                currentNode.Sample,
                exitDirection,
                currentNode.Sample.Rail,
                railConnectionSampleScratch,
                false))
        {
            return;
        }

        for (int i = 0; i < railConnectionSampleScratch.Count; i++)
        {
            RailSample connectedSample = railConnectionSampleScratch[i];
            Vector2 connectedTravelDirection = ResolveFacingTangentWithFallback(
                connectedSample.Tangent,
                exitDirection,
                exitDirection);
            float connectionPathDistance = ResolveRailTransitionMovementDistance(
                currentNode.Sample,
                connectedSample);
            float connectedPathDistance = currentNode.PathDistance + connectionPathDistance;
            if (connectedPathDistance - pathStartDistance
                > maxSearchDistance + ConsistPathSampleDistanceEpsilon
                || HasInitialConsistPathRouteNode(connectedSample, connectedTravelDirection))
            {
                continue;
            }

            int connectedIndex = AddInitialConsistPathRouteNode(
                connectedSample,
                connectedTravelDirection,
                currentIndex,
                connectedPathDistance);
            initialConsistPathRouteQueue.Enqueue(connectedIndex);
        }

        railConnectionSampleScratch.Clear();
    }

    private int AddInitialConsistPathRouteNode(
        RailSample sample,
        Vector2 travelDirection,
        int parentIndex,
        float pathDistance)
    {
        initialConsistPathRouteNodes.Add(new InitialConsistPathRouteNode
        {
            Sample = sample,
            TravelDirection = travelDirection.sqrMagnitude > 0.0001f
                ? travelDirection.normalized
                : Vector2.zero,
            ParentIndex = parentIndex,
            PathDistance = pathDistance
        });
        return initialConsistPathRouteNodes.Count - 1;
    }

    private bool HasInitialConsistPathRouteNode(RailSample sample, Vector2 travelDirection)
    {
        for (int i = 0; i < initialConsistPathRouteNodes.Count; i++)
        {
            InitialConsistPathRouteNode node = initialConsistPathRouteNodes[i];
            if (node.Sample.Rail == sample.Rail
                && Mathf.Abs(node.Sample.DistanceAlongPath - sample.DistanceAlongPath)
                    <= ConsistPathSampleDistanceEpsilon
                && (travelDirection.sqrMagnitude <= 0.0001f
                    || node.TravelDirection.sqrMagnitude <= 0.0001f
                    || Vector2.Dot(node.TravelDirection.normalized, travelDirection.normalized)
                    >= ConsistPathDirectionMinDot))
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildInitialConsistPathSamples(int targetIndex, List<ConsistPathSample> pathSamples)
    {
        int currentIndex = targetIndex;
        while (currentIndex >= 0 && currentIndex < initialConsistPathRouteNodes.Count)
        {
            InitialConsistPathRouteNode node = initialConsistPathRouteNodes[currentIndex];
            AddConsistPathSample(pathSamples, node.PathDistance, node.Sample);
            currentIndex = node.ParentIndex;
        }
    }

    private void ClearInitialConsistPathRouteScratch()
    {
        initialConsistPathRouteNodes.Clear();
        initialConsistPathRouteQueue.Clear();
        railConnectionSampleScratch.Clear();
    }

    private bool TryBuildInitialConsistPathSegmentInDirection(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        float pathStartDistance,
        List<ConsistPathSample> pathSamples,
        out float pathEndDistance)
    {
        pathEndDistance = pathStartDistance;
        if (backMove.StartSample.Rail == null
            || frontMove.StartSample.Rail == null
            || travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        RailSample currentSample = backMove.StartSample;
        Vector2 currentTravelDirection = travelDirection.normalized;
        float maxSearchDistance = ResolveInitialConsistSegmentSearchDistance(backMove, frontMove);
        AddConsistPathSample(pathSamples, pathStartDistance, currentSample);
        for (int hop = 0; hop < RailNetworkAdvanceMaxHops; hop++)
        {
            if (TryGetDistanceToTargetOnCurrentRail(
                    currentSample,
                    frontMove.StartSample,
                    currentTravelDirection,
                    out float distanceToFront))
            {
                if (pathEndDistance + distanceToFront - pathStartDistance
                    > maxSearchDistance + ConsistPathSampleDistanceEpsilon)
                {
                    return false;
                }

                pathEndDistance += distanceToFront;
                AddConsistPathSample(pathSamples, pathEndDistance, frontMove.StartSample);
                return true;
            }

            if (!currentSample.Rail.TryGetRenderedPathLength(out float pathLength))
            {
                return false;
            }

            float travelDot = Vector2.Dot(currentTravelDirection, currentSample.Tangent);
            if (Mathf.Abs(travelDot) <= 0.0001f)
            {
                travelDot = 1f;
            }

            float directionSign = Mathf.Sign(travelDot);
            float availableDistance = directionSign > 0f
                ? pathLength - currentSample.DistanceAlongPath
                : currentSample.DistanceAlongPath;
            float endpointDistance = directionSign > 0f ? pathLength : 0f;
            if (!TryCreateRailSampleAtDistance(
                    currentSample.Rail,
                    endpointDistance,
                    out RailSample endpointSample))
            {
                return false;
            }

            pathEndDistance += Mathf.Max(0f, availableDistance);
            if (pathEndDistance - pathStartDistance
                > maxSearchDistance + ConsistPathSampleDistanceEpsilon)
            {
                return false;
            }

            AddConsistPathSample(pathSamples, pathEndDistance, endpointSample);
            Vector2 exitDirection = directionSign > 0f ? endpointSample.Tangent : -endpointSample.Tangent;
            if (exitDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            exitDirection.Normalize();
            RailSample connectedSample = default;
            Railload preferredConnectedRail = frontMove.StartSample.Rail != currentSample.Rail
                ? frontMove.StartSample.Rail
                : null;
            if (!TryFindConnectedRailSample(
                    endpointSample,
                    exitDirection,
                    currentSample.Rail,
                    preferredConnectedRail,
                    out connectedSample))
            {
                return false;
            }

            pathEndDistance += ResolveRailTransitionMovementDistance(
                endpointSample,
                connectedSample);
            if (pathEndDistance - pathStartDistance
                > maxSearchDistance + ConsistPathSampleDistanceEpsilon)
            {
                return false;
            }

            currentSample = connectedSample;
            AddConsistPathSample(pathSamples, pathEndDistance, currentSample);
            currentTravelDirection = ResolveFacingTangent(currentSample.Tangent, exitDirection);
        }

        return false;
    }

    private static bool TryResolveInitialConsistSegmentDirection(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 fallbackDirection,
        out Vector2 travelDirection)
    {
        travelDirection = Vector2.zero;
        Vector2 backTangent = backMove.StartSample.Tangent;
        if (backTangent.sqrMagnitude > 0.0001f)
        {
            backTangent.Normalize();
            if (backMove.StartSample.Rail == frontMove.StartSample.Rail)
            {
                float distanceDelta = frontMove.StartSample.DistanceAlongPath
                                      - backMove.StartSample.DistanceAlongPath;
                if (Mathf.Abs(distanceDelta) > ConsistPathSampleDistanceEpsilon)
                {
                    travelDirection = backTangent * Mathf.Sign(distanceDelta);
                    return true;
                }
            }

            Vector2 toFront = frontMove.StartSample.Point - backMove.StartSample.Point;
            travelDirection = ResolveFacingTangentWithFallback(
                backTangent,
                toFront,
                fallbackDirection);
            return travelDirection.sqrMagnitude > 0.0001f;
        }

        Vector2 pointDelta = frontMove.StartSample.Point - backMove.StartSample.Point;
        if (pointDelta.sqrMagnitude > 0.0001f)
        {
            travelDirection = pointDelta.normalized;
            return true;
        }

        if (fallbackDirection.sqrMagnitude > 0.0001f)
        {
            travelDirection = fallbackDirection.normalized;
            return true;
        }

        return false;
    }

    private float ResolveRailTransitionPathDistance(
        RailSample fromSample,
        RailSample toSample)
    {
        if (fromSample.Rail == null || toSample.Rail == null)
        {
            return 0f;
        }

        if (fromSample.Rail == toSample.Rail)
        {
            return Mathf.Abs(toSample.DistanceAlongPath - fromSample.DistanceAlongPath);
        }

        return Vector2.Distance(fromSample.Point, toSample.Point);
    }

    protected virtual float ResolveRailTransitionMovementDistance(
        RailSample fromSample,
        RailSample toSample)
    {
        return ResolveRailTransitionPathDistance(fromSample, toSample);
    }

    private bool TryEstimateForwardRailGapDistance(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 fallbackDirection,
        out float gapDistance)
    {
        gapDistance = 0f;
        if (backMove.StartSample.Rail == null
            || frontMove.StartSample.Rail == null
            || fallbackDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        initialConsistSegmentScratch.Clear();
        if (TryBuildInitialConsistPathSegmentInDirection(
                backMove,
                frontMove,
                fallbackDirection.normalized,
                0f,
                initialConsistSegmentScratch,
                out gapDistance))
        {
            initialConsistSegmentScratch.Clear();
            return true;
        }

        initialConsistSegmentScratch.Clear();
        if (TryBuildInitialConsistPathSegmentWithRouteSearch(
                backMove,
                frontMove,
                fallbackDirection.normalized,
                0f,
                initialConsistSegmentScratch,
                out gapDistance))
        {
            initialConsistSegmentScratch.Clear();
            return true;
        }

        initialConsistSegmentScratch.Clear();
        gapDistance = 0f;
        return false;
    }

    private bool TryGetDistanceToTargetOnCurrentRail(
        RailSample currentSample,
        RailSample targetSample,
        Vector2 travelDirection,
        out float distance)
    {
        distance = 0f;
        if (currentSample.Rail == null
            || currentSample.Rail != targetSample.Rail
            || travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector2 pathTangent = ResolveRailPathTangent(currentSample);
        float travelDot = pathTangent.sqrMagnitude > 0.0001f
            ? Vector2.Dot(travelDirection.normalized, pathTangent)
            : 0f;
        if (Mathf.Abs(travelDot) <= 0.0001f)
        {
            travelDot = Vector2.Dot(targetSample.Point - currentSample.Point, travelDirection.normalized);
        }

        float directionSign = Mathf.Abs(travelDot) <= 0.0001f
            ? 1f
            : Mathf.Sign(travelDot);
        float signedDistance = directionSign > 0f
            ? targetSample.DistanceAlongPath - currentSample.DistanceAlongPath
            : currentSample.DistanceAlongPath - targetSample.DistanceAlongPath;
        if (signedDistance < -ConsistPathSampleDistanceEpsilon)
        {
            return false;
        }

        distance = Mathf.Max(0f, signedDistance);
        return true;
    }

    private float ResolveInitialConsistSegmentSearchDistance(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove)
    {
        float estimatedDistance = EstimateConsistSampleDistance(backMove, frontMove);
        estimatedDistance = Mathf.Max(
            estimatedDistance,
            ResolveDesiredConsistPairSpacing(backMove, frontMove));

        float padding = Mathf.Max(
            2f,
            Mathf.Max(railSearchRadius * 4f, branchSwitchLookAhead * 4f));
        return Mathf.Max(0.05f, estimatedDistance) + padding;
    }

    private bool EnsureConsistPathTape(Vector2 travelDirection)
    {
        if (!IsConsistPathTapeValid(travelDirection))
        {
            return InitializeConsistPathTape(travelDirection);
        }

        RailSample leaderStartSample = connectedTrainRailMoveScratch[0].StartSample;
        if (consistPathTape.Count <= 0)
        {
            return InitializeConsistPathTape(travelDirection);
        }

        ConsistPathSample lastSample = consistPathTape[consistPathTape.Count - 1];
        if (IsSameRailPosition(lastSample.Sample, leaderStartSample))
        {
            return true;
        }

        float maxSnapDistance = Mathf.Max(railSnapMaxDistance, ResolveRailConnectionMaxDistance());
        if ((lastSample.Sample.Point - leaderStartSample.Point).sqrMagnitude > maxSnapDistance * maxSnapDistance)
        {
            return InitializeConsistPathTape(travelDirection);
        }

        AddConsistPathSample(consistPathTape, consistPathEndDistance, leaderStartSample);
        return true;
    }

    private bool IsConsistPathTapeValid(Vector2 travelDirection)
    {
        if (consistPathTape.Count <= 0
            || connectedTrainRailMoveScratch.Count <= 0
            || consistPathLeader != connectedTrainRailMoveScratch[0].Train
            || consistPathTrainOrder.Count != connectedTrainRailMoveScratch.Count
            || consistPathFollowOffsets.Count != connectedTrainRailMoveScratch.Count)
        {
            return false;
        }

        if (travelDirection.sqrMagnitude <= 0.0001f
            || consistPathTravelDirection.sqrMagnitude <= 0.0001f
            || Vector2.Dot(consistPathTravelDirection.normalized, travelDirection.normalized)
                < ConsistPathDirectionMinDot)
        {
            return false;
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            if (consistPathTrainOrder[i] != connectedTrainRailMoveScratch[i].Train)
            {
                return false;
            }
        }

        return true;
    }

    private bool InitializeConsistPathTape(Vector2 travelDirection)
    {
        if (!TryApplyActualConnectedTrainFollowOffsets(
                travelDirection,
                true,
                out float pathEndDistance))
        {
            return false;
        }

        if (!IsConsistPathAlignedWithPreparedTrainSamples(pathEndDistance))
        {
            ResetConsistPathTape();
            return false;
        }

        RememberConsistPathOrder(travelDirection);
        return true;
    }

    private bool TryLockConsistToRouteLeaderPath(Vector2 routeLeaderTravelDirection)
    {
        if (connectedTrainRailMoveScratch.Count <= 0
            || routeLeaderTravelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        routeLeaderTravelDirection.Normalize();
        if (!TryApplyActualConnectedTrainFollowOffsets(
                routeLeaderTravelDirection,
                false,
                out _))
        {
            return false;
        }

        ConnectedTrainRailMove leaderMove = connectedTrainRailMoveScratch[0];
        if (leaderMove.StartSample.Rail == null)
        {
            return false;
        }

        float maxFollowOffset = 0f;
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            maxFollowOffset = Mathf.Max(maxFollowOffset, connectedTrainRailMoveScratch[i].FollowOffset);
        }

        ResetConsistPathTape();
        if (maxFollowOffset <= ConsistPathSampleDistanceEpsilon)
        {
            AddConsistPathSample(consistPathTape, 0f, leaderMove.StartSample);
            consistPathEndDistance = 0f;
            RememberConsistPathOrder(routeLeaderTravelDirection);
            return true;
        }

        leaderPathFrameScratch.Clear();
        bool advancedBackToTail = TryAdvanceAlongRailNetwork(
            leaderMove.StartSample,
            -routeLeaderTravelDirection,
            maxFollowOffset,
            out _,
            out float reversedPathDistance,
            leaderPathFrameScratch,
            0f);
        if (!advancedBackToTail
            || reversedPathDistance + ConsistPathSampleDistanceEpsilon < maxFollowOffset)
        {
            ResetConsistPathTape();
            leaderPathFrameScratch.Clear();
            return false;
        }

        for (int i = leaderPathFrameScratch.Count - 1; i >= 0; i--)
        {
            ConsistPathSample reverseSample = leaderPathFrameScratch[i];
            AddConsistPathSample(
                consistPathTape,
                reversedPathDistance - reverseSample.Distance,
                reverseSample.Sample);
        }

        leaderPathFrameScratch.Clear();
        consistPathEndDistance = reversedPathDistance;
        if (consistPathTape.Count <= 0)
        {
            ResetConsistPathTape();
            return false;
        }

        if (!IsConsistPathAlignedWithPreparedTrainSamples(consistPathEndDistance))
        {
            ResetConsistPathTape();
            return false;
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            ConnectedTrainRailMove railMove = connectedTrainRailMoveScratch[i];
            float targetPathDistance = consistPathEndDistance - railMove.FollowOffset;
            if (!TrySampleConsistPathTape(targetPathDistance, out RailSample routeSample, false))
            {
                ResetConsistPathTape();
                return false;
            }

            railMove.StartSample = routeSample;
            railMove.TargetSample = routeSample;
            railMove.StartFacingTangent = ResolveFollowerFacingTangent(
                routeSample.Tangent,
                railMove.StartFacingTangent,
                routeLeaderTravelDirection);
            connectedTrainRailMoveScratch[i] = railMove;
        }

        RememberConsistPathOrder(routeLeaderTravelDirection);
        return true;
    }

    private bool IsConsistPathAlignedWithPreparedTrainSamples(float pathEndDistance)
    {
        if (consistPathTape.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            ConnectedTrainRailMove railMove = connectedTrainRailMoveScratch[i];
            if (railMove.Train == null || railMove.StartSample.Rail == null)
            {
                return false;
            }

            float targetPathDistance = pathEndDistance - railMove.FollowOffset;
            if (!TrySampleConsistPathTape(targetPathDistance, out RailSample routeSample, false)
                || !IsConsistPathSampleAlignedWithTrainStart(routeSample, railMove))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsConsistPathSampleAlignedWithTrainStart(
        RailSample routeSample,
        ConnectedTrainRailMove railMove)
    {
        if (routeSample.Rail == null || railMove.StartSample.Rail == null)
        {
            return false;
        }

        float maxDistance = ResolveConsistPathSampleRetainDistance(railMove.Train);
        if ((routeSample.Point - railMove.StartSample.Point).sqrMagnitude > maxDistance * maxDistance)
        {
            return false;
        }

        if (routeSample.Rail == railMove.StartSample.Rail)
        {
            return true;
        }

        if (routeSample.Tangent.sqrMagnitude <= 0.0001f
            || railMove.StartSample.Tangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float tangentDot = Mathf.Abs(Vector2.Dot(
            routeSample.Tangent.normalized,
            railMove.StartSample.Tangent.normalized));
        return tangentDot >= BranchInternalOverlapMinTangentDot;
    }

    private float ResolveConsistPathSampleRetainDistance(Train train)
    {
        float trainSnapDistance = train != null ? train.ConnectionSnapMaxDistance : 0f;
        return Mathf.Max(
            railSnapMaxDistance,
            trainSnapDistance,
            ResolveRailConnectionMaxDistance()) + PushConsistGapTolerance;
    }

    private bool TryApplyActualConnectedTrainFollowOffsets(
        Vector2 travelDirection,
        bool rebuildConsistPathTape,
        out float leaderPathDistance,
        float maxFollowOffsetChange = float.PositiveInfinity)
    {
        leaderPathDistance = 0f;
        if (connectedTrainRailMoveScratch.Count <= 0 || travelDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        if (rebuildConsistPathTape)
        {
            consistPathTape.Clear();
            consistPathEndDistance = 0f;
        }

        actualConsistDistanceScratch.Clear();
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            actualConsistDistanceScratch.Add(0f);
        }

        int tailIndex = connectedTrainRailMoveScratch.Count - 1;
        ConnectedTrainRailMove tailMove = connectedTrainRailMoveScratch[tailIndex];
        if (tailMove.StartSample.Rail == null)
        {
            actualConsistDistanceScratch.Clear();
            return false;
        }

        if (rebuildConsistPathTape)
        {
            AddConsistPathSample(consistPathTape, 0f, tailMove.StartSample);
        }

        float currentPathDistance = 0f;
        for (int i = tailIndex; i > 0; i--)
        {
            ConnectedTrainRailMove backMove = connectedTrainRailMoveScratch[i];
            ConnectedTrainRailMove frontMove = connectedTrainRailMoveScratch[i - 1];
            if (!TryBuildActualConsistPathSegment(
                    backMove,
                    frontMove,
                    travelDirection,
                    currentPathDistance,
                    rebuildConsistPathTape ? consistPathTape : null,
                    out currentPathDistance))
            {
                if (rebuildConsistPathTape)
                {
                    consistPathTape.Clear();
                    consistPathEndDistance = 0f;
                }

                actualConsistDistanceScratch.Clear();
                return false;
            }

            actualConsistDistanceScratch[i - 1] = currentPathDistance;
        }

        leaderPathDistance = currentPathDistance;
        if (rebuildConsistPathTape)
        {
            consistPathEndDistance = leaderPathDistance;
        }

        float desiredFollowOffset = 0f;
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            ConnectedTrainRailMove railMove = connectedTrainRailMoveScratch[i];
            if (i > 0)
            {
                desiredFollowOffset += ResolveDesiredConsistPairSpacing(
                    connectedTrainRailMoveScratch[i - 1],
                    railMove);
            }

            float actualFollowOffset = Mathf.Max(
                0f,
                leaderPathDistance - actualConsistDistanceScratch[i]);
            float targetFollowOffset = Mathf.Max(0f, desiredFollowOffset);
            if (i == 0)
            {
                railMove.FollowOffset = 0f;
            }
            else if (rebuildConsistPathTape
                     || float.IsInfinity(maxFollowOffsetChange)
                     || float.IsNaN(maxFollowOffsetChange)
                     || Mathf.Abs(railMove.FollowOffset - targetFollowOffset) <= maxFollowOffsetChange
                     || railMove.FollowOffset <= ConsistPathSampleDistanceEpsilon)
            {
                railMove.FollowOffset = targetFollowOffset;
            }
            else
            {
                railMove.FollowOffset = Mathf.Max(
                    0f,
                    Mathf.MoveTowards(
                        railMove.FollowOffset,
                        targetFollowOffset,
                        Mathf.Max(0f, maxFollowOffsetChange)));
            }

            connectedTrainRailMoveScratch[i] = railMove;
        }

        actualConsistDistanceScratch.Clear();
        return true;
    }

    private bool TryBuildActualConsistPathSegment(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        float pathStartDistance,
        List<ConsistPathSample> targetPathSamples,
        out float pathEndDistance)
    {
        pathEndDistance = pathStartDistance;
        if (backMove.StartSample.Rail == null
            || frontMove.StartSample.Rail == null
            || !TryResolveInitialConsistSegmentDirection(
                backMove,
                frontMove,
                travelDirection,
                out Vector2 initialTravelDirection))
        {
            return false;
        }

        if (TryBuildActualConsistPathSegmentInDirection(
                backMove,
                frontMove,
                initialTravelDirection,
                pathStartDistance,
                targetPathSamples,
                out pathEndDistance))
        {
            return true;
        }

        if (TryBuildActualConsistPathSegmentInDirection(
                backMove,
                frontMove,
                -initialTravelDirection,
                pathStartDistance,
                targetPathSamples,
                out pathEndDistance))
        {
            return true;
        }

        pathEndDistance = pathStartDistance;
        return false;
    }

    private bool TryBuildActualConsistPathSegmentInDirection(
        ConnectedTrainRailMove backMove,
        ConnectedTrainRailMove frontMove,
        Vector2 travelDirection,
        float pathStartDistance,
        List<ConsistPathSample> targetPathSamples,
        out float pathEndDistance)
    {
        initialConsistSegmentScratch.Clear();
        bool built = TryBuildInitialConsistPathSegmentInDirection(
                         backMove,
                         frontMove,
                         travelDirection,
                         pathStartDistance,
                         initialConsistSegmentScratch,
                         out pathEndDistance)
                     || TryBuildInitialConsistPathSegmentWithRouteSearch(
                         backMove,
                         frontMove,
                         travelDirection,
                         pathStartDistance,
                         initialConsistSegmentScratch,
                         out pathEndDistance);
        if (built && targetPathSamples != null)
        {
            for (int i = 0; i < initialConsistSegmentScratch.Count; i++)
            {
                ConsistPathSample pathSample = initialConsistSegmentScratch[i];
                AddConsistPathSample(targetPathSamples, pathSample.Distance, pathSample.Sample);
            }
        }

        initialConsistSegmentScratch.Clear();
        return built;
    }

    private static float ResolveDesiredConsistPairSpacing(
        ConnectedTrainRailMove first,
        ConnectedTrainRailMove second)
    {
        if (first.Train != null && second.Train != null)
        {
            return ResolveDesiredConsistPairSpacing(first.Train, second.Train);
        }

        return Mathf.Max(0.05f, EstimateConsistSampleDistance(first, second));
    }

    private static float ResolveDesiredConsistPairSpacing(Train first, Train second)
    {
        if (first == null || second == null)
        {
            return 0.05f;
        }

        return Mathf.Max(
            0.05f,
            (first.ConnectionCenterDistance + second.ConnectionCenterDistance) * 0.5f);
    }

    private void RememberConsistPathOrder(Vector2 travelDirection)
    {
        consistPathLeader = connectedTrainRailMoveScratch.Count > 0
            ? connectedTrainRailMoveScratch[0].Train
            : null;
        consistPathTravelDirection = travelDirection.sqrMagnitude > 0.0001f
            ? travelDirection.normalized
            : Vector2.zero;
        consistPathTrainOrder.Clear();
        consistPathFollowOffsets.Clear();
        for (int i = 0; i < connectedTrainRailMoveScratch.Count; i++)
        {
            consistPathTrainOrder.Add(connectedTrainRailMoveScratch[i].Train);
            consistPathFollowOffsets.Add(connectedTrainRailMoveScratch[i].FollowOffset);
        }
    }

    private void ResetConsistPathTape()
    {
        consistPathTape.Clear();
        consistPathTrainOrder.Clear();
        consistPathFollowOffsets.Clear();
        leaderPathFrameScratch.Clear();
        initialConsistSegmentScratch.Clear();
        consistPathLeader = null;
        consistPathTravelDirection = Vector2.zero;
        consistPathEndDistance = 0f;
    }

    private void RestoreConsistPathTape(
        int sampleCount,
        ConsistPathSample lastSample,
        float endDistance)
    {
        sampleCount = Mathf.Clamp(sampleCount, 0, consistPathTape.Count);
        if (consistPathTape.Count > sampleCount)
        {
            consistPathTape.RemoveRange(sampleCount, consistPathTape.Count - sampleCount);
        }

        if (sampleCount > 0 && consistPathTape.Count == sampleCount)
        {
            consistPathTape[sampleCount - 1] = lastSample;
        }

        consistPathEndDistance = endDistance;
    }

    private void AppendConsistPathFrame(List<ConsistPathSample> frameSamples)
    {
        if (frameSamples == null || frameSamples.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < frameSamples.Count; i++)
        {
            AddConsistPathSample(consistPathTape, frameSamples[i].Distance, frameSamples[i].Sample);
        }

        if (consistPathTape.Count > 0)
        {
            consistPathEndDistance = Mathf.Max(
                consistPathEndDistance,
                consistPathTape[consistPathTape.Count - 1].Distance);
        }
    }

    private void AddConsistPathSample(
        List<ConsistPathSample> pathSamples,
        float distance,
        RailSample sample)
    {
        if (pathSamples == null || sample.Rail == null)
        {
            return;
        }

        ConsistPathSample pathSample = new ConsistPathSample
        {
            Sample = sample,
            Distance = distance
        };
        if (pathSamples.Count <= 0)
        {
            pathSamples.Add(pathSample);
            return;
        }

        int lastIndex = pathSamples.Count - 1;
        ConsistPathSample lastSample = pathSamples[lastIndex];
        if (Mathf.Abs(lastSample.Distance - distance) <= ConsistPathSampleDistanceEpsilon
            && IsSameRailPosition(lastSample.Sample, sample))
        {
            pathSamples[lastIndex] = pathSample;
            return;
        }

        if (distance >= lastSample.Distance - ConsistPathSampleDistanceEpsilon)
        {
            pathSamples.Add(pathSample);
            return;
        }

        for (int i = 0; i < pathSamples.Count; i++)
        {
            if (distance < pathSamples[i].Distance - ConsistPathSampleDistanceEpsilon)
            {
                pathSamples.Insert(i, pathSample);
                return;
            }
        }

        pathSamples.Add(pathSample);
    }

    private bool TrySampleConsistPathTape(
        float targetDistance,
        out RailSample sample,
        bool clampToExtents = true)
    {
        sample = default;
        if (consistPathTape.Count <= 0)
        {
            return false;
        }

        for (int i = consistPathTape.Count - 1; i >= 0; i--)
        {
            if (Mathf.Abs(consistPathTape[i].Distance - targetDistance)
                <= ConsistPathSampleDistanceEpsilon)
            {
                sample = consistPathTape[i].Sample;
                return true;
            }
        }

        if (targetDistance <= consistPathTape[0].Distance)
        {
            if (!clampToExtents
                && targetDistance < consistPathTape[0].Distance - ConsistPathSampleDistanceEpsilon)
            {
                return false;
            }

            sample = consistPathTape[0].Sample;
            return true;
        }

        int lastIndex = consistPathTape.Count - 1;
        if (targetDistance >= consistPathTape[lastIndex].Distance)
        {
            if (!clampToExtents
                && targetDistance > consistPathTape[lastIndex].Distance + ConsistPathSampleDistanceEpsilon)
            {
                return false;
            }

            sample = consistPathTape[lastIndex].Sample;
            return true;
        }

        for (int i = 0; i + 1 < consistPathTape.Count; i++)
        {
            ConsistPathSample startSample = consistPathTape[i];
            ConsistPathSample endSample = consistPathTape[i + 1];
            if (targetDistance < startSample.Distance - ConsistPathSampleDistanceEpsilon
                || targetDistance > endSample.Distance + ConsistPathSampleDistanceEpsilon)
            {
                continue;
            }

            if (TrySampleBetweenConsistPathSamples(
                    startSample,
                    endSample,
                    targetDistance,
                    out sample))
            {
                return true;
            }
        }

        return false;
    }

    private bool TrySampleBetweenConsistPathSamples(
        ConsistPathSample startSample,
        ConsistPathSample endSample,
        float targetDistance,
        out RailSample sample)
    {
        sample = default;
        float segmentDistance = endSample.Distance - startSample.Distance;
        if (segmentDistance <= ConsistPathSampleDistanceEpsilon)
        {
            sample = endSample.Sample;
            return sample.Rail != null;
        }

        float t = Mathf.Clamp01((targetDistance - startSample.Distance) / segmentDistance);
        if (startSample.Sample.Rail != null
            && startSample.Sample.Rail == endSample.Sample.Rail)
        {
            if (IsRailConnectionBridgeSample(startSample.Sample)
                || IsRailConnectionBridgeSample(endSample.Sample))
            {
                return TryCreateLinearRailSample(
                    startSample.Sample,
                    endSample.Sample,
                    t,
                    out sample);
            }

            float railDistance = Mathf.Lerp(
                startSample.Sample.DistanceAlongPath,
                endSample.Sample.DistanceAlongPath,
                t);
            return TryCreateRailSampleAtDistance(
                startSample.Sample.Rail,
                railDistance,
                out sample);
        }

        float connectionPathDistance = ResolveRailTransitionMovementDistance(
            startSample.Sample,
            endSample.Sample);
        if (connectionPathDistance > ResolveRailConnectionMaxDistance() + ConsistPathSampleDistanceEpsilon)
        {
            return false;
        }

        return TryCreateRailConnectionBridgeSample(
            startSample.Sample,
            endSample.Sample,
            Mathf.Max(connectionPathDistance, ConsistPathSampleDistanceEpsilon),
            connectionPathDistance * t,
            out sample);
    }

    private static bool TryCreateLinearRailSample(
        RailSample startSample,
        RailSample endSample,
        float t,
        out RailSample sample)
    {
        sample = default;
        Railload rail = t >= 1f - ConsistPathSampleDistanceEpsilon
            ? endSample.Rail
            : startSample.Rail;
        if (rail == null)
        {
            return false;
        }

        Vector2 point = Vector2.Lerp(startSample.Point, endSample.Point, t);
        Vector2 tangent = endSample.Point - startSample.Point;
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            tangent = Vector2.Lerp(startSample.Tangent, endSample.Tangent, t);
        }

        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        tangent.Normalize();
        sample.Rail = rail;
        sample.DistanceAlongPath = Mathf.Lerp(
            startSample.DistanceAlongPath,
            endSample.DistanceAlongPath,
            t);
        sample.Point = point;
        sample.Tangent = tangent;
        sample.SqrDistance = 0f;
        return true;
    }

    private void TrimConsistPathTape(float minDistanceToKeep)
    {
        while (consistPathTape.Count > 2
               && consistPathTape[1].Distance < minDistanceToKeep)
        {
            consistPathTape.RemoveAt(0);
        }
    }

    private float ResolveConsistPathTrimPadding()
    {
        return Mathf.Max(
            ConnectionCenterDistance * 2f,
            branchSwitchLookAhead + railSnapMaxDistance);
    }

    private static bool IsSameRailPosition(RailSample first, RailSample second)
    {
        return first.Rail != null
               && first.Rail == second.Rail
               && Mathf.Abs(first.DistanceAlongPath - second.DistanceAlongPath)
                   <= ConsistPathSampleDistanceEpsilon;
    }

    private void CollectConnectedTrainGroupForMovement()
    {
        CollectConnectedTrainGroupForMovement(this);
    }

    private void CollectConnectedTrainGroupForMovement(Train startTrain)
    {
        connectedTrainGroupScratch.Clear();
        connectedTrainGroupVisited.Clear();
        connectedTrainGroupQueue.Clear();
        if (startTrain == null)
        {
            return;
        }

        connectedTrainGroupQueue.Enqueue(startTrain);
        connectedTrainGroupVisited.Add(startTrain);

        while (connectedTrainGroupQueue.Count > 0)
        {
            Train currentTrain = connectedTrainGroupQueue.Dequeue();
            if (currentTrain == null || !currentTrain.gameObject.activeInHierarchy)
            {
                continue;
            }

            connectedTrainGroupScratch.Add(currentTrain);
            foreach (Train connectedTrain in currentTrain.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !connectedTrainGroupVisited.Add(connectedTrain))
                {
                    continue;
                }

                connectedTrainGroupQueue.Enqueue(connectedTrain);
            }
        }
    }

    private bool TryResolveRailSampleForTrain(
        Train train,
        Vector2 preferredDirection,
        float maxSqrDistance,
        out RailSample sample,
        out Vector2 facingTangent)
    {
        sample = default;
        facingTangent = Vector2.zero;
        if (train == null)
        {
            return false;
        }

        Vector2 currentPoint = new Vector2(
            train.transform.position.x,
            train.transform.position.z);
        if (train.TryGetCurrentRailSample(
                currentPoint,
                maxSqrDistance,
                out Railload rail,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent,
                out float sqrDistance))
        {
            Vector2 storedFacingTangent = tangent;
            if (!rail.TrySampleRenderedPath(distanceAlongPath, out _, out Vector2 sampledTangent))
            {
                return false;
            }

            sample.Rail = rail;
            sample.DistanceAlongPath = distanceAlongPath;
            sample.Point = pathPoint;
            sample.Tangent = tangent.sqrMagnitude > 0.0001f ? tangent : sampledTangent;
            sample.SqrDistance = sqrDistance;

            facingTangent = ResolveFollowerFacingTangent(
                sample.Tangent,
                storedFacingTangent,
                preferredDirection);
            return true;
        }

        if (TryFindBestRailSample(
                currentPoint,
                preferredDirection,
                maxSqrDistance,
                out sample))
        {
            Vector2 transformFacing = new Vector2(
                train.transform.forward.x,
                train.transform.forward.z);
            facingTangent = ResolveFollowerFacingTangent(
                sample.Tangent,
                transformFacing,
                preferredDirection);
            return true;
        }

        return false;
    }

    private static bool IsRailConnectionBridgeSample(RailSample sample)
    {
        return sample.Rail != null
               && sample.Rail.TrySampleRenderedPath(
                   sample.DistanceAlongPath,
                   out Vector2 pathPoint,
                   out _)
               && (sample.Point - pathPoint).sqrMagnitude
               > RailConnectionBridgePointTolerance * RailConnectionBridgePointTolerance;
    }

    private void ApplyConnectedTrainRailPose(
        Train train,
        RailSample sample,
        Vector2 facingTangent,
        float deltaTime)
    {
        if (train == null || sample.Rail == null)
        {
            return;
        }

        if (train is RailHandcar handcar)
        {
            handcar.ApplyRailPose(sample, facingTangent, deltaTime, true);
            return;
        }

        if (train is FreightCar freightCar)
        {
            freightCar.TryApplyRailPose(
                sample.Rail,
                sample.DistanceAlongPath,
                sample.Point,
                facingTangent,
                deltaTime,
                true,
                true);
            return;
        }

        train.TryApplyRailPose(
            sample.Rail,
            sample.DistanceAlongPath,
            sample.Point,
            facingTangent);
    }

    private void RotateConnectedTrainWheels(Train train, float signedWheelDistance)
    {
        if (Mathf.Abs(signedWheelDistance) <= 0.0001f || train == null)
        {
            return;
        }

        train.RotateTrainWheelsByDistance(signedWheelDistance);
    }

    private static float EstimateSignedRailSampleMoveDistance(
        Train train,
        RailSample startSample,
        RailSample targetSample)
    {
        float signedPathDistance = 0f;
        float distance;
        if (startSample.Rail != null
            && startSample.Rail == targetSample.Rail)
        {
            signedPathDistance = targetSample.DistanceAlongPath - startSample.DistanceAlongPath;
            distance = Mathf.Abs(signedPathDistance);
        }
        else
        {
            distance = Vector2.Distance(startSample.Point, targetSample.Point);
        }

        if (distance <= 0.0001f)
        {
            return 0f;
        }

        Vector2 moveDirection = targetSample.Point - startSample.Point;
        if (TryResolveSignedDistanceByReference(train, moveDirection, distance, out float signedDistance))
        {
            return signedDistance;
        }

        if (Mathf.Abs(signedPathDistance) > 0.0001f)
        {
            return distance * Mathf.Sign(signedPathDistance);
        }

        return distance;
    }

    private static bool TryResolveSignedDistanceByReference(
        Train train,
        Vector2 moveDirection,
        float distance,
        out float signedDistance)
    {
        signedDistance = 0f;
        if (distance <= 0.0001f || moveDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        moveDirection.Normalize();
        if (TryGetTrainVisualForward(train, out Vector2 visualForward)
            && TryResolveSignedDistanceByDirection(moveDirection, visualForward, distance, out signedDistance))
        {
            return true;
        }

        if (train != null
            && train.TryGetCurrentRailPose(out _, out _, out _, out Vector2 railTangent)
            && TryResolveSignedDistanceByDirection(moveDirection, railTangent, distance, out signedDistance))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetTrainVisualForward(Train train, out Vector2 visualForward)
    {
        visualForward = Vector2.zero;
        if (train == null)
        {
            return false;
        }

        visualForward = new Vector2(train.transform.forward.x, train.transform.forward.z);
        if (visualForward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        visualForward.Normalize();
        return true;
    }

    private static bool TryResolveSignedDistanceByDirection(
        Vector2 moveDirection,
        Vector2 referenceDirection,
        float distance,
        out float signedDistance)
    {
        signedDistance = 0f;
        if (moveDirection.sqrMagnitude <= 0.0001f
            || referenceDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float dot = Vector2.Dot(moveDirection.normalized, referenceDirection.normalized);
        if (Mathf.Abs(dot) <= 0.0001f)
        {
            return false;
        }

        signedDistance = distance * Mathf.Sign(dot);
        return true;
    }

    private void ClearConnectedTrainMovementScratch()
    {
        activeTrainScratch.Clear();
        connectedTrainGroupScratch.Clear();
        connectedTrainGroupVisited.Clear();
        connectedTrainGroupQueue.Clear();
        connectedTrainRailMoveScratch.Clear();
        connectedTrainOrderScratch.Clear();
        leaderPathFrameScratch.Clear();
    }

    private static bool TryCreateRailSampleAtDistance(
        Railload rail,
        float distanceAlongPath,
        out RailSample sample)
    {
        sample = default;
        if (rail == null
            || !rail.TrySampleRenderedPath(distanceAlongPath, out Vector2 pathPoint, out Vector2 tangent))
        {
            return false;
        }

        sample.Rail = rail;
        sample.DistanceAlongPath = distanceAlongPath;
        sample.Point = pathPoint;
        sample.Tangent = tangent;
        sample.SqrDistance = 0f;
        return true;
    }

    private bool TryFindBestRailSample(
        Vector2 point,
        Vector2 preferredDirection,
        float maxSqrDistance,
        out RailSample bestSample,
        Railload excludedRail = null)
    {
        bestSample = default;
        if (preferredDirection.sqrMagnitude > 0.0001f)
        {
            preferredDirection.Normalize();
        }

        CollectRailCandidates(point);
        bool found = false;
        float bestScore = float.MaxValue;
        for (int i = 0; i < railCandidateScratch.Count; i++)
        {
            Railload rail = railCandidateScratch[i];
            if (rail == null
                || rail == excludedRail
                || !rail.TryFindNearestRenderedPathSample(
                    point,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent,
                    out float sqrDistance)
                || sqrDistance > maxSqrDistance)
            {
                continue;
            }

            float directionScore = preferredDirection.sqrMagnitude > 0.0001f
                ? Mathf.Abs(Vector2.Dot(preferredDirection, tangent))
                : 0f;
            float score = sqrDistance - directionScore * 0.08f;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestSample.Rail = rail;
            bestSample.DistanceAlongPath = distanceAlongPath;
            bestSample.Point = pathPoint;
            bestSample.Tangent = tangent;
            bestSample.SqrDistance = sqrDistance;
            found = true;
        }

        railCandidateScratch.Clear();
        railSearchScratch.Clear();
        return found;
    }

    private bool TryFindBranchRailSample(
        RailSample currentSample,
        Vector2 inputDirection,
        out RailSample branchSample)
    {
        branchSample = default;
        if (currentSample.Rail == null || inputDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        inputDirection.Normalize();
        float maxBranchSnapDistance = Mathf.Min(branchSwitchMaxDistance, ResolveRailConnectionMaxDistance());
        float branchLookAheadDistance = ResolveBranchLookAheadDistance();

        float maxBranchSqrDistance = maxBranchSnapDistance * maxBranchSnapDistance;
        float currentInputDot = Mathf.Abs(Vector2.Dot(inputDirection, currentSample.Tangent));
        float currentProgress = ResolveRailLookAheadProgress(
            currentSample.Rail,
            currentSample.DistanceAlongPath,
            currentSample.Tangent,
            currentSample.Point,
            inputDirection,
            branchLookAheadDistance);
        float currentScore = ResolveBranchSelectionScore(currentInputDot, currentProgress, 0f);

        if (TryGetPreferredBranchRail(currentSample, inputDirection, out Railload preferredRail)
            && TryFindPreferredBranchRailSample(
                currentSample,
                inputDirection,
                preferredRail,
                maxBranchSqrDistance,
                branchLookAheadDistance,
                out RailSample preferredBranchSample))
        {
            branchSample = preferredBranchSample;
            railCandidateScratch.Clear();
            railSearchScratch.Clear();
            return true;
        }

        bool restrictBranchSelection = ShouldRestrictBranchRailSelection(currentSample, inputDirection);

        railCandidateScratch.Clear();
        AddRailCandidates(currentSample.Point);
        AddRailCandidates(currentSample.Point + inputDirection * branchSwitchLookAhead);

        bool found = false;
        float bestScore = float.MinValue;
        for (int i = 0; i < railCandidateScratch.Count; i++)
        {
            Railload rail = railCandidateScratch[i];
            if (rail == null
                || rail == currentSample.Rail
                || (restrictBranchSelection
                    && !ShouldAllowRestrictedBranchRailCandidate(
                        currentSample,
                        inputDirection,
                        rail))
                || !TryFindBranchRailSampleNearPoint(
                    rail,
                    currentSample,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent,
                    out float sqrDistance)
                || sqrDistance > maxBranchSqrDistance)
            {
                continue;
            }

            Vector2 alignedTangent = ResolveAlignedRailTangent(
                tangent,
                inputDirection,
                currentSample.Tangent);
            float inputDot = Mathf.Abs(Vector2.Dot(inputDirection, alignedTangent));
            if (inputDot < branchSwitchMinInputDot)
            {
                continue;
            }

            float progress = ResolveRailLookAheadProgress(
                rail,
                distanceAlongPath,
                alignedTangent,
                currentSample.Point,
                inputDirection,
                branchLookAheadDistance);
            if (progress <= BranchInternalProgressMin)
            {
                continue;
            }

            float score = ResolveBranchSelectionScore(inputDot, progress, sqrDistance);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            branchSample.Rail = rail;
            branchSample.DistanceAlongPath = distanceAlongPath;
            branchSample.Point = pathPoint;
            branchSample.Tangent = tangent;
            branchSample.SqrDistance = sqrDistance;
            found = true;
        }

        railCandidateScratch.Clear();
        railSearchScratch.Clear();
        return found
               && (restrictBranchSelection
                   || bestScore + BranchSwitchCurrentScoreTolerance >= currentScore);
    }

    protected virtual bool TryGetPreferredBranchRail(
        RailSample currentSample,
        Vector2 inputDirection,
        out Railload preferredRail)
    {
        preferredRail = null;
        return false;
    }

    protected virtual bool TryGetPreferredConnectedRail(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        out Railload preferredRail)
    {
        preferredRail = null;
        return false;
    }

    protected virtual bool ShouldRestrictBranchRailSelection(
        RailSample currentSample,
        Vector2 inputDirection)
    {
        return false;
    }

    protected virtual bool ShouldRestrictConnectedRailSelection(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail)
    {
        return false;
    }

    protected virtual bool ShouldAllowRestrictedBranchRailCandidate(
        RailSample currentSample,
        Vector2 inputDirection,
        Railload candidateRail)
    {
        return false;
    }

    protected virtual bool ShouldAllowRestrictedConnectedRailCandidate(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        RailSample candidateSample)
    {
        return false;
    }

    protected virtual bool ShouldAllowLowProgressConnectedRailCandidate(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        RailSample candidateSample)
    {
        return false;
    }

    protected virtual bool TryGetPreferredConnectedRailEntrySample(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        out RailSample connectedSample)
    {
        connectedSample = default;
        return false;
    }

    private bool TryFindPreferredBranchRailSample(
        RailSample currentSample,
        Vector2 inputDirection,
        Railload preferredRail,
        float maxBranchSqrDistance,
        float branchLookAheadDistance,
        out RailSample branchSample)
    {
        branchSample = default;
        if (preferredRail == null
            || preferredRail == currentSample.Rail
            || !TryFindBranchRailSampleNearPoint(
                preferredRail,
                currentSample,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent,
                out float sqrDistance)
            || sqrDistance > maxBranchSqrDistance)
        {
            return false;
        }

        Vector2 alignedTangent = ResolveAlignedRailTangent(
            tangent,
            inputDirection,
            currentSample.Tangent);
        if (alignedTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float progress = ResolveRailLookAheadProgress(
            preferredRail,
            distanceAlongPath,
            alignedTangent,
            currentSample.Point,
            alignedTangent,
            branchLookAheadDistance);
        if (progress <= BranchInternalProgressMin)
        {
            return false;
        }

        branchSample = new RailSample
        {
            Rail = preferredRail,
            DistanceAlongPath = distanceAlongPath,
            Point = pathPoint,
            Tangent = tangent,
            SqrDistance = sqrDistance
        };
        return true;
    }

    private float ResolveBranchLookAheadDistance()
    {
        return Mathf.Max(
            branchSwitchLookAhead,
            railConnectionLookAhead,
            railSearchRadius,
            0.05f)
               * BranchExtendedLookAheadMultiplier;
    }

    private static bool IsNearCurrentRailEndpoint(RailSample sample, float maxDistance)
    {
        if (sample.Rail == null || !sample.Rail.TryGetRenderedPathLength(out float pathLength))
        {
            return false;
        }

        float clampedDistance = Mathf.Clamp(sample.DistanceAlongPath, 0f, pathLength);
        float endpointDistance = Mathf.Min(clampedDistance, pathLength - clampedDistance);
        return endpointDistance <= Mathf.Max(0f, maxDistance);
    }

    private float ResolveRailLookAheadProgress(
        Railload rail,
        float distanceAlongPath,
        Vector2 tangent,
        Vector2 originPoint,
        Vector2 inputDirection,
        float lookAheadDistance)
    {
        if (rail == null || inputDirection.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        Vector2 normalizedInputDirection = inputDirection.normalized;
        Vector2 pathDistanceTangent = tangent;
        if (rail.TrySampleRenderedPath(
                Mathf.Max(0f, distanceAlongPath),
                out _,
                out Vector2 sampledPathTangent)
            && sampledPathTangent.sqrMagnitude > 0.0001f)
        {
            // Use the rail's stored path direction so reversed placements still advance
            // along the correct distance axis when we evaluate a branch candidate.
            pathDistanceTangent = sampledPathTangent;
        }

        if (pathDistanceTangent.sqrMagnitude <= 0.0001f
            || !rail.TryGetRenderedPathLength(out float pathLength))
        {
            return 0f;
        }

        float tangentDot = Vector2.Dot(normalizedInputDirection, pathDistanceTangent.normalized);
        if (Mathf.Abs(tangentDot) <= 0.0001f)
        {
            return 0f;
        }

        float targetDistance = Mathf.Clamp(
            distanceAlongPath + Mathf.Sign(tangentDot) * Mathf.Max(0.05f, lookAheadDistance),
            0f,
            pathLength);
        if (Mathf.Abs(targetDistance - distanceAlongPath) <= 0.0001f
            || !rail.TrySampleRenderedPath(targetDistance, out Vector2 futurePoint, out _))
        {
            return 0f;
        }

        return Mathf.Max(0f, Vector2.Dot(futurePoint - originPoint, normalizedInputDirection));
    }

    private static float ResolveBranchSelectionScore(float inputDot, float progress, float sqrDistance)
    {
        return inputDot * 1.4f + progress * 0.8f - sqrDistance * 2f;
    }

    private bool TryFindPreferredConnectedRailSample(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        Railload preferredRail,
        out RailSample connectedSample)
    {
        connectedSample = default;
        if (endpointSample.Rail == null
            || preferredRail == null
            || preferredRail == excludedRail
            || exitDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        exitDirection.Normalize();
        float maxConnectionDistance = ResolveRailConnectionMaxDistance();
        if (!TryFindRailConnectionSample(
                preferredRail,
                endpointSample.Point,
                true,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent,
                out float sqrDistance)
            || sqrDistance > maxConnectionDistance * maxConnectionDistance)
        {
            return false;
        }

        connectedSample.Rail = preferredRail;
        connectedSample.DistanceAlongPath = distanceAlongPath;
        connectedSample.Point = pathPoint;
        connectedSample.Tangent = tangent;
        connectedSample.SqrDistance = sqrDistance;
        return true;
    }

    private bool TryFindConnectedRailSample(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        out RailSample connectedSample)
    {
        if (TryGetPreferredConnectedRailEntrySample(
                endpointSample,
                exitDirection,
                excludedRail,
                out connectedSample))
        {
            return true;
        }

        Railload preferredRail = null;
        TryGetPreferredConnectedRail(
            endpointSample,
            exitDirection,
            excludedRail,
            out preferredRail);
        return TryFindConnectedRailSample(
            endpointSample,
            exitDirection,
            excludedRail,
            preferredRail,
            out connectedSample);
    }

    private bool TryFindConnectedRailSample(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        Railload preferredRail,
        out RailSample connectedSample)
    {
        connectedSample = default;
        if (preferredRail != null
            && TryFindPreferredConnectedRailSample(
                endpointSample,
                exitDirection,
                excludedRail,
                preferredRail,
                out connectedSample))
        {
            return true;
        }

        bool restrictConnectedRailSelection =
            ShouldRestrictConnectedRailSelection(endpointSample, exitDirection, excludedRail);

        if (!TryCollectConnectedRailSamples(
                endpointSample,
                exitDirection,
                excludedRail,
                railConnectionSampleScratch,
                true))
        {
            return false;
        }

        exitDirection.Normalize();
        bool found = false;
        float bestScore = float.MinValue;
        for (int i = 0; i < railConnectionSampleScratch.Count; i++)
        {
            RailSample candidateSample = railConnectionSampleScratch[i];
            if (restrictConnectedRailSelection
                && !ShouldAllowRestrictedConnectedRailCandidate(
                    endpointSample,
                    exitDirection,
                    excludedRail,
                    candidateSample))
            {
                continue;
            }

            float directionScore = Mathf.Abs(Vector2.Dot(exitDirection, candidateSample.Tangent));
            float progress = ResolveRailLookAheadProgress(
                candidateSample.Rail,
                candidateSample.DistanceAlongPath,
                candidateSample.Tangent,
                endpointSample.Point,
                exitDirection,
                railConnectionLookAhead);

            float score = progress * 1.2f + directionScore * 0.6f - candidateSample.SqrDistance * 3f;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            connectedSample = candidateSample;
            found = true;
        }

        railConnectionSampleScratch.Clear();
        return found;
    }

    private bool TryCollectConnectedRailSamples(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        List<RailSample> results,
        bool requireDirectionalMatch)
    {
        results.Clear();
        if (endpointSample.Rail == null || exitDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        exitDirection.Normalize();
        railCandidateScratch.Clear();
        AddRailCandidates(endpointSample.Point);
        AddRailCandidates(endpointSample.Point + exitDirection * railConnectionLookAhead);

        float maxConnectionDistance = ResolveRailConnectionMaxDistance();
        float maxConnectionSqrDistance = maxConnectionDistance * maxConnectionDistance;
        for (int i = 0; i < railCandidateScratch.Count; i++)
        {
            Railload rail = railCandidateScratch[i];
            if (rail == null
                || rail == excludedRail
                || !TryFindRailConnectionSample(
                    rail,
                    endpointSample.Point,
                    true,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent,
                    out float sqrDistance)
                || sqrDistance > maxConnectionSqrDistance)
            {
                continue;
            }

            Vector2 alignedTangent = ResolveAlignedRailTangent(
                tangent,
                exitDirection,
                endpointSample.Tangent);
            float directionScore = Mathf.Abs(Vector2.Dot(exitDirection, alignedTangent));
            float progress = ResolveRailLookAheadProgress(
                rail,
                distanceAlongPath,
                alignedTangent,
                endpointSample.Point,
                exitDirection,
                railConnectionLookAhead);
            RailSample candidateSample = new RailSample
            {
                Rail = rail,
                DistanceAlongPath = distanceAlongPath,
                Point = pathPoint,
                Tangent = tangent,
                SqrDistance = sqrDistance
            };
            if (requireDirectionalMatch
                && progress <= 0.01f
                && directionScore < 0.12f
                && !ShouldAllowLowProgressConnectedRailCandidate(
                    endpointSample,
                    exitDirection,
                    excludedRail,
                    candidateSample))
            {
                continue;
            }

            results.Add(candidateSample);
        }

        railCandidateScratch.Clear();
        railSearchScratch.Clear();
        return results.Count > 0;
    }

    private bool TryFindBranchRailSampleNearPoint(
        Railload rail,
        RailSample currentSample,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        distanceAlongPath = 0f;
        pathPoint = currentSample.Point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (rail == null || currentSample.Rail == null)
        {
            return false;
        }

        if (TryFindRailConnectionSample(
            rail,
            currentSample.Point,
            false,
            out distanceAlongPath,
            out pathPoint,
            out tangent,
            out sqrDistance))
        {
            return true;
        }

        return TryFindOverlappingBranchRailSample(
            rail,
            currentSample,
            out distanceAlongPath,
            out pathPoint,
            out tangent,
            out sqrDistance);
    }

    private bool TryFindOverlappingBranchRailSample(
        Railload rail,
        RailSample currentSample,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        distanceAlongPath = 0f;
        pathPoint = currentSample.Point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (rail == null
            || currentSample.Rail == null
            || currentSample.Tangent.sqrMagnitude <= 0.0001f
            || !rail.TryFindNearestRenderedPathSample(
                currentSample.Point,
                out distanceAlongPath,
                out pathPoint,
                out tangent,
                out sqrDistance)
            || tangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float tangentDot = Mathf.Abs(Vector2.Dot(currentSample.Tangent.normalized, tangent.normalized));
        return tangentDot >= BranchInternalOverlapMinTangentDot;
    }

    private bool TryFindRailConnectionSample(
        Railload rail,
        Vector2 point,
        bool allowInternalFallback,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        distanceAlongPath = 0f;
        pathPoint = point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (rail == null)
        {
            return false;
        }

        bool found = false;
        if (TryUpdateEndpointConnectionSample(rail, point, true, ref distanceAlongPath, ref pathPoint, ref tangent, ref sqrDistance))
        {
            found = true;
        }

        if (TryUpdateEndpointConnectionSample(rail, point, false, ref distanceAlongPath, ref pathPoint, ref tangent, ref sqrDistance))
        {
            found = true;
        }

        if (!allowInternalFallback)
        {
            return found;
        }

        float maxInternalConnectionDistance = ResolveInternalConnectionMaxDistance();
        if (rail.TryFindNearestRenderedPathSample(
                point,
                out float internalDistance,
                out Vector2 internalPoint,
                out Vector2 internalTangent,
                out float internalSqrDistance)
            && internalSqrDistance <= maxInternalConnectionDistance * maxInternalConnectionDistance
            && (!found || internalSqrDistance < sqrDistance))
        {
            distanceAlongPath = internalDistance;
            pathPoint = internalPoint;
            tangent = internalTangent;
            sqrDistance = internalSqrDistance;
            found = true;
        }

        return found;
    }

    protected virtual float ResolveRailConnectionMaxDistance()
    {
        return Mathf.Max(
            Mathf.Max(MinRailConnectionMaxDistance, railConnectionMaxDistance),
            railSnapMaxDistance * 0.2f);
    }

    private float ResolveInternalConnectionMaxDistance()
    {
        return Mathf.Max(MinInternalConnectionMaxDistance, internalConnectionMaxDistance);
    }

    private static bool TryUpdateEndpointConnectionSample(
        Railload rail,
        Vector2 point,
        bool startEndpoint,
        ref float bestDistanceAlongPath,
        ref Vector2 bestPathPoint,
        ref Vector2 bestTangent,
        ref float bestSqrDistance)
    {
        if (rail == null
            || !rail.TryGetRenderedEndpointSample(
                startEndpoint,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent))
        {
            return false;
        }

        float sqrDistance = (point - pathPoint).sqrMagnitude;
        if (sqrDistance >= bestSqrDistance)
        {
            return false;
        }

        bestDistanceAlongPath = distanceAlongPath;
        bestPathPoint = pathPoint;
        bestTangent = tangent;
        bestSqrDistance = sqrDistance;
        return true;
    }

    private void CollectRailCandidates(Vector2 point)
    {
        railCandidateScratch.Clear();
        AddRailCandidates(point);
    }

    private void AddRailCandidates(Vector2 point)
    {
        int searchCells = Mathf.CeilToInt(Mathf.Max(0.05f, railSearchRadius));
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(point.x),
            Mathf.RoundToInt(point.y));

        for (int offsetY = -searchCells; offsetY <= searchCells; offsetY++)
        {
            for (int offsetX = -searchCells; offsetX <= searchCells; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                railSearchScratch.Clear();
                InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                    coordinate,
                    railSearchScratch);

                for (int i = 0; i < railSearchScratch.Count; i++)
                {
                    if (railSearchScratch[i] is not Railload rail
                        || railCandidateScratch.Contains(rail))
                    {
                        continue;
                    }

                    railCandidateScratch.Add(rail);
                }
            }
        }
    }

    private Vector2 ResolveReferenceFacing()
    {
        if (currentFacingTangent.sqrMagnitude > 0.0001f)
        {
            return currentFacingTangent.normalized;
        }

        Vector2 transformForward = new Vector2(transform.forward.x, transform.forward.z);
        return transformForward.sqrMagnitude > 0.0001f ? transformForward.normalized : Vector2.up;
    }

    private Vector2 ResolveCoastTravelDirection()
    {
        if (lastRailTravelDirection.sqrMagnitude > 0.0001f)
        {
            return lastRailTravelDirection.normalized;
        }

        Vector2 referenceFacing = ResolveReferenceFacing();
        if (referenceFacing.sqrMagnitude <= 0.0001f)
        {
            return Vector2.up;
        }

        referenceFacing.Normalize();
        return Mathf.Abs(CurrentVehicleSignedSpeed) > 0.0001f
            ? referenceFacing * Mathf.Sign(CurrentVehicleSignedSpeed)
            : referenceFacing;
    }

    private Vector2 ResolveCoastFacingDirection()
    {
        Vector2 travelDirection = ResolveCoastTravelDirection();
        if (travelDirection.sqrMagnitude > 0.0001f
            && Mathf.Abs(CurrentVehicleSignedSpeed) > 0.0001f)
        {
            return travelDirection.normalized * Mathf.Sign(CurrentVehicleSignedSpeed);
        }

        return ResolveReferenceFacing();
    }

    private Vector2 ResolveFacingTangent(Vector2 railTangent, Vector2 referenceDirection)
    {
        return ResolveFacingTangentWithFallback(
            railTangent,
            referenceDirection,
            ResolveReferenceFacing());
    }

    private Vector2 ResolveAlignedRailTangent(
        Vector2 railTangent,
        Vector2 referenceDirection,
        Vector2 fallbackDirection)
    {
        Vector2 resolvedFallbackDirection = fallbackDirection.sqrMagnitude > 0.0001f
            ? fallbackDirection
            : ResolveReferenceFacing();
        return ResolveFacingTangentWithFallback(
            railTangent,
            referenceDirection,
            resolvedFallbackDirection);
    }

    private static Vector2 ResolveFollowerFacingTangent(
        Vector2 railTangent,
        Vector2 previousFacing,
        Vector2 travelDirection)
    {
        if (railTangent.sqrMagnitude <= 0.0001f)
        {
            if (previousFacing.sqrMagnitude > 0.0001f)
            {
                return previousFacing.normalized;
            }

            return travelDirection.sqrMagnitude > 0.0001f
                ? travelDirection.normalized
                : Vector2.up;
        }

        railTangent.Normalize();
        if (TryResolveTangentReferenceSign(railTangent, previousFacing, out float referenceSign))
        {
            return railTangent * referenceSign;
        }

        if (previousFacing.sqrMagnitude > 0.0001f
            && travelDirection.sqrMagnitude > 0.0001f)
        {
            Vector2 previousFacingNormalized = previousFacing.normalized;
            Vector2 travelDirectionNormalized = travelDirection.normalized;
            float facingTravelDot = Vector2.Dot(previousFacingNormalized, travelDirectionNormalized);
            if (Mathf.Abs(facingTravelDot) > RailDirectionReferenceDeadZone
                && TryResolveTangentReferenceSign(
                    railTangent,
                    travelDirectionNormalized * Mathf.Sign(facingTravelDot),
                    out referenceSign))
            {
                return railTangent * referenceSign;
            }
        }

        return ResolveFacingTangentWithFallback(railTangent, travelDirection, previousFacing);
    }

    private static Vector2 ResolveFacingTangentWithFallback(
        Vector2 railTangent,
        Vector2 referenceDirection,
        Vector2 fallbackDirection)
    {
        if (railTangent.sqrMagnitude <= 0.0001f)
        {
            if (referenceDirection.sqrMagnitude > 0.0001f)
            {
                return referenceDirection.normalized;
            }

            return fallbackDirection.sqrMagnitude > 0.0001f
                ? fallbackDirection.normalized
                : Vector2.up;
        }

        railTangent.Normalize();
        if (TryResolveTangentReferenceSign(railTangent, referenceDirection, out float referenceSign)
            || TryResolveTangentReferenceSign(railTangent, fallbackDirection, out referenceSign))
        {
            railTangent *= referenceSign;
        }

        return railTangent;
    }

    private Vector2 ResolveBranchFacingTangent(Vector2 railTangent, Vector2 inputDirection)
    {
        Vector2 currentFacing = ResolveReferenceFacing();
        if (railTangent.sqrMagnitude <= 0.0001f)
        {
            return inputDirection.sqrMagnitude > 0.0001f
                ? inputDirection.normalized
                : currentFacing.sqrMagnitude > 0.0001f
                    ? currentFacing.normalized
                    : Vector2.up;
        }

        railTangent.Normalize();
        if (TryResolveTangentReferenceSign(railTangent, inputDirection, out float referenceSign)
            || TryResolveTangentReferenceSign(railTangent, currentFacing, out referenceSign))
        {
            railTangent *= referenceSign;
        }

        return railTangent;
    }

    private static bool TryResolveTangentReferenceSign(
        Vector2 tangent,
        Vector2 referenceDirection,
        out float sign)
    {
        sign = 1f;
        if (tangent.sqrMagnitude <= 0.0001f
            || referenceDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float dot = Vector2.Dot(tangent.normalized, referenceDirection.normalized);
        if (Mathf.Abs(dot) <= RailDirectionReferenceDeadZone)
        {
            return false;
        }

        sign = Mathf.Sign(dot);
        return true;
    }

    private void ApplyRailPose(
        RailSample sample,
        Vector2 facingTangent,
        float deltaTime = 0f,
        bool smoothRotation = false)
    {
        if (sample.Rail == null)
        {
            return;
        }

        if (facingTangent.sqrMagnitude <= 0.0001f)
        {
            facingTangent = sample.Tangent.sqrMagnitude > 0.0001f
                ? sample.Tangent
                : Vector2.up;
        }

        facingTangent = ResolveAlignedRailTangent(
            facingTangent,
            currentFacingTangent,
            ResolveReferenceFacing());
        facingTangent.Normalize();
        Quaternion targetRotation = Quaternion.LookRotation(
            new Vector3(facingTangent.x, 0f, facingTangent.y),
            Vector3.up);
        Quaternion rotation = targetRotation;
        if (smoothRotation && deltaTime > 0f)
        {
            float interpolation = 1f - Mathf.Exp(
                -Mathf.Max(0.01f, railRotationInterpolationSpeed) * deltaTime);
            rotation = Quaternion.Slerp(transform.rotation, targetRotation, interpolation);
        }

        if (!ApplyRailPoseToRail(
                sample.Rail,
                sample.DistanceAlongPath,
                sample.Point,
                facingTangent,
                rotation))
        {
            return;
        }

        SyncRailFacingTangent(facingTangent, false);
        Physics.SyncTransforms();
    }

    private void SyncRailFacingTangent(Vector2 facingTangent, bool updateLastTravelDirection)
    {
        if (facingTangent.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        currentFacingTangent = facingTangent.normalized;
        if (updateLastTravelDirection)
        {
            lastRailTravelDirection = currentFacingTangent;
        }
    }
}
