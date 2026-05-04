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
    private BlockBiomeVisualData BuildBlockBiomeVisualData(Vector2Int worldCoordinate)
    {
        TerrainBiome primaryBiome = GetTileBiome(worldCoordinate);
        return new BlockBiomeVisualData
        {
            primaryBiome = primaryBiome,
            surfaceBiomes = null
        };
    }

    private void ApplyBlockBiomeVisuals(Block block, BlockBiomeVisualData visualData)
    {
        if (block == null || block.Body == null)
        {
            return;
        }

        ApplyPrimaryBiomeToBaseBody(block);
    }

    private void ApplyPrimaryBiomeToBaseBody(Block block)
    {
        if (block == null || block.Body == null)
        {
            return;
        }

        block.SetBaseBodyVisible(false);
    }

    private void ApplyChunkBiomeSurface(Transform chunkRoot, ChunkSurfaceBuildData chunkSurface)
    {
        using (ApplyChunkSurfaceMarker.Auto())
        {
            if (chunkRoot == null || chunkSurface == null || chunkSurface.vertices.Count == 0)
            {
                return;
            }

            Transform generatedSurface = chunkRoot.Find("GeneratedSurface");
            if (generatedSurface == null)
            {
                GameObject surfaceObject = new GameObject("GeneratedSurface");
                generatedSurface = surfaceObject.transform;
                generatedSurface.SetParent(chunkRoot, false);
                surfaceObject.AddComponent<MeshFilter>();
                surfaceObject.AddComponent<MeshRenderer>();
            }

            MeshFilter meshFilter = generatedSurface.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = generatedSurface.GetComponent<MeshRenderer>();
            if (meshFilter == null || meshRenderer == null)
            {
                return;
            }

            Mesh generatedMesh = BuildGeneratedSurfaceMesh(chunkSurface);
            generatedMesh.name = $"GeneratedSurface_{chunkRoot.name}";
            meshFilter.sharedMesh = generatedMesh;
            meshRenderer.sharedMaterials = GetGeneratedSurfaceMaterials();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = true;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
    }

    private Mesh BuildGeneratedSurfaceMesh(ChunkSurfaceBuildData chunkSurface)
    {
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(chunkSurface.vertices);
        mesh.SetUVs(0, chunkSurface.uvs);
        if (chunkSurface.colors.Count == chunkSurface.vertices.Count)
        {
            mesh.SetColors(chunkSurface.colors);
        }
        mesh.subMeshCount = chunkSurface.trianglesByBiome.Length;
        for (int i = 0; i < chunkSurface.trianglesByBiome.Length; i++)
        {
            mesh.SetTriangles(chunkSurface.trianglesByBiome[i], i, true);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return mesh;
    }

    private ChunkSurfaceBuildData BuildCurvedChunkSurface(Vector2Int origin, int chunkSizeInBlocks)
    {
        ChunkSurfaceBuildData chunkSurface = new ChunkSurfaceBuildData(6);
        IEnumerator routine = BuildCurvedChunkSurfaceRoutine(chunkSurface, origin, chunkSizeInBlocks, false);
        while (routine.MoveNext())
        {
        }

        return chunkSurface;
    }

    private Task<ChunkSurfaceBuildData> CreateChunkSurfaceBuildTask(Vector2Int origin, int chunkSizeInBlocks)
    {
        ChunkSurfaceWorkerInput input = CreateChunkSurfaceWorkerInput(origin, chunkSizeInBlocks);
        return Task.Run(() => BuildCurvedChunkSurfaceFromSnapshot(input));
    }

    private ChunkSurfaceWorkerInput CreateChunkSurfaceWorkerInput(Vector2Int origin, int chunkSizeInBlocks)
    {
        int resolution = Mathf.Max(2, terrainSurfaceSubdivisions);
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
            biomeGrid = new TerrainBiome[gridSize * gridSize],
            blockedWaterGrid = new bool[gridSize * gridSize],
            generatedSurfaceYOffset = generatedSurfaceYOffset,
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
            }
        }

        return input;
    }

    private static ChunkSurfaceBuildData BuildCurvedChunkSurfaceFromSnapshot(ChunkSurfaceWorkerInput input)
    {
        ChunkSurfaceBuildData chunkSurface = new ChunkSurfaceBuildData(6)
        {
            origin = input.origin
        };

        AppendDominantBiomeBaseSurfaceFromSnapshot(chunkSurface, input);
        for (int biomeIndex = 0; biomeIndex < 6; biomeIndex++)
        {
            AppendBiomeContourSurfaceFromSnapshot(chunkSurface, (TerrainBiome)biomeIndex, input);
        }

        AppendContourSafetyPatchesFromSnapshot(chunkSurface, input);
        return chunkSurface;
    }

    private static void AppendDominantBiomeBaseSurfaceFromSnapshot(ChunkSurfaceBuildData chunkSurface, ChunkSurfaceWorkerInput input)
    {
        float[] weightBuffer = new float[6];
        for (int cellY = 0; cellY < input.cellCount; cellY++)
        {
            for (int cellX = 0; cellX < input.cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 center = (p00 + p11) * 0.5f;
                TerrainBiome dominantBiome = GetDominantBiomeAtSampleFromSnapshot(
                    input,
                    new Vector2(input.origin.x + center.x, input.origin.y + center.y),
                    weightBuffer);

                AppendContourPolygonAtHeightFromSnapshot(
                    chunkSurface,
                    input,
                    dominantBiome,
                    new List<Vector2> { p00, p10, p11, p01 },
                    input.generatedSurfaceYOffset - 0.0035f);
            }
        }
    }

    private static void AppendBiomeContourSurfaceFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        TerrainBiome biome,
        ChunkSurfaceWorkerInput input)
    {
        float[] weightBuffer = new float[6];
        float[,] scores = new float[input.cellCount + 1, input.cellCount + 1];

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

                float s00 = scores[cellX, cellY];
                float s10 = scores[cellX + 1, cellY];
                float s11 = scores[cellX + 1, cellY + 1];
                float s01 = scores[cellX, cellY + 1];
                float centerScore = GetBiomeScoreAtSampleFromSnapshot(
                    input,
                    new Vector2(input.origin.x + (p00.x + p11.x) * 0.5f, input.origin.y + (p00.y + p11.y) * 0.5f),
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
                    centerScore);
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
        float centerScore)
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
            AppendContourPolygonFromSnapshot(chunkSurface, input, biome, new List<Vector2> { p00, p10, p11, p01 });
            return;
        }

        Vector2 bottom = InterpolateContourPoint(p00, p10, s00, s10);
        Vector2 right = InterpolateContourPoint(p10, p11, s10, s11);
        Vector2 top = InterpolateContourPoint(p11, p01, s11, s01);
        Vector2 left = InterpolateContourPoint(p01, p00, s01, s00);

        if ((mask == 5 || mask == 10) && centerScore <= 0f)
        {
            if (mask == 5)
            {
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, new List<Vector2> { p00, bottom, left });
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, new List<Vector2> { p11, top, right });
            }
            else
            {
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, new List<Vector2> { p10, right, bottom });
                AppendContourPolygonFromSnapshot(chunkSurface, input, biome, new List<Vector2> { p01, left, top });
            }

            return;
        }

        List<Vector2> polygon = new List<Vector2>(8);
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
            input.generatedSurfaceYOffset + (GetBiomeMaterialIndex(biome) * 0.004f));
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
        float[] weightBuffer = new float[6];
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 point = polygon[i];
            chunkSurface.vertices.Add(new Vector3(point.x, y, point.y));
            chunkSurface.uvs.Add(point);
            chunkSurface.colors.Add(GetGeneratedSurfaceBlendWeightsFromSnapshot(input, chunkSurface.origin, point, weightBuffer));
        }

        List<int> targetTriangles = chunkSurface.trianglesByBiome[GetBiomeMaterialIndex(biome)];
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            targetTriangles.Add(vertexStart + 0);
            targetTriangles.Add(vertexStart + i + 1);
            targetTriangles.Add(vertexStart + i);
        }
    }

    private static void AppendContourSafetyPatchesFromSnapshot(ChunkSurfaceBuildData chunkSurface, ChunkSurfaceWorkerInput input)
    {
        float[] weightBuffer = new float[6];
        float patchRadius = 0.22f / Mathf.Max(1, input.resolution);

        for (int cellY = 0; cellY < input.cellCount; cellY++)
        {
            for (int cellX = 0; cellX < input.cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + (cellY / (float)input.resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)input.resolution), -0.5f + ((cellY + 1) / (float)input.resolution));
                Vector2 center = (p00 + p11) * 0.5f;

                TerrainBiome centerBiome = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + center.x, input.origin.y + center.y), weightBuffer);
                TerrainBiome biome00 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p00.x, input.origin.y + p00.y), weightBuffer);
                TerrainBiome biome10 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p10.x, input.origin.y + p10.y), weightBuffer);
                TerrainBiome biome11 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p11.x, input.origin.y + p11.y), weightBuffer);
                TerrainBiome biome01 = GetDominantBiomeAtSampleFromSnapshot(input, new Vector2(input.origin.x + p01.x, input.origin.y + p01.y), weightBuffer);

                int uniqueBiomeCount = CountUniqueBiomes(centerBiome, biome00, biome10, biome11, biome01);
                if (uniqueBiomeCount >= 3)
                {
                    AppendCenterSafetyPatchFromSnapshot(chunkSurface, input, centerBiome, center, patchRadius);
                }
            }
        }
    }

    private static void AppendCenterSafetyPatchFromSnapshot(
        ChunkSurfaceBuildData chunkSurface,
        ChunkSurfaceWorkerInput input,
        TerrainBiome biome,
        Vector2 center,
        float patchRadius)
    {
        AppendContourPolygonFromSnapshot(
            chunkSurface,
            input,
            biome,
            new List<Vector2>
            {
                new Vector2(center.x, center.y - patchRadius),
                new Vector2(center.x + patchRadius, center.y),
                new Vector2(center.x, center.y + patchRadius),
                new Vector2(center.x - patchRadius, center.y)
            });
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

    private static TerrainBiome GetTileBiomeFromSnapshot(ChunkSurfaceWorkerInput input, Vector2Int worldCoordinate)
    {
        int localX = worldCoordinate.x - input.biomeGridMinX;
        int localY = worldCoordinate.y - input.biomeGridMinY;
        if (localX < 0 || localY < 0 || localX >= input.biomeGridWidth || localY >= input.biomeGridHeight)
        {
            return TerrainBiome.Grass;
        }

        return input.biomeGrid[localX + (localY * input.biomeGridWidth)];
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
        int resolution = Mathf.Max(2, terrainSurfaceSubdivisions);
        int cellCount = Mathf.Max(1, chunkSizeInBlocks * resolution);
        IEnumerator baseRoutine = AppendDominantBiomeBaseSurfaceRoutine(chunkSurface, origin, cellCount, resolution, allowYield);
        while (baseRoutine.MoveNext())
        {
            if (allowYield && baseRoutine.Current != null)
            {
                yield return baseRoutine.Current;
            }
        }

        for (int biomeIndex = 0; biomeIndex < 6; biomeIndex++)
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

        float[] weightBuffer = new float[6];
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
                TerrainBiome dominantBiome = GetDominantBiomeAtSample(
                    new Vector2(origin.x + center.x, origin.y + center.y),
                    weightBuffer);

                AppendContourPolygonAtHeight(
                    chunkSurface,
                    dominantBiome,
                    new List<Vector2> { p00, p10, p11, p01 },
                    generatedSurfaceYOffset - 0.0035f);
            }

            if (allowYield && ++rowsSinceYield >= surfaceRowBudget)
            {
                rowsSinceYield = 0;
                yield return null;
            }
        }
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

        float[] weightBuffer = new float[6];
        float[,] scores = new float[cellCount + 1, cellCount + 1];
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

                float s00 = scores[cellX, cellY];
                float s10 = scores[cellX + 1, cellY];
                float s11 = scores[cellX + 1, cellY + 1];
                float s01 = scores[cellX, cellY + 1];
                float centerScore = GetBiomeScoreAtSample(
                    new Vector2(origin.x + (p00.x + p11.x) * 0.5f, origin.y + (p00.y + p11.y) * 0.5f),
                    biome,
                    weightBuffer);

                AppendMarchingSquaresCell(chunkSurface, biome, p00, p10, p11, p01, s00, s10, s11, s01, centerScore);
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
        float centerScore)
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
            AppendContourPolygon(chunkSurface, biome, new List<Vector2> { p00, p10, p11, p01 });
            return;
        }

        Vector2 bottom = InterpolateContourPoint(p00, p10, s00, s10);
        Vector2 right = InterpolateContourPoint(p10, p11, s10, s11);
        Vector2 top = InterpolateContourPoint(p11, p01, s11, s01);
        Vector2 left = InterpolateContourPoint(p01, p00, s01, s00);

        if ((mask == 5 || mask == 10) && centerScore <= 0f)
        {
            if (mask == 5)
            {
                AppendContourPolygon(chunkSurface, biome, new List<Vector2> { p00, bottom, left });
                AppendContourPolygon(chunkSurface, biome, new List<Vector2> { p11, top, right });
            }
            else
            {
                AppendContourPolygon(chunkSurface, biome, new List<Vector2> { p10, right, bottom });
                AppendContourPolygon(chunkSurface, biome, new List<Vector2> { p01, left, top });
            }

            return;
        }

        List<Vector2> polygon = new List<Vector2>(8);
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
            generatedSurfaceYOffset + (GetBiomeMaterialIndex(biome) * 0.004f));
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
            chunkSurface.vertices.Add(new Vector3(point.x, y, point.y));
            chunkSurface.uvs.Add(point);
            chunkSurface.colors.Add(GetGeneratedSurfaceBlendWeights(chunkSurface.origin, point, chunkSurface.blendWeightBuffer));
        }

        List<int> targetTriangles = chunkSurface.trianglesByBiome[GetBiomeMaterialIndex(biome)];
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            targetTriangles.Add(vertexStart + 0);
            targetTriangles.Add(vertexStart + i + 1);
            targetTriangles.Add(vertexStart + i);
        }
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

        float[] weightBuffer = new float[6];
        int surfaceRowBudget = Mathf.Max(1, chunkSurfaceRowsPerFrame);
        int rowsSinceYield = 0;
        float patchRadius = 0.22f / Mathf.Max(1, resolution);

        for (int cellY = 0; cellY < cellCount; cellY++)
        {
            for (int cellX = 0; cellX < cellCount; cellX++)
            {
                Vector2 p00 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p10 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + (cellY / (float)resolution));
                Vector2 p11 = new Vector2(-0.5f + ((cellX + 1) / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 p01 = new Vector2(-0.5f + (cellX / (float)resolution), -0.5f + ((cellY + 1) / (float)resolution));
                Vector2 center = (p00 + p11) * 0.5f;

                TerrainBiome centerBiome = GetDominantBiomeAtSample(
                    new Vector2(origin.x + center.x, origin.y + center.y),
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
                    AppendCenterSafetyPatch(chunkSurface, centerBiome, center, patchRadius);
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
        float patchRadius)
    {
        AppendContourPolygon(
            chunkSurface,
            biome,
            new List<Vector2>
            {
                new Vector2(center.x, center.y - patchRadius),
                new Vector2(center.x + patchRadius, center.y),
                new Vector2(center.x, center.y + patchRadius),
                new Vector2(center.x - patchRadius, center.y)
            });
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

    private Material[] GetGeneratedSurfaceMaterials()
    {
        Material blendMaterial = GetGeneratedSurfaceBlendMaterial();
        return new[]
        {
            GetBiomeMaterial(TerrainBiome.Water),
            blendMaterial ?? GetBiomeMaterial(TerrainBiome.Sand),
            blendMaterial ?? GetBiomeMaterial(TerrainBiome.Dirt),
            blendMaterial ?? GetBiomeMaterial(TerrainBiome.Grass),
            blendMaterial ?? GetBiomeMaterial(TerrainBiome.Forest),
            GetBiomeMaterial(TerrainBiome.Rock)
        };
    }

    private Material GetGeneratedSurfaceBlendMaterial()
    {
        if (!enableGeneratedSurfaceTextureBlend)
        {
            return null;
        }

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

    private void UpgradeLegacyGeneratedSurfaceBlendSettings()
    {
        if (Mathf.Approximately(generatedSurfaceBlendTextureTiling, 1.12f))
        {
            generatedSurfaceBlendTextureTiling = 0.28f;
        }
    }

    private void ApplyGeneratedSurfaceBlendSettingsToRuntimeMaterial()
    {
        if (generatedSurfaceBlendMaterial == null)
        {
            return;
        }

        ApplyGeneratedSurfaceBlendMaterialProperties(generatedSurfaceBlendMaterial);
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
        GameObject prefab = SelectBlockPrefab(blockSet, false);
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
}
