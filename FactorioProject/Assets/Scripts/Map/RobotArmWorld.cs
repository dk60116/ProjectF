using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Runtime;
using ProjectF.Simulation;
using ProjectF.Rendering;

// Runtime lifetime belongs to the world; its optional view never owns arm ticks.
public sealed class RobotArmWorld : IDisposable, IMapObjectUpdateTick, IMapObjectUpdateTickInterval, IMapObjectStagedUpdateTick, IMapObjectSimulationIdentity
{
    private const int MarkerChunkSize = 32;
    private const float MarkerVisibleRange = 5f;
    public static RobotArmWorld Current { get; private set; }
    internal TerrainGenerator Terrain { get; private set; }
    internal BlockStateStore StateStore { get; private set; }
    private readonly ResourceStateSlots<RobotArmRuntimeState> states = new ResourceStateSlots<RobotArmRuntimeState>();
    private readonly Dictionary<Vector2Int, RobotArmInstance> byKey = new Dictionary<Vector2Int, RobotArmInstance>();
    private readonly Dictionary<Vector2Int, List<RobotArmInstance>> observers = new Dictionary<Vector2Int, List<RobotArmInstance>>();
    private readonly List<RobotArmInstance> ordered = new List<RobotArmInstance>();
    private readonly Dictionary<Vector2Int, List<RobotArmInstance>> markerArmsByCell =
        new Dictionary<Vector2Int, List<RobotArmInstance>>();
    private readonly HashSet<RobotArmInstance> visibleMarkerArms = new HashSet<RobotArmInstance>();
    private readonly HashSet<RobotArmInstance> markerCandidateSet = new HashSet<RobotArmInstance>();
    private readonly List<RobotArmInstance> markerCandidates = new List<RobotArmInstance>();
    private readonly List<RobotArmInstance> planned = new List<RobotArmInstance>();
    private readonly ActiveTickSet<RobotArmInstance> activeTicks = new ActiveTickSet<RobotArmInstance>(
        Comparer<RobotArmInstance>.Create((a, b) => a.SimulationId.CompareTo(b.SimulationId)));
    internal double PresentationTime { get; private set; }
    private int lastPlannedCount;
    private long tickCandidatesVisited;
    private long wakeRequests;
    private long wakeAdmissions;
    private readonly HashSet<RobotArmInstance> wakeBatchSet = new HashSet<RobotArmInstance>();
    private readonly List<RobotArmInstance> wakeBatch = new List<RobotArmInstance>();
    private readonly Dictionary<RobotArm, RobotArmRenderTemplate> templates = new Dictionary<RobotArm, RobotArmRenderTemplate>();
    private bool orderDirty;
    private RobotArmInstance selectedMarkerArm;
    private bool markersDirty = true;
    private long interactionBlockCacheHits;
    private long interactionBlockCacheMisses;
    private long interactionTargetCacheHits;
    private long interactionTargetCacheMisses;
    private long interactionFreightCacheHits;
    private long interactionFreightCacheMisses;
    public int VisibleMarkerCount { get; private set; }
    public int MarkerVisibilityCandidateCount { get; private set; }
    public long SimulationId => long.MaxValue - 20;
    public float ManagedUpdateTickIntervalSeconds => MapObjectTickManager.FixedSimulationDeltaSeconds;
    public IReadOnlyList<RobotArmInstance> Instances => ordered;
    public int Count => byKey.Count;
    public float MaxFocusRadius { get; private set; }
    private RobotArmWorldView view;
    private bool disposed;
    public int VisibleCount => view != null ? view.VisibleCount : 0;
    public int MatrixCount => view != null ? view.MatrixCount : 0;
    public int RenderCandidateCount { get; private set; }
    public int RenderCandidateCellCount { get; private set; }
    public bool HasView => view != null;
    public static RobotArmWorld Ensure(TerrainGenerator terrain)
    {
        if (terrain == null) return null;
        if (Current != null && Current.Terrain == terrain) return Current;
        Current?.Dispose();
        var world = new RobotArmWorld { Terrain = terrain, StateStore = terrain.GetComponent<BlockStateStore>() };
        Current = world;
        MapObjectTickManager.RegisterUpdateTick(world);
        world.AttachView();
        return world;
    }

