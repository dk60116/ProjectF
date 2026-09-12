using System;
using System.Collections.Generic;
using UnityEngine;
using static InputOutputModule;
using RobotArmState = RobotArm.RobotArmState;
using TransferState = RobotArm.TransferState;

// Entity identity; mutable simulation components live in RobotArmWorld's slot array.
public sealed partial class RobotArmInstance : IMapObjectTarget, IMapObjectSimulationIdentity
{
    internal readonly RobotArmWorld World;
    internal readonly int Index;
    internal readonly uint Generation;
    public readonly RobotArm Prototype;
    internal readonly RobotArmRenderTemplate Template;
    public readonly BlockStateStore.InstallationSaveState Placement;
    internal bool MarkersVisible;
    private ref RobotArmRuntimeState Data => ref World.GetState(Index, Generation);
    private ref int heldItemId => ref Data.heldItemId;
    private ref float pickupTimer => ref Data.pickupTimer;
    private ref float dropRetryTimer => ref Data.dropRetryTimer;
    private ref float actionTurnTimer => ref Data.actionTurnTimer;
    private ref bool waitingForDropRetry => ref Data.waitingForDropRetry;
    private ref RobotArmState state => ref Data.state;
    private ref bool hasRuntimeStateInitialized => ref Data.hasRuntimeStateInitialized;
    private ref bool runtimeSleeping => ref Data.runtimeSleeping;
    private ref bool runtimeWakePending => ref Data.runtimeWakePending;
    private ref float runtimeSleepCheckTimer => ref Data.runtimeSleepCheckTimer;
    private ref float lastElectricPowerSupplyRatio => ref Data.lastElectricPowerSupplyRatio;
    private ref PlannedTransferCommand plannedTransferCommand => ref Data.plannedTransferCommand;
    private ref bool stagedTickPlanned => ref Data.stagedTickPlanned;
    private ref bool plannedPickupAvailabilityChecked => ref Data.plannedPickupAvailabilityChecked;
    private ref bool plannedPickupAvailable => ref Data.plannedPickupAvailable;
    private ref bool plannedDropAvailabilityChecked => ref Data.plannedDropAvailabilityChecked;
    private ref bool plannedDropAvailable => ref Data.plannedDropAvailable;
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

    internal enum PlannedTransferCommand
    {
        None,
        Pickup,
        Drop
    }


