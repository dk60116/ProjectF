using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class TerrainGenerator : MonoBehaviour
{
    private const int MaxConveyorSlotDotRefreshesPerFrame = 64;
    private const int MaxConveyorSlotDotInstancesPerBatch = 1023;
    private const int MaxBeltItemLineDebugRefreshesPerFrame = 128;
    private const float ConveyorSlotDotInstancedDiameter = 0.08f;
    private static readonly Color ConveyorSlotDotInstancedColor = new Color(1f, 0.36f, 0.08f, 1f);

    public void SetConveyorActive(Block block, bool isActive, bool queueWake = true)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (isActive)
        {
            if (activeConveyors.Add(block))
            {
                activeConveyorOrderDirty = true;
            }

            if (queueWake)
            {
                QueueConveyorWake(block);
            }
        }
        else
        {
            if (activeConveyors.Remove(block))
            {
                activeConveyorOrderDirty = true;
            }
        }
    }

    public void SetConveyorDataMotionActive(Block block, bool isActive)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (isActive)
        {
            activeConveyorDataMotionBlocks.Add(block);
        }
        else
        {
            activeConveyorDataMotionBlocks.Remove(block);
        }
    }

    private bool IsLoadedRuntimeBlock(Block block)
    {
        return block != null
            && block.gameObject.activeInHierarchy
            && loadedBlocks.TryGetValue(block.Coordinate, out Block loadedBlock)
            && loadedBlock == block;
    }

    private bool IsLoadedBlockReference(Block block)
    {
        return block != null
            && loadedBlocks.TryGetValue(block.Coordinate, out Block loadedBlock)
            && loadedBlock == block;
    }

    public bool IsConveyorRuntimeRefreshDeferred => deferredConveyorRuntimeRefreshDepth > 0;

    private void BeginConveyorRuntimeRefreshBatch()
    {
        deferredConveyorRuntimeRefreshDepth++;
    }

    private void EndConveyorRuntimeRefreshBatch()
    {
        if (deferredConveyorRuntimeRefreshDepth <= 0)
        {
            deferredConveyorRuntimeRefreshDepth = 0;
            return;
        }

        deferredConveyorRuntimeRefreshDepth--;
        if (deferredConveyorRuntimeRefreshDepth > 0)
        {
            return;
        }

        FlushDeferredConveyorRuntimeRefreshes();
        FlushDeferredConveyorNetworkWakes();
    }

    public void QueueDeferredConveyorRuntimeRefresh(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        deferredConveyorRuntimeRefreshBlocks.Add(block);
    }

    private void QueueDeferredConveyorNetworkWake(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        deferredConveyorNetworkWakeBlocks.Add(block);
    }

    private void FlushDeferredConveyorRuntimeRefreshes()
    {
        if (deferredConveyorRuntimeRefreshBlocks.Count == 0)
        {
            return;
        }

        conveyorTickBuffer.Clear();
        foreach (Block block in deferredConveyorRuntimeRefreshBlocks)
        {
            if (IsLoadedRuntimeBlock(block))
            {
                conveyorTickBuffer.Add(block);
            }
        }

        deferredConveyorRuntimeRefreshBlocks.Clear();

        for (int i = 0; i < conveyorTickBuffer.Count; i++)
        {
            Block block = conveyorTickBuffer[i];
            if (block == null || !IsLoadedRuntimeBlock(block))
            {
                continue;
            }

            block.RefreshConveyorActivityRegistration(false, false);
            block.RefreshConveyorSlotDotVisuals();
        }

        conveyorTickBuffer.Clear();

        if (GameManager.Instance != null && GameManager.Instance.ShowSleepAwake)
        {
            RefreshSleepAwakeRuntimeVisibility();
        }

        if (GameManager.Instance != null && GameManager.Instance.ShowBeltItemLine)
        {
            RefreshBeltItemLineRuntimeVisibility();
        }
    }

    private void FlushDeferredConveyorNetworkWakes()
    {
        if (deferredConveyorNetworkWakeBlocks.Count == 0)
        {
            return;
        }

        conveyorTickBuffer.Clear();
        foreach (Block block in deferredConveyorNetworkWakeBlocks)
        {
            if (IsLoadedBlockReference(block))
            {
                conveyorTickBuffer.Add(block);
            }
        }

        deferredConveyorNetworkWakeBlocks.Clear();
        if (conveyorTickBuffer.Count == 0)
        {
            return;
        }

        EnsureConveyorNetworkCache();
        for (int i = 0; i < conveyorTickBuffer.Count; i++)
        {
            Block block = conveyorTickBuffer[i];
            if (block == null || !IsLoadedBlockReference(block))
            {
                continue;
            }

            if (conveyorNetworkIds.TryGetValue(block, out int networkId))
            {
                conveyorNetworkRetryTimes.Remove(networkId);
                bool wasSleeping = conveyorNetworkSleepingIds.Remove(networkId);
                conveyorNetworkSleepCheckQueuedIds.Remove(networkId);
                if (wasSleeping)
                {
                    RefreshSleepAwakeDebugVisualsForNetwork(networkId);
                }
            }

            QueueConveyorWake(block);
        }

        conveyorTickBuffer.Clear();
    }

    public void QueueConveyorWake(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        int queuedLineId = -1;
        bool hasQueuedLineId = TryGetCachedNonCycleConveyorLineSlot(block, out queuedLineId, out _, out _);
        bool allowBlockWakeInsideQueuedLine = hasQueuedLineId && block.ShouldTickActiveConveyor();
        if (hasQueuedLineId
            && !allowBlockWakeInsideQueuedLine
            && !conveyorWakeQueuedLineIds.Add(queuedLineId))
        {
            return;
        }

        if (hasQueuedLineId && allowBlockWakeInsideQueuedLine)
        {
            conveyorWakeQueuedLineIds.Add(queuedLineId);
        }

        if (conveyorWakeQueued.Contains(block))
        {
            return;
        }

        conveyorWakeQueued.Add(block);
        conveyorWakeQueue.Enqueue(block);
    }

    public void SetConveyorDotVisualActive(Block block, bool isActive)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (isActive)
        {
            if (activeConveyorDotVisuals.Add(block))
            {
                activeConveyorDotVisualList.Add(block);
            }
        }
        else
        {
            if (activeConveyorDotVisuals.Remove(block))
            {
                RemoveConveyorDotVisualBlock(block);
            }
        }
    }

    private void ClearConveyorDotVisualState()
    {
        activeConveyorDotVisuals.Clear();
        activeConveyorDotVisualList.Clear();
        conveyorDotVisualTickBuffer.Clear();
        pendingConveyorSlotDotRefreshBlocks.Clear();
        pendingConveyorSlotDotRefreshIndex = 0;
    }

    private void RemoveConveyorDotVisualBlock(Block block)
    {
        int index = activeConveyorDotVisualList.IndexOf(block);
        if (index >= 0)
        {
            RemoveConveyorDotVisualAt(index);
        }
    }

    private void RemoveConveyorDotVisualAt(int index)
    {
        int lastIndex = activeConveyorDotVisualList.Count - 1;
        if (index < 0 || index > lastIndex)
        {
            return;
        }

        activeConveyorDotVisualList[index] = activeConveyorDotVisualList[lastIndex];
        activeConveyorDotVisualList.RemoveAt(lastIndex);
    }

    private void SyncConveyorSlotDotRuntimeVisibility()
    {
        bool showConveyorSlotDots = GameManager.Instance != null && GameManager.Instance.ShowConveyorSlotDots;
        if (conveyorSlotDotVisibilityInitialized && lastShowConveyorSlotDots == showConveyorSlotDots)
        {
            return;
        }

        ApplyConveyorSlotDotRuntimeVisibility(showConveyorSlotDots);
    }

    public void RefreshConveyorSlotDotRuntimeVisibility()
    {
        bool showConveyorSlotDots = GameManager.Instance != null && GameManager.Instance.ShowConveyorSlotDots;
        ApplyConveyorSlotDotRuntimeVisibility(showConveyorSlotDots);
    }

    public void RefreshSleepAwakeRuntimeVisibility()
    {
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            pair.Value?.RefreshSleepAwakeDebugVisuals(true);
        }
    }

    private void SyncBeltItemLineRuntimeVisibility()
    {
        bool showBeltItemLine = GameManager.Instance != null && GameManager.Instance.ShowBeltItemLine;
        if (beltItemLineVisibilityInitialized
            && lastShowBeltItemLine == showBeltItemLine
            && !beltItemLineVisualsDirty)
        {
            return;
        }

        ApplyBeltItemLineRuntimeVisibility(showBeltItemLine);
    }

    public void RefreshBeltItemLineRuntimeVisibility()
    {
        bool showBeltItemLine = GameManager.Instance != null && GameManager.Instance.ShowBeltItemLine;
        ApplyBeltItemLineRuntimeVisibility(showBeltItemLine);
    }

    private void ApplyBeltItemLineRuntimeVisibility(bool showBeltItemLine)
    {
        bool clearLoadedConveyors = beltItemLineVisibilityInitialized
            && lastShowBeltItemLine
            && !showBeltItemLine;

        beltItemLineVisibilityInitialized = true;
        lastShowBeltItemLine = showBeltItemLine;
        beltItemLineVisualsDirty = false;
        beltItemLineDebugCacheDirty = true;

        QueueAllBeltItemLineDebugRefreshes(clearLoadedConveyors);
    }

    public bool TryGetBeltItemLineDebugColor(Block block, int laneIndex, out Color32 color)
    {
        color = Color.white;
        if (block == null || laneIndex < 0)
        {
            return false;
        }

        EnsureBeltItemLineDebugCache();
        BeltItemLineLaneKey key = new BeltItemLineLaneKey(block, laneIndex);
        if (!beltItemLineDebugRunIds.TryGetValue(key, out int runId))
        {
            return false;
        }

        color = BeltItemLineDebugVisual.GetColor(runId);
        return true;
    }

    private void EnsureBeltItemLineDebugCache()
    {
        if (!beltItemLineDebugCacheDirty)
        {
            return;
        }

        RebuildBeltItemLineDebugCache();
    }

    private void RebuildBeltItemLineDebugCache()
    {
        beltItemLineDebugCacheDirty = false;
        beltItemLineDebugRunIds.Clear();
        beltItemLineDebugOccupiedLanes.Clear();
        beltItemLineDebugOccupiedLaneSet.Clear();
        beltItemLineDebugIncomingLanes.Clear();
        beltItemLineDebugVisitedLanes.Clear();

        if (GameManager.Instance == null || !GameManager.Instance.ShowBeltItemLine)
        {
            return;
        }

        foreach (Block block in conveyorItemVisualBlocks)
        {
            if (block == null || !block.IsRuntimeConveyor)
            {
                continue;
            }

            int laneCount = block.GetRuntimeConveyorLaneCount();
            for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
            {
                if (!block.HasRuntimeConveyorItemAtLane(laneIndex))
                {
                    continue;
                }

                BeltItemLineLaneKey key = new BeltItemLineLaneKey(block, laneIndex);
                beltItemLineDebugOccupiedLanes.Add(key);
                beltItemLineDebugOccupiedLaneSet.Add(key);
            }
        }

        for (int i = 0; i < beltItemLineDebugOccupiedLanes.Count; i++)
        {
            if (TryGetOccupiedBeltItemLineSuccessor(beltItemLineDebugOccupiedLanes[i], out BeltItemLineLaneKey successorKey))
            {
                beltItemLineDebugIncomingLanes.Add(successorKey);
            }
        }

        int nextRunId = 1;
        for (int i = 0; i < beltItemLineDebugOccupiedLanes.Count; i++)
        {
            BeltItemLineLaneKey key = beltItemLineDebugOccupiedLanes[i];
            if (beltItemLineDebugIncomingLanes.Contains(key)
                || beltItemLineDebugVisitedLanes.Contains(key))
            {
                continue;
            }

            AssignBeltItemLineDebugRun(key, nextRunId++);
        }

        for (int i = 0; i < beltItemLineDebugOccupiedLanes.Count; i++)
        {
            BeltItemLineLaneKey key = beltItemLineDebugOccupiedLanes[i];
            if (beltItemLineDebugVisitedLanes.Contains(key))
            {
                continue;
            }

            AssignBeltItemLineDebugRun(key, nextRunId++);
        }
    }

    private void AssignBeltItemLineDebugRun(BeltItemLineLaneKey startKey, int runId)
    {
        BeltItemLineLaneKey currentKey = startKey;
        while (beltItemLineDebugOccupiedLaneSet.Contains(currentKey)
            && beltItemLineDebugVisitedLanes.Add(currentKey))
        {
            beltItemLineDebugRunIds[currentKey] = runId;
            if (!TryGetOccupiedBeltItemLineSuccessor(currentKey, out currentKey))
            {
                break;
            }
        }
    }

    private bool TryGetOccupiedBeltItemLineSuccessor(
        BeltItemLineLaneKey key,
        out BeltItemLineLaneKey successorKey)
    {
        successorKey = new BeltItemLineLaneKey(null, -1);
        if (key.Block == null
            || !key.Block.TryGetRuntimeConveyorSuccessorLane(
                key.LaneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex)
            || destinationBlock == null)
        {
            return false;
        }

        successorKey = new BeltItemLineLaneKey(destinationBlock, destinationLaneIndex);
        return beltItemLineDebugOccupiedLaneSet.Contains(successorKey);
    }

    private void ClearBeltItemLineDebugCache()
    {
        beltItemLineDebugCacheDirty = true;
        beltItemLineDebugRunIds.Clear();
        beltItemLineDebugOccupiedLanes.Clear();
        beltItemLineDebugOccupiedLaneSet.Clear();
        beltItemLineDebugIncomingLanes.Clear();
        beltItemLineDebugVisitedLanes.Clear();
    }

    public void MarkBeltItemLineDebugDirty(Block block)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        InvalidateBeltItemLineDebugVisuals(block);
    }

    public void QueueBeltItemLineDebugVisualRefresh(Block block)
    {
        if (!Application.isPlaying
            || block == null
            || GameManager.Instance == null
            || !GameManager.Instance.ShowBeltItemLine)
        {
            return;
        }

        QueueBeltItemLineDebugRefresh(block);
    }

    private void InvalidateBeltItemLineDebugVisuals(Block changedBlock = null)
    {
        if (applyingBeltItemLineRuntimeVisibility)
        {
            return;
        }

        beltItemLineDebugCacheDirty = true;
        if (GameManager.Instance != null && GameManager.Instance.ShowBeltItemLine)
        {
            QueueAllBeltItemLineDebugRefreshes();
            QueueBeltItemLineDebugRefresh(changedBlock);
        }
    }

    private void QueueAllBeltItemLineDebugRefreshes(bool includeLoadedConveyors = false)
    {
        if (pendingBeltItemLineDebugRefreshAll && !includeLoadedConveyors)
        {
            return;
        }

        pendingBeltItemLineDebugRefreshAll = true;
        foreach (Block block in conveyorItemVisualBlocks)
        {
            QueueBeltItemLineDebugRefresh(block);
        }

        if (!includeLoadedConveyors)
        {
            return;
        }

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (block != null && block.IsConveyorStackingEnabled())
            {
                QueueBeltItemLineDebugRefresh(block);
            }
        }
    }

    private void QueueBeltItemLineDebugRefresh(Block block)
    {
        if (block == null || !pendingBeltItemLineDebugRefreshSet.Add(block))
        {
            return;
        }

        pendingBeltItemLineDebugRefreshBlocks.Add(block);
    }

    private void ClearPendingBeltItemLineDebugRefreshes()
    {
        pendingBeltItemLineDebugRefreshBlocks.Clear();
        pendingBeltItemLineDebugRefreshSet.Clear();
        pendingBeltItemLineDebugRefreshIndex = 0;
        pendingBeltItemLineDebugRefreshAll = false;
    }

    private void TickPendingBeltItemLineDebugRefreshes()
    {
        if (pendingBeltItemLineDebugRefreshBlocks.Count == 0)
        {
            pendingBeltItemLineDebugRefreshAll = false;
            return;
        }

        bool showBeltItemLine = GameManager.Instance != null && GameManager.Instance.ShowBeltItemLine;
        if (showBeltItemLine)
        {
            EnsureBeltItemLineDebugCache();
        }

        int processedCount = 0;
        applyingBeltItemLineRuntimeVisibility = true;
        try
        {
            while (processedCount < MaxBeltItemLineDebugRefreshesPerFrame
                && pendingBeltItemLineDebugRefreshIndex < pendingBeltItemLineDebugRefreshBlocks.Count)
            {
                Block block = pendingBeltItemLineDebugRefreshBlocks[pendingBeltItemLineDebugRefreshIndex];
                pendingBeltItemLineDebugRefreshIndex++;
                processedCount++;
                pendingBeltItemLineDebugRefreshSet.Remove(block);

                if (block == null)
                {
                    continue;
                }

                block.RefreshBeltItemLineDebugVisuals();
            }
        }
        finally
        {
            applyingBeltItemLineRuntimeVisibility = false;
        }

        if (pendingBeltItemLineDebugRefreshIndex >= pendingBeltItemLineDebugRefreshBlocks.Count)
        {
            ClearPendingBeltItemLineDebugRefreshes();
        }
    }

    private void ApplyConveyorSlotDotRuntimeVisibility(bool showConveyorSlotDots)
    {
        conveyorSlotDotVisibilityInitialized = true;
        lastShowConveyorSlotDots = showConveyorSlotDots;

        if (!showConveyorSlotDots)
        {
            conveyorDotVisualTickBuffer.Clear();
            conveyorDotVisualTickBuffer.AddRange(activeConveyorDotVisualList);

            for (int i = pendingConveyorSlotDotRefreshIndex; i < pendingConveyorSlotDotRefreshBlocks.Count; i++)
            {
                Block block = pendingConveyorSlotDotRefreshBlocks[i];
                if (block != null && !activeConveyorDotVisuals.Contains(block))
                {
                    conveyorDotVisualTickBuffer.Add(block);
                }
            }

            for (int i = 0; i < conveyorDotVisualTickBuffer.Count; i++)
            {
                Block block = conveyorDotVisualTickBuffer[i];
                if (block != null)
                {
                    block.RefreshConveyorSlotDotVisuals();
                }
            }

            ClearConveyorDotVisualState();
            return;
        }

        ClearConveyorDotVisualState();

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (block != null && block.IsConveyorStackingEnabled())
            {
                pendingConveyorSlotDotRefreshBlocks.Add(block);
            }
        }
    }

    private void TickPendingConveyorSlotDotRefreshes()
    {
        if (pendingConveyorSlotDotRefreshBlocks.Count == 0
            || GameManager.Instance == null
            || !GameManager.Instance.ShowConveyorSlotDots)
        {
            return;
        }

        int processedCount = 0;
        while (processedCount < MaxConveyorSlotDotRefreshesPerFrame
            && pendingConveyorSlotDotRefreshIndex < pendingConveyorSlotDotRefreshBlocks.Count)
        {
            Block block = pendingConveyorSlotDotRefreshBlocks[pendingConveyorSlotDotRefreshIndex];
            pendingConveyorSlotDotRefreshIndex++;
            processedCount++;

            if (block == null || !block.IsConveyorStackingEnabled())
            {
                continue;
            }

            block.RefreshConveyorSlotDotVisuals();
        }

        if (pendingConveyorSlotDotRefreshIndex >= pendingConveyorSlotDotRefreshBlocks.Count)
        {
            pendingConveyorSlotDotRefreshBlocks.Clear();
            pendingConveyorSlotDotRefreshIndex = 0;
        }
    }

    public void SetConveyorItemVisualActive(Block block, bool isActive)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (isActive)
        {
            if (conveyorItemVisualBlocks.Add(block))
            {
                conveyorItemVisualDirtyBlocks.Add(block);
                conveyorItemVisualBlockSetVersion++;
                InvalidateBeltItemLineDebugVisuals(block);
            }
        }
        else
        {
            if (conveyorItemVisualBlocks.Remove(block))
            {
                conveyorItemVisualDirtyBlocks.Add(block);
                conveyorItemVisualBlockSetVersion++;
                InvalidateBeltItemLineDebugVisuals(block);
            }
        }
    }

    public void MarkConveyorItemVisualDirty(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (conveyorItemVisualBlocks.Contains(block) && block.HasDynamicVirtualConveyorItemVisuals())
        {
            return;
        }

        conveyorItemVisualDirtyBlocks.Add(block);
    }

    public void MarkConveyorNetworkDirty()
    {
        conveyorNetworkCacheDirty = true;
        conveyorLineCacheDirty = true;
        conveyorNetworkRetryTimes.Clear();
        conveyorNetworkSleepingIds.Clear();
        conveyorNetworkActiveIds.Clear();
        conveyorNetworkSleepCheckQueuedIds.Clear();
        conveyorNetworkSleepCheckBuffer.Clear();
        InvalidateBeltItemLineDebugVisuals();
    }

    public int ConveyorLineCount
    {
        get
        {
            EnsureConveyorLineCache();
            return conveyorLines.Count;
        }
    }

    public bool TryGetConveyorLineSlot(
        Block block,
        out int lineId,
        out int slotIndex,
        out int lineLength,
        out bool isCycle)
    {
        lineId = -1;
        slotIndex = -1;
        lineLength = 0;
        isCycle = false;

        if (block == null)
        {
            return false;
        }

        EnsureConveyorLineCache();
        if (!conveyorLineSlots.TryGetValue(block, out ConveyorLineSlot slot))
        {
            return false;
        }

        lineId = slot.LineId;
        slotIndex = slot.SlotIndex;
        lineLength = slot.LineLength;
        isCycle = slot.IsCycle;
        return true;
    }

    private bool TryGetCachedNonCycleConveyorLineSlot(
        Block block,
        out int lineId,
        out int slotIndex,
        out int lineLength)
    {
        lineId = -1;
        slotIndex = -1;
        lineLength = 0;

        if (block == null
            || conveyorLineCacheDirty
            || !conveyorLineSlots.TryGetValue(block, out ConveyorLineSlot slot)
            || slot.IsCycle)
        {
            return false;
        }

        lineId = slot.LineId;
        slotIndex = slot.SlotIndex;
        lineLength = slot.LineLength;
        return true;
    }

    public void CopyConveyorLineBlocks(int lineId, List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        EnsureConveyorLineCache();
        for (int i = 0; i < conveyorLines.Count; i++)
        {
            ConveyorLine line = conveyorLines[i];
            if (line == null || line.id != lineId)
            {
                continue;
            }

            for (int blockIndex = 0; blockIndex < line.blocks.Count; blockIndex++)
            {
                results.Add(line.blocks[blockIndex]);
            }

            return;
        }
    }

    public bool IsConveyorNetworkMoveThrottled(Block block)
    {
        if (block == null)
        {
            return false;
        }

        if (IsConveyorRuntimeRefreshDeferred)
        {
            return false;
        }

        EnsureConveyorNetworkCache();
        return conveyorNetworkIds.TryGetValue(block, out int networkId)
            && (conveyorNetworkSleepingIds.Contains(networkId)
                || (conveyorNetworkRetryTimes.TryGetValue(networkId, out float retryTime)
                    && retryTime > 0f
                    && Time.time < retryTime));
    }

    public bool IsConveyorNetworkSleeping(Block block)
    {
        if (block == null)
        {
            return false;
        }

        if (IsConveyorRuntimeRefreshDeferred)
        {
            return false;
        }

        EnsureConveyorNetworkCache();
        return conveyorNetworkIds.TryGetValue(block, out int networkId)
            && conveyorNetworkSleepingIds.Contains(networkId);
    }

    public void DelayConveyorNetwork(Block block, float delay)
    {
        if (block == null)
        {
            return;
        }

        if (IsConveyorRuntimeRefreshDeferred)
        {
            return;
        }

        EnsureConveyorNetworkCache();
        if (!conveyorNetworkIds.TryGetValue(block, out int networkId))
        {
            return;
        }

        float retryTime = Time.time + Mathf.Max(0f, delay);
        if (!conveyorNetworkRetryTimes.TryGetValue(networkId, out float currentRetryTime)
            || retryTime > currentRetryTime)
        {
            conveyorNetworkRetryTimes[networkId] = retryTime;
        }
    }

    public void WakeConveyorNetwork(Block block)
    {
        if (block == null)
        {
            return;
        }

        if (IsConveyorRuntimeRefreshDeferred)
        {
            QueueDeferredConveyorNetworkWake(block);
            return;
        }

        EnsureConveyorNetworkCache();
        if (conveyorNetworkIds.TryGetValue(block, out int networkId))
        {
            conveyorNetworkRetryTimes.Remove(networkId);
            bool wasSleeping = conveyorNetworkSleepingIds.Remove(networkId);
            conveyorNetworkSleepCheckQueuedIds.Remove(networkId);
            if (wasSleeping)
            {
                RefreshSleepAwakeDebugVisualsForNetwork(networkId);
            }
        }

        QueueConveyorWake(block);
    }

    public void QueueConveyorNetworkSleepCheck(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (IsConveyorRuntimeRefreshDeferred)
        {
            return;
        }

        EnsureConveyorNetworkCache();
        if (conveyorNetworkIds.TryGetValue(block, out int networkId)
            && !conveyorNetworkSleepingIds.Contains(networkId))
        {
            conveyorNetworkSleepCheckQueuedIds.Add(networkId);
        }
    }

    private void ProcessQueuedConveyorNetworkSleepChecks()
    {
        if (conveyorNetworkSleepCheckQueuedIds.Count == 0)
        {
            return;
        }

        EnsureConveyorNetworkCache();
        conveyorNetworkSleepCheckBuffer.Clear();
        foreach (int networkId in conveyorNetworkSleepCheckQueuedIds)
        {
            conveyorNetworkSleepCheckBuffer.Add(networkId);
        }

        conveyorNetworkSleepCheckQueuedIds.Clear();
        for (int i = 0; i < conveyorNetworkSleepCheckBuffer.Count; i++)
        {
            TrySleepConveyorNetworkIfIdle(conveyorNetworkSleepCheckBuffer[i]);
        }

        conveyorNetworkSleepCheckBuffer.Clear();
    }

    private void TrySleepConveyorNetworkIfIdle(int networkId)
    {
        if (networkId <= 0 || conveyorNetworkSleepingIds.Contains(networkId))
        {
            return;
        }

        bool hasWork = false;
        foreach (KeyValuePair<Block, int> pair in conveyorNetworkIds)
        {
            if (pair.Value != networkId)
            {
                continue;
            }

            Block block = pair.Key;
            if (block != null && block.HasConveyorWorkIgnoringNetworkThrottle())
            {
                hasWork = true;
                break;
            }
        }

        if (hasWork)
        {
            return;
        }

        conveyorNetworkRetryTimes.Remove(networkId);
        conveyorNetworkSleepingIds.Add(networkId);
        foreach (KeyValuePair<Block, int> pair in conveyorNetworkIds)
        {
            if (pair.Value == networkId)
            {
                pair.Key?.RefreshConveyorActivityRegistration(false);
            }
        }
    }

    private void RefreshSleepAwakeDebugVisualsForNetwork(int networkId)
    {
        if (networkId <= 0)
        {
            return;
        }

        foreach (KeyValuePair<Block, int> pair in conveyorNetworkIds)
        {
            if (pair.Value == networkId)
            {
                pair.Key?.RefreshSleepAwakeDebugVisuals(true);
            }
        }
    }

    private void TickActiveConveyorDataMotions(float deltaTime)
    {
        if (deltaTime <= 0f || activeConveyorDataMotionBlocks.Count == 0)
        {
            return;
        }

        conveyorDataMotionTickBuffer.Clear();
        foreach (Block block in activeConveyorDataMotionBlocks)
        {
            if (IsLoadedRuntimeBlock(block))
            {
                conveyorDataMotionTickBuffer.Add(block);
            }
        }

        if (conveyorDataMotionTickBuffer.Count != activeConveyorDataMotionBlocks.Count)
        {
            activeConveyorDataMotionBlocks.RemoveWhere(block => !IsLoadedRuntimeBlock(block));
        }

        for (int i = 0; i < conveyorDataMotionTickBuffer.Count; i++)
        {
            Block block = conveyorDataMotionTickBuffer[i];
            if (block == null || !IsLoadedRuntimeBlock(block))
            {
                activeConveyors.Remove(block);
                continue;
            }

            if (!block.HasActiveVirtualConveyorDataMotion())
            {
                activeConveyorDataMotionBlocks.Remove(block);
                block.RefreshConveyorActivityRegistration();
                continue;
            }

            block.TickVirtualConveyorDataMotion(deltaTime);
            if (block.HasActiveVirtualConveyorDataMotion())
            {
                continue;
            }

            activeConveyorDataMotionBlocks.Remove(block);
            block.RefreshConveyorActivityRegistration();
            if (activeConveyors.Contains(block) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorWake(block);
            }
        }
    }

    private void TickActiveConveyors(float deltaTime)
    {
        if (deltaTime <= 0f
            || (activeConveyors.Count == 0
                && conveyorWakeQueue.Count == 0
                && conveyorNetworkSleepCheckQueuedIds.Count == 0))
        {
            return;
        }

        if (IsConveyorRuntimeRefreshDeferred)
        {
            return;
        }

        EnsureConveyorLineCache();
        conveyorLinesTickedThisFrame.Clear();
        MaybeEnqueueActiveConveyorSafetyScan();
        if (conveyorWakeQueue.Count == 0)
        {
            ProcessQueuedConveyorNetworkSleepChecks();
            return;
        }

        int queuedAtFrameStart = conveyorWakeQueue.Count;
        int processLimit = Mathf.Max(1, conveyorWakeQueueProcessLimit);
        int processedCount = 0;
        while (conveyorWakeQueue.Count > 0 && processedCount < queuedAtFrameStart && processedCount < processLimit)
        {
            Block block = conveyorWakeQueue.Dequeue();
            conveyorWakeQueued.Remove(block);
            if (TryGetCachedNonCycleConveyorLineSlot(block, out int queuedLineId, out _, out _))
            {
                conveyorWakeQueuedLineIds.Remove(queuedLineId);
            }

            processedCount++;
            if (block == null)
            {
                continue;
            }

            if (!block.ShouldTickActiveConveyor())
            {
                continue;
            }

            if (!activeConveyors.Contains(block))
            {
                SetConveyorActive(block, true, false);
            }

            if (TryTickStraightConveyorLine(block))
            {
                continue;
            }

            block.TickConveyor(deltaTime);
            if (activeConveyors.Contains(block) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorWake(block);
            }
        }

        ProcessQueuedConveyorNetworkSleepChecks();
    }

    private void MaybeEnqueueActiveConveyorSafetyScan()
    {
        if (activeConveyors.Count == 0 || Time.time < nextConveyorActiveFullScanTime)
        {
            return;
        }

        nextConveyorActiveFullScanTime = Time.time + Mathf.Max(0.02f, conveyorActiveFullScanInterval);
        EnsureSortedActiveConveyors();
        for (int i = 0; i < sortedActiveConveyors.Count; i++)
        {
            Block block = sortedActiveConveyors[i];
            if (IsLoadedRuntimeBlock(block) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorWake(block);
            }
        }
    }

    private bool TryTickStraightConveyorLine(Block triggerBlock)
    {
        if (triggerBlock == null
            || !TryGetConveyorLineSlot(triggerBlock, out int lineId, out _, out _, out bool isCycle)
            || isCycle)
        {
            return false;
        }

        if (conveyorLinesTickedThisFrame.Contains(lineId))
        {
            return true;
        }

        ConveyorLine line = FindConveyorLine(lineId);
        if (line == null || line.blocks.Count == 0 || !CanTickStraightConveyorLine(line))
        {
            return false;
        }

        conveyorLinesTickedThisFrame.Add(lineId);
        conveyorLineTouchedBlocks.Clear();
        conveyorLineTouchedSet.Clear();

        bool movedAny = false;
        movedAny |= TryTickStraightConveyorLineColumn(line, 0);
        movedAny |= TryTickStraightConveyorLineColumn(line, 1);
        NotifyStraightConveyorLineTickCompleted(line);

        if (!movedAny)
        {
            return true;
        }

        bool queuedNextTick = false;
        for (int i = 0; i < conveyorLineTouchedBlocks.Count; i++)
        {
            Block block = conveyorLineTouchedBlocks[i];
            if (block == null)
            {
                continue;
            }

            block.WakeConveyorMoveAttemptsAround();
            block.RefreshConveyorActivityRegistration(false);
            if (!queuedNextTick && activeConveyors.Contains(block) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorWake(block);
                queuedNextTick = true;
            }
        }

        return true;
    }

    private void NotifyStraightConveyorLineTickCompleted(ConveyorLine line)
    {
        if (line == null)
        {
            return;
        }

        for (int i = 0; i < line.blocks.Count; i++)
        {
            line.blocks[i]?.NotifyStraightConveyorLineTickCompleted();
        }
    }

    private ConveyorLine FindConveyorLine(int lineId)
    {
        for (int i = 0; i < conveyorLines.Count; i++)
        {
            ConveyorLine line = conveyorLines[i];
            if (line != null && line.id == lineId)
            {
                return line;
            }
        }

        return null;
    }

    private static bool CanTickStraightConveyorLine(ConveyorLine line)
    {
        if (line == null || line.isCycle || !line.simulationCacheValid || line.blocks.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < line.blocks.Count; i++)
        {
            Block block = line.blocks[i];
            if (block == null || !block.CanUseStraightConveyorLineSimulationStructureOnly())
            {
                return false;
            }
        }

        return true;
    }

    private bool TryTickStraightConveyorLineColumn(ConveyorLine line, int columnIndex)
    {
        if (line == null || !line.simulationCacheValid)
        {
            return false;
        }

        bool movedAny = false;
        for (int i = line.blocks.Count - 1; i >= 0; i--)
        {
            Block block = line.blocks[i];
            int frontLaneIndex = columnIndex == 0
                ? line.frontColumn0LaneIndices[i]
                : line.frontColumn1LaneIndices[i];
            int backLaneIndex = columnIndex == 0
                ? line.backColumn0LaneIndices[i]
                : line.backColumn1LaneIndices[i];

            if (!ShouldHoldStraightConveyorLineColumnForPairedMove(line, i, columnIndex, true)
                && i < line.blocks.Count - 1
                && block.TryMoveStraightConveyorDataLaneToCached(
                    line.blocks[i + 1],
                    frontLaneIndex,
                    columnIndex == 0
                        ? line.backColumn0LaneIndices[i + 1]
                        : line.backColumn1LaneIndices[i + 1],
                    columnIndex == 0
                        ? line.nextColumn0PathLengths[i]
                        : line.nextColumn1PathLengths[i]))
            {
                MarkConveyorLineBlockTouched(block);
                MarkConveyorLineBlockTouched(line.blocks[i + 1]);
                movedAny = true;
            }
            else if (!ShouldHoldStraightConveyorLineColumnForPairedMove(line, i, columnIndex, true)
                && i == line.blocks.Count - 1
                && HasNonLineConveyorSuccessor(block)
                && block.TryAdvanceStraightConveyorLineBoundaryLane(frontLaneIndex))
            {
                MarkConveyorLineBlockTouched(block);
                movedAny = true;
            }

            if (!ShouldHoldStraightConveyorLineColumnForPairedMove(line, i, columnIndex, false)
                && block.TryMoveStraightConveyorDataLaneToCached(
                    block,
                    backLaneIndex,
                    frontLaneIndex,
                    columnIndex == 0
                        ? line.withinColumn0PathLengths[i]
                        : line.withinColumn1PathLengths[i]))
            {
                MarkConveyorLineBlockTouched(block);
                movedAny = true;
            }
        }

        return movedAny;
    }

    private static bool ShouldHoldStraightConveyorLineColumnForPairedMove(
        ConveyorLine line,
        int blockIndex,
        int columnIndex,
        bool useFrontLane)
    {
        if (line == null
            || blockIndex < 0
            || blockIndex >= line.blocks.Count)
        {
            return false;
        }

        Block block = line.blocks[blockIndex];
        if (block == null)
        {
            return false;
        }

        int pairedColumnIndex = columnIndex == 0 ? 1 : 0;
        int pairedLaneIndex;
        if (useFrontLane)
        {
            pairedLaneIndex = pairedColumnIndex == 0
                ? line.frontColumn0LaneIndices[blockIndex]
                : line.frontColumn1LaneIndices[blockIndex];
        }
        else
        {
            pairedLaneIndex = pairedColumnIndex == 0
                ? line.backColumn0LaneIndices[blockIndex]
                : line.backColumn1LaneIndices[blockIndex];
        }

        return block.HasStraightConveyorDataItemAtLane(pairedLaneIndex)
            && !CanMoveStraightConveyorLineColumn(line, blockIndex, pairedColumnIndex, useFrontLane);
    }

    private static bool CanMoveStraightConveyorLineColumn(
        ConveyorLine line,
        int blockIndex,
        int columnIndex,
        bool useFrontLane)
    {
        if (line == null
            || blockIndex < 0
            || blockIndex >= line.blocks.Count)
        {
            return false;
        }

        Block block = line.blocks[blockIndex];
        if (block == null)
        {
            return false;
        }

        int frontLaneIndex = columnIndex == 0
            ? line.frontColumn0LaneIndices[blockIndex]
            : line.frontColumn1LaneIndices[blockIndex];
        int backLaneIndex = columnIndex == 0
            ? line.backColumn0LaneIndices[blockIndex]
            : line.backColumn1LaneIndices[blockIndex];

        if (useFrontLane)
        {
            if (blockIndex < line.blocks.Count - 1)
            {
                int destinationLaneIndex = columnIndex == 0
                    ? line.backColumn0LaneIndices[blockIndex + 1]
                    : line.backColumn1LaneIndices[blockIndex + 1];
                return block.CanMoveStraightConveyorDataLaneToCached(
                    line.blocks[blockIndex + 1],
                    frontLaneIndex,
                    destinationLaneIndex);
            }

            return HasNonLineConveyorSuccessor(block)
                && block.CanAdvanceStraightConveyorLineBoundaryLane(frontLaneIndex);
        }

        return block.CanMoveStraightConveyorDataLaneToCached(
            block,
            backLaneIndex,
            frontLaneIndex);
    }

    private static bool HasNonLineConveyorSuccessor(Block block)
    {
        return block != null
            && block.TryGetRuntimeNextConveyorBlock(out Block nextBlock)
            && nextBlock != null
            && nextBlock.IsRuntimeConveyor
            && !IsRuntimeConveyorLineBlock(nextBlock);
    }

    private static bool TryGetStraightLineColumnLanes(
        Block block,
        int columnIndex,
        out int frontLaneIndex,
        out int backLaneIndex)
    {
        frontLaneIndex = -1;
        backLaneIndex = -1;
        if (block == null
            || !block.TryGetStraightConveyorLineLaneIndices(
                out int frontColumn0LaneIndex,
                out int frontColumn1LaneIndex,
                out int backColumn0LaneIndex,
                out int backColumn1LaneIndex))
        {
            return false;
        }

        if (columnIndex == 0)
        {
            frontLaneIndex = frontColumn0LaneIndex;
            backLaneIndex = backColumn0LaneIndex;
        }
        else
        {
            frontLaneIndex = frontColumn1LaneIndex;
            backLaneIndex = backColumn1LaneIndex;
        }

        return frontLaneIndex >= 0 && backLaneIndex >= 0;
    }

    private void MarkConveyorLineBlockTouched(Block block)
    {
        if (block != null && conveyorLineTouchedSet.Add(block))
        {
            conveyorLineTouchedBlocks.Add(block);
        }
    }

    private void EnsureSortedActiveConveyors()
    {
        if (!activeConveyorOrderDirty)
        {
            return;
        }

        activeConveyors.RemoveWhere(block => block == null);
        sortedActiveConveyors.Clear();
        foreach (Block block in activeConveyors)
        {
            if (block != null)
            {
                sortedActiveConveyors.Add(block);
            }
        }

        sortedActiveConveyors.Sort(CompareActiveConveyorTickOrder);
        activeConveyorOrderDirty = false;
    }

    private void EnsureConveyorNetworkCache()
    {
        if (!conveyorNetworkCacheDirty)
        {
            return;
        }

        conveyorNetworkCacheDirty = false;
        conveyorNetworkIds.Clear();
        conveyorNetworkActiveIds.Clear();
        conveyorNetworkBuildQueue.Clear();

        int nextNetworkId = 1;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block startBlock = pair.Value;
            if (startBlock == null || !startBlock.IsRuntimeConveyor || conveyorNetworkIds.ContainsKey(startBlock))
            {
                continue;
            }

            int networkId = nextNetworkId++;
            conveyorNetworkActiveIds.Add(networkId);
            conveyorNetworkIds[startBlock] = networkId;
            conveyorNetworkBuildQueue.Enqueue(startBlock);

            while (conveyorNetworkBuildQueue.Count > 0)
            {
                Block block = conveyorNetworkBuildQueue.Dequeue();
                TryAddConveyorNetworkNeighbor(block, networkId, true);
                TryAddConveyorNetworkNeighbor(block, networkId, false);
            }
        }

        List<int> staleNetworkIds = null;
        foreach (KeyValuePair<int, float> pair in conveyorNetworkRetryTimes)
        {
            if (!conveyorNetworkActiveIds.Contains(pair.Key))
            {
                staleNetworkIds ??= new List<int>();
                staleNetworkIds.Add(pair.Key);
            }
        }

        if (staleNetworkIds != null)
        {
            for (int i = 0; i < staleNetworkIds.Count; i++)
            {
                conveyorNetworkRetryTimes.Remove(staleNetworkIds[i]);
            }
        }

        RemoveStaleConveyorNetworkIds(conveyorNetworkSleepingIds);
        RemoveStaleConveyorNetworkIds(conveyorNetworkSleepCheckQueuedIds);
    }

    private void RemoveStaleConveyorNetworkIds(HashSet<int> networkIds)
    {
        if (networkIds == null || networkIds.Count == 0)
        {
            return;
        }

        conveyorNetworkSleepCheckBuffer.Clear();
        foreach (int networkId in networkIds)
        {
            if (!conveyorNetworkActiveIds.Contains(networkId))
            {
                conveyorNetworkSleepCheckBuffer.Add(networkId);
            }
        }

        for (int i = 0; i < conveyorNetworkSleepCheckBuffer.Count; i++)
        {
            networkIds.Remove(conveyorNetworkSleepCheckBuffer[i]);
        }

        conveyorNetworkSleepCheckBuffer.Clear();
    }

    private void TryAddConveyorNetworkNeighbor(Block block, int networkId, bool next)
    {
        if (block == null)
        {
            return;
        }

        bool hasNeighbor = next
            ? block.TryGetRuntimeNextConveyorBlock(out Block neighborBlock)
            : block.TryGetRuntimePreviousConveyorBlock(out neighborBlock);
        if (!hasNeighbor || neighborBlock == null || !neighborBlock.IsRuntimeConveyor || conveyorNetworkIds.ContainsKey(neighborBlock))
        {
            return;
        }

        conveyorNetworkIds[neighborBlock] = networkId;
        conveyorNetworkBuildQueue.Enqueue(neighborBlock);
    }

    private void ClearConveyorLineCache()
    {
        conveyorLines.Clear();
        conveyorLineSlots.Clear();
        conveyorLineVisited.Clear();
        conveyorLineBuildIndices.Clear();
        conveyorLinesTickedThisFrame.Clear();
        conveyorLineTouchedBlocks.Clear();
        conveyorLineTouchedSet.Clear();
        conveyorWakeQueuedLineIds.Clear();
        conveyorLineCacheDirty = true;
    }

    private void EnsureConveyorLineCache()
    {
        if (!conveyorLineCacheDirty)
        {
            return;
        }

        conveyorLineCacheDirty = false;
        conveyorLines.Clear();
        conveyorLineSlots.Clear();
        conveyorLineVisited.Clear();
        conveyorLineBuildIndices.Clear();
        conveyorLinesTickedThisFrame.Clear();
        conveyorLineTouchedBlocks.Clear();
        conveyorLineTouchedSet.Clear();

        int nextLineId = 1;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (!IsConveyorLineStartBlock(block))
            {
                continue;
            }

            if (TryBuildConveyorLine(block, nextLineId))
            {
                nextLineId++;
            }
        }

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (!IsRuntimeConveyorLineBlock(block) || conveyorLineVisited.Contains(block))
            {
                continue;
            }

            if (TryBuildConveyorLine(block, nextLineId))
            {
                nextLineId++;
            }
        }
    }

    private bool TryBuildConveyorLine(Block startBlock, int lineId)
    {
        if (!IsRuntimeConveyorLineBlock(startBlock) || conveyorLineVisited.Contains(startBlock))
        {
            return false;
        }

        ConveyorLine line = new ConveyorLine(lineId);
        conveyorLineBuildIndices.Clear();
        bool isCycle = false;
        Block currentBlock = startBlock;

        while (IsRuntimeConveyorLineBlock(currentBlock))
        {
            if (conveyorLineBuildIndices.TryGetValue(currentBlock, out int loopStartIndex))
            {
                isCycle = loopStartIndex == 0;
                break;
            }

            if (conveyorLineVisited.Contains(currentBlock))
            {
                break;
            }

            conveyorLineBuildIndices[currentBlock] = line.blocks.Count;
            conveyorLineVisited.Add(currentBlock);
            line.blocks.Add(currentBlock);

            if (!currentBlock.TryGetRuntimeNextConveyorBlock(out Block nextBlock)
                || !IsRuntimeConveyorLineBlock(nextBlock))
            {
                break;
            }

            currentBlock = nextBlock;
        }

        if (line.blocks.Count == 0)
        {
            return false;
        }

        line.isCycle = isCycle;
        line.simulationCacheValid = PopulateConveyorLineSimulationCache(line);
        conveyorLines.Add(line);

        int lineLength = line.blocks.Count;
        for (int i = 0; i < lineLength; i++)
        {
            conveyorLineSlots[line.blocks[i]] = new ConveyorLineSlot(line.id, i, lineLength, line.isCycle);
        }

        return true;
    }

    private static bool PopulateConveyorLineSimulationCache(ConveyorLine line)
    {
        if (line == null || line.blocks.Count <= 0 || line.isCycle)
        {
            return false;
        }

        int lineLength = line.blocks.Count;
        line.frontColumn0LaneIndices = new int[lineLength];
        line.frontColumn1LaneIndices = new int[lineLength];
        line.backColumn0LaneIndices = new int[lineLength];
        line.backColumn1LaneIndices = new int[lineLength];
        line.withinColumn0PathLengths = new float[lineLength];
        line.withinColumn1PathLengths = new float[lineLength];
        line.nextColumn0PathLengths = new float[lineLength];
        line.nextColumn1PathLengths = new float[lineLength];

        for (int i = 0; i < lineLength; i++)
        {
            Block block = line.blocks[i];
            Block nextBlock = i < lineLength - 1 ? line.blocks[i + 1] : null;
            if (block == null
                || !block.TryGetStraightConveyorLineMotionData(
                    nextBlock,
                    out line.frontColumn0LaneIndices[i],
                    out line.frontColumn1LaneIndices[i],
                    out line.backColumn0LaneIndices[i],
                    out line.backColumn1LaneIndices[i],
                    out line.withinColumn0PathLengths[i],
                    out line.withinColumn1PathLengths[i],
                    out line.nextColumn0PathLengths[i],
                    out line.nextColumn1PathLengths[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsConveyorLineStartBlock(Block block)
    {
        if (!IsRuntimeConveyorLineBlock(block) || conveyorLineVisited.Contains(block))
        {
            return false;
        }

        return !block.TryGetRuntimePreviousConveyorBlock(out Block previousBlock)
            || !IsRuntimeConveyorLineBlock(previousBlock);
    }

    private static bool IsRuntimeConveyorLineBlock(Block block)
    {
        return block != null
            && block.IsRuntimeConveyor
            && !block.IsCornerConveyorBlock();
    }

    private void TickActiveConveyorDotVisuals(float deltaTime)
    {
        if (deltaTime <= 0f
            || activeConveyorDotVisualList.Count == 0
            || GameManager.Instance == null
            || !GameManager.Instance.ShowConveyorSlotDots)
        {
            return;
        }

        BeginConveyorSlotDotInstancedRendering();

        int index = 0;
        while (index < activeConveyorDotVisualList.Count)
        {
            Block block = activeConveyorDotVisualList[index];
            if (block == null || !block.IsConveyorStackingEnabled())
            {
                activeConveyorDotVisuals.Remove(block);
                RemoveConveyorDotVisualAt(index);
                continue;
            }

            block.TickConveyorSlotDots(deltaTime);
            index++;
        }

        EndConveyorSlotDotInstancedRendering();
    }

    public void AddConveyorSlotDotInstance(Vector3 worldPosition)
    {
        if (conveyorSlotDotInstanceMatrixCount >= MaxConveyorSlotDotInstancesPerBatch)
        {
            FlushConveyorSlotDotInstances();
        }

        conveyorSlotDotInstanceMatrices[conveyorSlotDotInstanceMatrixCount] = Matrix4x4.TRS(
            worldPosition,
            Quaternion.identity,
            new Vector3(ConveyorSlotDotInstancedDiameter, 1f, ConveyorSlotDotInstancedDiameter));
        conveyorSlotDotInstanceMatrixCount++;
    }

    private void BeginConveyorSlotDotInstancedRendering()
    {
        conveyorSlotDotInstanceMatrixCount = 0;
    }

    private void EndConveyorSlotDotInstancedRendering()
    {
        FlushConveyorSlotDotInstances();
    }

    private void FlushConveyorSlotDotInstances()
    {
        if (conveyorSlotDotInstanceMatrixCount <= 0)
        {
            return;
        }

        EnsureConveyorSlotDotInstancedResources();
        if (conveyorSlotDotInstancedMesh == null || conveyorSlotDotInstancedMaterial == null)
        {
            conveyorSlotDotInstanceMatrixCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(
            conveyorSlotDotInstancedMesh,
            0,
            conveyorSlotDotInstancedMaterial,
            conveyorSlotDotInstanceMatrices,
            conveyorSlotDotInstanceMatrixCount,
            null,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            false,
            gameObject.layer,
            null,
            UnityEngine.Rendering.LightProbeUsage.Off,
            null);
        conveyorSlotDotInstanceMatrixCount = 0;
    }

    private void EnsureConveyorSlotDotInstancedResources()
    {
        if (conveyorSlotDotInstancedMesh == null)
        {
            conveyorSlotDotInstancedMesh = CreateConveyorSlotDotInstancedMesh();
        }

        if (conveyorSlotDotInstancedMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return;
        }

        conveyorSlotDotInstancedMaterial = new Material(shader)
        {
            enableInstancing = true,
            hideFlags = HideFlags.DontSave
        };

        if (conveyorSlotDotInstancedMaterial.HasProperty("_BaseColor"))
        {
            conveyorSlotDotInstancedMaterial.SetColor("_BaseColor", ConveyorSlotDotInstancedColor);
        }

        if (conveyorSlotDotInstancedMaterial.HasProperty("_Color"))
        {
            conveyorSlotDotInstancedMaterial.SetColor("_Color", ConveyorSlotDotInstancedColor);
        }

        if (conveyorSlotDotInstancedMaterial.HasProperty("_Cull"))
        {
            conveyorSlotDotInstancedMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }
    }

    private static Mesh CreateConveyorSlotDotInstancedMesh()
    {
        const int segmentCount = 16;
        Vector3[] vertices = new Vector3[segmentCount + 1];
        Vector3[] normals = new Vector3[segmentCount + 1];
        int[] triangles = new int[segmentCount * 3];

        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;
        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / segmentCount;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f);
            normals[i + 1] = Vector3.up;
        }

        for (int i = 0; i < segmentCount; i++)
        {
            int nextIndex = i == segmentCount - 1 ? 1 : i + 2;
            int triangleIndex = i * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = nextIndex;
            triangles[triangleIndex + 2] = i + 1;
        }

        Mesh mesh = new Mesh
        {
            name = "Conveyor Slot Dot Instanced Mesh",
            hideFlags = HideFlags.DontSave
        };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int CompareActiveConveyorTickOrder(Block left, Block right)
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

        int yComparison = left.Coordinate.y.CompareTo(right.Coordinate.y);
        if (yComparison != 0)
        {
            return yComparison;
        }

        return left.Coordinate.x.CompareTo(right.Coordinate.x);
    }
}
