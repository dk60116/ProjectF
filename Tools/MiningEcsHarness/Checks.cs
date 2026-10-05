using System;
using UnityEngine;
using ProjectF.Simulation;
using ProjectF.MapObjects;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
InputOutputModule.Items[1] = new ItemDefinition { id = 1 };
var input = new Vector2Int(2, 3); var output = new Vector2Int(2, 4);
(MiningMachineInstance miner, MiningWorld world, ResourceInstance resource, Block block) Setup(int batch = 1)
{
    MapObjectTickManager.CurrentSimulationTick = 0; UtilityPole.Ratio = 1;
    ResourceInstance.All.Clear(); VirtualObjectWorld.Current = new(); FacilitySimulationWorld.Scheduled.Clear();
    InputOutputModule.SuccessfulEmitsBeforeFailure = int.MaxValue;
    var world = new MiningWorld();
    var resource = new ResourceInstance { Batch = batch };
    ResourceInstance.All[input] = resource; VirtualObjectWorld.Current.Resources[input] = new MapObjectHandle(1);
    var state = new BlockStateStore.InstallationSaveState { anchorCoordinate = input, occupiedCoordinates = new() { input } };
    state.inputOutputState.outputCoordinates.Add(output);
    var miner = new MiningMachineInstance(world, 0, 1, new MapObjectHandle(100), new MiningMachine(), state, new MiningRenderTemplate());
    var block = new Block(); world.Terrain.Blocks[output] = block;
    miner.Wake(); miner.ManagedUpdateTick(0);
    return (miner, world, resource, block);
}
void Step(MiningMachineInstance miner, long tick) { MapObjectTickManager.CurrentSimulationTick = tick; miner.ManagedUpdateTick(0); }

var a = Setup();
Check(a.miner.SceneObject == null, "electric miner has no scene component");
Check(a.miner.NextUpdateTick == 120, "60 energy at 30 watts completes at tick 120");
Check(a.resource.Harvests == 0 && a.block.Count == 0, "starting production never consumes resources");
MapObjectTickManager.CurrentSimulationTick = 60;
Check(Math.Abs(a.miner.WorkProgress - .5f) < .00001f, "HUD snapshot computes half progress without a production tick");
Check(a.world.Value.Clock.Production.ConsumedEnergyUnits == 0, "HUD sampling does not mutate production");
Step(a.miner, 120);
Check(a.resource.Harvests == 1 && a.block.Count == 1, "one completed cycle harvests and emits exactly once");
Check(a.miner.NextUpdateTick == 240, "next cycle reserves its own completion deadline");

a = Setup(); a.block.Count = 10; a.block.Item = 1; Step(a.miner, 120);
Check(a.resource.Harvests == 0 && a.world.Value.Clock.Production.WaitingForOutput, "full output waits without consuming resource");
Check(!FacilitySimulationWorld.Scheduled.Contains(a.miner), "blocked output sleeps");
a.block.Count = 0; MapObjectTickManager.CurrentSimulationTick = 180; a.miner.Wake(); Step(a.miner, 181);
Check(a.resource.Harvests == 1 && a.block.Count == 1, "output vacancy resumes the completed cycle");

a = Setup(3); InputOutputModule.SuccessfulEmitsBeforeFailure = 1; Step(a.miner, 120);
Check(a.resource.Harvests == 1 && a.block.Count == 1 && a.world.Value.PendingHarvestedItems == 2, "partial emit retains remaining harvested products");
a.miner.Persist(); var saved = a.miner.Placement;
Check(saved.inputOutputState.miningPendingHarvestedItems == 2, "save snapshot includes harvested output retry");
var restoredWorld = new MiningWorld { Terrain = a.world.Terrain, Store = a.world.Store };
var restored = new MiningMachineInstance(restoredWorld, 0, 1, new MapObjectHandle(100), a.miner.Prototype, saved, new MiningRenderTemplate());
InputOutputModule.SuccessfulEmitsBeforeFailure = int.MaxValue; restored.Wake(); Step(restored, 121);
Check(a.resource.Harvests == 1 && a.block.Count == 3, "save restore retries pending output without harvesting twice");

