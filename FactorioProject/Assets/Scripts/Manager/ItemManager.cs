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

    [SerializeField]
    private List<ItemSet> items;

    public List<ItemSet> ItemSets => items;

    public bool TryGetItemSetById(int id, out ItemSet itemSet)
    {
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

#if UNITY_EDITOR
    public void RebuildItemsFromAssets()
    {
        if (items == null)
        {
            items = new List<ItemSet>();
        }

        Dictionary<string, ItemSet> previousItemsByPath = new Dictionary<string, ItemSet>();
        HashSet<int> usedIds = new HashSet<int>();
        for (int i = 0; i < items.Count; i++)
        {
            ItemSet existingItem = items[i];
            if (existingItem.prefab != null)
            {
                string prefabPath = AssetDatabase.GetAssetPath(existingItem.prefab);
                if (!string.IsNullOrWhiteSpace(prefabPath))
                {
                    previousItemsByPath[prefabPath] = existingItem;
                }
            }

            if (existingItem.id >= 0)
            {
                usedIds.Add(existingItem.id);
            }
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Objects" });
        List<string> prefabPaths = new List<string>(prefabGuids.Length);
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            prefabPaths.Add(AssetDatabase.GUIDToAssetPath(prefabGuids[i]));
        }

        prefabPaths.Sort(StringComparer.OrdinalIgnoreCase);

        List<ItemSet> rebuiltItems = new List<ItemSet>();
        HashSet<int> assignedIds = new HashSet<int>();

        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string assetPath = prefabPaths[i];
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabRoot == null)
            {
                continue;
            }

            PropObj propObject = prefabRoot.GetComponent<PropObj>();
            if (propObject == null)
            {
                continue;
            }

            bool hasPreviousItem = previousItemsByPath.TryGetValue(assetPath, out ItemSet previousItem);
            Mesh portableMesh = hasPreviousItem ? previousItem.portableMesh : null;
            Material portableMaterial = hasPreviousItem ? previousItem.portableMat : null;

            TryResolvePortableAssets(assetPath, prefabRoot, ref portableMesh, ref portableMaterial);

            int itemId;
            if (hasPreviousItem && previousItem.id >= 0 && assignedIds.Add(previousItem.id))
            {
                itemId = previousItem.id;
            }
            else
            {
                itemId = GetNextAvailableId(usedIds);
                usedIds.Add(itemId);
                assignedIds.Add(itemId);
            }

            ItemSet itemSet = new ItemSet
            {
                id = itemId,
                name = hasPreviousItem && !string.IsNullOrWhiteSpace(previousItem.name)
                    ? previousItem.name
                    : (string.IsNullOrWhiteSpace(prefabRoot.name) ? $"Item {itemId}" : prefabRoot.name),
                prefab = propObject,
                portableMesh = portableMesh,
                portableMat = portableMaterial,
                icon = ResolveItemIcon(assetPath, prefabRoot.name, hasPreviousItem ? previousItem.icon : null),
                size = hasPreviousItem ? previousItem.size : 0
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

    private static Sprite ResolveItemIcon(string assetPath, string prefabName, Sprite fallbackIcon)
    {
        string prefabDirectory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(prefabDirectory))
        {
            return fallbackIcon;
        }

        List<string> searchDirectories = new List<string> { prefabDirectory };
        string parentDirectory = Path.GetDirectoryName(prefabDirectory)?.Replace("\\", "/");
        if (!string.IsNullOrWhiteSpace(parentDirectory) && !searchDirectories.Contains(parentDirectory))
        {
            searchDirectories.Add(parentDirectory);
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

        List<string> prefabAliases = BuildIconLookupAliases(prefabName);
        string bestPath = null;
        int bestScore = int.MinValue;
        bool foundExplicitIcon = false;

        foreach (string candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            bool isExplicitIcon = IsExplicitIconCandidate(candidatePath);
            if (foundExplicitIcon && !isExplicitIcon)
            {
                continue;
            }

            int score = ScoreIconCandidate(candidatePath, prefabAliases, prefabDirectory, isExplicitIcon);
            if (score > bestScore)
            {
                bestScore = score;
                bestPath = candidatePath;
                foundExplicitIcon = isExplicitIcon;
            }
        }

        if (string.IsNullOrWhiteSpace(bestPath) || bestScore <= int.MinValue / 2)
        {
            return fallbackIcon;
        }

        Sprite resolvedSprite = LoadOrConvertSprite(bestPath);
        return resolvedSprite != null ? resolvedSprite : fallbackIcon;
    }

    private static int ScoreIconCandidate(string assetPath, List<string> prefabAliases, string preferredDirectory, bool isExplicitIcon)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);
        string normalizedFileName = NormalizeItemLookupName(fileNameWithoutExtension);
        string lowerFileName = fileNameWithoutExtension.ToLower(CultureInfo.InvariantCulture);
        string normalizedPath = assetPath.Replace("\\", "/");

        int score = isExplicitIcon ? 10000 : 0;

        if (!string.IsNullOrWhiteSpace(preferredDirectory)
            && normalizedPath.StartsWith(preferredDirectory, StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
        }

        for (int i = 0; i < prefabAliases.Count; i++)
        {
            string alias = prefabAliases[i];
            if (string.IsNullOrEmpty(alias))
            {
                continue;
            }

            if (normalizedFileName == alias)
            {
                score += 700;
                continue;
            }

            if (normalizedFileName.StartsWith(alias, StringComparison.Ordinal))
            {
                score += 450;
                continue;
            }

            if (normalizedFileName.Contains(alias))
            {
                score += 250;
            }
        }

        if (isExplicitIcon)
        {
            score += 2000;
        }

        if (lowerFileName.Contains("_tb") || lowerFileName.EndsWith("tb"))
        {
            score += 200;
        }

        if (lowerFileName.Contains("item"))
        {
            score += 50;
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
        return lowerFileName.Contains("_icon") || lowerFileName.EndsWith("icon");
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

    private static void TryResolvePortableAssets(string assetPath, GameObject prefabRoot, ref Mesh portableMesh, ref Material portableMaterial)
    {
        if (prefabRoot == null)
        {
            return;
        }

        string prefabDirectory = System.IO.Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (!string.IsNullOrWhiteSpace(prefabDirectory))
        {
            Mesh explicitPortableMesh = FindPortableMesh(prefabDirectory, prefabRoot.name);

            if (explicitPortableMesh != null)
            {
                portableMesh = explicitPortableMesh;
            }

            Material explicitPortableMaterial = FindPortableMaterial(prefabDirectory, prefabRoot.name);
            if (explicitPortableMaterial != null)
            {
                portableMaterial = explicitPortableMaterial;
            }
        }

        MeshFilter[] meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
        MeshRenderer[] meshRenderers = prefabRoot.GetComponentsInChildren<MeshRenderer>(true);

        MeshFilter preferredMeshFilter = FindPreferredMeshFilter(meshFilters);
        if (preferredMeshFilter != null && preferredMeshFilter.sharedMesh != null)
        {
            portableMesh = preferredMeshFilter.sharedMesh;
        }

        MeshRenderer preferredMeshRenderer = FindPreferredMeshRenderer(meshRenderers);
        if (preferredMeshRenderer != null && preferredMeshRenderer.sharedMaterials != null)
        {
            for (int materialIndex = 0; materialIndex < preferredMeshRenderer.sharedMaterials.Length; materialIndex++)
            {
                Material sharedMaterial = preferredMeshRenderer.sharedMaterials[materialIndex];
                if (sharedMaterial == null)
                {
                    continue;
                }

                if (IsPortableName(preferredMeshRenderer.gameObject.name)
                    || IsPortableName(sharedMaterial.name))
                {
                    portableMaterial = sharedMaterial;
                    break;
                }
            }
        }
    }

    private static Mesh FindPortableMesh(string prefabDirectory, string prefabName)
    {
        string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { prefabDirectory });
        Mesh bestMesh = null;
        int bestScore = int.MinValue;
        string normalizedPrefabName = NormalizePortableLookupName(prefabName);

        for (int i = 0; i < guids.Length; i++)
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(candidatePath);
            if (mesh == null || !IsPortableMeshName(mesh.name))
            {
                continue;
            }

            int score = ScorePortableMeshCandidate(mesh.name, normalizedPrefabName);
            if (score > bestScore)
            {
                bestScore = score;
                bestMesh = mesh;
            }
        }

        if (bestMesh == null && prefabDirectory.Contains("/Objects/Ore"))
        {
            bestMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Objects/Ore/PortableMesh_Ore_P.mesh");
        }

        return bestMesh;
    }

    private static int ScorePortableMeshCandidate(string meshName, string normalizedPrefabName)
    {
        int score = 0;
        string lowerMeshName = meshName.ToLower(CultureInfo.InvariantCulture);
        string normalizedMeshName = NormalizePortableLookupName(meshName);

        if (lowerMeshName.Contains("portablemesh"))
        {
            score += 300;
        }

        if (lowerMeshName.Contains("portable"))
        {
            score += 200;
        }

        if (lowerMeshName.Contains("_p") || lowerMeshName.StartsWith("p_") || lowerMeshName.EndsWith("_p"))
        {
            score += 150;
        }

        if (!string.IsNullOrEmpty(normalizedPrefabName))
        {
            if (normalizedMeshName == normalizedPrefabName)
            {
                score += 200;
            }
            else if (normalizedMeshName.Contains(normalizedPrefabName))
            {
                score += 100;
            }
        }

        return score;
    }

    private static T FindAssetByName<T>(string directoryPath, string nameContains) where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name} {nameContains}", new[] { directoryPath });
        for (int i = 0; i < guids.Length; i++)
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
            T asset = AssetDatabase.LoadAssetAtPath<T>(candidatePath);
            if (asset != null && IsPortableName(asset.name))
            {
                return asset;
            }
        }

        return null;
    }

    private static Material FindPortableMaterial(string prefabDirectory, string prefabName)
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { prefabDirectory });
        Material fallbackPortableMaterial = null;

        string normalizedPrefabName = NormalizePortableLookupName(prefabName);
        for (int i = 0; i < guids.Length; i++)
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(candidatePath);
            if (material == null || !IsPortableName(material.name))
            {
                continue;
            }

            string normalizedMaterialName = NormalizePortableLookupName(material.name);
            if (!string.IsNullOrEmpty(normalizedPrefabName) && normalizedMaterialName.Contains(normalizedPrefabName))
            {
                return material;
            }

            fallbackPortableMaterial ??= material;
        }

        return fallbackPortableMaterial;
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
#endif
}
