using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SaveGameData
{
    public const int CurrentVersion = 9;

    public int version = CurrentVersion;
    public long savedAtUtcTicks;
    public TerrainSaveData terrain = new TerrainSaveData();
    public MapSaveData map = new MapSaveData();
    public PlayerSaveData player = new PlayerSaveData();
}

[Serializable]
public sealed class TerrainSaveData
{
    public int seed;
    public int mapSize;
}

[Serializable]
public sealed class MapSaveData
{
    public List<ResourceSaveEntry> resources = new List<ResourceSaveEntry>();
    public List<FloorObjectSaveEntry> floorObjects = new List<FloorObjectSaveEntry>();
    public List<InstallationSaveEntry> installations = new List<InstallationSaveEntry>();
    public List<ConveyorItemBlockSaveEntry> conveyorItems = new List<ConveyorItemBlockSaveEntry>();
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

[Serializable]
public sealed class PlayerSaveData
{
    public bool hasPlayer;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
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
