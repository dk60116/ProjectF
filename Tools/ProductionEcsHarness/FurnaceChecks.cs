using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEngine;

// Loads the real furnace recipes and ItemDefinition energy values, then drives both real scheduler layers.
// Scene objects, stacks and particle drawing remain explicit engine boundary doubles.
static class FurnaceChecks
{
    static int checks;
    static readonly Vector2Int FuelCoordinate = new(80, 0), OutputCoordinate = new(90, 0);
    static readonly Dictionary<string, ItemDefinition> itemsByGuid = new();
    static readonly Dictionary<int, string> names = new();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static string Field(string text, string name) => Regex.Match(text, @"(?m)^  " + name + @": (.+)$").Groups[1].Value.Trim();
    static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
    static long Units(float value) => DeterministicSimulationUnits.FromFloat(value);
    static void Steps(long count) { for (long i = 0; i < count; i++) MapObjectTickManager.Step(); }
    static void Unregister(ProductionFacilityInstance facility) => FacilitySimulationWorld.Unregister(facility);
    static void LoadItems(string root)
    {
        foreach (string path in Directory.GetFiles(Path.Combine(root, "FactorioProject/Assets/Data/Items"), "*.asset"))
        {
            string text = File.ReadAllText(path), guid = Regex.Match(File.ReadAllText(path + ".meta"), @"guid: (\w+)").Groups[1].Value;
            int id = int.Parse(Field(text, "id"));
            var item = new ItemDefinition { id = id, isFluid = Field(text, "isFluid") == "1" };
            if (Field(text, "energyType") == "1") { item.energyType = ItemDefinition.EnergyType.Burn; item.energyAmount = Number(Field(text, "energyAmount")); }
            itemsByGuid.Add(guid, item); names.Add(id, Field(text, "itemName")); InputOutputModule.Items[id] = item;
        }
    }
    static ProductionRenderTemplate LoadTemplate(string root, bool steel)
    {
        string definition = File.ReadAllText(Path.Combine(root, steel ? "FactorioProject/Assets/Data/Items/Item_120_Steel Funance.asset" : "FactorioProject/Assets/Data/Items/Item_13_Funance.asset"));
        float rate = Number(Regex.Match(definition, @"useEnergyAmount: ([\d.]+)").Groups[1].Value);
        float energy = Number(Regex.Match(definition, @"completeEnergy: ([\d.]+)").Groups[1].Value);
        var template = new ProductionRenderTemplate { Watts = 0, PrimaryRate = rate, Definition = new ItemDefinition { id = int.Parse(Field(definition, "id")) } };
        template.Definition.Requirements.Add(new ItemDefinition.EnergyUseRequirement { energyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = rate });
        var source = new InputOutputModule();
        template.Definition.CompleteEnergy = energy;
        foreach (string relative in steel ? new[] { "Funance/Furnace.prefab", "Steel Funance/Steel Furnace.prefab" } : new[] { "Funance/Furnace.prefab" })
        {
            string text = File.ReadAllText(Path.Combine(root, "FactorioProject/Assets/MapObject/InputOutputModule", relative));
            string pairs = text.Split("  inputOutputPairs:")[1].Split("  inputList:")[0];
            foreach (string pair in pairs.Split("  - inputs:").Skip(1))
            {
                var halves = pair.Split("    outputs:");
                var parsedPair = new InputOutputModule.InputOutputPair();
                foreach (Match input in Regex.Matches(halves[0], @"guid: (\w+), type: 2}\s+count: ([\d.]+)"))
                    parsedPair.inputs.Add(new InputOutputModule.ItemIoEntry(itemsByGuid[input.Groups[1].Value], Number(input.Groups[2].Value)));
                foreach (Match output in Regex.Matches(halves[1], @"guid: (\w+), type: 2}\s+count: ([\d.]+)"))
                    parsedPair.outputs.Add(new InputOutputModule.ItemIoEntry(itemsByGuid[output.Groups[1].Value], Number(output.Groups[2].Value)));
                source.InputOutputPairs.Add(parsedPair);
            }
        }
        string layout = File.ReadAllText(Path.Combine(root, "FactorioProject/Assets/MapObject/InputOutputModule", steel ? "Steel Funance/Steel Furnace.prefab" : "Funance/Furnace.prefab"));
        string placements = layout.Split("  rectGridPlacements:")[1].Split("  craftDuration:")[0];
        template.InputPortCount = Regex.Matches(placements, @"blockType: 3\b").Count;
        template.BuildRecipes(source); return template;
    }
    static (ProductionWorld world, ProductionFacilityInstance value, Block fuel, Block output) Create(ProductionRenderTemplate template, int selected, bool stocked = true, bool rejectFilter = false, bool saved = false)
    {
        var world = new ProductionWorld();
        var state = new BlockStateStore.InstallationSaveState { itemId = template.Definition.id, placementSequence = 10, itemFilterMaskInitialized = rejectFilter };
        if (rejectFilter) state.itemFilterMaskWords.AddRange(new ulong[] { 0, 0, 0 });
        state.inputOutputState.outputCoordinates.Add(OutputCoordinate); state.inputOutputState.inputEnergyCoordinates.Add(FuelCoordinate);
        var recipe = template.Recipes[selected];
        var destination = saved ? world.Store.Saved : world.Terrain.Blocks;
        for (int i = 0; i < template.InputPortCount; i++)
        {
            var coordinate = new Vector2Int(10 + i, 0); state.inputOutputState.gridCoordinates.Add(coordinate);
            foreach (int item in template.Recipes.SelectMany(r => r.Inputs).Select(input => input.itemId).Distinct())
                state.inputOutputState.inputItemAreas.Add(new InputOutputModule.PersistentInputItemAreaState(coordinate, item));
            destination[coordinate] = i < recipe.Inputs.Count
                ? new Block { Item = recipe.Inputs[i].itemId, Count = stocked ? recipe.Inputs[i].count : 0 } : new Block();
        }
        state.inputOutputState.gridCoordinates.AddRange(new[] { FuelCoordinate, OutputCoordinate });
        var fuel = new Block { Item = itemsByGuid.Values.Single(item => names[item.id] == "Coal").id, Count = stocked ? 10 : 0 };
        var output = new Block(); destination[FuelCoordinate] = fuel; destination[OutputCoordinate] = output;
        state.occupiedCoordinates.AddRange(state.inputOutputState.gridCoordinates);
        foreach (var pair in destination) pair.Value.Coordinate = pair.Key;
        var value = world.Register(world.CacheTemplate(template), state);
        Check(FacilitySimulationWorld.IsScheduled(value), "Actual registration preserves the first wake raised by synchronous block binding");
        return (world, value, fuel, output);
    }
    static void Main(string[] args)
    {
        LoadItems(args[0]);
        // Reproduce placement while a temporary scene adapter and another facility are ticking.
        var otherFacility = new TickProbe();
        var temporaryPresentation = new TickProbe();
        FacilitySimulationWorld.Register(otherFacility, true);
        FacilitySimulationWorld.Register(temporaryPresentation, true);
        Steps(1);
        var installed = Create(LoadTemplate(args[0], false), 3);
        installed.fuel.Count = 3;
        installed.world.Terrain.Blocks[new Vector2Int(10, 0)].Count = 5;
        FacilitySimulationWorld.Unregister(temporaryPresentation);
        Steps(1);
        Check(otherFacility.Ticks == 2 && installed.value.IsWorking,
            "Stone 5 / Coal 3 furnace starts after temporary presentation release while other facilities keep running");
        Steps(installed.value.NextUpdateTick - MapObjectTickManager.CurrentSimulationTick);
        Check(installed.output.Count == 1 && installed.value.IsWorking,
            "Newly installed furnace emits Brick and starts its next batch");
        Unregister(installed.value);
        FacilitySimulationWorld.Unregister(otherFacility);
        foreach (bool steel in new[] { false, true })
        {
            var template = LoadTemplate(args[0], steel);
            Check(template.InputPortCount == (steel ? 2 : 1), "Actual furnace input port count matches prefab");
            Check(template.Recipes.Length == (steel ? 9 : 8), "Actual inherited furnace recipes are all present");
            foreach (bool saved in new[] { false, true })
            for (int index = 0; index < template.Recipes.Length; index++)
            {
                var a = Create(template, index, rejectFilter: true, saved: saved); var recipe = template.Recipes[index];
                var accepted = new HashSet<int>();
                Check(a.world.AppendInputItemIds(new Vector2Int(10, 0), accepted, true) && recipe.Inputs.All(input => accepted.Contains(input.itemId)), "Input permission bridge accepts the actual recipe despite unrelated output filters");
                Steps(1); a.value.GetObjectInfoStatus(out string status, out _, out _);
                Check(a.value.IsWorking && a.value.OutputItemId == recipe.OutputId && status == "Working", $"{names[template.Definition.id]} {index} starts with input, coal and an unrelated zero filter ({saved})");
                a.value.TryGetObjectInfoProductionIngredient(0, out int item, out _, out _, out _);
                Check(item == recipe.Inputs[0].itemId, "Info panel follows the recipe actually consuming inputs");
                long deadline = a.value.NextUpdateTick;
                Steps(deadline - MapObjectTickManager.CurrentSimulationTick);
                Check(a.output.Item == recipe.OutputId && a.output.Count == recipe.OutputCount && !a.value.HasActiveWork, "Scheduler emits exactly the configured batch");
                a.value.GetObjectInfoStatus(out status, out bool green, out bool yellow);
                Check(status == "Waiting for materials" && !green && yellow && a.fuel.Count == 9, "Finished furnace waits with yellow status and buffered fuel");
                Unregister(a.value);
            }
            var sleeping = Create(template, 3, stocked: false); Steps(1);
            Check(!sleeping.value.TryGetObjectInfoProductionIngredientCount(out int emptyInputs) && emptyInputs == 0,
                "Empty actual furnace assets show no default Log ingredient");
            Check(!sleeping.value.TryGetObjectInfoProductionOutput(out _, out _, out _),
                "Empty actual furnace assets show no default Charcoal output");
            sleeping.value.GetObjectInfoStatus(out string emptyStatus, out _, out _);
            Check(emptyStatus == "No energy" && !FacilitySimulationWorld.IsScheduled(sleeping.value), "Empty furnace sleeps red");
            sleeping.fuel.Count = 4; InputOutputModule.WakeRuntimeModulesAtCoordinate(FuelCoordinate); Steps(1);
            sleeping.value.GetObjectInfoStatus(out emptyStatus, out _, out bool waiting);
            Check(emptyStatus == "Waiting for materials" && waiting, "Fuel notification wakes actual scheduler");
            sleeping.world.Terrain.Blocks[new Vector2Int(10, 0)].Count = 5;
            InputOutputModule.WakeRuntimeModulesAtCoordinate(new Vector2Int(10, 0)); Steps(1);
            Check(sleeping.value.IsWorking, "Stone arrival starts automatic Brick recipe");
            sleeping.output.Item = template.Recipes[3].OutputId; sleeping.output.Count = 10;
            Steps(sleeping.value.NextUpdateTick - MapObjectTickManager.CurrentSimulationTick);
            sleeping.value.GetObjectInfoStatus(out emptyStatus, out _, out waiting);
            Check(emptyStatus == "Waiting for output" && waiting && sleeping.value.IsWaitingForOutput, "Full output holds batch and warns yellow");
            int inputs = sleeping.world.Terrain.Blocks[new Vector2Int(10, 0)].Count;
            Check(inputs == 4, "Only one Stone consumed while output blocked");
            sleeping.output.Count = 0; InputOutputModule.WakeRuntimeOutputModulesAtCoordinate(OutputCoordinate); Steps(1);
            Check(sleeping.output.Count == 1 && sleeping.value.IsWorking, "Output notification drains Brick and starts next batch");
            Unregister(sleeping.value);
            var depleted = Create(template, 3);
            depleted.fuel.Count = 0;
            var depletedIo = depleted.value.Placement.inputOutputState;
            depletedIo.storedEnergyTypes.Add((int)ItemDefinition.EnergyType.Burn);
            depletedIo.storedEnergyUnitsByType.Add(template.Recipes[3].Energy);
            depletedIo.energyGaugeCapacityUnitsByType.Add(template.Recipes[3].Energy);
            Steps(1);
            InputOutputModule.OutputMutation = () => InputOutputModule.WakeRuntimeOutputModulesAtCoordinate(OutputCoordinate);
            Steps(depleted.value.NextUpdateTick - MapObjectTickManager.CurrentSimulationTick);
            InputOutputModule.OutputMutation = null;
            Check(depleted.output.Count == 1 && !FacilitySimulationWorld.IsScheduled(depleted.value), "Completed batch drains when fuel depletes and the furnace sleeps");
            depleted.fuel.Count = 1;
            depleted.world.Terrain.Blocks[new Vector2Int(10, 0)].Count = 1;
            InputOutputModule.WakeRuntimeModulesAtCoordinate(FuelCoordinate); Steps(1);
            Check(depleted.value.IsWorking, "A notification raised during output must not suppress the next fuel arrival wake");
            Unregister(depleted.value);
            // Alternatives share the same output, so the recipe index is the identity to preserve.
            var glass = Create(template, 6); Steps(1); Steps(60); glass.value.Persist();
            glass.value.Placement.inputOutputState = SaveProbe.Roundtrip(glass.value.Placement.inputOutputState);
            Unregister(glass.value); glass.world.Remove(glass.value);
            var restored = glass.world.Add(glass.value.Prototype, glass.value.Placement, template);
            FacilitySimulationWorld.Register(restored, false); restored.Wake(); Steps(1);
            Check(ReferenceEquals(restored.ActiveRecipe, template.Recipes[6]) && restored.WorkProgress > 0, "Binary restore preserves alternative glass ingredients and progress");
            Steps(restored.NextUpdateTick - MapObjectTickManager.CurrentSimulationTick);
            Check(glass.output.Count == 1 && glass.output.Item == template.Recipes[6].OutputId, "Restored furnace emits once without consuming ingredients again");
            Unregister(restored);
            var manualRecipe = template.Recipes[4];
            var manualManager = GameManager.Instance.ItemManger;
            manualManager.BlockedTargets.Add(manualRecipe.OutputId);
            var manualTemplate = LoadTemplate(args[0], steel);
            var manual = Create(manualTemplate, 4); Steps(1);
            Check(!manual.value.HasActiveWork && manual.world.Terrain.Blocks[new Vector2Int(10, 0)].Consumed == 0, "A recipe requiring an unavailable manual never consumes materials");
            manual.world.RegisterTemplateForAvailability(manualTemplate);
            manualManager.BlockedTargets.Remove(manualRecipe.OutputId); Steps(1);
            manual.world.RefreshRecipeAvailability(); Steps(1);
            Check(manual.value.IsWorking && manual.value.OutputItemId == manualRecipe.OutputId, "Acquiring the manual resumes a sleeping automatic furnace");
            Unregister(manual.value);
            // A facility evaluated before GameManager's item manager exists must be reawakened.
            var manager = GameManager.Instance.ItemManger; GameManager.Instance.ItemManger = null;
            var earlyTemplate = LoadTemplate(args[0], steel);
            var early = Create(earlyTemplate, 3); Steps(1);
            Check(!early.value.IsWorking, "Missing item manager prevents an early craft");
            GameManager.Instance.ItemManger = manager;
            early.world.RegisterTemplateForAvailability(earlyTemplate);
            early.world.RefreshRecipeAvailability(); Steps(1);
            Check(early.value.IsWorking, "Shared availability publication wakes recipes after manager initialization");
            Unregister(early.value);
        }
        MapObjectTickManager.SetDefaultInterval(.1f);
        var clockTemplate = LoadTemplate(args[0], true);
        var clockProbe = Create(clockTemplate, 3); Steps(1);
        Check(clockProbe.value.IsWorking, "A facility starts when the native default interval is 0.1 seconds");
        var newWhileRunning = Create(clockTemplate, 3);
        newWhileRunning.value.GetObjectInfoStatus(out string queuedStatus, out _, out bool queuedWarning);
        Check(queuedStatus == "Waiting for initialization" && queuedWarning, "Queued first evaluation is not falsely reported as a missing target or manual");
        Steps(1);
        Check(newWhileRunning.value.IsWorking, "A newly installed furnace receives its first evaluation while other facilities keep the global clock registered");
        Unregister(newWhileRunning.value);
        Steps(clockProbe.value.NextUpdateTick - MapObjectTickManager.CurrentSimulationTick + 6);
        Check(clockProbe.output.Count == 1, "Absolute completion deadline survives a default interval not dividing the furnace duration");
        Unregister(clockProbe.value);
        Console.WriteLine($"PASS: {checks} furnace integration checks using actual two-furnace assets, production entity, binary IO serializer, facility scheduler and global clock (engine boundaries doubled).");
    }

    sealed class TickProbe : IMapObjectUpdateTick
    {
        internal int Ticks;
        public void ManagedUpdateTick(float deltaTime) => Ticks++;
    }
}
