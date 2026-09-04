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
    private bool CanSpawnResourceOnBiome(TerrainBiome biome)
    {
        return biome != TerrainBiome.Water && biome != TerrainBiome.Sand;
    }

    private TerrainBiome GetTileBiome(Vector2Int worldCoordinate)
    {
        if (tileBiomeCache.TryGetValue(worldCoordinate, out TerrainBiome cachedBiome))
        {
            return cachedBiome;
        }

        TerrainBiome biome = ResolveTileBiome(worldCoordinate);
        tileBiomeCache[worldCoordinate] = biome;
        return biome;
    }

    private TerrainBiome ResolveTileBiome(Vector2Int worldCoordinate)
    {
        if (!IsCoordinateInsideMapBounds(worldCoordinate))
        {
            return TerrainBiome.Water;
        }

        if (IsRawWaterTileBiome(worldCoordinate))
        {
            return TerrainBiome.Water;
        }

        int shorelineWidth = GetShorelineWidth(worldCoordinate);
        if (HasRawWaterWithin(worldCoordinate, shorelineWidth))
        {
            return TerrainBiome.Sand;
        }

        TerrainBiome landBiome = ResolveLandBiome(worldCoordinate);
        if (landBiome == TerrainBiome.Rock && HasRawWaterWithin(worldCoordinate, shorelineWidth + 1))
        {
            float shoreLandSelector = Hash01(worldCoordinate.x, worldCoordinate.y, 9217);
            if (shoreLandSelector < 0.38f)
            {
                landBiome = TerrainBiome.Dirt;
            }
            else if (shoreLandSelector < 0.72f)
            {
                landBiome = TerrainBiome.Grass;
            }
            else
            {
                landBiome = TerrainBiome.Forest;
            }
        }

        return landBiome;
    }

    private TerrainBiome ResolveLandBiome(Vector2Int worldCoordinate)
    {
        float selector = GetLandBiomeSelector(worldCoordinate);
        GetLandBiomeThresholds(out float dirtThreshold, out float grassThreshold, out float forestThreshold);

        if (selector < dirtThreshold)
        {
            return TerrainBiome.Dirt;
        }

        if (selector < grassThreshold)
        {
            return TerrainBiome.Grass;
        }

        if (selector < forestThreshold)
        {
            return TerrainBiome.Forest;
        }

        return TerrainBiome.Rock;
    }

    private float GetLandBiomeSelector(Vector2Int worldCoordinate)
    {
        float primary = SampleNoise(worldCoordinate, landBiomePrimaryScale, new Vector2(117.3f, 901.8f));
        float detail = SampleNoise(worldCoordinate, landBiomeDetailScale, new Vector2(611.5f, 273.4f));
        return Mathf.Clamp01((primary * 0.72f) + (detail * 0.28f));
    }

    private void GetLandBiomeThresholds(out float dirtThreshold, out float grassThreshold, out float forestThreshold)
    {
        float totalWeight = Mathf.Max(0.001f, dirtWeight + grassWeight + forestWeight + rockWeight);
        dirtThreshold = dirtWeight / totalWeight;
        grassThreshold = dirtThreshold + (grassWeight / totalWeight);
        forestThreshold = grassThreshold + (forestWeight / totalWeight);
    }

    private float EvaluateTerrainDrivenTreeShapeMask(Vector2Int worldCoordinate, ResourceEntry entry)
    {
        TerrainBiome biome = GetTileBiome(worldCoordinate);
        if (!CanSpawnResourceOnBiome(biome))
        {
            return 0f;
        }

        Vector2 primaryOffset = new Vector2(117.3f, 901.8f) + (entry.patchOffset * 0.026f);
        Vector2 detailOffset = new Vector2(611.5f, 273.4f) + (entry.detailOffset * 0.032f);
        Vector2 coarseOffset = new Vector2(381.2f, 719.5f) + (entry.patchOffset * 0.014f) - (entry.detailOffset * 0.011f);

        float selector = SampleNoise(worldCoordinate, landBiomePrimaryScale * 0.95f, primaryOffset);
        float detail = SampleNoise(worldCoordinate, landBiomeDetailScale * 1.18f, detailOffset);
        float coarse = SampleNoise(worldCoordinate, landBiomePrimaryScale * 0.58f, coarseOffset);

        // Reuse the same terrain-noise "grain" as biome generation, but do not bind trees to the Forest biome band.
        float terrainShape = Mathf.Clamp01((selector * 0.54f) + (coarse * 0.31f) + (detail * 0.15f));
        float bandCenter = Mathf.Lerp(0.24f, 0.76f, Hash01(entry.salt, 177, 931));
        float bandHalfWidth = Mathf.Lerp(
            0.14f,
            0.34f,
            Mathf.InverseLerp(1f, 3f, Mathf.Max(1f, treePatchSizeMultiplier)));
        float normalizedDistance = Mathf.Abs(terrainShape - bandCenter) / Mathf.Max(0.001f, bandHalfWidth);
        float mask = Mathf.Clamp01(1f - normalizedDistance);
        mask = mask * mask * (3f - (2f * mask));

        float contourNoise = SampleNoise(
            worldCoordinate,
            landBiomeDetailScale * 0.82f,
            new Vector2(843.4f, 151.9f) + (entry.patchOffset * 0.017f));
        float contourMask = Mathf.Lerp(0.72f, 1f, contourNoise);
        return Mathf.Clamp01(mask * contourMask);
    }

    private float SampleTerrainDrivenTreeDensityNoise(Vector2Int worldCoordinate, ResourceEntry entry)
    {
        Vector2 primaryOffset = new Vector2(117.3f, 901.8f) + (entry.patchOffset * 0.035f);
        Vector2 detailOffset = new Vector2(611.5f, 273.4f) + (entry.detailOffset * 0.045f);
        float primary = SampleNoise(worldCoordinate, landBiomePrimaryScale * 1.15f, primaryOffset);
        float detail = SampleNoise(worldCoordinate, landBiomeDetailScale * 1.45f, detailOffset);
        return Mathf.Clamp01((primary * 0.58f) + (detail * 0.42f));
    }

    private bool IsRawWaterTileBiome(Vector2Int worldCoordinate)
    {
        if (rawWaterCache.TryGetValue(worldCoordinate, out bool cachedWater))
        {
            return cachedWater;
        }

        bool isWater = IsIslandCoastWater(worldCoordinate);
        if (!IsBlockedForWater(worldCoordinate))
        {
            float waterThreshold = Mathf.Lerp(0.64f, 0.48f, Mathf.Clamp01(waterFillPercent * 1.35f));
            float waterField = EvaluateWaterField(worldCoordinate);
            float continuityThreshold = waterThreshold - Mathf.Lerp(0.1f, 0.18f, Mathf.InverseLerp(0.8f, 3f, riverWidth));
            isWater = isWater
                      || waterField > waterThreshold
                      || (waterField >= continuityThreshold && HasRiverContinuitySupport(worldCoordinate));
        }

        rawWaterCache[worldCoordinate] = isWater;
        return isWater;
    }

    private bool IsIslandCoastWater(Vector2Int worldCoordinate)
    {
        if (!IsCoordinateInsideMapBounds(worldCoordinate))
        {
            return true;
        }

        float halfSize = GetNormalizedMapSize() * 0.5f;
        Vector2 normalized = new Vector2(worldCoordinate.x + 0.5f, worldCoordinate.y + 0.5f) / Mathf.Max(1f, halfSize);
        float distanceFromCenter = normalized.magnitude;
        float primaryNoise = SampleNoise(worldCoordinate, IslandCoastNoiseScale, new Vector2(187.4f, 58.6f));
        float detailNoise = SampleNoise(worldCoordinate, IslandCoastDetailNoiseScale, new Vector2(643.2f, 911.7f));
        float coastNoise = ((primaryNoise * 0.7f) + (detailNoise * 0.3f) - 0.5f) * IslandCoastIrregularity;
        float protectedRadius = GetIslandProtectedRadius(halfSize);
        float coastRadius = Mathf.Max(protectedRadius, IslandLandRadius + coastNoise);
        return distanceFromCenter > coastRadius;
    }

    private float GetIslandProtectedRadius(float halfSize)
    {
        float starterPatchReach = generateStarterResourcePatches
            ? starterPatchDistanceFromCenter + starterPatchHalfSize + 3f
            : 0f;
        float starterTreeReach = generateStarterTrees
            ? starterTreeDistanceFromCenter + 3f
            : 0f;
        float protectedDistance = Mathf.Max(startSafeZoneRadius + 3f, starterPatchReach, starterTreeReach);
        return Mathf.Clamp01(protectedDistance / Mathf.Max(1f, halfSize));
    }

    private bool HasRawWaterWithin(Vector2Int worldCoordinate, int radius)
    {
        int normalizedRadius = Mathf.Max(1, radius);
        for (int offsetY = -normalizedRadius; offsetY <= normalizedRadius; offsetY++)
        {
            for (int offsetX = -normalizedRadius; offsetX <= normalizedRadius; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }

                if (IsRawWaterTileBiome(worldCoordinate + new Vector2Int(offsetX, offsetY)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private int GetShorelineWidth(Vector2Int worldCoordinate)
    {
        int minWidth = Mathf.Max(1, Mathf.Min(sandMinWidth, sandMaxWidth));
        int maxWidth = Mathf.Max(minWidth, Mathf.Max(sandMinWidth, sandMaxWidth));
        if (minWidth == maxWidth)
        {
            return minWidth;
        }

        return Hash01(worldCoordinate.x, worldCoordinate.y, 8309) > 0.5f ? maxWidth : minWidth;
    }

    private float EvaluateWaterField(Vector2Int worldCoordinate)
    {
        return Mathf.Max(
            SampleLakeLayer(worldCoordinate, largeLakeCellSize, largeLakeChance, largeLakeRadiusRange, largeLakeBlobNoiseScale, 4101),
            SampleLakeLayer(worldCoordinate, smallLakeCellSize, smallLakeChance, smallLakeRadiusRange, smallLakeBlobNoiseScale, 5201),
            SampleRiverLayer(worldCoordinate),
            SampleGuaranteedStartLake(worldCoordinate));
    }

    private float SampleLakeLayer(
        Vector2Int worldCoordinate,
        float cellSize,
        float spawnChance,
        Vector2 radiusRange,
        float blobNoiseScale,
        int salt)
    {
        float normalizedCellSize = Mathf.Max(4f, cellSize);
        Vector2 position = new Vector2(worldCoordinate.x, worldCoordinate.y);
        int cellX = Mathf.FloorToInt(position.x / normalizedCellSize);
        int cellY = Mathf.FloorToInt(position.y / normalizedCellSize);
        float bestInfluence = 0f;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                int candidateCellX = cellX + offsetX;
                int candidateCellY = cellY + offsetY;
                if (Hash01(candidateCellX, candidateCellY, salt) > spawnChance)
                {
                    continue;
                }

                Vector2 center = GetCellFeatureCenter(candidateCellX, candidateCellY, normalizedCellSize, salt + 13);
                float radiusX = Mathf.Lerp(radiusRange.x, radiusRange.y, Hash01(candidateCellX, candidateCellY, salt + 29));
                float radiusY = Mathf.Lerp(radiusRange.x, radiusRange.y, Hash01(candidateCellX, candidateCellY, salt + 47));
                Vector2 delta = position - center;
                if (Mathf.Abs(delta.x) > radiusX * 1.6f || Mathf.Abs(delta.y) > radiusY * 1.6f)
                {
                    continue;
                }

                float radial = ((delta.x * delta.x) / Mathf.Max(0.001f, radiusX * radiusX))
                             + ((delta.y * delta.y) / Mathf.Max(0.001f, radiusY * radiusY));
                float blobNoise = Mathf.Lerp(
                    0.82f,
                    1.18f,
                    SampleNoise(
                        new Vector2(worldCoordinate.x, worldCoordinate.y),
                        blobNoiseScale,
                        new Vector2((candidateCellX * 13.7f) + salt, (candidateCellY * 29.1f) - salt)));

                float influence = 1f - (radial * blobNoise);
                if (influence > bestInfluence)
                {
                    bestInfluence = influence;
                }
            }
        }

        return bestInfluence;
    }

    private float SampleRiverLayer(Vector2Int worldCoordinate)
    {
        float normalizedCellSize = Mathf.Max(32f, riverCellSize);
        Vector2 position = new Vector2(worldCoordinate.x, worldCoordinate.y);
        int cellX = Mathf.FloorToInt(position.x / normalizedCellSize);
        int cellY = Mathf.FloorToInt(position.y / normalizedCellSize);
        float bestInfluence = 0f;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                int candidateCellX = cellX + offsetX;
                int candidateCellY = cellY + offsetY;
                if (Hash01(candidateCellX, candidateCellY, 6901) > riverChance)
                {
                    continue;
                }

                Vector2 cellMin = new Vector2(candidateCellX * normalizedCellSize, candidateCellY * normalizedCellSize);
                Vector2 cellCenter = cellMin + (Vector2.one * normalizedCellSize * 0.5f);
                bool horizontal = Hash01(candidateCellX, candidateCellY, 6917) > 0.5f;
                float startJitter = Mathf.Lerp(-normalizedCellSize * 0.22f, normalizedCellSize * 0.22f, Hash01(candidateCellX, candidateCellY, 6941));
                float endJitter = Mathf.Lerp(-normalizedCellSize * 0.22f, normalizedCellSize * 0.22f, Hash01(candidateCellX, candidateCellY, 6953));
                float controlJitter = Mathf.Lerp(-riverCurveStrength, riverCurveStrength, Hash01(candidateCellX, candidateCellY, 6967));

                Vector2 startPoint;
                Vector2 endPoint;
                Vector2 controlPoint;
                if (horizontal)
                {
                    startPoint = new Vector2(cellMin.x - 1f, cellCenter.y + startJitter);
                    endPoint = new Vector2(cellMin.x + normalizedCellSize + 1f, cellCenter.y + endJitter);
                    controlPoint = cellCenter + new Vector2(0f, controlJitter);
                }
                else
                {
                    startPoint = new Vector2(cellCenter.x + startJitter, cellMin.y - 1f);
                    endPoint = new Vector2(cellCenter.x + endJitter, cellMin.y + normalizedCellSize + 1f);
                    controlPoint = cellCenter + new Vector2(controlJitter, 0f);
                }

                float pathWidth = riverWidth * Mathf.Lerp(1.05f, 1.42f, Hash01(candidateCellX, candidateCellY, 6989));
                float distanceToPath = DistanceToQuadraticBezier(position, startPoint, controlPoint, endPoint, 12);
                float riverInfluence = 1f - (distanceToPath / Mathf.Max(0.01f, pathWidth));

                float startLakeRadius = Mathf.Lerp(
                    riverEndpointLakeRadiusRange.x,
                    riverEndpointLakeRadiusRange.y,
                    Hash01(candidateCellX, candidateCellY, 7013));
                float endLakeRadius = Mathf.Lerp(
                    riverEndpointLakeRadiusRange.x,
                    riverEndpointLakeRadiusRange.y,
                    Hash01(candidateCellX, candidateCellY, 7027));

                float startLakeInfluence = 1f - ((position - startPoint).sqrMagnitude / Mathf.Max(0.001f, startLakeRadius * startLakeRadius));
                float endLakeInfluence = 1f - ((position - endPoint).sqrMagnitude / Mathf.Max(0.001f, endLakeRadius * endLakeRadius));

                bestInfluence = Mathf.Max(bestInfluence, riverInfluence, startLakeInfluence, endLakeInfluence);
            }
        }

        return bestInfluence;
    }

    private float SampleGuaranteedStartLake(Vector2Int worldCoordinate)
    {
        float distance = Mathf.Max(startSafeZoneRadius + 4f, starterTreeDistanceFromCenter + 1f);
        float radius = Mathf.Lerp(startLakeRadiusRange.x, startLakeRadiusRange.y, Hash01(0, 0, 8123));
        int directionIndex = Mathf.Clamp(Mathf.FloorToInt(Hash01(0, 0, 8159) * 4f), 0, 3);
        Vector2 direction = directionIndex switch
        {
            0 => Vector2.right,
            1 => Vector2.up,
            2 => Vector2.left,
            _ => Vector2.down
        };

        Vector2 center = direction * distance;
        float influence = 1f - (((new Vector2(worldCoordinate.x, worldCoordinate.y) - center).sqrMagnitude) / Mathf.Max(0.001f, radius * radius));
        return influence;
    }

    private Vector2 GetCellFeatureCenter(int cellX, int cellY, float cellSize, int salt)
    {
        float offsetX = Mathf.Lerp(0.2f, 0.8f, Hash01(cellX, cellY, salt));
        float offsetY = Mathf.Lerp(0.2f, 0.8f, Hash01(cellX, cellY, salt + 7));
        return new Vector2((cellX + offsetX) * cellSize, (cellY + offsetY) * cellSize);
    }

    private static float DistanceToQuadraticBezier(Vector2 point, Vector2 start, Vector2 control, Vector2 end, int segments)
    {
        int stepCount = Mathf.Max(4, segments);
        float bestDistance = float.MaxValue;
        Vector2 previous = start;

        for (int i = 1; i <= stepCount; i++)
        {
            float t = i / (float)stepCount;
            float oneMinusT = 1f - t;
            Vector2 current = (oneMinusT * oneMinusT * start)
                              + (2f * oneMinusT * t * control)
                              + (t * t * end);
            float distance = DistanceToLineSegment(point, previous, current);
            if (distance < bestDistance)
            {
                bestDistance = distance;
            }

            previous = current;
        }

        return bestDistance;
    }

    private static float DistanceToLineSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= Mathf.Epsilon)
        {
            return Vector2.Distance(point, start);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr);
        Vector2 projection = start + (segment * t);
        return Vector2.Distance(point, projection);
    }

    private Vector2 GetBiomeBlendJitter(Vector2Int worldCoordinate)
    {
        float jitterX = Mathf.Lerp(-terrainBlendJitter, terrainBlendJitter, Hash01(worldCoordinate.x, worldCoordinate.y, 8801));
        float jitterY = Mathf.Lerp(-terrainBlendJitter, terrainBlendJitter, Hash01(worldCoordinate.x, worldCoordinate.y, 8819));
        return new Vector2(jitterX, jitterY);
    }

    private void InvalidateTerrainBiomeDataCaches()
    {
        tileBiomeCache.Clear();
        rawWaterCache.Clear();
        directWaterBlockCache.Clear();
        bufferedWaterBlockCache.Clear();
    }

    private void InvalidateTerrainBiomeMaterialCaches()
    {
        foreach (KeyValuePair<TerrainBiome, Material> entry in biomeMaterialCache)
        {
            if (entry.Value == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(entry.Value);
            }
            else
            {
                DestroyImmediate(entry.Value);
            }
        }

        biomeMaterialCache.Clear();

        if (generatedSurfaceBlendMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(generatedSurfaceBlendMaterial);
            }
            else
            {
                DestroyImmediate(generatedSurfaceBlendMaterial);
            }

            generatedSurfaceBlendMaterial = null;
        }

        if (generatedSurfaceFoamMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(generatedSurfaceFoamMaterial);
            }
            else
            {
                DestroyImmediate(generatedSurfaceFoamMaterial);
            }

            generatedSurfaceFoamMaterial = null;
        }

    }

