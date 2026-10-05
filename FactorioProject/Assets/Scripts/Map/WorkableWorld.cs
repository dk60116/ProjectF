using System;
using System.Collections.Generic;
using ProjectF.Runtime;
using UnityEngine;
using ProjectF.Rendering;

namespace ProjectF.MapObjects
{
public sealed class WorkableWorld : IDisposable
{
    public static WorkableWorld Current { get; private set; }
    public TerrainGenerator Terrain { get; }
    private readonly ResourceStateSlots<byte> states = new ResourceStateSlots<byte>();
    private readonly Dictionary<Vector2Int, WorkableInstance> byKey = new Dictionary<Vector2Int, WorkableInstance>();
    private readonly Dictionary<Vector2Int, List<WorkableInstance>> cells = new Dictionary<Vector2Int, List<WorkableInstance>>();
    private readonly List<WorkableInstance> instances = new List<WorkableInstance>();
    private readonly Dictionary<WorkableObject, WorkableRenderTemplate> templates = new Dictionary<WorkableObject, WorkableRenderTemplate>();
    private WorkableWorldView view;
    public IReadOnlyList<WorkableInstance> Instances => instances;
    public int Count => instances.Count;
    public int VisibleCount => view != null ? view.VisibleCount : 0;
    public static WorkableWorld Ensure(TerrainGenerator terrain)
    {
        if (Current != null && Current.Terrain == terrain) return Current;
        Current?.Dispose(); Current = new WorkableWorld(terrain); return Current;
    }
    private WorkableWorld(TerrainGenerator terrain)
    { Terrain = terrain; view = WorkableWorldView.Create(this, terrain.transform); WorkableObject.EnsureRangeVisualHost(); }
    internal bool Contains(int index, uint generation) => states.Contains(index, generation);
    public static bool Supports(WorkableObject prototype) => prototype != null
        && (prototype.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(prototype.ResolveItemId()))?.MapObjectArchetype?.RenderParts.Count > 0
        && prototype.GetComponent<BoxCollider>() != null;
    internal bool SupportsPrototype(WorkableObject prototype) => prototype != null && (templates.ContainsKey(prototype) || Supports(prototype));
    public WorkableInstance Register(WorkableObject prototype, BlockStateStore.InstallationSaveState placement)
    {
        var key = BlockStateStore.GetInstallationStorageKey(placement);
        if (byKey.TryGetValue(key, out var existing)) { Bind(existing); return existing; }
        if (!templates.TryGetValue(prototype, out var template))
        { template = new WorkableRenderTemplate(prototype); templates.Add(prototype, template); }
        if (!VirtualObjectWorld.Ensure().TryGetInstallationHandle(key, out var handle)) return null;
        var slot = states.Allocate(0);
        var instance = new WorkableInstance(this, slot.Index, slot.Generation, handle, prototype, placement, template);
        byKey.Add(key, instance); instance.OrderIndex = instances.Count; instances.Add(instance);
        var cell = Cell(instance.WorldPosition);
        if (!cells.TryGetValue(cell, out var members)) cells.Add(cell, members = new List<WorkableInstance>(8));
        members.Add(instance); Bind(instance); WorkableObject.RegisterTarget(instance);
        return instance;
    }
    public bool TryGet(Vector2Int key, out WorkableInstance instance) => byKey.TryGetValue(key, out instance);
    internal void Bind(WorkableInstance instance)
    {
        foreach (var coordinate in instance.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null) block.SetMapObject(instance);
    }
    public void Remove(Vector2Int key)
    {
        if (!byKey.Remove(key, out var instance)) return;
        WorkableObject.UnregisterTarget(instance);
        foreach (var coordinate in instance.RuntimeOccupiedCoordinates)
            if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null && ReferenceEquals(block.MapObject, instance)) block.SetMapObject(null);
        var cell = Cell(instance.WorldPosition); var members = cells[cell]; members.Remove(instance);
        if (members.Count == 0) cells.Remove(cell);
        int last = instances.Count - 1;
        instances[instance.OrderIndex] = instances[last]; instances[instance.OrderIndex].OrderIndex = instance.OrderIndex;
        instances.RemoveAt(last); view?.Unbind(instance); states.Release(instance.Index, instance.Generation);
    }
    internal void BuildCandidates(CameraRenderCulling culling, List<WorkableInstance> result)
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
    internal void BuildNearby(Vector3 position, List<WorkableInstance> result)
    {
        result.Clear(); var cell = Cell(position);
        for (int y = cell.y - 1; y <= cell.y + 1; y++)
        for (int x = cell.x - 1; x <= cell.x + 1; x++)
            if (cells.TryGetValue(new Vector2Int(x, y), out var list)) result.AddRange(list);
    }
    internal static Vector2Int Cell(Vector3 position) => new Vector2Int(Mathf.FloorToInt(position.x / 32), Mathf.FloorToInt(position.z / 32));
    public bool TryRaycast(Ray ray, float maxDistance, out WorkableInstance target, out float distance)
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

    public void ClearRecords()
    { for (int i = instances.Count - 1; i >= 0; i--) Remove(instances[i].StorageKey); templates.Clear(); cells.Clear(); }
    public void Dispose()
    { ClearRecords(); view?.Release(); view = null; if (ReferenceEquals(Current, this)) Current = null; }
    public static void AppendProfilerCounters()
    {
        MapObjectTickProfiler.AddRuntimeCounter("WorkableECS", "Entities", Current != null ? Current.Count : 0);
        MapObjectTickProfiler.AddRuntimeCounter("WorkableECS", "Visible", Current != null ? Current.VisibleCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("WorkableECS", "GameObjects", Current != null && Current.view != null ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter("WorkableECS", "RangeCandidateChecks", WorkableObject.RangeIndex.CandidateChecks);
        MapObjectTickProfiler.AddRuntimeCounter("WorkableECS", "RangeGroupBuilds", WorkableObject.RangeIndex.GroupBuilds);
    }
}
}
