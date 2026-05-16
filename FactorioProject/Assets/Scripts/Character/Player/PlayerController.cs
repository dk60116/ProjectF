using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Serialization;

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
    private const float DefaultMultiFocusFacingScoreWeight = 0.75f;
    private const float TemporaryDropFocusDuration = 0.18f;

    [SerializeField]
    private Transform movementReference;

    [SerializeField, FormerlySerializedAs("autoHarvestFacingDot"), Range(-1f, 1f)]
    private float resourceInteractionFacingDot = 0.45f;

    [SerializeField, Min(0f)]
    private float multiFocusFacingScoreWeight = 0.75f;

    [SerializeField, Min(0.01f)]
    private float rotationInterpolationSpeed = 12f;

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
    private readonly HashSet<Block> currentMouseFocusedBlocks = new HashSet<Block>();
    private readonly List<Block> mouseFocusBlocks = new List<Block>();
    private readonly List<Block> mouseFocusRemovalBuffer = new List<Block>();
    private readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>();
    private readonly List<InstallationObject> nearbyInstallationObjects = new List<InstallationObject>();
    private readonly List<WorkableObject> nearbyWorkableObjects = new List<WorkableObject>();
    private readonly List<BoxObject> nearbyBoxObjects = new List<BoxObject>();
    private readonly HashSet<WorkableObject> currentSelectedWorkableRangeObjects = new HashSet<WorkableObject>();
    private readonly HashSet<WorkableObject> nextSelectedWorkableRangeObjects = new HashSet<WorkableObject>();
    private readonly List<WorkableObject> selectedWorkableRangeRemovalBuffer = new List<WorkableObject>();
    private readonly List<Block> singleFocusedBlockBuffer = new List<Block>(1);
    private readonly List<Block> focusRemovalBuffer = new List<Block>();
    private Rigidbody cachedRigidbody;
    private Vector3 pendingMoveDirection;
    private const float MoveSweepBuffer = 0.01f;
    private const float ConveyorCarrySweepBuffer = 0f;
    private TerrainGenerator cachedTerrainGenerator;
    private readonly Queue<Resource> pendingHarvestResources = new Queue<Resource>();
    private bool wasInstallationPlacementActive;
    private InstallationPlacementController cachedInstallationPlacementController;
    private bool hasDefaultBodyLocalPosition;
    private Vector3 defaultBodyLocalPosition;
    private float standingVisualOffsetVelocity;
    private bool hasStandingConveyorCoordinate;
    private Vector2Int standingConveyorCoordinate;
    private Vector3 currentConveyorCarryVelocity;
    private bool hasPendingFacingDirection;
    private Vector3 pendingFacingDirection;
    private Block temporaryDropFocusBlock;
    private float temporaryDropFocusUntilTime;
    private MapObject currentMouseFocusedMapObject;
    private PointerEventData pointerEventData;

    private struct InteractionFocusCandidate
    {
        public bool hasCandidate;
        public bool useSingleBlock;
        public float score;
        public MapObject mapObject;
        public Block fallbackBlock;
        public Block singleBlock;
    }

    public bool IsResourceHarvestingActive => currentTargetResource != null && pendingHarvestResources.Count > 0;

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
        hasPendingFacingDirection = false;
        pendingFacingDirection = Vector3.zero;
        ClearTemporaryDropFocus();
        SetFocusedBlocks(null);
        SetMouseFocusedBlocks(null);
        currentFocusedBlocks.Clear();
        currentMouseFocusedBlocks.Clear();
        focusRemovalBuffer.Clear();
        mouseFocusRemovalBuffer.Clear();
        mouseFocusBlocks.Clear();
        UpdateSelectedWorkableRangeVisuals(null);
        singleFocusedBlockBuffer.Clear();
    }

    private TerrainGenerator ResolveTerrainGenerator()
    {
        if (cachedTerrainGenerator != null)
        {
            return cachedTerrainGenerator;
        }

        cachedTerrainGenerator = TerrainGenerator.ResolveActive();
        return cachedTerrainGenerator;
    }

    private InstallationPlacementController ResolveInstallationPlacementController()
    {
        if (cachedInstallationPlacementController != null)
        {
            return cachedInstallationPlacementController;
        }

        cachedInstallationPlacementController = FindObjectOfType<InstallationPlacementController>();
        return cachedInstallationPlacementController;
    }

    public void SetTemporaryDropFocus(Block block)
    {
        if (block == null)
        {
            ClearTemporaryDropFocus();
            return;
        }

        if (IsTemporaryDropFocusBlockedByMode())
        {
            ClearTemporaryDropFocus();
            return;
        }

        if (temporaryDropFocusBlock != null && temporaryDropFocusBlock != block)
        {
            ClearTemporaryDropFocus();
        }

        temporaryDropFocusBlock = block;
        temporaryDropFocusUntilTime = Time.time + TemporaryDropFocusDuration;
        temporaryDropFocusBlock.SetFocusVisible(true);
    }

    public void ClearTemporaryDropFocus()
    {
        if (temporaryDropFocusBlock == null && temporaryDropFocusUntilTime <= 0f)
        {
            return;
        }

        Block previousDropFocusBlock = temporaryDropFocusBlock;
        temporaryDropFocusBlock = null;
        temporaryDropFocusUntilTime = 0f;
        if (previousDropFocusBlock != null && !currentFocusedBlocks.Contains(previousDropFocusBlock))
        {
            previousDropFocusBlock.SetFocusVisible(false);
        }
    }

    private bool IsTemporaryDropFocusBlockedByMode()
    {
        if (GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked)
        {
            return true;
        }

        InstallationPlacementController placementController = ResolveInstallationPlacementController();
        return placementController != null && placementController.PlacementOrMapEditModeActive;
    }

    private void Update()
    {
        player?.UpdateDropExitGate(transform.position);

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

        if (!isInteractionLocked)
        {
            input = Vector2.ClampMagnitude(input + GetKeyboardMoveInput(), 1f);
        }

        Vector3 moveDirection = GetMoveDirection(input);
        bool hasMovement = moveDirection.sqrMagnitude > 0.0001f;

        if (hasMovement)
        {
            pendingFacingDirection = moveDirection;
            hasPendingFacingDirection = true;
        }

        pendingMoveDirection = moveDirection;

        if (isInteractionLocked)
        {
            SetMouseFocusedBlocks(null);
            HandleInstallationPlacementLock();
            wasInstallationPlacementActive = true;
            return;
        }

        if (wasInstallationPlacementActive)
        {
            wasInstallationPlacementActive = false;
        }

        if (hasMovement)
        {
            if (cachedRigidbody == null)
            {
                transform.position += moveDirection * player.Stat.currentMoveSpeed * Time.deltaTime;
            }
        }

        UpdateBodyRotation();

        player.UpdateCarryState();
        if (player.IsCarrying || hasMovement)
        {
            CancelActiveResourceHarvest();
        }

        bool finishedPickThisFrame = player.UpdateAnimationState(hasMovement);
        ResolveCompletedPick(finishedPickThisFrame);
        RefreshInteractionFocus(hasMovement);
        RefreshMouseMapObjectFocus();
        ClearInactiveResourceHarvestTarget();
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

        if (ResolveTerrainGenerator() == null)
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

        if (standingBlock.IsCornerConveyorBlock())
        {
            if (!standingBlock.TryGetConveyorCarryVelocity(samplePosition, out Vector3 carryVelocity))
            {
                return false;
            }

            carryDelta = carryVelocity * deltaTime;
            if (carryDelta.sqrMagnitude <= 0.0000001f)
            {
                return false;
            }

            Block resultingBlock = standingBlock;
            Vector3 predictedPosition = samplePosition + carryDelta;
            float switchDistanceSqr = ConveyorStandingHandoffDistance * ConveyorStandingHandoffDistance;
            if (standingBlock.TryGetNextConnectedConveyorBlock(out Block nextBlock)
                && nextBlock != null
                && nextBlock.TryGetConveyorStandingDistanceSqr(predictedPosition, out float nextDistanceSqr)
                && nextDistanceSqr <= switchDistanceSqr)
            {
                resultingBlock = nextBlock;
            }

            UpdateStandingConveyorCoordinateAfterCarry(standingBlock, resultingBlock, predictedPosition);
            return true;
        }

        if (!standingBlock.TryGetConveyorCarryDeltaWithHandoff(samplePosition, deltaTime, out Block resolvedResultingBlock, out carryDelta))
        {
            return false;
        }

        UpdateStandingConveyorCoordinateAfterCarry(standingBlock, resolvedResultingBlock, samplePosition + carryDelta);

        return true;
    }

    private void UpdateStandingConveyorCoordinateAfterCarry(Block standingBlock, Block resultingBlock, Vector3 predictedPosition)
    {
        float switchDistanceSqr = ConveyorStandingHandoffDistance * ConveyorStandingHandoffDistance;

        if (resultingBlock != null
            && resultingBlock.TryGetConveyorStandingDistanceSqr(predictedPosition, out float resultingDistanceSqr)
            && resultingDistanceSqr <= switchDistanceSqr)
        {
            hasStandingConveyorCoordinate = true;
            standingConveyorCoordinate = resultingBlock.Coordinate;
            return;
        }

        if (standingBlock != null
            && standingBlock.TryGetConveyorStandingDistanceSqr(predictedPosition, out float standingDistanceSqr)
            && standingDistanceSqr <= switchDistanceSqr)
        {
            hasStandingConveyorCoordinate = true;
            standingConveyorCoordinate = standingBlock.Coordinate;
            return;
        }

        hasStandingConveyorCoordinate = false;
        standingConveyorCoordinate = default;
    }

    private void HandleInstallationPlacementLock()
    {
        pendingMoveDirection = Vector3.zero;
        pendingFacingDirection = Vector3.zero;
        hasPendingFacingDirection = false;

        if (joystick != null)
        {
            joystick.ResetInput();
        }

        CancelPendingHarvest();
        currentTargetResource = null;
        ClearTemporaryDropFocus();
        SetFocusedBlock(null);
        resourceWorkGauge?.HideIfNotFinishing();
        player.StopImmediateActions();
        player.UpdateCarryState();
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

            if (!QueueResourceHarvestStep(currentTargetResource))
            {
                SetFocusedBlock(null);
                currentTargetResource = null;
                resourceWorkGauge?.HideIfNotFinishing();
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

    private void CancelActiveResourceHarvest()
    {
        if (currentTargetResource == null && pendingHarvestResources.Count == 0)
        {
            return;
        }

        CancelPendingHarvest();
        currentTargetResource = null;
        SetFocusedBlock(null);
        resourceWorkGauge?.HideIfNotFinishing();
    }

    private void ClearInactiveResourceHarvestTarget()
    {
        if (currentTargetResource == null || pendingHarvestResources.Count > 0)
        {
            return;
        }

        if (!currentTargetResource.CanHarvest
            || currentTargetResource.OwningBlock == null
            || !currentFocusedBlocks.Contains(currentTargetResource.OwningBlock))
        {
            currentTargetResource = null;
            resourceWorkGauge?.HideIfNotFinishing();
        }
    }

    private Resource FindBestResourceInteractionTarget()
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
            if (resource == null
                || !resource.gameObject.activeInHierarchy
                || !resource.AllowsFocus
                || !resource.CanHarvest)
            {
                continue;
            }

            Block owningBlock = ResolveResourceOwningBlock(resource);
            if (owningBlock == null)
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
            if (facingDot < resourceInteractionFacingDot)
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

    private static Block ResolveResourceOwningBlock(Resource resource)
    {
        if (resource == null)
        {
            return null;
        }

        Block owningBlock = resource.OwningBlock != null
            ? resource.OwningBlock
            : resource.GetComponentInParent<Block>();
        if (owningBlock != null && owningBlock.MapObject != null && owningBlock.MapObject != resource)
        {
            return null;
        }

        if (owningBlock != null && resource.OwningBlock == null)
        {
            resource.SetOwningBlock(owningBlock);
        }

        return owningBlock;
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
        ExpireTemporaryDropFocusIfNeeded();

        TryGetStandingConveyorFocusBlock(out Block standingConveyorFocusBlock);

        combinedInteractionFocusBlocks.Clear();
        AppendUniqueBlock(combinedInteractionFocusBlocks, standingConveyorFocusBlock);
        if (!player.IsCarrying)
        {
            Resource resourceInteractionTarget = FindBestResourceInteractionTarget();
            if (resourceInteractionTarget != null)
            {
                AppendUniqueBlock(combinedInteractionFocusBlocks, ResolveResourceOwningBlock(resourceInteractionTarget));
            }
        }

        InteractionFocusCandidate nearestFocusCandidate = CreateEmptyInteractionFocusCandidate();
        if (FindCurrentInputOutputModuleFocusBlocks(nearbyInputOutputModuleFocusBlocks, ref nearestFocusCandidate))
        {
            AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInputOutputModuleFocusBlocks);
        }

        FindNearbyWorkableBlocks(nearbyWorkableFocusBlocks, ref nearestFocusCandidate);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyWorkableFocusBlocks);

        FindNearbyBoxBlocks(nearbyBoxFocusBlocks, ref nearestFocusCandidate);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyBoxFocusBlocks);

        FindNearbyInstallationBlocks(
            nearbyInstallationFocusBlocks,
            ref nearestFocusCandidate,
            standingConveyorFocusBlock);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInstallationFocusBlocks);

        AppendInteractionFocusCandidate(nearestFocusCandidate, combinedInteractionFocusBlocks);
        SetFocusedBlocks(combinedInteractionFocusBlocks);
    }

    private void ExpireTemporaryDropFocusIfNeeded()
    {
        if (temporaryDropFocusBlock == null)
        {
            return;
        }

        if (Time.time > temporaryDropFocusUntilTime)
        {
            ClearTemporaryDropFocus();
        }
    }

    private void RefreshTemporaryDropFocusVisibility()
    {
        if (IsTemporaryDropFocusBlockedByMode())
        {
            ClearTemporaryDropFocus();
            return;
        }

        ExpireTemporaryDropFocusIfNeeded();
        if (temporaryDropFocusBlock != null)
        {
            temporaryDropFocusBlock.SetFocusVisible(true);
        }
    }

    private bool TryGetStandingConveyorFocusBlock(out Block standingBlock)
    {
        standingBlock = null;
        if (!TryGetStandingConveyorBlock(out standingBlock)
            || standingBlock == null
            || !(standingBlock.MapObject is ConveyorBelt conveyorBelt)
            || conveyorBelt == null
            || !conveyorBelt.gameObject.activeInHierarchy
            || !conveyorBelt.AllowsFocus)
        {
            return false;
        }

        return true;
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
                || !workableObject.gameObject.activeInHierarchy
                || !workableObject.AllowsFocus)
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
                || !boxObject.gameObject.activeInHierarchy
                || !boxObject.AllowsFocus)
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
                || !conveyorBelt.gameObject.activeInHierarchy
                || !conveyorBelt.AllowsFocus)
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

    public bool TryGetFocusedRobotArm(out RobotArm focusedRobotArm)
    {
        focusedRobotArm = null;
        if (currentFocusedBlocks.Count == 0 || player == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Block block in currentFocusedBlocks)
        {
            if (block == null
                || !TryResolveRobotArm(block.MapObject, out RobotArm robotArm)
                || robotArm == null
                || !robotArm.gameObject.activeInHierarchy
                || !robotArm.AllowsFocus)
            {
                continue;
            }

            float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(robotArm, block, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedRobotArm = robotArm;
        }

        return focusedRobotArm != null;
    }

    public bool TryGetFocusedFenceDoor(out FenceDoor focusedFenceDoor)
    {
        focusedFenceDoor = null;
        if (currentFocusedBlocks.Count == 0 || player == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Block block in currentFocusedBlocks)
        {
            if (block == null
                || !(block.MapObject is FenceDoor fenceDoor)
                || fenceDoor == null
                || !fenceDoor.gameObject.activeInHierarchy
                || !fenceDoor.AllowsFocus)
            {
                continue;
            }

            float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(fenceDoor, block, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedFenceDoor = fenceDoor;
        }

        return focusedFenceDoor != null;
    }

    public bool TryGetFocusedResource(out Resource focusedResource)
    {
        focusedResource = null;
        if (currentFocusedBlocks.Count == 0 || player == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Block block in currentFocusedBlocks)
        {
            Resource resource = block != null ? block.Resource : null;
            if (resource == null
                || !resource.gameObject.activeInHierarchy
                || !resource.AllowsFocus
                || !resource.CanHarvest)
            {
                continue;
            }

            float distanceSqr = GetResourceFocusSelectionDistanceSqr(resource, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedResource = resource;
        }

        return focusedResource != null;
    }

    public bool TryGetFocusedMapObject(out MapObject focusedMapObject)
    {
        focusedMapObject = null;
        if (currentFocusedBlocks.Count == 0 || player == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Block block in currentFocusedBlocks)
        {
            MapObject mapObject = block != null ? block.MapObject : null;
            if (mapObject == null && block != null)
            {
                mapObject = block.Resource;
            }

            if (mapObject == null
                || !mapObject.gameObject.activeInHierarchy
                || !mapObject.AllowsFocus)
            {
                continue;
            }

            float distanceSqr = mapObject is Resource resource
                ? GetResourceFocusSelectionDistanceSqr(resource, origin)
                : GetMapObjectFocusSelectionDistanceSqr(mapObject, block, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedMapObject = mapObject;
        }

        return focusedMapObject != null;
    }

    public bool TryGetMouseFocusedMapObject(out MapObject focusedMapObject)
    {
        RefreshMouseMapObjectFocus();
        focusedMapObject = currentMouseFocusedMapObject;
        return focusedMapObject != null
               && focusedMapObject.gameObject.activeInHierarchy
               && focusedMapObject.AllowsFocus;
    }

    public bool RequestFocusedResourceHarvest(Resource resource)
    {
        if (resource == null
            || !resource.CanHarvest
            || player == null
            || player.IsCarrying)
        {
            return false;
        }

        if (!TryGetFocusedResource(out Resource focusedResource) || focusedResource != resource)
        {
            return false;
        }

        if (currentTargetResource == resource && pendingHarvestResources.Count > 0)
        {
            return true;
        }

        if (currentTargetResource != resource)
        {
            CancelPendingHarvest();
            currentTargetResource = resource;
        }

        if (!QueueResourceHarvestStep(resource))
        {
            currentTargetResource = null;
            resourceWorkGauge?.HideIfNotFinishing();
            return false;
        }

        return true;
    }

    private bool QueueResourceHarvestStep(Resource resource)
    {
        if (resource == null
            || !resource.CanHarvest
            || player == null
            || player.IsCarrying)
        {
            return false;
        }

        int harvestPower = GetHarvestPower(resource);
        if (!resource.PrepareManualHarvestStep(harvestPower))
        {
            return false;
        }

        pendingHarvestResources.Enqueue(resource);
        player.QueuePickAnimation();
        SetFocusedBlock(resource.OwningBlock);
        resourceWorkGauge?.Bind(resource);
        return true;
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
            if (mapObject == null || !mapObject.gameObject.activeInHierarchy || !mapObject.AllowsFocus)
            {
                continue;
            }

            bool supportsItemFilter = IsItemFilterEnabled(mapObject.ResolveItemId(), definitions)
                                      || TryResolveRobotArm(mapObject, out _);
            if (!supportsItemFilter)
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

    private static bool TryResolveRobotArm(MapObject mapObject, out RobotArm robotArm)
    {
        robotArm = null;
        if (mapObject == null)
        {
            return false;
        }

        robotArm = mapObject as RobotArm;
        if (robotArm != null)
        {
            return true;
        }

        if (mapObject.TryGetComponent(out robotArm) && robotArm != null)
        {
            return true;
        }

        robotArm = mapObject.GetComponentInChildren<RobotArm>(true);
        return robotArm != null;
    }

    private bool FindCurrentInputOutputModuleFocusBlocks(List<Block> results, ref InteractionFocusCandidate nearestFocusCandidate)
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

        if (ResolveTerrainGenerator() == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector3 focusForward = GetInteractionFocusForward();
        Vector2Int playerCoordinate = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));

        if (!TryResolveStandingInputOutputModule(playerCoordinate, results, out InputOutputModule inputOutputModule))
        {
            return false;
        }

        if (!inputOutputModule.AllowsFocus)
        {
            results.Clear();
            return false;
        }

        IReadOnlyList<Vector2Int> focusCoordinates = inputOutputModule.RuntimeFocusCoordinates;
        if (focusCoordinates == null || focusCoordinates.Count <= 0)
        {
            return results.Count > 0;
        }

        if (inputOutputModule.FocusMode == MapObject.MultiFocusMode.NearOne)
        {
            Block nearestBlock = null;
            float nearestScore = float.MaxValue;

            for (int i = 0; i < focusCoordinates.Count; i++)
            {
                if (!cachedTerrainGenerator.TryGetLoadedBlock(focusCoordinates[i], out Block block) || block == null)
                {
                    continue;
                }

                float score = GetBlockFocusSelectionScore(block, origin, focusForward, out _);
                if (score >= nearestScore)
                {
                    continue;
                }

                nearestScore = score;
                nearestBlock = block;
            }

            if (nearestBlock != null)
            {
                TrySetInteractionFocusCandidate(ref nearestFocusCandidate, nearestScore, nearestBlock);
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

    private bool TryResolveStandingInputOutputModule(
        Vector2Int playerCoordinate,
        List<Block> focusBlocks,
        out InputOutputModule inputOutputModule)
    {
        if (InputOutputModule.TryGetModuleAtRuntimeAreaCoordinate(playerCoordinate, out inputOutputModule)
            && inputOutputModule != null)
        {
            TryAppendFocusBlock(focusBlocks, playerCoordinate);
            return true;
        }

        return InputOutputModule.TryGetModuleAtRuntimeGridCoordinate(playerCoordinate, out inputOutputModule)
            && inputOutputModule != null;
    }

    private void FindNearbyWorkableBlocks(List<Block> results, ref InteractionFocusCandidate nearestFocusCandidate)
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

        if (ResolveTerrainGenerator() == null)
        {
            return;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector3 focusForward = GetInteractionFocusForward();
        float globalWorkablePadding = Mathf.Max(0f, WorkableObject.GlobalMaxFocusActivationRadius);
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(globalWorkablePadding + 1f));
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        nearbyWorkableObjects.Clear();

        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (!(block.MapObject is WorkableObject workableObject)
                    || workableObject == null
                    || !workableObject.gameObject.activeInHierarchy
                    || !workableObject.AllowsFocus)
                {
                    continue;
                }

                if (nearbyWorkableObjects.Contains(workableObject))
                {
                    continue;
                }

                nearbyWorkableObjects.Add(workableObject);

                float score = GetWorkableFocusSelectionScore(workableObject, block, origin, focusForward, out float distanceSqr);
                if (!workableObject.ContainsWorldPositionInWorkableRange(origin))
                {
                    continue;
                }

                if (workableObject.FocusMode == MapObject.MultiFocusMode.NearOne)
                {
                    TrySetInteractionFocusCandidate(ref nearestFocusCandidate, score, workableObject, block);
                    continue;
                }

                AppendMapObjectFocusBlocks(workableObject, block, results);
            }
        }
    }

    private void FindNearbyBoxBlocks(List<Block> results, ref InteractionFocusCandidate nearestFocusCandidate)
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

        if (ResolveTerrainGenerator() == null)
        {
            return;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector3 focusForward = GetInteractionFocusForward();
        float globalBoxPadding = Mathf.Max(0f, BoxObject.GlobalMaxFocusActivationRadius);
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(globalBoxPadding + 2f));
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        nearbyBoxObjects.Clear();

        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (!(block.MapObject is BoxObject boxObject)
                    || boxObject == null
                    || !boxObject.gameObject.activeInHierarchy
                    || !boxObject.AllowsFocus)
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

                float score = GetMapObjectFocusSelectionScore(boxObject, block, origin, focusForward, out _);
                if (boxObject.FocusMode == MapObject.MultiFocusMode.NearOne)
                {
                    TrySetInteractionFocusCandidate(ref nearestFocusCandidate, score, boxObject, block);
                    continue;
                }

                AppendMapObjectFocusBlocks(boxObject, block, results);
            }
        }
    }

    private void FindNearbyInstallationBlocks(
        List<Block> results,
        ref InteractionFocusCandidate nearestFocusCandidate,
        Block standingConveyorFocusBlock = null)
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

        if (ResolveTerrainGenerator() == null)
        {
            return;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        Vector3 focusForward = GetInteractionFocusForward();
        float globalInstallationPadding = Mathf.Max(0f, InstallationObject.GlobalMaxFocusActivationRadius);
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(globalInstallationPadding + 2f));
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        nearbyInstallationObjects.Clear();

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
                    || !installationObject.AllowsFocus
                    || installationObject is WorkableObject
                    || installationObject is BoxObject)
                {
                    continue;
                }

                if (nearbyInstallationObjects.Contains(installationObject))
                {
                    continue;
                }

                if (standingConveyorFocusBlock != null && installationObject is ConveyorBelt)
                {
                    continue;
                }

                nearbyInstallationObjects.Add(installationObject);

                float focusRadius = Mathf.Max(0f, installationObject.FocusActivationRadius);
                if (focusRadius <= 0f)
                {
                    continue;
                }

                float score = GetMapObjectFocusSelectionScore(installationObject, block, origin, focusForward, out float distanceSqr);
                if (distanceSqr > focusRadius * focusRadius)
                {
                    continue;
                }

                if (installationObject.FocusMode == MapObject.MultiFocusMode.NearOne)
                {
                    TrySetInteractionFocusCandidate(ref nearestFocusCandidate, score, installationObject, block);
                    continue;
                }

                AppendMapObjectFocusBlocks(installationObject, block, results);
            }
        }
    }

    private float GetWorkableFocusDistanceSqr(WorkableObject workableObject, Block block, Vector3 origin)
    {
        Vector3 focusPoint = GetWorkableFocusPoint(workableObject, block, origin);
        Vector3 offset = focusPoint - origin;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private float GetWorkableFocusSelectionScore(WorkableObject workableObject, Block block, Vector3 origin, Vector3 focusForward, out float distanceSqr)
    {
        Vector3 focusPoint = GetWorkableFocusPoint(workableObject, block, origin);
        Vector3 offset = focusPoint - origin;
        offset.y = 0f;
        distanceSqr = offset.sqrMagnitude;
        return GetFacingAdjustedFocusScore(distanceSqr, focusPoint, origin, focusForward);
    }

    private static Vector3 GetWorkableFocusPoint(WorkableObject workableObject, Block block, Vector3 origin)
    {
        Vector3 focusPoint;
        if (workableObject != null)
        {
            focusPoint = workableObject.transform.position;
        }
        else if (block != null)
        {
            focusPoint = block.transform.position;
        }
        else
        {
            focusPoint = origin;
        }

        focusPoint.y = origin.y;
        return focusPoint;
    }

    private float GetMapObjectFocusSelectionDistanceSqr(MapObject mapObject, Block block, Vector3 origin)
    {
        return GetMapObjectFocusDistanceSqr(mapObject, block, origin, 0f);
    }

    private float GetMapObjectFocusSelectionScore(MapObject mapObject, Block block, Vector3 origin, Vector3 focusForward, out float distanceSqr)
    {
        Bounds bounds = GetMapObjectFocusBounds(mapObject, block);
        Vector3 focusPoint = GetFocusPointAndDistance(bounds, origin, out distanceSqr);
        return GetFacingAdjustedFocusScore(distanceSqr, focusPoint, origin, focusForward);
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
                if (rendererComponent == null
                    || !rendererComponent.enabled
                    || rendererComponent.GetComponent<WorkableObjectRangeVisual>() != null)
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

    private Vector3 GetInteractionFocusForward()
    {
        Transform rotationTarget = player != null && player.BodyTransform != null ? player.BodyTransform : transform;
        Vector3 forward = rotationTarget != null ? rotationTarget.forward : Vector3.zero;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = pendingMoveDirection;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private Vector3 GetFocusPointAndDistance(Bounds bounds, Vector3 origin, out float distanceSqr)
    {
        Vector3 focusPoint = bounds.ClosestPoint(origin);
        focusPoint.y = origin.y;

        Vector3 offset = focusPoint - origin;
        offset.y = 0f;
        distanceSqr = offset.sqrMagnitude;

        if (distanceSqr <= 0.0001f)
        {
            focusPoint = bounds.center;
            focusPoint.y = origin.y;
        }

        return focusPoint;
    }

    private float GetFacingAdjustedFocusScore(float distanceSqr, Vector3 focusPoint, Vector3 origin, Vector3 focusForward)
    {
        float facingScoreWeight = multiFocusFacingScoreWeight > 0f
            ? multiFocusFacingScoreWeight
            : DefaultMultiFocusFacingScoreWeight;
        if (distanceSqr >= float.MaxValue)
        {
            return distanceSqr;
        }

        Vector3 offset = focusPoint - origin;
        offset.y = 0f;
        if (offset.sqrMagnitude <= 0.0001f || focusForward.sqrMagnitude <= 0.0001f)
        {
            return distanceSqr;
        }

        float facingDot = Mathf.Clamp(Vector3.Dot(focusForward.normalized, offset.normalized), -1f, 1f);
        return distanceSqr + ((1f - facingDot) * facingScoreWeight);
    }

    private static float GetResourceFocusSelectionDistanceSqr(Resource resource, Vector3 origin)
    {
        if (resource == null)
        {
            return float.MaxValue;
        }

        Vector3 focusPoint = resource.FocusPoint;
        focusPoint.y = origin.y;
        Vector3 offset = focusPoint - origin;
        offset.y = 0f;
        return offset.sqrMagnitude;
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

    private static InteractionFocusCandidate CreateEmptyInteractionFocusCandidate()
    {
        return new InteractionFocusCandidate
        {
            score = float.MaxValue
        };
    }

    private static void TrySetInteractionFocusCandidate(ref InteractionFocusCandidate candidate, float score, Block singleBlock)
    {
        if (singleBlock == null || !(score < candidate.score))
        {
            return;
        }

        candidate.hasCandidate = true;
        candidate.useSingleBlock = true;
        candidate.score = score;
        candidate.mapObject = null;
        candidate.fallbackBlock = null;
        candidate.singleBlock = singleBlock;
    }

    private static void TrySetInteractionFocusCandidate(ref InteractionFocusCandidate candidate, float score, MapObject mapObject, Block fallbackBlock)
    {
        if (mapObject == null || !(score < candidate.score))
        {
            return;
        }

        candidate.hasCandidate = true;
        candidate.useSingleBlock = false;
        candidate.score = score;
        candidate.mapObject = mapObject;
        candidate.fallbackBlock = fallbackBlock;
        candidate.singleBlock = null;
    }

    private bool AppendInteractionFocusCandidate(InteractionFocusCandidate candidate, List<Block> results)
    {
        if (!candidate.hasCandidate || results == null)
        {
            return false;
        }

        if (candidate.useSingleBlock)
        {
            if (candidate.singleBlock == null || results.Contains(candidate.singleBlock))
            {
                return false;
            }

            results.Add(candidate.singleBlock);
            return true;
        }

        return AppendMapObjectFocusBlocks(candidate.mapObject, candidate.fallbackBlock, results);
    }

    private bool AppendMapObjectFocusBlocks(MapObject mapObject, Block fallbackBlock, List<Block> results)
    {
        if (mapObject == null || results == null || !mapObject.AllowsFocus)
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

        if (ResolveTerrainGenerator() == null
            || !cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || results.Contains(block))
        {
            return false;
        }

        results.Add(block);
        return true;
    }

    private void RefreshMouseMapObjectFocus()
    {
        if (GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked)
        {
            SetMouseFocusedBlocks(null);
            return;
        }

        Vector2 pointerPosition = Input.mousePosition;
        if (IsPointerOverMouseFocusBlockingUi(pointerPosition)
            || !TryResolveMouseFocusedMapObject(pointerPosition, out MapObject mapObject, out Block fallbackBlock))
        {
            SetMouseFocusedBlocks(null);
            return;
        }

        mouseFocusBlocks.Clear();
        if (!AppendMapObjectFocusBlocks(mapObject, fallbackBlock, mouseFocusBlocks))
        {
            SetMouseFocusedBlocks(null);
            return;
        }

        SetMouseFocusedBlocks(mouseFocusBlocks, mapObject);
    }

    private bool TryResolveMouseFocusedMapObject(Vector2 pointerPosition, out MapObject mapObject, out Block fallbackBlock)
    {
        mapObject = null;
        fallbackBlock = null;

        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        float maxDistance = targetCamera.farClipPlane > 0f ? targetCamera.farClipPlane : 512f;
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            Mathf.Max(0f, maxDistance),
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, CompareRaycastHits);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                MapObject candidate = hitCollider.GetComponentInParent<MapObject>();
                if (!IsValidMouseFocusMapObject(candidate))
                {
                    continue;
                }

                mapObject = candidate;
                TryResolveMouseFocusFallbackBlock(candidate, ray, out fallbackBlock);
                return true;
            }
        }

        if (!TryGetPointerBlockFromGroundPlane(ray, out fallbackBlock))
        {
            return false;
        }

        mapObject = fallbackBlock.MapObject != null ? fallbackBlock.MapObject : fallbackBlock.Resource;
        if (!IsValidMouseFocusMapObject(mapObject))
        {
            mapObject = null;
            fallbackBlock = null;
            return false;
        }

        return true;
    }

    private static bool IsValidMouseFocusMapObject(MapObject mapObject)
    {
        return mapObject != null
               && mapObject.gameObject.activeInHierarchy
               && mapObject.AllowsFocus;
    }

    private bool TryResolveMouseFocusFallbackBlock(MapObject mapObject, Ray ray, out Block fallbackBlock)
    {
        fallbackBlock = null;
        if (mapObject == null)
        {
            return false;
        }

        if (mapObject is Resource resource && resource.OwningBlock != null)
        {
            fallbackBlock = resource.OwningBlock;
            return true;
        }

        if (mapObject is InstallationObject installationObject)
        {
            IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject.RuntimeOccupiedCoordinates;
            if (occupiedCoordinates != null)
            {
                TerrainGenerator terrain = ResolveTerrainGenerator();
                for (int i = 0; i < occupiedCoordinates.Count; i++)
                {
                    if (terrain != null
                        && terrain.TryGetLoadedBlock(occupiedCoordinates[i], out Block block)
                        && block != null)
                    {
                        fallbackBlock = block;
                        return true;
                    }
                }
            }
        }

        fallbackBlock = mapObject.GetComponentInParent<Block>();
        if (fallbackBlock != null)
        {
            return true;
        }

        return TryGetPointerBlockFromGroundPlane(ray, out fallbackBlock);
    }

    private bool TryGetPointerBlockFromGroundPlane(Ray ray, out Block block)
    {
        block = null;
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null)
        {
            return false;
        }

        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, terrain.transform.position.y, 0f));
        if (!groundPlane.Raycast(ray, out float enter))
        {
            return false;
        }

        Vector3 worldPoint = ray.GetPoint(enter);
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPoint.x),
            Mathf.RoundToInt(worldPoint.z));

        return terrain.TryGetLoadedBlock(coordinate, out block) && block != null;
    }

    private bool IsPointerOverMouseFocusBlockingUi(Vector2 pointerPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        if (pointerEventData == null)
        {
            pointerEventData = new PointerEventData(EventSystem.current);
        }

        pointerEventData.Reset();
        pointerEventData.position = pointerPosition;
        pointerRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerEventData, pointerRaycastResults);
        for (int i = 0; i < pointerRaycastResults.Count; i++)
        {
            GameObject hitObject = pointerRaycastResults[i].gameObject;
            if (hitObject == null)
            {
                continue;
            }

            if (hitObject.GetComponentInParent<Selectable>() != null)
            {
                pointerRaycastResults.Clear();
                return true;
            }
        }

        pointerRaycastResults.Clear();
        return false;
    }

    private static int CompareRaycastHits(RaycastHit left, RaycastHit right)
    {
        return left.distance.CompareTo(right.distance);
    }

    private void SetMouseFocusedBlocks(List<Block> nextBlocks, MapObject nextMapObject = null)
    {
        currentMouseFocusedMapObject = nextBlocks != null && nextBlocks.Count > 0 ? nextMapObject : null;
        mouseFocusRemovalBuffer.Clear();

        foreach (Block currentBlock in currentMouseFocusedBlocks)
        {
            if (ContainsFocusedBlock(nextBlocks, currentBlock))
            {
                continue;
            }

            mouseFocusRemovalBuffer.Add(currentBlock);
        }

        for (int i = 0; i < mouseFocusRemovalBuffer.Count; i++)
        {
            Block block = mouseFocusRemovalBuffer[i];
            currentMouseFocusedBlocks.Remove(block);
            if (block != null)
            {
                block.SetMouseFocusVisible(false);
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

            if (currentMouseFocusedBlocks.Add(block))
            {
                block.SetMouseFocusVisible(true);
            }
            else
            {
                block.SetMouseFocusVisible(true);
            }
        }
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

    private float GetBlockFocusSelectionScore(Block block, Vector3 origin, Vector3 focusForward, out float distanceSqr)
    {
        if (block == null)
        {
            distanceSqr = float.MaxValue;
            return float.MaxValue;
        }

        Vector3 focusPoint = block.transform.position;
        focusPoint.y = origin.y;
        Vector3 offset = focusPoint - origin;
        offset.y = 0f;
        distanceSqr = offset.sqrMagnitude;
        return GetFacingAdjustedFocusScore(distanceSqr, focusPoint, origin, focusForward);
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
            UpdateSelectedWorkableRangeVisuals(null);
            RefreshTemporaryDropFocusVisibility();
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

        UpdateSelectedWorkableRangeVisuals(nextBlocks);
        RefreshTemporaryDropFocusVisibility();
    }

    private void UpdateSelectedWorkableRangeVisuals(List<Block> nextBlocks)
    {
        nextSelectedWorkableRangeObjects.Clear();

        if (nextBlocks != null)
        {
            for (int i = 0; i < nextBlocks.Count; i++)
            {
                Block block = nextBlocks[i];
                if (block == null
                    || !(block.MapObject is WorkableObject workableObject)
                    || workableObject == null)
                {
                    continue;
                }

                nextSelectedWorkableRangeObjects.Add(workableObject);
            }
        }

        selectedWorkableRangeRemovalBuffer.Clear();
        foreach (WorkableObject workableObject in currentSelectedWorkableRangeObjects)
        {
            if (workableObject != null && nextSelectedWorkableRangeObjects.Contains(workableObject))
            {
                continue;
            }

            selectedWorkableRangeRemovalBuffer.Add(workableObject);
        }

        for (int i = 0; i < selectedWorkableRangeRemovalBuffer.Count; i++)
        {
            WorkableObject workableObject = selectedWorkableRangeRemovalBuffer[i];
            currentSelectedWorkableRangeObjects.Remove(workableObject);
            if (workableObject != null)
            {
                workableObject.SetSelectedRangeVisualRequested(false);
            }
        }

        foreach (WorkableObject workableObject in nextSelectedWorkableRangeObjects)
        {
            if (workableObject == null || !currentSelectedWorkableRangeObjects.Add(workableObject))
            {
                continue;
            }

            workableObject.SetSelectedRangeVisualRequested(true);
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

    private static bool AppendUniqueBlock(List<Block> target, Block block)
    {
        if (target == null || block == null || target.Contains(block))
        {
            return false;
        }

        target.Add(block);
        return true;
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

    private void UpdateBodyRotation()
    {
        if (!hasPendingFacingDirection)
        {
            return;
        }

        if (RotateBodyTowards(pendingFacingDirection))
        {
            hasPendingFacingDirection = false;
            pendingFacingDirection = Vector3.zero;
        }
    }

    private bool RotateBodyTowards(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude <= 0.0001f || player == null)
        {
            return true;
        }

        Transform rotationTarget = player.BodyTransform != null ? player.BodyTransform : transform;
        Quaternion targetRotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
        float remainingAngle = Quaternion.Angle(rotationTarget.rotation, targetRotation);
        if (remainingAngle <= 0.1f)
        {
            rotationTarget.rotation = targetRotation;
            return true;
        }

        float interpolation = 1f - Mathf.Exp(-Mathf.Max(0.01f, rotationInterpolationSpeed) * Time.deltaTime);
        float maxDegrees = Mathf.Max(0f, player.Stat.rotateSpeed) * Time.deltaTime;
        if (maxDegrees <= 0f)
        {
            return false;
        }

        float stepDegrees = Mathf.Min(maxDegrees, remainingAngle * interpolation);
        rotationTarget.rotation = Quaternion.RotateTowards(rotationTarget.rotation, targetRotation, stepDegrees);
        if (Quaternion.Angle(rotationTarget.rotation, targetRotation) <= 0.1f)
        {
            rotationTarget.rotation = targetRotation;
            return true;
        }

        return false;
    }

    private static Vector2 GetKeyboardMoveInput()
    {
        Vector2 input = Vector2.zero;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            input.x -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            input.x += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            input.y -= 1f;
        }

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            input.y += 1f;
        }

        return input.sqrMagnitude > 1f ? input.normalized : input;
    }

}
