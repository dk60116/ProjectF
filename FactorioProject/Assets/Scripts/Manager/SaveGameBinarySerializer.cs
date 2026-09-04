using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

public static class SaveGameBinarySerializer
{
    private const string Magic = "PF_SAVE";
    private const int MaxSerializedListCount = 1000000;

    private enum SaveReadCompatibilityMode
    {
        Current = 0,
        LegacyV18AutoDriveInstallationFields = 1
    }

    public static void WriteToFile(string path, SaveGameData data)
    {
        if (string.IsNullOrEmpty(path) || data == null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{path}.tmp";
        using (FileStream fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (GZipStream gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionLevel.Fastest))
        using (BinaryWriter writer = new BinaryWriter(gzipStream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(SaveGameData.CurrentVersion);
            WriteSaveGameData(writer, data);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }

    public static SaveGameData ReadFromFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        byte[] payload;
        using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
        using (MemoryStream memoryStream = new MemoryStream())
        {
            gzipStream.CopyTo(memoryStream);
            payload = memoryStream.ToArray();
        }

        if (TryReadFromPayload(
                payload,
                SaveReadCompatibilityMode.Current,
                out SaveGameData data,
                out Exception currentException))
        {
            return data;
        }

        if (TryPeekSaveVersion(payload, out int version)
            && version == 18
            && TryReadFromPayload(
                payload,
                SaveReadCompatibilityMode.LegacyV18AutoDriveInstallationFields,
                out data,
                out _))
        {
            return data;
        }

        throw currentException;
    }

    private static bool TryReadFromPayload(
        byte[] payload,
        SaveReadCompatibilityMode compatibilityMode,
        out SaveGameData data,
        out Exception exception)
    {
        data = null;
        exception = null;

        try
        {
            using (MemoryStream memoryStream = new MemoryStream(payload, false))
            using (BinaryReader reader = new BinaryReader(memoryStream, Encoding.UTF8))
            {
                string magic = reader.ReadString();
                if (!string.Equals(magic, Magic, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Unsupported save file format.");
                }

                int version = reader.ReadInt32();
                if (version <= 0 || version > SaveGameData.CurrentVersion)
                {
                    throw new InvalidDataException($"Unsupported save version: {version}");
                }

                data = ReadSaveGameData(reader, version, compatibilityMode);
                if (memoryStream.Position != memoryStream.Length)
                {
                    throw new InvalidDataException(
                        $"Save file contains {memoryStream.Length - memoryStream.Position} unread bytes.");
                }

                return true;
            }
        }
        catch (Exception readException)
        {
            exception = readException;
            data = null;
            return false;
        }
    }

    private static bool TryPeekSaveVersion(byte[] payload, out int version)
    {
        version = 0;
        if (payload == null || payload.Length <= 0)
        {
            return false;
        }

        try
        {
            using (MemoryStream memoryStream = new MemoryStream(payload, false))
            using (BinaryReader reader = new BinaryReader(memoryStream, Encoding.UTF8))
            {
                string magic = reader.ReadString();
                if (!string.Equals(magic, Magic, StringComparison.Ordinal))
                {
                    return false;
                }

                version = reader.ReadInt32();
                return true;
            }
        }
        catch
        {
            version = 0;
            return false;
        }
    }

    private static void WriteSaveGameData(BinaryWriter writer, SaveGameData data)
    {
        writer.Write(data.version);
        writer.Write(data.savedAtUtcTicks);
        WriteItemCatalog(writer, data.itemCatalog);
        WriteTerrain(writer, data.terrain);
        WriteWorldTime(writer, data.worldTime);
        WriteMap(writer, data.map);
        WritePlayer(writer, data.player);
    }

    private static SaveGameData ReadSaveGameData(
        BinaryReader reader,
        int fileVersion,
        SaveReadCompatibilityMode compatibilityMode)
    {
        SaveGameData data = new SaveGameData
        {
            version = reader.ReadInt32(),
            savedAtUtcTicks = reader.ReadInt64()
        };

        data.itemCatalog = fileVersion >= 17
            ? ReadList(reader, () => ReadItemCatalogEntry(reader))
            : new List<SaveItemCatalogEntry>();
        data.terrain = ReadTerrain(reader, fileVersion);
        data.worldTime = fileVersion >= 24
            ? ReadWorldTime(reader)
            : new WorldTimeSaveData { hasTime = false };
        data.map = ReadMap(reader, fileVersion, compatibilityMode);
        data.player = ReadPlayer(reader, fileVersion);
        return data;
    }

    private static void WriteItemCatalog(BinaryWriter writer, List<SaveItemCatalogEntry> itemCatalog)
    {
        WriteList(writer, itemCatalog, WriteItemCatalogEntry);
    }

    private static void WriteItemCatalogEntry(BinaryWriter writer, SaveItemCatalogEntry entry)
    {
        entry ??= new SaveItemCatalogEntry();
        writer.Write(entry.itemId);
        writer.Write(entry.itemName ?? string.Empty);
    }

    private static SaveItemCatalogEntry ReadItemCatalogEntry(BinaryReader reader)
    {
        return new SaveItemCatalogEntry
        {
            itemId = reader.ReadInt32(),
            itemName = reader.ReadString()
        };
    }

    private static void WriteTerrain(BinaryWriter writer, TerrainSaveData terrain)
    {
        terrain ??= new TerrainSaveData();
        writer.Write(terrain.seed);
        writer.Write(terrain.mapSize);
        WriteVector2IntList(writer, terrain.activeChunkCoordinates);
    }

    private static TerrainSaveData ReadTerrain(BinaryReader reader, int version)
    {
        TerrainSaveData terrain = new TerrainSaveData
        {
            seed = reader.ReadInt32()
        };

        if (version >= 9)
        {
            terrain.mapSize = reader.ReadInt32();
        }

        if (version >= 49)
        {
            terrain.activeChunkCoordinates = ReadVector2IntList(reader);
        }

        if (version <= 1)
        {
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
        }

        return terrain;
    }

    private static void WriteWorldTime(BinaryWriter writer, WorldTimeSaveData worldTime)
    {
        worldTime ??= new WorldTimeSaveData();
        writer.Write(worldTime.hasTime);
        writer.Write(Mathf.Max(1, worldTime.dayIndex));
        writer.Write(worldTime.secondsOfDay);
    }

    private static WorldTimeSaveData ReadWorldTime(BinaryReader reader)
    {
        return new WorldTimeSaveData
        {
            hasTime = reader.ReadBoolean(),
            dayIndex = Mathf.Max(1, reader.ReadInt32()),
            secondsOfDay = reader.ReadDouble()
        };
    }

    public static bool RunWorldTimeRoundTripSelfCheck(out string firstIssue)
    {
        WorldTimeSaveData expected = new WorldTimeSaveData
        {
            hasTime = true,
            dayIndex = 17,
            secondsOfDay = (18d * WorldTimeService.GameSecondsPerHour)
                           + (45d * WorldTimeService.SecondsPerMinute)
        };

        using MemoryStream stream = new MemoryStream();
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            WriteWorldTime(writer, expected);
        }

        stream.Position = 0L;
        WorldTimeSaveData actual;
        using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            actual = ReadWorldTime(reader);
        }

        if (actual.hasTime != expected.hasTime
            || actual.dayIndex != expected.dayIndex
            || Math.Abs(actual.secondsOfDay - expected.secondsOfDay) > 0.001d)
        {
            firstIssue = "world_time_save_roundtrip_mismatch";
            return false;
        }

        firstIssue = string.Empty;
        return true;
    }

    public static bool RunConveyorItemRunRoundTripSelfCheck(out string firstIssue)
    {
        ConveyorItemRunSaveEntry expected = new ConveyorItemRunSaveEntry
        {
            startCoordinate = new Vector2Int(-17, 23),
            startLaneIndex = 2,
            endCoordinate = new Vector2Int(91, -4),
            endLaneIndex = 0,
            itemCount = 5,
            itemRuns = new List<ConveyorItemTypeRunSaveEntry>
            {
                new ConveyorItemTypeRunSaveEntry { itemId = 7, count = 2 },
                new ConveyorItemTypeRunSaveEntry { itemId = 11, count = 3 }
            }
        };

        using MemoryStream stream = new MemoryStream();
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            WriteConveyorItemRunEntry(writer, expected);
        }

        stream.Position = 0L;
        ConveyorItemRunSaveEntry actual;
        using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            actual = ReadConveyorItemRunEntry(reader);
        }

