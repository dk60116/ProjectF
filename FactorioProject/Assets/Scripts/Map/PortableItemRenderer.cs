using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualConveyorItemRenderData
{
    public VirtualConveyorItemRenderData(
        int itemId,
        Matrix4x4 matrix,
        int layer,
        bool useSleepAwakeDarkTint,
        bool useBeltItemLineDebugColor = false,
        Color32 beltItemLineDebugColor = default)
    {
        ItemId = itemId;
        Matrix = matrix;
        Layer = layer;
        UseSleepAwakeDarkTint = useSleepAwakeDarkTint;
        UseBeltItemLineDebugColor = useBeltItemLineDebugColor;
        BeltItemLineDebugColor = useBeltItemLineDebugColor ? beltItemLineDebugColor : (Color32)Color.white;
    }

    public readonly int ItemId;
    public readonly Matrix4x4 Matrix;
    public readonly int Layer;
    public readonly bool UseSleepAwakeDarkTint;
    public readonly bool UseBeltItemLineDebugColor;
    public readonly Color32 BeltItemLineDebugColor;
}

[DisallowMultipleComponent]
public sealed class PortableItemRenderer : MonoBehaviour
{
    private const int SharedVirtualConveyorItemBatchGroupId = 0;

    private static readonly ProfilerMarker RebuildPortableObjectBatchesMarker =
        new ProfilerMarker("PortableItemRenderer.RebuildPortableObjectBatches");
    private static readonly ProfilerMarker RenderPortableObjectBatchesMarker =
        new ProfilerMarker("PortableItemRenderer.RenderPortableObjectBatches");
    private static readonly ProfilerMarker RebuildVirtualConveyorBatchesMarker =
        new ProfilerMarker("PortableItemRenderer.RebuildVirtualConveyorBatches");
    private static readonly ProfilerMarker RenderVirtualConveyorBatchesMarker =
        new ProfilerMarker("PortableItemRenderer.RenderVirtualConveyorBatches");

    [SerializeField, Min(1f)]
    private float portableObjectBatchCellSize = 8f;

    [SerializeField, Min(1f)]
    private float virtualConveyorItemBatchCellSize = 64f;

    [SerializeField, Min(0.25f)]
    private float dynamicVirtualConveyorItemCullBoundsSize = 2.25f;

    [SerializeField, Min(0.5f)]
    private float dynamicVirtualConveyorItemCullBoundsHeight = 2.5f;

    [SerializeField, Min(0f)]
    private float dynamicVirtualConveyorCullCameraMoveThreshold = 0.25f;

    [SerializeField, Min(0f)]
    private float dynamicVirtualConveyorCullCameraRotateThreshold = 0.5f;

    private readonly HashSet<PortableObject> registeredPortableObjects = new HashSet<PortableObject>();
    private readonly VirtualRenderBatchCollection portableObjectBatches = new VirtualRenderBatchCollection();
    private readonly List<PortableObject> portableObjectCleanupBuffer = new List<PortableObject>();
    private bool portableObjectBatchesDirty = true;

    private readonly List<Block> activeVirtualConveyorRenderBlocks = new List<Block>(512);
    private readonly HashSet<Block> activeVirtualConveyorRenderBlockLookup = new HashSet<Block>();
    private readonly List<Block> activeDynamicVirtualConveyorRenderBlocks = new List<Block>(256);
    private readonly List<Block> dynamicVirtualConveyorCullCandidateBlocks = new List<Block>(256);
    private readonly List<Block> dirtyVirtualConveyorRenderBlocks = new List<Block>(256);
    private readonly List<VirtualConveyorItemRenderData> scratchVirtualConveyorRenderItems =
        new List<VirtualConveyorItemRenderData>(8);
    private readonly Plane[] dynamicVirtualConveyorRenderFrustumPlanes = new Plane[6];
    private readonly VirtualRenderBatchCollection virtualConveyorBatches = new VirtualRenderBatchCollection();
    private readonly VirtualRenderBatchCollection dynamicVirtualConveyorBatches = new VirtualRenderBatchCollection();
    private readonly Dictionary<Block, BlockRenderCache> virtualConveyorBlockRenderCaches =
        new Dictionary<Block, BlockRenderCache>();
    private readonly HashSet<Block> activeDynamicVirtualConveyorRenderBlockLookup = new HashSet<Block>();
    private readonly Dictionary<Block, DynamicBlockRenderCache> dynamicVirtualConveyorBlockRenderCaches =
        new Dictionary<Block, DynamicBlockRenderCache>();
    private readonly Dictionary<int, ItemRenderAsset> renderAssetsByItemId = new Dictionary<int, ItemRenderAsset>();
    private readonly List<Block> staleVirtualConveyorCacheBlocks = new List<Block>(64);
    private readonly List<Block> staleDynamicVirtualConveyorCacheBlocks = new List<Block>(64);

    private TerrainGenerator terrainGenerator;
    private ItemManager itemManager;
    private ItemManager cachedRenderAssetItemManager;
    private Camera mainCamera;
    private int cachedVirtualConveyorVisualBlockSetVersion = int.MinValue;
    private int cachedDynamicVirtualConveyorVisualBlockSetVersion = int.MinValue;
    private int lastDynamicVirtualConveyorCullSourceBlocks;
    private int lastDynamicVirtualConveyorCullCandidateBlocks;
    private int lastDynamicVirtualConveyorCullLayerSkippedBlocks;
    private int lastDynamicVirtualConveyorCullFrustumSkippedBlocks;
    private int lastDynamicVirtualConveyorCullPassedBlocks;
    private int lastDynamicVirtualConveyorRenderedItems;
    private int lastDynamicVirtualConveyorKeyCacheHits;
    private int lastDynamicVirtualConveyorKeyCacheMisses;
    private int lastDynamicVirtualConveyorKeyRebuilds;
    private int lastDynamicVirtualConveyorMatrixUpdates;
    private int lastDynamicVirtualConveyorMatrixRebuilds;
    private int lastDynamicVirtualConveyorCullCacheRefreshes;
    private int lastDynamicVirtualConveyorCullCachedBlocks;
    private int cachedDynamicVirtualConveyorCullBlockSetVersion = int.MinValue;
    private int cachedDynamicVirtualConveyorCullCandidateBlocks;
    private int cachedDynamicVirtualConveyorCullLayerSkippedBlocks;
    private int cachedDynamicVirtualConveyorCullFrustumSkippedBlocks;
    private Camera cachedDynamicVirtualConveyorCullCamera;
    private Vector3 cachedDynamicVirtualConveyorCullCameraPosition;
    private Quaternion cachedDynamicVirtualConveyorCullCameraRotation;
    private float cachedDynamicVirtualConveyorCullCameraOrthographicSize;
    private float cachedDynamicVirtualConveyorCullCameraFieldOfView;
    private float cachedDynamicVirtualConveyorCullCameraAspect;
    private float cachedDynamicVirtualConveyorCullCameraNearClip;
    private float cachedDynamicVirtualConveyorCullCameraFarClip;
    private int cachedDynamicVirtualConveyorCullCameraMask;
    private bool cachedDynamicVirtualConveyorCullCameraOrthographic;

