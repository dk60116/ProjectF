using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Simulation;

static class Checks
{
    private static int passed;
    private static long sequence;
    private static readonly Vector2Int Output = new(0, -1), Input = new(1, 0);
    static void Check(bool value, string label) { if (!value) throw new Exception(label); passed++; }
    static TerrainGenerator Reset()
    {
        ForestryWorld.Current?.Dispose();
        UtilityPole.SupplyRatio = 1; UtilityPole.EnergyCalls = UtilityPole.DemandChanges = 0;
        InputOutputModule.BlockInput = false;
        var terrain = new TerrainGenerator(); TerrainGenerator.Active = terrain;
        foreach (var c in new[] { Vector2Int.zero, Input, Output, new Vector2Int(0, 1), new Vector2Int(-1, 0) })
            terrain.Blocks[c] = new Block { Coordinate = c };
        return terrain;
    }
    static BlockStateStore.InstallationSaveState Placement(int item, Vector2Int anchor = default)
    {
        var state = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = anchor, itemId = item, worldPosition = new(anchor.x, 0, anchor.y),
            placementSequence = ++sequence, inputOutputState = new()
        };
        state.occupiedCoordinates.Add(anchor);
        state.inputOutputState.outputCoordinates.Add(Output);
        state.inputOutputState.inputItemAreas.Add(new InputOutputModule.Area { coordinate = Input, itemId = 2 });
        state.inputOutputState.gridCoordinates.Add(Output); state.inputOutputState.gridCoordinates.Add(Input);
        VirtualObjectWorld.Current.Register(anchor); return state;
    }
    static SeedPlanterInstance Planter(TerrainGenerator terrain, long progress = 0, bool loaded = true,
        long transfer = 0, Vector2Int anchor = default)
    {
        var state = Placement(4, anchor);
        var io = state.inputOutputState;
        io.seedPlanterPlantElapsedUnits = progress; io.seedPlanterHasLoadedSeed = loaded;
        io.seedPlanterLoadedSeedItemId = loaded ? 2 : -1; io.seedPlanterLoadedSeedInputCoordinate = Input;
        io.seedPlanterTransferRemainingUnits = transfer;
        return (SeedPlanterInstance)ForestryWorld.Ensure(terrain).Register(
            new SeedPlanter { BoundItemDefinition = InputOutputModule.Definitions[4] }, state);
    }
    static LoggingMachineInstance Logger(TerrainGenerator terrain, BlockStateStore.InstallationSaveState state = null)
    {
        state ??= Placement(3);
        VirtualObjectWorld.Current.Register(state.anchorCoordinate);
        return (LoggingMachineInstance)ForestryWorld.Ensure(terrain).Register(
            new LoggingMachine { BoundItemDefinition = InputOutputModule.Definitions[3] }, state);
    }
    static TreeInstance Tree(TerrainGenerator terrain, Vector2Int coordinate = default)
    {
        if (coordinate == default) coordinate = Output;
        var tree = new TreeInstance { OwningBlock = terrain.Blocks[coordinate] };
        terrain.Blocks[coordinate].Resource = tree; VirtualObjectWorld.Current.RegisterResource(coordinate); return tree;
    }
    static void Tick(ForestryInstance instance, int count = 1)
    { for (int i = 0; i < count; i++) instance.ManagedUpdateTick(.1f); }
    static void Main()
    {
        InputOutputModule.Definitions[1] = new() { id = 1 };
        InputOutputModule.Definitions[2] = new() { id = 2, isSeed = true };
        InputOutputModule.Definitions[3] = new() { id = 3, completeEnergy = 45000 };
        InputOutputModule.Definitions[4] = new() { id = 4 };
        GameManager.Instance.ItemManger.ItemDefinitions.AddRange(InputOutputModule.Definitions.Values);
        SeedChecks(); LoggingChecks(); RecoveryChecks(); LifetimeChecks(); ScaleChecks();
        Console.WriteLine($"{passed} forestry ECS checks passed. Managed boundaries; no editor launched.");
    }
    static void SeedChecks()
    {
        long duration = DeterministicSimulationUnits.FromFloat(5f);
        foreach (float supply in new[] { 1f, 0f })
        {
            var terrain = Reset(); var planter = Planter(terrain, duration); UtilityPole.SupplyRatio = supply;
            Tick(planter);
            Check(terrain.Plants == 1 && terrain.Animations == 1 && !planter.Placement.inputOutputState.seedPlanterHasLoadedSeed,
                "completed snapshot commits exactly once without extra power");
            Check(UtilityPole.EnergyCalls == 0, "completed snapshot consumes no extra energy");
            Tick(planter, 2); Check(terrain.Plants == 1, "completion cannot repeat");
        }
        foreach (long remaining in new[] { 1L, 5999L, 6000L })
        {
            var terrain = Reset(); var planter = Planter(terrain, duration - remaining); Tick(planter);
            Check(terrain.Plants == 1, "last integer units complete");
        }
        {
            var terrain = Reset(); var planter = Planter(terrain, duration - 1); UtilityPole.SupplyRatio = 0;
            Tick(planter);
            Check(planter.CurrentOperatingState == PlantingOperatingState.NoPower
                && planter.Placement.inputOutputState.seedPlanterPlantElapsedUnits == duration - 1
                && planter.Placement.inputOutputState.seedPlanterHasLoadedSeed, "outage preserves seed and work");
            Check(!FacilitySimulationWorld.Scheduled.Contains(planter), "outage sleeps");
            UtilityPole.SupplyRatio = 1; planter.WakeForElectricPowerChange(); Tick(planter);
            Check(terrain.Plants == 1, "power restoration resumes work");
        }
        {
            var terrain = Reset(); var planter = Planter(terrain, duration, transfer: DeterministicSimulationUnits.FromFloat(.3f));
            Tick(planter);
            Check(terrain.Plants == 0 && planter.CurrentOperatingState == PlantingOperatingState.LoadingSeed
                && FacilitySimulationWorld.Scheduled.Contains(planter), "transfer precedes completed commit");
            Tick(planter, 3); Check(terrain.Plants == 1, "arrival commits exactly once");
        }
        {
            var terrain = Reset(); terrain.Blocks[Input].Items.Item = 2; terrain.Blocks[Input].Items.Count = 1;
            var planter = Planter(terrain, loaded: false); UtilityPole.SupplyRatio = .5f;
            Tick(planter, 110);
            Check(terrain.Plants == 1 && terrain.Blocks[Input].Items.Count == 0, "half power completes whole cycle with one seed");
        }
        {
            var terrain = Reset(); var planter = Planter(terrain, loaded: false); Tick(planter);
            planter.GetObjectInfoStatus(out string text, out bool working, out bool warning);
            Check(text == "Waiting for seeds" && warning && !working, "seed wait remains a warning");
            Check(planter.TryGetElectricPowerDemand(out float watts) && watts == 45000, "seed wait retains full standby demand");
            Check(!FacilitySimulationWorld.Scheduled.Contains(planter), "empty input sleeps");
            terrain.Blocks[Input].Items.Item = 2; terrain.Blocks[Input].Items.Count = 1;
            FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(Input);
            Check(FacilitySimulationWorld.Scheduled.Contains(planter), "input coordinate wakes sleeping planter");
            terrain.Farmland = false; Tick(planter);
            Check(planter.IsErrorState && !planter.TryGetElectricPowerDemand(out _), "invalid ground removes demand");
        }
        {
            var terrain = Reset(); var planter = Planter(terrain, duration); terrain.FailPlant = true; Tick(planter);
            Check(terrain.Blocks[Input].Items.Count == 1 && !planter.Placement.inputOutputState.seedPlanterHasLoadedSeed
                && terrain.Animations == 0, "failed commit restores exactly one seed");
        }
        {
            var terrain = Reset(); var planter = Planter(terrain, duration); terrain.FailPlant = true;
            terrain.Blocks[Input].Items.Count = terrain.Blocks[Input].Items.Capacity;
            int errors = HarnessEngine.Errors; Tick(planter);
            Check(planter.Placement.inputOutputState.seedPlanterHasLoadedSeed && HarnessEngine.Errors == errors + 1,
                "failed return retains seed ownership when input is full");
        }
        {
            var terrain = Reset(); terrain.Blocks[Input].Items.Item = 2; terrain.Blocks[Input].Items.Count = 1;
            var planter = Planter(terrain, duration, loaded: false); Tick(planter);
            Check(terrain.Plants == 0 && terrain.Blocks[Input].Items.Count == 0
                && planter.CurrentOperatingState == PlantingOperatingState.LoadingSeed, "legacy progress must acquire a seed first");
            Tick(planter, 4); Check(terrain.Plants == 1, "legacy progress commits after transfer");
        }
        {
            var terrain = Reset(); var state = Placement(4); state.inputOutputState.seedPlanterHasLoadedSeed = true;
            state.inputOutputState.seedPlanterLoadedSeedItemId = 2;
            state.inputOutputState.seedPlanterLoadedSeedInputCoordinate = new(99, 99);
            var planter = (SeedPlanterInstance)ForestryWorld.Ensure(terrain).Register(
                new SeedPlanter { BoundItemDefinition = InputOutputModule.Definitions[4] }, state);
            planter.Persist();
            Check(state.inputOutputState.seedPlanterLoadedSeedInputCoordinate == Input, "moved planter rebinds loaded seed to configured input");
        }
        {
            var definition = InputOutputModule.Definitions[4];
            definition.Requirements.Add(new() { energyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = 5 });
            definition.Requirements.Add(new() { energyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = 5 });
            var terrain = Reset(); var planter = Planter(terrain);
            var io = planter.Placement.inputOutputState;
            io.storedEnergyTypes.Add((int)ItemDefinition.EnergyType.Burn);
            io.storedEnergyUnitsByType.Add(DeterministicSimulationUnits.FromFloat(10));
            io.energyGaugeCapacityUnitsByType.Add(DeterministicSimulationUnits.FromFloat(10)); Tick(planter);
            Check(FacilityFuel.Stored(io, ItemDefinition.EnergyType.Burn) == DeterministicSimulationUnits.FromFloat(9),
                "duplicate fuel requirements spend total rate only once");
            definition.Requirements.Clear();
        }
        {
            var terrain = Reset(); var planter = Planter(terrain, duration); terrain.Blocks[Output].Items.Item = 1; terrain.Blocks[Output].Items.Count = 4;
            Tick(planter); Check(terrain.Plants == 0 && planter.Placement.inputOutputState.seedPlanterHasLoadedSeed, "logs prevent replanting without losing loaded seed");
            terrain.Blocks[Output].Items.Count = 0; FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(Output);
            Check(FacilitySimulationWorld.Scheduled.Contains(planter), "last log pickup wakes planter");
        }
        {
            var terrain = Reset(); terrain.Blocks[Input].Items.Item = 2; terrain.Blocks[Input].Items.Count = 1;
            var planter = Planter(terrain, loaded: false); Tick(planter, 8);
            var saved = planter.Placement; long progress = saved.inputOutputState.seedPlanterPlantElapsedUnits;
            ForestryWorld.Current.Remove(planter.StorageKey);
            var restored = (SeedPlanterInstance)ForestryWorld.Current.Register(new SeedPlanter { BoundItemDefinition = InputOutputModule.Definitions[4] }, saved);
            Check(restored.Placement.inputOutputState.seedPlanterHasLoadedSeed && progress > 0, "save owns in-flight seed");
            Tick(restored, 60);
            Check(terrain.Plants == 1 && terrain.Blocks[Input].Items.Count == 0, "restored in-flight cycle does not reconsume");
        }
    }
    static void LoggingChecks()
    {
        {
            var terrain = Reset(); var tree = Tree(terrain); var logger = Logger(terrain);
            terrain.Blocks[Output].Items.Capacity = 1; Tick(logger, 10);
            Check(tree.Harvests == 1 && terrain.Blocks[Output].Items.Count == 4, "logging ignores output capacity and keeps all logs on tree coordinate");
            Check(!FacilitySimulationWorld.Scheduled.Contains(logger), "no remaining tree sleeps");
        }
        {
            var terrain = Reset(); var tree = Tree(terrain); tree.Growth = 9; var logger = Logger(terrain);
            Tick(logger); Check(tree.Harvests == 0 && !FacilitySimulationWorld.Scheduled.Contains(logger), "immature tree sleeps");
            tree.Growth = 10; FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(Output);
            Check(FacilitySimulationWorld.Scheduled.Contains(logger), "tree growth wakes logger");
            logger.SetGrowthRange(4, 7); Tick(logger); Check(tree.Harvests == 0, "maximum growth enforced");
            tree.Growth = 4; Tick(logger); Check(logger.Placement.loggingProcess.ConsumedEnergyUnits > 0, "minimum growth inclusive");
            logger.SetTreeTypeEnabled(tree.Definition, new[] { tree.Definition }, false); Tick(logger);
            Check(logger.Placement.loggingProcess.ConsumedEnergyUnits == 0, "disabled filter resets target work");
            logger.SetGrowthRange(6, 2); Check(logger.MinimumGrowth == 6 && logger.MaximumGrowth == 6, "crossed growth clamps");
            logger.SetGrowthRange(-5, 99); Check(logger.MinimumGrowth == 0 && logger.MaximumGrowth == 10, "growth range clamps to domain");
        }
        {
            var terrain = Reset(); var tree = Tree(terrain); var logger = Logger(terrain); Tick(logger, 3);
            long work = logger.Placement.loggingProcess.ConsumedEnergyUnits; Check(work > 0, "logging accumulates energy");
            UtilityPole.SupplyRatio = 0; Tick(logger);
            Check(logger.Placement.loggingProcess.ConsumedEnergyUnits == work && !FacilitySimulationWorld.Scheduled.Contains(logger), "logging power loss preserves work and sleeps");
            UtilityPole.SupplyRatio = 1; logger.WakeForElectricPowerChange(); Tick(logger, 7);
            Check(tree.Harvests == 1, "logging power recovery resumes");
        }
        {
            var terrain = Reset(); var tree = Tree(terrain); var logger = Logger(terrain); Tick(logger, 3);
            var state = logger.Placement; ForestryWorld.Current.Remove(logger.StorageKey); logger = Logger(terrain, state);
            Tick(logger, 7); Check(tree.Harvests == 1, "logging progress survives restore");
        }
        {
            var terrain = Reset(); var old = Tree(terrain); var logger = Logger(terrain); Tick(logger, 3);
            var replacement = Tree(terrain); Tick(logger);
            Check(logger.Placement.loggingProcess.ConsumedEnergyUnits == DeterministicSimulationUnits.FromFloat(4500), "replacement tree does not inherit work");
            Check(old.Harvests == 0 && replacement.Harvests == 0, "replaced resource cannot be harvested through stale identity");
        }
        {
            var terrain = Reset(); var tree = Tree(terrain); Tree(terrain, Vector2Int.right);
            var logger = Logger(terrain); Tick(logger, 3);
            long work = logger.Placement.loggingProcess.ConsumedEnergyUnits;
            var unloaded = terrain.Blocks[Output]; terrain.Blocks.Remove(Output); Tick(logger);
            Check(logger.Placement.loggingProcess.ConsumedEnergyUnits == work
                && !FacilitySimulationWorld.Scheduled.Contains(logger), "streamed target sleeps without switching to another tree");
            terrain.Blocks[Output] = unloaded; FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(Output); Tick(logger, 7);
            Check(tree.Harvests == 1, "streamed target resumes preserved work");
        }
        {
            var terrain = Reset(); var logger = Logger(terrain); var state = logger.Placement;
            state.loggingProcess.HasTarget = true; state.loggingProcess.TargetCoordinate = new(99, 99);
            state.loggingProcess.ConsumedEnergyUnits = 1;
            ForestryWorld.Current.Remove(logger.StorageKey); logger = Logger(terrain, state); logger.Persist();
            Check(!state.loggingProcess.HasTarget && state.loggingProcess.ConsumedEnergyUnits == 0, "moved logger discards work on old coordinate");
        }
        {
            var terrain = Reset(); var tree = Tree(terrain); var logger = Logger(terrain);
            var secondState = Placement(3, new Vector2Int(0, -2)); secondState.quarterTurns = 2;
            var second = Logger(terrain, secondState); Tick(logger, 10); Tick(second, 10);
            Check(tree.Harvests == 1 && terrain.Blocks[Output].Items.Count == 4, "two loggers cannot duplicate one tree");
        }
        Check(LoggingMachine.GetHarvestCoordinate(new(3, 4), 1, 0) == new Vector2Int(4, 4), "harvest direction rotates with installation");
    }
    static void RecoveryChecks()
    {
        var terrain = Reset(); var planter = Planter(terrain, loaded: false);
        terrain.Blocks[Input].Items.Capacity = 2;
        Check(planter.ReceiveHarvestedSeeds(Output, 2, 3, default) == 2, "recovery respects loaded capacity");
        Check(planter.ReceiveHarvestedSeeds(Output, 2, 1, default) == 0, "full input receives no reward");
        Check(planter.ReceiveHarvestedSeeds(Vector2Int.up, 2, 1, default) == 0, "unrelated planting coordinate cannot receive");
        InputOutputModule.BlockInput = true; terrain.Blocks[Input].Items.Count = 0;
        Check(planter.ReceiveHarvestedSeeds(Output, 2, 1, default) == 0, "overlap restriction preserved");
        InputOutputModule.BlockInput = false; terrain.Blocks.Remove(Input); terrain.Store.Saved[Input] = new Slot { Capacity = 2 };
        Check(planter.ReceiveHarvestedSeeds(Output, 2, 3, default) == 2, "recovery uses saved input stack");
        terrain = Reset(); planter = Planter(terrain, loaded: false); var tree = Tree(terrain); tree.Seeds.Add(new(2, 3));
        var logger = Logger(terrain, Placement(3, new(0, -2)));
        logger.Placement.quarterTurns = 2;
        terrain.Blocks[Input].Items.Capacity = 2; Tick(logger, 10);
        Check(terrain.Blocks[Input].Items.Count == 2 && terrain.Blocks[Output].Items.Count == 4, "harvest routes seeds without moving logs");
        int seeds = 0; foreach (var block in terrain.Blocks.Values) if (block.Items.Item == 2) seeds += block.Items.Count;
        Check(seeds == 3, "overflow drops exactly once");
        terrain = Reset();
        var early = Placement(4); var later = Placement(4, new(3, 0));
        var extraInput = new Vector2Int(4, 0); terrain.Blocks[extraInput] = new Block { Coordinate = extraInput };
        later.inputOutputState.inputItemAreas[0] = new() { coordinate = extraInput, itemId = 2 };
        later.inputOutputState.gridCoordinates[1] = extraInput;
        var world = ForestryWorld.Ensure(terrain); var prototype = new SeedPlanter { BoundItemDefinition = InputOutputModule.Definitions[4] };
        var second = world.Register(prototype, later); var first = world.Register(prototype, early);
        terrain.Blocks[Input].Items.Capacity = terrain.Blocks[extraInput].Items.Capacity = 2;
        Check(world.RecoverSeeds(Output, 2, 3, default) == 3 && terrain.Blocks[Input].Items.Count == 2
            && terrain.Blocks[extraInput].Items.Count == 1, "receiver order follows identity, not registration or restore order");
        world.Remove(first.StorageKey);
        Check(world.RecoverSeeds(Output, 2, 3, default) == 1, "expired receiver is removed from recovery index");
        terrain = Reset(); planter = Planter(terrain, loaded: false); Tree(terrain).HarvestSucceeds = false;
        TreeInstance failed = (TreeInstance)terrain.Blocks[Output].Resource; failed.Seeds.Add(new(2, 3));
        var scratch = new List<KeyValuePair<int, int>>();
        Check(!LoggingHarvest.CompleteTreeHarvest(failed, scratch) && terrain.Blocks[Input].Items.Count == 0,
            "failed harvest emits no seed rewards");
    }
    static void LifetimeChecks()
    {
        Check(ForestryWorld.Supports(new SeedPlanter { BoundItemDefinition = InputOutputModule.Definitions[4] })
            && ForestryWorld.Supports(new LoggingMachine { BoundItemDefinition = InputOutputModule.Definitions[3] }),
            "sphere-collider prototypes used by both production assets support data conversion");
        Check(!ForestryWorld.Supports(new LoggingMachine { BoundItemDefinition = InputOutputModule.Definitions[3], HasCollider = false }),
            "unsupported collision shape cannot enter conversion");
        var terrain = Reset(); var planter = Planter(terrain, loaded: false); var world = ForestryWorld.Current;
        Check(ReferenceEquals(terrain.Blocks[Vector2Int.zero].MapObject, planter), "block binds data identity");
        int index = planter.Index; uint generation = planter.Generation; world.Remove(planter.StorageKey);
        Check(!planter.IsRuntimeActive && !FacilitySimulationWorld.Registered.Contains(planter), "removal expires entity and scheduler");
        FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(Input);
        Check(!FacilitySimulationWorld.Scheduled.Contains(planter), "removed observer cannot wake");
        var replacement = Planter(terrain, loaded: false);
        Check(replacement.Index == index && replacement.Generation != generation && !planter.IsRuntimeActive, "slot reuse does not revive old identity");
        bool threw = false; try { world.GetState(index, generation); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "stale state access rejected");
        world.Dispose(); Check(ForestryWorld.Current == null && !replacement.IsRuntimeActive, "world disposal clears identities");
    }
    static void ScaleChecks()
    {
        var terrain = Reset(); var world = ForestryWorld.Ensure(terrain);
        var prototype = new LoggingMachine { BoundItemDefinition = InputOutputModule.Definitions[3] };
        for (int i = 0; i < 1000; i++) world.Register(prototype, Placement(3, new(100 + i * 4, 100)));
        FacilitySimulationWorld.Scheduled.Clear();
        FacilityRuntimeWakeRegistry.NotifyCoordinateChanged(new(100, 99));
        Check(world.Count == 1000 && FacilitySimulationWorld.Scheduled.Count == 1, "coordinate wake only reaches local observer among 1000 entities");
        var nearby = new List<ForestryInstance>(); world.BuildNearby(Vector3.zero, nearby);
        Check(nearby.Count == 0, "nearby index excludes remote entities");
        terrain = Reset(); Tree(terrain);
        var definition = InputOutputModule.Definitions[3]; float complete = definition.completeEnergy;
        definition.completeEnergy = 1000000000; var logger = Logger(terrain); Tick(logger, 32);
        long before = GC.GetAllocatedBytesForCurrentThread(); Tick(logger, 1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"warm logging update allocates {allocated} bytes");
        definition.completeEnergy = complete;
        terrain = Reset(); var planter = Planter(terrain, loaded: false); Tick(planter, 32);
        before = GC.GetAllocatedBytesForCurrentThread(); Tick(planter, 1000);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"warm empty planter update allocates {allocated} bytes");
    }
}
