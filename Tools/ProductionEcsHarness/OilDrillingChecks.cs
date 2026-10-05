using System;
using UnityEngine;
using ProjectF.Benchmark;
using ProjectF.Simulation;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;

static class OilDrillingChecks
{
    static int checks;
    static ProductionFacilityInstance active;
    static float burnRate = .75f, electricRate = 1.2f, burnEnergy = 1.8f, electricWatts = 50;
    static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    static long Units(float value) => DeterministicSimulationUnits.FromFloat(value);
    static bool Scheduled(ProductionFacilityInstance drill) => drill.IsSimulationScheduled;
    static void Unregister(ProductionFacilityInstance drill) => drill.World.OilBatch.Unregister(drill);
    static (ProductionWorld world, ProductionFacilityInstance drill, ResourceInstance oil, Fluidtank tank, Block fuel) Create(bool burn = false, int turns = 0, int reserves = 100)
    {
        if (active != null) Unregister(active);
        BenchmarkRuntime.ForceWorking = false; BenchmarkRuntime.SpilledFluidLiters = 0;
        UtilityPole.Ratio = 1; GameManager.Instance.ItemManger.Available = false;
        ResourceInstance.All.Clear();
        Pipe.Graph.Clear(); PipeWorld.Current.Pipes.Clear(); Pipe.RoutePump = null; Pipe.FluidId = -1; Pipe.SeedOnlyGraph = false;
        Pipe.DataPeers.Clear();
        InstallationObject.Bodies.Clear(); InputOutputModule.Storages.Clear(); InputOutputModule.FluidTopologyVersion++;
        InputOutputModule.Items[2] = new ItemDefinition { id = 2, isFluid = true };
        InputOutputModule.Items[3] = new ItemDefinition { id = 3, energyType = ItemDefinition.EnergyType.Burn, energyAmount = 10 };
        var port = new Vector2Int(0, 2);
        var prototype = new OilDrillingMachine(); prototype.PortTypes[port] = InputOutputModule.RectGridBlockType.PipeOutputItem;
        prototype.ExternalDirections[port] = Vector2Int.up;
        var rate = burn ? burnRate : electricRate;
        var template = new ProductionRenderTemplate { IsOilDrill = true, HasPipePorts = true, OilLitersPerSecond = rate,
            Watts = burn ? 0 : electricWatts, PrimaryRate = burn ? burnEnergy : electricWatts,
            Recipes = new[] { new ProductionRenderTemplate.Recipe { OutputId = 2, OutputRate = rate, OutputCount = 1, Duration = 1 / rate } } };
        template.Definition.Requirements.Add(new ItemDefinition.EnergyUseRequirement {
            energyType = burn ? ItemDefinition.EnergyType.Burn : ItemDefinition.EnergyType.Electricity, useEnergyAmount = template.PrimaryRate });
        var state = new BlockStateStore.InstallationSaveState { itemId = burn ? 61 : 62, quarterTurns = turns, placementSequence = 1 };
        state.inputOutputState.gridCoordinates.Add(port); state.inputOutputState.outputCoordinates.Add(port);
        var world = new ProductionWorld(); var fuel = new Block { Item = 3, Count = 10 };
        state.inputOutputState.inputEnergyCoordinates.Add(new Vector2Int(0, -1)); world.Terrain.Blocks[new Vector2Int(0, -1)] = fuel;
        world.CacheTemplate(template, prototype);
        var drill = world.Register(prototype, state); active = drill;
        var oil = new ResourceInstance { Item = 2, Reserves = reserves };
        ResourceInstance.All[drill.OilTargetCoordinate] = oil;
        var tank = new Fluidtank(); InputOutputModule.Storages[port] = tank;
        PipeWorld.Current.Pipes[port] = new PipeRecord { Directions = { Vector2Int.up, Vector2Int.down } };
        Pipe.Graph[port] = 0;
        world.OilBatch.KeepScheduled(drill); drill.ManagedUpdateTick(1f / 60);
        return (world, drill, oil, tank, fuel);
    }
    static void Step(ProductionFacilityInstance drill, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            MapObjectTickManager.Step();
        }
    }
    static void Conserved(ProductionFacilityInstance drill, ResourceInstance oil, Fluidtank tank)
        => Check(Math.Abs(tank.Units + drill.Placement.inputOutputState.productionOutputFluidUnits - Units(oil.Harvests * oil.Batch)) <= 60,
            "harvested oil equals delivered plus durable output buffer");
    static void Main(string[] args)
    {
        CheckAuthoring(args[0]);
        for (int q = 0; q < 4; q++)
        {
            var f = Create(turns: q);
            Check(f.drill.SceneObject == null, "installed drill has no scene object");
            Check(f.drill.OilTargetCoordinate == (q == 0 ? Vector2Int.right : q == 1 ? Vector2Int.down : q == 2 ? Vector2Int.left : Vector2Int.up), "rotated oil target");
            Step(f.drill, 600);
            Check(f.oil.Harvests == 12, $"electric drill extracts configured 1.2 L/s without a crafting manual: harvested={f.oil.Harvests}, delivered={f.tank.StoredFluidLiters}, progress={f.world.GetState(f.drill.Index, f.drill.Generation).Oil.ProgressUnits}, scheduled={Scheduled(f.drill)}");
            Conserved(f.drill, f.oil, f.tank);
            Check(f.drill.GetFluidPressure(2) == 1.2f && f.drill.GetFluidPressure(3) == 0, "oil pressure and fluid identity");
            Check(f.world.FluidRouteSearches == 1, "unchanged oil route is searched once across 600 ticks");
            f.drill.Persist();
            var saved = SaveProbe.Roundtrip(f.drill.Placement.inputOutputState);
            Check(saved.oilDrillingProgressUnits == f.world.GetState(f.drill.Index, f.drill.Generation).Oil.ProgressUnits, "fractional progress survives binary save");
            Check(saved.productionOutputFluidUnits == f.drill.Placement.inputOutputState.productionOutputFluidUnits, "pending oil survives binary save");
            f.world.Remove(f.drill);
            Check(!f.drill.IsRuntimeActive && f.drill.GetFluidPressure(2) == 0, "released state cannot supply pressure");
        }
        var burn = Create(burn: true); Step(burn.drill, 600);
        Check(burn.oil.Harvests == 7, "burn drill extracts configured .75 L/s");
        Check(burn.fuel.Consumed == 2, "burn drill refills 10-unit fuels for 18 energy units");
        Conserved(burn.drill, burn.oil, burn.tank);
        var partial = Create(); UtilityPole.Ratio = .25f; partial.drill.Wake(); partial.drill.ManagedUpdateTick(1f / 60);
        Step(partial.drill, 600);
        Check(partial.oil.Harvests == 3, "partial electricity scales oil output");
        Check(Math.Abs(partial.drill.GetFluidPressure(2) - .3f) < .00001f, "partial electricity scales pressure");
        var empty = Create(); ResourceInstance.All.Clear(); empty.drill.Wake(); empty.drill.ManagedUpdateTick(1f / 60);
        Step(empty.drill, 600);
        Check(empty.oil.Harvests == 0 && empty.tank.Units == 0 && !Scheduled(empty.drill), "missing resource sleeps without output");
        ResourceInstance.All[empty.drill.OilTargetCoordinate] = empty.oil; empty.drill.Wake(); Step(empty.drill, 100);
        Check(empty.oil.Harvests > 0, "restored resource wakes a drill");
        var full = Create(burn: true); full.tank.Capacity = 0; full.drill.Wake(); full.drill.ManagedUpdateTick(1f / 60);
        int consumed = full.fuel.Consumed; Step(full.drill, 600);
        Check(full.oil.Harvests == 0 && full.fuel.Consumed == consumed && !Scheduled(full.drill), "full output sleeps without mining or consuming fuel");
        full.tank.Capacity = Units(10); full.world.WakeFluidOutputStorage(full.tank); Step(full.drill, 100);
        Check(full.oil.Harvests == 1, "tank capacity increase wakes linked producer");
        var pumped = Create(); Pipe.RoutePump = new Pump { Rate = .5f }; InputOutputModule.FluidTopologyVersion++;
        pumped.drill.Wake(); pumped.drill.ManagedUpdateTick(1f / 60); Step(pumped.drill, 600);
        Conserved(pumped.drill, pumped.oil, pumped.tank);
        Check(pumped.tank.StoredFluidLiters <= 5.0001f && pumped.oil.Harvests <= 5, "pump capacity limits extraction and delivery");
        pumped.drill.Persist(); long pending = pumped.drill.Placement.inputOutputState.productionOutputFluidUnits;
        var roundtrip = SaveProbe.Roundtrip(pumped.drill.Placement.inputOutputState);
        Check(roundtrip.productionOutputFluidUnits == pending, "partial pump delivery is durable");
        var forced = Create(reserves: 0); BenchmarkRuntime.ForceWorking = true; forced.drill.Wake(); forced.drill.ManagedUpdateTick(1f / 60);
        Step(forced.drill, 600);
        Check(forced.oil.Harvests == 0 && forced.tank.Units == Units(12), "forced drill emits oil at configured rate without depleting resource");
        forced.tank.Capacity = forced.tank.Units; Step(forced.drill, 60);
        Check(Math.Abs(BenchmarkRuntime.SpilledFluidLiters - 1.2) < .0001, "forced blocked output records fluid spill");
        var mixed = Create(); mixed.tank.Fluid = 9; mixed.tank.Units = Units(1); mixed.drill.Wake(); mixed.drill.ManagedUpdateTick(1f / 60); Step(mixed.drill, 600);
        Check(mixed.oil.Harvests == 0 && mixed.drill.GetFluidPressure(2) == 0, "incompatible tank cannot receive oil");
        var finite = Create(reserves: 2); Step(finite.drill, 600);
        Check(finite.oil.Harvests == 2 && finite.tank.Units == Units(2), "depletion stops exactly at available reserves");
        var wrongResource = Create(); wrongResource.oil.Item = 9; wrongResource.drill.Wake(); Step(wrongResource.drill, 120);
        Check(wrongResource.oil.Harvests == 0 && !Scheduled(wrongResource.drill), "mismatched resource never burns energy indefinitely");
        var restored = Create(); Step(restored.drill, 23); restored.drill.Persist();
        var restoredState = restored.drill.Placement.Clone(); restoredState.inputOutputState = SaveProbe.Roundtrip(restoredState.inputOutputState);
        long progressBeforeUnload = restoredState.inputOutputState.oilDrillingProgressUnits;
        Unregister(restored.drill); restored.world.Remove(restored.drill);
        var restoredDrill = restored.world.Add(restored.drill.Prototype, restoredState, restored.drill.Template);
        restored.world.OilBatch.Register(restoredDrill); restoredDrill.Wake(); active = restoredDrill;
        Check(restored.world.GetState(restoredDrill.Index, restoredDrill.Generation).Oil.ProgressUnits == progressBeforeUnload, "reconstructed entity restores fractional progress");
        restoredDrill.ManagedUpdateTick(1f / 60); Step(restoredDrill, 27);
        Check(restored.oil.Harvests == 1, "save/load completes the original extraction without restarting it");
        var streaming = Create(); Step(streaming.drill, 20); ResourceInstance.All.Clear(); streaming.world.Wake(streaming.drill.OilTargetCoordinate); Step(streaming.drill, 1);
        long unloadedProgress = streaming.world.GetState(streaming.drill.Index, streaming.drill.Generation).Oil.ProgressUnits;
        Step(streaming.drill, 120);
        Check(streaming.world.GetState(streaming.drill.Index, streaming.drill.Generation).Oil.ProgressUnits == unloadedProgress && !Scheduled(streaming.drill), "unloaded deposit freezes progress and sleeps");
        ResourceInstance.All[streaming.drill.OilTargetCoordinate] = streaming.oil; streaming.world.Wake(streaming.drill.OilTargetCoordinate); Step(streaming.drill, 80);
        Check(streaming.oil.Harvests > 0, "resource-coordinate notification restarts a streamed drill");
        var disconnected = Create(); Pipe.Graph.Clear(); PipeWorld.Current.Pipes.Clear(); InputOutputModule.FluidTopologyVersion++;
        disconnected.drill.Wake(); Step(disconnected.drill, 120);
        Check(disconnected.oil.Harvests == 0 && !Scheduled(disconnected.drill), "disconnected pipe sleeps");
        var port = disconnected.drill.OutputCoordinates[0]; Pipe.Graph[port] = 0;
        PipeWorld.Current.Pipes[port] = new PipeRecord { Directions = { Vector2Int.up, Vector2Int.down } }; InputOutputModule.FluidTopologyVersion++;
        disconnected.drill.Wake(); Step(disconnected.drill, 100);
        Check(disconnected.oil.Harvests > 0, "topology invalidation rebuilds a reconnected route");
        var toggle = Create(); toggle.drill.Placement.inputOutputState.productionOutputFluidUnits = Units(.7f); toggle.tank.Capacity = 0;
        toggle.drill.ResetOilBenchmarkWork();
        Check(toggle.drill.Placement.inputOutputState.productionOutputFluidUnits == Units(.7f), "benchmark reset retains real harvested oil");
        BenchmarkRuntime.ForceWorking = true; Step(toggle.drill, 60);
        Check(toggle.drill.Placement.inputOutputState.productionOutputFluidUnits == Units(.7f), "forced blocked output spills only synthetic oil");
        BenchmarkRuntime.ForceWorking = false; toggle.drill.ResetOilBenchmarkWork(); toggle.tank.Capacity = Units(10); Step(toggle.drill, 60);
        Check(toggle.tank.Units >= Units(.7f), "turning benchmark off delivers retained real oil");
        var dataInput = Create(); InputOutputModule.Storages.Clear(); InputOutputModule.FluidTopologyVersion++;
        var inputPort = dataInput.drill.OutputCoordinates[0] + Vector2Int.up;
        var receiverPrototype = new ProductionMachine(); receiverPrototype.PortTypes[inputPort] = InputOutputModule.RectGridBlockType.PipeInputItem; receiverPrototype.ExternalDirections[inputPort] = Vector2Int.down;
        var receiverRecipe = new ProductionRenderTemplate.Recipe { OutputId = 3, OutputCount = 1, OutputRate = 1, Duration = 1 };
        receiverRecipe.Inputs.Add(new CraftingTreeRuntime.IngredientEntry(2, 1));
        var receiverState = new BlockStateStore.InstallationSaveState { itemFilterMaskInitialized = true }; receiverState.inputOutputState.gridCoordinates.Add(inputPort);
        GameManager.Instance.ItemManger.Available = true;
        var receiver = dataInput.world.Add(receiverPrototype, receiverState, new ProductionRenderTemplate { Recipes = new[] { receiverRecipe } });
        dataInput.drill.Wake(); Step(dataInput.drill, 100);
        Check(receiver.FluidUnits(2) == Units(1) && dataInput.oil.Harvests == 1, "oil feeds a data-owned fluid ingredient port");
        Check(!Scheduled(dataInput.drill), "full data-owned receiver puts oil producer to sleep");
        receiver.Placement.inputOutputState.productionInputFluidUnits[0] = 0; dataInput.world.WakeNativeProducers(receiver); Step(dataInput.drill, 100);
        Check(receiver.FluidUnits(2) == Units(1) && dataInput.oil.Harvests == 2, "receiver capacity publication wakes cached oil producer");
        var conflictingSource = Create(); GameManager.Instance.ItemManger.Available = true;
        InputOutputModule.Items[9] = new ItemDefinition { id = 9, isFluid = true };
        var peerPrototype = new ProductionMachine();
        var peerState = new BlockStateStore.InstallationSaveState { itemFilterMaskInitialized = true };
        var peerRecipe = new ProductionRenderTemplate.Recipe { OutputId = 9, OutputRate = 1, OutputCount = 1, Duration = 1 };
        var peer = conflictingSource.world.Add(peerPrototype, peerState, new ProductionRenderTemplate { HasPipePorts = true, Recipes = new[] { peerRecipe } });
        Pipe.DataPeers.Add(peer); InputOutputModule.FluidTopologyVersion++;
        conflictingSource.drill.Wake(); Step(conflictingSource.drill, 100);
        Check(conflictingSource.oil.Harvests == 0 && !Scheduled(conflictingSource.drill), "different live producer fluid blocks an oil route");
        peer.ClearProductionTargetSelection(); Step(conflictingSource.drill, 100);
        Check(conflictingSource.oil.Harvests > 0, "changing a cached peer's fluid identity wakes blocked oil without topology changes");
        CheckForcedBatch();
        CheckBatchOrder();
        MeasurePopulation(1000); MeasurePopulation(10000); MeasurePopulation(100000);
        MeasureForcedPopulation(100000);
        Console.WriteLine($"PASS: {checks} oil ECS authoring/scheduler/resource/energy/pipe/save/lifetime checks.");
    }

    static void CheckAuthoring(string root)
    {
        string archetypes = Path.Combine(root, "FactorioProject/Assets/Data/MapObjectArchetypes");
        foreach (bool burn in new[] { true, false })
        {
            string asset = File.ReadAllText(Path.Combine(root, "FactorioProject/Assets/Data/Items", burn ? "Item_78_Oil drilling machine.asset" : "Item_101_Electric Oil machine.asset"));
            float rate = float.Parse(Regex.Match(asset, @"(?m)^  fluidOutputLitersPerSecond: (.+)$").Groups[1].Value, CultureInfo.InvariantCulture);
            float energy = float.Parse(Regex.Match(asset, @"useEnergyAmount: ([\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
            if (burn) { burnRate = rate; burnEnergy = energy; } else { electricRate = rate; electricWatts = energy; }
            string guid = Regex.Match(asset, @"mapObjectArchetype: \{fileID: \d+, guid: (\w+)").Groups[1].Value;
            string archetype = null;
            foreach (string meta in Directory.EnumerateFiles(archetypes, "*.asset.meta"))
                if (File.ReadAllText(meta).Contains("guid: " + guid)) { archetype = File.ReadAllText(meta[..^5]); break; }
            Check(archetype != null && archetype.Contains("sourceRuntimeType: OilDrillingMachine"), "actual oil asset resolves its drilling render archetype");
            Check(archetype.Contains("  renderParts:\n") || archetype.Contains("  renderParts:\r\n"), "actual oil archetype contains batch-renderable parts");
            Check(archetype.Contains("  animationClips:") && archetype.Contains("  - clip: {fileID: 7400000"), "actual oil archetype has a baked animation clip");
        }
    }

    static void MeasurePopulation(int count)
    {
        var fixture = Create(); Unregister(fixture.drill);
        Pipe.SeedOnlyGraph = true;
        var drills = new ProductionFacilityInstance[count];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            var state = new BlockStateStore.InstallationSaveState { anchorCoordinate = new Vector2Int(i + 10, 0), placementSequence = i + 2 };
            var port = new Vector2Int(i + 10, 3);
            fixture.drill.Prototype.PortTypes[port] = InputOutputModule.RectGridBlockType.PipeOutputItem;
            fixture.drill.Prototype.ExternalDirections[port] = Vector2Int.up;
            state.inputOutputState.gridCoordinates.Add(port); state.inputOutputState.outputCoordinates.Add(port);
            InputOutputModule.Storages[port] = fixture.tank; Pipe.Graph[port] = 0;
            PipeWorld.Current.Pipes[port] = new PipeRecord { Directions = { Vector2Int.up, Vector2Int.down } };
            drills[i] = fixture.world.Add(fixture.drill.Prototype, state, fixture.drill.Template);
            ResourceInstance.All[drills[i].OilTargetCoordinate] = new ResourceInstance { Item = 2, Reserves = 10000 };
            fixture.world.OilBatch.Register(drills[i]); drills[i].Wake();
        }
        double creationMs = watch.Elapsed.TotalMilliseconds;
        Step(null, 60); GC.Collect(); GC.WaitForPendingFinalizers();
        long before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart(); Step(null, 120);
        double perTickMs = watch.Elapsed.TotalMilliseconds / 120; long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "warmed oil scheduler and route loop allocates no managed bytes");
        Check(fixture.world.FluidRouteSearches == count + 1, "mass ticks reuse all cached routes");
        Console.WriteLine($"Managed boundary benchmark: {count} drills, create={creationMs:F2}ms, tick={perTickMs:F3}ms, allocated={allocated} bytes; excludes Unity rendering and pipe traversal.");
        foreach (var drill in drills) { Unregister(drill); fixture.world.Remove(drill); }
    }

    static void CheckForcedBatch()
    {
        var f = Create(); Pipe.Graph.Clear(); PipeWorld.Current.Pipes.Clear(); InputOutputModule.FluidTopologyVersion++;
        BenchmarkRuntime.ForceWorking = true; f.drill.ResetOilBenchmarkWork(); Step(null, 1);
        long sorts = f.world.OilBatch.SortCount;
        Check(f.drill.OilFastForced && f.world.OilBatch.ScheduledCount == 1, "disconnected forced drill enters aggregate batch");
        int powerPreparations = UtilityPole.PowerPreparationCount;
        Step(null, 600);
        Check(UtilityPole.PowerPreparationCount == powerPreparations, "forced oil batch skips unused power snapshot preparation");
        Check(Math.Abs(BenchmarkRuntime.SpilledFluidLiters - 12) < .0001, "aggregate batch preserves configured forced rate");
        Check(f.world.OilBatch.SortCount == sorts, "unchanged batch membership is never sorted again");
        long processed = f.world.ProcessedUpdates;
        f.drill.Persist(); f.drill.ClearItems(); Step(null, 60);
        Check(Math.Abs(BenchmarkRuntime.SpilledFluidLiters - 13.2) < .0001 && f.world.ProcessedUpdates - processed == 60,
            "saving and clearing a fast drill neither duplicates nor drops synthetic output");
        var port = f.drill.OutputCoordinates[0]; Pipe.Graph[port] = 0;
        PipeWorld.Current.Pipes[port] = new PipeRecord { Directions = { Vector2Int.up, Vector2Int.down } };
        InputOutputModule.FluidTopologyVersion++; f.drill.Wake(); Step(null, 60);
        Check(!f.drill.OilFastForced && f.tank.Units == Units(1.2f), "connecting a forced drill leaves aggregate and feeds storage");
        BenchmarkRuntime.ForceWorking = false; f.drill.ResetOilBenchmarkWork(); Step(null, 101);
        Check(!f.drill.OilFastForced && f.oil.Harvests == 2, "returning to normal production restores resource harvesting");
        Check(UtilityPole.PowerPreparationCount > powerPreparations, "normal oil batch resumes power snapshot preparation");
        f.world.Remove(f.drill);
        Check(f.world.OilBatch.ScheduledCount == 0, "removing the final producer unschedules its batch");

        var late = Create(); Pipe.Graph.Clear(); PipeWorld.Current.Pipes.Clear(); InputOutputModule.FluidTopologyVersion++;
        BenchmarkRuntime.ForceWorking = true; late.drill.ResetOilBenchmarkWork(); Step(null, 1);
        double spilled = BenchmarkRuntime.SpilledFluidLiters;
        var wake = new TickAction(() => {
            Pipe.Graph[port] = 0;
            PipeWorld.Current.Pipes[port] = new PipeRecord { Directions = { Vector2Int.up, Vector2Int.down } };
            InputOutputModule.FluidTopologyVersion++; late.drill.Wake();
        });
        FacilitySimulationWorld.Register(wake, true); Step(null, 1);
        Check(Math.Abs(BenchmarkRuntime.SpilledFluidLiters - spilled - electricRate / 60) < .0001 && late.tank.Units == 0,
            "wake before oil apply settles the last aggregate interval exactly once");
        Step(null, 60);
        Check(late.tank.Units == Units(1.2f), "late topology wake joins individual output on the following interval");
        late.world.Remove(late.drill); BenchmarkRuntime.ForceWorking = false;
    }

    sealed class TickAction : IMapObjectUpdateTick, IMapObjectSimulationIdentity
    {
        readonly Action action;
        internal TickAction(Action action) { this.action = action; }
        public long SimulationId => -1000;
        public void ManagedUpdateTick(float delta) { FacilitySimulationWorld.Unregister(this); action(); }
    }

    static void CheckBatchOrder()
    {
        var f = Create();
        var state = new BlockStateStore.InstallationSaveState { anchorCoordinate = new Vector2Int(10, 0), placementSequence = 0 };
        var port = f.drill.OutputCoordinates[0];
        state.inputOutputState.gridCoordinates.Add(port); state.inputOutputState.outputCoordinates.Add(port);
        var earlier = f.world.Add(f.drill.Prototype, state, f.drill.Template);
        var resource = new ResourceInstance { Item = 2, Reserves = 100 };
        ResourceInstance.All[earlier.OilTargetCoordinate] = resource;
        f.world.GetState(earlier.Index, earlier.Generation).Oil.ProgressUnits = Units(1);
        f.world.GetState(f.drill.Index, f.drill.Generation).Oil.ProgressUnits = Units(1);
        f.tank.Capacity = Units(1);
        f.world.OilBatch.Register(earlier); earlier.Wake(); Step(null, 1);
        Check(resource.Harvests == 1 && f.oil.Harvests == 0 && f.tank.Units == Units(1),
            "shared output commits by placement sequence instead of registration order");
        resource.Reserves = 0; f.tank.Units = 0; f.world.WakeFluidOutputStorage(f.tank); Step(null, 1);
        Check(f.oil.Harvests == 1 && f.tank.Units == Units(1), "capacity wake requeues a sleeping producer after its earlier peer depletes");
        f.world.Remove(earlier); f.world.Remove(f.drill);
    }

    static void MeasureForcedPopulation(int count)
    {
        var fixture = Create(); Unregister(fixture.drill);
        Pipe.Graph.Clear(); PipeWorld.Current.Pipes.Clear(); InputOutputModule.Storages.Clear();
        ResourceInstance.All.Clear(); InputOutputModule.FluidTopologyVersion++;
        BenchmarkRuntime.ForceWorking = true;
        var drills = new ProductionFacilityInstance[count];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            var state = new BlockStateStore.InstallationSaveState { anchorCoordinate = new Vector2Int(i + 10, 0), placementSequence = i + 2 };
            var port = new Vector2Int(i + 10, 3);
            fixture.drill.Prototype.PortTypes[port] = InputOutputModule.RectGridBlockType.PipeOutputItem;
            fixture.drill.Prototype.ExternalDirections[port] = Vector2Int.up;
            state.inputOutputState.gridCoordinates.Add(port); state.inputOutputState.outputCoordinates.Add(port);
            drills[i] = fixture.world.Add(fixture.drill.Prototype, state, fixture.drill.Template);
            fixture.world.OilBatch.Register(drills[i]); drills[i].Wake();
        }
        double creationMs = watch.Elapsed.TotalMilliseconds;
        Step(null, 60); GC.Collect(); GC.WaitForPendingFinalizers();
        double spilled = BenchmarkRuntime.SpilledFluidLiters; long updates = fixture.world.ProcessedUpdates;
        long sorts = fixture.world.OilBatch.SortCount;
        long before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart(); Step(null, 120);
        double perTickMs = watch.Elapsed.TotalMilliseconds / 120; long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "100k forced aggregate ticks allocate no managed bytes");
        Check(fixture.world.OilBatch.SortCount == sorts, "100k forced aggregate ticks avoid membership sorting");
        Check(fixture.world.ProcessedUpdates - updates == (long)count * 120, "aggregate profiler counts logical entity ticks");
        Check(Math.Abs(BenchmarkRuntime.SpilledFluidLiters - spilled - (double)count * electricRate * 2) < 1,
            "100k forced aggregate conserves synthetic output");
        Check(fixture.world.FluidRouteSearches == 1, "100k disconnected forced drills never build a successful pipe route");
        Console.WriteLine($"Managed boundary benchmark: {count} forced disconnected drills, create={creationMs:F2}ms, tick={perTickMs:F4}ms, allocated={allocated} bytes; excludes Unity rendering.");
        foreach (var drill in drills) fixture.world.Remove(drill);
        Check(fixture.world.OilBatch.ScheduledCount == 0, "100k removals release aggregate scheduling");
        BenchmarkRuntime.ForceWorking = false;
    }
}