    public int RegisteredPortableObjectCount => registeredPortableObjects.Count;
    public int StaticVirtualConveyorItemBatchCount => virtualConveyorBatches.ActiveBatchCount;
    public int StaticVirtualConveyorItemInstanceCount => virtualConveyorBatches.ActiveMatrixCount;
    public int StaticVirtualConveyorItemDrawCallCount => virtualConveyorBatches.EstimatedDrawCallCount;
    public int DynamicVirtualConveyorItemBatchCount => dynamicVirtualConveyorBatches.ActiveBatchCount;
    public int DynamicVirtualConveyorItemInstanceCount => dynamicVirtualConveyorBatches.ActiveMatrixCount;
    public int DynamicVirtualConveyorItemDrawCallCount => dynamicVirtualConveyorBatches.EstimatedDrawCallCount;
    public int ActiveVirtualConveyorRenderBlockCount => activeVirtualConveyorRenderBlocks.Count;
    public int DynamicVirtualConveyorRenderBlockCount => activeDynamicVirtualConveyorRenderBlocks.Count;
    public int DirtyVirtualConveyorRenderBlockCount => dirtyVirtualConveyorRenderBlocks.Count;
    public int CachedVirtualConveyorRenderBlockCount => virtualConveyorBlockRenderCaches.Count;
    public int CachedDynamicVirtualConveyorRenderBlockCount => dynamicVirtualConveyorBlockRenderCaches.Count;
    public int CachedItemRenderAssetCount => renderAssetsByItemId.Count;
    public int DynamicVirtualConveyorCullSourceBlocks => lastDynamicVirtualConveyorCullSourceBlocks;
    public int DynamicVirtualConveyorCullCandidateBlocks => lastDynamicVirtualConveyorCullCandidateBlocks;
    public int DynamicVirtualConveyorCullLayerSkippedBlocks => lastDynamicVirtualConveyorCullLayerSkippedBlocks;
    public int DynamicVirtualConveyorCullFrustumSkippedBlocks => lastDynamicVirtualConveyorCullFrustumSkippedBlocks;
    public int DynamicVirtualConveyorCullPassedBlocks => lastDynamicVirtualConveyorCullPassedBlocks;
    public int DynamicVirtualConveyorRenderedItems => lastDynamicVirtualConveyorRenderedItems;
    public int DynamicVirtualConveyorKeyCacheHits => lastDynamicVirtualConveyorKeyCacheHits;
    public int DynamicVirtualConveyorKeyCacheMisses => lastDynamicVirtualConveyorKeyCacheMisses;
    public int DynamicVirtualConveyorKeyRebuilds => lastDynamicVirtualConveyorKeyRebuilds;
    public int DynamicVirtualConveyorMatrixUpdates => lastDynamicVirtualConveyorMatrixUpdates;
    public int DynamicVirtualConveyorMatrixRebuilds => lastDynamicVirtualConveyorMatrixRebuilds;
    public int DynamicVirtualConveyorCullCacheRefreshes => lastDynamicVirtualConveyorCullCacheRefreshes;
    public int DynamicVirtualConveyorCullCachedBlocks => lastDynamicVirtualConveyorCullCachedBlocks;
    public float VirtualConveyorItemBatchCellSize => virtualConveyorItemBatchCellSize;
    public float DynamicVirtualConveyorCullBoundsSize => dynamicVirtualConveyorItemCullBoundsSize;
    public float DynamicVirtualConveyorCullBoundsHeight => dynamicVirtualConveyorItemCullBoundsHeight;

    public static PortableItemRenderer EnsureFor(GameObject host)
    {
        if (host == null)
        {
            return null;
        }

        PortableItemRenderer renderer = host.GetComponent<PortableItemRenderer>();
        if (renderer == null)
        {
            renderer = host.AddComponent<PortableItemRenderer>();
        }

        return renderer;
    }

    public void Configure(TerrainGenerator generator, ItemManager manager)
    {
        terrainGenerator = generator;
        itemManager = manager;
    }

    public void Register(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (registeredPortableObjects.Add(portableObject))
        {
            portableObjectBatchesDirty = true;
        }
    }

    public void Unregister(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (registeredPortableObjects.Remove(portableObject))
        {
            portableObjectBatchesDirty = true;
        }
    }

    public void MarkDirty()
    {
        portableObjectBatchesDirty = true;
    }

    private void Awake()
    {
        ResolveDependencies();
    }

    private void LateUpdate()
    {
        ResolveDependencies();

        if (HasPortableObjectRenderWork())
        {
            using (RebuildPortableObjectBatchesMarker.Auto())
            {
                if (portableObjectBatchesDirty)
                {
                    RebuildPortableObjectBatches();
                    portableObjectBatchesDirty = false;
                }
            }

            using (RenderPortableObjectBatchesMarker.Auto())
            {
                RenderPortableObjectBatches();
            }
        }

        if (HasVirtualConveyorRenderWork())
        {
            RenderVirtualConveyorItems();
        }
    }

