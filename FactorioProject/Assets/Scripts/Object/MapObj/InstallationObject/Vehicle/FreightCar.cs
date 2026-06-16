using System.Collections.Generic;
using UnityEngine;

public class FreightCar : Train
{
    [SerializeField, Min(0.01f)]
    private float railRotationInterpolationSpeed = 10f;
    [SerializeField]
    private List<Transform> itemPointList;

    public override void ApplyPlacedRailSample(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent)
    {
        if (rail != null
            && rail.TrySampleRenderedPath(distanceAlongPath, out Vector2 sampledPoint, out Vector2 railTangent)
            && railTangent.sqrMagnitude > 0.0001f)
        {
            base.ApplyPlacedRailSample(
                rail,
                distanceAlongPath,
                sampledPoint,
                railTangent);
            return;
        }

        base.ApplyPlacedRailSample(rail, distanceAlongPath, railPoint, facingTangent);
    }

    public bool TryApplyRailPose(
        Railload rail,
        float distanceAlongPath,
        Vector2 railPoint,
        Vector2 facingTangent,
        float deltaTime,
        bool smoothRotation)
    {
        if (rail == null || facingTangent.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        facingTangent.Normalize();
        Vector2 visualFacingTangent = ResolveVisualFacingTangent(facingTangent);
        Quaternion targetRotation = Quaternion.LookRotation(
            new Vector3(visualFacingTangent.x, 0f, visualFacingTangent.y),
            Vector3.up);
        Quaternion rotation = targetRotation;
        if (smoothRotation && deltaTime > 0f)
        {
            float interpolation = 1f - Mathf.Exp(
                -Mathf.Max(0.01f, railRotationInterpolationSpeed) * deltaTime);
            rotation = Quaternion.Slerp(transform.rotation, targetRotation, interpolation);
        }

        return ApplyRailPoseToRail(
            rail,
            distanceAlongPath,
            railPoint,
            facingTangent,
            rotation);
    }

    private Vector2 ResolveVisualFacingTangent(Vector2 targetFacingTangent)
    {
        if (targetFacingTangent.sqrMagnitude <= 0.0001f)
        {
            return Vector2.up;
        }

        targetFacingTangent.Normalize();
        Vector2 currentForward = new Vector2(transform.forward.x, transform.forward.z);
        if (currentForward.sqrMagnitude <= 0.0001f)
        {
            return targetFacingTangent;
        }

        currentForward.Normalize();
        return Vector2.Dot(currentForward, targetFacingTangent) < 0f
            ? -targetFacingTangent
            : targetFacingTangent;
    }
}
