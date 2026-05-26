using UnityEngine;

public class MapFocus : MonoBehaviour
{
    private const string OverlayShaderName = "Custom/MapFocusOverlay";
    private const int AreaLineCount = 4;
    private const float SingleMarkerCornerVisibleLengthRatio = 131f / 512f;
    public static readonly Color DefaultFocusColor = new Color(1f, 0.86f, 0f, 0.45f);
    public static readonly Color MouseFocusColor = new Color(1f, 1f, 1f, 0.45f);

    [SerializeField]
    private SpriteRenderer render;
    [SerializeField]
    private Color focusColor = new Color(1f, 0.86f, 0f, 0.45f);
    [SerializeField, Min(0.01f)]
    private float areaCornerLength = 0.32f;
    [SerializeField, Min(0.001f)]
    private float areaLineWidth = 0.035f;

    private static Material overlayMaterial;
    private static Material lineMaterial;
    private bool hasDefaultTransform;
    private Vector3 defaultLocalPosition;
    private Quaternion defaultLocalRotation;
    private Vector3 defaultLocalScale;
    private LineRenderer[] areaLines;

    private void Awake()
    {
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        CacheDefaultTransform();
        ApplyOverlayRendering();
    }

    public void SetVisible(bool isVisible)
    {
        SetVisible(isVisible, focusColor);
    }

    public void SetVisible(bool isVisible, Color color)
    {
        HideAreaLines();
        ResetTransformToDefault();
        ApplyVisibility(isVisible, color, true);
    }

    public void SetAreaVisible(bool isVisible, Color color, Vector3 worldCenter, Vector2 worldSize)
    {
        CacheDefaultTransform();
        if (isVisible)
        {
            ResetTransformToDefault();
            ApplyVisibility(true, color, false);
            EnsureAreaLines();
            UpdateAreaLines(worldCenter, worldSize);
            SetAreaLinesVisible(true);
        }
        else
        {
            HideAreaLines();
            ResetTransformToDefault();
            ApplyVisibility(false, color, true);
        }
    }

    private void ApplyVisibility(bool isVisible, Color color, bool showSprite)
    {
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        focusColor = NormalizeFocusColor(color, DefaultFocusColor);
        ApplyOverlayRendering();

        if (gameObject.activeSelf != isVisible)
        {
            gameObject.SetActive(isVisible);
        }

        if (render != null)
        {
            render.enabled = isVisible && showSprite;
        }
    }

    private void EnsureAreaLines()
    {
        if (areaLines != null && areaLines.Length == AreaLineCount)
        {
            return;
        }

        areaLines = new LineRenderer[AreaLineCount];
        for (int i = 0; i < areaLines.Length; i++)
        {
            GameObject lineObject = new GameObject($"AreaCornerLine_{i}");
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 3;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 5001;
            line.sharedMaterial = GetLineMaterial();
            areaLines[i] = line;
        }
    }

    private void UpdateAreaLines(Vector3 worldCenter, Vector2 worldSize)
    {
        if (areaLines == null || areaLines.Length != AreaLineCount)
        {
            return;
        }

        focusColor = NormalizeFocusColor(focusColor, DefaultFocusColor);

        Vector2 markerMatchedSize = MatchAreaSizeToSingleMarker(worldSize);
        float width = Mathf.Max(0.01f, markerMatchedSize.x);
        float depth = Mathf.Max(0.01f, markerMatchedSize.y);
        float y = worldCenter.y;
        if (transform.parent != null)
        {
            y = transform.parent.TransformPoint(defaultLocalPosition).y;
        }

        // LineRenderer draws around its centerline. Keep the outside edge aligned
        // with the single-cell sprite marker by moving multi-cell corners inward.
        float strokeInset = Mathf.Max(0f, areaLineWidth * 0.5f);
        float minX = worldCenter.x - width * 0.5f + strokeInset;
        float maxX = worldCenter.x + width * 0.5f - strokeInset;
        float minZ = worldCenter.z - depth * 0.5f + strokeInset;
        float maxZ = worldCenter.z + depth * 0.5f - strokeInset;
        float length = Mathf.Min(
            ResolveMatchedCornerCenterlineLength(strokeInset),
            width * 0.5f,
            depth * 0.5f);

        SetAreaCornerLine(
            0,
            new Vector3(minX + length, y, minZ),
            new Vector3(minX, y, minZ),
            new Vector3(minX, y, minZ + length));
        SetAreaCornerLine(
            1,
            new Vector3(maxX - length, y, minZ),
            new Vector3(maxX, y, minZ),
            new Vector3(maxX, y, minZ + length));
        SetAreaCornerLine(
            2,
            new Vector3(minX + length, y, maxZ),
            new Vector3(minX, y, maxZ),
            new Vector3(minX, y, maxZ - length));
        SetAreaCornerLine(
            3,
            new Vector3(maxX - length, y, maxZ),
            new Vector3(maxX, y, maxZ),
            new Vector3(maxX, y, maxZ - length));
    }

    private Vector2 MatchAreaSizeToSingleMarker(Vector2 worldSize)
    {
        Vector2 singleMarkerSize = GetSingleMarkerWorldSize();
        return new Vector2(
            MatchAreaAxisSizeToSingleMarker(worldSize.x, singleMarkerSize.x),
            MatchAreaAxisSizeToSingleMarker(worldSize.y, singleMarkerSize.y));
    }

