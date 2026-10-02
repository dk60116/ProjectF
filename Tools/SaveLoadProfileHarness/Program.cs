using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace ProjectF.Tools.SaveLoadProfileHarness;

internal static class Program
{
    private static int Main(string[] args)
    {
        RegisterAssemblyResolver();
        if (args.Length == 1 && args[0] == "--refinery-self-check")
        {
            CheckRefinerySaveRoundTrip();
            Console.WriteLine("PASS refinery input, process, output remainder and clone save round trips");
            return 0;
        }
        if (args.Length == 2 && args[0] == "--pipe-topology" && File.Exists(args[1]))
        {
            FluidOutputSaveReport.WritePipeTopology(SaveGameBinarySerializer.ReadFromFile(Path.GetFullPath(args[1])));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--fluid-output" && File.Exists(args[1]))
        {
            FluidOutputSaveReport.Write(SaveGameBinarySerializer.ReadFromFile(Path.GetFullPath(args[1])));
            return 0;
        }
        if (args.Length == 1 && string.Equals(args[0], "--self-check", StringComparison.Ordinal))
        {
            if (!SaveGameBinarySerializer.RunTerrainCloneRegionRoundTripSelfCheck(
                    out string firstIssue))
            {
                Console.Error.WriteLine($"FAIL {firstIssue}");
                return 2;
            }

            Console.WriteLine("PASS terrain clone region save round trip");
            return 0;
        }

        if ((args.Length != 1 && args.Length != 3) || !File.Exists(args[0]))
        {
            Console.Error.WriteLine(
                "Usage: SaveLoadProfileHarness --self-check | --refinery-self-check | --fluid-output <save-file> | --pipe-topology <save-file> | <save-file> [chunk-size load-radius]");
            return 1;
        }

        string path = Path.GetFullPath(args[0]);
        int chunkSize = args.Length == 3 && int.TryParse(args[1], out int parsedChunkSize)
            ? Math.Max(1, parsedChunkSize)
            : 8;
        int loadRadius = args.Length == 3 && int.TryParse(args[2], out int parsedLoadRadius)
            ? Math.Max(0, parsedLoadRadius)
            : 8;
        Stopwatch timer = Stopwatch.StartNew();
        SaveGameData data = SaveGameBinarySerializer.ReadFromFile(path);
        timer.Stop();
        long readMilliseconds = timer.ElapsedMilliseconds;

        timer.Restart();
        long recompressedBytes = MeasureCompressedSerialization(data);
        timer.Stop();

        MapSaveData map = data.map ?? new MapSaveData();
        bool hasInstallationBounds = TryGetInstallationBounds(
            map.installations,
            out int installationMinimumX,
            out int installationMinimumY,
            out int installationMaximumX,
            out int installationMaximumY);
        int occupiedCoordinates = map.installations.Sum(
            entry => entry?.state?.occupiedCoordinates?.Count ?? 0);
        int installationListItems = map.installations.Sum(
            entry => entry?.state?.storedInstallationItemIds?.Count ?? 0);
        int floorItems = map.floorObjects.Sum(entry => entry?.itemIds?.Count ?? 0);
        int conveyorLanes = map.conveyorItems.Sum(entry => entry?.lanes?.Count ?? 0);
        long conveyorRunItems = map.conveyorItemRuns.Sum(entry => (long)(entry?.itemCount ?? 0));
        var activeChunks = data.terrain?.activeChunkCoordinates;
        int minimumChunkX = activeChunks?.Count > 0 ? activeChunks.Min(coordinate => coordinate.x) : 0;
        int maximumChunkX = activeChunks?.Count > 0 ? activeChunks.Max(coordinate => coordinate.x) : 0;
        int minimumChunkY = activeChunks?.Count > 0 ? activeChunks.Min(coordinate => coordinate.y) : 0;
        int maximumChunkY = activeChunks?.Count > 0 ? activeChunks.Max(coordinate => coordinate.y) : 0;
        int centerChunkX = (int)Math.Floor((data.player?.position.x ?? 0f) / chunkSize);
        int centerChunkY = (int)Math.Floor((data.player?.position.z ?? 0f) / chunkSize);
        (int x, int y) ToChunk(int x, int y) =>
            ((int)Math.Floor(x / (double)chunkSize), (int)Math.Floor(y / (double)chunkSize));
        bool IsNear(int x, int y)
        {
            (int chunkX, int chunkY) = ToChunk(x, y);
            return Math.Abs(chunkX - centerChunkX) <= loadRadius
                   && Math.Abs(chunkY - centerChunkY) <= loadRadius;
        }

        int nearbyChunks = activeChunks?.Count(coordinate =>
            Math.Abs(coordinate.x - centerChunkX) <= loadRadius
            && Math.Abs(coordinate.y - centerChunkY) <= loadRadius) ?? 0;
        int nearbyResources = map.resources.Count(entry =>
            entry != null && IsNear(entry.coordinate.x, entry.coordinate.y));
        int nearbyFloors = map.floorObjects.Count(entry =>
            entry != null && IsNear(entry.coordinate.x, entry.coordinate.y));
        int nearbyInstallations = map.installations.Count(entry =>
            entry?.state != null
            && IsNear(entry.state.anchorCoordinate.x, entry.state.anchorCoordinate.y));
        int nearbyConveyorBlocks = map.conveyorItems.Count(entry =>
            entry != null && IsNear(entry.coordinate.x, entry.coordinate.y));
        HashSet<(int x, int y)> installationChunks = new();
        foreach (InstallationSaveEntry entry in map.installations)
        {
            if (entry?.state == null)
            {
                continue;
            }

            var coordinates = entry.state.occupiedCoordinates;
            if (coordinates == null || coordinates.Count == 0)
            {
                installationChunks.Add(ToChunk(
                    entry.state.anchorCoordinate.x,
                    entry.state.anchorCoordinate.y));
                continue;
            }

            for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
            {
                var coordinate = coordinates[coordinateIndex];
                installationChunks.Add(ToChunk(coordinate.x, coordinate.y));
            }
        }

        HashSet<(int x, int y)> visibleOrInstallationChunks = new(installationChunks);
        if (activeChunks != null)
        {
            foreach (var coordinate in activeChunks)
            {
                if (Math.Abs(coordinate.x - centerChunkX) <= loadRadius
                    && Math.Abs(coordinate.y - centerChunkY) <= loadRadius)
                {
                    visibleOrInstallationChunks.Add((coordinate.x, coordinate.y));
                }
            }
        }

        Console.WriteLine($"File={path}");
        Console.WriteLine(
            $"Version={data.version} CompressedBytes={new FileInfo(path).Length} " +
            $"ReadParseMs={readMilliseconds} RecompressMs={timer.ElapsedMilliseconds} " +
            $"RecompressedBytes={recompressedBytes}");
        Console.WriteLine(
            $"Chunks={data.terrain?.activeChunkCoordinates?.Count ?? 0} " +
            $"MapSize={data.terrain?.mapSize ?? 0} " +
            $"ChunkBounds=({minimumChunkX},{minimumChunkY})..({maximumChunkX},{maximumChunkY}) " +
            $"Resources={map.resources?.Count ?? 0} Floors={map.floorObjects?.Count ?? 0} " +
            $"FloorItems={floorItems} Installations={map.installations?.Count ?? 0} " +
            $"OccupiedCoordinates={occupiedCoordinates} InstallationListItems={installationListItems}");
        Console.WriteLine(
            $"ConveyorBlocks={map.conveyorItems?.Count ?? 0} ConveyorLanes={conveyorLanes} " +
            $"ConveyorRuns={map.conveyorItemRuns?.Count ?? 0} ConveyorRunItems={conveyorRunItems} " +
            $"BeltSnapshotLanes={data.beltSimulation?.Lanes?.Count ?? 0}");
        Console.WriteLine(
            $"Animals={map.animals?.Count ?? 0} Farmland={map.farmlandCoordinates?.Count ?? 0} " +
            $"Fertilizer={map.farmlandFertilizer?.Count ?? 0} " +
            $"PlantedResources={map.plantedResources?.Count ?? 0} " +
            $"TerrainCloneRegions={map.terrainCloneRegions?.Count ?? 0}");
        Console.WriteLine(hasInstallationBounds
            ? $"InstallationBounds=({installationMinimumX},{installationMinimumY})..({installationMaximumX},{installationMaximumY}) "
              + $"Size={installationMaximumX - installationMinimumX + 1}x{installationMaximumY - installationMinimumY + 1}"
            : "InstallationBounds=none");
        for (int regionIndex = 0;
             map.terrainCloneRegions != null && regionIndex < map.terrainCloneRegions.Count;
             regionIndex++)
        {
            TerrainCloneRegionSaveEntry region = map.terrainCloneRegions[regionIndex];
            if (region == null)
            {
                continue;
            }

            Console.WriteLine(
                $"TerrainCloneRegion[{regionIndex}] Source=({region.sourceMinimum.x},{region.sourceMinimum.y})"
                + $"..({region.sourceMaximum.x},{region.sourceMaximum.y}) "
                + $"Offset=({region.offset.x},{region.offset.y})");
        }
        Console.WriteLine(
            $"Player=({data.player?.position.x ?? 0:F2},{data.player?.position.y ?? 0:F2}," +
            $"{data.player?.position.z ?? 0:F2}) HasPlayer={data.player?.hasPlayer ?? false}");
        Console.WriteLine(
            $"Nearby chunkSize={chunkSize} loadRadius={loadRadius} center=({centerChunkX},{centerChunkY}) " +
            $"Chunks={nearbyChunks} Resources={nearbyResources} Floors={nearbyFloors} " +
            $"Installations={nearbyInstallations} ConveyorBlocks={nearbyConveyorBlocks}");
        Console.WriteLine(
            $"ResidencyCandidates InstallationChunks={installationChunks.Count} " +
            $"VisibleOrInstallationChunks={visibleOrInstallationChunks.Count} " +
            $"ExcludedExploredChunks={Math.Max(0, (activeChunks?.Count ?? 0) - visibleOrInstallationChunks.Count)}");

        foreach (var group in map.installations
                     .Where(entry => entry?.state != null)
                     .GroupBy(entry => (entry.state.itemId, entry.state.itemName ?? string.Empty))
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key.itemId)
                     .Take(20))
        {
            Console.WriteLine(
                $"Installation itemId={group.Key.itemId} count={group.Count()} name={group.Key.Item2}");
        }

        return 0;
    }

