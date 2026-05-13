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
    private static WorkableObjectRangeVisual sharedRangeVisual;

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
        RefreshSharedRangeVisual();
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
        RefreshSharedRangeVisual();
    }

    protected override void OnDisable()
    {
        DisableLegacyRangeVisual();
        ActiveInstances.Remove(this);
        globalMaxFocusActivationRadiusDirty = true;
        RefreshSharedRangeVisual();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        RefreshSharedRangeVisual();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        globalMaxFocusActivationRadiusDirty = true;
        if (Application.isPlaying)
        {
            RefreshSharedRangeVisual();
        }
    }
#endif

    private void RefreshWorkableRangeVisual()
    {
        RefreshSharedRangeVisual();
    }

    private static void RefreshSharedRangeVisual()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        List<WorkableObjectRangeVisualRequest> requests = new List<WorkableObjectRangeVisualRequest>();
        foreach (WorkableObject workableObject in ActiveInstances)
        {
            if (workableObject == null)
            {
                continue;
            }

            workableObject.DisableLegacyRangeVisual();
            if (!workableObject.showWorkableRange
                || workableObject.workableRangeCells == 0u
                || !workableObject.ShouldShowWorkableRangeVisual())
            {
                continue;
            }

            float rangeRadius = ResolveRangeRadius(workableObject.workableRangeCells);
            if (rangeRadius <= 0f)
            {
                continue;
            }

            requests.Add(new WorkableObjectRangeVisualRequest(
                workableObject.transform.position,
                rangeRadius,
                workableObject.rangeVisualYOffset));
        }

        if (requests.Count <= 0)
        {
            SetSharedRangeVisualActive(false);
            return;
        }

        WorkableObjectRangeVisual visual = GetOrCreateSharedRangeVisual();
        if (visual == null)
        {
            return;
        }

        visual.Configure(requests);
        if (!visual.gameObject.activeSelf)
        {
            visual.gameObject.SetActive(true);
        }
    }

    private static WorkableObjectRangeVisual GetOrCreateSharedRangeVisual()
    {
        if (sharedRangeVisual != null)
        {
            return sharedRangeVisual;
        }

        GameObject visualObject = new GameObject("Workable Range Visuals");
        sharedRangeVisual = visualObject.AddComponent<WorkableObjectRangeVisual>();
        return sharedRangeVisual;
    }

    private static void SetSharedRangeVisualActive(bool active)
    {
        if (sharedRangeVisual != null && sharedRangeVisual.gameObject.activeSelf != active)
        {
            sharedRangeVisual.gameObject.SetActive(active);
        }
    }

    private void DisableLegacyRangeVisual()
    {
        WorkableObjectRangeVisual[] visuals = GetComponentsInChildren<WorkableObjectRangeVisual>(true);
        for (int i = 0; i < visuals.Length; i++)
        {
            WorkableObjectRangeVisual visual = visuals[i];
            if (visual != null && visual != sharedRangeVisual && visual.gameObject.activeSelf)
            {
                visual.gameObject.SetActive(false);
            }
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

public readonly struct WorkableObjectRangeVisualRequest
{
    public readonly Vector3 Center;
    public readonly float Radius;
    public readonly float YOffset;

    public WorkableObjectRangeVisualRequest(Vector3 center, float radius, float yOffset)
    {
        Center = center;
        Radius = radius;
        YOffset = yOffset;
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
    private const int RangeAlphaTextureSize = 256;
    private const float RangeCenterTransparentRadius = 0.8f;
    private static Mesh sharedRangeQuadMesh;
    private static Material sharedRangeMaterial;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Texture2D rangeAlphaTexture;

    public void Configure(IReadOnlyList<WorkableObjectRangeVisualRequest> requests)
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

        if (!TryBuildRangeAlphaTexture(requests, out Bounds bounds, out float yPosition))
        {
            return;
        }

        transform.SetParent(null, true);
        transform.position = new Vector3(bounds.center.x, yPosition, bounds.center.z);
        transform.rotation = Quaternion.identity;
        transform.localScale = new Vector3(
            Mathf.Max(0.01f, bounds.size.x),
            1f,
            Mathf.Max(0.01f, bounds.size.z));

        propertyBlock ??= new MaterialPropertyBlock();
        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorShaderId, RangeFillColor);
        propertyBlock.SetColor(ColorShaderId, RangeFillColor);
        propertyBlock.SetTexture(BaseMapShaderId, rangeAlphaTexture);
        propertyBlock.SetTexture(MainTexShaderId, rangeAlphaTexture);
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

    private bool TryBuildRangeAlphaTexture(
        IReadOnlyList<WorkableObjectRangeVisualRequest> requests,
        out Bounds bounds,
        out float yPosition)
    {
        bounds = default;
        yPosition = 0f;
        if (requests == null || requests.Count <= 0)
        {
            return false;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float visualY = float.MinValue;
        float maxRadius = 0f;
        for (int i = 0; i < requests.Count; i++)
        {
            WorkableObjectRangeVisualRequest request = requests[i];
            float radius = Mathf.Max(0f, request.Radius);
            minX = Mathf.Min(minX, request.Center.x - radius);
            maxX = Mathf.Max(maxX, request.Center.x + radius);
            minZ = Mathf.Min(minZ, request.Center.z - radius);
            maxZ = Mathf.Max(maxZ, request.Center.z + radius);
            visualY = Mathf.Max(visualY, request.Center.y + Mathf.Max(0f, request.YOffset));
            maxRadius = Mathf.Max(maxRadius, radius);
        }

        if (minX > maxX || minZ > maxZ || maxRadius <= 0f)
        {
            return false;
        }

        EnsureRangeAlphaTexture();
        float width = Mathf.Max(0.01f, maxX - minX);
        float height = Mathf.Max(0.01f, maxZ - minZ);
        yPosition = visualY > float.MinValue ? visualY : 0f;
        bounds = new Bounds(
            new Vector3((minX + maxX) * 0.5f, yPosition, (minZ + maxZ) * 0.5f),
            new Vector3(width, 0.01f, height));

        int textureWidth = rangeAlphaTexture.width;
        int textureHeight = rangeAlphaTexture.height;
        bool[] inside = new bool[textureWidth * textureHeight];
        int[] left = new int[inside.Length];
        int[] right = new int[inside.Length];
        int[] down = new int[inside.Length];
        int[] up = new int[inside.Length];

        for (int y = 0; y < textureHeight; y++)
        {
            float worldZ = minZ + (((float)y + 0.5f) / textureHeight) * height;
            for (int x = 0; x < textureWidth; x++)
            {
                float worldX = minX + (((float)x + 0.5f) / textureWidth) * width;
                inside[(y * textureWidth) + x] = IsInsideAnyRange(worldX, worldZ, requests);
            }
        }

        FillAxisDistanceFields(inside, textureWidth, textureHeight, left, right, down, up);

        Color[] pixels = new Color[inside.Length];
        float pixelWorldWidth = width / textureWidth;
        float pixelWorldHeight = height / textureHeight;
        float fadeDistance = Mathf.Max(0.001f, maxRadius * (1f - RangeCenterTransparentRadius));
        for (int i = 0; i < inside.Length; i++)
        {
            if (!inside[i])
            {
                pixels[i] = new Color(1f, 1f, 1f, 0f);
                continue;
            }

            float horizontalDistance = (Mathf.Min(left[i], right[i]) - 0.5f) * pixelWorldWidth;
            float verticalDistance = (Mathf.Min(down[i], up[i]) - 0.5f) * pixelWorldHeight;
            float distanceToBoundary = Mathf.Max(0f, Mathf.Min(horizontalDistance, verticalDistance));
            float edgeStrength = 1f - Mathf.Clamp01(distanceToBoundary / fadeDistance);
            float alpha = Mathf.SmoothStep(0f, 1f, edgeStrength);
            pixels[i] = new Color(1f, 1f, 1f, alpha);
        }

        rangeAlphaTexture.SetPixels(pixels);
        rangeAlphaTexture.Apply(false, false);
        return true;
    }

    private void EnsureRangeAlphaTexture()
    {
        if (rangeAlphaTexture != null
            && rangeAlphaTexture.width == RangeAlphaTextureSize
            && rangeAlphaTexture.height == RangeAlphaTextureSize)
        {
            return;
        }

        rangeAlphaTexture = new Texture2D(
            RangeAlphaTextureSize,
            RangeAlphaTextureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "WorkableObject_RangeUnionFade",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
    }

    private static bool IsInsideAnyRange(
        float worldX,
        float worldZ,
        IReadOnlyList<WorkableObjectRangeVisualRequest> requests)
    {
        for (int i = 0; i < requests.Count; i++)
        {
            WorkableObjectRangeVisualRequest request = requests[i];
            float radius = Mathf.Max(0f, request.Radius);
            if (Mathf.Abs(worldX - request.Center.x) <= radius
                && Mathf.Abs(worldZ - request.Center.z) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private static void FillAxisDistanceFields(
        bool[] inside,
        int width,
        int height,
        int[] left,
        int[] right,
        int[] down,
        int[] up)
    {
        for (int y = 0; y < height; y++)
        {
            int distance = 0;
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                distance = inside[index] ? distance + 1 : 0;
                left[index] = distance;
            }

            distance = 0;
            for (int x = width - 1; x >= 0; x--)
            {
                int index = (y * width) + x;
                distance = inside[index] ? distance + 1 : 0;
                right[index] = distance;
            }
        }

        for (int x = 0; x < width; x++)
        {
            int distance = 0;
            for (int y = 0; y < height; y++)
            {
                int index = (y * width) + x;
                distance = inside[index] ? distance + 1 : 0;
                down[index] = distance;
            }

            distance = 0;
            for (int y = height - 1; y >= 0; y--)
            {
                int index = (y * width) + x;
                distance = inside[index] ? distance + 1 : 0;
                up[index] = distance;
            }
        }
    }

    private static Material ResolveRangeMaterial()
    {
        if (sharedRangeMaterial != null)
        {
            return sharedRangeMaterial;
        }

        Shader shader = Shader.Find("Custom/WorkableRangeOverlay");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

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
            sharedRangeMaterial.SetTexture(BaseMapShaderId, Texture2D.whiteTexture);
        }

        if (sharedRangeMaterial.HasProperty(ColorShaderId))
        {
            sharedRangeMaterial.SetColor(ColorShaderId, RangeFillColor);
        }

        if (sharedRangeMaterial.HasProperty(MainTexShaderId))
        {
            sharedRangeMaterial.SetTexture(MainTexShaderId, Texture2D.whiteTexture);
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
