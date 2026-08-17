using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class WheelPortableAssetGenerator
{
    private const string OutputFolder = "Assets/Items/Train/Wheel";
    private const string MeshPath = OutputFolder + "/Wheel_P.mesh";
    private const string IronMaterialPath = OutputFolder + "/M_IronWheel_P.mat";
    private const string WoodenMaterialPath = OutputFolder + "/M_WoodenWheel_P.mat";
    private const string IronTexturePath = OutputFolder + "/T_IronWheel_P.png";
    private const string WoodenTexturePath = OutputFolder + "/T_WoodenWheel_P.png";
    private const string ItemDefinitionFolder = "Assets/Data/Items";
    private const string IronWheelItemName = "Iron wheel";
    private const string WoodenWheelItemName = "Wooden wheel";

    private const int RimSegments = 8;
    private const int SpokeCount = 6;
    private const int MaxPortableVertexCount = 100;
    private const float OuterRadius = 0.104f;
    private const float InnerRadius = 0.073f;
    private const float HubRadius = 0.028f;
    private const float HalfThickness = 0.014f;
    private const float SpokeHalfWidth = 0.0075f;

    [MenuItem("Tools/ProjectF/Generate Wheel Portable Models")]
    public static void GenerateWheelPortableAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Wheel Portable Models: exit Play Mode before generating assets.");
            return;
        }

        EnsureOutputFolder();
        Mesh mesh = CreateOrUpdateMesh();
        Texture2D ironTexture = LoadAndConfigureTexture(IronTexturePath);
        Texture2D woodenTexture = LoadAndConfigureTexture(WoodenTexturePath);
        Material ironMaterial = CreateOrUpdateMaterial(
            IronMaterialPath,
            "M_IronWheel_P",
            ironTexture,
            0.75f,
            0.42f);
        Material woodenMaterial = CreateOrUpdateMaterial(
            WoodenMaterialPath,
            "M_WoodenWheel_P",
            woodenTexture,
            0f,
            0.24f);

        int assignedDefinitionCount = AssignItemDefinitions(mesh, ironMaterial, woodenMaterial);
        int assignedSceneItemCount = AssignLoadedItemManagers(mesh, ironMaterial, woodenMaterial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
        Selection.activeObject = mesh;
        Debug.Log(
            $"Wheel Portable Models: generated a rim, hub, and {SpokeCount} spokes with "
            + $"{mesh.vertexCount} vertices and {mesh.GetIndexCount(0) / 3} triangles, then assigned "
            + $"{assignedDefinitionCount} ItemDefinition(s) and {assignedSceneItemCount} loaded scene item(s).");
    }

    [MenuItem("Tools/ProjectF/Generate Wheel Portable Models", true)]
    private static bool CanGenerateWheelPortableAssets()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static Mesh CreateOrUpdateMesh()
    {
        Mesh generatedMesh = BuildWheelMesh();
        Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (assetMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, MeshPath);
            return generatedMesh;
        }

        EditorUtility.CopySerialized(generatedMesh, assetMesh);
        UnityEngine.Object.DestroyImmediate(generatedMesh);
        EditorUtility.SetDirty(assetMesh);
        return assetMesh;
    }

    private static Mesh BuildWheelMesh()
    {
        int expectedVertexCount = RimSegments * 6 + SpokeCount * 8 + 2;
        List<Vector3> vertices = new List<Vector3>(expectedVertexCount);
        List<Vector2> uv = new List<Vector2>(vertices.Capacity);
        List<int> triangles = new List<int>(512);

        AddExtrudedRim(vertices, uv, triangles);
        AddHub(vertices, uv, triangles);
        AddSpokes(vertices, uv, triangles);

        Mesh mesh = new Mesh
        {
            name = "Wheel_P"
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0, false);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        if (mesh.vertexCount > MaxPortableVertexCount)
        {
            UnityEngine.Object.DestroyImmediate(mesh);
            throw new InvalidOperationException(
                $"Wheel portable mesh exceeds {MaxPortableVertexCount} vertices: {vertices.Count}.");
        }

        return mesh;
    }

    private static void AddExtrudedRim(
        List<Vector3> vertices,
        List<Vector2> uv,
        List<int> triangles)
    {
        int start = vertices.Count;
        for (int i = 0; i < RimSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / RimSegments;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            AddPlanarVertex(vertices, uv, cosine * OuterRadius, HalfThickness, sine * OuterRadius);
            AddPlanarVertex(vertices, uv, cosine * InnerRadius, HalfThickness, sine * InnerRadius);
            AddPlanarVertex(vertices, uv, cosine * OuterRadius, -HalfThickness, sine * OuterRadius);
            AddPlanarVertex(vertices, uv, cosine * InnerRadius, -HalfThickness, sine * InnerRadius);
        }

        for (int i = 0; i < RimSegments; i++)
        {
            int current = start + i * 4;
            int next = start + ((i + 1) % RimSegments) * 4;
            AddQuad(triangles, current, current + 1, next + 1, next);
            AddQuad(triangles, current + 2, next + 2, next + 3, current + 3);
            AddQuad(triangles, current + 2, current, next, next + 2);
            AddQuad(triangles, current + 3, next + 3, next + 1, current + 1);
        }
    }

    private static void AddHub(
        List<Vector3> vertices,
        List<Vector2> uv,
        List<int> triangles)
    {
        int topCenter = vertices.Count;
        AddPlanarVertex(vertices, uv, 0f, HalfThickness, 0f);
        int bottomCenter = vertices.Count;
        AddPlanarVertex(vertices, uv, 0f, -HalfThickness, 0f);
        int ringStart = vertices.Count;

        for (int i = 0; i < RimSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / RimSegments;
            float x = Mathf.Cos(angle) * HubRadius;
            float z = Mathf.Sin(angle) * HubRadius;
            AddPlanarVertex(vertices, uv, x, HalfThickness, z);
            AddPlanarVertex(vertices, uv, x, -HalfThickness, z);
        }

        for (int i = 0; i < RimSegments; i++)
        {
            int currentTop = ringStart + i * 2;
            int currentBottom = currentTop + 1;
            int nextTop = ringStart + ((i + 1) % RimSegments) * 2;
            int nextBottom = nextTop + 1;
            triangles.Add(topCenter);
            triangles.Add(nextTop);
            triangles.Add(currentTop);
            triangles.Add(bottomCenter);
            triangles.Add(currentBottom);
            triangles.Add(nextBottom);
            AddQuad(triangles, currentBottom, currentTop, nextTop, nextBottom);
        }
    }

    private static void AddSpokes(
        List<Vector3> vertices,
        List<Vector2> uv,
        List<int> triangles)
    {
        float spokeStart = HubRadius * 0.82f;
        float spokeEnd = InnerRadius * 1.04f;
        for (int i = 0; i < SpokeCount; i++)
        {
            float angle = Mathf.PI * 2f * i / SpokeCount;
            AddSpoke(vertices, uv, triangles, angle, spokeStart, spokeEnd);
        }
    }

    private static void AddSpoke(
        List<Vector3> vertices,
        List<Vector2> uv,
        List<int> triangles,
        float angle,
        float startRadius,
        float endRadius)
    {
        Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
        Vector3 center = radial * ((startRadius + endRadius) * 0.5f);
        Vector3 along = radial * ((endRadius - startRadius) * 0.5f);
        Vector3 across = tangent * SpokeHalfWidth;
        Vector3 up = Vector3.up * (HalfThickness * 0.72f);
        int start = vertices.Count;

        AddPlanarVertex(vertices, uv, center - along - across - up);
        AddPlanarVertex(vertices, uv, center + along - across - up);
        AddPlanarVertex(vertices, uv, center + along + across - up);
        AddPlanarVertex(vertices, uv, center - along + across - up);
        AddPlanarVertex(vertices, uv, center - along - across + up);
        AddPlanarVertex(vertices, uv, center + along - across + up);
        AddPlanarVertex(vertices, uv, center + along + across + up);
        AddPlanarVertex(vertices, uv, center - along + across + up);

        AddQuad(triangles, start, start + 3, start + 2, start + 1);
        AddQuad(triangles, start + 4, start + 5, start + 6, start + 7);
        AddQuad(triangles, start, start + 1, start + 5, start + 4);
        AddQuad(triangles, start + 1, start + 2, start + 6, start + 5);
        AddQuad(triangles, start + 2, start + 3, start + 7, start + 6);
        AddQuad(triangles, start + 3, start, start + 4, start + 7);
    }

    private static void AddPlanarVertex(
        List<Vector3> vertices,
        List<Vector2> uv,
        float x,
        float y,
        float z)
    {
        AddPlanarVertex(vertices, uv, new Vector3(x, y, z));
    }

    private static void AddPlanarVertex(
        List<Vector3> vertices,
        List<Vector2> uv,
        Vector3 vertex)
    {
        vertices.Add(vertex);
        uv.Add(new Vector2(
            0.5f + vertex.x / (OuterRadius * 2f),
            0.5f + vertex.z / (OuterRadius * 2f)));
    }

    private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);
    }

    private static Material CreateOrUpdateMaterial(
        string path,
        string materialName,
        Texture2D texture,
        float metallic,
        float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = materialName
            };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.mainTexture = texture;
        material.mainTextureScale = Vector2.one;
        material.mainTextureOffset = Vector2.zero;
        material.color = Color.white;
        material.enableInstancing = true;
        SetColorIfPresent(material, "_BaseColor", Color.white);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        SetFloatIfPresent(material, "_Metallic", metallic);
        SetFloatIfPresent(material, "_Smoothness", smoothness);
        SetFloatIfPresent(material, "_Surface", 0f);
        SetFloatIfPresent(material, "_Cull", 2f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D LoadAndConfigureTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidOperationException("Wheel texture could not be imported: " + path);
        }

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.npotScale = TextureImporterNPOTScale.ToNearest;
        importer.maxTextureSize = 512;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null)
        {
            throw new InvalidOperationException("Wheel texture asset is missing after import: " + path);
        }

        return texture;
    }

    private static int AssignItemDefinitions(
        Mesh mesh,
        Material ironMaterial,
        Material woodenMaterial)
    {
        int assignedCount = 0;
        string[] guids = AssetDatabase.FindAssets(
            "t:ItemDefinition",
            new[] { ItemDefinitionFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (definition == null)
            {
                continue;
            }

            if (!TryResolveWheelMaterial(
                    definition.itemName,
                    ironMaterial,
                    woodenMaterial,
                    out Material material))
            {
                continue;
            }

            Undo.RecordObject(definition, "Assign Wheel Portable Model");
            definition.portableMesh = mesh;
            definition.portableMat = material;
            EditorUtility.SetDirty(definition);
            assignedCount++;
        }

        return assignedCount;
    }

    private static int AssignLoadedItemManagers(
        Mesh mesh,
        Material ironMaterial,
        Material woodenMaterial)
    {
        int assignedCount = 0;
        ItemManager[] itemManagers = UnityEngine.Object.FindObjectsByType<ItemManager>(
            FindObjectsInactive.Include);

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
                if (!TryResolveWheelMaterial(
                        itemSet.name,
                        ironMaterial,
                        woodenMaterial,
                        out Material material)
                    || (itemSet.portableMesh == mesh && itemSet.portableMat == material))
                {
                    continue;
                }

                if (!recordedUndo)
                {
                    Undo.RecordObject(itemManager, "Assign Wheel Portable Models");
                    recordedUndo = true;
                }

                itemSet.portableMesh = mesh;
                itemSet.portableMat = material;
                itemSets[itemIndex] = itemSet;
                assignedCount++;
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

        return assignedCount;
    }

    private static bool TryResolveWheelMaterial(
        string itemName,
        Material ironMaterial,
        Material woodenMaterial,
        out Material material)
    {
        if (string.Equals(itemName, IronWheelItemName, StringComparison.OrdinalIgnoreCase))
        {
            material = ironMaterial;
            return true;
        }

        if (string.Equals(itemName, WoodenWheelItemName, StringComparison.OrdinalIgnoreCase))
        {
            material = woodenMaterial;
            return true;
        }

        material = null;
        return false;
    }

    private static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, value);
        }
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static void EnsureOutputFolder()
    {
        const string parentFolder = "Assets/Items/Train";
        if (!AssetDatabase.IsValidFolder(parentFolder))
        {
            throw new InvalidOperationException("Missing wheel parent folder: " + parentFolder);
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder(parentFolder, "Wheel");
        }
    }
}
