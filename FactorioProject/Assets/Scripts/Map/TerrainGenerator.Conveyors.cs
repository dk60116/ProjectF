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
    private const float ConveyorLineBlockedRetryInterval = 0.2f;
    private const float ConveyorLineBlockedRetryMaxInterval = 2.4f;
    private const float ConveyorLineBlockedRetryJitterStep = 0.02f;
    private const int ConveyorLineBlockedRetryJitterSteps = 4;
    private const int ConveyorLineBlockedRetryMaxBackoffExponent = 5;
    private const int ConveyorLineWakeRangeExpansionSlots = 2;
    private const float ConveyorLineMovedReadyWakeDelay = 0.02f;
    private const float ConveyorSlotDotInstancedDiameter = 0.08f;
    private static readonly Color ConveyorSlotDotInstancedColor = new Color(1f, 0.36f, 0.08f, 1f);
    private static readonly Color BeltDirectionArrowInstancedColor = new Color(1f, 0.92f, 0.08f, 1f);
    private static readonly ProfilerMarker ConveyorPromoteDeferredWakesMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.PromoteDeferredWakes");
    private static readonly ProfilerMarker ConveyorEnsureLineCacheMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.EnsureLineCache");
    private static readonly ProfilerMarker ConveyorProcessLineRetriesMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.ProcessLineRetries");
    private static readonly ProfilerMarker ConveyorSafetyScanMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.SafetyScan");
    private static readonly ProfilerMarker ConveyorProcessWakeQueueMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.ProcessWakeQueue");
    private static readonly ProfilerMarker ConveyorWakeLineMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.WakeLine");
    private static readonly ProfilerMarker ConveyorWakeCornerGroupMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.WakeCornerGroup");
    private static readonly ProfilerMarker ConveyorWakeBlockMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.WakeBlock");
    private static readonly ProfilerMarker ConveyorCornerGroupCollectMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.CornerGroupCollect");
    private static readonly ProfilerMarker ConveyorCornerGroupTickMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.CornerGroupTick");
    private static readonly ProfilerMarker ConveyorLineRetryWorkMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.LineRetryWork");
    private static readonly ProfilerMarker ConveyorLineBlockerScanMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.LineBlockerScan");
    private static readonly ProfilerMarker ConveyorLineMoveScanMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.LineMoveScan");
    private static readonly ProfilerMarker ConveyorLineWakeRefreshMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.LineWakeRefresh");
    private static readonly ProfilerMarker ConveyorLineNoMoveMarker =
        new ProfilerMarker("TerrainGenerator.TickConveyors.LineNoMove");
    private static readonly ProfilerMarker ConveyorRebuildNetworkCacheMarker =
        new ProfilerMarker("TerrainGenerator.RebuildConveyorNetworkCache");

    private readonly struct ConveyorLaneCoordinateKey : IEquatable<ConveyorLaneCoordinateKey>
    {
        public ConveyorLaneCoordinateKey(Vector2Int coordinate, int laneIndex)
        {
            this.coordinate = coordinate;
            this.laneIndex = laneIndex;
        }

        public readonly Vector2Int coordinate;
        public readonly int laneIndex;

        public bool Equals(ConveyorLaneCoordinateKey other)
        {
            return coordinate == other.coordinate && laneIndex == other.laneIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is ConveyorLaneCoordinateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (coordinate.GetHashCode() * 397) ^ laneIndex;
            }
        }
    }

    public void SetConveyorActive(Block block, bool isActive, bool queueWake = true)
    {
        if (!Application.isPlaying
            || block == null
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        if (isActive)
        {
            if (activeConveyors.Add(handle))
            {
                activeConveyorOrderDirty = true;
                block.ResetConveyorTickClock();
            }

            if (queueWake)
            {
                QueueConveyorWake(block);
            }
        }
        else
        {
            RemoveActiveConveyorHandle(handle);
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

    private bool TryGetRuntimeBlockHandle(Block block, out BlockHandle handle)
    {
        handle = block != null ? block.RuntimeHandle : default;
        return handle.IsValid;
    }

    private bool TryResolveLoadedRuntimeBlock(BlockHandle handle, out Block block)
    {
        block = null;
        return handle.IsValid
            && loadedBlocks.TryGetValue(handle, out block)
            && block != null
            && block.gameObject.activeInHierarchy;
    }

    private bool IsActiveConveyor(Block block)
    {
        return TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            && activeConveyors.Contains(handle);
    }

    private bool RemoveActiveConveyorHandle(BlockHandle handle)
    {
        if (!handle.IsValid || !activeConveyors.Remove(handle))
        {
            return false;
        }

        activeConveyorOrderDirty = true;
        return true;
    }

    public bool IsConveyorRuntimeRefreshDeferred => deferredConveyorRuntimeRefreshDepth > 0;

    private void ClearConveyorRuntimeState()
    {
        virtualConveyorBeltRenderer?.Clear();
        ConvayorBelt2F.ClearRuntimeCoverageLookup();
        Spliterbelt.ClearRuntimeCoverageLookup();
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
        ClearConveyorBlockedLaneWaiters();
        deferredConveyorRuntimeRefreshBlocks.Clear();
        deferredConveyorNetworkWakeBlocks.Clear();
        deferredConveyorMoveAttemptWakeAroundBlocks.Clear();
        deferredConveyorMoveAttemptWakeFlowBlocks.Clear();
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
        FlushDeferredConveyorMoveAttemptWakeFlows();
        FlushDeferredConveyorRuntimeRefreshes();
        FlushDeferredConveyorNetworkWakes();
    }

    public void RegisterConveyorBlockedLaneWaiter(
        Block sourceBlock,
        int sourceLaneIndex,
        Block destinationBlock,
        int destinationLaneIndex)
    {
        if (!Application.isPlaying
            || sourceBlock == null
            || destinationBlock == null
            || sourceLaneIndex < 0
            || destinationLaneIndex < 0)
        {
            return;
        }

        ConveyorLaneCoordinateKey sourceKey = new ConveyorLaneCoordinateKey(sourceBlock.Coordinate, sourceLaneIndex);
        ConveyorLaneCoordinateKey destinationKey = new ConveyorLaneCoordinateKey(destinationBlock.Coordinate, destinationLaneIndex);
        if (conveyorBlockedDestinationBySourceLane.TryGetValue(sourceKey, out ConveyorLaneCoordinateKey previousDestination))
        {
            if (previousDestination.Equals(destinationKey))
            {
                return;
            }

            RemoveConveyorBlockedSourceFromDestination(sourceKey, previousDestination);
        }

        conveyorBlockedDestinationBySourceLane[sourceKey] = destinationKey;
        if (!conveyorBlockedSourcesByDestinationLane.TryGetValue(destinationKey, out List<ConveyorLaneCoordinateKey> sources))
        {
            sources = new List<ConveyorLaneCoordinateKey>(1);
            conveyorBlockedSourcesByDestinationLane[destinationKey] = sources;
        }

        sources.Add(sourceKey);
        lastActiveConveyorBlockedWaiterRegistrations++;
    }

    public void ClearConveyorBlockedLaneWaiter(Block sourceBlock, int sourceLaneIndex)
    {
        if (sourceBlock == null || sourceLaneIndex < 0)
        {
            return;
        }

        ConveyorLaneCoordinateKey sourceKey = new ConveyorLaneCoordinateKey(sourceBlock.Coordinate, sourceLaneIndex);
        if (!conveyorBlockedDestinationBySourceLane.TryGetValue(sourceKey, out ConveyorLaneCoordinateKey destinationKey))
        {
            return;
        }

        conveyorBlockedDestinationBySourceLane.Remove(sourceKey);
        RemoveConveyorBlockedSourceFromDestination(sourceKey, destinationKey);
    }

    public void NotifyConveyorLaneVacated(Block destinationBlock, int destinationLaneIndex)
    {
        if (!Application.isPlaying || destinationBlock == null || destinationLaneIndex < 0)
        {
            return;
        }

        destinationBlock.WakeConveyorVacatedLanePredecessor(destinationLaneIndex);
        destinationBlock.WakeSplitterInputs();
        ConveyorLaneCoordinateKey destinationKey = new ConveyorLaneCoordinateKey(destinationBlock.Coordinate, destinationLaneIndex);
        WakeConveyorBlockedLaneWaitersForDestination(destinationKey);
    }

    public void NotifyConveyorLaneVacated(Vector2Int destinationCoordinate, int destinationLaneIndex)
    {
        if (!Application.isPlaying || destinationLaneIndex < 0)
        {
            return;
        }

        if (TryGetLoadedBlock(destinationCoordinate, out Block destinationBlock) && destinationBlock != null)
        {
            NotifyConveyorLaneVacated(destinationBlock, destinationLaneIndex);
            return;
        }

        ConveyorLaneCoordinateKey destinationKey = new ConveyorLaneCoordinateKey(destinationCoordinate, destinationLaneIndex);
        WakeConveyorBlockedLaneWaitersForDestination(destinationKey);
    }

    private void WakeConveyorBlockedLaneWaitersForPotentialVacatedCoordinate(Vector2Int destinationCoordinate)
    {
        if (conveyorBlockedSourcesByDestinationLane.Count <= 0)
        {
            return;
        }

        for (int laneIndex = 0; laneIndex < Block.ConveyorCellItemUnit; laneIndex++)
        {
            ConveyorLaneCoordinateKey destinationKey = new ConveyorLaneCoordinateKey(destinationCoordinate, laneIndex);
            WakeConveyorBlockedLaneWaitersForDestination(destinationKey);
        }
    }

    private void WakeConveyorBlockedLaneWaitersForDestination(ConveyorLaneCoordinateKey destinationKey)
    {
        if (!conveyorBlockedSourcesByDestinationLane.TryGetValue(destinationKey, out List<ConveyorLaneCoordinateKey> sources))
        {
            return;
        }

        conveyorBlockedSourcesByDestinationLane.Remove(destinationKey);
        conveyorBlockedWaiterWakeBuffer.Clear();
        conveyorBlockedWaiterWakeBuffer.AddRange(sources);
        sources.Clear();

        for (int i = 0; i < conveyorBlockedWaiterWakeBuffer.Count; i++)
        {
            ConveyorLaneCoordinateKey sourceKey = conveyorBlockedWaiterWakeBuffer[i];
            if (!conveyorBlockedDestinationBySourceLane.TryGetValue(sourceKey, out ConveyorLaneCoordinateKey registeredDestination)
                || !registeredDestination.Equals(destinationKey))
            {
                continue;
            }

            conveyorBlockedDestinationBySourceLane.Remove(sourceKey);
            if (!TryGetLoadedBlock(sourceKey.coordinate, out Block sourceBlock)
                || sourceBlock == null
                || !sourceBlock.IsRuntimeConveyor)
            {
                continue;
            }

            if (sourceBlock.WakeConveyorBlockedLaneWaiter(sourceKey.laneIndex))
            {
                lastActiveConveyorBlockedWaitersWoken++;
            }
        }

        conveyorBlockedWaiterWakeBuffer.Clear();
    }

    private void ClearConveyorBlockedLaneWaiters()
    {
        foreach (KeyValuePair<ConveyorLaneCoordinateKey, List<ConveyorLaneCoordinateKey>> pair in conveyorBlockedSourcesByDestinationLane)
        {
            pair.Value?.Clear();
        }

        conveyorBlockedSourcesByDestinationLane.Clear();
        conveyorBlockedDestinationBySourceLane.Clear();
        conveyorBlockedWaiterWakeBuffer.Clear();
    }

    private void RemoveConveyorBlockedSourceFromDestination(
        ConveyorLaneCoordinateKey sourceKey,
        ConveyorLaneCoordinateKey destinationKey)
    {
        if (!conveyorBlockedSourcesByDestinationLane.TryGetValue(destinationKey, out List<ConveyorLaneCoordinateKey> sources))
        {
            return;
        }

        sources.Remove(sourceKey);
        if (sources.Count <= 0)
        {
            conveyorBlockedSourcesByDestinationLane.Remove(destinationKey);
        }
    }

    public void QueueDeferredConveyorRuntimeRefresh(Block block)
    {
        if (!Application.isPlaying
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        deferredConveyorRuntimeRefreshBlocks.Add(handle);
    }

    private void QueueDeferredConveyorNetworkWake(Block block)
    {
        if (!Application.isPlaying
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        deferredConveyorNetworkWakeBlocks.Add(handle);
    }

    public void QueueDeferredConveyorMoveAttemptWakeAround(Block block)
    {
        if (!Application.isPlaying
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        deferredConveyorMoveAttemptWakeFlowBlocks.Remove(handle);
        deferredConveyorMoveAttemptWakeAroundBlocks.Add(handle);
    }

    public void QueueDeferredConveyorMoveAttemptWakeFlow(Block block)
    {
        if (!Application.isPlaying
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || deferredConveyorMoveAttemptWakeAroundBlocks.Contains(handle))
        {
            return;
        }

        deferredConveyorMoveAttemptWakeFlowBlocks.Add(handle);
    }

    internal void WakeAndRefreshConveyorRuntimeBlocks(
        IList<BlockHandle> blockHandles,
        bool queueWake = true,
        bool refreshDebugVisuals = true,
        ConveyorRuntimeWakeMode wakeMode = ConveyorRuntimeWakeMode.Around)
    {
        if (!Application.isPlaying || blockHandles == null || blockHandles.Count == 0)
        {
            return;
        }

        BeginConveyorRuntimeRefreshBatch();
        try
        {
            for (int i = 0; i < blockHandles.Count; i++)
            {
                if (!TryResolveLoadedRuntimeBlock(blockHandles[i], out Block block))
                {
                    continue;
                }

                if (wakeMode == ConveyorRuntimeWakeMode.Around)
                {
                    block.WakeConveyorMoveAttemptsAround();
                }
                else if (wakeMode == ConveyorRuntimeWakeMode.Flow)
                {
                    block.WakeConveyorMoveAttemptsAlongRuntimeFlow();
                }

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
        foreach (BlockHandle handle in deferredConveyorMoveAttemptWakeAroundBlocks)
        {
            conveyorTickBuffer.Add(handle);
        }

        deferredConveyorMoveAttemptWakeAroundBlocks.Clear();
        deferredConveyorRuntimeRefreshDepth++;
        try
        {
            for (int i = 0; i < conveyorTickBuffer.Count; i++)
            {
                if (TryResolveLoadedRuntimeBlock(conveyorTickBuffer[i], out Block block))
                {
                    block.WakeConveyorMoveAttemptsAroundImmediate();
                }
            }
        }
        finally
        {
            deferredConveyorRuntimeRefreshDepth--;
        }

        conveyorTickBuffer.Clear();
    }

    private void FlushDeferredConveyorMoveAttemptWakeFlows()
    {
        if (deferredConveyorMoveAttemptWakeFlowBlocks.Count == 0)
        {
            return;
        }

        conveyorTickBuffer.Clear();
        foreach (BlockHandle handle in deferredConveyorMoveAttemptWakeFlowBlocks)
        {
            conveyorTickBuffer.Add(handle);
        }

        deferredConveyorMoveAttemptWakeFlowBlocks.Clear();
        deferredConveyorRuntimeRefreshDepth++;
        try
        {
            for (int i = 0; i < conveyorTickBuffer.Count; i++)
            {
                if (TryResolveLoadedRuntimeBlock(conveyorTickBuffer[i], out Block block))
                {
                    block.WakeConveyorMoveAttemptsAlongRuntimeFlowImmediate();
                }
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
        foreach (BlockHandle handle in deferredConveyorRuntimeRefreshBlocks)
        {
            conveyorTickBuffer.Add(handle);
        }

        deferredConveyorRuntimeRefreshBlocks.Clear();

        for (int i = 0; i < conveyorTickBuffer.Count; i++)
        {
            if (!TryResolveLoadedRuntimeBlock(conveyorTickBuffer[i], out Block block)
                || !IsLoadedRuntimeBlock(block))
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
        foreach (BlockHandle handle in deferredConveyorNetworkWakeBlocks)
        {
            conveyorTickBuffer.Add(handle);
        }

        deferredConveyorNetworkWakeBlocks.Clear();
        if (conveyorTickBuffer.Count == 0)
        {
            return;
        }

        EnsureConveyorNetworkCache();
        for (int i = 0; i < conveyorTickBuffer.Count; i++)
        {
            BlockHandle handle = conveyorTickBuffer[i];
            if (!TryResolveLoadedRuntimeBlock(handle, out Block block))
            {
                continue;
            }

            if (conveyorNetworkIds.TryGetValue(handle, out int networkId))
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

        if (!VirtualizeConveyorItems || block.HasStraightConveyorLineFastPathRuntimeBlocker())
        {
            QueueConveyorDirectWake(block);
            return;
        }

        if (TryGetCachedNonCycleConveyorLineSlot(
                block,
                out int queuedLineId,
                out int slotIndex,
                out int lineLength))
        {
            QueueConveyorLineWake(queuedLineId, CreateConveyorLineWakeRangeAroundSlot(slotIndex, lineLength));
            return;
        }

        if (TryQueueConveyorCornerGroupWake(block))
        {
            return;
        }

        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || conveyorWakeQueued.Contains(handle))
        {
            return;
        }

        conveyorWakeQueued.Add(handle);
        conveyorWakeQueue.Enqueue(handle);
    }

    internal void QueueConveyorVacancyWake(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (VirtualizeConveyorItems
            && TryGetCachedNonCycleConveyorLineSlot(block, out int lineId, out int slotIndex, out int lineLength))
        {
            QueueConveyorLineVacancyWake(lineId, CreateConveyorLineWakeRangeAroundSlot(slotIndex, lineLength));
            return;
        }

        QueueConveyorWake(block);
    }

    private void QueueConveyorLineVacancyWake(int lineId, ConveyorLineWakeRange wakeRange)
    {
        // A vacancy invalidates both blocked backoff and ready delay.
        // Preserve other pending slots when promoting the retry to a wake.
        if (conveyorLineRetryStatesById.TryGetValue(lineId, out ConveyorLineRetryState retryState))
        {
            wakeRange.Include(retryState.wakeRange);
        }

        ClearStraightConveyorLineRetry(lineId);
        QueueConveyorLineWake(lineId, wakeRange);
    }

    private void QueueConveyorSafetyWake(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (!VirtualizeConveyorItems)
        {
            QueueConveyorDirectWake(block);
            return;
        }

        if (TryGetCachedNonCycleConveyorLineSlot(
                block,
                out int queuedLineId,
                out int slotIndex,
                out int lineLength))
        {
            QueueConveyorLineWake(queuedLineId, CreateConveyorLineWakeRangeAroundSlot(slotIndex, lineLength));
            return;
        }

        if (TryQueueConveyorCornerGroupWake(block))
        {
            return;
        }

        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || conveyorWakeQueued.Contains(handle))
        {
            return;
        }

        conveyorWakeQueued.Add(handle);
        conveyorWakeQueue.Enqueue(handle);
    }

    private void QueueConveyorDirectWake(Block block)
    {
        if (!Application.isPlaying
            || block == null
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        conveyorDirectWakeBlocks.Add(handle);
        conveyorCornerGroupWakeQueuedBlocks.Remove(handle);
        ClearStraightConveyorLineRetry(block);
        if (conveyorWakeQueued.Contains(handle))
        {
            return;
        }

        conveyorWakeQueued.Add(handle);
        conveyorWakeQueue.Enqueue(handle);
    }

    private bool TryQueueConveyorCornerGroupWake(Block block)
    {
        if (block == null
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || conveyorDirectWakeBlocks.Contains(handle)
            || !TryGetCachedConveyorCornerGroupSlot(
                block,
                out int groupId,
                out _,
                out _,
                out _))
        {
            return false;
        }

        if (!conveyorCornerGroupWakeQueuedBlocks.Add(handle))
        {
            return true;
        }

        if (!conveyorCornerGroupWakeBlocksById.TryGetValue(groupId, out List<BlockHandle> wakeBlocks))
        {
            wakeBlocks = new List<BlockHandle>();
            conveyorCornerGroupWakeBlocksById[groupId] = wakeBlocks;
        }

        wakeBlocks.Add(handle);
        if (conveyorCornerGroupWakeQueued.Add(groupId))
        {
            conveyorCornerGroupWakeQueue.Enqueue(groupId);
        }

        return true;
    }

    private bool QueueConveyorLineWake(int lineId)
    {
        return QueueConveyorLineWake(lineId, new ConveyorLineWakeRange(0, int.MaxValue, true));
    }

    private static ConveyorLineWakeRange CreateConveyorLineWakeRangeAroundSlot(int slotIndex, int lineLength)
    {
        if (slotIndex < 0 || lineLength <= 0)
        {
            return new ConveyorLineWakeRange(0, int.MaxValue, true);
        }

        return new ConveyorLineWakeRange(
            Mathf.Max(0, slotIndex - ConveyorLineWakeRangeExpansionSlots),
            Mathf.Min(lineLength - 1, slotIndex + ConveyorLineWakeRangeExpansionSlots),
            false);
    }

    private static ConveyorLineWakeRange CreateConveyorLineWakeRangeAroundSlots(
        int minSlotIndex,
        int maxSlotIndex,
        int lineLength)
    {
        if (minSlotIndex == int.MaxValue || maxSlotIndex < minSlotIndex || lineLength <= 0)
        {
            return new ConveyorLineWakeRange(0, int.MaxValue, true);
        }

        return new ConveyorLineWakeRange(
            Mathf.Max(0, minSlotIndex - ConveyorLineWakeRangeExpansionSlots),
            Mathf.Min(lineLength - 1, maxSlotIndex + ConveyorLineWakeRangeExpansionSlots),
            false);
    }

    private bool QueueConveyorLineWake(int lineId, ConveyorLineWakeRange wakeRange)
    {
        if (lineId <= 0)
        {
            return false;
        }

        if (TryAbsorbStraightConveyorLineWakeIntoRetry(lineId, wakeRange))
        {
            lastActiveConveyorLineWakesDroppedByRetryThrottle++;
            return false;
        }

        if (IsStraightConveyorLineWakeThrottled(lineId, wakeRange))
        {
            lastActiveConveyorLineWakesDroppedByRetryThrottle++;
            return false;
        }

        if (conveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange existingRange))
        {
            existingRange.Include(wakeRange);
            conveyorLineWakeRangesById[lineId] = existingRange;
            return true;
        }

        conveyorLineWakeRangesById[lineId] = wakeRange;
        conveyorLineWakeQueue.Enqueue(lineId);
        return true;
    }

    private void DeferConveyorLineWake(int lineId, ConveyorLineWakeRange wakeRange)
    {
        if (lineId <= 0)
        {
            return;
        }

        if (TryAbsorbStraightConveyorLineWakeIntoRetry(lineId, wakeRange))
        {
            lastActiveConveyorDeferredLineWakesDroppedByRetryThrottle++;
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

    private int PromoteDeferredConveyorLineWakes()
    {
        int deferredCount = deferredConveyorLineWakeQueue.Count;
        int promotedCount = 0;
        for (int i = 0; i < deferredCount; i++)
        {
            int lineId = deferredConveyorLineWakeQueue.Dequeue();
            if (!deferredConveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange wakeRange))
            {
                continue;
            }

            deferredConveyorLineWakeRangesById.Remove(lineId);
            if (QueueConveyorLineWake(lineId, wakeRange))
            {
                promotedCount++;
            }
        }

        return promotedCount;
    }

    private bool TryAbsorbStraightConveyorLineWakeIntoRetry(int lineId, ConveyorLineWakeRange wakeRange)
    {
        if (!conveyorLineRetryStatesById.TryGetValue(lineId, out ConveyorLineRetryState retryState)
            || retryState.readyDelay
            || Time.time >= retryState.retryTime)
        {
            return false;
        }

        retryState.wakeRange.Include(wakeRange);
        conveyorLineRetryStatesById[lineId] = retryState;
        lastActiveConveyorLineRetryRangeMerges++;
        return true;
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
        if (wakeRange.fullLine)
        {
            return true;
        }

        if (retryRange.fullLine)
        {
            return true;
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
                retryState.readyDelay = false;
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

    private void ScheduleStraightConveyorLineReadyWake(int lineId, int minSlotIndex, int maxSlotIndex)
    {
        if (lineId <= 0 || maxSlotIndex < minSlotIndex)
        {
            return;
        }

        ConveyorLineWakeRange wakeRange = new ConveyorLineWakeRange(minSlotIndex, maxSlotIndex, false);
        float retryTime = Time.time + ConveyorLineMovedReadyWakeDelay;
        if (conveyorLineRetryStatesById.TryGetValue(lineId, out ConveyorLineRetryState retryState))
        {
            retryState.wakeRange.Include(wakeRange);
            retryState.retryTime = Mathf.Min(retryState.retryTime, retryTime);
            retryState.attemptCount = 0;
            retryState.readyDelay = true;
            conveyorLineRetryStatesById[lineId] = retryState;
            TrackNextStraightConveyorLineRetryTime(retryState.retryTime);
            return;
        }

        conveyorLineRetryStatesById[lineId] = new ConveyorLineRetryState(wakeRange, retryTime, 0, true);
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

    private int ProcessDueStraightConveyorLineRetries()
    {
        if (conveyorLineRetryStatesById.Count == 0)
        {
            nextConveyorLineRetryTime = float.PositiveInfinity;
            return 0;
        }

        float now = Time.time;
        if (now + 0.0001f < nextConveyorLineRetryTime)
        {
            return 0;
        }

        conveyorLineRetryDueIds.Clear();
        float nextRetryTime = float.PositiveInfinity;
        int scannedCount = 0;
        int readyDelayCount = 0;
        foreach (KeyValuePair<int, ConveyorLineRetryState> pair in conveyorLineRetryStatesById)
        {
            scannedCount++;
            ConveyorLineRetryState retryState = pair.Value;
            if (retryState.readyDelay)
            {
                readyDelayCount++;
            }

            if (retryState.retryTime <= now + 0.0001f)
            {
                conveyorLineRetryDueIds.Add(pair.Key);
                continue;
            }

            nextRetryTime = Mathf.Min(nextRetryTime, retryState.retryTime);
        }

        int queuedRetryWakeCount = conveyorLineRetryDueIds.Count;
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

        lastActiveConveyorRetryStatesScanned = scannedCount;
        lastActiveConveyorRetryWakesQueued = queuedRetryWakeCount;
        lastActiveConveyorReadyDelayStates = readyDelayCount;
        conveyorLineRetryDueIds.Clear();
        nextConveyorLineRetryTime = conveyorLineRetryStatesById.Count > 0
            ? nextRetryTime
            : float.PositiveInfinity;
        return queuedRetryWakeCount;
    }

    public void SetConveyorDotVisualActive(Block block, bool isActive)
    {
        if (!Application.isPlaying
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        if (isActive)
        {
            if (activeConveyorDotVisuals.Add(handle))
            {
                activeConveyorDotVisualList.Add(handle);
            }
        }
        else
        {
            if (activeConveyorDotVisuals.Remove(handle))
            {
                RemoveConveyorDotVisualBlock(handle);
            }
        }
    }

    public void SetBeltDirectionVisualActive(Block block, bool isActive)
    {
        if (!Application.isPlaying
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        if (isActive)
        {
            if (activeBeltDirectionVisuals.Add(handle))
            {
                activeBeltDirectionVisualList.Add(handle);
            }
        }
        else
        {
            if (activeBeltDirectionVisuals.Remove(handle))
            {
                RemoveBeltDirectionVisualBlock(handle);
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

    private void RemoveConveyorDotVisualBlock(BlockHandle handle)
    {
        int index = activeConveyorDotVisualList.IndexOf(handle);
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

    private void RemoveBeltDirectionVisualBlock(BlockHandle handle)
    {
        int index = activeBeltDirectionVisualList.IndexOf(handle);
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
        if (laneIndex < 0 || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return false;
        }

        EnsureBeltItemLineDebugCache();
        BeltItemLineLaneKey key = new BeltItemLineLaneKey(handle, laneIndex);
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

        foreach (BlockHandle handle in conveyorItemVisualBlocks)
        {
            if (!TryResolveLoadedRuntimeBlock(handle, out Block block)
                || !block.IsRuntimeConveyor)
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

                BeltItemLineLaneKey key = new BeltItemLineLaneKey(handle, laneIndex);
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
        successorKey = default;
        if (!TryResolveLoadedRuntimeBlock(key.BlockHandle, out Block block)
            || !block.TryGetRuntimeConveyorSuccessorLane(
                key.LaneIndex,
                out Block destinationBlock,
                out int destinationLaneIndex)
            || !TryGetRuntimeBlockHandle(destinationBlock, out BlockHandle destinationHandle))
        {
            return false;
        }

        successorKey = new BeltItemLineLaneKey(destinationHandle, destinationLaneIndex);
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
        foreach (BlockHandle handle in conveyorItemVisualBlocks)
        {
            QueueBeltItemLineDebugRefresh(handle);
        }

        if (!includeLoadedConveyors)
        {
            return;
        }

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (block != null
                && block.IsConveyorStackingEnabled()
                && TryGetRuntimeBlockHandle(block, out BlockHandle handle))
            {
                QueueBeltItemLineDebugRefresh(handle);
            }
        }
    }

    private void QueueBeltItemLineDebugRefresh(Block block)
    {
        if (TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            QueueBeltItemLineDebugRefresh(handle);
        }
    }

    private void QueueBeltItemLineDebugRefresh(BlockHandle handle)
    {
        if (!handle.IsValid || !pendingBeltItemLineDebugRefreshSet.Add(handle))
        {
            return;
        }

        pendingBeltItemLineDebugRefreshBlocks.Add(handle);
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
                BlockHandle handle = pendingBeltItemLineDebugRefreshBlocks[pendingBeltItemLineDebugRefreshIndex];
                pendingBeltItemLineDebugRefreshIndex++;
                processedCount++;
                pendingBeltItemLineDebugRefreshSet.Remove(handle);

                if (!TryResolveLoadedRuntimeBlock(handle, out Block block))
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
                BlockHandle handle = pendingConveyorSlotDotRefreshBlocks[i];
                if (handle.IsValid && !activeConveyorDotVisuals.Contains(handle))
                {
                    conveyorDotVisualTickBuffer.Add(handle);
                }
            }

            for (int i = 0; i < conveyorDotVisualTickBuffer.Count; i++)
            {
                if (TryResolveLoadedRuntimeBlock(conveyorDotVisualTickBuffer[i], out Block block))
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
            if (block != null
                && block.IsConveyorStackingEnabled()
                && TryGetRuntimeBlockHandle(block, out BlockHandle handle))
            {
                pendingConveyorSlotDotRefreshBlocks.Add(handle);
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
            BlockHandle handle = pendingConveyorSlotDotRefreshBlocks[pendingConveyorSlotDotRefreshIndex];
            pendingConveyorSlotDotRefreshIndex++;
            processedCount++;

            if (!TryResolveLoadedRuntimeBlock(handle, out Block block)
                || !block.IsConveyorStackingEnabled())
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
        if (!Application.isPlaying
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        bool isTracked = conveyorItemVisualBlocks.Contains(handle);
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

        conveyorItemVisualDirtyBlocks.Add(handle);
    }

    public void RefreshBeltItemRenderingVisibility()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        foreach (BlockHandle handle in conveyorItemVisualBlocks)
        {
            if (!TryResolveLoadedRuntimeBlock(handle, out Block block))
            {
                continue;
            }

            block.RefreshConveyorObjectRenderingMode();
            conveyorItemVisualDirtyBlocks.Add(handle);
        }

        conveyorItemVisualBlockSetVersion++;
        dynamicConveyorItemVisualBlockSetVersion++;
    }

    private void TrackConveyorItemVisualBlock(Block block)
    {
        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        CacheConveyorBlockItemCount(block, CaptureConveyorBlockItemCount(block));
        bool added = conveyorItemVisualBlocks.Add(handle);
        SetDynamicConveyorItemVisualBlockTracked(block, block.HasDynamicVirtualConveyorItemVisuals());
        if (!added)
        {
            return;
        }

        conveyorItemVisualDirtyBlocks.Add(handle);
        conveyorItemVisualBlockSetVersion++;
        InvalidateBeltItemLineDebugVisuals(block);
    }

    private void UntrackConveyorItemVisualBlock(Block block)
    {
        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        SetDynamicConveyorItemVisualBlockTracked(block, false);
        RemoveCachedConveyorBlockItemCount(block);
        if (!conveyorItemVisualBlocks.Remove(handle))
        {
            return;
        }

        conveyorItemVisualDirtyBlocks.Add(handle);
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
        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        if (isTracked)
        {
            if (dynamicConveyorItemVisualBlockIndices.ContainsKey(handle))
            {
                return;
            }

            dynamicConveyorItemVisualBlockIndices.Add(handle, dynamicConveyorItemVisualBlocks.Count);
            dynamicConveyorItemVisualBlocks.Add(handle);
            dynamicConveyorItemVisualBlockSetVersion++;
            return;
        }

        if (!dynamicConveyorItemVisualBlockIndices.TryGetValue(handle, out int index))
        {
            return;
        }

        int lastIndex = dynamicConveyorItemVisualBlocks.Count - 1;
        BlockHandle lastHandle = dynamicConveyorItemVisualBlocks[lastIndex];
        dynamicConveyorItemVisualBlocks[index] = lastHandle;
        dynamicConveyorItemVisualBlockIndices[lastHandle] = index;
        dynamicConveyorItemVisualBlocks.RemoveAt(lastIndex);
        dynamicConveyorItemVisualBlockIndices.Remove(handle);
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
        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        int clampedItemCount = Mathf.Max(0, itemCount);
        conveyorItemCountsByBlock.TryGetValue(handle, out int previousItemCount);
        if (previousItemCount == clampedItemCount && conveyorItemCountsByBlock.ContainsKey(handle))
        {
            return;
        }

        conveyorItemCountsByBlock[handle] = clampedItemCount;
        cachedLoadedConveyorItemCount += clampedItemCount - previousItemCount;
        if (cachedLoadedConveyorItemCount < 0)
        {
            cachedLoadedConveyorItemCount = 0;
        }
    }

    private void RemoveCachedConveyorBlockItemCount(Block block)
    {
        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        if (!conveyorItemCountsByBlock.TryGetValue(handle, out int previousItemCount))
        {
            return;
        }

        conveyorItemCountsByBlock.Remove(handle);
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
        ClearConveyorLineWakeQueue();
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

        if (block == null || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return false;
        }

        EnsureConveyorLineCache();
        if (!conveyorLineSlots.TryGetValue(handle, out ConveyorLineSlot slot))
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
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || conveyorLineCacheDirty
            || !conveyorLineSlots.TryGetValue(handle, out ConveyorLineSlot slot)
            || slot.IsCycle)
        {
            return false;
        }

        lineId = slot.LineId;
        slotIndex = slot.SlotIndex;
        lineLength = slot.LineLength;
        return true;
    }

    private bool TryGetCachedConveyorCornerGroupSlot(
        Block block,
        out int groupId,
        out int slotIndex,
        out int groupLength,
        out bool isCycle)
    {
        groupId = -1;
        slotIndex = -1;
        groupLength = 0;
        isCycle = false;

        if (block == null
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || conveyorLineCacheDirty
            || !conveyorCornerGroupSlots.TryGetValue(handle, out ConveyorCornerGroupSlot slot))
        {
            return false;
        }

        groupId = slot.GroupId;
        slotIndex = slot.SlotIndex;
        groupLength = slot.GroupLength;
        isCycle = slot.IsCycle;
        return true;
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
        return TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            && conveyorNetworkIds.TryGetValue(handle, out int networkId)
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
        return TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            && conveyorNetworkIds.TryGetValue(handle, out int networkId)
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
        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || !conveyorNetworkIds.TryGetValue(handle, out int networkId))
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

    public void WakeConveyorNetwork(Block block, bool queueWake = true)
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
        if (TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            && conveyorNetworkIds.TryGetValue(handle, out int networkId))
        {
            conveyorNetworkRetryTimes.Remove(networkId);
            bool wasSleeping = conveyorNetworkSleepingIds.Remove(networkId);
            conveyorNetworkSleepCheckQueuedIds.Remove(networkId);
            if (wasSleeping)
            {
                RefreshSleepAwakeDebugVisualsForNetwork(networkId);
            }
        }

        if (queueWake)
        {
            QueueConveyorWake(block);
        }
    }

    public void QueueConveyorNetworkSleepCheck(Block block)
    {
        if (!Application.isPlaying || block == null)
        {
            return;
        }

        if (!TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        // Conveyor ticks run inside a refresh batch. The network cache was
        // already prepared by the active-conveyor tick, so recording its id is
        // safe here and avoids dropping the sleep request at batch boundaries.
        if (!IsConveyorRuntimeRefreshDeferred)
        {
            EnsureConveyorNetworkCache();
        }

        if (conveyorNetworkIds.TryGetValue(handle, out int networkId)
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

        if (!conveyorNetworkBlocksById.TryGetValue(networkId, out List<BlockHandle> networkBlocks)
            || networkBlocks == null
            || networkBlocks.Count == 0)
        {
            return;
        }

        bool hasWork = false;
        for (int i = 0; i < networkBlocks.Count; i++)
        {
            if (TryResolveLoadedRuntimeBlock(networkBlocks[i], out Block block)
                && block.HasConveyorWorkIgnoringNetworkThrottle())
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
            if (TryResolveLoadedRuntimeBlock(networkBlocks[i], out Block block))
            {
                block.RefreshConveyorActivityRegistration(false);
            }
        }
    }

    private void RefreshSleepAwakeDebugVisualsForNetwork(int networkId)
    {
        if (networkId <= 0)
        {
            return;
        }

        if (!conveyorNetworkBlocksById.TryGetValue(networkId, out List<BlockHandle> networkBlocks)
            || networkBlocks == null)
        {
            return;
        }

        for (int i = 0; i < networkBlocks.Count; i++)
        {
            if (TryResolveLoadedRuntimeBlock(networkBlocks[i], out Block block))
            {
                block.RefreshSleepAwakeDebugVisuals(true);
            }
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
            BlockHandle handle = activeConveyorDataMotionBlocks[0];
            if (!handle.IsValid || !activeConveyorDataMotionDueTimes.ContainsKey(handle))
            {
                RemoveActiveConveyorDataMotionAt(0);
                continue;
            }

            float dueTime = GetActiveConveyorDataMotionDueTime(handle);
            if (dueTime > now + 0.0001f)
            {
                break;
            }

            processedCount++;
            RemoveActiveConveyorDataMotionAt(0);
            if (!TryResolveLoadedRuntimeBlock(handle, out Block block))
            {
                RemoveActiveConveyorHandle(handle);
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

            if (activeConveyors.Contains(handle) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorWake(block);
            }
        }

        if (loopIterations > 0)
        {
            MapObjectTickProfiler.AddBeltLoopIterations(loopIterations, 0, 0, 0);
        }
    }

    private void AddActiveConveyorDataMotionBlock(Block block)
    {
        if (block == null
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        float dueTime = block.GetNextVirtualConveyorDataMotionCompletionTime();
        if (float.IsNaN(dueTime) || float.IsInfinity(dueTime))
        {
            RemoveActiveConveyorDataMotionBlock(block);
            return;
        }

        if (activeConveyorDataMotionIndices.TryGetValue(handle, out int existingIndex))
        {
            activeConveyorDataMotionDueTimes[handle] = dueTime;
            RestoreActiveConveyorDataMotionHeapAt(existingIndex);
            return;
        }

        int index = activeConveyorDataMotionBlocks.Count;
        activeConveyorDataMotionBlocks.Add(handle);
        activeConveyorDataMotionIndices[handle] = index;
        activeConveyorDataMotionDueTimes[handle] = dueTime;
        HeapifyActiveConveyorDataMotionUp(index);
    }

    private bool RemoveActiveConveyorDataMotionBlock(Block block)
    {
        return TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            && activeConveyorDataMotionIndices.TryGetValue(handle, out int index)
            && RemoveActiveConveyorDataMotionAt(index).IsValid;
    }

    private BlockHandle RemoveActiveConveyorDataMotionAt(int index)
    {
        int lastIndex = activeConveyorDataMotionBlocks.Count - 1;
        if (index < 0 || index > lastIndex)
        {
            return default;
        }

        BlockHandle removedHandle = activeConveyorDataMotionBlocks[index];
        BlockHandle lastHandle = activeConveyorDataMotionBlocks[lastIndex];
        activeConveyorDataMotionBlocks.RemoveAt(lastIndex);

        if (removedHandle.IsValid)
        {
            activeConveyorDataMotionIndices.Remove(removedHandle);
            activeConveyorDataMotionDueTimes.Remove(removedHandle);
        }

        if (index < lastIndex && lastHandle.IsValid)
        {
            activeConveyorDataMotionBlocks[index] = lastHandle;
            activeConveyorDataMotionIndices[lastHandle] = index;
            RestoreActiveConveyorDataMotionHeapAt(index);
        }

        return removedHandle;
    }

    private float GetActiveConveyorDataMotionDueTime(BlockHandle handle)
    {
        return handle.IsValid && activeConveyorDataMotionDueTimes.TryGetValue(handle, out float dueTime)
            ? dueTime
            : float.PositiveInfinity;
    }

    private void RestoreActiveConveyorDataMotionHeapAt(int index)
    {
        if (index < 0 || index >= activeConveyorDataMotionBlocks.Count)
        {
            return;
        }

        BlockHandle handle = activeConveyorDataMotionBlocks[index];
        if (!handle.IsValid || !activeConveyorDataMotionDueTimes.ContainsKey(handle))
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

        BlockHandle leftHandle = activeConveyorDataMotionBlocks[leftIndex];
        BlockHandle rightHandle = activeConveyorDataMotionBlocks[rightIndex];
        activeConveyorDataMotionBlocks[leftIndex] = rightHandle;
        activeConveyorDataMotionBlocks[rightIndex] = leftHandle;
        if (rightHandle.IsValid)
        {
            activeConveyorDataMotionIndices[rightHandle] = leftIndex;
        }

        if (leftHandle.IsValid)
        {
            activeConveyorDataMotionIndices[leftHandle] = rightIndex;
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

        ResetLastActiveConveyorTickCounters();
        bool profileActiveConveyors = MapObjectTickProfiler.IsEnabled;
        using (ConveyorPromoteDeferredWakesMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
            lastActiveConveyorDeferredLineWakesPromoted = PromoteDeferredConveyorLineWakes();
            EndConveyorRuntimeSample(
                profileActiveConveyors,
                "ConveyorPromoteDeferredWakes",
                "Conveyor Promote Deferred Wakes",
                startTimestamp);
        }

        if (activeConveyors.Count == 0
            && conveyorWakeQueue.Count == 0
            && conveyorLineWakeQueue.Count == 0
            && conveyorCornerGroupWakeQueue.Count == 0
            && !HasDueStraightConveyorLineRetry()
            && conveyorNetworkSleepCheckQueuedIds.Count == 0)
        {
            return;
        }

        using (ConveyorEnsureLineCacheMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
            EnsureConveyorLineCache();
            EndConveyorRuntimeSample(
                profileActiveConveyors,
                "ConveyorEnsureLineCache",
                "Conveyor Ensure Line Cache",
                startTimestamp);
        }

        conveyorLinesTickedThisFrame.Clear();
        using (ConveyorProcessLineRetriesMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
            ProcessDueStraightConveyorLineRetries();
            EndConveyorRuntimeSample(
                profileActiveConveyors,
                "ConveyorProcessLineRetries",
                "Conveyor Process Line Retries",
                startTimestamp);
        }

        using (ConveyorSafetyScanMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
            lastActiveConveyorSafetyWakesQueued = MaybeEnqueueActiveConveyorSafetyScan();
            EndConveyorRuntimeSample(
                profileActiveConveyors,
                "ConveyorSafetyScan",
                "Conveyor Safety Scan",
                startTimestamp);
        }

        if (conveyorWakeQueue.Count == 0
            && conveyorLineWakeQueue.Count == 0
            && conveyorCornerGroupWakeQueue.Count == 0)
        {
            ProcessQueuedConveyorNetworkSleepChecks();
            return;
        }

        int queuedAtFrameStart = conveyorWakeQueue.Count + conveyorLineWakeQueue.Count + conveyorCornerGroupWakeQueue.Count;
        int processLimit = GetEffectiveConveyorWakeQueueProcessLimit();
        lastActiveConveyorQueuedAtStart = queuedAtFrameStart;
        lastActiveConveyorProcessLimit = processLimit;
        int processedCount = 0;
        int activeLoopIterations = 0;
        conveyorLineBlockLoopIterations = 0;
        using (ConveyorProcessWakeQueueMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
            while ((conveyorLineWakeQueue.Count > 0
                    || conveyorCornerGroupWakeQueue.Count > 0
                    || conveyorWakeQueue.Count > 0)
                && processedCount < queuedAtFrameStart
                && processedCount < processLimit)
            {
                activeLoopIterations++;
                bool processBlockWake = ShouldProcessConveyorBlockWakeBeforeGroupedWakes(processedCount);
                if (!processBlockWake && conveyorLineWakeQueue.Count > 0)
                {
                    using (ConveyorWakeLineMarker.Auto())
                    {
                        long lineStartTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
                        int lineId = conveyorLineWakeQueue.Dequeue();
                        ConveyorLineWakeRange wakeRange = conveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange queuedWakeRange)
                            ? queuedWakeRange
                            : new ConveyorLineWakeRange(0, int.MaxValue, true);
                        conveyorLineWakeRangesById.Remove(lineId);
                        processedCount++;
                        lastActiveConveyorLineWakesProcessed++;
                        if (wakeRange.fullLine)
                        {
                            lastActiveConveyorFullLineWakesProcessed++;
                        }
                        else
                        {
                            lastActiveConveyorRangedLineWakesProcessed++;
                        }

                        TryTickStraightConveyorLine(lineId, wakeRange);
                        EndConveyorRuntimeSample(
                            profileActiveConveyors,
                            "ConveyorWakeLine",
                            "Conveyor Wake Line",
                            lineStartTimestamp);
                    }

                    continue;
                }

                if (!processBlockWake && conveyorCornerGroupWakeQueue.Count > 0)
                {
                    using (ConveyorWakeCornerGroupMarker.Auto())
                    {
                        long cornerStartTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
                        int groupId = conveyorCornerGroupWakeQueue.Dequeue();
                        conveyorCornerGroupWakeQueued.Remove(groupId);
                        processedCount++;
                        lastActiveConveyorCornerGroupWakesProcessed++;
                        TryTickConveyorCornerGroup(groupId, deltaTime);
                        EndConveyorRuntimeSample(
                            profileActiveConveyors,
                            "ConveyorWakeCornerGroup",
                            "Conveyor Wake Corner Group",
                            cornerStartTimestamp);
                    }

                    continue;
                }

                if (conveyorWakeQueue.Count == 0)
                {
                    continue;
                }

                BlockHandle handle = conveyorWakeQueue.Dequeue();
                conveyorWakeQueued.Remove(handle);
                processedCount++;
                lastActiveConveyorBlockWakesProcessed++;
                using (ConveyorWakeBlockMarker.Auto())
                {
                    long blockStartTimestamp = BeginConveyorRuntimeSample(profileActiveConveyors);
                    ProcessQueuedConveyorBlockWake(handle, deltaTime);
                    EndConveyorRuntimeSample(
                        profileActiveConveyors,
                        "ConveyorWakeBlock",
                        "Conveyor Wake Block",
                        blockStartTimestamp);
                }
            }

            EndConveyorRuntimeSample(
                profileActiveConveyors,
                "ConveyorProcessWakeQueue",
                "Conveyor Process Wake Queue",
                startTimestamp);
        }

        lastActiveConveyorProcessed = processedCount;
        if (activeLoopIterations > 0 || conveyorLineBlockLoopIterations > 0)
        {
            MapObjectTickProfiler.AddBeltLoopIterations(0, activeLoopIterations, conveyorLineBlockLoopIterations, 0);
        }

        ProcessQueuedConveyorNetworkSleepChecks();
    }

    private bool ShouldProcessConveyorBlockWakeBeforeGroupedWakes(int processedCount)
    {
        if (conveyorWakeQueue.Count == 0)
        {
            return false;
        }

        if (conveyorLineWakeQueue.Count == 0 && conveyorCornerGroupWakeQueue.Count == 0)
        {
            return true;
        }

        return (processedCount & 3) == 3;
    }

    private void ProcessQueuedConveyorBlockWake(BlockHandle handle, float deltaTime)
    {
        if (!TryResolveLoadedRuntimeBlock(handle, out Block block))
        {
            RemoveActiveConveyorHandle(handle);
            conveyorDirectWakeBlocks.Remove(handle);
            return;
        }

        bool forceDirectWake = conveyorDirectWakeBlocks.Remove(handle);
        if (!forceDirectWake && TryTickStraightConveyorLine(block))
        {
            lastActiveConveyorBlockWakeLineFallbacks++;
            return;
        }

        if (forceDirectWake)
        {
            // This block is already being processed. Clear stale sleep/throttle
            // state without putting it and its neighbours back into the queue.
            block.WakeConveyorMoveAttemptsAlongRuntimeFlowImmediate(false);
        }

        if (!block.ShouldTickActiveConveyor())
        {
            return;
        }

        if (!activeConveyors.Contains(handle))
        {
            SetConveyorActive(block, true, false);
        }

        BeginConveyorRuntimeRefreshBatch();
        try
        {
            bool tickProgressed = block.TickConveyor(deltaTime, out bool tickExecuted);
            lastActiveConveyorBlockWakeTicks++;

            if (!tickExecuted)
            {
                lastActiveConveyorDuplicateFrameTicksSkipped++;
                if (activeConveyors.Contains(handle) && block.ShouldTickActiveConveyor())
                {
                    QueueConveyorWake(block);
                }
            }
            else if (tickProgressed && activeConveyors.Contains(handle) && block.ShouldTickActiveConveyor())
            {
                QueueConveyorWake(block);
            }
            else if (!tickProgressed)
            {
                lastActiveConveyorBlockNoProgressRequeuesSkipped++;
                QueueConveyorNetworkSleepCheck(block);
            }
        }
        finally
        {
            EndConveyorRuntimeRefreshBatch();
        }
    }

    private bool TryTickConveyorCornerGroup(int groupId, float deltaTime)
    {
        if (groupId <= 0
            || deltaTime <= 0f
            || !conveyorCornerGroupsById.TryGetValue(groupId, out ConveyorCornerGroup group)
            || group == null)
        {
            ClearQueuedConveyorCornerGroupWakeBlocks(groupId);
            return false;
        }

        bool profileCornerGroup = MapObjectTickProfiler.IsEnabled;
        using (ConveyorCornerGroupCollectMarker.Auto())
        {
            long collectStartTimestamp = BeginConveyorRuntimeSample(profileCornerGroup);
            if (conveyorCornerGroupWakeBlocksById.TryGetValue(groupId, out List<BlockHandle> queuedBlocks))
            {
                conveyorCornerGroupWakeBlocksById.Remove(groupId);
                lastActiveConveyorCornerGroupBlocksQueued += queuedBlocks.Count;

                for (int i = 0; i < queuedBlocks.Count; i++)
                {
                    BlockHandle handle = queuedBlocks[i];
                    if (!conveyorCornerGroupWakeQueuedBlocks.Remove(handle)
                        || !TryResolveLoadedRuntimeBlock(handle, out Block block))
                    {
                        lastActiveConveyorCornerGroupBlocksSkipped++;
                        continue;
                    }

                    if (!TryAddConveyorCornerGroupTickBlock(groupId, block))
                    {
                        lastActiveConveyorCornerGroupBlocksSkipped++;
                    }
                }

                queuedBlocks.Clear();
            }

            EndConveyorRuntimeSample(
                profileCornerGroup,
                "ConveyorCornerGroupCollect",
                "Conveyor Corner Group Collect",
                collectStartTimestamp);
        }

        if (conveyorCornerGroupTickBlocks.Count == 0)
        {
            return true;
        }

        lastActiveConveyorCornerGroupBlocksSelected += conveyorCornerGroupTickBlocks.Count;
        using (ConveyorCornerGroupTickMarker.Auto())
        {
            long tickStartTimestamp = BeginConveyorRuntimeSample(profileCornerGroup);
            BeginConveyorRuntimeRefreshBatch();
            try
            {
                for (int i = 0; i < conveyorCornerGroupTickBlocks.Count; i++)
                {
                    if (!TryResolveLoadedRuntimeBlock(conveyorCornerGroupTickBlocks[i], out Block block)
                        || !IsLoadedRuntimeBlock(block)
                        || !block.ShouldTickActiveConveyor())
                    {
                        continue;
                    }

                    if (!IsActiveConveyor(block))
                    {
                        SetConveyorActive(block, true, false);
                    }

                    bool tickProgressed = block.TickConveyor(deltaTime, out bool tickExecuted);
                    lastActiveConveyorCornerGroupBlocksProcessed++;

                    if (!tickExecuted)
                    {
                        lastActiveConveyorDuplicateFrameTicksSkipped++;
                        if (IsActiveConveyor(block) && block.ShouldTickActiveConveyor())
                        {
                            QueueConveyorWake(block);
                        }
                    }
                    else if (tickProgressed && IsActiveConveyor(block) && block.ShouldTickActiveConveyor())
                    {
                        QueueConveyorWake(block);
                    }
                    else if (!tickProgressed)
                    {
                        lastActiveConveyorCornerGroupNoProgressRequeuesSkipped++;
                        QueueConveyorNetworkSleepCheck(block);
                    }
                }
            }
            finally
            {
                EndConveyorRuntimeRefreshBatch();
                conveyorCornerGroupTickBlocks.Clear();
            }

            EndConveyorRuntimeSample(
                profileCornerGroup,
                "ConveyorCornerGroupTick",
                "Conveyor Corner Group Tick",
                tickStartTimestamp);
        }

        return true;
    }

    private bool TryAddConveyorCornerGroupTickBlock(int groupId, Block block)
    {
        if (block == null
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || !conveyorCornerGroupSlots.TryGetValue(handle, out ConveyorCornerGroupSlot slot)
            || slot.GroupId != groupId)
        {
            return false;
        }

        int insertIndex = conveyorCornerGroupTickBlocks.Count;
        while (insertIndex > 0
            && conveyorCornerGroupSlots.TryGetValue(
                conveyorCornerGroupTickBlocks[insertIndex - 1],
                out ConveyorCornerGroupSlot existingSlot)
            && existingSlot.GroupId == groupId
            && existingSlot.SlotIndex < slot.SlotIndex)
        {
            insertIndex--;
        }

        conveyorCornerGroupTickBlocks.Insert(insertIndex, handle);
        return true;
    }

    private void ClearQueuedConveyorCornerGroupWakeBlocks(int groupId)
    {
        if (!conveyorCornerGroupWakeBlocksById.TryGetValue(groupId, out List<BlockHandle> queuedBlocks))
        {
            return;
        }

        conveyorCornerGroupWakeBlocksById.Remove(groupId);
        for (int i = 0; i < queuedBlocks.Count; i++)
        {
            conveyorCornerGroupWakeQueuedBlocks.Remove(queuedBlocks[i]);
        }

        queuedBlocks.Clear();
    }

    private void ResetLastActiveConveyorTickCounters()
    {
        lastActiveConveyorTickFrame = Time.frameCount;
        lastActiveConveyorQueuedAtStart = 0;
        lastActiveConveyorProcessLimit = 0;
        lastActiveConveyorProcessed = 0;
        lastActiveConveyorLineWakesProcessed = 0;
        lastActiveConveyorBlockWakesProcessed = 0;
        lastActiveConveyorCornerGroupWakesProcessed = 0;
        lastActiveConveyorCornerGroupBlocksProcessed = 0;
        lastActiveConveyorCornerGroupBlocksQueued = 0;
        lastActiveConveyorCornerGroupBlocksSelected = 0;
        lastActiveConveyorCornerGroupBlocksSkipped = 0;
        lastActiveConveyorCornerGroupNoProgressRequeuesSkipped = 0;
        lastActiveConveyorBlockWakeTicks = 0;
        lastActiveConveyorBlockNoProgressRequeuesSkipped = 0;
        lastActiveConveyorDuplicateFrameTicksSkipped = 0;
        lastActiveConveyorBlockWakeLineFallbacks = 0;
        lastActiveConveyorFullLineWakesProcessed = 0;
        lastActiveConveyorRangedLineWakesProcessed = 0;
        lastActiveConveyorDeferredLineWakesPromoted = 0;
        lastActiveConveyorLineNoMoveWakes = 0;
        lastActiveConveyorLineNoMoveBlocksChanged = 0;
        lastActiveConveyorLineNoMoveBlocksSkipped = 0;
        lastActiveConveyorLineNoMoveDirectFallbacks = 0;
        lastActiveConveyorLineWakesDroppedByRetryThrottle = 0;
        lastActiveConveyorDeferredLineWakesDroppedByRetryThrottle = 0;
        lastActiveConveyorLineRetryRangeMerges = 0;
        lastActiveConveyorRetryStatesScanned = 0;
        lastActiveConveyorRetryWakesQueued = 0;
        lastActiveConveyorReadyDelayStates = 0;
        lastActiveConveyorSafetyWakesQueued = 0;
        lastActiveConveyorMovedLineWakesScheduled = 0;
        lastActiveConveyorMovedLineWakeSlots = 0;
        lastActiveConveyorBlockedWaiterRegistrations = 0;
        lastActiveConveyorBlockedWaitersWoken = 0;
    }

    private static long BeginConveyorRuntimeSample(bool enabled)
    {
        return enabled ? MapObjectTickProfiler.BeginSample() : 0L;
    }

    private static void EndConveyorRuntimeSample(
        bool enabled,
        string typeName,
        string itemName,
        long startTimestamp)
    {
        if (!enabled)
        {
            return;
        }

        MapObjectTickProfiler.EndNamedSample(
            "Runtime",
            typeName,
            itemName,
            startTimestamp);
    }

    private int MaybeEnqueueActiveConveyorSafetyScan()
    {
        if (activeConveyors.Count == 0)
        {
            activeConveyorSafetyScanIndex = 0;
            return 0;
        }

        if (Time.time < nextConveyorActiveFullScanTime)
        {
            return 0;
        }

        nextConveyorActiveFullScanTime = Time.time + Mathf.Max(0.02f, conveyorActiveFullScanInterval);
        EnsureSortedActiveConveyors();
        int conveyorCount = sortedActiveConveyors.Count;
        if (conveyorCount == 0)
        {
            activeConveyorSafetyScanIndex = 0;
            return 0;
        }

        if (activeConveyorSafetyScanIndex < 0 || activeConveyorSafetyScanIndex >= conveyorCount)
        {
            activeConveyorSafetyScanIndex = 0;
        }

        int scanBudget = GetEffectiveActiveConveyorSafetyScanBudget(conveyorCount);
        int queuedCount = 0;
        for (int scannedCount = 0; scannedCount < scanBudget; scannedCount++)
        {
            BlockHandle handle = sortedActiveConveyors[activeConveyorSafetyScanIndex];
            activeConveyorSafetyScanIndex = (activeConveyorSafetyScanIndex + 1) % conveyorCount;
            if (TryResolveLoadedRuntimeBlock(handle, out Block block)
                && block.ShouldTickActiveConveyor())
            {
                QueueConveyorSafetyWake(block);
                queuedCount++;
            }
            else if (block == null)
            {
                RemoveActiveConveyorHandle(handle);
            }
        }

        return queuedCount;
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
            || !TryGetConveyorLineSlot(
                triggerBlock,
                out int lineId,
                out int slotIndex,
                out int lineLength,
                out bool isCycle)
            || isCycle)
        {
            return false;
        }

        return TryTickStraightConveyorLine(
            lineId,
            CreateConveyorLineWakeRangeAroundSlot(slotIndex, lineLength),
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
            QueueStraightConveyorLineRetryWorkDirectFallback(
                line, wakeRange.minSlotIndex, wakeRange.fullLine ? int.MaxValue : wakeRange.maxSlotIndex,
                directFallbackBlock);
            return directFallbackBlock == null && line != null;
        }

        ResolveStraightConveyorLineWakeRange(line, ref wakeRange, out int minSlotIndex, out int maxSlotIndex);

        bool profileLine = MapObjectTickProfiler.IsEnabled;
        bool hasRetryWork;
        using (ConveyorLineRetryWorkMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileLine);
            hasRetryWork = HasStraightConveyorLineRetryWork(line, minSlotIndex, maxSlotIndex);
            EndConveyorRuntimeSample(
                profileLine,
                "ConveyorLineRetryWork",
                "Conveyor Line Retry Work",
                startTimestamp);
        }

        if (!hasRetryWork)
        {
            ClearStraightConveyorLineRetry(line.id);
            return true;
        }

        bool hasRuntimeBlocker;
        using (ConveyorLineBlockerScanMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileLine);
            hasRuntimeBlocker = HasStraightConveyorLineFastPathRuntimeBlocker(line, minSlotIndex, maxSlotIndex);
            EndConveyorRuntimeSample(
                profileLine,
                "ConveyorLineBlockerScan",
                "Conveyor Line Blocker Scan",
                startTimestamp);
        }

        if (hasRuntimeBlocker)
        {
            // Falling back abandons the entire range's fast-path tick.
            // Data-only blocks in that range still need their own wake; they
            // cannot depend on a materialized item behind them pushing a run.
            QueueStraightConveyorLineRetryWorkDirectFallback(
                line,
                minSlotIndex,
                maxSlotIndex,
                directFallbackBlock);
            return directFallbackBlock == null;
        }

        conveyorLinesTickedThisFrame.Add(lineId);
        conveyorLineTouchedBlocks.Clear();
        conveyorLineTouchedSet.Clear();
        conveyorLineTouchedMinSlotIndex = int.MaxValue;
        conveyorLineTouchedMaxSlotIndex = -1;

        bool movedAny = false;
        using (ConveyorLineMoveScanMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileLine);
            movedAny |= TryTickStraightConveyorLine(line, minSlotIndex, maxSlotIndex);
            EndConveyorRuntimeSample(
                profileLine,
                "ConveyorLineMoveScan",
                "Conveyor Line Move Scan",
                startTimestamp);
        }

        if (!movedAny)
        {
            bool hasPostNoMoveRetryWork;
            using (ConveyorLineNoMoveMarker.Auto())
            {
                long startTimestamp = BeginConveyorRuntimeSample(profileLine);
                lastActiveConveyorLineNoMoveWakes++;
                lastActiveConveyorLineNoMoveBlocksChanged += NotifyStraightConveyorLineTickCompleted(
                    line,
                    minSlotIndex,
                    maxSlotIndex,
                    out hasPostNoMoveRetryWork);
                EndConveyorRuntimeSample(
                    profileLine,
                    "ConveyorLineNoMove",
                    "Conveyor Line No Move",
                    startTimestamp);
            }

            if (hasPostNoMoveRetryWork)
            {
                int directFallbackCount = QueueStraightConveyorLineRetryWorkDirectFallback(
                    line,
                    minSlotIndex,
                    maxSlotIndex,
                    directFallbackBlock);
                if (directFallbackCount > 0)
                {
                    lastActiveConveyorLineNoMoveDirectFallbacks += directFallbackCount;
                    ClearStraightConveyorLineRetry(line.id);
                }
                else
                {
                    DelayStraightConveyorLineRetry(line.id, minSlotIndex, maxSlotIndex);
                }
            }
            else
            {
                ClearStraightConveyorLineRetry(line.id);
            }

            return directFallbackBlock == null;
        }

        ClearStraightConveyorLineRetry(line.id);
        using (ConveyorLineWakeRefreshMarker.Auto())
        {
            long startTimestamp = BeginConveyorRuntimeSample(profileLine);
            WakeAndRefreshConveyorRuntimeBlocks(
                conveyorLineTouchedBlocks,
                false,
                true,
                ConveyorRuntimeWakeMode.None);
            DeferMovedStraightConveyorLineWake(line);
            EndConveyorRuntimeSample(
                profileLine,
                "ConveyorLineWakeRefresh",
                "Conveyor Line Wake Refresh",
                startTimestamp);
        }

        return true;
    }

    private bool HasStraightConveyorLineRetryWork(ConveyorLine line, int minSlotIndex, int maxSlotIndex)
    {
        if (line == null || line.blockHandles.Count <= 0)
        {
            return false;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            if (TryResolveConveyorLineBlock(line, i, out Block block)
                && block.HasStraightConveyorLineRetryWork())
            {
                return true;
            }
        }

        return false;
    }

    private int NotifyStraightConveyorLineTickCompleted(
        ConveyorLine line,
        int minSlotIndex,
        int maxSlotIndex,
        out bool hasRetryWork)
    {
        hasRetryWork = false;
        if (line == null)
        {
            return 0;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        int changedCount = 0;
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            if (!TryResolveConveyorLineBlock(line, i, out Block block))
            {
                continue;
            }

            if (!IsActiveConveyor(block) && block.GetRuntimeConveyorItemCount() <= 0)
            {
                lastActiveConveyorLineNoMoveBlocksSkipped++;
                continue;
            }

            if (block.NotifyStraightConveyorLineTickCompleted(
                    out bool blockHasRetryWork,
                    out bool skippedNoMoveWork))
            {
                changedCount++;
            }
            else if (skippedNoMoveWork)
            {
                lastActiveConveyorLineNoMoveBlocksSkipped++;
            }

            if (!hasRetryWork && blockHasRetryWork)
            {
                hasRetryWork = true;
            }
        }

        return changedCount;
    }

    private ConveyorLine FindConveyorLine(int lineId)
    {
        return conveyorLinesById.TryGetValue(lineId, out ConveyorLine line) ? line : null;
    }

    private bool TryResolveConveyorLineBlock(
        ConveyorLine line,
        int slotIndex,
        out Block block)
    {
        block = null;
        return line != null
            && slotIndex >= 0
            && slotIndex < line.blockHandles.Count
            && TryResolveLoadedRuntimeBlock(line.blockHandles[slotIndex], out block);
    }

    private static bool CanTickStraightConveyorLine(ConveyorLine line)
    {
        return line != null
            && !line.isCycle
            && line.simulationCacheValid
            && line.blockHandles.Count > 0;
    }

    private static void ResolveStraightConveyorLineWakeRange(
        ConveyorLine line,
        ref ConveyorLineWakeRange wakeRange,
        out int minSlotIndex,
        out int maxSlotIndex)
    {
        minSlotIndex = 0;
        maxSlotIndex = line != null ? line.blockHandles.Count - 1 : -1;
        if (line == null || line.blockHandles.Count <= 0 || wakeRange.fullLine)
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
        if (line == null || line.blockHandles.Count <= 0)
        {
            minSlotIndex = 0;
            maxSlotIndex = -1;
            return;
        }

        minSlotIndex = Mathf.Clamp(minSlotIndex, 0, line.blockHandles.Count - 1);
        maxSlotIndex = Mathf.Clamp(maxSlotIndex, minSlotIndex, line.blockHandles.Count - 1);
    }

    private bool HasStraightConveyorLineFastPathRuntimeBlocker(ConveyorLine line, int minSlotIndex, int maxSlotIndex)
    {
        if (line == null)
        {
            return true;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            if (!TryResolveConveyorLineBlock(line, i, out Block block)
                || block.HasStraightConveyorLineFastPathRuntimeBlocker())
            {
                return true;
            }
        }

        return false;
    }

    private int QueueStraightConveyorLineRetryWorkDirectFallback(
        ConveyorLine line,
        int minSlotIndex,
        int maxSlotIndex,
        Block directFallbackBlock)
    {
        if (line == null)
        {
            return 0;
        }

        ResolveStraightConveyorLineSlotRange(line, ref minSlotIndex, ref maxSlotIndex);
        int queuedCount = 0;
        for (int i = minSlotIndex; i <= maxSlotIndex; i++)
        {
            if (!TryResolveConveyorLineBlock(line, i, out Block block)
                || block == directFallbackBlock)
            {
                continue;
            }

            if (!block.ShouldTickActiveConveyor() && block.GetRuntimeConveyorItemCount() <= 0)
            {
                continue;
            }

            QueueConveyorDirectWake(block);
            queuedCount++;
        }

        return queuedCount;
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
            if (!TryResolveConveyorLineBlock(line, i, out Block block))
            {
                return false;
            }

            Block nextBlock = null;
            if (i < line.blockHandles.Count - 1
                && !TryResolveConveyorLineBlock(line, i + 1, out nextBlock))
            {
                return false;
            }

            int frontLaneIndex = line.frontLaneIndices[i];
            int backLaneIndex = line.backLaneIndices[i];

            if (nextBlock != null
                && block.CanMoveStraightConveyorDataLaneToCached(
                    nextBlock,
                    frontLaneIndex,
                    line.backLaneIndices[i + 1])
                && block.TryMoveStraightConveyorDataLaneToCached(
                    nextBlock,
                    frontLaneIndex,
                    line.backLaneIndices[i + 1],
                    line.nextPathLengths[i]))
            {
                MarkConveyorLineBlockTouched(block, i);
                MarkConveyorLineBlockTouched(nextBlock, i + 1);
                movedAny = true;
            }
            else if (nextBlock != null
                && block.HasStraightConveyorDataItemAtLane(frontLaneIndex)
                && block.TryAdvanceStraightConveyorLineLane(frontLaneIndex, true))
            {
                MarkConveyorLineBlockTouched(block, i);
                movedAny = true;
            }
            else if (i == line.blockHandles.Count - 1
                && block.HasStraightConveyorDataItemAtLane(frontLaneIndex)
                && HasNonLineConveyorSuccessor(block)
                && block.TryAdvanceStraightConveyorLineLane(frontLaneIndex, true))
            {
                MarkConveyorLineBlockTouched(block, i);
                movedAny = true;
            }

            if (block.CanMoveStraightConveyorDataLaneToCached(
                    block,
                    backLaneIndex,
                    frontLaneIndex)
                && block.TryMoveStraightConveyorDataLaneToCached(
                    block,
                    backLaneIndex,
                    frontLaneIndex,
                    line.withinPathLengths[i]))
            {
                MarkConveyorLineBlockTouched(block, i);
                movedAny = true;
            }
            else if (block.HasStraightConveyorDataItemAtLane(backLaneIndex)
                && block.TryAdvanceStraightConveyorLineLane(backLaneIndex, true))
            {
                MarkConveyorLineBlockTouched(block, i);
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

    private void MarkConveyorLineBlockTouched(Block block, int slotIndex)
    {
        if (TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            && conveyorLineTouchedSet.Add(handle))
        {
            conveyorLineTouchedBlocks.Add(handle);
        }

        if (slotIndex < 0)
        {
            return;
        }

        conveyorLineTouchedMinSlotIndex = Mathf.Min(conveyorLineTouchedMinSlotIndex, slotIndex);
        conveyorLineTouchedMaxSlotIndex = Mathf.Max(conveyorLineTouchedMaxSlotIndex, slotIndex);
    }

    private void DeferMovedStraightConveyorLineWake(ConveyorLine line)
    {
        if (line == null || line.id <= 0 || conveyorLineTouchedBlocks.Count == 0)
        {
            return;
        }

        ConveyorLineWakeRange wakeRange = CreateConveyorLineWakeRangeAroundSlots(
            conveyorLineTouchedMinSlotIndex,
            conveyorLineTouchedMaxSlotIndex,
            line.blockHandles.Count);
        if (wakeRange.fullLine)
        {
            DeferConveyorLineWake(line.id, wakeRange);
            return;
        }

        int wakeSlotCount = Mathf.Max(0, wakeRange.maxSlotIndex - wakeRange.minSlotIndex + 1);
        lastActiveConveyorMovedLineWakesScheduled++;
        lastActiveConveyorMovedLineWakeSlots += wakeSlotCount;
        ScheduleStraightConveyorLineReadyWake(line.id, wakeRange.minSlotIndex, wakeRange.maxSlotIndex);
    }

    private void EnsureSortedActiveConveyors()
    {
        if (!activeConveyorOrderDirty)
        {
            return;
        }

        activeConveyors.RemoveWhere(handle =>
            !TryResolveLoadedRuntimeBlock(handle, out _));
        sortedActiveConveyors.Clear();
        foreach (BlockHandle handle in activeConveyors)
        {
            sortedActiveConveyors.Add(handle);
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

        using (ConveyorRebuildNetworkCacheMarker.Auto())
        {
            RebuildConveyorNetworkCache();
        }
    }

    private void RebuildConveyorNetworkCache()
    {
        conveyorNetworkCacheDirty = false;
        conveyorNetworkIds.Clear();
        conveyorNetworkBlocksById.Clear();
        conveyorNetworkActiveIds.Clear();
        conveyorNetworkBuildQueue.Clear();

        int nextNetworkId = 1;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block startBlock = pair.Value;
            if (startBlock == null
                || !startBlock.IsRuntimeConveyor
                || !TryGetRuntimeBlockHandle(startBlock, out BlockHandle startHandle)
                || conveyorNetworkIds.ContainsKey(startHandle))
            {
                continue;
            }

            int networkId = nextNetworkId++;
            conveyorNetworkActiveIds.Add(networkId);
            AddConveyorBlockToNetwork(startBlock, networkId);
            conveyorNetworkBuildQueue.Enqueue(startHandle);

            while (conveyorNetworkBuildQueue.Count > 0)
            {
                BlockHandle handle = conveyorNetworkBuildQueue.Dequeue();
                if (!TryResolveLoadedRuntimeBlock(handle, out Block block))
                {
                    continue;
                }

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
        if (!hasNeighbor
            || neighborBlock == null
            || !neighborBlock.IsRuntimeConveyor
            || !TryGetRuntimeBlockHandle(neighborBlock, out BlockHandle neighborHandle)
            || conveyorNetworkIds.ContainsKey(neighborHandle))
        {
            return;
        }

        AddConveyorBlockToNetwork(neighborBlock, networkId);
        conveyorNetworkBuildQueue.Enqueue(neighborHandle);
    }

    private void AddConveyorBlockToNetwork(Block block, int networkId)
    {
        if (block == null
            || networkId <= 0
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            return;
        }

        conveyorNetworkIds[handle] = networkId;
        if (!conveyorNetworkBlocksById.TryGetValue(networkId, out List<BlockHandle> networkBlocks))
        {
            networkBlocks = new List<BlockHandle>();
            conveyorNetworkBlocksById[networkId] = networkBlocks;
        }

        networkBlocks.Add(handle);
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
        ClearConveyorBlockedLaneWaiters();
        ClearConveyorLineWakeQueue();
        ClearConveyorCornerGroupCache();
        conveyorLineCacheDirty = true;
    }

    private void ClearConveyorLineWakeQueue()
    {
        conveyorLineWakeQueue.Clear();
        conveyorLineWakeRangesById.Clear();
        deferredConveyorLineWakeQueue.Clear();
        deferredConveyorLineWakeRangesById.Clear();
        ClearConveyorCornerGroupWakeQueue();
    }

    private void ClearConveyorCornerGroupCache()
    {
        conveyorCornerGroups.Clear();
        conveyorCornerGroupsById.Clear();
        conveyorCornerGroupSlots.Clear();
        conveyorCornerGroupVisited.Clear();
        conveyorCornerGroupBuildIndices.Clear();
        conveyorCornerGroupTickBlocks.Clear();
    }

    private void ClearConveyorCornerGroupWakeQueue()
    {
        conveyorCornerGroupWakeQueue.Clear();
        conveyorCornerGroupWakeQueued.Clear();
        foreach (KeyValuePair<int, List<BlockHandle>> pair in conveyorCornerGroupWakeBlocksById)
        {
            pair.Value?.Clear();
        }

        conveyorCornerGroupWakeBlocksById.Clear();
        conveyorCornerGroupWakeQueuedBlocks.Clear();
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
        ClearConveyorCornerGroupCache();

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
            if (!IsRuntimeConveyorLineBlock(block)
                || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
                || conveyorLineVisited.Contains(handle))
            {
                continue;
            }

            if (TryBuildConveyorLine(block, nextLineId))
            {
                nextLineId++;
            }
        }

        BuildConveyorCornerGroupCache();
    }

    private bool TryBuildConveyorLine(Block startBlock, int lineId)
    {
        if (!IsRuntimeConveyorLineBlock(startBlock)
            || !TryGetRuntimeBlockHandle(startBlock, out BlockHandle startHandle)
            || conveyorLineVisited.Contains(startHandle))
        {
            return false;
        }

        ConveyorLine line = new ConveyorLine(lineId);
        conveyorLineBuildIndices.Clear();
        bool isCycle = false;
        Block currentBlock = startBlock;

        while (IsRuntimeConveyorLineBlock(currentBlock))
        {
            if (!TryGetRuntimeBlockHandle(currentBlock, out BlockHandle currentHandle))
            {
                break;
            }

            if (conveyorLineBuildIndices.TryGetValue(currentHandle, out int loopStartIndex))
            {
                isCycle = loopStartIndex == 0;
                break;
            }

            if (conveyorLineVisited.Contains(currentHandle))
            {
                break;
            }

            conveyorLineBuildIndices[currentHandle] = line.blockHandles.Count;
            conveyorLineVisited.Add(currentHandle);
            line.blockHandles.Add(currentHandle);

            if (!currentBlock.TryGetRuntimeNextConveyorBlock(out Block nextBlock)
                || !IsStraightConveyorLineSuccessor(currentBlock, nextBlock))
            {
                break;
            }

            currentBlock = nextBlock;
        }

        if (line.blockHandles.Count == 0)
        {
            return false;
        }

        line.isCycle = isCycle;
        line.simulationCacheValid = PopulateConveyorLineSimulationCache(line);
        conveyorLines.Add(line);
        conveyorLinesById[line.id] = line;

        int lineLength = line.blockHandles.Count;
        for (int i = 0; i < lineLength; i++)
        {
            conveyorLineSlots[line.blockHandles[i]] = new ConveyorLineSlot(line.id, i, lineLength, line.isCycle);
        }

        return true;
    }

    private bool PopulateConveyorLineSimulationCache(ConveyorLine line)
    {
        if (line == null || line.blockHandles.Count <= 0 || line.isCycle)
        {
            return false;
        }

        int lineLength = line.blockHandles.Count;
        line.frontLaneIndices = new int[lineLength];
        line.backLaneIndices = new int[lineLength];
        line.withinPathLengths = new float[lineLength];
        line.nextPathLengths = new float[lineLength];

        for (int i = 0; i < lineLength; i++)
        {
            if (!TryResolveConveyorLineBlock(line, i, out Block block))
            {
                return false;
            }

            Block nextBlock = null;
            if (i < lineLength - 1
                && !TryResolveConveyorLineBlock(line, i + 1, out nextBlock))
            {
                return false;
            }

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
        if (!IsRuntimeConveyorLineBlock(block)
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || conveyorLineVisited.Contains(handle))
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

    private void BuildConveyorCornerGroupCache()
    {
        int nextGroupId = 1;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (!IsConveyorCornerGroupStartBlock(block))
            {
                continue;
            }

            if (TryBuildConveyorCornerGroup(block, nextGroupId))
            {
                nextGroupId++;
            }
        }

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (!IsRuntimeConveyorCornerGroupBlock(block)
                || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
                || conveyorCornerGroupVisited.Contains(handle))
            {
                continue;
            }

            if (TryBuildConveyorCornerGroup(block, nextGroupId))
            {
                nextGroupId++;
            }
        }
    }

    private bool TryBuildConveyorCornerGroup(Block startBlock, int groupId)
    {
        if (!IsRuntimeConveyorCornerGroupBlock(startBlock)
            || !TryGetRuntimeBlockHandle(startBlock, out BlockHandle startHandle)
            || conveyorCornerGroupVisited.Contains(startHandle))
        {
            return false;
        }

        ConveyorCornerGroup group = new ConveyorCornerGroup(groupId);
        conveyorCornerGroupBuildIndices.Clear();
        bool isCycle = false;
        Block currentBlock = startBlock;

        while (IsRuntimeConveyorCornerGroupBlock(currentBlock))
        {
            if (!TryGetRuntimeBlockHandle(currentBlock, out BlockHandle currentHandle))
            {
                break;
            }

            if (conveyorCornerGroupBuildIndices.TryGetValue(currentHandle, out int loopStartIndex))
            {
                isCycle = loopStartIndex == 0;
                break;
            }

            if (conveyorCornerGroupVisited.Contains(currentHandle))
            {
                break;
            }

            conveyorCornerGroupBuildIndices[currentHandle] = group.blockHandles.Count;
            conveyorCornerGroupVisited.Add(currentHandle);
            group.blockHandles.Add(currentHandle);

            if (!currentBlock.TryGetRuntimeNextConveyorBlock(out Block nextBlock)
                || !IsConveyorCornerGroupSuccessor(currentBlock, nextBlock))
            {
                break;
            }

            currentBlock = nextBlock;
        }

        if (group.blockHandles.Count == 0)
        {
            return false;
        }

        group.isCycle = isCycle;
        conveyorCornerGroups.Add(group);
        conveyorCornerGroupsById[group.id] = group;

        int groupLength = group.blockHandles.Count;
        for (int i = 0; i < groupLength; i++)
        {
            conveyorCornerGroupSlots[group.blockHandles[i]] = new ConveyorCornerGroupSlot(
                group.id,
                i,
                groupLength,
                group.isCycle);
        }

        return true;
    }

    private bool IsConveyorCornerGroupStartBlock(Block block)
    {
        if (!IsRuntimeConveyorCornerGroupBlock(block)
            || !TryGetRuntimeBlockHandle(block, out BlockHandle handle)
            || conveyorCornerGroupVisited.Contains(handle))
        {
            return false;
        }

        return !block.TryGetRuntimePreviousConveyorBlock(out Block previousBlock)
            || !IsConveyorCornerGroupSuccessor(previousBlock, block);
    }

    private static bool IsRuntimeConveyorCornerGroupBlock(Block block)
    {
        return block != null
            && block.IsRuntimeConveyor
            && block.IsCornerConveyorBlock();
    }

    private static bool IsConveyorCornerGroupSuccessor(Block block, Block nextBlock)
    {
        return IsRuntimeConveyorCornerGroupBlock(block)
            && IsRuntimeConveyorCornerGroupBlock(nextBlock)
            && block.TryGetRuntimeNextConveyorBlock(out Block resolvedNextBlock)
            && resolvedNextBlock == nextBlock;
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
            BlockHandle handle = activeConveyorDotVisualList[index];
            if (!TryResolveLoadedRuntimeBlock(handle, out Block block)
                || !block.IsConveyorStackingEnabled())
            {
                activeConveyorDotVisuals.Remove(handle);
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
            BlockHandle handle = activeBeltDirectionVisualList[index];
            if (!TryResolveLoadedRuntimeBlock(handle, out Block block)
                || !TryAppendDirectionArrowMatrices(block))
            {
                activeBeltDirectionVisuals.Remove(handle);
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
        int activeBlockRootCount = loadedBlocks.Count > 0 && gameObject.activeInHierarchy ? 1 : 0;
        int inactiveBlockRootCount = loadedBlocks.Count > 0 && !gameObject.activeInHierarchy ? 1 : 0;
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
        int currentLineReadyDelayStates = 0;
        foreach (KeyValuePair<int, ConveyorLineRetryState> pair in conveyorLineRetryStatesById)
        {
            int attemptCount = Mathf.Max(0, pair.Value.attemptCount);
            retryAttemptSampleCount++;
            totalLineRetryAttempts += attemptCount;
            maxLineRetryAttempt = Mathf.Max(maxLineRetryAttempt, attemptCount);
            if (pair.Value.readyDelay)
            {
                currentLineReadyDelayStates++;
            }
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
        MapObjectTickProfiler.AddRuntimeCounter(
            "World",
            "ChunkGameObjects",
            createdBlockRuntimeProxyHostCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "DedicatedBlockGameObjects", 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "World",
            "BlockHostGameObjects",
            loadedBlocks.Count > 0 ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter("World", "BlockComponents", loadedBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter(
            "World",
            "ChunkSurfaceMeshes",
            GetLoadedChunkSurfaceMeshCount());
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedBlocks", loadedBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter(
            "World",
            "BlockSimulationStates",
            loadedBlocks.RuntimeSimulationStateCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "RegisteredBlockCells", loadedBlocks.RegisteredCellCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "World",
            "DataOnlyBlockCells",
            Mathf.Max(0, loadedBlocks.RegisteredCellCount - loadedBlocks.Count));
        MapObjectTickProfiler.AddRuntimeCounter("World", "BlockDataChunks", loadedBlocks.ChunkCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedMapObjects", loadedMapObjectCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedInstallations", loadedInstallationCount);
        MapObjectTickProfiler.AddRuntimeCounter("World", "LoadedConveyorBelts", loadedConveyorBeltCount);

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

        GameManager gameManager = GameManager.Instance;
        MapObjectTickProfiler.AddRuntimeCounter("RenderToggles", "HideBelts", gameManager != null && gameManager.HideBelts);
        MapObjectTickProfiler.AddRuntimeCounter("RenderToggles", "HideBeltItems", gameManager != null && gameManager.HideBeltItems);

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

        PortableItemRenderer itemRenderer = portableItemRenderer;
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "ItemBatchCellSize", itemRenderer != null ? itemRenderer.VirtualConveyorItemBatchCellSize : 0f);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "PortableBrgBatches", itemRenderer != null ? itemRenderer.PortableObjectBatchRendererGroupBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "StaticBatches", itemRenderer != null ? itemRenderer.StaticVirtualConveyorItemBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "StaticBrgBatches", itemRenderer != null ? itemRenderer.StaticVirtualConveyorItemBatchRendererGroupBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "StaticInstances", itemRenderer != null ? itemRenderer.StaticVirtualConveyorItemInstanceCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "GpuMotionInstances", itemRenderer != null ? itemRenderer.GpuMotionVirtualConveyorItemInstanceCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "StaticDrawCalls", itemRenderer != null ? itemRenderer.StaticVirtualConveyorItemDrawCallCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicBatches", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorItemBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicBrgBatches", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorItemBatchRendererGroupBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicInstances", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorItemInstanceCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicDrawCalls", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorItemDrawCallCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "ActiveRenderBlocks", itemRenderer != null ? itemRenderer.ActiveVirtualConveyorRenderBlockCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicRenderBlocks", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorRenderBlockCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DirtyRenderBlocks", itemRenderer != null ? itemRenderer.DirtyVirtualConveyorRenderBlockCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "CachedRenderBlocks", itemRenderer != null ? itemRenderer.CachedVirtualConveyorRenderBlockCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "CachedDynamicRenderBlocks", itemRenderer != null ? itemRenderer.CachedDynamicVirtualConveyorRenderBlockCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "CachedItemRenderAssets", itemRenderer != null ? itemRenderer.CachedItemRenderAssetCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullSourceBlocks", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullSourceBlocks : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullCandidateBlocks", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullCandidateBlocks : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullCacheRefreshes", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullCacheRefreshes : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullCachedBlocks", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullCachedBlocks : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullLayerSkippedBlocks", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullLayerSkippedBlocks : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullFrustumSkippedBlocks", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullFrustumSkippedBlocks : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullPassedBlocks", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullPassedBlocks : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicRenderedItems", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorRenderedItems : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicKeyCacheHits", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorKeyCacheHits : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicKeyCacheMisses", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorKeyCacheMisses : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicKeyRebuilds", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorKeyRebuilds : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicMatrixUpdates", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorMatrixUpdates : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicMatrixRebuilds", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorMatrixRebuilds : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicTransformJobItems", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorTransformJobItems : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicTransformJobScheduled", itemRenderer != null && itemRenderer.DynamicVirtualConveyorTransformJobScheduled ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullBoundsSize", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullBoundsSize : 0f);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorItemRender", "DynamicCullBoundsHeight", itemRenderer != null ? itemRenderer.DynamicVirtualConveyorCullBoundsHeight : 0f);

        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "SavedBlocks", lastConveyorItemLoadSavedBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "SavedLanes", lastConveyorItemLoadSavedLanes);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "LoadedBlocks", lastConveyorItemLoadLoadedBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "PendingBlocks", lastConveyorItemLoadPendingBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "PendingLanes", lastConveyorItemLoadPendingLanes);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "NotRuntimeBlocks", lastConveyorItemLoadNotRuntimeBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "ZeroLaneBlocks", lastConveyorItemLoadZeroLaneBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "AppliedLanes", lastConveyorItemLoadAppliedLanes);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "FallbackBlocks", lastConveyorItemLoadFallbackBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "ActualFailedBlocks", lastConveyorItemLoadActualFailedBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorLoad", "ActualFailedLanes", lastConveyorItemLoadActualFailedLanes);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorStateSave", "SaveConveyorItemsCalls", conveyorStateSaveConveyorBlocks);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorStateSave", "SavedItems", conveyorStateSaveConveyorItems);
        MapObjectTickProfiler.AddRuntimeCounter(
            "ConveyorStateSave",
            "ClearedNonConveyorBlocks",
            conveyorStateSaveClearedNonConveyorBlocks);

        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "WakeQueue", conveyorWakeQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "WakeQueuedSet", conveyorWakeQueued.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DirectWakeBlocks", conveyorDirectWakeBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "LineWakeQueue", conveyorLineWakeQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredLineWakeQueue", deferredConveyorLineWakeQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "CornerGroupWakeQueue", conveyorCornerGroupWakeQueue.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "CornerGroupWakeQueuedSet", conveyorCornerGroupWakeQueued.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "CornerGroupWakeQueuedBlocks", conveyorCornerGroupWakeQueuedBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "LineRetryStates", conveyorLineRetryStatesById.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "LineRetryDueLines", conveyorLineRetryAttemptsByDueLineId.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "LineReadyDelayStates", currentLineReadyDelayStates);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "BlockedWaiterDestinations", conveyorBlockedSourcesByDestinationLane.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "BlockedWaiterSources", conveyorBlockedDestinationBySourceLane.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "MaxLineRetryAttempt", maxLineRetryAttempt);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "AvgLineRetryAttempt", averageLineRetryAttempt);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "ActiveSafetyScanBudget", GetEffectiveActiveConveyorSafetyScanBudget(activeConveyors.Count));
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "ActiveSafetyScanIndex", activeConveyorSafetyScanIndex);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "NetworkSleepChecks", conveyorNetworkSleepCheckQueuedIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredRuntimeRefreshBlocks", deferredConveyorRuntimeRefreshBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredNetworkWakeBlocks", deferredConveyorNetworkWakeBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredWakeAroundBlocks", deferredConveyorMoveAttemptWakeAroundBlocks.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorQueue", "DeferredWakeFlowBlocks", deferredConveyorMoveAttemptWakeFlowBlocks.Count);

        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LastTickFrame", lastActiveConveyorTickFrame);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "QueuedAtStart", lastActiveConveyorQueuedAtStart);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "ProcessLimit", lastActiveConveyorProcessLimit);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "Processed", lastActiveConveyorProcessed);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LineWakesProcessed", lastActiveConveyorLineWakesProcessed);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "BlockWakesProcessed", lastActiveConveyorBlockWakesProcessed);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "CornerGroupWakesProcessed", lastActiveConveyorCornerGroupWakesProcessed);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "CornerGroupBlocksProcessed", lastActiveConveyorCornerGroupBlocksProcessed);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "CornerGroupBlocksQueued", lastActiveConveyorCornerGroupBlocksQueued);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "CornerGroupBlocksSelected", lastActiveConveyorCornerGroupBlocksSelected);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "CornerGroupBlocksSkipped", lastActiveConveyorCornerGroupBlocksSkipped);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "CornerGroupNoProgressRequeuesSkipped", lastActiveConveyorCornerGroupNoProgressRequeuesSkipped);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "BlockWakeTicks", lastActiveConveyorBlockWakeTicks);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "BlockNoProgressRequeuesSkipped", lastActiveConveyorBlockNoProgressRequeuesSkipped);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "DuplicateFrameTicksSkipped", lastActiveConveyorDuplicateFrameTicksSkipped);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "BlockWakeLineFallbacks", lastActiveConveyorBlockWakeLineFallbacks);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "FullLineWakesProcessed", lastActiveConveyorFullLineWakesProcessed);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "RangedLineWakesProcessed", lastActiveConveyorRangedLineWakesProcessed);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "DeferredLineWakesPromoted", lastActiveConveyorDeferredLineWakesPromoted);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LineNoMoveWakes", lastActiveConveyorLineNoMoveWakes);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LineNoMoveBlocksChanged", lastActiveConveyorLineNoMoveBlocksChanged);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LineNoMoveBlocksSkipped", lastActiveConveyorLineNoMoveBlocksSkipped);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LineNoMoveDirectFallbacks", lastActiveConveyorLineNoMoveDirectFallbacks);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LineWakesDroppedByRetryThrottle", lastActiveConveyorLineWakesDroppedByRetryThrottle);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "DeferredLineWakesDroppedByRetryThrottle", lastActiveConveyorDeferredLineWakesDroppedByRetryThrottle);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "LineRetryRangeMerges", lastActiveConveyorLineRetryRangeMerges);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "RetryStatesScanned", lastActiveConveyorRetryStatesScanned);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "RetryWakesQueued", lastActiveConveyorRetryWakesQueued);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "ReadyDelayStatesScanned", lastActiveConveyorReadyDelayStates);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "SafetyWakesQueued", lastActiveConveyorSafetyWakesQueued);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "MovedLineWakesScheduled", lastActiveConveyorMovedLineWakesScheduled);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "MovedLineWakeSlots", lastActiveConveyorMovedLineWakeSlots);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "BlockedWaiterRegistrations", lastActiveConveyorBlockedWaiterRegistrations);
        MapObjectTickProfiler.AddRuntimeCounter("ActiveConveyor", "BlockedWaitersWoken", lastActiveConveyorBlockedWaitersWoken);

        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "LineCacheDirty", conveyorLineCacheDirty);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkCacheDirty", conveyorNetworkCacheDirty);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "Lines", conveyorLines.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "LineSlots", conveyorLineSlots.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "CornerGroups", conveyorCornerGroups.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "CornerGroupSlots", conveyorCornerGroupSlots.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "Networks", conveyorNetworkBlocksById.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkIds", conveyorNetworkIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkSleeping", conveyorNetworkSleepingIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkActive", conveyorNetworkActiveIds.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NetworkRetries", conveyorNetworkRetryTimes.Count);
        MapObjectTickProfiler.AddRuntimeCounter("ConveyorCache", "NextLineRetryMs", FormatRuntimeRetryMs());

        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltRegistered", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.RegisteredBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltCorners", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.RegisteredCornerBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltSourceHidden", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.HiddenSourceViewBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltSourceHiddenObjects", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.HiddenSourceViewObjectCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltEffectiveBatchCellSize", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.EffectiveBatchCellSize : 0f);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltBatches", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltBrgBatches", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveBatchRendererGroupBatchCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltEntries", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveEntryCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltDedicatedTopEntries", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.DedicatedBeltTopEntryCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltTrackedTransformEntries", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.TrackedTransformEntryCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltTrackedTransformUpdates", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.LastTrackedTransformMatrixUpdates : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltInstances", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveInstanceCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("Virtualization", "VirtualBeltDrawCalls", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.EstimatedDrawCallCount : 0);

        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "NativeRendererTotal", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.NativeRendererCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "NativeRendererEnabled", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.EnabledNativeRendererCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "NativeRendererActiveEnabled", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.ActiveEnabledNativeRendererCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "NativeRendererSuppressed", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.SuppressedNativeRendererCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "NativeSourceSuppressionEnabled", virtualConveyorBeltRenderer != null && virtualConveyorBeltRenderer.NativeSourceSuppressionEnabled);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "NativeSourceObjectHidingEnabled", virtualConveyorBeltRenderer != null && virtualConveyorBeltRenderer.NativeSourceObjectHidingEnabled);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "SourceViewHiddenBelts", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.HiddenSourceViewBeltCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "SourceViewHiddenObjects", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.HiddenSourceViewObjectCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter("VirtualBelt", "VirtualizedBelts", virtualConveyorBeltRenderer != null ? virtualConveyorBeltRenderer.VirtualizedBeltCount : 0);
        ResetConveyorStateSaveCounters();
    }

    private void ResetConveyorStateSaveCounters()
    {
        conveyorStateSaveConveyorBlocks = 0;
        conveyorStateSaveConveyorItems = 0;
        conveyorStateSaveClearedNonConveyorBlocks = 0;
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

    private int CompareActiveConveyorTickOrder(BlockHandle left, BlockHandle right)
    {
        if (left == right)
        {
            return 0;
        }

        bool hasLeftCoordinate = loadedBlocks.TryGetCoordinate(left, out Vector2Int leftCoordinate);
        bool hasRightCoordinate = loadedBlocks.TryGetCoordinate(right, out Vector2Int rightCoordinate);
        if (!hasLeftCoordinate || !hasRightCoordinate)
        {
            if (hasLeftCoordinate != hasRightCoordinate)
            {
                return hasLeftCoordinate ? -1 : 1;
            }

            int chunkYComparison = left.ChunkCoordinate.y.CompareTo(right.ChunkCoordinate.y);
            if (chunkYComparison != 0)
            {
                return chunkYComparison;
            }

            int chunkXComparison = left.ChunkCoordinate.x.CompareTo(right.ChunkCoordinate.x);
            if (chunkXComparison != 0)
            {
                return chunkXComparison;
            }

            int localIndexComparison = left.LocalIndex.CompareTo(right.LocalIndex);
            return localIndexComparison != 0
                ? localIndexComparison
                : left.Generation.CompareTo(right.Generation);
        }

        int yComparison = leftCoordinate.y.CompareTo(rightCoordinate.y);
        if (yComparison != 0)
        {
            return yComparison;
        }

        return leftCoordinate.x.CompareTo(rightCoordinate.x);
    }
}
