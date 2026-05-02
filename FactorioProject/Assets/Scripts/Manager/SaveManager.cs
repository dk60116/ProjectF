using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

[DisallowMultipleComponent]
public class SaveManager : MonoBehaviour
{
    public const int SaveSlotCount = 10;

    private const int SaveVersion = 1;
    private const int DefaultSaveSlotIndex = 0;
    private const uint SaveMagic = 0x56534650; // PFSV
    private const string SaveFileNamePrefix = "save_slot_";
    private const string SaveFileExtension = ".pfsave";

    private readonly List<VirtualObjectRecord> virtualRecords = new List<VirtualObjectRecord>(1024);
    private readonly List<Block> loadedBlocks = new List<Block>(256);
    private readonly List<Block.RuntimeConveyorLaneSnapshot> conveyorSnapshots = new List<Block.RuntimeConveyorLaneSnapshot>(256);
    private readonly List<int> tempItemIds = new List<int>(128);
    private readonly HashSet<int> referencedItemIds = new HashSet<int>();
    private readonly List<ItemTableEntry> itemTable = new List<ItemTableEntry>(128);

    [SerializeField]
    private bool autoLoadOnStart = true;

    private bool hasInitialPlayerState;
    private Vector3 initialPlayerPosition;
    private Quaternion initialPlayerRotation = Quaternion.identity;
    private int initialBagLevel = 1;
    private bool hasInitialTerrainState;
    private int initialTerrainSeed;

    public int SlotCount => SaveSlotCount;
    public string SavePath => GetSavePath(DefaultSaveSlotIndex);
    public bool HasSaveFile => HasSaveFileAtSlot(DefaultSaveSlotIndex);

    private struct ItemTableEntry
    {
        public int legacyId;
        public string stableId;
        public string displayName;
    }

    private IEnumerator Start()
    {
        CaptureInitialRuntimeState();
        yield return null;

        if (autoLoadOnStart)
        {
            LoadSlot(DefaultSaveSlotIndex, true);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || !IsSaveShortcutPressed())
        {
            return;
        }

        SaveSlot(DefaultSaveSlotIndex);
    }

    public bool SaveSlot()
    {
        return SaveSlot(DefaultSaveSlotIndex);
    }

