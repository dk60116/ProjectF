using System;
using System.Collections;
using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;

public readonly struct ProfilingAreaCloneReport
{
    public ProfilingAreaCloneReport(
        Vector2Int sourceMinimum,
        Vector2Int sourceMaximum,
        Vector2Int destinationMinimum,
        Vector2Int offset,
        int installationCount,
        int resourceCount,
        int floorItemStackCount,
        int conveyorItemCount,
        int terrainTileCount)
    {
        SourceMinimum = sourceMinimum;
        SourceMaximum = sourceMaximum;
        DestinationMinimum = destinationMinimum;
        Offset = offset;
        InstallationCount = installationCount;
        ResourceCount = resourceCount;
        FloorItemStackCount = floorItemStackCount;
        ConveyorItemCount = conveyorItemCount;
        TerrainTileCount = terrainTileCount;
    }

    public Vector2Int SourceMinimum { get; }
    public Vector2Int SourceMaximum { get; }
    public Vector2Int DestinationMinimum { get; }
    public Vector2Int Offset { get; }
    public int InstallationCount { get; }
    public int ResourceCount { get; }
    public int FloorItemStackCount { get; }
    public int ConveyorItemCount { get; }
    public int TerrainTileCount { get; }
}

public partial class TerrainGenerator : MonoBehaviour
{
    private const int ProfilingCloneTerrainPadding = 2;
    private const int ProfilingCloneAreaGap = 2;

    // Tile lookup remains direct at runtime. The compact region list is the
    // persistent source of truth used to rebuild this cache after loading.
    private readonly Dictionary<Vector2Int, Vector2Int> profilingCloneTerrainSources =
        new Dictionary<Vector2Int, Vector2Int>();
    private readonly List<TerrainCloneRegionSaveEntry> profilingCloneTerrainRegions =
        new List<TerrainCloneRegionSaveEntry>();

