using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal enum MeshSymmetrySide
{
    Negative,
    Positive
}

internal readonly struct MeshSymmetrySettings
{
    public MeshSymmetrySettings(
        bool mirrorX,
        MeshSymmetrySide xSourceSide,
        bool mirrorY,
        MeshSymmetrySide ySourceSide,
        bool mirrorZ,
        MeshSymmetrySide zSourceSide,
        float planeTolerance)
    {
        MirrorX = mirrorX;
        XSourceSide = xSourceSide;
        MirrorY = mirrorY;
        YSourceSide = ySourceSide;
        MirrorZ = mirrorZ;
        ZSourceSide = zSourceSide;
        PlaneTolerance = Mathf.Max(0.000001f, planeTolerance);
    }

    public bool MirrorX { get; }
    public MeshSymmetrySide XSourceSide { get; }
    public bool MirrorY { get; }
    public MeshSymmetrySide YSourceSide { get; }
    public bool MirrorZ { get; }
    public MeshSymmetrySide ZSourceSide { get; }
    public float PlaneTolerance { get; }
    public bool Enabled => MirrorX || MirrorY || MirrorZ;
}

internal static class MeshSymmetryUtility
{
    private const int MirrorXMask = 1;
    private const int MirrorYMask = 2;
    private const int MirrorZMask = 4;
    private const int UvChannelCount = 8;

    public static Mesh CreateEditableCopy(
        Mesh source,
        string copyName,
        HideFlags hideFlags,
        out string error)
    {
        error = string.Empty;
        if (source == null)
        {
            error = "복사할 Mesh가 없습니다.";
            return null;
        }

        if (source.isReadable)
        {
            Mesh readableClone = UnityEngine.Object.Instantiate(source);
            readableClone.name = copyName;
            readableClone.hideFlags = hideFlags;
            return readableClone;
        }

        if (source.blendShapeCount > 0
            || source.HasVertexAttribute(VertexAttribute.BlendIndices)
            || source.HasVertexAttribute(VertexAttribute.BlendWeight))
        {
            error = $"'{source.name}'은 읽기 비활성 상태의 스킨/BlendShape Mesh입니다. "
                    + "Model Import Settings에서 Read/Write를 켠 뒤 다시 시도하세요.";
            return null;
        }

        Mesh editableCopy = new Mesh
        {
            name = copyName,
            hideFlags = hideFlags
        };

        Mesh.MeshDataArray readOnlyData = default;
        bool hasReadOnlyData = false;
        try
        {
            readOnlyData = MeshUtility.AcquireReadOnlyMeshData(source);
            hasReadOnlyData = true;
            Mesh.MeshData meshData = readOnlyData[0];
            editableCopy.indexFormat = meshData.indexFormat;

            CopyVertices(meshData, editableCopy);
            CopyVertexChannels(meshData, editableCopy);
            CopySubMeshes(meshData, editableCopy);
            editableCopy.bounds = source.bounds;
            return editableCopy;
        }
        catch (Exception exception)
        {
            UnityEngine.Object.DestroyImmediate(editableCopy);
            error = $"'{source.name}' Mesh 데이터를 읽을 수 없습니다: {exception.Message}";
            return null;
        }
        finally
        {
            if (hasReadOnlyData)
            {
                readOnlyData.Dispose();
            }
        }
    }

    private static void CopyVertices(Mesh.MeshData source, Mesh destination)
    {
        using (NativeArray<Vector3> vertices = new NativeArray<Vector3>(
                   source.vertexCount,
                   Allocator.Temp,
                   NativeArrayOptions.UninitializedMemory))
        {
            source.GetVertices(vertices);
            destination.SetVertices(vertices);
        }
    }

    private static void CopyVertexChannels(Mesh.MeshData source, Mesh destination)
    {
        int vertexCount = source.vertexCount;
        if (source.HasVertexAttribute(VertexAttribute.Normal))
        {
            using (NativeArray<Vector3> normals = new NativeArray<Vector3>(
                       vertexCount,
                       Allocator.Temp,
                       NativeArrayOptions.UninitializedMemory))
            {
                source.GetNormals(normals);
                destination.SetNormals(normals);
            }
        }

        if (source.HasVertexAttribute(VertexAttribute.Tangent))
        {
            using (NativeArray<Vector4> tangents = new NativeArray<Vector4>(
                       vertexCount,
                       Allocator.Temp,
                       NativeArrayOptions.UninitializedMemory))
            {
                source.GetTangents(tangents);
                destination.SetTangents(tangents);
            }
        }

        if (source.HasVertexAttribute(VertexAttribute.Color))
        {
            using (NativeArray<Color> colors = new NativeArray<Color>(
                       vertexCount,
                       Allocator.Temp,
                       NativeArrayOptions.UninitializedMemory))
            {
                source.GetColors(colors);
                destination.SetColors(colors);
            }
        }

        for (int channel = 0; channel < UvChannelCount; channel++)
        {
            VertexAttribute attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
            if (!source.HasVertexAttribute(attribute))
            {
                continue;
            }

            CopyUvChannel(source, destination, channel, source.GetVertexAttributeDimension(attribute));
        }
    }

