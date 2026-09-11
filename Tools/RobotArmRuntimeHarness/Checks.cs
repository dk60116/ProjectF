using System;
using System.Collections.Generic;
using UnityEngine;

// Only engine clock, scene IO, power delivery and tick-registration storage are doubles.
// Demand, wake coalescing, registration repair, delta time and bucket scheduling
// are extracted from production sources by Run.ps1.
public static class Application { public static bool isPlaying = true; }
public interface IMapObjectUpdateTick { void ManagedUpdateTick(float deltaTime); }
public interface IMapObjectStagedUpdateTick { void PlanManagedUpdateTick(float deltaTime); void ApplyManagedUpdateTick(); }
public interface IMapObjectSimulationIdentity { long SimulationId { get; } }
public class InputOutputModule : IMapObjectUpdateTick
{
    public virtual float ManagedUpdateTickIntervalSeconds => 0.1f;
    public virtual bool TryGetElectricPowerDemand(out float watts) { watts = 0; return false; }
    public virtual void ManagedUpdateTick(float dt) { }
    protected virtual void WakeRuntimeUpdate() => throw new Exception("Arm must own its wake state");
    public void NotifyModule() => WakeRuntimeUpdate();
}
public static class MapObjectTickManager
{
    public const int DefaultSimulationTicksPerSecond = 60;
    public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
    public static readonly HashSet<IMapObjectUpdateTick> Registered = new();
    public static int Registrations;
    public static bool IsUpdateTickRegistered(IMapObjectUpdateTick tick) => Registered.Contains(tick);
    public static void RegisterUpdateTick(IMapObjectUpdateTick tick) { Registered.Add(tick); Registrations++; }
    public static void UnregisterUpdateTick(IMapObjectUpdateTick tick) => Registered.Remove(tick);
}
public static class MapObjectTickProfiler
{
    public static bool IsEnabled => false;
    public static Scope SampleNamed(string kind, string type, string name) => default;
    public static long BeginSample() => 0;
    public static void EndUpdateSample(object tick, long start) { }
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}
public partial class RobotArm : InputOutputModule
{
    private const float DefaultManagedUpdateDeltaSeconds = 1f / 60f;
    private bool runtimeWakePending, runtimeSleeping;
    public bool isActiveAndEnabled = true, Placed = true, PortsValid = true;
    public int heldItemId = -1, WakeTransitions, StateCalls;
    public float Watts = 10, Elapsed, pickupTimer, dropRetryTimer, runtimeSleepCheckTimer, actionTurnTimer;
    public bool waitingForDropRetry;
    public RobotArmState state;
    public bool Sleeping => runtimeSleeping;
    public void NotifyCoordinate() => WakeRuntimeSleep();
    public void Sleep() { runtimeSleeping = true; SetUpdateTickRegistered(false); }
    public void Disable() { isActiveAndEnabled = false; SetUpdateTickRegistered(false); }
    private void SetRuntimeSleeping(bool sleeping) { runtimeSleeping = sleeping; WakeTransitions++; SetUpdateTickRegistered(!sleeping); }
    private bool TryGetElectricOperationalPowerRequirement(out float watts) { watts = Watts; return watts > 0; }
    private void EnsureRuntimeStateInitialized() { }
    private bool HasPlacementRuntime() => Placed;
    private bool TryResolvePickupCoordinate(out Vector2Int coordinate) { coordinate = default; return PortsValid; }
    private bool TryResolveDropCoordinate(out Vector2Int coordinate) { coordinate = default; return PortsValid; }
    private bool CanPickupOneItem() => throw new Exception("Power demand searched inventory");
    private void EnsureBodyRotationCache() { }
    private bool ShouldRunRuntimeSleepCheck(float dt) => false;
    private bool RefreshRuntimeSleepState() { SetRuntimeSleeping(!CanPlaceHeldItem()); return Sleeping; }
    private float ResolvePoweredDeltaTime(float dt) => dt;
    private void ApplyPoweredAnimatorSpeed() { }
    private void Record(float dt) { StateCalls++; Elapsed += dt; }
    private void TickPickup(float dt) => Record(dt);
    private void TickWaitBeforePickupTake(float dt) => Record(dt);
    private void TickWaitAfterPickupTake(float dt) => Record(dt);
    private void TickTurnToDrop(float dt) => Record(dt);
    private void TickWaitAfterDropPlace(float dt) => Record(dt);
    private void TickTurnToPickup(float dt) => Record(dt);
    public override void ManagedUpdateTick(float dt) { runtimeWakePending = false; Record(dt); }

