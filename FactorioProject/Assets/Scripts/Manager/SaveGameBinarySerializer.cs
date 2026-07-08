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

        if (version <= 1)
        {
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
        }

        return terrain;
    }

    private static void WriteMap(BinaryWriter writer, MapSaveData map)
    {
        map ??= new MapSaveData();

        WriteList(writer, map.resources, WriteResourceEntry);
        WriteList(writer, map.floorObjects, WriteFloorObjectEntry);
        WriteList(writer, map.installations, WriteInstallationEntry);
        WriteList(writer, map.conveyorItems, WriteConveyorBlockEntry);
    }

    private static MapSaveData ReadMap(
        BinaryReader reader,
        int version,
        SaveReadCompatibilityMode compatibilityMode)
    {
        return new MapSaveData
        {
            resources = ReadList(reader, () => ReadResourceEntry(reader)),
            floorObjects = ReadList(reader, () => ReadFloorObjectEntry(reader)),
            installations = ReadList(reader, () => ReadInstallationEntry(reader, version, compatibilityMode)),
            conveyorItems = ReadList(reader, () => ReadConveyorBlockEntry(reader))
        };
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
    }

    private static ResourceSaveEntry ReadResourceEntry(BinaryReader reader)
    {
        return new ResourceSaveEntry
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
        WriteInputOutputState(writer, state.inputOutputState);
        WriteRobotArmState(writer, state.robotArmState);
        writer.Write(state.lastBackgroundSimulationTicks);
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
            inputOutputState = ReadInputOutputState(reader, version),
            robotArmState = version >= 3 ? ReadRobotArmState(reader) : null,
            lastBackgroundSimulationTicks = reader.ReadInt64()
        };

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
        player.bagSlots = ReadList(reader, () => ReadPlayerSlot(reader));
        player.handSlots = ReadList(reader, () => ReadPlayerSlot(reader));

        if (version >= 4)
        {
            player.craftingQueue = ReadList(reader, () => ReadPlayerCraftingQueueEntry(reader));
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

    private static PlayerInventorySlotSaveState ReadPlayerSlot(BinaryReader reader)
    {
        return new PlayerInventorySlotSaveState
        {
            slotIndex = reader.ReadInt32(),
            itemId = reader.ReadInt32(),
            count = reader.ReadInt32(),
            capacity = reader.ReadInt32()
        };
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
