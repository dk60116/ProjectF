using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Player))]
public partial class PlayerController : MonoBehaviour
{
    private static readonly ProfilerMarker RefreshMouseFocusMarker =
        new ProfilerMarker("PlayerController.RefreshMouseMapObjectFocus");
    private static readonly ProfilerMarker FindMouseFocusInstallationMarker =
        new ProfilerMarker("PlayerController.FindMouseFocusInstallation");
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
    private const float PlayerPenetrationEscapeProbeDistance = 0.05f;
    private const float WaterBoundarySkin = 0.005f;
    private const int WaterMoveClampIterations = 5;
    private const float WaterBoundaryNormalProbeDistance = 0.05f;
    private const float WaterBoundarySlideScoreTolerance = 0.01f;
    private const float TemporaryDropFocusDuration = 0.18f;
    private const int TrainDismountSearchPadding = 4;
    private const float MinimumAnimalKnifeInteractionRange = 0.75f;
    private const float AnimalKnifeInteractionTimeout = 1.5f;
    private const float PickAnimationDuration = 1.1666666f;
    private const float AnimalKnifeDamageNormalizedTime = 0.5f;
    private const float AnimalKnifeDamageDelay =
        PickAnimationDuration * AnimalKnifeDamageNormalizedTime;
    private const float AnimalKnifeDamage = 20f;
    private const float AnimalPushClearance = 0.05f;
    private const float AutomaticAnimalInteractionRefreshInterval = 0.1f;
    private const int CrowdedAnimalPathThreshold = 3;
    private const float NooseThrowDistance = 3.5f;
    private const float NooseThrowWindupDuration = 0.8f;
    private const float NooseThrowOutboundDuration = 0.4f;
    private const float NooseThrowHoldDuration = 0.12f;
    private const float NooseThrowReturnDuration = 0.4f;
    private const float NooseThrowArcHeight = 0.45f;
    private const float PitchforkDiggingRange = 0.05f;
    private const float MountedAnimalRunJoystickThreshold = 0.85f;
    private const int AutomaticAnimalInteractionOverlapCapacity = 32;
    private const int InitialMouseFocusRaycastHitBufferSize = 32;
    private const int MaxMouseFocusRaycastHitBufferSize = 128;
    private const int InitialPlayerMovementSweepHitBufferSize = 32;
    private const int MaxPlayerMovementSweepHitBufferSize = 512;
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

    [SerializeField, Min(0.01f)]
    private float rotationInterpolationSpeed = 12f;

    private Player player;
    private Joystick joystick;
    private ResourceWrokGauge resourceWorkGauge;
    private Resource currentTargetResource;
    private Animal currentKnifeTargetAnimal;
    private Animal pendingKnifeTargetAnimal;
    private Animal currentCorpseHarvestTarget;
    private Animal cachedAutomaticInteractionAnimal;
    private float nextAutomaticAnimalInteractionRefreshTime;
    private bool cachedAutomaticInteractionIncludesCorpses;
    private readonly Collider[] automaticAnimalInteractionOverlapBuffer =
        new Collider[AutomaticAnimalInteractionOverlapCapacity];
    private readonly Queue<Animal> pendingCorpseHarvestAnimals = new Queue<Animal>();
    private bool animalKnifePickPending;
    private bool animalKnifeAnimationStarted;
    private bool animalKnifeDamageApplied;
    private float animalKnifeDamageTime;
    private float animalKnifeInteractionEndTime;
    private float animalKnifeInteractionTimeout;
    private NooseThrowVisual activeNooseThrowVisual;
    private readonly HashSet<Block> currentFocusedBlocks = new HashSet<Block>();
    private readonly List<Block> combinedInteractionFocusBlocks = new List<Block>();
    private Block standaloneInteractionAreaFocusBlock;
    private readonly List<Block> nearbyInputOutputModuleFocusBlocks = new List<Block>();
    private readonly List<Block> nearbyInstallationFocusBlocks = new List<Block>();
    private readonly List<Block> nearbyWorkableFocusBlocks = new List<Block>();
    private readonly List<Block> nearbyBoxFocusBlocks = new List<Block>();
    private readonly List<InputOutputModule> standingAreaModuleCandidates = new List<InputOutputModule>(4);
    private readonly List<MapObject> interactionButtonFocusTargets = new List<MapObject>(8);
    private readonly List<Block> interactionButtonFocusTargetBlocks = new List<Block>(8);
    private readonly List<Resource> nearbyResourceCandidates = new List<Resource>(16);
    private readonly HashSet<Block> currentMouseFocusedBlocks = new HashSet<Block>();
    private readonly List<Block> mouseFocusBlocks = new List<Block>();
    private readonly List<Block> mouseFocusRemovalBuffer = new List<Block>();
    private readonly HashSet<Block> currentSelectedFocusedBlocks = new HashSet<Block>();
    private readonly HashSet<Block> currentInteractionFarmlandFocusGroup = new HashSet<Block>();
    private readonly HashSet<Block> currentSelectionFarmlandFocusGroup = new HashSet<Block>();
    private readonly List<Block> selectedFocusBlocks = new List<Block>();
    private readonly List<Block> selectedFocusRemovalBuffer = new List<Block>();
    private Block selectedPitchforkGroundBlock;
    private Block pitchforkDigTargetBlock;
    private bool pitchforkDiggingQueued;
    private readonly List<FocusMarkerGroup> focusMarkerGroups = new List<FocusMarkerGroup>();
    private int focusMarkerGroupCount;
    private readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>();
    private RaycastHit[] mouseFocusRaycastHits = new RaycastHit[InitialMouseFocusRaycastHitBufferSize];
    private readonly HashSet<InstallationObject> mouseFocusCheckedInstallations = new HashSet<InstallationObject>();
    private readonly List<InstallationObject> mouseFocusRuntimeInstallationScratch = new List<InstallationObject>(8);
    private readonly List<InstallationObject> nearbyInstallationObjects = new List<InstallationObject>();
    private readonly List<InstallationObject> nearbyRuntimeInstallationScratch = new List<InstallationObject>(8);
    private readonly List<Renderer> mapObjectFocusRenderers = new List<Renderer>(16);
    private readonly List<Train> itemFilterTrainScratch = new List<Train>(8);
    private readonly Queue<Train> itemFilterTrainQueue = new Queue<Train>(8);
    private readonly HashSet<Train> itemFilterTrainVisited = new HashSet<Train>();
    private readonly Dictionary<Block, MapObject> interactionFocusTargetOverrides = new Dictionary<Block, MapObject>();
    private readonly List<WorkableObject> nearbyWorkableObjects = new List<WorkableObject>();
    private readonly List<WorkableObject> nearbyWorkableRangeObjects = new List<WorkableObject>();
    private readonly List<BoxObject> nearbyBoxObjects = new List<BoxObject>();
    private readonly HashSet<WorkableObject> currentSelectedWorkableRangeObjects = new HashSet<WorkableObject>();
    private readonly HashSet<WorkableObject> nextSelectedWorkableRangeObjects = new HashSet<WorkableObject>();
    private readonly List<WorkableObject> selectedWorkableRangeRemovalBuffer = new List<WorkableObject>();
    private readonly HashSet<Sprinkler> currentFocusedSprinklerRangeObjects = new HashSet<Sprinkler>();
    private readonly HashSet<Sprinkler> nextFocusedSprinklerRangeObjects = new HashSet<Sprinkler>();
    private readonly List<Sprinkler> focusedSprinklerRangeRemovalBuffer = new List<Sprinkler>();
    private readonly HashSet<Sprinkler> currentInRangeSprinklerRangeObjects = new HashSet<Sprinkler>();
    private readonly HashSet<Sprinkler> nextInRangeSprinklerRangeObjects = new HashSet<Sprinkler>();
    private readonly List<Sprinkler> inRangeSprinklerRangeRemovalBuffer = new List<Sprinkler>();
    private readonly List<Block> singleFocusedBlockBuffer = new List<Block>(1);
    private readonly List<Block> mountedPinnedFocusBlocks = new List<Block>();
    private readonly List<Block> focusRemovalBuffer = new List<Block>();
    private readonly float[] waterBoundaryWeightBuffer = new float[8];
    private readonly float[] waterBoundaryNormalWeightBuffer = new float[8];
    private RaycastHit[] playerMovementSweepHits =
        new RaycastHit[InitialPlayerMovementSweepHitBufferSize];
    private int playerMovementCollisionMaskLayer = -1;
    private int playerMovementCollisionMask;
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
    private Animal interactionPointSnapAnimal;
    private MapObject mountedPinnedFocusTarget;
    private Block mountedPinnedFocusFallbackBlock;
    private Block temporaryDropFocusBlock;
    private float temporaryDropFocusUntilTime;
    private MapObject currentMouseFocusedMapObject;
    private Animal currentMouseFocusedAnimal;
    private PortableObject currentMouseFocusedPortableObject;
    private Camera cachedMouseFocusCamera;
    private int mouseFocusRefreshFrame = -1;
    private bool mouseFocusRefreshInteractionLocked;
    private PointerEventData pointerEventData;
    private int nearbyWaterBiomeCacheFrame = -1;
    private Vector2Int nearbyWaterBiomeCacheCoordinate;
    private bool nearbyWaterBiomeCacheResult;
    private enum FocusMarkerKind
    {
        Interaction,
        Mouse,
        Selection
    }

    private sealed class FocusMarkerGroup
    {
        public MapObject mapObject;
        public bool isFarmlandGroup;
        public Block markerBlock;
        public int count;
        private Vector2Int markerCoordinate;
        private Vector3 minWorldPosition;
        private Vector3 maxWorldPosition;
        private readonly List<Vector2Int> coordinates = new List<Vector2Int>();

        public IReadOnlyList<Vector2Int> Coordinates => coordinates;

        public Vector3 Center => new Vector3(
            (minWorldPosition.x + maxWorldPosition.x) * 0.5f,
            markerBlock != null ? markerBlock.WorldPosition.y : (minWorldPosition.y + maxWorldPosition.y) * 0.5f,
            (minWorldPosition.z + maxWorldPosition.z) * 0.5f);

        public Vector2 Size => new Vector2(
            Mathf.Max(1f, maxWorldPosition.x - minWorldPosition.x + 1f),
            Mathf.Max(1f, maxWorldPosition.z - minWorldPosition.z + 1f));

        public void Reset(MapObject targetMapObject, Block block)
        {
            mapObject = targetMapObject;
            isFarmlandGroup = false;
            markerBlock = block;
            count = 0;
            coordinates.Clear();
            markerCoordinate = block != null ? block.Coordinate : Vector2Int.zero;
            if (block != null)
            {
                Vector3 position = block.WorldPosition;
                minWorldPosition = position;
                maxWorldPosition = position;
                Add(block);
            }
        }

        public void ResetFarmland(Block block)
        {
            Reset(null, block);
            isFarmlandGroup = true;
        }

        public void Add(Block block)
        {
            if (block == null)
            {
                return;
            }

            count++;
            Vector2Int coordinate = block.Coordinate;
            coordinates.Add(coordinate);
            if (markerBlock == null
                || coordinate.x < markerCoordinate.x
                || (coordinate.x == markerCoordinate.x && coordinate.y < markerCoordinate.y))
            {
                markerBlock = block;
                markerCoordinate = coordinate;
            }

            Vector3 position = block.WorldPosition;
            minWorldPosition = Vector3.Min(minWorldPosition, position);
            maxWorldPosition = Vector3.Max(maxWorldPosition, position);
        }
    }

    public bool IsResourceHarvestingActive => currentTargetResource != null && pendingHarvestResources.Count > 0;

    public bool TryGetActiveResourceHarvestMode(out Resource.HarvestMode harvestMode)
    {
        if (!IsResourceHarvestingActive)
        {
            harvestMode = default;
            return false;
        }

        harvestMode = currentTargetResource.ResolvedHarvestMode;
        return true;
    }

    public bool IsAnimalKnifeInteractionActive =>
        currentKnifeTargetAnimal != null
        || animalKnifePickPending
        || currentCorpseHarvestTarget != null
        || pendingCorpseHarvestAnimals.Count > 0;
    public bool IsNooseThrowActive => activeNooseThrowVisual != null;
    private bool IsNooseThrowInFlight =>
        activeNooseThrowVisual != null
        && !activeNooseThrowVisual.HasAttachedAnimal;