    public bool SaveSlot(int slotIndex)
    {
        try
        {
            int normalizedSlotIndex = NormalizeSlotIndex(slotIndex);
            string savePath = GetSavePath(normalizedSlotIndex);
            SaveStats stats = CaptureAndWriteSave(savePath);
            Debug.Log(
                $"SaveManager: Saved slot {ToDisplaySlotNumber(normalizedSlotIndex)} to '{savePath}'. " +
                $"items={stats.itemTableCount}, records={stats.virtualRecordCount}, conveyorLanes={stats.conveyorLaneCount}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"SaveManager: Failed to save slot {ToDisplaySlotNumber(slotIndex)}. {exception}");
            return false;
        }
    }

    public bool LoadSlot()
    {
        return LoadSlot(false);
    }

    public bool LoadSlot(bool autoLoad)
    {
        return LoadSlot(DefaultSaveSlotIndex, autoLoad);
    }

    public bool LoadSlot(int slotIndex)
    {
        return LoadSlot(slotIndex, false);
    }

    public bool LoadSlot(int slotIndex, bool autoLoad)
    {
        int normalizedSlotIndex = NormalizeSlotIndex(slotIndex);
        string savePath = GetSavePath(normalizedSlotIndex);
        if (!File.Exists(savePath))
        {
            ResetRuntimeState();
            if (!autoLoad)
            {
                Debug.Log(
                    $"SaveManager: Save slot {ToDisplaySlotNumber(normalizedSlotIndex)} is empty. Loaded initial runtime state.");
            }

            return true;
        }

        try
        {
            LoadedSaveData data = ReadSaveFile(savePath);
            RemapLoadedItemIds(data, GameManager.Instance != null ? GameManager.Instance.ItemManger : null);
            ApplyLoadedSave(data);
            Debug.Log(
                $"SaveManager: Loaded slot {ToDisplaySlotNumber(normalizedSlotIndex)} from '{savePath}'. " +
                $"records={data.virtualRecords.Count}, conveyorLanes={data.conveyorSnapshotCount}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"SaveManager: Failed to load slot {ToDisplaySlotNumber(normalizedSlotIndex)}. {exception}");
            return false;
        }
    }

    public bool ResetSlot()
    {
        return ResetSlot(DefaultSaveSlotIndex);
    }

    public bool ResetSlot(int slotIndex)
    {
        try
        {
            int normalizedSlotIndex = NormalizeSlotIndex(slotIndex);
            string savePath = GetSavePath(normalizedSlotIndex);
            string tempPath = savePath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            ResetRuntimeState();
            Debug.Log(
                $"SaveManager: Reset slot {ToDisplaySlotNumber(normalizedSlotIndex)} and cleared save file '{savePath}'.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"SaveManager: Failed to reset slot {ToDisplaySlotNumber(slotIndex)}. {exception}");
            return false;
        }
    }

    public string GetSavePath(int slotIndex)
    {
        return Path.Combine(Application.persistentDataPath, GetSaveFileName(slotIndex));
    }

    public bool HasSaveFileAtSlot(int slotIndex)
    {
        return File.Exists(GetSavePath(slotIndex));
    }

    public static int NormalizeSlotIndex(int slotIndex)
    {
        return Mathf.Clamp(slotIndex, 0, SaveSlotCount - 1);
    }

    private static string GetSaveFileName(int slotIndex)
    {
        return SaveFileNamePrefix + NormalizeSlotIndex(slotIndex) + SaveFileExtension;
    }

    private static int ToDisplaySlotNumber(int slotIndex)
    {
        return NormalizeSlotIndex(slotIndex) + 1;
    }

    private SaveStats CaptureAndWriteSave(string savePath)
    {
        GameManager gameManager = GameManager.Instance;
        TerrainGenerator terrainGenerator = TerrainGenerator.ResolveActive();
        if (terrainGenerator != null)
        {
            terrainGenerator.SaveLoadedBlockStatesForSnapshot();
        }

        CaptureVirtualRecords(gameManager);
        CaptureConveyorSnapshots(terrainGenerator);
        BuildReferencedItemIds(gameManager);
        BuildItemTable(gameManager != null ? gameManager.ItemManger : null);

        string directory = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = savePath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (FileStream fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (GZipStream gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionLevel.Fastest))
        using (BinaryWriter writer = new BinaryWriter(gzipStream))
        {
            WriteHeader(writer);
            WriteItemTable(writer);
            WritePlayerState(writer, ResolvePlayer());
            WriteTerrainState(writer, terrainGenerator);
            WriteVirtualRecords(writer);
            WriteConveyorSnapshots(writer);
        }

        if (File.Exists(savePath))
        {
            File.Replace(tempPath, savePath, null);
        }
        else
        {
            File.Move(tempPath, savePath);
        }

        return new SaveStats
        {
            itemTableCount = itemTable.Count,
            virtualRecordCount = virtualRecords.Count,
            conveyorLaneCount = conveyorSnapshots.Count
        };
    }

    private static bool IsSaveShortcutPressed()
    {
        return Input.GetKeyDown(KeyCode.S)
            && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
    }

    private void CaptureVirtualRecords(GameManager gameManager)
    {
        virtualRecords.Clear();
        VirtualObjectWorld world = gameManager != null ? gameManager.VirtualWorld : VirtualObjectWorld.Current;
        if (world != null)
        {
            world.CopyRecords(virtualRecords, true);
        }
    }

    private void CaptureConveyorSnapshots(TerrainGenerator terrainGenerator)
    {
        conveyorSnapshots.Clear();
        loadedBlocks.Clear();
        if (terrainGenerator == null)
        {
            return;
        }

        terrainGenerator.CopyLoadedBlocks(loadedBlocks);
        for (int i = 0; i < loadedBlocks.Count; i++)
        {
            Block block = loadedBlocks[i];
            if (block != null)
            {
                block.CopyRuntimeConveyorLaneSnapshots(conveyorSnapshots);
            }
        }
    }

    private void BuildReferencedItemIds(GameManager gameManager)
    {
        referencedItemIds.Clear();

        Player player = ResolvePlayer();
        if (player != null)
        {
            AddBagItemIds(player.GetHandBag());
            AddBagItemIds(player.GetBag());
        }

        for (int i = 0; i < virtualRecords.Count; i++)
        {
            AddVirtualRecordItemIds(virtualRecords[i]);
        }

        for (int i = 0; i < conveyorSnapshots.Count; i++)
        {
            AddItemId(conveyorSnapshots[i].ItemId);
        }
    }

    private void AddVirtualRecordItemIds(VirtualObjectRecord record)
    {
        if (record == null)
        {
            return;
        }

        AddItemId(record.itemId);

        tempItemIds.Clear();
        record.itemStack?.CopyTo(tempItemIds);
        for (int i = 0; i < tempItemIds.Count; i++)
        {
            AddItemId(tempItemIds[i]);
        }

        AddInstallationItemIds(record.installationState);
    }

    private void AddInstallationItemIds(BlockStateStore.InstallationSaveState state)
    {
        if (state == null)
        {
            return;
        }

        AddItemId(state.itemId);
        InputOutputModule.PersistentState inputOutputState = state.inputOutputState;
        if (inputOutputState == null)
        {
            return;
        }

        if (inputOutputState.inputItemAreas != null)
        {
            for (int i = 0; i < inputOutputState.inputItemAreas.Count; i++)
            {
                AddItemId(inputOutputState.inputItemAreas[i].itemId);
            }
        }

        AddItemId(inputOutputState.activeOutputItemId);
    }

    private void AddBagItemIds(PlayerBag bag)
    {
        if (bag == null)
        {
            return;
        }

        bag.RefreshExternalStackCounts(false);
        for (int i = 0; i < bag.SlotCount; i++)
        {
            if (bag.GetSlotCount(i) > 0)
            {
                AddItemId(bag.GetSlotItemId(i));
            }
        }
    }

    private void AddItemId(int itemId)
    {
        if (itemId >= 0)
        {
            referencedItemIds.Add(itemId);
        }
    }

    private void BuildItemTable(ItemManager itemManager)
    {
        itemTable.Clear();
        if (referencedItemIds.Count <= 0)
        {
            return;
        }

        List<int> sortedIds = new List<int>(referencedItemIds);
        sortedIds.Sort();

        for (int i = 0; i < sortedIds.Count; i++)
        {
            int itemId = sortedIds[i];
            ItemDefinition definition = FindItemDefinition(itemManager, itemId);
            string displayName = ResolveItemDisplayName(itemManager, definition, itemId);
            itemTable.Add(new ItemTableEntry
            {
                legacyId = itemId,
                stableId = ResolveItemStableId(definition, itemId, displayName),
                displayName = displayName
            });
        }
    }

    private static ItemDefinition FindItemDefinition(ItemManager itemManager, int itemId)
    {
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private static string ResolveItemDisplayName(ItemManager itemManager, ItemDefinition definition, int itemId)
    {
        if (definition != null)
        {
            if (!string.IsNullOrWhiteSpace(definition.itemName))
            {
                return definition.itemName;
            }

            if (!string.IsNullOrWhiteSpace(definition.name))
            {
                return definition.name;
            }
        }

        if (itemManager != null && itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            if (!string.IsNullOrWhiteSpace(itemSet.name))
            {
                return itemSet.name;
            }
        }

        return $"Item_{itemId}";
    }

    private static string ResolveItemStableId(ItemDefinition definition, int itemId, string displayName)
    {
        if (definition != null && !string.IsNullOrWhiteSpace(definition.stableId))
        {
            return definition.stableId;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return $"legacy:{itemId}:{displayName}";
        }

        return $"legacy:{itemId}";
    }

    private static void WriteHeader(BinaryWriter writer)
    {
        writer.Write(SaveMagic);
        writer.Write(SaveVersion);
        writer.Write(DateTime.UtcNow.Ticks);
    }

    private void WriteItemTable(BinaryWriter writer)
    {
        writer.Write(itemTable.Count);
        for (int i = 0; i < itemTable.Count; i++)
        {
            ItemTableEntry entry = itemTable[i];
            writer.Write(entry.legacyId);
            writer.Write(entry.stableId ?? string.Empty);
            writer.Write(entry.displayName ?? string.Empty);
        }
    }

    private static void WritePlayerState(BinaryWriter writer, Player player)
    {
        writer.Write(player != null);
        if (player == null)
        {
            return;
        }

        WriteVector3(writer, player.transform.position);
        WriteQuaternion(writer, player.transform.rotation);
        writer.Write(player.BagLevel);
        WriteBagState(writer, player.GetHandBag());
        WriteBagState(writer, player.GetBag());
    }

    private static void WriteBagState(BinaryWriter writer, PlayerBag bag)
    {
        writer.Write(bag != null);
        if (bag == null)
        {
            return;
        }

        bag.RefreshExternalStackCounts(false);
        writer.Write(bag.SlotCount);
        for (int i = 0; i < bag.SlotCount; i++)
        {
            writer.Write(bag.GetSlotItemId(i));
            writer.Write(bag.GetSlotCount(i));
            writer.Write(bag.GetSlotMaxCount(i));
        }
    }

    private static void WriteTerrainState(BinaryWriter writer, TerrainGenerator terrainGenerator)
    {
        writer.Write(terrainGenerator != null);
        if (terrainGenerator == null)
        {
            return;
        }

        writer.Write(terrainGenerator.CurrentSeed);
        writer.Write(terrainGenerator.ChunkSize);
        writer.Write(terrainGenerator.LoadRadius);
        writer.Write(terrainGenerator.UnloadRadius);
    }

    private void WriteVirtualRecords(BinaryWriter writer)
    {
        writer.Write(virtualRecords.Count);
        for (int i = 0; i < virtualRecords.Count; i++)
        {
            WriteVirtualRecord(writer, virtualRecords[i]);
        }
    }

    private void WriteVirtualRecord(BinaryWriter writer, VirtualObjectRecord record)
    {
        writer.Write(record != null);
        if (record == null)
        {
            return;
        }

        writer.Write(record.id.Value);
        writer.Write((byte)record.kind);
        writer.Write((byte)record.residency);
        writer.Write(record.itemId);
        writer.Write(record.count);
        WriteVector2Int(writer, record.anchorCoordinate);
        WriteVector3(writer, record.worldPosition);
        WriteQuaternion(writer, record.worldRotation);
        writer.Write(record.quarterTurns);
        writer.Write(record.sequence);
        writer.Write(record.liveInstanceId);
        WriteVector2IntList(writer, record.occupiedCoordinates);
        WriteItemStackState(writer, record.itemStack);
        WriteResourceState(writer, record.kind == VirtualObjectKind.Resource, record.resourceState);
        WriteInstallationState(writer, record.installationState);
    }

    private static void WriteItemStackState(BinaryWriter writer, VirtualItemStackState itemStack)
    {
        writer.Write(itemStack != null);
        if (itemStack == null)
        {
            return;
        }

        List<int> itemIds = new List<int>();
        itemStack.CopyTo(itemIds);
        WriteIntListRle(writer, itemIds);
    }

    private static void WriteResourceState(BinaryWriter writer, bool hasResourceState, Resource.ResourceSaveState state)
    {
        writer.Write(hasResourceState);
        if (!hasResourceState)
        {
            return;
        }

        writer.Write(state.resourceCount);
        writer.Write(state.maxGauge);
        writer.Write(state.currentGauge);
        writer.Write(state.initialResourceCount);
        writer.Write(state.hasBodyYawStep);
        writer.Write(state.bodyYawStep);
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
        writer.Write(state.lastBackgroundSimulationTicks);
        writer.Write(state.boxIsOpen.HasValue);
        if (state.boxIsOpen.HasValue)
        {
            writer.Write(state.boxIsOpen.Value);
        }

        writer.Write(state.itemFilterMaskInitialized);
        WriteUlongList(writer, state.itemFilterMaskWords);
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

    private static void WriteInputItemAreaList(BinaryWriter writer, IReadOnlyList<InputOutputModule.PersistentInputItemAreaState> inputItemAreas)
    {
        int count = inputItemAreas != null ? inputItemAreas.Count : 0;
        writer.Write(count);
        for (int i = 0; i < count; i++)
        {
            WriteVector2Int(writer, inputItemAreas[i].coordinate);
            writer.Write(inputItemAreas[i].itemId);
        }
    }

    private void WriteConveyorSnapshots(BinaryWriter writer)
    {
        writer.Write(conveyorSnapshots.Count);
        for (int i = 0; i < conveyorSnapshots.Count; i++)
        {
            WriteConveyorSnapshot(writer, conveyorSnapshots[i]);
        }
    }

    private static void WriteConveyorSnapshot(BinaryWriter writer, Block.RuntimeConveyorLaneSnapshot snapshot)
    {
        WriteVector2Int(writer, snapshot.BlockCoordinate);
        writer.Write(snapshot.LaneIndex);
        writer.Write(snapshot.LaneCount);
        writer.Write(snapshot.ItemId);
        WriteVector3(writer, snapshot.VisualWorldPosition);
        writer.Write(snapshot.IsSettled);
        writer.Write(snapshot.IsReadyToMove);
        writer.Write(snapshot.MovementHoldRemainingSeconds);
        writer.Write(snapshot.HasMotion);
        writer.Write(snapshot.MotionUsesCorner);
        writer.Write(snapshot.MotionSourceLaneIndex);
        writer.Write(snapshot.MotionDestinationLaneIndex);
        writer.Write(snapshot.MotionProgress);
        writer.Write(snapshot.MotionPathLength);
        writer.Write(snapshot.MotionDurationPathLength);
        WriteVector3(writer, snapshot.MotionStartWorldPosition);
        writer.Write(snapshot.MotionHasViaWorldPosition);
        WriteVector3(writer, snapshot.MotionViaWorldPosition);
        writer.Write(snapshot.HasCornerContinuation);
        WriteVector2Int(writer, snapshot.ContinuationBlockCoordinate);
        writer.Write(snapshot.ContinuationSourceLaneIndex);
        writer.Write(snapshot.ContinuationDestinationLaneIndex);
        WriteVector3(writer, snapshot.ContinuationStartWorldPosition);
        writer.Write(snapshot.ContinuationStartProgress);
        writer.Write(snapshot.ContinuationPathLength);
        writer.Write(snapshot.ContinuationDurationPathLength);
    }

    private static void WriteVector2IntList(BinaryWriter writer, IReadOnlyList<Vector2Int> values)
    {
        int count = values != null ? values.Count : 0;
        writer.Write(count);
        for (int i = 0; i < count; i++)
        {
            WriteVector2Int(writer, values[i]);
        }
    }

    private static void WriteUlongList(BinaryWriter writer, IReadOnlyList<ulong> values)
    {
        int count = values != null ? values.Count : 0;
        writer.Write(count);
        for (int i = 0; i < count; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static void WriteIntListRle(BinaryWriter writer, IReadOnlyList<int> values)
    {
        int count = values != null ? values.Count : 0;
        writer.Write(count);
        if (count <= 0)
        {
            writer.Write(0);
            return;
        }

        int runCount = 1;
        int previous = values[0];
        for (int i = 1; i < count; i++)
        {
            if (values[i] != previous)
            {
                runCount++;
                previous = values[i];
            }
        }

        writer.Write(runCount);
        int current = values[0];
        int currentCount = 1;
        for (int i = 1; i < count; i++)
        {
            int value = values[i];
            if (value == current)
            {
                currentCount++;
                continue;
            }

            writer.Write(current);
            writer.Write(currentCount);
            current = value;
            currentCount = 1;
        }

        writer.Write(current);
        writer.Write(currentCount);
    }

    private static void WriteVector2Int(BinaryWriter writer, Vector2Int value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }

    private void CaptureInitialRuntimeState()
    {
        CaptureInitialPlayerState();
        CaptureInitialTerrainState();
    }

    private void CaptureInitialPlayerState()
    {
        Player player = ResolvePlayer();
        if (player == null)
        {
            return;
        }

        initialPlayerPosition = player.transform.position;
        initialPlayerRotation = player.transform.rotation;
        initialBagLevel = player.BagLevel;
        hasInitialPlayerState = true;
    }

    private void CaptureInitialTerrainState()
    {
        TerrainGenerator terrainGenerator = TerrainGenerator.ResolveActive();
        if (terrainGenerator == null)
        {
            return;
        }

        initialTerrainSeed = terrainGenerator.CurrentSeed;
        hasInitialTerrainState = true;
    }

    private static LoadedSaveData ReadSaveFile(string savePath)
    {
        using (FileStream fileStream = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
        using (BinaryReader reader = new BinaryReader(gzipStream))
        {
            uint magic = reader.ReadUInt32();
            if (magic != SaveMagic)
            {
                throw new InvalidDataException("save magic mismatch");
            }

            int version = reader.ReadInt32();
            if (version <= 0 || version > SaveVersion)
            {
                throw new InvalidDataException($"unsupported save version {version}");
            }

            LoadedSaveData data = new LoadedSaveData
            {
                version = version,
                savedUtcTicks = reader.ReadInt64()
            };

            ReadItemTable(reader, data.itemTable);
            data.player = ReadPlayerState(reader);
            data.terrain = ReadTerrainState(reader);
            ReadVirtualRecords(reader, data.virtualRecords);
            data.conveyorSnapshotCount = ReadConveyorSnapshots(reader);
            return data;
        }
    }

    private static void ReadItemTable(BinaryReader reader, List<ItemTableEntry> results)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            results.Add(new ItemTableEntry
            {
                legacyId = reader.ReadInt32(),
                stableId = reader.ReadString(),
                displayName = reader.ReadString()
            });
        }
    }

    private static LoadedPlayerState ReadPlayerState(BinaryReader reader)
    {
        LoadedPlayerState state = new LoadedPlayerState
        {
            hasPlayer = reader.ReadBoolean()
        };

        if (!state.hasPlayer)
        {
            return state;
        }

        state.position = ReadVector3(reader);
        state.rotation = ReadQuaternion(reader);
        state.bagLevel = reader.ReadInt32();
        state.handBag = ReadBagState(reader);
        state.bag = ReadBagState(reader);
        return state;
    }

    private static LoadedBagState ReadBagState(BinaryReader reader)
    {
        LoadedBagState state = new LoadedBagState
        {
            hasBag = reader.ReadBoolean()
        };

        if (!state.hasBag)
        {
            return state;
        }

        int slotCount = reader.ReadInt32();
        for (int i = 0; i < slotCount; i++)
        {
            state.slots.Add(new LoadedBagSlot
            {
                itemId = reader.ReadInt32(),
                count = reader.ReadInt32(),
                maxCount = reader.ReadInt32()
            });
        }

        return state;
    }

    private static LoadedTerrainState ReadTerrainState(BinaryReader reader)
    {
        LoadedTerrainState state = new LoadedTerrainState
        {
            hasTerrain = reader.ReadBoolean()
        };

        if (!state.hasTerrain)
        {
            return state;
        }

        state.seed = reader.ReadInt32();
        state.chunkSize = reader.ReadInt32();
        state.loadRadius = reader.ReadInt32();
        state.unloadRadius = reader.ReadInt32();
        return state;
    }

    private static void ReadVirtualRecords(BinaryReader reader, List<LoadedVirtualRecord> results)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            LoadedVirtualRecord record = ReadVirtualRecord(reader);
            if (record != null && record.isValid)
            {
                results.Add(record);
            }
        }
    }

    private static LoadedVirtualRecord ReadVirtualRecord(BinaryReader reader)
    {
        LoadedVirtualRecord record = new LoadedVirtualRecord
        {
            isValid = reader.ReadBoolean()
        };

        if (!record.isValid)
        {
            return record;
        }

        record.id = reader.ReadInt32();
        record.kind = (VirtualObjectKind)reader.ReadByte();
        record.residency = (VirtualObjectResidency)reader.ReadByte();
        record.itemId = reader.ReadInt32();
        record.count = reader.ReadInt32();
        record.anchorCoordinate = ReadVector2Int(reader);
        record.worldPosition = ReadVector3(reader);
        record.worldRotation = ReadQuaternion(reader);
        record.quarterTurns = reader.ReadInt32();
        record.sequence = reader.ReadInt64();
        record.liveInstanceId = reader.ReadInt32();
        record.occupiedCoordinates = ReadVector2IntList(reader);
        record.itemStack = ReadItemStackState(reader);
        record.hasResourceState = reader.ReadBoolean();
        if (record.hasResourceState)
        {
            record.resourceState = ReadResourceState(reader);
        }

        record.installationState = ReadInstallationState(reader);
        return record;
    }

    private static List<int> ReadItemStackState(BinaryReader reader)
    {
        bool hasItemStack = reader.ReadBoolean();
        return hasItemStack ? ReadIntListRle(reader) : null;
    }

    private static Resource.ResourceSaveState ReadResourceState(BinaryReader reader)
    {
        return new Resource.ResourceSaveState
        {
            resourceCount = reader.ReadInt32(),
            maxGauge = reader.ReadInt32(),
            currentGauge = reader.ReadInt32(),
            initialResourceCount = reader.ReadInt32(),
            hasBodyYawStep = reader.ReadBoolean(),
            bodyYawStep = reader.ReadInt32()
        };
    }

    private static BlockStateStore.InstallationSaveState ReadInstallationState(BinaryReader reader)
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
            inputOutputState = ReadInputOutputState(reader),
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

    private static InputOutputModule.PersistentState ReadInputOutputState(BinaryReader reader)
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

    private static List<InputOutputModule.PersistentInputItemAreaState> ReadInputItemAreaList(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        List<InputOutputModule.PersistentInputItemAreaState> values =
            new List<InputOutputModule.PersistentInputItemAreaState>(count);

        for (int i = 0; i < count; i++)
        {
            Vector2Int coordinate = ReadVector2Int(reader);
            int itemId = reader.ReadInt32();
            values.Add(new InputOutputModule.PersistentInputItemAreaState(coordinate, itemId));
        }

        return values;
    }

    private static int ReadConveyorSnapshots(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ReadVector2Int(reader);
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            ReadVector3(reader);
            reader.ReadBoolean();
            reader.ReadBoolean();
            reader.ReadSingle();
            reader.ReadBoolean();
            reader.ReadBoolean();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
            ReadVector3(reader);
            reader.ReadBoolean();
            ReadVector3(reader);
            reader.ReadBoolean();
            ReadVector2Int(reader);
            reader.ReadInt32();
            reader.ReadInt32();
            ReadVector3(reader);
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadSingle();
        }

        return count;
    }

    private static List<Vector2Int> ReadVector2IntList(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        List<Vector2Int> values = new List<Vector2Int>(count);
        for (int i = 0; i < count; i++)
        {
            values.Add(ReadVector2Int(reader));
        }

        return values;
    }

    private static List<ulong> ReadUlongList(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        List<ulong> values = new List<ulong>(count);
        for (int i = 0; i < count; i++)
        {
            values.Add(reader.ReadUInt64());
        }

        return values;
    }

    private static List<int> ReadIntListRle(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        int runCount = reader.ReadInt32();
        List<int> values = new List<int>(Mathf.Max(0, count));
        for (int runIndex = 0; runIndex < runCount; runIndex++)
        {
            int value = reader.ReadInt32();
            int runLength = reader.ReadInt32();
            for (int i = 0; i < runLength; i++)
            {
                values.Add(value);
            }
        }

        if (values.Count > count)
        {
            values.RemoveRange(count, values.Count - count);
        }

        return values;
    }

    private static Vector2Int ReadVector2Int(BinaryReader reader)
    {
        return new Vector2Int(reader.ReadInt32(), reader.ReadInt32());
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static Quaternion ReadQuaternion(BinaryReader reader)
    {
        return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static void RemapLoadedItemIds(LoadedSaveData data, ItemManager itemManager)
    {
        if (data == null || itemManager == null)
        {
            return;
        }

        Dictionary<int, int> itemIdRemap = BuildItemIdRemap(data.itemTable, itemManager);
        RemapBagState(data.player?.handBag, itemIdRemap);
        RemapBagState(data.player?.bag, itemIdRemap);

        for (int i = 0; i < data.virtualRecords.Count; i++)
        {
            LoadedVirtualRecord record = data.virtualRecords[i];
            record.itemId = RemapItemId(record.itemId, itemIdRemap);
            RemapItemList(record.itemStack, itemIdRemap);
            RemapInstallationState(record.installationState, itemIdRemap);
        }
    }

    private static Dictionary<int, int> BuildItemIdRemap(List<ItemTableEntry> table, ItemManager itemManager)
    {
        Dictionary<int, int> remap = new Dictionary<int, int>();
        if (table == null || itemManager == null)
        {
            return remap;
        }

        for (int i = 0; i < table.Count; i++)
        {
            ItemTableEntry entry = table[i];
            int mappedId = entry.legacyId;
            if (!string.IsNullOrWhiteSpace(entry.stableId)
                && !entry.stableId.StartsWith("legacy:", StringComparison.OrdinalIgnoreCase))
            {
                ItemDefinition definition = FindItemDefinitionByStableId(itemManager, entry.stableId);
                if (definition != null)
                {
                    mappedId = definition.id;
                }
            }

            remap[entry.legacyId] = mappedId;
        }

        return remap;
    }

    private static ItemDefinition FindItemDefinitionByStableId(ItemManager itemManager, string stableId)
    {
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null || string.IsNullOrWhiteSpace(stableId))
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && string.Equals(definition.stableId, stableId, StringComparison.Ordinal))
            {
                return definition;
            }
        }

        return null;
    }

    private static void RemapBagState(LoadedBagState bagState, Dictionary<int, int> itemIdRemap)
    {
        if (bagState == null)
        {
            return;
        }

        for (int i = 0; i < bagState.slots.Count; i++)
        {
            LoadedBagSlot slot = bagState.slots[i];
            slot.itemId = RemapItemId(slot.itemId, itemIdRemap);
            bagState.slots[i] = slot;
        }
    }

    private static void RemapInstallationState(
        BlockStateStore.InstallationSaveState state,
        Dictionary<int, int> itemIdRemap)
    {
        if (state == null)
        {
            return;
        }

        state.itemId = RemapItemId(state.itemId, itemIdRemap);
        InputOutputModule.PersistentState inputOutputState = state.inputOutputState;
        if (inputOutputState == null)
        {
            return;
        }

        if (inputOutputState.inputItemAreas != null)
        {
            for (int i = 0; i < inputOutputState.inputItemAreas.Count; i++)
            {
                InputOutputModule.PersistentInputItemAreaState area = inputOutputState.inputItemAreas[i];
                area.itemId = RemapItemId(area.itemId, itemIdRemap);
                inputOutputState.inputItemAreas[i] = area;
            }
        }

        inputOutputState.activeOutputItemId = RemapItemId(inputOutputState.activeOutputItemId, itemIdRemap);
    }

    private static void RemapItemList(List<int> itemIds, Dictionary<int, int> itemIdRemap)
    {
        if (itemIds == null)
        {
            return;
        }

        for (int i = 0; i < itemIds.Count; i++)
        {
            itemIds[i] = RemapItemId(itemIds[i], itemIdRemap);
        }
    }

    private static int RemapItemId(int itemId, Dictionary<int, int> itemIdRemap)
    {
        return itemId >= 0 && itemIdRemap != null && itemIdRemap.TryGetValue(itemId, out int mappedId)
            ? mappedId
            : itemId;
    }

    private void ApplyLoadedSave(LoadedSaveData data)
    {
        if (data == null)
        {
            return;
        }

        TerrainGenerator terrainGenerator = TerrainGenerator.ResolveActive();
        BlockStateStore stateStore = null;
        if (terrainGenerator != null)
        {
            terrainGenerator.PrepareForSaveLoadApply();
            stateStore = terrainGenerator.SaveStateStore;
        }
        else
        {
            GameManager.Instance?.VirtualWorld?.Clear();
        }

        ApplyVirtualRecords(stateStore, data.virtualRecords);
        ApplyPlayerState(data.player);

        if (terrainGenerator != null)
        {
            int loadedSeed = data.terrain != null && data.terrain.hasTerrain
                ? data.terrain.seed
                : terrainGenerator.CurrentSeed;
            terrainGenerator.RebuildAfterSaveLoad(loadedSeed);
            ApplyPlayerTransformOnly(data.player);
        }
    }

    private static void ApplyVirtualRecords(BlockStateStore stateStore, List<LoadedVirtualRecord> records)
    {
        if (stateStore == null || records == null)
        {
            return;
        }

        for (int i = 0; i < records.Count; i++)
        {
            LoadedVirtualRecord record = records[i];
            if (record == null || !record.isValid)
            {
                continue;
            }

            switch (record.kind)
            {
                case VirtualObjectKind.ItemStack:
                    stateStore.SetFloorObjects(record.anchorCoordinate, record.itemStack);
                    break;
                case VirtualObjectKind.Resource:
                    if (record.hasResourceState)
                    {
                        stateStore.SetResourceState(record.anchorCoordinate, record.itemId, record.resourceState);
                    }

                    break;
                case VirtualObjectKind.Installation:
                    if (record.installationState != null)
                    {
                        stateStore.UpdateInstallationState(record.installationState);
                    }

                    break;
                default:
                    if (record.itemStack != null && record.itemStack.Count > 0)
                    {
                        stateStore.SetFloorObjects(record.anchorCoordinate, record.itemStack);
                    }

                    break;
            }
        }
    }

    private void ApplyPlayerState(LoadedPlayerState state)
    {
        if (state == null || !state.hasPlayer)
        {
            return;
        }

        Player player = ResolvePlayer();
        if (player == null)
        {
            return;
        }

        ApplyPlayerTransform(player, state.position, state.rotation);
        player.ClearInventoryBags(false);
        player.SetBagLevel(Mathf.Max(1, state.bagLevel));
        ApplyBagState(player.GetHandBag(), state.handBag);
        ApplyBagState(player.GetBag(), state.bag);
        player.StopImmediateActions();
        player.RefreshInventoryPresentation();
    }

    private static void ApplyBagState(PlayerBag bag, LoadedBagState state)
    {
        if (bag == null)
        {
            return;
        }

        bag.ClearAllSlots(false);
        if (state == null || !state.hasBag)
        {
            bag.ForceNotifyChanged();
            return;
        }

        int slotCount = Mathf.Min(bag.SlotCount, state.slots.Count);
        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            LoadedBagSlot slot = state.slots[slotIndex];
            if (slot.itemId < 0 || slot.count <= 0)
            {
                continue;
            }

            int maxCount = bag.GetSlotMaxCount(slotIndex);
            int targetCount = Mathf.Clamp(slot.count, 0, maxCount);
            for (int i = 0; i < targetCount; i++)
            {
                if (!bag.TryAddObjectToSlotOnly(slotIndex, slot.itemId, out _))
                {
                    break;
                }
            }
        }

        bag.ForceNotifyChanged();
    }

    private void ResetRuntimeState()
    {
        Player player = ResolvePlayer();
        if (player != null)
        {
            if (hasInitialPlayerState)
            {
                ApplyPlayerTransform(player, initialPlayerPosition, initialPlayerRotation);
                player.SetBagLevel(initialBagLevel);
            }
            else
            {
                player.SetBagLevel(1);
            }

            ClearPlayerInventory(player);
            player.StopImmediateActions();
            player.RefreshInventoryPresentation();
        }

        TerrainGenerator terrainGenerator = TerrainGenerator.ResolveActive();
        if (terrainGenerator != null)
        {
            if (hasInitialTerrainState)
            {
                terrainGenerator.PrepareForSaveLoadApply();
                terrainGenerator.RebuildAfterSaveLoad(initialTerrainSeed);
            }
            else
            {
                terrainGenerator.Generate();
            }

            if (hasInitialPlayerState)
            {
                ApplyPlayerTransform(player, initialPlayerPosition, initialPlayerRotation);
            }

            return;
        }

        GameManager.Instance?.VirtualWorld?.Clear();
    }

    private static void ClearPlayerInventory(Player player)
    {
        if (player == null)
        {
            return;
        }

        player.ClearInventoryBags(false);
    }

    private static void ApplyPlayerTransformOnly(LoadedPlayerState state)
    {
        if (state == null || !state.hasPlayer)
        {
            return;
        }

        ApplyPlayerTransform(ResolvePlayer(), state.position, state.rotation);
    }

    private static void ApplyPlayerTransform(Player player, Vector3 position, Quaternion rotation)
    {
        if (player == null)
        {
            return;
        }

        Rigidbody rigidbody = player.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.position = position;
            rigidbody.rotation = rotation;
        }

        player.transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
    }

