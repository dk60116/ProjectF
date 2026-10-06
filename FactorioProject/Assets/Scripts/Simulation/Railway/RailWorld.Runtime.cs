using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    public sealed partial class RailWorld
    {
        private readonly Dictionary<Vector2Int, RailwayInstance> runtimeByKey = new Dictionary<Vector2Int, RailwayInstance>();
        private readonly Dictionary<Vector2Int, List<RailwayInstance>> runtimeByCoordinate = new Dictionary<Vector2Int, List<RailwayInstance>>();
        private RailWorldView presentation;
        private readonly HashSet<RailwayInstance> rayCandidates = new HashSet<RailwayInstance>();
        public bool TryRaycast(Ray ray, float maxDistance, out RailwayInstance result, out float distance)
        {
            result = null; distance = maxDistance; rayCandidates.Clear();
            if (runtimeByKey.Count == 0 || Mathf.Abs(ray.direction.y) < .0001f) return false;
            var terrain = TerrainGenerator.Active;
            float y = terrain != null ? terrain.transform.position.y : 0;
            float start = 0, end = maxDistance;
            if (Mathf.Abs(ray.direction.y) > .0001f)
            {
                float low = (y - ray.origin.y) / ray.direction.y, high = (y + 8 - ray.origin.y) / ray.direction.y;
                start = Mathf.Clamp(Mathf.Min(low, high), 0, maxDistance); end = Mathf.Clamp(Mathf.Max(low, high), 0, maxDistance);
            }
            var a = ray.GetPoint(start); var b = ray.GetPoint(end);
            int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x)) - 2, maxX = Mathf.CeilToInt(Mathf.Max(a.x, b.x)) + 2;
            int minZ = Mathf.FloorToInt(Mathf.Min(a.z, b.z)) - 2, maxZ = Mathf.CeilToInt(Mathf.Max(a.z, b.z)) + 2;
            for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
                if (runtimeByCoordinate.TryGetValue(new Vector2Int(x, z), out var bucket))
                    for (int i = 0; i < bucket.Count; i++) rayCandidates.Add(bucket[i]);
            foreach (var candidate in rayCandidates)
            {
                if (candidate is RailInstance rail)
                {
                    float hit = (rail.WorldPosition.y + .1f - ray.origin.y) / ray.direction.y;
                    var point = ray.GetPoint(hit);
                    if (hit >= 0 && hit < distance && rail.TryFindNearestRenderedPathSample(new Vector2(point.x, point.z), out _, out _, out _, out float sqr) && sqr <= .45f * .45f)
                    { result = rail; distance = hit; }
                }
                else if (candidate.FocusBounds.IntersectRay(ray, out float hit) && hit >= 0 && hit < distance)
                { result = candidate; distance = hit; }
            }
            return result != null;
        }
        public bool Contains(RailwayInstance instance) => instance != null && runtimeByKey.TryGetValue(instance.StorageKey, out var current) && ReferenceEquals(instance, current);
        internal bool TryGetRuntime(Vector2Int key, out RailwayInstance instance) => runtimeByKey.TryGetValue(key, out instance);
        internal void NotifyPresentationChanged(RailwayInstance instance) { if (Contains(instance)) presentation?.Invalidate(instance); }
        internal RailwayInstance RegisterRuntime(BlockStateStore.InstallationSaveState state, InstallationObject prototype, TerrainGenerator terrain)
        {
            var key = BlockStateStore.GetInstallationStorageKey(state);
            if (runtimeByKey.TryGetValue(key, out var existing)) return existing;
            RailwayInstance instance;
            if (prototype is Railload rail)
            {
                UpsertSaved(state);
                if (!storedByKey.TryGetValue(key, out var record)) return null;
                instance = record.Runtime = new RailInstance(this, state, rail, record.StoredPath);
                LiveTopologyVersion++; orderDirty = true;
            }
            else if (prototype is Trainstation station)
            {
                instance = new TrainStationInstance(this, state, station);
                stations.Add((ITrainStationTarget)instance);
                stations.Sort(CompareStations);
                StationRoutingVersion++;
            }
            else return null;
            runtimeByKey.Add(key, instance);
            var coordinates = instance.RuntimeOccupiedCoordinates;
            for (int i = 0; i < coordinates.Count; i++)
            {
                if (!runtimeByCoordinate.TryGetValue(coordinates[i], out var bucket)) runtimeByCoordinate.Add(coordinates[i], bucket = new List<RailwayInstance>());
                bucket.Add(instance);
            }
            if (presentation == null) presentation = RailWorldView.Create(this, terrain);
            presentation.Add(instance);
            return instance;
        }
        private static int CompareStations(ITrainStationTarget a, ITrainStationTarget b) => a.RuntimePlacementSequence.CompareTo(b.RuntimePlacementSequence);
        internal void NotifyStationChanged(TrainStationInstance station, bool routeChanged = true)
        {
            if (!Contains(station)) return;
            if (routeChanged) StationRoutingVersion++;
            TerrainGenerator.Active?.NotifyTrainStationMapChanged();
        }
        internal void RemoveRuntime(Vector2Int key)
        {
            if (!runtimeByKey.TryGetValue(key, out var instance)) return;
            runtimeByKey.Remove(key);
            var coordinates = instance.RuntimeOccupiedCoordinates;
            for (int i = 0; i < coordinates.Count; i++)
                if (runtimeByCoordinate.TryGetValue(coordinates[i], out var bucket))
                { bucket.Remove(instance); if (bucket.Count == 0) runtimeByCoordinate.Remove(coordinates[i]); }
            if (instance is TrainStationInstance station) { stations.Remove(station); StationRoutingVersion++; }
            if (instance is RailInstance && storedByKey.TryGetValue(key, out var record))
            { record.Runtime = null; LiveTopologyVersion++; orderDirty = true; }
            presentation?.Remove(instance);
            var terrain = TerrainGenerator.Active;
            for (int i = 0; i < coordinates.Count; i++)
                if (terrain != null && terrain.TryGetLoadedBlock(coordinates[i], out var block) && block != null && ReferenceEquals(block.MapObject, instance))
                    block.SetMapObject(TryGetAtCoordinate(coordinates[i], out var remaining) ? remaining : null);
        }
        private void ClearRuntime()
        { runtimeByKey.Clear(); runtimeByCoordinate.Clear(); rayCandidates.Clear(); presentation?.Release(); presentation = null; }
        public bool TryGetAtCoordinate(Vector2Int coordinate, out RailwayInstance result)
        {
            result = null;
            if (!runtimeByCoordinate.TryGetValue(coordinate, out var bucket)) return false;
            for (int i = 0; i < bucket.Count; i++)
                if (result == null || bucket[i].SimulationId > result.SimulationId) result = bucket[i];
            return result != null;
        }
        public void CollectRails(Vector2 point, float radius, List<IRailTarget> results)
        {
            EnsureOrder(); CollectAtPoint(point, radius, candidates);
            for (int i = 0; i < candidates.Count; i++)
            { var target = candidates[i].Target; if (target.IsAlive() && !results.Contains(target)) results.Add(target); }
        }
        public void CollectRailsAtCoordinate(Vector2Int coordinate, List<IRailTarget> results)
        {
            results.Clear(); EnsureOrder(); CollectAtPoint(coordinate, 0, candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                var record = candidates[i];
                if (!record.Target.IsAlive()) continue;
                var occupied = record.Path.Coordinates;
                for (int j = 0; j < occupied.Count; j++)
                    if (occupied[j] == coordinate) { results.Add(record.Target); break; }
            }
        }
        public void CollectStations(Vector2 point, float radius, List<ITrainStationTarget> results)
        {
            results.Clear(); int cells = Mathf.CeilToInt(radius);
            var center = new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
            for (int x = -cells; x <= cells; x++) for (int y = -cells; y <= cells; y++)
                if (runtimeByCoordinate.TryGetValue(center + new Vector2Int(x, y), out var bucket))
                    for (int i = 0; i < bucket.Count; i++)
                        if (bucket[i] is ITrainStationTarget station && !results.Contains(station)) results.Add(station);
            // Scene-authored stations remain supported without scene-wide searches.
            for (int i = 0; i < nativeStations.Count; i++)
            {
                var station = nativeStations[i];
                var coordinates = station.RuntimeOccupiedCoordinates;
                for (int j = 0; j < coordinates.Count; j++)
                    if (Mathf.Abs(coordinates[j].x - center.x) <= cells && Mathf.Abs(coordinates[j].y - center.y) <= cells)
                    { if (!results.Contains(station)) results.Add(station); break; }
            }
        }
    }
}
