namespace ProjectF.Combat
{
    internal static class PlayerInventoryTransfer
    {
        public static bool TryMove(PlayerBag source, int sourceIndex, PlayerBag target, int targetIndex,
            int count, bool swap = false)
        {
            if (source == null || target == null || source == target && sourceIndex == targetIndex || count <= 0)
                return false;
            if (source.HasPendingSlotChanges(sourceIndex) || target.HasPendingSlotChanges(targetIndex)) return false;
            int itemId = source.GetSlotItemId(sourceIndex), sourceCount = source.GetSlotCount(sourceIndex);
            int targetId = target.GetSlotItemId(targetIndex), targetCount = target.GetSlotCount(targetIndex);
            int nextSourceId = swap ? targetId : itemId;
            int nextSourceCount = swap ? targetCount : sourceCount - count;
            int nextTargetCount = swap ? count : targetCount + count;
            if (itemId < 0 || source.GetSlotRemovableCount(sourceIndex) < count
                || swap && (count != sourceCount || target.GetSlotRemovableCount(targetIndex) < targetCount)
                || !swap && targetCount > 0 && targetId != itemId
                || !swap && count > target.GetAvailableSlotCapacityForItem(targetIndex, itemId)
                || nextSourceCount < 0 || nextSourceCount > source.GetSlotCapacityForItem(sourceIndex, nextSourceId)
                || nextTargetCount > target.GetSlotCapacityForItem(targetIndex, itemId)) return false;
            var sourceHealth = source.CaptureSlotInstallationHealth(sourceIndex, count);
            var targetHealth = target.CaptureSlotInstallationHealth(targetIndex, targetCount);
            if (!source.SetSlotContents(sourceIndex, nextSourceId, nextSourceCount, false, false)
                || !target.SetSlotContents(targetIndex, itemId, nextTargetCount, false, false))
            {
                source.SetSlotContents(sourceIndex, itemId, sourceCount, false, false);
                target.SetSlotContents(targetIndex, targetId, targetCount, false, false);
                source.SetSlotInstallationHealth(sourceIndex, sourceCount-count, count, sourceHealth);
                target.SetSlotInstallationHealth(targetIndex, 0, targetCount, targetHealth);
                return false;
            }
            target.SetSlotInstallationHealth(targetIndex, swap ? 0 : targetCount, count, sourceHealth);
            if (swap) source.SetSlotInstallationHealth(sourceIndex, 0, targetCount, targetHealth);
            source.ForceNotifyChanged();
            if (target != source) target.ForceNotifyChanged();
            return true;
        }
    }
}
