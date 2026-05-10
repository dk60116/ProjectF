using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

public static class SaveGameBinarySerializer
{
    private const string Magic = "PF_SAVE";

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

        using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
        using (BinaryReader reader = new BinaryReader(gzipStream, Encoding.UTF8))
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

            return ReadSaveGameData(reader, version);
        }
    }

    private static void WriteSaveGameData(BinaryWriter writer, SaveGameData data)
    {
        writer.Write(data.version);
        writer.Write(data.savedAtUtcTicks);
        WriteTerrain(writer, data.terrain);
        WriteMap(writer, data.map);
        WritePlayer(writer, data.player);
    }

    private static SaveGameData ReadSaveGameData(BinaryReader reader, int fileVersion)
    {
        SaveGameData data = new SaveGameData
        {
            version = reader.ReadInt32(),
            savedAtUtcTicks = reader.ReadInt64(),
            terrain = ReadTerrain(reader, fileVersion),
            map = ReadMap(reader, fileVersion),
            player = ReadPlayer(reader, fileVersion)
        };

        return data;
    }

    private static void WriteTerrain(BinaryWriter writer, TerrainSaveData terrain)
    {
        terrain ??= new TerrainSaveData();
        writer.Write(terrain.seed);
    }

    private static TerrainSaveData ReadTerrain(BinaryReader reader, int version)
    {
        TerrainSaveData terrain = new TerrainSaveData
        {
            seed = reader.ReadInt32()
        };

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

    private static MapSaveData ReadMap(BinaryReader reader, int version)
    {
        return new MapSaveData
        {
            resources = ReadList(reader, () => ReadResourceEntry(reader)),
            floorObjects = ReadList(reader, () => ReadFloorObjectEntry(reader)),
            installations = ReadList(reader, () => ReadInstallationEntry(reader, version)),
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

    private static InstallationSaveEntry ReadInstallationEntry(BinaryReader reader, int version)
    {
        return new InstallationSaveEntry
        {
            state = ReadInstallationState(reader, version)
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
        writer.Write(state.quarterTurns);
        writer.Write(state.placementSequence);
        writer.Write(state.conveyorVariantKind);
        WriteVector2IntList(writer, state.occupiedCoordinates);
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
    }

    private static BlockStateStore.InstallationSaveState ReadInstallationState(BinaryReader reader, int version)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        BlockStateStore.InstallationSaveState state = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = ReadVector2Int(reader),
            itemId = reader.ReadInt32(),
            quarterTurns = reader.ReadInt32(),
            placementSequence = reader.ReadInt64(),
            conveyorVariantKind = reader.ReadInt32(),
            occupiedCoordinates = ReadVector2IntList(reader),
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
    }

    private static InputOutputModule.PersistentState ReadInputOutputState(BinaryReader reader, int version)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        return new InputOutputModule.PersistentState
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
            bagLevel = reader.ReadInt32(),
            stats = ReadPlayerStats(reader),
            bagSlots = ReadList(reader, () => ReadPlayerSlot(reader)),
            handSlots = ReadList(reader, () => ReadPlayerSlot(reader))
        };

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
        int count = Mathf.Max(0, reader.ReadInt32());
        List<T> values = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            values.Add(read());
        }

        return values;
    }
}
