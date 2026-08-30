using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ProjectF.EditorTools.MeshSplit
{
    internal sealed class MeshSplitSourceData
    {
        public MeshSplitSourceData(
            string sourceName,
            Vector3[] vertices,
            Vector3[] normals,
            Vector4[] tangents,
            Vector2[] uv0,
            Vector2[] uv1,
            Color[] colors,
            int[] vertexConnectivityIds,
            int[] triangles,
            int[] triangleMaterialIndices,
            Material[] materials,
            bool hasNormals,
            bool hasTangents,
            bool hasUv0,
            bool hasUv1,
            bool hasColors,
            Bounds bounds)
        {
            SourceName = sourceName;
            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
            Uv0 = uv0;
            Uv1 = uv1;
            Colors = colors;
            VertexConnectivityIds = vertexConnectivityIds;
            Triangles = triangles;
            TriangleMaterialIndices = triangleMaterialIndices;
            Materials = materials;
            HasNormals = hasNormals;
            HasTangents = hasTangents;
            HasUv0 = hasUv0;
            HasUv1 = hasUv1;
            HasColors = hasColors;
            Bounds = bounds;
        }

        public string SourceName { get; }
        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public Vector4[] Tangents { get; }
        public Vector2[] Uv0 { get; }
        public Vector2[] Uv1 { get; }
        public Color[] Colors { get; }
        public int[] VertexConnectivityIds { get; }
        public int[] Triangles { get; }
        public int[] TriangleMaterialIndices { get; }
        public Material[] Materials { get; }
        public bool HasNormals { get; }
        public bool HasTangents { get; }
        public bool HasUv0 { get; }
        public bool HasUv1 { get; }
        public bool HasColors { get; }
        public Bounds Bounds { get; }
        public int TriangleCount => Triangles != null ? Triangles.Length / 3 : 0;
    }

    internal sealed class MeshSplitOutput
    {
        public MeshSplitOutput(Color32 groupColor, Mesh mesh, Material[] materials, int triangleCount)
        {
            GroupColor = groupColor;
            Mesh = mesh;
            Materials = materials;
            TriangleCount = triangleCount;
        }

        public Color32 GroupColor { get; }
        public Mesh Mesh { get; }
        public Material[] Materials { get; }
        public int TriangleCount { get; }
    }

    internal static class MeshSplitUtility
    {
        private readonly struct SourceEntry
        {
            public SourceEntry(Mesh mesh, Matrix4x4 meshToRoot, Material[] materials, int connectivityId)
            {
                Mesh = mesh;
                MeshToRoot = meshToRoot;
                Materials = materials;
                ConnectivityId = connectivityId;
            }

            public Mesh Mesh { get; }
            public Matrix4x4 MeshToRoot { get; }
            public Material[] Materials { get; }
            public int ConnectivityId { get; }
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public EdgeKey(WeldKey first, WeldKey second)
            {
                if (first.CompareTo(second) <= 0)
                {
                    First = first;
                    Second = second;
                }
                else
                {
                    First = second;
                    Second = first;
                }
            }

            private WeldKey First { get; }
            private WeldKey Second { get; }

            public bool Equals(EdgeKey other)
            {
                return First.Equals(other.First) && Second.Equals(other.Second);
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (First.GetHashCode() * 397) ^ Second.GetHashCode();
                }
            }
        }

        private readonly struct WeldKey : IEquatable<WeldKey>, IComparable<WeldKey>
        {
            public WeldKey(int connectivityId, Vector3 position, float tolerance)
            {
                double inverseTolerance = 1d / Math.Max(tolerance, 0.0000001f);
                ConnectivityId = connectivityId;
                X = (long)Math.Round(position.x * inverseTolerance, MidpointRounding.AwayFromZero);
                Y = (long)Math.Round(position.y * inverseTolerance, MidpointRounding.AwayFromZero);
                Z = (long)Math.Round(position.z * inverseTolerance, MidpointRounding.AwayFromZero);
            }

            private int ConnectivityId { get; }
            private long X { get; }
            private long Y { get; }
            private long Z { get; }

            public bool Equals(WeldKey other)
            {
                return ConnectivityId == other.ConnectivityId && X == other.X && Y == other.Y && Z == other.Z;
            }

            public int CompareTo(WeldKey other)
            {
                int result = ConnectivityId.CompareTo(other.ConnectivityId);
                if (result != 0)
                {
                    return result;
                }

                result = X.CompareTo(other.X);
                if (result != 0)
                {
                    return result;
                }

                result = Y.CompareTo(other.Y);
                return result != 0 ? result : Z.CompareTo(other.Z);
            }

            public override bool Equals(object obj)
            {
                return obj is WeldKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ConnectivityId;
                    hash = (hash * 397) ^ X.GetHashCode();
                    hash = (hash * 397) ^ Y.GetHashCode();
                    hash = (hash * 397) ^ Z.GetHashCode();
                    return hash;
                }
            }
        }

        public static bool TryBuildSourceData(Object source, out MeshSplitSourceData data, out string error)
        {
            data = null;
            error = string.Empty;
            List<SourceEntry> entries = new List<SourceEntry>();
            if (!TryCollectSourceEntries(source, entries, out string sourceName, out error))
            {
                return false;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uv0 = new List<Vector2>();
            List<Vector2> uv1 = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> vertexConnectivityIds = new List<int>();
            List<int> triangles = new List<int>();
            List<int> triangleMaterialIndices = new List<int>();
            List<Material> materials = new List<Material>();
            Dictionary<Material, int> materialIndices = new Dictionary<Material, int>();
            int nullMaterialIndex = -1;
            bool allNormalsValid = true;
            bool allTangentsValid = true;
            bool allUv0Valid = true;
            bool allUv1Valid = true;
            bool allColorsValid = true;
            bool hasBounds = false;
            Bounds bounds = default;

            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                SourceEntry entry = entries[entryIndex];
                Mesh readableMesh = global::MeshSymmetryUtility.CreateEditableCopy(
                    entry.Mesh,
                    $"{entry.Mesh.name}_MeshSplitSource",
                    HideFlags.HideAndDontSave,
                    out string copyError);
                if (readableMesh == null)
                {
                    error = copyError;
                    return false;
                }

                try
                {
                    if (!AppendSourceEntry(
                            readableMesh,
                            entry,
                            vertices,
                            normals,
                            tangents,
                            uv0,
                            uv1,
                            colors,
                            vertexConnectivityIds,
                            triangles,
                            triangleMaterialIndices,
                            materials,
                            materialIndices,
                            ref nullMaterialIndex,
                            ref allNormalsValid,
                            ref allTangentsValid,
                            ref allUv0Valid,
                            ref allUv1Valid,
                            ref allColorsValid,
                            ref bounds,
                            ref hasBounds,
                            out error))
                    {
                        return false;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(readableMesh);
                }
            }

            if (vertices.Count == 0 || triangles.Count == 0)
            {
                error = "분리할 수 있는 버텍스와 삼각형이 없습니다.";
                return false;
            }

            data = new MeshSplitSourceData(
                sourceName,
                vertices.ToArray(),
                normals.ToArray(),
                tangents.ToArray(),
                uv0.ToArray(),
                uv1.ToArray(),
                colors.ToArray(),
                vertexConnectivityIds.ToArray(),
                triangles.ToArray(),
                triangleMaterialIndices.ToArray(),
                materials.ToArray(),
                allNormalsValid,
                allTangentsValid,
                allUv0Valid,
                allUv1Valid,
                allColorsValid,
                bounds);
            return true;
        }

        public static int[][] BuildTriangleAdjacency(MeshSplitSourceData data, float weldTolerance)
        {
            int triangleCount = data != null ? data.TriangleCount : 0;
            if (triangleCount == 0)
            {
                return Array.Empty<int[]>();
            }

            float tolerance = Mathf.Max(0.0000001f, weldTolerance);
            Dictionary<WeldKey, List<int>> trianglesByVertex = new Dictionary<WeldKey, List<int>>();
            int[] triangles = data.Triangles;
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int baseIndex = triangleIndex * 3;
                WeldKey key0 = CreateWeldKey(data, triangles[baseIndex], tolerance);
                WeldKey key1 = CreateWeldKey(data, triangles[baseIndex + 1], tolerance);
                WeldKey key2 = CreateWeldKey(data, triangles[baseIndex + 2], tolerance);
                AddTriangleToVertexBucket(trianglesByVertex, key0, triangleIndex);
                if (!key1.Equals(key0))
                {
                    AddTriangleToVertexBucket(trianglesByVertex, key1, triangleIndex);
                }

                if (!key2.Equals(key0) && !key2.Equals(key1))
                {
                    AddTriangleToVertexBucket(trianglesByVertex, key2, triangleIndex);
                }
            }

            HashSet<int>[] neighborSets = new HashSet<int>[triangleCount];
            foreach (List<int> bucket in trianglesByVertex.Values)
            {
                for (int firstIndex = 0; firstIndex < bucket.Count; firstIndex++)
                {
                    int firstTriangle = bucket[firstIndex];
                    for (int secondIndex = firstIndex + 1; secondIndex < bucket.Count; secondIndex++)
                    {
                        int secondTriangle = bucket[secondIndex];
                        AddNeighbor(neighborSets, firstTriangle, secondTriangle);
                        AddNeighbor(neighborSets, secondTriangle, firstTriangle);
                    }
                }
            }

            int[][] adjacency = new int[triangleCount][];
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                adjacency[triangleIndex] = neighborSets[triangleIndex] != null
                    ? CopyAndSort(neighborSets[triangleIndex])
                    : Array.Empty<int>();
            }

            return adjacency;
        }

        public static int[] BuildConnectedComponentGroups(int[][] adjacency, out int componentCount)
        {
            int triangleCount = adjacency != null ? adjacency.Length : 0;
            int[] groups = new int[triangleCount];
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                groups[triangleIndex] = -1;
            }

            componentCount = 0;
            Queue<int> pending = new Queue<int>();

            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                if (groups[triangleIndex] >= 0)
                {
                    continue;
                }

                int groupIndex = componentCount++;
                groups[triangleIndex] = groupIndex;
                pending.Enqueue(triangleIndex);
                while (pending.Count > 0)
                {
                    int current = pending.Dequeue();
                    int[] neighbors = adjacency[current];
                    for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
                    {
                        int neighbor = neighbors[neighborIndex];
                        if (groups[neighbor] >= 0)
                        {
                            continue;
                        }

                        groups[neighbor] = groupIndex;
                        pending.Enqueue(neighbor);
                    }
                }
            }

            return groups;
        }

        public static Mesh BuildPreviewMesh(MeshSplitSourceData data, int[] triangleGroups, int groupCount)
        {
            if (data == null || triangleGroups == null || triangleGroups.Length != data.TriangleCount)
            {
                return null;
            }

            int safeGroupCount = Mathf.Max(1, groupCount);
            List<int>[] trianglesByGroup = new List<int>[safeGroupCount];
            for (int groupIndex = 0; groupIndex < safeGroupCount; groupIndex++)
            {
                trianglesByGroup[groupIndex] = new List<int>();
            }

            for (int triangleIndex = 0; triangleIndex < data.TriangleCount; triangleIndex++)
            {
                int groupIndex = Mathf.Clamp(triangleGroups[triangleIndex], 0, safeGroupCount - 1);
                int baseIndex = triangleIndex * 3;
                trianglesByGroup[groupIndex].Add(data.Triangles[baseIndex]);
                trianglesByGroup[groupIndex].Add(data.Triangles[baseIndex + 1]);
                trianglesByGroup[groupIndex].Add(data.Triangles[baseIndex + 2]);
            }

            Mesh mesh = CreateMeshWithSourceChannels(data, data.Vertices, data.Normals, data.Tangents, data.Uv0, data.Uv1, data.Colors);
            mesh.name = $"{data.SourceName}_MeshSplitPreview";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.subMeshCount = safeGroupCount;
            for (int groupIndex = 0; groupIndex < safeGroupCount; groupIndex++)
            {
                mesh.SetTriangles(trianglesByGroup[groupIndex], groupIndex, false);
            }

            FinalizeMeshChannels(mesh, data.HasNormals, data.HasTangents, data.HasUv0);
            return mesh;
        }

        public static Mesh BuildWireframeMesh(
            MeshSplitSourceData data,
            int[] triangleGroups,
            int groupCount,
            float weldTolerance)
        {
            if (data == null
                || data.TriangleCount == 0
                || triangleGroups == null
                || triangleGroups.Length != data.TriangleCount)
            {
                return null;
            }

            int safeGroupCount = Mathf.Max(1, groupCount);
            HashSet<EdgeKey>[] uniqueEdgesByGroup = new HashSet<EdgeKey>[safeGroupCount];
            List<int>[] lineIndicesByGroup = new List<int>[safeGroupCount];
            for (int groupIndex = 0; groupIndex < safeGroupCount; groupIndex++)
            {
                uniqueEdgesByGroup[groupIndex] = new HashSet<EdgeKey>();
                lineIndicesByGroup[groupIndex] = new List<int>();
            }

            float tolerance = Mathf.Max(0.0000001f, weldTolerance);
            for (int triangleIndex = 0; triangleIndex < data.TriangleCount; triangleIndex++)
            {
                int baseIndex = triangleIndex * 3;
                int first = data.Triangles[baseIndex];
                int second = data.Triangles[baseIndex + 1];
                int third = data.Triangles[baseIndex + 2];
                int groupIndex = Mathf.Clamp(triangleGroups[triangleIndex], 0, safeGroupCount - 1);
                HashSet<EdgeKey> uniqueEdges = uniqueEdgesByGroup[groupIndex];
                List<int> lineIndices = lineIndicesByGroup[groupIndex];
                AddWireframeEdge(data, first, second, tolerance, uniqueEdges, lineIndices);
                AddWireframeEdge(data, second, third, tolerance, uniqueEdges, lineIndices);
                AddWireframeEdge(data, third, first, tolerance, uniqueEdges, lineIndices);
            }

            Mesh mesh = new Mesh
            {
                name = $"{data.SourceName}_MeshSplitWireframe",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = data.Vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = data.Vertices
            };
            mesh.subMeshCount = safeGroupCount;
            for (int groupIndex = 0; groupIndex < safeGroupCount; groupIndex++)
            {
                mesh.SetIndices(lineIndicesByGroup[groupIndex].ToArray(), MeshTopology.Lines, groupIndex, false);
            }

            mesh.bounds = data.Bounds;
            return mesh;
        }

        public static List<MeshSplitOutput> BuildOutputs(
            MeshSplitSourceData data,
            int[] triangleGroups,
            IReadOnlyList<Color> groupColors,
            string outputName)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (triangleGroups == null || triangleGroups.Length != data.TriangleCount)
            {
                throw new InvalidOperationException("삼각형 그룹 데이터가 원본 메쉬와 일치하지 않습니다.");
            }

            if (groupColors == null || groupColors.Count == 0)
            {
                throw new InvalidOperationException("저장할 색 그룹이 없습니다.");
            }

            Dictionary<uint, List<int>> trianglesByColor = new Dictionary<uint, List<int>>();
            Dictionary<uint, Color32> colorsByKey = new Dictionary<uint, Color32>();
            List<uint> colorOrder = new List<uint>();
            for (int triangleIndex = 0; triangleIndex < data.TriangleCount; triangleIndex++)
            {
                int groupIndex = Mathf.Clamp(triangleGroups[triangleIndex], 0, groupColors.Count - 1);
                Color32 color = groupColors[groupIndex];
                color.a = 255;
                uint key = PackColor(color);
                if (!trianglesByColor.TryGetValue(key, out List<int> colorTriangles))
                {
                    colorTriangles = new List<int>();
                    trianglesByColor.Add(key, colorTriangles);
                    colorsByKey.Add(key, color);
                    colorOrder.Add(key);
                }

                colorTriangles.Add(triangleIndex);
            }

            string safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(outputName) ? data.SourceName : outputName);
            List<MeshSplitOutput> outputs = new List<MeshSplitOutput>(colorOrder.Count);
            try
            {
                for (int colorIndex = 0; colorIndex < colorOrder.Count; colorIndex++)
                {
                    uint key = colorOrder[colorIndex];
                    Color32 color = colorsByKey[key];
                    Mesh mesh = BuildCompactGroupMesh(
                        data,
                        trianglesByColor[key],
                        $"{safeName}_Group_{colorIndex + 1:00}_{color.r:X2}{color.g:X2}{color.b:X2}",
                        out Material[] materials);
                    outputs.Add(new MeshSplitOutput(color, mesh, materials, trianglesByColor[key].Count));
                }
            }
            catch
            {
                for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
                {
                    if (outputs[outputIndex].Mesh != null)
                    {
                        Object.DestroyImmediate(outputs[outputIndex].Mesh);
                    }
                }

                throw;
            }

            return outputs;
        }

        public static string MakeSafeFileName(string value)
        {
            string safeName = string.IsNullOrWhiteSpace(value) ? "MeshSplit" : value.Trim();
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(safeName) ? "MeshSplit" : safeName;
        }

        private static bool TryCollectSourceEntries(
            Object source,
            List<SourceEntry> entries,
            out string sourceName,
            out string error)
        {
            sourceName = source != null ? source.name : "MeshSplit";
            error = string.Empty;
            if (source == null)
            {
                error = "FBX, OBJ, Prefab 또는 Mesh를 선택해주세요.";
                return false;
            }

            if (source is GameObject gameObject)
            {
                CollectGameObjectEntries(gameObject, null, entries);
            }
            else if (source is Mesh mesh)
            {
                string assetPath = AssetDatabase.GetAssetPath(mesh);
                GameObject modelRoot = LoadModelRoot(assetPath);
                if (modelRoot != null)
                {
                    sourceName = modelRoot.name;
                    CollectGameObjectEntries(modelRoot, mesh, entries);
                }

                if (entries.Count == 0)
                {
                    entries.Add(new SourceEntry(mesh, Matrix4x4.identity, Array.Empty<Material>(), 0));
                }
            }
            else
            {
                error = "FBX, OBJ, Prefab 또는 Mesh Asset만 지원합니다.";
                return false;
            }

            if (entries.Count == 0)
            {
                error = "선택한 소스에서 정적 Mesh를 찾지 못했습니다.";
                return false;
            }

            return true;
        }

        private static GameObject LoadModelRoot(string assetPath)
        {
            string extension = Path.GetExtension(assetPath)?.ToLowerInvariant() ?? string.Empty;
            return extension == ".fbx" || extension == ".obj"
                ? AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)
                : null;
        }

        private static void CollectGameObjectEntries(GameObject rootObject, Mesh targetMesh, List<SourceEntry> entries)
        {
            Transform root = rootObject.transform;
            int connectivityId = 0;
            MeshFilter[] meshFilters = rootObject.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter filter = meshFilters[i];
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || (targetMesh != null && !IsSameMeshAsset(mesh, targetMesh)))
                {
                    continue;
                }

                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                entries.Add(new SourceEntry(
                    mesh,
                    root.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                    renderer != null ? renderer.sharedMaterials : Array.Empty<Material>(),
                    connectivityId++));
            }

            SkinnedMeshRenderer[] skinnedRenderers = rootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = skinnedRenderers[i];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null || (targetMesh != null && !IsSameMeshAsset(mesh, targetMesh)))
                {
                    continue;
                }

                entries.Add(new SourceEntry(
                    mesh,
                    root.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                    renderer.sharedMaterials,
                    connectivityId++));
            }
        }

        private static bool IsSameMeshAsset(Mesh first, Mesh second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            if (first == second)
            {
                return true;
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(first, out string firstGuid, out long firstLocalId)
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(second, out string secondGuid, out long secondLocalId))
            {
                return firstGuid == secondGuid && firstLocalId == secondLocalId;
            }

            return false;
        }

        private static bool AppendSourceEntry(
            Mesh mesh,
            SourceEntry entry,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv0,
            List<Vector2> uv1,
            List<Color> colors,
            List<int> vertexConnectivityIds,
            List<int> triangles,
            List<int> triangleMaterialIndices,
            List<Material> materials,
            Dictionary<Material, int> materialIndices,
            ref int nullMaterialIndex,
            ref bool allNormalsValid,
            ref bool allTangentsValid,
            ref bool allUv0Valid,
            ref bool allUv1Valid,
            ref bool allColorsValid,
            ref Bounds bounds,
            ref bool hasBounds,
            out string error)
        {
            error = string.Empty;
            Vector3[] sourceVertices = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector4[] sourceTangents = mesh.tangents;
            Vector2[] sourceUv0 = mesh.uv;
            Vector2[] sourceUv1 = mesh.uv2;
            Color[] sourceColors = mesh.colors;
            int vertexCount = sourceVertices.Length;
            int vertexOffset = vertices.Count;
            bool hasNormals = sourceNormals != null && sourceNormals.Length == vertexCount;
            bool hasTangents = sourceTangents != null && sourceTangents.Length == vertexCount;
            bool hasUv0 = sourceUv0 != null && sourceUv0.Length == vertexCount;
            bool hasUv1 = sourceUv1 != null && sourceUv1.Length == vertexCount;
            bool hasColors = sourceColors != null && sourceColors.Length == vertexCount;
            allNormalsValid &= hasNormals;
            allTangentsValid &= hasTangents;
            allUv0Valid &= hasUv0;
            allUv1Valid &= hasUv1;
            allColorsValid &= hasColors;

            Matrix4x4 normalMatrix = entry.MeshToRoot.inverse.transpose;
            bool mirrored = entry.MeshToRoot.determinant < 0f;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                Vector3 vertex = entry.MeshToRoot.MultiplyPoint3x4(sourceVertices[vertexIndex]);
                vertices.Add(vertex);
                normals.Add(hasNormals ? normalMatrix.MultiplyVector(sourceNormals[vertexIndex]).normalized : Vector3.up);
                if (hasTangents)
                {
                    Vector4 tangent = sourceTangents[vertexIndex];
                    Vector3 tangentDirection = entry.MeshToRoot.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                    tangents.Add(new Vector4(
                        tangentDirection.x,
                        tangentDirection.y,
                        tangentDirection.z,
                        mirrored ? -tangent.w : tangent.w));
                }
                else
                {
                    tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                }

                uv0.Add(hasUv0 ? sourceUv0[vertexIndex] : Vector2.zero);
                uv1.Add(hasUv1 ? sourceUv1[vertexIndex] : Vector2.zero);
                colors.Add(hasColors ? sourceColors[vertexIndex] : Color.white);
                vertexConnectivityIds.Add(entry.ConnectivityId);
                if (!hasBounds)
                {
                    bounds = new Bounds(vertex, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(vertex);
                }
            }

            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                {
                    error = $"'{mesh.name}'의 SubMesh {subMeshIndex}가 Triangle 형식이 아닙니다.";
                    return false;
                }

                int materialIndex = ResolveMaterialIndex(
                    entry.Materials != null && entry.Materials.Length > 0
                        ? entry.Materials[Mathf.Min(subMeshIndex, entry.Materials.Length - 1)]
                        : null,
                    materials,
                    materialIndices,
                    ref nullMaterialIndex);
                int[] sourceTriangles = mesh.GetTriangles(subMeshIndex);
                for (int triangleIndex = 0; triangleIndex + 2 < sourceTriangles.Length; triangleIndex += 3)
                {
                    int a = sourceTriangles[triangleIndex] + vertexOffset;
                    int b = sourceTriangles[triangleIndex + 1] + vertexOffset;
                    int c = sourceTriangles[triangleIndex + 2] + vertexOffset;
                    if (mirrored)
                    {
                        triangles.Add(b);
                        triangles.Add(a);
                    }
                    else
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                    }

                    triangles.Add(c);
                    triangleMaterialIndices.Add(materialIndex);
                }
            }

            return true;
        }

        private static int ResolveMaterialIndex(
            Material material,
            List<Material> materials,
            Dictionary<Material, int> materialIndices,
            ref int nullMaterialIndex)
        {
            if (material == null)
            {
                if (nullMaterialIndex < 0)
                {
                    nullMaterialIndex = materials.Count;
                    materials.Add(null);
                }

                return nullMaterialIndex;
            }

            if (materialIndices.TryGetValue(material, out int index))
            {
                return index;
            }

            index = materials.Count;
            materials.Add(material);
            materialIndices.Add(material, index);
            return index;
        }

        private static WeldKey CreateWeldKey(MeshSplitSourceData data, int vertexIndex, float tolerance)
        {
            return new WeldKey(
                data.VertexConnectivityIds[vertexIndex],
                data.Vertices[vertexIndex],
                tolerance);
        }

        private static void AddTriangleToVertexBucket(
            Dictionary<WeldKey, List<int>> trianglesByVertex,
            WeldKey key,
            int triangleIndex)
        {
            if (!trianglesByVertex.TryGetValue(key, out List<int> bucket))
            {
                bucket = new List<int>();
                trianglesByVertex.Add(key, bucket);
            }

            bucket.Add(triangleIndex);
        }

        private static void AddNeighbor(HashSet<int>[] neighborSets, int triangleIndex, int neighbor)
        {
            HashSet<int> neighbors = neighborSets[triangleIndex];
            if (neighbors == null)
            {
                neighbors = new HashSet<int>();
                neighborSets[triangleIndex] = neighbors;
            }

            neighbors.Add(neighbor);
        }

        private static void AddWireframeEdge(
            MeshSplitSourceData data,
            int first,
            int second,
            float tolerance,
            HashSet<EdgeKey> uniqueEdges,
            List<int> lineIndices)
        {
            if (first == second)
            {
                return;
            }

            EdgeKey edge = new EdgeKey(
                CreateWeldKey(data, first, tolerance),
                CreateWeldKey(data, second, tolerance));
            if (!uniqueEdges.Add(edge))
            {
                return;
            }

            lineIndices.Add(first);
            lineIndices.Add(second);
        }

        private static int[] CopyAndSort(HashSet<int> values)
        {
            int[] result = new int[values.Count];
            values.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        private static Mesh BuildCompactGroupMesh(
            MeshSplitSourceData data,
            List<int> sourceTriangleIndices,
            string meshName,
            out Material[] outputMaterials)
        {
            Dictionary<int, int> vertexMap = new Dictionary<int, int>();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uv0 = new List<Vector2>();
            List<Vector2> uv1 = new List<Vector2>();
            List<Color> colors = new List<Color>();
            Dictionary<int, int> outputSubMeshByMaterial = new Dictionary<int, int>();
            List<Material> materials = new List<Material>();
            List<List<int>> trianglesByMaterial = new List<List<int>>();

            for (int listIndex = 0; listIndex < sourceTriangleIndices.Count; listIndex++)
            {
                int sourceTriangleIndex = sourceTriangleIndices[listIndex];
                int sourceMaterialIndex = data.TriangleMaterialIndices[sourceTriangleIndex];
                if (!outputSubMeshByMaterial.TryGetValue(sourceMaterialIndex, out int outputSubMeshIndex))
                {
                    outputSubMeshIndex = trianglesByMaterial.Count;
                    outputSubMeshByMaterial.Add(sourceMaterialIndex, outputSubMeshIndex);
                    trianglesByMaterial.Add(new List<int>());
                    materials.Add(sourceMaterialIndex >= 0 && sourceMaterialIndex < data.Materials.Length
                        ? data.Materials[sourceMaterialIndex]
                        : null);
                }

                int baseIndex = sourceTriangleIndex * 3;
                List<int> outputTriangles = trianglesByMaterial[outputSubMeshIndex];
                outputTriangles.Add(GetOrCreateOutputVertex(data.Triangles[baseIndex], data, vertexMap, vertices, normals, tangents, uv0, uv1, colors));
                outputTriangles.Add(GetOrCreateOutputVertex(data.Triangles[baseIndex + 1], data, vertexMap, vertices, normals, tangents, uv0, uv1, colors));
                outputTriangles.Add(GetOrCreateOutputVertex(data.Triangles[baseIndex + 2], data, vertexMap, vertices, normals, tangents, uv0, uv1, colors));
            }

            Mesh mesh = CreateMeshWithSourceChannels(
                data,
                vertices.ToArray(),
                normals.ToArray(),
                tangents.ToArray(),
                uv0.ToArray(),
                uv1.ToArray(),
                colors.ToArray());
            mesh.name = meshName;
            mesh.subMeshCount = Mathf.Max(1, trianglesByMaterial.Count);
            for (int subMeshIndex = 0; subMeshIndex < trianglesByMaterial.Count; subMeshIndex++)
            {
                mesh.SetTriangles(trianglesByMaterial[subMeshIndex], subMeshIndex, false);
            }

            FinalizeMeshChannels(mesh, data.HasNormals, data.HasTangents, data.HasUv0);
            outputMaterials = materials.Count > 0 ? materials.ToArray() : new Material[1];
            return mesh;
        }

        private static int GetOrCreateOutputVertex(
            int sourceVertexIndex,
            MeshSplitSourceData data,
            Dictionary<int, int> vertexMap,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv0,
            List<Vector2> uv1,
            List<Color> colors)
        {
            if (vertexMap.TryGetValue(sourceVertexIndex, out int outputVertexIndex))
            {
                return outputVertexIndex;
            }

            outputVertexIndex = vertices.Count;
            vertexMap.Add(sourceVertexIndex, outputVertexIndex);
            vertices.Add(data.Vertices[sourceVertexIndex]);
            normals.Add(data.Normals[sourceVertexIndex]);
            tangents.Add(data.Tangents[sourceVertexIndex]);
            uv0.Add(data.Uv0[sourceVertexIndex]);
            uv1.Add(data.Uv1[sourceVertexIndex]);
            colors.Add(data.Colors[sourceVertexIndex]);
            return outputVertexIndex;
        }

        private static Mesh CreateMeshWithSourceChannels(
            MeshSplitSourceData data,
            Vector3[] vertices,
            Vector3[] normals,
            Vector4[] tangents,
            Vector2[] uv0,
            Vector2[] uv1,
            Color[] colors)
        {
            Mesh mesh = new Mesh
            {
                indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = vertices
            };
            if (data.HasNormals)
            {
                mesh.normals = normals;
            }

            if (data.HasTangents)
            {
                mesh.tangents = tangents;
            }

            if (data.HasUv0)
            {
                mesh.uv = uv0;
            }

            if (data.HasUv1)
            {
                mesh.uv2 = uv1;
            }

            if (data.HasColors)
            {
                mesh.colors = colors;
            }

            return mesh;
        }

        private static void FinalizeMeshChannels(Mesh mesh, bool hasNormals, bool hasTangents, bool hasUv0)
        {
            if (!hasNormals)
            {
                mesh.RecalculateNormals();
            }

            if (!hasTangents && hasUv0)
            {
                mesh.RecalculateTangents();
            }

            mesh.RecalculateBounds();
        }

        private static uint PackColor(Color32 color)
        {
            return ((uint)color.r << 24) | ((uint)color.g << 16) | ((uint)color.b << 8) | color.a;
        }
    }
}
