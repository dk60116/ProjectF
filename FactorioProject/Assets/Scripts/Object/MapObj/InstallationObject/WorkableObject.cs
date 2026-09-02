using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class WorkableObject : InstallationObject
{
    private static readonly HashSet<WorkableObject> ActiveInstances = new HashSet<WorkableObject>();
    private static readonly HashSet<WorkableObject> SelectedRangeVisualInstances = new HashSet<WorkableObject>();
    private static float cachedGlobalMaxFocusActivationRadius;
    private static bool globalMaxFocusActivationRadiusDirty = true;
    private static BagSlot craftingSlotRangeVisualRequestSource;
    private static WorkableObjectRangeVisual sharedRangeVisual;
    private static bool installOrEditWorkableSelectionRangeVisualsRequested;

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
        return Mathf.Max(0f, rangeCells * 0.5f);
    }

    public bool ContainsWorldPositionInWorkableRange(Vector3 worldPosition)
    {
        if (!TryGetWorkableRangeBounds(out Bounds rangeBounds))
        {
            return false;
        }

        return worldPosition.x >= rangeBounds.min.x
               && worldPosition.x <= rangeBounds.max.x
               && worldPosition.z >= rangeBounds.min.z
               && worldPosition.z <= rangeBounds.max.z;
    }

    public bool TryGetWorkableRangeBounds(out Bounds bounds)
    {
        bounds = default;
        float rangeRadius = FocusActivationRadius;
        if (rangeRadius <= 0f)
        {
            return false;
        }

        Vector3 center = GetWorkableRangeCenter();
        float rangeSize = rangeRadius * 2f;
        bounds = new Bounds(
            center,
            new Vector3(rangeSize, 0.01f, rangeSize));
        return true;
    }

    private Vector3 GetWorkableRangeCenter()
    {
        if (TryGetRuntimeFootprintCenter(out Vector3 runtimeCenter))
        {
            return runtimeCenter;
        }

        return transform.position;
    }

    private bool TryGetRuntimeFootprintCenter(out Vector3 center)
    {
        center = default;
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            return false;
        }

        float x = 0f;
        float z = 0f;
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = occupiedCoordinates[i];
            x += coordinate.x;
            z += coordinate.y;
        }

        float count = occupiedCoordinates.Count;
        center = new Vector3(x / count, transform.position.y, z / count);
        return true;
    }

    public void SetSelectedRangeVisualRequested(bool requested)
    {
        if (selectedRangeVisualRequested == requested)
        {
            return;
        }

        selectedRangeVisualRequested = requested;
        if (requested)
        {
            SelectedRangeVisualInstances.Add(this);
        }
        else
        {
            SelectedRangeVisualInstances.Remove(this);
        }

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

    public static void SetInstallOrEditWorkableSelectionRangeVisualsRequested(bool requested)
    {
        if (installOrEditWorkableSelectionRangeVisualsRequested == requested)
        {
            return;
        }

        installOrEditWorkableSelectionRangeVisualsRequested = requested;
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
        if (selectedRangeVisualRequested)
        {
            SelectedRangeVisualInstances.Add(this);
        }

        globalMaxFocusActivationRadiusDirty = true;
        RefreshSharedRangeVisual();
    }

    protected override void OnDisable()
    {
        DisableLegacyRangeVisual();
        ActiveInstances.Remove(this);
        if (selectedRangeVisualRequested && !gameObject.activeInHierarchy)
        {
            SelectedRangeVisualInstances.Remove(this);
            selectedRangeVisualRequested = false;
        }

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
        HashSet<WorkableObject> appendedObjects = new HashSet<WorkableObject>();
        AppendRangeVisualRequests(ActiveInstances, requests, appendedObjects);
        AppendRangeVisualRequests(SelectedRangeVisualInstances, requests, appendedObjects);

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

    private static void AppendRangeVisualRequests(
        IEnumerable<WorkableObject> sourceObjects,
        List<WorkableObjectRangeVisualRequest> requests,
        HashSet<WorkableObject> appendedObjects)
    {
        if (sourceObjects == null || requests == null || appendedObjects == null)
        {
            return;
        }

        foreach (WorkableObject workableObject in sourceObjects)
        {
            if (workableObject == null)
            {
                continue;
            }

            if (!appendedObjects.Add(workableObject))
            {
                continue;
            }

            workableObject.DisableLegacyRangeVisual();
            if (!workableObject.showWorkableRange
                || workableObject.workableRangeCells == 0u
                || !workableObject.gameObject.activeInHierarchy
                || !workableObject.ShouldShowWorkableRangeVisual())
            {
                continue;
            }

            if (!workableObject.TryGetWorkableRangeBounds(out Bounds rangeBounds))
            {
                continue;
            }

            requests.Add(new WorkableObjectRangeVisualRequest(
                rangeBounds.center,
                rangeBounds.extents.x,
                workableObject.rangeVisualYOffset));
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

        if (!installOrEditWorkableSelectionRangeVisualsRequested)
        {
            return false;
        }

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
    private const float RangeAlphaMultiplier = 0.5f;
    private const float NightRangeAlphaMultiplier = 0.35f;
    private const float DaylightFactorRefreshThreshold = 0.01f;
    private const int RangeAlphaTextureSize = 256;
    private const float RangeCenterTransparentRadius = 0.8f;
    private static Mesh sharedRangeQuadMesh;
    private static Material sharedRangeMaterial;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Texture2D rangeAlphaTexture;
    private Color configuredFillColor = RangeFillColor;
    private bool[] rangeInsideScratch;
    private float[] rangeBoundaryDistanceScratch;
    private Color[] rangePixelScratch;
    private bool hasCachedRangeLayout;
    private int cachedRangeRequestHash;
    private int cachedRangeRequestCount;
    private Bounds cachedRangeBounds;
    private float cachedRangeYPosition;
    private float lastAppliedDaylightFactor = -1f;
    private bool hasConfiguredFillColor;

    private void OnEnable()
    {
        WorldTimeService.GlobalTimeStateChanged -= HandleGlobalTimeStateChanged;
        WorldTimeService.GlobalTimeStateChanged += HandleGlobalTimeStateChanged;
        ApplyRendererProperties(ResolveCurrentDaylightFactor(), true);
    }

    private void OnDisable()
    {
        WorldTimeService.GlobalTimeStateChanged -= HandleGlobalTimeStateChanged;
        lastAppliedDaylightFactor = -1f;
    }

    public void Configure(IReadOnlyList<WorkableObjectRangeVisualRequest> requests)
    {
        Configure(requests, RangeFillColor);
    }

    public void Configure(IReadOnlyList<WorkableObjectRangeVisualRequest> requests, Color fillColor)
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

        int requestCount = requests != null ? requests.Count : 0;
        int requestHash = ComputeRangeRequestHash(requests);
        Bounds bounds;
        float yPosition;
        if (hasCachedRangeLayout
            && rangeAlphaTexture != null
            && cachedRangeRequestCount == requestCount
            && cachedRangeRequestHash == requestHash)
        {
            bounds = cachedRangeBounds;
            yPosition = cachedRangeYPosition;
        }
        else if (!TryBuildRangeAlphaTexture(requests, out bounds, out yPosition))
        {
            hasCachedRangeLayout = false;
            return;
        }
        else
        {
            hasCachedRangeLayout = true;
            cachedRangeRequestHash = requestHash;
            cachedRangeRequestCount = requestCount;
            cachedRangeBounds = bounds;
            cachedRangeYPosition = yPosition;
        }

        transform.SetParent(null, true);
        transform.position = new Vector3(bounds.center.x, yPosition, bounds.center.z);
        transform.rotation = Quaternion.identity;
        transform.localScale = new Vector3(
            Mathf.Max(0.01f, bounds.size.x),
            1f,
            Mathf.Max(0.01f, bounds.size.z));

        configuredFillColor = fillColor;
        hasConfiguredFillColor = true;
        ApplyRendererProperties(ResolveCurrentDaylightFactor(), true);
    }

    private void HandleGlobalTimeStateChanged(
        float normalizedDayTime,
        float daylightFactor,
        bool isDay)
    {
        ApplyRendererProperties(daylightFactor, false);
    }

    private void ApplyRendererProperties(float daylightFactor, bool force)
    {
        if (!hasConfiguredFillColor || meshRenderer == null || rangeAlphaTexture == null)
        {
            return;
        }

        float clampedDaylightFactor = Mathf.Clamp01(daylightFactor);
        if (!force
            && Mathf.Abs(lastAppliedDaylightFactor - clampedDaylightFactor)
            < DaylightFactorRefreshThreshold)
        {
            return;
        }

        propertyBlock ??= new MaterialPropertyBlock();
        propertyBlock.Clear();
        Color displayFillColor = ResolveDisplayFillColor(
            configuredFillColor,
            clampedDaylightFactor);
        propertyBlock.SetColor(BaseColorShaderId, displayFillColor);
        propertyBlock.SetColor(ColorShaderId, displayFillColor);
        propertyBlock.SetTexture(BaseMapShaderId, rangeAlphaTexture);
        propertyBlock.SetTexture(MainTexShaderId, rangeAlphaTexture);
        meshRenderer.SetPropertyBlock(propertyBlock);
        lastAppliedDaylightFactor = clampedDaylightFactor;
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
        EnsureRangeAlphaScratch(textureWidth * textureHeight);
        bool[] inside = rangeInsideScratch;
        float[] boundaryDistances = rangeBoundaryDistanceScratch;

        for (int y = 0; y < textureHeight; y++)
        {
            float worldZ = minZ + (((float)y + 0.5f) / textureHeight) * height;
            for (int x = 0; x < textureWidth; x++)
            {
                float worldX = minX + (((float)x + 0.5f) / textureWidth) * width;
                inside[(y * textureWidth) + x] = IsInsideAnyRange(worldX, worldZ, requests);
            }
        }

        Color[] pixels = rangePixelScratch;
        float pixelWorldWidth = width / textureWidth;
        float pixelWorldHeight = height / textureHeight;
        float boundaryPixelOffset = Mathf.Min(pixelWorldWidth, pixelWorldHeight) * 0.5f;
        float fadeDistance = Mathf.Max(0.001f, maxRadius * (1f - RangeCenterTransparentRadius));
        FillInsideBoundaryDistances(
            inside,
            textureWidth,
            textureHeight,
            pixelWorldWidth,
            pixelWorldHeight,
            minX,
            maxX,
            minZ,
            maxZ,
            boundaryDistances);

        for (int i = 0; i < inside.Length; i++)
        {
            if (!inside[i])
            {
                pixels[i] = new Color(1f, 1f, 1f, 0f);
                continue;
            }

            float distanceToBoundary = Mathf.Max(0f, boundaryDistances[i] - boundaryPixelOffset);
            float edgeStrength = 1f - Mathf.Clamp01(distanceToBoundary / fadeDistance);
            float alpha = Mathf.SmoothStep(0f, 1f, edgeStrength);
            pixels[i] = new Color(1f, 1f, 1f, alpha);
        }

        rangeAlphaTexture.SetPixels(pixels);
        rangeAlphaTexture.Apply(false, false);
        return true;
    }

    private static int ComputeRangeRequestHash(IReadOnlyList<WorkableObjectRangeVisualRequest> requests)
    {
        if (requests == null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + requests.Count;
            for (int i = 0; i < requests.Count; i++)
            {
                WorkableObjectRangeVisualRequest request = requests[i];
                hash = (hash * 31) + QuantizeRangeHashValue(request.Center.x);
                hash = (hash * 31) + QuantizeRangeHashValue(request.Center.y);
                hash = (hash * 31) + QuantizeRangeHashValue(request.Center.z);
                hash = (hash * 31) + QuantizeRangeHashValue(request.Radius);
                hash = (hash * 31) + QuantizeRangeHashValue(request.YOffset);
            }

            return hash;
        }
    }

    private static int QuantizeRangeHashValue(float value)
    {
        return Mathf.RoundToInt(value * 1000f);
    }

    private void EnsureRangeAlphaScratch(int length)
    {
        if (rangeInsideScratch == null || rangeInsideScratch.Length != length)
        {
            rangeInsideScratch = new bool[length];
        }

        if (rangeBoundaryDistanceScratch == null || rangeBoundaryDistanceScratch.Length != length)
        {
            rangeBoundaryDistanceScratch = new float[length];
        }

        if (rangePixelScratch == null || rangePixelScratch.Length != length)
        {
            rangePixelScratch = new Color[length];
        }
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

    private static void FillInsideBoundaryDistances(
        bool[] inside,
        int width,
        int height,
        float pixelWorldWidth,
        float pixelWorldHeight,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float[] distances)
    {
        if (inside == null || distances == null || inside.Length != distances.Length)
        {
            return;
        }

        float horizontalCost = Mathf.Max(0.0001f, pixelWorldWidth);
        float verticalCost = Mathf.Max(0.0001f, pixelWorldHeight);
        float diagonalCost = Mathf.Sqrt((horizontalCost * horizontalCost) + (verticalCost * verticalCost));

        for (int y = 0; y < height; y++)
        {
            float worldZ = minZ + (((float)y + 0.5f) / height) * (maxZ - minZ);
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (!inside[index])
                {
                    distances[index] = 0f;
                    continue;
                }

                float worldX = minX + (((float)x + 0.5f) / width) * (maxX - minX);
                float distanceToBounds = Mathf.Min(
                    worldX - minX,
                    maxX - worldX,
                    worldZ - minZ,
                    maxZ - worldZ);
                distances[index] = Mathf.Max(0f, distanceToBounds);
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                RelaxDistance(distances, width, height, x, y, index, -1, 0, horizontalCost);
                RelaxDistance(distances, width, height, x, y, index, 0, -1, verticalCost);
                RelaxDistance(distances, width, height, x, y, index, -1, -1, diagonalCost);
                RelaxDistance(distances, width, height, x, y, index, 1, -1, diagonalCost);
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                int index = (y * width) + x;
                RelaxDistance(distances, width, height, x, y, index, 1, 0, horizontalCost);
                RelaxDistance(distances, width, height, x, y, index, 0, 1, verticalCost);
                RelaxDistance(distances, width, height, x, y, index, 1, 1, diagonalCost);
                RelaxDistance(distances, width, height, x, y, index, -1, 1, diagonalCost);
            }
        }
    }

    private static void RelaxDistance(
        float[] distances,
        int width,
        int height,
        int x,
        int y,
        int index,
        int offsetX,
        int offsetY,
        float cost)
    {
        int neighborX = x + offsetX;
        int neighborY = y + offsetY;
        if (neighborX < 0 || neighborX >= width || neighborY < 0 || neighborY >= height)
        {
            return;
        }

        int neighborIndex = (neighborY * width) + neighborX;
        float nextDistance = distances[neighborIndex] + cost;
        if (nextDistance < distances[index])
        {
            distances[index] = nextDistance;
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
            sharedRangeMaterial.SetColor(
                BaseColorShaderId,
                ResolveDisplayFillColor(RangeFillColor, ResolveCurrentDaylightFactor()));
        }

        if (sharedRangeMaterial.HasProperty(BaseMapShaderId))
        {
            sharedRangeMaterial.SetTexture(BaseMapShaderId, Texture2D.whiteTexture);
        }

        if (sharedRangeMaterial.HasProperty(ColorShaderId))
        {
            sharedRangeMaterial.SetColor(
                ColorShaderId,
                ResolveDisplayFillColor(RangeFillColor, ResolveCurrentDaylightFactor()));
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

    private static float ResolveCurrentDaylightFactor()
    {
        return WorldTimeService.Active != null
            ? WorldTimeService.Active.DaylightFactor
            : 1f;
    }

    private static Color ResolveDisplayFillColor(Color fillColor, float daylightFactor)
    {
        fillColor.a = Mathf.Clamp01(fillColor.a * RangeAlphaMultiplier);
        float timeOfDayAlphaMultiplier = Mathf.Lerp(
            NightRangeAlphaMultiplier,
            1f,
            Mathf.Clamp01(daylightFactor));
        fillColor.a *= timeOfDayAlphaMultiplier;

        return fillColor;
    }
}
