using ProjectF.Benchmark;
using System.Collections.Generic;

public partial class InputOutputModule
{
    private int benchmarkInputVersion = -1, benchmarkInputOutput = -2;
    private void RefreshBenchmarkInputs()
    {
        if (!IsBenchmarkWorking) return;
        int output = ActiveOutputItemId;
        if (benchmarkInputVersion == BenchmarkInputSupply.Version && benchmarkInputOutput == output) return;
        benchmarkInputVersion = BenchmarkInputSupply.Version; benchmarkInputOutput = output;
        BenchmarkInputSupply.Remove(this);
        var terrain = ResolveTerrain(); var store = ResolveBlockStateStore();
        var inputs = ResolveBenchmarkInputs(output);
        if (inputs != null)
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                if (!BenchmarkRuntime.IsPortableItem(input.itemDefinition)) continue;
                for (int j = 0; j < runtimeInputItemAreas.Count; j++)
                {
                    var area = runtimeInputItemAreas[j];
                    if (area.itemId >= 0 && area.itemId != input.itemDefinition.id) continue;
                    BenchmarkInputSupply.Add(this, terrain, store, area.coordinate, input.itemDefinition.id, input.ResolvedItemCount, RuntimeAreaMaxObjects);
                }
            }
        BenchmarkInputSupply.AddEnergy(this, terrain, store, runtimeInputEnergyCoordinates, ResolveInstalledDefinition(), RuntimeAreaMaxObjects);
    }
    private IReadOnlyList<ItemIoEntry> ResolveBenchmarkInputs(int output)
    {
        foreach (var pair in InputOutputPairs)
        {
            if (pair?.outputs == null) continue;
            bool matches = false;
            for (int i = 0; i < pair.outputs.Count; i++) matches |= pair.outputs[i].itemDefinition?.id == output;
            if (matches) return pair.inputs;
        }
        return InputList;
    }
    private void ConsumeBenchmarkInputs()
    {
        RefreshBenchmarkInputs();
        if (!BenchmarkRuntime.ForceWorking || benchmarkInputOutput < 0) return;
        var inputs = ResolveBenchmarkInputs(benchmarkInputOutput);
        if (inputs != null)
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                if (!BenchmarkRuntime.IsPortableItem(input.itemDefinition)) continue;
                for (int j = 0; j < runtimeInputItemAreas.Count; j++)
                {
                    var area = runtimeInputItemAreas[j];
                    if (area.itemId >= 0 && area.itemId != input.itemDefinition.id) continue;
                    BenchmarkInputSupply.Consume(this, ResolveTerrain(), area.coordinate, input.itemDefinition.id,
                        input.ResolvedItemCount, ResolveConsumeTargetWorldPosition(), InputConsumeMoveInterval);
                    break;
                }
            }
    }
    private void SampleBenchmarkEnergy() => BenchmarkInputSupply.SampleEnergy(this, ResolveConsumeTargetWorldPosition(), hasActiveCraft);
}
