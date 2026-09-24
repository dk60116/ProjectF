using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CraftingPlanStep
{
    public readonly int ItemId;
    public readonly int OutputCount;
    public int ReservedOutputCount;

    public CraftingPlanStep(int itemId, int outputCount)
    {
        ItemId = itemId;
        OutputCount = Mathf.Max(1, outputCount);
    }
}

public sealed class CraftingPlanBuilder
{
    private const int MaximumRecursionDepth = 64;

    private readonly List<CraftingPlanStep> steps = new List<CraftingPlanStep>(16);
    private readonly List<CraftingTreeRuntime.IngredientEntry> consumedItems =
        new List<CraftingTreeRuntime.IngredientEntry>(16);
    private readonly Dictionary<int, int> ownedRemaining = new Dictionary<int, int>();
    private readonly Dictionary<int, int> consumedOwned = new Dictionary<int, int>();
    private readonly Dictionary<int, List<CraftedBatch>> craftedBatches =
        new Dictionary<int, List<CraftedBatch>>();
    private readonly HashSet<int> activeRecipes = new HashSet<int>();

    private Func<int, int> getOwnedCount;
    private Func<int, bool> canCraftItem;
    private int maximumStepCount;

    public IReadOnlyList<CraftingPlanStep> Steps => steps;
    public IReadOnlyList<CraftingTreeRuntime.IngredientEntry> ConsumedItems => consumedItems;

    public bool TryBuild(
        int targetItemId,
        int maxStepCount,
        Func<int, int> ownedCountResolver,
        Func<int, bool> craftingAccessResolver)
    {
        Reset();
        if (targetItemId < 0
            || maxStepCount <= 0
            || ownedCountResolver == null
            || craftingAccessResolver == null)
        {
            return false;
        }

        maximumStepCount = maxStepCount;
        getOwnedCount = ownedCountResolver;
        canCraftItem = craftingAccessResolver;
        if (!TryAppendCraft(targetItemId, false, 0))
        {
            Reset();
            return false;
        }

        foreach (KeyValuePair<int, int> pair in consumedOwned)
        {
            if (pair.Key >= 0 && pair.Value > 0)
            {
                consumedItems.Add(new CraftingTreeRuntime.IngredientEntry(pair.Key, pair.Value));
            }
        }

        consumedItems.Sort((left, right) => left.itemId.CompareTo(right.itemId));
        return steps.Count > 0 && consumedItems.Count > 0;
    }

    private bool TryAppendCraft(int itemId, bool addOutputToStock, int depth)
    {
        if (depth >= MaximumRecursionDepth
            || steps.Count >= maximumStepCount
            || !canCraftItem(itemId)
            || !activeRecipes.Add(itemId)
            || !CraftingTreeRuntime.TryGetIngredientsView(
                itemId,
                out IReadOnlyList<CraftingTreeRuntime.IngredientEntry> ingredients))
        {
            activeRecipes.Remove(itemId);
            return false;
        }

        bool success = true;
        for (int i = 0; i < ingredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = ingredients[i];
            if (ingredient.itemId < 0
                || ingredient.count <= 0
                || !TryConsumeIngredient(ingredient.itemId, ingredient.count, depth + 1))
            {
                success = false;
                break;
            }
        }

        activeRecipes.Remove(itemId);
        if (!success || steps.Count >= maximumStepCount)
        {
            return false;
        }

        CraftingPlanStep step = new CraftingPlanStep(
            itemId,
            CraftingTreeRuntime.GetOutputCount(itemId));
        int stepIndex = steps.Count;
        steps.Add(step);
        if (addOutputToStock)
        {
            if (!craftedBatches.TryGetValue(itemId, out List<CraftedBatch> batches))
            {
                batches = new List<CraftedBatch>(2);
                craftedBatches.Add(itemId, batches);
            }

            batches.Add(new CraftedBatch(stepIndex, step.OutputCount));
        }

        return true;
    }

    private bool TryConsumeIngredient(int itemId, int requestedCount, int depth)
    {
        int remaining = Mathf.Max(0, requestedCount);
        if (remaining <= 0)
        {
            return true;
        }

        int owned = GetOwnedRemaining(itemId);
        if (owned > 0)
        {
            int consumed = Mathf.Min(owned, remaining);
            ownedRemaining[itemId] = owned - consumed;
            AddCount(consumedOwned, itemId, consumed);
            remaining -= consumed;
        }

        remaining -= ConsumeCraftedBatches(itemId, remaining);
        while (remaining > 0)
        {
            if (!TryAppendCraft(itemId, true, depth))
            {
                return false;
            }

            int consumed = ConsumeCraftedBatches(itemId, remaining);
            if (consumed <= 0)
            {
                return false;
            }

            remaining -= consumed;
        }

        return true;
    }

    private int ConsumeCraftedBatches(int itemId, int requestedCount)
    {
        int remaining = Mathf.Max(0, requestedCount);
        if (remaining <= 0
            || !craftedBatches.TryGetValue(itemId, out List<CraftedBatch> batches))
        {
            return 0;
        }

        int consumedTotal = 0;
        for (int i = 0; i < batches.Count && remaining > 0; i++)
        {
            CraftedBatch batch = batches[i];
            if (batch.RemainingCount <= 0)
            {
                continue;
            }

            int consumed = Mathf.Min(batch.RemainingCount, remaining);
            batch.RemainingCount -= consumed;
            batches[i] = batch;
            CraftingPlanStep step = steps[batch.StepIndex];
            step.ReservedOutputCount += consumed;
            remaining -= consumed;
            consumedTotal += consumed;
        }

        return consumedTotal;
    }

    private int GetOwnedRemaining(int itemId)
    {
        if (!ownedRemaining.TryGetValue(itemId, out int count))
        {
            count = Mathf.Max(0, getOwnedCount(itemId));
            ownedRemaining.Add(itemId, count);
        }

        return count;
    }

    private static void AddCount(Dictionary<int, int> counts, int itemId, int amount)
    {
        if (itemId < 0 || amount <= 0)
        {
            return;
        }

        counts.TryGetValue(itemId, out int current);
        counts[itemId] = current + amount;
    }

    private void Reset()
    {
        steps.Clear();
        consumedItems.Clear();
        ownedRemaining.Clear();
        consumedOwned.Clear();
        craftedBatches.Clear();
        activeRecipes.Clear();
        getOwnedCount = null;
        canCraftItem = null;
        maximumStepCount = 0;
    }

    private struct CraftedBatch
    {
        public readonly int StepIndex;
        public int RemainingCount;

        public CraftedBatch(int stepIndex, int remainingCount)
        {
            StepIndex = stepIndex;
            RemainingCount = Mathf.Max(0, remainingCount);
        }
    }
}
