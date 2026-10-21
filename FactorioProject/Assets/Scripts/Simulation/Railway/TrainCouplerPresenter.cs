using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace ProjectF.Railway
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1100)]
    public sealed class TrainCouplerPresenter : MonoBehaviour
    {
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("TrainCouplerPresenter.UpdateLinks");
        private readonly Dictionary<TrainInstance, Car> cars = new Dictionary<TrainInstance, Car>();
        private readonly List<Link> links = new List<Link>();
        private readonly Plane[] cameraPlanes = new Plane[6];

        internal static TrainCouplerPresenter Create()
        {
            var host = new GameObject(nameof(TrainCouplerPresenter));
            host.hideFlags = HideFlags.DontSave;
            return host.AddComponent<TrainCouplerPresenter>();
        }

        internal void Register(TrainInstance instance, TrainCouplerRig rig)
        {
            Unregister(instance);
            cars.Add(instance, new Car(instance, rig));
            for (int i = 0; i < instance.ConnectionCount; i++)
                RefreshConnection(instance, instance.GetConnection(i).Target);
        }

        internal void Unregister(TrainInstance instance)
        {
            if (instance == null || !cars.TryGetValue(instance, out Car car)) return;
            RemoveLink(car.Front);
            RemoveLink(car.Rear);
            cars.Remove(instance);
        }

        internal void RefreshConnection(TrainInstance first, TrainInstance second)
        {
            cars.TryGetValue(first, out Car a);
            cars.TryGetValue(second, out Car b);
            RemoveLink(a?.FindLink(second) ?? b?.FindLink(first));
            TryAddConnection(a, b);
            // Placement can exchange a middle car's two ends in separate calls.
            // Retry only incident edges once the temporarily occupied end is free.
            RestoreConnections(a);
            RestoreConnections(b);
        }

        private void RestoreConnections(Car car)
        {
            if (car == null) return;
            for (int i = 0; i < car.Instance.ConnectionCount; i++)
                if (cars.TryGetValue(car.Instance.GetConnection(i).Target, out Car other))
                    TryAddConnection(car, other);
        }

        private void TryAddConnection(Car a, Car b)
        {
            if (a == null || b == null) return;
            if (a.FindLink(b.Instance) != null) return;
            int ai = a.Instance.FindConnection(b.Instance), bi = b.Instance.FindConnection(a.Instance);
            if (ai < 0 || bi < 0) return;
            bool aFront = a.Instance.GetConnection(ai).AtFront, bFront = b.Instance.GetConnection(bi).AtFront;
            if (a.Rig.GetEnd(aFront) == null && b.Rig.GetEnd(bFront) == null) return;
            // One visual pair per graph edge, independent of travel/consist order.
            if (a.GetLink(aFront) != null || b.GetLink(bFront) != null) return;
            var link = new Link(a, aFront, b, bFront, links.Count);
            a.SetLink(aFront, link);
            b.SetLink(bFront, link);
            links.Add(link);
        }

        private void RemoveLink(Link link)
        {
            if (link == null) return;
            link.A.SetLink(link.AFront, null);
            link.B.SetLink(link.BFront, null);
            link.A.Rig.GetEnd(link.AFront)?.Reset();
            link.B.Rig.GetEnd(link.BFront)?.Reset();
            int last = links.Count - 1;
            Link moved = links[last];
            links[link.Index] = moved;
            moved.Index = link.Index;
            links.RemoveAt(last);
        }

        private void LateUpdate()
        {
            using var sample = MapObjectTickProfiler.SampleLateUpdateCaller<TrainCouplerPresenter>();
            if (links.Count == 0 || MapObjectTickManager.WaitingForWorldLoad) return;
            Camera camera = Camera.main;
            bool cull = camera != null && camera.isActiveAndEnabled;
            if (cull) GeometryUtility.CalculateFrustumPlanes(camera, cameraPlanes);
            using (UpdateMarker.Auto())
                for (int i = 0; i < links.Count; i++) links[i].Update(cull ? cameraPlanes : null);
        }

        internal void Release()
        {
            Clear();
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        private void Clear()
        {
            while (links.Count > 0) RemoveLink(links[links.Count - 1]);
            cars.Clear();
        }

        private void OnDestroy() => Clear();

        private sealed class Car
        {
            internal readonly TrainInstance Instance;
            internal readonly TrainCouplerRig Rig;
            internal Link Front;
            internal Link Rear;
            internal Car(TrainInstance instance, TrainCouplerRig rig) { Instance = instance; Rig = rig; }
            internal Link GetLink(bool front) => front ? Front : Rear;
            internal void SetLink(bool front, Link link) { if (front) Front = link; else Rear = link; }
            internal Link FindLink(TrainInstance other) => Front != null && Front.Other(this).Instance == other
                ? Front : Rear != null && Rear.Other(this).Instance == other ? Rear : null;
        }

        private sealed class Link
        {
            internal readonly Car A;
            internal readonly Car B;
            internal readonly bool AFront;
            internal readonly bool BFront;
            internal int Index;
            private Matrix4x4 lastA;
            private Matrix4x4 lastB;
            private bool hasPose;

            internal Link(Car a, bool aFront, Car b, bool bFront, int index)
            { A = a; AFront = aFront; B = b; BFront = bFront; Index = index; }

            internal Car Other(Car car) => car == A ? B : A;

            internal void Update(Plane[] planes)
            {
                Transform ar = A.Rig.Root, br = B.Rig.Root;
                if (ar == null || br == null) return;
                Matrix4x4 am = ar.localToWorldMatrix, bm = br.localToWorldMatrix;
                if (hasPose && am.Equals(lastA) && bm.Equals(lastB)) return;
                Vector3 ap = am.MultiplyPoint3x4(A.Rig.GetMount(AFront));
                Vector3 bp = bm.MultiplyPoint3x4(B.Rig.GetMount(BFront));
                if (planes != null)
                {
                    var bounds = new Bounds(ap, Vector3.zero);
                    bounds.Encapsulate(bp);
                    float scale = Mathf.Max(ar.lossyScale.magnitude, br.lossyScale.magnitude);
                    bounds.Expand(2f * scale * Mathf.Max(A.Rig.BoundsPadding, B.Rig.BoundsPadding));
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds)) return;
                }

                // Midpoint aiming is identical to aiming at the opposite fixed mount.
                // Never aim at the other rotating mesh's tip: that creates feedback.
                A.Rig.GetEnd(AFront)?.Apply(ar.rotation, ap, bp, A.Rig.MaxYaw);
                B.Rig.GetEnd(BFront)?.Apply(br.rotation, bp, ap, B.Rig.MaxYaw);
                lastA = am;
                lastB = bm;
                hasPose = true;
            }
        }
    }
}
