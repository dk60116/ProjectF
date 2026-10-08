using System;
using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

public partial class RailloadInstallationController
{
    public List<Vector2> BuildTestPath(Vector2Int start, Vector2Int end, bool horizontalFirst,
        out bool extendsStart, out bool extendsEnd, out List<Vector2Int> occupied)
    {
        var plan = BuildPlan(start, end, horizontalFirst);
        extendsStart = plan.extendStartEndpoint;
        extendsEnd = plan.extendEndEndpoint;
        occupied = plan.occupiedCoordinates;
        return plan.visualPathPoints;
    }
}

static class RailCurveChecks
{
    static int checks;
    static long sequence;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    public static void Run()
    {
        var controller = new RailloadInstallationController();
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (bool authoredReverse in new[] { false, true })
        foreach (bool placingReverse in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        foreach (bool dataOnly in new[] { false, true })
        foreach (int endpointMode in new[] { 0, 1, 2 })
        {
            var side = new Vector2Int(-axis.y, axis.x);
            TerrainGenerator.Active = new TerrainGenerator();
            bool extended = endpointMode != 2;
            AddRail(authoredReverse, extended, axis * -8, Vector2Int.zero, dataOnly);
            Vector2Int joint = endpointMode == 0 ? axis : Vector2Int.zero;
            Vector2Int far = axis * 6 + side * 4;
            var path = controller.BuildTestPath(placingReverse ? far : joint, placingReverse ? joint : far,
                horizontalFirst, out bool extendStart, out bool extendEnd, out var occupied);
            Vector2 endpoint = placingReverse ? path[path.Count - 1] : path[0];
            Check(Vector2.Distance(endpoint, (Vector2)axis * (extended ? .5f : 0f)) < .0001f,
                "Adjacent/end-cell placement must snap to the existing RENDERED terminal");
            Check(!(placingReverse ? extendEnd : extendStart), "A snapped endpoint must not be extended a second time");
            Vector2 tangent = placingReverse ? path[path.Count - 1] - path[path.Count - 2] : path[1] - path[0];
            Check(Vector2.Dot(tangent.normalized, (Vector2)axis * (placingReverse ? -1 : 1)) > .9999f,
                "The new rail must continue the existing endpoint axis, independent of authoring/placement order");
            Check(occupied.Count > 0, "The corrected curve must rebuild its actual placement footprint");

            var savedPath = new RailPathData();
            savedPath.Configure(path, occupied, occupied[0], extendStart, extendEnd);
            Check(savedPath.TrySample(placingReverse ? savedPath.Length : 0, out var point, out var direction)
                && Vector2.Distance(point, endpoint) < .0001f && Vector2.Dot(direction, tangent.normalized) > .9999f,
                "Installed/save-restored sampling must keep the preview endpoint and tangent");
        }
        CheckStraightAndInteriorJoins(controller);
        CheckBothEnds(controller);
        CheckCurvedSource(controller);
        CheckSingleTurn(controller);
        Console.WriteLine($"PASS smooth rail placement: {checks} checks");
    }

    static void CheckSingleTurn(RailloadInstallationController controller)
    {
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (int sideSign in new[] { -1, 1 })
        foreach (bool authoredReverse in new[] { false, true })
        foreach (bool placingReverse in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        foreach (bool dataOnly in new[] { false, true })
        foreach (int forward in new[] { -3, 0, 1, 6, 16 })
        {
            var side = new Vector2Int(-axis.y, axis.x) * sideSign;
            TerrainGenerator.Active = new TerrainGenerator();
            AddRail(authoredReverse, true, axis * -8, Vector2Int.zero, dataOnly);
            var far = axis * forward + side * 5;
            var path = controller.BuildTestPath(placingReverse ? far : Vector2Int.zero,
                placingReverse ? Vector2Int.zero : far, horizontalFirst, out _, out _, out _);
            int turnSign = 0;
            for (int i = 1; i + 1 < path.Count; i++)
            {
                var incoming = (path[i] - path[i - 1]).normalized;
                var outgoing = (path[i + 1] - path[i]).normalized;
                float turn = incoming.x * outgoing.y - incoming.y * outgoing.x;
                if (Math.Abs(turn) <= .00001f) continue;
                int sign = Math.Sign(turn);
                Check(turnSign == 0 || turnSign == sign,
                    $"A single-ended rail must turn in one direction, not form an S: forward={forward}, reverse={placingReverse}, horizontal={horizontalFirst}");
                turnSign = sign;
            }
            Check(turnSign != 0, "A displaced single-ended rail must contain a turn");
            if (forward > 0)
            {
                var freeTangent = placingReverse ? path[1] - path[0] : path[path.Count - 1] - path[path.Count - 2];
                Check(Vector2.Dot(freeTangent.normalized, (Vector2)side * (placingReverse ? -1 : 1)) > .995f,
                    "A forward single-ended corner should finish along the perpendicular axis");
            }
        }
    }

    static void CheckCurvedSource(RailloadInstallationController controller)
    {
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (bool authoredReverse in new[] { false, true })
        foreach (bool placingReverse in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        {
            var side = new Vector2Int(-axis.y, axis.x);
            TerrainGenerator.Active = new TerrainGenerator();
            var rail = AddRail(authoredReverse, true, axis * -8, Vector2Int.zero, false);
            var source = new[] { (Vector2)axis * -8, (Vector2)axis * -3 - (Vector2)side,
                (Vector2)axis * -1 - (Vector2)side * .2f, Vector2.zero };
            if (authoredReverse) Array.Reverse(source);
            rail.Configure(source, true, true);
            TerrainGenerator.Active.World.UpsertLive(rail);
            Check(rail.TryGetRenderedEndpointSample(authoredReverse, out _, out var terminal, out var existingTangent),
                "A curved existing rail must provide its rendered terminal");
            Vector2Int far = axis * 6 + side * 4;
            var path = controller.BuildTestPath(placingReverse ? far : axis, placingReverse ? axis : far,
                horizontalFirst, out _, out _, out _);
            var point = placingReverse ? path[path.Count - 1] : path[0];
            var tangent = placingReverse ? path[path.Count - 1] - path[path.Count - 2] : path[1] - path[0];
            Check(Vector2.Distance(point, terminal) < .0001f,
                "Joining a curved rail must use its actual extended terminal, not its source point");
            Vector2 expected = existingTangent * (authoredReverse == placingReverse ? 1 : -1);
            Check(Vector2.Dot(tangent.normalized, expected) > .9999f,
                "Joining a curved rail must keep its non-cardinal sampled tangent");
        }
    }

    static void CheckStraightAndInteriorJoins(RailloadInstallationController controller)
    {
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (bool reverse in new[] { false, true })
        foreach (bool placingReverse in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        {
            var side = new Vector2Int(-axis.y, axis.x);
            TerrainGenerator.Active = new TerrainGenerator();
            var path = controller.BuildTestPath(Vector2Int.zero, axis * 8, horizontalFirst, out var startExtended, out var endExtended, out _);
            Check(path.Count == 2 && startExtended && endExtended, "Unconstrained straight rails must retain the two-point fast path");

            AddRail(reverse, true, axis * -8, Vector2Int.zero, true);
            Vector2Int far = side * 6;
            path = controller.BuildTestPath(placingReverse ? far : Vector2Int.zero, placingReverse ? Vector2Int.zero : far,
                horizontalFirst, out startExtended, out endExtended, out _);
            Vector2 tangent = placingReverse ? path[path.Count - 1] - path[path.Count - 2] : path[1] - path[0];
            Check(path.Count > 2 && !(placingReverse ? endExtended : startExtended)
                && Vector2.Dot(tangent.normalized, (Vector2)axis * (placingReverse ? -1 : 1)) > .9999f,
                "A collinear grid drag joining a perpendicular existing rail must produce a tangent-matched curve, not a sharp corner");

            TerrainGenerator.Active = new TerrainGenerator();
            AddRail(reverse, true, axis * -8, axis * 8, true);
            far = axis * 3 + side * 6;
            path = controller.BuildTestPath(placingReverse ? far : Vector2Int.zero, placingReverse ? Vector2Int.zero : far,
                horizontalFirst, out startExtended, out endExtended, out _);
            tangent = placingReverse ? path[path.Count - 1] - path[path.Count - 2] : path[1] - path[0];
            Check(Vector2.Dot(tangent.normalized, (Vector2)axis * (placingReverse ? -1 : 1)) > .9999f,
                "Interior branch tangents must resolve by the route, not reverse-authored point order");
        }
    }

    static void CheckBothEnds(RailloadInstallationController controller)
    {
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (bool reverseFirst in new[] { false, true })
        foreach (bool reverseLast in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        {
            var side = new Vector2Int(-axis.y, axis.x);
            var end = axis * 6 + side * 4;
            TerrainGenerator.Active = new TerrainGenerator();
            AddRail(reverseFirst, true, axis * -8, Vector2Int.zero, true);
            AddRail(reverseLast, true, end, end + side * 8, true);
            var path = controller.BuildTestPath(Vector2Int.zero, end, horizontalFirst, out bool extendStart, out bool extendEnd, out var occupied);
            var reversedPath = controller.BuildTestPath(end, Vector2Int.zero, !horizontalFirst, out bool reversedStart, out bool reversedEnd, out var reversedOccupied);
            Check(!extendStart && !extendEnd && !reversedStart && !reversedEnd, "Both connected ends must suppress auto-extension");
            Check(Vector2.Dot((path[1] - path[0]).normalized, axis) > .9999f
                && Vector2.Dot((path[path.Count - 1] - path[path.Count - 2]).normalized, side) > .9999f,
                "A two-ended curve must match both existing rail axes");
            Check(Vector2.Distance(path[0], (Vector2)axis * .5f) < .0001f
                && Vector2.Distance(path[path.Count - 1], (Vector2)end - (Vector2)side * .5f) < .0001f,
                "A two-ended curve must use both rendered terminals");
            var forward = new RailPathData(); var backward = new RailPathData();
            forward.Configure(path, occupied, occupied[0], false, false);
            backward.Configure(reversedPath, reversedOccupied, reversedOccupied[0], false, false);
            Check(Math.Abs(forward.Length - backward.Length) < .001f, "Reversing placement must retain route length");
            for (int i = 0; i <= 10; i++)
            {
                forward.TrySample(forward.Length * i / 10, out var point, out var tangent);
                backward.TrySample(backward.Length * (10 - i) / 10, out var reversePoint, out var reverseTangent);
                Check(Vector2.Distance(point, reversePoint) < .001f && Vector2.Dot(tangent, reverseTangent) < -.999f,
                    "Reversed authoring must represent the same physical curve with reversed traversal");
            }
        }
    }

    static Railload AddRail(bool reverse, bool extend, Vector2Int start, Vector2Int end, bool dataOnly)
    {
        var rail = new Railload { RuntimePlacementSequence = ++sequence, RuntimeAnchorCoordinate = reverse ? end : start };
        Vector2Int step = new Vector2Int(Math.Sign(end.x - start.x), Math.Sign(end.y - start.y));
        for (var coordinate = start; ; coordinate += step)
        {
            rail.RuntimeOccupiedCoordinates.Add(coordinate);
            if (coordinate == end) break;
        }
        rail.Configure(reverse ? new Vector2[] { end, start } : new Vector2[] { start, end }, extend, extend);
        if (dataOnly)
        {
            var state = new BlockStateStore.InstallationSaveState {
                placementSequence = rail.RuntimePlacementSequence, anchorCoordinate = rail.RuntimeAnchorCoordinate,
                railVisualPathPoints = new List<Vector2>(rail.RuntimeVisualPathPoints),
                occupiedCoordinates = new List<Vector2Int>(rail.RuntimeOccupiedCoordinates),
                railVisualPathExtendsStart = extend, railVisualPathExtendsEnd = extend };
            TerrainGenerator.Active.World.RegisterRuntime(state, rail, TerrainGenerator.Active);
        }
        else TerrainGenerator.Active.World.UpsertLive(rail);
        return rail;
    }
}
