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
    private const float GeneratedSurfaceBaseInset = 0.0035f;
    private const float GeneratedSurfaceBiomeLayerStep = 0.004f;
    public const float GeneratedOilSurfaceLocalY = -0.15f;
    private const float GeneratedOilPitDepth = 0.20f;
    public const float GeneratedOilPitInnerRadius = 0.285f;
    public const float GeneratedOilPitOuterRadius = 0.3675f;
    private const float GeneratedOilPitOutlineJitter = 0.005f;
    // Oil pits need one extra subdivision over the common runtime setting, but
    // raising the entire 8x8 chunk to 8 subdivisions multiplies contour and
    // mesh-upload work by more than seven times. Four samples per tile retain
    // the pit silhouette without creating periodic streaming spikes.
    private const int GeneratedOilChunkSurfaceSubdivisions = 4;
    private const float GeneratedWaterWallVerticalOverlap = 0.018f;
    private const int GeneratedWaterDepthSearchRadius = 4;
    private const float GeneratedWaterDepthDeepDistance = 2.65f;
    private const int GeneratedWaterFoamRenderQueue = 3010;
    private const int DynamicRangeCullingBudgetPerFrame = 128;

    private static readonly ProfilerMarker TickConveyorDataMotionsMarker = new ProfilerMarker("TerrainGenerator.TickConveyorDataMotions");
    private static readonly ProfilerMarker TickConveyorsMarker = new ProfilerMarker("TerrainGenerator.TickConveyors");
    private static readonly ProfilerMarker TickConveyorDotsMarker = new ProfilerMarker("TerrainGenerator.TickConveyorDots");
    private static readonly ProfilerMarker RefreshChunksMarker = new ProfilerMarker("TerrainGenerator.RefreshTrackedChunks");
    private static readonly ProfilerMarker RefreshChunkLoadScanMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkLoadScan");
    private static readonly ProfilerMarker RefreshChunkLoadSortMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkLoadSort");
    private static readonly ProfilerMarker RefreshChunkGenerationQueueMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkGenerationQueue");
    private static readonly ProfilerMarker SaveChunkStatesMarker = new ProfilerMarker("TerrainGenerator.SaveChunkStates");
    private static readonly ProfilerMarker RemoveChunkLookupMarker = new ProfilerMarker("TerrainGenerator.RemoveChunkLookup");
    private static readonly ProfilerMarker ReleaseChunkBlocksMarker = new ProfilerMarker("TerrainGenerator.ReleaseChunkBlocks");
    private static readonly ProfilerMarker CleanupInstallationsMarker = new ProfilerMarker("TerrainGenerator.CleanupInstallations");
    private static readonly ProfilerMarker GenerateChunkCoroutineStepMarker = new ProfilerMarker("TerrainGenerator.GenerateChunkCoroutineStep");
    private static readonly ProfilerMarker GenerateChunkRuntimeProxyMarker = new ProfilerMarker("TerrainGenerator.GenerateChunkRuntimeProxy");
    private static readonly ProfilerMarker RestoreChunkBlockStateMarker = new ProfilerMarker("TerrainGenerator.RestoreChunkBlockState");
    private static readonly ProfilerMarker SpawnChunkAnimalStepMarker = new ProfilerMarker("TerrainGenerator.SpawnChunkAnimalStep");
    private static readonly ProfilerMarker FinalizeChunkRuntimeViewMarker = new ProfilerMarker("TerrainGenerator.FinalizeChunkRuntimeView");
    private static readonly ProfilerMarker RestoreChunkConveyorItemsMarker = new ProfilerMarker("TerrainGenerator.RestoreChunkConveyorItems");
    private static readonly ProfilerMarker ReleaseEmptyChunkRuntimeProxyMarker = new ProfilerMarker("TerrainGenerator.ReleaseEmptyChunkRuntimeProxy");
    private static readonly ProfilerMarker RefreshChunkRangeCullingMarker = new ProfilerMarker("TerrainGenerator.RefreshChunkRangeCulling");
    private static readonly ProfilerMarker RestoreSavedInstallationMarker = new ProfilerMarker("TerrainGenerator.RestoreSavedInstallation");
    private static readonly ProfilerMarker InstantiateSavedInstallationMarker = new ProfilerMarker("TerrainGenerator.InstantiateSavedInstallation");
    private static readonly ProfilerMarker BindLoadedInstallationBlocksMarker = new ProfilerMarker("TerrainGenerator.BindLoadedInstallationBlocks");
    private static readonly ProfilerMarker ApplyChunkSurfaceMarker = new ProfilerMarker("TerrainGenerator.ApplyChunkBiomeSurface");
    private static readonly ProfilerMarker RenderChunkSurfacesMarker = new ProfilerMarker("TerrainGenerator.RenderChunkSurfaces");

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
    private const int GeneratedSurfaceWaterRenderSubMeshIndex = 0;
    private const int GeneratedSurfaceBlendRenderSubMeshIndex = 1;
    private const int GeneratedSurfaceRockRenderSubMeshIndex = 2;
    private const int GeneratedSurfaceBaseRenderSubMeshCount = 3;
    private const int GeneratedSurfaceFoamRenderSubMeshIndex = GeneratedSurfaceBaseRenderSubMeshCount;
    private const int GeneratedSurfaceRenderSubMeshCount = GeneratedSurfaceFoamRenderSubMeshIndex + 1;

    private sealed class ChunkSurfaceBuildData
    {
        public Vector2Int origin;
        public ChunkSurfaceWorkerInput surfaceInput;
        public readonly List<Vector3> vertices;
        public readonly List<Vector3> normals;
        public readonly List<Vector2> uvs;
        public readonly List<Color> colors;
        public readonly float[] blendWeightBuffer = new float[GeneratedSurfaceBiomeMaterialCount];
        public readonly List<Vector2> contourPolygonScratch = new List<Vector2>(8);
        public readonly List<int>[] trianglesByBiome;
        private float[] contourScores = Array.Empty<float>();

        public ChunkSurfaceBuildData(int biomeCount, int surfaceCellWidth)
        {
            int normalizedBiomeCount = Mathf.Max(1, biomeCount);
            long surfaceCellCountLong = (long)Mathf.Max(1, surfaceCellWidth) * Mathf.Max(1, surfaceCellWidth);
            int estimatedSurfaceCellCount = (int)Math.Min(surfaceCellCountLong, int.MaxValue / 8L);
            int estimatedVertexCapacity = Math.Max(64, estimatedSurfaceCellCount * 8);
            int estimatedTriangleCapacity = Math.Max(96, estimatedSurfaceCellCount * 3);

            vertices = new List<Vector3>(estimatedVertexCapacity);
            normals = new List<Vector3>(estimatedVertexCapacity);
            uvs = new List<Vector2>(estimatedVertexCapacity);
            colors = new List<Color>(estimatedVertexCapacity);
            trianglesByBiome = new List<int>[normalizedBiomeCount];
            for (int i = 0; i < trianglesByBiome.Length; i++)
            {
                trianglesByBiome[i] = new List<int>(estimatedTriangleCapacity);
            }
        }

        public void Reset(Vector2Int nextOrigin, ChunkSurfaceWorkerInput nextSurfaceInput)
        {
            origin = nextOrigin;
            surfaceInput = nextSurfaceInput;
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            colors.Clear();
            for (int i = 0; i < trianglesByBiome.Length; i++)
            {
                trianglesByBiome[i].Clear();
            }
        }

        public float[] GetContourScores(int rowLength)
        {
            int requiredLength = Mathf.Max(1, rowLength * rowLength);
            if (contourScores.Length < requiredLength)
            {
                contourScores = new float[requiredLength];
            }

            return contourScores;
        }
    }

    private sealed class ChunkRuntimeData
    {
        public readonly Vector2Int coordinate;
        public readonly Vector2Int origin;
        public Mesh surfaceMesh;
        public Matrix4x4 surfaceMatrix;
        public Bounds surfaceWorldBounds;
        public int surfaceSubMeshMask;

        public ChunkRuntimeData(Vector2Int coordinate, Vector2Int origin)
        {
            this.coordinate = coordinate;
            this.origin = origin;
        }
    }

    private sealed class ConveyorLine
    {
        public int id;
        public bool isCycle;
        public bool simulationCacheValid;
        public readonly List<BlockHandle> blockHandles = new List<BlockHandle>();
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
        public readonly List<BlockHandle> blockHandles = new List<BlockHandle>();

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
        public BeltItemLineLaneKey(BlockHandle blockHandle, int laneIndex)
        {
            BlockHandle = blockHandle;
            LaneIndex = laneIndex;
        }

        public readonly BlockHandle BlockHandle;
        public readonly int LaneIndex;

        public bool Equals(BeltItemLineLaneKey other)
        {
            return BlockHandle == other.BlockHandle && LaneIndex == other.LaneIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is BeltItemLineLaneKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (BlockHandle.GetHashCode() * 397) ^ LaneIndex;
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
    private List<ResourceEntry> oilResources = new List<ResourceEntry>();
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

    [Header("Runtime Rendering")]
    [SerializeField, Min(0), Tooltip("플레이어를 중심으로 전체 렌더링을 유지할 블록 반경입니다. 범위 밖 오브젝트는 계속 동작하지만 렌더링만 꺼집니다.")]
    private int playerRenderRadius = 64;

    [Header("Editor Preview")]
    [SerializeField]
    private bool expandEditorPreviewRange = false;

    [Header("Chunk Streaming")]
    [SerializeField, Min(1)]
    private int chunkGenerationBlocksPerFrame = 16;

    [SerializeField, Min(1)]
    private int chunkSurfaceRowsPerFrame = 12;

    [SerializeField, Min(1)]
    private int chunkInstallationRestoresPerFrame = 1;

    [SerializeField, Min(0.25f)]
    private float chunkGenerationFrameTimeBudgetMilliseconds = 3f;

    [Header("Conveyor Runtime")]
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

    private readonly Dictionary<Vector2Int, ChunkRuntimeData> loadedChunks =
        new Dictionary<Vector2Int, ChunkRuntimeData>();
    private readonly BlockDataStore loadedBlocks = new BlockDataStore();
    private int suppressedBlockProxyMaterializationDepth;
    private readonly PlayerRangeCullingIndex playerRangeCullingIndex =
        new PlayerRangeCullingIndex();
    private readonly List<Renderer> terrainRendererScratch = new List<Renderer>(256);
    private readonly HashSet<Renderer> terrainRendererScanSet = new HashSet<Renderer>();
    private readonly List<Collider> terrainColliderScratch = new List<Collider>(256);
    private readonly HashSet<Collider> terrainColliderScanSet = new HashSet<Collider>();
    private readonly List<Vector2Int> chunksToGenerateScratch = new List<Vector2Int>();
    private ChunkSurfaceBuildData reusableChunkSurfaceBuildData;
    private ChunkSurfaceWorkerInput reusableChunkSurfaceWorkerInput;
    private readonly List<Block> chunkRuntimeBlockScratch = new List<Block>();
    private readonly ChunkDistanceComparer chunkDistanceComparer = new ChunkDistanceComparer();
    private readonly HashSet<BlockHandle> activeConveyors = new HashSet<BlockHandle>();
    private readonly List<BlockHandle> conveyorTickBuffer = new List<BlockHandle>();
    private readonly List<BlockHandle> activeConveyorDataMotionBlocks = new List<BlockHandle>();
    private readonly Dictionary<BlockHandle, int> activeConveyorDataMotionIndices = new Dictionary<BlockHandle, int>();
    private readonly Dictionary<BlockHandle, float> activeConveyorDataMotionDueTimes = new Dictionary<BlockHandle, float>();
    private readonly List<BlockHandle> sortedActiveConveyors = new List<BlockHandle>();
    private readonly HashSet<BlockHandle> activeConveyorDotVisuals = new HashSet<BlockHandle>();
    private readonly List<BlockHandle> activeConveyorDotVisualList = new List<BlockHandle>();
    private readonly List<BlockHandle> conveyorDotVisualTickBuffer = new List<BlockHandle>();
    private readonly HashSet<BlockHandle> activeBeltDirectionVisuals = new HashSet<BlockHandle>();
    private readonly List<BlockHandle> activeBeltDirectionVisualList = new List<BlockHandle>();
    private readonly List<Matrix4x4> directionArrowMatrixScratch = new List<Matrix4x4>(4);
    private readonly List<BlockHandle> pendingConveyorSlotDotRefreshBlocks = new List<BlockHandle>();
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
    private readonly List<BlockHandle> pendingBeltItemLineDebugRefreshBlocks = new List<BlockHandle>(512);
    private readonly HashSet<BlockHandle> pendingBeltItemLineDebugRefreshSet = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> conveyorItemVisualBlocks = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> conveyorItemVisualDirtyBlocks = new HashSet<BlockHandle>();
    private readonly List<BlockHandle> dynamicConveyorItemVisualBlocks = new List<BlockHandle>(256);
    private readonly Dictionary<BlockHandle, int> dynamicConveyorItemVisualBlockIndices = new Dictionary<BlockHandle, int>();
    private readonly Dictionary<BlockHandle, int> conveyorItemCountsByBlock = new Dictionary<BlockHandle, int>();
    private readonly HashSet<BlockHandle> conveyorDropBlockScratch = new HashSet<BlockHandle>();
    private int pendingBeltItemLineDebugRefreshIndex;
    private int conveyorItemVisualBlockSetVersion;
    private int dynamicConveyorItemVisualBlockSetVersion;
    private int cachedLoadedConveyorItemCount;
    private int authoritativeConveyorItemTotal;
    private bool authoritativeConveyorItemTotalInitialized;
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
    private int conveyorStateSaveConveyorBlocks;
    private int conveyorStateSaveConveyorItems;
    private int conveyorStateSaveClearedNonConveyorBlocks;
    private readonly Dictionary<BlockHandle, int> conveyorNetworkIds = new Dictionary<BlockHandle, int>();
    private readonly Dictionary<int, List<BlockHandle>> conveyorNetworkBlocksById = new Dictionary<int, List<BlockHandle>>();
    private readonly Dictionary<int, float> conveyorNetworkRetryTimes = new Dictionary<int, float>();
    private readonly HashSet<int> conveyorNetworkSleepingIds = new HashSet<int>();
    private readonly HashSet<int> conveyorNetworkActiveIds = new HashSet<int>();
    private readonly HashSet<int> conveyorNetworkSleepCheckQueuedIds = new HashSet<int>();
    private readonly List<int> conveyorNetworkSleepCheckBuffer = new List<int>();
    private readonly Queue<BlockHandle> conveyorNetworkBuildQueue = new Queue<BlockHandle>();
    private readonly Queue<BlockHandle> conveyorWakeQueue = new Queue<BlockHandle>();
    private readonly Queue<int> conveyorLineWakeQueue = new Queue<int>();
    private readonly HashSet<BlockHandle> conveyorWakeQueued = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> conveyorDirectWakeBlocks = new HashSet<BlockHandle>();
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
    private readonly Dictionary<BlockHandle, ConveyorLineSlot> conveyorLineSlots = new Dictionary<BlockHandle, ConveyorLineSlot>();
    private readonly HashSet<BlockHandle> conveyorLineVisited = new HashSet<BlockHandle>();
    private readonly Dictionary<BlockHandle, int> conveyorLineBuildIndices = new Dictionary<BlockHandle, int>();
    private readonly HashSet<int> conveyorLinesTickedThisFrame = new HashSet<int>();
    private readonly List<BlockHandle> conveyorLineTouchedBlocks = new List<BlockHandle>();
    private readonly HashSet<BlockHandle> conveyorLineTouchedSet = new HashSet<BlockHandle>();
    private readonly Queue<int> conveyorCornerGroupWakeQueue = new Queue<int>();
    private readonly HashSet<int> conveyorCornerGroupWakeQueued = new HashSet<int>();
    private readonly Dictionary<int, List<BlockHandle>> conveyorCornerGroupWakeBlocksById = new Dictionary<int, List<BlockHandle>>();
    private readonly HashSet<BlockHandle> conveyorCornerGroupWakeQueuedBlocks = new HashSet<BlockHandle>();
    private readonly List<ConveyorCornerGroup> conveyorCornerGroups = new List<ConveyorCornerGroup>();
    private readonly Dictionary<int, ConveyorCornerGroup> conveyorCornerGroupsById = new Dictionary<int, ConveyorCornerGroup>();
    private readonly Dictionary<BlockHandle, ConveyorCornerGroupSlot> conveyorCornerGroupSlots = new Dictionary<BlockHandle, ConveyorCornerGroupSlot>();
    private readonly HashSet<BlockHandle> conveyorCornerGroupVisited = new HashSet<BlockHandle>();
    private readonly Dictionary<BlockHandle, int> conveyorCornerGroupBuildIndices = new Dictionary<BlockHandle, int>();
    private readonly List<BlockHandle> conveyorCornerGroupTickBlocks = new List<BlockHandle>();
    private readonly HashSet<BlockHandle> deferredConveyorRuntimeRefreshBlocks = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> deferredConveyorNetworkWakeBlocks = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> deferredConveyorMoveAttemptWakeAroundBlocks = new HashSet<BlockHandle>();
    private readonly HashSet<BlockHandle> deferredConveyorMoveAttemptWakeFlowBlocks = new HashSet<BlockHandle>();
    private readonly List<ConveyorItemLaneSaveState> conveyorItemCountLaneScratch = new List<ConveyorItemLaneSaveState>();
    private readonly Dictionary<Vector2Int, TerrainBiome> tileBiomeCache = new Dictionary<Vector2Int, TerrainBiome>();
    private readonly Dictionary<Vector2Int, bool> rawWaterCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> directWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<Vector2Int, bool> bufferedWaterBlockCache = new Dictionary<Vector2Int, bool>();
    private readonly Dictionary<TerrainBiome, Material> biomeMaterialCache = new Dictionary<TerrainBiome, Material>();

    private bool hasGeneratedChunks;
    private bool hasSeedInitialized;
    private int terrainGenerationVersion;
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
    private int lastActiveConveyorCornerGroupNoProgressRequeuesSkipped;
    private int lastActiveConveyorBlockWakeTicks;
    private int lastActiveConveyorBlockNoProgressRequeuesSkipped;
    private int lastActiveConveyorDuplicateFrameTicksSkipped;
    private int lastActiveConveyorBlockWakeLineFallbacks;
    private int lastActiveConveyorFullLineWakesProcessed;
    private int lastActiveConveyorRangedLineWakesProcessed;
    private int lastActiveConveyorDeferredLineWakesPromoted;
    private int lastActiveConveyorLineNoMoveWakes;
    private int lastActiveConveyorLineNoMoveBlocksChanged;
    private int lastActiveConveyorLineNoMoveBlocksSkipped;
    private int lastActiveConveyorLineNoMoveDirectFallbacks;
    private int lastActiveConveyorLineWakesDroppedByRetryThrottle;
    private int lastActiveConveyorDeferredLineWakesDroppedByRetryThrottle;
    private int lastActiveConveyorLineRetryRangeMerges;
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
    private Vector2Int currentCenterChunk;
    private Vector2Int currentPlayerRenderCenter;
    private bool hasPlayerRenderCenter;
    private int appliedPlayerRenderRadius = -1;
    private BlockStateStore resourceStateStore;
    private InstallationPlacementController installationRestoreController;
    private InstallationObjectPool installationObjectPool;
    private PortableItemRenderer portableItemRenderer;
    private VirtualConveyorBeltRenderer virtualConveyorBeltRenderer;
    private TerrainChunkStreamingScheduler chunkStreamingScheduler;

    private readonly List<ResourceEntry> starterTreeCacheEntries = new List<ResourceEntry>();
    private readonly List<Vector2Int> starterTreeCacheCandidates = new List<Vector2Int>();
    private readonly Dictionary<Vector2Int, Resource> starterTreeCacheLookup = new Dictionary<Vector2Int, Resource>();
    private int starterTreeCacheSeed = int.MinValue;
    private int starterTreeCacheConfigHash = int.MinValue;
    private bool starterTreeCacheValid;
    private Material generatedSurfaceBlendMaterial;
    private Material generatedSurfaceFoamMaterial;
    private Material[] generatedSurfaceMaterials;

    private void OnValidate()
    {
        generatedSurfaceMaterials = null;
        NormalizeTerrainBoundsSettings();
        playerRenderRadius = Mathf.Max(0, playerRenderRadius);
        starterOreMaxResourceCount = Mathf.Max(starterOreMinResourceCount, starterOreMaxResourceCount);
        normalOreMaxResourceCount = Mathf.Max(normalOreMinResourceCount, normalOreMaxResourceCount);
        starterTreeMaxCount = Mathf.Max(starterTreeMinCount, starterTreeMaxCount);
        waterSurfaceDepth = Mathf.Max(0f, waterSurfaceDepth);
        waterFoamWidth = Mathf.Max(0f, waterFoamWidth);
        waterFoamSurfaceOffset = Mathf.Max(0f, waterFoamSurfaceOffset);
        waterSurfaceGlintScale = Mathf.Max(0.01f, waterSurfaceGlintScale);
        waterSurfaceGlintFlowSpeed = Mathf.Max(0f, waterSurfaceGlintFlowSpeed);
        NormalizeResourceGenerationSettings();
        NormalizeAnimalGenerationSettings();
        InvalidateTerrainGenerationCaches();
#if UNITY_EDITOR
        PopulateGeneratedSurfaceBlendEditorDefaults();
#endif
        ApplyGeneratedSurfaceRuntimeMaterialSettings();
    }

    private void Awake()
    {
        Active = this;
        loadedBlocks.ConfigureChunkSize(Mathf.Max(4, chunkSize));
        EnsurePortableItemRenderer();
        EnsureVirtualConveyorBeltRenderer();
    }

    private void OnEnable()
    {
        Active = this;
#if UNITY_EDITOR
        SceneView.duringSceneGui -= RenderEditorChunkSurfaces;
        SceneView.duringSceneGui += RenderEditorChunkSurfaces;
#endif
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
        NormalizeOilResourceEntries(oilResources);
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
        loadedBlocks.ConfigureChunkSize(chunkSize);
        NormalizeResourceGenerationSettings();
        NormalizeAnimalGenerationSettings();
        EnsureResourceStateStore();
        EnsurePortableItemRenderer();
        EnsureVirtualConveyorBeltRenderer();

        SaveManager saveManager = FindFirstObjectByType<SaveManager>();
        bool terrainInitializationIsDeferred =
            saveManager != null && saveManager.WillInitializeTerrainOnStart;
        if (generateOnStart && !terrainInitializationIsDeferred)
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

        RefreshTrackedPlayerRangeCullingIfNeeded();
        playerRangeCullingIndex.RefreshDynamicComponents(
            DynamicRangeCullingBudgetPerFrame,
            DynamicRangeCullingBudgetPerFrame);

        bool profileBeltTicks = RefreshBeltTickProfilerFrameState();

        TickFarmlandFertilizerAbsorption();

        profileBeltTicks = RefreshBeltTickProfilerFrameState();

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

        using (RenderChunkSurfacesMarker.Auto())
        {
            RenderLoadedChunkSurfaces();
        }

    }

    private bool RefreshBeltTickProfilerFrameState()
    {
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

        return profileBeltTicks;
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

    public int PlayerRenderRadius => Mathf.Max(0, playerRenderRadius);

    public void GetPlayerRenderCoordinateBounds(
        out Vector2Int minCoordinate,
        out Vector2Int maxCoordinate)
    {
        Vector2Int centerCoordinate = GetTrackingBlockCoordinate();
        int radius = PlayerRenderRadius;
        Vector2Int range = new Vector2Int(radius, radius);
        minCoordinate = centerCoordinate - range;
        maxCoordinate = centerCoordinate + range;
    }

    public bool IsWorldPositionWithinPlayerRenderRange(Vector3 worldPosition)
    {
        if (!Application.isPlaying)
        {
            return true;
        }

        EnsurePlayerRenderCenter();
        int radius = PlayerRenderRadius;
        return Mathf.Abs(Mathf.RoundToInt(worldPosition.x) - currentPlayerRenderCenter.x) <= radius
               && Mathf.Abs(Mathf.RoundToInt(worldPosition.z) - currentPlayerRenderCenter.y) <= radius;
    }

    private void RefreshTrackedPlayerRangeCullingIfNeeded()
    {
        Vector2Int renderCenter = GetTrackingBlockCoordinate();
        int renderRadius = PlayerRenderRadius;
        if (hasPlayerRenderCenter
            && renderCenter == currentPlayerRenderCenter
            && appliedPlayerRenderRadius == renderRadius)
        {
            return;
        }

        currentPlayerRenderCenter = renderCenter;
        hasPlayerRenderCenter = true;
        appliedPlayerRenderRadius = renderRadius;
        playerRangeCullingIndex.SetRange(renderCenter, renderRadius);
    }

    private void EnsurePlayerRenderCenter()
    {
        if (hasPlayerRenderCenter)
        {
            return;
        }

        currentPlayerRenderCenter = GetTrackingBlockCoordinate();
        hasPlayerRenderCenter = true;
        appliedPlayerRenderRadius = PlayerRenderRadius;
        playerRangeCullingIndex.SetRange(currentPlayerRenderCenter, appliedPlayerRenderRadius);
    }

    private Vector2Int GetTrackingBlockCoordinate()
    {
        ResolveTrackingTarget();
        Vector3 sourcePosition = trackingTarget != null ? trackingTarget.position : transform.position;
        return new Vector2Int(Mathf.RoundToInt(sourcePosition.x), Mathf.RoundToInt(sourcePosition.z));
    }

    private void RefreshAllTerrainRangeCulling()
    {
        EnsurePlayerRenderCenter();
        terrainRendererScratch.Clear();
        terrainRendererScanSet.Clear();
        transform.GetComponentsInChildren(true, terrainRendererScratch);

        for (int i = 0; i < terrainRendererScratch.Count; i++)
        {
            Renderer targetRenderer = terrainRendererScratch[i];
            if (targetRenderer == null || !targetRenderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            terrainRendererScanSet.Add(targetRenderer);
            playerRangeCullingIndex.Register(targetRenderer);
        }

        playerRangeCullingIndex.RemoveMissing(terrainRendererScanSet);

        terrainColliderScratch.Clear();
        terrainColliderScanSet.Clear();
        transform.GetComponentsInChildren(true, terrainColliderScratch);
        for (int i = 0; i < terrainColliderScratch.Count; i++)
        {
            Collider targetCollider = terrainColliderScratch[i];
            if (targetCollider == null || !targetCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            terrainColliderScanSet.Add(targetCollider);
            playerRangeCullingIndex.Register(targetCollider);
        }

        playerRangeCullingIndex.RemoveMissing(terrainColliderScanSet);
    }

    private void RefreshTerrainRangeCulling(Transform hierarchyRoot)
    {
        if (!Application.isPlaying || hierarchyRoot == null)
        {
            return;
        }

        EnsurePlayerRenderCenter();
        terrainRendererScratch.Clear();
        hierarchyRoot.GetComponentsInChildren(true, terrainRendererScratch);
        for (int i = 0; i < terrainRendererScratch.Count; i++)
        {
            Renderer targetRenderer = terrainRendererScratch[i];
            if (targetRenderer != null && targetRenderer.gameObject.activeInHierarchy)
            {
                playerRangeCullingIndex.Register(targetRenderer);
            }
        }

        terrainColliderScratch.Clear();
        hierarchyRoot.GetComponentsInChildren(true, terrainColliderScratch);
        for (int i = 0; i < terrainColliderScratch.Count; i++)
        {
            Collider targetCollider = terrainColliderScratch[i];
            if (targetCollider != null && targetCollider.gameObject.activeInHierarchy)
            {
                playerRangeCullingIndex.Register(targetCollider);
            }
        }
    }

    private void RefreshTerrainRangeCulling(Block[] blocksToRegister)
    {
        IEnumerator routine = RefreshTerrainRangeCullingRoutine(blocksToRegister, false);
        while (routine.MoveNext())
        {
        }
    }

    private IEnumerator RefreshTerrainRangeCullingRoutine(
        IReadOnlyList<Block> blocksToRegister,
        bool allowYield)
    {
        if (!Application.isPlaying || blocksToRegister == null)
        {
            yield break;
        }

        int processedSinceYield = 0;
        int countBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);
        double stepStartTime = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < blocksToRegister.Count; i++)
        {
            Block block = blocksToRegister[i];
            if (block != null)
            {
                if (block.MapObject != null)
                {
                    using (RefreshChunkRangeCullingMarker.Auto())
                    {
                        RefreshTerrainRangeCulling(block.MapObject.transform);
                    }
                }
            }

            processedSinceYield++;
            if (allowYield
                && (processedSinceYield >= countBudget
                    || HasExceededChunkGenerationFrameBudget(stepStartTime)))
            {
                processedSinceYield = 0;
                yield return null;
                stepStartTime = Time.realtimeSinceStartupAsDouble;
            }
        }
    }

    private bool HasExceededChunkGenerationFrameBudget(double stepStartTime)
    {
        return (Time.realtimeSinceStartupAsDouble - stepStartTime) * 1000.0
               >= Mathf.Max(0.25f, chunkGenerationFrameTimeBudgetMilliseconds);
    }

    public bool DoesWorldBoundsIntersectPlayerRenderRange(Bounds bounds)
    {
        EnsurePlayerRenderCenter();
        return playerRangeCullingIndex.Intersects(bounds);
    }

    private void RestorePlayerRangeCulling(Transform hierarchyRoot = null)
    {
        if (hierarchyRoot == null)
        {
            playerRangeCullingIndex.Clear(true);
            terrainRendererScratch.Clear();
            terrainRendererScanSet.Clear();
            terrainColliderScratch.Clear();
            terrainColliderScanSet.Clear();
            hasPlayerRenderCenter = false;
            appliedPlayerRenderRadius = -1;
            return;
        }

        terrainRendererScratch.Clear();
        hierarchyRoot.GetComponentsInChildren(true, terrainRendererScratch);
        for (int i = 0; i < terrainRendererScratch.Count; i++)
        {
            Renderer targetRenderer = terrainRendererScratch[i];
            playerRangeCullingIndex.Unregister(targetRenderer, true);
        }

        terrainColliderScratch.Clear();
        hierarchyRoot.GetComponentsInChildren(true, terrainColliderScratch);
        for (int i = 0; i < terrainColliderScratch.Count; i++)
        {
            Collider targetCollider = terrainColliderScratch[i];
            playerRangeCullingIndex.Unregister(targetCollider, true);
        }
    }

    private void RestorePlayerRangeCulling(Block[] blocksToUnregister)
    {
        if (blocksToUnregister == null)
        {
            return;
        }

        for (int i = 0; i < blocksToUnregister.Length; i++)
        {
            Block block = blocksToUnregister[i];
            if (block != null)
            {
                if (block.MapObject != null)
                {
                    RestorePlayerRangeCulling(block.MapObject.transform);
                }
            }
        }
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        SceneView.duringSceneGui -= RenderEditorChunkSurfaces;
#endif
        if (Active == this)
        {
            Active = null;
        }

        RestorePlayerRangeCulling();
        ClearConveyorRuntimeState();
        ResetAuthoritativeConveyorItemTotal();
        ClearPendingChunkGenerations();
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            ReleaseChunkSurfaceMeshes(pair.Value);
        }

        loadedChunks.Clear();
    }

    public bool VirtualizeConveyorItems => false;
    public bool VirtualizeConveyorBelts => true;
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
        if (!Application.isPlaying)
        {
            return CalculateConveyorItemCountSnapshot();
        }

        EnsureAuthoritativeConveyorItemTotalInitialized();
        return authoritativeConveyorItemTotal;
    }

    public void NotifyConveyorItemAddedToBelt()
    {
        AddAuthoritativeConveyorItemTotalDelta(1);
    }

    public void NotifyConveyorItemRemovedFromBelt()
    {
        AddAuthoritativeConveyorItemTotalDelta(-1);
    }

    private void EnsureAuthoritativeConveyorItemTotalInitialized()
    {
        if (authoritativeConveyorItemTotalInitialized)
        {
            return;
        }

        authoritativeConveyorItemTotal = CalculateConveyorItemCountSnapshot();
        authoritativeConveyorItemTotalInitialized = true;
    }

    private void AddAuthoritativeConveyorItemTotalDelta(int delta)
    {
        if (delta == 0 || !authoritativeConveyorItemTotalInitialized)
        {
            return;
        }

        authoritativeConveyorItemTotal = Mathf.Max(0, authoritativeConveyorItemTotal + delta);
    }

    private void ResetAuthoritativeConveyorItemTotal()
    {
        authoritativeConveyorItemTotal = 0;
        authoritativeConveyorItemTotalInitialized = false;
    }

    private int CalculateConveyorItemCountSnapshot()
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

            count += CaptureLoadedConveyorItemCountContribution(pair.Key, block);
        }

        return Mathf.Max(0, count);
    }

    private int CaptureLoadedConveyorItemCountContribution(Vector2Int coordinate, Block block)
    {
        if (block == null || !block.IsRuntimeConveyor)
        {
            return 0;
        }

        if (resourceStateStore == null
            || !resourceStateStore.TryGetConveyorItems(coordinate, out List<ConveyorItemLaneSaveState> savedLanes))
        {
            return block.GetRuntimeConveyorItemCount();
        }

        int savedLaneCount = CountConveyorItemSaveLanes(savedLanes);
        if (savedLaneCount <= 0)
        {
            return block.GetRuntimeConveyorItemCount();
        }

        conveyorItemCountLaneScratch.Clear();
        try
        {
            block.CaptureConveyorItemSaveStates(conveyorItemCountLaneScratch);
            int liveCount = CountConveyorItemSaveLanes(conveyorItemCountLaneScratch);
            if (liveCount <= 0)
            {
                return 0;
            }

            int overlapCount = CountConveyorItemLaneOverlap(savedLanes, conveyorItemCountLaneScratch);
            return liveCount - overlapCount;
        }
        finally
        {
            conveyorItemCountLaneScratch.Clear();
        }
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

    public bool HasStoredInstallationItem(int itemId)
    {
        EnsureResourceStateStore();
        return resourceStateStore != null && resourceStateStore.HasStoredInstallationItem(itemId);
    }

    public void SaveRuntimeInstallationState(InstallationObject installationObject)
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null
            || installationObject == null
            || installationObject.ExcludeFromTerrainPersistence)
        {
            return;
        }

        if (installationObject is Trainstation trainStation)
        {
            EnsureTrainStationNameAssigned(trainStation);
        }

        resourceStateStore.SaveInstallation(installationObject);
        resourceStateStore.RegisterLiveInstallation(installationObject);
        if (installationObject is Trainstation || installationObject is Railload)
        {
            RefreshAutomaticTrainStationNames();
        }
    }

    public void RefreshMovedInstallationRuntimeState(
        InstallationObject installationObject,
        Vector2Int previousAnchorCoordinate,
        bool runtimePlacementChanged)
    {
        if (installationObject == null
            || !installationObject.TryGetPlacementRuntime(out Vector2Int currentAnchorCoordinate, out _))
        {
            return;
        }

        if (previousAnchorCoordinate != currentAnchorCoordinate)
        {
            if (TryGetLoadedBlock(previousAnchorCoordinate, out Block previousBlock)
                && previousBlock != null
                && previousBlock.MapObject == installationObject)
            {
                previousBlock.SetMapObject(null);
            }

            if (TryGetLoadedBlock(currentAnchorCoordinate, out Block currentBlock)
                && currentBlock != null
                && (currentBlock.MapObject == null || currentBlock.MapObject == installationObject))
            {
                currentBlock.SetMapObject(installationObject);
            }
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null || installationObject.ExcludeFromTerrainPersistence)
        {
            return;
        }

        if (runtimePlacementChanged)
        {
            SaveRuntimeInstallationState(installationObject);
        }
        else if (!resourceStateStore.UpdateLiveInstallationWorldPose(installationObject))
        {
            SaveRuntimeInstallationState(installationObject);
        }
    }

    public TerrainSaveData CaptureTerrainSaveState()
    {
        List<Vector2Int> activeChunkCoordinates = new List<Vector2Int>(loadedChunks.Count);
        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            if (pair.Value != null)
            {
                activeChunkCoordinates.Add(pair.Key);
            }
        }

        activeChunkCoordinates.Sort(CompareChunkCoordinates);
        return new TerrainSaveData
        {
            seed = seed,
            mapSize = GetNormalizedMapSize(),
            activeChunkCoordinates = activeChunkCoordinates
        };
    }

    private static int CompareChunkCoordinates(Vector2Int left, Vector2Int right)
    {
        int yComparison = left.y.CompareTo(right.y);
        return yComparison != 0 ? yComparison : left.x.CompareTo(right.x);
    }

    public MapSaveData CaptureMapSaveState()
    {
        EnsureResourceStateStore();
        MapSaveData mapSaveData = new MapSaveData();
        if (resourceStateStore != null)
        {
            FlushLoadedRuntimeStateToStore();
            resourceStateStore.CaptureSaveState(mapSaveData);
            CaptureLoadedConveyorItemSaveStates(mapSaveData);
        }

        CaptureAnimalSaveStates(mapSaveData);
        CaptureFarmlandSaveState(mapSaveData);
        SaveGameConveyorItemBackfill.BackfillFromFloorObjects(mapSaveData);
        return mapSaveData;
    }

    public void LoadFromSaveState(TerrainSaveData terrainSaveData, MapSaveData mapSaveData)
    {
        NormalizeTerrainBoundsSettings();
        NormalizeResourceGenerationSettings();
        NormalizeAnimalGenerationSettings();
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
            hasSeedInitialized = true;
        }

        InvalidateTerrainGenerationCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks(false, true);
        ApplyFarmlandSaveState(mapSaveData);
        ApplyAnimalSaveStates(mapSaveData);
        SaveGameConveyorItemBackfill.BackfillFromFloorObjects(mapSaveData);
        resourceStateStore?.ApplySaveState(mapSaveData);

        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        if (!QueueSavedActiveChunks(terrainSaveData?.activeChunkCoordinates))
        {
            RefreshChunks(currentCenterChunk, true);
        }
        ProcessQueuedChunkGenerationsImmediate();
        RefreshLoadedConveyorBeltRuntimeViews();
        ApplyLoadedConveyorItemSaveStates(mapSaveData);
        RefreshLoadedRuntimeRegistrations();
        RefreshLoadedRuntimeVisibility();
        RefreshAllTerrainRangeCulling();
    }

    private bool QueueSavedActiveChunks(IReadOnlyList<Vector2Int> activeChunkCoordinates)
    {
        if (activeChunkCoordinates == null || activeChunkCoordinates.Count <= 0)
        {
            return false;
        }

        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        bool queuedAny = false;
        for (int i = 0; i < activeChunkCoordinates.Count; i++)
        {
            Vector2Int chunkCoordinate = activeChunkCoordinates[i];
            if (!DoesChunkIntersectMapBounds(chunkCoordinate, normalizedChunkSize))
            {
                continue;
            }

            QueueChunkGeneration(chunkCoordinate, normalizedChunkSize);
            queuedAny = true;
        }

        return queuedAny;
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

            SaveLoadedBlockFloorObjects(block);

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

    private IEnumerator ApplyStoredConveyorItemSaveStatesRoutine(Block[] blocks, bool allowYield)
    {
        if (blocks == null || blocks.Length <= 0)
        {
            yield break;
        }

        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            yield break;
        }

        int processedSinceYield = 0;
        int blockBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);
        double stepStartTime = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < blocks.Length; i++)
        {
            using (RestoreChunkConveyorItemsMarker.Auto())
            {
                Block block = blocks[i];
                if (block != null
                    && resourceStateStore.TryGetConveyorItems(
                        block.Coordinate,
                        out List<ConveyorItemLaneSaveState> lanes)
                    && CountConveyorItemSaveLanes(lanes) > 0)
                {
                    ApplyLoadedConveyorItemSaveStatesToBlock(block.Coordinate, block, lanes, null, false);
                }
            }

            if (allowYield
                && (++processedSinceYield >= blockBudget
                    || HasExceededChunkGenerationFrameBudget(stepStartTime)))
            {
                processedSinceYield = 0;
                yield return null;
                stepStartTime = Time.realtimeSinceStartupAsDouble;
            }
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

    private static int CountConveyorItemLaneOverlap(
        IReadOnlyList<ConveyorItemLaneSaveState> savedLanes,
        IReadOnlyList<ConveyorItemLaneSaveState> runtimeLanes)
    {
        if (savedLanes == null || runtimeLanes == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < savedLanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = savedLanes[i];
            if (lane != null
                && lane.itemId >= 0
                && lane.laneIndex >= 0
                && ContainsConveyorItemSaveLane(runtimeLanes, lane.laneIndex))
            {
                count++;
            }
        }

        return count;
    }

    private static bool ContainsConveyorItemSaveLane(
        IReadOnlyList<ConveyorItemLaneSaveState> lanes,
        int laneIndex)
    {
        if (lanes == null || laneIndex < 0)
        {
            return false;
        }

        for (int i = 0; i < lanes.Count; i++)
        {
            ConveyorItemLaneSaveState lane = lanes[i];
            if (lane != null && lane.laneIndex == laneIndex && lane.itemId >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void MarkLoadedConveyorItemBlockLive(Vector2Int coordinate)
    {
        resourceStateStore?.SetFloorObjectsResidency(coordinate, VirtualObjectResidency.Live);
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

    public void CopyConveyorItemVisualBlocks(List<BlockHandle> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        results.AddRange(conveyorItemVisualBlocks);
    }

    public void CopyDynamicConveyorItemVisualBlocks(List<BlockHandle> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        results.AddRange(dynamicConveyorItemVisualBlocks);
    }

    public void CopyConveyorItemVisualDirtyBlocks(List<BlockHandle> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        results.AddRange(conveyorItemVisualDirtyBlocks);

        conveyorItemVisualDirtyBlocks.Clear();
    }

    public void Generate()
    {
        NormalizeTerrainBoundsSettings();
        NormalizeResourceGenerationSettings();
        NormalizeAnimalGenerationSettings();
        EnsureResourceStateStore();
        InitializeSeedForGeneration();
        InvalidateTerrainGenerationCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks(false, true);
        ClearAnimalPersistentState();
        ClearFarmlandPersistentState();
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
        NormalizeAnimalGenerationSettings();
        EnsureResourceStateStore();
        InvalidateTerrainGenerationCaches();
        ClearPendingChunkGenerations();
        ClearLoadedChunks();

        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        RefreshChunks(currentCenterChunk, true);
    }

#if UNITY_EDITOR
    public bool HasEditorPreviewChunks()
    {
        if (Application.isPlaying)
        {
            return false;
        }

        return loadedChunks.Count > 0;
    }

    public void ClearEditorPreviewChunks()
    {
        if (Application.isPlaying)
        {
            return;
        }

        ClearPendingChunkGenerations();
        ClearLoadedChunks(false, true);
        hasGeneratedChunks = false;
    }
#endif

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

        loadedBlocks.ConfigureChunkSize(normalizedChunkSize);
        if (loadedChunks.TryGetValue(chunkCoordinate, out ChunkRuntimeData existingChunk))
        {
            Block[] existingBlocks = GetChunkRuntimeBlocks(chunkCoordinate);
            SaveChunkResourceStates(existingBlocks);
            ForgetAnimalRuntimeIds(chunkCoordinate);
            DestroyAnimalViewsInChunk(chunkCoordinate);
            RestorePlayerRangeCulling(existingBlocks);
            RemoveChunkBlocksFromLookup(existingBlocks);
            ReleaseChunkBlockRuntimeProxies(existingBlocks);
            ReleaseChunkSurfaceMeshes(existingChunk);
            loadedChunks.Remove(chunkCoordinate);
            loadedBlocks.UnregisterChunk(chunkCoordinate);
        }

        Vector2Int origin = new Vector2Int(chunkCoordinate.x * normalizedChunkSize, chunkCoordinate.y * normalizedChunkSize);
        loadedBlocks.RegisterChunk(chunkCoordinate);
        ChunkRuntimeData chunk = new ChunkRuntimeData(chunkCoordinate, origin);
        loadedChunks.Add(chunkCoordinate, chunk);
        List<Block> generatedChunkBlocks = new List<Block>(64);
        int blocksSinceYield = 0;
        int blockBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);
        double blockStepStartTime = Time.realtimeSinceStartupAsDouble;

        for (int localY = 0; localY < normalizedChunkSize; localY++)
        {
            for (int localX = 0; localX < normalizedChunkSize; localX++)
            {
                Vector2Int worldCoordinate = new Vector2Int(origin.x + localX, origin.y + localY);
                if (!IsCoordinateInsideMapBounds(worldCoordinate))
                {
                    continue;
                }

                loadedBlocks.RegisterCell(worldCoordinate, Block.BlockType.Ground, out _);
                bool requiresRuntimeProxy = RequiresInitialBlockRuntimeProxy(
                    worldCoordinate,
                    out Resource generatedResourcePrefab);
                if (!requiresRuntimeProxy)
                {
                    if (allowYield
                        && (++blocksSinceYield >= blockBudget
                            || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
                    {
                        blocksSinceYield = 0;
                        yield return null;
                        blockStepStartTime = Time.realtimeSinceStartupAsDouble;
                    }

                    continue;
                }

                Block block;
                using (GenerateChunkRuntimeProxyMarker.Auto())
                {
                    block = CreateBlock(
                        groundSet,
                        Block.BlockType.Ground,
                        worldCoordinate);
                    if (block != null)
                    {
                        generatedChunkBlocks.Add(block);

                        RefreshFarmlandVisual(block);
                        bool spawnedPlantedResource =
                            TrySpawnPlantedResourceOnBlock(block, worldCoordinate);
                        if (!spawnedPlantedResource
                            && generatedResourcePrefab != null)
                        {
                            SpawnResourceOnBlock(block, generatedResourcePrefab, worldCoordinate);
                        }
                    }
                }
                if (block == null)
                {
                    continue;
                }

                if (allowYield
                    && (++blocksSinceYield >= blockBudget
                        || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
                {
                    blocksSinceYield = 0;
                    yield return null;
                    blockStepStartTime = Time.realtimeSinceStartupAsDouble;
                }
            }
        }

        if (allowYield)
        {
            yield return null;
        }

        Block[] chunkBlocks = generatedChunkBlocks.ToArray();
        IEnumerator installationRestoreRoutine = RestoreChunkInstallationsRoutine(chunkBlocks, allowYield);
        while (installationRestoreRoutine.MoveNext())
        {
            if (allowYield)
            {
                yield return installationRestoreRoutine.Current;
            }
        }

        IEnumerator blockStateRestoreRoutine = RestoreChunkBlockStatesRoutine(chunkBlocks, allowYield);
        while (blockStateRestoreRoutine.MoveNext())
        {
            if (allowYield)
            {
                yield return blockStateRestoreRoutine.Current;
            }
        }

        IEnumerator animalSpawnRoutine = SpawnAnimalsForChunkRoutine(chunkCoordinate, allowYield);
        while (true)
        {
            bool hasNext;
            object current = null;
            using (SpawnChunkAnimalStepMarker.Auto())
            {
                hasNext = animalSpawnRoutine.MoveNext();
                if (hasNext)
                {
                    current = animalSpawnRoutine.Current;
                }
            }

            if (!hasNext)
            {
                break;
            }

            if (allowYield)
            {
                yield return current;
            }
        }
        IEnumerator runtimeViewRoutine = RefreshChunkBlockRuntimeViewsRoutine(chunkBlocks, allowYield);
        while (runtimeViewRoutine.MoveNext())
        {
            if (allowYield)
            {
                yield return runtimeViewRoutine.Current;
            }
        }

        IEnumerator conveyorItemRoutine = ApplyStoredConveyorItemSaveStatesRoutine(chunkBlocks, allowYield);
        while (conveyorItemRoutine.MoveNext())
        {
            if (allowYield)
            {
                yield return conveyorItemRoutine.Current;
            }
        }

        IEnumerator emptyProxyRoutine = ReleaseEmptyChunkBlockRuntimeProxiesRoutine(
            chunkCoordinate,
            chunkBlocks,
            allowYield);
        while (emptyProxyRoutine.MoveNext())
        {
            if (allowYield)
            {
                yield return emptyProxyRoutine.Current;
            }
        }

        ChunkSurfaceBuildData chunkSurface;
        if (Application.isPlaying)
        {
            Task<ChunkSurfaceBuildData> surfaceTask = CreateChunkSurfaceBuildTask(
                origin,
                normalizedChunkSize,
                out ChunkSurfaceBuildData workerSurface);
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

                ReturnChunkSurfaceBuildData(workerSurface);
                chunkSurface = BuildCurvedChunkSurface(origin, normalizedChunkSize);
            }
            else
            {
                chunkSurface = surfaceTask.Result;
            }
        }
        else
        {
            ChunkSurfaceWorkerInput surfaceInput = CreateChunkSurfaceWorkerInput(origin, normalizedChunkSize);
            chunkSurface = RentChunkSurfaceBuildData(surfaceInput);
            IEnumerator surfaceRoutine = BuildCurvedChunkSurfaceRoutine(chunkSurface, origin, normalizedChunkSize, allowYield);
            while (surfaceRoutine.MoveNext())
            {
                if (allowYield)
                {
                    yield return surfaceRoutine.Current;
                }
            }
        }

        if (allowYield)
        {
            yield return null;
        }

        try
        {
            ApplyChunkBiomeSurface(chunk, chunkSurface);
        }
        finally
        {
            ReturnChunkSurfaceBuildData(chunkSurface);
        }

        if (Application.isPlaying)
        {
            IEnumerator cullingRoutine = RefreshTerrainRangeCullingRoutine(chunkBlocks, allowYield);
            while (cullingRoutine.MoveNext())
            {
                if (allowYield)
                {
                    yield return cullingRoutine.Current;
                }
            }
        }

        if (allowYield)
        {
            yield return null;
        }

    }

    private void ClearLoadedChunks(bool preserveRuntimeState = true, bool releaseLiveInstallations = false)
    {
        RestorePlayerRangeCulling();

        if (preserveRuntimeState)
        {
            RefreshAnimalOverridesFromRuntime();
        }

        if (releaseLiveInstallations)
        {
            ReleaseLiveInstallationsForReload();
        }

        List<Block> loadedBlockList = new List<Block>(loadedBlocks.Count);
        CopyLoadedBlocks(loadedBlockList);
        Block[] loadedBlockSnapshot = loadedBlockList.ToArray();
        if (preserveRuntimeState)
        {
            SaveChunkResourceStates(loadedBlockSnapshot);
        }

        RemoveChunkBlocksFromLookup(loadedBlockSnapshot);
        ReleaseChunkBlockRuntimeProxies(loadedBlockSnapshot);
        DestroyAllTerrainAnimalViews();

        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            ReleaseChunkSurfaceMeshes(pair.Value);
        }

        loadedChunks.Clear();
        loadedBlocks.Clear();
        ClearAnimalRuntimeTracking();
        ClearConveyorRuntimeState();
        ResetAuthoritativeConveyorItemTotal();
        if (!releaseLiveInstallations && preserveRuntimeState)
        {
            CleanupOrphanedLiveInstallations();
        }
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
        BlockSet blockSet,
        Block.BlockType blockType,
        Vector2Int coordinate)
    {
        GameObject prefab = SelectBlockPrefab(blockSet);
        if (prefab == null || !prefab.TryGetComponent(out Block template))
        {
            return null;
        }

        // Register the cell before creating the compatibility component so any
        // later simulation-state access resolves through a generation-checked
        // handle. Empty cells never allocate a simulation-state object.
        loadedBlocks.RegisterCell(coordinate, blockType, out BlockHandle handle);

        Block block = gameObject.AddComponent<Block>();
        block.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
        block.ConfigureRuntimeTemplate(template);
        block.BindRuntimeHandle(handle);
        block.Initialize(coordinate, blockType);
        if (loadedBlocks.BindRuntimeProxy(coordinate, block, out BlockHandle boundHandle))
        {
            block.BindRuntimeHandle(boundHandle);
        }
        return block;
    }

    private bool TryMaterializeBlockRuntimeProxy(Vector2Int coordinate, out Block block)
    {
        block = null;
        if (suppressedBlockProxyMaterializationDepth > 0
            || !loadedBlocks.TryGetCell(coordinate, out BlockCellData cellData)
            || !loadedBlocks.TryGetHandle(coordinate, out BlockHandle handle)
            || !loadedChunks.ContainsKey(handle.ChunkCoordinate)
            || !TryGetBlockSet(cellData.Type, out BlockSet blockSet))
        {
            return false;
        }

        block = CreateBlock(
            blockSet,
            cellData.Type,
            coordinate);
        if (block == null)
        {
            return false;
        }

        RefreshFarmlandVisual(block);
        return true;
    }

    private Block[] GetChunkRuntimeBlocks(Vector2Int chunkCoordinate)
    {
        loadedBlocks.CopyRuntimeProxies(chunkCoordinate, chunkRuntimeBlockScratch);
        return chunkRuntimeBlockScratch.Count > 0
            ? chunkRuntimeBlockScratch.ToArray()
            : Array.Empty<Block>();
    }

    private IEnumerator ReleaseEmptyChunkBlockRuntimeProxiesRoutine(
        Vector2Int chunkCoordinate,
        Block[] chunkBlocks,
        bool allowYield)
    {
        if (chunkBlocks == null || chunkBlocks.Length == 0)
        {
            yield break;
        }

        int processedSinceYield = 0;
        int blockBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);
        double stepStartTime = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            using (ReleaseEmptyChunkRuntimeProxyMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null && block.CanReleaseEmptyRuntimeProxy)
                {
                    BlockHandle handle = block.RuntimeHandle;
                    loadedBlocks.Remove(block.Coordinate);
                    ReleaseFarmlandVisual(block.Coordinate);
                    block.PrepareForRuntimeRelease();
                    if (block.HasRuntimeSimulationState)
                    {
                        loadedBlocks.RemoveRuntimeSimulationState(handle);
                    }

                    block.DetachRuntimeSimulationState();
                    if (Application.isPlaying)
                    {
                        Destroy(block);
                    }
                    else
                    {
                        DestroyImmediate(block);
                    }
                }
            }

            if (allowYield
                && (++processedSinceYield >= blockBudget
                    || HasExceededChunkGenerationFrameBudget(stepStartTime)))
            {
                processedSinceYield = 0;
                yield return null;
                stepStartTime = Time.realtimeSinceStartupAsDouble;
            }
        }

        loadedBlocks.CompactRuntimeProxyStorage(chunkCoordinate);
    }


}
