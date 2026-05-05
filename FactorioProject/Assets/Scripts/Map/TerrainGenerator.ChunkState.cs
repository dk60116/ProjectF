using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class TerrainGenerator
{
    private void RestoreChunkBlockStates(Transform chunkTransform)
    {
        IEnumerator routine = RestoreChunkBlockStatesRoutine(chunkTransform, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator RestoreChunkBlockStatesRoutine(Transform chunkTransform, bool allowYield)
    {
        if (chunkTransform == null)
        {
            yield break;
        }

        Block[] chunkBlocks = chunkTransform.GetComponentsInChildren<Block>(true);
        EnsureResourceStateStore();

        int blocksSinceYield = 0;
        int blockBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);
        BeginConveyorRuntimeRefreshBatch();
        try
        {
            for (int i = 0; i < chunkBlocks.Length; i++)
            {
                RestoreBlockState(chunkBlocks[i]);
                if (allowYield && ++blocksSinceYield >= blockBudget)
                {
                    blocksSinceYield = 0;
                    yield return null;
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

        if (resourceStateStore.TryGetFloorObjects(block.Coordinate, out List<int> itemIds))
        {
            if (!hasDetailedConveyorItems && ShouldKeepFloorObjectsVirtual(block.Coordinate, itemIds))
            {
                block.ApplyFloorObjectState(null);
                resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Virtual);
                virtualizedFloorObjectCoordinates.Add(block.Coordinate);
                return;
            }

            if (!hasDetailedConveyorItems)
            {
                block.ApplyFloorObjectState(itemIds);
            }

            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Live);
            virtualizedFloorObjectCoordinates.Remove(block.Coordinate);
        }
        else
        {
            virtualizedFloorObjectCoordinates.Remove(block.Coordinate);
        }

        if (hasDetailedConveyorItems)
        {
            block.ApplyConveyorItemSaveStates(conveyorItems);
            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Live);
            virtualizedFloorObjectCoordinates.Remove(block.Coordinate);
        }
    }

    private void TickFloorObjectVirtualization()
    {
        if (!virtualizeDistantFloorObjects
            || Time.time < nextFloorObjectVirtualizationTime)
        {
            return;
        }

        nextFloorObjectVirtualizationTime = Time.time + Mathf.Max(0.02f, floorObjectVirtualizationInterval);
        if (loadedBlocks.Count <= 0)
        {
            ClearFloorObjectVirtualizationScan();
            return;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        if (floorObjectVirtualizationScanCoordinates.Count != loadedBlocks.Count
            || floorObjectVirtualizationScanIndex >= floorObjectVirtualizationScanCoordinates.Count)
        {
            RebuildFloorObjectVirtualizationScanCoordinates();
        }

        if (floorObjectVirtualizationScanCoordinates.Count <= 0)
        {
            return;
        }

        int conversionBudget = Mathf.Max(1, floorObjectVirtualizationConversionsPerTick);
        int scanBudget = Mathf.Max(1, floorObjectVirtualizationConversionsPerTick);
        Vector2Int centerCoordinate = GetFloorObjectLiveCenterCoordinate();
        int radius = Mathf.Max(0, floorObjectLiveRadius);
        int conversionCount = 0;
        int scannedCount = 0;

        using (FloorObjectVirtualizationScanMarker.Auto())
        {
            while (scannedCount < scanBudget
                && floorObjectVirtualizationScanIndex < floorObjectVirtualizationScanCoordinates.Count)
            {
                Vector2Int coordinate = floorObjectVirtualizationScanCoordinates[floorObjectVirtualizationScanIndex++];
                scannedCount++;

                if (!loadedBlocks.TryGetValue(coordinate, out Block block)
                    || block == null
                    || block.Type != Block.BlockType.Ground)
                {
                    continue;
                }

                bool shouldBeLive = IsWithinFloorObjectLiveRadius(coordinate, centerCoordinate, radius);
                if (shouldBeLive)
                {
                    if (TryMaterializeVirtualFloorObjects(block))
                    {
                        conversionCount++;
                    }
                }
                else if (TryVirtualizeLoadedFloorObjects(block))
                {
                    conversionCount++;
                }

                if (conversionCount >= conversionBudget)
                {
                    break;
                }
            }
        }
    }

    private void RebuildFloorObjectVirtualizationScanCoordinates()
    {
        using (FloorObjectVirtualizationRebuildMarker.Auto())
        {
            floorObjectVirtualizationScanCoordinates.Clear();
            foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
            {
                floorObjectVirtualizationScanCoordinates.Add(pair.Key);
            }

            floorObjectVirtualizationScanIndex = 0;
        }
    }

    private void ClearFloorObjectVirtualizationScan()
    {
        floorObjectVirtualizationScanCoordinates.Clear();
        floorObjectVirtualizationScanIndex = 0;
    }

    private bool ShouldKeepFloorObjectsVirtual(Vector2Int coordinate, IReadOnlyList<int> itemIds)
    {
        return virtualizeDistantFloorObjects
               && Block.IsVirtualizableFloorObjectState(itemIds)
               && !IsWithinFloorObjectLiveRadius(coordinate);
    }

    private bool IsWithinFloorObjectLiveRadius(Vector2Int coordinate)
    {
        return IsWithinFloorObjectLiveRadius(
            coordinate,
            GetFloorObjectLiveCenterCoordinate(),
            Mathf.Max(0, floorObjectLiveRadius));
    }

    private Vector2Int GetFloorObjectLiveCenterCoordinate()
    {
        ResolveTrackingTarget();
        Vector3 sourcePosition = trackingTarget != null ? trackingTarget.position : transform.position;
        return GetWorldBlockCoordinate(sourcePosition);
    }

    private static bool IsWithinFloorObjectLiveRadius(Vector2Int coordinate, Vector2Int centerCoordinate, int radius)
    {
        return Mathf.Abs(coordinate.x - centerCoordinate.x) <= radius
               && Mathf.Abs(coordinate.y - centerCoordinate.y) <= radius;
    }

    private bool TryMaterializeVirtualFloorObjects(Block block)
    {
        if (block == null || !virtualizedFloorObjectCoordinates.Contains(block.Coordinate))
        {
            return false;
        }

        if (resourceStateStore != null
            && resourceStateStore.TryGetFloorObjects(block.Coordinate, out List<int> itemIds)
            && itemIds != null
            && itemIds.Count > 0)
        {
            block.ApplyFloorObjectState(itemIds);
            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Live);
        }
        else
        {
            block.ApplyFloorObjectState(null);
        }

        virtualizedFloorObjectCoordinates.Remove(block.Coordinate);
        return true;
    }

    private bool TryVirtualizeLoadedFloorObjects(Block block)
    {
        if (block == null || virtualizedFloorObjectCoordinates.Contains(block.Coordinate))
        {
            return false;
        }

        if (!block.TryCaptureVirtualizableFloorObjectState(out List<int> itemIds))
        {
            return false;
        }

        resourceStateStore.SetFloorObjects(block.Coordinate, itemIds);
        block.ClearLiveFloorObjectsForVirtualization();
        resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Virtual);
        virtualizedFloorObjectCoordinates.Add(block.Coordinate);
        return true;
    }

    private void SaveLoadedBlockFloorObjects(Block block)
    {
        if (block == null || resourceStateStore == null)
        {
            return;
        }

        if (virtualizedFloorObjectCoordinates.Contains(block.Coordinate))
        {
            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Virtual);
            return;
        }

        resourceStateStore.SaveFloorObjects(block.Coordinate, block);
        if (block.IsRuntimeConveyor)
        {
            resourceStateStore.SaveConveyorItems(block.Coordinate, block);
        }
        else
        {
            resourceStateStore.RemoveConveyorItems(block.Coordinate);
        }
    }
}
