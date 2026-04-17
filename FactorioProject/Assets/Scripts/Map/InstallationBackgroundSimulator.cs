using System;
using System.Collections.Generic;
using UnityEngine;

public class InstallationBackgroundSimulator : MonoBehaviour
{
    private const int InputAreaCenterStackStateSentinel = -1000000001;

    [SerializeField, Min(1)]
    private int maxCraftIterationsPerSimulation = 256;

    private BlockStateStore cachedStateStore;

    private sealed class SavedBlockInventory
    {
        public readonly List<int> floorItems = new List<int>();
        public readonly List<int> centerItems = new List<int>();

        public static SavedBlockInventory FromSerialized(IReadOnlyList<int> itemIds)
        {
            SavedBlockInventory inventory = new SavedBlockInventory();
            if (itemIds == null)
            {
                return inventory;
            }

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                if (itemId == InputAreaCenterStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        break;
                    }

                    int centerCount = Mathf.Max(0, itemIds[++i]);
                    for (int centerIndex = 0; centerIndex < centerCount && i + 1 < itemIds.Count; centerIndex++)
                    {
                        inventory.centerItems.Add(itemIds[++i]);
                    }

                    continue;
                }

                inventory.floorItems.Add(itemId);
            }

            return inventory;
        }

        public List<int> ToSerialized()
        {
            List<int> itemIds = new List<int>(floorItems.Count + centerItems.Count + 2);
            itemIds.AddRange(floorItems);

            if (centerItems.Count > 0)
            {
                itemIds.Add(InputAreaCenterStackStateSentinel);
                itemIds.Add(centerItems.Count);
                itemIds.AddRange(centerItems);
            }

            return itemIds;
        }
    }

    public void SimulateSavedInstallation(Vector2Int anchorCoordinate)
    {
        if (!Application.isPlaying || !TryGetStateStore(out BlockStateStore stateStore))
        {
            return;
        }

        if (stateStore.TryGetLiveInstallation(anchorCoordinate, out _, out _))
        {
            return;
        }

        if (!stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState)
            || installationState?.inputOutputState == null)
        {
            return;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        if (installationState.lastBackgroundSimulationTicks <= 0)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        double elapsedSeconds = TimeSpan.FromTicks(Math.Max(0L, nowTicks - installationState.lastBackgroundSimulationTicks)).TotalSeconds;
        if (elapsedSeconds <= 0.0001d)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        if (!TryResolveTemplateModule(installationState.itemId, out ItemDefinition installedDefinition, out InputOutputModule templateModule))
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        double remainingElapsed = elapsedSeconds;
        double simulatedSeconds = 0d;
        bool blockedOrIdle = false;
        int iterationCount = 0;

        while (remainingElapsed > 0.0001d && iterationCount < Mathf.Max(1, maxCraftIterationsPerSimulation))
        {
            iterationCount++;

            if (installationState.inputOutputState.hasActiveCraft)
            {
                if (!AdvanceActiveCraft(
                        stateStore,
                        installationState.inputOutputState,
                        templateModule,
                        installedDefinition,
                        ref remainingElapsed,
                        ref simulatedSeconds,
                        out bool blocked))
                {
                    blockedOrIdle = blocked;
                    break;
                }

                continue;
            }

            if (!TryStartNextCraft(stateStore, installationState.inputOutputState, templateModule, installedDefinition))
            {
                blockedOrIdle = true;
                break;
            }
        }

        bool hitIterationLimit = iterationCount >= Mathf.Max(1, maxCraftIterationsPerSimulation) && remainingElapsed > 0.0001d && !blockedOrIdle;
        if (hitIterationLimit)
        {
            installationState.lastBackgroundSimulationTicks += TimeSpan.FromSeconds(simulatedSeconds).Ticks;
        }
        else
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
        }

        stateStore.UpdateInstallationState(installationState);
    }

    private bool AdvanceActiveCraft(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition,
        ref double remainingElapsed,
        ref double simulatedSeconds,
        out bool blocked)
    {
        blocked = false;
        if (state == null)
        {
            blocked = true;
            return false;
        }

        if (state.waitingForOutput)
        {
            if (!TryCompleteActiveCraft(stateStore, state, templateModule))
            {
                blocked = true;
                return false;
            }

            return true;
        }

        float remainingCraftTime = Mathf.Max(0f, state.remainingCraftTime);
        if (remainingCraftTime <= 0.0001f)
        {
            state.waitingForOutput = true;
            if (!TryCompleteActiveCraft(stateStore, state, templateModule))
            {
                blocked = true;
                return false;
            }

            return true;
        }

        if (!RequiresOperationalEnergy(installedDefinition))
        {
            double delta = Math.Min(remainingElapsed, remainingCraftTime);
            if (delta <= 0.0001d)
            {
                blocked = true;
                return false;
            }

            state.remainingCraftTime = Mathf.Max(0f, remainingCraftTime - (float)delta);
            remainingElapsed -= delta;
            simulatedSeconds += delta;
        }
        else
        {
            float energyRate = Mathf.Max(0f, installedDefinition.useEnergyAmount);
            while (remainingElapsed > 0.0001d && state.remainingCraftTime > 0.0001f)
            {
                if (state.storedEnergy <= 0.0001f && !TryRefillEnergyStore(stateStore, state, templateModule, installedDefinition))
                {
                    blocked = true;
                    return simulatedSeconds > 0d;
                }

                float maxTimeByEnergy = energyRate <= 0.0001f
                    ? float.PositiveInfinity
                    : Mathf.Max(0f, state.storedEnergy / energyRate);
                double delta = Math.Min(remainingElapsed, Math.Min(state.remainingCraftTime, maxTimeByEnergy));
                if (delta <= 0.0001d)
                {
                    blocked = true;
                    return simulatedSeconds > 0d;
                }

                state.storedEnergy = Mathf.Max(0f, state.storedEnergy - (energyRate * (float)delta));
                if (state.storedEnergy <= 0.0001f)
                {
                    state.energyGaugeCapacity = 0f;
                }

                state.remainingCraftTime = Mathf.Max(0f, state.remainingCraftTime - (float)delta);
                remainingElapsed -= delta;
                simulatedSeconds += delta;
            }
        }

        if (state.remainingCraftTime > 0.0001f)
        {
            return true;
        }

        state.waitingForOutput = true;
        if (!TryCompleteActiveCraft(stateStore, state, templateModule))
        {
            blocked = true;
            return false;
        }

        return true;
    }

    private bool TryStartNextCraft(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition)
    {
        if (state == null || installedDefinition == null || templateModule == null)
        {
            return false;
        }

        int recipeCount = Mathf.Min(templateModule.InputList.Count, templateModule.OutputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(templateModule, recipeIndex, out int inputItemId, out int inputCount, out int outputItemId, out int outputCount))
            {
                continue;
            }

            if (!TryResolveRuntimeInputItemArea(state, recipeIndex, inputItemId, out Vector2Int inputCoordinate))
            {
                continue;
            }

            if (GetCenterItemCount(stateStore, inputCoordinate, inputItemId) < inputCount)
            {
                continue;
            }

            if (!TryResolveOutputCoordinate(stateStore, state, templateModule, outputItemId, outputCount, out _))
            {
                continue;
            }

            if (!TryEnsureCraftStartEnergy(stateStore, state, templateModule, installedDefinition))
            {
                continue;
            }

            if (RemoveCenterItems(stateStore, inputCoordinate, inputItemId, inputCount) != inputCount)
            {
                continue;
            }

            state.hasActiveCraft = true;
            state.waitingForOutput = false;
            state.remainingCraftTime = templateModule.CraftDurationSeconds;
            state.activeRecipeIndex = recipeIndex;
            state.activeOutputItemId = outputItemId;
            state.activeOutputCount = outputCount;
            return true;
        }

        return false;
    }

    private bool TryCompleteActiveCraft(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule)
    {
        if (state == null || !state.hasActiveCraft || state.activeOutputItemId < 0 || state.activeOutputCount <= 0)
        {
            ClearActiveCraft(state);
            return false;
        }

        if (!TryResolveOutputCoordinate(
                stateStore,
                state,
                templateModule,
                state.activeOutputItemId,
                state.activeOutputCount,
                out Vector2Int outputCoordinate))
        {
            return false;
        }

        if (!AddCenterItems(
                stateStore,
                outputCoordinate,
                state.activeOutputItemId,
                state.activeOutputCount,
                ResolveBlockCenterCapacity(stateStore, outputCoordinate, templateModule.RuntimeAreaMaxObjects)))
        {
            return false;
        }

        ClearActiveCraft(state);
        return true;
    }

    private bool TryEnsureCraftStartEnergy(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        if (state.storedEnergy > 0.0001f)
        {
            return true;
        }

        return TryRefillEnergyStore(stateStore, state, templateModule, installedDefinition);
    }

    private bool TryRefillEnergyStore(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        float minimumOperationalEnergy = Mathf.Max(1f, installedDefinition.useEnergyAmount);
        bool consumedAnyEnergyItem = false;

        while (state.storedEnergy < minimumOperationalEnergy)
        {
            if (!TryConsumeOneEnergyItem(stateStore, state, installedDefinition.useEnergyType, out int gainedEnergy))
            {
                break;
            }

            state.storedEnergy += gainedEnergy;
            consumedAnyEnergyItem = true;
        }

        if (consumedAnyEnergyItem)
        {
            state.energyGaugeCapacity = Mathf.Max(state.storedEnergy, 1f);
        }

        return state.storedEnergy >= minimumOperationalEnergy;
    }

    private bool TryConsumeOneEnergyItem(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        ItemDefinition.EnergyType requiredEnergyType,
        out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (state == null
            || requiredEnergyType == ItemDefinition.EnergyType.None
            || state.inputEnergyCoordinates == null
            || state.inputEnergyCoordinates.Count <= 0)
        {
            return false;
        }

        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < state.inputEnergyCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.inputEnergyCoordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            int energyItemId = GetCenterTopItemId(stateStore, coordinate);
            if (energyItemId < 0)
            {
                continue;
            }

            ItemDefinition energyDefinition = ResolveItemDefinition(energyItemId);
            if (energyDefinition == null
                || energyDefinition.energyType != requiredEnergyType
                || energyDefinition.energyAmount <= 0)
            {
                continue;
            }

            if (RemoveCenterItems(stateStore, coordinate, energyItemId, 1) != 1)
            {
                continue;
            }

            gainedEnergy = energyDefinition.energyAmount;
            return true;
        }

        return false;
    }

    private bool TryResolveOutputCoordinate(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        int outputItemId,
        int outputCount,
        out Vector2Int targetCoordinate)
    {
        targetCoordinate = default;
        if (state == null
            || outputItemId < 0
            || outputCount <= 0
            || state.outputCoordinates == null
            || state.outputCoordinates.Count <= 0)
        {
            return false;
        }

        int totalCapacity = ResolveAreaCapacity(stateStore, state.outputCoordinates, templateModule.RuntimeAreaMaxObjects);
        if (GetAreaObjectCount(stateStore, state.outputCoordinates) + outputCount > totalCapacity)
        {
            return false;
        }

        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExistingStack = pass == 0;
            visitedCoordinates.Clear();

            for (int i = 0; i < state.outputCoordinates.Count; i++)
            {
                Vector2Int coordinate = state.outputCoordinates[i];
                if (!visitedCoordinates.Add(coordinate))
                {
                    continue;
                }

                SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
                int blockCapacity = ResolveBlockCenterCapacity(stateStore, coordinate, templateModule.RuntimeAreaMaxObjects);
                if (!CanAddCenterItems(inventory, outputItemId, outputCount, blockCapacity))
                {
                    continue;
                }

                if (requireExistingStack && GetCenterTopItemId(inventory) != outputItemId)
                {
                    continue;
                }

                targetCoordinate = coordinate;
                return true;
            }
        }

        return false;
    }

    private int ResolveAreaCapacity(BlockStateStore stateStore, IReadOnlyList<Vector2Int> coordinates, int defaultCapacity)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return Mathf.Max(1, defaultCapacity);
        }

        int installedCapacityTotal = 0;
        bool hasInstalledCapacity = false;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            if (!TryResolveInstalledItemAreaCapacity(stateStore, coordinate, out int blockCapacity))
            {
                continue;
            }

            installedCapacityTotal += Mathf.Max(1, blockCapacity);
            hasInstalledCapacity = true;
        }

        return hasInstalledCapacity
            ? Mathf.Max(1, installedCapacityTotal)
            : Mathf.Max(1, defaultCapacity);
    }

    private bool TryResolveInstalledItemAreaCapacity(BlockStateStore stateStore, Vector2Int coordinate, out int capacity)
    {
        capacity = 0;
        if (stateStore == null || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        if (!stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState))
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveItemDefinition(installationState.itemId);
        if (installedDefinition == null
            || !(installedDefinition.mapObject is InstallationObject installationObject)
            || (installationObject.MapFilter & InstallationMapFilter.ItemArea) == 0)
        {
            return false;
        }

        capacity = installedDefinition.capacity > 0 ? installedDefinition.capacity : 10;
        return true;
    }

    private static bool TryResolveRuntimeInputItemArea(
        InputOutputModule.PersistentState state,
        int recipeIndex,
        int inputItemId,
        out Vector2Int coordinate)
    {
        coordinate = default;
        if (state == null
            || inputItemId < 0
            || state.inputItemAreas == null
            || state.inputItemAreas.Count <= 0)
        {
            return false;
        }

        if (recipeIndex >= 0 && recipeIndex < state.inputItemAreas.Count)
        {
            InputOutputModule.PersistentInputItemAreaState indexedArea = state.inputItemAreas[recipeIndex];
            if (indexedArea.itemId == inputItemId)
            {
                coordinate = indexedArea.coordinate;
                return true;
            }
        }

        for (int i = 0; i < state.inputItemAreas.Count; i++)
        {
            InputOutputModule.PersistentInputItemAreaState area = state.inputItemAreas[i];
            if (area.itemId != inputItemId)
            {
                continue;
            }

            coordinate = area.coordinate;
            return true;
        }

        return false;
    }

    private static bool TryGetRecipePair(
        InputOutputModule templateModule,
        int recipeIndex,
        out int inputItemId,
        out int inputCount,
        out int outputItemId,
        out int outputCount)
    {
        inputItemId = -1;
        inputCount = 0;
        outputItemId = -1;
        outputCount = 0;

        if (templateModule == null
            || recipeIndex < 0
            || recipeIndex >= templateModule.InputList.Count
            || recipeIndex >= templateModule.OutputList.Count)
        {
            return false;
        }

        InputOutputModule.ItemIoEntry inputEntry = templateModule.InputList[recipeIndex];
        InputOutputModule.ItemIoEntry outputEntry = templateModule.OutputList[recipeIndex];
        if (inputEntry.itemDefinition == null || outputEntry.itemDefinition == null)
        {
            return false;
        }

        inputItemId = inputEntry.itemDefinition.id;
        inputCount = Mathf.Max(1, inputEntry.count);
        outputItemId = outputEntry.itemDefinition.id;
        outputCount = Mathf.Max(1, outputEntry.count);
        return inputItemId >= 0 && outputItemId >= 0;
    }

    private static bool RequiresOperationalEnergy(ItemDefinition installedDefinition)
    {
        return installedDefinition != null
               && installedDefinition.useEnergyType != ItemDefinition.EnergyType.None
               && installedDefinition.useEnergyAmount > 0f;
    }

    private static void ClearActiveCraft(InputOutputModule.PersistentState state)
    {
        if (state == null)
        {
            return;
        }

        state.hasActiveCraft = false;
        state.waitingForOutput = false;
        state.remainingCraftTime = 0f;
        state.activeRecipeIndex = -1;
        state.activeOutputItemId = -1;
        state.activeOutputCount = 0;
    }

    private static int GetAreaObjectCount(BlockStateStore stateStore, IReadOnlyList<Vector2Int> coordinates, int itemId = -1)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return 0;
        }

        int count = 0;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            count += GetCenterItemCount(stateStore, coordinate, itemId);
        }

        return count;
    }

    private static int GetCenterItemCount(BlockStateStore stateStore, Vector2Int coordinate, int itemId = -1)
    {
        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        if (itemId < 0)
        {
            return inventory.centerItems.Count;
        }

        int count = 0;
        for (int i = 0; i < inventory.centerItems.Count; i++)
        {
            if (inventory.centerItems[i] == itemId)
            {
                count++;
            }
        }

        return count;
    }

    private static int GetCenterTopItemId(BlockStateStore stateStore, Vector2Int coordinate)
    {
        return GetCenterTopItemId(LoadBlockInventory(stateStore, coordinate));
    }

    private static int GetCenterTopItemId(SavedBlockInventory inventory)
    {
        if (inventory == null || inventory.centerItems.Count <= 0)
        {
            return -1;
        }

        return inventory.centerItems[inventory.centerItems.Count - 1];
    }

    private static int ResolveBlockCenterCapacity(BlockStateStore stateStore, Vector2Int coordinate, int defaultCapacity)
    {
        return TryResolveInstalledItemAreaCapacityStatic(stateStore, coordinate, out int installedCapacity)
            ? Mathf.Max(1, installedCapacity)
            : Mathf.Max(1, defaultCapacity);
    }

    private static bool TryResolveInstalledItemAreaCapacityStatic(BlockStateStore stateStore, Vector2Int coordinate, out int capacity)
    {
        capacity = 0;
        if (stateStore == null || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        if (!stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState))
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveItemDefinition(installationState.itemId);
        if (installedDefinition == null
            || !(installedDefinition.mapObject is InstallationObject installationObject)
            || (installationObject.MapFilter & InstallationMapFilter.ItemArea) == 0)
        {
            return false;
        }

        capacity = installedDefinition.capacity > 0 ? installedDefinition.capacity : 10;
        return true;
    }

    private static bool CanAddCenterItems(SavedBlockInventory inventory, int itemId, int count, int capacity)
    {
        if (inventory == null || count <= 0)
        {
            return false;
        }

        if (inventory.centerItems.Count > 0 && inventory.centerItems[0] != itemId)
        {
            return false;
        }

        return Mathf.Max(1, capacity) - inventory.centerItems.Count >= count;
    }

    private static bool AddCenterItems(BlockStateStore stateStore, Vector2Int coordinate, int itemId, int count, int capacity)
    {
        if (count <= 0)
        {
            return true;
        }

        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        if (!CanAddCenterItems(inventory, itemId, count, capacity))
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            inventory.centerItems.Add(itemId);
        }

        SaveBlockInventory(stateStore, coordinate, inventory);
        return true;
    }

    private static int RemoveCenterItems(BlockStateStore stateStore, Vector2Int coordinate, int itemId, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        int removed = 0;
        for (int i = inventory.centerItems.Count - 1; i >= 0 && removed < count; i--)
        {
            if (itemId >= 0 && inventory.centerItems[i] != itemId)
            {
                continue;
            }

            inventory.centerItems.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
        {
            SaveBlockInventory(stateStore, coordinate, inventory);
        }

        return removed;
    }

    private static SavedBlockInventory LoadBlockInventory(BlockStateStore stateStore, Vector2Int coordinate)
    {
        if (stateStore != null && stateStore.TryGetFloorObjectsCopy(coordinate, out List<int> itemIds))
        {
            return SavedBlockInventory.FromSerialized(itemIds);
        }

        return new SavedBlockInventory();
    }

    private static void SaveBlockInventory(BlockStateStore stateStore, Vector2Int coordinate, SavedBlockInventory inventory)
    {
        if (stateStore == null)
        {
            return;
        }

        stateStore.SetFloorObjects(coordinate, inventory != null ? inventory.ToSerialized() : null);
    }

    private static bool TryResolveTemplateModule(int itemId, out ItemDefinition installedDefinition, out InputOutputModule templateModule)
    {
        installedDefinition = ResolveItemDefinition(itemId);
        templateModule = installedDefinition != null ? installedDefinition.mapObject as InputOutputModule : null;
        return installedDefinition != null && templateModule != null;
    }

    private static ItemDefinition ResolveItemDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private bool TryGetStateStore(out BlockStateStore stateStore)
    {
        if (cachedStateStore == null)
        {
            cachedStateStore = GetComponent<BlockStateStore>();
        }

        stateStore = cachedStateStore;
        return stateStore != null;
    }
}
