using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MapPaper : MonoBehaviour
{
    [SerializeField]
    private Image targetImage;

    [SerializeField]
    private RawImage targetRawImage;

    [SerializeField]
    private Vector2Int viewRadius = new Vector2Int(100, 100);

    [SerializeField, Min(0.02f)]
    private float refreshInterval = 0.05f;

    [SerializeField, Min(1)]
    private int texturePadding = 2;

    private TerrainGenerator boundTerrain;
    private Transform trackedTarget;
    private Texture2D mapTexture;
    private Color32[] pixelBuffer;
    private RectMask2D hostMask;
    private Color originalTargetImageColor = Color.white;
    private Vector2Int lastCenterCoordinate;
    private Vector2Int lastTextureSize;
    private bool hasLastCenterCoordinate;
    private bool isDirty = true;
    private float refreshTimer;
    private bool hasStoredOriginalImageColor;

    private void Awake()
    {
        ResolveTargetGraphics();
    }

    private void OnEnable()
    {
        isDirty = true;
        refreshTimer = 0f;
    }

    private void OnValidate()
    {
        viewRadius.x = Mathf.Max(1, viewRadius.x);
        viewRadius.y = Mathf.Max(1, viewRadius.y);
        refreshInterval = Mathf.Max(0.02f, refreshInterval);
        texturePadding = Mathf.Max(1, texturePadding);
        ResolveTargetGraphics();
        isDirty = true;
    }

    private void Update()
    {
        ResolveTargetGraphics();
        ResolveRuntimeReferences();

        if (targetImage == null || targetRawImage == null || boundTerrain == null || trackedTarget == null)
        {
            return;
        }

        Vector2 trackedPosition = new Vector2(trackedTarget.position.x, trackedTarget.position.z);
        Vector2Int centerCoordinate = new Vector2Int(
            Mathf.FloorToInt(trackedPosition.x),
            Mathf.FloorToInt(trackedPosition.y));

        refreshTimer -= Time.unscaledDeltaTime;
        bool sizeChanged = EnsureTexture();
        bool movedToNewTile = !hasLastCenterCoordinate || centerCoordinate != lastCenterCoordinate;
        bool needsPeriodicRefresh = refreshTimer <= 0f;
        if (!isDirty && !sizeChanged && !movedToNewTile && !needsPeriodicRefresh)
        {
            UpdateViewport(centerCoordinate, trackedPosition);
            return;
        }

        Redraw(centerCoordinate);
        UpdateViewport(centerCoordinate, trackedPosition);
        lastCenterCoordinate = centerCoordinate;
        hasLastCenterCoordinate = true;
        isDirty = false;
        refreshTimer = refreshInterval;
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
        refreshTimer = 0f;
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
        refreshTimer = 0f;
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
    }

    private void ResolveRuntimeReferences()
    {
        if (boundTerrain == null)
        {
            boundTerrain = UnityEngine.Object.FindObjectOfType<TerrainGenerator>();
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
    }

    private bool EnsureTexture()
    {
        Vector2Int textureSize = new Vector2Int(
            (viewRadius.x * 2) + 1 + (texturePadding * 2),
            (viewRadius.y * 2) + 1 + (texturePadding * 2));
        if (mapTexture != null && lastTextureSize == textureSize && pixelBuffer != null && pixelBuffer.Length == textureSize.x * textureSize.y)
        {
            return false;
        }

        ReleaseGeneratedResources();

        mapTexture = new Texture2D(textureSize.x, textureSize.y, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "MapPaperTexture"
        };
        pixelBuffer = new Color32[textureSize.x * textureSize.y];
        lastTextureSize = textureSize;

        if (targetRawImage != null)
        {
            targetRawImage.texture = mapTexture;
            targetRawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        return true;
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
                pixelBuffer[index++] = boundTerrain.GetMapBiomeColor32At(new Vector2Int(worldX, worldY));
            }
        }

        mapTexture.SetPixels32(pixelBuffer);
        mapTexture.Apply(false, false);
    }

    private void UpdateViewport(Vector2Int centerCoordinate, Vector2 trackedPosition)
    {
        if (targetRawImage == null || lastTextureSize.x <= 0 || lastTextureSize.y <= 0)
        {
            return;
        }

        float visibleWidth = (viewRadius.x * 2) + 1;
        float visibleHeight = (viewRadius.y * 2) + 1;
        float fullWidth = lastTextureSize.x;
        float fullHeight = lastTextureSize.y;

        float fractionalX = trackedPosition.x - centerCoordinate.x;
        float fractionalY = trackedPosition.y - centerCoordinate.y;

        Rect uvRect = new Rect(
            (texturePadding + fractionalX) / fullWidth,
            (texturePadding + fractionalY) / fullHeight,
            visibleWidth / fullWidth,
            visibleHeight / fullHeight);
        targetRawImage.uvRect = uvRect;
    }

    private void ReleaseGeneratedResources()
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

        if (targetImage != null && hasStoredOriginalImageColor)
        {
            targetImage.color = originalTargetImageColor;
        }

        pixelBuffer = null;
        lastTextureSize = Vector2Int.zero;
    }
}
