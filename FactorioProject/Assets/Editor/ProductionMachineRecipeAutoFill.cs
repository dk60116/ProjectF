using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class ProductionMachineRecipeAutoFill
{
    private const string ProductionMachineMk1Name = "Production machine (Mk1)";
    private const string ProductionMachineMk2Name = "Production machine (MK2)";
    private const int CurrentCraftingTreeFileVersion = 5;
    private const int ItemNameCraftingTreeFileVersion = 5;
    private const int ItemIdCraftingTreeFileVersion = 4;
    private const int MultiCraftingMapObjectGuidFileVersion = 3;
    private const int OutputCountCraftingTreeFileVersion = 2;
    private const int LegacyCraftingTreeFileVersion = 1;

    [Serializable]
    private sealed class CraftingTreeJsonFile
    {
        public List<CraftingTreeJsonEntry> recipes = new List<CraftingTreeJsonEntry>();
        public List<CraftingTreeJsonEntry> items = new List<CraftingTreeJsonEntry>();
        public List<CraftingTreeJsonEntry> entries = new List<CraftingTreeJsonEntry>();
    }

    [Serializable]
    private sealed class CraftingTreeJsonEntry
    {
        public int itemId = -1;
        public string itemName = string.Empty;
        public string definitionAssetPath = string.Empty;
        public int outputCount = 1;
        public List<CraftingIngredientJsonEntry> ingredients = new List<CraftingIngredientJsonEntry>();
        public List<CraftingMapObjectJsonEntry> craftingMapObjects = new List<CraftingMapObjectJsonEntry>();
        public List<CraftingMapObjectJsonEntry> requiredMapObjects = new List<CraftingMapObjectJsonEntry>();
    }

    [Serializable]
    private sealed class CraftingIngredientJsonEntry
    {
        public int itemId = -1;
        public string itemName = string.Empty;
        public string definitionAssetPath = string.Empty;
        public int count = 1;
    }

    [Serializable]
    private sealed class CraftingMapObjectJsonEntry
    {
        public int itemId = -1;
        public string mapObjectName = string.Empty;
        public string assetPath = string.Empty;
    }

    private readonly struct RecipeEntry
    {
        public readonly ItemDefinition inputDefinition;
        public readonly int inputCount;
        public readonly ItemDefinition outputDefinition;
        public readonly int outputCount;

        public RecipeEntry(
            ItemDefinition inputDefinition,
            int inputCount,
            ItemDefinition outputDefinition,
            int outputCount)
        {
            this.inputDefinition = inputDefinition;
            this.inputCount = Mathf.Max(1, inputCount);
            this.outputDefinition = outputDefinition;
            this.outputCount = Mathf.Max(1, outputCount);
        }
    }

    public static int SyncProductionMachines(ItemManager itemManager)
    {
        List<ItemDefinition> definitions = CollectDefinitions(itemManager);
        if (definitions.Count == 0)
        {
            return 0;
        }

        int syncedRecipeCount = 0;
        ProductionMachine mk1 = FindProductionMachine(definitions, ProductionMachineMk1Name);
        if (mk1 != null)
        {
            List<RecipeEntry> recipes = BuildInputRecipes(definitions, 1);
            ApplyRecipes(mk1, recipes);
            syncedRecipeCount += recipes.Count;
        }

        ProductionMachine mk2 = FindProductionMachine(definitions, ProductionMachineMk2Name);
        if (mk2 != null)
        {
            List<RecipeEntry> recipes = BuildInputRecipes(definitions, 2);
            ApplyRecipes(mk2, recipes);
            syncedRecipeCount += recipes.Count;
        }

        return syncedRecipeCount;
    }

    private static List<ItemDefinition> CollectDefinitions(ItemManager itemManager)
    {
        List<ItemDefinition> results = new List<ItemDefinition>();
        if (itemManager == null || itemManager.ItemDefinitions == null)
        {
            return results;
        }

        for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
        {
            ItemDefinition definition = itemManager.ItemDefinitions[i];
            if (definition != null && definition.id >= 0)
            {
                results.Add(definition);
            }
        }

        return results;
    }

    private static ProductionMachine FindProductionMachine(List<ItemDefinition> definitions, string machineName)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            ProductionMachine productionMachine = ResolveProductionMachine(definition.mapObject);
            if (productionMachine == null)
            {
                continue;
            }

            string definitionName = GetDefinitionDisplayName(definition);
            GameObject prefabRoot = productionMachine.transform.root != null
                ? productionMachine.transform.root.gameObject
                : productionMachine.gameObject;
            string prefabName = prefabRoot != null ? prefabRoot.name : productionMachine.name;
            if (string.Equals(definitionName, machineName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabName, machineName, StringComparison.OrdinalIgnoreCase))
            {
                return productionMachine;
            }
        }

        return null;
    }

    private static ProductionMachine ResolveProductionMachine(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        if (mapObject is ProductionMachine productionMachine)
        {
            return productionMachine;
        }

        return mapObject.GetComponentInChildren<ProductionMachine>(true);
    }

    private static List<RecipeEntry> BuildInputRecipes(List<ItemDefinition> definitions, int maxIngredientTypes)
    {
        List<RecipeEntry> recipes = new List<RecipeEntry>();
        DefinitionLookup definitionLookup = new DefinitionLookup(definitions);
        List<CraftingTreeJsonEntry> entries = LoadCraftingTreeEntries(definitions);
        HashSet<int> seenOutputItemIds = new HashSet<int>();
        int allowedIngredientTypes = Mathf.Max(1, maxIngredientTypes);

        for (int i = 0; i < entries.Count; i++)
        {
            CraftingTreeJsonEntry entry = entries[i];
            if (entry == null
                || entry.ingredients == null
                || entry.ingredients.Count <= 0
                || entry.ingredients.Count > allowedIngredientTypes)
            {
                continue;
            }

            CraftingIngredientJsonEntry ingredient = entry.ingredients[0];
            if (ingredient == null
                || (ingredient.itemId < 0
                    && string.IsNullOrWhiteSpace(ingredient.itemName)
                    && string.IsNullOrWhiteSpace(ingredient.definitionAssetPath)))
            {
                continue;
            }

            if (!IsCraftableByHandOrWorkableObject(entry, definitionLookup))
            {
                continue;
            }

            ItemDefinition inputDefinition = definitionLookup.Resolve(
                ingredient.itemName,
                ingredient.definitionAssetPath,
                ingredient.itemId);
            ItemDefinition outputDefinition = definitionLookup.Resolve(
                entry.itemName,
                entry.definitionAssetPath,
                entry.itemId);
            if (inputDefinition == null || outputDefinition == null)
            {
                continue;
            }

            if (!seenOutputItemIds.Add(outputDefinition.id))
            {
                continue;
            }

            recipes.Add(new RecipeEntry(
                inputDefinition,
                Mathf.Max(1, ingredient.count),
                outputDefinition,
                Mathf.Max(1, entry.outputCount)));
        }

        return recipes;
    }

    private static bool IsCraftableByHandOrWorkableObject(CraftingTreeJsonEntry entry, DefinitionLookup definitionLookup)
    {
        IReadOnlyList<CraftingMapObjectJsonEntry> craftingMapObjects = GetCraftingMapObjectEntries(entry);
        if (craftingMapObjects.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < craftingMapObjects.Count; i++)
        {
            CraftingMapObjectJsonEntry mapObjectEntry = craftingMapObjects[i];
            if (mapObjectEntry == null)
            {
                continue;
            }

            MapObject mapObject = definitionLookup.ResolveMapObject(
                mapObjectEntry.mapObjectName,
                mapObjectEntry.assetPath,
                mapObjectEntry.itemId);
            if (IsWorkableMapObject(mapObject))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<CraftingMapObjectJsonEntry> GetCraftingMapObjectEntries(CraftingTreeJsonEntry entry)
    {
        if (entry == null)
        {
            return Array.Empty<CraftingMapObjectJsonEntry>();
        }

        if (entry.craftingMapObjects != null && entry.craftingMapObjects.Count > 0)
        {
            return entry.craftingMapObjects;
        }

        if (entry.requiredMapObjects != null && entry.requiredMapObjects.Count > 0)
        {
            return entry.requiredMapObjects;
        }

        return Array.Empty<CraftingMapObjectJsonEntry>();
    }

    private static bool IsWorkableMapObject(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return false;
        }

        return mapObject is WorkableObject || mapObject.GetComponentInChildren<WorkableObject>(true) != null;
    }

    private sealed class DefinitionLookup
    {
        private readonly List<ItemDefinition> definitions = new List<ItemDefinition>();
        private readonly Dictionary<int, ItemDefinition> definitionsById = new Dictionary<int, ItemDefinition>();
        private readonly Dictionary<string, ItemDefinition> definitionsByName = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);

        public DefinitionLookup(List<ItemDefinition> definitions)
        {
            if (definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                this.definitions.Add(definition);

                if (definition.id >= 0 && !definitionsById.ContainsKey(definition.id))
                {
                    definitionsById.Add(definition.id, definition);
                }

                string definitionName = GetDefinitionDisplayName(definition);
                if (!string.IsNullOrWhiteSpace(definitionName) && !definitionsByName.ContainsKey(definitionName))
                {
                    definitionsByName.Add(definitionName, definition);
                }
            }
        }

        public ItemDefinition Resolve(string itemName, string assetPath, int itemId)
        {
            string normalizedName = string.IsNullOrWhiteSpace(itemName) ? string.Empty : itemName.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedName)
                && definitionsByName.TryGetValue(normalizedName, out ItemDefinition namedDefinition))
            {
                return namedDefinition;
            }

            ItemDefinition pathDefinition = ResolveByAssetPath(assetPath);
            if (pathDefinition != null && (string.IsNullOrWhiteSpace(normalizedName)
                                           || string.Equals(GetDefinitionDisplayName(pathDefinition), normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                return pathDefinition;
            }

            if (string.IsNullOrWhiteSpace(normalizedName)
                && itemId >= 0
                && definitionsById.TryGetValue(itemId, out ItemDefinition idDefinition))
            {
                return idDefinition;
            }

            return null;
        }

        public MapObject ResolveMapObject(string mapObjectName, string assetPath, int itemId)
        {
            string normalizedName = string.IsNullOrWhiteSpace(mapObjectName) ? string.Empty : mapObjectName.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                MapObject namedMapObject = ResolveMapObjectByName(normalizedName);
                if (namedMapObject != null)
                {
                    return namedMapObject;
                }
            }

            MapObject pathMapObject = ResolveMapObjectByAssetPath(assetPath);
            if (pathMapObject != null && (string.IsNullOrWhiteSpace(normalizedName)
                                          || MapObjectNameMatches(pathMapObject, normalizedName)))
            {
                return pathMapObject;
            }

            if (string.IsNullOrWhiteSpace(normalizedName)
                && itemId >= 0
                && definitionsById.TryGetValue(itemId, out ItemDefinition idDefinition))
            {
                return idDefinition != null ? idDefinition.mapObject : null;
            }

            return null;
        }

        private MapObject ResolveMapObjectByName(string normalizedName)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null || definition.mapObject == null)
                {
                    continue;
                }

                if (string.Equals(GetDefinitionDisplayName(definition), normalizedName, StringComparison.OrdinalIgnoreCase)
                    || MapObjectNameMatches(definition.mapObject, normalizedName))
                {
                    return definition.mapObject;
                }
            }

            return null;
        }

        private static ItemDefinition ResolveByAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            string normalizedPath = assetPath.Trim().Replace("\\", "/");
            return AssetDatabase.LoadAssetAtPath<ItemDefinition>(normalizedPath);
        }

        private static MapObject ResolveMapObjectByAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            string normalizedPath = assetPath.Trim().Replace("\\", "/");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPath);
            return prefab != null ? prefab.GetComponentInChildren<MapObject>(true) : null;
        }

        private static bool MapObjectNameMatches(MapObject mapObject, string normalizedName)
        {
            if (mapObject == null || string.IsNullOrWhiteSpace(normalizedName))
            {
                return false;
            }

            if (string.Equals(mapObject.name, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            GameObject prefabRoot = mapObject.transform.root != null
                ? mapObject.transform.root.gameObject
                : mapObject.gameObject;
            return prefabRoot != null
                   && string.Equals(prefabRoot.name, normalizedName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<CraftingTreeJsonEntry> LoadCraftingTreeEntries(List<ItemDefinition> definitions)
    {
        if (TryLoadCraftingTreeBytes(definitions, out List<CraftingTreeJsonEntry> binaryEntries))
        {
            return binaryEntries;
        }

        CraftingTreeJsonFile craftingTree = LoadCraftingTreeJson();
        return GetCraftingTreeEntries(craftingTree);
    }

    private static bool TryLoadCraftingTreeBytes(
        List<ItemDefinition> definitions,
        out List<CraftingTreeJsonEntry> entries)
    {
        entries = new List<CraftingTreeJsonEntry>();

        string absolutePath = Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.bytes");
        if (!File.Exists(absolutePath))
        {
            absolutePath = Path.Combine(Application.dataPath, "Resources", "Data", "CraftingTree", "crafting_tree.bytes");
        }

        if (!File.Exists(absolutePath))
        {
            return false;
        }

        try
        {
            using (FileStream stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int version = reader.ReadInt32();
                if (version < LegacyCraftingTreeFileVersion || version > CurrentCraftingTreeFileVersion)
                {
                    return false;
                }

                int itemCount = Mathf.Max(0, reader.ReadInt32());
                for (int i = 0; i < itemCount; i++)
                {
                    CraftingTreeJsonEntry entry = new CraftingTreeJsonEntry();
                    ReadItemReference(reader, version, definitions, entry);

                    List<CraftingMapObjectJsonEntry> mapObjects =
                        ReadCraftingMapObjectEntries(reader, version, definitions);
                    entry.craftingMapObjects.AddRange(mapObjects);
                    entry.requiredMapObjects.AddRange(mapObjects);

                    entry.outputCount = version >= OutputCountCraftingTreeFileVersion
                        ? Mathf.Max(1, reader.ReadInt32())
                        : 1;

                    int ingredientCount = Mathf.Max(0, reader.ReadInt32());
                    for (int ingredientIndex = 0; ingredientIndex < ingredientCount; ingredientIndex++)
                    {
                        CraftingIngredientJsonEntry ingredient = new CraftingIngredientJsonEntry();
                        ReadItemReference(reader, version, definitions, ingredient);
                        ingredient.count = Mathf.Max(1, reader.ReadInt32());
                        entry.ingredients.Add(ingredient);
                    }

                    entries.Add(entry);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ProductionMachineRecipeAutoFill: Failed to load CraftingTree bytes. {ex.Message}");
            entries.Clear();
            return false;
        }
    }

    private static List<CraftingMapObjectJsonEntry> ReadCraftingMapObjectEntries(
        BinaryReader reader,
        int version,
        List<ItemDefinition> definitions)
    {
        List<CraftingMapObjectJsonEntry> results = new List<CraftingMapObjectJsonEntry>();
        if (reader == null)
        {
            return results;
        }

        if (version >= ItemNameCraftingTreeFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                string persistenceName = reader.ReadString();
                ItemDefinition definition =
                    ItemDefinitionLookup.ResolveByPersistenceName(definitions, persistenceName);
                if (definition != null)
                {
                    results.Add(new CraftingMapObjectJsonEntry
                    {
                        itemId = definition.id,
                        mapObjectName = GetDefinitionDisplayName(definition),
                        assetPath = definition.mapObject != null
                            ? AssetDatabase.GetAssetPath(definition.mapObject.transform.root.gameObject)
                            : string.Empty
                    });
                }
            }

            return results;
        }

        if (version >= ItemIdCraftingTreeFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                int runtimeId = reader.ReadInt32();
                if (runtimeId >= 0)
                {
                    results.Add(new CraftingMapObjectJsonEntry { itemId = runtimeId });
                }
            }

            return results;
        }

        if (version >= MultiCraftingMapObjectGuidFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                AddGuidMapObjectEntry(results, reader.ReadString());
            }

            return results;
        }

        AddGuidMapObjectEntry(results, reader.ReadString());
        return results;
    }

    private static void ReadItemReference(
        BinaryReader reader,
        int version,
        List<ItemDefinition> definitions,
        CraftingTreeJsonEntry entry)
    {
        if (version < ItemNameCraftingTreeFileVersion)
        {
            entry.itemId = reader.ReadInt32();
            return;
        }

        ApplyItemReference(
            ItemDefinitionLookup.ResolveByPersistenceName(definitions, reader.ReadString()),
            entry);
    }

    private static void ReadItemReference(
        BinaryReader reader,
        int version,
        List<ItemDefinition> definitions,
        CraftingIngredientJsonEntry entry)
    {
        if (version < ItemNameCraftingTreeFileVersion)
        {
            entry.itemId = reader.ReadInt32();
            return;
        }

        ApplyItemReference(
            ItemDefinitionLookup.ResolveByPersistenceName(definitions, reader.ReadString()),
            entry);
    }

    private static void ApplyItemReference(ItemDefinition definition, CraftingTreeJsonEntry entry)
    {
        if (definition == null || entry == null)
        {
            return;
        }

        entry.itemId = definition.id;
        entry.itemName = GetDefinitionDisplayName(definition);
        entry.definitionAssetPath = AssetDatabase.GetAssetPath(definition);
    }

    private static void ApplyItemReference(ItemDefinition definition, CraftingIngredientJsonEntry entry)
    {
        if (definition == null || entry == null)
        {
            return;
        }

        entry.itemId = definition.id;
        entry.itemName = GetDefinitionDisplayName(definition);
        entry.definitionAssetPath = AssetDatabase.GetAssetPath(definition);
    }

    private static void AddGuidMapObjectEntry(List<CraftingMapObjectJsonEntry> results, string guid)
    {
        if (results == null || string.IsNullOrWhiteSpace(guid))
        {
            return;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            results.Add(new CraftingMapObjectJsonEntry { assetPath = assetPath });
        }
    }

    private static CraftingTreeJsonFile LoadCraftingTreeJson()
    {
        string absolutePath = Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.json");
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        string json = File.ReadAllText(absolutePath);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonUtility.FromJson<CraftingTreeJsonFile>(json);
    }

    private static List<CraftingTreeJsonEntry> GetCraftingTreeEntries(CraftingTreeJsonFile file)
    {
        if (file == null)
        {
            return new List<CraftingTreeJsonEntry>();
        }

        if (file.recipes != null && file.recipes.Count > 0)
        {
            return file.recipes;
        }

        if (file.items != null && file.items.Count > 0)
        {
            return file.items;
        }

        if (file.entries != null && file.entries.Count > 0)
        {
            return file.entries;
        }

        return new List<CraftingTreeJsonEntry>();
    }

    private static void ApplyRecipes(ProductionMachine productionMachine, List<RecipeEntry> recipes)
    {
        if (productionMachine == null || recipes == null)
        {
            return;
        }

        Undo.RecordObject(productionMachine, "Auto Fill Production Machine Recipes");
        SerializedObject serializedMachine = new SerializedObject(productionMachine);
        serializedMachine.Update();

        SerializedProperty inputListProperty = serializedMachine.FindProperty("inputList");
        SerializedProperty outputListProperty = serializedMachine.FindProperty("outputList");
        SerializedProperty legacyOutputProperty = serializedMachine.FindProperty("output");
        if (inputListProperty == null || outputListProperty == null)
        {
            return;
        }

        inputListProperty.ClearArray();
        outputListProperty.ClearArray();

        for (int i = 0; i < recipes.Count; i++)
        {
            RecipeEntry recipe = recipes[i];

            inputListProperty.InsertArrayElementAtIndex(i);
            SetIoEntry(inputListProperty.GetArrayElementAtIndex(i), recipe.inputDefinition, recipe.inputCount);

            outputListProperty.InsertArrayElementAtIndex(i);
            SetIoEntry(outputListProperty.GetArrayElementAtIndex(i), recipe.outputDefinition, recipe.outputCount);
        }

        if (legacyOutputProperty != null)
        {
            SetIoEntry(legacyOutputProperty, null, 1);
        }

        serializedMachine.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(productionMachine);

        GameObject prefabRoot = productionMachine.transform.root != null
            ? productionMachine.transform.root.gameObject
            : productionMachine.gameObject;
        if (prefabRoot != null)
        {
            EditorUtility.SetDirty(prefabRoot);
            if (PrefabUtility.IsPartOfPrefabAsset(prefabRoot))
            {
                PrefabUtility.SavePrefabAsset(prefabRoot);
            }
        }
    }

    private static void SetIoEntry(SerializedProperty entryProperty, ItemDefinition definition, int count)
    {
        if (entryProperty == null)
        {
            return;
        }

        SerializedProperty itemDefinitionProperty = entryProperty.FindPropertyRelative("itemDefinition");
        SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");
        if (itemDefinitionProperty != null)
        {
            itemDefinitionProperty.objectReferenceValue = definition;
        }

        if (countProperty != null)
        {
            countProperty.intValue = Mathf.Max(1, count);
        }
    }

    private static string GetDefinitionDisplayName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName.Trim();
    }
}
