using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Serialization;

[RequireComponent(typeof(Player))]
public class PlayerController : MonoBehaviour
{
    private const int Belt2FDefaultFootprintWidth = 1;
    private const int Belt2FDefaultFootprintLength = 3;

    private const float PlayerRootY = 0f;
    private const float PlayerRootYEpsilon = 0.0001f;
    private const float ConveyorStandingHeight = 0.2f;
    private const float ConveyorStandingSmoothTime = 0.08f;
    private const float ConveyorStandingEnterDistance = 0.08f;
    private const float ConveyorStandingExitDistance = 0.12f;
    private const float ConveyorStandingHandoffDistance = 0.2f;
    private const float ConveyorCarryAcceleration = 8f;
    private const float ConveyorCarryDeceleration = 10f;
    private const float MinPhysicsMoveDistance = 0.00001f;
    private const float MinPhysicsMoveDistanceSqr = MinPhysicsMoveDistance * MinPhysicsMoveDistance;
    private const float WaterBoundarySkin = 0.005f;
    private const int WaterMoveClampIterations = 5;
    private const float WaterBoundaryNormalProbeDistance = 0.05f;
    private const float WaterBoundarySlideScoreTolerance = 0.01f;
    private const float DefaultMultiFocusFacingScoreWeight = 0.75f;
    private const float TemporaryDropFocusDuration = 0.18f;
    private static readonly Vector2[] WaterBoundarySampleDirections =
    {
        new Vector2(1f, 0f),
        new Vector2(0.7071068f, 0.7071068f),
        new Vector2(0f, 1f),
        new Vector2(-0.7071068f, 0.7071068f),
        new Vector2(-1f, 0f),
        new Vector2(-0.7071068f, -0.7071068f),
        new Vector2(0f, -1f),
        new Vector2(0.7071068f, -0.7071068f)
    };

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
    private readonly List<FocusMarkerGroup> focusMarkerGroups = new List<FocusMarkerGroup>();
    private int focusMarkerGroupCount;
    private readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>();
    private readonly List<InstallationObject> nearbyInstallationObjects = new List<InstallationObject>();
    private readonly List<InstallationObject> nearbyRuntimeInstallationScratch = new List<InstallationObject>(8);
    private readonly Dictionary<Block, MapObject> interactionFocusTargetOverrides = new Dictionary<Block, MapObject>();
    private readonly List<WorkableObject> nearbyWorkableObjects = new List<WorkableObject>();
    private readonly List<WorkableObject> nearbyWorkableRangeObjects = new List<WorkableObject>();
    private readonly List<BoxObject> nearbyBoxObjects = new List<BoxObject>();
    private readonly HashSet<WorkableObject> currentSelectedWorkableRangeObjects = new HashSet<WorkableObject>();
    private readonly HashSet<WorkableObject> nextSelectedWorkableRangeObjects = new HashSet<WorkableObject>();
    private readonly List<WorkableObject> selectedWorkableRangeRemovalBuffer = new List<WorkableObject>();
    private readonly List<Block> singleFocusedBlockBuffer = new List<Block>(1);
    private readonly List<Block> focusRemovalBuffer = new List<Block>();
    private readonly float[] waterBoundaryWeightBuffer = new float[8];
    private readonly float[] waterBoundaryNormalWeightBuffer = new float[8];
    private Rigidbody cachedRigidbody;
    private CapsuleCollider cachedCapsuleCollider;
    private Vector3 defaultCapsuleColliderCenter;
    private bool hasDefaultCapsuleColliderCenter;
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
    private Transform interactionPointSnapTarget;
    private Vehicle interactionPointSnapVehicle;
    private Block temporaryDropFocusBlock;
    private float temporaryDropFocusUntilTime;
    private MapObject currentMouseFocusedMapObject;
    private PointerEventData pointerEventData;
    private int nearbyWaterBiomeCacheFrame = -1;
    private Vector2Int nearbyWaterBiomeCacheCoordinate;
    private bool nearbyWaterBiomeCacheResult;

    private struct InteractionFocusCandidate
    {
        public bool hasCandidate;
        public bool useSingleBlock;
        public float score;
        public MapObject mapObject;
        public Block fallbackBlock;
        public Block singleBlock;
    }

    private sealed class FocusMarkerGroup
    {
        public MapObject mapObject;
        public Block markerBlock;
        public int count;
        private Vector2Int markerCoordinate;
        private Vector3 minWorldPosition;
        private Vector3 maxWorldPosition;

        public Vector3 Center => new Vector3(
            (minWorldPosition.x + maxWorldPosition.x) * 0.5f,
            markerBlock != null ? markerBlock.transform.position.y : (minWorldPosition.y + maxWorldPosition.y) * 0.5f,
            (minWorldPosition.z + maxWorldPosition.z) * 0.5f);

        public Vector2 Size => new Vector2(
            Mathf.Max(1f, maxWorldPosition.x - minWorldPosition.x + 1f),
            Mathf.Max(1f, maxWorldPosition.z - minWorldPosition.z + 1f));

        public void Reset(MapObject targetMapObject, Block block)
        {
            mapObject = targetMapObject;
            markerBlock = block;
            count = 0;
            markerCoordinate = block != null ? block.Coordinate : Vector2Int.zero;
            if (block != null)
            {
                Vector3 position = block.transform.position;
                minWorldPosition = position;
                maxWorldPosition = position;
                Add(block);
            }
        }

        public void Add(Block block)
        {
            if (block == null)
            {
                return;
            }

            count++;
            Vector2Int coordinate = block.Coordinate;
            if (markerBlock == null
                || coordinate.x < markerCoordinate.x
                || (coordinate.x == markerCoordinate.x && coordinate.y < markerCoordinate.y))
            {
                markerBlock = block;
                markerCoordinate = coordinate;
            }

            Vector3 position = block.transform.position;
            minWorldPosition = Vector3.Min(minWorldPosition, position);
            maxWorldPosition = Vector3.Max(maxWorldPosition, position);
        }
    }

    public bool IsResourceHarvestingActive => currentTargetResource != null && pendingHarvestResources.Count > 0;

