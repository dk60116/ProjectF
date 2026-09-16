using System;
using System.Collections.Generic;
using UnityEngine;
using RobotArmState = RobotArm.RobotArmState;
using ProjectF.Runtime;

// World IO and power supply are doubles. Wake/plan/apply/sleep/demand/scheduling/slot storage are production code.
public static class Application { public static bool isPlaying = true; }
public static class MapObjectTickManager { public const int DefaultSimulationTicksPerSecond = 60; public const float FixedSimulationDeltaSeconds = 1f / 60f; }
public static class MapObjectTickProfiler
{
    public static bool IsEnabled => false;
    public static Scope SampleNamed(string kind, string type, string name) => default;
    public static long BeginSample() => 0;
    public static void EndUpdateSample(object tick, long start) { }
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}
public static class UtilityPole
{
    public static int PrepareCalls;
    public static void PrepareRobotArmPowerTick() { PrepareCalls++; }
}
public sealed class Block
{
    public Vector2Int Coordinate { get; }
    public Block(Vector2Int coordinate) { Coordinate = coordinate; }
}
public partial class RobotArmInstance
{
    public RobotArmWorld World;
    public sealed class PresentationData { public double SleepingPresentationTime; }
    private readonly PresentationData Data = new();
    private void SynchronizeSleepingPresentation() { }
    private void ApplyRuntimeSleepPose() { }
    public long SimulationId;
    public int HeldItemId => heldItemId;
    public void Persist() { }
    public void PersistTransferState() { }
    public bool IsRuntimeActive = true, Placed = true, PortsValid = true, MovingFreight;
    public bool runtimeSleeping = true, runtimeWakePending, waitingForDropRetry;
    public float pickupTimer, dropRetryTimer, actionTurnTimer, runtimeSleepCheckTimer;
    public int heldItemId = 1, Queries, PickupQueries, Animations, TransferAttempts;
    public float Watts = 10f;
    public bool PickupAvailable;
    public RobotArmState state = RobotArmState.WaitingForDrop;
    private float dropRetryInterval = .1f, actionTurnDelay = .1f;
    private const float RuntimeSleepRecheckIntervalSeconds = .1f;
    private float PickupIntervalSeconds => .1f;
    private PlannedTransferCommand plannedTransferCommand;
    private bool stagedTickPlanned, plannedPickupAvailabilityChecked, plannedPickupAvailable, plannedDropAvailabilityChecked, plannedDropAvailable;
    public sealed class Destination { public bool Available; public int Items; }
    public Destination Output = new();
    public void Sleep() { SetRuntimeSleeping(true); }
    private bool TryGetElectricOperationalPowerRequirement(out float watts) { watts = Watts; return watts > 0; }
    private void EnsureRuntimeStateInitialized() { }
    private bool HasPlacementRuntime() => Placed;
    private bool TryResolvePickupCoordinate(out Vector2Int coordinate) { coordinate = default; return PortsValid; }
    private bool TryResolveDropCoordinate(out Vector2Int coordinate) { coordinate = default; return PortsValid; }
    private bool IsMovingFreightCarAtCoordinate(Vector2Int coordinate) => MovingFreight;
    private bool CanPickupOneItem() { PickupQueries++; return PickupAvailable; }
    private bool CanPlaceHeldItem() { Queries++; return Output.Available && !MovingFreight; }
    private bool TryPlaceHeldItem()
    {
        TransferAttempts++;
        if (!Output.Available || MovingFreight) return false;
        Output.Available = false; Output.Items++; return true;
    }
    private void ClearHeldItem() { heldItemId = -1; }
    private void PlayDropAnimation() { Animations++; }
    private float ResolvePoweredDeltaTime(float dt) => dt;
    private void AdvanceAnimation(float dt) { }
    private void TickPickup(float dt) { }
    private void TickWaitBeforePickupTake(float dt) { }
    private void TickWaitAfterPickupTake(float dt) { }
    private void TickTurnToDrop(float dt) { }
    private void TickWaitAfterDropPlace(float dt) { }
    private void TickTurnToPickup(float dt) { }
    private void ApplyPlannedPickup() { }
    public void Normalize() => NormalizeRuntimeState();
}
public partial class RobotArmWorld
{
    private readonly ProjectF.Simulation.ActiveTickSet<RobotArmInstance> activeTicks = new(
        Comparer<RobotArmInstance>.Create((a, b) => a.SimulationId.CompareTo(b.SimulationId)));
    public double PresentationTime { get; private set; }
    private int lastPlannedCount;
    public long tickCandidatesVisited;
    public int ActiveCount => activeTicks.Count;
    internal void ScheduleTick(RobotArmInstance arm, bool wake = false) { activeTicks.Add(arm); }
    internal void UnscheduleTick(RobotArmInstance arm) { activeTicks.Remove(arm); }
    private readonly List<RobotArmInstance> ordered = new();
    private readonly List<RobotArmInstance> planned = new();
    private readonly Dictionary<Vector2Int, List<RobotArmInstance>> observers = new();
    private bool orderDirty;
    public void Add(RobotArmInstance arm, Vector2Int input, Vector2Int output)
    { arm.World = this; ordered.Add(arm); if (!arm.runtimeSleeping || arm.runtimeWakePending) activeTicks.Add(arm); orderDirty = true; Observe(input, arm); Observe(output, arm); }
    public void Tick() { PlanManagedUpdateTick(1f / 60f); ApplyManagedUpdateTick(); }
}
public sealed class TickProbe : IMapObjectUpdateTick
{
    public int StateCalls; public float Elapsed;
    public void ManagedUpdateTick(float dt) { StateCalls++; Elapsed += dt; }
}
public partial class SchedulingProbe
{
    public static void Check(int fps)
    {
        using var probe = new ProjectF.Simulation.SimulationTickWorld();
        var arms = new List<TickProbe>();
        for (int i = 0; i < 34; i++)
        {
            var arm = new TickProbe(); arms.Add(arm);
            probe.Register(arm);
        }
        double accumulator = 0;
        for (int f = 1; f <= fps * 5; f++)
        {
            accumulator += 1d / fps;
            while (accumulator + 0.000000001d >= 1d / 60d)
            {
                accumulator -= 1d / 60d;
                probe.Step();
            }
        }
        int total = 0;
        foreach (var arm in arms)
        {
            int expected = 60 * 5;
            Checks.Require(Math.Abs(arm.StateCalls - expected) <= 1, $"{fps} FPS fair 60Hz budget");
            Checks.Require(Math.Abs(arm.Elapsed - 5) < 0.04f, $"{fps} FPS elapsed time preserved");
            total += arm.StateCalls;
        }
        Console.WriteLine($"{fps} FPS, 34 arms, 5s: {total} ticks; expected {34 * 60 * 5}");
    }

