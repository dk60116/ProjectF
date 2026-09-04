using UnityEngine;

namespace ProjectF.Conveyors
{
    // Motion time belongs to an item segment, never to the block's wake clock.
    internal readonly struct ConveyorMotionTiming
    {
        internal readonly float StartTime;
        internal readonly float Duration;

        internal ConveyorMotionTiming(float startTime, float duration)
        {
            StartTime = startTime;
            Duration = duration;
        }

        internal static ConveyorMotionTiming FromPath(float now, float pathLength, float speed, float progress = 0f)
        {
            float duration = speed > 0.0001f && pathLength > 0.0001f ? pathLength / speed : 0f;
            return new ConveyorMotionTiming(now - duration * Mathf.Clamp01(progress), duration);
        }

        internal float Evaluate(float now)
        {
            return Duration > 0.0001f ? Mathf.Clamp01((now - StartTime) / Duration) : 1f;
        }
    }
}
