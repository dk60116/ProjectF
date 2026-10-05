using System;
using System.Collections.Generic;
using ProjectF.Runtime;
using UnityEngine;
using ProjectF.Rendering;

namespace ProjectF.MapObjects
{
public sealed partial class ForestryWorld : IDisposable
{
    public static ForestryWorld Current { get; private set; }
    public TerrainGenerator Terrain { get; }
    internal struct State
    {
        internal ProjectF.Simulation.LoggingProcess Logging;
        internal ProjectF.Simulation.PlantingProcess Planting;
        internal MapObjectHandle ResourceIdentity;
        internal bool HasDemand, PowerBlocked, Working, Applying;
        internal float SupplyRatio;
        internal double AnimationPhase;
    }
    private readonly ResourceStateSlots<State> states = new ResourceStateSlots<State>();
    internal ref State GetState(int index, uint generation) => ref states.Get(index, generation);
    internal BlockStateStore Store { get; }
    private readonly List<Vector2Int> wakeCoordinates = new List<Vector2Int>(16);
    private readonly Dictionary<Vector2Int, List<ForestryInstance>> observers = new Dictionary<Vector2Int, List<ForestryInstance>>();
    private readonly Dictionary<ForestryInstance, Vector2Int[]> observedCoordinates = new Dictionary<ForestryInstance, Vector2Int[]>();
    private readonly List<Block> beltWakeScratch = new List<Block>();
    public long ProcessedUpdates { get; internal set; }
    private readonly Dictionary<Vector2Int, ForestryInstance> byKey = new Dictionary<Vector2Int, ForestryInstance>();
    private readonly Dictionary<Vector2Int, List<ForestryInstance>> cells = new Dictionary<Vector2Int, List<ForestryInstance>>();
    private readonly List<ForestryInstance> instances = new List<ForestryInstance>();
    private readonly Dictionary<InstallationObject, ForestryRenderTemplate> templates = new Dictionary<InstallationObject, ForestryRenderTemplate>();
    private ForestryWorldView view;
    public IReadOnlyList<ForestryInstance> Instances => instances;
    public int Count => instances.Count;
    public int VisibleCount => view != null ? view.VisibleCount : 0;
    public static ForestryWorld Ensure(TerrainGenerator terrain)
    {
        if (Current != null && Current.Terrain == terrain) return Current;
        Current?.Dispose(); Current = new ForestryWorld(terrain); return Current;
    }
    private ForestryWorld(TerrainGenerator terrain)
    { Terrain = terrain; Store = terrain.GetComponent<BlockStateStore>(); view = ForestryWorldView.Create(this, terrain.transform); }
    internal bool Contains(int index, uint generation) => states.Contains(index, generation);
    public static bool Supports(InstallationObject prototype) => (prototype is LoggingMachine || prototype is SeedPlanter)
        && (prototype.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(prototype.ResolveItemId()))?.MapObjectArchetype?.RenderParts.Count > 0
        && (prototype.GetComponent<BoxCollider>() != null || prototype.GetComponent<SphereCollider>() != null);
    internal bool SupportsPrototype(InstallationObject prototype) => prototype != null && (templates.ContainsKey(prototype) || Supports(prototype));
    public ForestryInstance Register(InstallationObject prototype, BlockStateStore.InstallationSaveState placement)
    {
        var key = BlockStateStore.GetInstallationStorageKey(placement);
        if (byKey.TryGetValue(key, out var existing)) { Bind(existing); return existing; }
        if (!templates.TryGetValue(prototype, out var template))
        { template = new ForestryRenderTemplate(prototype, Terrain.ResolveInstallationPlacementController()); templates.Add(prototype, template); }
        if (!VirtualObjectWorld.Ensure().TryGetInstallationHandle(key, out var handle)) return null;
        var slot = states.Allocate(default);
        ForestryInstance instance = prototype is LoggingMachine logger
            ? new LoggingMachineInstance(this, slot.Index, slot.Generation, handle, logger, placement, template)
            : new SeedPlanterInstance(this, slot.Index, slot.Generation, handle, (SeedPlanter)prototype, placement, template);
        byKey.Add(key, instance); instance.OrderIndex = instances.Count; instances.Add(instance);
        var cell = Cell(instance.WorldPosition);
        if (!cells.TryGetValue(cell, out var members)) cells.Add(cell, members = new List<ForestryInstance>(8));
        members.Add(instance);
        FacilitySimulationWorld.Register(instance, false);
        wakeCoordinates.Clear(); wakeCoordinates.AddRange(placement.occupiedCoordinates);
        if (instance is LoggingMachineInstance)
            for (int i = 0; i < LoggingMachine.HarvestDirectionCount; i++)
                wakeCoordinates.Add(LoggingMachine.GetHarvestCoordinate(instance.AnchorCoordinate, placement.quarterTurns, i));
        else if (placement.inputOutputState != null)
        {
            wakeCoordinates.AddRange(placement.inputOutputState.gridCoordinates);
            wakeCoordinates.AddRange(placement.inputOutputState.outputCoordinates);
            wakeCoordinates.AddRange(placement.inputOutputState.inputEnergyCoordinates);
            for (int i = 0; i < placement.inputOutputState.inputItemAreas.Count; i++)
                wakeCoordinates.Add(placement.inputOutputState.inputItemAreas[i].coordinate);
        }
        FacilityRuntimeWakeRegistry.Register(instance, wakeCoordinates);
        observedCoordinates.Add(instance, wakeCoordinates.ToArray());
        for (int i = 0; i < wakeCoordinates.Count; i++)
        {
            if (!observers.TryGetValue(wakeCoordinates[i], out var owners))
                observers.Add(wakeCoordinates[i], owners = new List<ForestryInstance>(2));
            if (owners.Contains(instance)) continue;
            int insertion = 0;
            while (insertion < owners.Count && owners[insertion].SimulationId < instance.SimulationId) insertion++;
            owners.Insert(insertion, instance);
        }
        markersDirty = true; Bind(instance); UtilityPole.InvalidateRobotArmConsumers(); instance.Wake();
        return instance;
    }
    public bool TryGet(Vector2Int key, out ForestryInstance instance) => byKey.TryGetValue(key, out instance);
    internal void Bind(ForestryInstance instance)
    {
        foreach (var coordinate in instance.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null) block.SetMapObject(instance);
    }
    public void Remove(Vector2Int key)
    {
        if (!byKey.Remove(key, out var instance)) return;
        visibleMarkers.Remove(instance); markersDirty = true;
        if (ReferenceEquals(selectedMarkerInstance, instance)) selectedMarkerInstance = null;
        FacilityRuntimeWakeRegistry.Unregister(instance);
        FacilitySimulationWorld.Unregister(instance); UtilityPole.UnregisterRobotArmConsumer(instance);
        if (observedCoordinates.Remove(instance, out var observed))
            for (int i = 0; i < observed.Length; i++)
                if (observers.TryGetValue(observed[i], out var owners))
                { owners.Remove(instance); if (owners.Count == 0) observers.Remove(observed[i]); }
        foreach (var coordinate in instance.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null && ReferenceEquals(block.MapObject, instance)) block.SetMapObject(null);
        var cell = Cell(instance.WorldPosition); var members = cells[cell]; members.Remove(instance);
        if (members.Count == 0) cells.Remove(cell);
        int last = instances.Count - 1;
        instances[instance.OrderIndex] = instances[last]; instances[instance.OrderIndex].OrderIndex = instance.OrderIndex;
        instances.RemoveAt(last); view?.Unbind(instance); states.Release(instance.Index, instance.Generation);
    }
    internal void BuildCandidates(CameraRenderCulling culling, List<ForestryInstance> result)
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
    internal void BuildNearby(Vector3 position, List<ForestryInstance> result)
    {
        result.Clear(); var cell = Cell(position);
        for (int y = cell.y - 1; y <= cell.y + 1; y++)
        for (int x = cell.x - 1; x <= cell.x + 1; x++)
            if (cells.TryGetValue(new Vector2Int(x, y), out var list)) result.AddRange(list);
    }
    internal static Vector2Int Cell(Vector3 position) => new Vector2Int(Mathf.FloorToInt(position.x / 32), Mathf.FloorToInt(position.z / 32));
    public bool TryRaycast(Ray ray, float maxDistance, out ForestryInstance target, out float distance)
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
                        if (members[i].IsRuntimeActive && !members[i].PlacementPresentationSuppressed
                            && members[i].CullBounds.IntersectRay(ray, out float d) && d >= 0f && d < distance)
                        { target = members[i]; distance = d; }
        }
        return target != null;
    }

    public void WakeAll() { for (int i = 0; i < instances.Count; i++) instances[i].Wake(); }
    public void Wake(Vector2Int coordinate)
    { if (observers.TryGetValue(coordinate, out var owners)) for (int i = 0; i < owners.Count; i++) owners[i].Wake(); }
    internal void WakePublishedBelts(TerrainGenerator terrain)
    {
        terrain.CollectPublishedBeltObservers(observers, beltWakeScratch);
        for (int i = 0; i < beltWakeScratch.Count; i++) Wake(beltWakeScratch[i].Coordinate);
        beltWakeScratch.Clear();
    }
    public void PersistAll() { for (int i = 0; i < instances.Count; i++) instances[i].Persist(); }
    public void ClearItems() { for (int i = 0; i < instances.Count; i++) instances[i].ClearItems(); }
    public bool CoordinateIsBlockType(Vector2Int coordinate, InputOutputModule.RectGridBlockType type)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i] is SeedPlanterInstance planter && planter.ContainsArea(coordinate, type)) return true;
        return false;
    }
    public bool AppendInputItemIds(Vector2Int coordinate, ISet<int> ids)
    {
        if (ids == null || !observers.TryGetValue(coordinate, out var owners)) return false;
        bool found = false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i] is SeedPlanterInstance planter) found |= planter.AppendInputItemIds(coordinate, ids);
        return found;
    }
    public bool AppendEnergyTypes(Vector2Int coordinate, ISet<ItemDefinition.EnergyType> types)
    {
        if (types == null || !observers.TryGetValue(coordinate, out var owners)) return false;
        bool found = false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i] is SeedPlanterInstance planter && planter.Placement.inputOutputState.inputEnergyCoordinates.Contains(coordinate))
                for (int j = 0; j < planter.BoundItemDefinition.UseEnergyRequirementCount; j++)
                    if (planter.BoundItemDefinition.TryGetUseEnergyRequirement(j, out var energy)
                        && energy.energyType != ItemDefinition.EnergyType.None && energy.energyType != ItemDefinition.EnergyType.Electricity)
                    { types.Add(energy.energyType); found = true; }
        return found;
    }
    internal int RecoverSeeds(Vector2Int coordinate, int item, int count, Vector3 position)
    {
        int accepted = 0;
        if (!observers.TryGetValue(coordinate, out var owners)) return 0;
        // Simulation identity order is stable across restore; each seed has one receiver.
        for (int i = 0; i < owners.Count && accepted < count; i++)
            if (owners[i] is SeedPlanterInstance planter)
                accepted += planter.ReceiveHarvestedSeeds(coordinate, item, count - accepted, position);
        return accepted;
    }
    public void ClearRecords()
    {
        for (int i = instances.Count - 1; i >= 0; i--) Remove(instances[i].StorageKey);
        foreach (var template in templates.Values) template.Dispose();
        templates.Clear(); cells.Clear(); observers.Clear(); observedCoordinates.Clear();
        visibleMarkers.Clear(); markerCandidates.Clear(); markerScratch.Clear(); selectedMarkerInstance = null;
    }
    public void Dispose()
    { ClearRecords(); view?.Release(); view = null; if (ReferenceEquals(Current, this)) Current = null; }
    public static void AppendProfilerCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("ForestryECS", "Entities", Current != null ? Current.Count : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ForestryECS", "Visible", Current != null ? Current.VisibleCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ForestryECS", "GameObjects", Current != null && Current.view != null ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ForestryECS", "ProcessedUpdates", Current != null ? Current.ProcessedUpdates : 0);
    }
}
}

