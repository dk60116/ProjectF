using System;
using System.Collections.Generic;
using UnityEngine;

public class ProductionMachine : InputOutputModule
{
    private const int LegacyMaximumProductionIngredientTypes = 2;

    [SerializeField]
    private List<SpriteRenderer> targetIconDisplays;

    private readonly List<CraftingTreeRuntime.IngredientEntry> productionIngredientBuffer =
        new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<CraftingTreeRuntime.IngredientEntry> resolvedProductionIngredients =
        new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<Vector2Int> resolvedProductionInputCoordinates = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> resolvedProductionInputCoordinateSet = new HashSet<Vector2Int>();
    private readonly HashSet<int> productionIngredientItemIds = new HashSet<int>();
    private readonly List<int> productionTargetItemIds = new List<int>();
    private readonly HashSet<int> productionTargetItemIdSet = new HashSet<int>();
    private readonly Dictionary<int, long> productionFluidUnits = new Dictionary<int, long>();
    private readonly List<Vector2Int> productionFluidInputCoordinates = new List<Vector2Int>(4);
    private readonly List<int> productionFluidSaveItemIds = new List<int>(2);
    private int maximumProductionIngredientTypes = LegacyMaximumProductionIngredientTypes;
    public int MaximumProductionIngredientTypes => ResolveMaximumProductionIngredientTypes();

    protected override void OnEnable()
    {
        base.OnEnable();
        maximumProductionIngredientTypes = MaximumProductionIngredientTypes;
        RefreshProductionTargetIconDisplays();
    }

    public override PersistentState CapturePersistentState()
    {
        PersistentState state = base.CapturePersistentState();
        productionFluidSaveItemIds.Clear();
        foreach (KeyValuePair<int, long> entry in productionFluidUnits)
        {
            if (entry.Value > 0L)
            {
                productionFluidSaveItemIds.Add(entry.Key);
            }
        }

        productionFluidSaveItemIds.Sort();
        for (int i = 0; i < productionFluidSaveItemIds.Count; i++)
        {
            int itemId = productionFluidSaveItemIds[i];
            state.productionInputFluidItemIds.Add(itemId);
            state.productionInputFluidUnits.Add(productionFluidUnits[itemId]);
        }

        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        if (state == null)
        {
            return;
        }

        base.ApplyPersistentState(state);
        productionFluidUnits.Clear();
        if (state.productionInputFluidItemIds == null
            || state.productionInputFluidUnits == null)
        {
            return;
        }

        int count = Math.Min(
            state.productionInputFluidItemIds.Count,
            state.productionInputFluidUnits.Count);
        for (int i = 0; i < count; i++)
        {
            int itemId = state.productionInputFluidItemIds[i];
            long units = Math.Max(0L, state.productionInputFluidUnits[i]);
            if (itemId >= 0 && units > 0L && IsFluidItemId(itemId))
            {
                productionFluidUnits[itemId] = units;
            }
        }
    }

    public override void PrepareForPool()
    {
        productionFluidUnits.Clear();
        productionFluidInputCoordinates.Clear();
        productionFluidSaveItemIds.Clear();
        base.PrepareForPool();
    }