    public bool TryCloneInstalledAreaForProfiling(
        out ProfilingAreaCloneReport report,
        out string error)
    {
        report = default;
        error = string.Empty;
        if (IsChunkStreamingBusy || !worldReadyForPresentation)
        {
            error = "world is still loading";
            return false;
        }

        TerrainSaveData terrainState = CaptureTerrainSaveState();
        MapSaveData mapState = CaptureMapSaveState();
        List<InstallationSaveEntry> installations = mapState?.installations;
        if (installations == null || installations.Count <= 0)
        {
            error = "no installations to clone";
            return false;
        }

        if (!TryGetProfilingCloneSourceBounds(
                installations,
                out Vector2Int objectMinimum,
                out Vector2Int objectMaximum))
        {
            error = "installation bounds are invalid";
            return false;
        }

        int mapMinimum = GetMapMinCoordinate();
        int mapMaximum = GetMapMaxExclusiveCoordinate() - 1;
        Vector2Int sourceMinimum = new Vector2Int(
            Mathf.Max(mapMinimum, objectMinimum.x - ProfilingCloneTerrainPadding),
            Mathf.Max(mapMinimum, objectMinimum.y - ProfilingCloneTerrainPadding));
        Vector2Int sourceMaximum = new Vector2Int(
            Mathf.Min(mapMaximum, objectMaximum.x + ProfilingCloneTerrainPadding),
            Mathf.Min(mapMaximum, objectMaximum.y + ProfilingCloneTerrainPadding));

        if (!TrySelectProfilingCloneOffset(
                sourceMinimum,
                sourceMaximum,
                mapMinimum,
                mapMaximum,
                out Vector2Int offset))
        {
            error = "map has no non-overlapping nearby area large enough for the clone";
            return false;
        }

        Vector2Int destinationMinimum = sourceMinimum + offset;
        Vector2Int destinationMaximum = sourceMaximum + offset;
        List<ResourceSaveEntry> sourceResources = CollectSourceResources(mapState.resources, sourceMinimum, sourceMaximum);
        List<FloorObjectSaveEntry> sourceFloorObjects = CollectSourceFloorObjects(mapState.floorObjects, sourceMinimum, sourceMaximum);
        List<InstallationSaveEntry> sourceInstallations = CollectSourceInstallations(installations);
        List<ConveyorItemBlockSaveEntry> sourceConveyorItems = CollectSourceConveyorItems(
            mapState.conveyorItems,
            sourceMinimum,
            sourceMaximum);
        List<ConveyorItemRunSaveEntry> sourceConveyorRuns = CollectSourceConveyorRuns(
            mapState.conveyorItemRuns,
            sourceMinimum,
            sourceMaximum);
        List<AnimalSaveEntry> sourceAnimals = CollectSourceAnimals(
            mapState.animals,
            sourceMinimum,
            sourceMaximum);
        List<Vector2Int> sourceFarmland = CollectSourceCoordinates(
            mapState.farmlandCoordinates,
            sourceMinimum,
            sourceMaximum);
        List<FarmlandFertilizerSaveEntry> sourceFertilizer = CollectSourceFertilizer(
            mapState.farmlandFertilizer,
            sourceMinimum,
            sourceMaximum);
        List<PlantedResourceSaveEntry> sourcePlants = CollectSourcePlants(
            mapState.plantedResources,
            sourceMinimum,
            sourceMaximum);

        RemoveProfilingCloneDestinationState(mapState, destinationMinimum, destinationMaximum);
        Dictionary<long, long> placementSequenceMap = BuildProfilingClonePlacementSequenceMap(
            sourceInstallations,
            installations);
        Dictionary<string, string> stationNameMap = BuildProfilingCloneStationNameMap(
            sourceInstallations,
            installations,
            offset);

        for (int i = 0; i < sourceResources.Count; i++)
        {
            ResourceSaveEntry source = sourceResources[i];
            mapState.resources.Add(new ResourceSaveEntry
            {
                coordinate = source.coordinate + offset,
                itemId = source.itemId,
                state = source.state
            });
        }

        for (int i = 0; i < sourceFloorObjects.Count; i++)
        {
            FloorObjectSaveEntry source = sourceFloorObjects[i];
            mapState.floorObjects.Add(new FloorObjectSaveEntry
            {
                coordinate = source.coordinate + offset,
                itemIds = source.itemIds != null
                    ? new List<int>(source.itemIds)
                    : new List<int>()
            });
        }

        for (int i = 0; i < sourceInstallations.Count; i++)
        {
            BlockStateStore.InstallationSaveState clonedState =
                CloneProfilingInstallationState(
                    sourceInstallations[i].state,
                    offset,
                    placementSequenceMap,
                    stationNameMap);
            mapState.installations.Add(new InstallationSaveEntry { state = clonedState });
        }

        int conveyorItemCount = 0;
        for (int i = 0; i < sourceConveyorItems.Count; i++)
        {
            ConveyorItemBlockSaveEntry clonedEntry = CloneProfilingConveyorItemEntry(
                sourceConveyorItems[i],
                offset);
            mapState.conveyorItems.Add(clonedEntry);
            conveyorItemCount += clonedEntry.lanes?.Count ?? 0;
        }

        for (int i = 0; i < sourceConveyorRuns.Count; i++)
        {
            ConveyorItemRunSaveEntry clonedRun = CloneProfilingConveyorRun(
                sourceConveyorRuns[i],
                offset);
            mapState.conveyorItemRuns.Add(clonedRun);
            conveyorItemCount += Mathf.Max(0, clonedRun.itemCount);
        }

        CloneProfilingAnimals(mapState.animals, sourceAnimals, offset, placementSequenceMap);
        for (int i = 0; i < sourceFarmland.Count; i++)
        {
            mapState.farmlandCoordinates.Add(sourceFarmland[i] + offset);
        }

        for (int i = 0; i < sourceFertilizer.Count; i++)
        {
            FarmlandFertilizerSaveEntry source = sourceFertilizer[i];
            mapState.farmlandFertilizer.Add(new FarmlandFertilizerSaveEntry
            {
                coordinate = source.coordinate + offset,
                fertilizerEnergy = source.fertilizerEnergy,
                fertilizerEnergyUnits = source.fertilizerEnergyUnits
            });
        }

        for (int i = 0; i < sourcePlants.Count; i++)
        {
            PlantedResourceSaveEntry source = sourcePlants[i];
            mapState.plantedResources.Add(new PlantedResourceSaveEntry
            {
                coordinate = source.coordinate + offset,
                seedItemId = source.seedItemId
            });
        }

        int terrainTileCount = ApplyProfilingCloneTerrainMapping(
            mapState,
            sourceMinimum,
            sourceMaximum,
            offset);
        AddProfilingCloneActiveChunks(terrainState, destinationMinimum, destinationMaximum);
        LoadFromSaveState(terrainState, mapState);

        report = new ProfilingAreaCloneReport(
            sourceMinimum,
            sourceMaximum,
            destinationMinimum,
            offset,
            sourceInstallations.Count,
            sourceResources.Count,
            sourceFloorObjects.Count,
            conveyorItemCount,
            terrainTileCount);
        return true;
    }

    private Vector2Int ResolveProfilingCloneTerrainSource(Vector2Int coordinate)
    {
        Vector2Int resolved = coordinate;
        for (int i = 0; i < 32; i++)
        {
            if (!profilingCloneTerrainSources.TryGetValue(resolved, out Vector2Int source)
                || source == resolved)
            {
                return resolved;
            }

            resolved = source;
        }

        return resolved;
    }

    private void ClearProfilingCloneTerrainSources()
    {
        profilingCloneTerrainSources.Clear();
        profilingCloneTerrainRegions.Clear();
    }

    private void CaptureProfilingCloneTerrainRegions(MapSaveData mapSaveData)
    {
        IEnumerator capture = CaptureProfilingCloneTerrainRegionsIncremental(
            mapSaveData,
            int.MaxValue);
        while (capture.MoveNext()) { }
    }