    public void AttachView()
    {
        if (disposed) throw new ObjectDisposedException(nameof(RobotArmWorld));
        if (view != null) return;
        view = RobotArmWorldView.Create(this, Terrain.transform);
        foreach (var arm in ordered) view.Bind(arm);
    }

    public void DetachView()
    {
        if (view == null) return;
        var previous = view;
        view = null;
        previous.Release();
    }

    internal void OnViewDestroyed(RobotArmWorldView previous)
    {
        if (ReferenceEquals(view, previous)) view = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        MapObjectTickManager.UnregisterUpdateTick(this);
        ClearRecords();
        DetachView();
        disposed = true;
        if (ReferenceEquals(Current, this)) Current = null;
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
        AddMarkerArm(arm);
        orderDirty = true;
        markersDirty = true;
        arm.ApplyTransferState(placement.robotArmState);
        view?.Bind(arm);
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
    public bool TryGetAtCoordinate(Vector2Int coordinate, out RobotArmInstance arm)
    {
        arm = null;
        if (!observers.TryGetValue(coordinate, out List<RobotArmInstance> candidates))
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            RobotArmInstance candidate = candidates[i];
            if (candidate == null
                || !candidate.IsRuntimeActive
                || !ContainsCoordinate(candidate.RuntimeOccupiedCoordinates, coordinate)
                || arm != null && candidate.SimulationId >= arm.SimulationId)
            {
                continue;
            }

            arm = candidate;
        }

        return arm != null;
    }