    private static bool TryGetInstallationBounds(
        IReadOnlyList<InstallationSaveEntry> installations,
        out int minimumX,
        out int minimumY,
        out int maximumX,
        out int maximumY)
    {
        minimumX = minimumY = int.MaxValue;
        maximumX = maximumY = int.MinValue;
        bool found = false;

        for (int i = 0; installations != null && i < installations.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = installations[i]?.state;
            if (state == null)
            {
                continue;
            }

            IncludeBoundsCoordinate(
                state.anchorCoordinate.x,
                state.anchorCoordinate.y,
                ref minimumX,
                ref minimumY,
                ref maximumX,
                ref maximumY,
                ref found);
            for (int coordinateIndex = 0;
                 state.occupiedCoordinates != null && coordinateIndex < state.occupiedCoordinates.Count;
                 coordinateIndex++)
            {
                var coordinate = state.occupiedCoordinates[coordinateIndex];
                IncludeBoundsCoordinate(
                    coordinate.x,
                    coordinate.y,
                    ref minimumX,
                    ref minimumY,
                    ref maximumX,
                    ref maximumY,
                    ref found);
            }
        }

        return found;
    }

    private static void IncludeBoundsCoordinate(
        int x,
        int y,
        ref int minimumX,
        ref int minimumY,
        ref int maximumX,
        ref int maximumY,
        ref bool found)
    {
        minimumX = Math.Min(minimumX, x);
        minimumY = Math.Min(minimumY, y);
        maximumX = Math.Max(maximumX, x);
        maximumY = Math.Max(maximumY, y);
        found = true;
    }

