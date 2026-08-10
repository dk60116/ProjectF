using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class CraftingTreeRuntime
{
    private const int CurrentCraftingTreeFileVersion = 5;
    private const int ItemNameCraftingTreeFileVersion = 5;
    private const int ItemIdCraftingTreeFileVersion = 4;
    private const int MultiCraftingMapObjectGuidFileVersion = 3;
    private const int OutputCountCraftingTreeFileVersion = 2;
    private const int LegacyCraftingTreeFileVersion = 1;

    public struct IngredientEntry
    {
        public int itemId;
        public int count;

        public IngredientEntry(int itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }

    private static readonly Dictionary<int, List<int>> CraftableByIngredient = new Dictionary<int, List<int>>();
    private static readonly Dictionary<int, List<IngredientEntry>> IngredientsByItem = new Dictionary<int, List<IngredientEntry>>();
    private static readonly Dictionary<int, List<int>> RequiredCraftingMapObjectIdsByItem = new Dictionary<int, List<int>>();
    private static readonly Dictionary<int, int> OutputCountByItem = new Dictionary<int, int>();
    private static bool loadAttempted;
    private static bool loaded;

    public static bool TryGetCraftableItemIds(int ingredientItemId, List<int> results)
    {
        if (results == null)
        {
            return false;
        }

        EnsureLoaded();
        results.Clear();

        if (CraftableByIngredient.TryGetValue(ingredientItemId, out List<int> craftables))
        {
            results.AddRange(craftables);
        }

        return results.Count > 0;
    }

    public static void ForceReload()
    {
        CraftableByIngredient.Clear();
        IngredientsByItem.Clear();
        RequiredCraftingMapObjectIdsByItem.Clear();
        OutputCountByItem.Clear();
        loadAttempted = false;
        loaded = false;
        EnsureLoaded();
    }

    public static bool TryGetIngredients(int itemId, List<IngredientEntry> results)
    {
        if (results == null)
        {
            return false;
        }

        EnsureLoaded();
        results.Clear();

        if (IngredientsByItem.TryGetValue(itemId, out List<IngredientEntry> ingredients))
        {
            results.AddRange(ingredients);
        }

        return results.Count > 0;
    }

    public static int GetOutputCount(int itemId)
    {
        EnsureLoaded();

        if (itemId < 0)
        {
            return 1;
        }

        if (OutputCountByItem.TryGetValue(itemId, out int outputCount))
        {
            return Mathf.Max(1, outputCount);
        }

        return 1;
    }

    public static bool TryGetRequiredCraftingMapObjectIds(int itemId, List<int> results)
    {
        if (results == null)
        {
            return false;
        }

        EnsureLoaded();
        results.Clear();

        if (RequiredCraftingMapObjectIdsByItem.TryGetValue(itemId, out List<int> requiredIds))
        {
            results.AddRange(requiredIds);
        }

        return results.Count > 0;
    }

    private static void EnsureLoaded()
    {
        if (loaded || loadAttempted)
        {
            return;
        }

        loadAttempted = true;

        byte[] data = LoadCraftingTreeBytes();
        if (data == null || data.Length == 0)
        {
            loaded = true;
            return;
        }

        try
        {
            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int version = reader.ReadInt32();
                if (version < LegacyCraftingTreeFileVersion || version > CurrentCraftingTreeFileVersion)
                {
                    Debug.LogWarning($"CraftingTreeRuntime: Unsupported version {version}.");
                    loaded = true;
                    return;
                }

                Dictionary<string, int> itemIdsByName = null;
                if (version >= ItemNameCraftingTreeFileVersion
                    && !TryBuildItemIdLookup(out itemIdsByName))
                {
                    loadAttempted = false;
                    return;
                }

                int itemCount = reader.ReadInt32();
                for (int i = 0; i < itemCount; i++)
                {
                    int itemId = ReadItemId(reader, version, itemIdsByName);
                    List<int> requiredCraftingMapObjectIds = ReadCraftingMapObjectRuntimeIds(reader, version, itemIdsByName);
                    int outputCount = version >= OutputCountCraftingTreeFileVersion
                        ? Mathf.Max(1, reader.ReadInt32())
                        : 1;

                    if (itemId >= 0)
                    {
                        if (requiredCraftingMapObjectIds != null && requiredCraftingMapObjectIds.Count > 0)
                        {
                            RequiredCraftingMapObjectIdsByItem[itemId] = requiredCraftingMapObjectIds;
                        }

                        OutputCountByItem[itemId] = outputCount;
                    }

                    int ingredientCount = reader.ReadInt32();
                    List<IngredientEntry> ingredientList = null;
                    for (int j = 0; j < ingredientCount; j++)
                    {
                        int ingredientId = ReadItemId(reader, version, itemIdsByName);
                        int ingredientCountValue = reader.ReadInt32();

                        if (itemId < 0 || ingredientId < 0)
                        {
                            continue;
                        }

                        AddCraftable(ingredientId, itemId);

                        if (ingredientList == null)
                        {
                            ingredientList = new List<IngredientEntry>(ingredientCount);
                        }

                        ingredientList.Add(new IngredientEntry(ingredientId, Mathf.Max(1, ingredientCountValue)));
                    }

                    if (ingredientList != null && ingredientList.Count > 0)
                    {
                        IngredientsByItem[itemId] = ingredientList;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CraftingTreeRuntime: Failed to load crafting data. {ex.Message}");
        }

        foreach (List<int> list in CraftableByIngredient.Values)
        {
            list.Sort();
        }

        foreach (List<IngredientEntry> list in IngredientsByItem.Values)
        {
            list.Sort((left, right) => left.itemId.CompareTo(right.itemId));
        }

        loaded = true;
    }

    private static byte[] LoadCraftingTreeBytes()
    {
        TextAsset resource = Resources.Load<TextAsset>("Data/CraftingTree/crafting_tree");
        if (resource != null)
        {
            return resource.bytes;
        }

        string path = Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.bytes");
        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadAllBytes(path);
    }

    private static void AddCraftable(int ingredientId, int itemId)
    {
        if (!CraftableByIngredient.TryGetValue(ingredientId, out List<int> list))
        {
            list = new List<int>();
            CraftableByIngredient.Add(ingredientId, list);
        }

        if (!list.Contains(itemId))
        {
            list.Add(itemId);
        }
    }

    private static List<int> ReadCraftingMapObjectRuntimeIds(
        BinaryReader reader,
        int version,
        Dictionary<string, int> itemIdsByName)
    {
        List<int> results = null;
        if (reader == null)
        {
            return results;
        }

        if (version >= ItemNameCraftingTreeFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                results = AppendRuntimeMapObjectId(
                    results,
                    ResolveItemId(reader.ReadString(), itemIdsByName),
                    mapObjectCount);
            }

            return results;
        }

        if (version >= ItemIdCraftingTreeFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                int runtimeId = reader.ReadInt32();
                if (runtimeId < 0)
                {
                    continue;
                }

                if (results == null)
                {
                    results = new List<int>(mapObjectCount);
                }

                if (!results.Contains(runtimeId))
                {
                    results.Add(runtimeId);
                }
            }

            return results;
        }

        if (version >= MultiCraftingMapObjectGuidFileVersion)
        {
            int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
            for (int i = 0; i < mapObjectCount; i++)
            {
                results = AppendRuntimeMapObjectId(
                    results,
                    ResolveRuntimeMapObjectIdFromGuid(reader.ReadString()),
                    mapObjectCount);
            }

            return results;
        }

        results = AppendRuntimeMapObjectId(results, ResolveRuntimeMapObjectIdFromGuid(reader.ReadString()), 1);
        return results;
    }

    private static int ReadItemId(BinaryReader reader, int version, Dictionary<string, int> itemIdsByName)
    {
        return version >= ItemNameCraftingTreeFileVersion
            ? ResolveItemId(reader.ReadString(), itemIdsByName)
            : reader.ReadInt32();
    }

    private static int ResolveItemId(string itemName, Dictionary<string, int> itemIdsByName)
    {
        if (itemIdsByName != null
            && !string.IsNullOrWhiteSpace(itemName)
            && itemIdsByName.TryGetValue(itemName.Trim(), out int itemId))
        {
            return itemId;
        }

        Debug.LogWarning($"CraftingTreeRuntime: ItemDefinition '{itemName}' was not found.");
        return -1;
    }

    private static bool TryBuildItemIdLookup(out Dictionary<string, int> itemIdsByName)
    {
        itemIdsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : UnityEngine.Object.FindAnyObjectByType<ItemManager>();
        if (itemManager == null)
        {
            return false;
        }

        IReadOnlyList<ItemDefinition> definitions = itemManager.ItemDefinitions;
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null || definition.id < 0)
                {
                    continue;
                }

                AddItemName(
                    itemIdsByName,
                    ItemDefinitionLookup.GetPersistenceName(definition, definitions),
                    definition.id);
            }
        }

        if (itemIdsByName.Count == 0)
        {
            IReadOnlyList<ItemManager.ItemSet> itemSets = itemManager.ItemSets;
            if (itemSets != null)
            {
                for (int i = 0; i < itemSets.Count; i++)
                {
                    AddItemName(itemIdsByName, itemSets[i].name, itemSets[i].id);
                }
            }
        }

        return itemIdsByName.Count > 0;
    }

    private static void AddItemName(Dictionary<string, int> itemIdsByName, string itemName, int itemId)
    {
        if (itemIdsByName == null || string.IsNullOrWhiteSpace(itemName) || itemId < 0)
        {
            return;
        }

        string normalizedName = itemName.Trim();
        if (itemIdsByName.TryGetValue(normalizedName, out int existingId))
        {
            if (existingId != itemId)
            {
                Debug.LogError($"CraftingTreeRuntime: duplicate item name '{normalizedName}' ({existingId}, {itemId}).");
            }

            return;
        }

        itemIdsByName.Add(normalizedName, itemId);
    }

    private static List<int> AppendRuntimeMapObjectId(List<int> results, int runtimeId, int capacityHint)
    {
        if (runtimeId < 0)
        {
            return results;
        }

        if (results == null)
        {
            results = new List<int>(Mathf.Max(1, capacityHint));
        }

        if (!results.Contains(runtimeId))
        {
            results.Add(runtimeId);
        }

        return results;
    }

    private static int ResolveRuntimeMapObjectIdFromGuid(string guid)
    {
#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(guid))
        {
            return -1;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return -1;
        }

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefabRoot == null)
        {
            return -1;
        }

        MapObject mapObject = prefabRoot.GetComponent<MapObject>();
        if (mapObject == null)
        {
            mapObject = prefabRoot.GetComponentInChildren<MapObject>(true);
        }

        return mapObject != null ? mapObject.ResolveItemId() : -1;
#else
        return -1;
#endif
    }
}
