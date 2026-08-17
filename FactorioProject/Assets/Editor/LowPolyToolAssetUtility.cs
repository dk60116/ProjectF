using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal enum ToolPalette
    {
        WoodDeep,
        WoodDark,
        WoodMid,
        WoodLight,
        SteelDeep,
        SteelDark,
        SteelMid,
        SteelLight,
        SteelEdge,
        Count
    }

    internal static class LowPolyToolAssetUtility
    {
        private static readonly Color32[] PaletteColors =
        {
            new Color32(70, 28, 10, 255),
            new Color32(116, 48, 12, 255),
            new Color32(180, 82, 14, 255),
            new Color32(255, 157, 25, 255),
            new Color32(32, 40, 47, 255),
            new Color32(67, 77, 85, 255),
            new Color32(124, 138, 147, 255),
            new Color32(187, 200, 207, 255),
            new Color32(238, 245, 247, 255)
        };

        internal static Mesh CreateOrUpdateMeshAsset(string assetPath, Mesh generatedMesh)
        {
            Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (assetMesh == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, assetPath);
                return generatedMesh;
            }

            assetMesh.Clear(false);
            assetMesh.name = generatedMesh.name;
            assetMesh.vertices = generatedMesh.vertices;
            assetMesh.normals = generatedMesh.normals;
            assetMesh.tangents = generatedMesh.tangents;
            assetMesh.uv = generatedMesh.uv;
            assetMesh.triangles = generatedMesh.triangles;
            assetMesh.bounds = generatedMesh.bounds;
            assetMesh.UploadMeshData(false);
            UnityEngine.Object.DestroyImmediate(generatedMesh);
            EditorUtility.SetDirty(assetMesh);
            return assetMesh;
        }

        internal static void CenterVerticesOnOrigin(List<Vector3> vertices)
        {
            if (vertices == null || vertices.Count == 0)
            {
                return;
            }

            Vector3 min = vertices[0];
            Vector3 max = vertices[0];
            for (int i = 1; i < vertices.Count; i++)
            {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }

            Vector3 offset = new Vector3(
                -(min.x + max.x) * 0.5f,
                0f,
                -(min.z + max.z) * 0.5f);
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] += offset;
            }
        }

        internal static Vector3[] BuildRadialRing(
            int sideCount,
            float z,
            float radiusX,
            float radiusY)
        {
            Vector3[] ring = new Vector3[sideCount];
            for (int i = 0; i < sideCount; i++)
            {
                float angle = Mathf.PI * 2f * i / sideCount;
                ring[i] = new Vector3(
                    Mathf.Sin(angle) * radiusX,
                    Mathf.Cos(angle) * radiusY,
                    z);
            }

            return ring;
        }

        internal static void AddPolygonFan(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 center,
            IReadOnlyList<Vector3> ring,
            ToolPalette palette,
            bool reverseWinding)
        {
            int centerIndex = AddVertex(vertices, uv, center, palette);
            int ringStart = vertices.Count;
            for (int i = 0; i < ring.Count; i++)
            {
                AddVertex(vertices, uv, ring[i], palette);
            }

            for (int i = 0; i < ring.Count; i++)
            {
                int next = (i + 1) % ring.Count;
                triangles.Add(centerIndex);
                triangles.Add(ringStart + (reverseWinding ? next : i));
                triangles.Add(ringStart + (reverseWinding ? i : next));
            }
        }

        internal static void AddQuad(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            ToolPalette palette)
        {
            int start = vertices.Count;
            AddVertex(vertices, uv, first, palette);
            AddVertex(vertices, uv, second, palette);
            AddVertex(vertices, uv, third, palette);
            AddVertex(vertices, uv, fourth, palette);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        internal static void AddTriangle(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            ToolPalette palette)
        {
            int start = vertices.Count;
            AddVertex(vertices, uv, first, palette);
            AddVertex(vertices, uv, second, palette);
            AddVertex(vertices, uv, third, palette);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        internal static int AddVertex(
            List<Vector3> vertices,
            List<Vector2> uv,
            Vector3 position,
            ToolPalette palette)
        {
            int index = vertices.Count;
            vertices.Add(position);
            uv.Add(new Vector2(((int)palette + 0.5f) / (int)ToolPalette.Count, 0.5f));
            return index;
        }

        internal static ToolPalette GetWoodFacetPalette(int sideIndex)
        {
            switch (sideIndex)
            {
                case 0:
                    return ToolPalette.WoodLight;
                case 1:
                case 5:
                    return ToolPalette.WoodMid;
                case 2:
                case 4:
                    return ToolPalette.WoodDark;
                default:
                    return ToolPalette.WoodDeep;
            }
        }

        internal static Texture2D CreateOrUpdatePaletteTexture(
            string assetPath,
            string textureName,
            string logContext)
        {
            Texture2D palette = new Texture2D(
                (int)ToolPalette.Count,
                1,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            palette.SetPixels32(PaletteColors);
            palette.Apply(false, false);
            byte[] pngBytes = palette.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(palette);

            if (!File.Exists(assetPath)
                || !ByteArraysEqual(pngBytes, File.ReadAllBytes(assetPath)))
            {
                File.WriteAllBytes(assetPath, pngBytes);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"{logContext}: failed to import palette texture at '{assetPath}'.");
            }

            return texture;
        }

        internal static Material CreateOrUpdateMaterial(
            string assetPath,
            string materialName,
            Texture2D paletteTexture,
            string logContext)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException($"{logContext}: supported Lit shader was not found.");
            }

            Material configuredMaterial = new Material(shader)
            {
                name = materialName,
                mainTexture = paletteTexture,
                color = Color.white,
                enableInstancing = true,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry
            };
            configuredMaterial.SetOverrideTag("RenderType", "Opaque");
            SetFloatIfPresent(configuredMaterial, "_Surface", 0f);
            SetFloatIfPresent(configuredMaterial, "_AlphaClip", 0f);
            SetFloatIfPresent(configuredMaterial, "_Smoothness", 0.22f);
            SetFloatIfPresent(configuredMaterial, "_Metallic", 0.08f);
            SetFloatIfPresent(configuredMaterial, "_Cull", 2f);
            SetFloatIfPresent(configuredMaterial, "_ZWrite", 1f);
            if (configuredMaterial.HasProperty("_BaseMap"))
            {
                configuredMaterial.SetTexture("_BaseMap", paletteTexture);
            }

            if (configuredMaterial.HasProperty("_BaseColor"))
            {
                configuredMaterial.SetColor("_BaseColor", Color.white);
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = configuredMaterial;
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                EditorUtility.CopySerialized(configuredMaterial, material);
                UnityEngine.Object.DestroyImmediate(configuredMaterial);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        internal static ItemDefinition FindItemDefinition(string folder, string itemName)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (definition != null
                    && string.Equals(definition.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
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

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
    }
}
