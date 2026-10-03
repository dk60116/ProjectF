using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class MiningWorldView : MonoBehaviour
{
    private MiningWorld world;
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly CameraRenderCulling culling = new CameraRenderCulling();
    private readonly List<MiningMachineInstance> candidates = new List<MiningMachineInstance>();
    private readonly List<MiningMachineInstance> collisionCandidates = new List<MiningMachineInstance>();
    private readonly Dictionary<MiningMachineInstance, BoxCollider> colliders = new Dictionary<MiningMachineInstance, BoxCollider>();
    private readonly HashSet<MiningMachineInstance> nearby = new HashSet<MiningMachineInstance>();
    private readonly List<MiningMachineInstance> stale = new List<MiningMachineInstance>();
    private readonly Stack<BoxCollider> colliderPool = new Stack<BoxCollider>();
    private readonly Dictionary<MiningMachineInstance, DefaultGauge> workGauges = new Dictionary<MiningMachineInstance, DefaultGauge>();
    private readonly HashSet<MiningMachineInstance> visibleWorkGauges = new HashSet<MiningMachineInstance>();
    public int VisibleCount { get; private set; }
    internal static MiningWorldView Create(MiningWorld owner, Transform parent)
    {
        var host = new GameObject(nameof(MiningWorldView));
        host.transform.SetParent(parent, false); host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var view = host.AddComponent<MiningWorldView>(); view.world = owner; return view;
    }
    internal void Unbind(MiningMachineInstance miner)
    {
        ReleaseWorkGauge(miner);
        ReleaseCollider(miner);
    }
    private void ReleaseCollider(MiningMachineInstance miner)
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
        ReleaseAllWorkGauges();
        batches.SuspendRendering();
        foreach (var pair in colliders) if (pair.Value != null) pair.Value.enabled = false;
    }
    private void OnDestroy() => batches.Dispose();
    private void LateUpdate()
    {
        using var sample = MapObjectTickProfiler.SampleLateUpdateCaller<MiningWorldView>();
        if (world == null) return;
        if (MapObjectTickManager.WaitingForWorldLoad || world.Terrain.IsBenchmarkPlacementInProgress)
        { ReleaseAllWorkGauges(); batches.SuspendRendering(); VisibleCount = 0; return; }
        Camera camera = Camera.main; culling.Update(camera);
        world.BuildCandidates(culling, candidates);
        batches.ClearActiveMatrices(); VisibleCount = 0; nearby.Clear();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        AreaMarkerVisibilityContext uiContext = AreaMarkerVisibilityContext.Capture();
        visibleWorkGauges.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            var miner = candidates[i];
            if (!miner.PlacementPresentationSuppressed && culling.Intersects(miner.CullBounds))
            {
                miner.Template.Append(miner, batches); VisibleCount++;
                if (miner.HasActiveWork && world.ShouldShowLinkedUi(miner, uiContext)) UpdateWorkGauge(miner);
            }
        }
        ReleaseHiddenWorkGauges();
        if (player != null) world.BuildNearby(player.transform.position, collisionCandidates); else collisionCandidates.Clear();
        for (int i = 0; i < collisionCandidates.Count; i++)
        {
            var miner = collisionCandidates[i];
            if ((miner.WorldPosition - player.transform.position).sqrMagnitude < 100f)
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

    private void UpdateWorkGauge(MiningMachineInstance miner)
    {
        UIManager ui = UIManager.Instance;
        if (ui == null) return;
        if (!workGauges.TryGetValue(miner, out var gauge) || gauge == null)
        {
            gauge = ui.AcquireEnergyGauge();
            if (gauge == null) return;
            workGauges[miner] = gauge;
        }
        visibleWorkGauges.Add(miner);
        gauge.SetFillColor(miner.Template.WorkGaugeFillColor);
        ui.UpdateEnergyGauge(gauge, miner.WorkGaugeWorldPosition, miner.WorkProgress);
    }

    private void ReleaseHiddenWorkGauges()
    {
        stale.Clear();
        foreach (var pair in workGauges)
            if (!visibleWorkGauges.Contains(pair.Key)) stale.Add(pair.Key);
        for (int i = 0; i < stale.Count; i++) ReleaseWorkGauge(stale[i]);
        stale.Clear();
    }

    private void ReleaseWorkGauge(MiningMachineInstance miner)
    {
        if (!workGauges.Remove(miner, out var gauge) || gauge == null) return;
        ReleaseGauge(gauge);
    }

    private static void ReleaseGauge(DefaultGauge gauge)
    {
        if (gauge == null) return;
        if (UIManager.Instance != null) UIManager.Instance.ReleaseEnergyGauge(gauge);
        else Destroy(gauge.gameObject);
    }

    private void ReleaseAllWorkGauges()
    {
        foreach (var pair in workGauges) ReleaseGauge(pair.Value);
        workGauges.Clear();
        visibleWorkGauges.Clear();
    }
}