    public override void ApplyManagedUpdateTick()
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime))
        {
            return;
        }

        PullProductionFluidIngredients(deltaTime);
        ApplyPlannedBaseModuleTick(deltaTime);
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        if (base.ShouldKeepRuntimeUpdateTickActive())
        {
            return true;
        }

        if (!TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients, out _, out int outputItemId, out _))
        {
            return false;
        }

        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
            if (IsFluidItemId(ingredient.itemId)
                && GetProductionFluidUnits(ingredient.itemId)
                < GetRequiredProductionFluidUnits(outputItemId, ingredient))
            {
                return true;
            }
        }

        return false;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy()
    {
        // Ingredient intake continues while the energy used for crafting is unavailable.
        return ShouldKeepRuntimeUpdateTickActive();
    }

    protected override void AppendDedicatedFluidStorageRuntimeCoordinates(List<Vector2Int> coordinates)
    {
        if (coordinates == null
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if ((!IsInputItemBlockType(placement.blockType)
                 && placement.blockType != RectGridBlockType.PipeInput)
                || !AllowsPipeAreaInteraction(placement.blockType)
                || !TryGetRectGridPlacementCoordinate(
                    this, anchorCoordinate, quarterTurns, placement, out Vector2Int coordinate)
                || !TryGetRuntimePipeAreaExternalDirection(coordinate, out _)
                || coordinates.Contains(coordinate))
            {
                continue;
            }

            coordinates.Add(coordinate);
        }
    }

    internal override bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int coordinate) =>
        TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
        && TryGetRectGridBlockTypeAtCoordinate(
            this, anchorCoordinate, quarterTurns, coordinate, out RectGridBlockType blockType)
        && (IsInputItemBlockType(blockType) || blockType == RectGridBlockType.PipeInput)
        && AllowsPipeAreaInteraction(blockType)
        && TryGetRuntimePipeAreaExternalDirection(coordinate, out _);

    internal override float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(Vector2Int coordinate)
    {
        if (!UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate)
            || !TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients, out _, out int outputItemId, out _))
        {
            return 0f;
        }

        float lowestFillRatio = 1f;
        bool hasFluidIngredient = false;
        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
            if (!IsFluidItemId(ingredient.itemId))
            {
                continue;
            }

            long requiredUnits = GetRequiredProductionFluidUnits(outputItemId, ingredient);
            lowestFillRatio = Mathf.Min(
                lowestFillRatio,
                (float)GetProductionFluidUnits(ingredient.itemId) / requiredUnits);
            hasFluidIngredient = true;
        }

        return hasFluidIngredient ? Mathf.Clamp01(lowestFillRatio) : 0f;
    }

    internal override float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(
        Vector2Int coordinate, int fluidItemId) =>
        UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate)
        && TryGetProductionFluidIngredientRequiredUnits(fluidItemId, out long requiredUnits)
            ? Mathf.Clamp01((float)GetProductionFluidUnits(fluidItemId) / requiredUnits)
            : 0f;

    internal override float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(Vector2Int coordinate)
    {
        if (!UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate)
            || !TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients, out _, out int outputItemId, out _))
        {
            return 0f;
        }

        long greatestAvailableUnits = 0L;
        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
            if (IsFluidItemId(ingredient.itemId))
            {
                greatestAvailableUnits = Math.Max(
                    greatestAvailableUnits,
                    GetRequiredProductionFluidUnits(outputItemId, ingredient)
                    - GetProductionFluidUnits(ingredient.itemId));
            }
        }

        return DeterministicSimulationUnits.ToFloat(greatestAvailableUnits);
    }

    internal override float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(
        Vector2Int coordinate, int fluidItemId) =>
        UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate)
        && TryGetProductionFluidIngredientRequiredUnits(fluidItemId, out long requiredUnits)
            ? DeterministicSimulationUnits.ToFloat(
                Math.Max(0L, requiredUnits - GetProductionFluidUnits(fluidItemId)))
            : 0f;

    internal override bool CanAcceptDedicatedFluidAtRuntimeCoordinate(
        Vector2Int coordinate, int fluidItemId, float requestedLiters) =>
        UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate)
        && TryGetProductionFluidIngredientRequiredUnits(fluidItemId, out long requiredUnits)
        && requiredUnits - GetProductionFluidUnits(fluidItemId)
           >= Math.Max(1L, DeterministicSimulationUnits.FromFloat(Mathf.Max(0f, requestedLiters)));

    internal override bool TryAddDedicatedFluidAtRuntimeCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters,
        float temperatureCelsius,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (requestedLiters <= 0f
            || !UsesDedicatedFluidStorageAtRuntimeCoordinate(coordinate)
            || !TryGetProductionFluidIngredientRequiredUnits(fluidItemId, out long requiredUnits))
        {
            return false;
        }

        long storedUnits = GetProductionFluidUnits(fluidItemId);
        long acceptedUnits = Math.Min(
            Math.Max(0L, requiredUnits - storedUnits),
            DeterministicSimulationUnits.FromFloat(requestedLiters));
        if (acceptedUnits <= 0L)
        {
            return false;
        }

        productionFluidUnits[fluidItemId] = storedUnits + acceptedUnits;
        acceptedLiters = DeterministicSimulationUnits.ToFloat(acceptedUnits);
        MarkPersistenceStateDirty();
        WakeRuntimeUpdate();
        return true;
    }

    private bool TryGetProductionFluidIngredientRequiredUnits(int fluidItemId, out long requiredUnits)
    {
        requiredUnits = 0L;
        if (!IsFluidItemId(fluidItemId)
            || !TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients, out _, out int outputItemId, out _))
        {
            return false;
        }

        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
            if (ingredient.itemId == fluidItemId)
            {
                requiredUnits = GetRequiredProductionFluidUnits(outputItemId, ingredient);
                return true;
            }
        }

        return false;
    }

    private int ResolveMaximumProductionIngredientTypes()
    {
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        int inputAreaCount = 0;
        for (int i = 0; i < placements.Count; i++)
        {
            if (IsInputItemBlockType(placements[i].blockType)
                || placements[i].blockType == RectGridBlockType.PipeInput)
            {
                inputAreaCount++;
            }
        }

        return inputAreaCount > 0 ? inputAreaCount : LegacyMaximumProductionIngredientTypes;
    }

    public bool TryCollectProductionTargetItemIds(ICollection<int> itemIds)
    {
        return TryCollectProductionTargetItemIds(itemIds, true);
    }

    public bool TryCollectAllProductionTargetItemIds(ICollection<int> itemIds)
    {
        return TryCollectProductionTargetItemIds(itemIds, false);
    }

    public bool CanSelectProductionTarget(int itemId)
    {
        if (itemId < 0 || !HasRequiredCraftingManual(itemId))
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
                return true;
            }
        }

        return false;
    }

    private bool TryCollectProductionTargetItemIds(ICollection<int> itemIds, bool requireCraftingManual)
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

        productionTargetItemIdSet.Clear();
        bool foundAny = false;
        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition outputDefinition = outputs[i].itemDefinition;
            int outputItemId = outputDefinition != null ? outputDefinition.id : -1;
            if (outputItemId < 0
                || (requireCraftingManual && !HasRequiredCraftingManual(outputItemId))
                || !productionTargetItemIdSet.Add(outputItemId))
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
            || !TryResolveObjectInfoProductionIngredients(resolvedProductionIngredients, out _, out int outputItemId, out _)
            || ingredientIndex >= resolvedProductionIngredients.Count)
        {
            return false;
        }

        CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[ingredientIndex];
        itemId = ingredient.itemId;
        requiredCount = Mathf.Max(1, ingredient.count);
        if (IsFluidItemId(itemId))
        {
            float requiredLiters = ResolveFluidIngredientRequiredLiters(
                outputItemId, itemId, ingredient.count);
            long storedUnits = GetProductionFluidUnits(itemId);
            requiredCount = Mathf.CeilToInt(requiredLiters);
            areaCount = storedUnits >= DeterministicSimulationUnits.FromFloat(requiredLiters)
                ? requiredCount
                : Mathf.FloorToInt(DeterministicSimulationUnits.ToFloat(storedUnits));
            areaCapacity = requiredCount;
            return true;
        }

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

    public bool TryGetObjectInfoProductionFluidIngredient(
        int ingredientIndex,
        out int fluidItemId,
        out float storedLiters,
        out float requiredLiters)
    {
        fluidItemId = -1;
        storedLiters = 0f;
        requiredLiters = 0f;
        if (ingredientIndex < 0
            || !TryResolveObjectInfoProductionIngredients(
                resolvedProductionIngredients, out _, out int outputItemId, out _)
            || ingredientIndex >= resolvedProductionIngredients.Count)
        {
            return false;
        }

        CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[ingredientIndex];
        if (!IsFluidItemId(ingredient.itemId))
        {
            return false;
        }

        fluidItemId = ingredient.itemId;
        storedLiters = DeterministicSimulationUnits.ToFloat(GetProductionFluidUnits(fluidItemId));
        requiredLiters = ResolveFluidIngredientRequiredLiters(
            outputItemId, fluidItemId, ingredient.count);
        return requiredLiters > 0f;
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
        productionTargetItemIds.Clear();
        if (!TryCollectProductionTargetItemIds(productionTargetItemIds))
        {
            return -1;
        }

        if (!IsItemFilterMaskInitialized)
        {
            return -1;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(productionTargetItemIds);
        for (int i = 0; i < productionTargetItemIds.Count; i++)
        {
            int targetItemId = productionTargetItemIds[i];
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
        productionTargetItemIds.Clear();
        if (!TryCollectProductionTargetItemIds(productionTargetItemIds))
        {
            return;
        }

        if (!productionTargetItemIds.Contains(itemId))
        {
            ClearProductionTargetSelection();
            return;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(productionTargetItemIds);
        ClearAllProductionTargetFilterBits(filterBitCount);
        SetItemFilterEnabled(itemId, filterBitCount, true);
        RefreshProductionTargetIconDisplays();
        WakeRuntimeUpdate();
    }

    public void ClearProductionTargetSelection()
    {
        productionTargetItemIds.Clear();
        if (!TryCollectProductionTargetItemIds(productionTargetItemIds))
        {
            return;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(productionTargetItemIds);
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
            if (ingredientItemId < 0
                || IsFluidItemId(ingredientItemId)
                || !ContainsRuntimeInputItemArea(coordinate, ingredientItemId))
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
                out int outputPairIndex,
                out int outputItemId,
                out int outputCount))
        {
            return;
        }

        if (!TryResolveProductionIngredientBlocks(outputItemId, resolvedProductionIngredients))
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
            if (IsFluidItemId(ingredient.itemId))
            {
                continue;
            }

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

        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
            if (!IsFluidItemId(ingredient.itemId))
            {
                continue;
            }

            long requiredUnits = GetRequiredProductionFluidUnits(outputItemId, ingredient);
            productionFluidUnits[ingredient.itemId] = GetProductionFluidUnits(ingredient.itemId) - requiredUnits;
            MarkPersistenceStateDirty();
        }

        BeginActiveCraft(outputPairIndex, outputItemId, outputCount, installedDefinition);
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
                out _))
        {
            return "No recipe";
        }

        bool missingInputArea = false;
        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            int ingredientItemId = resolvedProductionIngredients[i].itemId;
            if (IsFluidItemId(ingredientItemId)
                ? GetProductionFluidUnits(ingredientItemId)
                  < GetRequiredProductionFluidUnits(outputItemId, resolvedProductionIngredients[i])
                   && !HasProductionFluidInputPort()
                : !HasRuntimeInputItemArea(ingredientItemId))
            {
                missingInputArea = true;
                break;
            }
        }

        if (!TryResolveProductionIngredientBlocks(outputItemId, resolvedProductionIngredients))
        {
            if (missingInputArea)
            {
                return "No input area";
            }

            for (int i = 0; i < resolvedProductionIngredients.Count; i++)
            {
                CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
                if (IsFluidItemId(ingredient.itemId)
                    && GetProductionFluidUnits(ingredient.itemId)
                    < GetRequiredProductionFluidUnits(outputItemId, ingredient))
                {
                    return "No input fluid";
                }
            }

            return "No input item";
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
        if (Application.isPlaying && TryGetPlacementRuntime(out _, out _))
        {
            WakeRuntimeUpdate();
            NotifyRuntimePipeTopologyChanged(RuntimeGridCoordinates);
        }
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

            // The icon sits just above an animated machine surface. Dynamic occlusion can
            // incorrectly discard this thin renderer at distant camera zoom levels.
            display.allowOcclusionWhenDynamic = false;
            display.sprite = targetIcon;
            display.enabled = targetIcon != null;
        }
    }

    private bool TryResolveSelectedProductionRecipe(
        List<CraftingTreeRuntime.IngredientEntry> ingredients,
        out int outputPairIndex,
        out int outputItemId,
        out int outputCount)
    {
        outputPairIndex = -1;
        outputItemId = -1;
        outputCount = 0;

        if (ingredients == null)
        {
            return false;
        }

        outputItemId = ResolveSelectedProductionTargetItemId();
        outputPairIndex = ResolveProductionTargetPairIndex(outputItemId);
        if (outputItemId < 0
            || outputPairIndex < 0
            || !TryGetProductionIngredients(outputItemId, ingredients)
            || ingredients.Count <= 0
            || ingredients.Count > maximumProductionIngredientTypes)
        {
            return false;
        }

        outputCount = ResolveProductionOutputCount(outputPairIndex, outputItemId);
        return outputCount > 0;
    }

    private bool TryResolveObjectInfoProductionIngredients(
        List<CraftingTreeRuntime.IngredientEntry> ingredients,
        out int outputPairIndex,
        out int outputItemId,
        out int outputCount)
    {
        outputPairIndex = -1;
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
            outputPairIndex = ResolveProductionTargetPairIndex(outputItemId);
            outputCount = ActiveOutputCount > 0
                ? ActiveOutputCount
                : ResolveProductionOutputCount(outputPairIndex, outputItemId);
            return outputCount > 0;
        }

        return TryResolveSelectedProductionRecipe(
            ingredients,
            out outputPairIndex,
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

        if (TryGetConfiguredProductionIngredients(outputItemId, ingredients))
        {
            return true;
        }

        if (CraftingTreeRuntime.TryGetIngredients(outputItemId, ingredients))
        {
            MergeDuplicateProductionIngredients(ingredients);
            return ingredients.Count > 0;
        }

        return false;
    }

    private bool TryGetConfiguredProductionIngredients(
        int outputItemId,
        List<CraftingTreeRuntime.IngredientEntry> ingredients)
    {
        if (ingredients == null)
        {
            return false;
        }

        ingredients.Clear();
        int outputPairIndex = ResolveProductionTargetPairIndex(outputItemId);
        if (!TryGetInputOutputPair(outputPairIndex, out InputOutputPair pair)
            || pair.inputs == null)
        {
            return false;
        }

        for (int i = 0; i < pair.inputs.Count; i++)
        {
            ItemIoEntry inputEntry = pair.inputs[i];
            int inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
            if (inputItemId >= 0)
            {
                ingredients.Add(new CraftingTreeRuntime.IngredientEntry(
                    inputItemId,
                    inputEntry.ResolvedItemCount));
            }
        }

        MergeDuplicateProductionIngredients(ingredients);
        return ingredients.Count > 0;
    }

    private bool TryResolveProductionIngredientBlocks(
        int outputItemId,
        List<CraftingTreeRuntime.IngredientEntry> ingredients)
    {
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
            if (IsFluidItemId(ingredient.itemId))
            {
                if (GetProductionFluidUnits(ingredient.itemId)
                    < GetRequiredProductionFluidUnits(outputItemId, ingredient))
                {
                    return false;
                }

                resolvedProductionInputCoordinates.Add(default);
                continue;
            }

            if (!TryResolveRuntimeInputItemBlock(
                    ingredient.itemId,
                    ingredient.count,
                    excludedCoordinates,
                    out _,
                    out Vector2Int inputCoordinate))
            {
                return false;
            }

            resolvedProductionInputCoordinates.Add(inputCoordinate);
            resolvedProductionInputCoordinateSet.Add(inputCoordinate);
        }

        return resolvedProductionInputCoordinates.Count == ingredients.Count;
    }

    private void PullProductionFluidIngredients(float deltaTime)
    {
        if (deltaTime <= 0f
            || !TryResolveSelectedProductionRecipe(
                resolvedProductionIngredients, out _, out int outputItemId, out _))
        {
            return;
        }

        for (int i = 0; i < resolvedProductionIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = resolvedProductionIngredients[i];
            int fluidItemId = ingredient.itemId;
            if (!IsFluidItemId(fluidItemId))
            {
                continue;
            }

            long requiredUnits = GetRequiredProductionFluidUnits(outputItemId, ingredient);
            long storedUnits = GetProductionFluidUnits(fluidItemId);
            if (storedUnits >= requiredUnits)
            {
                continue;
            }

            CollectProductionFluidInputCoordinates();
            for (int portIndex = 0; portIndex < productionFluidInputCoordinates.Count; portIndex++)
            {
                Vector2Int coordinate = productionFluidInputCoordinates[portIndex];
                if (!TryGetRuntimeFluidInputPressure(
                        coordinate, fluidItemId, out float pressureLitersPerSecond)
                    || pressureLitersPerSecond <= 0f)
                {
                    continue;
                }

                float remainingLiters = DeterministicSimulationUnits.ToFloat(requiredUnits - storedUnits);
                float requestedLiters = Mathf.Min(
                    remainingLiters, pressureLitersPerSecond * deltaTime);
                if (requestedLiters <= 0f)
                {
                    continue;
                }

                // A partial transfer still removes fluid from the source.
                TryConsumeConnectedFluidInputAtCoordinate(
                    coordinate, fluidItemId, requestedLiters, out float consumedLiters, out _);
                if (consumedLiters <= 0f)
                {
                    continue;
                }

                storedUnits += DeterministicSimulationUnits.FromFloat(consumedLiters);
                productionFluidUnits[fluidItemId] = storedUnits;
                MarkPersistenceStateDirty();
                if (storedUnits >= requiredUnits)
                {
                    break;
                }
            }
        }
    }

    private long GetProductionFluidUnits(int fluidItemId) =>
        productionFluidUnits.TryGetValue(fluidItemId, out long units)
            ? Math.Max(0L, units)
            : 0L;

    private long GetRequiredProductionFluidUnits(
        int outputItemId,
        CraftingTreeRuntime.IngredientEntry ingredient) =>
        Math.Max(1L, DeterministicSimulationUnits.FromFloat(
            ResolveFluidIngredientRequiredLiters(
                outputItemId, ingredient.itemId, ingredient.count)));

    private float ResolveFluidIngredientRequiredLiters(
        int outputItemId,
        int fluidItemId,
        int fallbackCount)
    {
        int pairIndex = ResolveProductionTargetPairIndex(outputItemId);
        if (!TryGetInputOutputPair(pairIndex, out InputOutputPair pair)
            || pair.inputs == null)
        {
            return Mathf.Max(1, fallbackCount);
        }

        float requiredLiters = 0f;
        for (int i = 0; i < pair.inputs.Count; i++)
        {
            ItemIoEntry entry = pair.inputs[i];
            if (entry.itemDefinition != null && entry.itemDefinition.id == fluidItemId)
            {
                requiredLiters += entry.ResolvedAmount;
            }
        }

        return requiredLiters > 0f ? requiredLiters : Mathf.Max(1, fallbackCount);
    }

    private bool HasProductionFluidInputPort()
    {
        CollectProductionFluidInputCoordinates();
        return productionFluidInputCoordinates.Count > 0;
    }

    private void CollectProductionFluidInputCoordinates()
    {
        productionFluidInputCoordinates.Clear();
        AppendDedicatedFluidStorageRuntimeCoordinates(productionFluidInputCoordinates);
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

    private int ResolveProductionTargetPairIndex(int outputItemId)
    {
        if (outputItemId < 0)
        {
            return -1;
        }

        IReadOnlyList<InputOutputPair> pairs = InputOutputPairs;
        if (pairs == null)
        {
            return -1;
        }

        for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            InputOutputPair pair = pairs[pairIndex];
            if (pair?.outputs == null)
            {
                continue;
            }

            for (int outputIndex = 0; outputIndex < pair.outputs.Count; outputIndex++)
            {
                ItemDefinition outputDefinition = pair.outputs[outputIndex].itemDefinition;
                if (outputDefinition != null && outputDefinition.id == outputItemId)
                {
                    return pairIndex;
                }
            }
        }

        return -1;
    }

    private int ResolveProductionOutputCount(int outputPairIndex, int outputItemId)
    {
        if (TryGetInputOutputPair(outputPairIndex, out InputOutputPair pair)
            && pair.outputs != null)
        {
            for (int i = 0; i < pair.outputs.Count; i++)
            {
                ItemIoEntry output = pair.outputs[i];
                if (output.itemDefinition != null && output.itemDefinition.id == outputItemId)
                {
                    return output.ResolvedItemCount;
                }
            }
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