a = Setup(); MapObjectTickManager.CurrentSimulationTick = 60; UtilityPole.Ratio = 0;
a.miner.Wake(); Step(a.miner, 60);
Check(a.world.Value.Clock.Production.ConsumedEnergyUnits == DeterministicSimulationUnits.FromInt(30), "power transition integrates the previous rate exactly");
Check(!FacilitySimulationWorld.Scheduled.Contains(a.miner), "no power sleeps");
MapObjectTickManager.CurrentSimulationTick = 600; UtilityPole.Ratio = .5f; a.miner.Wake(); Step(a.miner, 600);
Check(a.miner.NextUpdateTick == 720, "power outage adds no progress; half power doubles remaining duration");
Step(a.miner, 720); Check(a.resource.Harvests == 1, "power resumes retained progress");

a = Setup(); a.resource.Reserves = 1; Step(a.miner, 120);
Check(a.resource.Reserves == 0 && !a.miner.TryGetElectricPowerDemand(out _), "last resource disables standby power demand");
Check(!FacilitySimulationWorld.Scheduled.Contains(a.miner), "depleted miner sleeps");

a = Setup(); a.world.Terrain.Blocks.Remove(output); Step(a.miner, 120);
Check(a.world.Store.GetSavedCenterItemCount(output) == 1, "unloaded ground receives saved output");
a = Setup(); a.world.Terrain.Blocks.Remove(output);
InputOutputModule.Items[2] = new ItemDefinition { id = 2, mapObject = new ConveyorBelt() };
a.world.Store.Installed[output] = new BlockStateStore.InstallationSaveState { itemId = 2, anchorCoordinate = output };
Step(a.miner, 120);
Check(a.resource.Harvests == 0 && a.world.Store.GetSavedCenterItemCount(output) == 0, "unloaded belt never receives an unrelated floor stack");

a = Setup(); a.block.IsRuntimeConveyor = true; a.block.Count = 10; Step(a.miner, 120);
Check(a.block.BoundaryRequests > 0 && a.resource.Harvests == 0, "blocked belt output exposes a wake boundary");
a = Setup(); a.miner.SetItemFilterEnabled(1, 3, false); Step(a.miner, 120);
Check(!a.miner.TryGetElectricPowerDemand(out _), "filtered resource is not an operating target");

a = Setup(2); InputOutputModule.Items[1].oneItem = true;
var second = new Vector2Int(3, 4); a.miner.Placement.inputOutputState.outputCoordinates.Add(second);
a.world.Terrain.Blocks[second] = new Block(); Step(a.miner, 120);
Check(a.block.Count == 1 && a.world.Terrain.Blocks[second].Count == 1, "single-item products distribute across output coordinates");
InputOutputModule.Items[1].oneItem = false;

a = Setup(); a.world.Alive = false; a.miner.ManagedUpdateTick(0);
Check(a.resource.Harvests == 0 && !a.miner.IsRuntimeActive, "removed generation cannot produce");
a = Setup(); MapObjectTickManager.CurrentSimulationTick = 60;
a.world.Store.Resources[input] = new Resource.ResourceSaveState { resourceCount = a.resource.Reserves };
ResourceInstance.All.Remove(input); a.miner.Wake(); Step(a.miner, 61);
Check(a.world.Value.Clock.Production.Active && a.resource.Harvests == 0 && !a.miner.TryGetElectricPowerDemand(out _),
    "chunk unload preserves unfinished mining and suspends power demand");
ResourceInstance.All[input] = a.resource; MapObjectTickManager.CurrentSimulationTick = 600; a.miner.Wake(); Step(a.miner, 600);
Step(a.miner, a.miner.NextUpdateTick);
Check(a.resource.Harvests == 1, "chunk reload resumes unfinished mining");

var dto = new InputOutputModule.PersistentState { hasActiveCraft = true, activeOutputItemId = 1, activeOutputCount = 3,
    activeCraftConsumedEnergyUnits = 123456789, miningPendingHarvestedItems = 2, miningResourceCursor = 3,
    miningResourceCoordinate = new Vector2Int(-42, 37) };
