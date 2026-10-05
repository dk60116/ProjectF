using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Runtime;
using ProjectF.Simulation;
using ProjectF.Rendering;

// FacilitySimulationWorld owns ticks. This world owns identity, state and spatial indices.
public sealed partial class ProductionWorld : IDisposable
{
    internal struct State
    {
        internal ProductionProcess Production;
        internal OilDrillingProcess Oil;
        internal float OilRetention;
        internal long OilUnitsPerTick;
        internal long SampleTick, CompleteEnergy, DurationTicks;
        internal float SupplyRatio;
        internal double AnimationPhase;
        internal bool HasTarget, NeedsEvaluation;
    }
    public static ProductionWorld Current { get; private set; }
    public TerrainGenerator Terrain { get; }
    internal BlockStateStore Store { get; }
    private readonly ResourceStateSlots<State> states = new ResourceStateSlots<State>();
    private readonly Dictionary<Vector2Int, ProductionFacilityInstance> byKey = new Dictionary<Vector2Int, ProductionFacilityInstance>();
    private readonly Dictionary<Vector2Int, List<ProductionFacilityInstance>> observers = new Dictionary<Vector2Int, List<ProductionFacilityInstance>>();
    private readonly List<Block> beltWakeScratch = new List<Block>();
    private readonly Dictionary<Vector2Int, List<ProductionFacilityInstance>> cells = new Dictionary<Vector2Int, List<ProductionFacilityInstance>>();
    private readonly List<ProductionFacilityInstance> instances = new List<ProductionFacilityInstance>();
    private readonly Dictionary<InputOutputModule, ProductionRenderTemplate> templates = new Dictionary<InputOutputModule, ProductionRenderTemplate>();
    internal readonly List<int> InputAreaScratch = new List<int>(8);
    private ProductionWorldView view;
    private OilDrillingBatch oilBatch;
    internal OilDrillingBatch OilBatch => oilBatch ??= new OilDrillingBatch(this);
    private ProductionFacilityInstance selectedMarkerFacility;
    private readonly HashSet<ProductionFacilityInstance> visibleMarkers = new HashSet<ProductionFacilityInstance>();
    private readonly HashSet<ProductionFacilityInstance> markerCandidates = new HashSet<ProductionFacilityInstance>();
    private readonly List<ProductionFacilityInstance> markerScratch = new List<ProductionFacilityInstance>();
    private bool markersDirty = true;
    private long nextManualCheckTick;
    public int VisibleMarkerCount { get; private set; }
    public int MarkerCount { get; private set; }
    internal int MarkerCandidateCount => markerCandidates.Count;
    public IReadOnlyList<ProductionFacilityInstance> Instances => instances;
    public int Count => instances.Count;
    public int OilDrillCount { get; private set; }
    public long ProcessedUpdates { get; internal set; }
    public int VisibleCount => view != null ? view.VisibleCount : 0;
    public static ProductionWorld Ensure(TerrainGenerator terrain)
    {
        if (Current != null && Current.Terrain == terrain) return Current;
        Current?.Dispose();
        Current = new ProductionWorld(terrain);
        return Current;
    }
    private ProductionWorld(TerrainGenerator terrain)
    {
        Terrain = terrain; Store = terrain.GetComponent<BlockStateStore>();
        view = ProductionWorldView.Create(this, terrain.transform);
        InputOutputModule.RuntimePipeTopologyChanged += HandlePipeTopologyChanged;
    }
    internal ref State GetState(int index, uint generation) => ref states.Get(index, generation);
    internal bool Contains(int index, uint generation) => states.Contains(index, generation);
    public static bool Supports(InputOutputModule prototype)
    {
        if (prototype == null) return false;
        var definition = prototype.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(prototype.ResolveItemId());
        return (prototype.GetType() == typeof(InputOutputModule) || prototype is ProductionMachine || prototype is OilDrillingMachine)
            && definition?.MapObjectArchetype?.RenderParts.Count > 0
            && prototype.GetComponent<BoxCollider>() != null;
    }
    internal bool SupportsPrototype(InputOutputModule prototype) => prototype != null && (templates.ContainsKey(prototype) || Supports(prototype));
    public ProductionFacilityInstance Register(InputOutputModule prototype, BlockStateStore.InstallationSaveState placement)
    {
        Vector2Int key = BlockStateStore.GetInstallationStorageKey(placement);
        if (byKey.TryGetValue(key, out var existing)) { Bind(existing); return existing; }
        if (!templates.TryGetValue(prototype, out var template))
        { template = new ProductionRenderTemplate(prototype, Terrain.ResolveInstallationPlacementController()); templates.Add(prototype, template); }
        if (!VirtualObjectWorld.Ensure().TryGetInstallationHandle(key, out var handle)) return null;
        var slot = states.Allocate(new State { Production = ProductionProcess.Empty,
            SampleTick = MapObjectTickManager.CurrentSimulationTick });
        var facility = new ProductionFacilityInstance(this, slot.Index, slot.Generation, handle, prototype, placement, template);
        byKey.Add(key, facility); facility.OrderIndex = instances.Count; instances.Add(facility);
        if (template.IsOilDrill) OilDrillCount++;
        // Bind emits synchronous item-stack notifications that can schedule this facility.
        // Register before publishing observers so registration cannot cancel that first wake.
        if (template.IsOilDrill) OilBatch.Register(facility); else FacilitySimulationWorld.Register(facility, false);
        Observe(placement.occupiedCoordinates, facility); Observe(facility.OutputCoordinates, facility); Observe(placement.inputOutputState.gridCoordinates, facility);
        if (template.IsOilDrill) Observe(facility.OilTargetCoordinate, facility);
        Vector2Int cell = Cell(facility.WorldPosition);
        if (!cells.TryGetValue(cell, out var members)) cells.Add(cell, members = new List<ProductionFacilityInstance>(8));
        members.Add(facility); Bind(facility); MarkerCount += facility.MarkerCount; markersDirty = true;
        UtilityPole.InvalidateRobotArmConsumers();
        facility.Wake();
        if (template.HasPipePorts) InputOutputModule.NotifyRuntimePipeTopologyChanged(null);
        return facility;
    }
    private void Observe(IReadOnlyList<Vector2Int> coordinates, ProductionFacilityInstance facility)
    {
        for (int i = 0; coordinates != null && i < coordinates.Count; i++)
            Observe(coordinates[i], facility);
    }
    private void Observe(Vector2Int coordinate, ProductionFacilityInstance facility)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) observers.Add(coordinate, owners = new List<ProductionFacilityInstance>(2));
        if (!owners.Contains(facility)) owners.Add(facility);
    }
    private void HandlePipeTopologyChanged(InputOutputModule source)
    {
        for (int i = 0; i < instances.Count; i++) if (instances[i].Template.HasPipePorts) instances[i].Wake();
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
                found |= owners[i].AppendOutputItemIds(result);
        return found;
    }
    internal void MarkDisplayDirty(ProductionFacilityInstance facility)
    {
        if (visibleMarkers.Contains(facility) || facility == selectedMarkerFacility) markersDirty = true;
        if (facility.Template.HasPipePorts) Pipe.InvalidateFluidDisplayNetworkCache();
    }
    public bool AppendInputItemIds(Vector2Int coordinate, ISet<int> result, bool acceptedOnly)
    {
        if (result == null || !observers.TryGetValue(coordinate, out var owners)) return false;
        bool found = false;
        for (int i = 0; i < owners.Count; i++)
        {
            var facility = owners[i];
            var areas = facility.Placement.inputOutputState.inputItemAreas;
            for (int j = 0; j < areas.Count; j++)
            {
                var area = areas[j];
                if (area.coordinate != coordinate) continue;
                if (!acceptedOnly && area.itemId >= 0) { result.Add(area.itemId); found = true; continue; }
                for (int n = 0; n < facility.Template.Recipes.Length; n++)
                {
                    var recipe = facility.Template.Recipes[n];
                    if (acceptedOnly && (!facility.IsRecipeAvailable(recipe) || facility.Prototype is ProductionMachine
                        && !ReferenceEquals(recipe, facility.SelectedRecipe))) continue;
                    for (int k = 0; k < recipe.Inputs.Count; k++)
                    {
                        int item = recipe.Inputs[k].itemId;
                        if ((area.itemId < 0 || area.itemId == item) && InputOutputModule.ResolveItemDefinition(item)?.isFluid != true)
                        { result.Add(item); found = true; }
                    }
                }
            }
        }
        return found;
    }
    public bool AppendEnergyTypes(Vector2Int coordinate, ISet<ItemDefinition.EnergyType> result)
    {
        if (result == null || !observers.TryGetValue(coordinate, out var owners)) return false;
        bool found = false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i].Placement.inputOutputState.inputEnergyCoordinates.Contains(coordinate))
                found |= owners[i].BoundItemDefinition.AppendUseEnergyTypes(result);
        return found;
    }
    public bool IsDirectItemArea(Vector2Int coordinate, int item = -1)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
        {
            var facility = owners[i];
            if (!facility.Prototype.TryGetRectGridBlockTypeAtCoordinate(facility.Prototype, facility.AnchorCoordinate,
                facility.Placement.quarterTurns, coordinate, out var type) || !InputOutputModule.IsInputItemBlockType(type)
                || !InputOutputModule.AllowsDirectAreaInteraction(type)) continue;
            var areas = facility.Placement.inputOutputState.inputItemAreas;
            for (int j = 0; j < areas.Count; j++)
                if (areas[j].coordinate == coordinate && (item < 0 || areas[j].itemId < 0 || areas[j].itemId == item)) return true;
        }
        return false;
    }
    public bool IsDirectOutputArea(Vector2Int coordinate) => CoordinateIsBlockType(coordinate, InputOutputModule.RectGridBlockType.Output)
        || CoordinateIsBlockType(coordinate, InputOutputModule.RectGridBlockType.DoublePipeOutputItem);
    public bool IsEnergyArea(Vector2Int coordinate, ItemDefinition.EnergyType type = ItemDefinition.EnergyType.None)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
        {
            if (!owners[i].Placement.inputOutputState.inputEnergyCoordinates.Contains(coordinate)) continue;
            var definition = owners[i].BoundItemDefinition;
            for (int j = 0; j < definition.UseEnergyRequirementCount; j++)
                if (definition.TryGetUseEnergyRequirement(j, out var requirement) && requirement.useEnergyAmount > 0
                    && requirement.energyType != ItemDefinition.EnergyType.None
                    && (type == ItemDefinition.EnergyType.None || requirement.energyType == type)) return true;
        }
        return false;
    }
    public void ClearItems()
    {
        for (int i = 0; i < instances.Count; i++)
        {
            var facility = instances[i];
            facility.ClearItems();
            facility.Wake(); facility.Persist();
        }
    }
    public bool TryGet(Vector2Int key, out ProductionFacilityInstance facility) => byKey.TryGetValue(key, out facility);
    internal bool IsOilTargetClaimed(Vector2Int coordinate)
    {
        if (!observers.TryGetValue(coordinate, out var owners)) return false;
        for (int i = 0; i < owners.Count; i++)
            if (owners[i].Template.IsOilDrill && owners[i].IsRuntimeActive && owners[i].OilTargetCoordinate == coordinate) return true;
        return false;
    }
    internal void Bind(ProductionFacilityInstance facility)
    {
        foreach (var coordinate in facility.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null) block.SetMapObject(facility);
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
    internal void RefreshRecipeAvailability()
    {
        long tick = MapObjectTickManager.CurrentSimulationTick;
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking || tick < nextManualCheckTick) return;
        nextManualCheckTick = tick + SimulationTickWorld.DefaultSimulationTicksPerSecond;
        // Manual possession is a global dependency. Probe shared recipes rather than each sleeping facility.
        foreach (var template in templates.Values)
        {
            if (template.IsOilDrill) continue;
            bool changed = false;
            for (int i = 0; i < template.Recipes.Length; i++)
            {
                var recipe = template.Recipes[i];
                if (recipe.PublishedManualAvailable == recipe.IsManualAvailable) continue;
                recipe.PublishedManualAvailable = recipe.IsManualAvailable; changed = true;
            }
            if (!changed) continue;
            for (int i = 0; i < instances.Count; i++)
                if (ReferenceEquals(instances[i].Template, template))
                { instances[i].Wake(); if (template.HasPipePorts) WakeNativeProducers(instances[i]); }
        }
    }
    public void WakeAll() { for (int i = 0; i < instances.Count; i++) instances[i].Wake(); }
    internal void ResetOilBenchmarkWork()
    {
        for (int i = 0; i < instances.Count; i++) if (instances[i].Template.IsOilDrill) instances[i].ResetOilBenchmarkWork();
    }
    public void Remove(Vector2Int key)
    {
        if (!byKey.Remove(key, out var facility)) return;
        ProjectF.Benchmark.BenchmarkInputSupply.Remove(facility);
        if (facility.Template.IsOilDrill) oilBatch?.Unregister(facility); else FacilitySimulationWorld.Unregister(facility);
        UtilityPole.UnregisterRobotArmConsumer(facility);
        foreach (var coordinate in facility.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null && ReferenceEquals(block.MapObject, facility)) block.SetMapObject(null);
        RemoveObservers(facility.RuntimeOccupiedCoordinates, facility); RemoveObservers(facility.OutputCoordinates, facility); RemoveObservers(facility.Placement.inputOutputState.gridCoordinates, facility);
        if (facility.Template.IsOilDrill) RemoveObserver(facility.OilTargetCoordinate, facility);
        RemoveFluidOutputRoutes(facility);
        Vector2Int cell = Cell(facility.WorldPosition);
        var members = cells[cell]; members.Remove(facility); if (members.Count == 0) cells.Remove(cell);
        int last = instances.Count - 1;
        instances[facility.OrderIndex] = instances[last]; instances[facility.OrderIndex].OrderIndex = facility.OrderIndex;
        instances.RemoveAt(last);
        if (facility.Template.IsOilDrill) OilDrillCount--;
        MarkerCount -= facility.MarkerCount; visibleMarkers.Remove(facility); markersDirty = true;
        if (ReferenceEquals(selectedMarkerFacility, facility)) selectedMarkerFacility = null;
        view?.Unbind(facility); states.Release(facility.Index, facility.Generation);
        nativeProducers.Remove(facility);
        if (facility.Template.HasPipePorts) InputOutputModule.NotifyRuntimePipeTopologyChanged(null);
    }
    private void RemoveObservers(IReadOnlyList<Vector2Int> coordinates, ProductionFacilityInstance facility)
    {
        for (int i = 0; coordinates != null && i < coordinates.Count; i++)
            if (observers.TryGetValue(coordinates[i], out var list))
            { list.Remove(facility); if (list.Count == 0) observers.Remove(coordinates[i]); }
    }
    private void RemoveObserver(Vector2Int coordinate, ProductionFacilityInstance facility)
    {
        if (observers.TryGetValue(coordinate, out var owners))
        { owners.Remove(facility); if (owners.Count == 0) observers.Remove(coordinate); }
    }
    internal void BuildCandidates(CameraRenderCulling culling, List<ProductionFacilityInstance> result)
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
    internal void BuildNearby(Vector3 position, List<ProductionFacilityInstance> result)
    {
        result.Clear(); var cell = Cell(position);
        for (int y = cell.y - 1; y <= cell.y + 1; y++)
        for (int x = cell.x - 1; x <= cell.x + 1; x++)
            if (cells.TryGetValue(new Vector2Int(x, y), out var list)) result.AddRange(list);
    }
    internal static Vector2Int Cell(Vector3 position) => new Vector2Int(Mathf.FloorToInt(position.x / 32), Mathf.FloorToInt(position.z / 32));
    public bool TryRaycast(Ray ray, float maxDistance, out ProductionFacilityInstance target, out float distance)
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
    public void SetSelectedMarkerFacility(ProductionFacilityInstance facility)
    { if (!ReferenceEquals(selectedMarkerFacility, facility)) { selectedMarkerFacility = facility; markersDirty = true; } }
    internal bool ShouldShowLinkedUi(ProductionFacilityInstance facility, in AreaMarkerVisibilityContext context)
    {
        return facility.IsRuntimeActive && facility.MarkerCount > 0
            && AreaMarkerVisibilityContext.ShouldShow(3.6f, false, facility == selectedMarkerFacility,
                context.ShowAll, context.HasPlayer, context.PlayerPosition, facility.WorldPosition);
    }
    internal bool RefreshAreaMarkers(in AreaMarkerVisibilityContext context)
    {
        bool changed = markersDirty; markersDirty = false; markerCandidates.Clear();
        foreach (var facility in visibleMarkers) markerCandidates.Add(facility);
        if (context.ShowAll) foreach (var facility in instances) markerCandidates.Add(facility);
        else if (context.HasPlayer)
        { BuildNearby(context.PlayerPosition, markerScratch); foreach (var facility in markerScratch) markerCandidates.Add(facility); }
        if (selectedMarkerFacility != null) markerCandidates.Add(selectedMarkerFacility);
        VisibleMarkerCount = 0;
        foreach (var facility in markerCandidates)
        {
            bool visible = ShouldShowLinkedUi(facility, context);
            if (visible) { changed |= visibleMarkers.Add(facility); VisibleMarkerCount += facility.MarkerCount; }
            else changed |= visibleMarkers.Remove(facility);
        }
        return changed;
    }
    internal void AppendAreaMarkers(AreaMarkerRenderer renderer)
    {
        if (Terrain.IsBenchmarkPlacementInProgress) return;
        Sprite arrow = UIManager.Instance != null ? UIManager.Instance.ArrowImage : null;
        foreach (var facility in visibleMarkers)
        foreach (var coordinate in facility.Placement.inputOutputState.gridCoordinates)
        {
            if (!facility.Prototype.TryGetRectGridBlockPlacementAtCoordinate(facility.Prototype, facility.AnchorCoordinate,
                facility.Placement.quarterTurns, coordinate, out var placement) || !InputOutputModule.IsInputOutputAreaBlockType(placement.blockType)) continue;
            Vector3 position = new Vector3(coordinate.x, facility.WorldPosition.y + .08f, coordinate.y);
            bool output = InputOutputModule.IsOutputBlockType(placement.blockType);
            Vector2Int external = Vector2Int.zero;
            TryPortDirection(facility, coordinate, out external);
            if (!output) external = -external;
            float rotation = -Mathf.Atan2(external.x, external.y) * Mathf.Rad2Deg;
            AreaMarkerSpawnRequest request;
            if (InputOutputModule.IsInputEnergyBlockType(placement.blockType))
                request = new AreaMarkerSpawnRequest(position, facility.Template.EnergyMarkerIcon);
            else if (InputOutputModule.AllowsPipeAreaInteraction(placement.blockType))
            {
                // Output markers always represent the selected product, never the incoming pipe's fluid.
                Sprite icon = output ? InputOutputModule.ResolveItemDefinition(facility.OutputItemId)?.isFluid == true
                    ? InputOutputModule.ResolveItemDefinition(facility.OutputItemId).icon : null : placement.itemDefinition?.icon;
                request = new AreaMarkerSpawnRequest(position, icon != null ? icon : facility.Template.FluidMarkerIcon);
                if (placement.blockType != InputOutputModule.RectGridBlockType.PipeInput) request = request.WithOverlay(arrow, rotation);
            }
            else request = new AreaMarkerSpawnRequest(position, arrow, rotation);
            renderer.Append(request, Matrix4x4.Translate(position), 0, false, false);
        }
    }
    public void FlushSaveStates() { for (int i = 0; i < instances.Count; i++) instances[i].Persist(); }
    public void ClearRecords()
    {
        InputOutputModule.BeginRuntimePipeTopologyBatch();
        try { for (int i = instances.Count - 1; i >= 0; i--) Remove(instances[i].StorageKey); }
        finally { InputOutputModule.EndRuntimePipeTopologyBatch(); }
        cells.Clear();
        outputRoutes.Clear(); storageOutputWaiters.Clear(); dataOutputWaiters.Clear(); nativeProducers.Clear(); FluidRouteSearches = 0;
        foreach (var template in templates.Values) template.Dispose();
        templates.Clear(); markerCandidates.Clear(); visibleMarkers.Clear(); markerScratch.Clear();
        selectedMarkerFacility = null; VisibleMarkerCount = MarkerCount = 0; markersDirty = true; ProcessedUpdates = 0; nextManualCheckTick = 0;
    }
    public void Dispose()
    {
        InputOutputModule.RuntimePipeTopologyChanged -= HandlePipeTopologyChanged;
        ClearRecords(); oilBatch?.Dispose(); oilBatch = null; view?.Release(); view = null;
        if (ReferenceEquals(Current, this)) Current = null;
    }
    public static void AppendProfilerCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("ProductionECS", "Entities", Current != null ? Current.Count : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ProductionECS", "Visible", Current != null ? Current.VisibleCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ProductionECS", "ProcessedUpdates", Current != null ? Current.ProcessedUpdates : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ProductionECS", "GameObjects", Current != null && Current.view != null ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter("OilDrillingECS", "Entities", Current != null ? Current.OilDrillCount : 0);
        Current?.oilBatch?.AppendProfilerCounters();
        MapObjectTickProfiler.AddRuntimeCounter("ProductionECS", "FluidRouteBuildSearches", Current != null ? Current.FluidRouteSearches : 0);
    }
}
