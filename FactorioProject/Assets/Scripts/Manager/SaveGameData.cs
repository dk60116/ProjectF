using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SaveGameData
{
    public const int CurrentVersion = 34;

    public int version = CurrentVersion;
    public long savedAtUtcTicks;
    public List<SaveItemCatalogEntry> itemCatalog = new List<SaveItemCatalogEntry>();
    public TerrainSaveData terrain = new TerrainSaveData();
    public WorldTimeSaveData worldTime = new WorldTimeSaveData();
    public MapSaveData map = new MapSaveData();
    public PlayerSaveData player = new PlayerSaveData();
}

[Serializable]
public sealed class SaveItemCatalogEntry
{
    public int itemId = -1;
    public string itemName = string.Empty;
}

[Serializable]
public sealed class TerrainSaveData
{
    public int seed;
    public int mapSize;
}

[Serializable]
public sealed class WorldTimeSaveData
{
    public bool hasTime = true;
    public int dayIndex = 1;
    public double secondsOfDay = WorldTimeService.DefaultStartHour * WorldTimeService.GameSecondsPerHour;
}

[Serializable]
public sealed class MapSaveData
{
    public List<ResourceSaveEntry> resources = new List<ResourceSaveEntry>();
    public List<FloorObjectSaveEntry> floorObjects = new List<FloorObjectSaveEntry>();
    public List<InstallationSaveEntry> installations = new List<InstallationSaveEntry>();
    public List<ConveyorItemBlockSaveEntry> conveyorItems = new List<ConveyorItemBlockSaveEntry>();
    public List<AnimalSaveEntry> animals = new List<AnimalSaveEntry>();
}

[Serializable]
public sealed class AnimalSaveEntry
{
    public long deterministicId;
    public int definitionId = -1;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public float age = 10f;
    public float baseScale = 1f;
    public bool removed;
    public long herdId;
    public Vector3 herdCenter;
    public float herdRadius = AnimalAISettings.DefaultHerdAreaRadius;
    public int behaviorState;
    public float behaviorTimeRemaining;
    public Vector3 targetPosition;
    public bool hasTarget;
    public bool movingToActivity;
    public int randomState;
    public bool hasHealth;
    public float currentHealth;
    public bool corpseLootInitialized;
    public List<int> corpseRemainingItemIds = new List<int>();
}

[Serializable]
public sealed class ResourceSaveEntry
{
    public Vector2Int coordinate;
    public int itemId = -1;
    public Resource.ResourceSaveState state;
}

[Serializable]
public sealed class FloorObjectSaveEntry
{
    public Vector2Int coordinate;
    public List<int> itemIds = new List<int>();
}

[Serializable]
public sealed class InstallationSaveEntry
{
    public BlockStateStore.InstallationSaveState state;
}

[Serializable]
public sealed class ConveyorItemBlockSaveEntry
{
    public Vector2Int coordinate;
    public List<ConveyorItemLaneSaveState> lanes = new List<ConveyorItemLaneSaveState>();
}

[Serializable]
public sealed class ConveyorItemLaneSaveState
{
    public int laneIndex = -1;
    public int itemId = -1;
    public Vector3 visualWorldPosition;
    public bool hasMotion;
    public bool useCornerMotion;
    public int sourceLaneIndex = -1;
    public int destinationLaneIndex = -1;
    public Vector3 startWorldPosition;
    public bool hasViaWorldPosition;
    public Vector3 viaWorldPosition;
    public float progress;
    public float pathLength;
    public float durationPathLength;
    public bool cornerContinuationActive;
    public Vector2Int cornerContinuationBlockCoordinate;
    public int cornerContinuationSourceLaneIndex = -1;
    public int cornerContinuationDestinationLaneIndex = -1;
    public Vector3 cornerContinuationStartWorldPosition;
    public float cornerContinuationStartProgress;
    public float cornerContinuationPathLength;
    public float cornerContinuationDurationPathLength;
}

