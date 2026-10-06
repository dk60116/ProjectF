using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Railway;

static class DebugRendererChecks
{
    static int checks;
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; }
    static BlockStateStore.InstallationSaveState State(long sequence, int start, int end)
    {
        var state = new BlockStateStore.InstallationSaveState { itemId = 1, placementSequence = sequence,
            anchorCoordinate = new Vector2Int(start, 0), worldPosition = new Vector3(start, 3, 0),
            railVisualPathPoints = new List<Vector2> { new Vector2(start, 0), new Vector2(end, 0) },
            occupiedCoordinates = new List<Vector2Int>() };
        for (int x = Math.Min(start, end); x <= Math.Max(start, end); x++) state.occupiedCoordinates.Add(new Vector2Int(x, 0));
        return state;
    }
    public static void Run()
    {
        var terrain = TerrainGenerator.Active = new TerrainGenerator(); var world = terrain.World;
        var a = (RailInstance)world.RegisterRuntime(State(10, -6, 0), new Railload(), terrain);
        var b = (RailInstance)world.RegisterRuntime(State(20, 0, 6), new Railload(), terrain);
        world.UpsertSaved(State(30, 100, 105));
        var native = new Railload { RuntimePlacementSequence = 40, RuntimeAnchorCoordinate = new Vector2Int(10, 0) };
        native.RuntimeOccupiedCoordinates.Add(new Vector2Int(10, 0)); native.RuntimeOccupiedCoordinates.Add(new Vector2Int(14, 0));
        native.transform.position = new Vector3(10, 2, 0);
        native.Configure(new[] { new Vector2(10, 0), new Vector2(14, 0) }); world.UpsertLive(native);

        var renderer = new RailLineDebugRenderer();
        renderer.SetVisible(true);
        Check(renderer.DebugRailCount == 3, "ShowRailLine must collect ECS rails without native Views as well as native rails");
        Check(renderer.RootActive && renderer.EnabledLineCount == 3, "Visible toggle must submit every live rail line");
        Check(renderer.HasRail(a) && renderer.HasRail(b) && renderer.HasRail(native), "Route highlight lookup must use the collected runtime target identity");
        Check(renderer.LineFor(a).startColor == renderer.LineFor(b).startColor,
            "Connected ECS rails must share the world component color");
        Check(renderer.EnabledArrowCount > 0, "ECS rails must generate direction arrows");
        Check(Math.Abs(renderer.LineFor(a).Points[0].y - 3.18f) < .0001f,
            "Data-only debug lines must use target world height");
        foreach (var line in renderer.DebugLines)
        {
            Check(line.enabled && line.positionCount >= 2, "Live rail line must have sampled positions");
            foreach (var point in line.Points) Check(!float.IsNaN(point.x) && !float.IsNaN(point.z), "Debug line samples must be finite");
        }
        renderer.SetVisible(false);
        Check(!renderer.RootActive && renderer.EnabledLineCount == 0 && renderer.EnabledArrowCount == 0,
            "Hidden toggle must disable lines and direction arrows");
        renderer.SetVisible(true);
        Check(renderer.EnabledLineCount == 3, "Repeated toggle must restore data rail lines");
        world.RemoveSaved(b.StorageKey); renderer.Tick();
        Check(renderer.DebugRailCount == 2 && !renderer.HasRail(b) && renderer.EnabledLineCount == 2,
            "Rail demolition must remove stale debug lines on rebuild");
        var added = (RailInstance)world.RegisterRuntime(State(50, 0, 8), new Railload(), terrain);
        renderer.Tick();
        Check(renderer.DebugRailCount == 3 && renderer.HasRail(added) && renderer.EnabledLineCount == 3,
            "Placement must refresh visible debug lines through the topology version");
        world.Clear(); renderer.Tick();
        Check(renderer.DebugRailCount == 0 && renderer.EnabledLineCount == 0 && renderer.EnabledArrowCount == 0,
            "World reset must clear all debug presentation");
        TerrainGenerator.Active = null; renderer.Tick();
        Check(renderer.DebugRailCount == 0, "Missing terrain must not leave a stale rail collection");
        CheckDisplayDirections();
        Console.WriteLine($"PASS rail debug visibility and coherent directions: {checks} checks");
    }

    static RailInstance AddPath(TerrainGenerator terrain, long sequence, bool reverse, params Vector2[] points)
    {
        var state = State(sequence, 0, 0);
        state.railVisualPathPoints.Clear();
        state.occupiedCoordinates.Clear();
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 point = points[reverse ? points.Length - 1 - i : i];
            state.railVisualPathPoints.Add(point);
            state.occupiedCoordinates.Add(new Vector2Int((int)point.x, (int)point.y));
        }
        state.anchorCoordinate = state.occupiedCoordinates[0];
        state.hasStorageKey = true;
        state.storageKey = new Vector2Int((int)sequence, 1000);
        return (RailInstance)terrain.World.RegisterRuntime(state, new Railload(), terrain);
    }

    static void AssertDirection(RailLineDebugRenderer renderer, IRailTarget rail, int expected, string message)
    {
        Check(renderer.DirectionFor(rail) == expected, message);
        var arrows = renderer.ArrowsFor(rail);
        Check(arrows.Count > 0, "Direction test must submit actual arrow geometry");
        foreach (var arrow in arrows)
        {
            Vector2 center = (arrow.Item1 + arrow.Item2) * .5f;
            rail.TryFindNearestRenderedPathSample(center, out _, out _, out Vector2 tangent, out _);
            Check(Vector2.Dot(arrow.Item2 - arrow.Item1, tangent * expected) > .4f,
                "Rendered arrow shaft must follow the resolved line direction");
        }
    }

    static void CheckDisplayDirections()
    {
        List<(Vector2, Vector2)>[] referenceArrows = null;
        // A U-turn needs connectivity propagation; independently sorting every rail's endpoints fails.
        for (int mask = 0; mask < 8; mask++)
        {
            var terrain = TerrainGenerator.Active = new TerrainGenerator();
            bool reverseA = (mask & 1) != 0, reverseB = (mask & 2) != 0, reverseC = (mask & 4) != 0;
            var a = AddPath(terrain, 30, reverseA, new Vector2(-8, 0), new Vector2(0, 0));
            var b = AddPath(terrain, 20, reverseB, new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 4), new Vector2(0, 4));
            var c = AddPath(terrain, 10, reverseC, new Vector2(0, 4), new Vector2(-8, 4));
            var renderer = new RailLineDebugRenderer(); renderer.SetVisible(true);
            AssertDirection(renderer, a, reverseA ? -1 : 1, "U-turn entry must have a geometry-based direction");
            AssertDirection(renderer, b, reverseB ? -1 : 1, $"Curved connector must continue the entry direction (mask {mask}, actual {renderer.DirectionFor(b)}, rails {renderer.DebugRailCount})");
            AssertDirection(renderer, c, reverseC ? -1 : 1, "U-turn exit must follow the curve even toward decreasing X");
            var actualArrows = new[] { renderer.ArrowsFor(a), renderer.ArrowsFor(b), renderer.ArrowsFor(c) };
            if (referenceArrows == null) referenceArrows = actualArrows;
            else for (int i = 0; i < actualArrows.Length; i++)
            {
                Check(actualArrows[i].Count == referenceArrows[i].Count, "Path reversal must preserve arrow count");
                for (int j = 0; j < actualArrows[i].Count; j++)
                    Check((actualArrows[i][j].Item1 - referenceArrows[i][j].Item1).sqrMagnitude < .000001f
                        && (actualArrows[i][j].Item2 - referenceArrows[i][j].Item2).sqrMagnitude < .000001f,
                        "Reversed installation must preserve both arrow positions and orientation");
            }
            Check(a.RuntimeVisualPathPoints[0] == (reverseA ? new Vector2(0, 0) : new Vector2(-8, 0)),
                "Debug direction must not reverse persisted path points or train sampling");
            renderer.SetVisible(false); renderer.SetVisible(true);
            Check(renderer.DirectionFor(c) == (reverseC ? -1 : 1), "Toggle must retain coherent geometry-based directions");
        }

        for (int mask = 0; mask < 16; mask++)
        {
            var terrain = TerrainGenerator.Active = new TerrainGenerator();
            var corners = new[] { new Vector2(0, 0), new Vector2(0, 6), new Vector2(6, 6), new Vector2(6, 0) };
            var targets = new RailInstance[4];
            for (int i = 0; i < 4; i++)
                targets[i] = AddPath(terrain, 10 + (mask % 2 == 0 ? i : 3 - i), (mask & (1 << i)) != 0,
                    corners[i], corners[(i + 1) % 4]);
            var renderer = new RailLineDebugRenderer(); renderer.SetVisible(true);
            for (int i = 0; i < 4; i++)
                AssertDirection(renderer, targets[i], (mask & (1 << i)) != 0 ? -1 : 1,
                    "Closed loop and sharp corners must have one consistent direction regardless of installation order");
        }

        var mixedTerrain = TerrainGenerator.Active = new TerrainGenerator();
        var data = AddPath(mixedTerrain, 20, true, new Vector2(-6, 0), new Vector2(0, 0));
        var native = new Railload { RuntimePlacementSequence = 10, RuntimeAnchorCoordinate = new Vector2Int(6, 0) };
        native.RuntimeOccupiedCoordinates.Add(new Vector2Int(6, 0)); native.RuntimeOccupiedCoordinates.Add(new Vector2Int(0, 0));
        native.Configure(new[] { new Vector2(6, 0), new Vector2(0, 0) }); mixedTerrain.World.UpsertLive(native);
        var mixedRenderer = new RailLineDebugRenderer(); mixedRenderer.SetVisible(true);
        AssertDirection(mixedRenderer, data, -1, "Data rail must share the same direction with a native rail");
        AssertDirection(mixedRenderer, native, -1, "Native view must not take priority over the geometric root");
        var branch = AddPath(mixedTerrain, 30, true, new Vector2(3, 0), new Vector2(9, 3)); mixedRenderer.Tick();
        AssertDirection(mixedRenderer, branch, -1, "Endpoint-to-interior branch must align with the main line tangent");
        mixedTerrain.World.RemoveSaved(data.StorageKey); mixedRenderer.Tick();
        AssertDirection(mixedRenderer, native, -1, "Demolition must rebuild orientation without a stale seed");

        for (int reverse = 0; reverse < 2; reverse++)
        {
            var terrain = TerrainGenerator.Active = new TerrainGenerator();
            var loop = AddPath(terrain, 1, reverse != 0, new Vector2(0, 0), new Vector2(0, 6),
                new Vector2(6, 6), new Vector2(6, 0), new Vector2(0, 0));
            var renderer = new RailLineDebugRenderer(); renderer.SetVisible(true);
            AssertDirection(renderer, loop, reverse == 0 ? 1 : -1, "Single closed path must have a stable geometric direction");
        }

        var snapTerrain = TerrainGenerator.Active = new TerrainGenerator();
        var entry = AddPath(snapTerrain, 2, true, new Vector2(-6, 0), new Vector2(0, 0));
        var snapped = AddPath(snapTerrain, 1, true, new Vector2(.4f, 0), new Vector2(.4f, 6));
        var snapRenderer = new RailLineDebugRenderer(); snapRenderer.SetVisible(true);
        AssertDirection(snapRenderer, entry, -1, "Snap-distance entry must use the geometric root");
        AssertDirection(snapRenderer, snapped, -1, "Connection tolerance must also propagate direction across a corner");
        CheckOverlappingDirections();
        TerrainGenerator.Active = null;
    }

    static void CheckOverlappingDirections()
    {
        var paths = new[]
        {
            new[] { new Vector2(-8, 0), new Vector2(0, 0) },
            new[] { new Vector2(-3, 0), new Vector2(0, 0) },
            new[] { new Vector2(-8, 0), new Vector2(-3, 0) },
            new[] { new Vector2(-8, 0), new Vector2(0, 0) }
        };
        for (int mask = 0; mask < 16; mask++)
        {
            var terrain = TerrainGenerator.Active = new TerrainGenerator();
            var targets = new RailInstance[paths.Length];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = AddPath(terrain, 10 + (mask % 2 == 0 ? i : targets.Length - 1 - i),
                    (mask & (1 << i)) != 0, paths[i]);
            var renderer = new RailLineDebugRenderer(); renderer.SetVisible(true);
            for (int i = 0; i < targets.Length; i++)
                AssertDirection(renderer, targets[i], (mask & (1 << i)) != 0 ? -1 : 1,
                    $"Overlapping rails must agree even with shared starts/ends (mask {mask}, rail {i})");
        }

        for (int mask = 0; mask < 4; mask++)
        {
            var terrain = TerrainGenerator.Active = new TerrainGenerator();
            var main = AddPath(terrain, 20, (mask & 1) != 0, new Vector2(-8, 0), new Vector2(0, 0),
                new Vector2(4, 0), new Vector2(7, 2), new Vector2(9, 6));
            var overlap = AddPath(terrain, 10, (mask & 2) != 0, new Vector2(0, 0),
                new Vector2(4, 0), new Vector2(7, 2), new Vector2(9, 6));
            var renderer = new RailLineDebugRenderer(); renderer.SetVisible(true);
            AssertDirection(renderer, main, (mask & 1) != 0 ? -1 : 1, "Curved main rail must keep the root direction");
            AssertDirection(renderer, overlap, (mask & 2) != 0 ? -1 : 1,
                "Shared curved terminal must not flip the overlapping rail");
        }
    }
}

