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
                RobotArm.WakeAroundCoordinate(block.Coordinate);
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

        RobotArm.WakeAroundCoordinate(block.Coordinate);
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

        int conversionBudget = Mathf.Max(1, floorObjectVirtualizationConversionsPerTick);
        Vector2Int centerCoordinate = GetFloorObjectLiveCenterCoordinate();
        int radius = Mathf.Max(0, floorObjectLiveRadius);
        UpdateFloorObjectVirtualizationWorkQueue(centerCoordinate, radius);
        if (floorObjectVirtualizationWorkQueue.Count <= 0)
        {
            return;
        }

        int conversionCount = 0;
        int processedCount = 0;

        using (FloorObjectVirtualizationScanMarker.Auto())
        {
            while (processedCount < conversionBudget
                && floorObjectVirtualizationWorkQueue.Count > 0)
            {
                Vector2Int coordinate = floorObjectVirtualizationWorkQueue.Dequeue();
                if (!floorObjectVirtualizationQueuedCoordinates.Remove(coordinate))
                {
                    continue;
                }

                processedCount++;

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

    private void UpdateFloorObjectVirtualizationWorkQueue(Vector2Int centerCoordinate, int radius)
    {
        radius = Mathf.Max(0, radius);
        if (!floorObjectVirtualizationLiveAreaInitialized)
        {
            floorObjectVirtualizationLiveAreaInitialized = true;
            floorObjectVirtualizationLiveCenterCoordinate = centerCoordinate;
            floorObjectVirtualizationLiveRadius = radius;
            EnqueueLoadedFloorObjectVirtualizationWork();
            return;
        }

        if (floorObjectVirtualizationLiveCenterCoordinate == centerCoordinate
            && floorObjectVirtualizationLiveRadius == radius)
        {
            return;
        }

        Vector2Int previousCenterCoordinate = floorObjectVirtualizationLiveCenterCoordinate;
        int previousRadius = Mathf.Max(0, floorObjectVirtualizationLiveRadius);
        floorObjectVirtualizationLiveCenterCoordinate = centerCoordinate;
        floorObjectVirtualizationLiveRadius = radius;

        EnqueueFloorObjectLiveAreaDelta(
            previousCenterCoordinate,
            previousRadius,
            centerCoordinate,
            radius);
    }

    private void EnqueueLoadedFloorObjectVirtualizationWork()
    {
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            EnqueueFloorObjectVirtualizationCoordinate(pair.Key);
        }
    }

    private void EnqueueFloorObjectLiveAreaDelta(
        Vector2Int previousCenterCoordinate,
        int previousRadius,
        Vector2Int currentCenterCoordinate,
        int currentRadius)
    {
        previousRadius = Mathf.Max(0, previousRadius);
        currentRadius = Mathf.Max(0, currentRadius);

        int previousMinX = previousCenterCoordinate.x - previousRadius;
        int previousMaxX = previousCenterCoordinate.x + previousRadius;
        int previousMinY = previousCenterCoordinate.y - previousRadius;
        int previousMaxY = previousCenterCoordinate.y + previousRadius;
        int currentMinX = currentCenterCoordinate.x - currentRadius;
        int currentMaxX = currentCenterCoordinate.x + currentRadius;
        int currentMinY = currentCenterCoordinate.y - currentRadius;
        int currentMaxY = currentCenterCoordinate.y + currentRadius;

        EnqueueFloorObjectRectangleDifference(
            currentMinX,
            currentMaxX,
            currentMinY,
            currentMaxY,
            previousMinX,
            previousMaxX,
            previousMinY,
            previousMaxY);
        EnqueueFloorObjectRectangleDifference(
            previousMinX,
            previousMaxX,
            previousMinY,
            previousMaxY,
            currentMinX,
            currentMaxX,
            currentMinY,
            currentMaxY);
    }

    private void EnqueueFloorObjectRectangleDifference(
        int includeMinX,
        int includeMaxX,
        int includeMinY,
        int includeMaxY,
        int excludeMinX,
        int excludeMaxX,
        int excludeMinY,
        int excludeMaxY)
    {
        int overlapMinX = Mathf.Max(includeMinX, excludeMinX);
        int overlapMaxX = Mathf.Min(includeMaxX, excludeMaxX);
        int overlapMinY = Mathf.Max(includeMinY, excludeMinY);
        int overlapMaxY = Mathf.Min(includeMaxY, excludeMaxY);
        if (overlapMinX > overlapMaxX || overlapMinY > overlapMaxY)
        {
            EnqueueFloorObjectRectangle(includeMinX, includeMaxX, includeMinY, includeMaxY);
            return;
        }

        EnqueueFloorObjectRectangle(includeMinX, includeMaxX, includeMinY, overlapMinY - 1);
        EnqueueFloorObjectRectangle(includeMinX, includeMaxX, overlapMaxY + 1, includeMaxY);
        EnqueueFloorObjectRectangle(includeMinX, overlapMinX - 1, overlapMinY, overlapMaxY);
        EnqueueFloorObjectRectangle(overlapMaxX + 1, includeMaxX, overlapMinY, overlapMaxY);
    }

    private void EnqueueFloorObjectRectangle(int minX, int maxX, int minY, int maxY)
    {
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                EnqueueFloorObjectVirtualizationCoordinate(new Vector2Int(x, y));
            }
        }
    }

    private void EnqueueFloorObjectVirtualizationCoordinate(Vector2Int coordinate)
    {
        if (!virtualizeDistantFloorObjects
            || !loadedBlocks.ContainsKey(coordinate)
            || !floorObjectVirtualizationQueuedCoordinates.Add(coordinate))
        {
            return;
        }

        floorObjectVirtualizationWorkQueue.Enqueue(coordinate);
    }

    private void ClearFloorObjectVirtualizationScan()
    {
        floorObjectVirtualizationWorkQueue.Clear();
        floorObjectVirtualizationQueuedCoordinates.Clear();
        floorObjectVirtualizationLiveAreaInitialized = false;
        floorObjectVirtualizationLiveRadius = -1;
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
        if (block == null)
        {
            return false;
        }

        Vector2Int coordinate = block.Coordinate;
        if (!virtualizedFloorObjectCoordinates.Remove(coordinate))
        {
            return false;
        }

        bool wasMaterializing = isMaterializingVirtualFloorObjects;
        isMaterializingVirtualFloorObjects = true;
        try
        {
            if (resourceStateStore != null
                && resourceStateStore.TryGetFloorObjects(coordinate, out List<int> itemIds)
                && itemIds != null
                && itemIds.Count > 0)
            {
                block.ApplyFloorObjectState(itemIds);
                resourceStateStore.SetFloorObjectsResidency(coordinate, VirtualObjectResidency.Live);
            }
            else
            {
                block.ApplyFloorObjectState(null);
            }
        }
        finally
        {
            isMaterializingVirtualFloorObjects = wasMaterializing;
        }

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

    private void SaveLoadedBlockFloorObjects(Block block, VirtualObjectResidency residency)
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

        resourceStateStore.SaveFloorObjects(block.Coordinate, block, residency);
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
