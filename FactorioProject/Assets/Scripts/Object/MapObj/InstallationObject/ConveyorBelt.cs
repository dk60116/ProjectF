using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ConveyorBelt : InstallationObject
{
    private const float UvLengthReferenceAspect = 1.4285714f;

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

    private MaterialPropertyBlock beltTopPropertyBlock;
    private MeshRenderer[] cachedRenderers;
    private MeshFilter[] cachedRendererMeshFilters;
    private readonly List<Material> sharedMaterialBuffer = new List<Material>(4);
    private readonly List<BeltTopRenderInfo> beltTopRenderInfos = new List<BeltTopRenderInfo>(8);
    private bool virtualRenderingSuppressed;
    private bool virtualRenderingSuppressBeltTopOnly;

    private struct BeltTopRenderInfo
    {
        public MeshRenderer Renderer;
        public float CenterZ;
        public float UvLengthScale;
        public float UvLengthOffset;
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

    public void CopyObjectInfoItemIds(List<int> results, int maxCount)
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

            int laneCount = block.GetRuntimeConveyorLaneCount();
            for (int laneIndex = 0; laneIndex < laneCount && results.Count < maxCount; laneIndex++)
            {
                if (block.TryGetRuntimeConveyorItemIdAtLane(laneIndex, out int itemId))
                {
                    results.Add(itemId);
                }
            }
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
        RefreshBeltTopRenderInfo();
        ConfigureRuntimeRenderers();
        ApplyBeltTopShaderProperties();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        RefreshBeltTopRenderInfo();
        ConfigureRuntimeRenderers();
        ApplyBeltTopShaderProperties();
    }

    protected override void OnDisable()
    {
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
            if (renderer == null)
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
            if (renderer == null)
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

        if (conveyorSpeed < 0f)
        {
            conveyorSpeed = 0f;
        }

        RefreshBeltTopRenderInfo();
        ApplyBeltTopShaderProperties();
    }
#endif
}
