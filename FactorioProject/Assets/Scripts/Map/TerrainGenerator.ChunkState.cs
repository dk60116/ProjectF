using System.Collections.Generic;
using UnityEngine;

public partial class TerrainGenerator
{
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

        if (hasDetailedConveyorItems && !deferConveyorItemRestoreUntilBeltTopologyReady)
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

    private void CaptureConveyorItemSaveRuns(MapSaveData mapSaveData)
    {
        if (mapSaveData == null)
        {
            return;
        }

        mapSaveData.conveyorItemRuns ??= new List<ConveyorItemRunSaveEntry>();
        mapSaveData.conveyorItemRuns.Clear();
        if (mapSaveData.conveyorItems == null || mapSaveData.conveyorItems.Count <= 0)
        {
            return;
        }

        Dictionary<ConveyorLaneCoordinateKey, ConveyorItemLaneSaveState> occupiedItems =
            new Dictionary<ConveyorLaneCoordinateKey, ConveyorItemLaneSaveState>();
        for (int blockIndex = 0; blockIndex < mapSaveData.conveyorItems.Count; blockIndex++)
        {
            ConveyorItemBlockSaveEntry entry = mapSaveData.conveyorItems[blockIndex];
            if (entry?.lanes == null
                || !loadedBlocks.TryGetValue(entry.coordinate, out Block block)
                || block == null
                || !block.IsRuntimeConveyor)
            {
                continue;
            }

            int laneCount = block.GetRuntimeConveyorLaneCount();
            for (int laneStateIndex = 0; laneStateIndex < entry.lanes.Count; laneStateIndex++)
            {
                ConveyorItemLaneSaveState laneState = entry.lanes[laneStateIndex];
                if (laneState == null
                    || laneState.itemId < 0
                    || laneState.laneIndex < 0
                    || laneState.laneIndex >= laneCount)
                {
                    continue;
                }

                ConveyorLaneCoordinateKey key =
                    new ConveyorLaneCoordinateKey(entry.coordinate, laneState.laneIndex);
                if (!occupiedItems.ContainsKey(key))
                {
                    occupiedItems.Add(key, laneState);
                }
            }
        }

        if (occupiedItems.Count <= 0)
        {
            return;
        }

        Dictionary<ConveyorLaneCoordinateKey, ConveyorLaneCoordinateKey> successors =
            new Dictionary<ConveyorLaneCoordinateKey, ConveyorLaneCoordinateKey>(occupiedItems.Count);
        Dictionary<ConveyorLaneCoordinateKey, int> predecessorCounts =
            new Dictionary<ConveyorLaneCoordinateKey, int>(occupiedItems.Count);
        foreach (KeyValuePair<ConveyorLaneCoordinateKey, ConveyorItemLaneSaveState> pair in occupiedItems)
        {
            predecessorCounts[pair.Key] = 0;
        }

        foreach (KeyValuePair<ConveyorLaneCoordinateKey, ConveyorItemLaneSaveState> pair in occupiedItems)
        {
            ConveyorLaneCoordinateKey sourceKey = pair.Key;
            if (!loadedBlocks.TryGetValue(sourceKey.coordinate, out Block sourceBlock)
                || sourceBlock == null
                || !sourceBlock.TryGetRuntimeConveyorSuccessorLane(
                    sourceKey.laneIndex,
                    out Block destinationBlock,
                    out int destinationLaneIndex)
                || destinationBlock == null)
            {
                continue;
            }

            ConveyorLaneCoordinateKey destinationKey =
                new ConveyorLaneCoordinateKey(destinationBlock.Coordinate, destinationLaneIndex);
            if (!occupiedItems.ContainsKey(destinationKey))
            {
                continue;
            }

            successors[sourceKey] = destinationKey;
            predecessorCounts[destinationKey] = predecessorCounts[destinationKey] + 1;
        }

        List<ConveyorLaneCoordinateKey> orderedKeys =
            new List<ConveyorLaneCoordinateKey>(occupiedItems.Keys);
        orderedKeys.Sort(CompareConveyorSaveLaneKeys);
        HashSet<ConveyorLaneCoordinateKey> compressedKeys =
            new HashSet<ConveyorLaneCoordinateKey>();

        for (int i = 0; i < orderedKeys.Count; i++)
        {
            ConveyorLaneCoordinateKey key = orderedKeys[i];
            if (predecessorCounts[key] != 1)
            {
                CaptureConveyorItemSaveRun(
                    key,
                    occupiedItems,
                    successors,
                    predecessorCounts,
                    compressedKeys,
                    mapSaveData.conveyorItemRuns);
            }
        }

        // 닫힌 순환 벨트는 모든 칸의 선행자가 하나이므로 위 시작점 탐색에 걸리지 않는다.
        for (int i = 0; i < orderedKeys.Count; i++)
        {
            CaptureConveyorItemSaveRun(
                orderedKeys[i],
                occupiedItems,
                successors,
                predecessorCounts,
                compressedKeys,
                mapSaveData.conveyorItemRuns);
        }

        RemoveCompressedConveyorItemStates(mapSaveData.conveyorItems, compressedKeys);
    }

