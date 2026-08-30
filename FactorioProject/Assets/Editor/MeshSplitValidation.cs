using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools.MeshSplit
{
    internal static class MeshSplitValidation
    {
        private const float WeldTolerance = 0.0001f;

        [MenuItem("Tools/MapObject/Validate Mesh Split")]
        public static void Run()
        {
            ValidateDisconnectedIslandDetectionAcrossDuplicatedVertices();
            ValidateDifferentMeshFiltersStayDisconnected();
            ValidateWireframeEdgeWelding();
            ValidateSameColorMergeAndDifferentColorSplit();
            ValidateObjExport();
            Debug.Log("Mesh Split validation passed: island detection, MeshFilter isolation, wireframe welding, same-color merge, different-color split, and OBJ export.");
        }

        public static void RunFromCommandLine()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateDisconnectedIslandDetectionAcrossDuplicatedVertices()
        {
            MeshSplitSourceData data = CreateThreeTriangleSource(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            int[][] adjacency = MeshSplitUtility.BuildTriangleAdjacency(data, WeldTolerance);
            int[] groups = MeshSplitUtility.BuildConnectedComponentGroups(adjacency, out int componentCount);

            Assert(componentCount == 2, $"중복 버텍스 seam을 포함한 소스의 아일랜드 수가 올바르지 않습니다: {componentCount}");
            Assert(groups[0] == groups[1], "위치가 같은 중복 버텍스를 공유한 삼각형이 하나의 아일랜드로 연결되지 않았습니다.");
            Assert(groups[2] != groups[0], "떨어진 삼각형이 기존 아일랜드에 잘못 연결됐습니다.");
        }

        private static void ValidateDifferentMeshFiltersStayDisconnected()
        {
            Vector3[] vertices =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            MeshSplitSourceData data = CreateSource(
                vertices,
                new[] { 0, 1, 2, 3, 4, 5 },
                new[] { 0, 0, 0, 1, 1, 1 });
            int[][] adjacency = MeshSplitUtility.BuildTriangleAdjacency(data, WeldTolerance);
            MeshSplitUtility.BuildConnectedComponentGroups(adjacency, out int componentCount);
            Assert(componentCount == 2, "서로 다른 MeshFilter의 겹친 버텍스가 하나의 아일랜드로 잘못 연결됐습니다.");
        }

        private static void ValidateWireframeEdgeWelding()
        {
            MeshSplitSourceData data = CreateThreeTriangleSource(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            int[][] adjacency = MeshSplitUtility.BuildTriangleAdjacency(data, WeldTolerance);
            int[] groups = MeshSplitUtility.BuildConnectedComponentGroups(adjacency, out int componentCount);
            Mesh wireframe = MeshSplitUtility.BuildWireframeMesh(data, groups, componentCount, WeldTolerance);
            try
            {
                Assert(wireframe != null, "와이어프레임 Mesh 생성에 실패했습니다.");
                long indexCount = 0;
                for (int subMeshIndex = 0; subMeshIndex < wireframe.subMeshCount; subMeshIndex++)
                {
                    indexCount += (long)wireframe.GetIndexCount(subMeshIndex);
                }

                Assert(
                    indexCount == 16,
                    $"중복 seam 모서리가 제거되지 않았습니다: indexCount={indexCount}");
            }
            finally
            {
                if (wireframe != null)
                {
                    UnityEngine.Object.DestroyImmediate(wireframe);
                }
            }
        }

        private static void ValidateSameColorMergeAndDifferentColorSplit()
        {
            MeshSplitSourceData data = CreateThreeTriangleSource(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            int[][] adjacency = MeshSplitUtility.BuildTriangleAdjacency(data, WeldTolerance);
            int[] groups = MeshSplitUtility.BuildConnectedComponentGroups(adjacency, out int componentCount);
            Assert(componentCount == 2, "출력 검증용 그룹 생성에 실패했습니다.");

            List<MeshSplitOutput> outputs = null;
            try
            {
                outputs = MeshSplitUtility.BuildOutputs(
                    data,
                    groups,
                    new[] { Color.red, Color.red },
                    "MeshSplitValidation");
                Assert(outputs.Count == 1, $"같은 색 그룹이 하나의 Mesh로 합쳐지지 않았습니다: {outputs.Count}");
                Assert(outputs[0].TriangleCount == 3, "같은 색 병합 과정에서 삼각형이 누락됐습니다.");
            }
            finally
            {
                DestroyOutputs(outputs);
            }

            outputs = null;
            try
            {
                outputs = MeshSplitUtility.BuildOutputs(
                    data,
                    groups,
                    new[] { Color.red, Color.blue },
                    "MeshSplitValidation");
                Assert(outputs.Count == 2, $"다른 색 그룹이 별도 Mesh로 분리되지 않았습니다: {outputs.Count}");
                Assert(outputs[0].TriangleCount + outputs[1].TriangleCount == 3, "색 분리 과정에서 삼각형이 누락됐습니다.");
            }
            finally
            {
                DestroyOutputs(outputs);
            }
        }

        private static void ValidateObjExport()
        {
            MeshSplitSourceData data = CreateThreeTriangleSource(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            int[][] adjacency = MeshSplitUtility.BuildTriangleAdjacency(data, WeldTolerance);
            int[] groups = MeshSplitUtility.BuildConnectedComponentGroups(adjacency, out _);
            List<MeshSplitOutput> outputs = null;
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ProjectF_MeshSplitValidation_{Guid.NewGuid():N}");
            string objPath = Path.Combine(temporaryDirectory, "MeshSplitValidation.obj");
            string materialPath = MeshSplitExportUtility.GetObjMaterialPath(objPath);
            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                outputs = MeshSplitUtility.BuildOutputs(
                    data,
                    groups,
                    new[] { Color.red, Color.blue },
                    "MeshSplitValidation");
                MeshSplitExportUtility.ExportObj(objPath, outputs);
                Assert(File.Exists(objPath), "OBJ 파일이 생성되지 않았습니다.");
                Assert(File.Exists(materialPath), "OBJ Material 파일이 생성되지 않았습니다.");

                string objText = File.ReadAllText(objPath);
                Assert(objText.Contains("mtllib MeshSplitValidation.mtl"), "OBJ의 Material Library 참조가 올바르지 않습니다.");
                Assert(CountOccurrences(objText, "\no ") == 2, "색상별 Mesh가 OBJ Object로 각각 출력되지 않았습니다.");
            }
            finally
            {
                DestroyOutputs(outputs);
                MeshSplitExportUtility.TryDeleteFile(objPath);
                MeshSplitExportUtility.TryDeleteFile(materialPath);
                MeshSplitExportUtility.TryDeleteEmptyDirectory(temporaryDirectory);
            }
        }

        private static int CountOccurrences(string value, string token)
        {
            int count = 0;
            int searchIndex = 0;
            while ((searchIndex = value.IndexOf(token, searchIndex, StringComparison.Ordinal)) >= 0)
            {
                count++;
                searchIndex += token.Length;
            }

            return count;
        }

        private static MeshSplitSourceData CreateThreeTriangleSource(int[] connectivityIds)
        {
            Vector3[] vertices =
            {
                new Vector3(-2f, 0f, 0f),
                new Vector3(-1f, 0f, 0f),
                new Vector3(-1f, 1f, 0f),
                new Vector3(-2f, 0f, 0f),
                new Vector3(-1f, 1f, 0f),
                new Vector3(-2f, 1f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(3f, 1f, 0f)
            };
            return CreateSource(vertices, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, connectivityIds);
        }

        private static MeshSplitSourceData CreateSource(Vector3[] vertices, int[] triangles, int[] connectivityIds)
        {
            Vector3[] normals = new Vector3[vertices.Length];
            Vector4[] tangents = new Vector4[vertices.Length];
            Vector2[] uv0 = new Vector2[vertices.Length];
            Vector2[] uv1 = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                normals[vertexIndex] = Vector3.forward;
                tangents[vertexIndex] = new Vector4(1f, 0f, 0f, 1f);
                uv0[vertexIndex] = new Vector2(vertices[vertexIndex].x, vertices[vertexIndex].y);
                uv1[vertexIndex] = uv0[vertexIndex];
                colors[vertexIndex] = Color.white;
                bounds.Encapsulate(vertices[vertexIndex]);
            }

            int[] triangleMaterials = new int[triangles.Length / 3];
            return new MeshSplitSourceData(
                "MeshSplitValidation",
                vertices,
                normals,
                tangents,
                uv0,
                uv1,
                colors,
                connectivityIds,
                triangles,
                triangleMaterials,
                new Material[1],
                true,
                true,
                true,
                true,
                true,
                bounds);
        }

        private static void DestroyOutputs(List<MeshSplitOutput> outputs)
        {
            if (outputs == null)
            {
                return;
            }

            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                if (outputs[outputIndex].Mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(outputs[outputIndex].Mesh);
                }
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