#if UNITY_EDITOR
    private const string SandBaseColorTexturePath = "Assets/Stylized_Terrain_VOL3/textures/sand_ground/T_sand_ground_basecolor.tga";
    private const string DirtBaseColorTexturePath = "Assets/Stylized_Terrain_VOL3/textures/earth/T_earth_basecolor.tga";
    private const string GrassBaseColorTexturePath = "Assets/Stylized_Terrain_VOL3/textures/grass_a/T_grass_a_basecolor.tga";
    private const string ForestBaseColorTexturePath = "Assets/Stylized_Terrain_VOL3/textures/grass_b/T_grass_b_basecolor.tga";

    private void PopulateGeneratedSurfaceBlendEditorDefaults()
    {
        if (generatedSurfaceBlendShader == null)
        {
            generatedSurfaceBlendShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TerrainBiomeBlend.shader");
        }

        if (generatedSurfaceFoamShader == null)
        {
            generatedSurfaceFoamShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TerrainWaterFoamOverlay.shader");
        }

        if (generatedSurfaceWaterMaterial == null)
        {
            generatedSurfaceWaterMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/M_ToonWater_Terrain.mat");
        }

        if (generatedSurfaceBlendTextureDefaultsInitialized)
        {
            return;
        }

        generatedSurfaceBlendTextureDefaultsInitialized = true;

        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendSandTexture,
            SandBaseColorTexturePath);

        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendDirtTexture,
            DirtBaseColorTexturePath);

        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendGrassTexture,
            GrassBaseColorTexturePath);
        if (generatedSurfaceBlendGrassTexture == null)
        {
            generatedSurfaceBlendGrassTexture = ResolveGeneratedSurfaceBlendTexture(
                null,
                ResolveSourceMaterialForBiome(TerrainBiome.Grass),
                "_BaseMap",
                "_MainTex");
        }

        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendForestTexture,
            ForestBaseColorTexturePath);
        if (generatedSurfaceBlendForestTexture == null)
        {
            generatedSurfaceBlendForestTexture = ResolveGeneratedSurfaceBlendTexture(
                null,
                ResolveSourceMaterialForBiome(TerrainBiome.Forest),
                "_BaseMap",
                "_MainTex");

            if (generatedSurfaceBlendForestTexture == null)
            {
                generatedSurfaceBlendForestTexture = generatedSurfaceBlendGrassTexture;
            }
        }
    }

    private static void AssignEditorTextureIfMissing(ref Texture2D texture, string defaultAssetPath)
    {
        if (texture != null)
        {
            return;
        }

        Texture2D defaultTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(defaultAssetPath);
        if (defaultTexture != null)
        {
            texture = defaultTexture;
        }
    }
#endif
}
