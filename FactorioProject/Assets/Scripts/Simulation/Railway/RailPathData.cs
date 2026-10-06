using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    // Geometry shared by rail views, topology queries and train sampling.
    public sealed class RailPathData
    {
        private readonly List<Vector2> sourcePoints = new List<Vector2>();
        private readonly List<Vector2Int> coordinates = new List<Vector2Int>();
        private readonly List<Vector2> samples = new List<Vector2>();
        private readonly List<float> distances = new List<float>();
        private readonly List<Vector3> centerScratch = new List<Vector3>();
        private Vector2Int anchor;
        private bool extendsStart, extendsEnd, configured;

        public IReadOnlyList<Vector2> SourcePoints => sourcePoints;
        public IReadOnlyList<Vector2Int> Coordinates => coordinates;
        public IReadOnlyList<Vector2> Samples => samples;
        public float Length { get; private set; }
        public int Version { get; private set; }
        public bool IsValid => samples.Count >= 2;
        public Vector2 ConnectionStart { get; private set; }
        public Vector2 ConnectionEnd { get; private set; }

        public bool Matches(IReadOnlyList<Vector2> points, IReadOnlyList<Vector2Int> occupied,
            Vector2Int origin, bool extendStart, bool extendEnd)
        {
            return configured && anchor == origin && extendsStart == extendStart && extendsEnd == extendEnd
                && Equal(sourcePoints, points) && Equal(coordinates, occupied);
        }

        internal bool ContentEquals(RailPathData other)
            => other != null && Matches(other.sourcePoints, other.coordinates, other.anchor, other.extendsStart, other.extendsEnd);

        public void Configure(IReadOnlyList<Vector2> points, IReadOnlyList<Vector2Int> occupied,
            Vector2Int origin, bool extendStart, bool extendEnd)
        {
            if (Matches(points, occupied, origin, extendStart, extendEnd)) return;
            configured = true;
            anchor = origin;
            extendsStart = extendStart;
            extendsEnd = extendEnd;
            Copy(points, sourcePoints);
            Copy(occupied, coordinates);
            samples.Clear();
            if (sourcePoints.Count >= 2)
            {
                for (int i = 0; i < sourcePoints.Count; i++) AddSample(sourcePoints[i]);
                Railload.ExtendPathEndpointsToCellEdges2D(samples, extendStart, extendEnd);
            }
            else if (coordinates.Count >= 2)
            {
                centerScratch.Clear();
                Railload.BuildCenterPath(coordinates, origin, 0f, centerScratch);
                for (int i = 0; i < centerScratch.Count; i++)
                    AddSample(new Vector2(centerScratch[i].x + origin.x, centerScratch[i].z + origin.y));
            }
            RailConnectionUtility.TryResolveConnectionEndpoints(sourcePoints, coordinates,
                out Vector2 start, out Vector2 end);
            ConnectionStart = start;
            ConnectionEnd = end;
            Length = 0f;
            distances.Clear();
            if (samples.Count > 0) distances.Add(0f);
            for (int i = 1; i < samples.Count; i++)
            {
                Length += Vector2.Distance(samples[i - 1], samples[i]);
                distances.Add(Length);
            }
            Version++;
        }

        public bool TrySample(float distance, out Vector2 point, out Vector2 tangent)
        {
            bool found = Simulation.RailPathSampling.TrySample(new PointSource(samples), distances,
                Length, distance, out var p, out var t);
            point = new Vector2(p.X, p.Y);
            tangent = new Vector2(t.X, t.Y);
            return found;
        }

        public bool TryFindNearest(Vector2 point, out float distance, out Vector2 pathPoint,
            out Vector2 tangent, out float sqrDistance)
        {
            distance = 0f;
            pathPoint = point;
            tangent = Vector2.zero;
            sqrDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i + 1 < samples.Count; i++)
            {
                Vector2 segment = samples[i + 1] - samples[i];
                float length = segment.magnitude;
                if (length <= 0.0001f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(point - samples[i], segment) / (length * length));
                Vector2 candidate = Vector2.Lerp(samples[i], samples[i + 1], t);
                float candidateDistance = (point - candidate).sqrMagnitude;
                if (candidateDistance >= sqrDistance) continue;
                distance = distances[i] + length * t;
                pathPoint = candidate;
                tangent = segment / length;
                sqrDistance = candidateDistance;
                found = true;
            }
            return found;
        }

        private void AddSample(Vector2 point)
        {
            if (samples.Count == 0 || (samples[samples.Count - 1] - point).sqrMagnitude > 0.0001f)
                samples.Add(point);
        }

        private static bool Equal<T>(List<T> first, IReadOnlyList<T> second)
        {
            if (first.Count != (second?.Count ?? 0)) return false;
            for (int i = 0; i < first.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(first[i], second[i])) return false;
            return true;
        }

        private static void Copy<T>(IReadOnlyList<T> source, List<T> destination)
        {
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++) destination.Add(source[i]);
        }

        private readonly struct PointSource : Simulation.IRailPathPoints
        {
            private readonly IReadOnlyList<Vector2> points;
            public PointSource(IReadOnlyList<Vector2> points) { this.points = points; }
            public int Count => points.Count;
            public Simulation.RailPoint GetPoint(int index)
                => new Simulation.RailPoint(points[index].x, points[index].y);
        }
    }
}
