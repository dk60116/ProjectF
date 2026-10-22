using UnityEngine;

namespace ProjectF.Railway
{
    public readonly struct TrainBodyRailPose
    {
        internal readonly Vector2 Position;
        internal readonly Vector2 Forward;

        private TrainBodyRailPose(Vector2 position, Vector2 forward)
        { Position = position; Forward = forward; }

        internal static bool TryGetSupportOffsets(Train train, out float rear, out float front)
        {
            rear = front = 0f;
            int length = train.Status.mapSizeY;
            if (length <= 1) return false;
            float scale = train.transform.lossyScale.z;
            float center = train.ResolveFootprintLocalCenter().z;
            rear = (center - (length - 1) * 0.5f) * scale;
            front = (center + (length - 1) * 0.5f) * scale;
            return front - rear > 0.0001f;
        }

        internal static bool TrySolve(
            Vector2 rearPoint, Vector2 frontPoint, float rearOffset, float frontOffset,
            Vector2 physicalForward, out TrainBodyRailPose pose)
        {
            pose = default;
            Vector2 chord = frontPoint - rearPoint;
            if (chord.sqrMagnitude <= 0.000001f || Vector2.Dot(chord, physicalForward) <= 0f) return false;
            Vector2 forward = chord.normalized;
            // Center the rigid body between its two supports without stretching it.
            Vector2 root = (rearPoint + frontPoint) * 0.5f - forward * ((rearOffset + frontOffset) * 0.5f);
            pose = new TrainBodyRailPose(root, forward);
            return true;
        }

        internal static bool TrySampleOnRail(
            Train train, IRailTarget rail, float distance, Vector2 anchor, Vector2 physicalForward,
            out TrainBodyRailPose pose)
        {
            pose = default;
            if (!TryGetSupportOffsets(train, out float rear, out float front)
                || rail == null || !rail.TryGetRenderedPathLength(out float length)
                || !rail.TrySampleRenderedPath(distance, out Vector2 sampledAnchor, out Vector2 tangent)) return false;
            float sign = Vector2.Dot(tangent, physicalForward) < 0f ? -1f : 1f;
            if (!TrySampleExtended(rail, distance + rear * sign, length, out Vector2 rearPoint)
                || !TrySampleExtended(rail, distance + front * sign, length, out Vector2 frontPoint)) return false;
            Vector2 correction = anchor - sampledAnchor;
            return TrySolve(rearPoint + correction, frontPoint + correction, rear, front, physicalForward, out pose);
        }

        private static bool TrySampleExtended(IRailTarget rail, float distance, float length, out Vector2 point)
        {
            float clamped = Mathf.Clamp(distance, 0f, length);
            if (!rail.TrySampleRenderedPath(clamped, out point, out Vector2 tangent)) return false;
            point += tangent.normalized * (distance - clamped);
            return true;
        }
    }
}