    public bool TryGetAnimalKnifeFocusTarget(out Animal focusedAnimal)
    {
        focusedAnimal = currentKnifeTargetAnimal != null
            ? currentKnifeTargetAnimal
            : pendingKnifeTargetAnimal;
        return focusedAnimal != null
               && focusedAnimal.gameObject.activeInHierarchy;
    }

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
        interactionPointSnapAnimal?.NotifyRiderDismounted(player);
        player?.SetRidingAnimation(false);
        interactionPointSnapTarget = null;
        interactionPointSnapVehicle = null;
        interactionPointSnapAnimal = null;
        cachedAutomaticInteractionAnimal = null;
        nextAutomaticAnimalInteractionRefreshTime = 0f;
        RestoreStandingVisualOffset();
        currentConveyorCarryVelocity = Vector3.zero;
        hasPendingFacingDirection = false;
        pendingFacingDirection = Vector3.zero;
        ClearTemporaryDropFocus();
        CancelPitchforkDigging();
        selectedPitchforkGroundBlock = null;
        CancelSeedPlanting();
        selectedSeedGroundBlock = null;
        SetSelectedFocusedBlocks(null);
        SetFocusedBlocks(null);
        SetMouseFocusedAnimal(null);
        SetMouseFocusedPortableObject(null);
        SetMouseFocusedBlocks(null);
        currentFocusedBlocks.Clear();
        currentMouseFocusedBlocks.Clear();
        currentSelectedFocusedBlocks.Clear();
        interactionButtonFocusTargets.Clear();
        interactionButtonFocusTargetBlocks.Clear();
        focusRemovalBuffer.Clear();
        mouseFocusRemovalBuffer.Clear();
        mouseFocusBlocks.Clear();
        selectedFocusBlocks.Clear();
        mountedPinnedFocusBlocks.Clear();
        mountedPinnedFocusTarget = null;
        mountedPinnedFocusFallbackBlock = null;
        mouseFocusRefreshFrame = -1;
        UpdateSelectedWorkableRangeVisuals(null);
        UpdateInRangeSprinklerRangeVisuals(null);
        singleFocusedBlockBuffer.Clear();
        CancelAnimalKnifeInteraction();
        CancelNooseThrow();
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
        temporaryDropFocusBlock.SetTemporaryDropFocusVisible(true);
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
        if (previousDropFocusBlock != null)
        {
            previousDropFocusBlock.SetTemporaryDropFocusVisible(false);
        }
    }

    public void SetSelectedMapObjectFocus(MapObject mapObject)
    {
        if (mapObject == null
            && HasGroundActionFocusSelectionOrTarget())
        {
            return;
        }

        if (mapObject != null)
        {
            CancelPitchforkDigging();
            CancelSeedPlanting();
        }

        selectedPitchforkGroundBlock = null;
        selectedSeedGroundBlock = null;
        selectedFocusBlocks.Clear();
        if (mapObject == null
            || !mapObject.gameObject.activeInHierarchy
            || !mapObject.AllowsFocus)
        {
            SetSelectedFocusedBlocks(null);
            return;
        }

        Block fallbackBlock = ResolveSelectedFocusFallbackBlock(mapObject);
        if (!AppendMapObjectFocusBlocks(mapObject, fallbackBlock, selectedFocusBlocks))
        {
            SetSelectedFocusedBlocks(null);
            return;
        }

        SetSelectedFocusedBlocks(selectedFocusBlocks);
    }

    public bool TrySelectPitchforkGroundAtPointer(Vector2 pointerPosition)
    {
        if (player == null || !player.IsHoldingPitchfork)
        {
            SetSelectedPitchforkGroundBlock(null);
            return false;
        }

        Camera targetCamera = ResolveMouseFocusCamera();
        if (targetCamera == null
            || !TryGetPointerBlockFromGroundPlane(
                targetCamera.ScreenPointToRay(pointerPosition),
                out Block block)
            || !CanFocusPitchforkGroundBlock(block))
        {
            SetSelectedPitchforkGroundBlock(null);
            return false;
        }

        SetSelectedPitchforkGroundBlock(block);
        return true;
    }

    public bool TryGetSelectedPitchforkGroundBlock(out Block block)
    {
        block = selectedPitchforkGroundBlock;
        if (block != null
            && player != null
            && player.IsHoldingPitchfork
            && CanFocusPitchforkGroundBlock(block))
        {
            return true;
        }

        SetSelectedPitchforkGroundBlock(null);
        block = null;
        return false;
    }

    public bool RequestPitchforkDigging()
    {
        if (interactionPointSnapTarget != null
            || player == null
            || player.IsCarrying
            || !TryGetSelectedPitchforkGroundBlock(out Block targetBlock))
        {
            return false;
        }

        CancelActiveResourceHarvest();
        CancelAnimalKnifeInteraction();
        pitchforkDigTargetBlock = targetBlock;
        pitchforkDiggingQueued = false;
        return true;
    }

    public bool IsPitchforkDiggingActive => pitchforkDigTargetBlock != null;

    private void SetSelectedPitchforkGroundBlock(Block block)
    {
        if (selectedPitchforkGroundBlock == block)
        {
            return;
        }

        if (pitchforkDigTargetBlock != null && pitchforkDigTargetBlock != block)
        {
            CancelPitchforkDigging();
        }

        if (block != null)
        {
            CancelSeedPlanting();
            selectedSeedGroundBlock = null;
            selectedSeedDefinition = null;
        }

        selectedPitchforkGroundBlock = block;
        selectedFocusBlocks.Clear();
        if (block != null)
        {
            selectedFocusBlocks.Add(block);
        }

        SetSelectedFocusedBlocks(block != null ? selectedFocusBlocks : null);
    }

    private Block ResolveSelectedFocusFallbackBlock(MapObject mapObject)
    {
        if (mapObject is Resource resource)
        {
            return ResolveResourceOwningBlock(resource);
        }

        if (currentMouseFocusedMapObject != mapObject)
        {
            return null;
        }

        foreach (Block block in currentMouseFocusedBlocks)
        {
            if (block != null)
            {
                return block;
            }
        }

        return null;
    }

    public Vehicle MountedVehicle => interactionPointSnapTarget != null ? interactionPointSnapVehicle : null;
    public Animal MountedAnimal => interactionPointSnapTarget != null ? interactionPointSnapAnimal : null;
    public bool IsMounted => interactionPointSnapTarget != null;

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

    public bool IsMountedOnAnimal(Animal animal)
    {
        return animal != null
               && interactionPointSnapTarget != null
               && interactionPointSnapAnimal == animal;
    }

    public bool TryGetMountedAnimalId(out long deterministicId)
    {
        deterministicId = 0L;
        TerrainAnimalInstance instance = MountedAnimal != null
            ? MountedAnimal.GetComponentInParent<TerrainAnimalInstance>()
            : null;
        if (instance == null || instance.DeterministicId == 0L)
        {
            return false;
        }

        deterministicId = instance.DeterministicId;
        return true;
    }

    public bool TryRestoreMountedAnimal(long deterministicId)
    {
        if (deterministicId == 0L || player == null)
        {
            ClearInteractionPointSnapForLoad();
            return false;
        }

        AnimalAIWorld world = AnimalAIWorld.Instance;
        if (world == null
            || !world.TryGetControllerByDeterministicId(
                deterministicId,
                out AnimalAIController animalController)
            || animalController.Animal == null)
        {
            ClearInteractionPointSnapForLoad();
            return false;
        }

        ClearInteractionPointSnapForLoad();
        return animalController.Animal.TryMount(player, this);
    }

    public bool TryMountSaddledAnimal(Animal animal)
    {
        if (animal == null
            || player == null
            || IsMounted
            || !animal.CanBeMounted
            || !IsAnimalWithinInteractionRange(animal))
        {
            return false;
        }

        CancelNooseThrow();
        return animal.TryMount(player, this);
    }

    public void ClearInteractionPointSnapForLoad()
    {
        interactionPointSnapAnimal?.NotifyRiderDismounted(player);
        ClearInteractionPointSnap(true);
        pendingMoveDirection = Vector3.zero;
        pendingFacingDirection = Vector3.zero;
        hasPendingFacingDirection = false;
        currentConveyorCarryVelocity = Vector3.zero;
    }

    public void ClearNooseForLoad()
    {
        CancelNooseThrow();
    }

    public bool TryGetNooseLeashedAnimalId(out long deterministicId)
    {
        deterministicId = 0L;
        return activeNooseThrowVisual != null
               && activeNooseThrowVisual.TryGetAttachedAnimalId(out deterministicId);
    }

    public bool TryGetNooseLeashedAnimal(out Animal animal)
    {
        animal = null;
        return activeNooseThrowVisual != null
               && activeNooseThrowVisual.TryGetAttachedAnimal(out animal);
    }

    public bool TryConsumeNooseLeash(Animal expectedAnimal)
    {
        if (expectedAnimal == null
            || !TryGetNooseLeashedAnimal(out Animal leashedAnimal)
            || leashedAnimal != expectedAnimal)
        {
            return false;
        }

        CancelNooseThrow(true);
        return true;
    }

    public bool TryRestoreNooseLeashedAnimal(long deterministicId)
    {
        CancelNooseThrow();
        if (deterministicId == 0L
            || player == null
            || interactionPointSnapTarget != null)
        {
            return false;
        }

        AnimalAIWorld world = AnimalAIWorld.Instance;
        if (world == null
            || !world.TryGetControllerByDeterministicId(
                deterministicId,
                out AnimalAIController animalController))
        {
            return false;
        }

        Animal animal = animalController.Animal;
        PlayerBag handBag = player.GetHandBag();
        if (animal == null
            || handBag == null
            || handBag.GetSlotCount(0) <= 0)
        {
            return false;
        }

        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        ItemDefinition nooseDefinition = ItemDefinitionLookup.ResolveById(
            itemManager != null ? itemManager.ItemDefinitions : null,
            handBag.GetSlotItemId(0));
        if (nooseDefinition == null || nooseDefinition.portableMat == null)
        {
            return false;
        }

        Vector3 attachDirection = animal.GetWorldCenter() - handBag.transform.position;
        attachDirection.y = 0f;
        if (attachDirection.sqrMagnitude <= 0.0001f)
        {
            attachDirection = transform.forward;
            attachDirection.y = 0f;
        }

        if (attachDirection.sqrMagnitude <= 0.0001f)
        {
            attachDirection = Vector3.forward;
        }

        NooseThrowVisual visual = CreateNooseThrowVisual(
            handBag,
            nooseDefinition.id,
            attachDirection.normalized,
            nooseDefinition.portableMat);
        if (visual != null && visual.TryAttachExisting(animal, animalController))
        {
            return true;
        }

        CancelNooseThrow();
        return false;
    }

    public bool TrySnapBodyToInteractionPoint(Transform targetPoint, Vehicle vehicle = null)
    {
        return TrySnapBodyToMountPoint(targetPoint, vehicle, null);
    }

    public bool TrySnapBodyToAnimalMountPoint(Transform targetPoint, Animal animal)
    {
        return animal != null && TrySnapBodyToMountPoint(targetPoint, null, animal);
    }

    private bool TrySnapBodyToMountPoint(
        Transform targetPoint,
        Vehicle vehicle,
        Animal animal)
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
        CancelAnimalKnifeInteraction();
        ClearTemporaryDropFocus();
        SetFocusedBlocks(null);
        SetMouseFocusedPortableObject(null);
        SetMouseFocusedBlocks(null);
        currentMouseFocusedMapObject = null;

        interactionPointSnapTarget = targetPoint;
        interactionPointSnapVehicle = vehicle;
        interactionPointSnapAnimal = animal;
        player.SetRidingAnimation(animal != null);
        mountedPinnedFocusTarget = vehicle;
        mountedPinnedFocusFallbackBlock = null;
        ApplyInteractionPointSnap();
        RefreshMountedPinnedInteractionFocus();
        player.StopImmediateActions();
        player.UpdateCarryState();
        return true;
    }

    public bool TryDismountFromVehicle()
    {
        return TryDismount();
    }

    public bool TryDismount()
    {
        if (interactionPointSnapTarget == null)
        {
            return false;
        }

        if (player == null)
        {
            player = GetComponent<Player>();
        }

        Vehicle dismountedVehicle = interactionPointSnapVehicle;
        Animal dismountedAnimal = interactionPointSnapAnimal;
        if (!TryResolveInteractionPointExitPosition(
                dismountedVehicle,
                dismountedAnimal,
                out Vector3 exitPosition))
        {
            return false;
        }

        Quaternion exitRotation = transform.rotation;
        ClearInteractionPointSnap(true);
        dismountedVehicle?.NotifyPlayerDismounted(player);
        dismountedAnimal?.NotifyRiderDismounted(player);

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
            cachedRigidbody.linearVelocity = Vector3.zero;
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
            if (interactionPointSnapAnimal != null)
            {
                player?.SetRidingAnimation(false);
                interactionPointSnapAnimal = null;
            }

            ClearMountedPinnedFocus();
            return;
        }

        bool wasAnimalMount = interactionPointSnapAnimal != null;
        interactionPointSnapTarget = null;
        interactionPointSnapVehicle = null;
        interactionPointSnapAnimal = null;
        if (wasAnimalMount)
        {
            player?.SetRidingAnimation(false);
        }
        ClearMountedPinnedFocus();
        if (restoreVisualOffset)
        {
            RestoreStandingVisualOffset();
        }
    }

    private void ClearMountedPinnedFocus()
    {
        mountedPinnedFocusTarget = null;
        mountedPinnedFocusFallbackBlock = null;
        mountedPinnedFocusBlocks.Clear();
    }

    private bool TryResolveInteractionPointExitPosition(
        Vehicle vehicle,
        Animal animal,
        out Vector3 exitPosition)
    {
        Transform snapTarget = interactionPointSnapTarget;
        if (snapTarget == null)
        {
            exitPosition = ClampRootPositionToGroundY(transform.position);
            return true;
        }

        Vector3 center = vehicle != null
            ? vehicle.transform.position
            : animal != null
                ? animal.transform.position
                : transform.position;
        Vector3 direction = snapTarget.position - center;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = animal != null ? animal.transform.right : snapTarget.right;
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
        else if (animal != null)
        {
            exitDistance = Mathf.Max(0.85f, animal.GetWorldRadius() + GetPlayerCollisionRadius());
        }

        Vector3 preferredExitPosition = ClampRootPositionToGroundY(
            center + direction.normalized * exitDistance);
        if (!(vehicle is Train train))
        {
            exitPosition = preferredExitPosition;
            return true;
        }

        return TryResolveClearTrainDismountPosition(
            train,
            preferredExitPosition,
            out exitPosition);
    }

    private bool TryResolveClearTrainDismountPosition(
        Train train,
        Vector3 preferredExitPosition,
        out Vector3 exitPosition)
    {
        exitPosition = default;
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (train == null || terrain == null)
        {
            return false;
        }

        Vector3 trainPosition = train.transform.position;
        MapObject.MapObjectStatus status = train.Status;
        float trainHalfExtent = Mathf.Max(
            1,
            Mathf.Max(status.mapSizeX, status.mapSizeY)) * 0.5f;
        float minimumDistance = trainHalfExtent + GetPlayerCollisionRadius();
        float minimumDistanceSqr = minimumDistance * minimumDistance;
        int searchRadius = Mathf.CeilToInt(minimumDistance)
                           + TrainDismountSearchPadding;
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(trainPosition.x),
            Mathf.RoundToInt(trainPosition.z));

        bool found = false;
        float bestPreferredDistanceSqr = float.MaxValue;
        Vector2Int bestCoordinate = default;
        Vector3 bestPosition = default;
        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate
                                        + new Vector2Int(offsetX, offsetY);
                if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
                    || !IsClearTrainDismountBlock(block))
                {
                    continue;
                }

                Vector3 candidatePosition = ClampRootPositionToGroundY(
                    block.WorldPosition);
                float trainDeltaX = candidatePosition.x - trainPosition.x;
                float trainDeltaZ = candidatePosition.z - trainPosition.z;
                if (trainDeltaX * trainDeltaX + trainDeltaZ * trainDeltaZ
                    < minimumDistanceSqr
                    || IsPlayerBlockedByWaterAtPosition(candidatePosition))
                {
                    continue;
                }

                float preferredDeltaX = candidatePosition.x
                                        - preferredExitPosition.x;
                float preferredDeltaZ = candidatePosition.z
                                        - preferredExitPosition.z;
                float preferredDistanceSqr = preferredDeltaX * preferredDeltaX
                                             + preferredDeltaZ * preferredDeltaZ;
                if (found
                    && (preferredDistanceSqr > bestPreferredDistanceSqr
                        || Mathf.Approximately(
                               preferredDistanceSqr,
                               bestPreferredDistanceSqr)
                           && !CoordinatePrecedes(
                               coordinate,
                               bestCoordinate)))
                {
                    continue;
                }

                found = true;
                bestPreferredDistanceSqr = preferredDistanceSqr;
                bestCoordinate = coordinate;
                bestPosition = candidatePosition;
            }
        }

        if (!found)
        {
            return false;
        }

        exitPosition = bestPosition;
        return true;
    }

    private static bool IsClearTrainDismountBlock(Block block)
    {
        return block != null && block.MapObject == null;
    }

    private static bool CoordinatePrecedes(
        Vector2Int candidate,
        Vector2Int current)
    {
        return candidate.x < current.x
               || candidate.x == current.x && candidate.y < current.y;
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
            interactionPointSnapAnimal?.NotifyRiderDismounted(null);
            interactionPointSnapTarget = null;
            interactionPointSnapVehicle = null;
            interactionPointSnapAnimal = null;
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

        Vector3 targetPosition = interactionPointSnapAnimal != null
            ? interactionPointSnapAnimal.RiderMountPosition
            : interactionPointSnapTarget.position;
        Quaternion targetRotation = interactionPointSnapAnimal != null
            ? interactionPointSnapAnimal.transform.rotation
            : interactionPointSnapTarget.rotation;

        if (cachedRigidbody != null)
        {
            cachedRigidbody.position = targetPosition;
            cachedRigidbody.rotation = targetRotation;
            cachedRigidbody.linearVelocity = Vector3.zero;
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
        if (GameManager.TextInputFocused
            || (GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked))
        {
            return true;
        }

        InstallationPlacementController placementController = ResolveInstallationPlacementController();
        return placementController != null && placementController.PlacementOrMapEditModeActive;
    }

    private void Update()
    {
        if (interactionPointSnapTarget == null && interactionPointSnapAnimal != null)
        {
            interactionPointSnapAnimal.NotifyRiderDismounted(player);
            interactionPointSnapAnimal = null;
            player?.SetRidingAnimation(false);
        }

        if (interactionPointSnapAnimal != null && !interactionPointSnapAnimal.IsAlive)
        {
            TryDismount();
        }

        if (interactionPointSnapTarget != null)
        {
            ApplyInteractionPointSnap();
        }
        else
        {
            SnapRootToGroundY();
        }

        player?.UpdateDropExitGate(transform.position);

        GameManager gameManager = GameManager.Instance;
        bool isInteractionLocked = GameManager.TextInputFocused
                                   || (gameManager != null && gameManager.PlayerInteractionLocked);
        bool isKeyboardMoveLocked = gameManager != null && gameManager.FreeCamera;

        Vector2 input = Vector2.zero;
        Vector2 joystickInput = Vector2.zero;

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
            joystickInput = joystick.InputDirection;
            input = joystickInput;
        }

        if (!isInteractionLocked && !isKeyboardMoveLocked)
        {
            input = Vector2.ClampMagnitude(input + GetKeyboardMoveInput(), 1f);
        }

        if (IsNooseThrowInFlight)
        {
            input = Vector2.zero;
        }

        bool hasManualMovementInput = input.sqrMagnitude > 0.0001f;
        if (pitchforkDigTargetBlock != null
            && (isInteractionLocked
                || isKeyboardMoveLocked
                || interactionPointSnapTarget != null
                || hasManualMovementInput))
        {
            CancelPitchforkDigging();
        }

        if (seedPlantTargetBlock != null
            && (isInteractionLocked
                || isKeyboardMoveLocked
                || interactionPointSnapTarget != null
                || hasManualMovementInput))
        {
            CancelSeedPlanting();
        }

        if (isInteractionLocked
            || isKeyboardMoveLocked
            || interactionPointSnapTarget != null)
        {
            CancelAnimalKnifeInteraction();
        }
        else if (hasManualMovementInput)
        {
            if (animalKnifePickPending)
            {
                input = Vector2.zero;
                hasManualMovementInput = false;
            }
            else
            {
                CancelAnimalKnifeApproach();
            }
        }

        Vector3 moveDirection = GetMoveDirection(input);
        if (!hasManualMovementInput
            && !isInteractionLocked
            && !isKeyboardMoveLocked
            && interactionPointSnapTarget == null
            && !animalKnifePickPending
            && currentKnifeTargetAnimal != null
            && TryGetAnimalKnifeApproachDirection(out Vector3 knifeApproachDirection))
        {
            moveDirection = knifeApproachDirection;
        }

        if (!hasManualMovementInput
            && !isInteractionLocked
            && !isKeyboardMoveLocked
            && interactionPointSnapTarget == null
            && currentKnifeTargetAnimal == null
            && pitchforkDigTargetBlock != null
            && TryGetPitchforkDiggingApproachDirection(out Vector3 pitchforkApproachDirection))
        {
            moveDirection = pitchforkApproachDirection;
        }

        if (!hasManualMovementInput
            && !isInteractionLocked
            && !isKeyboardMoveLocked
            && interactionPointSnapTarget == null
            && currentKnifeTargetAnimal == null
            && pitchforkDigTargetBlock == null
            && seedPlantTargetBlock != null
            && TryGetSeedPlantingApproachDirection(out Vector3 seedPlantApproachDirection))
        {
            moveDirection = seedPlantApproachDirection;
        }

        bool hasMovement = moveDirection.sqrMagnitude > 0.0001f;

        if (interactionPointSnapTarget != null)
        {
            if (interactionPointSnapVehicle != null)
            {
                float mountedMoveSpeed = player != null ? player.Stat.currentMoveSpeed : 0f;
                interactionPointSnapVehicle.HandleMountedInput(moveDirection, mountedMoveSpeed, Time.deltaTime, player);
                player?.UpdateMountedVehicleAnimation(interactionPointSnapVehicle);
                ApplyInteractionPointSnap();
            }
            else if (interactionPointSnapAnimal != null)
            {
                interactionPointSnapAnimal.HandleMountedInput(
                    moveDirection,
                    IsMountedAnimalRunRequested(joystickInput),
                    Time.deltaTime);
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
            SetMouseFocusedAnimal(null);
            SetMouseFocusedPortableObject(null);
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
                Vector3 moveDelta = moveDirection * GetCurrentOnFootMoveSpeed() * Time.deltaTime;
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

        if (hasMovement)
        {
            CancelActiveCorpseHarvest();
        }

        bool finishedPickThisFrame = player.UpdateAnimationState(
            hasMovement,
            GetCurrentLocomotionBlend(moveDirection));
        ResolveCompletedPitchforkDigging();
        ResolveCompletedSeedPlanting();
        TryStartPendingAnimalKnifeAttack();
        if (animalKnifePickPending
            && animalKnifeAnimationStarted
            && !animalKnifeDamageApplied
            && Time.time >= animalKnifeDamageTime)
        {
            ApplyPendingAnimalKnifeDamage();
        }

        if (animalKnifePickPending
            && (animalKnifeAnimationStarted
                ? Time.time >= animalKnifeInteractionEndTime
                  || Time.time >= animalKnifeInteractionTimeout
                : Time.time >= animalKnifeInteractionTimeout))
        {
            ClearPendingAnimalKnifeAttack();
        }

        ResolveCompletedPick(finishedPickThisFrame);
        RefreshInteractionFocus();
        using (RefreshMouseFocusMarker.Auto())
        {
            RefreshMouseMapObjectFocus();
        }
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

        if (GameManager.TextInputFocused
            || (GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked))
        {
            pendingMoveDirection = Vector3.zero;
            currentConveyorCarryVelocity = Vector3.zero;
            return;
        }

        if (cachedRigidbody == null)
        {
            return;
        }

        Vector3 manualVelocity = pendingMoveDirection * GetCurrentOnFootMoveSpeed();
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
        bool relieveAnimalCrowd = HasCrowdedAnimalPath(
            startPosition,
            startPosition + delta);
        Vector3 finalMove = Vector3.zero;

        if (TryGetBlockingSweepHit(
                Vector3.zero,
                direction,
                distance + sweepBuffer,
                relieveAnimalCrowd,
                out RaycastHit hit))
        {
            float allowedDistance = Mathf.Min(
                distance,
                Mathf.Max(0f, hit.distance - sweepBuffer));
            if (allowedDistance > 0f)
            {
                finalMove += direction * allowedDistance;
            }

            float remainingDistance = distance - allowedDistance;
            if (remainingDistance > MinPhysicsMoveDistance)
            {
                Vector3 remaining = direction * remainingDistance;
                Vector3 collisionNormal = hit.normal;
                collisionNormal.y = 0f;
                Vector3 slide = collisionNormal.sqrMagnitude > MinPhysicsMoveDistanceSqr
                    ? Vector3.ProjectOnPlane(remaining, collisionNormal.normalized)
                    : Vector3.zero;
                slide.y = 0f;
                if (slide.sqrMagnitude > MinPhysicsMoveDistanceSqr)
                {
                    Vector3 slideDirection = slide.normalized;
                    float slideDistance = slide.magnitude;
                    bool relieveSlideCrowd = HasCrowdedAnimalPath(
                        startPosition + finalMove,
                        startPosition + finalMove + slide);
                    if (TryGetBlockingSweepHit(
                            finalMove,
                            slideDirection,
                            slideDistance + sweepBuffer,
                            relieveSlideCrowd,
                            out RaycastHit slideHit))
                    {
                        float allowedSlideDistance = Mathf.Min(
                            slideDistance,
                            Mathf.Max(0f, slideHit.distance - sweepBuffer));
                        if (allowedSlideDistance > MinPhysicsMoveDistance)
                        {
                            finalMove += slideDirection * allowedSlideDistance;
                            relieveAnimalCrowd |= relieveSlideCrowd;
                        }
                    }
                    else
                    {
                        finalMove += slide;
                        relieveAnimalCrowd |= relieveSlideCrowd;
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
            if (relieveAnimalCrowd)
            {
                RelieveAnimalCrowdAlongMovement(startPosition, finalPosition);
            }

            cachedRigidbody.MovePosition(finalPosition);
        }
    }

    private bool HasCrowdedAnimalPath(Vector3 startPosition, Vector3 endPosition)
    {
        AnimalAIWorld world = AnimalAIWorld.Instance;
        if (world == null)
        {
            return false;
        }

        return world.CountAnimalsAlongPath(
                   startPosition,
                   endPosition,
                   GetPlayerCollisionRadius(),
                   AnimalPushClearance,
                   CrowdedAnimalPathThreshold)
               >= CrowdedAnimalPathThreshold;
    }

    private void RelieveAnimalCrowdAlongMovement(
        Vector3 startPosition,
        Vector3 endPosition)
    {
        AnimalAIWorld world = AnimalAIWorld.Instance;
        if (world == null
            || !HasCrowdedAnimalPath(startPosition, endPosition))
        {
            return;
        }

        int pushedCount = world.PushAnimalsAlongPath(
            startPosition,
            endPosition,
            GetPlayerCollisionRadius(),
            AnimalPushClearance);
        if (pushedCount > 0)
        {
            Physics.SyncTransforms();
        }
    }

    private float GetPlayerCollisionRadius()
    {
        if (cachedCapsuleCollider == null)
        {
            return 0.2f;
        }

        Vector3 scale = cachedCapsuleCollider.transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        return Mathf.Max(0.01f, cachedCapsuleCollider.radius * horizontalScale);
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

        Vector3 velocity = cachedRigidbody.linearVelocity;
        if (Mathf.Abs(velocity.y) > PlayerRootYEpsilon)
        {
            velocity.y = 0f;
            cachedRigidbody.linearVelocity = velocity;
        }
    }

    private static Vector3 ClampRootPositionToGroundY(Vector3 position)
    {
        position.y = PlayerRootY;
        return position;
    }

    private bool TryGetPhysicsBlockingSweepHit(
        Vector3 originOffset,
        Vector3 direction,
        float distance,
        bool ignoreLiveAnimals,
        out RaycastHit blockingHit)
    {
        blockingHit = default;
        if (cachedRigidbody == null || distance <= 0f)
        {
            return false;
        }

        CacheDefaultCapsuleColliderCenter();
        if (cachedCapsuleCollider == null || !cachedCapsuleCollider.enabled)
        {
            return cachedRigidbody.SweepTest(
                       direction,
                       out blockingHit,
                       distance,
                       QueryTriggerInteraction.Ignore)
                   && !ShouldIgnorePlayerMovementSweepHit(
                       blockingHit,
                       direction,
                       distance,
                       originOffset,
                       ignoreLiveAnimals);
        }

        int hitCount = CastPlayerMovementCapsuleNonAlloc(
            originOffset,
            direction,
            distance);
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = playerMovementSweepHits[i];
            if (hit.collider == null
                || hit.collider.attachedRigidbody == cachedRigidbody
                || Physics.GetIgnoreCollision(cachedCapsuleCollider, hit.collider)
                || ShouldIgnorePlayerMovementSweepHit(
                    hit,
                    direction,
                    distance,
                    originOffset,
                    ignoreLiveAnimals)
                || hit.distance >= nearestDistance)
            {
                continue;
            }

            blockingHit = hit;
            nearestDistance = hit.distance;
        }

        return !float.IsPositiveInfinity(nearestDistance);
    }

    private int CastPlayerMovementCapsuleNonAlloc(
        Vector3 originOffset,
        Vector3 direction,
        float distance)
    {
        GetPlayerMovementCapsuleWorldGeometry(
            out Vector3 point1,
            out Vector3 point2,
            out float radius);
        point1 += originOffset;
        point2 += originOffset;
        int collisionMask = GetPlayerMovementCollisionMask();
        while (true)
        {
            int hitCount = Physics.CapsuleCastNonAlloc(
                point1,
                point2,
                radius,
                direction,
                playerMovementSweepHits,
                distance,
                collisionMask,
                QueryTriggerInteraction.Ignore);
            if (hitCount < playerMovementSweepHits.Length
                || playerMovementSweepHits.Length >= MaxPlayerMovementSweepHitBufferSize)
            {
                return hitCount;
            }

            int expandedCapacity = Mathf.Min(
                playerMovementSweepHits.Length * 2,
                MaxPlayerMovementSweepHitBufferSize);
            System.Array.Resize(ref playerMovementSweepHits, expandedCapacity);
        }
    }

    private void GetPlayerMovementCapsuleWorldGeometry(
        out Vector3 point1,
        out Vector3 point2,
        out float radius)
    {
        Transform colliderTransform = cachedCapsuleCollider.transform;
        Vector3 scale = colliderTransform.lossyScale;
        Vector3 localAxis;
        float axisScale;
        float radiusScale;
        switch (cachedCapsuleCollider.direction)
        {
            case 0:
                localAxis = Vector3.right;
                axisScale = Mathf.Abs(scale.x);
                radiusScale = Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                break;
            case 2:
                localAxis = Vector3.forward;
                axisScale = Mathf.Abs(scale.z);
                radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                break;
            default:
                localAxis = Vector3.up;
                axisScale = Mathf.Abs(scale.y);
                radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                break;
        }

        radius = Mathf.Max(0.0001f, cachedCapsuleCollider.radius * radiusScale);
        float height = Mathf.Max(radius * 2f, cachedCapsuleCollider.height * axisScale);
        float halfSegment = Mathf.Max(0f, (height * 0.5f) - radius);
        Vector3 worldAxis = colliderTransform.TransformDirection(localAxis).normalized;
        Vector3 center = colliderTransform.TransformPoint(cachedCapsuleCollider.center);
        Vector3 segmentOffset = worldAxis * halfSegment;
        point1 = center + segmentOffset;
        point2 = center - segmentOffset;
    }

    private int GetPlayerMovementCollisionMask()
    {
        int playerLayer = gameObject.layer;
        if (playerMovementCollisionMaskLayer == playerLayer)
        {
            return playerMovementCollisionMask;
        }

        int collisionMask = 0;
        for (int layer = 0; layer < 32; layer++)
        {
            if (!Physics.GetIgnoreLayerCollision(playerLayer, layer))
            {
                collisionMask |= 1 << layer;
            }
        }

        playerMovementCollisionMaskLayer = playerLayer;
        playerMovementCollisionMask = collisionMask;
        return collisionMask;
    }

    private bool ShouldIgnorePlayerMovementSweepHit(
        RaycastHit hit,
        Vector3 direction,
        float sweepDistance,
        Vector3 originOffset,
        bool ignoreLiveAnimals)
    {
        AnimalAIController animalController = hit.collider != null
            ? hit.collider.GetComponentInParent<AnimalAIController>()
            : null;
        if (ignoreLiveAnimals
            && animalController != null
            && animalController.IsConfigured)
        {
            return true;
        }

        // A cast that starts overlapped reports a zero-distance hit and normally
        // blocks every direction. Allow only a small step that measurably reduces
        // the existing penetration so the player can walk back out without being
        // allowed to move farther into, or tunnel through, the obstacle.
        if (hit.distance <= MinPhysicsMoveDistance
            && IsPlayerMovementEscapingPenetration(
                hit.collider,
                direction,
                sweepDistance,
                originOffset))
        {
            return true;
        }

        Pipe pipe = hit.collider != null ? hit.collider.GetComponentInParent<Pipe>() : null;
        if (pipe == null
            || !TryResolvePipeBridgeBelt(pipe, out ConvayorBelt2F belt2F)
            || belt2F == null)
        {
            return false;
        }

        Vector3 currentPosition = cachedRigidbody != null ? cachedRigidbody.position : transform.position;
        currentPosition += originOffset;
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

    private bool IsPlayerMovementEscapingPenetration(
        Collider obstacle,
        Vector3 direction,
        float sweepDistance,
        Vector3 originOffset)
    {
        CacheDefaultCapsuleColliderCenter();
        if (cachedCapsuleCollider == null
            || !cachedCapsuleCollider.enabled
            || obstacle == null
            || direction.sqrMagnitude <= MinPhysicsMoveDistanceSqr
            || sweepDistance <= MinPhysicsMoveDistance)
        {
            return false;
        }

        Transform capsuleTransform = cachedCapsuleCollider.transform;
        Vector3 capsulePosition = capsuleTransform.position + originOffset;
        Quaternion capsuleRotation = capsuleTransform.rotation;
        if (!Physics.ComputePenetration(
                cachedCapsuleCollider,
                capsulePosition,
                capsuleRotation,
                obstacle,
                obstacle.transform.position,
                obstacle.transform.rotation,
                out _,
                out float currentPenetration))
        {
            return false;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude <= MinPhysicsMoveDistanceSqr)
        {
            return false;
        }

        float probeDistance = Mathf.Min(
            sweepDistance,
            PlayerPenetrationEscapeProbeDistance);
        Vector3 probePosition = capsulePosition + (direction.normalized * probeDistance);
        if (!Physics.ComputePenetration(
                cachedCapsuleCollider,
                probePosition,
                capsuleRotation,
                obstacle,
                obstacle.transform.position,
                obstacle.transform.rotation,
                out _,
                out float probePenetration))
        {
            return true;
        }

        return probePenetration < currentPenetration - MinPhysicsMoveDistance;
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
        if (interactionPointSnapTarget != null
            && (interactionPointSnapVehicle != null || interactionPointSnapAnimal != null))
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
        if (!finishedPickThisFrame)
        {
            return;
        }

        if (pendingHarvestResources.Count > 0)
        {
            ResolveCompletedResourceHarvest();
            return;
        }

        if (pendingCorpseHarvestAnimals.Count > 0)
        {
            ResolveCompletedCorpseHarvest();
        }
    }

    private void ResolveCompletedResourceHarvest()
    {
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

    private void ResolveCompletedCorpseHarvest()
    {
        Animal harvestedCorpse = pendingCorpseHarvestAnimals.Dequeue();
        if (harvestedCorpse == null || !harvestedCorpse.CanHarvestCorpse)
        {
            currentCorpseHarvestTarget = null;
            return;
        }

        bool hasReward = harvestedCorpse.TryGetPreparedCorpseLootItem(out int itemId);
        bool rewardDelivered = !hasReward || TryDeliverCorpseLoot(harvestedCorpse, itemId);
        if (!harvestedCorpse.CommitPreparedCorpseHarvestStep(rewardDelivered))
        {
            harvestedCorpse.CancelPreparedCorpseHarvestStep();
            currentCorpseHarvestTarget = null;
            return;
        }

        if (!harvestedCorpse.HasRemainingCorpseLoot)
        {
            currentCorpseHarvestTarget = null;
            RemoveHarvestedCorpse(harvestedCorpse);
            return;
        }

        if (harvestedCorpse != currentCorpseHarvestTarget
            || !IsAnimalWithinKnifeInteractionRange(harvestedCorpse)
            || !QueueCorpseHarvestStep(harvestedCorpse))
        {
            currentCorpseHarvestTarget = null;
        }
    }

    private bool TryDeliverCorpseLoot(Animal corpse, int itemId)
    {
        if (corpse == null || itemId < 0 || player == null)
        {
            return false;
        }

        ItemDefinition itemDefinition = corpse.ResolveCorpseLootItemDefinition(itemId);
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (itemDefinition != null)
        {
            itemManager?.RegisterRuntimeItemDefinition(itemDefinition);
        }

        return player.TryAddToBagAnimated(
            itemId,
            corpse.GetCorpseLootOrigin());
    }

    private void RemoveHarvestedCorpse(Animal corpse)
    {
        if (corpse == null)
        {
            return;
        }

        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain != null && terrain.RemoveAnimal(corpse))
        {
            return;
        }

        TerrainAnimalInstance instance = corpse.GetComponentInParent<TerrainAnimalInstance>();
        GameObject corpseRoot = instance != null ? instance.gameObject : corpse.gameObject;
        Destroy(corpseRoot);
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

    private Resource FindNearestResourceInteractionTarget()
    {
        if (!CanPrepareHandForResourceHarvest())
        {
            return null;
        }

        return FindNearestResourceInteractionTarget(false);
    }

    private bool CanPrepareHandForResourceHarvest()
    {
        return player != null
               && (!player.IsCarrying || player.CanClearHandIntoBag());
    }

    private bool TryPrepareHandForResourceHarvest()
    {
        return player != null
               && (!player.IsCarrying || player.TryStoreHandItemsInBag());
    }

    public bool TryFindNearestBucketFluidSource(
        out Block sourceBlock,
        out Resource oilSource)
    {
        sourceBlock = null;
        oilSource = null;
        if (player == null || IsMounted)
        {
            return false;
        }

        oilSource = FindNearestResourceInteractionTarget(true);
        if (oilSource != null)
        {
            sourceBlock = ResolveResourceOwningBlock(oilSource);
            if (sourceBlock != null)
            {
                return true;
            }

            oilSource = null;
        }

        return TryFindNearestWaterInteractionBlock(out sourceBlock);
    }

    private bool TryFindNearestWaterInteractionBlock(out Block waterBlock)
    {
        waterBlock = null;
        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null)
        {
            return false;
        }

        Vector3 rootPosition = cachedRigidbody != null
            ? cachedRigidbody.position
            : transform.position;
        Vector2 center = GetPlayerCollisionCenterXZ(rootPosition);
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(center.x),
            Mathf.RoundToInt(center.y));
        float nearestDistanceSqr = float.MaxValue;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!terrain.IsWaterBiomeAt(coordinate)
                    || !terrain.TryGetLoadedBlock(coordinate, out Block block)
                    || block == null)
                {
                    continue;
                }

                float deltaX = block.WorldPosition.x - center.x;
                float deltaZ = block.WorldPosition.z - center.y;
                float distanceSqr = deltaX * deltaX + deltaZ * deltaZ;
                if (distanceSqr >= nearestDistanceSqr)
                {
                    continue;
                }

                nearestDistanceSqr = distanceSqr;
                waterBlock = block;
            }
        }

        return waterBlock != null;
    }

    private Resource FindNearestResourceInteractionTarget(bool oilOnly)
    {
        if (player == null)
        {
            return null;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        float harvestRange = player.State.HarvestRange;
        float maxDistanceSqr = harvestRange * harvestRange;
        float nearestDistanceSqr = float.MaxValue;
        Resource nearestResource = null;

        IReadOnlyList<Resource> resources = Resource.ActiveResources;
        bool usingNearbyResourceCandidates = TryCollectNearbyResourceCandidates(origin, harvestRange, out IReadOnlyList<Resource> nearbyResources);
        if (usingNearbyResourceCandidates)
        {
            resources = nearbyResources;
        }

        for (int i = 0; i < resources.Count; i++)
        {
            Resource resource = resources[i];
            bool isOil = resource != null
                         && resource.PlacementCategory == ResourceDefinition.PlacementCategory.Oil;
            if (resource == null
                || !resource.gameObject.activeInHierarchy
                || !resource.AllowsFocus
                || !resource.CanHarvest
                || isOil != oilOnly)
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
            if (distanceSqr > maxDistanceSqr)
            {
                continue;
            }

            if (distanceSqr < nearestDistanceSqr)
            {
                nearestDistanceSqr = distanceSqr;
                nearestResource = resource;
            }
        }

        if (usingNearbyResourceCandidates)
        {
            nearbyResourceCandidates.Clear();
        }

        return nearestResource;
    }

    private bool TryCollectNearbyResourceCandidates(
        Vector3 origin,
        float harvestRange,
        out IReadOnlyList<Resource> resources)
    {
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(harvestRange + 1f));
        if (Resource.TryCollectActiveResourcesInCoordinateRange(center, searchRadius, nearbyResourceCandidates))
        {
            resources = nearbyResourceCandidates;
            return true;
        }

        resources = null;
        return false;
    }

    private static Block ResolveResourceOwningBlock(Resource resource)
    {
        if (resource == null)
        {
            return null;
        }

        Block owningBlock = resource.OwningBlock;
        if (owningBlock == null)
        {
            TerrainGenerator terrain = TerrainGenerator.Active;
            Vector3 position = resource.transform.position;
            terrain?.TryGetLoadedBlock(
                new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z)),
                out owningBlock);
        }
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

    private void RefreshInteractionFocus()
    {
        standaloneInteractionAreaFocusBlock = null;
        if (MountedVehicle != null)
        {
            UpdateInRangeSprinklerRangeVisuals(null);
            RefreshMountedPinnedInteractionFocus();
            return;
        }

        ResetInteractionButtonFocusTargets();
        ExpireTemporaryDropFocusIfNeeded();

        interactionFocusTargetOverrides.Clear();
        TryGetStandingConveyorFocusBlock(out Block standingConveyorFocusBlock);

        combinedInteractionFocusBlocks.Clear();
        AppendUniqueBlock(combinedInteractionFocusBlocks, standingConveyorFocusBlock);
        Resource resourceInteractionTarget = FindNearestResourceInteractionTarget();
        if (resourceInteractionTarget != null)
        {
            Block resourceBlock = ResolveResourceOwningBlock(resourceInteractionTarget);
            AppendUniqueBlock(combinedInteractionFocusBlocks, resourceBlock);
            CacheInteractionButtonFocusTarget(resourceInteractionTarget, resourceBlock);
        }

        bool hasStandingAreaFocusBlock = TryGetStandingInputOutputAreaFocusBlock(
            out Block standingAreaFocusBlock,
            out InputOutputModule standingAreaOwnerModule);
        if (FindCurrentInputOutputModuleFocusBlocks(nearbyInputOutputModuleFocusBlocks))
        {
            AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInputOutputModuleFocusBlocks);
        }

        FindNearbyWorkableBlocks(nearbyWorkableFocusBlocks);
        UpdateSelectedWorkableRangeVisuals(nearbyWorkableRangeObjects);
        Vector3 playerRangeOrigin = player.BodyTransform != null
            ? player.BodyTransform.position
            : transform.position;
        Sprinkler.CollectActiveSprinklersContainingWorldPosition(
            playerRangeOrigin,
            nextInRangeSprinklerRangeObjects);
        UpdateInRangeSprinklerRangeVisuals(nextInRangeSprinklerRangeObjects);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyWorkableFocusBlocks);

        FindNearbyBoxBlocks(nearbyBoxFocusBlocks);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyBoxFocusBlocks);

        FindNearbyInstallationBlocks(
            nearbyInstallationFocusBlocks,
            standingConveyorFocusBlock);
        AppendUniqueBlocks(combinedInteractionFocusBlocks, nearbyInstallationFocusBlocks);

        CacheInteractionButtonFocusTargets(
            combinedInteractionFocusBlocks,
            hasStandingAreaFocusBlock ? standingAreaFocusBlock : null);
        KeepClosestInteractionFocusTarget(combinedInteractionFocusBlocks);
        if (hasStandingAreaFocusBlock)
        {
            BuildStandingAreaAndObjectFocusBlocks(
                standingAreaFocusBlock,
                standingAreaOwnerModule,
                combinedInteractionFocusBlocks);
        }

        if (TryGetStandingSeedGroundBlock(out Block standingSeedGroundBlock, out _))
        {
            AppendUniqueBlock(combinedInteractionFocusBlocks, standingSeedGroundBlock);
        }

        if (player.IsHoldingEmptyBucket
            && TryFindNearestBucketFluidSource(out Block bucketFluidBlock, out _))
        {
            AppendUniqueBlock(combinedInteractionFocusBlocks, bucketFluidBlock);
        }

        if (TryFindNearestPlantWateringTarget(
                out Block plantWateringBlock,
                out _))
        {
            AppendUniqueBlock(combinedInteractionFocusBlocks, plantWateringBlock);
        }

        bool hasFarmlandFocusGroup = false;
        if (combinedInteractionFocusBlocks.Count == 0
            && TryFindNearestFarmlandFocusBlock(out Block farmlandFocusBlock))
        {
            hasFarmlandFocusGroup = AppendConnectedFarmlandFocusBlocks(
                farmlandFocusBlock,
                combinedInteractionFocusBlocks,
                currentInteractionFarmlandFocusGroup);
            if (!hasFarmlandFocusGroup)
            {
                AppendUniqueBlock(combinedInteractionFocusBlocks, farmlandFocusBlock);
            }
        }

        SetFocusedBlocks(combinedInteractionFocusBlocks, hasFarmlandFocusGroup);
    }

    private void RefreshMountedPinnedInteractionFocus()
    {
        standaloneInteractionAreaFocusBlock = null;
        ResetInteractionButtonFocusTargets();
        interactionFocusTargetOverrides.Clear();
        mountedPinnedFocusBlocks.Clear();

        if (!IsValidMouseFocusMapObject(mountedPinnedFocusTarget))
        {
            mountedPinnedFocusTarget = MountedVehicle;
            mountedPinnedFocusFallbackBlock = null;
        }

        if (mountedPinnedFocusTarget == null
            || !AppendMapObjectFocusBlocks(
                mountedPinnedFocusTarget,
                mountedPinnedFocusFallbackBlock,
                mountedPinnedFocusBlocks))
        {
            SetFocusedBlocks(null);
            return;
        }

        CacheInteractionButtonFocusTargets(mountedPinnedFocusBlocks, null);
        SetFocusedBlocks(mountedPinnedFocusBlocks);
    }

    private void ResetInteractionButtonFocusTargets()
    {
        interactionButtonFocusTargets.Clear();
        interactionButtonFocusTargetBlocks.Clear();
    }

    private void CacheInteractionButtonFocusTargets(
        List<Block> focusBlocks,
        Block additionalBlock)
    {
        if (focusBlocks != null)
        {
            for (int i = 0; i < focusBlocks.Count; i++)
            {
                CacheInteractionButtonFocusTarget(focusBlocks[i]);
            }
        }

        CacheInteractionButtonFocusTarget(additionalBlock);
    }

    private void CacheInteractionButtonFocusTarget(Block block)
    {
        CacheInteractionButtonFocusTarget(ResolveInteractionFocusTarget(block), block);
    }

    private void CacheInteractionButtonFocusTarget(MapObject target, Block fallbackBlock)
    {
        if (target == null
            || !target.gameObject.activeInHierarchy
            || !target.AllowsFocus
            || interactionButtonFocusTargets.Contains(target))
        {
            return;
        }

        interactionButtonFocusTargets.Add(target);
        interactionButtonFocusTargetBlocks.Add(fallbackBlock);
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

        for (int i = 0; i < focusBlocks.Count; i++)
        {
            Block block = focusBlocks[i];
            if (block == null)
            {
                continue;
            }

            MapObject target = ResolveInteractionFocusTarget(block);
            float distanceSqr = GetInteractionFocusTargetDistanceSqr(target, block, origin);
            if (distanceSqr >= closestDistanceSqr)
            {
                continue;
            }

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

        if (Spliterbelt.TryFindCoveringBelt(block.Coordinate, out Spliterbelt splitter))
            return splitter;

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
            temporaryDropFocusBlock.SetTemporaryDropFocusVisible(true);
        }
    }

    private bool TryGetStandingConveyorFocusBlock(out Block standingBlock)
    {
        standingBlock = null;
        if (!TryGetStandingConveyorBlock(out standingBlock)
            || standingBlock == null
            || !TryResolveConveyorFocusTarget(standingBlock, out ConveyorBelt conveyorBelt)
            || conveyorBelt == null
            || !conveyorBelt.gameObject.activeInHierarchy
            || !conveyorBelt.AllowsFocus)
        {
            return false;
        }

        return true;
    }

    public void CollectFocusedWorkableObjectItemIds(HashSet<int> itemIds)
    {
        if (itemIds == null)
        {
            return;
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
            if (itemId >= 0)
            {
                itemIds.Add(itemId);
            }
        }
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
                || !TryResolveConveyorFocusTarget(block, out ConveyorBelt conveyorBelt)
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

    private static bool TryResolveConveyorFocusTarget(Block block, out ConveyorBelt belt)
    {
        belt = block != null ? block.MapObject as ConveyorBelt : null;
        if (belt == null && block != null && Spliterbelt.TryFindCoveringBelt(block.Coordinate, out Spliterbelt splitter))
            belt = splitter;
        return belt != null;
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

    public bool IsWithinInteractionRange(MapObject mapObject)
    {
        if (player == null
            || mapObject == null
            || !mapObject.gameObject.activeInHierarchy
            || !mapObject.AllowsFocus)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
        if (mapObject is Resource resource)
        {
            if (!CanPrepareHandForResourceHarvest() || !resource.CanHarvest)
            {
                return false;
            }

            float harvestRange = player.State.HarvestRange;
            return GetResourceFocusSelectionDistanceSqr(resource, origin) <= harvestRange * harvestRange;
        }

        if (!(mapObject is InstallationObject installationObject))
        {
            return false;
        }

        float interactionRadius = Mathf.Max(0f, installationObject.FocusActivationRadius);
        return interactionRadius > 0f
               && GetMapObjectFocusSelectionDistanceSqr(installationObject, null, origin)
               <= interactionRadius * interactionRadius;
    }

    public int InteractionButtonFocusTargetCount => interactionButtonFocusTargets.Count;

    public bool TryGetInteractionButtonFocusTarget(
        int index,
        out MapObject focusTarget,
        out float distanceSqr)
    {
        focusTarget = null;
        distanceSqr = float.MaxValue;
        if (player == null
            || index < 0
            || index >= interactionButtonFocusTargets.Count
            || index >= interactionButtonFocusTargetBlocks.Count)
        {
            return false;
        }

        MapObject candidate = interactionButtonFocusTargets[index];
        if (candidate == null
            || !candidate.gameObject.activeInHierarchy
            || !candidate.AllowsFocus)
        {
            return false;
        }

        Block fallbackBlock = interactionButtonFocusTargetBlocks[index];
        Vector3 origin = player.BodyTransform != null
            ? player.BodyTransform.position
            : transform.position;
        distanceSqr = GetInteractionFocusTargetDistanceSqr(
            candidate,
            fallbackBlock,
            origin);
        focusTarget = candidate;
        return distanceSqr < float.MaxValue;
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

    public bool TryGetMouseFocusedAnimal(out Animal focusedAnimal)
    {
        RefreshMouseMapObjectFocus();
        focusedAnimal = currentMouseFocusedAnimal;
        return focusedAnimal != null
               && focusedAnimal.gameObject.activeInHierarchy;
    }

    public bool TryResolvePointerFocusTarget(
        Vector2 pointerPosition,
        out Animal focusedAnimal,
        out MapObject focusedMapObject)
    {
        return TryResolveMouseFocusTargets(
                   pointerPosition,
                   out focusedAnimal,
                   out focusedMapObject,
                   out _,
                   out _)
               && (focusedAnimal != null || focusedMapObject != null);
    }

    public bool TryResolvePointerFocusTarget(
        Vector2 pointerPosition,
        out Animal focusedAnimal,
        out MapObject focusedMapObject,
        out PortableObject focusedPortableObject)
    {
        return TryResolvePointerFocusTarget(
            pointerPosition,
            out focusedAnimal,
            out focusedMapObject,
            out focusedPortableObject,
            out _);
    }

    public bool TryResolvePointerFocusTarget(
        Vector2 pointerPosition,
        out Animal focusedAnimal,
        out MapObject focusedMapObject,
        out PortableObject focusedPortableObject,
        out Block focusedBlock)
    {
        return TryResolveMouseFocusTargets(
                   pointerPosition,
                   out focusedAnimal,
                   out focusedMapObject,
                   out focusedPortableObject,
                   out focusedBlock)
               && (focusedAnimal != null
                   || focusedMapObject != null
                   || focusedPortableObject != null);
    }

    public bool RequestAnimalKnifeInteraction(Animal animal)
    {
        if (animal == null
            || !animal.gameObject.activeInHierarchy
            || player == null
            || interactionPointSnapTarget != null)
        {
            return false;
        }

        if (!animal.IsAlive)
        {
            return RequestCorpseHarvest(animal);
        }

        if (!animal.CanBeAttacked || player.IsCarrying)
        {
            return false;
        }

        if (currentKnifeTargetAnimal == animal || animalKnifePickPending)
        {
            return true;
        }

        CancelActiveResourceHarvest();
        CancelActiveCorpseHarvest();
        currentKnifeTargetAnimal = animal;
        return true;
    }

    public bool RequestNooseThrow(ItemDefinition nooseDefinition)
    {
        if (nooseDefinition == null
            || nooseDefinition.portableMat == null
            || player == null
            || interactionPointSnapTarget != null
            || GameManager.TextInputFocused
            || (GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked))
        {
            return false;
        }

        PlayerBag handBag = player.GetHandBag();
        if (handBag == null
            || handBag.GetSlotCount(0) <= 0
            || handBag.GetSlotItemId(0) != nooseDefinition.id)
        {
            return false;
        }

        if (activeNooseThrowVisual != null)
        {
            if (!activeNooseThrowVisual.HasAttachedAnimal)
            {
                return false;
            }

            CancelNooseThrow(true);
            return true;
        }

        Transform body = player.BodyTransform != null ? player.BodyTransform : transform;
        Vector3 throwDirection = body.forward;
        throwDirection.y = 0f;
        if (throwDirection.sqrMagnitude <= 0.0001f)
        {
            throwDirection = transform.forward;
            throwDirection.y = 0f;
        }

        if (throwDirection.sqrMagnitude <= 0.0001f)
        {
            throwDirection = Vector3.forward;
        }

        CancelActiveResourceHarvest();
        CancelAnimalKnifeInteraction();
        pendingMoveDirection = Vector3.zero;
        pendingFacingDirection = Vector3.zero;
        hasPendingFacingDirection = false;

        if (CreateNooseThrowVisual(
                handBag,
                nooseDefinition.id,
                throwDirection.normalized,
                nooseDefinition.portableMat) == null)
        {
            return false;
        }

        if (player.TriggerThrowAnimation())
        {
            return true;
        }

        CancelNooseThrow();
        return false;
    }

    private NooseThrowVisual CreateNooseThrowVisual(
        PlayerBag handBag,
        int nooseItemId,
        Vector3 throwDirection,
        Material material)
    {
        if (handBag == null || nooseItemId < 0 || material == null)
        {
            return null;
        }

        GameObject visualObject = new GameObject("NooseThrowVisual");
        visualObject.layer = gameObject.layer;
        activeNooseThrowVisual = visualObject.AddComponent<NooseThrowVisual>();
        activeNooseThrowVisual.Initialize(
            handBag.transform,
            nooseItemId,
            throwDirection,
            material,
            NooseThrowDistance,
            NooseThrowWindupDuration,
            NooseThrowOutboundDuration,
            NooseThrowHoldDuration,
            NooseThrowReturnDuration,
            NooseThrowArcHeight);
        return activeNooseThrowVisual;
    }

    private void CancelNooseThrow(bool consumeAttachedNoose = false)
    {
        if (activeNooseThrowVisual == null)
        {
            return;
        }

        activeNooseThrowVisual.ReleaseAttachment(consumeAttachedNoose);
        Destroy(activeNooseThrowVisual.gameObject);
        activeNooseThrowVisual = null;
    }

    private float GetCurrentOnFootMoveSpeed()
    {
        float playerMoveSpeed = player != null
            ? Mathf.Max(0f, player.Stat.currentMoveSpeed)
            : 0f;
        if (activeNooseThrowVisual == null
            || !activeNooseThrowVisual.HasAttachedAnimal)
        {
            return playerMoveSpeed;
        }

        return Mathf.Min(
            playerMoveSpeed,
            activeNooseThrowVisual.AttachedMovementSpeedLimit);
    }

    private float GetCurrentLocomotionBlend(Vector3 moveDirection)
    {
        if (player == null)
        {
            return 1f;
        }

        float normalMoveSpeed = Mathf.Max(0f, player.Stat.moveSpeed);
        if (normalMoveSpeed <= 0.0001f)
        {
            return 1f;
        }

        float inputMagnitude = Mathf.Clamp01(moveDirection.magnitude);
        float actualRequestedSpeed = GetCurrentOnFootMoveSpeed() * inputMagnitude;
        return Mathf.Clamp01(actualRequestedSpeed / normalMoveSpeed);
    }

    public bool IsAnimalWithinKnifeInteractionRange(Animal animal)
    {
        return IsAnimalWithinInteractionRange(animal);
    }

    public bool IsAnimalWithinInteractionRange(Animal animal)
    {
        if (animal == null
            || !animal.gameObject.activeInHierarchy
            || player == null
            || interactionPointSnapTarget != null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null
            ? player.BodyTransform.position
            : transform.position;
        Vector3 offset = animal.transform.position - origin;
        offset.y = 0f;
        float interactionRange = Mathf.Max(
            MinimumAnimalKnifeInteractionRange,
            player.State.HarvestRange);
        return offset.sqrMagnitude <= interactionRange * interactionRange;
    }

    public bool TryGetNearestAutomaticInteractionAnimal(
        bool includeCorpses,
        out Animal animal)
    {
        animal = null;
        if (player == null || interactionPointSnapTarget != null)
        {
            return false;
        }

        bool cachedAnimalIsValid = cachedAutomaticInteractionAnimal != null
                                   && cachedAutomaticInteractionAnimal.gameObject.activeInHierarchy
                                   && (cachedAutomaticInteractionAnimal.CanBeMounted
                                       || (includeCorpses
                                           && cachedAutomaticInteractionAnimal.CanHarvestCorpse))
                                   && IsAnimalWithinInteractionRange(cachedAutomaticInteractionAnimal);
        bool cachedAnimalNeedsRefresh = cachedAutomaticInteractionAnimal != null
                                        && !cachedAnimalIsValid;
        if (cachedAutomaticInteractionIncludesCorpses != includeCorpses
            || Time.unscaledTime >= nextAutomaticAnimalInteractionRefreshTime
            || cachedAnimalNeedsRefresh)
        {
            Transform bodyTransform = player.BodyTransform;
            Vector3 origin = bodyTransform != null ? bodyTransform.position : transform.position;
            float interactionRange = Mathf.Max(
                MinimumAnimalKnifeInteractionRange,
                player.State.HarvestRange);
            TryFindNearestAutomaticInteractionAnimal(
                origin,
                interactionRange,
                includeCorpses,
                out cachedAutomaticInteractionAnimal);
            cachedAutomaticInteractionIncludesCorpses = includeCorpses;
            nextAutomaticAnimalInteractionRefreshTime =
                Time.unscaledTime + AutomaticAnimalInteractionRefreshInterval;
        }

        animal = cachedAutomaticInteractionAnimal;
        return animal != null;
    }

    private bool TryFindNearestAutomaticInteractionAnimal(
        Vector3 origin,
        float maximumDistance,
        bool includeCorpses,
        out Animal nearestAnimal)
    {
        nearestAnimal = null;
        float resolvedDistance = Mathf.Max(0f, maximumDistance);
        int hitCount = Physics.OverlapSphereNonAlloc(
            origin,
            resolvedDistance,
            automaticAnimalInteractionOverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        Animal nearestRideableAnimal = null;
        Animal nearestCorpse = null;
        float maximumDistanceSqr = resolvedDistance * resolvedDistance;
        float nearestRideableDistanceSqr = maximumDistanceSqr;
        float nearestCorpseDistanceSqr = maximumDistanceSqr;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = automaticAnimalInteractionOverlapBuffer[i];
            Animal candidate = hit != null ? hit.GetComponentInParent<Animal>() : null;
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - origin;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (candidate.CanBeMounted)
            {
                if (distanceSqr <= nearestRideableDistanceSqr)
                {
                    nearestRideableAnimal = candidate;
                    nearestRideableDistanceSqr = distanceSqr;
                }

                continue;
            }

            if (includeCorpses
                && candidate.CanHarvestCorpse
                && distanceSqr <= nearestCorpseDistanceSqr)
            {
                nearestCorpse = candidate;
                nearestCorpseDistanceSqr = distanceSqr;
            }
        }

        nearestAnimal = nearestRideableAnimal != null
            ? nearestRideableAnimal
            : nearestCorpse;
        return nearestAnimal != null;
    }

    private bool RequestCorpseHarvest(Animal corpse)
    {
        if (!corpse.CanHarvestCorpse || !IsAnimalWithinKnifeInteractionRange(corpse))
        {
            return false;
        }

        if (currentCorpseHarvestTarget == corpse
            && pendingCorpseHarvestAnimals.Count > 0)
        {
            return true;
        }

        CancelActiveResourceHarvest();
        CancelAnimalKnifeAttack();
        if (currentCorpseHarvestTarget != corpse)
        {
            CancelActiveCorpseHarvest();
            currentCorpseHarvestTarget = corpse;
        }

        if (QueueCorpseHarvestStep(corpse))
        {
            return true;
        }

        currentCorpseHarvestTarget = null;
        return false;
    }

    private bool QueueCorpseHarvestStep(Animal corpse)
    {
        if (corpse == null
            || !corpse.CanHarvestCorpse
            || !IsAnimalWithinKnifeInteractionRange(corpse)
            || !corpse.PrepareCorpseHarvestStep())
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null
            ? player.BodyTransform.position
            : transform.position;
        Vector3 facingDirection = corpse.transform.position - origin;
        facingDirection.y = 0f;
        if (facingDirection.sqrMagnitude > 0.0001f)
        {
            pendingFacingDirection = facingDirection.normalized;
            hasPendingFacingDirection = true;
        }

        pendingCorpseHarvestAnimals.Enqueue(corpse);
        player.QueuePickAnimation();
        return true;
    }

    private bool TryGetAnimalKnifeApproachDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        Animal targetAnimal = currentKnifeTargetAnimal;
        if (targetAnimal == null
            || !targetAnimal.gameObject.activeInHierarchy
            || !targetAnimal.CanBeAttacked
            || player == null
            || player.IsCarrying)
        {
            CancelAnimalKnifeApproach();
            return false;
        }

        Vector3 origin = player.BodyTransform != null
            ? player.BodyTransform.position
            : transform.position;
        Vector3 offset = targetAnimal.transform.position - origin;
        offset.y = 0f;
        float interactionRange = Mathf.Max(
            MinimumAnimalKnifeInteractionRange,
            player.State.HarvestRange);
        if (offset.sqrMagnitude > interactionRange * interactionRange)
        {
            direction = offset.normalized;
            return true;
        }

        currentKnifeTargetAnimal = null;
        if (offset.sqrMagnitude > 0.0001f)
        {
            pendingFacingDirection = offset.normalized;
            hasPendingFacingDirection = true;
        }

        pendingKnifeTargetAnimal = targetAnimal;
        animalKnifePickPending = true;
        animalKnifeAnimationStarted = false;
        animalKnifeDamageApplied = false;
        animalKnifeDamageTime = 0f;
        animalKnifeInteractionEndTime = 0f;
        animalKnifeInteractionTimeout = Time.time + AnimalKnifeInteractionTimeout;
        player.QueuePickAnimation();
        return false;
    }

    private void TryStartPendingAnimalKnifeAttack()
    {
        if (!animalKnifePickPending
            || animalKnifeAnimationStarted
            || player == null
            || !player.PickAnimationStartedThisFrame)
        {
            return;
        }

        animalKnifeAnimationStarted = true;
        float animationStartTime = Time.time;
        animalKnifeDamageTime = animationStartTime + AnimalKnifeDamageDelay;
        animalKnifeInteractionEndTime = animationStartTime + PickAnimationDuration;
        animalKnifeInteractionTimeout = animationStartTime + AnimalKnifeInteractionTimeout;
        pendingKnifeTargetAnimal?.NotifyAttackAnimationStarted();
    }

    private void ApplyPendingAnimalKnifeDamage()
    {
        if (!animalKnifePickPending || animalKnifeDamageApplied)
        {
            return;
        }

        animalKnifeDamageApplied = true;
        if (pendingKnifeTargetAnimal != null && pendingKnifeTargetAnimal.CanBeAttacked)
        {
            pendingKnifeTargetAnimal.TakeDamage(
                AnimalKnifeDamage,
                player != null ? player.transform.position : transform.position);
        }
    }

    private void CancelAnimalKnifeApproach()
    {
        currentKnifeTargetAnimal = null;
    }

    private void ClearPendingAnimalKnifeAttack()
    {
        pendingKnifeTargetAnimal = null;
        animalKnifePickPending = false;
        animalKnifeAnimationStarted = false;
        animalKnifeDamageApplied = false;
        animalKnifeDamageTime = 0f;
        animalKnifeInteractionEndTime = 0f;
        animalKnifeInteractionTimeout = 0f;
    }

    private void CancelAnimalKnifeAttack()
    {
        bool hadActiveInteraction = currentKnifeTargetAnimal != null || animalKnifePickPending;
        CancelAnimalKnifeApproach();
        ClearPendingAnimalKnifeAttack();
        if (hadActiveInteraction)
        {
            player?.ClearQueuedPickAnimations();
        }
    }

    private void CancelActiveCorpseHarvest()
    {
        if (currentCorpseHarvestTarget == null && pendingCorpseHarvestAnimals.Count == 0)
        {
            return;
        }

        while (pendingCorpseHarvestAnimals.Count > 0)
        {
            pendingCorpseHarvestAnimals.Dequeue()?.CancelPreparedCorpseHarvestStep();
        }

        currentCorpseHarvestTarget = null;
        player?.ClearQueuedPickAnimations();
    }

    private void CancelAnimalKnifeInteraction()
    {
        CancelAnimalKnifeAttack();
        CancelActiveCorpseHarvest();
    }

    public bool RequestResourceHarvest(Resource resource)
    {
        if (resource == null
            || !resource.CanHarvest
            || player == null)
        {
            return false;
        }

        if (!IsWithinInteractionRange(resource)
            || !TryPrepareHandForResourceHarvest())
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
        if (player == null)
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
            MapObject mapObject = ResolveInteractionFocusTarget(block);
            if (mapObject == null || !mapObject.gameObject.activeInHierarchy || !mapObject.AllowsFocus)
            {
                continue;
            }

            if (!TryResolveFocusedItemFilterTarget(mapObject, definitions, origin, out MapObject filterTarget))
            {
                continue;
            }

            float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(filterTarget, block, origin);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            focusedMapObject = filterTarget;
        }

        TryFindFreightCarAttachedBoxFilterTarget(
            definitions,
            origin,
            ref nearestDistanceSqr,
            ref focusedMapObject);

        return focusedMapObject != null;
    }

    private bool TryFindFreightCarAttachedBoxFilterTarget(
        List<ItemDefinition> definitions,
        Vector3 origin,
        ref float nearestDistanceSqr,
        ref MapObject focusedMapObject)
    {
        itemFilterTrainScratch.Clear();
        Train.CollectActiveRuntimeTrains(itemFilterTrainScratch);
        if (itemFilterTrainScratch.Count <= 0)
        {
            return false;
        }

        bool found = false;
        Train mountedTrain = MountedVehicle as Train;
        for (int i = 0; i < itemFilterTrainScratch.Count; i++)
        {
            if (!(itemFilterTrainScratch[i] is FreightCar freightCar)
                || freightCar == null
                || !freightCar.gameObject.activeInHierarchy
                || !freightCar.AllowsFocus)
            {
                continue;
            }

            bool isMountedTrainGroup = mountedTrain != null
                                       && IsSameConnectedTrainGroup(mountedTrain, freightCar);
            float freightCarDistanceSqr = GetMapObjectFocusSelectionDistanceSqr(freightCar, null, origin);
            if (!isMountedTrainGroup)
            {
                float focusRadius = Mathf.Max(0f, freightCar.FocusActivationRadius);
                if (focusRadius <= 0f || freightCarDistanceSqr > focusRadius * focusRadius)
                {
                    continue;
                }
            }

            if (!freightCar.TryGetClosestAttachedBoxObject(origin, out BoxObject attachedBox)
                || attachedBox == null
                || !attachedBox.gameObject.activeInHierarchy
                || !SupportsItemFilter(attachedBox, definitions))
            {
                continue;
            }

            float attachedBoxDistanceSqr = GetMapObjectFocusSelectionDistanceSqr(attachedBox, null, origin);
            if (attachedBoxDistanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = attachedBoxDistanceSqr;
            focusedMapObject = attachedBox;
            found = true;
        }

        itemFilterTrainScratch.Clear();
        itemFilterTrainQueue.Clear();
        itemFilterTrainVisited.Clear();
        return found;
    }

    private bool IsSameConnectedTrainGroup(Train first, Train second)
    {
        if (first == null || second == null)
        {
            return false;
        }

        if (first == second)
        {
            return true;
        }

        itemFilterTrainQueue.Clear();
        itemFilterTrainVisited.Clear();
        itemFilterTrainQueue.Enqueue(first);
        itemFilterTrainVisited.Add(first);
        while (itemFilterTrainQueue.Count > 0)
        {
            Train train = itemFilterTrainQueue.Dequeue();
            foreach (Train connectedTrain in train.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !itemFilterTrainVisited.Add(connectedTrain))
                {
                    continue;
                }

                if (connectedTrain == second)
                {
                    return true;
                }

                itemFilterTrainQueue.Enqueue(connectedTrain);
            }
        }

        return false;
    }

    private static bool TryResolveFocusedItemFilterTarget(
        MapObject mapObject,
        List<ItemDefinition> definitions,
        Vector3 origin,
        out MapObject filterTarget)
    {
        filterTarget = null;
        if (mapObject == null)
        {
            return false;
        }

        if (SupportsItemFilter(mapObject, definitions))
        {
            filterTarget = mapObject;
            return true;
        }

        if (TryResolveFreightCar(mapObject, out FreightCar freightCar)
            && freightCar.TryGetClosestAttachedBoxObject(origin, out BoxObject attachedBox)
            && attachedBox != null
            && attachedBox.gameObject.activeInHierarchy
            && SupportsItemFilter(attachedBox, definitions))
        {
            filterTarget = attachedBox;
            return true;
        }

        return false;
    }

    private static bool SupportsItemFilter(MapObject mapObject, List<ItemDefinition> definitions)
    {
        return mapObject != null
               && (IsItemFilterEnabled(mapObject.ResolveItemId(), definitions)
                   || mapObject is Spliterbelt
                   || TryResolveRobotArm(mapObject, out _)
                   || TryResolveProductionMachine(mapObject, out _));
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

    private static bool TryResolveFreightCar(MapObject mapObject, out FreightCar freightCar)
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

        if (ResolveTerrainGenerator() == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : transform.position;
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

        CacheInteractionButtonFocusTarget(
            inputOutputModule,
            results.Count > 0 ? results[0] : null);

        IReadOnlyList<Vector2Int> focusCoordinates = inputOutputModule.RuntimeFocusCoordinates;
        if (focusCoordinates == null || focusCoordinates.Count <= 0)
        {
            return results.Count > 0;
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
                AppendUniqueBlock(results, nearestBlock);
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

    private bool TryGetStandingInputOutputAreaFocusBlock(
        out Block focusBlock,
        out InputOutputModule ownerModule)
    {
        focusBlock = null;
        ownerModule = null;
        if (player == null || ResolveTerrainGenerator() == null)
        {
            return false;
        }

        Vector3 rootPosition = cachedRigidbody != null ? cachedRigidbody.position : transform.position;
        Vector2 sampleCenter = GetPlayerCollisionCenterXZ(rootPosition);
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.RoundToInt(sampleCenter.x),
            Mathf.RoundToInt(sampleCenter.y));
        if (!IsInputOutputRuntimeFocusAreaCoordinate(centerCoordinate)
            || !cachedTerrainGenerator.TryGetLoadedBlock(centerCoordinate, out focusBlock)
            || focusBlock == null)
        {
            return false;
        }

        TryResolveStandingAreaOwnerModule(focusBlock.Coordinate, out ownerModule);
        return true;
    }

    public bool TryGetStandingInputOutputAreaBlock(out Block areaBlock)
    {
        return TryGetStandingInputOutputAreaFocusBlock(out areaBlock, out _);
    }

    private void BuildStandingAreaAndObjectFocusBlocks(
        Block areaBlock,
        InputOutputModule ownerModule,
        List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        if (areaBlock == null)
        {
            return;
        }

        if (ownerModule != null)
        {
            AppendInputOutputModuleObjectFocusBlocks(ownerModule, results);
            SetInteractionFocusTargetOverride(areaBlock, ownerModule);
        }

        standaloneInteractionAreaFocusBlock = ownerModule == null
                                              || !ModuleOwnsObjectFocusCoordinate(
                                                  ownerModule,
                                                  areaBlock.Coordinate)
            ? areaBlock
            : null;
        AppendUniqueBlock(results, areaBlock);
    }

    private static bool ModuleOwnsObjectFocusCoordinate(
        InputOutputModule module,
        Vector2Int coordinate)
    {
        if (module == null)
        {
            return false;
        }

        IReadOnlyList<Vector2Int> focusCoordinates = module.RuntimeFocusCoordinates;
        if (focusCoordinates != null)
        {
            for (int i = 0; i < focusCoordinates.Count; i++)
            {
                if (focusCoordinates[i] == coordinate
                    && !ModuleOwnsAnyAreaCoordinate(module, coordinate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveStandingAreaOwnerModule(
        Vector2Int coordinate,
        out InputOutputModule ownerModule)
    {
        ownerModule = null;
        standingAreaModuleCandidates.Clear();
        InputOutputModule.CollectModulesAtRuntimeAreaCoordinate(
            coordinate,
            standingAreaModuleCandidates);
        InputOutputModule.CollectModulesAtRuntimeGridCoordinate(
            coordinate,
            standingAreaModuleCandidates);

        int bestScore = int.MinValue;
        long bestPlacementSequence = long.MinValue;
        for (int i = 0; i < standingAreaModuleCandidates.Count; i++)
        {
            InputOutputModule candidate = standingAreaModuleCandidates[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            int score = GetStandingAreaOwnershipScore(candidate, coordinate);
            long placementSequence = candidate.RuntimePlacementSequence;
            if (score < bestScore
                || (score == bestScore && placementSequence <= bestPlacementSequence))
            {
                continue;
            }

            bestScore = score;
            bestPlacementSequence = placementSequence;
            ownerModule = candidate;
        }

        standingAreaModuleCandidates.Clear();
        return ownerModule != null;
    }

    private static int GetStandingAreaOwnershipScore(
        InputOutputModule module,
        Vector2Int coordinate)
    {
        if (module == null)
        {
            return int.MinValue;
        }

        int areaKindScore = 0;
        if ((InputOutputModuleOutputAreaController.CoordinateIsOutputArea(coordinate)
             || InputOutputModule.CoordinateIsRuntimeOutputBlock(coordinate))
            && ModuleOwnsOutputAreaCoordinate(module, coordinate))
        {
            areaKindScore = 400;
        }
        else if ((InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
                  || InputOutputModule.CoordinateIsRuntimeInputItemBlock(coordinate))
                 && ModuleOwnsInputItemAreaCoordinate(module, coordinate))
        {
            areaKindScore = 300;
        }
        else if ((InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate)
                  || InputOutputModule.CoordinateIsRuntimeInputEnergyBlock(coordinate))
                 && ModuleOwnsInputEnergyAreaCoordinate(module, coordinate))
        {
            areaKindScore = 200;
        }
        else if (InputOutputModule.CoordinateIsRuntimePipeInputBlock(coordinate)
                 && module.ContainsRuntimeRectGridBlockType(
                     coordinate,
                     InputOutputModule.RectGridBlockType.PipeInput))
        {
            areaKindScore = 100;
        }

        int objectCoordinateCount = GetModuleObjectFocusCoordinateCount(module);
        return areaKindScore + Mathf.Min(objectCoordinateCount, 99);
    }

    private void AppendInputOutputModuleObjectFocusBlocks(
        InputOutputModule module,
        List<Block> results)
    {
        if (module == null || results == null)
        {
            return;
        }

        bool appended = false;
        IReadOnlyList<Vector2Int> focusCoordinates = module.RuntimeFocusCoordinates;
        if (focusCoordinates != null)
        {
            for (int i = 0; i < focusCoordinates.Count; i++)
            {
                Vector2Int coordinate = focusCoordinates[i];
                if (ModuleOwnsAnyAreaCoordinate(module, coordinate))
                {
                    continue;
                }

                appended |= TryAppendFocusBlock(results, coordinate, module);
            }
        }

        if (!appended)
        {
            IReadOnlyList<Vector2Int> occupiedCoordinates = module.RuntimeOccupiedCoordinates;
            if (occupiedCoordinates != null)
            {
                for (int i = 0; i < occupiedCoordinates.Count; i++)
                {
                    Vector2Int coordinate = occupiedCoordinates[i];
                    if (ModuleOwnsAnyAreaCoordinate(module, coordinate))
                    {
                        continue;
                    }

                    appended |= TryAppendFocusBlock(results, coordinate, module);
                }
            }
        }

        if (!appended)
        {
            TryAppendFocusBlock(results, module.RuntimeAnchorCoordinate, module);
        }
    }

    private static int GetModuleObjectFocusCoordinateCount(InputOutputModule module)
    {
        if (module == null)
        {
            return 0;
        }

        int count = 0;
        IReadOnlyList<Vector2Int> focusCoordinates = module.RuntimeFocusCoordinates;
        if (focusCoordinates != null)
        {
            for (int i = 0; i < focusCoordinates.Count; i++)
            {
                if (!ModuleOwnsAnyAreaCoordinate(module, focusCoordinates[i]))
                {
                    count++;
                }
            }

            if (count > 0)
            {
                return count;
            }
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = module.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null)
        {
            return count;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (!ModuleOwnsAnyAreaCoordinate(module, occupiedCoordinates[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static bool ModuleOwnsAnyAreaCoordinate(
        InputOutputModule module,
        Vector2Int coordinate)
    {
        return ModuleOwnsInputEnergyAreaCoordinate(module, coordinate)
               || ModuleOwnsInputItemAreaCoordinate(module, coordinate)
               || ModuleOwnsOutputAreaCoordinate(module, coordinate)
               || (module != null
                   && module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.PipeInput));
    }

    private static bool ModuleOwnsInputEnergyAreaCoordinate(
        InputOutputModule module,
        Vector2Int coordinate)
    {
        return module != null
               && (module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.InputEnergy)
                   || module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.PipeInputEnergy)
                   || module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.DoubleEnergy));
    }

    private static bool ModuleOwnsInputItemAreaCoordinate(
        InputOutputModule module,
        Vector2Int coordinate)
    {
        return module != null
               && (module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.InputItem)
                   || module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.PipeInputItem)
                   || module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.DoubleInputItem));
    }

    private static bool ModuleOwnsOutputAreaCoordinate(
        InputOutputModule module,
        Vector2Int coordinate)
    {
        return module != null
               && (module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.Output)
                   || module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.PipeOutputItem)
                   || module.ContainsRuntimeRectGridBlockType(
                       coordinate,
                       InputOutputModule.RectGridBlockType.DoublePipeOutputItem));
    }

    private static bool IsInputOutputRuntimeFocusAreaCoordinate(Vector2Int coordinate)
    {
        return InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate)
               || InputOutputModuleOutputAreaController.CoordinateIsOutputArea(coordinate)
               || InputOutputModule.TryGetModuleAtRuntimeAreaCoordinate(coordinate, out _)
               || InputOutputModule.CoordinateIsRuntimeInputOutputAreaBlock(coordinate);
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

    private void FindNearbyWorkableBlocks(List<Block> results)
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

                if (!workableObject.ContainsWorldPositionInWorkableRange(origin))
                {
                    continue;
                }

                nearbyWorkableRangeObjects.Add(workableObject);
                CacheInteractionButtonFocusTarget(workableObject, block);

                AppendMapObjectFocusBlocks(workableObject, block, results);
            }
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

        if (ResolveTerrainGenerator() == null)
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

                CacheInteractionButtonFocusTarget(boxObject, block);
                AppendMapObjectFocusBlocks(boxObject, block, results);
            }
        }
    }

    private void FindNearbyInstallationBlocks(
        List<Block> results,
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
                    results,
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
                        results,
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
        List<Block> results,
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

        float distanceSqr = GetMapObjectFocusSelectionDistanceSqr(
            installationObject,
            block,
            origin);
        if (distanceSqr > focusRadius * focusRadius)
        {
            return;
        }

        CacheInteractionButtonFocusTarget(installationObject, block);
        AppendMapObjectFocusBlocks(installationObject, block, results);
    }

    private float GetWorkableFocusDistanceSqr(WorkableObject workableObject, Block block, Vector3 origin)
    {
        Vector3 focusPoint = GetWorkableFocusPoint(workableObject, block, origin);
        Vector3 offset = focusPoint - origin;
        offset.y = 0f;
        return offset.sqrMagnitude;
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
            focusPoint = block.WorldPosition;
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
        if (mapObject is ConveyorBelt)
        {
            return CreateMapObjectStatusFocusBounds(mapObject, block, focusPadding);
        }

        mapObjectFocusRenderers.Clear();
        if (mapObject != null)
        {
            mapObject.GetComponentsInChildren(true, mapObjectFocusRenderers);
            Bounds combinedBounds = default;
            bool hasBounds = false;
            for (int i = 0; i < mapObjectFocusRenderers.Count; i++)
            {
                Renderer rendererComponent = mapObjectFocusRenderers[i];
                if (rendererComponent == null
                    || !rendererComponent.enabled
                    || rendererComponent is ParticleSystemRenderer
                    || rendererComponent is TrailRenderer
                    || rendererComponent is LineRenderer
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

                mapObjectFocusRenderers.Clear();
                return combinedBounds;
            }
        }

        mapObjectFocusRenderers.Clear();
        return CreateMapObjectStatusFocusBounds(mapObject, block, focusPadding);
    }

    private static Bounds CreateMapObjectStatusFocusBounds(MapObject mapObject, Block block, float focusPadding)
    {
        Vector3 center = block != null
            ? block.WorldPosition
            : mapObject != null
                ? mapObject.transform.position
                : Vector3.zero;
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
        GameManager gameManager = GameManager.Instance;
        bool isInteractionLocked = GameManager.TextInputFocused
                                   || (gameManager != null && gameManager.PlayerInteractionLocked);
        if (mouseFocusRefreshFrame == Time.frameCount
            && mouseFocusRefreshInteractionLocked == isInteractionLocked)
        {
            return;
        }

        mouseFocusRefreshFrame = Time.frameCount;
        mouseFocusRefreshInteractionLocked = isInteractionLocked;

        if (isInteractionLocked)
        {
            SetMouseFocusedAnimal(null);
            SetMouseFocusedPortableObject(null);
            SetMouseFocusedBlocks(null);
            return;
        }

        if (MountedVehicle != null)
        {
            SetMouseFocusedPortableObject(null);
            RefreshMouseAnimalFocus();
            RefreshMountedPinnedMouseFocus();
            return;
        }

        Vector2 pointerPosition = Input.mousePosition;
        if (IsPointerOverMouseFocusBlockingUi(pointerPosition))
        {
            SetMouseFocusedAnimal(null);
            SetMouseFocusedPortableObject(null);
            SetMouseFocusedBlocks(null);
            return;
        }

        if (!TryResolveMouseFocusTargets(
                pointerPosition,
                out Animal animal,
                out MapObject mapObject,
                out PortableObject portableObject,
                out Block fallbackBlock))
        {
            if (player != null && player.IsHoldingPitchfork)
            {
                RefreshPitchforkGroundMouseFocus(pointerPosition);
                return;
            }

            if (TryResolveHeldSeedDefinition(out _))
            {
                RefreshSeedGroundMouseFocus(pointerPosition);
                return;
            }

            SetMouseFocusedAnimal(null);
            SetMouseFocusedPortableObject(null);
            SetMouseFocusedBlocks(null);
            return;
        }

        SetMouseFocusedAnimal(animal);
        if (animal != null)
        {
            SetMouseFocusedPortableObject(null);
            SetMouseFocusedBlocks(null);
            return;
        }

        SetMouseFocusedPortableObject(portableObject);
        if (portableObject != null)
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

    private void RefreshPitchforkGroundMouseFocus(Vector2 pointerPosition)
    {
        SetMouseFocusedAnimal(null);
        SetMouseFocusedPortableObject(null);

        Camera targetCamera = ResolveMouseFocusCamera();
        if (targetCamera == null
            || !TryGetPointerBlockFromGroundPlane(
                targetCamera.ScreenPointToRay(pointerPosition),
                out Block block)
            || !CanFocusPitchforkGroundBlock(block))
        {
            SetMouseFocusedBlocks(null);
            return;
        }

        mouseFocusBlocks.Clear();
        mouseFocusBlocks.Add(block);
        SetMouseFocusedBlocks(mouseFocusBlocks);
    }

    private bool CanFocusPitchforkGroundBlock(Block block)
    {
        TerrainGenerator terrain = ResolveTerrainGenerator();
        return block != null
               && terrain != null
               && terrain.IsFarmableGroundBiomeAt(block.Coordinate)
               && IsClearGroundActionBlock(block);
    }

    private bool TryGetPitchforkDiggingApproachDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (pitchforkDigTargetBlock == null
            || player == null
            || !player.IsHoldingPitchfork
            || !CanFocusPitchforkGroundBlock(pitchforkDigTargetBlock))
        {
            CancelPitchforkDigging();
            return false;
        }

        Vector3 targetPosition = pitchforkDigTargetBlock.WorldPosition;
        Vector3 offset = targetPosition - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        if (distance > PitchforkDiggingRange)
        {
            float moveSpeed = Mathf.Max(0.01f, GetCurrentOnFootMoveSpeed());
            float maximumStep = moveSpeed * Mathf.Max(Time.deltaTime, Time.fixedDeltaTime);
            float inputScale = Mathf.Clamp01((distance - PitchforkDiggingRange) / maximumStep);
            direction = (offset / distance) * inputScale;
            return true;
        }

        if (offset.sqrMagnitude > 0.0001f)
        {
            pendingFacingDirection = offset;
            hasPendingFacingDirection = true;
        }

        if (!pitchforkDiggingQueued)
        {
            pitchforkDiggingQueued = true;
            player.QueueDiggingAnimation();
        }

        return false;
    }

    private void ResolveCompletedPitchforkDigging()
    {
        if (pitchforkDigTargetBlock == null
            || player == null
            || !player.DiggingAnimationFinishedThisFrame)
        {
            return;
        }

        Block completedBlock = pitchforkDigTargetBlock;
        TerrainGenerator terrain = ResolveTerrainGenerator();
        bool completed = terrain != null
                         && player.IsHoldingPitchfork
                         && CanFocusPitchforkGroundBlock(completedBlock)
                         && terrain.TryToggleFarmland(completedBlock);
        CancelPitchforkDigging(false);
        if (completed)
        {
            SetSelectedPitchforkGroundBlock(null);
        }
    }

    private void CancelPitchforkDigging(bool interruptAnimation = true)
    {
        pitchforkDigTargetBlock = null;
        pitchforkDiggingQueued = false;
        if (interruptAnimation)
        {
            player?.CancelDiggingAnimation(false);
        }
    }

    private void RefreshMouseAnimalFocus()
    {
        Vector2 pointerPosition = Input.mousePosition;
        if (IsPointerOverMouseFocusBlockingUi(pointerPosition)
            || !TryResolveMouseFocusTargets(
                pointerPosition,
                out Animal animal,
                out _,
                out _,
                out _))
        {
            SetMouseFocusedAnimal(null);
            return;
        }

        SetMouseFocusedAnimal(animal);
    }

    private void RefreshMountedPinnedMouseFocus()
    {
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1))
        {
            return;
        }

        Vector2 pointerPosition = Input.mousePosition;
        if (IsPointerOverMouseFocusBlockingUi(pointerPosition)
            || !TryResolveMouseFocusedMapObject(
                pointerPosition,
                out MapObject mapObject,
                out Block fallbackBlock))
        {
            return;
        }

        mountedPinnedFocusTarget = mapObject;
        mountedPinnedFocusFallbackBlock = fallbackBlock;

        mouseFocusBlocks.Clear();
        if (AppendMapObjectFocusBlocks(mapObject, fallbackBlock, mouseFocusBlocks))
        {
            SetMouseFocusedBlocks(mouseFocusBlocks, mapObject);
        }

        RefreshMountedPinnedInteractionFocus();
    }

    private bool TryResolveMouseFocusedMapObject(Vector2 pointerPosition, out MapObject mapObject, out Block fallbackBlock)
    {
        if (!TryResolveMouseFocusTargets(
                pointerPosition,
                out Animal animal,
                out mapObject,
                out PortableObject portableObject,
                out fallbackBlock)
            || animal != null
            || portableObject != null)
        {
            mapObject = null;
            fallbackBlock = null;
            return false;
        }

        return mapObject != null;
    }

    private bool TryResolveMouseFocusTargets(
        Vector2 pointerPosition,
        out Animal animal,
        out MapObject mapObject,
        out PortableObject portableObject,
        out Block fallbackBlock)
    {
        animal = null;
        mapObject = null;
        portableObject = null;
        fallbackBlock = null;

        Camera targetCamera = ResolveMouseFocusCamera();
        if (targetCamera == null)
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        float maxDistance = targetCamera.farClipPlane > 0f ? targetCamera.farClipPlane : 512f;
        int hitCount = RaycastMouseFocus(ray, Mathf.Max(0f, maxDistance));
        Animal closestAnimal = null;
        float closestAnimalDistance = float.MaxValue;
        MapObject closestCandidate = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = mouseFocusRaycastHits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hit.distance >= closestDistance)
            {
                continue;
            }

            Animal animalCandidate = hitCollider.GetComponentInParent<Animal>();
            if (animalCandidate != null
                && animalCandidate.gameObject.activeInHierarchy
                && hit.distance < closestAnimalDistance)
            {
                closestAnimal = animalCandidate;
                closestAnimalDistance = hit.distance;
            }

            MapObject candidate = hitCollider.GetComponentInParent<MapObject>();
            if (!IsValidMouseFocusMapObject(candidate))
            {
                continue;
            }

            closestCandidate = candidate;
            closestDistance = hit.distance;
        }

        bool hasPortableObject = TryResolvePortableItemStackFocus(
            ray,
            out PortableObject closestPortableObject,
            out float closestPortableObjectDistance);
        if (closestAnimal != null
            && closestAnimalDistance <= closestDistance
            && (!hasPortableObject || closestAnimalDistance <= closestPortableObjectDistance))
        {
            animal = closestAnimal;
            return true;
        }

        if (hasPortableObject && closestPortableObjectDistance <= closestDistance)
        {
            portableObject = closestPortableObject;
            return true;
        }

        if (closestCandidate != null)
        {
            mapObject = closestCandidate;
            TryResolveMouseFocusFallbackBlock(closestCandidate, ray, out fallbackBlock);
            return true;
        }

        if (!TryGetPointerCoordinateFromGroundPlane(ray, out Vector2Int pointerCoordinate))
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrainGenerator();
        terrain?.TryGetLoadedBlockRuntimeProxy(pointerCoordinate, out fallbackBlock);
        using (FindMouseFocusInstallationMarker.Auto())
        {
            mapObject = TryFindInstallationCoveringCoordinate(
                    pointerCoordinate,
                    out InstallationObject coveringInstallation,
                    out Block coveringFallbackBlock)
                ? coveringInstallation
                : fallbackBlock != null && fallbackBlock.MapObject != null
                    ? fallbackBlock.MapObject
                    : fallbackBlock != null
                        ? fallbackBlock.Resource
                        : null;

            if (coveringFallbackBlock != null)
            {
                fallbackBlock = coveringFallbackBlock;
            }
        }

        if (!IsValidMouseFocusMapObject(mapObject))
        {
            mapObject = null;
            fallbackBlock = null;
            return false;
        }

        return true;
    }

    private bool TryResolvePortableItemStackFocus(
        Ray ray,
        out PortableObject portableObject,
        out float hitDistance)
    {
        portableObject = null;
        hitDistance = float.MaxValue;
        if (!TryGetPointerBlockFromGroundPlane(ray, out Block centerBlock))
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrainGenerator();
        if (terrain == null)
        {
            return false;
        }

        Vector2Int centerCoordinate = centerBlock.Coordinate;
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
                    || block == null
                    || !block.TryGetPortableItemStackUnderRay(
                        ray,
                        out PortableObject candidate,
                        out float candidateDistance)
                    || candidateDistance >= hitDistance)
                {
                    continue;
                }

                portableObject = candidate;
                hitDistance = candidateDistance;
            }
        }

        return portableObject != null;
    }

    private void SetMouseFocusedAnimal(Animal nextAnimal)
    {
        if (currentMouseFocusedAnimal == nextAnimal)
        {
            return;
        }

        if (currentMouseFocusedAnimal != null)
        {
            currentMouseFocusedAnimal.SetHoverOutline(false);
        }

        currentMouseFocusedAnimal = nextAnimal;
        if (currentMouseFocusedAnimal != null)
        {
            currentMouseFocusedAnimal.SetHoverOutline(true);
        }
    }

    private void SetMouseFocusedPortableObject(PortableObject nextPortableObject)
    {
        if (currentMouseFocusedPortableObject == nextPortableObject)
        {
            return;
        }

        if (currentMouseFocusedPortableObject != null)
        {
            currentMouseFocusedPortableObject.SetHoverOutline(false);
        }

        currentMouseFocusedPortableObject = nextPortableObject;
        if (currentMouseFocusedPortableObject != null)
        {
            currentMouseFocusedPortableObject.SetHoverOutline(true);
        }
    }

    private Camera ResolveMouseFocusCamera()
    {
        if (cachedMouseFocusCamera != null
            && cachedMouseFocusCamera.isActiveAndEnabled
            && cachedMouseFocusCamera.CompareTag("MainCamera"))
        {
            return cachedMouseFocusCamera;
        }

        cachedMouseFocusCamera = Camera.main;
        return cachedMouseFocusCamera;
    }

    private int RaycastMouseFocus(Ray ray, float maxDistance)
    {
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            mouseFocusRaycastHits,
            maxDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        while (hitCount >= mouseFocusRaycastHits.Length
               && mouseFocusRaycastHits.Length < MaxMouseFocusRaycastHitBufferSize)
        {
            int nextSize = Mathf.Min(mouseFocusRaycastHits.Length * 2, MaxMouseFocusRaycastHitBufferSize);
            if (nextSize <= mouseFocusRaycastHits.Length)
            {
                break;
            }

            System.Array.Resize(ref mouseFocusRaycastHits, nextSize);
            hitCount = Physics.RaycastNonAlloc(
                ray,
                mouseFocusRaycastHits,
                maxDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }

        return hitCount;
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

        mouseFocusCheckedInstallations.Clear();
        mouseFocusRuntimeInstallationScratch.Clear();
        if (TryFindMouseFocusInstallationAtSearchCoordinate(
                coordinate,
                coordinate,
                terrain,
                out installationObject,
                out fallbackBlock))
        {
            ClearMouseFocusInstallationSearchBuffers();
            return true;
        }

        int searchRadius = Mathf.Max(4, Mathf.CeilToInt(InstallationObject.GlobalMaxFocusActivationRadius) + 4);
        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }

                Vector2Int candidateCoordinate = coordinate + new Vector2Int(offsetX, offsetY);
                if (TryFindMouseFocusInstallationAtSearchCoordinate(
                        candidateCoordinate,
                        coordinate,
                        terrain,
                        out installationObject,
                        out fallbackBlock))
                {
                    ClearMouseFocusInstallationSearchBuffers();
                    return true;
                }
            }
        }

        ClearMouseFocusInstallationSearchBuffers();
        return false;
    }

    private bool TryFindMouseFocusInstallationAtSearchCoordinate(
        Vector2Int searchCoordinate,
        Vector2Int targetCoordinate,
        TerrainGenerator terrain,
        out InstallationObject installationObject,
        out Block fallbackBlock)
    {
        terrain.TryGetLoadedBlockRuntimeProxy(searchCoordinate, out Block candidateBlock);
        if (TrySelectMouseFocusInstallation(
                candidateBlock != null ? candidateBlock.MapObject as InstallationObject : null,
                candidateBlock,
                targetCoordinate,
                terrain,
                out installationObject,
                out fallbackBlock))
        {
            return true;
        }

        mouseFocusRuntimeInstallationScratch.Clear();
        InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
            searchCoordinate,
            mouseFocusRuntimeInstallationScratch);
        for (int i = 0; i < mouseFocusRuntimeInstallationScratch.Count; i++)
        {
            if (TrySelectMouseFocusInstallation(
                    mouseFocusRuntimeInstallationScratch[i],
                    candidateBlock,
                    targetCoordinate,
                    terrain,
                    out installationObject,
                    out fallbackBlock))
            {
                return true;
            }
        }

        installationObject = null;
        fallbackBlock = null;
        return false;
    }

    private bool TrySelectMouseFocusInstallation(
        InstallationObject candidate,
        Block candidateBlock,
        Vector2Int targetCoordinate,
        TerrainGenerator terrain,
        out InstallationObject installationObject,
        out Block fallbackBlock)
    {
        installationObject = null;
        fallbackBlock = null;
        if (candidate == null
            || !candidate.gameObject.activeInHierarchy
            || !candidate.AllowsFocus
            || !mouseFocusCheckedInstallations.Add(candidate)
            || !InstallationCoversCoordinate(candidate, targetCoordinate))
        {
            return false;
        }

        installationObject = candidate;
        fallbackBlock = candidateBlock != null && candidateBlock.MapObject == candidate
            ? candidateBlock
            : null;
        if (fallbackBlock != null || terrain == null)
        {
            return true;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = candidate.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null)
        {
            return true;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (terrain.TryGetLoadedBlockRuntimeProxy(occupiedCoordinates[i], out fallbackBlock)
                && fallbackBlock != null)
            {
                break;
            }
        }

        return true;
    }

    private void ClearMouseFocusInstallationSearchBuffers()
    {
        mouseFocusCheckedInstallations.Clear();
        mouseFocusRuntimeInstallationScratch.Clear();
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
        if (terrain == null
            || !TryGetPointerCoordinateFromGroundPlane(ray, out Vector2Int coordinate))
        {
            return false;
        }

        return terrain.TryGetLoadedBlock(coordinate, out block) && block != null;
    }

    private bool TryGetPointerCoordinateFromGroundPlane(Ray ray, out Vector2Int coordinate)
    {
        coordinate = default;
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
        coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPoint.x),
            Mathf.RoundToInt(worldPoint.z));
        return true;
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

        Vector3 offset = block.WorldPosition - origin;
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

    private void SetFocusedBlocks(
        List<Block> nextBlocks,
        bool preserveFarmlandFocusGroup = false)
    {
        if (!preserveFarmlandFocusGroup)
        {
            currentInteractionFarmlandFocusGroup.Clear();
        }

        UpdateFocusedSprinklerRangeVisuals(nextBlocks);
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

    private void SetSelectedFocusedBlocks(
        List<Block> nextBlocks,
        bool preserveFarmlandFocusGroup = false)
    {
        if (!preserveFarmlandFocusGroup)
        {
            currentSelectionFarmlandFocusGroup.Clear();
        }

        selectedFocusRemovalBuffer.Clear();
        foreach (Block currentBlock in currentSelectedFocusedBlocks)
        {
            if (!ContainsFocusedBlock(nextBlocks, currentBlock))
            {
                selectedFocusRemovalBuffer.Add(currentBlock);
            }
        }

        for (int i = 0; i < selectedFocusRemovalBuffer.Count; i++)
        {
            Block block = selectedFocusRemovalBuffer[i];
            currentSelectedFocusedBlocks.Remove(block);
            if (block != null)
            {
                block.SetSelectionFocusVisible(false);
            }
        }

        if (nextBlocks == null)
        {
            return;
        }

        for (int i = 0; i < nextBlocks.Count; i++)
        {
            Block block = nextBlocks[i];
            if (block != null)
            {
                currentSelectedFocusedBlocks.Add(block);
            }
        }

        RefreshSelectedFocusMarkers();
    }

    private void RefreshInteractionFocusMarkers()
    {
        RefreshGroupedFocusMarkers(currentFocusedBlocks, FocusMarkerKind.Interaction);
    }

    private void RefreshMouseFocusMarkers()
    {
        RefreshGroupedFocusMarkers(currentMouseFocusedBlocks, FocusMarkerKind.Mouse);
    }

    private void RefreshSelectedFocusMarkers()
    {
        RefreshGroupedFocusMarkers(currentSelectedFocusedBlocks, FocusMarkerKind.Selection);
    }

    private void RefreshGroupedFocusMarkers(HashSet<Block> focusedBlocks, FocusMarkerKind focusKind)
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

            SetBlockFocusVisible(block, focusKind, false);
            if (focusKind == FocusMarkerKind.Interaction
                && block == standaloneInteractionAreaFocusBlock)
            {
                SetBlockFocusVisible(block, focusKind, true);
                continue;
            }

            HashSet<Block> farmlandFocusGroup = focusKind == FocusMarkerKind.Interaction
                ? currentInteractionFarmlandFocusGroup
                : focusKind == FocusMarkerKind.Selection
                    ? currentSelectionFarmlandFocusGroup
                    : null;
            if (farmlandFocusGroup != null && farmlandFocusGroup.Contains(block))
            {
                FocusMarkerGroup farmlandGroup = GetFarmlandFocusMarkerGroup();
                if (farmlandGroup == null)
                {
                    farmlandGroup = GetNextFocusMarkerGroup();
                    farmlandGroup.ResetFarmland(block);
                }
                else
                {
                    farmlandGroup.Add(block);
                }

                continue;
            }

            MapObject focusedMapObject = ResolveInteractionFocusTarget(block);
            if (focusedMapObject == null)
            {
                SetBlockFocusVisible(block, focusKind, true);
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
                SetBlockFocusVisible(group.markerBlock, focusKind, true);
            }
            else if (group.isFarmlandGroup)
            {
                SetBlockFocusShapeVisible(
                    group.markerBlock,
                    focusKind,
                    true,
                    group.Coordinates);
            }
            else
            {
                SetBlockFocusVisible(group.markerBlock, focusKind, true, group.Center, group.Size);
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

    private FocusMarkerGroup GetFarmlandFocusMarkerGroup()
    {
        for (int i = 0; i < focusMarkerGroupCount; i++)
        {
            FocusMarkerGroup group = focusMarkerGroups[i];
            if (group != null && group.isFarmlandGroup)
            {
                return group;
            }
        }

        return null;
    }

    private static void SetBlockFocusVisible(Block block, FocusMarkerKind focusKind, bool isVisible)
    {
        if (block == null)
        {
            return;
        }

        switch (focusKind)
        {
            case FocusMarkerKind.Mouse:
                block.SetMouseFocusVisible(isVisible);
                break;
            case FocusMarkerKind.Selection:
                block.SetSelectionFocusVisible(isVisible);
                break;
            default:
                block.SetFocusVisible(isVisible);
                break;
        }
    }

    private static void SetBlockFocusVisible(
        Block block,
        FocusMarkerKind focusKind,
        bool isVisible,
        Vector3 center,
        Vector2 size)
    {
        if (block == null)
        {
            return;
        }

        switch (focusKind)
        {
            case FocusMarkerKind.Mouse:
                block.SetMouseFocusVisible(isVisible, center, size);
                break;
            case FocusMarkerKind.Selection:
                block.SetSelectionFocusVisible(isVisible, center, size);
                break;
            default:
                block.SetFocusVisible(isVisible, center, size);
                break;
        }
    }

    private static void SetBlockFocusShapeVisible(
        Block block,
        FocusMarkerKind focusKind,
        bool isVisible,
        IReadOnlyList<Vector2Int> coordinates)
    {
        if (block == null)
        {
            return;
        }

        switch (focusKind)
        {
            case FocusMarkerKind.Mouse:
                block.SetMouseFocusShapeVisible(isVisible, coordinates);
                break;
            case FocusMarkerKind.Selection:
                block.SetSelectionFocusShapeVisible(isVisible, coordinates);
                break;
            default:
                block.SetFocusShapeVisible(isVisible, coordinates);
                break;
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

    private void UpdateFocusedSprinklerRangeVisuals(IReadOnlyList<Block> nextBlocks)
    {
        nextFocusedSprinklerRangeObjects.Clear();
        if (nextBlocks != null)
        {
            for (int i = 0; i < nextBlocks.Count; i++)
            {
                Sprinkler sprinkler = ResolveFocusedSprinkler(nextBlocks[i]);
                if (sprinkler != null)
                {
                    nextFocusedSprinklerRangeObjects.Add(sprinkler);
                }
            }
        }

        focusedSprinklerRangeRemovalBuffer.Clear();
        foreach (Sprinkler sprinkler in currentFocusedSprinklerRangeObjects)
        {
            if (sprinkler != null && nextFocusedSprinklerRangeObjects.Contains(sprinkler))
            {
                continue;
            }

            focusedSprinklerRangeRemovalBuffer.Add(sprinkler);
        }

        for (int i = 0; i < focusedSprinklerRangeRemovalBuffer.Count; i++)
        {
            Sprinkler sprinkler = focusedSprinklerRangeRemovalBuffer[i];
            currentFocusedSprinklerRangeObjects.Remove(sprinkler);
            if (sprinkler != null)
            {
                sprinkler.SetFocusedRangeVisualRequested(false);
            }
        }

        foreach (Sprinkler sprinkler in nextFocusedSprinklerRangeObjects)
        {
            if (sprinkler == null || !currentFocusedSprinklerRangeObjects.Add(sprinkler))
            {
                continue;
            }

            sprinkler.SetFocusedRangeVisualRequested(true);
        }
    }

    private void UpdateInRangeSprinklerRangeVisuals(HashSet<Sprinkler> nextObjects)
    {
        inRangeSprinklerRangeRemovalBuffer.Clear();
        foreach (Sprinkler sprinkler in currentInRangeSprinklerRangeObjects)
        {
            if (sprinkler != null && nextObjects != null && nextObjects.Contains(sprinkler))
            {
                continue;
            }

            inRangeSprinklerRangeRemovalBuffer.Add(sprinkler);
        }

        for (int i = 0; i < inRangeSprinklerRangeRemovalBuffer.Count; i++)
        {
            Sprinkler sprinkler = inRangeSprinklerRangeRemovalBuffer[i];
            currentInRangeSprinklerRangeObjects.Remove(sprinkler);
            if (sprinkler != null)
            {
                sprinkler.SetInRangeVisualRequested(false);
            }
        }

        if (nextObjects == null)
        {
            nextInRangeSprinklerRangeObjects.Clear();
            return;
        }

        foreach (Sprinkler sprinkler in nextObjects)
        {
            if (sprinkler == null)
            {
                continue;
            }

            currentInRangeSprinklerRangeObjects.Add(sprinkler);
            sprinkler.SetInRangeVisualRequested(true);
        }
    }

    private static Sprinkler ResolveFocusedSprinkler(Block block)
    {
        if (block == null)
        {
            return null;
        }

        if (block.MapObject is Sprinkler sprinkler)
        {
            return sprinkler;
        }

        if (InputOutputModule.TryGetModuleAtRuntimeGridCoordinate(block.Coordinate, out InputOutputModule gridModule)
            && gridModule is Sprinkler gridSprinkler)
        {
            return gridSprinkler;
        }

        return InputOutputModule.TryGetModuleAtRuntimeAreaCoordinate(
                   block.Coordinate,
                   out InputOutputModule areaModule)
               && areaModule is Sprinkler areaSprinkler
            ? areaSprinkler
            : null;
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

    private static bool IsMountedAnimalRunRequested(Vector2 joystickInput)
    {
        return joystickInput.sqrMagnitude
                   >= MountedAnimalRunJoystickThreshold * MountedAnimalRunJoystickThreshold
               || Input.GetKey(KeyCode.LeftShift)
               || Input.GetKey(KeyCode.RightShift);
    }

}
