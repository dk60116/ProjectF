using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class PickaxePortableAssetGenerator
    {
        private const string PickaxeItemName = "Pickaxe";
        private const string PickaxeFolder = "Assets/Items/Equip/Pickaxe";
        private const string PickaxeMeshPath = PickaxeFolder + "/PickAxe_P.mesh";
        private const string PickaxeMaterialPath = PickaxeFolder + "/M_Pickaxe_P.mat";
        private const string PickaxePalettePath = PickaxeFolder + "/Pickaxe_P_TB.png";
        private const string ItemDefinitionFolder = "Assets/Data/Items";

        private const int HandleSides = 4;
        private const int HeadRingPointCount = 4;
        private const int ExpectedVertexCount = 100;
        private const int ExpectedTriangleCount = 56;

        // Mirror the right-hand curved blade around the handle center.
        // Each section also remains centered on Y so the front/back thickness stays aligned.
        private static readonly HeadSection[] HeadSections =
        {
            new HeadSection(-0.125f, 0.015f, 0.010f, 0.006f),
            new HeadSection(-0.075f, 0.076f, 0.015f, 0.019f),
            new HeadSection(0f, 0.095f, 0.018f, 0.023f),
            new HeadSection(0.075f, 0.076f, 0.015f, 0.019f),
            new HeadSection(0.125f, 0.015f, 0.010f, 0.006f)
        };

        [MenuItem("Tools/ProjectF/Generate Pickaxe Portable Model")]
        public static void GeneratePickaxePortableModel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Pickaxe Portable Model: exit Play Mode before generating assets.");
                return;
            }

            Mesh mesh = LowPolyToolAssetUtility.CreateOrUpdateMeshAsset(
                PickaxeMeshPath,
                BuildLowPolyPickaxeMesh());
            Texture2D paletteTexture = LowPolyToolAssetUtility.CreateOrUpdatePaletteTexture(
                PickaxePalettePath,
                "Pickaxe_P_TB",
                "Pickaxe Portable Model");
            Material material = LowPolyToolAssetUtility.CreateOrUpdateMaterial(
                PickaxeMaterialPath,
                "M_Pickaxe_P",
                paletteTexture,
                "Pickaxe Portable Model");
            ItemDefinition definition = LowPolyToolAssetUtility.FindItemDefinition(
                ItemDefinitionFolder,
                PickaxeItemName);
            if (definition == null)
            {
                Debug.LogError(
                    $"Pickaxe Portable Model: no ItemDefinition named '{PickaxeItemName}' was found in "
                    + ItemDefinitionFolder + ".");
                return;
            }

            Undo.RecordObject(definition, "Assign Pickaxe Portable Model");
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
                $"Pickaxe Portable Model: generated {mesh.vertexCount} vertices and "
                + $"{mesh.GetIndexCount(0) / 3} triangles, then assigned it to Pickaxe.");
        }

        [MenuItem("Tools/ProjectF/Generate Pickaxe Portable Model", true)]
        private static bool CanGeneratePickaxePortableModel()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static Mesh BuildLowPolyPickaxeMesh()
        {
            ValidateHeadSymmetry();

            List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
            List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
            List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

            AddHandle(vertices, uv, triangles);
            AddHead(vertices, uv, triangles);
            LowPolyToolAssetUtility.CenterVerticesOnOrigin(vertices);

            Mesh mesh = new Mesh
            {
                name = "PickAxe_P"
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
            const float bottomZ = -0.170f;
            const float topZ = 0.105f;
            const float bottomRadiusX = 0.021f;
            const float bottomRadiusY = 0.016f;
            const float topRadiusX = 0.014f;
            const float topRadiusY = 0.011f;

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
            Vector3[][] rings = new Vector3[HeadSections.Length][];
            for (int i = 0; i < HeadSections.Length; i++)
            {
                rings[i] = BuildHeadRing(HeadSections[i]);
            }

            for (int sectionIndex = 0; sectionIndex < rings.Length - 1; sectionIndex++)
            {
                Vector3[] current = rings[sectionIndex];
                Vector3[] next = rings[sectionIndex + 1];
                for (int sideIndex = 0; sideIndex < HeadRingPointCount; sideIndex++)
                {
                    int nextSide = (sideIndex + 1) % HeadRingPointCount;
                    LowPolyToolAssetUtility.AddQuad(
                        vertices,
                        uv,
                        triangles,
                        current[sideIndex],
                        next[sideIndex],
                        next[nextSide],
                        current[nextSide],
                        GetHeadSurfacePalette(sideIndex));
                }
            }

            HeadSection leftTip = HeadSections[0];
            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                new Vector3(leftTip.X, 0f, leftTip.Z),
                rings[0],
                ToolPalette.SteelEdge,
                false);

            int lastSectionIndex = HeadSections.Length - 1;
            HeadSection rightTip = HeadSections[lastSectionIndex];
            LowPolyToolAssetUtility.AddPolygonFan(
                vertices,
                uv,
                triangles,
                new Vector3(rightTip.X, 0f, rightTip.Z),
                rings[lastSectionIndex],
                ToolPalette.SteelEdge,
                true);
        }

        private static Vector3[] BuildHeadRing(HeadSection section)
        {
            return new[]
            {
                new Vector3(section.X, 0f, section.Z + section.HalfHeight),
                new Vector3(section.X, section.HalfThickness, section.Z),
                new Vector3(section.X, 0f, section.Z - section.HalfHeight),
                new Vector3(section.X, -section.HalfThickness, section.Z)
            };
        }

        private static ToolPalette GetHeadSurfacePalette(int sideIndex)
        {
            switch (sideIndex)
            {
                case 0:
                    return ToolPalette.SteelLight;
                case 1:
                    return ToolPalette.SteelDark;
                case 2:
                    return ToolPalette.SteelMid;
                default:
                    return ToolPalette.SteelDark;
            }
        }

        private static void ValidateMesh(Mesh mesh)
        {
            long triangleCount = mesh.GetIndexCount(0) / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"PickAxe_P topology changed unexpectedly. Expected {ExpectedVertexCount} vertices "
                    + $"and {ExpectedTriangleCount} triangles, but generated {mesh.vertexCount} and "
                    + $"{triangleCount}.");
            }

            Vector3 boundsCenter = mesh.bounds.center;
            if (Mathf.Abs(boundsCenter.x) > 0.0001f || Mathf.Abs(boundsCenter.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"PickAxe_P pivot must remain centered. Generated bounds center: {boundsCenter}.");
            }
        }

        private static void ValidateHeadSymmetry()
        {
            const float tolerance = 0.0001f;
            int leftIndex = 0;
            int rightIndex = HeadSections.Length - 1;
            while (leftIndex < rightIndex)
            {
                HeadSection left = HeadSections[leftIndex];
                HeadSection right = HeadSections[rightIndex];
                if (Mathf.Abs(left.X + right.X) > tolerance
                    || Mathf.Abs(left.Z - right.Z) > tolerance
                    || Mathf.Abs(left.HalfThickness - right.HalfThickness) > tolerance
                    || Mathf.Abs(left.HalfHeight - right.HalfHeight) > tolerance)
                {
                    throw new InvalidOperationException(
                        $"PickAxe_P head sections {leftIndex} and {rightIndex} must be mirrored.");
                }

                leftIndex++;
                rightIndex--;
            }

            if (leftIndex == rightIndex && Mathf.Abs(HeadSections[leftIndex].X) > tolerance)
            {
                throw new InvalidOperationException("PickAxe_P center head section must remain on X = 0.");
            }
        }

        private readonly struct HeadSection
        {
            public readonly float X;
            public readonly float Z;
            public readonly float HalfThickness;
            public readonly float HalfHeight;

            public HeadSection(float x, float z, float halfThickness, float halfHeight)
            {
                X = x;
                Z = z;
                HalfThickness = halfThickness;
                HalfHeight = halfHeight;
            }
        }
    }
}
