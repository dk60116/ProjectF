using System;
using System.Diagnostics;
using UnityEngine;
using ProjectF.MapObjects;

int checks = 0;
var settings = MiningAssetSettings.Read(args[0], "Item_28_Mining machine.asset", 1);
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
InputOutputModule.Items[1] = new ItemDefinition { id = 1 };
InputOutputModule.Items[7] = new ItemDefinition { id = 7, energyType = ItemDefinition.EnergyType.Burn, energyAmount = 30 };
foreach (int count in new[] { 1000, 10000, 100000 })
foreach (bool forced in new[] { false, true })
{
    MiningSchedulerHost.Reset();
    VirtualObjectWorld.Current = new(); ResourceInstance.All.Clear();
    ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = forced;
    InputOutputModule.SuccessfulEmitsBeforeFailure = int.MaxValue;
    var world = new MiningWorld(count);
    var definition = new ItemDefinition { id = 59 };
    definition.Requirements.Add(new() { energyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = settings.rate });
    var template = new MiningRenderTemplate { Definition = definition, Watts = 0, WorkRate = settings.rate,
        WorkUnitsPerTick = DeterministicSimulationUnits.RateForTicks(settings.rate, 1),
        CompleteEnergy = settings.energy, EnergyType = ItemDefinition.EnergyType.Burn };
    var prototype = new MiningMachine();
    var miners = new MiningMachineInstance[count];
    var watch = Stopwatch.StartNew();
    for (int i = 0; i < count; i++)
    {
        var coordinate = new Vector2Int(i * 3, 0); var output = new Vector2Int(i * 3 + 1, 0); var fuel = new Vector2Int(i * 3 + 2, 0);
        var placement = new BlockStateStore.InstallationSaveState { itemId = 59, placementSequence = i + 1, occupiedCoordinates = new() { coordinate } };
        placement.inputOutputState.inputEnergyCoordinates.Add(fuel); placement.inputOutputState.outputCoordinates.Add(output);
        ResourceInstance.All[coordinate] = new ResourceInstance { Reserves = 100 };
        VirtualObjectWorld.Current.Resources[coordinate] = new(1);
        world.Terrain.Blocks[output] = new Block { Capacity = 100 };
        world.Terrain.Blocks[fuel] = new Block { Item = 7, Count = 10 };
        miners[i] = new MiningMachineInstance(world, i, 1, new(100), prototype, placement, template);
        miners[i].Wake();
    }
    MiningSchedulerHost.Step(1);
    watch.Stop(); double creationMs = watch.Elapsed.TotalMilliseconds;
    Check(world.ProcessedUpdates == count && miners[0].NextUpdateTick == 601, "each entity starts once and reserves completion");
    for (long tick = 2; tick <= 601; tick++) MiningSchedulerHost.Step(tick); // Warm scheduling, completion and all buffers.
    Check(world.ProcessedUpdates == count * 2L, "600 tick interval has one completion update per entity");
    long beforeUpdates = world.ProcessedUpdates;
    long beforeGc = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
    for (long tick = 602; tick <= 1200; tick++) MiningSchedulerHost.Step(tick);
    watch.Stop(); double idleMs = watch.Elapsed.TotalMilliseconds / 599;
    long idleGc = GC.GetAllocatedBytesForCurrentThread() - beforeGc;
    Check(world.ProcessedUpdates == beforeUpdates && idleGc == 0, "between deadlines no entity is processed or allocated");
    beforeGc = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
    MiningSchedulerHost.Step(1201);
    watch.Stop(); double completionMs = watch.Elapsed.TotalMilliseconds;
    long completionGc = GC.GetAllocatedBytesForCurrentThread() - beforeGc;
    Check(world.ProcessedUpdates == beforeUpdates + count && completionGc == 0, "synchronized completion processes exactly due entities with no allocation");
    Check(UtilityPole.PowerPreparationCount == 0, "burn and forced entities skip electric snapshot preparation");
    Check(world.Terrain.Blocks[new Vector2Int(1, 0)].Count == 2, "two cycles emit twice");
    Check(ResourceInstance.All[new Vector2Int(0, 0)].Harvests == (forced ? 0 : 2), "normal mines resources; forced leaves them intact");
    Console.WriteLine($"MASS burn miners={count} forced={forced}: create+first evaluation={creationMs:F2} ms; idle tick={idleMs:F6} ms/{idleGc} B; synchronized completion={completionMs:F2} ms/{completionGc} B");
    var random = new System.Random(45);
    foreach (var miner in miners) Check(miner.TryRandomizeWorkProgress(random), "every ongoing mass cycle randomizes");
    MiningSchedulerHost.Step(1202);
    var deadlines = new System.Collections.Generic.HashSet<long>();
    foreach (var miner in miners) deadlines.Add(miner.NextUpdateTick);
    Check(deadlines.Count > 300, "randomized work spreads deadlines across hundreds of ticks");
    long maximumDue = 0;
    for (long tick = 1203; tick <= 1802; tick++)
    {
        beforeUpdates = world.ProcessedUpdates; MiningSchedulerHost.Step(tick);
        maximumDue = Math.Max(maximumDue, world.ProcessedUpdates - beforeUpdates);
    }
    Check(maximumDue < count / 8, "randomization removes synchronized completion waves");
    Console.WriteLine($"RANDOMIZED miners={count} forced={forced}: distinct deadlines={deadlines.Count}; peak due entities={maximumDue}");
    foreach (var miner in miners) FacilitySimulationWorld.Unregister(miner);
    MiningSchedulerHost.Reset();
}
ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = false;
Console.WriteLine($"PASS {checks} mining mass scheduler checks (actual scheduler/entity/fuel/output, engine boundaries doubled)");