    private static bool ContainsCoordinate(IReadOnlyList<Vector2Int> coordinates, Vector2Int coordinate)
    {
        if (coordinates == null)
        {
            return false;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (coordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

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
        RemoveMarkerArm(arm);
        if (arm.TryResolveEndpoints(out var input, out var output)) { Unobserve(input, arm); Unobserve(output, arm); }
        foreach (var coordinate in arm.RuntimeOccupiedCoordinates) Unobserve(coordinate, arm);
        ReleaseEntity(arm);
    }
    private void ReleaseEntity(RobotArmInstance arm)
    {
        activeTicks.Remove(arm);
        arm.ReleaseRuntimeCaches();
        UtilityPole.UnregisterRobotArmConsumer(arm);
        foreach (var coordinate in arm.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null && ReferenceEquals(block.MapObject, arm))
                block.SetMapObject(null);
        view?.Unbind(arm);
        states.Release(arm.Index, arm.Generation);
    }
    public void Wake(Vector2Int coordinate)
    {
        if (!observers.TryGetValue(coordinate, out var list)) return;
        for (int i = 0; i < list.Count; i++) list[i].WakeRuntimeSleep();
    }
    internal void Wake(IReadOnlyList<Block> changedBlocks)
    {
        wakeBatchSet.Clear();
        wakeBatch.Clear();
        for (int blockIndex = 0; changedBlocks != null && blockIndex < changedBlocks.Count; blockIndex++)
        {
            Block block = changedBlocks[blockIndex];
            if (block == null || !observers.TryGetValue(block.Coordinate, out var list)) continue;
            for (int observerIndex = 0; observerIndex < list.Count; observerIndex++)
            {
                RobotArmInstance arm = list[observerIndex];
                if (arm != null && wakeBatchSet.Add(arm)) wakeBatch.Add(arm);
            }
        }

        for (int i = 0; i < wakeBatch.Count; i++) wakeBatch[i].WakeRuntimeSleep();
        wakeBatch.Clear();
        wakeBatchSet.Clear();
    }
    private void Unobserve(Vector2Int coordinate, RobotArmInstance arm)
    {
        if (!observers.TryGetValue(coordinate, out var list)) return;
        list.Remove(arm);
        if (list.Count == 0) observers.Remove(coordinate);
    }

    private void AddMarkerArm(RobotArmInstance arm)
    {
        Vector2Int cell = GetMarkerCell(arm.WorldPosition);
        if (!markerArmsByCell.TryGetValue(cell, out List<RobotArmInstance> arms))
        {
            arms = new List<RobotArmInstance>(4);
            markerArmsByCell.Add(cell, arms);
        }
        arms.Add(arm);
    }

    private void RemoveMarkerArm(RobotArmInstance arm)
    {
        Vector2Int cell = GetMarkerCell(arm.WorldPosition);
        if (markerArmsByCell.TryGetValue(cell, out List<RobotArmInstance> arms))
        {
            arms.Remove(arm);
            if (arms.Count == 0) markerArmsByCell.Remove(cell);
        }
        visibleMarkerArms.Remove(arm);
        markerCandidateSet.Remove(arm);
    }

    private void BuildMarkerCandidates(in AreaMarkerVisibilityContext context)
    {
        markerCandidateSet.Clear();
        markerCandidates.Clear();
        if (context.ShowAll)
        {
            for (int i = 0; i < ordered.Count; i++) AddMarkerCandidate(ordered[i]);
            return;
        }

        foreach (RobotArmInstance arm in visibleMarkerArms) AddMarkerCandidate(arm);
        AddMarkerCandidate(selectedMarkerArm);
        if (!context.HasPlayer) return;

        Vector2Int center = GetMarkerCell(context.PlayerPosition);
        int radius = Mathf.CeilToInt(MarkerVisibleRange / MarkerChunkSize);
        for (int z = center.y - radius; z <= center.y + radius; z++)
        {
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                if (!markerArmsByCell.TryGetValue(new Vector2Int(x, z), out List<RobotArmInstance> arms)) continue;
                for (int i = 0; i < arms.Count; i++) AddMarkerCandidate(arms[i]);
            }
        }
    }

    private void AddMarkerCandidate(RobotArmInstance arm)
    {
        if (arm != null && markerCandidateSet.Add(arm)) markerCandidates.Add(arm);
    }

    private static Vector2Int GetMarkerCell(Vector3 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / MarkerChunkSize),
            Mathf.FloorToInt(position.z / MarkerChunkSize));
    }
    internal void BuildRenderCandidates(CameraRenderCulling culling, List<RobotArmInstance> destination)
    {
        destination.Clear();
        RenderCandidateCellCount = 0;
        if (culling == null || !culling.TryGetVisibleCellRange(MarkerChunkSize, 2,
                out Vector2Int minimum, out Vector2Int maximum))
        {
            destination.AddRange(ordered);
            RenderCandidateCount = destination.Count;
            return;
        }
        long cellCount = CameraRenderCulling.GetCellCount(minimum, maximum);
        RenderCandidateCellCount = cellCount <= int.MaxValue ? (int)cellCount : int.MaxValue;
        if (cellCount >= (long)Mathf.Max(1, ordered.Count) * 2L)
        {
            destination.AddRange(ordered);
            RenderCandidateCount = destination.Count;
            return;
        }
        for (int y = minimum.y; y <= maximum.y; y++)
        for (int x = minimum.x; x <= maximum.x; x++)
            if (markerArmsByCell.TryGetValue(new Vector2Int(x, y), out List<RobotArmInstance> arms))
                destination.AddRange(arms);
        RenderCandidateCount = destination.Count;
    }
    internal void ResetRenderCandidateMetrics()
    {
        RenderCandidateCount = 0;
        RenderCandidateCellCount = 0;
    }

    public void WakeAll() { foreach (var arm in ordered) arm.WakeRuntimeSleep(); }
    internal void ScheduleTick(RobotArmInstance arm, bool wake = false)
    {
        if (wake) wakeRequests++;
        if (activeTicks.Add(arm) && wake) wakeAdmissions++;
    }
    internal void UnscheduleTick(RobotArmInstance arm) => activeTicks.Remove(arm);
    public void SetSelectedMarkerArm(RobotArmInstance arm)
    {
        if (ReferenceEquals(selectedMarkerArm, arm)) return;
        selectedMarkerArm = arm;
        markersDirty = true;
    }
    internal bool RefreshAreaMarkers(in AreaMarkerVisibilityContext context)
    {
        bool changed = markersDirty;
        markersDirty = false;
        BuildMarkerCandidates(context);
        for (int i = 0; i < markerCandidates.Count; i++)
        {
            RobotArmInstance arm = markerCandidates[i];
            bool visible = AreaMarkerVisibilityContext.ShouldShow(MarkerVisibleRange, false, arm == selectedMarkerArm,
                context.ShowAll, context.HasPlayer, context.PlayerPosition, arm.WorldPosition);
            changed |= arm.MarkersVisible != visible;
            arm.MarkersVisible = visible;
            if (visible) visibleMarkerArms.Add(arm);
            else visibleMarkerArms.Remove(arm);
        }
        MarkerVisibilityCandidateCount = markerCandidates.Count;
        VisibleMarkerCount = visibleMarkerArms.Count * 2;
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
    internal static RobotArmInstance ResolveCollider(Collider collider) =>
        Current?.view != null ? Current.view.ResolveCollider(collider) : null;
    internal void SuspendForEditing(RobotArmInstance arm)
    { arm.Persist(); Remove(arm.Placement.hasStorageKey ? arm.Placement.storageKey : arm.Placement.anchorCoordinate); }
    public void ClearRecords()
    {
        foreach (var arm in ordered) ReleaseEntity(arm);
        byKey.Clear(); ordered.Clear(); observers.Clear(); planned.Clear();
        activeTicks.Clear();
        PresentationTime = 0d;
        lastPlannedCount = 0;
        tickCandidatesVisited = wakeRequests = wakeAdmissions = 0L;
        markerArmsByCell.Clear(); visibleMarkerArms.Clear();
        markerCandidateSet.Clear(); markerCandidates.Clear();
        selectedMarkerArm = null; MaxFocusRadius = 0f; markersDirty = true;
        VisibleMarkerCount = MarkerVisibilityCandidateCount = 0;
        RenderCandidateCount = RenderCandidateCellCount = 0;
        interactionBlockCacheHits = interactionBlockCacheMisses = 0L;
        interactionTargetCacheHits = interactionTargetCacheMisses = 0L;
        interactionFreightCacheHits = interactionFreightCacheMisses = 0L;
        view?.ClearPresentation();
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
        PresentationTime += Math.Max(0f, dt);
        if (orderDirty) { ordered.Sort((a,b) => a.SimulationId.CompareTo(b.SimulationId)); orderDirty = false; }
        // Wake/Sleep can mutate membership during Plan or Apply, never this snapshot.
        activeTicks.CopyOrderedTo(planned);
        lastPlannedCount = planned.Count;
        tickCandidatesVisited += planned.Count;
        if (planned.Count == 0) return;
        UtilityPole.PrepareRobotArmPowerTick();
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArm), "Robot Arm Entity Plan");
        foreach (var arm in planned)
        {
            if (!arm.IsRuntimeActive) continue;
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

    public static void AppendProfilerCounters()
    {
        if (Current == null) return;
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "GameObjects", Current.HasView ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "MonoBehaviours", Current.HasView ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "Entities", Current.Count);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "Scheduled", Current.activeTicks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "LastPlanned", Current.lastPlannedCount);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "TickCandidatesVisited", Current.tickCandidatesVisited);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "CoalescedWakeRequests", Current.wakeRequests);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "WakeAdmissions", Current.wakeAdmissions);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "Visible", Current.VisibleCount);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "Matrices", Current.MatrixCount);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "RenderCandidates", Current.RenderCandidateCount);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmWorld", "RenderCandidateCells", Current.RenderCandidateCellCount);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "BlockHits", Current.interactionBlockCacheHits);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "BlockMisses", Current.interactionBlockCacheMisses);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "TargetHits", Current.interactionTargetCacheHits);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "TargetMisses", Current.interactionTargetCacheMisses);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "FreightHits", Current.interactionFreightCacheHits);
        MapObjectTickProfiler.AddRuntimeCounter("RobotArmTargetCache", "FreightMisses", Current.interactionFreightCacheMisses);
        UtilityPole.AppendRobotArmPowerProfilerCounters();
    }
}
