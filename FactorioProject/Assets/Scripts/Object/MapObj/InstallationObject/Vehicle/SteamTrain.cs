using System.Collections.Generic;
using UnityEngine;

public class SteamTrain : RailHandcar,
    IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IMapObjectStagedUpdateTick
{
    private static readonly List<AutoDriveRoutePlanner.RouteSegment> SharedDebugRouteSegmentScratch =
        new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private static readonly Dictionary<Vector2Int, List<SteamTrain>> WaterPipeReceiversByCoordinate =
        new Dictionary<Vector2Int, List<SteamTrain>>();
    private static ulong nextAutoDriveControllerRevision;

    public readonly struct AutoDriveDebugRouteSegment
    {
        public AutoDriveDebugRouteSegment(Railload rail, float startDistance, float endDistance)
        {
            Rail = rail;
            StartDistance = startDistance;
            EndDistance = endDistance;
        }

        public Railload Rail { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
    }

    private enum AutoDriveFuelFilter
    {
        Free = 0,
        Full = 1
    }

    private enum AutoDriveFreightFilter
    {
        Free = 0,
        Full = 1,
        Empty = 2
    }

    private enum AutoDriveStatus
    {
        Idle = 0,
        NoTarget = 1,
        Planning = 2,
        Moving = 3,
        Docking = 4,
        WaitingAtStation = 5,
        WaitingForFuel = 6,
        WaitingForFreight = 7,
        WaitingForPath = 8,
        WaitingForClearTrack = 9,
        Arrived = 10,
        WaitingForDepartureFuel = 11,
        WaitingForWater = 12
    }

    private enum DriveMotionOutcome
    {
        Applied = 0,
        BlockedByFuel = 1,
        BlockedByWater = 2
    }

    public enum InfoWarning
    {
        None,
        DepartureCondition,
        ResourceShortage
    }

    [SerializeField]
    private Transform waterPipe;
    [SerializeField, Min(0f)]
    private float waterPipeExtendDistance = 0.3f;
    [SerializeField, Min(0.01f)]
    private float waterPipeInterpolationSpeed = 8f;

    private const float MovementParticleMinDistanceSqr = 0.000001f;
    private const float BurnEnergyEpsilon = 0.0001f;
    private const float WaterEpsilon = 0.0001f;
    private const float BurnEnergyDrivingSpeedThreshold = 0.0001f;
    private const float RearFreightCarMinBehindDistance = 0.01f;
    private const float WaterUseRatePerSecond = 0.8f;
    private const int WaterPipeNetworkSearchMaxNodes = 128;
    private const float WaterPipeDockRailCoordinateSampleMaxDistance = 0.6f;
    private const float WaterPipeDockFacingDotEpsilon = 0.05f;
    private const float WaterPipeDockMinAlongDistance = 0.45f;
    private const float WaterPipeDockMaxAlongDistance = 1.35f;
    private const float WaterPipeDockMaxLateralDistance = 0.35f;
    private const float AutoDriveRouteRefreshInterval = 0.25f;
    private const float AutoDriveLookAheadDistance = 0.65f;
    private const float AutoDriveBranchLookAheadDistance = 0.45f;
    private const float AutoDriveRouteSegmentTolerance = 0.2f;
    private const float AutoDriveRailConnectionMovementCostMaxDistance = RailConnectionUtility.ConnectionDistance;
    private const int AutoDriveRouteCursorLookAheadSegments = 2;
    private const float AutoDrivePreferredBranchSelectionDistance =
        AutoDriveLookAheadDistance + AutoDriveRouteSegmentTolerance;
    private const float AutoDriveDockSnapDistance = 0.05f;
    // Keep this above RailHandcar's input dead zone so auto-drive docking creep still moves.
    private const float AutoDriveDockApproachMinInputMagnitude = 0.18f;
    private const float AutoDriveWaitDurationSeconds = 5f;
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private Vector3 lastMovementParticlePosition;
    private bool hasLastMovementParticlePosition;
    private int lastDrivenInputFrame = -1;
    private long lastManualDriveSimulationTick = -1L;
    private float plannedAutoDriveDeltaTime;
    private bool autoDriveTickPlanned;
    private long storedBurnEnergyUnits;
    private long burnEnergyGaugeCapacityUnits;
    private long pendingBurnEnergyCostUnits;
    private int pendingBurnEnergyFrame = -1;
    private long pendingWaterCostUnits;
    private int pendingWaterFrame = -1;
    private Vector3 waterPipeDefaultLocalPosition;
    private Quaternion waterPipeDefaultLocalRotation = Quaternion.identity;
    private Vector3 waterPipeTargetLocalPosition;
    private Quaternion waterPipeTargetLocalRotation = Quaternion.identity;
    private bool waterPipeDefaultsCaptured;
    private bool waterPipeTargetActive;
    private bool waterPipeAnimating;
    private bool waterPipeTransferReady;
    private bool waterPipeReceiverRegistered;
    private Vector2Int registeredWaterPipeReceiverCoordinate;
    private Vector2Int registeredWaterPipeCoordinate;
    private Vector2Int registeredWaterPipeDirectionFromTrainToPipe;
    private bool autoDriveEnabled;
    private string autoDriveTargetAStationName = string.Empty;
    private string autoDriveTargetBStationName = string.Empty;
    private AutoDriveFuelFilter autoDriveTargetAFuelFilter;
    private AutoDriveFreightFilter autoDriveTargetAFreightFilter;
    private AutoDriveFuelFilter autoDriveTargetBFuelFilter;
    private AutoDriveFreightFilter autoDriveTargetBFreightFilter;
    private AutoDriveStatus autoDriveStatus;
    private string autoDriveCurrentTargetStationName = string.Empty;
    private string autoDriveNextTargetStationName = string.Empty;
    private Vector2Int activeWaterPipeDirectionFromTrainToPipe;
    private bool waterPipeDockLockActive;
    private Railload lockedWaterPipeDockRail;
    private long lockedWaterPipeDockDistanceUnits;
    private Vector2 lockedWaterPipeDockFacing;
    private Vector2Int lockedWaterPipeDockDirectionFromTrainToPipe;
    private Vector2Int lockedWaterPipeDockCoordinate;
    private readonly List<PortableObject> burnEnergyPortableMoveBuffer = new List<PortableObject>();
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveRouteSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveFixedRouteSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveRouteScratchSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveRouteReferenceScratchSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<Train> autoDriveConnectedTrainScratch = new List<Train>(8);
    private readonly HashSet<FreightCar> autoDriveFuelFreightCarScratch = new HashSet<FreightCar>();
    private readonly Queue<Vector2Int> waterPipeSearchQueue = new Queue<Vector2Int>(32);
    private readonly Queue<Train> autoDriveConnectedTrainQueue = new Queue<Train>(8);
    private readonly HashSet<Vector2Int> waterPipeSearchVisited = new HashSet<Vector2Int>();
    private readonly HashSet<Train> autoDriveConnectedTrainVisited = new HashSet<Train>();
    private string autoDriveRouteTargetStationName = string.Empty;
    private string autoDriveCachedRouteReferenceTargetStationName = string.Empty;
    private long autoDriveRouteReferenceTrainSimulationId;
    private int autoDriveRouteGraphVersion = -1;
    private int autoDriveFixedRouteGraphVersion = -1;
    private bool autoDriveRouteRefreshRequested;
    private int autoDriveRouteSegmentCursor;
    // Pending departure station. Cleared once the train actually moves; the active
    // route keeps the destination across controller handoff and save/load.
    private string autoDriveLastArrivedStationName = string.Empty;
    private string autoDriveFixedRouteStartStationName = string.Empty;
    private string autoDriveFixedRouteEndStationName = string.Empty;
    private string autoDriveResolvedTargetStationName = string.Empty;
    private string autoDriveResolvedNextStationName = string.Empty;
    private Trainstation autoDriveResolvedTargetStation;
    private RailHandcar autoDriveCachedRouteReferenceTrain;
    private float autoDriveRouteRefreshTimer;
    private float autoDriveStationWaitTimer;
    private long autoDriveSimulationTick = -1L;
    private Player autoDriveMountedPlayer;
    private ulong autoDriveControllerRevision;
    private ulong autoDriveConnectedTrainGraphRevision;
    private bool autoDriveConnectedTrainCacheValid;

    public float ObjectInfoStoredBurnEnergy => DeterministicSimulationUnits.ToFloat(storedBurnEnergyUnits);
    public float ManagedUpdateTickIntervalSeconds => MapObjectTickManager.FixedSimulationDeltaSeconds;
    public float ObjectInfoBurnEnergyGaugeCapacity => DeterministicSimulationUnits.ToFloat(
        System.Math.Max(burnEnergyGaugeCapacityUnits, storedBurnEnergyUnits));
    public float ObjectInfoBurnEnergyGaugeFillAmount
    {
        get
        {
            float gaugeCapacity = ObjectInfoBurnEnergyGaugeCapacity;
            return gaugeCapacity > BurnEnergyEpsilon
                ? Mathf.Clamp01(ObjectInfoStoredBurnEnergy / gaugeCapacity)
                : 0f;
        }
    }
    public float ObjectInfoBurnEnergyUseRatePerSecond
    {
        get
        {
            ItemDefinition installedDefinition = ResolveInstalledDefinition();
            return installedDefinition != null && installedDefinition.useEnergyType == ItemDefinition.EnergyType.Burn
                ? ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition)
                : 0f;
        }
    }
    public float ObjectInfoStoredWaterLiters => Mathf.Max(0f, StoredFluidLiters);
    public float ObjectInfoWaterCapacityLiters => Mathf.Max(0f, FluidStorageCapacityLiters);
    private SteamTrain AutoDriveSettingsOwner => ResolveAutoDriveControllerForConsist() ?? this;
    public bool AutoDriveEnabled => AutoDriveSettingsOwner.autoDriveEnabled;
    public string AutoDriveTargetAStationName => AutoDriveSettingsOwner.autoDriveTargetAStationName;
    public string AutoDriveTargetBStationName => AutoDriveSettingsOwner.autoDriveTargetBStationName;
    public string AutoDriveTargetAFuelFilterName => AutoDriveSettingsOwner.autoDriveTargetAFuelFilter.ToString();
    public string AutoDriveTargetAFreightFilterName => AutoDriveSettingsOwner.autoDriveTargetAFreightFilter.ToString();
    public string AutoDriveTargetBFuelFilterName => AutoDriveSettingsOwner.autoDriveTargetBFuelFilter.ToString();
    public string AutoDriveTargetBFreightFilterName => AutoDriveSettingsOwner.autoDriveTargetBFreightFilter.ToString();
    public string AutoDriveStatusText => AutoDriveSettingsOwner.ResolveAutoDriveStatusText();
    public override bool BlocksManualDisconnection => AutoDriveEnabled;
    private bool HasAnyAutoDriveTarget =>
        !string.IsNullOrWhiteSpace(autoDriveTargetAStationName)
        || !string.IsNullOrWhiteSpace(autoDriveTargetBStationName);
    public float ObjectInfoWaterGaugeFillAmount
    {
        get
        {
            float capacityLiters = ObjectInfoWaterCapacityLiters;
            return capacityLiters > WaterEpsilon
                ? Mathf.Clamp01(ObjectInfoStoredWaterLiters / capacityLiters)
                : 0f;
        }
    }
    public float ObjectInfoWaterUseRatePerSecond => WaterUseRatePerSecond;
    public int ObjectInfoWaterItemId
    {
        get
        {
            int storedFluidItemId = StoredFluidItemId;
            return storedFluidItemId >= 0
                ? storedFluidItemId
                : ResolveWaterItemId();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWaterPipeReceiverRegistry()
    {
        WaterPipeReceiversByCoordinate.Clear();
    }

    internal static bool TryGetWaterPipeReceiverAtCoordinate(
        Vector2Int coordinate,
        out SteamTrain receiver)
    {
        receiver = null;
        if (!WaterPipeReceiversByCoordinate.TryGetValue(
                coordinate,
                out List<SteamTrain> candidates))
        {
            return false;
        }

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            SteamTrain candidate = candidates[i];
            if (candidate == null
                || !candidate.waterPipeReceiverRegistered
                || candidate.registeredWaterPipeReceiverCoordinate != coordinate)
            {
                candidates.RemoveAt(i);
            }
        }

        if (candidates.Count <= 0)
        {
            WaterPipeReceiversByCoordinate.Remove(coordinate);
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            SteamTrain candidate = candidates[i];
            if (!candidate.isActiveAndEnabled
                || !candidate.gameObject.activeInHierarchy
                || !candidate.waterPipeTargetActive
                || !candidate.waterPipeTransferReady)
            {
                continue;
            }

            receiver = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetAutoDriveDebugRouteSegments(List<AutoDriveDebugRouteSegment> result)
    {
        SteamTrain controller = AutoDriveSettingsOwner;
        if (controller != this)
        {
            return controller.TryGetAutoDriveDebugRouteSegments(result);
        }

        result?.Clear();
        if (result == null || !autoDriveEnabled)
        {
            return false;
        }

        List<AutoDriveRoutePlanner.RouteSegment> sourceSegments = autoDriveRouteSegments;
        if (sourceSegments.Count <= 0)
        {
            if (!TryEnsureAutoDriveFixedRoute())
            {
                return false;
            }

            sourceSegments = autoDriveFixedRouteSegments;
        }

        for (int i = 0; i < sourceSegments.Count; i++)
        {
            AutoDriveRoutePlanner.RouteSegment segment = sourceSegments[i];
            if (segment.Rail == null || segment.Length <= 0.0001f)
            {
                continue;
            }

            result.Add(new AutoDriveDebugRouteSegment(
                segment.Rail,
                segment.StartDistance,
                segment.EndDistance));
        }

        return result.Count > 0;
    }

    public static bool TryBuildDebugRouteBetweenStations(
        string startStationName,
        string destinationStationName,
        List<AutoDriveDebugRouteSegment> result)
    {
        result?.Clear();
        if (string.IsNullOrWhiteSpace(startStationName)
            || string.IsNullOrWhiteSpace(destinationStationName)
            || !AutoDriveRoutePlanner.TryFindStationByName(startStationName.Trim(), out Trainstation startStation)
            || !AutoDriveRoutePlanner.TryFindStationByName(destinationStationName.Trim(), out Trainstation destinationStation))
        {
            return false;
        }

        return TryBuildDebugRouteBetweenStations(startStation, destinationStation, result);
    }

    public static bool TryBuildDebugRouteBetweenStations(
        Trainstation startStation,
        Trainstation destinationStation,
        List<AutoDriveDebugRouteSegment> result)
    {
        result?.Clear();
        if (result == null
            || startStation == null
            || destinationStation == null)
        {
            return false;
        }

        SharedDebugRouteSegmentScratch.Clear();
        if (!AutoDriveRoutePlanner.TryBuildRoute(
                startStation,
                destinationStation,
                SharedDebugRouteSegmentScratch))
        {
            return false;
        }

        for (int i = 0; i < SharedDebugRouteSegmentScratch.Count; i++)
        {
            AutoDriveRoutePlanner.RouteSegment segment = SharedDebugRouteSegmentScratch[i];
            if (segment.Rail == null || segment.Length <= 0.0001f)
            {
                continue;
            }

            result.Add(new AutoDriveDebugRouteSegment(
                segment.Rail,
                segment.StartDistance,
                segment.EndDistance));
        }

        return result.Count > 0;
    }

    public bool TryGetCurrentAutoDriveTargetStation(out Trainstation station)
    {
        station = autoDriveResolvedTargetStation;
        if (station != null && station.gameObject.activeInHierarchy)
        {
            return true;
        }

        station = null;
        return !string.IsNullOrWhiteSpace(autoDriveResolvedTargetStationName)
               && AutoDriveRoutePlanner.TryFindStationByName(autoDriveResolvedTargetStationName, out station);
    }

    public override bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        int waterItemId = ResolveWaterItemId();
        if (fluidItemId >= 0 && (waterItemId < 0 || fluidItemId != waterItemId))
        {
            return false;
        }

        return base.CanAcceptFluidItem(fluidItemId, requestedLiters);
    }

    public bool CanAcceptWaterFromPipeDirection(
        Vector2Int directionFromPipeToTrain,
        int fluidItemId,
        bool requireStorageSpace)
    {
        if (directionFromPipeToTrain == Vector2Int.zero)
        {
            return false;
        }

        Vector2Int directionFromTrainToPipe = -directionFromPipeToTrain;
        int waterItemId = ResolveWaterItemId();
        return waterPipeTargetActive
               && waterPipeTransferReady
               && activeWaterPipeDirectionFromTrainToPipe == directionFromTrainToPipe
               && waterItemId >= 0
               && fluidItemId == waterItemId
               && CanAcceptFluidItem(fluidItemId, requireStorageSpace ? 0.0001f : 0f);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        MapObjectTickManager.RegisterUpdateTick(this);
        InvalidateAutoDriveConnectedTrainCache();
        ResetMovementParticleState();
        CaptureWaterPipeDefaults();
        ResetWaterPipeImmediate(false);
    }

    protected override void OnDisable()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        InvalidateAutoDriveConnectedTrainCache();
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        lastManualDriveSimulationTick = -1L;
        autoDriveSimulationTick = -1L;
        autoDriveMountedPlayer = null;
        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        ResetWaterPipeImmediate(false);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        lastManualDriveSimulationTick = -1L;
        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        storedBurnEnergyUnits = 0L;
        burnEnergyGaugeCapacityUnits = 0L;
        autoDriveSimulationTick = -1L;
        autoDriveMountedPlayer = null;
        ResetAutoDriveState();
        ResetWaterPipeImmediate(false);
        base.PrepareForPool();
    }

    public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
        HandleMountedInput(worldMoveDirection, moveSpeed, deltaTime, null);
    }

    public override void HandleMountedInput(
        Vector3 worldMoveDirection,
        float moveSpeed,
        float deltaTime,
        Player mountedPlayer)
    {
        SteamTrain controller = ResolveAutoDriveControllerForConsist();
        if (controller != null)
        {
            controller.autoDriveMountedPlayer = mountedPlayer;
            return;
        }

        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        lastManualDriveSimulationTick = MapObjectTickManager.CurrentSimulationTick;
        HandleResolvedDriveMotion(
            worldMoveDirection,
            moveSpeed,
            deltaTime,
            mountedPlayer,
            false);
    }

    public override void NotifyPlayerDismounted(Player dismountedPlayer)
    {
        if (autoDriveMountedPlayer == dismountedPlayer)
        {
            autoDriveMountedPlayer = null;
        }

        base.NotifyPlayerDismounted(dismountedPlayer);
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }

    public void PlanManagedUpdateTick(float deltaTime)
    {
        plannedAutoDriveDeltaTime = Mathf.Max(0f, deltaTime);
        autoDriveTickPlanned = autoDriveEnabled && IsPrimaryAutoDriveControllerForConsist();
    }

    public void ApplyManagedUpdateTick()
    {
        if (!autoDriveTickPlanned)
        {
            return;
        }

        autoDriveTickPlanned = false;
        TickAutoDrive(plannedAutoDriveDeltaTime, autoDriveMountedPlayer);
        plannedAutoDriveDeltaTime = 0f;
    }

    private void TickAutoDrive(float deltaTime, Player mountedPlayer)
    {
        long currentSimulationTick = MapObjectTickManager.CurrentSimulationTick;
        if (!autoDriveEnabled
            || autoDriveSimulationTick == currentSimulationTick
            || !IsPrimaryAutoDriveControllerForConsist())
        {
            return;
        }

        autoDriveSimulationTick = currentSimulationTick;
        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        if (!HasCompleteAutoDriveTargets())
        {
            autoDriveEnabled = false;
            autoDriveControllerRevision = 0;
            ResetAutoDriveRuntimeState();
            SetAutoDriveStatus(AutoDriveStatus.NoTarget, string.Empty, string.Empty);
            PersistAutoDriveState();
            return;
        }

        Vector3 moveDirection = ResolveAutoDriveMoveDirection(deltaTime, out SteamTrain powerSource);
        if (powerSource != null && powerSource != this)
        {
            TransferAutoDriveControl(powerSource);
            powerSource.TickAutoDrive(deltaTime, mountedPlayer);
            return;
        }

        // Manual reverse momentum must not carry over into automatic driving.
        if (CurrentVehicleSignedSpeed < 0f || powerSource == null)
        {
            ResetVehicleMotion();
        }

        bool isDepartureAttempt = moveDirection.sqrMagnitude > 0.0001f
            && !string.IsNullOrEmpty(autoDriveLastArrivedStationName);
        Vector3 departurePosition = isDepartureAttempt ? transform.position : default;
        DriveMotionOutcome outcome = HandleResolvedDriveMotion(
            moveDirection,
            0f,
            deltaTime,
            mountedPlayer,
            true);
        if (outcome != DriveMotionOutcome.Applied)
        {
            SetAutoDriveStatus(
                outcome == DriveMotionOutcome.BlockedByWater
                    ? AutoDriveStatus.WaitingForWater : AutoDriveStatus.WaitingForFuel,
                autoDriveResolvedTargetStationName,
                autoDriveResolvedNextStationName);
            return;
        }

        if (isDepartureAttempt)
        {
            Vector3 displacement = transform.position - departurePosition;
            if (displacement.x * displacement.x + displacement.z * displacement.z > 0f)
            {
                // Full fuel/cargo is a departure condition, not an invariant while driving.
                // Wait until actual movement so fuel shortages and blocked track still retry
                // the station's conditions. CaptureAutoDriveState preserves this completed
                // departure as an empty station name without changing the save format.
                autoDriveLastArrivedStationName = string.Empty;
            }
        }

        TryFinalizeAutoDriveArrival(deltaTime);
    }

    private DriveMotionOutcome HandleResolvedDriveMotion(
        Vector3 resolvedMoveDirection,
        float moveSpeed,
        float deltaTime,
        Player mountedPlayer,
        bool commitResourceCostsImmediately)
    {
        if (resolvedMoveDirection.sqrMagnitude > 0.0001f)
        {
            RequestWaterPipeRetract();
        }

        if (RequiresWater(resolvedMoveDirection, deltaTime, out float waterCost)
            && !TryEnsureWaterAvailable(waterCost))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
            return DriveMotionOutcome.BlockedByWater;
        }

        if (RequiresPoweredBurnEnergy(resolvedMoveDirection, deltaTime, out float burnEnergyCost)
            && !TryEnsureBurnEnergyAvailable(burnEnergyCost, mountedPlayer))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
            return DriveMotionOutcome.BlockedByFuel;
        }

        lastDrivenInputFrame = Time.frameCount;
        base.HandleMountedInput(resolvedMoveDirection, moveSpeed, deltaTime);
        if (commitResourceCostsImmediately)
        {
            if (CurrentVehicleSpeed > BurnEnergyDrivingSpeedThreshold)
            {
                SpendStoredBurnEnergyUnits(DeterministicSimulationUnits.FromFloat(burnEnergyCost));
                SpendStoredWaterUnits(DeterministicSimulationUnits.FromFloat(waterCost));
            }
        }
        else
        {
            if (burnEnergyCost > BurnEnergyEpsilon)
            {
                pendingBurnEnergyCostUnits = DeterministicSimulationUnits.FromFloat(burnEnergyCost);
                pendingBurnEnergyFrame = Time.frameCount;
            }

            if (waterCost > WaterEpsilon)
            {
                pendingWaterCostUnits = DeterministicSimulationUnits.FromFloat(waterCost);
                pendingWaterFrame = Time.frameCount;
            }
        }

        return DriveMotionOutcome.Applied;
    }

    private void LateUpdate()
    {
        Vector3 currentPosition = transform.position;
        if (!hasLastMovementParticlePosition)
        {
            lastMovementParticlePosition = currentPosition;
            hasLastMovementParticlePosition = true;
            StopMovementParticle(false);
            return;
        }

        bool isDrivenThisFrame = lastDrivenInputFrame == Time.frameCount;
        bool hasMovedSinceLastFrame =
            GetPlanarDistanceSqr(lastMovementParticlePosition, currentPosition)
            > MovementParticleMinDistanceSqr;
        bool isDrivenAndMoving = isDrivenThisFrame && hasMovedSinceLastFrame;
        if (isDrivenThisFrame
            && pendingBurnEnergyFrame == Time.frameCount
            && CurrentVehicleSpeed > BurnEnergyDrivingSpeedThreshold)
        {
            SpendStoredBurnEnergyUnits(pendingBurnEnergyCostUnits);
        }

        if (isDrivenThisFrame
            && pendingWaterFrame == Time.frameCount
            && CurrentVehicleSpeed > BurnEnergyDrivingSpeedThreshold)
        {
            SpendStoredWaterUnits(pendingWaterCostUnits);
        }

        if (waterPipeTargetActive
            && waterPipeTransferReady
            && hasMovedSinceLastFrame)
        {
            RequestWaterPipeRetract();
        }

        if (!HasFluidStorageSpace)
        {
            RequestWaterPipeRetract();
        }
        else if (!hasMovedSinceLastFrame)
        {
            RefreshAlignedWaterPipeDock();
        }

        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        SetMovementParticleActive(isDrivenAndMoving);
        lastMovementParticlePosition = currentPosition;
    }

    protected override void TickManagedVisuals(float deltaTime)
    {
        base.TickManagedVisuals(deltaTime);
        UpdateWaterPipeVisual(deltaTime);
    }

    protected override void OnManagedVisualsResumed()
    {
        // The docking state continues offscreen; synchronize to its latest visual target.
        UpdateWaterPipeVisual(0f);
    }

    public void CaptureBurnEnergyState(out float storedEnergy, out float gaugeCapacity)
    {
        storedEnergy = DeterministicSimulationUnits.ToFloat(storedBurnEnergyUnits);
        gaugeCapacity = DeterministicSimulationUnits.ToFloat(
            System.Math.Max(burnEnergyGaugeCapacityUnits, storedBurnEnergyUnits));
    }

    public void CaptureBurnEnergyStateUnits(out long storedEnergyUnits, out long gaugeCapacityUnits)
    {
        storedEnergyUnits = System.Math.Max(0L, storedBurnEnergyUnits);
        gaugeCapacityUnits = System.Math.Max(burnEnergyGaugeCapacityUnits, storedEnergyUnits);
    }

    public void ApplyBurnEnergyState(float storedEnergy, float gaugeCapacity)
    {
        storedBurnEnergyUnits = DeterministicSimulationUnits.FromFloat(storedEnergy);
        burnEnergyGaugeCapacityUnits = System.Math.Max(
            DeterministicSimulationUnits.FromFloat(gaugeCapacity),
            storedBurnEnergyUnits);
    }

    public void ApplyBurnEnergyStateUnits(long storedEnergyUnits, long gaugeCapacityUnits)
    {
        storedBurnEnergyUnits = System.Math.Max(0L, storedEnergyUnits);
        burnEnergyGaugeCapacityUnits = System.Math.Max(
            System.Math.Max(0L, gaugeCapacityUnits),
            storedBurnEnergyUnits);
    }

    public void ClearBurnEnergyState()
    {
        ClearPendingBurnEnergyCost();
        ApplyBurnEnergyStateUnits(0L, 0L);
    }

    public void ApplyAutoDriveSettings(
        bool enabled,
        string targetAStationName,
        string targetBStationName,
        string targetAFuelFilterName,
        string targetAFreightFilterName,
        string targetBFuelFilterName,
        string targetBFreightFilterName)
    {
        SteamTrain controller = AutoDriveSettingsOwner;
        if (controller != this)
        {
            controller.ApplyAutoDriveSettings(
                enabled,
                targetAStationName,
                targetBStationName,
                targetAFuelFilterName,
                targetAFreightFilterName,
                targetBFuelFilterName,
                targetBFreightFilterName);
            return;
        }

        string normalizedTargetA = NormalizeAutoDriveStationName(targetAStationName);
        string normalizedTargetB = NormalizeAutoDriveStationName(targetBStationName);
        AutoDriveFuelFilter normalizedTargetAFuelFilter = ParseAutoDriveFuelFilter(targetAFuelFilterName);
        AutoDriveFreightFilter normalizedTargetAFreightFilter = ParseAutoDriveFreightFilter(targetAFreightFilterName);
        AutoDriveFuelFilter normalizedTargetBFuelFilter = ParseAutoDriveFuelFilter(targetBFuelFilterName);
        AutoDriveFreightFilter normalizedTargetBFreightFilter = ParseAutoDriveFreightFilter(targetBFreightFilterName);
        bool normalizedEnabled = enabled && HasCompleteAutoDriveTargets(normalizedTargetA, normalizedTargetB);

        bool changed =
            autoDriveEnabled != normalizedEnabled
            || !string.Equals(autoDriveTargetAStationName, normalizedTargetA, System.StringComparison.OrdinalIgnoreCase)
            || !string.Equals(autoDriveTargetBStationName, normalizedTargetB, System.StringComparison.OrdinalIgnoreCase)
            || autoDriveTargetAFuelFilter != normalizedTargetAFuelFilter
            || autoDriveTargetAFreightFilter != normalizedTargetAFreightFilter
            || autoDriveTargetBFuelFilter != normalizedTargetBFuelFilter
            || autoDriveTargetBFreightFilter != normalizedTargetBFreightFilter;

        autoDriveEnabled = normalizedEnabled;
        autoDriveTargetAStationName = normalizedTargetA;
        autoDriveTargetBStationName = normalizedTargetB;
        autoDriveTargetAFuelFilter = normalizedTargetAFuelFilter;
        autoDriveTargetAFreightFilter = normalizedTargetAFreightFilter;
        autoDriveTargetBFuelFilter = normalizedTargetBFuelFilter;
        autoDriveTargetBFreightFilter = normalizedTargetBFreightFilter;
        if (autoDriveEnabled)
        {
            if (changed || autoDriveControllerRevision == 0)
            {
                ClaimAutoDriveControl();
            }
        }
        else
        {
            autoDriveControllerRevision = 0;
        }

        if (!changed)
        {
            return;
        }

        ResetAutoDriveRuntimeState();
        SetAutoDriveStatus(autoDriveEnabled ? AutoDriveStatus.Planning : AutoDriveStatus.Idle, string.Empty, string.Empty);
        PersistAutoDriveState();
    }

    public void CaptureAutoDriveState(
        out bool enabled,
        out string targetAStationName,
        out string targetBStationName,
        out int targetAFuelFilter,
        out int targetAFreightFilter,
        out int targetBFuelFilter,
        out int targetBFreightFilter,
        out string routeTargetStationName,
        out string lastArrivedStationName,
        out float stationWaitTimer)
    {
        enabled = autoDriveEnabled;
        targetAStationName = autoDriveTargetAStationName;
        targetBStationName = autoDriveTargetBStationName;
        targetAFuelFilter = (int)autoDriveTargetAFuelFilter;
        targetAFreightFilter = (int)autoDriveTargetAFreightFilter;
        targetBFuelFilter = (int)autoDriveTargetBFuelFilter;
        targetBFreightFilter = (int)autoDriveTargetBFreightFilter;
        routeTargetStationName = !string.IsNullOrWhiteSpace(autoDriveRouteTargetStationName)
            ? autoDriveRouteTargetStationName
            : autoDriveResolvedTargetStationName;
        lastArrivedStationName = autoDriveLastArrivedStationName;
        stationWaitTimer = Mathf.Max(0f, autoDriveStationWaitTimer);
    }

    public void ApplyAutoDriveState(
        bool enabled,
        string targetAStationName,
        string targetBStationName,
        int targetAFuelFilter,
        int targetAFreightFilter,
        int targetBFuelFilter,
        int targetBFreightFilter,
        string routeTargetStationName,
        string lastArrivedStationName,
        float stationWaitTimer)
    {
        autoDriveTargetAStationName = NormalizeAutoDriveStationName(targetAStationName);
        autoDriveTargetBStationName = NormalizeAutoDriveStationName(targetBStationName);
        autoDriveTargetAFuelFilter = ClampAutoDriveFuelFilter(targetAFuelFilter);
        autoDriveTargetAFreightFilter = ClampAutoDriveFreightFilter(targetAFreightFilter);
        autoDriveTargetBFuelFilter = ClampAutoDriveFuelFilter(targetBFuelFilter);
        autoDriveTargetBFreightFilter = ClampAutoDriveFreightFilter(targetBFreightFilter);
        autoDriveEnabled = enabled && HasCompleteAutoDriveTargets();
        if (autoDriveEnabled)
        {
            ClaimAutoDriveControl();
        }
        else
        {
            autoDriveControllerRevision = 0;
        }

        ResetAutoDriveRuntimeState();
        autoDriveRouteTargetStationName = NormalizeAutoDriveStationName(routeTargetStationName);
        autoDriveLastArrivedStationName = NormalizeAutoDriveStationName(lastArrivedStationName);
        autoDriveStationWaitTimer = Mathf.Max(0f, stationWaitTimer);
        SetAutoDriveStatus(
            autoDriveEnabled
                ? (autoDriveStationWaitTimer > 0f ? AutoDriveStatus.WaitingAtStation : AutoDriveStatus.Planning)
                : AutoDriveStatus.Idle,
            autoDriveStationWaitTimer > 0f ? autoDriveLastArrivedStationName : autoDriveRouteTargetStationName,
            autoDriveStationWaitTimer > 0f ? autoDriveRouteTargetStationName : string.Empty);
    }

    protected override bool TryApplyCustomIdleDocking(
        RailSample currentSample,
        Vector2 currentFacing,
        float deltaTime)
    {
        if (deltaTime > 0f
            && currentSample.Rail != null
            && TryApplyConsistWaterPipeDocking(currentSample, currentFacing, deltaTime))
        {
            return true;
        }

        return base.TryApplyCustomIdleDocking(currentSample, currentFacing, deltaTime);
    }

    private bool TryApplyConsistWaterPipeDocking(
        RailSample currentSample,
        Vector2 currentFacing,
        float deltaTime)
    {
        CollectAutoDriveConnectedTrains();
        SteamTrain bestMovingTrain = null;
        RailSample bestMovingTrainSample = default;
        RailSample bestMovingDockSample = default;
        Vector2 bestMovingDockFacing = Vector2.zero;
        Vector2Int bestMovingPipeDirection = Vector2Int.zero;
        float bestMovingSignedPathDelta = 0f;
        float bestMovingDistance = float.MaxValue;
        bool hasReadyDock = false;

        for (int i = 0; i < autoDriveConnectedTrainScratch.Count; i++)
        {
            if (autoDriveConnectedTrainScratch[i] is not SteamTrain candidate
                || !candidate.HasFluidStorageSpace
                || !TryBuildCurrentRailSample(candidate, out RailSample candidateSample)
                || !candidate.TryResolveWaterPipeDockSample(
                    candidateSample,
                    candidateSample.Tangent,
                    out RailSample candidateDockSample,
                    out float candidateSignedPathDelta,
                    out Vector2Int candidatePipeDirection,
                    out Vector2 candidateDockFacing))
            {
                if (autoDriveConnectedTrainScratch[i] is SteamTrain unavailableCandidate)
                {
                    unavailableCandidate.RequestWaterPipeRetract();
                }

                continue;
            }

            float candidateDistance = Mathf.Abs(candidateSignedPathDelta);
            if (candidateDistance <= ResolveDockCompleteDistance())
            {
                candidate.SetWaterPipeDockTarget(candidatePipeDirection, true);
                hasReadyDock = true;
                continue;
            }

            if (candidateDistance >= bestMovingDistance)
            {
                candidate.RequestWaterPipeRetract();
                continue;
            }

            if (bestMovingTrain != null)
            {
                bestMovingTrain.RequestWaterPipeRetract();
            }

            bestMovingTrain = candidate;
            bestMovingTrainSample = candidateSample;
            bestMovingDockSample = candidateDockSample;
            bestMovingDockFacing = candidateDockFacing;
            bestMovingPipeDirection = candidatePipeDirection;
            bestMovingSignedPathDelta = candidateSignedPathDelta;
            bestMovingDistance = candidateDistance;
        }

        // Do not pull an already filling locomotive away to align another one.
        if (hasReadyDock)
        {
            bestMovingTrain?.RequestWaterPipeRetract();
            return true;
        }

        if (bestMovingTrain == null)
        {
            return false;
        }

        bestMovingTrain.SetWaterPipeDockTarget(bestMovingPipeDirection, false);
        return bestMovingTrain == this
            ? TryApplyDockingToSample(
                currentSample,
                bestMovingDockFacing,
                bestMovingDockSample,
                bestMovingSignedPathDelta,
                deltaTime,
                preserveCoastTravelDirection: true,
                allowReverseDocking: true)
            : TryApplyConnectedTrainMemberDocking(
                currentSample,
                currentFacing,
                bestMovingTrainSample,
                bestMovingDockSample,
                deltaTime);
    }

    private void RefreshAlignedWaterPipeDock()
    {
        if (!TryBuildCurrentRailSample(this, out RailSample currentSample)
            || !TryFindWaterPipeDockSample(
                currentSample,
                out RailSample dockSample,
                out float signedPathDelta,
                out Vector2Int directionFromTrainToPipe,
                out Vector2Int pipeCoordinate)
            || Mathf.Abs(signedPathDelta) > ResolveDockCompleteDistance())
        {
            RequestWaterPipeRetract();
            return;
        }

        LockWaterPipeDock(
            dockSample,
            currentSample.Tangent,
            directionFromTrainToPipe,
            pipeCoordinate);
        SetWaterPipeDockTarget(directionFromTrainToPipe, true);
    }

    private bool RequiresPoweredBurnEnergy(Vector3 worldMoveDirection, float deltaTime, out float burnEnergyCost)
    {
        burnEnergyCost = 0f;
        if (IsFreeTrainEnabled())
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null
            || installedDefinition.useEnergyType != ItemDefinition.EnergyType.Burn
            || worldMoveDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float burnEnergyPerSecond = ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition);
        burnEnergyCost = burnEnergyPerSecond * Mathf.Max(0f, deltaTime);
        return burnEnergyCost > BurnEnergyEpsilon;
    }

    private bool RequiresWater(Vector3 worldMoveDirection, float deltaTime, out float waterCost)
    {
        waterCost = 0f;
        if (IsFreeTrainEnabled())
        {
            return false;
        }

        if (worldMoveDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float waterLitersPerSecond = ObjectInfoWaterUseRatePerSecond;
        waterCost = waterLitersPerSecond * Mathf.Max(0f, deltaTime);
        return waterCost > WaterEpsilon;
    }

    private bool TryEnsureWaterAvailable(float requiredLiters)
    {
        requiredLiters = Mathf.Max(0f, requiredLiters);
        if (requiredLiters <= WaterEpsilon)
        {
            return true;
        }

        int waterItemId = ResolveWaterItemId();
        return waterItemId >= 0 && CanProvideFluidItem(waterItemId, requiredLiters);
    }

    private bool TryEnsureBurnEnergyAvailable(float requiredEnergy, Player mountedPlayer)
    {
        requiredEnergy = Mathf.Max(0f, requiredEnergy);
        long requiredEnergyUnits = DeterministicSimulationUnits.FromFloat(requiredEnergy);
        while (storedBurnEnergyUnits < requiredEnergyUnits)
        {
            if (!TryConsumeOneBurnEnergyItem(mountedPlayer, out int gainedEnergy))
            {
                break;
            }

            storedBurnEnergyUnits += DeterministicSimulationUnits.FromFloat(gainedEnergy);
            burnEnergyGaugeCapacityUnits = System.Math.Max(
                burnEnergyGaugeCapacityUnits,
                System.Math.Max(storedBurnEnergyUnits, DeterministicSimulationUnits.UnitsPerWhole));
        }

        return storedBurnEnergyUnits >= requiredEnergyUnits;
    }

    private ItemDefinition ResolveInstalledDefinition()
    {
        return BoundItemDefinition != null
            ? BoundItemDefinition
            : InputOutputModule.ResolveItemDefinition(ResolveItemId());
    }

    private int ResolveWaterItemId()
    {
        return Pump.ResolveWaterItemId(null);
    }

    private static bool IsFreeTrainEnabled()
    {
        return GameManager.Instance != null && GameManager.Instance.FreeTrain;
    }

    private bool TryConsumeOneBurnEnergyItem(Player mountedPlayer, out int gainedEnergy)
    {
        if (TryConsumeBurnEnergyFromRearFreightCar(out gainedEnergy))
        {
            return true;
        }

        return TryConsumeBurnEnergyFromMountedPlayer(mountedPlayer, out gainedEnergy);
    }

    private bool TryConsumeBurnEnergyFromRearFreightCar(out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (!TryGetRearFreightCar(out FreightCar freightCar)
            || !freightCar.TryTakeOneItem(
                transform.position,
                IsUsableBurnEnergyItem,
                out int consumedItemId,
                out Vector3 pickupWorldPosition,
                out PortableObject consumedPortableObject))
        {
            return false;
        }

        if (!TryResolveBurnEnergyAmount(consumedItemId, out gainedEnergy))
        {
            DestroyPortableMoveObject(consumedPortableObject);
            return false;
        }

        PlayBurnEnergyPortableMove(consumedPortableObject, consumedItemId, pickupWorldPosition, true);
        return true;
    }

    private bool TryConsumeBurnEnergyFromMountedPlayer(Player mountedPlayer, out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (mountedPlayer == null)
        {
            return false;
        }

        PlayerBag bag = mountedPlayer.GetBag();
        if (TryConsumeBurnEnergyFromBag(bag, out gainedEnergy))
        {
            return true;
        }

        PlayerBag handBag = mountedPlayer.GetHandBag();
        if (handBag != null
            && handBag != bag
            && TryConsumeBurnEnergyFromBag(handBag, out gainedEnergy))
        {
            handBag.RefreshExternalStackCounts(false);
            mountedPlayer.UpdateCarryState();
            return true;
        }

        return false;
    }

    private bool TryConsumeBurnEnergyFromBag(PlayerBag bag, out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (bag == null)
        {
            return false;
        }

        int slotCount = bag.SlotCount;
        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            int itemId = bag.GetSlotItemId(slotIndex);
            if (!TryResolveBurnEnergyAmount(itemId, out int itemEnergyAmount))
            {
                continue;
            }

            TryGetTopPortableObjectInSlot(bag, slotIndex, out PortableObject sourcePortableObject);
            Vector3 startPosition = sourcePortableObject != null
                ? sourcePortableObject.transform.position
                : transform.position;
            if (!bag.TryRemoveItemsAtSlot(
                    slotIndex,
                    1,
                    out int removedItemId,
                    out int removedCount,
                    out Vector3 removedStartPosition)
                || removedCount <= 0)
            {
                continue;
            }

            if (sourcePortableObject == null)
            {
                startPosition = removedStartPosition;
            }

            if (removedItemId != itemId
                && !TryResolveBurnEnergyAmount(removedItemId, out itemEnergyAmount))
            {
                continue;
            }

            gainedEnergy = itemEnergyAmount;
            PlayBurnEnergyPortableMove(sourcePortableObject, removedItemId, startPosition, false);
            return true;
        }

        return false;
    }

    private bool TryGetTopPortableObjectInSlot(
        PlayerBag bag,
        int slotIndex,
        out PortableObject portableObject)
    {
        portableObject = null;
        burnEnergyPortableMoveBuffer.Clear();
        if (bag == null
            || !bag.TryGetOccupiedSlotObjects(slotIndex, burnEnergyPortableMoveBuffer)
            || burnEnergyPortableMoveBuffer.Count <= 0)
        {
            return false;
        }

        for (int i = burnEnergyPortableMoveBuffer.Count - 1; i >= 0; i--)
        {
            if (burnEnergyPortableMoveBuffer[i] == null)
            {
                continue;
            }

            portableObject = burnEnergyPortableMoveBuffer[i];
            return true;
        }

        return false;
    }

    internal bool TryGetRearFreightCar(out FreightCar freightCar)
    {
        freightCar = null;
        if (!TryResolveForward2D(out Vector2 forward))
        {
            return false;
        }

        Vector2 position = new Vector2(transform.position.x, transform.position.z);
        float bestRearScore = 0f;
        foreach (Train connectedTrain in ConnectedTrains)
        {
            if (connectedTrain is not FreightCar candidate
                || candidate == null
                || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 candidatePosition = candidate.transform.position;
            Vector2 delta = new Vector2(candidatePosition.x, candidatePosition.z) - position;
            float rearScore = -Vector2.Dot(delta, forward);
            if (rearScore <= RearFreightCarMinBehindDistance || rearScore <= bestRearScore)
            {
                continue;
            }

            bestRearScore = rearScore;
            freightCar = candidate;
        }

        return freightCar != null;
    }

    private bool TryResolveForward2D(out Vector2 forward)
    {
        if (TryGetCurrentRailPose(out _, out _, out _, out forward)
            && forward.sqrMagnitude > 0.0001f)
        {
            forward.Normalize();
            return true;
        }

        forward = new Vector2(transform.forward.x, transform.forward.z);
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        forward.Normalize();
        return true;
    }

    private bool TryResolveWaterPipeDockSample(
        RailSample currentSample,
        Vector2 currentFacing,
        out RailSample dockSample,
        out float signedPathDelta,
        out Vector2Int directionFromTrainToPipe,
        out Vector2 dockFacing)
    {
        if (TryGetLockedWaterPipeDockSample(
                currentSample,
                out dockSample,
                out signedPathDelta,
                out directionFromTrainToPipe,
                out dockFacing))
        {
            return true;
        }

        if (!TryFindWaterPipeDockSample(
                currentSample,
                out dockSample,
                out signedPathDelta,
                out directionFromTrainToPipe,
                out Vector2Int pipeCoordinate))
        {
            return false;
        }

        LockWaterPipeDock(dockSample, currentFacing, directionFromTrainToPipe, pipeCoordinate);
        dockFacing = lockedWaterPipeDockFacing;
        return true;
    }

    private bool TryGetLockedWaterPipeDockSample(
        RailSample currentSample,
        out RailSample dockSample,
        out float signedPathDelta,
        out Vector2Int directionFromTrainToPipe,
        out Vector2 dockFacing)
    {
        dockSample = default;
        signedPathDelta = 0f;
        directionFromTrainToPipe = Vector2Int.zero;
        dockFacing = Vector2.zero;

        if (!waterPipeDockLockActive)
        {
            return false;
        }

        if (currentSample.Rail == null
            || currentSample.Rail != lockedWaterPipeDockRail
            || !TryValidateLockedWaterPipeDock()
            || !lockedWaterPipeDockRail.TrySampleRenderedPath(
                DeterministicSimulationUnits.ToFloat(lockedWaterPipeDockDistanceUnits),
                out Vector2 pathPoint,
                out Vector2 tangent))
        {
            ClearWaterPipeDockLock();
            return false;
        }

        float lockedWaterPipeDockDistance =
            DeterministicSimulationUnits.ToFloat(lockedWaterPipeDockDistanceUnits);
        signedPathDelta = lockedWaterPipeDockDistance - currentSample.DistanceAlongPath;
        float captureDistance = ResolveDockCaptureDistance();
        float captureSqrDistance = captureDistance * captureDistance;
        if (Mathf.Abs(signedPathDelta) > captureDistance
            || (pathPoint - currentSample.Point).sqrMagnitude > captureSqrDistance)
        {
            ClearWaterPipeDockLock();
            return false;
        }

        if (!TryGetWaterPipeDockOffsetMetrics(
                pathPoint,
                lockedWaterPipeDockDirectionFromTrainToPipe,
                lockedWaterPipeDockCoordinate,
                out _,
                out _))
        {
            ClearWaterPipeDockLock();
            return false;
        }

        dockSample.Rail = lockedWaterPipeDockRail;
        dockSample.DistanceAlongPath = lockedWaterPipeDockDistance;
        dockSample.Point = pathPoint;
        dockSample.Tangent = tangent;
        dockSample.SqrDistance = (pathPoint - currentSample.Point).sqrMagnitude;
        directionFromTrainToPipe = lockedWaterPipeDockDirectionFromTrainToPipe;
        dockFacing = lockedWaterPipeDockFacing;
        return true;
    }

    private bool TryValidateLockedWaterPipeDock()
    {
        int waterItemId = ResolveWaterItemId();
        return waterItemId >= 0
               && lockedWaterPipeDockDirectionFromTrainToPipe != Vector2Int.zero
               && TryGetActivePipeAtCoordinate(
                   lockedWaterPipeDockCoordinate,
                   out Pipe pipe,
                   out Quaternion pipeRotation,
                   out PipeRuntimeRecord pipeRecord)
               && HasActivePipeConnectionTowards(
                   pipe,
                   pipeRecord,
                   lockedWaterPipeDockCoordinate,
                   pipeRotation,
                   -lockedWaterPipeDockDirectionFromTrainToPipe)
               && CanDeployWaterPipeToNetwork(
                   lockedWaterPipeDockCoordinate,
                   waterItemId);
    }

    private void LockWaterPipeDock(
        RailSample dockSample,
        Vector2 currentFacing,
        Vector2Int directionFromTrainToPipe,
        Vector2Int pipeCoordinate)
    {
        waterPipeDockLockActive = true;
        lockedWaterPipeDockRail = dockSample.Rail;
        lockedWaterPipeDockDistanceUnits = DeterministicSimulationUnits.FromFloat(
            dockSample.DistanceAlongPath);
        lockedWaterPipeDockFacing = ResolveWaterPipeDockFacing(dockSample, currentFacing);
        lockedWaterPipeDockDirectionFromTrainToPipe = directionFromTrainToPipe;
        lockedWaterPipeDockCoordinate = pipeCoordinate;
    }

    private Vector2 ResolveWaterPipeDockFacing(RailSample dockSample, Vector2 currentFacing)
    {
        Vector2 railFacing = dockSample.Tangent;
        if (railFacing.sqrMagnitude <= 0.0001f)
        {
            return currentFacing.sqrMagnitude > 0.0001f
                ? currentFacing.normalized
                : Vector2.up;
        }

        railFacing.Normalize();
        if (currentFacing.sqrMagnitude <= 0.0001f)
        {
            return railFacing;
        }

        float dot = Vector2.Dot(railFacing, currentFacing.normalized);
        if (dot < -WaterPipeDockFacingDotEpsilon)
        {
            railFacing = -railFacing;
        }

        return railFacing;
    }

    private void ClearWaterPipeDockLock()
    {
        waterPipeDockLockActive = false;
        lockedWaterPipeDockRail = null;
        lockedWaterPipeDockDistanceUnits = 0L;
        lockedWaterPipeDockFacing = Vector2.zero;
        lockedWaterPipeDockDirectionFromTrainToPipe = Vector2Int.zero;
        lockedWaterPipeDockCoordinate = Vector2Int.zero;
    }

    private bool TryFindWaterPipeDockSample(
        RailSample currentSample,
        out RailSample dockSample,
        out float signedPathDelta,
        out Vector2Int directionFromTrainToPipe,
        out Vector2Int pipeCoordinate)
    {
        dockSample = default;
        signedPathDelta = 0f;
        directionFromTrainToPipe = Vector2Int.zero;
        pipeCoordinate = Vector2Int.zero;
        if (currentSample.Rail == null)
        {
            return false;
        }

        int waterItemId = ResolveWaterItemId();
        if (waterItemId < 0)
        {
            return false;
        }

        float captureDistance = ResolveDockCaptureDistance();
        float captureSqrDistance = captureDistance * captureDistance;
        float bestScore = float.MaxValue;
        bool found = false;
        int searchCells = Mathf.CeilToInt(ResolveDockSearchRadius());
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(currentSample.Point.x),
            Mathf.RoundToInt(currentSample.Point.y));

        for (int offsetY = -searchCells; offsetY <= searchCells; offsetY++)
        {
            for (int offsetX = -searchCells; offsetX <= searchCells; offsetX++)
            {
                Vector2Int candidatePipeCoordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!TryGetActivePipeAtCoordinate(
                        candidatePipeCoordinate,
                        out Pipe pipe,
                        out Quaternion pipeRotation,
                        out PipeRuntimeRecord pipeRecord))
                {
                    continue;
                }

                bool hasCheckedWaterSource = false;
                bool hasWaterSource = false;
                for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
                {
                    Vector2Int directionFromPipeToTrain = CardinalDirections[directionIndex];
                    if (!HasActivePipeConnectionTowards(
                            pipe,
                            pipeRecord,
                            candidatePipeCoordinate,
                            pipeRotation,
                            directionFromPipeToTrain))
                    {
                        continue;
                    }

                    Vector2Int trainCoordinate = candidatePipeCoordinate + directionFromPipeToTrain;
                    if (!TryFindWaterPipeRailDockSample(
                            trainCoordinate,
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

                    if (!TryGetWaterPipeDockOffsetMetrics(
                            candidateSample.Point,
                            -directionFromPipeToTrain,
                            candidatePipeCoordinate,
                            out float alongDistance,
                            out float lateralDistance))
                    {
                        continue;
                    }

                    if (!hasCheckedWaterSource)
                    {
                        hasWaterSource = CanDeployWaterPipeToNetwork(
                            candidatePipeCoordinate,
                            waterItemId);
                        hasCheckedWaterSource = true;
                    }

                    if (!hasWaterSource)
                    {
                        continue;
                    }

                    float score = candidatePathDistance
                                  + candidateSqrDistance * 0.25f
                                  + Mathf.Abs(1f - alongDistance) * 0.2f
                                  + lateralDistance * 0.35f;
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    dockSample = candidateSample;
                    signedPathDelta = candidatePathDelta;
                    directionFromTrainToPipe = -directionFromPipeToTrain;
                    pipeCoordinate = candidatePipeCoordinate;
                    found = true;
                }
            }
        }

        return found;
    }

    private bool CanDeployWaterPipeToNetwork(
        Vector2Int pipeCoordinate,
        int waterItemId)
    {
        return waterItemId >= 0
               && WaterPipeNetworkHasWaterSource(pipeCoordinate, waterItemId);
    }

    private bool TryFindWaterPipeRailDockSample(
        Vector2Int railCoordinate,
        Railload currentRail,
        out RailSample dockSample)
    {
        dockSample = default;
        if (currentRail == null)
        {
            return false;
        }

        Vector2 railPoint = new Vector2(railCoordinate.x, railCoordinate.y);
        float maxSqrDistance = WaterPipeDockRailCoordinateSampleMaxDistance
                               * WaterPipeDockRailCoordinateSampleMaxDistance;
        if (!currentRail.TryFindNearestRenderedPathSample(
                railPoint,
                out float distanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent,
                out float sqrDistance)
            || sqrDistance > maxSqrDistance)
        {
            return false;
        }

        dockSample.Rail = currentRail;
        dockSample.DistanceAlongPath = distanceAlongPath;
        dockSample.Point = pathPoint;
        dockSample.Tangent = tangent;
        dockSample.SqrDistance = sqrDistance;
        return true;
    }

    private void ResetAutoDriveState()
    {
        autoDriveEnabled = false;
        autoDriveControllerRevision = 0;
        autoDriveTargetAStationName = string.Empty;
        autoDriveTargetBStationName = string.Empty;
        autoDriveTargetAFuelFilter = AutoDriveFuelFilter.Free;
        autoDriveTargetAFreightFilter = AutoDriveFreightFilter.Free;
        autoDriveTargetBFuelFilter = AutoDriveFuelFilter.Free;
        autoDriveTargetBFreightFilter = AutoDriveFreightFilter.Free;
        ResetAutoDriveRuntimeState();
        SetAutoDriveStatus(AutoDriveStatus.Idle, string.Empty, string.Empty);
    }

    private void ClaimAutoDriveControl()
    {
        nextAutoDriveControllerRevision++;
        if (nextAutoDriveControllerRevision == 0)
        {
            nextAutoDriveControllerRevision = 1;
        }

        autoDriveControllerRevision = nextAutoDriveControllerRevision;
    }

    private bool IsPrimaryAutoDriveControllerForConsist()
    {
        if (ResolveAutoDriveControllerForConsist() != this)
        {
            return false;
        }

        for (int i = 0; i < autoDriveConnectedTrainScratch.Count; i++)
        {
            if (autoDriveConnectedTrainScratch[i] is SteamTrain candidate
                && candidate != this
                && candidate.lastManualDriveSimulationTick
                == MapObjectTickManager.CurrentSimulationTick)
            {
                return false;
            }
        }

        return true;
    }

    private SteamTrain ResolveAutoDriveControllerForConsist()
    {
        CollectAutoDriveConnectedTrains();
        SteamTrain controller = null;
        for (int i = 0; i < autoDriveConnectedTrainScratch.Count; i++)
        {
            SteamTrain candidate = autoDriveConnectedTrainScratch[i] as SteamTrain;
            if (candidate == null)
            {
                continue;
            }

            if (!candidate.autoDriveEnabled
                || !candidate.HasCompleteAutoDriveTargets())
            {
                continue;
            }

            if (controller == null
                || candidate.autoDriveControllerRevision > controller.autoDriveControllerRevision
                || (candidate.autoDriveControllerRevision == controller.autoDriveControllerRevision
                    && CompareSimulationOrder(candidate, controller) < 0))
            {
                controller = candidate;
            }
        }

        return controller;
    }

    private void TransferAutoDriveControl(SteamTrain powerSource)
    {
        // Preserve the current leg and station wait while changing the locomotive
        // that actually accelerates, consumes fuel and follows the route.
        TransferConsistPathTo(powerSource);
        powerSource.ApplyAutoDriveState(
            true,
            autoDriveTargetAStationName,
            autoDriveTargetBStationName,
            (int)autoDriveTargetAFuelFilter,
            (int)autoDriveTargetAFreightFilter,
            (int)autoDriveTargetBFuelFilter,
            (int)autoDriveTargetBFreightFilter,
            autoDriveResolvedTargetStationName,
            autoDriveLastArrivedStationName,
            autoDriveStationWaitTimer);
        powerSource.CollectAutoDriveConnectedTrains();
        powerSource.CacheAutoDriveRouteReferenceTrain(autoDriveResolvedTargetStationName, powerSource);
        powerSource.ResetVehicleMotion();
        ResetVehicleMotion();
        StopMovementParticle(false);
        autoDriveEnabled = false;
        autoDriveControllerRevision = 0;
        ResetAutoDriveRuntimeState();
        SetAutoDriveStatus(AutoDriveStatus.Idle, string.Empty, string.Empty);
        PersistAutoDriveState();
        powerSource.PersistAutoDriveState();
    }

    private void ResetAutoDriveRuntimeState()
    {
        autoDriveRouteSegments.Clear();
        autoDriveRouteScratchSegments.Clear();
        autoDriveRouteReferenceScratchSegments.Clear();
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveCachedRouteReferenceTargetStationName = string.Empty;
        autoDriveCachedRouteReferenceTrain = null;
        autoDriveRouteReferenceTrainSimulationId = 0L;
        autoDriveRouteGraphVersion = -1;
        autoDriveRouteRefreshRequested = false;
        autoDriveRouteSegmentCursor = 0;
        autoDriveLastArrivedStationName = string.Empty;
        ClearAutoDriveFixedRoute();
        autoDriveResolvedTargetStationName = string.Empty;
        autoDriveResolvedNextStationName = string.Empty;
        autoDriveResolvedTargetStation = null;
        autoDriveRouteRefreshTimer = 0f;
        autoDriveStationWaitTimer = 0f;
    }

    private void ClearAutoDriveFixedRoute()
    {
        autoDriveFixedRouteSegments.Clear();
        autoDriveFixedRouteStartStationName = string.Empty;
        autoDriveFixedRouteEndStationName = string.Empty;
        autoDriveFixedRouteGraphVersion = -1;
    }

    private bool HasAutoDriveFixedRouteForCurrentTargets()
    {
        return autoDriveFixedRouteSegments.Count > 0
               && autoDriveFixedRouteGraphVersion == AutoDriveRoutePlanner.RouteGraphVersion
               && !string.IsNullOrWhiteSpace(autoDriveTargetAStationName)
               && !string.IsNullOrWhiteSpace(autoDriveTargetBStationName)
               && string.Equals(
                   autoDriveFixedRouteStartStationName,
                   autoDriveTargetAStationName,
                   System.StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   autoDriveFixedRouteEndStationName,
                   autoDriveTargetBStationName,
                   System.StringComparison.OrdinalIgnoreCase);
    }

    private bool TryEnsureAutoDriveFixedRoute()
    {
        if (HasAutoDriveFixedRouteForCurrentTargets())
        {
            return true;
        }

        ClearAutoDriveFixedRoute();
        if (string.IsNullOrWhiteSpace(autoDriveTargetAStationName)
            || string.IsNullOrWhiteSpace(autoDriveTargetBStationName)
            || !AutoDriveRoutePlanner.TryFindStationByName(autoDriveTargetAStationName, out Trainstation startStation)
            || !AutoDriveRoutePlanner.TryFindStationByName(autoDriveTargetBStationName, out Trainstation destinationStation)
            || !AutoDriveRoutePlanner.TryBuildRoute(
                startStation,
                destinationStation,
                autoDriveFixedRouteSegments))
        {
            ClearAutoDriveFixedRoute();
            return false;
        }

        autoDriveFixedRouteStartStationName = autoDriveTargetAStationName;
        autoDriveFixedRouteEndStationName = autoDriveTargetBStationName;
        autoDriveFixedRouteGraphVersion = AutoDriveRoutePlanner.RouteGraphVersion;
        return autoDriveFixedRouteSegments.Count > 0;
    }

    private bool TryBuildActiveRouteFromFixedRoute(
        RailHandcar routeReferenceTrain,
        string targetStationName,
        List<AutoDriveRoutePlanner.RouteSegment> result)
    {
        if (result == null)
        {
            return false;
        }

        result.Clear();
        if (routeReferenceTrain == null
            || !TryEnsureAutoDriveFixedRoute())
        {
            return false;
        }

        bool useForwardRoute;
        if (string.Equals(
                targetStationName,
                autoDriveFixedRouteEndStationName,
                System.StringComparison.OrdinalIgnoreCase))
        {
            useForwardRoute = true;
        }
        else if (string.Equals(
                     targetStationName,
                     autoDriveFixedRouteStartStationName,
                     System.StringComparison.OrdinalIgnoreCase))
        {
            useForwardRoute = false;
        }
        else
        {
            return false;
        }

        if (useForwardRoute)
        {
            for (int i = 0; i < autoDriveFixedRouteSegments.Count; i++)
            {
                result.Add(autoDriveFixedRouteSegments[i]);
            }
        }
        else
        {
            for (int i = autoDriveFixedRouteSegments.Count - 1; i >= 0; i--)
            {
                AutoDriveRoutePlanner.RouteSegment segment = autoDriveFixedRouteSegments[i];
                result.Add(
                    new AutoDriveRoutePlanner.RouteSegment(
                        segment.Rail,
                        segment.EndDistance,
                        segment.StartDistance));
            }
        }

        return TryAlignAutoDriveRouteSegmentsToCurrentPose(routeReferenceTrain, result)
               && AutoDriveRoutePlanner.IsForwardRoute(routeReferenceTrain, result);
    }

    private bool TryAlignAutoDriveRouteSegmentsToCurrentPose(
        RailHandcar routeReferenceTrain,
        List<AutoDriveRoutePlanner.RouteSegment> segments)
    {
        if (routeReferenceTrain == null
            || segments == null
            || segments.Count <= 0
            || !routeReferenceTrain.TryGetCurrentRailPose(
                out Railload currentRail,
                out float currentDistanceAlongPath,
                out _,
                out _)
            || currentRail == null)
        {
            return false;
        }

        int matchedSegmentIndex = -1;
        bool matchedWithinSegment = false;
        float bestDistanceToSegment = float.PositiveInfinity;
        float maxReconnectDistance = Mathf.Max(
            AutoDriveRouteSegmentTolerance * 4f,
            ResolveDockCaptureDistance());
        for (int i = 0; i < segments.Count; i++)
        {
            AutoDriveRoutePlanner.RouteSegment segment = segments[i];
            if (segment.Rail != currentRail)
            {
                continue;
            }

            float minDistance =
                Mathf.Min(segment.StartDistance, segment.EndDistance) - AutoDriveRouteSegmentTolerance;
            float maxDistance =
                Mathf.Max(segment.StartDistance, segment.EndDistance) + AutoDriveRouteSegmentTolerance;
            if (currentDistanceAlongPath >= minDistance && currentDistanceAlongPath <= maxDistance)
            {
                matchedSegmentIndex = i;
                matchedWithinSegment = true;
                break;
            }

            float distanceToSegment = currentDistanceAlongPath < minDistance
                ? minDistance - currentDistanceAlongPath
                : currentDistanceAlongPath - maxDistance;
            if (distanceToSegment >= bestDistanceToSegment)
            {
                continue;
            }

            bestDistanceToSegment = distanceToSegment;
            matchedSegmentIndex = i;
        }

        if (matchedSegmentIndex < 0
            || (!matchedWithinSegment && bestDistanceToSegment > maxReconnectDistance))
        {
            segments.Clear();
            return false;
        }

        if (matchedSegmentIndex > 0)
        {
            segments.RemoveRange(0, matchedSegmentIndex);
        }

        if (segments.Count <= 0)
        {
            return false;
        }

        AutoDriveRoutePlanner.RouteSegment firstSegment = segments[0];
        if (!matchedWithinSegment)
        {
            currentDistanceAlongPath = Mathf.Clamp(
                currentDistanceAlongPath,
                Mathf.Min(firstSegment.StartDistance, firstSegment.EndDistance),
                Mathf.Max(firstSegment.StartDistance, firstSegment.EndDistance));
        }

        if (Mathf.Abs(firstSegment.EndDistance - currentDistanceAlongPath) <= 0.0001f)
        {
            segments.RemoveAt(0);
            return segments.Count > 0;
        }

        segments[0] = new AutoDriveRoutePlanner.RouteSegment(
            firstSegment.Rail,
            currentDistanceAlongPath,
            firstSegment.EndDistance);
        return true;
    }

    private void SetAutoDriveStatus(
        AutoDriveStatus status,
        string currentTargetStationName,
        string nextTargetStationName)
    {
#if UNITY_EDITOR
        string normalizedCurrentTarget = NormalizeAutoDriveStationName(currentTargetStationName);
        string normalizedNextTarget = NormalizeAutoDriveStationName(nextTargetStationName);
        if (autoDriveStatus != status
            || !string.Equals(
                autoDriveCurrentTargetStationName,
                normalizedCurrentTarget,
                System.StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                autoDriveNextTargetStationName,
                normalizedNextTarget,
                System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log(
                $"[AutoDriveState] train={name} state={autoDriveStatus}->{status} "
                + $"target='{normalizedCurrentTarget}' next='{normalizedNextTarget}' "
                + $"enabled={autoDriveEnabled} position={transform.position}",
                this);
        }
#endif
        autoDriveStatus = status;
        autoDriveCurrentTargetStationName = NormalizeAutoDriveStationName(currentTargetStationName);
        autoDriveNextTargetStationName = NormalizeAutoDriveStationName(nextTargetStationName);
    }

    private string ResolveAutoDriveStatusText()
    {
        if (!autoDriveEnabled)
        {
            return "AutoDrive: Off";
        }

        string targetSuffix = !string.IsNullOrWhiteSpace(autoDriveCurrentTargetStationName)
            ? $" [{autoDriveCurrentTargetStationName}]"
            : string.Empty;
        return autoDriveStatus switch
        {
            AutoDriveStatus.NoTarget => "AutoDrive: No Target",
            AutoDriveStatus.Planning => $"AutoDrive: Planning{targetSuffix}",
            AutoDriveStatus.Moving => $"AutoDrive: Moving{targetSuffix}",
            AutoDriveStatus.Docking => $"AutoDrive: Docking{targetSuffix}",
            AutoDriveStatus.WaitingAtStation => $"AutoDrive: Waiting{targetSuffix}",
            AutoDriveStatus.WaitingForFuel => $"AutoDrive: Waiting Fuel{targetSuffix}",
            AutoDriveStatus.WaitingForDepartureFuel => $"AutoDrive: Waiting Full Fuel{targetSuffix}",
            AutoDriveStatus.WaitingForWater => $"AutoDrive: Waiting Water{targetSuffix}",
            AutoDriveStatus.WaitingForFreight => $"AutoDrive: Waiting Freight{targetSuffix}",
            AutoDriveStatus.WaitingForPath => $"AutoDrive: No Path{targetSuffix}",
            AutoDriveStatus.WaitingForClearTrack => $"AutoDrive: Track Busy{targetSuffix}",
            AutoDriveStatus.Arrived => $"AutoDrive: Arrived{targetSuffix}",
            _ => "AutoDrive: Ready"
        };
    }

    public void GetObjectInfoStatus(out string statusText, out InfoWarning warning)
    {
        SteamTrain controller = AutoDriveSettingsOwner;
        warning = InfoWarning.None;
        if (!controller.autoDriveEnabled)
        {
            statusText = Mathf.Abs(CurrentVehicleSignedSpeed) > 0.0001f
                ? "Manual driving" : "Stopped: Auto-drive off";
            return;
        }

        switch (controller.autoDriveStatus)
        {
            case AutoDriveStatus.WaitingAtStation:
                statusText = "Waiting: Station wait";
                warning = InfoWarning.DepartureCondition;
                return;
            case AutoDriveStatus.WaitingForDepartureFuel:
                statusText = "Waiting: Full fuel";
                warning = InfoWarning.DepartureCondition;
                return;
            case AutoDriveStatus.WaitingForFreight:
                controller.ResolveAutoDriveDepartureFilters(out _, out AutoDriveFreightFilter freightFilter);
                statusText = freightFilter == AutoDriveFreightFilter.Empty
                    ? "Waiting: Empty freight" : "Waiting: Full freight";
                warning = InfoWarning.DepartureCondition;
                return;
            case AutoDriveStatus.WaitingForFuel:
                statusText = "Resource shortage: Fuel";
                warning = InfoWarning.ResourceShortage;
                return;
            case AutoDriveStatus.WaitingForWater:
                statusText = "Resource shortage: Water";
                warning = InfoWarning.ResourceShortage;
                return;
            case AutoDriveStatus.WaitingForPath:
                statusText = "Stopped: No available route";
                return;
            case AutoDriveStatus.WaitingForClearTrack:
                statusText = "Stopped: Waiting for clear track";
                return;
            case AutoDriveStatus.NoTarget:
                statusText = "Stopped: No destination";
                return;
            case AutoDriveStatus.Planning:
                statusText = "Planning route";
                return;
            case AutoDriveStatus.Moving:
                statusText = "Auto-driving";
                return;
            case AutoDriveStatus.Docking:
                statusText = "Approaching station";
                return;
            case AutoDriveStatus.Arrived:
                statusText = "Stopped: Arrived at destination";
                return;
            default:
                statusText = "Preparing departure";
                return;
        }
    }

    private static string NormalizeAutoDriveStationName(string stationName)
    {
        return string.IsNullOrWhiteSpace(stationName)
            ? string.Empty
            : stationName.Trim();
    }

    private bool HasCompleteAutoDriveTargets()
    {
        return HasCompleteAutoDriveTargets(autoDriveTargetAStationName, autoDriveTargetBStationName);
    }

    private static bool HasCompleteAutoDriveTargets(string targetAStationName, string targetBStationName)
    {
        return !string.IsNullOrWhiteSpace(targetAStationName)
               && !string.IsNullOrWhiteSpace(targetBStationName)
               && !string.Equals(
                   targetAStationName,
                   targetBStationName,
                   System.StringComparison.OrdinalIgnoreCase);
    }

    private static AutoDriveFuelFilter ParseAutoDriveFuelFilter(string filterName)
    {
        return System.Enum.TryParse(filterName, true, out AutoDriveFuelFilter filter)
            ? filter
            : AutoDriveFuelFilter.Free;
    }

    private static AutoDriveFreightFilter ParseAutoDriveFreightFilter(string filterName)
    {
        return System.Enum.TryParse(filterName, true, out AutoDriveFreightFilter filter)
            ? filter
            : AutoDriveFreightFilter.Free;
    }

    private static AutoDriveFuelFilter ClampAutoDriveFuelFilter(int filter)
    {
        return filter == (int)AutoDriveFuelFilter.Full
            ? AutoDriveFuelFilter.Full
            : AutoDriveFuelFilter.Free;
    }

    private static AutoDriveFreightFilter ClampAutoDriveFreightFilter(int filter)
    {
        return filter switch
        {
            (int)AutoDriveFreightFilter.Full => AutoDriveFreightFilter.Full,
            (int)AutoDriveFreightFilter.Empty => AutoDriveFreightFilter.Empty,
            _ => AutoDriveFreightFilter.Free
        };
    }

    private void PersistAutoDriveState()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        TerrainGenerator.ResolveActive()?.SaveRuntimeInstallationState(this);
    }

    private Vector3 ResolveAutoDriveMoveDirection(float deltaTime, out SteamTrain powerSource)
    {
        powerSource = null;
        if (!TryResolveAutoDriveTargets(
                out Trainstation targetStation,
                out string targetStationName,
                out string nextTargetStationName))
        {
            autoDriveResolvedTargetStation = null;
            autoDriveResolvedTargetStationName = string.Empty;
            autoDriveResolvedNextStationName = string.Empty;
            autoDriveRouteSegments.Clear();
            autoDriveRouteTargetStationName = string.Empty;
            autoDriveRouteGraphVersion = -1;
            autoDriveRouteRefreshRequested = false;
            SetAutoDriveStatus(
                HasAnyAutoDriveTarget ? AutoDriveStatus.WaitingForPath : AutoDriveStatus.NoTarget,
                string.Empty,
                string.Empty);
            return Vector3.zero;
        }

        autoDriveResolvedTargetStation = targetStation;
        autoDriveResolvedTargetStationName = targetStationName;
        autoDriveResolvedNextStationName = nextTargetStationName;

        powerSource = ResolveAutoDriveRouteReferenceTrain(targetStation, targetStationName) as SteamTrain;
        if (powerSource != this)
        {
            if (powerSource == null)
            {
                SetAutoDriveStatus(AutoDriveStatus.WaitingForPath, targetStationName, nextTargetStationName);
            }

            return Vector3.zero;
        }

        bool hasDockDistance = TryGetAutoDriveTargetDockDistance(
            targetStation,
            out float remainingDockDistance);
        if (hasDockDistance && remainingDockDistance <= ResolveAutoDriveArrivalSnapDistance())
        {
            if (TrySnapAutoDriveToTargetDock(targetStation, deltaTime))
            {
                HandleAutoDriveArrived(targetStationName, nextTargetStationName);
            }

            return Vector3.zero;
        }

        if (autoDriveStationWaitTimer > 0f)
        {
            autoDriveStationWaitTimer = Mathf.Max(0f, autoDriveStationWaitTimer - Mathf.Max(0f, deltaTime));
            SetAutoDriveStatus(
                AutoDriveStatus.WaitingAtStation,
                autoDriveLastArrivedStationName,
                targetStationName);
            return Vector3.zero;
        }

        ResolveAutoDriveDepartureFilters(
            out AutoDriveFuelFilter departureFuelFilter,
            out AutoDriveFreightFilter departureFreightFilter);
        if (!TryEvaluateAutoDriveFuelFilterSatisfied(departureFuelFilter))
        {
            SetAutoDriveStatus(
                AutoDriveStatus.WaitingForDepartureFuel,
                autoDriveLastArrivedStationName,
                targetStationName);
            return Vector3.zero;
        }

        if (!TryEvaluateAutoDriveFreightFilterSatisfied(departureFreightFilter))
        {
            SetAutoDriveStatus(
                AutoDriveStatus.WaitingForFreight,
                autoDriveLastArrivedStationName,
                targetStationName);
            return Vector3.zero;
        }

        if (!TryEnsureAutoDriveRoute(targetStation, targetStationName, deltaTime))
        {
            SetAutoDriveStatus(AutoDriveStatus.WaitingForPath, targetStationName, nextTargetStationName);
            return Vector3.zero;
        }

        if (!TryResolveAutoDriveRouteMoveDirection(out Vector3 moveDirection))
        {
            autoDriveRouteRefreshRequested = true;
            autoDriveRouteRefreshTimer = 0f;
            SetAutoDriveStatus(AutoDriveStatus.WaitingForPath, targetStationName, nextTargetStationName);
            return Vector3.zero;
        }

        bool isDockingApproach =
            hasDockDistance
            && TryApplyAutoDriveDockApproachSpeed(
                ref moveDirection,
                remainingDockDistance);
        SetAutoDriveStatus(
            isDockingApproach ? AutoDriveStatus.Docking : AutoDriveStatus.Moving,
            targetStationName,
            nextTargetStationName);
        return moveDirection;
    }

    private bool TryResolveAutoDriveTargets(
        out Trainstation targetStation,
        out string targetStationName,
        out string nextTargetStationName)
    {
        targetStation = null;
        targetStationName = string.Empty;
        nextTargetStationName = string.Empty;

        string targetA = autoDriveTargetAStationName;
        string targetB = autoDriveTargetBStationName;
        bool hasTargetA = !string.IsNullOrWhiteSpace(targetA);
        bool hasTargetB = !string.IsNullOrWhiteSpace(targetB);
        if (!hasTargetA && !hasTargetB)
        {
            return false;
        }

        if (hasTargetA && !hasTargetB)
        {
            if (string.Equals(autoDriveLastArrivedStationName, targetA, System.StringComparison.OrdinalIgnoreCase))
            {
                SetAutoDriveStatus(AutoDriveStatus.Arrived, targetA, string.Empty);
                return false;
            }

            nextTargetStationName = string.Empty;
            targetStationName = targetA;
            return AutoDriveRoutePlanner.TryFindStationByName(targetStationName, out targetStation);
        }

        if (!hasTargetA && hasTargetB)
        {
            if (string.Equals(autoDriveLastArrivedStationName, targetB, System.StringComparison.OrdinalIgnoreCase))
            {
                SetAutoDriveStatus(AutoDriveStatus.Arrived, targetB, string.Empty);
                return false;
            }

            nextTargetStationName = string.Empty;
            targetStationName = targetB;
            return AutoDriveRoutePlanner.TryFindStationByName(targetStationName, out targetStation);
        }

        if (string.Equals(autoDriveLastArrivedStationName, targetA, System.StringComparison.OrdinalIgnoreCase))
        {
            targetStationName = targetB;
            nextTargetStationName = targetA;
            return AutoDriveRoutePlanner.TryFindStationByName(targetStationName, out targetStation);
        }

        if (string.Equals(autoDriveLastArrivedStationName, targetB, System.StringComparison.OrdinalIgnoreCase))
        {
            targetStationName = targetA;
            nextTargetStationName = targetB;
            return AutoDriveRoutePlanner.TryFindStationByName(targetStationName, out targetStation);
        }

        if (!string.IsNullOrWhiteSpace(autoDriveRouteTargetStationName))
        {
            if (string.Equals(autoDriveRouteTargetStationName, targetA, System.StringComparison.OrdinalIgnoreCase)
                && AutoDriveRoutePlanner.TryFindStationByName(targetA, out targetStation))
            {
                targetStationName = targetA;
                nextTargetStationName = targetB;
                return true;
            }

            if (string.Equals(autoDriveRouteTargetStationName, targetB, System.StringComparison.OrdinalIgnoreCase)
                && AutoDriveRoutePlanner.TryFindStationByName(targetB, out targetStation))
            {
                targetStationName = targetB;
                nextTargetStationName = targetA;
                return true;
            }
        }

        bool foundTargetA = AutoDriveRoutePlanner.TryFindStationByName(targetA, out Trainstation stationA);
        bool foundTargetB = AutoDriveRoutePlanner.TryFindStationByName(targetB, out Trainstation stationB);
        if (!foundTargetA && !foundTargetB)
        {
            return false;
        }

        if (!foundTargetA)
        {
            targetStation = stationB;
            targetStationName = targetB;
            nextTargetStationName = targetA;
            return true;
        }

        if (!foundTargetB)
        {
            targetStation = stationA;
            targetStationName = targetA;
            nextTargetStationName = targetB;
            return true;
        }

        float routeLengthToA = TryBuildRouteLengthToStation(targetA, stationA, out float lengthToA)
            ? lengthToA
            : float.PositiveInfinity;
        float routeLengthToB = TryBuildRouteLengthToStation(targetB, stationB, out float lengthToB)
            ? lengthToB
            : float.PositiveInfinity;
        if (float.IsPositiveInfinity(routeLengthToA)
            && float.IsPositiveInfinity(routeLengthToB))
        {
            return false;
        }

        if (routeLengthToA <= routeLengthToB)
        {
            targetStation = stationA;
            targetStationName = targetA;
            nextTargetStationName = targetB;
            return true;
        }

        targetStation = stationB;
        targetStationName = targetB;
        nextTargetStationName = targetA;
        return true;
    }

    private bool TryBuildRouteLengthToStation(
        string targetStationName,
        Trainstation station,
        out float routeLength)
    {
        routeLength = float.PositiveInfinity;
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain(
            station,
            targetStationName);
        if (routeReferenceTrain == null)
        {
            return false;
        }

        autoDriveRouteScratchSegments.Clear();
        bool hasFixedRoute = TryEnsureAutoDriveFixedRoute();
        bool builtRoute = hasFixedRoute
            ? TryBuildActiveRouteFromFixedRoute(
                routeReferenceTrain,
                targetStationName,
                autoDriveRouteScratchSegments)
            : AutoDriveRoutePlanner.TryBuildRoute(
                routeReferenceTrain,
                station,
                autoDriveRouteScratchSegments);
        if (!builtRoute)
        {
            autoDriveRouteScratchSegments.Clear();
            return false;
        }

        routeLength = AutoDriveRoutePlanner.GetRouteLength(autoDriveRouteScratchSegments);
        autoDriveRouteScratchSegments.Clear();
        return true;
    }

    private bool IsWithinAutoDriveArrivalSnapDistance(Trainstation targetStation)
    {
        return TryGetAutoDriveTargetDockDistance(targetStation, out float remainingDistance)
               && remainingDistance <= ResolveAutoDriveArrivalSnapDistance();
    }

    private void ResolveAutoDriveDepartureFilters(
        out AutoDriveFuelFilter fuelFilter,
        out AutoDriveFreightFilter freightFilter)
    {
        if (string.Equals(
                autoDriveLastArrivedStationName,
                autoDriveTargetAStationName,
                System.StringComparison.OrdinalIgnoreCase))
        {
            fuelFilter = autoDriveTargetAFuelFilter;
            freightFilter = autoDriveTargetAFreightFilter;
            return;
        }

        if (string.Equals(
                autoDriveLastArrivedStationName,
                autoDriveTargetBStationName,
                System.StringComparison.OrdinalIgnoreCase))
        {
            fuelFilter = autoDriveTargetBFuelFilter;
            freightFilter = autoDriveTargetBFreightFilter;
            return;
        }

        fuelFilter = AutoDriveFuelFilter.Free;
        freightFilter = AutoDriveFreightFilter.Free;
    }

    private bool TryEvaluateAutoDriveFuelFilterSatisfied(AutoDriveFuelFilter fuelFilter)
    {
        if (fuelFilter != AutoDriveFuelFilter.Full || IsFreeTrainEnabled())
        {
            return true;
        }

        if (!TryGetRearFreightCar(out FreightCar freightCar))
        {
            return false;
        }

        freightCar.GetAutoDriveStorageSummary(
            IsUsableBurnEnergyItem,
            out int storedFuelCount,
            out int fuelCapacity,
            out bool hasFuelStorage);
        return hasFuelStorage
               && fuelCapacity > 0
               && storedFuelCount >= fuelCapacity;
    }

    private bool TryEvaluateAutoDriveFreightFilterSatisfied(AutoDriveFreightFilter freightFilter)
    {
        // Free is an unrestricted departure, including full or absent freight storage.
        if (freightFilter == AutoDriveFreightFilter.Free)
        {
            return true;
        }

        CollectAutoDriveConnectedTrains();
        // Reserve every locomotive's fuel supplier, including the return-trip engine.
        // Identify the car by its supply role, not by the items currently loaded in it.
        autoDriveFuelFreightCarScratch.Clear();
        for (int i = 0; i < autoDriveConnectedTrainScratch.Count; i++)
        {
            if (autoDriveConnectedTrainScratch[i] is SteamTrain engine
                && engine != null
                && engine.gameObject.activeInHierarchy
                && engine.TryGetRearFreightCar(out FreightCar fuelCar))
            {
                autoDriveFuelFreightCarScratch.Add(fuelCar);
            }
        }

        int totalItemCount = 0;
        int totalCapacity = 0;
        bool hasStorage = false;
        for (int i = 0; i < autoDriveConnectedTrainScratch.Count; i++)
        {
            if (autoDriveConnectedTrainScratch[i] is not FreightCar freightCar
                || freightCar == null
                || !freightCar.gameObject.activeInHierarchy
                || autoDriveFuelFreightCarScratch.Contains(freightCar))
            {
                continue;
            }

            freightCar.GetAutoDriveStorageSummary(
                out int itemCount,
                out int capacity,
                out bool freightHasStorage);
            totalItemCount += Mathf.Max(0, itemCount);
            totalCapacity += Mathf.Max(0, capacity);
            hasStorage |= freightHasStorage;
        }

        if (!hasStorage || totalCapacity <= 0)
        {
            // A fuel-only consist has no cargo to unload, but cannot be cargo-full.
            return freightFilter == AutoDriveFreightFilter.Empty;
        }

        return freightFilter switch
        {
            AutoDriveFreightFilter.Full => totalItemCount >= totalCapacity,
            AutoDriveFreightFilter.Empty => totalItemCount <= 0,
            _ => false
        };
    }

    private void HandleAutoDriveArrived(string currentStationName, string nextTargetStationName)
    {
        ResetVehicleMotion();
        autoDriveLastArrivedStationName = NormalizeAutoDriveStationName(currentStationName);
        autoDriveRouteSegments.Clear();
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveRouteReferenceTrainSimulationId = 0L;
        autoDriveRouteGraphVersion = -1;
        autoDriveRouteRefreshRequested = false;
        autoDriveRouteSegmentCursor = 0;
        autoDriveRouteRefreshTimer = 0f;
        autoDriveStationWaitTimer = !string.IsNullOrWhiteSpace(nextTargetStationName)
            ? AutoDriveWaitDurationSeconds
            : 0f;
        SetAutoDriveStatus(
            autoDriveStationWaitTimer > 0f ? AutoDriveStatus.WaitingAtStation : AutoDriveStatus.Arrived,
            currentStationName,
            nextTargetStationName);
    }

    private bool TrySnapAutoDriveToTargetDock(Trainstation targetStation, float deltaTime)
    {
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain(targetStation);
        if (targetStation == null
            || routeReferenceTrain == null
            || !targetStation.TryGetRailCoordinate(out Vector2Int railCoordinate))
        {
            return false;
        }

        return routeReferenceTrain.TryDockConnectedTrainGroupAtRailCoordinate(
            railCoordinate,
            deltaTime,
            true);
    }

    private bool TryEnsureAutoDriveRoute(Trainstation targetStation, string targetStationName, float deltaTime)
    {
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain(
            targetStation,
            targetStationName);
        bool hasFixedRoute = TryEnsureAutoDriveFixedRoute();
        int routeGraphVersion = AutoDriveRoutePlanner.RouteGraphVersion;
        autoDriveRouteRefreshTimer = Mathf.Max(0f, autoDriveRouteRefreshTimer - Mathf.Max(0f, deltaTime));
        if (targetStation != null
            && autoDriveRouteSegments.Count > 0
            && !autoDriveRouteRefreshRequested
            && autoDriveRouteGraphVersion == routeGraphVersion
            && !HasAutoDriveRouteReferenceChanged(routeReferenceTrain)
            && string.Equals(autoDriveRouteTargetStationName, targetStationName, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (autoDriveRouteSegments.Count <= 0
            && autoDriveRouteRefreshTimer > 0f
            && autoDriveRouteGraphVersion == routeGraphVersion)
        {
            return false;
        }

        autoDriveRouteSegments.Clear();
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveRouteReferenceTrainSimulationId = 0L;
        autoDriveRouteGraphVersion = -1;
        autoDriveRouteSegmentCursor = 0;
        autoDriveRouteRefreshTimer = AutoDriveRouteRefreshInterval;
        if (targetStation == null || routeReferenceTrain == null)
        {
            autoDriveRouteGraphVersion = routeGraphVersion;
            return false;
        }

        bool builtRoute = hasFixedRoute
            ? TryBuildActiveRouteFromFixedRoute(
                routeReferenceTrain,
                targetStationName,
                autoDriveRouteSegments)
            : AutoDriveRoutePlanner.TryBuildRoute(
                routeReferenceTrain,
                targetStation,
                autoDriveRouteSegments);
        if (!builtRoute)
        {
            autoDriveRouteGraphVersion = routeGraphVersion;
            return false;
        }

        autoDriveRouteSegmentCursor = 0;
        autoDriveRouteTargetStationName = targetStationName;
        autoDriveRouteReferenceTrainSimulationId = routeReferenceTrain.SimulationId;
        autoDriveRouteGraphVersion = routeGraphVersion;
        autoDriveRouteRefreshRequested = false;
        autoDriveRouteRefreshTimer = 0f;
        return autoDriveRouteSegments.Count > 0;
    }

    private bool TryGetAutoDriveTargetDockDistance(
        Trainstation targetStation,
        out float remainingDistance)
    {
        if (!TryGetAutoDriveTargetDockDelta(targetStation, out float signedPathDelta))
        {
            remainingDistance = 0f;
            return false;
        }

        remainingDistance = Mathf.Abs(signedPathDelta);
        return true;
    }

    private bool TryGetAutoDriveTargetDockDelta(
        Trainstation targetStation,
        out float signedPathDelta)
    {
        signedPathDelta = 0f;
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain(targetStation);
        return targetStation != null
               && routeReferenceTrain != null
               && targetStation.TryGetRailCoordinate(out Vector2Int railCoordinate)
               && routeReferenceTrain.TryGetRailDockDeltaAtCoordinate(
                   railCoordinate,
                   out signedPathDelta);
    }

    private bool TryGetAutoDriveTargetDockPathDelta(
        Trainstation targetStation,
        out float signedPathDelta,
        out Vector2 dockTravelDirection)
    {
        signedPathDelta = 0f;
        dockTravelDirection = Vector2.zero;
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain(targetStation);
        if (targetStation == null
            || routeReferenceTrain == null
            || !targetStation.TryGetRailCoordinate(out Vector2Int railCoordinate)
            || !routeReferenceTrain.TryGetRailDockDeltaAtCoordinate(
                railCoordinate,
                out signedPathDelta)
            || !routeReferenceTrain.TryGetCurrentRailPose(
                out Railload currentRail,
                out float currentDistanceAlongPath,
                out _,
                out Vector2 fallbackTangent)
            || currentRail == null)
        {
            return false;
        }

        Vector2 pathTangent = Vector2.zero;
        if (!currentRail.TrySampleRenderedPath(
                currentDistanceAlongPath,
                out _,
                out pathTangent)
            || pathTangent.sqrMagnitude <= 0.0001f)
        {
            pathTangent = fallbackTangent;
        }

        if (pathTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        pathTangent.Normalize();
        dockTravelDirection = signedPathDelta >= 0f
            ? pathTangent
            : -pathTangent;
        return true;
    }

    private static bool TryResolveAutoDriveDockSignedStep(
        Vector2 currentFacing,
        Vector2 dockTravelDirection,
        float remainingDistance,
        float requestedSignedStep,
        out float dockSignedStep)
    {
        dockSignedStep = 0f;
        if (currentFacing.sqrMagnitude <= 0.0001f
            || dockTravelDirection.sqrMagnitude <= 0.0001f
            || remainingDistance <= 0.0001f
            || requestedSignedStep <= 0.0001f)
        {
            return false;
        }

        float facingDot = Vector2.Dot(
            currentFacing.normalized,
            dockTravelDirection.normalized);
        if (facingDot <= 0.0001f)
        {
            return false;
        }

        dockSignedStep = Mathf.Min(requestedSignedStep, remainingDistance);
        return true;
    }

    private bool TryApplyAutoDriveDockApproachSpeed(
        ref Vector3 moveDirection,
        float remainingDistance)
    {
        if (moveDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float effectiveMaxSpeed = Mathf.Max(0.01f, EffectiveVehicleMaxSpeed);
        float distanceUntilComplete = Mathf.Max(
            0f,
            remainingDistance - ResolveAutoDriveArrivalSnapDistance());
        float desiredSpeed = Mathf.Sqrt(
            2f
            * Mathf.Max(0.01f, VehicleDecelerationPerSecond)
            * distanceUntilComplete);
        desiredSpeed = Mathf.Min(effectiveMaxSpeed, desiredSpeed);

        float inputMagnitude = desiredSpeed / effectiveMaxSpeed;
        if (remainingDistance > ResolveDockCompleteDistance())
        {
            inputMagnitude = Mathf.Max(
                AutoDriveDockApproachMinInputMagnitude,
                inputMagnitude);
        }

        bool isDockingApproach =
            remainingDistance <= ResolveAutoDriveDockApproachDistance()
            || inputMagnitude < 0.999f;
        if (!isDockingApproach)
        {
            return false;
        }

        moveDirection = moveDirection.normalized * Mathf.Clamp01(inputMagnitude);
        return true;
    }

    private bool TryResolveAutoDriveRouteMoveDirection(out Vector3 moveDirection)
    {
        moveDirection = Vector3.zero;
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain(
            autoDriveResolvedTargetStation,
            autoDriveRouteTargetStationName);
        if (autoDriveRouteSegments.Count <= 0
            || routeReferenceTrain == null
            || !routeReferenceTrain.TryGetCurrentRailPose(
                out Railload currentRail,
                out float currentDistanceAlongPath,
                out Vector2 currentPathPoint,
                out _))
        {
            return false;
        }

        if (TryResolveAutoDriveDockMoveDirection(out moveDirection))
        {
            return true;
        }

        ReconcileAutoDriveRouteCursor(currentRail, currentDistanceAlongPath);
        int currentSegmentIndex = Mathf.Clamp(
            autoDriveRouteSegmentCursor,
            0,
            autoDriveRouteSegments.Count - 1);

        AutoDriveRoutePlanner.RouteSegment currentSegment = autoDriveRouteSegments[currentSegmentIndex];
        float startDistance = currentSegment.StartDistance;
        float endDistance = currentSegment.EndDistance;
        float directionSign = Mathf.Sign(endDistance - startDistance);
        if (Mathf.Abs(directionSign) <= 0.0001f)
        {
            return false;
        }

        currentDistanceAlongPath = ClampDistanceAlongSegment(currentSegment, currentDistanceAlongPath);
        float minDistance = Mathf.Min(startDistance, endDistance);
        float maxDistance = Mathf.Max(startDistance, endDistance);
        float remainingDistance = directionSign > 0f
            ? maxDistance - currentDistanceAlongPath
            : currentDistanceAlongPath - minDistance;
        float branchSteerDistance = ResolveAutoDriveBranchSteerDistance(routeReferenceTrain);

        Railload desiredRail = currentSegment.Rail;
        float desiredDirectionSign = directionSign;
        float desiredDistanceAlongPath;
        if (remainingDistance <= branchSteerDistance
            && currentSegmentIndex + 1 < autoDriveRouteSegments.Count)
        {
            AutoDriveRoutePlanner.RouteSegment nextSegment = autoDriveRouteSegments[currentSegmentIndex + 1];
            desiredRail = nextSegment.Rail;
            desiredDirectionSign = Mathf.Sign(nextSegment.EndDistance - nextSegment.StartDistance);
            desiredDistanceAlongPath = nextSegment.StartDistance + desiredDirectionSign * Mathf.Min(AutoDriveBranchLookAheadDistance, Mathf.Max(0.05f, nextSegment.Length));
            desiredDistanceAlongPath = Mathf.Clamp(
                desiredDistanceAlongPath,
                Mathf.Min(nextSegment.StartDistance, nextSegment.EndDistance),
                Mathf.Max(nextSegment.StartDistance, nextSegment.EndDistance));
        }
        else
        {
            desiredDistanceAlongPath = Mathf.Clamp(
                currentDistanceAlongPath + directionSign * AutoDriveLookAheadDistance,
                minDistance,
                maxDistance);
        }

        if (desiredRail == null
            || !desiredRail.TrySampleRenderedPath(desiredDistanceAlongPath, out Vector2 desiredPoint, out Vector2 desiredTangent))
        {
            desiredRail = currentSegment.Rail;
            desiredDirectionSign = directionSign;
            desiredDistanceAlongPath = Mathf.Clamp(
                currentDistanceAlongPath + directionSign * AutoDriveLookAheadDistance,
                minDistance,
                maxDistance);
            if (desiredRail == null
                || !desiredRail.TrySampleRenderedPath(
                    desiredDistanceAlongPath,
                    out desiredPoint,
                    out desiredTangent))
            {
                return false;
            }
        }

        Vector2 desiredDirection = desiredPoint - currentPathPoint;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
        {
            desiredDirection = desiredTangent * desiredDirectionSign;
        }

        if (desiredDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        desiredDirection.Normalize();
        moveDirection = new Vector3(desiredDirection.x, 0f, desiredDirection.y);
        return true;
    }

    private bool TryResolveAutoDriveDockMoveDirection(out Vector3 moveDirection)
    {
        moveDirection = Vector3.zero;
        if (autoDriveResolvedTargetStation == null
            || !TryGetAutoDriveTargetDockPathDelta(
                autoDriveResolvedTargetStation,
                out float signedDockDelta,
                out Vector2 dockDirection))
        {
            return false;
        }

        float remainingDistance = Mathf.Abs(signedDockDelta);
        if (remainingDistance <= ResolveDockCompleteDistance()
            || remainingDistance > ResolveAutoDriveDockApproachDistance())
        {
            return false;
        }

        if (dockDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        dockDirection.Normalize();
        moveDirection = new Vector3(dockDirection.x, 0f, dockDirection.y);
        return true;
    }

    protected override float AdjustDrivenSignedStep(
        RailSample currentSample,
        Vector2 currentFacing,
        bool hasInput,
        Vector2 inputDirection,
        float deltaTime,
        float signedStep)
    {
        if (!autoDriveEnabled)
        {
            return signedStep;
        }

        signedStep = Mathf.Max(0f, signedStep);
        if (signedStep <= 0.0001f
            || autoDriveResolvedTargetStation == null
            || !TryGetAutoDriveTargetDockPathDelta(
                autoDriveResolvedTargetStation,
                out float signedDockDelta,
                out Vector2 dockDirection))
        {
            return signedStep;
        }

        float remainingDistance = Mathf.Abs(signedDockDelta);
        if (remainingDistance > ResolveAutoDriveDockApproachDistance())
        {
            return signedStep;
        }

        if (!TryResolveAutoDriveDockSignedStep(
                currentFacing,
                dockDirection,
                remainingDistance,
                signedStep,
                out float dockSignedStep))
        {
            return 0f;
        }

        return dockSignedStep;
    }

    protected override bool CanDockInDirection(Vector2 facing, Vector2 travelDirection)
    {
        return !autoDriveEnabled || Vector2.Dot(facing, travelDirection) > 0f;
    }

    protected override float ResolveRailInputAxis(
        bool hasInput,
        Vector2 inputDirection,
        float inputMagnitude,
        Vector2 facing,
        RailSample currentSample)
    {
        float baseAxis = base.ResolveRailInputAxis(
            hasInput,
            inputDirection,
            inputMagnitude,
            facing,
            currentSample);
        if (!autoDriveEnabled)
        {
            return baseAxis;
        }

        if (!hasInput
            || autoDriveRouteSegments.Count <= 0
            || IsAutoDriveDockingApproachActive())
        {
            return Mathf.Max(0f, baseAxis);
        }

        return TryResolveAutoDriveRouteInputAxis(
            currentSample,
            facing,
            inputMagnitude,
            out float routeAxis)
            ? Mathf.Max(0f, routeAxis)
            : Mathf.Max(0f, baseAxis);
    }

    private bool TryResolveAutoDriveRouteInputAxis(
        RailSample currentSample,
        Vector2 facing,
        float inputMagnitude,
        out float inputAxis)
    {
        inputAxis = 0f;
        if (currentSample.Rail == null
            || facing.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        ReconcileAutoDriveRouteCursor(currentSample.Rail, currentSample.DistanceAlongPath);
        if (!TryFindBestAutoDriveRouteSegmentIndex(
                currentSample.Rail,
                currentSample.DistanceAlongPath,
                out int segmentIndex))
        {
            return false;
        }

        AutoDriveRoutePlanner.RouteSegment segment = autoDriveRouteSegments[segmentIndex];
        float directionSign = Mathf.Sign(segment.EndDistance - segment.StartDistance);
        if (Mathf.Abs(directionSign) <= 0.0001f)
        {
            return false;
        }

        Vector2 pathTangent = currentSample.Tangent;
        if (currentSample.Rail.TrySampleRenderedPath(
                currentSample.DistanceAlongPath,
                out _,
                out Vector2 sampledTangent)
            && sampledTangent.sqrMagnitude > 0.0001f)
        {
            pathTangent = sampledTangent;
        }

        if (pathTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector2 routeDirection = pathTangent.normalized * directionSign;
        float facingDot = Vector2.Dot(routeDirection, facing.normalized);
        if (facingDot <= 0.05f)
        {
            return false;
        }

        inputAxis = Mathf.Clamp01(inputMagnitude);
        return true;
    }

    protected override bool TryResolvePreferredConnectedRailTravelDirection(
        RailSample endpointSample,
        Vector2 exitDirection,
        RailSample connectedSample,
        out Vector2 travelDirection)
    {
        travelDirection = Vector2.zero;
        if (!autoDriveEnabled
            || autoDriveRouteSegments.Count <= 0
            || connectedSample.Rail == null)
        {
            return false;
        }

        if (endpointSample.Rail != null)
        {
            ReconcileAutoDriveRouteCursor(endpointSample.Rail, endpointSample.DistanceAlongPath);
        }

        if (!TryFindBestAutoDriveRouteSegmentIndex(
                connectedSample.Rail,
                connectedSample.DistanceAlongPath,
                out int segmentIndex))
        {
            return false;
        }

        AutoDriveRoutePlanner.RouteSegment segment = autoDriveRouteSegments[segmentIndex];
        if (!IsDistanceWithinAutoDriveRouteSegment(
                segment,
                connectedSample.DistanceAlongPath,
                AutoDriveRouteSegmentTolerance))
        {
            return false;
        }

        float directionSign = Mathf.Sign(segment.EndDistance - segment.StartDistance);
        if (Mathf.Abs(directionSign) <= 0.0001f)
        {
            return false;
        }

        Vector2 pathTangent = connectedSample.Tangent;
        if (connectedSample.Rail.TrySampleRenderedPath(
                connectedSample.DistanceAlongPath,
                out _,
                out Vector2 sampledTangent)
            && sampledTangent.sqrMagnitude > 0.0001f)
        {
            pathTangent = sampledTangent;
        }

        if (pathTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        autoDriveRouteSegmentCursor = Mathf.Clamp(
            segmentIndex,
            0,
            autoDriveRouteSegments.Count - 1);
        travelDirection = pathTangent.normalized * directionSign;
        return true;
    }

    private bool IsAutoDriveDockingApproachActive()
    {
        return autoDriveStatus == AutoDriveStatus.Docking;
    }

    protected override bool TryGetPreferredBranchRail(
        RailSample currentSample,
        Vector2 inputDirection,
        out Railload preferredRail)
    {
        preferredRail = null;
        if (!autoDriveEnabled
            || autoDriveRouteSegments.Count <= 0
            || currentSample.Rail == null)
        {
            return false;
        }

        ReconcileAutoDriveRouteCursor(currentSample.Rail, currentSample.DistanceAlongPath);
        if (!TryResolveNextAutoDriveRouteSegment(
                currentSample.Rail,
                currentSample.DistanceAlongPath,
                null,
                out AutoDriveRoutePlanner.RouteSegment currentSegment,
                out _,
                out AutoDriveRoutePlanner.RouteSegment nextSegment))
        {
            return false;
        }

        float branchPreviewDistance = ResolveAutoDriveBranchPreviewDistance(ResolveAutoDriveRouteReferenceTrain());
        float remainingDistance = ResolveAutoDriveRemainingSegmentDistance(
            currentSegment,
            ClampDistanceAlongSegment(currentSegment, currentSample.DistanceAlongPath));
        if (remainingDistance > branchPreviewDistance)
        {
            return false;
        }

        preferredRail = nextSegment.Rail;
        return preferredRail != null;
    }

    protected override bool TryGetPreferredConnectedRail(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        out Railload preferredRail)
    {
        preferredRail = null;
        if (!autoDriveEnabled
            || autoDriveRouteSegments.Count <= 0
            || endpointSample.Rail == null)
        {
            return false;
        }

        ReconcileAutoDriveRouteCursor(endpointSample.Rail, endpointSample.DistanceAlongPath);
        if (!TryResolveNextAutoDriveRouteSegment(
                endpointSample.Rail,
                endpointSample.DistanceAlongPath,
                excludedRail,
                out _,
                out _,
                out AutoDriveRoutePlanner.RouteSegment nextSegment))
        {
            return false;
        }

        preferredRail = nextSegment.Rail;
        return preferredRail != null;
    }

    protected override bool TryGetPreferredConnectedRailEntrySample(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        out RailSample connectedSample)
    {
        connectedSample = default;
        if (!autoDriveEnabled
            || autoDriveRouteSegments.Count <= 0
            || endpointSample.Rail == null)
        {
            return false;
        }

        ReconcileAutoDriveRouteCursor(endpointSample.Rail, endpointSample.DistanceAlongPath);
        if (TryResolveNextAutoDriveRouteSegment(
                endpointSample.Rail,
                endpointSample.DistanceAlongPath,
                excludedRail,
                out _,
                out _,
                out AutoDriveRoutePlanner.RouteSegment nextSegment)
            && TryCreateAutoDriveConnectedRailEntrySample(
                endpointSample,
                nextSegment,
                out connectedSample))
        {
            return true;
        }

        return TryFindAutoDriveConnectedRailEntrySampleFromRoute(
            endpointSample,
            excludedRail,
            out connectedSample);
    }

    private bool TryResolveNextAutoDriveRouteSegment(
        Railload currentRail,
        float currentDistanceAlongPath,
        Railload excludedRail,
        out AutoDriveRoutePlanner.RouteSegment currentSegment,
        out int currentSegmentIndex,
        out AutoDriveRoutePlanner.RouteSegment nextSegment)
    {
        currentSegment = default;
        currentSegmentIndex = -1;
        nextSegment = default;
        if (currentRail == null
            || autoDriveRouteSegments.Count <= 0
            || !TryFindBestAutoDriveRouteSegmentIndex(
                currentRail,
                currentDistanceAlongPath,
                out currentSegmentIndex))
        {
            return false;
        }

        currentSegment = autoDriveRouteSegments[currentSegmentIndex];
        for (int nextSegmentIndex = currentSegmentIndex + 1;
             nextSegmentIndex < autoDriveRouteSegments.Count;
             nextSegmentIndex++)
        {
            AutoDriveRoutePlanner.RouteSegment candidateSegment = autoDriveRouteSegments[nextSegmentIndex];
            Railload candidateRail = candidateSegment.Rail;
            if (candidateRail == null
                || candidateRail == currentRail
                || candidateRail == excludedRail)
            {
                continue;
            }

            nextSegment = candidateSegment;
            return true;
        }

        return false;
    }

    private bool TryFindBestAutoDriveRouteSegmentIndex(
        Railload rail,
        float distanceAlongPath,
        out int segmentIndex)
    {
        segmentIndex = -1;
        if (rail == null || autoDriveRouteSegments.Count <= 0)
        {
            return false;
        }

        int cursor = Mathf.Clamp(
            autoDriveRouteSegmentCursor,
            0,
            autoDriveRouteSegments.Count - 1);
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < autoDriveRouteSegments.Count; i++)
        {
            AutoDriveRoutePlanner.RouteSegment segment = autoDriveRouteSegments[i];
            if (segment.Rail != rail)
            {
                continue;
            }

            float score = ResolveAutoDriveRouteSegmentDistanceScore(segment, distanceAlongPath)
                          + Mathf.Abs(i - cursor) * 0.05f;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            segmentIndex = i;
        }

        return segmentIndex >= 0;
    }

    private static float ResolveAutoDriveRouteSegmentDistanceScore(
        AutoDriveRoutePlanner.RouteSegment segment,
        float distanceAlongPath)
    {
        float minDistance =
            Mathf.Min(segment.StartDistance, segment.EndDistance) - AutoDriveRouteSegmentTolerance;
        float maxDistance =
            Mathf.Max(segment.StartDistance, segment.EndDistance) + AutoDriveRouteSegmentTolerance;
        if (distanceAlongPath < minDistance)
        {
            return minDistance - distanceAlongPath;
        }

        if (distanceAlongPath > maxDistance)
        {
            return distanceAlongPath - maxDistance;
        }

        return 0f;
    }

    private bool TryCreateAutoDriveConnectedRailEntrySample(
        RailSample endpointSample,
        AutoDriveRoutePlanner.RouteSegment nextSegment,
        out RailSample connectedSample)
    {
        connectedSample = default;
        Railload nextRail = nextSegment.Rail;
        if (nextRail == null)
        {
            return false;
        }

        float maxConnectionDistance = ResolveRailConnectionMaxDistance();
        float maxConnectionSqrDistance = maxConnectionDistance * maxConnectionDistance;
        bool found = false;
        float bestSqrDistance = maxConnectionSqrDistance;
        if (nextRail.TrySampleRenderedPath(
                nextSegment.StartDistance,
                out Vector2 pathPoint,
                out Vector2 tangent))
        {
            float sqrDistance = (pathPoint - endpointSample.Point).sqrMagnitude;
            if (sqrDistance <= bestSqrDistance)
            {
                connectedSample.Rail = nextRail;
                connectedSample.DistanceAlongPath = nextSegment.StartDistance;
                connectedSample.Point = pathPoint;
                connectedSample.Tangent = tangent;
                connectedSample.SqrDistance = sqrDistance;
                bestSqrDistance = sqrDistance;
                found = true;
            }
        }

        if (nextRail.TryFindNearestRenderedPathSample(
                endpointSample.Point,
                out float nearestDistanceAlongPath,
                out Vector2 nearestPoint,
                out Vector2 nearestTangent,
                out float nearestSqrDistance)
            && nearestSqrDistance <= bestSqrDistance
            && IsDistanceWithinAutoDriveRouteSegment(
                nextSegment,
                nearestDistanceAlongPath,
                AutoDriveRouteSegmentTolerance))
        {
            connectedSample.Rail = nextRail;
            connectedSample.DistanceAlongPath = nearestDistanceAlongPath;
            connectedSample.Point = nearestPoint;
            connectedSample.Tangent = nearestTangent;
            connectedSample.SqrDistance = nearestSqrDistance;
            found = true;
        }

        return found;
    }

    private bool TryFindAutoDriveConnectedRailEntrySampleFromRoute(
        RailSample endpointSample,
        Railload excludedRail,
        out RailSample connectedSample)
    {
        connectedSample = default;
        if (endpointSample.Rail == null || autoDriveRouteSegments.Count <= 0)
        {
            return false;
        }

        int cursor = Mathf.Clamp(
            autoDriveRouteSegmentCursor,
            0,
            autoDriveRouteSegments.Count - 1);
        if (TryFindAutoDriveConnectedRailEntrySampleInRange(
                endpointSample,
                excludedRail,
                Mathf.Max(0, cursor - AutoDriveRouteCursorLookAheadSegments),
                Mathf.Min(
                    autoDriveRouteSegments.Count - 1,
                    cursor + AutoDriveRouteCursorLookAheadSegments + 4),
                out connectedSample,
                out _))
        {
            return true;
        }

        return TryFindAutoDriveConnectedRailEntrySampleInRange(
            endpointSample,
            excludedRail,
            0,
            autoDriveRouteSegments.Count - 1,
            out connectedSample,
            out _);
    }

    private bool TryFindAutoDriveConnectedRailEntrySampleInRange(
        RailSample endpointSample,
        Railload excludedRail,
        int minSegmentIndex,
        int maxSegmentIndex,
        out RailSample connectedSample,
        out int connectedSegmentIndex)
    {
        connectedSample = default;
        connectedSegmentIndex = -1;
        minSegmentIndex = Mathf.Clamp(minSegmentIndex, 0, autoDriveRouteSegments.Count - 1);
        maxSegmentIndex = Mathf.Clamp(maxSegmentIndex, 0, autoDriveRouteSegments.Count - 1);
        if (minSegmentIndex > maxSegmentIndex)
        {
            return false;
        }

        bool found = false;
        float bestSqrDistance = float.MaxValue;
        for (int segmentIndex = minSegmentIndex; segmentIndex <= maxSegmentIndex; segmentIndex++)
        {
            AutoDriveRoutePlanner.RouteSegment segment = autoDriveRouteSegments[segmentIndex];
            if (segment.Rail == null
                || segment.Rail == endpointSample.Rail
                || segment.Rail == excludedRail
                || !TryCreateAutoDriveConnectedRailEntrySample(
                    endpointSample,
                    segment,
                    out RailSample candidateSample)
                || candidateSample.SqrDistance >= bestSqrDistance)
            {
                continue;
            }

            bestSqrDistance = candidateSample.SqrDistance;
            connectedSample = candidateSample;
            connectedSegmentIndex = segmentIndex;
            found = true;
        }

        if (found)
        {
            autoDriveRouteSegmentCursor = Mathf.Clamp(
                connectedSegmentIndex,
                0,
                autoDriveRouteSegments.Count - 1);
        }

        return found;
    }

    protected override bool ShouldAllowRestrictedBranchRailCandidate(
        RailSample currentSample,
        Vector2 inputDirection,
        Railload candidateRail)
    {
        if (!autoDriveEnabled
            || candidateRail == null
            || currentSample.Rail == null
            || candidateRail == currentSample.Rail)
        {
            return false;
        }

        ReconcileAutoDriveRouteCursor(currentSample.Rail, currentSample.DistanceAlongPath);
        if (!TryResolveNextAutoDriveRouteSegment(
                currentSample.Rail,
                currentSample.DistanceAlongPath,
                null,
                out AutoDriveRoutePlanner.RouteSegment currentSegment,
                out _,
                out AutoDriveRoutePlanner.RouteSegment nextSegment)
            || nextSegment.Rail != candidateRail)
        {
            return false;
        }

        float remainingDistance = ResolveAutoDriveRemainingSegmentDistance(
            currentSegment,
            ClampDistanceAlongSegment(currentSegment, currentSample.DistanceAlongPath));
        return remainingDistance <= ResolveAutoDriveBranchPreviewDistance(ResolveAutoDriveRouteReferenceTrain());
    }

    protected override bool ShouldAllowRestrictedConnectedRailCandidate(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        RailSample candidateSample)
    {
        if (!autoDriveEnabled
            || endpointSample.Rail == null
            || candidateSample.Rail == null)
        {
            return false;
        }

        ReconcileAutoDriveRouteCursor(endpointSample.Rail, endpointSample.DistanceAlongPath);
        if (TryResolveNextAutoDriveRouteSegment(
                endpointSample.Rail,
                endpointSample.DistanceAlongPath,
                excludedRail,
                out _,
                out _,
                out AutoDriveRoutePlanner.RouteSegment nextSegment)
            && IsAutoDriveRouteSegmentCandidate(nextSegment, candidateSample, excludedRail))
        {
            return true;
        }

        return TryFindBestAutoDriveRouteSegmentIndex(
                   endpointSample.Rail,
                   endpointSample.DistanceAlongPath,
                   out int currentSegmentIndex)
               && IsConnectedRailCandidateWithinAutoDriveRouteWindow(
                   currentSegmentIndex,
                   candidateSample,
                   excludedRail);
    }

    protected override bool ShouldAllowLowProgressConnectedRailCandidate(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail,
        RailSample candidateSample)
    {
        return autoDriveEnabled
               && autoDriveRouteSegments.Count > 0
               && ShouldAllowRestrictedConnectedRailCandidate(
                   endpointSample,
                   exitDirection,
                   excludedRail,
                   candidateSample);
    }

    protected override bool ShouldRestrictBranchRailSelection(
        RailSample currentSample,
        Vector2 inputDirection)
    {
        return autoDriveEnabled && autoDriveRouteSegments.Count > 0;
    }

    protected override bool ShouldRestrictConnectedRailSelection(
        RailSample endpointSample,
        Vector2 exitDirection,
        Railload excludedRail)
    {
        return autoDriveEnabled && autoDriveRouteSegments.Count > 0;
    }

    private float ResolveAutoDriveBranchPreviewDistance(RailHandcar routeReferenceTrain)
    {
        float routeThreshold = AutoDrivePreferredBranchSelectionDistance;
        if (routeReferenceTrain == null)
        {
            return routeThreshold;
        }

        return Mathf.Max(
            routeThreshold,
            routeReferenceTrain.GetBranchPreviewDistance());
    }

    private bool IsConnectedRailCandidateWithinAutoDriveRouteWindow(
        int currentSegmentIndex,
        RailSample candidateSample,
        Railload excludedRail)
    {
        if (candidateSample.Rail == null
            || autoDriveRouteSegments.Count <= 0
            || currentSegmentIndex < 0)
        {
            return false;
        }

        int maxSegmentIndex = Mathf.Min(
            autoDriveRouteSegments.Count - 1,
            currentSegmentIndex + AutoDriveRouteCursorLookAheadSegments + 2);
        for (int segmentIndex = currentSegmentIndex + 1;
             segmentIndex <= maxSegmentIndex;
             segmentIndex++)
        {
            AutoDriveRoutePlanner.RouteSegment segment = autoDriveRouteSegments[segmentIndex];
            if (!IsAutoDriveRouteSegmentCandidate(segment, candidateSample, excludedRail))
            {
                continue;
            }

            autoDriveRouteSegmentCursor = Mathf.Clamp(
                segmentIndex,
                0,
                autoDriveRouteSegments.Count - 1);
            return true;
        }

        return false;
    }

    private static bool IsAutoDriveRouteSegmentCandidate(
        AutoDriveRoutePlanner.RouteSegment segment,
        RailSample candidateSample,
        Railload excludedRail)
    {
        return candidateSample.Rail != null
               && segment.Rail == candidateSample.Rail
               && segment.Rail != excludedRail
               && IsDistanceWithinAutoDriveRouteSegment(
                   segment,
                   candidateSample.DistanceAlongPath,
                   AutoDriveRouteSegmentTolerance);
    }

    private float ResolveAutoDriveBranchSteerDistance(RailHandcar routeReferenceTrain)
    {
        float previewDistance = ResolveAutoDriveBranchPreviewDistance(routeReferenceTrain);
        float connectionDistance = ResolveRailConnectionMaxDistance();
        return Mathf.Min(
            previewDistance,
            Mathf.Max(AutoDriveRouteSegmentTolerance, connectionDistance));
    }

    protected override float ResolveRailConnectionMaxDistance()
    {
        float baseDistance = base.ResolveRailConnectionMaxDistance();
        return autoDriveEnabled
            ? Mathf.Max(baseDistance, RailConnectionUtility.ConnectionDistance)
            : baseDistance;
    }

    protected override float ResolveRailTransitionMovementDistance(
        RailSample fromSample,
        RailSample toSample)
    {
        float distance = base.ResolveRailTransitionMovementDistance(fromSample, toSample);
        if (!autoDriveEnabled
            || fromSample.Rail == null
            || toSample.Rail == null
            || fromSample.Rail == toSample.Rail)
        {
            return distance;
        }

        return Mathf.Min(
            distance,
            AutoDriveRailConnectionMovementCostMaxDistance);
    }

    private void ReconcileAutoDriveRouteCursor(
        Railload currentRail,
        float currentDistanceAlongPath)
    {
        if (currentRail == null || autoDriveRouteSegments.Count <= 0)
        {
            return;
        }

        autoDriveRouteSegmentCursor = Mathf.Clamp(
            autoDriveRouteSegmentCursor,
            0,
            autoDriveRouteSegments.Count - 1);

        int maxProbeSegmentIndex = Mathf.Min(
            autoDriveRouteSegments.Count - 1,
            autoDriveRouteSegmentCursor + AutoDriveRouteCursorLookAheadSegments);
        for (int probeIndex = autoDriveRouteSegmentCursor; probeIndex <= maxProbeSegmentIndex; probeIndex++)
        {
            if (!IsAutoDriveRouteSegmentMatch(
                    autoDriveRouteSegments[probeIndex],
                    currentRail,
                    currentDistanceAlongPath))
            {
                continue;
            }

            autoDriveRouteSegmentCursor = probeIndex;
            return;
        }

        for (int probeIndex = autoDriveRouteSegmentCursor + 1; probeIndex <= maxProbeSegmentIndex; probeIndex++)
        {
            if (autoDriveRouteSegments[probeIndex].Rail != currentRail)
            {
                continue;
            }

            autoDriveRouteSegmentCursor = probeIndex;
            return;
        }

        for (int attempt = 0; attempt < AutoDriveRouteCursorLookAheadSegments + 1; attempt++)
        {
            AutoDriveRoutePlanner.RouteSegment currentSegment =
                autoDriveRouteSegments[autoDriveRouteSegmentCursor];
            if (IsAutoDriveRouteSegmentMatch(currentSegment, currentRail, currentDistanceAlongPath))
            {
                return;
            }

            int nextSegmentIndex = autoDriveRouteSegmentCursor + 1;
            if (nextSegmentIndex < autoDriveRouteSegments.Count
                && IsAutoDriveRouteSegmentMatch(
                    autoDriveRouteSegments[nextSegmentIndex],
                    currentRail,
                    currentDistanceAlongPath))
            {
                autoDriveRouteSegmentCursor = nextSegmentIndex;
                return;
            }

            if (nextSegmentIndex < autoDriveRouteSegments.Count
                && autoDriveRouteSegments[nextSegmentIndex].Rail == currentRail)
            {
                autoDriveRouteSegmentCursor = nextSegmentIndex;
                return;
            }

            if (nextSegmentIndex < autoDriveRouteSegments.Count
                && HasAutoDriveRouteSegmentBeenPassed(
                    currentSegment,
                    currentRail,
                    currentDistanceAlongPath))
            {
                autoDriveRouteSegmentCursor = nextSegmentIndex;
                continue;
            }

            break;
        }
    }

    private static float ClampDistanceAlongSegment(
        AutoDriveRoutePlanner.RouteSegment segment,
        float distanceAlongPath)
    {
        return Mathf.Clamp(
            distanceAlongPath,
            Mathf.Min(segment.StartDistance, segment.EndDistance),
            Mathf.Max(segment.StartDistance, segment.EndDistance));
    }

    private static bool IsAutoDriveRouteSegmentMatch(
        AutoDriveRoutePlanner.RouteSegment segment,
        Railload rail,
        float distanceAlongPath)
    {
        if (segment.Rail != rail)
        {
            return false;
        }

        return IsDistanceWithinAutoDriveRouteSegment(
            segment,
            distanceAlongPath,
            AutoDriveRouteSegmentTolerance);
    }

    private static bool IsDistanceWithinAutoDriveRouteSegment(
        AutoDriveRoutePlanner.RouteSegment segment,
        float distanceAlongPath,
        float tolerance)
    {
        float minDistance =
            Mathf.Min(segment.StartDistance, segment.EndDistance) - Mathf.Max(0f, tolerance);
        float maxDistance =
            Mathf.Max(segment.StartDistance, segment.EndDistance) + Mathf.Max(0f, tolerance);
        return distanceAlongPath >= minDistance && distanceAlongPath <= maxDistance;
    }

    private static bool HasAutoDriveRouteSegmentBeenPassed(
        AutoDriveRoutePlanner.RouteSegment segment,
        Railload rail,
        float distanceAlongPath)
    {
        if (segment.Rail != rail)
        {
            return false;
        }

        float directionSign = Mathf.Sign(segment.EndDistance - segment.StartDistance);
        if (Mathf.Abs(directionSign) <= 0.0001f)
        {
            return false;
        }

        return directionSign > 0f
            ? distanceAlongPath > segment.EndDistance - AutoDriveRouteSegmentTolerance
            : distanceAlongPath < segment.EndDistance + AutoDriveRouteSegmentTolerance;
    }

    private RailHandcar ResolveAutoDriveRouteReferenceTrain()
    {
        return ResolveAutoDriveRouteReferenceTrain(
            autoDriveResolvedTargetStation,
            autoDriveResolvedTargetStationName);
    }

    private RailHandcar ResolveAutoDriveRouteReferenceTrain(Trainstation targetStation)
    {
        return ResolveAutoDriveRouteReferenceTrain(
            targetStation,
            targetStation != null ? targetStation.StationName : string.Empty);
    }

    private RailHandcar ResolveAutoDriveRouteReferenceTrain(
        Trainstation targetStation,
        string targetStationName)
    {
        CollectAutoDriveConnectedTrains();
        if (TryGetCachedAutoDriveRouteReferenceTrain(targetStationName, out RailHandcar cachedTrain))
        {
            return cachedTrain;
        }

        if (TryResolveAutoDriveClosestEndpointTrain(
                targetStation,
                targetStationName,
                out RailHandcar endpointTrain))
        {
            CacheAutoDriveRouteReferenceTrain(targetStationName, endpointTrain);
            return endpointTrain;
        }

        // A failed forward route must wait, rather than fall back to reversing.
        return null;
    }

    private bool TryResolveAutoDriveClosestEndpointTrain(
        Trainstation targetStation,
        string targetStationName,
        out RailHandcar routeReferenceTrain)
    {
        routeReferenceTrain = null;
        if (targetStation == null)
        {
            return false;
        }

        if (TryResolveAutoDriveClosestRouteReferenceTrain(
                targetStation,
                targetStationName,
                endpointOnly: true,
                this,
                out routeReferenceTrain))
        {
            return true;
        }

        // A powered vehicle inside the consist is usable only on a forward route.
        return TryResolveAutoDriveClosestRouteReferenceTrain(
            targetStation,
            targetStationName,
            endpointOnly: false,
            this,
            out routeReferenceTrain);
    }

    private bool TryGetCachedAutoDriveRouteReferenceTrain(
        string targetStationName,
        out RailHandcar routeReferenceTrain)
    {
        routeReferenceTrain = autoDriveCachedRouteReferenceTrain;
        return !string.IsNullOrWhiteSpace(targetStationName)
               && string.Equals(
                   autoDriveCachedRouteReferenceTargetStationName,
                   targetStationName,
                   System.StringComparison.OrdinalIgnoreCase)
               && IsValidAutoDriveRouteReferenceTrain(routeReferenceTrain)
               && autoDriveConnectedTrainVisited.Contains(routeReferenceTrain);
    }

    private void CacheAutoDriveRouteReferenceTrain(
        string targetStationName,
        RailHandcar routeReferenceTrain)
    {
        autoDriveCachedRouteReferenceTargetStationName = targetStationName ?? string.Empty;
        autoDriveCachedRouteReferenceTrain = routeReferenceTrain;
    }

    private bool TryResolveAutoDriveClosestRouteReferenceTrain(
        Trainstation targetStation,
        string targetStationName,
        bool endpointOnly,
        RailHandcar fallbackReferenceTrain,
        out RailHandcar routeReferenceTrain)
    {
        routeReferenceTrain = null;
        float bestRouteLength = float.PositiveInfinity;
        for (int i = 0; i < autoDriveConnectedTrainScratch.Count; i++)
        {
            if (!TryGetAutoDriveRouteReferenceCandidate(
                    autoDriveConnectedTrainScratch[i],
                    endpointOnly,
                    out RailHandcar candidate)
                || !TryBuildRouteLengthForReferenceCandidate(
                    candidate,
                    targetStation,
                    targetStationName,
                    out float candidateRouteLength))
            {
                continue;
            }

            if (candidateRouteLength + 0.0001f < bestRouteLength
                || (Mathf.Abs(candidateRouteLength - bestRouteLength) <= 0.0001f
                    && routeReferenceTrain != fallbackReferenceTrain
                    && candidate == fallbackReferenceTrain))
            {
                bestRouteLength = candidateRouteLength;
                routeReferenceTrain = candidate;
            }
        }

        return routeReferenceTrain != null;
    }

    private bool TryGetAutoDriveRouteReferenceCandidate(
        Train train,
        bool endpointOnly,
        out RailHandcar candidate)
    {
        candidate = train as SteamTrain;
        if (!IsValidAutoDriveRouteReferenceTrain(candidate))
        {
            return false;
        }

        return !endpointOnly || CountConnectedTrainsWithinAutoDriveGroup(train) <= 1;
    }

    private bool TryBuildRouteLengthForReferenceCandidate(
        RailHandcar candidate,
        Trainstation targetStation,
        string targetStationName,
        out float routeLength)
    {
        routeLength = float.PositiveInfinity;
        if (!IsValidAutoDriveRouteReferenceTrain(candidate) || targetStation == null)
        {
            return false;
        }

        if (targetStation.TryGetRailCoordinate(out Vector2Int dockCoordinate)
            && candidate.TryGetRailDockDeltaAtCoordinate(dockCoordinate, out float dockDelta)
            && Mathf.Abs(dockDelta) <= 0.0001f)
        {
            routeLength = 0f;
            return true;
        }

        autoDriveRouteReferenceScratchSegments.Clear();
        bool hasFixedRoute = TryEnsureAutoDriveFixedRoute();
        bool builtRoute = hasFixedRoute
            ? TryBuildActiveRouteFromFixedRoute(
                candidate,
                targetStationName,
                autoDriveRouteReferenceScratchSegments)
            : AutoDriveRoutePlanner.TryBuildRoute(
                candidate,
                targetStation,
                autoDriveRouteReferenceScratchSegments);
        if (!builtRoute)
        {
            autoDriveRouteReferenceScratchSegments.Clear();
            return false;
        }

        routeLength = AutoDriveRoutePlanner.GetRouteLength(autoDriveRouteReferenceScratchSegments);
        autoDriveRouteReferenceScratchSegments.Clear();
        return !float.IsPositiveInfinity(routeLength);
    }

    private void CollectAutoDriveConnectedTrains()
    {
        ulong graphRevision = Train.ConnectionGraphRevision;
        if (autoDriveConnectedTrainCacheValid
            && autoDriveConnectedTrainGraphRevision == graphRevision)
        {
            return;
        }

        autoDriveConnectedTrainScratch.Clear();
        autoDriveConnectedTrainVisited.Clear();
        autoDriveConnectedTrainQueue.Clear();
        autoDriveConnectedTrainGraphRevision = graphRevision;
        autoDriveConnectedTrainCacheValid = true;
        autoDriveCachedRouteReferenceTrain = null;
        if (!IsValidAutoDriveRouteReferenceTrain(this))
        {
            return;
        }

        autoDriveConnectedTrainQueue.Enqueue(this);
        autoDriveConnectedTrainVisited.Add(this);
        while (autoDriveConnectedTrainQueue.Count > 0)
        {
            Train currentTrain = autoDriveConnectedTrainQueue.Dequeue();
            if (currentTrain == null || !currentTrain.gameObject.activeInHierarchy)
            {
                continue;
            }

            autoDriveConnectedTrainScratch.Add(currentTrain);
            foreach (Train connectedTrain in currentTrain.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !autoDriveConnectedTrainVisited.Add(connectedTrain))
                {
                    continue;
                }

                autoDriveConnectedTrainQueue.Enqueue(connectedTrain);
            }
        }
    }

    private void InvalidateAutoDriveConnectedTrainCache()
    {
        autoDriveConnectedTrainScratch.Clear();
        autoDriveFuelFreightCarScratch.Clear();
        autoDriveConnectedTrainVisited.Clear();
        autoDriveConnectedTrainQueue.Clear();
        autoDriveConnectedTrainGraphRevision = 0;
        autoDriveConnectedTrainCacheValid = false;
    }

    private int CountConnectedTrainsWithinAutoDriveGroup(Train train)
    {
        if (train == null)
        {
            return 0;
        }

        int connectedCount = 0;
        foreach (Train connectedTrain in train.ConnectedTrains)
        {
            if (connectedTrain != null
                && connectedTrain.gameObject.activeInHierarchy
                && autoDriveConnectedTrainVisited.Contains(connectedTrain))
            {
                connectedCount++;
            }
        }

        return connectedCount;
    }

    private static bool IsValidAutoDriveRouteReferenceTrain(RailHandcar candidate)
    {
        return candidate != null
               && candidate.gameObject.activeInHierarchy
               && candidate.TryGetPlacementRuntime(out _, out _);
    }

    private bool HasAutoDriveRouteReferenceChanged(RailHandcar routeReferenceTrain)
    {
        return routeReferenceTrain == null
               || routeReferenceTrain.SimulationId != autoDriveRouteReferenceTrainSimulationId;
    }

    private float ResolveAutoDriveDockApproachDistance()
    {
        float stoppingDistance =
            CurrentVehicleSpeed * CurrentVehicleSpeed
            / (2f * Mathf.Max(0.01f, VehicleDecelerationPerSecond));
        return Mathf.Max(
            ResolveDockCaptureDistance(),
            stoppingDistance + ResolveAutoDriveArrivalSnapDistance());
    }

    private float ResolveAutoDriveArrivalSnapDistance()
    {
        return Mathf.Max(AutoDriveDockSnapDistance, ResolveDockCompleteDistance());
    }

    private static float ResolveAutoDriveRemainingSegmentDistance(
        AutoDriveRoutePlanner.RouteSegment segment,
        float currentDistanceAlongPath)
    {
        float directionSign = Mathf.Sign(segment.EndDistance - segment.StartDistance);
        if (Mathf.Abs(directionSign) <= 0.0001f)
        {
            return 0f;
        }

        float minDistance = Mathf.Min(segment.StartDistance, segment.EndDistance);
        float maxDistance = Mathf.Max(segment.StartDistance, segment.EndDistance);
        return directionSign > 0f
            ? maxDistance - currentDistanceAlongPath
            : currentDistanceAlongPath - minDistance;
    }

    private bool TryFinalizeAutoDriveArrival(float deltaTime)
    {
        if (deltaTime < 0f
            || autoDriveResolvedTargetStation == null
            || !IsWithinAutoDriveArrivalSnapDistance(autoDriveResolvedTargetStation))
        {
            return false;
        }

        if (!TrySnapAutoDriveToTargetDock(autoDriveResolvedTargetStation, deltaTime))
        {
            return false;
        }

        return FinalizeAutoDriveArrivalFromCurrentTarget();
    }

    private bool FinalizeAutoDriveArrivalFromCurrentTarget()
    {
        if (string.IsNullOrWhiteSpace(autoDriveResolvedTargetStationName))
        {
            return false;
        }

        HandleAutoDriveArrived(autoDriveResolvedTargetStationName, autoDriveResolvedNextStationName);
        return true;
    }

    private bool IsMountedByPlayer()
    {
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        return playerController != null && playerController.IsMountedOnVehicle(this);
    }

    private bool TryGetWaterPipeDockOffsetMetrics(
        Vector2 trainPoint,
        Vector2Int directionFromTrainToPipe,
        Vector2Int pipeCoordinate,
        out float alongDistance,
        out float lateralDistance)
    {
        alongDistance = 0f;
        lateralDistance = 0f;
        if (directionFromTrainToPipe == Vector2Int.zero)
        {
            return false;
        }

        Vector2 pipeDirection = new Vector2(directionFromTrainToPipe.x, directionFromTrainToPipe.y);
        if (pipeDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        pipeDirection.Normalize();
        Vector2 offsetToPipe = new Vector2(pipeCoordinate.x, pipeCoordinate.y) - trainPoint;
        alongDistance = Vector2.Dot(offsetToPipe, pipeDirection);
        lateralDistance = Mathf.Abs((pipeDirection.x * offsetToPipe.y) - (pipeDirection.y * offsetToPipe.x));
        return alongDistance >= WaterPipeDockMinAlongDistance
               && alongDistance <= WaterPipeDockMaxAlongDistance
               && lateralDistance <= WaterPipeDockMaxLateralDistance;
    }

    private bool WaterPipeNetworkHasWaterSource(Vector2Int startCoordinate, int waterItemId)
    {
        waterPipeSearchQueue.Clear();
        waterPipeSearchVisited.Clear();
        EnqueueWaterPipeSearchCoordinate(startCoordinate);

        int searchedNodeCount = 0;
        while (waterPipeSearchQueue.Count > 0
               && searchedNodeCount < WaterPipeNetworkSearchMaxNodes)
        {
            Vector2Int coordinate = waterPipeSearchQueue.Dequeue();
            searchedNodeCount++;

            if (!TryGetActivePipeAtCoordinate(
                    coordinate,
                    out Pipe pipe,
                    out Quaternion pipeRotation,
                    out PipeRuntimeRecord pipeRecord))
            {
                continue;
            }

            // Pipe-output areas share their grid coordinate with the connected
            // pipe. Check that exact output cell before walking adjacent cells.
            if (TryGetWaterSourceAtCoordinate(
                    coordinate,
                    Vector2Int.zero,
                    waterItemId))
            {
                waterPipeSearchQueue.Clear();
                waterPipeSearchVisited.Clear();
                return true;
            }

            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = CardinalDirections[directionIndex];
                if (!HasActivePipeConnectionTowards(
                        pipe,
                        pipeRecord,
                        coordinate,
                        pipeRotation,
                        direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                if (TryGetWaterSourceAtCoordinate(
                        nextCoordinate,
                        -direction,
                        waterItemId))
                {
                    waterPipeSearchQueue.Clear();
                    waterPipeSearchVisited.Clear();
                    return true;
                }

                if (TryGetActivePipeAtCoordinate(
                        nextCoordinate,
                        out Pipe nextPipe,
                        out Quaternion nextPipeRotation,
                        out PipeRuntimeRecord nextPipeRecord)
                    && HasActivePipeConnectionTowards(
                        nextPipe,
                        nextPipeRecord,
                        nextCoordinate,
                        nextPipeRotation,
                        -direction))
                {
                    EnqueueWaterPipeSearchCoordinate(nextCoordinate);
                }
            }

            if (TryGetActivePipeRemoteCoordinate(
                    pipe,
                    pipeRecord,
                    coordinate,
                    out Vector2Int remoteCoordinate))
            {
                EnqueueWaterPipeSearchCoordinate(remoteCoordinate);
            }
        }

        waterPipeSearchQueue.Clear();
        waterPipeSearchVisited.Clear();
        return false;
    }

    private void EnqueueWaterPipeSearchCoordinate(Vector2Int coordinate)
    {
        if (waterPipeSearchVisited.Add(coordinate))
        {
            waterPipeSearchQueue.Enqueue(coordinate);
        }
    }

    private bool TryGetWaterSourceAtCoordinate(
        Vector2Int coordinate,
        Vector2Int directionToPipe,
        int waterItemId)
    {
        if (!InputOutputModule.TryGetRuntimePipeSourceAtCoordinate(coordinate, out Pump pump)
            || pump == null
            || !pump.gameObject.activeInHierarchy
            || (directionToPipe != Vector2Int.zero
                && !pump.HasPipeConnectionTowards(pump.transform.rotation, directionToPipe))
            || !pump.TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond))
        {
            return false;
        }

        return outputItemId == waterItemId && litersPerSecond > WaterEpsilon;
    }

    private bool TryGetActivePipeAtCoordinate(
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion pipeRotation,
        out PipeRuntimeRecord pipeRecord)
    {
        pipe = null;
        pipeRotation = Quaternion.identity;
        pipeRecord = null;
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null)
        {
            return false;
        }

        if (block.TryGetRuntimePipeRecord(out pipeRecord))
        {
            pipe = pipeRecord.Prototype;
            pipeRotation = pipeRecord.WorldRotation;
            return pipe != null;
        }

        if (!block.TryGetRuntimePipe(out Pipe candidatePipe, out pipeRotation))
        {
            return false;
        }

        pipe = candidatePipe;
        return pipe != null;
    }

    private static bool HasActivePipeConnectionTowards(
        Pipe pipe,
        PipeRuntimeRecord pipeRecord,
        Vector2Int coordinate,
        Quaternion pipeRotation,
        Vector2Int direction)
    {
        return pipeRecord != null
            ? pipeRecord.HasConnectionTowardsAt(coordinate, direction)
            : pipe != null && pipe.HasConnectionTowardsAt(coordinate, pipeRotation, direction);
    }

    private static bool TryGetActivePipeRemoteCoordinate(
        Pipe pipe,
        PipeRuntimeRecord pipeRecord,
        Vector2Int coordinate,
        out Vector2Int remoteCoordinate)
    {
        remoteCoordinate = default;
        return pipeRecord != null
            ? pipeRecord.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate)
            : pipe != null && pipe.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate);
    }

    private static bool IsUsableBurnEnergyItem(int itemId)
    {
        return TryResolveBurnEnergyAmount(itemId, out _);
    }

    private static bool TryResolveBurnEnergyAmount(int itemId, out int energyAmount)
    {
        energyAmount = 0;
        ItemDefinition definition = InputOutputModule.ResolveItemDefinition(itemId);
        if (definition == null
            || definition.energyType != ItemDefinition.EnergyType.Burn
            || definition.energyAmount <= 0)
        {
            return false;
        }

        energyAmount = definition.energyAmount;
        return true;
    }

    private void SpendStoredBurnEnergyUnits(long costUnits)
    {
        if (costUnits <= 0L)
        {
            return;
        }

        storedBurnEnergyUnits = System.Math.Max(0L, storedBurnEnergyUnits - costUnits);
        if (storedBurnEnergyUnits <= 0L)
        {
            storedBurnEnergyUnits = 0L;
            burnEnergyGaugeCapacityUnits = 0L;
        }
    }

    private void SpendStoredWaterUnits(long costUnits)
    {
        if (costUnits <= 0L)
        {
            return;
        }

        float cost = DeterministicSimulationUnits.ToFloat(costUnits);
        int waterItemId = ResolveWaterItemId();
        if (waterItemId < 0 || !CanProvideFluidItem(waterItemId, cost))
        {
            return;
        }

        TryConsumeFluidLiters(waterItemId, cost, out _);
    }

    private void CaptureWaterPipeDefaults()
    {
        if (waterPipe == null || waterPipeDefaultsCaptured)
        {
            return;
        }

        waterPipeDefaultLocalPosition = waterPipe.localPosition;
        waterPipeDefaultLocalRotation = waterPipe.localRotation;
        waterPipeTargetLocalPosition = waterPipeDefaultLocalPosition;
        waterPipeTargetLocalRotation = waterPipeDefaultLocalRotation;
        waterPipeDefaultsCaptured = true;
    }

    private void SetWaterPipeDockTarget(Vector2Int directionFromTrainToPipe, bool transferReady)
    {
        if (directionFromTrainToPipe == Vector2Int.zero)
        {
            return;
        }

        waterPipeTargetActive = true;
        waterPipeTransferReady = transferReady;
        activeWaterPipeDirectionFromTrainToPipe = directionFromTrainToPipe;
        RefreshWaterPipeReceiverRegistration();

        // The authored child is visual-only. A missing visual reference must not
        // disable the functional water connection.
        if (waterPipe == null)
        {
            return;
        }

        CaptureWaterPipeDefaults();
        Vector3 worldDirection = new Vector3(directionFromTrainToPipe.x, 0f, directionFromTrainToPipe.y);
        if (worldDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        worldDirection.Normalize();
        Vector3 localDirection = transform.InverseTransformDirection(worldDirection);
        localDirection.y = 0f;
        if (localDirection.sqrMagnitude <= 0.0001f)
        {
            localDirection = Vector3.forward;
        }

        localDirection.Normalize();
        waterPipeTargetLocalPosition =
            waterPipeDefaultLocalPosition + localDirection * Mathf.Max(0f, waterPipeExtendDistance);
        waterPipeTargetLocalRotation =
            Quaternion.Inverse(transform.rotation)
            * Quaternion.LookRotation(worldDirection, Vector3.up)
            * Quaternion.Euler(0f, 90f, 0f);
        waterPipeAnimating = true;
        if (!waterPipe.gameObject.activeSelf)
        {
            waterPipe.gameObject.SetActive(true);
        }
    }

    private void RequestWaterPipeRetract()
    {
        ClearWaterPipeDockLock();
        waterPipeTargetActive = false;
        waterPipeTransferReady = false;
        activeWaterPipeDirectionFromTrainToPipe = Vector2Int.zero;
        RefreshWaterPipeReceiverRegistration();
        if (waterPipe == null)
        {
            return;
        }

        CaptureWaterPipeDefaults();
        waterPipeTargetLocalPosition = waterPipeDefaultLocalPosition;
        waterPipeTargetLocalRotation = waterPipeDefaultLocalRotation;
        if (waterPipe.gameObject.activeSelf)
        {
            waterPipeAnimating = true;
        }
    }

    private void ResetWaterPipeImmediate(bool active)
    {
        ClearWaterPipeDockLock();
        waterPipeTargetActive = active;
        waterPipeTransferReady = false;
        activeWaterPipeDirectionFromTrainToPipe = Vector2Int.zero;
        RefreshWaterPipeReceiverRegistration();
        waterPipeAnimating = false;
        if (waterPipe == null)
        {
            return;
        }

        CaptureWaterPipeDefaults();
        waterPipe.localPosition = waterPipeDefaultLocalPosition;
        waterPipe.localRotation = waterPipeDefaultLocalRotation;
        waterPipeTargetLocalPosition = waterPipeDefaultLocalPosition;
        waterPipeTargetLocalRotation = waterPipeDefaultLocalRotation;
        if (waterPipe.gameObject.activeSelf != active)
        {
            waterPipe.gameObject.SetActive(active);
        }
    }

    private void RefreshWaterPipeReceiverRegistration()
    {
        bool shouldRegister = waterPipeTargetActive
                              && waterPipeTransferReady
                              && waterPipeDockLockActive
                              && activeWaterPipeDirectionFromTrainToPipe != Vector2Int.zero;
        Vector2Int receiverCoordinate = shouldRegister
            ? lockedWaterPipeDockCoordinate - activeWaterPipeDirectionFromTrainToPipe
            : Vector2Int.zero;

        if (waterPipeReceiverRegistered
            && (!shouldRegister
                || registeredWaterPipeReceiverCoordinate != receiverCoordinate
                || registeredWaterPipeCoordinate != lockedWaterPipeDockCoordinate
                || registeredWaterPipeDirectionFromTrainToPipe != activeWaterPipeDirectionFromTrainToPipe))
        {
            UnregisterWaterPipeReceiver();
        }

        if (shouldRegister && !waterPipeReceiverRegistered)
        {
            RegisterWaterPipeReceiver(receiverCoordinate);
        }
    }

    private void RegisterWaterPipeReceiver(Vector2Int coordinate)
    {
        if (!WaterPipeReceiversByCoordinate.TryGetValue(
                coordinate,
                out List<SteamTrain> receivers))
        {
            receivers = new List<SteamTrain>(1);
            WaterPipeReceiversByCoordinate.Add(coordinate, receivers);
        }

        int insertIndex = receivers.Count;
        for (int i = 0; i < receivers.Count; i++)
        {
            SteamTrain existing = receivers[i];
            if (existing == this)
            {
                waterPipeReceiverRegistered = true;
                registeredWaterPipeReceiverCoordinate = coordinate;
                registeredWaterPipeCoordinate = lockedWaterPipeDockCoordinate;
                registeredWaterPipeDirectionFromTrainToPipe = activeWaterPipeDirectionFromTrainToPipe;
                InputOutputModule.NotifyRuntimePipeTopologyChanged(null);
                return;
            }

            if (existing == null || RuntimePlacementSequence < existing.RuntimePlacementSequence)
            {
                insertIndex = i;
                break;
            }
        }

        receivers.Insert(insertIndex, this);
        waterPipeReceiverRegistered = true;
        registeredWaterPipeReceiverCoordinate = coordinate;
        registeredWaterPipeCoordinate = lockedWaterPipeDockCoordinate;
        registeredWaterPipeDirectionFromTrainToPipe = activeWaterPipeDirectionFromTrainToPipe;
        InputOutputModule.NotifyRuntimePipeTopologyChanged(null);
    }

    private void UnregisterWaterPipeReceiver()
    {
        Vector2Int coordinate = registeredWaterPipeReceiverCoordinate;
        if (WaterPipeReceiversByCoordinate.TryGetValue(
                coordinate,
                out List<SteamTrain> receivers))
        {
            receivers.Remove(this);
            if (receivers.Count <= 0)
            {
                WaterPipeReceiversByCoordinate.Remove(coordinate);
            }
        }

        waterPipeReceiverRegistered = false;
        registeredWaterPipeReceiverCoordinate = Vector2Int.zero;
        registeredWaterPipeCoordinate = Vector2Int.zero;
        registeredWaterPipeDirectionFromTrainToPipe = Vector2Int.zero;
        InputOutputModule.NotifyRuntimePipeTopologyChanged(null);
    }

    private void UpdateWaterPipeVisual(float deltaTime)
    {
        if (waterPipe == null || !waterPipeAnimating)
        {
            return;
        }

        CaptureWaterPipeDefaults();
        if (!waterPipe.gameObject.activeSelf)
        {
            waterPipe.gameObject.SetActive(true);
        }

        float interpolation = deltaTime > 0f
            ? 1f - Mathf.Exp(-Mathf.Max(0.01f, waterPipeInterpolationSpeed) * deltaTime)
            : 1f;
        waterPipe.localPosition = Vector3.Lerp(
            waterPipe.localPosition,
            waterPipeTargetLocalPosition,
            interpolation);
        waterPipe.localRotation = Quaternion.Slerp(
            waterPipe.localRotation,
            waterPipeTargetLocalRotation,
            interpolation);

        if ((waterPipe.localPosition - waterPipeTargetLocalPosition).sqrMagnitude > 0.000001f
            || Quaternion.Angle(waterPipe.localRotation, waterPipeTargetLocalRotation) > 0.1f)
        {
            return;
        }

        waterPipe.localPosition = waterPipeTargetLocalPosition;
        waterPipe.localRotation = waterPipeTargetLocalRotation;
        waterPipeAnimating = false;
        if (!waterPipeTargetActive && waterPipe.gameObject.activeSelf)
        {
            waterPipe.gameObject.SetActive(false);
        }
    }

    private void PlayBurnEnergyPortableMove(
        PortableObject sourcePortableObject,
        int itemId,
        Vector3 startPosition,
        bool useSourcePortableObject)
    {
        PortableObject movingPortableObject = useSourcePortableObject
            ? sourcePortableObject
            : CreateBurnEnergyPortableMoveObject(sourcePortableObject, itemId, startPosition);
        if (movingPortableObject == null)
        {
            return;
        }

        Transform movingTransform = movingPortableObject.transform;
        movingPortableObject.name = $"{movingPortableObject.name}_BurnEnergyMove";
        movingTransform.SetParent(null, true);
        movingTransform.position = startPosition;
        if (sourcePortableObject != null)
        {
            movingTransform.localScale = sourcePortableObject.transform.lossyScale;
        }

        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            DestroyPortableMoveObject(movingPortableObject);
            return;
        }

        Vector3 targetPosition = ResolveBurnEnergyPortableMoveTargetPosition();
        movingPortableObject.MoveTo(
            () => this != null ? ResolveBurnEnergyPortableMoveTargetPosition() : targetPosition,
            0f,
            () => startPosition,
            () => DestroyPortableMoveObject(movingPortableObject),
            false);
    }

    private PortableObject CreateBurnEnergyPortableMoveObject(
        PortableObject sourcePortableObject,
        int itemId,
        Vector3 startPosition)
    {
        PortableObject movingPortableObject = null;
        if (sourcePortableObject != null)
        {
            movingPortableObject = Instantiate(
                sourcePortableObject,
                startPosition,
                sourcePortableObject.transform.rotation);
        }
        else
        {
            GameObject itemObject = new GameObject($"SteamTrainBurnEnergyMove_{itemId}");
            itemObject.AddComponent<MeshFilter>();
            itemObject.AddComponent<MeshRenderer>();
            movingPortableObject = itemObject.AddComponent<PortableObject>();
        }

        if (movingPortableObject == null)
        {
            return null;
        }

        movingPortableObject.gameObject.layer = gameObject.layer;
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = startPosition;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            DestroyPortableMoveObject(movingPortableObject);
            return null;
        }

        return movingPortableObject;
    }

    private Vector3 ResolveBurnEnergyPortableMoveTargetPosition()
    {
        if (particleEffect != null)
        {
            return particleEffect.transform.position;
        }

        return transform.position;
    }

    private static void DestroyPortableMoveObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.CancelMove();
        if (Application.isPlaying)
        {
            Destroy(portableObject.gameObject);
            return;
        }

        DestroyImmediate(portableObject.gameObject);
    }

    private void ClearPendingBurnEnergyCost()
    {
        pendingBurnEnergyCostUnits = 0L;
        pendingBurnEnergyFrame = -1;
    }

    private void ClearPendingWaterCost()
    {
        pendingWaterCostUnits = 0L;
        pendingWaterFrame = -1;
    }

    private void ResetMovementParticleState()
    {
        lastMovementParticlePosition = transform.position;
        hasLastMovementParticlePosition = true;
        StopMovementParticle(true);
    }

    private void SetMovementParticleActive(bool isMoving)
    {
        SetVisualParticleActive(particleEffect, isMoving);
    }

    private void StopMovementParticle(bool clearParticles)
    {
        SetVisualParticleActive(particleEffect, false, clear: clearParticles);
    }

    private static float GetPlanarDistanceSqr(Vector3 from, Vector3 to)
    {
        float deltaX = to.x - from.x;
        float deltaZ = to.z - from.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private static class AutoDriveRoutePlanner
    {
        private const float RouteNodeMergeDistance = 0.12f;
        private const float RouteEndpointSnapDistance = 0.75f;
        private const float RouteRailConnectionSnapDistance = RailConnectionUtility.ConnectionDistance;
        private const float RouteRailConnectionMinTangentDot = 0.2f;
        private const float RouteStartForwardDotThreshold = 0.05f;
        private const float RouteTurnSharpPenalty = 240f;
        private const float RouteTurnReverseDotThreshold = -0.1f;
        private const float RouteTurnSharpDotThreshold = 0.35f;

        public readonly struct RouteSegment
        {
            public RouteSegment(Railload rail, float startDistance, float endDistance)
            {
                Rail = rail;
                StartDistance = startDistance;
                EndDistance = endDistance;
            }

            public Railload Rail { get; }
            public float StartDistance { get; }
            public float EndDistance { get; }
            public float Length => Mathf.Abs(EndDistance - StartDistance);
        }

        private readonly struct RouteEndpoint
        {
            public RouteEndpoint(int railIndex, float distanceAlongPath, Vector2 point)
            {
                RailIndex = railIndex;
                DistanceAlongPath = distanceAlongPath;
                Point = point;
            }

            public int RailIndex { get; }
            public float DistanceAlongPath { get; }
            public Vector2 Point { get; }
        }

        private readonly struct RouteConnection
        {
            public RouteConnection(
                int leftRailIndex,
                float leftDistanceAlongPath,
                int rightRailIndex,
                float rightDistanceAlongPath,
                Vector2 point)
            {
                LeftRailIndex = leftRailIndex;
                LeftDistanceAlongPath = leftDistanceAlongPath;
                RightRailIndex = rightRailIndex;
                RightDistanceAlongPath = rightDistanceAlongPath;
                Point = point;
            }

            public int LeftRailIndex { get; }
            public float LeftDistanceAlongPath { get; }
            public int RightRailIndex { get; }
            public float RightDistanceAlongPath { get; }
            public Vector2 Point { get; }
        }

        private sealed class RailInfo
        {
            public Railload Rail;
            public IReadOnlyList<Vector2Int> OccupiedCoordinates;
            public Vector2 StartPoint;
            public Vector2 EndPoint;
            public float Length;
        }

        private sealed class RouteGraphNode
        {
            public RouteGraphNode(Vector2 point)
            {
                Point = point;
            }

            public Vector2 Point;
            public readonly List<RouteGraphRailRef> RailRefs = new List<RouteGraphRailRef>(4);
        }

        private readonly struct RouteGraphRailRef
        {
            public RouteGraphRailRef(int railIndex, float distanceAlongPath)
            {
                RailIndex = railIndex;
                DistanceAlongPath = distanceAlongPath;
            }

            public int RailIndex { get; }
            public float DistanceAlongPath { get; }
        }

        private readonly struct RouteGraphNodeRef
        {
            public RouteGraphNodeRef(int nodeIndex, float distanceAlongPath)
            {
                NodeIndex = nodeIndex;
                DistanceAlongPath = distanceAlongPath;
            }

            public int NodeIndex { get; }
            public float DistanceAlongPath { get; }
        }

        private readonly struct RouteGraphEdge
        {
            public RouteGraphEdge(
                int toNodeIndex,
                int railIndex,
                float startDistanceAlongPath,
                float endDistanceAlongPath,
                float cost)
            {
                ToNodeIndex = toNodeIndex;
                RailIndex = railIndex;
                StartDistanceAlongPath = startDistanceAlongPath;
                EndDistanceAlongPath = endDistanceAlongPath;
                Cost = cost;
            }

            public int ToNodeIndex { get; }
            public int RailIndex { get; }
            public float StartDistanceAlongPath { get; }
            public float EndDistanceAlongPath { get; }
            public float Cost { get; }
        }

        private readonly struct RouteTraversalState
        {
            public RouteTraversalState(int fromNodeIndex, RouteGraphEdge edge)
            {
                FromNodeIndex = fromNodeIndex;
                Edge = edge;
            }

            public int FromNodeIndex { get; }
            public RouteGraphEdge Edge { get; }
            public int ToNodeIndex => Edge.ToNodeIndex;
        }

        private readonly struct RouteQueueEntry
        {
            public RouteQueueEntry(int stateIndex, float distance)
            {
                StateIndex = stateIndex;
                Distance = distance;
            }

            public int StateIndex { get; }
            public float Distance { get; }
        }

        private static readonly List<RailInfo> CachedRails = new List<RailInfo>(64);
        private static readonly Dictionary<string, Trainstation> CachedStationsByName =
            new Dictionary<string, Trainstation>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly List<RouteGraphNode> CachedBaseGraphNodes = new List<RouteGraphNode>(128);
        private static readonly Dictionary<int, List<RouteGraphNodeRef>> CachedBaseRailRefsByRail =
            new Dictionary<int, List<RouteGraphNodeRef>>();
        private static bool routeCacheDirty = true;
        private static bool routeCacheEventsRegistered;
        private static int routeGraphVersion;

        public static int RouteGraphVersion
        {
            get
            {
                EnsureRouteCache();
                return routeGraphVersion;
            }
        }

        public static bool TryFindStationByName(string stationName, out Trainstation station)
        {
            station = null;
            if (string.IsNullOrWhiteSpace(stationName))
            {
                return false;
            }

            EnsureRouteCache();
            string normalizedStationName = stationName.Trim();
            if (!CachedStationsByName.TryGetValue(normalizedStationName, out station)
                || station == null
                || !station.gameObject.activeInHierarchy)
            {
                station = null;
                return false;
            }

            return true;
        }

        public static float GetRouteLength(IReadOnlyList<RouteSegment> segments)
        {
            if (segments == null)
            {
                return float.PositiveInfinity;
            }

            float totalLength = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                totalLength += Mathf.Max(0f, segments[i].Length);
            }

            return totalLength;
        }

        public static bool TryBuildRoute(Train train, Trainstation destinationStation, List<RouteSegment> result)
        {
            result?.Clear();
            if (train == null
                || destinationStation == null
                || !train.TryGetCurrentRailPose(out Railload currentRail, out float currentDistanceAlongPath, out Vector2 currentPathPoint, out Vector2 currentRailTangent))
            {
                return false;
            }

            List<RailInfo> rails = CollectRails();
            if (rails.Count <= 0)
            {
                return false;
            }

            int startRailIndex = FindRailIndex(rails, currentRail);
            if (startRailIndex < 0
                || !TryFindStationRouteEndpoint(rails, destinationStation, out RouteEndpoint endEndpoint))
            {
                return false;
            }

            RouteEndpoint startEndpoint = new RouteEndpoint(startRailIndex, currentDistanceAlongPath, currentPathPoint);
            Vector2 preferredStartDirection = ResolvePreferredRouteStartDirection(train, currentRailTangent);
            return TryBuildRouteFromConnectionGraph(
                rails,
                startEndpoint,
                endEndpoint,
                preferredStartDirection,
                result);
        }

        public static bool TryBuildRoute(Trainstation startStation, Trainstation destinationStation, List<RouteSegment> result)
        {
            result?.Clear();
            if (startStation == null || destinationStation == null)
            {
                return false;
            }

            List<RailInfo> rails = CollectRails();
            if (rails.Count <= 0
                || !TryFindStationRouteEndpoint(rails, startStation, out RouteEndpoint startEndpoint)
                || !TryFindStationRouteEndpoint(rails, destinationStation, out RouteEndpoint endEndpoint))
            {
                return false;
            }

            return TryBuildRouteFromConnectionGraph(
                rails,
                startEndpoint,
                endEndpoint,
                Vector2.zero,
                result);
        }

        public static bool IsForwardRoute(Train train, IReadOnlyList<RouteSegment> segments)
        {
            if (train == null || segments == null)
            {
                return false;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                RouteSegment segment = segments[i];
                if (segment.Length <= 0.0001f)
                {
                    continue;
                }

                if (segment.Rail == null
                    || !segment.Rail.TrySampleRenderedPath(segment.StartDistance, out _, out Vector2 tangent)
                    || tangent.sqrMagnitude <= 0.0001f)
                {
                    return false;
                }

                Vector2 forward = ResolvePreferredRouteStartDirection(train, tangent);
                Vector2 travelDirection = tangent.normalized * Mathf.Sign(segment.EndDistance - segment.StartDistance);
                return Vector2.Dot(forward, travelDirection) > RouteStartForwardDotThreshold;
            }

            return true; // Already at the destination: no reverse movement is required.
        }

        private static Vector2 ResolvePreferredRouteStartDirection(Train train, Vector2 currentRailTangent)
        {
            // The physical front is fixed at Complete. Previous coasting or manual
            // reverse movement does not redefine the front for automatic driving.
            if (train is RailHandcar railHandcar
                && railHandcar.TryGetRailForwardDirection(out Vector2 railForward))
            {
                return railForward;
            }

            Vector3 transformForward = train.transform.forward;
            Vector2 forward = new Vector2(transformForward.x, transformForward.z);
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return currentRailTangent.normalized;
            }

            forward.Normalize();
            return forward;
        }

        private static List<RailInfo> CollectRails()
        {
            EnsureRouteCache();
            return CachedRails;
        }

        private static void EnsureRouteCache()
        {
            RegisterRouteCacheEvents();
            if (!routeCacheDirty)
            {
                return;
            }

            RebuildRouteCache();
            routeCacheDirty = false;
            routeGraphVersion++;
        }

        private static void RegisterRouteCacheEvents()
        {
            if (routeCacheEventsRegistered)
            {
                return;
            }

            InstallationObject.PlacementRuntimeChanged += HandleRouteCachePlacementRuntimeChanged;
            InstallationObject.PlacementRuntimeCleared += HandleRouteCachePlacementRuntimeChanged;
            routeCacheEventsRegistered = true;
        }

        private static void HandleRouteCachePlacementRuntimeChanged(InstallationObject installationObject)
        {
            if (installationObject == null
                || installationObject is Railload
                || installationObject is Trainstation)
            {
                routeCacheDirty = true;
            }
        }

        private static void RebuildRouteCache()
        {
            CachedRails.Clear();
            CachedStationsByName.Clear();

            Trainstation[] liveStations = Object.FindObjectsOfType<Trainstation>(false);
            System.Array.Sort(
                liveStations,
                (left, right) => CompareSimulationOrder(left, right));
            for (int i = 0; i < liveStations.Length; i++)
            {
                Trainstation station = liveStations[i];
                if (station == null
                    || !station.gameObject.activeInHierarchy
                    || string.IsNullOrWhiteSpace(station.StationName))
                {
                    continue;
                }

                string normalizedStationName = station.StationName.Trim();
                if (!CachedStationsByName.ContainsKey(normalizedStationName))
                {
                    CachedStationsByName.Add(normalizedStationName, station);
                }
            }

            Railload[] liveRails = Object.FindObjectsOfType<Railload>(false);
            System.Array.Sort(
                liveRails,
                (left, right) => CompareSimulationOrder(left, right));
            for (int i = 0; i < liveRails.Length; i++)
            {
                Railload rail = liveRails[i];
                if (rail == null
                    || !rail.isActiveAndEnabled
                    || !rail.TryGetPlacementRuntime(out _, out _))
                {
                    continue;
                }

                IReadOnlyList<Vector2> points = rail.RuntimeVisualPathPoints;
                if (points == null
                    || points.Count < 2
                    || !RailConnectionUtility.TryResolveConnectionEndpoints(
                        points,
                        rail.RuntimeOccupiedCoordinates,
                        out Vector2 startPoint,
                        out Vector2 endPoint)
                    || !rail.TryGetRenderedPathLength(out float length))
                {
                    continue;
                }

                CachedRails.Add(new RailInfo
                {
                    Rail = rail,
                    OccupiedCoordinates = rail.RuntimeOccupiedCoordinates,
                    StartPoint = startPoint,
                    EndPoint = endPoint,
                    Length = length
                });
            }

            RebuildCachedBaseRouteGraph();
        }

        private static void RebuildCachedBaseRouteGraph()
        {
            CachedBaseGraphNodes.Clear();
            CachedBaseRailRefsByRail.Clear();

            float maxConnectionSqrDistance = RouteRailConnectionSnapDistance * RouteRailConnectionSnapDistance;
            for (int leftRailIndex = 0; leftRailIndex < CachedRails.Count; leftRailIndex++)
            {
                for (int rightRailIndex = leftRailIndex + 1; rightRailIndex < CachedRails.Count; rightRailIndex++)
                {
                    if (!TryResolveRouteConnectionBetweenRails(
                            CachedRails,
                            leftRailIndex,
                            rightRailIndex,
                            maxConnectionSqrDistance,
                            out RouteConnection connection))
                    {
                        continue;
                    }

                    int nodeIndex = GetOrCreateRouteGraphNode(CachedBaseGraphNodes, connection.Point);
                    AddRouteGraphNodeRef(
                        CachedBaseGraphNodes,
                        CachedBaseRailRefsByRail,
                        nodeIndex,
                        connection.LeftRailIndex,
                        connection.LeftDistanceAlongPath);
                    AddRouteGraphNodeRef(
                        CachedBaseGraphNodes,
                        CachedBaseRailRefsByRail,
                        nodeIndex,
                        connection.RightRailIndex,
                        connection.RightDistanceAlongPath);
                }
            }
        }

        private static void CopyCachedBaseRouteGraph(
            List<RouteGraphNode> graphNodes,
            Dictionary<int, List<RouteGraphNodeRef>> railRefsByRail)
        {
            graphNodes.Clear();
            railRefsByRail.Clear();

            for (int nodeIndex = 0; nodeIndex < CachedBaseGraphNodes.Count; nodeIndex++)
            {
                RouteGraphNode sourceNode = CachedBaseGraphNodes[nodeIndex];
                RouteGraphNode copiedNode = new RouteGraphNode(sourceNode.Point);
                for (int refIndex = 0; refIndex < sourceNode.RailRefs.Count; refIndex++)
                {
                    copiedNode.RailRefs.Add(sourceNode.RailRefs[refIndex]);
                }

                graphNodes.Add(copiedNode);
            }

            foreach (KeyValuePair<int, List<RouteGraphNodeRef>> pair in CachedBaseRailRefsByRail)
            {
                List<RouteGraphNodeRef> sourceRefs = pair.Value;
                List<RouteGraphNodeRef> copiedRefs = new List<RouteGraphNodeRef>(
                    sourceRefs != null ? sourceRefs.Count : 0);
                if (sourceRefs != null)
                {
                    for (int refIndex = 0; refIndex < sourceRefs.Count; refIndex++)
                    {
                        copiedRefs.Add(sourceRefs[refIndex]);
                    }
                }

                railRefsByRail.Add(pair.Key, copiedRefs);
            }
        }

        private static int FindRailIndex(IReadOnlyList<RailInfo> rails, Railload rail)
        {
            if (rails == null || rail == null)
            {
                return -1;
            }

            for (int i = 0; i < rails.Count; i++)
            {
                if (rails[i]?.Rail == rail)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryFindStationRouteEndpoint(
            IReadOnlyList<RailInfo> rails,
            Trainstation station,
            out RouteEndpoint endpoint)
        {
            endpoint = default;
            if (rails == null
                || station == null
                || !station.TryGetRailCoordinate(out Vector2Int railCoordinate))
            {
                return false;
            }

            Vector2 stationPoint = new Vector2(railCoordinate.x, railCoordinate.y);
            if (TryFindStationRouteEndpoint(
                    rails,
                    stationPoint,
                    railCoordinate,
                    true,
                    out endpoint))
            {
                return true;
            }

            return TryFindStationRouteEndpoint(
                rails,
                stationPoint,
                railCoordinate,
                false,
                out endpoint);
        }

        private static bool TryFindStationRouteEndpoint(
            IReadOnlyList<RailInfo> rails,
            Vector2 stationPoint,
            Vector2Int railCoordinate,
            bool requireOccupiedCoordinate,
            out RouteEndpoint endpoint)
        {
            endpoint = default;
            float bestSqrDistance = RouteEndpointSnapDistance * RouteEndpointSnapDistance;
            bool found = false;
            for (int i = 0; i < rails.Count; i++)
            {
                RailInfo rail = rails[i];
                if (rail == null
                    || rail.Rail == null
                    || (requireOccupiedCoordinate
                        && !RailOccupiesCoordinate(rail, railCoordinate))
                    || !rail.Rail.TryFindNearestRenderedPathSample(
                        stationPoint,
                        out float distanceAlongPath,
                        out Vector2 pathPoint,
                        out _,
                        out float sqrDistance)
                    || sqrDistance > bestSqrDistance)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                endpoint = new RouteEndpoint(i, distanceAlongPath, pathPoint);
                found = true;
            }

            return found;
        }

        private static bool RailOccupiesCoordinate(RailInfo rail, Vector2Int coordinate)
        {
            IReadOnlyList<Vector2Int> occupiedCoordinates = rail?.OccupiedCoordinates;
            if (occupiedCoordinates == null)
            {
                return false;
            }

            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                if (occupiedCoordinates[i] == coordinate)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBuildRouteFromConnectionGraph(
            List<RailInfo> rails,
            RouteEndpoint startEndpoint,
            RouteEndpoint endEndpoint,
            Vector2 preferredStartDirection,
            List<RouteSegment> result)
        {
            if (rails == null || result == null)
            {
                return false;
            }

            result.Clear();
            List<RouteGraphNode> graphNodes = new List<RouteGraphNode>(Mathf.Max(4, rails.Count + 2));
            Dictionary<int, List<RouteGraphNodeRef>> railRefsByRail = new Dictionary<int, List<RouteGraphNodeRef>>();
            CopyCachedBaseRouteGraph(graphNodes, railRefsByRail);
            int startNodeIndex = GetOrCreateRouteGraphNode(graphNodes, startEndpoint.Point);
            AddRouteGraphNodeRef(graphNodes, railRefsByRail, startNodeIndex, startEndpoint.RailIndex, startEndpoint.DistanceAlongPath);
            int endNodeIndex = GetOrCreateRouteGraphNode(graphNodes, endEndpoint.Point);
            AddRouteGraphNodeRef(graphNodes, railRefsByRail, endNodeIndex, endEndpoint.RailIndex, endEndpoint.DistanceAlongPath);

            if (!TryBuildRouteGraphAdjacency(graphNodes.Count, railRefsByRail, out List<RouteGraphEdge>[] adjacency))
            {
                return false;
            }

            return TryFindRouteGraphPath(
                rails,
                adjacency,
                startNodeIndex,
                endNodeIndex,
                preferredStartDirection,
                result);
        }

        private static bool TryResolveRouteConnectionBetweenRails(
            IReadOnlyList<RailInfo> rails,
            int leftRailIndex,
            int rightRailIndex,
            float maxConnectionSqrDistance,
            out RouteConnection connection)
        {
            connection = default;
            RailInfo leftRail = rails[leftRailIndex];
            RailInfo rightRail = rails[rightRailIndex];
            if (leftRail?.Rail == null || rightRail?.Rail == null)
            {
                return false;
            }

            bool found = false;
            float bestScore = float.PositiveInfinity;
            ConsiderRouteEndpointConnectionCandidate(rails, leftRailIndex, rightRailIndex, true, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);
            ConsiderRouteEndpointConnectionCandidate(rails, leftRailIndex, rightRailIndex, false, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);
            ConsiderRouteEndpointConnectionCandidate(rails, rightRailIndex, leftRailIndex, true, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);
            ConsiderRouteEndpointConnectionCandidate(rails, rightRailIndex, leftRailIndex, false, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);

            return found;
        }

        private static void ConsiderRouteEndpointConnectionCandidate(
            IReadOnlyList<RailInfo> rails,
            int endpointRailIndex,
            int otherRailIndex,
            bool useStartEndpoint,
            float maxConnectionSqrDistance,
            ref bool found,
            ref float bestScore,
            ref RouteConnection bestConnection)
        {
            RailInfo endpointRail = rails[endpointRailIndex];
            RailInfo otherRail = rails[otherRailIndex];
            if (endpointRail?.Rail == null
                || otherRail?.Rail == null
                || !endpointRail.Rail.TryGetRenderedEndpointSample(
                    useStartEndpoint,
                    out float endpointDistanceAlongPath,
                    out Vector2 endpointPathPoint,
                    out Vector2 endpointTangent)
                || !otherRail.Rail.TryFindNearestRenderedPathSample(
                    endpointPathPoint,
                    out float otherDistanceAlongPath,
                    out Vector2 otherPathPoint,
                    out Vector2 otherTangent,
                    out float otherSqrDistance)
                || otherSqrDistance > maxConnectionSqrDistance)
            {
                return;
            }

            Vector2 normalizedEndpointTangent = endpointTangent.sqrMagnitude > 0.0001f
                ? endpointTangent.normalized
                : Vector2.zero;
            Vector2 normalizedOtherTangent = otherTangent.sqrMagnitude > 0.0001f
                ? otherTangent.normalized
                : Vector2.zero;
            if (normalizedEndpointTangent.sqrMagnitude <= 0.0001f
                || normalizedOtherTangent.sqrMagnitude <= 0.0001f
                || Mathf.Abs(Vector2.Dot(normalizedEndpointTangent, normalizedOtherTangent))
                   < RouteRailConnectionMinTangentDot)
            {
                return;
            }

            float score = otherSqrDistance;
            if (found && score >= bestScore)
            {
                return;
            }

            found = true;
            bestScore = score;
            bestConnection = new RouteConnection(
                endpointRailIndex,
                endpointDistanceAlongPath,
                otherRailIndex,
                otherDistanceAlongPath,
                (endpointPathPoint + otherPathPoint) * 0.5f);
        }

        private static int GetOrCreateRouteGraphNode(List<RouteGraphNode> graphNodes, Vector2 point)
        {
            float maxNodeMergeSqrDistance = RouteNodeMergeDistance * RouteNodeMergeDistance;
            for (int i = 0; i < graphNodes.Count; i++)
            {
                if ((graphNodes[i].Point - point).sqrMagnitude <= maxNodeMergeSqrDistance)
                {
                    return i;
                }
            }

            graphNodes.Add(new RouteGraphNode(point));
            return graphNodes.Count - 1;
        }

        private static void AddRouteGraphNodeRef(
            List<RouteGraphNode> graphNodes,
            Dictionary<int, List<RouteGraphNodeRef>> railRefsByRail,
            int nodeIndex,
            int railIndex,
            float distanceAlongPath)
        {
            RouteGraphNode node = graphNodes[nodeIndex];
            float clampedDistance = Mathf.Max(0f, distanceAlongPath);
            for (int i = 0; i < node.RailRefs.Count; i++)
            {
                RouteGraphRailRef existingRef = node.RailRefs[i];
                if (existingRef.RailIndex == railIndex
                    && Mathf.Abs(existingRef.DistanceAlongPath - clampedDistance) <= 0.01f)
                {
                    return;
                }
            }

            node.RailRefs.Add(new RouteGraphRailRef(railIndex, clampedDistance));
            if (!railRefsByRail.TryGetValue(railIndex, out List<RouteGraphNodeRef> railRefs))
            {
                railRefs = new List<RouteGraphNodeRef>(4);
                railRefsByRail.Add(railIndex, railRefs);
            }

            railRefs.Add(new RouteGraphNodeRef(nodeIndex, clampedDistance));
        }

        private static bool TryBuildRouteGraphAdjacency(
            int nodeCount,
            Dictionary<int, List<RouteGraphNodeRef>> railRefsByRail,
            out List<RouteGraphEdge>[] adjacency)
        {
            adjacency = null;
            if (nodeCount <= 0)
            {
                return false;
            }

            adjacency = new List<RouteGraphEdge>[nodeCount];
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                adjacency[nodeIndex] = new List<RouteGraphEdge>(4);
            }

            foreach (KeyValuePair<int, List<RouteGraphNodeRef>> pair in railRefsByRail)
            {
                List<RouteGraphNodeRef> refs = pair.Value;
                if (refs == null || refs.Count <= 1)
                {
                    continue;
                }

                refs.Sort((left, right) => left.DistanceAlongPath.CompareTo(right.DistanceAlongPath));
                for (int refIndex = 1; refIndex < refs.Count; refIndex++)
                {
                    RouteGraphNodeRef previousRef = refs[refIndex - 1];
                    RouteGraphNodeRef currentRef = refs[refIndex];
                    if (previousRef.NodeIndex == currentRef.NodeIndex)
                    {
                        continue;
                    }

                    float segmentLength = Mathf.Abs(currentRef.DistanceAlongPath - previousRef.DistanceAlongPath);
                    if (segmentLength <= 0.0001f)
                    {
                        continue;
                    }

                    adjacency[previousRef.NodeIndex].Add(new RouteGraphEdge(currentRef.NodeIndex, pair.Key, previousRef.DistanceAlongPath, currentRef.DistanceAlongPath, segmentLength));
                    adjacency[currentRef.NodeIndex].Add(new RouteGraphEdge(previousRef.NodeIndex, pair.Key, currentRef.DistanceAlongPath, previousRef.DistanceAlongPath, segmentLength));
                }
            }

            return true;
        }

        private static bool TryFindRouteGraphPath(
            IReadOnlyList<RailInfo> rails,
            List<RouteGraphEdge>[] adjacency,
            int startNodeIndex,
            int endNodeIndex,
            Vector2 preferredStartDirection,
            List<RouteSegment> result)
        {
            if (adjacency == null
                || startNodeIndex < 0
                || startNodeIndex >= adjacency.Length
                || endNodeIndex < 0
                || endNodeIndex >= adjacency.Length)
            {
                return false;
            }

            int stateCount = 0;
            for (int nodeIndex = 0; nodeIndex < adjacency.Length; nodeIndex++)
            {
                List<RouteGraphEdge> edges = adjacency[nodeIndex];
                if (edges != null)
                {
                    stateCount += edges.Count;
                }
            }

            if (stateCount <= 0)
            {
                return false;
            }

            int[] stateOffsets = new int[adjacency.Length];
            RouteTraversalState[] states = new RouteTraversalState[stateCount];
            int nextStateIndex = 0;
            for (int nodeIndex = 0; nodeIndex < adjacency.Length; nodeIndex++)
            {
                stateOffsets[nodeIndex] = nextStateIndex;
                List<RouteGraphEdge> edges = adjacency[nodeIndex];
                if (edges == null)
                {
                    continue;
                }

                for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                {
                    states[nextStateIndex++] = new RouteTraversalState(nodeIndex, edges[edgeIndex]);
                }
            }

            float[] distances = new float[stateCount];
            int[] previousStates = new int[stateCount];
            bool[] visited = new bool[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                distances[i] = float.PositiveInfinity;
                previousStates[i] = -1;
            }

            int bestEndStateIndex = -1;
            List<RouteGraphEdge> startEdges = adjacency[startNodeIndex];
            if (startEdges == null || startEdges.Count <= 0)
            {
                return false;
            }

            List<RouteQueueEntry> openSet = new List<RouteQueueEntry>(Mathf.Max(4, startEdges.Count));
            for (int edgeIndex = 0; edgeIndex < startEdges.Count; edgeIndex++)
            {
                RouteGraphEdge edge = startEdges[edgeIndex];
                int stateIndex = stateOffsets[startNodeIndex] + edgeIndex;
                float startPenalty = ResolveRouteStartEdgePenalty(
                    rails,
                    edge,
                    preferredStartDirection);
                if (float.IsPositiveInfinity(startPenalty))
                {
                    continue;
                }

                distances[stateIndex] = Mathf.Max(0.01f, edge.Cost) + startPenalty;
                PushRouteQueue(openSet, new RouteQueueEntry(stateIndex, distances[stateIndex]));
            }

            while (TryPopRouteQueue(openSet, out RouteQueueEntry queueEntry))
            {
                int currentStateIndex = queueEntry.StateIndex;
                if (currentStateIndex < 0
                    || currentStateIndex >= stateCount
                    || visited[currentStateIndex]
                    || queueEntry.Distance > distances[currentStateIndex] + 0.0001f)
                {
                    continue;
                }

                RouteTraversalState currentState = states[currentStateIndex];
                if (currentState.ToNodeIndex == endNodeIndex)
                {
                    bestEndStateIndex = currentStateIndex;
                    break;
                }

                visited[currentStateIndex] = true;
                List<RouteGraphEdge> nextEdges = adjacency[currentState.ToNodeIndex];
                if (nextEdges == null || nextEdges.Count <= 0)
                {
                    continue;
                }

                int nextStateBaseIndex = stateOffsets[currentState.ToNodeIndex];
                for (int edgeIndex = 0; edgeIndex < nextEdges.Count; edgeIndex++)
                {
                    int nextStateIndexValue = nextStateBaseIndex + edgeIndex;
                    if (visited[nextStateIndexValue])
                    {
                        continue;
                    }

                    RouteGraphEdge nextEdge = nextEdges[edgeIndex];
                    float candidateDistance =
                        queueEntry.Distance
                        + Mathf.Max(0.01f, nextEdge.Cost)
                        + ResolveRouteTurnPenalty(
                            rails,
                            currentState.Edge,
                            nextEdge);
                    if (candidateDistance >= distances[nextStateIndexValue])
                    {
                        continue;
                    }

                    distances[nextStateIndexValue] = candidateDistance;
                    previousStates[nextStateIndexValue] = currentStateIndex;
                    PushRouteQueue(openSet, new RouteQueueEntry(nextStateIndexValue, candidateDistance));
                }
            }

            if (bestEndStateIndex < 0 || float.IsPositiveInfinity(distances[bestEndStateIndex]))
            {
                return false;
            }

            List<RouteSegment> reversedSegments = new List<RouteSegment>(16);
            for (int currentStateIndex = bestEndStateIndex;
                 currentStateIndex >= 0;
                 currentStateIndex = previousStates[currentStateIndex])
            {
                RouteGraphEdge edge = states[currentStateIndex].Edge;
                AppendRouteSegment(
                    reversedSegments,
                    rails[edge.RailIndex].Rail,
                    edge.StartDistanceAlongPath,
                    edge.EndDistanceAlongPath);
            }

            result.Clear();
            for (int i = reversedSegments.Count - 1; i >= 0; i--)
            {
                AppendRouteSegment(result, reversedSegments[i].Rail, reversedSegments[i].StartDistance, reversedSegments[i].EndDistance);
            }

            return result.Count > 0;
        }

        private static void PushRouteQueue(List<RouteQueueEntry> queue, RouteQueueEntry entry)
        {
            queue.Add(entry);
            int childIndex = queue.Count - 1;
            while (childIndex > 0)
            {
                int parentIndex = (childIndex - 1) / 2;
                if (queue[parentIndex].Distance <= entry.Distance)
                {
                    break;
                }

                queue[childIndex] = queue[parentIndex];
                childIndex = parentIndex;
            }

            queue[childIndex] = entry;
        }

        private static bool TryPopRouteQueue(List<RouteQueueEntry> queue, out RouteQueueEntry entry)
        {
            entry = default;
            if (queue == null || queue.Count <= 0)
            {
                return false;
            }

            entry = queue[0];
            int lastIndex = queue.Count - 1;
            RouteQueueEntry lastEntry = queue[lastIndex];
            queue.RemoveAt(lastIndex);
            if (lastIndex <= 0)
            {
                return true;
            }

            int parentIndex = 0;
            while (true)
            {
                int leftChildIndex = parentIndex * 2 + 1;
                if (leftChildIndex >= queue.Count)
                {
                    break;
                }

                int rightChildIndex = leftChildIndex + 1;
                int bestChildIndex =
                    rightChildIndex < queue.Count
                    && queue[rightChildIndex].Distance < queue[leftChildIndex].Distance
                        ? rightChildIndex
                        : leftChildIndex;
                if (queue[bestChildIndex].Distance >= lastEntry.Distance)
                {
                    break;
                }

                queue[parentIndex] = queue[bestChildIndex];
                parentIndex = bestChildIndex;
            }

            queue[parentIndex] = lastEntry;
            return true;
        }

        private static float ResolveRouteStartEdgePenalty(
            IReadOnlyList<RailInfo> rails,
            RouteGraphEdge edge,
            Vector2 preferredStartDirection)
        {
            if (preferredStartDirection.sqrMagnitude <= 0.0001f
                || rails == null
                || edge.RailIndex < 0
                || edge.RailIndex >= rails.Count
                || !TryResolveRouteEdgeTravelDirectionAtPosition(
                    rails[edge.RailIndex],
                    edge,
                    0.05f,
                    out Vector2 edgeTravelDirection))
            {
                return 0f;
            }

            float directionDot = Vector2.Dot(preferredStartDirection, edgeTravelDirection);
            return directionDot <= RouteStartForwardDotThreshold
                ? float.PositiveInfinity
                : 0f;
        }

        private static float ResolveRouteTurnPenalty(
            IReadOnlyList<RailInfo> rails,
            RouteGraphEdge incomingEdge,
            RouteGraphEdge outgoingEdge)
        {
            if (rails == null
                || incomingEdge.RailIndex < 0
                || incomingEdge.RailIndex >= rails.Count
                || outgoingEdge.RailIndex < 0
                || outgoingEdge.RailIndex >= rails.Count
                || !TryResolveRouteEdgeTravelDirectionAtPosition(
                    rails[incomingEdge.RailIndex],
                    incomingEdge,
                    0.95f,
                    out Vector2 incomingDirection)
                || !TryResolveRouteEdgeTravelDirectionAtPosition(
                    rails[outgoingEdge.RailIndex],
                    outgoingEdge,
                    0.05f,
                    out Vector2 outgoingDirection))
            {
                return 0f;
            }

            float directionDot = Vector2.Dot(incomingDirection, outgoingDirection);
            if (directionDot < RouteTurnReverseDotThreshold)
            {
                return float.PositiveInfinity;
            }

            if (directionDot >= RouteTurnSharpDotThreshold)
            {
                return 0f;
            }

            float turnPenaltyT = 1f - Mathf.InverseLerp(
                RouteTurnReverseDotThreshold,
                RouteTurnSharpDotThreshold,
                directionDot);
            return turnPenaltyT * RouteTurnSharpPenalty;
        }

        private static bool TryResolveRouteEdgeTravelDirectionAtPosition(
            RailInfo rail,
            RouteGraphEdge edge,
            float normalizedPosition,
            out Vector2 direction)
        {
            direction = Vector2.zero;
            if (rail?.Rail == null)
            {
                return false;
            }

            float directionSign = Mathf.Sign(edge.EndDistanceAlongPath - edge.StartDistanceAlongPath);
            if (Mathf.Abs(directionSign) <= 0.0001f)
            {
                return false;
            }

            float sampleDistanceAlongPath = Mathf.Lerp(
                edge.StartDistanceAlongPath,
                edge.EndDistanceAlongPath,
                Mathf.Clamp01(normalizedPosition));
            if (!rail.Rail.TrySampleRenderedPath(
                    sampleDistanceAlongPath,
                    out _,
                    out Vector2 tangent)
                || tangent.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            direction = tangent * directionSign;
            direction.Normalize();
            return true;
        }

        private static void AppendRouteSegment(
            List<RouteSegment> segments,
            Railload rail,
            float startDistance,
            float endDistance)
        {
            if (segments == null
                || rail == null
                || Mathf.Abs(endDistance - startDistance) <= 0.0001f)
            {
                return;
            }

            if (segments.Count > 0)
            {
                RouteSegment lastSegment = segments[segments.Count - 1];
                if (lastSegment.Rail == rail
                    && Mathf.Abs(lastSegment.EndDistance - startDistance) <= 0.01f)
                {
                    segments[segments.Count - 1] = new RouteSegment(rail, lastSegment.StartDistance, endDistance);
                    return;
                }
            }

            segments.Add(new RouteSegment(rail, startDistance, endDistance));
        }
    }
}
