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
Require(string.Join(",", log) == "P10,P20,A10,A20",
    "facility plan/apply uses stable entity order");
Require(UtilityPole.PrepareCalls == 1,
    "power topology is prepared once for the due facility batch");
Require(Math.Abs(early.LastDelta - 1f / 60f) < 0.000001f,
    "newly woken facility receives one fixed tick first");

log.Clear();
for (int i = 0; i < 5; i++) MapObjectTickManager.Step();
Require(log.Count == 0, "0.1 second facilities sleep inside their interval bucket");
MapObjectTickManager.Step();
Require(string.Join(",", log) == "P10,P20,A10,A20"
        && Math.Abs(early.LastDelta - 0.1f) < 0.000001f,
    "interval execution preserves elapsed simulation time");

FacilitySimulationWorld.SetScheduled(early, false);
log.Clear();
for (int i = 0; i < 6; i++) MapObjectTickManager.Step();
Require(string.Join(",", log) == "P20,A20",
    "sleeping entity leaves the due set without removing its registration");
FacilitySimulationWorld.SetScheduled(early, true);
MapObjectTickManager.Step();
Require(log[^2] == "P10" && log[^1] == "A10",
    "wake resets the entity schedule and executes on the next fixed tick");

FacilitySimulationWorld.Unregister(early);
FacilitySimulationWorld.Unregister(late);
Require(MapObjectTickManager.RegisteredCount == 0,
    "empty facility world releases its global clock target");

var restored = new PowerProbe(25, log);
FacilitySimulationWorld.Register(restored, true);
MapObjectTickManager.Restore(1_000_000);
MapObjectTickManager.Step();
Require(Math.Abs(restored.LastDelta - 1f / 60f) < 0.000001f,
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
    public long SimulationId => 30;
    public float ManagedUpdateTickIntervalSeconds => 0.1f;
    public int Calls { get; private set; }
    public void ManagedUpdateTick(float deltaTime)
    {
        Calls++;
        FacilitySimulationWorld.SetScheduled(this, false);
    }
}
