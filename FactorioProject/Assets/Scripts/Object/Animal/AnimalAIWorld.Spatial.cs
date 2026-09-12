using System.Collections.Generic;
using ProjectF.Animals;
using UnityEngine;

public sealed partial class AnimalAIWorld
{
    private struct SpatialEntry
    {
        public Vector2Int cell;
        public Vector3 position;
        public long herd;
        public long identity;
        public float radius;
        public bool fleeing;
    }

    private sealed class ControllerComparer : IComparer<AnimalAIController>
    {
        public static readonly ControllerComparer Instance = new ControllerComparer();
        public int Compare(AnimalAIController x, AnimalAIController y) => CompareControllers(x, y);
    }

    private readonly Dictionary<AnimalAIController, SpatialEntry> spatialEntries =
        new Dictionary<AnimalAIController, SpatialEntry>();
    private readonly HashSet<AnimalAIController> spatialDirtySet = new HashSet<AnimalAIController>();
    private readonly List<AnimalAIController> spatialDirty = new List<AnimalAIController>();
    private readonly HashSet<long> dirtyHerds = new HashSet<long>();
    private bool maximumRadiusDirty;

    internal static void NotifySpatialChanged(AnimalAIController controller)
    {
        if (!ReferenceEquals(controller, null) && Instance != null)
            Instance.MarkSpatialDirty(controller);
    }

    private void MarkSpatialDirty(AnimalAIController controller)
    {
        if (spatialDirtySet.Add(controller)) spatialDirty.Add(controller);
    }

    private void RefreshSpatialCaches()
    {
        using var sample = AnimalAIProfiler.Sample("Animal Spatial Cache");
        // Publish together before any AI runs. Every animal reads the same tick snapshot.
        spatialDirty.Sort(ControllerComparer.Instance);
        for (int i = 0; i < spatialDirty.Count; i++) RefreshSpatialEntry(spatialDirty[i]);
        spatialDirty.Clear();
        spatialDirtySet.Clear();

        foreach (long herd in dirtyHerds)
        {
            HerdFrame frame = default;
            if (controllersByHerd.TryGetValue(herd, out List<AnimalAIController> members))
            {
                // Recompute only changed herds, in stable ID order; avoid float sum drift.
                members.Sort(ControllerComparer.Instance);
                for (int i = 0; i < members.Count; i++)
                {
                    if (!spatialEntries.TryGetValue(members[i], out SpatialEntry entry) || entry.fleeing)
                        continue;
                    frame.positionSum += entry.position;
                    frame.count++;
                }
            }
            if (frame.count > 0) herdFrames[herd] = frame;
            else herdFrames.Remove(herd);
            AnimalAIProfiler.Add(AnimalAIProfiler.Counter.HerdRefreshes);
        }
        dirtyHerds.Clear();

        if (maximumRadiusDirty)
        {
            maximumAnimalColliderRadius = 0.5f;
            foreach (SpatialEntry entry in spatialEntries.Values)
                maximumAnimalColliderRadius = Mathf.Max(maximumAnimalColliderRadius, entry.radius);
            maximumRadiusDirty = false;
        }
        spatialIndexReady = true;
    }

    private void RefreshSpatialEntry(AnimalAIController controller)
    {
        bool existed = spatialEntries.TryGetValue(controller, out SpatialEntry previous);
        if (!IsActiveController(controller) || !controllerLookup.Contains(controller))
        {
            if (existed)
            {
                RemoveFromSpatialCell(controller, previous.cell);
                spatialEntries.Remove(controller);
                dirtyHerds.Add(previous.herd);
                maximumRadiusDirty |= previous.radius >= maximumAnimalColliderRadius;
            }
            return;
        }

        RefreshHerdMembership(controller);
        controller.CaptureCrowdSnapshot();
        Vector3 position = controller.CrowdSnapshotPosition;
        var entry = new SpatialEntry
        {
            position = position,
            cell = new Vector2Int(Mathf.FloorToInt(position.x / SpatialCellSize),
                Mathf.FloorToInt(position.z / SpatialCellSize)),
            radius = controller.AvoidanceColliderRadius,
            herd = controller.HerdId,
            identity = controller.SimulationId,
            fleeing = controller.IsFleeing
        };
        if (!existed || previous.cell != entry.cell || previous.identity != entry.identity)
        {
            if (existed) RemoveFromSpatialCell(controller, previous.cell);
            if (!controllersBySpatialCell.TryGetValue(entry.cell, out List<AnimalAIController> bucket))
            {
                bucket = spatialBucketPool.Count > 0 ? spatialBucketPool.Pop() : new List<AnimalAIController>(4);
                controllersBySpatialCell.Add(entry.cell, bucket);
            }
            int insert = bucket.BinarySearch(controller, ControllerComparer.Instance);
            bucket.Insert(insert < 0 ? ~insert : insert, controller);
            AnimalAIProfiler.Add(AnimalAIProfiler.Counter.SpatialCellChanges);
        }
        if (!existed || !previous.position.Equals(entry.position)
            || previous.herd != entry.herd || previous.fleeing != entry.fleeing
            || previous.identity != entry.identity)
        {
            if (existed) dirtyHerds.Add(previous.herd);
            dirtyHerds.Add(entry.herd);
        }
        if (existed && previous.radius >= maximumAnimalColliderRadius && previous.radius != entry.radius)
            maximumRadiusDirty = true;
        maximumAnimalColliderRadius = Mathf.Max(maximumAnimalColliderRadius, entry.radius);
        spatialEntries[controller] = entry;
        AnimalAIProfiler.Add(AnimalAIProfiler.Counter.SpatialUpdates);
    }

    private void RemoveFromSpatialCell(AnimalAIController controller, Vector2Int cell)
    {
        if (!controllersBySpatialCell.TryGetValue(cell, out List<AnimalAIController> bucket)) return;
        bucket.Remove(controller);
        if (bucket.Count != 0) return;
        controllersBySpatialCell.Remove(cell);
        spatialBucketPool.Push(bucket);
    }

    public void AppendRuntimeProfilerCounters()
    {
        int activeCount = CountActiveControllers();
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Total", ControllerCount,
            "World counts are current; work counters cover the last completed render frame. AI Detail timings overlap their parents.");
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Active", activeCount);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Dormant", ControllerCount - activeCount);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Near", nearActiveControllers);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Mid", midActiveControllers);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Far", farActiveControllers);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "SpatialCells", controllersBySpatialCell.Count);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Herds", HerdGroupCount);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Ticks", activeSimulationTicksLastFrame);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Due", simulationTickCandidatesLastFrame);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "Deferred", deferredSimulationTicksLastFrame);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "SeparationChecks", separationCandidateChecksLastFrame);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "CollisionChecks", animalCollisionCandidateChecksLastFrame);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "CollisionCells", animalCollisionCellChecksLastFrame);
        MapObjectTickProfiler.AddRuntimeCounter("AnimalAI", "PathWorkBudget", PathWorkBudgetPerTick,
            "Cache-independent logical work limit. Queries finish atomically; cold/warm caches schedule the same animals.");
        AnimalAIProfiler.AppendCounters();
    }
}