    private static long MeasureCompressedSerialization(SaveGameData data)
    {
        MethodInfo writeSaveGameData = typeof(SaveGameBinarySerializer).GetMethod(
            "WriteSaveGameData",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(SaveGameBinarySerializer), "WriteSaveGameData");

        using MemoryStream output = new MemoryStream();
        using (GZipStream gzip = new GZipStream(output, CompressionLevel.Fastest, true))
        using (BinaryWriter writer = new BinaryWriter(gzip, Encoding.UTF8, true))
        {
            writer.Write("PF_SAVE");
            writer.Write(SaveGameData.CurrentVersion);
            writeSaveGameData.Invoke(null, new object[] { writer, data });
        }

        return output.Length;
    }

    private static void CheckRefinerySaveRoundTrip()
    {
        const long fluidUnitsPerLiter = 60_000_000L;
        var write = typeof(SaveGameBinarySerializer).GetMethod("WriteInputOutputState", BindingFlags.Static | BindingFlags.NonPublic);
        var read = typeof(SaveGameBinarySerializer).GetMethod("ReadInputOutputState", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (bool outputting in new[] { false, true })
        {
            var state = new InputOutputModule.PersistentState
            {
                hasDeterministicUnits = true, hasActiveCraft = true, waitingForOutput = outputting,
                activeRecipeIndex = 0, activeOutputItemId = 1, activeOutputCount = 1,
                activeCraftConsumedEnergyUnits = 1375L * fluidUnitsPerLiter,
                refineryBatchDuration = 5f, refineryBatchTemperature = 70f
            };
            state.refineryInputFluidItemIds.Add(10);
            state.refineryInputFluidUnits.Add(25L * fluidUnitsPerLiter / 2L);
            state.refineryInputFluidTemperatures.Add(80f);
            for (int i = 0; i < 3; i++)
                state.refineryOutputs.Add(new InputOutputModule.RefineryOutputState
                {
                    itemId = i + 1, litersPerSecond = i == 2 ? 2f : .5f,
                    totalUnits = (i == 2 ? 10L * fluidUnitsPerLiter : 5L * fluidUnitsPerLiter / 2L),
                    remainingUnits = outputting ? (i == 0 ? 0L : 5L * fluidUnitsPerLiter / 4L) : -1L
                });
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true)) write.Invoke(null, new object[] { writer, state });
            bytes.Position = 0;
            using var reader = new BinaryReader(bytes, Encoding.UTF8, true);
            var restored = (InputOutputModule.PersistentState)read.Invoke(null, new object[] { reader, SaveGameData.CurrentVersion });
            if (bytes.Position != bytes.Length || !restored.hasActiveCraft || restored.waitingForOutput != outputting
                || restored.activeCraftConsumedEnergyUnits != state.activeCraftConsumedEnergyUnits
                || restored.refineryBatchDuration != 5f || restored.refineryBatchTemperature != 70f
                || restored.refineryInputFluidUnits[0] != state.refineryInputFluidUnits[0]
                || restored.refineryInputFluidTemperatures[0] != 80f || restored.refineryInputFluidItemIds[0] != 10
                || restored.refineryOutputs.Count != 3)
                throw new InvalidOperationException("Refinery state did not survive binary save round trip");
            for (int i = 0; i < 3; i++)
                if (restored.refineryOutputs[i].itemId != state.refineryOutputs[i].itemId
                    || restored.refineryOutputs[i].litersPerSecond != state.refineryOutputs[i].litersPerSecond
                    || restored.refineryOutputs[i].totalUnits != state.refineryOutputs[i].totalUnits
                    || restored.refineryOutputs[i].remainingUnits != state.refineryOutputs[i].remainingUnits)
                    throw new InvalidOperationException("Refinery output lost its rate or remainder");
            var clone = restored.Clone();
            clone.refineryOutputs[0].remainingUnits = 123L;
            if (restored.refineryOutputs[0].remainingUnits == 123L)
                throw new InvalidOperationException("Refinery output snapshot clone shares mutable state");
            clone.ClearStoredEnergyAndProduction();
            if (clone.refineryOutputs.Count != 0 || clone.refineryInputFluidUnits.Count != 0 || clone.refineryBatchDuration != 0f)
                throw new InvalidOperationException("Refinery batch was not cleared with production");
            // The older reader consumes only the version 68 prefix; its new fields default empty.
            bytes.Position = 0;
            var legacy = (InputOutputModule.PersistentState)read.Invoke(null, new object[] { reader, 68 });
            if (legacy.refineryOutputs.Count != 0 || legacy.refineryBatchDuration != 0f || bytes.Position >= bytes.Length)
                throw new InvalidOperationException("Legacy refinery read did not respect the version boundary");
        }
    }

    private static void RegisterAssemblyResolver()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[]
                     {
                         Path.GetDirectoryName(typeof(SaveGameBinarySerializer).Assembly.Location),
                         Path.GetFullPath("FactorioProject/Library/ScriptAssemblies"),
                         "C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine"
                     })
            {
                string candidate = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(candidate))
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
            }

            return null;
        };
    }
}
