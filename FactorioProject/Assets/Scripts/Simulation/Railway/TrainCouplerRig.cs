using UnityEngine;

namespace ProjectF.Railway
{
    // Captured once per pooled view, before any visual rotation is applied.
    internal sealed class TrainCouplerRig
    {
        internal readonly Transform Root;
        internal readonly End Front;
        internal readonly End Rear;
        internal readonly float MaxYaw;
        internal readonly float BoundsPadding;
        private readonly Vector3 frontFallback;
        private readonly Vector3 rearFallback;

        internal TrainCouplerRig(Train train, Transform[] couplers, float maxYaw)
        {
            Root = train.transform;
            MaxYaw = maxYaw;
            float frontReach = train.ResolveConnectionReach(true);
            float rearReach = train.ResolveConnectionReach(false);
            frontFallback = Vector3.forward * frontReach;
            rearFallback = Vector3.back * rearReach;
            BoundsPadding = 1f + Mathf.Max(frontReach, rearReach);
            if (couplers == null) return;

            for (int i = 0; i < couplers.Length; i++)
            {
                Transform pivot = couplers[i];
                if (pivot == null || pivot == Root || !pivot.IsChildOf(Root)) continue;
                Quaternion neutral = Quaternion.Inverse(Root.rotation) * pivot.rotation;
                // Use the authored outward axis, not array order or a nested local Z.
                bool front = (neutral * Vector3.forward).z >= 0f;
                if (front ? Front != null : Rear != null) continue;
                var end = new End(pivot, Root.InverseTransformPoint(pivot.position), neutral);
                if (front) Front = end; else Rear = end;
            }
        }

        internal End GetEnd(bool front) => front ? Front : Rear;
        internal Vector3 GetMount(bool front) => GetEnd(front)?.RootMount ??
            (front ? frontFallback : rearFallback);

        internal sealed class End
        {
            internal readonly Transform Pivot;
            internal readonly Vector3 RootMount;
            internal readonly Quaternion RootNeutralRotation;
            private readonly Quaternion originalLocalRotation;

            internal End(Transform pivot, Vector3 mount, Quaternion neutral)
            {
                Pivot = pivot;
                RootMount = mount;
                RootNeutralRotation = neutral;
                originalLocalRotation = pivot.localRotation;
            }

            internal void Apply(Quaternion rootRotation, Vector3 mount, Vector3 target, float maxYaw)
            {
                if (Pivot == null) return;
                if (!TrainCouplerPoseSolver.TrySolveYaw(rootRotation * RootNeutralRotation,
                    mount, target, maxYaw, out Quaternion rotation)) return;
                if (Mathf.Abs(Quaternion.Dot(Pivot.rotation, rotation)) < 0.9999999f)
                    Pivot.rotation = rotation;
            }

            internal void Reset()
            {
                if (Pivot != null && Mathf.Abs(Quaternion.Dot(Pivot.localRotation, originalLocalRotation)) < 0.9999999f)
                    Pivot.localRotation = originalLocalRotation;
            }
        }
    }
}
