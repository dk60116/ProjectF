using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MapPaper : MonoBehaviour
{
    private readonly struct MapMarkerStampKey : IEquatable<MapMarkerStampKey>
    {
        public MapMarkerStampKey(
            Vector2Int coordinate,
            TerrainGenerator.MapMarkerLayer layer)
        {
            this.coordinate = coordinate;
            this.layer = layer;
        }

        private readonly Vector2Int coordinate;
        private readonly TerrainGenerator.MapMarkerLayer layer;

        public bool Equals(MapMarkerStampKey other)
        {
            return coordinate == other.coordinate && layer == other.layer;
        }

        public override bool Equals(object obj)
        {
            return obj is MapMarkerStampKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (coordinate.GetHashCode() * 397) ^ (int)layer;
            }
        }
    }

    private const int MinimumTexturePadding = 2;

    [SerializeField]
    private Image targetImage;

    [SerializeField]
    private RawImage targetRawImage;

    [SerializeField]
    private Vector2Int viewRadius = new Vector2Int(100, 100);

    [SerializeField, Min(MinimumTexturePadding)]
    private int texturePadding = 2;

    [SerializeField, ColorUsage(false)]
    private Color playerMarkerColor = new Color(1f, 0.82f, 0.12f, 1f);

    [SerializeField, Min(2f)]
    private float playerMarkerSize = 8f;

    [SerializeField, ColorUsage(false)]
    private Color cameraViewportColor = Color.white;

    [SerializeField, Min(0.5f)]
    private float cameraViewportLineWidth = 2f;

    private TerrainGenerator boundTerrain;
    private Transform trackedTarget;
    private Camera trackedCamera;
    private Texture2D mapTexture;
    private Image playerMarker;
    private RectTransform cameraViewportMarker;
    private readonly RectTransform[] cameraViewportEdges = new RectTransform[4];
    private readonly Vector2[] cameraViewportPoints = new Vector2[4];
    private float mapOrientationScale = 1f;
    private Color32[] pixelBuffer;
    private Color32[] biomePixelBuffer;
    private Color32[] composedPixelBuffer;
    private int[] markerDistanceBuffer;
    private byte[] markerLayerBuffer;
    private Color32[] smallResourceColorBuffer;
    private byte[] smallResourceSourceBuffer;
    private readonly List<TerrainGenerator.MapMarkerSample> mapMarkerSampleScratch =
        new List<TerrainGenerator.MapMarkerSample>(4);
    private readonly HashSet<MapMarkerStampKey> stampedMarkerKeys =
        new HashSet<MapMarkerStampKey>();
    private float nextMarkerRefreshTime;
    private const float MarkerRefreshInterval = 0.5f;
    private RectMask2D hostMask;
    private Color originalTargetImageColor = Color.white;
    private Vector2Int lastCenterCoordinate;
    private Vector2Int lastTextureSize;
    private bool hasLastCenterCoordinate;
    private bool isDirty = true;
    private bool hasStoredOriginalImageColor;
    private int lastTerrainGenerationVersion = int.MinValue;
    private Rect lastUvRect;
    private bool hasLastUvRect;

    private void Awake()
    {
        texturePadding = Mathf.Max(MinimumTexturePadding, texturePadding);
        ResolveTargetGraphics();
    }

    private void OnEnable()
    {
        texturePadding = Mathf.Max(MinimumTexturePadding, texturePadding);
        ResolveTargetGraphics();
        isDirty = true;
        hasLastUvRect = false;
    }

    private void OnValidate()
    {
        viewRadius.x = Mathf.Max(1, viewRadius.x);
        viewRadius.y = Mathf.Max(1, viewRadius.y);
        texturePadding = Mathf.Max(MinimumTexturePadding, texturePadding);
        playerMarkerSize = Mathf.Max(2f, playerMarkerSize);
        cameraViewportLineWidth = Mathf.Max(0.5f, cameraViewportLineWidth);
        ResolveTargetGraphics();
        isDirty = true;
    }

    private void Update()
    {
        if (targetImage == null || targetRawImage == null || hostMask == null)
        {
            ResolveTargetGraphics();
        }

        ResolveRuntimeReferences();

        if (targetImage == null || targetRawImage == null || boundTerrain == null || trackedTarget == null)
        {
            SetCameraViewportMarkerVisible(false);
            return;
        }

        Vector2 trackedPosition = new Vector2(trackedTarget.position.x, trackedTarget.position.z);
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.FloorToInt(trackedPosition.x),
            Mathf.FloorToInt(trackedPosition.y));

        bool sizeChanged = EnsureTexture();
        int terrainGenerationVersion = boundTerrain.TerrainGenerationVersion;
        bool terrainChanged = terrainGenerationVersion != lastTerrainGenerationVersion;
        bool movedOutsideBufferedTexture = !IsInsideBufferedTexture(trackedPosition);
        if (isDirty || sizeChanged || terrainChanged || movedOutsideBufferedTexture)
        {
            Redraw(centerCoordinate);
            lastCenterCoordinate = centerCoordinate;
            hasLastCenterCoordinate = true;
            isDirty = false;
            lastTerrainGenerationVersion = terrainGenerationVersion;
            hasLastUvRect = false;
        }
        else if (Time.unscaledTime >= nextMarkerRefreshTime)
        {
            RefreshMapMarkers(lastCenterCoordinate);
        }

        UpdateViewport(lastCenterCoordinate, trackedPosition);
        UpdateCameraViewportMarker(trackedPosition);
    }

    private void OnDestroy()
    {
        ReleaseGeneratedResources();
    }

    public void Bind(TerrainGenerator terrain, Transform target)
    {
        if (boundTerrain == terrain && trackedTarget == target)
        {
            return;
        }

        boundTerrain = terrain;
        trackedTarget = target;
        isDirty = true;
    }

    public void SetViewRadius(Vector2Int radius)
    {
        Vector2Int clampedRadius = new Vector2Int(
            Mathf.Max(1, radius.x),
            Mathf.Max(1, radius.y));
        if (viewRadius == clampedRadius)
        {
            return;
        }

        viewRadius = clampedRadius;
        isDirty = true;
        hasLastCenterCoordinate = false;
        hasLastUvRect = false;
    }

    public Vector2Int ViewRadius => viewRadius;

    private void ResolveTargetGraphics()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        if (targetImage != null && !hasStoredOriginalImageColor)
        {
            originalTargetImageColor = targetImage.color;
            hasStoredOriginalImageColor = true;
        }

        if (hostMask == null)
        {
            hostMask = GetComponent<RectMask2D>();
            if (hostMask == null)
            {
                hostMask = gameObject.AddComponent<RectMask2D>();
            }
        }

        if (targetRawImage == null)
        {
            Transform existingChild = transform.Find("MapTexture");
            if (existingChild != null)
            {
                targetRawImage = existingChild.GetComponent<RawImage>();
            }
        }

        if (targetRawImage == null)
        {
            GameObject rawImageObject = new GameObject("MapTexture", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawImageObject.layer = gameObject.layer;
            RectTransform rectTransform = rawImageObject.GetComponent<RectTransform>();
            rectTransform.SetParent(transform, false);
            rectTransform.SetAsLastSibling();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;
            targetRawImage = rawImageObject.GetComponent<RawImage>();
        }

        if (targetRawImage != null)
        {
            RectTransform textureRect = targetRawImage.rectTransform;
            textureRect.anchorMin = Vector2.zero;
            textureRect.anchorMax = Vector2.one;
            textureRect.pivot = new Vector2(0.5f, 0.5f);
            textureRect.offsetMin = Vector2.zero;
            textureRect.offsetMax = Vector2.zero;
            targetRawImage.raycastTarget = false;
            targetRawImage.color = Color.white;
            targetRawImage.transform.SetAsLastSibling();
        }

        if (targetImage != null)
        {
            Color hiddenColor = originalTargetImageColor;
            hiddenColor.a = 0f;
            targetImage.color = hiddenColor;
        }

        ResolveCameraViewportMarker();
        ResolvePlayerMarker();
    }

    private void ResolveCameraViewportMarker()
    {
        if (cameraViewportMarker == null)
        {
            Transform existingMarker = transform.Find("CameraViewportMarker");
            if (existingMarker != null)
            {
                cameraViewportMarker = existingMarker as RectTransform;
            }
        }

        if (cameraViewportMarker == null)
        {
            GameObject markerObject = new GameObject("CameraViewportMarker", typeof(RectTransform));
            markerObject.layer = gameObject.layer;
            markerObject.transform.SetParent(transform, false);
            cameraViewportMarker = markerObject.GetComponent<RectTransform>();
        }

        cameraViewportMarker.anchorMin = Vector2.zero;
        cameraViewportMarker.anchorMax = Vector2.one;
        cameraViewportMarker.pivot = new Vector2(0.5f, 0.5f);
        cameraViewportMarker.offsetMin = Vector2.zero;
        cameraViewportMarker.offsetMax = Vector2.zero;
        cameraViewportMarker.localScale = Vector3.one;

        for (int i = 0; i < cameraViewportEdges.Length; i++)
        {
            RectTransform edge = cameraViewportEdges[i];
            if (edge == null)
            {
                Transform existingEdge = cameraViewportMarker.Find($"Edge{i}");
                if (existingEdge != null)
                {
                    edge = existingEdge as RectTransform;
                }
            }

            if (edge == null)
            {
                GameObject edgeObject = new GameObject(
                    $"Edge{i}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                edgeObject.layer = gameObject.layer;
                edgeObject.transform.SetParent(cameraViewportMarker, false);
                edge = edgeObject.GetComponent<RectTransform>();
            }

            edge.anchorMin = new Vector2(0.5f, 0.5f);
            edge.anchorMax = new Vector2(0.5f, 0.5f);
            edge.pivot = new Vector2(0.5f, 0.5f);
            edge.localScale = Vector3.one;
            Image edgeImage = edge.GetComponent<Image>();
            edgeImage.color = cameraViewportColor;
            edgeImage.raycastTarget = false;
            cameraViewportEdges[i] = edge;
        }

        cameraViewportMarker.SetAsLastSibling();
    }

    private void ResolvePlayerMarker()
    {
        if (playerMarker == null)
        {
            Transform existingMarker = transform.Find("PlayerMarker");
            if (existingMarker != null)
            {
                playerMarker = existingMarker.GetComponent<Image>();
            }
        }

        if (playerMarker == null)
        {
            GameObject markerObject = new GameObject(
                "PlayerMarker",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            markerObject.layer = gameObject.layer;
            markerObject.transform.SetParent(transform, false);
            playerMarker = markerObject.GetComponent<Image>();
        }

        RectTransform markerRect = playerMarker.rectTransform;
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.anchoredPosition = Vector2.zero;
        markerRect.sizeDelta = Vector2.one * playerMarkerSize;
        markerRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        markerRect.localScale = Vector3.one;
        playerMarker.color = playerMarkerColor;
        playerMarker.raycastTarget = false;
        playerMarker.enabled = trackedTarget != null;
        playerMarker.transform.SetAsLastSibling();
    }

    private void UpdateCameraViewportMarker(Vector2 trackedPosition)
    {
        if (cameraViewportMarker == null)
        {
            ResolveCameraViewportMarker();
        }

        if (trackedCamera == null || !trackedCamera.isActiveAndEnabled)
        {
            trackedCamera = Camera.main;
        }

        if (cameraViewportMarker == null
            || trackedCamera == null
            || targetRawImage == null)
        {
            SetCameraViewportMarkerVisible(false);
            return;
        }

        UpdateMapOrientation();
        if (!TryResolveCameraViewportPoints(trackedPosition))
        {
            SetCameraViewportMarkerVisible(false);
            return;
        }

        SetCameraViewportMarkerVisible(true);
        for (int i = 0; i < cameraViewportEdges.Length; i++)
        {
            PositionViewportEdge(
                cameraViewportEdges[i],
                cameraViewportPoints[i],
                cameraViewportPoints[(i + 1) % cameraViewportPoints.Length]);
        }
    }

    private void UpdateMapOrientation()
    {
        RectTransform textureRect = targetRawImage.rectTransform;
        Rect mapRect = textureRect.rect;
        if (mapRect.width <= 0f || mapRect.height <= 0f)
        {
            return;
        }

        Vector3 cameraUp = trackedCamera.transform.up;
        Vector2 horizontalUp = new Vector2(cameraUp.x, cameraUp.z);
        float angle = horizontalUp.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(horizontalUp.x, horizontalUp.y) * Mathf.Rad2Deg
            : trackedCamera.transform.eulerAngles.y;
        float angleRadians = angle * Mathf.Deg2Rad;
        float absoluteCosine = Mathf.Abs(Mathf.Cos(angleRadians));
        float absoluteSine = Mathf.Abs(Mathf.Sin(angleRadians));
        float horizontalCoverage = absoluteCosine
                                   + absoluteSine * mapRect.height / mapRect.width;
        float verticalCoverage = absoluteCosine
                                 + absoluteSine * mapRect.width / mapRect.height;
        mapOrientationScale = Mathf.Max(1f, Mathf.Max(horizontalCoverage, verticalCoverage));

        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        Vector3 scale = Vector3.one * mapOrientationScale;
        textureRect.localRotation = rotation;
        textureRect.localScale = scale;
        cameraViewportMarker.localRotation = rotation;
        cameraViewportMarker.localScale = scale;
    }

    private bool TryResolveCameraViewportPoints(Vector2 trackedPosition)
    {
        float visibleWidth = (viewRadius.x * 2) + 1;
        float visibleHeight = (viewRadius.y * 2) + 1;
        Rect mapRect = targetRawImage.rectTransform.rect;
        if (visibleWidth <= 0f
            || visibleHeight <= 0f
            || mapRect.width <= 0f
            || mapRect.height <= 0f)
        {
            return false;
        }

        float groundHeight = trackedTarget.position.y;
        for (int i = 0; i < cameraViewportPoints.Length; i++)
        {
            Vector3 viewportPoint;
            switch (i)
            {
                case 0:
                    viewportPoint = Vector3.zero;
                    break;
                case 1:
                    viewportPoint = Vector3.right;
                    break;
                case 2:
                    viewportPoint = new Vector3(1f, 1f, 0f);
                    break;
                default:
                    viewportPoint = Vector3.up;
                    break;
            }

            Ray ray = trackedCamera.ViewportPointToRay(viewportPoint);
            if (Mathf.Abs(ray.direction.y) <= 0.0001f)
            {
                return false;
            }

            float distance = (groundHeight - ray.origin.y) / ray.direction.y;
            if (distance < 0f)
            {
                return false;
            }

            Vector3 worldPoint = ray.GetPoint(distance);
            cameraViewportPoints[i] = new Vector2(
                (worldPoint.x - trackedPosition.x) / visibleWidth * mapRect.width,
                (worldPoint.z - trackedPosition.y) / visibleHeight * mapRect.height);
        }

        return true;
    }

    private void PositionViewportEdge(RectTransform edge, Vector2 start, Vector2 end)
    {
        if (edge == null)
        {
            return;
        }

        Vector2 delta = end - start;
        edge.anchoredPosition = (start + end) * 0.5f;
        edge.sizeDelta = new Vector2(
            delta.magnitude,
            cameraViewportLineWidth / mapOrientationScale);
        edge.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void SetCameraViewportMarkerVisible(bool visible)
    {
        if (cameraViewportMarker != null && cameraViewportMarker.gameObject.activeSelf != visible)
        {
            cameraViewportMarker.gameObject.SetActive(visible);
        }
    }

    private void ResolveRuntimeReferences()
    {
        if (boundTerrain == null)
        {
            boundTerrain = TerrainGenerator.ResolveActive();
            if (boundTerrain != null)
            {
                isDirty = true;
            }
        }

        if (trackedTarget == null && GameManager.Instance != null && GameManager.Instance.Player != null)
        {
            trackedTarget = GameManager.Instance.Player.transform;
            isDirty = true;
        }

        if (playerMarker != null)
        {
            playerMarker.enabled = trackedTarget != null;
        }
    }

    private bool EnsureTexture()
    {
        Vector2Int textureSize = new Vector2Int(
            (viewRadius.x * 2) + 1 + (texturePadding * 2),
            (viewRadius.y * 2) + 1 + (texturePadding * 2));
        int pixelCount = textureSize.x * textureSize.y;
        if (mapTexture != null
            && lastTextureSize == textureSize
            && pixelBuffer != null
            && pixelBuffer.Length == pixelCount
            && biomePixelBuffer != null
            && composedPixelBuffer != null
            && markerDistanceBuffer != null
            && markerLayerBuffer != null
            && smallResourceColorBuffer != null
            && smallResourceSourceBuffer != null)
        {
            return false;
        }

        ReleaseGeneratedResources(false);

        mapTexture = new Texture2D(textureSize.x, textureSize.y, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "MapPaperTexture"
        };
        pixelBuffer = new Color32[pixelCount];
        biomePixelBuffer = new Color32[pixelBuffer.Length];
        composedPixelBuffer = new Color32[pixelBuffer.Length];
        markerDistanceBuffer = new int[pixelBuffer.Length];
        markerLayerBuffer = new byte[pixelBuffer.Length];
        smallResourceColorBuffer = new Color32[pixelBuffer.Length];
        smallResourceSourceBuffer = new byte[pixelBuffer.Length];
        lastTextureSize = textureSize;

        if (targetRawImage != null)
        {
            targetRawImage.texture = mapTexture;
            targetRawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            hasLastUvRect = false;
        }

        return true;
    }

    private bool IsInsideBufferedTexture(Vector2 trackedPosition)
    {
        Vector2 offset = trackedPosition - new Vector2(lastCenterCoordinate.x, lastCenterCoordinate.y);
        return hasLastCenterCoordinate
               && Mathf.Abs(offset.x) <= texturePadding
               && Mathf.Abs(offset.y) <= texturePadding;
    }

    private void Redraw(Vector2Int centerCoordinate)
    {
        if (mapTexture == null || pixelBuffer == null || boundTerrain == null)
        {
            return;
        }

        int width = lastTextureSize.x;
        int height = lastTextureSize.y;
        int minX = centerCoordinate.x - viewRadius.x - texturePadding;
        int minY = centerCoordinate.y - viewRadius.y - texturePadding;

        int index = 0;
        for (int y = 0; y < height; y++)
        {
            int worldY = minY + y;
            for (int x = 0; x < width; x++)
            {
                int worldX = minX + x;
                biomePixelBuffer[index++] = boundTerrain.GetMapBiomeColor32At(new Vector2Int(worldX, worldY));
            }
        }

        RefreshMapMarkers(centerCoordinate, true);
    }

    private void RefreshMapMarkers(Vector2Int centerCoordinate, bool forceUpload = false)
    {
        int width = lastTextureSize.x;
        int height = lastTextureSize.y;
        int minX = centerCoordinate.x - viewRadius.x - texturePadding;
        int minY = centerCoordinate.y - viewRadius.y - texturePadding;
        Array.Copy(biomePixelBuffer, composedPixelBuffer, biomePixelBuffer.Length);
        Array.Fill(markerDistanceBuffer, int.MaxValue);
        Array.Clear(markerLayerBuffer, 0, markerLayerBuffer.Length);
        Array.Clear(smallResourceSourceBuffer, 0, smallResourceSourceBuffer.Length);
        stampedMarkerKeys.Clear();

        int index = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++, index++)
            {
                mapMarkerSampleScratch.Clear();
                boundTerrain.CollectMapMarkerSamplesAt(
                    new Vector2Int(minX + x, minY + y),
                    mapMarkerSampleScratch);
                for (int markerIndex = 0; markerIndex < mapMarkerSampleScratch.Count; markerIndex++)
                {
                    TerrainGenerator.MapMarkerSample marker = mapMarkerSampleScratch[markerIndex];
                    if (marker.layer == TerrainGenerator.MapMarkerLayer.Resource
                        && marker.size == MapMarkerSize.Small)
                    {
                        smallResourceSourceBuffer[index] = 1;
                        smallResourceColorBuffer[index] = marker.color;
                        continue;
                    }

                    if (stampedMarkerKeys.Add(new MapMarkerStampKey(
                            marker.footprintMinimum,
                            marker.layer)))
                    {
                        StampMapMarker(marker, minX, minY, width, height);
                    }
                }
            }
        }

        mapMarkerSampleScratch.Clear();
        boundTerrain.CollectLiveTrainMapMarkers(mapMarkerSampleScratch);
        for (int markerIndex = 0; markerIndex < mapMarkerSampleScratch.Count; markerIndex++)
        {
            TerrainGenerator.MapMarkerSample marker = mapMarkerSampleScratch[markerIndex];
            if (stampedMarkerKeys.Add(new MapMarkerStampKey(
                    marker.footprintMinimum,
                    marker.layer)))
            {
                StampMapMarker(marker, minX, minY, width, height);
            }
        }

        StampSmallResourceMarkers(minX, minY, width, height);

        bool changed = forceUpload;
        for (int i = 0; i < pixelBuffer.Length; i++)
        {
            Color32 color = composedPixelBuffer[i];
            Color32 previous = pixelBuffer[i];
            changed |= previous.r != color.r || previous.g != color.g
                       || previous.b != color.b || previous.a != color.a;
            pixelBuffer[i] = color;
        }

        if (changed)
        {
            mapTexture.SetPixels32(pixelBuffer);
            mapTexture.Apply(false, false);
        }

        nextMarkerRefreshTime = Time.unscaledTime + MarkerRefreshInterval;
    }

    private void StampSmallResourceMarkers(
        int textureMinimumX,
        int textureMinimumY,
        int width,
        int height)
    {
        int index = 0;
        for (int y = 0; y < height; y++)
        {
            int worldY = textureMinimumY + y;
            for (int x = 0; x < width; x++, index++)
            {
                if (smallResourceSourceBuffer[index] == 0)
                {
                    continue;
                }

                int worldX = textureMinimumX + x;
                bool selectedBySparsePattern = ((worldX + worldY) & 1) == 0;
                if (!selectedBySparsePattern && HasAdjacentSmallResource(x, y, width, height))
                {
                    continue;
                }

                byte resourceLayer = (byte)TerrainGenerator.MapMarkerLayer.Resource;
                if (markerLayerBuffer[index] < resourceLayer
                    || markerLayerBuffer[index] == resourceLayer
                    && markerDistanceBuffer[index] > 0)
                {
                    markerLayerBuffer[index] = resourceLayer;
                    markerDistanceBuffer[index] = 0;
                    composedPixelBuffer[index] = smallResourceColorBuffer[index];
                }
            }
        }
    }

    private bool HasAdjacentSmallResource(int x, int y, int width, int height)
    {
        int index = (y * width) + x;
        return (x > 0 && smallResourceSourceBuffer[index - 1] != 0)
               || (x + 1 < width && smallResourceSourceBuffer[index + 1] != 0)
               || (y > 0 && smallResourceSourceBuffer[index - width] != 0)
               || (y + 1 < height && smallResourceSourceBuffer[index + width] != 0);
    }

    private void StampMapMarker(
        TerrainGenerator.MapMarkerSample marker,
        int textureMinimumX,
        int textureMinimumY,
        int width,
        int height)
    {
        int sourceWidth = marker.footprintMaximum.x - marker.footprintMinimum.x + 1;
        int sourceHeight = marker.footprintMaximum.y - marker.footprintMinimum.y + 1;
        int targetWidth = GetScaledMarkerDimension(sourceWidth, marker.size);
        int targetHeight = GetScaledMarkerDimension(sourceHeight, marker.size);
        int worldMinimumX = GetCenteredMarkerMinimum(
            marker.footprintMinimum.x,
            sourceWidth,
            targetWidth);
        int worldMinimumY = GetCenteredMarkerMinimum(
            marker.footprintMinimum.y,
            sourceHeight,
            targetHeight);
        int minimumX = Mathf.Max(0, worldMinimumX - textureMinimumX);
        int maximumX = Mathf.Min(width - 1, worldMinimumX + targetWidth - 1 - textureMinimumX);
        int minimumY = Mathf.Max(0, worldMinimumY - textureMinimumY);
        int maximumY = Mathf.Min(height - 1, worldMinimumY + targetHeight - 1 - textureMinimumY);
        for (int y = minimumY; y <= maximumY; y++)
        {
            int rowStart = y * width;
            int worldY = textureMinimumY + y;
            int verticalDistance = DistanceOutsideRange(
                worldY,
                marker.footprintMinimum.y,
                marker.footprintMaximum.y);
            for (int x = minimumX; x <= maximumX; x++)
            {
                int worldX = textureMinimumX + x;
                int horizontalDistance = DistanceOutsideRange(
                    worldX,
                    marker.footprintMinimum.x,
                    marker.footprintMaximum.x);
                int distance = Mathf.Max(horizontalDistance, verticalDistance);
                int targetIndex = rowStart + x;
                byte markerLayer = (byte)marker.layer;
                byte existingLayer = markerLayerBuffer[targetIndex];
                if (markerLayer < existingLayer
                    || markerLayer == existingLayer
                    && distance >= markerDistanceBuffer[targetIndex])
                {
                    continue;
                }

                markerLayerBuffer[targetIndex] = markerLayer;
                markerDistanceBuffer[targetIndex] = distance;
                composedPixelBuffer[targetIndex] = marker.color;
            }
        }
    }

    private static int GetScaledMarkerDimension(int sourceDimension, MapMarkerSize markerSize)
    {
        switch (markerSize)
        {
            case MapMarkerSize.Small:
                return Mathf.Max(1, Mathf.RoundToInt(sourceDimension * 0.5f));
            case MapMarkerSize.Large:
                return Mathf.Max(sourceDimension, Mathf.CeilToInt(sourceDimension * 1.5f));
            default:
                return sourceDimension;
        }
    }

    private static int GetCenteredMarkerMinimum(
        int sourceMinimum,
        int sourceDimension,
        int targetDimension)
    {
        if (targetDimension < sourceDimension)
        {
            return sourceMinimum + ((sourceDimension - targetDimension) / 2);
        }

        return sourceMinimum - ((targetDimension - sourceDimension) / 2);
    }

    private static int DistanceOutsideRange(int value, int minimum, int maximum)
    {
        if (value < minimum)
        {
            return minimum - value;
        }

        return value > maximum ? value - maximum : 0;
    }

    private void UpdateViewport(Vector2Int drawnCenterCoordinate, Vector2 trackedPosition)
    {
        if (targetRawImage == null || lastTextureSize.x <= 0 || lastTextureSize.y <= 0)
        {
            return;
        }

        float visibleWidth = (viewRadius.x * 2) + 1;
        float visibleHeight = (viewRadius.y * 2) + 1;
        float fullWidth = lastTextureSize.x;
        float fullHeight = lastTextureSize.y;

        float offsetX = trackedPosition.x - drawnCenterCoordinate.x;
        float offsetY = trackedPosition.y - drawnCenterCoordinate.y;

        Rect uvRect = new Rect(
            (texturePadding + offsetX) / fullWidth,
            (texturePadding + offsetY) / fullHeight,
            visibleWidth / fullWidth,
            visibleHeight / fullHeight);
        if (hasLastUvRect && Approximately(lastUvRect, uvRect))
        {
            return;
        }

        targetRawImage.uvRect = uvRect;
        lastUvRect = uvRect;
        hasLastUvRect = true;
    }

    private static bool Approximately(Rect first, Rect second)
    {
        return Mathf.Approximately(first.x, second.x)
               && Mathf.Approximately(first.y, second.y)
               && Mathf.Approximately(first.width, second.width)
               && Mathf.Approximately(first.height, second.height);
    }

    private void ReleaseGeneratedResources(bool restoreTargetImage = true)
    {
        if (mapTexture != null)
        {
            if (Application.isPlaying)
            {
                Destroy(mapTexture);
            }
            else
            {
                DestroyImmediate(mapTexture);
            }

            mapTexture = null;
        }

        if (targetRawImage != null)
        {
            targetRawImage.texture = null;
            targetRawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        if (restoreTargetImage && targetImage != null && hasStoredOriginalImageColor)
        {
            targetImage.color = originalTargetImageColor;
        }

        pixelBuffer = null;
        biomePixelBuffer = null;
        composedPixelBuffer = null;
        markerDistanceBuffer = null;
        markerLayerBuffer = null;
        smallResourceColorBuffer = null;
        smallResourceSourceBuffer = null;
        mapMarkerSampleScratch.Clear();
        stampedMarkerKeys.Clear();
        lastTextureSize = Vector2Int.zero;
        hasLastUvRect = false;
    }
}
