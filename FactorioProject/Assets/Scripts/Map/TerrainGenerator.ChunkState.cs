using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class TerrainGenerator
{
    private IEnumerator RestoreChunkBlockStatesRoutine(Block[] chunkBlocks, bool allowYield)
    {
        if (chunkBlocks == null)
        {
            yield break;
        }

        EnsureResourceStateStore();

        int blocksSinceYield = 0;
        int blockBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);
        double restoreStepStartTime = Time.realtimeSinceStartupAsDouble;
        BeginConveyorRuntimeRefreshBatch();
        try
        {
            for (int i = 0; i < chunkBlocks.Length; i++)
            {
                using (RestoreChunkBlockStateMarker.Auto())
                {
                    RestoreBlockState(chunkBlocks[i]);
                }
                if (allowYield
                    && (++blocksSinceYield >= blockBudget
                        || HasExceededChunkGenerationFrameBudget(restoreStepStartTime)))
                {
                    blocksSinceYield = 0;
                    yield return null;
                    restoreStepStartTime = Time.realtimeSinceStartupAsDouble;
                }
            }
        }
        finally
        {
            EndConveyorRuntimeRefreshBatch();
        }
    }

    private void RestoreBlockState(Block block)
    {
        if (block == null)
        {
            return;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        List<ConveyorItemLaneSaveState> conveyorItems = null;
        bool hasDetailedConveyorItems = block.IsRuntimeConveyor
            && resourceStateStore.TryGetConveyorItems(block.Coordinate, out conveyorItems);

        bool hasFloorObjects = resourceStateStore.TryGetFloorObjects(block.Coordinate, out List<int> itemIds);
        if (hasFloorObjects && !hasDetailedConveyorItems)
        {
            block.ApplyFloorObjectState(itemIds);
            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Live);
        }

        if (hasDetailedConveyorItems)
        {
            int restoredItemCount = block.ApplyConveyorItemSaveStates(conveyorItems);
            if (restoredItemCount <= 0 && HasConveyorFloorObjectFallback(itemIds))
            {
                block.ApplyFloorObjectState(itemIds);
            }

            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Live);
            resourceStateStore.RemoveConveyorItems(block.Coordinate);
        }

        RobotArm.WakeAroundCoordinate(block.Coordinate);
    }

    private static bool HasConveyorFloorObjectFallback(IReadOnlyList<int> itemIds)
    {
        if (itemIds == null)
        {
            return false;
        }

        for (int i = 0; i < itemIds.Count; i++)
        {
            if (itemIds[i] == Block.ConveyorStackStateSentinel)
            {
                return true;
            }
        }

        return false;
    }

    private void SaveLoadedBlockFloorObjects(Block block)
    {
        if (block == null || resourceStateStore == null)
        {
            return;
        }

        resourceStateStore.SaveFloorObjects(block.Coordinate, block, VirtualObjectResidency.Live);
        if (block.IsRuntimeConveyor)
        {
            conveyorStateSaveConveyorBlocks++;
            conveyorStateSaveConveyorItems += block.GetRuntimeConveyorItemCount();
            resourceStateStore.SaveConveyorItems(block.Coordinate, block);
        }
        else
        {
            conveyorStateSaveClearedNonConveyorBlocks++;
            resourceStateStore.RemoveConveyorItems(block.Coordinate);
        }
    }
}