    private static Player ResolvePlayer()
    {
        GameManager gameManager = GameManager.Instance;
        return gameManager != null && gameManager.Player != null
            ? gameManager.Player
            : FindObjectOfType<Player>();
    }

    private sealed class LoadedSaveData
    {
        public int version;
        public long savedUtcTicks;
        public readonly List<ItemTableEntry> itemTable = new List<ItemTableEntry>();
        public LoadedPlayerState player;
        public LoadedTerrainState terrain;
        public readonly List<LoadedVirtualRecord> virtualRecords = new List<LoadedVirtualRecord>();
        public int conveyorSnapshotCount;
    }

    private sealed class LoadedPlayerState
    {
        public bool hasPlayer;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public int bagLevel = 1;
        public LoadedBagState handBag;
        public LoadedBagState bag;
    }

    private sealed class LoadedBagState
    {
        public bool hasBag;
        public readonly List<LoadedBagSlot> slots = new List<LoadedBagSlot>();
    }

    private struct LoadedBagSlot
    {
        public int itemId;
        public int count;
        public int maxCount;
    }

    private sealed class LoadedTerrainState
    {
        public bool hasTerrain;
        public int seed;
        public int chunkSize;
        public int loadRadius;
        public int unloadRadius;
    }

    private sealed class LoadedVirtualRecord
    {
        public bool isValid;
        public int id;
        public VirtualObjectKind kind;
        public VirtualObjectResidency residency;
        public int itemId = -1;
        public int count;
        public Vector2Int anchorCoordinate;
        public Vector3 worldPosition;
        public Quaternion worldRotation = Quaternion.identity;
        public int quarterTurns;
        public long sequence;
        public int liveInstanceId;
        public List<Vector2Int> occupiedCoordinates;
        public List<int> itemStack;
        public bool hasResourceState;
        public Resource.ResourceSaveState resourceState;
        public BlockStateStore.InstallationSaveState installationState;
    }

    private struct SaveStats
    {
        public int itemTableCount;
        public int virtualRecordCount;
        public int conveyorLaneCount;
    }
}
