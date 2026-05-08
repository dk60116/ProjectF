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
    private const float MinOreBodyScaleRatioLimit = 0.5f;
    private const float MaxOreBodyScaleRatioLimit = 1f;
    private const float BackgroundConveyorSimulationInterval = 0.05f;
    private const int BackgroundConveyorSimulationPassesPerTick = 1;

    private static readonly ProfilerMarker TickConveyorDataMotionsMarker = new ProfilerMarker("TerrainGenerator.TickConveyorDataMotions");
    private static readonly ProfilerMarker TickConveyorsMarker = new ProfilerMarker("TerrainGenerator.TickConveyors");
    private static readonly ProfilerMarker TickBackgroundConveyorsMarker = new ProfilerMarker("TerrainGenerator.TickBackgroundConveyors");
    private static readonly ProfilerMarker TickConveyorDotsMarker = new ProfilerMarker("TerrainGenerator.TickConveyorDots");
    private static readonly ProfilerMarker RefreshChunksMarker = new ProfilerMarker("TerrainGenerator.RefreshTrackedChunks");
    private static readonly ProfilerMarker RefreshChunkLoadScanMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkLoadScan");
    private static readonly ProfilerMarker RefreshChunkLoadSortMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkLoadSort");
    private static readonly ProfilerMarker RefreshChunkGenerationQueueMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkGenerationQueue");
    private static readonly ProfilerMarker RefreshChunkUnloadScanMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkUnloadScan");
    private static readonly ProfilerMarker FloorObjectVirtualizationMarker = new ProfilerMarker("TerrainGenerator.FloorObjectVirtualization");
    private static readonly ProfilerMarker FloorObjectVirtualizationRebuildMarker = new ProfilerMarker("TerrainGenerator.FloorObjectVirtualization.RebuildScan");
    private static readonly ProfilerMarker FloorObjectVirtualizationScanMarker = new ProfilerMarker("TerrainGenerator.FloorObjectVirtualization.Scan");
    private static readonly ProfilerMarker GenerateChunkCoroutineStepMarker = new ProfilerMarker("TerrainGenerator.GenerateChunkCoroutineStep");
    private static readonly ProfilerMarker UnloadChunkCoroutineStepMarker = new ProfilerMarker("TerrainGenerator.UnloadChunkCoroutineStep");
    private static readonly ProfilerMarker RestoreSavedInstallationMarker = new ProfilerMarker("TerrainGenerator.RestoreSavedInstallation");
    private static readonly ProfilerMarker SimulateSavedInstallationMarker = new ProfilerMarker("TerrainGenerator.SimulateSavedInstallation");
    private static readonly ProfilerMarker InstantiateSavedInstallationMarker = new ProfilerMarker("TerrainGenerator.InstantiateSavedInstallation");
    private static readonly ProfilerMarker BindLoadedInstallationBlocksMarker = new ProfilerMarker("TerrainGenerator.BindLoadedInstallationBlocks");
    private static readonly ProfilerMarker UnloadChunkCollectBlocksMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.CollectBlocks");
    private static readonly ProfilerMarker UnloadChunkSaveStatesMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.SaveStates");
    private static readonly ProfilerMarker UnloadChunkCollectAnchorsMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.CollectAnchors");
    private static readonly ProfilerMarker UnloadChunkRemoveLookupMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.RemoveLookup");
    private static readonly ProfilerMarker UnloadChunkReleaseBlocksMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.ReleaseBlocks");
    private static readonly ProfilerMarker UnloadChunkCleanupInstallationsMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.CleanupInstallations");
    private static readonly ProfilerMarker UnloadChunkSleepBlocksMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.SleepBlocks");
    private static readonly ProfilerMarker UnloadChunkSleepInstallationsMarker = new ProfilerMarker("TerrainGenerator.UnloadChunk.SleepInstallations");
    private static readonly ProfilerMarker WakeChunkViewMarker = new ProfilerMarker("TerrainGenerator.WakeChunkView");
    private static readonly ProfilerMarker ApplyChunkSurfaceMarker = new ProfilerMarker("TerrainGenerator.ApplyChunkBiomeSurface");

    public static TerrainGenerator Active { get; private set; }

    public static TerrainGenerator ResolveActive()
    {
        return Active != null
            ? Active
            : UnityEngine.Object.FindObjectOfType<TerrainGenerator>();
    }

    public int CurrentSeed => seed;

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

    private sealed class ConveyorLine
    {
        public int id;
        public bool isCycle;
        public bool simulationCacheValid;
        public readonly List<Block> blocks = new List<Block>();
        public int[] frontColumn0LaneIndices = Array.Empty<int>();
        public int[] frontColumn1LaneIndices = Array.Empty<int>();
        public int[] backColumn0LaneIndices = Array.Empty<int>();
        public int[] backColumn1LaneIndices = Array.Empty<int>();
        public float[] withinColumn0PathLengths = Array.Empty<float>();
        public float[] withinColumn1PathLengths = Array.Empty<float>();
        public float[] nextColumn0PathLengths = Array.Empty<float>();
        public float[] nextColumn1PathLengths = Array.Empty<float>();

        public ConveyorLine(int id)
        {
            this.id = id;
        }
    }

    private readonly struct ConveyorLineSlot
    {
        public ConveyorLineSlot(int lineId, int slotIndex, int lineLength, bool isCycle)
        {
            this.lineId = lineId;
            this.slotIndex = slotIndex;
            this.lineLength = lineLength;
            this.isCycle = isCycle;
        }

        private readonly int lineId;
        private readonly int slotIndex;
        private readonly int lineLength;
        private readonly bool isCycle;

        public int LineId => lineId;
        public int SlotIndex => slotIndex;
        public int LineLength => lineLength;
        public bool IsCycle => isCycle;
    }

    private readonly struct BeltItemLineLaneKey : IEquatable<BeltItemLineLaneKey>
    {
        public BeltItemLineLaneKey(Block block, int laneIndex)
        {
            Block = block;
            LaneIndex = laneIndex;
        }

        public readonly Block Block;
        public readonly int LaneIndex;

        public bool Equals(BeltItemLineLaneKey other)
        {
            return Block == other.Block && LaneIndex == other.LaneIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is BeltItemLineLaneKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Block != null ? Block.GetInstanceID() : 0) * 397) ^ LaneIndex;
            }
        }
    }

    [Serializable]
    public struct BlockSet
    {
        [SerializeField]
        private Block.BlockType type;

        public Block normal;

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

    private Resource stone;
    private Resource coal;
    private Resource iron;
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

    [SerializeField, Min(1)]
    private int chunkInstallationRestoresPerFrame = 1;

    [SerializeField, Min(1)]
    private int chunkRestoreBackgroundSimulationIterations = 4;

    [SerializeField, Min(1)]
    private int chunkUnloadsPerFrame = 1;

    [Header("Object Virtualization")]
    [SerializeField]
    private bool virtualizeDistantFloorObjects = true;

    [SerializeField, Min(0)]
    private int floorObjectLiveRadius = 10;

    [SerializeField, Min(0.02f)]
    private float floorObjectVirtualizationInterval = 0.2f;

    [SerializeField, Min(1)]
    private int floorObjectVirtualizationConversionsPerTick = 256;

    [SerializeField]
    private bool virtualizeConveyorItems = true;

    [SerializeField]
    private bool virtualizeConveyorBelts = true;

    [SerializeField, Min(16)]
    private int conveyorWakeQueueProcessLimit = 4096;

    [SerializeField, Min(0.02f)]
    private float conveyorActiveFullScanInterval = 0.25f;

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
    private float generatedSurfaceBlendTextureTiling = 0.28f;

    [SerializeField, Min(0.01f)]
    private float generatedSurfaceBlendNoiseScale = 0.11f;

    [SerializeField, Range(0f, 0.5f)]
    private float generatedSurfaceBlendNoiseStrength = 0.18f;

    [SerializeField, HideInInspector]
    private Shader generatedSurfaceBlendShader;

    [SerializeField, HideInInspector]
    private Material generatedSurfaceWaterMaterial;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendSandTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendDirtTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendGrassTexture;

    [SerializeField, HideInInspector]
    private Texture2D generatedSurfaceBlendForestTexture;

    [SerializeField, HideInInspector]
    private bool generatedSurfaceBlendTextureDefaultsInitialized;

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
    private float riverWidth = 2.7f;

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
    private float stoneSpawnChance = 0.1f;

    [SerializeField, Range(0f, 1f)]
    [HideInInspector]
    private float coalSpawnChance = 0.1f;

    [SerializeField, Range(0f, 1f)]
    [HideInInspector]
    private float ironSpawnChance = 0.1f;

    [SerializeField, Range(0f, 1f)]
    [HideInInspector]
    private float cooperSpawnChance = 0.1f;

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
    private int starterOreMinResourceCount = 256;

    [SerializeField, Min(1), HideInInspector]
    private int starterOreMaxResourceCount = 512;

    [SerializeField, Min(1), HideInInspector]
    private int normalOreMinResourceCount = 512;

    [SerializeField, Min(1), HideInInspector]
    private int normalOreMaxResourceCount = 1024;

    [SerializeField, Range(MinOreBodyScaleRatioLimit, MaxOreBodyScaleRatioLimit)]
    private float oreMinimumBodyScaleRatio = 0.5f;

    [SerializeField, Range(MinOreBodyScaleRatioLimit, MaxOreBodyScaleRatioLimit)]
    private float oreMaximumBodyScaleRatio = 1f;

    [SerializeField, Min(1)]
    private int oreScaleAtResourceCount = 1000;

    [SerializeField, Range(1f, 6f)]
    private float treeSingleDensityMultiplier = 2.4f;

    [SerializeField, Range(1f, 6f)]
    private float treePatchDensityMultiplier = 2.1f;

    [SerializeField, Range(1f, 3f)]
    private float treePatchSizeMultiplier = 1.35f;

    private readonly Dictionary<Vector2Int, Transform> loadedChunks = new Dictionary<Vector2Int, Transform>();
    private readonly Dictionary<Vector2Int, Block> loadedBlocks = new Dictionary<Vector2Int, Block>();
    private readonly Dictionary<Vector2Int, Transform> sleepingChunkViews = new Dictionary<Vector2Int, Transform>();
    private readonly Dictionary<Vector2Int, InstallationObject> sleepingInstallationViews = new Dictionary<Vector2Int, InstallationObject>();
    private readonly HashSet<Block> activeConveyors = new HashSet<Block>();
    private readonly List<Block> conveyorTickBuffer = new List<Block>();
    private readonly HashSet<Block> activeConveyorDataMotionBlocks = new HashSet<Block>();
    private readonly List<Block> conveyorDataMotionTickBuffer = new List<Block>();
    private readonly List<Block> sortedActiveConveyors = new List<Block>();
    private readonly HashSet<Block> activeConveyorDotVisuals = new HashSet<Block>();
    private readonly List<Block> activeConveyorDotVisualList = new List<Block>();
    private readonly List<Block> conveyorDotVisualTickBuffer = new List<Block>();
    private readonly List<Block> pendingConveyorSlotDotRefreshBlocks = new List<Block>();
    private readonly Matrix4x4[] conveyorSlotDotInstanceMatrices = new Matrix4x4[MaxConveyorSlotDotInstancesPerBatch];
    private int conveyorSlotDotInstanceMatrixCount;
    private Mesh conveyorSlotDotInstancedMesh;
    private Material conveyorSlotDotInstancedMaterial;
    private int pendingConveyorSlotDotRefreshIndex;
    private bool conveyorSlotDotVisibilityInitialized;
    private bool lastShowConveyorSlotDots;
    private bool beltItemLineVisibilityInitialized;
    private bool lastShowBeltItemLine;
    private bool beltItemLineVisualsDirty;
    private bool beltItemLineDebugCacheDirty = true;
    private bool applyingBeltItemLineRuntimeVisibility;
    private bool pendingBeltItemLineDebugRefreshAll;
    private readonly Dictionary<BeltItemLineLaneKey, int> beltItemLineDebugRunIds = new Dictionary<BeltItemLineLaneKey, int>();
    private readonly List<BeltItemLineLaneKey> beltItemLineDebugOccupiedLanes = new List<BeltItemLineLaneKey>(512);
    private readonly HashSet<BeltItemLineLaneKey> beltItemLineDebugOccupiedLaneSet = new HashSet<BeltItemLineLaneKey>();
    private readonly HashSet<BeltItemLineLaneKey> beltItemLineDebugIncomingLanes = new HashSet<BeltItemLineLaneKey>();
    private readonly HashSet<BeltItemLineLaneKey> beltItemLineDebugVisitedLanes = new HashSet<BeltItemLineLaneKey>();
    private readonly List<Block> pendingBeltItemLineDebugRefreshBlocks = new List<Block>(512);
    private readonly HashSet<Block> pendingBeltItemLineDebugRefreshSet = new HashSet<Block>();
    private readonly HashSet<Block> conveyorItemVisualBlocks = new HashSet<Block>();
    private readonly HashSet<Block> conveyorItemVisualDirtyBlocks = new HashSet<Block>();
    private int pendingBeltItemLineDebugRefreshIndex;
    private int conveyorItemVisualBlockSetVersion;
    private readonly Dictionary<Block, int> conveyorNetworkIds = new Dictionary<Block, int>();
    private readonly Dictionary<int, float> conveyorNetworkRetryTimes = new Dictionary<int, float>();
    private readonly HashSet<int> conveyorNetworkSleepingIds = new HashSet<int>();
    private readonly HashSet<int> conveyorNetworkActiveIds = new HashSet<int>();
    private readonly HashSet<int> conveyorNetworkSleepCheckQueuedIds = new HashSet<int>();
    private readonly List<int> conveyorNetworkSleepCheckBuffer = new List<int>();
    private readonly Queue<Block> conveyorNetworkBuildQueue = new Queue<Block>();
    private readonly Queue<Block> conveyorWakeQueue = new Queue<Block>();
    private readonly HashSet<Block> conveyorWakeQueued = new HashSet<Block>();
    private readonly List<ConveyorLine> conveyorLines = new List<ConveyorLine>();
    private readonly Dictionary<Block, ConveyorLineSlot> conveyorLineSlots = new Dictionary<Block, ConveyorLineSlot>();
    private readonly HashSet<Block> conveyorLineVisited = new HashSet<Block>();
    private readonly Dictionary<Block, int> conveyorLineBuildIndices = new Dictionary<Block, int>();
    private readonly HashSet<int> conveyorLinesTickedThisFrame = new HashSet<int>();
    private readonly List<Block> conveyorLineTouchedBlocks = new List<Block>();
    private readonly HashSet<Block> conveyorLineTouchedSet = new HashSet<Block>();
    private readonly HashSet<int> conveyorWakeQueuedLineIds = new HashSet<int>();
    private readonly HashSet<Block> deferredConveyorRuntimeRefreshBlocks = new HashSet<Block>();
    private readonly HashSet<Block> deferredConveyorNetworkWakeBlocks = new HashSet<Block>();
    private readonly HashSet<Vector2Int> virtualizedFloorObjectCoordinates = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> backgroundConveyorDirtyCoordinates = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> backgroundConveyorWakeCoordinates = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, TerrainBiome> tileBiomeCache = new Dictionary<Vector2Int, TerrainBiome>();
    private readonly Dictionary<Vector2Int, bool> rawWaterCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> directWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> bufferedWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<TerrainBiome, Material> biomeMaterialCache = new Dictionary<TerrainBiome, Material>();
    private readonly List<Vector2Int> floorObjectVirtualizationScanCoordinates = new List<Vector2Int>();

    private bool hasGeneratedChunks;
    private bool hasSeedInitialized;
    private bool isMaterializingVirtualFloorObjects;
    private bool activeConveyorOrderDirty = true;
    private bool conveyorNetworkCacheDirty = true;
    private bool conveyorLineCacheDirty = true;
    private int deferredConveyorRuntimeRefreshDepth;
    private float nextConveyorActiveFullScanTime;
    private float nextBackgroundConveyorSimulationTime;
    private Vector2Int currentCenterChunk;
    private float nextFloorObjectVirtualizationTime;
    private int floorObjectVirtualizationScanIndex;
    private BlockStateStore resourceStateStore;
    private InstallationPlacementController installationRestoreController;
    private InstallationBackgroundSimulator installationBackgroundSimulator;
    private BlockPool blockPool;
    private InstallationObjectPool installationObjectPool;
    private PortableItemRenderer portableItemRenderer;
    private VirtualConveyorBeltRenderer virtualConveyorBeltRenderer;
    private TerrainChunkStreamingScheduler chunkStreamingScheduler;
    private Transform sleepingChunkViewRoot;
    private Transform sleepingInstallationViewRoot;

    private readonly List<ResourceEntry> starterTreeCacheEntries = new List<ResourceEntry>();
    private readonly List<Vector2Int> starterTreeCacheCandidates = new List<Vector2Int>();
    private readonly Dictionary<Vector2Int, Resource> starterTreeCacheLookup = new Dictionary<Vector2Int, Resource>();
    private int starterTreeCacheSeed = int.MinValue;
    private int starterTreeCacheConfigHash = int.MinValue;
    private bool starterTreeCacheValid;
    private Material generatedSurfaceBlendMaterial;

    private void OnValidate()
    {
        MigrateLegacyResourcesIfNeeded();
        UpgradeLegacyGeneratedSurfaceBlendSettings();
        starterOreMaxResourceCount = Mathf.Max(starterOreMinResourceCount, starterOreMaxResourceCount);
        normalOreMaxResourceCount = Mathf.Max(normalOreMinResourceCount, normalOreMaxResourceCount);
        starterTreeMaxCount = Mathf.Max(starterTreeMinCount, starterTreeMaxCount);
        NormalizeOreBodyScaleSettings();
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

    private void Awake()
    {
        Active = this;
        EnsurePortableItemRenderer();
        EnsureVirtualConveyorBeltRenderer();
    }

    private void NormalizeOreBodyScaleSettings()
    {
        oreMinimumBodyScaleRatio = Mathf.Clamp(
            oreMinimumBodyScaleRatio,
            MinOreBodyScaleRatioLimit,
            MaxOreBodyScaleRatioLimit);
        oreMaximumBodyScaleRatio = Mathf.Clamp(
            oreMaximumBodyScaleRatio,
            oreMinimumBodyScaleRatio,
            MaxOreBodyScaleRatioLimit);
    }

    private void Start()
    {
        MigrateLegacyResourcesIfNeeded();
        UpgradeLegacyGeneratedSurfaceBlendSettings();
        NormalizeOreBodyScaleSettings();
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        SyncResourceEntryDefinitions();
        EnsureResourceStateStore();
        EnsurePortableItemRenderer();
        EnsureVirtualConveyorBeltRenderer();

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

        using (TickConveyorDataMotionsMarker.Auto())
        {
            TickActiveConveyorDataMotions(Time.deltaTime);
        }

        using (TickConveyorsMarker.Auto())
        {
            TickActiveConveyors(Time.deltaTime);
        }

        using (TickBackgroundConveyorsMarker.Auto())
        {
            TickBackgroundConveyors();
        }

        using (TickConveyorDotsMarker.Auto())
        {
            SyncConveyorSlotDotRuntimeVisibility();
            TickPendingConveyorSlotDotRefreshes();
            SyncBeltItemLineRuntimeVisibility();
            TickPendingBeltItemLineDebugRefreshes();
            TickActiveConveyorDotVisuals(Time.deltaTime);
        }

        using (RefreshChunksMarker.Auto())
        {
            RefreshTrackedChunks();
        }

        using (FloorObjectVirtualizationMarker.Auto())
        {
            TickFloorObjectVirtualization();
        }
    }

    private void TickBackgroundConveyors()
    {
        if (Time.time < nextBackgroundConveyorSimulationTime)
        {
            return;
        }

        nextBackgroundConveyorSimulationTime = Time.time + BackgroundConveyorSimulationInterval;
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        backgroundConveyorDirtyCoordinates.Clear();
        resourceStateStore.SimulateSavedConveyorItems(
            BackgroundConveyorSimulationPassesPerTick,
            backgroundConveyorDirtyCoordinates);
        WakeLoadedConveyorsNearBackgroundConveyorChanges();
    }

    private void WakeLoadedConveyorsNearBackgroundConveyorChanges()
    {
        if (backgroundConveyorDirtyCoordinates.Count <= 0)
        {
            return;
        }

        backgroundConveyorWakeCoordinates.Clear();
        for (int i = 0; i < backgroundConveyorDirtyCoordinates.Count; i++)
        {
            Vector2Int coordinate = backgroundConveyorDirtyCoordinates[i];
            backgroundConveyorWakeCoordinates.Add(coordinate);
            backgroundConveyorWakeCoordinates.Add(coordinate + Vector2Int.up);
            backgroundConveyorWakeCoordinates.Add(coordinate + Vector2Int.down);
            backgroundConveyorWakeCoordinates.Add(coordinate + Vector2Int.left);
            backgroundConveyorWakeCoordinates.Add(coordinate + Vector2Int.right);
        }

        foreach (Vector2Int coordinate in backgroundConveyorWakeCoordinates)
        {
            if (!TryGetLoadedBlock(coordinate, out Block block)
                || block == null
                || !block.IsRuntimeConveyor)
            {
                continue;
            }

            block.WakeConveyorMoveAttemptsAround();
            block.RefreshConveyorActivityRegistration();
        }

        backgroundConveyorWakeCoordinates.Clear();
    }

    private void OnDisable()
    {
        if (Active == this)
        {
            Active = null;
        }

        activeConveyors.Clear();
        conveyorTickBuffer.Clear();
        activeConveyorDataMotionBlocks.Clear();
        conveyorDataMotionTickBuffer.Clear();
        sortedActiveConveyors.Clear();
        activeConveyorOrderDirty = true;
        conveyorNetworkIds.Clear();
        conveyorNetworkRetryTimes.Clear();
        conveyorNetworkSleepingIds.Clear();
        conveyorNetworkActiveIds.Clear();
        conveyorNetworkSleepCheckQueuedIds.Clear();
        conveyorNetworkSleepCheckBuffer.Clear();
        conveyorNetworkBuildQueue.Clear();
        conveyorWakeQueue.Clear();
        conveyorWakeQueued.Clear();
        conveyorWakeQueuedLineIds.Clear();
        deferredConveyorRuntimeRefreshBlocks.Clear();
        deferredConveyorNetworkWakeBlocks.Clear();
        deferredConveyorRuntimeRefreshDepth = 0;
        conveyorNetworkCacheDirty = true;
        ClearConveyorLineCache();
        nextConveyorActiveFullScanTime = 0f;
        ClearConveyorDotVisualState();
        conveyorSlotDotVisibilityInitialized = false;
        lastShowConveyorSlotDots = false;
        beltItemLineVisibilityInitialized = false;
        lastShowBeltItemLine = false;
        beltItemLineVisualsDirty = false;
        ClearBeltItemLineDebugCache();
        ClearPendingBeltItemLineDebugRefreshes();
        conveyorItemVisualBlocks.Clear();
        conveyorItemVisualDirtyBlocks.Clear();
        conveyorItemVisualBlockSetVersion++;
        virtualizedFloorObjectCoordinates.Clear();
        ClearFloorObjectVirtualizationScan();
        ClearPendingChunkGenerations();
    }

    public bool VirtualizeConveyorItems => virtualizeConveyorItems;
    public bool VirtualizeConveyorBelts => virtualizeConveyorBelts;
    public int ConveyorItemVisualBlockSetVersion => conveyorItemVisualBlockSetVersion;

    public void CopyLoadedBlocks(List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            if (pair.Value != null)
            {
                results.Add(pair.Value);
            }
        }
    }

    public int GetLoadedConveyorItemCount()
    {
        int count = 0;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (block != null)
            {
                count += block.GetRuntimeConveyorItemCount();
            }
        }

        return count;
    }

    public int GetInstallationItemCounts(Dictionary<int, int> countsByItemId)
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            countsByItemId?.Clear();
            return 0;
        }

        return resourceStateStore.GetInstallationItemCounts(countsByItemId);
    }

    public TerrainSaveData CaptureTerrainSaveState()
    {
        return new TerrainSaveData
        {
            seed = seed
        };
    }

    public MapSaveData CaptureMapSaveState()
    {
        EnsureResourceStateStore();
        MapSaveData mapSaveData = new MapSaveData();
        if (resourceStateStore == null)
        {
            return mapSaveData;
        }

        FlushLoadedRuntimeStateToStore();
        resourceStateStore.CaptureSaveState(mapSaveData);
        CaptureLoadedConveyorItemSaveStates(mapSaveData);
        return mapSaveData;
    }

    public void LoadFromSaveState(TerrainSaveData terrainSaveData, MapSaveData mapSaveData)
    {
        MigrateLegacyResourcesIfNeeded();
        NormalizeOreBodyScaleSettings();
        NormalizeResourceEntries(oreResources, normalOreMinResourceCount, normalOreMaxResourceCount, starterOreMinResourceCount, starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        SyncResourceEntryDefinitions();
        EnsureResourceStateStore();
        EnsurePortableItemRenderer();
        EnsureVirtualConveyorBeltRenderer();

        if (terrainSaveData != null)
        {
            seed = terrainSaveData.seed;
            chunkSize = Mathf.Max(4, chunkSize);
            loadRadius = Mathf.Max(0, loadRadius);
            unloadRadius = Mathf.Max(loadRadius + 1, unloadRadius);
            hasSeedInitialized = true;
        }

        InvalidateStarterTreeCache();
        InvalidateTerrainBiomeDataCaches();
        InvalidateTerrainBiomeMaterialCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks();
        resourceStateStore?.ApplySaveState(mapSaveData);

        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        RefreshChunks(currentCenterChunk, true);
        ProcessQueuedChunkGenerationsImmediate();
        ApplyLoadedConveyorItemSaveStates(mapSaveData);
        RefreshLoadedRuntimeRegistrations();
    }

    public void StartNewGeneratedMap(bool randomizeSeed)
    {
        if (randomizeSeed)
        {
            RandomizeSeed();
        }

        Generate();
        ProcessQueuedChunkGenerationsImmediate();
        RefreshLoadedRuntimeRegistrations();
    }

    public void FlushLoadedRuntimeStateToStore()
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        HashSet<InstallationObject> savedInstallations = new HashSet<InstallationObject>();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (block == null)
            {
                continue;
            }

            SaveLoadedBlockFloorObjects(block, VirtualObjectResidency.Live);

            if (block.MapObject is InstallationObject installationObject && savedInstallations.Add(installationObject))
            {
                resourceStateStore.SaveInstallation(installationObject);
                resourceStateStore.RegisterLiveInstallation(installationObject);
            }

            Resource resource = block.Resource;
            if (resource != null)
            {
                resourceStateStore.Save(block.Coordinate, resource);
            }
        }
    }

    private void CaptureLoadedConveyorItemSaveStates(MapSaveData mapSaveData)
    {
        if (mapSaveData == null)
        {
            return;
        }

        mapSaveData.conveyorItems ??= new List<ConveyorItemBlockSaveEntry>();

        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            Block block = pair.Value;
            if (block == null || !block.IsRuntimeConveyor)
            {
                continue;
            }

            ConveyorItemBlockSaveEntry entry = new ConveyorItemBlockSaveEntry
            {
                coordinate = pair.Key
            };
            block.CaptureConveyorItemSaveStates(entry.lanes);
            int existingEntryIndex = FindConveyorItemSaveEntryIndex(mapSaveData.conveyorItems, pair.Key);
            if (entry.lanes.Count > 0)
            {
                if (existingEntryIndex >= 0)
                {
                    mapSaveData.conveyorItems[existingEntryIndex] = entry;
                }
                else
                {
                    mapSaveData.conveyorItems.Add(entry);
                }
            }
            else if (existingEntryIndex >= 0)
            {
                mapSaveData.conveyorItems.RemoveAt(existingEntryIndex);
            }
        }
    }

    private static int FindConveyorItemSaveEntryIndex(List<ConveyorItemBlockSaveEntry> entries, Vector2Int coordinate)
    {
        if (entries == null)
        {
            return -1;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            ConveyorItemBlockSaveEntry entry = entries[i];
            if (entry != null && entry.coordinate == coordinate)
            {
                return i;
            }
        }

        return -1;
    }

    private void ApplyLoadedConveyorItemSaveStates(MapSaveData mapSaveData)
    {
        if (mapSaveData?.conveyorItems == null)
        {
            return;
        }

        for (int i = 0; i < mapSaveData.conveyorItems.Count; i++)
        {
            ConveyorItemBlockSaveEntry entry = mapSaveData.conveyorItems[i];
            if (entry == null
                || entry.lanes == null
                || !loadedBlocks.TryGetValue(entry.coordinate, out Block block)
                || block == null)
            {
                continue;
            }

            IReadOnlyList<ConveyorItemLaneSaveState> lanes = entry.lanes;
            EnsureResourceStateStore();
            if (resourceStateStore != null
                && resourceStateStore.TryGetConveyorItems(entry.coordinate, out List<ConveyorItemLaneSaveState> storedLanes))
            {
                lanes = storedLanes;
            }

            block.ApplyConveyorItemSaveStates(lanes);
        }
    }

    private void RefreshLoadedRuntimeRegistrations()
    {
        MarkConveyorNetworkDirty();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            RefreshRestoredBlockRuntimeRegistration(pair.Value);
        }
    }

    private static void RefreshRestoredBlockRuntimeRegistration(Block block)
    {
        if (block == null)
        {
            return;
        }

        bool shouldWakeConveyor = block.IsRuntimeConveyor && block.GetRuntimeConveyorItemCount() > 0;
        if (shouldWakeConveyor)
        {
            block.WakeConveyorMoveAttemptsAround();
        }

        block.RefreshConveyorActivityRegistration(shouldWakeConveyor);
        block.RefreshConveyorSlotDotVisuals();
    }

    public void CopyConveyorItemVisualBlocks(List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (Block block in conveyorItemVisualBlocks)
        {
            if (block != null)
            {
                results.Add(block);
            }
        }
    }

    public void CopyConveyorItemVisualDirtyBlocks(List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (Block block in conveyorItemVisualDirtyBlocks)
        {
            if (block != null)
            {
                results.Add(block);
            }
        }

        conveyorItemVisualDirtyBlocks.Clear();
    }

    public void Generate()
    {
        MigrateLegacyResourcesIfNeeded();
        NormalizeOreBodyScaleSettings();
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
        NormalizeOreBodyScaleSettings();
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
        SetSeed(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
    }

    public void SetSeed(int value)
    {
        seed = value;
        hasSeedInitialized = true;
        InvalidateStarterTreeCache();
        InvalidateTerrainBiomeDataCaches();
        InvalidateTerrainBiomeMaterialCaches();
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

        using (RefreshChunkLoadScanMarker.Auto())
        {
            for (int chunkY = centerChunk.y - normalizedLoadRadius; chunkY <= centerChunk.y + normalizedLoadRadius; chunkY++)
            {
                for (int chunkX = centerChunk.x - normalizedLoadRadius; chunkX <= centerChunk.x + normalizedLoadRadius; chunkX++)
                {
                    Vector2Int chunkCoordinate = new Vector2Int(chunkX, chunkY);

                    if (forceReload || (!loadedChunks.ContainsKey(chunkCoordinate) && !IsChunkGenerationActive(chunkCoordinate)))
                    {
                        chunksToGenerate.Add(chunkCoordinate);
                    }
                }
            }
        }

        using (RefreshChunkLoadSortMarker.Auto())
        {
            chunksToGenerate.Sort((left, right) =>
            {
                int leftDistance = GetChunkDistanceSqr(left, centerChunk);
                int rightDistance = GetChunkDistanceSqr(right, centerChunk);
                return leftDistance.CompareTo(rightDistance);
            });
        }

        using (RefreshChunkGenerationQueueMarker.Auto())
        {
            for (int i = 0; i < chunksToGenerate.Count; i++)
            {
                QueueChunkGeneration(chunksToGenerate[i], normalizedChunkSize);
            }

            EnsureChunkGenerationProcessing();
        }

        using (RefreshChunkUnloadScanMarker.Auto())
        {
            foreach (KeyValuePair<Vector2Int, Transform> loadedChunk in loadedChunks)
            {
                int distanceX = Mathf.Abs(loadedChunk.Key.x - centerChunk.x);
                int distanceY = Mathf.Abs(loadedChunk.Key.y - centerChunk.y);

                if (distanceX > normalizedUnloadRadius || distanceY > normalizedUnloadRadius)
                {
                    QueueChunkUnload(loadedChunk.Key);
                }
            }

            EnsureChunkUnloadProcessing();
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

        if (loadedChunks.TryGetValue(chunkCoordinate, out Transform existingChunk))
        {
            SaveChunkResourceStates(existingChunk);
            RemoveChunkBlocksFromLookup(existingChunk);
            ReleaseChunkBlocksToPool(existingChunk);
            DestroyChunkObject(existingChunk.gameObject);
            loadedChunks.Remove(chunkCoordinate);
        }

        Vector2Int origin = new Vector2Int(chunkCoordinate.x * normalizedChunkSize, chunkCoordinate.y * normalizedChunkSize);
        if (TryTakeSleepingChunkView(chunkCoordinate, normalizedChunkSize, out Transform sleepingChunk))
        {
            IEnumerator wakeRoutine = WakeSleepingChunkViewRoutine(chunkCoordinate, sleepingChunk, origin, allowYield);
            while (wakeRoutine.MoveNext())
            {
                if (allowYield && wakeRoutine.Current != null)
                {
                    yield return wakeRoutine.Current;
                }
            }

            MarkChunkGenerationComplete(chunkCoordinate);

            if (!IsChunkWithinRadius(chunkCoordinate, currentCenterChunk, GetEffectiveUnloadRadius()))
            {
                QueueChunkUnload(chunkCoordinate);
                EnsureChunkUnloadProcessing();
            }

            yield break;
        }

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
                Block block = CreateBlock(chunkObject.transform, groundSet, Block.BlockType.Ground, worldCoordinate, localPosition, false, 0f);
                if (block == null)
                {
                    continue;
                }

                ApplyBlockBiomeVisuals(block, visualData);
                if ((!HasSavedOrLiveInstallationAtCoordinate(worldCoordinate)
                        || CanSpawnResourceUnderMiningMachine(worldCoordinate))
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

        IEnumerator installationRestoreRoutine = RestoreChunkInstallationsRoutine(chunkObject.transform, allowYield);
        while (installationRestoreRoutine.MoveNext())
        {
            if (allowYield && installationRestoreRoutine.Current != null)
            {
                yield return installationRestoreRoutine.Current;
            }
        }

        IEnumerator blockStateRestoreRoutine = RestoreChunkBlockStatesRoutine(chunkObject.transform, allowYield);
        while (blockStateRestoreRoutine.MoveNext())
        {
            if (allowYield && blockStateRestoreRoutine.Current != null)
            {
                yield return blockStateRestoreRoutine.Current;
            }
        }

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

        if (allowYield)
        {
            yield return null;
        }

        ApplyChunkBiomeSurface(chunkObject.transform, chunkSurface);

        if (allowYield)
        {
            yield return null;
        }

        MarkChunkGenerationComplete(chunkCoordinate);

        if (!IsChunkWithinRadius(chunkCoordinate, currentCenterChunk, GetEffectiveUnloadRadius()))
        {
            QueueChunkUnload(chunkCoordinate);
            EnsureChunkUnloadProcessing();
        }
    }

    private void UnloadChunk(Vector2Int chunkCoordinate)
    {
        IEnumerator routine = UnloadChunkRoutine(chunkCoordinate, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator UnloadChunkRoutine(Vector2Int chunkCoordinate, bool allowYield)
    {
        if (IsChunkGenerationActive(chunkCoordinate))
        {
            yield break;
        }

        if (!loadedChunks.TryGetValue(chunkCoordinate, out Transform chunkTransform))
        {
            yield break;
        }

        Block[] chunkBlocks;
        using (UnloadChunkCollectBlocksMarker.Auto())
        {
            chunkBlocks = GetLoadedChunkBlockSnapshot(chunkCoordinate, chunkTransform);
        }

        if (allowYield)
        {
            yield return null;
        }

        IEnumerator saveRoutine = SaveChunkResourceStatesRoutine(chunkBlocks, allowYield);
        while (saveRoutine.MoveNext())
        {
            yield return saveRoutine.Current;
        }

        List<Vector2Int> affectedInstallationAnchors = new List<Vector2Int>();
        IEnumerator collectAnchorsRoutine = CollectChunkInstallationAnchorsRoutine(chunkBlocks, affectedInstallationAnchors, allowYield);
        while (collectAnchorsRoutine.MoveNext())
        {
            yield return collectAnchorsRoutine.Current;
        }

        IEnumerator removeLookupRoutine = RemoveChunkBlocksFromLookupRoutine(chunkBlocks, allowYield);
        while (removeLookupRoutine.MoveNext())
        {
            yield return removeLookupRoutine.Current;
        }

        if (Application.isPlaying)
        {
            IEnumerator sleepBlocksRoutine = SleepChunkBlocksForStreamingRoutine(chunkBlocks, allowYield);
            while (sleepBlocksRoutine.MoveNext())
            {
                yield return sleepBlocksRoutine.Current;
            }
        }
        else
        {
            IEnumerator releaseRoutine = ReleaseChunkBlocksToPoolRoutine(chunkBlocks, allowYield);
            while (releaseRoutine.MoveNext())
            {
                yield return releaseRoutine.Current;
            }
        }

        loadedChunks.Remove(chunkCoordinate);
        if (allowYield)
        {
            yield return null;
        }

        IEnumerator cleanupRoutine = CleanupOrphanedLiveInstallationsRoutine(affectedInstallationAnchors, allowYield);
        while (cleanupRoutine.MoveNext())
        {
            yield return cleanupRoutine.Current;
        }

        if (Application.isPlaying)
        {
            SleepChunkView(chunkCoordinate, chunkTransform);
        }
        else
        {
            DestroyChunkObject(chunkTransform.gameObject);
        }
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
                DestroyChunkObject(chunkObjects[i].gameObject);
            }
        }

        DestroySleepingViewCaches();

        loadedChunks.Clear();
        loadedBlocks.Clear();
        activeConveyors.Clear();
        conveyorTickBuffer.Clear();
        activeConveyorDataMotionBlocks.Clear();
        conveyorDataMotionTickBuffer.Clear();
        sortedActiveConveyors.Clear();
        activeConveyorOrderDirty = true;
        conveyorNetworkIds.Clear();
        conveyorNetworkRetryTimes.Clear();
        conveyorNetworkSleepingIds.Clear();
        conveyorNetworkActiveIds.Clear();
        conveyorNetworkSleepCheckQueuedIds.Clear();
        conveyorNetworkSleepCheckBuffer.Clear();
        conveyorNetworkBuildQueue.Clear();
        conveyorWakeQueue.Clear();
        conveyorWakeQueued.Clear();
        conveyorWakeQueuedLineIds.Clear();
        deferredConveyorRuntimeRefreshBlocks.Clear();
        deferredConveyorNetworkWakeBlocks.Clear();
        deferredConveyorRuntimeRefreshDepth = 0;
        conveyorNetworkCacheDirty = true;
        ClearConveyorLineCache();
        nextConveyorActiveFullScanTime = 0f;
        ClearConveyorDotVisualState();
        conveyorSlotDotVisibilityInitialized = false;
        lastShowConveyorSlotDots = false;
        beltItemLineVisibilityInitialized = false;
        lastShowBeltItemLine = false;
        beltItemLineVisualsDirty = false;
        ClearBeltItemLineDebugCache();
        ClearPendingBeltItemLineDebugRefreshes();
        conveyorItemVisualBlocks.Clear();
        conveyorItemVisualDirtyBlocks.Clear();
        conveyorItemVisualBlockSetVersion++;
        virtualizedFloorObjectCoordinates.Clear();
        ClearFloorObjectVirtualizationScan();
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


}
