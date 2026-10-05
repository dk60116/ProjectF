using System;
using UnityEngine;
using ProjectF.Benchmark;

static class BenchmarkInputChecks
{
    static int checks;
    static void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
    static void Main()
    {
        BenchmarkRuntime.ForceWorking = true; BenchmarkInputSupply.Clear();
        InputOutputModule.Items[1] = new ItemDefinition { id = 1 };
        InputOutputModule.Items[2] = new ItemDefinition { id = 2 };
        InputOutputModule.Items[3] = new ItemDefinition { id = 3, energyType = ItemDefinition.EnergyType.Burn, energyAmount = 10 };
        InputOutputModule.Items[4] = new ItemDefinition { id = 4, isFluid = true };
        GameManager.Instance.ItemManger.ItemDefinitions.Add(InputOutputModule.Items[3]);
        var terrain = new TerrainGenerator(); var store = new BlockStateStore();
        var coordinate = new Vector2Int(1, 1); var block = new Block { Item = 1, Count = 6 };
        terrain.Blocks[coordinate] = block;
        var owner = new object();
        BenchmarkInputSupply.Add(owner, terrain, store, coordinate, 1, 2);
        Check(block.Count == 16, "initial fill includes player-supplied items");
        BenchmarkInputSupply.Consume(owner, terrain, coordinate, 1, 3, default, .1f);
        Check(block.Count == 16 && block.VirtualConsumes == 3, "fake consumption preserves quantity and plays real animation boundary");
        block.Count -= 5; InputOutputModule.WakeRuntimeModulesAtCoordinate(coordinate);
        Check(block.Count == 16, "actual mutation wake hook immediately replenishes external extraction");
        BenchmarkInputSupply.Add(owner, terrain, store, coordinate, 1, 2);
        Check(block.Count == 16, "repeated supply registration does not duplicate stock");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) BenchmarkInputSupply.Refill(coordinate);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "full registered stacks allocate no managed bytes");
        var saved = new Vector2Int(2, 2);
        BenchmarkInputSupply.Add(owner, terrain, store, saved, 1, 2);
        Check(store.GetSavedCenterItemCount(saved, 1) == 16, "unloaded center stack fills through saved storage");
        store.RemoveSavedCenterItems(saved, 1, 5); InputOutputModule.WakeRuntimeModulesAtCoordinate(saved);
        Check(store.GetSavedCenterItemCount(saved, 1) == 16, "unloaded center stack replenishes after extraction");
        var conflict = new Vector2Int(3, 3); terrain.Blocks[conflict] = new Block { Item = 2, Count = 7 };
        BenchmarkInputSupply.Add(owner, terrain, store, conflict, 1, 2);
        Check(terrain.Blocks[conflict].Item == 2 && terrain.Blocks[conflict].Count == 7, "incompatible player stack is never replaced or deleted");
        var fuel = new Vector2Int(4, 4); terrain.Blocks[fuel] = new Block();
        var definition = new ItemDefinition(); definition.Requirements.Add(new ItemDefinition.EnergyUseRequirement { energyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = 1.8f });
        BenchmarkInputSupply.AddEnergy(owner, terrain, store, new[] { fuel }, definition);
        Check(terrain.Blocks[fuel].Item == 3 && terrain.Blocks[fuel].Count == 16, "energy cell automatically chooses a matching fuel");
        MapObjectTickManager.CurrentSimulationTick += 600;
        BenchmarkInputSupply.SampleEnergy(owner, default, true);
        Check(terrain.Blocks[fuel].Count == 16 && terrain.Blocks[fuel].VirtualConsumes == 2, "ten seconds of forced energy simulates two fuels without losing either");
        BenchmarkInputSupply.SampleEnergy(owner, default, true);
        Check(terrain.Blocks[fuel].VirtualConsumes == 2, "sampling the same energy tick twice does not duplicate consumption");
        BenchmarkInputSupply.Remove(owner); block.Count--;
        InputOutputModule.WakeRuntimeModulesAtCoordinate(coordinate);
        Check(block.Count == 15, "removed owner leaves no refill subscription");
        BenchmarkInputSupply.Add(owner, terrain, store, coordinate, 1, 2);
        BenchmarkRuntime.ForceWorking = false; BenchmarkInputSupply.Clear(); block.Count--;
        InputOutputModule.WakeRuntimeModulesAtCoordinate(coordinate);
        Check(block.Count == 15, "normal mode disables automatic replenishment");

        BenchmarkRuntime.ForceWorking = true; BenchmarkInputSupply.Clear();
        var native = new InputOutputModule();
        native.NativeDefinition = definition;
        var nativeInput = new Vector2Int(12, 0); var nativeEnergy = new Vector2Int(13, 0);
        native.NativeTerrain.Blocks[nativeInput] = new Block(); native.NativeTerrain.Blocks[nativeEnergy] = new Block();
        native.runtimeInputItemAreas.Add(new InputOutputModule.PersistentInputItemAreaState(nativeInput, 1));
        native.runtimeInputEnergyCoordinates.Add(nativeEnergy);
        var pair = new InputOutputModule.InputOutputPair();
        pair.inputs.Add(new InputOutputModule.ItemIoEntry { itemDefinition = InputOutputModule.Items[1], ResolvedItemCount = 3 });
        pair.outputs.Add(new InputOutputModule.ItemIoEntry { itemDefinition = InputOutputModule.Items[2], ResolvedItemCount = 1 });
        native.InputOutputPairs.Add(pair); native.FillNativeBenchmarkInputs();
        Check(native.NativeTerrain.Blocks[nativeInput].Count == native.RuntimeAreaMaxObjects
            && native.NativeTerrain.Blocks[nativeEnergy].Item == 3, "native recipe and energy adapter fills its configured IO cells");
        native.ConsumeNativeBenchmarkInputs(); native.ConsumeNativeBenchmarkInputs();
        Check(native.NativeTerrain.Blocks[nativeInput].Count == native.RuntimeAreaMaxObjects
            && native.NativeTerrain.Blocks[nativeInput].VirtualConsumes == 6, "native repeated ingredient consumption preserves stock");
        MapObjectTickManager.CurrentSimulationTick += 600; native.ConsumeNativeBenchmarkEnergy();
        Check(native.NativeTerrain.Blocks[nativeEnergy].Count == native.RuntimeAreaMaxObjects
            && native.NativeTerrain.Blocks[nativeEnergy].VirtualConsumes == 2, "native energy adapter simulates fuel consumption without depletion");
        var unpaired = new InputOutputModule(); var seed = new Vector2Int(14, 0);
        unpaired.NativeTerrain.Blocks[seed] = new Block();
        unpaired.runtimeInputItemAreas.Add(new InputOutputModule.PersistentInputItemAreaState(seed, 1));
        unpaired.InputList.Add(pair.inputs[0]); unpaired.FillNativeBenchmarkInputs(); unpaired.ConsumeNativeBenchmarkInputs();
        Check(unpaired.NativeTerrain.Blocks[seed].Count == unpaired.RuntimeAreaMaxObjects
            && unpaired.NativeTerrain.Blocks[seed].VirtualConsumes == 3, "service machine without a manufactured output still fills and simulates input");

        BenchmarkRuntime.ForceWorking = true; BenchmarkInputSupply.Clear();
        var world = new ProductionWorld();
        var input = new Vector2Int(8, 0); var output = new Vector2Int(9, 0);
        world.Terrain.Blocks[input] = new Block(); world.Terrain.Blocks[output] = new Block { Capacity = 100 };
        var recipe = new ProductionRenderTemplate.Recipe { OutputId = 2, OutputCount = 1, Duration = 1, OutputRate = 1 };
        recipe.Inputs.Add(new CraftingTreeRuntime.IngredientEntry(1, 2));
        var template = new ProductionRenderTemplate { Recipes = new[] { recipe }, Watts = 0, PrimaryRate = 0 };
        var state = new BlockStateStore.InstallationSaveState { itemFilterMaskInitialized = true };
        state.inputOutputState.inputItemAreas.Add(new InputOutputModule.PersistentInputItemAreaState(input, 1));
        state.inputOutputState.outputCoordinates.Add(output);
        var entity = world.Add(new ProductionMachine(), state, template);
        entity.Wake();
        int reserve = entity.Prototype.RuntimeAreaMaxObjects;
        Check(world.Terrain.Blocks[input].Count == reserve, "ECS wake fills selected recipe input before production starts");
        entity.ManagedUpdateTick(1f / 60);
        Check(world.Terrain.Blocks[input].Count == reserve && world.Terrain.Blocks[input].VirtualConsumes == 2, "ECS forced craft simulates its actual ingredient quantities");
        MapObjectTickManager.CurrentSimulationTick += 60; entity.ManagedUpdateTick(1f / 60);
        Check(world.Terrain.Blocks[input].Count == reserve && world.Terrain.Blocks[input].VirtualConsumes == 4 && world.Terrain.Blocks[output].Count == 1,
            "ECS repeated craft emits output and retains all input items");
        BenchmarkRuntime.ForceWorking = false; BenchmarkInputSupply.Clear();
        MapObjectTickManager.CurrentSimulationTick += 60; entity.ManagedUpdateTick(1f / 60);
        Check(world.Terrain.Blocks[input].Count == reserve - 2, "normal ECS crafting resumes real ingredient consumption");
        Console.WriteLine($"PASS: {checks} benchmark input supply and ECS consumption checks (animation/storage boundaries doubled).");
    }
}
