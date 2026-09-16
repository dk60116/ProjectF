using System;
using System.Collections;
using System.Collections.Generic;
using ProjectF.Persistence;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class TerrainGenerator : MonoBehaviour,
    IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IMapObjectSimulationIdentity,
    IMapObjectStagedUpdateTick
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
    public const float GeneratedOilSurfaceLocalY = -0.04f;
    public const int GeneratedOilSurfaceSegmentCount = 48;
    public const float GeneratedOilSurfaceRadius = 0.34f;
    private const int GeneratedOilSurfaceYawStepCount = 8;
    private const int GeneratedOilSurfaceYawSalt = 9127;
    private const float GeneratedOilPitInnerMargin = 0.025f;
    private const float GeneratedOilPitOuterMargin = 0.115f;
    private const float GeneratedOilPitDepth = 0.20f;
    private const int GeneratedSurfaceBiomeMargin = 4;
    private const int ChunkCapacityGrowthHeadroom = 64;
    private const int WorldFinalizationEntriesPerCheckpoint = 64;
    public const float GeneratedOilPitInnerRadius =
        GeneratedOilSurfaceRadius + GeneratedOilPitInnerMargin;
    public const float GeneratedOilPitOuterRadius =
        GeneratedOilSurfaceRadius + GeneratedOilPitOuterMargin;
    // Only oil-bearing chunks use the denser grid. This lets the excavated rim
    // follow the liquid outline while keeping ordinary streaming chunks cheap.
    private const int GeneratedOilChunkSurfaceSubdivisions = 8;
    private const float GeneratedWaterWallVerticalOverlap = 0.018f;
    private const int GeneratedWaterDepthSearchRadius = 4;
    private const float GeneratedWaterDepthDeepDistance = 2.65f;
    private const int GeneratedWaterFoamRenderQueue = 3010;

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
    private static readonly ProfilerMarker EvaluateChunkEntityMarker = new ProfilerMarker("TerrainGenerator.EvaluateChunkEntity");
    private static readonly ProfilerMarker GenerateChunkEntityMarker = new ProfilerMarker("TerrainGenerator.GenerateChunkEntity");
    private static readonly ProfilerMarker SpawnChunkResourceMarker = new ProfilerMarker("TerrainGenerator.SpawnChunkResource");
    private static readonly ProfilerMarker RestoreChunkBlockStateMarker = new ProfilerMarker("TerrainGenerator.RestoreChunkBlockState");
    private static readonly ProfilerMarker SpawnChunkAnimalStepMarker = new ProfilerMarker("TerrainGenerator.SpawnChunkAnimalStep");
    private static readonly ProfilerMarker FinalizeChunkRuntimeViewMarker = new ProfilerMarker("TerrainGenerator.FinalizeChunkRuntimeView");
    private static readonly ProfilerMarker RestoreChunkConveyorItemsMarker = new ProfilerMarker("TerrainGenerator.RestoreChunkConveyorItems");
    private static readonly ProfilerMarker ReleaseEmptyChunkEntityMarker = new ProfilerMarker("TerrainGenerator.ReleaseEmptyChunkEntity");
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
    public long SimulationId => long.MinValue;
    public float ManagedUpdateTickIntervalSeconds => MapObjectTickManager.FixedSimulationDeltaSeconds;
    public int TerrainGenerationVersion => terrainGenerationVersion;
    public int MapMarkerVersion => resourceStateStore != null ? resourceStateStore.MapMarkerVersion : 0;
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

    public static float GetGeneratedOilSurfaceRadius(float angle)
    {
        return GeneratedOilSurfaceRadius
               + (Mathf.Sin((angle * 2f) + 0.7f) * 0.018f)
               + (Mathf.Sin((angle * 3f) - 1.1f) * 0.012f)
               + (Mathf.Sin((angle * 5f) + 0.35f) * 0.008f)
               + (Mathf.Sin((angle * 7f) - 0.4f) * 0.005f);
    }

    private static int GetGeneratedOilSurfaceYawStep(int seedValue, Vector2Int coordinate)
    {
        return Mathf.Clamp(
            Mathf.FloorToInt(
                Hash01WithSeed(seedValue, coordinate.x, coordinate.y, GeneratedOilSurfaceYawSalt)
                * GeneratedOilSurfaceYawStepCount),
            0,
            GeneratedOilSurfaceYawStepCount - 1);
    }

    private static float GetGeneratedOilSurfaceRotationRadians(int seedValue, Vector2Int coordinate)
    {
        return GetGeneratedOilSurfaceYawStep(seedValue, coordinate)
               * (Mathf.PI * 2f / GeneratedOilSurfaceYawStepCount);
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
        public BlockDataStore.EntityCache[] entityCache = Array.Empty<BlockDataStore.EntityCache>();
        public int[] frontLaneIndices = Array.Empty<int>();
        public int[] backLaneIndices = Array.Empty<int>();
        public float[] withinPathLengths = Array.Empty<float>();
        public float[] nextPathLengths = Array.Empty<float>();
        public List<ProjectF.Conveyors.ConveyorTransportRun> transportRuns;
        public List<int> transportLegacySlots;
        public float transportRetryTime;

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

        public BlockTemplate normal;

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

    [Header("Editor Preview")]
    [SerializeField]
    private bool expandEditorPreviewRange = false;

    [Header("Chunk Streaming")]
    [SerializeField, Min(1)]
    private int chunkGenerationBlocksPerFrame = 16;

    [SerializeField, Min(1)]
    private int chunkInstallationRestoresPerFrame = 1;

    [SerializeField, Min(0.25f)]
    private float chunkGenerationFrameTimeBudgetMilliseconds = 3f;
    [SerializeField, Min(0.25f)]
    private float initialWorldLoadFrameBudgetMilliseconds = 8f;

    [Header("Chunk Streaming Diagnostics")]
    [Tooltip("Measures per-stage managed allocations and active time for each generated chunk.")]
    [SerializeField]
    private bool enableChunkGenerationDiagnostics;

    [Tooltip("Writes one detailed log after each measured chunk. Leave disabled for allocation-neutral captures.")]
    [SerializeField]
    private bool logChunkGenerationDiagnostics;

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
    private int suppressedBlockEntityCreationDepth;
    private readonly List<Vector2Int> chunksToGenerateScratch = new List<Vector2Int>();
    private ChunkSurfaceBuildData reusableChunkSurfaceBuildData;
    private ChunkSurfaceWorkerInput reusableChunkSurfaceWorkerInput;
    private readonly List<Block> generatedChunkBlockScratch = new List<Block>(64);
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
    private readonly HashSet<BlockHandle> persistenceDirtyBlocks = new HashSet<BlockHandle>();
    private readonly List<BlockHandle> persistenceDirtyBlockScratch = new List<BlockHandle>(256);
    private bool persistenceDirtyTrackingReady;
    private readonly HashSet<InstallationObject> persistenceDirtyInstallations =
        new HashSet<InstallationObject>();
    private readonly List<InstallationObject> persistenceDirtyInstallationScratch =
        new List<InstallationObject>(256);
    private bool persistenceInstallationDirtyTrackingReady;
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
    private readonly Stack<List<BlockHandle>> conveyorCornerGroupWakeBlockPool = new Stack<List<BlockHandle>>();
    private readonly HashSet<BlockHandle> conveyorCornerGroupWakeQueuedBlocks = new HashSet<BlockHandle>();
    private readonly List<ConveyorCornerGroup> conveyorCornerGroups = new List<ConveyorCornerGroup>();
    private readonly Dictionary<int, ConveyorCornerGroup> conveyorCornerGroupsById = new Dictionary<int, ConveyorCornerGroup>();
    private readonly Dictionary<BlockHandle, ConveyorCornerGroupSlot> conveyorCornerGroupSlots = new Dictionary<BlockHandle, ConveyorCornerGroupSlot>();
    private readonly HashSet<BlockHandle> conveyorCornerGroupVisited = new HashSet<BlockHandle>();
    private readonly Dictionary<BlockHandle, int> conveyorCornerGroupBuildIndices = new Dictionary<BlockHandle, int>();
    private readonly List<BlockHandle> conveyorCornerGroupTickBlocks = new List<BlockHandle>();
    private readonly HashSet<BlockHandle> deferredConveyorRuntimeRefreshBlocks = new HashSet<BlockHandle>();
    private readonly Dictionary<BlockHandle, bool> deferredConveyorNetworkWakeBlocks = new Dictionary<BlockHandle, bool>();
    private readonly List<KeyValuePair<BlockHandle, bool>> deferredConveyorNetworkWakeBuffer = new List<KeyValuePair<BlockHandle, bool>>();
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
    private bool deferConveyorItemRestoreUntilBeltTopologyReady;
    private readonly ProjectF.Simulation.WorldRestoreProgress worldRestore = new ProjectF.Simulation.WorldRestoreProgress();
    private bool worldReadyForPresentation => worldRestore.IsReady;
    private bool pendingWorldFinalization => worldRestore.IsPending;
    private bool pendingSavedWorldFinalization;
    private MapSaveData pendingWorldMapSaveData;
    private Action pendingWorldReadyCallback;
    private IEnumerator worldFinalizationRoutine;
    private int worldFinalizationAdvancedFrame = -1;
    private bool worldPoleTopologyBatchActive;
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
    private int lastActiveConveyorDeferredNetworkWakeSuppressed;
    private int lastActiveConveyorDirectWakeInactiveSkips;
    private float nextConveyorLineRetryTime = float.PositiveInfinity;
    private float nextConveyorActiveFullScanTime;
    private int activeConveyorSafetyScanIndex;
    private Vector2Int currentCenterChunk;
    private BlockStateStore resourceStateStore;
    private InstallationPlacementController installationRestoreController;
    private InstallationObjectPool installationObjectPool;
    private PortableItemRenderer portableItemRenderer;
    private VirtualConveyorBeltRenderer virtualConveyorBeltRenderer;
    private ConveyorWorld conveyorWorld;
    private PipeWorld pipeWorld;
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
        EnsureConveyorWorld();
        EnsurePipeWorld();
        EnsureVirtualConveyorBeltRenderer();
    }

    private void OnEnable()
    {
        Active = this;
        if (Application.isPlaying)
        {
            MapObjectTickManager.RegisterUpdateTick(this);
            ProjectF.Rendering.TerrainWorldRenderer.EnsureFor(this);
        }
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
        EnsureConveyorWorld();
        EnsurePipeWorld();
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
        using var callerSample = MapObjectTickProfiler.SampleUpdateCaller<TerrainGenerator>();
        using var sample = MapObjectTickProfiler.SampleNamed("World", "Terrain Update", "Terrain Update (inclusive)");
        if (!Application.isPlaying || !hasGeneratedChunks)
        {
            return;
        }

        TryFinalizePendingWorldLoad();
        if (!worldReadyForPresentation)
        {
            return;
        }

        // Save capture yields across frames. Keep chunk membership stable while UI and
        // rendering continue, otherwise player movement could invalidate map iterators.
        if (MapObjectTickManager.SaveSnapshotCapturePaused)
        {
            return;
        }

        EnsureBeltSplitGroups();

        if (ShouldRefreshTrackedChunks())
        {
            using (RefreshChunksMarker.Auto())
            {
                RefreshTrackedChunks();
            }
        }
    }

    private bool managedUpdateTickPlanned;

    public void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }

    public void PlanManagedUpdateTick(float deltaTime)
    {
        managedUpdateTickPlanned = false;
        if (!Application.isPlaying || !hasGeneratedChunks || !worldReadyForPresentation)
        {
            return;
        }

        managedUpdateTickPlanned = true;
        ScheduleFluidSimulationShadow();
        PlanManagedBeltSimulation();
    }

    public void ApplyManagedUpdateTick()
    {
        if (!managedUpdateTickPlanned)
        {
            CompleteBeltSimulationStep();
            CompleteFluidSimulationShadow();
            return;
        }

        managedUpdateTickPlanned = false;
        try
        {
            TickFarmlandFertilizerAbsorption();
            bool profileBeltTicks = RefreshBeltTickProfilerFrameState();
            long beltJobsStart = profileBeltTicks ? MapObjectTickProfiler.BeginSample() : 0L;
            CompleteBeltSimulationStep();
            if (profileBeltTicks)
            {
                MapObjectTickProfiler.EndNamedSample(
                    "Belt",
                    "BeltJobs",
                    "Belt Jobs Tick",
                    beltJobsStart);
            }
        }
        finally
        {
            CompleteFluidSimulationShadow();
        }
    }

    internal void RenderWorldVisuals()
    {
        if (!Application.isPlaying || !hasGeneratedChunks)
            return;

        bool profileBeltTicks = MapObjectTickProfiler.IsDetailedEnabled;
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

        DrawBeltPipeSplitGroups();

        using (RenderChunkSurfacesMarker.Auto())
        using (MapObjectTickProfiler.SampleNamed("Render", "Terrain Surfaces", "Terrain Surfaces"))
        {
            RenderLoadedChunkSurfaces();
        }
    }

    private bool RefreshBeltTickProfilerFrameState()
    {
        bool profileBeltTicks = MapObjectTickProfiler.IsDetailedEnabled;
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

    private bool HasExceededChunkGenerationFrameBudget(double stepStartTime)
    {
        if (pendingWorldFinalization) return !EnsureChunkStreamingScheduler().HasFrameBudget;
        return (Time.realtimeSinceStartupAsDouble - stepStartTime) * 1000.0
               >= Mathf.Max(0.25f, chunkGenerationFrameTimeBudgetMilliseconds);
    }

    private void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting)
        {
            if (Active == this) Active = null;
            return;
        }

        if (worldRestore.IsPending)
            FailWorldRestoration(new OperationCanceledException("World restoration was interrupted by terrain host disable."));
        MapObjectTickManager.UnregisterUpdateTick(this);
