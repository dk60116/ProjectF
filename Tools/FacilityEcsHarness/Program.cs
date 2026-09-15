using System;
using System.Collections.Generic;

int checks = 0;
void Require(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}

var log = new List<string>();
var late = new PowerProbe(20, log);
var early = new PowerProbe(10, log);
FacilitySimulationWorld.Register(late, true);
FacilitySimulationWorld.Register(early, true);
Require(MapObjectTickManager.RegisteredCount == 1,
    "many facilities collapse to one global clock target");
MapObjectTickManager.Step();
Require(log.Count == 0, "SimulationId phases defer the first interval batch");
MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20",
    "SimulationId phase executes the first facility bucket");
MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20",
    "adjacent simulation tick does not repeat an interval bucket");
MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20,P10,A10",
    "different SimulationId phases distribute facilities across ticks");
Require(UtilityPole.PrepareCalls == 2,
    "power topology is prepared once for each due facility bucket");
Require(UtilityPole.MutationBatchDepth == 0,
    "facility Apply closes the power mutation batch after every due bucket");
Require(Math.Abs(early.LastDelta - 4f / 60f) < 0.000001f,
    "first phase execution preserves elapsed simulation time");

log.Clear();
for (int i = 0; i < 3; i++) MapObjectTickManager.Step();
Require(log.Count == 0, "0.1 second facilities sleep inside their interval bucket");
MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20"
        && Math.Abs(late.LastDelta - 0.1f) < 0.000001f,
    "interval execution preserves elapsed simulation time");
MapObjectTickManager.Step();
MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20,P10,A10"
        && Math.Abs(early.LastDelta - 0.1f) < 0.000001f,
    "facility phases remain separated on later intervals");
for (int i = 0; i < 4; i++) MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20,P10,A10,P20,A20",
    "each stable phase repeats at its configured interval");

FacilitySimulationWorld.SetScheduled(early, false);
log.Clear();
for (int i = 0; i < 6; i++) MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20",
    "sleeping entity leaves the due set without removing its registration");
FacilitySimulationWorld.SetScheduled(early, true);
for (int i = 0; i < 3; i++) MapObjectTickManager.Step();
Require(log[^2] == "P10" && log[^1] == "A10",
    "wake returns the entity to its stable SimulationId phase");

FacilitySimulationWorld.Unregister(early);
FacilitySimulationWorld.Unregister(late);
Require(MapObjectTickManager.RegisteredCount == 0,
    "empty facility world releases its global clock target");

var restored = new PowerProbe(25, log);
FacilitySimulationWorld.Register(restored, true);
MapObjectTickManager.Restore(1_000_000);
for (int i = 0; i < 3; i++) MapObjectTickManager.Step();
Require(Math.Abs(restored.LastDelta - 3f / 60f) < 0.000001f,
    "restoring a large world tick cannot fast-forward facility elapsed time");
FacilitySimulationWorld.Unregister(restored);

UtilityPole.PrepareCalls = 0;
var fluidOnly = new FluidProbe();
FacilitySimulationWorld.Register(fluidOnly, true);
MapObjectTickManager.Step();
Require(fluidOnly.Calls == 1 && UtilityPole.PrepareCalls == 0,
    "fluid-only batch does not evaluate an unrelated power network");
for (int i = 0; i < 6; i++) MapObjectTickManager.Step();
Require(fluidOnly.Calls == 1 && MapObjectTickManager.RegisteredCount == 0,
    "direct fluid target can sleep itself and release the outer tick");
FacilitySimulationWorld.Unregister(fluidOnly);

var flowProbe = new FlowProbe();
FacilitySimulationWorld.Register(flowProbe, true);
for (int i = 0; i < 6 && flowProbe.ApplyCalls == 0; i++) MapObjectTickManager.Step();
Require(flowProbe.CaptureCalls == 1
        && flowProbe.ApplyCalls == 1
        && Math.Abs(flowProbe.RequestedLiters - flowProbe.CapturedDelta * 60f) < .0001f,
    "facility world routes data adapters through one SoA plan before main-thread Apply");
FacilitySimulationWorld.Unregister(flowProbe);

var steamFlow = new ProjectF.Simulation.FacilityFlowBatch(1);
int steamIndex = steamFlow.ReserveSlot();
steamFlow.ConfigureSteamGenerator(steamIndex, 2, 10f, 0.1f, true, 1f);
steamFlow.PlanAll();
Require(Math.Abs(steamFlow.GetSteamRequestedLiters(steamIndex) - 1f) < .0001f
        && Math.Abs(steamFlow.GetSteamRequiredLiters(steamIndex) - 1f) < .0001f
        && steamFlow.GetSteamMissingLiters(steamIndex) < .0001f,
    "one scheduled interval of steam starts a generator without an artificial reserve");
steamFlow.Begin();
steamIndex = steamFlow.ReserveSlot();
steamFlow.ConfigureSteamGenerator(steamIndex, 2, 10f, 0.1f, true, 0.25f);
steamFlow.PlanAll();
Require(Math.Abs(steamFlow.GetSteamMissingLiters(steamIndex) - 0.75f) < .0001f,
    "generator requests only the exact missing steam for its scheduled interval");

