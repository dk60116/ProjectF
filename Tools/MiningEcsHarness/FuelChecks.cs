using System;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Simulation;

internal static class MiningFuelChecks
{
    internal static void Run(string repo)
    {
        int checks = 0;
        var burnSettings = MiningAssetSettings.Read(repo, "Item_28_Mining machine.asset", 1);
        var electricSettings = MiningAssetSettings.Read(repo, "Item_29_Electric mining machine.asset", 2);
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var resourceCoordinate = new Vector2Int(0, 0);
        var energyCoordinate = new Vector2Int(1, 0);
        var outputCoordinate = new Vector2Int(2, 0);
        InputOutputModule.Items[7] = new ItemDefinition { id = 7, energyType = ItemDefinition.EnergyType.Burn, energyAmount = 3 };
        (MiningMachineInstance miner, MiningWorld world, Block fuel, Block output, ResourceInstance resource) Setup(int fuelCount = 10)
        {
            MapObjectTickManager.CurrentSimulationTick = 0; UtilityPole.Ratio = 0;
            ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = false;
            FacilitySimulationWorld.Scheduled.Clear(); ResourceInstance.All.Clear(); VirtualObjectWorld.Current = new();
            var world = new MiningWorld();
            var definition = new ItemDefinition { id = 59 };
            definition.Requirements.Add(new() { energyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = burnSettings.rate });
            var template = new MiningRenderTemplate { Definition = definition, Watts = 0, WorkRate = burnSettings.rate,
                WorkUnitsPerTick = DeterministicSimulationUnits.RateForTicks(burnSettings.rate, 1),
                CompleteEnergy = burnSettings.energy, EnergyType = ItemDefinition.EnergyType.Burn };
            var placement = new BlockStateStore.InstallationSaveState { itemId = 59, occupiedCoordinates = new() { resourceCoordinate } };
            placement.inputOutputState.inputEnergyCoordinates.Add(energyCoordinate);
            placement.inputOutputState.outputCoordinates.Add(outputCoordinate);
            var fuel = new Block { Item = 7, Count = fuelCount }; var output = new Block();
            world.Terrain.Blocks[energyCoordinate] = fuel; world.Terrain.Blocks[outputCoordinate] = output;
            var resource = new ResourceInstance { Reserves = 100 };
            ResourceInstance.All[resourceCoordinate] = resource; VirtualObjectWorld.Current.Resources[resourceCoordinate] = new(1);
            var miner = new MiningMachineInstance(world, 0, 1, new(100), new MiningMachine(), placement, template);
            fuel.Mutation = miner.Wake; // Real IO mutation wakes its own owner synchronously.
            miner.Wake(); miner.ManagedUpdateTick(0);
            return (miner, world, fuel, output, resource);
        }
        void Step(MiningMachineInstance miner, long tick) { MapObjectTickManager.CurrentSimulationTick = tick; miner.ManagedUpdateTick(0); }
        var a = Setup();
        Check(MiningWorld.Supports(new MiningMachine { BoundItemDefinition = a.miner.Template.Definition }), "burn definition is accepted by actual ECS registration gate");
        var unsupported = new ItemDefinition(); unsupported.Requirements.Add(new() { energyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = 0 });
        Check(!MiningWorld.Supports(new MiningMachine { BoundItemDefinition = unsupported }), "zero-rate definition stays out of ECS");
        MiningWorld.Current = a.world; a.world.ObserveFuel(a.miner);
        var accepted = new System.Collections.Generic.HashSet<ItemDefinition.EnergyType>();
        Check(InputOutputModuleEnergyAreaController.TryGetAcceptedEnergyTypes(energyCoordinate, accepted)
            && accepted.Contains(ItemDefinition.EnergyType.Burn), "ECS energy port publishes accepted fuel type");
        Check(InputOutputModuleEnergyAreaController.CoordinateAcceptsEnergyType(energyCoordinate, ItemDefinition.EnergyType.Burn)
            && !InputOutputModuleEnergyAreaController.CoordinateAcceptsEnergyType(energyCoordinate, ItemDefinition.EnergyType.Electricity), "robot and player fuel acceptance follows miner requirement");
        Check(InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(energyCoordinate)
            && InputOutputModuleEnergyAreaController.CoordinateBlocksInstallationPlacement(energyCoordinate), "data fuel port remains interactive and blocks overlapping placement");
        Check(a.miner.IsWorking && a.fuel.Consumed == 1, "burn miner starts from fuel with no electricity");
        Check(!a.miner.TryGetElectricPowerDemand(out _) && !a.miner.RequiresFacilityPowerEvaluation, "burn miner never participates in power evaluation");
        Check(a.miner.NextUpdateTick == 120, "schedule fuel depletion before 600 tick work completion");
        MapObjectTickManager.CurrentSimulationTick = 60;
        a.miner.GetFuelGauge(out long current, out long capacity);
        Check(current == DeterministicSimulationUnits.FromFloat(1.5f) && capacity == DeterministicSimulationUnits.FromInt(3), "fuel gauge snapshots remaining energy without consuming");
        Check(Math.Abs(a.miner.WorkProgress - .1f) < .00001f && a.fuel.Consumed == 1, "work gauge uses burn rate and stays read only");
        for (int tick = 120; tick <= 600; tick += 120) Step(a.miner, tick);
        Check(a.resource.Harvests == 1 && a.output.Count == 1, "15 energy at 1.5 per second produces one batch in 600 ticks");
        Check(a.fuel.Consumed == 6, "five fuel items power completed work and one prepares next cycle");
        Check(!a.world.Value.NeedsEvaluation, "self fuel consumption does not recursively schedule updates");

        a = Setup(1); Step(a.miner, 120);
        Check(!a.miner.IsWorking && !FacilitySimulationWorld.Scheduled.Contains(a.miner), "empty fuel sleeps");
        Check(a.world.Value.Clock.Production.ConsumedEnergyUnits == DeterministicSimulationUnits.FromInt(3), "fuel exhaustion retains exact progress");
        a.fuel.Count = 4; MapObjectTickManager.CurrentSimulationTick = 1000; a.miner.Wake(); Step(a.miner, 1000);
        for (int tick = 1120; tick <= 1480; tick += 120) Step(a.miner, tick);
        Check(a.resource.Harvests == 1, "fuel refill resumes without progressing during outage");

        InputOutputModule.Items[7].energyAmount = 1.51f;
        a = Setup(1); Step(a.miner, 61);
        Check(a.world.Value.Clock.Production.ConsumedEnergyUnits == DeterministicSimulationUnits.FromFloat(1.51f), "partial final fuel tick advances only supplied energy");
        Check(!a.miner.IsWorking && a.resource.Harvests == 0, "partial fuel cannot create free output");
        InputOutputModule.Items[7].energyAmount = 3;

        a = Setup(); a.output.Item = 1; a.output.Count = 10;
        Step(a.miner, 600);
        int consumed = a.fuel.Consumed;
        Check(consumed == 5 && a.resource.Harvests == 0, "blocked output reserves before harvesting and does not preload fuel");
        Step(a.miner, 10000);
        Check(a.fuel.Consumed == consumed, "waiting output does not burn standby fuel");
        a.output.Count = 0; a.miner.Wake(); Step(a.miner, 10000);
        Check(a.resource.Harvests == 1 && a.fuel.Consumed == 6, "output vacancy emits and prepares next cycle");

        a = Setup(); MapObjectTickManager.CurrentSimulationTick = 60; a.miner.Persist();
        var saved = MiningSaveProbe.Decode(MiningSaveProbe.Encode(a.miner.Placement.inputOutputState), 70);
        Check(saved.activeCraftConsumedEnergyUnits == DeterministicSimulationUnits.FromFloat(1.5f)
            && saved.storedEnergyUnitsByType[0] == DeterministicSimulationUnits.FromFloat(1.5f), "binary save preserves work and remaining fuel exactly");
        a.miner.Placement.inputOutputState = saved;
        var restoredWorld = new MiningWorld { Terrain = a.world.Terrain, Store = a.world.Store };
        restoredWorld.Value.Clock.SampleTick = 500;
        MapObjectTickManager.CurrentSimulationTick = 500;
        var restored = new MiningMachineInstance(restoredWorld, 0, 1, new(100), a.miner.Prototype, a.miner.Placement, a.miner.Template);
        restored.Wake(); Step(restored, 500); Step(restored, 1040);
        Check(a.resource.Harvests == 1, "save restore excludes unloaded elapsed time");

        a = Setup(0); var io = a.miner.Placement.inputOutputState;
        io.storedEnergy = 7; io.energyGaugeCapacity = 10;
        var legacyWorld = new MiningWorld { Terrain = a.world.Terrain, Store = a.world.Store };
        var legacy = new MiningMachineInstance(legacyWorld, 0, 1, new(100), a.miner.Prototype, a.miner.Placement, a.miner.Template);
        Check(io.storedEnergyTypes.Count == 1 && io.storedEnergyUnitsByType[0] == DeterministicSimulationUnits.FromInt(7), "legacy native fuel migrates once");

        a = Setup(0); a.world.Terrain.Blocks.Remove(energyCoordinate);
        a.world.Store.Saved[energyCoordinate] = new Block { Item = 7, Count = 2 };
        a.miner.Wake(); Step(a.miner, 0); Step(a.miner, 120);
        Check(a.world.Store.Saved[energyCoordinate].Count == 0 && a.miner.IsWorking, "unloaded saved fuel uses shared transport boundary");

        a = Setup(); MapObjectTickManager.CurrentSimulationTick = 60;
        ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = true; a.miner.Wake(); Step(a.miner, 60);
        long remainingFuel = a.miner.Placement.inputOutputState.storedEnergyUnitsByType[0]; consumed = a.fuel.Consumed;
        Step(a.miner, 600);
        Check(a.fuel.Consumed == consumed && a.miner.Placement.inputOutputState.storedEnergyUnitsByType[0] == remainingFuel
            && a.resource.Harvests == 0, "forced burn work never spends fuel or resources");
        MapObjectTickManager.CurrentSimulationTick = 660; ProjectF.Benchmark.BenchmarkRuntime.ForceWorking = false;
        a.miner.Wake(); Step(a.miner, 660);
        Check(a.miner.Placement.inputOutputState.storedEnergyUnitsByType[0] == remainingFuel, "force off samples previous interval without charging real fuel");
        Step(a.miner, 720);
        Check(a.miner.Placement.inputOutputState.storedEnergyUnitsByType[0] < remainingFuel || a.fuel.Consumed > consumed, "normal work resumes real fuel consumption");
        a.miner.ClearStoredEnergyAndProduction();
        Check(a.miner.Placement.inputOutputState.storedEnergyTypes.Count == 0 && !a.miner.HasActiveWork, "clear removes stored fuel and work before reevaluation");

        a = Setup(); a.resource.Reserves = 1; Step(a.miner, 600);
        Check(a.fuel.Consumed == 5 && !a.miner.IsWorking, "depleted resource does not preload another fuel item");
        a = Setup(); MapObjectTickManager.CurrentSimulationTick = 60;
        a.world.Store.Resources[resourceCoordinate] = new Resource.ResourceSaveState { resourceCount = 100 };
        ResourceInstance.All.Remove(resourceCoordinate); a.miner.Wake(); Step(a.miner, 60);
        long energy = a.world.Value.Clock.Production.ConsumedEnergyUnits;
        MapObjectTickManager.CurrentSimulationTick = 600; a.miner.Persist();
        Check(a.world.Value.Clock.Production.ConsumedEnergyUnits == energy && a.fuel.Consumed == 1, "streamed out resource suspends both progress and fuel");
        ResourceInstance.All[resourceCoordinate] = a.resource; a.miner.Wake(); Step(a.miner, 600); Step(a.miner, 1140);
        Check(a.resource.Harvests == 1, "chunk reload resumes burn mining");

        a = Setup();
        var electricDefinition = new ItemDefinition { id = 60 };
        electricDefinition.Requirements.Add(new() { energyType = ItemDefinition.EnergyType.Electricity, useEnergyAmount = electricSettings.rate });
        Check(MiningWorld.Supports(new MiningMachine { BoundItemDefinition = electricDefinition }), "electric definition remains supported");
        var electricWorld = new MiningWorld { Terrain = a.world.Terrain, Store = a.world.Store };
        var electric = new MiningMachineInstance(electricWorld, 0, 1, new(100), a.miner.Prototype, a.miner.Placement,
            new MiningRenderTemplate { Definition = electricDefinition, Watts = electricSettings.rate, WorkRate = electricSettings.rate,
                WorkUnitsPerTick = DeterministicSimulationUnits.RateForTicks(electricSettings.rate, 1), CompleteEnergy = electricSettings.energy });
        UtilityPole.Ratio = 1; electric.Wake(); Step(electric, 0);
        Check(electric.NextUpdateTick == 560, "authored electric rate 30 and energy 280 completes in 560 ticks");
        Step(electric, 560);
        Check(a.resource.Harvests == 1, "electric regression retains exact authored cycle duration");
        a = Setup();
        var random = new System.Random(13);
        int originalFuel = a.fuel.Consumed; long originalStoredFuel = a.miner.Placement.inputOutputState.storedEnergyUnitsByType[0];
        long earliest = long.MaxValue, latest = 0;
        for (int i = 0; i < 100; i++)
        {
            Check(a.miner.TryRandomizeWorkProgress(random), "active burn work randomizes");
            Step(a.miner, 0); // Resolve invalidation without elapsed time.
            Check(a.miner.WorkProgress >= 0 && a.miner.WorkProgress < 1, "random work progress is below completion");
            earliest = Math.Min(earliest, a.miner.NextUpdateTick); latest = Math.Max(latest, a.miner.NextUpdateTick);
        }
        Check(earliest < 30 && latest == 120, "completion/fuel deadlines follow randomized progress");
        Check(a.fuel.Consumed == originalFuel && a.miner.Placement.inputOutputState.storedEnergyUnitsByType[0] == originalStoredFuel
            && a.resource.Harvests == 0 && a.output.Count == 0, "randomization changes only work progress, not fuel/resource/output");
        a.world.Value.Clock.Production.WaitingForOutput = true; a.world.Value.PendingHarvestedItems = 1;
        Check(!a.miner.TryRandomizeWorkProgress(random) && a.world.Value.PendingHarvestedItems == 1, "pending harvested mining output is preserved");
        Console.WriteLine($"PASS {checks} burn mining fuel/deadline/save/forced checks (actual sources, engine boundary doubles)");
    }
}
