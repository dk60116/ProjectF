using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    public sealed partial class RailWorld
    {
        public sealed class Record
        {
            internal (long, int, Vector2Int) Identity;
            internal int IndexedVersion;
            internal BlockStateStore.InstallationSaveState Saved;
            internal readonly HashSet<Vector2Int> Cells = new HashSet<Vector2Int>();
            internal readonly RailPathData StoredPath = new RailPathData();
            public RailPathData Path { get; internal set; }
            public Railload View { get; internal set; }
            public RailInstance Runtime { get; internal set; }
            public IRailTarget Target => Runtime ?? (IRailTarget)View;
            internal bool IsLive => Runtime != null || View != null;
            public long PlacementSequence => Identity.Item1;
            public int Order { get; internal set; }
        }

        private const float CellSize = 8f;
        private static readonly System.Comparison<Record> RecordOrderComparison = CompareOrder;
        private readonly Dictionary<(long, int, Vector2Int), Record> records =
            new Dictionary<(long, int, Vector2Int), Record>();
        private readonly Dictionary<Vector2Int, Record> storedByKey = new Dictionary<Vector2Int, Record>();
        private readonly Dictionary<Railload, Record> liveByView = new Dictionary<Railload, Record>();
        private readonly Dictionary<Vector2Int, List<Record>> spatial = new Dictionary<Vector2Int, List<Record>>();
        private readonly Dictionary<int, bool> railItemKinds = new Dictionary<int, bool>();
        private readonly List<Record> ordered = new List<Record>();
        private readonly List<Record> live = new List<Record>();
        private readonly HashSet<Record> visited = new HashSet<Record>();
        private readonly List<Record> candidates = new List<Record>();
        private readonly Dictionary<Record, int> allComponents = new Dictionary<Record, int>();
        private readonly Dictionary<Record, int> liveComponents = new Dictionary<Record, int>();
        private readonly List<int> parents = new List<int>();
        private readonly Dictionary<Record, int> indices = new Dictionary<Record, int>();
        private readonly Dictionary<int, int> componentLabels = new Dictionary<int, int>();
        private readonly Dictionary<ITrainStationTarget, StationPose> stationPoses = new Dictionary<ITrainStationTarget, StationPose>();
        private readonly List<ITrainStationTarget> stations = new List<ITrainStationTarget>();
        private readonly List<Trainstation> nativeStations = new List<Trainstation>();
        private bool orderDirty = true;
        private int allComponentsVersion = -1, liveComponentsVersion = -1;
        private float liveConnectionDistance;

        public int TopologyVersion { get; private set; }
        public int LiveTopologyVersion { get; private set; }
        public int StationRoutingVersion { get; private set; }
        public int ComponentBuilds { get; private set; }
        public long ConnectionChecks { get; private set; }
        public IReadOnlyList<Record> LiveRails { get { EnsureOrder(); return live; } }
        public IReadOnlyList<ITrainStationTarget> LiveStations => stations;

        public void UpsertLive(Railload rail)
        {
            if (rail == null || !rail.isActiveAndEnabled || !rail.TryGetPlacementRuntime(out var anchor, out _)) return;
            rail.TryGetPathData(out var path);
            var identity = Identity(rail.RuntimePlacementSequence, rail.ResolveItemId(), anchor);
            if (liveByView.TryGetValue(rail, out Record previous) && previous.Identity != identity) DetachLive(rail);
            Record record = GetOrCreate(identity);
            bool attached = record.View != rail;
            if (attached && !ReferenceEquals(record.View, null)) liveByView.Remove(record.View);
            record.View = rail;
            liveByView[rail] = record;
            bool changed = SelectPath(record, path);
            if (attached && !changed) LiveTopologyVersion++;
            orderDirty |= attached;
        }

        public void DetachLive(Railload rail)
        {
            if (ReferenceEquals(rail, null) || !liveByView.TryGetValue(rail, out Record record)) return;
            liveByView.Remove(rail);
            if (!ReferenceEquals(record.View, rail)) return;
            record.View = null;
            LiveTopologyVersion++;
            orderDirty = true;
            if (record.Saved == null) RemoveRecord(record);
            else ConfigureStoredPath(record);
        }

        public void UpsertSaved(BlockStateStore.InstallationSaveState state)
        {
            if (state == null) return;
            Vector2Int key = BlockStateStore.GetInstallationStorageKey(state);
            if (!IsRail(state)) { RemoveSaved(key); return; }
            var identity = Identity(state.placementSequence, state.itemId, state.anchorCoordinate);
            if (storedByKey.TryGetValue(key, out Record previous) && previous.Identity != identity) RemoveSaved(key);
            Record record = GetOrCreate(identity);
            record.Saved = state;
            storedByKey[key] = record;
            if (record.View == null) ConfigureStoredPath(record);
        }

        public void RemoveSaved(Vector2Int key)
        {
            RemoveRuntime(key);
            if (!storedByKey.TryGetValue(key, out Record record)) return;
            storedByKey.Remove(key);
            record.Saved = null;
            // Actual demolition removes both representations. Detaching a view never calls this.
            if (!ReferenceEquals(record.View, null)) liveByView.Remove(record.View);
            RemoveRecord(record);
        }

        public void UpsertStation(Trainstation station)
        {
            if (station == null || !station.isActiveAndEnabled || !station.TryGetPlacementRuntime(out var anchor, out var rotation)) return;
            bool found = stationPoses.TryGetValue(station, out var old);
            if (found && old.Matches(station.StoredStationName, anchor, rotation,
                station.RuntimePlacementSequence, station.RuntimeOccupiedCoordinates)) return;
            if (!found) { stations.Add(station); nativeStations.Add(station); }
            stationPoses[station] = new StationPose(station.StoredStationName, anchor, rotation,
                station.RuntimePlacementSequence, station.RuntimeOccupiedCoordinates);
            stations.Sort(CompareStations);
            StationRoutingVersion++;
        }

        public void DetachStation(Trainstation station)
        {
            if (ReferenceEquals(station, null) || !stationPoses.Remove(station)) return;
            stations.Remove(station);
            nativeStations.Remove(station);
            StationRoutingVersion++;
        }

        public void CollectCandidates(Record record, float distance, List<Record> result, bool liveOnly)
        {
            result.Clear();
            visited.Clear();
            int radius = Mathf.CeilToInt(Mathf.Max(0f, distance) / CellSize);
            foreach (Vector2Int cell in record.Cells)
                CollectCellRange(cell.x - radius, cell.y - radius, cell.x + radius, cell.y + radius, result, liveOnly);
            result.Sort(RecordOrderComparison);
        }

        public int FindComponentAtCoordinate(Vector2Int coordinate)
        {
            EnsureComponents(false, RailConnectionUtility.ConnectionDistance);
            CollectAtPoint(coordinate, 0f, candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                var occupied = candidates[i].Path.Coordinates;
                for (int j = 0; j < occupied.Count; j++)
                    if (occupied[j] == coordinate && allComponents.TryGetValue(candidates[i], out int component)) return component;
            }
            return FindComponentAtPoint(coordinate);
        }

        public bool CoordinateExists(Vector2Int coordinate, bool liveOnly = false)
        {
            EnsureOrder();
            CollectAtPoint(coordinate, 0f, candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (liveOnly && !candidates[i].IsLive) continue;
                var occupied = candidates[i].Path.Coordinates;
                for (int j = 0; j < occupied.Count; j++) if (occupied[j] == coordinate) return true;
            }
            return false;
        }

        public int FindComponentAtPoint(Vector2 point)
        {
            EnsureComponents(false, RailConnectionUtility.ConnectionDistance);
            const float snap = 0.75f;
            CollectAtPoint(point, snap, candidates);
            float best = snap * snap;
            int result = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                RailPathData path = candidates[i].Path;
                if (RailConnectionUtility.TryFindNearestPointOnConnectionPath(path.Coordinates, path.SourcePoints, point, out float distance)
                    && distance < best && allComponents.TryGetValue(candidates[i], out int component))
                { best = distance; result = component; }
            }
            return result;
        }

        public int GetLiveComponent(IRailTarget rail, float distance)
        {
            EnsureComponents(true, distance);
            Record record = rail is RailInstance data && storedByKey.TryGetValue(data.StorageKey, out var stored) ? stored :
                rail is Railload native && liveByView.TryGetValue(native, out var nativeRecord) ? nativeRecord : null;
            return record != null
                && liveComponents.TryGetValue(record, out int component) ? component : -1;
        }

        public void Clear()
        {
            ClearRuntime();
            records.Clear(); storedByKey.Clear(); liveByView.Clear(); spatial.Clear();
            ordered.Clear(); live.Clear(); visited.Clear(); candidates.Clear();
            allComponents.Clear(); liveComponents.Clear(); stationPoses.Clear(); stations.Clear(); nativeStations.Clear();
            railItemKinds.Clear();
            orderDirty = true;
            TopologyVersion++; LiveTopologyVersion++; StationRoutingVersion++;
        }

        private static (long, int, Vector2Int) Identity(long sequence, int item, Vector2Int anchor)
            => (sequence, item, sequence > 0 ? default : anchor);

        private Record GetOrCreate((long, int, Vector2Int) identity)
        {
            if (!records.TryGetValue(identity, out Record record))
            {
                record = new Record { Identity = identity };
                records.Add(identity, record);
                orderDirty = true;
            }
            return record;
        }

        private bool IsRail(BlockStateStore.InstallationSaveState state)
        {
            if (state == null) return false;
            if (state.railVisualPathPoints?.Count >= 2 || state.railRequiredItemCount > 0) return true;
            if (railItemKinds.TryGetValue(state.itemId, out bool result)) return result;
            var definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
            if (definitions == null) return false;
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null || definition.id != state.itemId) continue;
                result = definition.mapObject != null && (definition.mapObject is Railload
                    || definition.mapObject.GetComponent<Railload>() != null
                    || definition.mapObject.GetComponentInChildren<Railload>(true) != null);
                railItemKinds[state.itemId] = result;
                return result;
            }
            return false;
        }

        private void ConfigureStoredPath(Record record)
        {
            var state = record.Saved;
            record.StoredPath.Configure(state.railVisualPathPoints, state.occupiedCoordinates,
                state.anchorCoordinate, state.railVisualPathExtendsStart, state.railVisualPathExtendsEnd);
            // A streamed-out view can leave an equivalent path; preserve topology caches.
            if (record.Path != null && !ReferenceEquals(record.Path, record.StoredPath)
                && record.IndexedVersion == record.Path.Version
                && record.Path.Matches(state.railVisualPathPoints, state.occupiedCoordinates,
                state.anchorCoordinate, state.railVisualPathExtendsStart, state.railVisualPathExtendsEnd))
            { record.Path = record.StoredPath; record.IndexedVersion = record.Path.Version; return; }
            SelectPath(record, record.StoredPath);
        }

        private bool SelectPath(Record record, RailPathData path)
        {
            if (ReferenceEquals(record.Path, path) && record.IndexedVersion == path.Version) return false;
            if (record.Path != null && record.IndexedVersion == record.Path.Version && record.Path.ContentEquals(path))
            {
                record.Path = path;
                record.IndexedVersion = path.Version;
                return false;
            }
            RemoveSpatial(record);
            record.Path = path;
            record.IndexedVersion = path.Version;
            IndexPoints(record, path.SourcePoints);
            IndexPoints(record, path.Samples);
            for (int i = 0; i < path.Coordinates.Count; i++) AddCell(record, Cell(path.Coordinates[i]));
            foreach (Vector2Int cell in record.Cells)
            {
                if (!spatial.TryGetValue(cell, out List<Record> bucket)) spatial[cell] = bucket = new List<Record>();
                bucket.Add(record);
            }
            TopologyVersion++;
            if (record.IsLive) LiveTopologyVersion++;
            orderDirty = true;
            return true;
        }

        private void IndexPoints(Record record, IReadOnlyList<Vector2> points)
        {
            if (points.Count > 0) AddCell(record, Cell(points[0]));
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 start = points[i - 1], delta = points[i] - start;
                Vector2Int current = Cell(start), end = Cell(points[i]);
                int stepX = delta.x > 0f ? 1 : -1, stepY = delta.y > 0f ? 1 : -1;
                float incrementX = delta.x == 0f ? float.PositiveInfinity : CellSize / Mathf.Abs(delta.x);
                float incrementY = delta.y == 0f ? float.PositiveInfinity : CellSize / Mathf.Abs(delta.y);
                float nextX = delta.x == 0f ? float.PositiveInfinity
                    : ((current.x + (stepX > 0 ? 1 : 0)) * CellSize - start.x) / delta.x;
                float nextY = delta.y == 0f ? float.PositiveInfinity
                    : ((current.y + (stepY > 0 ? 1 : 0)) * CellSize - start.y) / delta.y;
                // Traverse the segment, not its rectangular area. Corner cells are
                // included so endpoint queries remain conservative on grid borders.
                while (current != end)
                {
                    if (current.y == end.y || current.x != end.x && nextX < nextY)
                    { current.x += stepX; nextX += incrementX; }
                    else if (current.x == end.x || nextY < nextX)
                    { current.y += stepY; nextY += incrementY; }
                    else
                    {
                        AddCell(record, new Vector2Int(current.x + stepX, current.y));
                        AddCell(record, new Vector2Int(current.x, current.y + stepY));
                        current.x += stepX; current.y += stepY;
                        nextX += incrementX; nextY += incrementY;
                    }
                    AddCell(record, current);
                }
            }
        }

        private static Vector2Int Cell(Vector2 point)
            => new Vector2Int(Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.y / CellSize));
        private static void AddCell(Record record, Vector2Int cell) => record.Cells.Add(cell);

        private void RemoveSpatial(Record record)
        {
            foreach (Vector2Int cell in record.Cells)
                if (spatial.TryGetValue(cell, out List<Record> bucket))
                { bucket.Remove(record); if (bucket.Count == 0) spatial.Remove(cell); }
            record.Cells.Clear();
        }

        private void RemoveRecord(Record record)
        {
            RemoveSpatial(record);
            records.Remove(record.Identity);
            TopologyVersion++;
            if (record.IsLive) LiveTopologyVersion++;
            orderDirty = true;
        }

        private void EnsureOrder()
        {
            if (!orderDirty) return;
            ordered.Clear(); live.Clear();
            foreach (Record record in records.Values) if (record.Path != null && record.Path.IsValid) ordered.Add(record);
            ordered.Sort(CompareRecords);
            for (int i = 0; i < ordered.Count; i++)
            { ordered[i].Order = i; if (ordered[i].IsLive) live.Add(ordered[i]); }
            orderDirty = false;
        }

        private static int CompareRecords(Record a, Record b)
        {
            if (a.View != null && b.View != null) return InstallationObject.CompareSimulationOrder(a.View, b.View);
            if (a.View != null) return -1;
            if (b.View != null) return 1;
            int result = a.Identity.Item1.CompareTo(b.Identity.Item1);
            if (result == 0) result = a.Identity.Item2.CompareTo(b.Identity.Item2);
            if (result == 0) result = a.Identity.Item3.x.CompareTo(b.Identity.Item3.x);
            return result != 0 ? result : a.Identity.Item3.y.CompareTo(b.Identity.Item3.y);
        }
        private static int CompareOrder(Record a, Record b) => a.Order.CompareTo(b.Order);

        private void CollectAtPoint(Vector2 point, float radius, List<Record> result)
        {
            result.Clear(); visited.Clear();
            Vector2Int min = Cell(point - Vector2.one * radius), max = Cell(point + Vector2.one * radius);
            CollectCellRange(min.x, min.y, max.x, max.y, result, false);
            result.Sort(RecordOrderComparison);
        }

        private void CollectCellRange(int minX, int minY, int maxX, int maxY, List<Record> result, bool liveOnly)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    if (spatial.TryGetValue(new Vector2Int(x, y), out List<Record> bucket))
                        for (int i = 0; i < bucket.Count; i++)
                            if (bucket[i].Path.IsValid && (!liveOnly || bucket[i].IsLive)
                                && visited.Add(bucket[i])) result.Add(bucket[i]);
        }

        private void EnsureComponents(bool liveOnly, float distance)
        {
            int version = liveOnly ? LiveTopologyVersion : TopologyVersion;
            if (liveOnly ? liveComponentsVersion == version && liveConnectionDistance == distance : allComponentsVersion == version) return;
            EnsureOrder();
            var source = liveOnly ? live : ordered;
            var components = liveOnly ? liveComponents : allComponents;
            components.Clear(); indices.Clear(); parents.Clear(); componentLabels.Clear();
            for (int i = 0; i < source.Count; i++) { indices[source[i]] = i; parents.Add(i); }
            float sqrDistance = distance * distance;
            for (int i = 0; i < source.Count; i++)
            {
                Record left = source[i];
                CollectCandidates(left, distance, candidates, liveOnly);
                for (int j = 0; j < candidates.Count; j++)
                {
                    Record right = candidates[j];
                    if (!indices.TryGetValue(right, out int other) || other <= i) continue;
                    ConnectionChecks++;
                    if (RailConnectionUtility.AreConnected(left.Path.Coordinates, left.Path.SourcePoints,
                        left.Path.ConnectionStart, left.Path.ConnectionEnd, right.Path.Coordinates, right.Path.SourcePoints,
                        right.Path.ConnectionStart, right.Path.ConnectionEnd, sqrDistance)) parents[Root(other)] = Root(i);
                }
            }
            for (int i = 0; i < source.Count; i++)
            {
                int root = Root(i);
                if (!componentLabels.TryGetValue(root, out int component)) componentLabels[root] = component = componentLabels.Count;
                components[source[i]] = component;
            }
            ComponentBuilds++;
            if (liveOnly) { liveComponentsVersion = version; liveConnectionDistance = distance; }
            else allComponentsVersion = version;
        }

        private int Root(int index)
        {
            while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
            return index;
        }

        private sealed class StationPose
        {
            private readonly string name;
            private readonly Vector2Int anchor;
            private readonly int rotation;
            private readonly long sequence;
            private readonly List<Vector2Int> footprint = new List<Vector2Int>();
            public StationPose(string name, Vector2Int anchor, int rotation, long sequence, IReadOnlyList<Vector2Int> coordinates)
            {
                this.name = name; this.anchor = anchor; this.rotation = rotation; this.sequence = sequence;
                if (coordinates != null)
                    for (int i = 0; i < coordinates.Count; i++) footprint.Add(coordinates[i]);
            }
            public bool Matches(string otherName, Vector2Int otherAnchor, int otherRotation, long otherSequence,
                IReadOnlyList<Vector2Int> coordinates)
            {
                if (name != otherName || anchor != otherAnchor || rotation != otherRotation || sequence != otherSequence
                    || footprint.Count != (coordinates?.Count ?? 0)) return false;
                for (int i = 0; i < footprint.Count; i++) if (footprint[i] != coordinates[i]) return false;
                return true;
            }
        }
    }
}