    private static void CaptureConveyorItemSaveRun(
        ConveyorLaneCoordinateKey startKey,
        IReadOnlyDictionary<ConveyorLaneCoordinateKey, ConveyorItemLaneSaveState> occupiedItems,
        IReadOnlyDictionary<ConveyorLaneCoordinateKey, ConveyorLaneCoordinateKey> successors,
        IReadOnlyDictionary<ConveyorLaneCoordinateKey, int> predecessorCounts,
        ISet<ConveyorLaneCoordinateKey> compressedKeys,
        ICollection<ConveyorItemRunSaveEntry> output)
    {
        if (compressedKeys.Contains(startKey) || !occupiedItems.ContainsKey(startKey))
        {
            return;
        }

        ConveyorItemRunSaveEntry run = new ConveyorItemRunSaveEntry
        {
            startCoordinate = startKey.coordinate,
            startLaneIndex = startKey.laneIndex
        };
        ConveyorLaneCoordinateKey currentKey = startKey;
        while (!compressedKeys.Contains(currentKey)
               && occupiedItems.TryGetValue(currentKey, out ConveyorItemLaneSaveState laneState))
        {
            compressedKeys.Add(currentKey);
            run.endCoordinate = currentKey.coordinate;
            run.endLaneIndex = currentKey.laneIndex;
            run.itemCount++;
            AppendConveyorItemTypeRun(run.itemRuns, laneState.itemId);

            if (!successors.TryGetValue(currentKey, out ConveyorLaneCoordinateKey nextKey)
                || predecessorCounts[nextKey] != 1
                || compressedKeys.Contains(nextKey))
            {
                break;
            }

            currentKey = nextKey;
        }

        if (run.itemCount > 0)
        {
            output.Add(run);
        }
    }

    private static void AppendConveyorItemTypeRun(
        List<ConveyorItemTypeRunSaveEntry> itemRuns,
        int itemId)
    {
        int lastIndex = itemRuns.Count - 1;
        if (lastIndex >= 0 && itemRuns[lastIndex].itemId == itemId)
        {
            itemRuns[lastIndex].count++;
            return;
        }

        itemRuns.Add(new ConveyorItemTypeRunSaveEntry
        {
            itemId = itemId,
            count = 1
        });
    }

    private static void RemoveCompressedConveyorItemStates(
        List<ConveyorItemBlockSaveEntry> blockEntries,
        ISet<ConveyorLaneCoordinateKey> compressedKeys)
    {
        for (int blockIndex = blockEntries.Count - 1; blockIndex >= 0; blockIndex--)
        {
            ConveyorItemBlockSaveEntry entry = blockEntries[blockIndex];
            if (entry?.lanes == null)
            {
                continue;
            }

            for (int laneIndex = entry.lanes.Count - 1; laneIndex >= 0; laneIndex--)
            {
                ConveyorItemLaneSaveState lane = entry.lanes[laneIndex];
                if (lane != null
                    && compressedKeys.Contains(
                        new ConveyorLaneCoordinateKey(entry.coordinate, lane.laneIndex)))
                {
                    entry.lanes.RemoveAt(laneIndex);
                }
            }

            if (entry.lanes.Count <= 0)
            {
                blockEntries.RemoveAt(blockIndex);
            }
        }
    }

