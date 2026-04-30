using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualConveyorItemRenderData
{
    public VirtualConveyorItemRenderData(int itemId, Matrix4x4 matrix, int layer)
    {
        ItemId = itemId;
        Matrix = matrix;
        Layer = layer;
    }

    public readonly int ItemId;
    public readonly Matrix4x4 Matrix;
    public readonly int Layer;
}

[DisallowMultipleComponent]
public sealed class VirtualConveyorItemRenderer : MonoBehaviour
{
    private static readonly ProfilerMarker RebuildBatchesMarker = new ProfilerMarker("VirtualConveyorItemRenderer.RebuildBatches");
    private static readonly ProfilerMarker RenderBatchesMarker = new ProfilerMarker("VirtualConveyorItemRenderer.RenderBatches");

    private readonly List<Block> activeRenderBlocks = new List<Block>(512);
    private readonly HashSet<Block> activeRenderBlockLookup = new HashSet<Block>();
    private readonly List<Block> dirtyRenderBlocks = new List<Block>(256);
    private readonly List<VirtualConveyorItemRenderData> scratchRenderItems = new List<VirtualConveyorItemRenderData>(8);
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly VirtualRenderBatchCollection dynamicBatches = new VirtualRenderBatchCollection();
    private readonly Dictionary<Block, BlockRenderCache> blockRenderCaches = new Dictionary<Block, BlockRenderCache>();
    private readonly Dictionary<int, ItemRenderAsset> renderAssetsByItemId = new Dictionary<int, ItemRenderAsset>();
    private readonly List<Block> staleCacheBlocks = new List<Block>(64);

    private TerrainGenerator terrainGenerator;
    private ItemManager itemManager;
    private ItemManager cachedRenderAssetItemManager;
    private int cachedVisualBlockSetVersion = int.MinValue;

    public void Configure(TerrainGenerator generator, ItemManager manager)
    {
        terrainGenerator = generator;
        itemManager = manager;
    }

    private void Awake()
    {
        ResolveDependencies();
    }

