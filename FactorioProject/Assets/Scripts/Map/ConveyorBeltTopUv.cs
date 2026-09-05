using UnityEngine;

namespace ProjectF.Conveyors
{
    internal static class ConveyorBeltTopUv
    {
        private const float PhaseEpsilon = 0.0001f;

        internal static Vector2 GetWorldAlignedMapping(
            Vector3 worldCenter,
            Vector3 worldLengthVector,
            Vector3 flowDirection,
            float repeatsPerWorldUnit)
        {
            // Flat tops and seam overlays share a world-space phase reference.
            float lengthScale = Mathf.Max(
                Mathf.Abs(Vector3.Dot(worldLengthVector, flowDirection)) * repeatsPerWorldUnit,
                PhaseEpsilon);
            float unwrappedOffset = Vector3.Dot(worldCenter, flowDirection) * repeatsPerWorldUnit
                                    + repeatsPerWorldUnit * 0.5f
                                    - lengthScale * 0.5f;
            return new Vector2(lengthScale, WrapPhase(unwrappedOffset));
        }

        internal static float GetSurfaceRepeatScale(float surfaceLength, float projectedLength, float repeatsPerWorldUnit)
        {
            if (surfaceLength <= PhaseEpsilon)
            {
                return repeatsPerWorldUnit;
            }

            // Add whole repeats for the extra ramp distance. Both ends then keep
            // the flat belt phase, and surface density is never lower than on 1F.
            float extraRepeats = Mathf.Ceil(Mathf.Max(0f, surfaceLength - projectedLength)
                                           * repeatsPerWorldUnit - PhaseEpsilon);
            return (projectedLength * repeatsPerWorldUnit + extraRepeats) / surfaceLength;
        }

        internal static Vector2 GetSurfaceAlignedMapping(
            Vector3 pathStart,
            Vector3 flowDirection,
            float surfaceOffset,
            float segmentLength,
            float surfaceRepeatScale,
            float repeatsPerWorldUnit)
        {
            float phase = (Vector3.Dot(pathStart, flowDirection) + 0.5f) * repeatsPerWorldUnit
                          + surfaceOffset * surfaceRepeatScale;
            return new Vector2(segmentLength * surfaceRepeatScale, WrapPhase(phase));
        }

        internal static float GetSignedGapLength(Vector3 from, Vector3 to, Vector3 flowDirection)
        {
            Vector3 gap = to - from;
            return Vector3.Dot(gap, flowDirection) >= 0f ? gap.magnitude : -gap.magnitude;
        }

        private static float WrapPhase(float phase)
        {
            float offset = Mathf.Repeat(phase, 1f);
            if (offset <= PhaseEpsilon || offset >= 1f - PhaseEpsilon)
            {
                offset = 0f;
            }

            return offset;
        }
    }
}
