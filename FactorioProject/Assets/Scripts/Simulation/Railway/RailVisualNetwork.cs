using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    // An arrangement of visible centerlines, independent of routing and storage.
    // Only components touched by an edit are rebuilt. Geometry intersections use
    // millimetre tolerance, not the much wider train connection/snap distance.
    internal sealed class RailVisualNetwork
    {
        internal const float JoinTolerance = 0.001f;
        private const float IndexSize = 2f;
        private const float SegmentLength = 1f;
        private readonly Dictionary<object, Source> sources = new Dictionary<object, Source>();
        private readonly Dictionary<Vector2Int, List<Segment>> spatial = new Dictionary<Vector2Int, List<Segment>>();
        private readonly HashSet<Group> invalidGroups = new HashSet<Group>();
        private readonly HashSet<Source> pending = new HashSet<Source>();
        private readonly List<Source> queue = new List<Source>();
        private readonly HashSet<Source> visited = new HashSet<Source>();
        private readonly List<Segment> segments = new List<Segment>();
        private readonly Dictionary<Vector2Int, List<Node>> nodeBuckets = new Dictionary<Vector2Int, List<Node>>();
        private readonly List<Node> nodes = new List<Node>();
        private readonly HashSet<(int, int)> edgeKeys = new HashSet<(int, int)>();
        private readonly Dictionary<Vector2Int, List<(Vector3 Point, Run Run)>> sleeperBuckets =
            new Dictionary<Vector2Int, List<(Vector3 Point, Run Run)>>();
        private int nextSegmentId;

        internal readonly List<Group> Removed = new List<Group>();
        internal readonly List<Group> Added = new List<Group>();
        internal long IntersectionChecks { get; private set; }

        internal sealed class Group
        {
            internal readonly List<Source> Sources = new List<Source>();
            internal readonly List<Run> Runs = new List<Run>();
            internal readonly List<Sleeper> Sleepers = new List<Sleeper>();
            internal RailVisualStyle Style;
        }

        internal sealed class Run
        {
            internal Group Owner;
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal bool Closed;
            internal bool PassesJunction;
            internal int StartDegree, EndDegree;
        }

        internal readonly struct Sleeper
        {
            internal readonly Group Owner;
            internal readonly Vector3 Position, Tangent;
            internal Sleeper(Group owner, Vector3 position, Vector3 tangent)
            { Owner = owner; Position = position; Tangent = tangent; }
        }

        internal sealed class Source
        {
            internal readonly List<Segment> Segments = new List<Segment>();
            internal readonly HashSet<Source> Neighbors = new HashSet<Source>();
            internal Group Group;
            internal RailVisualStyle Style;
            internal bool Registered;
        }

        internal sealed class Segment
        {
            internal Source Owner;
            internal Vector3 A, B;
            internal int Id;
            internal readonly List<float> Cuts = new List<float>();
        }

        private sealed class Node
        {
            internal int Id;
            internal Vector3 Point;
            internal readonly List<Edge> Edges = new List<Edge>();
            internal Edge MainlineA, MainlineB;
        }

        private sealed class Edge
        {
            internal Node A, B;
            internal bool Used;
            internal Node Other(Node node) => ReferenceEquals(A, node) ? B : A;
        }

        internal void Upsert(object key, IReadOnlyList<Vector2> points, float height, RailVisualStyle style)
        {
            Remove(key);
            if (key == null || points == null || points.Count < 2) return;
            var source = new Source { Style = style, Registered = true };
            sources.Add(key, source);
            pending.Add(source);
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 a = new Vector3(points[i - 1].x, height, points[i - 1].y);
                Vector3 b = new Vector3(points[i].x, height, points[i].y);
                if (!Finite(a) || !Finite(b)) continue;
                float length = Vector3.Distance(a, b);
                if (length <= JoinTolerance) continue;
                int count = Mathf.Max(1, Mathf.CeilToInt(length / SegmentLength));
                for (int part = 0; part < count; part++)
                {
                    var segment = new Segment { Owner = source, Id = nextSegmentId++,
                        A = Vector3.Lerp(a, b, (float)part / count), B = Vector3.Lerp(a, b, (float)(part + 1) / count) };
                    source.Segments.Add(segment);
                    Index(segment);
                }
            }
        }

        internal void Remove(object key)
        {
            if (key == null || !sources.TryGetValue(key, out Source source)) return;
            sources.Remove(key);
            source.Registered = false;
            Invalidate(source);
            foreach (Source neighbor in source.Neighbors)
            {
                neighbor.Neighbors.Remove(source);
                Invalidate(neighbor);
            }
            source.Neighbors.Clear();
            for (int i = 0; i < source.Segments.Count; i++)
            {
                Segment segment = source.Segments[i];
                Bounds(segment, out Vector2Int min, out Vector2Int max);
                for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!spatial.TryGetValue(cell, out List<Segment> bucket)) continue;
                    bucket.Remove(segment);
                    if (bucket.Count == 0) spatial.Remove(cell);
                }
            }
        }

        private void Invalidate(Source source)
        {
            pending.Add(source);
            if (source.Group != null) invalidGroups.Add(source.Group);
        }

        private void Index(Segment segment)
        {
            Bounds(segment, out Vector2Int min, out Vector2Int max);
            for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++)
            {
                var key = new Vector2Int(x, y);
                if (!spatial.TryGetValue(key, out List<Segment> bucket))
                    spatial.Add(key, bucket = new List<Segment>());
                for (int i = 0; i < bucket.Count; i++)
                {
                    Segment other = bucket[i];
                    if (ReferenceEquals(other.Owner, segment.Owner) || !Compatible(segment, other)) continue;
                    IntersectionChecks++;
                    if (!Intersection(segment, other, out _, out _, out _)) continue;
                    if (segment.Owner.Neighbors.Add(other.Owner))
                    {
                        other.Owner.Neighbors.Add(segment.Owner);
                        Invalidate(other.Owner);
                    }
                }
                bucket.Add(segment);
            }
        }

        internal bool Rebuild()
        {
            Removed.Clear(); Added.Clear();
            if (pending.Count == 0) return false;
            queue.Clear(); visited.Clear();
            foreach (Group group in invalidGroups)
            {
                Removed.Add(group);
                for (int i = 0; i < group.Sources.Count; i++) Enqueue(group.Sources[i]);
            }
            foreach (Source source in pending) Enqueue(source);
            // A newly inserted source can join several formerly cached components.
            for (int i = 0; i < queue.Count; i++)
                foreach (Source neighbor in queue[i].Neighbors) Enqueue(neighbor);
            visited.Clear();
            for (int i = 0; i < queue.Count; i++)
            {
                Source source = queue[i];
                if (!visited.Add(source)) continue;
                var group = new Group { Style = source.Style };
                group.Sources.Add(source);
                for (int j = 0; j < group.Sources.Count; j++)
                {
                    Source member = group.Sources[j];
                    member.Group = group;
                    foreach (Source neighbor in member.Neighbors)
                        if (neighbor.Registered && visited.Add(neighbor)) group.Sources.Add(neighbor);
                }
                Build(group);
                Added.Add(group);
            }
            invalidGroups.Clear(); pending.Clear(); queue.Clear(); visited.Clear();
            return true;
        }

        internal void AcknowledgeChanges() { Removed.Clear(); Added.Clear(); }

        private void Enqueue(Source source)
        { if (source.Registered && visited.Add(source)) queue.Add(source); }

        private void Build(Group group)
        {
            segments.Clear(); nodes.Clear(); nodeBuckets.Clear(); edgeKeys.Clear(); sleeperBuckets.Clear();
            for (int i = 0; i < group.Sources.Count; i++)
                for (int j = 0; j < group.Sources[i].Segments.Count; j++)
                {
                    Segment segment = group.Sources[i].Segments[j];
                    segment.Cuts.Clear(); segment.Cuts.Add(0f); segment.Cuts.Add(1f);
                    segments.Add(segment);
                }
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                Bounds(segment, out Vector2Int min, out Vector2Int max);
                for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++)
                {
                    if (!spatial.TryGetValue(new Vector2Int(x, y), out List<Segment> bucket)) continue;
                    for (int j = 0; j < bucket.Count; j++)
                    {
                        Segment other = bucket[j];
                        if (other.Id >= segment.Id || !ReferenceEquals(other.Owner.Group, group) || !Compatible(segment, other)) continue;
                        IntersectionChecks++;
                        if (!Intersection(segment, other, out float t, out float u, out bool collinear)) continue;
                        if (collinear)
                        {
                            AddProjection(segment, other.A); AddProjection(segment, other.B);
                            AddProjection(other, segment.A); AddProjection(other, segment.B);
                        }
                        else { segment.Cuts.Add(t); other.Cuts.Add(u); }
                    }
                }
            }
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                segment.Cuts.Sort();
                for (int j = 1; j < segment.Cuts.Count; j++)
                {
                    Vector3 a = Vector3.Lerp(segment.A, segment.B, segment.Cuts[j - 1]);
                    Vector3 b = Vector3.Lerp(segment.A, segment.B, segment.Cuts[j]);
                    if ((a - b).sqrMagnitude <= JoinTolerance * JoinTolerance) continue;
                    Node first = GetNode(a), second = GetNode(b);
                    if (ReferenceEquals(first, second)) continue;
                    var edgeKey = first.Id < second.Id ? (first.Id, second.Id) : (second.Id, first.Id);
                    if (!edgeKeys.Add(edgeKey)) continue;
                    var edge = new Edge { A = first, B = second };
                    first.Edges.Add(edge); second.Edges.Add(edge);
                }
            }
            nodes.Sort(CompareNodes);
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                // Order outgoing edges geometrically, independent of installation order.
                for (int a = 1; a < node.Edges.Count; a++)
                    for (int b = a; b > 0 && ComparePoints(node.Edges[b].Other(node).Point,
                        node.Edges[b - 1].Other(node).Point) < 0; b--)
                    { Edge swap = node.Edges[b]; node.Edges[b] = node.Edges[b - 1]; node.Edges[b - 1] = swap; }
                ResolveMainline(node);
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                for (int j = 0; j < node.Edges.Count; j++)
                    if (Continuation(node, node.Edges[j]) == null) Walk(group, node, node.Edges[j]);
            }
            // Remaining degree-two components are closed loops.
            for (int i = 0; i < nodes.Count; i++)
                for (int j = 0; j < nodes[i].Edges.Count; j++) Walk(group, nodes[i], nodes[i].Edges[j]);
            // Preserve through-line pitch first; branch arms yield at the junction.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < group.Runs.Count; i++)
                    if (group.Runs[i].PassesJunction == (pass == 0)) PlaceSleepers(group, group.Runs[i]);
            segments.Clear(); nodes.Clear(); nodeBuckets.Clear(); edgeKeys.Clear(); sleeperBuckets.Clear();
        }

        private static void ResolveMainline(Node node)
        {
            if (node.Edges.Count <= 2) return;
            float bestDot = -0.5f;
            for (int i = 0; i < node.Edges.Count; i++)
            for (int j = i + 1; j < node.Edges.Count; j++)
            {
                Vector3 first = (node.Edges[i].Other(node).Point - node.Point).normalized;
                Vector3 second = (node.Edges[j].Other(node).Point - node.Point).normalized;
                float dot = Vector3.Dot(first, second);
                if (dot >= bestDot - 0.00001f) continue;
                bestDot = dot;
                node.MainlineA = node.Edges[i]; node.MainlineB = node.Edges[j];
            }
        }

        private static Edge Continuation(Node node, Edge incoming)
        {
            if (node.Edges.Count == 2)
                return ReferenceEquals(incoming, node.Edges[0]) ? node.Edges[1] : node.Edges[0];
            if (ReferenceEquals(incoming, node.MainlineA)) return node.MainlineB;
            return ReferenceEquals(incoming, node.MainlineB) ? node.MainlineA : null;
        }

        private static void Walk(Group group, Node start, Edge edge)
        {
            if (edge.Used) return;
            var run = new Run { Owner = group, StartDegree = start.Edges.Count };
            run.Points.Add(start.Point);
            Node node = start;
            while (!edge.Used)
            {
                edge.Used = true;
                node = edge.Other(node);
                run.Points.Add(node.Point);
                if (ReferenceEquals(node, start)) { run.Closed = true; break; }
                Edge next = Continuation(node, edge);
                if (next == null) break;
                if (node.Edges.Count > 2) run.PassesJunction = true;
                edge = next;
            }
            run.EndDegree = node.Edges.Count;
            // Intermediate collinear split points must not alter rail-strip tangents.
            int retained = 0;
            int originalCount = run.Points.Count;
            for (int i = 0; i < originalCount; i++)
            {
                Vector3 point = run.Points[i];
                while (retained >= 2)
                {
                    Vector3 a = run.Points[retained - 1] - run.Points[retained - 2];
                    Vector3 b = point - run.Points[retained - 1];
                    if (Vector3.Dot(a.normalized, b.normalized) <= 0.999999f) break;
                    retained--;
                }
                run.Points[retained++] = point;
            }
            if (retained < originalCount) run.Points.RemoveRange(retained, originalCount - retained);
            group.Runs.Add(run);
        }

        private void PlaceSleepers(Group group, Run run)
        {
            float length = 0f;
            for (int i = 1; i < run.Points.Count; i++) length += Vector3.Distance(run.Points[i - 1], run.Points[i]);
            float junctionClearance = Mathf.Max(group.Style.SleeperSpacing * 0.5f,
                (group.Style.SleeperLength + group.Style.SleeperWidth) * 0.5f);
            RailSleeperLayout layout = RailSleeperLayout.Create(length, group.Style.SleeperSpacing, run.Closed,
                run.StartDegree > 2 ? junctionClearance : 0f, run.EndDegree > 2 ? junctionClearance : 0f);
            int segment = 1;
            float traversed = 0f;
            for (int i = 0; i < layout.Count; i++)
            {
                float distance = layout.Start + i * layout.Step;
                float segmentLength = Vector3.Distance(run.Points[segment - 1], run.Points[segment]);
                while (segment + 1 < run.Points.Count && traversed + segmentLength < distance)
                {
                    traversed += segmentLength; segment++;
                    segmentLength = Vector3.Distance(run.Points[segment - 1], run.Points[segment]);
                }
                Vector3 tangent = (run.Points[segment] - run.Points[segment - 1]).normalized;
                Vector3 point = run.Points[segment - 1] + tangent * (distance - traversed);
                if (!AcceptSleeper(point, run, group.Style.SleeperSpacing * 0.75f)) continue;
                group.Sleepers.Add(new Sleeper(group, point, tangent));
            }
        }

        private bool AcceptSleeper(Vector3 point, Run run, float separation)
        {
            separation = Mathf.Max(0.075f, separation);
            var cell = new Vector2Int(Mathf.FloorToInt(point.x / separation), Mathf.FloorToInt(point.z / separation));
            for (int x = cell.x - 1; x <= cell.x + 1; x++) for (int y = cell.y - 1; y <= cell.y + 1; y++)
                if (sleeperBuckets.TryGetValue(new Vector2Int(x, y), out var bucket))
                    for (int i = 0; i < bucket.Count; i++)
                        if (!ReferenceEquals(bucket[i].Run, run)
                            && (bucket[i].Point - point).sqrMagnitude < separation * separation - 0.00001f) return false;
            if (!sleeperBuckets.TryGetValue(cell, out var points))
                sleeperBuckets.Add(cell, points = new List<(Vector3 Point, Run Run)>());
            points.Add((point, run));
            return true;
        }

        private Node GetNode(Vector3 point)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(point.x / JoinTolerance), Mathf.FloorToInt(point.z / JoinTolerance));
            for (int x = cell.x - 1; x <= cell.x + 1; x++) for (int y = cell.y - 1; y <= cell.y + 1; y++)
                if (nodeBuckets.TryGetValue(new Vector2Int(x, y), out List<Node> bucket))
                    for (int i = 0; i < bucket.Count; i++)
                        if ((bucket[i].Point - point).sqrMagnitude <= JoinTolerance * JoinTolerance) return bucket[i];
            var node = new Node { Id = nodes.Count, Point = point };
            nodes.Add(node);
            if (!nodeBuckets.TryGetValue(cell, out List<Node> points)) nodeBuckets.Add(cell, points = new List<Node>());
            points.Add(node);
            return node;
        }

        private static bool Compatible(Segment a, Segment b) => a.Owner.Style.Equals(b.Owner.Style)
            && Mathf.Abs(a.A.y - b.A.y) <= JoinTolerance;

        private static bool Intersection(Segment a, Segment b, out float t, out float u, out bool collinear)
        {
            Vector2 p = new Vector2(a.A.x, a.A.z), r = new Vector2(a.B.x - a.A.x, a.B.z - a.A.z);
            Vector2 q = new Vector2(b.A.x, b.A.z), s = new Vector2(b.B.x - b.A.x, b.B.z - b.A.z);
            float cross = Cross(r, s);
            collinear = Mathf.Abs(cross) <= 0.000001f * Mathf.Sqrt(r.sqrMagnitude * s.sqrMagnitude);
            t = u = 0f;
            if (collinear)
            {
                if (Mathf.Abs(Cross(q - p, r)) > JoinTolerance * r.magnitude) return false;
                float first = Vector2.Dot(q - p, r) / r.sqrMagnitude;
                float last = Vector2.Dot(q + s - p, r) / r.sqrMagnitude;
                return Mathf.Max(first, last) >= -JoinTolerance / r.magnitude
                    && Mathf.Min(first, last) <= 1f + JoinTolerance / r.magnitude;
            }
            t = Cross(q - p, s) / cross; u = Cross(q - p, r) / cross;
            float tTolerance = JoinTolerance / r.magnitude, uTolerance = JoinTolerance / s.magnitude;
            if (t < -tTolerance || t > 1f + tTolerance || u < -uTolerance || u > 1f + uTolerance) return false;
            t = Mathf.Clamp01(t); u = Mathf.Clamp01(u);
            return true;
        }

        private static void AddProjection(Segment segment, Vector3 point)
        {
            Vector3 direction = segment.B - segment.A;
            float t = Vector3.Dot(point - segment.A, direction) / direction.sqrMagnitude;
            segment.Cuts.Add(Mathf.Clamp01(t));
        }

        private static void Bounds(Segment segment, out Vector2Int min, out Vector2Int max)
        {
            min = new Vector2Int(Mathf.FloorToInt((Mathf.Min(segment.A.x, segment.B.x) - JoinTolerance) / IndexSize),
                Mathf.FloorToInt((Mathf.Min(segment.A.z, segment.B.z) - JoinTolerance) / IndexSize));
            max = new Vector2Int(Mathf.FloorToInt((Mathf.Max(segment.A.x, segment.B.x) + JoinTolerance) / IndexSize),
                Mathf.FloorToInt((Mathf.Max(segment.A.z, segment.B.z) + JoinTolerance) / IndexSize));
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x)
            && !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        private static int CompareNodes(Node a, Node b) => ComparePoints(a.Point, b.Point);
        private static int ComparePoints(Vector3 a, Vector3 b)
        {
            int comparison = a.x.CompareTo(b.x);
            if (comparison == 0) comparison = a.z.CompareTo(b.z);
            return comparison != 0 ? comparison : a.y.CompareTo(b.y);
        }
    }
}
