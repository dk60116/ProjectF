using System;
using ProjectF.Conveyors;
using UnityEngine;

static partial class WorldChecks
{
    static void At(TerrainGenerator world, float time) { Time.time = time; Time.frameCount++; world.Tick(); }
    static TerrainGenerator TimedWorld(int length = 6)
    {
        Time.time = 0; Time.frameCount = 0;
        var world = new TerrainGenerator(length, 1);
        foreach (Block block in world.Blocks) block.Speed = 1;
        return world;
    }
    static void BoundaryTiming()
    {
        var empty = TimedWorld(); empty.Tick();
        var run = empty.Blocks[1].ConveyorTransport;
        long checksBefore = run.BoundaryChecks;
        for (int f = 1; f <= 1200; f++) At(empty, f / 120f);
        Check(run.BoundaryChecks == checksBefore && double.IsPositiveInfinity(run.NextBoundaryTime), "empty 10 seconds: no boundary checks");
        empty.Blocks[0].Put(1, 1);
        At(empty, 10);
        Check(run.Items.Count == 1 && run.InputAttempts == 1, "late input event reopens an idle appointment");
        empty.Blocks[0].Put(1, 2);
        At(empty, 10.1f);
        long inputAttempts = run.InputAttempts;
        long boundaryChecks = run.BoundaryChecks;
        for (int f = 13; f < 60; f++) At(empty, 10 + f / 120f);
        Check(run.InputAttempts == inputAttempts && run.BoundaryChecks == boundaryChecks, "no port polling before 0.5 second spacing");
        At(empty, 10.5f);
        Check(run.Items.Count == 2 && run.InputAttempts == 2, "one input at the calculated 0.5 second boundary");

        var travel = TimedWorld(); travel.Blocks[1].Put(0, 10); travel.Tick();
        run = travel.Blocks[1].ConveyorTransport;
        for (int f = 1; f < 420; f++) At(travel, f / 120f);
        Check(run.OutputAttempts == 0 && travel.Blocks[5].Id(0) < 0, "no output probe during 3.5 second interior travel");
        At(travel, 3.5f);
        Check(run.OutputAttempts == 1 && travel.Blocks[5].Id(0) == 10, "output occurs on head arrival");

        var jam = TimedWorld();
        for (int b = 1; b < 6; b++) { jam.Blocks[b].Put(0, b * 2); jam.Blocks[b].Put(1, b * 2 + 1); }
        jam.Tick(); run = jam.Blocks[1].ConveyorTransport;
        checksBefore = run.BoundaryChecks;
        int occupancyVersion = jam.Blocks[2].OccupancyVersion(0);
        int visualVersion = jam.Blocks[2].ConveyorItemVisualVersion;
        for (int f = 1; f <= 1200; f++) At(jam, f / 120f);
        Check(run.OutputAttempts == 1 && run.BoundaryChecks == checksBefore, "blocked 10 seconds: one failed output, zero periodic retries");
        Check(jam.Blocks[2].OccupancyVersion(0) == occupancyVersion && jam.Blocks[2].ConveyorItemVisualVersion == visualVersion, "packed state preserves failure and render cache versions");
        jam.Blocks[5].Take(0); At(jam, 10);
        Check(run.OutputAttempts == 2 && jam.Blocks[5].Id(0) >= 0, "output vacancy wakes a sleeping run");
        long outputAttempts = run.OutputAttempts;
        for (int f = 1; f < 60; f++) At(jam, 10 + f / 120f);
        Check(run.OutputAttempts == outputAttempts, "following item waits its 0.5 second spacing");
        At(jam, 10.5f);
        Check(run.OutputAttempts == outputAttempts + 1, "next occupied output is probed once at the next arrival");

        var hold = TimedWorld(); hold.Tick();
        hold.Blocks[0].Put(1, 50); hold.Blocks[0].HoldUntil(1, 2);
        At(hold, .1f); run = hold.Blocks[1].ConveyorTransport;
        for (int f = 1; f < 200; f++) At(hold, f * .01f);
        Check(run.InputAttempts == 0, "held inlet is scheduled, not polled");
        At(hold, 2); Check(run.InputAttempts == 1 && run.Items.Count == 1, "hold deadline reopens inlet without a wake loop");
        hold.Blocks[0].Put(1, 51); hold.Blocks[0].HoldUntil(1, 5);
        At(hold, 2.5f); hold.Blocks[0].HoldUntil(1, 0); At(hold, 2.6f);
        Check(run.Items.Count == 2, "early hold cancellation invalidates appointment");

        var arriving = TimedWorld(); arriving.Tick();
        arriving.Blocks[0].Put(1, 70); arriving.Blocks[0].SetArrivalTime(1, 3);
        At(arriving, .1f); run = arriving.Blocks[1].ConveyorTransport;
        checksBefore = run.BoundaryChecks;
        for (int f = 2; f < 30; f++) At(arriving, f * .1f);
        Check(run.BoundaryChecks == checksBefore && run.InputAttempts == 0, "input motion completion is reserved without intermediate probes");
        At(arriving, 3); Check(run.Items.Count == 1, "scheduled inlet data motion transfers at completion");
        arriving.Blocks[0].Put(1, 71); arriving.Blocks[0].MarkMovedNow();
        // The source is already settled, but is guarded in this same frame.
        Time.time = 3.5f; run.NotifyInputChanged();
        run.TryScheduledInput();
        Check(run.Items.Count == 1, "same-frame moved guard cannot cause an early transfer");
        At(arriving, 3.51f);
        Check(run.Items.Count == 2, "same-frame rejection does not lose the next input appointment");

        var gaps = TimedWorld(); gaps.Blocks[1].Put(0, 80); gaps.Blocks[3].Put(0, 81); gaps.Tick();
        run = gaps.Blocks[1].ConveyorTransport;
        At(gaps, 1.5f); Check(gaps.Blocks[5].Id(0) == 81, "sparse head reaches output at its own deadline");
        gaps.Blocks[5].Take(0); At(gaps, 1.6f);
        outputAttempts = run.OutputAttempts;
        for (int f = 17; f < 35; f++) At(gaps, f * .1f);
        Check(run.OutputAttempts == outputAttempts, "sparse gap does not use an unconditional 0.5 second timer");
        At(gaps, 3.5f); Check(gaps.Blocks[5].Id(0) == 80, "sparse follower uses its calculated arrival time");

        var pickup = TimedWorld(); pickup.Blocks[1].Put(0, 90); pickup.Blocks[3].Put(0, 91); pickup.Tick();
        run = pickup.Blocks[1].ConveyorTransport;
        At(pickup, .5f);
        run.Items.TryGetAt(1, out _, out double headPosition);
        int headSlot = run.Items.GetReservedLane(1, headPosition);
        run.Blocks[headSlot / 2].Take(headSlot % 2);
        At(pickup, 1.5f);
        Check(run.OutputAttempts == 0, "pickup cancels the removed head's old output deadline");
        At(pickup, 3.5f);
        Check(pickup.Blocks[5].Id(0) == 90 && pickup.Total == 1, "replacement head keeps its own arrival and item count");

        var topology = TimedWorld(12); topology.Blocks[1].Put(0, 100); topology.Tick();
        run = topology.Blocks[1].ConveyorTransport;
        int writes = topology.RawWrites;
        double appointment = run.NextBoundaryTime;
        topology.BumpProxyVersion(); At(topology, .01f);
        Check(topology.Blocks[1].ConveyorTransport == run && topology.RawWrites == writes, "unrelated proxies preserve ownership without slot exports");
        Check(run.NextBoundaryTime == appointment, "unrelated proxies preserve deadline");
        int priorVersion = topology.Blocks[1].OccupancyVersion(1);
        topology.Blocks[5].ChangeSpeed(.5f); At(topology, .02f);
        Check(!run.Active && topology.Total == 1, "speed edit exports before rebuilding homogeneous segments");
        Check(topology.Blocks[1].OccupancyVersion(1) != priorVersion, "new ownership cannot reuse the previous run's occupancy cache version");
        foreach (Block block in topology.Blocks)
            if (block.OwnsConveyorTransport) Check(block.ConveyorTransport.Speed == block.Speed, "rebuilt timing matches uniform speed");
        var portRun = topology.Blocks[8].ConveyorTransport;
        topology.Blocks[11].ReleaseConveyorTransport();
        Check(!portRun.Active, "port topology removal releases its connected run and deadline");

        Console.WriteLine("Timing: 120 Hz x 10 seconds empty/blocked = 0 repeated boundary checks; 0.5 s cadence, head arrival, holds, vacancy, stable versions and proxy/speed/topology events PASS.");
    }
}

