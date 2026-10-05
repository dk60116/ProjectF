using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Runtime;
using ProjectF.Simulation;
using ProjectF.Rendering;

// FacilitySimulationWorld owns ticks. This world owns identity, state and spatial indices.
public sealed class MiningWorld : IDisposable
{
    internal struct State
    {
        internal MiningProcess Clock;
        internal MapObjectHandle Resource;
        internal Vector2Int ResourceCoordinate;
        internal int ResourceCursor, PendingHarvestedItems;
        internal bool HasTarget, NeedsEvaluation, FuelBypassed;
    }
    public static MiningWorld Current { get; private set; }
    public TerrainGenerator Terrain { get; }
    internal BlockStateStore Store { get; }
    private readonly ResourceStateSlots<State> states = new ResourceStateSlots<State>();
    private readonly Dictionary<Vector2Int, MiningMachineInstance> byKey = new Dictionary<Vector2Int, MiningMachineInstance>();
    private readonly Dictionary<Vector2Int, List<MiningMachineInstance>> observers = new Dictionary<Vector2Int, List<MiningMachineInstance>>();
    private readonly List<Block> beltWakeScratch = new List<Block>();
    private readonly Dictionary<Vector2Int, List<MiningMachineInstance>> cells = new Dictionary<Vector2Int, List<MiningMachineInstance>>();
    private readonly List<MiningMachineInstance> instances = new List<MiningMachineInstance>();
    private readonly Dictionary<MiningMachine, MiningRenderTemplate> templates = new Dictionary<MiningMachine, MiningRenderTemplate>();
    private MiningWorldView view;
    private MiningMachineInstance selectedMarkerMiner;
    private readonly HashSet<MiningMachineInstance> visibleMarkers = new HashSet<MiningMachineInstance>();
    private readonly HashSet<MiningMachineInstance> markerCandidates = new HashSet<MiningMachineInstance>();
    private readonly List<MiningMachineInstance> markerScratch = new List<MiningMachineInstance>();
    private bool markersDirty = true;
    public int VisibleMarkerCount { get; private set; }
    public int MarkerCount { get; private set; }
    internal int MarkerCandidateCount => markerCandidates.Count;
    public IReadOnlyList<MiningMachineInstance> Instances => instances;
    public int Count => instances.Count;
    public int FuelDrivenCount { get; private set; }
    public long ProcessedUpdates { get; internal set; }
    public int VisibleCount => view != null ? view.VisibleCount : 0;
    public static MiningWorld Ensure(TerrainGenerator terrain)
    {
        if (Current != null && Current.Terrain == terrain) return Current;
        Current?.Dispose();
        Current = new MiningWorld(terrain);
        return Current;
    }
    private MiningWorld(TerrainGenerator terrain)
    {
        Terrain = terrain; Store = terrain.GetComponent<BlockStateStore>();
        view = MiningWorldView.Create(this, terrain.transform);
    }
    internal ref State GetState(int index, uint generation) => ref states.Get(index, generation);
    internal bool Contains(int index, uint generation) => states.Contains(index, generation);
    public static bool Supports(MiningMachine prototype)
    {
        if (prototype == null) return false;
        ItemDefinition definition = prototype.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(prototype.ResolveItemId());
        return definition != null && definition.UseEnergyRequirementCount == 1
            && definition.TryGetUseEnergyRequirement(0, out var energy)
            && (energy.energyType == ItemDefinition.EnergyType.Electricity || energy.energyType == ItemDefinition.EnergyType.Burn) && energy.useEnergyAmount > 0f
            && definition.MapObjectArchetype != null && definition.MapObjectArchetype.RenderParts.Count > 0
            && prototype.GetComponent<BoxCollider>() != null;
    }
    internal bool SupportsPrototype(MiningMachine prototype) => prototype != null && (templates.ContainsKey(prototype) || Supports(prototype));
    public MiningMachineInstance Register(MiningMachine prototype, BlockStateStore.InstallationSaveState placement)
    {
        Vector2Int key = BlockStateStore.GetInstallationStorageKey(placement);
        if (byKey.TryGetValue(key, out var existing)) { Bind(existing); return existing; }
        if (!templates.TryGetValue(prototype, out var template))
        { template = new MiningRenderTemplate(prototype, Terrain.ResolveInstallationPlacementController()); templates.Add(prototype, template); }
        if (!VirtualObjectWorld.Ensure().TryGetInstallationHandle(key, out var handle)) return null;
        var slot = states.Allocate(new State { Clock = new MiningProcess { Production = ProductionProcess.Empty,
            SampleTick = MapObjectTickManager.CurrentSimulationTick } });
        var miner = new MiningMachineInstance(this, slot.Index, slot.Generation, handle, prototype, placement, template);
        byKey.Add(key, miner); miner.OrderIndex = instances.Count; instances.Add(miner);
        if (template.UsesFuel) FuelDrivenCount++;
        // Block binding can wake the miner synchronously; initialize scheduling first.
        FacilitySimulationWorld.Register(miner, false);
        Observe(placement.occupiedCoordinates, miner); Observe(miner.OutputCoordinates, miner); Observe(placement.inputOutputState.gridCoordinates, miner);
        Observe(placement.inputOutputState.inputEnergyCoordinates, miner);
        Vector2Int cell = Cell(miner.WorldPosition);
        if (!cells.TryGetValue(cell, out var members)) cells.Add(cell, members = new List<MiningMachineInstance>(8));
        members.Add(miner); Bind(miner); MarkerCount += miner.OutputCoordinates.Count + placement.inputOutputState.inputEnergyCoordinates.Count; markersDirty = true;
        if (template.Watts > 0f) UtilityPole.InvalidateRobotArmConsumers();
        miner.Wake();
        return miner;
    }
    private void Observe(IReadOnlyList<Vector2Int> coordinates, MiningMachineInstance miner)
    {
        for (int i = 0; coordinates != null && i < coordinates.Count; i++)
        {
            if (!observers.TryGetValue(coordinates[i], out var list))
                observers.Add(coordinates[i], list = new List<MiningMachineInstance>(2));
            if (!list.Contains(miner)) list.Add(miner);
        }
    }
    public bool CoordinateIsBlockType(Vector2Int coordinate, InputOutputModule.RectGridBlockType type)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i].Prototype.TryGetRectGridBlockTypeAtCoordinate(owners[i].Prototype, owners[i].AnchorCoordinate,
                owners[i].Placement.quarterTurns, coordinate, out var actual) && actual == type) return true;
        return false;
    }
    public bool AppendOutputItemIds(Vector2Int coordinate, ISet<int> result)
    {
        if (result == null || !observers.TryGetValue(coordinate, out var owners)) return false;
        bool found = false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i].Placement.inputOutputState.outputCoordinates.Contains(coordinate))
                found |= owners[i].Prototype.TryAppendPlacementOutputItemIds(Terrain, owners[i].RuntimeOccupiedCoordinates, result);
        return found;
    }
    public bool IsEnergyArea(Vector2Int coordinate, ItemDefinition.EnergyType type = ItemDefinition.EnergyType.None)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i].Placement.inputOutputState.inputEnergyCoordinates.Contains(coordinate)
                && (type == ItemDefinition.EnergyType.None || type == owners[i].Template.EnergyType)) return true;
        return false;
    }
    public bool AppendEnergyTypes(Vector2Int coordinate, ISet<ItemDefinition.EnergyType> result)
    {
        if (result == null || !observers.TryGetValue(coordinate, out var owners)) return false;
        bool found = false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i].Placement.inputOutputState.inputEnergyCoordinates.Contains(coordinate))
            { result.Add(owners[i].Template.EnergyType); found = true; }
        return found;
    }
    public void ClearItems()
    {
        for (int i = 0; i < instances.Count; i++)
            instances[i].ClearStoredEnergyAndProduction();
    }
    public bool TryGet(Vector2Int key, out MiningMachineInstance miner) => byKey.TryGetValue(key, out miner);
    internal void Bind(MiningMachineInstance miner)
    {
        foreach (var coordinate in miner.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null) block.SetMapObject(miner);
    }
    public void Wake(Vector2Int coordinate)
    {
        if (!observers.TryGetValue(coordinate, out var list)) return;
        for (int i = 0; i < list.Count; i++) list[i].Wake();
    }
    internal void WakePublishedBelts(TerrainGenerator terrain)
    {
        terrain.CollectPublishedBeltObservers(observers, beltWakeScratch);
        for (int i = 0; i < beltWakeScratch.Count; i++) Wake(beltWakeScratch[i].Coordinate);
        beltWakeScratch.Clear();
    }
    public void WakeAll() { for (int i = 0; i < instances.Count; i++) instances[i].Wake(); }
    public void Remove(Vector2Int key)
    {
        if (!byKey.Remove(key, out var miner)) return;
        ProjectF.Benchmark.BenchmarkInputSupply.Remove(miner);
        FacilitySimulationWorld.Unregister(miner); UtilityPole.UnregisterRobotArmConsumer(miner);
        foreach (var coordinate in miner.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null && ReferenceEquals(block.MapObject, miner)) block.SetMapObject(null);
        RemoveObservers(miner.RuntimeOccupiedCoordinates, miner); RemoveObservers(miner.OutputCoordinates, miner); RemoveObservers(miner.Placement.inputOutputState.gridCoordinates, miner);
        RemoveObservers(miner.Placement.inputOutputState.inputEnergyCoordinates, miner);
        Vector2Int cell = Cell(miner.WorldPosition);
        var members = cells[cell]; members.Remove(miner); if (members.Count == 0) cells.Remove(cell);
        int last = instances.Count - 1;
        instances[miner.OrderIndex] = instances[last]; instances[miner.OrderIndex].OrderIndex = miner.OrderIndex;
        instances.RemoveAt(last);
        if (miner.Template.UsesFuel) FuelDrivenCount--;
        MarkerCount -= miner.OutputCoordinates.Count + miner.Placement.inputOutputState.inputEnergyCoordinates.Count; visibleMarkers.Remove(miner); markersDirty = true;
        if (ReferenceEquals(selectedMarkerMiner, miner)) selectedMarkerMiner = null;
        view?.Unbind(miner); states.Release(miner.Index, miner.Generation);
    }
    private void RemoveObservers(IReadOnlyList<Vector2Int> coordinates, MiningMachineInstance miner)
    {
        for (int i = 0; coordinates != null && i < coordinates.Count; i++)
            if (observers.TryGetValue(coordinates[i], out var list))
            { list.Remove(miner); if (list.Count == 0) observers.Remove(coordinates[i]); }
    }
    internal void BuildCandidates(CameraRenderCulling culling, List<MiningMachineInstance> result)
    {
        result.Clear();
        if (!culling.TryGetVisibleCellRange(32, 2, out var min, out var max)) { result.AddRange(instances); return; }
        if ((long)(max.x - min.x + 1) * (max.y - min.y + 1) > cells.Count * 2L)
        {
            foreach (var pair in cells) if (pair.Key.x >= min.x && pair.Key.x <= max.x && pair.Key.y >= min.y && pair.Key.y <= max.y)
                result.AddRange(pair.Value);
            return;
        }
        for (int y = min.y; y <= max.y; y++)
        for (int x = min.x; x <= max.x; x++)
            if (cells.TryGetValue(new Vector2Int(x, y), out var list)) result.AddRange(list);
    }
    internal void BuildNearby(Vector3 position, List<MiningMachineInstance> result)
    {
        result.Clear(); var cell = Cell(position);
        for (int y = cell.y - 1; y <= cell.y + 1; y++)
        for (int x = cell.x - 1; x <= cell.x + 1; x++)
            if (cells.TryGetValue(new Vector2Int(x, y), out var list)) result.AddRange(list);
    }
    internal static Vector2Int Cell(Vector3 position) => new Vector2Int(Mathf.FloorToInt(position.x / 32), Mathf.FloorToInt(position.z / 32));
    public bool TryRaycast(Ray ray, float maxDistance, out MiningMachineInstance target, out float distance)
    {
        target = null; distance = maxDistance;
        if (maxDistance <= 0f || cells.Count == 0) return false;
        // Traverse the ray, not its entire enclosing rectangle. Long free-camera rays
        // must not turn into a quadratic scan of empty spatial cells.
        var traversal = new SpatialRayCellTraversal(ray, maxDistance, 32);
        while (traversal.MoveNext())
        {
            Vector2Int cell = traversal.Current;
            for (int y = cell.y - 1; y <= cell.y + 1; y++)
            for (int x = cell.x - 1; x <= cell.x + 1; x++)
                if (cells.TryGetValue(new Vector2Int(x, y), out var members))
                    for (int i = 0; i < members.Count; i++)
                        if (members[i].CullBounds.IntersectRay(ray, out float d) && d >= 0f && d < distance)
                        { target = members[i]; distance = d; }
        }
        return target != null;
    }
    public void SetSelectedMarkerMiner(MiningMachineInstance miner)
    { if (!ReferenceEquals(selectedMarkerMiner, miner)) { selectedMarkerMiner = miner; markersDirty = true; } }
    internal bool ShouldShowLinkedUi(MiningMachineInstance miner, in AreaMarkerVisibilityContext context)
    {
        return miner.IsRuntimeActive && miner.OutputCoordinates.Count > 0
            && AreaMarkerVisibilityContext.ShouldShow(3.6f, false, miner == selectedMarkerMiner,
                context.ShowAll, context.HasPlayer, context.PlayerPosition, miner.WorldPosition);
    }
    internal bool RefreshAreaMarkers(in AreaMarkerVisibilityContext context)
    {
        bool changed = markersDirty; markersDirty = false; markerCandidates.Clear();
        foreach (var miner in visibleMarkers) markerCandidates.Add(miner);
        if (context.ShowAll) foreach (var miner in instances) markerCandidates.Add(miner);
        else if (context.HasPlayer)
        { BuildNearby(context.PlayerPosition, markerScratch); foreach (var miner in markerScratch) markerCandidates.Add(miner); }
        if (selectedMarkerMiner != null) markerCandidates.Add(selectedMarkerMiner);
        VisibleMarkerCount = 0;
        foreach (var miner in markerCandidates)
        {
            bool visible = ShouldShowLinkedUi(miner, context);
            if (visible) { changed |= visibleMarkers.Add(miner); VisibleMarkerCount += miner.OutputCoordinates.Count + miner.Placement.inputOutputState.inputEnergyCoordinates.Count; }
            else changed |= visibleMarkers.Remove(miner);
        }
        return changed;
    }
    internal void AppendAreaMarkers(AreaMarkerRenderer renderer)
    {
        if (Terrain.IsBenchmarkPlacementInProgress) return;
        Sprite arrow = UIManager.Instance != null ? UIManager.Instance.ArrowImage : null;
        foreach (var miner in visibleMarkers)
        {
            foreach (var coordinate in miner.Placement.inputOutputState.inputEnergyCoordinates)
            {
                Vector3 position = new Vector3(coordinate.x, miner.WorldPosition.y + 0.08f, coordinate.y);
                renderer.Append(new AreaMarkerSpawnRequest(position, miner.Template.EnergyMarkerIcon), Matrix4x4.Translate(position), 0, false, false);
            }
            foreach (var coordinate in miner.OutputCoordinates)
            {
                Vector3 position = new Vector3(coordinate.x, miner.WorldPosition.y + 0.08f, coordinate.y);
                Vector3 direction = position - miner.WorldPosition;
                var request = new AreaMarkerSpawnRequest(position, arrow, -Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
                renderer.Append(request, Matrix4x4.Translate(position), 0, false, false);
            }
        }
    }
    public void FlushSaveStates() { for (int i = 0; i < instances.Count; i++) instances[i].Persist(); }
    public void ClearRecords()
    {
        for (int i = instances.Count - 1; i >= 0; i--) Remove(instances[i].StorageKey);
        cells.Clear(); templates.Clear(); markerCandidates.Clear(); visibleMarkers.Clear(); markerScratch.Clear();
        selectedMarkerMiner = null; VisibleMarkerCount = MarkerCount = 0; markersDirty = true; ProcessedUpdates = 0;
    }
    public void Dispose()
    {
        ClearRecords(); view?.Release(); view = null;
        if (ReferenceEquals(Current, this)) Current = null;
    }
    public static void AppendProfilerCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("MiningECS", "Entities", Current != null ? Current.Count : 0);
        MapObjectTickProfiler.AddRuntimeCounter("MiningECS", "BurnEntities", Current != null ? Current.FuelDrivenCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("MiningECS", "Visible", Current != null ? Current.VisibleCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("MiningECS", "ProcessedUpdates", Current != null ? Current.ProcessedUpdates : 0);
        MapObjectTickProfiler.AddRuntimeCounter("MiningECS", "GameObjects", Current != null && Current.view != null ? 1 : 0);
    }
}
