using System;
using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public enum ResourcePlacementMode
    {
        Clustered,
        Sparse
    }

    [Serializable]
    public struct BlockSet
    {
        [SerializeField]
        private Block.BlockType type;

        public Block normal;
        public Block corner;

        public Block.BlockType Type => type;
    }

    [Serializable]
    private struct ResourceRule
    {
        public Resource prefab;
        public float spawnChance;
        public Vector2 patchOffset;
        public Vector2 detailOffset;
        public int salt;

        public ResourceRule(Resource prefab, float spawnChance, Vector2 patchOffset, Vector2 detailOffset, int salt)
        {
            this.prefab = prefab;
            this.spawnChance = spawnChance;
            this.patchOffset = patchOffset;
            this.detailOffset = detailOffset;
            this.salt = salt;
        }
    }

    [Serializable]
    public struct ResourceEntry
    {
        public string name;
        public Resource prefab;
        public ResourcePlacementMode placementMode;
        [Range(0f, 1f)] public float spawnChance;
        [Range(1f, 6f)] public float spacingMultiplier;
        [Min(1)] public int minResourceCount;
        [Min(1)] public int maxResourceCount;
        [Min(1)] public int starterMinResourceCount;
        [Min(1)] public int starterMaxResourceCount;
        public Vector2 patchOffset;
        public Vector2 detailOffset;
        public int salt;
        public bool useStarterPatch;
        public Vector2Int starterDirection;
    }

    [SerializeField]
    private List<BlockSet> blocks = new List<BlockSet>();

    [SerializeField]
    private List<ResourceEntry> oreResources = new List<ResourceEntry>();
    [SerializeField]
    private List<ResourceEntry> treeResources = new List<ResourceEntry>();

    [SerializeField, HideInInspector]
    private Resource stone;
    [SerializeField]
    private Resource coar;
    [SerializeField]
    [HideInInspector]
    private Resource iron;
    [SerializeField]
    [HideInInspector]
    private Resource cooper;

    [SerializeField, Min(4)]
    private int chunkSize = 16;

    [SerializeField, Min(0)]
    private int loadRadius = 2;

    [SerializeField, Min(1)]
    private int unloadRadius = 3;

    [SerializeField]
    private Transform trackingTarget;

    [SerializeField]
    private bool generateOnStart = true;

    [SerializeField]
    private bool useRandomSeed = true;

    [SerializeField]
    private int seed = 12345;

    [SerializeField, Range(0f, 1f)]
    private float waterFillPercent = 0.15f;

    [SerializeField, Min(0.001f)]
    private float waterNoiseScale = 0.08f;

    [SerializeField, Min(0)]
    private int startSafeZoneRadius = 2;

    [SerializeField]
    private bool keepStartSafeZoneClearOfResources = true;

    [SerializeField, Range(0f, 1f)]
    [HideInInspector]
    private float stoneSpawnChance = 0.08f;

    [SerializeField, Range(0f, 1f)]
    [HideInInspector]
    private float coarSpawnChance = 0.05f;

    [SerializeField, Range(0f, 1f)]
    [HideInInspector]
    private float ironSpawnChance = 0.04f;

    [SerializeField, Range(0f, 1f)]
    [HideInInspector]
    private float cooperSpawnChance = 0.05f;

    [SerializeField, Min(0.001f)]
    private float resourcePatchScale = 0.12f;

    [SerializeField, Min(0.001f)]
    private float resourceDetailScale = 0.14f;

    [SerializeField, Range(0f, 1f)]
    private float resourceDensityMultiplier = 0.6f;

    [SerializeField, Range(1f, 5f)]
    private float resourcePatchSpacing = 2.2f;

    [SerializeField, Range(0f, 1f)]
    private float resourceClusterSparsity = 0.45f;

    [SerializeField, Range(0.2f, 3f)]
    private float resourceClusterBreakupScale = 1.6f;

    [SerializeField, Range(0.2f, 0.9f)]
    private float resourceClusterLobeSpread = 0.55f;

    [SerializeField, Min(2)]
    private int minimumResourcePatchSize = 2;

    [SerializeField, Min(2)]
    private int maximumResourcePatchSize = 10;

    [SerializeField, Min(6)]
    private int resourcePatchCellSize = 16;

    [SerializeField]
    private bool generateStarterResourcePatches = true;

    [SerializeField, Min(1)]
    private int starterPatchHalfSize = 2;

    [SerializeField, Min(1)]
    private int starterPatchDistanceFromCenter = 5;

    [SerializeField]
    private bool generateStarterTrees = true;

    [SerializeField, Min(4)]
    private int starterTreeMinCount = 4;

    [SerializeField, Min(4)]
    private int starterTreeMaxCount = 6;

    [SerializeField, Min(2)]
    private int starterTreeDistanceFromCenter = 4;

    [SerializeField, Min(1), HideInInspector]
    private int starterOreMinResourceCount = 30;

    [SerializeField, Min(1), HideInInspector]
    private int starterOreMaxResourceCount = 50;

    [SerializeField, Min(1), HideInInspector]
    private int normalOreMinResourceCount = 100;

    [SerializeField, Min(1), HideInInspector]
    private int normalOreMaxResourceCount = 300;

    [SerializeField, Range(0f, 2f)]
    private float oreMinimumBodyScaleRatio = 0.3f;

    [SerializeField, Min(0.01f)]
    private float oreMaximumBodyScaleRatio = 2f;

    [SerializeField, Min(1)]
    private int oreScaleAtResourceCount = 300;

    private readonly Dictionary<Vector2Int, Transform> loadedChunks = new Dictionary<Vector2Int, Transform>();
    private readonly Dictionary<Vector2Int, Block> loadedBlocks = new Dictionary<Vector2Int, Block>();

    private bool hasGeneratedChunks;
    private bool hasSeedInitialized;
    private Vector2Int currentCenterChunk;
    private BlockStateStore resourceStateStore;

    private readonly List<ResourceEntry> starterTreeCacheEntries = new List<ResourceEntry>();
    private readonly List<Vector2Int> starterTreeCacheCandidates = new List<Vector2Int>();
    private readonly Dictionary<Vector2Int, Resource> starterTreeCacheLookup = new Dictionary<Vector2Int, Resource>();
    private int starterTreeCacheSeed = int.MinValue;
    private bool starterTreeCacheValid;

    private void OnValidate()
    {
        MigrateLegacyResourcesIfNeeded();
        starterOreMaxResourceCount = Mathf.Max(starterOreMinResourceCount, starterOreMaxResourceCount);
        normalOreMaxResourceCount = Mathf.Max(normalOreMinResourceCount, normalOreMaxResourceCount);
        oreMaximumBodyScaleRatio = Mathf.Max(oreMinimumBodyScaleRatio, oreMaximumBodyScaleRatio);
        oreScaleAtResourceCount = Mathf.Max(1, oreScaleAtResourceCount);
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        InvalidateStarterTreeCache();
    }

    private void Start()
    {
        MigrateLegacyResourcesIfNeeded();
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        EnsureResourceStateStore();

        if (generateOnStart)
        {
            Generate();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || !hasGeneratedChunks)
        {
            return;
        }

        RefreshTrackedChunks();
    }

    public void Generate()
    {
        MigrateLegacyResourcesIfNeeded();
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        EnsureResourceStateStore();
        InitializeSeedForGeneration();
        InvalidateStarterTreeCache();
        ClearLoadedChunks();
        resourceStateStore?.ClearStates();

        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        RefreshChunks(currentCenterChunk, true);
    }

    private void RefreshTrackedChunks()
    {
        Vector2Int centerChunk = GetCenterChunkCoordinate();
        if (centerChunk == currentCenterChunk)
        {
            return;
        }

        currentCenterChunk = centerChunk;
        RefreshChunks(currentCenterChunk, false);
    }

    private void RefreshChunks(Vector2Int centerChunk, bool forceReload)
    {
        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        int normalizedLoadRadius = Mathf.Max(0, loadRadius);
        int normalizedUnloadRadius = Mathf.Max(normalizedLoadRadius + 1, unloadRadius);

        for (int chunkY = centerChunk.y - normalizedLoadRadius; chunkY <= centerChunk.y + normalizedLoadRadius; chunkY++)
        {
            for (int chunkX = centerChunk.x - normalizedLoadRadius; chunkX <= centerChunk.x + normalizedLoadRadius; chunkX++)
            {
                Vector2Int chunkCoordinate = new Vector2Int(chunkX, chunkY);

                if (forceReload || !loadedChunks.ContainsKey(chunkCoordinate))
                {
                    GenerateChunk(chunkCoordinate, normalizedChunkSize);
                }
            }
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();

        foreach (KeyValuePair<Vector2Int, Transform> loadedChunk in loadedChunks)
        {
            int distanceX = Mathf.Abs(loadedChunk.Key.x - centerChunk.x);
            int distanceY = Mathf.Abs(loadedChunk.Key.y - centerChunk.y);

            if (distanceX > normalizedUnloadRadius || distanceY > normalizedUnloadRadius)
            {
                chunksToRemove.Add(loadedChunk.Key);
            }
        }

        for (int i = 0; i < chunksToRemove.Count; i++)
        {
            UnloadChunk(chunksToRemove[i]);
        }
    }

    private void GenerateChunk(Vector2Int chunkCoordinate, int normalizedChunkSize)
    {
        if (!TryGetBlockSet(Block.BlockType.Ground, out BlockSet groundSet))
        {
            return;
        }

        if (loadedChunks.TryGetValue(chunkCoordinate, out Transform existingChunk))
        {
            SaveChunkResourceStates(existingChunk);
            RemoveChunkBlocksFromLookup(existingChunk);
            DestroyChunkObject(existingChunk.gameObject);
            loadedChunks.Remove(chunkCoordinate);
        }

        bool hasWaterSet = TryGetBlockSet(Block.BlockType.Water, out BlockSet waterSet);
        Vector2Int origin = new Vector2Int(chunkCoordinate.x * normalizedChunkSize, chunkCoordinate.y * normalizedChunkSize);
        bool[,] chunkWaterMap = hasWaterSet ? BuildChunkWaterMap(origin, normalizedChunkSize) : null;
        GameObject chunkObject = new GameObject($"Chunk ({chunkCoordinate.x}, {chunkCoordinate.y})");
        chunkObject.transform.SetParent(transform, false);
        chunkObject.transform.position = new Vector3(origin.x, 0f, origin.y);
        loadedChunks.Add(chunkCoordinate, chunkObject.transform);

        for (int localY = 0; localY < normalizedChunkSize; localY++)
        {
            for (int localX = 0; localX < normalizedChunkSize; localX++)
            {
                Vector2Int worldCoordinate = new Vector2Int(origin.x + localX, origin.y + localY);
                Vector3 localPosition = new Vector3(localX, 0f, localY);
                int mapX = localX + 1;
                int mapY = localY + 1;
                bool isWaterTile = hasWaterSet && chunkWaterMap != null && chunkWaterMap[mapX, mapY];

                if (isWaterTile)
                {
                    bool isCorner = TryGetWaterCornerRotation(chunkWaterMap, mapX, mapY, out float waterRotation);
                    CreateBlock(chunkObject.transform, waterSet, Block.BlockType.Water, worldCoordinate, localPosition, isCorner, waterRotation);
                    continue;
                }

                Block groundBlock = CreateBlock(chunkObject.transform, groundSet, Block.BlockType.Ground, worldCoordinate, localPosition, false, 0f);
                if (groundBlock != null && TryGetResourcePrefab(worldCoordinate, out Resource resourcePrefab))
                {
                    SpawnResourceOnBlock(groundBlock, resourcePrefab, worldCoordinate);
                }
            }
        }
    }

    private void UnloadChunk(Vector2Int chunkCoordinate)
    {
        if (!loadedChunks.TryGetValue(chunkCoordinate, out Transform chunkTransform))
        {
            return;
        }

        SaveChunkResourceStates(chunkTransform);
        RemoveChunkBlocksFromLookup(chunkTransform);
        loadedChunks.Remove(chunkCoordinate);
        DestroyChunkObject(chunkTransform.gameObject);
    }

    private void ClearLoadedChunks()
    {
        List<Transform> chunkObjects = new List<Transform>(loadedChunks.Values);

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!chunkObjects.Contains(child))
            {
                chunkObjects.Add(child);
            }
        }

        for (int i = 0; i < chunkObjects.Count; i++)
        {
            if (chunkObjects[i] != null)
            {
                SaveChunkResourceStates(chunkObjects[i]);
                RemoveChunkBlocksFromLookup(chunkObjects[i]);
                DestroyChunkObject(chunkObjects[i].gameObject);
            }
        }

        loadedChunks.Clear();
        loadedBlocks.Clear();
    }

    private void DestroyChunkObject(GameObject chunkObject)
    {
        if (chunkObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(chunkObject);
        }
        else
        {
            DestroyImmediate(chunkObject);
        }
    }

    private bool TryGetBlockSet(Block.BlockType type, out BlockSet blockSet)
    {
        if (blocks != null)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].Type == type)
                {
                    blockSet = blocks[i];
                    return true;
                }
            }
        }

        blockSet = default;
        return false;
    }

    private Block CreateBlock(
        Transform parent,
        BlockSet blockSet,
        Block.BlockType blockType,
        Vector2Int coordinate,
        Vector3 localPosition,
        bool useCorner,
        float yRotation)
    {
        GameObject prefab = SelectBlockPrefab(blockSet, useCorner);
        if (prefab == null)
        {
            return null;
        }

        GameObject blockObject = Instantiate(prefab, parent);
        blockObject.transform.localPosition = localPosition;
        blockObject.transform.localRotation = Quaternion.identity;
        ApplyBodyRotation(blockObject, yRotation);

        Block block = blockObject.GetComponent<Block>();
        if (block == null)
        {
            block = blockObject.AddComponent<Block>();
        }

        block.Initialize(coordinate, blockType);
        loadedBlocks[coordinate] = block;
        RestoreBlockState(block);
        return block;
    }

    public bool TryAddDroppedItemAtPlayerBlock(Vector3 worldPosition, int itemId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        if (loadedBlocks.TryGetValue(centerCoordinate, out Block centerBlock) && centerBlock != null)
        {
            if (centerBlock.Type == Block.BlockType.Ground && centerBlock.TryAddFloorObject(itemId, out targetPortableObject))
            {
                MarkDroppedPickupGate(targetPortableObject);
                return true;
            }
        }

        return false;
    }

    public bool TryAddDroppedItemStackAtPlayerBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        float moveInterval = 0.1f)
    {
        if (itemCount <= 0)
        {
            return false;
        }

        Block targetBlock = FindPreferredDropBlock(worldPosition, itemId, itemCount);
        if (targetBlock == null)
        {
            return false;
        }

        for (int i = 0; i < itemCount; i++)
        {
            if (!targetBlock.TryAddFloorObjectAnimated(itemId, startWorldPosition, i * Mathf.Max(0f, moveInterval), out PortableObject droppedObject))
            {
                return false;
            }

            MarkDroppedPickupGate(droppedObject);
        }

        return true;
    }

    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
    {
        if (loadedBlocks.TryGetValue(coordinate, out block))
        {
            if (block == null)
            {
                loadedBlocks.Remove(coordinate);
                return false;
            }

            return true;
        }

        return false;
    }

    public bool TryAddDroppedItemNear(Vector3 worldPosition, int itemId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        const int maxSearchRadius = 2;
        for (int radius = 0; radius <= maxSearchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (radius > 0 && Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    {
                        continue;
                    }

                    Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                    if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                    {
                        loadedBlocks.Remove(coordinate);
                        continue;
                    }

                    if (block.Type != Block.BlockType.Ground)
                    {
                        continue;
                    }

                    if (block.TryAddFloorObject(itemId, out targetPortableObject))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void SpawnResourceOnBlock(Block block, Resource prefab, Vector2Int worldCoordinate)
    {
        if (block == null || prefab == null)
        {
            return;
        }

        EnsureResourceStateStore();
        if (resourceStateStore != null && resourceStateStore.IsDepleted(worldCoordinate))
        {
            block.SetMapObject(null);
            return;
        }

        Resource spawnedResource = Instantiate(prefab, block.transform);
        spawnedResource.transform.localPosition = Vector3.zero;
        spawnedResource.transform.localRotation = Quaternion.identity;
        ApplyResourceScaleProfile(spawnedResource, prefab);

        if (resourceStateStore != null && resourceStateStore.TryGet(worldCoordinate, out Resource.ResourceSaveState savedState))
        {
            spawnedResource.ApplySavedState(savedState);
        }
        else
        {
            spawnedResource.InitializeRuntimeQuantity(GetInitialResourceCount(prefab, worldCoordinate));
        }

        block.SetMapObject(spawnedResource);
    }

    private void ApplyResourceScaleProfile(Resource spawnedResource, Resource prefab)
    {
        if (spawnedResource == null)
        {
            return;
        }

        if (IsTreeResourcePrefab(prefab))
        {
            spawnedResource.ConfigureDynamicBodyScale(1f, 1f, 1);
            return;
        }

        spawnedResource.ConfigureDynamicBodyScale(
            oreMinimumBodyScaleRatio,
            oreMaximumBodyScaleRatio,
            oreScaleAtResourceCount);
    }

    private void SaveChunkResourceStates(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        Block[] chunkBlocks = chunkTransform.GetComponentsInChildren<Block>(true);
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            resourceStateStore.SaveFloorObjects(chunkBlocks[i].Coordinate, chunkBlocks[i]);

            Resource resource = chunkBlocks[i].Resource;
            if (resource == null)
            {
                continue;
            }

            resourceStateStore.Save(chunkBlocks[i].Coordinate, resource);
        }
    }

    private void RemoveChunkBlocksFromLookup(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return;
        }

        Block[] chunkBlocks = chunkTransform.GetComponentsInChildren<Block>(true);
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            Block block = chunkBlocks[i];
            if (block == null)
            {
                continue;
            }

            if (loadedBlocks.TryGetValue(block.Coordinate, out Block loadedBlock) && loadedBlock == block)
            {
                loadedBlocks.Remove(block.Coordinate);
            }
        }
    }

    private Block FindPreferredDropBlock(Vector3 worldPosition, int itemId, int itemCount)
    {
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        Block sameItemBlock = FindNearestDropBlock(centerCoordinate, itemId, itemCount, 3, true);
        if (sameItemBlock != null)
        {
            return sameItemBlock;
        }

        if (loadedBlocks.TryGetValue(centerCoordinate, out Block centerBlock)
            && IsValidDropBlock(centerBlock, itemId, itemCount))
        {
            return centerBlock;
        }

        return null;
    }

    private Block FindNearestDropBlock(Vector2Int centerCoordinate, int itemId, int itemCount, int radius, bool requireSameItem)
    {
        Block bestBlock = null;
        int bestDistance = int.MaxValue;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(x, y);
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (!IsValidDropBlock(block, itemId, itemCount))
                {
                    continue;
                }

                if (requireSameItem && !block.HasFloorObjectItem(itemId))
                {
                    continue;
                }

                int distance = Mathf.Abs(x) + Mathf.Abs(y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestBlock = block;
                }
            }
        }

        return bestBlock;
    }

    private static bool IsValidDropBlock(Block block, int itemId, int itemCount)
    {
        return block != null
               && block.Type == Block.BlockType.Ground
               && block.CanAddFloorObjects(itemCount, itemId);
    }

    private static void MarkDroppedPickupGate(PortableObject droppedObject)
    {
        if (droppedObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = droppedObject.GetComponent<DroppedItemPickupGate>();
        if (gate == null)
        {
            gate = droppedObject.gameObject.AddComponent<DroppedItemPickupGate>();
        }

        gate.MarkDropped();
    }

    private static Vector2Int GetWorldBlockCoordinate(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
    }

    private bool[,] BuildChunkWaterMap(Vector2Int origin, int chunkLength)
    {
        const int margin = 1;
        int mapLength = chunkLength + (margin * 2);
        bool[,] map = new bool[mapLength, mapLength];
        bool[,] nextMap = new bool[mapLength, mapLength];

        for (int y = 0; y < mapLength; y++)
        {
            for (int x = 0; x < mapLength; x++)
            {
                Vector2Int worldCoordinate = new Vector2Int(origin.x + x - margin, origin.y + y - margin);
                map[x, y] = IsWaterTile(worldCoordinate);
            }
        }

        bool changed;
        int safetyIteration = 0;
        do
        {
            changed = false;
            safetyIteration++;

            for (int y = 0; y < mapLength; y++)
            {
                for (int x = 0; x < mapLength; x++)
                {
                    if (!map[x, y])
                    {
                        nextMap[x, y] = false;
                        continue;
                    }

                    int orthogonalCount = GetOrthogonalWaterCount(map, x, y);
                    bool north = GetWaterMapValue(map, x, y + 1);
                    bool east = GetWaterMapValue(map, x + 1, y);
                    bool south = GetWaterMapValue(map, x, y - 1);
                    bool west = GetWaterMapValue(map, x - 1, y);

                    bool shouldRemove =
                        orthogonalCount <= 1
                        || (orthogonalCount == 2 && ((north && south) || (east && west)))
                        || HasDisconnectedDiagonal(map, x, y)
                        || (orthogonalCount <= 2 && !HasWaterSquareSupport(map, x, y));

                     if (!shouldRemove)
                     {
                        nextMap[x, y] = true;
                        continue;
                     }

                    nextMap[x, y] = false;
                    changed = true;
                }
            }

            bool[,] swap = map;
            map = nextMap;
            nextMap = swap;
        }
        while (changed && safetyIteration < 8);

        return map;
    }

    private void RestoreBlockState(Block block)
    {
        if (block == null)
        {
            return;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        if (resourceStateStore.TryGetFloorObjects(block.Coordinate, out List<int> itemIds))
        {
            block.ApplyFloorObjectState(itemIds);
        }
    }

    private void EnsureResourceStateStore()
    {
        if (resourceStateStore != null)
        {
            return;
        }

        resourceStateStore = GetComponent<BlockStateStore>();
        if (resourceStateStore == null)
        {
            resourceStateStore = gameObject.AddComponent<BlockStateStore>();
        }
    }

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
            if (!TryEvaluateResourceEntry(worldCoordinate, oreResources[i], out float score))
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                prefab = oreResources[i].prefab;
            }
        }

        if (prefab != null)
        {
            return true;
        }

        for (int i = 0; i < treeResources.Count; i++)
        {
            if (!TryEvaluateResourceEntry(worldCoordinate, treeResources[i], out float score))
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                prefab = treeResources[i].prefab;
            }
        }

        return prefab != null;
    }

    private bool TryEvaluateResourceEntry(Vector2Int worldCoordinate, ResourceEntry entry, out float score)
    {
        score = float.MinValue;
        if (entry.prefab == null || entry.spawnChance <= 0f)
        {
            return false;
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

    private bool IsTreeResourcePrefab(Resource prefab)
    {
        for (int i = 0; i < treeResources.Count; i++)
        {
            if (treeResources[i].prefab == prefab)
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
            if (entries[i].prefab == prefab)
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
            if (!entry.useStarterPatch || entry.prefab == null)
            {
                continue;
            }

            int distance = Mathf.Max(startSafeZoneRadius + 2, starterPatchDistanceFromCenter);
            Vector2Int starterCenter = GetStarterPatchCenter(entry, distance);
            if (IsInsideStarterPatch(worldCoordinate, starterCenter, patchSize, entry.salt + 4000))
            {
                prefab = entry.prefab;
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

    private void EnsureStarterTreeCache()
    {
        EnsureSeedInitialized();

        if (starterTreeCacheValid && starterTreeCacheSeed == seed)
        {
            return;
        }

        starterTreeCacheSeed = seed;
        starterTreeCacheValid = true;
        starterTreeCacheEntries.Clear();
        starterTreeCacheLookup.Clear();

        if (!generateStarterTrees || treeResources == null)
        {
            return;
        }

        for (int i = 0; i < treeResources.Count; i++)
        {
            if (treeResources[i].prefab != null)
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
            Resource prefab = starterTreeCacheEntries[treeIndex].prefab;
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
        int primary = Mathf.Max(startSafeZoneRadius + 1, starterTreeDistanceFromCenter);
        int secondary = Mathf.Max(2, primary - 1);

        candidates.Add(new Vector2Int(primary, primary));
        candidates.Add(new Vector2Int(-primary, primary));
        candidates.Add(new Vector2Int(primary, -primary));
        candidates.Add(new Vector2Int(-primary, -primary));
        candidates.Add(new Vector2Int(primary, secondary));
        candidates.Add(new Vector2Int(-primary, secondary));
        candidates.Add(new Vector2Int(primary, -secondary));
        candidates.Add(new Vector2Int(-primary, -secondary));
        candidates.Add(new Vector2Int(secondary, primary));
        candidates.Add(new Vector2Int(-secondary, primary));
        candidates.Add(new Vector2Int(secondary, -primary));
        candidates.Add(new Vector2Int(-secondary, -primary));
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
        bestScore = float.MinValue;
        int baseCellSize = Mathf.Max(maximumResourcePatchSize + 4, resourcePatchCellSize);
        float spacing = Mathf.Max(1f, resourcePatchSpacing * Mathf.Max(1f, spacingMultiplier));
        int cellSize = Mathf.Max(baseCellSize, Mathf.RoundToInt(baseCellSize * spacing));
        int baseCellX = FloorDivide(worldCoordinate.x, cellSize);
        int baseCellY = FloorDivide(worldCoordinate.y, cellSize);
        bool found = false;

        for (int cellY = baseCellY - 1; cellY <= baseCellY + 1; cellY++)
        {
            for (int cellX = baseCellX - 1; cellX <= baseCellX + 1; cellX++)
            {
                if (!TryBuildResourcePatch(rule, cellX, cellY, cellSize, out Vector2 center, out int width, out int height, out int salt))
                {
                    continue;
                }

                if (!EvaluatePatchShape(worldCoordinate, center, width, height, rule.detailOffset, salt, out float score))
                {
                    continue;
                }

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

    private bool TryBuildResourcePatch(
        ResourceRule rule,
        int cellX,
        int cellY,
        int cellSize,
        out Vector2 center,
        out int width,
        out int height,
        out int salt)
    {
        center = default;
        width = 0;
        height = 0;
        salt = rule.salt;

        float spacingFactor = Mathf.Max(1f, resourcePatchSpacing);
        float density = Mathf.Clamp01(rule.spawnChance * Mathf.Max(0.15f, resourceDensityMultiplier) * (4.2f / spacingFactor));
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
        if (orthogonalCount <= 1)
        {
            return false;
        }

        bool north = IsWaterCandidate(worldCoordinate + Vector2Int.up);
        bool east = IsWaterCandidate(worldCoordinate + Vector2Int.right);
        bool south = IsWaterCandidate(worldCoordinate + Vector2Int.down);
        bool west = IsWaterCandidate(worldCoordinate + Vector2Int.left);

        if (orthogonalCount == 2 && ((north && south) || (east && west)))
        {
            return false;
        }

        if (HasDisconnectedDiagonalCandidate(worldCoordinate))
        {
            return false;
        }

        if (orthogonalCount <= 2 && !HasCandidateWaterSquareSupport(worldCoordinate))
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
            if (!entry.useStarterPatch || entry.prefab == null)
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

    private List<ResourceEntry> GetStarterTreeEntries()
    {
        List<ResourceEntry> entries = new List<ResourceEntry>();

        for (int i = 0; i < treeResources.Count; i++)
        {
            if (treeResources[i].prefab != null)
            {
                entries.Add(treeResources[i]);
            }
        }

        return entries;
    }

    private List<Vector2Int> GetStarterTreeCandidateOffsets()
    {
        int primary = Mathf.Max(startSafeZoneRadius + 1, starterTreeDistanceFromCenter);
        int secondary = Mathf.Max(2, primary - 1);

        return new List<Vector2Int>
        {
            new Vector2Int(primary, primary),
            new Vector2Int(-primary, primary),
            new Vector2Int(primary, -primary),
            new Vector2Int(-primary, -primary),
            new Vector2Int(primary, secondary),
            new Vector2Int(-primary, secondary),
            new Vector2Int(primary, -secondary),
            new Vector2Int(-primary, -secondary),
            new Vector2Int(secondary, primary),
            new Vector2Int(-secondary, primary),
            new Vector2Int(secondary, -primary),
            new Vector2Int(-secondary, -primary)
        };
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

        bool raw = IsRawWaterTile(worldCoordinate);
        int surroundingRawWater = GetSurroundingRawWaterCount(worldCoordinate);
        return raw ? surroundingRawWater >= 4 : surroundingRawWater >= 6;
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
        return IsStartSafeZoneCoordinate(worldCoordinate)
               || (generateStarterResourcePatches && IsInsideAnyStarterPatch(worldCoordinate))
               || (generateStarterTrees && TryGetStarterTreePrefab(worldCoordinate, out _));
    }

    private void MigrateLegacyResourcesIfNeeded()
    {
        if (oreResources != null && oreResources.Count > 0)
        {
            return;
        }

        oreResources = new List<ResourceEntry>(4);
        AddLegacyResource("Iron", iron, ironSpawnChance, new Vector2(901.3f, 117.2f), new Vector2(77.6f, 401.7f), 101, true, Vector2Int.right);
        AddLegacyResource("Coal", coar, coarSpawnChance, new Vector2(451.2f, 772.8f), new Vector2(191.4f, 68.9f), 202, true, Vector2Int.up);
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
        return new ResourceRule(entry.prefab, entry.spawnChance, entry.patchOffset, entry.detailOffset, entry.salt);
    }

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

    private bool TryGetWaterCornerRotation(Vector2Int worldCoordinate, out float yRotation)
    {
        yRotation = 0f;

        bool north = IsWaterTile(worldCoordinate + Vector2Int.up);
        bool east = IsWaterTile(worldCoordinate + Vector2Int.right);
        bool south = IsWaterTile(worldCoordinate + Vector2Int.down);
        bool west = IsWaterTile(worldCoordinate + Vector2Int.left);
        int orthogonalCount = (north ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (west ? 1 : 0);

        if (orthogonalCount != 2)
        {
            return false;
        }

        if (east && south)
        {
            yRotation = 0f;
            return true;
        }

        if (west && south)
        {
            yRotation = 90f;
            return true;
        }

        if (west && north)
        {
            yRotation = 180f;
            return true;
        }

        if (east && north)
        {
            yRotation = -90f;
            return true;
        }

        return false;
    }

    private bool TryGetWaterCornerRotation(bool[,] map, int x, int y, out float yRotation)
    {
        yRotation = 0f;

        bool north = GetWaterMapValue(map, x, y + 1);
        bool east = GetWaterMapValue(map, x + 1, y);
        bool south = GetWaterMapValue(map, x, y - 1);
        bool west = GetWaterMapValue(map, x - 1, y);
        int orthogonalCount = (north ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (west ? 1 : 0);

        if (orthogonalCount != 2)
        {
            return false;
        }

        if (east && south)
        {
            yRotation = 0f;
            return true;
        }

        if (west && south)
        {
            yRotation = 90f;
            return true;
        }

        if (west && north)
        {
            yRotation = 180f;
            return true;
        }

        if (east && north)
        {
            yRotation = -90f;
            return true;
        }

        return false;
    }

    private static bool GetWaterMapValue(bool[,] map, int x, int y)
    {
        if (map == null)
        {
            return false;
        }

        if (x < 0 || y < 0 || x >= map.GetLength(0) || y >= map.GetLength(1))
        {
            return false;
        }

        return map[x, y];
    }

    private static int GetOrthogonalWaterCount(bool[,] map, int x, int y)
    {
        int count = 0;

        if (GetWaterMapValue(map, x, y + 1))
        {
            count++;
        }

        if (GetWaterMapValue(map, x + 1, y))
        {
            count++;
        }

        if (GetWaterMapValue(map, x, y - 1))
        {
            count++;
        }

        if (GetWaterMapValue(map, x - 1, y))
        {
            count++;
        }

        return count;
    }

    private static bool HasWaterSquareSupport(bool[,] map, int x, int y)
    {
        return (GetWaterMapValue(map, x - 1, y)
                && GetWaterMapValue(map, x, y - 1)
                && GetWaterMapValue(map, x - 1, y - 1))
               || (GetWaterMapValue(map, x + 1, y)
                   && GetWaterMapValue(map, x, y - 1)
                   && GetWaterMapValue(map, x + 1, y - 1))
               || (GetWaterMapValue(map, x - 1, y)
                   && GetWaterMapValue(map, x, y + 1)
                   && GetWaterMapValue(map, x - 1, y + 1))
               || (GetWaterMapValue(map, x + 1, y)
                   && GetWaterMapValue(map, x, y + 1)
                   && GetWaterMapValue(map, x + 1, y + 1));
    }

    private static bool HasDisconnectedDiagonal(bool[,] map, int x, int y)
    {
        bool north = GetWaterMapValue(map, x, y + 1);
        bool east = GetWaterMapValue(map, x + 1, y);
        bool south = GetWaterMapValue(map, x, y - 1);
        bool west = GetWaterMapValue(map, x - 1, y);

        bool northEast = GetWaterMapValue(map, x + 1, y + 1);
        bool southEast = GetWaterMapValue(map, x + 1, y - 1);
        bool southWest = GetWaterMapValue(map, x - 1, y - 1);
        bool northWest = GetWaterMapValue(map, x - 1, y + 1);

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

        if (useRandomSeed)
        {
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
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
        if (isCorner && blockSet.corner != null)
        {
            return blockSet.corner.gameObject;
        }

        if (blockSet.normal != null)
        {
            return blockSet.normal.gameObject;
        }

        return blockSet.corner != null ? blockSet.corner.gameObject : null;
    }

    private static void ApplyBodyRotation(GameObject blockObject, float yRotation)
    {
        Transform body = blockObject.transform.Find("Body");
        if (body != null)
        {
            body.localRotation = Quaternion.Euler(0f, yRotation, 0f);
        }
    }
}