    private IEnumerator CaptureProfilingCloneTerrainRegionsIncremental(
        MapSaveData mapSaveData,
        int entriesPerFrame)
    {
        if (mapSaveData == null)
        {
            yield break;
        }

        entriesPerFrame = Mathf.Max(1, entriesPerFrame);
        mapSaveData.terrainCloneRegions ??= new List<TerrainCloneRegionSaveEntry>();
        mapSaveData.terrainCloneRegions.Clear();
        if (mapSaveData.terrainCloneRegions.Capacity < profilingCloneTerrainRegions.Count)
        {
            mapSaveData.terrainCloneRegions.Capacity = profilingCloneTerrainRegions.Count;
        }

        for (int i = 0; i < profilingCloneTerrainRegions.Count; i++)
        {
            TerrainCloneRegionSaveEntry source = profilingCloneTerrainRegions[i];
            mapSaveData.terrainCloneRegions.Add(CloneProfilingTerrainRegion(source));
            if ((i + 1) % entriesPerFrame == 0)
            {
                yield return null;
            }
        }
    }

    private void RestoreProfilingCloneTerrainRegions(MapSaveData mapSaveData)
    {
        profilingCloneTerrainSources.Clear();
        profilingCloneTerrainRegions.Clear();
        IReadOnlyList<TerrainCloneRegionSaveEntry> savedRegions =
            mapSaveData?.terrainCloneRegions;
        for (int i = 0; savedRegions != null && i < savedRegions.Count; i++)
        {
            TerrainCloneRegionSaveEntry savedRegion = savedRegions[i];
            if (!IsValidProfilingTerrainRegion(savedRegion))
            {
                continue;
            }

            TerrainCloneRegionSaveEntry region = CloneProfilingTerrainRegion(savedRegion);
            profilingCloneTerrainRegions.Add(region);
            ApplyProfilingCloneTerrainMapping(region);
        }
    }

    private static TerrainCloneRegionSaveEntry CloneProfilingTerrainRegion(
        TerrainCloneRegionSaveEntry source)
    {
        return source == null
            ? null
            : new TerrainCloneRegionSaveEntry
            {
                sourceMinimum = source.sourceMinimum,
                sourceMaximum = source.sourceMaximum,
                offset = source.offset
            };
    }

    private static bool IsValidProfilingTerrainRegion(TerrainCloneRegionSaveEntry region)
    {
        return region != null
               && region.sourceMinimum.x <= region.sourceMaximum.x
               && region.sourceMinimum.y <= region.sourceMaximum.y
               && region.offset != Vector2Int.zero;
    }

    private static bool TryGetProfilingCloneSourceBounds(
        IReadOnlyList<InstallationSaveEntry> installations,
        out Vector2Int minimum,
        out Vector2Int maximum)
    {
        minimum = new Vector2Int(int.MaxValue, int.MaxValue);
        maximum = new Vector2Int(int.MinValue, int.MinValue);
        bool found = false;
        for (int i = 0; i < installations.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = installations[i]?.state;
            if (state == null)
            {
                continue;
            }

            IncludeProfilingCloneCoordinate(state.anchorCoordinate, ref minimum, ref maximum, ref found);
            IncludeProfilingCloneCoordinates(state.occupiedCoordinates, ref minimum, ref maximum, ref found);
            IncludeProfilingCloneCoordinates(state.utilityPoleConnectedAnchors, ref minimum, ref maximum, ref found);
            if (state.hasStorageKey)
            {
                IncludeProfilingCloneCoordinate(state.storageKey, ref minimum, ref maximum, ref found);
            }

            if (state.hasTrainRailSample)
            {
                IncludeProfilingCloneCoordinate(state.trainRailAnchorCoordinate, ref minimum, ref maximum, ref found);
                IncludeProfilingCloneCoordinate(
                    Vector2Int.RoundToInt(state.trainRailPathPoint),
                    ref minimum,
                    ref maximum,
                    ref found);
            }

            if (state.hasWorldPose)
            {
                IncludeProfilingCloneCoordinate(
                    new Vector2Int(
                        Mathf.RoundToInt(state.worldPosition.x),
                        Mathf.RoundToInt(state.worldPosition.z)),
                    ref minimum,
                    ref maximum,
                    ref found);
            }

            for (int pointIndex = 0;
                 state.railVisualPathPoints != null && pointIndex < state.railVisualPathPoints.Count;
                 pointIndex++)
            {
                IncludeProfilingCloneCoordinate(
                    Vector2Int.RoundToInt(state.railVisualPathPoints[pointIndex]),
                    ref minimum,
                    ref maximum,
                    ref found);
            }

            IncludeProfilingCloneInputOutputCoordinates(
                state.inputOutputState,
                ref minimum,
                ref maximum,
                ref found);
        }

        return found;
    }

