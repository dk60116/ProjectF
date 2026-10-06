using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

public sealed partial class RailLineDebugRenderer
{
    private static readonly System.Comparison<RailInfo> DisplayRailComparison = CompareDisplayRails;
    private readonly Dictionary<RailWorld.Record, RailInfo> displayRailsByRecord = new Dictionary<RailWorld.Record, RailInfo>();
    private readonly List<RailInfo> displayRailSeeds = new List<RailInfo>();
    private readonly List<RailInfo> displayRailNeighbors = new List<RailInfo>();
    private readonly List<RailWorld.Record> displayRailCandidates = new List<RailWorld.Record>();
    private readonly Queue<RailInfo> displayRailQueue = new Queue<RailInfo>();

    private void ResolveRailDisplayDirections(float distance)
    {
        displayRailsByRecord.Clear();
        displayRailSeeds.Clear();
        displayRailQueue.Clear();
        foreach (var rail in rails)
        {
            displayRailsByRecord.Add(rail.Record, rail);
            displayRailSeeds.Add(rail);
        }
        // Geometry, not placement order, chooses the root of each continuous line.
        displayRailSeeds.Sort(DisplayRailComparison);
        float maxSqrDistance = distance * distance;
        foreach (var seed in displayRailSeeds)
        {
            if (seed.DisplayDirection != 0) continue;
            var path = seed.Record.Path;
            int comparison = CompareDisplayPoints(path.ConnectionStart, path.ConnectionEnd);
            if (comparison == 0)
                comparison = CompareDisplayPoints(path.Samples[1], path.Samples[path.Samples.Count - 2]);
            seed.DisplayDirection = comparison <= 0 ? 1 : -1;
            displayRailQueue.Enqueue(seed);
            while (displayRailQueue.Count > 0)
            {
                RailInfo current = displayRailQueue.Dequeue();
                displayedRailWorld.CollectCandidates(current.Record, distance, displayRailCandidates, true);
                displayRailNeighbors.Clear();
                foreach (var candidate in displayRailCandidates)
                    if (displayRailsByRecord.TryGetValue(candidate, out var neighbor) && neighbor.DisplayDirection == 0)
                        displayRailNeighbors.Add(neighbor);
                displayRailNeighbors.Sort(DisplayRailComparison);
                foreach (var neighbor in displayRailNeighbors)
                {
                    int relation = ResolveDisplayDirectionRelation(current.Record.Path, neighbor.Record.Path, maxSqrDistance);
                    if (relation == 0) continue;
                    neighbor.DisplayDirection = current.DisplayDirection * relation;
                    displayRailQueue.Enqueue(neighbor);
                }
            }
        }
        displayRailsByRecord.Clear();
        displayRailSeeds.Clear();
        displayRailNeighbors.Clear();
        displayRailCandidates.Clear();
    }

    private static int CompareDisplayPoints(Vector2 left, Vector2 right)
    {
        int comparison = left.x.CompareTo(right.x);
        return comparison != 0 ? comparison : left.y.CompareTo(right.y);
    }

    private static int CompareDisplayRails(RailInfo left, RailInfo right)
    {
        var a = left.Record.Path;
        var b = right.Record.Path;
        bool aForward = CompareDisplayPoints(a.ConnectionStart, a.ConnectionEnd) <= 0;
        bool bForward = CompareDisplayPoints(b.ConnectionStart, b.ConnectionEnd) <= 0;
        int comparison = CompareDisplayPoints(aForward ? a.ConnectionStart : a.ConnectionEnd,
            bForward ? b.ConnectionStart : b.ConnectionEnd);
        if (comparison != 0) return comparison;
        return CompareDisplayPoints(aForward ? a.ConnectionEnd : a.ConnectionStart,
            bForward ? b.ConnectionEnd : b.ConnectionStart);
    }

    private static int ResolveDisplayDirectionRelation(RailPathData left, RailPathData right, float maxSqrDistance)
    {
        float bestDistance = float.MaxValue;
        int relation = 0;
        ConsiderDisplayConnection(left, right, true, maxSqrDistance, ref bestDistance, ref relation);
        ConsiderDisplayConnection(left, right, false, maxSqrDistance, ref bestDistance, ref relation);
        ConsiderDisplayConnection(right, left, true, maxSqrDistance, ref bestDistance, ref relation);
        ConsiderDisplayConnection(right, left, false, maxSqrDistance, ref bestDistance, ref relation);
        return relation;
    }

    private static void ConsiderDisplayConnection(RailPathData source, RailPathData target, bool sourceStart,
        float maxSqrDistance, ref float bestDistance, ref int relation)
    {
        Vector2 endpoint = sourceStart ? source.ConnectionStart : source.ConnectionEnd;
        if (!RailConnectionUtility.TryFindNearestPointOnConnectionPath(target.Coordinates, target.SourcePoints,
                endpoint, out float sqrDistance) || sqrDistance > maxSqrDistance || sqrDistance >= bestDistance)
            return;

        if (!source.TryFindNearest(endpoint, out _, out _, out Vector2 sourceTangent, out _)
            || !target.TryFindNearest(endpoint, out _, out _, out Vector2 targetTangent, out _)) return;
        float alignment = Vector2.Dot(sourceTangent, targetTangent);
        int candidate;
        if (Mathf.Abs(alignment) >= 0.0001f)
        {
            // Shared starts/ends can overlap rather than meet from opposite sides.
            candidate = alignment > 0f ? 1 : -1;
        }
        else
        {
            float startDistance = (endpoint - target.ConnectionStart).sqrMagnitude;
            float endDistance = (endpoint - target.ConnectionEnd).sqrMagnitude;
            if (Mathf.Min(startDistance, endDistance) > maxSqrDistance) return;
            // Only orthogonal endpoint joins need parity; a perpendicular interior crossing has no continuation.
            bool targetStart = startDistance <= endDistance;
            candidate = sourceStart == targetStart ? -1 : 1;
        }
        bestDistance = sqrDistance;
        relation = candidate;
    }
}
