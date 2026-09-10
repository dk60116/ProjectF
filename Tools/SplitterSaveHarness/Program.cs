using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private static void Main()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] {
                Path.GetDirectoryName(typeof(SaveGameBinarySerializer).Assembly.Location),
                Path.GetFullPath("FactorioProject/Library/ScriptAssemblies"),
                "C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine" })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        RunChecks();
    }

    private static void RunChecks()
    {
        Type serializer = typeof(SaveGameBinarySerializer);
        MethodInfo write = serializer.GetMethod("WriteInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo read = serializer.GetMethod("ReadInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
        Type compatibilityType = read.GetParameters()[2].ParameterType;
        object current = Enum.ToObject(compatibilityType, 0);
        int cases = 0;
        for (int mode = 0; mode < 3; mode++)
        for (int input = 0; input < 2; input++)
        for (int output = 0; output < 2; output++)
        for (int wheels = 0; wheels < 4; wheels++)
        {
            var state = new BlockStateStore.InstallationSaveState
            {
                itemId = 46,
                itemName = "Spliter belt",
                itemFilterMaskInitialized = true,
                itemFilterMaskWords = new System.Collections.Generic.List<ulong> { 1UL << 42 },
                boxMinimumRetainedItemCount = wheels + 2,
                boxMaximumStoredItemCount = wheels + 5,
                loggingMinimumGrowth = wheels + 1,
                loggingMaximumGrowth = wheels + 4,
                hasDeterministicUnits = true,
                storedFluidUnits = 123456789L,
                trainRailDistanceAlongPathUnits = 234567890L,
                steamTrainStoredBurnEnergyUnits = 345678901L,
                steamTrainBurnEnergyGaugeCapacityUnits = 456789012L,
                splitterState = new Spliterbelt.PersistentState
                    { filterOutput = mode, nextInput = input, nextOutput = output, wheelRotationMask = wheels }
            };
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            write.Invoke(null, new object[] { writer, state });
            writer.Flush(); stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var restored = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { reader, (object)SaveGameData.CurrentVersion, current });
            if (restored.splitterState.filterOutput != mode || restored.splitterState.nextInput != input
                || restored.splitterState.nextOutput != output || restored.itemFilterMaskWords[0] != 1UL << 42
                || restored.splitterState.wheelRotationMask != wheels
                || restored.boxMinimumRetainedItemCount != wheels + 2
                || restored.boxMaximumStoredItemCount != wheels + 5
                || restored.loggingMinimumGrowth != wheels + 1 || restored.loggingMaximumGrowth != wheels + 4
                || !restored.hasDeterministicUnits || restored.storedFluidUnits != 123456789L
                || restored.trainRailDistanceAlongPathUnits != 234567890L
                || restored.steamTrainStoredBurnEnergyUnits != 345678901L
                || restored.steamTrainBurnEnergyGaugeCapacityUnits != 456789012L
                || stream.Position != stream.Length)
                throw new Exception("Splitter save round-trip mismatch");

            // Version 52 lacks the box reserve; version 51 also lacks the wheel field;
            // version 50 lacks the entire splitter tail.
            // The version 57 B filters and version 58 unassigned-color flag follow the v56 tail.
            // Remove the v61 deterministic-unit tail, then the v57-v58 tail.
            byte[] bytes = stream.ToArray()[..^42];
            using var v55Stream = new MemoryStream(bytes, 0, bytes.Length - 8);
            using var v55Reader = new BinaryReader(v55Stream);
            var v55 = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { v55Reader, (object)55, current });
            if (v55.boxMinimumRetainedItemCount != wheels + 2
                || v55.boxMaximumStoredItemCount != BoxObject.DefaultMaximumStoredItemCount
                || v55.loggingMaximumGrowth != LoggingMachine.DefaultMaximumGrowth
                || v55Stream.Position != v55Stream.Length)
                throw new Exception("Version 55 must retain old minimums and default to unrestricted maximums");
            using var v52Stream = new MemoryStream(bytes, 0, bytes.Length - 12);
            using var v52Reader = new BinaryReader(v52Stream);
            var v52 = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { v52Reader, (object)52, current });
            if (v52.splitterState.wheelRotationMask != wheels
                || v52.boxMinimumRetainedItemCount != BoxObject.DefaultMinimumRetainedItemCount
                || v52Stream.Position != v52Stream.Length)
                throw new Exception("Version 52 installation alignment changed");
            using var previousStream = new MemoryStream(bytes, 0, bytes.Length - 16);
            using var previousReader = new BinaryReader(previousStream);
            var previous = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { previousReader, (object)51, current });
            if (previous.splitterState.wheelRotationMask != 0 || previous.splitterState.nextInput != input
                || previous.splitterState.nextOutput != output || previousStream.Position != previousStream.Length)
                throw new Exception("Version 51 splitter alignment changed");
            using var legacyStream = new MemoryStream(bytes, 0, bytes.Length - 29);
            using var legacyReader = new BinaryReader(legacyStream);
            var legacy = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { legacyReader, (object)50, current });
            if (legacy.splitterState != null || legacy.itemFilterMaskWords[0] != 1UL << 42
                || legacyStream.Position != legacyStream.Length)
                throw new Exception("Legacy installation alignment changed");
            cases += 5;
        }
        CheckDeterministicInputOutputState(serializer);
        Console.WriteLine($"PASS: {cases} production serializer installation round-trips, including range bounds and versions 50/51/52/55 compatibility. No engine launched.");
        CheckNativeBelts(serializer, current);
    }

    private static void CheckDeterministicInputOutputState(Type serializer)
    {
        MethodInfo write = serializer.GetMethod("WriteInputOutputState", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo read = serializer.GetMethod("ReadInputOutputState", BindingFlags.Static | BindingFlags.NonPublic);
        var state = new InputOutputModule.PersistentState
        {
            hasDeterministicUnits = true,
            storedEnergyUnits = 567890123L,
            energyGaugeCapacityUnits = 678901234L,
            remainingCraftTicks = 789L,
            activeCraftConsumedEnergyUnits = 789012345L,
            oilDrillingProgressUnits = 890123456L,
            seedPlanterPlantElapsedUnits = 901234567L
        };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        write.Invoke(null, new object[] { writer, state });
        writer.Flush();
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var restored = (InputOutputModule.PersistentState)read.Invoke(
            null,
            new object[] { reader, SaveGameData.CurrentVersion });
        if (restored == null
            || !restored.hasDeterministicUnits
            || restored.storedEnergyUnits != state.storedEnergyUnits
            || restored.energyGaugeCapacityUnits != state.energyGaugeCapacityUnits
            || restored.remainingCraftTicks != state.remainingCraftTicks
            || restored.activeCraftConsumedEnergyUnits != state.activeCraftConsumedEnergyUnits
            || restored.oilDrillingProgressUnits != state.oilDrillingProgressUnits
            || restored.seedPlanterPlantElapsedUnits != state.seedPlanterPlantElapsedUnits
            || stream.Position != stream.Length)
            throw new Exception("Deterministic input/output state round-trip mismatch");
    }

    private static void CheckNativeBelts(Type serializer, object compatibility)
    {
        var save = new SaveGameData
        {
            beltSimulation = new ProjectF.Conveyors.BeltSimulationSnapshot { Tick = 478921 },
            simulationTick = 912345,
            nextInstallationSimulationId = 67890
        };
        var lane = new ProjectF.Conveyors.BeltSavedLane
        {
            X = 4, Y = -5, Lane = 2, OriginX = 3, OriginY = -5, OriginLane = 0,
            CursorX = 3, CursorY = -5, CursorLane = 0,
            State = new ProjectF.Conveyors.BeltLaneState { ItemId = 42, Origin = -1, Remaining = 1001, Duration = 393217, GateBits = 31 }
        };
        save.map.conveyorItems.Add(new ConveyorItemBlockSaveEntry
        {
            coordinate = new UnityEngine.Vector2Int(4, -5),
            lanes = new System.Collections.Generic.List<ConveyorItemLaneSaveState>
            { new ConveyorItemLaneSaveState { laneIndex = 2, itemId = 42, nativeBeltState = lane } }
        });
        save.beltSimulation.Lanes.Add(new ProjectF.Conveyors.BeltSavedLane
        { X = 3, Y = -5, Lane = 0, State = ProjectF.Conveyors.BeltLaneState.Empty, CursorX = 4, CursorLane = 2 });
        save.map.farmlandFertilizer.Add(new FarmlandFertilizerSaveEntry
        {
            coordinate = new UnityEngine.Vector2Int(7, 8),
            fertilizerEnergy = 1.2345679f,
            fertilizerEnergyUnits = 74074073L
        });
        MethodInfo write = serializer.GetMethod("WriteSaveGameData", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo read = serializer.GetMethod("ReadSaveGameData", BindingFlags.Static | BindingFlags.NonPublic);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        write.Invoke(null, new object[] { writer, save }); writer.Flush(); stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var loaded = (SaveGameData)read.Invoke(null, new[] { reader, (object)SaveGameData.CurrentVersion, compatibility });
        var item = loaded.map.conveyorItems[0].lanes[0].nativeBeltState;
        if (loaded.beltSimulation.Tick != 478921 || loaded.simulationTick != 912345
            || loaded.nextInstallationSimulationId != 67890
            || loaded.beltSimulation.Lanes[0].CursorLane != 2
            || loaded.map.farmlandFertilizer[0].fertilizerEnergyUnits != 74074073L
            || item.State.ItemId != 42 || item.State.Remaining != 1001 || item.State.Duration != 393217
            || item.OriginLane != 0 || item.CursorX != 3 || stream.Position != stream.Length)
            throw new Exception("Native belt production save round-trip mismatch");
        using var output = new MemoryStream(); using var secondWriter = new BinaryWriter(output);
        write.Invoke(null, new object[] { secondWriter, loaded }); secondWriter.Flush();
        if (!System.Linq.Enumerable.SequenceEqual(stream.ToArray(), output.ToArray()))
            throw new Exception("Native belt save is not byte identical after round-trip");
        Console.WriteLine("PASS: production v61 save preserves shared and belt clocks, deterministic units, stable installation ID allocation, occupied lanes and empty-lane merge cursors; bytes match after round-trip.");
    }
}
