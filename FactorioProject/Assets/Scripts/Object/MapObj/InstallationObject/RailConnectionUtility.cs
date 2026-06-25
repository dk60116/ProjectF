using System.Collections.Generic;
using UnityEngine;

public static class RailConnectionUtility
{
    public const float ConnectionDistance = Railload.ConnectionEndpointSnapMaxDistance;

    public static bool AreConnected(
        IReadOnlyList<Vector2Int> leftCoordinates,
        IReadOnlyList<Vector2> leftPathPoints,
        Vector2 leftStartPoint,
        Vector2 leftEndPoint,
        IReadOnlyList<Vector2Int> rightCoordinates,
        IReadOnlyList<Vector2> rightPathPoints,
        Vector2 rightStartPoint,
        Vector2 rightEndPoint,
        float maxConnectionSqrDistance)
    {
        return ShareOccupiedCoordinate(leftCoordinates, rightCoordinates)
               || IsEndpointNearPath(leftStartPoint, rightCoordinates, rightPathPoints, maxConnectionSqrDistance)
               || IsEndpointNearPath(leftEndPoint, rightCoordinates, rightPathPoints, maxConnectionSqrDistance)
               || IsEndpointNearPath(rightStartPoint, leftCoordinates, leftPathPoints, maxConnectionSqrDistance)
               || IsEndpointNearPath(rightEndPoint, leftCoordinates, leftPathPoints, maxConnectionSqrDistance);
    }

    public static bool TryResolveConnectionEndpoints(
        IReadOnlyList<Vector2> pathPoints,
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        out Vector2 startPoint,
        out Vector2 endPoint)
    {
        startPoint = Vector2.zero;
        endPoint = Vector2.zero;

        if (pathPoints != null && pathPoints.Count >= 2)
        {
            startPoint = pathPoints[0];
            endPoint = pathPoints[pathPoints.Count - 1];
            return true;
        }

        if (occupiedCoordinates == null || occupiedCoordinates.Count < 2)
        {
            return false;
        }

        startPoint = occupiedCoordinates[0];
        endPoint = occupiedCoordinates[occupiedCoordinates.Count - 1];
        return true;
    }

    public static bool TryFindNearestPointOnConnectionPath(
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        IReadOnlyList<Vector2> pathPoints,
        Vector2 point,
        out float sqrDistance)
    {
        sqrDistance = float.MaxValue;
        if (pathPoints != null && pathPoints.Count >= 2)
        {
            return TryFindNearestPointOnPath(pathPoints, point, out sqrDistance);
        }

        if (occupiedCoordinates == null || occupiedCoordinates.Count < 2)
        {
            return false;
        }

        Vector2 previousPoint = occupiedCoordinates[0];
        for (int i = 1; i < occupiedCoordinates.Count; i++)
        {
            Vector2 currentPoint = occupiedCoordinates[i];
            if (TryGetSegmentSqrDistance(previousPoint, currentPoint, point, out float candidateSqrDistance)
                && candidateSqrDistance < sqrDistance)
            {
                sqrDistance = candidateSqrDistance;
            }

            previousPoint = currentPoint;
        }

        return sqrDistance < float.MaxValue;
    }

    private static bool ShareOccupiedCoordinate(
        IReadOnlyList<Vector2Int> leftCoordinates,
        IReadOnlyList<Vector2Int> rightCoordinates)
    {
        if (leftCoordinates == null || rightCoordinates == null)
        {
            return false;
        }

        for (int leftIndex = 0; leftIndex < leftCoordinates.Count; leftIndex++)
        {
            for (int rightIndex = 0; rightIndex < rightCoordinates.Count; rightIndex++)
            {
                if (leftCoordinates[leftIndex] == rightCoordinates[rightIndex])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEndpointNearPath(
        Vector2 endpoint,
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        IReadOnlyList<Vector2> pathPoints,
        float maxConnectionSqrDistance)
    {
        return TryFindNearestPointOnConnectionPath(occupiedCoordinates, pathPoints, endpoint, out float sqrDistance)
               && sqrDistance <= maxConnectionSqrDistance;
    }

    private static bool TryFindNearestPointOnPath(
        IReadOnlyList<Vector2> pathPoints,
        Vector2 point,
        out float sqrDistance)
    {
        sqrDistance = float.MaxValue;
        if (pathPoints == null || pathPoints.Count < 2)
        {
            return false;
        }

        for (int i = 1; i < pathPoints.Count; i++)
        {
            if (!TryGetSegmentSqrDistance(pathPoints[i - 1], pathPoints[i], point, out float candidateSqrDistance)
                || candidateSqrDistance >= sqrDistance)
            {
                continue;
            }

            sqrDistance = candidateSqrDistance;
        }

        return sqrDistance < float.MaxValue;
    }

    private static bool TryGetSegmentSqrDistance(Vector2 start, Vector2 end, Vector2 point, out float sqrDistance)
    {
        Vector2 segment = end - start;
        float segmentSqrMagnitude = segment.sqrMagnitude;
        if (segmentSqrMagnitude <= 0.0001f)
        {
            sqrDistance = float.MaxValue;
            return false;
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentSqrMagnitude);
        Vector2 closest = start + segment * t;
        sqrDistance = (closest - point).sqrMagnitude;
        return true;
    }
}
