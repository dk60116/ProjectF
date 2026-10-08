using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class RailloadInstallationController : MonoBehaviour
{
    private const float PreviewRailWidth = 0.04f;
    private const float PreviewRailHalfSpacing = 0.3f;
    private const float PreviewRailHeight = 0.16f;
    private const float VisualBezierSamplesPerCell = 10f;
    private const float VisualBezierHandleFactor = 0.55f;
    private const float VisualBezierCrossAxisHandleFactor = 0.18f;
    private const float VisualBezierMinHandleLength = 0.35f;
    private const float VisualBezierEndpointLeadLength = 0.1f;
    private const float ConnectionAngleScoreEpsilon = 0.001f;
    private const float ConnectionSideScoreWeight = 2f;
    private const float ConnectionSideEpsilon = 0.05f;
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
    private static readonly List<ProjectF.Railway.IRailTarget> connectionCandidates = new List<ProjectF.Railway.IRailTarget>(8);

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
    private readonly List<Vector3> previewCenterPath = new List<Vector3>(64);

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
            placementController.RefundInstallItemsToPlayer(railloadDefinition.id, removedCount);
            return false;
        }

        Vector2Int anchorCoordinate = pathCoordinates[0];
        long placementSequence = InstallationObject.ClaimNextPlacementSequence();
        var state = new BlockStateStore.InstallationSaveState
        {
            itemId = railloadDefinition.id, anchorCoordinate = anchorCoordinate,
            placementSequence = placementSequence, occupiedCoordinates = occupiedCoordinates,
            railVisualPathPoints = visualPathPoints, railRequiredItemCount = currentPlan.requiredItemCount,
            railVisualPathExtendsStart = currentPlan.extendStartEndpoint, railVisualPathExtendsEnd = currentPlan.extendEndEndpoint,
            hasWorldPose = true, worldRotation = Quaternion.identity,
            worldPosition = placementController.GetInstalledObjectWorldPosition(anchorCoordinate, railloadPrefab, 0)
        };
        if (terrain.RegisterDataOnlyRailwayState(state, railloadPrefab, out _)) return true;
        placementController.RefundInstallItemsToPlayer(railloadDefinition.id, removedCount);
        return false;
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
        }
        else if (start.y == end.y)
        {
            AppendLine(plan.pathCoordinates, start, end, true);
        }
        else
        {
            AppendCardinalCornerRoute(plan.pathCoordinates, start, end, horizontalFirst);
        }
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

        TryBuildRailCoordinatesFromVisualPath(
            plan.visualPathPoints,
            plan.extendStartEndpoint,
            plan.extendEndEndpoint,
            plan.pathCoordinates,
            plan.occupiedCoordinates);
    }

    internal static int ResolveRequiredItemCountFromVisualPath(
        IReadOnlyList<Vector2> visualPathPoints,
        bool extendStartEndpoint,
        bool extendEndEndpoint)
    {
        if (visualPathPoints == null || visualPathPoints.Count <= 0)
        {
            return 0;
        }

        List<Vector2Int> pathCoordinates = new List<Vector2Int>(visualPathPoints.Count);
        TryBuildRailCoordinatesFromVisualPath(
            visualPathPoints,
            extendStartEndpoint,
            extendEndEndpoint,
            pathCoordinates,
            null);
        return Railload.ResolveRequiredItemCount(pathCoordinates);
    }

    internal static bool TryBuildRailCoordinatesFromVisualPath(
        IReadOnlyList<Vector2> visualPathPoints,
        bool extendStartEndpoint,
        bool extendEndEndpoint,
        List<Vector2Int> pathCoordinates,
        List<Vector2Int> occupiedCoordinates)
    {
        pathCoordinates?.Clear();
        occupiedCoordinates?.Clear();
        if ((pathCoordinates == null && occupiedCoordinates == null)
            || visualPathPoints == null
            || visualPathPoints.Count < 2)
        {
            return false;
        }

        List<Vector2> renderedCenterPath = new List<Vector2>(visualPathPoints.Count);
        BuildRenderedVisualPath(
            visualPathPoints,
            extendStartEndpoint,
            extendEndEndpoint,
            renderedCenterPath);
        if (renderedCenterPath.Count < 2)
        {
            return false;
        }

        if (pathCoordinates != null)
        {
            AddCoordinatesTraversedByPath(pathCoordinates, renderedCenterPath, null);
        }

        if (occupiedCoordinates != null)
        {
            AddRailFootprintCoordinates(occupiedCoordinates, renderedCenterPath);
            if (occupiedCoordinates.Count <= 0)
            {
                AddCoordinatesTraversedByPath(occupiedCoordinates, renderedCenterPath, null);
            }
        }

        return (pathCoordinates != null && pathCoordinates.Count > 0)
               || (occupiedCoordinates != null && occupiedCoordinates.Count > 0);
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
            true,
            end,
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
            false,
            start,
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

        float leadLength = Mathf.Min(VisualBezierEndpointLeadLength, distance * 0.1f);
        bool singleConnected = startConnected != endConnected;
        bool singleCorner = false;
        if (singleConnected)
        {
            Vector2 outwardDirection = startConnected ? startDirection : -endDirection;
            Vector2 towardFreeEndpoint = startConnected ? delta : -delta;
            Vector2 perpendicular = new Vector2(-outwardDirection.y, outwardDirection.x);
            float forward = Vector2.Dot(towardFreeEndpoint, outwardDirection);
            float lateral = Vector2.Dot(towardFreeEndpoint, perpendicular);
            singleCorner = forward > leadLength && Mathf.Abs(lateral) > GridTraversalEpsilon;
            // Only the connected end constrains heading. The free end follows
            // a single corner instead of retaining the grid-route's S bend.
            Vector2 freeDirection = singleCorner
                ? perpendicular * Mathf.Sign(lateral)
                : outwardDirection;
            if (startConnected) endDirection = freeDirection;
            else startDirection = -freeDirection;
        }

        AddVisualPathPoint(visualPathPoints, start);
        if (Mathf.Abs(Cross2D(delta, startDirection)) <= 0.0001f
            && Mathf.Abs(Cross2D(delta, endDirection)) <= 0.0001f
            && Vector2.Dot(delta, startDirection) > 0f && Vector2.Dot(delta, endDirection) > 0f)
        {
            AddVisualPathPoint(visualPathPoints, end);
            return;
        }

        // The rendered/sampled polyline needs an exact terminal tangent too,
        // not just the analytic Bezier derivative between two sampled points.
        Vector2 curveStart = startConnected || singleCorner ? start + startDirection * leadLength : start;
        Vector2 curveEnd = endConnected || singleCorner ? end - endDirection * leadLength : end;
        distance = (curveEnd - curveStart).magnitude;
        ResolveBezierControls(curveStart, curveEnd, startDirection, endDirection,
            startConnected, endConnected, out Vector2 controlA, out Vector2 controlB);
        AddVisualPathPoint(visualPathPoints, curveStart);
        int sampleCount = Mathf.Clamp(Mathf.CeilToInt(distance * VisualBezierSamplesPerCell), 16, 256);
        for (int i = 1; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            AddVisualPathPoint(visualPathPoints, EvaluateCubicBezier(curveStart, controlA, controlB, curveEnd, t));
        }
        AddVisualPathPoint(visualPathPoints, end);
    }

    private static void ResolveBezierControls(Vector2 start, Vector2 end, Vector2 startDirection,
        Vector2 endDirection, bool startConnected, bool endConnected, out Vector2 controlA, out Vector2 controlB)
    {
        Vector2 delta = end - start;
        float distance = delta.magnitude;
        if (startConnected == endConnected)
        {
            controlA = start + startDirection * ResolveBezierHandleLength(delta, startDirection, distance);
            controlB = end - endDirection * ResolveBezierHandleLength(delta, endDirection, distance);
            return;
        }

        // Build from the constrained end so reversed placement has identical
        // geometry. Forward/lateral handle bounds keep a corner from inflecting.
        Vector2 anchor = startConnected ? start : end;
        Vector2 freeEndpoint = startConnected ? end : start;
        Vector2 outward = startConnected ? startDirection : -endDirection;
        Vector2 towardFree = freeEndpoint - anchor;
        Vector2 perpendicular = new Vector2(-outward.y, outward.x);
        float forward = Vector2.Dot(towardFree, outward);
        float lateral = Vector2.Dot(towardFree, perpendicular);
        float handle = ResolveBezierHandleLength(towardFree, outward, distance);
        Vector2 connectedControl;
        Vector2 freeControl;
        if (forward > GridTraversalEpsilon && Mathf.Abs(lateral) > GridTraversalEpsilon)
        {
            Vector2 outgoing = perpendicular * Mathf.Sign(lateral);
            connectedControl = anchor + outward * Mathf.Min(handle, forward);
            freeControl = freeEndpoint - outgoing * Mathf.Min(
                ResolveBezierHandleLength(towardFree, outgoing, distance), Mathf.Abs(lateral));
        }
        else
        {
            connectedControl = anchor + outward * handle;
            freeControl = (connectedControl + freeEndpoint) * 0.5f;
        }
        controlA = startConnected ? connectedControl : freeControl;
        controlB = startConnected ? freeControl : connectedControl;
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

    private static bool TryResolveEndpointConnection(
        Vector2Int connectionCoordinate,
        Vector2 endpoint,
        Vector2 preferredDirection,
        bool startEndpoint,
        Vector2 oppositeEndpoint,
        out Vector2 connectionPoint,
        out Vector2 connectionDirection)
    {
        connectionPoint = endpoint;
        connectionDirection = preferredDirection;
        if (!TryNormalize(preferredDirection, out Vector2 normalizedPreferredDirection))
        {
            return false;
        }

        float maxSqrDistance = Railload.ConnectionEndpointSnapMaxDistance * Railload.ConnectionEndpointSnapMaxDistance;
        float bestSqrDistance = float.MaxValue;
        Vector2 bestPoint = endpoint;
        Vector2 bestDirection = normalizedPreferredDirection;
        float bestEndpointOutwardSign = 0f;
        var candidates = connectionCandidates; candidates.Clear();
        for (int offsetIndex = 0; offsetIndex < ConnectionProbeOffsets.Length; offsetIndex++)
        {
            candidates.Clear();
            TerrainGenerator.Active?.GetRailWorld().CollectRailsAtCoordinate(
                connectionCoordinate + ConnectionProbeOffsets[offsetIndex],
                candidates);
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (candidates[candidateIndex] is not ProjectF.Railway.IRailTarget rail)
                {
                    continue;
                }

                if (TryFindNearestPointAndTangentOnRailPath(
                        endpoint,
                        rail,
                        out Vector2 candidatePoint,
                        out Vector2 candidateDirection,
                        out float candidateSqrDistance,
                        out float endpointOutwardSign)
                    && candidateSqrDistance <= maxSqrDistance
                    && (endpointOutwardSign != 0f && bestEndpointOutwardSign == 0f
                        || (endpointOutwardSign != 0f) == (bestEndpointOutwardSign != 0f)
                            && candidateSqrDistance < bestSqrDistance))
                {
                    bestSqrDistance = candidateSqrDistance;
                    bestPoint = candidatePoint;
                    bestDirection = candidateDirection;
                    bestEndpointOutwardSign = endpointOutwardSign;
                }
            }
        }

        if (bestSqrDistance > maxSqrDistance)
        {
            return false;
        }

        if (bestEndpointOutwardSign != 0f)
        {
            // At an existing terminal the new rail leaves it at its start and
            // enters it at its end. Authored point order cannot choose this sign.
            bestDirection *= startEndpoint ? bestEndpointOutwardSign : -bestEndpointOutwardSign;
        }
        else
        {
            float directionDot = Vector2.Dot(bestDirection, normalizedPreferredDirection);
            if (Mathf.Abs(directionDot) <= 0.0001f)
            {
                Vector2 alongPlan = startEndpoint ? oppositeEndpoint - bestPoint : bestPoint - oppositeEndpoint;
                directionDot = Vector2.Dot(bestDirection, alongPlan);
                if (Mathf.Abs(directionDot) <= 0.0001f)
                    directionDot = Mathf.Abs(bestDirection.x) > 0.0001f ? bestDirection.x : bestDirection.y;
            }
            if (directionDot < 0f) bestDirection = -bestDirection;
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
        var candidates = connectionCandidates; candidates.Clear();
        for (int offsetIndex = 0; offsetIndex < ConnectionProbeOffsets.Length; offsetIndex++)
        {
            candidates.Clear();
            TerrainGenerator.Active?.GetRailWorld().CollectRailsAtCoordinate(
                connectionCoordinate + ConnectionProbeOffsets[offsetIndex],
                candidates);
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (candidates[candidateIndex] is not ProjectF.Railway.IRailTarget rail)
                {
                    continue;
                }

                if (TryFindNearestPointAndTangentOnRailPath(
                        endpoint,
                        rail,
                        out Vector2 candidatePoint,
                        out Vector2 candidateTangent,
                        out float candidateSqrDistance,
                        out _)
                    && candidateSqrDistance < sqrDistance)
                {
                    guidePoint = candidatePoint;
                    guideTangent = candidateTangent;
                    sqrDistance = candidateSqrDistance;
                }
            }
        }

        return sqrDistance <= Railload.ConnectionEndpointSnapMaxDistance * Railload.ConnectionEndpointSnapMaxDistance
               && TryNormalize(guideTangent, out guideTangent);
    }

    private static bool TryFindNearestPointAndTangentOnRailPath(
        Vector2 point,
        ProjectF.Railway.IRailTarget rail,
        out Vector2 guidePoint,
        out Vector2 guideTangent,
        out float sqrDistance,
        out float endpointOutwardSign)
    {
        guidePoint = point;
        guideTangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        endpointOutwardSign = 0f;
        if (!rail.IsAlive() || !rail.TryFindNearestRenderedPathSample(point, out _,
                out guidePoint, out guideTangent, out sqrDistance))
        {
            return false;
        }

        // Cell-center clicks near an open end join its rendered terminal, not
        // the unextended source point or an overlapping interior projection.
        float maxSqrDistance = Railload.ConnectionEndpointSnapMaxDistance * Railload.ConnectionEndpointSnapMaxDistance;
        float endpointSqrDistance = float.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            if (!rail.TryGetRenderedEndpointSample(i == 0, out _, out Vector2 terminal, out Vector2 tangent)) continue;
            float candidateSqrDistance = (terminal - point).sqrMagnitude;
            if (candidateSqrDistance > maxSqrDistance || candidateSqrDistance >= endpointSqrDistance) continue;
            endpointSqrDistance = candidateSqrDistance;
            sqrDistance = candidateSqrDistance;
            guidePoint = terminal;
            guideTangent = tangent;
            endpointOutwardSign = i == 0 ? -1f : 1f;
        }
        return TryNormalize(guideTangent, out guideTangent);
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
        if (!TryNormalize(planTangent, out Vector2 normalizedPlanTangent)
            || !TryFindNearestRailGuide(CoordinateToVisualPoint(connectionCoordinate),
                out _, out Vector2 guideTangent, out _))
        {
            return 0f;
        }

        return Mathf.Abs(Vector2.Dot(normalizedPlanTangent, guideTangent));
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

        return block.MapObject is ProjectF.Railway.IRailTarget;
    }

    private void RefreshPreviewMesh(RailPathPlan plan)
    {
        if (plan == null || plan.visualPathPoints.Count < 2 || railloadPrefab == null)
        {
            SetPreviewVisible(false);
            return;
        }

        EnsurePreviewMesh();
        previewVertices.Clear();
        previewTriangles.Clear();
        railloadPrefab.AppendPlacementPreviewMesh(
            previewVertices,
            previewTriangles,
            plan.visualPathPoints,
            Vector2Int.zero,
            PreviewRailHeight,
            plan.extendStartEndpoint,
            plan.extendEndEndpoint,
            previewCenterPath);
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
        mesh.indexFormat = IndexFormat.UInt32;
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
