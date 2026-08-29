using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class WaterBucketItemGenerator
{
    private const int EmptyBucketItemId = 87;
    private const int WaterBucketItemId = 88;
    private const int OilBucketItemId = 89;
    private const string EmptyBucketName = "Bucket";
    private const string WaterBucketName = "Water Bucket";
    private const string OilBucketName = "Oil Bucket";
    private const string EmptyDefinitionPath = "Assets/Data/Items/Item_74_Bucket.asset";
    private const string WaterDefinitionPath = "Assets/Data/Items/Item_75_Water_Bucket.asset";
    private const string OilDefinitionPath = "Assets/Data/Items/Item_89_Oil_Bucket.asset";
    private const string EmptyPrefabPath = "Assets/MapObject/Fluid/Bucket/Bucket.prefab";
    private const string WaterPrefabPath = "Assets/MapObject/Fluid/Water Bucket/Water Bucket.prefab";
    private const string OilPrefabFolder = "Assets/MapObject/Fluid/Oil Bucket";
    private const string OilPrefabPath = OilPrefabFolder + "/Oil Bucket.prefab";
    private const string WaterIconPath = "Assets/Items/Fluid/Water Bucket/Water Bucket_Icon.png";
    private const string OilIconPath = "Assets/Items/Fluid/Oil Bucket/Oil Bucket_Icon.png";
    private const string WaterSurfaceMeshPath = "Assets/Items/Fluid/Bucket/Bucket_Water_Surface.mesh";
    private const string WaterSurfaceMaterialPath = "Assets/MapObject/Fluid/Pipe/M_Fluid.mat";
    private const string OilSurfaceMaterialPath = OilPrefabFolder + "/M_Oil_Bucket_Surface.mat";

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

        EnsureItemIdAvailable(WaterBucketItemId, WaterDefinitionPath, WaterBucketName);
        ConfigurePortableIcon(WaterIconPath);
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

    [MenuItem("Tools/ProjectF/Generate Oil Bucket Item")]
    public static void GenerateOilBucket()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Oil Bucket: exit Play Mode before generating item assets.");
            return;
        }

        AssetDatabase.Refresh();
        ItemDefinition emptyDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(EmptyDefinitionPath);
        if (emptyDefinition == null)
        {
            throw new InvalidOperationException($"Oil Bucket: missing source definition at {EmptyDefinitionPath}.");
        }

        EnsureItemIdAvailable(OilBucketItemId, OilDefinitionPath, OilBucketName);
        ConfigurePortableIcon(OilIconPath);
        Mesh surfaceMesh = EnsureWaterSurfaceMesh();
        Material oilSurfaceMaterial = EnsureOilSurfaceMaterial();

        ConfigureBucketPrefab(
            EmptyPrefabPath,
            emptyDefinition,
            EmptyBucketItemId,
            EmptyBucketName,
            false,
            surfaceMesh,
            null,
            oilSurfaceMaterial);

        ItemDefinition oilDefinition = LoadOrCreateDefinition(
            OilDefinitionPath,
            "Item_89_Oil_Bucket");
        CopyDefinitionDefaults(emptyDefinition, oilDefinition);
        ConfigureDefinition(oilDefinition, OilBucketItemId, OilBucketName);

        EnsureAssetFolder(OilPrefabFolder);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(OilPrefabPath) == null
            && !AssetDatabase.CopyAsset(WaterPrefabPath, OilPrefabPath))
        {
            throw new InvalidOperationException($"Oil Bucket: failed to create prefab at {OilPrefabPath}.");
        }

        ConfigureBucketPrefab(
            OilPrefabPath,
            oilDefinition,
            OilBucketItemId,
            OilBucketName,
            true,
            surfaceMesh,
            oilSurfaceMaterial,
            oilSurfaceMaterial);
        GameObject oilPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OilPrefabPath);
        Bucket oilBucket = oilPrefab != null
            ? oilPrefab.GetComponentInChildren<Bucket>(true)
            : null;
        if (oilBucket == null)
        {
            throw new InvalidOperationException("Oil Bucket: generated prefab has no Bucket component.");
        }

        oilDefinition.mapObject = oilBucket;
        oilDefinition.icon = AssetDatabase.LoadAssetAtPath<Sprite>(OilIconPath);
        EditorUtility.SetDirty(oilDefinition);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RebuildLoadedItemManagers();
        CopyDefinitionDefaults(emptyDefinition, oilDefinition);
        ConfigureDefinition(oilDefinition, OilBucketItemId, OilBucketName);
        oilDefinition.mapObject = oilBucket;
        oilDefinition.icon = AssetDatabase.LoadAssetAtPath<Sprite>(OilIconPath);
        EditorUtility.SetDirty(oilDefinition);
        AssetDatabase.SaveAssets();
        ValidateOilBucketAssets();
        Debug.Log("Oil Bucket: generated item ID 89 with pipe filling, black fluid visuals, and normal stack rules.");
    }

    [MenuItem("Tools/ProjectF/Generate Oil Bucket Item", true)]
    private static bool CanGenerateOilBucket()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem("Tools/ProjectF/Validation/Oil Bucket Item")]
    public static void ValidateOilBucketAssets()
    {
        ItemDefinition emptyDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(EmptyDefinitionPath);
        ItemDefinition oilDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(OilDefinitionPath);
        ValidateDefinition(emptyDefinition, EmptyBucketItemId, EmptyBucketName);
        ValidateDefinition(oilDefinition, OilBucketItemId, OilBucketName);

        Material oilSurfaceMaterial = AssetDatabase.LoadAssetAtPath<Material>(OilSurfaceMaterialPath);
        if (oilDefinition.icon == null
            || oilSurfaceMaterial == null
            || emptyDefinition.portableMesh != oilDefinition.portableMesh
            || emptyDefinition.portableMat != oilDefinition.portableMat)
        {
            throw new InvalidOperationException("Oil Bucket validation failed: item assets are incomplete.");
        }

        Bucket oilBucket = oilDefinition.mapObject as Bucket;
        MeshFilter body = ResolveBucketBody(oilBucket);
        Transform surface = body != null
            ? body.transform.Find(PortableBucketWaterVisual.SurfaceObjectName)
            : null;
        MeshRenderer surfaceRenderer = surface != null ? surface.GetComponent<MeshRenderer>() : null;
        if (oilBucket == null
            || surface == null
            || surface.GetComponent<MeshFilter>()?.sharedMesh == null
            || surfaceRenderer == null
            || surfaceRenderer.sharedMaterial != oilSurfaceMaterial
            || oilBucket.ResolveFluidSurfaceMaterial(4) != oilSurfaceMaterial)
        {
            throw new InvalidOperationException("Oil Bucket validation failed: installed oil surface is invalid.");
        }

        Debug.Log("Oil Bucket validation passed: pipe conversion and portable/installed oil visuals are configured.");
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
        return LoadOrCreateDefinition(WaterDefinitionPath, "Item_75_Water_Bucket");
    }

    private static ItemDefinition LoadOrCreateDefinition(string assetPath, string assetName)
    {
        ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
        if (definition != null)
        {
            return definition;
        }

        definition = ScriptableObject.CreateInstance<ItemDefinition>();
        definition.name = assetName;
        AssetDatabase.CreateAsset(definition, assetPath);
        return definition;
    }

    private static void CopyDefinitionDefaults(ItemDefinition source, ItemDefinition target)
    {
        target.portableMesh = source.portableMesh;
        target.portableMat = source.portableMat;
        target.interactionButtonList = source.interactionButtonList != null
            ? new List<Sprite>(source.interactionButtonList)
            : new List<Sprite>();
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
        target.bucketFillDurationSeconds = source.bucketFillDurationSeconds;
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
        Mesh waterSurfaceMesh,
        Material surfaceMaterialOverride = null,
        Material oilSurfaceMaterial = null)
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
            SerializedProperty oilMaterialProperty = serializedBucket.FindProperty("portableOilSurfaceMaterial");
            if (oilMaterialProperty != null && oilSurfaceMaterial != null)
            {
                oilMaterialProperty.objectReferenceValue = oilSurfaceMaterial;
            }
            serializedBucket.ApplyModifiedPropertiesWithoutUndo();
            Material surfaceMaterial = surfaceMaterialOverride != null
                ? surfaceMaterialOverride
                : bucket.ResolveFluidSurfaceMaterial(Pump.ResolveWaterItemId(null));
            ConfigureInstalledWaterSurface(
                bucket,
                containsWater,
                waterSurfaceMesh,
                surfaceMaterial);
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

    private static Material EnsureOilSurfaceMaterial()
    {
        EnsureAssetFolder(OilPrefabFolder);
        Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterSurfaceMaterialPath);
        if (sourceMaterial == null)
        {
            throw new InvalidOperationException(
                $"Oil Bucket: missing surface material template at {WaterSurfaceMaterialPath}.");
        }

        Material oilMaterial = AssetDatabase.LoadAssetAtPath<Material>(OilSurfaceMaterialPath);
        if (oilMaterial == null)
        {
            oilMaterial = new Material(sourceMaterial)
            {
                name = "M_Oil_Bucket_Surface"
            };
            AssetDatabase.CreateAsset(oilMaterial, OilSurfaceMaterialPath);
        }
        else
        {
            EditorUtility.CopySerialized(sourceMaterial, oilMaterial);
            oilMaterial.name = "M_Oil_Bucket_Surface";
        }

        Color oilColor = new Color(0.015f, 0.012f, 0.01f, 1f);
        if (oilMaterial.HasProperty("_BaseColor"))
        {
            oilMaterial.SetColor("_BaseColor", oilColor);
        }
        if (oilMaterial.HasProperty("_Color"))
        {
            oilMaterial.SetColor("_Color", oilColor);
        }

        EditorUtility.SetDirty(oilMaterial);
        return oilMaterial;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parentFolder = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(folderPath);
        if (string.IsNullOrWhiteSpace(parentFolder)
            || string.IsNullOrWhiteSpace(folderName)
            || !AssetDatabase.IsValidFolder(parentFolder)
            || string.IsNullOrWhiteSpace(AssetDatabase.CreateFolder(parentFolder, folderName)))
        {
            throw new InvalidOperationException($"Oil Bucket: failed to create asset folder {folderPath}.");
        }
    }

    private static void ConfigureInstalledWaterSurface(
        Bucket bucket,
        bool containsWater,
        Mesh waterSurfaceMesh,
        Material surfaceMaterial)
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
        surfaceRenderer.sharedMaterial = surfaceMaterial;
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
                            && surfaceRenderer.sharedMaterial
                            == bucket.ResolveFluidSurfaceMaterial(Pump.ResolveWaterItemId(null));
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

        ConfigureInstalledWaterSurface(
            bucket,
            true,
            waterSurfaceMesh,
            bucket.ResolveFluidSurfaceMaterial(Pump.ResolveWaterItemId(null)));
        EditorSceneManager.MarkSceneDirty(prefabStage.scene);
        SceneView.RepaintAll();
    }

    private static void ConfigurePortableIcon(string iconPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"Bucket item: missing icon at {iconPath}.");
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

    private static void EnsureItemIdAvailable(
        int expectedItemId,
        string expectedDefinitionPath,
        string itemName)
    {
        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data/Items" });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition != null
                && definition.id == expectedItemId
                && !string.Equals(assetPath, expectedDefinitionPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{itemName}: item ID {expectedItemId} is already used by {assetPath}.");
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
