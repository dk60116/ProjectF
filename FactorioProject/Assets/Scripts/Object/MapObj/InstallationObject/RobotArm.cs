using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class RobotArm : InstallationObject, IMapObjectUpdateTick
{
    private static readonly int PickTriggerHash = Animator.StringToHash("tPick");
    private static readonly int DropTriggerHash = Animator.StringToHash("tDrop");
    private static readonly List<RobotArm> ActiveRobotArms = new List<RobotArm>();
    private const float ItemMoveDuration = PortableObject.MoveToDuration * 0.5f;

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
        Conveyor,
        InputArea
    }

    [System.Serializable]
    public sealed class PersistentState
    {
        public int heldItemId = -1;
        public RobotArmState state = RobotArmState.WaitingForPickup;
        public float pickupTimer;
        public float dropRetryTimer;
        public float actionTurnTimer;
        public float turnTimer;
        public bool waitingForDropRetry;

        public PersistentState Clone()
        {
            return new PersistentState
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
    private float pickupTimer;
    private float dropRetryTimer;
    private float actionTurnTimer;
    private bool waitingForDropRetry;
    private RobotArmState state;
    private Quaternion inputBodyLocalRotation;
    private bool hasInputBodyLocalRotation;
    private bool hasRuntimeStateInitialized;
    private bool runtimeSleeping;
    private bool updateTickRegistered;
    private Animator cachedAnimator;
    private Renderer[] sleepAwakeRenderers;
    private MaterialPropertyBlock sleepAwakePropertyBlock;
    private bool sleepAwakeVisualStateInitialized;
    private bool lastSleepAwakeDarkTint;
    private RobotArmRenderBatcher renderBatcher;
    private RobotArmInstancedRenderPart[] instancedRenderParts;
    private bool instancedRenderingActive;
    private Transform handItemRestParent;
    private Vector3 handItemRestLocalPosition;
    private Quaternion handItemRestLocalRotation;
    private Vector3 handItemRestLocalScale;
    private bool hasHandItemRestTransform;

    public bool HasHeldItem => heldItemId >= 0;
    public int HeldItemId => heldItemId;
    public bool CanTakeHeldItemFromSlot => CanTakeHeldItemFromSlotInternal();
    public Vector3 HeldItemWorldPosition => GetHandWorldPosition();
    public bool IsRuntimeSleeping => runtimeSleeping;
    public float PickupIntervalSeconds => Mathf.Max(0.01f, pickupInterval);
    public float DropRetryIntervalSeconds => Mathf.Max(0.01f, dropRetryInterval);
    public float ActionTurnDelaySeconds => Mathf.Max(0f, actionTurnDelay);
    public float BackgroundTurnDurationSeconds => 180f / Mathf.Max(1f, bodyTurnSpeedDegreesPerSecond);

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
        waitingForDropRetry = false;
        state = RobotArmState.WaitingForPickup;
        hasRuntimeStateInitialized = false;
        runtimeSleeping = false;
        UnregisterInstancedRendering();
        SetUpdateTickRegistered(false);
        SetBodyLocalRotation(inputBodyLocalRotation);
        RefreshSleepAwakeVisual(true);
        base.PrepareForPool();
    }

    public PersistentState CapturePersistentState()
    {
        EnsureBodyRotationCache();
        EnsureRuntimeStateInitialized();
        return new PersistentState
        {
            heldItemId = heldItemId,
            state = state,
            pickupTimer = Mathf.Max(0f, pickupTimer),
            dropRetryTimer = Mathf.Max(0f, dropRetryTimer),
            actionTurnTimer = Mathf.Max(0f, actionTurnTimer),
            turnTimer = IsTurningState(state) ? BackgroundTurnDurationSeconds : 0f,
            waitingForDropRetry = waitingForDropRetry
        };
    }

    public void ApplyPersistentState(PersistentState persistentState)
    {
        EnsureBodyRotationCache();
        if (persistentState == null)
        {
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

        if (heldItemId < 0 && state == RobotArmState.WaitingForPickup)
        {
            SetBodyLocalRotation(inputBodyLocalRotation);
        }

        RefreshHandItemVisual();
        RefreshRuntimeSleepState(true);
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        EnsureBodyRotationCache();
        RefreshHeldItemVisualIfNeeded();
        if (RefreshRuntimeSleepState())
        {
            return;
        }

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

    public static void WakeAroundCoordinate(Vector2Int coordinate)
    {
        for (int i = ActiveRobotArms.Count - 1; i >= 0; i--)
        {
            RobotArm robotArm = ActiveRobotArms[i];
            if (robotArm == null)
            {
                ActiveRobotArms.RemoveAt(i);
                continue;
            }

            if (!robotArm.isActiveAndEnabled || !robotArm.IsCoordinateInsideRuntimeSleepWakeRange(coordinate))
            {
                continue;
            }

            robotArm.WakeRuntimeSleep();
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

    private static void RegisterActiveRobotArm(RobotArm robotArm)
    {
        if (robotArm == null || ActiveRobotArms.Contains(robotArm))
        {
            return;
        }

        ActiveRobotArms.Add(robotArm);
    }

    private static void UnregisterActiveRobotArm(RobotArm robotArm)
    {
        if (robotArm == null)
        {
            return;
        }

        ActiveRobotArms.Remove(robotArm);
    }

    private bool RefreshRuntimeSleepState(bool force = false)
    {
        bool shouldSleep = ShouldRuntimeSleep();
        SetRuntimeSleeping(shouldSleep, force);
        return shouldSleep;
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

        return !HasNearbyRuntimeInteractionTarget();
    }

    private bool ShouldRuntimeSleepWithHeldItem()
    {
        if (state != RobotArmState.WaitingForDrop)
        {
            return false;
        }

        if (!TryResolveLoadedDropBlock(out Vector2Int dropCoordinate, out Block dropBlock))
        {
            return false;
        }

        if (IsConveyorBeltMapObject(dropBlock.MapObject))
        {
            return false;
        }

        return !CanPlaceHeldItem(dropBlock, dropCoordinate);
    }

    private bool HasNearbyRuntimeInteractionTarget()
    {
        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (terrainGenerator == null || RuntimeOccupiedCoordinates == null || RuntimeOccupiedCoordinates.Count <= 0)
        {
            return true;
        }

        for (int occupiedIndex = 0; occupiedIndex < RuntimeOccupiedCoordinates.Count; occupiedIndex++)
        {
            Vector2Int occupiedCoordinate = RuntimeOccupiedCoordinates[occupiedIndex];
            for (int x = occupiedCoordinate.x - 1; x <= occupiedCoordinate.x + 1; x++)
            {
                for (int y = occupiedCoordinate.y - 1; y <= occupiedCoordinate.y + 1; y++)
                {
                    Vector2Int coordinate = new Vector2Int(x, y);
                    if (IsOwnRuntimeCoordinate(coordinate))
                    {
                        continue;
                    }

                    if (!terrainGenerator.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                    {
                        continue;
                    }

                    if (BlockHasRuntimeInteractionTarget(block, coordinate))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool BlockHasRuntimeInteractionTarget(Block block, Vector2Int coordinate)
    {
        if (block == null)
        {
            return false;
        }

        MapObject blockObject = block.MapObject;
        if (blockObject != null && blockObject != this)
        {
            return true;
        }

        if (CoordinateAcceptsInputAreaObject(coordinate) || block.GetInputAreaCenterItemId() >= 0)
        {
            return true;
        }

        Vector3 referenceWorldPosition = transform.position;
        return block.TryGetClosestFloorObjectWorldPosition(referenceWorldPosition, out _)
               || block.TryGetClosestConveyorObjectWorldPosition(referenceWorldPosition, out _);
    }

    private bool IsOwnRuntimeCoordinate(Vector2Int coordinate)
    {
        if (RuntimeOccupiedCoordinates == null)
        {
            return false;
        }

        for (int i = 0; i < RuntimeOccupiedCoordinates.Count; i++)
        {
            if (RuntimeOccupiedCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsCoordinateInsideRuntimeSleepWakeRange(Vector2Int coordinate)
    {
        if (RuntimeOccupiedCoordinates == null || RuntimeOccupiedCoordinates.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < RuntimeOccupiedCoordinates.Count; i++)
        {
            Vector2Int occupiedCoordinate = RuntimeOccupiedCoordinates[i];
            if (Mathf.Abs(coordinate.x - occupiedCoordinate.x) <= 1
                && Mathf.Abs(coordinate.y - occupiedCoordinate.y) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    private void WakeRuntimeSleep()
    {
        pickupTimer = 0f;
        dropRetryTimer = 0f;
        waitingForDropRetry = false;
        SetRuntimeSleeping(false, true);
    }

    private void SetRuntimeSleeping(bool sleeping, bool force = false)
    {
        if (!force && runtimeSleeping == sleeping)
        {
            RefreshSleepAwakeVisual();
            return;
        }

        runtimeSleeping = sleeping;
        SetUpdateTickRegistered(!runtimeSleeping && isActiveAndEnabled);
        handItem?.SetSleepAwakeSleeping(runtimeSleeping);
        RefreshSleepAwakeVisual(true);
    }

    private void SetUpdateTickRegistered(bool registered)
    {
        if (updateTickRegistered == registered)
        {
            return;
        }

        updateTickRegistered = registered;
        if (registered)
        {
            MapObjectTickManager.RegisterUpdateTick(this);
        }
        else
        {
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

    internal void AppendInstancedRenderData(VirtualRenderBatchCollection batches, float batchCellSize)
    {
        if (!instancedRenderingActive || batches == null || !isActiveAndEnabled)
        {
            return;
        }

        EnsureInstancedRenderParts();
        if (instancedRenderParts == null || instancedRenderParts.Length <= 0)
        {
            return;
        }

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

            Mesh mesh = part.MeshFilter.sharedMesh;
            Material[] materials = part.SharedMaterials;
            int submeshCount = mesh.subMeshCount;
            int materialCount = Mathf.Min(materials.Length, submeshCount);
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                if (!material.enableInstancing)
                {
                    material.enableInstancing = true;
                }

                VirtualRenderBatchKey key = new VirtualRenderBatchKey(
                    mesh,
                    material,
                    part.Renderer.gameObject.layer,
                    materialIndex,
                    part.Renderer.shadowCastingMode,
                    part.Renderer.receiveShadows,
                    false,
                    0,
                    useSleepTint,
                    false,
                    default,
                    0,
                    cellX,
                    cellZ);
                batches.AddMatrix(key, part.Transform.localToWorldMatrix);
            }
        }
    }

    private void EnsureInstancedRenderingRegistered()
    {
        if (!Application.isPlaying || !useInstancedRendering)
        {
            SetInstancedRenderingActive(false);
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

    private void UnregisterInstancedRendering()
    {
        if (renderBatcher != null)
        {
            renderBatcher.Unregister(this);
            renderBatcher = null;
        }

        SetInstancedRenderingActive(false);
    }

    private void SetInstancedRenderingActive(bool active)
    {
        if (instancedRenderingActive == active)
        {
            return;
        }

        EnsureInstancedRenderParts();
        instancedRenderingActive = active;
        SetInstancedSourceRenderersEnabled(!instancedRenderingActive);
        RefreshSleepAwakeVisual(true);
    }

    private void SetInstancedSourceRenderersEnabled(bool enabled)
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

            part.Renderer.enabled = enabled && part.OriginalRendererEnabled;
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

        RotateBodyToward(inputBodyLocalRotation, deltaTime);
        if (pickupTimer > 0f)
        {
            pickupTimer -= deltaTime;
            return;
        }

        if (CanPickupOneItem())
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

        if (actionTurnTimer > 0f)
        {
            actionTurnTimer -= deltaTime;
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

        if (actionTurnTimer > 0f)
        {
            actionTurnTimer -= deltaTime;
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

        if (dropRetryTimer > 0f)
        {
            dropRetryTimer -= deltaTime;
            return;
        }

        waitingForDropRetry = false;
        if (CanPlaceHeldItem())
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

        if (actionTurnTimer > 0f)
        {
            actionTurnTimer -= deltaTime;
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

        if (actionTurnTimer > 0f)
        {
            actionTurnTimer -= deltaTime;
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

    private bool TryPickupOneItem(out int pickedItemId, out Vector3 pickupWorldPosition)
    {
        pickedItemId = -1;
        pickupWorldPosition = GetHandRestWorldPosition();
        if (!TryResolvePickupCandidate(
                out Block pickupBlock,
                out BoxObject boxObject,
                out RobotArmPickupSource pickupSource,
                out Vector3 referenceWorldPosition,
                out pickupWorldPosition))
        {
            return false;
        }

        return pickupSource switch
        {
            RobotArmPickupSource.Floor => pickupBlock.TryTakeClosestFloorObject(referenceWorldPosition, AcceptsPickupItem, out pickedItemId),
            RobotArmPickupSource.Box => boxObject != null && boxObject.TryTakeOneContainedObject(AcceptsPickupItem, out pickedItemId),
            RobotArmPickupSource.Conveyor => pickupBlock.TryTakeOneConveyorObject(referenceWorldPosition, AcceptsPickupItem, out pickedItemId),
            RobotArmPickupSource.InputArea => TryTakeFilteredInputAreaItem(pickupBlock, out pickedItemId),
            _ => false
        };
    }

    private bool CanPickupOneItem()
    {
        return TryResolvePickupCandidate(out _, out _, out _, out _, out _);
    }

    private bool TryResolvePickupCandidate(
        out Block pickupBlock,
        out BoxObject boxObject,
        out RobotArmPickupSource pickupSource,
        out Vector3 referenceWorldPosition,
        out Vector3 pickupWorldPosition)
    {
        pickupBlock = null;
        boxObject = null;
        pickupSource = RobotArmPickupSource.None;
        referenceWorldPosition = GetHandWorldPosition();
        pickupWorldPosition = referenceWorldPosition;

        if (!TryResolvePickupCoordinate(out Vector2Int pickupCoordinate))
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (terrainGenerator == null || !terrainGenerator.TryGetLoadedBlock(pickupCoordinate, out pickupBlock) || pickupBlock == null)
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;

        if (pickupBlock.TryGetClosestFloorObjectWorldPosition(referenceWorldPosition, AcceptsPickupItem, out Vector3 candidateWorldPosition))
        {
            TryChoosePickupSource(RobotArmPickupSource.Floor, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
        }

        if (TryGetBoxObject(pickupBlock, out BoxObject candidateBoxObject))
        {
            boxObject = candidateBoxObject;
            if (candidateBoxObject.TryGetContainedObjectTopItemId(out int containedItemId)
                && AcceptsPickupItem(containedItemId)
                && candidateBoxObject.TryGetContainedObjectTopWorldPosition(out candidateWorldPosition))
            {
                TryChoosePickupSource(RobotArmPickupSource.Box, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
            }
        }

        if (pickupBlock.TryGetClosestConveyorObjectWorldPosition(referenceWorldPosition, AcceptsPickupItem, out candidateWorldPosition))
        {
            TryChoosePickupSource(RobotArmPickupSource.Conveyor, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
        }

        int inputAreaItemId = pickupBlock.GetInputAreaCenterItemId();
        if (AcceptsPickupItem(inputAreaItemId)
            && pickupBlock.TryGetInputAreaCenterTopWorldPosition(-1, out candidateWorldPosition))
        {
            TryChoosePickupSource(RobotArmPickupSource.InputArea, candidateWorldPosition, referenceWorldPosition, ref pickupSource, ref bestDistanceSqr, ref pickupWorldPosition);
        }

        return pickupSource != RobotArmPickupSource.None;
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

        int maxItemId = fallbackItemId;
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

        return Mathf.Max(1, maxItemId + 1);
    }

    private bool TryPlaceHeldItem()
    {
        if (heldItemId < 0 || !TryResolveLoadedDropBlock(out Vector2Int dropCoordinate, out Block dropBlock))
        {
            return false;
        }

        if (HasBlockingDropMapObject(dropBlock))
        {
            return false;
        }

        int itemId = heldItemId;
        Vector3 dropReferenceWorldPosition = GetDropReferencePosition(dropBlock, dropCoordinate);
        Vector3 dropStartWorldPosition = GetHandRestWorldPosition();
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
        if (heldItemId < 0 || !TryResolveLoadedDropBlock(out Vector2Int dropCoordinate, out Block dropBlock))
        {
            return false;
        }

        return CanPlaceHeldItem(dropBlock, dropCoordinate);
    }

    private bool CanPlaceHeldItem(Block dropBlock, Vector2Int dropCoordinate)
    {
        if (HasBlockingDropMapObject(dropBlock))
        {
            return false;
        }

        int itemId = heldItemId;
        Vector3 dropReferenceWorldPosition = GetDropReferencePosition(dropBlock, dropCoordinate);
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

    private bool TryResolveLoadedDropBlock(out Vector2Int dropCoordinate, out Block dropBlock)
    {
        dropBlock = null;
        if (!TryResolveDropCoordinate(out dropCoordinate))
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        return terrainGenerator != null
               && terrainGenerator.TryGetLoadedBlock(dropCoordinate, out dropBlock)
               && dropBlock != null;
    }

    private bool TryResolvePickupCoordinate(out Vector2Int pickupCoordinate)
    {
        return TryResolveInteractionCoordinate(true, out pickupCoordinate);
    }

    private bool TryResolveDropCoordinate(out Vector2Int dropCoordinate)
    {
        return TryResolveInteractionCoordinate(false, out dropCoordinate);
    }

    private bool TryResolveInteractionCoordinate(bool inputSide, out Vector2Int interactionCoordinate)
    {
        interactionCoordinate = default;

        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _) || RuntimeOccupiedCoordinates == null || RuntimeOccupiedCoordinates.Count == 0)
        {
            return false;
        }

        if (!TryResolveFlowDirection(out Vector2Int flowDirection))
        {
            return false;
        }

        Vector2Int edgeCoordinate = anchorCoordinate;
        int bestProjection = inputSide ? int.MaxValue : int.MinValue;
        for (int i = 0; i < RuntimeOccupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = RuntimeOccupiedCoordinates[i];
            int projection = coordinate.x * flowDirection.x + coordinate.y * flowDirection.y;
            bool betterProjection = inputSide ? projection < bestProjection : projection > bestProjection;
            if (betterProjection)
            {
                bestProjection = projection;
                edgeCoordinate = coordinate;
            }
        }

        interactionCoordinate = inputSide ? edgeCoordinate - flowDirection : edgeCoordinate + flowDirection;
        return true;
    }

    private bool TryResolveFlowDirection(out Vector2Int flowDirection)
    {
        Vector3 forward = transform.rotation * Vector3.forward;
        Vector2 flatForward = new Vector2(forward.x, forward.z);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flowDirection = Vector2Int.up;
            return true;
        }

        flatForward.Normalize();
        flowDirection = Mathf.Abs(flatForward.x) >= Mathf.Abs(flatForward.y)
            ? new Vector2Int(flatForward.x >= 0f ? 1 : -1, 0)
            : new Vector2Int(0, flatForward.y >= 0f ? 1 : -1);
        return true;
    }

    private TerrainGenerator ResolveTerrainGenerator()
    {
        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = TerrainGenerator.ResolveActive();
        }

        return cachedTerrainGenerator;
    }

    private static Vector3 GetPickupReferencePosition(Block pickupBlock, Vector2Int pickupCoordinate)
    {
        if (pickupBlock != null)
        {
            return pickupBlock.transform.position;
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

    private static bool CoordinateAcceptsInputAreaObject(Vector2Int coordinate)
    {
        return InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate);
    }

    private static bool CanPlaceSingleLineDrop(Block dropBlock, Vector2Int coordinate)
    {
        if (dropBlock == null)
        {
            return false;
        }

        return CoordinateAcceptsInputAreaObject(coordinate) || dropBlock.MapObject == null;
    }

    private static bool HasBlockingDropMapObject(Block dropBlock)
    {
        MapObject mapObject = dropBlock != null ? dropBlock.MapObject : null;
        return mapObject != null
               && !IsOreMapObject(mapObject)
               && !IsConveyorBeltMapObject(mapObject);
    }

    private static bool IsOreMapObject(MapObject mapObject)
    {
        return mapObject is Resource resource
               && resource.ResolvedHarvestMode == Resource.HarvestMode.Mining;
    }

    private static bool IsConveyorBeltMapObject(MapObject mapObject)
    {
        return mapObject is ConveyorBelt;
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

    private static bool IsTurningState(RobotArmState robotArmState)
    {
        return robotArmState == RobotArmState.TurningToDrop
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

        handItem.MarkBatchedRenderDataDirty();
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
        Quaternion nextRotation = Quaternion.RotateTowards(
            currentRotation,
            targetLocalRotation,
            Mathf.Max(1f, bodyTurnSpeedDegreesPerSecond) * deltaTime);
        if (Quaternion.Angle(currentRotation, nextRotation) > 0.001f)
        {
            body.localRotation = nextRotation;
            MarkHandItemRenderDataDirty();
        }

        return Quaternion.Angle(body.localRotation, targetLocalRotation) <= 0.1f;
    }

    private void SetBodyLocalRotation(Quaternion targetLocalRotation)
    {
        if (body != null)
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
        public readonly Material[] SharedMaterials;
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
            SharedMaterials = sharedMaterials;
            OriginalRendererEnabled = originalRendererEnabled;
        }

        public bool IsValid =>
            Renderer != null
            && MeshFilter != null
            && MeshFilter.sharedMesh != null
            && Transform != null
            && SharedMaterials != null
            && SharedMaterials.Length > 0;
    }
}
