using ProjectF.Attributes;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Block : BaseObject
{
    public enum BlockType { Ground }
    public const int ConveyorCellItemUnit = 4;
    public const int InputAreaCenterStackStateSentinel = -1000000001;
    public const int ConveyorStackStateSentinel = -1000000002;
    public const int FloorStackStateSentinel = -1000000003;
    private const float InputAreaCenterVerticalSpacing = 0.05f;
    private const int ConveyorStackLaneLimit = ConveyorCellItemUnit;
    private const int ConveyorSingleLineFrontLaneIndex = 0;
    private const int ConveyorSingleLineBackLaneIndex = 2;
    private const float ConveyorLaneHeight = 0.2f;
    private const float ConveyorLaneSettleEpsilon = 0.01f;
    private const float ConveyorCycleReadyDistance = 0.12f;
    private const float ConveyorItemSpacing = 0.5f;
    private const float ConveyorLaneHalfExtent = ConveyorItemSpacing * 0.5f;
    private const float ConveyorCornerCenterRadius = 0.35f;
    private const float ConveyorCornerArcEndInsetDegrees = 20f;
    private const float ConveyorSlotDotDiameter = 0.08f;
    private const float ConveyorSlotDotThickness = 0.02f;
    private const float ConveyorSlotDotVerticalOffset = 0.012f;
    private const float BeltDirectionArrowLength = 0.62f;
    private const float BeltDirectionArrowShaftWidth = 0.18f;
    private const float BeltDirectionArrowHeadLength = 0.24f;
    private const float BeltDirectionArrowHeadWidth = 0.34f;
    private const float BeltDirectionArrowVerticalOffset = 0.08f;
    private const float BeltDirectionArrowMinimumWorldHeight = 0.32f;
    private const int ConveyorSlotDotPathMaxSegments = 32;
    private const int ConveyorRunMoveMaxSegments = ConveyorSlotDotPathMaxSegments;
    private const int ConveyorPlacementForwardSearchDepth = 4;
    private const int ConveyorPlacementBackwardSearchDepth = 2;
    private const float ConveyorBlockedRetryInterval = 0.08f;
    private const float ConveyorBlockedRetryJitterStep = 0.01f;
    private const int ConveyorBlockedRetryJitterSteps = 5;
    private const int ConveyorContinuousMotionMaxCarrySteps = 4;

    public event Action<Block> RuntimeItemStackChanged;

    private void NotifyRuntimeItemStackChanged()
    {
        RobotArm.WakeAroundCoordinate(coordinate);
        InputOutputModule.WakeRuntimeModulesAtCoordinate(coordinate);
        RuntimeItemStackChanged?.Invoke(this);
    }
    private const float ConveyorContinuousMotionEpsilon = 0.0001f;
    private const string ConveyorSlotDotRootName = "ConveyorSlotDots";
    private static readonly Vector2Int[] ConveyorNeighborDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    [SerializeField, ReadOnly]
    private Vector2Int coordinate;

    [SerializeField]
    private BlockType type;

    [SerializeField]
    private Transform body;

    [SerializeField, ReadOnly]
    private MapObject mapObject;
    private ConveyorBelt runtimeConveyorOverride;

    [SerializeField]
    private List<Transform> floorObjects;

    [SerializeField]
    private PortableObject floorObjectPrefab;

    [SerializeField, Min(1)]
    private int maxFloorObjectsPerStack = 10;

    [SerializeField, Min(0.01f)]
    private float floorObjectVerticalSpacing = 0.05f;

    [SerializeField, Min(1)]
    private int inputAreaCenterMaxObjects = 10;

    [SerializeField]
    private MapFocus focus;

    private bool interactionFocusVisible;
    private bool mouseFocusVisible;
    private bool interactionFocusUsesArea;
    private bool mouseFocusUsesArea;
    private Vector3 interactionFocusAreaCenter;
    private Vector2 interactionFocusAreaSize = Vector2.one;
    private Vector3 mouseFocusAreaCenter;
    private Vector2 mouseFocusAreaSize = Vector2.one;

    private readonly List<List<PortableObject>> floorStacks = new List<List<PortableObject>>();
    private readonly List<PortableObject> inputAreaCenterStack = new List<PortableObject>();
    private readonly List<PortableObject> conveyorStack = new List<PortableObject>();
    private readonly List<int> conveyorItemIds = new List<int>();
    private readonly List<int> conveyorItemMoveFrames = new List<int>();
    private readonly List<ConveyorDataMotionState> conveyorItemMotionStates = new List<ConveyorDataMotionState>();
    private readonly List<ConveyorPickupGateState> conveyorItemPickupGateStates = new List<ConveyorPickupGateState>();
    private readonly List<float> conveyorItemMovementHoldUntilTimes = new List<float>();
    private readonly Dictionary<PortableObject, ConveyorCornerMotionState> conveyorCornerMotionStates = new Dictionary<PortableObject, ConveyorCornerMotionState>();
    private readonly Dictionary<PortableObject, ConveyorLinearMotionState> conveyorLinearMotionStates = new Dictionary<PortableObject, ConveyorLinearMotionState>();
    private readonly List<PortableObject> conveyorCornerMotionTickBuffer = new List<PortableObject>();
    private readonly List<PortableObject> conveyorLinearMotionTickBuffer = new List<PortableObject>();
    private readonly List<Transform> conveyorSlotDots = new List<Transform>();
    private readonly List<MeshRenderer> conveyorSlotDotRenderers = new List<MeshRenderer>();
    private readonly List<ConveyorSlotDotSegment> conveyorSlotDotSegments = new List<ConveyorSlotDotSegment>(ConveyorSlotDotPathMaxSegments);
    private readonly List<ConveyorSlotDotPathCache> conveyorSlotDotPathCaches = new List<ConveyorSlotDotPathCache>();
    private readonly List<ConveyorLaneKey> conveyorSlotDotOrderedLaneKeys = new List<ConveyorLaneKey>(ConveyorSlotDotPathMaxSegments + 1);
    private readonly HashSet<ConveyorLaneKey> conveyorSlotDotVisitedLaneKeys = new HashSet<ConveyorLaneKey>();
    private readonly HashSet<ConveyorLaneKey> conveyorMoveVisiting = new HashSet<ConveyorLaneKey>();
    private readonly List<ConveyorLaneMove> conveyorPlannedMoves = new List<ConveyorLaneMove>();
    private readonly HashSet<ConveyorLaneKey> conveyorCanMoveVisiting = new HashSet<ConveyorLaneKey>();
    private readonly bool[] conveyorCanMoveCacheValid = new bool[ConveyorStackLaneLimit * 2];
    private readonly int[] conveyorCanMoveCacheFrames = new int[ConveyorStackLaneLimit * 2];
    private readonly int[] conveyorCanMoveCacheVersions = new int[ConveyorStackLaneLimit * 2];
    private readonly bool[] conveyorCanMoveCacheResults = new bool[ConveyorStackLaneLimit * 2];
    private readonly bool[] conveyorPlanFailureCacheValid = new bool[ConveyorStackLaneLimit * 2];
    private readonly float[] conveyorPlanFailureCacheUntilTimes = new float[ConveyorStackLaneLimit * 2];
    private readonly int[] conveyorPlanFailureCacheVersions = new int[ConveyorStackLaneLimit * 2];
    private readonly List<Block> conveyorTouchedBlocks = new List<Block>(4);
    private readonly HashSet<Block> conveyorTouchedBlockSet = new HashSet<Block>();
    private readonly Dictionary<object, bool> inputAreaCenterVisibilityRequests = new Dictionary<object, bool>();
    private MaterialPropertyBlock conveyorSlotDotPropertyBlock;
    private static Material conveyorSlotDotMaterial;
    private static Mesh beltDirectionArrowMesh;
    private readonly List<Vector2Int> fluidDirectionConnectedDirections = new List<Vector2Int>(4);
    private readonly Queue<FluidSourceSearchNode> fluidDirectionSourceSearchQueue = new Queue<FluidSourceSearchNode>(32);
    private readonly HashSet<Vector2Int> fluidDirectionSourceSearchVisited = new HashSet<Vector2Int>();
    private PortableObjectPool floorObjectPool;
    private TerrainGenerator cachedTerrainGenerator;
    private Transform inputAreaCenterAnchor;
    private Transform conveyorSlotDotRoot;
    private MeshRenderer[] cachedBodyRenderers = Array.Empty<MeshRenderer>();
    private float cachedInputAreaCenterHeight;
    private float nextConveyorMoveAttemptTime;
    private readonly float[] nextConveyorLaneMoveAttemptTimes = new float[ConveyorStackLaneLimit];
    private Block cachedNextConveyorBlock;
    private bool conveyorConnectionCacheDirty = true;
    private static int conveyorCanMoveGlobalStateVersion = 1;
    private static int conveyorPlanFailureGlobalStateVersion = 1;
    private bool cachedHasNextConveyorBlock;
    private readonly Block[] cachedConveyorSuccessorBlocks = new Block[ConveyorStackLaneLimit];
    private readonly int[] cachedConveyorSuccessorLaneIndices = new int[ConveyorStackLaneLimit];
    private readonly bool[] cachedConveyorSuccessorExists = new bool[ConveyorStackLaneLimit];
    private readonly bool[] cachedConveyorSuccessorUsesCornerMotion = new bool[ConveyorStackLaneLimit];
    private bool conveyorSuccessorCacheDirty = true;
    private bool conveyorLaneLayoutCacheDirty = true;
    private bool cachedConveyorLaneLayoutValid;
    private int cachedFrontLaneIndex = -1;
    private int cachedBackLaneIndex = -1;
    private readonly bool[] conveyorLaneBlockedSleepStates = new bool[ConveyorStackLaneLimit];
    private readonly bool[] conveyorLaneCycleBlockedSleepStates = new bool[ConveyorStackLaneLimit];
    private readonly bool[] conveyorLaneSleepAwakeDarkTintStates = new bool[ConveyorStackLaneLimit];
    private readonly bool[] conveyorLaneBeltItemLineDebugStates = new bool[ConveyorStackLaneLimit];
    private readonly Color32[] conveyorLaneBeltItemLineDebugColors = new Color32[ConveyorStackLaneLimit];
    private int conveyorItemVisualVersion;
    private bool childReferencesCached;
    private bool inputAreaCenterObjectsVisible = true;

    private struct ConveyorCornerMotionState
    {
        public int sourceLaneIndex;
        public int destinationLaneIndex;
        public Vector3 startWorldPosition;
        public float progress;
        public float pathLength;
        public float durationPathLength;
    }

    private struct ConveyorCornerContinuation
    {
        public bool active;
        public Block block;
        public int sourceLaneIndex;
        public int destinationLaneIndex;
        public Vector3 startWorldPosition;
        public float startProgress;
        public float pathLength;
        public float durationPathLength;
    }

    private struct FluidSourceSearchNode
    {
        public Vector2Int coordinate;
        public Vector2Int firstDirection;

        public FluidSourceSearchNode(Vector2Int coordinate, Vector2Int firstDirection)
        {
            this.coordinate = coordinate;
            this.firstDirection = firstDirection;
        }
    }

    private struct ConveyorLinearMotionState
    {
        public ConveyorCornerContinuation cornerContinuation;
        public Vector3 startWorldPosition;
        public bool hasViaWorldPosition;
        public Vector3 viaWorldPosition;
        public int destinationLaneIndex;
        public float progress;
        public float pathLength;
    }

    private struct ConveyorDataMotionState
    {
        public bool active;
        public bool useCornerMotion;
        public ConveyorCornerContinuation cornerContinuation;
        public Vector3 startWorldPosition;
        public bool hasViaWorldPosition;
        public Vector3 viaWorldPosition;
        public int sourceLaneIndex;
        public int destinationLaneIndex;
        public float progress;
        public float pathLength;
        public float durationPathLength;
        public float startTime;
        public float duration;
    }

    private struct ConveyorPickupGateState
    {
        public bool hasGate;
        public bool requiresExit;
        public bool hasExited;
        public float exitRadius;
        public bool isSettled;
        public Vector3 dropOrigin;
        public bool hasOrigin;
        public bool autoPickupBlocked;

        public static ConveyorPickupGateState Settled()
        {
            return new ConveyorPickupGateState
            {
                isSettled = true
            };
        }

        public void MarkDropped(float radius, bool settled, Vector3 origin)
        {
            hasGate = true;
            requiresExit = true;
            hasExited = false;
            exitRadius = Mathf.Max(0f, radius);
            isSettled = settled;
            dropOrigin = origin;
            hasOrigin = true;
            autoPickupBlocked = false;
        }

        public void MarkSettled()
        {
            isSettled = true;
        }

        public void UpdateExitState(Vector3 playerPosition, Vector3 fallbackOrigin)
        {
            if (!requiresExit || hasExited)
            {
                return;
            }

            Vector3 origin = hasOrigin ? dropOrigin : fallbackOrigin;
            Vector3 offset = playerPosition - origin;
            offset.y = 0f;
            if (offset.sqrMagnitude > exitRadius * exitRadius)
            {
                hasExited = true;
            }
        }

        public bool CanPickup(float distanceSqr, float pickupRadiusSqr)
        {
            if (autoPickupBlocked)
            {
                return false;
            }

            if (!requiresExit)
            {
                return true;
            }

            return hasExited && distanceSqr <= pickupRadiusSqr;
        }

        public bool CanManualPickup(float distanceSqr, float pickupRadiusSqr)
        {
            return isSettled && distanceSqr <= pickupRadiusSqr;
        }
    }

    private readonly struct ConveyorLaneKey : IEquatable<ConveyorLaneKey>
    {
        public ConveyorLaneKey(Block block, int laneIndex)
        {
            this.block = block;
            this.laneIndex = laneIndex;
        }

        private readonly Block block;
        private readonly int laneIndex;

        public Block Block => block;
        public int LaneIndex => laneIndex;

        public bool Equals(ConveyorLaneKey other)
        {
            return block == other.block && laneIndex == other.laneIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is ConveyorLaneKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((block != null ? block.GetHashCode() : 0) * 397) ^ laneIndex;
            }
        }
    }

    private readonly struct ConveyorLaneMove
    {
        public ConveyorLaneMove(
            Block sourceBlock,
            int sourceLaneIndex,
            Block destinationBlock,
            int destinationLaneIndex,
            PortableObject portableObject,
            int itemId,
            ConveyorPickupGateState pickupGateState,
            bool useCornerMotion,
            Vector3 startWorldPosition,
            ConveyorCornerContinuation cornerContinuation)
        {
            this.sourceBlock = sourceBlock;
            this.sourceLaneIndex = sourceLaneIndex;
            this.destinationBlock = destinationBlock;
            this.destinationLaneIndex = destinationLaneIndex;
            this.portableObject = portableObject;
            this.itemId = itemId;
            this.pickupGateState = pickupGateState;
            this.useCornerMotion = useCornerMotion;
            this.startWorldPosition = startWorldPosition;
            this.cornerContinuation = cornerContinuation;
        }

        public readonly Block sourceBlock;
        public readonly int sourceLaneIndex;
        public readonly Block destinationBlock;
        public readonly int destinationLaneIndex;
        public readonly PortableObject portableObject;
        public readonly int itemId;
        public readonly ConveyorPickupGateState pickupGateState;
        public readonly bool useCornerMotion;
        public readonly Vector3 startWorldPosition;
        public readonly ConveyorCornerContinuation cornerContinuation;
    }

    private readonly struct ConveyorSlotDotSegment
    {
        public ConveyorSlotDotSegment(
            Block sourceBlock,
            int sourceLaneIndex,
            Block destinationBlock,
            int destinationLaneIndex,
            bool useCornerMotion,
            float length)
        {
            this.sourceBlock = sourceBlock;
            this.sourceLaneIndex = sourceLaneIndex;
            this.destinationBlock = destinationBlock;
            this.destinationLaneIndex = destinationLaneIndex;
            this.useCornerMotion = useCornerMotion;
            this.length = length;
        }

        public readonly Block sourceBlock;
        public readonly int sourceLaneIndex;
        public readonly Block destinationBlock;
        public readonly int destinationLaneIndex;
        public readonly bool useCornerMotion;
        public readonly float length;
    }

    private sealed class ConveyorSlotDotPathCache
    {
        public readonly List<ConveyorSlotDotSegment> Segments = new List<ConveyorSlotDotSegment>(ConveyorSlotDotPathMaxSegments);
        public bool IsValid;
        public bool HasPath;
        public float TotalLength;
        public float PhaseOffset;

        public void Store(List<ConveyorSlotDotSegment> sourceSegments, float totalLength, float phaseOffset)
        {
            Segments.Clear();
            if (sourceSegments != null)
            {
                Segments.AddRange(sourceSegments);
            }

            IsValid = true;
            HasPath = Segments.Count > 0 && totalLength > 0.0001f;
            TotalLength = totalLength;
            PhaseOffset = phaseOffset;
        }

        public void StoreMissing()
        {
            Segments.Clear();
            IsValid = true;
            HasPath = false;
            TotalLength = 0f;
            PhaseOffset = 0f;
        }

        public void Invalidate()
        {
            IsValid = false;
        }
    }

    private void Awake()
    {
        CacheChildReferences();
        RefreshSerializedResourceOwnership();
        EnsureFloorObjectsInitialized();
        conveyorSlotDotPropertyBlock ??= new MaterialPropertyBlock();
        RefreshConveyorSlotDotVisuals();
    }

    private void RefreshSerializedResourceOwnership()
    {
        if (mapObject is Resource mapResource)
        {
            mapResource.SetOwningBlock(this);
            return;
        }

        Resource childResource = mapObject == null ? GetComponentInChildren<Resource>(true) : null;
        if (childResource != null && childResource.transform.IsChildOf(transform))
        {
            childResource.SetOwningBlock(this);
        }
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        TerrainGenerator.Active?.SetConveyorDotVisualActive(this, false);

        if (floorObjectPool == null)
        {
            return;
        }

        ResetFloorObjects(false, false);
    }

    public void Initialize(Vector2Int blockCoordinate, BlockType blockType)
    {
        CacheChildReferences();
        coordinate = blockCoordinate;
        type = blockType;
        objectName = $"{blockType}_{blockCoordinate.x}_{blockCoordinate.y}";
        gameObject.name = $"Block ({blockCoordinate.x}, {blockCoordinate.y})";
        inputAreaCenterVisibilityRequests.Clear();
        inputAreaCenterObjectsVisible = true;
        InvalidateConveyorRuntimeCaches();
        SetFocusVisible(false);
        SetMouseFocusVisible(false);
    }

    public void SetMapObject(MapObject value)
    {
        ConveyorBelt previousConveyorBelt = mapObject as ConveyorBelt;
        bool wasConveyor = IsConveyorStackingEnabled();
        bool wasFluidDirectionObject = IsFluidDirectionMapObject(mapObject);

        if (mapObject is Resource existingResource && existingResource != value)
        {
            existingResource.SetOwningBlock(null);
        }

        mapObject = value;

        if (mapObject is Resource resource)
        {
            resource.SetOwningBlock(this);
        }

        bool isConveyor = IsConveyorStackingEnabled();
        bool isFluidDirectionObject = IsFluidDirectionMapObject(mapObject);
        if (wasConveyor || isConveyor)
        {
            InvalidateConveyorRuntimeCachesAround();
            TerrainGenerator activeTerrain = TerrainGenerator.Active;
            if (activeTerrain != null && activeTerrain.IsConveyorRuntimeRefreshDeferred)
            {
                activeTerrain.QueueDeferredConveyorRuntimeRefresh(this);
            }
            else
            {
                RefreshConveyorActivityRegistration();
                RefreshConveyorSlotDotVisuals();
                RefreshBeltDirectionDebugVisuals();
            }

            previousConveyorBelt?.RefreshEndpointVisualsAndNeighbors();
            if (mapObject is ConveyorBelt currentConveyorBelt && currentConveyorBelt != previousConveyorBelt)
            {
                currentConveyorBelt.RefreshEndpointVisualsAndNeighbors();
            }
        }
        else if (wasFluidDirectionObject || isFluidDirectionObject)
        {
            RefreshBeltDirectionDebugVisuals();
        }

        if ((wasFluidDirectionObject || isFluidDirectionObject)
            && GameManager.Instance != null
            && GameManager.Instance.ShowDirections)
        {
            TerrainGenerator.Active?.RefreshBeltDirectionRuntimeVisibility();
        }

        NotifyRuntimeItemStackChanged();
    }

    private void InvalidateConveyorRuntimeCaches()
    {
        InvalidateConveyorConnectionCache();
        InvalidateConveyorLaneLayoutCache();
    }

    private void InvalidateConveyorRuntimeCachesAround()
    {
        InvalidateConveyorRuntimeCaches();
        TerrainGenerator.Active?.MarkConveyorNetworkDirty();
        if (!TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator))
        {
            return;
        }

        for (int i = 0; i < ConveyorNeighborDirections.Length; i++)
        {
            if (terrainGenerator.TryGetLoadedBlock(coordinate + ConveyorNeighborDirections[i], out Block neighborBlock)
                && neighborBlock != null)
            {
                neighborBlock.InvalidateConveyorRuntimeCaches();
            }
        }

        WakeConveyorMoveAttemptsAround();
    }

    public void InvalidateRuntimeConveyorTopology()
    {
        InvalidateConveyorRuntimeCachesAround();
        RefreshConveyorActivityRegistration(false, false);
        RefreshConveyorSlotDotVisuals();
        RefreshBeltDirectionDebugVisuals();
    }

    private void InvalidateConveyorConnectionCache()
    {
        cachedNextConveyorBlock = null;
        cachedHasNextConveyorBlock = false;
        conveyorConnectionCacheDirty = true;
        InvalidateConveyorSuccessorCache();
        InvalidateConveyorSlotDotPathCache();
    }

    private void InvalidateConveyorLaneLayoutCache()
    {
        cachedConveyorLaneLayoutValid = false;
        cachedFrontLaneIndex = -1;
        cachedBackLaneIndex = -1;
        conveyorLaneLayoutCacheDirty = true;
        InvalidateConveyorSuccessorCache();
        InvalidateConveyorSlotDotPathCache();
        MarkConveyorItemVisualDirty();
    }

    private void InvalidateConveyorSuccessorCache()
    {
        conveyorSuccessorCacheDirty = true;
    }

    private void InvalidateConveyorSlotDotPathCache()
    {
        for (int i = 0; i < conveyorSlotDotPathCaches.Count; i++)
        {
            conveyorSlotDotPathCaches[i].Invalidate();
        }
    }

    public void PrepareForPool()
    {
        SetFocusVisible(false);
        SetMouseFocusVisible(false);
        inputAreaCenterVisibilityRequests.Clear();
        inputAreaCenterObjectsVisible = true;
        ResetFloorObjects();

        MapObject childMapObject = mapObject;
        if (childMapObject != null && childMapObject.transform != null && childMapObject.transform.parent == transform)
        {
            childMapObject.transform.SetParent(null, true);
            if (Application.isPlaying)
            {
                Destroy(childMapObject.gameObject);
            }
            else
            {
                DestroyImmediate(childMapObject.gameObject);
            }
        }

        DestroyChildMapObjectsForPool(childMapObject);
        SetMapObject(null);
        coordinate = default;
        type = default;
        objectName = string.Empty;
        gameObject.name = "Pooled Block";
    }

    private void DestroyChildMapObjectsForPool(MapObject skippedObject)
    {
        MapObject[] childMapObjects = GetComponentsInChildren<MapObject>(true);
        for (int i = 0; i < childMapObjects.Length; i++)
        {
            MapObject childMapObject = childMapObjects[i];
            if (childMapObject == null
                || childMapObject == skippedObject
                || childMapObject.transform == null
                || !childMapObject.transform.IsChildOf(transform))
            {
                continue;
            }

            childMapObject.transform.SetParent(null, true);
            if (Application.isPlaying)
            {
                Destroy(childMapObject.gameObject);
            }
            else
            {
                DestroyImmediate(childMapObject.gameObject);
            }
        }
    }

    public void SetBodyRotation(float yRotation)
    {
        CacheChildReferences();
        if (body == null)
        {
            return;
        }

        body.localRotation = Quaternion.Euler(0f, yRotation, 0f);
        RefreshConveyorSlotDotVisuals();
    }

    public void SetBaseBodyVisible(bool visible)
    {
        CacheChildReferences();
        if (cachedBodyRenderers == null || cachedBodyRenderers.Length == 0)
        {
            return;
        }

        for (int i = 0; i < cachedBodyRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedBodyRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = visible;
        }
    }

    public bool TryAddFloorObject(int objectId, out PortableObject targetPortableObject)
    {
        return TryAddFloorObject(objectId, ResolveDefaultFloorDropReferenceWorldPosition(), out targetPortableObject);
    }

    private bool TryAddFloorObjectFromState(int objectId, out PortableObject targetPortableObject)
    {
        return TryAddFloorObject(objectId, transform.position, out targetPortableObject);
    }

    private bool TryAddFloorObjectToStackFromState(int objectId, int stackIndex, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (BlocksFloorObjectStacking()
            || objectId < 0
            || stackIndex < 0
            || stackIndex >= floorObjects.Count
            || stackIndex >= floorStacks.Count)
        {
            return false;
        }

        if (!ResolveFloorObjectPool())
        {
            return false;
        }

        Transform anchor = floorObjects[stackIndex];
        List<PortableObject> stack = floorStacks[stackIndex];
        if (anchor == null
            || stack == null
            || !IsStackCompatible(stack, objectId)
            || stack.Count >= Mathf.Max(1, maxFloorObjectsPerStack))
        {
            return false;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        ConfigureFloorObjectTransform(portableObject, anchor, stack.Count);
        portableObject.SetItem(objectId);
        portableObject.SetBatchedRendering(true);
        stack.Add(portableObject);
        targetPortableObject = portableObject;
        NotifyRuntimeItemStackChanged();
        return true;
    }

    private bool TryAddFloorObject(int objectId, Vector3 referenceWorldPosition, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (BlocksFloorObjectStacking())
        {
            return false;
        }

        if (!ResolveFloorObjectPool())
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExisting = pass == 0;
            if (!TryGetBestFloorStackIndex(objectId, requireExisting, referenceWorldPosition, out int stackIndex))
            {
                continue;
            }

            Transform anchor = floorObjects[stackIndex];
            List<PortableObject> stack = floorStacks[stackIndex];
            PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
            if (portableObject == null)
            {
                continue;
            }

            ConfigureFloorObjectTransform(portableObject, anchor, stack.Count);
            portableObject.SetItem(objectId);
            portableObject.SetBatchedRendering(true);
            stack.Add(portableObject);
            targetPortableObject = portableObject;
            NotifyRuntimeItemStackChanged();
            return true;
        }

        return false;
    }

    public bool CanAddInputAreaCenterObjects(int count)
    {
        return CanAddInputAreaCenterObjects(count, -1);
    }

    public bool CanAddInputAreaCenterObjects(int count, int itemId)
    {
        if (count <= 0)
        {
            return true;
        }

        if (itemId >= 0 && !InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(coordinate, itemId))
        {
            return false;
        }

        CleanupPortableStack(inputAreaCenterStack);
        if (itemId >= 0 && !IsStackCompatible(inputAreaCenterStack, itemId))
        {
            return false;
        }

        return ResolveInputAreaCenterCapacity() - inputAreaCenterStack.Count >= count;
    }

    public bool HasInputAreaCenterObjects()
    {
        CleanupPortableStack(inputAreaCenterStack);
        return inputAreaCenterStack.Count > 0;
    }

    public bool HasInputAreaCenterItem(int itemId)
    {
        CleanupPortableStack(inputAreaCenterStack);
        if (itemId < 0 || inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject bottom = inputAreaCenterStack[0];
        return bottom != null && bottom.ItemId == itemId;
    }

    public int GetInputAreaCenterItemCount(int itemId = -1)
    {
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return 0;
        }

        if (itemId < 0)
        {
            return inputAreaCenterStack.Count;
        }

        int count = 0;
        for (int i = 0; i < inputAreaCenterStack.Count; i++)
        {
            PortableObject portableObject = inputAreaCenterStack[i];
            if (portableObject != null && portableObject.ItemId == itemId)
            {
                count++;
            }
        }

        return count;
    }

    public int GetInputAreaCenterItemId()
    {
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return -1;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        return topObject != null ? topObject.ItemId : -1;
    }

    public void SetInputAreaCenterObjectsVisible(bool visible)
    {
        inputAreaCenterVisibilityRequests.Clear();
        ApplyInputAreaCenterObjectsVisible(visible);
    }

    public void SetInputAreaCenterObjectsVisible(bool visible, object visibilitySource)
    {
        if (visibilitySource == null)
        {
            SetInputAreaCenterObjectsVisible(visible);
            return;
        }

        inputAreaCenterVisibilityRequests[visibilitySource] = visible;
        ApplyInputAreaCenterObjectsVisible(ResolveRequestedInputAreaCenterVisibility());
    }

    public void ClearInputAreaCenterObjectsVisibilitySource(object visibilitySource)
    {
        if (visibilitySource == null || !inputAreaCenterVisibilityRequests.Remove(visibilitySource))
        {
            return;
        }

        ApplyInputAreaCenterObjectsVisible(ResolveRequestedInputAreaCenterVisibility());
    }

    private bool ResolveRequestedInputAreaCenterVisibility()
    {
        if (inputAreaCenterVisibilityRequests.Count <= 0)
        {
            return true;
        }

        foreach (bool visible in inputAreaCenterVisibilityRequests.Values)
        {
            if (visible)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyInputAreaCenterObjectsVisible(bool visible)
    {
        inputAreaCenterObjectsVisible = visible;
        CleanupPortableStack(inputAreaCenterStack);
        EnsureInputAreaCenterAnchorInitialized();

        for (int i = 0; i < inputAreaCenterStack.Count; i++)
        {
            PortableObject portableObject = inputAreaCenterStack[i];
            if (portableObject == null)
            {
                continue;
            }

            ApplyInputAreaCenterObjectVisibility(portableObject, i);
        }
    }

    public bool TryGetInputAreaCenterTopWorldPosition(int expectedItemId, out Vector3 worldPosition)
    {
        CleanupPortableStack(inputAreaCenterStack);
        EnsureInputAreaCenterAnchorInitialized();
        worldPosition = inputAreaCenterAnchor != null ? inputAreaCenterAnchor.position : transform.position;

        if (inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (expectedItemId >= 0 && itemId != expectedItemId)
        {
            return false;
        }

        worldPosition = topObject.transform.position;
        return true;
    }

    public bool TryConsumeOneInputAreaCenterObject(int expectedItemId, out int consumedItemId)
    {
        consumedItemId = -1;
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            NotifyRuntimeItemStackChanged();
            return false;
        }

        if (expectedItemId >= 0 && itemId != expectedItemId)
        {
            return false;
        }

        consumedItemId = itemId;
        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
        ReleaseFloorObject(topObject);
        NotifyRuntimeItemStackChanged();
        return true;
    }

    public bool TryTakeOneInputAreaCenterObject(out int takenItemId)
    {
        return TryConsumeOneInputAreaCenterObject(-1, out takenItemId);
    }

    public bool TryConsumeOneInputAreaCenterObjectAnimated(
        int expectedItemId,
        Vector3 targetWorldPosition,
        out int consumedItemId,
        float delay = 0f,
        Action onComplete = null)
    {
        consumedItemId = -1;
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            NotifyRuntimeItemStackChanged();
            return false;
        }

        if (expectedItemId >= 0 && itemId != expectedItemId)
        {
            return false;
        }

        consumedItemId = itemId;
        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
        NotifyRuntimeItemStackChanged();

        DroppedItemPickupGate gate = topObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        topObject.MoveTo(
            targetWorldPosition,
            Mathf.Max(0f, delay),
            () =>
            {
                ReleaseFloorObject(topObject);
                onComplete?.Invoke();
            });
        return true;
    }

    public int ConsumeInputAreaCenterObjects(int expectedItemId, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int consumed = 0;
        while (consumed < count && TryConsumeOneInputAreaCenterObject(expectedItemId, out _))
        {
            consumed++;
        }

        return consumed;
    }

    public int ConsumeInputAreaCenterObjectsAnimated(
        int expectedItemId,
        int count,
        Vector3 targetWorldPosition,
        float moveInterval = 0.1f)
    {
        if (count <= 0)
        {
            return 0;
        }

        int consumed = 0;
        float interval = Mathf.Max(0f, moveInterval);
        while (consumed < count
               && TryConsumeOneInputAreaCenterObjectAnimated(
                   expectedItemId,
                   targetWorldPosition,
                   out _,
                   consumed * interval))
        {
            consumed++;
        }

        return consumed;
    }

    public bool TryAddInputAreaCenterObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null, Func<Vector3> startWorldPositionProvider = null, bool useJumpArc = true, float moveDuration = PortableObject.MoveToDuration)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (objectId < 0
            || InputOutputModule.IsFluidItemId(objectId)
            || !ResolveFloorObjectPool())
        {
            return false;
        }

        if (!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(coordinate, objectId))
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null
            || inputAreaCenterStack.Count >= ResolveInputAreaCenterCapacity()
            || !IsStackCompatible(inputAreaCenterStack, objectId))
        {
            return false;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetItem(objectId);
        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(inputAreaCenterAnchor, true);
        portableObject.transform.position = startWorldPositionProvider != null ? startWorldPositionProvider() : startWorldPosition;
        portableObject.transform.rotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(inputAreaCenterObjectsVisible);

        int objectIndex = inputAreaCenterStack.Count;
        Vector3 finalLocalPosition = new Vector3(0f, objectIndex * InputAreaCenterVerticalSpacing, 0f);
        Vector3 finalWorldPosition = inputAreaCenterAnchor.TransformPoint(finalLocalPosition);
        inputAreaCenterStack.Add(portableObject);
        NotifyRuntimeItemStackChanged();
        DroppedItemPickupGate gate = portableObject.GetOrAddPickupGate();

        portableObject.MoveTo(() => inputAreaCenterAnchor != null ? inputAreaCenterAnchor.TransformPoint(finalLocalPosition) : finalWorldPosition, delay, startWorldPositionProvider, () =>
        {
            if (portableObject == null || inputAreaCenterAnchor == null)
            {
                onComplete?.Invoke();
                return;
            }

            ApplyInputAreaCenterObjectVisibility(portableObject, objectIndex);
            gate?.MarkSettled();
            onComplete?.Invoke();
        }, false, useJumpArc, moveDuration, false);

        targetPortableObject = portableObject;
        return true;
    }

    public bool CanAddFloorObjects(int count)
    {
        return CanAddFloorObjects(count, -1);
    }

    public bool CanAddFloorObjects(int count, int itemId)
    {
        EnsureFloorObjectsInitialized();

        if (count <= 0)
        {
            return true;
        }

        if (BlocksFloorObjectStacking())
        {
            return false;
        }

        return GetAvailableFloorCapacity(itemId) >= count;
    }

    public bool HasFloorObjectItem(int itemId)
    {
        EnsureFloorObjectsInitialized();

        if (itemId < 0)
        {
            return false;
        }

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null && portableObject.ItemId == itemId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public int CountFloorObjects(int itemId)
    {
        EnsureFloorObjectsInitialized();
        if (itemId < 0)
        {
            return 0;
        }

        int count = 0;
        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null && portableObject.ItemId == itemId)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public int RemoveFloorObjects(int itemId, int count)
    {
        EnsureFloorObjectsInitialized();
        if (itemId < 0 || count <= 0)
        {
            return 0;
        }

        int remaining = count;
        for (int stackIndex = 0; stackIndex < floorStacks.Count && remaining > 0; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            for (int objectIndex = stack.Count - 1; objectIndex >= 0 && remaining > 0; objectIndex--)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject == null)
                {
                    stack.RemoveAt(objectIndex);
                    continue;
                }

                if (portableObject.ItemId != itemId)
                {
                    continue;
                }

                stack.RemoveAt(objectIndex);
                ReleaseFloorObject(portableObject);
                remaining--;
            }
        }

        return count - remaining;
    }

    public bool TryRemoveFloorObject(PortableObject targetPortableObject)
    {
        EnsureFloorObjectsInitialized();
        if (targetPortableObject == null)
        {
            return false;
        }

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count <= 0)
            {
                continue;
            }

            int objectIndex = stack.IndexOf(targetPortableObject);
            if (objectIndex < 0)
            {
                continue;
            }

            stack.RemoveAt(objectIndex);
            ReleaseFloorObject(targetPortableObject);

            Transform anchor = stackIndex < floorObjects.Count ? floorObjects[stackIndex] : null;
            if (anchor != null)
            {
                for (int i = objectIndex; i < stack.Count; i++)
                {
                    PortableObject portableObject = stack[i];
                    if (portableObject != null)
                    {
                        ConfigureFloorObjectTransform(portableObject, anchor, i);
                        portableObject.SetBatchedRendering(true);
                    }
                }
            }

            return true;
        }

        return false;
    }

    public bool TryAddFloorObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null, Func<Vector3> startWorldPositionProvider = null)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (BlocksFloorObjectStacking())
        {
            return false;
        }

        if (!ResolveFloorObjectPool())
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExisting = pass == 0;
            if (!TryGetBestFloorStackIndex(objectId, requireExisting, startWorldPosition, out int stackIndex))
            {
                continue;
            }

            Transform anchor = floorObjects[stackIndex];
            List<PortableObject> stack = floorStacks[stackIndex];
            PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
            if (portableObject == null)
            {
                continue;
            }

            portableObject.SetItem(objectId);
            portableObject.SetBatchedRendering(false);
            portableObject.transform.SetParent(anchor, true);
            portableObject.transform.position = startWorldPositionProvider != null ? startWorldPositionProvider() : startWorldPosition;
            portableObject.transform.rotation = Quaternion.identity;
            portableObject.transform.localScale = Vector3.one;
            portableObject.gameObject.SetActive(true);

            int objectIndex = stack.Count;
            Vector3 finalLocalPosition = new Vector3(0f, objectIndex * floorObjectVerticalSpacing, 0f);
            Vector3 finalWorldPosition = anchor.TransformPoint(finalLocalPosition);
            stack.Add(portableObject);
            NotifyRuntimeItemStackChanged();
            DroppedItemPickupGate gate = portableObject.GetOrAddPickupGate();

            portableObject.MoveTo(() => anchor != null ? anchor.TransformPoint(finalLocalPosition) : finalWorldPosition, delay, startWorldPositionProvider, () =>
            {
                if (portableObject == null || anchor == null)
                {
                    onComplete?.Invoke();
                    return;
                }

                portableObject.transform.SetParent(anchor, false);
                portableObject.transform.localPosition = finalLocalPosition;
                portableObject.transform.localRotation = Quaternion.identity;
                portableObject.transform.localScale = Vector3.one;
                portableObject.gameObject.SetActive(true);
                portableObject.SetBatchedRendering(true);
                gate?.MarkSettled();
                onComplete?.Invoke();
            }, false, true, PortableObject.MoveToDuration, false);

            targetPortableObject = portableObject;
            return true;
        }

        return false;
    }

    private bool TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt)
    {
        conveyorBelt = null;
        if (IsActiveRuntimeConveyor(runtimeConveyorOverride))
        {
            conveyorBelt = runtimeConveyorOverride;
            return true;
        }

        if (mapObject is ConveyorBelt mappedConveyor && IsActiveRuntimeConveyor(mappedConveyor))
        {
            conveyorBelt = mappedConveyor;
            return true;
        }

        if (ConvayorBelt2F.TryFindCoveringBelt(coordinate, out ConvayorBelt2F belt2F)
            && IsActiveRuntimeConveyor(belt2F))
        {
            conveyorBelt = belt2F;
            return true;
        }

        return false;
    }

    private bool TryGetPlayerRuntimeConveyorBelt(out ConveyorBelt conveyorBelt)
    {
        if (ConvayorBelt2F.TryFindCoveringBelt(coordinate, out ConvayorBelt2F belt2F)
            && IsActiveRuntimeConveyor(belt2F))
        {
            conveyorBelt = belt2F;
            return true;
        }

        return TryGetRuntimeConveyorBelt(out conveyorBelt);
    }

    private static bool IsActiveRuntimeConveyor(ConveyorBelt conveyorBelt)
    {
        return conveyorBelt != null
               && conveyorBelt.IsRuntimeRootAvailable;
    }

    private bool TryGetRuntimeBelt2F(out ConvayorBelt2F belt2F)
    {
        belt2F = null;
        if (TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt)
            && conveyorBelt is ConvayorBelt2F resolvedBelt2F)
        {
            belt2F = resolvedBelt2F;
            return true;
        }

        return false;
    }

    private bool TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F belt2F)
    {
        belt2F = null;
        return mapObject is ConveyorBelt mappedConveyor
               && !(mappedConveyor is ConvayorBelt2F)
               && ConvayorBelt2F.TryFindCoveringBelt(coordinate, out belt2F)
               && IsActiveRuntimeConveyor(belt2F)
               && belt2F.IsBridgeCenterCoordinate(coordinate);
    }

    private bool IsBelt2FBridgeCenterFor(ConvayorBelt2F belt2F)
    {
        return belt2F != null
               && TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F centerBelt)
               && ReferenceEquals(centerBelt, belt2F);
    }

    private static bool IsBelt2FBridgeLaneIndex(int laneIndex)
    {
        return laneIndex == 1 || laneIndex == 3;
    }

    private bool IsActiveRuntimeConveyorLaneIndex(int laneIndex)
    {
        return IsActiveConveyorLaneIndex(laneIndex)
               || (IsBelt2FBridgeLaneIndex(laneIndex) && TryGetBelt2FBridgeCenterBelt(out _));
    }

    public bool IsConveyorStackingEnabled()
    {
        return TryGetRuntimeConveyorBelt(out _);
    }

    public bool HasRuntimeBelt2FConveyor()
    {
        return TryGetRuntimeBelt2F(out _)
               || TryGetBelt2FBridgeCenterBelt(out _);
    }

    public bool TryGetConveyorStandingDistanceSqr(Vector3 worldPosition, out float distanceSqr)
    {
        distanceSqr = float.MaxValue;
        if (!TryGetPlayerRuntimeConveyorBelt(out ConveyorBelt playerConveyor))
        {
            return false;
        }

        ConveyorBelt previousOverride = runtimeConveyorOverride;
        runtimeConveyorOverride = playerConveyor;
        try
        {
            if (!IsConveyorStackingEnabled())
            {
                return false;
            }

            if (IsCornerConveyor())
            {
                return TryGetCornerConveyorStandingDistanceSqr(worldPosition, out distanceSqr);
            }

            Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
            Vector2 flatLocalPosition = new Vector2(localPosition3.x, localPosition3.z);
            if (!TryGetConveyorLocalAxes(out Vector2 localFlowAxis, out Vector2 localRightAxis))
            {
                localFlowAxis = Vector2.up;
                localRightAxis = Vector2.right;
            }

            float forward = Vector2.Dot(flatLocalPosition, localFlowAxis);
            float right = Vector2.Dot(flatLocalPosition, localRightAxis);
            float clampedForward = Mathf.Clamp(forward, -0.5f, 0.5f);
            float halfWidth = Mathf.Max(0.18f, GetConveyorLaneHalfExtent() + 0.12f);
            float clampedRight = Mathf.Clamp(right, -halfWidth, halfWidth);
            Vector2 delta = new Vector2(forward - clampedForward, right - clampedRight);
            distanceSqr = delta.sqrMagnitude;
            return true;
        }
        finally
        {
            runtimeConveyorOverride = previousOverride;
        }
    }

    public bool TryGetConveyorCarryVelocity(Vector3 worldPosition, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!TryGetPlayerRuntimeConveyorBelt(out ConveyorBelt playerConveyor))
        {
            return false;
        }

        ConveyorBelt previousOverride = runtimeConveyorOverride;
        runtimeConveyorOverride = playerConveyor;
        try
        {
            if (!IsConveyorStackingEnabled())
            {
                return false;
            }

            float conveyorSpeed = GetConveyorSpeed();
            if (conveyorSpeed <= 0f)
            {
                return false;
            }

            if (IsCornerConveyor())
            {
                if (!TryGetCornerConveyorCarryDirection(worldPosition, out Vector3 cornerCarryDirection))
                {
                    return false;
                }

                cornerCarryDirection.y = 0f;
                if (cornerCarryDirection.sqrMagnitude <= 0.0001f)
                {
                    return false;
                }

                velocity = cornerCarryDirection.normalized * conveyorSpeed;
                return true;
            }

            if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
            {
                return false;
            }

            Vector3 carryDirection = new Vector3(flowDirection.x, 0f, flowDirection.y);
            carryDirection.y = 0f;
            if (carryDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            velocity = carryDirection.normalized * conveyorSpeed;
            return true;
        }
        finally
        {
            runtimeConveyorOverride = previousOverride;
        }
    }

    public bool ShouldBlockPlayerCarryForCrossingBelt2F(Vector3 incomingCarryVelocity)
    {
        if (!TryGetMappedConveyorCrossingBelt2F(out Vector3 lowerDirection, out Vector3 upperDirection))
        {
            return false;
        }

        Vector3 flatIncomingVelocity = incomingCarryVelocity;
        flatIncomingVelocity.y = 0f;
        if (flatIncomingVelocity.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        flatIncomingVelocity.Normalize();
        float lowerAlignment = Mathf.Abs(Vector3.Dot(flatIncomingVelocity, lowerDirection));
        float upperAlignment = Mathf.Abs(Vector3.Dot(flatIncomingVelocity, upperDirection));
        return lowerAlignment >= upperAlignment;
    }

    public bool TryGetConveyorStandingWorldHeight(Vector3 worldPosition, out float worldHeight)
    {
        worldHeight = 0f;
        if (!TryGetPlayerRuntimeConveyorBelt(out ConveyorBelt playerConveyor))
        {
            return false;
        }

        ConveyorBelt previousOverride = runtimeConveyorOverride;
        runtimeConveyorOverride = playerConveyor;
        try
        {
            if (!IsConveyorStackingEnabled())
            {
                return false;
            }

            if (playerConveyor is ConvayorBelt2F belt2F)
            {
                worldHeight = belt2F.ApplyPathHeight(worldPosition).y;
                return true;
            }

            worldHeight = transform.position.y + ConveyorLaneHeight;
            return true;
        }
        finally
        {
            runtimeConveyorOverride = previousOverride;
        }
    }

    private bool TryGetMappedConveyorCrossingBelt2F(out Vector3 lowerDirection, out Vector3 upperDirection)
    {
        lowerDirection = Vector3.zero;
        upperDirection = Vector3.zero;
        if (!(mapObject is ConveyorBelt mappedConveyor)
            || mappedConveyor is ConvayorBelt2F
            || !IsActiveRuntimeConveyor(mappedConveyor)
            || !ConvayorBelt2F.TryFindCoveringBelt(coordinate, out ConvayorBelt2F coveringBelt2F)
            || !IsActiveRuntimeConveyor(coveringBelt2F)
            || !TryGetFlatOutputDirection(mappedConveyor, out lowerDirection)
            || !TryGetFlatOutputDirection(coveringBelt2F, out upperDirection)
            || Mathf.Abs(Vector3.Dot(lowerDirection, upperDirection)) > 0.0001f)
        {
            lowerDirection = Vector3.zero;
            upperDirection = Vector3.zero;
            return false;
        }

        return true;
    }

    private static bool TryGetFlatOutputDirection(ConveyorBelt conveyorBelt, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (conveyorBelt == null
            || !conveyorBelt.TryGetOutputDirection(conveyorBelt.transform.rotation, out Vector2Int outputDirection)
            || outputDirection == Vector2Int.zero)
        {
            return false;
        }

        direction = new Vector3(outputDirection.x, 0f, outputDirection.y).normalized;
        return direction.sqrMagnitude > 0.0001f;
    }

    public bool TryGetConveyorCarryDelta(Vector3 worldPosition, float deltaTime, out Vector3 delta)
    {
        delta = Vector3.zero;
        if (!TryGetPlayerRuntimeConveyorBelt(out ConveyorBelt playerConveyor))
        {
            return false;
        }

        ConveyorBelt previousOverride = runtimeConveyorOverride;
        runtimeConveyorOverride = playerConveyor;
        try
        {
            if (!IsConveyorStackingEnabled() || deltaTime <= 0f)
            {
                return false;
            }

            float conveyorSpeed = GetConveyorSpeed();
            if (conveyorSpeed <= 0f)
            {
                return false;
            }

            if (IsCornerConveyor())
            {
                return TryGetCornerConveyorCarryDelta(worldPosition, conveyorSpeed, deltaTime, out delta);
            }

            if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
            {
                return false;
            }

            Vector3 carryDirection = new Vector3(flowDirection.x, 0f, flowDirection.y).normalized;
            delta = ApplyConveyorCarryPathHeightDelta(
                worldPosition,
                carryDirection * conveyorSpeed * deltaTime);
            return delta.sqrMagnitude > 0.0000001f;
        }
        finally
        {
            runtimeConveyorOverride = previousOverride;
        }
    }

    private Vector3 ApplyConveyorCarryPathHeightDelta(Vector3 worldPosition, Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.0000001f
            || !TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt)
            || !(conveyorBelt is ConvayorBelt2F belt2F))
        {
            return delta;
        }

        Vector3 currentPathPosition = belt2F.ApplyPathHeight(worldPosition);
        Vector3 nextPathPosition = belt2F.ApplyPathHeight(worldPosition + delta);
        delta.y += nextPathPosition.y - currentPathPosition.y;
        return delta;
    }

    public bool TryGetConveyorCarryDeltaWithHandoff(Vector3 worldPosition, float deltaTime, out Block resultingBlock, out Vector3 delta)
    {
        resultingBlock = this;
        delta = Vector3.zero;
        if (!TryGetPlayerRuntimeConveyorBelt(out ConveyorBelt playerConveyor))
        {
            return false;
        }

        ConveyorBelt previousOverride = runtimeConveyorOverride;
        runtimeConveyorOverride = playerConveyor;
        try
        {
            if (!IsConveyorStackingEnabled() || deltaTime <= 0f)
            {
                return false;
            }

            float conveyorSpeed = GetConveyorSpeed();
            if (conveyorSpeed <= 0f)
            {
                return false;
            }

            if (!IsCornerConveyor())
            {
                if (!TryGetConveyorCarryDelta(worldPosition, deltaTime, out delta))
                {
                    return false;
                }

                resultingBlock = this;
                return true;
            }

            return TryGetCornerConveyorCarryDeltaWithHandoff(worldPosition, conveyorSpeed, deltaTime, out resultingBlock, out delta);
        }
        finally
        {
            runtimeConveyorOverride = previousOverride;
        }
    }

    public bool TryGetNextConnectedConveyorBlock(out Block nextBlock)
    {
        return TryGetNextConveyorBlock(out nextBlock);
    }

    public bool IsCornerConveyorBlock()
    {
        return IsConveyorStackingEnabled() && IsCornerConveyor();
    }

    public bool CanAddConveyorObjects(int count)
    {
        if (count <= 0)
        {
            return true;
        }

        CleanupConveyorStack();
        return IsConveyorStackingEnabled() && GetAvailableConveyorCapacity() >= count;
    }

    public int GetAvailableConveyorCapacity()
    {
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return 0;
        }

        int capacity = 0;
        int laneCount = GetConveyorLaneCount();
        for (int i = 0; i < laneCount; i++)
        {
            if (IsValidConveyorLaneIndex(i) && !HasConveyorItemAtLane(i))
            {
                capacity++;
            }
        }

        return capacity;
    }

    public bool TryAddConveyorObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null, Func<Vector3> startWorldPositionProvider = null)
    {
        return TryAddConveyorObjectAnimatedWithPlacementReference(
            objectId,
            startWorldPosition,
            startWorldPosition,
            delay,
            out targetPortableObject,
            onComplete,
            startWorldPositionProvider);
    }

    public bool TryAddConveyorObjectAnimatedAtPlacement(
        int objectId,
        Vector3 placementReferenceWorldPosition,
        Vector3 startWorldPosition,
        float delay,
        out PortableObject targetPortableObject,
        Action onComplete = null,
        Func<Vector3> startWorldPositionProvider = null,
        float movementReleaseDelay = 0f,
        bool useJumpArc = true,
        float moveDuration = PortableObject.MoveToDuration)
    {
        return TryAddConveyorObjectAnimatedWithPlacementReference(
            objectId,
            placementReferenceWorldPosition,
            startWorldPosition,
            delay,
            out targetPortableObject,
            onComplete,
            startWorldPositionProvider,
            movementReleaseDelay,
            useJumpArc,
            moveDuration);
    }

    public bool CanAddConveyorObjectAtPlacement(int objectId, Vector3 placementReferenceWorldPosition)
    {
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        return objectId >= 0
               && IsConveyorStackingEnabled()
               && (ShouldUseVirtualConveyorItemRendering() || ResolveFloorObjectPool())
               && TryGetBestConveyorPlacementLaneIndex(placementReferenceWorldPosition, out _);
    }

    public bool TryAddConveyorObjectAnimatedNearConnected(
        int objectId,
        Vector3 placementReferenceWorldPosition,
        Vector3 startWorldPosition,
        float delay,
        out PortableObject targetPortableObject,
        out Block targetBlock,
        Action onComplete = null,
        Func<Vector3> startWorldPositionProvider = null,
        float movementReleaseDelay = 0f,
        bool useJumpArc = true,
        float moveDuration = PortableObject.MoveToDuration)
    {
        targetPortableObject = null;
        targetBlock = null;

        if (TryAddConveyorObjectAnimatedWithPlacementReference(
                objectId,
                placementReferenceWorldPosition,
                startWorldPosition,
                delay,
                out targetPortableObject,
                onComplete,
                startWorldPositionProvider,
                movementReleaseDelay,
                useJumpArc,
                moveDuration))
        {
            targetBlock = this;
            return true;
        }

        if (!TryFindConnectedConveyorInsertionBlock(out Block insertionBlock))
        {
            return false;
        }

        if (!insertionBlock.TryAddConveyorObjectAnimatedWithPlacementReference(
                objectId,
                placementReferenceWorldPosition,
                startWorldPosition,
                delay,
                out targetPortableObject,
                onComplete,
                startWorldPositionProvider,
                movementReleaseDelay,
                useJumpArc,
                moveDuration))
        {
            return false;
        }

        targetBlock = insertionBlock;
        return true;
    }

    private bool TryAddConveyorObjectAnimatedWithPlacementReference(
        int objectId,
        Vector3 placementReferenceWorldPosition,
        Vector3 startWorldPosition,
        float delay,
        out PortableObject targetPortableObject,
        Action onComplete = null,
        Func<Vector3> startWorldPositionProvider = null,
        float movementReleaseDelay = 0f,
        bool useJumpArc = true,
        float moveDuration = PortableObject.MoveToDuration)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        bool useVirtualDataSlot = ShouldUseVirtualConveyorItemRendering()
            && ShouldSnapConveyorPlacementImmediately(delay, startWorldPositionProvider);

        if (objectId < 0
            || !IsConveyorStackingEnabled()
            || (!useVirtualDataSlot && !ResolveFloorObjectPool())
            || !TryGetBestConveyorPlacementLaneIndex(placementReferenceWorldPosition, out int laneIndex))
        {
            return false;
        }

        Transform anchor = floorObjects != null && laneIndex < floorObjects.Count ? floorObjects[laneIndex] : null;
        if (anchor == null)
        {
            return false;
        }

        if (useVirtualDataSlot)
        {
            SetConveyorItemAtLane(laneIndex, objectId, null, ConveyorPickupGateState.Settled());
            NotifyRuntimeItemStackChanged();
            HoldConveyorLaneMovement(laneIndex, movementReleaseDelay);
            WakeConveyorMoveAttempts();
            RefreshConveyorActivityRegistration();
            onComplete?.Invoke();
            return true;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetItem(objectId);
        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(transform, true);
        portableObject.transform.position = startWorldPositionProvider != null ? startWorldPositionProvider() : startWorldPosition;
        portableObject.transform.rotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);
        SetConveyorItemAtLane(laneIndex, objectId, portableObject, ConveyorPickupGateState.Settled());
        NotifyRuntimeItemStackChanged();
        HoldConveyorLaneMovement(laneIndex, movementReleaseDelay);
        WakeConveyorMoveAttempts();
        RefreshConveyorActivityRegistration();

        DroppedItemPickupGate gate = portableObject.GetOrAddPickupGate();

        if (ShouldSnapConveyorPlacementImmediately(delay, startWorldPositionProvider))
        {
            ConfigureConveyorObjectTransform(portableObject, laneIndex, anchor);
            ApplyConveyorObjectRenderingMode(portableObject);
            gate?.MarkSettled();
            TryVirtualizeSettledConveyorPortableObject(laneIndex, portableObject);
            MarkConveyorItemVisualDirty();
            WakeConveyorMoveAttempts();
            RefreshConveyorActivityRegistration();
            onComplete?.Invoke();
            targetPortableObject = portableObject;
            return true;
        }

        Vector3 finalWorldPosition = GetConveyorLaneWorldPosition(laneIndex, anchor);
        portableObject.MoveTo(() => anchor != null ? GetConveyorLaneWorldPosition(laneIndex, anchor) : finalWorldPosition, delay, startWorldPositionProvider, () =>
        {
            if (portableObject == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (anchor != null)
            {
                ConfigureConveyorObjectTransform(portableObject, laneIndex, anchor);
            }

            ApplyConveyorObjectRenderingMode(portableObject);
            gate?.MarkSettled();
            TryVirtualizeSettledConveyorPortableObject(laneIndex, portableObject);
            MarkConveyorItemVisualDirty();
            WakeConveyorMoveAttempts();
            RefreshConveyorActivityRegistration();
            onComplete?.Invoke();
        }, false, useJumpArc, moveDuration, false);

        targetPortableObject = portableObject;
        return true;
    }

    private bool TryFindConnectedConveyorInsertionBlock(out Block insertionBlock)
    {
        insertionBlock = null;

        if (TryFindConnectedConveyorInsertionBlockInDirection(
                true,
                ConveyorPlacementForwardSearchDepth,
                new HashSet<Block> { this },
                out insertionBlock))
        {
            return true;
        }

        return TryFindConnectedConveyorInsertionBlockInDirection(
            false,
            ConveyorPlacementBackwardSearchDepth,
            new HashSet<Block> { this },
            out insertionBlock);
    }

    private bool TryFindConnectedConveyorInsertionBlockInDirection(
        bool downstream,
        int maxDepth,
        HashSet<Block> visitedBlocks,
        out Block insertionBlock)
    {
        insertionBlock = null;
        if (maxDepth <= 0)
        {
            return false;
        }

        Block currentBlock = this;
        for (int depth = 0; depth < maxDepth; depth++)
        {
            Block nextBlock;
            bool hasNextBlock = downstream
                ? currentBlock.TryGetNextConveyorBlock(out nextBlock)
                : currentBlock.TryGetPreviousConveyorBlock(out nextBlock);

            if (!hasNextBlock || nextBlock == null || !visitedBlocks.Add(nextBlock))
            {
                return false;
            }

            if (nextBlock.GetAvailableConveyorCapacity() > 0)
            {
                insertionBlock = nextBlock;
                return true;
            }

            currentBlock = nextBlock;
        }

        return false;
    }

    public bool TryPickupOneConveyorObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex = -1, int preferredItemId = -1, int maxPickupCount = int.MaxValue)
    {
        if (player == null || pickupRadius <= 0f || maxPickupCount <= 0)
        {
            return false;
        }

        bool pickedAny = false;
        int pickupLimit = Mathf.Min(ConveyorCellItemUnit, maxPickupCount);
        for (int i = 0; i < pickupLimit; i++)
        {
            if (!TryPickupSingleConveyorObjectToBag(player, playerPosition, pickupRadius, preferredSlotIndex, preferredItemId))
            {
                break;
            }

            pickedAny = true;
        }

        return pickedAny;
    }

    public bool TryPreviewPickupOneConveyorObject(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId)
    {
        return TryPreviewPickupConveyorObjects(
            player,
            playerPosition,
            pickupRadius,
            preferredItemId,
            out previewItemId,
            out _);
    }

    public bool TryPreviewPickupConveyorObjects(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        int laneIndex = FindBestConveyorPickupLaneIndex(playerPosition, pickupRadiusSqr, gateOriginPosition, true, preferredItemId);
        if (laneIndex < 0)
        {
            return false;
        }

        previewItemId = GetConveyorItemIdAtLane(laneIndex);
        if (previewItemId < 0)
        {
            return false;
        }

        int laneCount = GetConveyorLaneCount();
        for (int previewLaneIndex = 0; previewLaneIndex < laneCount && previewPickupCount < ConveyorCellItemUnit; previewLaneIndex++)
        {
            if (TryGetConveyorPickupLaneCandidate(
                    previewLaneIndex,
                    playerPosition,
                    pickupRadiusSqr,
                    gateOriginPosition,
                    true,
                    previewItemId,
                    out _,
                    out _))
            {
                previewPickupCount++;
            }
        }

        return previewPickupCount > 0;
    }

    private bool TryPickupSingleConveyorObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex, int preferredItemId)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        int laneIndex = FindBestConveyorPickupLaneIndex(playerPosition, pickupRadiusSqr, gateOriginPosition, true, preferredItemId);
        if (laneIndex < 0)
        {
            return false;
        }

        PortableObject targetObject = GetConveyorPortableObjectAtLane(laneIndex);
        int itemId = GetConveyorItemIdAtLane(laneIndex);
        if (itemId < 0)
        {
            ClearConveyorItemAtLane(laneIndex);
            WakeConveyorMoveAttemptsAround();
            ReleaseFloorObject(targetObject);
            return false;
        }

        if (!TryAddPickupObjectToBagOrMatchingHand(player, itemId, preferredSlotIndex, out PortableObject storageTarget, out bool addedToHand))
        {
            return false;
        }

        targetObject = MaterializeConveyorObjectForTransfer(targetObject, itemId, laneIndex);
        ClearConveyorItemAtLane(laneIndex);
        WakeConveyorMoveAttemptsAround();
        if (targetObject != null)
        {
            ReleasePickupObjectToStorage(targetObject, storageTarget, addedToHand);
        }

        return true;
    }

    public bool TryPickupOneConveyorObjectToHand(Player player, Vector3 playerPosition, float pickupRadius, int maxPickupCount = int.MaxValue)
    {
        if (player == null || pickupRadius <= 0f || maxPickupCount <= 0)
        {
            return false;
        }

        bool pickedAny = false;
        int pickupLimit = Mathf.Min(ConveyorCellItemUnit, maxPickupCount);
        for (int i = 0; i < pickupLimit; i++)
        {
            if (!TryPickupSingleConveyorObjectToHand(player, playerPosition, pickupRadius))
            {
                break;
            }

            pickedAny = true;
        }

        return pickedAny;
    }

    private bool TryPickupSingleConveyorObjectToHand(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        int laneIndex = FindBestConveyorPickupLaneIndex(playerPosition, pickupRadiusSqr, gateOriginPosition, true);
        if (laneIndex < 0)
        {
            return false;
        }

        PortableObject targetObject = GetConveyorPortableObjectAtLane(laneIndex);
        int itemId = GetConveyorItemIdAtLane(laneIndex);
        if (itemId < 0)
        {
            ClearConveyorItemAtLane(laneIndex);
            WakeConveyorMoveAttemptsAround();
            ReleaseFloorObject(targetObject);
            return false;
        }

        if (!player.TryAddToHand(itemId, out PortableObject handTarget))
        {
            return false;
        }

        targetObject = MaterializeConveyorObjectForTransfer(targetObject, itemId, laneIndex);
        ClearConveyorItemAtLane(laneIndex);
        WakeConveyorMoveAttemptsAround();
        if (targetObject != null)
        {
            ReleaseFloorObjectToHand(targetObject, handTarget);
        }

        return true;
    }

    public bool TryTakeOneConveyorObject(Vector3 referenceWorldPosition, out int takenItemId)
    {
        return TryTakeOneConveyorObject(referenceWorldPosition, null, out takenItemId);
    }

    public bool TryTakeOneConveyorObject(Vector3 referenceWorldPosition, Predicate<int> itemFilter, out int takenItemId)
    {
        return TryTakeOneConveyorObject(referenceWorldPosition, itemFilter, -1f, out takenItemId);
    }

    public bool TryTakeOneConveyorObject(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        float maxDistance,
        out int takenItemId)
    {
        takenItemId = -1;
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        if (!IsConveyorStackingEnabled()
            || !TryGetClosestConveyorItemLane(
                referenceWorldPosition,
                itemFilter,
                maxDistance,
                out int laneIndex))
        {
            return false;
        }

        PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
        int itemId = GetConveyorItemIdAtLane(laneIndex);
        if (itemId < 0)
        {
            ClearConveyorItemAtLane(laneIndex);
            NotifyConveyorLaneVacatedExternally(laneIndex);
            ReleaseFloorObject(portableObject);
            return false;
        }

        portableObject = MaterializeConveyorObjectForTransfer(portableObject, itemId, laneIndex);
        ClearConveyorItemAtLane(laneIndex);
        NotifyRuntimeItemStackChanged();
        NotifyConveyorLaneVacatedExternally(laneIndex);
        ReleaseFloorObject(portableObject);
        takenItemId = itemId;
        return true;
    }

    public bool TryGetClosestConveyorObjectWorldPosition(Vector3 referenceWorldPosition, out Vector3 worldPosition)
    {
        return TryGetClosestConveyorObjectWorldPosition(referenceWorldPosition, null, out worldPosition);
    }

    public bool TryGetClosestConveyorObjectWorldPosition(Vector3 referenceWorldPosition, Predicate<int> itemFilter, out Vector3 worldPosition)
    {
        return TryGetClosestConveyorObjectWorldPosition(referenceWorldPosition, itemFilter, -1f, out worldPosition);
    }

    public bool TryGetClosestConveyorObjectWorldPosition(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        float maxDistance,
        out Vector3 worldPosition)
    {
        worldPosition = transform.position;
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        int bestLaneIndex = -1;
        float bestDistanceSqr = float.MaxValue;
        float maxDistanceSqr = maxDistance >= 0f ? maxDistance * maxDistance : float.MaxValue;
        Vector3 bestWorldPosition = worldPosition;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            int itemId = GetConveyorItemIdAtLane(laneIndex);
            if (itemId < 0 || (itemFilter != null && !itemFilter(itemId)))
            {
                continue;
            }

            Vector3 candidateWorldPosition = GetConveyorItemVisualWorldPosition(laneIndex);
            Vector3 offset = candidateWorldPosition - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > maxDistanceSqr
                || (bestLaneIndex >= 0 && distanceSqr >= bestDistanceSqr))
            {
                continue;
            }

            bestLaneIndex = laneIndex;
            bestDistanceSqr = distanceSqr;
            bestWorldPosition = candidateWorldPosition;
        }

        if (bestLaneIndex < 0)
        {
            return false;
        }

        worldPosition = bestWorldPosition;
        return true;
    }

    private bool TrySetConveyorObjectAtLane(int laneIndex, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        if (objectId < 0
            || !IsConveyorStackingEnabled()
            || !IsValidConveyorLaneIndex(laneIndex)
            || HasConveyorItemAtLane(laneIndex))
        {
            return false;
        }

        Transform anchor = floorObjects != null && laneIndex < floorObjects.Count ? floorObjects[laneIndex] : null;
        if (anchor == null)
        {
            return false;
        }

        if (ShouldUseVirtualConveyorItemRendering())
        {
            SetConveyorItemAtLane(laneIndex, objectId, null, ConveyorPickupGateState.Settled());
            WakeConveyorMoveAttempts();
            targetPortableObject = null;
            RefreshConveyorActivityRegistration();
            return true;
        }

        if (!ResolveFloorObjectPool())
        {
            return false;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetItem(objectId);
        ConfigureConveyorObjectTransform(portableObject, laneIndex, anchor);
        ApplyConveyorObjectRenderingMode(portableObject);
        SetConveyorItemAtLane(laneIndex, objectId, portableObject, ConveyorPickupGateState.Settled());
        WakeConveyorMoveAttempts();
        targetPortableObject = portableObject;
        RefreshConveyorActivityRegistration();
        return true;
    }

    public List<int> CaptureFloorObjectState()
    {
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        List<int> itemIds = new List<int>();
        int floorStackItemCount = 0;
        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null)
                {
                    floorStackItemCount++;
                }
            }
        }

        if (floorStackItemCount > 0)
        {
            itemIds.Add(FloorStackStateSentinel);
            itemIds.Add(floorStacks.Count);

            for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
            {
                List<PortableObject> stack = floorStacks[stackIndex];
                int stackItemCount = 0;
                for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
                {
                    if (stack[objectIndex] != null)
                    {
                        stackItemCount++;
                    }
                }

                itemIds.Add(stackItemCount);
                for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
                {
                    PortableObject portableObject = stack[objectIndex];
                    if (portableObject != null)
                    {
                        itemIds.Add(portableObject.ItemId);
                    }
                }
            }
        }

        int centerStackCount = 0;
        for (int i = 0; i < inputAreaCenterStack.Count; i++)
        {
            if (inputAreaCenterStack[i] != null)
            {
                centerStackCount++;
            }
        }

        if (centerStackCount > 0)
        {
            itemIds.Add(InputAreaCenterStackStateSentinel);
            itemIds.Add(centerStackCount);

            for (int i = 0; i < inputAreaCenterStack.Count; i++)
            {
                PortableObject portableObject = inputAreaCenterStack[i];
                if (portableObject != null)
                {
                    itemIds.Add(portableObject.ItemId);
                }
            }
        }

        bool hasConveyorObjects = false;
        int conveyorLaneCount = GetConveyorLaneCount();
        for (int i = 0; i < conveyorLaneCount; i++)
        {
            if (HasConveyorItemAtLane(i))
            {
                hasConveyorObjects = true;
                break;
            }
        }

        if (hasConveyorObjects)
        {
            itemIds.Add(ConveyorStackStateSentinel);
            itemIds.Add(conveyorLaneCount);
            for (int i = 0; i < conveyorLaneCount; i++)
            {
                itemIds.Add(GetConveyorItemIdAtLane(i));
            }
        }

        return itemIds;
    }

    public List<int> CaptureFloorObjectStateWithDroppedConveyorObjects()
    {
        List<int> itemIds = CaptureFloorObjectState();
        if (itemIds == null || itemIds.Count <= 0)
        {
            return itemIds;
        }

        List<int> droppedState = new List<int>(itemIds.Count);
        for (int i = 0; i < itemIds.Count; i++)
        {
            int itemId = itemIds[i];
            if (itemId != ConveyorStackStateSentinel)
            {
                droppedState.Add(itemId);
                continue;
            }

            if (i + 1 >= itemIds.Count)
            {
                break;
            }

            int laneCount = Mathf.Max(0, itemIds[++i]);
            for (int laneIndex = 0; laneIndex < laneCount && i + 1 < itemIds.Count; laneIndex++)
            {
                int laneItemId = itemIds[++i];
                if (laneItemId >= 0)
                {
                    droppedState.Add(laneItemId);
                }
            }
        }

        return droppedState;
    }

    public static bool IsVirtualizableFloorObjectState(IReadOnlyList<int> itemIds)
    {
        if (itemIds == null || itemIds.Count <= 0)
        {
            return false;
        }

        bool hasAnyFloorItem = false;
        for (int i = 0; i < itemIds.Count; i++)
        {
            int itemId = itemIds[i];
            if (itemId == InputAreaCenterStackStateSentinel
                || itemId == ConveyorStackStateSentinel)
            {
                return false;
            }

            if (itemId == FloorStackStateSentinel)
            {
                if (i + 1 >= itemIds.Count)
                {
                    return false;
                }

                int stackCount = Mathf.Max(0, itemIds[++i]);
                for (int stackIndex = 0; stackIndex < stackCount; stackIndex++)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        return false;
                    }

                    int stackItemCount = Mathf.Max(0, itemIds[++i]);
                    for (int objectIndex = 0; objectIndex < stackItemCount; objectIndex++)
                    {
                        if (i + 1 >= itemIds.Count || itemIds[i + 1] < 0)
                        {
                            return false;
                        }

                        i++;
                        hasAnyFloorItem = true;
                    }
                }

                continue;
            }

            if (itemId < 0)
            {
                return false;
            }

            hasAnyFloorItem = true;
        }

        return hasAnyFloorItem;
    }

    public bool HasVirtualizableFloorObjectState()
    {
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        bool hasFloorObjects = false;
        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject == null)
                {
                    continue;
                }

                if (portableObject.ItemId < 0)
                {
                    return false;
                }

                hasFloorObjects = true;
            }
        }

        for (int objectIndex = 0; objectIndex < inputAreaCenterStack.Count; objectIndex++)
        {
            if (inputAreaCenterStack[objectIndex] != null)
            {
                return false;
            }
        }

        int conveyorLaneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < conveyorLaneCount; laneIndex++)
        {
            if (HasConveyorItemAtLane(laneIndex))
            {
                return false;
            }
        }

        return hasFloorObjects;
    }

    public bool TryCaptureVirtualizableFloorObjectState(out List<int> itemIds)
    {
        itemIds = null;
        if (!HasVirtualizableFloorObjectState())
        {
            return false;
        }

        List<int> capturedState = CaptureFloorObjectState();
        if (!IsVirtualizableFloorObjectState(capturedState))
        {
            return false;
        }

        itemIds = capturedState;
        return true;
    }

    public void ClearLiveFloorObjectsForVirtualization()
    {
        ResetFloorObjects(false);
    }

    public void AppendVirtualConveyorItemRenderData(List<VirtualConveyorItemRenderData> results)
    {
        if (results == null || !ShouldUseVirtualConveyorItemRendering())
        {
            return;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return;
        }

        AppendVirtualConveyorItemRenderDataCore(results, false);
    }

    public void AppendDynamicVirtualConveyorItemRenderData(List<VirtualConveyorItemRenderData> results)
    {
        if (results == null || !Application.isPlaying)
        {
            return;
        }

        if (floorObjects == null || floorObjects.Count == 0)
        {
            EnsureFloorObjectsInitialized();
        }

        if (!IsConveyorStackingEnabled()
            || !HasConveyorMotionStates())
        {
            return;
        }

        AppendVirtualConveyorItemRenderDataCore(results, true);
    }

    private void AppendVirtualConveyorItemRenderDataCore(
        List<VirtualConveyorItemRenderData> results,
        bool useDynamicFastPath)
    {
        int laneCount = GetConveyorLaneCount();
        bool useIdentityRotation = useDynamicFastPath && !HasRuntimeBelt2FConveyor();
        GameManager gameManager = useDynamicFastPath ? GameManager.Instance : null;
        TerrainGenerator terrainGenerator = useDynamicFastPath ? TerrainGenerator.Active : null;
        bool showSleepAwake = gameManager != null && gameManager.ShowSleepAwake;
        bool showBeltItemLine = gameManager != null && gameManager.ShowBeltItemLine;

        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
            int itemId = GetConveyorItemIdAtLane(laneIndex);
            if (itemId < 0 || (portableObject != null && portableObject.IsMovingToTarget))
            {
                continue;
            }

            if (useDynamicFastPath)
            {
                ApplyConveyorObjectVirtualRenderingSuppressionIfNeeded(portableObject);
            }
            else
            {
                ApplyConveyorObjectRenderingMode(portableObject);
            }

            Vector3 position = GetConveyorItemVisualWorldPosition(laneIndex, portableObject);
            Quaternion rotation = useIdentityRotation
                ? Quaternion.identity
                : GetConveyorItemVisualWorldRotation(laneIndex, position);
            bool useSleepAwakeDarkTint = useDynamicFastPath
                ? showSleepAwake && IsConveyorItemSleepAwakeSleeping(laneIndex)
                : ShouldUseSleepAwakeDarkTint(laneIndex);
            Color32 beltItemLineDebugColor;
            bool useBeltItemLineDebugColor = useDynamicFastPath
                ? TryGetBeltItemLineDebugColorFast(
                    terrainGenerator,
                    showBeltItemLine,
                    laneIndex,
                    out beltItemLineDebugColor)
                : TryGetBeltItemLineDebugColor(laneIndex, out beltItemLineDebugColor);

            results.Add(new VirtualConveyorItemRenderData(
                itemId,
                Matrix4x4.TRS(position, rotation, Vector3.one),
                gameObject.layer,
                useSleepAwakeDarkTint,
                useBeltItemLineDebugColor,
                beltItemLineDebugColor));
        }
    }

    public void ApplyFloorObjectState(IReadOnlyList<int> itemIds)
    {
        EnsureFloorObjectsInitialized();
        ResetFloorObjects(false);

        if (itemIds == null)
        {
            RefreshConveyorActivityRegistration(false);
            return;
        }

        for (int i = 0; i < itemIds.Count; i++)
        {
            int itemId = itemIds[i];
            if (itemId == FloorStackStateSentinel)
            {
                if (i + 1 >= itemIds.Count)
                {
                    break;
                }

                int stackCount = Mathf.Max(0, itemIds[++i]);
                for (int stackIndex = 0; stackIndex < stackCount && i + 1 < itemIds.Count; stackIndex++)
                {
                    int stackItemCount = Mathf.Max(0, itemIds[++i]);
                    for (int objectIndex = 0; objectIndex < stackItemCount && i + 1 < itemIds.Count; objectIndex++)
                    {
                        int stackItemId = itemIds[++i];
                        if (stackItemId < 0)
                        {
                            continue;
                        }

                        TryAddFloorObjectToStackFromState(stackItemId, stackIndex, out _);
                    }
                }

                continue;
            }

            if (itemId == InputAreaCenterStackStateSentinel)
            {
                if (i + 1 >= itemIds.Count)
                {
                    break;
                }

                int centerStackCount = Mathf.Max(0, itemIds[++i]);
                for (int centerIndex = 0; centerIndex < centerStackCount && i + 1 < itemIds.Count; centerIndex++)
                {
                    int centerItemId = itemIds[++i];
                    if (!TryAddInputAreaCenterObject(centerItemId, out _))
                    {
                        break;
                    }
                }

                continue;
            }

            if (itemId == ConveyorStackStateSentinel)
            {
                if (i + 1 >= itemIds.Count)
                {
                    break;
                }

                int laneCount = Mathf.Max(0, itemIds[++i]);
                for (int laneIndex = 0; laneIndex < laneCount && i + 1 < itemIds.Count; laneIndex++)
                {
                    int laneItemId = itemIds[++i];
                    if (laneItemId < 0)
                    {
                        continue;
                    }

                    TrySetConveyorObjectAtLaneOrSingleLineFallback(laneIndex, laneItemId, out _);
                }

                continue;
            }

            if (!TryAddFloorObjectFromState(itemId, out _))
            {
                break;
            }
        }

        WakeConveyorMoveAttemptsAround();
        RefreshConveyorActivityRegistration();
    }

    public void CaptureConveyorItemSaveStates(List<ConveyorItemLaneSaveState> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return;
        }

        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            int itemId = GetConveyorItemIdAtLane(laneIndex);
            if (itemId < 0)
            {
                continue;
            }

            ConveyorItemLaneSaveState state = new ConveyorItemLaneSaveState
            {
                laneIndex = laneIndex,
                itemId = itemId,
                visualWorldPosition = GetConveyorItemVisualWorldPosition(laneIndex)
            };

            PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
            if (portableObject != null && conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState cornerMotionState))
            {
                CopyCornerMotionToSaveState(cornerMotionState, state);
            }
            else if (portableObject != null && conveyorLinearMotionStates.TryGetValue(portableObject, out ConveyorLinearMotionState linearMotionState))
            {
                CopyLinearMotionToSaveState(linearMotionState, state);
            }
            else if (laneIndex < conveyorItemMotionStates.Count && conveyorItemMotionStates[laneIndex].active)
            {
                CopyDataMotionToSaveState(conveyorItemMotionStates[laneIndex], state);
            }

            results.Add(state);
        }
    }

    public int ApplyConveyorItemSaveStates(IReadOnlyList<ConveyorItemLaneSaveState> states)
    {
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return -1;
        }

        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            PortableObject existingPortableObject = GetConveyorPortableObjectAtLane(laneIndex);
            ClearConveyorItemAtLane(laneIndex);
            ReleaseFloorObject(existingPortableObject);
        }

        int appliedCount = 0;
        if (states != null)
        {
            for (int i = 0; i < states.Count; i++)
            {
                ConveyorItemLaneSaveState state = states[i];
                if (state == null
                    || state.itemId < 0
                    || state.laneIndex < 0)
                {
                    continue;
                }

                int targetLaneIndex = state.laneIndex;
                if (!IsValidConveyorLaneIndex(targetLaneIndex)
                    && (!TryNormalizeConveyorLaneIndex(targetLaneIndex, out targetLaneIndex)
                        || !IsValidConveyorLaneIndex(targetLaneIndex)))
                {
                    continue;
                }

                if (!TrySetConveyorObjectAtLane(targetLaneIndex, state.itemId, out PortableObject portableObject))
                {
                    int fallbackLaneIndex = targetLaneIndex == ConveyorSingleLineFrontLaneIndex
                        ? ConveyorSingleLineBackLaneIndex
                        : ConveyorSingleLineFrontLaneIndex;
                    if (!TrySetConveyorObjectAtLane(fallbackLaneIndex, state.itemId, out portableObject))
                    {
                        continue;
                    }

                    targetLaneIndex = fallbackLaneIndex;
                }

                if (targetLaneIndex != state.laneIndex)
                {
                    SetConveyorPortableObjectWorldPose(
                        portableObject,
                        targetLaneIndex,
                        GetConveyorLaneWorldPosition(targetLaneIndex));
                    appliedCount++;
                    continue;
                }

                ApplyConveyorItemLaneSaveState(state, portableObject);
                appliedCount++;
            }
        }

        MarkConveyorItemVisualDirty();
        WakeConveyorMoveAttemptsAround();
        RefreshConveyorActivityRegistration();
        return appliedCount;
    }

    private static void CopyCornerMotionToSaveState(
        ConveyorCornerMotionState motionState,
        ConveyorItemLaneSaveState state)
    {
        state.hasMotion = true;
        state.useCornerMotion = true;
        state.sourceLaneIndex = motionState.sourceLaneIndex;
        state.destinationLaneIndex = motionState.destinationLaneIndex;
        state.startWorldPosition = motionState.startWorldPosition;
        state.progress = Mathf.Clamp01(motionState.progress);
        state.pathLength = motionState.pathLength;
        state.durationPathLength = motionState.durationPathLength;
    }

    private void CopyLinearMotionToSaveState(
        ConveyorLinearMotionState motionState,
        ConveyorItemLaneSaveState state)
    {
        state.hasMotion = true;
        state.useCornerMotion = false;
        state.destinationLaneIndex = motionState.destinationLaneIndex;
        state.startWorldPosition = motionState.startWorldPosition;
        state.hasViaWorldPosition = motionState.hasViaWorldPosition;
        state.viaWorldPosition = motionState.viaWorldPosition;
        state.progress = Mathf.Clamp01(motionState.progress);
        state.pathLength = motionState.pathLength;
        CopyCornerContinuationToSaveState(motionState.cornerContinuation, state);
    }

    private void CopyDataMotionToSaveState(
        ConveyorDataMotionState motionState,
        ConveyorItemLaneSaveState state)
    {
        state.hasMotion = true;
        state.useCornerMotion = motionState.useCornerMotion;
        state.sourceLaneIndex = motionState.sourceLaneIndex;
        state.destinationLaneIndex = motionState.destinationLaneIndex;
        state.startWorldPosition = motionState.startWorldPosition;
        state.hasViaWorldPosition = motionState.hasViaWorldPosition;
        state.viaWorldPosition = motionState.viaWorldPosition;
        state.progress = EvaluateConveyorDataMotionProgress(motionState);
        state.pathLength = motionState.pathLength;
        state.durationPathLength = motionState.durationPathLength;
        CopyCornerContinuationToSaveState(motionState.cornerContinuation, state);
    }

    private static void CopyCornerContinuationToSaveState(
        ConveyorCornerContinuation continuation,
        ConveyorItemLaneSaveState state)
    {
        if (!continuation.active || continuation.block == null)
        {
            return;
        }

        state.cornerContinuationActive = true;
        state.cornerContinuationBlockCoordinate = continuation.block.Coordinate;
        state.cornerContinuationSourceLaneIndex = continuation.sourceLaneIndex;
        state.cornerContinuationDestinationLaneIndex = continuation.destinationLaneIndex;
        state.cornerContinuationStartWorldPosition = continuation.startWorldPosition;
        state.cornerContinuationStartProgress = Mathf.Clamp01(continuation.startProgress);
        state.cornerContinuationPathLength = continuation.pathLength;
        state.cornerContinuationDurationPathLength = continuation.durationPathLength;
    }

    private void ApplyConveyorItemLaneSaveState(
        ConveyorItemLaneSaveState state,
        PortableObject portableObject)
    {
        if (state == null)
        {
            return;
        }

        if (!state.hasMotion)
        {
            if (portableObject != null)
            {
                Vector3 settledWorldPosition = IsValidConveyorLaneIndex(state.laneIndex)
                    ? GetConveyorLaneWorldPosition(state.laneIndex)
                    : state.visualWorldPosition;
                SetConveyorPortableObjectWorldPose(portableObject, state.laneIndex, settledWorldPosition);
            }

            return;
        }

        if (state.useCornerMotion)
        {
            ConveyorCornerMotionState motionState = new ConveyorCornerMotionState
            {
                sourceLaneIndex = state.sourceLaneIndex,
                destinationLaneIndex = state.destinationLaneIndex,
                startWorldPosition = state.startWorldPosition,
                progress = Mathf.Clamp01(state.progress),
                pathLength = state.pathLength,
                durationPathLength = state.durationPathLength
            };

            if (portableObject != null)
            {
                conveyorCornerMotionStates[portableObject] = motionState;
                conveyorLinearMotionStates.Remove(portableObject);
                SetConveyorPortableObjectWorldPose(portableObject, state.laneIndex, state.visualWorldPosition);
            }
            else if (state.laneIndex >= 0 && state.laneIndex < conveyorItemMotionStates.Count)
            {
                ConveyorDataMotionState dataMotionState = new ConveyorDataMotionState
                {
                    active = true,
                    useCornerMotion = true,
                    sourceLaneIndex = state.sourceLaneIndex,
                    destinationLaneIndex = state.destinationLaneIndex,
                    startWorldPosition = state.startWorldPosition,
                    progress = Mathf.Clamp01(state.progress),
                    pathLength = state.pathLength,
                    durationPathLength = state.durationPathLength
                };
                conveyorItemMotionStates[state.laneIndex] = InitializeConveyorDataMotionTiming(
                    dataMotionState,
                    state.progress);
            }

            return;
        }

        ConveyorCornerContinuation continuation = BuildCornerContinuationFromSaveState(state);
        if (portableObject != null)
        {
            conveyorCornerMotionStates.Remove(portableObject);
            conveyorLinearMotionStates[portableObject] = new ConveyorLinearMotionState
            {
                cornerContinuation = continuation,
                startWorldPosition = state.startWorldPosition,
                hasViaWorldPosition = state.hasViaWorldPosition,
                viaWorldPosition = state.viaWorldPosition,
                destinationLaneIndex = state.destinationLaneIndex,
                progress = Mathf.Clamp01(state.progress),
                pathLength = state.pathLength
            };
            SetConveyorPortableObjectWorldPose(portableObject, state.laneIndex, state.visualWorldPosition);
        }
        else if (state.laneIndex >= 0 && state.laneIndex < conveyorItemMotionStates.Count)
        {
            ConveyorDataMotionState dataMotionState = new ConveyorDataMotionState
            {
                active = true,
                useCornerMotion = false,
                cornerContinuation = continuation,
                startWorldPosition = state.startWorldPosition,
                hasViaWorldPosition = state.hasViaWorldPosition,
                viaWorldPosition = state.viaWorldPosition,
                destinationLaneIndex = state.destinationLaneIndex,
                progress = Mathf.Clamp01(state.progress),
                pathLength = state.pathLength
            };
            conveyorItemMotionStates[state.laneIndex] = InitializeConveyorDataMotionTiming(
                dataMotionState,
                state.progress);
        }
    }

    private ConveyorCornerContinuation BuildCornerContinuationFromSaveState(ConveyorItemLaneSaveState state)
    {
        ConveyorCornerContinuation continuation = default;
        if (state == null || !state.cornerContinuationActive)
        {
            return continuation;
        }

        Block continuationBlock = null;
        if (state.cornerContinuationBlockCoordinate == coordinate)
        {
            continuationBlock = this;
        }
        else if (TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator)
                 && terrainGenerator.TryGetLoadedBlock(state.cornerContinuationBlockCoordinate, out Block loadedBlock))
        {
            continuationBlock = loadedBlock;
        }

        if (continuationBlock == null)
        {
            return continuation;
        }

        continuation.active = true;
        continuation.block = continuationBlock;
        continuation.sourceLaneIndex = state.cornerContinuationSourceLaneIndex;
        continuation.destinationLaneIndex = state.cornerContinuationDestinationLaneIndex;
        continuation.startWorldPosition = state.cornerContinuationStartWorldPosition;
        continuation.startProgress = Mathf.Clamp01(state.cornerContinuationStartProgress);
        continuation.pathLength = state.cornerContinuationPathLength;
        continuation.durationPathLength = state.cornerContinuationDurationPathLength;
        return continuation;
    }

    public void SetFocusVisible(bool isVisible)
    {
        if (this == null)
        {
            return;
        }

        interactionFocusVisible = isVisible;
        interactionFocusUsesArea = false;
        interactionFocusAreaSize = Vector2.one;
        RefreshFocusMarker();
    }

    public void SetFocusVisible(bool isVisible, Vector3 worldCenter, Vector2 worldSize)
    {
        if (this == null)
        {
            return;
        }

        interactionFocusVisible = isVisible;
        interactionFocusUsesArea = isVisible;
        interactionFocusAreaCenter = worldCenter;
        interactionFocusAreaSize = new Vector2(
            Mathf.Max(0.01f, worldSize.x),
            Mathf.Max(0.01f, worldSize.y));
        RefreshFocusMarker();
    }

    public void SetMouseFocusVisible(bool isVisible)
    {
        if (this == null)
        {
            return;
        }

        mouseFocusVisible = isVisible;
        mouseFocusUsesArea = false;
        mouseFocusAreaSize = Vector2.one;
        RefreshFocusMarker();
    }

    public void SetMouseFocusVisible(bool isVisible, Vector3 worldCenter, Vector2 worldSize)
    {
        if (this == null)
        {
            return;
        }

        mouseFocusVisible = isVisible;
        mouseFocusUsesArea = isVisible;
        mouseFocusAreaCenter = worldCenter;
        mouseFocusAreaSize = new Vector2(
            Mathf.Max(0.01f, worldSize.x),
            Mathf.Max(0.01f, worldSize.y));
        RefreshFocusMarker();
    }

    private void RefreshFocusMarker()
    {
        if (focus == null)
        {
            focus = GetComponentInChildren<MapFocus>(true);
        }

        if (focus == null)
        {
            return;
        }

        bool isVisible = interactionFocusVisible || mouseFocusVisible;
        Color focusColor = mouseFocusVisible ? MapFocus.MouseFocusColor : MapFocus.DefaultFocusColor;
        if (mouseFocusVisible && mouseFocusUsesArea)
        {
            focus.SetAreaVisible(true, focusColor, mouseFocusAreaCenter, mouseFocusAreaSize);
        }
        else if (interactionFocusVisible && interactionFocusUsesArea)
        {
            focus.SetAreaVisible(true, focusColor, interactionFocusAreaCenter, interactionFocusAreaSize);
        }
        else
        {
            focus.SetVisible(isVisible, focusColor);
        }
    }

    public Vector2Int Coordinate => coordinate;
    public BlockType Type => type;
    public MapObject MapObject => mapObject;
    public Resource Resource
    {
        get
        {
            if (mapObject is Resource resource)
            {
                return resource;
            }

            return GetComponentInChildren<Resource>(true);
        }
    }
    public Transform Body => body;

    public bool IsRuntimeConveyor => IsConveyorStackingEnabled();
    public float RuntimeConveyorSpeed => IsConveyorStackingEnabled() ? GetConveyorSpeed() : 0f;

    public int ConveyorItemVisualVersion => conveyorItemVisualVersion;

    public int GetRuntimeConveyorItemCount()
    {
        if (!IsConveyorStackingEnabled())
        {
            return 0;
        }

        EnsureFloorObjectsInitialized();
        int count = 0;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (HasConveyorItemAtLane(laneIndex))
            {
                count++;
            }
        }

        return count;
    }

    public int GetRuntimeConveyorLaneCount()
    {
        if (!IsConveyorStackingEnabled())
        {
            return 0;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        return GetConveyorLaneCount();
    }

    public bool HasRuntimeConveyorItemAtLane(int laneIndex)
    {
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        return HasConveyorItemAtLane(laneIndex);
    }

    public bool TryGetRuntimeConveyorItemIdAtLane(int laneIndex, out int itemId)
    {
        itemId = -1;
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        itemId = GetConveyorItemIdAtLane(laneIndex);
        return itemId >= 0;
    }

    public bool TryGetRuntimeConveyorItemSlotIdAtLane(int laneIndex, out int itemId)
    {
        itemId = -1;
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsValidConveyorLaneIndex(laneIndex))
        {
            return false;
        }

        itemId = GetConveyorItemIdAtLane(laneIndex);
        return true;
    }

    public bool TryGetRuntimeConveyorSuccessorLane(
        int sourceLaneIndex,
        out Block destinationBlock,
        out int destinationLaneIndex)
    {
        destinationBlock = null;
        destinationLaneIndex = -1;
        return IsConveyorStackingEnabled()
            && TryGetConveyorSuccessor(sourceLaneIndex, out destinationBlock, out destinationLaneIndex, out _);
    }

    public bool TryGetRuntimeConveyorLaneLink(
        int sourceLaneIndex,
        out Vector2Int destinationCoordinate,
        out int destinationLaneIndex,
        out float pathLength)
    {
        destinationCoordinate = default;
        destinationLaneIndex = -1;
        pathLength = 0f;

        if (!IsConveyorStackingEnabled()
            || !TryGetConveyorSuccessor(
                sourceLaneIndex,
                out Block destinationBlock,
                out destinationLaneIndex,
                out bool useCornerMotion)
            || destinationBlock == null)
        {
            return false;
        }

        destinationCoordinate = destinationBlock.Coordinate;
        pathLength = GetConveyorPathSegmentLength(
            sourceLaneIndex,
            destinationBlock,
            destinationLaneIndex,
            useCornerMotion);
        return destinationLaneIndex >= 0 && pathLength > ConveyorContinuousMotionEpsilon;
    }

    public bool TryGetRuntimeNextConveyorBlock(out Block nextBlock)
    {
        return TryGetNextConveyorBlock(out nextBlock);
    }

    public bool TryGetRuntimePreviousConveyorBlock(out Block previousBlock)
    {
        return TryGetPreviousConveyorBlock(out previousBlock);
    }

    public bool TryGetRuntimeConveyorFlowDirection(out Vector2Int flowDirection)
    {
        flowDirection = Vector2Int.zero;
        return IsConveyorStackingEnabled()
            && TryGetConveyorFlowDirection(out flowDirection)
            && flowDirection != Vector2Int.zero;
    }

    public bool CanUseStraightConveyorLineSimulation()
    {
        return CanUseStraightConveyorLineSimulationStructureOnly()
            && !HasConveyorMotionStates()
            && !HasUnsettledConveyorObjects();
    }

    public bool CanUseStraightConveyorLineSimulationStateOnly()
    {
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        return Application.isPlaying
            && IsConveyorStackingEnabled()
            && !IsCornerConveyor()
            && ShouldUseVirtualConveyorItemRendering()
            && !HasMaterializedConveyorItems()
            && !HasConveyorMotionStates()
            && !HasUnsettledConveyorObjects();
    }

    public bool HasStraightConveyorLineFastPathRuntimeBlocker()
    {
        return HasMaterializedConveyorItems() || HasPortableConveyorMotionStates();
    }

    public bool CanUseStraightConveyorLineSimulationStructureOnly()
    {
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        return Application.isPlaying
            && IsConveyorStackingEnabled()
            && !TryGetRuntimeBelt2F(out _)
            && !TryGetBelt2FBridgeCenterBelt(out _)
            && !IsCornerConveyor()
            && ShouldUseVirtualConveyorItemRendering()
            && TryGetConveyorLaneLayout(out _, out _);
    }

    public bool TryGetStraightConveyorLineLaneIndices(
        out int frontLaneIndex,
        out int backLaneIndex)
    {
        frontLaneIndex = -1;
        backLaneIndex = -1;
        return IsConveyorStackingEnabled()
            && !IsCornerConveyor()
            && TryGetConveyorLaneLayout(
                out frontLaneIndex,
                out backLaneIndex);
    }

    public bool HasStraightConveyorDataItemAtLane(int laneIndex)
    {
        return IsValidConveyorLaneIndex(laneIndex)
            && HasConveyorItemAtLane(laneIndex)
            && GetConveyorPortableObjectAtLane(laneIndex) == null
            && GetConveyorItemIdAtLane(laneIndex) >= 0;
    }

    public bool TryMoveStraightConveyorDataLaneTo(Block destinationBlock, int sourceLaneIndex, int destinationLaneIndex)
    {
        if (destinationBlock == null
            || !CanUseStraightConveyorLineSimulationStructureOnly()
            || !destinationBlock.CanUseStraightConveyorLineSimulationStructureOnly()
            || !IsValidConveyorLaneIndex(sourceLaneIndex)
            || !destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            || destinationBlock.HasConveyorItemAtLane(destinationLaneIndex)
            || !HasConveyorItemAtLane(sourceLaneIndex)
            || GetConveyorPortableObjectAtLane(sourceLaneIndex) != null
            || WasConveyorItemMovedThisFrame(sourceLaneIndex)
            || !IsConveyorItemReadyToMoveAtLane(sourceLaneIndex))
        {
            return false;
        }

        int itemId = GetConveyorItemIdAtLane(sourceLaneIndex);
        if (itemId < 0)
        {
            return false;
        }

        float pathLength = GetConveyorPathSegmentLength(
            sourceLaneIndex,
            destinationBlock,
            destinationLaneIndex,
            false);

        return TryMoveStraightConveyorDataLaneToCached(
            destinationBlock,
            sourceLaneIndex,
            destinationLaneIndex,
            pathLength);
    }

    public bool TryMoveStraightConveyorDataLaneToCached(
        Block destinationBlock,
        int sourceLaneIndex,
        int destinationLaneIndex,
        float pathLength)
    {
        bool moved = TryMoveStraightConveyorDataLaneToCachedCore(
            destinationBlock,
            sourceLaneIndex,
            destinationLaneIndex,
            pathLength);
        MapObjectTickProfiler.AddBeltStraightMoveAttempt(moved);
        return moved;
    }

    private bool TryMoveStraightConveyorDataLaneToCachedCore(
        Block destinationBlock,
        int sourceLaneIndex,
        int destinationLaneIndex,
        float pathLength)
    {
        if (destinationBlock == null
            || !IsValidConveyorLaneIndex(sourceLaneIndex)
            || !destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            || destinationBlock.HasConveyorItemAtLane(destinationLaneIndex)
            || !HasConveyorItemAtLane(sourceLaneIndex)
            || GetConveyorPortableObjectAtLane(sourceLaneIndex) != null
            || WasConveyorItemMovedThisFrame(sourceLaneIndex)
            || !IsConveyorItemReadyToMoveAtLane(sourceLaneIndex))
        {
            return false;
        }

        int itemId = GetConveyorItemIdAtLane(sourceLaneIndex);
        if (itemId < 0)
        {
            return false;
        }

        ConveyorPickupGateState pickupGateState = GetConveyorPickupGateStateAtLane(sourceLaneIndex);
        pickupGateState.MarkSettled();
        Vector3 startWorldPosition = GetConveyorItemVisualWorldPosition(sourceLaneIndex);
        bool hasViaWorldPosition = TryGetConveyorLinearMoveViaWorldPosition(
            sourceLaneIndex,
            startWorldPosition,
            out Vector3 viaWorldPosition);
        if (hasViaWorldPosition)
        {
            Vector3 destinationWorldPosition = destinationBlock.GetConveyorLaneWorldPosition(destinationLaneIndex);
            float viaPathLength =
                Vector3.Distance(startWorldPosition, viaWorldPosition)
                + Vector3.Distance(viaWorldPosition, destinationWorldPosition);
            if (viaPathLength > ConveyorContinuousMotionEpsilon)
            {
                pathLength = viaPathLength;
            }
        }

        ClearConveyorItemAtLane(sourceLaneIndex);
        destinationBlock.SetConveyorItemAtLane(destinationLaneIndex, itemId, null, pickupGateState);
        ConveyorDataMotionState dataMotionState = new ConveyorDataMotionState
        {
            active = true,
            useCornerMotion = false,
            startWorldPosition = startWorldPosition,
            hasViaWorldPosition = hasViaWorldPosition,
            viaWorldPosition = viaWorldPosition,
            destinationLaneIndex = destinationLaneIndex,
            progress = 0f,
            pathLength = pathLength
        };
        destinationBlock.conveyorItemMotionStates[destinationLaneIndex] =
            destinationBlock.InitializeConveyorDataMotionTiming(dataMotionState, 0f);
        destinationBlock.MarkConveyorItemVisualDirty();
        destinationBlock.MarkConveyorItemMovedThisFrame(destinationLaneIndex);
        return true;
    }

    public bool CanMoveStraightConveyorDataLaneToCached(
        Block destinationBlock,
        int sourceLaneIndex,
        int destinationLaneIndex)
    {
        return destinationBlock != null
            && IsValidConveyorLaneIndex(sourceLaneIndex)
            && destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            && !destinationBlock.HasConveyorItemAtLane(destinationLaneIndex)
            && HasStraightConveyorDataItemAtLane(sourceLaneIndex)
            && !WasConveyorItemMovedThisFrame(sourceLaneIndex)
            && IsConveyorItemReadyToMoveAtLane(sourceLaneIndex);
    }

    public bool TryGetStraightConveyorLineMotionData(
        Block nextLineBlock,
        out int frontLaneIndex,
        out int backLaneIndex,
        out float withinPathLength,
        out float nextPathLength)
    {
        frontLaneIndex = -1;
        backLaneIndex = -1;
        withinPathLength = 0f;
        nextPathLength = 0f;

        if (!CanUseStraightConveyorLineSimulationStructureOnly()
            || !TryGetStraightConveyorLineLaneIndices(
                out frontLaneIndex,
                out backLaneIndex))
        {
            return false;
        }

        withinPathLength = GetConveyorPathSegmentLength(
            backLaneIndex,
            this,
            frontLaneIndex,
            false);

        if (nextLineBlock == null)
        {
            return true;
        }

        if (!nextLineBlock.TryGetStraightConveyorLineLaneIndices(
                out _,
                out int nextBackLaneIndex))
        {
            return false;
        }

        nextPathLength = GetConveyorPathSegmentLength(
            frontLaneIndex,
            nextLineBlock,
            nextBackLaneIndex,
            false);
        return true;
    }

    public bool TryAdvanceStraightConveyorLineLane(int sourceLaneIndex, bool ignoreMoveAttemptThrottle = false)
    {
        if (!IsConveyorStackingEnabled() || IsCornerConveyor())
        {
            return false;
        }

        return TryMoveConveyorLane(
            sourceLaneIndex,
            out _,
            out _,
            ignoreMoveAttemptThrottle);
    }

    public bool HasActiveVirtualConveyorDataMotion()
    {
        return Application.isPlaying
            && IsConveyorStackingEnabled()
            && ShouldUseVirtualConveyorItemRendering()
            && HasConveyorDataMotionStates();
    }

    public bool HasDynamicVirtualConveyorItemVisuals()
    {
        return Application.isPlaying
            && IsConveyorStackingEnabled()
            && ShouldUseVirtualConveyorItemRendering()
            && HasConveyorMotionStates();
    }

    public bool TickVirtualConveyorDataMotion(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return false;
        }

        return CompleteDueVirtualConveyorDataMotions(Time.time);
    }

    public bool CompleteDueVirtualConveyorDataMotions(float now)
    {
        if (!HasActiveVirtualConveyorDataMotion())
        {
            return false;
        }

        bool advancedAny = false;
        float conveyorSpeed = GetConveyorSpeed();
        int laneCount = Mathf.Min(GetConveyorLaneCount(), conveyorItemMotionStates.Count);
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!IsValidConveyorLaneIndex(laneIndex))
            {
                continue;
            }

            ConveyorDataMotionState motionState = conveyorItemMotionStates[laneIndex];
            if (!motionState.active)
            {
                continue;
            }

            ConveyorDataMotionState timedMotionState = EnsureConveyorDataMotionTiming(motionState);
            if (timedMotionState.startTime != motionState.startTime || timedMotionState.duration != motionState.duration)
            {
                conveyorItemMotionStates[laneIndex] = timedMotionState;
                motionState = timedMotionState;
            }

            float completionTime = GetConveyorDataMotionCompletionTime(motionState, now);
            if (completionTime > now + ConveyorContinuousMotionEpsilon)
            {
                continue;
            }

            advancedAny |= TryCompleteDueVirtualConveyorLaneMotion(
                laneIndex,
                conveyorSpeed,
                now,
                completionTime,
                ConveyorContinuousMotionMaxCarrySteps);
        }

        if (!HasConveyorDataMotionStates())
        {
            WakeConveyorMoveAttemptsAround();
        }
        else if (HasConveyorReadyLaneForMove(false, false) || HasMovableConveyorLaneSleepState())
        {
            WakeConveyorMoveAttemptsAround();
        }

        return advancedAny;
    }

    public void TickConveyor(float deltaTime)
    {
        if (!Application.isPlaying || deltaTime <= 0f)
        {
            return;
        }

        UpdateConveyorObjects(deltaTime);
    }

    public void NotifyStraightConveyorLineTickCompleted()
    {
        if (!Application.isPlaying || !IsConveyorStackingEnabled())
        {
            return;
        }

        if (!HasAnyConveyorObjects())
        {
            RefreshConveyorActivityRegistration(false);
            return;
        }

        SleepConveyorMoveAttempts();
        RefreshConveyorActivityRegistration(false);
    }

    public bool ShouldTickActiveConveyor()
    {
        if (!Application.isPlaying || !IsConveyorStackingEnabled())
        {
            return false;
        }

        if (HasPortableConveyorMotionStates())
        {
            return true;
        }

        if (HasActiveVirtualConveyorDataMotion())
        {
            return HasConveyorReadyLaneForMove(true, false);
        }

        if (HasConveyorDataMotionStates())
        {
            return true;
        }

        return HasConveyorReadyLaneForMove(true)
            || HasMovableConveyorLaneSleepState();
    }

    public bool HasConveyorWorkIgnoringNetworkThrottle()
    {
        if (!Application.isPlaying || !IsConveyorStackingEnabled())
        {
            return false;
        }

        if (HasPortableConveyorMotionStates()
            || HasActiveVirtualConveyorDataMotion()
            || HasConveyorDataMotionStates())
        {
            return true;
        }

        if (!HasAnyConveyorObjectsNotMoveAttemptSleeping())
        {
            return HasMovableConveyorLaneSleepState();
        }

        return HasConveyorReadyLaneForMove(false);
    }

    public bool HasStraightConveyorLineRetryWork()
    {
        if (!Application.isPlaying || !IsConveyorStackingEnabled())
        {
            return false;
        }

        CleanupConveyorStack();
        return HasConveyorMotionStates()
            || HasAnyConveyorObjectsNotMoveAttemptSleeping()
            || HasMovableConveyorLaneSleepState();
    }

    private bool HasConveyorReadyLaneForMove(bool respectNetworkThrottle, bool respectLaneThrottle = true)
    {
        if (respectNetworkThrottle && IsConveyorNetworkMoveAttemptThrottled())
        {
            return false;
        }

        if (!HasAnyConveyorObjectsNotMoveAttemptSleeping()
            || ShouldThrottleBlockedConveyorMoveAttempts())
        {
            return false;
        }

        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (HasConveyorItemAtLane(laneIndex)
                && !IsConveyorLaneBlockedSleep(laneIndex)
                && !IsConveyorLaneCycleBlockedSleep(laneIndex)
                && IsConveyorItemReadyToMoveAtLane(laneIndex)
                && (!respectLaneThrottle || !IsConveyorLaneMoveAttemptThrottled(laneIndex)))
            {
                return true;
            }
        }

        return false;
    }

    public void RefreshConveyorActivityRegistration(bool queueWake = true, bool refreshDebugVisuals = true)
    {
        TerrainGenerator generator = TerrainGenerator.Active;
        if (generator == null)
        {
            return;
        }

        if (generator.IsConveyorRuntimeRefreshDeferred)
        {
            generator.QueueDeferredConveyorRuntimeRefresh(this);
            return;
        }

        MapObjectTickProfiler.AddBeltActivityRefreshCall();

        generator.SetConveyorDataMotionActive(this, HasActiveVirtualConveyorDataMotion());
        generator.SetConveyorActive(this, HasActiveConveyorMotion(), queueWake);
        generator.SetConveyorItemVisualActive(this, IsConveyorStackingEnabled() && HasAnyConveyorObjects());
        if (refreshDebugVisuals)
        {
            RefreshSleepAwakeDebugVisuals();
            generator.QueueBeltItemLineDebugVisualRefresh(this);
            RefreshBeltDirectionDebugVisuals();
        }
    }

    public void RefreshSleepAwakeDebugVisuals(bool forceVirtualRenderRefresh = false)
    {
        if (!Application.isPlaying || !IsConveyorStackingEnabled())
        {
            return;
        }

        bool visualStateChanged = false;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            bool isSleeping = IsConveyorItemSleepAwakeSleeping(laneIndex);
            GetConveyorPortableObjectAtLane(laneIndex)?.SetSleepAwakeSleeping(isSleeping);

            bool useDarkTint = ShouldUseSleepAwakeDarkTint(laneIndex);
            if (laneIndex < conveyorLaneSleepAwakeDarkTintStates.Length
                && conveyorLaneSleepAwakeDarkTintStates[laneIndex] != useDarkTint)
            {
                conveyorLaneSleepAwakeDarkTintStates[laneIndex] = useDarkTint;
                visualStateChanged = true;
            }
        }

        for (int laneIndex = laneCount; laneIndex < conveyorLaneSleepAwakeDarkTintStates.Length; laneIndex++)
        {
            if (!conveyorLaneSleepAwakeDarkTintStates[laneIndex])
            {
                continue;
            }

            conveyorLaneSleepAwakeDarkTintStates[laneIndex] = false;
            visualStateChanged = true;
        }

        if (forceVirtualRenderRefresh || visualStateChanged)
        {
            MarkConveyorItemVisualDirty();
        }
    }

    public void RefreshBeltItemLineDebugVisuals(bool forceVirtualRenderRefresh = false)
    {
        if (!Application.isPlaying || !IsConveyorStackingEnabled())
        {
            return;
        }

        bool visualStateChanged = false;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            bool useLineDebugColor = TryGetBeltItemLineDebugColor(laneIndex, out Color32 lineDebugColor);
            GetConveyorPortableObjectAtLane(laneIndex)?.SetBeltItemLineDebugColor(useLineDebugColor, lineDebugColor);

            if (laneIndex >= conveyorLaneBeltItemLineDebugStates.Length)
            {
                continue;
            }

            bool stateChanged = conveyorLaneBeltItemLineDebugStates[laneIndex] != useLineDebugColor;
            bool colorChanged = useLineDebugColor
                && !conveyorLaneBeltItemLineDebugColors[laneIndex].Equals(lineDebugColor);
            if (!stateChanged && !colorChanged)
            {
                continue;
            }

            conveyorLaneBeltItemLineDebugStates[laneIndex] = useLineDebugColor;
            conveyorLaneBeltItemLineDebugColors[laneIndex] = useLineDebugColor ? lineDebugColor : (Color32)Color.white;
            visualStateChanged = true;
        }

        for (int laneIndex = laneCount; laneIndex < conveyorLaneBeltItemLineDebugStates.Length; laneIndex++)
        {
            if (!conveyorLaneBeltItemLineDebugStates[laneIndex])
            {
                continue;
            }

            conveyorLaneBeltItemLineDebugStates[laneIndex] = false;
            conveyorLaneBeltItemLineDebugColors[laneIndex] = Color.white;
            visualStateChanged = true;
        }

        if (forceVirtualRenderRefresh || visualStateChanged)
        {
            MarkConveyorItemVisualDirty();
        }
    }

    private void WakeConveyorMoveAttempts(bool clearBlockedSleepImmediately = false)
    {
        bool hadSleepingLanes = clearBlockedSleepImmediately
            ? ClearConveyorLaneSleepStates()
            : ClearMovableConveyorLaneSleepStates();
        TerrainGenerator.Active?.WakeConveyorNetwork(this);
        bool clearedMoveAttemptThrottle = nextConveyorMoveAttemptTime > 0f;
        nextConveyorMoveAttemptTime = 0f;
        for (int i = 0; i < nextConveyorLaneMoveAttemptTimes.Length; i++)
        {
            clearedMoveAttemptThrottle |= nextConveyorLaneMoveAttemptTimes[i] > 0f;
            nextConveyorLaneMoveAttemptTimes[i] = 0f;
        }

        if (clearedMoveAttemptThrottle)
        {
            InvalidateConveyorCanMoveCaches(false);
        }

        RefreshSleepAwakeDebugVisuals();
        if (hadSleepingLanes)
        {
            RefreshConveyorActivityRegistration();
        }
    }

    public void WakeConveyorMoveAttemptsAround()
    {
        TerrainGenerator activeTerrain = TerrainGenerator.Active;
        if (activeTerrain != null && activeTerrain.IsConveyorRuntimeRefreshDeferred)
        {
            activeTerrain.QueueDeferredConveyorMoveAttemptWakeAround(this);
            return;
        }

        WakeConveyorMoveAttemptsAroundImmediate();
    }

    public void WakeConveyorMoveAttemptsAroundImmediate()
    {
        MapObjectTickProfiler.AddBeltWakeAroundCall();

        WakeConveyorMoveAttempts(true);
        if (!TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator))
        {
            return;
        }

        for (int i = 0; i < ConveyorNeighborDirections.Length; i++)
        {
            if (terrainGenerator.TryGetLoadedBlock(coordinate + ConveyorNeighborDirections[i], out Block neighborBlock)
                && neighborBlock != null)
            {
                neighborBlock.WakeConveyorMoveAttempts(true);
            }
        }
    }

    private void NotifyConveyorMotionSettled()
    {
        WakeConveyorMoveAttemptsAround();
        RefreshConveyorActivityRegistration();
    }

    private void NotifyConveyorLaneVacatedExternally(int laneIndex)
    {
        if (!IsConveyorStorageLaneIndex(laneIndex))
        {
            return;
        }

        InvalidateConveyorCanMoveCaches();
        TerrainGenerator.Active?.QueueConveyorDirectWakeAround(this);
        WakeConveyorMoveAttemptsAround();
        RefreshConveyorActivityRegistration();
    }

    private void SleepConveyorMoveAttempts()
    {
        bool sleptAnyLane = false;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (IsConveyorLaneCycleBlockedSleep(laneIndex))
            {
                continue;
            }

            sleptAnyLane |= SleepConveyorLaneBlocked(laneIndex);
        }

        nextConveyorMoveAttemptTime = 0f;
        if (sleptAnyLane)
        {
            RefreshSleepAwakeDebugVisuals();
        }
    }

    public void TickConveyorSlotDots(float deltaTime)
    {
        if (!Application.isPlaying || deltaTime <= 0f)
        {
            return;
        }

        if (!IsConveyorStackingEnabled())
        {
            SetConveyorSlotDotsVisible(false);
            TerrainGenerator.Active?.SetConveyorDotVisualActive(this, false);
            return;
        }

        if (!ShouldShowConveyorSlotDots())
        {
            SetConveyorSlotDotsVisible(false);
            TerrainGenerator.Active?.SetConveyorDotVisualActive(this, false);
            return;
        }

        TerrainGenerator terrainGenerator = TerrainGenerator.Active;
        if (terrainGenerator == null)
        {
            return;
        }

        SetConveyorSlotDotsVisible(false);

        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!IsValidConveyorLaneIndex(laneIndex))
            {
                continue;
            }

            Vector3 worldPosition = EvaluateAnimatedConveyorSlotDotWorldPosition(laneIndex);
            worldPosition.y += ConveyorSlotDotVerticalOffset;
            terrainGenerator.AddConveyorSlotDotInstance(worldPosition);
        }
    }

    public bool TryGetInstalledItemAreaCapacity(out int capacity)
    {
        capacity = 0;

        if (!(mapObject is InstallationObject installationObject))
        {
            return false;
        }

        bool supportsCenterStack = installationObject is BoxObject
                                   || (installationObject.MapFilter & InstallationMapFilter.ItemArea) != 0;
        if (!supportsCenterStack)
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveInstalledItemAreaDefinition();
        if (installedDefinition == null)
        {
            return false;
        }

        capacity = installedDefinition.capacity > 0 ? installedDefinition.capacity : 10;
        return true;
    }

    public bool TryPickupOneFloorObjectToBag(Player player, Vector3 playerPosition, float pickupRadius)
    {
        return TryPickupOneFloorObjectToBag(player, playerPosition, pickupRadius, -1);
    }

    public bool TryPickupOneFloorObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex, int preferredItemId = -1)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();

        if ((floorObjects == null || floorObjects.Count == 0) && inputAreaCenterStack.Count == 0)
        {
            return false;
        }

        HashSet<int> skippedFloorStackIndexes = null;
        bool skipInputAreaCenter = false;
        while (TryFindBestManualPickupCandidate(
                   player,
                   playerPosition,
                   pickupRadius,
                   preferredItemId,
                   skippedFloorStackIndexes,
                   skipInputAreaCenter,
                   out bool useInputAreaCenter,
                   out int stackIndex,
                   out List<PortableObject> stack,
                   out PortableObject topObject,
                   out int itemId,
                   out _))
        {
            if (!TryAddPickupObjectToBagOrMatchingHand(player, itemId, preferredSlotIndex, out PortableObject storageTarget, out bool addedToHand))
            {
                if (useInputAreaCenter)
                {
                    skipInputAreaCenter = true;
                }
                else
                {
                    skippedFloorStackIndexes ??= new HashSet<int>();
                    skippedFloorStackIndexes.Add(stackIndex);
                }

                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            ReleasePickupObjectToStorage(topObject, storageTarget, addedToHand);
            if (useInputAreaCenter)
            {
                NotifyRuntimeItemStackChanged();
            }

            return true;
        }

        return false;
    }

    public bool TryPreviewPickupOneFloorObject(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId)
    {
        return TryPreviewPickupFloorObjects(
            player,
            playerPosition,
            pickupRadius,
            preferredItemId,
            out previewItemId,
            out _);
    }

    public bool TryPreviewPickupFloorObjects(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();

        return TryPreviewPickupFloorStackObjects(
            player,
            playerPosition,
            pickupRadius,
            preferredItemId,
            out previewItemId,
            out previewPickupCount);
    }

    private bool TryPreviewPickupFloorStackObjects(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        if (!TryFindBestManualPickupCandidate(
                player,
                playerPosition,
                pickupRadius,
                preferredItemId,
                null,
                false,
                out _,
                out _,
                out List<PortableObject> stack,
                out _,
                out int itemId,
                out float distanceSqr))
        {
            return false;
        }

        int stackPickupCount = CountManualPickupStackObjectsFromTop(stack, itemId, distanceSqr, pickupRadiusSqr);
        if (stackPickupCount <= 0)
        {
            return false;
        }

        previewItemId = itemId;
        previewPickupCount = stackPickupCount;
        return true;
    }

    public bool TryPickupOneFloorObjectToHand(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();

        if ((floorObjects == null || floorObjects.Count == 0) && inputAreaCenterStack.Count == 0)
        {
            return false;
        }

        HashSet<int> skippedFloorStackIndexes = null;
        bool skipInputAreaCenter = false;
        while (TryFindBestManualPickupCandidate(
                   player,
                   playerPosition,
                   pickupRadius,
                   -1,
                   skippedFloorStackIndexes,
                   skipInputAreaCenter,
                   out bool useInputAreaCenter,
                   out int stackIndex,
                   out List<PortableObject> stack,
                   out PortableObject topObject,
                   out int itemId,
                   out _))
        {
            if (!player.TryAddToHand(itemId, out PortableObject handTarget))
            {
                if (useInputAreaCenter)
                {
                    skipInputAreaCenter = true;
                }
                else
                {
                    skippedFloorStackIndexes ??= new HashSet<int>();
                    skippedFloorStackIndexes.Add(stackIndex);
                }

                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            ReleaseFloorObjectToHand(topObject, handTarget);
            if (useInputAreaCenter)
            {
                NotifyRuntimeItemStackChanged();
            }

            return true;
        }

        return false;
    }

    private bool TryFindBestManualPickupCandidate(
        Player player,
        Vector3 playerPosition,
        float pickupRadius,
        int preferredItemId,
        ISet<int> skippedFloorStackIndexes,
        bool skipInputAreaCenter,
        out bool useInputAreaCenter,
        out int stackIndex,
        out List<PortableObject> stack,
        out PortableObject topObject,
        out int itemId,
        out float distanceSqr)
    {
        useInputAreaCenter = false;
        stackIndex = -1;
        stack = null;
        topObject = null;
        itemId = -1;
        distanceSqr = 0f;

        bool hasFloorCandidate = TryFindBestManualPickupFloorStack(
            player,
            playerPosition,
            pickupRadius,
            preferredItemId,
            skippedFloorStackIndexes,
            out int floorStackIndex,
            out List<PortableObject> floorStack,
            out PortableObject floorTopObject,
            out int floorItemId,
            out float floorDistanceSqr);
        List<PortableObject> inputAreaStack = null;
        PortableObject inputAreaTopObject = null;
        int inputAreaItemId = -1;
        float inputAreaDistanceSqr = 0f;
        bool hasInputAreaCandidate = false;
        if (!skipInputAreaCenter)
        {
            hasInputAreaCandidate = TryFindManualPickupInputAreaCenterStack(
                player,
                playerPosition,
                pickupRadius,
                preferredItemId,
                out inputAreaStack,
                out inputAreaTopObject,
                out inputAreaItemId,
                out inputAreaDistanceSqr);
        }

        if (!hasFloorCandidate && !hasInputAreaCandidate)
        {
            return false;
        }

        useInputAreaCenter = hasInputAreaCandidate
                             && (!hasFloorCandidate || inputAreaDistanceSqr <= floorDistanceSqr);
        if (useInputAreaCenter)
        {
            stack = inputAreaStack;
            topObject = inputAreaTopObject;
            itemId = inputAreaItemId;
            distanceSqr = inputAreaDistanceSqr;
            return true;
        }

        stackIndex = floorStackIndex;
        stack = floorStack;
        topObject = floorTopObject;
        itemId = floorItemId;
        distanceSqr = floorDistanceSqr;
        return true;
    }

    private bool TryFindBestManualPickupFloorStack(
        Player player,
        Vector3 playerPosition,
        float pickupRadius,
        int preferredItemId,
        ISet<int> skippedStackIndexes,
        out int bestStackIndex,
        out List<PortableObject> bestStack,
        out PortableObject bestTopObject,
        out int bestItemId,
        out float bestDistanceSqr)
    {
        bestStackIndex = -1;
        bestStack = null;
        bestTopObject = null;
        bestItemId = -1;
        bestDistanceSqr = 0f;
        if (player == null
            || pickupRadius <= 0f
            || floorObjects == null
            || floorObjects.Count == 0
            || floorStacks == null
            || floorStacks.Count == 0)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        bool found = false;
        for (int candidateStackIndex = 0; candidateStackIndex < floorObjects.Count; candidateStackIndex++)
        {
            if (skippedStackIndexes != null && skippedStackIndexes.Contains(candidateStackIndex))
            {
                continue;
            }

            Transform anchor = floorObjects[candidateStackIndex];
            if (anchor == null || candidateStackIndex >= floorStacks.Count)
            {
                continue;
            }

            List<PortableObject> candidateStack = floorStacks[candidateStackIndex];
            CleanupPortableStack(candidateStack);
            if (candidateStack == null || candidateStack.Count == 0)
            {
                continue;
            }

            PortableObject candidateTopObject = candidateStack[candidateStack.Count - 1];
            Vector3 offset = anchor.position - playerPosition;
            offset.y = 0f;
            float candidateDistanceSqr = offset.sqrMagnitude;
            UpdatePickupGates(candidateStack, gateOriginPosition);

            if (!IsManualPickupStackCandidate(
                    candidateTopObject,
                    candidateDistanceSqr,
                    pickupRadiusSqr,
                    preferredItemId,
                    out int candidateItemId))
            {
                continue;
            }

            if (found && candidateDistanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            found = true;
            bestStackIndex = candidateStackIndex;
            bestStack = candidateStack;
            bestTopObject = candidateTopObject;
            bestItemId = candidateItemId;
            bestDistanceSqr = candidateDistanceSqr;
        }

        return found;
    }

    private bool TryFindManualPickupInputAreaCenterStack(
        Player player,
        Vector3 playerPosition,
        float pickupRadius,
        int preferredItemId,
        out List<PortableObject> stack,
        out PortableObject topObject,
        out int itemId,
        out float distanceSqr)
    {
        stack = null;
        topObject = null;
        itemId = -1;
        distanceSqr = 0f;
        if (player == null
            || pickupRadius <= 0f
            || inputAreaCenterStack.Count == 0
            || IsClosedBoxContentPickupBlocked())
        {
            return false;
        }

        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count == 0)
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null)
        {
            return false;
        }

        Vector3 offset = inputAreaCenterAnchor.position - playerPosition;
        offset.y = 0f;
        distanceSqr = offset.sqrMagnitude;
        float pickupRadiusSqr = pickupRadius * pickupRadius;
        UpdatePickupGates(inputAreaCenterStack, player.transform.position);
        topObject = GetTopPortableObject(inputAreaCenterStack);
        if (!IsManualPickupStackCandidate(
                topObject,
                distanceSqr,
                pickupRadiusSqr,
                preferredItemId,
                out itemId))
        {
            return false;
        }

        stack = inputAreaCenterStack;
        return true;
    }

    private static bool IsManualPickupStackCandidate(
        PortableObject topObject,
        float distanceSqr,
        float pickupRadiusSqr,
        int preferredItemId,
        out int itemId)
    {
        itemId = -1;
        if (topObject == null)
        {
            return false;
        }

        itemId = topObject.ItemId;
        if (itemId < 0)
        {
            return false;
        }

        if (preferredItemId >= 0 && itemId != preferredItemId)
        {
            return false;
        }

        DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
        return topGate == null || topGate.CanManualPickup(distanceSqr, pickupRadiusSqr);
    }

    public bool TryTakeOneFloorObject(out int takenItemId)
    {
        takenItemId = -1;
        EnsureFloorObjectsInitialized();

        if (floorObjects == null || floorObjects.Count == 0)
        {
            return false;
        }

        for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
        {
            List<PortableObject> stack = stackIndex < floorStacks.Count ? floorStacks[stackIndex] : null;
            while (stack != null && stack.Count > 0)
            {
                PortableObject portableObject = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (portableObject == null)
                {
                    continue;
                }

                int itemId = portableObject.ItemId;
                ReleaseFloorObject(portableObject);
                if (itemId < 0)
                {
                    continue;
                }

                takenItemId = itemId;
                return true;
            }
        }

        return false;
    }

    public bool TryGetClosestFloorObjectWorldPosition(Vector3 referenceWorldPosition, out Vector3 worldPosition)
    {
        return TryGetClosestFloorObjectWorldPosition(referenceWorldPosition, null, out worldPosition);
    }

    public bool TryGetClosestFloorObjectWorldPosition(Vector3 referenceWorldPosition, Predicate<int> itemFilter, out Vector3 worldPosition)
    {
        worldPosition = transform.position;
        return TryFindClosestFloorObject(referenceWorldPosition, itemFilter, out _, out _, out worldPosition);
    }

    public bool TryTakeClosestFloorObject(Vector3 referenceWorldPosition, out int takenItemId)
    {
        return TryTakeClosestFloorObject(referenceWorldPosition, null, out takenItemId);
    }

    public bool TryTakeClosestFloorObject(Vector3 referenceWorldPosition, Predicate<int> itemFilter, out int takenItemId)
    {
        takenItemId = -1;
        if (!TryFindClosestFloorObject(referenceWorldPosition, itemFilter, out int stackIndex, out PortableObject portableObject, out _)
            || portableObject == null
            || stackIndex < 0
            || stackIndex >= floorStacks.Count)
        {
            return false;
        }

        List<PortableObject> stack = floorStacks[stackIndex];
        if (stack == null || stack.Count <= 0 || stack[stack.Count - 1] != portableObject)
        {
            return false;
        }

        int itemId = portableObject.ItemId;
        stack.RemoveAt(stack.Count - 1);
        ReleaseFloorObject(portableObject);
        NotifyRuntimeItemStackChanged();
        if (itemId < 0)
        {
            return false;
        }

        takenItemId = itemId;
        return true;
    }

    private bool TryFindClosestFloorObject(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        out int bestStackIndex,
        out PortableObject bestPortableObject,
        out Vector3 bestWorldPosition)
    {
        bestStackIndex = -1;
        bestPortableObject = null;
        bestWorldPosition = transform.position;
        EnsureFloorObjectsInitialized();

        if (floorObjects == null || floorObjects.Count == 0)
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
        {
            List<PortableObject> stack = stackIndex < floorStacks.Count ? floorStacks[stackIndex] : null;
            while (stack != null && stack.Count > 0)
            {
                PortableObject portableObject = stack[stack.Count - 1];
                if (portableObject == null)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                int itemId = portableObject.ItemId;
                if (itemId < 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                    ReleaseFloorObject(portableObject);
                    continue;
                }

                if (itemFilter != null && !itemFilter(itemId))
                {
                    break;
                }

                Vector3 worldPosition = portableObject.transform.position;
                Vector3 offset = worldPosition - referenceWorldPosition;
                offset.y = 0f;
                float distanceSqr = offset.sqrMagnitude;
                if (bestStackIndex >= 0 && distanceSqr >= bestDistanceSqr)
                {
                    break;
                }

                bestStackIndex = stackIndex;
                bestPortableObject = portableObject;
                bestWorldPosition = worldPosition;
                bestDistanceSqr = distanceSqr;
                break;
            }
        }

        return bestStackIndex >= 0;
    }

    public int TransferFloorObjectsToHand(Player player)
    {
        if (player == null)
        {
            return 0;
        }

        EnsureFloorObjectsInitialized();
        if (floorStacks == null || floorStacks.Count == 0)
        {
            return 0;
        }

        int transferred = 0;

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            for (int objectIndex = stack.Count - 1; objectIndex >= 0; objectIndex--)
            {
                PortableObject floorObject = stack[objectIndex];
                if (floorObject == null)
                {
                    stack.RemoveAt(objectIndex);
                    continue;
                }

                int itemId = floorObject.ItemId;
                if (itemId < 0)
                {
                    stack.RemoveAt(objectIndex);
                    ReleaseFloorObjectToHand(floorObject, null);
                    continue;
                }

                if (!player.TryAddToHand(itemId, out PortableObject handTarget))
                {
                    return transferred;
                }

                stack.RemoveAt(objectIndex);
                ReleaseFloorObjectToHand(floorObject, handTarget);
                transferred++;
            }
        }

        for (int objectIndex = inputAreaCenterStack.Count - 1; objectIndex >= 0; objectIndex--)
        {
            PortableObject floorObject = inputAreaCenterStack[objectIndex];
            if (floorObject == null)
            {
                inputAreaCenterStack.RemoveAt(objectIndex);
                NotifyRuntimeItemStackChanged();
                continue;
            }

            int itemId = floorObject.ItemId;
            if (itemId < 0)
            {
                inputAreaCenterStack.RemoveAt(objectIndex);
                ReleaseFloorObjectToHand(floorObject, null);
                NotifyRuntimeItemStackChanged();
                continue;
            }

            if (!player.TryAddToHand(itemId, out PortableObject handTarget))
            {
                return transferred;
            }

            inputAreaCenterStack.RemoveAt(objectIndex);
            ReleaseFloorObjectToHand(floorObject, handTarget);
            NotifyRuntimeItemStackChanged();
            transferred++;
        }

        return transferred;
    }

    private void EnsureFloorObjectsInitialized()
    {
        if (floorObjects == null)
        {
            floorObjects = new List<Transform>();
        }

        while (floorStacks.Count < floorObjects.Count)
        {
            floorStacks.Add(new List<PortableObject>());
        }

        while (floorStacks.Count > floorObjects.Count)
        {
            floorStacks.RemoveAt(floorStacks.Count - 1);
        }

        int conveyorLaneCount = GetConveyorLaneCount();
        int previousConveyorLaneCount = conveyorStack.Count;
        while (conveyorStack.Count < conveyorLaneCount)
        {
            conveyorStack.Add(null);
            conveyorItemIds.Add(-1);
            conveyorItemMoveFrames.Add(-1);
            conveyorItemMotionStates.Add(default);
            conveyorItemPickupGateStates.Add(default);
            conveyorItemMovementHoldUntilTimes.Add(0f);
        }

        while (conveyorStack.Count > conveyorLaneCount)
        {
            int lastIndex = conveyorStack.Count - 1;
            conveyorStack.RemoveAt(lastIndex);
            if (lastIndex < conveyorItemIds.Count)
            {
                conveyorItemIds.RemoveAt(lastIndex);
            }

            if (lastIndex < conveyorItemMoveFrames.Count)
            {
                conveyorItemMoveFrames.RemoveAt(lastIndex);
            }

            if (lastIndex < conveyorItemMotionStates.Count)
            {
                conveyorItemMotionStates.RemoveAt(lastIndex);
            }

            if (lastIndex < conveyorItemPickupGateStates.Count)
            {
                conveyorItemPickupGateStates.RemoveAt(lastIndex);
            }

            if (lastIndex < conveyorItemMovementHoldUntilTimes.Count)
            {
                conveyorItemMovementHoldUntilTimes.RemoveAt(lastIndex);
            }
        }

        while (conveyorItemIds.Count < conveyorStack.Count)
        {
            conveyorItemIds.Add(-1);
        }

        while (conveyorItemMoveFrames.Count < conveyorStack.Count)
        {
            conveyorItemMoveFrames.Add(-1);
        }

        while (conveyorItemMotionStates.Count < conveyorStack.Count)
        {
            conveyorItemMotionStates.Add(default);
        }

        while (conveyorItemPickupGateStates.Count < conveyorStack.Count)
        {
            conveyorItemPickupGateStates.Add(default);
        }

        while (conveyorItemMovementHoldUntilTimes.Count < conveyorStack.Count)
        {
            conveyorItemMovementHoldUntilTimes.Add(0f);
        }

        while (conveyorItemIds.Count > conveyorStack.Count)
        {
            conveyorItemIds.RemoveAt(conveyorItemIds.Count - 1);
        }

        while (conveyorItemMoveFrames.Count > conveyorStack.Count)
        {
            conveyorItemMoveFrames.RemoveAt(conveyorItemMoveFrames.Count - 1);
        }

        while (conveyorItemMotionStates.Count > conveyorStack.Count)
        {
            conveyorItemMotionStates.RemoveAt(conveyorItemMotionStates.Count - 1);
        }

        while (conveyorItemPickupGateStates.Count > conveyorStack.Count)
        {
            conveyorItemPickupGateStates.RemoveAt(conveyorItemPickupGateStates.Count - 1);
        }

        while (conveyorItemMovementHoldUntilTimes.Count > conveyorStack.Count)
        {
            conveyorItemMovementHoldUntilTimes.RemoveAt(conveyorItemMovementHoldUntilTimes.Count - 1);
        }

        if (previousConveyorLaneCount != conveyorStack.Count)
        {
            InvalidateConveyorLaneLayoutCache();
        }

        NormalizeInactiveConveyorLanes();
    }

    public void RefreshConveyorSlotDotVisuals()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!IsConveyorStackingEnabled())
        {
            SetConveyorSlotDotsVisible(false);
            TerrainGenerator.Active?.SetConveyorDotVisualActive(this, false);
            return;
        }

        if (!ShouldShowConveyorSlotDots())
        {
            SetConveyorSlotDotsVisible(false);
            TerrainGenerator.Active?.SetConveyorDotVisualActive(this, false);
            return;
        }

        int laneCount = GetConveyorLaneCount();
        SetConveyorSlotDotsVisible(false);
        TerrainGenerator.Active?.SetConveyorDotVisualActive(this, laneCount > 0);
    }

    private static bool ShouldShowConveyorSlotDots()
    {
        return GameManager.Instance != null && GameManager.Instance.ShowConveyorSlotDots;
    }

    public void RefreshBeltDirectionDebugVisuals()
    {
        TerrainGenerator.Active?.SetBeltDirectionVisualActive(
            this,
            Application.isPlaying
            && ShouldShowBeltDirections()
            && (TryGetBeltDirectionArrowPose(out _, out _)
                || HasFluidDirectionArrow()));
    }

    public int AppendDirectionArrowMatrices(List<Matrix4x4> matrices)
    {
        if (matrices == null)
        {
            return 0;
        }

        int startCount = matrices.Count;
        if (TryGetBeltDirectionArrowMatrix(out Matrix4x4 beltMatrix))
        {
            matrices.Add(beltMatrix);
        }

        AppendFluidDirectionArrowMatrices(matrices);
        return matrices.Count - startCount;
    }

    public bool TryGetBeltDirectionArrowMatrix(out Matrix4x4 matrix)
    {
        matrix = Matrix4x4.identity;
        if (!Application.isPlaying
            || !IsConveyorStackingEnabled()
            || !ShouldShowBeltDirections()
            || !TryGetBeltDirectionArrowPose(out Vector3 worldPosition, out Quaternion worldRotation))
        {
            return false;
        }

        matrix = Matrix4x4.TRS(worldPosition, worldRotation, Vector3.one);
        return true;
    }

    private static bool ShouldShowBeltDirections()
    {
        return GameManager.Instance != null && GameManager.Instance.ShowDirections;
    }

    private bool TryGetBeltDirectionArrowPose(out Vector3 worldPosition, out Quaternion worldRotation)
    {
        worldPosition = Vector3.zero;
        worldRotation = Quaternion.identity;
        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            return false;
        }

        Vector3 flowWorldDirection = new Vector3(flowDirection.x, 0f, flowDirection.y);
        if (flowWorldDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        worldPosition = GetBeltDirectionArrowWorldPosition();
        worldRotation = Quaternion.LookRotation(flowWorldDirection.normalized, Vector3.up);
        return true;
    }

    private bool HasFluidDirectionArrow()
    {
        if (!IsFluidDirectionMapObject(mapObject))
        {
            return false;
        }

        if (mapObject is Pump pump)
        {
            return pump.TryGetPipeConnectionDirection(pump.transform.rotation, out Vector2Int pumpDirection)
                   && pumpDirection != Vector2Int.zero;
        }

        if (!(mapObject is Pipe pipe) || !TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator))
        {
            return false;
        }

        return TryFindFluidSourceDirection(pipe, terrainGenerator, out _, out _)
               && CollectFluidConnectedDirections(pipe, terrainGenerator, fluidDirectionConnectedDirections) > 0;
    }

    private int AppendFluidDirectionArrowMatrices(List<Matrix4x4> matrices)
    {
        if (matrices == null
            || !Application.isPlaying
            || !ShouldShowBeltDirections()
            || !IsFluidDirectionMapObject(mapObject))
        {
            return 0;
        }

        if (mapObject is Pump pump)
        {
            if (!pump.TryGetPipeConnectionDirection(pump.transform.rotation, out Vector2Int pumpDirection)
                || pumpDirection == Vector2Int.zero)
            {
                return 0;
            }

            matrices.Add(CreateFluidDirectionArrowMatrix(pumpDirection, 0, 1));
            return 1;
        }

        if (!(mapObject is Pipe pipe)
            || !TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator)
            || !TryFindFluidSourceDirection(pipe, terrainGenerator, out Vector2Int sourceDirection, out Vector2Int sourceFlowDirection))
        {
            return 0;
        }

        int connectedDirectionCount = CollectFluidConnectedDirections(pipe, terrainGenerator, fluidDirectionConnectedDirections);
        if (connectedDirectionCount <= 0)
        {
            if (sourceFlowDirection == Vector2Int.zero)
            {
                return 0;
            }

            matrices.Add(CreateFluidDirectionArrowMatrix(sourceFlowDirection, 0, 1));
            return 1;
        }

        int addedCount = 0;
        int outgoingIndex = 0;
        int outgoingCount = CountFluidOutgoingDirections(fluidDirectionConnectedDirections, sourceDirection);
        for (int i = 0; i < fluidDirectionConnectedDirections.Count; i++)
        {
            Vector2Int direction = fluidDirectionConnectedDirections[i];
            if (direction == Vector2Int.zero || direction == sourceDirection)
            {
                continue;
            }

            matrices.Add(CreateFluidDirectionArrowMatrix(direction, outgoingIndex, outgoingCount));
            outgoingIndex++;
            addedCount++;
        }

        if (addedCount <= 0 && sourceDirection != Vector2Int.zero)
        {
            matrices.Add(CreateFluidDirectionArrowMatrix(-sourceDirection, 0, 1));
            addedCount = 1;
        }

        return addedCount;
    }

    private static int CountFluidOutgoingDirections(IReadOnlyList<Vector2Int> directions, Vector2Int sourceDirection)
    {
        if (directions == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < directions.Count; i++)
        {
            Vector2Int direction = directions[i];
            if (direction != Vector2Int.zero && direction != sourceDirection)
            {
                count++;
            }
        }

        return count;
    }

    private Matrix4x4 CreateFluidDirectionArrowMatrix(Vector2Int flowDirection, int arrowIndex, int arrowCount)
    {
        Vector3 flowWorldDirection = new Vector3(flowDirection.x, 0f, flowDirection.y);
        if (flowWorldDirection.sqrMagnitude <= 0.0001f)
        {
            return Matrix4x4.identity;
        }

        flowWorldDirection.Normalize();
        Vector3 worldPosition = GetFluidDirectionArrowWorldPosition(flowWorldDirection, arrowIndex, arrowCount);
        Quaternion worldRotation = Quaternion.LookRotation(flowWorldDirection, Vector3.up);
        return Matrix4x4.TRS(worldPosition, worldRotation, Vector3.one);
    }

    private Vector3 GetFluidDirectionArrowWorldPosition(Vector3 flowWorldDirection, int arrowIndex, int arrowCount)
    {
        Vector3 center = transform.position;
        float verticalOffset = mapObject is Pump ? 0.42f : 0.28f;
        center.y = Mathf.Max(
            center.y + verticalOffset,
            transform.position.y + BeltDirectionArrowMinimumWorldHeight);

        if (arrowCount > 1)
        {
            Vector3 perpendicular = Vector3.Cross(Vector3.up, flowWorldDirection).normalized;
            float normalizedIndex = arrowIndex - ((arrowCount - 1) * 0.5f);
            center += perpendicular * (normalizedIndex * 0.18f);
        }

        return center;
    }

    private bool TryFindFluidSourceDirection(
        Pipe originPipe,
        TerrainGenerator terrainGenerator,
        out Vector2Int sourceDirection,
        out Vector2Int sourceFlowDirection)
    {
        sourceDirection = Vector2Int.zero;
        sourceFlowDirection = Vector2Int.zero;
        if (originPipe == null || terrainGenerator == null)
        {
            return false;
        }

        if (InputOutputModule.TryGetRuntimePipeSourceAtCoordinate(coordinate, out Pump sameCoordinatePump)
            && sameCoordinatePump != null
            && sameCoordinatePump.TryGetPipeConnectionDirection(sameCoordinatePump.transform.rotation, out sourceFlowDirection)
            && sourceFlowDirection != Vector2Int.zero)
        {
            sourceDirection = -sourceFlowDirection;
            return true;
        }

        fluidDirectionSourceSearchQueue.Clear();
        fluidDirectionSourceSearchVisited.Clear();
        fluidDirectionSourceSearchQueue.Enqueue(new FluidSourceSearchNode(coordinate, Vector2Int.zero));
        fluidDirectionSourceSearchVisited.Add(coordinate);

        while (fluidDirectionSourceSearchQueue.Count > 0)
        {
            FluidSourceSearchNode node = fluidDirectionSourceSearchQueue.Dequeue();
            if (!terrainGenerator.TryGetLoadedBlock(node.coordinate, out Block currentBlock)
                || currentBlock == null
                || !(currentBlock.MapObject is Pipe currentPipe))
            {
                continue;
            }

            Quaternion currentPipeRotation = currentPipe.transform.rotation;
            for (int i = 0; i < ConveyorNeighborDirections.Length; i++)
            {
                Vector2Int direction = ConveyorNeighborDirections[i];
                if (!currentPipe.HasConnectionTowards(currentPipeRotation, direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = node.coordinate + direction;
                if (TryGetFluidSourceAtNeighbor(
                        terrainGenerator,
                        nextCoordinate,
                        -direction,
                        out Pump sourcePump)
                    && sourcePump != null)
                {
                    sourceDirection = node.firstDirection != Vector2Int.zero
                        ? node.firstDirection
                        : direction;
                    sourceFlowDirection = -sourceDirection;
                    return true;
                }

                if (!terrainGenerator.TryGetLoadedBlock(nextCoordinate, out Block nextBlock)
                    || nextBlock == null
                    || !(nextBlock.MapObject is Pipe nextPipe)
                    || !nextPipe.gameObject.activeInHierarchy
                    || !nextPipe.HasConnectionTowards(nextPipe.transform.rotation, -direction)
                    || !fluidDirectionSourceSearchVisited.Add(nextCoordinate))
                {
                    continue;
                }

                Vector2Int firstDirection = node.firstDirection != Vector2Int.zero
                    ? node.firstDirection
                    : direction;
                fluidDirectionSourceSearchQueue.Enqueue(new FluidSourceSearchNode(nextCoordinate, firstDirection));
            }
        }

        return false;
    }

    private bool TryGetFluidSourceAtNeighbor(
        TerrainGenerator terrainGenerator,
        Vector2Int sourceCoordinate,
        Vector2Int directionFromSourceToPipe,
        out Pump sourcePump)
    {
        sourcePump = null;
        if (InputOutputModule.TryGetRuntimePipeSourceAtCoordinate(sourceCoordinate, out sourcePump)
            && sourcePump != null)
        {
            if (directionFromSourceToPipe == Vector2Int.zero
                || sourcePump.HasPipeConnectionTowards(sourcePump.transform.rotation, directionFromSourceToPipe))
            {
                return true;
            }
        }

        if (terrainGenerator == null
            || !terrainGenerator.TryGetLoadedBlock(sourceCoordinate, out Block sourceBlock)
            || sourceBlock == null
            || !(sourceBlock.MapObject is Pump directPump)
            || !directPump.gameObject.activeInHierarchy
            || !directPump.HasPipeConnectionTowards(directPump.transform.rotation, directionFromSourceToPipe))
        {
            return false;
        }

        sourcePump = directPump;
        return true;
    }

    private int CollectFluidConnectedDirections(
        Pipe pipe,
        TerrainGenerator terrainGenerator,
        List<Vector2Int> connectedDirections)
    {
        if (connectedDirections == null)
        {
            return 0;
        }

        connectedDirections.Clear();
        if (pipe == null || terrainGenerator == null)
        {
            return 0;
        }

        Quaternion pipeRotation = pipe.transform.rotation;
        for (int i = 0; i < ConveyorNeighborDirections.Length; i++)
        {
            Vector2Int direction = ConveyorNeighborDirections[i];
            if (!pipe.HasConnectionTowards(pipeRotation, direction)
                || !IsFluidConnectedDirection(pipe, terrainGenerator, direction))
            {
                continue;
            }

            connectedDirections.Add(direction);
        }

        return connectedDirections.Count;
    }

    private bool IsFluidConnectedDirection(Pipe pipe, TerrainGenerator terrainGenerator, Vector2Int direction)
    {
        if (pipe == null || terrainGenerator == null || direction == Vector2Int.zero)
        {
            return false;
        }

        Vector2Int neighborCoordinate = coordinate + direction;
        if (InputOutputModule.TryGetRuntimePipeFluidStorageAtCoordinate(
                neighborCoordinate,
                null,
                false,
                out _))
        {
            return true;
        }

        if (InputOutputModule.TryGetRuntimePipeSourceAtCoordinate(neighborCoordinate, out Pump sourcePump)
            && sourcePump != null
            && sourcePump.HasPipeConnectionTowards(sourcePump.transform.rotation, -direction))
        {
            return true;
        }

        if (!terrainGenerator.TryGetLoadedBlock(neighborCoordinate, out Block neighborBlock)
            || neighborBlock == null
            || neighborBlock.MapObject == null)
        {
            return false;
        }

        if (neighborBlock.MapObject is Pipe neighborPipe)
        {
            return neighborPipe.gameObject.activeInHierarchy
                   && neighborPipe.HasConnectionTowards(neighborPipe.transform.rotation, -direction);
        }

        if (neighborBlock.MapObject is Pump pump)
        {
            return pump.gameObject.activeInHierarchy
                   && pump.HasPipeConnectionTowards(pump.transform.rotation, -direction);
        }

        return neighborBlock.MapObject is InstallationObject installationObject
               && installationObject.gameObject.activeInHierarchy
               && installationObject.CanStoreFluid;
    }

    private static bool IsFluidDirectionMapObject(MapObject candidate)
    {
        return candidate is Pipe || candidate is Pump;
    }

    private Vector3 GetBeltDirectionArrowWorldPosition()
    {
        Vector3 center = transform.position;
        int laneCount = GetConveyorLaneCount();
        if (laneCount > 0)
        {
            Vector3 sum = Vector3.zero;
            int validLaneCount = 0;
            for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
            {
                if (!IsValidConveyorLaneIndex(laneIndex))
                {
                    continue;
                }

                sum += GetConveyorLaneWorldPosition(laneIndex);
                validLaneCount++;
            }

            if (validLaneCount > 0)
            {
                center = sum / validLaneCount;
            }
        }

        center.y = Mathf.Max(
            center.y + BeltDirectionArrowVerticalOffset,
            transform.position.y + BeltDirectionArrowMinimumWorldHeight);
        return center;
    }

    public static Mesh ResolveBeltDirectionArrowMesh()
    {
        if (beltDirectionArrowMesh != null)
        {
            return beltDirectionArrowMesh;
        }

        beltDirectionArrowMesh = new Mesh
        {
            name = "BeltDirectionArrowMesh",
            hideFlags = HideFlags.DontSave
        };

        List<Vector3> vertices = new List<Vector3>(14);
        List<int> triangles = new List<int>(18);
        List<Color> colors = new List<Color>(14);
        AddBeltDirectionArrowShape(
            vertices,
            triangles,
            colors,
            BeltDirectionArrowLength * 1.12f,
            BeltDirectionArrowShaftWidth * 1.45f,
            BeltDirectionArrowHeadLength * 1.15f,
            BeltDirectionArrowHeadWidth * 1.35f,
            -0.012f,
            new Color(0f, 0f, 0f, 0.82f));
        AddBeltDirectionArrowShape(
            vertices,
            triangles,
            colors,
            BeltDirectionArrowLength,
            BeltDirectionArrowShaftWidth,
            BeltDirectionArrowHeadLength,
            BeltDirectionArrowHeadWidth,
            0f,
            new Color(1f, 0.92f, 0.08f, 1f));

        beltDirectionArrowMesh.SetVertices(vertices);
        beltDirectionArrowMesh.SetTriangles(triangles, 0);
        beltDirectionArrowMesh.SetColors(colors);
        List<Vector3> normals = new List<Vector3>(vertices.Count);
        List<Vector2> uvs = new List<Vector2>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(vertices[i].x, vertices[i].z));
        }

        beltDirectionArrowMesh.SetNormals(normals);
        beltDirectionArrowMesh.SetUVs(0, uvs);
        beltDirectionArrowMesh.RecalculateBounds();
        return beltDirectionArrowMesh;
    }

    private static void AddBeltDirectionArrowShape(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        float length,
        float shaftWidth,
        float headLength,
        float headWidth,
        float y,
        Color color)
    {
        float halfLength = length * 0.5f;
        float shaftHalfWidth = shaftWidth * 0.5f;
        float headHalfWidth = headWidth * 0.5f;
        float headBaseZ = halfLength - headLength;
        int startIndex = vertices.Count;

        vertices.Add(new Vector3(-shaftHalfWidth, y, -halfLength));
        vertices.Add(new Vector3(shaftHalfWidth, y, -halfLength));
        vertices.Add(new Vector3(shaftHalfWidth, y, headBaseZ));
        vertices.Add(new Vector3(-shaftHalfWidth, y, headBaseZ));
        vertices.Add(new Vector3(-headHalfWidth, y, headBaseZ));
        vertices.Add(new Vector3(headHalfWidth, y, headBaseZ));
        vertices.Add(new Vector3(0f, y, halfLength));

        for (int i = 0; i < 7; i++)
        {
            colors.Add(color);
        }

        triangles.Add(startIndex);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex);
        triangles.Add(startIndex + 3);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 4);
        triangles.Add(startIndex + 6);
        triangles.Add(startIndex + 5);
    }

    private void EnsureConveyorSlotDotRoot()
    {
        if (conveyorSlotDotRoot != null)
        {
            return;
        }

        GameObject rootObject = new GameObject(ConveyorSlotDotRootName);
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        conveyorSlotDotRoot = rootObject.transform;
    }

    private Transform GetOrCreateConveyorSlotDot(int laneIndex)
    {
        while (conveyorSlotDots.Count <= laneIndex)
        {
            conveyorSlotDots.Add(null);
        }

        while (conveyorSlotDotRenderers.Count <= laneIndex)
        {
            conveyorSlotDotRenderers.Add(null);
        }

        Transform existingDot = conveyorSlotDots[laneIndex];
        if (existingDot != null)
        {
            return existingDot;
        }

        if (conveyorSlotDotRoot == null)
        {
            return null;
        }

        GameObject dotObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotObject.name = $"ConveyorSlotDot_{laneIndex}";
        dotObject.layer = gameObject.layer;
        dotObject.transform.SetParent(conveyorSlotDotRoot, false);
        dotObject.transform.localRotation = Quaternion.identity;
        dotObject.transform.localScale = new Vector3(
            ConveyorSlotDotDiameter,
            ConveyorSlotDotThickness,
            ConveyorSlotDotDiameter);

        Collider collider = dotObject.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        MeshRenderer renderer = dotObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ResolveConveyorSlotDotMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        conveyorSlotDots[laneIndex] = dotObject.transform;
        conveyorSlotDotRenderers[laneIndex] = renderer;
        return dotObject.transform;
    }

    private void SetConveyorSlotDotsVisible(bool visible)
    {
        if (conveyorSlotDotRoot == null)
        {
            return;
        }

        GameObject rootObject = conveyorSlotDotRoot.gameObject;
        if (rootObject.activeSelf != visible)
        {
            rootObject.SetActive(visible);
        }
    }

    private static Material ResolveConveyorSlotDotMaterial()
    {
        if (conveyorSlotDotMaterial != null)
        {
            return conveyorSlotDotMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        conveyorSlotDotMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        conveyorSlotDotMaterial.color = new Color(1f, 0.36f, 0.08f, 1f);
        return conveyorSlotDotMaterial;
    }

    private void UpdateConveyorSlotDotPositions()
    {
        int laneCount = Mathf.Min(GetConveyorLaneCount(), conveyorSlotDots.Count);
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!IsValidConveyorLaneIndex(laneIndex))
            {
                continue;
            }

            Transform dot = conveyorSlotDots[laneIndex];
            if (dot == null || !dot.gameObject.activeSelf)
            {
                continue;
            }

            Vector3 worldPosition = EvaluateAnimatedConveyorSlotDotWorldPosition(laneIndex);
            worldPosition.y += ConveyorSlotDotVerticalOffset;
            dot.position = worldPosition;
            UpdateConveyorSlotDotAppearance(laneIndex);
        }
    }

    private Vector3 EvaluateAnimatedConveyorSlotDotWorldPosition(int laneIndex)
    {
        Vector3 laneWorldPosition = GetConveyorLaneWorldPosition(laneIndex);
        if (!TryGetConveyorSlotDotMotionDirection(laneIndex, laneWorldPosition, out Vector3 motionDirection))
        {
            return laneWorldPosition;
        }

        float travelLength = Mathf.Max(GetConveyorLaneHalfExtent() * 1.35f, ConveyorSlotDotDiameter * 2f);
        float phaseOffset = GetConveyorSlotDotLocalPhaseOffset(laneIndex, motionDirection, travelLength);
        float travelOffset = Mathf.Repeat(
            (Time.timeSinceLevelLoad * Mathf.Max(GetConveyorSpeed(), 0.01f)) + phaseOffset,
            travelLength) - (travelLength * 0.5f);

        return laneWorldPosition + (motionDirection * travelOffset);
    }

    private bool TryGetConveyorSlotDotMotionDirection(int laneIndex, Vector3 laneWorldPosition, out Vector3 motionDirection)
    {
        motionDirection = Vector3.zero;

        if (TryGetConveyorSuccessor(
                laneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex,
                out bool useCornerMotion)
            && destinationBlock != null)
        {
            if (useCornerMotion && destinationBlock == this)
            {
                if (TryGetConveyorCornerLaneTransition(
                        laneIndex,
                        out int cornerSourceLaneIndex,
                        out int cornerDestinationLaneIndex,
                        out float cornerProgress))
                {
                    float previousProgress = Mathf.Clamp01(cornerProgress - 0.05f);
                    float nextProgress = Mathf.Clamp01(cornerProgress + 0.05f);
                    Vector3 previousWorldPosition = EvaluateConveyorCornerPathWorldPosition(
                        cornerSourceLaneIndex,
                        cornerDestinationLaneIndex,
                        previousProgress);
                    Vector3 nextWorldPosition = EvaluateConveyorCornerPathWorldPosition(
                        cornerSourceLaneIndex,
                        cornerDestinationLaneIndex,
                        nextProgress);
                    motionDirection = nextWorldPosition - previousWorldPosition;
                }
            }
            else
            {
                motionDirection = destinationBlock.GetConveyorLaneWorldPosition(destinationLaneIndex) - laneWorldPosition;
            }
        }

        if (motionDirection.sqrMagnitude <= 0.0001f
            && TryGetConveyorPredecessor(
                laneIndex,
                out Block sourceBlock,
                out int sourceLaneIndex,
                out _)
            && sourceBlock != null)
        {
            motionDirection = laneWorldPosition - sourceBlock.GetConveyorLaneWorldPosition(sourceLaneIndex);
        }

        if (motionDirection.sqrMagnitude <= 0.0001f
            && TryGetConveyorFlowDirection(out Vector2Int flowDirection)
            && flowDirection != Vector2Int.zero)
        {
            motionDirection = new Vector3(flowDirection.x, 0f, flowDirection.y);
        }

        motionDirection.y = 0f;
        if (motionDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        motionDirection.Normalize();
        return true;
    }

    private float GetConveyorSlotDotLocalPhaseOffset(int laneIndex, Vector3 motionDirection, float travelLength)
    {
        Vector3 coordinateVector = new Vector3(coordinate.x, 0f, coordinate.y);
        float blockOffset = Vector3.Dot(coordinateVector, motionDirection) * travelLength;
        float laneOffset = laneIndex * (travelLength / Mathf.Max(1, GetConveyorLaneCount()));
        return blockOffset + laneOffset;
    }

    private ConveyorSlotDotPathCache GetConveyorSlotDotPathCache(int laneIndex)
    {
        while (conveyorSlotDotPathCaches.Count <= laneIndex)
        {
            conveyorSlotDotPathCaches.Add(new ConveyorSlotDotPathCache());
        }

        return conveyorSlotDotPathCaches[laneIndex];
    }

    private bool TryBuildAnimatedConveyorSlotDotPath(int laneIndex, out float totalLength, out float phaseOffset)
    {
        totalLength = 0f;
        phaseOffset = 0f;
        conveyorSlotDotSegments.Clear();

        if (!IsValidConveyorLaneIndex(laneIndex))
        {
            return false;
        }

        conveyorSlotDotOrderedLaneKeys.Clear();
        conveyorSlotDotVisitedLaneKeys.Clear();

        ConveyorLaneKey startLaneKey = new ConveyorLaneKey(this, laneIndex);
        conveyorSlotDotOrderedLaneKeys.Add(startLaneKey);
        conveyorSlotDotVisitedLaneKeys.Add(startLaneKey);
        ConveyorLaneKey currentLaneKey = startLaneKey;
        bool closedLoop = false;

        for (int segmentIndex = 0; segmentIndex < ConveyorSlotDotPathMaxSegments; segmentIndex++)
        {
            if (currentLaneKey.Block == null
                || !currentLaneKey.Block.TryGetConveyorPredecessor(
                    currentLaneKey.LaneIndex,
                    out Block previousBlock,
                    out int previousLaneIndex,
                    out _)
                || previousBlock == null)
            {
                break;
            }

            ConveyorLaneKey previousLaneKey = new ConveyorLaneKey(previousBlock, previousLaneIndex);
            if (!conveyorSlotDotVisitedLaneKeys.Add(previousLaneKey))
            {
                closedLoop = previousLaneKey.Equals(startLaneKey);
                break;
            }

            conveyorSlotDotOrderedLaneKeys.Add(previousLaneKey);
            currentLaneKey = previousLaneKey;
        }

        conveyorSlotDotOrderedLaneKeys.Reverse();

        if (!TryAppendConveyorSlotDotSegmentsFromLaneSequence(conveyorSlotDotOrderedLaneKeys, ref totalLength))
        {
            return false;
        }

        phaseOffset = totalLength;

        if (closedLoop)
        {
            ConveyorLaneKey orderedStartLaneKey = conveyorSlotDotOrderedLaneKeys[0];
            if (!TryAppendConveyorSlotDotSegment(startLaneKey, orderedStartLaneKey, ref totalLength))
            {
                return conveyorSlotDotSegments.Count > 0;
            }

            return conveyorSlotDotSegments.Count > 0;
        }

        currentLaneKey = startLaneKey;
        for (int segmentIndex = 0; segmentIndex < ConveyorSlotDotPathMaxSegments; segmentIndex++)
        {
            if (currentLaneKey.Block == null
                || !currentLaneKey.Block.TryGetConveyorSuccessor(
                    currentLaneKey.LaneIndex,
                    out Block nextBlock,
                    out int nextLaneIndex,
                    out _)
                || nextBlock == null)
            {
                break;
            }

            ConveyorLaneKey nextLaneKey = new ConveyorLaneKey(nextBlock, nextLaneIndex);
            if (!conveyorSlotDotVisitedLaneKeys.Add(nextLaneKey)
                || !TryAppendConveyorSlotDotSegment(currentLaneKey, nextLaneKey, ref totalLength))
            {
                break;
            }

            currentLaneKey = nextLaneKey;
        }

        return conveyorSlotDotSegments.Count > 0;
    }

    private bool TryAppendConveyorSlotDotSegmentsFromLaneSequence(
        List<ConveyorLaneKey> orderedLaneKeys,
        ref float totalLength)
    {
        if (orderedLaneKeys == null || orderedLaneKeys.Count <= 1)
        {
            return false;
        }

        bool appendedAny = false;
        for (int i = 0; i < orderedLaneKeys.Count - 1; i++)
        {
            if (!TryAppendConveyorSlotDotSegment(orderedLaneKeys[i], orderedLaneKeys[i + 1], ref totalLength))
            {
                return false;
            }

            appendedAny = true;
        }

        return appendedAny;
    }

    private bool TryAppendConveyorSlotDotSegment(
        ConveyorLaneKey sourceLaneKey,
        ConveyorLaneKey destinationLaneKey,
        ref float totalLength)
    {
        if (sourceLaneKey.Block == null
            || destinationLaneKey.Block == null
            || !sourceLaneKey.Block.TryGetConveyorSuccessor(
                sourceLaneKey.LaneIndex,
                out Block resolvedDestinationBlock,
                out int resolvedDestinationLaneIndex,
                out bool useCornerMotion)
            || resolvedDestinationBlock != destinationLaneKey.Block
            || resolvedDestinationLaneIndex != destinationLaneKey.LaneIndex)
        {
            return false;
        }

        float segmentLength = sourceLaneKey.Block.GetConveyorPathSegmentLength(
            sourceLaneKey.LaneIndex,
            destinationLaneKey.Block,
            destinationLaneKey.LaneIndex,
            useCornerMotion);
        if (segmentLength <= 0.0001f)
        {
            return false;
        }

        conveyorSlotDotSegments.Add(new ConveyorSlotDotSegment(
            sourceLaneKey.Block,
            sourceLaneKey.LaneIndex,
            destinationLaneKey.Block,
            destinationLaneKey.LaneIndex,
            useCornerMotion,
            segmentLength));
        totalLength += segmentLength;
        return true;
    }

    private float GetConveyorPathSegmentLength(
        int sourceLaneIndex,
        Block destinationBlock,
        int destinationLaneIndex,
        bool useCornerMotion)
    {
        if (useCornerMotion && destinationBlock == this)
        {
            float cornerTimingLength = GetConveyorCornerMotionPathLength(sourceLaneIndex, destinationLaneIndex);
            if (cornerTimingLength > 0.0001f)
            {
                return cornerTimingLength;
            }
        }

        if (destinationBlock != null)
        {
            int coordinateDistance =
                Mathf.Abs(destinationBlock.coordinate.x - coordinate.x)
                + Mathf.Abs(destinationBlock.coordinate.y - coordinate.y);
            if (coordinateDistance > 1)
            {
                Vector3 skippedSourceWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
                Vector3 skippedDestinationWorldPosition = destinationBlock.GetConveyorLaneWorldPosition(destinationLaneIndex);
                if (TryGetBelt2FBridgeViaWorldPosition(out Vector3 skippedViaWorldPosition))
                {
                    return Vector3.Distance(skippedSourceWorldPosition, skippedViaWorldPosition)
                           + Vector3.Distance(skippedViaWorldPosition, skippedDestinationWorldPosition);
                }

                return Vector3.Distance(skippedSourceWorldPosition, skippedDestinationWorldPosition);
            }
        }

        float logicalLength = GetConveyorSlotDotLogicalSegmentLength(this, destinationBlock);
        if (logicalLength > 0.0001f)
        {
            return logicalLength;
        }

        Vector3 sourceWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
        Vector3 destinationWorldPosition = destinationBlock != null
            ? destinationBlock.GetConveyorLaneWorldPosition(destinationLaneIndex)
            : sourceWorldPosition;
        return Vector3.Distance(sourceWorldPosition, destinationWorldPosition);
    }

    private float GetConveyorMovePathLength(
        int sourceLaneIndex,
        Block destinationBlock,
        int destinationLaneIndex,
        bool useCornerMotion,
        Vector3 startWorldPosition)
    {
        float nominalPathLength = GetConveyorPathSegmentLength(
            sourceLaneIndex,
            destinationBlock,
            destinationLaneIndex,
            useCornerMotion);
        if (useCornerMotion && destinationBlock == this)
        {
            return GetConveyorCornerMotionPathLength(sourceLaneIndex, destinationLaneIndex, startWorldPosition);
        }

        if (!TryGetConveyorLinearMoveViaWorldPosition(
                sourceLaneIndex,
                startWorldPosition,
                out Vector3 viaWorldPosition))
        {
            return nominalPathLength;
        }

        float leadInLength = Vector3.Distance(startWorldPosition, viaWorldPosition);
        Vector3 destinationWorldPosition = destinationBlock != null
            ? destinationBlock.GetConveyorLaneWorldPosition(destinationLaneIndex)
            : GetConveyorLaneWorldPosition(sourceLaneIndex);
        float viaToDestinationLength = Vector3.Distance(viaWorldPosition, destinationWorldPosition);
        float viaPathLength = leadInLength + viaToDestinationLength;
        return viaPathLength > ConveyorContinuousMotionEpsilon ? viaPathLength : nominalPathLength;
    }

    private bool TryGetConveyorLinearMoveViaWorldPosition(
        int sourceLaneIndex,
        Vector3 startWorldPosition,
        out Vector3 viaWorldPosition)
    {
        if (TryGetConveyorLaneLayout(out int frontLaneIndex, out _)
            && sourceLaneIndex == frontLaneIndex
            && TryGetNextConveyorBlock(out Block nextBlock)
            && nextBlock != null)
        {
            int coordinateDistance =
                Mathf.Abs(nextBlock.coordinate.x - coordinate.x)
                + Mathf.Abs(nextBlock.coordinate.y - coordinate.y);
            if (coordinateDistance > 1 && TryGetBelt2FBridgeViaWorldPosition(out viaWorldPosition))
            {
                return true;
            }
        }

        viaWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
        Vector3 sourceOffset = startWorldPosition - viaWorldPosition;
        sourceOffset.y = 0f;
        return sourceOffset.sqrMagnitude > ConveyorContinuousMotionEpsilon * ConveyorContinuousMotionEpsilon;
    }

    private bool TryGetBelt2FBridgeViaWorldPosition(out Vector3 viaWorldPosition)
    {
        viaWorldPosition = default;
        return TryGetRuntimeConveyorBelt(out ConveyorBelt runtimeConveyor)
               && runtimeConveyor is ConvayorBelt2F belt2F
               && belt2F != null
               && belt2F.TryGetBridgePeakWorldPosition(out viaWorldPosition);
    }

    private float GetConveyorSlotDotLogicalSegmentLength(Block sourceBlock, Block destinationBlock)
    {
        float sourceSpacing = sourceBlock != null ? sourceBlock.GetConveyorLaneHalfExtent() * 2f : 0f;
        float destinationSpacing = destinationBlock != null ? destinationBlock.GetConveyorLaneHalfExtent() * 2f : 0f;

        if (sourceSpacing > 0.0001f && destinationSpacing > 0.0001f)
        {
            return (sourceSpacing + destinationSpacing) * 0.5f;
        }

        if (sourceSpacing > 0.0001f)
        {
            return sourceSpacing;
        }

        if (destinationSpacing > 0.0001f)
        {
            return destinationSpacing;
        }

        return ConveyorItemSpacing;
    }

    private Vector3 EvaluateConveyorPathSegmentWorldPosition(
        Block sourceBlock,
        int sourceLaneIndex,
        Block destinationBlock,
        int destinationLaneIndex,
        bool useCornerMotion,
        float progress)
    {
        if (useCornerMotion
            && sourceBlock != null
            && destinationBlock == sourceBlock)
        {
            return sourceBlock.EvaluateConveyorCornerPathWorldPosition(
                sourceLaneIndex,
                destinationLaneIndex,
                progress);
        }

        Vector3 sourceWorldPosition = sourceBlock != null
            ? sourceBlock.GetConveyorLaneWorldPosition(sourceLaneIndex)
            : transform.position;
        Vector3 destinationWorldPosition = destinationBlock != null
            ? destinationBlock.GetConveyorLaneWorldPosition(destinationLaneIndex)
            : sourceWorldPosition;

        if (sourceBlock != null
            && destinationBlock != null
            && sourceBlock.TryGetBelt2FBridgeViaWorldPosition(out Vector3 bridgeViaWorldPosition))
        {
            int coordinateDistance =
                Mathf.Abs(destinationBlock.coordinate.x - sourceBlock.coordinate.x)
                + Mathf.Abs(destinationBlock.coordinate.y - sourceBlock.coordinate.y);
            if (coordinateDistance > 1)
            {
                float sourceToViaLength = Vector3.Distance(sourceWorldPosition, bridgeViaWorldPosition);
                float viaToDestinationLength = Vector3.Distance(bridgeViaWorldPosition, destinationWorldPosition);
                float pathLength = sourceToViaLength + viaToDestinationLength;
                if (pathLength > ConveyorContinuousMotionEpsilon)
                {
                    float distance = Mathf.Clamp01(progress) * pathLength;
                    if (sourceToViaLength > ConveyorContinuousMotionEpsilon && distance < sourceToViaLength)
                    {
                        return Vector3.Lerp(sourceWorldPosition, bridgeViaWorldPosition, distance / sourceToViaLength);
                    }

                    if (viaToDestinationLength > ConveyorContinuousMotionEpsilon)
                    {
                        return Vector3.Lerp(
                            bridgeViaWorldPosition,
                            destinationWorldPosition,
                            Mathf.Clamp01((distance - sourceToViaLength) / viaToDestinationLength));
                    }
                }
            }
        }

        return Vector3.LerpUnclamped(sourceWorldPosition, destinationWorldPosition, progress);
    }

    private void UpdateConveyorSlotDotAppearance(int laneIndex)
    {
        if (laneIndex < 0 || laneIndex >= conveyorSlotDotRenderers.Count)
        {
            return;
        }

        MeshRenderer renderer = conveyorSlotDotRenderers[laneIndex];
        Transform dot = laneIndex < conveyorSlotDots.Count ? conveyorSlotDots[laneIndex] : null;
        if (renderer == null || dot == null)
        {
            return;
        }

        dot.localScale = new Vector3(
            ConveyorSlotDotDiameter,
            ConveyorSlotDotThickness,
            ConveyorSlotDotDiameter);

        Color color = new Color(1f, 0.36f, 0.08f, 1f);
        conveyorSlotDotPropertyBlock ??= new MaterialPropertyBlock();
        conveyorSlotDotPropertyBlock.Clear();
        conveyorSlotDotPropertyBlock.SetColor("_Color", color);
        conveyorSlotDotPropertyBlock.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(conveyorSlotDotPropertyBlock);
    }

    private void ResetFloorObjects(bool notifyRuntime = true, bool releaseToPool = true)
    {
        EnsureFloorObjectsInitialized();
        if (floorObjectPool == null)
        {
            for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
            {
                List<PortableObject> stack = floorStacks[stackIndex];
                for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
                {
                    DestroyDetachedFloorObject(stack[objectIndex]);
                }

                stack.Clear();
            }

            for (int objectIndex = 0; objectIndex < inputAreaCenterStack.Count; objectIndex++)
            {
                DestroyDetachedFloorObject(inputAreaCenterStack[objectIndex]);
            }

            for (int objectIndex = 0; objectIndex < conveyorStack.Count; objectIndex++)
            {
                DestroyDetachedFloorObject(conveyorStack[objectIndex]);
            }

            inputAreaCenterStack.Clear();
            conveyorStack.Clear();
            conveyorItemIds.Clear();
            conveyorItemMoveFrames.Clear();
            conveyorItemMotionStates.Clear();
            conveyorItemPickupGateStates.Clear();
            conveyorCornerMotionStates.Clear();
            conveyorLinearMotionStates.Clear();
            if (notifyRuntime)
            {
                WakeConveyorMoveAttemptsAround();
                RefreshConveyorActivityRegistration();
            }

            return;
        }

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null)
                {
                    ReleaseOrDestroyResetFloorObject(portableObject, releaseToPool);
                }
            }

            stack.Clear();
        }

        for (int objectIndex = 0; objectIndex < inputAreaCenterStack.Count; objectIndex++)
        {
            PortableObject portableObject = inputAreaCenterStack[objectIndex];
            if (portableObject != null)
            {
                ReleaseOrDestroyResetFloorObject(portableObject, releaseToPool);
            }
        }

        inputAreaCenterStack.Clear();

        for (int i = 0; i < conveyorStack.Count; i++)
        {
            PortableObject portableObject = conveyorStack[i];
            if (portableObject != null)
            {
                ReleaseOrDestroyResetFloorObject(portableObject, releaseToPool);
            }
        }

        conveyorStack.Clear();
        conveyorItemIds.Clear();
        conveyorItemMoveFrames.Clear();
        conveyorItemMotionStates.Clear();
        conveyorItemPickupGateStates.Clear();
        conveyorCornerMotionStates.Clear();
        conveyorLinearMotionStates.Clear();
        if (notifyRuntime)
        {
            WakeConveyorMoveAttemptsAround();
            RefreshConveyorActivityRegistration();
        }
    }

    private void ReleaseOrDestroyResetFloorObject(PortableObject portableObject, bool releaseToPool)
    {
        if (portableObject == null)
        {
            return;
        }

        if (releaseToPool && floorObjectPool != null && floorObjectPool.CanRelease)
        {
            floorObjectPool.Release(portableObject);
            return;
        }

        DestroyDetachedFloorObject(portableObject);
    }

    private void DestroyDetachedFloorObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(portableObject.gameObject);
            return;
        }

        DestroyImmediate(portableObject.gameObject);
    }

    private int GetAvailableFloorCapacity()
    {
        return GetAvailableFloorCapacity(-1);
    }

    private int GetAvailableFloorCapacity(int itemId)
    {
        EnsureFloorObjectsInitialized();

        if (BlocksFloorObjectStacking())
        {
            return 0;
        }

        int capacity = 0;
        int maxPerStack = Mathf.Max(1, maxFloorObjectsPerStack);
        for (int i = 0; i < floorStacks.Count; i++)
        {
            List<PortableObject> stack = floorStacks[i];
            if (stack == null)
            {
                continue;
            }

            if (itemId >= 0 && !IsStackCompatible(stack, itemId) && stack.Count > 0)
            {
                continue;
            }

            capacity += Mathf.Max(0, maxPerStack - stack.Count);
        }

        return capacity;
    }

    private bool TryGetBestFloorStackIndex(int objectId, bool requireExisting, Vector3 referenceWorldPosition, out int bestStackIndex)
    {
        bestStackIndex = -1;
        float bestDistanceSqr = float.MaxValue;

        for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
        {
            Transform anchor = floorObjects[stackIndex];
            if (anchor == null)
            {
                continue;
            }

            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null)
            {
                continue;
            }

            if (requireExisting && stack.Count == 0)
            {
                continue;
            }

            if (!IsStackCompatible(stack, objectId))
            {
                continue;
            }

            if (stack.Count >= Mathf.Max(1, maxFloorObjectsPerStack))
            {
                continue;
            }

            Vector3 offset = anchor.position - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (bestStackIndex >= 0 && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestStackIndex = stackIndex;
        }

        return bestStackIndex >= 0;
    }

    private Vector3 ResolveDefaultFloorDropReferenceWorldPosition()
    {
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player != null)
        {
            Transform bodyTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
            if (bodyTransform != null)
            {
                return bodyTransform.position;
            }
        }

        return transform.position;
    }

    private bool BlocksFloorObjectStacking()
    {
        if (mapObject is InstallationObject installationObject
            && installationObject != null
            && installationObject.gameObject != null
            && installationObject.gameObject.activeInHierarchy)
        {
            return true;
        }

        return InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate)
               || InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleOutputAreaController.CoordinateIsOutputArea(coordinate);
    }

    private bool ResolveFloorObjectPool()
    {
        if (floorObjectPool != null)
        {
            return true;
        }

        TerrainGenerator generator = GetComponentInParent<TerrainGenerator>();
        GameObject host = generator != null ? generator.gameObject : gameObject;
        floorObjectPool = host.GetComponent<PortableObjectPool>();

        if (floorObjectPool == null)
        {
            floorObjectPool = host.AddComponent<PortableObjectPool>();
        }

        floorObjectPool.Configure(floorObjectPrefab);
        return floorObjectPool != null;
    }

    private bool TryAddPickupObjectToBagOrMatchingHand(Player player, int itemId, int preferredSlotIndex, out PortableObject targetPortableObject, out bool addedToHand)
    {
        targetPortableObject = null;
        addedToHand = false;
        if (player == null || itemId < 0)
        {
            return false;
        }

        if (preferredSlotIndex >= 0)
        {
            if (player.TryAddToBagAtSlot(preferredSlotIndex, itemId, out targetPortableObject))
            {
                return true;
            }

            if (player.HasMatchingHandStackSpace(itemId) && player.TryAddToHand(itemId, out targetPortableObject))
            {
                addedToHand = true;
                return true;
            }

            return false;
        }

        if (player.HasMatchingHandStackSpace(itemId) && player.TryAddToHand(itemId, out targetPortableObject))
        {
            addedToHand = true;
            return true;
        }

        return player.TryAddToBag(itemId, out targetPortableObject);
    }

    private void ReleasePickupObjectToStorage(PortableObject floorObject, PortableObject storageTarget, bool addedToHand)
    {
        if (addedToHand)
        {
            ReleaseFloorObjectToHand(floorObject, storageTarget);
            return;
        }

        ReleaseFloorObjectToBag(floorObject, storageTarget);
    }

    private PortableObject MaterializeConveyorObjectForTransfer(PortableObject portableObject, int itemId, int laneIndex)
    {
        if (itemId < 0)
        {
            return portableObject;
        }

        Vector3 worldPosition = GetConveyorItemVisualWorldPosition(laneIndex);
        if (portableObject == null)
        {
            if (!ResolveFloorObjectPool() || floorObjectPool == null)
            {
                return null;
            }

            portableObject = floorObjectPool.Get(floorObjectPrefab);
            if (portableObject == null)
            {
                return null;
            }

            portableObject.SetItem(itemId);
        }

        if (!portableObject.CachedGameObject.activeSelf)
        {
            portableObject.SetCachedActive(true);
        }

        if (portableObject.IsVisualRenderingSuppressed)
        {
            portableObject.SetVisualRenderingSuppressed(false);
        }

        DroppedItemPickupGate gate = portableObject.PickupGate;
        gate?.SetPreserveStateOnDisable(false);
        portableObject.SetBatchedRendering(false);
        portableObject.SetCachedParent(transform, true);
        SetConveyorPortableObjectWorldPose(portableObject, laneIndex, worldPosition);
        portableObject.CachedTransform.localScale = Vector3.one;
        return portableObject;
    }

    private void ReleaseFloorObjectToBag(PortableObject floorObject, PortableObject bagTarget)
    {
        if (floorObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = floorObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        if (!ResolveFloorObjectPool() || floorObjectPool == null)
        {
            floorObject.SetBatchedRendering(false);
            floorObject.gameObject.SetActive(false);
            return;
        }

        floorObject.SetBatchedRendering(false);
        floorObject.transform.SetParent(null, true);

        if (bagTarget != null)
        {
            floorObject.MoveTo(bagTarget.transform, () => floorObjectPool.Release(floorObject));
        }
        else
        {
            floorObjectPool.Release(floorObject);
        }
    }

    private void ReleaseFloorObject(PortableObject floorObject)
    {
        if (floorObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = floorObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        if (!ResolveFloorObjectPool() || floorObjectPool == null)
        {
            floorObject.SetBatchedRendering(false);
            floorObject.gameObject.SetActive(false);
            return;
        }

        floorObject.SetBatchedRendering(false);
        floorObject.transform.SetParent(null, true);
        floorObjectPool.Release(floorObject);
    }

    private void ReleaseFloorObjectToHand(PortableObject floorObject, PortableObject handTarget)
    {
        if (floorObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = floorObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        if (!ResolveFloorObjectPool() || floorObjectPool == null)
        {
            floorObject.SetBatchedRendering(false);
            floorObject.gameObject.SetActive(false);
            return;
        }

        floorObject.SetBatchedRendering(false);
        floorObject.transform.SetParent(null, true);

        if (handTarget != null)
        {
            floorObject.MoveTo(handTarget.transform, () => floorObjectPool.Release(floorObject));
        }
        else
        {
            floorObjectPool.Release(floorObject);
        }
    }

    private void ConfigureFloorObjectTransform(PortableObject portableObject, Transform anchor, int stackIndex)
    {
        portableObject.transform.SetParent(anchor, false);
        portableObject.transform.localPosition = new Vector3(0f, stackIndex * floorObjectVerticalSpacing, 0f);
        portableObject.transform.localRotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);
    }

    private void ConfigureConveyorObjectTransform(PortableObject portableObject, int laneIndex, Transform anchor)
    {
        if (portableObject == null || anchor == null)
        {
            return;
        }

        Transform portableTransform = portableObject.CachedTransform;
        portableObject.SetCachedParent(transform, true);
        SetConveyorPortableObjectWorldPose(portableObject, laneIndex, GetConveyorLaneWorldPosition(laneIndex, anchor));
        portableTransform.localScale = Vector3.one;
        portableObject.SetCachedActive(true);
    }

    private static bool ShouldSnapConveyorPlacementImmediately(float delay, Func<Vector3> startWorldPositionProvider)
    {
        return delay <= 0f && startWorldPositionProvider == null;
    }

    private void ConfigureInputAreaCenterObjectTransform(PortableObject portableObject, int stackIndex)
    {
        if (portableObject == null || inputAreaCenterAnchor == null)
        {
            return;
        }

        portableObject.transform.SetParent(inputAreaCenterAnchor, false);
        portableObject.transform.localPosition = new Vector3(0f, stackIndex * InputAreaCenterVerticalSpacing, 0f);
        portableObject.transform.localRotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);
    }

    private void ApplyInputAreaCenterObjectVisibility(PortableObject portableObject, int stackIndex)
    {
        if (portableObject == null)
        {
            return;
        }

        if (!inputAreaCenterObjectsVisible)
        {
            if (inputAreaCenterAnchor != null)
            {
                portableObject.transform.SetParent(inputAreaCenterAnchor, false);
                portableObject.transform.localPosition = new Vector3(0f, stackIndex * InputAreaCenterVerticalSpacing, 0f);
                portableObject.transform.localRotation = Quaternion.identity;
                portableObject.transform.localScale = Vector3.one;
            }

            portableObject.SetBatchedRendering(false);
            if (portableObject.gameObject.activeSelf)
            {
                portableObject.gameObject.SetActive(false);
            }

            return;
        }

        if (portableObject.IsMovingToTarget)
        {
            if (!portableObject.gameObject.activeSelf)
            {
                portableObject.gameObject.SetActive(true);
            }

            return;
        }

        ConfigureInputAreaCenterObjectTransform(portableObject, stackIndex);
        portableObject.SetBatchedRendering(true);
    }

    private bool IsStackCompatible(List<PortableObject> stack, int objectId)
    {
        if (stack == null)
        {
            return false;
        }

        if (stack.Count == 0)
        {
            return true;
        }

        PortableObject bottom = stack[0];
        if (bottom == null)
        {
            stack.Clear();
            return true;
        }

        return bottom.ItemId == objectId;
    }

    private int GetConveyorLaneCount()
    {
        return Mathf.Min(ConveyorStackLaneLimit, floorObjects != null ? floorObjects.Count : 0);
    }

    private static bool IsActiveConveyorLaneIndex(int laneIndex)
    {
        return laneIndex == ConveyorSingleLineFrontLaneIndex
            || laneIndex == ConveyorSingleLineBackLaneIndex;
    }

    private static bool TryNormalizeConveyorLaneIndex(int laneIndex, out int normalizedLaneIndex)
    {
        switch (laneIndex)
        {
            case 0:
            case 1:
                normalizedLaneIndex = ConveyorSingleLineFrontLaneIndex;
                return true;
            case 2:
            case 3:
                normalizedLaneIndex = ConveyorSingleLineBackLaneIndex;
                return true;
            default:
                normalizedLaneIndex = -1;
                return false;
        }
    }

    private bool IsConveyorStorageLaneIndex(int laneIndex)
    {
        return laneIndex >= 0
            && laneIndex < conveyorItemIds.Count
            && laneIndex < conveyorStack.Count;
    }

    private bool IsValidConveyorLaneIndex(int laneIndex)
    {
        return IsActiveRuntimeConveyorLaneIndex(laneIndex)
            && IsConveyorStorageLaneIndex(laneIndex);
    }

    private int GetConveyorStoredItemIdAtLane(int laneIndex)
    {
        if (!IsConveyorStorageLaneIndex(laneIndex))
        {
            return -1;
        }

        int itemId = conveyorItemIds[laneIndex];
        if (itemId >= 0)
        {
            return itemId;
        }

        PortableObject portableObject = laneIndex < conveyorStack.Count ? conveyorStack[laneIndex] : null;
        return portableObject != null ? portableObject.ItemId : -1;
    }

    private bool HasConveyorStoredItemAtLane(int laneIndex)
    {
        return GetConveyorStoredItemIdAtLane(laneIndex) >= 0;
    }

    private int GetConveyorItemIdAtLane(int laneIndex)
    {
        if (!IsValidConveyorLaneIndex(laneIndex))
        {
            return -1;
        }

        return GetConveyorStoredItemIdAtLane(laneIndex);
    }

    private bool HasConveyorItemAtLane(int laneIndex)
    {
        return GetConveyorItemIdAtLane(laneIndex) >= 0;
    }

    private PortableObject GetConveyorPortableObjectAtLane(int laneIndex)
    {
        return laneIndex >= 0 && laneIndex < conveyorStack.Count ? conveyorStack[laneIndex] : null;
    }

    private ConveyorPickupGateState GetConveyorPickupGateStateAtLane(int laneIndex)
    {
        if (laneIndex < 0 || laneIndex >= conveyorItemPickupGateStates.Count)
        {
            return ConveyorPickupGateState.Settled();
        }

        ConveyorPickupGateState gateState = conveyorItemPickupGateStates[laneIndex];
        if (!gateState.hasGate)
        {
            gateState.isSettled = true;
        }

        return gateState;
    }

    private void SetConveyorPickupGateStateAtLane(int laneIndex, ConveyorPickupGateState gateState)
    {
        if (laneIndex < 0 || laneIndex >= conveyorItemPickupGateStates.Count)
        {
            return;
        }

        conveyorItemPickupGateStates[laneIndex] = gateState;
        InvalidateConveyorCanMoveCaches();
    }

    private void SetConveyorItemAtLane(
        int laneIndex,
        int itemId,
        PortableObject portableObject,
        ConveyorPickupGateState pickupGateState)
    {
        if (!IsValidConveyorLaneIndex(laneIndex))
        {
            return;
        }

        conveyorItemIds[laneIndex] = itemId;
        conveyorStack[laneIndex] = portableObject;
        conveyorItemMoveFrames[laneIndex] = -1;
        conveyorItemMotionStates[laneIndex] = default;
        conveyorItemPickupGateStates[laneIndex] = pickupGateState;
        ClearConveyorLaneMovementHold(laneIndex);
        ClearConveyorLaneBlockedSleep(laneIndex);
        ClearConveyorLaneCycleBlockedSleep(laneIndex);
        portableObject?.SetSleepAwakeSleeping(IsConveyorItemSleepAwakeSleeping(laneIndex));
        MarkConveyorItemVisualDirty();
        TerrainGenerator.Active?.MarkBeltItemLineDebugDirty(this);
    }

    private bool TryGetConveyorSingleLineMigrationTarget(int sourceLaneIndex, out int targetLaneIndex)
    {
        targetLaneIndex = -1;
        if (!TryNormalizeConveyorLaneIndex(sourceLaneIndex, out int normalizedLaneIndex))
        {
            return false;
        }

        if (IsValidConveyorLaneIndex(normalizedLaneIndex) && !HasConveyorStoredItemAtLane(normalizedLaneIndex))
        {
            targetLaneIndex = normalizedLaneIndex;
            return true;
        }

        int fallbackLaneIndex = normalizedLaneIndex == ConveyorSingleLineFrontLaneIndex
            ? ConveyorSingleLineBackLaneIndex
            : ConveyorSingleLineFrontLaneIndex;
        if (IsValidConveyorLaneIndex(fallbackLaneIndex) && !HasConveyorStoredItemAtLane(fallbackLaneIndex))
        {
            targetLaneIndex = fallbackLaneIndex;
            return true;
        }

        return false;
    }

    private void NormalizeInactiveConveyorLanes()
    {
        bool changed = false;
        for (int laneIndex = 0; laneIndex < conveyorStack.Count; laneIndex++)
        {
            if (IsActiveRuntimeConveyorLaneIndex(laneIndex) || !HasConveyorStoredItemAtLane(laneIndex))
            {
                continue;
            }

            if (TryGetConveyorSingleLineMigrationTarget(laneIndex, out int targetLaneIndex))
            {
                MoveConveyorStoredItemToLane(laneIndex, targetLaneIndex);
            }
            else
            {
                PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
                ClearConveyorStorageLaneRaw(laneIndex);
                ReleaseFloorObject(portableObject);
            }

            changed = true;
        }

        if (!changed)
        {
            return;
        }

        MarkConveyorItemVisualDirty();
        TerrainGenerator.Active?.MarkBeltItemLineDebugDirty(this);
    }

    private void MoveConveyorStoredItemToLane(int sourceLaneIndex, int targetLaneIndex)
    {
        if (!IsConveyorStorageLaneIndex(sourceLaneIndex) || !IsValidConveyorLaneIndex(targetLaneIndex))
        {
            return;
        }

        int itemId = GetConveyorStoredItemIdAtLane(sourceLaneIndex);
        PortableObject portableObject = GetConveyorPortableObjectAtLane(sourceLaneIndex);
        ConveyorPickupGateState pickupGateState = GetConveyorPickupGateStateAtLane(sourceLaneIndex);

        ClearConveyorStorageLaneRaw(sourceLaneIndex);
        conveyorItemIds[targetLaneIndex] = itemId;
        conveyorStack[targetLaneIndex] = portableObject;
        conveyorItemMoveFrames[targetLaneIndex] = -1;
        conveyorItemMotionStates[targetLaneIndex] = default;
        conveyorItemPickupGateStates[targetLaneIndex] = pickupGateState;
        conveyorItemMovementHoldUntilTimes[targetLaneIndex] = 0f;
        ClearConveyorLaneMovementHold(targetLaneIndex);
        ClearConveyorLaneBlockedSleep(targetLaneIndex);
        ClearConveyorLaneCycleBlockedSleep(targetLaneIndex);
        portableObject?.SetSleepAwakeSleeping(IsConveyorItemSleepAwakeSleeping(targetLaneIndex));
    }

    private void ClearConveyorStorageLaneRaw(int laneIndex)
    {
        if (!IsConveyorStorageLaneIndex(laneIndex))
        {
            return;
        }

        PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
        if (portableObject != null)
        {
            conveyorCornerMotionStates.Remove(portableObject);
            conveyorLinearMotionStates.Remove(portableObject);
            portableObject.SetSleepAwakeSleeping(false);
        }

        conveyorItemIds[laneIndex] = -1;
        conveyorStack[laneIndex] = null;
        conveyorItemMoveFrames[laneIndex] = -1;
        conveyorItemMotionStates[laneIndex] = default;
        conveyorItemPickupGateStates[laneIndex] = default;
        ClearConveyorLaneMovementHold(laneIndex);
        ClearConveyorLaneBlockedSleep(laneIndex);
        ClearConveyorLaneCycleBlockedSleep(laneIndex);
        if (laneIndex < conveyorLaneSleepAwakeDarkTintStates.Length)
        {
            conveyorLaneSleepAwakeDarkTintStates[laneIndex] = false;
        }
    }

    private void ClearConveyorItemAtLane(int laneIndex)
    {
        if (!IsConveyorStorageLaneIndex(laneIndex))
        {
            return;
        }

        ClearConveyorStorageLaneRaw(laneIndex);
        MarkConveyorItemVisualDirty();
        TerrainGenerator.Active?.MarkBeltItemLineDebugDirty(this);
    }

    private bool IsConveyorLaneMovementHeld(int laneIndex)
    {
        return laneIndex >= 0
            && laneIndex < conveyorItemMovementHoldUntilTimes.Count
            && conveyorItemMovementHoldUntilTimes[laneIndex] > Time.time;
    }

    private void HoldConveyorLaneMovement(int laneIndex, float delay)
    {
        if (laneIndex < 0 || laneIndex >= conveyorItemMovementHoldUntilTimes.Count || delay <= 0f)
        {
            return;
        }

        conveyorItemMovementHoldUntilTimes[laneIndex] = Mathf.Max(
            conveyorItemMovementHoldUntilTimes[laneIndex],
            Time.time + delay);
        InvalidateConveyorCanMoveCaches();
    }

    private void ClearConveyorLaneMovementHold(int laneIndex)
    {
        if (laneIndex < 0 || laneIndex >= conveyorItemMovementHoldUntilTimes.Count)
        {
            return;
        }

        conveyorItemMovementHoldUntilTimes[laneIndex] = 0f;
        InvalidateConveyorCanMoveCaches();
    }

    private bool IsConveyorLaneBlockedSleep(int laneIndex)
    {
        return laneIndex >= 0
            && laneIndex < conveyorLaneBlockedSleepStates.Length
            && conveyorLaneBlockedSleepStates[laneIndex];
    }

    private bool SleepConveyorLaneBlocked(int laneIndex)
    {
        if (laneIndex < 0
            || laneIndex >= conveyorLaneBlockedSleepStates.Length
            || conveyorLaneBlockedSleepStates[laneIndex]
            || !HasConveyorItemAtLane(laneIndex)
            || WasConveyorItemMovedThisFrame(laneIndex)
            || !IsConveyorItemSettledAtLane(laneIndex))
        {
            return false;
        }

        if (ShouldKeepSoloConveyorLaneAwake(laneIndex))
        {
            return false;
        }

        if (CanRetryConveyorLaneMove(laneIndex, true))
        {
            return false;
        }

        conveyorLaneBlockedSleepStates[laneIndex] = true;
        nextConveyorLaneMoveAttemptTimes[laneIndex] = 0f;
        return true;
    }

    private bool ShouldKeepSoloConveyorLaneAwake(int laneIndex)
    {
        if (!HasConveyorItemAtLane(laneIndex)
            || WasConveyorItemMovedThisFrame(laneIndex)
            || !IsConveyorItemSettledAtLane(laneIndex))
        {
            return false;
        }

        if (!TryGetConveyorSuccessor(
                laneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex,
                out _)
            || destinationBlock == null
            || !destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            || (destinationBlock == this && destinationLaneIndex == laneIndex))
        {
            return false;
        }

        if (destinationBlock.HasConveyorItemAtLane(destinationLaneIndex))
        {
            return false;
        }

        return true;
    }

    private void ClearConveyorLaneBlockedSleep(int laneIndex)
    {
        if (laneIndex >= 0 && laneIndex < conveyorLaneBlockedSleepStates.Length)
        {
            conveyorLaneBlockedSleepStates[laneIndex] = false;
        }
    }

    private bool IsConveyorLaneCycleBlockedSleep(int laneIndex)
    {
        return laneIndex >= 0
            && laneIndex < conveyorLaneCycleBlockedSleepStates.Length
            && conveyorLaneCycleBlockedSleepStates[laneIndex];
    }

    private bool SleepConveyorLaneCycleBlocked(int laneIndex)
    {
        if (laneIndex < 0
            || laneIndex >= conveyorLaneCycleBlockedSleepStates.Length
            || conveyorLaneCycleBlockedSleepStates[laneIndex]
            || HasConveyorMotionStates()
            || !HasConveyorItemAtLane(laneIndex)
            || WasConveyorItemMovedThisFrame(laneIndex)
            || !IsConveyorItemSettledAtLane(laneIndex))
        {
            return false;
        }

        if (ShouldKeepSoloConveyorLaneAwake(laneIndex))
        {
            return false;
        }

        conveyorLaneCycleBlockedSleepStates[laneIndex] = true;
        nextConveyorLaneMoveAttemptTimes[laneIndex] = 0f;
        return true;
    }

    private void ClearConveyorLaneCycleBlockedSleep(int laneIndex)
    {
        if (laneIndex >= 0 && laneIndex < conveyorLaneCycleBlockedSleepStates.Length)
        {
            conveyorLaneCycleBlockedSleepStates[laneIndex] = false;
        }
    }

    private bool ClearMovableConveyorLaneSleepStates()
    {
        bool hadSleepingLanes = false;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            bool laneBlockedSleep = IsConveyorLaneBlockedSleep(laneIndex);
            bool cycleBlockedSleep = IsConveyorLaneCycleBlockedSleep(laneIndex);
            if ((!laneBlockedSleep && !cycleBlockedSleep)
                || (!CanRetryConveyorLaneMove(laneIndex, true) && !ShouldKeepSoloConveyorLaneAwake(laneIndex)))
            {
                continue;
            }

            ClearConveyorLaneBlockedSleep(laneIndex);
            ClearConveyorLaneCycleBlockedSleep(laneIndex);
            hadSleepingLanes = true;
        }

        return hadSleepingLanes;
    }

    private bool ClearConveyorLaneSleepStates()
    {
        bool hadSleepingLanes = false;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            bool laneBlockedSleep = IsConveyorLaneBlockedSleep(laneIndex);
            bool cycleBlockedSleep = IsConveyorLaneCycleBlockedSleep(laneIndex);
            if (!laneBlockedSleep && !cycleBlockedSleep)
            {
                continue;
            }

            ClearConveyorLaneBlockedSleep(laneIndex);
            ClearConveyorLaneCycleBlockedSleep(laneIndex);
            hadSleepingLanes = true;
        }

        return hadSleepingLanes;
    }

    private bool IsConveyorItemSleepAwakeSleeping(int laneIndex)
    {
        if (!HasConveyorItemAtLane(laneIndex))
        {
            return false;
        }

        if (IsConveyorLaneBlockedSleep(laneIndex)
            || IsConveyorLaneCycleBlockedSleep(laneIndex))
        {
            return true;
        }

        TerrainGenerator generator = TerrainGenerator.Active;
        return generator != null && generator.IsConveyorNetworkSleeping(this);
    }

    private bool ShouldUseSleepAwakeDarkTint(int laneIndex)
    {
        return GameManager.Instance != null
            && GameManager.Instance.ShowSleepAwake
            && IsConveyorItemSleepAwakeSleeping(laneIndex);
    }

    private bool TryGetBeltItemLineDebugColor(int laneIndex, out Color32 color)
    {
        color = Color.white;
        TerrainGenerator generator = TerrainGenerator.Active;
        return GameManager.Instance != null
            && GameManager.Instance.ShowBeltItemLine
            && HasConveyorItemAtLane(laneIndex)
            && generator != null
            && generator.TryGetBeltItemLineDebugColor(this, laneIndex, out color);
    }

    private bool TryGetBeltItemLineDebugColorFast(
        TerrainGenerator generator,
        bool showBeltItemLine,
        int laneIndex,
        out Color32 color)
    {
        color = Color.white;
        return showBeltItemLine
               && generator != null
               && generator.TryGetBeltItemLineDebugColor(this, laneIndex, out color);
    }

    private void MarkConveyorItemVisualDirty()
    {
        InvalidateConveyorCanMoveCaches();
        unchecked
        {
            conveyorItemVisualVersion++;
            if (conveyorItemVisualVersion == 0)
            {
                conveyorItemVisualVersion = 1;
            }
        }

        TerrainGenerator.Active?.MarkConveyorItemVisualDirty(this);
    }

    private bool WasConveyorItemMovedThisFrame(int laneIndex)
    {
        PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
        if (portableObject != null)
        {
            return portableObject.WasMovedByConveyorThisFrame;
        }

        return laneIndex >= 0
            && laneIndex < conveyorItemMoveFrames.Count
            && conveyorItemMoveFrames[laneIndex] == Time.frameCount;
    }

    private void MarkConveyorItemMovedThisFrame(int laneIndex)
    {
        if (laneIndex < 0 || laneIndex >= conveyorItemMoveFrames.Count)
        {
            return;
        }

        conveyorItemMoveFrames[laneIndex] = Time.frameCount;
        GetConveyorPortableObjectAtLane(laneIndex)?.MarkMovedByConveyorThisFrame();
        InvalidateConveyorCanMoveCaches();
    }

    private static void InvalidateConveyorCanMoveCaches(bool invalidatePlanFailures = true)
    {
        unchecked
        {
            conveyorCanMoveGlobalStateVersion++;
            if (conveyorCanMoveGlobalStateVersion == 0)
            {
                conveyorCanMoveGlobalStateVersion = 1;
            }

            if (!invalidatePlanFailures)
            {
                return;
            }

            conveyorPlanFailureGlobalStateVersion++;
            if (conveyorPlanFailureGlobalStateVersion == 0)
            {
                conveyorPlanFailureGlobalStateVersion = 1;
            }
        }
    }

    public void MarkConveyorDroppedItem(Vector3 referenceWorldPosition, bool settled)
    {
        EnsureFloorObjectsInitialized();
        if (!IsConveyorStackingEnabled() || !TryGetClosestConveyorItemLane(referenceWorldPosition, out int laneIndex))
        {
            return;
        }

        ConveyorPickupGateState gateState = GetConveyorPickupGateStateAtLane(laneIndex);
        gateState.MarkDropped(0.5f, settled, referenceWorldPosition);
        SetConveyorPickupGateStateAtLane(laneIndex, gateState);
    }

    private bool TryGetClosestConveyorItemLane(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        return TryGetClosestConveyorItemLane(referenceWorldPosition, null, out bestLaneIndex);
    }

    private bool TryGetClosestConveyorItemLane(Vector3 referenceWorldPosition, Predicate<int> itemFilter, out int bestLaneIndex)
    {
        return TryGetClosestConveyorItemLane(referenceWorldPosition, itemFilter, -1f, out bestLaneIndex);
    }

    private bool TryGetClosestConveyorItemLane(
        Vector3 referenceWorldPosition,
        Predicate<int> itemFilter,
        float maxDistance,
        out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        float bestDistanceSqr = float.MaxValue;
        float maxDistanceSqr = maxDistance >= 0f ? maxDistance * maxDistance : float.MaxValue;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            int itemId = GetConveyorItemIdAtLane(laneIndex);
            if (itemId < 0 || (itemFilter != null && !itemFilter(itemId)))
            {
                continue;
            }

            Vector3 offset = GetConveyorItemVisualWorldPosition(laneIndex) - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > maxDistanceSqr
                || (bestLaneIndex >= 0 && distanceSqr >= bestDistanceSqr))
            {
                continue;
            }

            bestLaneIndex = laneIndex;
            bestDistanceSqr = distanceSqr;
        }

        return bestLaneIndex >= 0;
    }

    private void CleanupConveyorStack()
    {
        EnsureFloorObjectsInitialized();

        if (conveyorCornerMotionStates.Count == 0
            && conveyorLinearMotionStates.Count == 0)
        {
            return;
        }

        List<PortableObject> staleObjects = null;
        foreach (KeyValuePair<PortableObject, ConveyorCornerMotionState> pair in conveyorCornerMotionStates)
        {
            PortableObject portableObject = pair.Key;
            if (portableObject == null || !ContainsPortableObjectInConveyorStack(portableObject))
            {
                staleObjects ??= new List<PortableObject>();
                staleObjects.Add(portableObject);
            }
        }

        foreach (KeyValuePair<PortableObject, ConveyorLinearMotionState> pair in conveyorLinearMotionStates)
        {
            PortableObject portableObject = pair.Key;
            if (portableObject == null || !ContainsPortableObjectInConveyorStack(portableObject))
            {
                staleObjects ??= new List<PortableObject>();
                staleObjects.Add(portableObject);
            }
        }

        if (staleObjects == null)
        {
            return;
        }

        for (int i = 0; i < staleObjects.Count; i++)
        {
            conveyorCornerMotionStates.Remove(staleObjects[i]);
            conveyorLinearMotionStates.Remove(staleObjects[i]);
        }
    }

    private bool ContainsPortableObjectInConveyorStack(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return false;
        }

        for (int laneIndex = 0; laneIndex < conveyorStack.Count; laneIndex++)
        {
            if (conveyorStack[laneIndex] == portableObject)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetBestConveyorLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        return TryGetBestConveyorLaneIndexFromRange(referenceWorldPosition, GetConveyorLaneCount(), out bestLaneIndex);
    }

    private bool TryGetBestConveyorPlacementLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        return TryGetBestConveyorLaneIndex(referenceWorldPosition, out bestLaneIndex);
    }

    private int FindBestConveyorPickupLaneIndex(Vector3 playerPosition, float pickupRadiusSqr, Vector3 gateOriginPosition, bool manualPickup, int preferredItemId = -1)
    {
        float bestDistanceSqr = float.MaxValue;
        int bestLaneIndex = -1;
        int laneCount = GetConveyorLaneCount();

        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!TryGetConveyorPickupLaneCandidate(
                    laneIndex,
                    playerPosition,
                    pickupRadiusSqr,
                    gateOriginPosition,
                    manualPickup,
                    preferredItemId,
                    out _,
                    out float distanceSqr)
                || distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestLaneIndex = laneIndex;
        }

        return bestLaneIndex;
    }

    private bool TryGetConveyorPickupLaneCandidate(
        int laneIndex,
        Vector3 playerPosition,
        float pickupRadiusSqr,
        Vector3 gateOriginPosition,
        bool manualPickup,
        int preferredItemId,
        out int itemId,
        out float distanceSqr)
    {
        itemId = -1;
        distanceSqr = float.MaxValue;

        PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
        itemId = GetConveyorItemIdAtLane(laneIndex);
        Transform anchor = floorObjects != null && laneIndex < floorObjects.Count ? floorObjects[laneIndex] : null;
        if (itemId < 0 || anchor == null)
        {
            return false;
        }

        if (preferredItemId >= 0 && itemId != preferredItemId)
        {
            return false;
        }

        Vector3 itemWorldPosition = GetConveyorItemVisualWorldPosition(laneIndex);
        DroppedItemPickupGate gate = portableObject != null ? portableObject.GetComponent<DroppedItemPickupGate>() : null;
        if (gate != null)
        {
            gate.UpdateExitState(gateOriginPosition);
        }
        else
        {
            ConveyorPickupGateState gateState = GetConveyorPickupGateStateAtLane(laneIndex);
            gateState.UpdateExitState(gateOriginPosition, itemWorldPosition);
            SetConveyorPickupGateStateAtLane(laneIndex, gateState);
        }

        Vector3 offset = itemWorldPosition - playerPosition;
        offset.y = 0f;
        distanceSqr = offset.sqrMagnitude;
        if (distanceSqr > pickupRadiusSqr)
        {
            return false;
        }

        if (gate != null)
        {
            return manualPickup
                ? gate.CanManualPickup(distanceSqr, pickupRadiusSqr)
                : gate.CanPickup(distanceSqr, pickupRadiusSqr);
        }

        ConveyorPickupGateState candidateGateState = GetConveyorPickupGateStateAtLane(laneIndex);
        return manualPickup
            ? candidateGateState.CanManualPickup(distanceSqr, pickupRadiusSqr)
            : candidateGateState.CanPickup(distanceSqr, pickupRadiusSqr);
    }

    private void UpdateConveyorObjects(float deltaTime)
    {
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled() || !HasAnyConveyorObjects())
        {
            RefreshConveyorActivityRegistration();
            return;
        }

        float conveyorSpeed = GetConveyorSpeed();
        if (ShouldThrottleBlockedConveyorMoveAttempts())
        {
            return;
        }

        UpdateConveyorObjectWorldPositions(conveyorSpeed, deltaTime);
        bool movedAny = false;
        if (IsCornerConveyor())
        {
            movedAny |= TryMoveConveyorLane(ConveyorSingleLineFrontLaneIndex, out _, out _, true);
            if (TryGetConveyorCornerLaneCandidates(
                    out int outerSourceLaneIndex,
                    out _,
                    out _,
                    out _))
            {
                movedAny |= TryMoveConveyorLane(outerSourceLaneIndex, out _, out _, true);
            }

            AdvanceStartedCornerConveyorMotionStates(conveyorSpeed, deltaTime);
            AdvanceStartedLinearConveyorMotionStates(conveyorSpeed, deltaTime);
            UpdateConveyorBlockedRetry(movedAny);
            RefreshConveyorActivityRegistration();
            return;
        }

        if (!TryGetConveyorLaneLayout(out int frontLaneIndex, out int backLaneIndex))
        {
            UpdateConveyorBlockedRetry(false);
            RefreshConveyorActivityRegistration();
            return;
        }

        movedAny |= TryMoveConveyorLane(frontLaneIndex);
        movedAny |= TryMoveConveyorLane(backLaneIndex);
        AdvanceStartedLinearConveyorMotionStates(conveyorSpeed, deltaTime);
        UpdateConveyorBlockedRetry(movedAny);
        RefreshConveyorActivityRegistration();
    }

    private bool ShouldThrottleBlockedConveyorMoveAttempts()
    {
        return !HasConveyorMotionStates()
            && nextConveyorMoveAttemptTime > 0f
            && Time.time < nextConveyorMoveAttemptTime;
    }

    private void UpdateConveyorBlockedRetry(bool movedAny)
    {
        if (movedAny || HasConveyorMotionStates())
        {
            SleepConveyorMoveAttempts();
            WakeConveyorMoveAttempts();
            return;
        }

        if (!HasUnsettledConveyorObjects())
        {
            SleepConveyorMoveAttempts();
            return;
        }

        nextConveyorMoveAttemptTime = Time.time + GetConveyorBlockedRetryDelay();
    }

    private bool IsConveyorLaneMoveAttemptThrottled(int laneIndex)
    {
        return laneIndex >= 0
            && laneIndex < nextConveyorLaneMoveAttemptTimes.Length
            && nextConveyorLaneMoveAttemptTimes[laneIndex] > 0f
            && Time.time < nextConveyorLaneMoveAttemptTimes[laneIndex];
    }

    private void DelayConveyorLaneMoveAttempt(int laneIndex, float delay)
    {
        if (laneIndex < 0 || laneIndex >= nextConveyorLaneMoveAttemptTimes.Length)
        {
            return;
        }

        float retryTime = Time.time + Mathf.Max(0f, delay);
        if (retryTime > nextConveyorLaneMoveAttemptTimes[laneIndex])
        {
            nextConveyorLaneMoveAttemptTimes[laneIndex] = retryTime;
            InvalidateConveyorCanMoveCaches(false);
        }
    }

    private void MarkConveyorLaneCycleBlocked(HashSet<ConveyorLaneKey> blockedLanes)
    {
        if (blockedLanes == null)
        {
            return;
        }

        foreach (ConveyorLaneKey blockedLane in blockedLanes)
        {
            Block blockedBlock = blockedLane.Block;
            if (blockedBlock == null)
            {
                continue;
            }

            if (blockedBlock.SleepConveyorLaneCycleBlocked(blockedLane.LaneIndex))
            {
                blockedBlock.RefreshConveyorActivityRegistration();
            }
        }

        TerrainGenerator.Active?.QueueConveyorNetworkSleepCheck(this);
    }

    private bool HasConveyorMotionStates()
    {
        return HasPortableConveyorMotionStates() || HasConveyorDataMotionStates();
    }

    private bool HasPortableConveyorMotionStates()
    {
        return conveyorCornerMotionStates.Count > 0 || conveyorLinearMotionStates.Count > 0;
    }

    private bool HasConveyorDataMotionStates()
    {
        for (int i = 0; i < conveyorItemMotionStates.Count; i++)
        {
            if (IsValidConveyorLaneIndex(i) && conveyorItemMotionStates[i].active)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasUnsettledConveyorObjects()
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!HasConveyorItemAtLane(laneIndex))
            {
                continue;
            }

            if (!IsConveyorItemSettledAtLane(laneIndex))
            {
                return true;
            }
        }

        return false;
    }

    private float GetConveyorBlockedRetryDelay()
    {
        uint hash = unchecked((uint)((coordinate.x * 73856093) ^ (coordinate.y * 19349663)));
        float jitter = (hash % ConveyorBlockedRetryJitterSteps) * ConveyorBlockedRetryJitterStep;
        return ConveyorBlockedRetryInterval + jitter;
    }

    private bool HasAnyConveyorObjects()
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (HasConveyorItemAtLane(laneIndex))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyConveyorObjectsNotMoveAttemptSleeping()
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (HasConveyorItemAtLane(laneIndex)
                && !IsConveyorLaneBlockedSleep(laneIndex)
                && !IsConveyorLaneCycleBlockedSleep(laneIndex))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMovableConveyorLaneSleepState()
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (CanWakeSleepingConveyorLaneCheap(laneIndex))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanWakeSleepingConveyorLaneCheap(int laneIndex)
    {
        return (IsConveyorLaneBlockedSleep(laneIndex) || IsConveyorLaneCycleBlockedSleep(laneIndex))
            && HasConveyorItemAtLane(laneIndex)
            && !WasConveyorItemMovedThisFrame(laneIndex)
            && IsConveyorItemReadyToMoveAtLane(laneIndex)
            && (CanMoveConveyorLaneDirect(laneIndex)
                || CanMoveConveyorLaneToUnloadedSuccessor(laneIndex)
                || ShouldKeepSoloConveyorLaneAwake(laneIndex));
    }

    private bool HasMaterializedConveyorItems()
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (IsValidConveyorLaneIndex(laneIndex)
                && GetConveyorPortableObjectAtLane(laneIndex) != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasActiveConveyorMotion()
    {
        CleanupConveyorStack();
        return IsConveyorStackingEnabled()
            && ((!IsConveyorNetworkMoveAttemptThrottled() && HasAnyConveyorObjectsNotMoveAttemptSleeping())
                || HasConveyorMotionStates()
                || HasMovableConveyorLaneSleepState());
    }

    private bool IsConveyorNetworkMoveAttemptThrottled()
    {
        TerrainGenerator generator = TerrainGenerator.Active;
        return generator != null && generator.IsConveyorNetworkMoveThrottled(this);
    }

    private float GetConveyorSpeed()
    {
        return TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt) ? conveyorBelt.ConveyorSpeed : 0f;
    }

    private float ResolveConveyorDataMotionDurationPathLength(ConveyorDataMotionState motionState)
    {
        return motionState.useCornerMotion
            ? ResolveConveyorCornerMotionDurationPathLength(
                motionState.sourceLaneIndex,
                motionState.destinationLaneIndex,
                motionState.startWorldPosition,
                motionState.pathLength,
                motionState.durationPathLength)
            : motionState.pathLength;
    }

    private ConveyorDataMotionState InitializeConveyorDataMotionTiming(
        ConveyorDataMotionState motionState,
        float progress)
    {
        float conveyorSpeed = GetConveyorSpeed();
        float durationPathLength = ResolveConveyorDataMotionDurationPathLength(motionState);
        float duration = conveyorSpeed > ConveyorContinuousMotionEpsilon && durationPathLength > ConveyorContinuousMotionEpsilon
            ? durationPathLength / conveyorSpeed
            : 0f;
        float clampedProgress = Mathf.Clamp01(progress);
        float now = Time.time;
        motionState.progress = clampedProgress;
        motionState.duration = duration;
        motionState.startTime = duration > ConveyorContinuousMotionEpsilon
            ? now - (duration * clampedProgress)
            : now;
        return motionState;
    }

    private ConveyorDataMotionState EnsureConveyorDataMotionTiming(ConveyorDataMotionState motionState)
    {
        if (!motionState.active)
        {
            return motionState;
        }

        if (motionState.duration > ConveyorContinuousMotionEpsilon && motionState.startTime > 0f)
        {
            return motionState;
        }

        return InitializeConveyorDataMotionTiming(motionState, motionState.progress);
    }

    private float EvaluateConveyorDataMotionProgress(ConveyorDataMotionState motionState)
    {
        if (!motionState.active)
        {
            return 0f;
        }

        if (motionState.startTime <= 0f)
        {
            return Mathf.Clamp01(motionState.progress);
        }

        if (motionState.duration <= ConveyorContinuousMotionEpsilon)
        {
            return 1f;
        }

        return Mathf.Clamp01((Time.time - motionState.startTime) / motionState.duration);
    }

    private float GetConveyorDataMotionCompletionTime(ConveyorDataMotionState motionState, float now)
    {
        if (!motionState.active)
        {
            return float.PositiveInfinity;
        }

        if (motionState.duration <= ConveyorContinuousMotionEpsilon)
        {
            return now;
        }

        return motionState.startTime > 0f
            ? motionState.startTime + motionState.duration
            : now;
    }

    public float GetNextVirtualConveyorDataMotionCompletionTime()
    {
        if (!HasActiveVirtualConveyorDataMotion())
        {
            return float.PositiveInfinity;
        }

        float now = Time.time;
        float nextCompletionTime = float.PositiveInfinity;
        int laneCount = Mathf.Min(GetConveyorLaneCount(), conveyorItemMotionStates.Count);
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!IsValidConveyorLaneIndex(laneIndex))
            {
                continue;
            }

            ConveyorDataMotionState motionState = conveyorItemMotionStates[laneIndex];
            if (!motionState.active)
            {
                continue;
            }

            ConveyorDataMotionState timedMotionState = EnsureConveyorDataMotionTiming(motionState);
            if (timedMotionState.startTime != motionState.startTime || timedMotionState.duration != motionState.duration)
            {
                conveyorItemMotionStates[laneIndex] = timedMotionState;
                motionState = timedMotionState;
            }

            nextCompletionTime = Mathf.Min(
                nextCompletionTime,
                GetConveyorDataMotionCompletionTime(motionState, now));
        }

        return nextCompletionTime;
    }

    private bool ShouldUseVirtualConveyorItemRendering()
    {
        if (!Application.isPlaying)
        {
            return false;
        }

        TerrainGenerator generator = cachedTerrainGenerator;
        if (generator == null)
        {
            TryResolveOwningTerrainGenerator(out generator);
        }

        return generator != null && generator.VirtualizeConveyorItems;
    }

    private static bool ShouldHideBeltItemRendering()
    {
        return GameManager.Instance != null && GameManager.Instance.HideBeltItems;
    }

    private void ApplyConveyorObjectRenderingMode(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (ShouldHideBeltItemRendering())
        {
            if (!portableObject.IsVisualRenderingSuppressed)
            {
                portableObject.SetVisualRenderingSuppressed(true);
            }

            return;
        }

        if (ShouldUseVirtualConveyorItemRendering())
        {
            if (!portableObject.IsVisualRenderingSuppressed)
            {
                portableObject.SetVisualRenderingSuppressed(true);
            }

            if (!portableObject.IsMovingToTarget && portableObject.CachedGameObject.activeSelf)
            {
                DroppedItemPickupGate gate = portableObject.PickupGate;
                gate?.SetPreserveStateOnDisable(true);
                portableObject.SetCachedActive(false);
            }

            return;
        }

        if (!portableObject.CachedGameObject.activeSelf)
        {
            portableObject.SetCachedActive(true);
        }

        if (portableObject.IsVisualRenderingSuppressed)
        {
            portableObject.SetVisualRenderingSuppressed(false);
        }

        if (!portableObject.IsUsingBatchedRendering)
        {
            portableObject.SetBatchedRendering(true);
        }
    }

    private void ApplyConveyorObjectVirtualRenderingSuppressionIfNeeded(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (!portableObject.IsVisualRenderingSuppressed)
        {
            portableObject.SetVisualRenderingSuppressed(true);
        }

        if (!portableObject.IsMovingToTarget && portableObject.CachedGameObject.activeSelf)
        {
            DroppedItemPickupGate gate = portableObject.PickupGate;
            gate?.SetPreserveStateOnDisable(true);
            portableObject.SetCachedActive(false);
        }
    }

    public void RefreshConveyorObjectRenderingMode()
    {
        if (conveyorStack == null || conveyorStack.Count <= 0)
        {
            return;
        }

        for (int laneIndex = 0; laneIndex < conveyorStack.Count; laneIndex++)
        {
            ApplyConveyorObjectRenderingMode(conveyorStack[laneIndex]);
        }

        MarkConveyorItemVisualDirty();
    }

    private bool TryVirtualizeSettledConveyorPortableObject(int laneIndex, PortableObject portableObject)
    {
        if (portableObject == null
            || portableObject.IsMovingToTarget
            || !ShouldUseVirtualConveyorItemRendering()
            || !IsValidConveyorLaneIndex(laneIndex)
            || GetConveyorPortableObjectAtLane(laneIndex) != portableObject
            || GetConveyorItemIdAtLane(laneIndex) < 0)
        {
            return false;
        }

        conveyorCornerMotionStates.Remove(portableObject);
        conveyorLinearMotionStates.Remove(portableObject);
        conveyorStack[laneIndex] = null;
        portableObject.SetSleepAwakeSleeping(false);
        ReleaseFloorObject(portableObject);
        MarkConveyorItemVisualDirty();
        TerrainGenerator.Active?.MarkConveyorLineCacheDirty();
        TerrainGenerator.Active?.MarkBeltItemLineDebugDirty(this);
        return true;
    }

    private Vector3 GetConveyorObjectVisualWorldPosition(int laneIndex, PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return GetConveyorItemVisualWorldPosition(laneIndex);
        }

        if (conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState cornerMotionState))
        {
            return EvaluateConveyorCornerMotionWorldPosition(
                cornerMotionState.sourceLaneIndex,
                cornerMotionState.destinationLaneIndex,
                cornerMotionState.startWorldPosition,
                cornerMotionState.pathLength,
                cornerMotionState.progress);
        }

        if (conveyorLinearMotionStates.TryGetValue(portableObject, out ConveyorLinearMotionState linearMotionState))
        {
            Vector3 targetPosition = GetConveyorLaneWorldPosition(linearMotionState.destinationLaneIndex);
            return EvaluateConveyorLinearMotionWorldPosition(
                linearMotionState.cornerContinuation,
                linearMotionState.startWorldPosition,
                linearMotionState.hasViaWorldPosition,
                linearMotionState.viaWorldPosition,
                targetPosition,
                linearMotionState.pathLength,
                linearMotionState.progress);
        }

        return ShouldUseVirtualConveyorItemRendering()
            ? GetConveyorLaneWorldPosition(laneIndex)
            : portableObject.WorldPosition;
    }

    private Vector3 GetConveyorItemVisualWorldPosition(int laneIndex)
    {
        return GetConveyorItemVisualWorldPosition(laneIndex, GetConveyorPortableObjectAtLane(laneIndex));
    }

    private Vector3 GetConveyorItemVisualWorldPosition(int laneIndex, PortableObject portableObject)
    {
        if (portableObject != null)
        {
            return GetConveyorObjectVisualWorldPosition(laneIndex, portableObject);
        }

        if (laneIndex >= 0
            && laneIndex < conveyorItemMotionStates.Count
            && conveyorItemMotionStates[laneIndex].active)
        {
            ConveyorDataMotionState originalMotionState = conveyorItemMotionStates[laneIndex];
            ConveyorDataMotionState motionState = EnsureConveyorDataMotionTiming(originalMotionState);
            if (motionState.startTime != originalMotionState.startTime || motionState.duration != originalMotionState.duration)
            {
                conveyorItemMotionStates[laneIndex] = motionState;
            }

            float motionProgress = EvaluateConveyorDataMotionProgress(motionState);
            if (motionState.useCornerMotion)
            {
                return EvaluateConveyorCornerMotionWorldPosition(
                    motionState.sourceLaneIndex,
                    motionState.destinationLaneIndex,
                    motionState.startWorldPosition,
                    motionState.pathLength,
                    motionProgress);
            }

            Vector3 targetPosition = GetConveyorLaneWorldPosition(motionState.destinationLaneIndex);
            return EvaluateConveyorLinearMotionWorldPosition(
                motionState.cornerContinuation,
                motionState.startWorldPosition,
                motionState.hasViaWorldPosition,
                motionState.viaWorldPosition,
                targetPosition,
                motionState.pathLength,
                motionProgress);
        }

        return GetConveyorLaneWorldPosition(laneIndex);
    }

    private Quaternion GetConveyorItemVisualWorldRotation(int laneIndex, Vector3 worldPosition)
    {
        return ResolveConveyorItemWorldRotation(laneIndex, worldPosition);
    }

    private void SetConveyorPortableObjectWorldPose(PortableObject portableObject, int laneIndex, Vector3 worldPosition)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.SetWorldPose(worldPosition, ResolveConveyorItemWorldRotation(laneIndex, worldPosition));
    }

    private Quaternion ResolveConveyorItemWorldRotation(int laneIndex, Vector3 worldPosition)
    {
        if (TryGetConveyorItemBelt2F(laneIndex, out ConvayorBelt2F belt2F))
        {
            return belt2F.ResolvePathItemRotation(worldPosition);
        }

        return Quaternion.identity;
    }

    private bool TryGetConveyorItemBelt2F(int laneIndex, out ConvayorBelt2F belt2F)
    {
        belt2F = null;
        if (IsBelt2FBridgeLaneIndex(laneIndex)
            && TryGetBelt2FBridgeCenterBelt(out belt2F))
        {
            return true;
        }

        if (TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt)
            && conveyorBelt is ConvayorBelt2F resolvedBelt2F)
        {
            belt2F = resolvedBelt2F;
            return true;
        }

        return false;
    }

    private float GetConveyorCornerContinuationRemainingPathLength(ConveyorCornerContinuation continuation)
    {
        if (!continuation.active || continuation.block == null)
        {
            return 0f;
        }

        float pathLength = continuation.block.ResolveConveyorCornerMotionDurationPathLength(
            continuation.sourceLaneIndex,
            continuation.destinationLaneIndex,
            continuation.startWorldPosition,
            continuation.pathLength,
            continuation.durationPathLength);
        if (pathLength <= ConveyorContinuousMotionEpsilon)
        {
            return 0f;
        }

        return pathLength * (1f - Mathf.Clamp01(continuation.startProgress));
    }

    private Vector3 GetConveyorCornerContinuationEndWorldPosition(
        ConveyorCornerContinuation continuation,
        Vector3 fallbackWorldPosition)
    {
        if (!continuation.active || continuation.block == null)
        {
            return fallbackWorldPosition;
        }

        return continuation.block.EvaluateConveyorCornerMotionWorldPosition(
            continuation.sourceLaneIndex,
            continuation.destinationLaneIndex,
            continuation.startWorldPosition,
            continuation.pathLength,
            1f);
    }

    private Vector3 EvaluateConveyorCornerContinuationWorldPosition(
        ConveyorCornerContinuation continuation,
        float distance)
    {
        if (!continuation.active || continuation.block == null)
        {
            return Vector3.zero;
        }

        float durationPathLength = continuation.block.ResolveConveyorCornerMotionDurationPathLength(
            continuation.sourceLaneIndex,
            continuation.destinationLaneIndex,
            continuation.startWorldPosition,
            continuation.pathLength,
            continuation.durationPathLength);
        if (durationPathLength <= ConveyorContinuousMotionEpsilon)
        {
            return continuation.block.EvaluateConveyorCornerMotionWorldPosition(
                continuation.sourceLaneIndex,
                continuation.destinationLaneIndex,
                continuation.startWorldPosition,
                continuation.pathLength,
                1f);
        }

        float currentDistance = Mathf.Clamp01(continuation.startProgress) * durationPathLength;
        float progress = Mathf.Clamp01((currentDistance + Mathf.Max(0f, distance)) / durationPathLength);
        return continuation.block.EvaluateConveyorCornerMotionWorldPosition(
            continuation.sourceLaneIndex,
            continuation.destinationLaneIndex,
            continuation.startWorldPosition,
            continuation.pathLength,
            progress);
    }

    private Vector3 EvaluateConveyorLinearMotionWorldPosition(
        ConveyorCornerContinuation cornerContinuation,
        Vector3 startWorldPosition,
        bool hasViaWorldPosition,
        Vector3 viaWorldPosition,
        Vector3 targetWorldPosition,
        float pathLength,
        float progress)
    {
        float cornerRemainingPathLength = GetConveyorCornerContinuationRemainingPathLength(cornerContinuation);
        if (cornerRemainingPathLength <= ConveyorContinuousMotionEpsilon)
        {
            return EvaluateConveyorLinearMotionWorldPosition(
                startWorldPosition,
                hasViaWorldPosition,
                viaWorldPosition,
                targetWorldPosition,
                pathLength,
                progress);
        }

        float resolvedPathLength = pathLength > ConveyorContinuousMotionEpsilon
            ? pathLength
            : cornerRemainingPathLength;
        float distance = Mathf.Clamp01(progress) * resolvedPathLength;
        if (distance < cornerRemainingPathLength)
        {
            return EvaluateConveyorCornerContinuationWorldPosition(cornerContinuation, distance);
        }

        Vector3 linearStartWorldPosition = GetConveyorCornerContinuationEndWorldPosition(
            cornerContinuation,
            startWorldPosition);
        float linearPathLength = Mathf.Max(0f, resolvedPathLength - cornerRemainingPathLength);
        if (linearPathLength <= ConveyorContinuousMotionEpsilon)
        {
            return linearStartWorldPosition;
        }

        return EvaluateConveyorLinearMotionWorldPosition(
            linearStartWorldPosition,
            hasViaWorldPosition,
            viaWorldPosition,
            targetWorldPosition,
            linearPathLength,
            Mathf.Clamp01((distance - cornerRemainingPathLength) / linearPathLength));
    }

    private Vector3 EvaluateConveyorLinearMotionWorldPosition(
        Vector3 startWorldPosition,
        bool hasViaWorldPosition,
        Vector3 viaWorldPosition,
        Vector3 targetWorldPosition,
        float pathLength,
        float progress)
    {
        if (!hasViaWorldPosition)
        {
            return Vector3.Lerp(startWorldPosition, targetWorldPosition, progress);
        }

        if (progress >= 1f - ConveyorContinuousMotionEpsilon)
        {
            return targetWorldPosition;
        }

        float firstLength = Vector3.Distance(startWorldPosition, viaWorldPosition);
        float resolvedPathLength = pathLength > ConveyorContinuousMotionEpsilon
            ? pathLength
            : firstLength + Vector3.Distance(viaWorldPosition, targetWorldPosition);
        if (resolvedPathLength <= ConveyorContinuousMotionEpsilon)
        {
            return targetWorldPosition;
        }

        float distance = Mathf.Clamp01(progress) * resolvedPathLength;
        float firstDurationLength = Mathf.Min(firstLength, resolvedPathLength);
        if (firstDurationLength > ConveyorContinuousMotionEpsilon && distance < firstDurationLength)
        {
            return Vector3.Lerp(startWorldPosition, viaWorldPosition, distance / firstDurationLength);
        }

        float secondDurationLength = Mathf.Max(0f, resolvedPathLength - firstDurationLength);
        if (secondDurationLength <= ConveyorContinuousMotionEpsilon)
        {
            return targetWorldPosition;
        }

        return Vector3.Lerp(viaWorldPosition, targetWorldPosition, Mathf.Clamp01((distance - firstDurationLength) / secondDurationLength));
    }

    private void UpdateConveyorObjectWorldPositions(float conveyorSpeed, float deltaTime)
    {
        if (ShouldUseVirtualConveyorItemRendering())
        {
            UpdateVirtualConveyorObjectMotionStates(conveyorSpeed, deltaTime);
            return;
        }

        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!IsValidConveyorLaneIndex(laneIndex))
            {
                continue;
            }

            PortableObject portableObject = conveyorStack[laneIndex];
            if (portableObject == null)
            {
                continue;
            }

            Transform portableTransform = portableObject.CachedTransform;
            if (portableTransform.parent != transform)
            {
                portableObject.SetCachedParent(transform, true);
            }

            if (portableObject.IsMovingToTarget)
            {
                continue;
            }

            ApplyConveyorObjectRenderingMode(portableObject);
            if (TryUpdateCornerConveyorObjectWorldPosition(laneIndex, portableObject, conveyorSpeed, deltaTime))
            {
                continue;
            }

            if (TryUpdateLinearConveyorObjectWorldPosition(laneIndex, portableObject, conveyorSpeed, deltaTime))
            {
                continue;
            }

            Vector3 targetPosition = GetConveyorLaneWorldPosition(laneIndex);
            Vector3 currentOffset = portableObject.WorldPosition - targetPosition;
            currentOffset.y = 0f;
            if (currentOffset.sqrMagnitude <= ConveyorLaneSettleEpsilon * ConveyorLaneSettleEpsilon)
            {
                continue;
            }

            if (conveyorSpeed <= 0f || deltaTime <= 0f)
            {
                SetConveyorPortableObjectWorldPose(portableObject, laneIndex, targetPosition);
                continue;
            }

            SetConveyorPortableObjectWorldPose(portableObject, laneIndex, Vector3.MoveTowards(
                portableObject.WorldPosition,
                targetPosition,
                conveyorSpeed * deltaTime));
        }
    }

    private void UpdateVirtualConveyorObjectMotionStates(float conveyorSpeed, float deltaTime)
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!HasConveyorItemAtLane(laneIndex))
            {
                continue;
            }

            PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.IsMovingToTarget)
            {
                continue;
            }

            ApplyConveyorObjectRenderingMode(portableObject);
            if (TryAdvanceVirtualCornerConveyorMotion(portableObject, conveyorSpeed, deltaTime))
            {
                continue;
            }

            TryAdvanceVirtualLinearConveyorMotion(portableObject, conveyorSpeed, deltaTime);
        }

        if (HasConveyorReadyLaneForMove(false, false) || HasMovableConveyorLaneSleepState())
        {
            WakeConveyorMoveAttemptsAround();
        }
    }

    private bool TryAdvanceVirtualConveyorLaneMotion(int laneIndex, float conveyorSpeed, float deltaTime)
    {
        return TryAdvanceVirtualConveyorLaneMotion(
            laneIndex,
            conveyorSpeed,
            deltaTime,
            ConveyorContinuousMotionMaxCarrySteps);
    }

    private bool TryCompleteDueVirtualConveyorLaneMotion(
        int laneIndex,
        float conveyorSpeed,
        float now,
        float completionTime,
        int remainingCarrySteps)
    {
        if (laneIndex < 0
            || laneIndex >= conveyorItemMotionStates.Count
            || !conveyorItemMotionStates[laneIndex].active)
        {
            return false;
        }

        ConveyorDataMotionState motionState = EnsureConveyorDataMotionTiming(conveyorItemMotionStates[laneIndex]);
        float durationPathLength = ResolveConveyorDataMotionDurationPathLength(motionState);
        if (durationPathLength <= ConveyorContinuousMotionEpsilon || conveyorSpeed <= ConveyorContinuousMotionEpsilon)
        {
            conveyorItemMotionStates[laneIndex] = default;
            MarkConveyorItemVisualDirty();
            NotifyConveyorMotionSettled();
            RefreshConveyorActivityRegistration(false, false);
            return true;
        }

        if (GetConveyorDataMotionCompletionTime(motionState, now) > now + ConveyorContinuousMotionEpsilon)
        {
            conveyorItemMotionStates[laneIndex] = motionState;
            return false;
        }

        conveyorItemMotionStates[laneIndex] = default;
        MarkConveyorItemVisualDirty();
        NotifyConveyorMotionSettled();

        float carryDeltaTime = Mathf.Max(0f, now - completionTime);
        if (carryDeltaTime > ConveyorContinuousMotionEpsilon && remainingCarrySteps > 0)
        {
            TryContinueConveyorItemMotion(
                laneIndex,
                null,
                conveyorSpeed,
                carryDeltaTime,
                remainingCarrySteps - 1);
        }

        RefreshConveyorActivityRegistration(false, false);
        return true;
    }

    private bool TryAdvanceVirtualConveyorLaneMotion(
        int laneIndex,
        float conveyorSpeed,
        float deltaTime,
        int remainingCarrySteps)
    {
        if (laneIndex < 0
            || laneIndex >= conveyorItemMotionStates.Count
            || !conveyorItemMotionStates[laneIndex].active)
        {
            return false;
        }

        ConveyorDataMotionState motionState = EnsureConveyorDataMotionTiming(conveyorItemMotionStates[laneIndex]);
        float pathLength = ResolveConveyorDataMotionDurationPathLength(motionState);
        if (pathLength <= 0.0001f || conveyorSpeed <= 0f || deltaTime <= 0f)
        {
            conveyorItemMotionStates[laneIndex] = default;
            MarkConveyorItemVisualDirty();
            NotifyConveyorMotionSettled();
            RefreshConveyorActivityRegistration(false, false);
            return true;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float currentProgress = EvaluateConveyorDataMotionProgress(motionState);
        float remainingPathDistance = pathLength * (1f - currentProgress);
        if (travelDistance + ConveyorContinuousMotionEpsilon < remainingPathDistance)
        {
            motionState = InitializeConveyorDataMotionTiming(
                motionState,
                Mathf.Clamp01(currentProgress + (travelDistance / pathLength)));
            conveyorItemMotionStates[laneIndex] = motionState;
            MarkConveyorItemVisualDirty();
            RefreshConveyorActivityRegistration(false, false);
            return true;
        }

        conveyorItemMotionStates[laneIndex] = default;
        MarkConveyorItemVisualDirty();
        NotifyConveyorMotionSettled();

        float carryDeltaTime = conveyorSpeed > ConveyorContinuousMotionEpsilon
            ? Mathf.Max(0f, travelDistance - remainingPathDistance) / conveyorSpeed
            : 0f;
        if (carryDeltaTime > ConveyorContinuousMotionEpsilon && remainingCarrySteps > 0)
        {
            TryContinueConveyorItemMotion(
                laneIndex,
                null,
                conveyorSpeed,
                carryDeltaTime,
                remainingCarrySteps - 1);
        }

        RefreshConveyorActivityRegistration(false, false);
        return true;
    }

    private bool TryAdvanceVirtualLinearConveyorMotion(PortableObject portableObject, float conveyorSpeed, float deltaTime)
    {
        return TryAdvanceVirtualLinearConveyorMotion(
            portableObject,
            conveyorSpeed,
            deltaTime,
            ConveyorContinuousMotionMaxCarrySteps);
    }

    private bool TryAdvanceVirtualLinearConveyorMotion(
        PortableObject portableObject,
        float conveyorSpeed,
        float deltaTime,
        int remainingCarrySteps)
    {
        if (portableObject == null || !conveyorLinearMotionStates.TryGetValue(portableObject, out ConveyorLinearMotionState motionState))
        {
            return false;
        }

        if (motionState.pathLength <= 0.0001f || conveyorSpeed <= 0f || deltaTime <= 0f)
        {
            conveyorLinearMotionStates.Remove(portableObject);
            MarkConveyorItemVisualDirty();
            NotifyConveyorMotionSettled();
            return true;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float remainingPathDistance = motionState.pathLength * (1f - Mathf.Clamp01(motionState.progress));
        if (travelDistance + ConveyorContinuousMotionEpsilon < remainingPathDistance)
        {
            motionState.progress = Mathf.Clamp01(motionState.progress + (travelDistance / motionState.pathLength));
            conveyorLinearMotionStates[portableObject] = motionState;
            MarkConveyorItemVisualDirty();
            return true;
        }

        conveyorLinearMotionStates.Remove(portableObject);
        MarkConveyorItemVisualDirty();
        NotifyConveyorMotionSettled();

        float carryDeltaTime = conveyorSpeed > ConveyorContinuousMotionEpsilon
            ? Mathf.Max(0f, travelDistance - remainingPathDistance) / conveyorSpeed
            : 0f;
        if (carryDeltaTime > ConveyorContinuousMotionEpsilon && remainingCarrySteps > 0)
        {
            int laneIndex = FindConveyorLaneIndexForPortableObject(portableObject);
            TryContinueConveyorItemMotion(
                laneIndex,
                portableObject,
                conveyorSpeed,
                carryDeltaTime,
                remainingCarrySteps - 1);
        }

        return true;
    }

    private bool TryAdvanceVirtualCornerConveyorMotion(PortableObject portableObject, float conveyorSpeed, float deltaTime)
    {
        return TryAdvanceVirtualCornerConveyorMotion(
            portableObject,
            conveyorSpeed,
            deltaTime,
            ConveyorContinuousMotionMaxCarrySteps);
    }

    private bool TryAdvanceVirtualCornerConveyorMotion(
        PortableObject portableObject,
        float conveyorSpeed,
        float deltaTime,
        int remainingCarrySteps)
    {
        if (!IsCornerConveyor()
            || portableObject == null
            || !conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState motionState))
        {
            return false;
        }

        float durationPathLength = ResolveConveyorCornerMotionDurationPathLength(
            motionState.sourceLaneIndex,
            motionState.destinationLaneIndex,
            motionState.startWorldPosition,
            motionState.pathLength,
            motionState.durationPathLength);
        if (durationPathLength <= 0.0001f || conveyorSpeed <= 0f || deltaTime <= 0f)
        {
            conveyorCornerMotionStates.Remove(portableObject);
            MarkConveyorItemVisualDirty();
            NotifyConveyorMotionSettled();
            return true;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float remainingPathDistance = durationPathLength * (1f - Mathf.Clamp01(motionState.progress));
        if (travelDistance + ConveyorContinuousMotionEpsilon < remainingPathDistance)
        {
            motionState.progress = Mathf.Clamp01(motionState.progress + (travelDistance / durationPathLength));
            conveyorCornerMotionStates[portableObject] = motionState;
            MarkConveyorItemVisualDirty();
            return true;
        }

        conveyorCornerMotionStates.Remove(portableObject);
        MarkConveyorItemVisualDirty();
        NotifyConveyorMotionSettled();

        float carryDeltaTime = conveyorSpeed > ConveyorContinuousMotionEpsilon
            ? Mathf.Max(0f, travelDistance - remainingPathDistance) / conveyorSpeed
            : 0f;
        if (carryDeltaTime > ConveyorContinuousMotionEpsilon && remainingCarrySteps > 0)
        {
            int laneIndex = FindConveyorLaneIndexForPortableObject(portableObject);
            TryContinueConveyorItemMotion(
                laneIndex,
                portableObject,
                conveyorSpeed,
                carryDeltaTime,
                remainingCarrySteps - 1);
        }

        return true;
    }

    private bool TryUpdateLinearConveyorObjectWorldPosition(int laneIndex, PortableObject portableObject, float conveyorSpeed, float deltaTime)
    {
        return TryUpdateLinearConveyorObjectWorldPosition(
            laneIndex,
            portableObject,
            conveyorSpeed,
            deltaTime,
            ConveyorContinuousMotionMaxCarrySteps);
    }

    private bool TryUpdateLinearConveyorObjectWorldPosition(
        int laneIndex,
        PortableObject portableObject,
        float conveyorSpeed,
        float deltaTime,
        int remainingCarrySteps)
    {
        if (portableObject == null
            || !conveyorLinearMotionStates.TryGetValue(portableObject, out ConveyorLinearMotionState motionState))
        {
            return false;
        }

        Vector3 targetPosition = GetConveyorLaneWorldPosition(motionState.destinationLaneIndex);
        if (motionState.pathLength <= 0.0001f || conveyorSpeed <= 0f || deltaTime <= 0f)
        {
            SetConveyorPortableObjectWorldPose(portableObject, laneIndex, targetPosition);
            conveyorLinearMotionStates.Remove(portableObject);
            NotifyConveyorMotionSettled();
            return true;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float remainingPathDistance = motionState.pathLength * (1f - Mathf.Clamp01(motionState.progress));
        if (travelDistance + ConveyorContinuousMotionEpsilon < remainingPathDistance)
        {
            motionState.progress = Mathf.Clamp01(motionState.progress + (travelDistance / motionState.pathLength));
            SetConveyorPortableObjectWorldPose(portableObject, laneIndex, EvaluateConveyorLinearMotionWorldPosition(
                motionState.cornerContinuation,
                motionState.startWorldPosition,
                motionState.hasViaWorldPosition,
                motionState.viaWorldPosition,
                targetPosition,
                motionState.pathLength,
                motionState.progress));
            conveyorLinearMotionStates[portableObject] = motionState;
            return true;
        }

        SetConveyorPortableObjectWorldPose(portableObject, laneIndex, targetPosition);
        conveyorLinearMotionStates.Remove(portableObject);
        NotifyConveyorMotionSettled();

        float carryDeltaTime = conveyorSpeed > ConveyorContinuousMotionEpsilon
            ? Mathf.Max(0f, travelDistance - remainingPathDistance) / conveyorSpeed
            : 0f;
        if (carryDeltaTime > ConveyorContinuousMotionEpsilon && remainingCarrySteps > 0)
        {
            TryContinueConveyorItemMotion(
                laneIndex,
                portableObject,
                conveyorSpeed,
                carryDeltaTime,
                remainingCarrySteps - 1);
        }

        return true;
    }

    private bool TryUpdateCornerConveyorObjectWorldPosition(int laneIndex, PortableObject portableObject, float conveyorSpeed, float deltaTime)
    {
        return TryUpdateCornerConveyorObjectWorldPosition(
            laneIndex,
            portableObject,
            conveyorSpeed,
            deltaTime,
            ConveyorContinuousMotionMaxCarrySteps);
    }

    private bool TryUpdateCornerConveyorObjectWorldPosition(
        int laneIndex,
        PortableObject portableObject,
        float conveyorSpeed,
        float deltaTime,
        int remainingCarrySteps)
    {
        if (!IsCornerConveyor() || portableObject == null || !conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState motionState))
        {
            return false;
        }

        float pathLength = ResolveConveyorCornerMotionPathLength(
            motionState.sourceLaneIndex,
            motionState.destinationLaneIndex,
            motionState.startWorldPosition,
            motionState.pathLength);
        float durationPathLength = ResolveConveyorCornerMotionDurationPathLength(
            motionState.sourceLaneIndex,
            motionState.destinationLaneIndex,
            motionState.startWorldPosition,
            motionState.pathLength,
            motionState.durationPathLength);
        if (durationPathLength <= 0.0001f || conveyorSpeed <= 0f || deltaTime <= 0f)
        {
            SetConveyorPortableObjectWorldPose(portableObject, laneIndex, EvaluateConveyorCornerMotionWorldPosition(
                motionState.sourceLaneIndex,
                motionState.destinationLaneIndex,
                motionState.startWorldPosition,
                pathLength,
                1f));
            conveyorCornerMotionStates.Remove(portableObject);
            NotifyConveyorMotionSettled();
            return true;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float remainingPathDistance = durationPathLength * (1f - Mathf.Clamp01(motionState.progress));
        if (travelDistance + ConveyorContinuousMotionEpsilon < remainingPathDistance)
        {
            motionState.progress = Mathf.Clamp01(motionState.progress + (travelDistance / durationPathLength));
            SetConveyorPortableObjectWorldPose(portableObject, laneIndex, EvaluateConveyorCornerMotionWorldPosition(
                motionState.sourceLaneIndex,
                motionState.destinationLaneIndex,
                motionState.startWorldPosition,
                pathLength,
                motionState.progress));
            conveyorCornerMotionStates[portableObject] = motionState;
            return true;
        }

        SetConveyorPortableObjectWorldPose(portableObject, laneIndex, EvaluateConveyorCornerMotionWorldPosition(
            motionState.sourceLaneIndex,
            motionState.destinationLaneIndex,
            motionState.startWorldPosition,
            pathLength,
            1f));
        conveyorCornerMotionStates.Remove(portableObject);
        NotifyConveyorMotionSettled();

        float carryDeltaTime = conveyorSpeed > ConveyorContinuousMotionEpsilon
            ? Mathf.Max(0f, travelDistance - remainingPathDistance) / conveyorSpeed
            : 0f;
        if (carryDeltaTime > ConveyorContinuousMotionEpsilon && remainingCarrySteps > 0)
        {
            TryContinueConveyorItemMotion(
                laneIndex,
                portableObject,
                conveyorSpeed,
                carryDeltaTime,
                remainingCarrySteps - 1);
        }

        return true;
    }

    private void AdvanceStartedCornerConveyorMotionStates(float conveyorSpeed, float deltaTime)
    {
        if (ShouldUseVirtualConveyorItemRendering())
        {
            return;
        }

        if (!IsCornerConveyor()
            || conveyorSpeed <= 0f
            || deltaTime <= 0f
            || conveyorCornerMotionStates.Count == 0)
        {
            return;
        }

        conveyorCornerMotionTickBuffer.Clear();
        foreach (KeyValuePair<PortableObject, ConveyorCornerMotionState> pair in conveyorCornerMotionStates)
        {
            if (pair.Key == null
                || !pair.Key.WasMovedByConveyorThisFrame
                || pair.Value.progress > 0.0001f)
            {
                continue;
            }

            conveyorCornerMotionTickBuffer.Add(pair.Key);
        }

        for (int i = 0; i < conveyorCornerMotionTickBuffer.Count; i++)
        {
            PortableObject portableObject = conveyorCornerMotionTickBuffer[i];
            if (portableObject == null
                || !conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState motionState))
            {
                continue;
            }

            float pathLength = ResolveConveyorCornerMotionPathLength(
                motionState.sourceLaneIndex,
                motionState.destinationLaneIndex,
                motionState.startWorldPosition,
                motionState.pathLength);
            float durationPathLength = ResolveConveyorCornerMotionDurationPathLength(
                motionState.sourceLaneIndex,
                motionState.destinationLaneIndex,
                motionState.startWorldPosition,
                motionState.pathLength,
                motionState.durationPathLength);
            int laneIndex = FindConveyorLaneIndexForPortableObject(portableObject);
            if (durationPathLength <= 0.0001f)
            {
                SetConveyorPortableObjectWorldPose(portableObject, laneIndex, EvaluateConveyorCornerMotionWorldPosition(
                    motionState.sourceLaneIndex,
                    motionState.destinationLaneIndex,
                    motionState.startWorldPosition,
                    pathLength,
                    1f));
                conveyorCornerMotionStates.Remove(portableObject);
                NotifyConveyorMotionSettled();
                continue;
            }

            float travelDistance = conveyorSpeed * deltaTime;
            motionState.progress = Mathf.Clamp01(travelDistance / durationPathLength);
            SetConveyorPortableObjectWorldPose(portableObject, laneIndex, EvaluateConveyorCornerMotionWorldPosition(
                motionState.sourceLaneIndex,
                motionState.destinationLaneIndex,
                motionState.startWorldPosition,
                pathLength,
                motionState.progress));
            if (motionState.progress >= 1f - 0.0001f)
            {
                conveyorCornerMotionStates.Remove(portableObject);
                NotifyConveyorMotionSettled();
                float carryDeltaTime = conveyorSpeed > ConveyorContinuousMotionEpsilon
                    ? Mathf.Max(0f, travelDistance - durationPathLength) / conveyorSpeed
                    : 0f;
                if (carryDeltaTime > ConveyorContinuousMotionEpsilon)
                {
                    TryContinueConveyorItemMotion(
                        laneIndex,
                        portableObject,
                        conveyorSpeed,
                        carryDeltaTime,
                        ConveyorContinuousMotionMaxCarrySteps - 1);
                }
            }
            else
            {
                conveyorCornerMotionStates[portableObject] = motionState;
            }
        }

        conveyorCornerMotionTickBuffer.Clear();
    }

    private void AdvanceStartedLinearConveyorMotionStates(float conveyorSpeed, float deltaTime)
    {
        if (ShouldUseVirtualConveyorItemRendering())
        {
            return;
        }

        if (conveyorSpeed <= 0f
            || deltaTime <= 0f
            || conveyorLinearMotionStates.Count == 0)
        {
            return;
        }

        conveyorLinearMotionTickBuffer.Clear();
        foreach (KeyValuePair<PortableObject, ConveyorLinearMotionState> pair in conveyorLinearMotionStates)
        {
            if (pair.Key == null
                || !pair.Key.WasMovedByConveyorThisFrame
                || pair.Value.progress > 0.0001f)
            {
                continue;
            }

            conveyorLinearMotionTickBuffer.Add(pair.Key);
        }

        for (int i = 0; i < conveyorLinearMotionTickBuffer.Count; i++)
        {
            PortableObject portableObject = conveyorLinearMotionTickBuffer[i];
            if (portableObject == null
                || !conveyorLinearMotionStates.TryGetValue(portableObject, out ConveyorLinearMotionState motionState))
            {
                continue;
            }

            Vector3 targetPosition = GetConveyorLaneWorldPosition(motionState.destinationLaneIndex);
            int laneIndex = FindConveyorLaneIndexForPortableObject(portableObject);
            if (motionState.pathLength <= 0.0001f)
            {
                SetConveyorPortableObjectWorldPose(portableObject, laneIndex, targetPosition);
                conveyorLinearMotionStates.Remove(portableObject);
                NotifyConveyorMotionSettled();
                continue;
            }

            float travelDistance = conveyorSpeed * deltaTime;
            motionState.progress = Mathf.Clamp01(travelDistance / motionState.pathLength);
            SetConveyorPortableObjectWorldPose(portableObject, laneIndex, EvaluateConveyorLinearMotionWorldPosition(
                motionState.cornerContinuation,
                motionState.startWorldPosition,
                motionState.hasViaWorldPosition,
                motionState.viaWorldPosition,
                targetPosition,
                motionState.pathLength,
                motionState.progress));
            if (motionState.progress >= 1f - 0.0001f)
            {
                SetConveyorPortableObjectWorldPose(portableObject, laneIndex, targetPosition);
                conveyorLinearMotionStates.Remove(portableObject);
                NotifyConveyorMotionSettled();
                float carryDeltaTime = conveyorSpeed > ConveyorContinuousMotionEpsilon
                    ? Mathf.Max(0f, travelDistance - motionState.pathLength) / conveyorSpeed
                    : 0f;
                if (carryDeltaTime > ConveyorContinuousMotionEpsilon)
                {
                    TryContinueConveyorItemMotion(
                        laneIndex,
                        portableObject,
                        conveyorSpeed,
                        carryDeltaTime,
                        ConveyorContinuousMotionMaxCarrySteps - 1);
                }
            }
            else
            {
                conveyorLinearMotionStates[portableObject] = motionState;
            }
        }

        conveyorLinearMotionTickBuffer.Clear();
    }

    private bool TryMoveConveyorLane(int sourceLaneIndex)
    {
        return TryMoveConveyorLane(sourceLaneIndex, out _, out _);
    }

    private bool TryMoveConveyorLane(
        int sourceLaneIndex,
        out Block movedDestinationBlock,
        out int movedDestinationLaneIndex,
        bool ignoreMoveAttemptThrottle = false)
    {
        bool moved = TryMoveConveyorLaneCore(
            sourceLaneIndex,
            out movedDestinationBlock,
            out movedDestinationLaneIndex,
            ignoreMoveAttemptThrottle);
        MapObjectTickProfiler.AddBeltTryMoveAttempt(moved);
        return moved;
    }

    private bool TryMoveConveyorLaneCore(
        int sourceLaneIndex,
        out Block movedDestinationBlock,
        out int movedDestinationLaneIndex,
        bool ignoreMoveAttemptThrottle = false)
    {
        movedDestinationBlock = null;
        movedDestinationLaneIndex = -1;

        if (!IsValidConveyorLaneIndex(sourceLaneIndex))
        {
            return false;
        }

        if (!ignoreMoveAttemptThrottle && IsConveyorNetworkMoveAttemptThrottled())
        {
            return false;
        }

        if (!ignoreMoveAttemptThrottle && IsConveyorLaneMoveAttemptThrottled(sourceLaneIndex))
        {
            return false;
        }

        if (!HasConveyorItemAtLane(sourceLaneIndex)
            || WasConveyorItemMovedThisFrame(sourceLaneIndex)
            || !IsConveyorItemReadyToMoveAtLane(sourceLaneIndex))
        {
            return false;
        }

        if (TryMoveConveyorLaneRunToOpenDestination(
                sourceLaneIndex,
                ignoreMoveAttemptThrottle,
                out movedDestinationBlock,
                out movedDestinationLaneIndex))
        {
            return true;
        }

        if (TryMoveConveyorLaneToUnloadedSuccessor(
                sourceLaneIndex,
                out movedDestinationBlock,
                out movedDestinationLaneIndex))
        {
            return true;
        }

        if (TryGetCachedConveyorPlanFailure(sourceLaneIndex, ignoreMoveAttemptThrottle))
        {
            DelayConveyorLaneMoveAttempt(sourceLaneIndex, GetConveyorBlockedRetryDelay());
            return false;
        }

        ConveyorLaneKey rootLane = new ConveyorLaneKey(this, sourceLaneIndex);
        conveyorMoveVisiting.Clear();
        conveyorPlannedMoves.Clear();
        conveyorMoveVisiting.Add(rootLane);

        bool canMove = TryPlanConveyorLaneMove(
            rootLane,
            rootLane,
            conveyorMoveVisiting,
            conveyorPlannedMoves,
            ignoreMoveAttemptThrottle)
            && conveyorPlannedMoves.Count > 0;
        if (!canMove)
        {
            DelayConveyorLaneMoveAttempt(sourceLaneIndex, GetConveyorBlockedRetryDelay());
            conveyorMoveVisiting.Clear();
            conveyorPlannedMoves.Clear();
            return false;
        }

        ResolvePlannedConveyorMoveDestination(
            conveyorPlannedMoves,
            this,
            sourceLaneIndex,
            out movedDestinationBlock,
            out movedDestinationLaneIndex);
        ApplyPlannedConveyorLaneMoves(conveyorPlannedMoves);
        conveyorMoveVisiting.Clear();
        conveyorPlannedMoves.Clear();
        return true;
    }

    private bool CanMoveConveyorLane(int sourceLaneIndex, bool ignoreMoveAttemptThrottle = false)
    {
        if (!IsValidConveyorLaneIndex(sourceLaneIndex))
        {
            return false;
        }

        if (TryGetCachedCanMoveConveyorLane(sourceLaneIndex, ignoreMoveAttemptThrottle, out bool cachedCanMove))
        {
            return cachedCanMove;
        }

        bool canMove = CanMoveConveyorLaneUncached(sourceLaneIndex, ignoreMoveAttemptThrottle);
        CacheCanMoveConveyorLane(sourceLaneIndex, ignoreMoveAttemptThrottle, canMove);
        return canMove;
    }

    private bool CanMoveConveyorLaneUncached(int sourceLaneIndex, bool ignoreMoveAttemptThrottle)
    {
        if (!ignoreMoveAttemptThrottle && IsConveyorNetworkMoveAttemptThrottled())
        {
            return false;
        }

        if (!ignoreMoveAttemptThrottle && IsConveyorLaneMoveAttemptThrottled(sourceLaneIndex))
        {
            return false;
        }

        if (!HasConveyorItemAtLane(sourceLaneIndex)
            || WasConveyorItemMovedThisFrame(sourceLaneIndex)
            || !IsConveyorItemReadyToMoveAtLane(sourceLaneIndex))
        {
            return false;
        }

        if (CanMoveConveyorLaneDirect(sourceLaneIndex))
        {
            return true;
        }

        if (CanMoveConveyorLaneToUnloadedSuccessor(sourceLaneIndex))
        {
            return true;
        }

        ConveyorLaneKey rootLane = new ConveyorLaneKey(this, sourceLaneIndex);
        conveyorCanMoveVisiting.Clear();
        conveyorCanMoveVisiting.Add(rootLane);

        bool canMove = TryPlanConveyorLaneMove(
            rootLane,
            rootLane,
            conveyorCanMoveVisiting,
            null,
            ignoreMoveAttemptThrottle,
            markBlockedCycles: false,
            recordPlannedMoves: false,
            countPlanCall: false,
            cacheFailures: false);

        conveyorCanMoveVisiting.Clear();
        return canMove;
    }

    private bool TryGetCachedCanMoveConveyorLane(
        int sourceLaneIndex,
        bool ignoreMoveAttemptThrottle,
        out bool canMove)
    {
        canMove = false;
        int cacheIndex = GetCanMoveConveyorLaneCacheIndex(sourceLaneIndex, ignoreMoveAttemptThrottle);
        if (cacheIndex < 0
            || !conveyorCanMoveCacheValid[cacheIndex]
            || conveyorCanMoveCacheFrames[cacheIndex] != Time.frameCount
            || conveyorCanMoveCacheVersions[cacheIndex] != conveyorCanMoveGlobalStateVersion)
        {
            return false;
        }

        canMove = conveyorCanMoveCacheResults[cacheIndex];
        return true;
    }

    private void CacheCanMoveConveyorLane(int sourceLaneIndex, bool ignoreMoveAttemptThrottle, bool canMove)
    {
        int cacheIndex = GetCanMoveConveyorLaneCacheIndex(sourceLaneIndex, ignoreMoveAttemptThrottle);
        if (cacheIndex < 0)
        {
            return;
        }

        conveyorCanMoveCacheValid[cacheIndex] = true;
        conveyorCanMoveCacheFrames[cacheIndex] = Time.frameCount;
        conveyorCanMoveCacheVersions[cacheIndex] = conveyorCanMoveGlobalStateVersion;
        conveyorCanMoveCacheResults[cacheIndex] = canMove;
    }

    private static int GetCanMoveConveyorLaneCacheIndex(int sourceLaneIndex, bool ignoreMoveAttemptThrottle)
    {
        if (sourceLaneIndex < 0 || sourceLaneIndex >= ConveyorStackLaneLimit)
        {
            return -1;
        }

        return (sourceLaneIndex * 2) + (ignoreMoveAttemptThrottle ? 1 : 0);
    }

    private bool TryGetCachedConveyorPlanFailure(int sourceLaneIndex, bool ignoreMoveAttemptThrottle)
    {
        int cacheIndex = GetCanMoveConveyorLaneCacheIndex(sourceLaneIndex, ignoreMoveAttemptThrottle);
        if (cacheIndex < 0 || !conveyorPlanFailureCacheValid[cacheIndex])
        {
            return false;
        }

        if (conveyorPlanFailureCacheVersions[cacheIndex] != conveyorPlanFailureGlobalStateVersion
            || Time.time >= conveyorPlanFailureCacheUntilTimes[cacheIndex])
        {
            conveyorPlanFailureCacheValid[cacheIndex] = false;
            return false;
        }

        return true;
    }

    private void CacheConveyorPlanFailure(int sourceLaneIndex, bool ignoreMoveAttemptThrottle)
    {
        int cacheIndex = GetCanMoveConveyorLaneCacheIndex(sourceLaneIndex, ignoreMoveAttemptThrottle);
        if (cacheIndex < 0)
        {
            return;
        }

        float retryUntilTime = Time.time + GetConveyorBlockedRetryDelay();
        conveyorPlanFailureCacheUntilTimes[cacheIndex] =
            conveyorPlanFailureCacheValid[cacheIndex]
            && conveyorPlanFailureCacheVersions[cacheIndex] == conveyorPlanFailureGlobalStateVersion
                ? Mathf.Max(conveyorPlanFailureCacheUntilTimes[cacheIndex], retryUntilTime)
                : retryUntilTime;
        conveyorPlanFailureCacheValid[cacheIndex] = true;
        conveyorPlanFailureCacheVersions[cacheIndex] = conveyorPlanFailureGlobalStateVersion;
    }

    private static bool CacheConveyorPlanFailureAndReturnFalse(
        Block block,
        int laneIndex,
        bool ignoreMoveAttemptThrottle,
        bool cacheFailure = true)
    {
        if (cacheFailure)
        {
            block?.CacheConveyorPlanFailure(laneIndex, ignoreMoveAttemptThrottle);
        }

        return false;
    }

    private bool CanRetryConveyorLaneMove(int sourceLaneIndex, bool ignoreMoveAttemptThrottle = false)
    {
        return CanMoveConveyorLane(sourceLaneIndex, ignoreMoveAttemptThrottle);
    }

    private bool CanMoveConveyorLaneDirect(int sourceLaneIndex)
    {
        return TryGetConveyorSuccessor(
                sourceLaneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex,
                out _)
            && destinationBlock != null
            && destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            && !(destinationBlock == this && destinationLaneIndex == sourceLaneIndex)
            && !destinationBlock.HasConveyorItemAtLane(destinationLaneIndex)
            && GetConveyorItemIdAtLane(sourceLaneIndex) >= 0;
    }

    private bool CanMoveConveyorLaneToUnloadedSuccessor(int sourceLaneIndex)
    {
        if (!TryGetUnloadedConveyorHandoffContext(
                sourceLaneIndex,
                out TerrainGenerator terrainGenerator,
                out Vector2Int flowDirection,
                out int sourceColumnOrdinal)
            || terrainGenerator == null)
        {
            return false;
        }

        return terrainGenerator.CanHandoffConveyorItemToVirtualConveyor(
            coordinate,
            flowDirection,
            sourceColumnOrdinal);
    }

    private bool TryMoveConveyorLaneToUnloadedSuccessor(
        int sourceLaneIndex,
        out Block movedDestinationBlock,
        out int movedDestinationLaneIndex)
    {
        movedDestinationBlock = null;
        movedDestinationLaneIndex = -1;

        if (!TryGetUnloadedConveyorHandoffContext(
                sourceLaneIndex,
                out TerrainGenerator terrainGenerator,
                out Vector2Int flowDirection,
                out int sourceColumnOrdinal)
            || terrainGenerator == null)
        {
            return false;
        }

        int itemId = GetConveyorItemIdAtLane(sourceLaneIndex);
        if (itemId < 0)
        {
            return false;
        }

        ConveyorItemLaneSaveState laneState = new ConveyorItemLaneSaveState
        {
            laneIndex = sourceLaneIndex,
            itemId = itemId,
            visualWorldPosition = GetConveyorItemVisualWorldPosition(sourceLaneIndex)
        };

        if (!terrainGenerator.TryHandoffConveyorItemToVirtualConveyor(
                coordinate,
                flowDirection,
                sourceColumnOrdinal,
                laneState,
                out _,
                out movedDestinationLaneIndex))
        {
            return false;
        }

        PortableObject portableObject = GetConveyorPortableObjectAtLane(sourceLaneIndex);
        ClearConveyorItemAtLane(sourceLaneIndex);
        ReleaseFloorObject(portableObject);
        WakeConveyorMoveAttemptsAround();
        RefreshConveyorActivityRegistration();
        return true;
    }

    private bool TrySetConveyorObjectAtLaneOrSingleLineFallback(int laneIndex, int objectId, out PortableObject targetPortableObject)
    {
        if (TrySetConveyorObjectAtLane(laneIndex, objectId, out targetPortableObject))
        {
            return true;
        }

        if (TryNormalizeConveyorLaneIndex(laneIndex, out int normalizedLaneIndex)
            && TrySetConveyorObjectAtLane(normalizedLaneIndex, objectId, out targetPortableObject))
        {
            return true;
        }

        if (TrySetConveyorObjectAtLane(ConveyorSingleLineBackLaneIndex, objectId, out targetPortableObject))
        {
            return true;
        }

        return TrySetConveyorObjectAtLane(ConveyorSingleLineFrontLaneIndex, objectId, out targetPortableObject);
    }

    private bool TryGetUnloadedConveyorHandoffContext(
        int sourceLaneIndex,
        out TerrainGenerator terrainGenerator,
        out Vector2Int flowDirection,
        out int sourceColumnOrdinal)
    {
        terrainGenerator = null;
        flowDirection = Vector2Int.zero;
        sourceColumnOrdinal = -1;

        if (!IsValidConveyorLaneIndex(sourceLaneIndex)
            || GetConveyorItemIdAtLane(sourceLaneIndex) < 0
            || !TryGetConveyorExitLaneColumnOrdinal(sourceLaneIndex, out sourceColumnOrdinal)
            || !TryGetConveyorFlowDirection(out flowDirection)
            || flowDirection == Vector2Int.zero
            || !TryResolveOwningTerrainGenerator(out terrainGenerator)
            || terrainGenerator == null)
        {
            return false;
        }

        Vector2Int destinationCoordinate = coordinate + flowDirection;
        return !terrainGenerator.TryGetLoadedBlock(destinationCoordinate, out Block loadedDestinationBlock)
            || loadedDestinationBlock == null;
    }

    private bool TryGetConveyorExitLaneColumnOrdinal(int sourceLaneIndex, out int columnOrdinal)
    {
        columnOrdinal = -1;
        if (IsCornerConveyor())
        {
            if (sourceLaneIndex != ConveyorSingleLineFrontLaneIndex)
            {
                return false;
            }

            columnOrdinal = 0;
            return true;
        }

        if (!TryGetConveyorLaneLayout(out int frontLaneIndex, out _))
        {
            return false;
        }

        if (sourceLaneIndex == frontLaneIndex)
        {
            columnOrdinal = 0;
            return true;
        }

        return false;
    }

    private bool TryMoveConveyorLaneRunToOpenDestination(
        int sourceLaneIndex,
        bool ignoreMoveAttemptThrottle,
        out Block movedDestinationBlock,
        out int movedDestinationLaneIndex)
    {
        movedDestinationBlock = null;
        movedDestinationLaneIndex = -1;

        if (!TryGetConveyorSuccessor(
                sourceLaneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex,
                out bool useCornerMotion)
            || destinationBlock == null
            || !destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex)
            || (destinationBlock == this && destinationLaneIndex == sourceLaneIndex)
            || destinationBlock.HasConveyorItemAtLane(destinationLaneIndex))
        {
            return false;
        }

        conveyorPlannedMoves.Clear();
        conveyorMoveVisiting.Clear();

        if (!TryAppendConveyorLaneMove(
                conveyorPlannedMoves,
                this,
                sourceLaneIndex,
                destinationBlock,
                destinationLaneIndex,
                useCornerMotion))
        {
            conveyorPlannedMoves.Clear();
            conveyorMoveVisiting.Clear();
            return false;
        }

        conveyorMoveVisiting.Add(new ConveyorLaneKey(this, sourceLaneIndex));
        Block currentDestinationBlock = this;
        int currentDestinationLaneIndex = sourceLaneIndex;

        for (int runIndex = 1; runIndex < ConveyorRunMoveMaxSegments; runIndex++)
        {
            if (!TryAppendConveyorRunPredecessorMove(
                    currentDestinationBlock,
                    currentDestinationLaneIndex,
                    ignoreMoveAttemptThrottle,
                    out Block predecessorBlock,
                    out int predecessorLaneIndex))
            {
                break;
            }

            currentDestinationBlock = predecessorBlock;
            currentDestinationLaneIndex = predecessorLaneIndex;
        }

        ApplyPlannedConveyorLaneMoves(conveyorPlannedMoves);
        conveyorPlannedMoves.Clear();
        conveyorMoveVisiting.Clear();

        movedDestinationBlock = destinationBlock;
        movedDestinationLaneIndex = destinationLaneIndex;
        return true;
    }

    private bool TryAppendConveyorRunPredecessorMove(
        Block destinationBlock,
        int destinationLaneIndex,
        bool ignoreMoveAttemptThrottle,
        out Block predecessorBlock,
        out int predecessorLaneIndex)
    {
        predecessorBlock = null;
        predecessorLaneIndex = -1;
        if (destinationBlock == null
            || !destinationBlock.TryGetConveyorPredecessor(
                destinationLaneIndex,
                out predecessorBlock,
                out predecessorLaneIndex,
                out _)
            || predecessorBlock == null
            || !predecessorBlock.IsValidConveyorLaneIndex(predecessorLaneIndex)
            || !predecessorBlock.TryGetConveyorSuccessor(
                predecessorLaneIndex,
                out Block resolvedDestinationBlock,
                out int resolvedDestinationLaneIndex,
                out bool resolvedUseCornerMotion)
            || resolvedDestinationBlock != destinationBlock
            || resolvedDestinationLaneIndex != destinationLaneIndex)
        {
            predecessorBlock = null;
            predecessorLaneIndex = -1;
            return false;
        }

        ConveyorLaneKey predecessorLane = new ConveyorLaneKey(predecessorBlock, predecessorLaneIndex);
        if (!conveyorMoveVisiting.Add(predecessorLane))
        {
            predecessorBlock = null;
            predecessorLaneIndex = -1;
            return false;
        }

        if (!predecessorBlock.CanConveyorLaneJoinRun(predecessorLaneIndex, ignoreMoveAttemptThrottle)
            || !TryAppendConveyorLaneMove(
                conveyorPlannedMoves,
                predecessorBlock,
                predecessorLaneIndex,
                destinationBlock,
                destinationLaneIndex,
                resolvedUseCornerMotion))
        {
            conveyorMoveVisiting.Remove(predecessorLane);
            predecessorBlock = null;
            predecessorLaneIndex = -1;
            return false;
        }

        return true;
    }

    private bool CanConveyorLaneJoinRun(int laneIndex, bool ignoreMoveAttemptThrottle)
    {
        if (!IsValidConveyorLaneIndex(laneIndex)
            || (!ignoreMoveAttemptThrottle && IsConveyorNetworkMoveAttemptThrottled())
            || (!ignoreMoveAttemptThrottle && IsConveyorLaneMoveAttemptThrottled(laneIndex))
            || !HasConveyorItemAtLane(laneIndex)
            || WasConveyorItemMovedThisFrame(laneIndex)
            || !IsConveyorItemReadyToMoveAtLane(laneIndex)
            || GetConveyorItemIdAtLane(laneIndex) < 0)
        {
            return false;
        }

        return true;
    }

    private static bool TryAppendConveyorLaneMove(
        List<ConveyorLaneMove> plannedMoves,
        Block sourceBlock,
        int sourceLaneIndex,
        Block destinationBlock,
        int destinationLaneIndex,
        bool useCornerMotion)
    {
        if (plannedMoves == null
            || sourceBlock == null
            || destinationBlock == null
            || !sourceBlock.IsValidConveyorLaneIndex(sourceLaneIndex)
            || !destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex))
        {
            return false;
        }

        int itemId = sourceBlock.GetConveyorItemIdAtLane(sourceLaneIndex);
        if (itemId < 0)
        {
            return false;
        }

        PortableObject portableObject = sourceBlock.GetConveyorPortableObjectAtLane(sourceLaneIndex);
        plannedMoves.Add(new ConveyorLaneMove(
            sourceBlock,
            sourceLaneIndex,
            destinationBlock,
            destinationLaneIndex,
            portableObject,
            itemId,
            sourceBlock.GetConveyorPickupGateStateAtLane(sourceLaneIndex),
            useCornerMotion,
            sourceBlock.GetConveyorItemVisualWorldPosition(sourceLaneIndex),
            sourceBlock.GetConveyorCornerContinuationForLane(sourceLaneIndex, portableObject)));
        return true;
    }

    private static void ResolvePlannedConveyorMoveDestination(
        List<ConveyorLaneMove> plannedMoves,
        Block sourceBlock,
        int sourceLaneIndex,
        out Block destinationBlock,
        out int destinationLaneIndex)
    {
        destinationBlock = null;
        destinationLaneIndex = -1;
        if (plannedMoves == null || sourceBlock == null)
        {
            return;
        }

        for (int i = 0; i < plannedMoves.Count; i++)
        {
            ConveyorLaneMove move = plannedMoves[i];
            if (move.sourceBlock != sourceBlock || move.sourceLaneIndex != sourceLaneIndex)
            {
                continue;
            }

            destinationBlock = move.destinationBlock;
            destinationLaneIndex = move.destinationLaneIndex;
            return;
        }
    }

    private bool TryPlanConveyorLaneMove(
        ConveyorLaneKey rootLane,
        ConveyorLaneKey currentLane,
        HashSet<ConveyorLaneKey> visiting,
        List<ConveyorLaneMove> plannedMoves,
        bool ignoreMoveAttemptThrottle,
        bool markBlockedCycles = true,
        bool recordPlannedMoves = true,
        bool countPlanCall = true,
        bool cacheFailures = true)
    {
        Block currentBlock = currentLane.Block;
        if (currentBlock == null
            || currentLane.LaneIndex < 0
            || !currentBlock.IsValidConveyorLaneIndex(currentLane.LaneIndex))
        {
            return false;
        }

        if (cacheFailures && currentBlock.TryGetCachedConveyorPlanFailure(currentLane.LaneIndex, ignoreMoveAttemptThrottle))
        {
            return false;
        }

        if (recordPlannedMoves && plannedMoves == null)
        {
            return false;
        }

        if (countPlanCall)
        {
            MapObjectTickProfiler.AddBeltPlanMoveCall();
        }

        int itemId = currentBlock.GetConveyorItemIdAtLane(currentLane.LaneIndex);
        if (itemId < 0
            || currentBlock.WasConveyorItemMovedThisFrame(currentLane.LaneIndex)
            || (!currentBlock.IsConveyorItemReadyToMoveAtLane(currentLane.LaneIndex)
                && !currentBlock.CanTreatConveyorLaneAsCycleReady(rootLane, currentLane)))
        {
            return CacheConveyorPlanFailureAndReturnFalse(
                currentBlock,
                currentLane.LaneIndex,
                ignoreMoveAttemptThrottle,
                cacheFailures);
        }

        if (!currentBlock.TryGetConveyorSuccessor(
                currentLane.LaneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex,
                out bool useCornerMotion))
        {
            return CacheConveyorPlanFailureAndReturnFalse(
                currentBlock,
                currentLane.LaneIndex,
                ignoreMoveAttemptThrottle,
                cacheFailures);
        }

        ConveyorLaneKey destinationLane = new ConveyorLaneKey(destinationBlock, destinationLaneIndex);
        if (!destinationBlock.IsValidConveyorLaneIndex(destinationLaneIndex))
        {
            return CacheConveyorPlanFailureAndReturnFalse(
                currentBlock,
                currentLane.LaneIndex,
                ignoreMoveAttemptThrottle,
                cacheFailures);
        }

        if (destinationBlock == currentBlock && destinationLaneIndex == currentLane.LaneIndex)
        {
            return CacheConveyorPlanFailureAndReturnFalse(
                currentBlock,
                currentLane.LaneIndex,
                ignoreMoveAttemptThrottle,
                cacheFailures);
        }

        if (destinationLane.Equals(rootLane))
        {
            return !recordPlannedMoves
                || TryAppendConveyorLaneMove(
                    plannedMoves,
                    currentBlock,
                    currentLane.LaneIndex,
                    destinationBlock,
                    destinationLaneIndex,
                    useCornerMotion);
        }

        if (destinationBlock.HasConveyorItemAtLane(destinationLaneIndex))
        {
            if (cacheFailures && destinationBlock.TryGetCachedConveyorPlanFailure(destinationLaneIndex, ignoreMoveAttemptThrottle))
            {
                return CacheConveyorPlanFailureAndReturnFalse(
                    currentBlock,
                    currentLane.LaneIndex,
                    ignoreMoveAttemptThrottle,
                    cacheFailures);
            }

            if ((!ignoreMoveAttemptThrottle && destinationBlock.IsConveyorLaneMoveAttemptThrottled(destinationLaneIndex))
                || destinationBlock.WasConveyorItemMovedThisFrame(destinationLaneIndex)
                || (!destinationBlock.IsConveyorItemReadyToMoveAtLane(destinationLaneIndex)
                    && !destinationBlock.CanTreatConveyorLaneAsCycleReady(rootLane, destinationLane)))
            {
                return CacheConveyorPlanFailureAndReturnFalse(
                    currentBlock,
                    currentLane.LaneIndex,
                    ignoreMoveAttemptThrottle,
                    cacheFailures);
            }

            if (!visiting.Add(destinationLane))
            {
                if (markBlockedCycles)
                {
                    currentBlock.MarkConveyorLaneCycleBlocked(visiting);
                }

                return false;
            }

            bool planned = TryPlanConveyorLaneMove(
                rootLane,
                destinationLane,
                visiting,
                plannedMoves,
                ignoreMoveAttemptThrottle,
                markBlockedCycles,
                recordPlannedMoves,
                countPlanCall,
                cacheFailures);
            visiting.Remove(destinationLane);
            if (!planned)
            {
                return CacheConveyorPlanFailureAndReturnFalse(
                    currentBlock,
                    currentLane.LaneIndex,
                    ignoreMoveAttemptThrottle,
                    cacheFailures);
            }
        }

        if (!recordPlannedMoves)
        {
            return true;
        }

        bool appended = TryAppendConveyorLaneMove(
            plannedMoves,
            currentBlock,
            currentLane.LaneIndex,
            destinationBlock,
            destinationLaneIndex,
            useCornerMotion);
        return appended
            || CacheConveyorPlanFailureAndReturnFalse(
                currentBlock,
                currentLane.LaneIndex,
                ignoreMoveAttemptThrottle,
                cacheFailures);
    }

    private void ApplyPlannedConveyorLaneMoves(List<ConveyorLaneMove> plannedMoves)
    {
        if (plannedMoves == null || plannedMoves.Count == 0)
        {
            return;
        }

        conveyorTouchedBlocks.Clear();
        conveyorTouchedBlockSet.Clear();
        for (int i = 0; i < plannedMoves.Count; i++)
        {
            ConveyorLaneMove move = plannedMoves[i];
            if (move.sourceBlock != null
                && move.sourceLaneIndex >= 0
                && move.sourceBlock.IsValidConveyorLaneIndex(move.sourceLaneIndex)
                && move.sourceBlock.GetConveyorItemIdAtLane(move.sourceLaneIndex) == move.itemId
                && (move.portableObject == null
                    || move.sourceBlock.GetConveyorPortableObjectAtLane(move.sourceLaneIndex) == move.portableObject))
            {
                move.sourceBlock.ClearConveyorItemAtLane(move.sourceLaneIndex);
                MarkConveyorTouchedBlock(move.sourceBlock);
            }
        }

        for (int i = 0; i < plannedMoves.Count; i++)
        {
            ConveyorLaneMove move = plannedMoves[i];
            if (move.destinationBlock == null
                || move.destinationLaneIndex < 0
                || !move.destinationBlock.IsValidConveyorLaneIndex(move.destinationLaneIndex)
                || move.itemId < 0)
            {
                continue;
            }

            PortableObject portableObject = move.portableObject;
            ConveyorPickupGateState pickupGateState = move.pickupGateState;
            pickupGateState.MarkSettled();
            move.destinationBlock.SetConveyorItemAtLane(
                move.destinationLaneIndex,
                move.itemId,
                portableObject,
                pickupGateState);

            if (portableObject != null)
            {
                portableObject.SetCachedParent(move.destinationBlock.transform, true);
                if (!move.destinationBlock.ShouldUseVirtualConveyorItemRendering())
                {
                    portableObject.SetCachedActive(true);
                }

                move.destinationBlock.ApplyConveyorObjectRenderingMode(portableObject);

                DroppedItemPickupGate gate = portableObject.PickupGate;
                gate?.MarkSettled();
            }

            move.destinationBlock.MarkConveyorItemMovedThisFrame(move.destinationLaneIndex);

            if (move.useCornerMotion && move.sourceBlock == move.destinationBlock && move.destinationBlock.IsCornerConveyor())
            {
                float cornerPathLength = move.destinationBlock.GetConveyorCornerMotionPathLength(
                    move.sourceLaneIndex,
                    move.destinationLaneIndex,
                    move.startWorldPosition);
                float cornerDurationPathLength = move.destinationBlock.GetSynchronizedConveyorCornerMotionDurationPathLength(
                    move.sourceLaneIndex,
                    move.destinationLaneIndex,
                    move.startWorldPosition,
                    cornerPathLength);
                if (portableObject != null)
                {
                    move.destinationBlock.conveyorCornerMotionStates[portableObject] = new ConveyorCornerMotionState
                    {
                        sourceLaneIndex = move.sourceLaneIndex,
                        destinationLaneIndex = move.destinationLaneIndex,
                        startWorldPosition = move.startWorldPosition,
                        progress = 0f,
                        pathLength = cornerPathLength,
                        durationPathLength = cornerDurationPathLength
                    };
                    move.destinationBlock.conveyorLinearMotionStates.Remove(portableObject);
                    move.destinationBlock.conveyorItemMotionStates[move.destinationLaneIndex] = default;
                }
                else
                {
                    ConveyorDataMotionState dataMotionState = new ConveyorDataMotionState
                    {
                        active = true,
                        useCornerMotion = true,
                        startWorldPosition = move.startWorldPosition,
                        sourceLaneIndex = move.sourceLaneIndex,
                        destinationLaneIndex = move.destinationLaneIndex,
                        progress = 0f,
                        pathLength = cornerPathLength,
                        durationPathLength = cornerDurationPathLength
                    };
                    move.destinationBlock.conveyorItemMotionStates[move.destinationLaneIndex] =
                        move.destinationBlock.InitializeConveyorDataMotionTiming(dataMotionState, 0f);
                    move.destinationBlock.MarkConveyorItemVisualDirty();
                }
            }
            else
            {
                ConveyorCornerContinuation cornerContinuation = move.cornerContinuation;
                Vector3 linearStartWorldPosition = GetConveyorCornerContinuationEndWorldPosition(
                    cornerContinuation,
                    move.startWorldPosition);
                float pathLength = move.sourceBlock != null
                    ? move.sourceBlock.GetConveyorMovePathLength(
                        move.sourceLaneIndex,
                        move.destinationBlock,
                        move.destinationLaneIndex,
                        false,
                        linearStartWorldPosition)
                    : 0f;
                pathLength += GetConveyorCornerContinuationRemainingPathLength(cornerContinuation);

                if (portableObject != null)
                {
                    move.destinationBlock.conveyorCornerMotionStates.Remove(portableObject);
                    Vector3 viaWorldPosition = default;
                    bool hasViaWorldPosition = move.sourceBlock != null
                        && move.sourceBlock.TryGetConveyorLinearMoveViaWorldPosition(
                            move.sourceLaneIndex,
                            linearStartWorldPosition,
                            out viaWorldPosition);
                    move.destinationBlock.conveyorLinearMotionStates[portableObject] = new ConveyorLinearMotionState
                    {
                        cornerContinuation = cornerContinuation,
                        startWorldPosition = move.startWorldPosition,
                        hasViaWorldPosition = hasViaWorldPosition,
                        viaWorldPosition = viaWorldPosition,
                        destinationLaneIndex = move.destinationLaneIndex,
                        progress = 0f,
                        pathLength = pathLength
                    };
                    move.destinationBlock.conveyorItemMotionStates[move.destinationLaneIndex] = default;
                }
                else
                {
                    Vector3 viaWorldPosition = default;
                    bool hasViaWorldPosition = move.sourceBlock != null
                        && move.sourceBlock.TryGetConveyorLinearMoveViaWorldPosition(
                            move.sourceLaneIndex,
                            linearStartWorldPosition,
                            out viaWorldPosition);
                    ConveyorDataMotionState dataMotionState = new ConveyorDataMotionState
                    {
                        active = true,
                        useCornerMotion = false,
                        cornerContinuation = cornerContinuation,
                        startWorldPosition = move.startWorldPosition,
                        hasViaWorldPosition = hasViaWorldPosition,
                        viaWorldPosition = viaWorldPosition,
                        destinationLaneIndex = move.destinationLaneIndex,
                        progress = 0f,
                        pathLength = pathLength
                    };
                    move.destinationBlock.conveyorItemMotionStates[move.destinationLaneIndex] =
                        move.destinationBlock.InitializeConveyorDataMotionTiming(dataMotionState, 0f);
                    move.destinationBlock.MarkConveyorItemVisualDirty();
                }
            }

            MarkConveyorTouchedBlock(move.destinationBlock);
        }

        int touchedBlockCount = conveyorTouchedBlocks.Count;
        MapObjectTickProfiler.AddBeltPlannedMoveApplication(plannedMoves.Count, touchedBlockCount);
        TerrainGenerator activeTerrain = TerrainGenerator.Active;
        if (activeTerrain != null)
        {
            activeTerrain.WakeAndRefreshConveyorRuntimeBlocks(conveyorTouchedBlocks);
        }
        else
        {
            for (int i = 0; i < touchedBlockCount; i++)
            {
                Block touchedBlock = conveyorTouchedBlocks[i];
                touchedBlock?.WakeConveyorMoveAttemptsAround();
                touchedBlock?.RefreshConveyorActivityRegistration();
            }
        }

        conveyorTouchedBlocks.Clear();
        conveyorTouchedBlockSet.Clear();
    }

    private void MarkConveyorTouchedBlock(Block block)
    {
        if (block != null && conveyorTouchedBlockSet.Add(block))
        {
            conveyorTouchedBlocks.Add(block);
        }
    }

    private ConveyorCornerContinuation GetConveyorCornerContinuationForLane(
        int laneIndex,
        PortableObject portableObject)
    {
        ConveyorCornerContinuation continuation = default;
        if (!IsCornerConveyor() || !IsValidConveyorLaneIndex(laneIndex))
        {
            return continuation;
        }

        ConveyorCornerMotionState motionState;
        if (portableObject != null)
        {
            if (!conveyorCornerMotionStates.TryGetValue(portableObject, out motionState))
            {
                return continuation;
            }
        }
        else
        {
            if (laneIndex >= conveyorItemMotionStates.Count
                || !conveyorItemMotionStates[laneIndex].active
                || !conveyorItemMotionStates[laneIndex].useCornerMotion)
            {
                return continuation;
            }

            ConveyorDataMotionState originalDataMotionState = conveyorItemMotionStates[laneIndex];
            ConveyorDataMotionState dataMotionState = EnsureConveyorDataMotionTiming(originalDataMotionState);
            if (dataMotionState.startTime != originalDataMotionState.startTime || dataMotionState.duration != originalDataMotionState.duration)
            {
                conveyorItemMotionStates[laneIndex] = dataMotionState;
            }

            motionState = new ConveyorCornerMotionState
            {
                sourceLaneIndex = dataMotionState.sourceLaneIndex,
                destinationLaneIndex = dataMotionState.destinationLaneIndex,
                startWorldPosition = dataMotionState.startWorldPosition,
                progress = EvaluateConveyorDataMotionProgress(dataMotionState),
                pathLength = dataMotionState.pathLength,
                durationPathLength = dataMotionState.durationPathLength
            };
        }

        if (motionState.destinationLaneIndex != laneIndex
            || motionState.progress >= 1f - ConveyorContinuousMotionEpsilon)
        {
            return continuation;
        }

        float pathLength = ResolveConveyorCornerMotionPathLength(
            motionState.sourceLaneIndex,
            motionState.destinationLaneIndex,
            motionState.startWorldPosition,
            motionState.pathLength);
        if (pathLength <= ConveyorContinuousMotionEpsilon)
        {
            return continuation;
        }

        continuation.active = true;
        continuation.block = this;
        continuation.sourceLaneIndex = motionState.sourceLaneIndex;
        continuation.destinationLaneIndex = motionState.destinationLaneIndex;
        continuation.startWorldPosition = motionState.startWorldPosition;
        continuation.startProgress = Mathf.Clamp01(motionState.progress);
        continuation.pathLength = pathLength;
        continuation.durationPathLength = ResolveConveyorCornerMotionDurationPathLength(
            motionState.sourceLaneIndex,
            motionState.destinationLaneIndex,
            motionState.startWorldPosition,
            motionState.pathLength,
            motionState.durationPathLength);
        return continuation;
    }

    private int FindConveyorLaneIndexForPortableObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return -1;
        }

        int laneCount = Mathf.Min(GetConveyorLaneCount(), conveyorStack.Count);
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (conveyorStack[laneIndex] == portableObject)
            {
                return laneIndex;
            }
        }

        return -1;
    }

    private bool TryContinueConveyorItemMotion(
        int laneIndex,
        PortableObject portableObject,
        float conveyorSpeed,
        float carryDeltaTime,
        int remainingCarrySteps)
    {
        if (laneIndex < 0
            || remainingCarrySteps < 0
            || conveyorSpeed <= ConveyorContinuousMotionEpsilon
            || carryDeltaTime <= ConveyorContinuousMotionEpsilon)
        {
            return false;
        }

        if (!TryMoveConveyorLane(
                laneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex,
                true)
            || destinationBlock == null
            || destinationLaneIndex < 0)
        {
            return false;
        }

        if (portableObject != null)
        {
            return destinationBlock.TryAdvancePortableConveyorMotion(
                destinationLaneIndex,
                portableObject,
                conveyorSpeed,
                carryDeltaTime,
                remainingCarrySteps);
        }

        return destinationBlock.TryAdvanceVirtualConveyorLaneMotion(
            destinationLaneIndex,
            conveyorSpeed,
            carryDeltaTime,
            remainingCarrySteps);
    }

    private bool TryAdvancePortableConveyorMotion(
        int laneIndex,
        PortableObject portableObject,
        float conveyorSpeed,
        float deltaTime,
        int remainingCarrySteps)
    {
        if (portableObject == null)
        {
            return false;
        }

        if (!IsValidConveyorLaneIndex(laneIndex))
        {
            laneIndex = FindConveyorLaneIndexForPortableObject(portableObject);
        }

        if (laneIndex < 0)
        {
            return false;
        }

        if (!ShouldUseVirtualConveyorItemRendering())
        {
            if (TryUpdateCornerConveyorObjectWorldPosition(
                    laneIndex,
                    portableObject,
                    conveyorSpeed,
                    deltaTime,
                    remainingCarrySteps))
            {
                return true;
            }

            return TryUpdateLinearConveyorObjectWorldPosition(
                laneIndex,
                portableObject,
                conveyorSpeed,
                deltaTime,
                remainingCarrySteps);
        }

        if (TryAdvanceVirtualCornerConveyorMotion(
                portableObject,
                conveyorSpeed,
                deltaTime,
                remainingCarrySteps))
        {
            return true;
        }

        return TryAdvanceVirtualLinearConveyorMotion(
            portableObject,
            conveyorSpeed,
            deltaTime,
            remainingCarrySteps);
    }

    private bool IsConveyorObjectSettledAtLane(int laneIndex, PortableObject portableObject)
    {
        if (portableObject == null || portableObject.IsMovingToTarget)
        {
            return false;
        }

        if (conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState motionState))
        {
            return motionState.destinationLaneIndex == laneIndex && motionState.progress >= 1f - 0.0001f;
        }

        if (conveyorLinearMotionStates.TryGetValue(portableObject, out ConveyorLinearMotionState linearMotionState))
        {
            return linearMotionState.destinationLaneIndex == laneIndex && linearMotionState.progress >= 1f - 0.0001f;
        }

        if (ShouldUseVirtualConveyorItemRendering())
        {
            return true;
        }

        Vector3 targetPosition = GetConveyorLaneWorldPosition(laneIndex);
        Vector3 delta = portableObject.WorldPosition - targetPosition;
        delta.y = 0f;
        return delta.sqrMagnitude <= ConveyorLaneSettleEpsilon * ConveyorLaneSettleEpsilon;
    }

    private bool IsConveyorItemSettledAtLane(int laneIndex)
    {
        if (IsConveyorLaneMovementHeld(laneIndex))
        {
            return false;
        }

        PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
        if (portableObject != null)
        {
            return IsConveyorObjectSettledAtLane(laneIndex, portableObject);
        }

        if (!HasConveyorItemAtLane(laneIndex))
        {
            return false;
        }

        if (laneIndex >= 0
            && laneIndex < conveyorItemMotionStates.Count
            && conveyorItemMotionStates[laneIndex].active)
        {
            ConveyorDataMotionState motionState = EnsureConveyorDataMotionTiming(conveyorItemMotionStates[laneIndex]);
            return motionState.destinationLaneIndex == laneIndex
                && EvaluateConveyorDataMotionProgress(motionState) >= 1f - 0.0001f;
        }

        return true;
    }

    private bool IsConveyorItemReadyToMoveAtLane(int laneIndex)
    {
        if (IsConveyorLaneMovementHeld(laneIndex))
        {
            return false;
        }

        PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
        if (portableObject != null)
        {
            return IsConveyorObjectReadyToMoveAtLane(laneIndex, portableObject);
        }

        if (!HasConveyorItemAtLane(laneIndex))
        {
            return false;
        }

        if (laneIndex >= 0
            && laneIndex < conveyorItemMotionStates.Count
            && conveyorItemMotionStates[laneIndex].active)
        {
            ConveyorDataMotionState motionState = EnsureConveyorDataMotionTiming(conveyorItemMotionStates[laneIndex]);
            if (motionState.destinationLaneIndex != laneIndex)
            {
                return false;
            }

            return EvaluateConveyorDataMotionProgress(motionState) >= 1f - ConveyorContinuousMotionEpsilon;
        }

        return true;
    }

    private bool IsConveyorObjectReadyToMoveAtLane(int laneIndex, PortableObject portableObject)
    {
        if (portableObject == null || portableObject.IsMovingToTarget)
        {
            return false;
        }

        if (conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState cornerMotionState))
        {
            if (cornerMotionState.destinationLaneIndex != laneIndex)
            {
                return false;
            }

            return cornerMotionState.progress >= 1f - ConveyorContinuousMotionEpsilon;
        }

        if (conveyorLinearMotionStates.TryGetValue(portableObject, out ConveyorLinearMotionState linearMotionState))
        {
            if (linearMotionState.destinationLaneIndex != laneIndex)
            {
                return false;
            }

            return linearMotionState.progress >= 1f - ConveyorContinuousMotionEpsilon;
        }

        return IsConveyorObjectSettledAtLane(laneIndex, portableObject);
    }

    private bool CanTreatConveyorLaneAsCycleReady(ConveyorLaneKey rootLane, ConveyorLaneKey currentLane)
    {
        if (WasConveyorItemMovedThisFrame(currentLane.LaneIndex)
            || rootLane.Block == null
            || currentLane.Block == null
            || rootLane.Block != this
            || currentLane.Block != this
            || rootLane.Block != currentLane.Block
            || !IsCornerConveyor())
        {
            return false;
        }

        if (!TryGetConveyorCornerSourceLaneForDestination(rootLane.LaneIndex, out int pairedSourceLaneIndex)
            || currentLane.LaneIndex != pairedSourceLaneIndex)
        {
            return false;
        }

        Vector3 targetPosition = GetConveyorLaneWorldPosition(currentLane.LaneIndex);
        Vector3 delta = GetConveyorItemVisualWorldPosition(currentLane.LaneIndex) - targetPosition;
        delta.y = 0f;
        return delta.sqrMagnitude <= ConveyorCycleReadyDistance * ConveyorCycleReadyDistance;
    }

    private bool TryGetConveyorCornerSourceLaneForDestination(int destinationLaneIndex, out int sourceLaneIndex)
    {
        sourceLaneIndex = -1;
        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        if (destinationLaneIndex == outerDestinationLaneIndex)
        {
            sourceLaneIndex = outerSourceLaneIndex;
            return true;
        }

        if (destinationLaneIndex == innerDestinationLaneIndex)
        {
            sourceLaneIndex = innerSourceLaneIndex;
            return true;
        }

        return false;
    }

    private bool TryGetPairedConveyorCornerTransition(
        int sourceLaneIndex,
        int destinationLaneIndex,
        out int pairedSourceLaneIndex,
        out int pairedDestinationLaneIndex)
    {
        pairedSourceLaneIndex = -1;
        pairedDestinationLaneIndex = -1;
        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        if (sourceLaneIndex == outerSourceLaneIndex && destinationLaneIndex == outerDestinationLaneIndex)
        {
            if (innerSourceLaneIndex < 0 || innerDestinationLaneIndex < 0)
            {
                return false;
            }

            pairedSourceLaneIndex = innerSourceLaneIndex;
            pairedDestinationLaneIndex = innerDestinationLaneIndex;
            return true;
        }

        if (sourceLaneIndex == innerSourceLaneIndex && destinationLaneIndex == innerDestinationLaneIndex)
        {
            if (outerSourceLaneIndex < 0 || outerDestinationLaneIndex < 0)
            {
                return false;
            }

            pairedSourceLaneIndex = outerSourceLaneIndex;
            pairedDestinationLaneIndex = outerDestinationLaneIndex;
            return true;
        }

        return false;
    }

    private bool TryGetNextConveyorBlock(out Block nextBlock)
    {
        if (conveyorConnectionCacheDirty)
        {
            RebuildConveyorConnectionCache();
        }

        nextBlock = cachedNextConveyorBlock;
        return cachedHasNextConveyorBlock && nextBlock != null;
    }

    private void RebuildConveyorConnectionCache()
    {
        cachedNextConveyorBlock = null;
        cachedHasNextConveyorBlock = false;
        conveyorConnectionCacheDirty = false;

        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            return;
        }

        if (!TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator))
        {
            return;
        }

        Vector2Int nextCoordinate = coordinate + flowDirection;
        if (!terrainGenerator.TryGetLoadedBlock(nextCoordinate, out Block nextBlock) || nextBlock == null || nextBlock == this)
        {
            return;
        }

        if (TryGetRuntimeBelt2F(out ConvayorBelt2F currentBelt2F)
            && nextBlock.IsBelt2FBridgeCenterFor(currentBelt2F))
        {
            cachedNextConveyorBlock = nextBlock;
            cachedHasNextConveyorBlock = true;
            return;
        }

        if (TryResolveBelt2FSkippedAdjacentBlock(terrainGenerator, nextBlock, flowDirection, out Block skippedNextBlock))
        {
            nextBlock = skippedNextBlock;
        }

        if (!nextBlock.IsConveyorStackingEnabled())
        {
            return;
        }

        if (!nextBlock.TryGetConveyorInputDirection(out Vector2Int nextInputDirection) || nextInputDirection == Vector2Int.zero)
        {
            return;
        }

        if (!CanReceiveConveyorHandoff(nextBlock, nextInputDirection, flowDirection))
        {
            return;
        }

        cachedNextConveyorBlock = nextBlock;
        cachedHasNextConveyorBlock = true;
    }

    private bool TryGetPreviousConveyorBlock(out Block previousBlock)
    {
        previousBlock = null;
        if (!TryGetConveyorInputDirection(out Vector2Int inputDirection) || inputDirection == Vector2Int.zero)
        {
            return false;
        }

        if (!TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator))
        {
            return false;
        }

        Vector2Int previousCoordinate = coordinate + inputDirection;
        if (!terrainGenerator.TryGetLoadedBlock(previousCoordinate, out previousBlock) || previousBlock == null || previousBlock == this)
        {
            return false;
        }

        if (TryGetRuntimeBelt2F(out ConvayorBelt2F currentBelt2F)
            && previousBlock.IsBelt2FBridgeCenterFor(currentBelt2F))
        {
            return true;
        }

        if (TryResolveBelt2FSkippedAdjacentBlock(terrainGenerator, previousBlock, inputDirection, out Block skippedPreviousBlock))
        {
            previousBlock = skippedPreviousBlock;
        }

        if (!previousBlock.IsConveyorStackingEnabled())
        {
            return false;
        }

        if (!previousBlock.TryGetConveyorFlowDirection(out Vector2Int previousFlowDirection) || previousFlowDirection == Vector2Int.zero)
        {
            return false;
        }

        return previousFlowDirection == -inputDirection;
    }

    private static bool CanReceiveConveyorHandoff(
        Block receiverBlock,
        Vector2Int receiverInputDirection,
        Vector2Int incomingFlowDirection)
    {
        if (receiverInputDirection == Vector2Int.zero || incomingFlowDirection == Vector2Int.zero)
        {
            return false;
        }

        if (receiverInputDirection == -incomingFlowDirection)
        {
            return true;
        }

        return receiverBlock != null
            && !receiverBlock.IsCornerConveyorBlock()
            && IsPerpendicularCardinal(receiverInputDirection, incomingFlowDirection);
    }

    private static bool IsPerpendicularCardinal(Vector2Int left, Vector2Int right)
    {
        return left != Vector2Int.zero
            && right != Vector2Int.zero
            && ((left.x * right.x) + (left.y * right.y)) == 0;
    }

    private bool TryResolveBelt2FSkippedAdjacentBlock(
        TerrainGenerator terrainGenerator,
        Block immediateBlock,
        Vector2Int direction,
        out Block resolvedBlock)
    {
        resolvedBlock = null;
        if (terrainGenerator == null
            || immediateBlock == null
            || direction == Vector2Int.zero
            || !TryGetRuntimeConveyorBelt(out ConveyorBelt runtimeConveyor)
            || !(runtimeConveyor is ConvayorBelt2F belt2F)
            || belt2F == null
            || !belt2F.CoversCoordinate(coordinate)
            || !belt2F.CoversCoordinate(immediateBlock.Coordinate)
            || immediateBlock.IsBelt2FBridgeCenterFor(belt2F)
            || !(immediateBlock.MapObject is ConveyorBelt centerConveyor)
            || centerConveyor is ConvayorBelt2F)
        {
            return false;
        }

        Vector2Int skippedCoordinate = immediateBlock.Coordinate + direction;
        if (!terrainGenerator.TryGetLoadedBlock(skippedCoordinate, out Block candidateBlock)
            || candidateBlock == null
            || candidateBlock == this
            || !belt2F.CoversCoordinate(candidateBlock.Coordinate))
        {
            return false;
        }

        resolvedBlock = candidateBlock;
        return true;
    }

    private bool TryGetConveyorPredecessor(
        int destinationLaneIndex,
        out Block sourceBlock,
        out int sourceLaneIndex,
        out bool useCornerMotion)
    {
        sourceBlock = null;
        sourceLaneIndex = -1;
        useCornerMotion = false;

        CleanupConveyorStack();
        if (!IsValidConveyorLaneIndex(destinationLaneIndex))
        {
            return false;
        }

        if (TryGetBelt2FBridgeCenterPredecessor(destinationLaneIndex, out sourceBlock, out sourceLaneIndex))
        {
            return true;
        }

        if (IsCornerConveyor())
        {
            if (!TryGetConveyorCornerLaneCandidates(
                    out int firstSourceLaneIndex,
                    out int firstDestinationLaneIndex,
                    out int secondSourceLaneIndex,
                    out int secondDestinationLaneIndex))
            {
                return false;
            }

            if (destinationLaneIndex == firstDestinationLaneIndex)
            {
                sourceBlock = this;
                sourceLaneIndex = firstSourceLaneIndex;
                useCornerMotion = true;
                return true;
            }

            if (destinationLaneIndex == secondDestinationLaneIndex)
            {
                sourceBlock = this;
                sourceLaneIndex = secondSourceLaneIndex;
                useCornerMotion = true;
                return true;
            }

            return TryGetUpstreamConveyorPredecessor(destinationLaneIndex, out sourceBlock, out sourceLaneIndex);
        }

        if (!TryGetConveyorLaneLayout(out int frontLaneIndex, out int backLaneIndex))
        {
            return false;
        }

        if (destinationLaneIndex == frontLaneIndex)
        {
            sourceBlock = this;
            sourceLaneIndex = backLaneIndex;
            return true;
        }

        return TryGetUpstreamConveyorPredecessor(destinationLaneIndex, out sourceBlock, out sourceLaneIndex);
    }

    private bool TryGetBelt2FBridgeCenterPredecessor(
        int destinationLaneIndex,
        out Block sourceBlock,
        out int sourceLaneIndex)
    {
        sourceBlock = null;
        sourceLaneIndex = -1;
        if (!TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F belt2F))
        {
            return false;
        }

        if (destinationLaneIndex == 1)
        {
            sourceBlock = this;
            sourceLaneIndex = 3;
            return IsValidConveyorLaneIndex(sourceLaneIndex);
        }

        if (destinationLaneIndex != 3
            || !belt2F.TryGetInputDirection(belt2F.transform.rotation, out Vector2Int inputDirection)
            || inputDirection == Vector2Int.zero
            || !TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator)
            || terrainGenerator == null)
        {
            return false;
        }

        Vector2Int previousCoordinate = coordinate + inputDirection;
        if (!terrainGenerator.TryGetLoadedBlock(previousCoordinate, out Block previousBlock)
            || previousBlock == null
            || previousBlock == this
            || !belt2F.CoversCoordinate(previousBlock.Coordinate))
        {
            return false;
        }

        int candidateSourceLaneIndex = ConveyorSingleLineFrontLaneIndex;
        if (!previousBlock.IsValidConveyorLaneIndex(candidateSourceLaneIndex)
            || !previousBlock.TryGetConveyorSuccessor(
                candidateSourceLaneIndex,
                out Block candidateDestinationBlock,
                out int candidateDestinationLaneIndex,
                out _)
            || candidateDestinationBlock != this
            || candidateDestinationLaneIndex != destinationLaneIndex)
        {
            return false;
        }

        sourceBlock = previousBlock;
        sourceLaneIndex = candidateSourceLaneIndex;
        return true;
    }

    private bool TryGetUpstreamConveyorPredecessor(
        int destinationLaneIndex,
        out Block sourceBlock,
        out int sourceLaneIndex)
    {
        sourceBlock = null;
        sourceLaneIndex = -1;

        if (!TryGetPreviousConveyorBlock(out Block previousBlock) || previousBlock == null)
        {
            return false;
        }

        previousBlock.CleanupConveyorStack();
        int previousLaneCount = previousBlock.GetConveyorLaneCount();
        for (int candidateLaneIndex = 0; candidateLaneIndex < previousLaneCount; candidateLaneIndex++)
        {
            if (!previousBlock.TryGetConveyorSuccessor(
                    candidateLaneIndex,
                    out Block candidateDestinationBlock,
                    out int candidateDestinationLaneIndex,
                    out _))
            {
                continue;
            }

            if (candidateDestinationBlock == this && candidateDestinationLaneIndex == destinationLaneIndex)
            {
                sourceBlock = previousBlock;
                sourceLaneIndex = candidateLaneIndex;
                return true;
            }
        }

        return false;
    }

    private bool TryGetConveyorSuccessor(
        int sourceLaneIndex,
        out Block destinationBlock,
        out int destinationLaneIndex,
        out bool useCornerMotion)
    {
        destinationBlock = null;
        destinationLaneIndex = -1;
        useCornerMotion = false;

        if (!IsValidConveyorLaneIndex(sourceLaneIndex)
            || sourceLaneIndex >= ConveyorStackLaneLimit)
        {
            return false;
        }

        if (conveyorSuccessorCacheDirty)
        {
            RebuildConveyorSuccessorCache();
        }

        if (!cachedConveyorSuccessorExists[sourceLaneIndex])
        {
            return false;
        }

        destinationBlock = cachedConveyorSuccessorBlocks[sourceLaneIndex];
        destinationLaneIndex = cachedConveyorSuccessorLaneIndices[sourceLaneIndex];
        useCornerMotion = cachedConveyorSuccessorUsesCornerMotion[sourceLaneIndex];
        return destinationBlock != null;
    }

    private void RebuildConveyorSuccessorCache()
    {
        conveyorSuccessorCacheDirty = false;
        for (int laneIndex = 0; laneIndex < ConveyorStackLaneLimit; laneIndex++)
        {
            cachedConveyorSuccessorBlocks[laneIndex] = null;
            cachedConveyorSuccessorLaneIndices[laneIndex] = -1;
            cachedConveyorSuccessorExists[laneIndex] = false;
            cachedConveyorSuccessorUsesCornerMotion[laneIndex] = false;
        }

        int laneCount = Mathf.Min(GetConveyorLaneCount(), ConveyorStackLaneLimit);
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (!TryResolveConveyorSuccessorUncached(
                    laneIndex,
                    out Block destinationBlock,
                    out int destinationLaneIndex,
                    out bool useCornerMotion))
            {
                continue;
            }

            cachedConveyorSuccessorBlocks[laneIndex] = destinationBlock;
            cachedConveyorSuccessorLaneIndices[laneIndex] = destinationLaneIndex;
            cachedConveyorSuccessorExists[laneIndex] = true;
            cachedConveyorSuccessorUsesCornerMotion[laneIndex] = useCornerMotion;
        }
    }

    private bool TryResolveConveyorSuccessorUncached(
        int sourceLaneIndex,
        out Block destinationBlock,
        out int destinationLaneIndex,
        out bool useCornerMotion)
    {
        destinationBlock = null;
        destinationLaneIndex = -1;
        useCornerMotion = false;

        if (!IsValidConveyorLaneIndex(sourceLaneIndex))
        {
            return false;
        }

        if (TryResolveBelt2FBridgeCenterSuccessor(
                sourceLaneIndex,
                out destinationBlock,
                out destinationLaneIndex))
        {
            return true;
        }

        if (IsCornerConveyor())
        {
            if (sourceLaneIndex == 0 || sourceLaneIndex == 1)
            {
                if (!TryGetNextConveyorBlock(out destinationBlock))
                {
                    return false;
                }

                Vector3 handoffWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
                if (TryGetConveyorCornerLaneTransition(sourceLaneIndex, out int cornerSourceLaneIndex, out int cornerDestinationLaneIndex, out _)
                    && TryGetCornerConveyorHandoffWorldPosition(cornerSourceLaneIndex, cornerDestinationLaneIndex, out Vector3 resolvedHandoffWorldPosition))
                {
                    handoffWorldPosition = resolvedHandoffWorldPosition;
                }

                return TryGetConveyorHandoffReceiveLaneIndex(
                    destinationBlock,
                    handoffWorldPosition,
                    out destinationLaneIndex);
            }

            if (!TryGetConveyorCornerLaneCandidates(
                    out int outerSourceLaneIndex,
                    out int outerDestinationLaneIndex,
                    out int innerSourceLaneIndex,
                    out int innerDestinationLaneIndex))
            {
                return false;
            }

            if (sourceLaneIndex == outerSourceLaneIndex)
            {
                destinationBlock = this;
                destinationLaneIndex = outerDestinationLaneIndex;
                useCornerMotion = true;
                return true;
            }

            if (sourceLaneIndex == innerSourceLaneIndex)
            {
                destinationBlock = this;
                destinationLaneIndex = innerDestinationLaneIndex;
                useCornerMotion = true;
                return true;
            }

            return false;
        }

        if (!TryGetConveyorLaneLayout(out int frontLaneIndex, out int backLaneIndex))
        {
            return false;
        }

        if (sourceLaneIndex == frontLaneIndex)
        {
            if (!TryGetNextConveyorBlock(out destinationBlock))
            {
                return false;
            }

            Vector3 handoffWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
            return TryGetConveyorHandoffReceiveLaneIndex(
                destinationBlock,
                handoffWorldPosition,
                out destinationLaneIndex);
        }

        if (sourceLaneIndex == backLaneIndex)
        {
            destinationBlock = this;
            destinationLaneIndex = frontLaneIndex;
            return true;
        }

        return false;
    }

    private bool TryResolveBelt2FBridgeCenterSuccessor(
        int sourceLaneIndex,
        out Block destinationBlock,
        out int destinationLaneIndex)
    {
        destinationBlock = null;
        destinationLaneIndex = -1;
        if (!TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F belt2F))
        {
            return false;
        }

        if (sourceLaneIndex == 3)
        {
            destinationBlock = this;
            destinationLaneIndex = 1;
            return IsValidConveyorLaneIndex(destinationLaneIndex);
        }

        if (sourceLaneIndex != 1
            || !belt2F.TryGetOutputDirection(belt2F.transform.rotation, out Vector2Int outputDirection)
            || outputDirection == Vector2Int.zero
            || !TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator)
            || terrainGenerator == null)
        {
            return false;
        }

        Vector2Int nextCoordinate = coordinate + outputDirection;
        if (!terrainGenerator.TryGetLoadedBlock(nextCoordinate, out destinationBlock)
            || destinationBlock == null
            || destinationBlock == this
            || !belt2F.CoversCoordinate(destinationBlock.Coordinate)
            || !destinationBlock.IsConveyorStackingEnabled())
        {
            destinationBlock = null;
            return false;
        }

        Vector3 handoffWorldPosition = GetConveyorLaneWorldPosition(sourceLaneIndex);
        return TryGetConveyorHandoffReceiveLaneIndex(
            destinationBlock,
            handoffWorldPosition,
            out destinationLaneIndex);
    }

    private bool TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator)
    {
        terrainGenerator = cachedTerrainGenerator;
        if (terrainGenerator != null)
        {
            return true;
        }

        cachedTerrainGenerator = GetComponentInParent<TerrainGenerator>();
        terrainGenerator = cachedTerrainGenerator;
        return terrainGenerator != null;
    }

    private bool TryGetConveyorFlowDirection(out Vector2Int flowDirection)
    {
        flowDirection = Vector2Int.zero;
        if (!TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt))
        {
            return false;
        }

        return conveyorBelt.TryGetOutputDirection(conveyorBelt.transform.rotation, out flowDirection);
    }

    private bool TryGetConveyorInputDirection(out Vector2Int inputDirection)
    {
        inputDirection = Vector2Int.zero;
        if (!TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt))
        {
            return false;
        }

        return conveyorBelt.TryGetInputDirection(conveyorBelt.transform.rotation, out inputDirection);
    }

    private bool TryReceiveConveyorObject(PortableObject portableObject, Vector3 sourceWorldPosition, out int laneIndex)
    {
        laneIndex = -1;
        if (portableObject == null)
        {
            return false;
        }

        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled() || !TryGetBestConveyorReceiveLaneIndex(sourceWorldPosition, out laneIndex))
        {
            return false;
        }

        portableObject.SetCachedParent(transform, true);
        if (!ShouldUseVirtualConveyorItemRendering())
        {
            portableObject.SetCachedActive(true);
        }

        ApplyConveyorObjectRenderingMode(portableObject);
        SetConveyorItemAtLane(laneIndex, portableObject.ItemId, portableObject, ConveyorPickupGateState.Settled());
        WakeConveyorMoveAttemptsAround();
        RefreshConveyorActivityRegistration();

        DroppedItemPickupGate gate = portableObject.PickupGate;
        gate?.MarkSettled();
        TryVirtualizeSettledConveyorPortableObject(laneIndex, portableObject);
        conveyorCornerMotionStates.Remove(portableObject);
        conveyorLinearMotionStates.Remove(portableObject);
        return true;
    }

    private bool TryGetBestConveyorReceiveLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        if (TryGetBelt2FBridgeReceiveLaneIndex(referenceWorldPosition, true, out bestLaneIndex))
        {
            return true;
        }

        if (IsCornerConveyor())
        {
            return TryGetBestCornerConveyorReceiveLaneIndex(referenceWorldPosition, out bestLaneIndex);
        }

        if (!TryGetConveyorLaneLayout(out _, out int backLaneIndex))
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        TryConsiderBestConveyorLane(referenceWorldPosition, backLaneIndex, true, ref bestLaneIndex, ref bestDistanceSqr);
        return bestLaneIndex >= 0;
    }

    private bool TryGetPreferredConveyorReceiveLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        if (TryGetBelt2FBridgeReceiveLaneIndex(referenceWorldPosition, false, out bestLaneIndex))
        {
            return true;
        }

        if (IsCornerConveyor())
        {
            return TryGetPreferredCornerConveyorReceiveLaneIndex(referenceWorldPosition, out bestLaneIndex);
        }

        if (!TryGetConveyorLaneLayout(out _, out int backLaneIndex))
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        TryConsiderBestConveyorLane(referenceWorldPosition, backLaneIndex, false, ref bestLaneIndex, ref bestDistanceSqr);
        return bestLaneIndex >= 0;
    }

    private static bool TryGetConveyorHandoffReceiveLaneIndex(
        Block destinationBlock,
        Vector3 handoffWorldPosition,
        out int destinationLaneIndex)
    {
        destinationLaneIndex = -1;
        return destinationBlock != null
            && destinationBlock.TryGetConveyorReceiveLaneIndexForHandoffPosition(
                handoffWorldPosition,
                out destinationLaneIndex);
    }

    private bool TryGetConveyorReceiveLaneIndexForHandoffPosition(
        Vector3 handoffWorldPosition,
        out int laneIndex)
    {
        laneIndex = -1;
        if (TryGetBelt2FBridgeReceiveLaneIndex(handoffWorldPosition, false, out laneIndex))
        {
            return true;
        }

        if (IsCornerConveyor())
        {
            return TryGetPreferredCornerConveyorReceiveLaneIndex(handoffWorldPosition, out laneIndex);
        }

        return TryGetConveyorLaneLayout(out _, out laneIndex)
            && IsValidConveyorLaneIndex(laneIndex);
    }

    private bool TryGetBelt2FBridgeReceiveLaneIndex(
        Vector3 referenceWorldPosition,
        bool requireEmpty,
        out int laneIndex)
    {
        laneIndex = -1;
        if (!TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F belt2F)
            || !belt2F.IsUpperPathWorldPosition(referenceWorldPosition))
        {
            return false;
        }

        const int bridgeBackLaneIndex = 3;
        if (!IsValidConveyorLaneIndex(bridgeBackLaneIndex)
            || (requireEmpty && HasConveyorItemAtLane(bridgeBackLaneIndex)))
        {
            return false;
        }

        laneIndex = bridgeBackLaneIndex;
        return true;
    }

    private bool TryGetBestConveyorLaneIndexFromRange(Vector3 referenceWorldPosition, int laneCount, out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        float bestDistanceSqr = float.MaxValue;
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            TryConsiderBestConveyorLane(referenceWorldPosition, laneIndex, true, ref bestLaneIndex, ref bestDistanceSqr);
        }

        return bestLaneIndex >= 0;
    }

    private void TryConsiderBestConveyorLane(
        Vector3 referenceWorldPosition,
        int laneIndex,
        bool requireEmpty,
        ref int bestLaneIndex,
        ref float bestDistanceSqr)
    {
        if (!IsValidConveyorLaneIndex(laneIndex) || (requireEmpty && HasConveyorItemAtLane(laneIndex)))
        {
            return;
        }

        Vector3 offset = GetConveyorLaneWorldPosition(laneIndex) - referenceWorldPosition;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (bestLaneIndex >= 0 && distanceSqr >= bestDistanceSqr)
        {
            return;
        }

        bestDistanceSqr = distanceSqr;
        bestLaneIndex = laneIndex;
    }

    private bool TryGetConveyorLocalAxes(out Vector2 localFlowAxis, out Vector2 localRightAxis)
    {
        localFlowAxis = Vector2.zero;
        localRightAxis = Vector2.zero;
        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            return false;
        }

        Vector3 localFlowDirection3 = transform.InverseTransformDirection(new Vector3(flowDirection.x, 0f, flowDirection.y));
        Vector2 localFlowDirection = new Vector2(localFlowDirection3.x, localFlowDirection3.z);
        if (localFlowDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        localFlowAxis = localFlowDirection.normalized;
        localRightAxis = new Vector2(-localFlowAxis.y, localFlowAxis.x);
        return true;
    }

    private bool TryGetConveyorLaneLayout(
        out int frontLaneIndex,
        out int backLaneIndex)
    {
        if (conveyorLaneLayoutCacheDirty)
        {
            RebuildConveyorLaneLayoutCache();
        }

        frontLaneIndex = cachedFrontLaneIndex;
        backLaneIndex = cachedBackLaneIndex;
        return cachedConveyorLaneLayoutValid;
    }

    private void RebuildConveyorLaneLayoutCache()
    {
        cachedFrontLaneIndex = -1;
        cachedBackLaneIndex = -1;
        cachedConveyorLaneLayoutValid = false;
        conveyorLaneLayoutCacheDirty = false;

        if (!IsValidConveyorLaneIndex(ConveyorSingleLineFrontLaneIndex)
            || !IsValidConveyorLaneIndex(ConveyorSingleLineBackLaneIndex))
        {
            return;
        }

        cachedFrontLaneIndex = ConveyorSingleLineFrontLaneIndex;
        cachedBackLaneIndex = ConveyorSingleLineBackLaneIndex;
        cachedConveyorLaneLayoutValid = true;
    }

    private bool IsCornerConveyor()
    {
        return TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt)
               && conveyorBelt.IsCornerVariant;
    }

    private Vector3 GetConveyorLaneWorldPosition(int laneIndex, Transform anchor = null)
    {
        if (TryGetConveyorCornerLaneLocalPosition(laneIndex, out Vector3 cornerLocalPosition))
        {
            cornerLocalPosition.y = GetConveyorLaneHeight();
            return ResolveConveyorLaneWorldPosition(laneIndex, transform.TransformPoint(cornerLocalPosition));
        }

        Vector3 localPosition = GetConveyorLaneLocalOffset(laneIndex);
        localPosition.y = GetConveyorLaneHeight();

        return ResolveConveyorLaneWorldPosition(laneIndex, transform.TransformPoint(localPosition));
    }

    private Vector3 ResolveConveyorLaneWorldPosition(int laneIndex, Vector3 worldPosition)
    {
        if (IsBelt2FBridgeLaneIndex(laneIndex)
            && TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F centerBelt2F))
        {
            return centerBelt2F.TryGetLaneWorldPosition(coordinate, laneIndex, worldPosition, out Vector3 bridgeLaneWorldPosition)
                ? bridgeLaneWorldPosition
                : centerBelt2F.ApplyPathHeight(worldPosition);
        }

        if (TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt)
            && conveyorBelt is ConvayorBelt2F belt2F)
        {
            return belt2F.TryGetLaneWorldPosition(coordinate, laneIndex, worldPosition, out Vector3 laneWorldPosition)
                ? laneWorldPosition
                : belt2F.ApplyPathHeight(worldPosition);
        }

        return worldPosition;
    }

    private Vector3 GetConveyorLaneLocalOffset(int laneIndex)
    {
        float halfExtent = GetConveyorLaneHalfExtent();
        Vector2 localFlowAxis = Vector2.down;
        if (!TryGetConveyorLocalAxes(out localFlowAxis, out _))
        {
            localFlowAxis = Vector2.down;
        }

        Vector2 frontOffset = localFlowAxis.normalized * halfExtent;
        Vector2 backOffset = -frontOffset;
        switch (laneIndex)
        {
            case 0:
            case 1:
                return new Vector3(frontOffset.x, 0f, frontOffset.y);
            case 2:
            case 3:
                return new Vector3(backOffset.x, 0f, backOffset.y);
            default:
                return Vector3.zero;
        }
    }

    private bool TryGetConveyorCornerLaneLocalPosition(int laneIndex, out Vector3 localPosition)
    {
        localPosition = Vector3.zero;
        if (!IsCornerConveyor() || laneIndex < 0 || laneIndex >= ConveyorStackLaneLimit)
        {
            return false;
        }

        if (!TryGetConveyorCornerLaneTransition(laneIndex, out int sourceLaneIndex, out int destinationLaneIndex, out float progress))
        {
            return false;
        }

        Vector3 worldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, progress);
        localPosition = transform.InverseTransformPoint(worldPosition);
        localPosition.y = 0f;
        return true;
    }

    private bool TryGetConveyorCornerLaneTransition(int laneIndex, out int sourceLaneIndex, out int destinationLaneIndex, out float progress)
    {
        sourceLaneIndex = -1;
        destinationLaneIndex = -1;
        progress = 0f;

        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        if (laneIndex == outerSourceLaneIndex)
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = 0f;
            return true;
        }

        if (innerSourceLaneIndex >= 0 && laneIndex == innerSourceLaneIndex)
        {
            sourceLaneIndex = innerSourceLaneIndex;
            destinationLaneIndex = innerDestinationLaneIndex;
            progress = 0f;
            return true;
        }

        if (laneIndex == outerDestinationLaneIndex)
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = 1f;
            return true;
        }

        if (innerDestinationLaneIndex >= 0 && laneIndex == innerDestinationLaneIndex)
        {
            sourceLaneIndex = innerSourceLaneIndex;
            destinationLaneIndex = innerDestinationLaneIndex;
            progress = 1f;
            return true;
        }

        return false;
    }

    private bool TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection)
    {
        localInputDirection = Vector2.zero;
        localOutputDirection = Vector2.zero;
        if (!TryGetRuntimeConveyorBelt(out ConveyorBelt conveyorBelt))
        {
            return false;
        }

        if (!conveyorBelt.TryGetInputDirection(conveyorBelt.transform.rotation, out Vector2Int inputDirection)
            || !conveyorBelt.TryGetOutputDirection(conveyorBelt.transform.rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        Vector3 localInputDirection3 = transform.InverseTransformDirection(new Vector3(inputDirection.x, 0f, inputDirection.y));
        Vector3 localOutputDirection3 = transform.InverseTransformDirection(new Vector3(outputDirection.x, 0f, outputDirection.y));
        localInputDirection = new Vector2(Mathf.Round(localInputDirection3.x), Mathf.Round(localInputDirection3.z));
        localOutputDirection = new Vector2(Mathf.Round(localOutputDirection3.x), Mathf.Round(localOutputDirection3.z));
        return localInputDirection.sqrMagnitude > 0.5f && localOutputDirection.sqrMagnitude > 0.5f;
    }

    private bool TryGetCornerConveyorCarryDirection(Vector3 worldPosition, out Vector3 carryDirection)
    {
        carryDirection = Vector3.zero;
        if (!TryGetConveyorCornerCenterlineArcParameters(out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out _))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 radial = new Vector2(localPosition3.x, localPosition3.z) - center;
        if (radial.sqrMagnitude <= 0.0001f)
        {
            if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
            {
                return false;
            }

            carryDirection = new Vector3(flowDirection.x, 0f, flowDirection.y);
            return carryDirection.sqrMagnitude > 0.0001f;
        }

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float startAngleDegrees = startAngleRadians * Mathf.Rad2Deg;
        float deltaAngleDegrees = deltaAngleRadians * Mathf.Rad2Deg;
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleDegrees, angleRadians * Mathf.Rad2Deg);
        float clampedProgress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleDegrees) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleDegrees);
        float clampedAngleRadians = startAngleRadians + (deltaAngleRadians * clampedProgress);

        Vector2 localTangent = Mathf.Sign(deltaAngleRadians) >= 0f
            ? new Vector2(-Mathf.Sin(clampedAngleRadians), Mathf.Cos(clampedAngleRadians))
            : new Vector2(Mathf.Sin(clampedAngleRadians), -Mathf.Cos(clampedAngleRadians));

        Vector3 worldDirection = transform.TransformDirection(new Vector3(localTangent.x, 0f, localTangent.y));
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        carryDirection = worldDirection.normalized;
        return true;
    }

    private bool TryGetCornerConveyorCarryDelta(Vector3 worldPosition, float conveyorSpeed, float deltaTime, out Vector3 delta)
    {
        delta = Vector3.zero;
        if (!TryGetClosestCornerConveyorCarryProjection(
                worldPosition,
                out int sourceLaneIndex,
                out int destinationLaneIndex,
                out float progress,
                out _))
        {
            return false;
        }

        float pathLength = GetConveyorCornerCarryPathLength(sourceLaneIndex, destinationLaneIndex);
        if (pathLength <= 0.0001f)
        {
            return false;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float currentDistanceAlongPath = progress * pathLength;
        float nextDistanceAlongPath = currentDistanceAlongPath + travelDistance;
        float clampedNextDistanceAlongPath = Mathf.Min(nextDistanceAlongPath, pathLength);
        float nextProgress = Mathf.Clamp01(clampedNextDistanceAlongPath / pathLength);
        Vector3 nextWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, nextProgress);

        if (nextDistanceAlongPath > pathLength
            && TryGetConveyorFlowDirection(out Vector2Int flowDirection)
            && flowDirection != Vector2Int.zero)
        {
            float overflowDistance = nextDistanceAlongPath - pathLength;
            Vector3 outputDirection = new Vector3(flowDirection.x, 0f, flowDirection.y).normalized;
            nextWorldPosition += outputDirection * overflowDistance;
        }

        delta = nextWorldPosition - worldPosition;
        delta.y = 0f;
        return delta.sqrMagnitude > 0.0000001f;
    }

    private bool TryGetCornerConveyorCarryDeltaWithHandoff(Vector3 worldPosition, float conveyorSpeed, float deltaTime, out Block resultingBlock, out Vector3 delta)
    {
        resultingBlock = this;
        delta = Vector3.zero;
        if (!TryGetClosestCornerConveyorCarryProjection(
                worldPosition,
                out int sourceLaneIndex,
                out int destinationLaneIndex,
                out float progress,
                out _))
        {
            return false;
        }

        float pathLength = GetConveyorCornerCarryPathLength(sourceLaneIndex, destinationLaneIndex);
        if (pathLength <= 0.0001f)
        {
            return false;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float currentDistanceAlongPath = progress * pathLength;
        float nextDistanceAlongPath = currentDistanceAlongPath + travelDistance;
        float clampedNextDistanceAlongPath = Mathf.Min(nextDistanceAlongPath, pathLength);
        float nextProgress = Mathf.Clamp01(clampedNextDistanceAlongPath / pathLength);
        Vector3 endOfCornerPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, 1f);
        Vector3 handoffPosition = endOfCornerPosition;
        if (TryGetCornerConveyorHandoffWorldPosition(sourceLaneIndex, destinationLaneIndex, out Vector3 resolvedHandoffPosition))
        {
            handoffPosition = resolvedHandoffPosition;
        }

        if (nextDistanceAlongPath <= pathLength + 0.0001f)
        {
            Vector3 nextWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, nextProgress);
            delta = nextWorldPosition - worldPosition;
            delta.y = 0f;
            resultingBlock = this;
            return delta.sqrMagnitude > 0.0000001f;
        }

        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            delta = handoffPosition - worldPosition;
            delta.y = 0f;
            resultingBlock = this;
            return delta.sqrMagnitude > 0.0000001f;
        }

        float overflowDistance = nextDistanceAlongPath - pathLength;
        float remainingDeltaTime = overflowDistance / Mathf.Max(conveyorSpeed, 0.0001f);
        if (TryGetNextConveyorBlock(out Block nextBlock)
            && nextBlock != null
            && nextBlock.TryGetConveyorCarryDeltaWithHandoff(handoffPosition, remainingDeltaTime, out Block downstreamBlock, out Vector3 downstreamDelta))
        {
            delta = (handoffPosition - worldPosition) + downstreamDelta;
            delta.y = 0f;
            resultingBlock = downstreamBlock != null ? downstreamBlock : nextBlock;
            return delta.sqrMagnitude > 0.0000001f;
        }

        Vector3 outputDirection = new Vector3(flowDirection.x, 0f, flowDirection.y).normalized;
        Vector3 fallbackNextWorldPosition = handoffPosition + (outputDirection * overflowDistance);
        delta = fallbackNextWorldPosition - worldPosition;
        delta.y = 0f;
        resultingBlock = nextBlock != null ? nextBlock : this;
        return delta.sqrMagnitude > 0.0000001f;
    }

    private bool TryGetCornerConveyorStandingDistanceSqr(Vector3 worldPosition, out float distanceSqr)
    {
        distanceSqr = float.MaxValue;
        if (!TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 localPosition = new Vector2(localPosition3.x, localPosition3.z);

        float centerOffset = GetConveyorCornerCenterOffset(localInputDirection, localOutputDirection);
        Vector2 center = (localInputDirection + localOutputDirection) * centerOffset;
        float innerRadius = Mathf.Max(0.01f, GetConveyorCornerLaneRadius(false, centerOffset));
        float outerRadius = Mathf.Max(innerRadius, GetConveyorCornerLaneRadius(true, centerOffset));

        Vector2 radial = localPosition - center;
        float radius = radial.magnitude;
        if (radius <= 0.0001f)
        {
            Vector2 closestPoint = center + (-localOutputDirection * innerRadius);
            distanceSqr = (closestPoint - localPosition).sqrMagnitude;
            return true;
        }

        Vector2 startVector = -localOutputDirection;
        Vector2 endVector = -localInputDirection;
        float startAngleRadians = Mathf.Atan2(startVector.y, startVector.x);
        float endAngleRadians = Mathf.Atan2(endVector.y, endVector.x);
        float deltaAngleRadians = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, endAngleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, angleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float progress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleRadians) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleRadians);

        float clampedAngleRadians = startAngleRadians + (deltaAngleRadians * progress);
        float clampedRadius = Mathf.Clamp(radius, innerRadius, outerRadius);
        Vector2 closestPoint2D = center + new Vector2(Mathf.Cos(clampedAngleRadians), Mathf.Sin(clampedAngleRadians)) * clampedRadius;
        distanceSqr = (closestPoint2D - localPosition).sqrMagnitude;
        return true;
    }

    private bool TryGetClosestCornerConveyorLaneProjection(
        Vector3 worldPosition,
        out int sourceLaneIndex,
        out int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition)
    {
        sourceLaneIndex = -1;
        destinationLaneIndex = -1;
        progress = 0f;
        projectedWorldPosition = worldPosition;

        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        bool hasOuterProjection = TryProjectCornerConveyorPositionOntoLanePath(
            worldPosition,
            outerSourceLaneIndex,
            outerDestinationLaneIndex,
            out float outerProgress,
            out Vector3 outerProjectedWorldPosition,
            out float outerDistanceSqr);
        bool hasInnerProjection = TryProjectCornerConveyorPositionOntoLanePath(
            worldPosition,
            innerSourceLaneIndex,
            innerDestinationLaneIndex,
            out float innerProgress,
            out Vector3 innerProjectedWorldPosition,
            out float innerDistanceSqr);

        if (!hasOuterProjection && !hasInnerProjection)
        {
            return false;
        }

        if (hasOuterProjection && (!hasInnerProjection || outerDistanceSqr <= innerDistanceSqr))
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = outerProgress;
            projectedWorldPosition = outerProjectedWorldPosition;
            return true;
        }

        sourceLaneIndex = innerSourceLaneIndex;
        destinationLaneIndex = innerDestinationLaneIndex;
        progress = innerProgress;
        projectedWorldPosition = innerProjectedWorldPosition;
        return true;
    }

    private bool TryGetClosestCornerConveyorCarryProjection(
        Vector3 worldPosition,
        out int sourceLaneIndex,
        out int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition)
    {
        sourceLaneIndex = -1;
        destinationLaneIndex = -1;
        progress = 0f;
        projectedWorldPosition = worldPosition;

        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        bool hasOuterProjection = TryProjectCornerConveyorPositionOntoCarryPath(
            worldPosition,
            outerSourceLaneIndex,
            outerDestinationLaneIndex,
            out float outerProgress,
            out Vector3 outerProjectedWorldPosition,
            out float outerDistanceSqr);
        bool hasInnerProjection = TryProjectCornerConveyorPositionOntoCarryPath(
            worldPosition,
            innerSourceLaneIndex,
            innerDestinationLaneIndex,
            out float innerProgress,
            out Vector3 innerProjectedWorldPosition,
            out float innerDistanceSqr);

        if (!hasOuterProjection && !hasInnerProjection)
        {
            return false;
        }

        if (hasOuterProjection && (!hasInnerProjection || outerDistanceSqr <= innerDistanceSqr))
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = outerProgress;
            projectedWorldPosition = outerProjectedWorldPosition;
            return true;
        }

        sourceLaneIndex = innerSourceLaneIndex;
        destinationLaneIndex = innerDestinationLaneIndex;
        progress = innerProgress;
        projectedWorldPosition = innerProjectedWorldPosition;
        return true;
    }

    private bool TryGetConveyorCornerLaneCandidates(
        out int outerSourceLaneIndex,
        out int outerDestinationLaneIndex,
        out int innerSourceLaneIndex,
        out int innerDestinationLaneIndex)
    {
        outerSourceLaneIndex = -1;
        outerDestinationLaneIndex = -1;
        innerSourceLaneIndex = -1;
        innerDestinationLaneIndex = -1;

        if (!TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        outerSourceLaneIndex = ConveyorSingleLineBackLaneIndex;
        outerDestinationLaneIndex = ConveyorSingleLineFrontLaneIndex;
        return true;
    }

    private bool TryProjectCornerConveyorPositionOntoLanePath(
        Vector3 worldPosition,
        int sourceLaneIndex,
        int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition,
        out float distanceSqr)
    {
        progress = 0f;
        projectedWorldPosition = worldPosition;
        distanceSqr = float.MaxValue;

        if (!TryGetConveyorCornerArcParameters(
                sourceLaneIndex,
                destinationLaneIndex,
                out Vector2 center,
                out float startAngleRadians,
                out float deltaAngleRadians,
                out _))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 radial = new Vector2(localPosition3.x, localPosition3.z) - center;
        if (radial.sqrMagnitude <= 0.0001f)
        {
            projectedWorldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, 0f);
            Vector3 initialDelta = projectedWorldPosition - worldPosition;
            initialDelta.y = 0f;
            distanceSqr = initialDelta.sqrMagnitude;
            return true;
        }

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float startAngleDegrees = startAngleRadians * Mathf.Rad2Deg;
        float deltaAngleDegrees = deltaAngleRadians * Mathf.Rad2Deg;
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleDegrees, angleRadians * Mathf.Rad2Deg);
        progress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleDegrees) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleDegrees);

        projectedWorldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, progress);
        Vector3 projectedDelta = projectedWorldPosition - worldPosition;
        projectedDelta.y = 0f;
        distanceSqr = projectedDelta.sqrMagnitude;
        return true;
    }

    private bool TryProjectCornerConveyorPositionOntoCarryPath(
        Vector3 worldPosition,
        int sourceLaneIndex,
        int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition,
        out float distanceSqr)
    {
        progress = 0f;
        projectedWorldPosition = worldPosition;
        distanceSqr = float.MaxValue;

        if (!TryGetConveyorCornerCarryArcParameters(
                sourceLaneIndex,
                destinationLaneIndex,
                out Vector2 center,
                out float startAngleRadians,
                out float deltaAngleRadians,
                out _))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 radial = new Vector2(localPosition3.x, localPosition3.z) - center;
        if (radial.sqrMagnitude <= 0.0001f)
        {
            projectedWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, 0f);
            Vector3 initialDelta = projectedWorldPosition - worldPosition;
            initialDelta.y = 0f;
            distanceSqr = initialDelta.sqrMagnitude;
            return true;
        }

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float startAngleDegrees = startAngleRadians * Mathf.Rad2Deg;
        float deltaAngleDegrees = deltaAngleRadians * Mathf.Rad2Deg;
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleDegrees, angleRadians * Mathf.Rad2Deg);
        progress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleDegrees) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleDegrees);

        projectedWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, progress);
        Vector3 projectedDelta = projectedWorldPosition - worldPosition;
        projectedDelta.y = 0f;
        distanceSqr = projectedDelta.sqrMagnitude;
        return true;
    }

    private float GetConveyorCornerPathLength(int sourceLaneIndex, int destinationLaneIndex)
    {
        if (!TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, out _, out _, out float deltaAngleRadians, out float radius))
        {
            return 0f;
        }

        return Mathf.Abs(deltaAngleRadians) * radius;
    }

    private float GetConveyorCornerCenterlinePathLength()
    {
        if (!TryGetConveyorCornerCenterlineArcParameters(out _, out _, out float deltaAngleRadians, out float radius))
        {
            return 0f;
        }

        return Mathf.Abs(deltaAngleRadians) * radius;
    }

    private float GetConveyorCornerMotionPathLength(int sourceLaneIndex, int destinationLaneIndex)
    {
        float centerlineLength = GetConveyorCornerCenterlinePathLength();
        if (centerlineLength > 0.0001f)
        {
            return centerlineLength;
        }

        return GetConveyorCornerPathLength(sourceLaneIndex, destinationLaneIndex);
    }

    private float GetConveyorCornerMotionPathLength(int sourceLaneIndex, int destinationLaneIndex, Vector3 startWorldPosition)
    {
        float arcLength = GetConveyorCornerMotionPathLength(sourceLaneIndex, destinationLaneIndex);
        Vector3 cornerStartWorldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, 0f);
        Vector3 leadInOffset = cornerStartWorldPosition - startWorldPosition;
        leadInOffset.y = 0f;
        float leadInLength = leadInOffset.magnitude;
        return arcLength + leadInLength;
    }

    private float ResolveConveyorCornerMotionPathLength(
        int sourceLaneIndex,
        int destinationLaneIndex,
        Vector3 startWorldPosition,
        float pathLength)
    {
        if (pathLength > ConveyorContinuousMotionEpsilon)
        {
            return pathLength;
        }

        return GetConveyorCornerMotionPathLength(sourceLaneIndex, destinationLaneIndex, startWorldPosition);
    }

    private float ResolveConveyorCornerMotionDurationPathLength(
        int sourceLaneIndex,
        int destinationLaneIndex,
        Vector3 startWorldPosition,
        float pathLength,
        float durationPathLength)
    {
        float resolvedPathLength = ResolveConveyorCornerMotionPathLength(
            sourceLaneIndex,
            destinationLaneIndex,
            startWorldPosition,
            pathLength);
        if (durationPathLength <= ConveyorContinuousMotionEpsilon)
        {
            return resolvedPathLength;
        }

        return Mathf.Max(resolvedPathLength, durationPathLength);
    }

    private float GetSynchronizedConveyorCornerMotionDurationPathLength(
        int sourceLaneIndex,
        int destinationLaneIndex,
        Vector3 startWorldPosition,
        float pathLength)
    {
        float resolvedPathLength = ResolveConveyorCornerMotionPathLength(
            sourceLaneIndex,
            destinationLaneIndex,
            startWorldPosition,
            pathLength);
        if (!TryGetPairedConveyorCornerTransition(
                sourceLaneIndex,
                destinationLaneIndex,
                out int pairedSourceLaneIndex,
                out int pairedDestinationLaneIndex))
        {
            return resolvedPathLength;
        }

        if (!HasConveyorItemAtLane(pairedSourceLaneIndex)
            && !HasActiveConveyorCornerMotionForTransition(
                pairedDestinationLaneIndex,
                pairedSourceLaneIndex,
                pairedDestinationLaneIndex))
        {
            return resolvedPathLength;
        }

        Vector3 pairedStartWorldPosition = GetConveyorLaneWorldPosition(pairedSourceLaneIndex);
        float pairedPathLength = GetConveyorCornerMotionPathLength(
            pairedSourceLaneIndex,
            pairedDestinationLaneIndex,
            pairedStartWorldPosition);
        return Mathf.Max(resolvedPathLength, pairedPathLength);
    }

    private bool HasActiveConveyorCornerMotionForTransition(
        int laneIndex,
        int sourceLaneIndex,
        int destinationLaneIndex)
    {
        if (!IsValidConveyorLaneIndex(laneIndex))
        {
            return false;
        }

        PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
        if (portableObject != null
            && conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState cornerMotionState))
        {
            return cornerMotionState.sourceLaneIndex == sourceLaneIndex
                && cornerMotionState.destinationLaneIndex == destinationLaneIndex;
        }

        return laneIndex < conveyorItemMotionStates.Count
            && conveyorItemMotionStates[laneIndex].active
            && conveyorItemMotionStates[laneIndex].useCornerMotion
            && conveyorItemMotionStates[laneIndex].sourceLaneIndex == sourceLaneIndex
            && conveyorItemMotionStates[laneIndex].destinationLaneIndex == destinationLaneIndex;
    }

    private float GetConveyorCornerCarryPathLength(int sourceLaneIndex, int destinationLaneIndex)
    {
        if (!TryGetConveyorCornerCarryArcParameters(sourceLaneIndex, destinationLaneIndex, out _, out _, out float deltaAngleRadians, out float radius))
        {
            return 0f;
        }

        return Mathf.Abs(deltaAngleRadians) * radius;
    }

    private bool TryGetCornerConveyorHandoffWorldPosition(int sourceLaneIndex, int destinationLaneIndex, out Vector3 handoffWorldPosition)
    {
        handoffWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, 1f);
        return TryGetConveyorCornerCarryArcParameters(sourceLaneIndex, destinationLaneIndex, out _, out _, out _, out _);
    }

    private Vector3 EvaluateConveyorCornerPathWorldPosition(int sourceLaneIndex, int destinationLaneIndex, float t)
    {
        if (!TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius))
        {
            return GetDefaultConveyorLaneWorldPosition(destinationLaneIndex);
        }

        float angleRadians = startAngleRadians + (deltaAngleRadians * Mathf.Clamp01(t));
        Vector2 point2D = center + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * radius;
        Vector3 localPosition = new Vector3(point2D.x, GetConveyorLaneHeight(), point2D.y);
        return transform.TransformPoint(localPosition);
    }

    private Vector3 EvaluateConveyorCornerMotionWorldPosition(
        int sourceLaneIndex,
        int destinationLaneIndex,
        Vector3 startWorldPosition,
        float pathLength,
        float progress)
    {
        float arcLength = GetConveyorCornerMotionPathLength(sourceLaneIndex, destinationLaneIndex);
        if (arcLength <= ConveyorContinuousMotionEpsilon)
        {
            return EvaluateConveyorPathSegmentWorldPosition(
                this,
                sourceLaneIndex,
                this,
                destinationLaneIndex,
                true,
                progress);
        }

        float resolvedPathLength = ResolveConveyorCornerMotionPathLength(
            sourceLaneIndex,
            destinationLaneIndex,
            startWorldPosition,
            pathLength);
        Vector3 cornerStartWorldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, 0f);
        float leadInLength = Mathf.Max(0f, resolvedPathLength - arcLength);
        float distance = Mathf.Clamp01(progress) * resolvedPathLength;
        if (leadInLength > ConveyorContinuousMotionEpsilon && distance < leadInLength)
        {
            return Vector3.Lerp(startWorldPosition, cornerStartWorldPosition, distance / leadInLength);
        }

        float arcProgress = Mathf.Clamp01((distance - leadInLength) / arcLength);
        return EvaluateConveyorPathSegmentWorldPosition(
            this,
            sourceLaneIndex,
            this,
            destinationLaneIndex,
            true,
            arcProgress);
    }

    private Vector3 EvaluateConveyorCornerCarryWorldPosition(int sourceLaneIndex, int destinationLaneIndex, float t)
    {
        if (!TryGetConveyorCornerCarryArcParameters(sourceLaneIndex, destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius))
        {
            return GetDefaultConveyorLaneWorldPosition(destinationLaneIndex);
        }

        float angleRadians = startAngleRadians + (deltaAngleRadians * Mathf.Clamp01(t));
        Vector2 point2D = center + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * radius;
        Vector3 localPosition = new Vector3(point2D.x, GetConveyorLaneHeight(), point2D.y);
        return transform.TransformPoint(localPosition);
    }

    private Vector3 GetDefaultConveyorLaneWorldPosition(int laneIndex)
    {
        Vector3 localPosition = GetConveyorLaneLocalOffset(laneIndex);
        localPosition.y = GetConveyorLaneHeight();
        return transform.TransformPoint(localPosition);
    }

    private bool TryGetConveyorCornerCenterlineArcParameters(out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        return TryGetConveyorCornerCenterlineArcParameters(true, out center, out startAngleRadians, out deltaAngleRadians, out radius);
    }

    private bool TryGetConveyorCornerCenterlineArcParameters(bool applyInset, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        center = Vector2.zero;
        startAngleRadians = 0f;
        deltaAngleRadians = 0f;
        radius = 0f;

        if (!IsCornerConveyor()
            || !TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        float centerOffset = GetConveyorCornerCenterOffset(localInputDirection, localOutputDirection);
        radius = centerOffset;
        center = (localInputDirection + localOutputDirection) * centerOffset;

        Vector2 startVector = -localOutputDirection;
        Vector2 endVector = -localInputDirection;
        startAngleRadians = Mathf.Atan2(startVector.y, startVector.x);
        float endAngleRadians = Mathf.Atan2(endVector.y, endVector.x);
        deltaAngleRadians = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, endAngleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        if (applyInset)
        {
            float insetRadians = Mathf.Min(Mathf.Abs(deltaAngleRadians) * 0.45f, ConveyorCornerArcEndInsetDegrees * Mathf.Deg2Rad);
            if (insetRadians > 0.0001f)
            {
                float rotationSign = Mathf.Sign(deltaAngleRadians);
                startAngleRadians += rotationSign * insetRadians;
                deltaAngleRadians -= rotationSign * insetRadians * 2f;
            }
        }

        return true;
    }

    private bool TryGetConveyorCornerCarryArcParameters(int sourceLaneIndex, int destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        return TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, false, out center, out startAngleRadians, out deltaAngleRadians, out radius);
    }

    private bool TryGetConveyorCornerArcParameters(int sourceLaneIndex, int destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        return TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, true, out center, out startAngleRadians, out deltaAngleRadians, out radius);
    }

    private bool TryGetConveyorCornerArcParameters(int sourceLaneIndex, int destinationLaneIndex, bool applyInset, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        center = Vector2.zero;
        startAngleRadians = 0f;
        deltaAngleRadians = 0f;
        radius = 0f;

        if (!IsCornerConveyor()
            || sourceLaneIndex < 0
            || destinationLaneIndex < 0
            || sourceLaneIndex >= ConveyorStackLaneLimit
            || destinationLaneIndex >= ConveyorStackLaneLimit)
        {
            return false;
        }

        if (!TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        if (sourceLaneIndex == ConveyorSingleLineBackLaneIndex
            && destinationLaneIndex == ConveyorSingleLineFrontLaneIndex)
        {
            return TryGetConveyorCornerCenterlineArcParameters(
                applyInset,
                out center,
                out startAngleRadians,
                out deltaAngleRadians,
                out radius);
        }

        if (!TryIsOuterCornerTransition(sourceLaneIndex, destinationLaneIndex, localInputDirection, localOutputDirection, out bool isOuterLane))
        {
            return false;
        }

        float centerOffset = GetConveyorCornerCenterOffset(localInputDirection, localOutputDirection);
        radius = GetConveyorCornerLaneRadius(isOuterLane, centerOffset);
        center = (localInputDirection + localOutputDirection) * centerOffset;

        Vector2 startVector = -localOutputDirection;
        Vector2 endVector = -localInputDirection;
        startAngleRadians = Mathf.Atan2(startVector.y, startVector.x);
        float endAngleRadians = Mathf.Atan2(endVector.y, endVector.x);
        deltaAngleRadians = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, endAngleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        if (applyInset)
        {
            float insetRadians = Mathf.Min(Mathf.Abs(deltaAngleRadians) * 0.45f, ConveyorCornerArcEndInsetDegrees * Mathf.Deg2Rad);
            if (insetRadians > 0.0001f)
            {
                float rotationSign = Mathf.Sign(deltaAngleRadians);
                startAngleRadians += rotationSign * insetRadians;
                deltaAngleRadians -= rotationSign * insetRadians * 2f;
            }
        }

        return true;
    }

    private bool TryIsOuterCornerTransition(int sourceLaneIndex, int destinationLaneIndex, Vector2 localInputDirection, Vector2 localOutputDirection, out bool isOuterLane)
    {
        isOuterLane = false;
        bool isCounterClockwiseTurn = IsCounterClockwiseTurn(localInputDirection, localOutputDirection);

        if (isCounterClockwiseTurn)
        {
            if (sourceLaneIndex == 3 && destinationLaneIndex == 0)
            {
                isOuterLane = true;
                return true;
            }

            if (sourceLaneIndex == 2 && destinationLaneIndex == 1)
            {
                isOuterLane = false;
                return true;
            }
        }
        else
        {
            if (sourceLaneIndex == 2 && destinationLaneIndex == 0)
            {
                isOuterLane = true;
                return true;
            }

            if (sourceLaneIndex == 3 && destinationLaneIndex == 1)
            {
                isOuterLane = false;
                return true;
            }
        }

        return false;
    }

    private static bool IsCounterClockwiseTurn(Vector2 localInputDirection, Vector2 localOutputDirection)
    {
        float cross = (localInputDirection.x * localOutputDirection.y) - (localInputDirection.y * localOutputDirection.x);
        return cross > 0f;
    }

    private float GetConveyorCornerCenterOffset(Vector2 localInputDirection, Vector2 localOutputDirection)
    {
        float inputOffset = GetConveyorEdgeOffsetForDirection(localInputDirection);
        float outputOffset = GetConveyorEdgeOffsetForDirection(localOutputDirection);
        float resolvedOffset = (inputOffset + outputOffset) * 0.5f;
        float minimumOffset = GetConveyorLaneHalfExtent() + 0.05f;
        return Mathf.Max(minimumOffset, resolvedOffset);
    }

    private float GetConveyorEdgeOffsetForDirection(Vector2 localDirection)
    {
        if (localDirection.sqrMagnitude <= 0.5f || floorObjects == null || floorObjects.Count == 0)
        {
            return ConveyorCornerCenterRadius;
        }

        Vector2 direction = localDirection.normalized;
        float bestProjection = 0f;
        for (int i = 0; i < floorObjects.Count; i++)
        {
            Transform laneAnchor = floorObjects[i];
            if (laneAnchor == null)
            {
                continue;
            }

            Vector3 localPosition3 = transform.InverseTransformPoint(laneAnchor.position);
            Vector2 localPosition = new Vector2(localPosition3.x, localPosition3.z);
            float projection = Vector2.Dot(localPosition, direction);
            if (projection > bestProjection)
            {
                bestProjection = projection;
            }
        }

        return bestProjection > 0.0001f ? bestProjection : ConveyorCornerCenterRadius;
    }

    private float GetConveyorCornerLaneRadius(bool isOuterLane, float centerOffset)
    {
        float halfExtent = GetConveyorLaneHalfExtent();
        float radius = centerOffset + (isOuterLane ? halfExtent : -halfExtent);
        return Mathf.Max(0.05f, radius);
    }

    private bool TryGetBestCornerConveyorReceiveLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        bestLaneIndex = ConveyorSingleLineBackLaneIndex;
        if (!IsValidConveyorLaneIndex(bestLaneIndex))
        {
            bestLaneIndex = -1;
            return false;
        }

        if (HasConveyorItemAtLane(bestLaneIndex))
        {
            bestLaneIndex = -1;
            return false;
        }

        return true;
    }

    private bool TryGetPreferredCornerConveyorReceiveLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        bestLaneIndex = ConveyorSingleLineBackLaneIndex;
        return IsValidConveyorLaneIndex(bestLaneIndex);
    }

    private float GetConveyorLaneHalfExtent()
    {
        return ConveyorLaneHalfExtent;
    }

    private float GetConveyorLaneHeight()
    {
        return ConveyorLaneHeight;
    }

    public bool TryAddInputAreaCenterObject(int objectId, out PortableObject targetPortableObject, bool enforceIoOverlapFilter = false)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (objectId < 0
            || InputOutputModule.IsFluidItemId(objectId)
            || !ResolveFloorObjectPool())
        {
            return false;
        }

        if (enforceIoOverlapFilter && !InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(coordinate, objectId))
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null
            || inputAreaCenterStack.Count >= ResolveInputAreaCenterCapacity()
            || !IsStackCompatible(inputAreaCenterStack, objectId))
        {
            return false;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetItem(objectId);
        int objectIndex = inputAreaCenterStack.Count;
        inputAreaCenterStack.Add(portableObject);
        NotifyRuntimeItemStackChanged();
        ApplyInputAreaCenterObjectVisibility(portableObject, objectIndex);
        portableObject.GetOrAddPickupGate()?.MarkSettled();
        targetPortableObject = portableObject;
        return true;
    }

    public bool TryPickupOneInputAreaCenterObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex, int preferredItemId = -1)
    {
        if (player == null || pickupRadius <= 0f || inputAreaCenterStack.Count == 0 || IsClosedBoxContentPickupBlocked())
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        UpdatePickupGates(inputAreaCenterStack, gateOriginPosition);

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        Vector3 offset = inputAreaCenterAnchor.position - playerPosition;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (distanceSqr > pickupRadiusSqr)
        {
            return false;
        }

        DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
        if (topGate != null && !topGate.CanManualPickup(distanceSqr, pickupRadiusSqr))
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            NotifyRuntimeItemStackChanged();
            return false;
        }

        if (preferredItemId >= 0 && itemId != preferredItemId)
        {
            return false;
        }

        if (!TryAddPickupObjectToBagOrMatchingHand(player, itemId, preferredSlotIndex, out PortableObject storageTarget, out bool addedToHand))
        {
            return false;
        }

        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
        ReleasePickupObjectToStorage(topObject, storageTarget, addedToHand);
        NotifyRuntimeItemStackChanged();
        return true;
    }

    public bool TryPreviewPickupOneInputAreaCenterObject(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId)
    {
        return TryPreviewPickupInputAreaCenterObjects(
            player,
            playerPosition,
            pickupRadius,
            preferredItemId,
            out previewItemId,
            out _);
    }

    public bool TryPreviewPickupInputAreaCenterObjects(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (player == null || pickupRadius <= 0f || inputAreaCenterStack.Count == 0 || IsClosedBoxContentPickupBlocked())
        {
            return false;
        }

        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count == 0)
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        UpdatePickupGates(inputAreaCenterStack, gateOriginPosition);

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        Vector3 offset = inputAreaCenterAnchor.position - playerPosition;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (distanceSqr > pickupRadiusSqr)
        {
            return false;
        }

        DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
        if (topGate != null && !topGate.CanManualPickup(distanceSqr, pickupRadiusSqr))
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            return false;
        }

        if (preferredItemId >= 0 && itemId != preferredItemId)
        {
            return false;
        }

        previewItemId = itemId;
        previewPickupCount = CountManualPickupStackObjectsFromTop(inputAreaCenterStack, itemId, distanceSqr, pickupRadiusSqr);
        return previewPickupCount > 0;
    }

    public bool TryPickupOneInputAreaCenterObjectToHand(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (player == null || pickupRadius <= 0f || inputAreaCenterStack.Count == 0 || IsClosedBoxContentPickupBlocked())
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        UpdatePickupGates(inputAreaCenterStack, gateOriginPosition);

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        Vector3 offset = inputAreaCenterAnchor.position - playerPosition;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (distanceSqr > pickupRadiusSqr)
        {
            return false;
        }

        DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
        if (topGate != null && !topGate.CanManualPickup(distanceSqr, pickupRadiusSqr))
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            NotifyRuntimeItemStackChanged();
            return false;
        }

        if (!player.TryAddToHand(itemId, out PortableObject handTarget))
        {
            return false;
        }

        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
        ReleaseFloorObjectToHand(topObject, handTarget);
        NotifyRuntimeItemStackChanged();
        return true;
    }

    private bool IsClosedBoxContentPickupBlocked()
    {
        return mapObject is BoxObject boxObject && !boxObject.IsOpen;
    }

    private void EnsureInputAreaCenterAnchorInitialized()
    {
        CacheChildReferences();
        if (inputAreaCenterAnchor != null)
        {
            return;
        }

        Transform existingAnchor = transform.Find("InputAreaCenterAnchor");
        if (existingAnchor != null)
        {
            inputAreaCenterAnchor = existingAnchor;
        }
        else
        {
            GameObject anchorObject = new GameObject("InputAreaCenterAnchor");
            inputAreaCenterAnchor = anchorObject.transform;
            inputAreaCenterAnchor.SetParent(transform, false);
        }

        Vector3 localPosition = inputAreaCenterAnchor.localPosition;
        localPosition.x = 0f;
        localPosition.z = 0f;
        localPosition.y = ResolveInputAreaCenterHeight();
        inputAreaCenterAnchor.localPosition = localPosition;
        inputAreaCenterAnchor.localRotation = Quaternion.identity;
        inputAreaCenterAnchor.localScale = Vector3.one;
    }

    private float ResolveInputAreaCenterHeight()
    {
        CacheChildReferences();
        return cachedInputAreaCenterHeight;
    }

    private int ResolveInputAreaCenterCapacity()
    {
        if (TryGetInstalledItemAreaCapacity(out int capacity))
        {
            return capacity;
        }

        return Mathf.Max(1, inputAreaCenterMaxObjects);
    }

    private ItemDefinition ResolveInstalledItemAreaDefinition()
    {
        if (mapObject == null)
        {
            return null;
        }

        int itemId = mapObject.ResolveItemId();
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private static PortableObject GetTopPortableObject(List<PortableObject> stack)
    {
        CleanupPortableStack(stack);
        return stack != null && stack.Count > 0 ? stack[stack.Count - 1] : null;
    }

    private static int CountManualPickupStackObjectsFromTop(List<PortableObject> stack, int itemId, float distanceSqr, float pickupRadiusSqr)
    {
        if (stack == null || itemId < 0)
        {
            return 0;
        }

        int count = 0;
        for (int objectIndex = stack.Count - 1; objectIndex >= 0; objectIndex--)
        {
            PortableObject portableObject = stack[objectIndex];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.ItemId != itemId)
            {
                break;
            }

            DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
            if (gate != null && !gate.CanManualPickup(distanceSqr, pickupRadiusSqr))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static void CleanupPortableStack(List<PortableObject> stack)
    {
        if (stack == null)
        {
            return;
        }

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i] == null)
            {
                stack.RemoveAt(i);
            }
        }
    }

    private static void UpdatePickupGates(List<PortableObject> stack, Vector3 gateOriginPosition)
    {
        if (stack == null)
        {
            return;
        }

        for (int i = 0; i < stack.Count; i++)
        {
            PortableObject portableObject = stack[i];
            if (portableObject == null)
            {
                continue;
            }

            DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
            if (gate != null)
            {
                gate.UpdateExitState(gateOriginPosition);
            }
        }
    }

    private void CacheChildReferences()
    {
        if (childReferencesCached)
        {
            return;
        }

        if (body == null)
        {
            Transform bodyTransform = transform.Find("Body");
            if (bodyTransform != null)
            {
                body = bodyTransform;
            }
        }

        cachedBodyRenderers = body != null
            ? body.GetComponentsInChildren<MeshRenderer>(true)
            : Array.Empty<MeshRenderer>();

        if (inputAreaCenterAnchor == null)
        {
            Transform existingAnchor = transform.Find("InputAreaCenterAnchor");
            if (existingAnchor != null)
            {
                inputAreaCenterAnchor = existingAnchor;
            }
        }

        cachedInputAreaCenterHeight = 0f;
        if (floorObjects != null)
        {
            for (int i = 0; i < floorObjects.Count; i++)
            {
                Transform anchor = floorObjects[i];
                if (anchor == null)
                {
                    continue;
                }

                cachedInputAreaCenterHeight = transform.InverseTransformPoint(anchor.position).y;
                break;
            }
        }

        childReferencesCached = true;
    }
}
