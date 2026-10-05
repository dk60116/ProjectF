using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class RobotArmWorldView : MonoBehaviour
{
    private RobotArmWorld world;
    private readonly Dictionary<Collider, RobotArmInstance> colliderOwners = new Dictionary<Collider, RobotArmInstance>();
    private readonly Dictionary<RobotArmInstance, SphereCollider> colliders = new Dictionary<RobotArmInstance, SphereCollider>();
    private readonly Stack<SphereCollider> colliderPool = new Stack<SphereCollider>();
    private readonly List<RobotArmInstance> collisionCandidates = new List<RobotArmInstance>();
    private readonly List<RobotArmInstance> staleColliders = new List<RobotArmInstance>();
    private readonly HashSet<RobotArmInstance> nearbyArms = new HashSet<RobotArmInstance>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly ProjectF.Rendering.CameraRenderCulling culling = new ProjectF.Rendering.CameraRenderCulling();
    private readonly List<RobotArmInstance> renderCandidates = new List<RobotArmInstance>();
    private Camera renderCamera;
    public int VisibleCount { get; private set; }
    public int MatrixCount { get; private set; }
    internal static RobotArmWorldView Create(RobotArmWorld owner, Transform parent)
    {
        var root = new GameObject("RobotArmWorldView");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var view = root.AddComponent<RobotArmWorldView>();
        view.world = owner;
        return view;
    }
    private void BindCollider(RobotArmInstance arm)
    {
        if (colliders.ContainsKey(arm)) return;
        var template = arm.Template;
        if (!template.HasCollider) return;
        gameObject.layer = template.ColliderLayer;
        SphereCollider collider = colliderPool.Count > 0 ? colliderPool.Pop() : gameObject.AddComponent<SphereCollider>();
        collider.center = transform.InverseTransformPoint(arm.ColliderCenter);
        Vector3 inverseScale = transform.worldToLocalMatrix.lossyScale;
        collider.radius = template.ColliderRadius * Mathf.Max(Mathf.Abs(inverseScale.x), Mathf.Abs(inverseScale.y), Mathf.Abs(inverseScale.z));
        collider.sharedMaterial = template.ColliderMaterial;
        collider.isTrigger = template.ColliderIsTrigger;
        collider.enabled = true;
        colliderOwners.Add(collider, arm);
        colliders.Add(arm, collider);
    }
    public void Unbind(RobotArmInstance arm)
    {
        if (!colliders.TryGetValue(arm, out var collider)) return;
        colliders.Remove(arm);
        if (collider != null)
        {
            colliderOwners.Remove(collider); collider.enabled = false;
            colliderPool.Push(collider);
        }
    }
    private void RefreshColliders()
    {
        nearbyArms.Clear();
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player != null) world.BuildNearby(player.transform.position, collisionCandidates);
        else collisionCandidates.Clear();
        Vector3 position = player != null ? player.transform.position : default;
        for (int i = 0; i < collisionCandidates.Count; i++)
        {
            RobotArmInstance arm = collisionCandidates[i];
            float reach = 10f + arm.Template.ColliderRadius;
            if (!arm.IsRuntimeActive || arm.PlacementPresentationSuppressed || !arm.Template.HasCollider
                || (arm.ColliderCenter - position).sqrMagnitude >= reach * reach) continue;
            nearbyArms.Add(arm);
        }
        // Return outgoing colliders first so crossing the range boundary reuses
        // them in this frame instead of allocating replacements before release.
        staleColliders.Clear();
        foreach (var pair in colliders) if (!nearbyArms.Contains(pair.Key)) staleColliders.Add(pair.Key);
        for (int i = 0; i < staleColliders.Count; i++) Unbind(staleColliders[i]);
        foreach (var arm in nearbyArms) BindCollider(arm);
    }
    private void ClearColliders()
    {
        staleColliders.Clear();
        foreach (var arm in colliders.Keys) staleColliders.Add(arm);
        for (int i = 0; i < staleColliders.Count; i++) Unbind(staleColliders[i]);
        nearbyArms.Clear(); collisionCandidates.Clear(); staleColliders.Clear();
    }
    public RobotArmInstance ResolveCollider(Collider collider)
        => collider != null && colliderOwners.TryGetValue(collider, out var arm) ? arm : null;
    public int ActiveColliderCount => colliders.Count;
    public int PooledColliderCount => colliderPool.Count;
    public void ClearPresentation() { ClearColliders(); batches.ClearActiveMatrices(); VisibleCount = MatrixCount = 0; }
    internal void Release()
    {
        world = null;
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }
    private void OnDisable()
    {
        ClearColliders();
        batches.SuspendRendering();
        VisibleCount = MatrixCount = 0;
        world?.ResetRenderCandidateMetrics();
    }
    private void OnDestroy()
    {
        world?.OnViewDestroyed(this);
        colliderOwners.Clear(); colliders.Clear(); colliderPool.Clear(); batches.Dispose();
    }
    private void LateUpdate()
    {
        using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<RobotArmWorldView>();
        if (world == null) return;
        if (MapObjectTickManager.WaitingForWorldLoad || world.Terrain.IsBenchmarkPlacementInProgress)
        {
            ClearColliders();
            batches.SuspendRendering();
            VisibleCount = MatrixCount = 0;
            world.ResetRenderCandidateMetrics();
            return;
        }
        RefreshColliders();
        if (renderCamera == null || !renderCamera.isActiveAndEnabled) renderCamera = Camera.main;
        culling.Update(renderCamera);
        world.BuildRenderCandidates(culling, renderCandidates);
        batches.ClearActiveMatrices();
        VisibleCount = MatrixCount = 0;
        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Render Build"))
        {
            for (int i = 0; i < renderCandidates.Count; i++)
            {
                RobotArmInstance arm = renderCandidates[i];
                if (arm.PlacementPresentationSuppressed
                    || !culling.IsAnyLayerVisible(arm.Template.LayerMask)
                    || !culling.Intersects(arm.CullBounds)) continue;
                VisibleCount++;
                MatrixCount += arm.Template.Append(arm, batches);
            }
        }
        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Render Submit"))
            batches.RenderBatches(renderCamera);
    }
}
