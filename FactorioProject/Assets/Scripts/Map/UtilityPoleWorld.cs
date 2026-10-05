using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Rendering;

namespace ProjectF.Power
{
    public sealed class UtilityPoleWorld : IDisposable
    {
        public static UtilityPoleWorld Current { get; private set; }
        public TerrainGenerator Terrain { get; }
        internal BlockStateStore Store { get; }
        private readonly Dictionary<Vector2Int, UtilityPoleRuntime> byKey = new Dictionary<Vector2Int, UtilityPoleRuntime>();
        private readonly Dictionary<Vector2Int, List<UtilityPoleRuntime>> cells = new Dictionary<Vector2Int, List<UtilityPoleRuntime>>();
        private readonly Dictionary<global::UtilityPole, UtilityPoleRenderTemplate> templates = new Dictionary<global::UtilityPole, UtilityPoleRenderTemplate>();
        private readonly List<UtilityPoleRuntime> instances = new List<UtilityPoleRuntime>();
        private UtilityPoleWorldView view;
        public IReadOnlyList<UtilityPoleRuntime> Instances => instances;
        public int Count => instances.Count;
        public int VisibleCount => view != null ? view.VisibleCount : 0;
        public static UtilityPoleWorld Ensure(TerrainGenerator terrain)
        {
            if (Current != null && Current.Terrain == terrain) return Current;
            Current?.Dispose(); Current = new UtilityPoleWorld(terrain); return Current;
        }
        private UtilityPoleWorld(TerrainGenerator terrain)
        { Terrain = terrain; Store = terrain.GetComponent<BlockStateStore>(); view = UtilityPoleWorldView.Create(this, terrain.transform); }
        public static bool Supports(global::UtilityPole prototype) => prototype != null
            && (prototype.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(prototype.ResolveItemId()))?.MapObjectArchetype?.RenderParts.Count > 0
            && prototype.GetComponent<SphereCollider>() != null;
        public bool TryGet(Vector2Int key, out UtilityPoleRuntime pole) => byKey.TryGetValue(key, out pole);
        public UtilityPoleRuntime Register(global::UtilityPole prototype, BlockStateStore.InstallationSaveState placement)
        {
            Vector2Int key = BlockStateStore.GetInstallationStorageKey(placement);
            if (byKey.TryGetValue(key, out var existing)) { Bind(existing); return existing; }
            if (!templates.TryGetValue(prototype, out var template))
            { template = new UtilityPoleRenderTemplate(prototype); templates.Add(prototype, template); }
            if (!VirtualObjectWorld.Ensure().TryGetInstallationHandle(key, out var handle)) return null;
            var pole = new UtilityPoleRuntime(this, prototype, placement, handle, template) { Registered = true };
            byKey.Add(key, pole); pole.OrderIndex = instances.Count; instances.Add(pole);
            Vector2Int cell = Cell(pole.WorldPosition);
            if (!cells.TryGetValue(cell, out var list)) cells.Add(cell, list = new List<UtilityPoleRuntime>(8));
            list.Add(pole); Bind(pole); pole.Activate(); return pole;
        }
        internal void Bind(UtilityPoleRuntime pole)
        {
            foreach (var coordinate in pole.RuntimeOccupiedCoordinates)
                if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null) block.SetMapObject(pole);
        }
        public void Remove(Vector2Int key)
        {
            if (!byKey.Remove(key, out var pole)) return;
            pole.Registered = false; pole.Deactivate(); view?.Unbind(pole);
            foreach (var coordinate in pole.RuntimeOccupiedCoordinates)
                if (Terrain.TryGetLoadedBlock(coordinate, out var block) && block != null && ReferenceEquals(block.MapObject, pole)) block.SetMapObject(null);
            var cell = Cell(pole.WorldPosition); var list = cells[cell]; list.Remove(pole); if (list.Count == 0) cells.Remove(cell);
            int last = instances.Count - 1;
            instances[pole.OrderIndex] = instances[last]; instances[pole.OrderIndex].OrderIndex = pole.OrderIndex;
            instances.RemoveAt(last);
        }
        internal void BuildCandidates(CameraRenderCulling culling, List<UtilityPoleRuntime> result)
        {
            result.Clear();
            if (!culling.TryGetVisibleCellRange(32, 2, out var min, out var max)) { result.AddRange(instances); return; }
            if (CameraRenderCulling.GetCellCount(min, max) > cells.Count * 2L)
            {
                foreach (var pair in cells) if (pair.Key.x >= min.x && pair.Key.x <= max.x && pair.Key.y >= min.y && pair.Key.y <= max.y) result.AddRange(pair.Value);
                return;
            }
            for (int y = min.y; y <= max.y; y++) for (int x = min.x; x <= max.x; x++)
                if (cells.TryGetValue(new Vector2Int(x, y), out var list)) result.AddRange(list);
        }
        internal void BuildNearby(Vector3 position, List<UtilityPoleRuntime> result)
        {
            result.Clear(); var cell = Cell(position);
            for (int y = cell.y - 1; y <= cell.y + 1; y++) for (int x = cell.x - 1; x <= cell.x + 1; x++)
                if (cells.TryGetValue(new Vector2Int(x, y), out var list)) result.AddRange(list);
        }
        private static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 32), Mathf.FloorToInt(p.z / 32));
        public bool TryRaycast(Ray ray, float maxDistance, out UtilityPoleRuntime target, out float distance)
        {
            target = null; distance = maxDistance;
            if (maxDistance <= 0 || cells.Count == 0) return false;
            var traversal = new SpatialRayCellTraversal(ray, maxDistance, 32);
            while (traversal.MoveNext())
            {
                Vector2Int cell = traversal.Current;
                for (int y = cell.y - 1; y <= cell.y + 1; y++) for (int x = cell.x - 1; x <= cell.x + 1; x++)
                    if (cells.TryGetValue(new Vector2Int(x,y), out var list))
                        for (int i = 0; i < list.Count; i++) if (list[i].IsRuntimeActive && list[i].CullBounds.IntersectRay(ray, out float d) && d >= 0 && d < distance)
                        { target = list[i]; distance = d; }
            }
            return target != null;
        }
        public void FlushSaveStates()
        {
            if (Store != null && !Store.CanCaptureUtilityPoleTopology) return;
            for (int i = 0; i < instances.Count; i++) instances[i].Persist(topologyChecked: true);
        }
        public void ClearRecords()
        {
            UtilityPoleRuntime.BeginTopologyRefreshBatch();
            try { for (int i = instances.Count - 1; i >= 0; i--) Remove(instances[i].StorageKey); }
            finally { UtilityPoleRuntime.EndTopologyRefreshBatch(); }
            cells.Clear(); templates.Clear();
        }
        public void Dispose() { ClearRecords(); view?.Release(); view = null; if (ReferenceEquals(Current,this)) Current = null; }
        internal static void AppendProfilerCounters()
        {
            MapObjectTickProfiler.AddRuntimeCounter("UtilityPoleECS", "Entities", Current != null ? Current.Count : 0);
            MapObjectTickProfiler.AddRuntimeCounter("UtilityPoleECS", "Visible", Current != null ? Current.VisibleCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("UtilityPoleECS", "BodyGameObjects", Current != null && Current.view != null ? 1 : 0);
        }
    }
}
