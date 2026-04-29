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
    private const int MaxInstancesPerDraw = 1023;
    private static readonly ProfilerMarker RebuildBatchesMarker = new ProfilerMarker("VirtualConveyorItemRenderer.RebuildBatches");
    private static readonly ProfilerMarker RenderBatchesMarker = new ProfilerMarker("VirtualConveyorItemRenderer.RenderBatches");

    private readonly List<Block> activeRenderBlocks = new List<Block>(512);
    private readonly HashSet<Block> activeRenderBlockLookup = new HashSet<Block>();
    private readonly List<Block> dirtyRenderBlocks = new List<Block>(256);
    private readonly List<VirtualConveyorItemRenderData> scratchRenderItems = new List<VirtualConveyorItemRenderData>(8);
    private readonly Dictionary<BatchKey, BatchRenderCache> batchesByKey = new Dictionary<BatchKey, BatchRenderCache>();
    private readonly Dictionary<Block, BlockRenderCache> blockRenderCaches = new Dictionary<Block, BlockRenderCache>();
    private readonly Dictionary<int, ItemRenderAsset> renderAssetsByItemId = new Dictionary<int, ItemRenderAsset>();
    private readonly List<BatchKey> activeBatchKeys = new List<BatchKey>();
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

        BatchKey key = new BatchKey(mesh, material, renderData.Layer);
        if (!batchesByKey.TryGetValue(key, out BatchRenderCache batchCache))
        {
            batchCache = new BatchRenderCache();
            batchesByKey.Add(key, batchCache);
            activeBatchKeys.Add(key);
        }

        int entryIndex = blockCache.batchEntries.Count;
        int matrixIndex = batchCache.matrices.Count;
        blockCache.batchEntries.Add(new BlockBatchEntry(key, matrixIndex));
        batchCache.matrices.Add(renderData.Matrix);
        batchCache.owners.Add(new MatrixOwner(blockCache, entryIndex));
    }

    private void RemoveBlockBatchEntries(BlockRenderCache blockCache)
    {
        for (int i = blockCache.batchEntries.Count - 1; i >= 0; i--)
        {
            RemoveBlockBatchEntry(blockCache, i);
        }

        blockCache.batchEntries.Clear();
    }

    private void RemoveBlockBatchEntry(BlockRenderCache blockCache, int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= blockCache.batchEntries.Count)
        {
            return;
        }

        BlockBatchEntry entry = blockCache.batchEntries[entryIndex];
        if (!batchesByKey.TryGetValue(entry.BatchKey, out BatchRenderCache batchCache))
        {
            return;
        }

        int lastIndex = batchCache.matrices.Count - 1;
        int matrixIndex = entry.MatrixIndex;
        if (matrixIndex < 0 || matrixIndex > lastIndex)
        {
            return;
        }

        if (matrixIndex != lastIndex)
        {
            batchCache.matrices[matrixIndex] = batchCache.matrices[lastIndex];
            MatrixOwner movedOwner = batchCache.owners[lastIndex];
            batchCache.owners[matrixIndex] = movedOwner;
            if (movedOwner.BlockCache != null
                && movedOwner.EntryIndex >= 0
                && movedOwner.EntryIndex < movedOwner.BlockCache.batchEntries.Count)
            {
                BlockBatchEntry movedEntry = movedOwner.BlockCache.batchEntries[movedOwner.EntryIndex];
                movedEntry.MatrixIndex = matrixIndex;
                movedOwner.BlockCache.batchEntries[movedOwner.EntryIndex] = movedEntry;
            }
        }

        batchCache.matrices.RemoveAt(lastIndex);
        batchCache.owners.RemoveAt(lastIndex);
        if (batchCache.matrices.Count == 0)
        {
            batchesByKey.Remove(entry.BatchKey);
            activeBatchKeys.Remove(entry.BatchKey);
        }
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
        batchesByKey.Clear();
        activeBatchKeys.Clear();

        foreach (KeyValuePair<Block, BlockRenderCache> pair in blockRenderCaches)
        {
            pair.Value.batchEntries.Clear();
            pair.Value.version = int.MinValue;
            pair.Value.isValid = false;
        }
    }

    private void RenderBatches()
    {
        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            BatchKey key = activeBatchKeys[batchIndex];
            if (!batchesByKey.TryGetValue(key, out BatchRenderCache batchCache) || batchCache.matrices.Count <= 0)
            {
                continue;
            }

            RenderParams renderParams = new RenderParams(key.Material)
            {
                layer = key.Layer,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true
            };

            List<Matrix4x4> matrices = batchCache.matrices;
            int remaining = matrices.Count;
            int startIndex = 0;
            while (remaining > 0)
            {
                int drawCount = Mathf.Min(MaxInstancesPerDraw, remaining);
                Graphics.RenderMeshInstanced(renderParams, key.Mesh, 0, matrices, drawCount, startIndex);
                startIndex += drawCount;
                remaining -= drawCount;
            }
        }
    }

    private readonly struct BatchKey : System.IEquatable<BatchKey>
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly int Layer;

        public BatchKey(Mesh mesh, Material material, int layer)
        {
            Mesh = mesh;
            Material = material;
            Layer = layer;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Mesh != null ? Mesh.GetInstanceID() : 0;
                hash = (hash * 397) ^ (Material != null ? Material.GetInstanceID() : 0);
                hash = (hash * 397) ^ Layer;
                return hash;
            }
        }

        public bool Equals(BatchKey other)
        {
            return Mesh == other.Mesh
                   && Material == other.Material
                   && Layer == other.Layer;
        }

        public override bool Equals(object obj)
        {
            return obj is BatchKey other && Equals(other);
        }
    }

    private sealed class BlockRenderCache
    {
        public readonly List<BlockBatchEntry> batchEntries = new List<BlockBatchEntry>(4);
        public int version = int.MinValue;
        public bool isValid;
    }

    private sealed class BatchRenderCache
    {
        public readonly List<Matrix4x4> matrices = new List<Matrix4x4>(64);
        public readonly List<MatrixOwner> owners = new List<MatrixOwner>(64);
    }

    private readonly struct MatrixOwner
    {
        public readonly BlockRenderCache BlockCache;
        public readonly int EntryIndex;

        public MatrixOwner(BlockRenderCache blockCache, int entryIndex)
        {
            BlockCache = blockCache;
            EntryIndex = entryIndex;
        }
    }

    private struct BlockBatchEntry
    {
        public BatchKey BatchKey;
        public int MatrixIndex;

        public BlockBatchEntry(BatchKey batchKey, int matrixIndex)
        {
            BatchKey = batchKey;
            MatrixIndex = matrixIndex;
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
