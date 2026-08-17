using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class KnifePortableAssetGenerator
    {
        private const string KnifeItemName = "Knife";
        private const string KnifeFolder = "Assets/Items/Equip/Knife";
        private const string KnifeMeshPath = KnifeFolder + "/Knife_P.mesh";
        private const string KnifeMaterialPath = KnifeFolder + "/M_Knife_P.mat";
        private const string KnifePalettePath = KnifeFolder + "/Knife_P_TB.png";
        private const string ItemDefinitionFolder = "Assets/Data/Items";

        private const int HandleSides = 4;
        private const int BladePointCount = 5;
        private const int ExpectedVertexCount = 126;
        private const int ExpectedTriangleCount = 58;

        // Clockwise when viewed from the front (+Y). The offset tip follows the icon silhouette.
        private static readonly Vector2[] BladeOutline =
        {
            new Vector2(-0.030f, 0.040f),
            new Vector2(-0.034f, 0.205f),
            new Vector2(0.012f, 0.340f),
            new Vector2(0.045f, 0.198f),
            new Vector2(0.030f, 0.040f)
        };

        private static readonly float[] BladeHalfThickness =
        {
            0.009f,
            0.007f,
            0.0015f,
            0.003f,
            0.008f
        };

        [MenuItem("Tools/ProjectF/Generate Knife Portable Model")]
        public static void GenerateKnifePortableModel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Knife Portable Model: exit Play Mode before generating assets.");
                return;
            }

            Mesh mesh = LowPolyToolAssetUtility.CreateOrUpdateMeshAsset(
                KnifeMeshPath,
                BuildLowPolyKnifeMesh());
            Texture2D paletteTexture = LowPolyToolAssetUtility.CreateOrUpdatePaletteTexture(
                KnifePalettePath,
                "Knife_P_TB",
                "Knife Portable Model");
            Material material = LowPolyToolAssetUtility.CreateOrUpdateMaterial(
                KnifeMaterialPath,
                "M_Knife_P",
                paletteTexture,
                "Knife Portable Model");
            ItemDefinition definition = LowPolyToolAssetUtility.FindItemDefinition(
                ItemDefinitionFolder,
                KnifeItemName);
            if (definition == null)
            {
                Debug.LogError(
                    $"Knife Portable Model: no ItemDefinition named '{KnifeItemName}' was found in "
                    + ItemDefinitionFolder + ".");
                return;
            }

            Undo.RecordObject(definition, "Assign Knife Portable Model");
            definition.portableMesh = mesh;
            definition.portableMat = material;
            EditorUtility.SetDirty(definition);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
            Selection.activeObject = mesh;
            EditorGUIUtility.PingObject(mesh);
            Debug.Log(
                $"Knife Portable Model: generated {mesh.vertexCount} vertices and "
                + $"{mesh.GetIndexCount(0) / 3} triangles, then assigned it to Knife.");
        }

        [MenuItem("Tools/ProjectF/Generate Knife Portable Model", true)]
        private static bool CanGenerateKnifePortableModel()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static Mesh BuildLowPolyKnifeMesh()
        {
            List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
            List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
            List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

            AddHandle(vertices, uv, triangles);
            AddGuard(vertices, uv, triangles);
            AddBlade(vertices, uv, triangles);
            LowPolyToolAssetUtility.CenterVerticesOnOrigin(vertices);

            Mesh mesh = new Mesh
            {
                name = "Knife_P"
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
            Vector3[] bottom = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                -0.170f,
                0.024f,
                0.016f);
            Vector3[] bandBottom = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                -0.075f,
                0.030f,
                0.018f);
            Vector3[] bandTop = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                -0.047f,
                0.030f,
                0.018f);
            Vector3[] top = LowPolyToolAssetUtility.BuildRadialRing(
                HandleSides,
                0.025f,
                0.026f,
                0.017f);

            AddHandleSegment(vertices, uv, triangles, bottom, bandBottom, false);
            AddHandleSegment(vertices, uv, triangles, bandBottom, bandTop, true);
            AddHandleSegment(vertices, uv, triangles, bandTop, top, false);

            LowPolyToolAssetUtility.AddQuad(
                vertices,
                uv,
                triangles,
                bottom[0],
                bottom[3],
                bottom[2],
                bottom[1],
                ToolPalette.WoodDeep);
        }

        private static void AddHandleSegment(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            IReadOnlyList<Vector3> lower,
            IReadOnlyList<Vector3> upper,
            bool isBand)
        {
            for (int i = 0; i < HandleSides; i++)
            {
                int next = (i + 1) % HandleSides;
                LowPolyToolAssetUtility.AddQuad(
                    vertices,
                    uv,
                    triangles,
                    lower[i],
                    upper[i],
                    upper[next],
                    lower[next],
                    isBand ? ToolPalette.SteelLight : LowPolyToolAssetUtility.GetWoodFacetPalette(i));
            }
        }

        private static void AddGuard(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            const float minX = -0.070f;
            const float maxX = 0.070f;
            const float minY = -0.018f;
            const float maxY = 0.018f;
            const float minZ = 0.022f;
            const float maxZ = 0.044f;

            Vector3 leftBackBottom = new Vector3(minX, minY, minZ);
            Vector3 leftFrontBottom = new Vector3(minX, maxY, minZ);
            Vector3 rightBackBottom = new Vector3(maxX, minY, minZ);
            Vector3 rightFrontBottom = new Vector3(maxX, maxY, minZ);
            Vector3 leftBackTop = new Vector3(minX, minY, maxZ);
            Vector3 leftFrontTop = new Vector3(minX, maxY, maxZ);
            Vector3 rightBackTop = new Vector3(maxX, minY, maxZ);
            Vector3 rightFrontTop = new Vector3(maxX, maxY, maxZ);

            LowPolyToolAssetUtility.AddQuad(
                vertices, uv, triangles,
                leftFrontBottom, leftFrontTop, rightFrontTop, rightFrontBottom,
                ToolPalette.SteelMid);
            LowPolyToolAssetUtility.AddQuad(
                vertices, uv, triangles,
                leftBackBottom, rightBackBottom, rightBackTop, leftBackTop,
                ToolPalette.SteelDark);
            LowPolyToolAssetUtility.AddQuad(
                vertices, uv, triangles,
                leftBackBottom, leftBackTop, leftFrontTop, leftFrontBottom,
                ToolPalette.SteelDeep);
            LowPolyToolAssetUtility.AddQuad(
                vertices, uv, triangles,
                rightBackBottom, rightFrontBottom, rightFrontTop, rightBackTop,
                ToolPalette.SteelLight);
            LowPolyToolAssetUtility.AddQuad(
                vertices, uv, triangles,
                leftBackBottom, leftFrontBottom, rightFrontBottom, rightBackBottom,
                ToolPalette.SteelDeep);
            LowPolyToolAssetUtility.AddQuad(
                vertices, uv, triangles,
                leftBackTop, rightBackTop, rightFrontTop, leftFrontTop,
                ToolPalette.SteelLight);
        }

        private static void AddBlade(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            if (BladeOutline.Length != BladePointCount
                || BladeHalfThickness.Length != BladePointCount)
            {
                throw new InvalidOperationException("Knife_P blade profile data is incomplete.");
            }

            Vector3[] front = new Vector3[BladePointCount];
            Vector3[] back = new Vector3[BladePointCount];
            Vector3 frontCenter = Vector3.zero;
            Vector3 backCenter = Vector3.zero;
            for (int i = 0; i < BladePointCount; i++)
            {
                Vector2 point = BladeOutline[i];
                float halfThickness = BladeHalfThickness[i];
                front[i] = new Vector3(point.x, halfThickness, point.y);
                back[i] = new Vector3(point.x, -halfThickness, point.y);
                frontCenter += front[i];
                backCenter += back[i];
            }

            frontCenter /= BladePointCount;
            backCenter /= BladePointCount;
            frontCenter.y = 0.012f;
            backCenter.y = -0.012f;

            for (int i = 0; i < BladePointCount; i++)
            {
                int next = (i + 1) % BladePointCount;
                LowPolyToolAssetUtility.AddTriangle(
                    vertices,
                    uv,
                    triangles,
                    frontCenter,
                    front[i],
                    front[next],
                    GetBladeFacePalette(i, true));
                LowPolyToolAssetUtility.AddTriangle(
                    vertices,
                    uv,
                    triangles,
                    backCenter,
                    back[next],
                    back[i],
                    GetBladeFacePalette(i, false));
                LowPolyToolAssetUtility.AddQuad(
                    vertices,
                    uv,
                    triangles,
                    back[i],
                    back[next],
                    front[next],
                    front[i],
                    GetBladeEdgePalette(i));
            }
        }

        private static ToolPalette GetBladeFacePalette(int sideIndex, bool isFront)
        {
            if (!isFront)
            {
                return sideIndex == 1 || sideIndex == 2
                    ? ToolPalette.SteelMid
                    : ToolPalette.SteelDark;
            }

            switch (sideIndex)
            {
                case 0:
                case 1:
                    return ToolPalette.SteelLight;
                case 2:
                    return ToolPalette.SteelMid;
                case 3:
                    return ToolPalette.SteelDark;
                default:
                    return ToolPalette.SteelMid;
            }
        }

        private static ToolPalette GetBladeEdgePalette(int sideIndex)
        {
            if (sideIndex == 2 || sideIndex == 3)
            {
                return ToolPalette.SteelEdge;
            }

            return sideIndex == 4 ? ToolPalette.SteelDeep : ToolPalette.SteelDark;
        }

        private static void ValidateMesh(Mesh mesh)
        {
            long triangleCount = mesh.GetIndexCount(0) / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Knife_P topology changed unexpectedly. Expected {ExpectedVertexCount} vertices and "
                    + $"{ExpectedTriangleCount} triangles, but generated {mesh.vertexCount} and "
                    + $"{triangleCount}.");
            }

            Vector3 boundsCenter = mesh.bounds.center;
            if (Mathf.Abs(boundsCenter.x) > 0.0001f || Mathf.Abs(boundsCenter.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Knife_P pivot must remain centered. Generated bounds center: {boundsCenter}.");
            }
        }
    }
}