    private void ExpandConveyorItemSaveRunsAfterBeltTopology(MapSaveData mapSaveData)
    {
        if (mapSaveData?.conveyorItemRuns == null || mapSaveData.conveyorItemRuns.Count <= 0)
        {
            return;
        }

        mapSaveData.conveyorItems ??= new List<ConveyorItemBlockSaveEntry>();
        Dictionary<Vector2Int, ConveyorItemBlockSaveEntry> entriesByCoordinate =
            new Dictionary<Vector2Int, ConveyorItemBlockSaveEntry>(mapSaveData.conveyorItems.Count);
        HashSet<ConveyorLaneCoordinateKey> occupiedKeys = new HashSet<ConveyorLaneCoordinateKey>();
        for (int entryIndex = 0; entryIndex < mapSaveData.conveyorItems.Count; entryIndex++)
        {
            ConveyorItemBlockSaveEntry entry = mapSaveData.conveyorItems[entryIndex];
            if (entry == null)
            {
                continue;
            }

            entriesByCoordinate[entry.coordinate] = entry;
            if (entry.lanes == null)
            {
                entry.lanes = new List<ConveyorItemLaneSaveState>();
                continue;
            }

            for (int laneIndex = 0; laneIndex < entry.lanes.Count; laneIndex++)
            {
                ConveyorItemLaneSaveState lane = entry.lanes[laneIndex];
                if (lane != null && lane.itemId >= 0 && lane.laneIndex >= 0)
                {
                    occupiedKeys.Add(
                        new ConveyorLaneCoordinateKey(entry.coordinate, lane.laneIndex));
                }
            }
        }

        int rejectedRunCount = 0;
        List<ConveyorLaneCoordinateKey> runKeys = new List<ConveyorLaneCoordinateKey>();
        List<int> runItemIds = new List<int>();
        HashSet<ConveyorLaneCoordinateKey> runKeySet = new HashSet<ConveyorLaneCoordinateKey>();
        for (int runIndex = 0; runIndex < mapSaveData.conveyorItemRuns.Count; runIndex++)
        {
            ConveyorItemRunSaveEntry run = mapSaveData.conveyorItemRuns[runIndex];
            if (!TryExpandConveyorItemSaveRun(
                    run,
                    occupiedKeys,
                    runKeys,
                    runItemIds,
                    runKeySet))
            {
                rejectedRunCount++;
                continue;
            }

            for (int itemIndex = 0; itemIndex < runKeys.Count; itemIndex++)
            {
                ConveyorLaneCoordinateKey key = runKeys[itemIndex];
                if (!entriesByCoordinate.TryGetValue(
                        key.coordinate,
                        out ConveyorItemBlockSaveEntry entry))
                {
                    entry = new ConveyorItemBlockSaveEntry
                    {
                        coordinate = key.coordinate
                    };
                    entriesByCoordinate.Add(key.coordinate, entry);
                    mapSaveData.conveyorItems.Add(entry);
                }

                entry.lanes.Add(new ConveyorItemLaneSaveState
                {
                    laneIndex = key.laneIndex,
                    itemId = runItemIds[itemIndex]
                });
                occupiedKeys.Add(key);
            }
        }

        mapSaveData.conveyorItems.Sort(CompareConveyorItemBlockSaveEntries);
        for (int i = 0; i < mapSaveData.conveyorItems.Count; i++)
        {
            ConveyorItemBlockSaveEntry entry = mapSaveData.conveyorItems[i];
            entry?.lanes?.Sort(CompareConveyorItemLaneSaveStates);
            if (entry != null)
            {
                resourceStateStore?.SetConveyorItems(entry.coordinate, entry.lanes);
            }
        }

        mapSaveData.conveyorItemRuns.Clear();
        if (rejectedRunCount > 0)
        {
            Debug.LogWarning(
                $"Conveyor item load skipped {rejectedRunCount} invalid run(s) after belt topology validation.");
        }
    }

