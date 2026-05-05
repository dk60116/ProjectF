using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ConveyorBelt : InstallationObject
{
    private static readonly int UvScrollXShaderId = Shader.PropertyToID("_UVScrollX");
    private static readonly int UvScrollYShaderId = Shader.PropertyToID("_UVScrollY");

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
    private float lastAppliedUvScrollY = float.NaN;
    private bool virtualRenderingSuppressed;
    private bool virtualRenderingSuppressBeltTopOnly;

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
        ResolveBeltTopRenderer();
        ConfigureRuntimeRenderers();
        ApplyBeltTopScroll();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResolveBeltTopRenderer();
        ConfigureRuntimeRenderers();
        ApplyBeltTopScroll();
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

        ResolveBeltTopRenderer();
        EnsureRendererCache();

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (beltTopOnly && renderer != beltTopRenderer)
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
            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            int entryCount = Mathf.Min(materialCount, subMeshCount);
            bool hasUvScroll = renderer == beltTopRenderer;
            float uvScrollY = hasUvScroll ? -ConveyorSpeed * 0.75f : 0f;
            Matrix4x4 matrix = renderer.localToWorldMatrix;
            int layer = renderer.gameObject.layer;

            for (int materialIndex = 0; materialIndex < entryCount; materialIndex++)
            {
                Material material = sharedMaterialBuffer[materialIndex];
                if (material == null)
                {
                    continue;
                }

                results.Add(new VirtualConveyorBeltRenderData(
                    mesh,
                    material,
                    matrix,
                    layer,
                    materialIndex,
                    hasUvScroll,
                    uvScrollY));
            }
        }
    }

    public void SetVirtualRenderingSuppressed(bool isSuppressed, bool beltTopOnly = false)
    {
        virtualRenderingSuppressed = isSuppressed;
        virtualRenderingSuppressBeltTopOnly = isSuppressed && beltTopOnly;
        ApplyVirtualRenderingSuppression();
    }

    private void ResolveBeltTopRenderer()
    {
        if (beltTopRenderer != null)
        {
            return;
        }

        Transform beltTopTransform = transform.Find("BeltTop");
        if (beltTopTransform != null)
        {
            beltTopRenderer = beltTopTransform.GetComponent<MeshRenderer>();
        }

        if (beltTopRenderer == null)
        {
            MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < childRenderers.Length; i++)
            {
                MeshRenderer candidate = childRenderers[i];
                if (candidate != null && candidate.name == "BeltTop")
                {
                    beltTopRenderer = candidate;
                    break;
                }
            }
        }
    }

    private void ApplyBeltTopScroll()
    {
        ResolveBeltTopRenderer();
        if (beltTopRenderer == null)
        {
            return;
        }

        float targetUvScrollY = -ConveyorSpeed * 0.75f;
        if (!float.IsNaN(lastAppliedUvScrollY) && Mathf.Approximately(lastAppliedUvScrollY, targetUvScrollY))
        {
            return;
        }

        if (beltTopPropertyBlock == null)
        {
            beltTopPropertyBlock = new MaterialPropertyBlock();
        }

        beltTopRenderer.GetPropertyBlock(beltTopPropertyBlock);
        beltTopPropertyBlock.SetFloat(UvScrollXShaderId, 0f);
        beltTopPropertyBlock.SetFloat(UvScrollYShaderId, targetUvScrollY);
        beltTopRenderer.SetPropertyBlock(beltTopPropertyBlock);
        lastAppliedUvScrollY = targetUvScrollY;
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

        EnsureRendererCache();
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = virtualRenderingSuppressBeltTopOnly
                ? renderer != beltTopRenderer
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

        ResolveBeltTopRenderer();
        ApplyBeltTopScroll();
    }
#endif
}
