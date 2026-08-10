using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class CraftingTreeItemIdRemapper
{
    private const int CurrentCraftingTreeFileVersion = 5;
    private const int ItemNameCraftingTreeFileVersion = 5;
    private const int ItemIdCraftingTreeFileVersion = 4;

    [Serializable]
    private sealed class CraftingTreeJsonFile
    {
        public string format = "ProjectF.CraftingTree";
        public int version = 2;
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

    private struct BinaryIngredientEntry
    {
        public int itemId;
        public int count;
    }

    private sealed class BinaryRecipeEntry
    {
        public int itemId;
        public int outputCount;
        public readonly List<int> requiredMapObjectItemIds = new List<int>();
        public readonly List<BinaryIngredientEntry> ingredients = new List<BinaryIngredientEntry>();
    }

    internal sealed class CapturedCraftingTree
    {
        internal readonly List<CapturedRecipeEntry> recipes = new List<CapturedRecipeEntry>();

        public bool HasRecipes => recipes.Count > 0;
    }

    internal sealed class CapturedRecipeEntry
    {
        public DefinitionReference item;
        public int outputCount = 1;
        public readonly List<CapturedMapObjectEntry> mapObjects = new List<CapturedMapObjectEntry>();
        public readonly List<CapturedIngredientEntry> ingredients = new List<CapturedIngredientEntry>();
    }

    internal sealed class CapturedIngredientEntry
    {
        public DefinitionReference item;
        public int count = 1;
    }

    internal sealed class CapturedMapObjectEntry
    {
        public DefinitionReference item;
        public string mapObjectName = string.Empty;
        public string assetPath = string.Empty;
    }

    internal sealed class DefinitionReference
    {
        public int oldItemId = -1;
        public string guid = string.Empty;
        public string assetPath = string.Empty;
        public string itemName = string.Empty;

        public bool HasStableIdentity =>
            !string.IsNullOrWhiteSpace(guid)
            || !string.IsNullOrWhiteSpace(assetPath)
            || !string.IsNullOrWhiteSpace(itemName);
    }

    private sealed class DefinitionLookup
    {
        public readonly IReadOnlyList<ItemDefinition> definitions;
        public readonly Dictionary<string, ItemDefinition> byGuid = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ItemDefinition> byPath = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ItemDefinition> byName = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);

        public DefinitionLookup(IReadOnlyList<ItemDefinition> definitions)
        {
            this.definitions = definitions;
        }
    }

    private sealed class DefinitionIdentity
    {
        public int itemId = -1;
        public string guid = string.Empty;
        public string assetPath = string.Empty;
        public string itemName = string.Empty;
        public string persistenceName = string.Empty;
    }

    internal static CapturedCraftingTree CapturePersistedCraftingTree(IReadOnlyList<ItemDefinition> definitions)
    {
        Dictionary<int, DefinitionIdentity> identitiesById = BuildDefinitionIdentitiesById(definitions);

        CapturedCraftingTree captured = CaptureBinaryFile(GetCraftingTreeAssetPath(), identitiesById);
        if (captured.HasRecipes)
        {
            return captured;
        }

        captured = CaptureBinaryFile(GetCraftingTreeResourcesPath(), identitiesById);
        if (captured.HasRecipes)
        {
            return captured;
        }

        return CaptureJsonFile(GetCraftingTreeJsonPath(), identitiesById);
    }

    internal static bool RewritePersistedCraftingTree(CapturedCraftingTree capturedTree, IReadOnlyList<ItemDefinition> definitions)
    {
        if (capturedTree == null)
        {
            return false;
        }

        DefinitionLookup lookup = BuildDefinitionLookup(definitions);
        List<BinaryRecipeEntry> entries = BuildBinaryEntries(capturedTree, lookup);
        WriteCurrentBinaryFile(GetCraftingTreeAssetPath(), entries, definitions);
        WriteCurrentBinaryFile(GetCraftingTreeResourcesPath(), entries, definitions);
        WriteJsonFile(GetCraftingTreeJsonPath(), entries, definitions);

        AssetDatabase.Refresh();
        CraftingTreeRuntime.ForceReload();
        return true;
    }

    private static CapturedCraftingTree CaptureBinaryFile(string path, Dictionary<int, DefinitionIdentity> identitiesById)
    {
        CapturedCraftingTree captured = new CapturedCraftingTree();
        if (!TryReadCurrentBinaryFile(path, identitiesById, out List<BinaryRecipeEntry> entries))
        {
            return captured;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            BinaryRecipeEntry sourceEntry = entries[i];
            DefinitionReference targetReference = BuildDefinitionReference(sourceEntry.itemId, identitiesById);
            if (!targetReference.HasStableIdentity)
            {
                Debug.LogWarning($"CraftingTreeItemIdRemapper: skipped recipe for unresolved item id {sourceEntry.itemId}.");
                continue;
            }

            CapturedRecipeEntry capturedEntry = new CapturedRecipeEntry
            {
                item = targetReference,
                outputCount = Mathf.Max(1, sourceEntry.outputCount)
            };

            for (int mapObjectIndex = 0; mapObjectIndex < sourceEntry.requiredMapObjectItemIds.Count; mapObjectIndex++)
            {
                DefinitionReference mapObjectReference = BuildDefinitionReference(sourceEntry.requiredMapObjectItemIds[mapObjectIndex], identitiesById);
                if (mapObjectReference.HasStableIdentity)
                {
                    capturedEntry.mapObjects.Add(new CapturedMapObjectEntry
                    {
                        item = mapObjectReference
                    });
                }
            }

            for (int ingredientIndex = 0; ingredientIndex < sourceEntry.ingredients.Count; ingredientIndex++)
            {
                BinaryIngredientEntry sourceIngredient = sourceEntry.ingredients[ingredientIndex];
                DefinitionReference ingredientReference = BuildDefinitionReference(sourceIngredient.itemId, identitiesById);
                if (!ingredientReference.HasStableIdentity)
                {
                    Debug.LogWarning($"CraftingTreeItemIdRemapper: skipped unresolved ingredient id {sourceIngredient.itemId}.");
                    continue;
                }

                capturedEntry.ingredients.Add(new CapturedIngredientEntry
                {
                    item = ingredientReference,
                    count = Mathf.Max(1, sourceIngredient.count)
                });
            }

            captured.recipes.Add(capturedEntry);
        }

        return captured;
    }

    private static CapturedCraftingTree CaptureJsonFile(string path, Dictionary<int, DefinitionIdentity> identitiesById)
    {
        CapturedCraftingTree captured = new CapturedCraftingTree();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return captured;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return captured;
            }

            CraftingTreeJsonFile file = JsonUtility.FromJson<CraftingTreeJsonFile>(json);
            List<CraftingTreeJsonEntry> entries = GetJsonEntries(file);
            for (int i = 0; i < entries.Count; i++)
            {
                CraftingTreeJsonEntry sourceEntry = entries[i];
                DefinitionReference targetReference = BuildDefinitionReference(
                    sourceEntry.definitionAssetPath,
                    sourceEntry.itemName,
                    sourceEntry.itemId,
                    identitiesById);

                if (!targetReference.HasStableIdentity)
                {
                    Debug.LogWarning($"CraftingTreeItemIdRemapper: skipped JSON recipe for unresolved item id {sourceEntry.itemId}.");
                    continue;
                }

                CapturedRecipeEntry capturedEntry = new CapturedRecipeEntry
                {
                    item = targetReference,
                    outputCount = Mathf.Max(1, sourceEntry.outputCount)
                };

                List<CraftingMapObjectJsonEntry> mapObjects = GetJsonMapObjectEntries(sourceEntry);
                for (int mapObjectIndex = 0; mapObjectIndex < mapObjects.Count; mapObjectIndex++)
                {
                    CraftingMapObjectJsonEntry mapObjectEntry = mapObjects[mapObjectIndex];
                    capturedEntry.mapObjects.Add(new CapturedMapObjectEntry
                    {
                        item = BuildDefinitionReference(mapObjectEntry.itemId, identitiesById),
                        mapObjectName = mapObjectEntry.mapObjectName ?? string.Empty,
                        assetPath = NormalizeAssetPath(mapObjectEntry.assetPath)
                    });
                }

                if (sourceEntry.ingredients != null)
                {
                    for (int ingredientIndex = 0; ingredientIndex < sourceEntry.ingredients.Count; ingredientIndex++)
                    {
                        CraftingIngredientJsonEntry sourceIngredient = sourceEntry.ingredients[ingredientIndex];
                        DefinitionReference ingredientReference = BuildDefinitionReference(
                            sourceIngredient.definitionAssetPath,
                            sourceIngredient.itemName,
                            sourceIngredient.itemId,
                            identitiesById);

                        if (!ingredientReference.HasStableIdentity)
                        {
                            Debug.LogWarning($"CraftingTreeItemIdRemapper: skipped unresolved JSON ingredient id {sourceIngredient.itemId}.");
                            continue;
                        }

                        capturedEntry.ingredients.Add(new CapturedIngredientEntry
                        {
                            item = ingredientReference,
                            count = Mathf.Max(1, sourceIngredient.count)
                        });
                    }
                }

                captured.recipes.Add(capturedEntry);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"CraftingTreeItemIdRemapper: failed to capture JSON crafting tree '{path}'. {exception.Message}");
        }

        return captured;
    }

    private static List<BinaryRecipeEntry> BuildBinaryEntries(CapturedCraftingTree capturedTree, DefinitionLookup lookup)
    {
        List<BinaryRecipeEntry> entries = new List<BinaryRecipeEntry>();
        for (int i = 0; i < capturedTree.recipes.Count; i++)
        {
            CapturedRecipeEntry capturedEntry = capturedTree.recipes[i];
            ItemDefinition targetDefinition = ResolveDefinition(capturedEntry.item, lookup);
            if (targetDefinition == null)
            {
                Debug.LogWarning("CraftingTreeItemIdRemapper: skipped recipe because its target item could not be resolved after reorder.");
                continue;
            }

            BinaryRecipeEntry entry = new BinaryRecipeEntry
            {
                itemId = targetDefinition.id,
                outputCount = Mathf.Max(1, capturedEntry.outputCount)
            };

            HashSet<int> seenMapObjectIds = new HashSet<int>();
            for (int mapObjectIndex = 0; mapObjectIndex < capturedEntry.mapObjects.Count; mapObjectIndex++)
            {
                int mapObjectItemId = ResolveMapObjectItemId(capturedEntry.mapObjects[mapObjectIndex], lookup);
                if (mapObjectItemId >= 0 && seenMapObjectIds.Add(mapObjectItemId))
                {
                    entry.requiredMapObjectItemIds.Add(mapObjectItemId);
                }
            }

            for (int ingredientIndex = 0; ingredientIndex < capturedEntry.ingredients.Count; ingredientIndex++)
            {
                CapturedIngredientEntry capturedIngredient = capturedEntry.ingredients[ingredientIndex];
                ItemDefinition ingredientDefinition = ResolveDefinition(capturedIngredient.item, lookup);
                if (ingredientDefinition == null)
                {
                    Debug.LogWarning("CraftingTreeItemIdRemapper: skipped ingredient because it could not be resolved after reorder.");
                    continue;
                }

                entry.ingredients.Add(new BinaryIngredientEntry
                {
                    itemId = ingredientDefinition.id,
                    count = Mathf.Max(1, capturedIngredient.count)
                });
            }

            entries.Add(entry);
        }

        entries.Sort((left, right) => CompareRecipeEntriesByDefinitionOrder(left, right, lookup.definitions));
        return entries;
    }

    private static int CompareRecipeEntriesByDefinitionOrder(
        BinaryRecipeEntry left,
        BinaryRecipeEntry right,
        IReadOnlyList<ItemDefinition> definitions)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int leftOrder = FindDefinitionOrderById(definitions, left.itemId);
        int rightOrder = FindDefinitionOrderById(definitions, right.itemId);
        if (leftOrder != rightOrder)
        {
            return leftOrder.CompareTo(rightOrder);
        }

        return left.itemId.CompareTo(right.itemId);
    }

    private static int FindDefinitionOrderById(IReadOnlyList<ItemDefinition> definitions, int itemId)
    {
        if (definitions == null)
        {
            return int.MaxValue;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int ResolveMapObjectItemId(CapturedMapObjectEntry capturedMapObject, DefinitionLookup lookup)
    {
        if (capturedMapObject == null)
        {
            return -1;
        }

        ItemDefinition definition = ResolveDefinition(capturedMapObject.item, lookup);
        if (definition != null)
        {
            return definition.id;
        }

        MapObject mapObject = ResolveMapObject(capturedMapObject);
        if (mapObject != null)
        {
            int runtimeId = mapObject.ResolveItemId();
            if (runtimeId >= 0)
            {
                return runtimeId;
            }

            definition = FindDefinitionByMapObject(lookup.definitions, mapObject);
            if (definition != null)
            {
                return definition.id;
            }
        }

        return -1;
    }

    private static MapObject ResolveMapObject(CapturedMapObjectEntry capturedMapObject)
    {
        if (capturedMapObject == null || string.IsNullOrWhiteSpace(capturedMapObject.assetPath))
        {
            return null;
        }

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(capturedMapObject.assetPath);
        if (prefabRoot == null)
        {
            return null;
        }

        MapObject mapObject = prefabRoot.GetComponent<MapObject>();
        if (mapObject == null)
        {
            mapObject = prefabRoot.GetComponentInChildren<MapObject>(true);
        }

        return mapObject;
    }

    private static void WriteJsonFile(string path, List<BinaryRecipeEntry> entries, IReadOnlyList<ItemDefinition> definitions)
    {
        CraftingTreeJsonFile file = new CraftingTreeJsonFile();
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                CraftingTreeJsonEntry entry = BuildJsonEntry(entries[i], definitions);
                file.recipes.Add(entry);
                file.items.Add(entry);
                file.entries.Add(entry);
            }
        }

        EnsureParentFolder(path);
        File.WriteAllText(path, JsonUtility.ToJson(file, true));
    }

    private static CraftingTreeJsonEntry BuildJsonEntry(BinaryRecipeEntry sourceEntry, IReadOnlyList<ItemDefinition> definitions)
    {
        ItemDefinition targetDefinition = FindDefinitionById(definitions, sourceEntry.itemId);
        CraftingTreeJsonEntry entry = new CraftingTreeJsonEntry
        {
            itemId = sourceEntry.itemId,
            itemName = GetDefinitionDisplayName(targetDefinition),
            definitionAssetPath = GetDefinitionAssetPath(targetDefinition),
            outputCount = Mathf.Max(1, sourceEntry.outputCount)
        };

        for (int mapObjectIndex = 0; mapObjectIndex < sourceEntry.requiredMapObjectItemIds.Count; mapObjectIndex++)
        {
            CraftingMapObjectJsonEntry mapObjectEntry = BuildMapObjectJsonEntry(sourceEntry.requiredMapObjectItemIds[mapObjectIndex], definitions);
            entry.craftingMapObjects.Add(mapObjectEntry);
            entry.requiredMapObjects.Add(mapObjectEntry);
        }

        for (int ingredientIndex = 0; ingredientIndex < sourceEntry.ingredients.Count; ingredientIndex++)
        {
            BinaryIngredientEntry ingredient = sourceEntry.ingredients[ingredientIndex];
            ItemDefinition ingredientDefinition = FindDefinitionById(definitions, ingredient.itemId);
            entry.ingredients.Add(new CraftingIngredientJsonEntry
            {
                itemId = ingredient.itemId,
                itemName = GetDefinitionDisplayName(ingredientDefinition),
                definitionAssetPath = GetDefinitionAssetPath(ingredientDefinition),
                count = Mathf.Max(1, ingredient.count)
            });
        }

        return entry;
    }

    private static CraftingMapObjectJsonEntry BuildMapObjectJsonEntry(int itemId, IReadOnlyList<ItemDefinition> definitions)
    {
        ItemDefinition definition = FindDefinitionById(definitions, itemId);
        MapObject mapObject = definition != null ? definition.mapObject : null;
        GameObject prefabRoot = null;
        if (mapObject != null)
        {
            prefabRoot = mapObject.transform.root != null ? mapObject.transform.root.gameObject : mapObject.gameObject;
        }

        return new CraftingMapObjectJsonEntry
        {
            itemId = itemId,
            mapObjectName = prefabRoot != null ? prefabRoot.name : GetDefinitionDisplayName(definition),
            assetPath = prefabRoot != null ? AssetDatabase.GetAssetPath(prefabRoot) : string.Empty
        };
    }

    private static Dictionary<int, DefinitionIdentity> BuildDefinitionIdentitiesById(IReadOnlyList<ItemDefinition> definitions)
    {
        Dictionary<int, DefinitionIdentity> identitiesById = new Dictionary<int, DefinitionIdentity>();
        if (definitions == null)
        {
            return identitiesById;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || identitiesById.ContainsKey(definition.id))
            {
                continue;
            }

            string assetPath = GetDefinitionAssetPath(definition);
            identitiesById.Add(definition.id, new DefinitionIdentity
            {
                itemId = definition.id,
                guid = GetGuid(assetPath),
                assetPath = assetPath,
                itemName = GetDefinitionDisplayName(definition),
                persistenceName = ItemDefinitionLookup.GetPersistenceName(definition, definitions)
            });
        }

        return identitiesById;
    }

    private static DefinitionLookup BuildDefinitionLookup(IReadOnlyList<ItemDefinition> definitions)
    {
        DefinitionLookup lookup = new DefinitionLookup(definitions);
        if (definitions == null)
        {
            return lookup;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            string assetPath = GetDefinitionAssetPath(definition);
            string guid = GetGuid(assetPath);
            AddLookupValue(lookup.byGuid, guid, definition);
            AddLookupValue(lookup.byPath, assetPath, definition);
            AddLookupValue(lookup.byName, GetDefinitionDisplayName(definition), definition);
            AddLookupValue(lookup.byName, definition.itemName, definition);
            AddLookupValue(lookup.byName, definition.name, definition);
        }

        return lookup;
    }

    private static DefinitionReference BuildDefinitionReference(int itemId, Dictionary<int, DefinitionIdentity> identitiesById)
    {
        if (identitiesById != null && identitiesById.TryGetValue(itemId, out DefinitionIdentity identity))
        {
            return BuildDefinitionReference(identity);
        }

        return new DefinitionReference
        {
            oldItemId = itemId
        };
    }

    private static DefinitionReference BuildDefinitionReference(
        string assetPath,
        string itemName,
        int itemId,
        Dictionary<int, DefinitionIdentity> identitiesById)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            return new DefinitionReference
            {
                oldItemId = itemId,
                guid = GetGuid(normalizedPath),
                assetPath = normalizedPath,
                itemName = itemName ?? string.Empty
            };
        }

        DefinitionReference reference = BuildDefinitionReference(itemId, identitiesById);
        if (reference.HasStableIdentity)
        {
            return reference;
        }

        reference.itemName = itemName ?? string.Empty;
        return reference;
    }

    private static DefinitionReference BuildDefinitionReference(DefinitionIdentity identity)
    {
        if (identity == null)
        {
            return new DefinitionReference();
        }

        return new DefinitionReference
        {
            oldItemId = identity.itemId,
            guid = identity.guid ?? string.Empty,
            assetPath = identity.assetPath ?? string.Empty,
            itemName = identity.itemName ?? string.Empty
        };
    }

    private static ItemDefinition ResolveDefinition(DefinitionReference reference, DefinitionLookup lookup)
    {
        if (reference == null || lookup == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(reference.guid) && lookup.byGuid.TryGetValue(reference.guid, out ItemDefinition guidMatch))
        {
            return guidMatch;
        }

        if (!string.IsNullOrWhiteSpace(reference.assetPath) && lookup.byPath.TryGetValue(reference.assetPath, out ItemDefinition pathMatch))
        {
            return pathMatch;
        }

        if (!string.IsNullOrWhiteSpace(reference.itemName) && lookup.byName.TryGetValue(reference.itemName, out ItemDefinition nameMatch))
        {
            return nameMatch;
        }

        if (reference.HasStableIdentity)
        {
            return null;
        }

        return reference.oldItemId >= 0 ? FindDefinitionById(lookup.definitions, reference.oldItemId) : null;
    }

    private static ItemDefinition FindDefinitionById(IReadOnlyList<ItemDefinition> definitions, int id)
    {
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == id)
            {
                return definition;
            }
        }

        return null;
    }

    private static ItemDefinition FindDefinitionByMapObject(IReadOnlyList<ItemDefinition> definitions, MapObject mapObject)
    {
        if (definitions == null || mapObject == null)
        {
            return null;
        }

        GameObject mapObjectRoot = mapObject.transform.root != null ? mapObject.transform.root.gameObject : mapObject.gameObject;
        string mapObjectPath = AssetDatabase.GetAssetPath(mapObjectRoot);
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.mapObject == null)
            {
                continue;
            }

            GameObject definitionRoot = definition.mapObject.transform.root != null
                ? definition.mapObject.transform.root.gameObject
                : definition.mapObject.gameObject;
            if (definitionRoot == mapObjectRoot || AssetDatabase.GetAssetPath(definitionRoot) == mapObjectPath)
            {
                return definition;
            }
        }

        return null;
    }

    private static List<CraftingTreeJsonEntry> GetJsonEntries(CraftingTreeJsonFile file)
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

    private static List<CraftingMapObjectJsonEntry> GetJsonMapObjectEntries(CraftingTreeJsonEntry entry)
    {
        if (entry == null)
        {
            return new List<CraftingMapObjectJsonEntry>();
        }

        if (entry.craftingMapObjects != null && entry.craftingMapObjects.Count > 0)
        {
            return entry.craftingMapObjects;
        }

        if (entry.requiredMapObjects != null && entry.requiredMapObjects.Count > 0)
        {
            return entry.requiredMapObjects;
        }

        return new List<CraftingMapObjectJsonEntry>();
    }

    private static void AddLookupValue(Dictionary<string, ItemDefinition> lookup, string key, ItemDefinition definition)
    {
        if (lookup == null || definition == null || string.IsNullOrWhiteSpace(key) || lookup.ContainsKey(key))
        {
            return;
        }

        lookup.Add(key.Trim(), definition);
    }

    private static string GetDefinitionAssetPath(ItemDefinition definition)
    {
        return definition != null ? NormalizeAssetPath(AssetDatabase.GetAssetPath(definition)) : string.Empty;
    }

    private static string GetDefinitionDisplayName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName.Trim();
    }

    private static string GetGuid(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : assetPath.Trim().Replace("\\", "/");
    }

    private static string GetCraftingTreeAssetPath()
    {
        return Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.bytes");
    }

    private static string GetCraftingTreeResourcesPath()
    {
        return Path.Combine(Application.dataPath, "Resources", "Data", "CraftingTree", "crafting_tree.bytes");
    }

    private static string GetCraftingTreeJsonPath()
    {
        return Path.Combine(Application.dataPath, "Data", "CraftingTree", "crafting_tree.json");
    }

    private static bool TryReadCurrentBinaryFile(
        string path,
        Dictionary<int, DefinitionIdentity> identitiesById,
        out List<BinaryRecipeEntry> entries)
    {
        entries = new List<BinaryRecipeEntry>();
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int version = reader.ReadInt32();
                if (version != ItemIdCraftingTreeFileVersion
                    && version != ItemNameCraftingTreeFileVersion)
                {
                    Debug.LogWarning($"CraftingTreeItemIdRemapper: unsupported crafting tree version {version} at '{path}'.");
                    return false;
                }

                int recipeCount = Mathf.Max(0, reader.ReadInt32());
                for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
                {
                    BinaryRecipeEntry entry = new BinaryRecipeEntry
                    {
                        itemId = ReadItemId(reader, version, identitiesById)
                    };

                    int mapObjectCount = Mathf.Max(0, reader.ReadInt32());
                    for (int mapObjectIndex = 0; mapObjectIndex < mapObjectCount; mapObjectIndex++)
                    {
                        int mapObjectItemId = ReadItemId(reader, version, identitiesById);
                        if (mapObjectItemId >= 0)
                        {
                            entry.requiredMapObjectItemIds.Add(mapObjectItemId);
                        }
                    }

                    entry.outputCount = Mathf.Max(1, reader.ReadInt32());

                    int ingredientCount = Mathf.Max(0, reader.ReadInt32());
                    for (int ingredientIndex = 0; ingredientIndex < ingredientCount; ingredientIndex++)
                    {
                        entry.ingredients.Add(new BinaryIngredientEntry
                        {
                            itemId = ReadItemId(reader, version, identitiesById),
                            count = Mathf.Max(1, reader.ReadInt32())
                        });
                    }

                    entries.Add(entry);
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"CraftingTreeItemIdRemapper: failed to read '{path}'. {exception.Message}");
            entries.Clear();
            return false;
        }
    }

    private static int ReadItemId(
        BinaryReader reader,
        int version,
        Dictionary<int, DefinitionIdentity> identitiesById)
    {
        if (version < ItemNameCraftingTreeFileVersion)
        {
            return reader.ReadInt32();
        }

        string itemName = reader.ReadString();
        if (identitiesById != null && !string.IsNullOrWhiteSpace(itemName))
        {
            foreach (KeyValuePair<int, DefinitionIdentity> pair in identitiesById)
            {
                DefinitionIdentity identity = pair.Value;
                if (identity != null
                    && string.Equals(identity.persistenceName, itemName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return identity.itemId;
                }
            }
        }

        Debug.LogWarning($"CraftingTreeItemIdRemapper: unresolved item name '{itemName}'.");
        return -1;
    }

    private static void WriteCurrentBinaryFile(
        string path,
        List<BinaryRecipeEntry> entries,
        IReadOnlyList<ItemDefinition> definitions)
    {
        EnsureParentFolder(path);
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(CurrentCraftingTreeFileVersion);
            writer.Write(entries != null ? entries.Count : 0);
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                BinaryRecipeEntry entry = entries[i];
                writer.Write(GetRequiredItemName(entry.itemId, definitions));
                writer.Write(entry.requiredMapObjectItemIds.Count);
                for (int mapObjectIndex = 0; mapObjectIndex < entry.requiredMapObjectItemIds.Count; mapObjectIndex++)
                {
                    writer.Write(GetRequiredItemName(entry.requiredMapObjectItemIds[mapObjectIndex], definitions));
                }

                writer.Write(Mathf.Max(1, entry.outputCount));
                writer.Write(entry.ingredients.Count);
                for (int ingredientIndex = 0; ingredientIndex < entry.ingredients.Count; ingredientIndex++)
                {
                    BinaryIngredientEntry ingredient = entry.ingredients[ingredientIndex];
                    writer.Write(GetRequiredItemName(ingredient.itemId, definitions));
                    writer.Write(Mathf.Max(1, ingredient.count));
                }
            }
        }
    }

    private static string GetRequiredItemName(int itemId, IReadOnlyList<ItemDefinition> definitions)
    {
        ItemDefinition definition = FindDefinitionById(definitions, itemId);
        string itemName = ItemDefinitionLookup.GetPersistenceName(definition, definitions);
        if (definition == null || string.IsNullOrWhiteSpace(itemName))
        {
            throw new InvalidDataException($"CraftingTree item id {itemId}의 이름을 찾지 못했습니다.");
        }

        return itemName.Trim();
    }

    private static void EnsureParentFolder(string path)
    {
        string folderPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folderPath) && !Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
    }
}
