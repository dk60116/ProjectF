using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

namespace ProjectF.MapObjects
{
[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class WorkableWorldView : MonoBehaviour
{
    private WorkableWorld world;
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly CameraRenderCulling culling = new CameraRenderCulling();
    private readonly List<WorkableInstance> candidates = new List<WorkableInstance>();
    private readonly List<WorkableInstance> collisionCandidates = new List<WorkableInstance>();
    private readonly Dictionary<WorkableInstance, BoxCollider> colliders = new Dictionary<WorkableInstance, BoxCollider>();
    private readonly HashSet<WorkableInstance> nearby = new HashSet<WorkableInstance>();
    private readonly List<WorkableInstance> stale = new List<WorkableInstance>();
    private readonly Stack<BoxCollider> colliderPool = new Stack<BoxCollider>();
    public int VisibleCount { get; private set; }
    internal static WorkableWorldView Create(WorkableWorld owner, Transform parent)
    {
        var host = new GameObject(nameof(WorkableWorldView));
        host.transform.SetParent(parent, false); host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var view = host.AddComponent<WorkableWorldView>(); view.world = owner; return view;
    }
    internal void Unbind(WorkableInstance miner)
    {
        ReleaseCollider(miner);
    }
    private void ReleaseCollider(WorkableInstance miner)
    {
        if (!colliders.Remove(miner, out var collider) || collider == null) return;
        collider.enabled = false; colliderPool.Push(collider);
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
        using var sample = MapObjectTickProfiler.SampleLateUpdateCaller<WorkableWorldView>();
        if (world == null) return;
        if (MapObjectTickManager.WaitingForWorldLoad || world.Terrain.IsBenchmarkPlacementInProgress)
        { batches.SuspendRendering(); VisibleCount = 0; return; }
        Camera camera = Camera.main; culling.Update(camera);
        world.BuildCandidates(culling, candidates);
        batches.ClearActiveMatrices(); VisibleCount = 0; nearby.Clear();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var miner = candidates[i];
            if (!miner.PlacementPresentationSuppressed && culling.Intersects(miner.CullBounds))
            {
                miner.Template.Append(miner, batches); VisibleCount++;
            }
        }
        if (player != null) world.BuildNearby(player.transform.position, collisionCandidates); else collisionCandidates.Clear();
        for (int i = 0; i < collisionCandidates.Count; i++)
        {
            var miner = collisionCandidates[i];
            if (!miner.PlacementPresentationSuppressed && (miner.WorldPosition - player.transform.position).sqrMagnitude < 100f)
            {
                nearby.Add(miner);
                if (!colliders.ContainsKey(miner))
                {
                    gameObject.layer = miner.Prototype.gameObject.layer;
                    var collider = colliderPool.Count > 0 ? colliderPool.Pop() : gameObject.AddComponent<BoxCollider>();
                    Bounds bounds = VirtualRenderBatchCollection.CalculateWorldBounds(miner.Template.ColliderBounds, transform.worldToLocalMatrix * miner.RootMatrix);
                    collider.center = bounds.center; collider.size = bounds.size;
                    collider.sharedMaterial = miner.Template.ColliderMaterial; collider.isTrigger = miner.Template.ColliderTrigger;
                    collider.includeLayers = (int)miner.Template.ColliderIncludeLayers; collider.excludeLayers = (int)miner.Template.ColliderExcludeLayers;
                    collider.enabled = true;
                    colliders.Add(miner, collider);
                }
                else colliders[miner].enabled = true;
            }
        }
        stale.Clear(); foreach (var pair in colliders) if (!nearby.Contains(pair.Key)) stale.Add(pair.Key);
        for (int i = 0; i < stale.Count; i++) ReleaseCollider(stale[i]);
        batches.RenderBatches(camera);
    }

}
}
