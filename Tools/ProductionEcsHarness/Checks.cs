using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;
using ProjectF.Benchmark;

static class Checks
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    static bool Near(float a, float b) => Math.Abs(a - b) < .00001f;
    static long Units(float value) => DeterministicSimulationUnits.FromFloat(value);
    static void Tick(ProductionFacilityInstance facility, long tick)
    { MapObjectTickManager.CurrentSimulationTick = tick; facility.ManagedUpdateTick(1f / 60); }
    static (ProductionWorld world, ProductionFacilityInstance value, ProductionRenderTemplate template, Block input, Block output) Create(bool furnace = false, bool fluid = false, bool noEnergy = false, int outputCount = 1)
    {
        MapObjectTickManager.CurrentSimulationTick = 0; UtilityPole.Ratio = 1; BenchmarkRuntime.ForceWorking = false;
        InputOutputModule.SuccessfulEmitsBeforeFailure = int.MaxValue;
        InputOutputModule.Items[1] = new ItemDefinition { id = 1, isFluid = fluid };
        InputOutputModule.Items[2] = new ItemDefinition { id = 2, isFluid = fluid };
        InputOutputModule.Items[3] = new ItemDefinition { id = 3, energyType = ItemDefinition.EnergyType.Burn, energyAmount = 100 };
        var recipe = new ProductionRenderTemplate.Recipe { OutputId = 2, OutputRate = 1, Duration = 2, OutputCount = outputCount, Energy = noEnergy ? 0 : Units(20) };
        recipe.Inputs.Add(new CraftingTreeRuntime.IngredientEntry(1, fluid ? 2 : 3));
        var template = new ProductionRenderTemplate { Recipes = new[] { recipe }, Watts = furnace || noEnergy ? 0 : 10, PrimaryRate = noEnergy ? 0 : 10 };
        if (!noEnergy) template.Definition.Requirements.Add(new ItemDefinition.EnergyUseRequirement {
            energyType = furnace ? ItemDefinition.EnergyType.Burn : ItemDefinition.EnergyType.Electricity, useEnergyAmount = 10 });
        var world = new ProductionWorld();
        var state = new BlockStateStore.InstallationSaveState { itemId = furnace ? 13 : 34, itemFilterMaskInitialized = true, placementSequence = 1 };
        state.inputOutputState.inputItemAreas.Add(new InputOutputModule.PersistentInputItemAreaState(new Vector2Int(1, 0), 1));
        state.inputOutputState.inputEnergyCoordinates.Add(new Vector2Int(3, 0));
        state.inputOutputState.outputCoordinates.Add(new Vector2Int(2, 0));
        state.inputOutputState.gridCoordinates.AddRange(new[] { new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) });
        var input = new Block { Item = 1, Count = 3 }; var output = new Block();
        world.Terrain.Blocks[new Vector2Int(1, 0)] = input; world.Terrain.Blocks[new Vector2Int(2, 0)] = output;
        world.Terrain.Blocks[new Vector2Int(3, 0)] = new Block { Item = 3, Count = 1 };
        var value = world.Add(furnace ? new InputOutputModule() : new ProductionMachine(), state, template);
        return (world, value, template, input, output);
    }
    static void Main()
    {
        var filterFacility = Create();
        var filterDefinitions = new List<ItemDefinition> { filterFacility.template.Definition };
        Check(filterFacility.value.SceneObject == null && PlayerFilterProbe.CanShowButton(filterFacility.value, filterDefinitions),
            "ECS assembler without a scene object exposes the filter button");
        Check(filterFacility.value.TryGetProductionTargetSelection(out var filterTarget) && ReferenceEquals(filterTarget, filterFacility.value),
            "Filter panel resolves the clicked ECS assembler instance");
        var filterUi = new FilterSelectUI();
        Check(filterUi.Toggle(filterFacility.value, 2, false) && !filterFacility.value.IsProductionTargetSelected(2),
            "Filter toggle clears the ECS assembler target");
        Check(filterUi.Toggle(filterFacility.value, 2, true) && filterFacility.value.IsProductionTargetSelected(2),
            "Filter toggle selects the ECS assembler target");
        var automaticFurnace = Create(furnace: true);
        Check(!PlayerFilterProbe.CanShowButton(automaticFurnace.value, filterDefinitions)
            && !filterUi.Toggle(automaticFurnace.value, 2, true), "Automatic furnaces do not expose manual recipe selection");
        var nativeMachine = new ProductionMachine();
        Check(PlayerFilterProbe.CanShowButton(nativeMachine, filterDefinitions), "Native assembler filter remains available");
        var componentTarget = new FilterSceneTarget { ProductionComponent = nativeMachine };
        Check(componentTarget.TryGetProductionTargetSelection(out filterTarget) && ReferenceEquals(filterTarget, nativeMachine),
            "Scene component lookup still resolves native assembler selection");
        Check(!PlayerFilterProbe.CanShowButton(null, filterDefinitions), "A missing target does not expose a filter button");
        var a = Create(); a.input.Count = 2; Tick(a.value, 0);
        Check(!a.value.HasActiveWork && a.input.Count == 2, "Incomplete ingredients must not start");
        var other = new Vector2Int(4, 0); a.value.Placement.inputOutputState.inputItemAreas.Add(new(other, 1));
        a.world.Terrain.Blocks[other] = new Block { Item = 1, Count = 1 }; Tick(a.value, 1);
        Check(!a.value.HasActiveWork, "Machine cannot split a required ingredient between areas");
        a.input.Count = 3; Tick(a.value, 2);
        Check(a.value.HasActiveWork && a.input.Count == 0, "Complete ingredients consumed once");
        Check(a.value.NextUpdateTick == 122, "Machine completion deadline uses item duration");
        MapObjectTickManager.CurrentSimulationTick = 62;
        Check(Near(a.value.WorkProgress, .5f), "On-demand halfway progress");
        Check(a.world.GetState(a.value.Index, a.value.Generation).Production.ConsumedEnergyUnits == 0, "Reading progress does not mutate production");
        a.value.Persist(); Check(a.value.Placement.inputOutputState.activeCraftConsumedEnergyUnits == Units(10), "Save samples elapsed energy");
        Tick(a.value, 122); Check(a.output.Count == 1 && !a.value.HasActiveWork, "Completion emits one solid batch");

        a = Create(); Tick(a.value, 0); MapObjectTickManager.CurrentSimulationTick = 60; a.value.Wake(); UtilityPole.Ratio = .5f; Tick(a.value, 60);
        Check(Near(a.value.WorkProgress, .5f) && a.value.NextUpdateTick == 180, "Half power doubles remaining duration");
        MapObjectTickManager.CurrentSimulationTick = 120; a.value.Wake(); UtilityPole.Ratio = 0; Tick(a.value, 120);
        Check(Near(a.value.WorkProgress, .75f), "Power transition samples previous supply");
        MapObjectTickManager.CurrentSimulationTick = 300; Check(Near(a.value.WorkProgress, .75f), "No power does not advance work");
        a.value.Wake(); UtilityPole.Ratio = 1; Tick(a.value, 300); Check(a.value.NextUpdateTick == 330, "Power recovery resumes remaining work");
        Tick(a.value, 330); Check(a.output.Count == 1, "Recovery emits without duplicate ingredients");

        // Empty automatic inputs must not invent a first-recipe preview.
        a = Create(furnace: true); a.input.Count = 0;
        Check(!a.value.TryGetObjectInfoProductionIngredientCount(out int emptyIngredients) && emptyIngredients == 0,
            "An empty furnace does not display the first Log ingredient");
        Check(!a.value.TryGetObjectInfoProductionOutput(out _, out _, out _),
            "An empty furnace does not display a fabricated Charcoal output");
        a.output.Item = 2; a.output.Count = 2;
        Check(a.value.TryGetObjectInfoProductionOutput(out int storedOutput, out int storedCount, out _) && storedOutput == 2 && storedCount == 2,
            "An idle furnace with no inputs shows the actual items remaining in output");
        a.world.Store.Saved[new Vector2Int(2, 0)] = a.output;
        a.world.Terrain.Blocks.Remove(new Vector2Int(2, 0));
        Check(a.value.TryGetObjectInfoProductionOutput(out storedOutput, out storedCount, out _) && storedOutput == 2 && storedCount == 2,
            "Stored output remains visible after its block streams out");
        a = Create(); a.input.Count = 0;
        Check(a.value.TryGetObjectInfoProductionIngredientCount(out int selectedIngredients) && selectedIngredients == 1,
            "A manually selected assembler target remains visible without materials");

        // Furnace recipes follow actual input, while an existing batch retains its recipe until drained.
        a = Create(furnace: true); InputOutputModule.Items[4] = new ItemDefinition { id = 4 };
        InputOutputModule.Items[5] = new ItemDefinition { id = 5 };
        var oreRecipe = new ProductionRenderTemplate.Recipe { OutputId = 5, OutputCount = 1, OutputRate = 1, Duration = 2, Energy = Units(20) };
        oreRecipe.Inputs.Add(new CraftingTreeRuntime.IngredientEntry(4, 2));
        a.template.Recipes = new[] { a.template.Recipes[0], oreRecipe };
        a.value.Placement.inputOutputState.inputItemAreas.Add(new(new Vector2Int(1, 0), 4));
        a.input.Item = 4; a.input.Count = 1;
        Check(ReferenceEquals(a.value.SelectedRecipe, oreRecipe), "Automatic furnace selects partially filled ore recipe over first log recipe");
        a.value.TryGetObjectInfoProductionIngredient(0, out int shownInput, out int shownRequired, out int shownCount, out _);
        a.value.TryGetObjectInfoProductionOutput(out int shownOutput, out _, out _);
        Check(shownInput == 4 && shownCount == 1 && shownRequired == 2 && shownOutput == 5, "Idle furnace panel shows actual ore and matching output");
        a.value.SetItemFilterEnabled(5, 0, false); Tick(a.value, 0);
        a.value.TryGetObjectInfoProductionIngredient(0, out shownInput, out _, out shownCount, out _);
        Check(shownInput == 4 && shownCount == 1 && !a.value.HasActiveWork, "Automatic furnace ignores a production target mask and still waits for a complete ore batch");
        a.value.SetItemFilterEnabled(5, 0, true); a.input.Count = 2; Tick(a.value, 1);
        Check(a.value.IsWorking && a.value.OutputItemId == 5 && a.input.Count == 0, "Full ore batch starts the matching furnace recipe");
        a.input.Item = 1; a.input.Count = 3; a.output.Item = 5; a.output.Count = 10; Tick(a.value, 121);
        a.value.TryGetObjectInfoProductionIngredient(0, out shownInput, out _, out _, out _);
        Check(a.value.IsWaitingForOutput && shownInput == 4, "Pending ore output retains active recipe when logs arrive");
        a.output.Count = 0; Tick(a.value, 122);
        Check(a.value.IsWorking && a.value.OutputItemId == 2 && a.output.Item == 5, "After ore drains furnace switches to available log recipe");
        var idleState = new BlockStateStore.InstallationSaveState { inputOutputState = new InputOutputModule.PersistentState { activeOutputItemId = 2, hasActiveCraft = false } };
        var idle = a.world.Add(new InputOutputModule(), idleState, a.template);
        Check(idle.ActiveRecipe == null, "Inactive restored output id never pins a stale active recipe");

        a = Create(outputCount: 3); a.output.Count = 10; a.output.Item = 2; Tick(a.value, 0); a.input.Count = 3; Tick(a.value, 120);
        Check(a.value.IsWaitingForOutput && a.input.Count == 3, "Blocked output holds pending batch and new inputs");
        a.output.Count = 0; InputOutputModule.SuccessfulEmitsBeforeFailure = 1; Tick(a.value, 121);
        Check(a.output.Count == 1 && a.value.IsWaitingForOutput, "Partial emission remains pending");
        a.value.Persist(); a.value.Placement.inputOutputState = SaveProbe.Roundtrip(a.value.Placement.inputOutputState);
        Check(a.value.Placement.inputOutputState.activeOutputCount == 2, "Binary save retains only unemitted remainder");
        a.world.Remove(a.value); var restored = a.world.Add(a.value.Prototype, a.value.Placement, a.template);
        InputOutputModule.SuccessfulEmitsBeforeFailure = int.MaxValue; Tick(restored, 122);
        Check(a.output.Count == 3 && a.input.Consumed == 6, "Restored output completes once, then begins next batch");
        Check(!a.value.IsRuntimeActive && restored.IsRuntimeActive, "Reused state slot rejects old generation");

        a = Create(noEnergy: true); Tick(a.value, 0); Check(a.value.NextUpdateTick == 120, "Zero-use MK4 retains timer deadline");
        MapObjectTickManager.CurrentSimulationTick = 60; Check(Near(a.value.WorkProgress, .5f), "Zero-use recipe progress uses item time");
        Tick(a.value, 120); Check(a.output.Count == 1, "Zero-use recipe completes");

        // Reproduce a sleeping, empty furnace receiving coal through the actual shared wake entry point.
        a = Create(furnace: true); a.input.Count = 0;
        var fuel = a.world.Terrain.Blocks[new Vector2Int(3, 0)]; fuel.Count = 0; Tick(a.value, 0);
        a.value.GetObjectInfoStatus(out string status, out _, out _);
        Check(status == "No energy" && !FacilitySimulationWorld.IsScheduled(a.value), "Empty furnace sleeps without energy");
        fuel.Count = 10; InputOutputModule.WakeRuntimeModulesAtCoordinate(new Vector2Int(3, 0));
        Check(FacilitySimulationWorld.IsScheduled(a.value) && a.value.NextUpdateTick == 1, "Coal arrival schedules sleeping data furnace");
        Tick(a.value, 1); a.value.GetObjectInfoStatus(out status, out _, out bool warning);
        Check(status == "Waiting for materials" && warning && fuel.Count == 9, "Loaded coal becomes burn energy, missing logs remain waiting");
        a.value.GetFuelGauge(out long refilledFuel, out _);
        Check(refilledFuel == Units(100), "Refilled fuel appears in energy gauge");
        a.input.Count = 3; InputOutputModule.WakeRuntimeModulesAtCoordinate(new Vector2Int(1, 0)); Tick(a.value, 2);
        Check(a.value.IsWorking && a.input.Count == 0, "Material arrival starts fueled furnace");
        a.output.Item = 2; a.output.Count = 10; Tick(a.value, 122);
        Check(a.value.IsWaitingForOutput, "Filled output blocks furnace batch");
        a.output.Count = 0; InputOutputModule.WakeRuntimeOutputModulesAtCoordinate(new Vector2Int(2, 0));
        Check(a.value.NextUpdateTick == 123, "Output-only notification wakes data producer immediately");
        Tick(a.value, 123); Check(a.output.Count == 1 && !a.value.IsWaitingForOutput, "Cleared output drains pending batch");

        a = Create(furnace: true); Tick(a.value, 0); Tick(a.value, 120);
        Check(a.output.Count == 1, "Burn furnace completes with real fuel");
        Check(a.value.NextUpdateTick == 600, "Idle furnace sleeps until remaining fuel depletion");
        MapObjectTickManager.CurrentSimulationTick = 150; a.value.GetFuelGauge(out long displayedFuel, out _);
        Check(displayedFuel == Units(75), "Fuel gauge samples standby time without per-tick mutation");
        Check(a.value.Placement.inputOutputState.storedEnergyUnitsByType[0] == Units(80), "Craft consumes defined fuel rate");
        Tick(a.value, 180); Check(a.value.Placement.inputOutputState.storedEnergyUnitsByType[0] == Units(70), "Material waiting consumes same standby rate");
        a.value.Placement.inputOutputState.outputCoordinates.Clear(); Tick(a.value, 180); Tick(a.value, 240);
        Check(a.value.Placement.inputOutputState.storedEnergyUnitsByType[0] == Units(70), "No output area consumes no fuel");

        a = Create(furnace: true); a.output.Item = 2; a.output.Count = 10; Tick(a.value, 0); Tick(a.value, 120);
        FacilitySimulationWorld.SetScheduled(a.value, true); Tick(a.value, 180);
        Check(FacilitySimulationWorld.IsScheduled(a.value) && a.value.Placement.inputOutputState.storedEnergyUnitsByType[0] == Units(70),
            "Output-blocked furnace keeps fuel standby scheduling");
        Check(a.value.NextUpdateTick == 600, "Blocked-output furnace wakes at fuel boundary, not each tick");
        a = Create(); InputOutputModule.Items[4] = new ItemDefinition { id = 4 };
        var next = new ProductionRenderTemplate.Recipe { OutputId = 4, OutputCount = 1, OutputRate = 1, Duration = 1, Energy = Units(10) };
        next.Inputs.Add(new CraftingTreeRuntime.IngredientEntry(1, 3));
        a.template.Recipes = new[] { a.template.Recipes[0], next };
        a.value.SetExclusiveProductionTarget(2); Tick(a.value, 0); MapObjectTickManager.CurrentSimulationTick = 60;
        a.value.SetExclusiveProductionTarget(4); Tick(a.value, 60); a.input.Count = 3; Tick(a.value, 120);
        Check(a.output.Item == 2 && a.output.Count == 1 && a.value.OutputItemId == 4,
            "Recipe selection during craft applies after old output drains");
        a = Create(); Tick(a.value, 0); a.value.ClearProductionTargetSelection(); a.input.Count = 3; Tick(a.value, 120);
        Check(!a.value.HasActiveWork && a.input.Count == 3, "Clearing target completes existing craft without starting another");

        a = Create(fluid: true); a.world.SourceUnits = Units(100); a.world.SourcePressure = 60; a.world.OutputCapacity = Units(.5f);
        Tick(a.value, 0); Check(!a.value.HasActiveWork && a.value.FluidUnits(1) == Units(1), "Partial fluid intake waits for full batch");
        Tick(a.value, 1); Tick(a.value, 2); Tick(a.value, 3);
        Check(a.value.HasActiveWork && a.value.FluidUnits(1) == 0, "Full four-liter input batch consumed once");
        int calls = a.world.InputCalls; long source = a.world.SourceUnits;
        MapObjectTickManager.CurrentSimulationTick = 63;
        Check(a.value.TryGetObjectInfoProductionFluidGauge(0, out var gauge) && gauge.isConverting && Near(gauge.convertedFillAmount, .5f), "Single two-color gauge samples conversion");
        Check(a.value.TryGetObjectInfoProductionFluidIngredient(0, out _, out _, out float required) && Near(required, 4), "Fluid input denominator is full required batch");
        Check(a.value.GetFluidPressure(2) == 0, "Crafting has no output pressure");
        Tick(a.value, 123); Check(Near(a.value.GetFluidPressure(2), 1), "Count one output pressure is one L/s");
        a.world.GetState(a.value.Index, a.value.Generation).SupplyRatio = 0;
        a.value.GetObjectInfoStatus(out string noPower, out bool green, out bool yellow);
        Check(noPower == "No energy" && !green && !yellow, "Pending fluid with no power has red status");
        a.world.GetState(a.value.Index, a.value.Generation).SupplyRatio = 1;
        for (long t = 124; t <= 180; t++) Tick(a.value, t);
        Check(a.world.InputCalls == calls && a.world.SourceUnits == source, "Craft and output prevent new fluid intake");
        Check(a.value.IsWaitingForOutput && a.world.OutputAcceptedUnits == Units(.5f), "Full destination stalls exact remaining fluid");
        a.value.Persist(); var fluidState = SaveProbe.Roundtrip(a.value.Placement.inputOutputState);
        Check(fluidState.productionOutputFluidUnits == Units(1.5f), "Binary save preserves fluid reserve");
        a.value.Placement.inputOutputState = fluidState; a.world.Remove(a.value);
        restored = a.world.Add(a.value.Prototype, a.value.Placement, a.template); a.world.OutputCapacity = long.MaxValue;
        long drainTick = 181; while (restored.IsWaitingForOutput && drainTick < 400) Tick(restored, drainTick++);
        Check(a.world.OutputAcceptedUnits == Units(2), "Actual fluid output equals one L/s times two seconds");
        Check(restored.GetFluidPressure(2) == 0 && a.world.InputCalls == calls, "Empty output clears pressure before next intake");
        Tick(restored, drainTick); Check(a.world.InputCalls == calls + 1, "Next intake starts after complete drain");

        var sharedRecipe = new ProductionRenderTemplate.Recipe { OutputId = 2 };
        var manager = GameManager.Instance.ItemManger;
        manager.Probes = 0; manager.Available = true;
        MapObjectTickManager.CurrentSimulationTick = 400;
        Check(sharedRecipe.IsManualAvailable && sharedRecipe.IsManualAvailable && manager.Probes == 1,
            "Shared recipe probes manual possession once per tick, not per entity");
        manager.Available = false; MapObjectTickManager.CurrentSimulationTick = 401;
        Check(!sharedRecipe.IsManualAvailable && manager.Probes == 2, "Next-tick manual removal invalidates shared availability");
        manager.Available = true;

        const int total = 100000;
        a = Create(); BenchmarkRuntime.ForceWorking = true;
        var entities = new ProductionFacilityInstance[total];
        for (int i = 0; i < total; i++)
        {
            var state = new BlockStateStore.InstallationSaveState { placementSequence = i + 1 };
            state.inputOutputState.outputCoordinates.Add(new Vector2Int(2, 0));
            entities[i] = a.world.Add(a.value.Prototype, state, a.template); Tick(entities[i], 0);
        }
        // Warm boundary/scheduler storage before measuring the steady production burst.
        MapObjectTickManager.CurrentSimulationTick = 120;
        foreach (var entity in entities) { entity.ManagedUpdateTick(1f / 60); entity.Persist(); }
        Check(a.output.Count == total, "100k first synchronized completion emits exactly 100k items");
        MapObjectTickManager.CurrentSimulationTick = 240;
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var entity in entities) { entity.ManagedUpdateTick(1f / 60); entity.Persist(); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"100k production burst boundary allocation: {allocated} bytes");
        Check(a.output.Count == total * 2, "100k next synchronized completion emits exactly 100k items");
        Check(allocated == 0, "100k entity production/save steady path allocates no managed bytes");
        Console.WriteLine($"PASS: {checks} production ECS checks; 100k entity completion + save allocated {allocated} bytes (engine/pipe boundaries doubled).");
    }
}
