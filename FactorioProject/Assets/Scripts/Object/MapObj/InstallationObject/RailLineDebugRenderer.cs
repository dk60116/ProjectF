using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class RailLineDebugRenderer : MonoBehaviour
{
    private const float DefaultConnectionDistance = 0.22f;
    private const float DefaultSampleSpacing = 0.2f;
    private const float DefaultLineWidth = 0.035f;
    private const float DefaultLineYOffset = 0.18f;
    private const float DefaultRefreshInterval = 0.35f;
    private const float DefaultCartArrowYOffset = 1.15f;
    private const float DefaultCartArrowLength = 0.9f;
    private const float DefaultCartArrowHeadLength = 0.28f;
    private const float DefaultCartArrowHeadWidth = 0.18f;
    private const float DefaultCartArrowLineWidth = 0.055f;

    private static readonly Color[] GroupPalette =
    {
        new Color(0.10f, 0.85f, 1.00f, 1f),
        new Color(1.00f, 0.35f, 0.85f, 1f),
        new Color(1.00f, 0.90f, 0.15f, 1f),
        new Color(0.25f, 1.00f, 0.45f, 1f),
        new Color(1.00f, 0.45f, 0.10f, 1f),
        new Color(0.45f, 0.45f, 1.00f, 1f),
        new Color(0.90f, 0.20f, 0.30f, 1f),
        new Color(0.55f, 1.00f, 0.85f, 1f)
    };
    private static readonly Color CartDirectionColor = new Color(1.00f, 0.95f, 0.12f, 1f);

    [SerializeField, Min(0.01f)]
    private float connectionDistance = DefaultConnectionDistance;
    [SerializeField, Min(0.05f)]
    private float sampleSpacing = DefaultSampleSpacing;
    [SerializeField, Min(0.005f)]
    private float lineWidth = DefaultLineWidth;
    [SerializeField]
    private float lineYOffset = DefaultLineYOffset;
    [SerializeField, Min(0.05f)]
    private float refreshInterval = DefaultRefreshInterval;
    [SerializeField, Min(0.05f)]
    private float cartArrowYOffset = DefaultCartArrowYOffset;
    [SerializeField, Min(0.05f)]
    private float cartArrowLength = DefaultCartArrowLength;
    [SerializeField, Min(0.01f)]
    private float cartArrowHeadLength = DefaultCartArrowHeadLength;
    [SerializeField, Min(0.01f)]
    private float cartArrowHeadWidth = DefaultCartArrowHeadWidth;
    [SerializeField, Min(0.005f)]
    private float cartArrowLineWidth = DefaultCartArrowLineWidth;

    private readonly List<RailInfo> rails = new List<RailInfo>();
    private readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> cartArrowRenderers = new List<LineRenderer>();
    private readonly Queue<int> componentQueue = new Queue<int>();

    private Transform debugRoot;
    private Material lineMaterial;
    private bool isVisible;
    private bool isDirty = true;
    private float nextRefreshTime;

    public void SetVisible(bool visible)
    {
        if (isVisible == visible)
        {
            return;
        }

        isVisible = visible;
        EnsureDebugRoot();
        debugRoot.gameObject.SetActive(isVisible);
        isDirty = true;

        if (!isVisible)
        {
            DisableAllRenderers();
            return;
        }

        Rebuild();
    }

    public void RefreshNow()
    {
        if (!isVisible)
        {
            isDirty = true;
            return;
        }

        Rebuild();
    }

    private void Awake()
    {
        EnsureDebugRoot();
    }

    private void OnEnable()
    {
        InstallationObject.PlacementRuntimeChanged += HandlePlacementRuntimeChanged;
        InstallationObject.PlacementRuntimeCleared += HandlePlacementRuntimeChanged;
        isDirty = true;
    }

    private void OnDisable()
    {
        InstallationObject.PlacementRuntimeChanged -= HandlePlacementRuntimeChanged;
        InstallationObject.PlacementRuntimeCleared -= HandlePlacementRuntimeChanged;
        DisableAllRenderers();
    }

    private void LateUpdate()
    {
        if (!isVisible)
        {
            return;
        }

        if (!isDirty && Time.unscaledTime < nextRefreshTime)
        {
            RefreshCartDirectionArrows();
            return;
        }

        Rebuild();
    }

    private void HandlePlacementRuntimeChanged(InstallationObject installationObject)
    {
        if (installationObject == null || installationObject is Railload)
        {
            isDirty = true;
        }
    }

    private void Rebuild()
    {
        EnsureDebugRoot();
        EnsureLineMaterial();
        rails.Clear();
        CollectRails();

        int rendererIndex = 0;
        int componentIndex = 0;
        float maxConnectionSqrDistance = connectionDistance * connectionDistance;
        for (int railIndex = 0; railIndex < rails.Count; railIndex++)
        {
            RailInfo rail = rails[railIndex];
            if (rail.ComponentIndex >= 0)
            {
                continue;
            }

            Color color = ResolveGroupColor(componentIndex);
            AssignComponent(railIndex, componentIndex, maxConnectionSqrDistance);
            for (int i = 0; i < rails.Count; i++)
            {
                if (rails[i].ComponentIndex != componentIndex)
                {
                    continue;
                }

                LineRenderer lineRenderer = EnsureLineRenderer(rendererIndex++);
                ApplyRailLine(lineRenderer, rails[i], color);
            }

            componentIndex++;
        }

        for (int i = rendererIndex; i < lineRenderers.Count; i++)
        {
            lineRenderers[i].enabled = false;
        }

        RefreshCartDirectionArrows();
        isDirty = false;
        nextRefreshTime = Time.unscaledTime + refreshInterval;
    }

    private void CollectRails()
    {
        Railload[] activeRails = FindObjectsOfType<Railload>(false);
        for (int i = 0; i < activeRails.Length; i++)
        {
            Railload rail = activeRails[i];
            if (rail == null
                || !rail.isActiveAndEnabled
                || !rail.TryGetPlacementRuntime(out _, out _)
                || !rail.TryGetRenderedPathLength(out float length))
            {
                continue;
            }

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(length / sampleSpacing) + 1, 2, 256);
            RailInfo info = new RailInfo(rail, sampleCount);
            if (!rail.TryGetRenderedEndpointSample(true, out _, out info.StartPoint, out _)
                || !rail.TryGetRenderedEndpointSample(false, out _, out info.EndPoint, out _))
            {
                continue;
            }

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float t = sampleCount <= 1 ? 0f : sampleIndex / (sampleCount - 1f);
                float distance = length * t;
                if (!rail.TrySampleRenderedPath(distance, out Vector2 point, out _))
                {
                    point = sampleIndex == 0 ? info.StartPoint : info.EndPoint;
                }

                info.Points[sampleIndex] = point;
            }

            rails.Add(info);
        }
    }

    private void AssignComponent(int startRailIndex, int componentIndex, float maxConnectionSqrDistance)
    {
        componentQueue.Clear();
        componentQueue.Enqueue(startRailIndex);
        rails[startRailIndex].ComponentIndex = componentIndex;

        while (componentQueue.Count > 0)
        {
            int currentIndex = componentQueue.Dequeue();
            RailInfo currentRail = rails[currentIndex];
            for (int otherIndex = 0; otherIndex < rails.Count; otherIndex++)
            {
                RailInfo otherRail = rails[otherIndex];
                if (otherRail.ComponentIndex >= 0
                    || !AreRailsConnected(currentRail, otherRail, maxConnectionSqrDistance))
                {
                    continue;
                }

                otherRail.ComponentIndex = componentIndex;
                componentQueue.Enqueue(otherIndex);
            }
        }
    }

    private static bool AreRailsConnected(RailInfo a, RailInfo b, float maxConnectionSqrDistance)
    {
        return IsEndpointNearRail(a.StartPoint, b, maxConnectionSqrDistance)
               || IsEndpointNearRail(a.EndPoint, b, maxConnectionSqrDistance)
               || IsEndpointNearRail(b.StartPoint, a, maxConnectionSqrDistance)
               || IsEndpointNearRail(b.EndPoint, a, maxConnectionSqrDistance);
    }

    private static bool IsEndpointNearRail(Vector2 endpoint, RailInfo rail, float maxConnectionSqrDistance)
    {
        if ((endpoint - rail.StartPoint).sqrMagnitude <= maxConnectionSqrDistance
            || (endpoint - rail.EndPoint).sqrMagnitude <= maxConnectionSqrDistance)
        {
            return true;
        }

        return rail.Rail.TryFindNearestRenderedPathSample(
                   endpoint,
                   out _,
                   out _,
                   out _,
                   out float sqrDistance)
               && sqrDistance <= maxConnectionSqrDistance;
    }

    private void ApplyRailLine(LineRenderer lineRenderer, RailInfo rail, Color color)
    {
        lineRenderer.enabled = true;
        lineRenderer.positionCount = rail.Points.Length;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.material = lineMaterial;

        float y = rail.Rail.transform.position.y + lineYOffset;
        for (int i = 0; i < rail.Points.Length; i++)
        {
            Vector2 point = rail.Points[i];
            lineRenderer.SetPosition(i, new Vector3(point.x, y, point.y));
        }
    }

    private LineRenderer EnsureLineRenderer(int index)
    {
        EnsureDebugRoot();
        while (lineRenderers.Count <= index)
        {
            GameObject lineObject = new GameObject("Rail_Line_Debug");
            lineObject.transform.SetParent(debugRoot, false);

            LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = false;
            lineRenderer.numCapVertices = 2;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderers.Add(lineRenderer);
        }

        return lineRenderers[index];
    }

    private void RefreshCartDirectionArrows()
    {
        EnsureDebugRoot();
        EnsureLineMaterial();

        int rendererIndex = 0;
        RailHandcar[] activeHandcars = FindObjectsOfType<RailHandcar>(false);
        for (int i = 0; i < activeHandcars.Length; i++)
        {
            RailHandcar handcar = activeHandcars[i];
            if (handcar == null
                || !handcar.isActiveAndEnabled
                || !handcar.TryGetRailDebugDirection(out Vector3 cartPosition, out Vector3 direction))
            {
                continue;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            direction.Normalize();
            Vector3 side = new Vector3(-direction.z, 0f, direction.x);
            Vector3 center = cartPosition + Vector3.up * cartArrowYOffset;
            float halfLength = Mathf.Max(0.05f, cartArrowLength) * 0.5f;
            Vector3 tail = center - direction * halfLength;
            Vector3 tip = center + direction * halfLength;
            Vector3 headBase = tip - direction * Mathf.Max(0.01f, cartArrowHeadLength);
            Vector3 headSide = side * Mathf.Max(0.01f, cartArrowHeadWidth);

            ApplyCartArrowSegment(EnsureCartArrowRenderer(rendererIndex++), tail, tip);
            ApplyCartArrowSegment(EnsureCartArrowRenderer(rendererIndex++), tip, headBase + headSide);
            ApplyCartArrowSegment(EnsureCartArrowRenderer(rendererIndex++), tip, headBase - headSide);
        }

        for (int i = rendererIndex; i < cartArrowRenderers.Count; i++)
        {
            if (cartArrowRenderers[i] != null)
            {
                cartArrowRenderers[i].enabled = false;
            }
        }
    }

    private void ApplyCartArrowSegment(LineRenderer lineRenderer, Vector3 start, Vector3 end)
    {
        if (lineRenderer == null)
        {
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = cartArrowLineWidth;
        lineRenderer.endWidth = cartArrowLineWidth;
        lineRenderer.startColor = CartDirectionColor;
        lineRenderer.endColor = CartDirectionColor;
        lineRenderer.material = lineMaterial;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }

    private LineRenderer EnsureCartArrowRenderer(int index)
    {
        EnsureDebugRoot();
        while (cartArrowRenderers.Count <= index)
        {
            GameObject lineObject = new GameObject("Rail_Cart_Direction_Debug");
            lineObject.transform.SetParent(debugRoot, false);

            LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = false;
            lineRenderer.numCapVertices = 2;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.sortingOrder = 6501;
            cartArrowRenderers.Add(lineRenderer);
        }

        return cartArrowRenderers[index];
    }

    private void DisableAllRenderers()
    {
        for (int i = 0; i < lineRenderers.Count; i++)
        {
            if (lineRenderers[i] != null)
            {
                lineRenderers[i].enabled = false;
            }
        }

        for (int i = 0; i < cartArrowRenderers.Count; i++)
        {
            if (cartArrowRenderers[i] != null)
            {
                cartArrowRenderers[i].enabled = false;
            }
        }
    }

    private void EnsureDebugRoot()
    {
        if (debugRoot != null)
        {
            return;
        }

        GameObject rootObject = new GameObject("Rail Line Debug Root");
        rootObject.transform.SetParent(transform, false);
        rootObject.SetActive(isVisible);
        debugRoot = rootObject.transform;
    }

    private void EnsureLineMaterial()
    {
        if (lineMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        lineMaterial = new Material(shader)
        {
            name = "Rail Line Debug Material"
        };
    }

    private static Color ResolveGroupColor(int groupIndex)
    {
        if (groupIndex < GroupPalette.Length)
        {
            return GroupPalette[groupIndex];
        }

        float hue = Mathf.Repeat(groupIndex * 0.61803398875f, 1f);
        return Color.HSVToRGB(hue, 0.75f, 1f);
    }

    private sealed class RailInfo
    {
        public RailInfo(Railload rail, int sampleCount)
        {
            Rail = rail;
            Points = new Vector2[sampleCount];
            ComponentIndex = -1;
        }

        public Railload Rail { get; }
        public Vector2 StartPoint;
        public Vector2 EndPoint;
        public Vector2[] Points { get; }
        public int ComponentIndex;
    }
}