public static class SaveGameConveyorItemBackfill
{
    public static void BackfillFromFloorObjects(MapSaveData map)
    {
        if (map?.floorObjects == null || map.floorObjects.Count <= 0)
        {
            return;
        }

        map.conveyorItems ??= new List<ConveyorItemBlockSaveEntry>();
        for (int i = 0; i < map.floorObjects.Count; i++)
        {
            FloorObjectSaveEntry floorEntry = map.floorObjects[i];
            if (!TryCreateConveyorItemEntry(floorEntry, out ConveyorItemBlockSaveEntry conveyorEntry))
            {
                continue;
            }

            int existingIndex = FindConveyorItemEntryIndex(map.conveyorItems, conveyorEntry.coordinate);
            if (existingIndex < 0)
            {
                map.conveyorItems.Add(conveyorEntry);
                continue;
            }

            ConveyorItemBlockSaveEntry existingEntry = map.conveyorItems[existingIndex];
            if (existingEntry == null || existingEntry.lanes == null || existingEntry.lanes.Count <= 0)
            {
                map.conveyorItems[existingIndex] = conveyorEntry;
            }
        }
    }

    private static bool TryCreateConveyorItemEntry(
        FloorObjectSaveEntry floorEntry,
        out ConveyorItemBlockSaveEntry conveyorEntry)
    {
        conveyorEntry = null;
        if (floorEntry?.itemIds == null || floorEntry.itemIds.Count <= 0)
        {
            return false;
        }

        List<ConveyorItemLaneSaveState> lanes = null;
        List<int> itemIds = floorEntry.itemIds;
        for (int i = 0; i < itemIds.Count; i++)
        {
            if (itemIds[i] != Block.ConveyorStackStateSentinel)
            {
                continue;
            }

            if (i + 1 >= itemIds.Count)
            {
                break;
            }

            int laneCount = Mathf.Max(0, itemIds[++i]);
            for (int laneIndex = 0; laneIndex < laneCount && i + 1 < itemIds.Count; laneIndex++)
            {
                int laneItemId = itemIds[++i];
                if (laneItemId < 0)
                {
                    continue;
                }

                lanes ??= new List<ConveyorItemLaneSaveState>();
                lanes.Add(new ConveyorItemLaneSaveState
                {
                    laneIndex = laneIndex,
                    itemId = laneItemId
                });
            }
        }

        if (lanes == null || lanes.Count <= 0)
        {
            return false;
        }

        conveyorEntry = new ConveyorItemBlockSaveEntry
        {
            coordinate = floorEntry.coordinate,
            lanes = lanes
        };
        return true;
    }

    public static int FindConveyorItemEntryIndex(
        List<ConveyorItemBlockSaveEntry> entries,
        Vector2Int coordinate)
    {
        if (entries == null)
        {
            return -1;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            ConveyorItemBlockSaveEntry entry = entries[i];
            if (entry != null && entry.coordinate == coordinate)
            {
                return i;
            }
        }

        return -1;
    }
}

[Serializable]
public sealed class PlayerSaveData
{
    public bool hasPlayer;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public bool mountedOnVehicle;
    public long mountedVehiclePlacementSequence;
    public Vector2Int mountedVehicleAnchorCoordinate;
    public int mountedVehiclePlayerPointIndex = -1;
    public long nooseLeashedAnimalId;
    public int activeTorchItemId = -1;
    public float activeTorchRemainingEnergy;
    public int bagLevel = 1;
    public PlayerStatSaveData stats = new PlayerStatSaveData();
    public List<PlayerInventorySlotSaveState> bagSlots = new List<PlayerInventorySlotSaveState>();
    public List<PlayerInventorySlotSaveState> handSlots = new List<PlayerInventorySlotSaveState>();
    public List<PlayerCraftingQueueEntrySaveData> craftingQueue = new List<PlayerCraftingQueueEntrySaveData>();
}

[Serializable]
public sealed class PlayerStatSaveData
{
    public int miningPower;
    public int loggingPower;
    public float miningSpeed;
    public float loggingSpeed;
    public float harvestRange;
}

[Serializable]
public sealed class PlayerInventorySlotSaveState
{
    public int slotIndex;
    public int itemId = -1;
    public int count;
    public int capacity;
}

[Serializable]
public sealed class PlayerCraftingQueueEntrySaveData
{
    public int itemId = -1;
    public int outputCount;
    public int remainingOutputCount;
    public float remainingTime;
    public float duration;
    public List<PlayerCraftingIngredientSaveData> refundIngredients = new List<PlayerCraftingIngredientSaveData>();
}

[Serializable]
public sealed class PlayerCraftingIngredientSaveData
{
    public int itemId = -1;
    public int count;
}
