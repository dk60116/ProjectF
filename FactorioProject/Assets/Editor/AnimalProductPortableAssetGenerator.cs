using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class AnimalProductPortableAssetGenerator
{
    private const string MeatRootFolder = "Assets/Items/Meet";
    private const string MeatMeshPath = MeatRootFolder + "/Meet_P.mesh";
    private const string ItemDefinitionFolder = "Assets/Data/Items";
    private const int PortableTextureMaxSize = 256;

    private const float MeatTargetWidth = 0.23f;
    private const float MeatTargetDepth = 0.165f;
    private const float MeatTopHeight = 0.017f;
    private const float MeatOuterTopHeight = 0.007f;
    private const float MeatOuterBottomHeight = -0.007f;
    private const float MeatBottomHeight = -0.017f;
    private const float MeatInnerRingScale = 0.88f;
    private const int BoneSegments = 8;
    private const int ExpectedVertexCount = 65;
    private const int ExpectedTriangleCount = 116;

    private static readonly Vector2 MeatTextureCenter = new Vector2(0.5f, 0.50625f);
    private static readonly Vector2 MeatTopTextureCenter = new Vector2(0.5f, 0.55f);
    private static readonly Vector2 BoneTextureCenter = new Vector2(0.745f, 0.625f);
    private static readonly Vector2 BoneTextureRadius = new Vector2(0.075f, 0.058f);

    private static readonly MeatAssetSpec[] MeatAssets =
    {
        new MeatAssetSpec("Beef", "Beef", 0.24f),
        new MeatAssetSpec("Beef steak", "Beef steak", 0.30f),
        new MeatAssetSpec("Pork", "Pork", 0.24f),
        new MeatAssetSpec("Pork steak", "Pork steak", 0.30f)
    };

    // Clockwise outline matched to the shared raw/cooked meat icon silhouette.
    private static readonly Vector2[] MeatContourUv =
    {
        new Vector2(0.625f, 0.8125f),
        new Vector2(0.76f, 0.78f),
        new Vector2(0.84f, 0.71f),
        new Vector2(0.89f, 0.62f),
        new Vector2(0.90f, 0.50f),
        new Vector2(0.875f, 0.41f),
        new Vector2(0.82f, 0.36f),
        new Vector2(0.70f, 0.32f),
        new Vector2(0.60f, 0.27f),
        new Vector2(0.47f, 0.20f),
        new Vector2(0.32f, 0.20f),
        new Vector2(0.20f, 0.24f),
        new Vector2(0.13f, 0.32f),
        new Vector2(0.10f, 0.44f),
        new Vector2(0.11f, 0.57f),
        new Vector2(0.17f, 0.67f),
        new Vector2(0.27f, 0.75f),
        new Vector2(0.44f, 0.80f)
    };

    // The cut surface excludes the baked Y-thickness visible in the source icon.
    private static readonly Vector2[] MeatTopSurfaceUv =
    {
        new Vector2(0.625f, 0.80f),
        new Vector2(0.75f, 0.765f),
        new Vector2(0.82f, 0.695f),
        new Vector2(0.865f, 0.61f),
        new Vector2(0.87f, 0.52f),
        new Vector2(0.82f, 0.45f),
        new Vector2(0.75f, 0.41f),
        new Vector2(0.64f, 0.39f),
        new Vector2(0.56f, 0.35f),
        new Vector2(0.47f, 0.30f),
        new Vector2(0.34f, 0.30f),
        new Vector2(0.24f, 0.33f),
        new Vector2(0.17f, 0.39f),
        new Vector2(0.13f, 0.48f),
        new Vector2(0.14f, 0.59f),
        new Vector2(0.20f, 0.68f),
        new Vector2(0.30f, 0.75f),
        new Vector2(0.45f, 0.79f)
    };

    [MenuItem("Tools/ProjectF/Generate Meet Portable Assets")]
    public static void GenerateMeatPortableAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Meet Portable Assets: exit Play Mode before generating assets.");
            return;
        }

        if (!ValidateSourceIcons())
        {
            return;
        }

        Mesh sharedMesh = CreateOrUpdateSharedMesh();
        int assignedDefinitionCount = 0;
        for (int i = 0; i < MeatAssets.Length; i++)
        {
            MeatAssetSpec spec = MeatAssets[i];
            Texture2D portableTexture = CreateOrUpdatePortableTexture(spec);
            Material material = CreateOrUpdateMaterial(spec, portableTexture);
            assignedDefinitionCount += AssignPortableAssets(spec.ItemName, sharedMesh, material);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
        Selection.activeObject = sharedMesh;
        EditorGUIUtility.PingObject(sharedMesh);
        Debug.Log(
            $"Meet Portable Assets: generated shared Meet_P with {sharedMesh.vertexCount} vertices and "
            + $"{sharedMesh.GetIndexCount(0) / 3} triangles, generated {MeatAssets.Length} TB texture(s), "
            + $"and assigned {assignedDefinitionCount} ItemDefinition(s).");
    }

    [MenuItem("Tools/ProjectF/Generate Meet Portable Assets", true)]
    private static bool CanGenerateMeatPortableAssets()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static bool ValidateSourceIcons()
    {
        bool isValid = true;
        for (int i = 0; i < MeatAssets.Length; i++)
        {
            MeatAssetSpec spec = MeatAssets[i];
            if (File.Exists(spec.IconPath))
            {
                continue;
            }

            Debug.LogError(
                $"Meet Portable Assets: source icon for '{spec.ItemName}' was not found at '{spec.IconPath}'.");
            isValid = false;
        }

        return isValid;
    }

    private static Texture2D CreateOrUpdatePortableTexture(MeatAssetSpec spec)
    {
        byte[] sourceBytes = File.ReadAllBytes(spec.IconPath);
        if (!File.Exists(spec.TexturePath)
            || !ByteArraysEqual(sourceBytes, File.ReadAllBytes(spec.TexturePath)))
        {
            File.WriteAllBytes(spec.TexturePath, sourceBytes);
        }

        AssetDatabase.ImportAsset(spec.TexturePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(spec.TexturePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = PortableTextureMaxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.TexturePath);
        if (texture == null)
        {
            throw new InvalidOperationException(
                $"Meet Portable Assets: failed to import TB texture at '{spec.TexturePath}'.");
        }

        return texture;
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static Mesh CreateOrUpdateSharedMesh()
    {
        Mesh generatedMesh = BuildSharedMeatMesh();
        Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeatMeshPath);
        if (assetMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, MeatMeshPath);
            return generatedMesh;
        }

        CopyMeshGeometry(generatedMesh, assetMesh);
        UnityEngine.Object.DestroyImmediate(generatedMesh);
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

    private static Mesh BuildSharedMeatMesh()
    {
        int contourCount = MeatContourUv.Length;
        List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
        List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
        List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

        int topCenter = AddVertex(
            vertices,
            uv,
            new Vector3(0f, MeatTopHeight, 0f),
            MeatTopTextureCenter);
        int topInnerRing = AddContourRing(
            vertices,
            uv,
            MeatInnerRingScale,
            MeatTopHeight,
            MeatTopSurfaceUv,
            MeatTopTextureCenter,
            MeatInnerRingScale);
        int topBevelOuterRing = AddContourRing(
            vertices,
            uv,
            1f,
            MeatOuterTopHeight,
            MeatTopSurfaceUv,
            MeatTopTextureCenter,
            1f);
        int sideLowerRing = AddContourRing(
            vertices,
            uv,
            1f,
            MeatOuterBottomHeight,
            MeatContourUv,
            MeatTextureCenter,
            1f);
        int bottomCenter = AddVertex(
            vertices,
            uv,
            new Vector3(0f, MeatBottomHeight, 0f),
            MeatTextureCenter);

        AddTopFan(triangles, topCenter, topInnerRing, contourCount);
        AddTopBevelBand(triangles, topInnerRing, topBevelOuterRing, contourCount);
        AddOuterSideBand(triangles, topBevelOuterRing, sideLowerRing, contourCount);
        AddBottomFan(triangles, bottomCenter, sideLowerRing, contourCount);
        AddFlatBone(vertices, uv, triangles);

        Mesh mesh = new Mesh
        {
            name = "Meet_P"
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0, false);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        ValidateSharedMesh(mesh);
        return mesh;
    }

    private static int AddContourRing(
        List<Vector3> vertices,
        List<Vector2> uv,
        float worldScale,
        float height,
        IReadOnlyList<Vector2> textureContour,
        Vector2 textureCenter,
        float textureScale)
    {
        if (textureContour == null || textureContour.Count != MeatContourUv.Length)
        {
            throw new ArgumentException(
                "Meet_P texture contour must match the geometry contour.",
                nameof(textureContour));
        }

        int start = vertices.Count;
        for (int i = 0; i < MeatContourUv.Length; i++)
        {
            Vector2 worldUv = Vector2.LerpUnclamped(
                MeatTextureCenter,
                MeatContourUv[i],
                worldScale);
            Vector2 ringUv = Vector2.LerpUnclamped(
                textureCenter,
                textureContour[i],
                textureScale);
            vertices.Add(MeatUvToWorld(worldUv, height));
            uv.Add(ringUv);
        }

        return start;
    }

    private static Vector3 MeatUvToWorld(Vector2 textureCoordinate, float height)
    {
        const float contourUvWidth = 0.80f;
        const float contourUvDepth = 0.6125f;
        return new Vector3(
            (textureCoordinate.x - MeatTextureCenter.x)
            * (MeatTargetWidth / contourUvWidth),
            height,
            (textureCoordinate.y - MeatTextureCenter.y)
            * (MeatTargetDepth / contourUvDepth));
    }

    private static int AddVertex(
        List<Vector3> vertices,
        List<Vector2> uv,
        Vector3 position,
        Vector2 textureCoordinate)
    {
        int index = vertices.Count;
        vertices.Add(position);
        uv.Add(textureCoordinate);
        return index;
    }

    private static void AddTopFan(List<int> triangles, int center, int ringStart, int ringCount)
    {
        for (int i = 0; i < ringCount; i++)
        {
            int next = (i + 1) % ringCount;
            triangles.Add(center);
            triangles.Add(ringStart + i);
            triangles.Add(ringStart + next);
        }
    }

    private static void AddBottomFan(List<int> triangles, int center, int ringStart, int ringCount)
    {
        for (int i = 0; i < ringCount; i++)
        {
            int next = (i + 1) % ringCount;
            triangles.Add(center);
            triangles.Add(ringStart + next);
            triangles.Add(ringStart + i);
        }
    }

    private static void AddTopBevelBand(
        List<int> triangles,
        int innerRingStart,
        int outerRingStart,
        int ringCount)
    {
        for (int i = 0; i < ringCount; i++)
        {
            int next = (i + 1) % ringCount;
            triangles.Add(innerRingStart + i);
            triangles.Add(outerRingStart + i);
            triangles.Add(outerRingStart + next);
            triangles.Add(innerRingStart + i);
            triangles.Add(outerRingStart + next);
            triangles.Add(innerRingStart + next);
        }
    }

    private static void AddOuterSideBand(
        List<int> triangles,
        int upperRingStart,
        int lowerRingStart,
        int ringCount)
    {
        for (int i = 0; i < ringCount; i++)
        {
            int next = (i + 1) % ringCount;
            triangles.Add(upperRingStart + i);
            triangles.Add(lowerRingStart + i);
            triangles.Add(lowerRingStart + next);
            triangles.Add(upperRingStart + i);
            triangles.Add(lowerRingStart + next);
            triangles.Add(upperRingStart + next);
        }
    }

    private static void AddFlatBone(
        List<Vector3> vertices,
        List<Vector2> uv,
        List<int> triangles)
    {
        const float boneHeight = MeatTopHeight + 0.0005f;
        int center = AddVertex(
            vertices,
            uv,
            MeatUvToWorld(BoneTextureCenter, boneHeight),
            BoneTextureCenter);
        int ring = vertices.Count;
        for (int i = 0; i < BoneSegments; i++)
        {
            Vector2 boneUv = GetBoneUv(i);
            AddVertex(vertices, uv, MeatUvToWorld(boneUv, boneHeight), boneUv);
        }

        AddTopFan(triangles, center, ring, BoneSegments);
    }

    private static Vector2 GetBoneUv(int segmentIndex)
    {
        float angle = Mathf.PI * 2f * segmentIndex / BoneSegments;
        return BoneTextureCenter + new Vector2(
            Mathf.Sin(angle) * BoneTextureRadius.x,
            Mathf.Cos(angle) * BoneTextureRadius.y);
    }

    private static void ValidateSharedMesh(Mesh mesh)
    {
        long triangleCount = mesh.GetIndexCount(0) / 3;
        if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
        {
            throw new InvalidOperationException(
                $"Meet_P topology changed unexpectedly. Expected {ExpectedVertexCount} vertices and "
                + $"{ExpectedTriangleCount} triangles, but generated {mesh.vertexCount} and {triangleCount}.");
        }

        Vector3 size = mesh.bounds.size;
        if (Mathf.Abs(size.x - MeatTargetWidth) > 0.0001f
            || Mathf.Abs(size.z - MeatTargetDepth) > 0.0001f)
        {
            throw new InvalidOperationException(
                $"Meet_P bounds do not match the icon silhouette. Generated bounds: {size}.");
        }
    }

    private static Material CreateOrUpdateMaterial(MeatAssetSpec spec, Texture2D portableTexture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException(
                "Meet Portable Assets: supported Lit shader was not found.");
        }

        Material configuredMaterial = new Material(shader)
        {
            name = spec.MaterialName,
            mainTexture = portableTexture,
            color = Color.white,
            enableInstancing = true,
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry
        };
        configuredMaterial.SetOverrideTag("RenderType", "Opaque");
        SetFloatIfPresent(configuredMaterial, "_Surface", 0f);
        SetFloatIfPresent(configuredMaterial, "_AlphaClip", 0f);
        SetFloatIfPresent(configuredMaterial, "_Smoothness", spec.Smoothness);
        SetFloatIfPresent(configuredMaterial, "_Metallic", 0f);
        SetFloatIfPresent(configuredMaterial, "_Cull", 2f);
        SetFloatIfPresent(configuredMaterial, "_ZWrite", 1f);
        if (configuredMaterial.HasProperty("_BaseMap"))
        {
            configuredMaterial.SetTexture("_BaseMap", portableTexture);
        }

        if (configuredMaterial.HasProperty("_BaseColor"))
        {
            configuredMaterial.SetColor("_BaseColor", Color.white);
        }

        configuredMaterial.DisableKeyword("_ALPHATEST_ON");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(spec.MaterialPath);
        if (material == null)
        {
            material = configuredMaterial;
            AssetDatabase.CreateAsset(material, spec.MaterialPath);
        }
        else
        {
            EditorUtility.CopySerialized(configuredMaterial, material);
            UnityEngine.Object.DestroyImmediate(configuredMaterial);
        }

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

    private static int AssignPortableAssets(string itemName, Mesh mesh, Material material)
    {
        ItemDefinition definition = FindItemDefinition(itemName);
        if (definition == null)
        {
            Debug.LogWarning(
                $"Meet Portable Assets: no ItemDefinition named '{itemName}' was found in "
                + ItemDefinitionFolder + ".");
            return 0;
        }

        Undo.RecordObject(definition, "Assign " + itemName + " Portable Assets");
        definition.portableMesh = mesh;
        definition.portableMat = material;
        EditorUtility.SetDirty(definition);
        return 1;
    }

    private static ItemDefinition FindItemDefinition(string itemName)
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:ItemDefinition",
            new[] { ItemDefinitionFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            ItemDefinition definition =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
            if (definition != null
                && string.Equals(
                    definition.itemName,
                    itemName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }

    private readonly struct MeatAssetSpec
    {
        public readonly string ItemName;
        public readonly string IconPath;
        public readonly string TexturePath;
        public readonly string MaterialPath;
        public readonly string MaterialName;
        public readonly float Smoothness;

        public MeatAssetSpec(string itemName, string folderName, float smoothness)
        {
            string folder = MeatRootFolder + "/" + folderName;
            ItemName = itemName;
            IconPath = folder + "/" + itemName + "_Icon.png";
            TexturePath = folder + "/" + itemName + "_P_TB.png";
            MaterialPath = folder + "/M_" + itemName + "_P.mat";
            MaterialName = "M_" + itemName + "_P";
            Smoothness = smoothness;
        }
    }
}
