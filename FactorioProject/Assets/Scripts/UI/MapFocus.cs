using System.Collections.Generic;
using UnityEngine;

public class MapFocus : MonoBehaviour
{
    private const string OverlayShaderName = "Custom/MapFocusOverlay";
    private const int AreaLineCount = 4;
    private const int ShapeDirectionLeft = 1;
    private const int ShapeDirectionRight = 2;
    private const int ShapeDirectionDown = 4;
    private const int ShapeDirectionUp = 8;
    private const float SingleMarkerCornerVisibleLengthRatio = 131f / 512f;
    public static readonly Color DefaultFocusColor = new Color(1f, 0.86f, 0f, 0.45f);
    public static readonly Color MouseFocusColor = new Color(1f, 1f, 1f, 0.22f);
    public static readonly Color SelectionFocusColor = new Color(1f, 1f, 1f, 0.45f);

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

    private readonly struct BoundaryRun
    {
        public readonly int axis;
        public readonly int start;
        public readonly int end;

        public BoundaryRun(int axis, int start, int end)
        {
            this.axis = axis;
            this.start = start;
            this.end = end;
        }
    }

    private bool hasDefaultTransform;
    private Vector3 defaultLocalPosition;
    private Quaternion defaultLocalRotation;
    private Vector3 defaultLocalScale;
    private LineRenderer[] areaLines;
    private readonly HashSet<Vector2Int> shapeCoordinates = new HashSet<Vector2Int>();
    private readonly List<BoundaryRun> horizontalShapeRuns = new List<BoundaryRun>(32);
    private readonly List<BoundaryRun> verticalShapeRuns = new List<BoundaryRun>(32);
    private readonly Dictionary<Vector2Int, int> shapeCornerDirections =
        new Dictionary<Vector2Int, int>(32);
    private readonly List<LineRenderer> shapeLines = new List<LineRenderer>(16);
    private int visibleShapeLineCount;

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
        HideShapeLines();
        ResetTransformToDefault();
        ApplyVisibility(isVisible, color, true);
    }

    public void SetAreaVisible(bool isVisible, Color color, Vector3 worldCenter, Vector2 worldSize)
    {
        CacheDefaultTransform();
        HideShapeLines();
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

    public void SetGridShapeVisible(
        bool isVisible,
        Color color,
        IReadOnlyList<Vector2Int> worldCoordinates)
    {
        CacheDefaultTransform();
        HideAreaLines();
        if (!isVisible || worldCoordinates == null || worldCoordinates.Count <= 0)
        {
            HideShapeLines();
            ResetTransformToDefault();
            ApplyVisibility(false, color, true);
            return;
        }

        ResetTransformToDefault();
        ApplyVisibility(true, color, false);
        UpdateGridShapeLines(worldCoordinates);
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
            areaLines[i] = CreateLineRenderer($"AreaCornerLine_{i}", 3);
        }
    }

    private void UpdateGridShapeLines(IReadOnlyList<Vector2Int> worldCoordinates)
    {
        shapeCoordinates.Clear();
        horizontalShapeRuns.Clear();
        verticalShapeRuns.Clear();
        shapeCornerDirections.Clear();
        for (int i = 0; i < worldCoordinates.Count; i++)
        {
            shapeCoordinates.Add(worldCoordinates[i]);
        }

        foreach (Vector2Int coordinate in shapeCoordinates)
        {
            int centerX = coordinate.x * 2;
            int centerZ = coordinate.y * 2;
            if (!shapeCoordinates.Contains(coordinate + Vector2Int.down))
            {
                horizontalShapeRuns.Add(new BoundaryRun(centerZ - 1, centerX - 1, centerX + 1));
            }

            if (!shapeCoordinates.Contains(coordinate + Vector2Int.up))
            {
                horizontalShapeRuns.Add(new BoundaryRun(centerZ + 1, centerX - 1, centerX + 1));
            }

            if (!shapeCoordinates.Contains(coordinate + Vector2Int.left))
            {
                verticalShapeRuns.Add(new BoundaryRun(centerX - 1, centerZ - 1, centerZ + 1));
            }

            if (!shapeCoordinates.Contains(coordinate + Vector2Int.right))
            {
                verticalShapeRuns.Add(new BoundaryRun(centerX + 1, centerZ - 1, centerZ + 1));
            }
        }

        CollectShapeCornerDirections(horizontalShapeRuns, true);
        CollectShapeCornerDirections(verticalShapeRuns, false);
        visibleShapeLineCount = 0;
        float worldY = transform.parent != null
            ? transform.parent.TransformPoint(defaultLocalPosition).y
            : transform.position.y;
        float cornerLength = ResolveMatchedCornerCenterlineLength(
            Mathf.Max(0f, areaLineWidth * 0.5f));
        foreach (KeyValuePair<Vector2Int, int> pair in shapeCornerDirections)
        {
            int directions = pair.Value;
            bool hasHorizontalDirection =
                (directions & (ShapeDirectionLeft | ShapeDirectionRight)) != 0;
            bool hasVerticalDirection =
                (directions & (ShapeDirectionDown | ShapeDirectionUp)) != 0;
            if (!hasHorizontalDirection || !hasVerticalDirection)
            {
                continue;
            }

            SetShapeCornerLine(
                visibleShapeLineCount++,
                pair.Key,
                directions,
                cornerLength,
                worldY);
        }

        SetShapeLinesVisibleCount(visibleShapeLineCount);
    }

    private void CollectShapeCornerDirections(
        List<BoundaryRun> runs,
        bool horizontal)
    {
        if (runs == null)
        {
            return;
        }

        for (int i = 0; i < runs.Count; i++)
        {
            BoundaryRun run = runs[i];
            if (horizontal)
            {
                AddShapeCornerDirection(
                    new Vector2Int(run.start, run.axis),
                    ShapeDirectionRight);
                AddShapeCornerDirection(
                    new Vector2Int(run.end, run.axis),
                    ShapeDirectionLeft);
                continue;
            }

            AddShapeCornerDirection(
                new Vector2Int(run.axis, run.start),
                ShapeDirectionUp);
            AddShapeCornerDirection(
                new Vector2Int(run.axis, run.end),
                ShapeDirectionDown);
        }
    }

    private void AddShapeCornerDirection(Vector2Int vertex, int direction)
    {
        shapeCornerDirections.TryGetValue(vertex, out int directions);
        shapeCornerDirections[vertex] = directions | direction;
    }

    private void SetShapeCornerLine(
        int index,
        Vector2Int doubledVertex,
        int directions,
        float cornerLength,
        float worldY)
    {
        LineRenderer line = GetOrCreateShapeLine(index);
        if (line == null)
        {
            return;
        }

        ConfigureLineAppearance(line);
        int directionCount = CountShapeDirections(directions);
        int positionCount = directionCount * 2 - 1;
        if (line.positionCount != positionCount)
        {
            line.positionCount = positionCount;
        }

        Vector3 corner = new Vector3(
            doubledVertex.x * 0.5f,
            worldY,
            doubledVertex.y * 0.5f);
        int positionIndex = 0;
        for (int direction = ShapeDirectionLeft;
             direction <= ShapeDirectionUp;
             direction <<= 1)
        {
            if ((directions & direction) == 0)
            {
                continue;
            }

            if (positionIndex > 0)
            {
                line.SetPosition(positionIndex++, corner);
            }

            line.SetPosition(
                positionIndex++,
                corner + GetShapeDirectionOffset(direction, cornerLength));
        }
    }

    private static int CountShapeDirections(int directions)
    {
        int count = 0;
        for (int direction = ShapeDirectionLeft;
             direction <= ShapeDirectionUp;
             direction <<= 1)
        {
            if ((directions & direction) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static Vector3 GetShapeDirectionOffset(int direction, float length)
    {
        switch (direction)
        {
            case ShapeDirectionLeft:
                return Vector3.left * length;
            case ShapeDirectionRight:
                return Vector3.right * length;
            case ShapeDirectionDown:
                return Vector3.back * length;
            default:
                return Vector3.forward * length;
        }
    }

    private LineRenderer GetOrCreateShapeLine(int index)
    {
        while (shapeLines.Count <= index)
        {
            shapeLines.Add(CreateLineRenderer($"ShapeBoundaryLine_{shapeLines.Count}", 2));
        }

        return shapeLines[index];
    }

    private LineRenderer CreateLineRenderer(string objectName, int positionCount)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = positionCount;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 5001;
        line.sharedMaterial = GetLineMaterial();
        return line;
    }

    private void ConfigureLineAppearance(LineRenderer line)
    {
        if (line == null)
        {
            return;
        }

        line.sharedMaterial = GetLineMaterial();
        line.startWidth = areaLineWidth;
        line.endWidth = areaLineWidth;
        line.startColor = focusColor;
        line.endColor = focusColor;
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

        ConfigureLineAppearance(line);
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

    private void SetShapeLinesVisibleCount(int visibleCount)
    {
        for (int i = 0; i < shapeLines.Count; i++)
        {
            LineRenderer line = shapeLines[i];
            if (line == null)
            {
                continue;
            }

            bool isVisible = i < visibleCount;
            if (line.gameObject.activeSelf != isVisible)
            {
                line.gameObject.SetActive(isVisible);
            }

            line.enabled = isVisible;
        }
    }

    private void HideShapeLines()
    {
        visibleShapeLineCount = 0;
        SetShapeLinesVisibleCount(0);
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
