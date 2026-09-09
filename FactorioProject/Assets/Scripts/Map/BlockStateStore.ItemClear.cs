using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    private enum SavedItemClearCategory
    {
        DroppedFloor,
        InputOutputArea,
        Conveyor
    }

    private readonly List<Vector2Int> savedItemClearCoordinates = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> savedDetailedConveyorCoordinates = new HashSet<Vector2Int>();

    public int ClearSavedDroppedFloorItems()
    {
        return ClearSavedFloorAreaItems(SavedItemClearCategory.DroppedFloor, null);
    }

    public int ClearSavedInputOutputAreaItems()
    {
        return ClearSavedFloorAreaItems(SavedItemClearCategory.InputOutputArea, null);
    }

    public int ClearSavedConveyorItems()
    {
        int clearedCount = 0;
        savedDetailedConveyorCoordinates.Clear();
        foreach (KeyValuePair<Vector2Int, ConveyorItemBlockState> pair in savedConveyorItemStates)
        {
            int itemCount = CountSavedConveyorItems(pair.Value);
            if (itemCount <= 0)
            {
                continue;
            }

            clearedCount += itemCount;
            savedDetailedConveyorCoordinates.Add(pair.Key);
        }

        savedConveyorItemStates.Clear();
        clearedCount += ClearSavedFloorAreaItems(
            SavedItemClearCategory.Conveyor,
            savedDetailedConveyorCoordinates);
        savedDetailedConveyorCoordinates.Clear();
        return clearedCount;
    }

    private int ClearSavedFloorAreaItems(
        SavedItemClearCategory category,
        HashSet<Vector2Int> coordinatesAlreadyCounted)
    {
        savedItemClearCoordinates.Clear();
        foreach (KeyValuePair<Vector2Int, FloorObjectSaveState> pair in savedFloorObjectStates)
        {
            if (pair.Value != null)
            {
                savedItemClearCoordinates.Add(pair.Key);
            }
        }

        int clearedCount = 0;
        for (int coordinateIndex = 0; coordinateIndex < savedItemClearCoordinates.Count; coordinateIndex++)
        {
            Vector2Int coordinate = savedItemClearCoordinates[coordinateIndex];
            SavedFloorAreaInventory inventory = LoadSavedFloorAreaInventory(coordinate);
            int categoryCount;
            switch (category)
            {
                case SavedItemClearCategory.DroppedFloor:
                    categoryCount = inventory.floorItems.Count;
                    inventory.floorItems.Clear();
                    break;
                case SavedItemClearCategory.InputOutputArea:
                    categoryCount = inventory.centerItems.Count;
                    inventory.centerItems.Clear();
                    break;
                default:
                    categoryCount = CountValidItemIds(inventory.conveyorLaneItems);
                    inventory.conveyorLaneItems.Clear();
                    inventory.hasConveyorStack = false;
                    break;
            }

            if (categoryCount <= 0)
            {
                continue;
            }

            if (coordinatesAlreadyCounted == null || !coordinatesAlreadyCounted.Contains(coordinate))
            {
                clearedCount += categoryCount;
            }

            SaveSavedFloorAreaInventory(coordinate, inventory);
            NotifySavedFloorAreaStackChanged(coordinate);
        }

        savedItemClearCoordinates.Clear();
        return clearedCount;
    }

    private static int CountValidItemIds(List<int> itemIds)
    {
        int count = 0;
        for (int i = 0; i < itemIds.Count; i++)
        {
            if (itemIds[i] >= 0)
            {
                count++;
            }
        }

        return count;
    }
}
