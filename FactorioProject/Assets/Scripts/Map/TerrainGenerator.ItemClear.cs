using System.Collections.Generic;

public partial class TerrainGenerator
{
    private readonly List<Block> itemClearLoadedBlocks = new List<Block>();

    public int ClearAllBeltItems(out int runtimeCleared, out int savedCleared)
    {
        int authoritativeCount = GetConveyorItemCount();
        ReleaseAllConveyorTransport();
        runtimeCleared = ClearLoadedItems(ItemClearScope.Belt);
        EnsureResourceStateStore();
        savedCleared = resourceStateStore != null
            ? resourceStateStore.ClearSavedConveyorItems()
            : 0;
        authoritativeConveyorItemTotal = 0;
        authoritativeConveyorItemTotalInitialized = true;
        return authoritativeCount;
    }

    public int ClearAllDroppedFloorItems(out int savedCleared)
    {
        int runtimeCleared = ClearLoadedItems(ItemClearScope.DroppedFloor);
        EnsureResourceStateStore();
        savedCleared = resourceStateStore != null
            ? resourceStateStore.ClearSavedDroppedFloorItems()
            : 0;
        return runtimeCleared;
    }

    public int ClearAllInputOutputAreaItems(out int savedCleared)
    {
        int runtimeCleared = ClearLoadedItems(ItemClearScope.InputOutputArea);
        EnsureResourceStateStore();
        savedCleared = resourceStateStore != null
            ? resourceStateStore.ClearSavedInputOutputAreaItems()
            : 0;
        return runtimeCleared;
    }

    private int ClearLoadedItems(ItemClearScope scope)
    {
        CopyLoadedBlocks(itemClearLoadedBlocks);
        int clearedCount = 0;
        for (int i = 0; i < itemClearLoadedBlocks.Count; i++)
        {
            Block block = itemClearLoadedBlocks[i];
            if (block == null)
            {
                continue;
            }

            switch (scope)
            {
                case ItemClearScope.Belt:
                    clearedCount += block.ClearRuntimeConveyorItems();
                    break;
                case ItemClearScope.DroppedFloor:
                    clearedCount += block.ClearRuntimeDroppedFloorItems();
                    break;
                default:
                    clearedCount += block.ClearRuntimeInputOutputAreaItems();
                    break;
            }
        }

        itemClearLoadedBlocks.Clear();
        return clearedCount;
    }

    private enum ItemClearScope
    {
        Belt,
        DroppedFloor,
        InputOutputArea
    }
}
