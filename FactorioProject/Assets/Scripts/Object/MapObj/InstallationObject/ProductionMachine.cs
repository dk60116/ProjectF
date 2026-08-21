using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProductionMachine : InputOutputModule
{
    private const int MaximumProductionIngredientTypes = 2;

    [SerializeField]
    private List<SpriteRenderer> targetIconDisplays;

    private readonly List<CraftingTreeRuntime.IngredientEntry> productionIngredientBuffer =
        new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<CraftingTreeRuntime.IngredientEntry> resolvedProductionIngredients =
        new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<Block> resolvedProductionInputBlocks = new List<Block>();
    private readonly List<Vector2Int> resolvedProductionInputCoordinates = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> resolvedProductionInputCoordinateSet = new HashSet<Vector2Int>();
    private readonly HashSet<int> productionIngredientItemIds = new HashSet<int>();

    protected override void OnEnable()
    {
        base.OnEnable();
        RefreshProductionTargetIconDisplays();
    }

    public bool TryCollectProductionTargetItemIds(ICollection<int> itemIds)
    {
        if (itemIds == null)
        {
            return false;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (outputs == null || outputs.Count <= 0)
        {
            return false;
        }

        HashSet<int> seenItemIds = new HashSet<int>();
        bool foundAny = false;
        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition outputDefinition = outputs[i].itemDefinition;
            int outputItemId = outputDefinition != null ? outputDefinition.id : -1;
            if (outputItemId < 0
                || !HasRequiredCraftingManual(outputItemId)
                || !seenItemIds.Add(outputItemId))
            {
                continue;
            }

            itemIds.Add(outputItemId);
            foundAny = true;
        }

        return foundAny;
    }

    public bool TryCollectProductionIngredientItemIds(ICollection<int> itemIds)
    {
        if (itemIds == null)
        {
            return false;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (outputs == null || outputs.Count <= 0)
        {
            return false;
        }

        productionIngredientItemIds.Clear();
        bool foundAny = false;
        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition outputDefinition = outputs[i].itemDefinition;
            int outputItemId = outputDefinition != null ? outputDefinition.id : -1;
            if (outputItemId < 0
                || !HasRequiredCraftingManual(outputItemId)
                || !TryGetProductionIngredients(outputItemId, productionIngredientBuffer))
            {
                continue;
            }

            for (int ingredientIndex = 0; ingredientIndex < productionIngredientBuffer.Count; ingredientIndex++)
            {
                int ingredientItemId = productionIngredientBuffer[ingredientIndex].itemId;
                if (ingredientItemId < 0 || !productionIngredientItemIds.Add(ingredientItemId))
                {
                    continue;
                }

                itemIds.Add(ingredientItemId);
                foundAny = true;
            }
        }

        return foundAny;
    }

    public bool TryGetObjectInfoProductionIngredientCount(out int ingredientCount)
    {
        ingredientCount = 0;
        if (!TryResolveObjectInfoProductionIngredients(resolvedProductionIngredients, out _, out _, out _))
        {
            return false;
        }

        ingredientCount = resolvedProductionIngredients.Count;
        return ingredientCount > 0;
    }

    public bool TryGetObjectInfoProductionIngredient(
        int ingredientIndex,
        out int itemId,
        out int requiredCount,
        out int areaCount,
        out int areaCapacity)
    {
        itemId = -1;
        requiredCount = 0;
        areaCount = 0;
        areaCapacity = 0;

        if (ingredientIndex < 0
            || !TryResolveObjectInfoProductionIngredients(resolvedProductionIngredients, out _, out _, out _)
            || ingredientIndex >= resolvedProductionIngredients.Count)
        {
            return false;
        }

        CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[ingredientIndex];
        itemId = ingredient.itemId;
        requiredCount = Mathf.Max(1, ingredient.count);
        if (!TryResolveObjectInfoInputAreaCounts(
                itemId,
                requiredCount,
                out areaCount,
                out areaCapacity))
        {
            areaCount = 0;
            areaCapacity = requiredCount;
        }

        return itemId >= 0;
    }

    public bool TryGetObjectInfoProductionOutput(
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity)
    {
        outputItemId = -1;
        outputAreaCount = 0;
        outputAreaCapacity = 0;

        if (!TryResolveObjectInfoProductionIngredients(
                resolvedProductionIngredients,
                out _,
                out outputItemId,
                out int outputCount))
        {
            return false;
        }

        if (!TryResolveObjectInfoOutputAreaCounts(
                outputItemId,
                outputCount,
                out outputAreaCount,
                out outputAreaCapacity))
        {
            outputAreaCount = 0;
            outputAreaCapacity = Mathf.Max(1, outputCount);
        }

        return outputItemId >= 0;
    }

    public int ResolveSelectedProductionTargetItemId()
    {
        List<int> targetItemIds = new List<int>();
        if (!TryCollectProductionTargetItemIds(targetItemIds))
        {
            return -1;
        }

        if (!IsItemFilterMaskInitialized)
        {
            return -1;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(targetItemIds);
        for (int i = 0; i < targetItemIds.Count; i++)
        {
            int targetItemId = targetItemIds[i];
            if (IsItemFilterEnabled(targetItemId, filterBitCount))
            {
                return targetItemId;
            }
        }

        return -1;
    }

    public bool IsProductionTargetSelected(int itemId)
    {
        return itemId >= 0 && ResolveSelectedProductionTargetItemId() == itemId;
    }

    public void SetExclusiveProductionTarget(int itemId)
    {
        List<int> targetItemIds = new List<int>();
        if (!TryCollectProductionTargetItemIds(targetItemIds))
        {
            return;
        }

        if (!targetItemIds.Contains(itemId))
        {
            ClearProductionTargetSelection();
            return;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(targetItemIds);
        ClearAllProductionTargetFilterBits(filterBitCount);
        SetItemFilterEnabled(itemId, filterBitCount, true);
        RefreshProductionTargetIconDisplays();
        WakeRuntimeUpdate();
    }

    public void ClearProductionTargetSelection()
    {
        List<int> targetItemIds = new List<int>();
        if (!TryCollectProductionTargetItemIds(targetItemIds))
        {
            return;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(targetItemIds);
        ClearAllProductionTargetFilterBits(filterBitCount);
        RefreshProductionTargetIconDisplays();
        WakeRuntimeUpdate();
    }

    protected override bool IsRecipeOutputAllowedByItemFilter(int outputItemId)
    {
        return IsProductionTargetSelected(outputItemId);
    }

    protected override bool ShouldShowObjectInfoEmptyRecipeLine(int outputItemId)
    {
        return IsProductionTargetSelected(outputItemId);
    }

    protected override bool ShouldShowObjectInfoEmptyInputOutputSlots()
    {
        return ResolveSelectedProductionTargetItemId() >= 0;
    }

    protected override bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> itemIds)
    {
        return TryCollectProductionIngredientItemIds(itemIds);
    }

    protected override bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> inputItemIds)
    {
        if (inputItemIds == null
            || !TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients,
                out _,
                out _,
                out _))
        {
            return false;
        }

        bool foundAny = false;
        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            int ingredientItemId = resolvedProductionIngredients[i].itemId;
            if (ingredientItemId < 0 || !ContainsRuntimeInputItemArea(coordinate, ingredientItemId))
            {
                continue;
            }

            inputItemIds.Add(ingredientItemId);
            foundAny = true;
        }

        return foundAny;
    }

    protected override void TryStartNextCraft()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null || !HasRuntimeOutputCoordinates)
        {
            return;
        }

        if (!TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients,
                out int outputIndex,
                out int outputItemId,
                out int outputCount))
        {
            return;
        }

        if (!TryResolveProductionIngredientBlocks(resolvedProductionIngredients))
        {
            return;
        }

        if (!CanResolveOutputTarget(outputItemId, outputCount))
        {
            return;
        }

        if (!TryEnsureCraftStartEnergy(installedDefinition))
        {
            return;
        }

        Vector3 consumeTargetWorldPosition = ResolveConsumeTargetWorldPosition();
        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
            if (ConsumeRuntimeInputAreaCenterObjects(
                    resolvedProductionInputCoordinates[i],
                    ingredient.itemId,
                    ingredient.count,
                    consumeTargetWorldPosition,
                    InputConsumeMoveInterval) != ingredient.count)
            {
                return;
            }
        }

        BeginActiveCraft(outputIndex, outputItemId, outputCount, installedDefinition);
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null)
        {
            return "No machine";
        }

        if (IsWaitingForOutput)
        {
            return "Output full";
        }

        if (IsActiveCraftRunning)
        {
            if (!HasOperationalEnergyAvailable(installedDefinition))
            {
                return "No energy";
            }

            isProducing = true;
            return "Working";
        }

        if (!HasRuntimeOutputCoordinates)
        {
            return "No output area";
        }

        if (ResolveSelectedProductionTargetItemId() < 0)
        {
            return "No target";
        }

        if (!TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients,
                out _,
                out int outputItemId,
                out int outputCount))
        {
            return "No recipe";
        }

        bool missingInputArea = false;
        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            if (!HasRuntimeInputItemArea(resolvedProductionIngredients[i].itemId))
            {
                missingInputArea = true;
                break;
            }
        }

        if (!TryResolveProductionIngredientBlocks(resolvedProductionIngredients))
        {
            return missingInputArea ? "No input area" : "No input item";
        }

        if (!CanResolveOutputTarget(outputItemId, outputCount))
        {
            return "Output full";
        }

        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            return "No energy";
        }

        isProducing = true;
        return "Working";
    }

    protected override void OnItemFilterMaskChanged()
    {
        base.OnItemFilterMaskChanged();
        RefreshProductionTargetIconDisplays();
    }

    private void RefreshProductionTargetIconDisplays()
    {
        if (targetIconDisplays == null || targetIconDisplays.Count <= 0)
        {
            return;
        }

        Sprite targetIcon = null;
        int selectedTargetItemId = ResolveSelectedProductionTargetItemId();
        if (selectedTargetItemId >= 0
            && TryResolveProductionTargetDefinition(selectedTargetItemId, out ItemDefinition targetDefinition)
            && targetDefinition != null)
        {
            targetIcon = targetDefinition.icon;
        }

        for (int i = 0; i < targetIconDisplays.Count; i++)
        {
            SpriteRenderer display = targetIconDisplays[i];
            if (display == null)
            {
                continue;
            }

            display.sprite = targetIcon;
            display.enabled = targetIcon != null;
        }
    }

    private bool TryResolveSelectedProductionRecipe(
        List<CraftingTreeRuntime.IngredientEntry> ingredients,
        out int outputIndex,
        out int outputItemId,
        out int outputCount)
    {
        outputIndex = -1;
        outputItemId = -1;
        outputCount = 0;

        if (ingredients == null)
        {
            return false;
        }

        outputItemId = ResolveSelectedProductionTargetItemId();
        outputIndex = ResolveProductionTargetOutputIndex(outputItemId);
        if (outputItemId < 0
            || outputIndex < 0
            || !TryGetProductionIngredients(outputItemId, ingredients)
            || ingredients.Count <= 0
            || ingredients.Count > MaximumProductionIngredientTypes)
        {
            return false;
        }

        outputCount = ResolveProductionOutputCount(outputIndex, outputItemId);
        return outputCount > 0;
    }

    private bool TryResolveObjectInfoProductionIngredients(
        List<CraftingTreeRuntime.IngredientEntry> ingredients,
        out int outputIndex,
        out int outputItemId,
        out int outputCount)
    {
        outputIndex = -1;
        outputItemId = -1;
        outputCount = 0;

        if (ingredients == null)
        {
            return false;
        }

        if ((IsActiveCraftRunning || IsWaitingForOutput)
            && ActiveOutputItemId >= 0
            && TryGetProductionIngredients(ActiveOutputItemId, ingredients)
            && ingredients.Count > 0)
        {
            outputItemId = ActiveOutputItemId;
            outputIndex = ResolveProductionTargetOutputIndex(outputItemId);
            outputCount = ActiveOutputCount > 0
                ? ActiveOutputCount
                : ResolveProductionOutputCount(outputIndex, outputItemId);
            return outputCount > 0;
        }

        return TryResolveSelectedProductionRecipe(
            ingredients,
            out outputIndex,
            out outputItemId,
            out outputCount);
    }

    private bool TryGetProductionIngredients(
        int outputItemId,
        List<CraftingTreeRuntime.IngredientEntry> ingredients)
    {
        if (ingredients == null)
        {
            return false;
        }

        ingredients.Clear();
        if (outputItemId < 0)
        {
            return false;
        }

        if (CraftingTreeRuntime.TryGetIngredients(outputItemId, ingredients))
        {
            MergeDuplicateProductionIngredients(ingredients);
            return ingredients.Count > 0;
        }

        return TryGetLegacyProductionIngredients(outputItemId, ingredients);
    }

    private bool TryGetLegacyProductionIngredients(
        int outputItemId,
        List<CraftingTreeRuntime.IngredientEntry> ingredients)
    {
        if (ingredients == null)
        {
            return false;
        }

        ingredients.Clear();
        int outputIndex = ResolveProductionTargetOutputIndex(outputItemId);
        IReadOnlyList<ItemIoEntry> inputs = InputList;
        if (outputIndex < 0 || inputs == null || outputIndex >= inputs.Count)
        {
            return false;
        }

        ItemIoEntry inputEntry = inputs[outputIndex];
        int inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
        if (inputItemId < 0)
        {
            return false;
        }

        ingredients.Add(new CraftingTreeRuntime.IngredientEntry(inputItemId, Mathf.Max(1, inputEntry.count)));
        return true;
    }

    private bool TryResolveProductionIngredientBlocks(List<CraftingTreeRuntime.IngredientEntry> ingredients)
    {
        resolvedProductionInputBlocks.Clear();
        resolvedProductionInputCoordinates.Clear();
        resolvedProductionInputCoordinateSet.Clear();
        if (ingredients == null || ingredients.Count <= 0)
        {
            return false;
        }

        ISet<Vector2Int> excludedCoordinates = ingredients.Count > 1
            ? resolvedProductionInputCoordinateSet
            : null;
        for (int i = 0; i < ingredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = ingredients[i];
            if (!TryResolveRuntimeInputItemBlock(
                    ingredient.itemId,
                    ingredient.count,
                    excludedCoordinates,
                    out Block inputBlock,
                    out Vector2Int inputCoordinate))
            {
                return false;
            }

            resolvedProductionInputBlocks.Add(inputBlock);
            resolvedProductionInputCoordinates.Add(inputCoordinate);
            resolvedProductionInputCoordinateSet.Add(inputCoordinate);
        }

        return resolvedProductionInputBlocks.Count == ingredients.Count;
    }

    private static void MergeDuplicateProductionIngredients(List<CraftingTreeRuntime.IngredientEntry> ingredients)
    {
        if (ingredients == null || ingredients.Count <= 1)
        {
            return;
        }

        for (int i = 0; i < ingredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = ingredients[i];
            if (ingredient.itemId < 0)
            {
                ingredients.RemoveAt(i);
                i--;
                continue;
            }

            int mergedCount = Mathf.Max(1, ingredient.count);
            for (int j = i + 1; j < ingredients.Count; j++)
            {
                CraftingTreeRuntime.IngredientEntry candidate = ingredients[j];
                if (candidate.itemId != ingredient.itemId)
                {
                    continue;
                }

                mergedCount += Mathf.Max(1, candidate.count);
                ingredients.RemoveAt(j);
                j--;
            }

            ingredients[i] = new CraftingTreeRuntime.IngredientEntry(ingredient.itemId, mergedCount);
        }
    }

    private int ResolveProductionTargetOutputIndex(int outputItemId)
    {
        if (outputItemId < 0)
        {
            return -1;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (outputs == null)
        {
            return -1;
        }

        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition outputDefinition = outputs[i].itemDefinition;
            if (outputDefinition != null && outputDefinition.id == outputItemId)
            {
                return i;
            }
        }

        return -1;
    }

    private int ResolveProductionOutputCount(int outputIndex, int outputItemId)
    {
        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (outputs != null && outputIndex >= 0 && outputIndex < outputs.Count)
        {
            return Mathf.Max(1, outputs[outputIndex].count);
        }

        return CraftingTreeRuntime.GetOutputCount(outputItemId);
    }

    private bool TryResolveProductionTargetDefinition(int itemId, out ItemDefinition definition)
    {
        definition = null;
        if (itemId < 0)
        {
            return false;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (outputs == null)
        {
            return false;
        }

        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition outputDefinition = outputs[i].itemDefinition;
            if (outputDefinition != null && outputDefinition.id == itemId)
            {
                definition = outputDefinition;
                return true;
            }
        }

        return false;
    }

    private void ClearAllProductionTargetFilterBits(int filterBitCount)
    {
        for (int itemId = 0; itemId < filterBitCount; itemId++)
        {
            SetItemFilterEnabled(itemId, filterBitCount, false);
        }
    }

    private static int ResolveProductionTargetFilterBitCount(List<int> targetItemIds)
    {
        int maxItemId = -1;
        if (GameManager.Instance != null && GameManager.Instance.ItemManger != null)
        {
            List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    ItemDefinition definition = definitions[i];
                    if (definition != null && definition.id > maxItemId)
                    {
                        maxItemId = definition.id;
                    }
                }
            }
        }

        if (targetItemIds != null)
        {
            for (int i = 0; i < targetItemIds.Count; i++)
            {
                if (targetItemIds[i] > maxItemId)
                {
                    maxItemId = targetItemIds[i];
                }
            }
        }

        return Mathf.Max(0, maxItemId + 1);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        RefreshProductionTargetIconDisplays();
    }
#endif
}
