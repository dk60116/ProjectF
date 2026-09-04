using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class OilResourceAssetGenerator
{
    private const int OilItemId = 77;
    private const string OilName = "Oil";
    private const string ItemDefinitionPath = "Assets/Data/Items/Item_77_Oil.asset";
    private const string ResourceDefinitionPath = "Assets/Data/MapObject/Resource_Oil.asset";
    private const string OilFolderPath = "Assets/MapObject/Ore/Oil";
    private const string OilPrefabPath = OilFolderPath + "/Oil.prefab";
    private const string OilHoleMeshPath = OilFolderPath + "/Oil_Hole.mesh";
    private const string OilSurfaceMaterialPath = OilFolderPath + "/M_Oil_Surface.mat";
    private const string OilIconPath = "Assets/Items/Fluid/Oil/Oil_Icon.png";
    private const float OilSurfaceMinimumDiameter = 0.62f;
    private const float OilSurfaceMaximumDiameter = 0.80f;
    private const float OilSurfaceMaximumAxisDifference = 0.08f;

    [MenuItem("Tools/ProjectF/Generate Oil Resource")]
    public static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Oil Resource: exit Play Mode before generating assets.");
            return;
        }

        AssetDatabase.Refresh();
        EnsureFolder(OilFolderPath);
        EnsureItemIdAvailable();
        ConfigureIconImporter();

        ItemDefinition itemDefinition = LoadOrCreateItemDefinition();
        ResourceDefinition resourceDefinition = LoadOrCreateResourceDefinition();
        Material surfaceMaterial = LoadOrCreateMaterial(
            OilSurfaceMaterialPath,
            new Color(0.012f, 0.010f, 0.008f, 1f),
            true);
        Mesh oilHoleMesh = LoadOrCreateOilHoleMesh();

        ConfigureItemDefinition(itemDefinition);
        ConfigureResourceDefinition(resourceDefinition, null);
        Resource oilPrefab = CreateOrUpdateOilPrefab(
            itemDefinition,
            resourceDefinition,
            oilHoleMesh,
            surfaceMaterial);
        ConfigureResourceDefinition(resourceDefinition, oilPrefab);
        itemDefinition.mapObject = oilPrefab;
        EditorUtility.SetDirty(itemDefinition);

        AssetDatabase.SaveAssets();
        RegisterWithLoadedScenes(itemDefinition, resourceDefinition);
        AssetDatabase.SaveAssets();
        ValidateGeneratedAssets();
        SceneView.RepaintAll();

        Selection.activeObject = resourceDefinition;
        Debug.Log(
            "Oil Resource: generated item ID 77 and a one-cell oil surface. TerrainGenerator owns the excavated ground mesh.");
    }

    [MenuItem("Tools/ProjectF/Generate Oil Resource", true)]
    private static bool CanGenerate()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem("Tools/ProjectF/Regenerate Oil Visual")]
    public static void RegenerateVisual()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Oil Resource: exit Play Mode before regenerating the visual.");
            return;
        }

        EnsureFolder(OilFolderPath);
        Material surfaceMaterial = LoadOrCreateMaterial(
            OilSurfaceMaterialPath,
            new Color(0.012f, 0.010f, 0.008f, 1f),
            true);
        Mesh oilHoleMesh = LoadOrCreateOilHoleMesh();

        ApplyOilVisualToExistingPrefab(oilHoleMesh, surfaceMaterial);
        AssetDatabase.SaveAssets();
        ValidateOilVisual();
        SceneView.RepaintAll();
        Selection.activeObject = oilHoleMesh;
    }

    [MenuItem("Tools/ProjectF/Regenerate Oil Visual", true)]
    private static bool CanRegenerateVisual()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem("Tools/ProjectF/Validation/Oil Resource")]
    public static void ValidateGeneratedAssets()
    {
        ItemDefinition itemDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDefinitionPath);
        ResourceDefinition resourceDefinition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(ResourceDefinitionPath);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OilPrefabPath);
        Resource resource = prefab != null ? prefab.GetComponent<Resource>() : null;

        if (itemDefinition == null
            || itemDefinition.id != OilItemId
            || !string.Equals(itemDefinition.itemName, OilName, StringComparison.Ordinal)
            || itemDefinition.icon == null
            || itemDefinition.mapObject != resource)
        {
            throw new InvalidOperationException("Oil Resource validation failed: ItemDefinition is incomplete.");
        }

        if (resourceDefinition == null
            || resourceDefinition.prefab != resource
            || !string.Equals(resourceDefinition.resourceName, OilName, StringComparison.Ordinal)
            || resource == null
            || resource.Status.mapSizeX != 1
            || resource.Status.mapSizeY != 1)
        {
            throw new InvalidOperationException("Oil Resource validation failed: one-cell resource prefab is incomplete.");
        }

        ValidateOilVisual();
        Debug.Log("Oil Resource validation passed: item, one-cell oil surface, prefab, and resource definition are connected.");
    }

    [MenuItem("Tools/ProjectF/Validation/Oil Terrain Preview")]
    public static void ValidateOilTerrainPreview()
    {
        Resource[] resources = Resources.FindObjectsOfTypeAll<Resource>();
        int validatedCount = 0;
        string lastFailure = string.Empty;
        for (int resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
        {
            Resource resource = resources[resourceIndex];
            if (resource == null
                || EditorUtility.IsPersistent(resource)
                || !resource.gameObject.scene.IsValid()
                || !resource.gameObject.activeInHierarchy
                || !resource.gameObject.name.StartsWith(OilName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryValidateOilTerrain(resource, out string measurements))
            {
                lastFailure = measurements;
                continue;
            }

            validatedCount++;
            Debug.Log($"Oil terrain preview passed at {measurements}", resource);
        }

        if (validatedCount == 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(lastFailure)
                    ? "Oil terrain preview validation failed: generate an editor terrain preview containing Oil first."
                    : $"Oil terrain preview validation failed: {lastFailure}");
        }

        Debug.Log($"Oil terrain preview validation passed for {validatedCount} generated Oil cells.");
    }

    private static bool TryValidateOilTerrain(Resource resource, out string measurements)
    {
        measurements = string.Empty;
        Block block = resource != null ? resource.GetComponentInParent<Block>() : null;
        Transform chunkRoot = block != null ? block.transform.parent : null;
        MeshFilter terrainFilter = chunkRoot != null
            ? chunkRoot.Find("GeneratedSurface")?.GetComponent<MeshFilter>()
            : null;
        MeshFilter oilFilter = resource != null
            ? FindOilSurfaceTransform(resource.transform)?.GetComponent<MeshFilter>()
            : null;
        if (block == null
            || terrainFilter == null
            || terrainFilter.sharedMesh == null
            || oilFilter == null
            || oilFilter.sharedMesh == null)
        {
            measurements =
                $"cell hierarchy incomplete (block={block != null}, terrainFilter={terrainFilter != null}, "
                + $"terrainMesh={terrainFilter != null && terrainFilter.sharedMesh != null}, "
                + $"oilFilter={oilFilter != null}, oilMesh={oilFilter != null && oilFilter.sharedMesh != null})";
            return false;
        }

        Vector3 center = block.transform.position;
        float centerMinimumY = float.MaxValue;
        float centerMaximumY = float.MinValue;
        float rimMaximumY = float.MinValue;
        int centerVertexCount = 0;
        int rimVertexCount = 0;
        Vector3[] terrainVertices = terrainFilter.sharedMesh.vertices;
        for (int vertexIndex = 0; vertexIndex < terrainVertices.Length; vertexIndex++)
        {
            Vector3 worldVertex = terrainFilter.transform.TransformPoint(terrainVertices[vertexIndex]);
            terrainVertices[vertexIndex] = worldVertex;
            float deltaX = worldVertex.x - center.x;
            float deltaZ = worldVertex.z - center.z;
            float distance = Mathf.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
            if (distance <= TerrainGenerator.GeneratedOilPitInnerRadius * 0.78f)
            {
                centerMinimumY = Mathf.Min(centerMinimumY, worldVertex.y);
                centerMaximumY = Mathf.Max(centerMaximumY, worldVertex.y);
                centerVertexCount++;
            }
            else if (distance >= TerrainGenerator.GeneratedOilPitOuterRadius - 0.05f
                     && distance <= TerrainGenerator.GeneratedOilPitOuterRadius + 0.05f)
            {
                rimMaximumY = Mathf.Max(rimMaximumY, worldVertex.y);
                rimVertexCount++;
            }
        }

        Bounds oilBounds = oilFilter.GetComponent<MeshRenderer>().bounds;
        float oilSurfaceY = oilBounds.max.y;
        float oilPlaneTilt = Vector3.Angle(oilFilter.transform.up, Vector3.up);
        string occludingRendererNames = FindOilOccludingRenderers(oilFilter, center, oilSurfaceY);
        int[] terrainTriangles = terrainFilter.sharedMesh.triangles;
        float maximumTerrainYOverOil = float.MinValue;
        int oilSurfaceSampleCount = 0;
        Vector3 oilCenter = oilFilter.transform.TransformPoint(oilFilter.sharedMesh.bounds.center);
        float oilCenterOffsetXZ = Vector2.Distance(
            new Vector2(center.x, center.z),
            new Vector2(oilCenter.x, oilCenter.z));
        AccumulateTerrainHeightAtOilPoint(
            oilCenter,
            terrainVertices,
            terrainTriangles,
            ref maximumTerrainYOverOil,
            ref oilSurfaceSampleCount);

        const int denseSampleResolution = 25;
        float denseSampleRadius = TerrainGenerator.GeneratedOilPitInnerRadius * 0.6f;
        int denseSampleCount = 0;
        int denseOccludedSampleCount = 0;
        for (int sampleZ = 0; sampleZ < denseSampleResolution; sampleZ++)
        {
            float normalizedZ = sampleZ / (float)(denseSampleResolution - 1);
            float offsetZ = Mathf.Lerp(-denseSampleRadius, denseSampleRadius, normalizedZ);
            for (int sampleX = 0; sampleX < denseSampleResolution; sampleX++)
            {
                float normalizedX = sampleX / (float)(denseSampleResolution - 1);
                float offsetX = Mathf.Lerp(-denseSampleRadius, denseSampleRadius, normalizedX);
                if ((offsetX * offsetX) + (offsetZ * offsetZ) > denseSampleRadius * denseSampleRadius)
                {
                    continue;
                }

                if (!TryGetMaximumTerrainYAtPoint(
                        oilCenter.x + offsetX,
                        oilCenter.z + offsetZ,
                        terrainVertices,
                        terrainTriangles,
                        out float denseTerrainY))
                {
                    continue;
                }

                denseSampleCount++;
                maximumTerrainYOverOil = Mathf.Max(maximumTerrainYOverOil, denseTerrainY);
                if (denseTerrainY >= oilSurfaceY - 0.001f)
                {
                    denseOccludedSampleCount++;
                }
            }
        }

        float oilClearance = oilSurfaceY - maximumTerrainYOverOil;
        measurements =
            $"cell {block.Coordinate}, ground center {centerMinimumY:F3}..{centerMaximumY:F3}, "
            + $"rim max {rimMaximumY:F3}, oil surface {oilSurfaceY:F3}, "
            + $"plane center XZ offset {oilCenterOffsetXZ:F3}, "
            + $"plane size {oilBounds.size.x:F3}x{oilBounds.size.z:F3}, tilt {oilPlaneTilt:F3}, "
            + $"minimum visible clearance {oilClearance:F3}, dense occlusion "
            + $"{denseOccludedSampleCount}/{denseSampleCount}, occluders [{occludingRendererNames}]";
        if (centerVertexCount == 0
            || rimVertexCount == 0
            || oilSurfaceSampleCount == 0
            || oilCenterOffsetXZ > 0.01f
            || oilBounds.size.x < OilSurfaceMinimumDiameter
            || oilBounds.size.x > OilSurfaceMaximumDiameter
            || oilBounds.size.z < OilSurfaceMinimumDiameter
            || oilBounds.size.z > OilSurfaceMaximumDiameter
            || Mathf.Abs(oilBounds.size.x - oilBounds.size.z)
               > OilSurfaceMaximumAxisDifference
            || oilPlaneTilt > 0.1f
            || centerMaximumY >= oilSurfaceY - 0.025f
            || rimMaximumY <= oilSurfaceY + 0.008f
            || oilClearance <= 0.025f
            || denseSampleCount == 0
            || denseOccludedSampleCount > 0
            || !string.IsNullOrEmpty(occludingRendererNames))
        {
            throw new InvalidOperationException($"Oil terrain preview validation failed at {measurements}.");
        }

        return true;
    }

    private static string FindOilOccludingRenderers(MeshFilter oilFilter, Vector3 oilCenter, float oilSurfaceY)
    {
        MeshFilter[] filters = Resources.FindObjectsOfTypeAll<MeshFilter>();
        string occluderNames = string.Empty;
        for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
        {
            MeshFilter filter = filters[filterIndex];
            MeshRenderer renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (filter == null
                || filter == oilFilter
                || filter.sharedMesh == null
                || renderer == null
                || !renderer.enabled
                || !filter.gameObject.activeInHierarchy
                || EditorUtility.IsPersistent(filter)
                || !filter.gameObject.scene.IsValid())
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (oilCenter.x < bounds.min.x
                || oilCenter.x > bounds.max.x
                || oilCenter.z < bounds.min.z
                || oilCenter.z > bounds.max.z
                || bounds.max.y < oilSurfaceY - 0.001f)
            {
                continue;
            }

            Vector3[] vertices = filter.sharedMesh.vertices;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                vertices[vertexIndex] = filter.transform.TransformPoint(vertices[vertexIndex]);
            }

            if (!TryGetMaximumTerrainYAtPoint(
                    oilCenter.x,
                    oilCenter.z,
                    vertices,
                    filter.sharedMesh.triangles,
                    out float surfaceY)
                || surfaceY < oilSurfaceY - 0.001f)
            {
                continue;
            }

            string hierarchyPath = AnimationUtility.CalculateTransformPath(filter.transform, null);
            occluderNames = string.IsNullOrEmpty(occluderNames)
                ? $"{hierarchyPath}@{surfaceY:F3}"
                : $"{occluderNames}, {hierarchyPath}@{surfaceY:F3}";
        }

        return occluderNames;
    }

    private static void AccumulateTerrainHeightAtOilPoint(
        Vector3 oilPoint,
        Vector3[] terrainVertices,
        int[] terrainTriangles,
        ref float maximumTerrainY,
        ref int sampleCount)
    {
        if (!TryGetMaximumTerrainYAtPoint(
                oilPoint.x,
                oilPoint.z,
                terrainVertices,
                terrainTriangles,
                out float terrainY))
        {
            return;
        }

        maximumTerrainY = Mathf.Max(maximumTerrainY, terrainY);
        sampleCount++;
    }

    private static bool TryGetMaximumTerrainYAtPoint(
        float x,
        float z,
        Vector3[] vertices,
        int[] triangles,
        out float maximumY)
    {
        maximumY = float.MinValue;
        bool found = false;
        for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
        {
            Vector3 a = vertices[triangles[triangleIndex]];
            Vector3 b = vertices[triangles[triangleIndex + 1]];
            Vector3 c = vertices[triangles[triangleIndex + 2]];
            float denominator = ((b.z - c.z) * (a.x - c.x)) + ((c.x - b.x) * (a.z - c.z));
            if (Mathf.Abs(denominator) <= 0.000001f)
            {
                continue;
            }

            float weightA = (((b.z - c.z) * (x - c.x)) + ((c.x - b.x) * (z - c.z))) / denominator;
            float weightB = (((c.z - a.z) * (x - c.x)) + ((a.x - c.x) * (z - c.z))) / denominator;
            float weightC = 1f - weightA - weightB;
            const float edgeTolerance = -0.0001f;
            if (weightA < edgeTolerance || weightB < edgeTolerance || weightC < edgeTolerance)
            {
                continue;
            }

            float y = (a.y * weightA) + (b.y * weightB) + (c.y * weightC);
            maximumY = Mathf.Max(maximumY, y);
            found = true;
        }

        return found;
    }

    private static void ApplyOilVisualToExistingPrefab(
        Mesh oilHoleMesh,
        Material surfaceMaterial)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(OilPrefabPath);
        try
        {
            Transform body = root != null ? root.transform.Find("Body") : null;
            MeshFilter bodyFilter = body != null ? body.GetComponent<MeshFilter>() : null;
            MeshRenderer bodyRenderer = body != null ? body.GetComponent<MeshRenderer>() : null;
            if (bodyFilter == null || bodyRenderer == null)
            {
                throw new InvalidOperationException("Oil Resource: existing prefab is missing its Body mesh components.");
            }

            bodyFilter.sharedMesh = oilHoleMesh;
            bodyRenderer.sharedMaterials = new[] { surfaceMaterial };
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;
            bodyRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            PrefabUtility.SaveAsPrefabAsset(root, OilPrefabPath);
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ValidateOilVisual()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OilPrefabPath);
        MeshFilter bodyFilter = prefab != null
            ? FindOilSurfaceTransform(prefab.transform)?.GetComponent<MeshFilter>()
            : null;
        MeshRenderer bodyRenderer = bodyFilter != null ? bodyFilter.GetComponent<MeshRenderer>() : null;
        Transform bodyTransform = bodyFilter != null ? bodyFilter.transform : null;
        Bounds bounds = bodyFilter != null && bodyFilter.sharedMesh != null
            ? bodyFilter.sharedMesh.bounds
            : default;
        Vector3 scale = bodyTransform != null ? bodyTransform.lossyScale : Vector3.zero;
        float planeWorldSizeX = bounds.size.x * Mathf.Abs(scale.x);
        float planeWorldSizeZ = bounds.size.z * Mathf.Abs(scale.z);
        float planeTilt = bodyTransform != null ? Vector3.Angle(bodyTransform.up, Vector3.up) : 180f;
        if (bodyFilter == null
            || bodyFilter.sharedMesh == null
            || bodyFilter.sharedMesh.subMeshCount != 1
            || bodyRenderer == null
            || bodyRenderer.sharedMaterials.Length < 1
            || planeWorldSizeX < OilSurfaceMinimumDiameter
            || planeWorldSizeX > OilSurfaceMaximumDiameter
            || planeWorldSizeZ < OilSurfaceMinimumDiameter
            || planeWorldSizeZ > OilSurfaceMaximumDiameter
            || Mathf.Abs(planeWorldSizeX - planeWorldSizeZ)
               > OilSurfaceMaximumAxisDifference
            || planeTilt > 0.1f)
        {
            throw new InvalidOperationException(
                "Oil Resource visual validation failed: the liquid must be a horizontal, near-circular patch.");
        }

        Debug.Log("Oil Resource visual validation passed: the prefab contains one flat, irregular circular liquid patch.");
    }

    private static Transform FindOilSurfaceTransform(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        Transform plane = root.Find("Plane");
        return plane != null ? plane : root.Find("Body");
    }

    private static ItemDefinition LoadOrCreateItemDefinition()
    {
        ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDefinitionPath);
        if (definition != null)
        {
            return definition;
        }

        definition = ScriptableObject.CreateInstance<ItemDefinition>();
        definition.name = "Item_77_Oil";
        AssetDatabase.CreateAsset(definition, ItemDefinitionPath);
        return definition;
    }

    private static ResourceDefinition LoadOrCreateResourceDefinition()
    {
        ResourceDefinition definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(ResourceDefinitionPath);
        if (definition != null)
        {
            return definition;
        }

        definition = ScriptableObject.CreateInstance<ResourceDefinition>();
        definition.name = "Resource_Oil";
        AssetDatabase.CreateAsset(definition, ResourceDefinitionPath);
        return definition;
    }

    private static void ConfigureItemDefinition(ItemDefinition definition)
    {
        definition.itemName = OilName;
        definition.id = OilItemId;
        definition.icon = AssetDatabase.LoadAssetAtPath<Sprite>(OilIconPath);
        definition.portableMesh = null;
        definition.portableMat = null;
        definition.oneItem = false;
        definition.interactionButtonList.Clear();
        definition.size = 1;
        definition.itemFilter = false;
        definition.ignoreFilter = true;
        definition.isManual = false;
        definition.manualTargetItem = null;
        definition.upgradeable = false;
        definition.capacity = 10;
        definition.storesFluid = false;
        definition.fluidStorageLiters = 0f;
        definition.energyType = ItemDefinition.EnergyType.None;
        definition.energyAmount = 0;
        definition.useEnergyType = ItemDefinition.EnergyType.None;
        definition.useEnergyAmount = 0f;
        definition.completeEnergy = 0f;
        EditorUtility.SetDirty(definition);
    }

    private static void ConfigureResourceDefinition(ResourceDefinition definition, Resource prefab)
    {
        definition.resourceName = OilName;
        definition.prefab = prefab;
        definition.harvestMode = Resource.HarvestMode.Mining;
        definition.defaultResourceCount = 1;
        definition.defaultGetCount = 1;
        definition.defaultMaxGauge = 10;
        definition.defaultCurrentGauge = 10;
        EditorUtility.SetDirty(definition);
    }

    private static Resource CreateOrUpdateOilPrefab(
        ItemDefinition itemDefinition,
        ResourceDefinition resourceDefinition,
        Mesh oilHoleMesh,
        Material surfaceMaterial)
    {
        GameObject root = new GameObject(OilName);
        try
        {
            GameObject coalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MapObject/Ore/Coal/Coal.prefab");
            root.layer = coalPrefab != null ? coalPrefab.layer : 7;

            Resource resource = root.AddComponent<Resource>();
            ConfigureResourceComponent(resource, itemDefinition, resourceDefinition);

            GameObject body = new GameObject("Body");
            body.layer = root.layer;
            body.transform.SetParent(root.transform, false);
            MeshFilter bodyFilter = body.AddComponent<MeshFilter>();
            bodyFilter.sharedMesh = oilHoleMesh;
            MeshRenderer bodyRenderer = body.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterials = new[] { surfaceMaterial };
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;
            bodyRenderer.lightProbeUsage = LightProbeUsage.Off;
            bodyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            bodyRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, OilPrefabPath);
            Resource savedResource = savedPrefab != null ? savedPrefab.GetComponent<Resource>() : null;
            if (savedResource == null)
            {
                throw new InvalidOperationException($"Oil Resource: failed to save prefab at {OilPrefabPath}.");
            }

            return savedResource;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureResourceComponent(
        Resource resource,
        ItemDefinition itemDefinition,
        ResourceDefinition resourceDefinition)
    {
        SerializedObject serializedResource = new SerializedObject(resource);
        serializedResource.FindProperty("objectName").stringValue = OilName;
        serializedResource.FindProperty("objId").intValue = OilItemId;
        serializedResource.FindProperty("itemDefinition").objectReferenceValue = itemDefinition;

        SerializedProperty mapStatus = serializedResource.FindProperty("mapStatus");
        mapStatus.FindPropertyRelative("mapSizeX").intValue = 1;
        mapStatus.FindPropertyRelative("mapSizeY").intValue = 1;
        mapStatus.FindPropertyRelative("centerCellX").intValue = 0;
        mapStatus.FindPropertyRelative("centerCellY").intValue = 0;

        serializedResource.FindProperty("harvestMode").enumValueIndex = (int)Resource.HarvestMode.Mining;
        serializedResource.FindProperty("definition").objectReferenceValue = resourceDefinition;
        SerializedProperty status = serializedResource.FindProperty("resourceStatus");
        status.FindPropertyRelative("outputId").intValue = OilItemId;
        status.FindPropertyRelative("outputItemName").stringValue = OilName;
        status.FindPropertyRelative("resourceCount").intValue = 1;
        status.FindPropertyRelative("getCount").intValue = 1;
        status.FindPropertyRelative("maxGauge").intValue = 10;
        status.FindPropertyRelative("currentGague").intValue = 10;
        serializedResource.FindProperty("minimumBodyScaleRatio").floatValue = 1f;
        serializedResource.FindProperty("maximumBodyScaleRatio").floatValue = 1f;
        serializedResource.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Mesh LoadOrCreateOilHoleMesh()
    {
        float surfaceY = TerrainGenerator.GeneratedOilSurfaceLocalY;
        int segmentCount = TerrainGenerator.GeneratedOilSurfaceSegmentCount;
        float surfaceRadius = TerrainGenerator.GeneratedOilSurfaceRadius;
        List<Vector3> vertices = new List<Vector3>(segmentCount + 1);
        List<Vector2> uvs = new List<Vector2>(segmentCount + 1);
        List<int> surfaceTriangles = new List<int>(segmentCount * 3);
        vertices.Add(new Vector3(0f, surfaceY, 0f));
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            float angle = segmentIndex * Mathf.PI * 2f / segmentCount;
            float radius = TerrainGenerator.GetGeneratedOilSurfaceRadius(angle);
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices.Add(new Vector3(x, surfaceY, z));
            uvs.Add(new Vector2(
                0.5f + (x / (surfaceRadius * 2f)),
                0.5f + (z / (surfaceRadius * 2f))));

            int currentVertex = segmentIndex + 1;
            int nextVertex = ((segmentIndex + 1) % segmentCount) + 1;
            surfaceTriangles.Add(0);
            surfaceTriangles.Add(nextVertex);
            surfaceTriangles.Add(currentVertex);
        }

        Mesh generatedMesh = new Mesh { name = "Oil_Hole" };
        generatedMesh.SetVertices(vertices);
        generatedMesh.SetUVs(0, uvs);
        generatedMesh.subMeshCount = 1;
        generatedMesh.SetTriangles(surfaceTriangles, 0, true);
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateTangents();
        generatedMesh.RecalculateBounds();

        Mesh meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(OilHoleMeshPath);
        if (meshAsset == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, OilHoleMeshPath);
            AssetDatabase.SaveAssetIfDirty(generatedMesh);
            AssetDatabase.ImportAsset(OilHoleMeshPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Mesh>(OilHoleMeshPath);
        }

        EditorUtility.CopySerialized(generatedMesh, meshAsset);
        UnityEngine.Object.DestroyImmediate(generatedMesh);
        EditorUtility.SetDirty(meshAsset);
        AssetDatabase.SaveAssetIfDirty(meshAsset);
        AssetDatabase.ImportAsset(OilHoleMeshPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Mesh>(OilHoleMeshPath);
    }

    private static Material LoadOrCreateMaterial(string path, Color color, bool unlit)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(unlit
            ? "Universal Render Pipeline/Unlit"
            : "Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find(unlit
                ? "Universal Render Pipeline/Lit"
                : "Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            throw new InvalidOperationException("Oil Resource: no compatible URP shader was found.");
        }

        if (material == null)
        {
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void RegisterWithLoadedScenes(
        ItemDefinition itemDefinition,
        ResourceDefinition resourceDefinition)
    {
        TerrainGenerator[] generators = UnityEngine.Object.FindObjectsByType<TerrainGenerator>(FindObjectsInactive.Include);
        for (int i = 0; i < generators.Length; i++)
        {
            TerrainGenerator generator = generators[i];
            if (generator == null || !generator.gameObject.scene.IsValid() || !generator.gameObject.scene.isLoaded)
            {
                continue;
            }

            Undo.RecordObject(generator, "Connect Oil Resource");
            ConnectTerrainGenerator(generator, resourceDefinition);
            generator.SyncResourceEntryDefinitions();
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        ItemManager[] itemManagers = UnityEngine.Object.FindObjectsByType<ItemManager>(FindObjectsInactive.Include);
        for (int i = 0; i < itemManagers.Length; i++)
        {
            ItemManager itemManager = itemManagers[i];
            if (itemManager == null || !itemManager.gameObject.scene.IsValid() || !itemManager.gameObject.scene.isLoaded)
            {
                continue;
            }

            Undo.RecordObject(itemManager, "Register Oil Item");
            itemManager.RegisterRuntimeItemDefinition(itemDefinition);
            itemManager.MarkEditorDirty();
            EditorSceneManager.MarkSceneDirty(itemManager.gameObject.scene);
        }
    }

    private static void ConnectTerrainGenerator(
        TerrainGenerator generator,
        ResourceDefinition resourceDefinition)
    {
        SerializedObject serializedGenerator = new SerializedObject(generator);
        SerializedProperty oilResources = serializedGenerator.FindProperty("oilResources");
        int targetIndex = -1;
        for (int i = 0; i < oilResources.arraySize; i++)
        {
            SerializedProperty entry = oilResources.GetArrayElementAtIndex(i);
            ResourceDefinition existingDefinition = entry.FindPropertyRelative("definition").objectReferenceValue as ResourceDefinition;
            string entryName = entry.FindPropertyRelative("name").stringValue;
            if (existingDefinition == resourceDefinition
                || string.Equals(entryName, OilName, StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            targetIndex = oilResources.arraySize;
            oilResources.InsertArrayElementAtIndex(targetIndex);
        }

        SerializedProperty oilEntry = oilResources.GetArrayElementAtIndex(targetIndex);
        oilEntry.FindPropertyRelative("name").stringValue = OilName;
        oilEntry.FindPropertyRelative("prefab").objectReferenceValue = resourceDefinition.prefab;
        oilEntry.FindPropertyRelative("definition").objectReferenceValue = resourceDefinition;
        oilEntry.FindPropertyRelative("placementMode").enumValueIndex = (int)TerrainGenerator.ResourcePlacementMode.Clustered;

        SerializedProperty spawnChance = oilEntry.FindPropertyRelative("spawnChance");
        if (spawnChance.floatValue <= 0f)
        {
            spawnChance.floatValue = 0.014f;
        }

        oilEntry.FindPropertyRelative("spacingMultiplier").floatValue = Mathf.Max(
            1f,
            oilEntry.FindPropertyRelative("spacingMultiplier").floatValue);
        oilEntry.FindPropertyRelative("minResourceCount").intValue = 1;
        oilEntry.FindPropertyRelative("maxResourceCount").intValue = 1;
        oilEntry.FindPropertyRelative("starterMinResourceCount").intValue = 1;
        oilEntry.FindPropertyRelative("starterMaxResourceCount").intValue = 1;
        oilEntry.FindPropertyRelative("useStarterPatch").boolValue = false;

        SerializedProperty salt = oilEntry.FindPropertyRelative("salt");
        if (salt.intValue == 0)
        {
            salt.intValue = 7719;
        }

        serializedGenerator.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureIconImporter()
    {
        TextureImporter importer = AssetImporter.GetAtPath(OilIconPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"Oil Resource: missing icon at {OilIconPath}.");
        }

        bool changed = importer.textureType != TextureImporterType.Sprite
                       || importer.spriteImportMode != SpriteImportMode.Single
                       || !importer.alphaIsTransparency
                       || importer.mipmapEnabled;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static void EnsureItemIdAvailable()
    {
        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data/Items" });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (definition != null
                && definition.id == OilItemId
                && !string.Equals(path, ItemDefinitionPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Oil Resource: item ID {OilItemId} is already used by {path}.");
            }
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
