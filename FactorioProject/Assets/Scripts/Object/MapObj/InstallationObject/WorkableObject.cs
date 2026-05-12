using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class WorkableObject : InstallationObject
{
    private static readonly HashSet<WorkableObject> ActiveInstances = new HashSet<WorkableObject>();
    private static float cachedGlobalMaxFocusActivationRadius;
    private static bool globalMaxFocusActivationRadiusDirty = true;
    private static BagSlot craftingSlotRangeVisualRequestSource;

    [SerializeField, FormerlySerializedAs("focusActivationRadius")]
    private uint workableRangeCells = 1u;
    [SerializeField]
    private bool showWorkableRange = true;
    [SerializeField, Min(0f)]
    private float rangeVisualYOffset = 0.04f;
    private bool selectedRangeVisualRequested;
    private bool globalRangeVisualSuppressed;

    public uint WorkableRangeCells => workableRangeCells;
    public override float FocusActivationRadius => ResolveRangeRadius(workableRangeCells);

    public static float ResolveRangeRadius(uint rangeCells)
    {
        return Mathf.Max(0f, rangeCells - 0.5f);
    }

    public bool ContainsWorldPositionInWorkableRange(Vector3 worldPosition)
    {
        float rangeRadius = FocusActivationRadius;
        if (rangeRadius <= 0f)
        {
            return false;
        }

        Vector3 center = transform.position;
        return Mathf.Abs(worldPosition.x - center.x) <= rangeRadius
               && Mathf.Abs(worldPosition.z - center.z) <= rangeRadius;
    }

    public void SetSelectedRangeVisualRequested(bool requested)
    {
        if (selectedRangeVisualRequested == requested)
        {
            if (requested)
            {
                RefreshWorkableRangeVisual();
            }

            return;
        }

        selectedRangeVisualRequested = requested;
        RefreshWorkableRangeVisual();
    }

    public void SetGlobalRangeVisualSuppressed(bool suppressed)
    {
        if (globalRangeVisualSuppressed == suppressed)
        {
            return;
        }

        globalRangeVisualSuppressed = suppressed;
        RefreshWorkableRangeVisual();
    }

    public static void SetCraftingSlotRangeVisualsRequested(BagSlot source, bool requested)
    {
        if (requested)
        {
            if (source == null || craftingSlotRangeVisualRequestSource == source)
            {
                return;
            }

            craftingSlotRangeVisualRequestSource = source;
            RefreshAllRangeVisuals();
            return;
        }

        if (craftingSlotRangeVisualRequestSource != null && craftingSlotRangeVisualRequestSource != source)
        {
            return;
        }

        craftingSlotRangeVisualRequestSource = null;
        RefreshAllRangeVisuals();
    }

    public static void RefreshAllRangeVisuals()
    {
        foreach (WorkableObject workableObject in ActiveInstances)
        {
            if (workableObject == null)
            {
                continue;
            }

            workableObject.RefreshWorkableRangeVisual();
        }
    }

    public new static float GlobalMaxFocusActivationRadius
    {
        get
        {
            if (!globalMaxFocusActivationRadiusDirty)
            {
                return cachedGlobalMaxFocusActivationRadius;
            }

            cachedGlobalMaxFocusActivationRadius = 0f;
            foreach (WorkableObject workableObject in ActiveInstances)
            {
                if (workableObject == null)
                {
                    continue;
                }

                cachedGlobalMaxFocusActivationRadius = Mathf.Max(
                    cachedGlobalMaxFocusActivationRadius,
                    workableObject.FocusActivationRadius);
            }

            globalMaxFocusActivationRadiusDirty = false;
            return cachedGlobalMaxFocusActivationRadius;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveInstances.Add(this);
        globalMaxFocusActivationRadiusDirty = true;
        RefreshWorkableRangeVisual();
    }

    protected override void OnDisable()
    {
        SetWorkableRangeVisualActive(false);
        ActiveInstances.Remove(this);
        globalMaxFocusActivationRadiusDirty = true;
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        RefreshWorkableRangeVisual();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        globalMaxFocusActivationRadiusDirty = true;
        if (Application.isPlaying)
        {
            RefreshWorkableRangeVisual();
        }
    }
#endif

    private void RefreshWorkableRangeVisual()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!showWorkableRange || workableRangeCells == 0u || !ShouldShowWorkableRangeVisual())
        {
            SetWorkableRangeVisualActive(false);
            return;
        }

        WorkableObjectRangeVisual visual = GetOrCreateWorkableRangeVisual();
        if (visual == null)
        {
            return;
        }

        visual.Configure(workableRangeCells, rangeVisualYOffset);
        if (!visual.gameObject.activeSelf)
        {
            visual.gameObject.SetActive(true);
        }
    }

    private WorkableObjectRangeVisual GetOrCreateWorkableRangeVisual()
    {
        WorkableObjectRangeVisual visual = GetComponentInChildren<WorkableObjectRangeVisual>(true);
        if (visual != null)
        {
            return visual;
        }

        GameObject visualObject = new GameObject("Workable Range Visual");
        visualObject.transform.SetParent(transform, false);
        return visualObject.AddComponent<WorkableObjectRangeVisual>();
    }

    private void SetWorkableRangeVisualActive(bool active)
    {
        WorkableObjectRangeVisual visual = GetComponentInChildren<WorkableObjectRangeVisual>(true);
        if (visual != null && visual.gameObject.activeSelf != active)
        {
            visual.gameObject.SetActive(active);
        }
    }

    private bool ShouldShowWorkableRangeVisual()
    {
        if (selectedRangeVisualRequested)
        {
            return true;
        }

        return !globalRangeVisualSuppressed && ShouldShowWorkableRangeVisuals();
    }

    private static bool ShouldShowWorkableRangeVisuals()
    {
        if (craftingSlotRangeVisualRequestSource != null
            && craftingSlotRangeVisualRequestSource.IsCraftingExpanded)
        {
            return true;
        }

        craftingSlotRangeVisualRequestSource = null;

        GameManager gameManager = GameManager.Instance;
        return gameManager != null
               && (gameManager.InstallationPlacementActive || gameManager.MapEditActive);
    }
}

