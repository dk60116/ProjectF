using System;
using System.Collections.Generic;

public static class Checks
{
    private static int checks;

    public static void Main()
    {
        MultiplePipeOutputsRemainPlaced();
        UniqueOutputsDoNotRemovePipeOutputs();
        SerializedPlacementsKeepEveryPipeOutput();
        InputEnergyPlacementsRemainUnique();
        PipeOutputsAreNumberedByGridPosition();
        PipeOutputsResolveTheirOwnWorldCells();
        Console.WriteLine($"RectGrid placement checks passed: {checks}");
    }

    private static void MultiplePipeOutputsRemainPlaced()
    {
        var module = new InputOutputModule();
        module.SetRectGridBlock(0, 0, InputOutputModule.RectGridBlockType.Object);
        module.SetRectGridBlock(1, 0, InputOutputModule.RectGridBlockType.PipeOutputItem);
        module.SetRectGridBlock(2, 0, InputOutputModule.RectGridBlockType.PipeOutputItem);

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = module.Snapshot();
        Require(Count(placements, InputOutputModule.RectGridBlockType.PipeOutputItem) == 2,
            "placing a second PipeOutputItem must retain the first");
        Require(Contains(placements, 1, 0, InputOutputModule.RectGridBlockType.PipeOutputItem),
            "first PipeOutputItem cell must be retained");
        Require(Contains(placements, 2, 0, InputOutputModule.RectGridBlockType.PipeOutputItem),
            "second PipeOutputItem cell must be retained");
    }

    private static void UniqueOutputsDoNotRemovePipeOutputs()
    {
        var module = new InputOutputModule();
        module.SetRectGridBlock(0, 0, InputOutputModule.RectGridBlockType.PipeOutputItem);
        module.SetRectGridBlock(1, 0, InputOutputModule.RectGridBlockType.PipeOutputItem);
        module.SetRectGridBlock(2, 0, InputOutputModule.RectGridBlockType.Output);
        module.SetRectGridBlock(3, 0, InputOutputModule.RectGridBlockType.DoublePipeOutputItem);

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = module.Snapshot();
        Require(Count(placements, InputOutputModule.RectGridBlockType.PipeOutputItem) == 2,
            "placing a direct output must not remove repeated pipe outputs");
        Require(Count(placements, InputOutputModule.RectGridBlockType.Output) == 0,
            "combined direct output must replace the prior unique direct output");
        Require(Count(placements, InputOutputModule.RectGridBlockType.DoublePipeOutputItem) == 1,
            "latest unique direct output must remain");
    }

    private static void SerializedPlacementsKeepEveryPipeOutput()
    {
        var module = new InputOutputModule();
        module.LoadRaw(
            Placement(0, 0, InputOutputModule.RectGridBlockType.PipeOutputItem),
            Placement(1, 0, InputOutputModule.RectGridBlockType.PipeOutputItem),
            Placement(2, 0, InputOutputModule.RectGridBlockType.Output),
            Placement(3, 0, InputOutputModule.RectGridBlockType.Output));

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = module.Snapshot();
        Require(Count(placements, InputOutputModule.RectGridBlockType.PipeOutputItem) == 2,
            "normalization must retain every serialized pipe output");
        Require(Count(placements, InputOutputModule.RectGridBlockType.Output) == 1,
            "normalization must still collapse duplicate unique outputs");
    }

    private static void InputEnergyPlacementsRemainUnique()
    {
        var module = new InputOutputModule();
        module.SetRectGridBlock(0, 0, InputOutputModule.RectGridBlockType.InputEnergy);
        module.SetRectGridBlock(1, 0, InputOutputModule.RectGridBlockType.PipeInputEnergy);

        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements = module.Snapshot();
        Require(Count(placements, InputOutputModule.RectGridBlockType.InputEnergy) == 0,
            "new input-energy placement must replace the old input-energy variant");
        Require(Count(placements, InputOutputModule.RectGridBlockType.PipeInputEnergy) == 1,
            "latest input-energy placement must remain");
    }

