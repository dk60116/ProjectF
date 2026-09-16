using System;
using UnityEngine;

static class Checks
{
    private static int assertions;

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }

    private static BlockStateStore.InstallationSaveState CreateState()
    {
        return new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = new Vector2Int(10, 20),
            itemId = 3,
            quarterTurns = 1,
            stationColorAssigned = true,
            stationColor = new Color32 { r = 1, g = 2, b = 3, a = 255 },
            railVisualPathExtendsStart = true,
            railVisualPathExtendsEnd = false,
            railVisualPathPoints = { new Vector2(1, 2), new Vector2(3, 4) }
        };
    }

    private static void Main()
    {
        var first = CreateState();
        var second = CreateState();
        Require(BlockStateStore.SameMarker(first, second), "equal marker states must not invalidate the static map");

        second.unrelatedPayload++;
        Require(BlockStateStore.SameMarker(first, second), "non-visual payload must not invalidate the static map");

        second.itemId++;
        Require(!BlockStateStore.SameMarker(first, second), "item changes must invalidate the static map");
        second = CreateState(); second.anchorCoordinate = new Vector2Int(11, 20);
        Require(!BlockStateStore.SameMarker(first, second), "anchor changes must invalidate the static map");
        second = CreateState(); second.quarterTurns++;
        Require(!BlockStateStore.SameMarker(first, second), "rotation changes must invalidate the static map");
        second = CreateState(); second.stationColor = new Color32 { r = 9, g = 2, b = 3, a = 255 };
        Require(!BlockStateStore.SameMarker(first, second), "station color changes must invalidate the static map");
        second = CreateState(); second.railVisualPathPoints[1] = new Vector2(5, 4);
        Require(!BlockStateStore.SameMarker(first, second), "rail path changes must invalidate the static map");

        Console.WriteLine($"PASS: {assertions} map-paper layer and marker-version checks; production comparison extracted.");
    }
}
