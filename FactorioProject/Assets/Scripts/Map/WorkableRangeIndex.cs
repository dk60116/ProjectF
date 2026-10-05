using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
    // Cached components are invalidated only when a touching range changes. Bulk placement
    // does not build components; the first access builds each required component once.
    public sealed class WorkableRangeIndex
    {
        private const float CellSize = 8f, EdgeEpsilon = 0.001f;
        private sealed class Entry
        {
            internal IWorkableTarget Target;
            internal Bounds Bounds;
            internal int ItemId, Order;
            internal List<Entry> Group;
            internal int Visit;
        }
        private readonly Dictionary<IWorkableTarget, Entry> entries = new Dictionary<IWorkableTarget, Entry>();
        private readonly Dictionary<Vector2Int, List<Entry>> cells = new Dictionary<Vector2Int, List<Entry>>();
        private readonly List<IWorkableTarget> targets = new List<IWorkableTarget>();
        private readonly List<Entry> queue = new List<Entry>();
        private readonly HashSet<List<Entry>> groups = new HashSet<List<Entry>>();
        private readonly List<IWorkableTarget> cachedResults = new List<IWorkableTarget>();
        private Vector3 cachedPosition;
        private long cachedVersion = -1;
        private int visit;
        public long Version { get; private set; }
        public long CandidateChecks { get; private set; }
        public long GroupBuilds { get; private set; }
        public IReadOnlyList<IWorkableTarget> Targets => targets;

        public static bool Contains(Bounds bounds, Vector3 position) => position.x >= bounds.min.x
            && position.x <= bounds.max.x && position.z >= bounds.min.z && position.z <= bounds.max.z;
        private static Vector2Int Cell(float x, float z) => new Vector2Int(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));
        private static bool Connected(Entry a, Entry b)
        {
            if (a.ItemId < 0 || a.ItemId != b.ItemId) return false;
            float x = Mathf.Min(a.Bounds.max.x, b.Bounds.max.x) - Mathf.Max(a.Bounds.min.x, b.Bounds.min.x);
            float z = Mathf.Min(a.Bounds.max.z, b.Bounds.max.z) - Mathf.Max(a.Bounds.min.z, b.Bounds.min.z);
            return x >= -EdgeEpsilon && z >= -EdgeEpsilon && (x > EdgeEpsilon || z > EdgeEpsilon);
        }
        private static void Invalidate(Entry entry)
        {
            var group = entry.Group;
            if (group == null) return;
            for (int i = 0; i < group.Count; i++) group[i].Group = null;
        }
        public void Update(IWorkableTarget target)
        {
            if (target == null) return;
            if (!target.IsTargetActive || !target.TryGetWorkableRangeBounds(out var bounds)) { Remove(target); return; }
            int itemId = target.ResolveItemId();
            if (entries.TryGetValue(target, out var old))
            {
                if (old.Bounds == bounds && old.ItemId == itemId) return;
                Remove(target);
            }
            var entry = new Entry { Target = target, Bounds = bounds, ItemId = itemId, Order = targets.Count };
            // Any cached group joined by this new range must be rebuilt, including merges.
            var min = Cell(bounds.min.x - EdgeEpsilon, bounds.min.z - EdgeEpsilon);
            var max = Cell(bounds.max.x + EdgeEpsilon, bounds.max.z + EdgeEpsilon);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
                if (cells.TryGetValue(new Vector2Int(x, y), out var members))
                    for (int i = 0; i < members.Count; i++)
                        if (Connected(entry, members[i])) Invalidate(members[i]);
            entries.Add(target, entry); targets.Add(target); Version++;
            min = Cell(bounds.min.x, bounds.min.z); max = Cell(bounds.max.x, bounds.max.z);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!cells.TryGetValue(cell, out var members)) cells.Add(cell, members = new List<Entry>(4));
                members.Add(entry);
            }
        }
        public void Remove(IWorkableTarget target)
        {
            if (target == null || !entries.Remove(target, out var entry)) return;
            Invalidate(entry); Version++;
            var min = Cell(entry.Bounds.min.x, entry.Bounds.min.z); var max = Cell(entry.Bounds.max.x, entry.Bounds.max.z);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!cells.TryGetValue(cell, out var members)) continue;
                members.Remove(entry); if (members.Count == 0) cells.Remove(cell);
            }
            int last = targets.Count - 1;
            var moved = targets[last]; targets[entry.Order] = moved;
            if (entry.Order != last) entries[moved].Order = entry.Order;
            targets.RemoveAt(last);
            cachedResults.Clear(); groups.Clear(); queue.Clear();
        }
        private List<Entry> GetGroup(Entry root)
        {
            if (root.Group != null) return root.Group;
            if (visit == int.MaxValue) { foreach (var entry in entries.Values) entry.Visit = 0; visit = 0; }
            int stamp = ++visit; queue.Clear(); queue.Add(root); root.Visit = stamp; GroupBuilds++;
            for (int cursor = 0; cursor < queue.Count; cursor++)
            {
                var current = queue[cursor];
                var min = Cell(current.Bounds.min.x - EdgeEpsilon, current.Bounds.min.z - EdgeEpsilon);
                var max = Cell(current.Bounds.max.x + EdgeEpsilon, current.Bounds.max.z + EdgeEpsilon);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                    if (cells.TryGetValue(new Vector2Int(x, y), out var members))
                        for (int i = 0; i < members.Count; i++)
                        {
                            var candidate = members[i]; CandidateChecks++;
                            if (candidate.Visit == stamp || !Connected(current, candidate)) continue;
                            candidate.Visit = stamp; queue.Add(candidate);
                        }
            }
            var group = new List<Entry>(queue);
            group.Sort(CompareEntries);
            for (int i = 0; i < group.Count; i++) group[i].Group = group;
            queue.Clear(); return group;
        }
        private static int CompareEntries(Entry a, Entry b) => CompareTargets(a.Target, b.Target);
        private static int CompareTargets(IWorkableTarget a, IWorkableTarget b)
        {
            int order = a.WorkablePlacementSequence.CompareTo(b.WorkablePlacementSequence);
            if (order != 0) return order;
            order = a.AnchorCoordinate.x.CompareTo(b.AnchorCoordinate.x);
            return order != 0 ? order : a.AnchorCoordinate.y.CompareTo(b.AnchorCoordinate.y);
        }
        public void CollectOwn(Vector3 position, List<IWorkableTarget> result)
        {
            result.Clear();
            if (!cells.TryGetValue(Cell(position.x, position.z), out var members)) return;
            for (int i = 0; i < members.Count; i++)
            {
                CandidateChecks++;
                if (members[i].Target.IsTargetActive && Contains(members[i].Bounds, position)) result.Add(members[i].Target);
            }
            result.Sort(CompareTargets);
        }
        public void CollectContaining(Vector3 position, List<IWorkableTarget> result)
        {
            if (cachedVersion == Version && cachedPosition.Equals(position))
            { result.Clear(); result.AddRange(cachedResults); return; }
            groups.Clear(); cachedResults.Clear();
            if (cells.TryGetValue(Cell(position.x, position.z), out var members))
                for (int i = 0; i < members.Count; i++)
                {
                    var entry = members[i]; CandidateChecks++;
                    if (!entry.Target.IsTargetActive || !Contains(entry.Bounds, position)) continue;
                    var group = GetGroup(entry);
                    if (!groups.Add(group)) continue;
                    for (int n = 0; n < group.Count; n++) if (group[n].Target.IsTargetActive) cachedResults.Add(group[n].Target);
                }
            cachedResults.Sort(CompareTargets); cachedPosition = position; cachedVersion = Version;
            result.Clear(); result.AddRange(cachedResults); groups.Clear();
        }
        public bool ContainsConnected(IWorkableTarget target, Vector3 position)
        {
            if (target == null || !target.IsTargetActive || !entries.TryGetValue(target, out var root)
                || !cells.TryGetValue(Cell(position.x, position.z), out var members)) return false;
            var group = GetGroup(root);
            for (int i = 0; i < members.Count; i++)
            {
                CandidateChecks++;
                var entry = members[i];
                if (entry.Target.IsTargetActive && Contains(entry.Bounds, position) && ReferenceEquals(entry.Group, group)) return true;
            }
            return false;
        }
        public void CollectConnected(IWorkableTarget target, List<IWorkableTarget> result)
        {
            result.Clear();
            if (target == null || !entries.TryGetValue(target, out var entry)) return;
            var group = GetGroup(entry);
            for (int i = 0; i < group.Count; i++) if (group[i].Target.IsTargetActive) result.Add(group[i].Target);
        }
    }
}
