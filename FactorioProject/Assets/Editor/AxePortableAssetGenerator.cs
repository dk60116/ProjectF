using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class AxePortableAssetGenerator
    {
        private const string AxeItemName = "Axe";
        private const string AxeFolder = "Assets/Items/Equip/Axe";
        private const string AxeMeshPath = AxeFolder + "/Axe_P.mesh";
        private const string AxeMaterialPath = AxeFolder + "/Axe_P_M.mat";
        private const string AxePalettePath = AxeFolder + "/Axe_P_TB.png";
        private const string ItemDefinitionFolder = "Assets/Data/Items";

        private const int HandleSides = 6;
        private const int HeadPointCount = 6;
        private const int ExpectedVertexCount = 76;
        private const int ExpectedTriangleCount = 48;

        private static readonly Vector2[] HeadOutline =
        {
            new Vector2(-0.060f, 0.140f),
            new Vector2(0.045f, 0.155f),
            new Vector2(0.120f, 0.140f),
            new Vector2(0.135f, 0.050f),
            new Vector2(0.045f, 0.075f),
            new Vector2(-0.060f, 0.090f)
        };

        [MenuItem("Tools/ProjectF/Generate Axe Portable Model")]
        public static void GenerateAxePortableModel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Axe Portable Model: exit Play Mode before generating assets.");
                return;
            }

            Mesh mesh = LowPolyToolAssetUtility.CreateOrUpdateMeshAsset(
                AxeMeshPath,
                BuildLowPolyAxeMesh());
            Texture2D paletteTexture = LowPolyToolAssetUtility.CreateOrUpdatePaletteTexture(
                AxePalettePath,
                "Axe_P_TB",
                "Axe Portable Model");
            Material material = LowPolyToolAssetUtility.CreateOrUpdateMaterial(
                AxeMaterialPath,
                "Axe_P_M",
                paletteTexture,
                "Axe Portable Model");
            ItemDefinition definition = LowPolyToolAssetUtility.FindItemDefinition(
                ItemDefinitionFolder,
                AxeItemName);
            if (definition == null)
            {
                Debug.LogError(
                    $"Axe Portable Model: no ItemDefinition named '{AxeItemName}' was found in "
                    + ItemDefinitionFolder + ".");
                return;
            }

            Undo.RecordObject(definition, "Assign Axe Portable Model");
            definition.portableMesh = mesh;
            definition.portableMat = material;
            definition.isManual = false;
            definition.manualTargetItem = null;
            EditorUtility.SetDirty(definition);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
            Selection.activeObject = mesh;
            EditorGUIUtility.PingObject(mesh);
            Debug.Log(
                $"Axe Portable Model: generated {mesh.vertexCount} vertices and "
                + $"{mesh.GetIndexCount(0) / 3} triangles, then assigned it to Axe.");
        }

        [MenuItem("Tools/ProjectF/Generate Axe Portable Model", true)]
        private static bool CanGenerateAxePortableModel()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static Mesh BuildLowPolyAxeMesh()
        {
            List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
            List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
            List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

            AddHandle(vertices, uv, triangles);
            AddHead(vertices, uv, triangles);
            LowPolyToolAssetUtility.CenterVerticesOnOrigin(vertices);

            Mesh mesh = new Mesh
            {
                name = "Axe_P"
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

        private static void AddHandle(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            const float bottomZ = -0.160f;
            const float topZ = 0.105f;
            const float bottomRadiusX = 0.021f;
            const float bottomRadiusY = 0.016f;
            const float topRadiusX = 0.014f;
            const float topRadiusY = 0.012f;

            Vector3[] bottomRing = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                bottomZ,
                bottomRadiusX,
                bottomRadiusY);
            Vector3[] topRing = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                topZ,
                topRadiusX,
                topRadiusY);
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

            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                new Vector3(0f, 0f, bottomZ),
                bottomRing,
                ToolPalette.WoodDeep,
                false);
            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                new Vector3(0f, 0f, topZ),
                topRing,
                ToolPalette.WoodLight,
                true);
        }

        private static void AddHead(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            Vector3[] top = new Vector3[HeadPointCount];
            Vector3[] bottom = new Vector3[HeadPointCount];
            Vector3 centerTop = Vector3.zero;
            Vector3 centerBottom = Vector3.zero;
            for (int i = 0; i < HeadPointCount; i++)
            {
                Vector2 point = HeadOutline[i];
                float halfThickness = GetHeadHalfThickness(i);
                top[i] = new Vector3(point.x, halfThickness, point.y);
                bottom[i] = new Vector3(point.x, -halfThickness, point.y);
                centerTop += top[i];
                centerBottom += bottom[i];
            }

            centerTop /= HeadPointCount;
            centerBottom /= HeadPointCount;
            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                centerTop,
                top,
                ToolPalette.SteelMid,
                false);
            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                centerBottom,
                bottom,
                ToolPalette.SteelDark,
                true);

            for (int i = 0; i < HeadPointCount; i++)
            {
                int next = (i + 1) % HeadPointCount;
                LowPolyToolAssetUtility.AddQuad(
                    vertices,
                    uv,
                    triangles,
                    bottom[i],
                    bottom[next],
                    top[next],
                    top[i],
                    GetHeadSidePalette(i));
            }
        }

        private static ToolPalette GetHeadSidePalette(int sideIndex)
        {
            if (sideIndex == 2)
            {
                return ToolPalette.SteelEdge;
            }

            if (sideIndex == 1 || sideIndex == 3)
            {
                return ToolPalette.SteelLight;
            }

            return ToolPalette.SteelDeep;
        }

        private static float GetHeadHalfThickness(int pointIndex)
        {
            if (pointIndex == 2 || pointIndex == 3)
            {
                return 0.0015f;
            }

            if (pointIndex == 1 || pointIndex == 4)
            {
                return 0.012f;
            }

            return 0.018f;
        }

        private static void ValidateMesh(Mesh mesh)
        {
            long triangleCount = mesh.GetIndexCount(0) / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Axe_P topology changed unexpectedly. Expected {ExpectedVertexCount} vertices and "
                    + $"{ExpectedTriangleCount} triangles, but generated {mesh.vertexCount} and "
                    + $"{triangleCount}.");
            }

            Vector3 boundsCenter = mesh.bounds.center;
            if (Mathf.Abs(boundsCenter.x) > 0.0001f || Mathf.Abs(boundsCenter.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Axe_P pivot must remain centered. Generated bounds center: {boundsCenter}.");
            }
        }

    }
}
