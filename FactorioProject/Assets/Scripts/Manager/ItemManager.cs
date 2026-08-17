using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
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
        public ItemDefinition.ItemLightMode lightMode;
        public float lightRange;
        public float lightIntensityMultiplier;
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

    public bool TryGetItemDefinitionById(int id, out ItemDefinition definition)
    {
        if (id >= 0 && itemDefinitions != null)
        {
            for (int i = 0; i < itemDefinitions.Count; i++)
            {
                ItemDefinition candidate = itemDefinitions[i];
                if (candidate != null && candidate.id == id)
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        definition = null;
        return false;
    }

    public bool TryGetRequiredManualForTarget(int targetItemId, out ItemDefinition manualDefinition)
    {
        if (targetItemId >= 0 && itemDefinitions != null)
        {
            for (int i = 0; i < itemDefinitions.Count; i++)
            {
                ItemDefinition candidate = itemDefinitions[i];
                ItemDefinition target = candidate != null ? candidate.ManualTargetItem : null;
                if (candidate != null
                    && candidate.id >= 0
                    && target != null
                    && target.id == targetItemId)
                {
                    manualDefinition = candidate;
                    return true;
                }
            }
        }

        manualDefinition = null;
        return false;
    }

    public bool RegisterRuntimeItemDefinition(ItemDefinition definition)
    {
        if (definition == null || definition.id < 0)
        {
            return false;
        }

        itemDefinitions ??= new List<ItemDefinition>();
        for (int i = 0; i < itemDefinitions.Count; i++)
        {
            ItemDefinition registeredDefinition = itemDefinitions[i];
            if (registeredDefinition == definition
                || (registeredDefinition != null
                    && registeredDefinition.id == definition.id))
            {
                return true;
            }
        }

        itemDefinitions.Add(definition);
        return true;
    }

    public bool TryGetItemSetById(int id, out ItemSet itemSet)
    {
        if (TryGetItemDefinitionById(id, out ItemDefinition definition))
        {
            itemSet = new ItemSet
            {
                id = definition.id,
                name = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName,
                prefab = ResolvePrefabForId(definition.id),
                portableMesh = definition.portableMesh,
                portableMat = definition.portableMat,
                icon = definition.icon,
                lightMode = definition.lightMode,
                lightRange = definition.LightRange,
                lightIntensityMultiplier = definition.LightIntensityMultiplier,
                size = (int)definition.size
            };
            return true;
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
    private const string SharedAnimalMeatPortableMeshPath = "Assets/Items/Meet/Meet_P.mesh";
    private const string SharedWheelPortableMeshPath = "Assets/Items/Train/Wheel/Wheel_P.mesh";
    private const string IronWheelPortableMaterialPath = "Assets/Items/Train/Wheel/M_IronWheel_P.mat";
    private const string WoodenWheelPortableMaterialPath = "Assets/Items/Train/Wheel/M_WoodenWheel_P.mat";
    private static readonly string[] ItemIconAssetFilters = { "t:Sprite", "t:Texture2D" };

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

        RecreateItemDefinitionsFromItems(items);
    }

    [ContextMenu("Rebuild Items From Assets (Definitions)")]
    public void RebuildItemDefinitionsFromAssets()
    {
        RebuildItemsFromAssetFolders(null);
    }

    public void RebuildItemDefinitionsFromAssets(Action<string, float> reportProgress)
    {
        RebuildItemsFromAssetFolders(reportProgress);
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
        RebuildItemsFromAssetFolders(null);
    }

    public bool RebuildItemDefinitionFromAssets(ItemDefinition definition, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (definition == null)
        {
            errorMessage = "선택한 ItemDefinition이 없습니다.";
            return false;
        }

        if (definition.id < 0)
        {
            errorMessage = $"'{GetItemDefinitionLookupName(definition)}'의 ID가 유효하지 않습니다.";
            return false;
        }

        string definitionName = GetItemDefinitionLookupName(definition);
        Dictionary<string, List<string>> itemFolderLookup = BuildItemFolderLookup();
        List<string> itemFolders = GetItemFoldersForName(definitionName, itemFolderLookup);
        if (itemFolders == null || itemFolders.Count == 0)
        {
            errorMessage = $"Assets/Items에서 '{definitionName}'에 대응하는 아이템 폴더를 찾지 못했습니다.";
            return false;
        }

        string itemName = ResolveItemName(itemFolders[0], Path.GetFileName(itemFolders[0]));
        if (string.IsNullOrWhiteSpace(itemName))
        {
            errorMessage = $"'{itemFolders[0]}'에서 아이템 이름을 확인할 수 없습니다.";
            return false;
        }

        items ??= new List<ItemSet>();
        int itemIndex = FindItemSetIndex(definition.id, itemName);
        bool hasPreviousItem = itemIndex >= 0;
        ItemSet previousItem = hasPreviousItem
            ? items[itemIndex]
            : CreateItemSetFallback(definition, itemName);
        ItemSet rebuiltItem = BuildItemSetFromAssetFolders(
            itemFolders,
            itemName,
            definition.id,
            hasPreviousItem,
            previousItem,
            definition);

        if (hasPreviousItem)
        {
            items[itemIndex] = rebuiltItem;
        }
        else
        {
            items.Add(rebuiltItem);
        }

        SortItemSetsByIdThenName(items);
        ApplyItemSetToDefinition(rebuiltItem, definition, itemFolderLookup);
        RegisterRebuiltItemDefinition(definition);
        SortItemDefinitionsById(itemDefinitions);
        ApplyItemIdToPrefab(rebuiltItem);
        MarkEditorDirty();
        return true;
    }

    private void RebuildItemsFromAssetFolders(Action<string, float> reportProgress)
    {
        ReportRebuildProgress(reportProgress, "아이템 폴더 검색 중...", 0f);
        if (itemDefinitions == null)
        {
            itemDefinitions = new List<ItemDefinition>();
        }

        if (items == null)
        {
            items = new List<ItemSet>();
        }

        Dictionary<string, ItemSet> previousItemsByName = BuildItemSetLookupByName(items);
        Dictionary<string, List<string>> itemFolderLookup = BuildItemFolderLookup();
        List<string> itemNames = new List<string>(itemFolderLookup.Keys);
        itemNames.Sort(StringComparer.OrdinalIgnoreCase);
        ReportRebuildProgress(reportProgress, $"아이템 목록 분석 중... ({itemNames.Count}개)", 0.08f);
        Dictionary<string, int> preservedIdsByItemName = BuildPreservedItemIds(itemNames, previousItemsByName);
        Dictionary<string, ItemDefinition> existingDefinitionsByName = BuildExistingItemDefinitionLookupByName();
        HashSet<int> usedIds = new HashSet<int>(preservedIdsByItemName.Values);
        HashSet<int> assignedIds = new HashSet<int>();

        List<ItemSet> rebuiltItems = new List<ItemSet>();

        for (int i = 0; i < itemNames.Count; i++)
        {
            string itemName = itemNames[i];
            float itemProgress = itemNames.Count > 0 ? (float)i / itemNames.Count : 1f;
            ReportRebuildProgress(
                reportProgress,
                $"아이템 리빌드 중 ({i + 1}/{itemNames.Count}): {itemName}",
                Mathf.Lerp(0.12f, 0.58f, itemProgress));
            if (string.IsNullOrWhiteSpace(itemName)
                || !itemFolderLookup.TryGetValue(itemName, out List<string> itemFolders)
                || itemFolders == null
                || itemFolders.Count == 0)
            {
                continue;
            }

            if (itemFolders.Count > 1)
            {
                Debug.LogWarning(
                    $"ItemManager: '{itemName}' resolves from multiple asset folders and will be rebuilt as one item: "
                    + string.Join(", ", itemFolders));
            }

            bool hasPreviousItem = previousItemsByName.TryGetValue(itemName, out ItemSet previousItem);
            existingDefinitionsByName.TryGetValue(itemName, out ItemDefinition existingDefinition);

            int itemId = preservedIdsByItemName.TryGetValue(itemName, out int preservedId)
                         && assignedIds.Add(preservedId)
                ? preservedId
                : GetNextAvailableId(usedIds);
            usedIds.Add(itemId);
            assignedIds.Add(itemId);

            rebuiltItems.Add(BuildItemSetFromAssetFolders(
                itemFolders,
                itemName,
                itemId,
                hasPreviousItem,
                previousItem,
                existingDefinition));
        }

        SortItemSetsByIdThenName(rebuiltItems);
        items = rebuiltItems;
        RecreateItemDefinitionsFromItems(
            rebuiltItems,
            itemFolderLookup,
            (message, progress) => ReportRebuildProgress(
                reportProgress,
                message,
                Mathf.Lerp(0.58f, 0.86f, progress)));
        SortItemDefinitionsById(itemDefinitions);
        ReportRebuildProgress(reportProgress, "리소스 Definition 동기화 중...", 0.88f);
        MigrateResourceDefinitionsFromResources();
        ReportRebuildProgress(reportProgress, "Terrain 리소스 연결 중...", 0.96f);
        SyncTerrainGeneratorResourceDefinitions();
        MarkEditorDirty();
        ReportRebuildProgress(reportProgress, "아이템 데이터 리빌드 완료", 1f);
    }

    private static void ReportRebuildProgress(
        Action<string, float> reportProgress,
        string message,
        float progress)
    {
        reportProgress?.Invoke(message, Mathf.Clamp01(progress));
    }

    private static ItemSet BuildItemSetFromAssetFolders(
        IReadOnlyList<string> itemFolders,
        string itemName,
        int itemId,
        bool hasPreviousItem,
        ItemSet previousItem,
        ItemDefinition existingDefinition)
    {
        PropObj propObject = null;
        GameObject prefabRoot = null;
        Mesh portableMesh = null;
        Material portableMaterial = null;
        Sprite icon = existingDefinition != null
            ? existingDefinition.icon
            : hasPreviousItem ? previousItem.icon : null;

        for (int i = 0; itemFolders != null && i < itemFolders.Count; i++)
        {
            string itemFolder = itemFolders[i];
            PropObj candidatePropObject = FindPropObjInFolder(itemFolder, out GameObject candidatePrefabRoot);
            if (propObject == null && candidatePropObject != null)
            {
                propObject = candidatePropObject;
                prefabRoot = candidatePrefabRoot;
            }

            ResolvePortableAssets(
                itemFolder,
                candidatePrefabRoot,
                out Mesh candidatePortableMesh,
                out Material candidatePortableMaterial);
            portableMesh ??= candidatePortableMesh;
            portableMaterial ??= candidatePortableMaterial;
            icon = ResolveItemIcon(itemFolder, itemName, icon);
        }

        for (int i = 0; itemFolders != null && i < itemFolders.Count; i++)
        {
            TryOverridePortableAssets(
                itemName,
                itemFolders[i],
                prefabRoot,
                ref portableMesh,
                ref portableMaterial);
        }

        if (existingDefinition != null)
        {
            propObject ??= existingDefinition.mapObject;
            portableMesh ??= existingDefinition.portableMesh;
            portableMaterial ??= existingDefinition.portableMat;
        }

        if (hasPreviousItem)
        {
            portableMesh ??= previousItem.portableMesh;
            portableMaterial ??= previousItem.portableMat;
        }

        return new ItemSet
        {
            id = itemId,
            name = itemName,
            prefab = propObject,
            portableMesh = portableMesh,
            portableMat = portableMaterial,
            icon = icon,
            lightMode = existingDefinition != null ? existingDefinition.lightMode : previousItem.lightMode,
            lightRange = existingDefinition != null ? existingDefinition.LightRange : previousItem.lightRange,
            lightIntensityMultiplier = existingDefinition != null
                ? existingDefinition.LightIntensityMultiplier
                : previousItem.lightIntensityMultiplier > 0f ? previousItem.lightIntensityMultiplier : 1f,
            size = existingDefinition != null
                ? Mathf.Max(0, (int)existingDefinition.size)
                : Mathf.Max(0, previousItem.size)
        };
    }

    private static ItemSet CreateItemSetFallback(ItemDefinition definition, string itemName)
    {
        return new ItemSet
        {
            id = definition.id,
            name = itemName,
            prefab = definition.mapObject,
            portableMesh = definition.portableMesh,
            portableMat = definition.portableMat,
            icon = definition.icon,
            lightMode = definition.lightMode,
            lightRange = definition.LightRange,
            lightIntensityMultiplier = definition.LightIntensityMultiplier,
            size = Mathf.Max(0, (int)definition.size)
        };
    }

    private int FindItemSetIndex(int itemId, string itemName)
    {
        int nameMatchIndex = -1;
        for (int i = 0; items != null && i < items.Count; i++)
        {
            ItemSet itemSet = items[i];
            if (itemSet.id == itemId)
            {
                return i;
            }

            if (nameMatchIndex < 0
                && !string.IsNullOrWhiteSpace(itemName)
                && string.Equals(itemSet.name, itemName, StringComparison.OrdinalIgnoreCase))
            {
                nameMatchIndex = i;
            }
        }

        return nameMatchIndex;
    }

    private static Dictionary<string, ItemSet> BuildItemSetLookupByName(List<ItemSet> sourceItems)
    {
        Dictionary<string, ItemSet> results = new Dictionary<string, ItemSet>(StringComparer.OrdinalIgnoreCase);
        if (sourceItems == null)
        {
            return results;
        }

        for (int i = 0; i < sourceItems.Count; i++)
        {
            ItemSet itemSet = sourceItems[i];
            if (string.IsNullOrWhiteSpace(itemSet.name))
            {
                continue;
            }

            if (!results.TryGetValue(itemSet.name, out ItemSet existingItem)
                || (itemSet.id >= 0 && (existingItem.id < 0 || itemSet.id < existingItem.id)))
            {
                results[itemSet.name] = itemSet;
            }
        }

        return results;
    }

    private static void SortItemSetsByIdThenName(List<ItemSet> targetItems)
    {
        if (targetItems == null || targetItems.Count <= 1)
        {
            return;
        }

        targetItems.Sort((left, right) =>
        {
            int idCompare = left.id.CompareTo(right.id);
            return idCompare != 0
                ? idCompare
                : string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        });
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

    private static Dictionary<string, int> BuildPreservedItemIds(
        IReadOnlyList<string> itemNames,
        Dictionary<string, ItemSet> previousItemsByName)
    {
        Dictionary<string, int> results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (itemNames == null || previousItemsByName == null || previousItemsByName.Count == 0)
        {
            return results;
        }

        HashSet<int> usedIds = new HashSet<int>();
        for (int i = 0; i < itemNames.Count; i++)
        {
            string itemName = itemNames[i];
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

    private static Dictionary<string, List<string>> BuildItemFolderLookup()
    {
        Dictionary<string, List<string>> lookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string> itemFolders = CollectItemFolderPaths();
        for (int i = 0; i < itemFolders.Count; i++)
        {
            string itemFolder = itemFolders[i];
            if (string.IsNullOrWhiteSpace(itemFolder) || !ContainsItemAsset(itemFolder))
            {
                continue;
            }

            string itemName = ResolveItemName(itemFolder, Path.GetFileName(itemFolder));
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            if (!lookup.TryGetValue(itemName, out List<string> matchingFolders))
            {
                matchingFolders = new List<string>();
                lookup[itemName] = matchingFolders;
            }

            if (!matchingFolders.Contains(itemFolder))
            {
                matchingFolders.Add(itemFolder);
            }
        }

        List<string> itemNamesWithoutIcons = null;
        foreach (KeyValuePair<string, List<string>> entry in lookup)
        {
            if (ContainsExplicitItemIcon(entry.Value))
            {
                continue;
            }

            itemNamesWithoutIcons ??= new List<string>();
            itemNamesWithoutIcons.Add(entry.Key);
        }

        if (itemNamesWithoutIcons != null)
        {
            for (int i = 0; i < itemNamesWithoutIcons.Count; i++)
            {
                lookup.Remove(itemNamesWithoutIcons[i]);
            }
        }

        return lookup;
    }

    private static bool ContainsItemAsset(string itemFolder)
    {
        if (string.IsNullOrWhiteSpace(itemFolder) || !AssetDatabase.IsValidFolder(itemFolder))
        {
            return false;
        }

        string absoluteFolder = Path.GetFullPath(itemFolder);
        if (!Directory.Exists(absoluteFolder))
        {
            return false;
        }

        foreach (string filePath in Directory.EnumerateFiles(absoluteFolder, "*", SearchOption.AllDirectories))
        {
            if (!filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsExplicitItemIcon(IReadOnlyList<string> itemFolders)
    {
        for (int i = 0; itemFolders != null && i < itemFolders.Count; i++)
        {
            string itemFolder = itemFolders[i];
            if (string.IsNullOrWhiteSpace(itemFolder) || !AssetDatabase.IsValidFolder(itemFolder))
            {
                continue;
            }

            for (int filterIndex = 0; filterIndex < ItemIconAssetFilters.Length; filterIndex++)
            {
                string[] iconGuids = AssetDatabase.FindAssets(ItemIconAssetFilters[filterIndex], new[] { itemFolder });
                for (int guidIndex = 0; guidIndex < iconGuids.Length; guidIndex++)
                {
                    string iconPath = AssetDatabase.GUIDToAssetPath(iconGuids[guidIndex]);
                    if (IsExplicitIconCandidate(iconPath) && !IsPortableBaseTextureCandidate(iconPath))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static List<string> GetItemFoldersForName(
        string itemName,
        Dictionary<string, List<string>> itemFolderLookup)
    {
        if (string.IsNullOrWhiteSpace(itemName) || itemFolderLookup == null)
        {
            return null;
        }

        return itemFolderLookup.TryGetValue(itemName, out List<string> folderPaths) ? folderPaths : null;
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

    private static MapObject FindMapObjectForItem(
        string itemName,
        IReadOnlyList<string> itemFolders)
    {
        for (int i = 0; itemFolders != null && i < itemFolders.Count; i++)
        {
            MapObject mapObject = FindMapObjectForItem(itemName, itemFolders[i]);
            if (mapObject != null)
            {
                return mapObject;
            }
        }

        return FindMapObjectForItem(itemName, string.Empty);
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

            // Portable mesh base textures are not UI icons. Letting a *_TB
            // texture through here also converts its importer to Sprite below.
            if (IsPortableBaseTextureCandidate(candidatePath))
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

        return score;
    }

    private static bool IsPortableBaseTextureCandidate(string assetPath)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return false;
        }

        string lowerFileName = fileNameWithoutExtension.ToLower(CultureInfo.InvariantCulture);
        return lowerFileName.EndsWith("_tb", StringComparison.Ordinal)
               || lowerFileName.EndsWith("-tb", StringComparison.Ordinal)
               || lowerFileName.EndsWith(" tb", StringComparison.Ordinal);
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
        Mesh resolvedPortableMesh = FindExactPortableMeshInItemDirectory(portableFolder, itemName, prefabRoot)
                                    ?? FindPortableMesh(portableFolder, prefabRoot);
        if (resolvedPortableMesh != null)
        {
            portableMesh = resolvedPortableMesh;
        }
        else if (portableMesh == null)
        {
            Mesh packageMesh = LoadPackagePortableMesh();
            if (packageMesh != null)
            {
                portableMesh = packageMesh;
            }
        }

        Material resolvedPortableMaterial = FindExactPortableMaterialInItemDirectory(portableFolder, itemName, prefabRoot)
                                            ?? FindPortableMaterial(portableFolder, prefabRoot);
        if (resolvedPortableMaterial != null)
        {
            portableMaterial = resolvedPortableMaterial;
        }
        else if (portableMaterial == null)
        {
            Material packageMaterial = LoadPackagePortableMaterial();
            if (packageMaterial != null)
            {
                portableMaterial = packageMaterial;
            }
        }
    }

    private static void TryOverridePortableAssets(
        string itemName,
        string itemFolder,
        GameObject prefabRoot,
        ref Mesh portableMesh,
        ref Material portableMaterial)
    {
        TryOverrideInstallationPortableAssets(
            itemName,
            itemFolder,
            prefabRoot,
            ref portableMesh,
            ref portableMaterial);

        TryOverrideAnimalMeatPortableMesh(itemName, ref portableMesh);
        TryOverrideWheelPortableAssets(itemName, ref portableMesh, ref portableMaterial);
    }

    private static void TryOverrideAnimalMeatPortableMesh(string itemName, ref Mesh portableMesh)
    {
        string itemKey = NormalizePortableLookupName(itemName);
        if (itemKey != "beef"
            && itemKey != "beefsteak"
            && itemKey != "pork"
            && itemKey != "porksteak")
        {
            return;
        }

        Mesh sharedMeatMesh = AssetDatabase.LoadAssetAtPath<Mesh>(SharedAnimalMeatPortableMeshPath);
        if (sharedMeatMesh != null)
        {
            portableMesh = sharedMeatMesh;
            return;
        }

        Debug.LogWarning(
            $"ItemManager: Animal meat portable mesh was not found at '{SharedAnimalMeatPortableMeshPath}'.");
    }

    private static void TryOverrideWheelPortableAssets(
        string itemName,
        ref Mesh portableMesh,
        ref Material portableMaterial)
    {
        string itemKey = NormalizePortableLookupName(itemName);
        string materialPath;
        if (itemKey == "ironwheel")
        {
            materialPath = IronWheelPortableMaterialPath;
        }
        else if (itemKey == "woodenwheel")
        {
            materialPath = WoodenWheelPortableMaterialPath;
        }
        else
        {
            return;
        }

        Mesh wheelMesh = AssetDatabase.LoadAssetAtPath<Mesh>(SharedWheelPortableMeshPath);
        Material wheelMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (wheelMesh != null)
        {
            portableMesh = wheelMesh;
        }
        else
        {
            Debug.LogWarning($"ItemManager: Wheel portable mesh was not found at '{SharedWheelPortableMeshPath}'.");
        }

        if (wheelMaterial != null)
        {
            portableMaterial = wheelMaterial;
        }
        else
        {
            Debug.LogWarning($"ItemManager: Wheel portable material was not found at '{materialPath}'.");
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

    private static Mesh FindExactPortableMeshInItemDirectory(string itemDirectory, string itemName, GameObject prefabRoot)
    {
        if (string.IsNullOrWhiteSpace(itemDirectory) || !AssetDatabase.IsValidFolder(itemDirectory))
        {
            return null;
        }

        string[] extensions = { ".mesh", ".asset", ".fbx" };
        List<string> baseNames = BuildExactPortableAssetBaseNames(itemName, prefabRoot);
        for (int nameIndex = 0; nameIndex < baseNames.Count; nameIndex++)
        {
            string baseName = baseNames[nameIndex];
            for (int extensionIndex = 0; extensionIndex < extensions.Length; extensionIndex++)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    $"{itemDirectory}/{baseName}{extensions[extensionIndex]}");
                if (mesh != null && IsPortableMeshName(mesh.name))
                {
                    return mesh;
                }
            }
        }

        return null;
    }

    private static Material FindPortableMaterialInItemDirectory(string itemDirectory, string itemKey)
    {
        if (string.IsNullOrWhiteSpace(itemDirectory) || !AssetDatabase.IsValidFolder(itemDirectory))
        {
            return null;
        }

        return FindBestPortableMaterialByGuidSearch(new[] { itemDirectory }, itemKey);
    }

    private static Material FindExactPortableMaterialInItemDirectory(string itemDirectory, string itemName, GameObject prefabRoot)
    {
        if (string.IsNullOrWhiteSpace(itemDirectory) || !AssetDatabase.IsValidFolder(itemDirectory))
        {
            return null;
        }

        List<string> baseNames = BuildExactPortableAssetBaseNames(itemName, prefabRoot);
        for (int nameIndex = 0; nameIndex < baseNames.Count; nameIndex++)
        {
            string baseName = baseNames[nameIndex];
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"{itemDirectory}/M_{baseName}.mat");
            if (material != null && IsPortableName(material.name))
            {
                return material;
            }

            material = AssetDatabase.LoadAssetAtPath<Material>($"{itemDirectory}/{baseName}.mat");
            if (material != null && IsPortableName(material.name))
            {
                return material;
            }
        }

        return null;
    }

    private static List<string> BuildExactPortableAssetBaseNames(string itemName, GameObject prefabRoot)
    {
        List<string> baseNames = new List<string>();
        AddPortableAssetBaseName(baseNames, itemName);
        AddPortableAssetBaseName(baseNames, prefabRoot != null ? prefabRoot.name : null);
        return baseNames;
    }

    private static void AddPortableAssetBaseName(List<string> baseNames, string rawName)
    {
        if (baseNames == null || string.IsNullOrWhiteSpace(rawName))
        {
            return;
        }

        string trimmedName = rawName.Trim();
        string portableName = trimmedName.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            ? trimmedName
            : $"{trimmedName}_P";
        if (!baseNames.Contains(portableName))
        {
            baseNames.Add(portableName);
        }
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
        ApplyItemIdsToPrefabs(null);
    }

    public void ApplyItemIdsToPrefabs(Action<string, float> reportProgress)
    {
        if (items == null)
        {
            ReportRebuildProgress(reportProgress, "Prefab ID 반영 완료", 1f);
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            ItemSet itemSet = items[i];
            string itemName = string.IsNullOrWhiteSpace(itemSet.name)
                ? $"ID {itemSet.id}"
                : itemSet.name;
            ReportRebuildProgress(
                reportProgress,
                $"Prefab ID 반영 중 ({i + 1}/{items.Count}): {itemName}",
                items.Count > 0 ? (float)i / items.Count : 1f);
            ApplyItemIdToPrefab(itemSet);
        }

        ReportRebuildProgress(reportProgress, "Prefab 변경사항 저장 중...", 0.98f);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ReportRebuildProgress(reportProgress, "Prefab ID 반영 완료", 1f);
    }

    private static void ApplyItemIdToPrefab(ItemSet itemSet)
    {
        PropObj prefab = itemSet.prefab;
        if (prefab == null)
        {
            return;
        }

        SerializedObject serializedPrefab = new SerializedObject(prefab);
        SerializedProperty objIdProperty = serializedPrefab.FindProperty("objId");
        if (objIdProperty == null)
        {
            return;
        }

        objIdProperty.intValue = itemSet.id;
        serializedPrefab.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefabRoot = prefab.gameObject;
        PrefabUtility.SavePrefabAsset(prefabRoot);
        EditorUtility.SetDirty(prefabRoot);
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

    private void RecreateItemDefinitionsFromItems(
        List<ItemSet> sourceItems,
        Dictionary<string, List<string>> itemFolderLookup = null,
        Action<string, float> reportProgress = null)
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

        itemFolderLookup ??= BuildItemFolderLookup();
        List<ItemDefinition> existingDefinitions = CollectExistingItemDefinitions(targetDirectory);
        List<ItemDefinition> rebuiltDefinitions = new List<ItemDefinition>();
        HashSet<ItemDefinition> usedDefinitions = new HashSet<ItemDefinition>();
        HashSet<string> rebuiltItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < sourceItems.Count; i++)
        {
            ItemSet itemSet = sourceItems[i];
            string progressItemName = string.IsNullOrWhiteSpace(itemSet.name)
                ? $"ID {itemSet.id}"
                : itemSet.name;
            ReportRebuildProgress(
                reportProgress,
                $"ItemDefinition 연결 중 ({i + 1}/{sourceItems.Count}): {progressItemName}",
                sourceItems.Count > 0 ? (float)i / sourceItems.Count : 1f);
            if (itemSet.id < 0)
            {
                continue;
            }

            string itemName = itemSet.name?.Trim();
            if (!string.IsNullOrWhiteSpace(itemName) && !rebuiltItemNames.Add(itemName))
            {
                Debug.LogWarning(
                    $"ItemManager: Skipped duplicate rebuild entry for '{itemName}' (id {itemSet.id}).");
                continue;
            }

            ItemDefinition definition = ResolveExistingItemDefinitionForRebuild(
                itemSet,
                targetDirectory,
                existingDefinitions,
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

            ApplyItemSetToDefinition(itemSet, definition, itemFolderLookup);
            rebuiltDefinitions.Add(definition);
            usedDefinitions.Add(definition);
        }

        ReportRebuildProgress(reportProgress, "ItemDefinition 목록 정리 중...", 0.96f);

        itemDefinitions.Clear();
        itemDefinitions.AddRange(rebuiltDefinitions);
        SortItemDefinitionsById(itemDefinitions);
        DeleteUnusedGeneratedItemDefinitions(targetDirectory, existingDefinitions, usedDefinitions);
        MarkEditorDirty();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ReportRebuildProgress(reportProgress, "ItemDefinition 연결 완료", 1f);
    }

    private static void ApplyItemSetToDefinition(
        ItemSet itemSet,
        ItemDefinition definition,
        Dictionary<string, List<string>> itemFolderLookup)
    {
        if (definition == null)
        {
            return;
        }

        List<string> itemFolders = GetItemFoldersForName(itemSet.name, itemFolderLookup);
        definition.id = itemSet.id;
        definition.itemName = itemSet.name;
        definition.mapObject = FindMapObjectForItem(itemSet.name, itemFolders);

        Mesh definitionPortableMesh = itemSet.portableMesh;
        Material definitionPortableMaterial = itemSet.portableMat;
        GameObject mapObjectRoot = GetMapObjectPrefabRoot(definition.mapObject);
        if (itemFolders == null || itemFolders.Count == 0)
        {
            TryOverridePortableAssets(
                itemSet.name,
                string.Empty,
                mapObjectRoot,
                ref definitionPortableMesh,
                ref definitionPortableMaterial);
        }
        else
        {
            for (int i = 0; i < itemFolders.Count; i++)
            {
                TryOverridePortableAssets(
                    itemSet.name,
                    itemFolders[i],
                    mapObjectRoot,
                    ref definitionPortableMesh,
                    ref definitionPortableMaterial);
            }
        }
        definition.portableMesh = definitionPortableMesh;
        definition.portableMat = definitionPortableMaterial;
        definition.icon = itemSet.icon;
        definition.size = (uint)Mathf.Max(0, itemSet.size);

        BindMapObjectDefinition(definition);
        TryBindItemDefinitionToPrefab(itemSet.prefab, definition);
        EditorUtility.SetDirty(definition);
    }

    private void RegisterRebuiltItemDefinition(ItemDefinition definition)
    {
        itemDefinitions ??= new List<ItemDefinition>();
        for (int i = 0; i < itemDefinitions.Count; i++)
        {
            ItemDefinition registeredDefinition = itemDefinitions[i];
            if (registeredDefinition == definition)
            {
                return;
            }

            if (registeredDefinition != null && registeredDefinition.id == definition.id)
            {
                itemDefinitions[i] = definition;
                return;
            }
        }

        itemDefinitions.Add(definition);
    }

    public void MarkEditorDirty()
    {
        EditorUtility.SetDirty(this);
        if (!Application.isPlaying && gameObject != null && gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
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
        string targetDirectory,
        List<ItemDefinition> existingDefinitions,
        HashSet<ItemDefinition> usedDefinitions)
    {
        if (existingDefinitions == null || existingDefinitions.Count == 0)
        {
            return null;
        }

        ItemDefinition bestDefinition = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < existingDefinitions.Count; i++)
        {
            ItemDefinition candidate = existingDefinitions[i];
            int candidateScore = ScoreExistingItemDefinitionForRebuild(
                candidate,
                itemSet,
                targetDirectory,
                usedDefinitions);
            if (candidateScore > bestScore)
            {
                bestDefinition = candidate;
                bestScore = candidateScore;
            }
        }

        return bestScore > int.MinValue ? bestDefinition : null;
    }

    private static int ScoreExistingItemDefinitionForRebuild(
        ItemDefinition definition,
        ItemSet itemSet,
        string targetDirectory,
        HashSet<ItemDefinition> usedDefinitions)
    {
        if (definition == null || (usedDefinitions != null && usedDefinitions.Contains(definition)))
        {
            return int.MinValue;
        }

        string itemName = itemSet.name?.Trim();
        string definitionName = GetItemDefinitionLookupName(definition);
        bool idMatches = definition.id == itemSet.id;
        bool nameMatches = !string.IsNullOrWhiteSpace(itemName)
                           && !string.IsNullOrWhiteSpace(definitionName)
                           && string.Equals(definitionName, itemName, StringComparison.OrdinalIgnoreCase);
        // IDs are reordered editor data, not stable asset identities. Reusing a
        // differently named definition by ID transfers all definition-only data
        // (energy, filters, light settings, etc.) to the wrong item.
        if (!nameMatches)
        {
            return int.MinValue;
        }

        string assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(definition));
        int score = 0;
        if (idMatches && nameMatches)
        {
            score += 1000;
        }
        else if (nameMatches)
        {
            score += 700;
        }
        if (IsExpectedItemDefinitionAssetPath(assetPath, targetDirectory, itemSet.id, itemName))
        {
            score += 2000;
        }
        else if (IsGeneratedItemDefinitionAssetPath(assetPath, targetDirectory))
        {
            score += 100;
        }

        if (IsNumberedDuplicateAssetPath(assetPath))
        {
            score -= 500;
        }

        if (IsBlankGeneratedItemDefinition(definition))
        {
            score -= 1000;
        }

        return score;
    }

    private static void DeleteUnusedGeneratedItemDefinitions(
        string targetDirectory,
        List<ItemDefinition> existingDefinitions,
        HashSet<ItemDefinition> usedDefinitions)
    {
        if (existingDefinitions == null || existingDefinitions.Count == 0)
        {
            return;
        }

        HashSet<string> deletedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < existingDefinitions.Count; i++)
        {
            ItemDefinition definition = existingDefinitions[i];
            if (definition == null || usedDefinitions.Contains(definition))
            {
                continue;
            }

            string assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(definition));
            if (!IsGeneratedItemDefinitionAssetPath(assetPath, targetDirectory)
                || !deletedAssetPaths.Add(assetPath)
                || !ShouldDeleteUnusedGeneratedItemDefinition(definition, usedDefinitions))
            {
                continue;
            }

            ItemDefinition replacementDefinition = FindUsedDefinitionWithSameIdentity(definition, usedDefinitions);
            if (replacementDefinition != null)
            {
                ReplaceItemDefinitionReferencesInPrefabs(definition, replacementDefinition);
            }

            if (!AssetDatabase.DeleteAsset(assetPath))
            {
                Debug.LogWarning($"ItemManager: Failed to delete duplicate item definition at '{assetPath}'.");
            }
        }
    }

    private static bool ShouldDeleteUnusedGeneratedItemDefinition(
        ItemDefinition definition,
        HashSet<ItemDefinition> usedDefinitions)
    {
        return IsBlankGeneratedItemDefinition(definition)
               || HasUsedDefinitionWithSameIdentity(definition, usedDefinitions);
    }

    private static bool HasUsedDefinitionWithSameIdentity(
        ItemDefinition definition,
        HashSet<ItemDefinition> usedDefinitions)
    {
        return FindUsedDefinitionWithSameIdentity(definition, usedDefinitions) != null;
    }

    private static ItemDefinition FindUsedDefinitionWithSameIdentity(
        ItemDefinition definition,
        HashSet<ItemDefinition> usedDefinitions)
    {
        if (definition == null || usedDefinitions == null)
        {
            return null;
        }

        string definitionName = definition.itemName?.Trim();
        foreach (ItemDefinition usedDefinition in usedDefinitions)
        {
            if (usedDefinition == null || usedDefinition == definition)
            {
                continue;
            }

            string usedName = usedDefinition.itemName?.Trim();
            bool sameName = !string.IsNullOrWhiteSpace(definitionName)
                            && !string.IsNullOrWhiteSpace(usedName)
                            && string.Equals(definitionName, usedName, StringComparison.OrdinalIgnoreCase);
            if (sameName)
            {
                return usedDefinition;
            }
        }

        return null;
    }

    private static void ReplaceItemDefinitionReferencesInPrefabs(
        ItemDefinition oldDefinition,
        ItemDefinition newDefinition)
    {
        if (oldDefinition == null || newDefinition == null || oldDefinition == newDefinition)
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                continue;
            }

            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabRoot == null)
            {
                continue;
            }

            bool changed = false;
            Component[] components = prefabRoot.GetComponentsInChildren<Component>(true);
            for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                changed |= ReplaceItemDefinitionReferences(components[componentIndex], oldDefinition, newDefinition);
            }

            if (!changed)
            {
                continue;
            }

            EditorUtility.SetDirty(prefabRoot);
            PrefabUtility.SavePrefabAsset(prefabRoot);
        }
    }

    private static bool ReplaceItemDefinitionReferences(
        UnityEngine.Object target,
        ItemDefinition oldDefinition,
        ItemDefinition newDefinition)
    {
        if (target == null || oldDefinition == null || newDefinition == null)
        {
            return false;
        }

        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.GetIterator();
        bool changed = false;
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.propertyType != SerializedPropertyType.ObjectReference
                || property.objectReferenceValue != oldDefinition)
            {
                continue;
            }

            property.objectReferenceValue = newDefinition;
            changed = true;
        }

        if (changed)
        {
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        return changed;
    }

    private static bool IsBlankGeneratedItemDefinition(ItemDefinition definition)
    {
        return definition != null
               && string.IsNullOrWhiteSpace(definition.itemName)
               && definition.id == 0
               && definition.mapObject == null
               && definition.portableMesh == null
               && definition.portableMat == null
               && definition.icon == null
               && (definition.interactionButtonList == null || definition.interactionButtonList.Count == 0);
    }

    private static bool IsExpectedItemDefinitionAssetPath(
        string assetPath,
        string targetDirectory,
        int itemId,
        string itemName)
    {
        return !string.IsNullOrWhiteSpace(assetPath)
               && string.Equals(
                   assetPath,
                   GetExpectedItemDefinitionAssetPath(targetDirectory, itemId, itemName),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExpectedItemDefinitionAssetPath(string targetDirectory, int itemId, string itemName)
    {
        string safeName = string.IsNullOrWhiteSpace(itemName) ? $"Item_{itemId}" : itemName;
        return $"{NormalizeAssetPath(targetDirectory)}/Item_{itemId}_{SanitizeAssetFileName(safeName)}.asset";
    }

    private static bool IsGeneratedItemDefinitionAssetPath(string assetPath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        string normalizedTargetDirectory = NormalizeAssetPath(targetDirectory);
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        return assetPath.StartsWith($"{normalizedTargetDirectory}/", StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetExtension(assetPath), ".asset", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(fileName)
               && fileName.StartsWith("Item_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumberedDuplicateAssetPath(string assetPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        return !string.IsNullOrWhiteSpace(fileName)
               && fileName.EndsWith(" 1", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : assetPath.Replace("\\", "/");
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
