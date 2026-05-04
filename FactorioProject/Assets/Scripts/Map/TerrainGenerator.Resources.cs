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
    private bool TryGetResourcePrefab(Vector2Int worldCoordinate, out Resource prefab)
    {
        prefab = null;

        if (keepStartSafeZoneClearOfResources && IsStartSafeZoneCoordinate(worldCoordinate))
        {
            return false;
        }

        if (generateStarterResourcePatches && TryGetStarterResourcePrefab(worldCoordinate, out prefab))
        {
            return true;
        }

        if (generateStarterTrees && TryGetStarterTreePrefab(worldCoordinate, out prefab))
        {
            return true;
        }

        float bestScore = float.MinValue;

        for (int i = 0; i < oreResources.Count; i++)
        {
            if (!TryEvaluateResourceEntry(worldCoordinate, oreResources[i], false, out float score))
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                prefab = oreResources[i].Prefab;
            }
        }

        if (prefab != null)
        {
            return true;
        }

        for (int i = 0; i < treeResources.Count; i++)
        {
            if (!TryEvaluateResourceEntry(worldCoordinate, treeResources[i], true, out float score))
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                prefab = treeResources[i].Prefab;
            }
        }

        return prefab != null;
    }

    private bool TryEvaluateResourceEntry(Vector2Int worldCoordinate, ResourceEntry entry, bool isTreeEntry, out float score)
    {
        score = float.MinValue;
        if (entry.Prefab == null || entry.spawnChance <= 0f)
        {
            return false;
        }

        if (isTreeEntry)
        {
            if (entry.placementMode == ResourcePlacementMode.Clustered)
            {
                return TryEvaluateSingleTreeResource(worldCoordinate, entry, out score);
            }

            return TryEvaluateTreePatchResource(worldCoordinate, entry, out score);
        }

        if (entry.placementMode == ResourcePlacementMode.Sparse)
        {
            return TryEvaluateSparseResource(worldCoordinate, entry, out score);
        }

        return TryEvaluateResourcePatch(worldCoordinate, ToResourceRule(entry), entry.spacingMultiplier, out score);
    }

    private int GetInitialResourceCount(Resource prefab, Vector2Int worldCoordinate)
    {
        if (prefab == null)
        {
            return 1;
        }

        if (TryGetMatchingResourceEntry(prefab, oreResources, out ResourceEntry oreEntry))
        {
            bool isStarterOre = generateStarterResourcePatches
                                && oreEntry.useStarterPatch
                                && IsInsideStarterPatch(
                                    worldCoordinate,
                                    GetStarterPatchCenter(oreEntry, Mathf.Max(startSafeZoneRadius + 2, starterPatchDistanceFromCenter)),
                                    Mathf.Max(2, starterPatchHalfSize * 2),
                                    oreEntry.salt + 4000);

            int minCount = isStarterOre ? oreEntry.starterMinResourceCount : oreEntry.minResourceCount;
            int maxCount = isStarterOre ? oreEntry.starterMaxResourceCount : oreEntry.maxResourceCount;
            return GetDeterministicRandomRange(worldCoordinate, prefab, minCount, maxCount);
        }

        if (TryGetMatchingResourceEntry(prefab, treeResources, out ResourceEntry treeEntry))
        {
            return GetDeterministicRandomRange(worldCoordinate, prefab, treeEntry.minResourceCount, treeEntry.maxResourceCount);
        }

        return 1;
    }

    private int GetResourceBodyYawStep(Resource prefab, Vector2Int worldCoordinate)
    {
        int prefabSalt = GetStableStringHash(prefab != null ? prefab.name : string.Empty);
        return Mathf.Clamp(Mathf.FloorToInt(Hash01(worldCoordinate.x, worldCoordinate.y, prefabSalt ^ 7349) * 8f), 0, 7);
    }

    private bool IsTreeResourcePrefab(Resource prefab)
    {
        for (int i = 0; i < treeResources.Count; i++)
        {
            if (treeResources[i].Prefab == prefab)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetMatchingResourceEntry(Resource prefab, List<ResourceEntry> entries, out ResourceEntry entry)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Prefab == prefab)
            {
                entry = entries[i];
                return true;
            }
        }

        entry = default;
        return false;
    }

    private int GetDeterministicRandomRange(Vector2Int worldCoordinate, Resource prefab, int minValue, int maxValue)
    {
        int normalizedMin = Mathf.Max(0, minValue);
        int normalizedMax = Mathf.Max(normalizedMin, maxValue);
        if (normalizedMin == normalizedMax)
        {
            return normalizedMin;
        }

        int prefabSalt = GetStableStringHash(prefab != null ? prefab.name : string.Empty);
        int hash = seed;
        hash = (hash * 397) ^ worldCoordinate.x;
        hash = (hash * 397) ^ worldCoordinate.y;
        hash = (hash * 397) ^ prefabSalt;

        int range = normalizedMax - normalizedMin + 1;
        return normalizedMin + Mathf.Abs(hash % range);
    }

    private static int GetStableStringHash(string value)
    {
        unchecked
        {
            int hash = 23;
            if (string.IsNullOrEmpty(value))
            {
                return hash;
            }

            for (int i = 0; i < value.Length; i++)
            {
                hash = (hash * 31) + value[i];
            }

            return hash;
        }
    }

    private bool TryGetStarterResourcePrefab(Vector2Int worldCoordinate, out Resource prefab)
    {
        prefab = null;

        int patchSize = Mathf.Max(2, starterPatchHalfSize * 2);

        for (int i = 0; i < oreResources.Count; i++)
        {
            ResourceEntry entry = oreResources[i];
            if (!entry.useStarterPatch || entry.Prefab == null)
            {
                continue;
            }

            int distance = Mathf.Max(startSafeZoneRadius + 2, starterPatchDistanceFromCenter);
            Vector2Int starterCenter = GetStarterPatchCenter(entry, distance);
            if (IsInsideStarterPatch(worldCoordinate, starterCenter, patchSize, entry.salt + 4000))
            {
                prefab = entry.Prefab;
                return true;
            }
        }

        return false;
    }

    private bool TryGetStarterTreePrefab(Vector2Int worldCoordinate, out Resource prefab)
    {
        prefab = null;

        EnsureStarterTreeCache();
        return starterTreeCacheLookup.TryGetValue(worldCoordinate, out prefab) && prefab != null;
    }

    private void InvalidateStarterTreeCache()
    {
        starterTreeCacheValid = false;
    }

    private int GetStarterTreeCacheConfigHash()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + seed;
            hash = (hash * 31) + (generateStarterTrees ? 1 : 0);
            hash = (hash * 31) + starterTreeMinCount;
            hash = (hash * 31) + starterTreeMaxCount;
            hash = (hash * 31) + starterTreeDistanceFromCenter;
            hash = (hash * 31) + startSafeZoneRadius;
            hash = (hash * 31) + starterPatchHalfSize;
            hash = (hash * 31) + starterPatchDistanceFromCenter;
            hash = (hash * 31) + (treeResources != null ? treeResources.Count : 0);
            return hash;
        }
    }

    private void EnsureStarterTreeCache()
    {
        EnsureSeedInitialized();
        int configHash = GetStarterTreeCacheConfigHash();

        if (starterTreeCacheValid && starterTreeCacheSeed == seed && starterTreeCacheConfigHash == configHash)
        {
            return;
        }

        starterTreeCacheSeed = seed;
        starterTreeCacheConfigHash = configHash;
        starterTreeCacheValid = true;
        starterTreeCacheEntries.Clear();
        starterTreeCacheLookup.Clear();

        if (!generateStarterTrees || treeResources == null)
        {
            return;
        }

        for (int i = 0; i < treeResources.Count; i++)
        {
            if (treeResources[i].Prefab != null)
            {
                starterTreeCacheEntries.Add(treeResources[i]);
            }
        }

        if (starterTreeCacheEntries.Count == 0)
        {
            return;
        }

        BuildStarterTreeCandidateOffsets(starterTreeCacheCandidates);

        if (starterTreeCacheCandidates.Count == 0)
        {
            return;
        }

        int selectedCount = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(starterTreeMinCount, starterTreeMaxCount, Hash01(seed, 991, 1777))),
            Mathf.Min(starterTreeMinCount, starterTreeCacheCandidates.Count),
            Mathf.Min(starterTreeMaxCount, starterTreeCacheCandidates.Count));

        for (int candidateIndex = 0; candidateIndex < starterTreeCacheCandidates.Count; candidateIndex++)
        {
            if (!IsStarterTreeCandidateSelected(starterTreeCacheCandidates, candidateIndex, selectedCount))
            {
                continue;
            }

            int treeIndex = Mathf.Abs(seed + candidateIndex) % starterTreeCacheEntries.Count;
            Resource prefab = starterTreeCacheEntries[treeIndex].Prefab;
            if (prefab != null)
            {
                starterTreeCacheLookup[starterTreeCacheCandidates[candidateIndex]] = prefab;
            }
        }
    }

    private void BuildStarterTreeCandidateOffsets(List<Vector2Int> candidates)
    {
        if (candidates == null)
        {
            return;
        }

        candidates.Clear();
        List<Vector2Int> preferredCandidates = GetStarterTreeCandidateOffsets();

        HashSet<Vector2Int> usedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < preferredCandidates.Count; i++)
        {
            if (TryResolveStarterTreeCandidateCoordinate(preferredCandidates[i], usedCoordinates, out Vector2Int resolvedCoordinate))
            {
                candidates.Add(resolvedCoordinate);
                usedCoordinates.Add(resolvedCoordinate);
            }
        }
    }

    private bool TryResolveStarterTreeCandidateCoordinate(Vector2Int preferredCoordinate, HashSet<Vector2Int> usedCoordinates, out Vector2Int resolvedCoordinate)
    {
        resolvedCoordinate = preferredCoordinate;
        const int searchRadius = 4;

        for (int radius = 0; radius <= searchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    {
                        continue;
                    }

                    Vector2Int candidateCoordinate = preferredCoordinate + new Vector2Int(offsetX, offsetY);
                    if (!IsValidStarterTreeCandidateCoordinate(candidateCoordinate, usedCoordinates))
                    {
                        continue;
                    }

                    resolvedCoordinate = candidateCoordinate;
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsValidStarterTreeCandidateCoordinate(Vector2Int coordinate, HashSet<Vector2Int> usedCoordinates)
    {
        if (usedCoordinates != null && usedCoordinates.Contains(coordinate))
        {
            return false;
        }

        if (IsInsideAnyStarterPatch(coordinate))
        {
            return false;
        }

        return CanSpawnResourceOnBiome(GetTileBiome(coordinate));
    }

    private bool IsInsideStarterPatch(Vector2Int worldCoordinate, Vector2Int center, int patchSize, int salt)
    {
        return EvaluatePatchShape(
            worldCoordinate,
            center,
            patchSize,
            patchSize,
            new Vector2(0.37f, 0.81f),
            salt,
            out _);
    }

    private bool TryEvaluateResourcePatch(Vector2Int worldCoordinate, ResourceRule rule, float spacingMultiplier, out float bestScore)
    {
        int baseCellSize = Mathf.Max(maximumResourcePatchSize + 4, resourcePatchCellSize);
        float spacing = Mathf.Max(1f, resourcePatchSpacing * Mathf.Max(1f, spacingMultiplier));
        return TryEvaluateResourcePatchWithSettings(worldCoordinate, rule, baseCellSize, spacing, 1f, 0f, out bestScore);
    }

    private bool TryEvaluateResourcePatchWithSettings(
        Vector2Int worldCoordinate,
        ResourceRule rule,
        int baseCellSize,
        float spacing,
        float presenceDensityScale,
        float scoreBias,
        out float bestScore)
    {
        bestScore = float.MinValue;
        int cellSize = Mathf.Max(baseCellSize, Mathf.RoundToInt(baseCellSize * spacing));
        int baseCellX = FloorDivide(worldCoordinate.x, cellSize);
        int baseCellY = FloorDivide(worldCoordinate.y, cellSize);
        bool found = false;

        for (int cellY = baseCellY - 1; cellY <= baseCellY + 1; cellY++)
        {
            for (int cellX = baseCellX - 1; cellX <= baseCellX + 1; cellX++)
            {
                if (!TryBuildResourcePatch(rule, cellX, cellY, cellSize, spacing, presenceDensityScale, out Vector2 center, out int width, out int height, out int salt))
                {
                    continue;
                }

                if (!EvaluatePatchShape(worldCoordinate, center, width, height, rule.detailOffset, salt, out float score))
                {
                    continue;
                }

                score += scoreBias;

                if (score > bestScore)
                {
                    bestScore = score;
                    found = true;
                }
            }
        }

        return found;
    }

    private bool TryEvaluateSparseResource(Vector2Int worldCoordinate, ResourceEntry entry, out float score)
    {
        score = float.MinValue;

        int baseCellSize = Mathf.Max(4, resourcePatchCellSize);
        float spacing = Mathf.Max(1.2f, resourcePatchSpacing * Mathf.Max(1f, entry.spacingMultiplier));
        int cellSize = Mathf.Max(baseCellSize, Mathf.RoundToInt(baseCellSize * spacing));
        int cellX = FloorDivide(worldCoordinate.x, cellSize);
        int cellY = FloorDivide(worldCoordinate.y, cellSize);

        float density = Mathf.Clamp01(entry.spawnChance * 8f * (1.8f / spacing));
        if (Hash01(cellX, cellY, entry.salt) > density)
        {
            return false;
        }

        int originX = cellX * cellSize;
        int originY = cellY * cellSize;
        int targetX = originX + Mathf.RoundToInt(Mathf.Lerp(1f, cellSize - 2f, Hash01(cellX, cellY, entry.salt + 11)));
        int targetY = originY + Mathf.RoundToInt(Mathf.Lerp(1f, cellSize - 2f, Hash01(cellX, cellY, entry.salt + 23)));

        if (worldCoordinate.x != targetX || worldCoordinate.y != targetY)
        {
            return false;
        }

        score = 1f;
        return true;
    }

    private bool TryEvaluateSingleTreeResource(Vector2Int worldCoordinate, ResourceEntry entry, out float score)
    {
        score = float.MinValue;

        int baseCellSize = 1;
        float spacing = Mathf.Max(1f, Mathf.Lerp(1f, 3.2f, Mathf.InverseLerp(1f, 6f, Mathf.Max(1f, entry.spacingMultiplier * 0.7f))));
        int cellSize = Mathf.Max(baseCellSize, Mathf.RoundToInt(baseCellSize * spacing));
        int cellX = FloorDivide(worldCoordinate.x, cellSize);
        int cellY = FloorDivide(worldCoordinate.y, cellSize);

        float normalizedChance = Mathf.Clamp01(entry.spawnChance);
        float chanceWeight = Mathf.Pow(normalizedChance, 1.35f);
        float densityBoost = Mathf.Lerp(1.2f, 2.6f, Mathf.Clamp01(resourceDensityMultiplier));
        float treeDensityWeight = Mathf.Lerp(0.85f, 1.35f, Mathf.InverseLerp(1f, 6f, Mathf.Max(1f, treeSingleDensityMultiplier)));
        float density = Mathf.Clamp01(chanceWeight * densityBoost * treeDensityWeight * (1.55f / spacing));
        if (Hash01(cellX, cellY, entry.salt) > density)
        {
            return false;
        }

        int originX = cellX * cellSize;
        int originY = cellY * cellSize;
        int targetX = originX + Mathf.RoundToInt(Mathf.Lerp(0.5f, cellSize - 0.5f, Hash01(cellX, cellY, entry.salt + 11)));
        int targetY = originY + Mathf.RoundToInt(Mathf.Lerp(0.5f, cellSize - 0.5f, Hash01(cellX, cellY, entry.salt + 23)));

        if (worldCoordinate.x != targetX || worldCoordinate.y != targetY)
        {
            return false;
        }

        score = (chanceWeight * 3f) + Hash01(cellX, cellY, entry.salt + 37) * 0.1f;
        return true;
    }

    private bool TryEvaluateTreePatchResource(Vector2Int worldCoordinate, ResourceEntry entry, out float score)
    {
        score = float.MinValue;
        float shapeMask = EvaluateTerrainDrivenTreeShapeMask(worldCoordinate, entry);
        if (shapeMask <= 0f)
        {
            return false;
        }

        float normalizedChance = Mathf.Clamp01(entry.spawnChance);
        float chanceWeight = Mathf.Pow(normalizedChance, 1.15f);
        float spacingWeight = Mathf.Lerp(1.35f, 0.65f, Mathf.InverseLerp(1f, 6f, Mathf.Max(1f, entry.spacingMultiplier)));
        float densityWeight = Mathf.Lerp(1f, 1.8f, Mathf.InverseLerp(1f, 6f, Mathf.Max(1f, treePatchDensityMultiplier)));
        float density = Mathf.Clamp01(chanceWeight * spacingWeight * densityWeight);

        float terrainDensityNoise = SampleTerrainDrivenTreeDensityNoise(worldCoordinate, entry);
        float patchSizeWeight = Mathf.Lerp(0.2f, 0.5f, Mathf.InverseLerp(1f, 3f, Mathf.Max(1f, treePatchSizeMultiplier)));
        float combined = Mathf.Clamp01((shapeMask * (1f - patchSizeWeight)) + (terrainDensityNoise * patchSizeWeight));
        float threshold = Mathf.Lerp(0.9f, 0.12f, density);
        threshold = Mathf.Lerp(threshold + 0.04f, threshold - 0.16f, shapeMask);
        if (combined < threshold)
        {
            return false;
        }

        score = (shapeMask * 2.2f) + combined + (normalizedChance * 0.45f);
        return true;
    }

    private bool TryBuildResourcePatch(
        ResourceRule rule,
        int cellX,
        int cellY,
        int cellSize,
        float spacingFactor,
        float presenceDensityScale,
        out Vector2 center,
        out int width,
        out int height,
        out int salt)
    {
        center = default;
        width = 0;
        height = 0;
        salt = rule.salt;

        float normalizedSpacing = Mathf.Max(0.35f, spacingFactor);
        float density = Mathf.Clamp01(rule.spawnChance
                                      * Mathf.Max(0.15f, resourceDensityMultiplier)
                                      * Mathf.Max(0.1f, presenceDensityScale)
                                      * (4.2f / normalizedSpacing));
        float presence = Hash01(cellX, cellY, rule.salt);
        if (presence > density)
        {
            return false;
        }

        int minSize = Mathf.Max(2, minimumResourcePatchSize);
        int maxSize = Mathf.Max(minSize, maximumResourcePatchSize);
        width = Mathf.RoundToInt(Mathf.Lerp(minSize, maxSize, Hash01(cellX, cellY, rule.salt + 11)));
        height = Mathf.RoundToInt(Mathf.Lerp(minSize, maxSize, Hash01(cellX, cellY, rule.salt + 29)));

        float originX = cellX * cellSize;
        float originY = cellY * cellSize;
        float jitterX = Mathf.Lerp(-cellSize * 0.25f, cellSize * 0.25f, Hash01(cellX, cellY, rule.salt + 41));
        float jitterY = Mathf.Lerp(-cellSize * 0.25f, cellSize * 0.25f, Hash01(cellX, cellY, rule.salt + 53));
        center = new Vector2(originX + cellSize * 0.5f + jitterX, originY + cellSize * 0.5f + jitterY);
        salt = rule.salt + cellX * 73856093 ^ cellY * 19349663;
        return true;
    }

    private bool EvaluatePatchShape(
        Vector2Int worldCoordinate,
        Vector2 center,
        int width,
        int height,
        Vector2 detailOffset,
        int salt,
        out float score)
    {
        float baseHalfWidth = Mathf.Max(1.2f, width * 0.5f);
        float baseHalfHeight = Mathf.Max(1.2f, height * 0.5f);
        float best = EvaluateEllipse(worldCoordinate, center, baseHalfWidth, baseHalfHeight);

        int lobeCount = 2 + Mathf.FloorToInt(Hash01(width + salt, height, salt + 7) * 3f);
        for (int i = 0; i < lobeCount; i++)
        {
            float angle = Hash01(i, salt, salt + 17) * Mathf.PI * 2f;
            float distance = Mathf.Lerp(0.16f, resourceClusterLobeSpread, Hash01(i, salt, salt + 31));
            Vector2 lobeOffset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 lobeCenter = center + new Vector2(lobeOffset.x * baseHalfWidth * distance, lobeOffset.y * baseHalfHeight * distance);
            float lobeHalfWidth = baseHalfWidth * Mathf.Lerp(0.45f, 0.8f, Hash01(i, salt, salt + 43));
            float lobeHalfHeight = baseHalfHeight * Mathf.Lerp(0.45f, 0.8f, Hash01(i, salt, salt + 59));
            best = Mathf.Max(best, EvaluateEllipse(worldCoordinate, lobeCenter, lobeHalfWidth, lobeHalfHeight));
        }

        float breakup = SampleNoise(
            worldCoordinate,
            resourcePatchScale * Mathf.Max(0.2f, resourceClusterBreakupScale),
            detailOffset + new Vector2(salt * 0.013f, salt * 0.021f));
        float micro = SampleNoise(
            worldCoordinate,
            resourceDetailScale * 2.1f,
            detailOffset + new Vector2(salt * 0.031f, salt * 0.017f));
        float holeThreshold = Mathf.Lerp(0.18f, 0.72f, resourceClusterSparsity);
        float breakupPenalty = breakup < holeThreshold ? (holeThreshold - breakup) * 1.15f : -0.04f;
        float microPenalty = micro < holeThreshold * 0.92f ? (holeThreshold * 0.92f - micro) * 0.45f : -0.015f;
        score = best - breakupPenalty - microPenalty;
        return score > 0f;
    }

    private static float EvaluateEllipse(Vector2 point, Vector2 center, float halfWidth, float halfHeight)
    {
        float normalizedX = (point.x - center.x) / Mathf.Max(0.01f, halfWidth);
        float normalizedY = (point.y - center.y) / Mathf.Max(0.01f, halfHeight);
        float radial = normalizedX * normalizedX + normalizedY * normalizedY;
        return 1f - radial;
    }

    private bool IsWaterTile(Vector2Int worldCoordinate)
    {
        if (IsBlockedForWater(worldCoordinate))
        {
            return false;
        }

        if (!IsWaterCandidate(worldCoordinate))
        {
            return false;
        }

        int orthogonalCount = GetOrthogonalCandidateWaterCount(worldCoordinate);
        bool preserveRiverTile = orthogonalCount >= 2 && HasRiverContinuitySupport(worldCoordinate);
        if (orthogonalCount <= 1)
        {
            return false;
        }

        bool north = IsWaterCandidate(worldCoordinate + Vector2Int.up);
        bool east = IsWaterCandidate(worldCoordinate + Vector2Int.right);
        bool south = IsWaterCandidate(worldCoordinate + Vector2Int.down);
        bool west = IsWaterCandidate(worldCoordinate + Vector2Int.left);

        if (!preserveRiverTile && orthogonalCount == 2 && ((north && south) || (east && west)))
        {
            return false;
        }

        if (!preserveRiverTile && HasDisconnectedDiagonalCandidate(worldCoordinate))
        {
            return false;
        }

        if (!preserveRiverTile && orthogonalCount <= 2 && !HasCandidateWaterSquareSupport(worldCoordinate))
        {
            return false;
        }

        return true;
    }

    private bool IsInsideAnyStarterPatch(Vector2Int worldCoordinate)
    {
        int patchSize = Mathf.Max(2, starterPatchHalfSize * 2);
        int distance = Mathf.Max(startSafeZoneRadius + 2, starterPatchDistanceFromCenter);

        for (int i = 0; i < oreResources.Count; i++)
        {
            ResourceEntry entry = oreResources[i];
            if (!entry.useStarterPatch || entry.Prefab == null)
            {
                continue;
            }

            if (IsInsideStarterPatch(worldCoordinate, GetStarterPatchCenter(entry, distance), patchSize, entry.salt + 4000))
            {
                return true;
            }
        }

        return false;
    }

    private List<Vector2Int> GetStarterTreeCandidateOffsets()
    {
        int baseRadius = Mathf.Max(startSafeZoneRadius + 1, starterTreeDistanceFromCenter);
        int targetCandidateCount = Mathf.Max(starterTreeMaxCount * 3, 24);
        int radialLayers = Mathf.Max(2, Mathf.CeilToInt(targetCandidateCount / 16f));
        float angleOffset = Hash01(seed, 1889, 733) * Mathf.PI * 2f;
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

        List<Vector2Int> candidates = new List<Vector2Int>(targetCandidateCount);
        HashSet<Vector2Int> uniqueCandidates = new HashSet<Vector2Int>();

        int spiralAttempts = Mathf.Max(targetCandidateCount * 8, 64);
        for (int attempt = 0; attempt < spiralAttempts && candidates.Count < targetCandidateCount; attempt++)
        {
            int layer = attempt % radialLayers;
            float radius = baseRadius + layer;
            float angle = angleOffset + (attempt * goldenAngle);
            Vector2Int coordinate = new Vector2Int(
                Mathf.RoundToInt(Mathf.Cos(angle) * radius),
                Mathf.RoundToInt(Mathf.Sin(angle) * radius));

            if (uniqueCandidates.Add(coordinate))
            {
                candidates.Add(coordinate);
            }
        }

        for (int radius = baseRadius;
             candidates.Count < targetCandidateCount && radius <= baseRadius + targetCandidateCount;
             radius++)
        {
            for (int offset = -radius; offset <= radius && candidates.Count < targetCandidateCount; offset++)
            {
                TryAddStarterTreeCandidateCoordinate(new Vector2Int(-radius, offset), uniqueCandidates, candidates);
                if (candidates.Count >= targetCandidateCount)
                {
                    break;
                }

                TryAddStarterTreeCandidateCoordinate(new Vector2Int(radius, offset), uniqueCandidates, candidates);
                if (candidates.Count >= targetCandidateCount)
                {
                    break;
                }

                TryAddStarterTreeCandidateCoordinate(new Vector2Int(offset, -radius), uniqueCandidates, candidates);
                if (candidates.Count >= targetCandidateCount)
                {
                    break;
                }

                TryAddStarterTreeCandidateCoordinate(new Vector2Int(offset, radius), uniqueCandidates, candidates);
            }
        }

        return candidates;
    }

    private void TryAddStarterTreeCandidateCoordinate(
        Vector2Int coordinate,
        HashSet<Vector2Int> uniqueCandidates,
        List<Vector2Int> candidates)
    {
        if (uniqueCandidates == null || candidates == null)
        {
            return;
        }

        if (uniqueCandidates.Add(coordinate))
        {
            candidates.Add(coordinate);
        }
    }

    private bool IsStarterTreeCandidateSelected(List<Vector2Int> candidates, int candidateIndex, int selectedCount)
    {
        float currentRank = Hash01(candidates[candidateIndex].x, candidates[candidateIndex].y, 5901);
        int betterCount = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (i == candidateIndex)
            {
                continue;
            }

            float otherRank = Hash01(candidates[i].x, candidates[i].y, 5901);
            if (otherRank > currentRank)
            {
                betterCount++;
            }
        }

        return betterCount < selectedCount;
    }

    private bool IsWaterCandidate(Vector2Int worldCoordinate)
    {
        if (IsBlockedForWater(worldCoordinate))
        {
            return false;
        }

        if (HasRiverContinuitySupport(worldCoordinate))
        {
            return true;
        }

        bool raw = IsRawWaterTile(worldCoordinate);
        int surroundingRawWater = GetSurroundingRawWaterCount(worldCoordinate);
        return raw ? surroundingRawWater >= 4 : surroundingRawWater >= 6;
    }

    private bool HasRiverContinuitySupport(Vector2Int worldCoordinate)
    {
        float center = SampleRiverLayer(worldCoordinate);
        if (center < 0.1f)
        {
            return false;
        }

        float north = SampleRiverLayer(worldCoordinate + Vector2Int.up);
        float east = SampleRiverLayer(worldCoordinate + Vector2Int.right);
        float south = SampleRiverLayer(worldCoordinate + Vector2Int.down);
        float west = SampleRiverLayer(worldCoordinate + Vector2Int.left);

        bool straightSupport = (north >= 0.12f && south >= 0.12f)
                               || (east >= 0.12f && west >= 0.12f);
        bool cornerSupport = (north >= 0.16f && east >= 0.16f)
                             || (east >= 0.16f && south >= 0.16f)
                             || (south >= 0.16f && west >= 0.16f)
                             || (west >= 0.16f && north >= 0.16f);

        return center >= 0.34f || (center >= 0.12f && (straightSupport || cornerSupport));
    }

    private bool IsRawWaterTile(Vector2Int worldCoordinate)
    {
        float primary = SampleNoise(worldCoordinate, waterNoiseScale, new Vector2(341.1f, 902.7f));
        float secondary = SampleNoise(worldCoordinate, waterNoiseScale * 1.65f, new Vector2(712.8f, 118.5f));
        float combined = primary * 0.84f + secondary * 0.16f;
        float threshold = Mathf.Lerp(0.82f, 0.56f, Mathf.Clamp01(waterFillPercent * 1.5f));
        return combined > threshold;
    }

    private int GetSurroundingRawWaterCount(Vector2Int worldCoordinate)
    {
        int count = 0;

        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0)
                {
                    continue;
                }

                Vector2Int neighborCoordinate = worldCoordinate + new Vector2Int(x, y);
                if (IsBlockedForWater(neighborCoordinate))
                {
                    continue;
                }

                if (IsRawWaterTile(neighborCoordinate))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private int GetOrthogonalCandidateWaterCount(Vector2Int worldCoordinate)
    {
        int count = 0;

        if (IsWaterCandidate(worldCoordinate + Vector2Int.up))
        {
            count++;
        }

        if (IsWaterCandidate(worldCoordinate + Vector2Int.right))
        {
            count++;
        }

        if (IsWaterCandidate(worldCoordinate + Vector2Int.down))
        {
            count++;
        }

        if (IsWaterCandidate(worldCoordinate + Vector2Int.left))
        {
            count++;
        }

        return count;
    }

    private bool IsBlockedForWater(Vector2Int worldCoordinate)
    {
        if (bufferedWaterBlockCache.TryGetValue(worldCoordinate, out bool cachedBlocked))
        {
            return cachedBlocked;
        }

        if (IsDirectlyBlockedForWater(worldCoordinate))
        {
            bufferedWaterBlockCache[worldCoordinate] = true;
            return true;
        }

        int exclusionRadius = Mathf.Max(0, starterWaterExclusionRadius);
        if (exclusionRadius <= 0)
        {
            bufferedWaterBlockCache[worldCoordinate] = false;
            return false;
        }

        for (int offsetY = -exclusionRadius; offsetY <= exclusionRadius; offsetY++)
        {
            for (int offsetX = -exclusionRadius; offsetX <= exclusionRadius; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }

                Vector2Int nearbyCoordinate = worldCoordinate + new Vector2Int(offsetX, offsetY);
                if (IsDirectlyBlockedForWater(nearbyCoordinate))
                {
                    bufferedWaterBlockCache[worldCoordinate] = true;
                    return true;
                }
            }
        }

        bufferedWaterBlockCache[worldCoordinate] = false;
        return false;
    }

    private bool IsDirectlyBlockedForWater(Vector2Int worldCoordinate)
    {
        if (directWaterBlockCache.TryGetValue(worldCoordinate, out bool cachedBlocked))
        {
            return cachedBlocked;
        }

        bool blocked = IsStartSafeZoneCoordinate(worldCoordinate)
                       || (generateStarterResourcePatches && IsInsideAnyStarterPatch(worldCoordinate))
                       || (generateStarterTrees && TryGetStarterTreePrefab(worldCoordinate, out _));
        directWaterBlockCache[worldCoordinate] = blocked;
        return blocked;
    }

    private void MigrateLegacyResourcesIfNeeded()
    {
        if (oreResources != null && oreResources.Count > 0)
        {
            return;
        }

        oreResources = new List<ResourceEntry>(4);
        AddLegacyResource("Iron", iron, ironSpawnChance, new Vector2(901.3f, 117.2f), new Vector2(77.6f, 401.7f), 101, true, Vector2Int.right);
        AddLegacyResource("Coal", coal, coalSpawnChance, new Vector2(451.2f, 772.8f), new Vector2(191.4f, 68.9f), 202, true, Vector2Int.up);
        AddLegacyResource("Stone", stone, stoneSpawnChance, new Vector2(137.9f, 251.6f), new Vector2(612.5f, 812.3f), 303, true, Vector2Int.left);
        AddLegacyResource("Cooper", cooper, cooperSpawnChance, new Vector2(623.4f, 528.6f), new Vector2(318.2f, 944.7f), 404, true, Vector2Int.down);
    }

    private void AddLegacyResource(
        string resourceName,
        Resource prefab,
        float spawnChance,
        Vector2 patchOffset,
        Vector2 detailOffset,
        int salt,
        bool useStarterPatch,
        Vector2Int starterDirection)
    {
        if (prefab == null)
        {
            return;
        }

        oreResources.Add(new ResourceEntry
        {
            name = resourceName,
            prefab = prefab,
            placementMode = ResourcePlacementMode.Clustered,
            spawnChance = spawnChance,
            spacingMultiplier = 1f,
            minResourceCount = normalOreMinResourceCount,
            maxResourceCount = normalOreMaxResourceCount,
            starterMinResourceCount = starterOreMinResourceCount,
            starterMaxResourceCount = starterOreMaxResourceCount,
            patchOffset = patchOffset,
            detailOffset = detailOffset,
            salt = salt,
            useStarterPatch = useStarterPatch,
            starterDirection = starterDirection
        });
    }

    private static void NormalizeResourceEntries(List<ResourceEntry> entries, int defaultMin, int defaultMax, int defaultStarterMin, int defaultStarterMax)
    {
        if (entries == null)
        {
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            ResourceEntry entry = entries[i];
            entry.minResourceCount = entry.minResourceCount <= 0 ? defaultMin : entry.minResourceCount;
            entry.maxResourceCount = entry.maxResourceCount <= 0 ? defaultMax : Mathf.Max(entry.minResourceCount, entry.maxResourceCount);
            entry.starterMinResourceCount = entry.starterMinResourceCount <= 0 ? defaultStarterMin : entry.starterMinResourceCount;
            entry.starterMaxResourceCount = entry.starterMaxResourceCount <= 0 ? defaultStarterMax : Mathf.Max(entry.starterMinResourceCount, entry.starterMaxResourceCount);
            entries[i] = entry;
        }
    }

    private static ResourceRule ToResourceRule(ResourceEntry entry)
    {
        return new ResourceRule(entry.Prefab, entry.spawnChance, entry.patchOffset, entry.detailOffset, entry.salt);
    }

    [ContextMenu("Sync Resource Definitions")]
    public void SyncResourceEntryDefinitions()
    {
#if UNITY_EDITOR
        SyncResourceEntryDefinitions(oreResources);
        SyncResourceEntryDefinitions(treeResources);
#endif
    }

