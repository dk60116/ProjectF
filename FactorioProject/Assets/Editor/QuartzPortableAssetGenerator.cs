using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    public static class QuartzPortableAssetGenerator
    {
        private enum QuartzPalette
        {
            Deep,
            Shadow,
            Mid,
            Milk,
            Light,
            Lavender,
            Vein,
            Glint,
            Count
        }

        private const string QuartzItemName = "Quartz";
        private const string QuartzFolder = "Assets/Items/Ore/Quartz";
        private const string QuartzMeshPath = QuartzFolder + "/Quartz_P.mesh";
        private const string QuartzMaterialPath = QuartzFolder + "/M_Quartz_P.mat";
        private const string QuartzPalettePath = QuartzFolder + "/Quartz_P_TB.png";
        private const string ItemDefinitionFolder = "Assets/Data/Items";

        private const int VertexBudget = 100;
        private const int ExpectedVertexCount = 91;
        private const int ExpectedTriangleCount = 39;

        private static readonly Color32[] PaletteColors =
        {
            new Color32(66, 64, 76, 255),
            new Color32(105, 102, 117, 255),
            new Color32(151, 148, 164, 255),
            new Color32(204, 201, 209, 255),
            new Color32(231, 228, 229, 255),
            new Color32(184, 174, 200, 255),
            new Color32(211, 196, 222, 255),
            new Color32(248, 242, 229, 255)
        };

        [MenuItem("Tools/ProjectF/Generate Quartz Portable Model")]
        public static void GenerateQuartzPortableModel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Quartz Portable Model: exit Play Mode before generating assets.");
                return;
            }

            EnsureOutputFolder();
            Mesh mesh = LowPolyToolAssetUtility.CreateOrUpdateMeshAsset(
                QuartzMeshPath,
                BuildQuartzMesh());
            Texture2D palette = LowPolyToolAssetUtility.CreateOrUpdatePaletteTexture(
                QuartzPalettePath,
                "Quartz_P_TB",
                "Quartz Portable Model",
                PaletteColors);
            Material material = LowPolyToolAssetUtility.CreateOrUpdateMaterial(
                QuartzMaterialPath,
                "M_Quartz_P",
                palette,
                "Quartz Portable Model");
            ConfigureMaterial(material);

            ItemDefinition definition = LowPolyToolAssetUtility.FindItemDefinition(
                ItemDefinitionFolder,
                QuartzItemName);
            if (definition != null)
            {
                Undo.RecordObject(definition, "Assign Quartz Portable Model");
                definition.portableMesh = mesh;
                definition.portableMat = material;
                EditorUtility.SetDirty(definition);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();

            string assignmentResult = definition != null
                ? "assigned to the Quartz ItemDefinition"
                : "generated without assignment because no Quartz ItemDefinition exists yet";
            Debug.Log(
                $"Quartz Portable Model: generated {mesh.vertexCount}/{VertexBudget} vertices and "
                + $"{mesh.GetIndexCount(0) / 3} triangles; {assignmentResult}.");
        }

        [MenuItem("Tools/ProjectF/Validation/Quartz Portable Model")]
        public static void ValidateGeneratedQuartzAssets()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(QuartzMeshPath);
            Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(QuartzPalettePath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(QuartzMaterialPath);
            if (mesh == null || palette == null || material == null)
            {
                throw new InvalidOperationException(
                    "Quartz Portable Model validation failed: generate all Quartz assets first.");
            }

            ValidateMesh(mesh);
            if (palette.width != (int)QuartzPalette.Count || palette.height != 1)
            {
                throw new InvalidOperationException(
                    $"Quartz Portable Model validation failed: palette must be "
                    + $"{(int)QuartzPalette.Count}x1, got {palette.width}x{palette.height}.");
            }

            if (!material.enableInstancing || material.mainTexture != palette)
            {
                throw new InvalidOperationException(
                    "Quartz Portable Model validation failed: material or palette assignment is invalid.");
            }

            Debug.Log(
                $"Quartz Portable Model validation passed: {mesh.vertexCount}/{VertexBudget} vertices, "
                + $"{mesh.GetIndexCount(0) / 3} triangles, bounds {mesh.bounds.size}.");
        }

        private static Mesh BuildQuartzMesh()
        {
            List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
            List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
            List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

            AddCrystal(
                vertices,
                uv,
                triangles,
                5,
                new Vector3(0f, -0.115f, 0.052f),
                0.105f,
                0.084f,
                0.455f,
                Quaternion.Euler(4f, -8f, 5f),
                0);
            AddCrystal(
                vertices,
                uv,
                triangles,
                4,
                new Vector3(-0.066f, -0.106f, -0.052f),
                0.074f,
                0.064f,
                0.318f,
                Quaternion.Euler(-5f, 18f, 17f),
                2);
            AddCrystal(
                vertices,
                uv,
                triangles,
                4,
                new Vector3(0.069f, -0.108f, -0.058f),
                0.068f,
                0.061f,
                0.286f,
                Quaternion.Euler(6f, -15f, -19f),
                4);

            LowPolyToolAssetUtility.CenterVerticesOnOrigin(vertices);
            Mesh mesh = new Mesh
            {
                name = "Quartz_P"
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

        private static void AddCrystal(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            int sideCount,
            Vector3 center,
            float radiusX,
            float radiusZ,
            float height,
            Quaternion rotation,
            int paletteOffset)
        {
            Vector3[] bottom = new Vector3[sideCount];
            Vector3[] shoulder = new Vector3[sideCount];
            float shoulderHeight = height * 0.72f;
            float shoulderRadiusScale = 0.93f;
            float angularOffset = Mathf.PI / sideCount;
            for (int side = 0; side < sideCount; side++)
            {
                float angle = angularOffset + Mathf.PI * 2f * side / sideCount;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);
                bottom[side] = TransformPoint(
                    new Vector3(cos * radiusX, 0f, sin * radiusZ),
                    center,
                    rotation);
                shoulder[side] = TransformPoint(
                    new Vector3(
                        cos * radiusX * shoulderRadiusScale,
                        shoulderHeight,
                        sin * radiusZ * shoulderRadiusScale),
                    center,
                    rotation);
            }

            Vector3 tip = TransformPoint(new Vector3(0f, height, 0f), center, rotation);
            for (int side = 0; side < sideCount; side++)
            {
                int next = (side + 1) % sideCount;
                AddQuad(
                    vertices,
                    uv,
                    triangles,
                    bottom[next],
                    bottom[side],
                    shoulder[side],
                    shoulder[next],
                    ResolveSidePalette(side, paletteOffset));
                AddTriangle(
                    vertices,
                    uv,
                    triangles,
                    shoulder[next],
                    shoulder[side],
                    tip,
                    ResolveTipPalette(side, paletteOffset));
            }
        }

        private static Vector3 TransformPoint(Vector3 localPoint, Vector3 center, Quaternion rotation)
        {
            return center + rotation * localPoint;
        }

        private static QuartzPalette ResolveSidePalette(int side, int offset)
        {
            switch ((side + offset) % 5)
            {
                case 0:
                    return QuartzPalette.Milk;
                case 1:
                    return QuartzPalette.Lavender;
                case 2:
                    return QuartzPalette.Shadow;
                case 3:
                    return QuartzPalette.Deep;
                default:
                    return QuartzPalette.Mid;
            }
        }

        private static QuartzPalette ResolveTipPalette(int side, int offset)
        {
            switch ((side + offset) % 4)
            {
                case 0:
                    return QuartzPalette.Glint;
                case 1:
                    return QuartzPalette.Light;
                case 2:
                    return QuartzPalette.Vein;
                default:
                    return QuartzPalette.Milk;
            }
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            QuartzPalette palette)
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

        private static void AddTriangle(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            QuartzPalette palette)
        {
            int start = vertices.Count;
            AddVertex(vertices, uv, first, palette);
            AddVertex(vertices, uv, second, palette);
            AddVertex(vertices, uv, third, palette);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        private static void AddVertex(
            List<Vector3> vertices,
            List<Vector2> uv,
            Vector3 position,
            QuartzPalette palette)
        {
            vertices.Add(position);
            uv.Add(new Vector2(((int)palette + 0.5f) / (int)QuartzPalette.Count, 0.5f));
        }

        private static void ConfigureMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.02f);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.48f);
            }

            EditorUtility.SetDirty(material);
        }

        private static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder(QuartzFolder))
            {
                throw new InvalidOperationException(
                    $"Quartz Portable Model: output folder '{QuartzFolder}' does not exist.");
            }
        }

        private static void ValidateMesh(Mesh mesh)
        {
            long triangleCount = mesh.GetIndexCount(0) / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Quartz_P topology changed unexpectedly. Expected {ExpectedVertexCount} vertices "
                    + $"and {ExpectedTriangleCount} triangles, but generated {mesh.vertexCount} and "
                    + $"{triangleCount}.");
            }

            if (mesh.vertexCount > VertexBudget)
            {
                throw new InvalidOperationException(
                    $"Quartz_P exceeds the {VertexBudget}-vertex budget: {mesh.vertexCount} vertices.");
            }

            Vector3 center = mesh.bounds.center;
            if (Mathf.Abs(center.x) > 0.0001f || Mathf.Abs(center.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Quartz_P pivot must remain centered on X/Z. Generated bounds center: {center}.");
            }

            if (mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
            {
                throw new InvalidOperationException("Quartz_P must keep one palette UV per vertex.");
            }

            Vector3 size = mesh.bounds.size;
            if (size.z < size.x * 0.5f)
            {
                throw new InvalidOperationException(
                    $"Quartz_P crystals must form a compact cluster instead of a line. Bounds: {size}.");
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
            {
                Vector3 first = vertices[triangles[triangleIndex]];
                Vector3 second = vertices[triangles[triangleIndex + 1]];
                Vector3 third = vertices[triangles[triangleIndex + 2]];
                if (Vector3.Cross(second - first, third - first).sqrMagnitude <= 0.0000000001f)
                {
                    throw new InvalidOperationException(
                        $"Quartz_P contains a degenerate triangle at index {triangleIndex / 3}.");
                }
            }
        }
    }
}