var bytes = MiningSaveProbe.Encode(dto);
var roundtrip = MiningSaveProbe.Decode(bytes, 70);
Check(roundtrip.miningPendingHarvestedItems == 2 && roundtrip.miningResourceCursor == 3
    && roundtrip.miningResourceCoordinate == dto.miningResourceCoordinate, "v70 binary roundtrip retains miner selection and pending output");
Check(roundtrip.activeCraftConsumedEnergyUnits == 123456789, "v70 binary roundtrip retains exact production energy");
Array.Resize(ref bytes, bytes.Length - 16); // v69 lacks the appended three mining fields.
var legacy = MiningSaveProbe.Decode(bytes, 69);
Check(legacy.miningPendingHarvestedItems == 0 && legacy.miningResourceCursor == 0, "v69 binary input state defaults new mining fields");
var clone = dto.Clone();
Check(clone.miningResourceCoordinate == dto.miningResourceCoordinate && clone.miningPendingHarvestedItems == 2,
    "installation cloning preserves data mining state");
clone.ClearStoredEnergyAndProduction();
Check(clone.miningPendingHarvestedItems == 0 && !clone.hasActiveCraft, "clear items removes pending mining output");
a = Setup();
var powerNetwork = PowerDemandProbe.Attach(a.miner);
PowerDemandProbe.InvalidateDataConsumerDemand(a.miner);
Check(PowerDemandProbe.Demand(powerNetwork) == 30 && PowerDemandProbe.WakeQueued(powerNetwork),
    "mining demand increase wakes its supplied network so other facilities update their rates");
int wakeBatches = PowerDemandProbe.WakeBatches;
PowerDemandProbe.InvalidateDataConsumerDemand(a.miner);
Check(PowerDemandProbe.WakeBatches == wakeBatches, "unchanged demand does not trigger redundant network wakes");
a.world.Value.HasTarget = false; PowerDemandProbe.InvalidateDataConsumerDemand(a.miner);
Check(PowerDemandProbe.Demand(powerNetwork) == 0 && PowerDemandProbe.WakeBatches == wakeBatches + 1,
    "mining target loss removes demand and wakes other network consumers");
a = Setup(3); a.block.Count = 1000; a.block.Item = 1;
InputOutputModule.Items[1].oneItem = true; ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = true;
Step(a.miner, 120);
Check(a.block.Count == 1003 && a.resource.Harvests == 0, "forced miner appends a single-item batch to its full output without spilling or consuming resources");
var extraOutput = new Vector2Int(3, 4); a.miner.Placement.inputOutputState.outputCoordinates.Add(extraOutput);
a.world.Terrain.Blocks[extraOutput] = new Block();
a.miner.GetOutputInfo(out _, out int infoCount, out int infoCapacity);
Check(infoCount == 1003 && infoCapacity == int.MaxValue, "multiple unlimited output coordinates cannot overflow HUD capacity");
a.world.Terrain.Blocks.Remove(output); a.world.Terrain.Blocks.Remove(extraOutput);
Check(MiningItemOutput.TryReserve(a.miner, 1, 3, out var unlimitedSaved) && unlimitedSaved.Saved && unlimitedSaved.Capacity == int.MaxValue,
    "unloaded output reservation also bypasses forced single-item limits");
a.miner.Placement.inputOutputState.outputCoordinates.Remove(extraOutput);
InputOutputModule.Items[3] = new ItemDefinition { id = 3, capacity = 10, mapObject = new BoxObject() };
a.world.Store.Installed[output] = new BlockStateStore.InstallationSaveState { itemId = 3, anchorCoordinate = output };
Check(!MiningItemOutput.TryReserve(a.miner, 1, 3, out _), "forced mining keeps the authored limits of an unloaded container");
ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = false; InputOutputModule.Items[1].oneItem = false;
Console.WriteLine($"PASS {checks} mining ECS production/output/save/power checks (actual sources, engine boundary doubles)");
MiningOwnershipChecks.Run();
MiningGaugeChecks.Run();

MiningFuelChecks.Run(args[0]);
