using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TerrainGenerator : MonoBehaviour
{
    public enum ResourcePlacementMode
    {
        Clustered,
        Sparse
    }

    private enum TerrainBiome
    {
        Water = 0,
        Sand = 1,
        Dirt = 2,
        Grass = 3,
        Forest = 4,
        Rock = 5
    }

    private struct BlockBiomeVisualData
    {
        public TerrainBiome primaryBiome;
        public TerrainBiome[] surfaceBiomes;

        public bool IsWaterBlock => primaryBiome == TerrainBiome.Water;
    }

    private readonly struct ChunkGenerationRequest
    {
        public readonly Vector2Int coordinate;
        public readonly int chunkSize;

        public ChunkGenerationRequest(Vector2Int coordinate, int chunkSize)
        {
            this.coordinate = coordinate;
            this.chunkSize = chunkSize;
        }
    }

    private sealed class ChunkSurfaceBuildData
    {
        public Vector2Int origin;
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<Color> colors = new List<Color>();
        public readonly float[] blendWeightBuffer = new float[6];
        public readonly List<int>[] trianglesByBiome;

        public ChunkSurfaceBuildData(int biomeCount)
        {
            trianglesByBiome = new List<int>[biomeCount];
            for (int i = 0; i < trianglesByBiome.Length; i++)
            {
                trianglesByBiome[i] = new List<int>();
            }
        }
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
        [HideInInspector] public Resource prefab;
        public ResourceDefinition definition;
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

        public Resource Prefab => definition != null ? definition.prefab : prefab;
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

    [Header("Editor Preview")]
    [SerializeField]
    private bool expandEditorPreviewRange = false;

    [Header("Chunk Streaming")]
    [SerializeField, Min(1)]
    private int chunkGenerationBlocksPerFrame = 48;

    [SerializeField, Min(1)]
    private int chunkSurfaceRowsPerFrame = 12;

    [SerializeField]
    private Transform trackingTarget;

    [SerializeField]
    private bool generateOnStart = true;

    [SerializeField]
    private int seed = 12345;

    [SerializeField, Range(0f, 1f)]
    private float waterFillPercent = 0.15f;

    [SerializeField, Min(0.001f)]
    private float waterNoiseScale = 0.08f;

    [Header("Biome Terrain")]
    [SerializeField, Range(2, 6)]
    private int terrainSurfaceSubdivisions = 4;

    [SerializeField, Range(0f, 0.45f)]
    private float terrainBlendJitter = 0.18f;

    [SerializeField, Range(0f, 0.35f)]
    private float terrainSurfaceVertexJitter = 0.14f;

    [Header("Surface Texture Blend")]
    [SerializeField]
    private bool enableGeneratedSurfaceTextureBlend = true;

    [SerializeField, Min(0.01f)]
    private float generatedSurfaceBlendTextureTiling = 1.12f;

    [SerializeField, Min(0.01f)]
    private float generatedSurfaceBlendNoiseScale = 0.11f;

    [SerializeField, Range(0f, 0.5f)]
    private float generatedSurfaceBlendNoiseStrength = 0.18f;

    [SerializeField, HideInInspector]
    private Shader generatedSurfaceBlendShader;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendWaterTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendSandTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendDirtTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendGrassTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendForestTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendNoiseTexture;

    [SerializeField, Min(0.001f)]
    private float largeLakeCellSize = 72f;

    [SerializeField, Range(0f, 1f)]
    private float largeLakeChance = 0.55f;

    [SerializeField]
    private Vector2 largeLakeRadiusRange = new Vector2(9f, 19f);

    [SerializeField, Min(0.001f)]
    private float largeLakeBlobNoiseScale = 0.035f;

    [SerializeField, Min(0.001f)]
    private float smallLakeCellSize = 34f;

    [SerializeField, Range(0f, 1f)]
    private float smallLakeChance = 0.42f;

    [SerializeField]
    private Vector2 smallLakeRadiusRange = new Vector2(3.5f, 7.5f);

    [SerializeField, Min(0.001f)]
    private float smallLakeBlobNoiseScale = 0.065f;

    [SerializeField, Min(8f)]
    private float riverCellSize = 176f;

    [SerializeField, Range(0f, 0.3f)]
    private float riverChance = 0.035f;

    [SerializeField, Min(0.25f)]
    private float riverWidth = 1.35f;

    [SerializeField, Min(0f)]
    private float riverCurveStrength = 14f;

    [SerializeField]
    private Vector2 riverEndpointLakeRadiusRange = new Vector2(5.5f, 10.5f);

    [SerializeField, Min(1)]
    private int sandMinWidth = 1;

    [SerializeField, Min(1)]
    private int sandMaxWidth = 2;

    [SerializeField, Min(0.001f)]
    private float landBiomePrimaryScale = 0.03f;

    [SerializeField, Min(0.001f)]
    private float landBiomeDetailScale = 0.075f;

    [SerializeField, Range(0f, 1f)]
    private float dirtWeight = 0.48f;

    [SerializeField, Range(0f, 1f)]
    private float grassWeight = 0.52f;

    [SerializeField, Range(0f, 1f)]
    private float forestWeight = 0.30f;

    [SerializeField, Range(0f, 1f)]
    private float rockWeight = 0.18f;

    [SerializeField]
    private Color waterBiomeColor = new Color(0.27f, 0.52f, 0.86f, 1f);

    [SerializeField]
    private Color sandBiomeColor = new Color(0.94f, 0.85f, 0.58f, 1f);

    [SerializeField]
    private Color dirtBiomeColor = new Color(0.55f, 0.37f, 0.18f, 1f);

    [SerializeField]
    private Color grassBiomeColor = new Color(0.63f, 0.76f, 0.21f, 1f);

    [SerializeField]
    private Color forestBiomeColor = new Color(0.24f, 0.43f, 0.16f, 1f);

    [SerializeField]
    private Color rockBiomeColor = new Color(0.31f, 0.35f, 0.40f, 1f);

    [SerializeField, Min(0f)]
    private float generatedSurfaceYOffset = 0.01f;

    [SerializeField]
    private Vector2 startLakeRadiusRange = new Vector2(3f, 5f);

    [SerializeField, Min(0)]
    private int startSafeZoneRadius = 2;

    [SerializeField]
    private bool keepStartSafeZoneClearOfResources = true;

    [SerializeField, Min(0)]
    private int starterWaterExclusionRadius = 2;

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
    private int starterTreeMinCount = 8;

    [SerializeField, Min(4)]
    private int starterTreeMaxCount = 12;

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
    private readonly Dictionary<Vector2Int, TerrainBiome> tileBiomeCache = new Dictionary<Vector2Int, TerrainBiome>();
    private readonly Dictionary<Vector2Int, bool> rawWaterCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> directWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> bufferedWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<TerrainBiome, Material> biomeMaterialCache = new Dictionary<TerrainBiome, Material>();
    private readonly Queue<ChunkGenerationRequest> pendingChunkGenerations = new Queue<ChunkGenerationRequest>();
    private readonly HashSet<Vector2Int> pendingChunkGenerationCoordinates = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> activeChunkGenerationCoordinates = new HashSet<Vector2Int>();

    private bool hasGeneratedChunks;
    private bool hasSeedInitialized;
    private Vector2Int currentCenterChunk;
    private BlockStateStore resourceStateStore;
    private InstallationPlacementController installationRestoreController;
    private InstallationBackgroundSimulator installationBackgroundSimulator;
    private BlockPool blockPool;
    private Coroutine chunkGenerationCoroutine;

    private readonly List<ResourceEntry> starterTreeCacheEntries = new List<ResourceEntry>();
    private readonly List<Vector2Int> starterTreeCacheCandidates = new List<Vector2Int>();
    private readonly Dictionary<Vector2Int, Resource> starterTreeCacheLookup = new Dictionary<Vector2Int, Resource>();
    private int starterTreeCacheSeed = int.MinValue;
    private bool starterTreeCacheValid;
    private Material generatedSurfaceBlendMaterial;

    private void OnValidate()
    {
        MigrateLegacyResourcesIfNeeded();
        UpgradeLegacyGeneratedSurfaceBlendSettings();
        starterOreMaxResourceCount = Mathf.Max(starterOreMinResourceCount, starterOreMaxResourceCount);
        normalOreMaxResourceCount = Mathf.Max(normalOreMinResourceCount, normalOreMaxResourceCount);
        oreMaximumBodyScaleRatio = Mathf.Max(oreMinimumBodyScaleRatio, oreMaximumBodyScaleRatio);
        oreScaleAtResourceCount = Mathf.Max(1, oreScaleAtResourceCount);
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        SyncResourceEntryDefinitions();
        InvalidateStarterTreeCache();
#if UNITY_EDITOR
        PopulateGeneratedSurfaceBlendEditorDefaults();
#endif
        ApplyGeneratedSurfaceBlendSettingsToRuntimeMaterial();
    }

    private void Start()
    {
        MigrateLegacyResourcesIfNeeded();
        UpgradeLegacyGeneratedSurfaceBlendSettings();
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        SyncResourceEntryDefinitions();
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

    private void OnDisable()
    {
        ClearPendingChunkGenerations();
    }

    public void Generate()
    {
        MigrateLegacyResourcesIfNeeded();
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        SyncResourceEntryDefinitions();
        EnsureResourceStateStore();
        InitializeSeedForGeneration();
        InvalidateStarterTreeCache();
        InvalidateTerrainBiomeDataCaches();
        InvalidateTerrainBiomeMaterialCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks();
        resourceStateStore?.ClearStates();

        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        RefreshChunks(currentCenterChunk, true);
    }

    public void ResetChunks()
    {
        if (!hasGeneratedChunks)
        {
            Generate();
            return;
        }

        MigrateLegacyResourcesIfNeeded();
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        SyncResourceEntryDefinitions();
        EnsureResourceStateStore();
        InvalidateStarterTreeCache();
        InvalidateTerrainBiomeDataCaches();
        InvalidateTerrainBiomeMaterialCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks();

        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        RefreshChunks(currentCenterChunk, true);
    }

    public void RandomizeSeed()
    {
        seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        hasSeedInitialized = true;
        InvalidateStarterTreeCache();
        InvalidateTerrainBiomeDataCaches();
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
        int normalizedLoadRadius = GetEffectiveLoadRadius();
        int normalizedUnloadRadius = GetEffectiveUnloadRadius();
        List<Vector2Int> chunksToGenerate = new List<Vector2Int>();

        for (int chunkY = centerChunk.y - normalizedLoadRadius; chunkY <= centerChunk.y + normalizedLoadRadius; chunkY++)
        {
            for (int chunkX = centerChunk.x - normalizedLoadRadius; chunkX <= centerChunk.x + normalizedLoadRadius; chunkX++)
            {
                Vector2Int chunkCoordinate = new Vector2Int(chunkX, chunkY);

                if (forceReload || (!loadedChunks.ContainsKey(chunkCoordinate) && !activeChunkGenerationCoordinates.Contains(chunkCoordinate)))
                {
                    chunksToGenerate.Add(chunkCoordinate);
                }
            }
        }

        chunksToGenerate.Sort((left, right) =>
        {
            int leftDistance = GetChunkDistanceSqr(left, centerChunk);
            int rightDistance = GetChunkDistanceSqr(right, centerChunk);
            return leftDistance.CompareTo(rightDistance);
        });

        for (int i = 0; i < chunksToGenerate.Count; i++)
        {
            QueueChunkGeneration(chunksToGenerate[i], normalizedChunkSize);
        }

        EnsureChunkGenerationProcessing();

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
        IEnumerator routine = GenerateChunkRoutine(chunkCoordinate, normalizedChunkSize, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator GenerateChunkRoutine(Vector2Int chunkCoordinate, int normalizedChunkSize, bool allowYield)
    {
        if (!TryGetBlockSet(Block.BlockType.Ground, out BlockSet groundSet))
        {
            yield break;
        }

        activeChunkGenerationCoordinates.Add(chunkCoordinate);

        if (loadedChunks.TryGetValue(chunkCoordinate, out Transform existingChunk))
        {
            SaveChunkResourceStates(existingChunk);
            RemoveChunkBlocksFromLookup(existingChunk);
            ReleaseChunkBlocksToPool(existingChunk);
            DestroyChunkObject(existingChunk.gameObject);
            loadedChunks.Remove(chunkCoordinate);
        }

        bool hasWaterSet = TryGetBlockSet(Block.BlockType.Water, out BlockSet waterSet);
        Vector2Int origin = new Vector2Int(chunkCoordinate.x * normalizedChunkSize, chunkCoordinate.y * normalizedChunkSize);
        GameObject chunkObject = new GameObject($"Chunk ({chunkCoordinate.x}, {chunkCoordinate.y})");
        chunkObject.transform.SetParent(transform, false);
        chunkObject.transform.position = new Vector3(origin.x, 0f, origin.y);
        loadedChunks.Add(chunkCoordinate, chunkObject.transform);
        int blocksSinceYield = 0;
        int blockBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);

        for (int localY = 0; localY < normalizedChunkSize; localY++)
        {
            for (int localX = 0; localX < normalizedChunkSize; localX++)
            {
                Vector2Int worldCoordinate = new Vector2Int(origin.x + localX, origin.y + localY);
                Vector3 localPosition = new Vector3(localX, 0f, localY);
                BlockBiomeVisualData visualData = BuildBlockBiomeVisualData(worldCoordinate);
                Block.BlockType blockType = visualData.IsWaterBlock ? Block.BlockType.Water : Block.BlockType.Ground;
                BlockSet activeBlockSet = blockType == Block.BlockType.Water && hasWaterSet ? waterSet : groundSet;
                Block block = CreateBlock(chunkObject.transform, activeBlockSet, blockType, worldCoordinate, localPosition, false, 0f);
                if (block == null)
                {
                    continue;
                }

                ApplyBlockBiomeVisuals(block, visualData);
                if (blockType == Block.BlockType.Ground
                    && !HasSavedOrLiveInstallationAtCoordinate(worldCoordinate)
                    && CanSpawnResourceOnBiome(visualData.primaryBiome)
                    && TryGetResourcePrefab(worldCoordinate, out Resource resourcePrefab))
                {
                    SpawnResourceOnBlock(block, resourcePrefab, worldCoordinate);
                }

                if (allowYield && ++blocksSinceYield >= blockBudget)
                {
                    blocksSinceYield = 0;
                    yield return null;
                }
            }
        }

        if (allowYield)
        {
            yield return null;
        }

        RestoreChunkInstallations(chunkObject.transform);
        RestoreChunkBlockStates(chunkObject.transform);

        ChunkSurfaceBuildData chunkSurface;
        if (Application.isPlaying)
        {
            Task<ChunkSurfaceBuildData> surfaceTask = CreateChunkSurfaceBuildTask(origin, normalizedChunkSize);
            while (!surfaceTask.IsCompleted)
            {
                yield return null;
            }

            if (surfaceTask.IsFaulted || surfaceTask.IsCanceled)
            {
                Exception surfaceException = surfaceTask.Exception?.Flatten().InnerException ?? surfaceTask.Exception;
                if (surfaceException != null)
                {
                    Debug.LogException(surfaceException, this);
                }

                chunkSurface = BuildCurvedChunkSurface(origin, normalizedChunkSize);
            }
            else
            {
                chunkSurface = surfaceTask.Result;
            }
        }
        else
        {
            chunkSurface = new ChunkSurfaceBuildData(6)
            {
                origin = origin
            };
            IEnumerator surfaceRoutine = BuildCurvedChunkSurfaceRoutine(chunkSurface, origin, normalizedChunkSize, allowYield);
            while (surfaceRoutine.MoveNext())
            {
                if (allowYield && surfaceRoutine.Current != null)
                {
                    yield return surfaceRoutine.Current;
                }
            }
        }

        ApplyChunkBiomeSurface(chunkObject.transform, chunkSurface);
        activeChunkGenerationCoordinates.Remove(chunkCoordinate);

        if (!IsChunkWithinRadius(chunkCoordinate, currentCenterChunk, GetEffectiveUnloadRadius()))
        {
            UnloadChunk(chunkCoordinate);
        }
    }

    private void UnloadChunk(Vector2Int chunkCoordinate)
    {
        if (activeChunkGenerationCoordinates.Contains(chunkCoordinate))
        {
            return;
        }

        if (!loadedChunks.TryGetValue(chunkCoordinate, out Transform chunkTransform))
        {
            return;
        }

        SaveChunkResourceStates(chunkTransform);
        RemoveChunkBlocksFromLookup(chunkTransform);
        ReleaseChunkBlocksToPool(chunkTransform);
        loadedChunks.Remove(chunkCoordinate);
        CleanupOrphanedLiveInstallations();
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
                ReleaseChunkBlocksToPool(chunkObjects[i]);
                CleanupOrphanedLiveInstallations();
                DestroyChunkObject(chunkObjects[i].gameObject);
            }
        }

        loadedChunks.Clear();
        loadedBlocks.Clear();
        CleanupOrphanedLiveInstallations();
    }

    private void DestroyChunkObject(GameObject chunkObject)
    {
        if (chunkObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            BlockPool resolvedBlockPool = blockPool;
            if (resolvedBlockPool != null
                && resolvedBlockPool.PoolRoot != null
                && chunkObject.transform == resolvedBlockPool.PoolRoot)
            {
                return;
            }
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

        GameObject blockObject;
        if (Application.isPlaying)
        {
            Block pooledBlock = ResolveBlockPool()?.Get(prefab, parent);
            if (pooledBlock == null)
            {
                return null;
            }

            blockObject = pooledBlock.gameObject;
        }
        else
        {
            blockObject = Instantiate(prefab, parent);
        }

        Block block = blockObject.GetComponent<Block>();
        if (block == null)
        {
            block = blockObject.AddComponent<Block>();
        }

        blockObject.transform.localPosition = localPosition;
        blockObject.transform.localRotation = Quaternion.identity;
        block.SetBodyRotation(yRotation);

        block.Initialize(coordinate, blockType);
        loadedBlocks[coordinate] = block;
        return block;
    }

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

    private void AppendBiomeContourSurface(
        ChunkSurfaceBuildData chunkSurface,
        TerrainBiome biome,
        Vector2Int origin,
        int cellCount,
        int resolution)
    {
        IEnumerator routine = AppendBiomeContourSurfaceRoutine(chunkSurface, biome, origin, cellCount, resolution, false);
        while (routine.MoveNext())
        {
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

        generatedSurfaceBlendMaterial.SetColor("_SandColor", GetBiomeColor(TerrainBiome.Sand));
        generatedSurfaceBlendMaterial.SetColor("_DirtColor", GetBiomeColor(TerrainBiome.Dirt));
        generatedSurfaceBlendMaterial.SetColor("_GrassColor", GetBiomeColor(TerrainBiome.Grass));
        generatedSurfaceBlendMaterial.SetColor("_ForestColor", GetBiomeColor(TerrainBiome.Forest));
        generatedSurfaceBlendMaterial.SetFloat("_TextureTiling", generatedSurfaceBlendTextureTiling);
        generatedSurfaceBlendMaterial.SetFloat("_NoiseScale", generatedSurfaceBlendNoiseScale);
        generatedSurfaceBlendMaterial.SetFloat("_NoiseStrength", generatedSurfaceBlendNoiseStrength);

        Material groundMaterial = ResolveSourceMaterialForBiome(TerrainBiome.Grass);
        if (groundMaterial != null && groundMaterial.HasProperty("_ShadowColor"))
        {
            generatedSurfaceBlendMaterial.SetColor("_ShadowColor", groundMaterial.GetColor("_ShadowColor"));
        }

        if (groundMaterial != null && groundMaterial.HasProperty("_ShadeThreshold"))
        {
            generatedSurfaceBlendMaterial.SetFloat("_ShadeThreshold", groundMaterial.GetFloat("_ShadeThreshold"));
        }

        if (groundMaterial != null && groundMaterial.HasProperty("_ShadeSmoothness"))
        {
            generatedSurfaceBlendMaterial.SetFloat("_ShadeSmoothness", groundMaterial.GetFloat("_ShadeSmoothness"));
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

        Texture2D noiseTexture = generatedSurfaceBlendNoiseTexture != null
            ? generatedSurfaceBlendNoiseTexture
            : grassTexture;

        if (sandTexture != null)
        {
            generatedSurfaceBlendMaterial.SetTexture("_SandMap", sandTexture);
        }

        if (dirtTexture != null)
        {
            generatedSurfaceBlendMaterial.SetTexture("_DirtMap", dirtTexture);
        }

        if (grassTexture != null)
        {
            generatedSurfaceBlendMaterial.SetTexture("_GrassMap", grassTexture);
        }

        if (forestTexture != null)
        {
            generatedSurfaceBlendMaterial.SetTexture("_ForestMap", forestTexture);
        }

        if (noiseTexture != null)
        {
            generatedSurfaceBlendMaterial.SetTexture("_BlendNoise", noiseTexture);
        }

        return generatedSurfaceBlendMaterial;
    }

    private void UpgradeLegacyGeneratedSurfaceBlendSettings()
    {
        if (Mathf.Approximately(generatedSurfaceBlendTextureTiling, 0.28f)
            || Mathf.Approximately(generatedSurfaceBlendTextureTiling, 0.56f))
        {
            generatedSurfaceBlendTextureTiling = 1.12f;
        }
    }

    private void ApplyGeneratedSurfaceBlendSettingsToRuntimeMaterial()
    {
        if (generatedSurfaceBlendMaterial == null)
        {
            return;
        }

        generatedSurfaceBlendMaterial.SetFloat("_TextureTiling", generatedSurfaceBlendTextureTiling);
        generatedSurfaceBlendMaterial.SetFloat("_NoiseScale", generatedSurfaceBlendNoiseScale);
        generatedSurfaceBlendMaterial.SetFloat("_NoiseStrength", generatedSurfaceBlendNoiseStrength);
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

        Color biomeColor = GetBiomeColor(biome);
        material.name = $"Runtime_{biome}";
        material.enableInstancing = true;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", biomeColor);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", biomeColor);
        }

        material.color = biomeColor;
        biomeMaterialCache[biome] = material;
        return material;
    }

    private Material ResolveSourceMaterialForBiome(TerrainBiome biome)
    {
        Block.BlockType blockType = biome == TerrainBiome.Water ? Block.BlockType.Water : Block.BlockType.Ground;
        if (!TryGetBlockSet(blockType, out BlockSet blockSet))
        {
            if (blockType == Block.BlockType.Water && TryGetBlockSet(Block.BlockType.Ground, out blockSet))
            {
                return GetBlockSetMaterial(blockSet);
            }

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

    private Color GetBiomeColor(TerrainBiome biome)
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
        return GetBiomeColor(GetTileBiome(worldCoordinate));
    }

    public Color32 GetMapBiomeColor32At(Vector2Int worldCoordinate)
    {
        return (Color32)GetMapBiomeColorAt(worldCoordinate);
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
        float primary = SampleNoise(worldCoordinate, landBiomePrimaryScale, new Vector2(117.3f, 901.8f));
        float detail = SampleNoise(worldCoordinate, landBiomeDetailScale, new Vector2(611.5f, 273.4f));
        float selector = Mathf.Clamp01((primary * 0.72f) + (detail * 0.28f));
        float totalWeight = Mathf.Max(0.001f, dirtWeight + grassWeight + forestWeight + rockWeight);
        float dirtThreshold = dirtWeight / totalWeight;
        float grassThreshold = dirtThreshold + (grassWeight / totalWeight);
        float forestThreshold = grassThreshold + (forestWeight / totalWeight);

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

    private bool IsRawWaterTileBiome(Vector2Int worldCoordinate)
    {
        if (rawWaterCache.TryGetValue(worldCoordinate, out bool cachedWater))
        {
            return cachedWater;
        }

        bool isWater = !IsBlockedForWater(worldCoordinate) && EvaluateWaterField(worldCoordinate) > Mathf.Lerp(0.64f, 0.48f, Mathf.Clamp01(waterFillPercent * 1.35f));
        rawWaterCache[worldCoordinate] = isWater;
        return isWater;
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

                float pathWidth = riverWidth * Mathf.Lerp(0.9f, 1.3f, Hash01(candidateCellX, candidateCellY, 6989));
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

    private void InvalidateTerrainBiomeCaches()
    {
        InvalidateTerrainBiomeDataCaches();
        InvalidateTerrainBiomeMaterialCaches();
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
    }

#if UNITY_EDITOR
    private void PopulateGeneratedSurfaceBlendEditorDefaults()
    {
        if (generatedSurfaceBlendShader == null)
        {
            generatedSurfaceBlendShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TerrainBiomeBlend.shader");
        }

        if (generatedSurfaceBlendWaterTexture == null)
        {
            generatedSurfaceBlendWaterTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/AureDevGames/Water Stylized Shader Orto & Perspective Camera/Textures/Procedural/waterTex2.png");
        }

        if (generatedSurfaceBlendSandTexture == null)
        {
            generatedSurfaceBlendSandTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/AureDevGames/Water Stylized Shader Orto & Perspective Camera/Textures/Sand/Sand.png");
        }

        if (generatedSurfaceBlendDirtTexture == null)
        {
            generatedSurfaceBlendDirtTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/Ground_TD_00.png");
        }

        if (generatedSurfaceBlendNoiseTexture == null)
        {
            generatedSurfaceBlendNoiseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/AureDevGames/Water Stylized Shader Orto & Perspective Camera/Textures/Procedural/perlinNoise.png");
        }

        if (generatedSurfaceBlendGrassTexture == null)
        {
            generatedSurfaceBlendGrassTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/Ground_TD_00.png");
            if (generatedSurfaceBlendGrassTexture == null)
            {
                generatedSurfaceBlendGrassTexture = ResolveGeneratedSurfaceBlendTexture(
                    null,
                    ResolveSourceMaterialForBiome(TerrainBiome.Grass),
                    "_BaseMap",
                    "_MainTex");
            }
        }

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
#endif

    private void QueueChunkGeneration(Vector2Int chunkCoordinate, int normalizedChunkSize)
    {
        if (loadedChunks.ContainsKey(chunkCoordinate)
            || pendingChunkGenerationCoordinates.Contains(chunkCoordinate)
            || activeChunkGenerationCoordinates.Contains(chunkCoordinate))
        {
            return;
        }

        pendingChunkGenerations.Enqueue(new ChunkGenerationRequest(chunkCoordinate, normalizedChunkSize));
        pendingChunkGenerationCoordinates.Add(chunkCoordinate);
    }

    private void EnsureChunkGenerationProcessing()
    {
        if (pendingChunkGenerations.Count <= 0)
        {
            return;
        }

        if (!Application.isPlaying)
        {
            ProcessQueuedChunkGenerationsImmediate();
            return;
        }

        if (chunkGenerationCoroutine == null)
        {
            chunkGenerationCoroutine = StartCoroutine(ProcessChunkGenerationQueue());
        }
    }

    private IEnumerator ProcessChunkGenerationQueue()
    {
        while (pendingChunkGenerations.Count > 0)
        {
            ChunkGenerationRequest request = pendingChunkGenerations.Dequeue();
            pendingChunkGenerationCoordinates.Remove(request.coordinate);
            if (!ShouldGenerateChunk(request.coordinate))
            {
                continue;
            }

            IEnumerator chunkRoutine = GenerateChunkRoutine(request.coordinate, request.chunkSize, true);
            while (chunkRoutine.MoveNext())
            {
                yield return chunkRoutine.Current;
            }
        }

        chunkGenerationCoroutine = null;
    }

    private void ProcessQueuedChunkGenerationsImmediate()
    {
        while (pendingChunkGenerations.Count > 0)
        {
            ChunkGenerationRequest request = pendingChunkGenerations.Dequeue();
            pendingChunkGenerationCoordinates.Remove(request.coordinate);
            if (!ShouldGenerateChunk(request.coordinate))
            {
                continue;
            }

            GenerateChunk(request.coordinate, request.chunkSize);
        }
    }

    private void ClearPendingChunkGenerations()
    {
        pendingChunkGenerations.Clear();
        pendingChunkGenerationCoordinates.Clear();
        activeChunkGenerationCoordinates.Clear();

        if (chunkGenerationCoroutine != null)
        {
            StopCoroutine(chunkGenerationCoroutine);
            chunkGenerationCoroutine = null;
        }
    }

    private bool ShouldGenerateChunk(Vector2Int chunkCoordinate)
    {
        return IsChunkWithinRadius(chunkCoordinate, currentCenterChunk, GetEffectiveLoadRadius());
    }

    private int GetEffectiveLoadRadius()
    {
        int normalizedLoadRadius = Mathf.Max(0, loadRadius);

#if UNITY_EDITOR
        if (!Application.isPlaying && expandEditorPreviewRange)
        {
            return normalizedLoadRadius * 8;
        }
#endif

        return normalizedLoadRadius;
    }

    private int GetEffectiveUnloadRadius()
    {
        int effectiveLoadRadius = GetEffectiveLoadRadius();
        int normalizedUnloadRadius = Mathf.Max(effectiveLoadRadius + 1, unloadRadius);

#if UNITY_EDITOR
        if (!Application.isPlaying && expandEditorPreviewRange)
        {
            normalizedUnloadRadius = Mathf.Max(normalizedUnloadRadius, Mathf.Max(1, unloadRadius) * 8);
        }
#endif

        return normalizedUnloadRadius;
    }

    private static bool IsChunkWithinRadius(Vector2Int chunkCoordinate, Vector2Int centerChunk, int radius)
    {
        int normalizedRadius = Mathf.Max(0, radius);
        return Mathf.Abs(chunkCoordinate.x - centerChunk.x) <= normalizedRadius
               && Mathf.Abs(chunkCoordinate.y - centerChunk.y) <= normalizedRadius;
    }

    private static int GetChunkDistanceSqr(Vector2Int a, Vector2Int b)
    {
        int deltaX = a.x - b.x;
        int deltaY = a.y - b.y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    public bool TryAddDroppedItemAtPlayerBlock(Vector3 worldPosition, int itemId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out Block focusedBoxBlock)
            && focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock)
            && inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        Block targetBlock = FindPreferredDropBlock(worldPosition, itemId, 1);
        if (targetBlock != null && targetBlock.TryAddFloorObject(itemId, out targetPortableObject))
        {
            MarkDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
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
        return TryAddDroppedItemStackAtPlayerBlock(
            worldPosition,
            itemId,
            itemCount,
            startWorldPosition,
            null,
            moveInterval,
            out _);
    }

    public bool TryAddDroppedItemStackAtPlayerBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float moveInterval = 0.1f)
    {
        return TryAddDroppedItemStackAtPlayerBlock(
            worldPosition,
            itemId,
            itemCount,
            startWorldPosition,
            startWorldPositionProvider,
            moveInterval,
            out _);
    }

    public bool TryAddDroppedItemStackAtPlayerBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        float moveInterval,
        out Vector2Int dropCoordinate)
    {
        return TryAddDroppedItemStackAtPlayerBlock(
            worldPosition,
            itemId,
            itemCount,
            startWorldPosition,
            null,
            moveInterval,
            out dropCoordinate);
    }

    public bool TryAddDroppedItemStackAtPlayerBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float moveInterval,
        out Vector2Int dropCoordinate)
    {
        dropCoordinate = GetWorldBlockCoordinate(worldPosition);
        if (itemCount <= 0)
        {
            return false;
        }

        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, itemCount, out Block focusedBoxBlock))
        {
            dropCoordinate = focusedBoxBlock.Coordinate;
            for (int i = 0; i < itemCount; i++)
            {
                if (!focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(
                        itemId,
                        startWorldPosition,
                        i * Mathf.Max(0f, moveInterval),
                        out PortableObject droppedObject,
                        null,
                        startWorldPositionProvider))
                {
                    return false;
                }

                MarkInputAreaDroppedPickupGate(droppedObject, false, worldPosition);
            }

            return true;
        }

        if (TryResolveInputAreaDropBlock(dropCoordinate, itemId, itemCount, out Block inputAreaBlock))
        {
            dropCoordinate = inputAreaBlock.Coordinate;
            for (int i = 0; i < itemCount; i++)
            {
                if (!inputAreaBlock.TryAddInputAreaCenterObjectAnimated(
                        itemId,
                        startWorldPosition,
                        i * Mathf.Max(0f, moveInterval),
                        out PortableObject droppedObject,
                        null,
                        startWorldPositionProvider))
                {
                    return false;
                }

                MarkInputAreaDroppedPickupGate(droppedObject, false, worldPosition);
            }

            return true;
        }

        Block targetBlock = FindPreferredDropBlock(worldPosition, itemId, itemCount);
        if (targetBlock == null)
        {
            return false;
        }

        dropCoordinate = targetBlock.Coordinate;
        for (int i = 0; i < itemCount; i++)
        {
            if (!targetBlock.TryAddFloorObjectAnimated(itemId, startWorldPosition, i * Mathf.Max(0f, moveInterval), out PortableObject droppedObject, null, startWorldPositionProvider))
            {
                return false;
            }

            MarkDroppedPickupGate(droppedObject, false, worldPosition);
        }

        return true;
    }

    public bool TryAddDroppedItemAnimated(
        Vector3 worldPosition,
        int itemId,
        Vector3 startWorldPosition,
        out PortableObject targetPortableObject,
        Action onComplete = null)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out Block focusedBoxBlock))
        {
            if (!focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out targetPortableObject, onComplete))
            {
                return false;
            }

            MarkInputAreaDroppedPickupGate(targetPortableObject, false, worldPosition);
            return true;
        }

        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock))
        {
            if (!inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, startWorldPosition, 0f, out targetPortableObject, onComplete))
            {
                return false;
            }

            MarkInputAreaDroppedPickupGate(targetPortableObject, false, worldPosition);
            return true;
        }

        Block targetBlock = FindPreferredDropBlock(worldPosition, itemId, 1);
        if (targetBlock == null)
        {
            targetBlock = FindNearestDropBlock(centerCoordinate, itemId, 1, 2, false);
            if (targetBlock == null)
            {
                return false;
            }
        }

        if (!targetBlock.TryAddFloorObjectAnimated(itemId, startWorldPosition, 0f, out targetPortableObject, onComplete))
        {
            return false;
        }

        MarkDroppedPickupGate(targetPortableObject, false, worldPosition);
        return true;
    }

    public bool TryPickupOneItemToHandAtCoordinate(Player player, Vector2Int coordinate)
    {
        if (player == null)
        {
            return false;
        }

        Vector3 playerPosition = player.transform.position;
        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToHand(player, playerPosition, 999f))
        {
            return true;
        }

        if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
        {
            loadedBlocks.Remove(coordinate);
            return false;
        }

        if (block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        Vector3 anchorPosition = new Vector3(coordinate.x, player.transform.position.y, coordinate.y);
        if (block.TryPickupOneInputAreaCenterObjectToHand(player, anchorPosition, 999f))
        {
            NotifyAreaManualPickup(coordinate);
            return true;
        }

        return block.TryPickupOneFloorObjectToHand(player, anchorPosition, 999f);
    }

    public bool TryPickupOneItemToBagAtCoordinate(Player player, Vector2Int coordinate)
    {
        return TryPickupOneItemToBagAtCoordinate(player, coordinate, -1);
    }

    public bool TryPickupOneItemToBagAtCoordinate(Player player, Vector2Int coordinate, int preferredSlotIndex)
    {
        if (player == null)
        {
            return false;
        }

        Vector3 playerPosition = player.transform.position;
        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToBag(player, playerPosition, 999f, preferredSlotIndex))
        {
            return true;
        }

        if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
        {
            loadedBlocks.Remove(coordinate);
            return false;
        }

        if (block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        Vector3 anchorPosition = new Vector3(coordinate.x, player.transform.position.y, coordinate.y);
        if (block.TryPickupOneInputAreaCenterObjectToBag(player, anchorPosition, 999f, preferredSlotIndex))
        {
            NotifyAreaManualPickup(coordinate);
            return true;
        }

        return block.TryPickupOneFloorObjectToBag(player, anchorPosition, 999f, preferredSlotIndex);
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

    public void RegisterLiveInstallationObject(InstallationObject installationObject)
    {
        EnsureResourceStateStore();
        resourceStateStore?.RegisterLiveInstallation(installationObject);
    }

    public void RegisterInstallationRuntimeState(InstallationObject installationObject)
    {
        EnsureResourceStateStore();
        resourceStateStore?.RegisterLiveInstallation(installationObject);
    }

    public void RemoveInstallationPersistence(Vector2Int anchorCoordinate)
    {
        EnsureResourceStateStore();
        resourceStateStore?.RemoveInstallation(anchorCoordinate);
    }

    private static void NotifyAreaManualPickup(Vector2Int coordinate)
    {
        InputOutputModuleEnergyAreaController.NotifyManualPickupAtCoordinate(coordinate);
        InputOutputModuleItemAreaController.NotifyManualPickupAtCoordinate(coordinate);
    }

    public bool TryGetLoadedBlockBounds(out Vector2Int minCoordinate, out Vector2Int maxCoordinate)
    {
        minCoordinate = Vector2Int.zero;
        maxCoordinate = Vector2Int.zero;

        bool hasValidBlock = false;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            if (pair.Value == null)
            {
                continue;
            }

            if (!hasValidBlock)
            {
                minCoordinate = pair.Key;
                maxCoordinate = pair.Key;
                hasValidBlock = true;
                continue;
            }

            minCoordinate.x = Mathf.Min(minCoordinate.x, pair.Key.x);
            minCoordinate.y = Mathf.Min(minCoordinate.y, pair.Key.y);
            maxCoordinate.x = Mathf.Max(maxCoordinate.x, pair.Key.x);
            maxCoordinate.y = Mathf.Max(maxCoordinate.y, pair.Key.y);
        }

        return hasValidBlock;
    }

    public bool TryAddDroppedItemNear(Vector3 worldPosition, int itemId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);

        if (TryResolveFocusedGroundBoxDropBlock(worldPosition, itemId, 1, out Block focusedBoxBlock)
            && focusedBoxBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock)
            && inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

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

    public bool TryAddDroppedItemToNearestStack(Vector3 worldPosition, int itemId, int searchRadius, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;

        if (itemId < 0)
        {
            return false;
        }

        int radius = Mathf.Max(0, searchRadius);
        if (radius <= 0)
        {
            return false;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        if (TryResolveInputAreaDropBlock(centerCoordinate, itemId, 1, out Block inputAreaBlock)
            && inputAreaBlock.HasInputAreaCenterItem(itemId)
            && inputAreaBlock.TryAddInputAreaCenterObjectAnimated(itemId, worldPosition, 0f, out targetPortableObject))
        {
            MarkInputAreaDroppedPickupGate(targetPortableObject, true, worldPosition);
            return true;
        }

        Block targetBlock = FindNearestDropBlock(centerCoordinate, itemId, 1, radius, true);
        if (targetBlock == null)
        {
            return false;
        }

        return targetBlock.TryAddFloorObject(itemId, out targetPortableObject);
    }

    public int GetDroppedItemCountAround(Vector3 worldPosition, int itemId, int radius)
    {
        if (itemId < 0 || radius < 0)
        {
            return 0;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        int total = 0;

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
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

                total += block.CountFloorObjects(itemId);
            }
        }

        return total;
    }

    public int RemoveDroppedItemsAround(Vector3 worldPosition, int itemId, int radius, int count)
    {
        if (itemId < 0 || radius < 0 || count <= 0)
        {
            return 0;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        int remaining = count;

        for (int searchRadius = 0; searchRadius <= radius && remaining > 0; searchRadius++)
        {
            for (int offsetY = -searchRadius; offsetY <= searchRadius && remaining > 0; offsetY++)
            {
                for (int offsetX = -searchRadius; offsetX <= searchRadius && remaining > 0; offsetX++)
                {
                    if (searchRadius > 0 && Mathf.Abs(offsetX) != searchRadius && Mathf.Abs(offsetY) != searchRadius)
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

                    int removed = block.RemoveFloorObjects(itemId, remaining);
                    remaining -= removed;
                }
            }
        }

        return count - remaining;
    }

    public int TransferDroppedItemsToHand(Player player, Vector3 worldPosition, int radius)
    {
        if (player == null || radius < 0)
        {
            return 0;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(worldPosition);
        int total = 0;

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
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

                total += block.TransferFloorObjectsToHand(player);
            }
        }

        return total;
    }

    public bool TryPickupOneItemToHand(Player player, Vector3 pickupOrigin, int radius, float pickupRadius)
    {
        if (player == null || radius < 0 || pickupRadius <= 0f)
        {
            return false;
        }

        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToHand(player, pickupOrigin, pickupRadius))
        {
            return true;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(pickupOrigin);

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
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

                if (block.TryPickupOneFloorObjectToHand(player, pickupOrigin, pickupRadius))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryPickupOneItemToBag(Player player, Vector3 pickupOrigin, int radius, float pickupRadius)
    {
        return TryPickupOneItemToBag(player, pickupOrigin, radius, pickupRadius, -1);
    }

    public bool TryPickupOneItemToBag(Player player, Vector3 pickupOrigin, int radius, float pickupRadius, int preferredSlotIndex)
    {
        if (player == null || radius < 0 || pickupRadius <= 0f)
        {
            return false;
        }

        if (TryGetFocusedBoxObject(player, out BoxObject focusedBoxObject)
            && focusedBoxObject != null
            && focusedBoxObject.TryPickupContainedObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex))
        {
            return true;
        }

        Vector2Int centerCoordinate = GetWorldBlockCoordinate(pickupOrigin);

        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
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

                if (block.TryPickupOneFloorObjectToBag(player, pickupOrigin, pickupRadius, preferredSlotIndex))
                {
                    return true;
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
        HashSet<InstallationObject> savedInstallations = new HashSet<InstallationObject>();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            resourceStateStore.SaveFloorObjects(chunkBlocks[i].Coordinate, chunkBlocks[i]);

            if (chunkBlocks[i].MapObject is InstallationObject installationObject && savedInstallations.Add(installationObject))
            {
                resourceStateStore.SaveInstallation(installationObject);
                resourceStateStore.RegisterLiveInstallation(installationObject);
            }

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

        Block sameItemBlock = FindNearestDropBlock(centerCoordinate, itemId, itemCount, 2, true);
        if (sameItemBlock != null)
        {
            return sameItemBlock;
        }

        if (loadedBlocks.TryGetValue(centerCoordinate, out Block centerBlock)
            && IsValidDropBlock(centerBlock, itemId, itemCount))
        {
            return centerBlock;
        }

        return FindNearestDropBlock(centerCoordinate, itemId, itemCount, 2, false);
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

    private bool TryResolveFocusedGroundBoxDropBlock(
        Vector3 worldPosition,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        if (itemId < 0 || itemCount <= 0)
        {
            return false;
        }

        Player currentPlayer = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (!TryGetFocusedBoxObject(currentPlayer, out BoxObject focusedBox)
            || focusedBox == null
            || !focusedBox.IsOpen
            || !focusedBox.AcceptsItem(itemId))
        {
            return false;
        }

        if (focusedBox.TryGetGroundDropCoordinate(out Vector2Int targetCoordinate))
        {
            if (!loadedBlocks.TryGetValue(targetCoordinate, out Block groundBlock) || groundBlock == null)
            {
                loadedBlocks.Remove(targetCoordinate);
                return false;
            }

            if (groundBlock.Type != Block.BlockType.Ground
                || groundBlock.MapObject != focusedBox
                || !groundBlock.CanAddInputAreaCenterObjects(itemCount, itemId))
            {
                return false;
            }

            targetBlock = groundBlock;
            return true;
        }

        return TryResolveFocusedItemAreaBoxDropBlock(focusedBox, itemId, itemCount, out targetBlock);
    }

    private bool TryResolveFocusedItemAreaBoxDropBlock(
        BoxObject focusedBox,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        if (focusedBox == null || itemId < 0 || itemCount <= 0)
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = focusedBox.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireMatchingExistingStack = pass == 0;

            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                Vector2Int coordinate = occupiedCoordinates[i];
                if (!loadedBlocks.TryGetValue(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                if (block.Type != Block.BlockType.Ground
                    || block.MapObject != focusedBox
                    || !IsValidFocusedItemAreaBoxDropBlock(block, itemId, itemCount))
                {
                    continue;
                }

                if (requireMatchingExistingStack && !block.HasInputAreaCenterItem(itemId))
                {
                    continue;
                }

                targetBlock = block;
                return true;
            }
        }

        return false;
    }

    private bool IsValidFocusedItemAreaBoxDropBlock(Block block, int itemId, int itemCount)
    {
        if (block == null || block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        if (IsValidInputItemAreaDropBlock(block, itemId, itemCount))
        {
            return true;
        }

        ItemDefinition definition = ResolveItemDefinition(itemId);
        if (definition != null
            && definition.energyType != ItemDefinition.EnergyType.None
            && IsValidInputEnergyAreaDropBlock(block, definition.energyType, itemId, itemCount))
        {
            return true;
        }

        return IsValidOutputAreaDropBlock(block, itemId, itemCount);
    }

    private static bool TryGetFocusedBoxObject(Player player, out BoxObject focusedBoxObject)
    {
        focusedBoxObject = null;
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedBoxObject(out focusedBoxObject);
    }

    private bool TryResolveInputAreaDropBlock(Vector2Int centerCoordinate, int itemId, int itemCount, out Block targetBlock)
    {
        targetBlock = null;
        if (!loadedBlocks.TryGetValue(centerCoordinate, out Block block) || block == null)
        {
            return false;
        }

        if (IsValidInputItemAreaDropBlock(block, itemId, itemCount))
        {
            targetBlock = block;
            return true;
        }

        return TryResolveInputEnergyAreaDropBlock(centerCoordinate, itemId, itemCount, out targetBlock);
    }

    private bool TryResolveInputEnergyAreaDropBlock(Vector2Int centerCoordinate, int itemId, int itemCount, out Block targetBlock)
    {
        targetBlock = null;
        ItemDefinition definition = ResolveItemDefinition(itemId);
        if (definition == null || definition.energyType == ItemDefinition.EnergyType.None)
        {
            return false;
        }

        if (!loadedBlocks.TryGetValue(centerCoordinate, out Block block) || block == null)
        {
            return false;
        }

        if (!IsValidInputEnergyAreaDropBlock(block, definition.energyType, itemId, itemCount))
        {
            return false;
        }

        targetBlock = block;
        return true;
    }

    private static bool IsValidInputItemAreaDropBlock(Block block, int itemId, int itemCount)
    {
        return block != null
               && itemId >= 0
               && block.Type == Block.BlockType.Ground
               && InputOutputModuleItemAreaController.CoordinateAcceptsItemId(block.Coordinate, itemId)
               && block.CanAddInputAreaCenterObjects(itemCount, itemId);
    }

    private static bool IsValidInputEnergyAreaDropBlock(
        Block block,
        ItemDefinition.EnergyType energyType,
        int itemId,
        int itemCount)
    {
        return block != null
               && block.Type == Block.BlockType.Ground
               && InputOutputModuleEnergyAreaController.CoordinateAcceptsEnergyType(block.Coordinate, energyType)
               && block.CanAddInputAreaCenterObjects(itemCount, itemId);
    }

    private static bool IsValidOutputAreaDropBlock(Block block, int itemId, int itemCount)
    {
        return block != null
               && itemId >= 0
               && block.Type == Block.BlockType.Ground
               && InputOutputModuleOutputAreaController.CoordinateIsOutputArea(block.Coordinate)
               && InputOutputModule.RuntimeOutputCoordinateProducesItemId(block.Coordinate, itemId)
               && block.CanAddInputAreaCenterObjects(itemCount, itemId);
    }

    private static void MarkDroppedPickupGate(PortableObject droppedObject, bool settled, Vector3 origin)
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

        gate.MarkDropped(0.5f, settled, origin);
    }

    private static void MarkInputAreaDroppedPickupGate(PortableObject droppedObject, bool settled, Vector3 origin)
    {
        MarkDroppedPickupGate(droppedObject, settled, origin);
        if (droppedObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = droppedObject.GetComponent<DroppedItemPickupGate>();
        gate?.SetAutoPickupBlocked(true);
    }

    private static ItemDefinition ResolveItemDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
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

    private void RestoreChunkInstallations(Transform chunkTransform)
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
        HashSet<Vector2Int> installationAnchors = new HashSet<Vector2Int>();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            if (chunkBlocks[i] != null
                && resourceStateStore.TryGetInstallationAnchorAtCoordinate(chunkBlocks[i].Coordinate, out Vector2Int anchorCoordinate))
            {
                installationAnchors.Add(anchorCoordinate);
            }
        }

        foreach (Vector2Int anchorCoordinate in installationAnchors)
        {
            RestoreOrBindSavedInstallation(anchorCoordinate);
        }
    }

    private void RestoreOrBindSavedInstallation(Vector2Int anchorCoordinate)
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        if (resourceStateStore.TryGetLiveInstallation(anchorCoordinate, out InstallationObject liveInstallation, out BlockStateStore.InstallationSaveState liveState))
        {
            IReadOnlyList<Vector2Int> occupiedCoordinates = liveState != null && liveState.occupiedCoordinates != null && liveState.occupiedCoordinates.Count > 0
                ? liveState.occupiedCoordinates
                : liveInstallation.RuntimeOccupiedCoordinates;
            BindLoadedBlocksToInstallation(liveInstallation, occupiedCoordinates);
            return;
        }

        ResolveInstallationBackgroundSimulator()?.SimulateSavedInstallation(anchorCoordinate);

        if (!resourceStateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState savedState))
        {
            return;
        }

        if (TryInstantiateSavedInstallation(savedState, out InstallationObject restoredInstallation))
        {
            resourceStateStore.RegisterLiveInstallation(restoredInstallation, savedState);
        }
    }

    private bool TryInstantiateSavedInstallation(
        BlockStateStore.InstallationSaveState savedState,
        out InstallationObject installationObject)
    {
        installationObject = null;
        if (savedState == null)
        {
            return false;
        }

        ItemDefinition definition = ResolveInstallationDefinition(savedState.itemId);
        if (definition == null || definition.mapObject == null)
        {
            return false;
        }

        InstallationPlacementController placementController = ResolveInstallationPlacementController();
        Quaternion rotation = placementController != null
            ? placementController.GetInstalledObjectRotation(definition, savedState.quarterTurns)
            : definition.mapObject.transform.rotation * Quaternion.Euler(0f, (((savedState.quarterTurns % 4) + 4) % 4) * 90f, 0f);
        Vector3 position = placementController != null
            ? placementController.GetInstalledObjectWorldPosition(savedState.anchorCoordinate, definition, savedState.quarterTurns, 0f)
            : new Vector3(savedState.anchorCoordinate.x, transform.position.y, savedState.anchorCoordinate.y);

        MapObject restoredObject = Instantiate(definition.mapObject, transform);
        restoredObject.transform.SetPositionAndRotation(position, rotation);

        if (!(restoredObject is InstallationObject restoredInstallation))
        {
            if (Application.isPlaying)
            {
                Destroy(restoredObject.gameObject);
            }
            else
            {
                DestroyImmediate(restoredObject.gameObject);
            }

            return false;
        }

        List<Vector2Int> occupiedCoordinates = savedState.occupiedCoordinates != null && savedState.occupiedCoordinates.Count > 0
            ? new List<Vector2Int>(savedState.occupiedCoordinates)
            : placementController != null
                ? placementController.GetInstalledObjectFootprintCoordinates(savedState.anchorCoordinate, definition, savedState.quarterTurns)
                : new List<Vector2Int> { savedState.anchorCoordinate };
        savedState.occupiedCoordinates = occupiedCoordinates;

        if (placementController != null)
        {
            placementController.ConfigureInstalledObjectRuntime(
                restoredInstallation,
                savedState.anchorCoordinate,
                savedState.quarterTurns,
                savedState.inputOutputState);
        }
        else
        {
            restoredInstallation.ConfigurePlacementRuntime(savedState.anchorCoordinate, savedState.quarterTurns, occupiedCoordinates);
            if (savedState.inputOutputState != null && restoredInstallation is InputOutputModule inputOutputModule)
            {
                inputOutputModule.ApplyPersistentState(savedState.inputOutputState);
            }
        }

        restoredInstallation.ApplyItemFilterMask(savedState.itemFilterMaskWords, savedState.itemFilterMaskInitialized);

        if (restoredInstallation is BoxObject restoredBoxObject && savedState.boxIsOpen.HasValue)
        {
            restoredBoxObject.SetOpenState(savedState.boxIsOpen.Value, false);
        }

        BindLoadedBlocksToInstallation(restoredInstallation, occupiedCoordinates);
        installationObject = restoredInstallation;
        return true;
    }

    private void BindLoadedBlocksToInstallation(MapObject installedObject, IReadOnlyList<Vector2Int> occupiedCoordinates)
    {
        if (installedObject == null || occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (loadedBlocks.TryGetValue(occupiedCoordinates[i], out Block block) && block != null)
            {
                block.SetMapObject(installedObject);
            }
        }
    }

    private sealed class ChunkSurfaceWorkerInput
    {
        public Vector2Int origin;
        public int chunkSizeInBlocks;
        public int resolution;
        public int cellCount;
        public int biomeGridMinX;
        public int biomeGridMinY;
        public int biomeGridWidth;
        public int biomeGridHeight;
        public TerrainBiome[] biomeGrid;
        public bool[] blockedWaterGrid;
        public float generatedSurfaceYOffset;
        public float terrainBlendJitter;
        public float terrainSurfaceVertexJitter;
        public int seed;
    }

    private void ReleaseChunkBlocksToPool(Transform chunkTransform)
    {
        if (!Application.isPlaying || chunkTransform == null)
        {
            return;
        }

        BlockPool resolvedBlockPool = ResolveBlockPool();
        if (resolvedBlockPool == null)
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

            resolvedBlockPool.Release(block);
        }
    }

    private void RestoreChunkBlockStates(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return;
        }

        Block[] chunkBlocks = chunkTransform.GetComponentsInChildren<Block>(true);
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            RestoreBlockState(chunkBlocks[i]);
        }
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

    private bool HasSavedOrLiveInstallationAtCoordinate(Vector2Int worldCoordinate)
    {
        EnsureResourceStateStore();
        return resourceStateStore != null
               && resourceStateStore.TryGetInstallationAnchorAtCoordinate(worldCoordinate, out _);
    }

    private void CleanupOrphanedLiveInstallations()
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        List<Vector2Int> liveAnchors = resourceStateStore.GetLiveInstallationAnchors();
        for (int i = 0; i < liveAnchors.Count; i++)
        {
            Vector2Int anchorCoordinate = liveAnchors[i];
            if (!resourceStateStore.TryGetLiveInstallation(anchorCoordinate, out InstallationObject installationObject, out BlockStateStore.InstallationSaveState state))
            {
                continue;
            }

            IReadOnlyList<Vector2Int> occupiedCoordinates = state != null && state.occupiedCoordinates != null && state.occupiedCoordinates.Count > 0
                ? state.occupiedCoordinates
                : installationObject.RuntimeOccupiedCoordinates;
            bool hasAnyLoadedBlock = false;
            for (int coordinateIndex = 0; coordinateIndex < occupiedCoordinates.Count; coordinateIndex++)
            {
                if (loadedBlocks.TryGetValue(occupiedCoordinates[coordinateIndex], out Block loadedBlock) && loadedBlock != null)
                {
                    hasAnyLoadedBlock = true;
                    break;
                }
            }

            if (hasAnyLoadedBlock)
            {
                continue;
            }

            resourceStateStore.UnregisterLiveInstallation(anchorCoordinate);
            if (installationObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(installationObject.gameObject);
                }
                else
                {
                    DestroyImmediate(installationObject.gameObject);
                }
            }
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

    private InstallationPlacementController ResolveInstallationPlacementController()
    {
        if (installationRestoreController != null)
        {
            return installationRestoreController;
        }

        InstallationPlacementController[] controllers = Resources.FindObjectsOfTypeAll<InstallationPlacementController>();
        for (int i = 0; i < controllers.Length; i++)
        {
            InstallationPlacementController controller = controllers[i];
            if (controller != null && controller.gameObject.scene.IsValid())
            {
                installationRestoreController = controller;
                return installationRestoreController;
            }
        }

        return null;
    }

    private InstallationBackgroundSimulator ResolveInstallationBackgroundSimulator()
    {
        if (installationBackgroundSimulator != null)
        {
            return installationBackgroundSimulator;
        }

        installationBackgroundSimulator = GetComponent<InstallationBackgroundSimulator>();
        if (installationBackgroundSimulator == null)
        {
            installationBackgroundSimulator = gameObject.AddComponent<InstallationBackgroundSimulator>();
        }

        return installationBackgroundSimulator;
    }

    private BlockPool ResolveBlockPool()
    {
        if (blockPool != null)
        {
            return blockPool;
        }

        blockPool = GetComponent<BlockPool>();
        if (blockPool == null)
        {
            blockPool = gameObject.AddComponent<BlockPool>();
        }

        return blockPool;
    }

    private static ItemDefinition ResolveInstallationDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
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
                prefab = oreResources[i].Prefab;
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
                prefab = treeResources[i].Prefab;
            }
        }

        return prefab != null;
    }

    private bool TryEvaluateResourceEntry(Vector2Int worldCoordinate, ResourceEntry entry, out float score)
    {
        score = float.MinValue;
        if (entry.Prefab == null || entry.spawnChance <= 0f)
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

    private List<ResourceEntry> GetStarterTreeEntries()
    {
        List<ResourceEntry> entries = new List<ResourceEntry>();

        for (int i = 0; i < treeResources.Count; i++)
        {
            if (treeResources[i].Prefab != null)
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

}