#if UNITY_EDITOR
    private static void SyncResourceEntryDefinitions(List<ResourceEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return;
        }

        List<string> definitionSearchRoots = new List<string>();
        AddResourceDefinitionSearchFolder(definitionSearchRoots, "Assets/Data/Resources");
        AddResourceDefinitionSearchFolder(definitionSearchRoots, "Assets/Data/MapObject");
        AddResourceDefinitionSearchFolder(definitionSearchRoots, "Assets/Data/MapObjects");

        string[] definitionGuids = definitionSearchRoots.Count > 0
            ? AssetDatabase.FindAssets("t:ResourceDefinition", definitionSearchRoots.ToArray())
            : new string[0];
        Dictionary<string, ResourceDefinition> definitionsByPrefabPath = new Dictionary<string, ResourceDefinition>();

        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            ResourceDefinition definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path);
            if (definition == null || definition.prefab == null)
            {
                continue;
            }

            string prefabPath = AssetDatabase.GetAssetPath(definition.prefab);
            if (!string.IsNullOrWhiteSpace(prefabPath))
            {
                definitionsByPrefabPath[prefabPath] = definition;
            }
        }

        for (int i = 0; i < entries.Count; i++)
        {
            ResourceEntry entry = entries[i];
            if (entry.definition == null && entry.prefab != null)
            {
                string prefabPath = AssetDatabase.GetAssetPath(entry.prefab);
                if (!string.IsNullOrWhiteSpace(prefabPath)
                    && definitionsByPrefabPath.TryGetValue(prefabPath, out ResourceDefinition definition))
                {
                    entry.definition = definition;
                }
            }

            if (entry.definition != null && entry.prefab == null)
            {
                entry.prefab = entry.definition.prefab;
            }

            entries[i] = entry;
        }
    }

    private static void AddResourceDefinitionSearchFolder(List<string> folders, string folderPath)
    {
        if (folders == null || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder(folderPath) || folders.Contains(folderPath))
        {
            return;
        }

        folders.Add(folderPath);
    }
