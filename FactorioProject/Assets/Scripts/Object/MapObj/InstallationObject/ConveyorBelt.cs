using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ConveyorBelt : InstallationObject
{
    public delegate bool EndpointConveyorLookup(
        Vector2Int coordinate,
        ConveyorBelt ignoredBelt,
        out ConveyorBelt conveyorBelt,
        out Quaternion rotation);

    private const float UvLengthReferenceAspect = 1.4285714f;
    private const string EndStartObjectName = "End_S";
    private const string EndEndObjectName = "End_E";
    private const string SeamStartObjectName = "Seam_S";
    private const string SeamEndObjectName = "Seam_E";
    protected static readonly int[] ObjectInfoMainLaneIndices = { 0, 2 };
    protected static readonly int[] ObjectInfoBridgeLaneIndices = { 1, 3 };
    private static readonly Vector2Int[] EndpointRefreshDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private static readonly int UvScrollXShaderId = Shader.PropertyToID("_UVScrollX");
    private static readonly int UvScrollYShaderId = Shader.PropertyToID("_UVScrollY");
    private static readonly int UvLengthScaleShaderId = Shader.PropertyToID("_UvLengthScale");
    private static readonly int UvLengthOffsetShaderId = Shader.PropertyToID("_UvLengthOffset");

    [SerializeField]
    private ConveyorBelt straightVariantPrefab;
    [SerializeField]
    private ConveyorBelt cornerVariantPrefab;
    [SerializeField]
    private ConveyorBelt reverseCornerVariantPrefab;
    [SerializeField]
    private bool isCornerVariant;
    [SerializeField]
    private bool isReverseCornerVariant;
    [SerializeField]
    private InstallationFacingDirection localOutputDirection = InstallationFacingDirection.NegativeZ;
    [SerializeField]
    private InstallationFacingDirection localInputDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField, Min(0f)]
    private float conveyorSpeed = 1f;
    [SerializeField]
    private MeshRenderer beltTopRenderer;
    [SerializeField]
    private GameObject endStartObject;
    [SerializeField]
    private GameObject endEndObject;
    [SerializeField]
    private GameObject seamStartObject;
    [SerializeField]
    private GameObject seamEndObject;

    private MaterialPropertyBlock beltTopPropertyBlock;
    private MeshRenderer[] cachedRenderers;
    private MeshFilter[] cachedRendererMeshFilters;
    private readonly List<Material> sharedMaterialBuffer = new List<Material>(4);
    private readonly List<BeltTopRenderInfo> beltTopRenderInfos = new List<BeltTopRenderInfo>(8);
    private readonly List<BeltTopTransformState> beltTopTransformStates = new List<BeltTopTransformState>(8);
    private EndpointRuntimeState lastEndpointRuntimeState;
    private bool beltTopTransformStateCached;
    private bool virtualRenderingSuppressed;
    private bool virtualRenderingSuppressBeltTopOnly;

    private struct BeltTopRenderInfo
    {
        public MeshRenderer Renderer;
        public float CenterZ;
        public float UvLengthScale;
        public float UvLengthOffset;
    }

    private struct BeltTopTransformState
    {
        public Transform Transform;
        public MeshRenderer Renderer;
        public Vector3 BaseLocalPosition;
        public Quaternion BaseLocalRotation;
        public Vector3 BaseLocalScale;
        public Vector3 BaseRootPosition;
        public float BaseLocalLengthX;
        public float BaseLocalLengthZ;
    }

    private struct EndpointRuntimeState
    {
        public bool HasRuntime;
        public Vector2Int AnchorCoordinate;
        public Vector2Int StartCoordinate;
        public Vector2Int EndCoordinate;
        public Vector2Int StartDirection;
        public Vector2Int EndDirection;
        public Vector2Int[] OccupiedCoordinates;
    }

    public float ConveyorSpeed => Mathf.Max(0f, conveyorSpeed);
    public ConveyorBelt StraightVariantPrefab => straightVariantPrefab != null ? straightVariantPrefab : this;
    public ConveyorBelt CornerVariantPrefab => cornerVariantPrefab != null ? cornerVariantPrefab : this;
    public ConveyorBelt ReverseCornerVariantPrefab => reverseCornerVariantPrefab != null ? reverseCornerVariantPrefab : CornerVariantPrefab;
    public bool IsCornerVariant => isCornerVariant || isReverseCornerVariant;
    public bool IsReverseCornerVariant => isReverseCornerVariant;
    public int PlacementRotationQuarterTurnOffset => IsCornerVariant ? 3 : 0;

    public static bool TryGetFlowDirection(Quaternion rotation, out Vector2Int flowDirection)
    {
        return TryResolveCardinalDirection(rotation * Vector3.forward, out flowDirection);
    }

    public static Vector2Int RotateDirectionClockwise(Vector2Int direction)
    {
        return new Vector2Int(direction.y, -direction.x);
    }

    public static Vector2Int RotateDirectionCounterClockwise(Vector2Int direction)
    {
        return new Vector2Int(-direction.y, direction.x);
    }

    public bool TryGetInputDirection(Quaternion rotation, out Vector2Int inputDirection)
    {
        return TryResolveDirection(rotation, localInputDirection, out inputDirection);
    }

    public bool TryGetOutputDirection(Quaternion rotation, out Vector2Int outputDirection)
    {
        return TryResolveDirection(rotation, localOutputDirection, out outputDirection);
    }

    public ConveyorBelt GetPlacementPreviewVariantPrefab(bool useCornerVariant)
    {
        if (useCornerVariant)
        {
            return CornerVariantPrefab != null ? CornerVariantPrefab : this;
        }

        return StraightVariantPrefab != null ? StraightVariantPrefab : this;
    }

    public virtual void CopyObjectInfoItemIds(List<int> results, int maxCount)
    {
        if (results == null || maxCount <= 0)
        {
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        IReadOnlyList<Vector2Int> coordinates = RuntimeOccupiedCoordinates;
        if (terrain == null || coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        for (int coordinateIndex = 0; coordinateIndex < coordinates.Count && results.Count < maxCount; coordinateIndex++)
        {
            if (!terrain.TryGetLoadedBlock(coordinates[coordinateIndex], out Block block) || block == null)
            {
                continue;
            }

            AppendObjectInfoLaneItemIds(results, maxCount, block, ObjectInfoMainLaneIndices);
        }
    }

    protected static void AppendObjectInfoLaneItemIds(
        List<int> results,
        int maxCount,
        Block block,
        IReadOnlyList<int> laneIndices)
    {
        if (results == null || laneIndices == null || maxCount <= 0)
        {
            return;
        }

        for (int i = 0; i < laneIndices.Count && results.Count < maxCount; i++)
        {
            int itemId = -1;
            block?.TryGetRuntimeConveyorItemSlotIdAtLane(laneIndices[i], out itemId);
            results.Add(itemId);
        }
    }

    public void HandlePlacementRotation(ref int quarterTurns, ref bool useCornerVariant, bool canUseCornerVariantAfterTurn)
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;

        if (useCornerVariant)
        {
            useCornerVariant = false;
            return;
        }

        quarterTurns = (quarterTurns + 1) % 4;
        useCornerVariant = canUseCornerVariantAfterTurn;
    }

    public static bool IsPerpendicular(Vector2Int left, Vector2Int right)
    {
        return left != Vector2Int.zero
               && right != Vector2Int.zero
               && ((left.x * right.x) + (left.y * right.y)) == 0;
    }

    private bool TryResolveDirection(Quaternion rotation, InstallationFacingDirection localDirection, out Vector2Int resolvedDirection)
    {
        return TryResolveCardinalDirection(rotation * FacingDirectionToVector(localDirection), out resolvedDirection);
    }

    private static bool TryResolveCardinalDirection(Vector3 directionVector, out Vector2Int resolvedDirection)
    {
        resolvedDirection = Vector2Int.zero;

        directionVector.y = 0f;
        if (directionVector.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        directionVector.Normalize();
        if (Mathf.Abs(directionVector.x) >= Mathf.Abs(directionVector.z))
        {
            resolvedDirection = new Vector2Int(directionVector.x >= 0f ? 1 : -1, 0);
        }
        else
        {
            resolvedDirection = new Vector2Int(0, directionVector.z >= 0f ? 1 : -1);
        }

        return true;
    }

    private static Vector3 FacingDirectionToVector(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return Vector3.right;
            case InstallationFacingDirection.NegativeX:
                return Vector3.left;
            case InstallationFacingDirection.NegativeZ:
                return Vector3.back;
            default:
                return Vector3.forward;
        }
    }

    protected override InstallationFacingDirection ResolveInstalledDirection(Quaternion rotation)
    {
        if (TryGetInputDirection(rotation, out Vector2Int inputDirection))
        {
            return ToFacingDirection(inputDirection);
        }

        if (TryGetOutputDirection(rotation, out Vector2Int outputDirection))
        {
            return ToFacingDirection(outputDirection);
        }

        return base.ResolveInstalledDirection(rotation);
    }

    private static InstallationFacingDirection ToFacingDirection(Vector2Int direction)
    {
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            return direction.x >= 0
                ? InstallationFacingDirection.PositiveX
                : InstallationFacingDirection.NegativeX;
        }

        return direction.y >= 0
            ? InstallationFacingDirection.PositiveZ
            : InstallationFacingDirection.NegativeZ;
    }

    protected new void Awake()
    {
        base.Awake();
        EnsureEndpointVisualObjects();
        RefreshBeltTopRenderInfo();
        CacheBeltTopTransformState();
        ConfigureRuntimeRenderers();
        ApplyBeltTopShaderProperties();
        if (Application.isPlaying && TryGetPlacementRuntime(out _, out _))
        {
            RefreshEndpointVisuals();
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EnsureEndpointVisualObjects();
        RefreshBeltTopRenderInfo();
        CacheBeltTopTransformState();
        ConfigureRuntimeRenderers();
        ApplyBeltTopShaderProperties();
        if (Application.isPlaying && TryGetPlacementRuntime(out _, out _))
        {
            RefreshEndpointVisualsAndNeighbors();
        }
    }

    protected override void OnDisable()
    {
        if (Application.isPlaying)
        {
            EndpointRuntimeState previousState = lastEndpointRuntimeState;
            SetEndpointVisualsActive(false, false, false, false);
            ApplyBeltTopEndpointExtension(false, false);
            lastEndpointRuntimeState = default;
            RefreshEndpointNeighbors(previousState, this);
        }

        TerrainGenerator.Active?.UnregisterVirtualConveyorBelt(this, false);
        virtualRenderingSuppressed = false;
        virtualRenderingSuppressBeltTopOnly = false;
        base.OnDisable();
    }

    public void SetVirtualRuntimeRenderingEnabled(bool isEnabled)
    {
        if (!Application.isPlaying)
        {
            SetVirtualRenderingSuppressed(false);
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (isEnabled && terrain != null && terrain.VirtualizeConveyorBelts)
        {
            terrain.RegisterVirtualConveyorBelt(this);
        }
        else
        {
            if (terrain != null)
            {
                terrain.UnregisterVirtualConveyorBelt(this);
            }
            else
            {
                SetVirtualRenderingSuppressed(false);
            }
        }
    }

    public void AppendVirtualRenderData(List<VirtualConveyorBeltRenderData> results, bool beltTopOnly = false)
    {
        if (results == null)
        {
            return;
        }

        RefreshBeltTopRenderInfo();

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedRenderers[i];
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            bool hasUvScroll = TryGetBeltTopRenderInfo(renderer, out BeltTopRenderInfo beltTopInfo);
            if (beltTopOnly && !hasUvScroll)
            {
                continue;
            }

            MeshFilter meshFilter = i < cachedRendererMeshFilters.Length ? cachedRendererMeshFilters[i] : null;
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
            {
                continue;
            }

            sharedMaterialBuffer.Clear();
            renderer.GetSharedMaterials(sharedMaterialBuffer);
            int materialCount = sharedMaterialBuffer.Count;
            if (materialCount <= 0)
            {
                continue;
            }

            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            int entryCount = Mathf.Max(materialCount, subMeshCount);
            float uvScrollY = hasUvScroll ? -ConveyorSpeed * 0.75f : 0f;
            float uvLengthScale = 1f;
            float uvLengthOffset = 0f;
            if (hasUvScroll)
            {
                uvLengthScale = beltTopInfo.UvLengthScale;
                uvLengthOffset = beltTopInfo.UvLengthOffset;
            }

            Matrix4x4 matrix = renderer.localToWorldMatrix;
            int layer = renderer.gameObject.layer;

            for (int passIndex = 0; passIndex < entryCount; passIndex++)
            {
                int materialIndex = Mathf.Min(passIndex, materialCount - 1);
                Material material = sharedMaterialBuffer[materialIndex];
                if (material == null)
                {
                    continue;
                }

                int subMeshIndex = Mathf.Min(passIndex, subMeshCount - 1);

                results.Add(new VirtualConveyorBeltRenderData(
                    mesh,
                    material,
                    matrix,
                    layer,
                    subMeshIndex,
                    hasUvScroll,
                    uvScrollY,
                    uvLengthScale,
                    uvLengthOffset));
            }
        }
    }

    public void SetVirtualRenderingSuppressed(bool isSuppressed, bool beltTopOnly = false)
    {
        virtualRenderingSuppressed = isSuppressed;
        virtualRenderingSuppressBeltTopOnly = isSuppressed && beltTopOnly;
        ApplyVirtualRenderingSuppression();
    }

    public void RefreshBeltTopShaderProperties()
    {
        RefreshBeltTopRenderInfo();
        ApplyBeltTopShaderProperties();
    }

    public void RefreshEndpointVisualsAndNeighbors()
    {
        EndpointRuntimeState previousState = lastEndpointRuntimeState;
        RefreshEndpointVisuals();
        RefreshEndpointNeighbors(previousState, this);
        RefreshEndpointNeighbors(lastEndpointRuntimeState, this);
    }

    public void RefreshEndpointVisuals()
    {
        EnsureEndpointVisualObjects();

        if (!SupportsEndpointVisuals(this)
            || !TryCaptureEndpointRuntimeState(out EndpointRuntimeState state))
        {
            bool hiddenChanged = SetEndpointVisualsActive(false, false, false, false);
            hiddenChanged |= ApplyBeltTopEndpointExtension(false, false);
            lastEndpointRuntimeState = default;
            if (hiddenChanged)
            {
                RefreshVirtualRenderingAfterEndpointVisualChange();
            }

            return;
        }

        bool startSeam = HasStraightCrossingBeltAtEndpoint(state.StartCoordinate, state.StartDirection);
        bool endSeam = HasStraightCrossingBeltAtEndpoint(state.EndCoordinate, state.EndDirection);
        bool startEnd = !startSeam
                        && !HasConnectedBeltAtEndpoint(true, state.StartCoordinate, state.StartDirection);
        bool endEnd = !endSeam
                      && !HasConnectedBeltAtEndpoint(false, state.EndCoordinate, state.EndDirection);
        bool changed = SetEndpointVisualsActive(startEnd, endEnd, startSeam, endSeam);
        changed |= ApplyBeltTopEndpointExtension(startSeam, endSeam);
        lastEndpointRuntimeState = state;

        if (changed)
        {
            RefreshVirtualRenderingAfterEndpointVisualChange();
        }
    }

    public void RefreshEndpointVisualsForPreview(
        Vector2Int anchorCoordinate,
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        EndpointConveyorLookup lookup)
    {
        EnsureEndpointVisualObjects();

        if (!SupportsEndpointVisuals(this)
            || lookup == null
            || !TryCaptureEndpointPlacementState(anchorCoordinate, occupiedCoordinates, out EndpointRuntimeState state))
        {
            ClearEndpointVisualsForPreview();
            return;
        }

        bool startSeam = HasStraightCrossingBeltAtEndpoint(
            state.StartCoordinate,
            state.StartDirection,
            lookup);
        bool endSeam = HasStraightCrossingBeltAtEndpoint(
            state.EndCoordinate,
            state.EndDirection,
            lookup);
        bool startEnd = !startSeam
                        && !HasConnectedBeltAtEndpoint(
                            true,
                            state.StartCoordinate,
                            state.StartDirection,
                            lookup);
        bool endEnd = !endSeam
                      && !HasConnectedBeltAtEndpoint(
                          false,
                          state.EndCoordinate,
                          state.EndDirection,
                          lookup);

        SetEndpointVisualsActive(startEnd, endEnd, startSeam, endSeam);
        ApplyBeltTopEndpointExtension(startSeam, endSeam);
    }

    public void ClearEndpointVisualsForPreview()
    {
        EnsureEndpointVisualObjects();
        SetEndpointVisualsActive(false, false, false, false);
        ApplyBeltTopEndpointExtension(false, false);
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        RefreshEndpointVisualsAndNeighbors();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        EndpointRuntimeState previousState = lastEndpointRuntimeState;
        SetEndpointVisualsActive(false, false, false, false);
        ApplyBeltTopEndpointExtension(false, false);
        lastEndpointRuntimeState = default;
        RefreshEndpointNeighbors(previousState, this);
        base.OnPlacementRuntimeCleared();
    }

    private bool TryCaptureEndpointRuntimeState(out EndpointRuntimeState state)
    {
        state = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
            || !TryCaptureEndpointPlacementState(
                anchorCoordinate,
                CopyRuntimeOccupiedCoordinates(),
                out state))
        {
            return false;
        }

        return true;
    }

    private bool TryCaptureEndpointPlacementState(
        Vector2Int anchorCoordinate,
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        out EndpointRuntimeState state)
    {
        state = default;
        if (!TryGetInputDirection(transform.rotation, out Vector2Int inputDirection)
            || !TryGetOutputDirection(transform.rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        Vector2Int[] occupiedCoordinateArray = CopyOccupiedCoordinates(anchorCoordinate, occupiedCoordinates);
        Vector2Int startEdgeCoordinate = anchorCoordinate;
        Vector2Int endEdgeCoordinate = anchorCoordinate;
        int bestStartScore = int.MinValue;
        int bestEndScore = int.MinValue;
        for (int i = 0; i < occupiedCoordinateArray.Length; i++)
        {
            Vector2Int occupiedCoordinate = occupiedCoordinateArray[i];
            Vector2Int offset = occupiedCoordinate - anchorCoordinate;
            int startScore = Dot(offset, inputDirection);
            if (startScore > bestStartScore)
            {
                bestStartScore = startScore;
                startEdgeCoordinate = occupiedCoordinate;
            }

            int endScore = Dot(offset, outputDirection);
            if (endScore > bestEndScore)
            {
                bestEndScore = endScore;
                endEdgeCoordinate = occupiedCoordinate;
            }
        }

        state = new EndpointRuntimeState
        {
            HasRuntime = true,
            AnchorCoordinate = anchorCoordinate,
            StartCoordinate = startEdgeCoordinate + inputDirection,
            EndCoordinate = endEdgeCoordinate + outputDirection,
            StartDirection = inputDirection,
            EndDirection = outputDirection,
            OccupiedCoordinates = occupiedCoordinateArray
        };
        return true;
    }

    private Vector2Int[] CopyRuntimeOccupiedCoordinates()
    {
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count == 0)
        {
            return TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
                ? new[] { anchorCoordinate }
                : new Vector2Int[0];
        }

        Vector2Int[] result = new Vector2Int[occupiedCoordinates.Count];
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            result[i] = occupiedCoordinates[i];
        }

        return result;
    }

    private static Vector2Int[] CopyOccupiedCoordinates(
        Vector2Int anchorCoordinate,
        IReadOnlyList<Vector2Int> occupiedCoordinates)
    {
        if (occupiedCoordinates == null || occupiedCoordinates.Count == 0)
        {
            return new[] { anchorCoordinate };
        }

        Vector2Int[] result = new Vector2Int[occupiedCoordinates.Count];
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            result[i] = occupiedCoordinates[i];
        }

        return result;
    }

    private static int Dot(Vector2Int left, Vector2Int right)
    {
        return left.x * right.x + left.y * right.y;
    }

    private static bool SupportsEndpointVisuals(ConveyorBelt belt)
    {
        return belt != null;
    }

    private static bool IsStraightEndpointVisualBelt(ConveyorBelt belt)
    {
        return SupportsEndpointVisuals(belt)
               && !belt.IsCornerVariant;
    }

    private bool HasConnectedBeltAtEndpoint(bool isStartEndpoint, Vector2Int endpointCoordinate, Vector2Int endpointDirection)
    {
        if (!TryGetConveyorBlockAtCoordinate(endpointCoordinate, out Block endpointBlock, out ConveyorBelt endpointBelt))
        {
            return false;
        }

        if (TryGetCurrentRuntimeBlock(out Block currentBlock))
        {
            if (isStartEndpoint
                && currentBlock.TryGetRuntimePreviousConveyorBlock(out Block previousBlock)
                && previousBlock == endpointBlock)
            {
                return true;
            }

            if (!isStartEndpoint
                && currentBlock.TryGetRuntimeNextConveyorBlock(out Block nextBlock)
                && nextBlock == endpointBlock)
            {
                return true;
            }
        }

        if (isStartEndpoint)
        {
            return endpointBelt.TryGetOutputDirection(endpointBelt.transform.rotation, out Vector2Int neighborOutputDirection)
                   && neighborOutputDirection == -endpointDirection;
        }

        return endpointBelt.TryGetInputDirection(endpointBelt.transform.rotation, out Vector2Int neighborInputDirection)
               && neighborInputDirection == -endpointDirection;
    }

    private bool HasStraightCrossingBeltAtEndpoint(Vector2Int endpointCoordinate, Vector2Int flowDirection)
    {
        if (!TryGetConveyorBlockAtCoordinate(endpointCoordinate, out _, out ConveyorBelt endpointBelt)
            || !IsStraightEndpointVisualBelt(endpointBelt)
            || !endpointBelt.TryGetOutputDirection(endpointBelt.transform.rotation, out Vector2Int neighborFlowDirection))
        {
            return false;
        }

        return IsPerpendicular(flowDirection, neighborFlowDirection);
    }

    private bool HasConnectedBeltAtEndpoint(
        bool isStartEndpoint,
        Vector2Int endpointCoordinate,
        Vector2Int endpointDirection,
        EndpointConveyorLookup lookup)
    {
        if (lookup == null
            || !lookup(endpointCoordinate, this, out ConveyorBelt endpointBelt, out Quaternion endpointRotation)
            || endpointBelt == null)
        {
            return false;
        }

        if (isStartEndpoint)
        {
            return endpointBelt.TryGetOutputDirection(endpointRotation, out Vector2Int neighborOutputDirection)
                   && neighborOutputDirection == -endpointDirection;
        }

        return endpointBelt.TryGetInputDirection(endpointRotation, out Vector2Int neighborInputDirection)
               && neighborInputDirection == -endpointDirection;
    }

    private bool HasStraightCrossingBeltAtEndpoint(
        Vector2Int endpointCoordinate,
        Vector2Int flowDirection,
        EndpointConveyorLookup lookup)
    {
        if (lookup == null
            || !lookup(endpointCoordinate, this, out ConveyorBelt endpointBelt, out Quaternion endpointRotation)
            || !IsStraightEndpointVisualBelt(endpointBelt)
            || !endpointBelt.TryGetOutputDirection(endpointRotation, out Vector2Int neighborFlowDirection))
        {
            return false;
        }

        return IsPerpendicular(flowDirection, neighborFlowDirection);
    }

    private bool TryGetCurrentRuntimeBlock(out Block currentBlock)
    {
        currentBlock = null;
        TerrainGenerator terrain = TerrainGenerator.Active;
        return terrain != null
               && TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
               && terrain.TryGetLoadedBlock(anchorCoordinate, out currentBlock)
               && currentBlock != null
               && ReferenceEquals(currentBlock.MapObject, this);
    }

    private static bool TryGetConveyorBlockAtCoordinate(
        Vector2Int coordinate,
        out Block block,
        out ConveyorBelt conveyorBelt)
    {
        block = null;
        conveyorBelt = null;

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out block)
            || block == null)
        {
            return false;
        }

        if (block.MapObject is ConveyorBelt mappedBelt
            && mappedBelt != null
            && mappedBelt.gameObject.activeInHierarchy)
        {
            conveyorBelt = mappedBelt;
            return true;
        }

        if (ConvayorBelt2F.TryFindCoveringBelt(coordinate, out ConvayorBelt2F coveringBelt)
            && coveringBelt != null
            && coveringBelt.gameObject.activeInHierarchy)
        {
            conveyorBelt = coveringBelt;
            return true;
        }

        return false;
    }

    private void EnsureEndpointVisualObjects()
    {
        if (endStartObject == null)
        {
            endStartObject = FindEndpointVisualObject(EndStartObjectName);
        }

        if (endEndObject == null)
        {
            endEndObject = FindEndpointVisualObject(EndEndObjectName);
        }

        if (seamStartObject == null)
        {
            seamStartObject = FindEndpointVisualObject(SeamStartObjectName);
        }

        if (seamEndObject == null)
        {
            seamEndObject = FindEndpointVisualObject(SeamEndObjectName);
        }

        if (endStartObject == null
            || endEndObject == null
            || seamStartObject == null
            || seamEndObject == null)
        {
            CreateMissingEndpointVisualObjectsFromVariant();
        }
    }

    private GameObject FindEndpointVisualObject(string objectName)
    {
        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && childTransform.name == objectName)
            {
                return childTransform.gameObject;
            }
        }

        return null;
    }

    private void CreateMissingEndpointVisualObjectsFromVariant()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ConveyorBelt sourceVariant = ResolveEndpointVisualSourceVariant();
        if (sourceVariant == null || sourceVariant == this)
        {
            return;
        }

        bool created = false;
        created |= TryCreateEndpointVisualObjectFromVariant(ref endStartObject, EndStartObjectName, sourceVariant);
        created |= TryCreateEndpointVisualObjectFromVariant(ref endEndObject, EndEndObjectName, sourceVariant);
        created |= TryCreateEndpointVisualObjectFromVariant(ref seamStartObject, SeamStartObjectName, sourceVariant);
        created |= TryCreateEndpointVisualObjectFromVariant(ref seamEndObject, SeamEndObjectName, sourceVariant);
        if (!created)
        {
            return;
        }

        cachedRenderers = null;
        cachedRendererMeshFilters = null;
        beltTopTransformStateCached = false;
    }

    private ConveyorBelt ResolveEndpointVisualSourceVariant()
    {
        if (IsReverseCornerVariant
            && cornerVariantPrefab != null
            && cornerVariantPrefab != this)
        {
            return cornerVariantPrefab;
        }

        return null;
    }

    private bool TryCreateEndpointVisualObjectFromVariant(
        ref GameObject targetObject,
        string objectName,
        ConveyorBelt sourceVariant)
    {
        if (targetObject != null || sourceVariant == null)
        {
            return false;
        }

        GameObject sourceObject = sourceVariant.FindEndpointVisualObject(objectName);
        if (sourceObject == null)
        {
            return false;
        }

        GameObject clone = Instantiate(sourceObject, transform, false);
        if (clone == null)
        {
            return false;
        }

        clone.name = objectName;
        Transform cloneTransform = clone.transform;
        Transform sourceTransform = sourceObject.transform;
        cloneTransform.localPosition = sourceTransform.localPosition;
        cloneTransform.localRotation = sourceTransform.localRotation;
        cloneTransform.localScale = sourceTransform.localScale;
        clone.SetActive(sourceObject.activeSelf);
        targetObject = clone;
        return true;
    }

    private bool SetEndpointVisualsActive(bool startEnd, bool endEnd, bool startSeam, bool endSeam)
    {
        bool changed = false;
        changed |= SetEndpointVisualActive(endStartObject, startEnd);
        changed |= SetEndpointVisualActive(endEndObject, endEnd);
        changed |= SetEndpointVisualActive(seamStartObject, startSeam);
        changed |= SetEndpointVisualActive(seamEndObject, endSeam);
        return changed;
    }

    private static bool SetEndpointVisualActive(GameObject targetObject, bool isActive)
    {
        if (targetObject == null || targetObject.activeSelf == isActive)
        {
            return false;
        }

        targetObject.SetActive(isActive);
        return true;
    }

    private void CacheBeltTopTransformState()
    {
        if (beltTopTransformStateCached)
        {
            return;
        }

        RefreshBeltTopRenderInfo();
        beltTopTransformStates.Clear();

        for (int i = 0; i < beltTopRenderInfos.Count; i++)
        {
            MeshRenderer renderer = beltTopRenderInfos[i].Renderer;
            if (renderer == null || renderer.transform == null)
            {
                continue;
            }

            Transform topTransform = renderer.transform;
            Vector3 baseLocalScale = topTransform.localScale;
            beltTopTransformStates.Add(new BeltTopTransformState
            {
                Transform = topTransform,
                Renderer = renderer,
                BaseLocalPosition = topTransform.localPosition,
                BaseLocalRotation = topTransform.localRotation,
                BaseLocalScale = baseLocalScale,
                BaseRootPosition = transform.InverseTransformPoint(topTransform.position),
                BaseLocalLengthX = CalculateRendererLocalLength(renderer, baseLocalScale.x, true),
                BaseLocalLengthZ = CalculateRendererLocalLength(renderer, baseLocalScale.z, false)
            });
        }

        beltTopTransformStateCached = true;
    }

    private bool ApplyBeltTopEndpointExtension(bool startSeam, bool endSeam)
    {
        CacheBeltTopTransformState();
        if (beltTopTransformStates.Count == 0)
        {
            return false;
        }

        int topCount = beltTopTransformStates.Count;
        float[] xExtensions = new float[topCount];
        float[] zExtensions = new float[topCount];
        Vector3[] positionOffsets = new Vector3[topCount];
        CollectEndpointExtension(seamStartObject, startSeam, xExtensions, zExtensions, positionOffsets);
        CollectEndpointExtension(seamEndObject, endSeam, xExtensions, zExtensions, positionOffsets);

        bool changed = false;
        for (int i = 0; i < topCount; i++)
        {
            BeltTopTransformState topState = beltTopTransformStates[i];
            if (topState.Transform == null)
            {
                continue;
            }

            Vector3 targetScale = topState.BaseLocalScale;
            Vector3 targetPosition = topState.BaseLocalPosition;
            if (xExtensions[i] > 0f)
            {
                float targetLengthX = Mathf.Max(0.0001f, topState.BaseLocalLengthX + xExtensions[i]);
                targetScale.x = topState.BaseLocalScale.x * (targetLengthX / Mathf.Max(topState.BaseLocalLengthX, 0.0001f));
            }

            if (zExtensions[i] > 0f)
            {
                float targetLengthZ = Mathf.Max(0.0001f, topState.BaseLocalLengthZ + zExtensions[i]);
                targetScale.z = topState.BaseLocalScale.z * (targetLengthZ / Mathf.Max(topState.BaseLocalLengthZ, 0.0001f));
            }

            targetPosition += positionOffsets[i];

            if (Vector3.SqrMagnitude(topState.Transform.localPosition - targetPosition) <= 0.0000001f
                && Vector3.SqrMagnitude(topState.Transform.localScale - targetScale) <= 0.0000001f)
            {
                continue;
            }

            topState.Transform.localPosition = targetPosition;
            topState.Transform.localScale = targetScale;
            changed = true;
        }

        return changed;
    }

    private void CollectEndpointExtension(
        GameObject endpointObject,
        bool isActive,
        float[] xExtensions,
        float[] zExtensions,
        Vector3[] positionOffsets)
    {
        if (!isActive || endpointObject == null)
        {
            return;
        }

        Vector3 endpointRootPosition = transform.InverseTransformPoint(endpointObject.transform.position);
        int topIndex = FindNearestBeltTopTransformStateIndex(endpointRootPosition);
        if (topIndex < 0)
        {
            return;
        }

        BeltTopTransformState topState = beltTopTransformStates[topIndex];
        Vector3 localDirection = GetEndpointDirectionInBeltTopLocalSpace(topState, endpointObject.transform);
        bool useXAxis = Mathf.Abs(localDirection.x) > Mathf.Abs(localDirection.z);
        float sign = useXAxis
            ? Mathf.Sign(localDirection.x)
            : Mathf.Sign(localDirection.z);
        if (Mathf.Approximately(sign, 0f))
        {
            sign = 1f;
        }

        float extension = CalculateEndpointVisualLocalLength(endpointObject);
        if (extension <= 0f)
        {
            return;
        }

        if (useXAxis)
        {
            xExtensions[topIndex] += extension;
        }
        else
        {
            zExtensions[topIndex] += extension;
        }

        Vector3 localAxis = useXAxis ? Vector3.right : Vector3.forward;
        positionOffsets[topIndex] += topState.BaseLocalRotation * (localAxis * sign * extension * 0.5f);
    }

    private Vector3 GetEndpointDirectionInBeltTopLocalSpace(BeltTopTransformState topState, Transform endpointTransform)
    {
        if (topState.Transform == null || endpointTransform == null)
        {
            return Vector3.zero;
        }

        Transform parent = topState.Transform.parent;
        Vector3 endpointParentPosition = parent != null
            ? parent.InverseTransformPoint(endpointTransform.position)
            : endpointTransform.position;
        Vector3 parentDirection = endpointParentPosition - topState.BaseLocalPosition;
        return Quaternion.Inverse(topState.BaseLocalRotation) * parentDirection;
    }

    private int FindNearestBeltTopTransformStateIndex(Vector3 rootLocalPosition)
    {
        int result = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < beltTopTransformStates.Count; i++)
        {
            BeltTopTransformState topState = beltTopTransformStates[i];
            if (topState.Transform == null)
            {
                continue;
            }

            Vector3 delta = rootLocalPosition - topState.BaseRootPosition;
            float distance = delta.x * delta.x + delta.z * delta.z;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                result = i;
            }
        }

        return result;
    }

    private static float CalculateRendererLocalLength(MeshRenderer renderer, float localScale, bool useXAxis)
    {
        MeshFilter meshFilter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh == null)
        {
            return 1f;
        }

        float meshLength = useXAxis ? mesh.bounds.size.x : mesh.bounds.size.z;
        return Mathf.Max(0.0001f, Mathf.Abs(meshLength * localScale));
    }

    private static float CalculateEndpointVisualLocalLength(GameObject endpointObject)
    {
        if (endpointObject == null)
        {
            return 0f;
        }

        MeshFilter meshFilter = endpointObject.GetComponent<MeshFilter>();
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh == null)
        {
            return 0f;
        }

        return Mathf.Abs(mesh.bounds.size.z * endpointObject.transform.localScale.z);
    }

    private void RefreshEndpointNeighbors(EndpointRuntimeState state, ConveyorBelt skippedBelt)
    {
        if (!state.HasRuntime)
        {
            return;
        }

        RefreshEndpointVisualsNearCoordinate(state.AnchorCoordinate, skippedBelt);
        if (state.OccupiedCoordinates != null)
        {
            for (int i = 0; i < state.OccupiedCoordinates.Length; i++)
            {
                RefreshEndpointVisualsNearCoordinate(state.OccupiedCoordinates[i], skippedBelt);
            }
        }

        RefreshEndpointVisualsNearCoordinate(state.StartCoordinate, skippedBelt);
        RefreshEndpointVisualsNearCoordinate(state.EndCoordinate, skippedBelt);
    }

    private static void RefreshEndpointVisualsNearCoordinate(Vector2Int coordinate, ConveyorBelt skippedBelt)
    {
        RefreshEndpointVisualsAtCoordinate(coordinate, skippedBelt);
        for (int i = 0; i < EndpointRefreshDirections.Length; i++)
        {
            RefreshEndpointVisualsAtCoordinate(coordinate + EndpointRefreshDirections[i], skippedBelt);
        }
    }

    private static void RefreshEndpointVisualsAtCoordinate(Vector2Int coordinate, ConveyorBelt skippedBelt)
    {
        if (!TryGetConveyorBlockAtCoordinate(coordinate, out _, out ConveyorBelt conveyorBelt)
            || conveyorBelt == skippedBelt)
        {
            return;
        }

        conveyorBelt.RefreshEndpointVisuals();
    }

    private void RefreshVirtualRenderingAfterEndpointVisualChange()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RefreshBeltTopRenderInfo();
        ApplyBeltTopShaderProperties();
        TerrainGenerator.Active?.RegisterVirtualConveyorBelt(this);
    }

    private void RefreshBeltTopRenderInfo()
    {
        EnsureRendererCache();

        beltTopRenderInfos.Clear();
        TryAddBeltTopRenderInfo(beltTopRenderer);

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            TryAddBeltTopRenderInfo(cachedRenderers[i]);
        }

        beltTopRenderInfos.Sort(CompareBeltTopCenterZ);

        float uvLengthOffset = 0f;
        for (int i = 0; i < beltTopRenderInfos.Count; i++)
        {
            BeltTopRenderInfo info = beltTopRenderInfos[i];
            info.UvLengthOffset = uvLengthOffset;
            beltTopRenderInfos[i] = info;
            uvLengthOffset += info.UvLengthScale;
        }

        beltTopRenderer = beltTopRenderInfos.Count > 0 ? beltTopRenderInfos[0].Renderer : null;
    }

    private void ApplyBeltTopShaderProperties()
    {
        if (beltTopRenderInfos.Count == 0)
        {
            RefreshBeltTopRenderInfo();
        }

        float targetUvScrollY = -ConveyorSpeed * 0.75f;
        beltTopPropertyBlock ??= new MaterialPropertyBlock();

        for (int i = 0; i < beltTopRenderInfos.Count; i++)
        {
            BeltTopRenderInfo info = beltTopRenderInfos[i];
            MeshRenderer renderer = info.Renderer;
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(beltTopPropertyBlock);
            beltTopPropertyBlock.SetFloat(UvScrollXShaderId, 0f);
            beltTopPropertyBlock.SetFloat(UvScrollYShaderId, targetUvScrollY);
            beltTopPropertyBlock.SetFloat(UvLengthScaleShaderId, info.UvLengthScale);
            beltTopPropertyBlock.SetFloat(UvLengthOffsetShaderId, info.UvLengthOffset);
            renderer.SetPropertyBlock(beltTopPropertyBlock);
        }
    }

    private void TryAddBeltTopRenderInfo(MeshRenderer renderer)
    {
        if (renderer == null || renderer.name != "BeltTop" || TryGetBeltTopRenderInfo(renderer, out _))
        {
            return;
        }

        beltTopRenderInfos.Add(new BeltTopRenderInfo
        {
            Renderer = renderer,
            CenterZ = transform.InverseTransformPoint(renderer.transform.position).z,
            UvLengthScale = CalculateBeltTopUvLengthScale(renderer),
            UvLengthOffset = 0f
        });
    }

    private bool TryGetBeltTopRenderInfo(MeshRenderer renderer, out BeltTopRenderInfo info)
    {
        for (int i = 0; i < beltTopRenderInfos.Count; i++)
        {
            info = beltTopRenderInfos[i];
            if (info.Renderer == renderer)
            {
                return true;
            }
        }

        info = default;
        return false;
    }

    private static int CompareBeltTopCenterZ(BeltTopRenderInfo left, BeltTopRenderInfo right)
    {
        return left.CenterZ.CompareTo(right.CenterZ);
    }

    private static float CalculateBeltTopUvLengthScale(MeshRenderer renderer)
    {
        Matrix4x4 matrix = renderer.localToWorldMatrix;
        Vector3 widthAxis = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        Vector3 lengthAxis = new Vector3(matrix.m02, matrix.m12, matrix.m22);
        float currentAspect = lengthAxis.magnitude / Mathf.Max(widthAxis.magnitude, 0.0001f);
        return Mathf.Max(currentAspect / UvLengthReferenceAspect, 0.0001f);
    }

    private void ConfigureRuntimeRenderers()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureRendererCache();

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        ApplyVirtualRenderingSuppression();
    }

    private void EnsureRendererCache()
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0)
        {
            cachedRenderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        if (cachedRendererMeshFilters == null || cachedRendererMeshFilters.Length != cachedRenderers.Length)
        {
            cachedRendererMeshFilters = new MeshFilter[cachedRenderers.Length];
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                cachedRendererMeshFilters[i] = cachedRenderers[i] != null
                    ? cachedRenderers[i].GetComponent<MeshFilter>()
                    : null;
            }
        }
    }

    private void ApplyVirtualRenderingSuppression()
    {
        if (!virtualRenderingSuppressed)
        {
            SetNativeRenderersEnabled(true);
            return;
        }

        if (virtualRenderingSuppressBeltTopOnly && beltTopRenderInfos.Count == 0)
        {
            RefreshBeltTopRenderInfo();
        }

        EnsureRendererCache();
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedRenderers[i];
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            renderer.enabled = virtualRenderingSuppressBeltTopOnly
                ? !TryGetBeltTopRenderInfo(renderer, out _)
                : false;
        }
    }

    private void SetNativeRenderersEnabled(bool isEnabled)
    {
        EnsureRendererCache();
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedRenderers[i];
            if (renderer != null)
            {
                renderer.enabled = isEnabled;
            }
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();

        cachedRenderers = null;
        cachedRendererMeshFilters = null;
        beltTopTransformStates.Clear();
        beltTopTransformStateCached = false;

        if (conveyorSpeed < 0f)
        {
            conveyorSpeed = 0f;
        }

        RefreshBeltTopRenderInfo();
        CacheBeltTopTransformState();
        ApplyBeltTopShaderProperties();
    }
#endif
}
