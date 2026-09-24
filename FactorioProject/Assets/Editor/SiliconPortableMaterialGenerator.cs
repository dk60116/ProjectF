using System;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    public static class SiliconPortableMaterialGenerator
    {
        private const string SiliconItemName = "Silicon";
        private const string SiliconFolder = "Assets/Items/Plate/Silicon";
        private const string SiliconMeshPath = SiliconFolder + "/Silicon_P.mesh";
        private const string SiliconTexturePath = SiliconFolder + "/Silicon_P_TB.png";
        private const string SiliconMaterialPath = SiliconFolder + "/M_Silicon_P.mat";
        private const string ItemDefinitionFolder = "Assets/Data/Items";

        private const int ExpectedVertexCount = 68;
        private const int ExpectedTriangleCount = 64;
        private const int ExpectedTextureSize = 2048;

        [MenuItem("Tools/ProjectF/Generate Silicon Portable Material")]
        public static void GenerateSiliconPortableMaterial()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Silicon Portable Material: exit Play Mode before generating assets.");
                return;
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SiliconMeshPath);
            if (mesh == null)
            {
                throw new InvalidOperationException(
                    $"Silicon Portable Material: mesh was not found at '{SiliconMeshPath}'.");
            }

            ConfigureTextureImporter();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SiliconTexturePath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Silicon Portable Material: texture was not found at '{SiliconTexturePath}'.");
            }

            Material material = LowPolyToolAssetUtility.CreateOrUpdateMaterial(
                SiliconMaterialPath,
                "M_Silicon_P",
                texture,
                "Silicon Portable Material");
            ConfigureMaterial(material);

            ItemDefinition definition = LowPolyToolAssetUtility.FindItemDefinition(
                ItemDefinitionFolder,
                SiliconItemName);
            if (definition != null)
            {
                Undo.RecordObject(definition, "Assign Silicon Portable Material");
                definition.portableMesh = mesh;
                definition.portableMat = material;
                EditorUtility.SetDirty(definition);
            }

            AssetDatabase.SaveAssets();
            ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();

            string assignmentResult = definition != null
                ? "assigned to the Silicon ItemDefinition"
                : "generated without assignment because no Silicon ItemDefinition exists yet";
            Debug.Log($"Silicon Portable Material: {assignmentResult}.");
        }

        [MenuItem("Tools/ProjectF/Validation/Silicon Portable Material")]
        public static void ValidateSiliconPortableMaterial()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SiliconMeshPath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SiliconTexturePath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(SiliconMaterialPath);
            if (mesh == null || texture == null || material == null)
            {
                throw new InvalidOperationException(
                    "Silicon Portable Material validation failed: mesh, texture, or material is missing.");
            }

            ValidateMeshAndUv(mesh);
            if (texture.width != ExpectedTextureSize || texture.height != ExpectedTextureSize)
            {
                throw new InvalidOperationException(
                    $"Silicon Portable Material validation failed: texture must be "
                    + $"{ExpectedTextureSize}x{ExpectedTextureSize}, got {texture.width}x{texture.height}.");
            }

            if (!material.enableInstancing || material.mainTexture != texture)
            {
                throw new InvalidOperationException(
                    "Silicon Portable Material validation failed: material texture or instancing is invalid.");
            }

            Debug.Log(
                $"Silicon Portable Material validation passed: {mesh.vertexCount} vertices, "
                + $"{mesh.GetIndexCount(0) / 3} triangles, {texture.width}x{texture.height} texture.");
        }

        private static void ConfigureTextureImporter()
        {
            AssetDatabase.ImportAsset(SiliconTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(SiliconTexturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Silicon Portable Material: texture importer was not found at '{SiliconTexturePath}'.");
            }

            bool changed = importer.textureType != TextureImporterType.Default
                || !importer.sRGBTexture
                || importer.alphaSource != TextureImporterAlphaSource.None
                || importer.wrapMode != TextureWrapMode.Clamp
                || importer.filterMode != FilterMode.Bilinear
                || !importer.mipmapEnabled
                || importer.maxTextureSize != ExpectedTextureSize
                || importer.textureCompression != TextureImporterCompression.CompressedHQ;
            if (!changed)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = ExpectedTextureSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void ConfigureMaterial(Material material)
        {
            SetFloatIfPresent(material, "_Metallic", 0.16f);
            SetFloatIfPresent(material, "_Smoothness", 0.72f);
            EditorUtility.SetDirty(material);
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void ValidateMeshAndUv(Mesh mesh)
        {
            long triangleCount = mesh.GetIndexCount(0) / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Silicon Portable Material validation failed: expected {ExpectedVertexCount} vertices "
                    + $"and {ExpectedTriangleCount} triangles, got {mesh.vertexCount} and {triangleCount}.");
            }

            Vector2[] uv = mesh.uv;
            if (uv == null || uv.Length != ExpectedVertexCount)
            {
                throw new InvalidOperationException(
                    "Silicon Portable Material validation failed: the mesh needs one UV per vertex.");
            }

            ValidateUvPoint(uv[0], new Vector2(0f, 0f), "side strip start");
            ValidateUvPoint(uv[34], new Vector2(0.75f, 0.75f), "bottom center");
            ValidateUvPoint(uv[51], new Vector2(0.25f, 0.75f), "top center");
        }

        private static void ValidateUvPoint(Vector2 actual, Vector2 expected, string label)
        {
            if ((actual - expected).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Silicon Portable Material validation failed: {label} UV must remain "
                    + $"{expected}, got {actual}.");
            }
        }
    }
}
