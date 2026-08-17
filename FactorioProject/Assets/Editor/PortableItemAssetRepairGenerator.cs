using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class PortableItemAssetRepairGenerator
{
    private const string ItemDefinitionFolder = "Assets/Data/Items";
    private const string OutputFolder = "Assets/Items/_GeneratedPortable";
    private const string FallbackMeshPath = OutputFolder + "/PortableFallback_P.mesh";
    private const float HalfWidth = 0.08f;
    private const float HalfDepth = 0.06f;
    private const float HalfThickness = 0.006f;

    [MenuItem("Tools/ProjectF/Repair Missing Portable Item Assets")]
    public static void RepairMissingPortableItemAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Portable Item Repair: exit Play Mode before repairing assets.");
            return;
        }

        EnsureAssetFolder(OutputFolder);
        Mesh fallbackMesh = CreateOrUpdateFallbackMesh();
        List<ItemDefinition> repairedDefinitions = new List<ItemDefinition>();
        string[] definitionGuids = AssetDatabase.FindAssets(
            "t:ItemDefinition",
            new[] { ItemDefinitionFolder });

        for (int i = 0; i < definitionGuids.Length; i++)
        {
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                AssetDatabase.GUIDToAssetPath(definitionGuids[i]));
            if (definition == null || IsNonPhysicalItem(definition))
            {
                continue;
            }

            Mesh resolvedMesh = definition.portableMesh != null
                ? definition.portableMesh
                : fallbackMesh;
            Material resolvedMaterial = definition.portableMat != null
                ? definition.portableMat
                : CreateOrUpdateIconMaterial(definition);
            if (definition.portableMesh == resolvedMesh && definition.portableMat == resolvedMaterial)
            {
                continue;
            }

            Undo.RecordObject(definition, "Repair Portable Item Assets");
            definition.portableMesh = resolvedMesh;
            definition.portableMat = resolvedMaterial;
            EditorUtility.SetDirty(definition);
            repairedDefinitions.Add(definition);
        }

        int synchronizedSceneItems = SynchronizeLoadedItemManagers();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();

        int remainingMissingCount = CountMissingPhysicalPortableAssets();
        if (repairedDefinitions.Count > 0)
        {
            Selection.activeObject = repairedDefinitions[0];
        }

        if (remainingMissingCount > 0)
        {
            Debug.LogError(
                $"Portable Item Repair: {remainingMissingCount} physical ItemDefinition(s) still have missing assets.");
            return;
        }

        Debug.Log(
            $"Portable Item Repair: repaired {repairedDefinitions.Count} ItemDefinition(s), synchronized "
            + $"{synchronizedSceneItems} loaded scene item(s), and verified all physical portable assets.");
    }

    [MenuItem("Tools/ProjectF/Repair Missing Portable Item Assets", true)]
    private static bool CanRepairMissingPortableItemAssets()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static Mesh CreateOrUpdateFallbackMesh()
    {
        Mesh generatedMesh = BuildFallbackMesh();
        Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(FallbackMeshPath);
        if (assetMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, FallbackMeshPath);
            return generatedMesh;
        }

        EditorUtility.CopySerialized(generatedMesh, assetMesh);
        UnityEngine.Object.DestroyImmediate(generatedMesh);
        EditorUtility.SetDirty(assetMesh);
        return assetMesh;
    }

    private static Mesh BuildFallbackMesh()
    {
        List<Vector3> vertices = new List<Vector3>(24);
        List<Vector2> uv = new List<Vector2>(24);
        List<int> triangles = new List<int>(36);

        AddFace(vertices, uv, triangles,
            new Vector3(-HalfWidth, HalfThickness, -HalfDepth),
            new Vector3(-HalfWidth, HalfThickness, HalfDepth),
            new Vector3(HalfWidth, HalfThickness, HalfDepth),
            new Vector3(HalfWidth, HalfThickness, -HalfDepth));
        AddFace(vertices, uv, triangles,
            new Vector3(-HalfWidth, -HalfThickness, HalfDepth),
            new Vector3(-HalfWidth, -HalfThickness, -HalfDepth),
            new Vector3(HalfWidth, -HalfThickness, -HalfDepth),
            new Vector3(HalfWidth, -HalfThickness, HalfDepth));
        AddFace(vertices, uv, triangles,
            new Vector3(-HalfWidth, -HalfThickness, -HalfDepth),
            new Vector3(-HalfWidth, HalfThickness, -HalfDepth),
            new Vector3(HalfWidth, HalfThickness, -HalfDepth),
            new Vector3(HalfWidth, -HalfThickness, -HalfDepth));
        AddFace(vertices, uv, triangles,
            new Vector3(HalfWidth, -HalfThickness, HalfDepth),
            new Vector3(HalfWidth, HalfThickness, HalfDepth),
            new Vector3(-HalfWidth, HalfThickness, HalfDepth),
            new Vector3(-HalfWidth, -HalfThickness, HalfDepth));
        AddFace(vertices, uv, triangles,
            new Vector3(-HalfWidth, -HalfThickness, HalfDepth),
            new Vector3(-HalfWidth, HalfThickness, HalfDepth),
            new Vector3(-HalfWidth, HalfThickness, -HalfDepth),
            new Vector3(-HalfWidth, -HalfThickness, -HalfDepth));
        AddFace(vertices, uv, triangles,
            new Vector3(HalfWidth, -HalfThickness, -HalfDepth),
            new Vector3(HalfWidth, HalfThickness, -HalfDepth),
            new Vector3(HalfWidth, HalfThickness, HalfDepth),
            new Vector3(HalfWidth, -HalfThickness, HalfDepth));

        Mesh mesh = new Mesh
        {
            name = "PortableFallback_P"
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0, false);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddFace(
        List<Vector3> vertices,
        List<Vector2> uv,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        uv.Add(new Vector2(0f, 0f));
        uv.Add(new Vector2(0f, 1f));
        uv.Add(new Vector2(1f, 1f));
        uv.Add(new Vector2(1f, 0f));
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private static Material CreateOrUpdateIconMaterial(ItemDefinition definition)
    {
        string safeName = SanitizeFileName(
            string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName);
        string materialPath = $"{OutputFolder}/M_{definition.id}_{safeName}_P.mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException("Portable Item Repair: no supported Lit shader was found.");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = $"M_{definition.id}_{safeName}_P"
            };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            material.shader = shader;
        }

        Sprite icon = definition.icon;
        Texture2D iconTexture = icon != null ? icon.texture : null;
        material.mainTexture = iconTexture;
        material.color = Color.white;
        material.enableInstancing = true;
        material.mainTextureScale = Vector2.one;
        material.mainTextureOffset = Vector2.zero;
        if (iconTexture != null && icon != null)
        {
            Rect rect = icon.rect;
            material.mainTextureScale = new Vector2(
                rect.width / iconTexture.width,
                rect.height / iconTexture.height);
            material.mainTextureOffset = new Vector2(
                rect.x / iconTexture.width,
                rect.y / iconTexture.height);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", iconTexture);
            material.SetTextureScale("_BaseMap", material.mainTextureScale);
            material.SetTextureOffset("_BaseMap", material.mainTextureOffset);
        }

        SetFloatIfPresent(material, "_Metallic", 0f);
        SetFloatIfPresent(material, "_Smoothness", 0.18f);
        SetFloatIfPresent(material, "_Surface", 0f);
        SetFloatIfPresent(material, "_AlphaClip", 0f);
        SetFloatIfPresent(material, "_Cull", 2f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static int SynchronizeLoadedItemManagers()
    {
        Dictionary<int, ItemDefinition> definitionsById = BuildDefinitionLookupById();
        ItemManager[] itemManagers = UnityEngine.Object.FindObjectsByType<ItemManager>(
            FindObjectsInactive.Include);
        int synchronizedCount = 0;

        for (int managerIndex = 0; managerIndex < itemManagers.Length; managerIndex++)
        {
            ItemManager itemManager = itemManagers[managerIndex];
            List<ItemManager.ItemSet> itemSets = itemManager != null ? itemManager.ItemSets : null;
            if (itemSets == null)
            {
                continue;
            }

            bool recordedUndo = false;
            for (int itemIndex = 0; itemIndex < itemSets.Count; itemIndex++)
            {
                ItemManager.ItemSet itemSet = itemSets[itemIndex];
                if (!definitionsById.TryGetValue(itemSet.id, out ItemDefinition definition)
                    || definition == null
                    || (itemSet.portableMesh == definition.portableMesh
                        && itemSet.portableMat == definition.portableMat))
                {
                    continue;
                }

                if (!recordedUndo)
                {
                    Undo.RecordObject(itemManager, "Synchronize Portable Item Assets");
                    recordedUndo = true;
                }

                itemSet.portableMesh = definition.portableMesh;
                itemSet.portableMat = definition.portableMat;
                itemSets[itemIndex] = itemSet;
                synchronizedCount++;
            }

            if (!recordedUndo)
            {
                continue;
            }

            EditorUtility.SetDirty(itemManager);
            if (itemManager.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(itemManager.gameObject.scene);
            }
        }

        return synchronizedCount;
    }

    private static Dictionary<int, ItemDefinition> BuildDefinitionLookupById()
    {
        Dictionary<int, ItemDefinition> definitionsById = new Dictionary<int, ItemDefinition>();
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemDefinitionFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (definition != null && definition.id >= 0 && !definitionsById.ContainsKey(definition.id))
            {
                definitionsById.Add(definition.id, definition);
            }
        }

        return definitionsById;
    }

    private static int CountMissingPhysicalPortableAssets()
    {
        int missingCount = 0;
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemDefinitionFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (definition != null
                && !IsNonPhysicalItem(definition)
                && (definition.portableMesh == null || definition.portableMat == null))
            {
                missingCount++;
            }
        }

        return missingCount;
    }

    private static bool IsNonPhysicalItem(ItemDefinition definition)
    {
        string itemName = definition != null ? definition.itemName : string.Empty;
        return string.Equals(itemName, "Fire", StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Water", StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Steam", StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemName, "Electricity", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static string SanitizeFileName(string value)
    {
        string result = string.IsNullOrWhiteSpace(value) ? "Item" : value.Trim();
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidCharacters.Length; i++)
        {
            result = result.Replace(invalidCharacters[i], '_');
        }

        return result;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parentFolder = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrWhiteSpace(parentFolder) || string.IsNullOrWhiteSpace(folderName))
        {
            throw new InvalidOperationException("Portable Item Repair: invalid output folder " + folderPath + ".");
        }

        EnsureAssetFolder(parentFolder);
        AssetDatabase.CreateFolder(parentFolder, folderName);
    }
}
