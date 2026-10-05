using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using ProjectF.MapObjects;

public class WorkableObject : InstallationObject, IWorkableTarget
{
    internal static readonly WorkableRangeIndex RangeIndex = new WorkableRangeIndex();
    private static readonly HashSet<WorkableObject> NativeInstances = new HashSet<WorkableObject>();
    private static readonly HashSet<IWorkableTarget> SelectedRangeVisualInstances = new HashSet<IWorkableTarget>();
    private static readonly List<WorkableObjectRangeVisualRequest> RangeVisualRequests = new List<WorkableObjectRangeVisualRequest>();
    private static readonly HashSet<IWorkableTarget> AppendedTargets = new HashSet<IWorkableTarget>();
    private static readonly List<IWorkableTarget> ConnectedScratch = new List<IWorkableTarget>();
    private static BagSlot craftingSlotRangeVisualRequestSource;
    private static WorkableObjectRangeVisual sharedRangeVisual;
    private static bool installOrEditWorkableSelectionRangeVisualsRequested;
    private static bool rangeVisualDirty = true;
    [SerializeField, FormerlySerializedAs("focusActivationRadius")]
    private uint workableRangeCells = 1u;
    [SerializeField] private bool showWorkableRange = true;
    [SerializeField, Min(0f)] private float rangeVisualYOffset = 0.04f;
    private bool globalRangeVisualSuppressed;
    private bool legacyRangeVisualsScanned;
    public uint WorkableRangeCells => workableRangeCells;
    public override float FocusActivationRadius => ResolveRangeRadius(workableRangeCells);
    public static float ResolveRangeRadius(uint cells) => Mathf.Max(0f, cells * 0.5f);
    public long WorkablePlacementSequence => RuntimePlacementSequence;
    public Vector2Int AnchorCoordinate => TryGetPlacementRuntime(out var coordinate, out _) ? coordinate : default;
    public bool ShowWorkableRange => showWorkableRange;
    public bool GlobalRangeVisualSuppressed => globalRangeVisualSuppressed;
    public float RangeVisualYOffset => rangeVisualYOffset;
    public static void CollectActiveContainingWorldPosition(Vector3 position, List<IWorkableTarget> result) => RangeIndex.CollectContaining(position, result);
    public static void CollectOwnContainingWorldPosition(Vector3 position, List<IWorkableTarget> result) => RangeIndex.CollectOwn(position, result);
    public bool ContainsWorldPositionInWorkableRange(Vector3 position) => ContainsWorldPositionInOwnWorkableRange(position);
    public bool ContainsWorldPositionInOwnWorkableRange(Vector3 position) => IsTargetActive && TryGetWorkableRangeBounds(out var bounds) && WorkableRangeIndex.Contains(bounds, position);
    public bool ContainsWorldPositionInConnectedWorkableRange(Vector3 position) => RangeIndex.ContainsConnected(this, position);
    public bool TryGetWorkableRangeBounds(out Bounds bounds)
    {
        float radius = FocusActivationRadius;
        Vector3 center = transform.position;
        var occupied = RuntimeOccupiedCoordinates;
        if (occupied != null && occupied.Count > 0)
        {
            double x = 0, z = 0;
            for (int i = 0; i < occupied.Count; i++) { x += occupied[i].x; z += occupied[i].y; }
            center = new Vector3((float)(x / occupied.Count), center.y, (float)(z / occupied.Count));
        }
        bounds = new Bounds(center, new Vector3(radius * 2f, 0.01f, radius * 2f));
        return radius > 0f;
    }
    internal static void RegisterTarget(IWorkableTarget target)
    {
        // Blueprint components are disabled while their scene objects remain visible.
        if (target is WorkableObject native && !native.isActiveAndEnabled) RangeIndex.Remove(target);
        else RangeIndex.Update(target);
        rangeVisualDirty = true;
    }
    internal static void UnregisterTarget(IWorkableTarget target)
    { RangeIndex.Remove(target); SelectedRangeVisualInstances.Remove(target); rangeVisualDirty = true; }
    public void SetSelectedRangeVisualRequested(bool requested) => SetTargetSelected(this, requested);
    internal static void SetTargetSelected(IWorkableTarget target, bool requested)
    { rangeVisualDirty |= requested ? SelectedRangeVisualInstances.Add(target) : SelectedRangeVisualInstances.Remove(target); }
    public void SetGlobalRangeVisualSuppressed(bool suppressed)
    { if (globalRangeVisualSuppressed == suppressed) return; globalRangeVisualSuppressed = suppressed; rangeVisualDirty = true; }
    public static void SetCraftingSlotRangeVisualsRequested(BagSlot source, bool requested)
    {
        if (requested)
        { if (source == null || craftingSlotRangeVisualRequestSource == source) return; craftingSlotRangeVisualRequestSource = source; }
        else
        { if (craftingSlotRangeVisualRequestSource != null && craftingSlotRangeVisualRequestSource != source) return; craftingSlotRangeVisualRequestSource = null; }
        rangeVisualDirty = true;
    }
    public static void SetInstallOrEditWorkableSelectionRangeVisualsRequested(bool requested)
    { if (installOrEditWorkableSelectionRangeVisualsRequested == requested) return; installOrEditWorkableSelectionRangeVisualsRequested = requested; rangeVisualDirty = true; }
    public static void RefreshAllRangeVisuals()
    { foreach (var native in NativeInstances) if (native != null) RangeIndex.Update(native); rangeVisualDirty = true; }
    protected override void OnEnable()
    {
        base.OnEnable(); NativeInstances.Add(this); RegisterTarget(this);
        DisableLegacyRangeVisual(); EnsureRangeVisualHost();
    }
    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;
        NativeInstances.Remove(this); UnregisterTarget(this); base.OnDisable();
    }
    protected override void OnPlacementRuntimeChanged() { base.OnPlacementRuntimeChanged(); RegisterTarget(this); }
    protected override void OnPlacementRuntimeCleared() { base.OnPlacementRuntimeCleared(); RegisterTarget(this); }
    public override void PrepareForPool() { base.PrepareForPool(); UnregisterTarget(this); }
