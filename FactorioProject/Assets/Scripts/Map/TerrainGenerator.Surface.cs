using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class TerrainGenerator : MonoBehaviour
{
    private void ApplyChunkBiomeSurface(ChunkRuntimeData chunk, ChunkSurfaceBuildData chunkSurface)
    {
        using (ApplyChunkSurfaceMarker.Auto())
        {
            if (chunk == null || chunkSurface == null || chunkSurface.vertices.Count == 0)
            {
                return;
            }

            DestroyGeneratedSurfaceMesh(chunk.surfaceMesh);
            Mesh generatedMesh = BuildGeneratedSurfaceMesh(chunkSurface);
            generatedMesh.name = $"GeneratedSurface_{chunk.coordinate.x}_{chunk.coordinate.y}";
            chunk.surfaceMesh = generatedMesh;
            chunk.surfaceMatrix = Matrix4x4.Translate(
                new Vector3(chunk.origin.x, 0f, chunk.origin.y));
            chunk.surfaceWorldBounds = TransformMeshBounds(generatedMesh.bounds, chunk.surfaceMatrix);
            chunk.surfaceSubMeshMask = GetGeneratedSurfaceRenderSubMeshMask(chunkSurface);
        }
    }

    private void ReleaseChunkSurfaceMeshes(ChunkRuntimeData chunk)
    {
        if (chunk == null)
        {
            return;
        }

        DestroyGeneratedSurfaceMesh(chunk.surfaceMesh);
        chunk.surfaceMesh = null;
        chunk.surfaceSubMeshMask = 0;
    }

    private static void DestroyGeneratedSurfaceMesh(Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(mesh);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private void RenderLoadedChunkSurfaces(Camera renderCamera = null)
    {
        if (loadedChunks.Count == 0)
        {
            return;
        }

        Material[] surfaceMaterials = GetGeneratedSurfaceMaterials();
        Material foamMaterial = GetGeneratedSurfaceFoamMaterial();

        if (Application.isPlaying)
        {
            RenderVisibleChunkSurfaces(surfaceMaterials, foamMaterial, renderCamera);
            return;
        }

        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            RenderChunkSurface(pair.Value, surfaceMaterials, foamMaterial, renderCamera);
        }
    }

    private void RenderVisibleChunkSurfaces(
        Material[] surfaceMaterials,
        Material foamMaterial,
        Camera renderCamera)
    {
        GetPlayerRenderCoordinateBounds(out Vector2Int minCoordinate, out Vector2Int maxCoordinate);
        int normalizedChunkSize = Mathf.Max(4, chunkSize);

        // One padding chunk covers the half-cell mesh bounds and curved edge
        // vertices. The exact world-bounds test below removes any false positives.
        int minChunkX = Mathf.FloorToInt(minCoordinate.x / (float)normalizedChunkSize) - 1;
        int maxChunkX = Mathf.FloorToInt(maxCoordinate.x / (float)normalizedChunkSize) + 1;
        int minChunkY = Mathf.FloorToInt(minCoordinate.y / (float)normalizedChunkSize) - 1;
        int maxChunkY = Mathf.FloorToInt(maxCoordinate.y / (float)normalizedChunkSize) + 1;

        for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
        {
            for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                if (loadedChunks.TryGetValue(
                        new Vector2Int(chunkX, chunkY),
                        out ChunkRuntimeData chunk))
                {
                    RenderChunkSurface(chunk, surfaceMaterials, foamMaterial, renderCamera);
                }
            }
        }
    }

    private void RenderChunkSurface(
        ChunkRuntimeData chunk,
        Material[] surfaceMaterials,
        Material foamMaterial,
        Camera renderCamera)
    {
        if (chunk == null || chunk.surfaceMesh == null)
        {
            return;
        }

        if (Application.isPlaying
            && !DoesWorldBoundsIntersectPlayerRenderRange(chunk.surfaceWorldBounds))
        {
            return;
        }

        int subMeshCount = Mathf.Min(chunk.surfaceMesh.subMeshCount, surfaceMaterials.Length);
        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            Material material = surfaceMaterials[subMeshIndex];
            if (material == null || !HasGeneratedSurfaceRenderSubMesh(chunk, subMeshIndex))
            {
                continue;
            }

            RenderChunkMesh(
                chunk.surfaceMesh,
                subMeshIndex,
                material,
                chunk.surfaceMatrix,
                chunk.surfaceWorldBounds,
                true,
                renderCamera);
        }

        if (foamMaterial != null
            && HasGeneratedSurfaceRenderSubMesh(chunk, GeneratedSurfaceFoamRenderSubMeshIndex))
        {
            RenderChunkMesh(
                chunk.surfaceMesh,
                GeneratedSurfaceFoamRenderSubMeshIndex,
                foamMaterial,
                chunk.surfaceMatrix,
                chunk.surfaceWorldBounds,
                false,
                renderCamera);
        }
    }

    private int GetLoadedChunkSurfaceMeshCount()
    {
        int count = 0;
        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            ChunkRuntimeData chunk = pair.Value;
            if (chunk == null)
            {
                continue;
            }

            count += chunk.surfaceMesh != null ? 1 : 0;
        }

        return count;
    }

    private void RenderChunkMesh(
        Mesh mesh,
        int subMeshIndex,
        Material material,
        Matrix4x4 matrix,
        Bounds worldBounds,
        bool receiveShadows,
        Camera renderCamera)
    {
        RenderParams renderParams = new RenderParams(material)
        {
            layer = gameObject.layer,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = receiveShadows,
            motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
            lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off,
            reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off,
            camera = renderCamera,
            worldBounds = worldBounds
        };
        Graphics.RenderMesh(renderParams, mesh, subMeshIndex, matrix);
    }

#if UNITY_EDITOR
    private void RenderEditorChunkSurfaces(SceneView sceneView)
    {
        if (Application.isPlaying
            || sceneView == null
            || sceneView.camera == null
            || (Event.current != null && Event.current.type != EventType.Repaint))
        {
            return;
        }

        RenderLoadedChunkSurfaces(sceneView.camera);
    }
