using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ItemManager : MonoBehaviour
{
    [Serializable]
    public struct ItemSet
    {
        public string name;
        public int id;
        public PropObj prefab;
        public Mesh portableMesh;
        public Material portableMat;
        public Sprite icon;
        public int size;
    }

    [SerializeField, HideInInspector]
    private List<ItemSet> items;

    [SerializeField]
    private List<ItemDefinition> itemDefinitions;

#if UNITY_EDITOR
    [SerializeField]
    private bool autoMigrateDefinitions = true;
#endif

    public List<ItemSet> ItemSets => items;
    public List<ItemDefinition> ItemDefinitions => itemDefinitions;

    public bool TryGetItemSetById(int id, out ItemSet itemSet)
    {
        if (itemDefinitions != null && itemDefinitions.Count > 0)
        {
            for (int i = 0; i < itemDefinitions.Count; i++)
            {
                ItemDefinition definition = itemDefinitions[i];
                if (definition != null && definition.id == id)
                {
                    itemSet = new ItemSet
                    {
                        id = definition.id,
                        name = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName,
                        prefab = ResolvePrefabForId(definition.id),
                        portableMesh = definition.portableMesh,
                        portableMat = definition.portableMat,
                        icon = definition.icon,
                        size = (int)definition.size
                    };
                    return true;
                }
            }
        }

        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].id == id)
                {
                    itemSet = items[i];
                    return true;
                }
            }
        }

        itemSet = default;
        return false;
    }

    private PropObj ResolvePrefabForId(int id)
    {
        if (items == null)
        {
            return null;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].id == id)
            {
                return items[i].prefab;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!autoMigrateDefinitions)
        {
            return;
        }

        if (itemDefinitions == null || itemDefinitions.Count == 0)
        {
            MigrateAllDefinitions();
        }
    }

    [ContextMenu("Migrate Item Definitions From Items")]
    public void MigrateItemDefinitionsFromItems()
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        if (itemDefinitions == null)
        {
            itemDefinitions = new List<ItemDefinition>();
        }

        Dictionary<string, string> itemFolderLookup = BuildItemFolderLookup();

        string targetDirectory = "Assets/Data/Items";
        if (!EnsureAssetFolder(targetDirectory))
        {
            Debug.LogError($"ItemManager: Failed to create item definition folder at '{targetDirectory}'.");
            return;
        }

        Dictionary<int, ItemDefinition> existingById = new Dictionary<int, ItemDefinition>();
        for (int i = 0; i < itemDefinitions.Count; i++)
        {
            ItemDefinition definition = itemDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            existingById[definition.id] = definition;
        }

        for (int i = 0; i < items.Count; i++)
        {
            ItemSet itemSet = items[i];
            if (itemSet.id < 0)
            {
                continue;
            }

            if (!existingById.TryGetValue(itemSet.id, out ItemDefinition definition) || definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                string safeName = string.IsNullOrWhiteSpace(itemSet.name) ? $"Item_{itemSet.id}" : itemSet.name;
                safeName = SanitizeAssetFileName(safeName);
                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{targetDirectory}/Item_{itemSet.id}_{safeName}.asset");
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    Debug.LogError($"ItemManager: Failed to generate asset path for item '{itemSet.name}' (id {itemSet.id}).");
                    continue;
                }

                AssetDatabase.CreateAsset(definition, assetPath);
                itemDefinitions.Add(definition);
                existingById[itemSet.id] = definition;
            }

            definition.id = itemSet.id;
            definition.itemName = itemSet.name;
            definition.mapObject = FindMapObjectForItem(itemSet.name, GetItemFolderForName(itemSet.name, itemFolderLookup));

            Mesh definitionPortableMesh = itemSet.portableMesh;
            Material definitionPortableMaterial = itemSet.portableMat;
            TryOverrideInstallationPortableAssets(
                itemSet.name,
                GetItemFolderForName(itemSet.name, itemFolderLookup),
                GetMapObjectPrefabRoot(definition.mapObject),
                ref definitionPortableMesh,
                ref definitionPortableMaterial);
            definition.portableMesh = definitionPortableMesh;
            definition.portableMat = definitionPortableMaterial;
            definition.icon = itemSet.icon;
            definition.size = (uint)Mathf.Max(0, itemSet.size);
            BindMapObjectDefinition(definition);
            TryBindItemDefinitionToPrefab(itemSet.prefab, definition);

            EditorUtility.SetDirty(definition);
        }

        SortItemDefinitionsById(itemDefinitions);
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [ContextMenu("Rebuild Items From Assets (Definitions)")]
    public void RebuildItemDefinitionsFromAssets()
    {
        if (itemDefinitions == null)
        {
            itemDefinitions = new List<ItemDefinition>();
        }

        if (items == null)
        {
            items = new List<ItemSet>();
        }

        Dictionary<string, ItemSet> previousItemsByName = new Dictionary<string, ItemSet>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            ItemSet existingItem = items[i];
            if (!string.IsNullOrWhiteSpace(existingItem.name))
            {
                previousItemsByName[existingItem.name] = existingItem;
            }
        }

        List<string> itemFolders = CollectItemFolderPaths();
        Dictionary<string, int> preservedIdsByItemName = BuildPreservedItemIds(itemFolders, previousItemsByName);
        Dictionary<string, ItemDefinition> existingDefinitionsByName = BuildExistingItemDefinitionLookupByName();
        HashSet<int> usedIds = new HashSet<int>(preservedIdsByItemName.Values);

        List<ItemSet> rebuiltItems = new List<ItemSet>();

        for (int i = 0; i < itemFolders.Count; i++)
        {
            string itemFolder = itemFolders[i];
            string itemName = ResolveItemName(itemFolder, Path.GetFileName(itemFolder));
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            bool hasPreviousItem = previousItemsByName.TryGetValue(itemName, out ItemSet previousItem);
            PropObj propObject = FindPropObjInFolder(itemFolder, out GameObject prefabRoot);

            ResolvePortableAssets(itemFolder, prefabRoot, out Mesh portableMesh, out Material portableMaterial);

            TryOverrideInstallationPortableAssets(itemName, itemFolder, prefabRoot, ref portableMesh, ref portableMaterial);

            int itemId = preservedIdsByItemName.TryGetValue(itemName, out int preservedId)
                ? preservedId
                : GetNextAvailableId(usedIds);
            usedIds.Add(itemId);

            Sprite resolvedIcon = ResolveItemIcon(itemFolder, itemName, hasPreviousItem ? previousItem.icon : null);

            ItemSet itemSet = new ItemSet
            {
                id = itemId,
                name = itemName,
                prefab = propObject,
                portableMesh = portableMesh,
                portableMat = portableMaterial,
                icon = resolvedIcon,
                size = ResolvePreservedItemSize(itemName, hasPreviousItem ? previousItem.size : 0, existingDefinitionsByName)
            };

            rebuiltItems.Add(itemSet);
        }

        rebuiltItems.Sort((left, right) =>
        {
            int idCompare = left.id.CompareTo(right.id);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        });

        items = rebuiltItems;
        RecreateItemDefinitionsFromItems(rebuiltItems);
        SortItemDefinitionsById(itemDefinitions);
        MigrateResourceDefinitionsFromResources();
        SyncTerrainGeneratorResourceDefinitions();
        EditorUtility.SetDirty(this);
    }

    [ContextMenu("Migrate Item + Resource Definitions")]
    public void MigrateAllDefinitions()
    {
        MigrateItemDefinitionsFromItems();
        MigrateResourceDefinitionsFromResources();
        SyncTerrainGeneratorResourceDefinitions();
    }

    public void RebuildItemsFromAssets()
    {
        if (items == null)
        {
            items = new List<ItemSet>();
        }

        Dictionary<string, ItemSet> previousItemsByName = new Dictionary<string, ItemSet>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            ItemSet existingItem = items[i];
            if (!string.IsNullOrWhiteSpace(existingItem.name))
            {
                previousItemsByName[existingItem.name] = existingItem;
            }
        }

        List<string> itemFolders = CollectItemFolderPaths();
        Dictionary<string, int> preservedIdsByItemName = BuildPreservedItemIds(itemFolders, previousItemsByName);
        Dictionary<string, ItemDefinition> existingDefinitionsByName = BuildExistingItemDefinitionLookupByName();
        HashSet<int> usedIds = new HashSet<int>(preservedIdsByItemName.Values);

        List<ItemSet> rebuiltItems = new List<ItemSet>();

        for (int i = 0; i < itemFolders.Count; i++)
        {
            string itemFolder = itemFolders[i];
            string itemName = ResolveItemName(itemFolder, Path.GetFileName(itemFolder));
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            bool hasPreviousItem = previousItemsByName.TryGetValue(itemName, out ItemSet previousItem);
            PropObj propObject = FindPropObjInFolder(itemFolder, out GameObject prefabRoot);

            ResolvePortableAssets(itemFolder, prefabRoot, out Mesh portableMesh, out Material portableMaterial);

            TryOverrideInstallationPortableAssets(itemName, itemFolder, prefabRoot, ref portableMesh, ref portableMaterial);

            int itemId = preservedIdsByItemName.TryGetValue(itemName, out int preservedId)
                ? preservedId
                : GetNextAvailableId(usedIds);
            usedIds.Add(itemId);

            Sprite resolvedIcon = ResolveItemIcon(itemFolder, itemName, hasPreviousItem ? previousItem.icon : null);

            ItemSet itemSet = new ItemSet
            {
                id = itemId,
                name = itemName,
                prefab = propObject,
                portableMesh = portableMesh,
                portableMat = portableMaterial,
                icon = resolvedIcon,
                size = ResolvePreservedItemSize(itemName, hasPreviousItem ? previousItem.size : 0, existingDefinitionsByName)
            };

            rebuiltItems.Add(itemSet);
        }

        rebuiltItems.Sort((left, right) =>
        {
            int idCompare = left.id.CompareTo(right.id);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        });

        items = rebuiltItems;
        RecreateItemDefinitionsFromItems(rebuiltItems);
        SortItemDefinitionsById(itemDefinitions);
        MigrateResourceDefinitionsFromResources();
        SyncTerrainGeneratorResourceDefinitions();
        EditorUtility.SetDirty(this);
    }

    private static int GetNextAvailableId(HashSet<int> usedIds)
    {
        int candidateId = 0;
        while (usedIds.Contains(candidateId))
        {
            candidateId++;
        }

        return candidateId;
    }

    private static Dictionary<string, int> BuildPreservedItemIds(List<string> itemFolders, Dictionary<string, ItemSet> previousItemsByName)
    {
        Dictionary<string, int> results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (itemFolders == null || previousItemsByName == null || previousItemsByName.Count == 0)
        {
            return results;
        }

        HashSet<int> usedIds = new HashSet<int>();
        for (int i = 0; i < itemFolders.Count; i++)
        {
            string itemFolder = itemFolders[i];
            string itemName = ResolveItemName(itemFolder, Path.GetFileName(itemFolder));
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            if (!previousItemsByName.TryGetValue(itemName, out ItemSet previousItem))
            {
                continue;
            }

            if (previousItem.id < 0 || usedIds.Contains(previousItem.id))
            {
                continue;
            }

            usedIds.Add(previousItem.id);
            results[itemName] = previousItem.id;
        }

        return results;
    }

    private static string ResolveItemName(string assetPath, string prefabName)
    {
        string folderName = GetItemFolderName(assetPath);
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            return folderName;
        }

        return string.IsNullOrWhiteSpace(prefabName) ? "Item" : prefabName;
    }

    private static string GetItemFolderName(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return string.Empty;
        }

        string normalizedPath = assetPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(normalizedPath))
        {
            return Path.GetFileName(normalizedPath);
        }

        string directory = Path.GetDirectoryName(normalizedPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        string[] parts = directory.Split('/');
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        string lastFolder = parts[parts.Length - 1];
        if (string.IsNullOrWhiteSpace(lastFolder))
        {
            return string.Empty;
        }

        if (lastFolder.Equals("Meshes", StringComparison.OrdinalIgnoreCase)
            || lastFolder.Equals("Materials", StringComparison.OrdinalIgnoreCase)
            || lastFolder.Equals("Prefabs", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length >= 2)
            {
                return parts[parts.Length - 2];
            }
        }

        return lastFolder;
    }

    private static string[] GetItemFolderSearchRoots()
    {
        List<string> folders = new List<string>();
        AddSearchFolderIfExists(folders, "Assets/Items");

        if (folders.Count == 0)
        {
            return new[] { "Assets/Items" };
        }

        return folders.ToArray();
    }

    private static List<string> CollectItemFolderPaths()
    {
        string[] searchRoots = GetItemFolderSearchRoots();
        HashSet<string> results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < searchRoots.Length; i++)
        {
            string root = searchRoots[i];
            if (string.IsNullOrWhiteSpace(root) || !AssetDatabase.IsValidFolder(root))
            {
                continue;
            }

            string[] categoryFolders = AssetDatabase.GetSubFolders(root);
            for (int categoryIndex = 0; categoryIndex < categoryFolders.Length; categoryIndex++)
            {
                string categoryFolder = categoryFolders[categoryIndex];
                string[] itemFolders = AssetDatabase.GetSubFolders(categoryFolder);
                if (itemFolders.Length == 0)
                {
                    results.Add(categoryFolder);
                    continue;
                }

                for (int itemIndex = 0; itemIndex < itemFolders.Length; itemIndex++)
                {
                    results.Add(itemFolders[itemIndex]);
                }
            }
        }

        List<string> sorted = new List<string>(results);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    private static Dictionary<string, string> BuildItemFolderLookup()
    {
        Dictionary<string, string> lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<string> itemFolders = CollectItemFolderPaths();
        for (int i = 0; i < itemFolders.Count; i++)
        {
            string itemFolder = itemFolders[i];
            if (string.IsNullOrWhiteSpace(itemFolder))
            {
                continue;
            }

            string folderName = Path.GetFileName(itemFolder);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            if (!lookup.ContainsKey(folderName))
            {
                lookup[folderName] = itemFolder;
            }
        }

        return lookup;
    }

    private static string GetItemFolderForName(string itemName, Dictionary<string, string> itemFolderLookup)
    {
        if (string.IsNullOrWhiteSpace(itemName) || itemFolderLookup == null)
        {
            return string.Empty;
        }

        return itemFolderLookup.TryGetValue(itemName, out string folderPath) ? folderPath : string.Empty;
    }

    private static void AddSearchFolderIfExists(List<string> folders, string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            folders.Add(path);
        }
    }

    private static bool IsPortableOnlyCandidate(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        string normalized = assetPath.Replace("\\", "/").ToLowerInvariant();
        return normalized.Contains("/objects/equip/")
               || normalized.Contains("/objects/equips/")
               || normalized.Contains("/object/equip/")
               || normalized.Contains("/equips/");
    }

    private static bool IsGeneratedItemWrapper(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        string normalized = assetPath.Replace("\\", "/").ToLowerInvariant();
        return normalized.Contains("/objects/generateditems/");
    }

    private static string[] GetResourcePrefabSearchRoots()
    {
        List<string> folders = new List<string>();
        AddSearchFolderIfExists(folders, "Assets/MapResource");
        AddSearchFolderIfExists(folders, "Assets/MapResources");
        AddSearchFolderIfExists(folders, "Assets/MapObject");
        AddSearchFolderIfExists(folders, "Assets/MapObjects");
        return folders.ToArray();
    }

    private static string[] GetResourceDefinitionSearchRoots()
    {
        List<string> folders = new List<string>();
        AddSearchFolderIfExists(folders, "Assets/Data/Resources");
        AddSearchFolderIfExists(folders, "Assets/Data/MapObject");
        AddSearchFolderIfExists(folders, "Assets/Data/MapObjects");
        return folders.ToArray();
    }

    private static bool HasResourceDefinitionAssets(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            return false;
        }

        string[] guids = AssetDatabase.FindAssets("t:ResourceDefinition", new[] { folderPath });
        return guids != null && guids.Length > 0;
    }

    private static string GetResourceDefinitionTargetDirectory()
    {
        if (HasResourceDefinitionAssets("Assets/Data/Resources"))
        {
            return "Assets/Data/Resources";
        }

        if (HasResourceDefinitionAssets("Assets/Data/MapObject"))
        {
            return "Assets/Data/MapObject";
        }

        if (HasResourceDefinitionAssets("Assets/Data/MapObjects"))
        {
            return "Assets/Data/MapObjects";
        }

        if (AssetDatabase.IsValidFolder("Assets/Data/MapObject"))
        {
            return "Assets/Data/MapObject";
        }

        return "Assets/Data/Resources";
    }

    private static string GetItemCategoryName(string itemFolder)
    {
        if (string.IsNullOrWhiteSpace(itemFolder))
        {
            return string.Empty;
        }

        string normalized = itemFolder.Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(normalized))
        {
            return string.Empty;
        }

        string parent = Path.GetDirectoryName(normalized)?.Replace("\\", "/");
        return string.IsNullOrWhiteSpace(parent) ? string.Empty : Path.GetFileName(parent);
    }

    private static MapObject FindMapObjectForItem(string itemName, string itemFolder)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        List<string> searchFolders = new List<string>();
        string categoryName = GetItemCategoryName(itemFolder);
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            AddSearchFolderIfExists(searchFolders, $"Assets/MapObject/{categoryName}");
            AddSearchFolderIfExists(searchFolders, $"Assets/MapObjects/{categoryName}");
        }

        if (searchFolders.Count == 0)
        {
            AddSearchFolderIfExists(searchFolders, "Assets/MapObject");
            AddSearchFolderIfExists(searchFolders, "Assets/MapObjects");
        }

        if (searchFolders.Count == 0)
        {
            return null;
        }

        string itemKey = NormalizeItemLookupName(itemName);
        MapObject folderMatchedFallback = null;

        for (int folderIndex = 0; folderIndex < searchFolders.Count; folderIndex++)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { searchFolders[folderIndex] });
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (string.IsNullOrWhiteSpace(prefabPath))
                {
                    continue;
                }

                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                string prefabName = prefabRoot != null ? prefabRoot.name : Path.GetFileNameWithoutExtension(prefabPath);
                bool prefabNameMatches = IsMapObjectPrefabNameMatch(itemName, itemKey, prefabName);
                bool folderNameMatches = IsMapObjectPrefabFolderMatch(itemName, itemKey, prefabPath);
                if (!prefabNameMatches && !folderNameMatches)
                {
                    continue;
                }

                MapObject mapObject = FindPreferredMapObjectOnPrefab(prefabRoot, prefabPath);

                if (mapObject == null)
                {
                    continue;
                }

                if (prefabNameMatches)
                {
                    return mapObject;
                }

                folderMatchedFallback ??= mapObject;
            }
        }

        return folderMatchedFallback;
    }

    private static bool IsMapObjectPrefabNameMatch(string itemName, string itemKey, string prefabName)
    {
        if (string.Equals(prefabName, itemName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(itemKey))
        {
            string normalizedPrefab = NormalizeItemLookupName(prefabName);
            if (normalizedPrefab == itemKey)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMapObjectPrefabFolderMatch(string itemName, string itemKey, string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            return false;
        }

        string folderName = Path.GetFileName(Path.GetDirectoryName(prefabPath)?.Replace("\\", "/") ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            if (string.Equals(folderName, itemName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(itemKey))
            {
                string normalizedFolder = NormalizeItemLookupName(folderName);
                if (normalizedFolder == itemKey)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MapObject FindPreferredMapObjectOnPrefab(GameObject prefabRoot, string prefabPath = null)
    {
        MapObject directMatch = FindPreferredMapObjectOnGameObject(prefabRoot);
        if (directMatch != null)
        {
            return directMatch;
        }

        if (string.IsNullOrWhiteSpace(prefabPath) && prefabRoot != null)
        {
            prefabPath = AssetDatabase.GetAssetPath(prefabRoot);
        }

        return FindPreferredMapObjectInAsset(prefabPath);
    }

    private static MapObject FindPreferredMapObjectOnGameObject(GameObject prefabRoot)
    {
        if (prefabRoot == null)
        {
            return null;
        }

        Resource resource = prefabRoot.GetComponent<Resource>();
        if (resource == null)
        {
            resource = prefabRoot.GetComponentInChildren<Resource>(true);
        }

        if (resource != null)
        {
            return resource;
        }

        InstallationObject installationObject = prefabRoot.GetComponent<InstallationObject>();
        if (installationObject == null)
        {
            installationObject = prefabRoot.GetComponentInChildren<InstallationObject>(true);
        }

        if (installationObject != null)
        {
            return installationObject;
        }

        MapObject mapObject = prefabRoot.GetComponent<MapObject>();
        if (mapObject == null)
        {
            mapObject = prefabRoot.GetComponentInChildren<MapObject>(true);
        }

        return mapObject;
    }

    private static MapObject FindPreferredMapObjectInAsset(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            return null;
        }

        Resource directResource = AssetDatabase.LoadAssetAtPath<Resource>(prefabPath);
        if (directResource != null)
        {
            return directResource;
        }

        InstallationObject directInstallationObject = AssetDatabase.LoadAssetAtPath<InstallationObject>(prefabPath);
        if (directInstallationObject != null)
        {
            return directInstallationObject;
        }

        MapObject directMapObject = AssetDatabase.LoadAssetAtPath<MapObject>(prefabPath);
        if (directMapObject != null)
        {
            return directMapObject;
        }

        UnityEngine.Object[] assetObjects = AssetDatabase.LoadAllAssetsAtPath(prefabPath);
        if (assetObjects == null || assetObjects.Length == 0)
        {
            return null;
        }

        Resource resource = null;
        InstallationObject installationObject = null;
        MapObject mapObject = null;

        for (int i = 0; i < assetObjects.Length; i++)
        {
            switch (assetObjects[i])
            {
                case Resource foundResource:
                    resource ??= foundResource;
                    break;
                case InstallationObject foundInstallationObject:
                    installationObject ??= foundInstallationObject;
                    break;
                case MapObject foundMapObject:
                    mapObject ??= foundMapObject;
                    break;
            }
        }

        if (resource != null)
        {
            return resource;
        }

        if (installationObject != null)
        {
            return installationObject;
        }

        if (mapObject != null)
        {
            return mapObject;
        }

        for (int i = 0; i < assetObjects.Length; i++)
        {
            if (assetObjects[i] is GameObject assetGameObject)
            {
                MapObject nestedMatch = FindPreferredMapObjectOnGameObject(assetGameObject);
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }
        }

        return FindPreferredMapObjectInPrefabContents(prefabPath);
    }

    private static MapObject FindPreferredMapObjectInPrefabContents(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            return null;
        }

        GameObject prefabContentsRoot = null;
        try
        {
            prefabContentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            MapObject instanceMatch = FindPreferredMapObjectOnGameObject(prefabContentsRoot);
            if (instanceMatch == null)
            {
                return null;
            }

            MapObject sourceMatch = PrefabUtility.GetCorrespondingObjectFromSource(instanceMatch);
            return sourceMatch != null ? sourceMatch : instanceMatch;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ItemManager: Failed to inspect prefab contents for '{prefabPath}'. {ex.Message}");
            return null;
        }
        finally
        {
            if (prefabContentsRoot != null)
            {
                PrefabUtility.UnloadPrefabContents(prefabContentsRoot);
            }
        }
    }

    private static string ResolveLookupPath(PropObj propObject, GameObject prefabRoot, string assetPath)
    {
        if (propObject != null)
        {
            string propPath = AssetDatabase.GetAssetPath(propObject);
            if (!string.IsNullOrWhiteSpace(propPath))
            {
                return propPath;
            }
        }

        if (prefabRoot != null)
        {
            MeshFilter meshFilter = prefabRoot.GetComponentInChildren<MeshFilter>(true);
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                string meshPath = AssetDatabase.GetAssetPath(meshFilter.sharedMesh);
                if (!string.IsNullOrWhiteSpace(meshPath))
                {
                    return meshPath;
                }
            }
        }

        return assetPath;
    }

    private static PropObj FindPropObjOnPrefab(GameObject prefabRoot)
    {
        if (prefabRoot == null)
        {
            return null;
        }

        PropObj propObj = prefabRoot.GetComponent<PropObj>();
        if (propObj != null)
        {
            return propObj;
        }

        return prefabRoot.GetComponentInChildren<PropObj>(true);
    }

    private static PropObj FindPropObjInFolder(string folderPath, out GameObject prefabRoot)
    {
        prefabRoot = null;

        if (string.IsNullOrWhiteSpace(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            return null;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                continue;
            }

            GameObject candidateRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (candidateRoot == null)
            {
                continue;
            }

            PropObj propObj = FindPropObjOnPrefab(candidateRoot);
            if (propObj != null)
            {
                prefabRoot = candidateRoot;
                return propObj;
            }
        }

        return null;
    }

    private static List<string> BuildSearchDirectories(string assetPath)
    {
        List<string> directories = new List<string>();
        string prefabDirectory = ResolveAssetDirectory(assetPath);
        if (!string.IsNullOrWhiteSpace(prefabDirectory))
        {
            directories.Add(prefabDirectory);
            string parentDirectory = Path.GetDirectoryName(prefabDirectory)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(parentDirectory) && !directories.Contains(parentDirectory))
            {
                directories.Add(parentDirectory);
            }
        }

        AddSearchFolderIfExists(directories, "Assets/Items");

        return directories;
    }

    private static string ResolveAssetDirectory(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return string.Empty;
        }

        string normalized = assetPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(normalized))
        {
            return normalized;
        }

        return Path.GetDirectoryName(normalized)?.Replace("\\", "/") ?? string.Empty;
    }

    private static Sprite ResolveItemIcon(string assetPath, string prefabName, Sprite fallbackIcon)
    {
        List<string> searchDirectories = BuildSearchDirectories(assetPath);
        if (searchDirectories.Count == 0)
        {
            return fallbackIcon;
        }

        HashSet<string> candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < searchDirectories.Count; i++)
        {
            string[] spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { searchDirectories[i] });
            for (int j = 0; j < spriteGuids.Length; j++)
            {
                candidatePaths.Add(AssetDatabase.GUIDToAssetPath(spriteGuids[j]));
            }

            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { searchDirectories[i] });
            for (int j = 0; j < textureGuids.Length; j++)
            {
                candidatePaths.Add(AssetDatabase.GUIDToAssetPath(textureGuids[j]));
            }
        }

        if (candidatePaths.Count == 0)
        {
            return fallbackIcon;
        }

        string itemKey = NormalizeItemLookupName(prefabName);
        string prefabDirectory = ResolveAssetDirectory(assetPath);
        string parentDirectory = Path.GetDirectoryName(prefabDirectory)?.Replace("\\", "/") ?? string.Empty;
        string categoryToken = GetCategoryToken(assetPath, prefabName);

        string bestPath = null;
        int bestScore = int.MinValue;

        foreach (string candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            if (!IsCandidateInCategory(candidatePath, categoryToken, prefabDirectory, parentDirectory))
            {
                continue;
            }

            int score = ScoreIconCandidate(candidatePath, itemKey, prefabDirectory, parentDirectory);
            if (score > bestScore)
            {
                bestScore = score;
                bestPath = candidatePath;
            }
        }

        if (string.IsNullOrWhiteSpace(bestPath) || bestScore <= int.MinValue / 2)
        {
            return fallbackIcon;
        }

        Sprite resolvedSprite = LoadOrConvertSprite(bestPath);
        return resolvedSprite != null ? resolvedSprite : fallbackIcon;
    }

    private static int ScoreIconCandidate(string assetPath, string itemKey, string prefabDirectory, string parentDirectory)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);
        string normalizedFileName = NormalizeItemLookupName(fileNameWithoutExtension);
        string lowerFileName = fileNameWithoutExtension.ToLower(CultureInfo.InvariantCulture);
        string normalizedPath = assetPath.Replace("\\", "/");

        int score = 0;
        bool isExplicitIcon = IsExplicitIconCandidate(assetPath);
        if (isExplicitIcon)
        {
            score += 1200;
        }

        if (!string.IsNullOrWhiteSpace(prefabDirectory)
            && normalizedPath.StartsWith(prefabDirectory, StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }
        else if (!string.IsNullOrWhiteSpace(parentDirectory)
                 && normalizedPath.StartsWith(parentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (!string.IsNullOrWhiteSpace(itemKey))
        {
            if (normalizedFileName == itemKey)
            {
                score += 500;
            }
            else if (normalizedFileName.Contains(itemKey))
            {
                score += 250;
            }
        }

        if (lowerFileName.Contains("_tb") || lowerFileName.EndsWith("tb"))
        {
            score += 80;
        }

        return score;
    }

    private static bool IsExplicitIconCandidate(string assetPath)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return false;
        }

        string lowerFileName = fileNameWithoutExtension.ToLower(CultureInfo.InvariantCulture);
        return lowerFileName.Contains("icon");
    }

    private static Sprite LoadOrConvertSprite(string assetPath)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite != null)
        {
            return sprite;
        }

        TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (textureImporter == null)
        {
            return null;
        }

        if (textureImporter.textureType != TextureImporterType.Sprite
            || textureImporter.spriteImportMode != SpriteImportMode.Single)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.spriteImportMode = SpriteImportMode.Single;
            textureImporter.alphaIsTransparency = true;
            textureImporter.mipmapEnabled = false;
            textureImporter.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    private static void TryAutoAssignInteractionButtons(ItemDefinition definition)
    {
        if (definition == null
            || !(definition.mapObject is InstallationObject || definition.mapObject is Resource))
        {
            return;
        }

        string interactionFolderPath = GetMapObjectFolderPath(definition.mapObject);
        if (string.IsNullOrWhiteSpace(interactionFolderPath))
        {
            return;
        }

        bool isFenceDoor = definition.mapObject is FenceDoor;
        List<Sprite> resolvedInteractionSprites = isFenceDoor
            ? FindDoorInteractionButtonSprites(interactionFolderPath)
            : new List<Sprite>();
        if (resolvedInteractionSprites.Count > 0)
        {
            AssignInteractionButtons(definition, resolvedInteractionSprites);
            return;
        }

        if (!ShouldAutoAssignInteractionButtons(definition.interactionButtonList))
        {
            return;
        }

        resolvedInteractionSprites = FindInteractionButtonSprites(interactionFolderPath);
        if (resolvedInteractionSprites.Count == 0)
        {
            return;
        }

        AssignInteractionButtons(definition, resolvedInteractionSprites);
    }

    private static void AssignInteractionButtons(ItemDefinition definition, List<Sprite> interactionSprites)
    {
        if (definition == null || interactionSprites == null || interactionSprites.Count == 0)
        {
            return;
        }

        if (definition.interactionButtonList == null)
        {
            definition.interactionButtonList = new List<Sprite>();
        }

        definition.interactionButtonList.Clear();
        definition.interactionButtonList.AddRange(interactionSprites);
        EditorUtility.SetDirty(definition);
    }

    private static bool ShouldAutoAssignInteractionButtons(List<Sprite> interactionButtonList)
    {
        if (interactionButtonList == null || interactionButtonList.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < interactionButtonList.Count; i++)
        {
            if (interactionButtonList[i] != null)
            {
                return false;
            }
        }

        return true;
    }

    private static string GetMapObjectFolderPath(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return string.Empty;
        }

        GameObject prefabRoot = mapObject.transform.root != null
            ? mapObject.transform.root.gameObject
            : mapObject.gameObject;

        string prefabAssetPath = AssetDatabase.GetAssetPath(prefabRoot);
        if (string.IsNullOrWhiteSpace(prefabAssetPath))
        {
            return string.Empty;
        }

        string folderPath = Path.GetDirectoryName(prefabAssetPath);
        return string.IsNullOrWhiteSpace(folderPath)
            ? string.Empty
            : folderPath.Replace("\\", "/");
    }

    private static List<Sprite> FindInteractionButtonSprites(string folderPath, string fileNamePrefix = null)
    {
        return LoadInteractionButtonSprites(FindInteractionButtonSpritePaths(folderPath, fileNamePrefix));
    }

    private static List<string> FindInteractionButtonSpritePaths(string folderPath, string fileNamePrefix = null)
    {
        List<string> spriteCandidatePaths = new List<string>();
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
        for (int i = 0; i < textureGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension)
                || fileNameWithoutExtension.IndexOf("_Interaction_", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(fileNamePrefix)
                && !fileNameWithoutExtension.StartsWith(fileNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            spriteCandidatePaths.Add(assetPath);
        }

        SortInteractionButtonSpritePaths(spriteCandidatePaths);
        return spriteCandidatePaths;
    }

    private static List<Sprite> LoadInteractionButtonSprites(List<string> spriteCandidatePaths)
    {
        List<Sprite> interactionSprites = new List<Sprite>();
        for (int i = 0; i < spriteCandidatePaths.Count; i++)
        {
            Sprite sprite = LoadOrConvertSprite(spriteCandidatePaths[i]);
            if (sprite != null)
            {
                interactionSprites.Add(sprite);
            }
        }

        return interactionSprites;
    }

    private static List<Sprite> FindDoorInteractionButtonSprites(string prefabFolderPath)
    {
        const string doorInteractionPrefix = "Door_Interaction";

        List<string> doorInteractionPaths = FindInteractionButtonSpritePaths(prefabFolderPath, doorInteractionPrefix);

        string parentFolderPath = Path.GetDirectoryName(prefabFolderPath);
        if (!string.IsNullOrWhiteSpace(parentFolderPath))
        {
            parentFolderPath = parentFolderPath.Replace("\\", "/");
            AddUniquePaths(
                doorInteractionPaths,
                FindInteractionButtonSpritePaths(parentFolderPath, doorInteractionPrefix));
        }

        SortInteractionButtonSpritePaths(doorInteractionPaths);
        return LoadInteractionButtonSprites(doorInteractionPaths);
    }

    private static void AddUniquePaths(List<string> targetPaths, List<string> sourcePaths)
    {
        for (int i = 0; i < sourcePaths.Count; i++)
        {
            bool alreadyAdded = false;
            for (int targetIndex = 0; targetIndex < targetPaths.Count; targetIndex++)
            {
                if (string.Equals(targetPaths[targetIndex], sourcePaths[i], StringComparison.OrdinalIgnoreCase))
                {
                    alreadyAdded = true;
                    break;
                }
            }

            if (!alreadyAdded)
            {
                targetPaths.Add(sourcePaths[i]);
            }
        }
    }

    private static void SortInteractionButtonSpritePaths(List<string> spriteCandidatePaths)
    {
        spriteCandidatePaths.Sort((left, right) =>
        {
            int compareResult = ParseInteractionButtonIndex(Path.GetFileNameWithoutExtension(left))
                .CompareTo(ParseInteractionButtonIndex(Path.GetFileNameWithoutExtension(right)));
            if (compareResult != 0)
            {
                return compareResult;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static int ParseInteractionButtonIndex(string fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return int.MaxValue;
        }

        const string marker = "_interaction_";
        string normalizedName = fileNameWithoutExtension.ToLowerInvariant();
        int markerIndex = normalizedName.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return int.MaxValue;
        }

        int numberStartIndex = markerIndex + marker.Length;
        int numberEndIndex = numberStartIndex;
        while (numberEndIndex < normalizedName.Length && char.IsDigit(normalizedName[numberEndIndex]))
        {
            numberEndIndex++;
        }

        if (numberEndIndex <= numberStartIndex)
        {
            return int.MaxValue;
        }

        return int.TryParse(normalizedName.Substring(numberStartIndex, numberEndIndex - numberStartIndex), out int parsedIndex)
            ? parsedIndex
            : int.MaxValue;
    }

    private static void ResolvePortableAssets(string assetPath, GameObject prefabRoot, out Mesh portableMesh, out Material portableMaterial)
    {
        portableMesh = FindPortableMesh(assetPath, prefabRoot);
        portableMaterial = FindPortableMaterial(assetPath, prefabRoot);
    }

    private static void TryResolveMapObjectPortableAssetFallback(
        string itemName,
        string itemFolder,
        ref Mesh portableMesh,
        ref Material portableMaterial)
    {
        if (portableMesh != null && portableMaterial != null)
        {
            return;
        }

        MapObject mapObject = FindMapObjectForItem(itemName, itemFolder);
        GameObject mapObjectRoot = GetMapObjectPrefabRoot(mapObject);
        if (mapObjectRoot == null)
        {
            return;
        }

        TryResolveFallbackPortableAssets(mapObjectRoot, ref portableMesh, ref portableMaterial);
    }

    private static bool CanReusePreviousPortableMaterial(Material previousMaterial, string itemName, string itemFolder)
    {
        if (previousMaterial == null)
        {
            return false;
        }

        string itemKey = NormalizePortableLookupName(itemName);
        if (string.IsNullOrWhiteSpace(itemKey))
        {
            return false;
        }

        string materialKey = NormalizePortableLookupName(previousMaterial.name);
        if (!string.IsNullOrWhiteSpace(materialKey)
            && (materialKey == itemKey || materialKey.Contains(itemKey)))
        {
            return true;
        }

        string materialPath = AssetDatabase.GetAssetPath(previousMaterial);
        if (string.IsNullOrWhiteSpace(materialPath) || string.IsNullOrWhiteSpace(itemFolder))
        {
            return false;
        }

        string normalizedMaterialPath = materialPath.Replace("\\", "/").ToLowerInvariant();
        string normalizedItemFolder = itemFolder.Replace("\\", "/").ToLowerInvariant();
        return normalizedMaterialPath.StartsWith(normalizedItemFolder, StringComparison.Ordinal);
    }

    private static bool IsInstallationObjectPrefab(GameObject prefabRoot)
    {
        if (prefabRoot == null)
        {
            return false;
        }

        return prefabRoot.GetComponentInChildren<InstallationObject>(true) != null;
    }

    private static GameObject GetMapObjectPrefabRoot(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return null;
        }

        Transform rootTransform = mapObject.transform != null ? mapObject.transform.root : null;
        if (rootTransform != null)
        {
            return rootTransform.gameObject;
        }

        return mapObject.gameObject;
    }

    private static void TryOverrideInstallationPortableAssets(
        string itemName,
        string itemFolder,
        GameObject prefabRoot,
        ref Mesh portableMesh,
        ref Material portableMaterial)
    {
        if (!IsInstallationItem(itemName, itemFolder, prefabRoot))
        {
            return;
        }

        string portableFolder = ResolveInstallationPortableItemFolder(itemFolder, prefabRoot);
        string itemKey = NormalizePortableLookupName(
            !string.IsNullOrWhiteSpace(itemName)
                ? itemName
                : prefabRoot != null ? prefabRoot.name : string.Empty);

        Mesh folderPortableMesh = FindPortableMeshInItemDirectory(portableFolder, itemKey);
        if (folderPortableMesh != null)
        {
            portableMesh = folderPortableMesh;
        }
        else
        {
            Mesh packageMesh = LoadPackagePortableMesh();
            if (packageMesh != null)
            {
                portableMesh = packageMesh;
            }
        }

        Material folderPortableMaterial = FindPortableMaterialInItemDirectory(portableFolder, itemKey);
        if (folderPortableMaterial != null)
        {
            portableMaterial = folderPortableMaterial;
        }
        else
        {
            Material packageMaterial = LoadPackagePortableMaterial();
            if (packageMaterial != null)
            {
                portableMaterial = packageMaterial;
            }
        }
    }

    private static string ResolveInstallationPortableItemFolder(string itemFolder, GameObject prefabRoot)
    {
        if (!string.IsNullOrWhiteSpace(itemFolder))
        {
            string normalizedItemFolder = itemFolder.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(normalizedItemFolder))
            {
                return normalizedItemFolder;
            }
        }

        if (prefabRoot == null)
        {
            return string.Empty;
        }

        return ResolveAssetDirectory(AssetDatabase.GetAssetPath(prefabRoot));
    }

    private static bool IsInstallationItem(string itemName, string itemFolder, GameObject prefabRoot)
    {
        if (prefabRoot != null)
        {
            return IsInstallationObjectPrefab(prefabRoot);
        }

        MapObject mapObject = FindMapObjectForItem(itemName, itemFolder);
        return IsInstallationObjectPrefab(GetMapObjectPrefabRoot(mapObject));
    }

    private static Mesh LoadPackagePortableMesh()
    {
        const string packageMeshPath = "Assets/MapObject/Package_P.mesh";
        return AssetDatabase.LoadAssetAtPath<Mesh>(packageMeshPath);
    }

    private static Material LoadPackagePortableMaterial()
    {
        const string packageMaterialPath = "Assets/MapObject/M_Package_P.mat";
        return AssetDatabase.LoadAssetAtPath<Material>(packageMaterialPath);
    }

    private static Mesh FindPortableMesh(string assetPath, GameObject prefabRoot)
    {
        string prefabName = prefabRoot != null ? prefabRoot.name : Path.GetFileNameWithoutExtension(assetPath);
        string itemKey = NormalizePortableLookupName(prefabName);
        string itemDirectory = ResolveAssetDirectory(assetPath);
        Mesh portableMesh = FindPortableMeshInItemDirectory(itemDirectory, itemKey);
        if (portableMesh != null)
        {
            return portableMesh;
        }

        string parentDirectory = Path.GetDirectoryName(itemDirectory)?.Replace("\\", "/") ?? string.Empty;
        return FindPortableMeshInParentDirectory(parentDirectory, itemKey);
    }

    private static Material FindPortableMaterial(string assetPath, GameObject prefabRoot)
    {
        string prefabName = prefabRoot != null ? prefabRoot.name : Path.GetFileNameWithoutExtension(assetPath);
        string itemKey = NormalizePortableLookupName(prefabName);
        string itemDirectory = ResolveAssetDirectory(assetPath);
        Material portableMaterial = FindPortableMaterialInItemDirectory(itemDirectory, itemKey);
        if (portableMaterial != null)
        {
            return portableMaterial;
        }

        string parentDirectory = Path.GetDirectoryName(itemDirectory)?.Replace("\\", "/") ?? string.Empty;
        return FindPortableMaterialInParentDirectory(parentDirectory, itemKey);
    }

    private static Mesh FindPortableMeshInItemDirectory(string itemDirectory, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(itemDirectory) || !AssetDatabase.IsValidFolder(itemDirectory))
        {
            return null;
        }

        return FindBestPortableMeshByGuidSearch(new[] { itemDirectory }, itemKey);
    }

    private static Material FindPortableMaterialInItemDirectory(string itemDirectory, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(itemDirectory) || !AssetDatabase.IsValidFolder(itemDirectory))
        {
            return null;
        }

        return FindBestPortableMaterialByGuidSearch(new[] { itemDirectory }, itemKey);
    }

    private static Mesh FindPortableMeshInParentDirectory(string parentDirectory, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory) || !AssetDatabase.IsValidFolder(parentDirectory))
        {
            return null;
        }

        Mesh directMesh = FindBestPortableMeshFromTopLevelDirectory(parentDirectory, itemKey);
        if (directMesh != null)
        {
            return directMesh;
        }

        List<string> supportFolders = GetPortableSupportSubfolders(parentDirectory);
        return supportFolders.Count > 0
            ? FindBestPortableMeshByGuidSearch(supportFolders.ToArray(), itemKey)
            : null;
    }

    private static Material FindPortableMaterialInParentDirectory(string parentDirectory, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory) || !AssetDatabase.IsValidFolder(parentDirectory))
        {
            return null;
        }

        Material directMaterial = FindBestPortableMaterialFromTopLevelDirectory(parentDirectory, itemKey);
        if (directMaterial != null)
        {
            return directMaterial;
        }

        List<string> supportFolders = GetPortableSupportSubfolders(parentDirectory);
        return supportFolders.Count > 0
            ? FindBestPortableMaterialByGuidSearch(supportFolders.ToArray(), itemKey)
            : null;
    }

    private static Mesh FindBestPortableMeshByGuidSearch(string[] searchDirectories, string itemKey)
    {
        if (searchDirectories == null || searchDirectories.Length == 0)
        {
            return null;
        }

        HashSet<string> seenAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Mesh bestMesh = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < searchDirectories.Length; i++)
        {
            string searchDirectory = searchDirectories[i];
            if (string.IsNullOrWhiteSpace(searchDirectory) || !AssetDatabase.IsValidFolder(searchDirectory))
            {
                continue;
            }

            string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { searchDirectory });
            for (int j = 0; j < guids.Length; j++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[j]);
                if (string.IsNullOrWhiteSpace(assetPath) || !seenAssetPaths.Add(assetPath))
                {
                    continue;
                }

                ScorePortableMeshAssetsAtPath(assetPath, itemKey, ref bestMesh, ref bestScore);
            }
        }

        return bestMesh;
    }

    private static Material FindBestPortableMaterialByGuidSearch(string[] searchDirectories, string itemKey)
    {
        if (searchDirectories == null || searchDirectories.Length == 0)
        {
            return null;
        }

        HashSet<string> seenAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Material bestMaterial = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < searchDirectories.Length; i++)
        {
            string searchDirectory = searchDirectories[i];
            if (string.IsNullOrWhiteSpace(searchDirectory) || !AssetDatabase.IsValidFolder(searchDirectory))
            {
                continue;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { searchDirectory });
            for (int j = 0; j < guids.Length; j++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[j]);
                if (string.IsNullOrWhiteSpace(assetPath) || !seenAssetPaths.Add(assetPath))
                {
                    continue;
                }

                ScorePortableMaterialAssetAtPath(assetPath, itemKey, ref bestMaterial, ref bestScore);
            }
        }

        return bestMaterial;
    }

    private static Mesh FindBestPortableMeshFromTopLevelDirectory(string directoryPath, string itemKey)
    {
        Mesh bestMesh = null;
        int bestScore = int.MinValue;
        string[] assetPaths = GetTopLevelAssetPaths(directoryPath);
        for (int i = 0; i < assetPaths.Length; i++)
        {
            ScorePortableMeshAssetsAtPath(assetPaths[i], itemKey, ref bestMesh, ref bestScore);
        }

        return bestMesh;
    }

    private static Material FindBestPortableMaterialFromTopLevelDirectory(string directoryPath, string itemKey)
    {
        Material bestMaterial = null;
        int bestScore = int.MinValue;
        string[] assetPaths = GetTopLevelAssetPaths(directoryPath);
        for (int i = 0; i < assetPaths.Length; i++)
        {
            ScorePortableMaterialAssetAtPath(assetPaths[i], itemKey, ref bestMaterial, ref bestScore);
        }

        return bestMaterial;
    }

    private static void ScorePortableMeshAssetsAtPath(string assetPath, string itemKey, ref Mesh bestMesh, ref int bestScore)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        UnityEngine.Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assetsAtPath.Length; i++)
        {
            Mesh mesh = assetsAtPath[i] as Mesh;
            if (mesh == null || !IsPortableMeshName(mesh.name))
            {
                continue;
            }

            int score = ScorePortableCandidate(mesh.name, itemKey);
            if (score > bestScore)
            {
                bestScore = score;
                bestMesh = mesh;
            }
        }
    }

    private static void ScorePortableMaterialAssetAtPath(string assetPath, string itemKey, ref Material bestMaterial, ref int bestScore)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material == null || !IsPortableName(material.name))
        {
            return;
        }

        int score = ScorePortableCandidate(material.name, itemKey);
        if (score > bestScore)
        {
            bestScore = score;
            bestMaterial = material;
        }
    }

    private static string[] GetTopLevelAssetPaths(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !AssetDatabase.IsValidFolder(directoryPath))
        {
            return Array.Empty<string>();
        }

        string absoluteDirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(absoluteDirectoryPath))
        {
            return Array.Empty<string>();
        }

        string[] filePaths = Directory.GetFiles(absoluteDirectoryPath, "*", SearchOption.TopDirectoryOnly);
        List<string> assetPaths = new List<string>();
        for (int i = 0; i < filePaths.Length; i++)
        {
            string filePath = filePaths[i];
            if (string.IsNullOrWhiteSpace(filePath) || filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalizedPath = filePath.Replace("\\", "/");
            int assetsIndex = normalizedPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                normalizedPath = normalizedPath.Substring(assetsIndex + 1);
            }
            else if (normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                // already normalized
            }
            else
            {
                continue;
            }

            assetPaths.Add(normalizedPath);
        }

        return assetPaths.ToArray();
    }

    private static List<string> GetPortableSupportSubfolders(string parentDirectory)
    {
        List<string> results = new List<string>();
        if (string.IsNullOrWhiteSpace(parentDirectory) || !AssetDatabase.IsValidFolder(parentDirectory))
        {
            return results;
        }

        string[] subFolders = AssetDatabase.GetSubFolders(parentDirectory);
        for (int i = 0; i < subFolders.Length; i++)
        {
            string subFolder = subFolders[i];
            string folderName = Path.GetFileName(subFolder)?.Trim().ToLowerInvariant() ?? string.Empty;
            if (folderName == "meshes"
                || folderName == "mesh"
                || folderName == "materials"
                || folderName == "material"
                || folderName == "portable"
                || folderName == "portables"
                || folderName == "shared"
                || folderName == "common")
            {
                results.Add(subFolder.Replace("\\", "/"));
            }
        }

        return results;
    }

    private static int ScorePortableCandidate(string name, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return int.MinValue;
        }

        string lower = name.ToLower(CultureInfo.InvariantCulture);
        string normalized = NormalizePortableLookupName(name);
        int score = 0;

        if (lower.Contains("portablemesh"))
        {
            score += 700;
        }
        else if (lower.Contains("portable"))
        {
            score += 400;
        }

        if (lower.Contains("_p") || lower.StartsWith("p_") || lower.EndsWith("_p"))
        {
            score += 250;
        }

        if (!string.IsNullOrWhiteSpace(itemKey))
        {
            if (normalized == itemKey)
            {
                score += 300;
            }
            else if (normalized.Contains(itemKey))
            {
                score += 150;
            }
        }

        return score;
    }

    private static string GetCategoryToken(string assetPath, string prefabName)
    {
        string normalizedPath = assetPath.Replace("\\", "/").ToLowerInvariant();
        string normalizedName = prefabName?.ToLowerInvariant() ?? string.Empty;

        if (normalizedPath.Contains("/ore/") || normalizedName.Contains("ore"))
        {
            return "ore";
        }

        if (normalizedPath.Contains("/tree/") || normalizedName.Contains("tree") || normalizedName.Contains("log"))
        {
            return "tree";
        }

        if (normalizedPath.Contains("/log/") || normalizedName.Contains("log"))
        {
            return "log";
        }

        if (normalizedPath.Contains("/wood/") || normalizedName.Contains("wood"))
        {
            return "wood";
        }

        if (normalizedPath.Contains("/equip/") || normalizedPath.Contains("/equips/") || normalizedName.Contains("pick") || normalizedName.Contains("axe"))
        {
            return "equip";
        }

        return string.Empty;
    }

    private static bool IsCandidateInCategory(string candidatePath, string categoryToken, string prefabDirectory, string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(categoryToken))
        {
            return true;
        }

        string normalizedPath = candidatePath.Replace("\\", "/").ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(prefabDirectory)
            && normalizedPath.StartsWith(prefabDirectory.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(parentDirectory)
            && normalizedPath.StartsWith(parentDirectory.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return true;
        }

        return normalizedPath.Contains($"/{categoryToken}/");
    }

    private static MeshFilter FindPreferredMeshFilter(MeshFilter[] meshFilters)
    {
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            if (IsPortableMeshName(meshFilter.sharedMesh.name))
            {
                return meshFilter;
            }
        }

        return null;
    }

    private static MeshRenderer FindPreferredMeshRenderer(MeshRenderer[] meshRenderers)
    {
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            MeshRenderer meshRenderer = meshRenderers[i];
            if (meshRenderer == null || meshRenderer.sharedMaterials == null)
            {
                continue;
            }

            if (HasPortableMarker(meshRenderer.transform))
            {
                return meshRenderer;
            }

            for (int materialIndex = 0; materialIndex < meshRenderer.sharedMaterials.Length; materialIndex++)
            {
                Material sharedMaterial = meshRenderer.sharedMaterials[materialIndex];
                if (sharedMaterial != null && IsPortableName(sharedMaterial.name))
                {
                    return meshRenderer;
                }
            }
        }

        return null;
    }

    private static bool IsPortableName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLower(CultureInfo.InvariantCulture);
        return normalized.Contains("portable")
               || normalized == "p"
               || normalized.StartsWith("p_")
               || normalized.StartsWith("p ")
               || normalized.EndsWith("_p")
               || normalized.EndsWith(" p")
               || normalized.Contains("(p)");
    }

    private static bool IsPortableMeshName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLower(CultureInfo.InvariantCulture);
        return normalized.Contains("portable")
               || normalized.Contains("_p")
               || normalized.StartsWith("p_")
               || normalized.EndsWith("_p")
               || normalized == "p";
    }

    private static string NormalizePortableLookupName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().ToLower(CultureInfo.InvariantCulture);
        normalized = normalized.Replace("portable", string.Empty);
        normalized = normalized.Replace("mesh", string.Empty);
        normalized = normalized.Replace("material", string.Empty);
        normalized = normalized.Replace("mat", string.Empty);
        normalized = normalized.Replace("icon", string.Empty);
        normalized = normalized.Replace("item", string.Empty);
        normalized = normalized.Replace("_p", string.Empty);
        normalized = normalized.Replace("ore", string.Empty);
        normalized = normalized.Replace("_", string.Empty);
        normalized = normalized.Replace(" ", string.Empty);
        return normalized;
    }

    private static List<string> BuildIconLookupAliases(string prefabName)
    {
        List<string> aliases = new List<string>();
        AddAlias(aliases, NormalizeItemLookupName(prefabName));

        string lowerPrefabName = prefabName?.Trim().ToLower(CultureInfo.InvariantCulture) ?? string.Empty;
        if (lowerPrefabName.EndsWith("ore", StringComparison.Ordinal))
        {
            AddAlias(aliases, NormalizeItemLookupName(lowerPrefabName.Substring(0, lowerPrefabName.Length - 3)));
        }

        if (lowerPrefabName.EndsWith("_p", StringComparison.Ordinal))
        {
            AddAlias(aliases, NormalizeItemLookupName(lowerPrefabName.Substring(0, lowerPrefabName.Length - 2)));
        }

        return aliases;
    }

    private static void AddAlias(List<string> aliases, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias) || aliases.Contains(alias))
        {
            return;
        }

        aliases.Add(alias);
    }

    private static string NormalizeItemLookupName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().ToLower(CultureInfo.InvariantCulture);
        normalized = normalized.Replace("_icon", string.Empty);
        normalized = normalized.Replace("icon", string.Empty);
        normalized = normalized.Replace("_tb", string.Empty);
        normalized = normalized.Replace("tb", string.Empty);
        normalized = normalized.Replace("_p", string.Empty);
        normalized = normalized.Replace("-", string.Empty);
        normalized = normalized.Replace("_", string.Empty);
        normalized = normalized.Replace(" ", string.Empty);
        return normalized;
    }

    private static bool HasPortableMarker(Transform targetTransform)
    {
        Transform current = targetTransform;
        while (current != null)
        {
            if (IsPortableName(current.name))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void TryResolveFallbackPortableAssets(GameObject prefabRoot, ref Mesh portableMesh, ref Material portableMaterial)
    {
        if (prefabRoot == null)
        {
            return;
        }

        if (portableMesh == null)
        {
            MeshFilter meshFilter = prefabRoot.GetComponentInChildren<MeshFilter>(true);
            if (meshFilter != null)
            {
                portableMesh = meshFilter.sharedMesh;
            }
        }

        if (portableMaterial == null)
        {
            MeshRenderer meshRenderer = prefabRoot.GetComponentInChildren<MeshRenderer>(true);
            if (meshRenderer != null && meshRenderer.sharedMaterials != null && meshRenderer.sharedMaterials.Length > 0)
            {
                portableMaterial = meshRenderer.sharedMaterials[0];
            }
        }
    }

    public void ApplyItemIdsToPrefabs()
    {
        if (items == null)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            PropObj prefab = items[i].prefab;
            if (prefab == null)
            {
                continue;
            }

            SerializedObject serializedPrefab = new SerializedObject(prefab);
            SerializedProperty objIdProperty = serializedPrefab.FindProperty("objId");
            if (objIdProperty == null)
            {
                continue;
            }

            objIdProperty.intValue = items[i].id;
            serializedPrefab.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefabRoot = prefab.gameObject;
            PrefabUtility.SavePrefabAsset(prefabRoot);
            EditorUtility.SetDirty(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static string SanitizeAssetFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Item";
        }

        string sanitized = value.Trim();
        char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            sanitized = sanitized.Replace(invalidChars[i].ToString(), string.Empty);
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Item";
        }

        return sanitized;
    }

    private static bool EnsureAssetFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        folderPath = folderPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return true;
        }

        string[] parts = folderPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
        {
            return false;
        }

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                string guid = AssetDatabase.CreateFolder(current, parts[i]);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    return false;
                }
            }

            current = next;
        }

        return AssetDatabase.IsValidFolder(folderPath);
    }

    private void ClearItemDefinitionAssets()
    {
        string targetDirectory = "Assets/Data/Items";
        if (!EnsureAssetFolder(targetDirectory))
        {
            Debug.LogError($"ItemManager: Failed to create item definition folder at '{targetDirectory}'.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { targetDirectory });
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            if (!AssetDatabase.DeleteAsset(assetPath))
            {
                Debug.LogWarning($"ItemManager: Failed to delete item definition asset at '{assetPath}'.");
            }
        }

        itemDefinitions?.Clear();
        EditorUtility.SetDirty(this);
    }

    private void RecreateItemDefinitionsFromItems(List<ItemSet> sourceItems)
    {
        if (sourceItems == null || sourceItems.Count == 0)
        {
            Debug.LogWarning("ItemManager: No items found to rebuild ItemDefinitions.");
            return;
        }

        string targetDirectory = "Assets/Data/Items";
        if (!EnsureAssetFolder(targetDirectory))
        {
            Debug.LogError($"ItemManager: Failed to create item definition folder at '{targetDirectory}'.");
            return;
        }

        if (itemDefinitions == null)
        {
            itemDefinitions = new List<ItemDefinition>();
        }

        Dictionary<string, string> itemFolderLookup = BuildItemFolderLookup();
        List<ItemDefinition> existingDefinitions = CollectExistingItemDefinitions(targetDirectory);
        Dictionary<int, ItemDefinition> existingDefinitionsById = new Dictionary<int, ItemDefinition>();
        Dictionary<string, ItemDefinition> existingDefinitionsByName = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < existingDefinitions.Count; i++)
        {
            ItemDefinition existingDefinition = existingDefinitions[i];
            if (existingDefinition == null)
            {
                continue;
            }

            if (existingDefinition.id >= 0 && !existingDefinitionsById.ContainsKey(existingDefinition.id))
            {
                existingDefinitionsById[existingDefinition.id] = existingDefinition;
            }

            string lookupName = GetItemDefinitionLookupName(existingDefinition);
            if (!string.IsNullOrWhiteSpace(lookupName) && !existingDefinitionsByName.ContainsKey(lookupName))
            {
                existingDefinitionsByName[lookupName] = existingDefinition;
            }
        }

        List<ItemDefinition> rebuiltDefinitions = new List<ItemDefinition>();
        HashSet<ItemDefinition> usedDefinitions = new HashSet<ItemDefinition>();

        for (int i = 0; i < sourceItems.Count; i++)
        {
            ItemSet itemSet = sourceItems[i];
            if (itemSet.id < 0)
            {
                continue;
            }

            ItemDefinition definition = ResolveExistingItemDefinitionForRebuild(
                itemSet,
                existingDefinitionsById,
                existingDefinitionsByName,
                usedDefinitions);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                string safeName = string.IsNullOrWhiteSpace(itemSet.name) ? $"Item_{itemSet.id}" : itemSet.name;
                safeName = SanitizeAssetFileName(safeName);
                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{targetDirectory}/Item_{itemSet.id}_{safeName}.asset");
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    Debug.LogError($"ItemManager: Failed to generate asset path for item '{itemSet.name}' (id {itemSet.id}).");
                    continue;
                }

                AssetDatabase.CreateAsset(definition, assetPath);
            }

            definition.id = itemSet.id;
            definition.itemName = itemSet.name;
            definition.mapObject = FindMapObjectForItem(itemSet.name, GetItemFolderForName(itemSet.name, itemFolderLookup));

            Mesh definitionPortableMesh = itemSet.portableMesh;
            Material definitionPortableMaterial = itemSet.portableMat;
            TryOverrideInstallationPortableAssets(
                itemSet.name,
                GetItemFolderForName(itemSet.name, itemFolderLookup),
                GetMapObjectPrefabRoot(definition.mapObject),
                ref definitionPortableMesh,
                ref definitionPortableMaterial);
            definition.portableMesh = definitionPortableMesh;
            definition.portableMat = definitionPortableMaterial;
            definition.icon = itemSet.icon;
            rebuiltDefinitions.Add(definition);
            usedDefinitions.Add(definition);
            existingDefinitionsById[itemSet.id] = definition;
            string definitionLookupName = GetItemDefinitionLookupName(definition);
            if (!string.IsNullOrWhiteSpace(definitionLookupName))
            {
                existingDefinitionsByName[definitionLookupName] = definition;
            }

            BindMapObjectDefinition(definition);
            TryBindItemDefinitionToPrefab(itemSet.prefab, definition);

            EditorUtility.SetDirty(definition);
        }

        itemDefinitions.Clear();
        itemDefinitions.AddRange(rebuiltDefinitions);
        SortItemDefinitionsById(itemDefinitions);
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private List<ItemDefinition> CollectExistingItemDefinitions(string targetDirectory)
    {
        List<ItemDefinition> results = new List<ItemDefinition>();
        HashSet<string> seenAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDefinition(ItemDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(definition);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                if (!seenAssetPaths.Add(assetPath))
                {
                    return;
                }
            }
            else if (results.Contains(definition))
            {
                return;
            }

            results.Add(definition);
        }

        if (itemDefinitions != null)
        {
            for (int i = 0; i < itemDefinitions.Count; i++)
            {
                AddDefinition(itemDefinitions[i]);
            }
        }

        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { targetDirectory });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            AddDefinition(AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath));
        }

        return results;
    }

    private static ItemDefinition ResolveExistingItemDefinitionForRebuild(
        ItemSet itemSet,
        Dictionary<int, ItemDefinition> existingDefinitionsById,
        Dictionary<string, ItemDefinition> existingDefinitionsByName,
        HashSet<ItemDefinition> usedDefinitions)
    {
        if (existingDefinitionsById != null
            && existingDefinitionsById.TryGetValue(itemSet.id, out ItemDefinition byIdDefinition)
            && byIdDefinition != null
            && !usedDefinitions.Contains(byIdDefinition))
        {
            return byIdDefinition;
        }

        string lookupName = itemSet.name?.Trim();
        if (!string.IsNullOrWhiteSpace(lookupName)
            && existingDefinitionsByName != null
            && existingDefinitionsByName.TryGetValue(lookupName, out ItemDefinition byNameDefinition)
            && byNameDefinition != null
            && !usedDefinitions.Contains(byNameDefinition))
        {
            return byNameDefinition;
        }

        return null;
    }

    private static Dictionary<string, ItemDefinition> BuildExistingItemDefinitionLookupByName()
    {
        Dictionary<string, ItemDefinition> results = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
        ItemManager[] managers = FindObjectsOfType<ItemManager>(true);
        for (int managerIndex = 0; managerIndex < managers.Length; managerIndex++)
        {
            ItemManager manager = managers[managerIndex];
            if (manager == null || manager.itemDefinitions == null)
            {
                continue;
            }

            for (int definitionIndex = 0; definitionIndex < manager.itemDefinitions.Count; definitionIndex++)
            {
                ItemDefinition definition = manager.itemDefinitions[definitionIndex];
                string lookupName = GetItemDefinitionLookupName(definition);
                if (definition != null && !string.IsNullOrWhiteSpace(lookupName) && !results.ContainsKey(lookupName))
                {
                    results[lookupName] = definition;
                }
            }
        }

        return results;
    }

    private static int ResolvePreservedItemSize(string itemName, int fallbackSize, Dictionary<string, ItemDefinition> existingDefinitionsByName)
    {
        if (!string.IsNullOrWhiteSpace(itemName)
            && existingDefinitionsByName != null
            && existingDefinitionsByName.TryGetValue(itemName.Trim(), out ItemDefinition definition)
            && definition != null)
        {
            return Mathf.Max(0, (int)definition.size);
        }

        return Mathf.Max(0, fallbackSize);
    }

    private static string GetItemDefinitionLookupName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(definition.itemName))
        {
            return definition.itemName.Trim();
        }

        return string.IsNullOrWhiteSpace(definition.name) ? string.Empty : definition.name.Trim();
    }

    private static void TryBindItemDefinitionToPrefab(PropObj prefab, ItemDefinition definition)
    {
        SyncPropObjectMetadata(prefab, definition);
    }

    private static void SyncPropObjectMetadata(PropObj prefabObject, ItemDefinition definition)
    {
        if (prefabObject == null || definition == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(prefabObject);

        SerializedProperty objectNameProperty = serializedObject.FindProperty("objectName");
        if (objectNameProperty != null)
        {
            objectNameProperty.stringValue = ResolvePrefabObjectName(definition);
        }

        SerializedProperty objIdProperty = serializedObject.FindProperty("objId");
        if (objIdProperty != null)
        {
            objIdProperty.intValue = definition.id;
        }

        SerializedProperty definitionProperty = serializedObject.FindProperty("itemDefinition");
        if (definitionProperty != null)
        {
            definitionProperty.objectReferenceValue = definition;
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(prefabObject);

        GameObject prefabRoot = ResolvePrefabRoot(prefabObject.gameObject);
        if (prefabRoot == null)
        {
            return;
        }

        EditorUtility.SetDirty(prefabRoot);
        if (PrefabUtility.IsPartOfPrefabAsset(prefabRoot))
        {
            PrefabUtility.SavePrefabAsset(prefabRoot);
        }
    }

    private static string ResolvePrefabObjectName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(definition.itemName))
        {
            return definition.itemName.Trim();
        }

        return string.IsNullOrWhiteSpace(definition.name) ? string.Empty : definition.name.Trim();
    }

    private static GameObject ResolvePrefabRoot(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return null;
        }

        return gameObject.transform.root != null ? gameObject.transform.root.gameObject : gameObject;
    }

    [ContextMenu("Migrate Resource Definitions From Resources")]
    public void MigrateResourceDefinitionsFromResources()
    {
        string targetDirectory = GetResourceDefinitionTargetDirectory();
        if (!EnsureAssetFolder(targetDirectory))
        {
            Debug.LogError($"ItemManager: Failed to create resource definition folder at '{targetDirectory}'.");
            return;
        }

        string[] resourceSearchRoots = GetResourcePrefabSearchRoots();
        string[] resourceGuids = resourceSearchRoots.Length > 0
            ? AssetDatabase.FindAssets("t:Prefab", resourceSearchRoots)
            : new string[0];
        Dictionary<string, ResourceDefinition> existingDefinitions = new Dictionary<string, ResourceDefinition>();

        List<string> definitionSearchRoots = new List<string>();
        AddSearchFolderIfExists(definitionSearchRoots, targetDirectory);
        string[] allDefinitionRoots = GetResourceDefinitionSearchRoots();
        for (int i = 0; i < allDefinitionRoots.Length; i++)
        {
            string root = allDefinitionRoots[i];
            if (string.Equals(root, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            definitionSearchRoots.Add(root);
        }

        string[] existingDefGuids = definitionSearchRoots.Count > 0
            ? AssetDatabase.FindAssets("t:ResourceDefinition", definitionSearchRoots.ToArray())
            : new string[0];
        for (int i = 0; i < existingDefGuids.Length; i++)
        {
            string defPath = AssetDatabase.GUIDToAssetPath(existingDefGuids[i]);
            ResourceDefinition existing = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(defPath);
            if (existing != null && !string.IsNullOrWhiteSpace(existing.resourceName) && !existingDefinitions.ContainsKey(existing.resourceName))
            {
                existingDefinitions[existing.resourceName] = existing;
            }
        }

        for (int i = 0; i < resourceGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(resourceGuids[i]);
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabRoot == null)
            {
                continue;
            }

            Resource resource = prefabRoot.GetComponent<Resource>();
            if (resource == null)
            {
                resource = prefabRoot.GetComponentInChildren<Resource>(true);
            }

            if (resource == null)
            {
                continue;
            }

            string resourceName = prefabRoot.name;
            if (!existingDefinitions.TryGetValue(resourceName, out ResourceDefinition definition) || definition == null)
            {
                definition = ScriptableObject.CreateInstance<ResourceDefinition>();
                string assetName = $"Resource_{resourceName}.asset";
                string definitionPath = AssetDatabase.GenerateUniqueAssetPath($"{targetDirectory}/{assetName}");
                if (string.IsNullOrWhiteSpace(definitionPath))
                {
                    Debug.LogError($"ItemManager: Failed to generate resource definition path for '{resourceName}'.");
                    continue;
                }

                AssetDatabase.CreateAsset(definition, definitionPath);
                existingDefinitions[resourceName] = definition;
            }

            definition.resourceName = resourceName;
            definition.prefab = resource;
            definition.harvestMode = resource.ResolvedHarvestMode;
            definition.defaultResourceCount = resource.ResourceCount;
            definition.defaultGetCount = resource.GetCount;
            definition.defaultMaxGauge = resource.MaxGauge;
            definition.defaultCurrentGauge = resource.CurrentGauge;

            SerializedObject resourceObject = new SerializedObject(resource);
            SerializedProperty definitionProperty = resourceObject.FindProperty("definition");
            if (definitionProperty != null)
            {
                definitionProperty.objectReferenceValue = definition;
                resourceObject.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void BindMapObjectDefinition(ItemDefinition definition)
    {
        if (definition == null || definition.mapObject == null)
        {
            return;
        }

        GameObject prefabRoot = definition.mapObject.gameObject;
        if (prefabRoot != null)
        {
            prefabRoot = prefabRoot.transform.root != null ? prefabRoot.transform.root.gameObject : prefabRoot;
            string prefabPath = AssetDatabase.GetAssetPath(prefabRoot);
            MapObject preferredMapObject = FindPreferredMapObjectOnPrefab(prefabRoot, prefabPath);
            if (preferredMapObject != null && preferredMapObject != definition.mapObject)
            {
                definition.mapObject = preferredMapObject;
            }
        }

        SyncPropObjectMetadata(definition.mapObject, definition);

        TryAutoAssignInteractionButtons(definition);
        EditorUtility.SetDirty(definition.mapObject);
    }

    private static void SortItemDefinitionsById(List<ItemDefinition> definitions)
    {
        if (definitions == null || definitions.Count <= 1)
        {
            return;
        }

        definitions.Sort((left, right) =>
        {
            if (left == null && right == null)
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

            return left.id.CompareTo(right.id);
        });
    }

    private static void SyncTerrainGeneratorResourceDefinitions()
    {
        TerrainGenerator[] generators = FindObjectsOfType<TerrainGenerator>(true);
        for (int i = 0; i < generators.Length; i++)
        {
            TerrainGenerator generator = generators[i];
            if (generator == null)
            {
                continue;
            }

            generator.SyncResourceEntryDefinitions();
            EditorUtility.SetDirty(generator);
        }
    }
#endif
}