    private const float ItemMoveDuration = PortableObject.MoveToDuration * 0.5f;
    private const float RuntimeSleepRecheckIntervalSeconds = 0.1f;
    private static ItemManager cachedFilterBitCountItemManager;
    private static int cachedFilterBitCountDefinitionCount = -1, cachedFilterBitCount = 1;
    private bool interactionCoordinateCacheValid;
    private long cachedInteractionPlacementSequence;
    private Vector2Int cachedPickupCoordinate, cachedDropCoordinate;
    private readonly List<InstallationObject> freightCarCoordinateScratch = new List<InstallationObject>(4);
    private Predicate<int> cachedPickupItemFilter;
    private Func<Vector3> cachedDropTransferStartProvider;
    private Vector3 cachedDropTransferStartWorldPosition;
    private Predicate<int> PickupItemFilter => cachedPickupItemFilter ?? (cachedPickupItemFilter = AcceptsPickupItem);
    private Func<Vector3> DropTransferStartProvider =>
        cachedDropTransferStartProvider
        ?? (cachedDropTransferStartProvider = GetCachedDropTransferStartWorldPosition);
    internal RobotArmInstance(RobotArmWorld world, int index, uint generation, RobotArm prototype,
        BlockStateStore.InstallationSaveState placement)
    { World = world; Index = index; Generation = generation; Prototype = prototype; Placement = placement; Template = world.GetTemplate(prototype); }
    public bool IsRuntimeActive => World != null && World.IsValid(Index, Generation);
    public bool IsTargetActive => IsRuntimeActive && World.isActiveAndEnabled;
    public MapObject SceneObject => null;
    public string ObjectName => Prototype.ObjectName;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public Vector3 WorldPosition => Placement.worldPosition;
    public Quaternion WorldRotation => Placement.worldRotation;
    public ItemDefinition BoundItemDefinition => InputOutputModule.ResolveItemDefinition(Placement.itemId);
    public int ResolveItemId() => Placement.itemId;
    public int ResolvedItemId => ResolveItemId();
    public int ID => ResolveItemId();
    public long SimulationId => Placement.placementSequence;
    public long RuntimePlacementSequence => SimulationId;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Placement.occupiedCoordinates;
    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements => Prototype.RectGridPlacements;
    public bool TryGetPlacementRuntime(out Vector2Int anchor, out int turns)
    { anchor = Placement.anchorCoordinate; turns = Placement.quarterTurns; return IsRuntimeActive; }
    private float pickupInterval => Prototype.PickupIntervalSeconds;
    private float dropRetryInterval => Prototype.DropRetryIntervalSeconds;
    private float actionTurnDelay => Prototype.ActionTurnDelaySeconds;
    private float bodyTurnSpeedDegreesPerSecond => Prototype.BodyTurnSpeedDegreesPerSecond;
    private float PickupIntervalSeconds => pickupInterval;
    private float TurnDurationSeconds => 180f / bodyTurnSpeedDegreesPerSecond;
    private Quaternion inputBodyLocalRotation => Template.BodyRotation;
    public bool HasHeldItem => heldItemId >= 0;
    public int HeldItemId => heldItemId;
    public bool CanTakeHeldItemFromSlot => CanTakeHeldItemFromSlotInternal();
    public Vector3 HeldItemWorldPosition => GetHandWorldPosition();
    public bool IsRuntimeSleeping => runtimeSleeping;
    public bool IsWorkingForItemLight => !runtimeSleeping;
    public bool IsItemFilterMaskInitialized => Placement.itemFilterMaskInitialized;
    public bool IsItemFilterEnabled(int itemId, int count)
    {
        if (itemId < 0) return false;
        if (!Placement.itemFilterMaskInitialized) return true;
        int word = itemId >> 6;
        return word >= Placement.itemFilterMaskWords.Count || (Placement.itemFilterMaskWords[word] & (1UL << (itemId & 63))) != 0;
    }
    public void SetItemFilterEnabled(int itemId, int count, bool enabled)
    {
        if (itemId < 0) return;
        var words = Placement.itemFilterMaskWords;
        int required = (Mathf.Max(count, itemId + 1) + 63) >> 6;
        while (words.Count < required) words.Add(ulong.MaxValue);
        Placement.itemFilterMaskInitialized = true;
        if (enabled) words[itemId >> 6] |= 1UL << (itemId & 63);
        else words[itemId >> 6] &= ~(1UL << (itemId & 63));
        WakeRuntimeSleep();
    }
    internal bool ReadyForTick => !runtimeSleeping || runtimeWakePending;
    public void Persist() { Placement.robotArmState = CaptureTransferState(); World.StateStore.UpdateInstallationState(Placement); }
    internal void PersistTransferState()
    {
        Placement.robotArmState = CaptureTransferState();
        Vector2Int key = Placement.hasStorageKey ? Placement.storageKey : Placement.anchorCoordinate;
        if (World.StateStore.TryGetInstallationStateReadOnly(key, out var saved) && saved.placementSequence == SimulationId)
            saved.robotArmState = Placement.robotArmState;
    }
    internal bool TryResolveEndpoints(out Vector2Int input, out Vector2Int output)
    { bool hasInput = TryResolvePickupCoordinate(out input); return TryResolveDropCoordinate(out output) && hasInput; }
    internal void WakeRuntimeSleep()
    {
        // Notifications only invalidate; no transfer query runs inside another producer's mutation.
        if (!IsRuntimeActive || runtimeWakePending) return;
        runtimeWakePending = true;
        runtimeSleepCheckTimer = 0f;
    }
    private void SetRuntimeSleeping(bool sleeping, bool force = false)
    { runtimeSleeping = sleeping; runtimeSleepCheckTimer = 0f; if (sleeping) ApplyRuntimeSleepPose(); }
    private TerrainGenerator ResolveTerrainGenerator() => World.Terrain;
    private BlockStateStore ResolveBlockStateStore() => World.StateStore;
    private Vector3 GetBodyWorldPosition() => Template.BodyWorld(this);
    private Vector3 GetHandWorldPosition() => Template.HandWorld(this);
    private Vector3 GetHandRestWorldPosition() => GetHandWorldPosition();
    private Vector3 GetCachedDropTransferStartWorldPosition() => cachedDropTransferStartWorldPosition;
    private Vector3 GetDropReferencePosition(Block block, Vector2Int coordinate) => GetHandWorldPosition();
    private void SetHeldItem(int id, Vector3 position)
    { heldItemId = id; Data.ItemMoveStart = position; Data.ItemMoveElapsed = 0f; }
    private void ClearHeldItem() { heldItemId = -1; }
    internal Vector3 ItemPresentationPosition(Vector3 handPosition) => Vector3.Lerp(Data.ItemMoveStart, handPosition,
        ItemMoveDuration > 0f ? Mathf.Clamp01(Data.ItemMoveElapsed / ItemMoveDuration) : 1f);
    internal void AdvanceSleepingPresentation(float dt)
    { if (Data.AnimationTime < 1f) AdvanceAnimation(dt * Mathf.Clamp01(lastElectricPowerSupplyRatio)); }
    internal Quaternion BodyRotation => Data.BodyRotation;
    internal float AnimationTime => Data.AnimationTime;
    internal int AnimationKind => Data.AnimationKind;
    private void SetBodyLocalRotation(Quaternion rotation) { Data.BodyRotation = rotation; }
    private bool RotateBodyToward(Quaternion target, float dt)
    {
        Data.BodyRotation = Quaternion.RotateTowards(Data.BodyRotation, target, bodyTurnSpeedDegreesPerSecond * dt);
        return Quaternion.Angle(Data.BodyRotation, target) <= 0.1f;
    }
    private void AdvanceAnimation(float dt)
    { Data.AnimationTime = Mathf.Min(1f, Data.AnimationTime + dt); Data.ItemMoveElapsed = Mathf.Min(ItemMoveDuration, Data.ItemMoveElapsed + dt); }
    private void PlayPickAnimation() { Data.AnimationKind = 1; Data.AnimationTime = 0f; }
    private void PlayDropAnimation() { Data.AnimationKind = 2; Data.AnimationTime = 0f; }
    public Bounds PresentationBounds => new Bounds(WorldPosition + Vector3.up * 0.6f, new Vector3(1f, 1.5f, 1f));
    internal Bounds CullBounds => new Bounds(WorldPosition + Vector3.up * 0.6f, Vector3.one * Template.CullDiameter);