    private static void CopyUvChannel(Mesh.MeshData source, Mesh destination, int channel, int dimension)
    {
        if (dimension <= 2)
        {
            using (NativeArray<Vector2> uv = new NativeArray<Vector2>(
                       source.vertexCount,
                       Allocator.Temp,
                       NativeArrayOptions.UninitializedMemory))
            {
                source.GetUVs(channel, uv);
                destination.SetUVs(channel, uv);
            }

            return;
        }

        if (dimension == 3)
        {
            using (NativeArray<Vector3> uv = new NativeArray<Vector3>(
                       source.vertexCount,
                       Allocator.Temp,
                       NativeArrayOptions.UninitializedMemory))
            {
                source.GetUVs(channel, uv);
                destination.SetUVs(channel, uv);
            }

            return;
        }

        using (NativeArray<Vector4> uv = new NativeArray<Vector4>(
                   source.vertexCount,
                   Allocator.Temp,
                   NativeArrayOptions.UninitializedMemory))
        {
            source.GetUVs(channel, uv);
            destination.SetUVs(channel, uv);
        }
    }

    private static void CopySubMeshes(Mesh.MeshData source, Mesh destination)
    {
        destination.subMeshCount = source.subMeshCount;
        for (int subMeshIndex = 0; subMeshIndex < source.subMeshCount; subMeshIndex++)
        {
            SubMeshDescriptor descriptor = source.GetSubMesh(subMeshIndex);
            using (NativeArray<int> indices = new NativeArray<int>(
                       descriptor.indexCount,
                       Allocator.Temp,
                       NativeArrayOptions.UninitializedMemory))
            {
                source.GetIndices(indices, subMeshIndex, true);
                destination.SetIndices(indices, descriptor.topology, subMeshIndex, false);
            }
        }
    }