    private void ResolveDependencies()
    {
        if (terrainGenerator == null)
        {
            terrainGenerator = GetComponent<TerrainGenerator>();
            if (terrainGenerator == null)
            {
                terrainGenerator = TerrainGenerator.Active;
            }
        }

        if (itemManager == null && GameManager.Instance != null)
        {
            itemManager = GameManager.Instance.ItemManger;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    private void RebuildPortableObjectBatches()
    {
        portableObjectBatches.ClearActiveMatrices();
        portableObjectCleanupBuffer.Clear();

        foreach (PortableObject portableObject in registeredPortableObjects)
        {
            if (portableObject == null)
            {
                portableObjectCleanupBuffer.Add(portableObject);
                continue;
            }

            if (!portableObject.TryGetBatchRenderData(
                    out int itemId,
                    out Mesh mesh,
                    out Material material,
                    out Matrix4x4 localToWorldMatrix,
                    out Vector3 worldPosition,
                    out int layer,
                    out ShadowCastingMode shadowCastingMode,
                    out bool receiveShadows,
                    out bool useSleepAwakeDarkTint,
                    out bool useBeltItemLineDebugColor,
                    out Color32 beltItemLineDebugColor))
            {
                continue;
            }

            if (material != null && !material.enableInstancing)
            {
                material.enableInstancing = true;
            }

            int cellX = Mathf.FloorToInt(worldPosition.x / portableObjectBatchCellSize);
            int cellZ = Mathf.FloorToInt(worldPosition.z / portableObjectBatchCellSize);
            VirtualRenderBatchKey key = new VirtualRenderBatchKey(
                mesh,
                material,
                layer,
                0,
                shadowCastingMode,
                receiveShadows,
                false,
                0,
                useSleepAwakeDarkTint,
                useBeltItemLineDebugColor,
                beltItemLineDebugColor,
                itemId,
                cellX,
                cellZ);
            portableObjectBatches.AddMatrix(key, localToWorldMatrix);
        }

        for (int i = 0; i < portableObjectCleanupBuffer.Count; i++)
        {
            registeredPortableObjects.Remove(portableObjectCleanupBuffer[i]);
        }
    }

    private void RenderPortableObjectBatches()
    {
        portableObjectBatches.RenderBatches(mainCamera);
    }

    private void RenderVirtualConveyorItems()
    {
        if (terrainGenerator == null
            || itemManager == null
            || !terrainGenerator.VirtualizeConveyorItems
            || IsBeltItemRenderingHidden())
        {
            ClearVirtualConveyorRenderState();
            ResetDynamicVirtualConveyorRenderCounters();
            activeVirtualConveyorRenderBlocks.Clear();
            activeVirtualConveyorRenderBlockLookup.Clear();
            activeDynamicVirtualConveyorRenderBlocks.Clear();
            activeDynamicVirtualConveyorRenderBlockLookup.Clear();
            InvalidateDynamicVirtualConveyorCullCandidateCache(true);
            dirtyVirtualConveyorRenderBlocks.Clear();
            virtualConveyorBlockRenderCaches.Clear();
            dynamicVirtualConveyorBlockRenderCaches.Clear();
            cachedVirtualConveyorVisualBlockSetVersion = int.MinValue;
            cachedDynamicVirtualConveyorVisualBlockSetVersion = int.MinValue;
            return;
        }

        using (RebuildVirtualConveyorBatchesMarker.Auto())
        {
            long startTimestamp = BeginRuntimeProfileSample(out bool profileRebuild);
            try
            {
                RebuildVirtualConveyorBatches();
            }
            finally
            {
                EndRuntimeProfileSample(profileRebuild, "Conveyor Item Rebuild", startTimestamp);
            }
        }

        using (RenderVirtualConveyorBatchesMarker.Auto())
        {
            long startTimestamp = BeginRuntimeProfileSample(out bool profileRender);
            try
            {
                RenderVirtualConveyorBatches();
            }
            finally
            {
                EndRuntimeProfileSample(profileRender, "Conveyor Item Render", startTimestamp);
            }
        }
    }

    private bool HasPortableObjectRenderWork()
    {
        return portableObjectBatchesDirty
               || registeredPortableObjects.Count > 0
               || portableObjectBatches.ActiveBatchCount > 0;
    }

    private bool HasVirtualConveyorRenderWork()
    {
        if (terrainGenerator == null
            || itemManager == null
            || !terrainGenerator.VirtualizeConveyorItems
            || IsBeltItemRenderingHidden())
        {
            return HasVirtualConveyorRenderState();
        }

        return cachedRenderAssetItemManager != itemManager
               || cachedVirtualConveyorVisualBlockSetVersion != terrainGenerator.ConveyorItemVisualBlockSetVersion
               || cachedDynamicVirtualConveyorVisualBlockSetVersion != terrainGenerator.DynamicConveyorItemVisualBlockSetVersion
               || terrainGenerator.ConveyorItemVisualDirtyBlockCount > 0
               || activeVirtualConveyorRenderBlocks.Count > 0
               || activeDynamicVirtualConveyorRenderBlocks.Count > 0
               || virtualConveyorBatches.ActiveBatchCount > 0
               || dynamicVirtualConveyorBatches.ActiveBatchCount > 0;
    }

    private bool HasVirtualConveyorRenderState()
    {
        return activeVirtualConveyorRenderBlocks.Count > 0
               || activeVirtualConveyorRenderBlockLookup.Count > 0
               || activeDynamicVirtualConveyorRenderBlocks.Count > 0
               || activeDynamicVirtualConveyorRenderBlockLookup.Count > 0
               || dirtyVirtualConveyorRenderBlocks.Count > 0
               || virtualConveyorBlockRenderCaches.Count > 0
               || dynamicVirtualConveyorBlockRenderCaches.Count > 0
               || virtualConveyorBatches.ActiveBatchCount > 0
               || dynamicVirtualConveyorBatches.ActiveBatchCount > 0
               || cachedVirtualConveyorVisualBlockSetVersion != int.MinValue
               || cachedDynamicVirtualConveyorVisualBlockSetVersion != int.MinValue;
    }

    private static bool IsBeltItemRenderingHidden()
    {
        return GameManager.Instance != null && GameManager.Instance.HideBeltItems;
    }

    private static long BeginRuntimeProfileSample(out bool profile)
    {
        profile = MapObjectTickProfiler.IsEnabled;
        return profile ? MapObjectTickProfiler.BeginSample() : 0L;
    }

    private static void EndRuntimeProfileSample(bool profile, string itemName, long startTimestamp)
    {
        if (!profile)
        {
            return;
        }

        MapObjectTickProfiler.EndNamedSample(
            "Runtime",
            nameof(PortableItemRenderer),
            itemName,
            startTimestamp);
    }

    private static void EndRuntimeProfileElapsedSample(bool profile, string itemName, long elapsedTicks)
    {
        if (!profile || elapsedTicks <= 0L)
        {
            return;
        }

        long syntheticStartTimestamp = MapObjectTickProfiler.BeginSample() - elapsedTicks;
        EndRuntimeProfileSample(true, itemName, syntheticStartTimestamp);
    }

    private static long MeasureRuntimeProfilePhase(bool profile, long startTimestamp)
    {
        if (!profile)
        {
            return 0L;
        }

        return System.Math.Max(0L, MapObjectTickProfiler.BeginSample() - startTimestamp);
    }

    private void RebuildVirtualConveyorBatches()
    {
        if (cachedRenderAssetItemManager != itemManager)
        {
            long resetStartTimestamp = BeginRuntimeProfileSample(out bool profileReset);
            try
            {
                renderAssetsByItemId.Clear();
                ClearVirtualConveyorRenderState();
                cachedVirtualConveyorVisualBlockSetVersion = int.MinValue;
                cachedDynamicVirtualConveyorVisualBlockSetVersion = int.MinValue;
                cachedRenderAssetItemManager = itemManager;
            }
            finally
            {
                EndRuntimeProfileSample(profileReset, "Conveyor Item Reset Render Assets", resetStartTimestamp);
            }
        }

        bool activeSetChanged;
        long activeSetStartTimestamp = BeginRuntimeProfileSample(out bool profileActiveSet);
        try
        {
            activeSetChanged = RefreshActiveVirtualConveyorRenderBlocksIfNeeded();
        }
        finally
        {
            EndRuntimeProfileSample(profileActiveSet, "Conveyor Item Refresh Active Set", activeSetStartTimestamp);
        }

        bool dynamicSetChanged;
        long dynamicSetStartTimestamp = BeginRuntimeProfileSample(out bool profileDynamicSet);
        try
        {
            dynamicSetChanged = RefreshDynamicVirtualConveyorRenderBlocksIfNeeded();
        }
        finally
        {
            EndRuntimeProfileSample(profileDynamicSet, "Conveyor Item Refresh Dynamic Set", dynamicSetStartTimestamp);
        }

        if (activeSetChanged)
        {
            long staticCacheStartTimestamp = BeginRuntimeProfileSample(out bool profileStaticCache);
            try
            {
                ReconcileActiveVirtualConveyorRenderBlockCaches();
            }
            finally
            {
                EndRuntimeProfileSample(profileStaticCache, "Conveyor Item Reconcile Static Cache", staticCacheStartTimestamp);
            }
        }

        if (dynamicSetChanged)
        {
            long dynamicCacheStartTimestamp = BeginRuntimeProfileSample(out bool profileDynamicCache);
            try
            {
                ReconcileDynamicVirtualConveyorRenderBlockCaches();
            }
            finally
            {
                EndRuntimeProfileSample(profileDynamicCache, "Conveyor Item Reconcile Dynamic Cache", dynamicCacheStartTimestamp);
            }
        }

        long dirtyCacheStartTimestamp = BeginRuntimeProfileSample(out bool profileDirtyCache);
        try
        {
            RefreshDirtyVirtualConveyorRenderBlockCaches();
        }
        finally
        {
            EndRuntimeProfileSample(profileDirtyCache, "Conveyor Item Refresh Dirty Cache", dirtyCacheStartTimestamp);
        }
    }

    private bool RefreshActiveVirtualConveyorRenderBlocksIfNeeded()
    {
        if (terrainGenerator == null)
        {
            return false;
        }

        int version = terrainGenerator.ConveyorItemVisualBlockSetVersion;
        if (cachedVirtualConveyorVisualBlockSetVersion == version)
        {
            return false;
        }

        terrainGenerator.CopyConveyorItemVisualBlocks(activeVirtualConveyorRenderBlocks);
        activeVirtualConveyorRenderBlockLookup.Clear();
        for (int i = 0; i < activeVirtualConveyorRenderBlocks.Count; i++)
        {
            Block block = activeVirtualConveyorRenderBlocks[i];
            if (block != null)
            {
                activeVirtualConveyorRenderBlockLookup.Add(block);
            }
        }

        cachedVirtualConveyorVisualBlockSetVersion = version;
        return true;
    }

    private bool RefreshDynamicVirtualConveyorRenderBlocksIfNeeded()
    {
        if (terrainGenerator == null)
        {
            return false;
        }

        int version = terrainGenerator.DynamicConveyorItemVisualBlockSetVersion;
        if (cachedDynamicVirtualConveyorVisualBlockSetVersion == version)
        {
            return false;
        }

        terrainGenerator.CopyDynamicConveyorItemVisualBlocks(activeDynamicVirtualConveyorRenderBlocks);
        activeDynamicVirtualConveyorRenderBlockLookup.Clear();
        for (int i = 0; i < activeDynamicVirtualConveyorRenderBlocks.Count; i++)
        {
            Block block = activeDynamicVirtualConveyorRenderBlocks[i];
            if (block != null)
            {
                activeDynamicVirtualConveyorRenderBlockLookup.Add(block);
            }
        }

        PruneDynamicVirtualConveyorRenderBlockCaches();
        InvalidateDynamicVirtualConveyorCullCandidateCache(false);
        cachedDynamicVirtualConveyorVisualBlockSetVersion = version;
        return true;
    }

    private void ReconcileActiveVirtualConveyorRenderBlockCaches()
    {
        staleVirtualConveyorCacheBlocks.Clear();
        foreach (KeyValuePair<Block, BlockRenderCache> pair in virtualConveyorBlockRenderCaches)
        {
            if (pair.Key == null || !activeVirtualConveyorRenderBlockLookup.Contains(pair.Key))
            {
                staleVirtualConveyorCacheBlocks.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleVirtualConveyorCacheBlocks.Count; i++)
        {
            Block staleBlock = staleVirtualConveyorCacheBlocks[i];
            if (virtualConveyorBlockRenderCaches.TryGetValue(staleBlock, out BlockRenderCache staleCache))
            {
                RemoveVirtualConveyorBlockBatchEntries(staleCache);
            }

            virtualConveyorBlockRenderCaches.Remove(staleBlock);
        }

        for (int i = 0; i < activeVirtualConveyorRenderBlocks.Count; i++)
        {
            Block block = activeVirtualConveyorRenderBlocks[i];
            if (block == null)
            {
                continue;
            }

            if (block.HasDynamicVirtualConveyorItemVisuals())
            {
                RemoveVirtualConveyorBlockRenderCache(block);
                continue;
            }

            BlockRenderCache cache = GetOrCreateVirtualConveyorBlockRenderCache(block);
            if (cache.version != block.ConveyorItemVisualVersion || !cache.isValid)
            {
                RefreshVirtualConveyorBlockRenderCache(block, cache);
            }
        }
    }

    private void ReconcileDynamicVirtualConveyorRenderBlockCaches()
    {
        for (int i = 0; i < activeDynamicVirtualConveyorRenderBlocks.Count; i++)
        {
            RemoveVirtualConveyorBlockRenderCache(activeDynamicVirtualConveyorRenderBlocks[i]);
        }
    }

    private void PruneDynamicVirtualConveyorRenderBlockCaches()
    {
        staleDynamicVirtualConveyorCacheBlocks.Clear();
        foreach (KeyValuePair<Block, DynamicBlockRenderCache> pair in dynamicVirtualConveyorBlockRenderCaches)
        {
            if (pair.Key == null || !activeDynamicVirtualConveyorRenderBlockLookup.Contains(pair.Key))
            {
                staleDynamicVirtualConveyorCacheBlocks.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleDynamicVirtualConveyorCacheBlocks.Count; i++)
        {
            RemoveDynamicVirtualConveyorBlockRenderCache(staleDynamicVirtualConveyorCacheBlocks[i]);
        }
    }

    private void RefreshDirtyVirtualConveyorRenderBlockCaches()
    {
        if (terrainGenerator == null)
        {
            return;
        }

        terrainGenerator.CopyConveyorItemVisualDirtyBlocks(dirtyVirtualConveyorRenderBlocks);
        for (int i = 0; i < dirtyVirtualConveyorRenderBlocks.Count; i++)
        {
            Block block = dirtyVirtualConveyorRenderBlocks[i];
            if (block == null)
            {
                continue;
            }

            if (!activeVirtualConveyorRenderBlockLookup.Contains(block))
            {
                if (virtualConveyorBlockRenderCaches.TryGetValue(block, out BlockRenderCache removedCache))
                {
                    RemoveVirtualConveyorBlockBatchEntries(removedCache);
                    virtualConveyorBlockRenderCaches.Remove(block);
                }

                continue;
            }

            if (block.HasDynamicVirtualConveyorItemVisuals())
            {
                RemoveVirtualConveyorBlockRenderCache(block);
                continue;
            }

            BlockRenderCache cache = GetOrCreateVirtualConveyorBlockRenderCache(block);
            if (cache.version != block.ConveyorItemVisualVersion || !cache.isValid)
            {
                RefreshVirtualConveyorBlockRenderCache(block, cache);
            }
        }

        dirtyVirtualConveyorRenderBlocks.Clear();
    }

    private BlockRenderCache GetOrCreateVirtualConveyorBlockRenderCache(Block block)
    {
        if (!virtualConveyorBlockRenderCaches.TryGetValue(block, out BlockRenderCache cache))
        {
            cache = new BlockRenderCache();
            virtualConveyorBlockRenderCaches.Add(block, cache);
        }

        return cache;
    }

    private void RemoveVirtualConveyorBlockRenderCache(Block block)
    {
        if (block == null || !virtualConveyorBlockRenderCaches.TryGetValue(block, out BlockRenderCache cache))
        {
            return;
        }

        RemoveVirtualConveyorBlockBatchEntries(cache);
        virtualConveyorBlockRenderCaches.Remove(block);
    }

    private DynamicBlockRenderCache GetOrCreateDynamicVirtualConveyorBlockRenderCache(Block block)
    {
        if (!dynamicVirtualConveyorBlockRenderCaches.TryGetValue(block, out DynamicBlockRenderCache cache))
        {
            cache = new DynamicBlockRenderCache();
            dynamicVirtualConveyorBlockRenderCaches.Add(block, cache);
        }

        return cache;
    }

    private void RemoveDynamicVirtualConveyorBlockRenderCache(Block block)
    {
        if (ReferenceEquals(block, null)
            || !dynamicVirtualConveyorBlockRenderCaches.TryGetValue(block, out DynamicBlockRenderCache cache))
        {
            return;
        }

        RemoveDynamicVirtualConveyorBlockBatchEntries(cache);
        dynamicVirtualConveyorBlockRenderCaches.Remove(block);
    }

    private void RemoveDynamicVirtualConveyorBlockBatchEntries(DynamicBlockRenderCache blockCache)
    {
        if (blockCache == null)
        {
            return;
        }

        dynamicVirtualConveyorBatches.RemoveOwnedEntries(blockCache.batchEntries);
    }

    private void RefreshVirtualConveyorBlockRenderCache(Block block, BlockRenderCache cache)
    {
        RemoveVirtualConveyorBlockBatchEntries(cache);
        scratchVirtualConveyorRenderItems.Clear();
        block.AppendVirtualConveyorItemRenderData(scratchVirtualConveyorRenderItems);

        for (int itemIndex = 0; itemIndex < scratchVirtualConveyorRenderItems.Count; itemIndex++)
        {
            AddVirtualConveyorBlockRenderItem(cache, scratchVirtualConveyorRenderItems[itemIndex]);
        }

        scratchVirtualConveyorRenderItems.Clear();
        cache.version = block.ConveyorItemVisualVersion;
        cache.isValid = true;
    }

    private void AddVirtualConveyorBlockRenderItem(BlockRenderCache blockCache, VirtualConveyorItemRenderData renderData)
    {
        if (!TryCreateVirtualConveyorBatchKey(renderData, out VirtualRenderBatchKey key))
        {
            return;
        }

        virtualConveyorBatches.AddOwnedMatrix(blockCache, blockCache.batchEntries, key, renderData.Matrix);
    }

    private void RemoveVirtualConveyorBlockBatchEntries(BlockRenderCache blockCache)
    {
        virtualConveyorBatches.RemoveOwnedEntries(blockCache.batchEntries);
    }

    private bool TryGetItemRenderAsset(int itemId, out ItemRenderAsset renderAsset)
    {
        if (itemId < 0 || itemManager == null)
        {
            renderAsset = default;
            return false;
        }

        if (renderAssetsByItemId.TryGetValue(itemId, out renderAsset))
        {
            return renderAsset.IsValid;
        }

        if (!itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            renderAsset = ItemRenderAsset.Invalid;
            renderAssetsByItemId[itemId] = renderAsset;
            return false;
        }

        renderAsset = new ItemRenderAsset(itemSet.portableMesh, itemSet.portableMat);
        renderAssetsByItemId[itemId] = renderAsset;
        return renderAsset.IsValid;
    }

    private void ClearVirtualConveyorRenderState()
    {
        virtualConveyorBatches.Clear();
        dynamicVirtualConveyorBatches.Clear();
        dynamicVirtualConveyorBlockRenderCaches.Clear();
        InvalidateDynamicVirtualConveyorCullCandidateCache(true);

        foreach (KeyValuePair<Block, BlockRenderCache> pair in virtualConveyorBlockRenderCaches)
        {
            pair.Value.batchEntries.Clear();
            pair.Value.version = int.MinValue;
            pair.Value.isValid = false;
        }
    }

    private void RenderVirtualConveyorBatches()
    {
        long dynamicRebuildStartTimestamp = BeginRuntimeProfileSample(out bool profileDynamicRebuild);
        try
        {
            RebuildDynamicVirtualConveyorBatches();
        }
        finally
        {
            EndRuntimeProfileSample(profileDynamicRebuild, "Conveyor Item Rebuild Dynamic", dynamicRebuildStartTimestamp);
        }

        long staticRenderStartTimestamp = BeginRuntimeProfileSample(out bool profileStaticRender);
        try
        {
            virtualConveyorBatches.RenderBatches(mainCamera);
        }
        finally
        {
            EndRuntimeProfileSample(profileStaticRender, "Conveyor Item Render Static", staticRenderStartTimestamp);
        }

        long dynamicRenderStartTimestamp = BeginRuntimeProfileSample(out bool profileDynamicRender);
        try
        {
            dynamicVirtualConveyorBatches.RenderBatches(mainCamera);
        }
        finally
        {
            EndRuntimeProfileSample(profileDynamicRender, "Conveyor Item Render Dynamic", dynamicRenderStartTimestamp);
        }
    }

    private void RebuildDynamicVirtualConveyorBatches()
    {
        bool profileBreakdown = MapObjectTickProfiler.IsEnabled;
        ResetDynamicVirtualConveyorRenderCounters();
        lastDynamicVirtualConveyorCullSourceBlocks = activeDynamicVirtualConveyorRenderBlocks.Count;

        if (activeDynamicVirtualConveyorRenderBlocks.Count <= 0)
        {
            if (dynamicVirtualConveyorBatches.ActiveBatchCount > 0
                || dynamicVirtualConveyorBlockRenderCaches.Count > 0)
            {
                long clearStartTimestamp = BeginRuntimeProfileSample(out bool profileClear);
                dynamicVirtualConveyorBatches.ClearActiveMatrices();
                dynamicVirtualConveyorBlockRenderCaches.Clear();
                EndRuntimeProfileSample(profileClear, "Conveyor Item Dynamic Clear", clearStartTimestamp);
            }

            return;
        }

        long phaseStartTimestamp;
        long cullTicks = 0L;
        long cacheTicks = 0L;
        long appendDataTicks = 0L;
        long syncMatrixTicks = 0L;
        long trimCacheTicks = 0L;

        Camera renderCamera = mainCamera;
        bool useCullCandidateCache = renderCamera != null;
        IReadOnlyList<Block> dynamicRenderBlocks = activeDynamicVirtualConveyorRenderBlocks;
        if (useCullCandidateCache)
        {
            phaseStartTimestamp = profileBreakdown ? MapObjectTickProfiler.BeginSample() : 0L;
            bool refreshedCullCandidates = RefreshDynamicVirtualConveyorCullCandidateBlocksIfNeeded(renderCamera);
            cullTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);
            if (refreshedCullCandidates)
            {
                lastDynamicVirtualConveyorCullCacheRefreshes++;
            }

            lastDynamicVirtualConveyorCullCandidateBlocks = cachedDynamicVirtualConveyorCullCandidateBlocks;
            lastDynamicVirtualConveyorCullLayerSkippedBlocks = cachedDynamicVirtualConveyorCullLayerSkippedBlocks;
            lastDynamicVirtualConveyorCullFrustumSkippedBlocks = cachedDynamicVirtualConveyorCullFrustumSkippedBlocks;
            lastDynamicVirtualConveyorCullCachedBlocks = dynamicVirtualConveyorCullCandidateBlocks.Count;
            dynamicRenderBlocks = dynamicVirtualConveyorCullCandidateBlocks;
        }

        for (int i = 0; i < dynamicRenderBlocks.Count; i++)
        {
            phaseStartTimestamp = profileBreakdown ? MapObjectTickProfiler.BeginSample() : 0L;
            Block block = dynamicRenderBlocks[i];
            if (block == null || !block.HasDynamicVirtualConveyorItemVisuals())
            {
                cullTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);
                RemoveDynamicVirtualConveyorBlockRenderCache(block);
                continue;
            }

            if (!useCullCandidateCache)
            {
                lastDynamicVirtualConveyorCullCandidateBlocks++;
                if (renderCamera != null)
                {
                    DynamicVirtualConveyorCullResult cullResult =
                        GetDynamicVirtualConveyorBlockCullResult(block, renderCamera);
                    if (cullResult != DynamicVirtualConveyorCullResult.Render)
                    {
                        AddDynamicVirtualConveyorCullSkip(cullResult);
                        cullTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);
                        continue;
                    }
                }
            }

            cullTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);
            lastDynamicVirtualConveyorCullPassedBlocks++;

            phaseStartTimestamp = profileBreakdown ? MapObjectTickProfiler.BeginSample() : 0L;
            DynamicBlockRenderCache blockCache = GetOrCreateDynamicVirtualConveyorBlockRenderCache(block);
            if (blockCache.version != block.ConveyorItemVisualVersion)
            {
                RemoveDynamicVirtualConveyorBlockBatchEntries(blockCache);
                blockCache.itemKeyCaches.Clear();
                blockCache.version = block.ConveyorItemVisualVersion;
                blockCache.isValid = false;
            }
            cacheTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);

            phaseStartTimestamp = profileBreakdown ? MapObjectTickProfiler.BeginSample() : 0L;
            scratchVirtualConveyorRenderItems.Clear();
            block.AppendDynamicVirtualConveyorItemRenderData(scratchVirtualConveyorRenderItems);
            appendDataTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);

