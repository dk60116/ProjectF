using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

public sealed partial class PortableItemRenderer
{
    private readonly CameraRenderCulling itemCameraCulling = new CameraRenderCulling();
    private readonly Dictionary<Vector2Int, DeferredConveyorRenderChunk> deferredConveyorRenderChunks =
        new Dictionary<Vector2Int, DeferredConveyorRenderChunk>();
    private readonly List<BlockHandle> visibleDeferredConveyorBlocks = new List<BlockHandle>(128);

    public int LastStaticCacheRebuilds { get; private set; }
    public int DeferredStaticRenderBlocks
    {
        get
        {
            int count = 0;
            foreach (DeferredConveyorRenderChunk chunk in deferredConveyorRenderChunks.Values)
                count += chunk.Blocks.Count;
            return count;
        }
    }

    private void DeferConveyorBlock(BlockHandle handle, Bounds bounds)
    {
        if (!deferredConveyorRenderChunks.TryGetValue(handle.ChunkCoordinate, out DeferredConveyorRenderChunk chunk))
        {
            chunk = new DeferredConveyorRenderChunk { WorldBounds = bounds };
            deferredConveyorRenderChunks.Add(handle.ChunkCoordinate, chunk);
        }
        // Bounds may be conservative after removal, but must never omit pending blocks.
        chunk.WorldBounds.Encapsulate(bounds);
        chunk.Blocks.Add(handle);
    }

    private void RemoveDeferredConveyorBlock(BlockHandle handle)
    {
        if (!deferredConveyorRenderChunks.TryGetValue(handle.ChunkCoordinate, out DeferredConveyorRenderChunk chunk))
            return;
        chunk.Blocks.Remove(handle);
        if (chunk.Blocks.Count == 0)
            deferredConveyorRenderChunks.Remove(handle.ChunkCoordinate);
    }

    private void RefreshVisibleDeferredConveyorBlocks()
    {
        visibleDeferredConveyorBlocks.Clear();
        foreach (DeferredConveyorRenderChunk chunk in deferredConveyorRenderChunks.Values)
        {
            if (!itemCameraCulling.Intersects(chunk.WorldBounds))
                continue;
            foreach (BlockHandle handle in chunk.Blocks)
            {
                if (!TryResolveConveyorBlock(handle, out Block block)
                    || (itemCameraCulling.IsLayerVisible(block.gameObject.layer)
                        && itemCameraCulling.Intersects(CreateDynamicVirtualConveyorBlockCullBounds(block))))
                    visibleDeferredConveyorBlocks.Add(handle);
            }
        }

        // Apply outside the enumeration: rebuilding removes the pending chunk membership.
        for (int i = 0; i < visibleDeferredConveyorBlocks.Count; i++)
        {
            BlockHandle handle = visibleDeferredConveyorBlocks[i];
            if (!activeVirtualConveyorRenderBlockLookup.Contains(handle)
                || !TryResolveConveyorBlock(handle, out Block block)
                || block.HasDynamicVirtualConveyorItemVisuals())
            {
                RemoveVirtualConveyorBlockRenderCache(handle);
                continue;
            }
            RefreshVirtualConveyorBlockRenderCache(handle, block, GetOrCreateVirtualConveyorBlockRenderCache(handle));
        }
        visibleDeferredConveyorBlocks.Clear();
    }

    private sealed class DeferredConveyorRenderChunk
    {
        public Bounds WorldBounds;
        public readonly HashSet<BlockHandle> Blocks = new HashSet<BlockHandle>();
    }
}
