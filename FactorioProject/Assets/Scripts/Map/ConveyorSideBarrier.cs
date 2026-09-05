using UnityEngine;

namespace ProjectF.Conveyors
{
    // Side clearance uses the capsule radius, while the longitudinal ends are
    // clipped to the raised span so the feet can enter even a narrow low landing.
    internal static class ConveyorSideBarrier
    {
        private const float Epsilon = 0.00001f;

        public static bool Sweep(Vector2 start, Vector2 direction, float maxDistance,
            Vector2 center, Vector2 axis, float halfLength, float radius,
            out float distance, out Vector2 normal)
        {
            distance = float.PositiveInfinity;
            normal = Vector2.zero;
            Vector2 side = new Vector2(-axis.y, axis.x);
            Vector2 relative = start - center;
            float along = Vector2.Dot(relative, axis);
            float lateral = Vector2.Dot(relative, side);
            if (Mathf.Abs(along) < halfLength && Mathf.Abs(lateral) < radius - Epsilon)
            {
                // Installing/loading over a player must still allow walking back out.
                if (Mathf.Abs(lateral) > Epsilon)
                {
                    normal = lateral > 0f ? side : -side;
                    if (Vector2.Dot(direction, normal) >= -Epsilon)
                        return false;
                }
                else
                {
                    // Exactly on the rail there is no preferred escape side.
                    return false;
                }
                distance = 0f;
                return true;
            }

            float enter = float.NegativeInfinity;
            float exit = maxDistance;
            if (!ClipAxis(along, Vector2.Dot(direction, axis), halfLength, axis,
                    ref enter, ref exit, ref normal)
                || !ClipAxis(lateral, Vector2.Dot(direction, side), radius, side,
                    ref enter, ref exit, ref normal)
                || enter < -Epsilon || enter > maxDistance || exit <= enter + Epsilon)
                return false;

            distance = Mathf.Max(0f, enter);
            return true;
        }

        private static bool ClipAxis(float position, float speed, float extent, Vector2 axis,
            ref float enter, ref float exit, ref Vector2 normal)
        {
            if (Mathf.Abs(speed) <= Epsilon)
                return Mathf.Abs(position) < extent - Epsilon;

            float first = (-extent - position) / speed;
            float last = (extent - position) / speed;
            if (first > last)
            {
                float swap = first;
                first = last;
                last = swap;
            }
            if (first > enter)
            {
                enter = first;
                normal = speed > 0f ? -axis : axis;
            }
            exit = Mathf.Min(exit, last);
            return enter <= exit;
        }
    }
}