#if UNITY_EDITOR
    protected override void OnValidate() { base.OnValidate(); if (Application.isPlaying) RefreshAllRangeVisuals(); }
#endif
    internal static void EnsureRangeVisualHost()
    {
        if (!Application.isPlaying || sharedRangeVisual != null) return;
        var host = new GameObject("Workable Range Visuals");
        sharedRangeVisual = host.AddComponent<WorkableObjectRangeVisual>();
        host.AddComponent<WorkableRangeVisualUpdater>();
    }
    internal static void UpdateRangeVisual()
    {
        if (!rangeVisualDirty || !Application.isPlaying) return;
        // Wait until all bulk placements/selection changes have committed.
        if (TerrainGenerator.Active != null && TerrainGenerator.Active.IsBenchmarkPlacementInProgress) return;
        rangeVisualDirty = false; RangeVisualRequests.Clear(); AppendedTargets.Clear();
        if (ShouldShowWorkableRangeVisuals())
        {
            var targets = RangeIndex.Targets;
            for (int i = 0; i < targets.Count; i++) AppendRange(targets[i], false);
        }
        foreach (var target in SelectedRangeVisualInstances)
        {
            if (AppendedTargets.Contains(target)) continue;
            // A disabled blueprint is not indexed, but its own range still needs a preview.
            AppendRange(target, true);
            RangeIndex.CollectConnected(target, ConnectedScratch);
            for (int i = 0; i < ConnectedScratch.Count; i++) AppendRange(ConnectedScratch[i], true);
        }
        ConnectedScratch.Clear();
        if (RangeVisualRequests.Count == 0) { if (sharedRangeVisual != null) sharedRangeVisual.SetVisible(false); return; }
        EnsureRangeVisualHost(); sharedRangeVisual.Configure(RangeVisualRequests); sharedRangeVisual.SetVisible(true);
    }
    private static void AppendRange(IWorkableTarget target, bool selected)
    {
        if (!target.IsTargetActive || !target.ShowWorkableRange || !selected && target.GlobalRangeVisualSuppressed
            || !target.TryGetWorkableRangeBounds(out var bounds) || !AppendedTargets.Add(target)) return;
        RangeVisualRequests.Add(new WorkableObjectRangeVisualRequest(bounds.center, bounds.extents.x, target.RangeVisualYOffset));
    }
    private void DisableLegacyRangeVisual()
    {
        if (legacyRangeVisualsScanned) return; legacyRangeVisualsScanned = true;
        var visuals = GetComponentsInChildren<WorkableObjectRangeVisual>(true);
        for (int i = 0; i < visuals.Length; i++) if (visuals[i] != null && visuals[i] != sharedRangeVisual) visuals[i].gameObject.SetActive(false);
    }
    private static bool ShouldShowWorkableRangeVisuals()
    {
        if (craftingSlotRangeVisualRequestSource != null && craftingSlotRangeVisualRequestSource.IsCraftingExpanded) return true;
        craftingSlotRangeVisualRequestSource = null;
        return installOrEditWorkableSelectionRangeVisualsRequested && GameManager.Instance != null
            && (GameManager.Instance.InstallationPlacementActive || GameManager.Instance.MapEditActive);
    }
}

// Remains enabled while the renderer is hidden so deferred selection changes can reveal it.
public sealed class WorkableRangeVisualUpdater : MonoBehaviour
{
    private void LateUpdate() => WorkableObject.UpdateRangeVisual();
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

    public void SetVisible(bool visible) { if (meshRenderer != null) meshRenderer.enabled = visible; }

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