    private static void IncludeProfilingCloneInputOutputCoordinates(
        InputOutputModule.PersistentState state,
        ref Vector2Int minimum,
        ref Vector2Int maximum,
        ref bool found)
    {
        if (state == null)
        {
            return;
        }

        IncludeProfilingCloneCoordinates(state.inputEnergyCoordinates, ref minimum, ref maximum, ref found);
        IncludeProfilingCloneCoordinates(state.outputCoordinates, ref minimum, ref maximum, ref found);
        IncludeProfilingCloneCoordinates(state.pipeInputCoordinates, ref minimum, ref maximum, ref found);
        IncludeProfilingCloneCoordinates(state.gridCoordinates, ref minimum, ref maximum, ref found);
        IncludeProfilingCloneCoordinates(state.focusCoordinates, ref minimum, ref maximum, ref found);
        for (int i = 0; state.inputItemAreas != null && i < state.inputItemAreas.Count; i++)
        {
            IncludeProfilingCloneCoordinate(
                state.inputItemAreas[i].coordinate,
                ref minimum,
                ref maximum,
                ref found);
        }

        if (state.seedPlanterHasLoadedSeed)
        {
            IncludeProfilingCloneCoordinate(
                state.seedPlanterLoadedSeedInputCoordinate,
                ref minimum,
                ref maximum,
                ref found);
        }
    }

    private static void IncludeProfilingCloneCoordinates(
        IReadOnlyList<Vector2Int> coordinates,
        ref Vector2Int minimum,
        ref Vector2Int maximum,
        ref bool found)
    {
        for (int i = 0; coordinates != null && i < coordinates.Count; i++)
        {
            IncludeProfilingCloneCoordinate(coordinates[i], ref minimum, ref maximum, ref found);
        }
    }

    private static void IncludeProfilingCloneCoordinate(
        Vector2Int coordinate,
        ref Vector2Int minimum,
        ref Vector2Int maximum,
        ref bool found)
    {
        minimum = Vector2Int.Min(minimum, coordinate);
        maximum = Vector2Int.Max(maximum, coordinate);
        found = true;
    }

    private static bool TrySelectProfilingCloneOffset(
        Vector2Int sourceMinimum,
        Vector2Int sourceMaximum,
        int mapMinimum,
        int mapMaximum,
        out Vector2Int selectedOffset)
    {
        int width = sourceMaximum.x - sourceMinimum.x + 1;
        int height = sourceMaximum.y - sourceMinimum.y + 1;
        Vector2Int[] candidates =
        {
            new Vector2Int(width + ProfilingCloneAreaGap, 0),
            new Vector2Int(-(width + ProfilingCloneAreaGap), 0),
            new Vector2Int(0, height + ProfilingCloneAreaGap),
            new Vector2Int(0, -(height + ProfilingCloneAreaGap))
        };

        selectedOffset = default;
        int bestDistance = int.MaxValue;
        bool found = false;
        for (int i = 0; i < candidates.Length; i++)
        {
            Vector2Int offset = candidates[i];
            Vector2Int destinationMinimum = sourceMinimum + offset;
            Vector2Int destinationMaximum = sourceMaximum + offset;
            if (destinationMinimum.x < mapMinimum
                || destinationMinimum.y < mapMinimum
                || destinationMaximum.x > mapMaximum
                || destinationMaximum.y > mapMaximum)
            {
                continue;
            }

            int distance = (offset.x * offset.x) + (offset.y * offset.y);
            if (!found || distance < bestDistance)
            {
                found = true;
                bestDistance = distance;
                selectedOffset = offset;
            }
        }

        return found;
    }

    private int ApplyProfilingCloneTerrainMapping(
        MapSaveData mapState,
        Vector2Int sourceMinimum,
        Vector2Int sourceMaximum,
        Vector2Int offset)
    {
        TerrainCloneRegionSaveEntry region = new TerrainCloneRegionSaveEntry
        {
            sourceMinimum = sourceMinimum,
            sourceMaximum = sourceMaximum,
            offset = offset
        };
        profilingCloneTerrainRegions.Add(region);
        mapState.terrainCloneRegions ??= new List<TerrainCloneRegionSaveEntry>();
        mapState.terrainCloneRegions.Add(CloneProfilingTerrainRegion(region));
        return ApplyProfilingCloneTerrainMapping(region);
    }

    private int ApplyProfilingCloneTerrainMapping(TerrainCloneRegionSaveEntry region)
    {
        Vector2Int sourceMinimum = region.sourceMinimum;
        Vector2Int sourceMaximum = region.sourceMaximum;
        Vector2Int offset = region.offset;
        int width = sourceMaximum.x - sourceMinimum.x + 1;
        int height = sourceMaximum.y - sourceMinimum.y + 1;
        profilingCloneTerrainSources.EnsureCapacity(
            profilingCloneTerrainSources.Count + (width * height));
        for (int y = sourceMinimum.y; y <= sourceMaximum.y; y++)
        {
            for (int x = sourceMinimum.x; x <= sourceMaximum.x; x++)
            {
                Vector2Int source = ResolveProfilingCloneTerrainSource(new Vector2Int(x, y));
                profilingCloneTerrainSources[new Vector2Int(x + offset.x, y + offset.y)] = source;
            }
        }

        return width * height;
    }

