using ProjectF.Conveyors;

internal static class WorldChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Require(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        BeltSimulationWorld Create()
        {
            var world = new BeltSimulationWorld();
            world.Allocate(16, 1, 0, 0);
            var b = world.Buffers;
            b.Groups[0] = new BeltGroupRange { Start = 0, Count = 16, MaxWaves = 3 };
            for (int i = 0; i < 16; i++)
            {
                world.AddLane(new BeltLaneId(i - 8, 100, 0));
                b.Lanes[i] = BeltLaneState.Empty;
                b.Topology[i] = new BeltLaneTopology { Target = i < 15 ? i + 1 : -1, Alternate = -1,
                    Splitter = -1, Duration = BeltSimulationJob.TickUnits * 2 };
            }
            world.ValidateTopology(); return world;
        }
        using var original = Create();
        var first = new BeltLaneId(-8, 100, 0); var last = new BeltLaneId(7, 100, 0);
        Require(original.TryInsert(first, 41), "data port inserts without Block/view");
        Require(!original.TryInsert(first, 42), "data port cannot overwrite an occupied lane");
        for (int i = 0; i < 9; i++) original.Step();
        using var restored = Create();
        for (int i = 0; i < original.LaneCount; i++)
            Require(restored.RestoreLane(original.CaptureLane(original.GetLaneId(i))), "coordinate checkpoint restores without loaded Block");
        restored.RestoreTick(original.Tick);
        for (int step = 0; step < 70; step++)
        {
            original.Step(); restored.Step();
            for (int i = 0; i < 16; i++)
            {
                var a = original.Buffers.Lanes[i]; var b = restored.Buffers.Lanes[i];
                Require(a.Equals(b) && original.Buffers.MergeCursor[i] == restored.Buffers.MergeCursor[i],
                    "viewless restore preserves motion, ownership and merge cursor");
            }
        }
        Require(original.TryTakeSettled(last, out int item) && item == 41, "data-only transport crosses chunk boundary and reaches sink");
        Require(!original.TryTakeSettled(last, out _), "item ownership consumed exactly once");
        Require(original.TryInsert(first, 42, 10 * BeltSimulationJob.TickUnits), "hold inserts at data port");
        Require(!original.TryTakeSettled(first, out _), "held item cannot be consumed before arrival");
        // Parallel.For is the harness scheduler, not Unity's native job scheduler.
        // Measure the world/kernel allocation separately from that test double.
        Unity.Jobs.JobScheduling.UseParallel = false;
        try
        {
            for (int i = 0; i < 100; i++) original.Step();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) original.Step();
            Require(GC.GetAllocatedBytesForCurrentThread() == before, "world/kernel stepping allocates no managed memory with synchronous scheduling");
        }
        finally { Unity.Jobs.JobScheduling.UseParallel = true; }
        using var invalid = Create();
        var route = invalid.Buffers.Topology[0]; route.Target = 99; invalid.Buffers.Topology[0] = route;
        bool rejected = false;
        try { invalid.ValidateTopology(); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "invalid worker edge rejected before scheduling");
        using var boundary = Create();
        boundary.Schedule(); boundary.Complete();
        Require(boundary.Tick == 0, "job completion leaves the clock unchanged until publication commits");
        rejected = false;
        try { boundary.CaptureLane(first); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "cannot save new buffers with the previous tick clock");
        rejected = false;
        try { boundary.TryInsert(first, 9); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "data IO cannot race publication of an open tick");
        boundary.CommitStep(); boundary.CommitStep();
        Require(boundary.Tick == 1 && boundary.TryInsert(first, 9), "publication advances the clock exactly once and reopens IO");
        return checks;
    }
}