    private static void PipeOutputsAreNumberedByGridPosition()
    {
        var module = new InputOutputModule();
        module.SetRectGridBlock(1, 0, InputOutputModule.RectGridBlockType.PipeOutputItem);
        module.SetRectGridBlock(4, 1, InputOutputModule.RectGridBlockType.PipeOutputItem);
        module.SetRectGridBlock(2, 1, InputOutputModule.RectGridBlockType.PipeOutputItem);

        Require(RectGridEditorNumbering.PipeOutputIndex(module, new UnityEngine.Vector2Int(2, 1)) == 1,
            "leftmost pipe output on the highest row must be numbered first");
        Require(RectGridEditorNumbering.PipeOutputIndex(module, new UnityEngine.Vector2Int(4, 1)) == 2,
            "pipe outputs on the same row must be numbered left to right");
        Require(RectGridEditorNumbering.PipeOutputIndex(module, new UnityEngine.Vector2Int(1, 0)) == 3,
            "lower-row pipe outputs must follow higher-row outputs");
        Require(RectGridEditorNumbering.PipeOutputIndex(module, new UnityEngine.Vector2Int(0, 0)) == -1,
            "an empty cell must not receive a pipe-output number");
    }

    private static void PipeOutputsResolveTheirOwnWorldCells()
    {
        var firstFluid = new ItemDefinition { id = 11 };
        var secondFluid = new ItemDefinition { id = 22 };
        var module = new InputOutputModule
        {
            PlacementCenterCell = new UnityEngine.Vector2Int(0, 1)
        };
        module.LoadRaw(
            Placement(2, 2, InputOutputModule.RectGridBlockType.Object),
            Placement(1, 2, InputOutputModule.RectGridBlockType.Object),
            Placement(3, 3, InputOutputModule.RectGridBlockType.PipeOutputItem, firstFluid),
            Placement(3, 2, InputOutputModule.RectGridBlockType.PipeOutputItem, secondFluid));

        var anchor = new UnityEngine.Vector2Int(100, 200);
        Require(
            module.TryGetRectGridBlockPlacementAtCoordinate(
                module,
                anchor,
                0,
                new UnityEngine.Vector2Int(102, 201),
                out InputOutputModule.RectGridBlockPlacement firstPlacement)
            && firstPlacement.itemDefinition == firstFluid,
            "the first pipe output must resolve from the placement-center object anchor");
        Require(
            module.TryGetRectGridBlockPlacementAtCoordinate(
                module,
                anchor,
                0,
                new UnityEngine.Vector2Int(102, 200),
                out InputOutputModule.RectGridBlockPlacement secondPlacement)
            && secondPlacement.itemDefinition == secondFluid,
            "each pipe output world cell must retain its own configured fluid");
        Require(
            module.TryGetRectGridBlockPlacementAtCoordinate(
                module,
                anchor,
                1,
                new UnityEngine.Vector2Int(101, 198),
                out InputOutputModule.RectGridBlockPlacement rotatedPlacement)
            && rotatedPlacement.itemDefinition == firstFluid,
            "rotating the machine must preserve the pipe output's configured fluid");
    }

    private static InputOutputModule.RectGridBlockPlacement Placement(
        int x,
        int y,
        InputOutputModule.RectGridBlockType blockType,
        ItemDefinition itemDefinition = null) =>
        new InputOutputModule.RectGridBlockPlacement(x, y, blockType, itemDefinition);

    private static int Count(
        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements,
        InputOutputModule.RectGridBlockType blockType)
    {
        int count = 0;
        for (int i = 0; i < placements.Count; i++)
        {
            if (placements[i].blockType == blockType)
            {
                count++;
            }
        }

        return count;
    }

    private static bool Contains(
        IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements,
        int x,
        int y,
        InputOutputModule.RectGridBlockType blockType)
    {
        for (int i = 0; i < placements.Count; i++)
        {
            InputOutputModule.RectGridBlockPlacement placement = placements[i];
            if (placement.x == x && placement.y == y && placement.blockType == blockType)
            {
                return true;
            }
        }

        return false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        checks++;
    }
}