    private void AddProfilingCloneActiveChunks(
        TerrainSaveData terrainState,
        Vector2Int destinationMinimum,
        Vector2Int destinationMaximum)
    {
        terrainState.activeChunkCoordinates ??= new List<Vector2Int>();
        HashSet<Vector2Int> activeChunks = new HashSet<Vector2Int>(terrainState.activeChunkCoordinates);
        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        int minimumChunkX = Mathf.FloorToInt(destinationMinimum.x / (float)normalizedChunkSize);
        int minimumChunkY = Mathf.FloorToInt(destinationMinimum.y / (float)normalizedChunkSize);
        int maximumChunkX = Mathf.FloorToInt(destinationMaximum.x / (float)normalizedChunkSize);
        int maximumChunkY = Mathf.FloorToInt(destinationMaximum.y / (float)normalizedChunkSize);
        for (int chunkY = minimumChunkY; chunkY <= maximumChunkY; chunkY++)
        {
            for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
            {
                Vector2Int coordinate = new Vector2Int(chunkX, chunkY);
                if (DoesChunkIntersectMapBounds(coordinate, normalizedChunkSize))
                {
                    activeChunks.Add(coordinate);
                }
            }
        }

        terrainState.activeChunkCoordinates.Clear();
        terrainState.activeChunkCoordinates.AddRange(activeChunks);
        terrainState.activeChunkCoordinates.Sort(CompareChunkCoordinates);
    }

    private static Dictionary<long, long> BuildProfilingClonePlacementSequenceMap(
        List<InstallationSaveEntry> sourceInstallations,
        IReadOnlyList<InstallationSaveEntry> allInstallations)
    {
        sourceInstallations.Sort(CompareProfilingInstallations);
        long nextSequence = 1L;
        for (int i = 0; i < allInstallations.Count; i++)
        {
            long sequence = allInstallations[i]?.state?.placementSequence ?? 0L;
            if (sequence >= nextSequence && sequence < long.MaxValue)
            {
                nextSequence = sequence + 1L;
            }
        }

        Dictionary<long, long> result = new Dictionary<long, long>(sourceInstallations.Count);
        for (int i = 0; i < sourceInstallations.Count; i++)
        {
            long sourceSequence = sourceInstallations[i].state.placementSequence;
            long clonedSequence = nextSequence++;
            InstallationObject.ClaimNextPlacementSequence(clonedSequence);
            if (sourceSequence > 0L)
            {
                result[sourceSequence] = clonedSequence;
            }
        }

        return result;
    }

    private static int CompareProfilingInstallations(
        InstallationSaveEntry left,
        InstallationSaveEntry right)
    {
        BlockStateStore.InstallationSaveState leftState = left?.state;
        BlockStateStore.InstallationSaveState rightState = right?.state;
        if (ReferenceEquals(leftState, rightState))
        {
            return 0;
        }

        if (leftState == null)
        {
            return -1;
        }

        if (rightState == null)
        {
            return 1;
        }

        int sequenceComparison = leftState.placementSequence.CompareTo(rightState.placementSequence);
        if (sequenceComparison != 0)
        {
            return sequenceComparison;
        }

        int yComparison = leftState.anchorCoordinate.y.CompareTo(rightState.anchorCoordinate.y);
        return yComparison != 0
            ? yComparison
            : leftState.anchorCoordinate.x.CompareTo(rightState.anchorCoordinate.x);
    }

    private static BlockStateStore.InstallationSaveState CloneProfilingInstallationState(
        BlockStateStore.InstallationSaveState source,
        Vector2Int offset,
        IReadOnlyDictionary<long, long> placementSequenceMap,
        IReadOnlyDictionary<string, string> stationNameMap)
    {
        BlockStateStore.InstallationSaveState clone = source.Clone();
        clone.anchorCoordinate += offset;
        clone.placementSequence = placementSequenceMap.TryGetValue(source.placementSequence, out long sequence)
            ? sequence
            : InstallationObject.ClaimNextPlacementSequence();
        if (clone.hasStorageKey)
        {
            clone.storageKey += offset;
        }

        ShiftProfilingCoordinates(clone.occupiedCoordinates, offset);
        ShiftProfilingCoordinates(clone.utilityPoleConnectedAnchors, offset);
        ShiftProfilingRailPoints(clone.railVisualPathPoints, offset);
        ShiftProfilingInputOutputState(clone.inputOutputState, offset);
        if (clone.hasWorldPose)
        {
            clone.worldPosition += new Vector3(offset.x, 0f, offset.y);
        }

        if (clone.hasTrainRailSample)
        {
            clone.trainRailAnchorCoordinate += offset;
            clone.trainRailPathPoint += new Vector2(offset.x, offset.y);
            clone.trainRailPlacementSequence = RemapProfilingPlacementSequence(
                clone.trainRailPlacementSequence,
                placementSequenceMap);
        }

        clone.stationName = RemapProfilingStationName(clone.stationName, stationNameMap);
        clone.steamTrainAutoDriveTargetAStationName = RemapProfilingStationName(
            clone.steamTrainAutoDriveTargetAStationName,
            stationNameMap);
        clone.steamTrainAutoDriveTargetBStationName = RemapProfilingStationName(
            clone.steamTrainAutoDriveTargetBStationName,
            stationNameMap);
        clone.steamTrainAutoDriveRouteTargetStationName = RemapProfilingStationName(
            clone.steamTrainAutoDriveRouteTargetStationName,
            stationNameMap);
        clone.steamTrainAutoDriveLastArrivedStationName = RemapProfilingStationName(
            clone.steamTrainAutoDriveLastArrivedStationName,
            stationNameMap);

        return clone;
    }

