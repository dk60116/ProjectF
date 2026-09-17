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
    private struct IslandShapeEllipse
    {
        public Vector2 center;
        public Vector2 radii;
        public float rotationCos;
        public float rotationSin;
    }

    private sealed class IslandShapeProfile
    {
        public int seed;
        public IslandShapeEllipse mainBody;
        public IslandShapeEllipse[] lobes;
        public int lobeCount;
        public IslandShapeEllipse[] bays;
        public int bayCount;
        public IslandShapeEllipse[] satellites;
        public int satelliteCount;
        public float secondHarmonicPhase;
        public float thirdHarmonicPhase;
        public float fifthHarmonicPhase;
        public float secondHarmonicStrength;
        public float thirdHarmonicStrength;
        public float fifthHarmonicStrength;
    }

    private IslandShapeProfile islandShapeProfile;

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

        TerrainBiome biome = ResolveTileBiome(
            ResolveProfilingCloneTerrainSource(worldCoordinate));
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
        float protectedRadius = GetIslandProtectedRadius(halfSize);
        if (normalized.sqrMagnitude <= protectedRadius * protectedRadius)
        {
            return false;
        }

        IslandShapeProfile profile = GetIslandShapeProfile();
        float landScore = EvaluateIslandEllipse(normalized, profile.mainBody);
        for (int i = 0; i < profile.lobeCount; i++)
        {
            landScore = SmoothMaximum(
                landScore,
                EvaluateIslandEllipse(normalized, profile.lobes[i]),
                0.10f);
        }

        for (int i = 0; i < profile.satelliteCount; i++)
        {
            landScore = Mathf.Max(landScore, EvaluateIslandEllipse(normalized, profile.satellites[i]));
        }

        float angle = Mathf.Atan2(normalized.y, normalized.x);
        landScore += Mathf.Sin((angle * 2f) + profile.secondHarmonicPhase) * profile.secondHarmonicStrength;
        landScore += Mathf.Sin((angle * 3f) + profile.thirdHarmonicPhase) * profile.thirdHarmonicStrength;
        landScore += Mathf.Sin((angle * 5f) + profile.fifthHarmonicPhase) * profile.fifthHarmonicStrength;

        float bayProtectionRadius = Mathf.Max(0.18f, protectedRadius + 0.06f);
        if (normalized.sqrMagnitude > bayProtectionRadius * bayProtectionRadius)
        {
            for (int i = 0; i < profile.bayCount; i++)
            {
                float bayScore = EvaluateIslandEllipse(normalized, profile.bays[i]);
                if (bayScore > -0.12f)
                {
                    landScore = SmoothMinimum(
                        landScore,
                        0.025f - (bayScore * 0.72f),
                        0.10f);
                }
            }
        }

        float primaryNoise = SampleNoise(worldCoordinate, IslandCoastNoiseScale, new Vector2(187.4f, 58.6f));
        float detailNoise = SampleNoise(worldCoordinate, IslandCoastDetailNoiseScale, new Vector2(643.2f, 911.7f));
        float coastNoise = ((primaryNoise * 0.82f) + (detailNoise * 0.18f) - 0.5f) * IslandCoastIrregularity;
        return landScore + coastNoise < 0f;
    }

    private IslandShapeProfile GetIslandShapeProfile()
    {
        EnsureSeedInitialized();
        if (islandShapeProfile != null && islandShapeProfile.seed == seed)
        {
            return islandShapeProfile;
        }

        IslandShapeProfile profile = new IslandShapeProfile
        {
            seed = seed,
            lobes = new IslandShapeEllipse[IslandShapeLobeCapacity],
            bays = new IslandShapeEllipse[IslandShapeBayCapacity],
            satellites = new IslandShapeEllipse[IslandShapeSatelliteCapacity]
        };

        float mainRotation = Hash01(0, 0, 10009) * Mathf.PI * 2f;
        float mainAspect = Mathf.Lerp(0.68f, 1.42f, Hash01(0, 0, 10037));
        float mainAspectRoot = Mathf.Sqrt(mainAspect);
        float mainRadius = Mathf.Lerp(0.64f, 0.71f, Hash01(0, 0, 10061));
        Vector2 mainCenter = new Vector2(
            Mathf.Lerp(-0.055f, 0.055f, Hash01(0, 0, 10067)),
            Mathf.Lerp(-0.055f, 0.055f, Hash01(0, 0, 10069)));
        profile.mainBody = CreateIslandEllipse(
            mainCenter,
            new Vector2(mainRadius * mainAspectRoot, mainRadius / mainAspectRoot),
            mainRotation);

        profile.lobeCount = 2 + Mathf.Clamp(Mathf.FloorToInt(Hash01(0, 0, 10103) * 3f), 0, 2);
        for (int i = 0; i < profile.lobeCount; i++)
        {
            float angle = Hash01(i, 1, 10111) * Mathf.PI * 2f;
            float distance = Mathf.Lerp(0.31f, 0.55f, Hash01(i, 1, 10133));
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 center = mainCenter + (direction * distance);
            float radialRadius = Mathf.Lerp(0.28f, 0.43f, Hash01(i, 1, 10139));
            float tangentRadius = Mathf.Lerp(0.20f, 0.35f, Hash01(i, 1, 10141));
            float rotation = angle + Mathf.Lerp(-0.65f, 0.65f, Hash01(i, 1, 10151));
            profile.lobes[i] = CreateIslandEllipse(
                center,
                new Vector2(radialRadius, tangentRadius),
                rotation);
        }

        profile.bayCount = Hash01(0, 0, 10159) < 0.72f ? 1 : 2;
        float primaryBayAngle = Hash01(0, 2, 10163) * Mathf.PI * 2f;
        for (int i = 0; i < profile.bayCount; i++)
        {
            float angle = i == 0
                ? primaryBayAngle
                : primaryBayAngle + Mathf.Lerp(2.2f, 4.08f, Hash01(i, 2, 10167));
            float distance = Mathf.Lerp(0.57f, 0.71f, Hash01(i, 2, 10169));
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 center = mainCenter + (direction * distance);
            float radialRadius = Mathf.Lerp(0.39f, 0.54f, Hash01(i, 2, 10177));
            float tangentRadius = Mathf.Lerp(0.23f, 0.36f, Hash01(i, 2, 10181));
            float rotation = angle + Mathf.Lerp(-0.20f, 0.20f, Hash01(i, 2, 10193));
            profile.bays[i] = CreateIslandEllipse(
                center,
                new Vector2(radialRadius, tangentRadius),
                rotation);
        }

        float satelliteSelector = Hash01(0, 0, 10211);
        profile.satelliteCount = satelliteSelector < 0.28f ? 0 : satelliteSelector < 0.78f ? 1 : 2;
        for (int i = 0; i < profile.satelliteCount; i++)
        {
            float angle = Hash01(i, 3, 10223) * Mathf.PI * 2f;
            float distance = Mathf.Lerp(0.80f, 0.87f, Hash01(i, 3, 10243));
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 center = direction * distance;
            float radiusX = Mathf.Lerp(0.065f, 0.11f, Hash01(i, 3, 10247));
            float radiusY = Mathf.Lerp(0.055f, 0.10f, Hash01(i, 3, 10253));
            float rotation = Hash01(i, 3, 10259) * Mathf.PI * 2f;
            profile.satellites[i] = CreateIslandEllipse(center, new Vector2(radiusX, radiusY), rotation);
        }

        profile.secondHarmonicPhase = Hash01(0, 0, 10267) * Mathf.PI * 2f;
        profile.thirdHarmonicPhase = Hash01(0, 0, 10271) * Mathf.PI * 2f;
        profile.fifthHarmonicPhase = Hash01(0, 0, 10273) * Mathf.PI * 2f;
        profile.secondHarmonicStrength = Mathf.Lerp(0.025f, 0.065f, Hash01(0, 0, 10289));
        profile.thirdHarmonicStrength = Mathf.Lerp(0.035f, 0.080f, Hash01(0, 0, 10301));
        profile.fifthHarmonicStrength = Mathf.Lerp(0.008f, 0.022f, Hash01(0, 0, 10303));

        islandShapeProfile = profile;
        return profile;
    }

    private static IslandShapeEllipse CreateIslandEllipse(Vector2 center, Vector2 radii, float rotation)
    {
        return new IslandShapeEllipse
        {
            center = center,
            radii = new Vector2(Mathf.Max(0.001f, radii.x), Mathf.Max(0.001f, radii.y)),
            rotationCos = Mathf.Cos(rotation),
            rotationSin = Mathf.Sin(rotation)
        };
    }

    private static float EvaluateIslandEllipse(Vector2 normalizedCoordinate, IslandShapeEllipse ellipse)
    {
        Vector2 delta = normalizedCoordinate - ellipse.center;
        float rotatedX = (delta.x * ellipse.rotationCos) + (delta.y * ellipse.rotationSin);
        float rotatedY = (-delta.x * ellipse.rotationSin) + (delta.y * ellipse.rotationCos);
        float normalizedX = rotatedX / ellipse.radii.x;
        float normalizedY = rotatedY / ellipse.radii.y;
        return 1f - Mathf.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
    }

    private static float SmoothMinimum(float left, float right, float blendWidth)
    {
        float normalizedBlend = Mathf.Max(blendWidth - Mathf.Abs(left - right), 0f)
                                / Mathf.Max(0.0001f, blendWidth);
        return Mathf.Min(left, right) - ((normalizedBlend * normalizedBlend) * blendWidth * 0.25f);
    }

    private static float SmoothMaximum(float left, float right, float blendWidth)
    {
        return -SmoothMinimum(-left, -right, blendWidth);
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
        worldCoordinate = ResolveProfilingCloneTerrainSource(worldCoordinate);
        float jitterX = Mathf.Lerp(-terrainBlendJitter, terrainBlendJitter, Hash01(worldCoordinate.x, worldCoordinate.y, 8801));
        float jitterY = Mathf.Lerp(-terrainBlendJitter, terrainBlendJitter, Hash01(worldCoordinate.x, worldCoordinate.y, 8819));
        return new Vector2(jitterX, jitterY);
    }

    private void InvalidateTerrainBiomeDataCaches()
    {
        InvalidateAnimalNavigation();
        islandShapeProfile = null;
        tileBiomeCache.Clear();
        rawWaterCache.Clear();
        directWaterBlockCache.Clear();
        bufferedWaterBlockCache.Clear();
    }

    private void InvalidateTerrainBiomeMaterialCaches()
    {
        // GetGeneratedSurfaceMaterials caches the render array separately from
        // the materials below. Runtime map resets destroy those materials, so
        // retaining the array would leave terrain rendering bound to destroyed
        // Unity objects on the following frame.
        generatedSurfaceMaterials = null;

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
    private const string TerrainMapTexturePath = "Assets/Textures/Map/";
    private const string TerrainMapVariantTexturePath = "Assets/Resources/Textures/MapVariants/";

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

        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendSandTexture,
            TerrainMapTexturePath + "Sand.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendSandTexture2,
            TerrainMapVariantTexturePath + "Sand_02.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendSandTexture3,
            TerrainMapVariantTexturePath + "Sand_03.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendSandTexture4,
            TerrainMapVariantTexturePath + "Sand_04.png");

        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendDirtTexture,
            TerrainMapTexturePath + "Dirt.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendDirtTexture2,
            TerrainMapVariantTexturePath + "Dirt_02.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendDirtTexture3,
            TerrainMapVariantTexturePath + "Dirt_03.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendDirtTexture4,
            TerrainMapVariantTexturePath + "Dirt_04.png");

        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendGrassTexture,
            TerrainMapTexturePath + "Grass.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendGrassTexture2,
            TerrainMapVariantTexturePath + "Grass_02.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendGrassTexture3,
            TerrainMapVariantTexturePath + "Grass_03.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendGrassTexture4,
            TerrainMapVariantTexturePath + "Grass_04.png");
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
            TerrainMapTexturePath + "Forest.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendForestTexture2,
            TerrainMapVariantTexturePath + "Forest_02.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendForestTexture3,
            TerrainMapVariantTexturePath + "Forest_03.png");
        AssignEditorTextureIfMissing(
            ref generatedSurfaceBlendForestTexture4,
            TerrainMapVariantTexturePath + "Forest_04.png");
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