    private void Awake()
    {
        player = GetComponent<Player>();
        cachedRigidbody = GetComponent<Rigidbody>();
        CacheDefaultCapsuleColliderCenter();
        if (cachedRigidbody != null && cachedRigidbody.interpolation == RigidbodyInterpolation.None)
        {
            cachedRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        SnapRootToGroundY();
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
        interactionPointSnapTarget = null;
        interactionPointSnapVehicle = null;
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

    public Vehicle MountedVehicle => interactionPointSnapTarget != null ? interactionPointSnapVehicle : null;

    public bool IsMountedOnVehicle(Vehicle vehicle)
    {
        return vehicle != null
               && interactionPointSnapTarget != null
               && interactionPointSnapVehicle == vehicle;
    }

    public bool TryGetMountedVehicleState(out Vehicle vehicle, out int playerPointIndex)
    {
        vehicle = MountedVehicle;
        playerPointIndex = -1;
        return vehicle != null
               && vehicle.TryGetPlayerPointIndex(interactionPointSnapTarget, out playerPointIndex);
    }

    public bool TryRestoreMountedVehicle(Vehicle vehicle, int playerPointIndex)
    {
        if (vehicle == null)
        {
            ClearInteractionPointSnapForLoad();
            return false;
        }

        if (player == null)
        {
            player = GetComponent<Player>();
        }

        if (player == null)
        {
            ClearInteractionPointSnapForLoad();
            return false;
        }

        return vehicle.TryDockPlayerAtPoint(player, playerPointIndex);
    }

    public void ClearInteractionPointSnapForLoad()
    {
        ClearInteractionPointSnap(true);
        pendingMoveDirection = Vector3.zero;
        pendingFacingDirection = Vector3.zero;
        hasPendingFacingDirection = false;
        currentConveyorCarryVelocity = Vector3.zero;
    }

    public bool TrySnapBodyToInteractionPoint(Transform targetPoint, Vehicle vehicle = null)
    {
        if (targetPoint == null)
        {
            return false;
        }

        if (player == null)
        {
            player = GetComponent<Player>();
        }

        if (player == null)
        {
            return false;
        }

        if (cachedRigidbody == null)
        {
            cachedRigidbody = GetComponent<Rigidbody>();
        }

        pendingMoveDirection = Vector3.zero;
        pendingFacingDirection = Vector3.zero;
        hasPendingFacingDirection = false;
        currentConveyorCarryVelocity = Vector3.zero;

        if (joystick != null)
        {
            joystick.ResetInput();
        }

        CancelActiveResourceHarvest();
        ClearTemporaryDropFocus();
        SetFocusedBlocks(null);
        SetMouseFocusedBlocks(null);
        currentMouseFocusedMapObject = null;

        interactionPointSnapTarget = targetPoint;
        interactionPointSnapVehicle = vehicle;
        ApplyInteractionPointSnap();
        player.StopImmediateActions();
        player.UpdateCarryState();
        return true;
    }

    public bool TryDismountFromVehicle()
    {
        if (interactionPointSnapTarget == null)
        {
            return false;
        }

        if (player == null)
        {
            player = GetComponent<Player>();
        }

        Quaternion exitRotation = transform.rotation;
        Vector3 exitPosition = ResolveInteractionPointExitPosition();
        ClearInteractionPointSnap(true);

        pendingMoveDirection = Vector3.zero;
        pendingFacingDirection = Vector3.zero;
        hasPendingFacingDirection = false;
        currentConveyorCarryVelocity = Vector3.zero;

        if (joystick != null)
        {
            joystick.ResetInput();
        }

        if (cachedRigidbody == null)
        {
            cachedRigidbody = GetComponent<Rigidbody>();
        }

        if (cachedRigidbody != null)
        {
            cachedRigidbody.position = exitPosition;
            cachedRigidbody.rotation = exitRotation;
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(exitPosition, exitRotation);

        Transform bodyTransform = player != null && player.BodyTransform != null ? player.BodyTransform : transform;
        if (bodyTransform != null && bodyTransform != transform)
        {
            bodyTransform.rotation = exitRotation;
        }

        Physics.SyncTransforms();
        player?.StopImmediateActions();
        player?.UpdateCarryState();
        return true;
    }

    private void ClearInteractionPointSnap(bool restoreVisualOffset)
    {
        if (interactionPointSnapTarget == null)
        {
            return;
        }

        interactionPointSnapTarget = null;
        interactionPointSnapVehicle = null;
        if (restoreVisualOffset)
        {
            RestoreStandingVisualOffset();
        }
    }

    private Vector3 ResolveInteractionPointExitPosition()
    {
        Transform snapTarget = interactionPointSnapTarget;
        Vehicle vehicle = interactionPointSnapVehicle;
        if (snapTarget == null)
        {
            return ClampRootPositionToGroundY(transform.position);
        }

        Vector3 center = vehicle != null ? vehicle.transform.position : transform.position;
        Vector3 direction = snapTarget.position - center;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = snapTarget.right;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = transform.right;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = Vector3.right;
        }

        float exitDistance = 0.85f;
        if (vehicle != null)
        {
            MapObject.MapObjectStatus status = vehicle.Status;
            exitDistance = Mathf.Max(1, Mathf.Max(status.mapSizeX, status.mapSizeY)) * 0.5f + 0.55f;
        }

        return ClampRootPositionToGroundY(center + direction.normalized * exitDistance);
    }

    private void ApplyInteractionPointSnap()
    {
        if (interactionPointSnapTarget == null)
        {
            return;
        }

        if (player == null)
        {
            player = GetComponent<Player>();
        }

        if (player == null)
        {
            interactionPointSnapTarget = null;
            interactionPointSnapVehicle = null;
            return;
        }

        if (cachedRigidbody == null)
        {
            cachedRigidbody = GetComponent<Rigidbody>();
        }

        CacheDefaultBodyLocalPosition();
        ApplyStandingColliderOffset(0f);
        standingVisualOffsetVelocity = 0f;
        hasStandingConveyorCoordinate = false;
        standingConveyorCoordinate = default;

        Vector3 targetPosition = interactionPointSnapTarget.position;
        Quaternion targetRotation = interactionPointSnapTarget.rotation;

        if (cachedRigidbody != null)
        {
            cachedRigidbody.position = targetPosition;
            cachedRigidbody.rotation = targetRotation;
            cachedRigidbody.velocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(targetPosition, targetRotation);

        Transform bodyTransform = player.BodyTransform != null ? player.BodyTransform : transform;
        if (bodyTransform != null && bodyTransform != transform)
        {
            bodyTransform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        Physics.SyncTransforms();
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
        if (interactionPointSnapTarget != null)
        {
            ApplyInteractionPointSnap();
        }
        else
        {
            SnapRootToGroundY();
        }

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

        if (interactionPointSnapTarget != null)
        {
            if (interactionPointSnapVehicle != null)
            {
                float mountedMoveSpeed = player != null ? player.Stat.currentMoveSpeed : 0f;
                interactionPointSnapVehicle.HandleMountedInput(moveDirection, mountedMoveSpeed, Time.deltaTime);
                ApplyInteractionPointSnap();
            }

            moveDirection = Vector3.zero;
            hasMovement = false;
            currentConveyorCarryVelocity = Vector3.zero;
        }

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
                Vector3 startPosition = ClampRootPositionToGroundY(transform.position);
                Vector3 moveDelta = moveDirection * player.Stat.currentMoveSpeed * Time.deltaTime;
                moveDelta = ResolveWaterConstrainedMove(startPosition, moveDelta);
                transform.position = ClampRootPositionToGroundY(
                    startPosition + moveDelta);
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
        if (interactionPointSnapTarget != null)
        {
            ApplyInteractionPointSnap();
            return;
        }

        SnapRootToGroundY();

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
            rawCarryDelta = FlattenPlayerConveyorCarryDelta(rawCarryDelta);
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
        currentConveyorCarryVelocity = FlattenPlayerConveyorCarryDelta(currentConveyorCarryVelocity);

        Vector3 manualDelta = manualVelocity * Time.fixedDeltaTime;
        Vector3 carryDelta = currentConveyorCarryVelocity * Time.fixedDeltaTime;
        Vector3 totalDelta = manualDelta + carryDelta;

        ApplyStandingColliderOffset(ResolveStandingConveyorVisualOffset());

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

    private static Vector3 FlattenPlayerConveyorCarryDelta(Vector3 delta)
    {
        // The player's visible/collision height is handled by Body and Capsule offsets.
        // Keep Rigidbody motion planar so descending 2F ramps do not sweep downward into the ground.
        delta.y = 0f;
        return delta;
    }

    private void LateUpdate()
    {
        if (interactionPointSnapTarget != null)
        {
            ApplyInteractionPointSnap();
            return;
        }

        SnapRootToGroundY();
        ApplyStandingOffset();
    }

    private void MoveRigidbody(Vector3 delta)
    {
        MoveRigidbody(delta, MoveSweepBuffer);
    }

    private void MoveRigidbody(Vector3 delta, float maxSweepBuffer)
    {
        delta.y = 0f;

        float distance = delta.magnitude;
        if (distance <= MinPhysicsMoveDistance)
        {
            return;
        }

        float sweepBuffer = Mathf.Min(maxSweepBuffer, distance * 0.25f);

        Vector3 direction = delta / distance;
        Vector3 startPosition = ClampRootPositionToGroundY(cachedRigidbody.position);
        Vector3 finalMove = Vector3.zero;

        if (TryGetBlockingSweepHit(direction, distance + sweepBuffer, out RaycastHit hit))
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
                    if (!TryGetBlockingSweepHit(slideDirection, slideDistance + sweepBuffer, out _))
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

        finalMove = ResolveWaterConstrainedMove(startPosition, finalMove);

        Vector3 finalPosition = ClampRootPositionToGroundY(startPosition + finalMove);
        if (finalMove.sqrMagnitude > MinPhysicsMoveDistanceSqr)
        {
            cachedRigidbody.MovePosition(finalPosition);
        }
    }

    private Vector3 ResolveWaterConstrainedMove(Vector3 startPosition, Vector3 moveDelta)
    {
        moveDelta.y = 0f;
        if (moveDelta.sqrMagnitude <= MinPhysicsMoveDistanceSqr
            || ResolveTerrainGenerator() == null)
        {
            return moveDelta;
        }

        startPosition = ClampRootPositionToGroundY(startPosition);
        Vector3 targetPosition = ClampRootPositionToGroundY(startPosition + moveDelta);
        if (!IsPlayerBlockedByWaterAtPosition(targetPosition))
        {
            return moveDelta;
        }

        Vector3 directMove = ClampMoveBeforeWater(startPosition, moveDelta);
        Vector3 remainingMove = moveDelta - directMove;
        if (remainingMove.sqrMagnitude <= MinPhysicsMoveDistanceSqr
            || !TryEstimateWaterSurfaceNormal(startPosition + directMove, moveDelta, out Vector2 waterNormal))
        {
            return directMove;
        }

        Vector3 waterNormal3 = new Vector3(waterNormal.x, 0f, waterNormal.y);
        Vector3 slideMove = Vector3.ProjectOnPlane(remainingMove, waterNormal3);
        Vector3 slideOrigin = startPosition + directMove;
        Vector3 bestSlideMove = ClampSlideMoveAlongWaterBoundary(slideOrigin, slideMove);
        Vector3 xSlideMove = ClampSlideMoveAlongWaterBoundary(
            slideOrigin,
            new Vector3(remainingMove.x, 0f, 0f));
        Vector3 zSlideMove = ClampSlideMoveAlongWaterBoundary(
            slideOrigin,
            new Vector3(0f, 0f, remainingMove.z));

        if (xSlideMove.sqrMagnitude > bestSlideMove.sqrMagnitude)
        {
            bestSlideMove = xSlideMove;
        }

        if (zSlideMove.sqrMagnitude > bestSlideMove.sqrMagnitude)
        {
            bestSlideMove = zSlideMove;
        }

        return directMove + bestSlideMove;
    }

    private Vector3 ClampMoveBeforeWater(Vector3 startPosition, Vector3 moveDelta)
    {
        moveDelta.y = 0f;
        if (moveDelta.sqrMagnitude <= MinPhysicsMoveDistanceSqr)
        {
            return Vector3.zero;
        }

        startPosition = ClampRootPositionToGroundY(startPosition);
        if (IsPlayerBlockedByWaterAtPosition(startPosition))
        {
            return Vector3.zero;
        }

        if (!IsPlayerBlockedByWaterAtPosition(startPosition + moveDelta))
        {
            return moveDelta;
        }

        float allowed = 0f;
        float blocked = 1f;
        for (int i = 0; i < WaterMoveClampIterations; i++)
        {
            float candidate = (allowed + blocked) * 0.5f;
            if (IsPlayerBlockedByWaterAtPosition(startPosition + (moveDelta * candidate)))
            {
                blocked = candidate;
            }
            else
            {
                allowed = candidate;
            }
        }

        return moveDelta * allowed;
    }

    private Vector3 ClampSlideMoveAlongWaterBoundary(Vector3 startPosition, Vector3 moveDelta)
    {
        moveDelta.y = 0f;
        if (moveDelta.sqrMagnitude <= MinPhysicsMoveDistanceSqr)
        {
            return Vector3.zero;
        }

        startPosition = ClampRootPositionToGroundY(startPosition);
        float startWaterScore = GetPlayerWaterSurfaceMaxScore(startPosition);
        float allowedWaterScore = Mathf.Max(0f, startWaterScore + WaterBoundarySlideScoreTolerance);
        if (GetPlayerWaterSurfaceMaxScore(startPosition + moveDelta) <= allowedWaterScore)
        {
            return moveDelta;
        }

        float allowed = 0f;
        float blocked = 1f;
        for (int i = 0; i < WaterMoveClampIterations; i++)
        {
            float candidate = (allowed + blocked) * 0.5f;
            if (GetPlayerWaterSurfaceMaxScore(startPosition + (moveDelta * candidate)) <= allowedWaterScore)
            {
                allowed = candidate;
            }
            else
            {
                blocked = candidate;
            }
        }

        return moveDelta * allowed;
    }

    private bool IsPlayerBlockedByWaterAtPosition(Vector3 rootPosition)
    {
        return GetPlayerWaterSurfaceMaxScore(rootPosition) > 0f;
    }

    private float GetPlayerWaterSurfaceMaxScore(Vector3 rootPosition)
    {
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null)
        {
            return float.NegativeInfinity;
        }

        Vector2 center = GetPlayerCollisionCenterXZ(rootPosition);
        if (!HasNearbyWaterBiome(center))
        {
            return float.NegativeInfinity;
        }

        float radius = GetPlayerWaterCollisionRadius();
        float maxScore = terrain.GetWaterSurfaceScoreAtWorldPosition(center, waterBoundaryWeightBuffer);

        for (int i = 0; i < WaterBoundarySampleDirections.Length; i++)
        {
            Vector2 direction = WaterBoundarySampleDirections[i];
            float score = terrain.GetWaterSurfaceScoreAtWorldPosition(
                center + (direction * radius),
                waterBoundaryWeightBuffer);
            maxScore = Mathf.Max(maxScore, score);
        }

        return maxScore;
    }

    private bool TryEstimateWaterSurfaceNormal(
        Vector3 rootPosition,
        Vector3 preferredDirection,
        out Vector2 normal)
    {
        normal = Vector2.zero;
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null)
        {
            return false;
        }

        Vector2 center = GetPlayerCollisionCenterXZ(rootPosition);
        Vector2 direction = new Vector2(preferredDirection.x, preferredDirection.z);
        if (direction.sqrMagnitude > 0.0001f)
        {
            center += direction.normalized * GetPlayerWaterCollisionRadius();
        }

        float probe = WaterBoundaryNormalProbeDistance;
        float right = terrain.GetWaterSurfaceScoreAtWorldPosition(
            center + new Vector2(probe, 0f),
            waterBoundaryNormalWeightBuffer);
        float left = terrain.GetWaterSurfaceScoreAtWorldPosition(
            center + new Vector2(-probe, 0f),
            waterBoundaryNormalWeightBuffer);
        float up = terrain.GetWaterSurfaceScoreAtWorldPosition(
            center + new Vector2(0f, probe),
            waterBoundaryNormalWeightBuffer);
        float down = terrain.GetWaterSurfaceScoreAtWorldPosition(
            center + new Vector2(0f, -probe),
            waterBoundaryNormalWeightBuffer);

        normal = new Vector2(right - left, up - down);
        if (normal.sqrMagnitude <= 0.000001f)
        {
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            normal = direction;
        }

        normal.Normalize();
        return true;
    }

    private bool HasNearbyWaterBiome(Vector2 center)
    {
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null)
        {
            return false;
        }

        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(center.x),
            Mathf.RoundToInt(center.y));
        if (nearbyWaterBiomeCacheFrame == Time.frameCount
            && nearbyWaterBiomeCacheCoordinate == coordinate)
        {
            return nearbyWaterBiomeCacheResult;
        }

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (terrain.IsWaterBiomeAt(coordinate + new Vector2Int(offsetX, offsetY)))
                {
                    nearbyWaterBiomeCacheFrame = Time.frameCount;
                    nearbyWaterBiomeCacheCoordinate = coordinate;
                    nearbyWaterBiomeCacheResult = true;
                    return true;
                }
            }
        }

