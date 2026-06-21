using System;
using System.Collections.Generic;

public static class SaveGameItemIdRemapper
{
    public static List<SaveItemCatalogEntry> CaptureItemCatalog(IReadOnlyList<ItemDefinition> definitions)
    {
        List<SaveItemCatalogEntry> catalog = new List<SaveItemCatalogEntry>();
        if (definitions == null)
        {
            return catalog;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id < 0)
            {
                continue;
            }

            catalog.Add(new SaveItemCatalogEntry
            {
                itemId = definition.id,
                itemName = ResolveStableItemName(definition)
            });
        }

        return catalog;
    }

    public static void RemapToCurrentDefinitions(
        SaveGameData data,
        IReadOnlyList<ItemDefinition> currentDefinitions)
    {
        if (data == null || currentDefinitions == null || currentDefinitions.Count <= 0)
        {
            return;
        }

        Dictionary<int, int> itemIdMap = BuildUnambiguousItemIdMap(data.itemCatalog, currentDefinitions);
        RemapMap(data.map, itemIdMap, currentDefinitions);
        RemapPlayer(data.player, itemIdMap);
    }

    private static Dictionary<int, int> BuildUnambiguousItemIdMap(
        IReadOnlyList<SaveItemCatalogEntry> catalog,
        IReadOnlyList<ItemDefinition> currentDefinitions)
    {
        Dictionary<int, int> idMap = new Dictionary<int, int>();
        HashSet<int> ambiguousSavedIds = new HashSet<int>();
        if (catalog == null)
        {
            return idMap;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            SaveItemCatalogEntry entry = catalog[i];
            if (entry == null || entry.itemId < 0 || string.IsNullOrWhiteSpace(entry.itemName))
            {
                continue;
            }

            ItemDefinition currentDefinition = ItemDefinitionLookup.ResolveByStableName(
                currentDefinitions,
                entry.itemName);
            if (currentDefinition == null || currentDefinition.id < 0)
            {
                continue;
            }

            if (idMap.TryGetValue(entry.itemId, out int existingCurrentId)
                && existingCurrentId != currentDefinition.id)
            {
                idMap.Remove(entry.itemId);
                ambiguousSavedIds.Add(entry.itemId);
                continue;
            }

            if (!ambiguousSavedIds.Contains(entry.itemId))
            {
                idMap[entry.itemId] = currentDefinition.id;
            }
        }

        return idMap;
    }

    private static void RemapMap(
        MapSaveData map,
        Dictionary<int, int> itemIdMap,
        IReadOnlyList<ItemDefinition> currentDefinitions)
    {
        if (map == null)
        {
            return;
        }

        if (map.resources != null)
        {
            for (int i = 0; i < map.resources.Count; i++)
            {
                ResourceSaveEntry entry = map.resources[i];
                if (entry != null)
                {
                    entry.itemId = RemapItemId(entry.itemId, itemIdMap);
                }
            }
        }

        if (map.floorObjects != null)
        {
            for (int i = 0; i < map.floorObjects.Count; i++)
            {
                RemapItemIdList(map.floorObjects[i]?.itemIds, itemIdMap);
            }
        }

        if (map.conveyorItems != null)
        {
            for (int i = 0; i < map.conveyorItems.Count; i++)
            {
                RemapConveyorBlock(map.conveyorItems[i], itemIdMap);
            }
        }

        if (map.installations != null)
        {
            for (int i = 0; i < map.installations.Count; i++)
            {
                RemapInstallation(map.installations[i]?.state, itemIdMap, currentDefinitions);
            }
        }
    }

    private static void RemapConveyorBlock(
        ConveyorItemBlockSaveEntry block,
        Dictionary<int, int> itemIdMap)
    {
        if (block?.lanes == null)
        {
            return;
        }

        for (int i = 0; i < block.lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = block.lanes[i];
            if (lane != null)
            {
                lane.itemId = RemapItemId(lane.itemId, itemIdMap);
            }
        }
    }

    private static void RemapInstallation(
        BlockStateStore.InstallationSaveState state,
        Dictionary<int, int> itemIdMap,
        IReadOnlyList<ItemDefinition> currentDefinitions)
    {
        if (state == null)
        {
            return;
        }

        if (!TryResolveCurrentItemId(state.itemName, currentDefinitions, true, out state.itemId))
        {
            state.itemId = RemapItemId(state.itemId, itemIdMap);
        }

        state.storedFluidItemId = RemapItemId(state.storedFluidItemId, itemIdMap);
        if (state.robotArmState != null)
        {
            state.robotArmState.heldItemId = RemapItemId(state.robotArmState.heldItemId, itemIdMap);
        }

        RemapInputOutputState(state.inputOutputState, itemIdMap);
        if (state.itemFilterMaskInitialized)
        {
            state.itemFilterMaskWords = RemapItemFilterMask(state.itemFilterMaskWords, itemIdMap);
        }
    }

    private static void RemapInputOutputState(
        InputOutputModule.PersistentState state,
        Dictionary<int, int> itemIdMap)
    {
        if (state == null)
        {
            return;
        }

        if (state.inputItemAreas != null)
        {
            for (int i = 0; i < state.inputItemAreas.Count; i++)
            {
                InputOutputModule.PersistentInputItemAreaState area = state.inputItemAreas[i];
                area.itemId = RemapItemId(area.itemId, itemIdMap);
                state.inputItemAreas[i] = area;
            }
        }

        state.activeOutputItemId = RemapItemId(state.activeOutputItemId, itemIdMap);
    }

    private static void RemapPlayer(
        PlayerSaveData player,
        Dictionary<int, int> itemIdMap)
    {
        if (player == null)
        {
            return;
        }

        RemapPlayerSlots(player.bagSlots, itemIdMap);
        RemapPlayerSlots(player.handSlots, itemIdMap);
        if (player.craftingQueue == null)
        {
            return;
        }

        for (int i = 0; i < player.craftingQueue.Count; i++)
        {
            PlayerCraftingQueueEntrySaveData entry = player.craftingQueue[i];
            if (entry == null)
            {
                continue;
            }

            entry.itemId = RemapItemId(entry.itemId, itemIdMap);
            if (entry.refundIngredients == null)
            {
                continue;
            }

            for (int refundIndex = 0; refundIndex < entry.refundIngredients.Count; refundIndex++)
            {
                PlayerCraftingIngredientSaveData ingredient = entry.refundIngredients[refundIndex];
                if (ingredient != null)
                {
                    ingredient.itemId = RemapItemId(ingredient.itemId, itemIdMap);
                }
            }
        }
    }

    private static void RemapPlayerSlots(
        IReadOnlyList<PlayerInventorySlotSaveState> slots,
        Dictionary<int, int> itemIdMap)
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            PlayerInventorySlotSaveState slot = slots[i];
            if (slot != null)
            {
                slot.itemId = RemapItemId(slot.itemId, itemIdMap);
            }
        }
    }

    private static void RemapItemIdList(List<int> itemIds, Dictionary<int, int> itemIdMap)
    {
        if (itemIds == null)
        {
            return;
        }

        for (int i = 0; i < itemIds.Count; i++)
        {
            itemIds[i] = RemapItemId(itemIds[i], itemIdMap);
        }
    }

    private static int RemapItemId(int itemId, Dictionary<int, int> itemIdMap)
    {
        return itemId >= 0 && itemIdMap != null && itemIdMap.TryGetValue(itemId, out int currentItemId)
            ? currentItemId
            : itemId;
    }

    private static bool TryResolveCurrentItemId(
        string itemName,
        IReadOnlyList<ItemDefinition> currentDefinitions,
        bool requireInstallation,
        out int itemId)
    {
        itemId = -1;
        ItemDefinition definition = requireInstallation
            ? ItemDefinitionLookup.ResolveInstallationByStableName(currentDefinitions, itemName)
            : ItemDefinitionLookup.ResolveByStableName(currentDefinitions, itemName);
        if (definition == null || definition.id < 0)
        {
            return false;
        }

        itemId = definition.id;
        return true;
    }

    private static List<ulong> RemapItemFilterMask(
        List<ulong> words,
        Dictionary<int, int> itemIdMap)
    {
        if (words == null || words.Count <= 0 || itemIdMap == null || itemIdMap.Count <= 0)
        {
            return words;
        }

        int oldBitCount = words.Count * 64;
        int maxTargetItemId = oldBitCount - 1;
        for (int oldItemId = 0; oldItemId < oldBitCount; oldItemId++)
        {
            if (IsBitSet(words, oldItemId))
            {
                maxTargetItemId = Math.Max(maxTargetItemId, RemapItemId(oldItemId, itemIdMap));
            }
        }

        int newWordCount = Math.Max(1, (maxTargetItemId + 64) >> 6);
        List<ulong> remappedWords = new List<ulong>(new ulong[newWordCount]);
        for (int oldItemId = 0; oldItemId < oldBitCount; oldItemId++)
        {
            if (IsBitSet(words, oldItemId))
            {
                SetBit(remappedWords, RemapItemId(oldItemId, itemIdMap));
            }
        }

        return remappedWords;
    }

    private static bool IsBitSet(IReadOnlyList<ulong> words, int itemId)
    {
        int wordIndex = itemId >> 6;
        if (words == null || wordIndex < 0 || wordIndex >= words.Count)
        {
            return false;
        }

        return (words[wordIndex] & (1UL << (itemId & 63))) != 0UL;
    }

    private static void SetBit(List<ulong> words, int itemId)
    {
        if (words == null || itemId < 0)
        {
            return;
        }

        int wordIndex = itemId >> 6;
        while (words.Count <= wordIndex)
        {
            words.Add(0UL);
        }

        words[wordIndex] |= 1UL << (itemId & 63);
    }

    private static string ResolveStableItemName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(definition.itemName))
        {
            return definition.itemName;
        }

        if (!string.IsNullOrWhiteSpace(definition.name))
        {
            return definition.name;
        }

        return definition.mapObject != null ? definition.mapObject.name : string.Empty;
    }
}
