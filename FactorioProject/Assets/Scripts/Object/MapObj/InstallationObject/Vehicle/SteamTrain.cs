using System.Collections.Generic;
using UnityEngine;

public class SteamTrain : RailHandcar
{
    public enum AutoDriveFuelFilter
    {
        Free = 0,
        Full = 1
    }

    public enum AutoDriveFreightFilter
    {
        Free = 0,
        Full = 1,
        Empty = 2
    }

    public enum AutoDriveStatus
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
    private const float AutoDriveDockApproachMinInputMagnitude = 0.12f;
    private const float AutoDriveWaitDurationSeconds = 5f;
    private const float AutoDriveSpeedBlockedThreshold = 0.02f;
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
    private readonly Queue<Vector2Int> waterPipeSearchQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> waterPipeSearchVisited = new HashSet<Vector2Int>();
    private string autoDriveRouteTargetStationName = string.Empty;
    private int autoDriveRouteReferenceTrainInstanceId;
    private string autoDriveLastArrivedStationName = string.Empty;
    private string autoDriveResolvedTargetStationName = string.Empty;
    private string autoDriveResolvedNextStationName = string.Empty;
    private Trainstation autoDriveResolvedTargetStation;
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
    public bool AutoDriveEnabled => autoDriveEnabled;
    public string AutoDriveTargetAStationName => autoDriveTargetAStationName;
    public string AutoDriveTargetBStationName => autoDriveTargetBStationName;
    public AutoDriveFuelFilter AutoDriveFuelMode => autoDriveFuelFilter;
    public AutoDriveFreightFilter AutoDriveFreightMode => autoDriveFreightFilter;
    public AutoDriveStatus CurrentAutoDriveStatus => autoDriveStatus;
    public string CurrentAutoDriveTargetStationName => autoDriveCurrentTargetStationName;
    public string CurrentAutoDriveNextTargetStationName => autoDriveNextTargetStationName;
    public bool HasAnyAutoDriveTarget =>
        !string.IsNullOrWhiteSpace(autoDriveTargetAStationName)
        || !string.IsNullOrWhiteSpace(autoDriveTargetBStationName);
    public string CurrentAutoDriveStatusText => ResolveAutoDriveStatusText();
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
        ResetAutoDriveState();
        ResetWaterPipeImmediate(false);
        base.PrepareForPool();
    }

    private void Update()
    {
        if (!Application.isPlaying
            || !gameObject.activeInHierarchy
            || !autoDriveEnabled
            || IsMountedByPlayer())
        {
            return;
        }

        HandleMountedInput(Vector3.zero, 0f, Time.deltaTime, null);
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

        Vector3 resolvedMoveDirection = autoDriveEnabled
            ? ResolveAutoDriveMoveDirection(deltaTime)
            : worldMoveDirection;

        if (resolvedMoveDirection.sqrMagnitude > 0.0001f)
        {
            RequestWaterPipeRetract();
        }

        if (RequiresWater(resolvedMoveDirection, deltaTime, out float waterCost)
            && !TryEnsureWaterAvailable(waterCost))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
            if (autoDriveEnabled)
            {
                SetAutoDriveStatus(
                    AutoDriveStatus.WaitingForFuel,
                    autoDriveResolvedTargetStationName,
                    autoDriveResolvedNextStationName);
            }
            return;
        }

        if (RequiresPoweredBurnEnergy(resolvedMoveDirection, deltaTime, out float burnEnergyCost)
            && !TryEnsureBurnEnergyAvailable(burnEnergyCost, mountedPlayer))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
            if (autoDriveEnabled)
            {
                SetAutoDriveStatus(
                    AutoDriveStatus.WaitingForFuel,
                    autoDriveResolvedTargetStationName,
                    autoDriveResolvedNextStationName);
            }
            return;
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

        if (!autoDriveEnabled)
        {
            return;
        }

        if (TryFinalizeAutoDriveArrival(deltaTime))
        {
            return;
        }

        if (resolvedMoveDirection.sqrMagnitude > 0.0001f
            && CurrentVehicleSpeed <= AutoDriveSpeedBlockedThreshold
            && autoDriveStatus == AutoDriveStatus.Moving)
        {
            SetAutoDriveStatus(
                AutoDriveStatus.WaitingForClearTrack,
                autoDriveResolvedTargetStationName,
                autoDriveResolvedNextStationName);
        }
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

    public void ApplyAutoDriveFilterState(
        bool enabled,
        string targetAStationName,
        string targetBStationName,
        AutoDriveFuelFilter fuelFilter,
        AutoDriveFreightFilter freightFilter)
    {
        bool settingsChanged = autoDriveEnabled != enabled
                               || !string.Equals(autoDriveTargetAStationName, NormalizeAutoDriveStationName(targetAStationName), System.StringComparison.OrdinalIgnoreCase)
                               || !string.Equals(autoDriveTargetBStationName, NormalizeAutoDriveStationName(targetBStationName), System.StringComparison.OrdinalIgnoreCase)
                               || autoDriveFuelFilter != fuelFilter
                               || autoDriveFreightFilter != freightFilter;
        autoDriveEnabled = enabled;
        autoDriveTargetAStationName = NormalizeAutoDriveStationName(targetAStationName);
        autoDriveTargetBStationName = NormalizeAutoDriveStationName(targetBStationName);
        autoDriveFuelFilter = fuelFilter;
        autoDriveFreightFilter = freightFilter;

        if (settingsChanged)
        {
            ResetAutoDriveRuntimeState();
        }

        if (!autoDriveEnabled)
        {
            SetAutoDriveStatus(AutoDriveStatus.Idle, string.Empty, string.Empty);
        }
        else if (!HasAnyAutoDriveTarget)
        {
            SetAutoDriveStatus(AutoDriveStatus.NoTarget, string.Empty, string.Empty);
        }
    }

    public void CaptureAutoDriveState(
        out bool enabled,
        out string targetAStationName,
        out string targetBStationName,
        out AutoDriveFuelFilter fuelFilter,
        out AutoDriveFreightFilter freightFilter)
    {
        enabled = autoDriveEnabled;
        targetAStationName = autoDriveTargetAStationName ?? string.Empty;
        targetBStationName = autoDriveTargetBStationName ?? string.Empty;
        fuelFilter = autoDriveFuelFilter;
        freightFilter = autoDriveFreightFilter;
    }

    public void ApplyAutoDrivePersistentState(
        bool enabled,
        string targetAStationName,
        string targetBStationName,
        int fuelFilter,
        int freightFilter)
    {
        ApplyAutoDriveFilterState(
            enabled,
            targetAStationName,
            targetBStationName,
            (AutoDriveFuelFilter)Mathf.Clamp(fuelFilter, 0, 1),
            (AutoDriveFreightFilter)Mathf.Clamp(freightFilter, 0, 2));
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
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveRouteReferenceTrainInstanceId = 0;
        autoDriveLastArrivedStationName = string.Empty;
        autoDriveResolvedTargetStationName = string.Empty;
        autoDriveResolvedNextStationName = string.Empty;
        autoDriveResolvedTargetStation = null;
        autoDriveRouteRefreshTimer = 0f;
        autoDriveStationWaitTimer = 0f;
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

        float routeLengthToA = TryBuildRouteLengthToStation(stationA, out float lengthToA)
            ? lengthToA
            : float.PositiveInfinity;
        float routeLengthToB = TryBuildRouteLengthToStation(stationB, out float lengthToB)
            ? lengthToB
            : float.PositiveInfinity;

        if (routeLengthToA <= routeLengthToB)
        {
            targetStation = stationB;
            targetStationName = targetB;
            nextTargetStationName = targetA;
            return true;
        }

        targetStation = stationA;
        targetStationName = targetA;
        nextTargetStationName = targetB;
        return true;
    }

    private bool TryBuildRouteLengthToStation(Trainstation station, out float routeLength)
    {
        routeLength = float.PositiveInfinity;
        autoDriveRouteSegments.Clear();
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain();
        if (routeReferenceTrain == null
            || !AutoDriveRoutePlanner.TryBuildRoute(
                routeReferenceTrain,
                station,
                autoDriveRouteSegments))
        {
            autoDriveRouteSegments.Clear();
            return false;
        }

        routeLength = AutoDriveRoutePlanner.GetRouteLength(autoDriveRouteSegments);
        autoDriveRouteSegments.Clear();
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
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain();
        autoDriveRouteRefreshTimer = Mathf.Max(0f, autoDriveRouteRefreshTimer - Mathf.Max(0f, deltaTime));
        if (targetStation != null
            && autoDriveRouteSegments.Count > 0
            && autoDriveRouteRefreshTimer > 0f
            && !HasAutoDriveRouteReferenceChanged(routeReferenceTrain)
            && string.Equals(autoDriveRouteTargetStationName, targetStationName, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        autoDriveRouteSegments.Clear();
        autoDriveRouteTargetStationName = string.Empty;
        autoDriveRouteReferenceTrainInstanceId = 0;
        autoDriveRouteRefreshTimer = AutoDriveRouteRefreshInterval;
        if (targetStation == null
            || routeReferenceTrain == null
            || !AutoDriveRoutePlanner.TryBuildRoute(routeReferenceTrain, targetStation, autoDriveRouteSegments))
        {
            return false;
        }

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
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain();
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
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain();
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

        int currentSegmentIndex = FindCurrentAutoDriveRouteSegment(currentRail, currentDistanceAlongPath);
        if (currentSegmentIndex < 0)
        {
            return false;
        }

        AutoDriveRoutePlanner.RouteSegment currentSegment = autoDriveRouteSegments[currentSegmentIndex];
        float startDistance = currentSegment.StartDistance;
        float endDistance = currentSegment.EndDistance;
        float directionSign = Mathf.Sign(endDistance - startDistance);
        if (Mathf.Abs(directionSign) <= 0.0001f)
        {
            return false;
        }

        float minDistance = Mathf.Min(startDistance, endDistance);
        float maxDistance = Mathf.Max(startDistance, endDistance);
        float remainingDistance = directionSign > 0f
            ? maxDistance - currentDistanceAlongPath
            : currentDistanceAlongPath - minDistance;

        Railload desiredRail = currentSegment.Rail;
        float desiredDistanceAlongPath;
        if (remainingDistance <= AutoDriveBranchLookAheadDistance
            && currentSegmentIndex + 1 < autoDriveRouteSegments.Count)
        {
            AutoDriveRoutePlanner.RouteSegment nextSegment = autoDriveRouteSegments[currentSegmentIndex + 1];
            desiredRail = nextSegment.Rail;
            float nextDirectionSign = Mathf.Sign(nextSegment.EndDistance - nextSegment.StartDistance);
            desiredDistanceAlongPath = nextSegment.StartDistance + nextDirectionSign * Mathf.Min(AutoDriveBranchLookAheadDistance, Mathf.Max(0.05f, nextSegment.Length));
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
            return false;
        }

        Vector2 desiredDirection = desiredPoint - currentPathPoint;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
        {
            desiredDirection = desiredTangent * directionSign;
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

        return Mathf.Sign(signedStep) * Mathf.Min(Mathf.Abs(signedStep), remainingDistance);
    }

    protected override bool TryGetPreferredBranchRail(
        RailSample currentSample,
        Vector2 inputDirection,
        out Railload preferredRail)
    {
        preferredRail = null;
        RailHandcar routeReferenceTrain = ResolveAutoDriveRouteReferenceTrain();
        if (!autoDriveEnabled
            || autoDriveRouteSegments.Count <= 0
            || routeReferenceTrain == null
            || !routeReferenceTrain.TryGetCurrentRailPose(
                out Railload currentRail,
                out float currentDistanceAlongPath,
                out _,
                out _)
            || currentRail == null)
        {
            return false;
        }

        int currentSegmentIndex = FindCurrentAutoDriveRouteSegment(
            currentRail,
            currentDistanceAlongPath);
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

        float remainingDistance = ResolveAutoDriveRemainingSegmentDistance(
            currentSegment,
            currentDistanceAlongPath);
        if (remainingDistance > AutoDriveLookAheadDistance + AutoDriveRouteSegmentTolerance)
        {
            return false;
        }

        preferredRail = nextSegment.Rail;
        return true;
    }

    private RailHandcar ResolveAutoDriveRouteReferenceTrain()
    {
        RailHandcar routeReferenceTrain = CurrentRailDebugPowerSourceTrain as RailHandcar;
        if (routeReferenceTrain != null
            && routeReferenceTrain.gameObject.activeInHierarchy
            && routeReferenceTrain.TryGetPlacementRuntime(out _, out _))
        {
            return routeReferenceTrain;
        }

        return this;
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

    private int FindCurrentAutoDriveRouteSegment(Railload currentRail, float currentDistanceAlongPath)
    {
        if (currentRail == null)
        {
            return -1;
        }

        for (int i = 0; i < autoDriveRouteSegments.Count; i++)
        {
            AutoDriveRoutePlanner.RouteSegment segment = autoDriveRouteSegments[i];
            if (segment.Rail != currentRail)
            {
                continue;
            }

            float minDistance = Mathf.Min(segment.StartDistance, segment.EndDistance) - AutoDriveRouteSegmentTolerance;
            float maxDistance = Mathf.Max(segment.StartDistance, segment.EndDistance) + AutoDriveRouteSegmentTolerance;
            if (currentDistanceAlongPath >= minDistance && currentDistanceAlongPath <= maxDistance)
            {
                return i;
            }
        }

        return -1;
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
        private const float RouteEndpointTolerance = 0.15f;

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
                || !train.TryGetCurrentRailPose(out Railload currentRail, out float currentDistanceAlongPath, out Vector2 currentPathPoint, out _))
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
            return TryBuildRouteFromConnectionGraph(rails, startEndpoint, endEndpoint, result);
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
            float bestSqrDistance = RouteEndpointSnapDistance * RouteEndpointSnapDistance;
            bool found = false;
            for (int i = 0; i < rails.Count; i++)
            {
                RailInfo rail = rails[i];
                if (rail == null
                    || rail.Rail == null
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

        private static bool TryBuildRouteFromConnectionGraph(
            List<RailInfo> rails,
            RouteEndpoint startEndpoint,
            RouteEndpoint endEndpoint,
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

            float maxConnectionSqrDistance = RouteEndpointSnapDistance * RouteEndpointSnapDistance;
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

            return TryFindRouteGraphPath(rails, adjacency, startNodeIndex, endNodeIndex, result);
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
            ConsiderRouteEndpointConnectionCandidate(rails, leftRailIndex, rightRailIndex, leftRail.StartPoint, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);
            ConsiderRouteEndpointConnectionCandidate(rails, leftRailIndex, rightRailIndex, leftRail.EndPoint, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);
            ConsiderRouteEndpointConnectionCandidate(rails, leftRailIndex, rightRailIndex, rightRail.StartPoint, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);
            ConsiderRouteEndpointConnectionCandidate(rails, leftRailIndex, rightRailIndex, rightRail.EndPoint, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);

            IReadOnlyList<Vector2Int> leftCoordinates = leftRail.OccupiedCoordinates;
            IReadOnlyList<Vector2Int> rightCoordinates = rightRail.OccupiedCoordinates;
            if (leftCoordinates != null && rightCoordinates != null)
            {
                for (int leftCoordinateIndex = 0; leftCoordinateIndex < leftCoordinates.Count; leftCoordinateIndex++)
                {
                    for (int rightCoordinateIndex = 0; rightCoordinateIndex < rightCoordinates.Count; rightCoordinateIndex++)
                    {
                        if (leftCoordinates[leftCoordinateIndex] != rightCoordinates[rightCoordinateIndex])
                        {
                            continue;
                        }

                        Vector2 sharedPoint = new Vector2(leftCoordinates[leftCoordinateIndex].x, leftCoordinates[leftCoordinateIndex].y);
                        ConsiderRouteConnectionCandidate(rails, leftRailIndex, rightRailIndex, sharedPoint, maxConnectionSqrDistance, ref found, ref bestScore, ref connection);
                    }
                }
            }

            return found;
        }

        private static void ConsiderRouteConnectionCandidate(
            IReadOnlyList<RailInfo> rails,
            int leftRailIndex,
            int rightRailIndex,
            Vector2 candidatePoint,
            float maxConnectionSqrDistance,
            ref bool found,
            ref float bestScore,
            ref RouteConnection bestConnection)
        {
            RailInfo leftRail = rails[leftRailIndex];
            RailInfo rightRail = rails[rightRailIndex];
            if (leftRail?.Rail == null
                || rightRail?.Rail == null
                || !leftRail.Rail.TryFindNearestRenderedPathSample(candidatePoint, out float leftDistanceAlongPath, out Vector2 leftPathPoint, out _, out float leftSqrDistance)
                || !rightRail.Rail.TryFindNearestRenderedPathSample(candidatePoint, out float rightDistanceAlongPath, out Vector2 rightPathPoint, out _, out float rightSqrDistance)
                || leftSqrDistance > maxConnectionSqrDistance
                || rightSqrDistance > maxConnectionSqrDistance)
            {
                return;
            }

            float score = leftSqrDistance + rightSqrDistance;
            if (found && score >= bestScore)
            {
                return;
            }

            found = true;
            bestScore = score;
            bestConnection = new RouteConnection(
                leftRailIndex,
                leftDistanceAlongPath,
                rightRailIndex,
                rightDistanceAlongPath,
                (leftPathPoint + rightPathPoint) * 0.5f);
        }

        private static void ConsiderRouteEndpointConnectionCandidate(
            IReadOnlyList<RailInfo> rails,
            int leftRailIndex,
            int rightRailIndex,
            Vector2 candidatePoint,
            float maxConnectionSqrDistance,
            ref bool found,
            ref float bestScore,
            ref RouteConnection bestConnection)
        {
            RailInfo leftRail = rails[leftRailIndex];
            RailInfo rightRail = rails[rightRailIndex];
            if (leftRail?.Rail == null
                || rightRail?.Rail == null
                || !leftRail.Rail.TryFindNearestRenderedPathSample(candidatePoint, out float leftDistanceAlongPath, out Vector2 leftPathPoint, out _, out float leftSqrDistance)
                || !rightRail.Rail.TryFindNearestRenderedPathSample(candidatePoint, out float rightDistanceAlongPath, out Vector2 rightPathPoint, out _, out float rightSqrDistance)
                || leftSqrDistance > maxConnectionSqrDistance
                || rightSqrDistance > maxConnectionSqrDistance
                || !IsNearRailEndpoint(leftRail, leftDistanceAlongPath)
                || !IsNearRailEndpoint(rightRail, rightDistanceAlongPath))
            {
                return;
            }

            float score = leftSqrDistance + rightSqrDistance;
            if (found && score >= bestScore)
            {
                return;
            }

            found = true;
            bestScore = score;
            bestConnection = new RouteConnection(
                leftRailIndex,
                leftDistanceAlongPath,
                rightRailIndex,
                rightDistanceAlongPath,
                (leftPathPoint + rightPathPoint) * 0.5f);
        }

        private static bool IsNearRailEndpoint(RailInfo rail, float distanceAlongPath)
        {
            if (rail == null)
            {
                return false;
            }

            return distanceAlongPath <= RouteEndpointTolerance
                   || Mathf.Abs(rail.Length - distanceAlongPath) <= RouteEndpointTolerance;
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
            List<RouteSegment> result)
        {
            int nodeCount = adjacency.Length;
            float[] distances = new float[nodeCount];
            int[] previousNodes = new int[nodeCount];
            RouteGraphEdge[] previousEdges = new RouteGraphEdge[nodeCount];
            bool[] visited = new bool[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                distances[i] = float.PositiveInfinity;
                previousNodes[i] = -1;
            }

            distances[startNodeIndex] = 0f;
            for (int step = 0; step < nodeCount; step++)
            {
                int currentNodeIndex = -1;
                float currentDistance = float.PositiveInfinity;
                for (int i = 0; i < nodeCount; i++)
                {
                    if (visited[i] || distances[i] >= currentDistance)
                    {
                        continue;
                    }

                    currentDistance = distances[i];
                    currentNodeIndex = i;
                }

                if (currentNodeIndex < 0 || currentNodeIndex == endNodeIndex)
                {
                    break;
                }

                visited[currentNodeIndex] = true;
                List<RouteGraphEdge> edges = adjacency[currentNodeIndex];
                for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                {
                    RouteGraphEdge edge = edges[edgeIndex];
                    if (visited[edge.ToNodeIndex])
                    {
                        continue;
                    }

                    float candidateDistance = currentDistance + Mathf.Max(0.01f, edge.Cost);
                    if (candidateDistance >= distances[edge.ToNodeIndex])
                    {
                        continue;
                    }

                    distances[edge.ToNodeIndex] = candidateDistance;
                    previousNodes[edge.ToNodeIndex] = currentNodeIndex;
                    previousEdges[edge.ToNodeIndex] = edge;
                }
            }

            if (float.IsPositiveInfinity(distances[endNodeIndex]))
            {
                return false;
            }

            List<RouteSegment> reversedSegments = new List<RouteSegment>(16);
            for (int currentNodeIndex = endNodeIndex;
                 currentNodeIndex != startNodeIndex;
                 currentNodeIndex = previousNodes[currentNodeIndex])
            {
                int previousNodeIndex = previousNodes[currentNodeIndex];
                if (previousNodeIndex < 0)
                {
                    reversedSegments.Clear();
                    return false;
                }

                RouteGraphEdge edge = previousEdges[currentNodeIndex];
                AppendRouteSegment(reversedSegments, rails[edge.RailIndex].Rail, edge.StartDistanceAlongPath, edge.EndDistanceAlongPath);
            }

            result.Clear();
            for (int i = reversedSegments.Count - 1; i >= 0; i--)
            {
                AppendRouteSegment(result, reversedSegments[i].Rail, reversedSegments[i].StartDistance, reversedSegments[i].EndDistance);
            }

            return result.Count > 0;
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
