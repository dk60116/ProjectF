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
                "Usage: SaveLoadProfileHarness --self-check | <save-file> [chunk-size load-radius]");
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
