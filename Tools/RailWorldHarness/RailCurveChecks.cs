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

    public static bool TestBuildableCurve(IReadOnlyList<Vector2> points) => IsBuildableRailCurve(points);
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
        CheckShallowExtensions(controller);
        CheckCompactCorners(controller);
        CheckCurveLimits(controller);
        Console.WriteLine($"PASS smooth rail placement: {checks} checks");
    }

    static void CheckCompactCorners(RailloadInstallationController controller)
    {
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (int sideSign in new[] { -1, 1 })
        foreach (bool authoredReverse in new[] { false, true })
        foreach (bool placingReverse in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        foreach (int sourceMode in new[] { 0, 1, 2 })
        foreach (int span in new[] { 2, 3 })
        {
            TerrainGenerator.Active = new TerrainGenerator();
            if (sourceMode != 0)
                AddRail(authoredReverse, true, axis * -8, Vector2Int.zero, sourceMode == 2);
            var side = new Vector2Int(-axis.y, axis.x) * sideSign;
            var far = (axis + side) * span;
            var path = controller.BuildTestPath(placingReverse ? far : Vector2Int.zero,
                placingReverse ? Vector2Int.zero : far, horizontalFirst, out _, out _, out var occupied);
            Check(RailloadInstallationController.TestBuildableCurve(path),
                $"Compact rounded corners must be buildable: span={span}, source={sourceMode}, reverse={placingReverse}");
            Check(occupied.Count > 0, "Compact corners retain their occupied-cell footprint");
            var storedPath = new RailPathData();
            storedPath.Configure(path, occupied, occupied[0], false, false);
            Check(storedPath.TrySample(storedPath.Length * .5f, out var point, out var tangent)
                && !float.IsNaN(point.x) && tangent.sqrMagnitude > .99f,
                "Compact installed curves must retain a sampleable train path");
        }
    }

    static void CheckCurveLimits(RailloadInstallationController controller)
    {
        bool Allowed(IReadOnlyList<Vector2> points) => RailloadInstallationController.TestBuildableCurve(points);
        Check(!Allowed(null) && !Allowed(Array.Empty<Vector2>()) && !Allowed(new[] { Vector2.zero }),
            "Missing and single-point paths cannot be installed");
        Check(!Allowed(new[] { Vector2.zero, Vector2.zero }), "Duplicate points cannot bypass curvature checks");
        Check(!Allowed(new[] { Vector2.zero, new Vector2(float.NaN, 0) })
            && !Allowed(new[] { Vector2.zero, new Vector2(0, float.PositiveInfinity) }), "Nonfinite paths are rejected");
        Check(Allowed(new[] { Vector2.zero, Vector2.right * 10 }), "Two-point straight rails remain buildable");
        Check(!Allowed(new[] { Vector2.zero, Vector2.right * 10, Vector2.right * 10 + Vector2.up * 10 }),
            "A sharp 90-degree corner fails even when its circumradius is large");
        Check(!Allowed(new[] { Vector2.zero, Vector2.right * 10, Vector2.zero }), "A direction reversal cannot be installed");
        foreach (float radius in new[] { .45f, .55f, 1f, 1.95f, 2.05f, 4f })
        foreach (int sampleCount in new[] { 24, 96 })
        {
            var arc = new List<Vector2>();
            for (int i = 0; i <= sampleCount; i++)
            {
                double angle = Math.PI * i / sampleCount;
                arc.Add(new Vector2(radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle)));
            }
            Check(Allowed(arc) == (radius > .5f), "The half-cell radius limit must be independent of arc sampling density");
            arc.Reverse();
            Check(Allowed(arc) == (radius > .5f), "Curve limits are independent of traversal direction");
        }
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (int sideSign in new[] { -1, 1 })
        foreach (bool authoredReverse in new[] { false, true })
        foreach (bool placingReverse in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        foreach (bool dataOnly in new[] { false, true })
        {
            var side = new Vector2Int(-axis.y, axis.x) * sideSign;
            foreach (int forward in new[] { -16, -6, -1, 0, 1, 8, 16 })
            {
                TerrainGenerator.Active = new TerrainGenerator();
                AddRail(authoredReverse, true, axis * -24, Vector2Int.zero, dataOnly);
                var far = axis * forward + side;
                var path = controller.BuildTestPath(placingReverse ? far : Vector2Int.zero,
                    placingReverse ? Vector2Int.zero : far, horizontalFirst, out _, out _, out _);
                Check(Allowed(path) == (forward >= 8),
                    $"Tight hooks must be rejected while shallow forward extensions remain valid: forward={forward}");
            }
            TerrainGenerator.Active = new TerrainGenerator();
            AddRail(authoredReverse, true, axis * -8, Vector2Int.zero, dataOnly);
            var cornerEnd = axis * 12 + side * 12;
            var corner = controller.BuildTestPath(placingReverse ? cornerEnd : Vector2Int.zero,
                placingReverse ? Vector2Int.zero : cornerEnd, horizontalFirst, out _, out _, out _);
            Check(Allowed(corner), "A spacious real corner remains buildable");
        }
    }

    static void CheckShallowExtensions(RailloadInstallationController controller)
    {
        foreach (Vector2Int axis in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        foreach (int sideSign in new[] { -1, 1 })
        foreach (bool authoredReverse in new[] { false, true })
        foreach (bool placingReverse in new[] { false, true })
        foreach (bool horizontalFirst in new[] { false, true })
        foreach (int sourceMode in new[] { 0, 1, 2, 3 })
        foreach (int forward in new[] { 4, 8, 16, 64 })
        foreach (int lateral in new[] { 1, 2 })
        {
            var side = new Vector2Int(-axis.y, axis.x) * sideSign;
            TerrainGenerator.Active = new TerrainGenerator();
            bool connected = sourceMode != 0;
            bool extended = sourceMode != 3;
            if (connected) AddRail(authoredReverse, extended, axis * -8, Vector2Int.zero, sourceMode >= 2);
            var far = axis * forward + side * lateral;
            var path = controller.BuildTestPath(placingReverse ? far : Vector2Int.zero,
                placingReverse ? Vector2Int.zero : far, horizontalFirst,
                out bool extendsStart, out bool extendsEnd, out var occupied);
            var joint = (Vector2)axis * (connected && extended ? .5f : 0f);
            var chord = ((Vector2)far - joint).normalized;
            var expectedFree = 2f * Vector2.Dot(chord, axis) * chord - (Vector2)axis;
            Vector2 freeTangent = placingReverse ? path[0] - path[1] : path[path.Count - 1] - path[path.Count - 2];
            if (connected)
            {
                Check(Vector2.Dot(freeTangent.normalized, expectedFree) > .9999f,
                    "A shallow extension must finish along its natural arc, not a forced perpendicular");
                Check(!(placingReverse ? extendsEnd : extendsStart), "The connected shallow end must not extend again");
                var tangent = placingReverse ? path[path.Count - 2] - path[path.Count - 1] : path[1] - path[0];
                Check(Vector2.Dot(tangent.normalized, axis) > .9999f, "Shallow extensions retain the connected tangent");
            }
            int turnSign = 0;
            for (int i = 0; i < path.Count; i++)
            {
                int index = placingReverse ? path.Count - 1 - i : i;
                var point = path[index];
                float along = Vector2.Dot(point, axis);
                float across = Vector2.Dot(point, side);
                Check(along >= Vector2.Dot(joint, axis) - .0001f && along <= forward + .0001f
                    && across >= -.0001f && across <= lateral + .0001f,
                    "A shallow curve must stay inside its endpoint envelope, without a hook or overshoot");
                if (i == 0) continue;
                var previous = path[placingReverse ? index + 1 : index - 1];
                var step = point - previous;
                Check(Vector2.Dot(step, axis) >= -.0001f && Vector2.Dot(step, side) >= -.0001f,
                    "A shallow rail must progress monotonically in both endpoint axes");
                Check(Vector2.Dot(step.normalized, axis) > .45f,
                    "A shallow rail must not contain a short 90-degree terminal segment");
                if (i < 2) continue;
                var before = path[placingReverse ? index + 2 : index - 2];
                var incoming = previous - before;
                double cross = (double)incoming.x * step.y - (double)incoming.y * step.x;
                if (Math.Abs(cross) <= 1e-9) continue;
                int sign = Math.Sign(cross);
                Check(turnSign == 0 || turnSign == sign, "A shallow free-end curve must not regress into an S bend");
                turnSign = sign;
                double radius = incoming.magnitude * step.magnitude * (point - before).magnitude / (2 * Math.Abs(cross));
                Check(radius > .3, "Shallow curve radius must exceed the rail half-spacing");
            }
            Check(occupied.Count > 0, "Shallow curves must produce a placement footprint");
            var reverse = controller.BuildTestPath(placingReverse ? Vector2Int.zero : far,
                placingReverse ? far : Vector2Int.zero, !horizontalFirst, out _, out _, out _);
            Check(reverse.Count == path.Count, "Reversing shallow placement preserves sample count");
            for (int i = 0; i < path.Count; i++)
                Check(Vector2.Distance(path[i], reverse[reverse.Count - 1 - i]) < .001f,
                    "Reversing shallow placement preserves physical geometry");
        }
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
                var chord = ((Vector2)far - (Vector2)axis * .5f).normalized;
                var expected = forward - .5f <= 5
                    ? (Vector2)side : 2f * Vector2.Dot(chord, axis) * chord - (Vector2)axis;
                Check(Vector2.Dot(freeTangent.normalized, expected * (placingReverse ? -1 : 1)) > .995f,
                    "A free terminal must follow a natural arc for shallow extensions and retain true corners");
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