    private bool TryExpandConveyorItemSaveRun(
        ConveyorItemRunSaveEntry run,
        ISet<ConveyorLaneCoordinateKey> occupiedKeys,
        List<ConveyorLaneCoordinateKey> runKeys,
        List<int> runItemIds,
        ISet<ConveyorLaneCoordinateKey> runKeySet)
    {
        runKeys.Clear();
        runItemIds.Clear();
        runKeySet.Clear();
        long maximumTopologyItemCount = (long)loadedBlocks.Count * Block.ConveyorCellItemUnit;
        if (run == null
            || run.itemCount <= 0
            || run.itemCount > maximumTopologyItemCount
            || run.startLaneIndex < 0
            || run.endLaneIndex < 0
            || !TryExpandConveyorItemTypes(run, runItemIds))
        {
            return false;
        }

        ConveyorLaneCoordinateKey currentKey =
            new ConveyorLaneCoordinateKey(run.startCoordinate, run.startLaneIndex);
        for (int itemIndex = 0; itemIndex < run.itemCount; itemIndex++)
        {
            if (occupiedKeys.Contains(currentKey)
                || !runKeySet.Add(currentKey)
                || !loadedBlocks.TryGetValue(currentKey.coordinate, out Block block)
                || block == null
                || !block.IsRuntimeConveyor
                || currentKey.laneIndex < 0
                || currentKey.laneIndex >= block.GetRuntimeConveyorLaneCount())
            {
                return false;
            }

            runKeys.Add(currentKey);
            if (itemIndex + 1 >= run.itemCount)
            {
                continue;
            }

            if (!block.TryGetRuntimeConveyorSuccessorLane(
                    currentKey.laneIndex,
                    out Block destinationBlock,
                    out int destinationLaneIndex)
                || destinationBlock == null)
            {
                return false;
            }

            currentKey =
                new ConveyorLaneCoordinateKey(destinationBlock.Coordinate, destinationLaneIndex);
        }

        ConveyorLaneCoordinateKey expectedEndKey =
            new ConveyorLaneCoordinateKey(run.endCoordinate, run.endLaneIndex);
        return currentKey.Equals(expectedEndKey);
    }

    private static bool TryExpandConveyorItemTypes(
        ConveyorItemRunSaveEntry run,
        List<int> output)
    {
        if (run.itemRuns == null || run.itemRuns.Count <= 0)
        {
            return false;
        }

        long totalCount = 0L;
        for (int i = 0; i < run.itemRuns.Count; i++)
        {
            ConveyorItemTypeRunSaveEntry itemRun = run.itemRuns[i];
            if (itemRun == null || itemRun.itemId < 0 || itemRun.count <= 0)
            {
                return false;
            }

            totalCount += itemRun.count;
            if (totalCount > run.itemCount)
            {
                return false;
            }

            for (int count = 0; count < itemRun.count; count++)
            {
                output.Add(itemRun.itemId);
            }
        }

        return totalCount == run.itemCount;
    }

    private static int CompareConveyorSaveLaneKeys(
        ConveyorLaneCoordinateKey left,
        ConveyorLaneCoordinateKey right)
    {
        int yComparison = left.coordinate.y.CompareTo(right.coordinate.y);
        if (yComparison != 0)
        {
            return yComparison;
        }

        int xComparison = left.coordinate.x.CompareTo(right.coordinate.x);
        return xComparison != 0 ? xComparison : left.laneIndex.CompareTo(right.laneIndex);
    }

    private static int CompareConveyorItemBlockSaveEntries(
        ConveyorItemBlockSaveEntry left,
        ConveyorItemBlockSaveEntry right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int yComparison = left.coordinate.y.CompareTo(right.coordinate.y);
        return yComparison != 0
            ? yComparison
            : left.coordinate.x.CompareTo(right.coordinate.x);
    }

    private static int CompareConveyorItemLaneSaveStates(
        ConveyorItemLaneSaveState left,
        ConveyorItemLaneSaveState right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        return right == null ? -1 : left.laneIndex.CompareTo(right.laneIndex);
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