[DisallowMultipleComponent]
public sealed class WorkableObjectRangeVisual : MonoBehaviour
{
    private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapShaderId = Shader.PropertyToID("_BaseMap");
    private static readonly int ColorShaderId = Shader.PropertyToID("_Color");
    private static readonly int MainTexShaderId = Shader.PropertyToID("_MainTex");
    private static readonly Color RangeFillColor = new Color(0.05f, 1f, 0.05f, 0.1f);
    private const int RangeAlphaTextureSize = 64;
    private const float RangeCenterTransparentRadius = 0.8f;
    private static Mesh sharedRangeQuadMesh;
    private static Material sharedRangeMaterial;
    private static Texture2D sharedRangeAlphaTexture;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;

    public void Configure(uint rangeCells, float yOffset)
    {
        EnsureComponents();
        if (meshFilter == null || meshRenderer == null)
        {
            return;
        }

        meshFilter.sharedMesh = ResolveRangeQuadMesh();
        meshRenderer.sharedMaterial = ResolveRangeMaterial();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        float rangeDiameter = Mathf.Max(1f, WorkableObject.ResolveRangeRadius(rangeCells) * 2f);
        Vector3 parentScale = ResolveParentLossyScale();
        transform.localPosition = new Vector3(0f, DivideByScale(Mathf.Max(0f, yOffset), parentScale.y), 0f);
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(
            DivideByScale(rangeDiameter, parentScale.x),
            1f,
            DivideByScale(rangeDiameter, parentScale.z));

        propertyBlock ??= new MaterialPropertyBlock();
        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorShaderId, RangeFillColor);
        propertyBlock.SetColor(ColorShaderId, RangeFillColor);
        propertyBlock.SetTexture(BaseMapShaderId, ResolveRangeAlphaTexture());
        propertyBlock.SetTexture(MainTexShaderId, ResolveRangeAlphaTexture());
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private void EnsureComponents()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }
        }
    }

    private Vector3 ResolveParentLossyScale()
    {
        Transform parentTransform = transform.parent;
        return parentTransform != null ? parentTransform.lossyScale : Vector3.one;
    }

    private static float DivideByScale(float value, float scale)
    {
        float absoluteScale = Mathf.Abs(scale);
        return absoluteScale > 0.0001f ? value / absoluteScale : value;
    }

    private static Mesh ResolveRangeQuadMesh()
    {
        if (sharedRangeQuadMesh != null)
        {
            return sharedRangeQuadMesh;
        }

        sharedRangeQuadMesh = new Mesh
        {
            name = "WorkableObject_RangeCells",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            },
            triangles = new[]
            {
                0, 1, 2,
                0, 2, 3
            }
        };
        sharedRangeQuadMesh.RecalculateNormals();
        sharedRangeQuadMesh.RecalculateBounds();
        return sharedRangeQuadMesh;
    }

    private static Texture2D ResolveRangeAlphaTexture()
    {
        if (sharedRangeAlphaTexture != null)
        {
            return sharedRangeAlphaTexture;
        }

        sharedRangeAlphaTexture = new Texture2D(
            RangeAlphaTextureSize,
            RangeAlphaTextureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "WorkableObject_RangeEdgeFade",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[RangeAlphaTextureSize * RangeAlphaTextureSize];
        float denominator = Mathf.Max(1f, RangeAlphaTextureSize - 1f);
        for (int y = 0; y < RangeAlphaTextureSize; y++)
        {
            for (int x = 0; x < RangeAlphaTextureSize; x++)
            {
                float normalizedX = (x / denominator) * 2f - 1f;
                float normalizedY = (y / denominator) * 2f - 1f;
                float edgeDistance = Mathf.Max(Mathf.Abs(normalizedX), Mathf.Abs(normalizedY));
                float alpha = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(RangeCenterTransparentRadius, 1f, edgeDistance));
                pixels[(y * RangeAlphaTextureSize) + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        sharedRangeAlphaTexture.SetPixels(pixels);
        sharedRangeAlphaTexture.Apply(false, true);
        return sharedRangeAlphaTexture;
    }

    private static Material ResolveRangeMaterial()
    {
        if (sharedRangeMaterial != null)
        {
            return sharedRangeMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        sharedRangeMaterial = new Material(shader)
        {
            name = "WorkableObject_RangeVisual_Runtime",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Transparent
        };

        if (sharedRangeMaterial.HasProperty(BaseColorShaderId))
        {
            sharedRangeMaterial.SetColor(BaseColorShaderId, RangeFillColor);
        }

        if (sharedRangeMaterial.HasProperty(BaseMapShaderId))
        {
            sharedRangeMaterial.SetTexture(BaseMapShaderId, ResolveRangeAlphaTexture());
        }

        if (sharedRangeMaterial.HasProperty(ColorShaderId))
        {
            sharedRangeMaterial.SetColor(ColorShaderId, RangeFillColor);
        }

        if (sharedRangeMaterial.HasProperty(MainTexShaderId))
        {
            sharedRangeMaterial.SetTexture(MainTexShaderId, ResolveRangeAlphaTexture());
        }

        if (sharedRangeMaterial.HasProperty("_Surface"))
        {
            sharedRangeMaterial.SetFloat("_Surface", 1f);
        }

        if (sharedRangeMaterial.HasProperty("_Blend"))
        {
            sharedRangeMaterial.SetFloat("_Blend", 0f);
        }

        if (sharedRangeMaterial.HasProperty("_SrcBlend"))
        {
            sharedRangeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (sharedRangeMaterial.HasProperty("_DstBlend"))
        {
            sharedRangeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (sharedRangeMaterial.HasProperty("_ZWrite"))
        {
            sharedRangeMaterial.SetFloat("_ZWrite", 0f);
        }

        if (sharedRangeMaterial.HasProperty("_Cull"))
        {
            sharedRangeMaterial.SetFloat("_Cull", (float)CullMode.Off);
        }

        sharedRangeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return sharedRangeMaterial;
    }
}
