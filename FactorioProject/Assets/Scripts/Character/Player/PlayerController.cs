using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Player))]
public class PlayerController : MonoBehaviour
{
    private const float ConveyorStandingHeight = 0.2f;
    private const float ConveyorStandingSmoothTime = 0.08f;
    private const float ConveyorStandingEnterDistance = 0.08f;
    private const float ConveyorStandingExitDistance = 0.12f;
    private const float ConveyorStandingHandoffDistance = 0.2f;
    private const float ConveyorCarryAcceleration = 8f;
    private const float ConveyorCarryDeceleration = 10f;
    private const float MinPhysicsMoveDistance = 0.00001f;
    private const float MinPhysicsMoveDistanceSqr = MinPhysicsMoveDistance * MinPhysicsMoveDistance;

    [SerializeField]
    private Transform movementReference;

    [SerializeField, Range(-1f, 1f)]
    private float autoHarvestFacingDot = 0.45f;

    [SerializeField]
    private float harvestStartDelay = 0.5f;

    [SerializeField, Min(0.1f)]
    private float autoPickupRadius = 0.5f;

    [SerializeField, Min(0.5f)]
    private float autoPickupScanRadius = 3f;

    [SerializeField, Min(0f)]
    private float autoPickupInterval = 0.1f;

    private Player player;
    private Joystick joystick;
    private ResourceWrokGauge resourceWorkGauge;
    private Resource currentTargetResource;
    private readonly HashSet<Block> currentFocusedBlocks = new HashSet<Block>();
    private readonly List<Block> combinedInteractionFocusBlocks = new List<Block>();
    private readonly List<Block> nearbyInputOutputModuleFocusBlocks = new List<Block>();
    private readonly List<Block> nearbyInstallationFocusBlocks = new List<Block>();
    private readonly List<Block> nearbyWorkableFocusBlocks = new List<Block>();
    private readonly List<Block> nearbyBoxFocusBlocks = new List<Block>();
    private readonly List<InstallationObject> nearbyInstallationObjects = new List<InstallationObject>();
    private readonly List<WorkableObject> nearbyWorkableObjects = new List<WorkableObject>();
    private readonly List<BoxObject> nearbyBoxObjects = new List<BoxObject>();
    private readonly List<Block> singleFocusedBlockBuffer = new List<Block>(1);
    private readonly List<Block> focusRemovalBuffer = new List<Block>();
    private Rigidbody cachedRigidbody;
    private Vector3 pendingMoveDirection;
    private const float MoveSweepBuffer = 0.01f;
    private const float ConveyorCarrySweepBuffer = 0f;
    private float stationaryHarvestTimer;
    private float autoPickupTimer;
    private TerrainGenerator cachedTerrainGenerator;
    private readonly Queue<Resource> pendingHarvestResources = new Queue<Resource>();
    private bool wasInstallationPlacementActive;
    private bool hasDefaultBodyLocalPosition;
    private Vector3 defaultBodyLocalPosition;
    private float standingVisualOffsetVelocity;
    private bool hasStandingConveyorCoordinate;
    private Vector2Int standingConveyorCoordinate;
    private Vector3 currentConveyorCarryVelocity;

