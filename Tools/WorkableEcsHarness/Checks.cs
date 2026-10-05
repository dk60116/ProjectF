using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Rendering;

static class Checks
{
    private static int assertions;
    private static void Require(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }

    private sealed class Target : IWorkableTarget
    {
        public bool IsTargetActive { get; set; } = true;
        public long WorkablePlacementSequence { get; set; }
        public int Item;
        public Bounds Bounds;
        public Vector2Int AnchorCoordinate { get; set; }
        public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Array.Empty<Vector2Int>();
        public bool ShowWorkableRange => true;
        public bool GlobalRangeVisualSuppressed => false;
        public float RangeVisualYOffset => 0;
        public int ResolveItemId() => Item;
        public bool TryGetWorkableRangeBounds(out Bounds bounds) { bounds = Bounds; return Bounds.size.x > 0; }
        public void SetSelectedRangeVisualRequested(bool value) { }
    }
    private static Target Range(float x, float z, int item = 34, long order = 0, float size = 2) =>
        new() { Item = item, WorkablePlacementSequence = order, Bounds = new(new Vector3(x, 0, z), new Vector3(size, .01f, size)), AnchorCoordinate = new((int)x, (int)z) };

    public static void Main()
    {
        GroupChecks();
        WorldChecks();
        PlacementChecks();
        RandomizedChecks();
        foreach (int count in new[] { 1000, 10000, 100000 }) ScaleChecks(count);
        Console.WriteLine($"Workable ECS: {assertions} checks passed (engine/render/save boundaries doubled; not Unity FPS).");
    }
    private static void GroupChecks()
    {
        var index = new WorkableRangeIndex();
        var a = Range(0, 0, order: 30); var bridge = Range(2, 0, order: 20); var c = Range(4, 0, order: 10);
        var corner = Range(-2, -2); var anvil = Range(0, 0, 35); var isolated = Range(50, 0);
        foreach (var t in new[] { a, bridge, c, corner, anvil, isolated }) index.Update(t);
        var result = new List<IWorkableTarget>();
        index.CollectConnected(a, result);
        Require(result.Count == 3 && ReferenceEquals(result[0], c) && ReferenceEquals(result[2], a), "edge-connected group sorted by placement order");
        Require(index.ContainsConnected(a, new Vector3(5, 100, 0)), "closed outer boundary ignores Y");
        Require(!index.ContainsConnected(a, new Vector3(5.001f, 0, 0)), "outside closed outer boundary rejected");
        index.CollectContaining(Vector3.zero, result);
        Require(result.Count == 4 && result.Contains(anvil) && !result.Contains(corner), "overlapping types form deduplicated recipe/material union; corner alone not connected");
        long builds = index.GroupBuilds, version = index.Version;
        index.CollectContaining(Vector3.zero, result); index.Update(a);
        Require(index.GroupBuilds == builds && index.Version == version, "identical query/update reuses cache");
        index.CollectConnected(isolated, result);
        index.Remove(bridge);
        builds = index.GroupBuilds;
        index.CollectConnected(isolated, result);
        Require(index.GroupBuilds == builds, "unrelated group survives local removal");
        index.CollectConnected(a, result);
        Require(result.Count == 1 && !index.ContainsConnected(a, new Vector3(4, 0, 0)), "removing bridge splits access group");
        index.Update(bridge); index.CollectConnected(a, result);
        Require(result.Count == 3, "restored bridge merges groups");
        bridge.Bounds = Range(20, 0).Bounds; index.Update(bridge); index.CollectConnected(a, result);
        Require(result.Count == 1, "moving bridge invalidates old and new cells");
        bridge.Bounds = Range(2, 0).Bounds; bridge.Item = 35; index.Update(bridge); index.CollectConnected(a, result);
        Require(result.Count == 1, "upgraded item type cannot join old type");
        anvil.IsTargetActive = false; index.Update(anvil); index.CollectContaining(Vector3.zero, result);
        Require(result.Count == 1 && ReferenceEquals(result[0], a), "disabled target removes cached recipe access");
        var nearEdge = Range(2.0005f, 0); index.Update(nearEdge); index.CollectConnected(a, result);
        Require(result.Count == 3 && result.Contains(c), "edge epsilon bridges floating-point gap and reconnects far member");
        index.CollectOwn(new Vector3(-1, 0, -1), result);
        Require(result.Contains(a) && result.Contains(corner), "negative cell boundary includes both own ranges");
        a.IsTargetActive = false; index.Remove(a);
        Require(!index.ContainsConnected(a, Vector3.zero), "removed root cannot keep crafting panel open");
    }
    private static WorkableInstance Add(WorkableWorld world, WorkableObject source, int x, int z = 0, long order = 1)
    {
        var state = new BlockStateStore.InstallationSaveState { anchorCoordinate = new(x, z), worldPosition = new(x, 0, z), placementSequence = order, itemId = source.ResolveItemId() };
        state.occupiedCoordinates.Add(state.anchorCoordinate);
        world.Terrain.Blocks[state.anchorCoordinate] = new();
        VirtualObjectWorld.Ensure().Register(state.anchorCoordinate);
        return world.Register(source, state);
    }
    private static void WorldChecks()
    {
        var terrain = new TerrainGenerator(); var source = new WorkableObject();
        var world = WorkableWorld.Ensure(terrain);
        var a = Add(world, source, 0); var b = Add(world, source, 2);
        Require(a.IsRuntimeActive && a.SceneObject == null && world.Count == 2, "entity has live identity without per-object scene body");
        Require(ReferenceEquals(terrain.Blocks[a.AnchorCoordinate].MapObject, a), "loaded footprint binds to entity");
        Require(ReferenceEquals(world.Register(source, a.Placement), a) && world.Count == 2, "restore/register duplicate keeps existing identity");
        a.SetItemFilterEnabled(70, 80, false);
        Require(!a.IsItemFilterEnabled(70, 80) && a.Placement.itemFilterMaskInitialized, "filter writes shared saved state");
        a.SetItemFilterEnabled(70, 80, true);
        Require(a.IsItemFilterEnabled(70, 80), "filter re-enabled");
        var candidates = new List<WorkableInstance>();
        Add(world, source, 1000);
        world.BuildNearby(Vector3.zero, candidates);
        Require(candidates.Count == 2, "near-player collider candidates exclude distant cells");
        var camera = new CameraRenderCulling { Enabled = true, Minimum = new(-1, -1), Maximum = new(1, 1) };
        world.BuildCandidates(camera, candidates);
        Require(candidates.Count == 2, "camera cell range selects nearby render records");
        var ray = new Ray(new Vector3(-10, 0, 0), Vector3.right);
        Require(world.TryRaycast(ray, 20, out var hit, out var distance) && ReferenceEquals(hit, a) && Math.Abs(distance - 9.5f) < .001f, "ray picks nearest entity");
        world.Remove(a.StorageKey);
        Require(!a.IsRuntimeActive && terrain.Blocks[a.AnchorCoordinate].MapObject == null && world.Count == 2, "remove expires handle slot and footprint binding");
        var replacement = Add(world, source, 0);
        Require(!a.IsRuntimeActive && replacement.IsRuntimeActive, "slot reuse never resurrects stale handle");
        Require(WorkableObject.RangeIndex.ContainsConnected(replacement, b.WorldPosition), "restored state reconnects ranges");
        VirtualObjectWorld.Current.Expire(replacement.Handle);
        Require(!replacement.IsRuntimeActive, "virtual identity expiration propagates to target");
        Require(world.TryRaycast(ray,20,out hit,out _) && ReferenceEquals(hit,b), "ray excludes expired entity");
        b.PlacementPresentationSuppressed = true;
        Require(!world.TryRaycast(ray,20,out _,out _), "ray excludes hidden placement animation");
        world.ClearRecords();
        Require(world.Count == 0 && !b.IsRuntimeActive && WorkableObject.RangeIndex.Targets.Count == 0, "save reset clears records, range entries and binding");
        world.Dispose(); Require(WorkableWorld.Current == null, "world disposal releases singleton");
    }
    private static void PlacementChecks()
    {
        var source = new WorkableObject(); var terrain = new TerrainGenerator { Source = source };
        var controller = new InstallationPlacementController(terrain);
        var state = new BlockStateStore.InstallationSaveState { anchorCoordinate = new(12, -8), itemId = source.ResolveItemId(), placementSequence = 98, quarterTurns = 2 };
        state.occupiedCoordinates.Add(state.anchorCoordinate);
        terrain.Blocks[state.anchorCoordinate] = new();
        Require(terrain.Restore(state), "legacy installation restored through data-only path");
        Require(WorkableWorld.Current.TryGet(state.anchorCoordinate,out var entity)
            && entity.WorldPosition == new Vector3(12,0,-8) && entity.WorkablePlacementSequence == 98,
            "missing world pose resolved without losing placement order");
        entity.SetItemFilterEnabled(5,64,false);
        Require(ReferenceEquals(entity.Placement,terrain.Store.States[state.anchorCoordinate]), "entity uses store-owned save state");
        Require(controller.Materialize(entity,out var proxy) && !entity.IsRuntimeActive, "editing creates proxy and expires data record");
        Require(ReferenceEquals(terrain.Blocks[state.anchorCoordinate].MapObject,proxy)
            && !MapObject.IsItemAllowedByFilterMask(5,true,proxy.Placement.itemFilterMaskWords), "editing restores footprint and filter");
        proxy.Placement.anchorCoordinate = new(20,-8); proxy.Placement.occupiedCoordinates.Clear();
        proxy.Placement.occupiedCoordinates.Add(proxy.Placement.anchorCoordinate);
        proxy.transform.SetPositionAndRotation(new Vector3(20,.25f,-8),Quaternion.identity);
        terrain.Blocks[proxy.Placement.anchorCoordinate] = new();
        Require(terrain.ConvertWorkablePresentation((WorkableObject)proxy,source,out var moved), "moved editing proxy converts back to data");
        Require(moved.WorldPosition == proxy.transform.position && moved.WorkablePlacementSequence == 98 && !moved.IsItemFilterEnabled(5,64), "move preserves exact pose, sequence and filter");
        var snapshot = terrain.Store.States[moved.StorageKey].Clone();
        var key = moved.StorageKey; WorkableWorld.Current.ClearRecords();
        Require(terrain.Restore(snapshot) && WorkableWorld.Current.TryGet(key,out var restored)
            && restored.WorldPosition == snapshot.worldPosition && restored.WorkablePlacementSequence == 98,
            "saved snapshot reconstructs data identity and shared state");
        WorkableWorld.Current.Dispose();
    }
    private static bool Connected(Target a, Target b)
    {
        float x = Math.Min(a.Bounds.max.x, b.Bounds.max.x) - Math.Max(a.Bounds.min.x, b.Bounds.min.x);
        float z = Math.Min(a.Bounds.max.z, b.Bounds.max.z) - Math.Max(a.Bounds.min.z, b.Bounds.min.z);
        return a.Item >= 0 && a.Item == b.Item && x >= -.001f && z >= -.001f && (x > .001f || z > .001f);
    }
    private static void RandomizedChecks()
    {
        var random = new System.Random(7001); var index = new WorkableRangeIndex();
        var targets = new List<Target>(); var found = new List<IWorkableTarget>(); var expected = new HashSet<Target>(); var queue = new List<Target>();
        for (int i = 0; i < 160; i++) { var t = Range(random.Next(-20, 21), random.Next(-20, 21), 34 + random.Next(2), i, random.Next(1, 7)); targets.Add(t); index.Update(t); }
        for (int step = 0; step < 100; step++)
        {
            var changed = targets[random.Next(targets.Count)];
            changed.Bounds = Range(random.Next(-20, 21), random.Next(-20, 21), size: random.Next(1, 7)).Bounds;
            changed.IsTargetActive = random.Next(5) != 0; index.Update(changed);
            var position = new Vector3(random.Next(-20, 21), 0, random.Next(-20, 21));
            expected.Clear(); queue.Clear();
            foreach (var t in targets) if (t.IsTargetActive && WorkableRangeIndex.Contains(t.Bounds, position) && expected.Add(t)) queue.Add(t);
            for (int cursor = 0; cursor < queue.Count; cursor++)
                foreach (var t in targets) if (t.IsTargetActive && Connected(queue[cursor], t) && expected.Add(t)) queue.Add(t);
            index.CollectContaining(position, found);
            Require(found.Count == expected.Count, "indexed query matches brute-force graph after mutation");
            foreach (var t in found) Require(expected.Contains((Target)t), "query contains no stale or unrelated target");
        }
    }
    private static void ScaleChecks(int count)
    {
        var index = new WorkableRangeIndex(); var timer = Stopwatch.StartNew();
        var targets = new Target[count];
        for (int i = 0; i < count; i++) { var t = Range(i * 2, 0, order: i); targets[i] = t; index.Update(t); }
        double registration = timer.Elapsed.TotalMilliseconds;
        var results = new List<IWorkableTarget>(count);
        timer.Restart(); index.CollectContaining(Vector3.zero, results); double build = timer.Elapsed.TotalMilliseconds;
        Require(results.Count == count && index.GroupBuilds == 1, "large connected component built once");
        long candidates = index.CandidateChecks, builds = index.GroupBuilds;
        index.CollectContaining(Vector3.zero, results); index.CollectOwn(Vector3.zero, results);
        long before = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
        for (int i = 0; i < 100; i++) { index.CollectContaining(Vector3.zero, results); index.CollectOwn(Vector3.zero, results); }
        double queries = timer.Elapsed.TotalMilliseconds; long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocation == 0 && index.GroupBuilds == builds, "warmed repeated own/group queries allocate zero and reuse group");
        Require(index.CandidateChecks - candidates < 2000, "own-range query stays local at all scales");
        timer.Restart(); index.Remove(targets[count / 2]); index.CollectConnected(targets[0], results); double split = timer.Elapsed.TotalMilliseconds;
        Require(results.Count == count / 2, "large bridge removal rebuilds split group correctly");
        Console.WriteLine($"{count:N0} ranges: register {registration:F1} ms; first group {build:F1} ms; 100 warmed queries {queries:F1} ms/{allocation} bytes; split/rebuild {split:F1} ms.");
    }
}