    public static void CheckStagedOrder()
    {
        using var probe = new ProjectF.Simulation.SimulationTickWorld();
        var log = new List<string>();
        var high = new StagedProbeTick(20, log);
        var low = new StagedProbeTick(10, log);
        probe.Register(high); probe.Register(low); probe.Step();
        Checks.Require(
            string.Join(",", log) == "P10,P20,A10,A20",
            "all plans precede stable SimulationId apply order");
    }

    private sealed class StagedProbeTick : IMapObjectUpdateTick, IMapObjectStagedUpdateTick, IMapObjectSimulationIdentity
    {
        private readonly List<string> log;
        public long SimulationId { get; }
        public StagedProbeTick(long id, List<string> log) { SimulationId = id; this.log = log; }
        public void ManagedUpdateTick(float deltaTime) => throw new Exception("staged tick used direct update");
        public void PlanManagedUpdateTick(float deltaTime) => log.Add($"P{SimulationId}");
        public void ApplyManagedUpdateTick() => log.Add($"A{SimulationId}");
    }
}
public static class Checks
{
    private static int count;
    public static void Require(bool ok, string label) { if (!ok) throw new Exception(label); count++; }
    public static void Main()
    {
        var input = new Vector2Int(-1, 0); var output = new Vector2Int(1, 0);
        var world = new RobotArmWorld();
        var arm = new RobotArmInstance { SimulationId = 1, dropRetryTimer = .1f, waitingForDropRetry = true };
        world.Add(arm, input, output);
        for (int i = 0; i < 2000; i++) world.Wake(output);
        Require(arm.Queries == 0 && arm.runtimeSleeping && arm.runtimeWakePending, "notifications do not query or leave Sleep");
        Require(arm.dropRetryTimer == .1f && arm.waitingForDropRetry, "duplicate notifications preserve retry pose/timers");
        world.Tick();
        Require(arm.Queries == 1 && arm.runtimeSleeping && !arm.runtimeWakePending, "one query per dirty sleeping entity");
        UtilityPole.PrepareCalls = 0;
        for (int i = 0; i < 100; i++) world.Tick();
        Require(arm.Queries == 1 && arm.Animations == 0 && arm.TransferAttempts == 0, "idle sleeping world does no further destination queries");
        Require(UtilityPole.PrepareCalls == 0, "fully sleeping world does not refresh the electric network");
        Require(world.tickCandidatesVisited == 1 && world.ActiveCount == 0,
            "sleep removes tick membership, not only queries; duplicate wakes admit once");

        var batchWorld = new RobotArmWorld();
        var batchArm = new RobotArmInstance { SimulationId = 3 };
        var sharedInput = new Vector2Int(10, 0);
        var sharedOutput = new Vector2Int(11, 0);
        batchWorld.Add(batchArm, sharedInput, sharedOutput);
        batchWorld.Wake(new[] { new Block(sharedInput), new Block(sharedOutput), new Block(sharedInput) });
        Require(batchArm.runtimeWakePending && batchWorld.ActiveCount == 1,
            "batched block notifications rely on entity wake coalescing without duplicate scheduling");

        world.Wake(output + Vector2Int.up); world.Tick();
        Require(arm.Queries == 1, "unrelated neighboring cell does not wake an endpoint observer");
        arm.Output.Available = true;
        world.Wake(output);
        world.PlanManagedUpdateTick(1f / 60);
        Require(arm.Animations == 1 && arm.Output.Items == 0
                && arm.heldItemId == 1
                && arm.state == RobotArmState.WaitingBeforeDropPlace,
            "available output starts the bow while retaining the held item");
        world.ApplyManagedUpdateTick();
        Require(arm.Output.Items == 0 && arm.heldItemId == 1,
            "apply cannot place an item before the bow delay");
        for (int i = 0; i < 10; i++) world.Tick();
        Require(arm.Output.Items == 1 && arm.heldItemId == -1 && arm.Animations == 1,
            "the item commits after the bow in the ordered apply phase");
        world.ApplyManagedUpdateTick();
        Require(arm.Output.Items == 1 && arm.Animations == 1, "repeated apply cannot duplicate a transfer");

        var playerTake = new RobotArmInstance
        {
            SimulationId = 2,
            heldItemId = -1,
            state = RobotArmState.TurningToPickup,
            runtimeSleeping = true
        };
        var playerTakeWorld = new RobotArmWorld();
        playerTakeWorld.Add(playerTake, input, output);
        playerTake.WakeRuntimeSleep();
        playerTakeWorld.Tick();
        Require(!playerTake.runtimeSleeping && !playerTake.runtimeWakePending && playerTakeWorld.ActiveCount == 1,
            "taking a held item wakes the arm for its return-to-pickup state");

        var shared = new RobotArmInstance.Destination { Available = true };
        var first = new RobotArmInstance { SimulationId = 10, Output = shared };
        var second = new RobotArmInstance { SimulationId = 20, heldItemId = 2, Output = shared };
        var race = new RobotArmWorld();
        race.Add(second, input, output); race.Add(first, input, output);
        race.Wake(output); race.Tick();
        Require(shared.Items == 0 && first.Animations == 1 && second.Animations == 1,
            "contending arms both bow before attempting the shared slot");
        for (int i = 0; i < 10; i++) race.Tick();
        Require(shared.Items == 1 && first.heldItemId == -1 && second.heldItemId == 2, "stable ID order wins a shared slot despite reverse registration");
        Require(first.Animations == 1 && second.Animations == 1 && second.runtimeSleeping,
            "contention loser retains cargo and sleeps after the claimed gap closes");
        shared.Available = true; race.Wake(output); race.Tick();
        for (int i = 0; i < 10; i++) race.Tick();
        Require(shared.Items == 2 && second.heldItemId == -1, "contention loser resumes at the next vacancy");

        var empty = new RobotArmInstance { heldItemId = -1, state = RobotArmState.WaitingForPickup };
        var pickupWorld = new RobotArmWorld(); pickupWorld.Add(empty, input, output);
        pickupWorld.Wake(input); pickupWorld.Tick(); pickupWorld.Tick();
        Require(empty.PickupQueries == 1 && empty.runtimeSleeping, "empty pickup sleeps until another change");
        var moving = new RobotArmInstance { MovingFreight = true };
        var trainWorld = new RobotArmWorld(); trainWorld.Add(moving, input, output); trainWorld.Wake(output); trainWorld.Tick();
        Require(!moving.runtimeSleeping && moving.Animations == 0, "moving freight retains stop detection without transfer");
        moving.MovingFreight = false; moving.Output.Available = true;
        for (int i = 0; i < 20; i++) trainWorld.Tick();
        Require(moving.Output.Items == 1, "train stopping in the same cell eventually resumes transfer");
        var polling = new RobotArmInstance { heldItemId = -1, state = RobotArmState.WaitingForPickup, MovingFreight = true };
        var pollingWorld = new RobotArmWorld(); pollingWorld.Add(polling, input, output);
        pollingWorld.Wake(input); pollingWorld.Tick();
        pollingWorld.Tick(); // Wake transition resets once; subsequent confirmed checks must retain their interval.
        Require(polling.runtimeSleepCheckTimer > 0f, "confirmed awake state preserves sleep recheck interval");
        var idleWorld = new RobotArmWorld();
        for (int i = 0; i < 5000; i++)
            idleWorld.Add(new RobotArmInstance { SimulationId = i, heldItemId = -1, state = RobotArmState.WaitingForPickup },
                new Vector2Int(i * 4, 0), new Vector2Int(i * 4 + 1, 0));
        for (int i = 0; i < 100; i++) idleWorld.Tick();
        Require(idleWorld.tickCandidatesVisited == 0, "5000 sleeping arms cause zero tick candidate visits");
        idleWorld.Wake(new Vector2Int(0, 0)); idleWorld.Tick();
        Require(idleWorld.tickCandidatesVisited == 1 && idleWorld.ActiveCount == 0,
            "one input change wakes only its observer among 5000 sleeping arms");

        foreach (RobotArmState state in Enum.GetValues<RobotArmState>())
        foreach (bool held in new[] { false, true })
        foreach (bool ports in new[] { false, true })
        {
            if (state == RobotArmState.WaitingBeforeDropPlace) continue;
            var consumer = new RobotArmInstance { state = state, heldItemId = held ? 1 : -1, PortsValid = ports };
            bool expected = held || state != RobotArmState.WaitingForPickup && state != RobotArmState.WaitingForDrop ||
                state == RobotArmState.WaitingForPickup && ports;
            Require(consumer.TryGetElectricPowerDemand(out float watts) == expected && watts == (expected ? 10 : 0), "power demand truth table");
            Require(consumer.Queries == 0 && consumer.PickupQueries == 0, "power demand never queries contents");
        }
        var restored = new RobotArmInstance { heldItemId = 1, state = RobotArmState.WaitingBeforeDropPlace, actionTurnTimer = .1f };
        restored.Normalize();
        Require(restored.state == RobotArmState.WaitingForDrop && restored.actionTurnTimer == 0f,
            "interrupted pre-drop save restarts from a stable output pose without losing cargo");
        var dto = new RobotArm.TransferState { heldItemId = 3, pickupTimer = .2f, turnTimer = .123f };
        var copy = dto.Clone(); copy.heldItemId = 9;
        Require(dto.heldItemId == 3 && copy.turnTimer == .123f, "save DTO is independent and retains turn progress");
        var slots = new ResourceStateSlots<int>(1);
        var a = slots.Allocate(7); var b = slots.Allocate(8);
        Require(slots.Get(a.Index, a.Generation) == 7 && slots.Get(b.Index, b.Generation) == 8, "array resize preserves independent entity state");
        slots.Release(a.Index, a.Generation); var c = slots.Allocate(9);
        Require(c.Index == a.Index && !slots.Contains(a.Index, a.Generation) && slots.Get(c.Index, c.Generation) == 9, "reused slot never revives a removed target");
        bool staleRejected = false;
        try { _ = slots.Get(a.Index, a.Generation); } catch (InvalidOperationException) { staleRejected = true; }
        Require(staleRejected, "stale handle read fails explicitly");
        foreach (int fps in new[] { 30, 60, 113, 144, 240 }) SchedulingProbe.Check(fps);
        SchedulingProbe.CheckStagedOrder();
        Console.WriteLine($"PASS: {count} robot-arm ECS checks. No engine launched.");
    }
}
