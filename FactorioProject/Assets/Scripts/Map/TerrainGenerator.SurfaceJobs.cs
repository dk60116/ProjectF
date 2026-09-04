using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainGenerator
{
    private static readonly ProfilerMarker ScheduleChunkSurfaceJobMarker =
        new ProfilerMarker("TerrainGenerator.ScheduleChunkSurfaceBurstJob");
    private static readonly ProfilerMarker CompleteChunkSurfaceJobMarker =
        new ProfilerMarker("TerrainGenerator.CompleteChunkSurfaceBurstJob");
    private static readonly ProfilerMarker ScheduleChunkSurfaceMeshDataJobMarker =
        new ProfilerMarker("TerrainGenerator.ScheduleChunkSurfaceMeshDataJob");
    private static readonly ProfilerMarker ApplyChunkSurfaceMeshDataMarker =
        new ProfilerMarker("TerrainGenerator.ApplyChunkSurfaceMeshData");

    private static readonly VertexAttributeDescriptor[] GeneratedSurfaceVertexLayout =
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
    };

    private TerrainSurfaceBuildJobState activeSurfaceBuildJob;
    private TerrainSurfaceBuildJobState reusableSurfaceBuildJob;

    private TerrainSurfaceBuildJobState CreateChunkSurfaceBuildJob(
        Vector2Int origin,
        int chunkSizeInBlocks)
    {
        if (activeSurfaceBuildJob != null)
        {
            throw new InvalidOperationException("A terrain surface build job is already active.");
        }

        ChunkSurfaceWorkerInput input = CreateChunkSurfaceWorkerInput(origin, chunkSizeInBlocks);
        TerrainSurfaceBuildJobState state = reusableSurfaceBuildJob;
        reusableSurfaceBuildJob = null;
        try
        {
            if (state == null)
            {
                state = new TerrainSurfaceBuildJobState(input);
            }
            else
            {
                state.Prepare(input);
            }

            using (ScheduleChunkSurfaceJobMarker.Auto())
            {
                state.Schedule();
            }

            activeSurfaceBuildJob = state;
            return state;
        }
        catch
        {
            state?.Dispose();
            throw;
        }
        finally
        {
            ReturnChunkSurfaceWorkerInput(input);
        }
    }

    private void CompleteChunkSurfaceBuildJob(TerrainSurfaceBuildJobState state)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        using (CompleteChunkSurfaceJobMarker.Auto())
        {
            state.Complete();
        }
    }

    private bool ScheduleChunkSurfaceMeshDataJob(TerrainSurfaceBuildJobState state)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        using (ScheduleChunkSurfaceMeshDataJobMarker.Auto())
        {
            return state.ScheduleMeshDataCopy();
        }
    }

    private Mesh CompleteChunkSurfaceMeshDataJob(
        TerrainSurfaceBuildJobState state,
        out Bounds localBounds,
        out int subMeshMask)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        using (ApplyChunkSurfaceMeshDataMarker.Auto())
        {
            return state.CompleteMeshDataCopyAndCreateMesh(out localBounds, out subMeshMask);
        }
    }

    private void ReleaseChunkSurfaceBuildJob(TerrainSurfaceBuildJobState state)
    {
        if (state == null)
        {
            return;
        }

        if (ReferenceEquals(activeSurfaceBuildJob, state))
        {
            activeSurfaceBuildJob = null;
        }

        if (reusableSurfaceBuildJob == null)
        {
            reusableSurfaceBuildJob = state;
        }
        else
        {
            state.Dispose();
        }
    }

    private void DisposeActiveSurfaceBuildJob()
    {
        TerrainSurfaceBuildJobState state = activeSurfaceBuildJob;
        activeSurfaceBuildJob = null;
        state?.Dispose();

        state = reusableSurfaceBuildJob;
        reusableSurfaceBuildJob = null;
        state?.Dispose();
    }

    private sealed class TerrainSurfaceBuildJobState : IDisposable
    {
        private NativeArray<TerrainBiome> biomeGrid;
        private NativeArray<byte> blockedWaterGrid;
        private NativeArray<byte> oilGrid;
        private NativeArray<float> contourScores;
        private NativeList<Vector3> vertices;
        private NativeList<Vector3> normals;
        private NativeList<Vector2> uvs;
        private NativeList<Color> colors;
        private NativeList<int> waterTriangles;
        private NativeList<int> blendTriangles;
        private NativeList<int> rockTriangles;
        private NativeList<int> foamTriangles;
        private NativeArray<Vector3> surfaceBoundsMinMax;
        private BuildTerrainSurfaceJob job;
        private JobHandle handle;
        private bool scheduled;
        private bool completed;
        private Mesh.MeshDataArray writableMeshData;
        private JobHandle meshDataCopyHandle;
        private bool hasWritableMeshData;
        private bool meshDataCopyScheduled;
        private IndexFormat meshIndexFormat;

        public bool IsCompleted => scheduled && handle.IsCompleted;
        public bool IsMeshDataReady => meshDataCopyScheduled && meshDataCopyHandle.IsCompleted;

        public TerrainSurfaceBuildJobState(ChunkSurfaceWorkerInput input)
        {
            Prepare(input);
        }

        public void Prepare(ChunkSurfaceWorkerInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (scheduled && !completed)
            {
                Complete();
            }

            DisposeWritableMeshData();

            int gridLength = Math.Max(1, input.biomeGridWidth * input.biomeGridHeight);
            int scoreRowLength = input.cellCount + 1;
            int scoreCount = Math.Max(1, scoreRowLength * scoreRowLength);
            long cellCountLong = (long)Math.Max(1, input.cellCount) * Math.Max(1, input.cellCount);
            int estimatedCellCount = (int)Math.Min(cellCountLong, int.MaxValue / 16L);
            int estimatedVertexCapacity = Math.Max(64, estimatedCellCount * 8);
            int estimatedTriangleCapacity = Math.Max(96, estimatedCellCount * 3);

            EnsureNativeArrayCapacity(ref biomeGrid, gridLength);
            EnsureNativeArrayCapacity(ref blockedWaterGrid, gridLength);
            EnsureNativeArrayCapacity(ref oilGrid, gridLength);
            EnsureNativeArrayCapacity(ref contourScores, scoreCount);
            EnsureNativeListCapacity(ref vertices, estimatedVertexCapacity);
            EnsureNativeListCapacity(ref normals, estimatedVertexCapacity);
            EnsureNativeListCapacity(ref uvs, estimatedVertexCapacity);
            EnsureNativeListCapacity(ref colors, estimatedVertexCapacity);
            EnsureNativeListCapacity(ref waterTriangles, estimatedTriangleCapacity);
            EnsureNativeListCapacity(ref blendTriangles, estimatedTriangleCapacity);
            EnsureNativeListCapacity(ref rockTriangles, estimatedTriangleCapacity);
            EnsureNativeListCapacity(ref foamTriangles, estimatedTriangleCapacity);
            EnsureNativeArrayCapacity(ref surfaceBoundsMinMax, 2);
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            colors.Clear();
            waterTriangles.Clear();
            blendTriangles.Clear();
            rockTriangles.Clear();
            foamTriangles.Clear();

            for (int i = 0; i < gridLength; i++)
            {
                biomeGrid[i] = input.biomeGrid[i];
                blockedWaterGrid[i] = input.blockedWaterGrid[i] ? (byte)1 : (byte)0;
                oilGrid[i] = input.oilGrid[i] ? (byte)1 : (byte)0;
            }

            job = new BuildTerrainSurfaceJob
            {
                BiomeGrid = biomeGrid,
                BlockedWaterGrid = blockedWaterGrid,
                OilGrid = oilGrid,
                ContourScores = contourScores,
                Vertices = vertices,
                Normals = normals,
                Uvs = uvs,
                Colors = colors,
                WaterTriangles = waterTriangles,
                BlendTriangles = blendTriangles,
                RockTriangles = rockTriangles,
                FoamTriangles = foamTriangles,
                SurfaceBoundsMinMax = surfaceBoundsMinMax,
                Origin = input.origin,
                Resolution = input.resolution,
                CellCount = input.cellCount,
                BiomeGridMinX = input.biomeGridMinX,
                BiomeGridMinY = input.biomeGridMinY,
                BiomeGridWidth = input.biomeGridWidth,
                BiomeGridHeight = input.biomeGridHeight,
                MapMinX = input.mapMinX,
                MapMinY = input.mapMinY,
                MapMaxExclusiveX = input.mapMaxExclusiveX,
                MapMaxExclusiveY = input.mapMaxExclusiveY,
                GeneratedSurfaceYOffset = input.generatedSurfaceYOffset,
                WaterSurfaceDepth = input.waterSurfaceDepth,
                GenerateWaterFoamOverlay = input.generateWaterFoamOverlay ? (byte)1 : (byte)0,
                WaterFoamWidth = input.waterFoamWidth,
                WaterFoamSurfaceOffset = input.waterFoamSurfaceOffset,
                WaterFoamOverlayColor = input.waterFoamOverlayColor,
                TerrainBlendJitter = input.terrainBlendJitter,
                TerrainSurfaceVertexJitter = input.terrainSurfaceVertexJitter,
                Seed = input.seed
            };
            handle = default;
            scheduled = false;
            completed = false;
            meshDataCopyHandle = default;
            meshDataCopyScheduled = false;
        }

        public void Schedule()
        {
            if (scheduled)
            {
                return;
            }

            handle = job.Schedule();
            JobHandle.ScheduleBatchedJobs();
            scheduled = true;
        }

        public void Complete()
        {
            if (!scheduled || completed)
            {
                return;
            }

            try
            {
                handle.Complete();
            }
            finally
            {
                completed = true;
            }
        }

        public bool ScheduleMeshDataCopy()
        {
            Complete();
            if (meshDataCopyScheduled)
            {
                return true;
            }

            if (vertices.Length == 0)
            {
                return false;
            }

            int indexCount = waterTriangles.Length
                             + blendTriangles.Length
                             + rockTriangles.Length
                             + foamTriangles.Length;
            writableMeshData = Mesh.AllocateWritableMeshData(1);
            hasWritableMeshData = true;
            Mesh.MeshData meshData = writableMeshData[0];
            meshData.SetVertexBufferParams(vertices.Length, GeneratedSurfaceVertexLayout);
            meshIndexFormat = vertices.Length > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            meshData.SetIndexBufferParams(indexCount, meshIndexFormat);
            meshData.subMeshCount = GeneratedSurfaceRenderSubMeshCount;

            CopyTerrainSurfaceToMeshDataJob copyJob = new CopyTerrainSurfaceToMeshDataJob
            {
                MeshData = meshData,
                Vertices = vertices,
                Normals = normals,
                Uvs = uvs,
                Colors = colors,
                WaterTriangles = waterTriangles,
                BlendTriangles = blendTriangles,
                RockTriangles = rockTriangles,
                FoamTriangles = foamTriangles,
                Use32BitIndices = meshIndexFormat == IndexFormat.UInt32 ? (byte)1 : (byte)0
            };
            meshDataCopyHandle = copyJob.Schedule();
            JobHandle.ScheduleBatchedJobs();
            meshDataCopyScheduled = true;
            return true;
        }

        public Mesh CompleteMeshDataCopyAndCreateMesh(
            out Bounds localBounds,
            out int subMeshMask)
        {
            if (!meshDataCopyScheduled || !hasWritableMeshData)
            {
                throw new InvalidOperationException("Terrain surface MeshData has not been scheduled.");
            }

            meshDataCopyHandle.Complete();
            meshDataCopyHandle = default;
            meshDataCopyScheduled = false;
            localBounds = GetLocalBounds();
            Mesh.MeshData meshData = writableMeshData[0];
            MeshUpdateFlags updateFlags = MeshUpdateFlags.DontRecalculateBounds
                                          | MeshUpdateFlags.DontValidateIndices
                                          | MeshUpdateFlags.DontNotifyMeshUsers;
            int indexStart = 0;
            SetSubMesh(
                meshData,
                GeneratedSurfaceWaterRenderSubMeshIndex,
                ref indexStart,
                waterTriangles.Length,
                vertices.Length,
                localBounds,
                updateFlags);
            SetSubMesh(
                meshData,
                GeneratedSurfaceBlendRenderSubMeshIndex,
                ref indexStart,
                blendTriangles.Length,
                vertices.Length,
                localBounds,
                updateFlags);
            SetSubMesh(
                meshData,
                GeneratedSurfaceRockRenderSubMeshIndex,
                ref indexStart,
                rockTriangles.Length,
                vertices.Length,
                localBounds,
                updateFlags);
            SetSubMesh(
                meshData,
                GeneratedSurfaceFoamRenderSubMeshIndex,
                ref indexStart,
                foamTriangles.Length,
                vertices.Length,
                localBounds,
                updateFlags);

            subMeshMask = 0;
            SetSubMeshMaskBit(waterTriangles.Length, GeneratedSurfaceWaterRenderSubMeshIndex, ref subMeshMask);
            SetSubMeshMaskBit(blendTriangles.Length, GeneratedSurfaceBlendRenderSubMeshIndex, ref subMeshMask);
            SetSubMeshMaskBit(rockTriangles.Length, GeneratedSurfaceRockRenderSubMeshIndex, ref subMeshMask);
            SetSubMeshMaskBit(foamTriangles.Length, GeneratedSurfaceFoamRenderSubMeshIndex, ref subMeshMask);

            Mesh mesh = new Mesh
            {
                indexFormat = meshIndexFormat
            };
            try
            {
                Mesh.ApplyAndDisposeWritableMeshData(writableMeshData, mesh, updateFlags);
                hasWritableMeshData = false;
                writableMeshData = default;
                mesh.bounds = localBounds;
                mesh.UploadMeshData(true);
                return mesh;
            }
            catch
            {
                DestroyGeneratedSurfaceMesh(mesh);
                throw;
            }
        }

        public void Dispose()
        {
            if (scheduled && !completed)
            {
                handle.Complete();
                completed = true;
            }

            DisposeWritableMeshData();

            DisposeIfCreated(ref biomeGrid);
            DisposeIfCreated(ref blockedWaterGrid);
            DisposeIfCreated(ref oilGrid);
            DisposeIfCreated(ref contourScores);
            DisposeIfCreated(ref vertices);
            DisposeIfCreated(ref normals);
            DisposeIfCreated(ref uvs);
            DisposeIfCreated(ref colors);
            DisposeIfCreated(ref waterTriangles);
            DisposeIfCreated(ref blendTriangles);
            DisposeIfCreated(ref rockTriangles);
            DisposeIfCreated(ref foamTriangles);
            DisposeIfCreated(ref surfaceBoundsMinMax);
        }

        private Bounds GetLocalBounds()
        {
            if (!surfaceBoundsMinMax.IsCreated || vertices.Length == 0)
            {
                return default;
            }

            Vector3 minimum = surfaceBoundsMinMax[0];
            Vector3 maximum = surfaceBoundsMinMax[1];
            return new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);
        }

        private void DisposeWritableMeshData()
        {
            if (meshDataCopyScheduled)
            {
                meshDataCopyHandle.Complete();
                meshDataCopyScheduled = false;
            }

            if (hasWritableMeshData)
            {
                writableMeshData.Dispose();
                hasWritableMeshData = false;
                writableMeshData = default;
            }
        }

        private static void SetSubMesh(
            Mesh.MeshData meshData,
            int subMeshIndex,
            ref int indexStart,
            int indexCount,
            int vertexCount,
            Bounds bounds,
            MeshUpdateFlags updateFlags)
        {
            SubMeshDescriptor descriptor = new SubMeshDescriptor(
                indexStart,
                indexCount,
                MeshTopology.Triangles)
            {
                bounds = bounds,
                firstVertex = 0,
                vertexCount = vertexCount
            };
            meshData.SetSubMesh(subMeshIndex, descriptor, updateFlags);
            indexStart += indexCount;
        }

        private static void SetSubMeshMaskBit(int indexCount, int subMeshIndex, ref int mask)
        {
            if (indexCount > 0)
            {
                mask |= 1 << subMeshIndex;
            }
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }

            array = default;
        }

        private static void DisposeIfCreated<T>(ref NativeList<T> list)
            where T : unmanaged
        {
            if (list.IsCreated)
            {
                list.Dispose();
            }

            list = default;
        }

        private static void EnsureNativeArrayCapacity<T>(
            ref NativeArray<T> array,
            int requiredCapacity)
            where T : struct
        {
            if (array.IsCreated && array.Length >= requiredCapacity)
            {
                return;
            }

            DisposeIfCreated(ref array);
            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, requiredCapacity));
            array = new NativeArray<T>(
                capacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureNativeListCapacity<T>(
            ref NativeList<T> list,
            int requiredCapacity)
            where T : unmanaged
        {
            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, requiredCapacity));
            if (!list.IsCreated)
            {
                list = new NativeList<T>(capacity, Allocator.Persistent);
                return;
            }

            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TerrainSurfaceVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Color Color;
        public Vector2 Uv;
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Default)]
    private struct CopyTerrainSurfaceToMeshDataJob : IJob
    {
        public Mesh.MeshData MeshData;
        [ReadOnly] public NativeList<Vector3> Vertices;
        [ReadOnly] public NativeList<Vector3> Normals;
        [ReadOnly] public NativeList<Vector2> Uvs;
        [ReadOnly] public NativeList<Color> Colors;
        [ReadOnly] public NativeList<int> WaterTriangles;
        [ReadOnly] public NativeList<int> BlendTriangles;
        [ReadOnly] public NativeList<int> RockTriangles;
        [ReadOnly] public NativeList<int> FoamTriangles;
        public byte Use32BitIndices;

        public void Execute()
        {
            NativeArray<TerrainSurfaceVertex> vertexData =
                MeshData.GetVertexData<TerrainSurfaceVertex>();
            for (int i = 0; i < Vertices.Length; i++)
            {
                vertexData[i] = new TerrainSurfaceVertex
                {
                    Position = Vertices[i],
                    Normal = i < Normals.Length ? Normals[i] : Vector3.up,
                    Color = i < Colors.Length ? Colors[i] : Color.white,
                    Uv = i < Uvs.Length ? Uvs[i] : default
                };
            }

            if (Use32BitIndices != 0)
            {
                NativeArray<int> indexData = MeshData.GetIndexData<int>();
                int destinationIndex = 0;
                CopyIndices(WaterTriangles, indexData, ref destinationIndex);
                CopyIndices(BlendTriangles, indexData, ref destinationIndex);
                CopyIndices(RockTriangles, indexData, ref destinationIndex);
                CopyIndices(FoamTriangles, indexData, ref destinationIndex);
                return;
            }

            NativeArray<ushort> shortIndexData = MeshData.GetIndexData<ushort>();
            int shortDestinationIndex = 0;
            CopyIndices(WaterTriangles, shortIndexData, ref shortDestinationIndex);
            CopyIndices(BlendTriangles, shortIndexData, ref shortDestinationIndex);
            CopyIndices(RockTriangles, shortIndexData, ref shortDestinationIndex);
            CopyIndices(FoamTriangles, shortIndexData, ref shortDestinationIndex);
        }

        private static void CopyIndices(
            NativeList<int> source,
            NativeArray<int> destination,
            ref int destinationIndex)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[destinationIndex++] = source[i];
            }
        }

        private static void CopyIndices(
            NativeList<int> source,
            NativeArray<ushort> destination,
            ref int destinationIndex)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[destinationIndex++] = (ushort)source[i];
            }
        }
    }

    private struct SurfaceBiomeWeights
    {
        public float Water;
        public float Sand;
        public float Dirt;
        public float Grass;
        public float Forest;
        public float Rock;

        public float Get(int index)
        {
            switch (index)
            {
                case 0: return Water;
                case 1: return Sand;
                case 2: return Dirt;
                case 3: return Grass;
                case 4: return Forest;
                case 5: return Rock;
                default: return 0f;
            }
        }

        public void Add(TerrainBiome biome, float value)
        {
            switch (biome)
            {
                case TerrainBiome.Water:
                    Water += value;
                    break;
                case TerrainBiome.Sand:
                    Sand += value;
                    break;
                case TerrainBiome.Dirt:
                    Dirt += value;
                    break;
                case TerrainBiome.Grass:
                    Grass += value;
                    break;
                case TerrainBiome.Forest:
                    Forest += value;
                    break;
                case TerrainBiome.Rock:
                    Rock += value;
                    break;
            }
        }
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Default)]
    private struct BuildTerrainSurfaceJob : IJob
    {
        [ReadOnly] public NativeArray<TerrainBiome> BiomeGrid;
        [ReadOnly] public NativeArray<byte> BlockedWaterGrid;
        [ReadOnly] public NativeArray<byte> OilGrid;
        public NativeArray<float> ContourScores;
        public NativeList<Vector3> Vertices;
        public NativeList<Vector3> Normals;
        public NativeList<Vector2> Uvs;
        public NativeList<Color> Colors;
        public NativeList<int> WaterTriangles;
        public NativeList<int> BlendTriangles;
        public NativeList<int> RockTriangles;
        public NativeList<int> FoamTriangles;
        public NativeArray<Vector3> SurfaceBoundsMinMax;
        public Vector2Int Origin;
        public int Resolution;
        public int CellCount;
        public int BiomeGridMinX;
        public int BiomeGridMinY;
        public int BiomeGridWidth;
        public int BiomeGridHeight;
        public int MapMinX;
        public int MapMinY;
        public int MapMaxExclusiveX;
        public int MapMaxExclusiveY;
        public float GeneratedSurfaceYOffset;
        public float WaterSurfaceDepth;
        public byte GenerateWaterFoamOverlay;
        public float WaterFoamWidth;
        public float WaterFoamSurfaceOffset;
        public Color WaterFoamOverlayColor;
        public float TerrainBlendJitter;
        public float TerrainSurfaceVertexJitter;
        public int Seed;

        public void Execute()
        {
            AppendDominantBiomeBaseSurface();
            for (int biomeIndex = 0; biomeIndex < GeneratedSurfaceBiomeMaterialCount; biomeIndex++)
            {
                AppendBiomeContourSurface((TerrainBiome)biomeIndex);
            }

            AppendContourSafetyPatches();
            BuildNormals();
            CalculateBounds();
        }

        private void AppendDominantBiomeBaseSurface()
        {
            FixedList128Bytes<Vector2> polygon = default;
            for (int cellY = 0; cellY < CellCount; cellY++)
            {
                for (int cellX = 0; cellX < CellCount; cellX++)
                {
                    GetCellPoints(cellX, cellY, out Vector2 p00, out Vector2 p10, out Vector2 p11, out Vector2 p01);
                    Vector2 center = (p00 + p11) * 0.5f;
                    Vector2 centerWorld = new Vector2(Origin.x + center.x, Origin.y + center.y);
                    if (!IsSurfaceSampleInsideMapBounds(centerWorld))
                    {
                        continue;
                    }

                    TerrainBiome dominantBiome = GetDominantBiomeAtSample(centerWorld);
                    if (dominantBiome == TerrainBiome.Water
                        || ShouldSkipDominantBaseSurfaceForWaterEdge(p00, p10, p11, p01))
                    {
                        continue;
                    }

                    SetQuad(ref polygon, p00, p10, p11, p01);
                    AppendContourPolygonAtHeight(
                        dominantBiome,
                        ref polygon,
                        GetBiomeBaseSurfaceY(dominantBiome, GeneratedSurfaceYOffset, WaterSurfaceDepth));
                }
            }
        }

        private bool ShouldSkipDominantBaseSurfaceForWaterEdge(
            Vector2 p00,
            Vector2 p10,
            Vector2 p11,
            Vector2 p01)
        {
            bool water00 = GetBiomeScoreAtSample(ToWorld(p00), TerrainBiome.Water) > 0f;
            bool water10 = GetBiomeScoreAtSample(ToWorld(p10), TerrainBiome.Water) > 0f;
            bool water11 = GetBiomeScoreAtSample(ToWorld(p11), TerrainBiome.Water) > 0f;
            bool water01 = GetBiomeScoreAtSample(ToWorld(p01), TerrainBiome.Water) > 0f;
            return water00 != water10 || water10 != water11 || water11 != water01;
        }

        private void AppendBiomeContourSurface(TerrainBiome biome)
        {
            int scoreRowLength = CellCount + 1;
            FixedList128Bytes<Vector2> polygon = default;
            for (int sampleY = 0; sampleY <= CellCount; sampleY++)
            {
                for (int sampleX = 0; sampleX <= CellCount; sampleX++)
                {
                    Vector2 sampleLocal = new Vector2(
                        -0.5f + (sampleX / (float)Resolution),
                        -0.5f + (sampleY / (float)Resolution));
                    ContourScores[sampleX + (sampleY * scoreRowLength)] =
                        GetBiomeScoreAtSample(ToWorld(sampleLocal), biome);
                }
            }

            for (int cellY = 0; cellY < CellCount; cellY++)
            {
                for (int cellX = 0; cellX < CellCount; cellX++)
                {
                    GetCellPoints(cellX, cellY, out Vector2 p00, out Vector2 p10, out Vector2 p11, out Vector2 p01);
                    Vector2 centerWorld = ToWorld((p00 + p11) * 0.5f);
                    if (!IsSurfaceSampleInsideMapBounds(centerWorld))
                    {
                        continue;
                    }

                    int scoreIndex = cellX + (cellY * scoreRowLength);
                    AppendMarchingSquaresCell(
                        biome,
                        p00,
                        p10,
                        p11,
                        p01,
                        ContourScores[scoreIndex],
                        ContourScores[scoreIndex + 1],
                        ContourScores[scoreIndex + scoreRowLength + 1],
                        ContourScores[scoreIndex + scoreRowLength],
                        GetBiomeScoreAtSample(centerWorld, biome),
                        ref polygon);
                }
            }
        }

        private void AppendMarchingSquaresCell(
            TerrainBiome biome,
            Vector2 p00,
            Vector2 p10,
            Vector2 p11,
            Vector2 p01,
            float s00,
            float s10,
            float s11,
            float s01,
            float centerScore,
            ref FixedList128Bytes<Vector2> polygon)
        {
            bool inside00 = s00 > 0f;
            bool inside10 = s10 > 0f;
            bool inside11 = s11 > 0f;
            bool inside01 = s01 > 0f;
            int mask = (inside00 ? 1 : 0)
                       | (inside10 ? 2 : 0)
                       | (inside11 ? 4 : 0)
                       | (inside01 ? 8 : 0);
            if (mask == 0)
            {
                return;
            }

            if (mask == 15)
            {
                SetQuad(ref polygon, p00, p10, p11, p01);
                AppendContourPolygon(biome, ref polygon);
                return;
            }

            Vector2 bottom = InterpolateContourPoint(p00, p10, s00, s10);
            Vector2 right = InterpolateContourPoint(p10, p11, s10, s11);
            Vector2 top = InterpolateContourPoint(p11, p01, s11, s01);
            Vector2 left = InterpolateContourPoint(p01, p00, s01, s00);
            if (biome == TerrainBiome.Water)
            {
                AppendWaterContourWalls(mask, centerScore, bottom, right, top, left);
            }

            if ((mask == 5 || mask == 10) && centerScore <= 0f)
            {
                if (mask == 5)
                {
                    SetTriangle(ref polygon, p00, bottom, left);
                    AppendContourPolygon(biome, ref polygon);
                    SetTriangle(ref polygon, p11, top, right);
                    AppendContourPolygon(biome, ref polygon);
                }
                else
                {
                    SetTriangle(ref polygon, p10, right, bottom);
                    AppendContourPolygon(biome, ref polygon);
                    SetTriangle(ref polygon, p01, left, top);
                    AppendContourPolygon(biome, ref polygon);
                }

                return;
            }

            polygon.Clear();
            if (inside00) polygon.Add(p00);
            if (inside00 != inside10) polygon.Add(bottom);
            if (inside10) polygon.Add(p10);
            if (inside10 != inside11) polygon.Add(right);
            if (inside11) polygon.Add(p11);
            if (inside11 != inside01) polygon.Add(top);
            if (inside01) polygon.Add(p01);
            if (inside01 != inside00) polygon.Add(left);
            AppendContourPolygon(biome, ref polygon);
        }

        private void AppendContourPolygon(
            TerrainBiome biome,
            ref FixedList128Bytes<Vector2> polygon)
        {
            AppendContourPolygonAtHeight(
                biome,
                ref polygon,
                GetBiomeSurfaceY(biome, GeneratedSurfaceYOffset, WaterSurfaceDepth));
        }

        private void AppendContourPolygonAtHeight(
            TerrainBiome biome,
            ref FixedList128Bytes<Vector2> polygon,
            float y)
        {
            if (polygon.Length < 3)
            {
                return;
            }

            int vertexStart = Vertices.Length;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 point = polygon[i];
                float vertexY = biome == TerrainBiome.Water
                    ? y
                    : y - GetOilPitDepth(ToWorld(point));
                Vertices.Add(new Vector3(point.x, vertexY, point.y));
                Uvs.Add(point);
                Colors.Add(
                    biome == TerrainBiome.Water
                        ? GetGeneratedWaterDepthColor(point)
                        : GetGeneratedSurfaceBlendWeights(point));
            }

            bool useAlternateQuadDiagonal = polygon.Length == 4
                                              && HasHeightVariation(vertexStart)
                                              && UseAlternateGeneratedSurfaceQuadDiagonal(
                                                  polygon[0],
                                                  polygon[1],
                                                  polygon[2],
                                                  polygon[3],
                                                  Resolution);
            if (useAlternateQuadDiagonal)
            {
                AddTriangle(biome, vertexStart, vertexStart + 3, vertexStart + 1);
                AddTriangle(biome, vertexStart + 1, vertexStart + 3, vertexStart + 2);
                return;
            }

            for (int i = 1; i < polygon.Length - 1; i++)
            {
                AddTriangle(biome, vertexStart, vertexStart + i + 1, vertexStart + i);
            }
        }

        private bool HasHeightVariation(int vertexStart)
        {
            float firstY = Vertices[vertexStart].y;
            return Mathf.Abs(Vertices[vertexStart + 1].y - firstY) > 0.0001f
                   || Mathf.Abs(Vertices[vertexStart + 2].y - firstY) > 0.0001f
                   || Mathf.Abs(Vertices[vertexStart + 3].y - firstY) > 0.0001f;
        }

        private void AppendWaterContourWalls(
            int mask,
            float centerScore,
            Vector2 bottom,
            Vector2 right,
            Vector2 top,
            Vector2 left)
        {
            float waterY = GetBiomeSurfaceY(TerrainBiome.Water, GeneratedSurfaceYOffset, WaterSurfaceDepth);
            float probeDistance = GetWaterWallProbeDistance(Resolution);
            int segmentCount = ResolveWaterContourWallSegments(
                mask,
                centerScore,
                bottom,
                right,
                top,
                left,
                out Vector2 firstStart,
                out Vector2 firstEnd,
                out Vector2 secondStart,
                out Vector2 secondEnd);
            if (segmentCount > 0)
            {
                AppendWaterContourWallSegment(waterY, probeDistance, firstStart, firstEnd);
            }

            if (segmentCount > 1)
            {
                AppendWaterContourWallSegment(waterY, probeDistance, secondStart, secondEnd);
            }
        }

        private void AppendWaterContourWallSegment(
            float waterY,
            float probeDistance,
            Vector2 start,
            Vector2 end)
        {
            Vector2 edge = end - start;
            if (edge.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Vector2 leftNormal = new Vector2(-edge.y, edge.x).normalized;
            Vector2 worldMidpoint = ToWorld((start + end) * 0.5f);
            if (!TryResolveWaterWallShoreline(
                    worldMidpoint,
                    leftNormal,
                    probeDistance,
                    out TerrainBiome landBiome,
                    out Vector2 waterNormal))
            {
                return;
            }

            float landY = GetBiomeSurfaceY(landBiome, GeneratedSurfaceYOffset, WaterSurfaceDepth);
            if (landY <= waterY + 0.0001f)
            {
                return;
            }

            Color startColor = GetGeneratedSurfaceBlendWeights(start);
            Color endColor = GetGeneratedSurfaceBlendWeights(end);
            if (WaterSurfaceDepth > 0f)
            {
                AppendWaterWallQuad(landBiome, start, end, waterY, landY, startColor, endColor);
            }

            if (GenerateWaterFoamOverlay != 0)
            {
                AppendWaterFoamQuad(
                    start,
                    end,
                    waterNormal,
                    waterY + WaterFoamSurfaceOffset,
                    WaterFoamWidth,
                    WaterFoamOverlayColor);
            }
        }

        private bool TryResolveWaterWallShoreline(
            Vector2 worldMidpoint,
            Vector2 leftNormal,
            float probeDistance,
            out TerrainBiome landBiome,
            out Vector2 waterNormal)
        {
            landBiome = TerrainBiome.Sand;
            waterNormal = leftNormal;
            float normalizedProbeDistance = Mathf.Max(0.05f, probeDistance);
            for (int i = 0; i < 4; i++)
            {
                float distance = Mathf.Min(0.6f, normalizedProbeDistance * (1 << i));
                TerrainBiome leftBiome = GetTileBiome(GetWaterWallSampleCoordinate(worldMidpoint + (leftNormal * distance)));
                TerrainBiome rightBiome = GetTileBiome(GetWaterWallSampleCoordinate(worldMidpoint - (leftNormal * distance)));
                if (TryResolveWaterWallShorelineFromBiomes(
                        leftBiome,
                        rightBiome,
                        leftNormal,
                        out landBiome,
                        out waterNormal))
                {
                    return true;
                }
            }

            return false;
        }

        private void AppendWaterWallQuad(
            TerrainBiome wallBiome,
            Vector2 start,
            Vector2 end,
            float bottomY,
            float topY,
            Color startColor,
            Color endColor)
        {
            int vertexStart = Vertices.Length;
            float effectiveBottomY = bottomY - GeneratedWaterWallVerticalOverlap;
            float effectiveTopY = topY + GeneratedWaterWallVerticalOverlap;
            float height = Mathf.Max(0.001f, effectiveTopY - effectiveBottomY);
            float length = Mathf.Max(0.001f, Vector2.Distance(start, end));
            for (int side = 0; side < 2; side++)
            {
                Vertices.Add(new Vector3(start.x, effectiveBottomY, start.y));
                Vertices.Add(new Vector3(end.x, effectiveBottomY, end.y));
                Vertices.Add(new Vector3(end.x, effectiveTopY, end.y));
                Vertices.Add(new Vector3(start.x, effectiveTopY, start.y));
                Uvs.Add(new Vector2(0f, 0f));
                Uvs.Add(new Vector2(length, 0f));
                Uvs.Add(new Vector2(length, height));
                Uvs.Add(new Vector2(0f, height));
                Colors.Add(startColor);
                Colors.Add(endColor);
                Colors.Add(endColor);
                Colors.Add(startColor);
            }

            AddTriangle(wallBiome, vertexStart, vertexStart + 2, vertexStart + 1);
            AddTriangle(wallBiome, vertexStart, vertexStart + 3, vertexStart + 2);
            AddTriangle(wallBiome, vertexStart + 4, vertexStart + 5, vertexStart + 6);
            AddTriangle(wallBiome, vertexStart + 4, vertexStart + 6, vertexStart + 7);
        }

        private void AppendWaterFoamQuad(
            Vector2 start,
            Vector2 end,
            Vector2 waterNormal,
            float y,
            float width,
            Color foamColor)
        {
            if (width <= 0f || foamColor.a <= 0f)
            {
                return;
            }

            Vector2 edge = end - start;
            float length = edge.magnitude;
            if (length <= 0.000001f)
            {
                return;
            }

            Vector2 waterDirection = waterNormal.sqrMagnitude > 0.000001f
                ? waterNormal.normalized
                : new Vector2(-edge.y, edge.x) / length;
            float foamWidth = Mathf.Max(0.001f, width);
            float shoreInset = Mathf.Min(foamWidth * 0.18f, 0.06f);
            float peakDistance = Mathf.Clamp(foamWidth * 0.42f, shoreInset + 0.001f, foamWidth);
            Vector2 startNear = start + (waterDirection * shoreInset);
            Vector2 endNear = end + (waterDirection * shoreInset);
            Vector2 startPeak = start + (waterDirection * peakDistance);
            Vector2 endPeak = end + (waterDirection * peakDistance);
            Vector2 startOuter = start + (waterDirection * foamWidth);
            Vector2 endOuter = end + (waterDirection * foamWidth);
            int vertexStart = Vertices.Length;
            Color nearColor = new Color(foamColor.r, foamColor.g, foamColor.b, 0.34f);
            Color peakColor = new Color(foamColor.r, foamColor.g, foamColor.b, 0.72f);
            Color transparent = new Color(foamColor.r, foamColor.g, foamColor.b, 0f);

            Vertices.Add(new Vector3(startNear.x, y, startNear.y));
            Vertices.Add(new Vector3(endNear.x, y, endNear.y));
            Vertices.Add(new Vector3(startPeak.x, y, startPeak.y));
            Vertices.Add(new Vector3(endPeak.x, y, endPeak.y));
            Vertices.Add(new Vector3(startOuter.x, y, startOuter.y));
            Vertices.Add(new Vector3(endOuter.x, y, endOuter.y));
            Uvs.Add(new Vector2(0f, 0f));
            Uvs.Add(new Vector2(length, 0f));
            Uvs.Add(new Vector2(0f, 0.42f));
            Uvs.Add(new Vector2(length, 0.42f));
            Uvs.Add(new Vector2(0f, 1f));
            Uvs.Add(new Vector2(length, 1f));
            Colors.Add(nearColor);
            Colors.Add(nearColor);
            Colors.Add(peakColor);
            Colors.Add(peakColor);
            Colors.Add(transparent);
            Colors.Add(transparent);
            FoamTriangles.Add(vertexStart);
            FoamTriangles.Add(vertexStart + 3);
            FoamTriangles.Add(vertexStart + 2);
            FoamTriangles.Add(vertexStart);
            FoamTriangles.Add(vertexStart + 1);
            FoamTriangles.Add(vertexStart + 3);
            FoamTriangles.Add(vertexStart + 2);
            FoamTriangles.Add(vertexStart + 3);
            FoamTriangles.Add(vertexStart + 5);
            FoamTriangles.Add(vertexStart + 2);
            FoamTriangles.Add(vertexStart + 5);
            FoamTriangles.Add(vertexStart + 4);
        }

        private void AppendContourSafetyPatches()
        {
            float patchRadius = 0.22f / Mathf.Max(1, Resolution);
            FixedList128Bytes<Vector2> polygon = default;
            for (int cellY = 0; cellY < CellCount; cellY++)
            {
                for (int cellX = 0; cellX < CellCount; cellX++)
                {
                    GetCellPoints(cellX, cellY, out Vector2 p00, out Vector2 p10, out Vector2 p11, out Vector2 p01);
                    Vector2 center = (p00 + p11) * 0.5f;
                    if (!IsSurfaceSampleInsideMapBounds(ToWorld(center)))
                    {
                        continue;
                    }

                    TerrainBiome centerBiome = GetDominantBiomeAtSample(ToWorld(center));
                    int biomeMask = 1 << (int)centerBiome;
                    biomeMask |= 1 << (int)GetDominantBiomeAtSample(ToWorld(p00));
                    biomeMask |= 1 << (int)GetDominantBiomeAtSample(ToWorld(p10));
                    biomeMask |= 1 << (int)GetDominantBiomeAtSample(ToWorld(p11));
                    biomeMask |= 1 << (int)GetDominantBiomeAtSample(ToWorld(p01));
                    int uniqueBiomeCount = 0;
                    for (int biomeIndex = 0; biomeIndex < GeneratedSurfaceBiomeMaterialCount; biomeIndex++)
                    {
                        if ((biomeMask & (1 << biomeIndex)) != 0)
                        {
                            uniqueBiomeCount++;
                        }
                    }

                    if (uniqueBiomeCount < 3)
                    {
                        continue;
                    }

                    SetQuad(
                        ref polygon,
                        new Vector2(center.x, center.y - patchRadius),
                        new Vector2(center.x + patchRadius, center.y),
                        new Vector2(center.x, center.y + patchRadius),
                        new Vector2(center.x - patchRadius, center.y));
                    AppendContourPolygon(centerBiome, ref polygon);
                }
            }
        }

        private TerrainBiome GetDominantBiomeAtSample(Vector2 sampleWorldPosition)
        {
            SurfaceBiomeWeights weights = SampleBiomeWeights(sampleWorldPosition);
            int dominantIndex = 0;
            float dominantWeight = float.MinValue;
            for (int i = 0; i < GeneratedSurfaceBiomeMaterialCount; i++)
            {
                float weight = weights.Get(i);
                if (weight > dominantWeight)
                {
                    dominantWeight = weight;
                    dominantIndex = i;
                }
            }

            return (TerrainBiome)dominantIndex;
        }

        private float GetBiomeScoreAtSample(Vector2 sampleWorldPosition, TerrainBiome biome)
        {
            SurfaceBiomeWeights weights = SampleBiomeWeights(sampleWorldPosition);
            int biomeIndex = GetBiomeMaterialIndex(biome);
            float maxOther = float.MinValue;
            for (int i = 0; i < GeneratedSurfaceBiomeMaterialCount; i++)
            {
                if (i == biomeIndex)
                {
                    continue;
                }

                maxOther = Mathf.Max(maxOther, weights.Get(i));
            }

            return weights.Get(biomeIndex) - maxOther;
        }

        private SurfaceBiomeWeights SampleBiomeWeights(Vector2 sampleWorldPosition)
        {
            SurfaceBiomeWeights weights = default;
            Vector2Int centerCoordinate = new Vector2Int(
                Mathf.RoundToInt(sampleWorldPosition.x),
                Mathf.RoundToInt(sampleWorldPosition.y));
            bool suppressWaterWeights = GetBlockedForWater(centerCoordinate);
            const int sampleRadius = 2;
            for (int offsetY = -sampleRadius; offsetY <= sampleRadius; offsetY++)
            {
                for (int offsetX = -sampleRadius; offsetX <= sampleRadius; offsetX++)
                {
                    Vector2Int tileCoordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                    TerrainBiome biome = GetTileBiome(tileCoordinate);
                    if (suppressWaterWeights && biome == TerrainBiome.Water)
                    {
                        continue;
                    }

                    Vector2 jitter = GetBiomeBlendJitter(tileCoordinate) * (0.35f + TerrainSurfaceVertexJitter);
                    Vector2 tileCenter = new Vector2(tileCoordinate.x, tileCoordinate.y) + jitter;
                    float distanceSqr = (sampleWorldPosition - tileCenter).sqrMagnitude;
                    weights.Add(biome, 1f / (0.12f + distanceSqr));
                }
            }

            return weights;
        }

        private Color GetGeneratedSurfaceBlendWeights(Vector2 localPoint)
        {
            SurfaceBiomeWeights weights = SampleBiomeWeights(ToWorld(localPoint));
            float totalWeight = weights.Sand + weights.Dirt + weights.Grass + weights.Forest;
            if (totalWeight <= 0.0001f)
            {
                if (weights.Sand >= weights.Dirt
                    && weights.Sand >= weights.Grass
                    && weights.Sand >= weights.Forest)
                {
                    return new Color(1f, 0f, 0f, 0f);
                }

                if (weights.Dirt >= weights.Grass && weights.Dirt >= weights.Forest)
                {
                    return new Color(0f, 1f, 0f, 0f);
                }

                return weights.Grass >= weights.Forest
                    ? new Color(0f, 0f, 1f, 0f)
                    : new Color(0f, 0f, 0f, 1f);
            }

            float inverseTotal = 1f / totalWeight;
            return new Color(
                weights.Sand * inverseTotal,
                weights.Dirt * inverseTotal,
                weights.Grass * inverseTotal,
                weights.Forest * inverseTotal);
        }

        private Color GetGeneratedWaterDepthColor(Vector2 localPoint)
        {
            float landDistance = GetNearestGeneratedLandDistance(ToWorld(localPoint));
            float depth = Mathf.InverseLerp(0f, GeneratedWaterDepthDeepDistance, landDistance);
            depth = Mathf.SmoothStep(0f, 1f, depth);
            return new Color(depth, depth, depth, 1f);
        }

        private float GetNearestGeneratedLandDistance(Vector2 worldPoint)
        {
            Vector2Int center = GetSurfaceSampleCoordinate(worldPoint);
            float nearestDistance = GeneratedWaterDepthDeepDistance;
            for (int offsetY = -GeneratedWaterDepthSearchRadius; offsetY <= GeneratedWaterDepthSearchRadius; offsetY++)
            {
                for (int offsetX = -GeneratedWaterDepthSearchRadius; offsetX <= GeneratedWaterDepthSearchRadius; offsetX++)
                {
                    Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                    if (GetTileBiome(coordinate) == TerrainBiome.Water
                        && !GetBlockedForWater(coordinate))
                    {
                        continue;
                    }

                    nearestDistance = Mathf.Min(nearestDistance, GetDistanceToTileRect(worldPoint, coordinate));
                }
            }

            return nearestDistance;
        }

        private float GetOilPitDepth(Vector2 worldPosition)
        {
            Vector2Int oilCoordinate = new Vector2Int(
                Mathf.RoundToInt(worldPosition.x),
                Mathf.RoundToInt(worldPosition.y));
            int localX = oilCoordinate.x - BiomeGridMinX;
            int localY = oilCoordinate.y - BiomeGridMinY;
            if (localX < 0
                || localY < 0
                || localX >= BiomeGridWidth
                || localY >= BiomeGridHeight
                || OilGrid[localX + (localY * BiomeGridWidth)] == 0)
            {
                return 0f;
            }

            Vector2 delta = worldPosition - new Vector2(oilCoordinate.x, oilCoordinate.y);
            float shapeRotation = GetGeneratedOilSurfaceRotationRadians(Seed, oilCoordinate);
            return EvaluateOilPitDepth(delta, shapeRotation);
        }

        private TerrainBiome GetTileBiome(Vector2Int worldCoordinate)
        {
            int localX = worldCoordinate.x - BiomeGridMinX;
            int localY = worldCoordinate.y - BiomeGridMinY;
            if (localX < 0 || localY < 0 || localX >= BiomeGridWidth || localY >= BiomeGridHeight)
            {
                return IsCoordinateInsideMapBounds(worldCoordinate)
                    ? TerrainBiome.Grass
                    : TerrainBiome.Water;
            }

            return BiomeGrid[localX + (localY * BiomeGridWidth)];
        }

        private bool GetBlockedForWater(Vector2Int worldCoordinate)
        {
            int localX = worldCoordinate.x - BiomeGridMinX;
            int localY = worldCoordinate.y - BiomeGridMinY;
            return localX >= 0
                   && localY >= 0
                   && localX < BiomeGridWidth
                   && localY < BiomeGridHeight
                   && BlockedWaterGrid[localX + (localY * BiomeGridWidth)] != 0;
        }

        private Vector2 GetBiomeBlendJitter(Vector2Int worldCoordinate)
        {
            float jitterX = Mathf.Lerp(
                -TerrainBlendJitter,
                TerrainBlendJitter,
                Hash01WithSeed(Seed, worldCoordinate.x, worldCoordinate.y, 8801));
            float jitterY = Mathf.Lerp(
                -TerrainBlendJitter,
                TerrainBlendJitter,
                Hash01WithSeed(Seed, worldCoordinate.x, worldCoordinate.y, 8819));
            return new Vector2(jitterX, jitterY);
        }

        private bool IsSurfaceSampleInsideMapBounds(Vector2 sampleWorldPosition)
        {
            return IsCoordinateInsideMapBounds(GetSurfaceSampleCoordinate(sampleWorldPosition));
        }

        private bool IsCoordinateInsideMapBounds(Vector2Int coordinate)
        {
            return coordinate.x >= MapMinX
                   && coordinate.y >= MapMinY
                   && coordinate.x < MapMaxExclusiveX
                   && coordinate.y < MapMaxExclusiveY;
        }

        private Vector2 ToWorld(Vector2 localPoint)
        {
            return new Vector2(Origin.x + localPoint.x, Origin.y + localPoint.y);
        }

        private void GetCellPoints(
            int cellX,
            int cellY,
            out Vector2 p00,
            out Vector2 p10,
            out Vector2 p11,
            out Vector2 p01)
        {
            float x0 = -0.5f + (cellX / (float)Resolution);
            float x1 = -0.5f + ((cellX + 1) / (float)Resolution);
            float y0 = -0.5f + (cellY / (float)Resolution);
            float y1 = -0.5f + ((cellY + 1) / (float)Resolution);
            p00 = new Vector2(x0, y0);
            p10 = new Vector2(x1, y0);
            p11 = new Vector2(x1, y1);
            p01 = new Vector2(x0, y1);
        }

        private void AddTriangle(TerrainBiome biome, int index0, int index1, int index2)
        {
            switch (GetGeneratedSurfaceTriangleBucket(biome))
            {
                case 0:
                    WaterTriangles.Add(index0);
                    WaterTriangles.Add(index1);
                    WaterTriangles.Add(index2);
                    break;
                case 5:
                    RockTriangles.Add(index0);
                    RockTriangles.Add(index1);
                    RockTriangles.Add(index2);
                    break;
                default:
                    BlendTriangles.Add(index0);
                    BlendTriangles.Add(index1);
                    BlendTriangles.Add(index2);
                    break;
            }
        }

        private void BuildNormals()
        {
            Normals.ResizeUninitialized(Vertices.Length);
            for (int i = 0; i < Normals.Length; i++)
            {
                Normals[i] = Vector3.zero;
            }

            AccumulateTriangleNormals(WaterTriangles);
            AccumulateTriangleNormals(BlendTriangles);
            AccumulateTriangleNormals(RockTriangles);
            for (int i = 0; i < Normals.Length; i++)
            {
                Vector3 normal = Normals[i];
                Normals[i] = normal.sqrMagnitude > 0.0000001f
                    ? normal.normalized
                    : Vector3.up;
            }
        }

        private void CalculateBounds()
        {
            if (Vertices.Length == 0)
            {
                SurfaceBoundsMinMax[0] = default;
                SurfaceBoundsMinMax[1] = default;
                return;
            }

            Vector3 minimum = Vertices[0];
            Vector3 maximum = Vertices[0];
            for (int i = 1; i < Vertices.Length; i++)
            {
                Vector3 vertex = Vertices[i];
                minimum = Vector3.Min(minimum, vertex);
                maximum = Vector3.Max(maximum, vertex);
            }

            SurfaceBoundsMinMax[0] = minimum;
            SurfaceBoundsMinMax[1] = maximum;
        }

        private void AccumulateTriangleNormals(NativeList<int> triangles)
        {
            for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
            {
                int index0 = triangles[triangleIndex];
                int index1 = triangles[triangleIndex + 1];
                int index2 = triangles[triangleIndex + 2];
                if ((uint)index0 >= (uint)Vertices.Length
                    || (uint)index1 >= (uint)Vertices.Length
                    || (uint)index2 >= (uint)Vertices.Length)
                {
                    continue;
                }

                Vector3 edge1 = Vertices[index1] - Vertices[index0];
                Vector3 edge2 = Vertices[index2] - Vertices[index0];
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);
                if (faceNormal.sqrMagnitude <= 0.0000001f)
                {
                    continue;
                }

                Normals[index0] = Normals[index0] + faceNormal;
                Normals[index1] = Normals[index1] + faceNormal;
                Normals[index2] = Normals[index2] + faceNormal;
            }
        }

        private static void SetTriangle(
            ref FixedList128Bytes<Vector2> polygon,
            Vector2 a,
            Vector2 b,
            Vector2 c)
        {
            polygon.Clear();
            polygon.Add(a);
            polygon.Add(b);
            polygon.Add(c);
        }

        private static void SetQuad(
            ref FixedList128Bytes<Vector2> polygon,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            polygon.Clear();
            polygon.Add(a);
            polygon.Add(b);
            polygon.Add(c);
            polygon.Add(d);
        }
    }
}