    private void LateUpdate()
    {
        ResolveDependencies();
        if (terrainGenerator == null || itemManager == null || !terrainGenerator.VirtualizeConveyorItems)
        {
            ClearRenderState();
            activeRenderBlocks.Clear();
            activeRenderBlockLookup.Clear();
            dirtyRenderBlocks.Clear();
            blockRenderCaches.Clear();
            cachedVisualBlockSetVersion = int.MinValue;
            return;
        }

        using (RebuildBatchesMarker.Auto())
        {
            RebuildBatches();
        }

        using (RenderBatchesMarker.Auto())
        {
            RenderBatches();
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
    }

    private void RebuildBatches()
    {
        if (cachedRenderAssetItemManager != itemManager)
        {
            renderAssetsByItemId.Clear();
            ClearRenderState();
            cachedVisualBlockSetVersion = int.MinValue;
            cachedRenderAssetItemManager = itemManager;
        }

        bool activeSetChanged = RefreshActiveRenderBlocksIfNeeded();
        if (activeSetChanged)
        {
            ReconcileActiveRenderBlockCaches();
        }

        RefreshDirtyBlockRenderCaches();
    }

    private bool RefreshActiveRenderBlocksIfNeeded()
    {
        if (terrainGenerator == null)
        {
            return false;
        }

        int version = terrainGenerator.ConveyorItemVisualBlockSetVersion;
        if (cachedVisualBlockSetVersion == version)
        {
            return false;
        }

        terrainGenerator.CopyConveyorItemVisualBlocks(activeRenderBlocks);
        activeRenderBlockLookup.Clear();
        for (int i = 0; i < activeRenderBlocks.Count; i++)
        {
            Block block = activeRenderBlocks[i];
            if (block != null)
            {
                activeRenderBlockLookup.Add(block);
            }
        }

        cachedVisualBlockSetVersion = version;
        return true;
    }

    private void ReconcileActiveRenderBlockCaches()
    {
        staleCacheBlocks.Clear();
        foreach (KeyValuePair<Block, BlockRenderCache> pair in blockRenderCaches)
        {
            if (pair.Key == null || !activeRenderBlockLookup.Contains(pair.Key))
            {
                staleCacheBlocks.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleCacheBlocks.Count; i++)
        {
            Block staleBlock = staleCacheBlocks[i];
            if (blockRenderCaches.TryGetValue(staleBlock, out BlockRenderCache staleCache))
            {
                RemoveBlockBatchEntries(staleCache);
            }

            blockRenderCaches.Remove(staleBlock);
        }

        for (int i = 0; i < activeRenderBlocks.Count; i++)
        {
            Block block = activeRenderBlocks[i];
            if (block == null)
            {
                continue;
            }

            if (block.HasDynamicVirtualConveyorItemVisuals())
            {
                RemoveBlockRenderCache(block);
                continue;
            }

            BlockRenderCache cache = GetOrCreateBlockRenderCache(block);
            if (cache.version != block.ConveyorItemVisualVersion || !cache.isValid)
            {
                RefreshBlockRenderCache(block, cache);
            }
        }
    }

    private void RefreshDirtyBlockRenderCaches()
    {
        if (terrainGenerator == null)
        {
            return;
        }

        terrainGenerator.CopyConveyorItemVisualDirtyBlocks(dirtyRenderBlocks);
        for (int i = 0; i < dirtyRenderBlocks.Count; i++)
        {
            Block block = dirtyRenderBlocks[i];
            if (block == null)
            {
                continue;
            }

            if (!activeRenderBlockLookup.Contains(block))
            {
                if (blockRenderCaches.TryGetValue(block, out BlockRenderCache removedCache))
                {
                    RemoveBlockBatchEntries(removedCache);
                    blockRenderCaches.Remove(block);
                }

                continue;
            }

            if (block.HasDynamicVirtualConveyorItemVisuals())
            {
                RemoveBlockRenderCache(block);
                continue;
            }

            BlockRenderCache cache = GetOrCreateBlockRenderCache(block);
            if (cache.version != block.ConveyorItemVisualVersion || !cache.isValid)
            {
                RefreshBlockRenderCache(block, cache);
            }
        }

        dirtyRenderBlocks.Clear();
    }

    private BlockRenderCache GetOrCreateBlockRenderCache(Block block)
    {
        if (!blockRenderCaches.TryGetValue(block, out BlockRenderCache cache))
        {
            cache = new BlockRenderCache();
            blockRenderCaches.Add(block, cache);
        }

        return cache;
    }

    private void RemoveBlockRenderCache(Block block)
    {
        if (block == null || !blockRenderCaches.TryGetValue(block, out BlockRenderCache cache))
        {
            return;
        }

        RemoveBlockBatchEntries(cache);
        blockRenderCaches.Remove(block);
    }

    private void RefreshBlockRenderCache(Block block, BlockRenderCache cache)
    {
        RemoveBlockBatchEntries(cache);
        scratchRenderItems.Clear();
        block.AppendVirtualConveyorItemRenderData(scratchRenderItems);

        for (int itemIndex = 0; itemIndex < scratchRenderItems.Count; itemIndex++)
        {
            AddBlockRenderItem(cache, scratchRenderItems[itemIndex]);
        }

        scratchRenderItems.Clear();
        cache.version = block.ConveyorItemVisualVersion;
        cache.isValid = true;
    }

    private void AddBlockRenderItem(BlockRenderCache blockCache, VirtualConveyorItemRenderData renderData)
    {
        if (!TryGetItemRenderAsset(renderData.ItemId, out ItemRenderAsset renderAsset))
        {
            return;
        }

        Mesh mesh = renderAsset.Mesh;
        Material material = renderAsset.Material;
        if (mesh == null || material == null)
        {
            return;
        }

        if (!material.enableInstancing)
        {
            material.enableInstancing = true;
        }

        VirtualRenderBatchKey key = new VirtualRenderBatchKey(
            mesh,
            material,
            renderData.Layer,
            0,
            ShadowCastingMode.On,
            true,
            false,
            0);

        batches.AddOwnedMatrix(blockCache, blockCache.batchEntries, key, renderData.Matrix);
    }

    private void RemoveBlockBatchEntries(BlockRenderCache blockCache)
    {
        batches.RemoveOwnedEntries(blockCache.batchEntries);
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

    private void ClearRenderState()
    {
        batches.Clear();
        dynamicBatches.Clear();

        foreach (KeyValuePair<Block, BlockRenderCache> pair in blockRenderCaches)
        {
            pair.Value.batchEntries.Clear();
            pair.Value.version = int.MinValue;
            pair.Value.isValid = false;
        }
    }

    private void RenderBatches()
    {
        RebuildDynamicBatches();
        batches.RenderBatches();
        dynamicBatches.RenderBatches();
    }

    private void RebuildDynamicBatches()
    {
        ClearDynamicRenderState();

        for (int i = 0; i < activeRenderBlocks.Count; i++)
        {
            Block block = activeRenderBlocks[i];
            if (block == null || !block.HasDynamicVirtualConveyorItemVisuals())
            {
                continue;
            }

            RemoveBlockRenderCache(block);
            scratchRenderItems.Clear();
            block.AppendVirtualConveyorItemRenderData(scratchRenderItems);
            for (int itemIndex = 0; itemIndex < scratchRenderItems.Count; itemIndex++)
            {
                AddDynamicRenderItem(scratchRenderItems[itemIndex]);
            }
        }

        scratchRenderItems.Clear();
    }

    private void ClearDynamicRenderState()
    {
        dynamicBatches.ClearActiveMatrices();
    }

    private void AddDynamicRenderItem(VirtualConveyorItemRenderData renderData)
    {
        if (!TryGetItemRenderAsset(renderData.ItemId, out ItemRenderAsset renderAsset))
        {
            return;
        }

        Mesh mesh = renderAsset.Mesh;
        Material material = renderAsset.Material;
        if (mesh == null || material == null)
        {
            return;
        }

        if (!material.enableInstancing)
        {
            material.enableInstancing = true;
        }

        VirtualRenderBatchKey key = new VirtualRenderBatchKey(
            mesh,
            material,
            renderData.Layer,
            0,
            ShadowCastingMode.On,
            true,
            false,
            0);

        dynamicBatches.AddMatrix(key, renderData.Matrix);
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
