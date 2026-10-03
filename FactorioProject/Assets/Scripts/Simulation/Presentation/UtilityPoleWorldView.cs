using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

namespace ProjectF.Power
{
    [DefaultExecutionOrder(1000)]
    public sealed class UtilityPoleWorldView : MonoBehaviour
    {
        private UtilityPoleWorld world;
        private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
        private readonly CameraRenderCulling culling = new CameraRenderCulling();
        private readonly List<UtilityPoleRuntime> visible = new List<UtilityPoleRuntime>();
        private readonly List<UtilityPoleRuntime> collisionCandidates = new List<UtilityPoleRuntime>();
        private readonly Dictionary<UtilityPoleRuntime, SphereCollider> colliders = new Dictionary<UtilityPoleRuntime, SphereCollider>();
        private readonly Stack<SphereCollider> pool = new Stack<SphereCollider>();
        private readonly HashSet<UtilityPoleRuntime> nearby = new HashSet<UtilityPoleRuntime>();
        private readonly List<UtilityPoleRuntime> stale = new List<UtilityPoleRuntime>();
        public int VisibleCount { get; private set; }
        internal static UtilityPoleWorldView Create(UtilityPoleWorld owner, Transform parent)
        {
            var host = new GameObject(nameof(UtilityPoleWorldView)); host.transform.SetParent(parent, false);
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); host.transform.localScale = Vector3.one;
            var view = host.AddComponent<UtilityPoleWorldView>(); view.world = owner; return view;
        }
        internal void Unbind(UtilityPoleRuntime pole)
        { if (colliders.Remove(pole, out var collider) && collider != null) { collider.enabled = false; pool.Push(collider); } }
        internal void Release() { world = null; gameObject.SetActive(false); if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject); }
        private void OnDisable() { batches.SuspendRendering(); foreach (var collider in colliders.Values) if (collider != null) collider.enabled = false; }
        private void OnDestroy() { batches.Dispose(); }
        private void LateUpdate()
        {
            if (world == null) return;
            if (MapObjectTickManager.WaitingForWorldLoad || world.Terrain.IsBenchmarkPlacementInProgress) { batches.SuspendRendering(); VisibleCount = 0; return; }
            var camera = Camera.main; culling.Update(camera); world.BuildCandidates(culling, visible);
            batches.ClearActiveMatrices(); VisibleCount = 0;
            for (int i = 0; i < visible.Count; i++)
            {
                var pole = visible[i];
                if (pole.IsRuntimeActive && !pole.PlacementPresentationSuppressed && culling.Intersects(pole.CullBounds))
                { pole.Template.Append(pole, batches); VisibleCount++; }
            }
            nearby.Clear(); var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player != null) world.BuildNearby(player.transform.position, collisionCandidates); else collisionCandidates.Clear();
            for (int i = 0; i < collisionCandidates.Count; i++)
            {
                var pole = collisionCandidates[i];
                if (!pole.IsRuntimeActive || (pole.WorldPosition - player.transform.position).sqrMagnitude >= 100f) continue;
                nearby.Add(pole);
                if (!colliders.TryGetValue(pole, out var collider))
                {
                    collider = pool.Count > 0 ? pool.Pop() : gameObject.AddComponent<SphereCollider>();
                    var template = pole.Template; gameObject.layer = pole.Prototype.gameObject.layer;
                    collider.center = transform.worldToLocalMatrix.MultiplyPoint3x4(pole.RootMatrix.MultiplyPoint3x4(template.ColliderCenter));
                    collider.radius = template.ColliderRadius * Mathf.Max(Mathf.Abs(template.Scale.x), Mathf.Abs(template.Scale.y), Mathf.Abs(template.Scale.z));
                    collider.sharedMaterial = template.ColliderMaterial; collider.isTrigger = template.ColliderTrigger;
                    collider.includeLayers = template.ColliderIncludeLayers; collider.excludeLayers = template.ColliderExcludeLayers;
                    collider.layerOverridePriority = template.ColliderLayerPriority; collider.providesContacts = template.ColliderProvidesContacts;
                    colliders.Add(pole, collider);
                }
                collider.enabled = pole.Template.ColliderEnabled;
            }
            stale.Clear(); foreach (var pair in colliders) if (!nearby.Contains(pair.Key)) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) Unbind(stale[i]); batches.RenderBatches(camera);
        }
    }
}
