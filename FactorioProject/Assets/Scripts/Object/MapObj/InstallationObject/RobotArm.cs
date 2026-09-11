using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class RobotArm : InputOutputModule
{
    private const float MinimumInstancedRenderCullRadius = 1.5f;
    private const float InstancedRenderCullPadding = 0.25f;
    private static readonly int PickTriggerHash = Animator.StringToHash("tPick");
    private static readonly int DropTriggerHash = Animator.StringToHash("tDrop");
    private static readonly List<RobotArm> ActiveRobotArms = new List<RobotArm>();
    private static readonly Dictionary<Vector2Int, List<RobotArm>> WakeRobotArmsByCoordinate =
        new Dictionary<Vector2Int, List<RobotArm>>();

    private const float DefaultManagedUpdateDeltaSeconds = 1f / 60f;
    private const float ItemMoveDuration = PortableObject.MoveToDuration * 0.5f;
    private const float RuntimeSleepRecheckIntervalSeconds = 0.1f;
    private const float AnimatorSpeedChangeEpsilon = 0.001f;
    private const int WakeRangeCellRadius = 1;

    private static ItemManager cachedFilterBitCountItemManager;
    private static int cachedFilterBitCountDefinitionCount = -1;
    private static int cachedFilterBitCount = 1;

    public enum RobotArmState
    {
        WaitingForPickup,
        WaitingBeforePickupTake,
        WaitingAfterPickupTake,
        TurningToDrop,
        WaitingForDrop,
        WaitingBeforeDropPlace,
        WaitingAfterDropPlace,
        TurningToPickup
    }

    private enum RobotArmPickupSource
    {
        None,
        Floor,
        Box,
        FreightCar,
        Conveyor,
        InputArea,
        SavedFloor,
        SavedConveyor,
        SavedInputArea
    }

    private enum ObjectInfoStatusLevel
    {
        Error,
        Warning,
        Working
    }

    private enum PlannedTransferCommand
    {
        None,
        Pickup,
        Drop
    }

    [System.Serializable]
    public sealed class TransferState
    {
        public int heldItemId = -1;
        public RobotArmState state = RobotArmState.WaitingForPickup;
        public float pickupTimer;
        public float dropRetryTimer;
        public float actionTurnTimer;
        public float turnTimer;
        public bool waitingForDropRetry;

        public TransferState Clone()
        {
            return new TransferState
            {
                heldItemId = heldItemId,
                state = state,
                pickupTimer = pickupTimer,
                dropRetryTimer = dropRetryTimer,
                actionTurnTimer = actionTurnTimer,
                turnTimer = turnTimer,
                waitingForDropRetry = waitingForDropRetry
            };
        }
    }

    [SerializeField]
    private Transform body;

    [SerializeField]
    private PortableObject handItem;

    [SerializeField]
    private bool useInstancedRendering = true;

    [SerializeField, Min(0.01f)]
    private float pickupInterval = 0.1f;

    [SerializeField, Min(1f)]
    private float bodyTurnSpeedDegreesPerSecond = 540f;

    [SerializeField, Min(0.01f)]
    private float dropRetryInterval = 0.1f;

    [SerializeField, Min(0f), Tooltip("Delay between action timing and the actual pickup/drop.")]
    [FormerlySerializedAs("postActionTurnDelay")]
    private float actionTurnDelay = 0.1f;

    [SerializeField, HideInInspector]
    private int heldItemId = -1;

    private TerrainGenerator cachedTerrainGenerator;
    private BlockStateStore cachedBlockStateStore;
    private float pickupTimer;
    private float dropRetryTimer;
    private float actionTurnTimer;
    private bool waitingForDropRetry;
    private RobotArmState state;
    private Quaternion inputBodyLocalRotation;
    private bool hasInputBodyLocalRotation;
    private bool hasRuntimeStateInitialized;
    private bool runtimeSleeping;
    private bool runtimeWakePending;
    private Animator cachedAnimator;
    private Renderer[] sleepAwakeRenderers;
    private MaterialPropertyBlock sleepAwakePropertyBlock;
    private bool sleepAwakeVisualStateInitialized;
    private bool lastSleepAwakeDarkTint;
    private RobotArmRenderBatcher renderBatcher;
    private RobotArmInstancedRenderPart[] instancedRenderParts;
    private bool instancedRenderingActive;
    private bool instancedRenderCullingDataInitialized;
    private float instancedRenderCullRadius = MinimumInstancedRenderCullRadius;
    private int instancedRenderLayerMask;
    private bool previewRenderingMode;
    private float runtimeSleepCheckTimer;
    private Transform handItemRestParent;
    private Vector3 handItemRestLocalPosition;
    private Quaternion handItemRestLocalRotation;
    private Vector3 handItemRestLocalScale;
    private bool hasHandItemRestTransform;
    private float lastElectricPowerSupplyRatio = 1f;
    private float lastAppliedAnimatorSpeed = -1f;
    private readonly List<Vector2Int> registeredWakeCoordinates = new List<Vector2Int>(9);
    private bool interactionCoordinateCacheValid;
    private long cachedInteractionPlacementSequence;
    private Vector2Int cachedPickupCoordinate;
    private Vector2Int cachedDropCoordinate;
    private readonly List<InstallationObject> freightCarCoordinateScratch = new List<InstallationObject>(4);
    private System.Predicate<int> cachedPickupItemFilter;
    private PlannedTransferCommand plannedTransferCommand;
    private bool stagedTickPlanned;
    private bool plannedPickupAvailabilityChecked;
    private bool plannedPickupAvailable;
    private bool plannedDropAvailabilityChecked;
    private bool plannedDropAvailable;

    public bool HasHeldItem => heldItemId >= 0;
    public int HeldItemId => heldItemId;
    public PortableObject HeldPortableObject => heldItemId >= 0 ? handItem : null;
    public bool CanTakeHeldItemFromSlot => CanTakeHeldItemFromSlotInternal();
    public Vector3 HeldItemWorldPosition => GetHandWorldPosition();
    public bool IsRuntimeSleeping => runtimeSleeping;
    public bool UsesInstancedRendering => useInstancedRendering;
    public float PickupIntervalSeconds => Mathf.Max(0.01f, pickupInterval);
    public float BodyTurnSpeedDegreesPerSecond => Mathf.Max(1f, bodyTurnSpeedDegreesPerSecond);
    public float DropRetryIntervalSeconds => Mathf.Max(0.01f, dropRetryInterval);
    public float ActionTurnDelaySeconds => Mathf.Max(0f, actionTurnDelay);
    private float TurnDurationSeconds => 180f / Mathf.Max(1f, bodyTurnSpeedDegreesPerSecond);
    public override float ManagedUpdateTickIntervalSeconds => DefaultManagedUpdateDeltaSeconds;

    public void SetEditorSettings(
        bool enableInstancedRendering,
        float pickupIntervalSeconds,
        float turnSpeedDegreesPerSecond,
        float retryIntervalSeconds,
        float turnDelaySeconds)
    {
        useInstancedRendering = enableInstancedRendering;
        pickupInterval = Mathf.Max(0.01f, pickupIntervalSeconds);
        bodyTurnSpeedDegreesPerSecond = Mathf.Max(1f, turnSpeedDegreesPerSecond);
        dropRetryInterval = Mathf.Max(0.01f, retryIntervalSeconds);
        actionTurnDelay = Mathf.Max(0f, turnDelaySeconds);
    }

    public override bool IsWorkingForItemLight
    {
        get
        {
            ResolveObjectInfoStatus(out ObjectInfoStatusLevel statusLevel);
            return statusLevel == ObjectInfoStatusLevel.Working;
        }
    }

    public void SetPreviewRenderingMode(bool enabled)
    {
        previewRenderingMode = enabled;
        if (enabled)
        {
            UnregisterInstancedRendering(true);
            return;
        }

        EnsureInstancedRenderingRegistered();
    }

    public override bool TryGetElectricPowerRequirement(out float wattsPerSecond)
    {
        return TryGetElectricOperationalPowerRequirement(out wattsPerSecond);
    }

    public override bool TryGetElectricPowerDemand(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        if (!TryGetElectricOperationalPowerRequirement(out float configuredWatts))
        {
            return false;
        }

        EnsureRuntimeStateInitialized();
        if (!HasPlacementRuntime())
        {
            return false;
        }

        if (heldItemId >= 0 || IsActiveTransferState(state))
        {
            wattsPerSecond = configuredWatts;
            return true;
        }

        // A valid inserter waiting only for an input item remains part of the power demand.
        // Endpoint validity is sufficient: item availability cannot change this demand.
        if (state == RobotArmState.WaitingForPickup
            && TryResolvePickupCoordinate(out _)
            && TryResolveDropCoordinate(out _))
        {
            wattsPerSecond = configuredWatts;
            return true;
        }

        return false;
    }

    public override void GetObjectInfoStatus(out string statusText, out bool isWorking)
    {
        statusText = ResolveObjectInfoStatus(out ObjectInfoStatusLevel statusLevel);
        isWorking = statusLevel == ObjectInfoStatusLevel.Working;
    }

    public void GetObjectInfoStatus(out string statusText, out bool isWorking, out bool isWarning)
    {
        statusText = ResolveObjectInfoStatus(out ObjectInfoStatusLevel statusLevel);
        isWorking = statusLevel == ObjectInfoStatusLevel.Working;
        isWarning = statusLevel == ObjectInfoStatusLevel.Warning;
    }

    private string ResolveObjectInfoStatus(out ObjectInfoStatusLevel statusLevel)
    {
        statusLevel = ObjectInfoStatusLevel.Error;
        EnsureRuntimeStateInitialized();

        if (!HasPlacementRuntime())
        {
            return "No placement";
        }

        if (TryGetElectricOperationalPowerRequirement(out _) && !UtilityPole.HasElectricityAvailable(this))
        {
            return "No energy";
        }

        if (heldItemId >= 0)
        {
            if (IsDropSuppressedByPlacementMode())
            {
                statusLevel = ObjectInfoStatusLevel.Warning;
                return "Placement mode";
            }

            if (!TryResolveDropCoordinate(out _))
            {
                return "No output area";
            }

            if (!CanPlaceHeldItem())
            {
                return "Output full";
            }

            statusLevel = ObjectInfoStatusLevel.Working;
            return "Working";
        }

        if (IsActiveTransferState(state))
        {
            statusLevel = ObjectInfoStatusLevel.Working;
            return "Working";
        }

        if (!TryResolveDropCoordinate(out _))
        {
            return "No output area";
        }

        if (CanPickupOneItem())
        {
            statusLevel = ObjectInfoStatusLevel.Working;
            return "Working";
        }

        if (!TryResolvePickupCoordinate(out _))
        {
            return "No input area";
        }

        statusLevel = ObjectInfoStatusLevel.Warning;
        return "No input item";
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        RegisterActiveRobotArm(this);
        EnsureBodyRotationCache();
        EnsureHandItemRestTransformCache();
        EnsureRuntimeStateInitialized();
        if (heldItemId < 0 && state == RobotArmState.WaitingForPickup)
        {
            SetBodyLocalRotation(inputBodyLocalRotation);
        }

        RefreshHandItemVisual();
        RefreshRuntimeSleepState(true);
        EnsureInstancedRenderingRegistered();
    }

    protected override void OnDisable()
    {
        UnregisterInstancedRendering();
        SetUpdateTickRegistered(false);
        UnregisterActiveRobotArm(this);
        runtimeSleeping = false;
        handItem?.SetSleepAwakeSleeping(false);
        ResetAnimatorSpeed();
        RefreshSleepAwakeVisual(true);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        EnsureBodyRotationCache();
        ClearHeldItem();
        cachedTerrainGenerator = null;
        pickupTimer = 0f;
        dropRetryTimer = 0f;
        actionTurnTimer = 0f;
        runtimeSleepCheckTimer = 0f;
        waitingForDropRetry = false;
        state = RobotArmState.WaitingForPickup;
        lastElectricPowerSupplyRatio = 1f;
        hasRuntimeStateInitialized = false;
        runtimeSleeping = false;
        previewRenderingMode = false;
        UnregisterInstancedRendering();
        SetUpdateTickRegistered(false);
        SetBodyLocalRotation(inputBodyLocalRotation);
        ResetAnimatorSpeed();
        RefreshSleepAwakeVisual(true);
        base.PrepareForPool();
    }

    private void OnDestroy()
    {
        UnregisterInstancedRendering();
        SetUpdateTickRegistered(false);
        UnregisterActiveRobotArm(this);
        handItem?.SetSleepAwakeSleeping(false);
    }

    public TransferState CaptureTransferState()
    {
        EnsureBodyRotationCache();
        EnsureRuntimeStateInitialized();
        return new TransferState
        {
            heldItemId = heldItemId,
            state = state,
            pickupTimer = Mathf.Max(0f, pickupTimer),
            dropRetryTimer = Mathf.Max(0f, dropRetryTimer),
            actionTurnTimer = Mathf.Max(0f, actionTurnTimer),
            turnTimer = IsTurningState(state) ? TurnDurationSeconds : 0f,
            waitingForDropRetry = waitingForDropRetry
        };
    }

    public void ApplyTransferState(TransferState persistentState)
    {
        EnsureBodyRotationCache();
        if (persistentState == null)
        {
            plannedTransferCommand = PlannedTransferCommand.None;
            stagedTickPlanned = false;
            heldItemId = -1;
            pickupTimer = 0f;
            dropRetryTimer = 0f;
            actionTurnTimer = 0f;
            waitingForDropRetry = false;
            state = RobotArmState.WaitingForPickup;
            hasRuntimeStateInitialized = true;
            SetBodyLocalRotation(inputBodyLocalRotation);
            RefreshHandItemVisual();
            RefreshRuntimeSleepState(true);
            return;
        }

        heldItemId = persistentState.heldItemId;
        state = persistentState.state;
        pickupTimer = Mathf.Max(0f, persistentState.pickupTimer);
        dropRetryTimer = Mathf.Max(0f, persistentState.dropRetryTimer);
        actionTurnTimer = Mathf.Max(0f, persistentState.actionTurnTimer);
        waitingForDropRetry = persistentState.waitingForDropRetry;
        NormalizeRuntimeState();
        hasRuntimeStateInitialized = true;
        ApplyStableBodyRotationForCurrentState();

        RefreshHandItemVisual();
        RefreshRuntimeSleepState(true);
    }

    public void ClearHeldItemAndTransferState()
    {
        ApplyTransferState(null);
    }

    public override void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }

    public override void PlanManagedUpdateTick(float deltaTime)
    {
        plannedTransferCommand = PlannedTransferCommand.None;
        stagedTickPlanned = true;
        plannedPickupAvailabilityChecked = false;
        plannedDropAvailabilityChecked = false;
        runtimeWakePending = false;
        EnsureBodyRotationCache();
        if (ShouldRunRuntimeSleepCheck(deltaTime))
        {
            using var sleepSample = MapObjectTickProfiler.SampleNamed(
                "Runtime",
                nameof(RobotArm),
                "Robot Arm Sleep Check");
            if (RefreshRuntimeSleepState())
            {
                return;
            }
        }

        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Power"))
        {
            deltaTime = ResolvePoweredDeltaTime(deltaTime);
            ApplyPoweredAnimatorSpeed();
        }
        if (deltaTime <= 0f)
        {
            return;
        }

        using var stateSample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm State Tick");
        switch (state)
        {
            case RobotArmState.WaitingForPickup:
                TickPickup(deltaTime);
                break;
            case RobotArmState.WaitingBeforePickupTake:
                TickWaitBeforePickupTake(deltaTime);
                break;
            case RobotArmState.WaitingAfterPickupTake:
                TickWaitAfterPickupTake(deltaTime);
                break;
            case RobotArmState.TurningToDrop:
                TickTurnToDrop(deltaTime);
                break;
            case RobotArmState.WaitingForDrop:
                TickDrop(deltaTime);
                break;
            case RobotArmState.WaitingBeforeDropPlace:
                TickWaitBeforeDropPlace(deltaTime);
                break;
            case RobotArmState.WaitingAfterDropPlace:
                TickWaitAfterDropPlace(deltaTime);
                break;
            case RobotArmState.TurningToPickup:
                TickTurnToPickup(deltaTime);
                break;
        }
    }

    public override void ApplyManagedUpdateTick()
    {
        if (!stagedTickPlanned)
        {
            return;
        }

        stagedTickPlanned = false;
        switch (plannedTransferCommand)
        {
            case PlannedTransferCommand.Pickup:
                ApplyPlannedPickup();
                break;
            case PlannedTransferCommand.Drop:
                ApplyPlannedDrop();
                break;
        }

        plannedTransferCommand = PlannedTransferCommand.None;
    }

    public static void WakeAroundCoordinate(Vector2Int coordinate)
    {
        if (!WakeRobotArmsByCoordinate.TryGetValue(coordinate, out List<RobotArm> robotArms)
            || robotArms == null
            || robotArms.Count <= 0)
        {
            return;
        }

        for (int i = robotArms.Count - 1; i >= 0; i--)
        {
            RobotArm robotArm = robotArms[i];
            if (robotArm == null)
            {
                robotArms.RemoveAt(i);
                continue;
            }

            if (!robotArm.isActiveAndEnabled || !robotArm.IsCoordinateInsideRuntimeSleepWakeRange(coordinate))
            {
                robotArms.RemoveAt(i);
                continue;
            }

            robotArm.WakeRuntimeSleep();
        }

        if (robotArms.Count <= 0)
        {
            WakeRobotArmsByCoordinate.Remove(coordinate);
        }
    }

    public static void RefreshAllSleepAwakeDebugVisuals()
    {
        for (int i = ActiveRobotArms.Count - 1; i >= 0; i--)
        {
            RobotArm robotArm = ActiveRobotArms[i];
            if (robotArm == null)
            {
                ActiveRobotArms.RemoveAt(i);
                continue;
            }

            robotArm.RefreshSleepAwakeVisual(true);
        }
    }

    public static void WakeAllHeldItemTransfers()
    {
        for (int i = ActiveRobotArms.Count - 1; i >= 0; i--)
        {
            RobotArm robotArm = ActiveRobotArms[i];
            if (robotArm == null)
            {
                ActiveRobotArms.RemoveAt(i);
                continue;
            }

            if (robotArm.isActiveAndEnabled && robotArm.heldItemId >= 0)
            {
                robotArm.WakeRuntimeSleep();
            }
        }
    }

    private static void RegisterActiveRobotArm(RobotArm robotArm)
    {
        if (robotArm == null || ActiveRobotArms.Contains(robotArm))
        {
            return;
        }

        ActiveRobotArms.Add(robotArm);
        robotArm.RefreshRegisteredWakeCoordinates();
    }

    private static void UnregisterActiveRobotArm(RobotArm robotArm)
    {
        if (robotArm == null)
        {
            return;
        }

        robotArm.UnregisterWakeCoordinates();
        ActiveRobotArms.Remove(robotArm);
    }

    protected override void OnPlacementRuntimeChanged()
    {
        InvalidateInteractionCoordinateCache();
        base.OnPlacementRuntimeChanged();
        RefreshRegisteredWakeCoordinates();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        InvalidateInteractionCoordinateCache();
        UnregisterWakeCoordinates();
        base.OnPlacementRuntimeCleared();
    }

    private void RefreshRegisteredWakeCoordinates()
    {
        UnregisterWakeCoordinates();
        if (!isActiveAndEnabled || RuntimeOccupiedCoordinates == null || RuntimeOccupiedCoordinates.Count <= 0)
        {
            return;
        }

        for (int occupiedIndex = 0; occupiedIndex < RuntimeOccupiedCoordinates.Count; occupiedIndex++)
        {
            RegisterWakeCoordinatesAround(RuntimeOccupiedCoordinates[occupiedIndex]);
        }

        if (TryResolvePickupCoordinate(out Vector2Int pickupCoordinate))
            RegisterWakeCoordinatesAround(pickupCoordinate);
        if (TryResolveDropCoordinate(out Vector2Int dropCoordinate))
            RegisterWakeCoordinatesAround(dropCoordinate);
    }

    private void RegisterWakeCoordinatesAround(Vector2Int coordinate)
    {
        for (int x = -WakeRangeCellRadius; x <= WakeRangeCellRadius; x++)
        {
            for (int y = -WakeRangeCellRadius; y <= WakeRangeCellRadius; y++)
            {
                RegisterWakeCoordinate(coordinate + new Vector2Int(x, y));
            }
        }
    }

    private void RegisterWakeCoordinate(Vector2Int coordinate)
    {
        if (registeredWakeCoordinates.Contains(coordinate))
        {
            return;
        }

        registeredWakeCoordinates.Add(coordinate);
        if (!WakeRobotArmsByCoordinate.TryGetValue(coordinate, out List<RobotArm> robotArms)
            || robotArms == null)
        {
            robotArms = new List<RobotArm>(4);
            WakeRobotArmsByCoordinate[coordinate] = robotArms;
        }

        if (!robotArms.Contains(this))
        {
            robotArms.Add(this);
        }
    }

    private void UnregisterWakeCoordinates()
    {
        for (int i = 0; i < registeredWakeCoordinates.Count; i++)
        {
            Vector2Int coordinate = registeredWakeCoordinates[i];
            if (!WakeRobotArmsByCoordinate.TryGetValue(coordinate, out List<RobotArm> robotArms)
                || robotArms == null)
            {
                continue;
            }

            robotArms.Remove(this);
            if (robotArms.Count <= 0)
            {
                WakeRobotArmsByCoordinate.Remove(coordinate);
            }
        }

        registeredWakeCoordinates.Clear();
    }

    private bool RefreshRuntimeSleepState(bool force = false)
    {
        bool shouldSleep = ShouldRuntimeSleep();
        SetRuntimeSleeping(shouldSleep, force);
        return shouldSleep;
    }

    private bool ShouldRunRuntimeSleepCheck(float deltaTime)
    {
        if (!Application.isPlaying)
        {
            return true;
        }

        if (!CanRuntimeSleepInCurrentState())
        {
            runtimeSleepCheckTimer = 0f;
            return false;
        }

        runtimeSleepCheckTimer -= Mathf.Max(0f, deltaTime);
        if (runtimeSleepCheckTimer > 0f)
        {
            return false;
        }

        runtimeSleepCheckTimer = Mathf.Max(0.02f, Mathf.Min(RuntimeSleepRecheckIntervalSeconds, PickupIntervalSeconds));
        return true;
    }

    private bool CanRuntimeSleepInCurrentState()
    {
        return heldItemId >= 0
            ? state == RobotArmState.WaitingForDrop
            : state == RobotArmState.WaitingForPickup;
    }

    private bool ShouldRuntimeSleep()
    {
        if (!Application.isPlaying
            || !isActiveAndEnabled
            || !HasPlacementRuntime())
        {
            return false;
        }

        if (heldItemId >= 0)
        {
            return ShouldRuntimeSleepWithHeldItem();
        }

        if (state != RobotArmState.WaitingForPickup)
        {
            return false;
        }

        // Every supported pickup source publishes a coordinate wake when its item
        // availability changes. Once this query misses, polling an otherwise idle arm
        // cannot discover anything that its registered wake coordinates would not.
        return !CanPickupOneItemForCurrentPlan();
    }

    private bool ShouldRuntimeSleepWithHeldItem()
    {
        if (state != RobotArmState.WaitingForDrop)
        {
            return false;
        }

        if (!TryResolveDropCoordinate(out Vector2Int dropCoordinate))
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (terrainGenerator == null)
        {
            return false;
        }

        terrainGenerator.TryGetLoadedBlock(dropCoordinate, out Block dropBlock);

        // Speed can reach zero without crossing a grid cell or changing cargo.
        // Keep the normal drop retry active until that moving target stops.
        if (TryGetFreightCarObject(dropBlock, dropCoordinate, out FreightCar freightCar)
            && freightCar.IsConsistMoving())
        {
            return false;
        }

        return !CanPlaceHeldItemForCurrentPlan();
    }

    private bool IsCoordinateInsideRuntimeSleepWakeRange(Vector2Int coordinate)
    {
        return registeredWakeCoordinates.Contains(coordinate);
    }

    private void WakeRuntimeSleep()
    {
        // Both the coordinate observer and InputOutputModule can deliver the same
        // change. Coalesce until the next tick, but still repair lost registration.
        bool registered = MapObjectTickManager.IsUpdateTickRegistered(this);
        if (runtimeWakePending && !runtimeSleeping && registered)
        {
            return;
        }

        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Wake Applied");
        runtimeWakePending = true;
        pickupTimer = 0f;
        dropRetryTimer = 0f;
        runtimeSleepCheckTimer = 0f;
        waitingForDropRetry = false;
        if (runtimeSleeping)
        {
            SetRuntimeSleeping(false);
        }
        else if (!registered)
        {
            SetUpdateTickRegistered(true);
        }
    }

    protected override void WakeRuntimeUpdate()
    {
        if (Application.isPlaying && isActiveAndEnabled)
        {
            WakeRuntimeSleep();
        }
    }

    private void SetRuntimeSleeping(bool sleeping, bool force = false)
    {
        if (sleeping)
        {
            ApplyRuntimeSleepPose();
        }

        if (!force && runtimeSleeping == sleeping)
        {
            RefreshSleepAwakeVisual();
            return;
        }

        runtimeSleeping = sleeping;
        runtimeSleepCheckTimer = 0f;
        SetUpdateTickRegistered(!runtimeSleeping && isActiveAndEnabled);
        handItem?.SetSleepAwakeSleeping(runtimeSleeping);
        RefreshSleepAwakeVisual(true);
    }

    private void ApplyRuntimeSleepPose()
    {
        EnsureBodyRotationCache();
        if (heldItemId >= 0 && state == RobotArmState.WaitingForDrop)
        {
            SetBodyLocalRotation(GetOutputBodyLocalRotation());
            return;
        }

        if (heldItemId < 0 && state == RobotArmState.WaitingForPickup)
        {
            SetBodyLocalRotation(inputBodyLocalRotation);
        }
    }

    private void SetUpdateTickRegistered(bool registered)
    {
        if (registered)
        {
            MapObjectTickManager.RegisterUpdateTick(this);
        }
        else
        {
            runtimeWakePending = false;
            MapObjectTickManager.UnregisterUpdateTick(this);
        }
    }

    private void RefreshSleepAwakeVisual(bool force = false)
    {
        EnsureSleepAwakeRenderers();
        bool useDarkTint = ShouldUseSleepAwakeDarkTint();
        if (!force && sleepAwakeVisualStateInitialized && lastSleepAwakeDarkTint == useDarkTint)
        {
            return;
        }

        sleepAwakeVisualStateInitialized = true;
        lastSleepAwakeDarkTint = useDarkTint;

        if (sleepAwakeRenderers == null)
        {
            return;
        }

        for (int i = 0; i < sleepAwakeRenderers.Length; i++)
        {
            Renderer targetRenderer = sleepAwakeRenderers[i];
            if (targetRenderer == null)
            {
                continue;
            }

            if (!useDarkTint)
            {
                targetRenderer.SetPropertyBlock(null);
                continue;
            }

            sleepAwakePropertyBlock ??= new MaterialPropertyBlock();
            sleepAwakePropertyBlock.Clear();
            SleepAwakeDebugVisual.ApplySleepingColor(sleepAwakePropertyBlock, targetRenderer.sharedMaterial);
            targetRenderer.SetPropertyBlock(sleepAwakePropertyBlock);
        }
    }

    private bool ShouldUseSleepAwakeDarkTint()
    {
        return runtimeSleeping
               && GameManager.Instance != null
               && GameManager.Instance.ShowSleepAwake;
    }

    internal bool IsInstancedRenderVisible(ProjectF.Rendering.CameraRenderCulling cameraCulling)
    {
        if (!instancedRenderingActive || !isActiveAndEnabled)
        {
            return false;
        }

        EnsureInstancedRenderParts();
        EnsureInstancedRenderCullingData();
        if (instancedRenderParts == null
            || instancedRenderParts.Length <= 0
            || (cameraCulling != null && !cameraCulling.IsAnyLayerVisible(instancedRenderLayerMask)))
        {
            return false;
        }

        Bounds worldBounds = new Bounds(
            transform.position,
            Vector3.one * (instancedRenderCullRadius * 2f));
        return cameraCulling == null || cameraCulling.Intersects(worldBounds);
    }

    internal int AppendInstancedRenderData(VirtualRenderBatchCollection batches, float batchCellSize)
    {
        if (!instancedRenderingActive || batches == null || !isActiveAndEnabled)
        {
            return 0;
        }

        EnsureInstancedRenderParts();
        if (instancedRenderParts == null || instancedRenderParts.Length <= 0)
        {
            return 0;
        }

        int addedMatrixCount = 0;
        bool useSleepTint = ShouldUseSleepAwakeDarkTint();
        float safeCellSize = Mathf.Max(1f, batchCellSize);
        Vector3 cellPosition = transform.position;
        int cellX = Mathf.FloorToInt(cellPosition.x / safeCellSize);
        int cellZ = Mathf.FloorToInt(cellPosition.z / safeCellSize);

        for (int partIndex = 0; partIndex < instancedRenderParts.Length; partIndex++)
        {
            RobotArmInstancedRenderPart part = instancedRenderParts[partIndex];
            if (!part.IsValid)
            {
                continue;
            }

            Mesh mesh = part.Mesh;
            Material[] materials = part.SharedMaterials;
            int materialCount = part.MaterialCount;
            Matrix4x4 localToWorldMatrix = part.Transform.localToWorldMatrix;
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                VirtualRenderBatchKey key = new VirtualRenderBatchKey(
                    mesh,
                    material,
                    part.Layer,
                    materialIndex,
                    part.ShadowCastingMode,
                    part.ReceiveShadows,
                    false,
                    useSleepTint,
                    false,
                    default,
                    0,
                    cellX,
                    cellZ);
                batches.AddMatrix(key, localToWorldMatrix);
                addedMatrixCount++;
            }
        }

        return addedMatrixCount;
    }

    private void EnsureInstancedRenderingRegistered()
    {
        if (!Application.isPlaying || !useInstancedRendering || previewRenderingMode)
        {
            SetInstancedRenderingActive(false, previewRenderingMode);
            return;
        }

        if (renderBatcher == null)
        {
            TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
            GameObject host = terrainGenerator != null ? terrainGenerator.gameObject : null;
            renderBatcher = RobotArmRenderBatcher.EnsureFor(host);
        }

        if (renderBatcher == null)
        {
            SetInstancedRenderingActive(false);
            return;
        }

        EnsureInstancedRenderParts();
        renderBatcher.Register(this);
        SetInstancedRenderingActive(true);
    }

    private void UnregisterInstancedRendering(bool forceSourceVisible = false)
    {
        if (renderBatcher != null)
        {
            renderBatcher.Unregister(this);
            renderBatcher = null;
        }

        SetInstancedRenderingActive(false, forceSourceVisible);
    }

    private void SetInstancedRenderingActive(bool active, bool forceSourceVisible = false)
    {
        if (instancedRenderingActive == active)
        {
            if (!active && forceSourceVisible)
            {
                EnsureInstancedRenderParts();
                SetInstancedSourceRenderersEnabled(true, true);
                RefreshSleepAwakeVisual(true);
            }

            return;
        }

        EnsureInstancedRenderParts();
        instancedRenderingActive = active;
        SetInstancedSourceRenderersEnabled(!instancedRenderingActive, forceSourceVisible);
        RefreshSleepAwakeVisual(true);
    }

    private void SetInstancedSourceRenderersEnabled(bool enabled, bool forceVisible = false)
    {
        if (instancedRenderParts == null)
        {
            return;
        }

        for (int i = 0; i < instancedRenderParts.Length; i++)
        {
            RobotArmInstancedRenderPart part = instancedRenderParts[i];
            if (part.Renderer == null)
            {
                continue;
            }

            part.Renderer.enabled = enabled && (forceVisible || part.OriginalRendererEnabled);
        }
    }

    private void EnsureInstancedRenderParts()
    {
        if (instancedRenderParts != null)
        {
            return;
        }

        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
        Transform handRoot = handItem != null ? handItem.transform : null;
        List<RobotArmInstancedRenderPart> parts = new List<RobotArmInstancedRenderPart>(renderers.Length);
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer targetRenderer = renderers[i];
            if (targetRenderer == null)
            {
                continue;
            }

            Transform targetTransform = targetRenderer.transform;
            if ((handRoot != null && targetTransform.IsChildOf(handRoot))
                || targetRenderer.GetComponentInParent<PortableObject>() != null)
            {
                continue;
            }

            MeshFilter meshFilter = targetRenderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            if (materials == null || materials.Length <= 0)
            {
                continue;
            }

            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material != null && !material.enableInstancing)
                {
                    material.enableInstancing = true;
                }
            }

            parts.Add(new RobotArmInstancedRenderPart(
                targetRenderer,
                meshFilter,
                targetTransform,
                materials,
                targetRenderer.enabled));
        }

        instancedRenderParts = parts.ToArray();
        instancedRenderCullingDataInitialized = false;
    }

    private void EnsureInstancedRenderCullingData()
    {
        if (instancedRenderCullingDataInitialized)
        {
            return;
        }

        instancedRenderCullingDataInitialized = true;
        instancedRenderLayerMask = 0;
        instancedRenderCullRadius = MinimumInstancedRenderCullRadius;
        if (instancedRenderParts == null)
        {
            return;
        }

        Vector3 rootPosition = transform.position;
        for (int i = 0; i < instancedRenderParts.Length; i++)
        {
            RobotArmInstancedRenderPart part = instancedRenderParts[i];
            if (!part.IsValid)
            {
                continue;
            }

            if (part.Layer >= 0 && part.Layer <= 31)
            {
                instancedRenderLayerMask |= 1 << part.Layer;
            }

            Bounds rendererBounds = part.Renderer.bounds;
            float radius = Vector3.Distance(rootPosition, rendererBounds.center)
                           + rendererBounds.extents.magnitude
                           + InstancedRenderCullPadding;
            instancedRenderCullRadius = Mathf.Max(instancedRenderCullRadius, radius);
        }
    }

    private void EnsureSleepAwakeRenderers()
    {
        if (sleepAwakeRenderers != null)
        {
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (handItem == null)
        {
            sleepAwakeRenderers = renderers;
            return;
        }

        Transform handRoot = handItem.transform;
        List<Renderer> filteredRenderers = new List<Renderer>(renderers.Length);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null
                || (handRoot != null && targetRenderer.transform.IsChildOf(handRoot)))
            {
                continue;
            }

            filteredRenderers.Add(targetRenderer);
        }

        sleepAwakeRenderers = filteredRenderers.ToArray();
    }

    private void TickPickup(float deltaTime)
    {
        if (heldItemId >= 0)
        {
            state = RobotArmState.TurningToDrop;
            return;
        }

        if (!HasPlacementRuntime())
        {
            return;
        }

        if (TickTimerStillRunning(ref pickupTimer, deltaTime))
        {
            return;
        }

        RotateBodyToward(inputBodyLocalRotation, deltaTime);
        if (CanPickupOneItemForCurrentPlan())
        {
            PlayPickAnimation();
            state = RobotArmState.WaitingBeforePickupTake;
            actionTurnTimer = actionTurnDelay;
            return;
        }

        pickupTimer = pickupInterval;
    }

    private void TickWaitBeforePickupTake(float deltaTime)
    {
        if (heldItemId >= 0)
        {
            state = RobotArmState.TurningToDrop;
            return;
        }

        if (TickTimerStillRunning(ref actionTurnTimer, deltaTime))
        {
            return;
        }

        plannedTransferCommand = PlannedTransferCommand.Pickup;
    }

    private void ApplyPlannedPickup()
    {
        if (state != RobotArmState.WaitingBeforePickupTake || heldItemId >= 0)
        {
            return;
        }

        if (TryPickupOneItem(out int pickedItemId, out Vector3 pickupWorldPosition))
        {
            SetHeldItem(pickedItemId, pickupWorldPosition);
            dropRetryTimer = 0f;
            waitingForDropRetry = false;
            state = RobotArmState.WaitingAfterPickupTake;
            actionTurnTimer = actionTurnDelay;
            return;
        }

        state = RobotArmState.WaitingForPickup;
        pickupTimer = pickupInterval;
    }

    private void TickWaitAfterPickupTake(float deltaTime)
    {
        if (heldItemId < 0)
        {
            state = RobotArmState.WaitingForPickup;
            pickupTimer = pickupInterval;
            return;
        }

        if (TickTimerStillRunning(ref actionTurnTimer, deltaTime))
        {
            return;
        }

        state = RobotArmState.TurningToDrop;
    }

    private void TickTurnToDrop(float deltaTime)
    {
        if (heldItemId < 0)
        {
            waitingForDropRetry = false;
            dropRetryTimer = 0f;
            state = RobotArmState.TurningToPickup;
            return;
        }

        if (RotateBodyToward(GetOutputBodyLocalRotation(), deltaTime))
        {
            state = RobotArmState.WaitingForDrop;
            waitingForDropRetry = false;
            dropRetryTimer = 0f;
        }
    }

    private void TickDrop(float deltaTime)
    {
        if (heldItemId < 0)
        {
            waitingForDropRetry = false;
            dropRetryTimer = 0f;
            state = RobotArmState.TurningToPickup;
            return;
        }

        if (TickTimerStillRunning(ref dropRetryTimer, deltaTime))
        {
            return;
        }

        waitingForDropRetry = false;
        if (CanPlaceHeldItemForCurrentPlan())
        {
            PlayDropAnimation();
            state = RobotArmState.WaitingBeforeDropPlace;
            actionTurnTimer = actionTurnDelay;
            return;
        }

        BeginDropRetryDelay();
    }

    private void TickWaitBeforeDropPlace(float deltaTime)
    {
        if (heldItemId < 0)
        {
            waitingForDropRetry = false;
            dropRetryTimer = 0f;
            state = RobotArmState.TurningToPickup;
            return;
        }

        if (TickTimerStillRunning(ref actionTurnTimer, deltaTime))
        {
            return;
        }

        plannedTransferCommand = PlannedTransferCommand.Drop;
    }

    private void ApplyPlannedDrop()
    {
        if (state != RobotArmState.WaitingBeforeDropPlace || heldItemId < 0)
        {
            return;
        }

        if (TryPlaceHeldItem())
        {
            dropRetryTimer = 0f;
            ClearHeldItem();
            state = RobotArmState.WaitingAfterDropPlace;
            actionTurnTimer = actionTurnDelay;
            return;
        }

        BeginDropRetryDelay();
        state = RobotArmState.WaitingForDrop;
    }

    private void TickWaitAfterDropPlace(float deltaTime)
    {
        if (heldItemId >= 0)
        {
            state = RobotArmState.TurningToDrop;
            return;
        }

        if (TickTimerStillRunning(ref actionTurnTimer, deltaTime))
        {
            return;
        }

        state = RobotArmState.TurningToPickup;
    }

    private void TickTurnToPickup(float deltaTime)
    {
        if (RotateBodyToward(inputBodyLocalRotation, deltaTime))
        {
            state = RobotArmState.WaitingForPickup;
            pickupTimer = pickupInterval;
        }
    }

    private static bool TickTimerStillRunning(ref float timer, float deltaTime)
    {
        if (timer <= 0f)
        {
            return false;
        }

        timer = Mathf.Max(0f, timer - Mathf.Max(0f, deltaTime));
        return timer > 0f;
    }

    private bool TryPickupOneItem(out int pickedItemId, out Vector3 pickupWorldPosition)
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Pickup Transfer");
        pickedItemId = -1;
        pickupWorldPosition = GetHandRestWorldPosition();
        if (!TryResolvePickupCandidate(
                out Block pickupBlock,
                out BoxObject boxObject,
                out FreightCar freightCar,
                out RobotArmPickupSource pickupSource,
                out Vector2Int pickupCoordinate,
                out Vector3 referenceWorldPosition,
                out pickupWorldPosition))
        {
            return false;
        }

        switch (pickupSource)
        {
            case RobotArmPickupSource.Floor:
                return pickupBlock.TryTakeClosestFloorObject(referenceWorldPosition, PickupItemFilter, out pickedItemId);
            case RobotArmPickupSource.Box:
                return boxObject != null && boxObject.TryTakeOneContainedObject(PickupItemFilter, out pickedItemId);
            case RobotArmPickupSource.FreightCar:
                return freightCar != null
                       && !freightCar.IsConsistMoving()
                       && freightCar.TryTakeOneItem(
                           referenceWorldPosition,
                           PickupItemFilter,
                           out pickedItemId,
                           out pickupWorldPosition);
            case RobotArmPickupSource.Conveyor:
                return pickupBlock.TryTakeOneConveyorObject(
                    referenceWorldPosition,
                    PickupItemFilter,
                    out pickedItemId);
            case RobotArmPickupSource.InputArea:
                return TryTakeFilteredInputAreaItem(pickupBlock, out pickedItemId);
            case RobotArmPickupSource.SavedFloor:
                return TryTakeSavedFloorItem(pickupCoordinate, out pickedItemId);
            case RobotArmPickupSource.SavedConveyor:
                return TryTakeSavedConveyorItem(pickupCoordinate, GetBodyWorldPosition(), out pickedItemId);
            case RobotArmPickupSource.SavedInputArea:
                return TryTakeSavedInputAreaItem(pickupCoordinate, out pickedItemId);
            default:
                return false;
        }
    }

    private bool CanPickupOneItem()
    {
        return TryResolvePickupCandidate(out _, out _, out _, out _, out _, out _, out _);
    }

    private bool CanPickupOneItemForCurrentPlan()
    {
        if (!stagedTickPlanned)
        {
            return CanPickupOneItem();
        }

        if (!plannedPickupAvailabilityChecked)
        {
            plannedPickupAvailable = CanPickupOneItem();
            plannedPickupAvailabilityChecked = true;
        }

        return plannedPickupAvailable;
    }

    private bool TryResolvePickupCandidate(
        out Block pickupBlock,
        out BoxObject boxObject,
        out FreightCar freightCar,
        out RobotArmPickupSource pickupSource,
        out Vector2Int pickupCoordinate,
        out Vector3 referenceWorldPosition,
        out Vector3 pickupWorldPosition)
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Pickup Query");
        pickupBlock = null;
        boxObject = null;
        freightCar = null;
        pickupSource = RobotArmPickupSource.None;
        pickupCoordinate = default;
        referenceWorldPosition = GetHandWorldPosition();
        pickupWorldPosition = referenceWorldPosition;

        if (!TryResolvePickupCoordinate(out pickupCoordinate))
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (terrainGenerator == null)
        {
            return false;
        }

        bool hasLoadedPickupBlock = TryGetLoadedInteractionBlock(
            terrainGenerator,
            pickupCoordinate,
            out pickupBlock);

        Vector3 conveyorSelectionReferenceWorldPosition = GetBodyWorldPosition();
        float bestDistanceSqr = float.MaxValue;

        if (hasLoadedPickupBlock
            && pickupBlock.TryGetClosestFloorObjectWorldPosition(referenceWorldPosition, PickupItemFilter, out Vector3 candidateWorldPosition))
        {
            TryChoosePickupSource(RobotArmPickupSource.Floor, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
        }

        if (hasLoadedPickupBlock && TryGetBoxObject(pickupBlock, out BoxObject candidateBoxObject))
        {
            boxObject = candidateBoxObject;
            if (candidateBoxObject.TryGetContainedObjectTopItemId(out int containedItemId)
                && candidateBoxObject.CanTakeContainedObject()
                && AcceptsPickupItem(containedItemId)
                && candidateBoxObject.TryGetContainedObjectTopWorldPosition(out candidateWorldPosition))
            {
                TryChoosePickupSource(RobotArmPickupSource.Box, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
            }
        }

        if (hasLoadedPickupBlock
            && TryGetFreightCarObject(pickupBlock, pickupCoordinate, out FreightCar candidateFreightCar))
        {
            if (candidateFreightCar.IsConsistMoving())
            {
                return false;
            }

            freightCar = candidateFreightCar;
            if (candidateFreightCar.TryGetTopItem(referenceWorldPosition, PickupItemFilter, out _, out candidateWorldPosition))
            {
                TryChoosePickupSource(RobotArmPickupSource.FreightCar, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
            }
        }

        if (hasLoadedPickupBlock
            && pickupBlock.TryGetClosestConveyorObjectWorldPosition(
                conveyorSelectionReferenceWorldPosition,
                PickupItemFilter,
                out candidateWorldPosition))
        {
            TryChoosePickupSource(RobotArmPickupSource.Conveyor, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
        }

        if (hasLoadedPickupBlock && boxObject == null)
        {
            int inputAreaItemId = pickupBlock.GetInputAreaCenterItemId();
            if (AcceptsPickupItem(inputAreaItemId)
                && pickupBlock.TryGetInputAreaCenterTopWorldPosition(-1, out candidateWorldPosition))
            {
                TryChoosePickupSource(RobotArmPickupSource.InputArea, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
            }
        }

        TryResolveSavedPickupCandidate(
            terrainGenerator,
            pickupCoordinate,
            hasLoadedPickupBlock,
            conveyorSelectionReferenceWorldPosition,
            ref pickupSource,
            ref bestDistanceSqr,
            ref pickupWorldPosition);

        if (pickupSource == RobotArmPickupSource.Conveyor
            || pickupSource == RobotArmPickupSource.SavedConveyor)
        {
            referenceWorldPosition = conveyorSelectionReferenceWorldPosition;
        }

        return pickupSource != RobotArmPickupSource.None;
    }

    private void TryResolveSavedPickupCandidate(
        TerrainGenerator terrainGenerator,
        Vector2Int pickupCoordinate,
        bool hasLoadedPickupBlock,
        Vector3 conveyorSelectionReferenceWorldPosition,
        ref RobotArmPickupSource pickupSource,
        ref float bestDistanceSqr,
        ref Vector3 pickupWorldPosition)
    {
        BlockStateStore stateStore = ResolveBlockStateStore();
        if (stateStore == null)
        {
            return;
        }

        Vector3 referenceWorldPosition = GetHandWorldPosition();
        Vector3 savedWorldPosition = GetSavedCoordinateWorldPosition(pickupCoordinate);

        if (ShouldUseSavedFloorAreaCoordinate(terrainGenerator, pickupCoordinate, hasLoadedPickupBlock))
        {
            if (stateStore.TryPeekSavedFloorItem(pickupCoordinate, PickupItemFilter, out _))
            {
                TryChoosePickupSource(
                    RobotArmPickupSource.SavedFloor,
                    savedWorldPosition,
                    referenceWorldPosition,
                    ref pickupSource,
                    ref bestDistanceSqr,
                    ref pickupWorldPosition);
            }

            if (stateStore.TryPeekSavedCenterTopItem(pickupCoordinate, PickupItemFilter, out _))
            {
                TryChoosePickupSource(
                    RobotArmPickupSource.SavedInputArea,
                    savedWorldPosition,
                    referenceWorldPosition,
                    ref pickupSource,
                    ref bestDistanceSqr,
                    ref pickupWorldPosition);
            }
        }

        if (ShouldUseSavedConveyorCoordinate(terrainGenerator, pickupCoordinate, hasLoadedPickupBlock)
            && stateStore.TryPeekSavedConveyorItem(
                pickupCoordinate,
                PickupItemFilter,
                conveyorSelectionReferenceWorldPosition,
                out _,
                out Vector3 conveyorWorldPosition))
        {
            TryChoosePickupSource(
                RobotArmPickupSource.SavedConveyor,
                conveyorWorldPosition,
                referenceWorldPosition,
                ref pickupSource,
                ref bestDistanceSqr,
                ref pickupWorldPosition);
        }
    }

    private bool TryTakeSavedFloorItem(Vector2Int pickupCoordinate, out int pickedItemId)
    {
        pickedItemId = -1;
        BlockStateStore stateStore = ResolveBlockStateStore();
        return stateStore != null
               && stateStore.TryTakeSavedFloorItem(pickupCoordinate, PickupItemFilter, out pickedItemId);
    }

    private bool TryTakeSavedConveyorItem(Vector2Int pickupCoordinate, Vector3 referenceWorldPosition, out int pickedItemId)
    {
        pickedItemId = -1;
        BlockStateStore stateStore = ResolveBlockStateStore();
        return stateStore != null
               && stateStore.TryTakeSavedConveyorItem(
                   pickupCoordinate,
                   PickupItemFilter,
                   referenceWorldPosition,
                   out pickedItemId);
    }

    private bool TryTakeSavedInputAreaItem(Vector2Int pickupCoordinate, out int pickedItemId)
    {
        pickedItemId = -1;
        BlockStateStore stateStore = ResolveBlockStateStore();
        return stateStore != null
               && stateStore.TryTakeSavedCenterTopItem(pickupCoordinate, PickupItemFilter, out pickedItemId);
    }

    private static void TryChoosePickupSource(
        RobotArmPickupSource candidateSource,
        Vector3 candidateWorldPosition,
        Vector3 referenceWorldPosition,
        ref RobotArmPickupSource bestSource,
        ref float bestDistanceSqr,
        ref Vector3 bestWorldPosition)
    {
        Vector3 offset = candidateWorldPosition - referenceWorldPosition;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (bestSource != RobotArmPickupSource.None && distanceSqr >= bestDistanceSqr)
        {
            return;
        }

        bestSource = candidateSource;
        bestDistanceSqr = distanceSqr;
        bestWorldPosition = candidateWorldPosition;
    }

    private bool TryTakeFilteredInputAreaItem(Block pickupBlock, out int pickedItemId)
    {
        pickedItemId = -1;
        if (pickupBlock == null)
        {
            return false;
        }

        int itemId = pickupBlock.GetInputAreaCenterItemId();
        return AcceptsPickupItem(itemId)
               && pickupBlock.TryConsumeOneInputAreaCenterObject(itemId, out pickedItemId);
    }

    private System.Predicate<int> PickupItemFilter => cachedPickupItemFilter ??= AcceptsPickupItem;

    private bool AcceptsPickupItem(int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        return IsItemFilterEnabled(itemId, ResolveFilterBitCount(itemId));
    }

    private int ResolveFilterBitCount(int fallbackItemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null || definitions.Count <= 0)
        {
            return Mathf.Max(1, fallbackItemId + 1);
        }

        if (cachedFilterBitCountItemManager != itemManager
            || cachedFilterBitCountDefinitionCount != definitions.Count)
        {
            int maxItemId = 0;
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                if (definition.id > maxItemId)
                {
                    maxItemId = definition.id;
                }
            }

            cachedFilterBitCountItemManager = itemManager;
            cachedFilterBitCountDefinitionCount = definitions.Count;
            cachedFilterBitCount = Mathf.Max(1, maxItemId + 1);
        }

        return Mathf.Max(cachedFilterBitCount, fallbackItemId + 1);
    }

    private bool TryPlaceHeldItem()
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Drop Transfer");
        if (heldItemId < 0
            || IsDropSuppressedByPlacementMode()
            || !TryResolveDropCoordinate(out Vector2Int dropCoordinate))
        {
            return false;
        }

        int itemId = heldItemId;
        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        Block dropBlock = null;
        bool hasLoadedDropBlock = TryGetLoadedInteractionBlock(
            terrainGenerator,
            dropCoordinate,
            out dropBlock);

        if (ShouldUseSavedDropCoordinate(terrainGenerator, dropCoordinate, dropBlock))
        {
            return TryPlaceHeldItemInSavedCoordinate(dropCoordinate, itemId, true);
        }

        if (!hasLoadedDropBlock)
        {
            return false;
        }

        Vector3 dropReferenceWorldPosition = GetDropReferencePosition(dropBlock, dropCoordinate);
        Vector3 dropStartWorldPosition = GetHandRestWorldPosition();
        if (TryGetFreightCarObject(dropBlock, dropCoordinate, out FreightCar freightCar))
        {
            if (freightCar.IsConsistMoving())
            {
                return false;
            }

            if (freightCar.TryAddItemStack(
                    itemId,
                    1,
                    dropStartWorldPosition,
                    () => dropStartWorldPosition,
                    0f,
                    out int addedCount)
                && addedCount > 0)
            {
                return true;
            }
        }

        if (IsFarmlandFertilizerDropTarget(
                terrainGenerator,
                dropBlock,
                dropCoordinate,
                itemId))
        {
            return dropBlock.TryAddFloorObjectAnimated(
                itemId,
                dropStartWorldPosition,
                0f,
                out _);
        }

        if (HasBlockingDropMapObject(dropBlock))
        {
            return false;
        }

        if (TryGetBoxObject(dropBlock, out BoxObject boxObject)
            && boxObject.TryPutOneContainedObject(itemId, dropStartWorldPosition, 0f, out _, false, ItemMoveDuration))
        {
            return true;
        }

        if (dropBlock.TryAddConveyorObjectAnimatedAtPlacement(
                itemId,
                dropReferenceWorldPosition,
                dropStartWorldPosition,
                0f,
                out _,
                null,
                () => dropStartWorldPosition,
                ItemMoveDuration,
                false,
                ItemMoveDuration))
        {
            return true;
        }

        if (CanPlaceSingleLineDrop(dropBlock, dropCoordinate)
            && dropBlock.TryAddInputAreaCenterObjectAnimated(itemId, dropStartWorldPosition, 0f, out _, null, null, false, ItemMoveDuration))
        {
            return true;
        }

        return false;
    }

    private bool CanPlaceHeldItem()
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Drop Query");
        if (heldItemId < 0
            || IsDropSuppressedByPlacementMode()
            || !TryResolveDropCoordinate(out Vector2Int dropCoordinate))
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        Block dropBlock = null;
        bool hasLoadedDropBlock = TryGetLoadedInteractionBlock(
            terrainGenerator,
            dropCoordinate,
            out dropBlock);
        if (ShouldUseSavedDropCoordinate(terrainGenerator, dropCoordinate, dropBlock))
        {
            return TryPlaceHeldItemInSavedCoordinate(dropCoordinate, heldItemId, false);
        }

        if (!hasLoadedDropBlock)
        {
            return false;
        }

        return CanPlaceHeldItem(dropBlock, dropCoordinate);
    }

    private bool CanPlaceHeldItemForCurrentPlan()
    {
        if (!stagedTickPlanned)
        {
            return CanPlaceHeldItem();
        }

        if (!plannedDropAvailabilityChecked)
        {
            plannedDropAvailable = CanPlaceHeldItem();
            plannedDropAvailabilityChecked = true;
        }

        return plannedDropAvailable;
    }

    private static bool TryGetLoadedInteractionBlock(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        out Block block)
    {
        block = null;
        if (terrainGenerator == null
            || !terrainGenerator.TryGetLoadedBlock(coordinate, out block)
            || block == null)
        {
            return false;
        }

        if (block.IsRuntimeConveyor)
        {
            // Continuously owned transport has no per-cell arrival event. Keep the
            // arm's pickup/output cell as an observable port so occupancy and hold
            // changes can wake a sleeping arm without periodic neighborhood scans.
            block.EnsureConveyorTransportInteractionBoundary();
        }

        return true;
    }

    private static bool IsDropSuppressedByPlacementMode()
    {
        GameManager gameManager = GameManager.Instance;
        return gameManager != null && gameManager.PlayerInteractionLocked;
    }

    private bool CanPlaceHeldItem(Block dropBlock, Vector2Int dropCoordinate)
    {
        int itemId = heldItemId;
        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        Vector3 dropReferenceWorldPosition = GetDropReferencePosition(dropBlock, dropCoordinate);
        if (TryGetFreightCarObject(dropBlock, dropCoordinate, out FreightCar freightCar))
        {
            if (freightCar.IsConsistMoving())
            {
                return false;
            }

            if (freightCar.CanAddItem(itemId, dropReferenceWorldPosition))
            {
                return true;
            }
        }

        if (IsFarmlandFertilizerDropTarget(
                terrainGenerator,
                dropBlock,
                dropCoordinate,
                itemId))
        {
            return terrainGenerator.CanAbsorbDroppedFarmlandFertilizer(
                       dropCoordinate,
                       itemId)
                   || dropBlock.CanAddFloorObjects(1, itemId);
        }

        if (HasBlockingDropMapObject(dropBlock))
        {
            return false;
        }

        if (TryGetBoxObject(dropBlock, out BoxObject boxObject)
            && boxObject.CanPutOneContainedObject(itemId))
        {
            return true;
        }

        if (dropBlock.CanAddConveyorObjectAtPlacement(itemId, dropReferenceWorldPosition))
        {
            return true;
        }

        return CanPlaceSingleLineDrop(dropBlock, dropCoordinate)
               && dropBlock.CanAddInputAreaCenterObjects(1, itemId);
    }

    private bool TryPlaceHeldItemInSavedCoordinate(Vector2Int dropCoordinate, int itemId, bool mutate)
    {
        if (itemId < 0)
        {
            return false;
        }

        BlockStateStore stateStore = ResolveBlockStateStore();
        if (stateStore == null)
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (IsSavedFarmlandFertilizerDropTarget(
                terrainGenerator,
                stateStore,
                dropCoordinate,
                itemId))
        {
            if (mutate
                && terrainGenerator.TryAbsorbDroppedFarmlandFertilizer(
                    dropCoordinate,
                    itemId))
            {
                return true;
            }

            int floorCapacity = ItemDefinition.ResolveStackCapacity(
                InputOutputModule.ResolveItemDefinition(itemId),
                10);
            return mutate
                ? stateStore.TryAddSavedFloorItems(
                    dropCoordinate,
                    itemId,
                    1,
                    floorCapacity)
                : terrainGenerator.CanAbsorbDroppedFarmlandFertilizer(
                      dropCoordinate,
                      itemId)
                  || stateStore.CanAddSavedFloorItems(
                      dropCoordinate,
                      itemId,
                      1,
                      floorCapacity);
        }

        Vector3 referenceWorldPosition = GetSavedCoordinateWorldPosition(dropCoordinate);
        if (mutate)
        {
            if (stateStore.TryAddSavedConveyorItem(dropCoordinate, itemId, referenceWorldPosition))
            {
                return true;
            }
        }
        else if (stateStore.CanAddSavedConveyorItem(dropCoordinate, itemId, referenceWorldPosition))
        {
            return true;
        }

        if (!CanPlaceSavedSingleLineDrop(stateStore, dropCoordinate)
            || !InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(dropCoordinate, itemId))
        {
            return false;
        }

        int capacity = ResolveSavedCenterCapacity(stateStore, dropCoordinate, itemId, 10);
        return mutate
            ? stateStore.TryAddSavedCenterItems(dropCoordinate, itemId, 1, capacity)
            : stateStore.CanAddSavedCenterItems(dropCoordinate, itemId, 1, capacity);
    }

    private bool TryResolvePickupCoordinate(out Vector2Int pickupCoordinate)
    {
        if (!EnsureInteractionCoordinateCache())
        {
            pickupCoordinate = default;
            return false;
        }

        pickupCoordinate = cachedPickupCoordinate;
        return true;
    }

    private bool TryResolveDropCoordinate(out Vector2Int dropCoordinate)
    {
        if (!EnsureInteractionCoordinateCache())
        {
            dropCoordinate = default;
            return false;
        }

        dropCoordinate = cachedDropCoordinate;
        return true;
    }

    private void InvalidateInteractionCoordinateCache()
    {
        interactionCoordinateCacheValid = false;
        cachedInteractionPlacementSequence = 0;
        cachedPickupCoordinate = default;
        cachedDropCoordinate = default;
    }

    private bool EnsureInteractionCoordinateCache()
    {
        if (interactionCoordinateCacheValid && cachedInteractionPlacementSequence == RuntimePlacementSequence)
            return true;

        interactionCoordinateCacheValid = false;
        if (!TryGetPlacementRuntime(out Vector2Int anchor, out int quarterTurns))
            return false;

        bool foundInput = false;
        bool foundOutput = false;
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.InputItem && placement.blockType != RectGridBlockType.Output)
                continue;
            if (!TryGetRectGridPlacementCoordinate(this, anchor, quarterTurns, placement, out Vector2Int coordinate))
                continue;
            if (placement.blockType == RectGridBlockType.InputItem && !foundInput)
            {
                cachedPickupCoordinate = coordinate;
                foundInput = true;
            }
            else if (placement.blockType == RectGridBlockType.Output && !foundOutput)
            {
                cachedDropCoordinate = coordinate;
                foundOutput = true;
            }
        }

        cachedInteractionPlacementSequence = RuntimePlacementSequence;
        interactionCoordinateCacheValid = foundInput && foundOutput;
        return interactionCoordinateCacheValid;
    }

    public bool TryCollectTransferItemIds(ICollection<int> itemIds)
    {
        List<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        if (itemIds == null || definitions == null)
            return false;
        bool found = false;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id < 0 || IsFluidItemDefinition(definition))
                continue;
            // Area storage accepts solids; the existing pickup filter selects what the arm moves.
            itemIds.Add(definition.id);
            found = true;
        }
        return found;
    }

    protected override bool AppendOutputItemIds(ISet<int> itemIds) => TryCollectTransferItemIds(itemIds);

    protected override bool ShouldPlayWorkAnimation() => false;

    private TerrainGenerator ResolveTerrainGenerator()
    {
        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = TerrainGenerator.ResolveActive();
        }

        return cachedTerrainGenerator;
    }

    private BlockStateStore ResolveBlockStateStore()
    {
        if (cachedBlockStateStore != null)
        {
            return cachedBlockStateStore;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        cachedBlockStateStore = terrainGenerator != null ? terrainGenerator.GetComponent<BlockStateStore>() : null;
        return cachedBlockStateStore;
    }

    private static bool ShouldUseSavedFloorAreaCoordinate(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        bool hasLoadedBlock)
    {
        return !hasLoadedBlock
               || (terrainGenerator != null && terrainGenerator.IsFloorObjectCoordinateVirtualized(coordinate));
    }

    private static bool ShouldUseSavedConveyorCoordinate(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        bool hasLoadedBlock)
    {
        return !hasLoadedBlock
               || (terrainGenerator != null && terrainGenerator.IsConveyorItemCoordinateVirtualized(coordinate));
    }

    private static bool ShouldUseSavedDropCoordinate(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        Block dropBlock)
    {
        if (dropBlock == null)
        {
            return true;
        }

        // Floor stacks and belt lanes can be virtualized independently.
        return terrainGenerator != null
               && (IsConveyorBeltMapObject(dropBlock.MapObject)
                   ? terrainGenerator.IsConveyorItemCoordinateVirtualized(coordinate)
                   : terrainGenerator.IsFloorObjectCoordinateVirtualized(coordinate));
    }

    private static Vector3 GetPickupReferencePosition(Block pickupBlock, Vector2Int pickupCoordinate)
    {
        if (pickupBlock != null)
        {
            return pickupBlock.WorldPosition;
        }

        return new Vector3(pickupCoordinate.x, 0f, pickupCoordinate.y);
    }

    private Vector3 GetDropReferencePosition(Block dropBlock, Vector2Int dropCoordinate)
    {
        if (handItem != null || body != null)
        {
            return GetHandWorldPosition();
        }

        return GetPickupReferencePosition(dropBlock, dropCoordinate);
    }

    private static Vector3 GetSavedCoordinateWorldPosition(Vector2Int coordinate)
    {
        return new Vector3(coordinate.x, 0.2f, coordinate.y);
    }

    private static bool CoordinateAcceptsInputAreaObject(Vector2Int coordinate)
    {
        return InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate);
    }

    private static bool CanPlaceSingleLineDrop(Block dropBlock, Vector2Int coordinate)
    {
        if (dropBlock == null || IsConveyorBeltMapObject(dropBlock.MapObject))
        {
            return false;
        }

        return CoordinateAcceptsInputAreaObject(coordinate)
               || dropBlock.MapObject == null
               || IsOreMapObject(dropBlock.MapObject);
    }

    private static bool IsFarmlandFertilizerDropTarget(
        TerrainGenerator terrainGenerator,
        Block dropBlock,
        Vector2Int coordinate,
        int itemId)
    {
        if (terrainGenerator == null
            || dropBlock == null
            || !terrainGenerator.IsFarmlandFertilizerItemAt(coordinate, itemId))
        {
            return false;
        }

        IMapObjectTarget mapObject = dropBlock.MapObject;
        return mapObject == null || mapObject is ResourceInstance;
    }

    private static bool IsSavedFarmlandFertilizerDropTarget(
        TerrainGenerator terrainGenerator,
        BlockStateStore stateStore,
        Vector2Int coordinate,
        int itemId)
    {
        return terrainGenerator != null
               && stateStore != null
               && terrainGenerator.IsFarmlandFertilizerItemAt(coordinate, itemId)
               && !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out _);
    }

    private static bool CanPlaceSavedSingleLineDrop(BlockStateStore stateStore, Vector2Int coordinate)
    {
        Vector2Int anchorCoordinate = default;
        bool hasInstallation = stateStore != null
            && stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out anchorCoordinate);
        if (hasInstallation
            && stateStore.TryGetInstallationStateReadOnly(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState)
            && IsConveyorBeltMapObject(InputOutputModule.ResolveItemDefinition(installationState.itemId)?.mapObject))
        {
            return false;
        }

        return CoordinateAcceptsInputAreaObject(coordinate)
               || !hasInstallation;
    }

    private static int ResolveSavedCenterCapacity(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        int itemId,
        int defaultCapacity)
    {
        int physicalCapacity;
        if (stateStore == null
            || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate)
            || !stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState))
        {
            physicalCapacity = Mathf.Max(1, defaultCapacity);
            return ItemDefinition.ResolveStackCapacity(
                InputOutputModule.ResolveItemDefinition(itemId),
                physicalCapacity);
        }

        ItemDefinition installedDefinition = InputOutputModule.ResolveItemDefinition(installationState.itemId);
        if (installedDefinition == null
            || !(installedDefinition.mapObject is InstallationObject installationObject)
            || (installationObject.MapFilter & InstallationMapFilter.ItemArea) == 0)
        {
            physicalCapacity = Mathf.Max(1, defaultCapacity);
        }
        else
        {
            physicalCapacity = installedDefinition.capacity > 0
                ? installedDefinition.capacity
                : Mathf.Max(1, defaultCapacity);
        }

        return ItemDefinition.ResolveStackCapacity(
            InputOutputModule.ResolveItemDefinition(itemId),
            physicalCapacity);
    }

    private static bool HasBlockingDropMapObject(Block dropBlock)
    {
        IMapObjectTarget mapObject = dropBlock != null ? dropBlock.MapObject : null;
        return mapObject != null
               && !IsOreMapObject(mapObject)
               && !IsBoxMapObject(mapObject)
               && !IsFreightCarMapObject(mapObject)
               && !IsConveyorBeltMapObject(mapObject);
    }

    private static bool IsOreMapObject(IMapObjectTarget mapObject)
    {
        return mapObject is ResourceInstance resource
               && resource.ResolvedHarvestMode == Resource.HarvestMode.Mining;
    }

    private static bool IsConveyorBeltMapObject(IMapObjectTarget mapObject)
    {
        return mapObject is ConveyorBelt;
    }

    private static bool IsBoxMapObject(IMapObjectTarget mapObject)
    {
        return mapObject is BoxObject
               || (mapObject != null && mapObject.TryGetComponent(out BoxObject _));
    }

    private static bool IsFreightCarMapObject(IMapObjectTarget mapObject)
    {
        return mapObject is FreightCar
               || (mapObject != null && mapObject.TryGetComponent(out FreightCar _));
    }

    private static bool TryGetBoxObject(Block pickupBlock, out BoxObject boxObject)
    {
        boxObject = null;
        if (pickupBlock == null || pickupBlock.MapObject == null)
        {
            return false;
        }

        boxObject = pickupBlock.MapObject as BoxObject;
        if (boxObject != null)
        {
            return true;
        }

        return pickupBlock.MapObject.TryGetComponent(out boxObject);
    }

    private bool TryGetFreightCarObject(Block block, Vector2Int coordinate, out FreightCar freightCar)
    {
        freightCar = null;
        if (TryResolveFreightCar(block != null ? block.MapObject : null, out freightCar))
        {
            return true;
        }

        freightCarCoordinateScratch.Clear();
        InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(coordinate, freightCarCoordinateScratch);
        for (int i = 0; i < freightCarCoordinateScratch.Count; i++)
        {
            InstallationObject candidate = freightCarCoordinateScratch[i];
            if (candidate == this || !TryResolveFreightCar(candidate, out freightCar))
            {
                continue;
            }

            freightCarCoordinateScratch.Clear();
            return true;
        }

        freightCarCoordinateScratch.Clear();
        return false;
    }

    private static bool TryResolveFreightCar(IMapObjectTarget mapObject, out FreightCar freightCar)
    {
        freightCar = null;
        if (mapObject == null)
        {
            return false;
        }

        freightCar = mapObject as FreightCar;
        if (freightCar != null)
        {
            return true;
        }

        if (mapObject.TryGetComponent(out freightCar) && freightCar != null)
        {
            return true;
        }

        freightCar = mapObject.GetComponentInChildren<FreightCar>(true);
        return freightCar != null;
    }

    public bool TryTakeHeldItemToBag(PlayerBag targetBag, int targetSlotIndex)
    {
        if (targetBag == null || targetSlotIndex < 0 || !CanTakeHeldItemFromSlotInternal())
        {
            return false;
        }

        int itemId = heldItemId;
        if (!targetBag.TryAddObject(targetSlotIndex, itemId, out _))
        {
            return false;
        }

        ClearHeldItem();
        dropRetryTimer = 0f;
        actionTurnTimer = 0f;
        waitingForDropRetry = false;
        state = RobotArmState.TurningToPickup;
        WakeRuntimeSleep();
        return true;
    }

    public bool TryClearHeldItemForPacking(int expectedItemId)
    {
        if (heldItemId < 0 || (expectedItemId >= 0 && heldItemId != expectedItemId))
        {
            return false;
        }

        ClearHeldItem();
        dropRetryTimer = 0f;
        actionTurnTimer = 0f;
        waitingForDropRetry = false;
        state = RobotArmState.TurningToPickup;
        WakeRuntimeSleep();
        return true;
    }

    private bool CanTakeHeldItemFromSlotInternal()
    {
        return heldItemId >= 0 && state == RobotArmState.WaitingForDrop;
    }

    private void EnsureRuntimeStateInitialized()
    {
        if (hasRuntimeStateInitialized)
        {
            return;
        }

        NormalizeRuntimeState();
        hasRuntimeStateInitialized = true;
    }

    private void NormalizeRuntimeState()
    {
        pickupTimer = Mathf.Max(0f, pickupTimer);
        dropRetryTimer = Mathf.Max(0f, dropRetryTimer);
        actionTurnTimer = Mathf.Max(0f, actionTurnTimer);

        if (!System.Enum.IsDefined(typeof(RobotArmState), state))
        {
            state = RobotArmState.WaitingForPickup;
        }

        if (heldItemId < 0)
        {
            waitingForDropRetry = false;
            dropRetryTimer = 0f;
            if (state == RobotArmState.WaitingForDrop
                || state == RobotArmState.WaitingBeforeDropPlace
                || state == RobotArmState.TurningToDrop)
            {
                state = RobotArmState.TurningToPickup;
            }

            return;
        }

        if (state == RobotArmState.WaitingForPickup
            || state == RobotArmState.WaitingBeforePickupTake
            || state == RobotArmState.WaitingAfterDropPlace
            || state == RobotArmState.TurningToPickup)
        {
            state = RobotArmState.TurningToDrop;
        }
    }

    private void ApplyStableBodyRotationForCurrentState()
    {
        EnsureBodyRotationCache();
        if (heldItemId >= 0
            && (state == RobotArmState.WaitingForDrop
                || state == RobotArmState.WaitingBeforeDropPlace))
        {
            SetBodyLocalRotation(GetOutputBodyLocalRotation());
            return;
        }

        if (heldItemId < 0
            && (state == RobotArmState.WaitingForPickup
                || state == RobotArmState.WaitingBeforePickupTake))
        {
            SetBodyLocalRotation(inputBodyLocalRotation);
        }
    }

    private float ResolvePoweredDeltaTime(float deltaTime)
    {
        if (!TryGetElectricOperationalPowerRequirement(out float wattsPerSecond))
        {
            lastElectricPowerSupplyRatio = 1f;
            return deltaTime;
        }

        deltaTime = Mathf.Max(0f, deltaTime);
        float requestedEnergy = wattsPerSecond * deltaTime;
        if (requestedEnergy <= 0.0001f)
        {
            lastElectricPowerSupplyRatio = 0f;
            return 0f;
        }

        if (!UtilityPole.TryConsumeElectricity(this, requestedEnergy, deltaTime, out float consumedEnergy))
        {
            lastElectricPowerSupplyRatio = 0f;
            return 0f;
        }

        lastElectricPowerSupplyRatio = Mathf.Clamp01(consumedEnergy / requestedEnergy);
        return deltaTime * lastElectricPowerSupplyRatio;
    }

    private void ApplyPoweredAnimatorSpeed()
    {
        Animator targetAnimator = ResolveAnimator();
        if (targetAnimator == null)
        {
            lastAppliedAnimatorSpeed = -1f;
            return;
        }

        float speed = Mathf.Clamp01(lastElectricPowerSupplyRatio);
        if (lastAppliedAnimatorSpeed >= 0f && Mathf.Abs(lastAppliedAnimatorSpeed - speed) <= AnimatorSpeedChangeEpsilon)
        {
            return;
        }

        targetAnimator.speed = speed;
        lastAppliedAnimatorSpeed = speed;
    }

    private void ResetAnimatorSpeed()
    {
        lastElectricPowerSupplyRatio = 1f;
        if (cachedAnimator != null)
        {
            cachedAnimator.speed = 1f;
            lastAppliedAnimatorSpeed = 1f;
        }
        else
        {
            lastAppliedAnimatorSpeed = -1f;
        }
    }

    private bool TryGetElectricOperationalPowerRequirement(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        float electricUseWatts = ItemDefinition.ResolveElectricUseWatts(installedDefinition);
        if (electricUseWatts <= 0.0001f)
        {
            return false;
        }

        wattsPerSecond = electricUseWatts;
        return wattsPerSecond > 0.0001f;
    }

    private static bool IsTurningState(RobotArmState robotArmState)
    {
        return robotArmState == RobotArmState.TurningToDrop
               || robotArmState == RobotArmState.TurningToPickup;
    }

    private static bool IsActiveTransferState(RobotArmState robotArmState)
    {
        return robotArmState == RobotArmState.WaitingBeforePickupTake
               || robotArmState == RobotArmState.WaitingAfterPickupTake
               || robotArmState == RobotArmState.TurningToDrop
               || robotArmState == RobotArmState.WaitingBeforeDropPlace
               || robotArmState == RobotArmState.WaitingAfterDropPlace
               || robotArmState == RobotArmState.TurningToPickup;
    }

    private void SetHeldItem(int itemId, Vector3 pickupWorldPosition)
    {
        CancelHeldItemVisualMove();
        heldItemId = itemId;
        RefreshHandItemVisual();
        if (handItem != null && handItem.gameObject.activeSelf)
        {
            BeginHeldItemMoveToHand(pickupWorldPosition);
        }
    }

    private void ClearHeldItem()
    {
        CancelHeldItemVisualMove();
        heldItemId = -1;
        RefreshHandItemVisual();
    }

    private void RefreshHandItemVisual()
    {
        if (handItem == null)
        {
            return;
        }

        if (heldItemId < 0)
        {
            handItem.SetCachedActive(false);
            return;
        }

        handItem.SetBatchedRendering(true);
        if (handItem.SetItem(heldItemId))
        {
            if (!handItem.IsMovingToTarget)
            {
                RestoreHandItemRestTransform();
            }

            handItem.SetCachedActive(true);
            handItem.MarkBatchedRenderDataDirty();
        }
        else
        {
            handItem.SetCachedActive(false);
        }
    }

    internal static void RefreshHeldItemVisualsForRendering()
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Held Item Visuals");
        // Animator runs at render frequency even when simulation ticks are skipped.
        // PortableItemRenderer calls this before reading the final frame matrices,
        // including arms whose body instancing is disabled.
        for (int i = 0; i < ActiveRobotArms.Count; i++)
        {
            RobotArm arm = ActiveRobotArms[i];
            if (arm != null && !arm.runtimeSleeping)
            {
                arm.RefreshHeldItemVisualIfNeeded();
            }
        }
    }

    private void RefreshHeldItemVisualIfNeeded()
    {
        if (heldItemId < 0 || handItem == null)
        {
            return;
        }

        if (!handItem.gameObject.activeSelf)
        {
            RefreshHandItemVisual();
            return;
        }

        handItem.RequestBatchedRenderDataRefresh();
    }

    private Vector3 GetHandWorldPosition()
    {
        if (handItem != null)
        {
            return GetHandRestWorldPosition();
        }

        if (body != null)
        {
            return body.position;
        }

        return transform.position;
    }

    private Vector3 GetBodyWorldPosition()
    {
        return body != null ? body.position : transform.position;
    }

    private void BeginHeldItemMoveToHand(Vector3 startWorldPosition)
    {
        if (heldItemId < 0 || handItem == null)
        {
            return;
        }

        EnsureHandItemRestTransformCache();
        Vector3 fixedStartWorldPosition = startWorldPosition;
        handItem.SetCachedActive(true);
        handItem.MoveTo(
            GetHandRestWorldPosition,
            0f,
            () => fixedStartWorldPosition,
            () =>
            {
                if (heldItemId < 0 || handItem == null)
                {
                    return;
                }

                RestoreHandItemRestTransform();
                handItem.SetBatchedRendering(true);
                handItem.SetCachedActive(true);
                handItem.MarkBatchedRenderDataDirty();
            },
            false,
            false,
            ItemMoveDuration);
    }

    private void CancelHeldItemVisualMove()
    {
        if (handItem == null)
        {
            return;
        }

        if (handItem.IsMovingToTarget)
        {
            handItem.CancelMove();
        }

        RestoreHandItemRestTransform();
    }

    private void EnsureHandItemRestTransformCache()
    {
        if (hasHandItemRestTransform || handItem == null)
        {
            return;
        }

        Transform handTransform = handItem.transform;
        handItemRestParent = handTransform.parent;
        handItemRestLocalPosition = handTransform.localPosition;
        handItemRestLocalRotation = handTransform.localRotation;
        handItemRestLocalScale = handTransform.localScale;
        hasHandItemRestTransform = true;
    }

    private void RestoreHandItemRestTransform()
    {
        if (handItem == null)
        {
            return;
        }

        EnsureHandItemRestTransformCache();
        Transform handTransform = handItem.transform;
        if (handItemRestParent != null && handTransform.parent != handItemRestParent)
        {
            handItem.SetCachedParent(handItemRestParent, false);
        }

        handTransform.localPosition = handItemRestLocalPosition;
        handTransform.localRotation = handItemRestLocalRotation;
        handTransform.localScale = handItemRestLocalScale;
        handItem.MarkBatchedRenderDataDirty();
    }

    private Vector3 GetHandRestWorldPosition()
    {
        if (handItem == null)
        {
            if (body != null)
            {
                return body.position;
            }

            return transform.position;
        }

        EnsureHandItemRestTransformCache();
        if (handItemRestParent != null)
        {
            return handItemRestParent.TransformPoint(handItemRestLocalPosition);
        }

        return handItemRestLocalPosition;
    }

    private bool HasPlacementRuntime()
    {
        return TryGetPlacementRuntime(out _, out _) && RuntimeOccupiedCoordinates != null && RuntimeOccupiedCoordinates.Count > 0;
    }

    private void EnsureBodyRotationCache()
    {
        if (hasInputBodyLocalRotation)
        {
            return;
        }

        inputBodyLocalRotation = body != null ? body.localRotation : Quaternion.identity;
        hasInputBodyLocalRotation = true;
    }

    private Quaternion GetOutputBodyLocalRotation()
    {
        return inputBodyLocalRotation * Quaternion.Euler(0f, 180f, 0f);
    }

    private bool RotateBodyToward(Quaternion targetLocalRotation, float deltaTime)
    {
        if (body == null)
        {
            return true;
        }

        Quaternion currentRotation = body.localRotation;
        float currentAngle = Quaternion.Angle(currentRotation, targetLocalRotation);
        if (currentAngle <= 0.1f)
        {
            return true;
        }

        Quaternion nextRotation = Quaternion.RotateTowards(
            currentRotation,
            targetLocalRotation,
            Mathf.Max(1f, bodyTurnSpeedDegreesPerSecond) * deltaTime);
        if (Quaternion.Angle(currentRotation, nextRotation) > 0.001f)
        {
            body.localRotation = nextRotation;
            MarkHandItemRenderDataDirty();
        }

        return Quaternion.Angle(nextRotation, targetLocalRotation) <= 0.1f;
    }

    private void SetBodyLocalRotation(Quaternion targetLocalRotation)
    {
        if (body != null && Quaternion.Angle(body.localRotation, targetLocalRotation) > 0.001f)
        {
            body.localRotation = targetLocalRotation;
            MarkHandItemRenderDataDirty();
        }
    }

    private void MarkHandItemRenderDataDirty()
    {
        if (heldItemId >= 0 && handItem != null)
        {
            handItem.MarkBatchedRenderDataDirty();
        }
    }

    private void BeginDropRetryDelay()
    {
        if (waitingForDropRetry)
        {
            return;
        }

        dropRetryTimer = dropRetryInterval;
        waitingForDropRetry = true;
    }

    private void PlayPickAnimation()
    {
        Animator targetAnimator = ResolveAnimator();
        if (targetAnimator == null)
        {
            return;
        }

        targetAnimator.ResetTrigger(PickTriggerHash);
        targetAnimator.SetTrigger(PickTriggerHash);
    }

    private void PlayDropAnimation()
    {
        Animator targetAnimator = ResolveAnimator();
        if (targetAnimator == null)
        {
            return;
        }

        targetAnimator.ResetTrigger(DropTriggerHash);
        targetAnimator.SetTrigger(DropTriggerHash);
    }

    private Animator ResolveAnimator()
    {
        if (cachedAnimator == null)
        {
            cachedAnimator = GetComponent<Animator>();
            if (cachedAnimator == null)
            {
                cachedAnimator = GetComponentInChildren<Animator>(true);
            }
        }

        return cachedAnimator;
    }

    private sealed class RobotArmInstancedRenderPart
    {
        public readonly MeshRenderer Renderer;
        public readonly MeshFilter MeshFilter;
        public readonly Transform Transform;
        public readonly Mesh Mesh;
        public readonly Material[] SharedMaterials;
        public readonly int MaterialCount;
        public readonly int Layer;
        public readonly ShadowCastingMode ShadowCastingMode;
        public readonly bool ReceiveShadows;
        public readonly bool OriginalRendererEnabled;

        public RobotArmInstancedRenderPart(
            MeshRenderer renderer,
            MeshFilter meshFilter,
            Transform transform,
            Material[] sharedMaterials,
            bool originalRendererEnabled)
        {
            Renderer = renderer;
            MeshFilter = meshFilter;
            Transform = transform;
            Mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            SharedMaterials = sharedMaterials;
            MaterialCount = Mesh != null && sharedMaterials != null
                ? Mathf.Min(sharedMaterials.Length, Mesh.subMeshCount)
                : 0;
            Layer = renderer != null ? renderer.gameObject.layer : 0;
            ShadowCastingMode = renderer != null ? renderer.shadowCastingMode : ShadowCastingMode.On;
            ReceiveShadows = renderer != null && renderer.receiveShadows;
            OriginalRendererEnabled = originalRendererEnabled;
        }

        public bool IsValid =>
            Renderer != null
            && MeshFilter != null
            && Mesh != null
            && Transform != null
            && SharedMaterials != null
            && MaterialCount > 0;
    }
}
