// Frozen pre-optimization methods for differential regression checks only.
using System; using System.Collections.Generic;
partial class LegacyScheduler : SchedulerFixture {
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
}
