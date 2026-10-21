using UnityEngine;

namespace ProjectF.Railway
{
    internal static class TrainCouplerPoseSolver
    {
        internal static bool TrySolveYaw(
            Quaternion neutralRotation, Vector3 mount, Vector3 target, float maxYaw,
            out Quaternion rotation)
        {
            rotation = neutralRotation;
            Vector3 direction = target - mount;
            Vector3 neutralForward = neutralRotation * Vector3.forward;
            direction.y = neutralForward.y = 0f;
            if (direction.sqrMagnitude < 0.000001f || neutralForward.sqrMagnitude < 0.000001f)
                return false;

            // Keep each physical end pointing outwards even if two mounts overlap.
            float yaw = Mathf.Clamp(Vector3.SignedAngle(neutralForward, direction, Vector3.up),
                -Mathf.Clamp(maxYaw, 0f, 89f), Mathf.Clamp(maxYaw, 0f, 89f));
            rotation = Quaternion.AngleAxis(yaw, Vector3.up) * neutralRotation;
            return true;
        }
    }
}
