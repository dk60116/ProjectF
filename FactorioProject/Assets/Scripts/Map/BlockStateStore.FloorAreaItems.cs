using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    private static readonly Vector2Int[] SavedFloorAreaWakeOffsets =
    {
        Vector2Int.zero,
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private sealed class SavedFloorAreaInventory
    {
        public readonly List<int> floorItems = new List<int>();
        public readonly List<int> centerItems = new List<int>();
        public readonly List<int> conveyorLaneItems = new List<int>();
        public bool hasConveyorStack;

        public static SavedFloorAreaInventory FromSerialized(IReadOnlyList<int> itemIds)
        {
            SavedFloorAreaInventory inventory = new SavedFloorAreaInventory();
            if (itemIds == null)
            {
                return inventory;
            }

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                if (itemId == Block.FloorStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        break;
                    }

                    int stackCount = Mathf.Max(0, itemIds[++i]);
                    for (int stackIndex = 0; stackIndex < stackCount && i + 1 < itemIds.Count; stackIndex++)
                    {
                        int stackItemCount = Mathf.Max(0, itemIds[++i]);
                        for (int objectIndex = 0; objectIndex < stackItemCount && i + 1 < itemIds.Count; objectIndex++)
                        {
                            int stackItemId = itemIds[++i];
                            if (stackItemId >= 0)
                            {
                                inventory.floorItems.Add(stackItemId);
                            }
                        }
                    }

                    continue;
                }

                if (itemId == Block.InputAreaCenterStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        break;
                    }

                    int centerCount = Mathf.Max(0, itemIds[++i]);
                    for (int centerIndex = 0; centerIndex < centerCount && i + 1 < itemIds.Count; centerIndex++)
                    {
                        int centerItemId = itemIds[++i];
                        if (centerItemId >= 0)
                        {
                            inventory.centerItems.Add(centerItemId);
                        }
                    }

                    continue;
                }

                if (itemId == Block.ConveyorStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        break;
                    }

                    inventory.hasConveyorStack = true;
                    int laneCount = Mathf.Max(0, itemIds[++i]);
                    for (int laneIndex = 0; laneIndex < laneCount && i + 1 < itemIds.Count; laneIndex++)
                    {
                        inventory.conveyorLaneItems.Add(itemIds[++i]);
                    }

                    continue;
                }

                if (itemId >= 0)
                {
                    inventory.floorItems.Add(itemId);
                }
            }

            return inventory;
        }

        public List<int> ToSerialized()
        {
            List<int> itemIds = new List<int>(floorItems.Count + centerItems.Count + conveyorLaneItems.Count + 4);
            itemIds.AddRange(floorItems);

            if (hasConveyorStack)
            {
                itemIds.Add(Block.ConveyorStackStateSentinel);
                itemIds.Add(conveyorLaneItems.Count);
                itemIds.AddRange(conveyorLaneItems);
            }

            if (centerItems.Count > 0)
            {
                itemIds.Add(Block.InputAreaCenterStackStateSentinel);
                itemIds.Add(centerItems.Count);
                itemIds.AddRange(centerItems);
            }

            return itemIds;
        }
    }

    public bool TryPeekSavedFloorItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId)
    {
        itemId = -1;
        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        int itemIndex = FindSavedFloorItemIndex(inventory, itemFilter);
        if (itemIndex < 0)
        {
            return false;
        }

        itemId = inventory.floorItems[itemIndex];
        return true;
    }

    public bool TryTakeSavedFloorItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId)
    {
        itemId = -1;
        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        int itemIndex = FindSavedFloorItemIndex(inventory, itemFilter);
        if (itemIndex < 0)
        {
            return false;
        }

        itemId = inventory.floorItems[itemIndex];
        inventory.floorItems.RemoveAt(itemIndex);
        SaveSavedFloorAreaInventory(worldCoordinate, inventory);
        NotifySavedFloorAreaStackChanged(worldCoordinate);
        return true;
    }

    public bool CanAddSavedFloorItems(
        Vector2Int worldCoordinate,
        int itemId,
        int count,
        int capacity)
    {
        return CanAddSavedFloorItems(
            LoadSavedFloorAreaInventory(worldCoordinate),
            itemId,
            count,
            capacity);
    }

    public bool TryAddSavedFloorItems(
        Vector2Int worldCoordinate,
        int itemId,
        int count,
        int capacity)
    {
        if (count <= 0)
        {
            return true;
        }

        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        if (!CanAddSavedFloorItems(inventory, itemId, count, capacity))
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            inventory.floorItems.Add(itemId);
        }

        SaveSavedFloorAreaInventory(worldCoordinate, inventory);
        NotifySavedFloorAreaStackChanged(worldCoordinate);
        return true;
    }

    public bool TryPeekSavedCenterTopItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId)
    {
        itemId = GetSavedCenterTopItemId(worldCoordinate);
        return itemId >= 0 && (itemFilter == null || itemFilter(itemId));
    }

    public bool TryTakeSavedCenterTopItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId)
    {
        itemId = -1;
        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        itemId = GetSavedCenterTopItemId(inventory);
        if (itemId < 0 || (itemFilter != null && !itemFilter(itemId)))
        {
            return false;
        }

        inventory.centerItems.RemoveAt(inventory.centerItems.Count - 1);
        SaveSavedFloorAreaInventory(worldCoordinate, inventory);
        NotifySavedFloorAreaStackChanged(worldCoordinate);
        return true;
    }

    public int GetSavedCenterItemCount(Vector2Int worldCoordinate, int itemId = -1)
    {
        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        if (itemId < 0)
        {
            return inventory.centerItems.Count;
        }

        int count = 0;
        for (int i = 0; i < inventory.centerItems.Count; i++)
        {
            if (inventory.centerItems[i] == itemId)
            {
                count++;
            }
        }

        return count;
    }

    public int GetSavedCenterTopItemId(Vector2Int worldCoordinate)
    {
        return GetSavedCenterTopItemId(LoadSavedFloorAreaInventory(worldCoordinate));
    }

    public bool CanAddSavedCenterItems(Vector2Int worldCoordinate, int itemId, int count, int capacity)
    {
        return CanAddSavedCenterItems(LoadSavedFloorAreaInventory(worldCoordinate), itemId, count, capacity);
    }

    public bool TryAddSavedCenterItems(Vector2Int worldCoordinate, int itemId, int count, int capacity)
    {
        if (count <= 0)
        {
            return true;
        }

        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        if (!CanAddSavedCenterItems(inventory, itemId, count, capacity))
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            inventory.centerItems.Add(itemId);
        }

        SaveSavedFloorAreaInventory(worldCoordinate, inventory);
        NotifySavedFloorAreaStackChanged(worldCoordinate);
        return true;
    }

    public int RemoveSavedCenterItems(Vector2Int worldCoordinate, int itemId, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        int removed = 0;
        for (int i = inventory.centerItems.Count - 1; i >= 0 && removed < count; i--)
        {
            if (itemId >= 0 && inventory.centerItems[i] != itemId)
            {
                continue;
            }

            inventory.centerItems.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
        {
            SaveSavedFloorAreaInventory(worldCoordinate, inventory);
            NotifySavedFloorAreaStackChanged(worldCoordinate);
        }

        return removed;
    }

    private static int FindSavedFloorItemIndex(SavedFloorAreaInventory inventory, Predicate<int> itemFilter)
    {
        if (inventory == null || inventory.floorItems.Count <= 0)
        {
            return -1;
        }

        for (int i = inventory.floorItems.Count - 1; i >= 0; i--)
        {
            int itemId = inventory.floorItems[i];
            if (itemId >= 0 && (itemFilter == null || itemFilter(itemId)))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetSavedCenterTopItemId(SavedFloorAreaInventory inventory)
    {
        if (inventory == null || inventory.centerItems.Count <= 0)
        {
            return -1;
        }

        return inventory.centerItems[inventory.centerItems.Count - 1];
    }

    private static bool CanAddSavedCenterItems(SavedFloorAreaInventory inventory, int itemId, int count, int capacity)
    {
        if (inventory == null || itemId < 0 || count <= 0)
        {
            return false;
        }

        for (int i = 0; i < inventory.centerItems.Count; i++)
        {
            if (inventory.centerItems[i] != itemId)
            {
                return false;
            }
        }

        int stackCapacity = ResolveSavedCenterStackCapacity(itemId, capacity);
        return stackCapacity - inventory.centerItems.Count >= count;
    }

    private static bool CanAddSavedFloorItems(
        SavedFloorAreaInventory inventory,
        int itemId,
        int count,
        int capacity)
    {
        if (inventory == null || itemId < 0 || count <= 0)
        {
            return false;
        }

        for (int i = 0; i < inventory.floorItems.Count; i++)
        {
            if (inventory.floorItems[i] != itemId)
            {
                return false;
            }
        }

        int stackCapacity = ResolveSavedCenterStackCapacity(itemId, capacity);
        return stackCapacity - inventory.floorItems.Count >= count;
    }

    private static int ResolveSavedCenterStackCapacity(int itemId, int defaultCapacity)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return ItemDefinition.ResolveStackCapacity(itemManager, itemId, defaultCapacity);
    }

    private SavedFloorAreaInventory LoadSavedFloorAreaInventory(Vector2Int worldCoordinate)
    {
        return savedFloorObjectStates.TryGetValue(worldCoordinate, out FloorObjectSaveState savedState) && savedState != null
            ? SavedFloorAreaInventory.FromSerialized(savedState.ToSerializedList())
            : new SavedFloorAreaInventory();
    }

    private void SaveSavedFloorAreaInventory(Vector2Int worldCoordinate, SavedFloorAreaInventory inventory)
    {
        SetFloorObjects(worldCoordinate, inventory != null ? inventory.ToSerialized() : null);
    }

    private static void NotifySavedFloorAreaStackChanged(Vector2Int coordinate)
    {
        for (int i = 0; i < SavedFloorAreaWakeOffsets.Length; i++)
        {
            Vector2Int wakeCoordinate = coordinate + SavedFloorAreaWakeOffsets[i];
            RobotArm.WakeAroundCoordinate(wakeCoordinate);
            InputOutputModule.WakeRuntimeModulesAtCoordinate(wakeCoordinate);
        }
    }
}
