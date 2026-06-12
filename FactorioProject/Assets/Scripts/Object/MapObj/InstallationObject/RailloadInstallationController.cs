using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class RailloadInstallationController : MonoBehaviour
{
    private const float PreviewRailWidth = 0.04f;
    private const float PreviewRailHalfSpacing = 0.3f;
    private const float PreviewRailHeight = 0.16f;
    private const float PreviewRailThickness = 0.08f;
    private const float VisualBezierSamplesPerCell = 10f;
    private const float VisualBezierHandleFactor = 0.55f;
    private const float VisualBezierCrossAxisHandleFactor = 0.18f;
    private const float VisualBezierMinHandleLength = 0.35f;
    private const float ConnectionEndpointSnapMaxDistance = 0.55f;
    private const float ConnectionAngleScoreEpsilon = 0.001f;
    private const float ConnectionSideScoreWeight = 2f;
    private const float ConnectionSideEpsilon = 0.05f;
    private const float StraightEndpointConnectionMinDot = 0.75f;
    private const float GridTraversalEpsilon = 0.0001f;

    private static readonly Color ValidRailColor = new Color(0.07f, 0.82f, 1f, 0.82f);
    private static readonly Color InvalidRailColor = new Color(1f, 0.12f, 0.08f, 0.82f);
    private static readonly Vector2Int[] ConnectionProbeOffsets =
    {
        Vector2Int.zero,
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, -1)
    };
    private static Material previewRailMaterial;

    private sealed class RailPathPlan
    {
        public readonly List<Vector2Int> pathCoordinates = new List<Vector2Int>(32);
        public readonly List<Vector2Int> occupiedCoordinates = new List<Vector2Int>(32);
        public readonly List<Vector2> visualPathPoints = new List<Vector2>(64);
        public bool extendStartEndpoint = true;
        public bool extendEndEndpoint = true;
        public bool isPathValid;
        public bool isValid;
        public int requiredItemCount;
    }

    private InstallationPlacementController placementController;
    private ItemDefinition railloadDefinition;
    private Railload railloadPrefab;
    private TerrainGenerator terrain;
    private bool isActive;
    private bool hasStartCoordinate;
    private bool preferHorizontalFirst = true;
    private Vector2Int startCoordinate;
    private RailPathPlan currentPlan;

    private GameObject previewObject;
    private MeshFilter previewMeshFilter;
    private MeshRenderer previewMeshRenderer;
    private Mesh previewMesh;
    private readonly List<Vector3> previewVertices = new List<Vector3>(256);
    private readonly List<int> previewTriangles = new List<int>(384);

    public bool IsActive => isActive;

    public void Initialize(InstallationPlacementController controller)
    {
        placementController = controller;
    }

    public void Begin(InstallationPlacementController controller, ItemDefinition definition)
    {
        Initialize(controller);
        railloadDefinition = definition;
        railloadPrefab = definition != null ? definition.mapObject as Railload : null;
        terrain = TerrainGenerator.ResolveActive();
        isActive = railloadPrefab != null && placementController != null;
        hasStartCoordinate = false;
        currentPlan = null;
        SetPreviewVisible(false);
    }

    public bool Tick()
    {
        if (!isActive)
        {
            return false;
        }

        terrain = TerrainGenerator.ResolveActive();
        if (TryGetPrimaryPointerDown(out Vector2 pointerPosition)
            && placementController != null
            && !placementController.IsPlacementPointerOverBlockingUi(pointerPosition)
            && placementController.TryGetPlacementPointerBlock(pointerPosition, out Block clickedBlock)
            && clickedBlock != null)
        {
            if (!hasStartCoordinate)
            {
                startCoordinate = clickedBlock.Coordinate;
                hasStartCoordinate = true;
            }
            else
            {
                currentPlan = BuildBestPlan(startCoordinate, clickedBlock.Coordinate);
                RefreshPreviewMesh(currentPlan);
                if (CommitCurrentPreview(false))
                {
                    return true;
                }
            }
        }

        RefreshCurrentPreview();
        return false;
    }

    public void ToggleBendPriority()
    {
        preferHorizontalFirst = !preferHorizontalFirst;
        RefreshCurrentPreview();
    }

    public bool CommitCurrentPreview(bool refreshPreview = true)
    {
        if (!isActive)
        {
            return false;
        }

        if (refreshPreview)
        {
            RefreshCurrentPreview();
        }

        if (currentPlan == null || !currentPlan.isValid || currentPlan.requiredItemCount <= 0)
        {
            return false;
        }

        terrain = TerrainGenerator.ResolveActive();
        if (terrain == null || railloadPrefab == null || railloadDefinition == null)
        {
            return false;
        }

        int availableCount = placementController != null
            ? placementController.GetAvailableInstallItemCount(railloadDefinition.id)
            : 0;
        if (availableCount < currentPlan.requiredItemCount)
        {
            return false;
        }

        InstallationObject instance = terrain.CreateInstallationObject(railloadPrefab, terrain.transform);
        Railload railload = instance as Railload;
        if (railload == null)
        {
            if (instance != null)
            {
                terrain.ReleaseInstallationObject(instance, railloadPrefab);
            }

            return false;
        }

        List<Vector2Int> pathCoordinates = new List<Vector2Int>(currentPlan.pathCoordinates);
        List<Vector2Int> occupiedCoordinates = currentPlan.occupiedCoordinates.Count > 0
            ? new List<Vector2Int>(currentPlan.occupiedCoordinates)
            : new List<Vector2Int>(currentPlan.pathCoordinates);
        List<Vector2> visualPathPoints = new List<Vector2>(currentPlan.visualPathPoints);
        int removedCount = placementController.RemoveInstallItemsFromPlayer(
            railloadDefinition.id,
            currentPlan.requiredItemCount);
        if (removedCount < currentPlan.requiredItemCount)
        {
            terrain.ReleaseInstallationObject(railload, railloadPrefab);
            return false;
        }

        Vector2Int anchorCoordinate = pathCoordinates[0];
        long placementSequence = InstallationObject.ClaimNextPlacementSequence();
        railload.transform.SetPositionAndRotation(
            placementController.GetInstalledObjectWorldPosition(anchorCoordinate, railloadPrefab, 0),
            Quaternion.identity);
        railload.ConfigurePlacementRuntime(anchorCoordinate, 0, occupiedCoordinates, placementSequence);
        railload.ConfigureVisualPath(visualPathPoints, currentPlan.extendStartEndpoint, currentPlan.extendEndEndpoint);

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (terrain.TryGetLoadedBlock(occupiedCoordinates[i], out Block block) && block != null)
            {
                block.SetMapObject(railload);
            }
        }

        terrain.RegisterLiveInstallationObject(railload);
        return true;
    }

    public void Cancel()
    {
        isActive = false;
        hasStartCoordinate = false;
        currentPlan = null;
        SetPreviewVisible(false);
    }

    private void RefreshCurrentPreview()
    {
        if (!isActive || !hasStartCoordinate || placementController == null)
        {
            SetPreviewVisible(false);
            return;
        }

        if (!placementController.TryGetPlacementPointerBlock(out Block pointerBlock) || pointerBlock == null)
        {
            SetPreviewVisible(false);
            return;
        }

        currentPlan = BuildBestPlan(startCoordinate, pointerBlock.Coordinate);
        RefreshPreviewMesh(currentPlan);
    }

    private RailPathPlan BuildBestPlan(Vector2Int start, Vector2Int end)
    {
        RailPathPlan preferredPlan = BuildPlan(start, end, preferHorizontalFirst);
        ValidatePlan(preferredPlan);
        RailPathPlan alternatePlan = BuildPlan(start, end, !preferHorizontalFirst);
        ValidatePlan(alternatePlan);

        if (!preferredPlan.isPathValid)
        {
            return alternatePlan.isPathValid ? alternatePlan : preferredPlan;
        }

        if (!alternatePlan.isPathValid)
        {
            return preferredPlan;
        }

        float preferredScore = ResolvePlanSelectionScore(preferredPlan, start, end);
        float alternateScore = ResolvePlanSelectionScore(alternatePlan, start, end);
        return alternateScore > preferredScore + ConnectionAngleScoreEpsilon
            ? alternatePlan
            : preferredPlan;
    }

    private RailPathPlan BuildPlan(Vector2Int start, Vector2Int end, bool horizontalFirst)
    {
        RailPathPlan plan = new RailPathPlan();
        plan.pathCoordinates.Add(start);
        AddVisualPathPoint(plan.visualPathPoints, new Vector2(start.x, start.y));
        if (start == end)
        {
            return plan;
        }

        if (start.x == end.x)
        {
            AppendLine(plan.pathCoordinates, start, end, false);
            RebuildStraightVisualPathFromCoordinates(
                plan.pathCoordinates,
                plan.visualPathPoints,
                out plan.extendStartEndpoint,
                out plan.extendEndEndpoint);
            SynchronizePlanCoordinatesToVisualPath(plan);
            return plan;
        }

        if (start.y == end.y)
        {
            AppendLine(plan.pathCoordinates, start, end, true);
            RebuildStraightVisualPathFromCoordinates(
                plan.pathCoordinates,
                plan.visualPathPoints,
                out plan.extendStartEndpoint,
                out plan.extendEndEndpoint);
            SynchronizePlanCoordinatesToVisualPath(plan);
            return plan;
        }

        AppendCardinalCornerRoute(plan.pathCoordinates, start, end, horizontalFirst);
        RebuildBezierVisualPathFromCoordinates(
            plan.pathCoordinates,
            plan.visualPathPoints,
            out plan.extendStartEndpoint,
            out plan.extendEndEndpoint);
        SynchronizePlanCoordinatesToVisualPath(plan);
        return plan;
    }

    private static void AppendCardinalCornerRoute(
        List<Vector2Int> coordinates,
        Vector2Int start,
        Vector2Int end,
        bool horizontalFirst)
    {
        if (coordinates == null || coordinates.Count <= 0 || start == end)
        {
            return;
        }

        Vector2Int corner = horizontalFirst
            ? new Vector2Int(end.x, start.y)
            : new Vector2Int(start.x, end.y);
        AppendLine(coordinates, start, corner, horizontalFirst);
        AppendLine(coordinates, corner, end, !horizontalFirst);
    }

    private static void SynchronizePlanCoordinatesToVisualPath(RailPathPlan plan)
    {
        if (plan == null || plan.visualPathPoints == null || plan.visualPathPoints.Count <= 0)
        {
            return;
        }

        List<Vector2> renderedCenterPath = new List<Vector2>(plan.visualPathPoints.Count);
        BuildRenderedVisualPath(
            plan.visualPathPoints,
            plan.extendStartEndpoint,
            plan.extendEndEndpoint,
            renderedCenterPath);

        plan.pathCoordinates.Clear();
        AddCoordinatesTraversedByPath(plan.pathCoordinates, renderedCenterPath, null);

        plan.occupiedCoordinates.Clear();
        AddRailFootprintCoordinates(plan.occupiedCoordinates, renderedCenterPath);
        if (plan.occupiedCoordinates.Count <= 0)
        {
            plan.occupiedCoordinates.AddRange(plan.pathCoordinates);
        }
    }

    private static void BuildRenderedVisualPath(
        IReadOnlyList<Vector2> visualPathPoints,
        bool extendStartEndpoint,
        bool extendEndEndpoint,
        List<Vector2> renderedPath)
    {
        if (renderedPath == null)
        {
            return;
        }

        renderedPath.Clear();
        if (visualPathPoints == null || visualPathPoints.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < visualPathPoints.Count; i++)
        {
            renderedPath.Add(visualPathPoints[i]);
        }

        if (renderedPath.Count < 2)
        {
            return;
        }

        if (extendStartEndpoint && TryNormalize(renderedPath[0] - renderedPath[1], out Vector2 startDirection))
        {
            renderedPath[0] += startDirection * Mathf.Max(
                0f,
                ResolveCellEdgeExtension(startDirection) - GridTraversalEpsilon);
        }

        int last = renderedPath.Count - 1;
        if (extendEndEndpoint && TryNormalize(renderedPath[last] - renderedPath[last - 1], out Vector2 endDirection))
        {
            renderedPath[last] += endDirection * Mathf.Max(
                0f,
                ResolveCellEdgeExtension(endDirection) - GridTraversalEpsilon);
        }
    }

    private static void AddCoordinatesTraversedByPath(
        List<Vector2Int> coordinates,
        IReadOnlyList<Vector2> pathPoints,
        HashSet<Vector2Int> uniqueCoordinates)
    {
        if (coordinates == null || pathPoints == null || pathPoints.Count <= 0)
        {
            return;
        }

        AddPathCoordinate(coordinates, VisualPointToCoordinate(pathPoints[0]), uniqueCoordinates);
        for (int i = 0; i + 1 < pathPoints.Count; i++)
        {
            AppendVisualSegmentCoordinates(
                coordinates,
                pathPoints[i],
                pathPoints[i + 1],
                uniqueCoordinates);
        }
    }

    private static void AddRailFootprintCoordinates(
        List<Vector2Int> coordinates,
        IReadOnlyList<Vector2> centerPath)
    {
        if (coordinates == null || centerPath == null || centerPath.Count <= 0)
        {
            return;
        }

        float railHalfWidth = PreviewRailWidth * 0.5f;
        float railHalfSpacing = Mathf.Abs(PreviewRailHalfSpacing);
        float[] offsets =
        {
            0f,
            railHalfSpacing,
            -railHalfSpacing,
            railHalfSpacing + railHalfWidth,
            railHalfSpacing - railHalfWidth,
            -railHalfSpacing - railHalfWidth,
            -railHalfSpacing + railHalfWidth
        };
        HashSet<Vector2Int> uniqueCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i + 1 < centerPath.Count; i++)
        {
            Vector2 delta = centerPath[i + 1] - centerPath[i];
            if (!TryNormalize(delta, out Vector2 direction))
            {
                continue;
            }

            Vector2 side = new Vector2(-direction.y, direction.x);
            for (int offsetIndex = 0; offsetIndex < offsets.Length; offsetIndex++)
            {
                Vector2 offset = side * offsets[offsetIndex];
                AppendVisualSegmentCoordinates(
                    coordinates,
                    centerPath[i] + offset,
                    centerPath[i + 1] + offset,
                    uniqueCoordinates);
            }
        }
    }

    private static void AppendVisualSegmentCoordinates(
        List<Vector2Int> coordinates,
        Vector2 from,
        Vector2 to,
        HashSet<Vector2Int> uniqueCoordinates)
    {
        if (coordinates == null)
        {
            return;
        }

        Vector2Int current = VisualPointToCoordinate(from);
        Vector2Int target = VisualPointToCoordinate(to);
        AddPathCoordinate(coordinates, current, uniqueCoordinates);
        if (current == target)
        {
            return;
        }

        Vector2 delta = to - from;
        int stepX = delta.x > 0f ? 1 : (delta.x < 0f ? -1 : 0);
        int stepY = delta.y > 0f ? 1 : (delta.y < 0f ? -1 : 0);
        float tMaxX = stepX != 0
            ? ResolveInitialGridBoundaryT(from.x, current.x, stepX, delta.x)
            : float.PositiveInfinity;
        float tMaxY = stepY != 0
            ? ResolveInitialGridBoundaryT(from.y, current.y, stepY, delta.y)
            : float.PositiveInfinity;
        float tDeltaX = stepX != 0 ? 1f / Mathf.Abs(delta.x) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? 1f / Mathf.Abs(delta.y) : float.PositiveInfinity;

        int guard = 0;
        while (current != target && guard++ < 2048)
        {
            if (tMaxX + GridTraversalEpsilon < tMaxY)
            {
                current.x += stepX;
                tMaxX += tDeltaX;
                AddPathCoordinate(coordinates, current, uniqueCoordinates);
                continue;
            }

            if (tMaxY + GridTraversalEpsilon < tMaxX)
            {
                current.y += stepY;
                tMaxY += tDeltaY;
                AddPathCoordinate(coordinates, current, uniqueCoordinates);
                continue;
            }

            if (stepX != 0)
            {
                current.x += stepX;
                AddPathCoordinate(coordinates, current, uniqueCoordinates);
            }

            if (stepY != 0)
            {
                current.y += stepY;
                AddPathCoordinate(coordinates, current, uniqueCoordinates);
            }

            tMaxX += tDeltaX;
            tMaxY += tDeltaY;
        }
    }

    private static float ResolveInitialGridBoundaryT(
        float coordinate,
        int cell,
        int step,
        float delta)
    {
        float boundary = cell + (step > 0 ? 0.5f : -0.5f);
        return (boundary - coordinate) / delta;
    }

    private static float ResolveCellEdgeExtension(Vector2 direction)
    {
        float maxAxis = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
        return maxAxis > 0.0001f ? 0.5f / maxAxis : 0f;
    }

    private static Vector2Int VisualPointToCoordinate(Vector2 point)
    {
        return new Vector2Int(
            Mathf.FloorToInt(point.x + 0.5f),
            Mathf.FloorToInt(point.y + 0.5f));
    }

    private static void AddPathCoordinate(
        List<Vector2Int> coordinates,
        Vector2Int coordinate,
        HashSet<Vector2Int> uniqueCoordinates = null)
    {
        if (coordinates == null)
        {
            return;
        }

        if (uniqueCoordinates != null && !uniqueCoordinates.Add(coordinate))
        {
            return;
        }

        if (coordinates.Count > 0 && coordinates[coordinates.Count - 1] == coordinate)
        {
            return;
        }

        coordinates.Add(coordinate);
    }

    private static void RebuildStraightVisualPathFromCoordinates(
        IReadOnlyList<Vector2Int> coordinates,
        List<Vector2> visualPathPoints,
        out bool extendStartEndpoint,
        out bool extendEndEndpoint)
    {
        extendStartEndpoint = true;
        extendEndEndpoint = true;
        if (visualPathPoints == null)
        {
            return;
        }

        visualPathPoints.Clear();
        if (coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        Vector2 start = CoordinateToVisualPoint(coordinates[0]);
        if (coordinates.Count < 2)
        {
            AddVisualPathPoint(visualPathPoints, start);
            return;
        }

        Vector2 end = CoordinateToVisualPoint(coordinates[coordinates.Count - 1]);
        Vector2 startDirection = DirectionToVisual(coordinates[1] - coordinates[0]);
        Vector2 endDirection = DirectionToVisual(coordinates[coordinates.Count - 1] - coordinates[coordinates.Count - 2]);
        bool startConnected = TryResolveAlignedEndpointConnection(
            coordinates[0],
            start,
            startDirection,
            out Vector2 connectedStart,
            out _);
        if (startConnected)
        {
            start = connectedStart;
        }

        bool endConnected = TryResolveAlignedEndpointConnection(
            coordinates[coordinates.Count - 1],
            end,
            endDirection,
            out Vector2 connectedEnd,
            out _);
        if (endConnected)
        {
            end = connectedEnd;
        }

        extendStartEndpoint = !startConnected;
        extendEndEndpoint = !endConnected;
        AddVisualPathPoint(visualPathPoints, start);
        AddVisualPathPoint(visualPathPoints, end);
    }

    private static void RebuildBezierVisualPathFromCoordinates(
        IReadOnlyList<Vector2Int> coordinates,
        List<Vector2> visualPathPoints,
        out bool extendStartEndpoint,
        out bool extendEndEndpoint)
    {
        extendStartEndpoint = true;
        extendEndEndpoint = true;
        if (visualPathPoints == null)
        {
            return;
        }

        visualPathPoints.Clear();
        if (coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        if (coordinates.Count < 2)
        {
            AddVisualPathPoint(visualPathPoints, CoordinateToVisualPoint(coordinates[0]));
            return;
        }

        Vector2 start = CoordinateToVisualPoint(coordinates[0]);
        Vector2 end = CoordinateToVisualPoint(coordinates[coordinates.Count - 1]);
        Vector2 startDirection = DirectionToVisual(coordinates[1] - coordinates[0]);
        Vector2 endDirection = DirectionToVisual(coordinates[coordinates.Count - 1] - coordinates[coordinates.Count - 2]);
        bool startConnected = TryResolveEndpointConnection(
            coordinates[0],
            start,
            startDirection,
            out Vector2 connectedStart,
            out Vector2 connectedStartDirection);
        if (startConnected)
        {
            start = connectedStart;
            startDirection = connectedStartDirection;
        }

        bool endConnected = TryResolveEndpointConnection(
            coordinates[coordinates.Count - 1],
            end,
            endDirection,
            out Vector2 connectedEnd,
            out Vector2 connectedEndDirection);
        if (endConnected)
        {
            end = connectedEnd;
            endDirection = connectedEndDirection;
        }

        extendStartEndpoint = !startConnected;
        extendEndEndpoint = !endConnected;
        Vector2 delta = end - start;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
        {
            return;
        }

        float startHandleLength = ResolveBezierHandleLength(delta, startDirection, distance);
        float endHandleLength = ResolveBezierHandleLength(delta, endDirection, distance);
        Vector2 controlA = start + startDirection * startHandleLength;
        Vector2 controlB = end - endDirection * endHandleLength;
        AddVisualPathPoint(visualPathPoints, start);
        int sampleCount = Mathf.Clamp(Mathf.CeilToInt(distance * VisualBezierSamplesPerCell), 16, 256);
        for (int i = 1; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            AddVisualPathPoint(visualPathPoints, EvaluateCubicBezier(start, controlA, controlB, end, t));
        }
    }

    private static float ResolveBezierHandleLength(Vector2 delta, Vector2 direction, float distance)
    {
        float axisLength = Mathf.Abs(Vector2.Dot(delta, direction));
        float crossAxisLength = Mathf.Abs(Vector2.Dot(delta, new Vector2(-direction.y, direction.x)));
        return Mathf.Clamp(
            axisLength * VisualBezierHandleFactor + crossAxisLength * VisualBezierCrossAxisHandleFactor,
            VisualBezierMinHandleLength,
            distance * 0.65f);
    }

    private static Vector2 EvaluateCubicBezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * inverse * a
               + 3f * inverse * inverse * t * b
               + 3f * inverse * t * t * c
               + t * t * t * d;
    }

    private static Vector2 CoordinateToVisualPoint(Vector2Int coordinate)
    {
        return new Vector2(coordinate.x, coordinate.y);
    }

    private static Vector2 DirectionToVisual(Vector2Int direction)
    {
        direction = NormalizeCardinalDirection(direction);
        return new Vector2(direction.x, direction.y);
    }

    private static Vector2Int NormalizeCardinalDirection(Vector2Int direction)
    {
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            return new Vector2Int(direction.x > 0 ? 1 : -1, 0);
        }

        if (direction.y != 0)
        {
            return new Vector2Int(0, direction.y > 0 ? 1 : -1);
        }

        return Vector2Int.zero;
    }

    private static bool IsUnitCardinal(Vector2Int direction)
    {
        return Mathf.Abs(direction.x) + Mathf.Abs(direction.y) == 1;
    }

    private static void AppendLine(List<Vector2Int> coordinates, Vector2Int from, Vector2Int to, bool horizontal)
    {
        int delta = horizontal ? to.x - from.x : to.y - from.y;
        int step = delta >= 0 ? 1 : -1;
        int count = Mathf.Abs(delta);
        for (int i = 1; i <= count; i++)
        {
            Vector2Int coordinate = horizontal
                ? new Vector2Int(from.x + step * i, from.y)
                : new Vector2Int(from.x, from.y + step * i);
            if (coordinates.Count <= 0 || coordinates[coordinates.Count - 1] != coordinate)
            {
                coordinates.Add(coordinate);
            }
        }
    }

    private static void AddVisualPathPoint(List<Vector2> pathPoints, Vector2 point)
    {
        if (pathPoints == null)
        {
            return;
        }

        if (pathPoints.Count > 0 && (pathPoints[pathPoints.Count - 1] - point).sqrMagnitude <= 0.0001f)
        {
            return;
        }

        pathPoints.Add(point);
    }

    private static bool TryResolveAlignedEndpointConnection(
        Vector2Int connectionCoordinate,
        Vector2 endpoint,
        Vector2 preferredDirection,
        out Vector2 connectionPoint,
        out Vector2 connectionDirection)
    {
        connectionPoint = endpoint;
        connectionDirection = preferredDirection;
        if (!TryResolveEndpointConnection(
                connectionCoordinate,
                endpoint,
                preferredDirection,
                out Vector2 resolvedPoint,
                out Vector2 resolvedDirection)
            || !TryNormalize(preferredDirection, out Vector2 normalizedPreferredDirection)
            || !TryNormalize(resolvedDirection, out Vector2 normalizedResolvedDirection)
            || Vector2.Dot(normalizedPreferredDirection, normalizedResolvedDirection) < StraightEndpointConnectionMinDot)
        {
            return false;
        }

        connectionPoint = resolvedPoint;
        connectionDirection = normalizedResolvedDirection;
        return true;
    }

    private static bool TryResolveEndpointConnection(
        Vector2Int connectionCoordinate,
        Vector2 endpoint,
        Vector2 preferredDirection,
        out Vector2 connectionPoint,
        out Vector2 connectionDirection)
    {
        connectionPoint = endpoint;
        connectionDirection = preferredDirection;
        if (!TryNormalize(preferredDirection, out Vector2 normalizedPreferredDirection))
        {
            return false;
        }

        float maxSqrDistance = ConnectionEndpointSnapMaxDistance * ConnectionEndpointSnapMaxDistance;
        float bestSqrDistance = float.MaxValue;
        Vector2 bestPoint = endpoint;
        Vector2 bestDirection = normalizedPreferredDirection;
        List<InstallationObject> candidates = new List<InstallationObject>(8);
        for (int offsetIndex = 0; offsetIndex < ConnectionProbeOffsets.Length; offsetIndex++)
        {
            candidates.Clear();
            InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                connectionCoordinate + ConnectionProbeOffsets[offsetIndex],
                candidates);
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (candidates[candidateIndex] is not Railload rail)
                {
                    continue;
                }

                if (TryFindNearestPointAndTangentOnRailVisualPath(
                        endpoint,
                        rail,
                        out Vector2 candidatePoint,
                        out Vector2 candidateDirection,
                        out float candidateSqrDistance)
                    && candidateSqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = candidateSqrDistance;
                    bestPoint = candidatePoint;
                    bestDirection = candidateDirection;
                }
            }
        }

        if (bestSqrDistance > maxSqrDistance)
        {
            return false;
        }

        if (Vector2.Dot(bestDirection, normalizedPreferredDirection) < 0f)
        {
            bestDirection = -bestDirection;
        }

        connectionPoint = bestPoint;
        connectionDirection = bestDirection;
        return true;
    }

    private static bool TryFindNearestRailGuide(
        Vector2 endpoint,
        out Vector2 guidePoint,
        out Vector2 guideTangent,
        out float sqrDistance)
    {
        guidePoint = endpoint;
        guideTangent = Vector2.zero;
        sqrDistance = float.MaxValue;

        Vector2Int connectionCoordinate = VisualPointToCoordinate(endpoint);
        List<InstallationObject> candidates = new List<InstallationObject>(8);
        for (int offsetIndex = 0; offsetIndex < ConnectionProbeOffsets.Length; offsetIndex++)
        {
            candidates.Clear();
            InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                connectionCoordinate + ConnectionProbeOffsets[offsetIndex],
                candidates);
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (candidates[candidateIndex] is not Railload rail)
                {
                    continue;
                }

                if (TryFindNearestPointAndTangentOnRailVisualPath(
                        endpoint,
                        rail,
                        out Vector2 candidatePoint,
                        out Vector2 candidateTangent,
                        out float candidateSqrDistance)
                    && candidateSqrDistance < sqrDistance)
                {
                    guidePoint = candidatePoint;
                    guideTangent = candidateTangent;
                    sqrDistance = candidateSqrDistance;
                }
            }
        }

        return sqrDistance <= ConnectionEndpointSnapMaxDistance * ConnectionEndpointSnapMaxDistance
               && TryNormalize(guideTangent, out guideTangent);
    }

    private static bool TryFindNearestPointAndTangentOnRailVisualPath(
        Vector2 point,
        Railload rail,
        out Vector2 guidePoint,
        out Vector2 guideTangent,
        out float sqrDistance)
    {
        guidePoint = point;
        guideTangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        IReadOnlyList<Vector2> visualPathPoints = rail != null ? rail.RuntimeVisualPathPoints : null;
        if (visualPathPoints != null && visualPathPoints.Count >= 2)
        {
            return TryFindNearestPointAndTangentOnPath(
                visualPathPoints,
                point,
                out guidePoint,
                out guideTangent,
                out sqrDistance);
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = rail != null ? rail.RuntimeOccupiedCoordinates : null;
        if (occupiedCoordinates == null || occupiedCoordinates.Count < 2)
        {
            return false;
        }

        List<Vector2> coordinatePath = new List<Vector2>(occupiedCoordinates.Count);
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            coordinatePath.Add(CoordinateToVisualPoint(occupiedCoordinates[i]));
        }

        return TryFindNearestPointAndTangentOnPath(
            coordinatePath,
            point,
            out guidePoint,
            out guideTangent,
            out sqrDistance);
    }

    private static bool TryFindNearestPointAndTangentOnPath(
        IReadOnlyList<Vector2> pathPoints,
        Vector2 point,
        out Vector2 guidePoint,
        out Vector2 guideTangent,
        out float sqrDistance)
    {
        guidePoint = point;
        guideTangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (pathPoints == null || pathPoints.Count < 2)
        {
            return false;
        }

        bool found = false;
        Vector2 bestDelta = Vector2.zero;
        for (int i = 0; i + 1 < pathPoints.Count; i++)
        {
            Vector2 delta = pathPoints[i + 1] - pathPoints[i];
            float lengthSqr = delta.sqrMagnitude;
            if (lengthSqr <= 0.0001f)
            {
                continue;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - pathPoints[i], delta) / lengthSqr);
            Vector2 closest = pathPoints[i] + delta * t;
            float candidateSqrDistance = (closest - point).sqrMagnitude;
            if (candidateSqrDistance >= sqrDistance)
            {
                continue;
            }

            sqrDistance = candidateSqrDistance;
            guidePoint = closest;
            bestDelta = delta;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        return TryNormalize(bestDelta, out guideTangent);
    }

    private static float ResolvePlanSelectionScore(RailPathPlan plan, Vector2Int start, Vector2Int end)
    {
        return ResolveConnectionAngleScore(plan, start, end)
               + ResolveConnectionSideScore(plan);
    }

    private static float ResolveConnectionAngleScore(RailPathPlan plan, Vector2Int start, Vector2Int end)
    {
        if (plan == null || plan.visualPathPoints == null || plan.visualPathPoints.Count < 2)
        {
            return 0f;
        }

        float score = 0f;
        if (TryGetPlanEndpointTangent(plan.visualPathPoints, true, out Vector2 startTangent))
        {
            score += ResolveExistingRailAngleScore(start, startTangent);
        }

        if (TryGetPlanEndpointTangent(plan.visualPathPoints, false, out Vector2 endTangent))
        {
            score += ResolveExistingRailAngleScore(end, endTangent);
        }

        return score;
    }

    private static float ResolveConnectionSideScore(RailPathPlan plan)
    {
        if (plan == null || plan.visualPathPoints == null || plan.visualPathPoints.Count < 3)
        {
            return 0f;
        }

        float score = 0f;
        if (!plan.extendStartEndpoint)
        {
            score += ResolveEndpointConnectionSideScore(plan.visualPathPoints, true);
        }

        if (!plan.extendEndEndpoint)
        {
            score += ResolveEndpointConnectionSideScore(plan.visualPathPoints, false);
        }

        return score;
    }

    private static float ResolveEndpointConnectionSideScore(
        IReadOnlyList<Vector2> visualPathPoints,
        bool startEndpoint)
    {
        int lastIndex = visualPathPoints.Count - 1;
        Vector2 endpoint = startEndpoint ? visualPathPoints[0] : visualPathPoints[lastIndex];
        Vector2 oppositeEndpoint = startEndpoint ? visualPathPoints[lastIndex] : visualPathPoints[0];
        if (!TryFindNearestRailGuide(
                endpoint,
                out Vector2 guidePoint,
                out Vector2 guideTangent,
                out _))
        {
            return 0f;
        }

        float targetSide = Cross2D(guideTangent, oppositeEndpoint - guidePoint);
        float pathSide = ResolveAveragePathSide(visualPathPoints, guidePoint, guideTangent);
        if (Mathf.Abs(targetSide) <= ConnectionSideEpsilon
            || Mathf.Abs(pathSide) <= ConnectionSideEpsilon)
        {
            return 0f;
        }

        bool sameSide = Mathf.Sign(targetSide) == Mathf.Sign(pathSide);
        float strength = Mathf.Clamp01(Mathf.Min(Mathf.Abs(targetSide), Mathf.Abs(pathSide)));
        return (sameSide ? 1f : -1f) * ConnectionSideScoreWeight * strength;
    }

    private static float ResolveAveragePathSide(
        IReadOnlyList<Vector2> visualPathPoints,
        Vector2 guidePoint,
        Vector2 guideTangent)
    {
        float sideSum = 0f;
        int sideCount = 0;
        for (int i = 0; i < visualPathPoints.Count; i++)
        {
            Vector2 fromGuide = visualPathPoints[i] - guidePoint;
            if (fromGuide.sqrMagnitude <= 0.01f)
            {
                continue;
            }

            float side = Cross2D(guideTangent, fromGuide);
            if (Mathf.Abs(side) <= ConnectionSideEpsilon)
            {
                continue;
            }

            sideSum += side;
            sideCount++;
        }

        return sideCount > 0 ? sideSum / sideCount : 0f;
    }

    private static bool TryGetPlanEndpointTangent(
        IReadOnlyList<Vector2> visualPathPoints,
        bool startEndpoint,
        out Vector2 tangent)
    {
        tangent = Vector2.zero;
        if (visualPathPoints == null || visualPathPoints.Count < 2)
        {
            return false;
        }

        Vector2 delta = startEndpoint
            ? visualPathPoints[1] - visualPathPoints[0]
            : visualPathPoints[visualPathPoints.Count - 1] - visualPathPoints[visualPathPoints.Count - 2];
        return TryNormalize(delta, out tangent);
    }

    private static float ResolveExistingRailAngleScore(Vector2Int connectionCoordinate, Vector2 planTangent)
    {
        if (!TryNormalize(planTangent, out Vector2 normalizedPlanTangent))
        {
            return 0f;
        }

        List<InstallationObject> candidates = new List<InstallationObject>(8);
        for (int i = 0; i < ConnectionProbeOffsets.Length; i++)
        {
            InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                connectionCoordinate + ConnectionProbeOffsets[i],
                candidates);
        }

        float bestScore = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] is not Railload rail)
            {
                continue;
            }

            bestScore = Mathf.Max(
                bestScore,
                ResolveRailAngleScoreAtConnection(rail, connectionCoordinate, normalizedPlanTangent));
        }

        return bestScore;
    }

    private static float ResolveRailAngleScoreAtConnection(
        Railload rail,
        Vector2Int connectionCoordinate,
        Vector2 planTangent)
    {
        if (rail == null)
        {
            return 0f;
        }

        float bestScore = 0f;
        if (TryGetRailEndpointTangent(rail, connectionCoordinate, true, out Vector2 startTangent))
        {
            bestScore = Mathf.Max(bestScore, Mathf.Abs(Vector2.Dot(planTangent, startTangent)));
        }

        if (TryGetRailEndpointTangent(rail, connectionCoordinate, false, out Vector2 endTangent))
        {
            bestScore = Mathf.Max(bestScore, Mathf.Abs(Vector2.Dot(planTangent, endTangent)));
        }

        if (bestScore <= 0f
            && TryGetNearestRailVisualTangent(rail, connectionCoordinate, out Vector2 nearestTangent))
        {
            bestScore = Mathf.Abs(Vector2.Dot(planTangent, nearestTangent));
        }

        return bestScore;
    }

    private static bool TryGetRailEndpointTangent(
        Railload rail,
        Vector2Int connectionCoordinate,
        bool startEndpoint,
        out Vector2 tangent)
    {
        tangent = Vector2.zero;
        IReadOnlyList<Vector2> visualPathPoints = rail != null ? rail.RuntimeVisualPathPoints : null;
        if (visualPathPoints != null && visualPathPoints.Count >= 2)
        {
            int visualEndpointIndex = startEndpoint ? 0 : visualPathPoints.Count - 1;
            if (ChebyshevDistance(VisualPointToCoordinate(visualPathPoints[visualEndpointIndex]), connectionCoordinate) > 1)
            {
                return false;
            }

            Vector2 visualDelta = startEndpoint
                ? visualPathPoints[1] - visualPathPoints[0]
                : visualPathPoints[visualPathPoints.Count - 1] - visualPathPoints[visualPathPoints.Count - 2];
            return TryNormalize(visualDelta, out tangent);
        }

        IReadOnlyList<Vector2Int> coordinates = rail != null ? rail.RuntimeOccupiedCoordinates : null;
        if (coordinates == null || coordinates.Count < 2)
        {
            return false;
        }

        int endpointIndex = startEndpoint ? 0 : coordinates.Count - 1;
        if (ChebyshevDistance(coordinates[endpointIndex], connectionCoordinate) > 1)
        {
            return false;
        }

        Vector2Int gridDelta = startEndpoint
            ? coordinates[1] - coordinates[0]
            : coordinates[coordinates.Count - 1] - coordinates[coordinates.Count - 2];
        return TryNormalize(new Vector2(gridDelta.x, gridDelta.y), out tangent);
    }

    private static bool TryGetNearestRailVisualTangent(
        Railload rail,
        Vector2Int connectionCoordinate,
        out Vector2 tangent)
    {
        tangent = Vector2.zero;
        IReadOnlyList<Vector2> visualPathPoints = rail != null ? rail.RuntimeVisualPathPoints : null;
        if (visualPathPoints != null && visualPathPoints.Count >= 2)
        {
            return TryGetNearestPathTangent(visualPathPoints, CoordinateToVisualPoint(connectionCoordinate), out tangent);
        }

        IReadOnlyList<Vector2Int> coordinates = rail != null ? rail.RuntimeOccupiedCoordinates : null;
        if (coordinates == null || coordinates.Count < 2)
        {
            return false;
        }

        List<Vector2> coordinatePath = new List<Vector2>(coordinates.Count);
        for (int i = 0; i < coordinates.Count; i++)
        {
            coordinatePath.Add(CoordinateToVisualPoint(coordinates[i]));
        }

        return TryGetNearestPathTangent(coordinatePath, CoordinateToVisualPoint(connectionCoordinate), out tangent);
    }

    private static bool TryGetNearestPathTangent(
        IReadOnlyList<Vector2> pathPoints,
        Vector2 point,
        out Vector2 tangent)
    {
        tangent = Vector2.zero;
        if (pathPoints == null || pathPoints.Count < 2)
        {
            return false;
        }

        const float maxSqrDistance = 1.0001f;
        float bestSqrDistance = float.MaxValue;
        Vector2 bestDelta = Vector2.zero;
        for (int i = 0; i + 1 < pathPoints.Count; i++)
        {
            Vector2 delta = pathPoints[i + 1] - pathPoints[i];
            float lengthSqr = delta.sqrMagnitude;
            if (lengthSqr <= 0.0001f)
            {
                continue;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - pathPoints[i], delta) / lengthSqr);
            Vector2 closest = pathPoints[i] + delta * t;
            float sqrDistance = (closest - point).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
            {
                continue;
            }

            bestSqrDistance = sqrDistance;
            bestDelta = delta;
        }

        return bestSqrDistance <= maxSqrDistance && TryNormalize(bestDelta, out tangent);
    }

    private static bool TryNormalize(Vector2 value, out Vector2 normalized)
    {
        normalized = Vector2.zero;
        if (value.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        normalized = value.normalized;
        return true;
    }

    private static float Cross2D(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    private static int ChebyshevDistance(Vector2Int first, Vector2Int second)
    {
        return Mathf.Max(Mathf.Abs(first.x - second.x), Mathf.Abs(first.y - second.y));
    }

    private void ValidatePlan(RailPathPlan plan)
    {
        if (plan != null)
        {
            plan.isPathValid = false;
            plan.isValid = false;
            plan.requiredItemCount = 0;
        }

        if (plan == null || !Railload.IsValidRailPath(plan.pathCoordinates))
        {
            return;
        }

        plan.requiredItemCount = Railload.ResolveRequiredItemCount(plan.pathCoordinates);
        if (plan.requiredItemCount <= 0
            || placementController == null)
        {
            return;
        }

        HashSet<Vector2Int> checkedCoordinates = new HashSet<Vector2Int>();
        IReadOnlyList<Vector2Int> occupiedCoordinates = plan.occupiedCoordinates.Count > 0
            ? plan.occupiedCoordinates
            : plan.pathCoordinates;
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = occupiedCoordinates[i];
            if (!checkedCoordinates.Add(coordinate)
                || !CanPlaceRailCoordinate(coordinate))
            {
                return;
            }
        }

        plan.isPathValid = true;
        plan.isValid = HasEnoughRailItems(plan);
    }

    private bool HasEnoughRailItems(RailPathPlan plan)
    {
        if (plan == null
            || plan.requiredItemCount <= 0
            || placementController == null
            || railloadDefinition == null)
        {
            return false;
        }

        return placementController.GetAvailableInstallItemCount(railloadDefinition.id) >= plan.requiredItemCount;
    }

    private bool CanPlaceRailCoordinate(Vector2Int coordinate)
    {
        terrain = TerrainGenerator.ResolveActive();
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || placementController == null)
        {
            return false;
        }

        if (placementController.CanPlaceInstalledObjectAt(coordinate, railloadPrefab, 0, null, true))
        {
            return true;
        }

        return block.MapObject is Railload;
    }

    private void RefreshPreviewMesh(RailPathPlan plan)
    {
        if (plan == null || plan.visualPathPoints.Count < 2)
        {
            SetPreviewVisible(false);
            return;
        }

        EnsurePreviewMesh();
        previewVertices.Clear();
        previewTriangles.Clear();
        Railload.AppendRailVisualMesh(
            previewVertices,
            previewTriangles,
            plan.visualPathPoints,
            Vector2Int.zero,
            PreviewRailWidth,
            PreviewRailHalfSpacing,
            PreviewRailHeight,
            plan.extendStartEndpoint,
            plan.extendEndEndpoint,
            PreviewRailThickness);
        ApplyMesh(previewMesh, previewVertices, previewTriangles);
        ApplyMaterialColor(previewMeshRenderer.sharedMaterial, plan.isValid ? ValidRailColor : InvalidRailColor);
        SetPreviewVisible(true);
    }

    private void EnsurePreviewMesh()
    {
        if (previewObject == null)
        {
            previewObject = new GameObject("Railload Placement Preview");
            TerrainGenerator resolvedTerrain = TerrainGenerator.ResolveActive();
            if (resolvedTerrain != null)
            {
                previewObject.transform.SetParent(resolvedTerrain.transform, false);
            }
        }

        if (!previewObject.TryGetComponent(out previewMeshFilter))
        {
            previewMeshFilter = previewObject.AddComponent<MeshFilter>();
        }

        if (!previewObject.TryGetComponent(out previewMeshRenderer))
        {
            previewMeshRenderer = previewObject.AddComponent<MeshRenderer>();
        }

        if (previewMesh == null)
        {
            previewMesh = new Mesh
            {
                name = "Railload Placement Preview Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            previewMesh.MarkDynamic();
        }

        previewMeshFilter.sharedMesh = previewMesh;
        previewMeshRenderer.sharedMaterial = ResolvePreviewRailMaterial();
        previewMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        previewMeshRenderer.receiveShadows = false;
        previewMeshRenderer.sortingOrder = 6500;
    }

    private void SetPreviewVisible(bool visible)
    {
        if (previewMeshRenderer != null)
        {
            previewMeshRenderer.enabled = visible;
        }
    }

    private static void ApplyMesh(Mesh mesh, List<Vector3> vertices, List<int> triangles)
    {
        if (mesh == null)
        {
            return;
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private static bool TryGetPrimaryPointerDown(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                pointerPosition = touch.position;
                return true;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            pointerPosition = Input.mousePosition;
            return true;
        }

        return false;
    }

    private static Material ResolvePreviewRailMaterial()
    {
        if (previewRailMaterial != null)
        {
            return previewRailMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");
        previewRailMaterial = new Material(shader)
        {
            name = "Railload Placement Preview Rail Material",
            color = ValidRailColor
        };
        ConfigureTransparentMaterial(previewRailMaterial);
        return previewRailMaterial;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", ValidRailColor);
        }

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static void ApplyMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
    }
}