    public bool TryGetElectricPowerRequirement(out float wattsPerSecond)
    {
        return TryGetElectricOperationalPowerRequirement(out wattsPerSecond);
    }

    public bool TryGetElectricPowerDemand(out float wattsPerSecond)
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

    public void GetObjectInfoStatus(out string statusText, out bool isWorking)
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

            if (runtimeSleeping)
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

        if (!runtimeSleeping)
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

    public TransferState CaptureTransferState()
    {

        EnsureRuntimeStateInitialized();
        return new TransferState
        {
            heldItemId = heldItemId,
            state = state,
            pickupTimer = Mathf.Max(0f, pickupTimer),
            dropRetryTimer = Mathf.Max(0f, dropRetryTimer),
            actionTurnTimer = Mathf.Max(0f, actionTurnTimer),
            turnTimer = IsTurningState(state) ? Quaternion.Angle(BodyRotation,
                state == RobotArmState.TurningToDrop ? GetOutputBodyLocalRotation() : inputBodyLocalRotation) / bodyTurnSpeedDegreesPerSecond : 0f,
            waitingForDropRetry = waitingForDropRetry
        };
    }

    public void ApplyTransferState(TransferState persistentState)
    {

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
            Data.AnimationKind = 0;
            Data.AnimationTime = 1f;
            Data.ItemMoveElapsed = ItemMoveDuration;

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
        if (state == RobotArmState.WaitingAfterDropPlace) SetBodyLocalRotation(GetOutputBodyLocalRotation());
        if (IsTurningState(state))
        {
            float progress = 1f - Mathf.Clamp01(persistentState.turnTimer / TurnDurationSeconds);
            SetBodyLocalRotation(state == RobotArmState.TurningToDrop
                ? Quaternion.Slerp(inputBodyLocalRotation, GetOutputBodyLocalRotation(), progress)
                : Quaternion.Slerp(GetOutputBodyLocalRotation(), inputBodyLocalRotation, progress));
        }
        Data.AnimationTime = 1f;
        Data.ItemMoveElapsed = ItemMoveDuration;
        Data.ItemMoveStart = GetHandWorldPosition();


        RefreshRuntimeSleepState(true);
    }

