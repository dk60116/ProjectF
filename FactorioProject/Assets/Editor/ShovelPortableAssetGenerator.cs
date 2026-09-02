using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class ShovelPortableAssetGenerator
    {
        private const string ShovelItemName = "Shovel";
        private const string ShovelFolder = "Assets/Items/Equip/Shovel";
        private const string ShovelMeshPath = ShovelFolder + "/Shovel_P.mesh";
        private const string ShovelMaterialPath = ShovelFolder + "/M_Shovel_P.mat";
        private const string ShovelPalettePath = ShovelFolder + "/Shovel_P_TB.png";
        private const string ItemDefinitionFolder = "Assets/Data/Items";

        private const int HandleSides = 6;
        private const int BladePointCount = 7;
        private const int ExpectedVertexCount = 92;
        private const int ExpectedTriangleCount = 52;
        private const int VertexBudget = 99;

        private static readonly Vector2[] BladeOutline =
        {
            new Vector2(-0.026f, -0.030f),
            new Vector2(-0.090f, -0.080f),
            new Vector2(-0.100f, -0.170f),
            new Vector2(0f, -0.245f),
            new Vector2(0.100f, -0.170f),
            new Vector2(0.090f, -0.080f),
            new Vector2(0.026f, -0.030f)
        };

        [MenuItem("Tools/ProjectF/Generate Shovel Portable Model")]
        public static void GenerateShovelPortableModel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Shovel Portable Model: exit Play Mode before generating assets.");
                return;
            }

            Mesh mesh = LowPolyToolAssetUtility.CreateOrUpdateMeshAsset(
                ShovelMeshPath,
                BuildLowPolyShovelMesh());
            Texture2D paletteTexture = LowPolyToolAssetUtility.CreateOrUpdatePaletteTexture(
                ShovelPalettePath,
                "Shovel_P_TB",
                "Shovel Portable Model");
            Material material = LowPolyToolAssetUtility.CreateOrUpdateMaterial(
                ShovelMaterialPath,
                "M_Shovel_P",
                paletteTexture,
                "Shovel Portable Model");

            ItemDefinition definition = LowPolyToolAssetUtility.FindItemDefinition(
                ItemDefinitionFolder,
                ShovelItemName);
            if (definition != null)
            {
                Undo.RecordObject(definition, "Assign Shovel Portable Model");
                definition.portableMesh = mesh;
                definition.portableMat = material;
                definition.isManual = false;
                definition.manualTargetItem = null;
                EditorUtility.SetDirty(definition);
            }
            else
            {
                Debug.LogWarning(
                    $"Shovel Portable Model: generated assets, but no ItemDefinition named "
                    + $"'{ShovelItemName}' was found in {ItemDefinitionFolder}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
            Selection.activeObject = mesh;
            EditorGUIUtility.PingObject(mesh);
            Debug.Log(
                $"Shovel Portable Model: generated {mesh.vertexCount} vertices and "
                + $"{mesh.GetIndexCount(0) / 3} triangles.");
        }

        [MenuItem("Tools/ProjectF/Generate Shovel Portable Model", true)]
        private static bool CanGenerateShovelPortableModel()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static Mesh BuildLowPolyShovelMesh()
        {
            List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
            List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
            List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

            AddShaft(vertices, uv, triangles);
            AddBlade(vertices, uv, triangles);
            AddGrip(vertices, uv, triangles);
            LowPolyToolAssetUtility.CenterVerticesOnOrigin(vertices);

            Mesh mesh = new Mesh
            {
                name = "Shovel_P"
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

        private static void AddShaft(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            Vector3[] bottomRing = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                -0.040f,
                0.014f,
                0.011f);
            Vector3[] topRing = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                0.220f,
                0.013f,
                0.010f);

            for (int i = 0; i < HandleSides; i++)
            {
                int next = (i + 1) % HandleSides;
                LowPolyToolAssetUtility.AddQuad(
                    vertices,
                    uv,
                    triangles,
                    bottomRing[i],
                    topRing[i],
                    topRing[next],
                    bottomRing[next],
                    LowPolyToolAssetUtility.GetWoodFacetPalette(i));
            }
        }

        private static void AddBlade(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            Vector3[] front = new Vector3[BladePointCount];
            Vector3[] back = new Vector3[BladePointCount];
            Vector3 frontCenter = Vector3.zero;
            Vector3 backCenter = Vector3.zero;
            for (int i = 0; i < BladePointCount; i++)
            {
                Vector2 point = BladeOutline[i];
                front[i] = new Vector3(point.x, 0.010f, point.y);
                back[i] = new Vector3(point.x, -0.010f, point.y);
                frontCenter += front[i];
                backCenter += back[i];
            }

            frontCenter /= BladePointCount;
            backCenter /= BladePointCount;
            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                frontCenter,
                front,
                ToolPalette.SteelMid,
                true);
            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                backCenter,
                back,
                ToolPalette.SteelDark,
                false);

            for (int i = 0; i < BladePointCount; i++)
            {
                int next = (i + 1) % BladePointCount;
                LowPolyToolAssetUtility.AddQuad(
                    vertices,
                    uv,
                    triangles,
                    front[i],
                    front[next],
                    back[next],
                    back[i],
                    i >= 2 && i <= 4 ? ToolPalette.SteelEdge : ToolPalette.SteelDeep);
            }
        }

        private static void AddGrip(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            const float left = -0.075f;
            const float right = 0.075f;
            const float bottom = 0.220f;
            const float top = 0.255f;
            const float front = 0.015f;
            const float back = -0.015f;

            AddBoxFace(vertices, uv, triangles,
                new Vector3(left, back, bottom), new Vector3(left, front, bottom),
                new Vector3(right, front, bottom), new Vector3(right, back, bottom),
                ToolPalette.WoodDeep);
            AddBoxFace(vertices, uv, triangles,
                new Vector3(left, back, top), new Vector3(right, back, top),
                new Vector3(right, front, top), new Vector3(left, front, top),
                ToolPalette.WoodLight);
            AddBoxFace(vertices, uv, triangles,
                new Vector3(left, front, bottom), new Vector3(left, front, top),
                new Vector3(right, front, top), new Vector3(right, front, bottom),
                ToolPalette.WoodMid);
            AddBoxFace(vertices, uv, triangles,
                new Vector3(right, back, bottom), new Vector3(right, back, top),
                new Vector3(left, back, top), new Vector3(left, back, bottom),
                ToolPalette.WoodDark);
            AddBoxFace(vertices, uv, triangles,
                new Vector3(left, back, bottom), new Vector3(left, back, top),
                new Vector3(left, front, top), new Vector3(left, front, bottom),
                ToolPalette.WoodDark);
            AddBoxFace(vertices, uv, triangles,
                new Vector3(right, front, bottom), new Vector3(right, front, top),
                new Vector3(right, back, top), new Vector3(right, back, bottom),
                ToolPalette.WoodMid);
        }

        private static void AddBoxFace(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            ToolPalette palette)
        {
            LowPolyToolAssetUtility.AddQuad(
                vertices,
                uv,
                triangles,
                first,
                second,
                third,
                fourth,
                palette);
        }

        private static void ValidateMesh(Mesh mesh)
        {
            long triangleCount = mesh.GetIndexCount(0) / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Shovel_P topology changed unexpectedly. Expected {ExpectedVertexCount} vertices "
                    + $"and {ExpectedTriangleCount} triangles, but generated {mesh.vertexCount} and "
                    + $"{triangleCount}.");
            }

            if (mesh.vertexCount > VertexBudget)
            {
                throw new InvalidOperationException(
                    $"Shovel_P exceeds its low-poly budget: {mesh.vertexCount}/{VertexBudget} vertices.");
            }

            Vector3 boundsCenter = mesh.bounds.center;
            if (Mathf.Abs(boundsCenter.x) > 0.0001f || Mathf.Abs(boundsCenter.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Shovel_P pivot must remain centered. Generated bounds center: {boundsCenter}.");
            }
        }
    }
}
