using System;
using System.Collections.Generic;
using ProjectF.Animals;
using UnityEngine;

public static partial class Checks
{
    private static int passed;
    public static void Require(bool value, string name)
    {
        if (!value) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }
    private static AnimalAIController Animal(long id, float x, float z, long herd = 1)
        => new() { TerrainInstance = new() { DeterministicId = id }, HerdId = herd, SimulationPosition = new(x, 0, z) };

    public static void Main()
    {
        SpatialChecks();
        NeedsChecks();
        BudgetChecks();
        PresentationChecks();
        LoadingChecks();
        NavigationChecks();
        ActorClockChecks();
        AnimalAnimationChecks.Check();
        Console.WriteLine($"PASS {passed} animal optimization checks.");
    }

    private static void SpatialChecks()
    {
        var world = new AnimalAIWorld();
        var a = Animal(2, 0, 0); var b = Animal(1, 1, 0);
        world.AttachForCheck(a); world.AttachForCheck(b); world.RefreshForCheck();
        Require(world.IndexedForCheck == 2 && world.CellCountForCheck == 1, "shared spatial cell registers both animals");
        world.RefreshForCheck();
        Require(a.CrowdCaptures == 1 && b.CrowdCaptures == 1, "unchanged animals do not rebuild their crowd snapshots");
        a.SimulationPosition = new(10, 0, -4); world.DirtyForCheck(a);
        Require(a.CrowdSnapshotPosition.Equals(new Vector3(0, 0, 0)), "dirty movement remains unpublished until the next tick boundary");
        world.RefreshForCheck();
        var occupied = new HashSet<Vector2Int>();
        world.CopyOccupiedCoordinates(new(9, -5), new(11, -3), occupied);
        Require(occupied.SetEquals(new[] { new Vector2Int(10, -4) }), "cell migration updates spatial occupancy using simulation pose");
        Require(world.TryGetHerdCenter(1, out var center) && center.Equals(new Vector3(5.5f, 0, -2)), "only moved herd recomputes its center");
        a.IsFleeing = true; world.DirtyForCheck(a); world.RefreshForCheck();
        Require(world.TryGetHerdCenter(1, out center) && center.Equals(b.SimulationPosition), "fleeing animal leaves cohesion but remains a collision candidate");
        Require(world.IndexedForCheck == 2, "fleeing animal remains spatially indexed");
        b.gameObject.activeInHierarchy = false; world.DirtyForCheck(b); world.RefreshForCheck();
        Require(world.IndexedForCheck == 2 && world.TryGetHerdCenter(1, out _), "hidden views preserve simulation occupancy and herd contributions");
        b.gameObject.activeInHierarchy = true; world.DirtyForCheck(b); world.RefreshForCheck();
        Require(world.IndexedForCheck == 2 && world.TryGetHerdCenter(1, out _), "showing a view does not change simulation membership");
        world.RemoveForCheck(a); world.RemoveForCheck(b); world.RefreshForCheck();
        Require(world.IndexedForCheck == 0 && world.CellCountForCheck == 0, "removal recycles empty spatial buckets");

        var incremental = new AnimalAIWorld(); var rebuilt = new AnimalAIWorld();
        var left = new AnimalAIController[80]; var right = new AnimalAIController[80];
        for (int i = 0; i < left.Length; i++)
        {
            left[i] = Animal(i + 1, i % 10, i / 10, i % 5);
            right[i] = Animal(i + 1, i % 10, i / 10, i % 5);
            incremental.AttachForCheck(left[i]);
        }
        for (int i = right.Length - 1; i >= 0; i--) rebuilt.AttachForCheck(right[i]);
        var random = new Random(716);
        bool same = true;
        for (int step = 0; step < 300; step++)
        {
            int moved = random.Next(left.Length);
            var position = new Vector3(random.Next(-80, 80) * .125f, 0, random.Next(-80, 80) * .125f);
            foreach (var c in new[] { left[moved], right[moved] })
            {
                c.SimulationPosition = position; c.IsFleeing = step % 9 == 0;
                c.gameObject.activeInHierarchy = step % 13 != 0;
                c.HerdId = step % 5;
            }
            incremental.DirtyForCheck(left[moved]);
            rebuilt = new AnimalAIWorld();
            for (int i = right.Length - 1; i >= 0; i--) rebuilt.AttachForCheck(right[i]);
            incremental.RefreshForCheck(); rebuilt.RefreshForCheck();
            for (long herd = 0; herd < 5; herd++)
            {
                bool foundA = incremental.TryGetHerdCenter(herd, out var ca);
                bool foundB = rebuilt.TryGetHerdCenter(herd, out var cb);
                same &= foundA == foundB && ca.Equals(cb);
            }
            for (int i = 0; i < left.Length; i++)
            {
                same &= incremental.GetSeparation(left[i], 1.25f).Equals(rebuilt.GetSeparation(right[i], 1.25f));
                var candidate = left[i].SimulationPosition + new Vector3(.1f, 0, .2f);
                same &= incremental.IsAnimalPositionClearOrEscaping(left[i], left[i].SimulationPosition, candidate, .5f, true)
                    == rebuilt.IsAnimalPositionClearOrEscaping(right[i], right[i].SimulationPosition, candidate, .5f, true);
            }
        }
        Require(same, "300 movement/activation/herd changes match full refresh exactly, including reversed registration order");
        MapObjectTickProfiler.IsEnabled = false;
        for (int i = 0; i < 100; i++) incremental.RefreshForCheck();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) incremental.RefreshForCheck();
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "1000 unchanged spatial updates allocate zero bytes");
        MapObjectTickProfiler.IsEnabled = true;
    }

    private static void NeedsChecks()
    {
        var schedules = new AnimalNeedsSchedule[60];
        for (int i = 0; i < schedules.Length; i++) schedules[i].Initialize(0, i);
        bool spread = true; long total = 0;
        for (long tick = 1; tick <= 600; tick++)
        {
            int due = 0;
            for (int i = 0; i < schedules.Length; i++)
            {
                long elapsed = schedules[i].TakeElapsedTicks(tick, true);
                if (elapsed > 0) due++;
                total += elapsed;
            }
            spread &= due == 1;
        }
        for (int i = 0; i < schedules.Length; i++) total += schedules[i].TakeElapsedTicks(600, false);
        Require(spread && total == 36000, "60 distant animals spread one Needs update per tick with no elapsed time loss");
        var schedule = new AnimalNeedsSchedule(); schedule.Initialize(100, long.MinValue);
        long elapsedTotal = 0;
        for (long tick = 101; tick <= 137; tick++) elapsedTotal += schedule.TakeElapsedTicks(tick, true);
        elapsedTotal += schedule.TakeElapsedTicks(138, false);
        Require(elapsedTotal == 38 && schedule.TakeElapsedTicks(138, false) == 0, "near transition/save flush catches up once with a negative stable ID");
        var world = new AnimalAIWorld(); world.SelectForCheck();
        var distant = Animal(17, 100, 100); world.AttachForCheck(distant);
        for (int i = 0; i < 61; i++) world.ManagedUpdateTick(1f / 60);
        distant.FlushPendingNeeds();
        Require(MathF.Abs(distant.animal.NeedsElapsed - 61f / 60) < .00001f && distant.animal.NeedsUpdates <= 3,
            "production controller flush accounts for distant Needs before save/feeding");
        world.SetPaused(true); world.ManagedUpdateTick(1); distant.FlushPendingNeeds();
        Require(MathF.Abs(distant.animal.NeedsElapsed - 61f / 60) < .00001f, "paused AI does not accumulate deferred Needs time");
    }

    private static void BudgetChecks()
    {
        var world = new AnimalAIWorld(); var animals = new AnimalAIController[6];
        for (int i = 0; i < animals.Length; i++)
        {
            animals[i] = Animal(i + 1, i, 0); animals[i].Due = animals[i].ExpensiveSearch = true;
            world.AttachForCheck(animals[i]);
        }
        long start = AnimalGridPathfinder.ExpandedNodeCount;
        world.ManagedUpdateTick(1f / 60); world.CompleteForCheck();
        int count = 0; foreach (var a in animals) count += a.Executions;
        Require(count == 1 && world.DeferredSimulationTicksLastFrame == 5
            && AnimalGridPathfinder.ExpandedNodeCount - start > 2048,
            "node budget finishes one search, deferring remaining animals without false no-path results");
        for (int i = 0; i < 5; i++) world.ManagedUpdateTick(1f / 60);
        bool all = true; foreach (var a in animals) all &= a.Executions == 1;
        Require(all, "path-heavy animals rotate fairly across simulation ticks");
        world.CompleteForCheck(); world.AppendRuntimeProfilerCounters();
        Require(MapObjectTickProfiler.Counters["PathNodes"] > 0
            && MapObjectTickProfiler.Counters["ReachableCalls"] > 0
            && MapObjectTickProfiler.Counters["PathBudgetDeferrals"] > 0
            && MapObjectTickProfiler.Counters["PathBudgetMaxWorkPerTick"] > 2048,
            "profiler exports actual search work and budget deferrals");
    }

    private static void PresentationChecks()
    {
        var a = Animal(1, 10, 0); a.presentationActive = true;
        a.presentationTargetPosition = a.SimulationPosition;
        a.TickCulledPresentation(.02f, false);
        Require(!a.presentationActive && a.transform.position.Equals(a.SimulationPosition),
            "offscreen interpolation snaps once to authoritative pose");
        a.presentationActive = true; a.presentationStartPosition = a.SimulationPosition;
        a.presentationTargetPosition = new(11, 0, 0); a.presentationElapsed = 0;
        a.TickCulledPresentation(.05f, true);
        Require(a.presentationActive && MathF.Abs(a.transform.position.x - 10.5f) < .00001f,
            "visible presentation resumes normal interpolation");
    }

    private static void LoadingChecks()
    {
        var terrain = TerrainGenerator.Active = new(); var clock = new MapObjectTickManager();
        for (int i = 0; i < 50; i++) clock.Frame(.5f);
        Require(clock.Tick == 0 && clock.Backlog == 0, "initial world finalization blocks all fixed ticks and discards loading time");
        terrain.IsWorldReadyForPresentation = true; terrain.IsChunkStreamingBusy = true;
        clock.Frame(1);
        Require(clock.Executions == 0, "pending or in-progress chunk streaming still blocks simulation");
        terrain.IsChunkStreamingBusy = false; clock.Frame(1);
        Require(clock.Tick == 0, "completion frame cannot replay loading wall time");
        clock.Frame(1f / 60);
        Require(clock.Tick == 1 && clock.Executions == 1, "first post-loading fixed tick starts normally");
        terrain.IsChunkStreamingBusy = true; clock.Frame(10); terrain.IsChunkStreamingBusy = false; clock.Frame(10);
        clock.Frame(1f / 60);
        Require(clock.Tick == 2, "later chunk loads suspend and resume without catch-up bursts");
        clock.Pause(true); terrain.IsChunkStreamingBusy = true; clock.Frame(1);
        terrain.IsChunkStreamingBusy = false; clock.Frame(1); clock.Frame(1);
        Require(clock.Tick == 2, "loading completion preserves an explicit user pause");
        clock.Pause(false); clock.Frame(1f / 60);
        Require(clock.Tick == 3, "user can resume normally after loading and pause");
        TerrainGenerator.Active = null;
    }
}
