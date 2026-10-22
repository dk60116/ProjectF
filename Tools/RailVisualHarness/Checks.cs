using System;
using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

internal static class Checks
{
    private static int checks;
    private static readonly RailVisualStyle Style = new RailVisualStyle(.04f, .3f, .08f, .84f, .16f, .55f, .08f);
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message, float tolerance = .002f)
    { Check(Math.Abs(actual - expected) <= tolerance, message + $": {actual} != {expected}"); }
    private static Vector2[] Line(float start, float end, float z = 0) => new[] { new Vector2(start, z), new Vector2(end, z) };
    private static RailVisualNetwork Build(params Vector2[][] paths)
    {
        var network = new RailVisualNetwork();
        for (int i = 0; i < paths.Length; i++) network.Upsert(i, paths[i], .1f, Style);
        Check(network.Rebuild(), "Initial geometry must rebuild");
        return network;
    }
    private static float Length(RailVisualNetwork.Run run)
    {
        float result = 0;
        for (int i = 1; i < run.Points.Count; i++) result += Vector3.Distance(run.Points[i - 1], run.Points[i]);
        return result;
    }
    private static List<Vector3> Sleepers(RailVisualNetwork network)
    {
        var result = new List<Vector3>();
        foreach (var group in network.Added) foreach (var sleeper in group.Sleepers) result.Add(sleeper.Position);
        result.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
        return result;
    }
    private static void Same(RailVisualNetwork a, RailVisualNetwork b, string message)
    {
        List<Vector3> left = Sleepers(a), right = Sleepers(b);
        Check(left.Count == right.Count, message + ": sleeper count");
        for (int i = 0; i < left.Count; i++)
            Near(Vector3.Distance(left[i], right[i]), 0, message + ": position");
        float totalA = 0, totalB = 0;
        foreach (var group in a.Added) foreach (var run in group.Runs) totalA += Length(run);
        foreach (var group in b.Added) foreach (var run in group.Runs) totalB += Length(run);
        Near(totalA, totalB, message + ": rail length");
    }
    private static Vector2[] Reverse(Vector2[] points)
    { var result = (Vector2[])points.Clone(); Array.Reverse(result); return result; }
    private static Vector2[] Slice(Vector2[] points, int first, int last)
    { var result = new Vector2[last - first + 1]; Array.Copy(points, first, result, 0, result.Length); return result; }

    public static void Main()
    {
        StraightAndOverlap(); CurvesAndLoops(); JunctionSleeperGaps(); Junctions(); EditsAndCaches(); MeshGeometry(); UnevenCurveSampling(); BallastTerrainMask(); SparseAndIdle();
        Console.WriteLine($"Rail visual network passed: {checks} checks");
    }

    private static void StraightAndOverlap()
    {
        var whole = Build(Line(0, 12));
        Check(whole.Added.Count == 1 && whole.Added[0].Runs.Count == 1, "A line has one visual run");
        Near(Length(whole.Added[0].Runs[0]), 12, "Whole line length");
        List<Vector3> sleepers = Sleepers(whole);
        for (int i = 1; i < sleepers.Count; i++) Near(Vector3.Distance(sleepers[i - 1], sleepers[i]), .55f, "Fixed sleeper pitch");
        foreach (var pieces in new[] {
            new[] { Line(0, 4), Line(4, 8), Line(8, 12) },
            new[] { Line(12, 8), Line(4, 0), Line(8, 4) },
            new[] { Line(0, 6), Line(4, 12) },
            new[] { Line(12, 4), Line(6, 0) },
            new[] { Line(0, 12), Line(2, 9), Line(12, 0) },
            new[] { Line(0, 1.731f), Line(1.731f, 12) }
        })
        {
            var split = Build(pieces);
            Check(split.Added.Count == 1 && split.Added[0].Runs.Count == 1, "Overlaps become one run");
            Same(whole, split, "Split, reversed and overlapping installation must match whole installation");
        }
        var parallel = Build(Line(0, 12), Line(0, 12, .1f));
        Check(parallel.Added.Count == 2, "Nearby parallel lines must not be merged by the train snap radius");
        var gap = Build(Line(0, 5), Line(5.02f, 12));
        Check(gap.Added.Count == 2, "A real gap is not silently filled");
        var heights = new RailVisualNetwork();
        heights.Upsert(0, Line(0, 12), .1f, Style); heights.Upsert(1, Line(0, 12), 2, Style);
        heights.Rebuild();
        Check(heights.Added.Count == 2, "Stacked rails are independent");
        var different = new RailVisualNetwork();
        different.Upsert(0, Line(0, 5), .1f, Style);
        different.Upsert(1, Line(5, 12), .1f, new RailVisualStyle(.08f, .3f, .08f, .84f, .16f, .55f, .08f));
        different.Rebuild();
        Check(different.Added.Count == 2, "Different visual profiles retain their own geometry");
    }

    private static void CurvesAndLoops()
    {
        var curve = new Vector2[49];
        for (int i = 0; i < curve.Length; i++)
        {
            float angle = i * MathF.PI / 96;
            curve[i] = new Vector2(4 * MathF.Sin(angle), 4 * (1 - MathF.Cos(angle)));
        }
        var copy = (Vector2[])curve.Clone();
        var whole = Build(curve);
        Same(whole, Build(Slice(curve, 0, 24), Slice(curve, 24, 48)), "Curve split at a join");
        Same(whole, Build(Reverse(Slice(curve, 16, 48)), Reverse(Slice(curve, 0, 32))), "Reversed curve overlap");
        for (int i = 0; i < curve.Length; i++) Check(curve[i] == copy[i], "Source driving/save samples remain immutable");
        var loop = new Vector2[65];
        for (int i = 0; i < loop.Length - 1; i++)
        {
            float angle = i * MathF.PI / 32;
            loop[i] = new Vector2(3 * MathF.Cos(angle), 3 * MathF.Sin(angle));
        }
        loop[64] = loop[0];
        var closed = Build(loop);
        Check(closed.Added[0].Runs.Count == 1 && closed.Added[0].Runs[0].Closed, "Closed loop remains one closed run");
        Same(closed, Build(Reverse(loop)), "Closed-loop direction independence");
        Same(closed, Build(Slice(loop, 0, 24), Slice(loop, 24, 48), Slice(loop, 48, 64)), "Loop install partitions");
        var run = closed.Added[0].Runs[0];
        float length = Length(run);
        var distances = new List<float>();
        foreach (var sleeper in closed.Added[0].Sleepers) distances.Add(Project(run.Points, sleeper.Position));
        distances.Sort();
        float step = length / distances.Count;
        for (int i = 0; i < distances.Count; i++)
            Near(i + 1 == distances.Count ? length + distances[0] - distances[i] : distances[i + 1] - distances[i],
                step, "Closed-loop seam has the same pitch as the rest");
    }

    private static float Project(List<Vector3> points, Vector3 point)
    {
        float best = float.MaxValue, distance = 0, walked = 0;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 direction = points[i] - points[i - 1];
            float length = direction.magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(point - points[i - 1], direction) / direction.sqrMagnitude);
            float sqr = (point - points[i - 1] - direction * t).sqrMagnitude;
            if (sqr < best) { best = sqr; distance = walked + t * length; }
            walked += length;
        }
        return distance;
    }

    private static void Junctions()
    {
        var branch = Build(Line(-5, 5), new[] { Vector2.zero, new Vector2(3, 4) });
        Check(branch.Added.Count == 1 && branch.Added[0].Runs.Count == 2, "T junction preserves one through-line and one branch arm");
        var crossing = Build(Line(-5, 5), new[] { new Vector2(0, -5), new Vector2(0, 5) });
        Check(crossing.Added[0].Runs.Count == 3, "Crossing preserves a deterministic through-line and two crossing arms");
        foreach (var network in new[] { branch, crossing })
        {
            var sleepers = Sleepers(network);
            for (int i = 0; i < sleepers.Count; i++)
            {
                if (Math.Abs(sleepers[i].z) > .001f)
                    Check(new Vector2(sleepers[i].x, sleepers[i].z).magnitude >= .49f, "Branch arms yield space to continuous mainline boards");
                for (int j = 0; j < i; j++)
                    Check(Vector3.Distance(sleepers[i], sleepers[j]) >= .55f * .75f - .002f, "No cramped sleepers between junction arms");
            }
        }
        Same(branch, Build(new[] { new Vector2(3, 4), Vector2.zero }, Line(5, -5)), "Junction installation order");
        var selfCrossing = Build(new[] { new Vector2(-3, -3), new Vector2(3, 3), new Vector2(-3, 3), new Vector2(3, -3) });
        Check(selfCrossing.Added[0].Runs.Count >= 2, "Self-intersections retain a through-line and separate junction arms");
    }

    private static void JunctionSleeperGaps()
    {
        foreach (var stem in new[] {
            new[] { Vector2.zero, new Vector2(0, 4) },
            new[] { new Vector2(-3, 4), Vector2.zero }
        })
        {
            var whole = Build(Line(-5, 5));
            var fork = Build(Line(-5, 5), stem);
            var actual = new List<float>();
            foreach (var group in fork.Added) foreach (var sleeper in group.Sleepers)
                if (Math.Abs(sleeper.Position.z) < .001f) actual.Add(sleeper.Position.x);
            actual.Sort();
            var expected = Sleepers(whole);
            Check(actual.Count == expected.Count, "A side branch must not remove mainline sleepers or add endpoint margins");
            for (int i = 0; i < actual.Count; i++) Near(actual[i], expected[i].x, "Mainline pitch must continue across a branch");
        }
        var returning = Build(new[] {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(12, 2), new Vector2(10, 4),
            new Vector2(-2, 4), new Vector2(-4, 2), new Vector2(-2, .2f), new Vector2(0, .2f), new Vector2(10, .2f)
        });
        var run = returning.Added[0].Runs[0];
        Check(returning.Added[0].Runs.Count == 1, "Returning path is one continuous run");
        var distances = new List<float>();
        foreach (var sleeper in returning.Added[0].Sleepers) distances.Add(Project(run.Points, sleeper.Position));
        distances.Sort();
        for (int i = 1; i < distances.Count; i++) Near(distances[i] - distances[i - 1], .55f,
            "Proximity to another part of the same path must not delete a regularly spaced sleeper");
    }

    private static float MaskValue(RailBallastMask mask, Vector2 point)
    {
        if (!mask.Tiles.TryGetValue(mask.Key(point), out var tile)) return 0f;
        Vector2 pixel = (point - mask.Origin(tile.Key)) * RailBallastMask.PixelsPerUnit + Vector2.one * .5f;
        int x = Mathf.Clamp(Mathf.RoundToInt(pixel.x), 0, mask.PixelSize - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(pixel.y), 0, mask.PixelSize - 1);
        return tile.Pixels[z * mask.PixelSize + x] / 255f;
    }
    private static RailBallastMask MakeMask(params Vector2[][] paths)
    {
        var mask = new RailBallastMask(16);
        for (int i = 0; i < paths.Length; i++) mask.Upsert(i, paths[i], Style.BallastHalfWidth);
        Check(mask.Rebuild(), "Installed paths produce terrain mask updates");
        return mask;
    }
    private static void SameMask(RailBallastMask expected, RailBallastMask actual, string message)
    {
        Check(expected.Tiles.Count == actual.Tiles.Count, message + ": tile count");
        foreach (var pair in expected.Tiles)
        {
            Check(actual.Tiles.TryGetValue(pair.Key, out var tile), message + ": tile key");
            for (int i = 0; i < pair.Value.Pixels.Length; i++)
                Check(Math.Abs(pair.Value.Pixels[i] - tile.Pixels[i]) <= 1, message + ": max-union coverage");
        }
    }
    private static void BallastTerrainMask()
    {
        Near(Style.BallastHalfWidth, .64f, "Ballast extends past timber sleeper ends");
        var whole = MakeMask(Line(-4, 40));
        SameMask(whole, MakeMask(Line(40, -4)), "Reversed installation");
        SameMask(whole, MakeMask(Line(-4, 8), Line(8, 23), Line(23, 40)), "Split installations");
        SameMask(whole, MakeMask(Line(-4, 30), Line(10, 40), Line(40, -4)), "Overlaps and duplicate rails");
        foreach (float x in new[] { -4f, -3f, 0f, 15.5f, 31.5f, 39f, 40f })
            Check(MaskValue(whole, new Vector2(x, 0)) > .99f, "Continuous gravel across negative coordinates and terrain chunks");
        Check(MaskValue(whole, new Vector2(-4.4f, 0)) > .5f, "Rounded gravel cap fades before the rail endpoint");
        float innerShoulder = MaskValue(whole, new Vector2(3, .4f));
        float middleShoulder = MaskValue(whole, new Vector2(3, .5f));
        float outerShoulder = MaskValue(whole, new Vector2(3, .6f));
        Check(innerShoulder < 1f && innerShoulder > middleShoulder && middleShoulder > outerShoulder && outerShoulder > 0f,
            "Gravel blends progressively into the ground over a broad shoulder instead of one sharp pixel");
        Check(MaskValue(whole, new Vector2(-4.9f, 0)) == 0f, "Gravel stops outside its endpoint cap");
        Check(MaskValue(whole, new Vector2(3, .9f)) == 0f, "Ground outside the gravel shoulder is unchanged");
        foreach (var pair in whole.Tiles)
        {
            if (!whole.Tiles.TryGetValue(pair.Key + Vector2Int.right, out var right)) continue;
            int size = whole.PixelSize;
            for (int z = 0; z < size; z++) for (int border = 0; border < 2; border++)
                Check(Math.Abs(pair.Value.Pixels[z * size + size - 2 + border] - right.Pixels[z * size + border]) <= 1,
                    "Bilinear border texels agree across terrain chunk seams");
        }
        var junction = MakeMask(Line(-5, 5), new[] { new Vector2(0, -5), new Vector2(0, 5) });
        Check(MaskValue(junction, Vector2.zero) == 1f, "Crossings form one opaque gravel area");
        Check(MaskValue(junction, new Vector2(0, 4)) == 1f, "Branch arm remains covered");
        junction.Remove(0); junction.Rebuild();
        Check(MaskValue(junction, Vector2.zero) == 1f, "Demolishing one crossing preserves the other line");
        Check(MaskValue(junction, new Vector2(4, 0)) == 0f, "Demolition clears gravel only where no rail remains");
        junction.Remove(1); junction.Rebuild();
        Check(junction.Tiles.Count == 0, "Last demolition releases every empty mask tile");
        foreach (var tile in junction.Changed) Check(tile.Sources.Count == 0, "Empty tiles notify texture disposal");
        var curve = new[] { new Vector2(0, 0), new Vector2(3, 0), new Vector2(5, 2), new Vector2(5, 6) };
        var curved = MakeMask(curve);
        foreach (Vector2 point in curve) Check(MaskValue(curved, point) == 1f, "Curve joints have no holes");
        SameMask(curved, MakeMask(Reverse(curve)), "Reversed curves");
        var diagonal = MakeMask(new[] { new Vector2(-64, -64), new Vector2(64, 64) });
        Check(diagonal.Tiles.Count < 40, "Long diagonals index a narrow band instead of a rectangular map area");
        var edits = MakeMask(Line(1, 8, 4), Line(101, 108, 104));
        var distantKey = edits.Key(new Vector2(104, 104));
        var cached = edits.Tiles[distantKey];
        edits.Upsert(0, Line(1, 9, 4), Style.BallastHalfWidth); edits.Rebuild();
        Check(ReferenceEquals(cached, edits.Tiles[distantKey]), "Unrelated terrain masks keep their cache");
        foreach (var tile in edits.Changed) Check(tile.Key != distantKey, "A local edit uploads no distant texture");
        edits.Upsert(0, Line(20, 25, 4), Style.BallastHalfWidth); edits.Rebuild();
        Check(MaskValue(edits, new Vector2(3, 4)) == 0f && MaskValue(edits, new Vector2(22, 4)) == 1f,
            "Moving a native path clears the old mask and draws the new location");
        var invalid = new RailBallastMask(16);
        invalid.Upsert(0, new[] { Vector2.zero, Vector2.zero, new Vector2(float.NaN, 0) }, .64f);
        Check(!invalid.Rebuild() && invalid.Tiles.Count == 0, "Invalid and zero-length paths create no ground marks");
        for (int i = 0; i < 10; i++) edits.Rebuild();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) edits.Rebuild();
        Check(GC.GetAllocatedBytesForCurrentThread() - before == 0, "Idle terrain mask updates allocate zero bytes");
    }
    private static void EditsAndCaches()
    {
        var network = Build(Line(0, 4), Line(4, 8), Line(8, 12), Line(100, 110));
        RailVisualNetwork.Group untouched = null;
        foreach (var group in network.Added) if (group.Runs[0].Points[0].x > 50) untouched = group;
        network.Remove(1); network.Rebuild();
        Check(network.Removed.Count == 1 && network.Added.Count == 2, "Deleting a bridge only rebuilds the affected component");
        Check(!network.Removed.Contains(untouched), "Unrelated component mesh cache survives deletion");
        foreach (var group in network.Added) Near(Length(group.Runs[0]), 4, "Deletion restores each remaining open rail");
        network.Upsert(1, Line(4, 8), .1f, Style); network.Rebuild();
        Check(network.Removed.Count == 2 && network.Added.Count == 1, "Reconnect combines both invalidated components");
        Near(Length(network.Added[0].Runs[0]), 12, "Reconnect continuous length");
        network.Upsert(4, Line(2, 10), .1f, Style); network.Rebuild();
        var duplicate = Sleepers(network);
        network.Remove(4); network.Rebuild();
        Check(duplicate.Count == Sleepers(network).Count, "Removing a duplicate does not remove the surviving boards");
        network.Upsert(1, Line(4, 8, 20), .1f, Style); network.Rebuild();
        Check(network.Added.Count == 3, "Moving a path clears the old connection and indexes the new location");
        network.Remove(0); network.Remove(1); network.Remove(2); network.Remove(3); network.Rebuild();
        Check(network.Added.Count == 0, "Removing every source leaves no geometry");
    }

    private static void SparseAndIdle()
    {
        var network = new RailVisualNetwork();
        for (int i = 0; i < 1000; i++) network.Upsert(i, Line(i * 10, i * 10 + 3), .1f, Style);
        network.Rebuild();
        Check(network.Added.Count == 1000, "Sparse source count");
        Check(network.IntersectionChecks < 100000, "Sparse geometry must use spatial candidates, not all pairs");
        long checksBefore = network.IntersectionChecks;
        network.Upsert(0, Line(0, 4), .1f, Style); network.Rebuild();
        Check(network.Added.Count == 1 && network.Removed.Count == 1, "A sparse edit rebuilds one component");
        Check(network.IntersectionChecks - checksBefore < 200, "A sparse edit does not rescan distant paths");
        for (int i = 0; i < 10; i++) network.Rebuild();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) network.Rebuild();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "Unchanged-frame presentation layout must allocate zero bytes");
        Console.WriteLine($"Sparse: {network.IntersectionChecks} intersection checks; idle: {allocated} bytes");
    }

    private static void MeshGeometry()
    {
        var whole = Build(Line(0, 80));
        var overlap = Build(Line(0, 34), Line(31, 64), Line(60, 80));
        Same(whole, overlap, "Sleeper phase across 32-unit render cells");
        var vertices = new List<Vector3>(); var indices = new List<int>();
        var run = overlap.Added[0].Runs[0];
        Railload.AppendContinuousRails(Style, vertices, indices, run.Points);
        int wholeRailVertices = vertices.Count;
        foreach (int index in indices) Check(index >= 0 && index < vertices.Count, "Valid rail mesh indices");
        vertices.Clear(); indices.Clear();
        Railload.AppendContinuousRails(Style, vertices, indices, whole.Added[0].Runs[0].Points);
        Check(vertices.Count == wholeRailVertices, "Overlapping segments generate only one rail mesh");
        vertices.Clear(); indices.Clear();
        var sleeper = overlap.Added[0].Sleepers[0];
        Railload.AppendContinuousSleeper(Style, vertices, indices, sleeper.Position, sleeper.Tangent);
        Check(vertices.Count == 24 && indices.Count == 36, "Sleeper box keeps independent face normals");
        for (int i = 0; i < 4; i++) Near(vertices[i].y, .035f, "Sleeper top stays below rail with intentional overlap");
        foreach (int index in indices) Check(index >= 0 && index < vertices.Count, "Valid sleeper mesh indices");
        vertices.Clear(); indices.Clear();
        Railload.PreviewSleepers(vertices, indices, whole.Added[0].Runs[0].Points);
        Check(vertices.Count == whole.Added[0].Sleepers.Count * 24, "Isolated preview and installed run use the same layout");
        var loop = new Vector2[33];
        for (int i = 0; i < 32; i++) loop[i] = new Vector2(3 * MathF.Cos(i * MathF.PI / 16), 3 * MathF.Sin(i * MathF.PI / 16));
        loop[32] = loop[0];
        var network = Build(loop);
        run = network.Added[0].Runs[0];
        vertices.Clear(); indices.Clear();
        Railload.AppendContinuousRails(Style, vertices, indices, run.Points);
        int last = (run.Points.Count - 1) * 2;
        Near(Vector3.Distance(vertices[0], vertices[last]), 0, "Closed rail strip seam outer edges meet");
        Near(Vector3.Distance(vertices[1], vertices[last + 1]), 0, "Closed rail strip seam inner edges meet");
    }

    private static void UnevenCurveSampling()
    {
        // A long coalesced straight meets a gently curving, densely sampled path.
        // The curve itself has ample radius; sample lengths must not twist the offsets.
        var points = new Vector2[35];
        points[0] = new Vector2(-20, 0);
        points[1] = Vector2.zero;
        for (int i = 2; i < points.Length; i++)
        {
            float x = (i - 1) * .01f;
            points[i] = new Vector2(x, .05f * x + .1f * x * x);
        }
        foreach (bool reverse in new[] { false, true })
        foreach (int quarterTurns in new[] { 0, 1, 2, 3 })
        {
            var transformed = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 p = points[i];
                for (int j = 0; j < quarterTurns; j++) p = new Vector2(-p.y, p.x);
                transformed[i] = p;
            }
            if (reverse) Array.Reverse(transformed);
            var network = Build(transformed);
            foreach (var group in network.Added) foreach (var run in group.Runs)
            {
                var vertices = new List<Vector3>(); var indices = new List<int>();
                Railload.AppendContinuousRails(Style, vertices, indices, run.Points);
                for (int side = 0; side < 2; side++)
                {
                    int first = side * 8 * run.Points.Count, last = first + 2 * run.Points.Count;
                    for (int i = 0; i < indices.Count; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                        if (a < first || a >= last || b < first || b >= last || c < first || c >= last) continue;
                        Check(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y > 0f,
                            "Unequal sampling must not fold a top triangle into a visible rail gap");
                    }
                }
            }
        }
    }
}
