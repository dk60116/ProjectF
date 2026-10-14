using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    private readonly List<InstallationSaveState> savedCenterBoxStateBuffer =
        new List<InstallationSaveState>(2);
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
        public List<float> floorHealth, centerHealth, conveyorHealth;
        public bool hasConveyorStack;

        public static SavedFloorAreaInventory FromSerialized(IReadOnlyList<int> itemIds, IReadOnlyList<PortableItemHealthSaveState> health)
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
                                AppendHealth(ref inventory.floorHealth, inventory.floorItems.Count - 1, PortableItemHealthMetadata.Read(health, i));
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
                            AppendHealth(ref inventory.centerHealth, inventory.centerItems.Count - 1, PortableItemHealthMetadata.Read(health, i));
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
                        AppendHealth(ref inventory.conveyorHealth, inventory.conveyorLaneItems.Count - 1, PortableItemHealthMetadata.Read(health, i));
                    }

                    continue;
                }

                if (itemId >= 0)
                {
                    inventory.floorItems.Add(itemId);
                    AppendHealth(ref inventory.floorHealth, inventory.floorItems.Count - 1, PortableItemHealthMetadata.Read(health, i));
                }
            }

            return inventory;
        }

        public List<int> ToSerialized(out List<PortableItemHealthSaveState> health)
        {
            health = null;
            List<int> itemIds = new List<int>(floorItems.Count + centerItems.Count + conveyorLaneItems.Count + 4);
            WriteHealth(floorHealth, 0, ref health);
            itemIds.AddRange(floorItems);

            if (hasConveyorStack)
            {
                itemIds.Add(Block.ConveyorStackStateSentinel);
                itemIds.Add(conveyorLaneItems.Count);
                WriteHealth(conveyorHealth, itemIds.Count, ref health);
                itemIds.AddRange(conveyorLaneItems);
            }

            if (centerItems.Count > 0)
            {
                itemIds.Add(Block.InputAreaCenterStackStateSentinel);
                itemIds.Add(centerItems.Count);
                WriteHealth(centerHealth, itemIds.Count, ref health);
                itemIds.AddRange(centerItems);
            }

            return itemIds;
        }

        internal static void AppendHealth(ref List<float> values, int precedingCount, float value)
        {
            if (values == null && value > 0f) values = new List<float>(new float[precedingCount]);
            values?.Add(value);
        }

        private static void WriteHealth(List<float> values, int offset, ref List<PortableItemHealthSaveState> result)
        {
            for (int i = 0; values != null && i < values.Count; i++)
                if (values[i] > 0f)
                {
                    result ??= new List<PortableItemHealthSaveState>();
                    result.Add(new PortableItemHealthSaveState { index = offset + i, health = values[i] });
                }
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
        => TryTakeSavedFloorItem(worldCoordinate, itemFilter, out itemId, out _);

    public bool TryTakeSavedFloorItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId, out float installationHealth)
    {
        installationHealth = 0f;
        itemId = -1;
        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        int itemIndex = FindSavedFloorItemIndex(inventory, itemFilter);
        if (itemIndex < 0)
        {
            return false;
        }

        itemId = inventory.floorItems[itemIndex];
        installationHealth = inventory.floorHealth != null ? inventory.floorHealth[itemIndex] : 0f;
        inventory.floorHealth?.RemoveAt(itemIndex);
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
        int capacity, float installationHealth = 0f)
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
            SavedFloorAreaInventory.AppendHealth(ref inventory.floorHealth, inventory.floorItems.Count - 1, installationHealth);
        }

        SaveSavedFloorAreaInventory(worldCoordinate, inventory);
        NotifySavedFloorAreaStackChanged(worldCoordinate);
        return true;
    }

    public bool TryPeekSavedCenterTopItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId)
    {
        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        itemId = GetSavedCenterTopItemId(inventory);
        return itemId >= 0
               && inventory.centerItems.Count > GetSavedBoxMinimumRetainedItemCount(worldCoordinate)
               && (itemFilter == null || itemFilter(itemId));
    }

    public bool TryTakeSavedCenterTopItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId)
        => TryTakeSavedCenterTopItem(worldCoordinate, itemFilter, out itemId, out _);

    public bool TryTakeSavedCenterTopItem(Vector2Int worldCoordinate, Predicate<int> itemFilter, out int itemId, out float installationHealth)
    {
        installationHealth = 0f;
        itemId = -1;
        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        itemId = GetSavedCenterTopItemId(inventory);
        if (itemId < 0
            || inventory.centerItems.Count <= GetSavedBoxMinimumRetainedItemCount(worldCoordinate)
            || (itemFilter != null && !itemFilter(itemId)))
        {
            return false;
        }

        int index = inventory.centerItems.Count - 1;
        installationHealth = inventory.centerHealth != null ? inventory.centerHealth[index] : 0f;
        inventory.centerHealth?.RemoveAt(index);
        inventory.centerItems.RemoveAt(index);
        SaveSavedFloorAreaInventory(worldCoordinate, inventory);
        NotifySavedFloorAreaStackChanged(worldCoordinate);
        return true;
    }

    private int GetSavedBoxMinimumRetainedItemCount(Vector2Int worldCoordinate)
    {
        GetSavedBoxStorageRange(worldCoordinate, out int minimum, out _);
        return minimum;
    }

    private void GetSavedBoxStorageRange(Vector2Int worldCoordinate, out int minimum, out int maximum)
    {
        savedCenterBoxStateBuffer.Clear();
        CollectSavedInstallationStatesAtInteractionCoordinate(
            worldCoordinate,
            savedCenterBoxStateBuffer);
        if (savedCenterBoxStateBuffer.Count == 0
            && TryGetInstallationAnchorAtCoordinate(worldCoordinate, out Vector2Int storageKey)
            && TryGetInstallationStateReadOnly(storageKey, out InstallationSaveState coordinateState))
        {
            savedCenterBoxStateBuffer.Add(coordinateState);
        }
        minimum = BoxObject.DefaultMinimumRetainedItemCount;
        maximum = BoxObject.DefaultMaximumStoredItemCount;
        for (int i = 0; i < savedCenterBoxStateBuffer.Count; i++)
        {
            InstallationSaveState state = savedCenterBoxStateBuffer[i];
            if (state != null && state.boxIsOpen.HasValue)
            {
                minimum = Mathf.Max(minimum, state.boxMinimumRetainedItemCount);
                maximum = Mathf.Min(maximum, Mathf.Max(state.boxMinimumRetainedItemCount, state.boxMaximumStoredItemCount));
            }
        }
    }

    public int GetSavedCenterExtractableItemCount(Vector2Int worldCoordinate, int itemId = -1)
    {
        return Mathf.Max(0, GetSavedCenterItemCount(worldCoordinate, itemId) - GetSavedBoxMinimumRetainedItemCount(worldCoordinate));
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
        GetSavedBoxStorageRange(worldCoordinate, out _, out int maximum);
        return capacity > 0 && maximum > 0
            && CanAddSavedCenterItems(LoadSavedFloorAreaInventory(worldCoordinate), itemId, count, Mathf.Min(capacity, maximum));
    }

    public bool TryAddSavedCenterItems(Vector2Int worldCoordinate, int itemId, int count, int capacity, float installationHealth = 0f)
    {
        if (count <= 0)
        {
            return true;
        }

        SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(worldCoordinate);
        GetSavedBoxStorageRange(worldCoordinate, out _, out int maximum);
        if (capacity <= 0 || maximum <= 0 || !CanAddSavedCenterItems(inventory, itemId, count, Mathf.Min(capacity, maximum)))
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            inventory.centerItems.Add(itemId);
            SavedFloorAreaInventory.AppendHealth(ref inventory.centerHealth, inventory.centerItems.Count - 1, installationHealth);
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
            inventory.centerHealth?.RemoveAt(i);
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
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking && defaultCapacity == int.MaxValue) return int.MaxValue;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return ItemDefinition.ResolveStackCapacity(itemManager, itemId, defaultCapacity);
    }

    private SavedFloorAreaInventory LoadSavedFloorAreaInventory(Vector2Int worldCoordinate)
    {
        return savedFloorObjectStates.TryGetValue(worldCoordinate, out FloorObjectSaveState savedState) && savedState != null
            ? SavedFloorAreaInventory.FromSerialized(savedState.ToSerializedList(), savedState.InstallationHealth)
            : new SavedFloorAreaInventory();
    }

    private void SaveSavedFloorAreaInventory(Vector2Int worldCoordinate, SavedFloorAreaInventory inventory)
    {
        if (inventory == null) SetFloorObjects(worldCoordinate, null);
        else SetFloorObjects(worldCoordinate, inventory.ToSerialized(out var health), health);
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