    private void Awake()
    {
        player = GetComponent<Player>();
        cachedRigidbody = GetComponent<Rigidbody>();
        if (cachedRigidbody != null && cachedRigidbody.interpolation == RigidbodyInterpolation.None)
        {
            cachedRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void Start()
    {
        joystick = FindObjectOfType<Joystick>();
        resourceWorkGauge = ResourceWrokGauge.FindOrCreate();
        resourceWorkGauge?.Hide();
        ResolveMovementReference();
        CacheDefaultBodyLocalPosition();
    }

    private void OnDisable()
    {
        RestoreStandingVisualOffset();
        currentConveyorCarryVelocity = Vector3.zero;
        SetFocusedBlocks(null);
        currentFocusedBlocks.Clear();
        focusRemovalBuffer.Clear();
        singleFocusedBlockBuffer.Clear();
    }

    private void Update()
    {
        bool isInteractionLocked = GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked;

        Vector2 input = Vector2.zero;

        if (joystick == null)
        {
            joystick = FindObjectOfType<Joystick>();
        }

        if (movementReference == null)
        {
            ResolveMovementReference();
        }

        if (joystick != null && !isInteractionLocked)
        {
            input = joystick.InputDirection;
        }

        Vector3 moveDirection = GetMoveDirection(input);
        bool hasMovement = moveDirection.sqrMagnitude > 0.0001f;

        if (hasMovement)
        {
            stationaryHarvestTimer = 0f;
        }
        else
        {
            stationaryHarvestTimer += Time.deltaTime;
        }

        pendingMoveDirection = moveDirection;

        if (isInteractionLocked)
        {
            HandleInstallationPlacementLock();
            wasInstallationPlacementActive = true;
            return;
        }

        if (wasInstallationPlacementActive)
        {
            stationaryHarvestTimer = 0f;
            wasInstallationPlacementActive = false;
        }

        if (hasMovement)
        {
            if (cachedRigidbody == null)
            {
                transform.position += moveDirection * player.Stat.currentMoveSpeed * Time.deltaTime;
            }

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
            Transform rotationTarget = player.BodyTransform != null ? player.BodyTransform : transform;
            rotationTarget.rotation = Quaternion.RotateTowards(
                rotationTarget.rotation,
                targetRotation,
                player.Stat.rotateSpeed * Time.deltaTime);
        }

        player.UpdateCarryState();
        if (player.IsCarrying)
        {
            CancelPendingHarvest();
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
        }
        else
        {
            UpdateAutoHarvest(hasMovement);
        }

        bool finishedPickThisFrame = player.UpdateAnimationState(hasMovement);
        ResolveCompletedPick(finishedPickThisFrame);
        RefreshInteractionFocus(hasMovement);

        UpdateAutoPickup();
    }

    private void FixedUpdate()
    {
        if (GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked)
        {
            pendingMoveDirection = Vector3.zero;
            currentConveyorCarryVelocity = Vector3.zero;
            return;
        }

        if (cachedRigidbody == null)
        {
            return;
        }

        Vector3 manualVelocity = pendingMoveDirection * player.Stat.currentMoveSpeed;
        Vector3 targetCarryVelocity = Vector3.zero;
        bool hasRawCarryDelta = TryGetStandingConveyorCarryDelta(
            Time.fixedDeltaTime,
            out Vector3 rawCarryDelta,
            out Block standingConveyorBlock);
        if (hasRawCarryDelta)
        {
            targetCarryVelocity = rawCarryDelta / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            if (standingConveyorBlock != null
                && standingConveyorBlock.IsCornerConveyorBlock()
                && IsOpposingConveyorCarry(targetCarryVelocity))
            {
                hasRawCarryDelta = false;
                rawCarryDelta = Vector3.zero;
                targetCarryVelocity = Vector3.zero;
                currentConveyorCarryVelocity = Vector3.zero;
            }
        }

        float carryRate = targetCarryVelocity.sqrMagnitude > currentConveyorCarryVelocity.sqrMagnitude
            ? ConveyorCarryAcceleration
            : ConveyorCarryDeceleration;
        currentConveyorCarryVelocity = Vector3.MoveTowards(
            currentConveyorCarryVelocity,
            targetCarryVelocity,
            carryRate * Time.fixedDeltaTime);

        Vector3 manualDelta = manualVelocity * Time.fixedDeltaTime;
        Vector3 carryDelta = currentConveyorCarryVelocity * Time.fixedDeltaTime;
        Vector3 totalDelta = manualDelta + carryDelta;

        if (manualDelta.sqrMagnitude <= 0.0001f)
        {
            if (hasRawCarryDelta && rawCarryDelta.sqrMagnitude > 0.0000001f)
            {
                if (cachedRigidbody.IsSleeping())
                {
                    cachedRigidbody.WakeUp();
                }

                MoveRigidbody(rawCarryDelta, ConveyorCarrySweepBuffer);
                return;
            }

            if (carryDelta.sqrMagnitude > 0.0000001f)
            {
                if (cachedRigidbody.IsSleeping())
                {
                    cachedRigidbody.WakeUp();
                }

                MoveRigidbody(carryDelta, ConveyorCarrySweepBuffer);
                return;
            }
        }

        if (totalDelta.sqrMagnitude <= MinPhysicsMoveDistanceSqr)
        {
            return;
        }

        if (cachedRigidbody.IsSleeping())
        {
            cachedRigidbody.WakeUp();
        }

        MoveRigidbody(totalDelta, MoveSweepBuffer);
    }

    private void LateUpdate()
    {
        ApplyStandingVisualOffset();
    }

    private void MoveRigidbody(Vector3 delta)
    {
        MoveRigidbody(delta, MoveSweepBuffer);
    }

    private void MoveRigidbody(Vector3 delta, float maxSweepBuffer)
    {
        float distance = delta.magnitude;
        if (distance <= MinPhysicsMoveDistance)
        {
            return;
        }

        float sweepBuffer = Mathf.Min(maxSweepBuffer, distance * 0.25f);

        Vector3 direction = delta / distance;
        Vector3 startPosition = cachedRigidbody.position;
        Vector3 finalMove = Vector3.zero;

        if (cachedRigidbody.SweepTest(direction, out RaycastHit hit, distance + sweepBuffer, QueryTriggerInteraction.Ignore))
        {
            float allowedDistance = Mathf.Max(0f, hit.distance - sweepBuffer);
            if (allowedDistance > 0f)
            {
                finalMove += direction * allowedDistance;
            }

            float remainingDistance = distance - allowedDistance;
            if (remainingDistance > 0.0001f)
            {
                Vector3 remaining = direction * remainingDistance;
                Vector3 slide = Vector3.ProjectOnPlane(remaining, hit.normal);
                if (slide.sqrMagnitude > 0.0001f)
                {
                    Vector3 slideDirection = slide.normalized;
                    float slideDistance = slide.magnitude;
                    if (!cachedRigidbody.SweepTest(slideDirection, out _, slideDistance + sweepBuffer, QueryTriggerInteraction.Ignore))
                    {
                        finalMove += slide;
                    }
                }
            }
        }
        else
        {
            finalMove = delta;
        }

        Vector3 finalPosition = startPosition + finalMove;
        if (finalMove.sqrMagnitude > MinPhysicsMoveDistanceSqr)
        {
            cachedRigidbody.MovePosition(finalPosition);
        }
    }

    private void CacheDefaultBodyLocalPosition()
    {
        Transform bodyTransform = player != null ? player.BodyTransform : null;
        if (hasDefaultBodyLocalPosition || bodyTransform == null || bodyTransform == transform)
        {
            return;
        }

        defaultBodyLocalPosition = bodyTransform.localPosition;
        hasDefaultBodyLocalPosition = true;
    }

    private void ApplyStandingVisualOffset()
    {
        CacheDefaultBodyLocalPosition();

        Transform bodyTransform = player != null ? player.BodyTransform : null;
        if (!hasDefaultBodyLocalPosition || bodyTransform == null || bodyTransform == transform)
        {
            return;
        }

        float targetOffset = TryGetStandingConveyorBlock(out _) ? ConveyorStandingHeight : 0f;
        float targetY = defaultBodyLocalPosition.y + targetOffset;
        Vector3 localPosition = bodyTransform.localPosition;

        localPosition.y = Mathf.SmoothDamp(
            localPosition.y,
            targetY,
            ref standingVisualOffsetVelocity,
            ConveyorStandingSmoothTime);

        if (Mathf.Abs(localPosition.y - targetY) <= 0.001f
            && Mathf.Abs(standingVisualOffsetVelocity) <= 0.001f)
        {
            localPosition.y = targetY;
            standingVisualOffsetVelocity = 0f;
        }

        bodyTransform.localPosition = localPosition;
    }

    private void RestoreStandingVisualOffset()
    {
        Transform bodyTransform = player != null ? player.BodyTransform : null;
        if (!hasDefaultBodyLocalPosition || bodyTransform == null || bodyTransform == transform)
        {
            return;
        }

        bodyTransform.localPosition = defaultBodyLocalPosition;
        standingVisualOffsetVelocity = 0f;
        hasStandingConveyorCoordinate = false;
        standingConveyorCoordinate = default;
    }

    private bool IsOpposingConveyorCarry(Vector3 carryVelocity)
    {
        Vector3 inputDirection = pendingMoveDirection;
        inputDirection.y = 0f;
        if (inputDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        carryVelocity.y = 0f;
        if (carryVelocity.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        return Vector3.Dot(inputDirection.normalized, carryVelocity.normalized) <= -0.2f;
    }

    private bool IsOpposingConveyorCarry(Block conveyorBlock, Vector3 samplePosition)
    {
        if (conveyorBlock == null
            || !conveyorBlock.TryGetConveyorCarryVelocity(samplePosition, out Vector3 carryVelocity))
        {
            return false;
        }

        return IsOpposingConveyorCarry(carryVelocity);
    }

    private bool TryGetStandingConveyorBlock(out Block standingBlock)
    {
        standingBlock = null;

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
        }

        if (cachedTerrainGenerator == null)
        {
            hasStandingConveyorCoordinate = false;
            standingConveyorCoordinate = default;
            return false;
        }

        Vector3 samplePosition = cachedRigidbody != null
            ? cachedRigidbody.position
            : transform.position;

        float enterDistanceSqr = ConveyorStandingEnterDistance * ConveyorStandingEnterDistance;
        float exitDistanceSqr = ConveyorStandingExitDistance * ConveyorStandingExitDistance;
        float handoffDistanceSqr = ConveyorStandingHandoffDistance * ConveyorStandingHandoffDistance;

        if (hasStandingConveyorCoordinate
            && cachedTerrainGenerator.TryGetLoadedBlock(standingConveyorCoordinate, out Block currentBlock)
            && currentBlock != null
            && currentBlock.TryGetConveyorStandingDistanceSqr(samplePosition, out float currentDistanceSqr))
        {
            bool isOpposingCurrentCarry = IsOpposingConveyorCarry(currentBlock, samplePosition);
            float retainedDistanceSqr = isOpposingCurrentCarry ? enterDistanceSqr : exitDistanceSqr;
            bool canUseCarryHandoff = !isOpposingCurrentCarry && currentConveyorCarryVelocity.sqrMagnitude > 0.0001f;

            if (currentDistanceSqr <= retainedDistanceSqr
                || (canUseCarryHandoff && currentDistanceSqr <= handoffDistanceSqr))
            {
                standingBlock = currentBlock;
                return true;
            }

            if (!isOpposingCurrentCarry
                && currentBlock.TryGetNextConnectedConveyorBlock(out Block nextBlock)
                && nextBlock != null
                && nextBlock.TryGetConveyorStandingDistanceSqr(samplePosition, out float nextDistanceSqr)
                && nextDistanceSqr <= handoffDistanceSqr)
            {
                standingBlock = nextBlock;
                hasStandingConveyorCoordinate = true;
                standingConveyorCoordinate = nextBlock.Coordinate;
                return true;
            }
        }

        hasStandingConveyorCoordinate = false;
        standingConveyorCoordinate = default;

        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(samplePosition.x),
            Mathf.RoundToInt(samplePosition.z));

        bool isOpposingResidualCarry = IsOpposingConveyorCarry(currentConveyorCarryVelocity);
        float searchDistanceSqr = currentConveyorCarryVelocity.sqrMagnitude > 0.0001f && !isOpposingResidualCarry
            ? handoffDistanceSqr
            : enterDistanceSqr;
        float bestDistanceSqr = float.MaxValue;
        Block bestBlock = null;
        Vector2Int bestCoordinate = default;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block)
                    || block == null
                    || !block.TryGetConveyorStandingDistanceSqr(samplePosition, out float distanceSqr)
                    || distanceSqr > searchDistanceSqr
                    || distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                bestBlock = block;
                bestCoordinate = coordinate;
            }
        }