    public void ClearHeldItemAndTransferState()
    {
        ApplyTransferState(null);
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }

    public void PlanManagedUpdateTick(float deltaTime)
    {
        plannedTransferCommand = PlannedTransferCommand.None;
        stagedTickPlanned = true;
        plannedPickupAvailabilityChecked = false;
        plannedDropAvailabilityChecked = false;
        runtimeWakePending = false;

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
            AdvanceAnimation(deltaTime);
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
            case RobotArmState.WaitingAfterDropPlace:
                TickWaitAfterDropPlace(deltaTime);
                break;
            case RobotArmState.TurningToPickup:
                TickTurnToPickup(deltaTime);
                break;
        }
    }

    public void ApplyManagedUpdateTick()
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

    private bool RefreshRuntimeSleepState(bool force = false)
    {
        bool shouldSleep = ShouldRuntimeSleep();
        if (runtimeSleeping && !shouldSleep)
        { pickupTimer = 0f; dropRetryTimer = 0f; waitingForDropRetry = false; }
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
            || !IsRuntimeActive
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

        if (TryResolvePickupCoordinate(out Vector2Int pickupCoordinate)
            && IsMovingFreightCarAtCoordinate(pickupCoordinate))
        {
            // The consist can finish braking without crossing another grid cell or
            // changing its cargo. Keep polling until it stops so the arm can observe
            // the transition from an unavailable moving car to a valid pickup source.
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

        // Speed can reach zero without crossing a grid cell or changing cargo.
        // Keep the normal drop retry active until that moving target stops.
        if (IsMovingFreightCarAtCoordinate(dropCoordinate))
        {
            return false;
        }

        return !CanPlaceHeldItemForCurrentPlan();
    }

    private bool IsMovingFreightCarAtCoordinate(Vector2Int coordinate)
    {
        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (terrainGenerator == null)
        {
            return false;
        }

        return TryResolveInteractionFreightCar(
                   terrainGenerator,
                   coordinate,
                   out FreightCar freightCar)
               && freightCar.IsConsistMoving();
    }

    private void ApplyRuntimeSleepPose()
    {

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
            // Commit in this tick's ordered apply phase. Waiting for the animation
            // first lets the next belt item take this gap before the transfer.
            plannedTransferCommand = PlannedTransferCommand.Drop;
            return;
        }

        BeginDropRetryDelay();
    }

    private void ApplyPlannedDrop()
    {
        if (state != RobotArmState.WaitingForDrop || heldItemId < 0)
        {
            return;
        }

        if (TryPlaceHeldItem())
        {
            dropRetryTimer = 0f;
            waitingForDropRetry = false;
            PlayDropAnimation();
            ClearHeldItem();
            state = RobotArmState.WaitingAfterDropPlace;
            // Preserve the former pre/post-drop action budget after committing.
            actionTurnTimer = actionTurnDelay * 2f;
            return;
        }

        BeginDropRetryDelay();
        // Another arm may have claimed the same gap earlier in the apply order.
        // Keep the held item and idle pose, and sleep if the destination is full.
        RefreshRuntimeSleepState();
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

        bool hasLoadedPickupBlock = TryResolvePickupInteractionTargets(
            terrainGenerator,
            pickupCoordinate,
            out pickupBlock,
            out boxObject,
            out freightCar);

        Vector3 conveyorSelectionReferenceWorldPosition = GetBodyWorldPosition();
        float bestDistanceSqr = float.MaxValue;

        if (hasLoadedPickupBlock
            && pickupBlock.TryGetClosestFloorObjectWorldPosition(referenceWorldPosition, PickupItemFilter, out Vector3 candidateWorldPosition))
        {
            TryChoosePickupSource(RobotArmPickupSource.Floor, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
        }

        if (hasLoadedPickupBlock && boxObject != null)
        {
            if (boxObject.TryGetContainedObjectTopItemId(out int containedItemId)
                && boxObject.CanTakeContainedObject()
                && AcceptsPickupItem(containedItemId)
                && boxObject.TryGetContainedObjectTopWorldPosition(out candidateWorldPosition))
            {
                TryChoosePickupSource(RobotArmPickupSource.Box, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
            }
        }

        if (hasLoadedPickupBlock && freightCar != null)
        {
            if (freightCar.IsConsistMoving())
            {
                return false;
            }

            if (freightCar.TryGetTopItem(referenceWorldPosition, PickupItemFilter, out _, out candidateWorldPosition))
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
        bool hasLoadedDropBlock = TryResolveDropInteractionTargets(
            terrainGenerator,
            dropCoordinate,
            out dropBlock,
            out BoxObject boxObject,
            out FreightCar freightCar);

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
        cachedDropTransferStartWorldPosition = dropStartWorldPosition;
        Func<Vector3> dropStartProvider = DropTransferStartProvider;
        if (freightCar != null)
        {
            if (freightCar.IsConsistMoving())
            {
                return false;
            }

            if (freightCar.TryAddItemStack(
                    itemId,
                    1,
                    dropStartWorldPosition,
                    dropStartProvider,
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

        if (IsDropMapObjectBlocking(dropBlock))
        {
            return false;
        }

        if (boxObject != null
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
                dropStartProvider,
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
        bool hasLoadedDropBlock = TryResolveDropInteractionTargets(
            terrainGenerator,
            dropCoordinate,
            out dropBlock,
            out BoxObject boxObject,
            out FreightCar freightCar);
        if (ShouldUseSavedDropCoordinate(terrainGenerator, dropCoordinate, dropBlock))
        {
            return TryPlaceHeldItemInSavedCoordinate(dropCoordinate, heldItemId, false);
        }

        if (!hasLoadedDropBlock)
        {
            return false;
        }

        return CanPlaceHeldItem(dropBlock, dropCoordinate, boxObject, freightCar);
    }

    private bool CanPlaceHeldItem(
        Block dropBlock,
        Vector2Int dropCoordinate,
        BoxObject boxObject,
        FreightCar freightCar)
    {
        int itemId = heldItemId;
        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        Vector3 dropReferenceWorldPosition = GetDropReferencePosition(dropBlock, dropCoordinate);
        if (freightCar != null)
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

        if (IsDropMapObjectBlocking(dropBlock))
        {
            return false;
        }

        if (boxObject != null && boxObject.CanPutOneContainedObject(itemId))
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
        InvalidateInteractionTargetCaches();
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
            if (!Prototype.TryGetRectGridPlacementCoordinate(Prototype, anchor, quarterTurns, placement, out Vector2Int coordinate))
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

    private static bool IsOreMapObject(IMapObjectTarget mapObject)
    {
        return mapObject is ResourceInstance resource
               && resource.ResolvedHarvestMode == Resource.HarvestMode.Mining;
    }

    private static bool IsConveyorBeltMapObject(IMapObjectTarget mapObject)
    {
        return mapObject is ConveyorBelt;
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
        Persist();
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

        if (state == RobotArmState.WaitingBeforeDropPlace)
        {
            state = RobotArmState.WaitingForDrop;
            actionTurnTimer = 0f;
        }

        if (heldItemId < 0)
        {
            waitingForDropRetry = false;
            dropRetryTimer = 0f;
            if (state == RobotArmState.WaitingForDrop
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

        if (heldItemId >= 0
            && state == RobotArmState.WaitingForDrop)
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

        if (!UtilityPole.TryConsumeRobotArmElectricity(
                this,
                wattsPerSecond,
                requestedEnergy,
                out float consumedEnergy))
        {
            lastElectricPowerSupplyRatio = 0f;
            return 0f;
        }

        lastElectricPowerSupplyRatio = Mathf.Clamp01(consumedEnergy / requestedEnergy);
        return deltaTime * lastElectricPowerSupplyRatio;
    }

    private bool TryGetElectricOperationalPowerRequirement(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        ItemDefinition installedDefinition = BoundItemDefinition;
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
               || robotArmState == RobotArmState.WaitingAfterDropPlace
               || robotArmState == RobotArmState.TurningToPickup;
    }

    private bool HasPlacementRuntime()
    {
        return TryGetPlacementRuntime(out _, out _) && RuntimeOccupiedCoordinates != null && RuntimeOccupiedCoordinates.Count > 0;
    }

    private Quaternion GetOutputBodyLocalRotation()
    {
        return inputBodyLocalRotation * Quaternion.Euler(0f, 180f, 0f);
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
}
