using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class RobotArmWorldView : MonoBehaviour
{
    private RobotArmWorld world;
    private readonly Dictionary<Collider, RobotArmInstance> colliderOwners = new Dictionary<Collider, RobotArmInstance>();
    private readonly Dictionary<RobotArmInstance, SphereCollider> colliders = new Dictionary<RobotArmInstance, SphereCollider>();
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
    public void Bind(RobotArmInstance arm)
    {
        if (colliders.ContainsKey(arm)) return;
        SphereCollider source = arm.Prototype.GetComponent<SphereCollider>();
        if (source == null || !source.enabled) return;
        gameObject.layer = arm.Prototype.gameObject.layer;
        Matrix4x4 local = transform.worldToLocalMatrix * Matrix4x4.TRS(arm.WorldPosition, arm.WorldRotation, arm.Prototype.transform.localScale);
        SphereCollider collider = gameObject.AddComponent<SphereCollider>();
        collider.center = local.MultiplyPoint3x4(source.center);
        Vector3 scale = local.lossyScale;
        collider.radius = source.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        collider.sharedMaterial = source.sharedMaterial;
        collider.isTrigger = source.isTrigger;
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
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }
    }
    public RobotArmInstance ResolveCollider(Collider collider)
        => collider != null && colliderOwners.TryGetValue(collider, out var arm) ? arm : null;
    public void ClearPresentation() { batches.ClearActiveMatrices(); VisibleCount = MatrixCount = 0; }
    internal void Release()
    {
        world = null;
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }
    private void OnDisable()
    {
        batches.SuspendRendering();
        VisibleCount = MatrixCount = 0;
        world?.ResetRenderCandidateMetrics();
    }
    private void OnDestroy()
    {
        world?.OnViewDestroyed(this);
        colliderOwners.Clear(); colliders.Clear(); batches.Dispose();
    }
    private void LateUpdate()
    {
        if (world == null) return;
        if (MapObjectTickManager.WaitingForWorldLoad)
        {
            batches.SuspendRendering();
            VisibleCount = MatrixCount = 0;
            world.ResetRenderCandidateMetrics();
            return;
        }
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
                if (!culling.IsAnyLayerVisible(arm.Template.LayerMask) || !culling.Intersects(arm.CullBounds)) continue;
                VisibleCount++;
                MatrixCount += arm.Template.Append(arm, batches);
            }
        }
        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Render Submit"))
            batches.RenderBatches(renderCamera);
    }
}