var schedulerProbes = new List<ScheduleProbe>(600);
for (int i = 0; i < 600; i++)
{
    var probe = new ScheduleProbe(i);
    schedulerProbes.Add(probe);
    FacilitySimulationWorld.Register(probe, true);
}
MapObjectTickManager.Step();
FacilitySimulationWorld.AppendProfilerCounters();
Require(MapObjectTickProfiler.RuntimeCounters["FacilityECS/ScheduledEntities"] == 600
        && MapObjectTickProfiler.RuntimeCounters["FacilityECS/LastScheduleCandidates"] == 100
        && MapObjectTickProfiler.RuntimeCounters["FacilityECS/LastDueEntities"] == 100,
    "timing buckets inspect only the current SimulationId phase instead of all facilities");
for (int i = 0; i < 12; i++) MapObjectTickManager.Step();
long schedulerAllocated = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 120; i++) MapObjectTickManager.Step();
Require(GC.GetAllocatedBytesForCurrentThread() == schedulerAllocated,
    "steady-state timing buckets allocate no managed memory");
for (int i = 0; i < schedulerProbes.Count; i++)
    FacilitySimulationWorld.Unregister(schedulerProbes[i]);

var wakeCoordinate = new UnityEngine.Vector2Int(7, 11);
var wakeProbe = new WakeProbe();
FacilityRuntimeWakeRegistry.Register(wakeProbe, new[] { wakeCoordinate });
FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(wakeCoordinate);
Require(wakeProbe.WakeCalls == 1 && InputOutputModule.CoordinateWakeCalls == 1,
    "coordinate mutation wakes nearby sleeping facilities and input/output modules");
wakeProbe.Active = false;
FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(wakeCoordinate);
Require(wakeProbe.WakeCalls == 1,
    "inactive coordinate targets are not woken");
wakeProbe.Active = true;
FacilityRuntimeWakeRegistry.Unregister(wakeProbe);
FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(wakeCoordinate);
Require(wakeProbe.WakeCalls == 1,
    "unregistered coordinate targets stay asleep");

Console.WriteLine($"PASS: {checks} facility ECS scheduling checks.");

sealed class PowerProbe : InputOutputModule,
    IMapObjectStagedUpdateTick,
    IMapObjectUpdateTickInterval,
    IMapObjectSimulationIdentity
{
    private readonly List<string> log;
    public PowerProbe(long id, List<string> log) { SimulationId = id; this.log = log; }
    public long SimulationId { get; }
    public float ManagedUpdateTickIntervalSeconds => 0.1f;
    public float LastDelta { get; private set; }
    public override void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }
    public void PlanManagedUpdateTick(float deltaTime)
    {
        LastDelta = deltaTime;
        log.Add($"P{SimulationId}");
    }
    public void ApplyManagedUpdateTick() => log.Add($"A{SimulationId}");
}

sealed class FluidProbe : IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IMapObjectSimulationIdentity
{
    public long SimulationId => 32;
    public float ManagedUpdateTickIntervalSeconds => 0.1f;
    public int Calls { get; private set; }
    public void ManagedUpdateTick(float deltaTime)
    {
        Calls++;
        FacilitySimulationWorld.SetScheduled(this, false);
    }
}

sealed class WakeProbe : IFacilityRuntimeWakeTarget
{
    public bool Active { get; set; } = true;
    public int WakeCalls { get; private set; }
    public bool IsFacilityRuntimeWakeTargetActive => Active;
    public void WakeFacilityRuntimeTick() => WakeCalls++;
}

sealed class ScheduleProbe : IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IMapObjectSimulationIdentity
{
    public ScheduleProbe(long simulationId) => SimulationId = simulationId;
    public long SimulationId { get; }
    public float ManagedUpdateTickIntervalSeconds => 0.1f;
    public void ManagedUpdateTick(float deltaTime) { }
}

sealed class FlowProbe : InputOutputModule,
    IMapObjectStagedUpdateTick,
    IMapObjectUpdateTickInterval,
    IMapObjectSimulationIdentity,
    ProjectF.Simulation.IFacilityFlowAdapter
{
    public long SimulationId => 5;
    public float ManagedUpdateTickIntervalSeconds => 0.1f;
    public int CaptureCalls { get; private set; }
    public int ApplyCalls { get; private set; }
    public float CapturedDelta { get; private set; }
    public float RequestedLiters { get; private set; }
    public override void ManagedUpdateTick(float deltaTime) => throw new Exception("staged flow used direct tick");
    public void PlanManagedUpdateTick(float deltaTime) { }
    public void ApplyManagedUpdateTick() => throw new Exception("flow adapter bypassed SoA apply");
    public void CaptureFacilityFlow(
        ProjectF.Simulation.FacilityFlowBatch batch,
        int index,
        float deltaTime,
        long simulationTick)
    {
        CaptureCalls++;
        CapturedDelta = deltaTime;
        batch.ConfigurePump(index, 1, 60, deltaTime, simulationTick, true, 0, 0, -1);
    }
    public void ApplyFacilityFlow(ProjectF.Simulation.FacilityFlowBatch batch, int index)
    {
        ApplyCalls++;
        RequestedLiters = batch.GetPumpRequestedLiters(index);
    }
}