    private static float MatchAreaAxisSizeToSingleMarker(float worldSize, float singleMarkerSize)
    {
        float normalizedWorldSize = Mathf.Max(0.01f, worldSize);
        float normalizedMarkerSize = Mathf.Max(0.01f, singleMarkerSize);
        return normalizedWorldSize <= 1f
            ? normalizedMarkerSize
            : Mathf.Max(normalizedMarkerSize, normalizedWorldSize - 1f + normalizedMarkerSize);
    }

    private Vector2 GetSingleMarkerWorldSize()
    {
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        if (render == null || render.sprite == null)
        {
            return Vector2.one;
        }

        Bounds spriteBounds = render.sprite.bounds;
        Transform renderTransform = render.transform;
        Vector3 spriteXAxis = renderTransform.TransformVector(new Vector3(spriteBounds.size.x, 0f, 0f));
        Vector3 spriteYAxis = renderTransform.TransformVector(new Vector3(0f, spriteBounds.size.y, 0f));
        Vector2 worldSize = new Vector2(
            Mathf.Abs(spriteXAxis.x) + Mathf.Abs(spriteYAxis.x),
            Mathf.Abs(spriteXAxis.z) + Mathf.Abs(spriteYAxis.z));

        return worldSize.x > 0.01f && worldSize.y > 0.01f
            ? worldSize
            : Vector2.one;
    }

    private float ResolveMatchedCornerCenterlineLength(float strokeInset)
    {
        Vector2 singleMarkerSize = GetSingleMarkerWorldSize();
        float singleMarkerVisibleLength =
            Mathf.Min(singleMarkerSize.x, singleMarkerSize.y)
            * SingleMarkerCornerVisibleLengthRatio;
        float matchedCenterlineLength = Mathf.Max(0.01f, singleMarkerVisibleLength - Mathf.Max(0f, strokeInset));
        return Mathf.Min(Mathf.Max(0.01f, areaCornerLength), matchedCenterlineLength);
    }

    private void SetAreaCornerLine(int index, Vector3 start, Vector3 corner, Vector3 end)
    {
        LineRenderer line = areaLines[index];
        if (line == null)
        {
            return;
        }

        line.sharedMaterial = GetLineMaterial();
        line.startWidth = areaLineWidth;
        line.endWidth = areaLineWidth;
        line.startColor = focusColor;
        line.endColor = focusColor;
        if (line.positionCount != 3)
        {
            line.positionCount = 3;
        }

        line.SetPosition(0, start);
        line.SetPosition(1, corner);
        line.SetPosition(2, end);
    }

    private void SetAreaLinesVisible(bool isVisible)
    {
        if (areaLines == null)
        {
            return;
        }

        for (int i = 0; i < areaLines.Length; i++)
        {
            LineRenderer line = areaLines[i];
            if (line == null)
            {
                continue;
            }

            if (line.gameObject.activeSelf != isVisible)
            {
                line.gameObject.SetActive(isVisible);
            }

            line.enabled = isVisible;
        }
    }

    private void HideAreaLines()
    {
        SetAreaLinesVisible(false);
    }

    private static Material GetLineMaterial()
    {
        if (lineMaterial != null)
        {
            return lineMaterial;
        }

        Shader lineShader = Shader.Find(OverlayShaderName);
        if (lineShader == null)
        {
            lineShader = Shader.Find("Sprites/Default");
        }

        if (lineShader == null)
        {
            lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (lineShader != null)
        {
            lineMaterial = new Material(lineShader)
            {
                name = "MapFocusAreaLine (Runtime)"
            };
            lineMaterial.renderQueue = 5000;
        }

        return lineMaterial;
    }

    private void CacheDefaultTransform()
    {
        if (hasDefaultTransform)
        {
            return;
        }

        defaultLocalPosition = transform.localPosition;
        defaultLocalRotation = transform.localRotation;
        defaultLocalScale = transform.localScale;
        hasDefaultTransform = true;
    }

    private void ResetTransformToDefault()
    {
        CacheDefaultTransform();
        Transform focusTransform = transform;
        focusTransform.localPosition = defaultLocalPosition;
        focusTransform.localRotation = defaultLocalRotation;
        focusTransform.localScale = defaultLocalScale;
    }

    private void ApplyOverlayRendering()
    {
        if (render == null)
        {
            return;
        }

        if (overlayMaterial == null)
        {
            Shader overlayShader = Shader.Find(OverlayShaderName);
            if (overlayShader != null)
            {
                overlayMaterial = new Material(overlayShader)
                {
                    name = "MapFocusOverlay (Runtime)"
                };
            }
        }

        if (overlayMaterial != null && render.sharedMaterial != overlayMaterial)
        {
            render.sharedMaterial = overlayMaterial;
        }

        render.sortingOrder = 5000;
        render.color = focusColor;
    }

    private void OnValidate()
    {
        focusColor = NormalizeFocusColor(focusColor, DefaultFocusColor);

        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        if (render != null)
        {
            render.color = focusColor;
        }
    }

    private static Color NormalizeFocusColor(Color color, Color fallback)
    {
        return color.a > 0f ? color : fallback;
    }
}
