using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class ProductionWorldView : MonoBehaviour
{
    private ProductionWorld world;
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly CameraRenderCulling culling = new CameraRenderCulling();
    private readonly SpriteMeshCache spriteMeshes = new SpriteMeshCache();
    private readonly InstallationMaterialVariants materials = new InstallationMaterialVariants();
    private readonly List<ProductionFacilityInstance> candidates = new List<ProductionFacilityInstance>();
    private readonly List<ProductionFacilityInstance> collisionCandidates = new List<ProductionFacilityInstance>();
    private readonly Dictionary<ProductionFacilityInstance, BoxCollider> colliders = new Dictionary<ProductionFacilityInstance, BoxCollider>();
    private readonly HashSet<ProductionFacilityInstance> nearby = new HashSet<ProductionFacilityInstance>();
    private readonly List<ProductionFacilityInstance> stale = new List<ProductionFacilityInstance>();
    private readonly Stack<BoxCollider> colliderPool = new Stack<BoxCollider>();
    private readonly Dictionary<ProductionFacilityInstance, DefaultGauge> workGauges = new Dictionary<ProductionFacilityInstance, DefaultGauge>();
    private readonly HashSet<ProductionFacilityInstance> visibleWorkGauges = new HashSet<ProductionFacilityInstance>();
    public int VisibleCount { get; private set; }
    internal static ProductionWorldView Create(ProductionWorld owner, Transform parent)
    {
        var host = new GameObject(nameof(ProductionWorldView));
        host.transform.SetParent(parent, false); host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var view = host.AddComponent<ProductionWorldView>(); view.world = owner; return view;
    }
    internal void Unbind(ProductionFacilityInstance facility)
    {
        ReleaseWorkGauge(facility);
        ReleaseCollider(facility);
    }
    private void ReleaseCollider(ProductionFacilityInstance facility)
    {
        if (!colliders.Remove(facility, out var collider) || collider == null) return;
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
    private void OnDestroy() { batches.Dispose(); spriteMeshes.Dispose(); materials.Dispose(); }
    private void LateUpdate()
    {
        using var sample = MapObjectTickProfiler.SampleLateUpdateCaller<ProductionWorldView>();
        if (world == null) return;
        if (MapObjectTickManager.WaitingForWorldLoad || world.Terrain.IsBenchmarkPlacementInProgress)
        { ReleaseAllWorkGauges(); batches.SuspendRendering(); VisibleCount = 0; return; }
        world.RefreshRecipeAvailability();
        Camera camera = Camera.main; culling.Update(camera);
        world.BuildCandidates(culling, candidates);
        batches.ClearActiveMatrices(); VisibleCount = 0; nearby.Clear();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        AreaMarkerVisibilityContext uiContext = AreaMarkerVisibilityContext.Capture();
        visibleWorkGauges.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            var facility = candidates[i];
            if (!facility.PlacementPresentationSuppressed && culling.Intersects(facility.CullBounds))
            {
                facility.Template.Append(facility, batches, spriteMeshes, materials, camera); VisibleCount++;
                if (facility.HasActiveWork && world.ShouldShowLinkedUi(facility, uiContext)) UpdateWorkGauge(facility);
            }
        }
        ReleaseHiddenWorkGauges();
        if (player != null) world.BuildNearby(player.transform.position, collisionCandidates); else collisionCandidates.Clear();
        for (int i = 0; i < collisionCandidates.Count; i++)
        {
            var facility = collisionCandidates[i];
            if ((facility.WorldPosition - player.transform.position).sqrMagnitude < 100f)
            {
                nearby.Add(facility);
                if (!colliders.ContainsKey(facility))
                {
                    gameObject.layer = facility.Prototype.gameObject.layer;
                    var collider = colliderPool.Count > 0 ? colliderPool.Pop() : gameObject.AddComponent<BoxCollider>();
                    Bounds bounds = VirtualRenderBatchCollection.CalculateWorldBounds(facility.Template.ColliderBounds, transform.worldToLocalMatrix * facility.RootMatrix);
                    collider.center = bounds.center; collider.size = bounds.size;
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

    private void UpdateWorkGauge(ProductionFacilityInstance facility)
    {
        UIManager ui = UIManager.Instance;
        if (ui == null) return;
        if (!workGauges.TryGetValue(facility, out var gauge) || gauge == null)
        {
            gauge = ui.AcquireEnergyGauge();
            if (gauge == null) return;
            workGauges[facility] = gauge;
        }
        visibleWorkGauges.Add(facility);
        gauge.SetFillColor(facility.Template.WorkGaugeFillColor);
        ui.UpdateEnergyGauge(gauge, facility.WorkGaugeWorldPosition, facility.WorkProgress);
    }

    private void ReleaseHiddenWorkGauges()
    {
        stale.Clear();
        foreach (var pair in workGauges)
            if (!visibleWorkGauges.Contains(pair.Key)) stale.Add(pair.Key);
        for (int i = 0; i < stale.Count; i++) ReleaseWorkGauge(stale[i]);
        stale.Clear();
    }

    private void ReleaseWorkGauge(ProductionFacilityInstance facility)
    {
        if (!workGauges.Remove(facility, out var gauge) || gauge == null) return;
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