    private static Dictionary<string, string> BuildProfilingCloneStationNameMap(
        IReadOnlyList<InstallationSaveEntry> sourceInstallations,
        IReadOnlyList<InstallationSaveEntry> allInstallations,
        Vector2Int offset)
    {
        HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allInstallations.Count; i++)
        {
            string stationName = allInstallations[i]?.state?.stationName;
            if (!string.IsNullOrWhiteSpace(stationName))
            {
                usedNames.Add(stationName.Trim());
            }
        }

        Dictionary<string, string> result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < sourceInstallations.Count; i++)
        {
            string sourceName = sourceInstallations[i]?.state?.stationName;
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                continue;
            }

            sourceName = sourceName.Trim();
            if (result.ContainsKey(sourceName))
            {
                continue;
            }

            string suffix = $" Clone {offset.x},{offset.y}";
            string candidate = sourceName + suffix;
            int duplicateIndex = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = sourceName + suffix + $" #{duplicateIndex++}";
            }

            result.Add(sourceName, candidate);
        }

        return result;
    }

    private static string RemapProfilingStationName(
        string stationName,
        IReadOnlyDictionary<string, string> stationNameMap)
    {
        if (string.IsNullOrWhiteSpace(stationName))
        {
            return string.Empty;
        }

        string normalized = stationName.Trim();
        return stationNameMap.TryGetValue(normalized, out string mapped)
            ? mapped
            : normalized;
    }

    private static long RemapProfilingPlacementSequence(
        long sourceSequence,
        IReadOnlyDictionary<long, long> placementSequenceMap)
    {
        return sourceSequence > 0L && placementSequenceMap.TryGetValue(sourceSequence, out long mapped)
            ? mapped
            : 0L;
    }

    private static void ShiftProfilingInputOutputState(
        InputOutputModule.PersistentState state,
        Vector2Int offset)
    {
        if (state == null)
        {
            return;
        }

        ShiftProfilingCoordinates(state.inputEnergyCoordinates, offset);
        ShiftProfilingCoordinates(state.outputCoordinates, offset);
        ShiftProfilingCoordinates(state.pipeInputCoordinates, offset);
        ShiftProfilingCoordinates(state.gridCoordinates, offset);
        ShiftProfilingCoordinates(state.focusCoordinates, offset);
        for (int i = 0; state.inputItemAreas != null && i < state.inputItemAreas.Count; i++)
        {
            InputOutputModule.PersistentInputItemAreaState area = state.inputItemAreas[i];
            area.coordinate += offset;
            state.inputItemAreas[i] = area;
        }

        if (state.seedPlanterHasLoadedSeed)
        {
            state.seedPlanterLoadedSeedInputCoordinate += offset;
        }
    }

    private static void ShiftProfilingCoordinates(List<Vector2Int> coordinates, Vector2Int offset)
    {
        for (int i = 0; coordinates != null && i < coordinates.Count; i++)
        {
            coordinates[i] += offset;
        }
    }

    private static void ShiftProfilingRailPoints(List<Vector2> points, Vector2Int offset)
    {
        Vector2 vectorOffset = new Vector2(offset.x, offset.y);
        for (int i = 0; points != null && i < points.Count; i++)
        {
            points[i] += vectorOffset;
        }
    }

    private static ConveyorItemBlockSaveEntry CloneProfilingConveyorItemEntry(
        ConveyorItemBlockSaveEntry source,
        Vector2Int offset)
    {
        ConveyorItemBlockSaveEntry clone = new ConveyorItemBlockSaveEntry
        {
            coordinate = source.coordinate + offset,
            lanes = new List<ConveyorItemLaneSaveState>(source.lanes?.Count ?? 0)
        };
        for (int i = 0; source.lanes != null && i < source.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = source.lanes[i];
            if (lane == null)
            {
                continue;
            }

            BeltSavedLane nativeState = lane.nativeBeltState?.Clone();
            if (nativeState != null)
            {
                nativeState.X += offset.x;
                nativeState.Y += offset.y;
                if (nativeState.OriginLane >= 0)
                {
                    nativeState.OriginX += offset.x;
                    nativeState.OriginY += offset.y;
                }

                if (nativeState.CursorLane >= 0)
                {
                    nativeState.CursorX += offset.x;
                    nativeState.CursorY += offset.y;
                }

                BeltLaneState beltState = nativeState.State;
                beltState.StartX += offset.x;
                beltState.StartZ += offset.y;
                beltState.DropX += offset.x;
                beltState.DropZ += offset.y;
                nativeState.State = beltState;
            }

            Vector3 worldOffset = new Vector3(offset.x, 0f, offset.y);
            clone.lanes.Add(new ConveyorItemLaneSaveState
            {
                nativeBeltState = nativeState,
                laneIndex = lane.laneIndex,
                itemId = lane.itemId,
                visualWorldPosition = lane.visualWorldPosition + worldOffset,
                hasMotion = lane.hasMotion,
                useCornerMotion = lane.useCornerMotion,
                sourceLaneIndex = lane.sourceLaneIndex,
                destinationLaneIndex = lane.destinationLaneIndex,
                startWorldPosition = lane.startWorldPosition + worldOffset,
                hasViaWorldPosition = lane.hasViaWorldPosition,
                viaWorldPosition = lane.viaWorldPosition + worldOffset,
                progress = lane.progress,
                pathLength = lane.pathLength,
                durationPathLength = lane.durationPathLength,
                cornerContinuationActive = lane.cornerContinuationActive,
                cornerContinuationBlockCoordinate = lane.cornerContinuationBlockCoordinate + offset,
                cornerContinuationSourceLaneIndex = lane.cornerContinuationSourceLaneIndex,
                cornerContinuationDestinationLaneIndex = lane.cornerContinuationDestinationLaneIndex,
                cornerContinuationStartWorldPosition = lane.cornerContinuationStartWorldPosition + worldOffset,
                cornerContinuationStartProgress = lane.cornerContinuationStartProgress,
                cornerContinuationPathLength = lane.cornerContinuationPathLength,
                cornerContinuationDurationPathLength = lane.cornerContinuationDurationPathLength
            });
        }

        return clone;
    }

    private static ConveyorItemRunSaveEntry CloneProfilingConveyorRun(
        ConveyorItemRunSaveEntry source,
        Vector2Int offset)
    {
        ConveyorItemRunSaveEntry clone = new ConveyorItemRunSaveEntry
        {
            startCoordinate = source.startCoordinate + offset,
            startLaneIndex = source.startLaneIndex,
            endCoordinate = source.endCoordinate + offset,
            endLaneIndex = source.endLaneIndex,
            itemCount = source.itemCount,
            itemRuns = new List<ConveyorItemTypeRunSaveEntry>(source.itemRuns?.Count ?? 0)
        };
        for (int i = 0; source.itemRuns != null && i < source.itemRuns.Count; i++)
        {
            ConveyorItemTypeRunSaveEntry itemRun = source.itemRuns[i];
            if (itemRun != null)
            {
                clone.itemRuns.Add(new ConveyorItemTypeRunSaveEntry
                {
                    itemId = itemRun.itemId,
                    count = itemRun.count
                });
            }
        }

        return clone;
    }

    private static void CloneProfilingAnimals(
        List<AnimalSaveEntry> destination,
        IReadOnlyList<AnimalSaveEntry> sourceAnimals,
        Vector2Int offset,
        IReadOnlyDictionary<long, long> placementSequenceMap)
    {
        HashSet<long> usedIds = new HashSet<long>();
        for (int i = 0; i < destination.Count; i++)
        {
            if (destination[i] != null)
            {
                usedIds.Add(destination[i].deterministicId);
                usedIds.Add(destination[i].herdId);
            }
        }

        Dictionary<long, long> herdIdMap = new Dictionary<long, long>();
        Vector3 worldOffset = new Vector3(offset.x, 0f, offset.y);
        for (int i = 0; i < sourceAnimals.Count; i++)
        {
            AnimalSaveEntry source = sourceAnimals[i];
            AnimalSaveEntry clone = CloneAnimalSaveEntry(source);
            clone.deterministicId = CreateProfilingCloneId(
                source.deterministicId,
                offset,
                i,
                usedIds);
            if (source.herdId != 0L)
            {
                if (!herdIdMap.TryGetValue(source.herdId, out long clonedHerdId))
                {
                    clonedHerdId = CreateProfilingCloneId(
                        source.herdId,
                        offset,
                        sourceAnimals.Count + herdIdMap.Count,
                        usedIds);
                    herdIdMap.Add(source.herdId, clonedHerdId);
                }

                clone.herdId = clonedHerdId;
            }

            clone.position += worldOffset;
            clone.herdCenter += worldOffset;
            clone.targetPosition += worldOffset;
            if (clone.hasDraftHandcart)
            {
                clone.draftHandcartAnchorCoordinate += offset;
                clone.draftHandcartPlacementSequence = RemapProfilingPlacementSequence(
                    clone.draftHandcartPlacementSequence,
                    placementSequenceMap);
            }

            destination.Add(clone);
        }
    }

    private static long CreateProfilingCloneId(
        long sourceId,
        Vector2Int offset,
        int ordinal,
        ISet<long> usedIds)
    {
        unchecked
        {
            ulong value = 1469598103934665603UL;
            value = (value ^ (ulong)sourceId) * 1099511628211UL;
            value = (value ^ (uint)offset.x) * 1099511628211UL;
            value = (value ^ (uint)offset.y) * 1099511628211UL;
            value = (value ^ (uint)ordinal) * 1099511628211UL;
            long candidate = (long)value;
            while (candidate == 0L || usedIds.Contains(candidate))
            {
                candidate++;
            }

            usedIds.Add(candidate);
            return candidate;
        }
    }

    private static void RemoveProfilingCloneDestinationState(
        MapSaveData mapState,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        mapState.resources.RemoveAll(entry => entry != null && IsInsideProfilingCloneArea(entry.coordinate, minimum, maximum));
        mapState.floorObjects.RemoveAll(entry => entry != null && IsInsideProfilingCloneArea(entry.coordinate, minimum, maximum));
        mapState.conveyorItems.RemoveAll(entry => entry != null && IsInsideProfilingCloneArea(entry.coordinate, minimum, maximum));
        mapState.conveyorItemRuns.RemoveAll(entry => entry != null
            && (IsInsideProfilingCloneArea(entry.startCoordinate, minimum, maximum)
                || IsInsideProfilingCloneArea(entry.endCoordinate, minimum, maximum)));
        mapState.animals.RemoveAll(entry => entry != null
            && IsInsideProfilingCloneArea(entry.position, minimum, maximum));
        mapState.farmlandCoordinates.RemoveAll(coordinate => IsInsideProfilingCloneArea(coordinate, minimum, maximum));
        mapState.farmlandFertilizer.RemoveAll(entry => entry != null && IsInsideProfilingCloneArea(entry.coordinate, minimum, maximum));
        mapState.plantedResources.RemoveAll(entry => entry != null && IsInsideProfilingCloneArea(entry.coordinate, minimum, maximum));
    }

    private static List<ResourceSaveEntry> CollectSourceResources(
        IReadOnlyList<ResourceSaveEntry> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<ResourceSaveEntry> result = new List<ResourceSaveEntry>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (source[i] != null && IsInsideProfilingCloneArea(source[i].coordinate, minimum, maximum))
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static List<FloorObjectSaveEntry> CollectSourceFloorObjects(
        IReadOnlyList<FloorObjectSaveEntry> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<FloorObjectSaveEntry> result = new List<FloorObjectSaveEntry>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (source[i] != null && IsInsideProfilingCloneArea(source[i].coordinate, minimum, maximum))
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static List<InstallationSaveEntry> CollectSourceInstallations(
        IReadOnlyList<InstallationSaveEntry> source)
    {
        List<InstallationSaveEntry> result = new List<InstallationSaveEntry>(source?.Count ?? 0);
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (source[i]?.state != null)
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static List<ConveyorItemBlockSaveEntry> CollectSourceConveyorItems(
        IReadOnlyList<ConveyorItemBlockSaveEntry> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<ConveyorItemBlockSaveEntry> result = new List<ConveyorItemBlockSaveEntry>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (source[i] != null && IsInsideProfilingCloneArea(source[i].coordinate, minimum, maximum))
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static List<ConveyorItemRunSaveEntry> CollectSourceConveyorRuns(
        IReadOnlyList<ConveyorItemRunSaveEntry> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<ConveyorItemRunSaveEntry> result = new List<ConveyorItemRunSaveEntry>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            ConveyorItemRunSaveEntry entry = source[i];
            if (entry != null
                && IsInsideProfilingCloneArea(entry.startCoordinate, minimum, maximum)
                && IsInsideProfilingCloneArea(entry.endCoordinate, minimum, maximum))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static List<AnimalSaveEntry> CollectSourceAnimals(
        IReadOnlyList<AnimalSaveEntry> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<AnimalSaveEntry> result = new List<AnimalSaveEntry>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            AnimalSaveEntry entry = source[i];
            if (entry != null
                && !entry.removed
                && IsInsideProfilingCloneArea(entry.position, minimum, maximum))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static List<Vector2Int> CollectSourceCoordinates(
        IReadOnlyList<Vector2Int> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (IsInsideProfilingCloneArea(source[i], minimum, maximum))
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static List<FarmlandFertilizerSaveEntry> CollectSourceFertilizer(
        IReadOnlyList<FarmlandFertilizerSaveEntry> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<FarmlandFertilizerSaveEntry> result = new List<FarmlandFertilizerSaveEntry>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (source[i] != null && IsInsideProfilingCloneArea(source[i].coordinate, minimum, maximum))
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static List<PlantedResourceSaveEntry> CollectSourcePlants(
        IReadOnlyList<PlantedResourceSaveEntry> source,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        List<PlantedResourceSaveEntry> result = new List<PlantedResourceSaveEntry>();
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (source[i] != null && IsInsideProfilingCloneArea(source[i].coordinate, minimum, maximum))
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static bool IsInsideProfilingCloneArea(
        Vector2Int coordinate,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        return coordinate.x >= minimum.x
               && coordinate.y >= minimum.y
               && coordinate.x <= maximum.x
               && coordinate.y <= maximum.y;
    }

    private static bool IsInsideProfilingCloneArea(
        Vector3 position,
        Vector2Int minimum,
        Vector2Int maximum)
    {
        return position.x >= minimum.x - 0.5f
               && position.z >= minimum.y - 0.5f
               && position.x <= maximum.x + 0.5f
               && position.z <= maximum.y + 0.5f;
    }
}
