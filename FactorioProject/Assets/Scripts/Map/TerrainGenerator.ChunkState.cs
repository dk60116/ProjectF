using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

        bool hasFloorObjects = resourceStateStore.TryGetFloorObjects(block.Coordinate, out List<int> itemIds);
        if (hasFloorObjects)
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
            int restoredItemCount = block.ApplyConveyorItemSaveStates(conveyorItems);
            if (restoredItemCount <= 0
                && HasConveyorFloorObjectFallback(itemIds))
            {
                block.ApplyFloorObjectState(itemIds);
            }

            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Live);
            virtualizedFloorObjectCoordinates.Remove(block.Coordinate);
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

    private void TickConveyorItemResidencyScan()
    {
        if (!virtualizeConveyorItems
            || Time.time < nextConveyorItemResidencyScanTime)
        {
            return;
        }

        nextConveyorItemResidencyScanTime = Time.time + Mathf.Max(0.02f, conveyorItemResidencyScanInterval);
        ResetLastConveyorItemResidencyScanStats();
        if (loadedBlocks.Count <= 0)
        {
            ClearConveyorItemResidencyScan();
            return;
        }

        Vector2Int centerCoordinate = GetConveyorItemLiveCenterCoordinate();
        GetConveyorItemLiveAreaBounds(
            centerCoordinate,
            Mathf.Max(1, conveyorItemLiveAreaSize),
            out Vector2Int liveMin,
            out Vector2Int liveMaxExclusive);
        UpdateConveyorItemResidencyWorkQueue(liveMin, liveMaxExclusive);
        if (conveyorItemResidencyWorkQueue.Count <= 0)
        {
            return;
        }

        int scanBudget = Mathf.Max(1, conveyorItemResidencyScanBudget);
        int processedCount = 0;
        while (processedCount < scanBudget && conveyorItemResidencyWorkQueue.Count > 0)
        {
            Vector2Int coordinate = conveyorItemResidencyWorkQueue.Dequeue();
            if (!conveyorItemResidencyQueuedCoordinates.Remove(coordinate))
            {
                continue;
            }

            processedCount++;
            lastConveyorItemResidencyProcessed++;
            if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
            {
                lastConveyorItemResidencySkippedNotLoaded++;
                continue;
            }

            if (!block.IsRuntimeConveyor)
            {
                lastConveyorItemResidencySkippedNotConveyor++;
                continue;
            }

            if (IsWithinConveyorItemLiveArea(coordinate))
            {
                TryMaterializeStoredConveyorItemsForLiveArea(block);
                if (block.GetRuntimeConveyorItemCount() <= 0)
                {
                    lastConveyorItemResidencySkippedEmpty++;
                    continue;
                }

                lastConveyorItemResidencyLiveCandidates++;
                continue;
            }

            if (block.GetRuntimeConveyorItemCount() <= 0)
            {
                lastConveyorItemResidencySkippedEmpty++;
            }
            else
            {
                lastConveyorItemResidencyBackgroundCandidates++;
                if (!TryVirtualizeLoadedConveyorItems(block))
                {
                    lastConveyorItemResidencyVirtualizeFailed++;
                }
            }
        }
    }

    private void ResetLastConveyorItemResidencyScanStats()
    {
        lastConveyorItemResidencyProcessed = 0;
        lastConveyorItemResidencyLiveCandidates = 0;
        lastConveyorItemResidencyBackgroundCandidates = 0;
        lastConveyorItemResidencySkippedNotLoaded = 0;
        lastConveyorItemResidencySkippedNotConveyor = 0;
        lastConveyorItemResidencySkippedEmpty = 0;
        lastConveyorItemResidencyVirtualized = 0;
        lastConveyorItemResidencyVirtualizedItems = 0;
        lastConveyorItemResidencyVirtualizeFailed = 0;
        lastConveyorItemResidencyMaterialized = 0;
        lastConveyorItemResidencyMaterializedItems = 0;
        lastConveyorItemResidencyMaterializeFailed = 0;
        lastConveyorItemMaterializeTraceSamples = 0;
        lastConveyorItemMaterializeTraceLanes = 0;
        lastConveyorItemMaterializeTraceMotionLanes = 0;
        lastConveyorItemMaterializeTraceMissingRuntimeLanes = 0;
        lastConveyorItemMaterializeTraceMotionLost = 0;
        lastConveyorItemMaterializeTraceMotionModeMismatches = 0;
        lastConveyorItemMaterializeTraceLaneMismatches = 0;
        lastConveyorItemMaterializeTraceProgressMismatches = 0;
        lastConveyorItemMaterializeTraceVisualMismatches = 0;
        lastConveyorItemMaterializeTraceMaxProgressDelta = 0f;
        lastConveyorItemMaterializeTraceMaxVisualDistance = 0f;
        lastConveyorItemMaterializeTraceLastSample = string.Empty;
        lastConveyorItemMaterializeCarryLanes = 0;
        lastConveyorItemMaterializeCarryAdvanced = 0;
        lastConveyorItemMaterializeCarryFailed = 0;
        lastConveyorItemMaterializeCarryDistance = 0f;
        lastConveyorItemMaterializeCarryMaxDistance = 0f;
        lastConveyorItemMaterializeOwnershipRemovedLanes = 0;
        lastConveyorItemMaterializeOwnershipRollbackLanes = 0;
        lastConveyorItemMaterializeOwnershipFailures = 0;
        lastConveyorItemMaterializeOwnershipStaleSavedLanes = 0;
        lastConveyorItemMaterializeImmediateWakeCalls = 0;
        lastConveyorItemMaterializeImmediateWakeBlocks = 0;
    }

    private bool TryMaterializeStoredConveyorItemsForLiveArea(Block block)
    {
        if (isMaterializingConveyorItemsForLiveArea)
        {
            return false;
        }

        isMaterializingConveyorItemsForLiveArea = true;
        try
        {
            return TryMaterializeStoredConveyorItemsForLiveAreaCore(block);
        }
        finally
        {
            isMaterializingConveyorItemsForLiveArea = false;
        }
    }

    private bool TryMaterializeStoredConveyorItemsForLiveAreaCore(Block block)
    {
        if (block == null
            || !block.IsRuntimeConveyor
            || !IsConveyorItemCoordinateLiveForRefresh(block.Coordinate))
        {
            return false;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            if (virtualizedConveyorItemCoordinates.Contains(block.Coordinate))
            {
                lastConveyorItemResidencyMaterializeFailed++;
            }

            return false;
        }

        if (!resourceStateStore.TryGetConveyorItems(block.Coordinate, out List<ConveyorItemLaneSaveState> lanes)
            || CountConveyorItemSaveLanes(lanes) <= 0)
        {
            virtualizedConveyorItemCoordinates.Remove(block.Coordinate);
            return false;
        }

        conveyorItemResidencyLaneScratch.Clear();
        block.CaptureConveyorItemSaveStates(conveyorItemResidencyLaneScratch);
        CollectConveyorItemLaneOverlap(
            lanes,
            conveyorItemResidencyLaneScratch,
            conveyorItemResidencyStaleLaneScratch);
        conveyorItemResidencyLaneScratch.Clear();

        int restoredItemCount = block.ApplyConveyorItemSaveStatesToEmptyLanes(
            lanes,
            conveyorItemResidencyAppliedLaneScratch);
        conveyorItemResidencyOwnershipLaneScratch.Clear();
        AddUniqueConveyorLaneIndices(
            conveyorItemResidencyOwnershipLaneScratch,
            conveyorItemResidencyAppliedLaneScratch);
        AddUniqueConveyorLaneIndices(
            conveyorItemResidencyOwnershipLaneScratch,
            conveyorItemResidencyStaleLaneScratch);

        if (restoredItemCount <= 0 && conveyorItemResidencyStaleLaneScratch.Count <= 0)
        {
            lastConveyorItemResidencyMaterializeFailed++;
            conveyorItemResidencyAppliedLaneScratch.Clear();
            conveyorItemResidencyOwnershipLaneScratch.Clear();
            conveyorItemResidencyStaleLaneScratch.Clear();
            return false;
        }

        if (!resourceStateStore.TryRemoveConveyorItemLanes(
                block.Coordinate,
                conveyorItemResidencyOwnershipLaneScratch,
                out int removedLaneCount)
            || removedLaneCount != conveyorItemResidencyOwnershipLaneScratch.Count)
        {
            lastConveyorItemResidencyMaterializeFailed++;
            lastConveyorItemMaterializeOwnershipFailures++;
            lastConveyorItemMaterializeOwnershipRollbackLanes += block.ClearRuntimeConveyorItemLanes(
                conveyorItemResidencyAppliedLaneScratch);
            conveyorItemResidencyAppliedLaneScratch.Clear();
            conveyorItemResidencyOwnershipLaneScratch.Clear();
            conveyorItemResidencyStaleLaneScratch.Clear();
            return false;
        }

        lastConveyorItemMaterializeOwnershipRemovedLanes += removedLaneCount;
        lastConveyorItemMaterializeOwnershipStaleSavedLanes += conveyorItemResidencyStaleLaneScratch.Count;
        if (restoredItemCount > 0)
        {
            RecordConveyorItemMaterializeTrace(block, lanes, conveyorItemResidencyAppliedLaneScratch);
            AdvanceMaterializedConveyorItemCarryDistances(block, conveyorItemResidencyAppliedLaneScratch);
            WakeMaterializedConveyorItems(block);
        }

        conveyorItemResidencyAppliedLaneScratch.Clear();
        conveyorItemResidencyOwnershipLaneScratch.Clear();
        conveyorItemResidencyStaleLaneScratch.Clear();
        if (resourceStateStore.GetSavedConveyorItemCount(block.Coordinate) <= 0)
        {
            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Live);
            virtualizedConveyorItemCoordinates.Remove(block.Coordinate);
        }

        if (restoredItemCount > 0)
        {
            lastConveyorItemResidencyMaterialized++;
            lastConveyorItemResidencyMaterializedItems += restoredItemCount;
        }

        return true;
    }

    private void AdvanceMaterializedConveyorItemCarryDistances(
        Block block,
        IReadOnlyList<int> appliedLaneIndices)
    {
        if (block == null
            || resourceStateStore == null
            || appliedLaneIndices == null
            || appliedLaneIndices.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < appliedLaneIndices.Count; i++)
        {
            int laneIndex = appliedLaneIndices[i];
            if (!resourceStateStore.TryConsumeConveyorMaterializeCarryDistance(
                    block.Coordinate,
                    laneIndex,
                    out float carryDistance))
            {
                continue;
            }

            lastConveyorItemMaterializeCarryLanes++;
            lastConveyorItemMaterializeCarryDistance += carryDistance;
            lastConveyorItemMaterializeCarryMaxDistance = Mathf.Max(
                lastConveyorItemMaterializeCarryMaxDistance,
                carryDistance);

            if (block.TryAdvanceRuntimeConveyorItemCarryDistance(laneIndex, carryDistance))
            {
                lastConveyorItemMaterializeCarryAdvanced++;
            }
            else
            {
                lastConveyorItemMaterializeCarryFailed++;
            }
        }
    }

    private void RecordConveyorItemMaterializeTrace(
        Block block,
        IReadOnlyList<ConveyorItemLaneSaveState> savedLanes,
        IReadOnlyList<int> appliedLaneIndices)
    {
        if (block == null
            || savedLanes == null
            || appliedLaneIndices == null
            || appliedLaneIndices.Count <= 0)
        {
            return;
        }

        conveyorItemResidencyLaneScratch.Clear();
        try
        {
            block.CaptureConveyorItemSaveStates(conveyorItemResidencyLaneScratch);
            lastConveyorItemMaterializeTraceSamples++;
            lastConveyorItemMaterializeTraceLanes += appliedLaneIndices.Count;

            int sampleMissingRuntimeLanes = 0;
            int sampleMotionLost = 0;
            int sampleMotionModeMismatches = 0;
            int sampleLaneMismatches = 0;
            int sampleProgressMismatches = 0;
            int sampleVisualMismatches = 0;
            int worstLaneIndex = -1;
            float sampleMaxProgressDelta = 0f;
            float sampleMaxVisualDistance = 0f;

            for (int i = 0; i < appliedLaneIndices.Count; i++)
            {
                int laneIndex = appliedLaneIndices[i];
                if (!TryFindConveyorItemSaveLane(savedLanes, laneIndex, out ConveyorItemLaneSaveState savedLane))
                {
                    continue;
                }

                if (savedLane.hasMotion)
                {
                    lastConveyorItemMaterializeTraceMotionLanes++;
                }

                if (!TryFindConveyorItemSaveLane(
                        conveyorItemResidencyLaneScratch,
                        laneIndex,
                        out ConveyorItemLaneSaveState runtimeLane))
                {
                    sampleMissingRuntimeLanes++;
                    worstLaneIndex = laneIndex;
                    continue;
                }

                float visualDistance = Vector3.Distance(savedLane.visualWorldPosition, runtimeLane.visualWorldPosition);
                if (visualDistance > sampleMaxVisualDistance)
                {
                    sampleMaxVisualDistance = visualDistance;
                    worstLaneIndex = laneIndex;
                }

                if (visualDistance > 0.01f)
                {
                    sampleVisualMismatches++;
                }

                if (!savedLane.hasMotion)
                {
                    continue;
                }

                if (!runtimeLane.hasMotion)
                {
                    sampleMotionLost++;
                    worstLaneIndex = laneIndex;
                    continue;
                }

                if (savedLane.useCornerMotion != runtimeLane.useCornerMotion)
                {
                    sampleMotionModeMismatches++;
                    worstLaneIndex = laneIndex;
                }

                if (savedLane.sourceLaneIndex != runtimeLane.sourceLaneIndex
                    || savedLane.destinationLaneIndex != runtimeLane.destinationLaneIndex)
                {
                    sampleLaneMismatches++;
                    worstLaneIndex = laneIndex;
                }

                float progressDelta = Mathf.Abs(Mathf.Clamp01(savedLane.progress) - Mathf.Clamp01(runtimeLane.progress));
                if (progressDelta > sampleMaxProgressDelta)
                {
                    sampleMaxProgressDelta = progressDelta;
                    worstLaneIndex = laneIndex;
                }

                if (progressDelta > 0.001f)
                {
                    sampleProgressMismatches++;
                }
            }

            lastConveyorItemMaterializeTraceMissingRuntimeLanes += sampleMissingRuntimeLanes;
            lastConveyorItemMaterializeTraceMotionLost += sampleMotionLost;
            lastConveyorItemMaterializeTraceMotionModeMismatches += sampleMotionModeMismatches;
            lastConveyorItemMaterializeTraceLaneMismatches += sampleLaneMismatches;
            lastConveyorItemMaterializeTraceProgressMismatches += sampleProgressMismatches;
            lastConveyorItemMaterializeTraceVisualMismatches += sampleVisualMismatches;
            lastConveyorItemMaterializeTraceMaxProgressDelta = Mathf.Max(
                lastConveyorItemMaterializeTraceMaxProgressDelta,
                sampleMaxProgressDelta);
            lastConveyorItemMaterializeTraceMaxVisualDistance = Mathf.Max(
                lastConveyorItemMaterializeTraceMaxVisualDistance,
                sampleMaxVisualDistance);
            lastConveyorItemMaterializeTraceLastSample = BuildConveyorItemMaterializeTraceSample(
                block.Coordinate,
                appliedLaneIndices.Count,
                worstLaneIndex,
                sampleMissingRuntimeLanes,
                sampleMotionLost,
                sampleMotionModeMismatches,
                sampleLaneMismatches,
                sampleProgressMismatches,
                sampleVisualMismatches,
                sampleMaxProgressDelta,
                sampleMaxVisualDistance);
        }
        finally
        {
            conveyorItemResidencyLaneScratch.Clear();
        }
    }

    private static string BuildConveyorItemMaterializeTraceSample(
        Vector2Int coordinate,
        int laneCount,
        int worstLaneIndex,
        int missingRuntimeLanes,
        int motionLost,
        int motionModeMismatches,
        int laneMismatches,
        int progressMismatches,
        int visualMismatches,
        float maxProgressDelta,
        float maxVisualDistance)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "coord=({0},{1}) lanes={2} worstLane={3} missing={4} motionLost={5} modeMismatch={6} laneMismatch={7} progressMismatch={8} visualMismatch={9} maxProgressDelta={10:0.####} maxVisualDistance={11:0.###}",
            coordinate.x,
            coordinate.y,
            laneCount,
            worstLaneIndex,
            missingRuntimeLanes,
            motionLost,
            motionModeMismatches,
            laneMismatches,
            progressMismatches,
            visualMismatches,
            maxProgressDelta,
            maxVisualDistance);
    }

    private bool TryVirtualizeLoadedConveyorItems(Block block)
    {
        if (block == null
            || !block.IsRuntimeConveyor
            || IsWithinConveyorItemLiveArea(block.Coordinate)
            || block.GetRuntimeConveyorItemCount() <= 0)
        {
            return false;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return false;
        }

        conveyorItemResidencyLaneScratch.Clear();
        try
        {
            block.CaptureConveyorItemSaveStates(conveyorItemResidencyLaneScratch);
            int laneCount = CountConveyorItemSaveLanes(conveyorItemResidencyLaneScratch);
            if (laneCount <= 0)
            {
                return false;
            }

            int clearedCount = block.ClearLiveConveyorItemsForVirtualization();
            if (clearedCount <= 0)
            {
                return false;
            }

            if (resourceStateStore.TryGetConveyorItems(
                    block.Coordinate,
                    out List<ConveyorItemLaneSaveState> storedLanes))
            {
                AppendNonOverlappingConveyorItemSaveLanes(conveyorItemResidencyLaneScratch, storedLanes);
            }

            resourceStateStore.SetConveyorItems(block.Coordinate, conveyorItemResidencyLaneScratch);
            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Virtual);
            virtualizedConveyorItemCoordinates.Add(block.Coordinate);
            lastConveyorItemResidencyVirtualized++;
            lastConveyorItemResidencyVirtualizedItems += laneCount;
            return true;
        }
        finally
        {
            conveyorItemResidencyLaneScratch.Clear();
        }
    }

    private void RefreshConveyorItemResidencyAfterBackgroundConveyorChanges()
    {
        if (!virtualizeConveyorItems
            || resourceStateStore == null
            || (backgroundConveyorDirtyCoordinates.Count <= 0
                && backgroundConveyorOccupancyChangedCoordinates.Count <= 0))
        {
            return;
        }

        RefreshConveyorItemResidencyAfterBackgroundConveyorChanges(backgroundConveyorOccupancyChangedCoordinates);
        RefreshConveyorItemResidencyAfterBackgroundConveyorChanges(backgroundConveyorDirtyCoordinates);
    }

    private void RefreshConveyorItemResidencyAfterBackgroundConveyorChanges(IReadOnlyList<Vector2Int> coordinates)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            RefreshConveyorItemResidencyAfterBackgroundConveyorChange(coordinates[i]);
        }
    }

    private void RefreshConveyorItemResidencyAfterBackgroundConveyorChange(Vector2Int coordinate)
    {
        if (!virtualizeConveyorItems
            || resourceStateStore == null
            || !loadedBlocks.TryGetValue(coordinate, out Block block)
            || block == null
            || !block.IsRuntimeConveyor)
        {
            return;
        }

        int savedItemCount = resourceStateStore.GetSavedConveyorItemCount(coordinate);
        if (savedItemCount <= 0)
        {
            if (block.GetRuntimeConveyorItemCount() <= 0)
            {
                virtualizedConveyorItemCoordinates.Remove(coordinate);
            }

            return;
        }

        if (IsConveyorItemCoordinateLiveForRefresh(coordinate))
        {
            TryMaterializeStoredConveyorItemsForLiveArea(block);
            return;
        }

        if (block.GetRuntimeConveyorItemCount() <= 0)
        {
            MarkLoadedConveyorItemBlockVirtual(coordinate);
        }
        else if (!TryVirtualizeLoadedConveyorItems(block))
        {
            lastConveyorItemResidencyVirtualizeFailed++;
        }

        EnqueueConveyorItemResidencyCoordinate(coordinate);
    }

    private void WakeMaterializedConveyorItems(Block block)
    {
        if (block == null)
        {
            return;
        }

        block.WakeConveyorMoveAttemptsAlongRuntimeFlowImmediate();
        QueueConveyorDirectWakeAround(block);
        lastConveyorItemMaterializeImmediateWakeCalls++;
        lastConveyorItemMaterializeImmediateWakeBlocks += CountLoadedRuntimeConveyorNeighbors(block.Coordinate);
    }

    private int CountLoadedRuntimeConveyorNeighbors(Vector2Int coordinate)
    {
        int count = IsLoadedRuntimeConveyorCoordinate(coordinate) ? 1 : 0;
        if (IsLoadedRuntimeConveyorCoordinate(coordinate + Vector2Int.up))
        {
            count++;
        }

        if (IsLoadedRuntimeConveyorCoordinate(coordinate + Vector2Int.right))
        {
            count++;
        }

        if (IsLoadedRuntimeConveyorCoordinate(coordinate + Vector2Int.down))
        {
            count++;
        }

        if (IsLoadedRuntimeConveyorCoordinate(coordinate + Vector2Int.left))
        {
            count++;
        }

        return count;
    }

    private bool IsLoadedRuntimeConveyorCoordinate(Vector2Int coordinate)
    {
        return TryGetLoadedBlock(coordinate, out Block block)
               && block != null
               && block.IsRuntimeConveyor;
    }

    private static void GetConveyorItemLiveAreaBounds(
        Vector2Int centerCoordinate,
        int areaSize,
        out Vector2Int minCoordinate,
        out Vector2Int maxExclusiveCoordinate)
    {
        areaSize = Mathf.Max(1, areaSize);
        int beforeCenter = areaSize / 2;
        int afterCenter = areaSize - beforeCenter;
        minCoordinate = new Vector2Int(centerCoordinate.x - beforeCenter, centerCoordinate.y - beforeCenter);
        maxExclusiveCoordinate = new Vector2Int(centerCoordinate.x + afterCenter, centerCoordinate.y + afterCenter);
    }

    private void UpdateConveyorItemResidencyWorkQueue(Vector2Int liveMin, Vector2Int liveMaxExclusive)
    {
        if (!conveyorItemLiveAreaInitialized)
        {
            conveyorItemLiveAreaInitialized = true;
            conveyorItemLiveMinCoordinate = liveMin;
            conveyorItemLiveMaxExclusiveCoordinate = liveMaxExclusive;
            EnqueueLoadedConveyorItemResidencyWork();
            return;
        }

        if (conveyorItemLiveMinCoordinate == liveMin
            && conveyorItemLiveMaxExclusiveCoordinate == liveMaxExclusive)
        {
            return;
        }

        Vector2Int previousMin = conveyorItemLiveMinCoordinate;
        Vector2Int previousMaxExclusive = conveyorItemLiveMaxExclusiveCoordinate;
        conveyorItemLiveMinCoordinate = liveMin;
        conveyorItemLiveMaxExclusiveCoordinate = liveMaxExclusive;

        EnqueueConveyorItemResidencyRectangleDifference(
            liveMin,
            liveMaxExclusive,
            previousMin,
            previousMaxExclusive);
        EnqueueConveyorItemResidencyRectangleDifference(
            previousMin,
            previousMaxExclusive,
            liveMin,
            liveMaxExclusive);
    }

    private void EnqueueLoadedConveyorItemResidencyWork()
    {
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            EnqueueConveyorItemResidencyCoordinate(pair.Key);
        }
    }

    private void EnqueueConveyorItemResidencyRectangleDifference(
        Vector2Int includeMin,
        Vector2Int includeMaxExclusive,
        Vector2Int excludeMin,
        Vector2Int excludeMaxExclusive)
    {
        if (includeMin.x >= includeMaxExclusive.x || includeMin.y >= includeMaxExclusive.y)
        {
            return;
        }

        int overlapMinX = Mathf.Max(includeMin.x, excludeMin.x);
        int overlapMaxX = Mathf.Min(includeMaxExclusive.x, excludeMaxExclusive.x);
        int overlapMinY = Mathf.Max(includeMin.y, excludeMin.y);
        int overlapMaxY = Mathf.Min(includeMaxExclusive.y, excludeMaxExclusive.y);
        if (overlapMinX >= overlapMaxX || overlapMinY >= overlapMaxY)
        {
            EnqueueConveyorItemResidencyRectangle(includeMin, includeMaxExclusive);
            return;
        }

        EnqueueConveyorItemResidencyRectangle(
            new Vector2Int(includeMin.x, includeMin.y),
            new Vector2Int(includeMaxExclusive.x, overlapMinY));
        EnqueueConveyorItemResidencyRectangle(
            new Vector2Int(includeMin.x, overlapMaxY),
            new Vector2Int(includeMaxExclusive.x, includeMaxExclusive.y));
        EnqueueConveyorItemResidencyRectangle(
            new Vector2Int(includeMin.x, overlapMinY),
            new Vector2Int(overlapMinX, overlapMaxY));
        EnqueueConveyorItemResidencyRectangle(
            new Vector2Int(overlapMaxX, overlapMinY),
            new Vector2Int(includeMaxExclusive.x, overlapMaxY));
    }

    private void EnqueueConveyorItemResidencyRectangle(Vector2Int min, Vector2Int maxExclusive)
    {
        if (min.x >= maxExclusive.x || min.y >= maxExclusive.y)
        {
            return;
        }

        for (int y = min.y; y < maxExclusive.y; y++)
        {
            for (int x = min.x; x < maxExclusive.x; x++)
            {
                EnqueueConveyorItemResidencyCoordinate(new Vector2Int(x, y));
            }
        }
    }

    private void EnqueueConveyorItemResidencyCoordinate(Vector2Int coordinate)
    {
        if (!virtualizeConveyorItems
            || !loadedBlocks.ContainsKey(coordinate)
            || !conveyorItemResidencyQueuedCoordinates.Add(coordinate))
        {
            return;
        }

        conveyorItemResidencyWorkQueue.Enqueue(coordinate);
    }

    private bool IsWithinConveyorItemLiveArea(Vector2Int coordinate)
    {
        return conveyorItemLiveAreaInitialized
               && coordinate.x >= conveyorItemLiveMinCoordinate.x
               && coordinate.x < conveyorItemLiveMaxExclusiveCoordinate.x
               && coordinate.y >= conveyorItemLiveMinCoordinate.y
               && coordinate.y < conveyorItemLiveMaxExclusiveCoordinate.y;
    }

    private bool TryRefreshLiveConveyorItemsForSavedChange(Vector2Int coordinate)
    {
        if (!virtualizeConveyorItems
            || resourceStateStore == null
            || !IsConveyorItemCoordinateLiveForRefresh(coordinate)
            || !loadedBlocks.TryGetValue(coordinate, out Block block)
            || block == null
            || !block.IsRuntimeConveyor)
        {
            return false;
        }

        return TryMaterializeStoredConveyorItemsForLiveArea(block);
    }

    private bool IsConveyorItemCoordinateLiveForRefresh(Vector2Int coordinate)
    {
        if (IsWithinConveyorItemLiveArea(coordinate))
        {
            return true;
        }

        GetConveyorItemLiveAreaBounds(
            GetConveyorItemLiveCenterCoordinate(),
            Mathf.Max(1, conveyorItemLiveAreaSize),
            out Vector2Int currentLiveMin,
            out Vector2Int currentLiveMaxExclusive);
        return IsWithinConveyorItemLiveAreaBounds(coordinate, currentLiveMin, currentLiveMaxExclusive);
    }

    private static bool IsWithinConveyorItemLiveAreaBounds(
        Vector2Int coordinate,
        Vector2Int liveMin,
        Vector2Int liveMaxExclusive)
    {
        return coordinate.x >= liveMin.x
               && coordinate.x < liveMaxExclusive.x
               && coordinate.y >= liveMin.y
               && coordinate.y < liveMaxExclusive.y;
    }

    private Vector2Int GetConveyorItemLiveCenterCoordinate()
    {
        if (TryGetFreeCameraLiveCenterCoordinate(out Vector2Int cameraCenterCoordinate))
        {
            return cameraCenterCoordinate;
        }

        return GetFloorObjectLiveCenterCoordinate();
    }

    private bool TryGetFreeCameraLiveCenterCoordinate(out Vector2Int centerCoordinate)
    {
        centerCoordinate = default;
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null || !gameManager.FreeCamera)
        {
            return false;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        Ray centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(centerRay, out float distance) && distance >= 0f)
        {
            centerCoordinate = GetWorldBlockCoordinate(centerRay.GetPoint(distance));
            return true;
        }

        centerCoordinate = GetWorldBlockCoordinate(camera.transform.position);
        return true;
    }

    private void ClearConveyorItemResidencyScan()
    {
        conveyorItemResidencyWorkQueue.Clear();
        conveyorItemResidencyQueuedCoordinates.Clear();
        conveyorItemLiveAreaInitialized = false;
        conveyorItemLiveMinCoordinate = default;
        conveyorItemLiveMaxExclusiveCoordinate = default;
        ResetLastConveyorItemResidencyScanStats();
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

        bool isVirtualizedFloorObject = virtualizedFloorObjectCoordinates.Contains(block.Coordinate);
        bool isVirtualizedConveyorItems = virtualizedConveyorItemCoordinates.Contains(block.Coordinate);
        if (isVirtualizedFloorObject || isVirtualizedConveyorItems)
        {
            if (block.IsRuntimeConveyor)
            {
                conveyorUnloadSaveSkippedVirtualizedBlocks++;
                conveyorUnloadSaveSkippedVirtualizedItems += isVirtualizedConveyorItems
                    ? resourceStateStore.GetSavedConveyorItemCount(block.Coordinate)
                    : block.GetRuntimeConveyorItemCount();
            }

            resourceStateStore.SetFloorObjectsResidency(block.Coordinate, VirtualObjectResidency.Virtual);
            return;
        }

        resourceStateStore.SaveFloorObjects(block.Coordinate, block, residency);
        if (block.IsRuntimeConveyor)
        {
            conveyorUnloadSaveConveyorBlocks++;
            conveyorUnloadSaveConveyorItems += block.GetRuntimeConveyorItemCount();
            resourceStateStore.SaveConveyorItems(block.Coordinate, block);
        }
        else
        {
            conveyorUnloadSaveClearedNonConveyorBlocks++;
            resourceStateStore.RemoveConveyorItems(block.Coordinate);
        }
    }
}