// GPU, scene lifetime and unrelated train route/marker presentation are boundaries.
public partial class RailLineDebugRenderer
{
    readonly List<RailInfo> rails = new List<RailInfo>();
    readonly List<LineRenderer> lineRenderers = new List<LineRenderer>(), railArrowRenderers = new List<LineRenderer>();
    readonly DebugRoot debugRoot = new DebugRoot();
    readonly object lineMaterial = new object();
    RailWorld displayedRailWorld;
    int displayedTopologyVersion = -1, lastRouteSelectionVersion;
    float connectionDistance = RailGroupConnectionDistance, sampleSpacing = DefaultSampleSpacing;
    float lineWidth = DefaultLineWidth, lineYOffset = DefaultLineYOffset, refreshInterval = DefaultRefreshInterval;
    float railArrowSpacing = DefaultRailArrowSpacing, railArrowLength = DefaultRailArrowLength;
    float railArrowYOffset = DefaultRailArrowYOffset, railArrowHeadLength = DefaultRailArrowHeadLength;
    float railArrowHeadWidth = DefaultRailArrowHeadWidth, railArrowLineWidth = DefaultRailArrowLineWidth, nextCartArrowRefreshTime;
    bool isVisible, isDirty = true;
    public int DebugRailCount => rails.Count;
    public IReadOnlyList<LineRenderer> DebugLines => lineRenderers;
    public bool RootActive => debugRoot.gameObject.Active;
    public int EnabledLineCount { get { int count = 0; foreach (var line in lineRenderers) if (line.enabled) count++; return count; } }
    public int EnabledArrowCount { get { int count = 0; foreach (var line in railArrowRenderers) if (line.enabled) count++; return count; } }
    public bool HasRail(IRailTarget rail) => TryFindRailInfoIndex(rail, out _);
    public int DirectionFor(IRailTarget rail) => TryFindRailInfoIndex(rail, out int index) ? rails[index].DisplayDirection : 0;
    public List<(Vector2, Vector2)> ArrowsFor(IRailTarget rail)
    {
        var result = new List<(Vector2, Vector2)>();
        if (!TryFindRailInfoIndex(rail, out int index)) return result;
        int count = ApplyRailDirectionArrows(rails[index], Color.white, 0);
        for (int i = 0; i < count; i += 3)
        {
            var points = railArrowRenderers[i].Points;
            result.Add((new Vector2(points[0].x, points[0].z), new Vector2(points[1].x, points[1].z)));
        }
        return result;
    }
    public LineRenderer LineFor(IRailTarget rail) => TryFindRailInfoIndex(rail, out int index) ? lineRenderers[index] : null;
    public void Tick() => LateUpdate();
    static LineRenderer Ensure(List<LineRenderer> renderers, int index)
    { while (renderers.Count <= index) renderers.Add(new LineRenderer()); return renderers[index]; }
    LineRenderer EnsureLineRenderer(int index) => Ensure(lineRenderers, index);
    LineRenderer EnsureRailArrowRenderer(int index) => Ensure(railArrowRenderers, index);
    void EnsureDebugRoot() { }
    void EnsureLineMaterial() { }
    void ApplyRouteHighlight() { }
    void RefreshCartDirectionArrows() { }
    void RefreshAutoDrivePowerSourceMarker() { }
    void RefreshSelectedTargetStationMarker() { }
    void CacheRouteSelectionState() { }
    bool HasRouteSelectionStateChanged() => false;
    void DisableRouteHighlightRenderers() { }
    void DisableCartArrowRenderers() { }
    void DisablePowerMarkerRenderers() { }
    void DisableTargetStationMarkerRenderers() { }
    sealed class DebugRoot { public readonly DebugObject gameObject = new DebugObject(); }
    sealed class DebugObject { public bool Active; public void SetActive(bool value) => Active = value; }
}
public sealed class LineRenderer
{
    public bool enabled;
    Vector3[] points = Array.Empty<Vector3>();
    public int positionCount { get => points.Length; set { if (points.Length != value) points = new Vector3[value]; } }
    public float startWidth, endWidth;
    public Color startColor, endColor;
    public object material;
    public IReadOnlyList<Vector3> Points => points;
    public void SetPosition(int index, Vector3 value) => points[index] = value;
}
static class Time { public static float unscaledTime => 0; }
static class TrainFilter { public static int RouteSelectionVersion => 0; }
static class MapObjectTickProfiler
{
    public readonly struct Sample : IDisposable { public void Dispose() { } }
    public static Sample SampleLateUpdateCaller<T>() => default;
}