#if UNITY_EDITOR
        SceneView.duringSceneGui -= RenderEditorChunkSurfaces;
#endif
        if (Active == this)
        {
            Active = null;
        }

        ClearConveyorRuntimeState();
        ResetAuthoritativeConveyorItemTotal();
        ClearPendingChunkGenerations();
    }

    private void OnDestroy()
    {
        if (RobotArmWorld.Current?.Terrain == this) RobotArmWorld.Current.Dispose();
        if (PipeWorld.Current?.Owner == this) PipeWorld.Current.Dispose();
        if (ConveyorWorld.Current?.Owner == this) ConveyorWorld.Current.Dispose();
        DisposeActiveSurfaceBuildJob();
        CleanupChunkGenerationTransientState();
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            ReleaseChunkSurfaceMeshes(pair.Value);
        }

        loadedChunks.Clear();
    }

    // Native simulation always publishes data slots to the instanced item renderer.
    public bool VirtualizeConveyorItems => true;
    public bool VirtualizeConveyorBelts => true;
    public bool IsWorldReadyForPresentation => worldReadyForPresentation;
    public bool IsWorldRestorePending => worldRestore.IsPending;
    public ProjectF.Simulation.WorldRestorePhase WorldLoadingPhase => worldRestore.Phase;
    public string WorldLoadingError => worldRestore.Failure?.Message;
    public float WorldLoadingProgress => worldReadyForPresentation
        ? 1f
        : hasGeneratedChunks && chunkStreamingScheduler != null
            ? chunkStreamingScheduler.GenerationProgress * 0.95f
            : 0f;
    public int WorldLoadingCompletedChunks =>
        chunkStreamingScheduler?.CompletedGenerationCount ?? 0;
    public int WorldLoadingTotalChunks =>
        chunkStreamingScheduler?.TotalGenerationCount ?? 0;
    public int ConveyorItemVisualBlockSetVersion => conveyorItemVisualBlockSetVersion;
    public int DynamicConveyorItemVisualBlockSetVersion => dynamicConveyorItemVisualBlockSetVersion;
    public int ConveyorItemVisualDirtyBlockCount => conveyorItemVisualDirtyBlocks.Count;

    public bool CancelWorldRestorationForLoadReplacement()
    {
        if (!worldRestore.IsPending)
        {
            return false;
        }

        ClearPendingChunkGenerations();
        FailWorldRestoration(
            new OperationCanceledException("World restoration was replaced by a newer slot load."));
        return true;
    }

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
            return cachedLoadedConveyorItemCount + CountOwnedConveyorItems();
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

        RebuildAuthoritativeConveyorItemTotal();
    }

    private void RebuildAuthoritativeConveyorItemTotal()
    {
        authoritativeConveyorItemTotal = CalculateConveyorItemCountSnapshot();
        authoritativeConveyorItemTotalInitialized = true;
    }

    private IEnumerator RebuildAuthoritativeConveyorItemTotalIncremental(int entriesPerCheckpoint)
    {
        EnsureResourceStateStore();
        int count = resourceStateStore != null
            ? resourceStateStore.GetSavedConveyorItemCount()
            : 0;
        entriesPerCheckpoint = Mathf.Max(1, entriesPerCheckpoint);
        int processed = 0;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            if (pair.Value != null)
            {
                count += CaptureLoadedConveyorItemCountContribution(pair.Key, pair.Value);
            }

            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }
        }

        authoritativeConveyorItemTotal = Mathf.Max(0, count);
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
            EnsureTrainStationIdentityAssigned(trainStation);
        }

        resourceStateStore.RegisterLiveInstallation(installationObject);
        persistenceDirtyInstallations.Remove(installationObject);
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
                && ReferenceEquals(previousBlock.MapObject, installationObject))
            {
                previousBlock.SetMapObject(null);
            }

            if (TryGetLoadedBlock(currentAnchorCoordinate, out Block currentBlock)
                && currentBlock != null
                && (currentBlock.MapObject == null || ReferenceEquals(currentBlock.MapObject, installationObject)))
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
        else if (resourceStateStore.UpdateLiveInstallationWorldPose(installationObject))
        {
            persistenceDirtyInstallations.Remove(installationObject);
        }
        else
        {
            SaveRuntimeInstallationState(installationObject);
        }
    }

    public TerrainSaveData CaptureTerrainSaveState()
    {
        // Keep explored history even when an empty saved chunk has no resident view.
        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            if (pair.Value != null)
            {
                savedExploredChunkCoordinates.Add(pair.Key);
            }
        }

        List<Vector2Int> activeChunkCoordinates = new List<Vector2Int>(savedExploredChunkCoordinates);
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
        MapSaveData mapSaveData = new MapSaveData();
        IEnumerator capture = CaptureMapSaveStateIncremental(mapSaveData, int.MaxValue);
        try { while (capture.MoveNext()) { } }
        finally { (capture as IDisposable)?.Dispose(); }
        return mapSaveData;
    }

    public IEnumerator CaptureMapSaveStateIncremental(
        MapSaveData mapSaveData,
        int entriesPerFrame = 512)
    {
        if (mapSaveData == null) yield break;

        EnsureResourceStateStore();
        if (resourceStateStore != null)
        {
            // Tick remains paused for the whole iterator, but yielding lets rendering,
            // loading UI and input frames continue between detached DTO batches.
            IEnumerator flushRoutine = SlotSaveTimingLog.TrackStage(
                "map-flush-live",
                FlushLoadedRuntimeStateToStoreIncremental(entriesPerFrame));
            using (flushRoutine as IDisposable)
            {
                while (flushRoutine.MoveNext()) yield return flushRoutine.Current;
            }

            IEnumerator storeCapture = SlotSaveTimingLog.TrackStage(
                "map-copy-store",
                resourceStateStore.CaptureSaveStateIncremental(
                    mapSaveData,
                    entriesPerFrame));
            using (storeCapture as IDisposable)
            {
                while (storeCapture.MoveNext()) yield return storeCapture.Current;
            }

            // Flush already saved the occupied belt lanes (including native checkpoints)
            // through SaveLoadedBlockFloorObjects. Store capture owns the detached copy.
        }

        IEnumerator animalCapture = SlotSaveTimingLog.TrackStage(
            "map-animals",
            CaptureAnimalSaveStatesIncremental(
                mapSaveData,
                entriesPerFrame));
        using (animalCapture as IDisposable)
        {
            while (animalCapture.MoveNext()) yield return animalCapture.Current;
        }
        IEnumerator farmlandCapture = SlotSaveTimingLog.TrackStage(
            "map-farmland",
            CaptureFarmlandSaveStateIncremental(
                mapSaveData,
                entriesPerFrame));
        using (farmlandCapture as IDisposable)
        {
            while (farmlandCapture.MoveNext()) yield return farmlandCapture.Current;
        }
        IEnumerator runCapture = SlotSaveTimingLog.TrackStage(
            "map-conveyor-runs",
            CaptureConveyorItemSaveRunsIncremental(
                mapSaveData,
                entriesPerFrame));
        using (runCapture as IDisposable)
        {
            while (runCapture.MoveNext()) yield return runCapture.Current;
        }
        IEnumerator stripConveyors = SlotSaveTimingLog.TrackStage(
            "map-strip-floor-conveyors",
            SaveGameConveyorItemBackfill.StripConveyorItemsFromFloorObjectsIncremental(
                mapSaveData,
                entriesPerFrame));
        using (stripConveyors as IDisposable)
        {
            while (stripConveyors.MoveNext()) yield return stripConveyors.Current;
        }
        IEnumerator terrainCloneCapture = SlotSaveTimingLog.TrackStage(
            "map-profile-clones",
            CaptureProfilingCloneTerrainRegionsIncremental(mapSaveData, entriesPerFrame));
        using (terrainCloneCapture as IDisposable)
        {
            while (terrainCloneCapture.MoveNext()) yield return terrainCloneCapture.Current;
        }
    }

    public void LoadFromSaveState(
        TerrainSaveData terrainSaveData,
        MapSaveData mapSaveData,
        Action onWorldReady = null)
    {
        BeginWorldFinalization(true, mapSaveData, onWorldReady);
        try { LoadSavedWorldRecordsAndChunks(terrainSaveData, mapSaveData); }
        catch (Exception exception) { FailWorldRestoration(exception); throw; }
    }

    private void LoadSavedWorldRecordsAndChunks(TerrainSaveData terrainSaveData, MapSaveData mapSaveData)
    {
        long recordsStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        RestoreProfilingCloneTerrainRegions(mapSaveData);
        NormalizeTerrainBoundsSettings();
        NormalizeResourceGenerationSettings();
        NormalizeAnimalGenerationSettings();
        EnsureResourceStateStore();
        EnsurePortableItemRenderer();
        EnsureConveyorWorld();
        EnsurePipeWorld();
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
        resourceStateStore?.ApplySaveState(mapSaveData);
        SlotLoadTimingLog.RecordStageWork(
            "records-apply",
            (System.Diagnostics.Stopwatch.GetTimestamp() - recordsStartedAt)
            * (1000d / System.Diagnostics.Stopwatch.Frequency));

        worldRestore.RecordsRestored();
        deferConveyorItemRestoreUntilBeltTopologyReady = true;
        currentCenterChunk = GetCenterChunkCoordinate();
        hasGeneratedChunks = true;
        if (!QueueSavedActiveChunks(terrainSaveData?.activeChunkCoordinates, mapSaveData))
        {
            RefreshChunks(currentCenterChunk, true);
        }
        else
        {
            EnsureChunkGenerationProcessing();
        }

        if (!Application.isPlaying)
        {
            ProcessQueuedChunkGenerationsImmediate();
            TryFinalizePendingWorldLoad();
        }
    }

    private readonly HashSet<Vector2Int> savedExploredChunkCoordinates = new HashSet<Vector2Int>();

    private bool QueueSavedActiveChunks(IReadOnlyList<Vector2Int> activeChunkCoordinates, MapSaveData mapSaveData)
    {
        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        if (activeChunkCoordinates != null)
        {
            for (int i = 0; i < activeChunkCoordinates.Count; i++)
                if (DoesChunkIntersectMapBounds(activeChunkCoordinates[i], normalizedChunkSize))
                    savedExploredChunkCoordinates.Add(activeChunkCoordinates[i]);
        }

        GetMapChunkRange(normalizedChunkSize, out Vector2Int minimumChunk, out Vector2Int maximumChunk);
        var plan = new ProjectF.Persistence.SavedWorldChunkPlan(normalizedChunkSize, minimumChunk, maximumChunk);
        List<Vector2Int> initialChunks = plan.Build(
            mapSaveData, currentCenterChunk, GetEffectiveLoadRadius(),
            itemId => ResolveItemDefinition(itemId)?.sprinklerRangeRadius ?? 0);
        EnsureChunkActivationStorageCapacity(initialChunks.Count);
        for (int i = 0; i < initialChunks.Count; i++)
        {
            QueueChunkGeneration(initialChunks[i], normalizedChunkSize);
        }

        Debug.Log($"[TerrainGenerator] Load chunks: explored={savedExploredChunkCoordinates.Count} "
            + $"initial={initialChunks.Count} installations={mapSaveData?.installations?.Count ?? 0}");
        return initialChunks.Count > 0;
    }

    public void StartNewGeneratedMap(bool randomizeSeed)
    {
        if (randomizeSeed)
        {
            RandomizeSeed();
        }

        Generate();
    }

    private void BeginWorldFinalization(
        bool restoreSavedWorld,
        MapSaveData mapSaveData,
        Action onWorldReady)
    {
        DisposeWorldFinalizationRoutine();
        EndWorldPoleTopologyBatch(false);
        persistenceDirtyBlocks.Clear();
        persistenceDirtyBlockScratch.Clear();
        persistenceDirtyTrackingReady = false;
        persistenceDirtyInstallations.Clear();
        persistenceDirtyInstallationScratch.Clear();
        persistenceInstallationDirtyTrackingReady = false;
        deferConveyorItemRestoreUntilBeltTopologyReady = false;
        worldRestore.Begin();
        UtilityPole.BeginTopologyRefreshBatch();
        worldPoleTopologyBatchActive = true;
        pendingSavedWorldFinalization = restoreSavedWorld;
        pendingWorldMapSaveData = mapSaveData;
        pendingWorldReadyCallback = onWorldReady;
    }

    private void TryFinalizePendingWorldLoad()
    {
        if (worldRestore.Phase == ProjectF.Simulation.WorldRestorePhase.Chunks)
        {
            if (IsChunkStreamingBusy)
            {
                return;
            }

            if (loadedChunks.Count <= 0)
            {
                var exception = new InvalidOperationException("World restoration produced no chunks.");
                FailWorldRestoration(exception);
                throw exception;
            }

            worldRestore.BeginConnections();
            worldFinalizationRoutine = FinalizePendingWorldLoadIncremental();
        }

        if (worldRestore.Phase != ProjectF.Simulation.WorldRestorePhase.Connections
            || worldFinalizationRoutine == null
            || (Application.isPlaying && worldFinalizationAdvancedFrame == Time.frameCount))
        {
            return;
        }

        worldFinalizationAdvancedFrame = Time.frameCount;
        double sliceStartedAt = Time.realtimeSinceStartupAsDouble;
        try
        {
            bool hasNext;
            do
            {
                hasNext = worldFinalizationRoutine.MoveNext();
                if (hasNext && worldFinalizationRoutine.Current != null)
                {
                    throw new InvalidOperationException(
                        "World finalization work may only yield null checkpoints.");
                }
            }
            while (hasNext
                   && (!Application.isPlaying
                       || (Time.realtimeSinceStartupAsDouble - sliceStartedAt) * 1000d
                       < Mathf.Max(0.25f, initialWorldLoadFrameBudgetMilliseconds)));

            SlotLoadTimingLog.RecordStageWork(
                "finalize-total",
                (Time.realtimeSinceStartupAsDouble - sliceStartedAt) * 1000d);
            if (hasNext)
            {
                return;
            }

            IEnumerator completedRoutine = worldFinalizationRoutine;
            worldFinalizationRoutine = null;
            (completedRoutine as IDisposable)?.Dispose();
            Action readyCallback = pendingWorldReadyCallback;
            ResetPersistenceDirtyTracking();
            long checkpointStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            worldRestore.Complete(readyCallback);
            SlotLoadTimingLog.RecordStageWork(
                "finalize-checkpoint",
                (System.Diagnostics.Stopwatch.GetTimestamp() - checkpointStartedAt)
                * (1000d / System.Diagnostics.Stopwatch.Frequency));
            ClearWorldRestoreReferences();
        }
        catch (Exception exception)
        {
            FailWorldRestoration(exception);
            throw;
        }
    }

    private IEnumerator FinalizePendingWorldLoadIncremental()
    {
        long powerTopologyStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        EndWorldPoleTopologyBatch(true);
        SlotLoadTimingLog.RecordStageWork(
            "finalize-power-topology",
            (System.Diagnostics.Stopwatch.GetTimestamp() - powerTopologyStartedAt)
            * (1000d / System.Diagnostics.Stopwatch.Frequency));

        if (pendingSavedWorldFinalization)
        {
            IEnumerator beltViews = SlotLoadTimingLog.TrackStage(
                "finalize-belts",
                RefreshLoadedConveyorBeltRuntimeViewsIncremental(
                    WorldFinalizationEntriesPerCheckpoint));
            using (beltViews as IDisposable)
            {
                while (beltViews.MoveNext()) yield return beltViews.Current;
            }

            IEnumerator pipeViews = SlotLoadTimingLog.TrackStage(
                "finalize-pipes",
                RefreshLoadedPipeRuntimeViewsIncremental(
                    WorldFinalizationEntriesPerCheckpoint));
            using (pipeViews as IDisposable)
            {
                while (pipeViews.MoveNext()) yield return pipeViews.Current;
            }

            IEnumerator expandedItems = SlotLoadTimingLog.TrackStage(
                "finalize-expand-items",
                ExpandConveyorItemSaveRunsAfterBeltTopologyIncremental(
                    pendingWorldMapSaveData,
                    WorldFinalizationEntriesPerCheckpoint));
            using (expandedItems as IDisposable)
            {
                while (expandedItems.MoveNext()) yield return expandedItems.Current;
            }

            IEnumerator appliedItems = SlotLoadTimingLog.TrackStage(
                "finalize-apply-items",
                ApplyLoadedConveyorItemSaveStatesIncremental(
                    pendingWorldMapSaveData,
                    WorldFinalizationEntriesPerCheckpoint));
            using (appliedItems as IDisposable)
            {
                while (appliedItems.MoveNext()) yield return appliedItems.Current;
            }
        }

        IEnumerator registrations = SlotLoadTimingLog.TrackStage(
            "finalize-registrations",
            RefreshLoadedRuntimeRegistrationsIncremental(
                WorldFinalizationEntriesPerCheckpoint));
        using (registrations as IDisposable)
        {
            while (registrations.MoveNext()) yield return registrations.Current;
        }

        IEnumerator visibility = SlotLoadTimingLog.TrackStage(
            "finalize-visibility",
            RefreshLoadedRuntimeVisibilityIncremental());
        using (visibility as IDisposable)
        {
            while (visibility.MoveNext()) yield return visibility.Current;
        }

        if (pendingSavedWorldFinalization)
        {
            IEnumerator itemCount = SlotLoadTimingLog.TrackStage(
                "finalize-item-count",
                RebuildAuthoritativeConveyorItemTotalIncremental(
                    WorldFinalizationEntriesPerCheckpoint));
            using (itemCount as IDisposable)
            {
                while (itemCount.MoveNext()) yield return itemCount.Current;
            }
        }

        long presentationStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        pipeWorld?.SynchronizeForWorldPresentation();
        conveyorWorld?.SynchronizeForWorldPresentation();
        GameManager.Instance?.StaticMapObjectRenderer?.SynchronizeForWorldPresentation();
        GameManager.Instance?.VirtualItemRenderer?.SynchronizeForWorldPresentation();
        SlotLoadTimingLog.RecordStageWork(
            "finalize-presentation",
            (System.Diagnostics.Stopwatch.GetTimestamp() - presentationStartedAt)
            * (1000d / System.Diagnostics.Stopwatch.Frequency));
    }

    private void FailWorldRestoration(Exception exception)
    {
        EndWorldPoleTopologyBatch(false);
        worldRestore.Fail(exception);
        ClearWorldRestoreReferences();
    }

    private void EndWorldPoleTopologyBatch(bool rebuildDirtyTopology)
    {
        if (!worldPoleTopologyBatchActive)
        {
            return;
        }

        worldPoleTopologyBatchActive = false;
        UtilityPole.EndTopologyRefreshBatch(rebuildDirtyTopology);
    }

    private void ClearWorldRestoreReferences()
    {
        DisposeWorldFinalizationRoutine();
        deferConveyorItemRestoreUntilBeltTopologyReady = false;
        pendingSavedWorldFinalization = false;
        pendingWorldMapSaveData = null;
        pendingWorldReadyCallback = null;
    }

    private void DisposeWorldFinalizationRoutine()
    {
        IEnumerator routine = worldFinalizationRoutine;
        worldFinalizationRoutine = null;
        worldFinalizationAdvancedFrame = -1;
        (routine as IDisposable)?.Dispose();
    }

    public void FlushLoadedRuntimeStateToStore()
    {
        IEnumerator flush = FlushLoadedRuntimeStateToStoreIncremental(int.MaxValue);
        while (flush.MoveNext()) { }
    }

    private IEnumerator FlushLoadedRuntimeStateToStoreIncremental(int entriesPerFrame)
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null) yield break;

        entriesPerFrame = Mathf.Max(1, entriesPerFrame);
        HashSet<InstallationObject> savedInstallations = new HashSet<InstallationObject>();
        IEnumerator blockCapture = SlotSaveTimingLog.TrackStage(
            "map-flush-blocks",
            FlushDirtyBlockRuntimeStateToStoreIncremental(
                savedInstallations,
                entriesPerFrame));
        using (blockCapture as IDisposable)
        {
            while (blockCapture.MoveNext()) yield return blockCapture.Current;
        }

        IEnumerator resourceCapture = SlotSaveTimingLog.TrackStage(
            "map-flush-resources",
            SaveAllRuntimeResourcesToStoreIncremental(entriesPerFrame));
        using (resourceCapture as IDisposable)
        {
            while (resourceCapture.MoveNext()) yield return resourceCapture.Current;
        }

        IEnumerator installationCapture = SlotSaveTimingLog.TrackStage(
            "map-flush-installations",
            FlushActiveInstallationRuntimeStateToStoreIncremental(
                savedInstallations,
                entriesPerFrame));
        using (installationCapture as IDisposable)
        {
            while (installationCapture.MoveNext()) yield return installationCapture.Current;
        }
    }

    private IEnumerator FlushDirtyBlockRuntimeStateToStoreIncremental(
        HashSet<InstallationObject> savedInstallations,
        int entriesPerFrame)
    {
        int processed = 0;
        if (!persistenceDirtyTrackingReady)
        {
            // This fallback is used before the first complete world checkpoint.
            // Enable tracking before scanning so a later mutation is not erased.
            persistenceDirtyBlocks.Clear();
            persistenceDirtyTrackingReady = true;
            bool completedFullScan = false;
            try
            {
                foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
                {
                    SaveLoadedBlockRuntimeState(pair.Value, savedInstallations);
                    if (++processed >= entriesPerFrame)
                    {
                        processed = 0;
                        yield return null;
                    }
                }
                completedFullScan = true;
            }
            finally
            {
                // An interrupted fallback must rescan next time because it has no
                // complete dirty baseline yet.
                if (!completedFullScan)
                {
                    persistenceDirtyTrackingReady = false;
                }
            }
            yield break;
        }

        persistenceDirtyBlockScratch.Clear();
        persistenceDirtyBlockScratch.AddRange(persistenceDirtyBlocks);
        for (int i = 0; i < persistenceDirtyBlockScratch.Count; i++)
        {
            BlockHandle handle = persistenceDirtyBlockScratch[i];
            // Remove before capture. A mutation occurring after this point re-adds
            // the handle and remains dirty for the next snapshot.
            persistenceDirtyBlocks.Remove(handle);
            try
            {
                if (TryResolveLoadedRuntimeBlock(handle, out Block block))
                {
                    SaveLoadedBlockRuntimeState(block, savedInstallations);
                }
            }
            catch
            {
                persistenceDirtyBlocks.Add(handle);
                throw;
            }

            if (++processed >= entriesPerFrame)
            {
                processed = 0;
                yield return null;
            }
        }
        persistenceDirtyBlockScratch.Clear();
    }

    private void SaveLoadedBlockRuntimeState(
        Block block,
        HashSet<InstallationObject> savedInstallations)
    {
        if (block == null)
        {
            return;
        }

        SaveLoadedBlockFloorObjects(block);
        if (!block.TryGetRuntimeConveyorRecord(out _)
            && !block.TryGetRuntimePipeRecord(out _)
            && block.MapObject is InstallationObject installationObject
            && !installationObject.ExcludeFromTerrainPersistence
            && savedInstallations.Add(installationObject))
        {
            resourceStateStore.RegisterLiveInstallation(installationObject);
            persistenceDirtyInstallations.Remove(installationObject);
        }
    }

    private IEnumerator FlushActiveInstallationRuntimeStateToStoreIncremental(
        HashSet<InstallationObject> savedInstallations,
        int entriesPerFrame)
    {
        int processed = 0;
        bool requiresFullScan = !persistenceInstallationDirtyTrackingReady;
        persistenceDirtyInstallationScratch.Clear();
        if (requiresFullScan)
        {
            // Before the first complete checkpoint there is no reliable dirty baseline.
            // Activate tracking before capture so mutations after an entry is read remain dirty.
            persistenceDirtyInstallations.Clear();
            persistenceInstallationDirtyTrackingReady = true;
            InstallationObject.CopyActiveInstances(persistenceDirtyInstallationScratch);
        }
        else
        {
            persistenceDirtyInstallationScratch.AddRange(persistenceDirtyInstallations);
            persistenceDirtyInstallationScratch.Sort(InstallationObject.CompareSimulationOrder);
        }

        bool completed = false;
        try
        {
            for (int i = 0; i < persistenceDirtyInstallationScratch.Count; i++)
            {
                InstallationObject installationObject = persistenceDirtyInstallationScratch[i];
                persistenceDirtyInstallations.Remove(installationObject);
                try
                {
                    if (installationObject != null
                        && !savedInstallations.Contains(installationObject)
                        && !installationObject.ExcludeFromTerrainPersistence
                        && installationObject.TryGetPlacementRuntime(out _, out _))
                    {
                        savedInstallations.Add(installationObject);
                        resourceStateStore.RegisterLiveInstallation(installationObject);
                    }
                }
                catch
                {
                    if (installationObject != null)
                    {
                        persistenceDirtyInstallations.Add(installationObject);
                    }
                    throw;
                }

                if (++processed >= entriesPerFrame)
                {
                    processed = 0;
                    yield return null;
                }
            }

            completed = true;
        }
        finally
        {
            persistenceDirtyInstallationScratch.Clear();
            if (requiresFullScan && !completed)
            {
                persistenceInstallationDirtyTrackingReady = false;
            }
        }
    }

    internal void MarkPersistenceStateDirty(Block block)
    {
        if (persistenceDirtyTrackingReady
            && block != null
            && TryGetRuntimeBlockHandle(block, out BlockHandle handle))
        {
            persistenceDirtyBlocks.Add(handle);
        }
    }

    internal void MarkPersistenceStateDirty(InstallationObject installationObject)
    {
        if (persistenceInstallationDirtyTrackingReady
            && installationObject != null
            && !installationObject.ExcludeFromTerrainPersistence)
        {
            persistenceDirtyInstallations.Add(installationObject);
        }
    }

    private void ResetPersistenceDirtyTracking()
    {
        persistenceDirtyBlocks.Clear();
        // Conveyor item entries are removed from the detached store when views are
        // restored. Capture occupied belts once even if they remain asleep forever;
        // subsequent changes arrive through MarkPersistenceStateDirty.
        persistenceDirtyBlocks.UnionWith(conveyorItemVisualBlocks);
        persistenceDirtyBlockScratch.Clear();
        persistenceDirtyTrackingReady = true;
        persistenceDirtyInstallations.Clear();
        persistenceDirtyInstallationScratch.Clear();
        persistenceInstallationDirtyTrackingReady = true;
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

        List<InstallationObject> activeInstallations = new List<InstallationObject>();
        InstallationObject.CopyActiveInstances(activeInstallations);
        for (int i = 0; i < activeInstallations.Count; i++)
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

    private IEnumerator ApplyLoadedConveyorItemSaveStatesIncremental(
        MapSaveData mapSaveData,
        int entriesPerCheckpoint)
    {
        ResetLastConveyorItemLoadStats();
        if (mapSaveData?.conveyorItems == null)
        {
            yield break;
        }

        entriesPerCheckpoint = Mathf.Max(1, entriesPerCheckpoint);
        int processed = 0;
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
            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }
        }
    }

    private void ApplyStoredConveyorItemSaveState(Block block)
    {
        if (deferConveyorItemRestoreUntilBeltTopologyReady
            || block == null
            || resourceStateStore == null)
        {
            return;
        }

        using (RestoreChunkConveyorItemsMarker.Auto())
        {
            if (resourceStateStore.TryGetConveyorItems(
                    block.Coordinate,
                    out List<ConveyorItemLaneSaveState> lanes)
                && CountConveyorItemSaveLanes(lanes) > 0)
            {
                ApplyLoadedConveyorItemSaveStatesToBlock(block.Coordinate, block, lanes, null, false);
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
            resourceStateStore?.RemoveConveyorItems(coordinate);
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
                resourceStateStore?.RemoveConveyorItems(coordinate);
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

    private IEnumerator RefreshLoadedRuntimeRegistrationsIncremental(int entriesPerCheckpoint)
    {
        MarkConveyorNetworkDirty();
        entriesPerCheckpoint = Mathf.Max(1, entriesPerCheckpoint);
        int processed = 0;
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            RefreshRestoredBlockRuntimeRegistration(pair.Value);
            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }
        }
    }

    private IEnumerator RefreshLoadedConveyorBeltRuntimeViewsIncremental(int entriesPerCheckpoint)
    {
        if (!Application.isPlaying)
        {
            yield break;
        }

        entriesPerCheckpoint = Mathf.Max(1, entriesPerCheckpoint);
        int processed = 0;
        List<ConveyorBelt> conveyorBelts = new List<ConveyorBelt>();
        HashSet<ConveyorBelt> uniqueConveyorBelts = new HashSet<ConveyorBelt>();
        EnsureResourceStateStore();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }

            Block block = pair.Value;
            if (block != null
                && !block.TryGetRuntimeConveyorRecord(out _)
                && block.MapObject is ConveyorBelt conveyorBelt
                && uniqueConveyorBelts.Add(conveyorBelt))
            {
                if (conveyorBelt.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
                    && resourceStateStore != null
                    && resourceStateStore.TryGetLiveInstallation(
                        anchorCoordinate,
                        out InstallationObject liveInstallation,
                        out BlockStateStore.InstallationSaveState liveState)
                    && ReferenceEquals(liveInstallation, conveyorBelt))
                {
                    MapObject sourcePrefab = ResolveInstallationSourcePrefab(liveState);
                    if (RegisterDataOnlyConveyorInstallation(
                            conveyorBelt,
                            sourcePrefab as ConveyorBelt))
                    {
                        ReleaseInstallationObject(conveyorBelt, sourcePrefab);
                        continue;
                    }
                }

                conveyorBelts.Add(conveyorBelt);
            }
        }

        if (conveyorBelts.Count <= 0)
        {
            yield break;
        }

        virtualConveyorBeltRenderer?.Clear();
        processed = 0;
        for (int i = 0; i < conveyorBelts.Count; i++)
        {
            conveyorBelts[i]?.RefreshEndpointVisuals();
            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }
        }

        ConvayorBelt2F.MarkCoverageDirty();
        processed = 0;
        for (int i = 0; i < conveyorBelts.Count; i++)
        {
            if (conveyorBelts[i] is ConvayorBelt2F belt2F)
            {
                belt2F.RefreshCoveredConveyorTopology();
            }
            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }
        }

        processed = 0;
        for (int i = 0; i < conveyorBelts.Count; i++)
        {
            RegisterVirtualConveyorBelt(conveyorBelts[i]);
            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }
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

    private IEnumerator RefreshLoadedRuntimeVisibilityIncremental()
    {
        RefreshBeltItemRenderingVisibility();
        yield return null;
        RefreshBeltRenderingVisibility();
        yield return null;
        RefreshConveyorSlotDotRuntimeVisibility();
        yield return null;
        RefreshBeltItemLineRuntimeVisibility();
        yield return null;
        RefreshBeltDirectionRuntimeVisibility();
        yield return null;
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

    private IEnumerator RefreshLoadedPipeRuntimeViewsIncremental(int entriesPerCheckpoint)
    {
        if (!Application.isPlaying)
        {
            yield break;
        }

        entriesPerCheckpoint = Mathf.Max(1, entriesPerCheckpoint);
        int processed = 0;
        HashSet<Pipe> uniquePipes = new HashSet<Pipe>();
        EnsureResourceStateStore();
        foreach (KeyValuePair<Vector2Int, Block> pair in loadedBlocks)
        {
            if (++processed >= entriesPerCheckpoint)
            {
                processed = 0;
                yield return null;
            }

            Block block = pair.Value;
            if (block == null
                || block.TryGetRuntimePipeRecord(out _)
                || !(block.MapObject is Pipe pipe)
                || !pipe.gameObject.scene.IsValid()
                || !uniquePipes.Add(pipe)
                || !pipe.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
                || resourceStateStore == null
                || !resourceStateStore.TryGetLiveInstallation(
                    anchorCoordinate,
                    out InstallationObject liveInstallation,
                    out BlockStateStore.InstallationSaveState liveState)
                || !ReferenceEquals(liveInstallation, pipe))
            {
                continue;
            }

            Pipe sourcePrefab = ResolveInstallationSourcePrefab(liveState) as Pipe;
            if (RegisterDataOnlyPipeInstallation(pipe, sourcePrefab))
            {
                ReleaseInstallationObject(pipe, sourcePrefab);
            }
        }
    }

    internal bool IsConveyorItemVisualBlockTracked(BlockHandle handle)
    {
        return handle.IsValid && conveyorItemVisualBlocks.Contains(handle);
    }

    internal bool IsDynamicConveyorItemVisualBlockTracked(BlockHandle handle)
    {
        return handle.IsValid && dynamicConveyorItemVisualBlockIndices.ContainsKey(handle);
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
        try
        {
            ClearProfilingCloneTerrainSources();
            BeginWorldFinalization(false, null, null);
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

            worldRestore.RecordsRestored();
            currentCenterChunk = GetCenterChunkCoordinate();
            hasGeneratedChunks = true;
            RefreshChunks(currentCenterChunk, true);
            if (!Application.isPlaying)
            {
                TryFinalizePendingWorldLoad();
            }
        }
        catch (Exception exception) { FailWorldRestoration(exception); throw; }
    }

    public void ResetChunks()
    {
        try
        {
            if (!hasGeneratedChunks)
            {
                Generate();
                return;
            }

            BeginWorldFinalization(false, null, null);
            NormalizeTerrainBoundsSettings();
            NormalizeResourceGenerationSettings();
            NormalizeAnimalGenerationSettings();
            EnsureResourceStateStore();
            InvalidateTerrainGenerationCaches();
            ClearPendingChunkGenerations();
            ClearLoadedChunks();

            worldRestore.RecordsRestored();
            currentCenterChunk = GetCenterChunkCoordinate();
            hasGeneratedChunks = true;
            RefreshChunks(currentCenterChunk, true);
            if (!Application.isPlaying)
            {
                TryFinalizePendingWorldLoad();
            }
        }
        catch (Exception exception) { FailWorldRestoration(exception); throw; }
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
        ClearProfilingCloneTerrainSources();
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

        if (chunksToGenerate.Count > 0)
        {
            EnsureChunkActivationStorageCapacity(loadedChunks.Count + chunksToGenerate.Count);
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

    private void EnsureChunkActivationStorageCapacity(int requiredChunkCount)
    {
        if (requiredChunkCount <= 0)
        {
            return;
        }

        // Grow shared stores before a generation coroutine starts. Otherwise a
        // single Add during chunk activation can rehash data accumulated by the
        // whole save and turn an unrelated chunk into a long main-thread frame.
        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        GetMapChunkRange(normalizedChunkSize, out Vector2Int minChunk, out Vector2Int maxChunk);
        long mapChunkWidth = (long)maxChunk.x - minChunk.x + 1L;
        long mapChunkHeight = (long)maxChunk.y - minChunk.y + 1L;
        long maximumChunkCount = Math.Max(1L, mapChunkWidth * mapChunkHeight);
        long roundedRequiredChunkCount =
            (((long)requiredChunkCount + ChunkCapacityGrowthHeadroom - 1L)
             / ChunkCapacityGrowthHeadroom)
            * ChunkCapacityGrowthHeadroom;
        long requestedWithHeadroom = roundedRequiredChunkCount + ChunkCapacityGrowthHeadroom;
        int chunkCapacity = (int)Math.Min(
            int.MaxValue,
            Math.Min(maximumChunkCount, requestedWithHeadroom));

        loadedChunks.EnsureCapacity(chunkCapacity);
        loadedBlocks.EnsureChunkCapacity(chunkCapacity);

        if (Application.isPlaying)
        {
            long maximumResources = (long)chunkCapacity * normalizedChunkSize * normalizedChunkSize;
            int resourceCapacity = (int)Math.Min(int.MaxValue, maximumResources);
            ResourceInstance.EnsureActiveResourceCapacity(resourceCapacity);
            ResourceBatchRenderer batchRenderer = GetComponent<ResourceBatchRenderer>();
            if (batchRenderer == null)
            {
                batchRenderer = gameObject.AddComponent<ResourceBatchRenderer>();
            }

            batchRenderer.EnsureCapacity(resourceCapacity);
        }

        int shorelineMargin = Mathf.Max(0, Mathf.Max(sandMinWidth, sandMaxWidth)) + 1;
        int coordinateMargin = GeneratedSurfaceBiomeMargin + shorelineMargin;
        long surfaceSampleWidth = (long)normalizedChunkSize + (coordinateMargin * 2L) + 1L;
        long requestedCoordinateCapacity = (long)chunkCapacity * surfaceSampleWidth * surfaceSampleWidth;
        long mapCoordinateWidth = (long)GetNormalizedMapSize() + (coordinateMargin * 2L);
        long maximumCoordinateCapacity = mapCoordinateWidth * mapCoordinateWidth;
        int coordinateCapacity = (int)Math.Min(
            int.MaxValue,
            Math.Min(maximumCoordinateCapacity, requestedCoordinateCapacity));

        tileBiomeCache.EnsureCapacity(coordinateCapacity);
        rawWaterCache.EnsureCapacity(coordinateCapacity);
        directWaterBlockCache.EnsureCapacity(coordinateCapacity);
        bufferedWaterBlockCache.EnsureCapacity(coordinateCapacity);
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
        // Initial restoration uses a shared time budget, not one frame per object/chunk.
        // Runtime streaming additionally keeps its conservative per-stage item limits.
        bool spreadMainThreadWorkAcrossFrames = allowYield;

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
            SaveAndReleaseDetachedResourcesInChunk(chunkCoordinate);
            SaveChunkResourceStates(existingBlocks);
            // Rebuilding a terrain view preserves live animal state and deterministic IDs.
            RemoveChunkBlocksFromLookup(existingBlocks);
            ReleaseChunkBlockEntities(existingBlocks);
            ReleaseChunkSurfaceMeshes(existingChunk);
            loadedChunks.Remove(chunkCoordinate);
            loadedBlocks.UnregisterChunk(chunkCoordinate);
        }

        Vector2Int origin = new Vector2Int(chunkCoordinate.x * normalizedChunkSize, chunkCoordinate.y * normalizedChunkSize);
        BeginChunkGenerationDiagnostics(chunkCoordinate);
        long diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
            out double diagnosticStageStartTime);
        loadedBlocks.RegisterChunk(chunkCoordinate);
        ChunkRuntimeData chunk = new ChunkRuntimeData(chunkCoordinate, origin);
        loadedChunks.Add(chunkCoordinate, chunk);
        List<Block> generatedChunkBlocks = generatedChunkBlockScratch;
        generatedChunkBlocks.Clear();
        long maximumBlockCount = (long)normalizedChunkSize * normalizedChunkSize;
        int requiredBlockCapacity = (int)Math.Min(maximumBlockCount, int.MaxValue);
        if (generatedChunkBlocks.Capacity < requiredBlockCapacity)
        {
            generatedChunkBlocks.Capacity = requiredBlockCapacity;
        }

        EndChunkGenerationDiagnosticStage(
            ChunkGenerationDiagnosticStage.Preparation,
            diagnosticAllocatedBytesAtStart,
            diagnosticStageStartTime);

        int blocksSinceYield = 0;
        int blockBudget = Mathf.Max(1, chunkGenerationBlocksPerFrame);
        double blockStepStartTime = Time.realtimeSinceStartupAsDouble;
        diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
            out diagnosticStageStartTime);

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
                bool requiresEntity;
                Resource generatedResourcePrefab;
                using (EvaluateChunkEntityMarker.Auto())
                {
                    requiresEntity = RequiresInitialBlockEntity(
                        worldCoordinate,
                        out generatedResourcePrefab);
                }
                if (!requiresEntity)
                {
                    if (spreadMainThreadWorkAcrossFrames
                        && ((!pendingWorldFinalization && ++blocksSinceYield >= blockBudget)
                            || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
                    {
                        blocksSinceYield = 0;
                        EndChunkGenerationDiagnosticStage(
                            ChunkGenerationDiagnosticStage.EntityGeneration,
                            diagnosticAllocatedBytesAtStart,
                            diagnosticStageStartTime);
                        yield return null;
                        blockStepStartTime = Time.realtimeSinceStartupAsDouble;
                        diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                            out diagnosticStageStartTime);
                    }

                    continue;
                }

                Block block;
                using (GenerateChunkEntityMarker.Auto())
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
                            using (SpawnChunkResourceMarker.Auto())
                            {
                                SpawnResourceOnBlock(block, generatedResourcePrefab, worldCoordinate);
                            }
                        }
                    }
                }
                if (block == null)
                {
                    continue;
                }

                if (spreadMainThreadWorkAcrossFrames
                    && ((!pendingWorldFinalization && ++blocksSinceYield >= blockBudget)
                        || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
                {
                    blocksSinceYield = 0;
                    EndChunkGenerationDiagnosticStage(
                        ChunkGenerationDiagnosticStage.EntityGeneration,
                        diagnosticAllocatedBytesAtStart,
                        diagnosticStageStartTime);
                    yield return null;
                    blockStepStartTime = Time.realtimeSinceStartupAsDouble;
                    diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                        out diagnosticStageStartTime);
                }
            }
        }

        EndChunkGenerationDiagnosticStage(
            ChunkGenerationDiagnosticStage.EntityGeneration,
            diagnosticAllocatedBytesAtStart,
            diagnosticStageStartTime);

        if (spreadMainThreadWorkAcrossFrames && !pendingWorldFinalization)
        {
            yield return null;
        }

        IReadOnlyList<Block> chunkBlocks = generatedChunkBlocks;
        diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
            out diagnosticStageStartTime);
        bool canRestoreInstallations = PrepareChunkInstallationRestore(chunkBlocks);
        EndChunkGenerationDiagnosticStage(
            ChunkGenerationDiagnosticStage.InstallationRestore,
            diagnosticAllocatedBytesAtStart,
            diagnosticStageStartTime);
        if (canRestoreInstallations)
        {
            int restoresSinceYield = 0;
            int restoreBudget = Mathf.Max(1, chunkInstallationRestoresPerFrame);
            double restoreStepStartTime = Time.realtimeSinceStartupAsDouble;
            BeginConveyorRuntimeRefreshBatch();
            try
            {
                for (int i = 0; i < orderedChunkInstallationAnchorScratch.Count; i++)
                {
                    diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                        out diagnosticStageStartTime);
                    RestoreOrBindSavedInstallation(orderedChunkInstallationAnchorScratch[i]);
                    EndChunkGenerationDiagnosticStage(
                        ChunkGenerationDiagnosticStage.InstallationRestore,
                        diagnosticAllocatedBytesAtStart,
                        diagnosticStageStartTime);
                    restoresSinceYield++;
                    if (spreadMainThreadWorkAcrossFrames
                        && ((!pendingWorldFinalization && restoresSinceYield >= restoreBudget)
                            || HasExceededChunkGenerationFrameBudget(restoreStepStartTime)))
                    {
                        restoresSinceYield = 0;
                        yield return null;
                        restoreStepStartTime = Time.realtimeSinceStartupAsDouble;
                    }
                }

                diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                    out diagnosticStageStartTime);
                InstallationPlacementController placementController =
                    ResolveInstallationPlacementController();
                placementController?.NormalizeLoadedFenceVariants(orderedChunkInstallationAnchorScratch);
                placementController?.NormalizeLoadedLegacyPipeVariants(orderedChunkInstallationAnchorScratch);
                EndChunkGenerationDiagnosticStage(
                    ChunkGenerationDiagnosticStage.InstallationRestore,
                    diagnosticAllocatedBytesAtStart,
                    diagnosticStageStartTime);
            }
            finally
            {
                EndConveyorRuntimeRefreshBatch();
                ClearChunkInstallationRestoreScratch();
            }
        }

        EnsureResourceStateStore();
        blocksSinceYield = 0;
        blockStepStartTime = Time.realtimeSinceStartupAsDouble;
        BeginConveyorRuntimeRefreshBatch();
        try
        {
            for (int i = 0; i < chunkBlocks.Count; i++)
            {
                diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                    out diagnosticStageStartTime);
                using (RestoreChunkBlockStateMarker.Auto())
                {
                    RestoreBlockState(chunkBlocks[i]);
                }

                EndChunkGenerationDiagnosticStage(
                    ChunkGenerationDiagnosticStage.BlockStateRestore,
                    diagnosticAllocatedBytesAtStart,
                    diagnosticStageStartTime);
                if (spreadMainThreadWorkAcrossFrames
                    && ((!pendingWorldFinalization && ++blocksSinceYield >= blockBudget)
                        || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
                {
                    blocksSinceYield = 0;
                    yield return null;
                    blockStepStartTime = Time.realtimeSinceStartupAsDouble;
                }
            }
        }
        finally
        {
            EndConveyorRuntimeRefreshBatch();
        }

        diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
            out diagnosticStageStartTime);
        bool animalSpawnComplete = BeginChunkAnimalSpawnWork(chunkCoordinate);
        EndChunkGenerationDiagnosticStage(
            ChunkGenerationDiagnosticStage.AnimalSpawn,
            diagnosticAllocatedBytesAtStart,
            diagnosticStageStartTime);
        while (!animalSpawnComplete)
        {
            diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                out diagnosticStageStartTime);
            using (SpawnChunkAnimalStepMarker.Auto())
            {
                animalSpawnComplete = AdvanceChunkAnimalSpawnWork(spreadMainThreadWorkAcrossFrames);
            }

            EndChunkGenerationDiagnosticStage(
                ChunkGenerationDiagnosticStage.AnimalSpawn,
                diagnosticAllocatedBytesAtStart,
                diagnosticStageStartTime);
            if (!animalSpawnComplete && spreadMainThreadWorkAcrossFrames)
            {
                yield return null;
            }
        }

        diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
            out diagnosticStageStartTime);
        PrepareChunkBlockRuntimeViews(chunkBlocks);
        EndChunkGenerationDiagnosticStage(
            ChunkGenerationDiagnosticStage.RuntimeViewRefresh,
            diagnosticAllocatedBytesAtStart,
            diagnosticStageStartTime);
        blocksSinceYield = 0;
        blockStepStartTime = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < chunkBlocks.Count; i++)
        {
            diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                out diagnosticStageStartTime);
            using (FinalizeChunkRuntimeViewMarker.Auto())
            {
                Block block = chunkBlocks[i];
                if (block != null)
                {
                    RefreshRestoredBlockRuntimeRegistration(block);
                }
            }

            EndChunkGenerationDiagnosticStage(
                ChunkGenerationDiagnosticStage.RuntimeViewRefresh,
                diagnosticAllocatedBytesAtStart,
                diagnosticStageStartTime);
            if (spreadMainThreadWorkAcrossFrames
                && ((!pendingWorldFinalization && ++blocksSinceYield >= blockBudget)
                    || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
            {
                blocksSinceYield = 0;
                yield return null;
                blockStepStartTime = Time.realtimeSinceStartupAsDouble;
            }
        }

        EnsureResourceStateStore();
        if (resourceStateStore != null)
        {
            blocksSinceYield = 0;
            blockStepStartTime = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < chunkBlocks.Count; i++)
            {
                diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                    out diagnosticStageStartTime);
                ApplyStoredConveyorItemSaveState(chunkBlocks[i]);
                EndChunkGenerationDiagnosticStage(
                    ChunkGenerationDiagnosticStage.ConveyorItemRestore,
                    diagnosticAllocatedBytesAtStart,
                    diagnosticStageStartTime);
                if (spreadMainThreadWorkAcrossFrames
                    && ((!pendingWorldFinalization && ++blocksSinceYield >= blockBudget)
                        || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
                {
                    blocksSinceYield = 0;
                    yield return null;
                    blockStepStartTime = Time.realtimeSinceStartupAsDouble;
                }
            }
        }

        blocksSinceYield = 0;
        blockStepStartTime = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < chunkBlocks.Count; i++)
        {
            diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                out diagnosticStageStartTime);
            ReleaseEmptyChunkBlockEntity(chunkBlocks[i]);
            EndChunkGenerationDiagnosticStage(
                ChunkGenerationDiagnosticStage.EmptyEntityRelease,
                diagnosticAllocatedBytesAtStart,
                diagnosticStageStartTime);
            if (spreadMainThreadWorkAcrossFrames
                && ((!pendingWorldFinalization && ++blocksSinceYield >= blockBudget)
                    || HasExceededChunkGenerationFrameBudget(blockStepStartTime)))
            {
                blocksSinceYield = 0;
                yield return null;
                blockStepStartTime = Time.realtimeSinceStartupAsDouble;
            }
        }

        diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
            out diagnosticStageStartTime);
        loadedBlocks.CompactEntityStorage(chunkCoordinate);
        EndChunkGenerationDiagnosticStage(
            ChunkGenerationDiagnosticStage.EmptyEntityRelease,
            diagnosticAllocatedBytesAtStart,
            diagnosticStageStartTime);

        ChunkSurfaceBuildData chunkSurface = null;
        Mesh generatedSurfaceMesh = null;
        Bounds generatedSurfaceBounds = default;
        int generatedSurfaceSubMeshMask = 0;
        if (Application.isPlaying)
        {
            diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                out diagnosticStageStartTime);
            TerrainSurfaceBuildJobState surfaceJob = CreateChunkSurfaceBuildJob(origin, normalizedChunkSize);
            EndChunkGenerationDiagnosticStage(
                ChunkGenerationDiagnosticStage.SurfaceBuildSchedule,
                diagnosticAllocatedBytesAtStart,
                diagnosticStageStartTime);
            while (!surfaceJob.IsCompleted)
            {
                if (!allowYield) break;
                yield return TerrainChunkStreamingScheduler.WaitForNextFrame;
            }

            try
            {
                bool scheduledMeshDataCopy = false;
                try
                {
                    diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                        out diagnosticStageStartTime);
                    CompleteChunkSurfaceBuildJob(surfaceJob);
                    EndChunkGenerationDiagnosticStage(
                        ChunkGenerationDiagnosticStage.SurfaceBuildComplete,
                        diagnosticAllocatedBytesAtStart,
                        diagnosticStageStartTime);
                    diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                        out diagnosticStageStartTime);
                    scheduledMeshDataCopy = ScheduleChunkSurfaceMeshDataJob(surfaceJob);
                    EndChunkGenerationDiagnosticStage(
                        ChunkGenerationDiagnosticStage.SurfaceMeshDataSchedule,
                        diagnosticAllocatedBytesAtStart,
                        diagnosticStageStartTime);
                }
                catch (Exception surfaceException)
                {
                    chunkSurface = BuildFallbackChunkSurface(
                        surfaceException,
                        origin,
                        normalizedChunkSize);
                }

                if (scheduledMeshDataCopy)
                {
                    while (!surfaceJob.IsMeshDataReady)
                    {
                        if (!allowYield) break;
                        yield return TerrainChunkStreamingScheduler.WaitForNextFrame;
                    }

                    try
                    {
                        diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                            out diagnosticStageStartTime);
                        generatedSurfaceMesh = CompleteChunkSurfaceMeshDataJob(
                            surfaceJob,
                            out generatedSurfaceBounds,
                            out generatedSurfaceSubMeshMask);
                        EndChunkGenerationDiagnosticStage(
                            ChunkGenerationDiagnosticStage.SurfaceMeshDataApply,
                            diagnosticAllocatedBytesAtStart,
                            diagnosticStageStartTime);
                    }
                    catch (Exception surfaceException)
                    {
                        chunkSurface = BuildFallbackChunkSurface(
                            surfaceException,
                            origin,
                            normalizedChunkSize);
                    }
                }
            }
            finally
            {
                ReleaseChunkSurfaceBuildJob(surfaceJob);
            }
        }
        else
        {
            chunkSurface = BuildCurvedChunkSurface(origin, normalizedChunkSize);
        }

        if (spreadMainThreadWorkAcrossFrames && !pendingWorldFinalization)
        {
            yield return null;
        }

        try
        {
            diagnosticAllocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(
                out diagnosticStageStartTime);
            if (generatedSurfaceMesh != null)
            {
                ApplyChunkBiomeSurface(
                    chunk,
                    generatedSurfaceMesh,
                    generatedSurfaceBounds,
                    generatedSurfaceSubMeshMask);
            }
            else
            {
                ApplyChunkBiomeSurface(chunk, chunkSurface);
            }

            EndChunkGenerationDiagnosticStage(
                ChunkGenerationDiagnosticStage.SurfaceAssignment,
                diagnosticAllocatedBytesAtStart,
                diagnosticStageStartTime);
        }
        finally
        {
            if (chunkSurface != null)
            {
                ReturnChunkSurfaceBuildData(chunkSurface);
            }
        }

        if (spreadMainThreadWorkAcrossFrames && !pendingWorldFinalization)
        {
            yield return null;
        }

        generatedChunkBlocks.Clear();
        EndChunkGenerationDiagnostics();

    }

    private ChunkSurfaceBuildData BuildFallbackChunkSurface(
        Exception surfaceException,
        Vector2Int origin,
        int normalizedChunkSize)
    {
        Debug.LogException(surfaceException, this);
        long allocatedBytesAtStart = BeginChunkGenerationDiagnosticStage(out double startTime);
        try
        {
            return BuildCurvedChunkSurface(origin, normalizedChunkSize);
        }
        finally
        {
            EndChunkGenerationDiagnosticStage(
                ChunkGenerationDiagnosticStage.SurfaceFallbackBuild,
                allocatedBytesAtStart,
                startTime);
        }
    }

    private void ClearLoadedChunks(bool preserveRuntimeState = true, bool releaseLiveInstallations = false)
    {
        if (preserveRuntimeState)
        {
            savedExploredChunkCoordinates.UnionWith(loadedChunks.Keys);
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

        SaveAndReleaseAllDetachedResources(preserveRuntimeState);
        RemoveChunkBlocksFromLookup(loadedBlockSnapshot);
        ReleaseChunkBlockEntities(loadedBlockSnapshot);
        DestroyAllTerrainAnimalViews();

        foreach (KeyValuePair<Vector2Int, ChunkRuntimeData> pair in loadedChunks)
        {
            ReleaseChunkSurfaceMeshes(pair.Value);
        }

        loadedChunks.Clear();
        if (!preserveRuntimeState) savedExploredChunkCoordinates.Clear();
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
        if (prefab == null || !prefab.TryGetComponent(out BlockTemplate template))
        {
            return null;
        }

        // A Block is a managed entity. The generation-checked handle owns its
        // lifetime and no GameObject/MonoBehaviour is created per coordinate.
        loadedBlocks.RegisterCell(coordinate, blockType, out BlockHandle handle);

        Block block = new Block();
        block.ConfigureRuntimeTemplate(template);
        block.BindRuntimeHandle(handle);
        block.Initialize(this, coordinate, blockType);
        if (loadedBlocks.BindEntity(coordinate, block, out BlockHandle boundHandle))
        {
            block.BindRuntimeHandle(boundHandle);
        }
        return block;
    }

    private bool TryMaterializeBlockEntity(Vector2Int coordinate, out Block block)
    {
        block = null;
        if (suppressedBlockEntityCreationDepth > 0
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
        if (ResourceInstance.TryGetActiveResourceAtCoordinate(this, coordinate, out ResourceInstance resource))
        {
            block.SetMapObject(resource);
        }
        return true;
    }

    private Block[] GetChunkRuntimeBlocks(Vector2Int chunkCoordinate)
    {
        loadedBlocks.CopyEntities(chunkCoordinate, chunkRuntimeBlockScratch);
        return chunkRuntimeBlockScratch.Count > 0
            ? chunkRuntimeBlockScratch.ToArray()
            : Array.Empty<Block>();
    }

    private void ReleaseEmptyChunkBlockEntity(Block block)
    {
        using (ReleaseEmptyChunkEntityMarker.Auto())
        {
            if (block == null)
            {
                return;
            }

            bool preserveResource = block.CanReleaseResourceOnlyEntity;
            if (!preserveResource && !block.CanReleaseEmptyEntity)
            {
                return;
            }

            BlockHandle handle = block.RuntimeHandle;
            loadedBlocks.Remove(block.Coordinate);
            if (!preserveResource)
            {
                ReleaseFarmlandVisual(block.Coordinate);
            }

            block.PrepareForRuntimeRelease(!preserveResource);
            if (block.HasRuntimeSimulationState)
            {
                loadedBlocks.RemoveRuntimeSimulationState(handle);
            }

            block.DetachRuntimeSimulationState();
        }
    }


}
