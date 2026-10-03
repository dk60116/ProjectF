namespace ProjectF.Tools.SaveLoadProfileHarness;

// Read-only evidence for recipe, fuel and placement restoration investigations.
internal static class FurnaceSaveReport
{
    internal static void Write(SaveGameData data)
    {
        Console.WriteLine($"Save version={data.version} savedUtc={new DateTime(data.savedAtUtcTicks, DateTimeKind.Utc):O} tick={data.simulationTick}");
        int shown = 0;
        foreach (var entry in data.map.installations)
        {
            var state = entry?.state;
            if (state == null || !state.itemName.Contains("Funance", StringComparison.OrdinalIgnoreCase)) continue;
            if (shown++ >= 20) { Console.WriteLine("Remaining furnace records omitted (report limit: 20)."); break; }
            var io = state.inputOutputState;
            Console.WriteLine($"{state.itemName} id={state.itemId} ({state.anchorCoordinate.x},{state.anchorCoordinate.y}) filterInitialized={state.itemFilterMaskInitialized} words={string.Join(",", state.itemFilterMaskWords.Select(w => w.ToString("X")))}");
            if (io == null) { Console.WriteLine("  Missing IO state"); continue; }
            Console.WriteLine($"  active={io.hasActiveCraft} waiting={io.waitingForOutput} recipe={io.activeRecipeIndex} output={io.activeOutputItemId} count={io.activeOutputCount} consumed={io.activeCraftConsumedEnergyUnits} fuel={io.storedEnergy} types={string.Join(",", io.storedEnergyTypes)} units={string.Join(",", io.storedEnergyUnitsByType)}");
            foreach (var c in io.inputEnergyCoordinates) PrintArea(data, "fuel", c);
            foreach (var a in io.inputItemAreas) PrintArea(data, $"input binding={a.itemId}", a.coordinate);
            foreach (var c in io.outputCoordinates) PrintArea(data, "output", c);
        }
    }
    private static void PrintArea(SaveGameData data, string name, UnityEngine.Vector2Int c)
    {
        var ids = data.map.floorObjects.FirstOrDefault(e => e.coordinate.x == c.x && e.coordinate.y == c.y)?.itemIds;
        int marker = ids?.IndexOf(Block.InputAreaCenterStackStateSentinel) ?? -1;
        var center = marker >= 0 && marker + 1 < ids.Count
            ? ids.Skip(marker + 2).Take(Math.Max(0, ids[marker + 1])).Where(id => id >= 0)
            : Enumerable.Empty<int>();
        Console.WriteLine($"  {name} ({c.x},{c.y}) center={string.Join(",", center.GroupBy(id => id).Select(g => $"{data.itemCatalog.FirstOrDefault(item => item.itemId == g.Key)?.itemName ?? g.Key.ToString()}:{g.Count()}"))}");
    }
}