        if (bestBlock == null)
        {
            return false;
        }

        standingBlock = bestBlock;
        hasStandingConveyorCoordinate = true;
        standingConveyorCoordinate = bestCoordinate;
        return true;
    }

    private bool TryGetStandingConveyorCarryDelta(float deltaTime, out Vector3 carryDelta, out Block standingBlock)
    {
        carryDelta = Vector3.zero;
        standingBlock = null;
        if (deltaTime <= 0f
            || !TryGetStandingConveyorBlock(out Block resolvedStandingBlock)
            || resolvedStandingBlock == null)
        {
            return false;
        }

        standingBlock = resolvedStandingBlock;

        Vector3 samplePosition = cachedRigidbody != null
            ? cachedRigidbody.position
            : transform.position;

        if (!standingBlock.TryGetConveyorCarryDeltaWithHandoff(samplePosition, deltaTime, out Block resultingBlock, out carryDelta))
        {
            return false;
        }

        if (resultingBlock != null)
        {
            float switchDistanceSqr = ConveyorStandingHandoffDistance * ConveyorStandingHandoffDistance;
            Vector3 predictedPosition = samplePosition + carryDelta;

            if (resultingBlock.TryGetConveyorStandingDistanceSqr(predictedPosition, out float resultingDistanceSqr)
                && resultingDistanceSqr <= switchDistanceSqr)
            {
                hasStandingConveyorCoordinate = true;
                standingConveyorCoordinate = resultingBlock.Coordinate;
            }
            else if (standingBlock.TryGetConveyorStandingDistanceSqr(predictedPosition, out float standingDistanceSqr)
                     && standingDistanceSqr <= switchDistanceSqr)
            {
                hasStandingConveyorCoordinate = true;
                standingConveyorCoordinate = standingBlock.Coordinate;
            }
            else
            {
                hasStandingConveyorCoordinate = false;
                standingConveyorCoordinate = default;
            }
        }

        return true;
    }

    private void HandleInstallationPlacementLock()
    {
        pendingMoveDirection = Vector3.zero;
        stationaryHarvestTimer = 0f;
        autoPickupTimer = 0f;

        if (joystick != null)
        {
            joystick.ResetInput();
        }

        CancelPendingHarvest();
        currentTargetResource = null;
        SetFocusedBlock(null);
        resourceWorkGauge?.HideIfNotFinishing();
        player.StopImmediateActions();
        player.UpdateCarryState();
    }

    private bool UpdateAutoHarvest(bool hasMovement)
    {
        Resource nextTarget = FindBestHarvestTarget();

        if (currentTargetResource != nextTarget)
        {
            CancelPendingHarvest();
            currentTargetResource = nextTarget;
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
        }

        if (currentTargetResource == null)
        {
            CancelPendingHarvest();
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }

        if (hasMovement)
        {
            CancelPendingHarvest();
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }
        
        if (stationaryHarvestTimer < harvestStartDelay)
        {
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }

        float harvestSpeed = GetHarvestSpeed(currentTargetResource);
        if (harvestSpeed <= 0f)
        {
            SetFocusedBlock(null);
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }

        SetFocusedBlock(currentTargetResource.OwningBlock);
        resourceWorkGauge?.Bind(currentTargetResource);

        int harvestPower = GetHarvestPower(currentTargetResource);
        int preparedStepCount = currentTargetResource.PrepareHarvestSteps(harvestSpeed * Time.deltaTime, harvestPower);

        for (int i = 0; i < preparedStepCount; i++)
        {
            pendingHarvestResources.Enqueue(currentTargetResource);
            player.QueuePickAnimation();
        }

        if (!currentTargetResource.CanHarvest)
        {
            SetFocusedBlock(null);
            currentTargetResource = null;
            stationaryHarvestTimer = 0f;
        }

        return preparedStepCount > 0;
    }

    private void ResolveCompletedPick(bool finishedPickThisFrame)
    {
        if (!finishedPickThisFrame || pendingHarvestResources.Count == 0)
        {
            return;
        }

        Resource harvestedResource = pendingHarvestResources.Dequeue();
        if (harvestedResource == null)
        {
            return;
        }

        harvestedResource.CommitPreparedHarvestStep();

        if (harvestedResource == currentTargetResource)
        {
            resourceWorkGauge?.Bind(currentTargetResource);

            if (!currentTargetResource.CanHarvest)
            {
                SetFocusedBlock(null);
                currentTargetResource = null;
                return;
            }
        }
    }

    private void CancelPendingHarvest()
    {
        if (pendingHarvestResources.Count == 0)
        {
            player.ClearQueuedPickAnimations();
            resourceWorkGauge?.HideIfNotFinishing();
            return;
        }

        foreach (Resource resource in pendingHarvestResources)
        {
            resource?.CancelPreparedHarvestStep();
        }

        pendingHarvestResources.Clear();
        player.ClearQueuedPickAnimations();
        resourceWorkGauge?.HideIfNotFinishing();
    }

    private Resource FindBestHarvestTarget()
    {
        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector3 forward = player.BodyTransform != null ? player.BodyTransform.forward : transform.forward;
        float harvestRange = player.State.HarvestRange;
        float maxDistanceSqr = harvestRange * harvestRange;
        float bestScore = float.NegativeInfinity;
        Resource bestResource = null;

        IReadOnlyList<Resource> resources = Resource.ActiveResources;
        for (int i = 0; i < resources.Count; i++)
        {
            Resource resource = resources[i];
            if (resource == null || !resource.CanHarvest)
            {
                continue;
            }

            Vector3 offset = resource.FocusPoint - origin;
            offset.y = 0f;

            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr <= 0.0001f || distanceSqr > maxDistanceSqr)
            {
                continue;
            }

            Vector3 direction = offset.normalized;
            float facingDot = Vector3.Dot(forward, direction);
            if (facingDot < autoHarvestFacingDot)
            {
                continue;
            }

            float normalizedDistanceScore = 1f - (distanceSqr / maxDistanceSqr);
            float score = facingDot * 2f + normalizedDistanceScore;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestResource = resource;
        }

        return bestResource;
    }

    private float GetHarvestSpeed(Resource resource)
    {
        if (resource == null)
        {
            return 0f;
        }

        return resource.ResolvedHarvestMode == Resource.HarvestMode.Logging
            ? player.State.LoggingSpeed
            : player.State.MiningSpeed;
    }

    private int GetHarvestPower(Resource resource)
    {
        if (resource == null)
        {
            return 1;
        }

        return resource.ResolvedHarvestMode == Resource.HarvestMode.Logging
            ? player.State.LoggingPower
            : player.State.MiningPower;
    }

    private void RefreshInteractionFocus(bool hasMovement)
    {
        if (!player.IsCarrying
            && currentTargetResource != null
            && currentTargetResource.CanHarvest
            && !hasMovement
            && stationaryHarvestTimer >= harvestStartDelay
            && GetHarvestSpeed(currentTargetResource) > 0f)
        {
            SetFocusedBlock(currentTargetResource.OwningBlock);
            return;
        }

        combinedInteractionFocusBlocks.Clear();
        if (FindCurrentInputOutputModuleFocusBlocks(nearbyInputOutputModuleFocusBlocks))
        {
            AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInputOutputModuleFocusBlocks);
        }

        FindNearbyWorkableBlocks(nearbyWorkableFocusBlocks);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyWorkableFocusBlocks);

        FindNearbyBoxBlocks(nearbyBoxFocusBlocks);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyBoxFocusBlocks);

        FindNearbyInstallationBlocks(nearbyInstallationFocusBlocks);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInstallationFocusBlocks);
        SetFocusedBlocks(combinedInteractionFocusBlocks);
    }

    public bool HasFocusedWorkableObject(IReadOnlyList<int> requiredItemIds)
    {
        if (requiredItemIds == null || requiredItemIds.Count <= 0)
        {
            return false;
        }

        foreach (Block block in currentFocusedBlocks)
        {
            if (block == null
                || !(block.MapObject is WorkableObject workableObject)
                || workableObject == null
                || !workableObject.gameObject.activeInHierarchy)
            {
                continue;
            }

            int itemId = workableObject.ResolveItemId();
            if (itemId < 0)
            {
                continue;
            }

            for (int i = 0; i < requiredItemIds.Count; i++)
            {
                if (requiredItemIds[i] == itemId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryGetFocusedBoxObject(out BoxObject focusedBoxObject)
    {
        focusedBoxObject = null;
        if (currentFocusedBlocks.Count == 0 || player == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Block block in currentFocusedBlocks)
        {
            if (block == null
                || !(block.MapObject is BoxObject boxObject)
                || boxObject == null
                || !boxObject.gameObject.activeInHierarchy)
            {
                continue;
            }

            float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(boxObject, block, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedBoxObject = boxObject;
        }

        return focusedBoxObject != null;
    }

    public bool TryGetFocusedConveyorBelt(out ConveyorBelt focusedConveyorBelt, out Block focusedBlock)
    {
        focusedConveyorBelt = null;
        focusedBlock = null;
        if (currentFocusedBlocks.Count == 0 || player == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Block block in currentFocusedBlocks)
        {
            if (block == null
                || !(block.MapObject is ConveyorBelt conveyorBelt)
                || conveyorBelt == null
                || !conveyorBelt.gameObject.activeInHierarchy)
            {
                continue;
            }

            float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(conveyorBelt, block, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedConveyorBelt = conveyorBelt;
            focusedBlock = block;
        }

        return focusedConveyorBelt != null && focusedBlock != null;
    }

    public bool TryGetFocusedItemFilterMapObject(out MapObject focusedMapObject)
    {
        focusedMapObject = null;
        if (currentFocusedBlocks.Count == 0 || player == null)
        {
            return false;
        }

        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null || definitions.Count == 0)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Block block in currentFocusedBlocks)
        {
            MapObject mapObject = block != null ? block.MapObject : null;
            if (mapObject == null || !mapObject.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!IsItemFilterEnabled(mapObject.ResolveItemId(), definitions))
            {
                continue;
            }

            float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(mapObject, block, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedMapObject = mapObject;
        }

        return focusedMapObject != null;
    }

    private bool FindCurrentInputOutputModuleFocusBlocks(List<Block> results)
    {
        if (results == null)
        {
            return false;
        }

        results.Clear();

        if (player == null)
        {
            return false;
        }

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
        }

        if (cachedTerrainGenerator == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));

        if (!InputOutputModule.TryGetModuleAtRuntimeGridCoordinate(playerCoordinate, out InputOutputModule inputOutputModule)
            || inputOutputModule == null)
        {
            return false;
        }

        IReadOnlyList<Vector2Int> focusCoordinates = inputOutputModule.RuntimeFocusCoordinates;
        if (focusCoordinates == null || focusCoordinates.Count <= 0)
        {
            return false;
        }

        if (inputOutputModule.FocusMode == MapObject.MultiFocusMode.NearOne)
        {
            Block nearestBlock = null;
            float nearestDistanceSqr = float.MaxValue;

            for (int i = 0; i < focusCoordinates.Count; i++)
            {
                if (!cachedTerrainGenerator.TryGetLoadedBlock(focusCoordinates[i], out Block block) || block == null)
                {
                    continue;
                }

                float distanceSqr = GetBlockFocusDistanceSqr(block, origin);
                if (distanceSqr >= nearestDistanceSqr)
                {
                    continue;
                }

                nearestDistanceSqr = distanceSqr;
                nearestBlock = block;
            }

            if (nearestBlock != null)
            {
                results.Add(nearestBlock);
            }

            return results.Count > 0;
        }

        for (int i = 0; i < focusCoordinates.Count; i++)
        {
            if (!cachedTerrainGenerator.TryGetLoadedBlock(focusCoordinates[i], out Block block) || block == null)
            {
                continue;
            }

            if (!results.Contains(block))
            {
                results.Add(block);
            }
        }

        return results.Count > 0;
    }

    private void FindNearbyWorkableBlocks(List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();

        if (player == null)
        {
            return;
        }

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
        }

        if (cachedTerrainGenerator == null)
        {
            return;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float focusRadius = Mathf.Max(0.5f, player.State.HarvestRange);
        float focusRadiusSqr = focusRadius * focusRadius;
        float globalWorkablePadding = Mathf.Max(0f, WorkableObject.GlobalMaxFocusActivationRadius);
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(focusRadius + globalWorkablePadding + 1f));
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        nearbyWorkableObjects.Clear();
        WorkableObject nearestWorkableObject = null;
        Block nearestWorkableBlock = null;
        float nearestWorkableDistanceSqr = float.MaxValue;

        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (!(block.MapObject is WorkableObject workableObject) || workableObject == null || !workableObject.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (nearbyWorkableObjects.Contains(workableObject))
                {
                    continue;
                }

                nearbyWorkableObjects.Add(workableObject);

                float distanceSqr = GetWorkableFocusDistanceSqr(workableObject, block, origin);
                if (distanceSqr > focusRadiusSqr)
                {
                    continue;
                }

                if (workableObject.FocusMode == MapObject.MultiFocusMode.NearOne)
                {
                    if (distanceSqr < nearestWorkableDistanceSqr)
                    {
                        nearestWorkableDistanceSqr = distanceSqr;
                        nearestWorkableObject = workableObject;
                        nearestWorkableBlock = block;
                    }

                    continue;
                }

                AppendMapObjectFocusBlocks(workableObject, block, results);
            }
        }

        if (nearestWorkableObject != null)
        {
            AppendMapObjectFocusBlocks(nearestWorkableObject, nearestWorkableBlock, results);
        }
    }

    private void FindNearbyBoxBlocks(List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();

        if (player == null)
        {
            return;
        }

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
        }

        if (cachedTerrainGenerator == null)
        {
            return;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float globalBoxPadding = Mathf.Max(0f, BoxObject.GlobalMaxFocusActivationRadius);
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(globalBoxPadding + 2f));
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        nearbyBoxObjects.Clear();
        BoxObject nearestBoxObject = null;
        Block nearestBoxBlock = null;
        float nearestBoxDistanceSqr = float.MaxValue;

        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (!(block.MapObject is BoxObject boxObject) || boxObject == null || !boxObject.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (nearbyBoxObjects.Contains(boxObject))
                {
                    continue;
                }

                nearbyBoxObjects.Add(boxObject);

                if (!boxObject.IsWithinFocusRange(origin))
                {
                    continue;
                }

                float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(boxObject, block, origin);
                if (boxObject.FocusMode == MapObject.MultiFocusMode.NearOne)
                {
                    if (distanceSqr < nearestBoxDistanceSqr)
                    {
                        nearestBoxDistanceSqr = distanceSqr;
                        nearestBoxObject = boxObject;
                        nearestBoxBlock = block;
                    }

                    continue;
                }

                AppendMapObjectFocusBlocks(boxObject, block, results);
            }
        }

        if (nearestBoxObject != null)
        {
            AppendMapObjectFocusBlocks(nearestBoxObject, nearestBoxBlock, results);
        }
    }

    private void FindNearbyInstallationBlocks(List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();

        if (player == null)
        {
            return;
        }

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
        }

        if (cachedTerrainGenerator == null)
        {
            return;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float globalInstallationPadding = Mathf.Max(0f, InstallationObject.GlobalMaxFocusActivationRadius);
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(globalInstallationPadding + 2f));
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        nearbyInstallationObjects.Clear();
        InstallationObject nearestInstallationObject = null;
        Block nearestInstallationBlock = null;
        float nearestInstallationDistanceSqr = float.MaxValue;

        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (!(block.MapObject is InstallationObject installationObject)
                    || installationObject == null
                    || !installationObject.gameObject.activeInHierarchy
                    || installationObject is WorkableObject
                    || installationObject is BoxObject)
                {
                    continue;
                }

                if (nearbyInstallationObjects.Contains(installationObject))
                {
                    continue;
                }

                nearbyInstallationObjects.Add(installationObject);

                float focusRadius = Mathf.Max(0f, installationObject.FocusActivationRadius);
                if (focusRadius <= 0f)
                {
                    continue;
                }

                float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(installationObject, block, origin);
                if (distanceSqr > focusRadius * focusRadius)
                {
                    continue;
                }

                if (installationObject.FocusMode == MapObject.MultiFocusMode.NearOne)
                {
                    if (distanceSqr < nearestInstallationDistanceSqr)
                    {
                        nearestInstallationDistanceSqr = distanceSqr;
                        nearestInstallationObject = installationObject;
                        nearestInstallationBlock = block;
                    }

                    continue;
                }

                AppendMapObjectFocusBlocks(installationObject, block, results);
            }
        }

        if (nearestInstallationObject != null)
        {
            AppendMapObjectFocusBlocks(nearestInstallationObject, nearestInstallationBlock, results);
        }
    }

    private float GetWorkableFocusDistanceSqr(WorkableObject workableObject, Block block, Vector3 origin)
    {
        Bounds bounds = GetMapObjectFocusBounds(workableObject, block, workableObject != null ? workableObject.FocusActivationRadius : 0f);
        Vector3 closestPoint = bounds.ClosestPoint(origin);
        closestPoint.y = origin.y;

        Vector3 offset = closestPoint - origin;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private float GetMapObjectFocusSelectionDistanceSqr(MapObject mapObject, Block block, Vector3 origin)
    {
        return GetMapObjectFocusDistanceSqr(mapObject, block, origin, 0f);
    }

    private float GetMapObjectFocusDistanceSqr(MapObject mapObject, Block block, Vector3 origin, float focusPadding = 0f)
    {
        Bounds bounds = GetMapObjectFocusBounds(mapObject, block, focusPadding);
        Vector3 closestPoint = bounds.ClosestPoint(origin);
        closestPoint.y = origin.y;

        Vector3 offset = closestPoint - origin;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private Bounds GetMapObjectFocusBounds(MapObject mapObject, Block block, float focusPadding = 0f)
    {
        Renderer[] renderers = mapObject != null
            ? mapObject.GetComponentsInChildren<Renderer>(true)
            : null;
        if (renderers != null)
        {
            Bounds combinedBounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rendererComponent = renderers[i];
                if (rendererComponent == null || !rendererComponent.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combinedBounds = rendererComponent.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(rendererComponent.bounds);
                }
            }

            if (hasBounds)
            {
                if (focusPadding > 0f)
                {
                    combinedBounds.Expand(new Vector3(
                        focusPadding * 2f,
                        0f,
                        focusPadding * 2f));
                }

                return combinedBounds;
            }
        }

        Vector3 center = block != null ? block.transform.position : mapObject.transform.position;
        Vector3 size = Vector3.one;
        if (mapObject != null)
        {
            MapObject.MapObjectStatus status = mapObject.Status;
            size = new Vector3(
                Mathf.Max(1f, status.mapSizeX),
                1f,
                Mathf.Max(1f, status.mapSizeY));
        }

        Bounds fallbackBounds = new Bounds(center, size);
        if (focusPadding > 0f)
        {
            fallbackBounds.Expand(new Vector3(
                focusPadding * 2f,
                0f,
                focusPadding * 2f));
        }

        return fallbackBounds;
    }

    private static bool IsItemFilterEnabled(int itemId, List<ItemDefinition> definitions)
    {
        if (itemId < 0 || definitions == null)
        {
            return false;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id != itemId)
            {
                continue;
            }

            return definition.itemFilter;
        }

        return false;
    }

    private bool AppendMapObjectFocusBlocks(MapObject mapObject, Block fallbackBlock, List<Block> results)
    {
        if (mapObject == null || results == null)
        {
            return false;
        }

        bool appended = false;

        if (mapObject is InputOutputModule inputOutputModule)
        {
            IReadOnlyList<Vector2Int> focusCoordinates = inputOutputModule.RuntimeFocusCoordinates;
            if (focusCoordinates != null)
            {
                for (int i = 0; i < focusCoordinates.Count; i++)
                {
                    if (!TryAppendFocusBlock(results, focusCoordinates[i]))
                    {
                        continue;
                    }

                    appended = true;
                }
            }
        }
        else if (mapObject is InstallationObject installationObject)
        {
            IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject.RuntimeOccupiedCoordinates;
            if (occupiedCoordinates != null)
            {
                for (int i = 0; i < occupiedCoordinates.Count; i++)
                {
                    if (!TryAppendFocusBlock(results, occupiedCoordinates[i]))
                    {
                        continue;
                    }

                    appended = true;
                }
            }
        }

        if (!appended && fallbackBlock != null && !results.Contains(fallbackBlock))
        {
            results.Add(fallbackBlock);
            appended = true;
        }

        return appended;
    }

    private bool TryAppendFocusBlock(List<Block> results, Vector2Int coordinate)
    {
        if (results == null)
        {
            return false;
        }

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
        }

        if (cachedTerrainGenerator == null
            || !cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || results.Contains(block))
        {
            return false;
        }

        results.Add(block);
        return true;
    }

    private static float GetBlockFocusDistanceSqr(Block block, Vector3 origin)
    {
        if (block == null)
        {
            return float.MaxValue;
        }

        Vector3 offset = block.transform.position - origin;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private void SetFocusedBlock(Block nextBlock)
    {
        if (nextBlock == null)
        {
            SetFocusedBlocks(null);
            return;
        }

        singleFocusedBlockBuffer.Clear();
        singleFocusedBlockBuffer.Add(nextBlock);
        SetFocusedBlocks(singleFocusedBlockBuffer);
    }

    private void SetFocusedBlocks(List<Block> nextBlocks)
    {
        focusRemovalBuffer.Clear();

        foreach (Block currentBlock in currentFocusedBlocks)
        {
            if (ContainsFocusedBlock(nextBlocks, currentBlock))
            {
                continue;
            }

            focusRemovalBuffer.Add(currentBlock);
        }

        for (int i = 0; i < focusRemovalBuffer.Count; i++)
        {
            Block block = focusRemovalBuffer[i];
            currentFocusedBlocks.Remove(block);
            if (block != null)
            {
                block.SetFocusVisible(false);
            }
        }

        if (nextBlocks == null)
        {
            return;
        }

        for (int i = 0; i < nextBlocks.Count; i++)
        {
            Block block = nextBlocks[i];
            if (block == null)
            {
                continue;
            }

            if (currentFocusedBlocks.Add(block))
            {
                if (block != null)
                {
                    block.SetFocusVisible(true);
                }
            }
        }
    }

    private static bool ContainsFocusedBlock(List<Block> blocks, Block target)
    {
        if (blocks == null || target == null)
        {
            return false;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendUniqueBlocks(List<Block> target, List<Block> source)
    {
        if (target == null || source == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            Block block = source[i];
            if (block == null || target.Contains(block))
            {
                continue;
            }

            target.Add(block);
        }
    }

    private void ResolveMovementReference()
    {
        if (movementReference != null)
        {
            return;
        }

        if (Camera.main != null)
        {
            movementReference = Camera.main.transform;
        }
    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        if (movementReference == null)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        Vector3 forward = movementReference.forward;
        Vector3 right = movementReference.right;
        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude <= 0.0001f || right.sqrMagnitude <= 0.0001f)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = (right * input.x) + (forward * input.y);
        return moveDirection.sqrMagnitude > 1f ? moveDirection.normalized : moveDirection;
    }

    private void UpdateAutoPickup()
    {
        if (player == null || autoPickupRadius <= 0f)
        {
            return;
        }

        player.UpdateDropExitGate(player.transform.position);
        if (player.IsDropExitPending)
        {
            return;
        }

        autoPickupTimer -= Time.deltaTime;
        if (autoPickupTimer > 0f)
        {
            return;
        }

        autoPickupTimer = Mathf.Max(0f, autoPickupInterval);

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
        }

        if (cachedTerrainGenerator == null)
        {
            return;
        }

        Vector3 playerPosition = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector2Int center = new Vector2Int(Mathf.RoundToInt(playerPosition.x), Mathf.RoundToInt(playerPosition.z));
        int radius = Mathf.Max(1, Mathf.CeilToInt(autoPickupScanRadius));
        float scanRadiusSqr = autoPickupScanRadius * autoPickupScanRadius;

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                Vector3 blockCenter = new Vector3(coordinate.x, playerPosition.y, coordinate.y);
                Vector3 offset = blockCenter - playerPosition;
                offset.y = 0f;
                if (offset.sqrMagnitude > scanRadiusSqr)
                {
                    continue;
                }

                if (!cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (block.Type != Block.BlockType.Ground)
                {
                    continue;
                }

                if (block.TryAutoPickupFloorObjects(player, playerPosition, autoPickupRadius))
                {
                    return;
                }
            }
        }
    }
}
