using UnityEngine;

namespace ProjectF.Rendering
{
    // Immutable presentation geometry. Routing and transport clocks stay in BeltSimulationWorld.
    public struct BeltItemVisualPath
    {
        public Vector3 Start, End, Via;
        public float FirstFraction, StartAngle, DeltaAngle, Radius;
        public byte Kind; // 0: resolved pose, 1: line, 2: via, 3: arc, 4: external jump

        public static BeltItemVisualPath Line(Vector3 start, Vector3 end, bool jump = false)
            => new BeltItemVisualPath { Start = start, End = end, Kind = jump ? (byte)4 : (byte)1 };

        public static BeltItemVisualPath Through(Vector3 start, Vector3 via, Vector3 end)
        {
            float first = Vector3.Distance(start, via);
            float total = first + Vector3.Distance(via, end);
            return new BeltItemVisualPath
            {
                Start = start, Via = via, End = end, Kind = 2,
                FirstFraction = total > 0f ? first / total : 0f
            };
        }

        public static BeltItemVisualPath Arc(Vector3 center, float startAngle, float deltaAngle, float radius)
            => new BeltItemVisualPath
            { Start = center, StartAngle = startAngle, DeltaAngle = deltaAngle, Radius = radius, Kind = 3 };

        public Vector3 Evaluate(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (Kind == 3)
            {
                float angle = StartAngle + DeltaAngle * progress;
                return Start + new Vector3(Mathf.Cos(angle) * Radius, 0f, Mathf.Sin(angle) * Radius);
            }
            if (Kind == 2)
                return progress <= FirstFraction
                    ? Vector3.Lerp(Start, Via, FirstFraction > 0f ? progress / FirstFraction : 1f)
                    : Vector3.Lerp(Via, End, FirstFraction < 1f ? (progress - FirstFraction) / (1f - FirstFraction) : 1f);
            Vector3 position = Vector3.Lerp(Start, End, progress);
            if (Kind == 4) position.y += Mathf.Sin(progress * Mathf.PI) * 0.35f;
            return position;
        }
    }

    // Owned only by visible renderer caches, rather than adding geometry to every loaded belt.
    internal struct BeltItemVisualPathCache
    {
        internal bool Valid;
        internal int LaneIndex, Origin, TopologyVersion;
        internal BeltItemVisualPath Path;
        internal bool RequiresSurface, RotateOnSurface;
    }
}
