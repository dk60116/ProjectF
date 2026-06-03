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
    private float virtualConveyorItemBatchCellSize = 8f;

    private readonly HashSet<PortableObject> registeredPortableObjects = new HashSet<PortableObject>();
    private readonly VirtualRenderBatchCollection portableObjectBatches = new VirtualRenderBatchCollection();
    private readonly List<PortableObject> portableObjectCleanupBuffer = new List<PortableObject>();
    private bool portableObjectBatchesDirty = true;

    private readonly List<Block> activeVirtualConveyorRenderBlocks = new List<Block>(512);
    private readonly HashSet<Block> activeVirtualConveyorRenderBlockLookup = new HashSet<Block>();
    private readonly List<Block> activeDynamicVirtualConveyorRenderBlocks = new List<Block>(256);
    private readonly List<Block> dirtyVirtualConveyorRenderBlocks = new List<Block>(256);
    private readonly List<VirtualConveyorItemRenderData> scratchVirtualConveyorRenderItems =
        new List<VirtualConveyorItemRenderData>(8);
    private readonly Plane[] dynamicVirtualConveyorRenderFrustumPlanes = new Plane[6];
    private readonly VirtualRenderBatchCollection virtualConveyorBatches = new VirtualRenderBatchCollection();
    private readonly VirtualRenderBatchCollection dynamicVirtualConveyorBatches = new VirtualRenderBatchCollection();
    private readonly Dictionary<Block, BlockRenderCache> virtualConveyorBlockRenderCaches =
        new Dictionary<Block, BlockRenderCache>();
    private readonly Dictionary<int, ItemRenderAsset> renderAssetsByItemId = new Dictionary<int, ItemRenderAsset>();
    private readonly List<Block> staleVirtualConveyorCacheBlocks = new List<Block>(64);

    private TerrainGenerator terrainGenerator;
    private ItemManager itemManager;
    private ItemManager cachedRenderAssetItemManager;
    private Camera mainCamera;
    private int cachedVirtualConveyorVisualBlockSetVersion = int.MinValue;
    private int cachedDynamicVirtualConveyorVisualBlockSetVersion = int.MinValue;

    public int RegisteredPortableObjectCount => registeredPortableObjects.Count;

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

        RenderVirtualConveyorItems();
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
        if (terrainGenerator == null || itemManager == null || !terrainGenerator.VirtualizeConveyorItems)
        {
            ClearVirtualConveyorRenderState();
            activeVirtualConveyorRenderBlocks.Clear();
            activeVirtualConveyorRenderBlockLookup.Clear();
            activeDynamicVirtualConveyorRenderBlocks.Clear();
            dirtyVirtualConveyorRenderBlocks.Clear();
            virtualConveyorBlockRenderCaches.Clear();
            cachedVirtualConveyorVisualBlockSetVersion = int.MinValue;
            cachedDynamicVirtualConveyorVisualBlockSetVersion = int.MinValue;
            return;
        }

        using (RebuildVirtualConveyorBatchesMarker.Auto())
        {
            RebuildVirtualConveyorBatches();
        }

        using (RenderVirtualConveyorBatchesMarker.Auto())
        {
            RenderVirtualConveyorBatches();
        }
    }

    private void RebuildVirtualConveyorBatches()
    {
        if (cachedRenderAssetItemManager != itemManager)
        {
            renderAssetsByItemId.Clear();
            ClearVirtualConveyorRenderState();
            cachedVirtualConveyorVisualBlockSetVersion = int.MinValue;
            cachedDynamicVirtualConveyorVisualBlockSetVersion = int.MinValue;
            cachedRenderAssetItemManager = itemManager;
        }

        bool activeSetChanged = RefreshActiveVirtualConveyorRenderBlocksIfNeeded();
        bool dynamicSetChanged = RefreshDynamicVirtualConveyorRenderBlocksIfNeeded();
        if (activeSetChanged)
        {
            ReconcileActiveVirtualConveyorRenderBlockCaches();
        }

        if (dynamicSetChanged)
        {
            ReconcileDynamicVirtualConveyorRenderBlockCaches();
        }

        RefreshDirtyVirtualConveyorRenderBlockCaches();
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

        foreach (KeyValuePair<Block, BlockRenderCache> pair in virtualConveyorBlockRenderCaches)
        {
            pair.Value.batchEntries.Clear();
            pair.Value.version = int.MinValue;
            pair.Value.isValid = false;
        }
    }

    private void RenderVirtualConveyorBatches()
    {
        RebuildDynamicVirtualConveyorBatches();
        virtualConveyorBatches.RenderBatches(mainCamera);
        dynamicVirtualConveyorBatches.RenderBatches(mainCamera);
    }

    private void RebuildDynamicVirtualConveyorBatches()
    {
        dynamicVirtualConveyorBatches.ClearActiveMatrices();
        Camera renderCamera = mainCamera;
        bool canCullDynamicBlocks = renderCamera != null;
        if (canCullDynamicBlocks)
        {
            GeometryUtility.CalculateFrustumPlanes(renderCamera, dynamicVirtualConveyorRenderFrustumPlanes);
        }

        for (int i = 0; i < activeDynamicVirtualConveyorRenderBlocks.Count; i++)
        {
            Block block = activeDynamicVirtualConveyorRenderBlocks[i];
            if (block == null || !block.HasDynamicVirtualConveyorItemVisuals())
            {
                continue;
            }

            if (canCullDynamicBlocks && !ShouldRenderDynamicVirtualConveyorBlock(block, renderCamera))
            {
                continue;
            }

            scratchVirtualConveyorRenderItems.Clear();
            block.AppendVirtualConveyorItemRenderData(scratchVirtualConveyorRenderItems);
            for (int itemIndex = 0; itemIndex < scratchVirtualConveyorRenderItems.Count; itemIndex++)
            {
                AddDynamicVirtualConveyorRenderItem(scratchVirtualConveyorRenderItems[itemIndex]);
            }
        }

        scratchVirtualConveyorRenderItems.Clear();
    }

    private bool ShouldRenderDynamicVirtualConveyorBlock(Block block, Camera renderCamera)
    {
        if (block == null || renderCamera == null || !IsLayerVisibleToCamera(renderCamera, block.gameObject.layer))
        {
            return false;
        }

        float boundsSize = Mathf.Max(4f, virtualConveyorItemBatchCellSize);
        Bounds bounds = new Bounds(block.transform.position + Vector3.up, new Vector3(boundsSize, boundsSize, boundsSize));
        return GeometryUtility.TestPlanesAABB(dynamicVirtualConveyorRenderFrustumPlanes, bounds);
    }

    private void AddDynamicVirtualConveyorRenderItem(VirtualConveyorItemRenderData renderData)
    {
        if (!TryCreateVirtualConveyorBatchKey(renderData, out VirtualRenderBatchKey key))
        {
            return;
        }

        dynamicVirtualConveyorBatches.AddMatrix(key, renderData.Matrix);
    }

    private bool TryCreateVirtualConveyorBatchKey(
        VirtualConveyorItemRenderData renderData,
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

        Vector3 worldPosition = ExtractWorldPosition(renderData.Matrix);
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
            renderData.ItemId,
            GetBatchCell(worldPosition.x, virtualConveyorItemBatchCellSize),
            GetBatchCell(worldPosition.z, virtualConveyorItemBatchCellSize));
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
