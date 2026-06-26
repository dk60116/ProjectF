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
    private const float RouteGraphNodeMergeDistance = 0.12f;

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

    private readonly List<RailInfo> rails = new List<RailInfo>();
    private readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> routeHighlightRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> railArrowRenderers = new List<LineRenderer>();
    private readonly List<LineRenderer> cartArrowRenderers = new List<LineRenderer>();
    private readonly List<RailHandcar> activeHandcarScratch = new List<RailHandcar>(4);
    private readonly Queue<int> componentQueue = new Queue<int>();
    private readonly List<RouteHighlightSegment> routeHighlightSegmentScratch = new List<RouteHighlightSegment>(32);
    private readonly List<Trainstation> routeStationScratch = new List<Trainstation>(8);

    private Transform debugRoot;
    private Material lineMaterial;
    private bool isVisible;
    private bool isDirty = true;
    private float nextCartArrowRefreshTime;
    private int lastRouteSelectionTrainInstanceId;
    private string lastRouteSelectionTargetA = string.Empty;
    private string lastRouteSelectionTargetB = string.Empty;
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
            routeStationScratch.Clear();
            return;
        }

        int rendererIndex = 0;
        for (int i = 0; i < routeHighlightSegmentScratch.Count; i++)
        {
            LineRenderer lineRenderer = EnsureRouteHighlightRenderer(rendererIndex++);
            ApplyRouteHighlightLine(lineRenderer, routeHighlightSegmentScratch[i]);
        }

        DisableRouteHighlightRenderers(rendererIndex);
        routeStationScratch.Clear();
    }

    private bool TryCollectSelectedRouteSegments(List<RouteHighlightSegment> result)
    {
        if (result == null
            || !TrainFilter.TryGetActiveRouteSelection(
                out SteamTrain boundTrain,
                out string targetAStationName,
                out string targetBStationName)
            || boundTrain == null
            || string.IsNullOrWhiteSpace(targetAStationName)
            || string.IsNullOrWhiteSpace(targetBStationName))
        {
            return false;
        }

        routeStationScratch.Clear();
        Trainstation[] liveStations = FindObjectsOfType<Trainstation>(false);
        for (int i = 0; i < liveStations.Length; i++)
        {
            Trainstation station = liveStations[i];
            if (station == null || !station.gameObject.activeInHierarchy)
            {
                continue;
            }

            string stationName = station.StationName;
            if (string.Equals(stationName, targetAStationName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(stationName, targetBStationName, System.StringComparison.OrdinalIgnoreCase))
            {
                routeStationScratch.Add(station);
            }
        }

        if (!TryFindStationRouteEndpoint(targetAStationName, out RouteEndpoint startEndpoint)
            || !TryFindStationRouteEndpoint(targetBStationName, out RouteEndpoint endEndpoint))
        {
            return false;
        }

        return TryBuildRouteFromConnectionGraph(startEndpoint, endEndpoint, result);
    }

    private bool TryFindStationRouteEndpoint(string stationName, out RouteEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(stationName))
        {
            return false;
        }

        float bestSqrDistance = RailGroupConnectionDistance * RailGroupConnectionDistance;
        bool found = false;
        for (int i = 0; i < routeStationScratch.Count; i++)
        {
            Trainstation station = routeStationScratch[i];
            if (station == null
                || !string.Equals(station.StationName, stationName, System.StringComparison.OrdinalIgnoreCase)
                || !station.TryGetRailCoordinate(out Vector2Int railCoordinate))
            {
                continue;
            }

            Vector2 stationPoint = new Vector2(railCoordinate.x, railCoordinate.y);
            for (int railInfoIndex = 0; railInfoIndex < rails.Count; railInfoIndex++)
            {
                RailInfo rail = rails[railInfoIndex];
                if (rail == null
                    || rail.Rail == null
                    || !rail.Rail.TryFindNearestRenderedPathSample(
                        stationPoint,
                        out float distanceAlongPath,
                        out Vector2 pathPoint,
                        out _,
                        out float sqrDistance)
                    || sqrDistance > bestSqrDistance)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                endpoint = new RouteEndpoint(railInfoIndex, distanceAlongPath, pathPoint);
                found = true;
            }
        }

        return found;
    }

    private bool TryBuildRouteFromConnectionGraph(
        RouteEndpoint startEndpoint,
        RouteEndpoint endEndpoint,
        List<RouteHighlightSegment> result)
    {
        result.Clear();

        List<RouteGraphNode> graphNodes = new List<RouteGraphNode>(Mathf.Max(4, rails.Count + 2));
        Dictionary<int, List<RouteGraphNodeRef>> railRefsByRail = new Dictionary<int, List<RouteGraphNodeRef>>();
        int startNodeIndex = GetOrCreateRouteGraphNode(graphNodes, startEndpoint.Point, true, false);
        AddRouteGraphNodeRef(graphNodes, railRefsByRail, startNodeIndex, startEndpoint.RailIndex, startEndpoint.DistanceAlongPath);
        int endNodeIndex = GetOrCreateRouteGraphNode(graphNodes, endEndpoint.Point, false, true);
        AddRouteGraphNodeRef(graphNodes, railRefsByRail, endNodeIndex, endEndpoint.RailIndex, endEndpoint.DistanceAlongPath);

        float effectiveConnectionDistance = Mathf.Max(connectionDistance, RailGroupConnectionDistance);
        float maxConnectionSqrDistance = effectiveConnectionDistance * effectiveConnectionDistance;
        for (int leftRailIndex = 0; leftRailIndex < rails.Count; leftRailIndex++)
        {
            for (int rightRailIndex = leftRailIndex + 1; rightRailIndex < rails.Count; rightRailIndex++)
            {
                if (!TryResolveRouteConnectionBetweenRails(
                        leftRailIndex,
                        rightRailIndex,
                        maxConnectionSqrDistance,
                        out RouteConnection connection))
                {
                    continue;
                }

                int nodeIndex = GetOrCreateRouteGraphNode(graphNodes, connection.Point, false, false);
                AddRouteGraphNodeRef(graphNodes, railRefsByRail, nodeIndex, connection.LeftRailIndex, connection.LeftDistanceAlongPath);
                AddRouteGraphNodeRef(graphNodes, railRefsByRail, nodeIndex, connection.RightRailIndex, connection.RightDistanceAlongPath);
            }
        }

        if (!TryBuildRouteGraphAdjacency(graphNodes.Count, railRefsByRail, out List<RouteGraphEdge>[] adjacency))
        {
            return false;
        }

        return TryFindRouteGraphPath(adjacency, startNodeIndex, endNodeIndex, result);
    }

    private bool TryResolveRouteConnectionBetweenRails(
        int leftRailIndex,
        int rightRailIndex,
        float maxConnectionSqrDistance,
        out RouteConnection connection)
    {
        connection = default;
        if (leftRailIndex < 0
            || rightRailIndex < 0
            || leftRailIndex >= rails.Count
            || rightRailIndex >= rails.Count)
        {
            return false;
        }

        RailInfo leftRail = rails[leftRailIndex];
        RailInfo rightRail = rails[rightRailIndex];
        if (leftRail == null
            || rightRail == null
            || leftRail.Rail == null
            || rightRail.Rail == null)
        {
            return false;
        }

        bool found = false;
        float bestScore = float.PositiveInfinity;
        ConsiderRouteEndpointConnectionCandidate(
            leftRailIndex,
            rightRailIndex,
            leftRail.StartPoint,
            maxConnectionSqrDistance,
            ref found,
            ref bestScore,
            ref connection);
        ConsiderRouteEndpointConnectionCandidate(
            leftRailIndex,
            rightRailIndex,
            leftRail.EndPoint,
            maxConnectionSqrDistance,
            ref found,
            ref bestScore,
            ref connection);
        ConsiderRouteEndpointConnectionCandidate(
            leftRailIndex,
            rightRailIndex,
            rightRail.StartPoint,
            maxConnectionSqrDistance,
            ref found,
            ref bestScore,
            ref connection);
        ConsiderRouteEndpointConnectionCandidate(
            leftRailIndex,
            rightRailIndex,
            rightRail.EndPoint,
            maxConnectionSqrDistance,
            ref found,
            ref bestScore,
            ref connection);

        IReadOnlyList<Vector2Int> leftCoordinates = leftRail.Rail.RuntimeOccupiedCoordinates;
        IReadOnlyList<Vector2Int> rightCoordinates = rightRail.Rail.RuntimeOccupiedCoordinates;
        if (leftCoordinates != null && rightCoordinates != null)
        {
            for (int leftCoordinateIndex = 0; leftCoordinateIndex < leftCoordinates.Count; leftCoordinateIndex++)
            {
                for (int rightCoordinateIndex = 0; rightCoordinateIndex < rightCoordinates.Count; rightCoordinateIndex++)
                {
                    if (leftCoordinates[leftCoordinateIndex] != rightCoordinates[rightCoordinateIndex])
                    {
                        continue;
                    }

                    Vector2 sharedPoint = new Vector2(
                        leftCoordinates[leftCoordinateIndex].x,
                        leftCoordinates[leftCoordinateIndex].y);
                    ConsiderRouteConnectionCandidate(
                        leftRailIndex,
                        rightRailIndex,
                        sharedPoint,
                        maxConnectionSqrDistance,
                        ref found,
                        ref bestScore,
                        ref connection);
                }
            }
        }

        return found;
    }

    private void ConsiderRouteConnectionCandidate(
        int leftRailIndex,
        int rightRailIndex,
        Vector2 candidatePoint,
        float maxConnectionSqrDistance,
        ref bool found,
        ref float bestScore,
        ref RouteConnection bestConnection)
    {
        RailInfo leftRail = rails[leftRailIndex];
        RailInfo rightRail = rails[rightRailIndex];
        if (leftRail == null
            || rightRail == null
            || leftRail.Rail == null
            || rightRail.Rail == null
            || !leftRail.Rail.TryFindNearestRenderedPathSample(
                candidatePoint,
                out float leftDistanceAlongPath,
                out Vector2 leftPathPoint,
                out _,
                out float leftSqrDistance)
            || !rightRail.Rail.TryFindNearestRenderedPathSample(
                candidatePoint,
                out float rightDistanceAlongPath,
                out Vector2 rightPathPoint,
                out _,
                out float rightSqrDistance)
            || leftSqrDistance > maxConnectionSqrDistance
            || rightSqrDistance > maxConnectionSqrDistance)
        {
            return;
        }

        float score = leftSqrDistance + rightSqrDistance;
        if (found && score >= bestScore)
        {
            return;
        }

        found = true;
        bestScore = score;
        bestConnection = new RouteConnection(
            leftRailIndex,
            leftDistanceAlongPath,
            rightRailIndex,
            rightDistanceAlongPath,
            (leftPathPoint + rightPathPoint) * 0.5f);
    }

    private void ConsiderRouteEndpointConnectionCandidate(
        int leftRailIndex,
        int rightRailIndex,
        Vector2 candidatePoint,
        float maxConnectionSqrDistance,
        ref bool found,
        ref float bestScore,
        ref RouteConnection bestConnection)
    {
        RailInfo leftRail = rails[leftRailIndex];
        RailInfo rightRail = rails[rightRailIndex];
        if (leftRail == null
            || rightRail == null
            || leftRail.Rail == null
            || rightRail.Rail == null
            || !leftRail.Rail.TryFindNearestRenderedPathSample(
                candidatePoint,
                out float leftDistanceAlongPath,
                out Vector2 leftPathPoint,
                out _,
                out float leftSqrDistance)
            || !rightRail.Rail.TryFindNearestRenderedPathSample(
                candidatePoint,
                out float rightDistanceAlongPath,
                out Vector2 rightPathPoint,
                out _,
                out float rightSqrDistance)
            || leftSqrDistance > maxConnectionSqrDistance
            || rightSqrDistance > maxConnectionSqrDistance
            || !IsNearRailEndpoint(leftRail, leftDistanceAlongPath)
            || !IsNearRailEndpoint(rightRail, rightDistanceAlongPath))
        {
            return;
        }

        float score = leftSqrDistance + rightSqrDistance;
        if (found && score >= bestScore)
        {
            return;
        }

        found = true;
        bestScore = score;
        bestConnection = new RouteConnection(
            leftRailIndex,
            leftDistanceAlongPath,
            rightRailIndex,
            rightDistanceAlongPath,
            (leftPathPoint + rightPathPoint) * 0.5f);
    }

    private bool IsNearRailEndpoint(RailInfo rail, float distanceAlongPath)
    {
        if (rail == null)
        {
            return false;
        }

        float endpointTolerance = Mathf.Max(0.15f, sampleSpacing * 1.5f);
        return distanceAlongPath <= endpointTolerance
               || Mathf.Abs(rail.Length - distanceAlongPath) <= endpointTolerance;
    }

    private int GetOrCreateRouteGraphNode(
        List<RouteGraphNode> graphNodes,
        Vector2 point,
        bool isStart,
        bool isEnd)
    {
        if (graphNodes == null)
        {
            return -1;
        }

        float maxNodeMergeSqrDistance = RouteGraphNodeMergeDistance * RouteGraphNodeMergeDistance;
        for (int i = 0; i < graphNodes.Count; i++)
        {
            if ((graphNodes[i].Point - point).sqrMagnitude > maxNodeMergeSqrDistance)
            {
                continue;
            }

            graphNodes[i].IsStart |= isStart;
            graphNodes[i].IsEnd |= isEnd;
            return i;
        }

        graphNodes.Add(new RouteGraphNode(point, isStart, isEnd));
        return graphNodes.Count - 1;
    }

    private static void AddRouteGraphNodeRef(
        List<RouteGraphNode> graphNodes,
        Dictionary<int, List<RouteGraphNodeRef>> railRefsByRail,
        int nodeIndex,
        int railIndex,
        float distanceAlongPath)
    {
        if (graphNodes == null
            || railRefsByRail == null
            || nodeIndex < 0
            || nodeIndex >= graphNodes.Count
            || railIndex < 0)
        {
            return;
        }

        RouteGraphNode node = graphNodes[nodeIndex];
        float clampedDistance = Mathf.Max(0f, distanceAlongPath);
        for (int i = 0; i < node.RailRefs.Count; i++)
        {
            RouteGraphRailRef existingRef = node.RailRefs[i];
            if (existingRef.RailIndex == railIndex
                && Mathf.Abs(existingRef.DistanceAlongPath - clampedDistance) <= 0.01f)
            {
                return;
            }
        }

        node.RailRefs.Add(new RouteGraphRailRef(railIndex, clampedDistance));
        if (!railRefsByRail.TryGetValue(railIndex, out List<RouteGraphNodeRef> railRefs))
        {
            railRefs = new List<RouteGraphNodeRef>(4);
            railRefsByRail.Add(railIndex, railRefs);
        }

        railRefs.Add(new RouteGraphNodeRef(nodeIndex, clampedDistance));
    }

    private bool TryBuildRouteGraphAdjacency(
        int nodeCount,
        Dictionary<int, List<RouteGraphNodeRef>> railRefsByRail,
        out List<RouteGraphEdge>[] adjacency)
    {
        adjacency = null;
        if (nodeCount <= 0)
        {
            return false;
        }

        adjacency = new List<RouteGraphEdge>[nodeCount];
        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            adjacency[nodeIndex] = new List<RouteGraphEdge>(4);
        }

        if (railRefsByRail == null || railRefsByRail.Count <= 0)
        {
            return true;
        }

        foreach (KeyValuePair<int, List<RouteGraphNodeRef>> pair in railRefsByRail)
        {
            List<RouteGraphNodeRef> refs = pair.Value;
            if (refs == null || refs.Count <= 1)
            {
                continue;
            }

            refs.Sort((left, right) => left.DistanceAlongPath.CompareTo(right.DistanceAlongPath));
            for (int refIndex = 1; refIndex < refs.Count; refIndex++)
            {
                RouteGraphNodeRef previousRef = refs[refIndex - 1];
                RouteGraphNodeRef currentRef = refs[refIndex];
                if (previousRef.NodeIndex == currentRef.NodeIndex)
                {
                    continue;
                }

                float segmentLength = Mathf.Abs(currentRef.DistanceAlongPath - previousRef.DistanceAlongPath);
                if (segmentLength <= 0.0001f)
                {
                    continue;
                }

                adjacency[previousRef.NodeIndex].Add(
                    new RouteGraphEdge(
                        currentRef.NodeIndex,
                        pair.Key,
                        previousRef.DistanceAlongPath,
                        currentRef.DistanceAlongPath,
                        segmentLength));
                adjacency[currentRef.NodeIndex].Add(
                    new RouteGraphEdge(
                        previousRef.NodeIndex,
                        pair.Key,
                        currentRef.DistanceAlongPath,
                        previousRef.DistanceAlongPath,
                        segmentLength));
            }
        }

        return true;
    }

    private bool TryFindRouteGraphPath(
        List<RouteGraphEdge>[] adjacency,
        int startNodeIndex,
        int endNodeIndex,
        List<RouteHighlightSegment> result)
    {
        if (adjacency == null
            || result == null
            || startNodeIndex < 0
            || endNodeIndex < 0
            || startNodeIndex >= adjacency.Length
            || endNodeIndex >= adjacency.Length)
        {
            return false;
        }

        int nodeCount = adjacency.Length;
        float[] distances = new float[nodeCount];
        int[] previousNodes = new int[nodeCount];
        RouteGraphEdge[] previousEdges = new RouteGraphEdge[nodeCount];
        bool[] visited = new bool[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            distances[i] = float.PositiveInfinity;
            previousNodes[i] = -1;
        }

        distances[startNodeIndex] = 0f;
        for (int step = 0; step < nodeCount; step++)
        {
            int currentNodeIndex = -1;
            float currentDistance = float.PositiveInfinity;
            for (int i = 0; i < nodeCount; i++)
            {
                if (visited[i] || distances[i] >= currentDistance)
                {
                    continue;
                }

                currentDistance = distances[i];
                currentNodeIndex = i;
            }

            if (currentNodeIndex < 0)
            {
                break;
            }

            if (currentNodeIndex == endNodeIndex)
            {
                break;
            }

            visited[currentNodeIndex] = true;
            List<RouteGraphEdge> edges = adjacency[currentNodeIndex];
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                RouteGraphEdge edge = edges[edgeIndex];
                if (visited[edge.ToNodeIndex])
                {
                    continue;
                }

                float candidateDistance = currentDistance + Mathf.Max(0.01f, edge.Cost);
                if (candidateDistance >= distances[edge.ToNodeIndex])
                {
                    continue;
                }

                distances[edge.ToNodeIndex] = candidateDistance;
                previousNodes[edge.ToNodeIndex] = currentNodeIndex;
                previousEdges[edge.ToNodeIndex] = edge;
            }
        }

        if (float.IsPositiveInfinity(distances[endNodeIndex]))
        {
            return false;
        }

        List<RouteHighlightSegment> reversedSegments = new List<RouteHighlightSegment>(16);
        for (int currentNodeIndex = endNodeIndex;
             currentNodeIndex != startNodeIndex;
             currentNodeIndex = previousNodes[currentNodeIndex])
        {
            int previousNodeIndex = previousNodes[currentNodeIndex];
            if (previousNodeIndex < 0)
            {
                reversedSegments.Clear();
                return false;
            }

            RouteGraphEdge edge = previousEdges[currentNodeIndex];
            AppendRouteHighlightSegment(
                reversedSegments,
                edge.RailIndex,
                edge.StartDistanceAlongPath,
                edge.EndDistanceAlongPath);
        }

        result.Clear();
        for (int i = reversedSegments.Count - 1; i >= 0; i--)
        {
            AppendRouteHighlightSegment(
                result,
                reversedSegments[i].RailIndex,
                reversedSegments[i].StartDistance,
                reversedSegments[i].EndDistance);
        }

        return result.Count > 0;
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
            out string currentTargetAStationName,
            out string currentTargetBStationName);
        return currentTrainInstanceId != lastRouteSelectionTrainInstanceId
               || !string.Equals(currentTargetAStationName, lastRouteSelectionTargetA, System.StringComparison.OrdinalIgnoreCase)
               || !string.Equals(currentTargetBStationName, lastRouteSelectionTargetB, System.StringComparison.OrdinalIgnoreCase);
    }

    private void CacheRouteSelectionState()
    {
        CaptureRouteSelectionState(
            out lastRouteSelectionTrainInstanceId,
            out lastRouteSelectionTargetA,
            out lastRouteSelectionTargetB);
    }

    private static void CaptureRouteSelectionState(
        out int trainInstanceId,
        out string targetAStationName,
        out string targetBStationName)
    {
        trainInstanceId = 0;
        targetAStationName = string.Empty;
        targetBStationName = string.Empty;
        if (!TrainFilter.TryGetActiveRouteSelection(
                out SteamTrain train,
                out targetAStationName,
                out targetBStationName)
            || train == null)
        {
            return;
        }

        trainInstanceId = train.GetInstanceID();
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

    private readonly struct RouteEndpoint
    {
        public RouteEndpoint(int railIndex, float distanceAlongPath, Vector2 point)
        {
            RailIndex = railIndex;
            DistanceAlongPath = distanceAlongPath;
            Point = point;
        }

        public int RailIndex { get; }
        public float DistanceAlongPath { get; }
        public Vector2 Point { get; }
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

    private readonly struct RouteConnection
    {
        public RouteConnection(
            int leftRailIndex,
            float leftDistanceAlongPath,
            int rightRailIndex,
            float rightDistanceAlongPath,
            Vector2 point)
        {
            LeftRailIndex = leftRailIndex;
            LeftDistanceAlongPath = leftDistanceAlongPath;
            RightRailIndex = rightRailIndex;
            RightDistanceAlongPath = rightDistanceAlongPath;
            Point = point;
        }

        public int LeftRailIndex { get; }
        public float LeftDistanceAlongPath { get; }
        public int RightRailIndex { get; }
        public float RightDistanceAlongPath { get; }
        public Vector2 Point { get; }
    }

    private sealed class RouteGraphNode
    {
        public RouteGraphNode(Vector2 point, bool isStart, bool isEnd)
        {
            Point = point;
            IsStart = isStart;
            IsEnd = isEnd;
        }

        public Vector2 Point { get; }
        public bool IsStart { get; set; }
        public bool IsEnd { get; set; }
        public List<RouteGraphRailRef> RailRefs { get; } = new List<RouteGraphRailRef>(4);
    }

    private readonly struct RouteGraphRailRef
    {
        public RouteGraphRailRef(int railIndex, float distanceAlongPath)
        {
            RailIndex = railIndex;
            DistanceAlongPath = distanceAlongPath;
        }

        public int RailIndex { get; }
        public float DistanceAlongPath { get; }
    }

    private readonly struct RouteGraphNodeRef
    {
        public RouteGraphNodeRef(int nodeIndex, float distanceAlongPath)
        {
            NodeIndex = nodeIndex;
            DistanceAlongPath = distanceAlongPath;
        }

        public int NodeIndex { get; }
        public float DistanceAlongPath { get; }
    }

    private readonly struct RouteGraphEdge
    {
        public RouteGraphEdge(
            int toNodeIndex,
            int railIndex,
            float startDistanceAlongPath,
            float endDistanceAlongPath,
            float cost)
        {
            ToNodeIndex = toNodeIndex;
            RailIndex = railIndex;
            StartDistanceAlongPath = startDistanceAlongPath;
            EndDistanceAlongPath = endDistanceAlongPath;
            Cost = cost;
        }

        public int ToNodeIndex { get; }
        public int RailIndex { get; }
        public float StartDistanceAlongPath { get; }
        public float EndDistanceAlongPath { get; }
        public float Cost { get; }
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