    private readonly struct IntersectionKey : IEquatable<IntersectionKey>
    {
        public IntersectionKey(int firstIndex, int secondIndex, int axis)
        {
            if (firstIndex <= secondIndex)
            {
                FirstIndex = firstIndex;
                SecondIndex = secondIndex;
            }
            else
            {
                FirstIndex = secondIndex;
                SecondIndex = firstIndex;
            }

            Axis = axis;
        }

        private int FirstIndex { get; }
        private int SecondIndex { get; }
        private int Axis { get; }

        public bool Equals(IntersectionKey other)
        {
            return FirstIndex == other.FirstIndex
                   && SecondIndex == other.SecondIndex
                   && Axis == other.Axis;
        }

        public override bool Equals(object obj)
        {
            return obj is IntersectionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = FirstIndex;
                hashCode = (hashCode * 397) ^ SecondIndex;
                hashCode = (hashCode * 397) ^ Axis;
                return hashCode;
            }
        }
    }

    private readonly struct OutputVertexKey : IEquatable<OutputVertexKey>
    {
        public OutputVertexKey(int sourceIndex, int mirrorMask)
        {
            SourceIndex = sourceIndex;
            MirrorMask = mirrorMask;
        }

        private int SourceIndex { get; }
        private int MirrorMask { get; }

        public bool Equals(OutputVertexKey other)
        {
            return SourceIndex == other.SourceIndex && MirrorMask == other.MirrorMask;
        }

        public override bool Equals(object obj)
        {
            return obj is OutputVertexKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (SourceIndex * 397) ^ MirrorMask;
        }
    }

    private readonly struct TriangleKey : IEquatable<TriangleKey>
    {
        public TriangleKey(int a, int b, int c)
        {
            if (a > b)
            {
                (a, b) = (b, a);
            }

            if (b > c)
            {
                (b, c) = (c, b);
            }

            if (a > b)
            {
                (a, b) = (b, a);
            }

            A = a;
            B = b;
            C = c;
        }

        private int A { get; }
        private int B { get; }
        private int C { get; }

        public bool Equals(TriangleKey other)
        {
            return A == other.A && B == other.B && C == other.C;
        }

        public override bool Equals(object obj)
        {
            return obj is TriangleKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = A;
                hashCode = (hashCode * 397) ^ B;
                hashCode = (hashCode * 397) ^ C;
                return hashCode;
            }
        }
    }

    private readonly struct InterpolationRecord
    {
        public InterpolationRecord(int firstIndex, int secondIndex, float t)
        {
            FirstIndex = firstIndex;
            SecondIndex = secondIndex;
            T = t;
        }

        public int FirstIndex { get; }
        public int SecondIndex { get; }
        public float T { get; }
    }

    private readonly struct OutputVertexOrigin
    {
        public OutputVertexOrigin(int sourceIndex, int mirrorMask)
        {
            SourceIndex = sourceIndex;
            MirrorMask = mirrorMask;
        }

        public int SourceIndex { get; }
        public int MirrorMask { get; }
    }

    private sealed class BlendShapeFrame
    {
        public string ShapeName;
        public float Weight;
        public Vector3[] DeltaVertices;
        public Vector3[] DeltaNormals;
        public Vector3[] DeltaTangents;
    }

    private sealed class MeshChannels
    {
        public readonly List<Vector3> Positions;
        public readonly List<Vector3> Normals;
        public readonly List<Vector4> Tangents;
        public readonly List<Color32> Colors;
        public readonly List<Vector4>[] UvChannels;
        public readonly List<BoneWeight> BoneWeights;
        public readonly List<InterpolationRecord> InterpolationRecords;
        public readonly bool HasNormals;
        public readonly bool HasTangents;
        public readonly bool HasColors;
        public readonly bool HasBoneWeights;

        public MeshChannels(Mesh mesh, Matrix4x4 meshToSymmetrySpace, Vector3 pivot, MeshSymmetrySettings settings)
        {
            int vertexCount = mesh.vertexCount;
            Vector3[] sourcePositions = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector4[] sourceTangents = mesh.tangents;
            Color32[] sourceColors = mesh.colors32;
            BoneWeight[] sourceBoneWeights = mesh.boneWeights;

            HasNormals = sourceNormals != null && sourceNormals.Length == vertexCount;
            HasTangents = sourceTangents != null && sourceTangents.Length == vertexCount;
            HasColors = sourceColors != null && sourceColors.Length == vertexCount;
            HasBoneWeights = sourceBoneWeights != null && sourceBoneWeights.Length == vertexCount;

            Positions = new List<Vector3>(vertexCount);
            Normals = HasNormals ? new List<Vector3>(vertexCount) : null;
            Tangents = HasTangents ? new List<Vector4>(vertexCount) : null;
            Colors = HasColors ? new List<Color32>(vertexCount) : null;
            BoneWeights = HasBoneWeights ? new List<BoneWeight>(vertexCount) : null;
            InterpolationRecords = new List<InterpolationRecord>(vertexCount);

            UvChannels = new List<Vector4>[UvChannelCount];
            List<Vector4> uvScratch = new List<Vector4>(vertexCount);
            for (int channel = 0; channel < UvChannelCount; channel++)
            {
                uvScratch.Clear();
                mesh.GetUVs(channel, uvScratch);
                if (uvScratch.Count == vertexCount)
                {
                    UvChannels[channel] = new List<Vector4>(uvScratch);
                }
            }

            Matrix4x4 normalToSymmetrySpace = meshToSymmetrySpace.inverse.transpose;
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 position = meshToSymmetrySpace.MultiplyPoint3x4(sourcePositions[i]);
                SnapPositionToEnabledPlanes(ref position, pivot, settings);
                Positions.Add(position);
                InterpolationRecords.Add(new InterpolationRecord(-1, -1, 0f));

                if (HasNormals)
                {
                    Normals.Add(normalToSymmetrySpace.MultiplyVector(sourceNormals[i]).normalized);
                }

                if (HasTangents)
                {
                    Vector3 tangentDirection = meshToSymmetrySpace.MultiplyVector(
                        new Vector3(sourceTangents[i].x, sourceTangents[i].y, sourceTangents[i].z)).normalized;
                    Tangents.Add(new Vector4(
                        tangentDirection.x,
                        tangentDirection.y,
                        tangentDirection.z,
                        sourceTangents[i].w));
                }

                if (HasColors)
                {
                    Colors.Add(sourceColors[i]);
                }

                if (HasBoneWeights)
                {
                    BoneWeights.Add(sourceBoneWeights[i]);
                }
            }
        }

        public int AddInterpolatedVertex(int firstIndex, int secondIndex, float t, int axis, float planeCoordinate)
        {
            int newIndex = Positions.Count;
            Vector3 position = Vector3.LerpUnclamped(Positions[firstIndex], Positions[secondIndex], t);
            SetAxis(ref position, axis, planeCoordinate);
            Positions.Add(position);
            InterpolationRecords.Add(new InterpolationRecord(firstIndex, secondIndex, t));

            if (HasNormals)
            {
                Normals.Add(Vector3.LerpUnclamped(Normals[firstIndex], Normals[secondIndex], t).normalized);
            }

            if (HasTangents)
            {
                Vector4 tangent = Vector4.LerpUnclamped(Tangents[firstIndex], Tangents[secondIndex], t);
                Vector3 tangentDirection = new Vector3(tangent.x, tangent.y, tangent.z).normalized;
                tangent.x = tangentDirection.x;
                tangent.y = tangentDirection.y;
                tangent.z = tangentDirection.z;
                tangent.w = Mathf.Abs(tangent.w) < 0.0001f ? 1f : Mathf.Sign(tangent.w);
                Tangents.Add(tangent);
            }

            if (HasColors)
            {
                Colors.Add(Color32.Lerp(Colors[firstIndex], Colors[secondIndex], t));
            }

            for (int channel = 0; channel < UvChannelCount; channel++)
            {
                List<Vector4> uv = UvChannels[channel];
                if (uv != null)
                {
                    uv.Add(Vector4.LerpUnclamped(uv[firstIndex], uv[secondIndex], t));
                }
            }

            if (HasBoneWeights)
            {
                BoneWeights.Add(InterpolateBoneWeight(BoneWeights[firstIndex], BoneWeights[secondIndex], t));
            }

            return newIndex;
        }
    }

    public static bool TryApply(
        Mesh mesh,
        Matrix4x4 meshToSymmetrySpace,
        Vector3 pivot,
        MeshSymmetrySettings settings,
        out string error)
    {
        error = string.Empty;
        if (mesh == null || !settings.Enabled)
        {
            return true;
        }

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                error = $"'{mesh.name}'의 SubMesh {subMeshIndex}가 Triangle 형식이 아니어서 대칭 처리할 수 없습니다.";
                return false;
            }
        }

        Matrix4x4[] bindPoses = mesh.bindposes;
        List<BlendShapeFrame> blendShapeFrames = CaptureBlendShapeFrames(mesh);
        MeshChannels channels = new MeshChannels(mesh, meshToSymmetrySpace, pivot, settings);
        List<int>[] sourceTrianglesBySubMesh = ClipSourceTriangles(mesh, channels, pivot, settings);

        List<Vector3> outputPositions = new List<Vector3>(channels.Positions.Count * GetCopyCount(settings));
        List<Vector3> outputNormals = channels.HasNormals ? new List<Vector3>(outputPositions.Capacity) : null;
        List<Vector4> outputTangents = channels.HasTangents ? new List<Vector4>(outputPositions.Capacity) : null;
        List<Color32> outputColors = channels.HasColors ? new List<Color32>(outputPositions.Capacity) : null;
        List<Vector4>[] outputUvChannels = CreateOutputUvChannels(channels, outputPositions.Capacity);
        List<BoneWeight> outputBoneWeights = channels.HasBoneWeights ? new List<BoneWeight>(outputPositions.Capacity) : null;
        List<OutputVertexOrigin> outputOrigins = new List<OutputVertexOrigin>(outputPositions.Capacity);
        List<int>[] outputTrianglesBySubMesh = CreateTriangleLists(sourceTrianglesBySubMesh, settings);
        Dictionary<OutputVertexKey, int> outputIndexBySource = new Dictionary<OutputVertexKey, int>();

        int[] mirrorMasks = BuildMirrorMasks(settings);
        Matrix4x4 symmetrySpaceToMesh = meshToSymmetrySpace.inverse;
        Matrix4x4 normalFromSymmetrySpace = meshToSymmetrySpace.transpose;
        for (int subMeshIndex = 0; subMeshIndex < sourceTrianglesBySubMesh.Length; subMeshIndex++)
        {
            List<int> sourceTriangles = sourceTrianglesBySubMesh[subMeshIndex];
            List<int> outputTriangles = outputTrianglesBySubMesh[subMeshIndex];
            for (int triangleIndex = 0; triangleIndex + 2 < sourceTriangles.Count; triangleIndex += 3)
            {
                int sourceA = sourceTriangles[triangleIndex];
                int sourceB = sourceTriangles[triangleIndex + 1];
                int sourceC = sourceTriangles[triangleIndex + 2];
                TriangleKey[] generatedKeys = new TriangleKey[mirrorMasks.Length];
                int generatedKeyCount = 0;
                for (int maskIndex = 0; maskIndex < mirrorMasks.Length; maskIndex++)
                {
                    int mirrorMask = mirrorMasks[maskIndex];
                    int a = GetOrCreateOutputVertex(
                        sourceA,
                        mirrorMask,
                        channels,
                        pivot,
                        settings,
                        symmetrySpaceToMesh,
                        normalFromSymmetrySpace,
                        outputIndexBySource,
                        outputPositions,
                        outputNormals,
                        outputTangents,
                        outputColors,
                        outputUvChannels,
                        outputBoneWeights,
                        outputOrigins);
                    int b = GetOrCreateOutputVertex(
                        sourceB,
                        mirrorMask,
                        channels,
                        pivot,
                        settings,
                        symmetrySpaceToMesh,
                        normalFromSymmetrySpace,
                        outputIndexBySource,
                        outputPositions,
                        outputNormals,
                        outputTangents,
                        outputColors,
                        outputUvChannels,
                        outputBoneWeights,
                        outputOrigins);
                    int c = GetOrCreateOutputVertex(
                        sourceC,
                        mirrorMask,
                        channels,
                        pivot,
                        settings,
                        symmetrySpaceToMesh,
                        normalFromSymmetrySpace,
                        outputIndexBySource,
                        outputPositions,
                        outputNormals,
                        outputTangents,
                        outputColors,
                        outputUvChannels,
                        outputBoneWeights,
                        outputOrigins);

                    TriangleKey key = new TriangleKey(a, b, c);
                    bool duplicateCopy = false;
                    for (int keyIndex = 0; keyIndex < generatedKeyCount; keyIndex++)
                    {
                        if (generatedKeys[keyIndex].Equals(key))
                        {
                            duplicateCopy = true;
                            break;
                        }
                    }

                    if (duplicateCopy)
                    {
                        continue;
                    }

                    generatedKeys[generatedKeyCount++] = key;
                    if (HasOddReflectionCount(mirrorMask))
                    {
                        outputTriangles.Add(b);
                        outputTriangles.Add(a);
                        outputTriangles.Add(c);
                    }
                    else
                    {
                        outputTriangles.Add(a);
                        outputTriangles.Add(b);
                        outputTriangles.Add(c);
                    }
                }
            }
        }

        string meshName = mesh.name;
        mesh.Clear();
        mesh.name = meshName;
        mesh.indexFormat = outputPositions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(outputPositions);
        if (outputNormals != null)
        {
            mesh.SetNormals(outputNormals);
        }

        if (outputTangents != null)
        {
            mesh.SetTangents(outputTangents);
        }

        if (outputColors != null)
        {
            mesh.SetColors(outputColors);
        }

        for (int channel = 0; channel < UvChannelCount; channel++)
        {
            if (outputUvChannels[channel] != null)
            {
                mesh.SetUVs(channel, outputUvChannels[channel]);
            }
        }

        mesh.subMeshCount = outputTrianglesBySubMesh.Length;
        for (int subMeshIndex = 0; subMeshIndex < outputTrianglesBySubMesh.Length; subMeshIndex++)
        {
            mesh.SetTriangles(outputTrianglesBySubMesh[subMeshIndex], subMeshIndex, false);
        }

        if (outputBoneWeights != null)
        {
            mesh.boneWeights = outputBoneWeights.ToArray();
            mesh.bindposes = bindPoses;
        }

        RestoreBlendShapes(
            mesh,
            blendShapeFrames,
            channels.InterpolationRecords,
            outputOrigins,
            meshToSymmetrySpace,
            symmetrySpaceToMesh);
        mesh.RecalculateBounds();
        return true;
    }

    private static List<int>[] ClipSourceTriangles(
        Mesh mesh,
        MeshChannels channels,
        Vector3 pivot,
        MeshSymmetrySettings settings)
    {
        List<int>[] trianglesBySubMesh = new List<int>[mesh.subMeshCount];
        Dictionary<IntersectionKey, int> intersectionIndices = new Dictionary<IntersectionKey, int>();
        List<int> polygon = new List<int>(8);
        List<int> scratch = new List<int>(8);
        float minimumAreaSquared = Mathf.Pow(settings.PlaneTolerance, 4f);

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            int[] sourceTriangles = mesh.GetTriangles(subMeshIndex);
            List<int> clippedTriangles = new List<int>(sourceTriangles.Length * GetCopyCount(settings));
            trianglesBySubMesh[subMeshIndex] = clippedTriangles;
            for (int triangleIndex = 0; triangleIndex + 2 < sourceTriangles.Length; triangleIndex += 3)
            {
                polygon.Clear();
                polygon.Add(sourceTriangles[triangleIndex]);
                polygon.Add(sourceTriangles[triangleIndex + 1]);
                polygon.Add(sourceTriangles[triangleIndex + 2]);

                if (settings.MirrorX)
                {
                    ClipPolygonToSourceSide(
                        polygon,
                        scratch,
                        channels,
                        intersectionIndices,
                        0,
                        pivot.x,
                        settings.XSourceSide,
                        settings.PlaneTolerance);
                    SwapLists(ref polygon, ref scratch);
                }

                if (settings.MirrorY && polygon.Count >= 3)
                {
                    ClipPolygonToSourceSide(
                        polygon,
                        scratch,
                        channels,
                        intersectionIndices,
                        1,
                        pivot.y,
                        settings.YSourceSide,
                        settings.PlaneTolerance);
                    SwapLists(ref polygon, ref scratch);
                }

                if (settings.MirrorZ && polygon.Count >= 3)
                {
                    ClipPolygonToSourceSide(
                        polygon,
                        scratch,
                        channels,
                        intersectionIndices,
                        2,
                        pivot.z,
                        settings.ZSourceSide,
                        settings.PlaneTolerance);
                    SwapLists(ref polygon, ref scratch);
                }

                for (int polygonIndex = 1; polygonIndex + 1 < polygon.Count; polygonIndex++)
                {
                    int a = polygon[0];
                    int b = polygon[polygonIndex];
                    int c = polygon[polygonIndex + 1];
                    Vector3 cross = Vector3.Cross(
                        channels.Positions[b] - channels.Positions[a],
                        channels.Positions[c] - channels.Positions[a]);
                    if (cross.sqrMagnitude <= minimumAreaSquared)
                    {
                        continue;
                    }

                    clippedTriangles.Add(a);
                    clippedTriangles.Add(b);
                    clippedTriangles.Add(c);
                }
            }
        }

        return trianglesBySubMesh;
    }

    private static void ClipPolygonToSourceSide(
        List<int> input,
        List<int> output,
        MeshChannels channels,
        Dictionary<IntersectionKey, int> intersectionIndices,
        int axis,
        float planeCoordinate,
        MeshSymmetrySide sourceSide,
        float tolerance)
    {
        output.Clear();
        if (input.Count <= 0)
        {
            return;
        }

        float sideSign = sourceSide == MeshSymmetrySide.Positive ? 1f : -1f;
        int previousIndex = input[input.Count - 1];
        float previousDistance = sideSign * (GetAxis(channels.Positions[previousIndex], axis) - planeCoordinate);
        bool previousInside = previousDistance >= -tolerance;
        for (int i = 0; i < input.Count; i++)
        {
            int currentIndex = input[i];
            float currentDistance = sideSign * (GetAxis(channels.Positions[currentIndex], axis) - planeCoordinate);
            bool currentInside = currentDistance >= -tolerance;

            if (previousInside != currentInside)
            {
                output.Add(GetOrCreateIntersection(
                    previousIndex,
                    currentIndex,
                    previousDistance,
                    currentDistance,
                    axis,
                    planeCoordinate,
                    channels,
                    intersectionIndices));
            }

            if (currentInside)
            {
                output.Add(currentIndex);
            }

            previousIndex = currentIndex;
            previousDistance = currentDistance;
            previousInside = currentInside;
        }
    }

    private static int GetOrCreateIntersection(
        int firstIndex,
        int secondIndex,
        float firstDistance,
        float secondDistance,
        int axis,
        float planeCoordinate,
        MeshChannels channels,
        Dictionary<IntersectionKey, int> intersectionIndices)
    {
        IntersectionKey key = new IntersectionKey(firstIndex, secondIndex, axis);
        if (intersectionIndices.TryGetValue(key, out int existingIndex))
        {
            return existingIndex;
        }

        float denominator = firstDistance - secondDistance;
        float t = Mathf.Abs(denominator) > Mathf.Epsilon
            ? Mathf.Clamp01(firstDistance / denominator)
            : 0.5f;
        int newIndex = channels.AddInterpolatedVertex(firstIndex, secondIndex, t, axis, planeCoordinate);
        intersectionIndices.Add(key, newIndex);
        return newIndex;
    }

    private static int GetOrCreateOutputVertex(
        int sourceIndex,
        int mirrorMask,
        MeshChannels channels,
        Vector3 pivot,
        MeshSymmetrySettings settings,
        Matrix4x4 symmetrySpaceToMesh,
        Matrix4x4 normalFromSymmetrySpace,
        Dictionary<OutputVertexKey, int> outputIndexBySource,
        List<Vector3> outputPositions,
        List<Vector3> outputNormals,
        List<Vector4> outputTangents,
        List<Color32> outputColors,
        List<Vector4>[] outputUvChannels,
        List<BoneWeight> outputBoneWeights,
        List<OutputVertexOrigin> outputOrigins)
    {
        Vector3 sourcePosition = channels.Positions[sourceIndex];
        int canonicalMask = mirrorMask;
        bool isOnXPlane = settings.MirrorX && Mathf.Abs(sourcePosition.x - pivot.x) <= settings.PlaneTolerance;
        bool isOnYPlane = settings.MirrorY && Mathf.Abs(sourcePosition.y - pivot.y) <= settings.PlaneTolerance;
        bool isOnZPlane = settings.MirrorZ && Mathf.Abs(sourcePosition.z - pivot.z) <= settings.PlaneTolerance;
        if (isOnXPlane)
        {
            canonicalMask &= ~MirrorXMask;
        }

        if (isOnYPlane)
        {
            canonicalMask &= ~MirrorYMask;
        }

        if (isOnZPlane)
        {
            canonicalMask &= ~MirrorZMask;
        }

        OutputVertexKey key = new OutputVertexKey(sourceIndex, canonicalMask);
        if (outputIndexBySource.TryGetValue(key, out int existingIndex))
        {
            return existingIndex;
        }

        Vector3 position = MirrorPoint(sourcePosition, pivot, canonicalMask);
        outputPositions.Add(symmetrySpaceToMesh.MultiplyPoint3x4(position));

        if (outputNormals != null)
        {
            Vector3 normal = MirrorVector(channels.Normals[sourceIndex], canonicalMask);
            if (isOnXPlane)
            {
                normal.x = 0f;
            }

            if (isOnYPlane)
            {
                normal.y = 0f;
            }

            if (isOnZPlane)
            {
                normal.z = 0f;
            }

            outputNormals.Add(normalFromSymmetrySpace.MultiplyVector(normal).normalized);
        }

        if (outputTangents != null)
        {
            Vector4 sourceTangent = channels.Tangents[sourceIndex];
            Vector3 tangentDirection = MirrorVector(
                new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z),
                canonicalMask);
            if (isOnXPlane)
            {
                tangentDirection.x = 0f;
            }

            if (isOnYPlane)
            {
                tangentDirection.y = 0f;
            }

            if (isOnZPlane)
            {
                tangentDirection.z = 0f;
            }

            tangentDirection = symmetrySpaceToMesh.MultiplyVector(tangentDirection).normalized;
            float tangentW = HasOddReflectionCount(canonicalMask) ? -sourceTangent.w : sourceTangent.w;
            outputTangents.Add(new Vector4(tangentDirection.x, tangentDirection.y, tangentDirection.z, tangentW));
        }

        if (outputColors != null)
        {
            outputColors.Add(channels.Colors[sourceIndex]);
        }

        for (int channel = 0; channel < UvChannelCount; channel++)
        {
            if (outputUvChannels[channel] != null)
            {
                outputUvChannels[channel].Add(channels.UvChannels[channel][sourceIndex]);
            }
        }

        if (outputBoneWeights != null)
        {
            outputBoneWeights.Add(channels.BoneWeights[sourceIndex]);
        }

        int outputIndex = outputPositions.Count - 1;
        outputOrigins.Add(new OutputVertexOrigin(sourceIndex, canonicalMask));
        outputIndexBySource.Add(key, outputIndex);
        return outputIndex;
    }

    private static List<Vector4>[] CreateOutputUvChannels(MeshChannels channels, int capacity)
    {
        List<Vector4>[] output = new List<Vector4>[UvChannelCount];
        for (int channel = 0; channel < UvChannelCount; channel++)
        {
            if (channels.UvChannels[channel] != null)
            {
                output[channel] = new List<Vector4>(capacity);
            }
        }

        return output;
    }

    private static List<int>[] CreateTriangleLists(List<int>[] sourceTrianglesBySubMesh, MeshSymmetrySettings settings)
    {
        int copyCount = GetCopyCount(settings);
        List<int>[] output = new List<int>[sourceTrianglesBySubMesh.Length];
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = new List<int>(sourceTrianglesBySubMesh[i].Count * copyCount);
        }

        return output;
    }

    private static int[] BuildMirrorMasks(MeshSymmetrySettings settings)
    {
        int[] masks = new int[GetCopyCount(settings)];
        int maskCount = 1;
        AppendMirrorAxisMasks(masks, ref maskCount, settings.MirrorX, MirrorXMask);
        AppendMirrorAxisMasks(masks, ref maskCount, settings.MirrorY, MirrorYMask);
        AppendMirrorAxisMasks(masks, ref maskCount, settings.MirrorZ, MirrorZMask);
        return masks;
    }

    private static int GetCopyCount(MeshSymmetrySettings settings)
    {
        int enabledAxisCount = 0;
        enabledAxisCount += settings.MirrorX ? 1 : 0;
        enabledAxisCount += settings.MirrorY ? 1 : 0;
        enabledAxisCount += settings.MirrorZ ? 1 : 0;
        return 1 << enabledAxisCount;
    }

    private static void AppendMirrorAxisMasks(int[] masks, ref int maskCount, bool enabled, int axisMask)
    {
        if (!enabled)
        {
            return;
        }

        for (int i = 0; i < maskCount; i++)
        {
            masks[maskCount + i] = masks[i] | axisMask;
        }

        maskCount *= 2;
    }

    private static void SwapLists(ref List<int> first, ref List<int> second)
    {
        (first, second) = (second, first);
    }

    private static void SnapPositionToEnabledPlanes(
        ref Vector3 position,
        Vector3 pivot,
        MeshSymmetrySettings settings)
    {
        if (settings.MirrorX && Mathf.Abs(position.x - pivot.x) <= settings.PlaneTolerance)
        {
            position.x = pivot.x;
        }

        if (settings.MirrorY && Mathf.Abs(position.y - pivot.y) <= settings.PlaneTolerance)
        {
            position.y = pivot.y;
        }

        if (settings.MirrorZ && Mathf.Abs(position.z - pivot.z) <= settings.PlaneTolerance)
        {
            position.z = pivot.z;
        }
    }

    private static Vector3 MirrorPoint(Vector3 point, Vector3 pivot, int mirrorMask)
    {
        if ((mirrorMask & MirrorXMask) != 0)
        {
            point.x = pivot.x * 2f - point.x;
        }

        if ((mirrorMask & MirrorYMask) != 0)
        {
            point.y = pivot.y * 2f - point.y;
        }

        if ((mirrorMask & MirrorZMask) != 0)
        {
            point.z = pivot.z * 2f - point.z;
        }

        return point;
    }

    private static Vector3 MirrorVector(Vector3 vector, int mirrorMask)
    {
        if ((mirrorMask & MirrorXMask) != 0)
        {
            vector.x = -vector.x;
        }

        if ((mirrorMask & MirrorYMask) != 0)
        {
            vector.y = -vector.y;
        }

        if ((mirrorMask & MirrorZMask) != 0)
        {
            vector.z = -vector.z;
        }

        return vector;
    }

    private static bool HasOddReflectionCount(int mirrorMask)
    {
        int reflectionCount = 0;
        reflectionCount += (mirrorMask & MirrorXMask) != 0 ? 1 : 0;
        reflectionCount += (mirrorMask & MirrorYMask) != 0 ? 1 : 0;
        reflectionCount += (mirrorMask & MirrorZMask) != 0 ? 1 : 0;
        return (reflectionCount & 1) != 0;
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void SetAxis(ref Vector3 value, int axis, float coordinate)
    {
        if (axis == 0)
        {
            value.x = coordinate;
        }
        else if (axis == 1)
        {
            value.y = coordinate;
        }
        else
        {
            value.z = coordinate;
        }
    }

    private static BoneWeight InterpolateBoneWeight(BoneWeight first, BoneWeight second, float t)
    {
        int[] boneIndices = new int[8];
        float[] weights = new float[8];
        int count = 0;
        AddBoneWeight(first.boneIndex0, first.weight0 * (1f - t), boneIndices, weights, ref count);
        AddBoneWeight(first.boneIndex1, first.weight1 * (1f - t), boneIndices, weights, ref count);
        AddBoneWeight(first.boneIndex2, first.weight2 * (1f - t), boneIndices, weights, ref count);
        AddBoneWeight(first.boneIndex3, first.weight3 * (1f - t), boneIndices, weights, ref count);
        AddBoneWeight(second.boneIndex0, second.weight0 * t, boneIndices, weights, ref count);
        AddBoneWeight(second.boneIndex1, second.weight1 * t, boneIndices, weights, ref count);
        AddBoneWeight(second.boneIndex2, second.weight2 * t, boneIndices, weights, ref count);
        AddBoneWeight(second.boneIndex3, second.weight3 * t, boneIndices, weights, ref count);

        for (int i = 0; i < count - 1; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (weights[j] <= weights[i])
                {
                    continue;
                }

                (weights[i], weights[j]) = (weights[j], weights[i]);
                (boneIndices[i], boneIndices[j]) = (boneIndices[j], boneIndices[i]);
            }
        }

        float totalWeight = 0f;
        int resultCount = Mathf.Min(4, count);
        for (int i = 0; i < resultCount; i++)
        {
            totalWeight += weights[i];
        }

        float inverseTotal = totalWeight > Mathf.Epsilon ? 1f / totalWeight : 0f;
        return new BoneWeight
        {
            boneIndex0 = resultCount > 0 ? boneIndices[0] : 0,
            weight0 = resultCount > 0 ? weights[0] * inverseTotal : 1f,
            boneIndex1 = resultCount > 1 ? boneIndices[1] : 0,
            weight1 = resultCount > 1 ? weights[1] * inverseTotal : 0f,
            boneIndex2 = resultCount > 2 ? boneIndices[2] : 0,
            weight2 = resultCount > 2 ? weights[2] * inverseTotal : 0f,
            boneIndex3 = resultCount > 3 ? boneIndices[3] : 0,
            weight3 = resultCount > 3 ? weights[3] * inverseTotal : 0f
        };
    }

    private static void AddBoneWeight(
        int boneIndex,
        float weight,
        int[] boneIndices,
        float[] weights,
        ref int count)
    {
        if (weight <= 0f)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (boneIndices[i] != boneIndex)
            {
                continue;
            }

            weights[i] += weight;
            return;
        }

        boneIndices[count] = boneIndex;
        weights[count] = weight;
        count++;
    }

    private static List<BlendShapeFrame> CaptureBlendShapeFrames(Mesh mesh)
    {
        List<BlendShapeFrame> frames = new List<BlendShapeFrame>();
        int vertexCount = mesh.vertexCount;
        for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
        {
            string shapeName = mesh.GetBlendShapeName(shapeIndex);
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                Vector3[] deltaVertices = new Vector3[vertexCount];
                Vector3[] deltaNormals = new Vector3[vertexCount];
                Vector3[] deltaTangents = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(
                    shapeIndex,
                    frameIndex,
                    deltaVertices,
                    deltaNormals,
                    deltaTangents);
                frames.Add(new BlendShapeFrame
                {
                    ShapeName = shapeName,
                    Weight = mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex),
                    DeltaVertices = deltaVertices,
                    DeltaNormals = deltaNormals,
                    DeltaTangents = deltaTangents
                });
            }
        }

        return frames;
    }

    private static void RestoreBlendShapes(
        Mesh mesh,
        List<BlendShapeFrame> frames,
        List<InterpolationRecord> interpolationRecords,
        List<OutputVertexOrigin> outputOrigins,
        Matrix4x4 meshToSymmetrySpace,
        Matrix4x4 symmetrySpaceToMesh)
    {
        if (frames == null || frames.Count <= 0 || outputOrigins.Count <= 0)
        {
            return;
        }

        Matrix4x4 normalToSymmetrySpace = meshToSymmetrySpace.inverse.transpose;
        Matrix4x4 normalFromSymmetrySpace = meshToSymmetrySpace.transpose;
        int workingVertexCount = interpolationRecords.Count;
        for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            BlendShapeFrame frame = frames[frameIndex];
            Vector3[] workingDeltaVertices = new Vector3[workingVertexCount];
            Vector3[] workingDeltaNormals = new Vector3[workingVertexCount];
            Vector3[] workingDeltaTangents = new Vector3[workingVertexCount];
            int originalVertexCount = frame.DeltaVertices.Length;
            for (int i = 0; i < originalVertexCount; i++)
            {
                workingDeltaVertices[i] = meshToSymmetrySpace.MultiplyVector(frame.DeltaVertices[i]);
                workingDeltaNormals[i] = normalToSymmetrySpace.MultiplyVector(frame.DeltaNormals[i]);
                workingDeltaTangents[i] = meshToSymmetrySpace.MultiplyVector(frame.DeltaTangents[i]);
            }

            for (int i = originalVertexCount; i < workingVertexCount; i++)
            {
                InterpolationRecord record = interpolationRecords[i];
                workingDeltaVertices[i] = Vector3.LerpUnclamped(
                    workingDeltaVertices[record.FirstIndex],
                    workingDeltaVertices[record.SecondIndex],
                    record.T);
                workingDeltaNormals[i] = Vector3.LerpUnclamped(
                    workingDeltaNormals[record.FirstIndex],
                    workingDeltaNormals[record.SecondIndex],
                    record.T);
                workingDeltaTangents[i] = Vector3.LerpUnclamped(
                    workingDeltaTangents[record.FirstIndex],
                    workingDeltaTangents[record.SecondIndex],
                    record.T);
            }

            Vector3[] outputDeltaVertices = new Vector3[outputOrigins.Count];
            Vector3[] outputDeltaNormals = new Vector3[outputOrigins.Count];
            Vector3[] outputDeltaTangents = new Vector3[outputOrigins.Count];
            for (int i = 0; i < outputOrigins.Count; i++)
            {
                OutputVertexOrigin origin = outputOrigins[i];
                Vector3 deltaVertex = MirrorVector(workingDeltaVertices[origin.SourceIndex], origin.MirrorMask);
                Vector3 deltaNormal = MirrorVector(workingDeltaNormals[origin.SourceIndex], origin.MirrorMask);
                Vector3 deltaTangent = MirrorVector(workingDeltaTangents[origin.SourceIndex], origin.MirrorMask);
                outputDeltaVertices[i] = symmetrySpaceToMesh.MultiplyVector(deltaVertex);
                outputDeltaNormals[i] = normalFromSymmetrySpace.MultiplyVector(deltaNormal);
                outputDeltaTangents[i] = symmetrySpaceToMesh.MultiplyVector(deltaTangent);
            }

            mesh.AddBlendShapeFrame(
                frame.ShapeName,
                frame.Weight,
                outputDeltaVertices,
                outputDeltaNormals,
                outputDeltaTangents);
        }
    }
}
