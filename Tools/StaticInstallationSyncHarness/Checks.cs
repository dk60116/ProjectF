using System.Collections;
using System.Reflection;
using ProjectF.MapObjects;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks++; }
    static BlockStateStore.InstallationSaveState State(int type, int cell) => new()
    {
        itemId = type, placementSequence = cell + 1, anchorCoordinate = new(cell, 0),
        occupiedCoordinates = new() { new(cell, 0) }, hasWorldPose = true, worldPosition = new(cell, 0, 0)
    };
    static void Invoke(StaticMapObjectBatchRenderer renderer, string method)
        => typeof(StaticMapObjectBatchRenderer).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, null);
    static void Complete(IEnumerator work) { while (work.MoveNext()) { } (work as IDisposable)?.Dispose(); }
    static void Main()
    {
        using var world = new VirtualObjectWorld();
        var manager = new ItemManager();
        manager.Definitions[1] = new() { MapObjectArchetype = new() { Supported = true } };
        manager.Definitions[2] = new() { MapObjectArchetype = new() { Supported = true } };
        manager.Definitions[3] = new();
        var renderer = new StaticMapObjectBatchRenderer(); renderer.Configure(world, manager);
        var a = State(1, 1); var b = State(2, 2); var unsupported = State(3, 3);
        var ah = world.UpsertInstallationHandle(a); var bh = world.UpsertInstallationHandle(b);
        world.UpsertInstallationHandle(unsupported);
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 2 && renderer.SynchronizationCount == 1, "initial supported types are synchronized");
        Require(renderer.LastSynchronizedDataOnlyInstallationCount == 2 && renderer.UnsupportedActiveTypeCount == 1,
            "unsupported records are rejected before record copying");
        var hostA = StaticMapObjectTypeHost.Hosts[1]; var hostB = StaticMapObjectTypeHost.Hosts[2];
        int initialQueries = manager.Queries;
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.SynchronizationCount == 1, "unchanged world skips synchronization");
        unsupported.worldPosition = new(3.5f, 0, 0); world.UpsertInstallationHandle(unsupported);
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.SynchronizationCount == 1 && manager.Queries == initialQueries,
            "unsupported visual changes do not rebuild hosts or repeat catalog lookup");
        var live = State(1, 4);
        world.AttachInstallationView(live, 55, live.worldPosition, live.worldRotation);
        renderer.SynchronizeForWorldPresentation();
        live.worldPosition = new(4.5f, 0, 0); world.AttachInstallationView(live, 55, live.worldPosition, live.worldRotation);
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.SynchronizationCount == 1, "live registrations and pose updates never rebuild data-only hosts");
        a.worldPosition = new(1.5f, 0, 0); world.UpsertInstallationHandle(a);
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.SynchronizationCount == 2 && hostA.BeginCalls == 2 && hostB.BeginCalls == 1,
            "only the changed supported type begins synchronization");
        Require(renderer.LastSynchronizedDataOnlyInstallationCount == 1 && hostB.Handles.Contains(bh),
            "unchanged type matrices survive another type's rebuild");
        world.AttachInstallationView(a, 56, a.worldPosition, a.worldRotation); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 1 && !StaticMapObjectTypeHost.Hosts.ContainsKey(1),
            "view attachment removes the last data-only instance without leaving a ghost host");
        world.UpsertInstallationHandle(a); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 2 && StaticMapObjectTypeHost.Hosts[1].Handles.Contains(ah),
            "view detachment restores the static instance with the same handle");
        world.RemoveInstallation(bh); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 1 && !StaticMapObjectTypeHost.Hosts.ContainsKey(2),
            "last instance removal releases only its own host");
        Invoke(renderer, "OnDisable"); Invoke(renderer, "OnEnable"); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 1, "disable and enable rebuild suspended matrices");
        int syncs = renderer.SynchronizationCount;
        for (int i = 0; i < 100; i++) renderer.SynchronizeForWorldPresentation();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) renderer.SynchronizeForWorldPresentation();
        Require(GC.GetAllocatedBytesForCurrentThread() == allocated && renderer.SynchronizationCount == syncs,
            "warmed unchanged synchronization checks allocate nothing");
        a.worldPosition = new(1.75f, 0, 0); world.UpsertInstallationHandle(a);
        StaticMapObjectTypeHost.ThrowNextSubmission = true;
        try { renderer.SynchronizeForWorldPresentation(); throw new Exception("missing expected fixture exception"); }
        catch (InvalidOperationException) { }
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 1, "a failed synchronization does not commit the aggregate revision");
        a.worldPosition = new(1.8f, 0, 0); world.UpsertInstallationHandle(a);
        ProjectF.Benchmark.BenchmarkLayout.ForcedYields = 1;
        var benchmark = renderer.PrepareBenchmarkPresentation(); Require(benchmark.MoveNext(), "benchmark synchronization can yield");
        var added = State(1, 5); world.UpsertInstallationHandle(added);
        Complete(benchmark); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 2, "changes during benchmark yield are picked up after completion");
        a.worldPosition = new(1.9f, 0, 0); world.UpsertInstallationHandle(a);
        ProjectF.Benchmark.BenchmarkLayout.ForcedYields = 1;
        benchmark = renderer.PrepareBenchmarkPresentation(); Require(benchmark.MoveNext(), "benchmark cancellation fixture yields");
        (benchmark as IDisposable)!.Dispose(); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 2, "cancelled benchmark invalidates and restores all host matrices");
        a.itemId = 2; var replacementHandle = world.UpsertInstallationHandle(a); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 2 && StaticMapObjectTypeHost.Hosts[1].InstanceCount == 1
            && StaticMapObjectTypeHost.Hosts[2].InstanceCount == 1 && !world.IsHandleAlive(ah),
            "item replacement invalidates both old and new type hosts without stale handles");
        int unchangedBegins = StaticMapObjectTypeHost.Hosts[1].BeginCalls;
        syncs = renderer.SynchronizationCount;
        MapObjectTickManager.WaitingForWorldLoad = true;
        Invoke(renderer, "LateUpdate");
        Require(renderer.ActiveInstanceCount == 2 && renderer.SynchronizationCount == syncs,
            "temporary rendering suspension preserves host membership and cached revisions");
        var restored = State(2, 6); world.UpsertInstallationHandle(restored);
        Invoke(renderer, "LateUpdate"); MapObjectTickManager.WaitingForWorldLoad = false;
        Invoke(renderer, "LateUpdate");
        Require(renderer.ActiveInstanceCount == 3 && StaticMapObjectTypeHost.Hosts[1].BeginCalls == unchangedBegins,
            "load resume rebuilds changed types while preserving unchanged host membership");
        world.Clear(); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 0 && renderer.ActiveTypeCount == 0 && renderer.UnsupportedActiveTypeCount == 0,
            "world clear removes all prior static hosts and active rejected types");
        world.UpsertInstallationHandle(b); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 1, "world repopulation after clear creates new hosts");
        var replacement = new VirtualObjectWorld();
        renderer.Configure(replacement, manager); replacement.UpsertInstallationHandle(a); renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 1 && StaticMapObjectTypeHost.Hosts[a.itemId].Handles.Count == 1,
            "Configure releases old hosts and clears type-version caches");
        replacement.Dispose(); Invoke(renderer, "OnDestroy");
        CheckLargeRejectedWorld(manager);
        Console.WriteLine($"PASS: {checks} static-installation incremental-sync checks; world and renderer are production code, GPU/type-host boundaries doubled.");
    }
    static void CheckLargeRejectedWorld(ItemManager manager)
    {
        using var world = new VirtualObjectWorld();
        var renderer = new StaticMapObjectBatchRenderer(); renderer.Configure(world, manager);
        for (int i = 0; i < 100000; i++) world.UpsertInstallationHandle(State(3, i));
        var supported = State(1, 100001); world.UpsertInstallationHandle(supported);
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.ActiveInstanceCount == 1 && renderer.LastSynchronizedDataOnlyInstallationCount == 1,
            "100000 rejected records are never copied into static synchronization");
        supported.worldPosition = new(100001.5f, 0, 0); world.UpsertInstallationHandle(supported);
        renderer.SynchronizeForWorldPresentation();
        Require(renderer.LastSynchronizedDataOnlyInstallationCount == 1, "a supported change copies only its own indexed records");
        long totalAllocated = 0; int syncs = renderer.SynchronizationCount;
        var rejected = State(3, 0);
        for (int i = 0; i < 1000; i++)
        {
            rejected.worldPosition = new(i + 0.5f, 0, 0); world.UpsertInstallationHandle(rejected);
            long before = GC.GetAllocatedBytesForCurrentThread();
            renderer.SynchronizeForWorldPresentation();
            if (i >= 100) totalAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Require(renderer.SynchronizationCount == syncs && totalAllocated == 0,
            "warmed rejected-type version churn skips full sync and allocates nothing in renderer");
        Invoke(renderer, "OnDestroy");
    }
}
