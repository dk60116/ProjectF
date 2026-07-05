using System.Collections.Generic;
using UnityEngine;

public class SteamTrain : RailHandcar
{
    private static readonly List<AutoDriveRoutePlanner.RouteSegment> SharedDebugRouteSegmentScratch =
        new List<AutoDriveRoutePlanner.RouteSegment>(32);

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
        Arrived = 10
    }

    private enum DriveMotionOutcome
    {
        Applied = 0,
        BlockedByFuel = 1
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
    private const int AutoDriveRouteCursorLookAheadSegments = 2;
    private const float AutoDrivePreferredBranchSelectionDistance =
        AutoDriveLookAheadDistance + AutoDriveRouteSegmentTolerance;
    private const float AutoDriveDockSnapDistance = 0.05f;
    private const float AutoDriveDockApproachMinInputMagnitude = 0.12f;
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
    private float storedBurnEnergy;
    private float burnEnergyGaugeCapacity;
    private float pendingBurnEnergyCost;
    private int pendingBurnEnergyFrame = -1;
    private float pendingWaterCost;
    private int pendingWaterFrame = -1;
    private Vector3 waterPipeDefaultLocalPosition;
    private Quaternion waterPipeDefaultLocalRotation = Quaternion.identity;
    private Vector3 waterPipeTargetLocalPosition;
    private Quaternion waterPipeTargetLocalRotation = Quaternion.identity;
    private bool waterPipeDefaultsCaptured;
    private bool waterPipeTargetActive;
    private bool waterPipeAnimating;
    private bool waterPipeTransferReady;
    private bool autoDriveEnabled;
    private string autoDriveTargetAStationName = string.Empty;
    private string autoDriveTargetBStationName = string.Empty;
    private AutoDriveFuelFilter autoDriveFuelFilter;
    private AutoDriveFreightFilter autoDriveFreightFilter;
    private AutoDriveStatus autoDriveStatus;
    private string autoDriveCurrentTargetStationName = string.Empty;
    private string autoDriveNextTargetStationName = string.Empty;
    private Vector2Int activeWaterPipeDirectionFromTrainToPipe;
    private bool waterPipeDockLockActive;
    private Railload lockedWaterPipeDockRail;
    private float lockedWaterPipeDockDistanceAlongPath;
    private Vector2 lockedWaterPipeDockFacing;
    private Vector2Int lockedWaterPipeDockDirectionFromTrainToPipe;
    private Vector2Int lockedWaterPipeDockCoordinate;
    private readonly List<PortableObject> burnEnergyPortableMoveBuffer = new List<PortableObject>();
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveRouteSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveFixedRouteSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveRouteScratchSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<AutoDriveRoutePlanner.RouteSegment> autoDriveRouteReferenceScratchSegments = new List<AutoDriveRoutePlanner.RouteSegment>(32);
    private readonly List<Train> autoDriveConnectedTrainScratch = new List<Train>(8);
    private readonly Queue<Vector2Int> waterPipeSearchQueue = new Queue<Vector2Int>(32);
    private readonly Queue<Train> autoDriveConnectedTrainQueue = new Queue<Train>(8);
    private readonly HashSet<Vector2Int> waterPipeSearchVisited = new HashSet<Vector2Int>();
    private readonly HashSet<Train> autoDriveConnectedTrainVisited = new HashSet<Train>();
    private string autoDriveRouteTargetStationName = string.Empty;
    private string autoDriveCachedRouteReferenceTargetStationName = string.Empty;
    private int autoDriveRouteReferenceTrainInstanceId;
    private int autoDriveRouteSegmentCursor;
    private string autoDriveLastArrivedStationName = string.Empty;
    private string autoDriveFixedRouteStartStationName = string.Empty;
    private string autoDriveFixedRouteEndStationName = string.Empty;
    private string autoDriveResolvedTargetStationName = string.Empty;
    private string autoDriveResolvedNextStationName = string.Empty;
    private Trainstation autoDriveResolvedTargetStation;
    private RailHandcar autoDriveCachedRouteReferenceTrain;
    private float autoDriveRouteRefreshTimer;
    private float autoDriveStationWaitTimer;

    public float ObjectInfoStoredBurnEnergy => Mathf.Max(0f, storedBurnEnergy);
    public float ObjectInfoBurnEnergyGaugeCapacity => Mathf.Max(0f, burnEnergyGaugeCapacity, storedBurnEnergy);
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
    public bool AutoDriveEnabled => false;
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

    public bool TryGetAutoDriveDebugRouteSegments(List<AutoDriveDebugRouteSegment> result)
    {
        result?.Clear();
        return false;
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
        station = null;
        return false;
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

    protected override bool CanConnectToTrainAtOffset(
        Train other,
        Vector2 offsetToOther,
        Vector2 forwardTangent)
    {
        return base.CanConnectToTrainAtOffset(other, offsetToOther, forwardTangent)
               && !IsConnectionOffsetAhead(offsetToOther, forwardTangent);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResetMovementParticleState();
        CaptureWaterPipeDefaults();
        ResetWaterPipeImmediate(false);
    }

    protected override void OnDisable()
    {
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        ResetWaterPipeImmediate(false);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        StopMovementParticle(true);
        hasLastMovementParticlePosition = false;
        lastDrivenInputFrame = -1;
        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        storedBurnEnergy = 0f;
        burnEnergyGaugeCapacity = 0f;
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
        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        HandleResolvedDriveMotion(
            worldMoveDirection,
            moveSpeed,
            deltaTime,
            mountedPlayer);
    }

    private DriveMotionOutcome HandleResolvedDriveMotion(
        Vector3 resolvedMoveDirection,
        float moveSpeed,
        float deltaTime,
        Player mountedPlayer)
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
            return DriveMotionOutcome.BlockedByFuel;
        }

        if (RequiresPoweredBurnEnergy(resolvedMoveDirection, deltaTime, out float burnEnergyCost)
            && !TryEnsureBurnEnergyAvailable(burnEnergyCost, mountedPlayer))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
            return DriveMotionOutcome.BlockedByFuel;
        }

        if (burnEnergyCost > BurnEnergyEpsilon)
        {
            pendingBurnEnergyCost = burnEnergyCost;
            pendingBurnEnergyFrame = Time.frameCount;
        }

        if (waterCost > WaterEpsilon)
        {
            pendingWaterCost = waterCost;
            pendingWaterFrame = Time.frameCount;
        }

        lastDrivenInputFrame = Time.frameCount;
        base.HandleMountedInput(resolvedMoveDirection, moveSpeed, deltaTime);
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
            UpdateWaterPipeVisual(Time.deltaTime);
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
            SpendStoredBurnEnergy(pendingBurnEnergyCost);
        }

        if (isDrivenThisFrame
            && pendingWaterFrame == Time.frameCount
            && CurrentVehicleSpeed > BurnEnergyDrivingSpeedThreshold)
        {
            SpendStoredWater(pendingWaterCost);
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

        ClearPendingBurnEnergyCost();
        ClearPendingWaterCost();
        SetMovementParticleActive(isDrivenAndMoving);
        UpdateWaterPipeVisual(Time.deltaTime);
        lastMovementParticlePosition = currentPosition;
    }

    public void CaptureBurnEnergyState(out float storedEnergy, out float gaugeCapacity)
    {
        storedEnergy = Mathf.Max(0f, storedBurnEnergy);
        gaugeCapacity = Mathf.Max(0f, burnEnergyGaugeCapacity, storedEnergy);
    }

    public void ApplyBurnEnergyState(float storedEnergy, float gaugeCapacity)
    {
        storedBurnEnergy = Mathf.Max(0f, storedEnergy);
        burnEnergyGaugeCapacity = Mathf.Max(0f, gaugeCapacity, storedBurnEnergy);
    }

    protected override bool TryApplyCustomIdleDocking(
        RailSample currentSample,
        Vector2 currentFacing,
        float deltaTime)
    {
        if (deltaTime <= 0f
            || currentSample.Rail == null
            || !HasFluidStorageSpace
            || !TryResolveWaterPipeDockSample(
                currentSample,
                currentFacing,
                out RailSample dockSample,
                out float signedPathDelta,
                out Vector2Int directionFromTrainToPipe,
                out Vector2 dockFacing))
        {
            RequestWaterPipeRetract();
            return false;
        }

        SetWaterPipeDockTarget(
            directionFromTrainToPipe,
            Mathf.Abs(signedPathDelta) <= ResolveDockCompleteDistance());

        if (TryApplyDockingToSample(
            currentSample,
            dockFacing,
            dockSample,
            signedPathDelta,
            deltaTime,
            true))
        {
            return true;
        }

        RequestWaterPipeRetract();
        return false;
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
        while (storedBurnEnergy + BurnEnergyEpsilon < requiredEnergy)
        {
            if (!TryConsumeOneBurnEnergyItem(mountedPlayer, out int gainedEnergy))
            {
                break;
            }

            storedBurnEnergy += gainedEnergy;
            burnEnergyGaugeCapacity = Mathf.Max(burnEnergyGaugeCapacity, storedBurnEnergy, 1f);
        }

        return storedBurnEnergy + BurnEnergyEpsilon >= requiredEnergy;
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

    private bool TryGetRearFreightCar(out FreightCar freightCar)
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
                lockedWaterPipeDockDistanceAlongPath,
                out Vector2 pathPoint,
                out Vector2 tangent))
        {
            ClearWaterPipeDockLock();
            return false;
        }

        signedPathDelta = lockedWaterPipeDockDistanceAlongPath - currentSample.DistanceAlongPath;
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
        dockSample.DistanceAlongPath = lockedWaterPipeDockDistanceAlongPath;
        dockSample.Point = pathPoint;
        dockSample.Tangent = tangent;
        dockSample.SqrDistance = (pathPoint - currentSample.Point).sqrMagnitude;
        directionFromTrainToPipe = lockedWaterPipeDockDirectionFromTrainToPipe;
        dockFacing = lockedWaterPipeDockFacing;
        return true;
    }

    private bool TryValidateLockedWaterPipeDock()
    {
        return lockedWaterPipeDockDirectionFromTrainToPipe != Vector2Int.zero
               && TryGetActivePipeAtCoordinate(
                   lockedWaterPipeDockCoordinate,
                   out Pipe pipe,
                   out Quaternion pipeRotation)
               && pipe.HasConnectionTowards(pipeRotation, -lockedWaterPipeDockDirectionFromTrainToPipe);
    }

    private void LockWaterPipeDock(
        RailSample dockSample,
        Vector2 currentFacing,
        Vector2Int directionFromTrainToPipe,
        Vector2Int pipeCoordinate)
    {
        waterPipeDockLockActive = true;
        lockedWaterPipeDockRail = dockSample.Rail;
        lockedWaterPipeDockDistanceAlongPath = dockSample.DistanceAlongPath;
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
        lockedWaterPipeDockDistanceAlongPath = 0f;
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
                if (!TryGetActivePipeAtCoordinate(candidatePipeCoordinate, out Pipe pipe, out Quaternion pipeRotation))
                {
                    continue;
                }

                bool hasCheckedWaterSource = false;
                bool hasWaterSource = false;
                for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
                {
                    Vector2Int directionFromPipeToTrain = CardinalDirections[directionIndex];
                    if (!pipe.HasConnectionTowards(pipeRotation, directionFromPipeToTrain))
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
                        hasWaterSource = WaterPipeNetworkHasWaterSource(candidatePipeCoordinate, waterItemId);
                        hasCheckedWaterSource = true;
                    }

                    float score = candidatePathDistance
                                  + candidateSqrDistance * 0.25f
                                  + Mathf.Abs(1f - alongDistance) * 0.2f
                                  + lateralDistance * 0.35f
                                  + (hasWaterSource ? 0f : 0.1f);
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
        autoDriveTargetAStationName = string.Empty;
        autoDriveTargetBStationName = string.Empty;
        autoDriveFuelFilter = AutoDriveFuelFilter.Free;
        autoDriveFreightFilter = AutoDriveFreightFilter.Free;
        ResetAutoDriveRuntimeState();
        SetAutoDriveStatus(AutoDriveStatus.Idle, string.Empty, string.Empty);
    }

    private void ResetAutoDriveRuntimeState()
    {
        autoDriveRouteSegments.Clear();
        autoDriveRouteScratchSegments.Clear();
        autoDriveRouteReferenceScratchSegments.Clear();
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveCachedRouteReferenceTargetStationName = string.Empty;
        autoDriveCachedRouteReferenceTrain = null;
        autoDriveRouteReferenceTrainInstanceId = 0;
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
    }

    private bool HasAutoDriveFixedRouteForCurrentTargets()
    {
        return autoDriveFixedRouteSegments.Count > 0
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

        return TryAlignAutoDriveRouteSegmentsToCurrentPose(routeReferenceTrain, result);
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
            AutoDriveStatus.WaitingForFreight => $"AutoDrive: Waiting Freight{targetSuffix}",
            AutoDriveStatus.WaitingForPath => $"AutoDrive: No Path{targetSuffix}",
            AutoDriveStatus.WaitingForClearTrack => $"AutoDrive: Track Busy{targetSuffix}",
            AutoDriveStatus.Arrived => $"AutoDrive: Arrived{targetSuffix}",
            _ => "AutoDrive: Ready"
        };
    }

    private static string NormalizeAutoDriveStationName(string stationName)
    {
        return string.IsNullOrWhiteSpace(stationName)
            ? string.Empty
            : stationName.Trim();
    }

    private Vector3 ResolveAutoDriveMoveDirection(float deltaTime)
    {
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
            SetAutoDriveStatus(
                HasAnyAutoDriveTarget ? AutoDriveStatus.WaitingForPath : AutoDriveStatus.NoTarget,
                string.Empty,
                string.Empty);
            return Vector3.zero;
        }

        autoDriveResolvedTargetStation = targetStation;
        autoDriveResolvedTargetStationName = targetStationName;
        autoDriveResolvedNextStationName = nextTargetStationName;

        bool hasDockDistance = TryGetAutoDriveTargetDockDistance(
            targetStation,
            out float remainingDockDistance);
        if (hasDockDistance && remainingDockDistance <= ResolveDockCompleteDistance())
        {
            HandleAutoDriveArrived(targetStationName, nextTargetStationName);
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

        if (!TrySatisfyAutoDriveFilters(targetStationName, nextTargetStationName))
        {
            return Vector3.zero;
        }

        if (!TryEnsureAutoDriveRoute(targetStation, targetStationName, deltaTime))
        {
            SetAutoDriveStatus(AutoDriveStatus.WaitingForPath, targetStationName, nextTargetStationName);
            return Vector3.zero;
        }

        if (!TryResolveAutoDriveRouteMoveDirection(out Vector3 moveDirection))
        {
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

    private bool TryIsDockedAtTargetStation(Trainstation targetStation, out bool inDockCaptureRange)
    {
        inDockCaptureRange = false;
        if (!TryGetAutoDriveTargetDockDistance(targetStation, out float remainingDistance))
        {
            return false;
        }

        inDockCaptureRange = remainingDistance <= ResolveAutoDriveDockApproachDistance();
        return remainingDistance <= ResolveDockCompleteDistance();
    }

    private void HandleAutoDriveArrived(string currentStationName, string nextTargetStationName)
    {
        ResetVehicleMotion();
        autoDriveLastArrivedStationName = NormalizeAutoDriveStationName(currentStationName);
        autoDriveRouteSegments.Clear();
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveRouteReferenceTrainInstanceId = 0;
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

    private bool TrySatisfyAutoDriveFilters(string currentTargetStationName, string nextTargetStationName)
    {
        if (!TryEvaluateFuelFilterSatisfied())
        {
            SetAutoDriveStatus(AutoDriveStatus.WaitingForFuel, currentTargetStationName, nextTargetStationName);
            return false;
        }

        if (!TryEvaluateFreightFilterSatisfied())
        {
            SetAutoDriveStatus(AutoDriveStatus.WaitingForFreight, currentTargetStationName, nextTargetStationName);
            return false;
        }

        return true;
    }

    private bool TryEvaluateFuelFilterSatisfied()
    {
        if (!TryGetRearFreightCar(out FreightCar rearFreightCar))
        {
            return false;
        }

        rearFreightCar.GetAutoDriveStorageSummary(out int itemCount, out int capacity, out bool hasStorage);
        if (!hasStorage || capacity <= 0)
        {
            return false;
        }

        return autoDriveFuelFilter == AutoDriveFuelFilter.Full
            ? itemCount >= capacity
            : itemCount < capacity;
    }

    private bool TryEvaluateFreightFilterSatisfied()
    {
        CollectConnectedFreightCars(out int totalItemCount, out int totalCapacity, out bool hasStorage);
        if (!hasStorage || totalCapacity <= 0)
        {
            return false;
        }

        return autoDriveFreightFilter switch
        {
            AutoDriveFreightFilter.Full => totalItemCount >= totalCapacity,
            AutoDriveFreightFilter.Empty => totalItemCount <= 0,
            _ => totalItemCount < totalCapacity
        };
    }

    private void CollectConnectedFreightCars(out int totalItemCount, out int totalCapacity, out bool hasStorage)
    {
        totalItemCount = 0;
        totalCapacity = 0;
        hasStorage = false;

        Queue<Train> queue = new Queue<Train>(8);
        HashSet<Train> visited = new HashSet<Train>();
        queue.Enqueue(this);
        visited.Add(this);
        while (queue.Count > 0)
        {
            Train train = queue.Dequeue();
            if (train is FreightCar freightCar)
            {
                freightCar.GetAutoDriveStorageSummary(out int itemCount, out int capacity, out bool freightHasStorage);
                totalItemCount += Mathf.Max(0, itemCount);
                totalCapacity += Mathf.Max(0, capacity);
                hasStorage |= freightHasStorage;
            }

            foreach (Train connectedTrain in train.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !visited.Add(connectedTrain))
                {
                    continue;
                }

                queue.Enqueue(connectedTrain);
            }
        }
    }

    private bool TryEnsureAutoDriveRoute(Trainstation targetStation, string targetStationName, float deltaTime)
    {
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain(
            targetStation,
            targetStationName);
        bool hasFixedRoute = TryEnsureAutoDriveFixedRoute();
        autoDriveRouteRefreshTimer = Mathf.Max(0f, autoDriveRouteRefreshTimer - Mathf.Max(0f, deltaTime));
        if (targetStation != null
            && autoDriveRouteSegments.Count > 0
            && (hasFixedRoute || autoDriveRouteRefreshTimer > 0f)
            && !HasAutoDriveRouteReferenceChanged(routeReferenceTrain)
            && string.Equals(autoDriveRouteTargetStationName, targetStationName, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        autoDriveRouteSegments.Clear();
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveRouteReferenceTrainInstanceId = 0;
        autoDriveRouteSegmentCursor = 0;
        autoDriveRouteRefreshTimer = AutoDriveRouteRefreshInterval;
        if (targetStation == null || routeReferenceTrain == null)
        {
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
            return false;
        }

        autoDriveRouteSegmentCursor = 0;
        autoDriveRouteTargetStationName = targetStationName;
        autoDriveRouteReferenceTrainInstanceId = routeReferenceTrain.GetInstanceID();
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
            remainingDistance - ResolveDockCompleteDistance());
        float desiredSpeed = Mathf.Sqrt(
            2f
            * Mathf.Max(0.01f, VehicleDecelerationPerSecond)
            * distanceUntilComplete);
        desiredSpeed = Mathf.Min(effectiveMaxSpeed, desiredSpeed);

        float inputMagnitude = desiredSpeed / effectiveMaxSpeed;
        if (remainingDistance > ResolveDockCaptureDistance())
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
        float branchPreviewDistance = ResolveAutoDriveBranchPreviewDistance(routeReferenceTrain);

        Railload desiredRail = currentSegment.Rail;
        float desiredDirectionSign = directionSign;
        float desiredDistanceAlongPath;
        if (remainingDistance <= branchPreviewDistance
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

    protected override float AdjustDrivenSignedStep(
        RailSample currentSample,
        Vector2 currentFacing,
        bool hasInput,
        Vector2 inputDirection,
        float deltaTime,
        float signedStep)
    {
        if (!autoDriveEnabled
            || !hasInput
            || Mathf.Abs(signedStep) <= 0.0001f
            || autoDriveResolvedTargetStation == null
            || !TryGetAutoDriveTargetDockDelta(
                autoDriveResolvedTargetStation,
                out float signedDockDelta))
        {
            return signedStep;
        }

        float remainingDistance = Mathf.Abs(signedDockDelta);
        if (remainingDistance > ResolveAutoDriveDockApproachDistance()
            || remainingDistance <= ResolveDockCompleteDistance())
        {
            return signedStep;
        }

        if (remainingDistance <= AutoDriveDockSnapDistance
            || signedDockDelta * signedStep <= 0f)
        {
            return signedDockDelta;
        }

        return Mathf.Sign(signedDockDelta) * Mathf.Min(Mathf.Abs(signedStep), remainingDistance);
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
        int currentSegmentIndex = Mathf.Clamp(
            autoDriveRouteSegmentCursor,
            0,
            autoDriveRouteSegments.Count - 1);
        if (currentSegmentIndex < 0 || currentSegmentIndex + 1 >= autoDriveRouteSegments.Count)
        {
            return false;
        }

        AutoDriveRoutePlanner.RouteSegment currentSegment = autoDriveRouteSegments[currentSegmentIndex];
        AutoDriveRoutePlanner.RouteSegment nextSegment = autoDriveRouteSegments[currentSegmentIndex + 1];
        if (nextSegment.Rail == null || nextSegment.Rail == currentSegment.Rail)
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
        return true;
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
        int currentSegmentIndex = Mathf.Clamp(
            autoDriveRouteSegmentCursor,
            0,
            autoDriveRouteSegments.Count - 1);
        if (currentSegmentIndex < 0 || currentSegmentIndex + 1 >= autoDriveRouteSegments.Count)
        {
            return false;
        }

        AutoDriveRoutePlanner.RouteSegment currentSegment = autoDriveRouteSegments[currentSegmentIndex];
        AutoDriveRoutePlanner.RouteSegment nextSegment = autoDriveRouteSegments[currentSegmentIndex + 1];
        if (nextSegment.Rail == null
            || nextSegment.Rail == excludedRail
            || nextSegment.Rail == currentSegment.Rail)
        {
            return false;
        }

        if (ResolveAutoDriveRemainingSegmentDistance(
                currentSegment,
                ClampDistanceAlongSegment(currentSegment, endpointSample.DistanceAlongPath)) > AutoDriveRouteSegmentTolerance * 2f)
        {
            return false;
        }

        preferredRail = nextSegment.Rail;
        return true;
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

        float minDistance =
            Mathf.Min(segment.StartDistance, segment.EndDistance) - AutoDriveRouteSegmentTolerance;
        float maxDistance =
            Mathf.Max(segment.StartDistance, segment.EndDistance) + AutoDriveRouteSegmentTolerance;
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

        RailHandcar fallbackRouteReferenceTrain = ResolveFallbackAutoDriveRouteReferenceTrain();
        CacheAutoDriveRouteReferenceTrain(targetStationName, fallbackRouteReferenceTrain);
        return fallbackRouteReferenceTrain;
    }

    private RailHandcar ResolveFallbackAutoDriveRouteReferenceTrain()
    {
        RailHandcar routeReferenceTrain = CurrentRailDebugPowerSourceTrain as RailHandcar;
        if (IsValidAutoDriveRouteReferenceTrain(routeReferenceTrain))
        {
            return routeReferenceTrain;
        }

        return IsValidAutoDriveRouteReferenceTrain(this) ? this : null;
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

        RailHandcar fallbackReferenceTrain = ResolveFallbackAutoDriveRouteReferenceTrain();
        if (TryResolveAutoDriveClosestRouteReferenceTrain(
                targetStation,
                targetStationName,
                endpointOnly: true,
                fallbackReferenceTrain,
                out routeReferenceTrain))
        {
            return true;
        }

        // Keep auto-drive usable even when a consist endpoint is not drivable.
        return TryResolveAutoDriveClosestRouteReferenceTrain(
            targetStation,
            targetStationName,
            endpointOnly: false,
            fallbackReferenceTrain,
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
        candidate = train as RailHandcar;
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
        autoDriveConnectedTrainScratch.Clear();
        autoDriveConnectedTrainVisited.Clear();
        autoDriveConnectedTrainQueue.Clear();
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
               || routeReferenceTrain.GetInstanceID() != autoDriveRouteReferenceTrainInstanceId;
    }

    private float ResolveAutoDriveDockApproachDistance()
    {
        float stoppingDistance =
            CurrentVehicleSpeed * CurrentVehicleSpeed
            / (2f * Mathf.Max(0.01f, VehicleDecelerationPerSecond));
        return Mathf.Max(
            ResolveDockCaptureDistance(),
            stoppingDistance + ResolveDockCompleteDistance());
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
        return deltaTime >= 0f
               && autoDriveResolvedTargetStation != null
               && TryIsDockedAtTargetStation(autoDriveResolvedTargetStation, out _)
               && FinalizeAutoDriveArrivalFromCurrentTarget();
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

            if (!TryGetActivePipeAtCoordinate(coordinate, out Pipe pipe, out Quaternion pipeRotation))
            {
                continue;
            }

            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = CardinalDirections[directionIndex];
                if (!pipe.HasConnectionTowards(pipeRotation, direction))
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
                        out Quaternion nextPipeRotation)
                    && nextPipe.HasConnectionTowards(nextPipeRotation, -direction))
                {
                    EnqueueWaterPipeSearchCoordinate(nextCoordinate);
                }
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
            || !pump.HasPipeConnectionTowards(pump.transform.rotation, directionToPipe)
            || !pump.TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond))
        {
            return false;
        }

        return outputItemId == waterItemId && litersPerSecond > WaterEpsilon;
    }

    private bool TryGetActivePipeAtCoordinate(
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion pipeRotation)
    {
        pipe = null;
        pipeRotation = Quaternion.identity;
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || block.MapObject is not Pipe candidatePipe
            || !candidatePipe.gameObject.activeInHierarchy)
        {
            return false;
        }

        pipe = candidatePipe;
        pipeRotation = candidatePipe.transform.rotation;
        return true;
    }

    private bool IsUsableBurnEnergyItem(int itemId)
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

    private void SpendStoredBurnEnergy(float cost)
    {
        if (cost <= BurnEnergyEpsilon)
        {
            return;
        }

        storedBurnEnergy = Mathf.Max(0f, storedBurnEnergy - cost);
        if (storedBurnEnergy <= BurnEnergyEpsilon)
        {
            storedBurnEnergy = 0f;
            burnEnergyGaugeCapacity = 0f;
        }
    }

    private void SpendStoredWater(float cost)
    {
        if (cost <= WaterEpsilon)
        {
            return;
        }

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
        if (waterPipe == null || directionFromTrainToPipe == Vector2Int.zero)
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
        waterPipeTargetActive = true;
        waterPipeTransferReady = transferReady;
        activeWaterPipeDirectionFromTrainToPipe = directionFromTrainToPipe;
        waterPipeAnimating = true;
        if (!waterPipe.gameObject.activeSelf)
        {
            waterPipe.gameObject.SetActive(true);
        }
    }

    private void RequestWaterPipeRetract()
    {
        if (waterPipe == null)
        {
            ClearWaterPipeDockLock();
            return;
        }

        ClearWaterPipeDockLock();
        CaptureWaterPipeDefaults();
        waterPipeTargetLocalPosition = waterPipeDefaultLocalPosition;
        waterPipeTargetLocalRotation = waterPipeDefaultLocalRotation;
        waterPipeTargetActive = false;
        waterPipeTransferReady = false;
        activeWaterPipeDirectionFromTrainToPipe = Vector2Int.zero;
        if (waterPipe.gameObject.activeSelf)
        {
            waterPipeAnimating = true;
        }
    }

    private void ResetWaterPipeImmediate(bool active)
    {
        if (waterPipe == null)
        {
            ClearWaterPipeDockLock();
            return;
        }

        ClearWaterPipeDockLock();
        CaptureWaterPipeDefaults();
        waterPipe.localPosition = waterPipeDefaultLocalPosition;
        waterPipe.localRotation = waterPipeDefaultLocalRotation;
        waterPipeTargetLocalPosition = waterPipeDefaultLocalPosition;
        waterPipeTargetLocalRotation = waterPipeDefaultLocalRotation;
        waterPipeTargetActive = active;
        waterPipeTransferReady = false;
        activeWaterPipeDirectionFromTrainToPipe = Vector2Int.zero;
        waterPipeAnimating = false;
        if (waterPipe.gameObject.activeSelf != active)
        {
            waterPipe.gameObject.SetActive(active);
        }
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
        pendingBurnEnergyCost = 0f;
        pendingBurnEnergyFrame = -1;
    }

    private void ClearPendingWaterCost()
    {
        pendingWaterCost = 0f;
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
        if (particleEffect == null)
        {
            return;
        }

        if (isMoving)
        {
            if (!particleEffect.isEmitting)
            {
                particleEffect.Play(true);
            }

            return;
        }

        StopMovementParticle(false);
    }

    private void StopMovementParticle(bool clearParticles)
    {
        if (particleEffect == null || (!particleEffect.isPlaying && !particleEffect.isEmitting))
        {
            return;
        }

        particleEffect.Stop(
            true,
            clearParticles
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
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
        private const float RouteRailConnectionSnapDistance = 0.1f;
        private const float RouteRailConnectionMinTangentDot = 0.2f;
        private const float RouteStartReversePenalty = 1000f;
        private const float RouteStartReverseDotThreshold = -0.1f;
        private const float RouteTurnReversePenalty = 1000f;
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

        public static bool TryFindStationByName(string stationName, out Trainstation station)
        {
            station = null;
            if (string.IsNullOrWhiteSpace(stationName))
            {
                return false;
            }

            Trainstation[] liveStations = Object.FindObjectsOfType<Trainstation>(false);
            for (int i = 0; i < liveStations.Length; i++)
            {
                Trainstation candidate = liveStations[i];
                if (candidate == null
                    || !candidate.gameObject.activeInHierarchy
                    || !string.Equals(candidate.StationName, stationName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                station = candidate;
                return true;
            }

            return false;
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

        private static Vector2 ResolvePreferredRouteStartDirection(Train train, Vector2 currentRailTangent)
        {
            if (train is RailHandcar railHandcar
                && railHandcar.TryGetPreferredRouteTravelDirection(out Vector2 routeDirection))
            {
                return routeDirection;
            }

            if (currentRailTangent.sqrMagnitude > 0.0001f)
            {
                return currentRailTangent.normalized;
            }

            Vector3 transformForward = train.transform.forward;
            Vector2 forward = new Vector2(transformForward.x, transformForward.z);
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return Vector2.zero;
            }

            forward.Normalize();
            return forward;
        }

        private static List<RailInfo> CollectRails()
        {
            List<RailInfo> results = new List<RailInfo>(64);
            Railload[] liveRails = Object.FindObjectsOfType<Railload>(false);
            for (int i = 0; i < liveRails.Length; i++)
            {
                Railload rail = liveRails[i];
                if (rail == null || !rail.isActiveAndEnabled)
                {
                    continue;
                }

                List<Vector2> points = rail.CopyVisualPathPoints();
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

                results.Add(new RailInfo
                {
                    Rail = rail,
                    OccupiedCoordinates = rail.RuntimeOccupiedCoordinates,
                    StartPoint = startPoint,
                    EndPoint = endPoint,
                    Length = length
                });
            }

            return results;
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
            int startNodeIndex = GetOrCreateRouteGraphNode(graphNodes, startEndpoint.Point);
            AddRouteGraphNodeRef(graphNodes, railRefsByRail, startNodeIndex, startEndpoint.RailIndex, startEndpoint.DistanceAlongPath);
            int endNodeIndex = GetOrCreateRouteGraphNode(graphNodes, endEndpoint.Point);
            AddRouteGraphNodeRef(graphNodes, railRefsByRail, endNodeIndex, endEndpoint.RailIndex, endEndpoint.DistanceAlongPath);

            float maxConnectionSqrDistance = RouteRailConnectionSnapDistance * RouteRailConnectionSnapDistance;
            for (int leftRailIndex = 0; leftRailIndex < rails.Count; leftRailIndex++)
            {
                for (int rightRailIndex = leftRailIndex + 1; rightRailIndex < rails.Count; rightRailIndex++)
                {
                    if (!TryResolveRouteConnectionBetweenRails(
                            rails,
                            leftRailIndex,
                            rightRailIndex,
                            maxConnectionSqrDistance,
                            out RouteConnection connection))
                    {
                        continue;
                    }

                    int nodeIndex = GetOrCreateRouteGraphNode(graphNodes, connection.Point);
                    AddRouteGraphNodeRef(graphNodes, railRefsByRail, nodeIndex, connection.LeftRailIndex, connection.LeftDistanceAlongPath);
                    AddRouteGraphNodeRef(graphNodes, railRefsByRail, nodeIndex, connection.RightRailIndex, connection.RightDistanceAlongPath);
                }
            }

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

            for (int edgeIndex = 0; edgeIndex < startEdges.Count; edgeIndex++)
            {
                RouteGraphEdge edge = startEdges[edgeIndex];
                int stateIndex = stateOffsets[startNodeIndex] + edgeIndex;
                float startPenalty = ResolveRouteStartEdgePenalty(
                    rails,
                    edge,
                    preferredStartDirection);
                distances[stateIndex] = Mathf.Max(0.01f, edge.Cost) + startPenalty;
                if (edge.ToNodeIndex == endNodeIndex
                    && (bestEndStateIndex < 0
                        || distances[stateIndex] < distances[bestEndStateIndex]))
                {
                    bestEndStateIndex = stateIndex;
                }
            }

            for (int step = 0; step < stateCount; step++)
            {
                int currentStateIndex = -1;
                float currentDistance = float.PositiveInfinity;
                for (int i = 0; i < stateCount; i++)
                {
                    if (visited[i] || distances[i] >= currentDistance)
                    {
                        continue;
                    }

                    currentDistance = distances[i];
                    currentStateIndex = i;
                }

                if (currentStateIndex < 0)
                {
                    break;
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
                        currentDistance
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
                    if (nextEdge.ToNodeIndex == endNodeIndex
                        && (bestEndStateIndex < 0
                            || candidateDistance < distances[bestEndStateIndex]))
                    {
                        bestEndStateIndex = nextStateIndexValue;
                    }
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
            return directionDot < RouteStartReverseDotThreshold
                ? RouteStartReversePenalty
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
                return RouteTurnReversePenalty;
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
