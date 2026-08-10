using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class AnimalProductPortableAssetGenerator
{
    private const string BeefItemName = "Beef";
    private const string BeefFolder = "Assets/Items/Animal/Beef";
    private const string BeefIconPath = BeefFolder + "/Beef_Icon.png";
    private const string BeefMeshPath = BeefFolder + "/Beef_P.mesh";
    private const string BeefMaterialPath = BeefFolder + "/M_Beef_P.mat";
    private const string ItemDefinitionFolder = "Assets/Data/Items";
    private const float BeefTargetWidth = 0.227f;
    private const float BeefTargetDepth = 0.11f;
    private const float BeefHalfThickness = 0.009f;
    private const int BeefCylinderSegments = 32;
    private const float SideUvInset = 0.06f;
    private static readonly Vector2 BeefTextureCenter = new Vector2(0.5f, 0.53f);
    private static readonly Vector2 BeefTextureRadius = new Vector2(0.30f, 0.18f);

    [MenuItem("Tools/ProjectF/Generate Beef Portable Model")]
    public static void GenerateBeefPortableAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Beef Portable Model: exit Play Mode before generating assets.");
            return;
        }

        Texture2D iconTexture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(BeefIconPath);
        if (iconTexture == null)
        {
            Debug.LogError("Beef Portable Model: missing icon at " + BeefIconPath + ".");
            return;
        }

        Mesh mesh = CreateOrUpdateMesh();
        Material material = CreateOrUpdateMaterial(iconTexture);
        ItemDefinition definition = FindItemDefinition(BeefItemName);
        if (definition == null)
        {
            Debug.LogError(
                "Beef Portable Model: no ItemDefinition named Beef was found in "
                + ItemDefinitionFolder + ".");
            return;
        }

        Undo.RecordObject(definition, "Assign Beef Portable Model");
        definition.portableMesh = mesh;
        definition.portableMat = material;
        EditorUtility.SetDirty(definition);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
        Selection.activeObject = definition;
        Debug.Log(
            $"Beef Portable Model: generated an elongated cylinder with {mesh.vertexCount} vertices and assigned "
            + BeefMeshPath + " and " + BeefMaterialPath + ".");
    }

    [MenuItem("Tools/ProjectF/Generate Beef Portable Model", true)]
    private static bool CanGenerateBeefPortableAssets()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static Mesh CreateOrUpdateMesh()
    {
        Mesh generatedMesh = BuildElongatedCylinderMesh();
        Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(BeefMeshPath);
        if (assetMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, BeefMeshPath);
            return generatedMesh;
        }

        EditorUtility.CopySerialized(generatedMesh, assetMesh);
        UnityEngine.Object.DestroyImmediate(generatedMesh);
        EditorUtility.SetDirty(assetMesh);
        return assetMesh;
    }

    private static Mesh BuildElongatedCylinderMesh()
    {
        const int faceVertexCount = BeefCylinderSegments + 1;
        const int sideVertexCount = (BeefCylinderSegments + 1) * 2;
        float radiusX = BeefTargetWidth * 0.5f;
        float radiusZ = BeefTargetDepth * 0.5f;
        List<Vector3> vertices = new List<Vector3>(
            faceVertexCount * 2 + sideVertexCount);
        List<Vector2> uv = new List<Vector2>(vertices.Capacity);
        List<int> triangles = new List<int>(BeefCylinderSegments * 12);

        int topStart = vertices.Count;
        vertices.Add(new Vector3(0f, BeefHalfThickness, 0f));
        uv.Add(BeefTextureCenter);
        for (int i = 0; i < BeefCylinderSegments; i++)
        {
            AddCylinderRingVertex(
                vertices, uv, i, BeefHalfThickness, radiusX, radiusZ,
                BeefTextureRadius);
        }

        int bottomStart = vertices.Count;
        vertices.Add(new Vector3(0f, -BeefHalfThickness, 0f));
        uv.Add(BeefTextureCenter);
        for (int i = 0; i < BeefCylinderSegments; i++)
        {
            AddCylinderRingVertex(
                vertices, uv, i, -BeefHalfThickness, radiusX, radiusZ,
                BeefTextureRadius);
        }

        for (int i = 0; i < BeefCylinderSegments; i++)
        {
            int current = i + 1;
            int next = (i + 1) % BeefCylinderSegments + 1;
            triangles.Add(topStart);
            triangles.Add(topStart + next);
            triangles.Add(topStart + current);
            triangles.Add(bottomStart);
            triangles.Add(bottomStart + current);
            triangles.Add(bottomStart + next);
        }

        int sideStart = vertices.Count;
        Vector2 sideTextureRadius = BeefTextureRadius * (1f - SideUvInset);
        for (int i = 0; i <= BeefCylinderSegments; i++)
        {
            int ringIndex = i % BeefCylinderSegments;
            AddCylinderRingVertex(
                vertices, uv, ringIndex, -BeefHalfThickness, radiusX, radiusZ,
                sideTextureRadius);
            AddCylinderRingVertex(
                vertices, uv, ringIndex, BeefHalfThickness, radiusX, radiusZ,
                sideTextureRadius);
        }

        for (int i = 0; i < BeefCylinderSegments; i++)
        {
            int bottomCurrent = sideStart + i * 2;
            int topCurrent = bottomCurrent + 1;
            int bottomNext = bottomCurrent + 2;
            int topNext = bottomCurrent + 3;
            triangles.Add(bottomCurrent);
            triangles.Add(topCurrent);
            triangles.Add(topNext);
            triangles.Add(bottomCurrent);
            triangles.Add(topNext);
            triangles.Add(bottomNext);
        }

        Mesh mesh = new Mesh
        {
            name = "Beef_P"
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0, false);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddCylinderRingVertex(
        List<Vector3> vertices,
        List<Vector2> uv,
        int ringIndex,
        float y,
        float radiusX,
        float radiusZ,
        Vector2 textureRadius)
    {
        float angle = Mathf.PI * 2f * ringIndex / BeefCylinderSegments;
        float cosine = Mathf.Cos(angle);
        float sine = Mathf.Sin(angle);
        vertices.Add(new Vector3(
            cosine * radiusX,
            y,
            sine * radiusZ));
        uv.Add(new Vector2(
            BeefTextureCenter.x + cosine * textureRadius.x,
            BeefTextureCenter.y + sine * textureRadius.y));
    }

    private static Material CreateOrUpdateMaterial(Texture2D iconTexture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");
        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(BeefMaterialPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = "M_Beef_P"
            };
            AssetDatabase.CreateAsset(material, BeefMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.mainTexture = iconTexture;
        material.color = Color.white;
        material.enableInstancing = true;
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        material.SetOverrideTag("RenderType", "Opaque");
        SetFloatIfPresent(material, "_Surface", 0f);
        SetFloatIfPresent(material, "_AlphaClip", 0f);
        SetFloatIfPresent(material, "_Smoothness", 0.22f);
        SetFloatIfPresent(material, "_Metallic", 0f);
        SetFloatIfPresent(material, "_Cull", 2f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", iconTexture);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        material.DisableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SetFloatIfPresent(
        Material material,
        string propertyName,
        float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
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
}
