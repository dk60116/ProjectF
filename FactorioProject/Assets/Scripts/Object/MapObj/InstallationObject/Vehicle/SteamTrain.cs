using System.Collections.Generic;
using UnityEngine;

public class SteamTrain : RailHandcar
{
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
    private Vector2Int activeWaterPipeDirectionFromTrainToPipe;
    private bool waterPipeDockLockActive;
    private Railload lockedWaterPipeDockRail;
    private float lockedWaterPipeDockDistanceAlongPath;
    private Vector2 lockedWaterPipeDockFacing;
    private Vector2Int lockedWaterPipeDockDirectionFromTrainToPipe;
    private Vector2Int lockedWaterPipeDockCoordinate;
    private readonly List<PortableObject> burnEnergyPortableMoveBuffer = new List<PortableObject>();
    private readonly Queue<Vector2Int> waterPipeSearchQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> waterPipeSearchVisited = new HashSet<Vector2Int>();

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

        if (worldMoveDirection.sqrMagnitude > 0.0001f)
        {
            RequestWaterPipeRetract();
        }

        if (RequiresWater(worldMoveDirection, deltaTime, out float waterCost)
            && !TryEnsureWaterAvailable(waterCost))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
            return;
        }

        if (RequiresPoweredBurnEnergy(worldMoveDirection, deltaTime, out float burnEnergyCost)
            && !TryEnsureBurnEnergyAvailable(burnEnergyCost, mountedPlayer))
        {
            StopMovementParticle(false);
            base.HandleMountedInput(Vector3.zero, moveSpeed, deltaTime);
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
        base.HandleMountedInput(worldMoveDirection, moveSpeed, deltaTime);
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
}