#endif

    private static Vector2Int GetStarterPatchCenter(ResourceEntry entry, int distance)
    {
        Vector2Int direction = entry.starterDirection;
        if (direction == Vector2Int.zero)
        {
            return Vector2Int.zero;
        }

        direction.x = Mathf.Clamp(direction.x, -1, 1);
        direction.y = Mathf.Clamp(direction.y, -1, 1);
        return new Vector2Int(direction.x * distance, direction.y * distance);
    }

    private bool HasCandidateWaterSquareSupport(Vector2Int worldCoordinate)
    {
        return (IsWaterCandidate(worldCoordinate + Vector2Int.left)
                && IsWaterCandidate(worldCoordinate + Vector2Int.down)
                && IsWaterCandidate(worldCoordinate + Vector2Int.left + Vector2Int.down))
               || (IsWaterCandidate(worldCoordinate + Vector2Int.right)
                   && IsWaterCandidate(worldCoordinate + Vector2Int.down)
                   && IsWaterCandidate(worldCoordinate + Vector2Int.right + Vector2Int.down))
               || (IsWaterCandidate(worldCoordinate + Vector2Int.left)
                   && IsWaterCandidate(worldCoordinate + Vector2Int.up)
                   && IsWaterCandidate(worldCoordinate + Vector2Int.left + Vector2Int.up))
               || (IsWaterCandidate(worldCoordinate + Vector2Int.right)
                   && IsWaterCandidate(worldCoordinate + Vector2Int.up)
                   && IsWaterCandidate(worldCoordinate + Vector2Int.right + Vector2Int.up));
    }

    private bool HasDisconnectedDiagonalCandidate(Vector2Int worldCoordinate)
    {
        bool north = IsWaterCandidate(worldCoordinate + Vector2Int.up);
        bool east = IsWaterCandidate(worldCoordinate + Vector2Int.right);
        bool south = IsWaterCandidate(worldCoordinate + Vector2Int.down);
        bool west = IsWaterCandidate(worldCoordinate + Vector2Int.left);

        bool northEast = IsWaterCandidate(worldCoordinate + Vector2Int.up + Vector2Int.right);
        bool southEast = IsWaterCandidate(worldCoordinate + Vector2Int.down + Vector2Int.right);
        bool southWest = IsWaterCandidate(worldCoordinate + Vector2Int.down + Vector2Int.left);
        bool northWest = IsWaterCandidate(worldCoordinate + Vector2Int.up + Vector2Int.left);

        return (northEast && !north && !east)
               || (southEast && !south && !east)
               || (southWest && !south && !west)
               || (northWest && !north && !west);
    }

    private void InitializeSeedForGeneration()
    {
        hasSeedInitialized = false;
        EnsureSeedInitialized();
    }

    private void EnsureSeedInitialized()
    {
        if (hasSeedInitialized)
        {
            return;
        }

        hasSeedInitialized = true;
    }

    private Vector2Int GetCenterChunkCoordinate()
    {
        EnsureSeedInitialized();
        ResolveTrackingTarget();

        Vector3 sourcePosition = trackingTarget != null ? trackingTarget.position : transform.position;
        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        int chunkX = Mathf.FloorToInt(sourcePosition.x / normalizedChunkSize);
        int chunkY = Mathf.FloorToInt(sourcePosition.z / normalizedChunkSize);
        return new Vector2Int(chunkX, chunkY);
    }

    private void ResolveTrackingTarget()
    {
        if (trackingTarget != null)
        {
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.Player != null)
        {
            trackingTarget = GameManager.Instance.Player.transform;
            return;
        }

        Player player = FindObjectOfType<Player>();
        if (player != null)
        {
            trackingTarget = player.transform;
        }
    }

    private bool IsStartSafeZoneCoordinate(Vector2Int worldCoordinate)
    {
        return Mathf.Abs(worldCoordinate.x) <= startSafeZoneRadius && Mathf.Abs(worldCoordinate.y) <= startSafeZoneRadius;
    }

    private float SampleNoise(Vector2Int worldCoordinate, float scale, Vector2 offset)
    {
        return SampleNoise(new Vector2(worldCoordinate.x, worldCoordinate.y), scale, offset);
    }

    private float SampleNoise(Vector2 worldCoordinate, float scale, Vector2 offset)
    {
        EnsureSeedInitialized();
        float seedOffsetX = (seed & 1023) * 0.03125f;
        float seedOffsetY = ((seed >> 10) & 1023) * 0.03125f;
        float sampleX = (worldCoordinate.x + offset.x + seedOffsetX) * scale;
        float sampleY = (worldCoordinate.y + offset.y + seedOffsetY) * scale;
        return Mathf.PerlinNoise(sampleX, sampleY);
    }

    private float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            uint hash = (uint)seed;
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

    private static int FloorDivide(int value, int divisor)
    {
        if (divisor == 0)
        {
            return 0;
        }

        if (value >= 0)
        {
            return value / divisor;
        }

        return ((value + 1) / divisor) - 1;
    }

    private static GameObject SelectBlockPrefab(BlockSet blockSet, bool isCorner)
    {
        if (blockSet.normal != null)
        {
            return blockSet.normal.gameObject;
        }

        return null;
    }
}