internal class ConveyorRuntimeArrays { internal readonly int[] LaneOccupancyVersions = new int[2]; }
public partial class Block
{
    private const int ConveyorStackLaneLimit = 2;
    private ConveyorRuntimeArrays conveyorRuntimeArrays;
    private int conveyorItemVisualVersion;
    private ConveyorRuntimeArrays EnsureConveyorRuntimeArrays() => conveyorRuntimeArrays ??= new ConveyorRuntimeArrays();
    internal int OccupancyVersion(int lane) => GetConveyorLaneOccupancyVersion(lane);
    internal void HoldUntil(int lane, float until) { conveyorItemMovementHoldUntilTimes[lane] = until; NotifyTransportPortChanged(lane); }
    internal void ChangeSpeed(float speed) { Speed = speed; ReleaseConveyorTransport(); }
    private int movedFrame = -1;
    private bool WasConveyorItemMovedThisFrame(int lane) => movedFrame == Time.frameCount;
    internal void MarkMovedNow() { movedFrame = Time.frameCount; }
    internal void SetArrivalTime(int lane, float arrival)
    {
        ready[lane] = arrival;
        conveyorItemMotionStates[lane] = new ConveyorDataMotionState { active = true, destinationLaneIndex = lane, startTime = Time.time, duration = arrival - Time.time };
        NotifyTransportPortChanged(lane);
    }
}
public partial class TerrainGenerator { internal void BumpProxyVersion() { loadedBlocks.RuntimeProxyVersion++; } }
