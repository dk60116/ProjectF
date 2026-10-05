using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

namespace ProjectF.MapObjects
{
[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class ForestryWorldView : MonoBehaviour
{
    private ForestryWorld world;
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly CameraRenderCulling culling = new CameraRenderCulling();
    private readonly List<ForestryInstance> candidates = new List<ForestryInstance>();
    private readonly List<ForestryInstance> collisionCandidates = new List<ForestryInstance>();
    private readonly Dictionary<ForestryInstance, Collider> colliders = new Dictionary<ForestryInstance, Collider>();
    private readonly HashSet<ForestryInstance> nearby = new HashSet<ForestryInstance>();
    private readonly List<ForestryInstance> stale = new List<ForestryInstance>();
    private readonly Stack<BoxCollider> colliderPool = new Stack<BoxCollider>();
    private readonly Stack<SphereCollider> sphereColliderPool = new Stack<SphereCollider>();
    public int VisibleCount { get; private set; }
    internal static ForestryWorldView Create(ForestryWorld owner, Transform parent)
    {
        var host = new GameObject(nameof(ForestryWorldView));
        host.transform.SetParent(parent, false); host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var view = host.AddComponent<ForestryWorldView>(); view.world = owner; return view;
    }
    internal void Unbind(ForestryInstance facility)
    {
        ReleaseCollider(facility);
    }
    private void ReleaseCollider(ForestryInstance facility)
    {
        if (!colliders.Remove(facility, out var collider) || collider == null) return;
        collider.enabled = false;
        if (collider is SphereCollider sphere) sphereColliderPool.Push(sphere);
        else colliderPool.Push((BoxCollider)collider);
    }
    internal void Release()
    {
        world = null; gameObject.SetActive(false); batches.Dispose();
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }
    private void OnDisable()
    {
        batches.SuspendRendering();
        foreach (var pair in colliders) if (pair.Value != null) pair.Value.enabled = false;
    }
    private void OnDestroy() => batches.Dispose();
    private void LateUpdate()
    {
        using var sample = MapObjectTickProfiler.SampleLateUpdateCaller<ForestryWorldView>();
        if (world == null) return;
        if (MapObjectTickManager.WaitingForWorldLoad || world.Terrain.IsBenchmarkPlacementInProgress)
        { batches.SuspendRendering(); VisibleCount = 0; return; }
        Camera camera = Camera.main; culling.Update(camera);
        world.BuildCandidates(culling, candidates);
        batches.ClearActiveMatrices(); VisibleCount = 0; nearby.Clear();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var facility = candidates[i];
            if (!facility.PlacementPresentationSuppressed && culling.Intersects(facility.CullBounds))
            {
                facility.Template.Append(facility, batches); VisibleCount++;
            }
        }
        if (player != null) world.BuildNearby(player.transform.position, collisionCandidates); else collisionCandidates.Clear();
        for (int i = 0; i < collisionCandidates.Count; i++)
        {
            var facility = collisionCandidates[i];
            if (!facility.PlacementPresentationSuppressed && (facility.WorldPosition - player.transform.position).sqrMagnitude < 100f)
            {
                nearby.Add(facility);
                if (!colliders.ContainsKey(facility))
                {
                    gameObject.layer = facility.Prototype.gameObject.layer;
                    Collider collider;
                    var localRoot = transform.worldToLocalMatrix * facility.RootMatrix;
                    if (facility.Template.UsesSphereCollider)
                    {
                        var sphere = sphereColliderPool.Count > 0 ? sphereColliderPool.Pop() : gameObject.AddComponent<SphereCollider>();
                        sphere.center = localRoot.MultiplyPoint3x4(facility.Template.ColliderBounds.center);
                        sphere.radius = facility.Template.ColliderBounds.extents.x * Mathf.Max(
                            Mathf.Max(localRoot.GetColumn(0).magnitude, localRoot.GetColumn(1).magnitude),
                            localRoot.GetColumn(2).magnitude);
                        collider = sphere;
                    }
                    else
                    {
                        var box = colliderPool.Count > 0 ? colliderPool.Pop() : gameObject.AddComponent<BoxCollider>();
                        Bounds bounds = VirtualRenderBatchCollection.CalculateWorldBounds(facility.Template.ColliderBounds, localRoot);
                        box.center = bounds.center; box.size = bounds.size; collider = box;
                    }
                    collider.sharedMaterial = facility.Template.ColliderMaterial; collider.isTrigger = facility.Template.ColliderTrigger;
                    collider.includeLayers = (int)facility.Template.ColliderIncludeLayers; collider.excludeLayers = (int)facility.Template.ColliderExcludeLayers;
                    collider.enabled = true;
                    colliders.Add(facility, collider);
                }
                else colliders[facility].enabled = true;
            }
        }
        stale.Clear(); foreach (var pair in colliders) if (!nearby.Contains(pair.Key)) stale.Add(pair.Key);
        for (int i = 0; i < stale.Count; i++) ReleaseCollider(stale[i]);
        batches.RenderBatches(camera);
    }

}
}