        nearbyWaterBiomeCacheFrame = Time.frameCount;
        nearbyWaterBiomeCacheCoordinate = coordinate;
        nearbyWaterBiomeCacheResult = false;
        return false;
    }

    private Vector2 GetPlayerCollisionCenterXZ(Vector3 rootPosition)
    {
        CacheDefaultCapsuleColliderCenter();
        if (cachedCapsuleCollider == null)
        {
            return new Vector2(rootPosition.x, rootPosition.z);
        }

        Vector3 currentRootPosition = cachedRigidbody != null
            ? cachedRigidbody.position
            : transform.position;
        Vector3 currentWorldCenter = cachedCapsuleCollider.transform.TransformPoint(cachedCapsuleCollider.center);
        Vector3 centerOffset = currentWorldCenter - currentRootPosition;
        return new Vector2(rootPosition.x + centerOffset.x, rootPosition.z + centerOffset.z);
    }

    private float GetPlayerWaterCollisionRadius()
    {
        CacheDefaultCapsuleColliderCenter();
        if (cachedCapsuleCollider == null)
        {
            return WaterBoundarySkin;
        }

        Transform colliderTransform = cachedCapsuleCollider.transform;
        Vector3 scale = colliderTransform != null ? colliderTransform.lossyScale : Vector3.one;
        float planarScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        return Mathf.Max(0f, cachedCapsuleCollider.radius * planarScale) + WaterBoundarySkin;
    }

    private void SnapRootToGroundY()
    {
        if (cachedRigidbody == null)
        {
            Vector3 transformPosition = transform.position;
            if (Mathf.Abs(transformPosition.y - PlayerRootY) > PlayerRootYEpsilon)
            {
                transform.position = ClampRootPositionToGroundY(transformPosition);
            }

            return;
        }

        Vector3 rigidbodyPosition = cachedRigidbody.position;
        Vector3 transformPositionWithRigidbody = transform.position;
        bool rigidbodyNeedsSnap = Mathf.Abs(rigidbodyPosition.y - PlayerRootY) > PlayerRootYEpsilon;
        bool transformNeedsSnap = Mathf.Abs(transformPositionWithRigidbody.y - PlayerRootY) > PlayerRootYEpsilon;
        if (!rigidbodyNeedsSnap && !transformNeedsSnap)
        {
            return;
        }

        Vector3 snappedPosition = ClampRootPositionToGroundY(rigidbodyPosition);
        if (rigidbodyNeedsSnap)
        {
            cachedRigidbody.position = snappedPosition;
            transform.position = snappedPosition;
        }
        else if (transformNeedsSnap)
        {
            transform.position = ClampRootPositionToGroundY(transformPositionWithRigidbody);
        }

        Vector3 velocity = cachedRigidbody.velocity;
        if (Mathf.Abs(velocity.y) > PlayerRootYEpsilon)
        {
            velocity.y = 0f;
            cachedRigidbody.velocity = velocity;
        }
    }

    private static Vector3 ClampRootPositionToGroundY(Vector3 position)
    {
        position.y = PlayerRootY;
        return position;
    }

    private bool TryGetBlockingSweepHit(Vector3 direction, float distance, out RaycastHit blockingHit)
    {
        blockingHit = default;
        if (cachedRigidbody == null || distance <= 0f)
        {
            return false;
        }

        RaycastHit[] hits = cachedRigidbody.SweepTestAll(
            direction,
            distance,
            QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, CompareSweepHitsByDistance);
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || ShouldIgnorePlayerMovementSweepHit(hit, direction))
            {
                continue;
            }

            blockingHit = hit;
            return true;
        }

        return false;
    }

    private static int CompareSweepHitsByDistance(RaycastHit left, RaycastHit right)
    {
        return left.distance.CompareTo(right.distance);
    }

    private bool ShouldIgnorePlayerMovementSweepHit(RaycastHit hit, Vector3 direction)
    {
        Pipe pipe = hit.collider != null ? hit.collider.GetComponentInParent<Pipe>() : null;
        if (pipe == null
            || !TryResolvePipeBridgeBelt(pipe, out ConvayorBelt2F belt2F)
            || belt2F == null)
        {
            return false;
        }

        Vector3 currentPosition = cachedRigidbody != null ? cachedRigidbody.position : transform.position;
        Vector3 hitProbePosition = hit.point;
        hitProbePosition.y = currentPosition.y;

        Vector3 forwardProbePosition = currentPosition;
        if (direction.sqrMagnitude > 0.0001f)
        {
            forwardProbePosition += direction.normalized * Mathf.Max(0.05f, hit.distance);
        }

        return IsPositionOnPlayerBelt2FPath(currentPosition, belt2F)
               || IsPositionOnPlayerBelt2FPath(forwardProbePosition, belt2F)
               || IsPositionOnPlayerBelt2FPath(hitProbePosition, belt2F);
    }

    private bool TryResolvePipeBridgeBelt(Pipe pipe, out ConvayorBelt2F belt2F)
    {
        belt2F = null;
        if (pipe == null)
        {
            return false;
        }

        Vector2Int pipeCoordinate = pipe.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
            ? anchorCoordinate
            : new Vector2Int(
                Mathf.RoundToInt(pipe.transform.position.x),
                Mathf.RoundToInt(pipe.transform.position.z));

        return ConvayorBelt2F.TryFindCoveringBelt(pipeCoordinate, out belt2F)
               && belt2F != null
               && belt2F.IsBridgeCenterCoordinate(pipeCoordinate);
    }

    private bool IsPositionOnPlayerBelt2FPath(Vector3 worldPosition, ConvayorBelt2F belt2F)
    {
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null || belt2F == null)
        {
            return false;
        }

        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
        float maxDistanceSqr = ConveyorStandingHandoffDistance * ConveyorStandingHandoffDistance;
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
                    || block == null
                    || !ConvayorBelt2F.TryFindCoveringBelt(coordinate, out ConvayorBelt2F coveringBelt)
                    || !ReferenceEquals(coveringBelt, belt2F)
                    || !block.TryGetConveyorStandingDistanceSqr(worldPosition, out float distanceSqr)
                    || distanceSqr > maxDistanceSqr)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private void CacheDefaultCapsuleColliderCenter()
    {
        if (hasDefaultCapsuleColliderCenter)
        {
            return;
        }

        cachedCapsuleCollider = GetComponent<CapsuleCollider>();
        if (cachedCapsuleCollider == null)
        {
            return;
        }

        defaultCapsuleColliderCenter = cachedCapsuleCollider.center;
        hasDefaultCapsuleColliderCenter = true;
    }

    private void ApplyStandingColliderOffset(float targetOffset)
    {
        CacheDefaultCapsuleColliderCenter();
        if (!hasDefaultCapsuleColliderCenter || cachedCapsuleCollider == null)
        {
            return;
        }

        Vector3 center = defaultCapsuleColliderCenter;
        center.y += Mathf.Max(0f, targetOffset);
        cachedCapsuleCollider.center = center;
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

    private void ApplyStandingOffset()
    {
        CacheDefaultBodyLocalPosition();
        float targetOffset = ResolveStandingConveyorVisualOffset();
        ApplyStandingColliderOffset(targetOffset);

        Transform bodyTransform = player != null ? player.BodyTransform : null;
        if (!hasDefaultBodyLocalPosition || bodyTransform == null || bodyTransform == transform)
        {
            return;
        }

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

    private float ResolveStandingConveyorVisualOffset()
    {
        if (!TryGetStandingConveyorBlock(out Block standingBlock) || standingBlock == null)
        {
            return 0f;
        }

        Vector3 samplePosition = GetConveyorSamplePosition();
        if (standingBlock.ShouldBlockPlayerCarryForCrossingBelt2F(currentConveyorCarryVelocity))
        {
            return ConveyorStandingHeight;
        }

        if (standingBlock.TryGetConveyorStandingWorldHeight(samplePosition, out float standingWorldHeight))
        {
            return Mathf.Max(0f, standingWorldHeight - samplePosition.y);
        }

        return ConveyorStandingHeight;
    }

    private Vector3 GetConveyorSamplePosition()
    {
        return cachedRigidbody != null
            ? cachedRigidbody.position
            : transform.position;
    }

    private void RestoreStandingVisualOffset()
    {
        ApplyStandingColliderOffset(0f);

        Transform bodyTransform = player != null ? player.BodyTransform : null;
        if (!hasDefaultBodyLocalPosition || bodyTransform == null || bodyTransform == transform)
        {
            standingVisualOffsetVelocity = 0f;
            hasStandingConveyorCoordinate = false;
            standingConveyorCoordinate = default;
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

        Vector3 samplePosition = GetConveyorSamplePosition();

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

        Vector3 samplePosition = GetConveyorSamplePosition();
        if (standingBlock.ShouldBlockPlayerCarryForCrossingBelt2F(currentConveyorCarryVelocity))
        {
            currentConveyorCarryVelocity = Vector3.zero;
            return false;
        }

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
        if (interactionPointSnapTarget != null && interactionPointSnapVehicle != null)
        {
            ApplyInteractionPointSnap();
        }
        else
        {
            ClearInteractionPointSnap(true);
        }

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

        switch (resource.ResolvedHarvestMode)
        {
            case Resource.HarvestMode.Logging:
            case Resource.HarvestMode.Cut:
                return player.State.LoggingPower;
            default:
                return player.State.MiningPower;
        }
    }

    private void RefreshInteractionFocus(bool hasMovement)
    {
        ExpireTemporaryDropFocusIfNeeded();

        interactionFocusTargetOverrides.Clear();
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
        bool hasStandingAreaFocusBlock = TryGetStandingInputOutputAreaFocusBlock(
            out Block standingAreaFocusBlock);
        if (FindCurrentInputOutputModuleFocusBlocks(nearbyInputOutputModuleFocusBlocks, ref nearestFocusCandidate))
        {
            AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInputOutputModuleFocusBlocks);
        }

        FindNearbyWorkableBlocks(nearbyWorkableFocusBlocks, ref nearestFocusCandidate);
        UpdateSelectedWorkableRangeVisuals(nearbyWorkableRangeObjects);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyWorkableFocusBlocks);

        FindNearbyBoxBlocks(nearbyBoxFocusBlocks, ref nearestFocusCandidate);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyBoxFocusBlocks);

        FindNearbyInstallationBlocks(
            nearbyInstallationFocusBlocks,
            ref nearestFocusCandidate,
            standingConveyorFocusBlock);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInstallationFocusBlocks);

        AppendInteractionFocusCandidate(nearestFocusCandidate, combinedInteractionFocusBlocks);
        KeepClosestInteractionFocusTarget(combinedInteractionFocusBlocks);
        if (hasStandingAreaFocusBlock)
        {
            AppendUniqueBlock(combinedInteractionFocusBlocks, standingAreaFocusBlock);
        }

        SetFocusedBlocks(combinedInteractionFocusBlocks);
    }

    private void KeepClosestInteractionFocusTarget(List<Block> focusBlocks)
    {
        if (focusBlocks == null || focusBlocks.Count <= 1 || player == null)
        {
            return;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        MapObject closestTarget = null;
        Block closestFallbackBlock = null;
        float closestDistanceSqr = float.MaxValue;
        bool foundVehicleTarget = false;

        for (int i = 0; i < focusBlocks.Count; i++)
        {
            Block block = focusBlocks[i];
            if (block == null)
            {
                continue;
            }

            MapObject target = ResolveInteractionFocusTarget(block);
            bool isVehicleTarget = target is Vehicle;
            if (foundVehicleTarget && !isVehicleTarget)
            {
                continue;
            }

            float distanceSqr = GetInteractionFocusTargetDistanceSqr(target, block, origin);
            if (!isVehicleTarget && distanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            if (isVehicleTarget
                && foundVehicleTarget
                && distanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            foundVehicleTarget = isVehicleTarget || foundVehicleTarget;
            closestDistanceSqr = distanceSqr;
            closestTarget = target;
            closestFallbackBlock = block;
        }

        if (closestFallbackBlock == null)
        {
            focusBlocks.Clear();
            return;
        }

        focusBlocks.Clear();
        if (closestTarget != null)
        {
            AppendMapObjectFocusBlocks(closestTarget, closestFallbackBlock, focusBlocks);
        }

        if (focusBlocks.Count <= 0)
        {
            focusBlocks.Add(closestFallbackBlock);
        }
    }

    private MapObject ResolveInteractionFocusTarget(Block block)
    {
        if (block == null)
        {
            return null;
        }

        if (interactionFocusTargetOverrides.TryGetValue(block, out MapObject overrideTarget)
            && overrideTarget != null
            && overrideTarget.gameObject.activeInHierarchy
            && overrideTarget.AllowsFocus)
        {
            return overrideTarget;
        }

        if (IsInputOutputRuntimeFocusAreaCoordinate(block.Coordinate)
            && InputOutputModule.TryGetModuleAtRuntimeAreaCoordinate(
                block.Coordinate,
                out InputOutputModule inputOutputModule)
            && inputOutputModule != null
            && inputOutputModule.AllowsFocus)
        {
            return inputOutputModule;
        }

        if (block.MapObject != null)
        {
            return block.MapObject;
        }

        return block.Resource;
    }

    private float GetInteractionFocusTargetDistanceSqr(MapObject target, Block block, Vector3 origin)
    {
        if (target is Resource resource)
        {
            return GetResourceFocusSelectionDistanceSqr(resource, origin);
        }

        if (target is WorkableObject workableObject)
        {
            return GetWorkableFocusDistanceSqr(workableObject, block, origin);
        }

        if (target != null)
        {
            return GetMapObjectFocusSelectionDistanceSqr(target, block, origin);
        }

        return GetBlockFocusDistanceSqr(block, origin);
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
            MapObject mapObject = ResolveInteractionFocusTarget(block);

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
                                      || TryResolveRobotArm(mapObject, out _)
                                      || TryResolveProductionMachine(mapObject, out _);
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

    private static bool TryResolveProductionMachine(MapObject mapObject, out ProductionMachine productionMachine)
    {
        productionMachine = null;
        if (mapObject == null)
        {
            return false;
        }

        productionMachine = mapObject as ProductionMachine;
        if (productionMachine != null)
        {
            return true;
        }

        productionMachine = mapObject.GetComponent<ProductionMachine>();
        if (productionMachine != null)
        {
            return true;
        }

        productionMachine = mapObject.GetComponentInChildren<ProductionMachine>(true);
        return productionMachine != null;
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

    private bool TryGetStandingInputOutputAreaFocusBlock(out Block focusBlock)
    {
        focusBlock = null;
        if (player == null || ResolveTerrainGenerator() == null)
        {
            return false;
        }

        Vector3 rootPosition = cachedRigidbody != null ? cachedRigidbody.position : transform.position;
        Vector2 sampleCenter = GetPlayerCollisionCenterXZ(rootPosition);
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(sampleCenter.x),
            Mathf.RoundToInt(sampleCenter.y));
        float overlapRadius = Mathf.Max(0.05f, GetPlayerWaterCollisionRadius());
        float maxDistanceSqr = overlapRadius * overlapRadius;
        float bestDistanceSqr = float.MaxValue;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!IsInputOutputRuntimeFocusAreaCoordinate(coordinate)
                    || !cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block)
                    || block == null)
                {
                    continue;
                }

                float distanceSqr = GetDistanceSqrToGridCell(sampleCenter, coordinate);
                if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                focusBlock = block;
            }
        }

        return focusBlock != null;
    }

    private static bool IsInputOutputRuntimeFocusAreaCoordinate(Vector2Int coordinate)
    {
        return InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate)
               || InputOutputModuleOutputAreaController.CoordinateIsOutputArea(coordinate);
    }

    private static float GetDistanceSqrToGridCell(Vector2 point, Vector2Int coordinate)
    {
        float minX = coordinate.x - 0.5f;
        float maxX = coordinate.x + 0.5f;
        float minY = coordinate.y - 0.5f;
        float maxY = coordinate.y + 0.5f;
        float closestX = Mathf.Clamp(point.x, minX, maxX);
        float closestY = Mathf.Clamp(point.y, minY, maxY);
        float dx = point.x - closestX;
        float dy = point.y - closestY;
        return (dx * dx) + (dy * dy);
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
        nearbyWorkableObjects.Clear();
        nearbyWorkableRangeObjects.Clear();

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

                nearbyWorkableRangeObjects.Add(workableObject);

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

                TryAppendNearbyInstallationFocus(
                    block.MapObject as InstallationObject,
                    block,
                    origin,
                    focusForward,
                    results,
                    ref nearestFocusCandidate,
                    standingConveyorFocusBlock);

                nearbyRuntimeInstallationScratch.Clear();
                InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                    coordinate,
                    nearbyRuntimeInstallationScratch);
                for (int i = 0; i < nearbyRuntimeInstallationScratch.Count; i++)
                {
                    TryAppendNearbyInstallationFocus(
                        nearbyRuntimeInstallationScratch[i],
                        block,
                        origin,
                        focusForward,
                        results,
                        ref nearestFocusCandidate,
                        standingConveyorFocusBlock);
                }
            }
        }

        nearbyRuntimeInstallationScratch.Clear();
    }

    private void TryAppendNearbyInstallationFocus(
        InstallationObject installationObject,
        Block block,
        Vector3 origin,
        Vector3 focusForward,
        List<Block> results,
        ref InteractionFocusCandidate nearestFocusCandidate,
        Block standingConveyorFocusBlock)
    {
        if (installationObject == null
            || block == null
            || !installationObject.gameObject.activeInHierarchy
            || !installationObject.AllowsFocus
            || installationObject is WorkableObject
            || installationObject is BoxObject)
        {
            return;
        }

        if (nearbyInstallationObjects.Contains(installationObject))
        {
            return;
        }

        if (standingConveyorFocusBlock != null && installationObject is ConveyorBelt)
        {
            return;
        }

        nearbyInstallationObjects.Add(installationObject);

        float focusRadius = Mathf.Max(0f, installationObject.FocusActivationRadius);
        if (focusRadius <= 0f)
        {
            return;
        }

        float score = GetMapObjectFocusSelectionScore(
            installationObject,
            block,
            origin,
            focusForward,
            out float distanceSqr);
        if (distanceSqr > focusRadius * focusRadius)
        {
            return;
        }

        if (installationObject.FocusMode == MapObject.MultiFocusMode.NearOne)
        {
            TrySetInteractionFocusCandidate(ref nearestFocusCandidate, score, installationObject, block);
            return;
        }

        AppendMapObjectFocusBlocks(installationObject, block, results);
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
                    if (!TryAppendFocusBlock(results, focusCoordinates[i], inputOutputModule))
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
                    if (!TryAppendFocusBlock(results, occupiedCoordinates[i], installationObject))
                    {
                        continue;
                    }

                    appended = true;
                }
            }

            if (TryGetInstallationVisualCoordinates(installationObject, out List<Vector2Int> visualCoordinates))
            {
                for (int i = 0; i < visualCoordinates.Count; i++)
                {
                    if (!TryAppendFocusBlock(results, visualCoordinates[i], installationObject))
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

    private bool TryAppendFocusBlock(List<Block> results, Vector2Int coordinate, MapObject targetOverride = null)
    {
        if (results == null)
        {
            return false;
        }

        if (ResolveTerrainGenerator() == null
            || !cachedTerrainGenerator.TryGetLoadedBlock(coordinate, out Block block)
            || block == null)
        {
            return false;
        }

        SetInteractionFocusTargetOverride(block, targetOverride);
        if (results.Contains(block))
        {
            return false;
        }

        results.Add(block);
        return true;
    }

    private void SetInteractionFocusTargetOverride(Block block, MapObject targetOverride)
    {
        if (block == null || targetOverride == null)
        {
            return;
        }

        if (interactionFocusTargetOverrides.TryGetValue(block, out MapObject existing)
            && existing != null
            && existing != targetOverride
            && existing is Vehicle
            && targetOverride is Railload)
        {
            return;
        }

        if (targetOverride is Vehicle
            || !interactionFocusTargetOverrides.TryGetValue(block, out existing)
            || existing == null
            || existing is Railload)
        {
            interactionFocusTargetOverrides[block] = targetOverride;
        }
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

        mapObject = TryFindInstallationCoveringCoordinate(
                fallbackBlock.Coordinate,
                out InstallationObject coveringInstallation,
                out Block coveringFallbackBlock)
            ? coveringInstallation
            : fallbackBlock.MapObject != null
                ? fallbackBlock.MapObject
                : fallbackBlock.Resource;
        if (coveringFallbackBlock != null)
        {
            fallbackBlock = coveringFallbackBlock;
        }

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

    private bool TryFindInstallationCoveringCoordinate(
        Vector2Int coordinate,
        out InstallationObject installationObject,
        out Block fallbackBlock)
    {
        installationObject = null;
        fallbackBlock = null;

        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null)
        {
            return false;
        }

        List<InstallationObject> checkedInstallations = new List<InstallationObject>();
        int searchRadius = Mathf.Max(4, Mathf.CeilToInt(InstallationObject.GlobalMaxFocusActivationRadius) + 4);
        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int candidateCoordinate = coordinate + new Vector2Int(offsetX, offsetY);
                if (!terrain.TryGetLoadedBlock(candidateCoordinate, out Block candidateBlock)
                    || candidateBlock == null
                    || !(candidateBlock.MapObject is InstallationObject candidate)
                    || candidate == null
                    || !candidate.gameObject.activeInHierarchy
                    || !candidate.AllowsFocus
                    || checkedInstallations.Contains(candidate)
                    || !InstallationCoversCoordinate(candidate, coordinate))
                {
                    continue;
                }

                checkedInstallations.Add(candidate);
                installationObject = candidate;
                fallbackBlock = candidateBlock;
                return true;
            }
        }

        return false;
    }

    private bool InstallationCoversCoordinate(InstallationObject installationObject, Vector2Int coordinate)
    {
        if (installationObject == null)
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates != null)
        {
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                if (occupiedCoordinates[i] == coordinate)
                {
                    return true;
                }
            }
        }

        if (!TryGetInstallationVisualCoordinates(installationObject, out List<Vector2Int> visualCoordinates))
        {
            return false;
        }

        for (int i = 0; i < visualCoordinates.Count; i++)
        {
            if (visualCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetInstallationVisualCoordinates(
        InstallationObject installationObject,
        out List<Vector2Int> coordinates)
    {
        coordinates = null;
        if (installationObject == null
            || !installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        List<Vector2Int> offsets = GetInstallationVisualLocalOffsets(installationObject, quarterTurns);
        if (offsets.Count <= 0)
        {
            return false;
        }

        coordinates = new List<Vector2Int>(offsets.Count);
        for (int i = 0; i < offsets.Count; i++)
        {
            coordinates.Add(anchorCoordinate + offsets[i]);
        }

        return coordinates.Count > 0;
    }

    private static List<Vector2Int> GetInstallationVisualLocalOffsets(
        InstallationObject installationObject,
        int quarterTurns)
    {
        int sizeX = Mathf.Max(1, installationObject != null ? installationObject.Status.mapSizeX : 1);
        int sizeY = Mathf.Max(1, installationObject != null ? installationObject.Status.mapSizeY : 1);
        Vector2Int anchorCell;
        if (installationObject is ConvayorBelt2F)
        {
            if (sizeX == 1 && sizeY == 1)
            {
                sizeX = Belt2FDefaultFootprintWidth;
                sizeY = Belt2FDefaultFootprintLength;
            }

            anchorCell = installationObject != null
                ? installationObject.PlacementCenterCell
                : Vector2Int.zero;
        }
        else
        {
            anchorCell = installationObject != null ? installationObject.PlacementCenterCell : Vector2Int.zero;
        }

        List<Vector2Int> offsets = new List<Vector2Int>(sizeX * sizeY);
        for (int y = 0; y < sizeY; y++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                offsets.Add(RotateFootprintOffset(new Vector2Int(x - anchorCell.x, y - anchorCell.y), quarterTurns));
            }
        }

        return offsets;
    }

    private static Vector2Int RotateFootprintOffset(Vector2Int offset, int quarterTurns)
    {
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        return normalizedQuarterTurns switch
        {
            1 => new Vector2Int(offset.y, -offset.x),
            2 => new Vector2Int(-offset.x, -offset.y),
            3 => new Vector2Int(-offset.y, offset.x),
            _ => offset
        };
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

            currentMouseFocusedBlocks.Add(block);
        }

        RefreshMouseFocusMarkers();
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

            currentFocusedBlocks.Add(block);
        }

        RefreshInteractionFocusMarkers();
        RefreshTemporaryDropFocusVisibility();
    }

    private void RefreshInteractionFocusMarkers()
    {
        RefreshGroupedFocusMarkers(currentFocusedBlocks, false);
    }

    private void RefreshMouseFocusMarkers()
    {
        RefreshGroupedFocusMarkers(currentMouseFocusedBlocks, true);
    }

    private void RefreshGroupedFocusMarkers(HashSet<Block> focusedBlocks, bool mouseFocus)
    {
        focusMarkerGroupCount = 0;
        if (focusedBlocks == null || focusedBlocks.Count <= 0)
        {
            return;
        }

        foreach (Block block in focusedBlocks)
        {
            if (block == null)
            {
                continue;
            }

            SetBlockFocusVisible(block, mouseFocus, false);
            if (IsInputOutputRuntimeFocusAreaCoordinate(block.Coordinate))
            {
                SetBlockFocusVisible(block, mouseFocus, true);
                continue;
            }

            MapObject focusedMapObject = block.MapObject;
            if (focusedMapObject == null)
            {
                SetBlockFocusVisible(block, mouseFocus, true);
                continue;
            }

            FocusMarkerGroup group = GetFocusMarkerGroup(focusedMapObject);
            if (group == null)
            {
                group = GetNextFocusMarkerGroup();
                group.Reset(focusedMapObject, block);
            }
            else
            {
                group.Add(block);
            }
        }

        for (int i = 0; i < focusMarkerGroupCount; i++)
        {
            FocusMarkerGroup group = focusMarkerGroups[i];
            if (group == null || group.markerBlock == null)
            {
                continue;
            }

            if (group.count <= 1)
            {
                SetBlockFocusVisible(group.markerBlock, mouseFocus, true);
            }
            else
            {
                SetBlockFocusVisible(group.markerBlock, mouseFocus, true, group.Center, group.Size);
            }
        }
    }

    private FocusMarkerGroup GetNextFocusMarkerGroup()
    {
        FocusMarkerGroup group;
        if (focusMarkerGroupCount < focusMarkerGroups.Count)
        {
            group = focusMarkerGroups[focusMarkerGroupCount];
        }
        else
        {
            group = new FocusMarkerGroup();
            focusMarkerGroups.Add(group);
        }

        focusMarkerGroupCount++;
        return group;
    }

    private FocusMarkerGroup GetFocusMarkerGroup(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        for (int i = 0; i < focusMarkerGroupCount; i++)
        {
            FocusMarkerGroup group = focusMarkerGroups[i];
            if (group != null && group.mapObject == mapObject)
            {
                return group;
            }
        }

        return null;
    }

    private static void SetBlockFocusVisible(Block block, bool mouseFocus, bool isVisible)
    {
        if (block == null)
        {
            return;
        }

        if (mouseFocus)
        {
            block.SetMouseFocusVisible(isVisible);
        }
        else
        {
            block.SetFocusVisible(isVisible);
        }
    }

    private static void SetBlockFocusVisible(Block block, bool mouseFocus, bool isVisible, Vector3 center, Vector2 size)
    {
        if (block == null)
        {
            return;
        }

        if (mouseFocus)
        {
            block.SetMouseFocusVisible(isVisible, center, size);
        }
        else
        {
            block.SetFocusVisible(isVisible, center, size);
        }
    }

    private void UpdateSelectedWorkableRangeVisuals(IReadOnlyList<WorkableObject> nextObjects)
    {
        nextSelectedWorkableRangeObjects.Clear();

        if (nextObjects != null)
        {
            for (int i = 0; i < nextObjects.Count; i++)
            {
                WorkableObject workableObject = nextObjects[i];
                if (workableObject == null)
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
