namespace ProjectF.Tools.SaveLoadProfileHarness;

// Reads the player's saved state instead of replacing connections with a fixture.
internal static class FluidOutputSaveReport
{
    internal static void WritePipeTopology(SaveGameData data)
    {
        Console.WriteLine($"Save version={data.version} savedUtc={new DateTime(data.savedAtUtcTicks, DateTimeKind.Utc):O}");
        foreach (var entry in data.map.installations)
        {
            var state = entry?.state;
            if (state == null || !(state.itemName == "Pipe" || state.itemName.Contains("Oil", StringComparison.OrdinalIgnoreCase)
                || state.itemName.Contains("Refinery", StringComparison.OrdinalIgnoreCase))) continue;
            var r = state.worldRotation;
            Console.WriteLine($"{state.itemName} ({state.anchorCoordinate.x},{state.anchorCoordinate.y}) variant={state.conveyorVariantKind} turns={state.quarterTurns} mask={state.pipeConnectionMask} pose={state.hasWorldPose} rotation=({r.x},{r.y},{r.z},{r.w}) fluid={state.storedFluidItemId}");
            if (state.inputOutputState == null) continue;
            foreach (var p in state.inputOutputState.outputCoordinates) Console.WriteLine($"  output=({p.x},{p.y})");
        }
    }
    internal static void Write(SaveGameData data)
    {
        Console.WriteLine($"Save version={data.version} savedUtc={new DateTime(data.savedAtUtcTicks, DateTimeKind.Utc):O}");
        foreach (InstallationSaveEntry entry in data.map.installations)
        {
            BlockStateStore.InstallationSaveState state = entry?.state;
            if (state == null) continue;
            if (state.itemName == "Fluid tank")
            {
                Console.WriteLine($"Tank ({state.anchorCoordinate.x},{state.anchorCoordinate.y}): fluid={state.storedFluidItemId} storedLiters={state.storedFluidLiters} units={state.storedFluidUnits}");
                continue;
            }
            if (state.itemName == "Pump")
            {
                Console.WriteLine($"Pump ({state.anchorCoordinate.x},{state.anchorCoordinate.y}): rotation={state.quarterTurns}");
                if (state.inputOutputState != null)
                    foreach (UnityEngine.Vector2Int port in state.inputOutputState.pipeInputCoordinates)
                        Console.WriteLine($"  pumpPort=({port.x},{port.y})");
                foreach (InstallationSaveEntry pipeEntry in data.map.installations)
                {
                    var pipe = pipeEntry?.state;
                    if (pipe?.itemName == "Pipe" && pipe.anchorCoordinate.x == state.anchorCoordinate.x)
                        Console.WriteLine($"  pipe=({pipe.anchorCoordinate.x},{pipe.anchorCoordinate.y}) mask={pipe.pipeConnectionMask}");
                }
                continue;
            }
            if (!state.itemName.StartsWith("Production machine", StringComparison.OrdinalIgnoreCase)
                || state.inputOutputState == null) continue;
            InputOutputModule.PersistentState process = state.inputOutputState;
            Console.WriteLine($"{state.itemName} ({state.anchorCoordinate.x},{state.anchorCoordinate.y}): active={process.hasActiveCraft} waiting={process.waitingForOutput} output={process.activeOutputItemId} count={process.activeOutputCount} recipe={process.activeRecipeIndex}");
            Console.WriteLine($"  remainingSeconds={process.remainingCraftTime} remainingTicks={process.remainingCraftTicks} consumedEnergy={process.activeCraftConsumedEnergy} pendingOutputUnits={process.productionOutputFluidUnits}");
            Console.WriteLine($"  rotation={state.quarterTurns}");
            Console.WriteLine($"  filterInitialized={state.itemFilterMaskInitialized} filterWords={string.Join(",", state.itemFilterMaskWords.Select(word => word.ToString("X")))}");
            for (int i = 0; i < process.productionInputFluidItemIds.Count && i < process.productionInputFluidUnits.Count; i++)
                Console.WriteLine($"  inputFluid={process.productionInputFluidItemIds[i]} units={process.productionInputFluidUnits[i]}");
            HashSet<string> inputCoordinates = new();
            foreach (var input in process.inputItemAreas)
            {
                string coordinate = $"({input.coordinate.x},{input.coordinate.y})";
                if (inputCoordinates.Add(coordinate)) Console.WriteLine($"  inputCoordinate={coordinate}");
            }
            foreach (UnityEngine.Vector2Int output in process.outputCoordinates)
            {
                Console.WriteLine($"  outputCoordinate=({output.x},{output.y})");
                foreach (InstallationSaveEntry pipeEntry in data.map.installations)
                {
                    BlockStateStore.InstallationSaveState pipe = pipeEntry?.state;
                    if (pipe?.itemName == "Pipe" && pipe.anchorCoordinate.y == output.y)
                        Console.WriteLine($"    pipe=({pipe.anchorCoordinate.x},{pipe.anchorCoordinate.y}) mask={pipe.pipeConnectionMask} rotation={pipe.quarterTurns}");
                }
            }
        }
    }
}