#endif

    private static int GetGeneratedSurfaceRenderSubMeshMask(ChunkSurfaceBuildData chunkSurface)
    {
        if (chunkSurface == null || chunkSurface.trianglesByBiome == null)
        {
            return 0;
        }

        int mask = 0;
        SetGeneratedSurfaceRenderSubMeshMaskBit(
            chunkSurface,
            GetBiomeMaterialIndex(TerrainBiome.Water),
            GeneratedSurfaceWaterRenderSubMeshIndex,
            ref mask);
        SetGeneratedSurfaceRenderSubMeshMaskBit(
            chunkSurface,
            GetGeneratedSurfaceTriangleBucket(TerrainBiome.Sand),
            GeneratedSurfaceBlendRenderSubMeshIndex,
            ref mask);
        SetGeneratedSurfaceRenderSubMeshMaskBit(
            chunkSurface,
            GetBiomeMaterialIndex(TerrainBiome.Rock),
            GeneratedSurfaceRockRenderSubMeshIndex,
            ref mask);
        SetGeneratedSurfaceRenderSubMeshMaskBit(
            chunkSurface,
            GeneratedSurfaceFoamMaterialIndex,
            GeneratedSurfaceFoamRenderSubMeshIndex,
            ref mask);
        return mask;
    }

    private static void SetGeneratedSurfaceRenderSubMeshMaskBit(
        ChunkSurfaceBuildData chunkSurface,
        int triangleBucketIndex,
        int renderSubMeshIndex,
        ref int mask)
    {
        if ((uint)triangleBucketIndex >= (uint)chunkSurface.trianglesByBiome.Length
            || chunkSurface.trianglesByBiome[triangleBucketIndex] == null
            || chunkSurface.trianglesByBiome[triangleBucketIndex].Count == 0)
        {
            return;
        }

        mask |= 1 << renderSubMeshIndex;
    }

    private static bool HasGeneratedSurfaceRenderSubMesh(ChunkRuntimeData chunk, int renderSubMeshIndex)
    {
        return chunk != null
               && renderSubMeshIndex >= 0
               && renderSubMeshIndex < GeneratedSurfaceRenderSubMeshCount
               && (chunk.surfaceSubMeshMask & (1 << renderSubMeshIndex)) != 0;
    }

    private static Bounds TransformMeshBounds(Bounds localBounds, Matrix4x4 matrix)
    {
        Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
        Vector3 extents = localBounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
        extents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(center, extents * 2f);
    }

    private Mesh BuildGeneratedSurfaceMesh(ChunkSurfaceBuildData chunkSurface)
    {
        Mesh mesh = new Mesh();
        mesh.indexFormat = chunkSurface.vertices.Count > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.SetVertices(chunkSurface.vertices);
        mesh.SetNormals(chunkSurface.normals);
        mesh.SetUVs(0, chunkSurface.uvs);
        if (chunkSurface.colors.Count == chunkSurface.vertices.Count)
        {
            mesh.SetColors(chunkSurface.colors);
        }
        mesh.subMeshCount = GeneratedSurfaceRenderSubMeshCount;
        mesh.SetTriangles(
            chunkSurface.trianglesByBiome[GetBiomeMaterialIndex(TerrainBiome.Water)],
            GeneratedSurfaceWaterRenderSubMeshIndex,
            false);
        mesh.SetTriangles(
            chunkSurface.trianglesByBiome[GetGeneratedSurfaceTriangleBucket(TerrainBiome.Sand)],
            GeneratedSurfaceBlendRenderSubMeshIndex,
            false);
        mesh.SetTriangles(
            chunkSurface.trianglesByBiome[GetBiomeMaterialIndex(TerrainBiome.Rock)],
            GeneratedSurfaceRockRenderSubMeshIndex,
            false);
        mesh.SetTriangles(
            chunkSurface.trianglesByBiome[GeneratedSurfaceFoamMaterialIndex],
            GeneratedSurfaceFoamRenderSubMeshIndex,
            false);

        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        return mesh;
    }

    private ChunkSurfaceBuildData BuildCurvedChunkSurface(Vector2Int origin, int chunkSizeInBlocks)
    {
        return BuildCurvedChunkSurfaceFromSnapshot(CreateChunkSurfaceWorkerInput(origin, chunkSizeInBlocks));
    }

    private Task<ChunkSurfaceBuildData> CreateChunkSurfaceBuildTask(Vector2Int origin, int chunkSizeInBlocks)
    {
        ChunkSurfaceWorkerInput input = CreateChunkSurfaceWorkerInput(origin, chunkSizeInBlocks);
        return Task.Run(() => BuildCurvedChunkSurfaceFromSnapshot(input));
    }

    private ChunkSurfaceWorkerInput CreateChunkSurfaceWorkerInput(Vector2Int origin, int chunkSizeInBlocks)
    {
        int resolution = GetChunkSurfaceResolution(origin, chunkSizeInBlocks);
        int margin = 4;
        int gridSize = chunkSizeInBlocks + (margin * 2) + 1;
        ChunkSurfaceWorkerInput input = new ChunkSurfaceWorkerInput
        {
            origin = origin,
            chunkSizeInBlocks = chunkSizeInBlocks,
            resolution = resolution,
            cellCount = Mathf.Max(1, chunkSizeInBlocks * resolution),
            biomeGridMinX = origin.x - margin,
            biomeGridMinY = origin.y - margin,
            biomeGridWidth = gridSize,
            biomeGridHeight = gridSize,
            mapMinX = GetMapMinCoordinate(),
            mapMinY = GetMapMinCoordinate(),
            mapMaxExclusiveX = GetMapMaxExclusiveCoordinate(),
            mapMaxExclusiveY = GetMapMaxExclusiveCoordinate(),
            biomeGrid = new TerrainBiome[gridSize * gridSize],
            blockedWaterGrid = new bool[gridSize * gridSize],
            oilGrid = new bool[gridSize * gridSize],
            generatedSurfaceYOffset = generatedSurfaceYOffset,
            waterSurfaceDepth = waterSurfaceDepth,
            generateWaterFoamOverlay = generateWaterFoamOverlay,
            waterFoamWidth = waterFoamWidth,
            waterFoamSurfaceOffset = waterFoamSurfaceOffset,
            waterFoamOverlayColor = waterFoamOverlayColor,
            terrainBlendJitter = terrainBlendJitter,
            terrainSurfaceVertexJitter = terrainSurfaceVertexJitter,
            seed = seed
        };

        for (int y = 0; y < gridSize; y++)
        {
            int worldY = input.biomeGridMinY + y;
            for (int x = 0; x < gridSize; x++)
            {
                int worldX = input.biomeGridMinX + x;
                int index = x + (y * gridSize);
                Vector2Int coordinate = new Vector2Int(worldX, worldY);
                input.biomeGrid[index] = GetTileBiome(coordinate);
                input.blockedWaterGrid[index] = IsBlockedForWater(coordinate);
                input.oilGrid[index] = IsCoordinateInsideMapBounds(coordinate)
                                       && IsGeneratedOilCoordinate(coordinate);
            }
        }

        return input;
    }

    private int GetChunkSurfaceResolution(Vector2Int origin, int chunkSizeInBlocks)
    {
        int resolution = Mathf.Max(2, terrainSurfaceSubdivisions);
        for (int localY = 0; localY < chunkSizeInBlocks; localY++)
        {
            for (int localX = 0; localX < chunkSizeInBlocks; localX++)
            {
                Vector2Int coordinate = new Vector2Int(origin.x + localX, origin.y + localY);
                if (IsCoordinateInsideMapBounds(coordinate) && IsGeneratedOilCoordinate(coordinate))
                {
                    return Mathf.Max(resolution, GeneratedOilChunkSurfaceSubdivisions);
                }
            }
        }

        return resolution;
    }

    private static ChunkSurfaceBuildData BuildCurvedChunkSurfaceFromSnapshot(ChunkSurfaceWorkerInput input)
    {
        ChunkSurfaceBuildData chunkSurface = new ChunkSurfaceBuildData(GeneratedSurfaceMaterialCount)
        {
            origin = input.origin,
            surfaceInput = input
        };

        AppendDominantBiomeBaseSurfaceFromSnapshot(chunkSurface, input);
        for (int biomeIndex = 0; biomeIndex < GeneratedSurfaceBiomeMaterialCount; biomeIndex++)
        {
            AppendBiomeContourSurfaceFromSnapshot(chunkSurface, (TerrainBiome)biomeIndex, input);
        }

        AppendContourSafetyPatchesFromSnapshot(chunkSurface, input);
        BuildChunkSurfaceNormals(chunkSurface);
        return chunkSurface;
    }

    private static void BuildChunkSurfaceNormals(ChunkSurfaceBuildData chunkSurface)
    {
        int vertexCount = chunkSurface != null ? chunkSurface.vertices.Count : 0;
        if (vertexCount <= 0)
        {
            return;
        }

        List<Vector3> normals = chunkSurface.normals;
        normals.Clear();
        if (normals.Capacity < vertexCount)
        {
            normals.Capacity = vertexCount;
        }

        for (int i = 0; i < vertexCount; i++)
        {
            normals.Add(Vector3.zero);
        }

        int surfaceSubMeshCount = Mathf.Min(
            GeneratedSurfaceBiomeMaterialCount,
            chunkSurface.trianglesByBiome.Length);
        for (int subMeshIndex = 0; subMeshIndex < surfaceSubMeshCount; subMeshIndex++)
        {
            List<int> triangles = chunkSurface.trianglesByBiome[subMeshIndex];
            for (int triangleIndex = 0; triangleIndex + 2 < triangles.Count; triangleIndex += 3)
            {
                int index0 = triangles[triangleIndex];
                int index1 = triangles[triangleIndex + 1];
                int index2 = triangles[triangleIndex + 2];
                if ((uint)index0 >= (uint)vertexCount
                    || (uint)index1 >= (uint)vertexCount
                    || (uint)index2 >= (uint)vertexCount)
                {
                    continue;
                }

                Vector3 edge1 = chunkSurface.vertices[index1] - chunkSurface.vertices[index0];
                Vector3 edge2 = chunkSurface.vertices[index2] - chunkSurface.vertices[index0];
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);
                if (faceNormal.sqrMagnitude <= 0.0000001f)
                {
                    continue;
                }

                normals[index0] += faceNormal;
                normals[index1] += faceNormal;
                normals[index2] += faceNormal;
            }
        }

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 normal = normals[i];
            normals[i] = normal.sqrMagnitude > 0.0000001f
                ? normal.normalized
                : Vector3.up;
        }
    }

    private static void AppendDominantBiomeBaseSurfaceFromSnapshot(ChunkSurfaceBuildData chunkSurface, ChunkSurfaceWorkerInput input)
    {
        float[] weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        List<Vector2> polygonScratch = new List<Vector2>(4);
        for (int cellY = 0; cellY < input.cellCount; cellY++)
        {
            for (int cellX = 0; cellX < input.cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 center = (p00 + p11) * 0.5f;
                Vector2 centerWorld = new Vector2(input.origin.x + center.x, input.origin.y + center.y);
                if (!IsSurfaceSampleInsideMapBounds(input, centerWorld))
                {
                    continue;
                }

                TerrainBiome dominantBiome = GetDominantBiomeAtSampleFromSnapshot(
                    input,
                    centerWorld,
                    weightBuffer);

                if (dominantBiome == TerrainBiome.Water)
                {
                    continue;
                }

                if (ShouldSkipDominantBaseSurfaceForWaterEdgeFromSnapshot(input, p00, p10, p11, p01, weightBuffer))
                {
                    continue;
                }

                AppendContourPolygonAtHeightFromSnapshot(
                    chunkSurface,
                    input,
                    dominantBiome,
                    SetContourQuad(polygonScratch, p00, p10, p11, p01),
                    GetBiomeBaseSurfaceY(dominantBiome, input.generatedSurfaceYOffset, input.waterSurfaceDepth));
            }
        }
    }

    private static bool ShouldSkipDominantBaseSurfaceForWaterEdgeFromSnapshot(
        ChunkSurfaceWorkerInput input,
        Vector2 p00,
        Vector2 p10,
        Vector2 p11,
        Vector2 p01,
        float[] weightBuffer)
    {
        if (input == null)
        {
            return false;
        }

        bool water00 = GetBiomeScoreAtSampleFromSnapshot(input, new Vector2(input.origin.x + p00.x, input.origin.y + p00.y), TerrainBiome.Water, weightBuffer) > 0f;
        bool water10 = GetBiomeScoreAtSampleFromSnapshot(input, new Vector2(input.origin.x + p10.x, input.origin.y + p10.y), TerrainBiome.Water, weightBuffer) > 0f;
        bool water11 = GetBiomeScoreAtSampleFromSnapshot(input, new Vector2(input.origin.x + p11.x, input.origin.y + p11.y), TerrainBiome.Water, weightBuffer) > 0f;
        bool water01 = GetBiomeScoreAtSampleFromSnapshot(input, new Vector2(input.origin.x + p01.x, input.origin.y + p01.y), TerrainBiome.Water, weightBuffer) > 0f;

        return water00 != water10
               || water10 != water11
               || water11 != water01;
    }

    private static void AppendBiomeContourSurfaceFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        TerrainBiome biome,
        ChunkSurfaceWorkerInput input)
    {
        float[] weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        float[,] scores = new float[input.cellCount + 1, input.cellCount + 1];
        List<Vector2> polygonScratch = new List<Vector2>(8);

        for (int sampleY = 0; sampleY <= input.cellCount; sampleY++)
        {
            for (int sampleX = 0; sampleX <= input.cellCount; sampleX++)
            {
                Vector2 sampleLocal = new Vector2(
                    -0.5f + (sampleX / (float)input.resolution),
                    -0.5f + (sampleY / (float)input.resolution));
                Vector2 sampleWorld = new Vector2(input.origin.x + sampleLocal.x, input.origin.y + sampleLocal.y);
                scores[sampleX, sampleY] = GetBiomeScoreAtSampleFromSnapshot(input, sampleWorld, biome, weightBuffer);
            }
        }

        for (int cellY = 0; cellY < input.cellCount; cellY++)
        {
            for (int cellX = 0; cellX < input.cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 centerWorld = new Vector2(input.origin.x + (p00.x + p11.x) * 0.5f, input.origin.y + (p00.y + p11.y) * 0.5f);
                if (!IsSurfaceSampleInsideMapBounds(input, centerWorld))
                {
                    continue;
                }

                float s00 = scores[cellX, cellY];
                float s10 = scores[cellX + 1, cellY];
                float s11 = scores[cellX + 1, cellY + 1];
                float s01 = scores[cellX, cellY + 1];
                float centerScore = GetBiomeScoreAtSampleFromSnapshot(
                    input,
                    centerWorld,
                    biome,
                    weightBuffer);

                AppendMarchingSquaresCellFromSnapshot(
                    chunkSurface,
                    input,
                    biome,
                    p00,
                    p10,
                    p11,
                    p01,
                    s00,
                    s10,
                    s11,
                    s01,
                    centerScore,
                    polygonScratch);
            }
        }
    }

    private static void AppendMarchingSquaresCellFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        ChunkSurfaceWorkerInput input,
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
        List<Vector2> polygon)
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
            AppendContourPolygonFromSnapshot(chunkSurface, input, biome, SetContourQuad(polygon, p00, p10, p11, p01));
            return;
        }

        Vector2 bottom = InterpolateContourPoint(p00, p10, s00, s10);
        Vector2 right = InterpolateContourPoint(p10, p11, s10, s11);
        Vector2 top = InterpolateContourPoint(p11, p01, s11, s01);
        Vector2 left = InterpolateContourPoint(p01, p00, s01, s00);
        if (biome == TerrainBiome.Water)
        {
            AppendWaterContourWallsFromSnapshot(
                chunkSurface,
                input,
                mask,
                centerScore,
                bottom,
                right,
                top,
                left);
        }

        if ((mask == 5 || mask == 10) && centerScore <= 0f)
        {
            if (mask == 5)
            {
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, SetContourTriangle(polygon, p00, bottom, left));
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, SetContourTriangle(polygon, p11, top, right));
            }
            else
            {
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, SetContourTriangle(polygon, p10, right, bottom));
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, SetContourTriangle(polygon, p01, left, top));
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

        AppendContourPolygonFromSnapshot(chunkSurface, input, biome, polygon);
    }

    private static List<Vector2> SetContourTriangle(List<Vector2> polygon, Vector2 a, Vector2 b, Vector2 c)
    {
        if (polygon == null)
        {
            polygon = new List<Vector2>(3);
        }

        polygon.Clear();
        polygon.Add(a);
        polygon.Add(b);
        polygon.Add(c);
        return polygon;
    }

    private static List<Vector2> SetContourQuad(List<Vector2> polygon, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (polygon == null)
        {
            polygon = new List<Vector2>(4);
        }

        polygon.Clear();
        polygon.Add(a);
        polygon.Add(b);
        polygon.Add(c);
        polygon.Add(d);
        return polygon;
    }

    private static void AppendContourPolygonFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        ChunkSurfaceWorkerInput input,
        TerrainBiome biome,
        List<Vector2> polygon)
    {
        AppendContourPolygonAtHeightFromSnapshot(
            chunkSurface,
            input,
            biome,
            polygon,
            GetBiomeSurfaceY(biome, input.generatedSurfaceYOffset, input.waterSurfaceDepth));
    }

    private static void AppendContourPolygonAtHeightFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        ChunkSurfaceWorkerInput input,
        TerrainBiome biome,
        List<Vector2> polygon,
        float y)
    {
        if (chunkSurface == null || polygon == null || polygon.Count < 3)
        {
            return;
        }

        int vertexStart = chunkSurface.vertices.Count;
        float[] weightBuffer = chunkSurface.blendWeightBuffer;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 point = polygon[i];
            float vertexY = biome == TerrainBiome.Water
                ? y
                : y - GetOilPitDepthFromSnapshot(
                    input,
                    new Vector2(input.origin.x + point.x, input.origin.y + point.y));
            chunkSurface.vertices.Add(new Vector3(point.x, vertexY, point.y));
            chunkSurface.uvs.Add(point);
            chunkSurface.colors.Add(
                biome == TerrainBiome.Water
                    ? GetGeneratedWaterDepthColorFromSnapshot(input, chunkSurface.origin, point)
                    : GetGeneratedSurfaceBlendWeightsFromSnapshot(input, chunkSurface.origin, point, weightBuffer));
        }

        List<int> targetTriangles = chunkSurface.trianglesByBiome[GetGeneratedSurfaceTriangleBucket(biome)];
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            targetTriangles.Add(vertexStart + 0);
            targetTriangles.Add(vertexStart + i + 1);
            targetTriangles.Add(vertexStart + i);
        }
    }

    private static void AppendWaterContourWallsFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        ChunkSurfaceWorkerInput input,
        int mask,
        float centerScore,
        Vector2 bottom,
        Vector2 right,
        Vector2 top,
        Vector2 left)
    {
        if (chunkSurface == null
            || input == null)
        {
            return;
        }

        float waterY = GetBiomeSurfaceY(TerrainBiome.Water, input.generatedSurfaceYOffset, input.waterSurfaceDepth);
        float probeDistance = GetWaterWallProbeDistance(input.resolution);

        void AppendSegment(Vector2 start, Vector2 end)
        {
            Vector2 edge = end - start;
            if (edge.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Vector2 leftNormal = new Vector2(-edge.y, edge.x).normalized;
            Vector2 midpoint = (start + end) * 0.5f;
            Vector2 worldMidpoint = new Vector2(input.origin.x + midpoint.x, input.origin.y + midpoint.y);

            if (!TryResolveWaterWallShorelineFromSnapshot(
                    input,
                    worldMidpoint,
                    leftNormal,
                    probeDistance,
                    out TerrainBiome landBiome,
                    out Vector2 waterNormal))
            {
                return;
            }

            float landY = GetBiomeSurfaceY(landBiome, input.generatedSurfaceYOffset, input.waterSurfaceDepth);
            if (landY <= waterY + 0.0001f)
            {
                return;
            }

            float[] weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
            Color startColor = GetGeneratedSurfaceBlendWeightsFromSnapshot(input, chunkSurface.origin, start, weightBuffer);
            Color endColor = GetGeneratedSurfaceBlendWeightsFromSnapshot(input, chunkSurface.origin, end, weightBuffer);
            if (input.waterSurfaceDepth > 0f)
            {
                AppendWaterWallQuad(chunkSurface, landBiome, start, end, waterY, landY, startColor, endColor);
            }

            if (input.generateWaterFoamOverlay)
            {
                AppendWaterFoamQuad(
                    chunkSurface,
                    start,
                    end,
                    waterNormal,
                    waterY + input.waterFoamSurfaceOffset,
                    input.waterFoamWidth,
                    input.waterFoamOverlayColor);
            }
        }

        AppendWaterContourWallSegments(mask, centerScore, bottom, right, top, left, AppendSegment);
    }

    private static void AppendContourSafetyPatchesFromSnapshot(ChunkSurfaceBuildData chunkSurface, ChunkSurfaceWorkerInput input)
    {
        float[] weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        float patchRadius = 0.22f / Mathf.Max(1, input.resolution);
        List<Vector2> polygonScratch = new List<Vector2>(4);

        for (int cellY = 0; cellY < input.cellCount; cellY++)
        {
            for (int cellX = 0; cellX < input.cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 center = (p00 + p11) * 0.5f;
                Vector2 centerWorld = new Vector2(input.origin.x + center.x, input.origin.y + center.y);
                if (!IsSurfaceSampleInsideMapBounds(input, centerWorld))
                {
                    continue;
                }

                TerrainBiome centerBiome = GetDominantBiomeAtSampleFromSnapshot(input, centerWorld, weightBuffer);
                TerrainBiome biome00 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p00.x, input.origin.y + p00.y), weightBuffer);
                TerrainBiome biome10 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p10.x, input.origin.y + p10.y), weightBuffer);
                TerrainBiome biome11 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p11.x, input.origin.y + p11.y), weightBuffer);
                TerrainBiome biome01 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p01.x, input.origin.y + p01.y), weightBuffer);

                int uniqueBiomeCount = CountUniqueBiomes(centerBiome, biome00, biome10, biome11, biome01);
                if (uniqueBiomeCount >= 3)
                {
                    AppendCenterSafetyPatchFromSnapshot(chunkSurface, input, centerBiome, center, patchRadius, polygonScratch);
                }
            }
        }
    }

    private static void AppendCenterSafetyPatchFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        ChunkSurfaceWorkerInput input,
        TerrainBiome biome,
        Vector2 center,
        float patchRadius,
        List<Vector2> polygon)
    {
        AppendContourPolygonFromSnapshot(
            chunkSurface,
            input,
            biome,
            SetContourQuad(
                polygon,
                new Vector2(center.x, center.y - patchRadius),
                new Vector2(center.x + patchRadius, center.y),
                new Vector2(center.x, center.y + patchRadius),
                new Vector2(center.x - patchRadius, center.y)));
    }

    private static TerrainBiome GetDominantBiomeAtSampleFromSnapshot(
        ChunkSurfaceWorkerInput input,
        Vector2 sampleWorldPosition,
        float[] weights)
    {
        SampleBiomeWeightsFromSnapshot(input, sampleWorldPosition, weights);
        int dominantIndex = 0;
        float dominantWeight = float.MinValue;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] > dominantWeight)
            {
                dominantWeight = weights[i];
                dominantIndex = i;
            }
        }

        return (TerrainBiome)dominantIndex;
    }

    private static float GetBiomeScoreAtSampleFromSnapshot(
        ChunkSurfaceWorkerInput input,
        Vector2 sampleWorldPosition,
        TerrainBiome biome,
        float[] weights)
    {
        SampleBiomeWeightsFromSnapshot(input, sampleWorldPosition, weights);
        int biomeIndex = GetBiomeMaterialIndex(biome);
        float maxOther = float.MinValue;
        for (int i = 0; i < weights.Length; i++)
        {
            if (i == biomeIndex)
            {
                continue;
            }

            if (weights[i] > maxOther)
            {
                maxOther = weights[i];
            }
        }

        return weights[biomeIndex] - maxOther;
    }

    private static void SampleBiomeWeightsFromSnapshot(
        ChunkSurfaceWorkerInput input,
        Vector2 sampleWorldPosition,
        float[] weights)
    {
        if (weights == null || weights.Length < 6)
        {
            return;
        }

        Array.Clear(weights, 0, weights.Length);
        Vector2Int centerCoordinate = new Vector2Int(Mathf.RoundToInt(sampleWorldPosition.x), Mathf.RoundToInt(sampleWorldPosition.y));
        bool suppressWaterWeights = GetBlockedForWaterFromSnapshot(input, centerCoordinate);
        const int sampleRadius = 2;
        for (int offsetY = -sampleRadius; offsetY <= sampleRadius; offsetY++)
        {
            for (int offsetX = -sampleRadius; offsetX <= sampleRadius; offsetX++)
            {
                Vector2Int tileCoordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                TerrainBiome biome = GetTileBiomeFromSnapshot(input, tileCoordinate);
                if (suppressWaterWeights && biome == TerrainBiome.Water)
                {
                    continue;
                }

                Vector2 jitter = GetBiomeBlendJitterFromSnapshot(input, tileCoordinate) * (0.35f + input.terrainSurfaceVertexJitter);
                Vector2 tileCenter = new Vector2(tileCoordinate.x, tileCoordinate.y) + jitter;
                float distanceSqr = (sampleWorldPosition - tileCenter).sqrMagnitude;
                float weight = 1f / (0.12f + distanceSqr);
                weights[GetBiomeMaterialIndex(biome)] += weight;
            }
        }
    }

    private static Color GetGeneratedSurfaceBlendWeightsFromSnapshot(
        ChunkSurfaceWorkerInput input,
        Vector2Int origin,
        Vector2 localPoint,
        float[] weights)
    {
        Vector2 worldPoint = new Vector2(origin.x + localPoint.x, origin.y + localPoint.y);
        SampleBiomeWeightsFromSnapshot(input, worldPoint, weights);

        float sandWeight = weights[GetBiomeMaterialIndex(TerrainBiome.Sand)];
        float dirtWeightValue = weights[GetBiomeMaterialIndex(TerrainBiome.Dirt)];
        float grassWeightValue = weights[GetBiomeMaterialIndex(TerrainBiome.Grass)];
        float forestWeightValue = weights[GetBiomeMaterialIndex(TerrainBiome.Forest)];
        float totalWeight = sandWeight + dirtWeightValue + grassWeightValue + forestWeightValue;

        if (totalWeight <= 0.0001f)
        {
            if (sandWeight >= dirtWeightValue && sandWeight >= grassWeightValue && sandWeight >= forestWeightValue)
            {
                return new Color(1f, 0f, 0f, 0f);
            }

            if (dirtWeightValue >= grassWeightValue && dirtWeightValue >= forestWeightValue)
            {
                return new Color(0f, 1f, 0f, 0f);
            }

            if (grassWeightValue >= forestWeightValue)
            {
                return new Color(0f, 0f, 1f, 0f);
            }

            return new Color(0f, 0f, 0f, 1f);
        }

        float inverseTotal = 1f / totalWeight;
        return new Color(
            sandWeight * inverseTotal,
            dirtWeightValue * inverseTotal,
            grassWeightValue * inverseTotal,
            forestWeightValue * inverseTotal);
    }

    private static Color GetGeneratedWaterDepthColorFromSnapshot(ChunkSurfaceWorkerInput input, Vector2Int origin, Vector2 localPoint)
    {
        Vector2 worldPoint = new Vector2(origin.x + localPoint.x, origin.y + localPoint.y);
        return EncodeGeneratedWaterDepthColor(GetNearestGeneratedLandDistanceFromSnapshot(input, worldPoint));
    }

    private static TerrainBiome GetTileBiomeFromSnapshot(ChunkSurfaceWorkerInput input, Vector2Int worldCoordinate)
    {
        int localX = worldCoordinate.x - input.biomeGridMinX;
        int localY = worldCoordinate.y - input.biomeGridMinY;
        if (localX < 0 || localY < 0 || localX >= input.biomeGridWidth || localY >= input.biomeGridHeight)
        {
            return IsCoordinateInsideMapBounds(input, worldCoordinate) ? TerrainBiome.Grass : TerrainBiome.Water;
        }

        return input.biomeGrid[localX + (localY * input.biomeGridWidth)];
    }

    private static bool IsSurfaceSampleInsideMapBounds(ChunkSurfaceWorkerInput input, Vector2 sampleWorldPosition)
    {
        return input != null && IsCoordinateInsideMapBounds(input, GetSurfaceSampleCoordinate(sampleWorldPosition));
    }

    private static bool IsCoordinateInsideMapBounds(ChunkSurfaceWorkerInput input, Vector2Int worldCoordinate)
    {
        return input != null
               && worldCoordinate.x >= input.mapMinX
               && worldCoordinate.y >= input.mapMinY
               && worldCoordinate.x < input.mapMaxExclusiveX
               && worldCoordinate.y < input.mapMaxExclusiveY;
    }

    private static Vector2Int GetSurfaceSampleCoordinate(Vector2 sampleWorldPosition)
    {
        return new Vector2Int(
            Mathf.FloorToInt(sampleWorldPosition.x + 0.5f),
            Mathf.FloorToInt(sampleWorldPosition.y + 0.5f));
    }

    private bool IsSurfaceSampleInsideMapBounds(Vector2 sampleWorldPosition)
    {
        return IsCoordinateInsideMapBounds(GetSurfaceSampleCoordinate(sampleWorldPosition));
    }

    private static bool GetBlockedForWaterFromSnapshot(ChunkSurfaceWorkerInput input, Vector2Int worldCoordinate)
    {
        int localX = worldCoordinate.x - input.biomeGridMinX;
        int localY = worldCoordinate.y - input.biomeGridMinY;
        if (localX < 0 || localY < 0 || localX >= input.biomeGridWidth || localY >= input.biomeGridHeight)
        {
            return false;
        }

        return input.blockedWaterGrid[localX + (localY * input.biomeGridWidth)];
    }

    private static float GetOilPitDepthFromSnapshot(ChunkSurfaceWorkerInput input, Vector2 worldPosition)
    {
        if (input == null || input.oilGrid == null)
        {
            return 0f;
        }

        Vector2Int oilCoordinate = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.y));
        int localX = oilCoordinate.x - input.biomeGridMinX;
        int localY = oilCoordinate.y - input.biomeGridMinY;
        if (localX < 0
            || localY < 0
            || localX >= input.biomeGridWidth
            || localY >= input.biomeGridHeight
            || !input.oilGrid[localX + (localY * input.biomeGridWidth)])
        {
            return 0f;
        }

        Vector2 delta = worldPosition - new Vector2(oilCoordinate.x, oilCoordinate.y);
        float angle = Mathf.Atan2(delta.y, delta.x);
        float phase = Hash01WithSeed(input.seed, oilCoordinate.x, oilCoordinate.y, 9127) * Mathf.PI * 2f;
        float outlineNoise =
            (Mathf.Sin((angle * 3f) + phase) * 0.55f)
            + (Mathf.Sin((angle * 5f) - (phase * 0.7f)) * 0.3f)
            + (Mathf.Sin((angle * 7f) + (phase * 1.3f)) * 0.15f);
        float outerRadius = Mathf.Min(
            0.495f,
            GeneratedOilPitOuterRadius + (outlineNoise * GeneratedOilPitOutlineJitter));
        float distance = delta.magnitude;
        if (distance >= outerRadius)
        {
            return 0f;
        }

        float innerRadius = GeneratedOilPitInnerRadius + (outlineNoise * 0.008f);
        if (distance <= innerRadius)
        {
            return GeneratedOilPitDepth;
        }

        float slope = Mathf.InverseLerp(outerRadius, innerRadius, distance);
        slope = slope * slope * (3f - (2f * slope));
        return GeneratedOilPitDepth * slope;
    }

    private static Vector2 GetBiomeBlendJitterFromSnapshot(ChunkSurfaceWorkerInput input, Vector2Int worldCoordinate)
    {
        float jitterX = Mathf.Lerp(
            -input.terrainBlendJitter,
            input.terrainBlendJitter,
            Hash01WithSeed(input.seed, worldCoordinate.x, worldCoordinate.y, 8801));
        float jitterY = Mathf.Lerp(
            -input.terrainBlendJitter,
            input.terrainBlendJitter,
            Hash01WithSeed(input.seed, worldCoordinate.x, worldCoordinate.y, 8819));
        return new Vector2(jitterX, jitterY);
    }

    private static float Hash01WithSeed(int seedValue, int x, int y, int salt)
    {
        unchecked
        {
            uint hash = (uint)seedValue;
            hash = (hash * 397u) ^ (uint)x;
            hash = (hash * 397u) ^ (uint)y;
            hash = (hash * 397u) ^ (uint)salt;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 326648991u;
            hash ^= hash >> 16;
            return hash / (float)uint.MaxValue;
        }
    }

    private IEnumerator BuildCurvedChunkSurfaceRoutine(
        ChunkSurfaceBuildData chunkSurface,
        Vector2Int origin,
        int chunkSizeInBlocks,
        bool allowYield)
    {
        int resolution = chunkSurface != null && chunkSurface.surfaceInput != null
            ? chunkSurface.surfaceInput.resolution
            : Mathf.Max(2, terrainSurfaceSubdivisions);
        int cellCount = Mathf.Max(1, chunkSizeInBlocks * resolution);
        IEnumerator baseRoutine = AppendDominantBiomeBaseSurfaceRoutine(chunkSurface, origin, cellCount, resolution, allowYield);
        while (baseRoutine.MoveNext())
        {
            if (allowYield && baseRoutine.Current != null)
            {
                yield return baseRoutine.Current;
            }
        }

        for (int biomeIndex = 0; biomeIndex < GeneratedSurfaceBiomeMaterialCount; biomeIndex++)
        {
            TerrainBiome biome = (TerrainBiome)biomeIndex;
            IEnumerator biomeRoutine = AppendBiomeContourSurfaceRoutine(chunkSurface, biome, origin, cellCount, resolution, allowYield);
            while (biomeRoutine.MoveNext())
            {
                if (allowYield && biomeRoutine.Current != null)
                {
                    yield return biomeRoutine.Current;
                }
            }
        }

        IEnumerator safetyRoutine = AppendContourSafetyPatchesRoutine(chunkSurface, origin, cellCount, resolution, allowYield);
        while (safetyRoutine.MoveNext())
        {
            if (allowYield && safetyRoutine.Current != null)
            {
                yield return safetyRoutine.Current;
            }
        }

        BuildChunkSurfaceNormals(chunkSurface);
    }

    private IEnumerator AppendDominantBiomeBaseSurfaceRoutine(
        ChunkSurfaceBuildData chunkSurface,
        Vector2Int origin,
        int cellCount,
        int resolution,
        bool allowYield)
    {
        if (chunkSurface == null)
        {
            yield break;
        }

        float[] weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        List<Vector2> polygonScratch = new List<Vector2>(4);
        int surfaceRowBudget = Mathf.Max(1, chunkSurfaceRowsPerFrame);
        int rowsSinceYield = 0;

        for (int cellY = 0; cellY < cellCount; cellY++)
        {
            for (int cellX = 0; cellX < cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 center = (p00 + p11) * 0.5f;
                Vector2 centerWorld = new Vector2(origin.x + center.x, origin.y + center.y);
                if (!IsSurfaceSampleInsideMapBounds(centerWorld))
                {
                    continue;
                }

                TerrainBiome dominantBiome = GetDominantBiomeAtSample(
                    centerWorld,
                    weightBuffer);

                if (dominantBiome == TerrainBiome.Water)
                {
                    continue;
                }

                if (ShouldSkipDominantBaseSurfaceForWaterEdge(origin, p00, p10, p11, p01, weightBuffer))
                {
                    continue;
                }

                AppendContourPolygonAtHeight(
                    chunkSurface,
                    dominantBiome,
                    SetContourQuad(polygonScratch, p00, p10, p11, p01),
                    GetBiomeBaseSurfaceY(dominantBiome));
            }

            if (allowYield && ++rowsSinceYield >= surfaceRowBudget)
            {
                rowsSinceYield = 0;
                yield return null;
            }
        }
    }

    private bool ShouldSkipDominantBaseSurfaceForWaterEdge(
        Vector2Int origin,
        Vector2 p00,
        Vector2 p10,
        Vector2 p11,
        Vector2 p01,
        float[] weightBuffer)
    {
        bool water00 = GetBiomeScoreAtSample(new Vector2(origin.x + p00.x, origin.y + p00.y), TerrainBiome.Water, weightBuffer) > 0f;
        bool water10 = GetBiomeScoreAtSample(new Vector2(origin.x + p10.x, origin.y + p10.y), TerrainBiome.Water, weightBuffer) > 0f;
        bool water11 = GetBiomeScoreAtSample(new Vector2(origin.x + p11.x, origin.y + p11.y), TerrainBiome.Water, weightBuffer) > 0f;
        bool water01 = GetBiomeScoreAtSample(new Vector2(origin.x + p01.x, origin.y + p01.y), TerrainBiome.Water, weightBuffer) > 0f;

        return water00 != water10
               || water10 != water11
               || water11 != water01;
    }

    private IEnumerator AppendBiomeContourSurfaceRoutine(
        ChunkSurfaceBuildData chunkSurface,
        TerrainBiome biome,
        Vector2Int origin,
        int cellCount,
        int resolution,
        bool allowYield)
    {
        if (chunkSurface == null)
        {
            yield break;
        }

        float[] weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        float[,] scores = new float[cellCount + 1, cellCount + 1];
        List<Vector2> polygonScratch = new List<Vector2>(8);
        int surfaceRowBudget = Mathf.Max(1, chunkSurfaceRowsPerFrame);
        int rowsSinceYield = 0;

        for (int sampleY = 0; sampleY <= cellCount; sampleY++)
        {
            for (int sampleX = 0; sampleX <= cellCount; sampleX++)
            {
                Vector2 sampleLocal = new Vector2(
                    -0.5f + (sampleX / (float)resolution),
                    -0.5f + (sampleY / (float)resolution));
                Vector2 sampleWorld = new Vector2(origin.x + sampleLocal.x, origin.y + sampleLocal.y);
                scores[sampleX, sampleY] = GetBiomeScoreAtSample(sampleWorld, biome, weightBuffer);
            }

            if (allowYield && ++rowsSinceYield >= surfaceRowBudget)
            {
                rowsSinceYield = 0;
                yield return null;
            }
        }

        rowsSinceYield = 0;
        for (int cellY = 0; cellY < cellCount; cellY++)
        {
            for (int cellX = 0; cellX < cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 centerWorld = new Vector2(origin.x + (p00.x + p11.x) * 0.5f, origin.y + (p00.y + p11.y) * 0.5f);
                if (!IsSurfaceSampleInsideMapBounds(centerWorld))
                {
                    continue;
                }

                float s00 = scores[cellX, cellY];
                float s10 = scores[cellX + 1, cellY];
                float s11 = scores[cellX + 1, cellY + 1];
                float s01 = scores[cellX, cellY + 1];
                float centerScore = GetBiomeScoreAtSample(
                    centerWorld,
                    biome,
                    weightBuffer);

                AppendMarchingSquaresCell(
                    chunkSurface,
                    biome,
                    p00,
                    p10,
                    p11,
                    p01,
                    s00,
                    s10,
                    s11,
                    s01,
                    centerScore,
                    polygonScratch);
            }

            if (allowYield && ++rowsSinceYield >= surfaceRowBudget)
            {
                rowsSinceYield = 0;
                yield return null;
            }
        }
    }

    private void AppendMarchingSquaresCell(
        ChunkSurfaceBuildData chunkSurface,
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
        List<Vector2> polygon)
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
            AppendContourPolygon(chunkSurface, biome, SetContourQuad(polygon, p00, p10, p11, p01));
            return;
        }

        Vector2 bottom = InterpolateContourPoint(p00, p10, s00, s10);
        Vector2 right = InterpolateContourPoint(p10, p11, s10, s11);
        Vector2 top = InterpolateContourPoint(p11, p01, s11, s01);
        Vector2 left = InterpolateContourPoint(p01, p00, s01, s00);
        if (biome == TerrainBiome.Water)
        {
            AppendWaterContourWalls(
                chunkSurface,
                mask,
                centerScore,
                bottom,
                right,
                top,
                left);
        }

        if ((mask == 5 || mask == 10) && centerScore <= 0f)
        {
            if (mask == 5)
            {
                AppendContourPolygon(chunkSurface, biome, SetContourTriangle(polygon, p00, bottom, left));
                AppendContourPolygon(chunkSurface, biome, SetContourTriangle(polygon, p11, top, right));
            }
            else
            {
                AppendContourPolygon(chunkSurface, biome, SetContourTriangle(polygon, p10, right, bottom));
                AppendContourPolygon(chunkSurface, biome, SetContourTriangle(polygon, p01, left, top));
            }

            return;
        }

        polygon.Clear();
        if (inside00)
        {
            polygon.Add(p00);
        }

        if (inside00 != inside10)
        {
            polygon.Add(bottom);
        }

        if (inside10)
        {
            polygon.Add(p10);
        }

        if (inside10 != inside11)
        {
            polygon.Add(right);
        }

        if (inside11)
        {
            polygon.Add(p11);
        }

        if (inside11 != inside01)
        {
            polygon.Add(top);
        }

        if (inside01)
        {
            polygon.Add(p01);
        }

        if (inside01 != inside00)
        {
            polygon.Add(left);
        }

        AppendContourPolygon(chunkSurface, biome, polygon);
    }

    private void AppendContourPolygon(ChunkSurfaceBuildData chunkSurface, TerrainBiome biome, List<Vector2> polygon)
    {
        AppendContourPolygonAtHeight(
            chunkSurface,
            biome,
            polygon,
            GetBiomeSurfaceY(biome));
    }

    private void AppendContourPolygonAtHeight(
        ChunkSurfaceBuildData chunkSurface,
        TerrainBiome biome,
        List<Vector2> polygon,
        float y)
    {
        if (chunkSurface == null || polygon == null || polygon.Count < 3)
        {
            return;
        }

        int vertexStart = chunkSurface.vertices.Count;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 point = polygon[i];
            float vertexY = biome == TerrainBiome.Water || chunkSurface.surfaceInput == null
                ? y
                : y - GetOilPitDepthFromSnapshot(
                    chunkSurface.surfaceInput,
                    new Vector2(chunkSurface.origin.x + point.x, chunkSurface.origin.y + point.y));
            chunkSurface.vertices.Add(new Vector3(point.x, vertexY, point.y));
            chunkSurface.uvs.Add(point);
            chunkSurface.colors.Add(
                biome == TerrainBiome.Water
                    ? GetGeneratedWaterDepthColor(chunkSurface.origin, point)
                    : GetGeneratedSurfaceBlendWeights(chunkSurface.origin, point, chunkSurface.blendWeightBuffer));
        }

        List<int> targetTriangles = chunkSurface.trianglesByBiome[GetGeneratedSurfaceTriangleBucket(biome)];
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            targetTriangles.Add(vertexStart + 0);
            targetTriangles.Add(vertexStart + i + 1);
            targetTriangles.Add(vertexStart + i);
        }
    }

    private void AppendWaterContourWalls(
        ChunkSurfaceBuildData chunkSurface,
        int mask,
        float centerScore,
        Vector2 bottom,
        Vector2 right,
        Vector2 top,
        Vector2 left)
    {
        if (chunkSurface == null
            || (!generateWaterFoamOverlay && waterSurfaceDepth <= 0f))
        {
            return;
        }

        int resolution = chunkSurface.surfaceInput != null
            ? chunkSurface.surfaceInput.resolution
            : Mathf.Max(2, terrainSurfaceSubdivisions);
        float waterY = GetBiomeSurfaceY(TerrainBiome.Water);
        float probeDistance = GetWaterWallProbeDistance(resolution);

        void AppendSegment(Vector2 start, Vector2 end)
        {
            Vector2 edge = end - start;
            if (edge.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Vector2 leftNormal = new Vector2(-edge.y, edge.x).normalized;
            Vector2 midpoint = (start + end) * 0.5f;
            Vector2 worldMidpoint = new Vector2(chunkSurface.origin.x + midpoint.x, chunkSurface.origin.y + midpoint.y);

            if (!TryResolveWaterWallShoreline(
                    worldMidpoint,
                    leftNormal,
                    probeDistance,
                    out TerrainBiome landBiome,
                    out Vector2 waterNormal))
            {
                return;
            }

            float landY = GetBiomeSurfaceY(landBiome);
            if (landY <= waterY + 0.0001f)
            {
                return;
            }

            float[] weightBuffer = chunkSurface.blendWeightBuffer;
            Color startColor = GetGeneratedSurfaceBlendWeights(chunkSurface.origin, start, weightBuffer);
            Color endColor = GetGeneratedSurfaceBlendWeights(chunkSurface.origin, end, weightBuffer);
            if (waterSurfaceDepth > 0f)
            {
                AppendWaterWallQuad(chunkSurface, landBiome, start, end, waterY, landY, startColor, endColor);
            }

            if (generateWaterFoamOverlay)
            {
                AppendWaterFoamQuad(
                    chunkSurface,
                    start,
                    end,
                    waterNormal,
                    waterY + waterFoamSurfaceOffset,
                    waterFoamWidth,
                    waterFoamOverlayColor);
            }
        }

        AppendWaterContourWallSegments(mask, centerScore, bottom, right, top, left, AppendSegment);
    }

    private IEnumerator AppendContourSafetyPatchesRoutine(
        ChunkSurfaceBuildData chunkSurface,
        Vector2Int origin,
        int cellCount,
        int resolution,
        bool allowYield)
    {
        if (chunkSurface == null)
        {
            yield break;
        }

        float[] weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        int surfaceRowBudget = Mathf.Max(1, chunkSurfaceRowsPerFrame);
        int rowsSinceYield = 0;
        float patchRadius = 0.22f / Mathf.Max(1, resolution);
        List<Vector2> polygonScratch = new List<Vector2>(4);

        for (int cellY = 0; cellY < cellCount; cellY++)
        {
            for (int cellX = 0; cellX < cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 center = (p00 + p11) * 0.5f;
                Vector2 centerWorld = new Vector2(origin.x + center.x, origin.y + center.y);
                if (!IsSurfaceSampleInsideMapBounds(centerWorld))
                {
                    continue;
                }

                TerrainBiome centerBiome = GetDominantBiomeAtSample(
                    centerWorld,
                    weightBuffer);
                TerrainBiome biome00 = GetDominantBiomeAtSample(
                    new Vector2(origin.x + p00.x, origin.y + p00.y),
                    weightBuffer);
                TerrainBiome biome10 = GetDominantBiomeAtSample(
                    new Vector2(origin.x + p10.x, origin.y + p10.y),
                    weightBuffer);
                TerrainBiome biome11 = GetDominantBiomeAtSample(
                    new Vector2(origin.x + p11.x, origin.y + p11.y),
                    weightBuffer);
                TerrainBiome biome01 = GetDominantBiomeAtSample(
                    new Vector2(origin.x + p01.x, origin.y + p01.y),
                    weightBuffer);

                int uniqueBiomeCount = CountUniqueBiomes(centerBiome, biome00, biome10, biome11, biome01);
                if (uniqueBiomeCount >= 3)
                {
                    AppendCenterSafetyPatch(chunkSurface, centerBiome, center, patchRadius, polygonScratch);
                }
            }

            if (allowYield && ++rowsSinceYield >= surfaceRowBudget)
            {
                rowsSinceYield = 0;
                yield return null;
            }
        }
    }

    private void AppendCenterSafetyPatch(
        ChunkSurfaceBuildData chunkSurface,
        TerrainBiome biome,
        Vector2 center,
        float patchRadius,
        List<Vector2> polygon)
    {
        AppendContourPolygon(
            chunkSurface,
            biome,
            SetContourQuad(
                polygon,
                new Vector2(center.x, center.y - patchRadius),
                new Vector2(center.x + patchRadius, center.y),
                new Vector2(center.x, center.y + patchRadius),
                new Vector2(center.x - patchRadius, center.y)));
    }

    private TerrainBiome GetDominantBiomeAtSample(Vector2 sampleWorldPosition, float[] weights)
    {
        SampleBiomeWeights(sampleWorldPosition, weights);
        int dominantIndex = 0;
        float dominantWeight = float.MinValue;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] > dominantWeight)
            {
                dominantWeight = weights[i];
                dominantIndex = i;
            }
        }

        return (TerrainBiome)dominantIndex;
    }

    private static int CountUniqueBiomes(
        TerrainBiome biomeA,
        TerrainBiome biomeB,
        TerrainBiome biomeC,
        TerrainBiome biomeD,
        TerrainBiome biomeE)
    {
        bool hasWater = false;
        bool hasSand = false;
        bool hasDirt = false;
        bool hasGrass = false;
        bool hasForest = false;
        bool hasRock = false;

        MarkBiome(biomeA, ref hasWater, ref hasSand, ref hasDirt, ref hasGrass, ref hasForest, ref hasRock);
        MarkBiome(biomeB, ref hasWater, ref hasSand, ref hasDirt, ref hasGrass, ref hasForest, ref hasRock);
        MarkBiome(biomeC, ref hasWater, ref hasSand, ref hasDirt, ref hasGrass, ref hasForest, ref hasRock);
        MarkBiome(biomeD, ref hasWater, ref hasSand, ref hasDirt, ref hasGrass, ref hasForest, ref hasRock);
        MarkBiome(biomeE, ref hasWater, ref hasSand, ref hasDirt, ref hasGrass, ref hasForest, ref hasRock);

        int count = 0;
        if (hasWater) count++;
        if (hasSand) count++;
        if (hasDirt) count++;
        if (hasGrass) count++;
        if (hasForest) count++;
        if (hasRock) count++;
        return count;
    }

    private static void MarkBiome(
        TerrainBiome biome,
        ref bool hasWater,
        ref bool hasSand,
        ref bool hasDirt,
        ref bool hasGrass,
        ref bool hasForest,
        ref bool hasRock)
    {
        switch (biome)
        {
            case TerrainBiome.Water:
                hasWater = true;
                break;
            case TerrainBiome.Sand:
                hasSand = true;
                break;
            case TerrainBiome.Dirt:
                hasDirt = true;
                break;
            case TerrainBiome.Grass:
                hasGrass = true;
                break;
            case TerrainBiome.Forest:
                hasForest = true;
                break;
            case TerrainBiome.Rock:
                hasRock = true;
                break;
        }
    }

    private float GetBiomeScoreAtSample(Vector2 sampleWorldPosition, TerrainBiome biome, float[] weights)
    {
        SampleBiomeWeights(sampleWorldPosition, weights);
        int biomeIndex = GetBiomeMaterialIndex(biome);
        float maxOther = float.MinValue;
        for (int i = 0; i < weights.Length; i++)
        {
            if (i == biomeIndex)
            {
                continue;
            }

            if (weights[i] > maxOther)
            {
                maxOther = weights[i];
            }
        }

        return weights[biomeIndex] - maxOther;
    }

    private void SampleBiomeWeights(Vector2 sampleWorldPosition, float[] weights)
    {
        if (weights == null || weights.Length < 6)
        {
            return;
        }

        Array.Clear(weights, 0, weights.Length);
        Vector2Int centerCoordinate = new Vector2Int(Mathf.RoundToInt(sampleWorldPosition.x), Mathf.RoundToInt(sampleWorldPosition.y));
        bool suppressWaterWeights = IsBlockedForWater(centerCoordinate);
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

                Vector2 jitter = GetBiomeBlendJitter(tileCoordinate) * (0.35f + terrainSurfaceVertexJitter);
                Vector2 tileCenter = new Vector2(tileCoordinate.x, tileCoordinate.y) + jitter;
                float distanceSqr = (sampleWorldPosition - tileCenter).sqrMagnitude;
                float weight = 1f / (0.12f + distanceSqr);
                weights[GetBiomeMaterialIndex(biome)] += weight;
            }
        }
    }

    private static Vector2 InterpolateContourPoint(Vector2 start, Vector2 end, float startValue, float endValue)
    {
        float delta = startValue - endValue;
        if (Mathf.Abs(delta) <= 0.0001f)
        {
            return (start + end) * 0.5f;
        }

        float t = Mathf.Clamp01(startValue / delta);
        return Vector2.Lerp(start, end, t);
    }

    private static float GetWaterWallProbeDistance(int resolution)
    {
        return 0.35f / Mathf.Max(1, resolution);
    }

    private static void AppendWaterContourWallSegments(
        int mask,
        float centerScore,
        Vector2 bottom,
        Vector2 right,
        Vector2 top,
        Vector2 left,
        Action<Vector2, Vector2> appendSegment)
    {
        if (appendSegment == null)
        {
            return;
        }

        switch (mask)
        {
            case 1:
                appendSegment(bottom, left);
                break;
            case 2:
                appendSegment(right, bottom);
                break;
            case 3:
                appendSegment(right, left);
                break;
            case 4:
                appendSegment(top, right);
                break;
            case 5:
                if (centerScore <= 0f)
                {
                    appendSegment(bottom, left);
                    appendSegment(top, right);
                }
                else
                {
                    appendSegment(bottom, right);
                    appendSegment(top, left);
                }
                break;
            case 6:
                appendSegment(top, bottom);
                break;
            case 7:
                appendSegment(top, left);
                break;
            case 8:
                appendSegment(left, top);
                break;
            case 9:
                appendSegment(bottom, top);
                break;
            case 10:
                if (centerScore <= 0f)
                {
                    appendSegment(right, bottom);
                    appendSegment(left, top);
                }
                else
                {
                    appendSegment(right, top);
                    appendSegment(left, bottom);
                }
                break;
            case 11:
                appendSegment(right, top);
                break;
            case 12:
                appendSegment(left, right);
                break;
            case 13:
                appendSegment(bottom, right);
                break;
            case 14:
                appendSegment(left, bottom);
                break;
        }
    }

    private static bool TryResolveWaterWallShorelineFromSnapshot(
        ChunkSurfaceWorkerInput input,
        Vector2 worldMidpoint,
        Vector2 leftNormal,
        float probeDistance,
        out TerrainBiome landBiome,
        out Vector2 waterNormal)
    {
        landBiome = TerrainBiome.Sand;
        waterNormal = leftNormal;
        if (input == null)
        {
            return false;
        }

        float normalizedProbeDistance = Mathf.Max(0.05f, probeDistance);
        for (int i = 0; i < 4; i++)
        {
            float distance = Mathf.Min(0.6f, normalizedProbeDistance * (1 << i));
            TerrainBiome leftBiome = GetTileBiomeFromSnapshot(input, GetWaterWallSampleCoordinate(worldMidpoint + (leftNormal * distance)));
            TerrainBiome rightBiome = GetTileBiomeFromSnapshot(input, GetWaterWallSampleCoordinate(worldMidpoint - (leftNormal * distance)));
            if (TryResolveWaterWallShorelineFromBiomes(leftBiome, rightBiome, leftNormal, out landBiome, out waterNormal))
            {
                return true;
            }
        }

        return false;
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
            if (TryResolveWaterWallShorelineFromBiomes(leftBiome, rightBiome, leftNormal, out landBiome, out waterNormal))
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2Int GetWaterWallSampleCoordinate(Vector2 sampleWorldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(sampleWorldPosition.x),
            Mathf.RoundToInt(sampleWorldPosition.y));
    }

    private static bool TryResolveWaterWallShorelineFromBiomes(
        TerrainBiome leftBiome,
        TerrainBiome rightBiome,
        Vector2 leftNormal,
        out TerrainBiome landBiome,
        out Vector2 waterNormal)
    {
        bool leftIsWater = leftBiome == TerrainBiome.Water;
        bool rightIsWater = rightBiome == TerrainBiome.Water;
        if (leftIsWater == rightIsWater)
        {
            landBiome = TerrainBiome.Sand;
            waterNormal = leftNormal;
            return false;
        }

        landBiome = !leftIsWater ? leftBiome : rightBiome;
        if (landBiome == TerrainBiome.Water)
        {
            landBiome = TerrainBiome.Sand;
            waterNormal = leftNormal;
            return false;
        }

        waterNormal = leftIsWater ? leftNormal : -leftNormal;
        return true;
    }

    private static void AppendWaterWallQuad(
        ChunkSurfaceBuildData chunkSurface,
        TerrainBiome wallBiome,
        Vector2 start,
        Vector2 end,
        float bottomY,
        float topY,
        Color startColor,
        Color endColor)
    {
        if (chunkSurface == null)
        {
            return;
        }

        int vertexStart = chunkSurface.vertices.Count;
        float effectiveBottomY = bottomY - GeneratedWaterWallVerticalOverlap;
        float effectiveTopY = topY + GeneratedWaterWallVerticalOverlap;
        float height = Mathf.Max(0.001f, effectiveTopY - effectiveBottomY);
        float length = Mathf.Max(0.001f, Vector2.Distance(start, end));

        for (int side = 0; side < 2; side++)
        {
            chunkSurface.vertices.Add(new Vector3(start.x, effectiveBottomY, start.y));
            chunkSurface.vertices.Add(new Vector3(end.x, effectiveBottomY, end.y));
            chunkSurface.vertices.Add(new Vector3(end.x, effectiveTopY, end.y));
            chunkSurface.vertices.Add(new Vector3(start.x, effectiveTopY, start.y));

            chunkSurface.uvs.Add(new Vector2(0f, 0f));
            chunkSurface.uvs.Add(new Vector2(length, 0f));
            chunkSurface.uvs.Add(new Vector2(length, height));
            chunkSurface.uvs.Add(new Vector2(0f, height));

            chunkSurface.colors.Add(startColor);
            chunkSurface.colors.Add(endColor);
            chunkSurface.colors.Add(endColor);
            chunkSurface.colors.Add(startColor);
        }

        List<int> targetTriangles = chunkSurface.trianglesByBiome[GetGeneratedSurfaceTriangleBucket(wallBiome)];
        targetTriangles.Add(vertexStart + 0);
        targetTriangles.Add(vertexStart + 2);
        targetTriangles.Add(vertexStart + 1);
        targetTriangles.Add(vertexStart + 0);
        targetTriangles.Add(vertexStart + 3);
        targetTriangles.Add(vertexStart + 2);

        targetTriangles.Add(vertexStart + 4);
        targetTriangles.Add(vertexStart + 5);
        targetTriangles.Add(vertexStart + 6);
        targetTriangles.Add(vertexStart + 4);
        targetTriangles.Add(vertexStart + 6);
        targetTriangles.Add(vertexStart + 7);
    }

    private static void AppendWaterFoamQuad(
        ChunkSurfaceBuildData chunkSurface,
        Vector2 start,
        Vector2 end,
        Vector2 waterNormal,
        float y,
        float width,
        Color foamColor)
    {
        if (chunkSurface == null
            || chunkSurface.trianglesByBiome == null
            || chunkSurface.trianglesByBiome.Length <= GeneratedSurfaceFoamMaterialIndex
            || width <= 0f
            || foamColor.a <= 0f)
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
        Vector2 nearOffset = waterDirection * shoreInset;
        Vector2 peakOffset = waterDirection * peakDistance;
        Vector2 outerOffset = waterDirection * foamWidth;
        Vector2 startNear = start + nearOffset;
        Vector2 endNear = end + nearOffset;
        Vector2 startPeak = start + peakOffset;
        Vector2 endPeak = end + peakOffset;
        Vector2 startOuter = start + outerOffset;
        Vector2 endOuter = end + outerOffset;

        int vertexStart = chunkSurface.vertices.Count;
        Color nearColor = new Color(foamColor.r, foamColor.g, foamColor.b, 0.34f);
        Color peakColor = new Color(foamColor.r, foamColor.g, foamColor.b, 0.72f);
        Color transparent = new Color(foamColor.r, foamColor.g, foamColor.b, 0f);

        chunkSurface.vertices.Add(new Vector3(startNear.x, y, startNear.y));
        chunkSurface.vertices.Add(new Vector3(endNear.x, y, endNear.y));
        chunkSurface.vertices.Add(new Vector3(startPeak.x, y, startPeak.y));
        chunkSurface.vertices.Add(new Vector3(endPeak.x, y, endPeak.y));
        chunkSurface.vertices.Add(new Vector3(startOuter.x, y, startOuter.y));
        chunkSurface.vertices.Add(new Vector3(endOuter.x, y, endOuter.y));

        chunkSurface.uvs.Add(new Vector2(0f, 0f));
        chunkSurface.uvs.Add(new Vector2(length, 0f));
        chunkSurface.uvs.Add(new Vector2(0f, 0.42f));
        chunkSurface.uvs.Add(new Vector2(length, 0.42f));
        chunkSurface.uvs.Add(new Vector2(0f, 1f));
        chunkSurface.uvs.Add(new Vector2(length, 1f));

        chunkSurface.colors.Add(nearColor);
        chunkSurface.colors.Add(nearColor);
        chunkSurface.colors.Add(peakColor);
        chunkSurface.colors.Add(peakColor);
        chunkSurface.colors.Add(transparent);
        chunkSurface.colors.Add(transparent);

        List<int> targetTriangles = chunkSurface.trianglesByBiome[GeneratedSurfaceFoamMaterialIndex];
        targetTriangles.Add(vertexStart + 0);
        targetTriangles.Add(vertexStart + 3);
        targetTriangles.Add(vertexStart + 2);
        targetTriangles.Add(vertexStart + 0);
        targetTriangles.Add(vertexStart + 1);
        targetTriangles.Add(vertexStart + 3);

        targetTriangles.Add(vertexStart + 2);
        targetTriangles.Add(vertexStart + 3);
        targetTriangles.Add(vertexStart + 5);
        targetTriangles.Add(vertexStart + 2);
        targetTriangles.Add(vertexStart + 5);
        targetTriangles.Add(vertexStart + 4);
    }

    private float GetBiomeSurfaceY(TerrainBiome biome)
    {
        return GetBiomeSurfaceY(biome, generatedSurfaceYOffset, waterSurfaceDepth);
    }

    private float GetBiomeBaseSurfaceY(TerrainBiome biome)
    {
        return GetBiomeBaseSurfaceY(biome, generatedSurfaceYOffset, waterSurfaceDepth);
    }

    private static float GetBiomeSurfaceY(TerrainBiome biome, float surfaceYOffset, float waterDepth)
    {
        if (biome == TerrainBiome.Water)
        {
            return surfaceYOffset - Mathf.Max(0f, waterDepth);
        }

        return surfaceYOffset + (GetBiomeMaterialIndex(biome) * GeneratedSurfaceBiomeLayerStep);
    }

    private static float GetBiomeBaseSurfaceY(TerrainBiome biome, float surfaceYOffset, float waterDepth)
    {
        return GetBiomeSurfaceY(biome, surfaceYOffset, waterDepth) - GeneratedSurfaceBaseInset;
    }

    private Material[] GetGeneratedSurfaceMaterials()
    {
        if (generatedSurfaceMaterials != null
            && generatedSurfaceMaterials.Length == GeneratedSurfaceBaseRenderSubMeshCount)
        {
            return generatedSurfaceMaterials;
        }

        Material blendMaterial = GetGeneratedSurfaceBlendMaterial();
        generatedSurfaceMaterials = new[]
        {
            GetBiomeMaterial(TerrainBiome.Water),
            blendMaterial ?? GetBiomeMaterial(TerrainBiome.Sand),
            GetBiomeMaterial(TerrainBiome.Rock)
        };
        return generatedSurfaceMaterials;
    }

    private Material GetGeneratedSurfaceBlendMaterial()
    {
        if (generatedSurfaceBlendMaterial != null)
        {
            return generatedSurfaceBlendMaterial;
        }

        Shader blendShader = generatedSurfaceBlendShader != null
            ? generatedSurfaceBlendShader
            : Shader.Find("ProjectF/Terrain/BiomeBlend");
        if (blendShader == null)
        {
            return null;
        }

        generatedSurfaceBlendMaterial = new Material(blendShader)
        {
            name = "Runtime_TerrainBiomeBlend",
            enableInstancing = true
        };

        ApplyGeneratedSurfaceBlendMaterialProperties(generatedSurfaceBlendMaterial);
        return generatedSurfaceBlendMaterial;
    }

    private void ApplyGeneratedSurfaceBlendMaterialProperties(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetColor("_SandColor", Color.white);
        material.SetColor("_DirtColor", Color.white);
        material.SetColor("_GrassColor", Color.white);
        material.SetColor("_ForestColor", Color.white);
        material.SetFloat("_TextureTiling", generatedSurfaceBlendTextureTiling);
        material.SetFloat("_NoiseScale", generatedSurfaceBlendNoiseScale);
        material.SetFloat("_NoiseStrength", generatedSurfaceBlendNoiseStrength);
        if (material.HasProperty("_BlendEnabled"))
        {
            material.SetFloat("_BlendEnabled", enableGeneratedSurfaceTextureBlend ? 1f : 0f);
        }

        Material groundMaterial = ResolveSourceMaterialForBiome(TerrainBiome.Grass);
        if (groundMaterial != null && groundMaterial.HasProperty("_ShadowColor"))
        {
            material.SetColor("_ShadowColor", groundMaterial.GetColor("_ShadowColor"));
        }

        if (groundMaterial != null && groundMaterial.HasProperty("_ShadeThreshold"))
        {
            material.SetFloat("_ShadeThreshold", groundMaterial.GetFloat("_ShadeThreshold"));
        }

        if (groundMaterial != null && groundMaterial.HasProperty("_ShadeSmoothness"))
        {
            material.SetFloat("_ShadeSmoothness", groundMaterial.GetFloat("_ShadeSmoothness"));
        }

        Texture2D grassTexture = ResolveGeneratedSurfaceBlendTexture(
            generatedSurfaceBlendGrassTexture,
            groundMaterial,
            "_BaseMap",
            "_MainTex");
        Texture2D forestTexture = ResolveGeneratedSurfaceBlendTexture(
            generatedSurfaceBlendForestTexture,
            ResolveSourceMaterialForBiome(TerrainBiome.Forest),
            "_BaseMap",
            "_MainTex");
        Texture2D dirtTexture = generatedSurfaceBlendDirtTexture != null
            ? generatedSurfaceBlendDirtTexture
            : grassTexture;
        Texture2D sandTexture = generatedSurfaceBlendSandTexture != null
            ? generatedSurfaceBlendSandTexture
            : grassTexture;
        if (forestTexture == null)
        {
            forestTexture = grassTexture;
        }

        if (sandTexture != null)
        {
            material.SetTexture("_SandMap", sandTexture);
        }

        if (dirtTexture != null)
        {
            material.SetTexture("_DirtMap", dirtTexture);
        }

        if (grassTexture != null)
        {
            material.SetTexture("_GrassMap", grassTexture);
        }

        if (forestTexture != null)
        {
            material.SetTexture("_ForestMap", forestTexture);
        }

    }

    private void ApplyGeneratedSurfaceRuntimeMaterialSettings()
    {
        if (generatedSurfaceBlendMaterial != null)
        {
            ApplyGeneratedSurfaceBlendMaterialProperties(generatedSurfaceBlendMaterial);
        }

        if (generatedSurfaceFoamMaterial != null)
        {
            ApplyGeneratedSurfaceFoamMaterialProperties(generatedSurfaceFoamMaterial);
        }

        if (biomeMaterialCache.TryGetValue(TerrainBiome.Water, out Material waterMaterial)
            && waterMaterial != null)
        {
            ApplyGeneratedSurfaceWaterMaterialProperties(waterMaterial);
        }
    }

    private Material GetGeneratedSurfaceFoamMaterial()
    {
        if (generatedSurfaceFoamMaterial != null)
        {
            return generatedSurfaceFoamMaterial;
        }

        Shader foamShader = generatedSurfaceFoamShader != null
            ? generatedSurfaceFoamShader
            : Shader.Find("ProjectF/Terrain/WaterFoamOverlay");
        if (foamShader == null)
        {
            foamShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        generatedSurfaceFoamMaterial = new Material(foamShader)
        {
            name = "Runtime_WaterFoamOverlay",
            enableInstancing = true
        };
        generatedSurfaceFoamMaterial.renderQueue = GeneratedWaterFoamRenderQueue;

        ApplyGeneratedSurfaceFoamMaterialProperties(generatedSurfaceFoamMaterial);
        return generatedSurfaceFoamMaterial;
    }

    private void ApplyGeneratedSurfaceFoamMaterialProperties(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.renderQueue = GeneratedWaterFoamRenderQueue;
        if (material.HasProperty("_FoamColor"))
        {
            Color foamColor = waterFoamOverlayColor;
            foamColor.a = 0f;
            material.SetColor("_FoamColor", foamColor);
        }
    }

    private void ApplyGeneratedSurfaceWaterMaterialProperties(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_GlintEnabled"))
        {
            material.SetFloat("_GlintEnabled", generateWaterSurfaceGlints ? 1f : 0f);
        }

        if (material.HasProperty("_GlintColor"))
        {
            material.SetColor("_GlintColor", waterSurfaceGlintColor);
        }

        if (material.HasProperty("_GlintDirection"))
        {
            Vector2 direction = waterSurfaceGlintDirection.sqrMagnitude > 0.0001f
                ? waterSurfaceGlintDirection.normalized
                : Vector2.right;
            material.SetVector("_GlintDirection", new Vector4(direction.x, direction.y, 0f, 0f));
        }

        if (material.HasProperty("_GlintScale"))
        {
            material.SetFloat("_GlintScale", waterSurfaceGlintScale);
        }

        if (material.HasProperty("_GlintLineWidth"))
        {
            material.SetFloat("_GlintLineWidth", waterSurfaceGlintLineWidth);
        }

        if (material.HasProperty("_GlintBreakup"))
        {
            material.SetFloat("_GlintBreakup", waterSurfaceGlintBreakup);
        }

        if (material.HasProperty("_GlintFlowSpeed"))
        {
            material.SetFloat("_GlintFlowSpeed", waterSurfaceGlintFlowSpeed);
        }
    }

    private Material GetBiomeMaterial(TerrainBiome biome)
    {
        if (biomeMaterialCache.TryGetValue(biome, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Material sourceMaterial = ResolveSourceMaterialForBiome(biome);
        Material material = sourceMaterial != null
            ? new Material(sourceMaterial)
            : new Material(Shader.Find("Universal Render Pipeline/Lit"));

        material.name = $"Runtime_{biome}";
        material.enableInstancing = true;
        if (biome == TerrainBiome.Water)
        {
            ApplyGeneratedSurfaceWaterMaterialProperties(material);
        }

        biomeMaterialCache[biome] = material;
        return material;
    }

    private Material ResolveSourceMaterialForBiome(TerrainBiome biome)
    {
        if (biome == TerrainBiome.Water)
        {
            return generatedSurfaceWaterMaterial;
        }

        if (!TryGetBlockSet(Block.BlockType.Ground, out BlockSet blockSet))
        {
            return null;
        }

        return GetBlockSetMaterial(blockSet);
    }

    private static Material GetBlockSetMaterial(BlockSet blockSet)
    {
        GameObject prefab = SelectBlockPrefab(blockSet);
        if (prefab == null)
        {
            return null;
        }

        MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
        return renderer != null ? renderer.sharedMaterial : null;
    }

    private static Texture2D ResolveGeneratedSurfaceBlendTexture(Texture2D preferredTexture, Material material, params string[] candidatePropertyNames)
    {
        if (preferredTexture != null)
        {
            return preferredTexture;
        }

        if (material == null || candidatePropertyNames == null)
        {
            return null;
        }

        for (int i = 0; i < candidatePropertyNames.Length; i++)
        {
            string propertyName = candidatePropertyNames[i];
            if (!material.HasProperty(propertyName))
            {
                continue;
            }

            Texture texture = material.GetTexture(propertyName);
            if (texture is Texture2D texture2D)
            {
                return texture2D;
            }
        }

        return null;
    }

    private Color GetMapBiomeColor(TerrainBiome biome)
    {
        switch (biome)
        {
            case TerrainBiome.Water:
                return waterBiomeColor;
            case TerrainBiome.Sand:
                return sandBiomeColor;
            case TerrainBiome.Dirt:
                return dirtBiomeColor;
            case TerrainBiome.Forest:
                return forestBiomeColor;
            case TerrainBiome.Rock:
                return rockBiomeColor;
            default:
                return grassBiomeColor;
        }
    }

    public Color GetMapBiomeColorAt(Vector2Int worldCoordinate)
    {
        return GetMapBiomeColor(GetTileBiome(worldCoordinate));
    }

    public Color32 GetMapBiomeColor32At(Vector2Int worldCoordinate)
    {
        return (Color32)GetMapBiomeColorAt(worldCoordinate);
    }

    public bool IsWaterBiomeAt(Vector2Int worldCoordinate)
    {
        return GetTileBiome(worldCoordinate) == TerrainBiome.Water;
    }

    public bool IsFarmableGroundBiomeAt(Vector2Int worldCoordinate)
    {
        TerrainBiome biome = GetTileBiome(worldCoordinate);
        return biome != TerrainBiome.Water && biome != TerrainBiome.Sand;
    }

    public bool IsWaterSurfaceAtWorldPosition(Vector2 worldPosition, float[] weightBuffer = null)
    {
        return GetWaterSurfaceScoreAtWorldPosition(worldPosition, weightBuffer) > 0f;
    }

    public float GetWaterSurfaceScoreAtWorldPosition(Vector2 worldPosition, float[] weightBuffer = null)
    {
        if (weightBuffer == null || weightBuffer.Length < GeneratedSurfaceBiomeMaterialCount)
        {
            weightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        }

        return GetBiomeScoreAtSample(worldPosition, TerrainBiome.Water, weightBuffer);
    }

    private Color GetGeneratedSurfaceBlendWeights(Vector2Int origin, Vector2 localPoint, float[] weights)
    {
        Vector2 worldPoint = new Vector2(origin.x + localPoint.x, origin.y + localPoint.y);
        SampleBiomeWeights(worldPoint, weights);

        float sandWeight = weights[GetBiomeMaterialIndex(TerrainBiome.Sand)];
        float dirtWeightValue = weights[GetBiomeMaterialIndex(TerrainBiome.Dirt)];
        float grassWeightValue = weights[GetBiomeMaterialIndex(TerrainBiome.Grass)];
        float forestWeightValue = weights[GetBiomeMaterialIndex(TerrainBiome.Forest)];
        float totalWeight = sandWeight + dirtWeightValue + grassWeightValue + forestWeightValue;

        if (totalWeight <= 0.0001f)
        {
            if (sandWeight >= dirtWeightValue && sandWeight >= grassWeightValue && sandWeight >= forestWeightValue)
            {
                return new Color(1f, 0f, 0f, 0f);
            }

            if (dirtWeightValue >= grassWeightValue && dirtWeightValue >= forestWeightValue)
            {
                return new Color(0f, 1f, 0f, 0f);
            }

            if (grassWeightValue >= forestWeightValue)
            {
                return new Color(0f, 0f, 1f, 0f);
            }

            return new Color(0f, 0f, 0f, 1f);
        }

        float inverseTotal = 1f / totalWeight;
        return new Color(
            sandWeight * inverseTotal,
            dirtWeightValue * inverseTotal,
            grassWeightValue * inverseTotal,
            forestWeightValue * inverseTotal);
    }

    private Color GetGeneratedWaterDepthColor(Vector2Int origin, Vector2 localPoint)
    {
        Vector2 worldPoint = new Vector2(origin.x + localPoint.x, origin.y + localPoint.y);
        return EncodeGeneratedWaterDepthColor(GetNearestGeneratedLandDistance(worldPoint));
    }

    private static Color EncodeGeneratedWaterDepthColor(float landDistance)
    {
        float depth = Mathf.InverseLerp(0f, GeneratedWaterDepthDeepDistance, landDistance);
        depth = Mathf.SmoothStep(0f, 1f, depth);
        return new Color(depth, depth, depth, 1f);
    }

    private static float GetNearestGeneratedLandDistanceFromSnapshot(ChunkSurfaceWorkerInput input, Vector2 worldPoint)
    {
        if (input == null)
        {
            return GeneratedWaterDepthDeepDistance;
        }

        Vector2Int center = GetSurfaceSampleCoordinate(worldPoint);
        float nearestDistance = GeneratedWaterDepthDeepDistance;
        for (int offsetY = -GeneratedWaterDepthSearchRadius; offsetY <= GeneratedWaterDepthSearchRadius; offsetY++)
        {
            for (int offsetX = -GeneratedWaterDepthSearchRadius; offsetX <= GeneratedWaterDepthSearchRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (GetTileBiomeFromSnapshot(input, coordinate) == TerrainBiome.Water
                    && !GetBlockedForWaterFromSnapshot(input, coordinate))
                {
                    continue;
                }

                nearestDistance = Mathf.Min(nearestDistance, GetDistanceToTileRect(worldPoint, coordinate));
            }
        }

        return nearestDistance;
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
                    && !IsBlockedForWater(coordinate))
                {
                    continue;
                }

                nearestDistance = Mathf.Min(nearestDistance, GetDistanceToTileRect(worldPoint, coordinate));
            }
        }

        return nearestDistance;
    }

    private static float GetDistanceToTileRect(Vector2 point, Vector2Int coordinate)
    {
        float dx = Mathf.Max(Mathf.Abs(point.x - coordinate.x) - 0.5f, 0f);
        float dy = Mathf.Max(Mathf.Abs(point.y - coordinate.y) - 0.5f, 0f);
        return Mathf.Sqrt((dx * dx) + (dy * dy));
    }

    private static int GetBiomeMaterialIndex(TerrainBiome biome)
    {
        switch (biome)
        {
            case TerrainBiome.Water:
                return 0;
            case TerrainBiome.Sand:
                return 1;
            case TerrainBiome.Dirt:
                return 2;
            case TerrainBiome.Grass:
                return 3;
            case TerrainBiome.Forest:
                return 4;
            case TerrainBiome.Rock:
                return 5;
            default:
                return 3;
        }
    }

    private static int GetGeneratedSurfaceTriangleBucket(TerrainBiome biome)
    {
        switch (biome)
        {
            case TerrainBiome.Sand:
            case TerrainBiome.Dirt:
            case TerrainBiome.Grass:
            case TerrainBiome.Forest:
                return GetBiomeMaterialIndex(TerrainBiome.Sand);
            default:
                return GetBiomeMaterialIndex(biome);
        }
    }
}