    public sealed class Destination { public bool Available; public int Items; }
    public Destination Output = new();
    public bool MovingFreight;
    public int DropAnimations, TransferAttempts;
    private float dropRetryInterval = 0.1f, actionTurnDelay = 0.1f;
    private PlannedTransferCommand plannedTransferCommand;
    private bool IsMovingFreightCarAtCoordinate(Vector2Int coordinate) => MovingFreight;
    private bool CanPlaceHeldItem() => Output.Available;
    private bool CanPlaceHeldItemForCurrentPlan() => CanPlaceHeldItem();
    private bool TryPlaceHeldItem()
    {
        TransferAttempts++;
        if (!Output.Available) return false;
        Output.Available = false;
        Output.Items++;
        return true;
    }
    private void ClearHeldItem() => heldItemId = -1;
    private void PlayDropAnimation() => DropAnimations++;
    public void PlanDrop() { plannedTransferCommand = PlannedTransferCommand.None; TickDrop(1f / 60); }
    public void ApplyDrop() { if (plannedTransferCommand == PlannedTransferCommand.Drop) ApplyPlannedDrop(); plannedTransferCommand = PlannedTransferCommand.None; }
    public void Normalize() => NormalizeRuntimeState();
}
public partial class SchedulingProbe
{
    private const float FixedSimulationDeltaSeconds = 1f / 60f;
    private bool updateTicksDirty;
    private long simulationTick;
    private readonly HashSet<IMapObjectUpdateTick> updateTickSet = new();
    private readonly List<UpdateTickEntry> dueUpdateTickEntries = new(64);
    public static void Check(int fps)
    {
        var probe = new SchedulingProbe();
        var arms = new List<RobotArm>();
        var bucket = new UpdateTickBucket(1);
        for (int i = 0; i < 34; i++)
        {
            var arm = new RobotArm(); arms.Add(arm);
            probe.updateTickSet.Add(arm);
            bucket.Entries.Add(new UpdateTickEntry(arm, 1, 0));
        }
        double accumulator = 0;
        for (int f = 1; f <= fps * 5; f++)
        {
            accumulator += 1d / fps;
            while (accumulator + 0.000000001d >= 1d / 60d)
            {
                accumulator -= 1d / 60d;
                probe.simulationTick++;
                probe.CollectDueUpdateEntries(bucket);
                probe.dueUpdateTickEntries.Sort(CompareUpdateTickEntries);
                probe.PlanStagedUpdateEntries();
                probe.ApplyDueUpdateEntries(false);
                probe.dueUpdateTickEntries.Clear();
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
        var probe = new SchedulingProbe { simulationTick = 1 };
        var log = new List<string>();
        var bucket = new UpdateTickBucket(1);
        var high = new StagedProbeTick(20, log);
        var low = new StagedProbeTick(10, log);
        probe.updateTickSet.Add(high);
        probe.updateTickSet.Add(low);
        bucket.Entries.Add(new UpdateTickEntry(high, 1, 0));
        bucket.Entries.Add(new UpdateTickEntry(low, 1, 0));
        probe.CollectDueUpdateEntries(bucket);
        probe.dueUpdateTickEntries.Sort(CompareUpdateTickEntries);
        probe.PlanStagedUpdateEntries();
        probe.ApplyDueUpdateEntries(false);
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
        long perTick = DeterministicSimulationUnits.RateForTicks(10f, 1L);
        Require(perTick * 60L == DeterministicSimulationUnits.FromInt(10), "integer rate is partition independent across one second");
        Require(DeterministicSimulationUnits.DeltaTimeToTicks(0f) == 0L, "zero delta consumes no simulation unit");
        Require(
            DeterministicSimulationUnits.MultiplyRatio(
                DeterministicSimulationUnits.FromInt(10),
                1L,
                4L) == DeterministicSimulationUnits.FromFloat(2.5f),
            "integer ratios truncate deterministically");
        foreach (var state in Enum.GetValues<RobotArm.RobotArmState>())
        foreach (bool held in new[] { false, true })
        foreach (bool ports in new[] { false, true })
        {
            if (state == RobotArm.RobotArmState.WaitingBeforeDropPlace) continue; // Legacy state is checked via normalization below.
            var arm = new RobotArm { state = state, heldItemId = held ? 1 : -1, PortsValid = ports };
            bool expected = held || (state != RobotArm.RobotArmState.WaitingForPickup && state != RobotArm.RobotArmState.WaitingForDrop)
                || (state == RobotArm.RobotArmState.WaitingForPickup && ports);
            Require(arm.TryGetElectricPowerDemand(out float watts) == expected, "demand state/port truth table");
            Require(watts == (expected ? 10 : 0), "same configured demand");
            arm.Placed = false;
            Require(!arm.TryGetElectricPowerDemand(out _), "unplaced arm demand excluded");
            arm.Placed = true; arm.Watts = 0;
            Require(!arm.TryGetElectricPowerDemand(out _), "non-electric arm demand excluded");
        }
        var target = new RobotArm { pickupTimer = 1, dropRetryTimer = 1, actionTurnTimer = 0.5f };
        target.Sleep(); target.NotifyCoordinate();
        Require(!target.Sleeping && MapObjectTickManager.IsUpdateTickRegistered(target), "wake sleeping arm");
        int registrations = MapObjectTickManager.Registrations;
        target.pickupTimer = 0.7f;
        for (int i = 0; i < 1000; i++) { target.NotifyCoordinate(); target.NotifyModule(); }
        Require(MapObjectTickManager.Registrations == registrations && target.WakeTransitions == 1, "coalesce coordinate and base-module notifications");
        Require(target.pickupTimer == 0.7f && target.actionTurnTimer == 0.5f, "duplicate wake preserves timers");
        MapObjectTickManager.Registered.Remove(target); target.NotifyModule();
        Require(MapObjectTickManager.IsUpdateTickRegistered(target), "pending wake repairs lost execution entry");
        target.ManagedUpdateTick(1f / 60); target.pickupTimer = 1; target.NotifyCoordinate();
        Require(target.pickupTimer == 0, "a new wake after tick rechecks promptly");
        target.Disable(); target.NotifyModule();
        Require(!MapObjectTickManager.IsUpdateTickRegistered(target), "disabled module stays unregistered");
        target.isActiveAndEnabled = true; target.NotifyModule();
        Require(MapObjectTickManager.IsUpdateTickRegistered(target), "pooled re-enable accepts wake");
        foreach (int fps in new[] { 30, 60, 113, 144, 240 }) SchedulingProbe.Check(fps);
        SchedulingProbe.CheckStagedOrder();
        CheckDropContention();
        Console.WriteLine($"PASS: {count} robot-arm demand/wake/tick checks. No engine launched.");
    }

    private static void CheckDropContention()
    {
        var arm = new RobotArm { heldItemId = 1, state = RobotArm.RobotArmState.WaitingForDrop, dropRetryTimer = 0.1f, waitingForDropRetry = true };
        arm.Sleep();
        for (int i = 0; i < 1000; i++) { arm.NotifyCoordinate(); arm.NotifyModule(); }
        Require(arm.Sleeping && arm.DropAnimations == 0 && arm.TransferAttempts == 0, "full belt changes preserve sleep without drop attempts");
        Require(arm.dropRetryTimer == 0.1f && arm.waitingForDropRetry, "full belt changes preserve retry state");

        arm.Output.Available = true;
        arm.NotifyCoordinate();
        Require(!arm.Sleeping, "real gap wakes held arm");
        arm.PlanDrop();
        Require(arm.DropAnimations == 0 && arm.heldItemId == 1 && arm.Output.Items == 0, "plan never animates or mutates inventory");
        arm.ApplyDrop();
        Require(arm.Output.Items == 1 && arm.heldItemId == -1 && arm.DropAnimations == 1, "gap claimed in same tick before drop animation");
        Require(arm.state == RobotArm.RobotArmState.WaitingAfterDropPlace && Math.Abs(arm.actionTurnTimer - 0.2f) < 0.0001f, "successful drop retains combined action recovery budget");
        arm.ApplyDrop();
        Require(arm.Output.Items == 1 && arm.DropAnimations == 1, "repeated apply cannot duplicate items or animation");

        var shared = new RobotArm.Destination { Available = true };
        var first = new RobotArm { heldItemId = 1, state = RobotArm.RobotArmState.WaitingForDrop, Output = shared };
        var second = new RobotArm { heldItemId = 2, state = RobotArm.RobotArmState.WaitingForDrop, Output = shared };
        first.PlanDrop(); second.PlanDrop(); first.ApplyDrop(); second.ApplyDrop();
        Require(shared.Items == 1 && first.DropAnimations == 1 && second.DropAnimations == 0, "two arms competing for one gap animate only the successful transfer");
        Require(second.heldItemId == 2 && second.Sleeping, "losing arm retains item and sleeps immediately");
        shared.Available = true;
        second.NotifyCoordinate(); second.PlanDrop(); second.ApplyDrop();
        Require(shared.Items == 2 && second.heldItemId == -1 && second.DropAnimations == 1, "losing arm resumes at next genuine gap");

        var trainArm = new RobotArm { heldItemId = 1, state = RobotArm.RobotArmState.WaitingForDrop, MovingFreight = true };
        trainArm.Sleep(); trainArm.NotifyCoordinate();
        Require(!trainArm.Sleeping, "moving freight retains wake path for stop detection");
        var restored = new RobotArm { heldItemId = 1, state = RobotArm.RobotArmState.WaitingBeforeDropPlace, actionTurnTimer = 0.1f };
        restored.Normalize();
        Require(restored.state == RobotArm.RobotArmState.WaitingForDrop && restored.heldItemId == 1 && restored.actionTurnTimer == 0f, "legacy pre-drop saves restore to idle held-item state");
    }
}
