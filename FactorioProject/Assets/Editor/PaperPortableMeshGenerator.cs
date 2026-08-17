using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class PaperPortableMeshGenerator
    {
        internal const string PaperItemFolder = "Assets/Items/Paper";
        internal const string PaperPortableMeshPath = PaperItemFolder + "/Paper_P.mesh";

        private const string ItemDefinitionFolder = "Assets/Data/Items";
        private const string PaperSourceIconUserDataPrefix = "ProjectF.PaperSourceIconGuid=";
        private const int ColumnCount = 4;
        private const int RowCount = 5;
        private const float HalfWidth = 0.12f;
        private const float HalfDepth = 0.085f;
        private const float Thickness = 0.0025f;

        private static readonly float[] ColumnPositions = { -1f, -0.34f, 0.33f, 1f };
        private static readonly float[] RowPositions = { -1f, -0.5f, 0f, 0.5f, 1f };
        private static readonly float[] LeftEdgeOffsets = { 0.002f, -0.003f, 0.001f, -0.002f, 0.003f };
        private static readonly float[] RightEdgeOffsets = { -0.003f, 0.002f, -0.001f, 0.003f, -0.002f };
        private static readonly float[] FrontEdgeOffsets = { 0.002f, -0.002f, 0.001f, -0.003f };
        private static readonly float[] BackEdgeOffsets = { -0.002f, 0.003f, -0.001f, 0.002f };
        private static readonly float[,] SurfaceWarp =
        {
            { 0.0014f, 0.0003f, 0.0006f, 0.0012f },
            { 0.0002f, 0.0008f, -0.0002f, 0.0004f },
            { 0.0005f, -0.0003f, 0.0007f, 0.0001f },
            { 0.0008f, 0.0001f, 0.0009f, 0.0003f },
            { 0.0015f, 0.0004f, 0.0005f, 0.0013f }
        };

        [MenuItem("Tools/ProjectF/Generate Paper_P Mesh")]
        private static void GenerateFromMenu()
        {
            Mesh mesh = EnsureAssetAndBindings();
            if (mesh != null)
            {
                Selection.activeObject = mesh;
                EditorGUIUtility.PingObject(mesh);
            }
        }

        internal static Mesh EnsureAssetAndBindings()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return AssetDatabase.LoadAssetAtPath<Mesh>(PaperPortableMeshPath);
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PaperPortableMeshPath);
            if (mesh == null)
            {
                EnsurePaperItemFolder();
                mesh = BuildMesh();
                AssetDatabase.CreateAsset(mesh, PaperPortableMeshPath);
            }

            bool changed = BindPaperDefinitions(mesh);
            if (changed)
            {
                AssetDatabase.SaveAssets();
            }

            return mesh;
        }

        private static void EnsurePaperItemFolder()
        {
            if (AssetDatabase.IsValidFolder(PaperItemFolder))
            {
                return;
            }

            if (Directory.Exists(PaperItemFolder))
            {
                AssetDatabase.ImportAsset(PaperItemFolder, ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.IsValidFolder(PaperItemFolder))
                {
                    return;
                }
            }

            const string parentFolder = "Assets/Items";
            if (!AssetDatabase.IsValidFolder(parentFolder))
            {
                throw new DirectoryNotFoundException($"Paper 에셋 상위 폴더를 찾을 수 없습니다: {parentFolder}");
            }

            string folderGuid = AssetDatabase.CreateFolder(parentFolder, "Paper");
            if (string.IsNullOrWhiteSpace(folderGuid))
            {
                throw new IOException($"Paper 에셋 폴더를 생성하지 못했습니다: {PaperItemFolder}");
            }
        }

        private static Mesh BuildMesh()
        {
            List<Vector3> vertices = new List<Vector3>(96);
            List<Vector2> uvs = new List<Vector2>(96);
            List<int> triangles = new List<int>(228);
            Vector3[,] topPositions = new Vector3[RowCount, ColumnCount];
            Vector3[,] bottomPositions = new Vector3[RowCount, ColumnCount];

            for (int row = 0; row < RowCount; row++)
            {
                float rowRatio = row / (float)(RowCount - 1);
                for (int column = 0; column < ColumnCount; column++)
                {
                    float columnRatio = column / (float)(ColumnCount - 1);
                    float x = ColumnPositions[column] * HalfWidth;
                    float z = RowPositions[row] * HalfDepth;
                    if (column == 0)
                    {
                        x += LeftEdgeOffsets[row];
                    }
                    else if (column == ColumnCount - 1)
                    {
                        x += RightEdgeOffsets[row];
                    }

                    if (row == 0)
                    {
                        z += FrontEdgeOffsets[column];
                    }
                    else if (row == RowCount - 1)
                    {
                        z += BackEdgeOffsets[column];
                    }

                    float warp = SurfaceWarp[row, column];
                    Vector3 top = new Vector3(x, Thickness * 0.5f + warp, z);
                    Vector3 bottom = new Vector3(x, -Thickness * 0.5f + warp, z);
                    topPositions[row, column] = top;
                    bottomPositions[row, column] = bottom;
                    vertices.Add(top);
                    uvs.Add(new Vector2(
                        Mathf.Lerp(0.01f, 0.49f, columnRatio),
                        Mathf.Lerp(0.675f, 0.99f, rowRatio)));
                }
            }

            int bottomVertexStart = vertices.Count;
            for (int row = 0; row < RowCount; row++)
            {
                float rowRatio = row / (float)(RowCount - 1);
                for (int column = 0; column < ColumnCount; column++)
                {
                    float columnRatio = column / (float)(ColumnCount - 1);
                    vertices.Add(bottomPositions[row, column]);
                    uvs.Add(new Vector2(
                        Mathf.Lerp(0.51f, 0.99f, columnRatio),
                        Mathf.Lerp(0.99f, 0.675f, rowRatio)));
                }
            }

            AddGridFaces(triangles, 0, false);
            AddGridFaces(triangles, bottomVertexStart, true);
            AddPerimeterSides(vertices, uvs, triangles, topPositions, bottomPositions);

            Mesh mesh = new Mesh
            {
                name = "Paper_P"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddGridFaces(List<int> triangles, int vertexStart, bool reverse)
        {
            for (int row = 0; row < RowCount - 1; row++)
            {
                for (int column = 0; column < ColumnCount - 1; column++)
                {
                    int a = vertexStart + row * ColumnCount + column;
                    int b = a + 1;
                    int c = a + ColumnCount;
                    int d = c + 1;
                    if (reverse)
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(d);
                        triangles.Add(c);
                    }
                    else
                    {
                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(d);
                    }
                }
            }
        }

        private static void AddPerimeterSides(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3[,] top,
            Vector3[,] bottom)
        {
            for (int column = 0; column < ColumnCount - 1; column++)
            {
                float startRatio = column / (float)(ColumnCount - 1);
                float endRatio = (column + 1) / (float)(ColumnCount - 1);
                AddSideQuad(
                    vertices,
                    uvs,
                    triangles,
                    top[0, column],
                    top[0, column + 1],
                    bottom[0, column],
                    bottom[0, column + 1],
                    Mathf.Lerp(0.01f, 0.49f, startRatio),
                    Mathf.Lerp(0.01f, 0.49f, endRatio),
                    0.342f,
                    0.658f);
            }

            for (int row = 0; row < RowCount - 1; row++)
            {
                float startRatio = row / (float)(RowCount - 1);
                float endRatio = (row + 1) / (float)(RowCount - 1);
                AddSideQuad(
                    vertices,
                    uvs,
                    triangles,
                    top[row, ColumnCount - 1],
                    top[row + 1, ColumnCount - 1],
                    bottom[row, ColumnCount - 1],
                    bottom[row + 1, ColumnCount - 1],
                    Mathf.Lerp(0.51f, 0.99f, startRatio),
                    Mathf.Lerp(0.51f, 0.99f, endRatio),
                    0.342f,
                    0.658f);
            }

            for (int column = ColumnCount - 1; column > 0; column--)
            {
                float startRatio = (ColumnCount - 1 - column) / (float)(ColumnCount - 1);
                float endRatio = (ColumnCount - column) / (float)(ColumnCount - 1);
                AddSideQuad(
                    vertices,
                    uvs,
                    triangles,
                    top[RowCount - 1, column],
                    top[RowCount - 1, column - 1],
                    bottom[RowCount - 1, column],
                    bottom[RowCount - 1, column - 1],
                    Mathf.Lerp(0.51f, 0.99f, startRatio),
                    Mathf.Lerp(0.51f, 0.99f, endRatio),
                    0.01f,
                    0.325f);
            }

            for (int row = RowCount - 1; row > 0; row--)
            {
                float startRatio = (RowCount - 1 - row) / (float)(RowCount - 1);
                float endRatio = (RowCount - row) / (float)(RowCount - 1);
                AddSideQuad(
                    vertices,
                    uvs,
                    triangles,
                    top[row, 0],
                    top[row - 1, 0],
                    bottom[row, 0],
                    bottom[row - 1, 0],
                    Mathf.Lerp(0.01f, 0.49f, startRatio),
                    Mathf.Lerp(0.01f, 0.49f, endRatio),
                    0.01f,
                    0.325f);
            }
        }

        private static void AddSideQuad(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 topStart,
            Vector3 topEnd,
            Vector3 bottomStart,
            Vector3 bottomEnd,
            float startU,
            float endU,
            float bottomV,
            float topV)
        {
            int vertexStart = vertices.Count;
            vertices.Add(topStart);
            vertices.Add(topEnd);
            vertices.Add(bottomEnd);
            vertices.Add(bottomStart);
            uvs.Add(new Vector2(startU, topV));
            uvs.Add(new Vector2(endU, topV));
            uvs.Add(new Vector2(endU, bottomV));
            uvs.Add(new Vector2(startU, bottomV));
            triangles.Add(vertexStart);
            triangles.Add(vertexStart + 1);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 3);
        }

        private static bool BindPaperDefinitions(Mesh mesh)
        {
            if (mesh == null)
            {
                return false;
            }

            bool changed = false;
            string[] definitionGuids = AssetDatabase.FindAssets(
                "t:ItemDefinition",
                new[] { ItemDefinitionFolder });
            for (int i = 0; i < definitionGuids.Length; i++)
            {
                string definitionPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (!UsesPaperPortableMesh(definition) || definition.portableMesh == mesh)
                {
                    continue;
                }

                definition.portableMesh = mesh;
                EditorUtility.SetDirty(definition);
                changed = true;
            }

            return changed;
        }

        private static bool UsesPaperPortableMesh(ItemDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            string itemName = string.IsNullOrWhiteSpace(definition.itemName)
                ? definition.name
                : definition.itemName.Trim();
            if (string.Equals(itemName, "Paper", StringComparison.OrdinalIgnoreCase)
                || itemName.StartsWith("Paper - ", StringComparison.OrdinalIgnoreCase)
                || itemName.StartsWith("Note - ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (definition.icon == null)
            {
                return false;
            }

            string iconPath = AssetDatabase.GetAssetPath(definition.icon);
            TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            return importer != null
                   && !string.IsNullOrWhiteSpace(importer.userData)
                   && importer.userData.StartsWith(
                       PaperSourceIconUserDataPrefix,
                       StringComparison.Ordinal);
        }
    }
}
