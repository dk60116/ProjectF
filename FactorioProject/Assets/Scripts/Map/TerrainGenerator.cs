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
    private const int MinMapSize = 32;
    private const float IslandLandRadius = 0.78f;
    private const float IslandCoastNoiseScale = 0.024f;
    private const float IslandCoastDetailNoiseScale = 0.067f;
    private const float IslandCoastIrregularity = 0.09f;
    private const float BackgroundConveyorSimulationInterval = 0.05f;
    private const float BackgroundConveyorBudgetContinuationInterval = 0.02f;
    private const int BackgroundConveyorSimulationPassesPerTick = 1;
    private const float GeneratedSurfaceBaseInset = 0.0035f;
    private const float GeneratedSurfaceBiomeLayerStep = 0.004f;
    private const float GeneratedWaterWallVerticalOverlap = 0.018f;
    private const int GeneratedWaterDepthSearchRadius = 4;
    private const float GeneratedWaterDepthDeepDistance = 2.65f;
    private const int GeneratedWaterFoamRenderQueue = 3010;
    private const int GeneratedWaterGlintRenderQueue = 3012;

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
    public int TerrainGenerationVersion => terrainGenerationVersion;
    public int CurrentMapSize => GetNormalizedMapSize();

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

    private const int GeneratedSurfaceBiomeMaterialCount = 6;
    private const int GeneratedSurfaceFoamMaterialIndex = GeneratedSurfaceBiomeMaterialCount;
    private const int GeneratedSurfaceMaterialCount = GeneratedSurfaceFoamMaterialIndex + 1;

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
        public readonly float[] blendWeightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
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
        public int[] frontLaneIndices = Array.Empty<int>();
        public int[] backLaneIndices = Array.Empty<int>();
        public float[] withinPathLengths = Array.Empty<float>();
        public float[] nextPathLengths = Array.Empty<float>();

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

    private sealed class ConveyorCornerGroup
    {
        public int id;
        public bool isCycle;
        public readonly List<Block> blocks = new List<Block>();

        public ConveyorCornerGroup(int id)
        {
            this.id = id;
        }
    }

    private readonly struct ConveyorCornerGroupSlot
    {
        public ConveyorCornerGroupSlot(int groupId, int slotIndex, int groupLength, bool isCycle)
        {
            this.groupId = groupId;
            this.slotIndex = slotIndex;
            this.groupLength = groupLength;
            this.isCycle = isCycle;
        }

        private readonly int groupId;
        private readonly int slotIndex;
        private readonly int groupLength;
        private readonly bool isCycle;

        public int GroupId => groupId;
        public int SlotIndex => slotIndex;
        public int GroupLength => groupLength;
        public bool IsCycle => isCycle;
    }

    public enum ConveyorRuntimeWakeMode
    {
        None,
        Flow,
        Around
    }

    private struct ConveyorLineWakeRange
    {
        public int minSlotIndex;
        public int maxSlotIndex;
        public bool fullLine;

        public ConveyorLineWakeRange(int minSlotIndex, int maxSlotIndex, bool fullLine)
        {
            this.minSlotIndex = minSlotIndex;
            this.maxSlotIndex = maxSlotIndex;
            this.fullLine = fullLine;
        }

        public void Include(ConveyorLineWakeRange other)
        {
            if (fullLine || other.fullLine)
            {
                fullLine = true;
                minSlotIndex = 0;
                maxSlotIndex = int.MaxValue;
                return;
            }

            minSlotIndex = Mathf.Min(minSlotIndex, other.minSlotIndex);
            maxSlotIndex = Mathf.Max(maxSlotIndex, other.maxSlotIndex);
        }
    }

    private struct ConveyorLineRetryState
    {
        public ConveyorLineWakeRange wakeRange;
        public float retryTime;
        public int attemptCount;
        public bool readyDelay;

        public ConveyorLineRetryState(
            ConveyorLineWakeRange wakeRange,
            float retryTime,
            int attemptCount,
            bool readyDelay = false)
        {
            this.wakeRange = wakeRange;
            this.retryTime = retryTime;
            this.attemptCount = attemptCount;
            this.readyDelay = readyDelay;
        }
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
    [SerializeField]
    private List<ResourceEntry> reedResources = new List<ResourceEntry>();

    [SerializeField, Min(4)]
    private int chunkSize = 16;

    [SerializeField, Min(MinMapSize)]
    private int mapSize = 256;

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

    [SerializeField, Min(1)]
    private int conveyorActiveSafetyScanBudget = 32;

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
    private Shader generatedSurfaceFoamShader;

    [SerializeField, HideInInspector]
    private Shader generatedSurfaceGlintShader;

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

    [SerializeField, Min(0f)]
    private float waterSurfaceDepth = 0.18f;

    [Header("Water Foam")]
    [SerializeField]
    private bool generateWaterFoamOverlay = false;

    [SerializeField, Min(0f)]
    private float waterFoamWidth = 0.22f;

    [SerializeField, Min(0f)]
    private float waterFoamSurfaceOffset = 0.004f;

    [SerializeField]
    private Color waterFoamOverlayColor = new Color(0.72f, 0.9f, 1f, 0f);

    [Header("Water Highlights")]
    [SerializeField]
    private bool generateWaterSurfaceGlints = false;

    [SerializeField, Min(0f)]
    private float waterSurfaceGlintOffset = 0.012f;

    [SerializeField]
    private Color waterSurfaceGlintColor = new Color(0.86f, 0.96f, 1f, 0.30f);

    [SerializeField]
    private Vector2 waterSurfaceGlintDirection = new Vector2(1f, 0.18f);

    [SerializeField, Min(0.01f)]
    private float waterSurfaceGlintScale = 1.35f;

    [SerializeField, Range(0.005f, 0.5f)]
    private float waterSurfaceGlintLineWidth = 0.16f;

    [SerializeField, Range(0f, 1f)]
    private float waterSurfaceGlintBreakup = 0.33f;

    [SerializeField, Min(0f)]
    private float waterSurfaceGlintFlowSpeed = 0.28f;

    [SerializeField]
    private Vector2 startLakeRadiusRange = new Vector2(3f, 5f);

    [SerializeField, Min(0)]
    private int startSafeZoneRadius = 2;

    [SerializeField]
    private bool keepStartSafeZoneClearOfResources = true;

    [SerializeField, Min(0)]
    private int starterWaterExclusionRadius = 2;

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

    [SerializeField, Min(1)]
    private int reedWaterSearchRadius = 2;

    [SerializeField, Range(0f, 1f)]
    private float reedDensityMultiplier = 0.65f;

    private readonly Dictionary<Vector2Int, Transform> loadedChunks = new Dictionary<Vector2Int, Transform>();
    private readonly Dictionary<Vector2Int, Block> loadedBlocks = new Dictionary<Vector2Int, Block>();
    private readonly Dictionary<Vector2Int, Transform> sleepingChunkViews = new Dictionary<Vector2Int, Transform>();
    private readonly Dictionary<Vector2Int, InstallationObject> sleepingInstallationViews = new Dictionary<Vector2Int, InstallationObject>();
    private readonly List<Vector2Int> chunksToGenerateScratch = new List<Vector2Int>();
    private readonly ChunkDistanceComparer chunkDistanceComparer = new ChunkDistanceComparer();
    private readonly HashSet<Block> activeConveyors = new HashSet<Block>();
    private readonly List<Block> conveyorTickBuffer = new List<Block>();
    private readonly List<Block> activeConveyorDataMotionBlocks = new List<Block>();
    private readonly Dictionary<Block, int> activeConveyorDataMotionIndices = new Dictionary<Block, int>();
    private readonly Dictionary<Block, float> activeConveyorDataMotionDueTimes = new Dictionary<Block, float>();
    private readonly List<Block> sortedActiveConveyors = new List<Block>();
    private readonly HashSet<Block> activeConveyorDotVisuals = new HashSet<Block>();
    private readonly List<Block> activeConveyorDotVisualList = new List<Block>();
    private readonly List<Block> conveyorDotVisualTickBuffer = new List<Block>();
    private readonly HashSet<Block> activeBeltDirectionVisuals = new HashSet<Block>();
    private readonly List<Block> activeBeltDirectionVisualList = new List<Block>();
    private readonly List<Matrix4x4> directionArrowMatrixScratch = new List<Matrix4x4>(4);
    private readonly List<Block> pendingConveyorSlotDotRefreshBlocks = new List<Block>();
    private readonly Matrix4x4[] conveyorSlotDotInstanceMatrices = new Matrix4x4[MaxConveyorSlotDotInstancesPerBatch];
    private readonly Matrix4x4[] beltDirectionArrowInstanceMatrices = new Matrix4x4[MaxBeltDirectionArrowInstancesPerBatch];
    private int conveyorSlotDotInstanceMatrixCount;
    private int beltDirectionArrowInstanceMatrixCount;
    private Mesh conveyorSlotDotInstancedMesh;
    private Material conveyorSlotDotInstancedMaterial;
    private Material beltDirectionArrowInstancedMaterial;
    private int pendingConveyorSlotDotRefreshIndex;
    private bool conveyorSlotDotVisibilityInitialized;
    private bool lastShowConveyorSlotDots;
    private bool beltItemLineVisibilityInitialized;
    private bool lastShowBeltItemLine;
    private bool beltDirectionVisibilityInitialized;
    private bool lastShowBeltDirections;
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
    private readonly List<Block> dynamicConveyorItemVisualBlocks = new List<Block>(256);
    private readonly Dictionary<Block, int> dynamicConveyorItemVisualBlockIndices = new Dictionary<Block, int>();
    private readonly Dictionary<Block, int> conveyorItemCountsByBlock = new Dictionary<Block, int>();
    private int pendingBeltItemLineDebugRefreshIndex;
    private int conveyorItemVisualBlockSetVersion;
    private int dynamicConveyorItemVisualBlockSetVersion;
    private int cachedLoadedConveyorItemCount;
    private int lastConveyorItemLoadSavedBlocks;
    private int lastConveyorItemLoadSavedLanes;
    private int lastConveyorItemLoadLoadedBlocks;
    private int lastConveyorItemLoadPendingBlocks;
    private int lastConveyorItemLoadPendingLanes;
    private int lastConveyorItemLoadNotRuntimeBlocks;
    private int lastConveyorItemLoadZeroLaneBlocks;
    private int lastConveyorItemLoadAppliedLanes;
    private int lastConveyorItemLoadFallbackBlocks;
    private int lastConveyorItemLoadActualFailedBlocks;
    private int lastConveyorItemLoadActualFailedLanes;
    private readonly Dictionary<Block, int> conveyorNetworkIds = new Dictionary<Block, int>();
    private readonly Dictionary<int, List<Block>> conveyorNetworkBlocksById = new Dictionary<int, List<Block>>();
    private readonly Dictionary<int, float> conveyorNetworkRetryTimes = new Dictionary<int, float>();
    private readonly HashSet<int> conveyorNetworkSleepingIds = new HashSet<int>();
    private readonly HashSet<int> conveyorNetworkActiveIds = new HashSet<int>();
    private readonly HashSet<int> conveyorNetworkSleepCheckQueuedIds = new HashSet<int>();
    private readonly List<int> conveyorNetworkSleepCheckBuffer = new List<int>();
    private readonly Queue<Block> conveyorNetworkBuildQueue = new Queue<Block>();
    private readonly Queue<Block> conveyorWakeQueue = new Queue<Block>();
    private readonly Queue<int> conveyorLineWakeQueue = new Queue<int>();
    private readonly HashSet<Block> conveyorWakeQueued = new HashSet<Block>();
    private readonly HashSet<Block> conveyorDirectWakeBlocks = new HashSet<Block>();
    private readonly Dictionary<int, ConveyorLineWakeRange> conveyorLineWakeRangesById = new Dictionary<int, ConveyorLineWakeRange>();
    private readonly Queue<int> deferredConveyorLineWakeQueue = new Queue<int>();
    private readonly Dictionary<int, ConveyorLineWakeRange> deferredConveyorLineWakeRangesById = new Dictionary<int, ConveyorLineWakeRange>();
    private readonly Dictionary<int, ConveyorLineRetryState> conveyorLineRetryStatesById = new Dictionary<int, ConveyorLineRetryState>();
    private readonly Dictionary<int, int> conveyorLineRetryAttemptsByDueLineId = new Dictionary<int, int>();
    private readonly List<int> conveyorLineRetryDueIds = new List<int>();
    private readonly Dictionary<ConveyorLaneCoordinateKey, List<ConveyorLaneCoordinateKey>> conveyorBlockedSourcesByDestinationLane =
        new Dictionary<ConveyorLaneCoordinateKey, List<ConveyorLaneCoordinateKey>>();
    private readonly Dictionary<ConveyorLaneCoordinateKey, ConveyorLaneCoordinateKey> conveyorBlockedDestinationBySourceLane =
        new Dictionary<ConveyorLaneCoordinateKey, ConveyorLaneCoordinateKey>();
    private readonly List<ConveyorLaneCoordinateKey> conveyorBlockedWaiterWakeBuffer = new List<ConveyorLaneCoordinateKey>(4);
    private readonly List<ConveyorLine> conveyorLines = new List<ConveyorLine>();
    private readonly Dictionary<int, ConveyorLine> conveyorLinesById = new Dictionary<int, ConveyorLine>();
    private readonly Dictionary<Block, ConveyorLineSlot> conveyorLineSlots = new Dictionary<Block, ConveyorLineSlot>();
    private readonly HashSet<Block> conveyorLineVisited = new HashSet<Block>();
    private readonly Dictionary<Block, int> conveyorLineBuildIndices = new Dictionary<Block, int>();
    private readonly HashSet<int> conveyorLinesTickedThisFrame = new HashSet<int>();
    private readonly List<Block> conveyorLineTouchedBlocks = new List<Block>();
    private readonly HashSet<Block> conveyorLineTouchedSet = new HashSet<Block>();
    private readonly Queue<int> conveyorCornerGroupWakeQueue = new Queue<int>();
    private readonly HashSet<int> conveyorCornerGroupWakeQueued = new HashSet<int>();
    private readonly Dictionary<int, List<Block>> conveyorCornerGroupWakeBlocksById = new Dictionary<int, List<Block>>();
    private readonly HashSet<Block> conveyorCornerGroupWakeQueuedBlocks = new HashSet<Block>();
    private readonly List<ConveyorCornerGroup> conveyorCornerGroups = new List<ConveyorCornerGroup>();
    private readonly Dictionary<int, ConveyorCornerGroup> conveyorCornerGroupsById = new Dictionary<int, ConveyorCornerGroup>();
    private readonly Dictionary<Block, ConveyorCornerGroupSlot> conveyorCornerGroupSlots = new Dictionary<Block, ConveyorCornerGroupSlot>();
    private readonly HashSet<Block> conveyorCornerGroupVisited = new HashSet<Block>();
    private readonly Dictionary<Block, int> conveyorCornerGroupBuildIndices = new Dictionary<Block, int>();
    private readonly List<Block> conveyorCornerGroupTickBlocks = new List<Block>();
    private readonly HashSet<Block> deferredConveyorRuntimeRefreshBlocks = new HashSet<Block>();
    private readonly HashSet<Block> deferredConveyorNetworkWakeBlocks = new HashSet<Block>();
    private readonly HashSet<Block> deferredConveyorMoveAttemptWakeAroundBlocks = new HashSet<Block>();
    private readonly HashSet<Block> deferredConveyorMoveAttemptWakeFlowBlocks = new HashSet<Block>();
    private readonly HashSet<Vector2Int> virtualizedFloorObjectCoordinates = new HashSet<Vector2Int>();
    private readonly Queue<Vector2Int> floorObjectVirtualizationWorkQueue = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> floorObjectVirtualizationQueuedCoordinates = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> backgroundConveyorDirtyCoordinates = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> backgroundConveyorWakeCoordinates = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, TerrainBiome> tileBiomeCache = new Dictionary<Vector2Int, TerrainBiome>();
    private readonly Dictionary<Vector2Int, bool> rawWaterCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> directWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> bufferedWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<TerrainBiome, Material> biomeMaterialCache = new Dictionary<TerrainBiome, Material>();

    private bool hasGeneratedChunks;
    private bool hasSeedInitialized;
    private int terrainGenerationVersion;
    private bool isMaterializingVirtualFloorObjects;
    private bool activeConveyorOrderDirty = true;
    private bool conveyorNetworkCacheDirty = true;
    private bool conveyorLineCacheDirty = true;
    private int deferredConveyorRuntimeRefreshDepth;
    private int conveyorLineBlockLoopIterations;
    private int conveyorLineTouchedMinSlotIndex = int.MaxValue;
    private int conveyorLineTouchedMaxSlotIndex = -1;
    private int lastActiveConveyorTickFrame;
    private int lastActiveConveyorQueuedAtStart;
    private int lastActiveConveyorProcessLimit;
    private int lastActiveConveyorProcessed;
    private int lastActiveConveyorLineWakesProcessed;
    private int lastActiveConveyorBlockWakesProcessed;
    private int lastActiveConveyorCornerGroupWakesProcessed;
    private int lastActiveConveyorCornerGroupBlocksProcessed;
    private int lastActiveConveyorCornerGroupBlocksQueued;
    private int lastActiveConveyorCornerGroupBlocksSelected;
    private int lastActiveConveyorCornerGroupBlocksSkipped;
    private int lastActiveConveyorBlockWakeTicks;
    private int lastActiveConveyorBlockWakeLineFallbacks;
    private int lastActiveConveyorFullLineWakesProcessed;
    private int lastActiveConveyorRangedLineWakesProcessed;
    private int lastActiveConveyorDeferredLineWakesPromoted;
    private int lastActiveConveyorRetryStatesScanned;
    private int lastActiveConveyorRetryWakesQueued;
    private int lastActiveConveyorReadyDelayStates;
    private int lastActiveConveyorSafetyWakesQueued;
    private int lastActiveConveyorMovedLineWakesScheduled;
    private int lastActiveConveyorMovedLineWakeSlots;
    private int lastActiveConveyorBlockedWaiterRegistrations;
    private int lastActiveConveyorBlockedWaitersWoken;
    private float nextConveyorLineRetryTime = float.PositiveInfinity;
    private float nextConveyorActiveFullScanTime;
    private int activeConveyorSafetyScanIndex;
    private float nextBackgroundConveyorSimulationTime;
    private Vector2Int currentCenterChunk;
    private float nextFloorObjectVirtualizationTime;
    private Vector2Int floorObjectVirtualizationLiveCenterCoordinate;
    private int floorObjectVirtualizationLiveRadius = -1;
    private bool floorObjectVirtualizationLiveAreaInitialized;
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
    private Material generatedSurfaceFoamMaterial;
    private Material generatedSurfaceGlintMaterial;

    private void OnValidate()
    {
        NormalizeTerrainBoundsSettings();
        starterOreMaxResourceCount = Mathf.Max(starterOreMinResourceCount, starterOreMaxResourceCount);
        normalOreMaxResourceCount = Mathf.Max(normalOreMinResourceCount, normalOreMaxResourceCount);
        starterTreeMaxCount = Mathf.Max(starterTreeMinCount, starterTreeMaxCount);
        waterSurfaceDepth = Mathf.Max(0f, waterSurfaceDepth);
        waterFoamWidth = Mathf.Max(0f, waterFoamWidth);
        waterFoamSurfaceOffset = Mathf.Max(0f, waterFoamSurfaceOffset);
        waterSurfaceGlintOffset = Mathf.Max(0f, waterSurfaceGlintOffset);
        waterSurfaceGlintScale = Mathf.Max(0.01f, waterSurfaceGlintScale);
        waterSurfaceGlintFlowSpeed = Mathf.Max(0f, waterSurfaceGlintFlowSpeed);
        NormalizeResourceGenerationSettings();
        InvalidateTerrainGenerationCaches();
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

    private void NormalizeResourceGenerationSettings()
    {
        NormalizeOreBodyScaleSettings();
        oreScaleAtResourceCount = Mathf.Max(1, oreScaleAtResourceCount);
        NormalizeResourceEntries(
            oreResources,
            normalOreMinResourceCount,
            normalOreMaxResourceCount,
            starterOreMinResourceCount,
            starterOreMaxResourceCount);
        NormalizeResourceEntries(treeResources, 1, 1, 1, 1);
        NormalizeResourceEntries(reedResources, 1, 1, 1, 1);
        SyncResourceEntryDefinitions();
    }

    private void NormalizeTerrainBoundsSettings()
    {
        chunkSize = Mathf.Max(4, chunkSize);
        mapSize = Mathf.Max(MinMapSize, mapSize);
    }

    private void InvalidateTerrainGenerationCaches()
    {
        unchecked
        {
            terrainGenerationVersion++;
        }

        InvalidateStarterTreeCache();
        InvalidateTerrainBiomeDataCaches();
        InvalidateTerrainBiomeMaterialCaches();
    }

    private void Start()
    {
        NormalizeTerrainBoundsSettings();
        NormalizeResourceGenerationSettings();
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

        bool profileBeltTicks = MapObjectTickProfiler.IsEnabled;
        if (profileBeltTicks)
        {
            MapObjectTickProfiler.SetBeltTickCounts(
                activeConveyors.Count,
                activeConveyorDataMotionBlocks.Count,
                activeConveyorDotVisualList.Count);
        }
        else
        {
            MapObjectTickProfiler.SetBeltProfilingFrameEnabled(false);
        }

        if (ShouldTickConveyorDataMotions(Time.deltaTime))
        {
            using (TickConveyorDataMotionsMarker.Auto())
            {
                long startTimestamp = profileBeltTicks ? MapObjectTickProfiler.BeginSample() : 0L;
                TickActiveConveyorDataMotions(Time.deltaTime);
                if (profileBeltTicks)
                {
                    MapObjectTickProfiler.EndNamedSample(
                        "Belt",
                        "ConveyorDataMotion",
                        "Belt Data Motion",
                        startTimestamp);
                }
            }
        }

        if (ShouldTickActiveConveyorRuntime(Time.deltaTime))
        {
            using (TickConveyorsMarker.Auto())
            {
                long startTimestamp = profileBeltTicks ? MapObjectTickProfiler.BeginSample() : 0L;
                TickActiveConveyors(Time.deltaTime);
                if (profileBeltTicks)
                {
                    MapObjectTickProfiler.EndNamedSample(
                        "Belt",
                        "ActiveConveyor",
                        "Active Belt Tick",
                        startTimestamp);
                }
            }
        }

        if (ShouldTickBackgroundConveyors())
        {
            using (TickBackgroundConveyorsMarker.Auto())
            {
                long startTimestamp = profileBeltTicks ? MapObjectTickProfiler.BeginSample() : 0L;
                TickBackgroundConveyors();
                if (profileBeltTicks)
                {
                    MapObjectTickProfiler.EndNamedSample(
                        "Belt",
                        "BackgroundConveyor",
                        "Background Belt Tick",
                        startTimestamp);
                }
            }
        }

        if (ShouldTickConveyorVisualRuntime())
        {
            using (TickConveyorDotsMarker.Auto())
            {
                long startTimestamp = profileBeltTicks ? MapObjectTickProfiler.BeginSample() : 0L;
                SyncConveyorSlotDotRuntimeVisibility();
                TickPendingConveyorSlotDotRefreshes();
                SyncBeltItemLineRuntimeVisibility();
                TickPendingBeltItemLineDebugRefreshes();
                SyncBeltDirectionRuntimeVisibility();
                TickActiveConveyorDotVisuals(Time.deltaTime);
                DrawActiveBeltDirectionArrows();
                if (profileBeltTicks)
                {
                    MapObjectTickProfiler.EndNamedSample(
                        "Belt",
                        "ConveyorVisual",
                        "Belt Visual Tick",
                        startTimestamp);
                }
            }
        }

        if (ShouldRefreshTrackedChunks())
        {
            using (RefreshChunksMarker.Auto())
            {
                RefreshTrackedChunks();
            }
        }

        if (ShouldTickFloorObjectVirtualization())
        {
            using (FloorObjectVirtualizationMarker.Auto())
            {
                TickFloorObjectVirtualization();
            }
        }
    }

    private bool ShouldTickConveyorDataMotions(float deltaTime)
    {
        return deltaTime > 0f && activeConveyorDataMotionBlocks.Count > 0;
    }

    private bool ShouldTickActiveConveyorRuntime(float deltaTime)
    {
        return deltaTime > 0f
               && (activeConveyors.Count > 0
                   || conveyorWakeQueue.Count > 0
                   || conveyorLineWakeQueue.Count > 0
                   || conveyorCornerGroupWakeQueue.Count > 0
                   || deferredConveyorLineWakeQueue.Count > 0
                   || HasDueStraightConveyorLineRetry()
                   || conveyorNetworkSleepCheckQueuedIds.Count > 0);
    }

    private bool ShouldTickBackgroundConveyors()
    {
        return Time.time >= nextBackgroundConveyorSimulationTime;
    }

    private bool ShouldTickConveyorVisualRuntime()
    {
        return !conveyorSlotDotVisibilityInitialized
               || !beltItemLineVisibilityInitialized
               || !beltDirectionVisibilityInitialized
               || pendingConveyorSlotDotRefreshBlocks.Count > 0
               || pendingBeltItemLineDebugRefreshAll
               || pendingBeltItemLineDebugRefreshBlocks.Count > 0
               || activeConveyorDotVisualList.Count > 0
               || activeBeltDirectionVisualList.Count > 0
               || conveyorSlotDotInstanceMatrixCount > 0
               || beltDirectionArrowInstanceMatrixCount > 0
               || beltItemLineVisualsDirty
               || applyingBeltItemLineRuntimeVisibility;
    }

    private bool ShouldRefreshTrackedChunks()
    {
        return GetCenterChunkCoordinate() != currentCenterChunk;
    }

    private bool ShouldTickFloorObjectVirtualization()
    {
        return virtualizeDistantFloorObjects
               && loadedBlocks.Count > 0
               && Time.time >= nextFloorObjectVirtualizationTime;
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
        if (resourceStateStore.LastBackgroundConveyorBudgetHit > 0)
        {
            nextBackgroundConveyorSimulationTime = Time.time + BackgroundConveyorBudgetContinuationInterval;
        }
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

            block.WakeConveyorMoveAttemptsAlongRuntimeFlow();
            block.RefreshConveyorActivityRegistration();
        }

        backgroundConveyorWakeCoordinates.Clear();
    }

    public void WakeLoadedConveyorsNearBackgroundConveyorChange(Vector2Int coordinate)
    {
        backgroundConveyorDirtyCoordinates.Clear();
        backgroundConveyorDirtyCoordinates.Add(coordinate);
        WakeLoadedConveyorsNearBackgroundConveyorChanges();
    }

    private void OnDisable()
    {
        if (Active == this)
        {
            Active = null;
        }

        ClearConveyorRuntimeState();
        virtualizedFloorObjectCoordinates.Clear();
        ClearFloorObjectVirtualizationScan();
        ClearPendingChunkGenerations();
    }

    public bool VirtualizeConveyorItems => virtualizeConveyorItems;
    public bool VirtualizeConveyorBelts => virtualizeConveyorBelts;
    public int ConveyorItemVisualBlockSetVersion => conveyorItemVisualBlockSetVersion;
    public int DynamicConveyorItemVisualBlockSetVersion => dynamicConveyorItemVisualBlockSetVersion;
    public int ConveyorItemVisualDirtyBlockCount => conveyorItemVisualDirtyBlocks.Count;

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
        if (Application.isPlaying)
        {
            return cachedLoadedConveyorItemCount;
        }

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

    public int GetConveyorItemCount()
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return GetLoadedConveyorItemCount();
        }

        int count = resourceStateStore.GetSavedConveyorItemCount();
        List<KeyValuePair<Vector2Int, Block>> loadedBlockSnapshot =
            new List<KeyValuePair<Vector2Int, Block>>(loadedBlocks);
        for (int i = 0; i < loadedBlockSnapshot.Count; i++)
        {
            KeyValuePair<Vector2Int, Block> pair = loadedBlockSnapshot[i];
            Block block = pair.Value;
            if (block == null)
            {
                continue;
            }

            int savedCount = resourceStateStore.GetSavedConveyorItemCount(pair.Key);
            int liveCount = block.IsRuntimeConveyor ? block.GetRuntimeConveyorItemCount() : 0;
            count += liveCount - savedCount;
        }

        return Mathf.Max(0, count);
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
            seed = seed,
            mapSize = GetNormalizedMapSize()
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
        SaveGameConveyorItemBackfill.BackfillFromFloorObjects(mapSaveData);
        return mapSaveData;
    }

    public void LoadFromSaveState(TerrainSaveData terrainSaveData, MapSaveData mapSaveData)
    {
        NormalizeTerrainBoundsSettings();
        NormalizeResourceGenerationSettings();
        EnsureResourceStateStore();
        EnsurePortableItemRenderer();
        EnsureVirtualConveyorBeltRenderer();

        if (terrainSaveData != null)
        {
            seed = terrainSaveData.seed;
            if (terrainSaveData.mapSize > 0)
            {
                mapSize = terrainSaveData.mapSize;
            }

            NormalizeTerrainBoundsSettings();
            loadRadius = Mathf.Max(0, loadRadius);
            unloadRadius = Mathf.Max(loadRadius + 1, unloadRadius);
            hasSeedInitialized = true;
        }

        InvalidateTerrainGenerationCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks(false, true);
        SaveGameConveyorItemBackfill.BackfillFromFloorObjects(mapSaveData);
        resourceStateStore?.ApplySaveState(mapSaveData);

        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        RefreshChunks(currentCenterChunk, true);
        ProcessQueuedChunkGenerationsImmediate();
        RefreshLoadedConveyorBeltRuntimeViews();
        ApplyLoadedConveyorItemSaveStates(mapSaveData);
        RefreshLoadedRuntimeRegistrations();
        RefreshLoadedRuntimeVisibility();
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
        List<KeyValuePair<Vector2Int, Block>> loadedBlockSnapshot =
            new List<KeyValuePair<Vector2Int, Block>>(loadedBlocks);
        for (int i = 0; i < loadedBlockSnapshot.Count; i++)
        {
            KeyValuePair<Vector2Int, Block> pair = loadedBlockSnapshot[i];
            Block block = pair.Value;
            if (block == null)
            {
                continue;
            }

            SaveLoadedBlockFloorObjects(block, VirtualObjectResidency.Live);

            if (block.MapObject is InstallationObject installationObject
                && !installationObject.ExcludeFromTerrainPersistence
                && savedInstallations.Add(installationObject))
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

        SaveActiveRuntimeInstallations(savedInstallations, null);
    }

    private void SaveActiveRuntimeInstallations(
        HashSet<InstallationObject> savedInstallations,
        ISet<Vector2Int> coordinateFilter)
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        InstallationObject[] activeInstallations = FindObjectsOfType<InstallationObject>(false);
        for (int i = 0; i < activeInstallations.Length; i++)
        {
            InstallationObject installationObject = activeInstallations[i];
            if (installationObject == null
                || (savedInstallations != null && savedInstallations.Contains(installationObject))
                || installationObject.ExcludeFromTerrainPersistence
                || !installationObject.TryGetPlacementRuntime(out _, out _)
                || !InstallationIntersectsCoordinateFilter(installationObject, coordinateFilter))
            {
                continue;
            }

            savedInstallations?.Add(installationObject);
            resourceStateStore.SaveInstallation(installationObject);
            resourceStateStore.RegisterLiveInstallation(installationObject);
        }
    }

    private static bool InstallationIntersectsCoordinateFilter(
        InstallationObject installationObject,
        ISet<Vector2Int> coordinateFilter)
    {
        if (coordinateFilter == null || coordinateFilter.Count <= 0)
        {
            return true;
        }

        if (installationObject == null)
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates != null)
        {
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                if (coordinateFilter.Contains(occupiedCoordinates[i]))
                {
                    return true;
                }
            }
        }

        return installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
               && coordinateFilter.Contains(anchorCoordinate);
    }

    private void CaptureLoadedConveyorItemSaveStates(MapSaveData mapSaveData)
    {
        if (mapSaveData == null)
        {
            return;
        }

        mapSaveData.conveyorItems ??= new List<ConveyorItemBlockSaveEntry>();

        List<KeyValuePair<Vector2Int, Block>> loadedBlockSnapshot =
            new List<KeyValuePair<Vector2Int, Block>>(loadedBlocks);
        for (int i = 0; i < loadedBlockSnapshot.Count; i++)
        {
            KeyValuePair<Vector2Int, Block> pair = loadedBlockSnapshot[i];
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
            int existingEntryIndex = SaveGameConveyorItemBackfill.FindConveyorItemEntryIndex(
                mapSaveData.conveyorItems,
                pair.Key);
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

    private void ApplyLoadedConveyorItemSaveStates(MapSaveData mapSaveData)
    {
        ResetLastConveyorItemLoadStats();
        if (mapSaveData?.conveyorItems == null)
        {
            return;
        }

        for (int i = 0; i < mapSaveData.conveyorItems.Count; i++)
        {
            ConveyorItemBlockSaveEntry entry = mapSaveData.conveyorItems[i];
            if (entry == null
                || entry.lanes == null
                || entry.lanes.Count <= 0)
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

            int savedLaneCount = CountConveyorItemSaveLanes(lanes);
            if (savedLaneCount <= 0)
            {
                continue;
            }

            lastConveyorItemLoadSavedBlocks++;
            lastConveyorItemLoadSavedLanes += savedLaneCount;

            loadedBlocks.TryGetValue(entry.coordinate, out Block block);
            ApplyLoadedConveyorItemSaveStatesToBlock(entry.coordinate, block, lanes, mapSaveData, true);
        }
    }

    private void ApplyStoredConveyorItemSaveStates(Block[] blocks)
    {
        if (blocks == null || blocks.Length <= 0)
        {
            return;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null
                || !resourceStateStore.TryGetConveyorItems(
                    block.Coordinate,
                    out List<ConveyorItemLaneSaveState> lanes)
                || CountConveyorItemSaveLanes(lanes) <= 0)
            {
                continue;
            }

            ApplyLoadedConveyorItemSaveStatesToBlock(block.Coordinate, block, lanes, null, false);
        }
    }

    private int ApplyLoadedConveyorItemSaveStatesToBlock(
        Vector2Int coordinate,
        Block block,
        IReadOnlyList<ConveyorItemLaneSaveState> lanes,
        MapSaveData mapSaveData,
        bool updateLoadStats)
    {
        int laneCount = updateLoadStats ? CountConveyorItemSaveLanes(lanes) : 0;
        if (block == null)
        {
            if (updateLoadStats)
            {
                lastConveyorItemLoadPendingBlocks++;
                lastConveyorItemLoadPendingLanes += laneCount;
            }

            return 0;
        }

        if (updateLoadStats)
        {
            lastConveyorItemLoadLoadedBlocks++;
        }

        if (!block.IsRuntimeConveyor)
        {
            if (updateLoadStats)
            {
                lastConveyorItemLoadNotRuntimeBlocks++;
                lastConveyorItemLoadActualFailedBlocks++;
                lastConveyorItemLoadActualFailedLanes += laneCount;
            }

            return 0;
        }

        if (block.GetRuntimeConveyorLaneCount() <= 0)
        {
            if (updateLoadStats)
            {
                lastConveyorItemLoadZeroLaneBlocks++;
                lastConveyorItemLoadActualFailedBlocks++;
                lastConveyorItemLoadActualFailedLanes += laneCount;
            }

            return 0;
        }

        int restoredItemCount = block.ApplyConveyorItemSaveStates(lanes);
        if (restoredItemCount > 0)
        {
            if (updateLoadStats)
            {
                lastConveyorItemLoadAppliedLanes += restoredItemCount;
            }

            MarkLoadedConveyorItemBlockLive(coordinate);
            return restoredItemCount;
        }

        if (TryGetLoadedConveyorItemFloorObjectFallback(coordinate, mapSaveData, out List<int> fallbackItemIds))
        {
            block.ApplyFloorObjectState(fallbackItemIds);
            int fallbackItemCount = block.GetRuntimeConveyorItemCount();
            if (fallbackItemCount > 0)
            {
                if (updateLoadStats)
                {
                    lastConveyorItemLoadAppliedLanes += fallbackItemCount;
                    lastConveyorItemLoadFallbackBlocks++;
                }

                MarkLoadedConveyorItemBlockLive(coordinate);
                return fallbackItemCount;
            }
        }

        if (updateLoadStats)
        {
            lastConveyorItemLoadActualFailedBlocks++;
            lastConveyorItemLoadActualFailedLanes += laneCount;
        }

        return 0;
    }

    private void ResetLastConveyorItemLoadStats()
    {
        lastConveyorItemLoadSavedBlocks = 0;
        lastConveyorItemLoadSavedLanes = 0;
        lastConveyorItemLoadLoadedBlocks = 0;
        lastConveyorItemLoadPendingBlocks = 0;
        lastConveyorItemLoadPendingLanes = 0;
        lastConveyorItemLoadNotRuntimeBlocks = 0;
        lastConveyorItemLoadZeroLaneBlocks = 0;
        lastConveyorItemLoadAppliedLanes = 0;
        lastConveyorItemLoadFallbackBlocks = 0;
        lastConveyorItemLoadActualFailedBlocks = 0;
        lastConveyorItemLoadActualFailedLanes = 0;
    }

    private static int CountConveyorItemSaveLanes(IReadOnlyList<ConveyorItemLaneSaveState> lanes)
    {
        if (lanes == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = lanes[i];
            if (lane != null && lane.itemId >= 0 && lane.laneIndex >= 0)
            {
                count++;
            }
        }

        return count;
    }

    private void MarkLoadedConveyorItemBlockLive(Vector2Int coordinate)
    {
        resourceStateStore?.SetFloorObjectsResidency(coordinate, VirtualObjectResidency.Live);
        virtualizedFloorObjectCoordinates.Remove(coordinate);
        RobotArm.WakeAroundCoordinate(coordinate);
    }

    private bool TryGetLoadedConveyorItemFloorObjectFallback(
        Vector2Int coordinate,
        MapSaveData mapSaveData,
        out List<int> itemIds)
    {
        EnsureResourceStateStore();
        if (resourceStateStore != null
            && resourceStateStore.TryGetFloorObjects(coordinate, out itemIds)
            && HasConveyorFloorObjectFallback(itemIds))
        {
            return true;
        }

        if (mapSaveData?.floorObjects != null)
        {
            for (int i = 0; i < mapSaveData.floorObjects.Count; i++)
            {
                FloorObjectSaveEntry entry = mapSaveData.floorObjects[i];
                if (entry != null
                    && entry.coordinate == coordinate
                    && HasConveyorFloorObjectFallback(entry.itemIds))
                {
                    itemIds = entry.itemIds;
                    return true;
                }
            }
        }

        itemIds = null;
        return false;
    }

    private void RefreshLoadedRuntimeRegistrations()
    {
        MarkConveyorNetworkDirty();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            RefreshRestoredBlockRuntimeRegistration(pair.Value);
        }
    }

    private void RefreshLoadedConveyorBeltRuntimeViews()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        List<ConveyorBelt> conveyorBelts = new List<ConveyorBelt>();
        HashSet<ConveyorBelt> uniqueConveyorBelts = new HashSet<ConveyorBelt>();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            if (pair.Value?.MapObject is ConveyorBelt conveyorBelt
                && uniqueConveyorBelts.Add(conveyorBelt))
            {
                conveyorBelts.Add(conveyorBelt);
            }
        }

        if (conveyorBelts.Count <= 0)
        {
            return;
        }

        virtualConveyorBeltRenderer?.Clear();
        for (int i = 0; i < conveyorBelts.Count; i++)
        {
            conveyorBelts[i]?.RefreshEndpointVisuals();
        }

        ConvayorBelt2F.MarkCoverageDirty();
        for (int i = 0; i < conveyorBelts.Count; i++)
        {
            if (conveyorBelts[i] is ConvayorBelt2F belt2F)
            {
                belt2F.RefreshCoveredConveyorTopology();
            }
        }

        for (int i = 0; i < conveyorBelts.Count; i++)
        {
            RegisterVirtualConveyorBelt(conveyorBelts[i]);
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

    private void RefreshLoadedRuntimeVisibility()
    {
        RefreshBeltItemRenderingVisibility();
        RefreshBeltRenderingVisibility();
        RefreshConveyorSlotDotRuntimeVisibility();
        RefreshBeltItemLineRuntimeVisibility();
        RefreshBeltDirectionRuntimeVisibility();
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

    public void CopyDynamicConveyorItemVisualBlocks(List<Block> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        for (int i = 0; i < dynamicConveyorItemVisualBlocks.Count; i++)
        {
            Block block = dynamicConveyorItemVisualBlocks[i];
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
        NormalizeTerrainBoundsSettings();
        NormalizeResourceGenerationSettings();
        EnsureResourceStateStore();
        InitializeSeedForGeneration();
        InvalidateTerrainGenerationCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks(false, true);
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

        NormalizeTerrainBoundsSettings();
        NormalizeResourceGenerationSettings();
        EnsureResourceStateStore();
        InvalidateTerrainGenerationCaches();
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
        InvalidateTerrainGenerationCaches();
    }

    public bool IsCoordinateWithinMapBounds(Vector2Int worldCoordinate)
    {
        return IsCoordinateInsideMapBounds(worldCoordinate);
    }

    private int GetNormalizedMapSize()
    {
        return Mathf.Max(MinMapSize, mapSize);
    }

    private int GetMapMinCoordinate()
    {
        return -(GetNormalizedMapSize() / 2);
    }

    private int GetMapMaxExclusiveCoordinate()
    {
        return GetMapMinCoordinate() + GetNormalizedMapSize();
    }

    private bool IsCoordinateInsideMapBounds(Vector2Int worldCoordinate)
    {
        int min = GetMapMinCoordinate();
        int maxExclusive = GetMapMaxExclusiveCoordinate();
        return worldCoordinate.x >= min
               && worldCoordinate.y >= min
               && worldCoordinate.x < maxExclusive
               && worldCoordinate.y < maxExclusive;
    }

    private bool DoesChunkIntersectMapBounds(Vector2Int chunkCoordinate, int normalizedChunkSize)
    {
        int chunkSizeInBlocks = Mathf.Max(4, normalizedChunkSize);
        int chunkMinX = chunkCoordinate.x * chunkSizeInBlocks;
        int chunkMinY = chunkCoordinate.y * chunkSizeInBlocks;
        int chunkMaxExclusiveX = chunkMinX + chunkSizeInBlocks;
        int chunkMaxExclusiveY = chunkMinY + chunkSizeInBlocks;
        int mapMin = GetMapMinCoordinate();
        int mapMaxExclusive = GetMapMaxExclusiveCoordinate();

        return chunkMaxExclusiveX > mapMin
               && chunkMaxExclusiveY > mapMin
               && chunkMinX < mapMaxExclusive
               && chunkMinY < mapMaxExclusive;
    }

    private Vector2Int ClampChunkCoordinateToMapBounds(Vector2Int chunkCoordinate, int normalizedChunkSize)
    {
        GetMapChunkRange(normalizedChunkSize, out Vector2Int minChunk, out Vector2Int maxChunk);
        return new Vector2Int(
            Mathf.Clamp(chunkCoordinate.x, minChunk.x, maxChunk.x),
            Mathf.Clamp(chunkCoordinate.y, minChunk.y, maxChunk.y));
    }

    private void GetMapChunkRange(int normalizedChunkSize, out Vector2Int minChunk, out Vector2Int maxChunk)
    {
        int chunkSizeInBlocks = Mathf.Max(4, normalizedChunkSize);
        int mapMin = GetMapMinCoordinate();
        int mapMaxInclusive = GetMapMaxExclusiveCoordinate() - 1;
        minChunk = new Vector2Int(
            Mathf.FloorToInt(mapMin / (float)chunkSizeInBlocks),
            Mathf.FloorToInt(mapMin / (float)chunkSizeInBlocks));
        maxChunk = new Vector2Int(
            Mathf.FloorToInt(mapMaxInclusive / (float)chunkSizeInBlocks),
            Mathf.FloorToInt(mapMaxInclusive / (float)chunkSizeInBlocks));
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
        List<Vector2Int> chunksToGenerate = chunksToGenerateScratch;
        chunksToGenerate.Clear();

        using (RefreshChunkLoadScanMarker.Auto())
        {
            for (int chunkY = centerChunk.y - normalizedLoadRadius; chunkY <= centerChunk.y + normalizedLoadRadius; chunkY++)
            {
                for (int chunkX = centerChunk.x - normalizedLoadRadius; chunkX <= centerChunk.x + normalizedLoadRadius; chunkX++)
                {
                    Vector2Int chunkCoordinate = new Vector2Int(chunkX, chunkY);
                    if (!DoesChunkIntersectMapBounds(chunkCoordinate, normalizedChunkSize))
                    {
                        continue;
                    }

                    if (forceReload || (!loadedChunks.ContainsKey(chunkCoordinate) && !IsChunkGenerationActive(chunkCoordinate)))
                    {
                        chunksToGenerate.Add(chunkCoordinate);
                    }
                }
            }
        }

        using (RefreshChunkLoadSortMarker.Auto())
        {
            if (chunksToGenerate.Count > 1)
            {
                chunkDistanceComparer.CenterChunk = centerChunk;
                chunksToGenerate.Sort(chunkDistanceComparer);
            }
        }

        using (RefreshChunkGenerationQueueMarker.Auto())
        {
            for (int i = 0; i < chunksToGenerate.Count; i++)
            {
                QueueChunkGeneration(chunksToGenerate[i], normalizedChunkSize);
            }

            chunksToGenerate.Clear();
            EnsureChunkGenerationProcessing();
        }

        using (RefreshChunkUnloadScanMarker.Auto())
        {
            foreach (KeyValuePair<Vector2Int, Transform> loadedChunk in loadedChunks)
            {
                int distanceX = Mathf.Abs(loadedChunk.Key.x - centerChunk.x);
                int distanceY = Mathf.Abs(loadedChunk.Key.y - centerChunk.y);

                if (!DoesChunkIntersectMapBounds(loadedChunk.Key, normalizedChunkSize)
                    || distanceX > normalizedUnloadRadius
                    || distanceY > normalizedUnloadRadius)
                {
                    QueueChunkUnload(loadedChunk.Key);
                }
            }

            EnsureChunkUnloadProcessing();
        }
    }

    private sealed class ChunkDistanceComparer : IComparer<Vector2Int>
    {
        public Vector2Int CenterChunk { get; set; }

        public int Compare(Vector2Int left, Vector2Int right)
        {
            int leftDistance = GetChunkDistanceSqr(left, CenterChunk);
            int rightDistance = GetChunkDistanceSqr(right, CenterChunk);
            return leftDistance.CompareTo(rightDistance);
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
        if (!DoesChunkIntersectMapBounds(chunkCoordinate, normalizedChunkSize))
        {
            yield break;
        }

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
                if (!IsCoordinateInsideMapBounds(worldCoordinate))
                {
                    continue;
                }

                Vector3 localPosition = new Vector3(localX, 0f, localY);
                BlockBiomeVisualData visualData = BuildBlockBiomeVisualData(worldCoordinate);
                Block block = CreateBlock(chunkObject.transform, groundSet, Block.BlockType.Ground, worldCoordinate, localPosition, false, 0f);
                if (block == null)
                {
                    continue;
                }

                ApplyBlockBiomeVisuals(block, visualData);
                if (TryGetResourcePrefab(worldCoordinate, out Resource resourcePrefab)
                    && CanSpawnResourceAtGeneratedCoordinate(worldCoordinate, resourcePrefab))
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

        Block[] chunkBlocks = GetDirectChunkBlocks(chunkObject.transform);
        RefreshChunkBlockRuntimeViews(chunkBlocks);
        ApplyStoredConveyorItemSaveStates(chunkBlocks);

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
            chunkSurface = new ChunkSurfaceBuildData(GeneratedSurfaceMaterialCount)
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

    private void ClearLoadedChunks(bool preserveRuntimeState = true, bool releaseLiveInstallations = false)
    {
        if (releaseLiveInstallations)
        {
            ReleaseLiveInstallationsForReload();
        }

        List<Transform> chunkObjects = new List<Transform>(loadedChunks.Values);

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != null
                && !chunkObjects.Contains(child)
                && HasDirectChunkBlocks(child))
            {
                chunkObjects.Add(child);
            }
        }

        for (int i = 0; i < chunkObjects.Count; i++)
        {
            if (chunkObjects[i] != null)
            {
                if (preserveRuntimeState)
                {
                    SaveChunkResourceStates(chunkObjects[i]);
                }

                RemoveChunkBlocksFromLookup(chunkObjects[i]);
                ReleaseChunkBlocksToPool(chunkObjects[i]);
                DestroyChunkObject(chunkObjects[i].gameObject);
            }
        }

        DestroySleepingViewCaches();

        loadedChunks.Clear();
        loadedBlocks.Clear();
        ClearConveyorRuntimeState();
        virtualizedFloorObjectCoordinates.Clear();
        ClearFloorObjectVirtualizationScan();
        if (!releaseLiveInstallations && preserveRuntimeState)
        {
            CleanupOrphanedLiveInstallations();
        }
    }

    private static bool HasDirectChunkBlocks(Transform chunkTransform)
    {
        if (chunkTransform == null)
        {
            return false;
        }

        for (int i = 0; i < chunkTransform.childCount; i++)
        {
            Transform child = chunkTransform.GetChild(i);
            if (child != null && child.TryGetComponent(out Block block) && block != null)
            {
                return true;
            }
        }

        return false;
    }

    private void ReleaseLiveInstallationsForReload()
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        IReadOnlyList<Vector2Int> liveAnchors = resourceStateStore.GetLiveInstallationStorageKeys();
        if (liveAnchors == null || liveAnchors.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < liveAnchors.Count; i++)
        {
            Vector2Int storageKey = liveAnchors[i];
            if (!resourceStateStore.TryDetachLiveInstallation(
                    storageKey,
                    out InstallationObject installationObject,
                    out BlockStateStore.InstallationSaveState state))
            {
                continue;
            }

            ReleaseInstallationObject(installationObject, ResolveInstallationSourcePrefab(state));
        }
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
        EnqueueFloorObjectVirtualizationCoordinate(coordinate);
        return block;
    }


}
