using System;
using UnityEngine;

public sealed partial class ProductionFacilityInstance
{
    private ProductionRenderTemplate.Recipe InfoRecipe => ActiveRecipe ?? (Prototype is ProductionMachine
        ? SelectedRecipe : ResolveAutomaticRecipe(availableOnly: false, fallbackToFirstRecipe: false));
    public bool TryGetObjectInfoProductionIngredientCount(out int count)
    { count = InfoRecipe?.Inputs.Count ?? 0; return count > 0; }
    public bool TryGetObjectInfoProductionIngredient(int index, out int item, out int required, out int count, out int capacity)
    {
        item = -1; required = count = capacity = 0;
        var recipe = InfoRecipe;
        if (recipe == null || index < 0 || index >= recipe.Inputs.Count) return false;
        var ingredient = recipe.Inputs[index]; item = ingredient.itemId; required = ingredient.count;
        if (InputOutputModule.ResolveItemDefinition(item)?.isFluid == true)
        {
            required = Mathf.CeilToInt(DeterministicSimulationUnits.ToFloat(RequiredFluidUnits(recipe, ingredient.amount)));
            count = Mathf.FloorToInt(DeterministicSimulationUnits.ToFloat(FluidUnits(item))); capacity = required;
        }
        else
        {
            count = IngredientCount(item);
            for (int i = 0; i < Io.inputItemAreas.Count; i++)
                if (Io.inputItemAreas[i].itemId < 0 || Io.inputItemAreas[i].itemId == item)
                    capacity = (int)Math.Min(int.MaxValue, (long)capacity + ItemDefinition.ResolveStackCapacity(InputOutputModule.ResolveItemDefinition(item), Prototype.RuntimeAreaMaxObjects));
        }
        return true;
    }
    public bool TryGetObjectInfoProductionFluidIngredient(int index, out int item, out float stored, out float required)
    {
        item = -1; stored = required = 0;
        var recipe = InfoRecipe;
        if (recipe == null || index < 0 || index >= recipe.Inputs.Count) return false;
        var ingredient = recipe.Inputs[index];
        if (InputOutputModule.ResolveItemDefinition(ingredient.itemId)?.isFluid != true) return false;
        item = ingredient.itemId; stored = DeterministicSimulationUnits.ToFloat(FluidUnits(item));
        required = DeterministicSimulationUnits.ToFloat(RequiredFluidUnits(recipe, ingredient.amount)); return required > 0;
    }
    public bool TryGetObjectInfoProductionFluidOutput(out int item, out float rate, out float stored, out float capacity)
    {
        item = -1; rate = stored = capacity = 0;
        var recipe = InfoRecipe;
        if (recipe == null || InputOutputModule.ResolveItemDefinition(recipe.OutputId)?.isFluid != true) return false;
        item = recipe.OutputId; rate = recipe.OutputRate; capacity = rate * recipe.Duration;
        stored = IsWaitingForOutput ? Io.productionOutputFluidUnits < 0 ? capacity : DeterministicSimulationUnits.ToFloat(Io.productionOutputFluidUnits) : 0;
        return true;
    }
    public bool TryGetObjectInfoProductionFluidGauge(int index, out ProductionMachine.FluidGaugeState state)
    {
        state = default;
        if (!TryGetObjectInfoProductionFluidIngredient(index, out int input, out float stored, out float required)) return false;
        if (HasActiveWork && TryGetObjectInfoProductionFluidOutput(out int output, out _, out float reserve, out float batch))
            state = IsWaitingForOutput
                ? new ProductionMachine.FluidGaugeState(input, output, Mathf.Clamp01(reserve / batch), 0, reserve, batch, false)
                : new ProductionMachine.FluidGaugeState(input, output, 1, WorkProgress, batch * WorkProgress, batch, true);
        else
        {
            float current = HasActiveWork ? required : stored;
            state = new ProductionMachine.FluidGaugeState(input, -1, Mathf.Clamp01(current / required), 0, current, required, false);
        }
        return true;
    }
    public float GetStoredFluidTemperatureCelsius(int item) => MapClimate.CurrentTemperatureCelsius;
    public void GetFuelGauge(out long current, out long capacity)
    {
        current = capacity = 0;
        if (Io.storedEnergyTypes.Count == 0) return;
        current = Io.storedEnergyUnitsByType[0]; capacity = Io.energyGaugeCapacityUnitsByType[0];
        if (Data.HasTarget && Data.SupplyRatio > 0 && !ProjectF.Benchmark.BenchmarkRuntime.ForceWorking)
            current = Math.Max(0, current - DeterministicSimulationUnits.RateForTicks(
                ItemDefinition.ResolveUseEnergyRatePerSecond(Template.Definition, (ItemDefinition.EnergyType)Io.storedEnergyTypes[0]),
                Math.Max(0, MapObjectTickManager.CurrentSimulationTick - Data.SampleTick)));
    }
    public bool TryGetFuelInputInfo(out int item, out int count, out int capacity, out int burnEnergy)
    {
        item = -1; count = capacity = burnEnergy = 0;
        if (!HasFuelRequirement || Io.inputEnergyCoordinates.Count == 0) return false;
        double totalEnergy = 0;
        for (int i = 0; i < Io.inputEnergyCoordinates.Count; i++)
        {
            var coordinate = Io.inputEnergyCoordinates[i];
            bool loaded = World.Terrain.TryGetLoadedBlock(coordinate, out var block) && !World.Terrain.IsFloorObjectCoordinateVirtualized(coordinate);
            int current = loaded ? block.GetInputAreaCenterItemId() : World.Store.GetSavedCenterTopItemId(coordinate);
            var fuel = InputOutputModule.ResolveItemDefinition(current);
            int stored = loaded ? block.GetInputAreaCenterItemCount(current) : World.Store.GetSavedCenterItemCount(coordinate, current);
            if (item < 0 && current >= 0) item = current;
            if (current == item) count = (int)Math.Min(int.MaxValue, (long)count + stored);
            capacity = (int)Math.Min(int.MaxValue, (long)capacity + (loaded ? block.GetInputAreaCenterCapacity(current) : Prototype.RuntimeAreaMaxObjects));
            if (fuel?.energyType == ItemDefinition.EnergyType.Burn) totalEnergy += stored * (double)fuel.energyAmount;
        }
        burnEnergy = (int)Math.Min(int.MaxValue, Math.Max(0, totalEnergy));
        return true;
    }
    public bool TryGetObjectInfoProductionOutput(out int item, out int count, out int capacity)
    {
        item = InfoRecipe?.OutputId ?? -1; count = capacity = 0;
        // An idle automatic facility has no preview recipe. Still show items left in its output.
        if (item < 0 && !(Prototype is ProductionMachine))
            for (int i = 0; i < OutputCoordinates.Count; i++)
            {
                var coordinate = OutputCoordinates[i];
                int storedItem = World.Terrain.TryGetLoadedBlock(coordinate, out var block)
                    && !World.Terrain.IsFloorObjectCoordinateVirtualized(coordinate)
                    ? block.GetInputAreaCenterItemId() : World.Store.GetSavedCenterTopItemId(coordinate);
                if (storedItem >= 0) { item = storedItem; break; }
            }
        if (item < 0) return false;
        for (int i = 0; i < OutputCoordinates.Count; i++)
        {
            if (World.Terrain.TryGetLoadedBlock(OutputCoordinates[i], out var block)
                && !World.Terrain.IsFloorObjectCoordinateVirtualized(OutputCoordinates[i]))
            {
                count = (int)Math.Min(int.MaxValue, (long)count + block.GetInputAreaCenterItemCount(item));
                capacity = (int)Math.Min(int.MaxValue, (long)capacity + block.GetInputAreaCenterCapacity(item));
            }
            else
            {
                count = (int)Math.Min(int.MaxValue, (long)count + World.Store.GetSavedCenterItemCount(OutputCoordinates[i], item));
                int max = ProjectF.Benchmark.BenchmarkRuntime.ForceWorking ? int.MaxValue
                    : ItemDefinition.ResolveStackCapacity(InputOutputModule.ResolveItemDefinition(item), Prototype.RuntimeAreaMaxObjects);
                capacity = (int)Math.Min(int.MaxValue, (long)capacity + max);
            }
        }
        return true;
    }
}
