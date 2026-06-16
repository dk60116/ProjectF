using UnityEngine;

public class Train : Vehicle
{
    private Rigidbody cachedTrainRigidbody;
    private Railload currentRail;
    private float currentRailDistance;
    private Vector2 currentRailTangent;

    public override void PrepareForPool()
    {
        currentRail = null;
        currentRailDistance = 0f;
        currentRailTangent = Vector2.zero;
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        currentRail = null;
        currentRailDistance = 0f;
        currentRailTangent = Vector2.zero;
        base.OnPlacementRuntimeCleared();
    }

    public virtual void ApplyPlacedRailSample(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        ApplyRailPose(rail, distanceAlongPath, railPoint, facingTangent);
    }

    private bool ApplyRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Quaternion rotation = Quaternion.LookRotation(
            new Vector3(facingTangent.x, 0f, facingTangent.y),
            Vector3.up);

        return ApplyRailPoseToRail(
            rail,
            distanceAlongPath,
            railPoint,
            facingTangent,
            rotation);
    }

    protected bool ApplyRailPoseToRail(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent,
        Quaternion rotation)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Vector3 position = transform.position;
        position.x = railPoint.x;
        position.z = railPoint.y;
        if (cachedTrainRigidbody == null)
        {
            cachedTrainRigidbody = GetComponent<Rigidbody>();
        }

        if (cachedTrainRigidbody != null)
        {
            cachedTrainRigidbody.position = position;
            cachedTrainRigidbody.rotation = rotation;
            cachedTrainRigidbody.velocity = Vector3.zero;
            cachedTrainRigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(position, rotation);
        SetCurrentRailSample(rail, distanceAlongPath, facingTangent);
        RefreshRuntimeCoordinate(position);
        return true;
    }

    public bool TryGetCurrentRailSample(
        Vector2 currentPoint,
        float maxSqrDistance,
        out Railload rail,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent,
        out float sqrDistance)
    {
        rail = null;
        distanceAlongPath = 0f;
        pathPoint = currentPoint;
        tangent = Vector2.zero;
        sqrDistance = float.MaxValue;
        if (!TryGetCurrentRailPose(out rail, out distanceAlongPath, out pathPoint, out tangent))
        {
            return false;
        }

        sqrDistance = (currentPoint - pathPoint).sqrMagnitude;
        return sqrDistance <= maxSqrDistance;
    }

    public bool TryGetCurrentRailPose(
        out Railload rail,
        out float distanceAlongPath,
        out Vector2 pathPoint,
        out Vector2 tangent)
    {
        rail = null;
        distanceAlongPath = 0f;
        pathPoint = Vector2.zero;
        tangent = Vector2.zero;
        if (currentRail == null
            || !currentRail.TrySampleRenderedPath(currentRailDistance, out pathPoint, out tangent))
        {
            return false;
        }

        if (currentRailTangent.sqrMagnitude > 0.0001f
            && tangent.sqrMagnitude > 0.0001f
            && Vector2.Dot(tangent, currentRailTangent.normalized) < 0f)
        {
            tangent = -tangent;
        }

        rail = currentRail;
        distanceAlongPath = currentRailDistance;
        return true;
    }

    protected void SetCurrentRailSample(Railload rail, float distanceAlongPath, Vector2 tangent)
    {
        currentRail = rail;
        currentRailDistance = Mathf.Max(0f, distanceAlongPath);
        currentRailTangent = tangent;
    }

    protected void RefreshRuntimeCoordinate(Vector3 worldPosition)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
        var occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates != null
            && occupiedCoordinates.Count == 1
            && occupiedCoordinates[0] == coordinate)
        {
            return;
        }

        ConfigurePlacementRuntime(
            coordinate,
            RuntimeQuarterTurns,
            new[] { coordinate },
            RuntimePlacementSequence);
    }
}
