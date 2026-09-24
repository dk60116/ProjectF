using System;
using System.Collections.Generic;
using System.IO;

public static class VerifyCraftingPlan
{
    public static string Run()
    {
        CraftingTreeRuntime.ForceReload();
        VerifyCraftingIcon();
        VerifyCombinedRecipes(
            ResolveItemId("Workbench"),
            ResolveItemId("Anvil"));
        CraftingPlanBuilder builder = new CraftingPlanBuilder();
        int verifiedTargetId = -1;
        int verifiedStepCount = 0;

        for (int itemId = 0; itemId < 4096; itemId++)
        {
            if (!builder.TryBuild(itemId, 5, ResolveLeafStock, AllowCrafting)
                || builder.Steps.Count < 2)
            {
                continue;
            }

            CraftingPlanStep finalStep = builder.Steps[builder.Steps.Count - 1];
            if (finalStep.ItemId != itemId || finalStep.ReservedOutputCount != 0)
            {
                throw new InvalidOperationException("Final crafting step is invalid.");
            }

            for (int stepIndex = 0; stepIndex < builder.Steps.Count - 1; stepIndex++)
            {
                CraftingPlanStep step = builder.Steps[stepIndex];
                if (step.ReservedOutputCount <= 0
                    || step.ReservedOutputCount > step.OutputCount)
                {
                    throw new InvalidOperationException("Intermediate output reservation is invalid.");
                }
            }

            if (builder.ConsumedItems.Count == 0)
            {
                throw new InvalidOperationException("Recursive plan did not consume leaf materials.");
            }

            verifiedTargetId = itemId;
            verifiedStepCount = builder.Steps.Count;
            break;
        }

        if (verifiedTargetId < 0)
        {
            throw new InvalidOperationException("No recursive crafting plan fitting five queue slots was found.");
        }

        if (!builder.TryBuild(verifiedTargetId, 5, ResolveAllStock, AllowCrafting)
            || builder.Steps.Count != 1)
        {
            throw new InvalidOperationException("Direct materials should produce a single-step plan.");
        }

        if (builder.TryBuild(verifiedTargetId, 5, ResolveNoStock, AllowCrafting))
        {
            throw new InvalidOperationException("A plan was created without leaf materials.");
        }

        VerifySaveRoundTrip(verifiedTargetId);

        return $"Recursive crafting plan validation passed. Target={verifiedTargetId}, Steps={verifiedStepCount}";
    }

    private static void VerifyCraftingIcon()
    {
        CraftingSlot[] slots = UnityEngine.Object.FindObjectsByType<CraftingSlot>(
            UnityEngine.FindObjectsInactive.Include);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].CreateActionIcon != null)
            {
                return;
            }
        }

        throw new InvalidOperationException("CraftingSlot create icon was not found.");
    }

    private static void VerifyCombinedRecipes(int firstWorkableItemId, int secondWorkableItemId)
    {
        List<int> firstRecipes = new List<int>();
        List<int> secondRecipes = new List<int>();
        if (!CraftingTreeRuntime.TryGetCraftableItemIdsForMapObject(
                firstWorkableItemId,
                firstRecipes)
            || !CraftingTreeRuntime.TryGetCraftableItemIdsForMapObject(
                secondWorkableItemId,
                secondRecipes))
        {
            throw new InvalidOperationException("Workable recipes could not be loaded.");
        }

        HashSet<int> combinedRecipes = new HashSet<int>(firstRecipes);
        combinedRecipes.UnionWith(secondRecipes);
        if (combinedRecipes.Count <= Math.Max(firstRecipes.Count, secondRecipes.Count))
        {
            throw new InvalidOperationException("Combined Workable recipes did not expand the recipe set.");
        }
    }

    private static int ResolveItemId(string itemName)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : UnityEngine.Object.FindAnyObjectByType<ItemManager>();
        IReadOnlyList<ItemDefinition> definitions = itemManager != null
            ? itemManager.ItemDefinitions
            : null;
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition != null
                    && string.Equals(definition.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                {
                    return definition.id;
                }
            }
        }

        throw new InvalidOperationException($"ItemDefinition '{itemName}' was not found.");
    }

    private static void VerifySaveRoundTrip(int itemId)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"projectf-crafting-plan-{Guid.NewGuid():N}.save");
        try
        {
            SaveGameData source = new SaveGameData();
            source.player.craftingQueue.Add(new PlayerCraftingQueueEntrySaveData
            {
                itemId = itemId,
                outputCount = 3,
                remainingOutputCount = 2,
                remainingTime = 1.25f,
                duration = 4f,
                planId = 19,
                isPlanFinal = false,
                reservedOutputCount = 1,
                planLedgerTransformed = true,
                refundIngredients = new List<PlayerCraftingIngredientSaveData>
                {
                    new PlayerCraftingIngredientSaveData { itemId = itemId + 1, count = 7 }
                }
            });

            SaveGameBinarySerializer.WriteToFile(path, source);
            SaveGameData loaded = SaveGameBinarySerializer.ReadFromFile(path);
            PlayerCraftingQueueEntrySaveData entry = loaded.player.craftingQueue[0];
            if (entry.planId != 19
                || entry.isPlanFinal
                || entry.reservedOutputCount != 1
                || !entry.planLedgerTransformed
                || entry.refundIngredients.Count != 1
                || entry.refundIngredients[0].count != 7)
            {
                throw new InvalidOperationException("Crafting plan save round trip failed.");
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static int ResolveLeafStock(int itemId)
    {
        return CraftingTreeRuntime.TryGetIngredientsView(
            itemId,
            out IReadOnlyList<CraftingTreeRuntime.IngredientEntry> _)
            ? 0
            : 10000;
    }

    private static int ResolveAllStock(int itemId)
    {
        return 10000;
    }

    private static int ResolveNoStock(int itemId)
    {
        return 0;
    }

    private static bool AllowCrafting(int itemId)
    {
        return true;
    }
}
