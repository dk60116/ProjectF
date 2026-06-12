using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Train : Vehicle
{
    [SerializeField, Min(0.1f)]
    private float trainCouplingCenterDistance = 1f;
    [SerializeField, Min(0f)]
    private float trainCouplingGapDistance = 0.4f;
    [SerializeField, Min(0.05f)]
    private float trainCouplingSnapMaxDistance = 1.15f;

    private Rigidbody cachedTrainRigidbody;
    private Railload currentRail;
    private float currentRailDistance;
    private Vector2 currentRailTangent;

    public virtual bool SnapToAdjacentTrainOnRailWhenPlaced => false;
    public float TrainCouplingCenterDistance => Mathf.Max(0.1f, trainCouplingCenterDistance);
    public float TrainCouplingGapDistance => Mathf.Max(0f, trainCouplingGapDistance);
    public float TrainCouplingSnapMaxDistance => Mathf.Max(0.05f, trainCouplingSnapMaxDistance);

    public float ResolveCouplingCenterDistance(Train other)
    {
        if (other == null)
        {
            return TrainCouplingCenterDistance;
        }

        float bodyDistance = ResolveForwardHalfLength() + other.ResolveForwardHalfLength();
        float gapDistance = Mathf.Max(TrainCouplingGapDistance, other.TrainCouplingGapDistance);
        return Mathf.Max(
            TrainCouplingCenterDistance,
            other.TrainCouplingCenterDistance,
            bodyDistance + gapDistance);
    }

    public void ApplyCoupledRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent,
        float deltaTime,
        float rotationInterpolationSpeed,
        float signedWheelDistance)
    {
        if (facingTangent.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        facingTangent.Normalize();
        Vector3 position = transform.position;
        position.x = railPoint.x;
        position.z = railPoint.y;
        Quaternion targetRotation = Quaternion.LookRotation(
            new Vector3(facingTangent.x, 0f, facingTangent.y),
            Vector3.up);
        Quaternion rotation = targetRotation;
        if (deltaTime > 0f)
        {
            float interpolation = 1f - Mathf.Exp(
                -Mathf.Max(0.01f, rotationInterpolationSpeed) * deltaTime);
            rotation = Quaternion.Slerp(transform.rotation, targetRotation, interpolation);
        }

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
        RotateWheelsByDistance(signedWheelDistance);
        SetCurrentRailSample(rail, distanceAlongPath, facingTangent);
        RefreshRuntimeCoordinate(position);
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

        sqrDistance = (currentPoint - pathPoint).sqrMagnitude;
        if (sqrDistance > maxSqrDistance)
        {
            return false;
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

    private void RefreshRuntimeCoordinate(Vector3 worldPosition)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
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

    private float ResolveForwardHalfLength()
    {
        Vector3 forward = transform.forward;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return 0.5f;
        }

        forward.Normalize();
        float halfLength = 0f;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            if (candidate == null || candidate.isTrigger)
            {
                continue;
            }

            halfLength = Mathf.Max(halfLength, ResolveColliderForwardHalfLength(candidate, forward));
        }

        return Mathf.Max(0.05f, halfLength);
    }

    private float ResolveColliderForwardHalfLength(Collider targetCollider, Vector3 forward)
    {
        if (targetCollider is BoxCollider boxCollider)
        {
            return ResolveBoxColliderForwardHalfLength(boxCollider, forward);
        }

        Bounds bounds = targetCollider.bounds;
        Vector3 relativeCenter = bounds.center - transform.position;
        float centerProjection = Vector3.Dot(relativeCenter, forward);
        Vector3 absForward = new Vector3(
            Mathf.Abs(forward.x),
            Mathf.Abs(forward.y),
            Mathf.Abs(forward.z));
        float extentProjection = Vector3.Dot(bounds.extents, absForward);
        return Mathf.Max(
            Mathf.Abs(centerProjection - extentProjection),
            Mathf.Abs(centerProjection + extentProjection));
    }

    private float ResolveBoxColliderForwardHalfLength(BoxCollider boxCollider, Vector3 forward)
    {
        Vector3 halfSize = boxCollider.size * 0.5f;
        Vector3 center = boxCollider.center;
        float minProjection = float.MaxValue;
        float maxProjection = float.MinValue;

        for (int z = -1; z <= 1; z += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    Vector3 localCorner = center + new Vector3(
                        halfSize.x * x,
                        halfSize.y * y,
                        halfSize.z * z);
                    Vector3 worldCorner = boxCollider.transform.TransformPoint(localCorner);
                    float projection = Vector3.Dot(worldCorner - transform.position, forward);
                    minProjection = Mathf.Min(minProjection, projection);
                    maxProjection = Mathf.Max(maxProjection, projection);
                }
            }
        }

        return Mathf.Max(Mathf.Abs(minProjection), Mathf.Abs(maxProjection));
    }
}
