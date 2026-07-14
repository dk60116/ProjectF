using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Railload : InstallationObject
{
    public const int RailCellsPerItem = 3;
    public const float ConnectionEndpointSnapMaxDistance = 0.55f;

    private const int CurveSegmentCount = 12;
    private const float CurveRadius = 0.5f;
    private const int CenterPathSmoothingIterations = 2;
    private const float RailEndpointCellHalfExtent = 0.5f;
    private const float DefaultRailVisualThickness = 0.08f;
    private const string ToonCharacterShaderName = "Custom/ToonCharacter";
    private static readonly Color RailVisualColor = new Color(0.68f, 0.7f, 0.72f, 1f);
    private static readonly Color SleeperVisualColor = new Color(0.38f, 0.18f, 0.08f, 1f);

    [SerializeField, Min(0.01f)]
    private float railLineWidth = 0.04f;
    [SerializeField, Min(0.01f)]
    private float railHalfSpacing = 0.3f;
    [SerializeField, Min(0f)]
    private float railVisualHeight = 0.1f;
    [SerializeField, Min(0.001f)]
    private float railVisualThickness = DefaultRailVisualThickness;
    [SerializeField, Min(0.01f)]
    private float sleeperLength = 0.84f;
    [SerializeField, Min(0.01f)]
    private float sleeperWidth = 0.16f;
    [SerializeField, Min(0.01f)]
    private float sleeperSpacing = 0.55f;
    [SerializeField, Min(0.001f)]
    private float sleeperVisualThickness = 0.08f;

    private static Material railRuntimeMaterial;
    private static Material sleeperRuntimeMaterial;
    private MeshFilter railMeshFilter;
    private MeshRenderer railMeshRenderer;
    private Mesh railMesh;
    private MeshFilter sleeperMeshFilter;
    private MeshRenderer sleeperMeshRenderer;
    private Mesh sleeperMesh;
    private readonly List<Vector3> railVertices = new List<Vector3>(128);
    private readonly List<int> railTriangles = new List<int>(192);
    private readonly List<Vector3> sleeperVertices = new List<Vector3>(128);
    private readonly List<int> sleeperTriangles = new List<int>(192);
    private readonly List<Vector3> railCenterPath = new List<Vector3>(64);
    private readonly List<Vector2> renderedPathSamples = new List<Vector2>(64);
    private readonly List<float> renderedPathCumulativeDistances = new List<float>(64);
    private float renderedPathLength;
    private bool renderedPathCacheDirty = true;
    [SerializeField, HideInInspector]
    private List<Vector2> runtimeVisualPathPoints = new List<Vector2>(64);
    [SerializeField, HideInInspector]
    private bool runtimeVisualPathExtendsStart = true;
    [SerializeField, HideInInspector]
    private bool runtimeVisualPathExtendsEnd = true;
    [SerializeField, HideInInspector, Min(0)]
    private int runtimeRequiredItemCount;

    protected override void OnEnable()
    {
        base.OnEnable();
        RefreshRailVisual();
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        RefreshRailVisual();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        base.OnPlacementRuntimeCleared();
        runtimeVisualPathPoints?.Clear();
        runtimeVisualPathExtendsStart = true;
        runtimeVisualPathExtendsEnd = true;
        runtimeRequiredItemCount = 0;
        InvalidateRenderedPathCache();
        ClearRailVisual();
    }

    public IReadOnlyList<Vector2> RuntimeVisualPathPoints => runtimeVisualPathPoints;
    public bool RuntimeVisualPathExtendsStart => runtimeVisualPathExtendsStart;
    public bool RuntimeVisualPathExtendsEnd => runtimeVisualPathExtendsEnd;
    public int RequiredItemCount
    {
        get
        {
            if (runtimeRequiredItemCount > 0)
            {
                return runtimeRequiredItemCount;
            }

            int visualPathItemCount = RailloadInstallationController.ResolveRequiredItemCountFromVisualPath(
                runtimeVisualPathPoints,
                runtimeVisualPathExtendsStart,
                runtimeVisualPathExtendsEnd);
            if (visualPathItemCount > 0)
            {
                return visualPathItemCount;
            }

            int occupiedPathItemCount = ResolveRequiredItemCount(RuntimeOccupiedCoordinates);
            return occupiedPathItemCount > 0 ? occupiedPathItemCount : 1;
        }
    }

    public void ConfigureRequiredItemCount(int requiredItemCount)
    {
        runtimeRequiredItemCount = Mathf.Max(0, requiredItemCount);
    }

    public void ConfigureVisualPath(IReadOnlyList<Vector2> visualPathPoints)
    {
        ConfigureVisualPath(visualPathPoints, true, true);
    }

    public void ConfigureVisualPath(
        IReadOnlyList<Vector2> visualPathPoints,
        bool extendStartEndpoint,
        bool extendEndEndpoint)
    {
        if (runtimeVisualPathPoints == null)
        {
            runtimeVisualPathPoints = new List<Vector2>(64);
        }
        else
        {
            runtimeVisualPathPoints.Clear();
        }

        if (visualPathPoints != null)
        {
            for (int i = 0; i < visualPathPoints.Count; i++)
            {
                AddVisualPathPoint(runtimeVisualPathPoints, visualPathPoints[i]);
            }
        }

        runtimeVisualPathExtendsStart = extendStartEndpoint;
        runtimeVisualPathExtendsEnd = extendEndEndpoint;
        RefreshRailVisual();
        base.OnPlacementRuntimeChanged();
    }

    public List<Vector2> CopyVisualPathPoints()
    {
        return runtimeVisualPathPoints != null
            ? new List<Vector2>(runtimeVisualPathPoints)
            : new List<Vector2>();
    }

    public bool TryFindNearestPathPointAndTangent(
        Vector2 point,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        pathPoint = point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;

        return TryEnsureRenderedPathSamples()
            && TryFindNearestPointAndTangentOnPath(
                renderedPathSamples,
                point,
                out pathPoint,
                out tangent,
                out sqrDistance);
    }

    public bool TryFindNearestRenderedPathSample(
        Vector2 point,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        distanceAlongPath = 0f;
        pathPoint = point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;

        if (!TryEnsureRenderedPathSamples())
        {
            return false;
        }

        return TryFindNearestSampleOnPath(
            renderedPathSamples,
            renderedPathCumulativeDistances,
            point,
            out distanceAlongPath,
            out pathPoint,
            out tangent,
            out sqrDistance);
    }

    public bool TrySampleRenderedPath(
        float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        pathPoint = Vector2.zero;
        tangent = Vector2.zero;
        if (!TryEnsureRenderedPathSamples())
        {
            return false;
        }

        return TrySamplePathAtDistance(
            renderedPathSamples,
            renderedPathCumulativeDistances,
            renderedPathLength,
            distanceAlongPath,
            out pathPoint,
            out tangent);
    }

    public bool TryGetRenderedPathLength(out float length)
    {
        length = 0f;
        if (!TryEnsureRenderedPathSamples())
        {
            return false;
        }

        length = renderedPathLength;
        return length > 0.0001f;
    }

    public bool TryGetRenderedEndpointSample(
        bool startEndpoint,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        distanceAlongPath = 0f;
        pathPoint = Vector2.zero;
        tangent = Vector2.zero;
        if (!TryEnsureRenderedPathSamples()
            || renderedPathSamples.Count < 2)
        {
            return false;
        }

        if (startEndpoint)
        {
            distanceAlongPath = 0f;
            pathPoint = renderedPathSamples[0];
            tangent = renderedPathSamples[1] - renderedPathSamples[0];
        }
        else
        {
            int lastIndex = renderedPathSamples.Count - 1;
            distanceAlongPath = renderedPathLength;
            pathPoint = renderedPathSamples[lastIndex];
            tangent = renderedPathSamples[lastIndex] - renderedPathSamples[lastIndex - 1];
        }

        tangent = NormalizeFlatDirection(tangent);
        return tangent.sqrMagnitude > 0.0001f;
    }

    public static bool IsValidRailPath(IReadOnlyList<Vector2Int> coordinates)
    {
        if (coordinates == null
            || coordinates.Count < 2)
        {
            return false;
        }

        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            if (!seen.Add(coordinates[i]))
            {
                return false;
            }

            if (i > 0 && !AreAdjacentCardinalCells(coordinates[i - 1], coordinates[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static int ResolveRequiredItemCount(IReadOnlyList<Vector2Int> coordinates)
    {
        return IsValidRailPath(coordinates)
            ? (coordinates.Count + RailCellsPerItem - 1) / RailCellsPerItem
            : 0;
    }

    public void RefreshRailVisual()
    {
        InvalidateRenderedPathCache();
        EnsureRailMesh();
        railVertices.Clear();
        railTriangles.Clear();
        sleeperVertices.Clear();
        sleeperTriangles.Clear();

        IReadOnlyList<Vector2Int> coordinates = RuntimeOccupiedCoordinates;
        if (coordinates != null && coordinates.Count >= 2)
        {
            railCenterPath.Clear();
            if (runtimeVisualPathPoints != null && runtimeVisualPathPoints.Count >= 2)
            {
                BuildVisualCenterPath(
                    runtimeVisualPathPoints,
                    RuntimeAnchorCoordinate,
                    railVisualHeight,
                    railCenterPath,
                    runtimeVisualPathExtendsStart,
                    runtimeVisualPathExtendsEnd);
            }
            else
            {
                BuildCenterPath(coordinates, RuntimeAnchorCoordinate, railVisualHeight, railCenterPath);
            }

            if (railCenterPath.Count >= 2)
            {
                AddRailStrips(
                    railVertices,
                    railTriangles,
                    railCenterPath,
                    railLineWidth,
                    railHalfSpacing,
                    railVisualThickness,
                    false,
                    false);
                AddRailSleepers(
                    sleeperVertices,
                    sleeperTriangles,
                    railCenterPath,
                    ResolveSleeperTopHeight(railVisualHeight, railVisualThickness),
                    sleeperLength,
                    sleeperWidth,
                    sleeperSpacing,
                    sleeperVisualThickness);
            }
        }

        ApplyMesh(railMesh, railVertices, railTriangles);
        ApplyMesh(sleeperMesh, sleeperVertices, sleeperTriangles);
    }

    public static void AppendRailMesh(
        List<Vector3> vertices,
        List<int> triangles,
        IReadOnlyList<Vector2Int> coordinates,
        Vector2Int originCoordinate,
        float width,
        float halfSpacing,
        float height,
        float thickness = DefaultRailVisualThickness)
    {
        if (vertices == null || triangles == null || coordinates == null || coordinates.Count < 2)
        {
            return;
        }

        List<Vector3> centerPath = new List<Vector3>(coordinates.Count * 2);
        BuildCenterPath(coordinates, originCoordinate, height, centerPath);
        AddRailStrips(vertices, triangles, centerPath, width, halfSpacing, thickness);
    }

    public static void AppendRailVisualMesh(
        List<Vector3> vertices,
        List<int> triangles,
        IReadOnlyList<Vector2> visualPathPoints,
        Vector2Int originCoordinate,
        float width,
        float halfSpacing,
        float height,
        bool extendStartEndpoint = true,
        bool extendEndEndpoint = true,
        float thickness = DefaultRailVisualThickness)
    {
        if (vertices == null || triangles == null || visualPathPoints == null || visualPathPoints.Count < 2)
        {
            return;
        }

        List<Vector3> centerPath = new List<Vector3>(visualPathPoints.Count);
        BuildVisualCenterPath(
            visualPathPoints,
            originCoordinate,
            height,
            centerPath,
            extendStartEndpoint,
            extendEndEndpoint);
        AddRailStrips(
            vertices,
            triangles,
            centerPath,
            width,
            halfSpacing,
            thickness,
            false,
            false);
    }

    private void ClearRailVisual()
    {
        if (railMesh != null)
        {
            railMesh.Clear();
        }

        if (sleeperMesh != null)
        {
            sleeperMesh.Clear();
        }
    }

    private void EnsureRailMesh()
    {
        EnsureVisualMesh(
            ref railMeshFilter,
            ref railMeshRenderer,
            ref railMesh,
            "Railload_RailMesh",
            "Railload Rail Mesh",
            ResolveRailRuntimeMaterial());
        EnsureVisualMesh(
            ref sleeperMeshFilter,
            ref sleeperMeshRenderer,
            ref sleeperMesh,
            "Railload_SleeperMesh",
            "Railload Sleeper Mesh",
            ResolveSleeperRuntimeMaterial());
    }

    private void EnsureVisualMesh(
        ref MeshFilter meshFilter,
        ref MeshRenderer meshRenderer,
        ref Mesh mesh,
        string objectName,
        string meshName,
        Material material)
    {
        GameObject meshObject = meshFilter != null ? meshFilter.gameObject : null;
        if (meshObject == null)
        {
            Transform existing = transform.Find(objectName);
            meshObject = existing != null ? existing.gameObject : new GameObject(objectName);
            meshObject.transform.SetParent(transform, false);
            meshObject.transform.localPosition = Vector3.zero;
            meshObject.transform.localRotation = Quaternion.identity;
            meshObject.transform.localScale = Vector3.one;
        }

        if (!meshObject.TryGetComponent(out MeshFilter resolvedMeshFilter))
        {
            resolvedMeshFilter = meshObject.AddComponent<MeshFilter>();
        }

        if (!meshObject.TryGetComponent(out MeshRenderer resolvedMeshRenderer))
        {
            resolvedMeshRenderer = meshObject.AddComponent<MeshRenderer>();
        }

        if (mesh == null)
        {
            mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.MarkDynamic();
        }

        meshFilter = resolvedMeshFilter;
        meshRenderer = resolvedMeshRenderer;
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    private static void BuildCenterPath(
        IReadOnlyList<Vector2Int> coordinates,
        Vector2Int originCoordinate,
        float height,
        List<Vector3> pathPoints)
    {
        pathPoints.Clear();
        if (coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        AddPathPoint(pathPoints, CoordinateToLocalPoint(coordinates[0], originCoordinate, height));
        for (int i = 1; i + 1 < coordinates.Count; i++)
        {
            Vector2Int previousDirection = coordinates[i] - coordinates[i - 1];
            Vector2Int nextDirection = coordinates[i + 1] - coordinates[i];
            Vector3 center = CoordinateToLocalPoint(coordinates[i], originCoordinate, height);
            if (IsUnitCardinal(previousDirection)
                && IsUnitCardinal(nextDirection)
                && previousDirection != nextDirection
                && nextDirection != -previousDirection)
            {
                Vector3 entry = DirectionToLocal(previousDirection);
                Vector3 exit = DirectionToLocal(nextDirection);
                Vector3 curveStart = center - entry * CurveRadius;
                Vector3 curveEnd = center + exit * CurveRadius;
                AddPathPoint(pathPoints, curveStart);
                AddBezierPathPoints(pathPoints, curveStart, curveEnd, entry, exit);
                continue;
            }

            AddPathPoint(pathPoints, center);
        }

        AddPathPoint(pathPoints, CoordinateToLocalPoint(coordinates[coordinates.Count - 1], originCoordinate, height));
        SmoothCenterPath(pathPoints, CenterPathSmoothingIterations);
        ExtendPathEndpointsToCellEdges(pathPoints, true, true);
    }

    private static void BuildVisualCenterPath(
        IReadOnlyList<Vector2> visualPathPoints,
        Vector2Int originCoordinate,
        float height,
        List<Vector3> pathPoints,
        bool extendStartEndpoint,
        bool extendEndEndpoint)
    {
        pathPoints.Clear();
        if (visualPathPoints == null)
        {
            return;
        }

        for (int i = 0; i < visualPathPoints.Count; i++)
        {
            AddPathPoint(pathPoints, VisualPathPointToLocal(visualPathPoints[i], originCoordinate, height));
        }

        ExtendPathEndpointsToCellEdges(pathPoints, extendStartEndpoint, extendEndEndpoint);
    }

    private void InvalidateRenderedPathCache()
    {
        renderedPathCacheDirty = true;
        renderedPathLength = 0f;
    }

    private bool TryEnsureRenderedPathSamples()
    {
        if (!renderedPathCacheDirty)
        {
            return renderedPathSamples.Count >= 2;
        }

        renderedPathCacheDirty = false;
        renderedPathLength = 0f;
        renderedPathSamples.Clear();
        renderedPathCumulativeDistances.Clear();
        if (runtimeVisualPathPoints != null && runtimeVisualPathPoints.Count >= 2)
        {
            for (int i = 0; i < runtimeVisualPathPoints.Count; i++)
            {
                AddVisualPathPoint(renderedPathSamples, runtimeVisualPathPoints[i]);
            }

            ExtendPathEndpointsToCellEdges2D(
                renderedPathSamples,
                runtimeVisualPathExtendsStart,
                runtimeVisualPathExtendsEnd);
            RebuildRenderedPathDistanceCache();
            return renderedPathSamples.Count >= 2;
        }

        IReadOnlyList<Vector2Int> coordinates = RuntimeOccupiedCoordinates;
        if (coordinates == null || coordinates.Count < 2)
        {
            return false;
        }

        railCenterPath.Clear();
        BuildCenterPath(coordinates, RuntimeAnchorCoordinate, 0f, railCenterPath);
        for (int i = 0; i < railCenterPath.Count; i++)
        {
            Vector3 localPoint = railCenterPath[i];
            AddVisualPathPoint(
                renderedPathSamples,
                new Vector2(
                    localPoint.x + RuntimeAnchorCoordinate.x,
                    localPoint.z + RuntimeAnchorCoordinate.y));
        }

        RebuildRenderedPathDistanceCache();
        return renderedPathSamples.Count >= 2;
    }

    private void RebuildRenderedPathDistanceCache()
    {
        renderedPathCumulativeDistances.Clear();
        renderedPathLength = 0f;
        if (renderedPathSamples.Count <= 0)
        {
            return;
        }

        renderedPathCumulativeDistances.Add(0f);
        for (int i = 1; i < renderedPathSamples.Count; i++)
        {
            renderedPathLength += Vector2.Distance(
                renderedPathSamples[i - 1],
                renderedPathSamples[i]);
            renderedPathCumulativeDistances.Add(renderedPathLength);
        }
    }

    private static void ExtendPathEndpointsToCellEdges2D(
        List<Vector2> pathPoints,
        bool extendStartEndpoint,
        bool extendEndEndpoint)
    {
        if (pathPoints == null || pathPoints.Count < 2)
        {
            return;
        }

        if (extendStartEndpoint)
        {
            Vector2 startDirection = NormalizeFlatDirection(pathPoints[0] - pathPoints[1]);
            pathPoints[0] += startDirection * ResolveCellEdgeExtension(startDirection);
        }

        if (extendEndEndpoint)
        {
            int lastIndex = pathPoints.Count - 1;
            Vector2 endDirection = NormalizeFlatDirection(pathPoints[lastIndex] - pathPoints[lastIndex - 1]);
            pathPoints[lastIndex] += endDirection * ResolveCellEdgeExtension(endDirection);
        }
    }

    private static Vector2 NormalizeFlatDirection(Vector2 direction)
    {
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
    }

    private static void ExtendPathEndpointsToCellEdges(
        List<Vector3> pathPoints,
        bool extendStartEndpoint,
        bool extendEndEndpoint)
    {
        if (pathPoints == null || pathPoints.Count < 2)
        {
            return;
        }

        if (extendStartEndpoint)
        {
            Vector3 startDirection = FlattenDirection(pathPoints[0] - pathPoints[1]);
            pathPoints[0] += startDirection * ResolveCellEdgeExtension(startDirection);
        }

        if (extendEndEndpoint)
        {
            Vector3 endDirection = FlattenDirection(pathPoints[pathPoints.Count - 1] - pathPoints[pathPoints.Count - 2]);
            pathPoints[pathPoints.Count - 1] += endDirection * ResolveCellEdgeExtension(endDirection);
        }
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private static float ResolveCellEdgeExtension(Vector3 direction)
    {
        float maxAxis = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.z));
        return maxAxis > 0.0001f ? RailEndpointCellHalfExtent / maxAxis : 0f;
    }

    private static float ResolveCellEdgeExtension(Vector2 direction)
    {
        float maxAxis = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
        return maxAxis > 0.0001f ? RailEndpointCellHalfExtent / maxAxis : 0f;
    }

    private static void SmoothCenterPath(List<Vector3> pathPoints, int iterations)
    {
        if (pathPoints == null || pathPoints.Count < 3 || iterations <= 0)
        {
            return;
        }

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (pathPoints.Count < 3)
            {
                return;
            }

            List<Vector3> smoothedPoints = new List<Vector3>(pathPoints.Count * 2);
            smoothedPoints.Add(pathPoints[0]);
            for (int i = 0; i + 1 < pathPoints.Count; i++)
            {
                Vector3 current = pathPoints[i];
                Vector3 next = pathPoints[i + 1];
                smoothedPoints.Add(Vector3.Lerp(current, next, 0.25f));
                smoothedPoints.Add(Vector3.Lerp(current, next, 0.75f));
            }

            smoothedPoints.Add(pathPoints[pathPoints.Count - 1]);
            pathPoints.Clear();
            pathPoints.AddRange(smoothedPoints);
        }
    }

    private static void AddBezierPathPoints(
        List<Vector3> pathPoints,
        Vector3 curveStart,
        Vector3 curveEnd,
        Vector3 entryDirection,
        Vector3 exitDirection)
    {
        const float bezierK = 0.55228475f;
        float controlLength = CurveRadius * bezierK;
        Vector3 controlA = curveStart + entryDirection.normalized * controlLength;
        Vector3 controlB = curveEnd - exitDirection.normalized * controlLength;
        for (int i = 1; i <= CurveSegmentCount; i++)
        {
            float t = i / (float)CurveSegmentCount;
            AddPathPoint(pathPoints, EvaluateCubicBezier(curveStart, controlA, controlB, curveEnd, t));
        }
    }

    private static Vector3 EvaluateCubicBezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * inverse * a
               + 3f * inverse * inverse * t * b
               + 3f * inverse * t * t * c
               + t * t * t * d;
    }

    private static void AddRailStrips(
        List<Vector3> vertices,
        List<int> triangles,
        IReadOnlyList<Vector3> centerPath,
        float width,
        float halfSpacing,
        float thickness,
        bool capStart = false,
        bool capEnd = false)
    {
        AddRailStrip(vertices, triangles, centerPath, Mathf.Abs(halfSpacing), Mathf.Max(0.01f, width), thickness, capStart, capEnd);
        AddRailStrip(vertices, triangles, centerPath, -Mathf.Abs(halfSpacing), Mathf.Max(0.01f, width), thickness, capStart, capEnd);
    }

    private static void AddRailStrip(
        List<Vector3> vertices,
        List<int> triangles,
        IReadOnlyList<Vector3> centerPath,
        float trackSideOffset,
        float width,
        float thickness,
        bool capStart,
        bool capEnd)
    {
        if (centerPath == null || centerPath.Count < 2)
        {
            return;
        }

        float halfWidth = width * 0.5f;
        float clampedThickness = Mathf.Max(0.001f, thickness);
        Vector3 bottomOffset = Vector3.down * clampedThickness;
        int topVertexStart = vertices.Count;
        for (int i = 0; i < centerPath.Count; i++)
        {
            Vector3 side = ResolveFlatSide(ResolvePathTangent(centerPath, i));
            Vector3 railCenter = centerPath[i] + side * trackSideOffset;
            Vector3 widthOffset = side * halfWidth;
            vertices.Add(railCenter - widthOffset);
            vertices.Add(railCenter + widthOffset);
        }

        int outerSideVertexStart = vertices.Count;
        for (int i = 0; i < centerPath.Count; i++)
        {
            Vector3 side = ResolveFlatSide(ResolvePathTangent(centerPath, i));
            Vector3 railCenter = centerPath[i] + side * trackSideOffset;
            Vector3 outerTop = railCenter - side * halfWidth;
            vertices.Add(outerTop);
            vertices.Add(outerTop + bottomOffset);
        }

        int innerSideVertexStart = vertices.Count;
        for (int i = 0; i < centerPath.Count; i++)
        {
            Vector3 side = ResolveFlatSide(ResolvePathTangent(centerPath, i));
            Vector3 railCenter = centerPath[i] + side * trackSideOffset;
            Vector3 innerTop = railCenter + side * halfWidth;
            vertices.Add(innerTop);
            vertices.Add(innerTop + bottomOffset);
        }

        int bottomVertexStart = vertices.Count;
        for (int i = 0; i < centerPath.Count; i++)
        {
            Vector3 side = ResolveFlatSide(ResolvePathTangent(centerPath, i));
            Vector3 railCenter = centerPath[i] + side * trackSideOffset;
            Vector3 widthOffset = side * halfWidth;
            vertices.Add(railCenter - widthOffset + bottomOffset);
            vertices.Add(railCenter + widthOffset + bottomOffset);
        }

        for (int i = 0; i + 1 < centerPath.Count; i++)
        {
            int current = topVertexStart + i * 2;
            int next = current + 2;
            AddQuad(triangles, current, current + 1, next + 1, next);

            current = outerSideVertexStart + i * 2;
            next = current + 2;
            AddQuad(triangles, current + 1, current, next, next + 1);

            current = innerSideVertexStart + i * 2;
            next = current + 2;
            AddQuad(triangles, current, current + 1, next + 1, next);

            current = bottomVertexStart + i * 2;
            next = current + 2;
            AddQuad(triangles, current + 1, current, next, next + 1);
        }

        if (capStart)
        {
            AddRailStripCap(vertices, triangles, centerPath[0], ResolvePathTangent(centerPath, 0), trackSideOffset, halfWidth, clampedThickness, true);
        }

        if (capEnd)
        {
            int lastIndex = centerPath.Count - 1;
            AddRailStripCap(vertices, triangles, centerPath[lastIndex], ResolvePathTangent(centerPath, lastIndex), trackSideOffset, halfWidth, clampedThickness, false);
        }
    }

    private static void AddRailStripCap(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 pathPoint,
        Vector3 tangent,
        float trackSideOffset,
        float halfWidth,
        float thickness,
        bool isStartCap)
    {
        Vector3 side = ResolveFlatSide(tangent);
        Vector3 railCenter = pathPoint + side * trackSideOffset;
        Vector3 widthOffset = side * halfWidth;
        Vector3 bottomOffset = Vector3.down * thickness;
        Vector3 outerTop = railCenter - widthOffset;
        Vector3 innerTop = railCenter + widthOffset;
        int start = vertices.Count;
        vertices.Add(outerTop);
        vertices.Add(innerTop);
        vertices.Add(outerTop + bottomOffset);
        vertices.Add(innerTop + bottomOffset);

        if (isStartCap)
        {
            AddQuad(triangles, start + 2, start + 3, start + 1, start);
        }
        else
        {
            AddQuad(triangles, start, start + 1, start + 3, start + 2);
        }
    }

    private static void AddRailSleepers(
        List<Vector3> vertices,
        List<int> triangles,
        IReadOnlyList<Vector3> centerPath,
        float topHeight,
        float length,
        float width,
        float spacing,
        float thickness)
    {
        if (centerPath == null || centerPath.Count < 2)
        {
            return;
        }

        float pathLength = CalculateFlatPathLength(centerPath);
        if (pathLength <= 0.001f)
        {
            return;
        }

        float clampedSpacing = Mathf.Max(0.1f, spacing);
        int sleeperCount = Mathf.Max(1, Mathf.FloorToInt(pathLength / clampedSpacing) + 1);
        float endInset = sleeperCount == 1
            ? pathLength * 0.5f
            : Mathf.Min(clampedSpacing * 0.35f, pathLength * 0.25f);
        float usableLength = Mathf.Max(0f, pathLength - endInset * 2f);
        float resolvedSpacing = sleeperCount > 1 ? usableLength / (sleeperCount - 1) : 0f;

        for (int i = 0; i < sleeperCount; i++)
        {
            float distance = sleeperCount == 1 ? pathLength * 0.5f : endInset + resolvedSpacing * i;
            if (!TrySampleFlatPath(centerPath, distance, out Vector3 center, out Vector3 tangent))
            {
                continue;
            }

            center.y = topHeight;
            AddSleeperBox(
                vertices,
                triangles,
                center,
                tangent,
                Mathf.Max(0.01f, length),
                Mathf.Max(0.01f, width),
                Mathf.Max(0.001f, thickness));
        }
    }

    private static void AddSleeperBox(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 topCenter,
        Vector3 tangent,
        float length,
        float width,
        float thickness)
    {
        tangent.y = 0f;
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            tangent = Vector3.forward;
        }
        else
        {
            tangent.Normalize();
        }

        Vector3 side = ResolveFlatSide(tangent);
        Vector3 halfLength = side * (length * 0.5f);
        Vector3 halfWidth = tangent * (width * 0.5f);
        Vector3 bottomOffset = Vector3.down * thickness;
        Vector3 topA = topCenter - halfLength - halfWidth;
        Vector3 topB = topCenter + halfLength - halfWidth;
        Vector3 topC = topCenter + halfLength + halfWidth;
        Vector3 topD = topCenter - halfLength + halfWidth;

        int start = vertices.Count;
        vertices.Add(topA);
        vertices.Add(topB);
        vertices.Add(topC);
        vertices.Add(topD);
        vertices.Add(topA + bottomOffset);
        vertices.Add(topB + bottomOffset);
        vertices.Add(topC + bottomOffset);
        vertices.Add(topD + bottomOffset);

        AddQuad(triangles, start, start + 1, start + 2, start + 3);
        AddQuad(triangles, start + 4, start + 7, start + 6, start + 5);
        AddQuad(triangles, start + 4, start + 5, start + 1, start);
        AddQuad(triangles, start + 5, start + 6, start + 2, start + 1);
        AddQuad(triangles, start + 6, start + 7, start + 3, start + 2);
        AddQuad(triangles, start + 7, start + 4, start, start + 3);
    }

    private static float CalculateFlatPathLength(IReadOnlyList<Vector3> pathPoints)
    {
        float length = 0f;
        for (int i = 0; i + 1 < pathPoints.Count; i++)
        {
            Vector3 segment = pathPoints[i + 1] - pathPoints[i];
            segment.y = 0f;
            length += segment.magnitude;
        }

        return length;
    }

    private static bool TrySampleFlatPath(
        IReadOnlyList<Vector3> pathPoints,
        float distance,
        out Vector3 point,
        out Vector3 tangent)
    {
        point = Vector3.zero;
        tangent = Vector3.forward;
        float walked = 0f;
        for (int i = 0; i + 1 < pathPoints.Count; i++)
        {
            Vector3 current = pathPoints[i];
            Vector3 next = pathPoints[i + 1];
            Vector3 segment = next - current;
            segment.y = 0f;
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.0001f)
            {
                continue;
            }

            if (walked + segmentLength >= distance || i + 2 >= pathPoints.Count)
            {
                float t = Mathf.Clamp01((distance - walked) / segmentLength);
                point = Vector3.Lerp(current, next, t);
                tangent = segment / segmentLength;
                return true;
            }

            walked += segmentLength;
        }

        return false;
    }

    private static bool TryFindNearestPointAndTangentOnPath(
        IReadOnlyList<Vector2> pathPoints,
        Vector2 point,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        pathPoint = point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (pathPoints == null || pathPoints.Count < 2)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i + 1 < pathPoints.Count; i++)
        {
            found |= TryUpdateNearestPointAndTangent(
                pathPoints[i],
                pathPoints[i + 1],
                point,
                ref pathPoint,
                ref tangent,
                ref sqrDistance);
        }

        return found;
    }

    private static bool TryFindNearestSampleOnPath(
        IReadOnlyList<Vector2> pathPoints,
        IReadOnlyList<float> cumulativeDistances,
        Vector2 point,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        distanceAlongPath = 0f;
        pathPoint = point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (pathPoints == null
            || cumulativeDistances == null
            || pathPoints.Count < 2
            || cumulativeDistances.Count != pathPoints.Count)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i + 1 < pathPoints.Count; i++)
        {
            Vector2 segment = pathPoints[i + 1] - pathPoints[i];
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.0001f)
            {
                continue;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - pathPoints[i], segment) / (segmentLength * segmentLength));
            Vector2 candidatePoint = Vector2.Lerp(pathPoints[i], pathPoints[i + 1], t);
            float candidateSqrDistance = (point - candidatePoint).sqrMagnitude;
            if (candidateSqrDistance < sqrDistance)
            {
                distanceAlongPath = cumulativeDistances[i] + segmentLength * t;
                pathPoint = candidatePoint;
                tangent = segment / segmentLength;
                sqrDistance = candidateSqrDistance;
                found = true;
            }
        }

        return found;
    }

    private static bool TrySamplePathAtDistance(
        IReadOnlyList<Vector2> pathPoints,
        IReadOnlyList<float> cumulativeDistances,
        float pathLength,
        float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        pathPoint = Vector2.zero;
        tangent = Vector2.zero;
        if (pathPoints == null
            || cumulativeDistances == null
            || pathPoints.Count < 2
            || cumulativeDistances.Count != pathPoints.Count)
        {
            return false;
        }

        float targetDistance = Mathf.Clamp(distanceAlongPath, 0f, Mathf.Max(0f, pathLength));
        int endIndex = FindCumulativeDistanceLowerBound(cumulativeDistances, targetDistance);
        endIndex = Mathf.Clamp(endIndex, 1, pathPoints.Count - 1);
        int startIndex = endIndex - 1;
        float segmentLength = cumulativeDistances[endIndex] - cumulativeDistances[startIndex];
        while (segmentLength <= 0.0001f && endIndex > 1)
        {
            endIndex--;
            startIndex--;
            segmentLength = cumulativeDistances[endIndex] - cumulativeDistances[startIndex];
        }

        while (segmentLength <= 0.0001f && endIndex + 1 < pathPoints.Count)
        {
            startIndex = endIndex;
            endIndex++;
            segmentLength = cumulativeDistances[endIndex] - cumulativeDistances[startIndex];
        }

        if (segmentLength <= 0.0001f)
        {
            return false;
        }

        Vector2 segment = pathPoints[endIndex] - pathPoints[startIndex];
        float t = Mathf.Clamp01(
            (targetDistance - cumulativeDistances[startIndex]) / segmentLength);
        pathPoint = Vector2.Lerp(pathPoints[startIndex], pathPoints[endIndex], t);
        tangent = segment / segmentLength;
        return true;
    }

    private static int FindCumulativeDistanceLowerBound(
        IReadOnlyList<float> cumulativeDistances,
        float targetDistance)
    {
        int low = 0;
        int high = cumulativeDistances.Count;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (cumulativeDistances[middle] < targetDistance)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static bool TryFindNearestPointAndTangentOnCoordinatePath(
        IReadOnlyList<Vector2Int> coordinates,
        Vector2 point,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        pathPoint = point;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (coordinates == null || coordinates.Count < 2)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i + 1 < coordinates.Count; i++)
        {
            Vector2 start = new Vector2(coordinates[i].x, coordinates[i].y);
            Vector2 end = new Vector2(coordinates[i + 1].x, coordinates[i + 1].y);
            found |= TryUpdateNearestPointAndTangent(
                start,
                end,
                point,
                ref pathPoint,
                ref tangent,
                ref sqrDistance);
        }

        return found;
    }

    private static bool TryUpdateNearestPointAndTangent(
        Vector2 start,
        Vector2 end,
        Vector2 point,
        ref Vector2 nearestPoint,
        ref Vector2 nearestTangent,
        ref float nearestSqrDistance)
    {
        Vector2 segment = end - start;
        float segmentSqrLength = segment.sqrMagnitude;
        if (segmentSqrLength <= 0.0001f)
        {
            return false;
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentSqrLength);
        Vector2 candidatePoint = Vector2.Lerp(start, end, t);
        float candidateSqrDistance = (point - candidatePoint).sqrMagnitude;
        if (candidateSqrDistance >= nearestSqrDistance)
        {
            return true;
        }

        nearestPoint = candidatePoint;
        nearestTangent = segment.normalized;
        nearestSqrDistance = candidateSqrDistance;
        return true;
    }

    private static float ResolveSleeperTopHeight(float railHeight, float railThickness)
    {
        const float sleeperRailOverlap = 0.015f;
        return railHeight - Mathf.Max(0.001f, railThickness) + sleeperRailOverlap;
    }

    private static Vector3 ResolvePathTangent(IReadOnlyList<Vector3> pathPoints, int index)
    {
        Vector3 tangent;
        if (index <= 0)
        {
            tangent = pathPoints[1] - pathPoints[0];
        }
        else if (index + 1 >= pathPoints.Count)
        {
            tangent = pathPoints[pathPoints.Count - 1] - pathPoints[pathPoints.Count - 2];
        }
        else
        {
            tangent = pathPoints[index + 1] - pathPoints[index - 1];
        }

        tangent.y = 0f;
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
    }

    private static Vector3 ResolveFlatSide(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector3.right;
        }

        direction.Normalize();
        return new Vector3(-direction.z, 0f, direction.x);
    }

    private static Vector3 CoordinateToLocalPoint(Vector2Int coordinate, Vector2Int originCoordinate, float height)
    {
        return new Vector3(coordinate.x - originCoordinate.x, height, coordinate.y - originCoordinate.y);
    }

    private static Vector3 VisualPathPointToLocal(Vector2 point, Vector2Int originCoordinate, float height)
    {
        return new Vector3(point.x - originCoordinate.x, height, point.y - originCoordinate.y);
    }

    private static Vector3 DirectionToLocal(Vector2Int direction)
    {
        direction = NormalizeCardinalDirection(direction);
        return new Vector3(direction.x, 0f, direction.y);
    }

    public static Vector2Int NormalizeCardinalDirection(Vector2Int direction)
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

    private static bool AreAdjacentCardinalCells(Vector2Int first, Vector2Int second)
    {
        return IsUnitCardinal(second - first);
    }

    private static bool IsUnitCardinal(Vector2Int direction)
    {
        return Mathf.Abs(direction.x) + Mathf.Abs(direction.y) == 1;
    }

    private static void AddPathPoint(List<Vector3> pathPoints, Vector3 point)
    {
        if (pathPoints.Count > 0 && (pathPoints[pathPoints.Count - 1] - point).sqrMagnitude <= 0.0001f)
        {
            return;
        }

        pathPoints.Add(point);
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

    private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);
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

    private static Material ResolveRailRuntimeMaterial()
    {
        if (railRuntimeMaterial != null)
        {
            return railRuntimeMaterial;
        }

        railRuntimeMaterial = CreateToonRuntimeMaterial(
            "Railload Runtime Rail Material",
            RailVisualColor,
            new Color(0.34f, 0.36f, 0.38f, 1f),
            true);
        return railRuntimeMaterial;
    }

    private static Material ResolveSleeperRuntimeMaterial()
    {
        if (sleeperRuntimeMaterial != null)
        {
            return sleeperRuntimeMaterial;
        }

        sleeperRuntimeMaterial = CreateToonRuntimeMaterial(
            "Railload Runtime Sleeper Material",
            SleeperVisualColor,
            new Color(0.18f, 0.08f, 0.035f, 1f),
            false);
        return sleeperRuntimeMaterial;
    }

    private static Material CreateToonRuntimeMaterial(
        string materialName,
        Color baseColor,
        Color shadowColor,
        bool useSpecular)
    {
        Shader shader = Shader.Find(ToonCharacterShaderName)
                        ?? Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = materialName,
            color = baseColor
        };
        SetMaterialColor(material, "_BaseColor", baseColor);
        SetMaterialColor(material, "_Color", baseColor);
        SetMaterialColor(material, "_ShadowColor", shadowColor);
        SetMaterialFloat(material, "_UseSpecular", useSpecular ? 1f : 0f);
        SetMaterialFloat(material, "_SpecularIntensity", useSpecular ? 0.35f : 0f);
        SetMaterialFloat(material, "_SpecularPower", 32f);
        return material;
    }

    private static void SetMaterialColor(Material material, string propertyName, Color color)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, color);
        }
    }

    private static void SetMaterialFloat(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }
}