            phaseStartTimestamp = profileBreakdown ? MapObjectTickProfiler.BeginSample() : 0L;
            lastDynamicVirtualConveyorRenderedItems += SyncDynamicVirtualConveyorBlockRenderItems(
                blockCache,
                scratchVirtualConveyorRenderItems);
            syncMatrixTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);

            phaseStartTimestamp = profileBreakdown ? MapObjectTickProfiler.BeginSample() : 0L;
            blockCache.isValid = true;
            trimCacheTicks += MeasureRuntimeProfilePhase(profileBreakdown, phaseStartTimestamp);
        }

        scratchVirtualConveyorRenderItems.Clear();
        EndRuntimeProfileElapsedSample(profileBreakdown, "Conveyor Item Dynamic Cull", cullTicks);
        EndRuntimeProfileElapsedSample(profileBreakdown, "Conveyor Item Dynamic Cache", cacheTicks);
        EndRuntimeProfileElapsedSample(profileBreakdown, "Conveyor Item Dynamic AppendData", appendDataTicks);
        EndRuntimeProfileElapsedSample(profileBreakdown, "Conveyor Item Dynamic SyncMatrix", syncMatrixTicks);
        EndRuntimeProfileElapsedSample(profileBreakdown, "Conveyor Item Dynamic TrimCache", trimCacheTicks);
    }

    private bool RefreshDynamicVirtualConveyorCullCandidateBlocksIfNeeded(Camera renderCamera)
    {
        if (renderCamera == null)
        {
            InvalidateDynamicVirtualConveyorCullCandidateCache(true);
            return false;
        }

        int blockSetVersion = cachedDynamicVirtualConveyorVisualBlockSetVersion;
        if (!ShouldRefreshDynamicVirtualConveyorCullCandidateBlocks(renderCamera, blockSetVersion))
        {
            return false;
        }

        GeometryUtility.CalculateFrustumPlanes(renderCamera, dynamicVirtualConveyorRenderFrustumPlanes);
        dynamicVirtualConveyorCullCandidateBlocks.Clear();
        cachedDynamicVirtualConveyorCullCandidateBlocks = 0;
        cachedDynamicVirtualConveyorCullLayerSkippedBlocks = 0;
        cachedDynamicVirtualConveyorCullFrustumSkippedBlocks = 0;

        for (int i = 0; i < activeDynamicVirtualConveyorRenderBlocks.Count; i++)
        {
            Block block = activeDynamicVirtualConveyorRenderBlocks[i];
            if (block == null || !block.HasDynamicVirtualConveyorItemVisuals())
            {
                RemoveDynamicVirtualConveyorBlockRenderCache(block);
                continue;
            }

            cachedDynamicVirtualConveyorCullCandidateBlocks++;
            DynamicVirtualConveyorCullResult cullResult =
                GetDynamicVirtualConveyorBlockCullResult(block, renderCamera);
            if (cullResult == DynamicVirtualConveyorCullResult.Render)
            {
                dynamicVirtualConveyorCullCandidateBlocks.Add(block);
                continue;
            }

            RemoveDynamicVirtualConveyorBlockRenderCache(block);
            if (cullResult == DynamicVirtualConveyorCullResult.Layer)
            {
                cachedDynamicVirtualConveyorCullLayerSkippedBlocks++;
            }
            else if (cullResult == DynamicVirtualConveyorCullResult.Frustum)
            {
                cachedDynamicVirtualConveyorCullFrustumSkippedBlocks++;
            }
        }

        CacheDynamicVirtualConveyorCullCameraState(renderCamera, blockSetVersion);
        return true;
    }

    private bool ShouldRefreshDynamicVirtualConveyorCullCandidateBlocks(
        Camera renderCamera,
        int blockSetVersion)
    {
        if (cachedDynamicVirtualConveyorCullBlockSetVersion != blockSetVersion
            || cachedDynamicVirtualConveyorCullCamera != renderCamera)
        {
            return true;
        }

        Transform cameraTransform = renderCamera.transform;
        float moveThreshold = Mathf.Max(0f, dynamicVirtualConveyorCullCameraMoveThreshold);
        if ((cameraTransform.position - cachedDynamicVirtualConveyorCullCameraPosition).sqrMagnitude
            > moveThreshold * moveThreshold)
        {
            return true;
        }

        float rotateThreshold = Mathf.Max(0f, dynamicVirtualConveyorCullCameraRotateThreshold);
        if (Quaternion.Angle(cameraTransform.rotation, cachedDynamicVirtualConveyorCullCameraRotation) > rotateThreshold)
        {
            return true;
        }

        return cachedDynamicVirtualConveyorCullCameraMask != renderCamera.cullingMask
               || cachedDynamicVirtualConveyorCullCameraOrthographic != renderCamera.orthographic
               || !Mathf.Approximately(cachedDynamicVirtualConveyorCullCameraOrthographicSize, renderCamera.orthographicSize)
               || !Mathf.Approximately(cachedDynamicVirtualConveyorCullCameraFieldOfView, renderCamera.fieldOfView)
               || !Mathf.Approximately(cachedDynamicVirtualConveyorCullCameraAspect, renderCamera.aspect)
               || !Mathf.Approximately(cachedDynamicVirtualConveyorCullCameraNearClip, renderCamera.nearClipPlane)
               || !Mathf.Approximately(cachedDynamicVirtualConveyorCullCameraFarClip, renderCamera.farClipPlane);
    }

    private void CacheDynamicVirtualConveyorCullCameraState(
        Camera renderCamera,
        int blockSetVersion)
    {
        cachedDynamicVirtualConveyorCullBlockSetVersion = blockSetVersion;
        cachedDynamicVirtualConveyorCullCamera = renderCamera;
        Transform cameraTransform = renderCamera.transform;
        cachedDynamicVirtualConveyorCullCameraPosition = cameraTransform.position;
        cachedDynamicVirtualConveyorCullCameraRotation = cameraTransform.rotation;
        cachedDynamicVirtualConveyorCullCameraOrthographicSize = renderCamera.orthographicSize;
        cachedDynamicVirtualConveyorCullCameraFieldOfView = renderCamera.fieldOfView;
        cachedDynamicVirtualConveyorCullCameraAspect = renderCamera.aspect;
        cachedDynamicVirtualConveyorCullCameraNearClip = renderCamera.nearClipPlane;
        cachedDynamicVirtualConveyorCullCameraFarClip = renderCamera.farClipPlane;
        cachedDynamicVirtualConveyorCullCameraMask = renderCamera.cullingMask;
        cachedDynamicVirtualConveyorCullCameraOrthographic = renderCamera.orthographic;
    }

    private void InvalidateDynamicVirtualConveyorCullCandidateCache(bool clearCandidates)
    {
        cachedDynamicVirtualConveyorCullBlockSetVersion = int.MinValue;
        cachedDynamicVirtualConveyorCullCamera = null;
        cachedDynamicVirtualConveyorCullCandidateBlocks = 0;
        cachedDynamicVirtualConveyorCullLayerSkippedBlocks = 0;
        cachedDynamicVirtualConveyorCullFrustumSkippedBlocks = 0;
        if (clearCandidates)
        {
            dynamicVirtualConveyorCullCandidateBlocks.Clear();
        }
    }

    private DynamicVirtualConveyorCullResult GetDynamicVirtualConveyorBlockCullResult(Block block, Camera renderCamera)
    {
        if (block == null || renderCamera == null || !IsLayerVisibleToCamera(renderCamera, block.gameObject.layer))
        {
            return DynamicVirtualConveyorCullResult.Layer;
        }

        Bounds bounds = CreateDynamicVirtualConveyorBlockCullBounds(block);
        if (!GeometryUtility.TestPlanesAABB(dynamicVirtualConveyorRenderFrustumPlanes, bounds))
        {
            return DynamicVirtualConveyorCullResult.Frustum;
        }

        return DynamicVirtualConveyorCullResult.Render;
    }

    private Bounds CreateDynamicVirtualConveyorBlockCullBounds(Block block)
    {
        float horizontalSize = Mathf.Max(0.25f, dynamicVirtualConveyorItemCullBoundsSize);
        float verticalSize = Mathf.Max(0.5f, dynamicVirtualConveyorItemCullBoundsHeight);
        return new Bounds(
            block.transform.position + Vector3.up * (verticalSize * 0.5f),
            new Vector3(horizontalSize, verticalSize, horizontalSize));
    }

    private int SyncDynamicVirtualConveyorBlockRenderItems(
        DynamicBlockRenderCache blockCache,
        List<VirtualConveyorItemRenderData> renderItems)
    {
        if (blockCache == null || renderItems == null)
        {
            return 0;
        }

        int itemCount = renderItems.Count;
        if (!blockCache.isValid
            || blockCache.batchEntries.Count != itemCount
            || blockCache.itemKeyCaches.Count != itemCount)
        {
            return RebuildDynamicVirtualConveyorBlockRenderItems(blockCache, renderItems);
        }

        for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            if (!TryUpdateDynamicVirtualConveyorRenderItem(
                    blockCache,
                    itemIndex,
                    renderItems[itemIndex]))
            {
                return RebuildDynamicVirtualConveyorBlockRenderItems(blockCache, renderItems);
            }
        }

        return itemCount;
    }

    private bool TryUpdateDynamicVirtualConveyorRenderItem(
        DynamicBlockRenderCache blockCache,
        int itemIndex,
        VirtualConveyorItemRenderData renderData)
    {
        if (!TryGetCachedDynamicVirtualConveyorBatchKey(blockCache, itemIndex, renderData, out VirtualRenderBatchKey key))
        {
            return false;
        }

        if (!dynamicVirtualConveyorBatches.TryUpdateOwnedMatrix(
                blockCache.batchEntries,
                itemIndex,
                key,
                renderData.Matrix))
        {
            return false;
        }

        lastDynamicVirtualConveyorMatrixUpdates++;
        return true;
    }

    private int RebuildDynamicVirtualConveyorBlockRenderItems(
        DynamicBlockRenderCache blockCache,
        List<VirtualConveyorItemRenderData> renderItems)
    {
        RemoveDynamicVirtualConveyorBlockBatchEntries(blockCache);
        blockCache.itemKeyCaches.Clear();
        lastDynamicVirtualConveyorMatrixRebuilds++;

        int renderedItems = 0;
        int itemCount = renderItems != null ? renderItems.Count : 0;
        for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            if (!TryGetDynamicVirtualConveyorBatchKey(
                    blockCache,
                    itemIndex,
                    renderItems[itemIndex],
                    out VirtualRenderBatchKey key))
            {
                continue;
            }

            dynamicVirtualConveyorBatches.AddOwnedMatrix(
                blockCache,
                blockCache.batchEntries,
                key,
                renderItems[itemIndex].Matrix);
            renderedItems++;
        }

        blockCache.isValid = true;
        return renderedItems;
    }

    private bool TryGetCachedDynamicVirtualConveyorBatchKey(
        DynamicBlockRenderCache blockCache,
        int itemIndex,
        VirtualConveyorItemRenderData renderData,
        out VirtualRenderBatchKey key)
    {
        key = default;
        if (blockCache == null
            || itemIndex < 0
            || itemIndex >= blockCache.itemKeyCaches.Count)
        {
            lastDynamicVirtualConveyorKeyCacheMisses++;
            return false;
        }

        Vector3 worldPosition = ExtractWorldPosition(renderData.Matrix);
        int cellX = GetBatchCell(worldPosition.x, virtualConveyorItemBatchCellSize);
        int cellZ = GetBatchCell(worldPosition.z, virtualConveyorItemBatchCellSize);
        DynamicItemRenderKeyCache itemKeyCache = blockCache.itemKeyCaches[itemIndex];
        if (!itemKeyCache.Matches(renderData, cellX, cellZ))
        {
            lastDynamicVirtualConveyorKeyCacheMisses++;
            return false;
        }

        key = itemKeyCache.Key;
        lastDynamicVirtualConveyorKeyCacheHits++;
        return true;
    }

    private bool TryGetDynamicVirtualConveyorBatchKey(
        DynamicBlockRenderCache blockCache,
        int itemIndex,
        VirtualConveyorItemRenderData renderData,
        out VirtualRenderBatchKey key)
    {
        key = default;
        if (blockCache == null || itemIndex < 0)
        {
            lastDynamicVirtualConveyorKeyCacheMisses++;
            return TryCreateVirtualConveyorBatchKey(renderData, out key);
        }

        Vector3 worldPosition = ExtractWorldPosition(renderData.Matrix);
        int cellX = GetBatchCell(worldPosition.x, virtualConveyorItemBatchCellSize);
        int cellZ = GetBatchCell(worldPosition.z, virtualConveyorItemBatchCellSize);

        while (blockCache.itemKeyCaches.Count <= itemIndex)
        {
            blockCache.itemKeyCaches.Add(default);
        }

        DynamicItemRenderKeyCache itemKeyCache = blockCache.itemKeyCaches[itemIndex];
        if (itemKeyCache.Matches(renderData, cellX, cellZ))
        {
            key = itemKeyCache.Key;
            lastDynamicVirtualConveyorKeyCacheHits++;
            return true;
        }

        lastDynamicVirtualConveyorKeyCacheMisses++;
        if (!TryCreateVirtualConveyorBatchKey(renderData, cellX, cellZ, out key))
        {
            blockCache.itemKeyCaches[itemIndex] = default;
            return false;
        }

        blockCache.itemKeyCaches[itemIndex] =
            new DynamicItemRenderKeyCache(renderData, cellX, cellZ, key);
        lastDynamicVirtualConveyorKeyRebuilds++;
        return true;
    }

    private bool TryCreateVirtualConveyorBatchKey(
        VirtualConveyorItemRenderData renderData,
        out VirtualRenderBatchKey key)
    {
        Vector3 worldPosition = ExtractWorldPosition(renderData.Matrix);
        return TryCreateVirtualConveyorBatchKey(
            renderData,
            GetBatchCell(worldPosition.x, virtualConveyorItemBatchCellSize),
            GetBatchCell(worldPosition.z, virtualConveyorItemBatchCellSize),
            out key);
    }

    private bool TryCreateVirtualConveyorBatchKey(
        VirtualConveyorItemRenderData renderData,
        int cellX,
        int cellZ,
        out VirtualRenderBatchKey key)
    {
        key = default;
        if (!TryGetItemRenderAsset(renderData.ItemId, out ItemRenderAsset renderAsset))
        {
            return false;
        }

        Mesh mesh = renderAsset.Mesh;
        Material material = renderAsset.Material;
        if (mesh == null || material == null)
        {
            return false;
        }

        if (!material.enableInstancing)
        {
            material.enableInstancing = true;
        }

        key = new VirtualRenderBatchKey(
            mesh,
            material,
            renderData.Layer,
            0,
            ShadowCastingMode.Off,
            false,
            false,
            0,
            renderData.UseSleepAwakeDarkTint,
            renderData.UseBeltItemLineDebugColor,
            renderData.BeltItemLineDebugColor,
            SharedVirtualConveyorItemBatchGroupId,
            cellX,
            cellZ);
        return true;
    }

    private static Vector3 ExtractWorldPosition(Matrix4x4 matrix)
    {
        return new Vector3(matrix.m03, matrix.m13, matrix.m23);
    }

    private static int GetBatchCell(float worldCoordinate, float cellSize)
    {
        return Mathf.FloorToInt(worldCoordinate / Mathf.Max(1f, cellSize));
    }

    private static bool IsLayerVisibleToCamera(Camera camera, int layer)
    {
        return camera == null
            || layer < 0
            || layer > 31
            || (camera.cullingMask & (1 << layer)) != 0;
    }

    private void ResetDynamicVirtualConveyorRenderCounters()
    {
        lastDynamicVirtualConveyorCullSourceBlocks = 0;
        lastDynamicVirtualConveyorCullCandidateBlocks = 0;
        lastDynamicVirtualConveyorCullLayerSkippedBlocks = 0;
        lastDynamicVirtualConveyorCullFrustumSkippedBlocks = 0;
        lastDynamicVirtualConveyorCullPassedBlocks = 0;
        lastDynamicVirtualConveyorRenderedItems = 0;
        lastDynamicVirtualConveyorKeyCacheHits = 0;
        lastDynamicVirtualConveyorKeyCacheMisses = 0;
        lastDynamicVirtualConveyorKeyRebuilds = 0;
        lastDynamicVirtualConveyorMatrixUpdates = 0;
        lastDynamicVirtualConveyorMatrixRebuilds = 0;
        lastDynamicVirtualConveyorCullCacheRefreshes = 0;
        lastDynamicVirtualConveyorCullCachedBlocks = 0;
    }

    private void AddDynamicVirtualConveyorCullSkip(DynamicVirtualConveyorCullResult cullResult)
    {
        switch (cullResult)
        {
            case DynamicVirtualConveyorCullResult.Layer:
                lastDynamicVirtualConveyorCullLayerSkippedBlocks++;
                break;
            case DynamicVirtualConveyorCullResult.Frustum:
                lastDynamicVirtualConveyorCullFrustumSkippedBlocks++;
                break;
        }
    }

    private enum DynamicVirtualConveyorCullResult
    {
        Render,
        Layer,
        Frustum
    }

    private sealed class BlockRenderCache : IVirtualRenderBatchOwner
    {
        public readonly List<VirtualRenderBatchEntry> batchEntries = new List<VirtualRenderBatchEntry>(4);
        public int version = int.MinValue;
        public bool isValid;

        public int BatchEntryCount => batchEntries.Count;

        public void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex)
        {
            if (entryIndex < 0 || entryIndex >= batchEntries.Count)
            {
                return;
            }

            VirtualRenderBatchEntry entry = batchEntries[entryIndex];
            entry.MatrixIndex = matrixIndex;
            batchEntries[entryIndex] = entry;
        }
    }

    private sealed class DynamicBlockRenderCache : IVirtualRenderBatchOwner
    {
        public readonly List<VirtualRenderBatchEntry> batchEntries = new List<VirtualRenderBatchEntry>(4);
        public readonly List<DynamicItemRenderKeyCache> itemKeyCaches =
            new List<DynamicItemRenderKeyCache>(4);
        public int version = int.MinValue;
        public bool isValid;

        public int BatchEntryCount => batchEntries.Count;

        public void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex)
        {
            if (entryIndex < 0 || entryIndex >= batchEntries.Count)
            {
                return;
            }

            VirtualRenderBatchEntry entry = batchEntries[entryIndex];
            entry.MatrixIndex = matrixIndex;
            batchEntries[entryIndex] = entry;
        }
    }

    private readonly struct DynamicItemRenderKeyCache
    {
        public DynamicItemRenderKeyCache(
            VirtualConveyorItemRenderData renderData,
            int cellX,
            int cellZ,
            VirtualRenderBatchKey key)
        {
            ItemId = renderData.ItemId;
            Layer = renderData.Layer;
            CellX = cellX;
            CellZ = cellZ;
            UseSleepAwakeDarkTint = renderData.UseSleepAwakeDarkTint;
            UseBeltItemLineDebugColor = renderData.UseBeltItemLineDebugColor;
            BeltItemLineDebugColor = renderData.BeltItemLineDebugColor;
            Key = key;
            IsValid = true;
        }

        public readonly int ItemId;
        public readonly int Layer;
        public readonly int CellX;
        public readonly int CellZ;
        public readonly bool UseSleepAwakeDarkTint;
        public readonly bool UseBeltItemLineDebugColor;
        public readonly Color32 BeltItemLineDebugColor;
        public readonly VirtualRenderBatchKey Key;
        public readonly bool IsValid;

        public bool Matches(VirtualConveyorItemRenderData renderData, int cellX, int cellZ)
        {
            return IsValid
                   && ItemId == renderData.ItemId
                   && Layer == renderData.Layer
                   && CellX == cellX
                   && CellZ == cellZ
                   && UseSleepAwakeDarkTint == renderData.UseSleepAwakeDarkTint
                   && UseBeltItemLineDebugColor == renderData.UseBeltItemLineDebugColor
                   && BeltItemLineDebugColor.Equals(renderData.BeltItemLineDebugColor);
        }
    }

    private readonly struct ItemRenderAsset
    {
        public static readonly ItemRenderAsset Invalid = new ItemRenderAsset(null, null);

        public readonly Mesh Mesh;
        public readonly Material Material;

        public ItemRenderAsset(Mesh mesh, Material material)
        {
            Mesh = mesh;
            Material = material;
        }

        public bool IsValid => Mesh != null && Material != null;
    }
}
