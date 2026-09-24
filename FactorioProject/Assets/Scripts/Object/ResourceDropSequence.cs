using System.Collections.Generic;

public static class ResourceDropSequence
{
    private const int WeightPrecision = 10000;

    public static bool TrySelect(
        IReadOnlyList<ResourceDropEntry> entries,
        float growth,
        int depletionOrdinal,
        int initialResourceCount,
        int sequenceSeed,
        out ResourceDropItem selectedItem)
    {
        selectedItem = null;
        if (entries == null || entries.Count == 0 || initialResourceCount <= 0)
        {
            return false;
        }

        int totalWeightUnits = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ResourceDropEntry entry = entries[i];
            if (entry == null || !entry.Matches(growth))
            {
                continue;
            }

            IReadOnlyList<ResourceDropItem> items = entry.Items;
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                ResourceDropItem item = items[itemIndex];
                if (IsEligible(item))
                {
                    int weightUnits = QuantizeWeight(item.DropChance);
                    totalWeightUnits += weightUnits;
                }
            }
        }

        if (totalWeightUnits <= 0)
        {
            return false;
        }

        int localOrdinal = System.Math.Max(0, depletionOrdinal) % initialResourceCount;
        int remainingSlots = initialResourceCount;
        int assignedBaseSlots = CalculateAssignedBaseSlots(
            entries,
            growth,
            initialResourceCount,
            totalWeightUnits);
        int remainderBonusCount = initialResourceCount - assignedBaseSlots;
        int eligibleItemOrdinal = 0;
        ResourceDropItem lastEligibleItem = null;
        for (int i = 0; i < entries.Count; i++)
        {
            ResourceDropEntry entry = entries[i];
            if (entry == null || !entry.Matches(growth))
            {
                continue;
            }

            IReadOnlyList<ResourceDropItem> items = entry.Items;
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                ResourceDropItem item = items[itemIndex];
                if (!IsEligible(item))
                {
                    continue;
                }

                lastEligibleItem = item;
                int weightUnits = QuantizeWeight(item.DropChance);
                long weightedSlots = (long)initialResourceCount * weightUnits;
                int itemSlotCount = (int)(weightedSlots / totalWeightUnits);
                long remainder = weightedSlots % totalWeightUnits;
                if (ReceivesRemainderBonus(
                        entries,
                        growth,
                        initialResourceCount,
                        totalWeightUnits,
                        remainderBonusCount,
                        eligibleItemOrdinal,
                        remainder))
                {
                    itemSlotCount++;
                }

                int rotation = ResolveRotation(
                    sequenceSeed,
                    eligibleItemOrdinal,
                    remainingSlots);
                if (itemSlotCount >= remainingSlots
                    || IsBalancedSelection(
                        localOrdinal,
                        remainingSlots,
                        itemSlotCount,
                        rotation))
                {
                    selectedItem = item;
                    return true;
                }

                localOrdinal -= CountBalancedSelectionsBefore(
                    localOrdinal,
                    remainingSlots,
                    itemSlotCount,
                    rotation);
                remainingSlots -= itemSlotCount;
                eligibleItemOrdinal++;
            }
        }

        selectedItem = lastEligibleItem;
        return selectedItem != null;
    }

    private static int QuantizeWeight(float weight)
    {
        return System.Math.Max(
            1,
            (int)System.Math.Round(
                weight * WeightPrecision,
                System.MidpointRounding.AwayFromZero));
    }

    private static bool IsEligible(ResourceDropItem item)
    {
        return item?.ItemDefinition != null
               && item.ItemDefinition.id >= 0
               && item.Amount > 0
               && item.DropChance > 0f;
    }

    private static int CalculateAssignedBaseSlots(
        IReadOnlyList<ResourceDropEntry> entries,
        float growth,
        int resourceCount,
        int totalWeightUnits)
    {
        int assigned = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ResourceDropEntry entry = entries[i];
            if (entry == null || !entry.Matches(growth))
            {
                continue;
            }

            IReadOnlyList<ResourceDropItem> items = entry.Items;
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                ResourceDropItem item = items[itemIndex];
                if (IsEligible(item))
                {
                    assigned += (int)((long)resourceCount
                                      * QuantizeWeight(item.DropChance)
                                      / totalWeightUnits);
                }
            }
        }

        return assigned;
    }

    private static bool ReceivesRemainderBonus(
        IReadOnlyList<ResourceDropEntry> entries,
        float growth,
        int resourceCount,
        int totalWeightUnits,
        int remainderBonusCount,
        int candidateOrdinal,
        long candidateRemainder)
    {
        if (remainderBonusCount <= 0)
        {
            return false;
        }

        int higherPriorityCount = 0;
        int otherOrdinal = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ResourceDropEntry entry = entries[i];
            if (entry == null || !entry.Matches(growth))
            {
                continue;
            }

            IReadOnlyList<ResourceDropItem> items = entry.Items;
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                ResourceDropItem item = items[itemIndex];
                if (!IsEligible(item))
                {
                    continue;
                }

                long remainder = (long)resourceCount
                                 * QuantizeWeight(item.DropChance)
                                 % totalWeightUnits;
                if (remainder > candidateRemainder
                    || (remainder == candidateRemainder
                        && otherOrdinal < candidateOrdinal))
                {
                    higherPriorityCount++;
                }

                otherOrdinal++;
            }
        }

        return higherPriorityCount < remainderBonusCount;
    }

    private static bool IsBalancedSelection(
        int ordinal,
        int count,
        int selectedCount,
        int rotation)
    {
        return CountBalancedSelectionsBefore(
                   ordinal + 1,
                   count,
                   selectedCount,
                   rotation)
               > CountBalancedSelectionsBefore(
                   ordinal,
                   count,
                   selectedCount,
                   rotation);
    }

    private static int CountBalancedSelectionsBefore(
        int ordinal,
        int count,
        int selectedCount,
        int rotation)
    {
        if (ordinal <= 0 || selectedCount <= 0)
        {
            return 0;
        }

        if (ordinal >= count || selectedCount >= count)
        {
            return selectedCount;
        }

        int rotatedEnd = rotation + ordinal;
        int beforeRotation = ScaleFloor(rotation, selectedCount, count);
        if (rotatedEnd <= count)
        {
            return ScaleFloor(rotatedEnd, selectedCount, count) - beforeRotation;
        }

        return selectedCount
               - beforeRotation
               + ScaleFloor(rotatedEnd - count, selectedCount, count);
    }

    private static int ScaleFloor(int value, int numerator, int denominator)
    {
        return (int)((long)value * numerator / denominator);
    }

    private static int ResolveRotation(int seed, int itemOrdinal, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        uint itemSeed = unchecked((uint)seed)
                        ^ unchecked((uint)(itemOrdinal + 1) * 0x9E3779B9u);
        return (int)(Mix(itemSeed) % (uint)count);
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}
