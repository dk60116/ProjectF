using System;
using System.Collections.Generic;

namespace ProjectF.Simulation
{
    public readonly struct RailPoint
    {
        public readonly float X, Y;
        public RailPoint(float x, float y) { X = x; Y = y; }
    }
    public interface IRailPathPoints
    {
        int Count { get; }
        RailPoint GetPoint(int index);
    }
    public static class RailPathSampling
    {
        // The source is a struct adapter or a data view. No delegate/boxing per sample.
        public static bool TrySample<TPoints>(TPoints points, IReadOnlyList<float> cumulativeDistances,
            float pathLength, float distance, out RailPoint point, out RailPoint tangent)
            where TPoints : struct, IRailPathPoints
        {
            point = tangent = default;
            if (cumulativeDistances == null || points.Count < 2 || cumulativeDistances.Count != points.Count) return false;
            float target = Math.Max(0, Math.Min(Math.Max(0, pathLength), distance));
            int low = 0, high = cumulativeDistances.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (cumulativeDistances[middle] < target) low = middle + 1;
                else high = middle;
            }
            int end = Math.Max(1, Math.Min(points.Count - 1, low)), start = end - 1;
            float length = cumulativeDistances[end] - cumulativeDistances[start];
            while (length <= 0.0001f && end > 1)
            { end--; start--; length = cumulativeDistances[end] - cumulativeDistances[start]; }
            while (length <= 0.0001f && end + 1 < points.Count)
            { start = end; end++; length = cumulativeDistances[end] - cumulativeDistances[start]; }
            if (length <= 0.0001f) return false;
            RailPoint a = points.GetPoint(start), b = points.GetPoint(end);
            float t = Math.Max(0, Math.Min(1, (target - cumulativeDistances[start]) / length));
            point = new RailPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            tangent = new RailPoint((b.X - a.X) / length, (b.Y - a.Y) / length);
            return true;
        }
    }
}
