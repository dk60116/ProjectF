using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    private const int MaxBeltDirectionArrowInstancesPerBatch = 1023;
    private const int BeltDirectionArrowRenderQueue = 5000;
    private const int MaxBeltItemLineDebugRefreshesPerFrame = 128;
    private const float ConveyorLineBlockedRetryInterval = 0.12f;
    private const float ConveyorLineBlockedRetryMaxInterval = 1.2f;
    private const float ConveyorLineBlockedRetryJitterStep = 0.02f;
    private const int ConveyorLineBlockedRetryJitterSteps = 4;
    private const int ConveyorLineBlockedRetryMaxBackoffExponent = 4;
    private const float ConveyorSlotDotInstancedDiameter = 0.08f;
    private static readonly Color ConveyorSlotDotInstancedColor = new Color(1f, 0.36f, 0.08f, 1f);
    private static readonly Color BeltDirectionArrowInstancedColor = new Color(1f, 0.92f, 0.08f, 1f);

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
            AddActiveConveyorDataMotionBlock(block);
        }
        else
        {
            RemoveActiveConveyorDataMotionBlock(block);
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

    private void ClearConveyorRuntimeState()
    {
        virtualConveyorBeltRenderer?.Clear();
        ConvayorBelt2F.ClearRuntimeCoverageLookup();
        activeConveyors.Clear();
        conveyorTickBuffer.Clear();
        activeConveyorDataMotionBlocks.Clear();
        activeConveyorDataMotionIndices.Clear();
        activeConveyorDataMotionDueTimes.Clear();
        sortedActiveConveyors.Clear();
        activeConveyorOrderDirty = true;
        conveyorNetworkIds.Clear();
        conveyorNetworkBlocksById.Clear();
        conveyorNetworkRetryTimes.Clear();
        conveyorNetworkSleepingIds.Clear();
        conveyorNetworkActiveIds.Clear();
        conveyorNetworkSleepCheckQueuedIds.Clear();
        conveyorNetworkSleepCheckBuffer.Clear();
        conveyorNetworkBuildQueue.Clear();
        conveyorWakeQueue.Clear();
        conveyorLineWakeQueue.Clear();
        conveyorWakeQueued.Clear();
        conveyorDirectWakeBlocks.Clear();
        conveyorLineWakeRangesById.Clear();
        deferredConveyorLineWakeQueue.Clear();
        deferredConveyorLineWakeRangesById.Clear();
        ClearStraightConveyorLineRetries();
        deferredConveyorRuntimeRefreshBlocks.Clear();
        deferredConveyorNetworkWakeBlocks.Clear();
        deferredConveyorMoveAttemptWakeAroundBlocks.Clear();
        deferredConveyorRuntimeRefreshDepth = 0;
        conveyorNetworkCacheDirty = true;
        ClearConveyorLineCache();
        nextConveyorActiveFullScanTime = 0f;
        activeConveyorSafetyScanIndex = 0;
        ClearConveyorDotVisualState();
        ClearBeltDirectionVisualState();
        conveyorSlotDotVisibilityInitialized = false;
        lastShowConveyorSlotDots = false;
        beltItemLineVisibilityInitialized = false;
        lastShowBeltItemLine = false;
        beltDirectionVisibilityInitialized = false;
        lastShowBeltDirections = false;
        beltItemLineVisualsDirty = false;
        ClearBeltItemLineDebugCache();
        ClearPendingBeltItemLineDebugRefreshes();
        ClearConveyorItemVisualTracking();
    }

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

        FlushDeferredConveyorMoveAttemptWakeArounds();
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

    public void QueueDeferredConveyorMoveAttemptWakeAround(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        deferredConveyorMoveAttemptWakeAroundBlocks.Add(block);
    }

    public void WakeAndRefreshConveyorRuntimeBlocks(
        IList<Block> blocks,
        bool queueWake = true,
        bool refreshDebugVisuals = true)
    {
        if (!Application.isPlaying || blocks == null || blocks.Count == 0)
        {
            return;
        }

        BeginConveyorRuntimeRefreshBatch();
        try
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                Block block = blocks[i];
                if (block == null)
                {
                    continue;
                }

                block.WakeConveyorMoveAttemptsAround();
                block.RefreshConveyorActivityRegistration(queueWake, refreshDebugVisuals);
            }
        }
        finally
        {
            EndConveyorRuntimeRefreshBatch();
        }
    }

    private void FlushDeferredConveyorMoveAttemptWakeArounds()
    {
        if (deferredConveyorMoveAttemptWakeAroundBlocks.Count == 0)
        {
            return;
        }

        conveyorTickBuffer.Clear();
        foreach (Block block in deferredConveyorMoveAttemptWakeAroundBlocks)
        {
            if (IsLoadedBlockReference(block))
            {
                conveyorTickBuffer.Add(block);
            }
        }

        deferredConveyorMoveAttemptWakeAroundBlocks.Clear();
        deferredConveyorRuntimeRefreshDepth++;
        try
        {
            for (int i = 0; i < conveyorTickBuffer.Count; i++)
            {
                conveyorTickBuffer[i]?.WakeConveyorMoveAttemptsAroundImmediate();
            }
        }
        finally
        {
            deferredConveyorRuntimeRefreshDepth--;
        }

        conveyorTickBuffer.Clear();
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
            block.RefreshBeltDirectionDebugVisuals();
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

        if (GameManager.Instance != null && GameManager.Instance.ShowDirections)
        {
            RefreshBeltDirectionRuntimeVisibility();
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

        if (TryGetCachedNonCycleConveyorLineSlot(block, out int queuedLineId, out _, out _))
        {
            ClearStraightConveyorLineRetry(queuedLineId);
            QueueConveyorLineWake(queuedLineId);
            return;
        }

        if (conveyorWakeQueued.Contains(block))
        {
            return;
        }

        conveyorWakeQueued.Add(block);
        conveyorWakeQueue.Enqueue(block);
    }

    private void QueueConveyorSafetyWake(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (TryGetCachedNonCycleConveyorLineSlot(block, out int queuedLineId, out _, out _))
        {
            QueueConveyorLineWake(queuedLineId);
            return;
        }

        if (conveyorWakeQueued.Contains(block))
        {
            return;
        }

        conveyorWakeQueued.Add(block);
        conveyorWakeQueue.Enqueue(block);
    }

    public void QueueConveyorDirectWakeAround(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        QueueConveyorDirectWake(block);
        Vector2Int coordinate = block.Coordinate;
        QueueConveyorDirectWakeAt(coordinate + Vector2Int.up);
        QueueConveyorDirectWakeAt(coordinate + Vector2Int.right);
        QueueConveyorDirectWakeAt(coordinate + Vector2Int.down);
        QueueConveyorDirectWakeAt(coordinate + Vector2Int.left);
    }

    private void QueueConveyorDirectWakeAt(Vector2Int coordinate)
    {
        if (!TryGetLoadedBlock(coordinate, out Block block) || block == null)
        {
            return;
        }

        QueueConveyorDirectWake(block);
    }

    private void QueueConveyorDirectWake(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        conveyorDirectWakeBlocks.Add(block);
        ClearStraightConveyorLineRetry(block);
        if (conveyorWakeQueued.Contains(block))
        {
            return;
        }

        conveyorWakeQueued.Add(block);
        conveyorWakeQueue.Enqueue(block);
    }

    private void QueueConveyorLineWake(int lineId)
    {
        QueueConveyorLineWake(lineId, new ConveyorLineWakeRange(0, int.MaxValue, true));
    }

    private void QueueConveyorLineWake(int lineId, ConveyorLineWakeRange wakeRange)
    {
        if (lineId <= 0)
        {
            return;
        }

        if (IsStraightConveyorLineWakeThrottled(lineId, wakeRange))
        {
            return;
        }

        if (conveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange existingRange))
        {
            existingRange.Include(wakeRange);
            conveyorLineWakeRangesById[lineId] = existingRange;
            return;
        }

        conveyorLineWakeRangesById[lineId] = wakeRange;
        conveyorLineWakeQueue.Enqueue(lineId);
    }

    private void DeferConveyorLineWake(int lineId, ConveyorLineWakeRange wakeRange)
    {
        if (lineId <= 0)
        {
            return;
        }

        if (deferredConveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange existingRange))
        {
            existingRange.Include(wakeRange);
            deferredConveyorLineWakeRangesById[lineId] = existingRange;
            return;
        }

        deferredConveyorLineWakeRangesById[lineId] = wakeRange;
        deferredConveyorLineWakeQueue.Enqueue(lineId);
    }

    private void PromoteDeferredConveyorLineWakes()
    {
        int deferredCount = deferredConveyorLineWakeQueue.Count;
        for (int i = 0; i < deferredCount; i++)
        {
            int lineId = deferredConveyorLineWakeQueue.Dequeue();
            if (!deferredConveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange wakeRange))
            {
                continue;
            }

            deferredConveyorLineWakeRangesById.Remove(lineId);
            QueueConveyorLineWake(lineId, wakeRange);
        }
    }

    private bool IsStraightConveyorLineWakeThrottled(int lineId, ConveyorLineWakeRange wakeRange)
    {
        if (!conveyorLineRetryStatesById.TryGetValue(lineId, out ConveyorLineRetryState retryState))
        {
            return false;
        }

        if (Time.time >= retryState.retryTime)
        {
            conveyorLineRetryStatesById.Remove(lineId);
            conveyorLineRetryAttemptsByDueLineId[lineId] = retryState.attemptCount;
            return false;
        }

        ConveyorLineWakeRange retryRange = retryState.wakeRange;
        if (retryRange.fullLine)
        {
            return true;
        }

        if (wakeRange.fullLine)
        {
            return false;
        }

        return wakeRange.minSlotIndex >= retryRange.minSlotIndex
            && wakeRange.maxSlotIndex <= retryRange.maxSlotIndex;
    }

    private void DelayStraightConveyorLineRetry(int lineId, int minSlotIndex, int maxSlotIndex)
    {
        if (lineId <= 0 || maxSlotIndex < minSlotIndex)
        {
            return;
        }

        ConveyorLineWakeRange wakeRange = new ConveyorLineWakeRange(minSlotIndex, maxSlotIndex, false);
        int previousAttemptCount = 0;
        if (conveyorLineRetryStatesById.TryGetValue(lineId, out ConveyorLineRetryState retryState))
        {
            previousAttemptCount = retryState.attemptCount;
            if (Time.time < retryState.retryTime)
            {
                int mergedAttemptCount = Mathf.Max(1, previousAttemptCount);
                float mergedRetryTime = Time.time + GetStraightConveyorLineBlockedRetryDelay(lineId, minSlotIndex, maxSlotIndex, mergedAttemptCount);
                retryState.wakeRange.Include(wakeRange);
                retryState.retryTime = Mathf.Max(retryState.retryTime, mergedRetryTime);
                retryState.attemptCount = mergedAttemptCount;
                conveyorLineRetryStatesById[lineId] = retryState;
                TrackNextStraightConveyorLineRetryTime(retryState.retryTime);
                return;
            }
        }
        else if (conveyorLineRetryAttemptsByDueLineId.TryGetValue(lineId, out int dueAttemptCount))
        {
            previousAttemptCount = dueAttemptCount;
        }

        conveyorLineRetryAttemptsByDueLineId.Remove(lineId);
        int attemptCount = Mathf.Max(0, previousAttemptCount) + 1;
        float retryTime = Time.time + GetStraightConveyorLineBlockedRetryDelay(lineId, minSlotIndex, maxSlotIndex, attemptCount);

        conveyorLineRetryStatesById[lineId] = new ConveyorLineRetryState(wakeRange, retryTime, attemptCount);
        TrackNextStraightConveyorLineRetryTime(retryTime);
    }

    private void ClearStraightConveyorLineRetry(int lineId)
    {
        if (lineId <= 0)
        {
            return;
        }

        conveyorLineRetryAttemptsByDueLineId.Remove(lineId);
        if (conveyorLineRetryStatesById.Remove(lineId) && conveyorLineRetryStatesById.Count == 0)
        {
            nextConveyorLineRetryTime = float.PositiveInfinity;
        }
    }

    private void ClearStraightConveyorLineRetry(Block block)
    {
        if (TryGetCachedNonCycleConveyorLineSlot(block, out int lineId, out _, out _))
        {
            ClearStraightConveyorLineRetry(lineId);
        }
    }

    private static float GetStraightConveyorLineBlockedRetryDelay(int lineId, int minSlotIndex, int maxSlotIndex, int attemptCount)
    {
        uint hash = unchecked((uint)((lineId * 73856093) ^ (minSlotIndex * 19349663) ^ (maxSlotIndex * 83492791)));
        float jitter = (hash % ConveyorLineBlockedRetryJitterSteps) * ConveyorLineBlockedRetryJitterStep;
        int exponent = Mathf.Clamp(attemptCount - 1, 0, ConveyorLineBlockedRetryMaxBackoffExponent);
        float retryInterval = ConveyorLineBlockedRetryInterval * (1 << exponent);
        return Mathf.Min(ConveyorLineBlockedRetryMaxInterval, retryInterval + jitter);
    }

    private bool HasDueStraightConveyorLineRetry()
    {
        return conveyorLineRetryStatesById.Count > 0
            && Time.time + 0.0001f >= nextConveyorLineRetryTime;
    }

    private void TrackNextStraightConveyorLineRetryTime(float retryTime)
    {
        if (float.IsNaN(retryTime) || float.IsInfinity(retryTime))
        {
            return;
        }

        nextConveyorLineRetryTime = Mathf.Min(nextConveyorLineRetryTime, retryTime);
    }

    private void ProcessDueStraightConveyorLineRetries()
    {
        if (conveyorLineRetryStatesById.Count == 0)
        {
            nextConveyorLineRetryTime = float.PositiveInfinity;
            return;
        }

        float now = Time.time;
        if (now + 0.0001f < nextConveyorLineRetryTime)
        {
            return;
        }

        conveyorLineRetryDueIds.Clear();
        float nextRetryTime = float.PositiveInfinity;
        foreach (KeyValuePair<int, ConveyorLineRetryState> pair in conveyorLineRetryStatesById)
        {
            ConveyorLineRetryState retryState = pair.Value;
            if (retryState.retryTime <= now + 0.0001f)
            {
                conveyorLineRetryDueIds.Add(pair.Key);
                continue;
            }

            nextRetryTime = Mathf.Min(nextRetryTime, retryState.retryTime);
        }

        for (int i = 0; i < conveyorLineRetryDueIds.Count; i++)
        {
            int lineId = conveyorLineRetryDueIds[i];
            if (!conveyorLineRetryStatesById.TryGetValue(lineId, out ConveyorLineRetryState retryState))
            {
                continue;
            }

            conveyorLineRetryStatesById.Remove(lineId);
            conveyorLineRetryAttemptsByDueLineId[lineId] = retryState.attemptCount;
            QueueConveyorLineWake(lineId, retryState.wakeRange);
        }

        conveyorLineRetryDueIds.Clear();
        nextConveyorLineRetryTime = conveyorLineRetryStatesById.Count > 0
            ? nextRetryTime
            : float.PositiveInfinity;
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

    public void SetBeltDirectionVisualActive(Block block, bool isActive)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (isActive)
        {
            if (activeBeltDirectionVisuals.Add(block))
            {
                activeBeltDirectionVisualList.Add(block);
            }
        }
        else
        {
            if (activeBeltDirectionVisuals.Remove(block))
            {
                RemoveBeltDirectionVisualBlock(block);
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

    private void ClearBeltDirectionVisualState()
    {
        activeBeltDirectionVisuals.Clear();
        activeBeltDirectionVisualList.Clear();
        beltDirectionArrowInstanceMatrixCount = 0;
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

    private void RemoveBeltDirectionVisualBlock(Block block)
    {
        int index = activeBeltDirectionVisualList.IndexOf(block);
        if (index >= 0)
        {
            RemoveBeltDirectionVisualAt(index);
        }
    }

    private void RemoveBeltDirectionVisualAt(int index)
    {
        int lastIndex = activeBeltDirectionVisualList.Count - 1;
        if (index < 0 || index > lastIndex)
        {
            return;
        }

        activeBeltDirectionVisualList[index] = activeBeltDirectionVisualList[lastIndex];
        activeBeltDirectionVisualList.RemoveAt(lastIndex);
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

        RobotArm.RefreshAllSleepAwakeDebugVisuals();
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

    private void SyncBeltDirectionRuntimeVisibility()
    {
        bool showBeltDirections = GameManager.Instance != null && GameManager.Instance.ShowDirections;
        if (beltDirectionVisibilityInitialized && lastShowBeltDirections == showBeltDirections)
        {
            return;
        }

        ApplyBeltDirectionRuntimeVisibility(showBeltDirections);
    }

    public void RefreshBeltDirectionRuntimeVisibility()
    {
        bool showBeltDirections = GameManager.Instance != null && GameManager.Instance.ShowDirections;
        ApplyBeltDirectionRuntimeVisibility(showBeltDirections);
    }

    private void ApplyBeltDirectionRuntimeVisibility(bool showBeltDirections)
    {
        beltDirectionVisibilityInitialized = true;
        lastShowBeltDirections = showBeltDirections;
        ClearBeltDirectionVisualState();
        if (!showBeltDirections)
        {
            return;
        }

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            pair.Value?.RefreshBeltDirectionDebugVisuals();
        }
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
            TrackConveyorItemVisualBlock(block);
        }
        else
        {
            UntrackConveyorItemVisualBlock(block);
        }
    }

    public void MarkConveyorItemVisualDirty(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        bool isTracked = conveyorItemVisualBlocks.Contains(block);
        if (isTracked)
        {
            CacheConveyorBlockItemCount(block, CaptureConveyorBlockItemCount(block));
            bool hasDynamicVisuals = block.HasDynamicVirtualConveyorItemVisuals();
            SetDynamicConveyorItemVisualBlockTracked(block, hasDynamicVisuals);
            if (hasDynamicVisuals)
            {
                return;
            }
        }

        conveyorItemVisualDirtyBlocks.Add(block);
    }

    public void RefreshBeltItemRenderingVisibility()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        foreach (Block block in conveyorItemVisualBlocks)
        {
            if (block == null)
            {
                continue;
            }

            block.RefreshConveyorObjectRenderingMode();
            conveyorItemVisualDirtyBlocks.Add(block);
        }

        conveyorItemVisualBlockSetVersion++;
        dynamicConveyorItemVisualBlockSetVersion++;
    }

    private void TrackConveyorItemVisualBlock(Block block)
    {
        CacheConveyorBlockItemCount(block, CaptureConveyorBlockItemCount(block));
        bool added = conveyorItemVisualBlocks.Add(block);
        SetDynamicConveyorItemVisualBlockTracked(block, block.HasDynamicVirtualConveyorItemVisuals());
        if (!added)
        {
            return;
        }

        conveyorItemVisualDirtyBlocks.Add(block);
        conveyorItemVisualBlockSetVersion++;
        InvalidateBeltItemLineDebugVisuals(block);
    }

    private void UntrackConveyorItemVisualBlock(Block block)
    {
        SetDynamicConveyorItemVisualBlockTracked(block, false);
        RemoveCachedConveyorBlockItemCount(block);
        if (!conveyorItemVisualBlocks.Remove(block))
        {
            return;
        }

        conveyorItemVisualDirtyBlocks.Add(block);
        conveyorItemVisualBlockSetVersion++;
        InvalidateBeltItemLineDebugVisuals(block);
    }

    private void ClearConveyorItemVisualTracking()
    {
        conveyorItemVisualBlocks.Clear();
        conveyorItemVisualDirtyBlocks.Clear();
        dynamicConveyorItemVisualBlocks.Clear();
        dynamicConveyorItemVisualBlockIndices.Clear();
        conveyorItemCountsByBlock.Clear();
        cachedLoadedConveyorItemCount = 0;
        conveyorItemVisualBlockSetVersion++;
        dynamicConveyorItemVisualBlockSetVersion++;
    }

    private void SetDynamicConveyorItemVisualBlockTracked(Block block, bool isTracked)
    {
        if (block == null)
        {
            return;
        }

        if (isTracked)
        {
            if (dynamicConveyorItemVisualBlockIndices.ContainsKey(block))
            {
                return;
            }

            dynamicConveyorItemVisualBlockIndices.Add(block, dynamicConveyorItemVisualBlocks.Count);
            dynamicConveyorItemVisualBlocks.Add(block);
            dynamicConveyorItemVisualBlockSetVersion++;
            return;
        }

        if (!dynamicConveyorItemVisualBlockIndices.TryGetValue(block, out int index))
        {
            return;
        }

        int lastIndex = dynamicConveyorItemVisualBlocks.Count - 1;
        Block lastBlock = dynamicConveyorItemVisualBlocks[lastIndex];
        dynamicConveyorItemVisualBlocks[index] = lastBlock;
        dynamicConveyorItemVisualBlockIndices[lastBlock] = index;
        dynamicConveyorItemVisualBlocks.RemoveAt(lastIndex);
        dynamicConveyorItemVisualBlockIndices.Remove(block);
        dynamicConveyorItemVisualBlockSetVersion++;
    }

    private int CaptureConveyorBlockItemCount(Block block)
    {
        return block != null && block.IsRuntimeConveyor
            ? block.GetRuntimeConveyorItemCount()
            : 0;
    }

    private void CacheConveyorBlockItemCount(Block block, int itemCount)
    {
        if (ReferenceEquals(block, null))
        {
            return;
        }

        int clampedItemCount = Mathf.Max(0, itemCount);
        conveyorItemCountsByBlock.TryGetValue(block, out int previousItemCount);
        if (previousItemCount == clampedItemCount && conveyorItemCountsByBlock.ContainsKey(block))
        {
            return;
        }

        conveyorItemCountsByBlock[block] = clampedItemCount;
        cachedLoadedConveyorItemCount += clampedItemCount - previousItemCount;
        if (cachedLoadedConveyorItemCount < 0)
        {
            cachedLoadedConveyorItemCount = 0;
        }
    }

    private void RemoveCachedConveyorBlockItemCount(Block block)
    {
        if (ReferenceEquals(block, null))
        {
            return;
        }

        if (!conveyorItemCountsByBlock.TryGetValue(block, out int previousItemCount))
        {
            return;
        }

        conveyorItemCountsByBlock.Remove(block);
        cachedLoadedConveyorItemCount -= previousItemCount;
        if (cachedLoadedConveyorItemCount < 0)
        {
            cachedLoadedConveyorItemCount = 0;
        }
    }

    public void MarkConveyorNetworkDirty()
    {
        conveyorNetworkCacheDirty = true;
        conveyorLineCacheDirty = true;
        conveyorNetworkBlocksById.Clear();
        conveyorNetworkRetryTimes.Clear();
        conveyorNetworkSleepingIds.Clear();
        conveyorNetworkActiveIds.Clear();
        conveyorNetworkSleepCheckQueuedIds.Clear();
        conveyorNetworkSleepCheckBuffer.Clear();
        ClearStraightConveyorLineRetries();
        InvalidateBeltItemLineDebugVisuals();
    }

    public void MarkConveyorLineCacheDirty()
    {
        conveyorLineCacheDirty = true;
        ClearConveyorLineWakeQueue();
        ClearStraightConveyorLineRetries();
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

        if (!conveyorNetworkBlocksById.TryGetValue(networkId, out List<Block> networkBlocks)
            || networkBlocks == null
            || networkBlocks.Count == 0)
        {
            return;
        }

        bool hasWork = false;
        for (int i = 0; i < networkBlocks.Count; i++)
        {
            Block block = networkBlocks[i];
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
        for (int i = 0; i < networkBlocks.Count; i++)
        {
            networkBlocks[i]?.RefreshConveyorActivityRegistration(false);
        }
    }

    private void RefreshSleepAwakeDebugVisualsForNetwork(int networkId)
    {
        if (networkId <= 0)
        {
            return;
        }

        if (!conveyorNetworkBlocksById.TryGetValue(networkId, out List<Block> networkBlocks)
            || networkBlocks == null)
        {
            return;
        }

        for (int i = 0; i < networkBlocks.Count; i++)
        {
            networkBlocks[i]?.RefreshSleepAwakeDebugVisuals(true);
        }
    }

    private void TickActiveConveyorDataMotions(float deltaTime)
    {
        if (deltaTime <= 0f || activeConveyorDataMotionBlocks.Count == 0)
        {
            return;
        }

        float now = Time.time;
        int processedCount = 0;
        int loopIterations = 0;
        int processLimit = Mathf.Max(GetEffectiveConveyorWakeQueueProcessLimit(), activeConveyorDataMotionBlocks.Count);
        while (activeConveyorDataMotionBlocks.Count > 0 && processedCount < processLimit)
        {
            loopIterations++;
            Block block = activeConveyorDataMotionBlocks[0];
            if (block == null || !activeConveyorDataMotionDueTimes.ContainsKey(block))
            {
                RemoveActiveConveyorDataMotionAt(0);
                continue;
            }

            float dueTime = GetActiveConveyorDataMotionDueTime(block);
            if (dueTime > now + 0.0001f)
            {
                break;
            }

            processedCount++;
            RemoveActiveConveyorDataMotionAt(0);
            if (block == null || !IsLoadedRuntimeBlock(block))
            {
                activeConveyors.Remove(block);
                continue;
            }

            if (!block.HasActiveVirtualConveyorDataMotion())
            {
                block.RefreshConveyorActivityRegistration();
                continue;
            }

            BeginConveyorRuntimeRefreshBatch();
            try
            {
                block.CompleteDueVirtualConveyorDataMotions(now);
                block.RefreshConveyorActivityRegistration(false);
            }
            finally
            {
                EndConveyorRuntimeRefreshBatch();
            }

            if (activeConveyors.Contains(block) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorDirectWake(block);
            }
        }

        if (loopIterations > 0)
        {
            MapObjectTickProfiler.AddBeltLoopIterations(loopIterations, 0, 0, 0);
        }
    }

    private void AddActiveConveyorDataMotionBlock(Block block)
    {
        if (block == null)
        {
            return;
        }

        float dueTime = block.GetNextVirtualConveyorDataMotionCompletionTime();
        if (float.IsNaN(dueTime) || float.IsInfinity(dueTime))
        {
            RemoveActiveConveyorDataMotionBlock(block);
            return;
        }

        if (activeConveyorDataMotionIndices.TryGetValue(block, out int existingIndex))
        {
            activeConveyorDataMotionDueTimes[block] = dueTime;
            RestoreActiveConveyorDataMotionHeapAt(existingIndex);
            return;
        }

        int index = activeConveyorDataMotionBlocks.Count;
        activeConveyorDataMotionBlocks.Add(block);
        activeConveyorDataMotionIndices[block] = index;
        activeConveyorDataMotionDueTimes[block] = dueTime;
        HeapifyActiveConveyorDataMotionUp(index);
    }

    private bool RemoveActiveConveyorDataMotionBlock(Block block)
    {
        return block != null
            && activeConveyorDataMotionIndices.TryGetValue(block, out int index)
            && RemoveActiveConveyorDataMotionAt(index) != null;
    }

    private Block RemoveActiveConveyorDataMotionAt(int index)
    {
        int lastIndex = activeConveyorDataMotionBlocks.Count - 1;
        if (index < 0 || index > lastIndex)
        {
            return null;
        }

        Block removedBlock = activeConveyorDataMotionBlocks[index];
        Block lastBlock = activeConveyorDataMotionBlocks[lastIndex];
        activeConveyorDataMotionBlocks.RemoveAt(lastIndex);

        if (removedBlock != null)
        {
            activeConveyorDataMotionIndices.Remove(removedBlock);
            activeConveyorDataMotionDueTimes.Remove(removedBlock);
        }

        if (index < lastIndex && lastBlock != null)
        {
            activeConveyorDataMotionBlocks[index] = lastBlock;
            activeConveyorDataMotionIndices[lastBlock] = index;
            RestoreActiveConveyorDataMotionHeapAt(index);
        }

        return removedBlock;
    }

    private float GetActiveConveyorDataMotionDueTime(Block block)
    {
        return block != null && activeConveyorDataMotionDueTimes.TryGetValue(block, out float dueTime)
            ? dueTime
            : float.PositiveInfinity;
    }

    private void RestoreActiveConveyorDataMotionHeapAt(int index)
    {
        if (index < 0 || index >= activeConveyorDataMotionBlocks.Count)
        {
            return;
        }

        Block block = activeConveyorDataMotionBlocks[index];
        if (block == null || !activeConveyorDataMotionDueTimes.ContainsKey(block))
        {
            RemoveActiveConveyorDataMotionAt(index);
            return;
        }

        int restoredIndex = HeapifyActiveConveyorDataMotionUp(index);
        HeapifyActiveConveyorDataMotionDown(restoredIndex);
    }

    private int HeapifyActiveConveyorDataMotionUp(int index)
    {
        while (index > 0)
        {
            int parentIndex = (index - 1) >> 1;
            if (CompareActiveConveyorDataMotionDueTime(index, parentIndex) >= 0)
            {
                break;
            }

            SwapActiveConveyorDataMotionHeapNodes(index, parentIndex);
            index = parentIndex;
        }

        return index;
    }

    private void HeapifyActiveConveyorDataMotionDown(int index)
    {
        int count = activeConveyorDataMotionBlocks.Count;
        while (true)
        {
            int leftIndex = (index << 1) + 1;
            if (leftIndex >= count)
            {
                return;
            }

            int rightIndex = leftIndex + 1;
            int smallestIndex = rightIndex < count && CompareActiveConveyorDataMotionDueTime(rightIndex, leftIndex) < 0
                ? rightIndex
                : leftIndex;
            if (CompareActiveConveyorDataMotionDueTime(smallestIndex, index) >= 0)
            {
                return;
            }

            SwapActiveConveyorDataMotionHeapNodes(index, smallestIndex);
            index = smallestIndex;
        }
    }

    private int CompareActiveConveyorDataMotionDueTime(int leftIndex, int rightIndex)
    {
        float leftDueTime = GetActiveConveyorDataMotionDueTime(activeConveyorDataMotionBlocks[leftIndex]);
        float rightDueTime = GetActiveConveyorDataMotionDueTime(activeConveyorDataMotionBlocks[rightIndex]);
        return leftDueTime.CompareTo(rightDueTime);
    }

    private void SwapActiveConveyorDataMotionHeapNodes(int leftIndex, int rightIndex)
    {
        if (leftIndex == rightIndex)
        {
            return;
        }

        Block leftBlock = activeConveyorDataMotionBlocks[leftIndex];
        Block rightBlock = activeConveyorDataMotionBlocks[rightIndex];
        activeConveyorDataMotionBlocks[leftIndex] = rightBlock;
        activeConveyorDataMotionBlocks[rightIndex] = leftBlock;
        if (rightBlock != null)
        {
            activeConveyorDataMotionIndices[rightBlock] = leftIndex;
        }

        if (leftBlock != null)
        {
            activeConveyorDataMotionIndices[leftBlock] = rightIndex;
        }
    }

    private void TickActiveConveyors(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        if (IsConveyorRuntimeRefreshDeferred)
        {
            return;
        }

        PromoteDeferredConveyorLineWakes();
        if (activeConveyors.Count == 0
            && conveyorWakeQueue.Count == 0
            && conveyorLineWakeQueue.Count == 0
            && !HasDueStraightConveyorLineRetry()
            && conveyorNetworkSleepCheckQueuedIds.Count == 0)
        {
            return;
        }

        EnsureConveyorLineCache();
        conveyorLinesTickedThisFrame.Clear();
        ProcessDueStraightConveyorLineRetries();
        MaybeEnqueueActiveConveyorSafetyScan();
        if (conveyorWakeQueue.Count == 0 && conveyorLineWakeQueue.Count == 0)
        {
            ProcessQueuedConveyorNetworkSleepChecks();
            return;
        }

        int queuedAtFrameStart = conveyorWakeQueue.Count + conveyorLineWakeQueue.Count;
        int processLimit = GetEffectiveConveyorWakeQueueProcessLimit();
        int processedCount = 0;
        int activeLoopIterations = 0;
        conveyorLineBlockLoopIterations = 0;
        while ((conveyorLineWakeQueue.Count > 0 || conveyorWakeQueue.Count > 0)
            && processedCount < queuedAtFrameStart
            && processedCount < processLimit)
        {
            activeLoopIterations++;
            if (conveyorLineWakeQueue.Count > 0)
            {
                int lineId = conveyorLineWakeQueue.Dequeue();
                ConveyorLineWakeRange wakeRange = conveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange queuedWakeRange)
                    ? queuedWakeRange
                    : new ConveyorLineWakeRange(0, int.MaxValue, true);
                conveyorLineWakeRangesById.Remove(lineId);
                processedCount++;
                TryTickStraightConveyorLine(lineId, wakeRange);
                continue;
            }

            Block block = conveyorWakeQueue.Dequeue();
            conveyorWakeQueued.Remove(block);
            processedCount++;
            if (block == null)
            {
                continue;
            }

            bool forceDirectWake = conveyorDirectWakeBlocks.Remove(block);
            if (!forceDirectWake && TryTickStraightConveyorLine(block))
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

            BeginConveyorRuntimeRefreshBatch();
            try
            {
                block.TickConveyor(deltaTime);
            }
            finally
            {
                EndConveyorRuntimeRefreshBatch();
            }

            if (activeConveyors.Contains(block) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorWake(block);
            }
        }

        if (activeLoopIterations > 0 || conveyorLineBlockLoopIterations > 0)
        {
            MapObjectTickProfiler.AddBeltLoopIterations(0, activeLoopIterations, conveyorLineBlockLoopIterations, 0);
        }

        ProcessQueuedConveyorNetworkSleepChecks();
    }

    private void MaybeEnqueueActiveConveyorSafetyScan()
    {
        if (activeConveyors.Count == 0)
        {
            activeConveyorSafetyScanIndex = 0;
            return;
        }

        if (Time.time < nextConveyorActiveFullScanTime)
        {
            return;
        }

        nextConveyorActiveFullScanTime = Time.time + Mathf.Max(0.02f, conveyorActiveFullScanInterval);
        EnsureSortedActiveConveyors();
        int conveyorCount = sortedActiveConveyors.Count;
        if (conveyorCount == 0)
        {
            activeConveyorSafetyScanIndex = 0;
            return;
        }

        if (activeConveyorSafetyScanIndex < 0 || activeConveyorSafetyScanIndex >= conveyorCount)
        {
            activeConveyorSafetyScanIndex = 0;
        }

        int scanBudget = GetEffectiveActiveConveyorSafetyScanBudget(conveyorCount);
        for (int scannedCount = 0; scannedCount < scanBudget; scannedCount++)
        {
            Block block = sortedActiveConveyors[activeConveyorSafetyScanIndex];
            activeConveyorSafetyScanIndex = (activeConveyorSafetyScanIndex + 1) % conveyorCount;
            if (IsLoadedRuntimeBlock(block) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorSafetyWake(block);
            }
        }
    }

    private int GetEffectiveActiveConveyorSafetyScanBudget(int conveyorCount)
    {
        if (conveyorCount <= 0)
        {
            return 0;
        }

        int dynamicBudget = Mathf.Max(
            conveyorActiveSafetyScanBudget,
            Mathf.CeilToInt(conveyorCount / 16f));
        return Mathf.Clamp(dynamicBudget, 1, conveyorCount);
    }

    private bool TryTickStraightConveyorLine(Block triggerBlock)
    {
        if (triggerBlock == null
            || !TryGetConveyorLineSlot(triggerBlock, out int lineId, out _, out _, out bool isCycle)
            || isCycle)
        {
            return false;
        }

        return TryTickStraightConveyorLine(
            lineId,
            new ConveyorLineWakeRange(0, int.MaxValue, true),
            triggerBlock);
    }

    private bool TryTickStraightConveyorLine(int lineId, ConveyorLineWakeRange wakeRange)
    {
        return TryTickStraightConveyorLine(lineId, wakeRange, null);
    }

    private bool TryTickStraightConveyorLine(int lineId, ConveyorLineWakeRange wakeRange, Block directFallbackBlock)
    {
        if (lineId <= 0)
        {
            return false;
        }

        if (conveyorLinesTickedThisFrame.Contains(lineId))
        {
            DeferConveyorLineWake(lineId, wakeRange);
            return true;
        }

        ConveyorLine line = FindConveyorLine(lineId);
        if (!CanTickStraightConveyorLine(line))
        {
            ClearStraightConveyorLineRetry(lineId);
            return false;
        }

        ResolveStraightConveyorLineWakeRange(line, ref wakeRange, out int minSlotIndex, out int maxSlotIndex);

        if (!HasStraightConveyorLineRetryWork(line, minSlotIndex, maxSlotIndex))
        {
            ClearStraightConveyorLineRetry(line.id);
            return true;
        }

        if (HasStraightConveyorLineFastPathRuntimeBlocker(line, minSlotIndex, maxSlotIndex))
        {
            QueueStraightConveyorLineDirectFallback(line, minSlotIndex, maxSlotIndex, directFallbackBlock);
            return directFallbackBlock == null;
        }

        conveyorLinesTickedThisFrame.Add(lineId);
        conveyorLineTouchedBlocks.Clear();
        conveyorLineTouchedSet.Clear();

        bool movedAny = false;
        movedAny |= TryTickStraightConveyorLine(line, minSlotIndex, maxSlotIndex);

        if (!movedAny)
        {
            NotifyStraightConveyorLineTickCompleted(line, minSlotIndex, maxSlotIndex);
            if (HasStraightConveyorLineRetryWork(line, minSlotIndex, maxSlotIndex))
            {
                DelayStraightConveyorLineRetry(line.id, minSlotIndex, maxSlotIndex);
            }
            else
            {
                ClearStraightConveyorLineRetry(line.id);
            }

            return directFallbackBlock == null;
        }

        ClearStraightConveyorLineRetry(line.id);
        WakeAndRefreshConveyorRuntimeBlocks(conveyorLineTouchedBlocks, false);
        DeferMovedStraightConveyorLineWake(line);

        return true;
    }

    private static bool HasStraightConveyorLineRetryWork(ConveyorLine line, int minSlotIndex, int maxSlotIndex)
    {
        if (line == null || line.blocks.Count <= 0)
        {
            return false;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            if (line.blocks[i] != null && line.blocks[i].HasStraightConveyorLineRetryWork())
            {
                return true;
            }
        }

        return false;
    }

    private void NotifyStraightConveyorLineTickCompleted(ConveyorLine line, int minSlotIndex, int maxSlotIndex)
    {
        if (line == null)
        {
            return;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            line.blocks[i]?.NotifyStraightConveyorLineTickCompleted();
        }
    }

    private ConveyorLine FindConveyorLine(int lineId)
    {
        return conveyorLinesById.TryGetValue(lineId, out ConveyorLine line) ? line : null;
    }

    private static bool CanTickStraightConveyorLine(ConveyorLine line)
    {
        return line != null
            && !line.isCycle
            && line.simulationCacheValid
            && line.blocks.Count > 0;
    }

    private static void ResolveStraightConveyorLineWakeRange(
        ConveyorLine line,
        ref ConveyorLineWakeRange wakeRange,
        out int minSlotIndex,
        out int maxSlotIndex)
    {
        minSlotIndex = 0;
        maxSlotIndex = line != null ? line.blocks.Count - 1 : -1;
        if (line == null || line.blocks.Count <= 0 || wakeRange.fullLine)
        {
            return;
        }

        minSlotIndex = wakeRange.minSlotIndex;
        maxSlotIndex = wakeRange.maxSlotIndex;
        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        wakeRange.minSlotIndex = minSlotIndex;
        wakeRange.maxSlotIndex = maxSlotIndex;
    }

    private static void ResolveStraightConveyorLineSlotRange(ConveyorLine line, ref int minSlotIndex, ref int maxSlotIndex)
    {
        if (line == null || line.blocks.Count <= 0)
        {
            minSlotIndex = 0;
            maxSlotIndex = -1;
            return;
        }

        minSlotIndex = Mathf.Clamp(minSlotIndex, 0, line.blocks.Count - 1);
        maxSlotIndex = Mathf.Clamp(maxSlotIndex, minSlotIndex, line.blocks.Count - 1);
    }

    private static bool HasStraightConveyorLineFastPathRuntimeBlocker(ConveyorLine line, int minSlotIndex, int maxSlotIndex)
    {
        if (line == null)
        {
            return true;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            if (line.blocks[i] == null || line.blocks[i].HasStraightConveyorLineFastPathRuntimeBlocker())
            {
                return true;
            }
        }

        return false;
    }

    private void QueueStraightConveyorLineDirectFallback(
        ConveyorLine line,
        int minSlotIndex,
        int maxSlotIndex,
        Block directFallbackBlock)
    {
        if (line == null)
        {
            return;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            Block block = line.blocks[i];
            if (block == null
                || block == directFallbackBlock
                || !block.HasStraightConveyorLineFastPathRuntimeBlocker())
            {
                continue;
            }

            QueueConveyorDirectWake(block);
        }
    }

    private int GetEffectiveConveyorWakeQueueProcessLimit()
    {
        return Mathf.Clamp(conveyorWakeQueueProcessLimit, 1, 512);
    }

    private bool TryTickStraightConveyorLine(ConveyorLine line, int minSlotIndex, int maxSlotIndex)
    {
        if (line == null || !line.simulationCacheValid)
        {
            return false;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        bool movedAny = false;
        for (int i = maxSlotIndex; i >= minSlotIndex; i--)
        {
            conveyorLineBlockLoopIterations++;
            Block block = line.blocks[i];
            int frontLaneIndex = line.frontLaneIndices[i];
            int backLaneIndex = line.backLaneIndices[i];

            if (i < line.blocks.Count - 1
                && block.TryMoveStraightConveyorDataLaneToCached(
                    line.blocks[i + 1],
                    frontLaneIndex,
                    line.backLaneIndices[i + 1],
                    line.nextPathLengths[i]))
            {
                MarkConveyorLineBlockTouched(block);
                MarkConveyorLineBlockTouched(line.blocks[i + 1]);
                movedAny = true;
            }
            else if (i < line.blocks.Count - 1
                && block.HasStraightConveyorDataItemAtLane(frontLaneIndex)
                && block.TryAdvanceStraightConveyorLineLane(frontLaneIndex, true))
            {
                MarkConveyorLineBlockTouched(block);
                movedAny = true;
            }
            else if (i == line.blocks.Count - 1
                && HasNonLineConveyorSuccessor(block)
                && block.TryAdvanceStraightConveyorLineLane(frontLaneIndex, true))
            {
                MarkConveyorLineBlockTouched(block);
                movedAny = true;
            }

            if (block.TryMoveStraightConveyorDataLaneToCached(
                    block,
                    backLaneIndex,
                    frontLaneIndex,
                    line.withinPathLengths[i]))
            {
                MarkConveyorLineBlockTouched(block);
                movedAny = true;
            }
            else if (block.HasStraightConveyorDataItemAtLane(backLaneIndex)
                && block.TryAdvanceStraightConveyorLineLane(backLaneIndex, true))
            {
                MarkConveyorLineBlockTouched(block);
                movedAny = true;
            }
        }

        return movedAny;
    }

    private static bool HasNonLineConveyorSuccessor(Block block)
    {
        return block != null
            && block.TryGetRuntimeNextConveyorBlock(out Block nextBlock)
            && nextBlock != null
            && nextBlock.IsRuntimeConveyor
            && !IsStraightConveyorLineSuccessor(block, nextBlock);
    }

    private void MarkConveyorLineBlockTouched(Block block)
    {
        if (block != null && conveyorLineTouchedSet.Add(block))
        {
            conveyorLineTouchedBlocks.Add(block);
        }
    }

    private void DeferMovedStraightConveyorLineWake(ConveyorLine line)
    {
        if (line == null || line.id <= 0 || conveyorLineTouchedBlocks.Count == 0)
        {
            return;
        }

        DeferConveyorLineWake(line.id, new ConveyorLineWakeRange(0, int.MaxValue, true));
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
        conveyorNetworkBlocksById.Clear();
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
            AddConveyorBlockToNetwork(startBlock, networkId);
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

        AddConveyorBlockToNetwork(neighborBlock, networkId);
        conveyorNetworkBuildQueue.Enqueue(neighborBlock);
    }

    private void AddConveyorBlockToNetwork(Block block, int networkId)
    {
        if (block == null || networkId <= 0)
        {
            return;
        }

        conveyorNetworkIds[block] = networkId;
        if (!conveyorNetworkBlocksById.TryGetValue(networkId, out List<Block> networkBlocks))
        {
            networkBlocks = new List<Block>();
            conveyorNetworkBlocksById[networkId] = networkBlocks;
        }

        networkBlocks.Add(block);
    }

    private void ClearConveyorLineCache()
    {
        conveyorLines.Clear();
        conveyorLinesById.Clear();
        conveyorLineSlots.Clear();
        conveyorLineVisited.Clear();
        conveyorLineBuildIndices.Clear();
        conveyorLinesTickedThisFrame.Clear();
        conveyorLineTouchedBlocks.Clear();
        conveyorLineTouchedSet.Clear();
        conveyorDirectWakeBlocks.Clear();
        ClearStraightConveyorLineRetries();
        ClearConveyorLineWakeQueue();
        conveyorLineCacheDirty = true;
    }

    private void ClearConveyorLineWakeQueue()
    {
        conveyorLineWakeQueue.Clear();
        conveyorLineWakeRangesById.Clear();
        deferredConveyorLineWakeQueue.Clear();
        deferredConveyorLineWakeRangesById.Clear();
    }

    private void ClearStraightConveyorLineRetries()
    {
        conveyorLineRetryStatesById.Clear();
        conveyorLineRetryAttemptsByDueLineId.Clear();
        conveyorLineRetryDueIds.Clear();
        nextConveyorLineRetryTime = float.PositiveInfinity;
    }

    private void EnsureConveyorLineCache()
    {
        if (!conveyorLineCacheDirty)
        {
            return;
        }

        conveyorLineCacheDirty = false;
        conveyorLines.Clear();
        conveyorLinesById.Clear();
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
                || !IsStraightConveyorLineSuccessor(currentBlock, nextBlock))
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
        conveyorLinesById[line.id] = line;

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
        line.frontLaneIndices = new int[lineLength];
        line.backLaneIndices = new int[lineLength];
        line.withinPathLengths = new float[lineLength];
        line.nextPathLengths = new float[lineLength];

        for (int i = 0; i < lineLength; i++)
        {
            Block block = line.blocks[i];
            Block nextBlock = i < lineLength - 1 ? line.blocks[i + 1] : null;
            if (block == null
                || (nextBlock != null && !IsStraightConveyorLineSuccessor(block, nextBlock))
                || !block.TryGetStraightConveyorLineMotionData(
                    nextBlock,
                    out line.frontLaneIndices[i],
                    out line.backLaneIndices[i],
                    out line.withinPathLengths[i],
                    out line.nextPathLengths[i]))
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
            || !IsStraightConveyorLineSuccessor(previousBlock, block);
    }

    private static bool IsRuntimeConveyorLineBlock(Block block)
    {
        return block != null
            && block.IsRuntimeConveyor
            && !block.IsCornerConveyorBlock();
    }

    private static bool IsStraightConveyorLineSuccessor(Block block, Block nextBlock)
    {
        return IsRuntimeConveyorLineBlock(block)
            && IsRuntimeConveyorLineBlock(nextBlock)
            && block.TryGetRuntimeConveyorFlowDirection(out Vector2Int flowDirection)
            && nextBlock.TryGetRuntimeConveyorFlowDirection(out Vector2Int nextFlowDirection)
            && flowDirection == nextFlowDirection;
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
        int loopIterations = 0;
        while (index < activeConveyorDotVisualList.Count)
        {
            loopIterations++;
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

        if (loopIterations > 0)
        {
            MapObjectTickProfiler.AddBeltLoopIterations(0, 0, 0, loopIterations);
        }
    }

    private void DrawActiveBeltDirectionArrows()
    {
        if (activeBeltDirectionVisualList.Count == 0
            || GameManager.Instance == null
            || !GameManager.Instance.ShowDirections)
        {
            return;
        }

        BeginBeltDirectionArrowInstancedRendering();

        int index = 0;
        while (index < activeBeltDirectionVisualList.Count)
        {
            Block block = activeBeltDirectionVisualList[index];
            if (block == null
                || !TryAppendDirectionArrowMatrices(block))
            {
                activeBeltDirectionVisuals.Remove(block);
                RemoveBeltDirectionVisualAt(index);
                continue;
            }

            index++;
        }

        EndBeltDirectionArrowInstancedRendering();
    }

    private bool TryAppendDirectionArrowMatrices(Block block)
    {
        if (block == null)
        {
            return false;
        }

        directionArrowMatrixScratch.Clear();
        int matrixCount = block.AppendDirectionArrowMatrices(directionArrowMatrixScratch);
        if (matrixCount <= 0)
        {
            return false;
        }

        for (int i = 0; i < directionArrowMatrixScratch.Count; i++)
        {
            AddBeltDirectionArrowInstance(directionArrowMatrixScratch[i]);
        }

        return true;
    }

    private void AddBeltDirectionArrowInstance(Matrix4x4 matrix)
    {
        if (beltDirectionArrowInstanceMatrixCount >= MaxBeltDirectionArrowInstancesPerBatch)
        {
            FlushBeltDirectionArrowInstances();
        }

        beltDirectionArrowInstanceMatrices[beltDirectionArrowInstanceMatrixCount] = matrix;
        beltDirectionArrowInstanceMatrixCount++;
    }

    private void BeginBeltDirectionArrowInstancedRendering()
    {
        beltDirectionArrowInstanceMatrixCount = 0;
    }

    private void EndBeltDirectionArrowInstancedRendering()
    {
        FlushBeltDirectionArrowInstances();
    }

    private void FlushBeltDirectionArrowInstances()
    {
        if (beltDirectionArrowInstanceMatrixCount <= 0)
        {
            return;
        }

        EnsureBeltDirectionArrowInstancedResources();
        Mesh arrowMesh = Block.ResolveBeltDirectionArrowMesh();
        if (arrowMesh == null || beltDirectionArrowInstancedMaterial == null)
        {
            beltDirectionArrowInstanceMatrixCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(
            arrowMesh,
            0,
            beltDirectionArrowInstancedMaterial,
            beltDirectionArrowInstanceMatrices,
            beltDirectionArrowInstanceMatrixCount,
            null,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            false,
            gameObject.layer,
            null,
            UnityEngine.Rendering.LightProbeUsage.Off,
            null);
        beltDirectionArrowInstanceMatrixCount = 0;
    }

    private void EnsureBeltDirectionArrowInstancedResources()
    {
        Shader shader = Shader.Find("Custom/InstallGridOverlay");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return;
        }

        if (beltDirectionArrowInstancedMaterial != null && beltDirectionArrowInstancedMaterial.shader == shader)
        {
            ConfigureBeltDirectionArrowInstancedMaterial(shader);
            return;
        }

        if (beltDirectionArrowInstancedMaterial != null)
        {
            Destroy(beltDirectionArrowInstancedMaterial);
        }

        beltDirectionArrowInstancedMaterial = new Material(shader)
        {
            name = "BeltDirectionArrowInstancedMaterial",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave,
            renderQueue = BeltDirectionArrowRenderQueue
        };

        ConfigureBeltDirectionArrowInstancedMaterial(shader);
    }

    private void ConfigureBeltDirectionArrowInstancedMaterial(Shader shader)
    {
        if (beltDirectionArrowInstancedMaterial == null || shader == null)
        {
            return;
        }

        beltDirectionArrowInstancedMaterial.enableInstancing = true;
        beltDirectionArrowInstancedMaterial.renderQueue = BeltDirectionArrowRenderQueue;

        Color materialColor = shader.name == "Custom/InstallGridOverlay" || shader.name == "Sprites/Default"
            ? Color.white
            : BeltDirectionArrowInstancedColor;

        if (beltDirectionArrowInstancedMaterial.HasProperty("_BaseColor"))
        {
            beltDirectionArrowInstancedMaterial.SetColor("_BaseColor", materialColor);
        }

        if (beltDirectionArrowInstancedMaterial.HasProperty("_Color"))
        {
            beltDirectionArrowInstancedMaterial.SetColor("_Color", materialColor);
        }

        if (beltDirectionArrowInstancedMaterial.HasProperty("_Cull"))
        {
            beltDirectionArrowInstancedMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        if (beltDirectionArrowInstancedMaterial.HasProperty("_ZTest"))
        {
            beltDirectionArrowInstancedMaterial.SetFloat(
                "_ZTest",
                (float)UnityEngine.Rendering.CompareFunction.Always);
        }

        if (beltDirectionArrowInstancedMaterial.HasProperty("_ZWrite"))
        {
            beltDirectionArrowInstancedMaterial.SetFloat("_ZWrite", 0f);
        }
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

    private readonly List<Transform> runtimeCounterTransformScratch = new List<Transform>(512);
    private readonly List<Renderer> runtimeCounterRendererScratch = new List<Renderer>(256);
    private readonly List<Collider> runtimeCounterColliderScratch = new List<Collider>(128);

    public void AppendRuntimeProfilerCounters()
    {
        int loadedMapObjectCount = 0;
        int loadedInstallationCount = 0;
        int loadedConveyorBeltCount = 0;
        int activeBlockRootCount = 0;
        int inactiveBlockRootCount = 0;
        int activeMapObjectRootCount = 0;
        int inactiveMapObjectRootCount = 0;
        int activeBeltRootCount = 0;
        int inactiveBeltRootCount = 0;
        int suspendedBeltRootCount = 0;
        int transformCount = 0;
        int activeTransformCount = 0;
        int beltTransformCount = 0;
        int activeBeltTransformCount = 0;
        int rendererCount = 0;
        int enabledRendererCount = 0;
        int activeEnabledRendererCount = 0;
        int beltRendererCount = 0;
        int enabledBeltRendererCount = 0;
        int activeEnabledBeltRendererCount = 0;
        int colliderCount = 0;
        int enabledColliderCount = 0;
        int beltColliderCount = 0;
        int enabledBeltColliderCount = 0;

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (block == null)
            {
                continue;
            }

            if (block.gameObject.activeInHierarchy)
            {
                activeBlockRootCount++;
            }
            else
            {
                inactiveBlockRootCount++;
            }

            if (block.MapObject == null)
            {
                continue;
            }

            MapObject mapObject = block.MapObject;
            loadedMapObjectCount++;
            if (mapObject.gameObject.activeInHierarchy)
            {
                activeMapObjectRootCount++;
            }
            else
            {
                inactiveMapObjectRootCount++;
            }

            if (mapObject is InstallationObject)
            {
                loadedInstallationCount++;
            }

            bool isBelt = mapObject is ConveyorBelt;
            if (isBelt)
            {
                loadedConveyorBeltCount++;
                if (mapObject is ConveyorBelt conveyorBelt && conveyorBelt.IsRuntimeRootSuspended)
                {
                    suspendedBeltRootCount++;
                }

                if (mapObject.gameObject.activeInHierarchy)
                {
                    activeBeltRootCount++;
                }
                else
                {
                    inactiveBeltRootCount++;
                }
            }

            CountRuntimeComponents(
                mapObject,
                isBelt,
                ref transformCount,
                ref activeTransformCount,
                ref beltTransformCount,
                ref activeBeltTransformCount,
                ref rendererCount,
                ref enabledRendererCount,
                ref activeEnabledRendererCount,
                ref beltRendererCount,
                ref enabledBeltRendererCount,
                ref activeEnabledBeltRendererCount,
                ref colliderCount,
                ref enabledColliderCount,
                ref beltColliderCount,
                ref enabledBeltColliderCount);
        }

        int retryAttemptSampleCount = 0;
        int totalLineRetryAttempts = 0;
        int maxLineRetryAttempt = 0;
        foreach (KeyValuePair<int, ConveyorLineRetryState> pair in conveyorLineRetryStatesById)
        {
            int attemptCount = Mathf.Max(0, pair.Value.attemptCount);
            retryAttemptSampleCount++;
            totalLineRetryAttempts += attemptCount;
            maxLineRetryAttempt = Mathf.Max(maxLineRetryAttempt, attemptCount);
        }

        foreach (KeyValuePair<int, int> pair in conveyorLineRetryAttemptsByDueLineId)
        {
            int attemptCount = Mathf.Max(0, pair.Value);
            retryAttemptSampleCount++;
            totalLineRetryAttempts += attemptCount;
            maxLineRetryAttempt = Mathf.Max(maxLineRetryAttempt, attemptCount);
        }

        float averageLineRetryAttempt = retryAttemptSampleCount > 0
            ? totalLineRetryAttempts / (float)retryAttemptSampleCount
            : 0f;

        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedChunks", loadedChunks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedBlocks", loadedBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedMapObjects", loadedMapObjectCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedInstallations", loadedInstallationCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedConveyorBelts", loadedConveyorBeltCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "SleepingChunks", sleepingChunkViews.Count);
        MapObjectTickProfiler.AddRuntimeCounter("World", "SleepingInstallationViews", sleepingInstallationViews.Count);

        MapObjectTickProfiler.AddRuntimeCounter("View", "ActiveBlockRoots", activeBlockRootCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "InactiveBlockRoots", inactiveBlockRootCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "ActiveMapObjectRoots", activeMapObjectRootCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "InactiveMapObjectRoots", inactiveMapObjectRootCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "ActiveBeltRoots", activeBeltRootCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "InactiveBeltRoots", inactiveBeltRootCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "SuspendedBeltRoots", suspendedBeltRootCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "MapObjectTransforms", transformCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "ActiveMapObjectTransforms", activeTransformCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "InactiveMapObjectTransforms", transformCount - activeTransformCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "BeltTransforms", beltTransformCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "ActiveBeltTransforms", activeBeltTransformCount);
        MapObjectTickProfiler.AddRuntimeCounter("View", "InactiveBeltTransforms", beltTransformCount - activeBeltTransformCount);

        MapObjectTickProfiler.AddRuntimeCounter("Render", "MapObjectRenderers", rendererCount);
        MapObjectTickProfiler.AddRuntimeCounter("Render", "EnabledMapObjectRenderers", enabledRendererCount);
        MapObjectTickProfiler.AddRuntimeCounter("Render", "ActiveEnabledMapObjectRenderers", activeEnabledRendererCount);
        MapObjectTickProfiler.AddRuntimeCounter("Render", "DisabledMapObjectRenderers", rendererCount - enabledRendererCount);
        MapObjectTickProfiler.AddRuntimeCounter("Render", "BeltRenderers", beltRendererCount);
        MapObjectTickProfiler.AddRuntimeCounter("Render", "EnabledBeltRenderers", enabledBeltRendererCount);
        MapObjectTickProfiler.AddRuntimeCounter("Render", "ActiveEnabledBeltRenderers", activeEnabledBeltRendererCount);
        MapObjectTickProfiler.AddRuntimeCounter("Render", "DisabledBeltRenderers", beltRendererCount - enabledBeltRendererCount);

        MapObjectTickProfiler.AddRuntimeCounter("Physics", "MapObjectColliders", colliderCount);
        MapObjectTickProfiler.AddRuntimeCounter("Physics", "EnabledMapObjectColliders", enabledColliderCount);
        MapObjectTickProfiler.AddRuntimeCounter("Physics", "DisabledMapObjectColliders", colliderCount - enabledColliderCount);
        MapObjectTickProfiler.AddRuntimeCounter("Physics", "BeltColliders", beltColliderCount);
        MapObjectTickProfiler.AddRuntimeCounter("Physics", "EnabledBeltColliders", enabledBeltColliderCount);

        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "LoadedConveyorItems", cachedLoadedConveyorItemCount);
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "TotalConveyorItems", GetConveyorItemCount());
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "ActiveConveyors", activeConveyors.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "DataMotionBlocks", activeConveyorDataMotionBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "ItemVisualBlocks", conveyorItemVisualBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "DirtyItemVisualBlocks", conveyorItemVisualDirtyBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "DynamicItemVisualBlocks", dynamicConveyorItemVisualBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "SlotDotVisualBlocks", activeConveyorDotVisualList.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Conveyor", "DirectionVisualBlocks", activeBeltDirectionVisualList.Count);

        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "SavedBlocks", lastConveyorItemLoadSavedBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "SavedLanes", lastConveyorItemLoadSavedLanes);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "LoadedBlocks", lastConveyorItemLoadLoadedBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "MissingBlocks", lastConveyorItemLoadMissingBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "NotRuntimeBlocks", lastConveyorItemLoadNotRuntimeBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "ZeroLaneBlocks", lastConveyorItemLoadZeroLaneBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "AppliedLanes", lastConveyorItemLoadAppliedLanes);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "FallbackBlocks", lastConveyorItemLoadFallbackBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "FailedBlocks", lastConveyorItemLoadFailedBlocks);

        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "WakeQueue", conveyorWakeQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "WakeQueuedSet", conveyorWakeQueued.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DirectWakeBlocks", conveyorDirectWakeBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "LineWakeQueue", conveyorLineWakeQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredLineWakeQueue", deferredConveyorLineWakeQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "LineRetryStates", conveyorLineRetryStatesById.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "LineRetryDueLines", conveyorLineRetryAttemptsByDueLineId.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "MaxLineRetryAttempt", maxLineRetryAttempt);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "AvgLineRetryAttempt", averageLineRetryAttempt);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "ActiveSafetyScanBudget", GetEffectiveActiveConveyorSafetyScanBudget(activeConveyors.Count));
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "ActiveSafetyScanIndex", activeConveyorSafetyScanIndex);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "NetworkSleepChecks", conveyorNetworkSleepCheckQueuedIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredRuntimeRefreshBlocks", deferredConveyorRuntimeRefreshBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredNetworkWakeBlocks", deferredConveyorNetworkWakeBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredWakeAroundBlocks", deferredConveyorMoveAttemptWakeAroundBlocks.Count);

        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "LineCacheDirty", conveyorLineCacheDirty);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkCacheDirty", conveyorNetworkCacheDirty);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "Lines", conveyorLines.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "LineSlots", conveyorLineSlots.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "Networks", conveyorNetworkBlocksById.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkIds", conveyorNetworkIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkSleeping", conveyorNetworkSleepingIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkActive", conveyorNetworkActiveIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkRetries", conveyorNetworkRetryTimes.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NextLineRetryMs", FormatRuntimeRetryMs());

        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualizedFloorObjects", virtualizedFloorObjectCoordinates.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "FloorVirtualizationWorkQueue", floorObjectVirtualizationWorkQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "FloorVirtualizationQueued", floorObjectVirtualizationQueuedCoordinates.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "BackgroundConveyorDirtyCoordinates", backgroundConveyorDirtyCoordinates.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "BackgroundConveyorWakeCoordinates", backgroundConveyorWakeCoordinates.Count);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltRegistered", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.RegisteredBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltCorners", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.RegisteredCornerBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltTopOnly", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.TopOnlyBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltSourceHidden", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.HiddenSourceViewBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltSourceHiddenObjects", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.HiddenSourceViewObjectCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltEffectiveBatchCellSize", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.EffectiveBatchCellSize : 0f);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltBatches", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltEntries", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveEntryCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltInstances", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveInstanceCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltDrawCalls", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.EstimatedDrawCallCount : 0);
    }

    private void CountRuntimeComponents(
        MapObject mapObject,
        bool isBelt,
        ref int transformCount,
        ref int activeTransformCount,
        ref int beltTransformCount,
        ref int activeBeltTransformCount,
        ref int rendererCount,
        ref int enabledRendererCount,
        ref int activeEnabledRendererCount,
        ref int beltRendererCount,
        ref int enabledBeltRendererCount,
        ref int activeEnabledBeltRendererCount,
        ref int colliderCount,
        ref int enabledColliderCount,
        ref int beltColliderCount,
        ref int enabledBeltColliderCount)
    {
        runtimeCounterTransformScratch.Clear();
        mapObject.GetComponentsInChildren(true, runtimeCounterTransformScratch);
        for (int i = 0; i < runtimeCounterTransformScratch.Count; i++)
        {
            Transform targetTransform = runtimeCounterTransformScratch[i];
            if (targetTransform == null)
            {
                continue;
            }

            transformCount++;
            if (targetTransform.gameObject.activeInHierarchy)
            {
                activeTransformCount++;
            }

            if (isBelt)
            {
                beltTransformCount++;
                if (targetTransform.gameObject.activeInHierarchy)
                {
                    activeBeltTransformCount++;
                }
            }
        }

        runtimeCounterRendererScratch.Clear();
        mapObject.GetComponentsInChildren(true, runtimeCounterRendererScratch);
        for (int i = 0; i < runtimeCounterRendererScratch.Count; i++)
        {
            Renderer renderer = runtimeCounterRendererScratch[i];
            if (renderer == null)
            {
                continue;
            }

            rendererCount++;
            if (renderer.enabled)
            {
                enabledRendererCount++;
                if (renderer.gameObject.activeInHierarchy)
                {
                    activeEnabledRendererCount++;
                    if (isBelt)
                    {
                        activeEnabledBeltRendererCount++;
                    }
                }
            }

            if (isBelt)
            {
                beltRendererCount++;
                if (renderer.enabled)
                {
                    enabledBeltRendererCount++;
                }
            }
        }

        runtimeCounterColliderScratch.Clear();
        mapObject.GetComponentsInChildren(true, runtimeCounterColliderScratch);
        for (int i = 0; i < runtimeCounterColliderScratch.Count; i++)
        {
            Collider targetCollider = runtimeCounterColliderScratch[i];
            if (targetCollider == null)
            {
                continue;
            }

            colliderCount++;
            if (targetCollider.enabled)
            {
                enabledColliderCount++;
            }

            if (isBelt)
            {
                beltColliderCount++;
                if (targetCollider.enabled)
                {
                    enabledBeltColliderCount++;
                }
            }
        }
    }

    private string FormatRuntimeRetryMs()
    {
        if (float.IsNaN(nextConveyorLineRetryTime) || float.IsInfinity(nextConveyorLineRetryTime))
        {
            return "inf";
        }

        float retryMs = Mathf.Max(0f, nextConveyorLineRetryTime - Time.time) * 1000f;
        return retryMs.ToString("0.#", CultureInfo.InvariantCulture);
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
