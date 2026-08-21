using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class WaterBucketItemGenerator
{
    private const int EmptyBucketItemId = 74;
    private const int WaterBucketItemId = 75;
    private const string EmptyBucketName = "Bucket";
    private const string WaterBucketName = "Water Bucket";
    private const string EmptyDefinitionPath = "Assets/Data/Items/Item_74_Bucket.asset";
    private const string WaterDefinitionPath = "Assets/Data/Items/Item_75_Water_Bucket.asset";
    private const string EmptyPrefabPath = "Assets/MapObject/Fluid/Bucket/Bucket.prefab";
    private const string WaterPrefabPath = "Assets/MapObject/Fluid/Water Bucket/Water Bucket.prefab";
    private const string WaterIconPath = "Assets/Items/Fluid/Water Bucket/Water_Bucket_Icon.png";
    private const string WaterSurfaceMeshPath = "Assets/Items/Fluid/Bucket/Bucket_Water_Surface.mesh";

    [MenuItem("Tools/ProjectF/Generate Water Bucket Item")]
    public static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Water Bucket: exit Play Mode before generating item assets.");
            return;
        }

        AssetDatabase.Refresh();
        ItemDefinition emptyDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(EmptyDefinitionPath);
        if (emptyDefinition == null)
        {
            throw new InvalidOperationException($"Water Bucket: missing source definition at {EmptyDefinitionPath}.");
        }

        EnsureItemIdAvailable();
        ConfigurePortableIcon();
        Mesh waterSurfaceMesh = EnsureWaterSurfaceMesh();
        ConfigureDefinition(emptyDefinition, EmptyBucketItemId, EmptyBucketName);
        ConfigureBucketPrefab(
            EmptyPrefabPath,
            emptyDefinition,
            EmptyBucketItemId,
            EmptyBucketName,
            false,
            waterSurfaceMesh);

        ItemDefinition waterDefinition = LoadOrCreateWaterDefinition();
        CopyDefinitionDefaults(emptyDefinition, waterDefinition);
        ConfigureDefinition(waterDefinition, WaterBucketItemId, WaterBucketName);

        if (AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefabPath) == null
            && !AssetDatabase.CopyAsset(EmptyPrefabPath, WaterPrefabPath))
        {
            throw new InvalidOperationException($"Water Bucket: failed to create prefab at {WaterPrefabPath}.");
        }

        ConfigureBucketPrefab(
            WaterPrefabPath,
            waterDefinition,
            WaterBucketItemId,
            WaterBucketName,
            true,
            waterSurfaceMesh);
        GameObject waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefabPath);
        Bucket waterBucket = waterPrefab != null
            ? waterPrefab.GetComponentInChildren<Bucket>(true)
            : null;
        if (waterBucket == null)
        {
            throw new InvalidOperationException("Water Bucket: generated prefab has no Bucket component.");
        }

        waterDefinition.mapObject = waterBucket;
        waterDefinition.icon = AssetDatabase.LoadAssetAtPath<Sprite>(WaterIconPath);
        EditorUtility.SetDirty(waterDefinition);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RebuildLoadedItemManagers();
        // ItemManager의 폴더 기반 재생성은 "Water Bucket"을 일반 Package 자산으로
        // 추론할 수 있으므로, 진실 원본인 ItemDefinition에 Bucket 전용 자산을 다시 확정한다.
        CopyDefinitionDefaults(emptyDefinition, waterDefinition);
        ConfigureDefinition(waterDefinition, WaterBucketItemId, WaterBucketName);
        waterDefinition.mapObject = waterBucket;
        waterDefinition.icon = AssetDatabase.LoadAssetAtPath<Sprite>(WaterIconPath);
        EditorUtility.SetDirty(waterDefinition);
        AssetDatabase.SaveAssets();
        ValidateGeneratedAssets();
        RefreshOpenWaterBucketPrefabStage(waterSurfaceMesh);
        Debug.Log("Water Bucket: generated item ID 75. Bucket and Water Bucket now use normal stack rules without portable fluid gauges.");
    }

    [MenuItem("Tools/ProjectF/Generate Water Bucket Item", true)]
    private static bool CanGenerate()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem("Tools/ProjectF/Validation/Water Bucket Item")]
    public static void ValidateGeneratedAssets()
    {
        ItemDefinition emptyDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(EmptyDefinitionPath);
        ItemDefinition waterDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WaterDefinitionPath);
        ValidateDefinition(emptyDefinition, EmptyBucketItemId, EmptyBucketName);
        ValidateDefinition(waterDefinition, WaterBucketItemId, WaterBucketName);

        if (waterDefinition.icon == null)
        {
            throw new InvalidOperationException("Water Bucket validation failed: icon is missing.");
        }

        if (emptyDefinition.portableMesh != waterDefinition.portableMesh
            || emptyDefinition.portableMat != waterDefinition.portableMat)
        {
            throw new InvalidOperationException("Water Bucket validation failed: portable model style differs from Bucket.");
        }

        ValidateInstalledWaterSurface(emptyDefinition, false);
        ValidateInstalledWaterSurface(waterDefinition, true);

        Debug.Log("Water Bucket validation passed: separate stackable items with portable and installed water visuals.");
    }

    private static ItemDefinition LoadOrCreateWaterDefinition()
    {
        ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WaterDefinitionPath);
        if (definition != null)
        {
            return definition;
        }

        definition = ScriptableObject.CreateInstance<ItemDefinition>();
        definition.name = "Item_75_Water_Bucket";
        AssetDatabase.CreateAsset(definition, WaterDefinitionPath);
        return definition;
    }

    private static void CopyDefinitionDefaults(ItemDefinition source, ItemDefinition target)
    {
        target.portableMesh = source.portableMesh;
        target.portableMat = source.portableMat;
        target.interactionButtonList = new List<Sprite>();
        target.lightMode = source.lightMode;
        target.lightRange = source.lightRange;
        target.lightIntensityMultiplier = source.lightIntensityMultiplier;
        target.size = source.size;
        target.itemFilter = source.itemFilter;
        target.ignoreFilter = source.ignoreFilter;
        target.isManual = false;
        target.manualTargetItem = null;
        target.upgradeable = source.upgradeable;
        target.capacity = Mathf.Max(1, source.capacity);
        target.energyType = source.energyType;
        target.energyAmount = source.energyAmount;
        target.useEnergyType = source.useEnergyType;
        target.useEnergyAmount = source.useEnergyAmount;
        target.completeEnergy = source.completeEnergy;
        target.utilityPoleConnectionRadius = source.utilityPoleConnectionRadius;
        target.utilityPoleSupplyRadius = source.utilityPoleSupplyRadius;
        target.SetCraftingDurationSeconds(source.CraftingDurationSeconds);
    }

    private static void ConfigureDefinition(
        ItemDefinition definition,
        int itemId,
        string itemName)
    {
        definition.id = itemId;
        definition.itemName = itemName;
        definition.oneItem = false;
        definition.storesFluid = false;
        definition.fluidStorageLiters = 0f;
        EditorUtility.SetDirty(definition);
    }

    private static void ConfigureBucketPrefab(
        string prefabPath,
        ItemDefinition definition,
        int itemId,
        string itemName,
        bool containsWater,
        Mesh waterSurfaceMesh)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Bucket bucket = contents.GetComponentInChildren<Bucket>(true);
            if (bucket == null)
            {
                throw new InvalidOperationException($"Water Bucket: no Bucket component in {prefabPath}.");
            }

            SerializedObject serializedBucket = new SerializedObject(bucket);
            serializedBucket.FindProperty("objectName").stringValue = itemName;
            serializedBucket.FindProperty("objId").intValue = itemId;
            serializedBucket.FindProperty("itemDefinition").objectReferenceValue = definition;
            serializedBucket.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInstalledWaterSurface(bucket, containsWater, waterSurfaceMesh);
            contents.name = itemName;
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static Mesh EnsureWaterSurfaceMesh()
    {
        Mesh generatedMesh = PortableBucketWaterVisual.CreateCircleMesh("Bucket_Water_Surface");
        Mesh meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(WaterSurfaceMeshPath);
        if (meshAsset == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, WaterSurfaceMeshPath);
            return generatedMesh;
        }

        EditorUtility.CopySerialized(generatedMesh, meshAsset);
        UnityEngine.Object.DestroyImmediate(generatedMesh);
        EditorUtility.SetDirty(meshAsset);
        return meshAsset;
    }

    private static void ConfigureInstalledWaterSurface(
        Bucket bucket,
        bool containsWater,
        Mesh waterSurfaceMesh)
    {
        MeshFilter body = ResolveBucketBody(bucket);
        if (body == null)
        {
            throw new InvalidOperationException("Water Bucket: installation body mesh is missing.");
        }

        Transform existingSurface = body.transform.Find(PortableBucketWaterVisual.SurfaceObjectName);
        if (!containsWater)
        {
            if (existingSurface != null)
            {
                UnityEngine.Object.DestroyImmediate(existingSurface.gameObject);
            }

            return;
        }

        GameObject surfaceObject = existingSurface != null
            ? existingSurface.gameObject
            : new GameObject(PortableBucketWaterVisual.SurfaceObjectName);
        Transform surfaceTransform = surfaceObject.transform;
        if (surfaceTransform.parent != body.transform)
        {
            surfaceTransform.SetParent(body.transform, false);
        }

        surfaceObject.layer = body.gameObject.layer;

        MeshFilter surfaceFilter = surfaceObject.GetComponent<MeshFilter>();
        if (surfaceFilter == null)
        {
            surfaceFilter = surfaceObject.AddComponent<MeshFilter>();
        }
        surfaceFilter.sharedMesh = waterSurfaceMesh;

        MeshRenderer surfaceRenderer = surfaceObject.GetComponent<MeshRenderer>();
        if (surfaceRenderer == null)
        {
            surfaceRenderer = surfaceObject.AddComponent<MeshRenderer>();
        }
        surfaceRenderer.sharedMaterial = bucket.PortableWaterSurfaceMaterial;
        surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        surfaceRenderer.receiveShadows = false;
        surfaceRenderer.lightProbeUsage = LightProbeUsage.Off;
        surfaceRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        surfaceRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private static MeshFilter ResolveBucketBody(Bucket bucket)
    {
        if (bucket == null)
        {
            return null;
        }

        MeshFilter[] meshFilters = bucket.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter != null
                && meshFilter.sharedMesh != null
                && !string.Equals(
                    meshFilter.gameObject.name,
                    PortableBucketWaterVisual.SurfaceObjectName,
                    StringComparison.Ordinal))
            {
                return meshFilter;
            }
        }

        return null;
    }

    private static void ValidateInstalledWaterSurface(ItemDefinition definition, bool expectedVisible)
    {
        Bucket bucket = definition != null ? definition.mapObject as Bucket : null;
        MeshFilter body = ResolveBucketBody(bucket);
        Transform surface = body != null
            ? body.transform.Find(PortableBucketWaterVisual.SurfaceObjectName)
            : null;
        MeshFilter surfaceFilter = surface != null ? surface.GetComponent<MeshFilter>() : null;
        MeshRenderer surfaceRenderer = surface != null ? surface.GetComponent<MeshRenderer>() : null;
        bool isConfigured = surfaceFilter != null
                            && surfaceFilter.sharedMesh != null
                            && surfaceRenderer != null
                            && surfaceRenderer.sharedMaterial == bucket.PortableWaterSurfaceMaterial;
        if (expectedVisible != isConfigured)
        {
            throw new InvalidOperationException(
                $"Water Bucket validation failed: installed water surface state for {definition?.itemName} is invalid.");
        }
    }

    private static void RefreshOpenWaterBucketPrefabStage(Mesh waterSurfaceMesh)
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage == null
            || !string.Equals(prefabStage.assetPath, WaterPrefabPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Bucket bucket = prefabStage.prefabContentsRoot != null
            ? prefabStage.prefabContentsRoot.GetComponentInChildren<Bucket>(true)
            : null;
        if (bucket == null)
        {
            return;
        }

        ConfigureInstalledWaterSurface(bucket, true, waterSurfaceMesh);
        EditorSceneManager.MarkSceneDirty(prefabStage.scene);
        SceneView.RepaintAll();
    }

    private static void ConfigurePortableIcon()
    {
        TextureImporter importer = AssetImporter.GetAtPath(WaterIconPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"Water Bucket: missing icon at {WaterIconPath}.");
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static void EnsureItemIdAvailable()
    {
        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data/Items" });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition != null
                && definition.id == WaterBucketItemId
                && !string.Equals(assetPath, WaterDefinitionPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Water Bucket: item ID {WaterBucketItemId} is already used by {assetPath}.");
            }
        }
    }

    private static void RebuildLoadedItemManagers()
    {
        ItemManager[] itemManagers = UnityEngine.Object.FindObjectsByType<ItemManager>(FindObjectsInactive.Include);
        for (int i = 0; i < itemManagers.Length; i++)
        {
            ItemManager itemManager = itemManagers[i];
            if (itemManager == null)
            {
                continue;
            }

            itemManager.RebuildItemDefinitionsFromAssets();
            itemManager.ApplyItemIdsToPrefabs();
            itemManager.MarkEditorDirty();
            if (itemManager.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(itemManager.gameObject.scene);
            }
        }
    }

    private static void ValidateDefinition(
        ItemDefinition definition,
        int expectedItemId,
        string expectedName)
    {
        if (definition == null
            || definition.id != expectedItemId
            || !string.Equals(definition.itemName, expectedName, StringComparison.Ordinal)
            || definition.oneItem
            || definition.storesFluid
            || definition.fluidStorageLiters > 0f
            || !(definition.mapObject is Bucket))
        {
            throw new InvalidOperationException($"Water Bucket validation failed for {expectedName}.");
        }
    }
}
