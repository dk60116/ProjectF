using System;
using System.Collections.Generic;

internal static class Checks
{
    private static int checks;

    private static ItemDefinition Item(int id, string itemName = null) =>
        new ItemDefinition { id = id, itemName = itemName ?? $"Item {id}" };

    private static void Equal<T>(T expected, T actual, string message)
    {
        checks++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
        }
    }

    public static void Main()
    {
        ItemDefinition iron = Item(1);
        ItemDefinition copper = Item(2);
        ItemDefinition plate = Item(3);
        ItemDefinition slag = Item(4);

        var legacy = new InputOutputModule();
        legacy.LoadLegacy(
            new[]
            {
                new InputOutputModule.ItemIoEntry(iron, 2),
                new InputOutputModule.ItemIoEntry(copper, 0)
            },
            new[]
            {
                new InputOutputModule.ItemIoEntry(plate, 3),
                new InputOutputModule.ItemIoEntry(slag, 1)
            },
            new InputOutputModule.ItemIoEntry(null, 1));
        Equal(2, legacy.LocalPairs.Count, "legacy pair count");
        Equal(2f, legacy.LocalPairs[0].inputs[0].count, "legacy input count");
        Equal(1f, legacy.LocalPairs[1].inputs[0].count, "legacy count normalization");
        Equal(4, legacy.LocalPairs[1].outputs[0].itemDefinition.id, "legacy output mapping");

        var multiPair = new InputOutputModule.InputOutputPair();
        multiPair.inputs.Add(new InputOutputModule.ItemIoEntry(iron, 2));
        multiPair.inputs.Add(new InputOutputModule.ItemIoEntry(copper, 3));
        multiPair.outputs.Add(new InputOutputModule.ItemIoEntry(plate, 4));
        multiPair.outputs.Add(new InputOutputModule.ItemIoEntry(slag, 5));
        var child = new InputOutputModule();
        child.LoadPairs(multiPair);
        Equal(2, child.LocalInputs.Count, "multiple local inputs retained");
        Equal(2, child.LocalOutputs.Count, "multiple local outputs retained");
        Equal(3f, child.LocalInputs[1].count, "second input count retained");
        Equal(5f, child.LocalOutputs[1].count, "second output count retained");

        ItemDefinition water = Item(5, "Water");
        var fluidPair = new InputOutputModule.InputOutputPair(
            new InputOutputModule.ItemIoEntry(water, 0.25f),
            new InputOutputModule.ItemIoEntry(water, 1.75f));
        var fluidModule = new InputOutputModule();
        fluidModule.LoadPairs(fluidPair);
        Equal(true, fluidModule.LocalInputs[0].IsFluid, "fluid item detection");
        Equal(0.25f, fluidModule.LocalInputs[0].count, "fractional fluid input retained");
        Equal(1.75f, fluidModule.LocalOutputs[0].count, "fractional fluid output retained");

        var discretePair = new InputOutputModule.InputOutputPair(
            new InputOutputModule.ItemIoEntry(iron, 2.6f),
            new InputOutputModule.ItemIoEntry(plate, 1f));
        var discreteModule = new InputOutputModule();
        discreteModule.LoadPairs(discretePair);
        Equal(3f, discreteModule.LocalInputs[0].count, "non-fluid count normalized to integer");

        var parentPair = new InputOutputModule.InputOutputPair(
            new InputOutputModule.ItemIoEntry(iron, 1),
            new InputOutputModule.ItemIoEntry(plate, 1));
        var parent = new InputOutputModule();
        parent.LoadPairs(parentPair);
        child.SetParent(parent);
        Equal(2, child.EffectivePairs.Count, "parent and child pair grouping retained");
        Equal(3, child.EffectiveInputs.Count, "effective inputs flattened without loss");
        Equal(3, child.EffectiveOutputs.Count, "effective outputs flattened without loss");
        Equal(2, child.EffectivePairs[1].inputs.Count, "child pair input cardinality retained");
        Equal(2, child.EffectivePairs[1].outputs.Count, "child pair output cardinality retained");

        Console.WriteLine($"InputOutputPair harness passed: {checks} checks");
    }
}
