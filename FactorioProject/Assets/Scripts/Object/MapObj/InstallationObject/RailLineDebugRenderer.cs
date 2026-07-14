using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class RailLineDebugRenderer : MonoBehaviour
{
    public const float RailGroupConnectionDistance = RailConnectionUtility.ConnectionDistance;
    private const float DefaultSampleSpacing = 0.2f;
    private const float DefaultLineWidth = 0.035f;
    private const float DefaultRouteHighlightWidthMultiplier = 2.6f;
    private const float DefaultLineYOffset = 0.18f;
    private const float DefaultRefreshInterval = 0.35f;
    private const float DefaultRailArrowYOffset = 0.32f;
    private const float DefaultRailArrowSpacing = 1.8f;
    private const float DefaultRailArrowLength = 0.42f;
    private const float DefaultRailArrowHeadLength = 0.12f;
    private const float DefaultRailArrowHeadWidth = 0.08f;
    private const float DefaultRailArrowLineWidth = 0.028f;
    private const float DefaultCartArrowYOffset = 1.15f;
    private const float DefaultCartArrowLength = 0.9f;
    private const float DefaultCartArrowHeadLength = 0.28f;
    private const float DefaultCartArrowHeadWidth = 0.18f;
    private const float DefaultCartArrowLineWidth = 0.055f;
    private const float DefaultPowerMarkerYOffset = 1.35f;
    private const float DefaultPowerMarkerSize = 0.8f;
    private const float DefaultPowerMarkerLineWidth = 0.07f;
    private const float DefaultTargetStationMarkerYOffset = 1.6f;
    private const float DefaultTargetStationMarkerSize = 1.05f;
    private const float DefaultTargetStationMarkerLineWidth = 0.085f;

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
    private static readonly Color BlockedCartDirectionColor = Color.black;
    private static readonly Color RouteHighlightColor = new Color(1.00f, 1.00f, 1.00f, 0.98f);
    private static readonly Color PowerSourceMarkerColor = new Color(1.00f, 0.35f, 0.10f, 1f);
    private static readonly Color TargetStationMarkerColor = new Color(0.20f, 1.00f, 0.55f, 1f);

    [SerializeField, Min(0.01f)]
    private float connectionDistance = RailGroupConnectionDistance;
    [SerializeField, Min(0.05f)]
    private float sampleSpacing = DefaultSampleSpacing;
    [SerializeField, Min(0.005f)]
    private float lineWidth = DefaultLineWidth;
    [SerializeField]
    private float lineYOffset = DefaultLineYOffset;
    [SerializeField, Min(0.05f)]
    private float refreshInterval = DefaultRefreshInterval;
    [SerializeField]
    private float railArrowYOffset = DefaultRailArrowYOffset;
    [SerializeField, Min(0.2f)]
    private float railArrowSpacing = DefaultRailArrowSpacing;
    [SerializeField, Min(0.05f)]
    private float railArrowLength = DefaultRailArrowLength;
    [SerializeField, Min(0.01f)]
    private float railArrowHeadLength = DefaultRailArrowHeadLength;
    [SerializeField, Min(0.01f)]
    private float railArrowHeadWidth = DefaultRailArrowHeadWidth;
    [SerializeField, Min(0.005f)]
    private float railArrowLineWidth = DefaultRailArrowLineWidth;
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
    [SerializeField]
    private float powerMarkerYOffset = DefaultPowerMarkerYOffset;
    [SerializeField, Min(0.05f)]
    private float powerMarkerSize = DefaultPowerMarkerSize;
    [SerializeField, Min(0.005f)]
    private float powerMarkerLineWidth = DefaultPowerMarkerLineWidth;
    [SerializeField]
    private float targetStationMarkerYOffset = DefaultTargetStationMarkerYOffset;
    [SerializeField, Min(0.05f)]
    private float targetStationMarkerSize = DefaultTargetStationMarkerSize;
    [SerializeField, Min(0.005f)]
    private float targetStationMarkerLineWidth = DefaultTargetStationMarkerLineWidth;

    private readonly List<RailInfo> rails = new List<RailInfo>();
    private readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> routeHighlightRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> railArrowRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> cartArrowRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> powerMarkerRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> targetStationMarkerRenderers = new List<LineRenderer>();
    private readonly List<RailHandcar> activeHandcarScratch = new List<RailHandcar>(4);
    private readonly List<SteamTrain> selectedPowerTrainScratch = new List<SteamTrain>(4);
    private readonly List<Trainstation> selectedTargetStationScratch = new List<Trainstation>(4);
    private readonly Queue<Train> selectedTrainQueue = new Queue<Train>(8);
    private readonly HashSet<Train> selectedTrainVisited = new HashSet<Train>();
    private readonly Queue<int> componentQueue = new Queue<int>();
    private readonly List<RouteHighlightSegment> routeHighlightSegmentScratch = new List<RouteHighlightSegment>(32);
    private readonly List<SteamTrain.AutoDriveDebugRouteSegment> autoDriveRouteSegmentScratch =
        new List<SteamTrain.AutoDriveDebugRouteSegment>(32);

    private Transform debugRoot;
    private Material lineMaterial;
    private bool isVisible;
    private bool isDirty = true;
    private float nextCartArrowRefreshTime;
    private int lastRouteSelectionTrainInstanceId;
    private string lastRouteSelectionCurrentTarget = string.Empty;
    private string lastRouteSelectionNextTarget = string.Empty;
    private int lastRouteSelectionVersion = -1;

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

        if (lastRouteSelectionVersion != TrainFilter.RouteSelectionVersion)
        {
            isDirty = true;
        }

        if (HasRouteSelectionStateChanged())
        {
            isDirty = true;
        }

        if (isDirty)
        {
            Rebuild();
            return;
        }

        if (Time.unscaledTime >= nextCartArrowRefreshTime)
        {
            RefreshCartDirectionArrows();
            RefreshAutoDrivePowerSourceMarker();
            RefreshSelectedTargetStationMarker();
        }
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
        int railArrowRendererIndex = 0;
        int componentIndex = 0;
        float effectiveConnectionDistance = Mathf.Max(connectionDistance, RailGroupConnectionDistance);
        float maxConnectionSqrDistance = effectiveConnectionDistance * effectiveConnectionDistance;
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
                railArrowRendererIndex = ApplyRailDirectionArrows(rails[i], color, railArrowRendererIndex);
            }

            componentIndex++;
        }

        for (int i = rendererIndex; i < lineRenderers.Count; i++)
        {
            lineRenderers[i].enabled = false;
        }

        ApplyRouteHighlight();

        DisableRailArrowRenderers(railArrowRendererIndex);

        RefreshCartDirectionArrows();
        RefreshAutoDrivePowerSourceMarker();
        RefreshSelectedTargetStationMarker();
        isDirty = false;
        nextCartArrowRefreshTime = Time.unscaledTime + refreshInterval;
        CacheRouteSelectionState();
        lastRouteSelectionVersion = TrainFilter.RouteSelectionVersion;
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
            info.Length = length;
            if (!RailConnectionUtility.TryResolveConnectionEndpoints(
                    rail.RuntimeVisualPathPoints,
                    rail.RuntimeOccupiedCoordinates,
                    out info.ConnectionStartPoint,
                    out info.ConnectionEndPoint))
            {
                continue;
            }

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
        return a != null
               && b != null
               && RailConnectionUtility.AreConnected(
                   a.Rail != null ? a.Rail.RuntimeOccupiedCoordinates : null,
                   a.Rail != null ? a.Rail.RuntimeVisualPathPoints : null,
                   a.ConnectionStartPoint,
                   a.ConnectionEndPoint,
                   b.Rail != null ? b.Rail.RuntimeOccupiedCoordinates : null,
                   b.Rail != null ? b.Rail.RuntimeVisualPathPoints : null,
                   b.ConnectionStartPoint,
                   b.ConnectionEndPoint,
                   maxConnectionSqrDistance);
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

    private int ApplyRailDirectionArrows(RailInfo rail, Color color, int rendererIndex)
    {
        if (rail == null
            || rail.Rail == null
            || rail.Length <= 0.05f)
        {
            return rendererIndex;
        }

        float spacing = Mathf.Max(0.2f, railArrowSpacing);
        float arrowLength = Mathf.Max(0.05f, railArrowLength);
        float halfLength = arrowLength * 0.5f;
        float startDistance = Mathf.Max(halfLength, spacing * 0.5f);
        float endDistance = rail.Length - halfLength;
        if (endDistance < startDistance)
        {
            return rendererIndex;
        }

        float y = rail.Rail.transform.position.y + railArrowYOffset;
        for (float distance = startDistance; distance <= endDistance + 0.001f; distance += spacing)
        {
            if (!rail.Rail.TrySampleRenderedPath(distance, out Vector2 point, out Vector2 tangent)
                || tangent.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            tangent.Normalize();
            Vector3 direction = new Vector3(tangent.x, 0f, tangent.y);
            Vector3 side = new Vector3(-direction.z, 0f, direction.x);
            Vector3 center = new Vector3(point.x, y, point.y);
            Vector3 tail = center - direction * halfLength;
            Vector3 tip = center + direction * halfLength;
            Vector3 headBase = tip - direction * Mathf.Max(0.01f, railArrowHeadLength);
            Vector3 headSide = side * Mathf.Max(0.01f, railArrowHeadWidth);

            ApplyArrowSegment(EnsureRailArrowRenderer(rendererIndex++), tail, tip, color, railArrowLineWidth);
            ApplyArrowSegment(EnsureRailArrowRenderer(rendererIndex++), tip, headBase + headSide, color, railArrowLineWidth);
            ApplyArrowSegment(EnsureRailArrowRenderer(rendererIndex++), tip, headBase - headSide, color, railArrowLineWidth);
        }

        return rendererIndex;
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

    private void ApplyRouteHighlight()
    {
        routeHighlightSegmentScratch.Clear();
        if (!TryCollectSelectedRouteSegments(routeHighlightSegmentScratch))
        {
            DisableRouteHighlightRenderers();
            return;
        }

        int rendererIndex = 0;
        for (int i = 0; i < routeHighlightSegmentScratch.Count; i++)
        {
            LineRenderer lineRenderer = EnsureRouteHighlightRenderer(rendererIndex++);
            ApplyRouteHighlightLine(lineRenderer, routeHighlightSegmentScratch[i]);
        }

        DisableRouteHighlightRenderers(rendererIndex);
    }

    private bool TryCollectSelectedRouteSegments(List<RouteHighlightSegment> result)
    {
        if (result == null
            || !TrainFilter.TryGetActiveRouteSelection(
                out _,
                out string startStationName,
                out string destinationStationName)
            || string.IsNullOrWhiteSpace(startStationName)
            || string.IsNullOrWhiteSpace(destinationStationName)
            || !SteamTrain.TryBuildDebugRouteBetweenStations(
                startStationName,
                destinationStationName,
                autoDriveRouteSegmentScratch))
        {
            return false;
        }

        result.Clear();
        for (int i = 0; i < autoDriveRouteSegmentScratch.Count; i++)
        {
            SteamTrain.AutoDriveDebugRouteSegment segment = autoDriveRouteSegmentScratch[i];
            if (segment.Rail == null
                || !TryFindRailInfoIndex(segment.Rail, out int railIndex))
            {
                continue;
            }

            AppendRouteHighlightSegment(
                result,
                railIndex,
                segment.StartDistance,
                segment.EndDistance);
        }

        return result.Count > 0;
    }

    private bool TryFindRailInfoIndex(Railload rail, out int railIndex)
    {
        railIndex = -1;
        if (rail == null)
        {
            return false;
        }

        for (int i = 0; i < rails.Count; i++)
        {
            RailInfo railInfo = rails[i];
            if (railInfo?.Rail != rail)
            {
                continue;
            }

            railIndex = i;
            return true;
        }

        return false;
    }

    private static void AppendRouteHighlightSegment(
        List<RouteHighlightSegment> segments,
        int railIndex,
        float startDistance,
        float endDistance)
    {
        if (segments == null
            || railIndex < 0
            || Mathf.Abs(endDistance - startDistance) <= 0.0001f)
        {
            return;
        }

        if (segments.Count > 0)
        {
            RouteHighlightSegment lastSegment = segments[segments.Count - 1];
            if (lastSegment.RailIndex == railIndex
                && Mathf.Abs(lastSegment.EndDistance - startDistance) <= 0.01f)
            {
                segments[segments.Count - 1] = new RouteHighlightSegment(
                    railIndex,
                    lastSegment.StartDistance,
                    endDistance);
                return;
            }
        }

        segments.Add(new RouteHighlightSegment(railIndex, startDistance, endDistance));
    }

    private void ApplyRouteHighlightLine(LineRenderer lineRenderer, RouteHighlightSegment segment)
    {
        if (lineRenderer == null
            || segment.RailIndex < 0
            || segment.RailIndex >= rails.Count)
        {
            return;
        }

        RailInfo rail = rails[segment.RailIndex];
        if (rail == null || rail.Rail == null)
        {
            return;
        }

        float startDistance = Mathf.Clamp(segment.StartDistance, 0f, rail.Length);
        float endDistance = Mathf.Clamp(segment.EndDistance, 0f, rail.Length);
        float highlightLength = Mathf.Abs(endDistance - startDistance);
        int sampleCount = Mathf.Clamp(Mathf.CeilToInt(highlightLength / Mathf.Max(0.01f, sampleSpacing)) + 1, 2, 256);

        lineRenderer.enabled = true;
        lineRenderer.positionCount = sampleCount;
        float width = Mathf.Max(0.005f, lineWidth * DefaultRouteHighlightWidthMultiplier);
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.startColor = RouteHighlightColor;
        lineRenderer.endColor = RouteHighlightColor;
        lineRenderer.material = lineMaterial;

        float y = rail.Rail.transform.position.y + lineYOffset + 0.01f;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = sampleCount <= 1 ? 0f : i / (sampleCount - 1f);
            float sampleDistance = Mathf.Lerp(startDistance, endDistance, t);
            if (!rail.Rail.TrySampleRenderedPath(sampleDistance, out Vector2 point, out _))
            {
                point = t <= 0f ? rail.StartPoint : rail.EndPoint;
            }

            lineRenderer.SetPosition(i, new Vector3(point.x, y, point.y));
        }
    }

    private bool HasRouteSelectionStateChanged()
    {
        CaptureRouteSelectionState(
            out int currentTrainInstanceId,
            out string currentTargetStationName,
            out string currentNextTargetStationName);
        return currentTrainInstanceId != lastRouteSelectionTrainInstanceId
               || !string.Equals(
                   currentTargetStationName,
                   lastRouteSelectionCurrentTarget,
                   System.StringComparison.OrdinalIgnoreCase)
               || !string.Equals(
                   currentNextTargetStationName,
                   lastRouteSelectionNextTarget,
                   System.StringComparison.OrdinalIgnoreCase);
    }

    private void CacheRouteSelectionState()
    {
        CaptureRouteSelectionState(
            out lastRouteSelectionTrainInstanceId,
            out lastRouteSelectionCurrentTarget,
            out lastRouteSelectionNextTarget);
    }

    private static void CaptureRouteSelectionState(
        out int trainInstanceId,
        out string currentTargetStationName,
        out string nextTargetStationName)
    {
        trainInstanceId = 0;
        currentTargetStationName = string.Empty;
        nextTargetStationName = string.Empty;
        if (!TrainFilter.TryGetActiveRouteSelection(
                out SteamTrain train,
                out string targetAStationName,
                out string targetBStationName)
            || train == null)
        {
            return;
        }

        trainInstanceId = train.GetInstanceID();
        currentTargetStationName = targetAStationName ?? string.Empty;
        nextTargetStationName = targetBStationName ?? string.Empty;
    }

    private void RefreshCartDirectionArrows()
    {
        EnsureDebugRoot();
        if (!ShouldShowCartDirectionArrows())
        {
            DisableCartArrowRenderers();
            nextCartArrowRefreshTime = Time.unscaledTime + refreshInterval;
            return;
        }

        EnsureLineMaterial();

        int rendererIndex = 0;
        activeHandcarScratch.Clear();
        RailHandcar.CollectActiveRuntimeHandcars(activeHandcarScratch);
        for (int i = 0; i < activeHandcarScratch.Count; i++)
        {
            RailHandcar handcar = activeHandcarScratch[i];
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
            Color arrowColor = handcar.IsRailDebugDirectionBlocked(direction)
                ? BlockedCartDirectionColor
                : CartDirectionColor;

            ApplyCartArrowSegment(EnsureCartArrowRenderer(rendererIndex++), tail, tip, arrowColor);
            ApplyCartArrowSegment(EnsureCartArrowRenderer(rendererIndex++), tip, headBase + headSide, arrowColor);
            ApplyCartArrowSegment(EnsureCartArrowRenderer(rendererIndex++), tip, headBase - headSide, arrowColor);
        }

        DisableCartArrowRenderers(rendererIndex);

        activeHandcarScratch.Clear();
        nextCartArrowRefreshTime = Time.unscaledTime + refreshInterval;
    }

    private static bool ShouldShowCartDirectionArrows()
    {
        return GameManager.Instance != null && GameManager.Instance.ShowDirections;
    }

    private void RefreshAutoDrivePowerSourceMarker()
    {
        EnsureDebugRoot();
        EnsureLineMaterial();
        if (!TryCollectSelectedAutoDrivePowerSourceTrains(selectedPowerTrainScratch))
        {
            DisablePowerMarkerRenderers();
            return;
        }

        float radius = Mathf.Max(0.05f, powerMarkerSize) * 0.5f;
        int rendererIndex = 0;
        for (int i = 0; i < selectedPowerTrainScratch.Count; i++)
        {
            SteamTrain powerSourceTrain = selectedPowerTrainScratch[i];
            if (powerSourceTrain == null)
            {
                continue;
            }

            Vector3 center = powerSourceTrain.transform.position + Vector3.up * powerMarkerYOffset;
            rendererIndex = ApplyPowerMarkerCross(rendererIndex, center, radius);
        }

        DisablePowerMarkerRenderers(rendererIndex);
        selectedPowerTrainScratch.Clear();
    }

    private void RefreshSelectedTargetStationMarker()
    {
        EnsureDebugRoot();
        EnsureLineMaterial();
        if (!TryCollectSelectedAutoDriveTargetStations(selectedTargetStationScratch))
        {
            DisableTargetStationMarkerRenderers();
            return;
        }

        float radius = Mathf.Max(0.05f, targetStationMarkerSize) * 0.5f;
        float diagonalRadius = radius * 0.72f;
        int rendererIndex = 0;
        for (int i = 0; i < selectedTargetStationScratch.Count; i++)
        {
            Trainstation targetStation = selectedTargetStationScratch[i];
            if (targetStation == null)
            {
                continue;
            }

            Vector3 center = targetStation.transform.position + Vector3.up * targetStationMarkerYOffset;
            rendererIndex = ApplyTargetStationMarkerCross(rendererIndex, center, radius, diagonalRadius);
        }

        DisableTargetStationMarkerRenderers(rendererIndex);
        selectedTargetStationScratch.Clear();
    }

    private int ApplyPowerMarkerCross(int rendererIndex, Vector3 center, float radius)
    {
        ApplyArrowSegment(
            EnsurePowerMarkerRenderer(rendererIndex++),
            center + new Vector3(-radius, 0f, 0f),
            center + new Vector3(radius, 0f, 0f),
            PowerSourceMarkerColor,
            powerMarkerLineWidth);
        ApplyArrowSegment(
            EnsurePowerMarkerRenderer(rendererIndex++),
            center + new Vector3(0f, 0f, -radius),
            center + new Vector3(0f, 0f, radius),
            PowerSourceMarkerColor,
            powerMarkerLineWidth);
        ApplyArrowSegment(
            EnsurePowerMarkerRenderer(rendererIndex++),
            center + new Vector3(-radius * 0.7f, 0f, -radius * 0.7f),
            center + new Vector3(radius * 0.7f, 0f, radius * 0.7f),
            PowerSourceMarkerColor,
            powerMarkerLineWidth);
        ApplyArrowSegment(
            EnsurePowerMarkerRenderer(rendererIndex++),
            center + new Vector3(-radius * 0.7f, 0f, radius * 0.7f),
            center + new Vector3(radius * 0.7f, 0f, -radius * 0.7f),
            PowerSourceMarkerColor,
            powerMarkerLineWidth);
        return rendererIndex;
    }

    private int ApplyTargetStationMarkerCross(
        int rendererIndex,
        Vector3 center,
        float radius,
        float diagonalRadius)
    {
        ApplyArrowSegment(
            EnsureTargetStationMarkerRenderer(rendererIndex++),
            center + new Vector3(-radius, 0f, 0f),
            center + new Vector3(radius, 0f, 0f),
            TargetStationMarkerColor,
            targetStationMarkerLineWidth);
        ApplyArrowSegment(
            EnsureTargetStationMarkerRenderer(rendererIndex++),
            center + new Vector3(0f, 0f, -radius),
            center + new Vector3(0f, 0f, radius),
            TargetStationMarkerColor,
            targetStationMarkerLineWidth);
        ApplyArrowSegment(
            EnsureTargetStationMarkerRenderer(rendererIndex++),
            center + new Vector3(-diagonalRadius, 0f, -diagonalRadius),
            center + new Vector3(diagonalRadius, 0f, diagonalRadius),
            TargetStationMarkerColor,
            targetStationMarkerLineWidth);
        ApplyArrowSegment(
            EnsureTargetStationMarkerRenderer(rendererIndex++),
            center + new Vector3(-diagonalRadius, 0f, diagonalRadius),
            center + new Vector3(diagonalRadius, 0f, -diagonalRadius),
            TargetStationMarkerColor,
            targetStationMarkerLineWidth);
        return rendererIndex;
    }

    private bool TryCollectSelectedAutoDrivePowerSourceTrains(List<SteamTrain> results)
    {
        if (results == null)
        {
            return false;
        }

        results.Clear();
        if (!TrainFilter.TryGetActiveRouteSelection(
                out SteamTrain selectedTrain,
                out string targetAStationName,
                out string targetBStationName)
            || selectedTrain == null
            || !selectedTrain.gameObject.activeInHierarchy
            || !selectedTrain.TryGetPlacementRuntime(out _, out _)
            || string.IsNullOrWhiteSpace(targetAStationName)
            || string.IsNullOrWhiteSpace(targetBStationName))
        {
            return false;
        }

        CollectMatchingAutoDrivePowerTrains(
            selectedTrain,
            targetAStationName,
            targetBStationName,
            results);
        return results.Count > 0;
    }

    private bool TryCollectSelectedAutoDriveTargetStations(List<Trainstation> results)
    {
        if (results == null)
        {
            return false;
        }

        results.Clear();
        if (!TryCollectSelectedAutoDrivePowerSourceTrains(selectedPowerTrainScratch))
        {
            return false;
        }

        for (int i = 0; i < selectedPowerTrainScratch.Count; i++)
        {
            SteamTrain powerTrain = selectedPowerTrainScratch[i];
            if (powerTrain == null
                || !powerTrain.TryGetCurrentAutoDriveTargetStation(out Trainstation targetStation)
                || targetStation == null
                || !targetStation.gameObject.activeInHierarchy
                || !targetStation.TryGetPlacementRuntime(out _, out _))
            {
                continue;
            }

            AddUniqueTargetStation(results, targetStation);
        }

        selectedPowerTrainScratch.Clear();
        return results.Count > 0;
    }

    private void CollectMatchingAutoDrivePowerTrains(
        SteamTrain selectedTrain,
        string targetAStationName,
        string targetBStationName,
        List<SteamTrain> results)
    {
        selectedTrainQueue.Clear();
        selectedTrainVisited.Clear();
        selectedTrainQueue.Enqueue(selectedTrain);
        selectedTrainVisited.Add(selectedTrain);

        while (selectedTrainQueue.Count > 0)
        {
            Train currentTrain = selectedTrainQueue.Dequeue();
            if (currentTrain == null || !currentTrain.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (currentTrain is SteamTrain steamTrain
                && steamTrain.AutoDriveEnabled
                && steamTrain.TryGetPlacementRuntime(out _, out _)
                && HasMatchingAutoDriveTargets(steamTrain, targetAStationName, targetBStationName))
            {
                results.Add(steamTrain);
            }

            foreach (Train connectedTrain in currentTrain.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !selectedTrainVisited.Add(connectedTrain))
                {
                    continue;
                }

                selectedTrainQueue.Enqueue(connectedTrain);
            }
        }

        selectedTrainQueue.Clear();
        selectedTrainVisited.Clear();
    }

    private static bool HasMatchingAutoDriveTargets(
        SteamTrain train,
        string targetAStationName,
        string targetBStationName)
    {
        if (train == null)
        {
            return false;
        }

        bool directMatch =
            IsSameStationName(train.AutoDriveTargetAStationName, targetAStationName)
            && IsSameStationName(train.AutoDriveTargetBStationName, targetBStationName);
        bool reverseMatch =
            IsSameStationName(train.AutoDriveTargetAStationName, targetBStationName)
            && IsSameStationName(train.AutoDriveTargetBStationName, targetAStationName);
        return directMatch || reverseMatch;
    }

    private static bool IsSameStationName(string first, string second)
    {
        return string.Equals(first, second, System.StringComparison.OrdinalIgnoreCase);
    }

    private static void AddUniqueTargetStation(List<Trainstation> results, Trainstation station)
    {
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i] == station)
            {
                return;
            }
        }

        results.Add(station);
    }

    private void ApplyArrowSegment(
        LineRenderer lineRenderer,
        Vector3 start,
        Vector3 end,
        Color color,
        float width)
    {
        if (lineRenderer == null)
        {
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.material = lineMaterial;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }

    private void ApplyCartArrowSegment(LineRenderer lineRenderer, Vector3 start, Vector3 end, Color color)
    {
        ApplyArrowSegment(lineRenderer, start, end, color, cartArrowLineWidth);
    }

    private LineRenderer EnsureRailArrowRenderer(int index)
    {
        EnsureDebugRoot();
        while (railArrowRenderers.Count <= index)
        {
            GameObject lineObject = new GameObject("Rail_Path_Direction_Debug");
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
            lineRenderer.sortingOrder = 6500;
            railArrowRenderers.Add(lineRenderer);
        }

        return railArrowRenderers[index];
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

    private LineRenderer EnsurePowerMarkerRenderer(int index)
    {
        EnsureDebugRoot();
        while (powerMarkerRenderers.Count <= index)
        {
            GameObject lineObject = new GameObject("Rail_AutoDrive_Power_Source_Debug");
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
            lineRenderer.sortingOrder = 6502;
            powerMarkerRenderers.Add(lineRenderer);
        }

        return powerMarkerRenderers[index];
    }

    private LineRenderer EnsureTargetStationMarkerRenderer(int index)
    {
        EnsureDebugRoot();
        while (targetStationMarkerRenderers.Count <= index)
        {
            GameObject lineObject = new GameObject("Rail_AutoDrive_Target_Station_Debug");
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
            lineRenderer.sortingOrder = 6503;
            targetStationMarkerRenderers.Add(lineRenderer);
        }

        return targetStationMarkerRenderers[index];
    }

    private LineRenderer EnsureRouteHighlightRenderer(int index)
    {
        EnsureDebugRoot();
        while (routeHighlightRenderers.Count <= index)
        {
            GameObject lineObject = new GameObject("Rail_Path_Highlight_Debug");
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
            lineRenderer.sortingOrder = 6499;
            routeHighlightRenderers.Add(lineRenderer);
        }

        return routeHighlightRenderers[index];
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

        DisableRouteHighlightRenderers();
        DisableCartArrowRenderers();
        DisableRailArrowRenderers();
        DisablePowerMarkerRenderers();
        DisableTargetStationMarkerRenderers();
    }

    private void DisableRouteHighlightRenderers(int startIndex = 0)
    {
        for (int i = Mathf.Max(0, startIndex); i < routeHighlightRenderers.Count; i++)
        {
            if (routeHighlightRenderers[i] != null)
            {
                routeHighlightRenderers[i].enabled = false;
            }
        }
    }

    private void DisableCartArrowRenderers(int startIndex = 0)
    {
        for (int i = Mathf.Max(0, startIndex); i < cartArrowRenderers.Count; i++)
        {
            if (cartArrowRenderers[i] != null)
            {
                cartArrowRenderers[i].enabled = false;
            }
        }
    }

    private void DisableRailArrowRenderers(int startIndex = 0)
    {
        for (int i = Mathf.Max(0, startIndex); i < railArrowRenderers.Count; i++)
        {
            if (railArrowRenderers[i] != null)
            {
                railArrowRenderers[i].enabled = false;
            }
        }
    }

    private void DisablePowerMarkerRenderers(int startIndex = 0)
    {
        for (int i = Mathf.Max(0, startIndex); i < powerMarkerRenderers.Count; i++)
        {
            if (powerMarkerRenderers[i] != null)
            {
                powerMarkerRenderers[i].enabled = false;
            }
        }
    }

    private void DisableTargetStationMarkerRenderers(int startIndex = 0)
    {
        for (int i = Mathf.Max(0, startIndex); i < targetStationMarkerRenderers.Count; i++)
        {
            if (targetStationMarkerRenderers[i] != null)
            {
                targetStationMarkerRenderers[i].enabled = false;
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

    private readonly struct RouteHighlightSegment
    {
        public RouteHighlightSegment(int railIndex, float startDistance, float endDistance)
        {
            RailIndex = railIndex;
            StartDistance = startDistance;
            EndDistance = endDistance;
        }

        public int RailIndex { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
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
        public Vector2 ConnectionStartPoint;
        public Vector2 ConnectionEndPoint;
        public Vector2[] Points { get; }
        public float Length;
        public int ComponentIndex;
    }
}
