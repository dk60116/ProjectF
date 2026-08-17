using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class BoxPortableAssetGenerator
{
    private const string ItemDefinitionFolder = "Assets/Data/Items";
    private const string OutputFolder = "Assets/Items/Box";
    private const string WoodenBoxFolder = OutputFolder + "/Wooden box";
    private const string SturdyBoxFolder = OutputFolder + "/Sturdy wooden box";
    private const string MeshPath = OutputFolder + "/Box_P.mesh";
    private const string WoodenTexturePath = WoodenBoxFolder + "/Wooden box_P.png";
    private const string SturdyTexturePath = SturdyBoxFolder + "/Sturdy wooden box_P.png";
    private const string WoodenMaterialPath = WoodenBoxFolder + "/M_Wooden box_P.mat";
    private const string SturdyMaterialPath = SturdyBoxFolder + "/M_Sturdy wooden box_P.mat";

    private const string WoodenBoxItemName = "Wooden box";
    private const string SturdyBoxItemName = "Sturdy wooden box";
    private const int TextureSize = 128;
    private const float FootprintSize = 0.234f;
    private const float BoundsTolerance = 0.0001f;
    private const int LowPolyVertexBudget = 120;
    private const int LowPolyTriangleBudget = 60;

    private static readonly Rect WoodUvRect = new Rect(0.02f, 0.04f, 0.96f, 0.92f);
    private static readonly Color32 WoodenIconWoodColor = new Color32(169, 79, 12, 255);
    private static readonly Color32 SturdyIconWoodColor = new Color32(111, 44, 9, 255);

    [MenuItem("Tools/ProjectF/Generate Box_P Model")]
    public static void GenerateBoxPortableAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Box_P: exit Play Mode before generating portable box assets.");
            return;
        }

        EnsureAssetFolder(OutputFolder);
        EnsureAssetFolder(WoodenBoxFolder);
        EnsureAssetFolder(SturdyBoxFolder);

        Mesh mesh = CreateOrUpdateMesh();
        Texture2D woodenTexture = CreateOrUpdateTexture(
            WoodenTexturePath,
            WoodenIconWoodColor);
        Texture2D sturdyTexture = CreateOrUpdateTexture(
            SturdyTexturePath,
            SturdyIconWoodColor);
        Material woodenMaterial = CreateOrUpdateMaterial(
            WoodenMaterialPath,
            "M_Wooden box_P",
            woodenTexture,
            0f,
            0.18f);
        Material sturdyMaterial = CreateOrUpdateMaterial(
            SturdyMaterialPath,
            "M_Sturdy wooden box_P",
            sturdyTexture,
            0.05f,
            0.22f);

        int assignedDefinitionCount = AssignItemDefinitions(
            mesh,
            woodenMaterial,
            sturdyMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
        Selection.activeObject = mesh;
        EditorGUIUtility.PingObject(mesh);

        Debug.Log(
            $"Box_P: generated open crate mesh with {mesh.vertexCount} vertices and "
            + $"{mesh.GetIndexCount(0) / 3} triangles, then assigned "
            + $"{assignedDefinitionCount} ItemDefinition(s).");
    }

    [MenuItem("Tools/ProjectF/Generate Box_P Model", true)]
    private static bool CanGenerateBoxPortableAssets()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static Mesh CreateOrUpdateMesh()
    {
        Mesh generatedMesh = BuildBoxMesh();
        Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (assetMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, MeshPath);
            ValidateSquareFootprint(CalculateVertexBounds(generatedMesh).size);
            return generatedMesh;
        }

        CopyMeshGeometry(generatedMesh, assetMesh);
        UnityEngine.Object.DestroyImmediate(generatedMesh);
        ValidateSquareFootprint(CalculateVertexBounds(assetMesh).size);
        EditorUtility.SetDirty(assetMesh);
        return assetMesh;
    }

    private static void CopyMeshGeometry(Mesh source, Mesh destination)
    {
        destination.Clear(false);
        destination.name = source.name;
        destination.indexFormat = source.indexFormat;
        destination.vertices = source.vertices;
        destination.normals = source.normals;
        destination.tangents = source.tangents;
        destination.colors = source.colors;
        destination.uv = source.uv;
        destination.subMeshCount = source.subMeshCount;
        for (int subMeshIndex = 0; subMeshIndex < source.subMeshCount; subMeshIndex++)
        {
            destination.SetTriangles(source.GetTriangles(subMeshIndex), subMeshIndex, false);
        }

        destination.bounds = source.bounds;
        destination.UploadMeshData(false);
    }

    private static Mesh BuildBoxMesh()
    {
        List<Vector3> vertices = new List<Vector3>(LowPolyVertexBudget);
        List<Vector2> uvs = new List<Vector2>(LowPolyVertexBudget);
        List<int> triangles = new List<int>(LowPolyTriangleBudget * 3);

        AddBox(
            vertices,
            uvs,
            triangles,
            new Vector3(0f, -0.064f, 0f),
            new Vector3(0.21f, 0.016f, 0.21f));

        const float wallOffset = 0.111f;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            AddBox(
                vertices,
                uvs,
                triangles,
                new Vector3(0f, 0f, wallOffset * sign),
                new Vector3(FootprintSize, 0.15f, 0.012f));
            AddBox(
                vertices,
                uvs,
                triangles,
                new Vector3(wallOffset * sign, 0f, 0f),
                new Vector3(0.012f, 0.15f, 0.21f));
        }

        Mesh mesh = new Mesh
        {
            name = "Box_P"
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, false);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        ValidateSquareFootprint(mesh.bounds.size);
        ValidatePolygonBudget(mesh);
        return mesh;
    }

    private static void ValidatePolygonBudget(Mesh mesh)
    {
        long triangleCount = mesh.GetIndexCount(0) / 3;
        if (mesh.vertexCount <= LowPolyVertexBudget && triangleCount <= LowPolyTriangleBudget)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Box_P exceeds its low-poly budget: {mesh.vertexCount}/{LowPolyVertexBudget} vertices, "
            + $"{triangleCount}/{LowPolyTriangleBudget} triangles.");
    }

    private static void ValidateSquareFootprint(Vector3 size)
    {
        if (Mathf.Abs(size.x - FootprintSize) <= BoundsTolerance
            && Mathf.Abs(size.z - FootprintSize) <= BoundsTolerance)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Box_P footprint must be square. Expected X/Z {FootprintSize:F3}, but generated {size}.");
    }

    private static Bounds CalculateVertexBounds(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
        {
            throw new InvalidOperationException("Box_P has no vertices.");
        }

        Bounds bounds = new Bounds(vertices[0], Vector3.zero);
        for (int vertexIndex = 1; vertexIndex < vertices.Length; vertexIndex++)
        {
            bounds.Encapsulate(vertices[vertexIndex]);
        }

        return bounds;
    }

    private static void AddBox(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> triangles,
        Vector3 center,
        Vector3 size)
    {
        Vector3 half = size * 0.5f;
        Vector3 minimum = center - half;
        Vector3 maximum = center + half;

        AddFace(vertices, uvs, triangles,
            new Vector3(minimum.x, maximum.y, minimum.z),
            new Vector3(minimum.x, maximum.y, maximum.z),
            new Vector3(maximum.x, maximum.y, maximum.z),
            new Vector3(maximum.x, maximum.y, minimum.z));
        AddFace(vertices, uvs, triangles,
            new Vector3(minimum.x, minimum.y, maximum.z),
            new Vector3(minimum.x, minimum.y, minimum.z),
            new Vector3(maximum.x, minimum.y, minimum.z),
            new Vector3(maximum.x, minimum.y, maximum.z));
        AddFace(vertices, uvs, triangles,
            new Vector3(minimum.x, minimum.y, minimum.z),
            new Vector3(minimum.x, maximum.y, minimum.z),
            new Vector3(maximum.x, maximum.y, minimum.z),
            new Vector3(maximum.x, minimum.y, minimum.z));
        AddFace(vertices, uvs, triangles,
            new Vector3(maximum.x, minimum.y, maximum.z),
            new Vector3(maximum.x, maximum.y, maximum.z),
            new Vector3(minimum.x, maximum.y, maximum.z),
            new Vector3(minimum.x, minimum.y, maximum.z));
        AddFace(vertices, uvs, triangles,
            new Vector3(minimum.x, minimum.y, maximum.z),
            new Vector3(minimum.x, maximum.y, maximum.z),
            new Vector3(minimum.x, maximum.y, minimum.z),
            new Vector3(minimum.x, minimum.y, minimum.z));
        AddFace(vertices, uvs, triangles,
            new Vector3(maximum.x, minimum.y, minimum.z),
            new Vector3(maximum.x, maximum.y, minimum.z),
            new Vector3(maximum.x, maximum.y, maximum.z),
            new Vector3(maximum.x, minimum.y, maximum.z));
    }

    private static void AddFace(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        Rect uvRect = WoodUvRect;
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
        uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
        uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
        uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private static Texture2D CreateOrUpdateTexture(
        string assetPath,
        Color32 woodColor)
    {
        Texture2D generatedTexture = new Texture2D(
            TextureSize,
            TextureSize,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = Path.GetFileNameWithoutExtension(assetPath),
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                int variation = Mathf.RoundToInt(
                    Mathf.Sin(y * 0.38f + Mathf.Sin(x * 0.12f) * 1.7f) * 13f)
                    + ((x * 11 + y * 7) % 9) - 4;
                if (y % 31 == 0 || y % 31 == 1)
                {
                    variation -= 22;
                }

                pixels[y * TextureSize + x] = OffsetColor(woodColor, variation);
            }
        }

        generatedTexture.SetPixels32(pixels);
        generatedTexture.Apply(false, false);
        byte[] pngBytes = generatedTexture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(generatedTexture);
        File.WriteAllBytes(assetPath, pngBytes);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = TextureSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Color32 OffsetColor(Color32 color, int offset)
    {
        return new Color32(
            (byte)Mathf.Clamp(color.r + offset, 0, 255),
            (byte)Mathf.Clamp(color.g + offset, 0, 255),
            (byte)Mathf.Clamp(color.b + offset, 0, 255),
            255);
    }

    private static Material CreateOrUpdateMaterial(
        string assetPath,
        string materialName,
        Texture2D texture,
        float metallic,
        float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException("Box_P: supported Lit shader was not found.");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = materialName
            };
            AssetDatabase.CreateAsset(material, assetPath);
        }
        else
        {
            material.shader = shader;
        }

        material.mainTexture = texture;
        material.color = Color.white;
        material.enableInstancing = true;
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

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static int AssignItemDefinitions(
        Mesh mesh,
        Material woodenMaterial,
        Material sturdyMaterial)
    {
        int assignedCount = 0;
        string[] definitionGuids = AssetDatabase.FindAssets(
            "t:ItemDefinition",
            new[] { ItemDefinitionFolder });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                AssetDatabase.GUIDToAssetPath(definitionGuids[i]));
            if (definition == null)
            {
                continue;
            }

            Material targetMaterial;
            if (string.Equals(definition.itemName, WoodenBoxItemName, StringComparison.OrdinalIgnoreCase))
            {
                targetMaterial = woodenMaterial;
            }
            else if (string.Equals(definition.itemName, SturdyBoxItemName, StringComparison.OrdinalIgnoreCase))
            {
                targetMaterial = sturdyMaterial;
            }
            else
            {
                continue;
            }

            if (definition.portableMesh == mesh && definition.portableMat == targetMaterial)
            {
                continue;
            }

            Undo.RecordObject(definition, "Assign Box_P Portable Assets");
            definition.portableMesh = mesh;
            definition.portableMat = targetMaterial;
            EditorUtility.SetDirty(definition);
            assignedCount++;
        }

        return assignedCount;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parentPath = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrWhiteSpace(parentPath)
            || string.IsNullOrWhiteSpace(folderName)
            || !AssetDatabase.IsValidFolder(parentPath))
        {
            throw new DirectoryNotFoundException("Box_P folder parent was not found: " + folderPath);
        }

        AssetDatabase.CreateFolder(parentPath, folderName);
    }
}
