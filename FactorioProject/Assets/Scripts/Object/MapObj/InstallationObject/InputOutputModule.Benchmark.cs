using System;
using ProjectF.Benchmark;
using ProjectF.Simulation;
using UnityEngine;

public partial class InputOutputModule
{
    internal bool IsBenchmarkWorking => BenchmarkRuntime.ForceWorking && !(this is BoxObject);

    internal void ResetBenchmarkWork()
    {
        ClearActiveCraft();
        if (this is SteamGenerator generator) generator.SetBenchmarkGeneration(false);
        MarkManagedRuntimeVisualsDirty();
        WakeRuntimeUpdate();
    }

    private void ApplyBenchmarkWork(float deltaTime)
    {
        var definition = ResolveInstalledDefinition();
        if (definition == null || deltaTime <= 0f) return;
        lastOperationalEnergySupplyRatio = 1f;
        if (this is SteamGenerator generator) generator.SetBenchmarkGeneration(true);
        runtimeSleeping = false;
        if (!hasActiveCraft) BeginBenchmarkCraft(definition);
        if (!hasActiveCraft) return;
        bool powered = RequiresOperationalEnergy(definition);
        long required = powered ? DeterministicSimulationUnits.FromFloat(ResolveCompleteEnergy(definition)) : 0;
        long supplied = powered ? DeterministicSimulationUnits.RateForTicks(
            ItemDefinition.ResolveUseEnergyRatePerSecond(definition), DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime)) : 0;
        if (production.Advance(powered ? 0 : DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime), powered, supplied, required))
        {
            EmitBenchmarkOutput(definition);
            ClearActiveCraft();
            BeginBenchmarkCraft(definition);
        }
        MarkManagedRuntimeVisualsDirty();
        SetRuntimeSleeping(false);
    }

    private void BeginBenchmarkCraft(ItemDefinition definition)
    {
        var outputs = OutputList;
        int outputIndex = -1;
        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].itemDefinition == null) continue;
            if (outputIndex < 0) outputIndex = i;
            if (this is ProductionMachine machine && machine.IsProductionTargetSelected(outputs[i].itemDefinition.id))
            { outputIndex = i; break; }
        }
        int itemId = outputIndex >= 0 ? outputs[outputIndex].itemDefinition.id : BenchmarkRuntime.FallbackItemId;
        int count = outputIndex >= 0 ? outputs[outputIndex].ResolvedItemCount : 1;
        if (itemId >= 0) BeginActiveCraft(outputIndex, itemId, count, definition);
    }

    private void EmitBenchmarkOutput(ItemDefinition definition)
    {
        // Transport and crop service machines have no manufactured portable
        // product. Keep their work clock/visuals running without inventing one.
        if (this is Pump || this is Sprinkler || this is SeedPlanter) return;
        var outputs = OutputList;
        float duration = ResolveInitialCraftDuration(definition, ActiveOutputItemId);
        if (this is CrudeOilRefinery)
        {
            for (int i = 0; i < outputs.Count; i++)
                if (outputs[i].itemDefinition != null)
                    EmitBenchmarkProduct(outputs[i].itemDefinition.id, outputs[i].ResolvedAmount, duration);
        }
        else EmitBenchmarkProduct(ActiveOutputItemId,
            ActiveRecipeIndex >= 0 && ActiveRecipeIndex < outputs.Count ? outputs[ActiveRecipeIndex].ResolvedAmount : ActiveOutputCount, duration);
    }

    private void EmitBenchmarkProduct(int itemId, float amount, float duration)
    {
        if (IsFluidItemId(itemId))
        {
            float liters = amount * duration;
            TryEmitFluidOutputToConnectedStorages(itemId, liters, MapClimate.DefaultCurrentTemperatureCelsius, out float accepted);
            // Open outputs spill excess fluid. A benchmark producer never stalls on
            // backpressure; the tool reports spill separately from physical delivery.
            BenchmarkRuntime.RecordSpill(liters - accepted);
            return;
        }
        if (ItemDefinition.IsElectricityItemDefinition(ResolveItemDefinition(itemId))) return;
        int count = Mathf.Max(1, Mathf.RoundToInt(amount));
        if (TryEmitOutputItems(itemId, count, transform.position))
        { BenchmarkRuntime.RecordItems(count); return; }
        var terrain = TerrainGenerator.ResolveActive();
        for (int i = 0; i < count; i++) BenchmarkRuntime.EmitItem(terrain, itemId, transform.position);
    }
}
