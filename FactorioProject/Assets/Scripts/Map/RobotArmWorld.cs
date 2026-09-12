using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Runtime;

// One scene object owns every installed arm, including long arms.
[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class RobotArmWorld : MonoBehaviour, IMapObjectUpdateTick, IMapObjectUpdateTickInterval, IMapObjectStagedUpdateTick, IMapObjectSimulationIdentity
{
    public static RobotArmWorld Current { get; private set; }
    internal TerrainGenerator Terrain { get; private set; }
    internal BlockStateStore StateStore { get; private set; }
    private readonly ResourceStateSlots<RobotArmRuntimeState> states = new ResourceStateSlots<RobotArmRuntimeState>();
    private readonly Dictionary<Vector2Int, RobotArmInstance> byKey = new Dictionary<Vector2Int, RobotArmInstance>();
    private readonly Dictionary<Vector2Int, List<RobotArmInstance>> observers = new Dictionary<Vector2Int, List<RobotArmInstance>>();
    private readonly List<RobotArmInstance> ordered = new List<RobotArmInstance>();
    private readonly List<RobotArmInstance> planned = new List<RobotArmInstance>();
    private readonly Dictionary<Collider, RobotArmInstance> colliderOwners = new Dictionary<Collider, RobotArmInstance>();
    private readonly Dictionary<RobotArmInstance, SphereCollider> colliders = new Dictionary<RobotArmInstance, SphereCollider>();
    private readonly Dictionary<RobotArm, RobotArmRenderTemplate> templates = new Dictionary<RobotArm, RobotArmRenderTemplate>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly ProjectF.Rendering.CameraRenderCulling culling = new ProjectF.Rendering.CameraRenderCulling();
    private bool orderDirty;
    private Camera renderCamera;
    private RobotArmInstance selectedMarkerArm;
    private bool markersDirty = true;
    private long interactionBlockCacheHits;
    private long interactionBlockCacheMisses;
    private long interactionTargetCacheHits;
    private long interactionTargetCacheMisses;
    private long interactionFreightCacheHits;
    private long interactionFreightCacheMisses;
    public int VisibleMarkerCount { get; private set; }
    public long SimulationId => long.MaxValue - 20;
    public float ManagedUpdateTickIntervalSeconds => MapObjectTickManager.FixedSimulationDeltaSeconds;
    public IReadOnlyList<RobotArmInstance> Instances => ordered;
    public int Count => byKey.Count;
    public float MaxFocusRadius { get; private set; }
    public int VisibleCount { get; private set; }
    public int MatrixCount { get; private set; }
    public static RobotArmWorld Ensure(TerrainGenerator terrain)
    {
        if (terrain == null) return null;
        if (Current != null && Current.Terrain == terrain) return Current;
        var root = new GameObject("RobotArmWorld");
        root.transform.SetParent(terrain.transform, false);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var world = root.AddComponent<RobotArmWorld>();
        world.Terrain = terrain;
        world.StateStore = terrain.GetComponent<BlockStateStore>();
        Current = world;
        return world;
    }
    internal bool IsValid(int index, uint generation) => states.Contains(index, generation);
    internal ref RobotArmRuntimeState GetState(int index, uint generation) => ref states.Get(index, generation);
    internal void RecordInteractionBlockCache(bool hit)
    {
        if (hit) interactionBlockCacheHits++;
        else interactionBlockCacheMisses++;
    }
    internal void RecordInteractionTargetCache(bool hit)
    {
        if (hit) interactionTargetCacheHits++;
        else interactionTargetCacheMisses++;
    }
    internal void RecordInteractionFreightCache(bool hit)
    {
        if (hit) interactionFreightCacheHits++;
        else interactionFreightCacheMisses++;
    }
    internal RobotArmRenderTemplate GetTemplate(RobotArm prototype)
    {
        if (!templates.TryGetValue(prototype, out var template))
        { template = new RobotArmRenderTemplate(prototype); templates.Add(prototype, template); }
        return template;
    }
    public RobotArmInstance Register(RobotArm prototype, BlockStateStore.InstallationSaveState placement)
    {
        Vector2Int key = placement.hasStorageKey ? placement.storageKey : placement.anchorCoordinate;
        if (byKey.TryGetValue(key, out var existing)) { Bind(existing); return existing; }
        var slot = states.Allocate(new RobotArmRuntimeState { heldItemId = -1, BodyRotation = GetTemplate(prototype).BodyRotation });
        var arm = new RobotArmInstance(this, slot.Index, slot.Generation, prototype, placement);
        byKey.Add(key, arm);
        MaxFocusRadius = Mathf.Max(MaxFocusRadius, prototype.FocusActivationRadius);
        ordered.Add(arm);
        orderDirty = true;
        markersDirty = true;
        arm.ApplyTransferState(placement.robotArmState);
        CreateCollider(arm);
        if (arm.TryResolveEndpoints(out var input, out var output)) { Observe(input, arm); Observe(output, arm); }
        foreach (var coordinate in placement.occupiedCoordinates) Observe(coordinate, arm);
        Bind(arm);
        UtilityPole.InvalidateRobotArmConsumers();
        return arm;
    }
    private void Observe(Vector2Int coordinate, RobotArmInstance arm)
    {
        if (!observers.TryGetValue(coordinate, out var list)) observers.Add(coordinate, list = new List<RobotArmInstance>(2));
        if (!list.Contains(arm)) list.Add(arm);
    }
    public bool TryGet(Vector2Int storageKey, out RobotArmInstance arm) => byKey.TryGetValue(storageKey, out arm);
    public void Bind(RobotArmInstance arm)
    {
        foreach (var coordinate in arm.Placement.occupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null) block.SetMapObject(arm);
    }
    public void Remove(Vector2Int storageKey)
    {
        if (!byKey.TryGetValue(storageKey, out var arm)) return;
        byKey.Remove(storageKey);
        markersDirty = true;
        ordered.Remove(arm);
        if (arm.TryResolveEndpoints(out var input, out var output)) { Unobserve(input, arm); Unobserve(output, arm); }
        foreach (var coordinate in arm.RuntimeOccupiedCoordinates) Unobserve(coordinate, arm);
        ReleaseEntity(arm);
    }
    private void ReleaseEntity(RobotArmInstance arm)
    {
        arm.ReleaseRuntimeCaches();
        UtilityPole.UnregisterRobotArmConsumer(arm);
        foreach (var coordinate in arm.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null && ReferenceEquals(block.MapObject, arm))
                block.SetMapObject(null);
        if (colliders.TryGetValue(arm, out var collider))
        { collider.enabled = false; colliderOwners.Remove(collider); colliders.Remove(arm); Destroy(collider); }
        states.Release(arm.Index, arm.Generation);
    }
    public void Wake(Vector2Int coordinate)
    {
        if (!observers.TryGetValue(coordinate, out var list)) return;
        for (int i = 0; i < list.Count; i++) list[i].WakeRuntimeSleep();
    }
    private void Unobserve(Vector2Int coordinate, RobotArmInstance arm)
    {
        if (!observers.TryGetValue(coordinate, out var list)) return;
        list.Remove(arm);
        if (list.Count == 0) observers.Remove(coordinate);
    }
    public void WakeAll() { foreach (var arm in ordered) arm.WakeRuntimeSleep(); }
    public void SetSelectedMarkerArm(RobotArmInstance arm) { selectedMarkerArm = arm; }
    internal bool RefreshAreaMarkers(in AreaMarkerVisibilityContext context)
    {
        bool changed = markersDirty;
        markersDirty = false;
        VisibleMarkerCount = 0;
        foreach (var arm in ordered)
        {
            bool visible = AreaMarkerVisibilityContext.ShouldShow(5f, false, arm == selectedMarkerArm,
                context.ShowAll, context.HasPlayer, context.PlayerPosition, arm.WorldPosition);
            changed |= arm.MarkersVisible != visible;
            arm.MarkersVisible = visible;
            if (visible) VisibleMarkerCount += 2;
        }
        return changed;
    }
    internal void AppendAreaMarkers(AreaMarkerRenderer renderer)
    {
        Sprite icon = UIManager.Instance != null ? UIManager.Instance.ArrowImage : null;
        foreach (var arm in ordered)
        {
            if (!arm.MarkersVisible || !arm.TryResolveEndpoints(out var input, out var output)) continue;
            Vector3 inputPosition = new Vector3(input.x, arm.WorldPosition.y + 0.08f, input.y);
            Vector3 outputPosition = new Vector3(output.x, arm.WorldPosition.y + 0.08f, output.y);
            Vector3 inputDirection = arm.WorldPosition - inputPosition, outputDirection = outputPosition - arm.WorldPosition;
            var inputRequest = new AreaMarkerSpawnRequest(inputPosition, icon, -Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg);
            var outputRequest = new AreaMarkerSpawnRequest(outputPosition, icon, -Mathf.Atan2(outputDirection.x, outputDirection.z) * Mathf.Rad2Deg);
            renderer.Append(inputRequest, Matrix4x4.Translate(inputPosition), 0, false, false);
            renderer.Append(outputRequest, Matrix4x4.Translate(outputPosition), 0, false, false);
        }
    }
    private void CreateCollider(RobotArmInstance arm)
    {
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
    internal static RobotArmInstance ResolveCollider(Collider collider) =>
        Current != null && collider != null && Current.colliderOwners.TryGetValue(collider, out var arm) ? arm : null;
    internal void SuspendForEditing(RobotArmInstance arm)
    { arm.Persist(); Remove(arm.Placement.hasStorageKey ? arm.Placement.storageKey : arm.Placement.anchorCoordinate); }
    public void ClearRecords()
    {
        foreach (var arm in ordered) ReleaseEntity(arm);
        byKey.Clear(); ordered.Clear(); observers.Clear(); planned.Clear();
        selectedMarkerArm = null; MaxFocusRadius = 0f; markersDirty = true;
        interactionBlockCacheHits = interactionBlockCacheMisses = 0L;
        interactionTargetCacheHits = interactionTargetCacheMisses = 0L;
        interactionFreightCacheHits = interactionFreightCacheMisses = 0L;
        batches.ClearActiveMatrices();
        UtilityPole.InvalidateRobotArmConsumers();
    }
    public void ClearItems() { foreach (var arm in ordered) arm.ClearHeldItemAndTransferState(); FlushSaveStates(); }
    public void FlushSaveStates()
    {
        foreach (var arm in ordered)
        {
            arm.Placement.robotArmState = arm.CaptureTransferState();
            StateStore.UpdateInstallationState(arm.Placement);
        }
    }
    public void ManagedUpdateTick(float dt) { PlanManagedUpdateTick(dt); ApplyManagedUpdateTick(); }
    public void PlanManagedUpdateTick(float dt)
    {
        if (orderDirty) { ordered.Sort((a,b) => a.SimulationId.CompareTo(b.SimulationId)); orderDirty = false; }
        planned.Clear();
        foreach (var arm in ordered)
        {
            if (!arm.ReadyForTick) { arm.AdvanceSleepingPresentation(dt); continue; }
            planned.Add(arm);
        }
        if (planned.Count == 0) return;
        UtilityPole.PrepareRobotArmPowerTick();
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Entity Plan");
        foreach (var arm in planned)
        {
            arm.PlanManagedUpdateTick(dt);
        }
    }
    public void ApplyManagedUpdateTick()
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Entity Apply");
        foreach (var arm in planned)
        {
            if (!arm.IsRuntimeActive) continue;
            int previousItem = arm.HeldItemId;
            arm.ApplyManagedUpdateTick();
            if (previousItem != arm.HeldItemId) arm.PersistTransferState();
        }
        planned.Clear();
    }
    private void OnEnable() { MapObjectTickManager.RegisterUpdateTick(this); }
    private void OnDisable() { MapObjectTickManager.UnregisterUpdateTick(this); batches.SuspendRendering(); }
    private void OnDestroy()
    {
        if (Current == this) Current = null;
        UtilityPole.InvalidateRobotArmConsumers();
        batches.Dispose();
    }
    private void LateUpdate()
    {
        if (renderCamera == null || !renderCamera.isActiveAndEnabled) renderCamera = Camera.main;
        culling.Update(renderCamera);
        batches.ClearActiveMatrices();
        VisibleCount = MatrixCount = 0;
        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Render Build"))
        {
            foreach (var arm in ordered)
            {
                if (!culling.IsAnyLayerVisible(arm.Template.LayerMask) || !culling.Intersects(arm.CullBounds)) continue;
                VisibleCount++;
                MatrixCount += arm.Template.Append(arm, batches);
            }
        }
        using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Render Submit"))
            batches.RenderBatches(renderCamera);
    }
    public static void AppendProfilerCounters()
    {
        if (Current == null) return;
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "GameObjects", 1);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "MonoBehaviours", 1);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "Entities", Current.Count);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "Visible", Current.VisibleCount);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "Matrices", Current.MatrixCount);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "BlockHits", Current.interactionBlockCacheHits);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "BlockMisses", Current.interactionBlockCacheMisses);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "TargetHits", Current.interactionTargetCacheHits);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "TargetMisses", Current.interactionTargetCacheMisses);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "FreightHits", Current.interactionFreightCacheHits);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "FreightMisses", Current.interactionFreightCacheMisses);
        UtilityPole.AppendRobotArmPowerProfilerCounters();
    }
}
