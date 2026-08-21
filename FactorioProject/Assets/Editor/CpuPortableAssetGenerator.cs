using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class CpuPortableAssetGenerator
    {
        private enum CpuPalette
        {
            BoardDark,
            BoardMid,
            BoardLight,
            PackageDeep,
            PackageDark,
            PackageMid,
            Gold,
            Count
        }

        private const string CpuItemName = "CPU";
        private const string CpuFolder = "Assets/Items/Plate/CPU";
        private const string CpuMeshPath = CpuFolder + "/CPU_P.mesh";
        private const string CpuMaterialPath = CpuFolder + "/M_CPU_P.mat";
        private const string CpuPalettePath = CpuFolder + "/CPU_P_TB.png";
        private const string ItemDefinitionFolder = "Assets/Data/Items";

        private const int EdgePinCount = 3;
        private const int ExpectedVertexCount = 144;
        private const int ExpectedTriangleCount = 168;

        private const float BoardHalfSize = 0.135f;
        private const float BoardBottomY = -0.025f;
        private const float BoardTopY = 0.012f;

        private const float PackageHalfSize = 0.1f;
        private const float PackageBottomY = 0.008f;
        private const float PackageTopY = 0.054f;

        private const float PinHalfWidth = 0.009f;
        private const float PinHalfLength = 0.025f;
        private const float PinCenterFromOrigin = 0.142f;
        private const float PinBottomY = -0.006f;
        private const float PinTopY = 0.022f;

        private static readonly float[] PinOffsets =
        {
            -0.058f,
            0f,
            0.058f
        };

        private static readonly Color32[] PaletteColors =
        {
            new Color32(5, 54, 31, 255),
            new Color32(8, 112, 59, 255),
            new Color32(22, 174, 89, 255),
            new Color32(14, 16, 19, 255),
            new Color32(35, 39, 45, 255),
            new Color32(61, 66, 74, 255),
            new Color32(232, 157, 20, 255)
        };

        [MenuItem("Tools/ProjectF/Generate CPU Portable Model %#F10")]
        public static void GenerateCpuPortableModel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("CPU Portable Model: exit Play Mode before generating assets.");
                return;
            }

            EnsureOutputFolder();
            Mesh mesh = LowPolyToolAssetUtility.CreateOrUpdateMeshAsset(
                CpuMeshPath,
                BuildLowPolyCpuMesh());
            Texture2D paletteTexture = LowPolyToolAssetUtility.CreateOrUpdatePaletteTexture(
                CpuPalettePath,
                "CPU_P_TB",
                "CPU Portable Model",
                PaletteColors);
            Material material = LowPolyToolAssetUtility.CreateOrUpdateMaterial(
                CpuMaterialPath,
                "M_CPU_P",
                paletteTexture,
                "CPU Portable Model");
            ConfigureMaterial(material);

            ItemDefinition definition = LowPolyToolAssetUtility.FindItemDefinition(
                ItemDefinitionFolder,
                CpuItemName);
            if (definition != null)
            {
                Undo.RecordObject(definition, "Assign CPU Portable Model");
                definition.portableMesh = mesh;
                definition.portableMat = material;
                EditorUtility.SetDirty(definition);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
            Selection.activeObject = mesh;
            EditorGUIUtility.PingObject(mesh);

            string assignmentResult = definition != null
                ? "assigned to the CPU ItemDefinition"
                : "generated without assignment because no CPU ItemDefinition exists yet";
            Debug.Log(
                $"CPU Portable Model: generated {mesh.vertexCount} vertices and "
                + $"{mesh.GetIndexCount(0) / 3} triangles; {assignmentResult}.");
        }

        [MenuItem("Tools/ProjectF/Generate CPU Portable Model %#F10", true)]
        private static bool CanGenerateCpuPortableModel()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("Tools/ProjectF/Validation/CPU Portable Model")]
        public static void ValidateGeneratedCpuAssets()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(CpuMeshPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CpuMaterialPath);
            Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(CpuPalettePath);
            if (mesh == null || material == null || palette == null)
            {
                throw new InvalidOperationException(
                    "CPU Portable Model validation failed: generate all CPU assets first.");
            }

            ValidateMesh(mesh);
            if (!material.enableInstancing || material.mainTexture != palette)
            {
                throw new InvalidOperationException(
                    "CPU Portable Model validation failed: material or palette assignment is invalid.");
            }

            Debug.Log(
                $"CPU Portable Model validation passed: {mesh.vertexCount} vertices, "
                + $"{mesh.GetIndexCount(0) / 3} triangles, symmetric bounds {mesh.bounds.size}.");
        }

        private static Mesh BuildLowPolyCpuMesh()
        {
            List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
            List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
            List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

            AddSimplePrism(
                vertices,
                uv,
                triangles,
                BoardHalfSize,
                BoardBottomY,
                BoardTopY,
                CpuPalette.BoardMid,
                CpuPalette.BoardDark,
                CpuPalette.BoardLight);
            AddPins(vertices, uv, triangles);
            AddSimplePrism(
                vertices,
                uv,
                triangles,
                PackageHalfSize,
                PackageBottomY,
                PackageTopY,
                CpuPalette.PackageDark,
                CpuPalette.PackageDeep,
                CpuPalette.PackageMid);

            Mesh mesh = new Mesh
            {
                name = "CPU_P"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            ValidateMesh(mesh);
            return mesh;
        }

        private static void AddPins(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            float halfHeight = (PinTopY - PinBottomY) * 0.5f;
            float centerY = (PinTopY + PinBottomY) * 0.5f;
            for (int side = 0; side < 4; side++)
            {
                for (int pinIndex = 0; pinIndex < EdgePinCount; pinIndex++)
                {
                    float offset = PinOffsets[pinIndex];
                    bool alongX = side < 2;
                    float sideSign = (side & 1) == 0 ? -1f : 1f;
                    Vector3 center = alongX
                        ? new Vector3(offset, centerY, sideSign * PinCenterFromOrigin)
                        : new Vector3(sideSign * PinCenterFromOrigin, centerY, offset);
                    Vector3 halfExtents = alongX
                        ? new Vector3(PinHalfWidth, halfHeight, PinHalfLength)
                        : new Vector3(PinHalfLength, halfHeight, PinHalfWidth);
                    AddSharedBox(
                        vertices,
                        uv,
                        triangles,
                        center,
                        halfExtents,
                        CpuPalette.Gold);
                }
            }
        }

        private static void AddSimplePrism(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            float halfSize,
            float bottomY,
            float topY,
            CpuPalette topPalette,
            CpuPalette bottomPalette,
            CpuPalette sideHighlightPalette)
        {
            Vector3 bottomFrontLeft = new Vector3(-halfSize, bottomY, -halfSize);
            Vector3 bottomFrontRight = new Vector3(halfSize, bottomY, -halfSize);
            Vector3 bottomBackRight = new Vector3(halfSize, bottomY, halfSize);
            Vector3 bottomBackLeft = new Vector3(-halfSize, bottomY, halfSize);
            Vector3 topFrontLeft = new Vector3(-halfSize, topY, -halfSize);
            Vector3 topFrontRight = new Vector3(halfSize, topY, -halfSize);
            Vector3 topBackRight = new Vector3(halfSize, topY, halfSize);
            Vector3 topBackLeft = new Vector3(-halfSize, topY, halfSize);

            AddQuad(
                vertices, uv, triangles,
                topFrontLeft, topBackLeft, topBackRight, topFrontRight,
                topPalette);
            AddQuad(
                vertices, uv, triangles,
                bottomFrontLeft, bottomFrontRight, bottomBackRight, bottomBackLeft,
                bottomPalette);
            AddQuad(
                vertices, uv, triangles,
                bottomFrontLeft, topFrontLeft, topFrontRight, bottomFrontRight,
                bottomPalette);
            AddQuad(
                vertices, uv, triangles,
                bottomFrontRight, topFrontRight, topBackRight, bottomBackRight,
                sideHighlightPalette);
            AddQuad(
                vertices, uv, triangles,
                bottomBackRight, topBackRight, topBackLeft, bottomBackLeft,
                sideHighlightPalette);
            AddQuad(
                vertices, uv, triangles,
                bottomBackLeft, topBackLeft, topFrontLeft, bottomFrontLeft,
                bottomPalette);
        }

        private static void AddSharedBox(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 center,
            Vector3 halfExtents,
            CpuPalette palette)
        {
            int start = vertices.Count;
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    for (int x = -1; x <= 1; x += 2)
                    {
                        vertices.Add(center + new Vector3(
                            x * halfExtents.x,
                            y * halfExtents.y,
                            z * halfExtents.z));
                        uv.Add(PaletteUv(palette));
                    }
                }
            }

            AddSharedQuadIndices(triangles, start + 0, start + 1, start + 3, start + 2);
            AddSharedQuadIndices(triangles, start + 4, start + 6, start + 7, start + 5);
            AddSharedQuadIndices(triangles, start + 0, start + 4, start + 5, start + 1);
            AddSharedQuadIndices(triangles, start + 2, start + 3, start + 7, start + 6);
            AddSharedQuadIndices(triangles, start + 0, start + 2, start + 6, start + 4);
            AddSharedQuadIndices(triangles, start + 1, start + 5, start + 7, start + 3);
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            CpuPalette palette)
        {
            int start = vertices.Count;
            AddVertex(vertices, uv, first, palette);
            AddVertex(vertices, uv, second, palette);
            AddVertex(vertices, uv, third, palette);
            AddVertex(vertices, uv, fourth, palette);
            AddSharedQuadIndices(triangles, start, start + 1, start + 2, start + 3);
        }

        private static int AddVertex(
            List<Vector3> vertices,
            List<Vector2> uv,
            Vector3 position,
            CpuPalette palette)
        {
            int index = vertices.Count;
            vertices.Add(position);
            uv.Add(PaletteUv(palette));
            return index;
        }

        private static Vector2 PaletteUv(CpuPalette palette)
        {
            return new Vector2(((int)palette + 0.5f) / (int)CpuPalette.Count, 0.5f);
        }

        private static void AddSharedQuadIndices(
            List<int> triangles,
            int first,
            int second,
            int third,
            int fourth)
        {
            triangles.Add(first);
            triangles.Add(second);
            triangles.Add(third);
            triangles.Add(first);
            triangles.Add(third);
            triangles.Add(fourth);
        }

        private static void ConfigureMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.22f);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.28f);
            }

            EditorUtility.SetDirty(material);
        }

        private static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder(CpuFolder))
            {
                throw new InvalidOperationException(
                    $"CPU Portable Model: output folder '{CpuFolder}' does not exist.");
            }
        }

        private static void ValidateMesh(Mesh mesh)
        {
            long triangleCount = mesh.GetIndexCount(0) / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"CPU Portable Model topology changed unexpectedly. Expected "
                    + $"{ExpectedVertexCount} vertices and {ExpectedTriangleCount} triangles, but "
                    + $"generated {mesh.vertexCount} and {triangleCount}.");
            }

            Vector3 center = mesh.bounds.center;
            if (Mathf.Abs(center.x) > 0.0001f || Mathf.Abs(center.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"CPU Portable Model pivot must remain centered. Generated bounds center: {center}.");
            }

            Vector3 size = mesh.bounds.size;
            if (Mathf.Abs(size.x - size.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"CPU Portable Model must remain symmetric. Generated bounds size: {size}.");
            }
        }
    }
}