        if (actual == null
            || actual.startCoordinate != expected.startCoordinate
            || actual.startLaneIndex != expected.startLaneIndex
            || actual.endCoordinate != expected.endCoordinate
            || actual.endLaneIndex != expected.endLaneIndex
            || actual.itemCount != expected.itemCount
            || actual.itemRuns == null
            || actual.itemRuns.Count != expected.itemRuns.Count)
        {
            firstIssue = "conveyor_item_run_save_roundtrip_mismatch";
            return false;
        }

        for (int i = 0; i < expected.itemRuns.Count; i++)
        {
            if (actual.itemRuns[i] == null
                || actual.itemRuns[i].itemId != expected.itemRuns[i].itemId
                || actual.itemRuns[i].count != expected.itemRuns[i].count)
            {
                firstIssue = "conveyor_item_type_run_save_roundtrip_mismatch";
                return false;
            }
        }

        firstIssue = string.Empty;
        return true;
    }

    private static void WriteMap(BinaryWriter writer, MapSaveData map)
    {
        map ??= new MapSaveData();

        WriteList(writer, map.resources, WriteResourceEntry);
        WriteList(writer, map.floorObjects, WriteFloorObjectEntry);
        WriteList(writer, map.installations, WriteInstallationEntry);
        WriteList(writer, map.conveyorItems, WriteConveyorBlockEntry);
        WriteList(writer, map.animals, WriteAnimalEntry);
        WriteList(writer, map.farmlandCoordinates, WriteVector2Int);
        WriteList(writer, map.plantedResources, WritePlantedResourceEntry);
        WriteList(writer, map.farmlandFertilizer, WriteFarmlandFertilizerEntry);
        WriteList(writer, map.conveyorItemRuns, WriteConveyorItemRunEntry);
    }

    private static MapSaveData ReadMap(
        BinaryReader reader,
        int version,
        SaveReadCompatibilityMode compatibilityMode)
    {
        MapSaveData map = new MapSaveData
        {
            resources = ReadList(reader, () => ReadResourceEntry(reader, version)),
            floorObjects = ReadList(reader, () => ReadFloorObjectEntry(reader)),
            installations = ReadList(reader, () => ReadInstallationEntry(reader, version, compatibilityMode)),
            conveyorItems = ReadList(reader, () => ReadConveyorBlockEntry(reader))
        };

        if (version >= 22)
        {
            map.animals = ReadList(reader, () => ReadAnimalEntry(reader, version));
        }

        if (version >= 38)
        {
            map.farmlandCoordinates = ReadList(reader, () => ReadVector2Int(reader));
        }

        if (version >= 40)
        {
            map.plantedResources = ReadList(reader, () => ReadPlantedResourceEntry(reader));
        }

        if (version >= 43)
        {
            map.farmlandFertilizer = ReadList(
                reader,
                () => ReadFarmlandFertilizerEntry(reader));
        }

        if (version >= 50)
        {
            map.conveyorItemRuns = ReadList(reader, () => ReadConveyorItemRunEntry(reader));
        }

        return map;
    }

    private static void WriteFarmlandFertilizerEntry(
        BinaryWriter writer,
        FarmlandFertilizerSaveEntry entry)
    {
        entry ??= new FarmlandFertilizerSaveEntry();
        WriteVector2Int(writer, entry.coordinate);
        writer.Write(Mathf.Max(0f, entry.fertilizerEnergy));
    }

    private static FarmlandFertilizerSaveEntry ReadFarmlandFertilizerEntry(
        BinaryReader reader)
    {
        return new FarmlandFertilizerSaveEntry
        {
            coordinate = ReadVector2Int(reader),
            fertilizerEnergy = Mathf.Max(0f, reader.ReadSingle())
        };
    }

    private static void WritePlantedResourceEntry(
        BinaryWriter writer,
        PlantedResourceSaveEntry entry)
    {
        entry ??= new PlantedResourceSaveEntry();
        WriteVector2Int(writer, entry.coordinate);
        writer.Write(entry.seedItemId);
    }

    private static PlantedResourceSaveEntry ReadPlantedResourceEntry(BinaryReader reader)
    {
        return new PlantedResourceSaveEntry
        {
            coordinate = ReadVector2Int(reader),
            seedItemId = reader.ReadInt32()
        };
    }

    private static void WriteAnimalEntry(BinaryWriter writer, AnimalSaveEntry entry)
    {
        entry ??= new AnimalSaveEntry();
        writer.Write(entry.deterministicId);
        writer.Write(entry.definitionId);
        WriteVector3(writer, entry.position);
        WriteQuaternion(writer, entry.rotation);
        writer.Write(entry.age);
        writer.Write(entry.baseScale);
        writer.Write(entry.removed);
        writer.Write(entry.herdId);
        WriteVector3(writer, entry.herdCenter);
        writer.Write(entry.herdRadius);
        writer.Write(entry.behaviorState);
        writer.Write(entry.behaviorTimeRemaining);
        WriteVector3(writer, entry.targetPosition);
        writer.Write(entry.hasTarget);
        writer.Write(entry.movingToActivity);
        writer.Write(entry.randomState);
        writer.Write(entry.hasHealth);
        writer.Write(entry.currentHealth);
        writer.Write(entry.corpseLootInitialized);
        WriteIntList(writer, entry.corpseRemainingItemIds);
        writer.Write(entry.hasSaddle);
        writer.Write(entry.hasDraftHandcart);
        writer.Write(entry.draftHandcartAnchorCoordinate.x);
        writer.Write(entry.draftHandcartAnchorCoordinate.y);
        writer.Write(entry.draftHandcartPlacementSequence);
        writer.Write(entry.hasNeedsState);
        writer.Write(entry.currentHunger);
        writer.Write(entry.defecationTimeRemaining);
        writer.Write(entry.digestedMealCount);
    }

    private static AnimalSaveEntry ReadAnimalEntry(BinaryReader reader, int version)
    {
        AnimalSaveEntry entry = new AnimalSaveEntry
        {
            deterministicId = reader.ReadInt64(),
            definitionId = reader.ReadInt32(),
            position = ReadVector3(reader),
            rotation = ReadQuaternion(reader),
            age = reader.ReadSingle(),
            baseScale = reader.ReadSingle(),
            removed = reader.ReadBoolean()
        };

        if (version >= 23)
        {
            entry.herdId = reader.ReadInt64();
            entry.herdCenter = ReadVector3(reader);
            entry.herdRadius = reader.ReadSingle();
            entry.behaviorState = reader.ReadInt32();
            entry.behaviorTimeRemaining = reader.ReadSingle();
            entry.targetPosition = ReadVector3(reader);
            entry.hasTarget = reader.ReadBoolean();
            entry.movingToActivity = reader.ReadBoolean();
            entry.randomState = reader.ReadInt32();
        }
        else
        {
            entry.herdId = entry.deterministicId;
            entry.herdCenter = entry.position;
            entry.herdRadius = AnimalAISettings.DefaultHerdAreaRadius;
        }

        if (version >= 25)
        {
            entry.hasHealth = reader.ReadBoolean();
            entry.currentHealth = reader.ReadSingle();
        }

        if (version >= 26)
        {
            entry.corpseLootInitialized = reader.ReadBoolean();
            entry.corpseRemainingItemIds = ReadIntList(reader);
        }

        if (version >= 35)
        {
            entry.hasSaddle = reader.ReadBoolean();
        }

        if (version >= 37)
        {
            entry.hasDraftHandcart = reader.ReadBoolean();
            entry.draftHandcartAnchorCoordinate = new Vector2Int(
                reader.ReadInt32(),
                reader.ReadInt32());
            entry.draftHandcartPlacementSequence = reader.ReadInt64();
        }

        if (version >= 45)
        {
            entry.hasNeedsState = reader.ReadBoolean();
            entry.currentHunger = reader.ReadSingle();
            entry.defecationTimeRemaining = reader.ReadSingle();
            entry.digestedMealCount = reader.ReadInt32();
        }

        return entry;
    }

    private static void WriteResourceEntry(BinaryWriter writer, ResourceSaveEntry entry)
    {
        entry ??= new ResourceSaveEntry();
        WriteVector2Int(writer, entry.coordinate);
        writer.Write(entry.itemId);
        writer.Write(entry.state.resourceCount);
        writer.Write(entry.state.maxGauge);
        writer.Write(entry.state.currentGauge);
        writer.Write(entry.state.initialResourceCount);
        writer.Write(entry.state.hasBodyYawStep);
        writer.Write(entry.state.bodyYawStep);
        writer.Write(entry.state.hasGrowth);
        writer.Write(entry.state.growth);
        writer.Write(entry.state.hasPlantGrowthState);
        writer.Write(entry.state.growthWaterLiters);
        writer.Write(entry.state.growthFertilizerAmount);
        writer.Write(entry.state.growthElapsedSeconds);
    }

    private static ResourceSaveEntry ReadResourceEntry(BinaryReader reader, int version)
    {
        ResourceSaveEntry entry = new ResourceSaveEntry
        {
            coordinate = ReadVector2Int(reader),
            itemId = reader.ReadInt32(),
            state = new Resource.ResourceSaveState
            {
                resourceCount = reader.ReadInt32(),
                maxGauge = reader.ReadInt32(),
                currentGauge = reader.ReadInt32(),
                initialResourceCount = reader.ReadInt32(),
                hasBodyYawStep = reader.ReadBoolean(),
                bodyYawStep = reader.ReadInt32()
            }
        };

        if (version >= 39)
        {
            entry.state.hasGrowth = reader.ReadBoolean();
            entry.state.growth = reader.ReadSingle();
        }

        if (version >= 41)
        {
            entry.state.hasPlantGrowthState = reader.ReadBoolean();
            entry.state.growthWaterLiters = reader.ReadSingle();
            entry.state.growthFertilizerAmount = reader.ReadSingle();
            entry.state.growthElapsedSeconds = reader.ReadSingle();
        }

        return entry;
    }

    private static void WriteFloorObjectEntry(BinaryWriter writer, FloorObjectSaveEntry entry)
    {
        entry ??= new FloorObjectSaveEntry();
        WriteVector2Int(writer, entry.coordinate);
        WriteIntList(writer, entry.itemIds);
    }

    private static FloorObjectSaveEntry ReadFloorObjectEntry(BinaryReader reader)
    {
        return new FloorObjectSaveEntry
        {
            coordinate = ReadVector2Int(reader),
            itemIds = ReadIntList(reader)
        };
    }

    private static void WriteInstallationEntry(BinaryWriter writer, InstallationSaveEntry entry)
    {
        WriteInstallationState(writer, entry?.state);
    }

    private static InstallationSaveEntry ReadInstallationEntry(
        BinaryReader reader,
        int version,
        SaveReadCompatibilityMode compatibilityMode)
    {
        return new InstallationSaveEntry
        {
            state = ReadInstallationState(reader, version, compatibilityMode)
        };
    }

    private static void WriteInstallationState(BinaryWriter writer, BlockStateStore.InstallationSaveState state)
    {
        writer.Write(state != null);
        if (state == null)
        {
            return;
        }

        WriteVector2Int(writer, state.anchorCoordinate);
        writer.Write(state.itemId);
        writer.Write(state.itemName ?? string.Empty);
        writer.Write(state.quarterTurns);
        writer.Write(state.placementSequence);
        writer.Write(state.conveyorVariantKind);
        WriteVector2IntList(writer, state.occupiedCoordinates);
        WriteVector2List(writer, state.railVisualPathPoints);
        writer.Write(state.railVisualPathExtendsStart);
        writer.Write(state.railVisualPathExtendsEnd);
        writer.Write(state.railRequiredItemCount);
        WriteInputOutputState(writer, state.inputOutputState);
        WriteRobotArmState(writer, state.robotArmState);
        writer.Write(0L); // Legacy background-simulation slot retained for binary layout.
        writer.Write(state.boxIsOpen.HasValue);
        if (state.boxIsOpen.HasValue)
        {
            writer.Write(state.boxIsOpen.Value);
        }

        writer.Write(state.itemFilterMaskInitialized);
        WriteUlongList(writer, state.itemFilterMaskWords);
        writer.Write(state.storedFluidLiters);
        writer.Write(state.storedFluidItemId);
        writer.Write(state.storedFluidTemperatureCelsius);
        writer.Write(state.hasStorageKey);
        WriteVector2Int(writer, state.storageKey);
        writer.Write(state.hasWorldPose);
        if (state.hasWorldPose)
        {
            WriteVector3(writer, state.worldPosition);
            WriteQuaternion(writer, state.worldRotation);
        }

        writer.Write(state.hasTrainRailSample);
        if (state.hasTrainRailSample)
        {
            writer.Write(state.trainRailPlacementSequence);
            WriteVector2Int(writer, state.trainRailAnchorCoordinate);
            writer.Write(state.trainRailDistanceAlongPath);
            WriteVector2(writer, state.trainRailPathPoint);
            WriteVector2(writer, state.trainRailFacingTangent);
        }

        writer.Write(state.hasSteamTrainBurnEnergyState);
        if (state.hasSteamTrainBurnEnergyState)
        {
            writer.Write(state.steamTrainStoredBurnEnergy);
            writer.Write(state.steamTrainBurnEnergyGaugeCapacity);
        }

        writer.Write(state.stationName ?? string.Empty);
        writer.Write(state.steamTrainAutoDriveEnabled);
        writer.Write(state.steamTrainAutoDriveTargetAStationName ?? string.Empty);
        writer.Write(state.steamTrainAutoDriveTargetBStationName ?? string.Empty);
        writer.Write(state.steamTrainAutoDriveFuelFilter);
        writer.Write(state.steamTrainAutoDriveFreightFilter);
        writer.Write(state.steamTrainAutoDriveRouteTargetStationName ?? string.Empty);
        writer.Write(state.steamTrainAutoDriveLastArrivedStationName ?? string.Empty);
        writer.Write(state.steamTrainAutoDriveStationWaitTimer);
        writer.Write(state.storedInstallationItemId);
        WriteIntList(writer, state.storedInstallationItemIds);
        writer.Write(state.pipeConnectionMask);
        writer.Write(state.loggingTreeFilterInitialized);
        WriteStringList(writer, state.loggingEnabledTreeDefinitionKeys);
        writer.Write(state.loggingMinimumGrowth);
        writer.Write(state.utilityPoleConnectionsInitialized);
        WriteVector2IntList(writer, state.utilityPoleConnectedAnchors);
    }

    private static BlockStateStore.InstallationSaveState ReadInstallationState(
        BinaryReader reader,
        int version,
        SaveReadCompatibilityMode compatibilityMode)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        BlockStateStore.InstallationSaveState state = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = ReadVector2Int(reader),
            itemId = reader.ReadInt32(),
            itemName = version >= 17 ? reader.ReadString() : string.Empty,
            quarterTurns = reader.ReadInt32(),
            placementSequence = reader.ReadInt64(),
            conveyorVariantKind = reader.ReadInt32(),
            occupiedCoordinates = ReadVector2IntList(reader),
            railVisualPathPoints = version >= 10 ? ReadVector2List(reader) : new List<Vector2>(),
            railVisualPathExtendsStart = version >= 11 ? reader.ReadBoolean() : true,
            railVisualPathExtendsEnd = version >= 11 ? reader.ReadBoolean() : true,
            railRequiredItemCount = version >= 21 ? reader.ReadInt32() : 0,
            inputOutputState = ReadInputOutputState(reader, version),
            robotArmState = version >= 3 ? ReadRobotArmState(reader) : null
        };
        _ = reader.ReadInt64(); // Discard the legacy background-simulation slot.

        if (reader.ReadBoolean())
        {
            state.boxIsOpen = reader.ReadBoolean();
        }

        state.itemFilterMaskInitialized = reader.ReadBoolean();
        state.itemFilterMaskWords = ReadUlongList(reader);
        if (version >= 5)
        {
            state.storedFluidLiters = reader.ReadSingle();
        }
        if (version >= 6)
        {
            state.storedFluidItemId = reader.ReadInt32();
        }
        if (version >= 8)
        {
            state.storedFluidTemperatureCelsius = reader.ReadSingle();
        }
        else
        {
            state.storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        }

        if (version >= 13)
        {
            state.hasStorageKey = reader.ReadBoolean();
            state.storageKey = ReadVector2Int(reader);
        }

        if (version >= 14)
        {
            state.hasWorldPose = reader.ReadBoolean();
            if (state.hasWorldPose)
            {
                state.worldPosition = ReadVector3(reader);
                state.worldRotation = ReadQuaternion(reader);
            }
        }

        if (version >= 15)
        {
            state.hasTrainRailSample = reader.ReadBoolean();
            if (state.hasTrainRailSample)
            {
                state.trainRailPlacementSequence = reader.ReadInt64();
                state.trainRailAnchorCoordinate = ReadVector2Int(reader);
                state.trainRailDistanceAlongPath = reader.ReadSingle();
                state.trainRailPathPoint = ReadVector2(reader);
                state.trainRailFacingTangent = ReadVector2(reader);
            }
        }

        if (version >= 16)
        {
            state.hasSteamTrainBurnEnergyState = reader.ReadBoolean();
            if (state.hasSteamTrainBurnEnergyState)
            {
                state.steamTrainStoredBurnEnergy = reader.ReadSingle();
                state.steamTrainBurnEnergyGaugeCapacity = reader.ReadSingle();
            }
        }

        if (version == 18
            && compatibilityMode == SaveReadCompatibilityMode.LegacyV18AutoDriveInstallationFields)
        {
            reader.ReadBoolean();
            reader.ReadString();
            reader.ReadString();
            reader.ReadInt32();
            reader.ReadInt32();
        }

        if (version >= 20)
        {
            state.stationName = reader.ReadString();
            state.steamTrainAutoDriveEnabled = reader.ReadBoolean();
            state.steamTrainAutoDriveTargetAStationName = reader.ReadString();
            state.steamTrainAutoDriveTargetBStationName = reader.ReadString();
            state.steamTrainAutoDriveFuelFilter = reader.ReadInt32();
            state.steamTrainAutoDriveFreightFilter = reader.ReadInt32();
            state.steamTrainAutoDriveRouteTargetStationName = reader.ReadString();
            state.steamTrainAutoDriveLastArrivedStationName = reader.ReadString();
            state.steamTrainAutoDriveStationWaitTimer = reader.ReadSingle();
        }

        if (version >= 29)
        {
            state.storedInstallationItemId = reader.ReadInt32();
        }
        if (version >= 30)
        {
            state.storedInstallationItemIds = ReadIntList(reader);
        }
        if (version >= 34)
        {
            state.pipeConnectionMask = reader.ReadInt32();
        }
        if (version >= 42)
        {
            state.loggingTreeFilterInitialized = reader.ReadBoolean();
            state.loggingEnabledTreeDefinitionKeys = ReadStringList(reader);
            state.loggingMinimumGrowth = reader.ReadInt32();
        }
        if (version >= 48)
        {
            state.utilityPoleConnectionsInitialized = reader.ReadBoolean();
            state.utilityPoleConnectedAnchors = ReadVector2IntList(reader);
        }

        return state;
    }

    private static void WriteInputOutputState(BinaryWriter writer, InputOutputModule.PersistentState state)
    {
        writer.Write(state != null);
        if (state == null)
        {
            return;
        }

        WriteVector2IntList(writer, state.inputEnergyCoordinates);
        WriteInputItemAreaList(writer, state.inputItemAreas);
        WriteVector2IntList(writer, state.outputCoordinates);
        WriteVector2IntList(writer, state.gridCoordinates);
        WriteVector2IntList(writer, state.focusCoordinates);
        writer.Write(state.storedEnergy);
        writer.Write(state.energyGaugeCapacity);
        writer.Write(state.hasActiveCraft);
        writer.Write(state.waitingForOutput);
        writer.Write(state.remainingCraftTime);
        writer.Write(state.activeCraftConsumedEnergy);
        writer.Write(state.activeRecipeIndex);
        writer.Write(state.activeOutputItemId);
        writer.Write(state.activeOutputCount);
        writer.Write(state.boilerWaterTemperatureCelsius);
        writer.Write(state.boilerSteamLiterAccumulator);
        writer.Write(state.oilDrillingProgressLiters);
        WriteVector2IntList(writer, state.pipeInputCoordinates);
        writer.Write(state.sprinklerSprayElapsedSeconds);
        writer.Write(state.seedPlanterPlantElapsedSeconds);
        writer.Write(state.steamGeneratorHasGenerationReserve);
    }

    private static InputOutputModule.PersistentState ReadInputOutputState(BinaryReader reader, int version)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        InputOutputModule.PersistentState state = new InputOutputModule.PersistentState
        {
            inputEnergyCoordinates = ReadVector2IntList(reader),
            inputItemAreas = ReadInputItemAreaList(reader),
            outputCoordinates = ReadVector2IntList(reader),
            gridCoordinates = ReadVector2IntList(reader),
            focusCoordinates = ReadVector2IntList(reader),
            storedEnergy = reader.ReadSingle(),
            energyGaugeCapacity = reader.ReadSingle(),
            hasActiveCraft = reader.ReadBoolean(),
            waitingForOutput = reader.ReadBoolean(),
            remainingCraftTime = reader.ReadSingle(),
            activeCraftConsumedEnergy = reader.ReadSingle(),
            activeRecipeIndex = reader.ReadInt32(),
            activeOutputItemId = reader.ReadInt32(),
            activeOutputCount = reader.ReadInt32()
        };

        if (version >= 7)
        {
            state.boilerWaterTemperatureCelsius = reader.ReadSingle();
            state.boilerSteamLiterAccumulator = reader.ReadSingle();
        }

        if (version >= 33)
        {
            state.oilDrillingProgressLiters = reader.ReadSingle();
        }

        if (version >= 44)
        {
            state.pipeInputCoordinates = ReadVector2IntList(reader);
            state.sprinklerSprayElapsedSeconds = reader.ReadSingle();
            state.seedPlanterPlantElapsedSeconds = reader.ReadSingle();
            if (version < 46)
            {
                // Versions 44-45 stored a Seed Planter power snapshot that is no
                // longer used because background power is evaluated per network.
                reader.ReadBoolean();
            }

            if (version >= 47)
            {
                state.steamGeneratorHasGenerationReserve = reader.ReadBoolean();
            }
        }

        return state;
    }

    private static void WriteRobotArmState(BinaryWriter writer, RobotArm.PersistentState state)
    {
        writer.Write(state != null);
        if (state == null)
        {
            return;
        }

        writer.Write(state.heldItemId);
        writer.Write((int)state.state);
        writer.Write(state.pickupTimer);
        writer.Write(state.dropRetryTimer);
        writer.Write(state.actionTurnTimer);
        writer.Write(state.turnTimer);
        writer.Write(state.waitingForDropRetry);
    }

    private static RobotArm.PersistentState ReadRobotArmState(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        return new RobotArm.PersistentState
        {
            heldItemId = reader.ReadInt32(),
            state = (RobotArm.RobotArmState)reader.ReadInt32(),
            pickupTimer = reader.ReadSingle(),
            dropRetryTimer = reader.ReadSingle(),
            actionTurnTimer = reader.ReadSingle(),
            turnTimer = reader.ReadSingle(),
            waitingForDropRetry = reader.ReadBoolean()
        };
    }

    private static void WriteConveyorBlockEntry(BinaryWriter writer, ConveyorItemBlockSaveEntry entry)
    {
        entry ??= new ConveyorItemBlockSaveEntry();
        WriteVector2Int(writer, entry.coordinate);
        WriteList(writer, entry.lanes, WriteConveyorLaneEntry);
    }

    private static ConveyorItemBlockSaveEntry ReadConveyorBlockEntry(BinaryReader reader)
    {
        return new ConveyorItemBlockSaveEntry
        {
            coordinate = ReadVector2Int(reader),
            lanes = ReadList(reader, () => ReadConveyorLaneEntry(reader))
        };
    }

    private static void WriteConveyorItemRunEntry(
        BinaryWriter writer,
        ConveyorItemRunSaveEntry entry)
    {
        entry ??= new ConveyorItemRunSaveEntry();
        WriteVector2Int(writer, entry.startCoordinate);
        writer.Write(entry.startLaneIndex);
        WriteVector2Int(writer, entry.endCoordinate);
        writer.Write(entry.endLaneIndex);
        writer.Write(Mathf.Max(0, entry.itemCount));
        WriteList(writer, entry.itemRuns, WriteConveyorItemTypeRunEntry);
    }

    private static ConveyorItemRunSaveEntry ReadConveyorItemRunEntry(BinaryReader reader)
    {
        return new ConveyorItemRunSaveEntry
        {
            startCoordinate = ReadVector2Int(reader),
            startLaneIndex = reader.ReadInt32(),
            endCoordinate = ReadVector2Int(reader),
            endLaneIndex = reader.ReadInt32(),
            itemCount = reader.ReadInt32(),
            itemRuns = ReadList(reader, () => ReadConveyorItemTypeRunEntry(reader))
        };
    }

    private static void WriteConveyorItemTypeRunEntry(
        BinaryWriter writer,
        ConveyorItemTypeRunSaveEntry entry)
    {
        entry ??= new ConveyorItemTypeRunSaveEntry();
        writer.Write(entry.itemId);
        writer.Write(Mathf.Max(0, entry.count));
    }

    private static ConveyorItemTypeRunSaveEntry ReadConveyorItemTypeRunEntry(BinaryReader reader)
    {
        return new ConveyorItemTypeRunSaveEntry
        {
            itemId = reader.ReadInt32(),
            count = reader.ReadInt32()
        };
    }

    private static void WriteConveyorLaneEntry(BinaryWriter writer, ConveyorItemLaneSaveState state)
    {
        state ??= new ConveyorItemLaneSaveState();
        writer.Write(state.laneIndex);
        writer.Write(state.itemId);
        WriteVector3(writer, state.visualWorldPosition);
        writer.Write(state.hasMotion);
        writer.Write(state.useCornerMotion);
        writer.Write(state.sourceLaneIndex);
        writer.Write(state.destinationLaneIndex);
        WriteVector3(writer, state.startWorldPosition);
        writer.Write(state.hasViaWorldPosition);
        WriteVector3(writer, state.viaWorldPosition);
        writer.Write(state.progress);
        writer.Write(state.pathLength);
        writer.Write(state.durationPathLength);
        writer.Write(state.cornerContinuationActive);
        WriteVector2Int(writer, state.cornerContinuationBlockCoordinate);
        writer.Write(state.cornerContinuationSourceLaneIndex);
        writer.Write(state.cornerContinuationDestinationLaneIndex);
        WriteVector3(writer, state.cornerContinuationStartWorldPosition);
        writer.Write(state.cornerContinuationStartProgress);
        writer.Write(state.cornerContinuationPathLength);
        writer.Write(state.cornerContinuationDurationPathLength);
    }

    private static ConveyorItemLaneSaveState ReadConveyorLaneEntry(BinaryReader reader)
    {
        return new ConveyorItemLaneSaveState
        {
            laneIndex = reader.ReadInt32(),
            itemId = reader.ReadInt32(),
            visualWorldPosition = ReadVector3(reader),
            hasMotion = reader.ReadBoolean(),
            useCornerMotion = reader.ReadBoolean(),
            sourceLaneIndex = reader.ReadInt32(),
            destinationLaneIndex = reader.ReadInt32(),
            startWorldPosition = ReadVector3(reader),
            hasViaWorldPosition = reader.ReadBoolean(),
            viaWorldPosition = ReadVector3(reader),
            progress = reader.ReadSingle(),
            pathLength = reader.ReadSingle(),
            durationPathLength = reader.ReadSingle(),
            cornerContinuationActive = reader.ReadBoolean(),
            cornerContinuationBlockCoordinate = ReadVector2Int(reader),
            cornerContinuationSourceLaneIndex = reader.ReadInt32(),
            cornerContinuationDestinationLaneIndex = reader.ReadInt32(),
            cornerContinuationStartWorldPosition = ReadVector3(reader),
            cornerContinuationStartProgress = reader.ReadSingle(),
            cornerContinuationPathLength = reader.ReadSingle(),
            cornerContinuationDurationPathLength = reader.ReadSingle()
        };
    }

    private static void WritePlayer(BinaryWriter writer, PlayerSaveData player)
    {
        player ??= new PlayerSaveData();
        writer.Write(player.hasPlayer);
        WriteVector3(writer, player.position);
        WriteQuaternion(writer, player.rotation);
        writer.Write(player.mountedOnVehicle);
        writer.Write(player.mountedVehiclePlacementSequence);
        WriteVector2Int(writer, player.mountedVehicleAnchorCoordinate);
        writer.Write(player.mountedVehiclePlayerPointIndex);
        writer.Write(player.bagLevel);
        WritePlayerStats(writer, player.stats);
        WriteList(writer, player.bagSlots, WritePlayerSlot);
        WriteList(writer, player.handSlots, WritePlayerSlot);
        WriteList(writer, player.craftingQueue, WritePlayerCraftingQueueEntry);
        writer.Write(player.nooseLeashedAnimalId);
        writer.Write(player.activeTorchItemId);
        writer.Write(player.activeTorchRemainingEnergy);
        writer.Write(player.mountedAnimalId);
    }

    private static PlayerSaveData ReadPlayer(BinaryReader reader, int version)
    {
        PlayerSaveData player = new PlayerSaveData
        {
            hasPlayer = reader.ReadBoolean(),
            position = ReadVector3(reader),
            rotation = ReadQuaternion(reader),
        };

        if (version >= 12)
        {
            player.mountedOnVehicle = reader.ReadBoolean();
            player.mountedVehiclePlacementSequence = reader.ReadInt64();
            player.mountedVehicleAnchorCoordinate = ReadVector2Int(reader);
            player.mountedVehiclePlayerPointIndex = reader.ReadInt32();
        }

        player.bagLevel = reader.ReadInt32();
        player.stats = ReadPlayerStats(reader);
        player.bagSlots = ReadList(reader, () => ReadPlayerSlot(reader, version));
        player.handSlots = ReadList(reader, () => ReadPlayerSlot(reader, version));

        if (version >= 4)
        {
            player.craftingQueue = ReadList(reader, () => ReadPlayerCraftingQueueEntry(reader));
        }

        if (version >= 27)
        {
            player.nooseLeashedAnimalId = reader.ReadInt64();
        }

        if (version >= 28)
        {
            player.activeTorchItemId = reader.ReadInt32();
            player.activeTorchRemainingEnergy = reader.ReadSingle();
        }

        if (version >= 36)
        {
            player.mountedAnimalId = reader.ReadInt64();
        }

        return player;
    }

    private static void WritePlayerStats(BinaryWriter writer, PlayerStatSaveData stats)
    {
        stats ??= new PlayerStatSaveData();
        writer.Write(stats.miningPower);
        writer.Write(stats.loggingPower);
        writer.Write(stats.miningSpeed);
        writer.Write(stats.loggingSpeed);
        writer.Write(stats.harvestRange);
    }

    private static PlayerStatSaveData ReadPlayerStats(BinaryReader reader)
    {
        return new PlayerStatSaveData
        {
            miningPower = reader.ReadInt32(),
            loggingPower = reader.ReadInt32(),
            miningSpeed = reader.ReadSingle(),
            loggingSpeed = reader.ReadSingle(),
            harvestRange = reader.ReadSingle()
        };
    }

    private static void WritePlayerSlot(BinaryWriter writer, PlayerInventorySlotSaveState slot)
    {
        slot ??= new PlayerInventorySlotSaveState();
        writer.Write(slot.slotIndex);
        writer.Write(slot.itemId);
        writer.Write(slot.count);
        writer.Write(slot.capacity);
    }

    private static PlayerInventorySlotSaveState ReadPlayerSlot(BinaryReader reader, int version)
    {
        PlayerInventorySlotSaveState slot = new PlayerInventorySlotSaveState
        {
            slotIndex = reader.ReadInt32(),
            itemId = reader.ReadInt32(),
            count = reader.ReadInt32(),
            capacity = reader.ReadInt32()
        };

        if (version == 31)
        {
            reader.ReadInt32();
            reader.ReadSingle();
        }

        return slot;
    }

    private static void WritePlayerCraftingQueueEntry(BinaryWriter writer, PlayerCraftingQueueEntrySaveData entry)
    {
        entry ??= new PlayerCraftingQueueEntrySaveData();
        writer.Write(entry.itemId);
        writer.Write(entry.outputCount);
        writer.Write(entry.remainingOutputCount);
        writer.Write(entry.remainingTime);
        writer.Write(entry.duration);
        WriteList(writer, entry.refundIngredients, WritePlayerCraftingIngredient);
    }

    private static PlayerCraftingQueueEntrySaveData ReadPlayerCraftingQueueEntry(BinaryReader reader)
    {
        return new PlayerCraftingQueueEntrySaveData
        {
            itemId = reader.ReadInt32(),
            outputCount = reader.ReadInt32(),
            remainingOutputCount = reader.ReadInt32(),
            remainingTime = reader.ReadSingle(),
            duration = reader.ReadSingle(),
            refundIngredients = ReadList(reader, () => ReadPlayerCraftingIngredient(reader))
        };
    }

    private static void WritePlayerCraftingIngredient(BinaryWriter writer, PlayerCraftingIngredientSaveData ingredient)
    {
        ingredient ??= new PlayerCraftingIngredientSaveData();
        writer.Write(ingredient.itemId);
        writer.Write(ingredient.count);
    }

    private static PlayerCraftingIngredientSaveData ReadPlayerCraftingIngredient(BinaryReader reader)
    {
        return new PlayerCraftingIngredientSaveData
        {
            itemId = reader.ReadInt32(),
            count = reader.ReadInt32()
        };
    }

    private static void WriteVector2Int(BinaryWriter writer, Vector2Int value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
    }

    private static Vector2Int ReadVector2Int(BinaryReader reader)
    {
        return new Vector2Int(reader.ReadInt32(), reader.ReadInt32());
    }

    private static void WriteVector2(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
    }

    private static Vector2 ReadVector2(BinaryReader reader)
    {
        return new Vector2(reader.ReadSingle(), reader.ReadSingle());
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }

    private static Quaternion ReadQuaternion(BinaryReader reader)
    {
        return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static void WriteVector2IntList(BinaryWriter writer, List<Vector2Int> values)
    {
        WriteList(writer, values, WriteVector2Int);
    }

    private static List<Vector2Int> ReadVector2IntList(BinaryReader reader)
    {
        return ReadList(reader, () => ReadVector2Int(reader));
    }

    private static void WriteVector2List(BinaryWriter writer, List<Vector2> values)
    {
        WriteList(writer, values, WriteVector2);
    }

    private static List<Vector2> ReadVector2List(BinaryReader reader)
    {
        return ReadList(reader, () => ReadVector2(reader));
    }

    private static void WriteInputItemAreaList(BinaryWriter writer, List<InputOutputModule.PersistentInputItemAreaState> values)
    {
        WriteList(writer, values, (binaryWriter, value) =>
        {
            WriteVector2Int(binaryWriter, value.coordinate);
            binaryWriter.Write(value.itemId);
        });
    }

    private static List<InputOutputModule.PersistentInputItemAreaState> ReadInputItemAreaList(BinaryReader reader)
    {
        return ReadList(reader, () => new InputOutputModule.PersistentInputItemAreaState(
            ReadVector2Int(reader),
            reader.ReadInt32()));
    }

    private static void WriteIntList(BinaryWriter writer, List<int> values)
    {
        WriteList(writer, values, (binaryWriter, value) => binaryWriter.Write(value));
    }

    private static List<int> ReadIntList(BinaryReader reader)
    {
        return ReadList(reader, () => reader.ReadInt32());
    }

    private static void WriteStringList(BinaryWriter writer, List<string> values)
    {
        WriteList(writer, values, (binaryWriter, value) => binaryWriter.Write(value ?? string.Empty));
    }

    private static List<string> ReadStringList(BinaryReader reader)
    {
        return ReadList(reader, () => reader.ReadString());
    }

    private static void WriteUlongList(BinaryWriter writer, List<ulong> values)
    {
        WriteList(writer, values, (binaryWriter, value) => binaryWriter.Write(value));
    }

    private static List<ulong> ReadUlongList(BinaryReader reader)
    {
        return ReadList(reader, () => reader.ReadUInt64());
    }

    private static void WriteList<T>(BinaryWriter writer, List<T> values, Action<BinaryWriter, T> write)
    {
        if (values == null)
        {
            writer.Write(0);
            return;
        }

        writer.Write(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            write(writer, values[i]);
        }
    }

    private static List<T> ReadList<T>(BinaryReader reader, Func<T> read)
    {
        int count = reader.ReadInt32();
        if (count < 0)
        {
            count = 0;
        }
        else if (count > MaxSerializedListCount)
        {
            throw new InvalidDataException($"Serialized list count is too large: {count}");
        }

        List<T> values = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            values.Add(read());
        }

        return values;
    }
}
