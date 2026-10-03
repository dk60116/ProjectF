using System.Collections.Generic;
using ProjectF.Rendering;
using UnityEngine;

public partial class TerrainGenerator
{
    // Native movement stays authoritative off screen. Only the latest state of a
    // changed cell crosses into presentation, when its chunk becomes visible.
    private sealed class BeltPublicationChunk
    {
        internal Bounds Bounds;
        internal readonly List<int> Blocks = new List<int>();
        internal readonly List<int> VisualChanges = new List<int>();
        internal bool PersistenceDirty;
    }

    private readonly List<BeltPublicationChunk> beltPublicationChunks = new List<BeltPublicationChunk>();
    private readonly Dictionary<Vector2Int, int> beltPublicationChunkIndices = new Dictionary<Vector2Int, int>();
    private readonly List<int> beltPublicationBlockChunks = new List<int>();
    private readonly List<bool> beltPublicationVisualPending = new List<bool>();
    private readonly List<bool> beltPublicationVisualActivityPending = new List<bool>();
    private int beltJobLoadedItemCount, beltJobLastRuntimeNotifications, beltJobLastVisualPublishedBlocks;
    private int beltPublicationPendingVisualBlocks;
    internal bool HasBeltJobPresentationChanges => beltPublicationPendingVisualBlocks > 0;

    private void RebuildBeltPublicationChunks()
    {
        ClearBeltPublicationChunks(false);
        for (int i = 0; i < beltJobPublicationViews.Count; i++)
        {
            Block block = beltJobPublicationViews[i];
            // All baked lanes belong to loaded runtime blocks.
            Vector2Int coordinate = block.RuntimeHandle.ChunkCoordinate;
            Bounds bounds = new Bounds(block.WorldPosition + new Vector3(0f, 1.25f, 0f),
                new Vector3(2.25f, 2.5f, 2.25f));
            if (!beltPublicationChunkIndices.TryGetValue(coordinate, out int chunkIndex))
            {
                chunkIndex = beltPublicationChunks.Count;
                beltPublicationChunkIndices.Add(coordinate, chunkIndex);
                beltPublicationChunks.Add(new BeltPublicationChunk { Bounds = bounds });
            }
            BeltPublicationChunk chunk = beltPublicationChunks[chunkIndex];
            chunk.Bounds.Encapsulate(bounds);
            chunk.Blocks.Add(i);
            // Drop managed count mirrors once at ownership transfer. Native count
            // changes only on external insert/remove; internal transfers conserve it.
            RemoveCachedConveyorBlockItemCount(block.RuntimeHandle);
            beltPublicationBlockChunks.Add(chunkIndex);
            beltPublicationVisualPending.Add(false);
            beltPublicationVisualActivityPending.Add(false);
        }
        foreach (BeltPublicationChunk chunk in beltPublicationChunks)
            chunk.VisualChanges.Capacity = chunk.Blocks.Count;
    }

    private void MarkBeltPublicationChunkChanged(int index, bool refreshActivity)
    {
        BeltPublicationChunk chunk = beltPublicationChunks[beltPublicationBlockChunks[index]];
        chunk.PersistenceDirty = true;
        if (refreshActivity) beltPublicationVisualActivityPending[index] = true;
        if (beltPublicationVisualPending[index]) return;
        beltPublicationVisualPending[index] = true;
        beltPublicationPendingVisualBlocks++;
        chunk.VisualChanges.Add(index);
    }

    internal void FlushBeltJobPresentation(CameraRenderCulling culling)
    {
        beltJobLastVisualPublishedBlocks = 0;
        CompleteBeltSimulationDataDependency(true);
        for (int c = 0; c < beltPublicationChunks.Count; c++)
        {
            BeltPublicationChunk chunk = beltPublicationChunks[c];
            if (chunk.VisualChanges.Count == 0 || (culling != null && !culling.Intersects(chunk.Bounds))) continue;
            // A deterministic order also makes dense, sparse and coalesced flushes
            // comparable in the harness. Culled chunks never enter this loop.
            chunk.VisualChanges.Sort();
            for (int i = 0; i < chunk.VisualChanges.Count; i++)
            {
                int index = chunk.VisualChanges[i];
                Block block = beltJobPublicationViews[index];
                CapturePublishedBeltVisualState(block, out int itemCount, out bool dynamic);
                block.NotifyBeltJobVisualPublished(itemCount, dynamic, beltPublicationVisualActivityPending[index]);
                beltPublicationVisualPending[index] = false;
                beltPublicationPendingVisualBlocks--;
                beltPublicationVisualActivityPending[index] = false;
                beltJobLastVisualPublishedBlocks++;
            }
            chunk.VisualChanges.Clear();
        }
    }

    private void ExpandBeltJobDirtyPersistenceBlocks()
    {
        // Called at checkpoint/topology boundaries, not for every item handoff.
        // Clear before capture so mutations between incremental save yields remain
        // dirty for the next checkpoint, including an empty source cell.
        foreach (BeltPublicationChunk chunk in beltPublicationChunks)
        {
            if (!chunk.PersistenceDirty) continue;
            chunk.PersistenceDirty = false;
            foreach (int index in chunk.Blocks) MarkPersistenceStateDirty(beltJobPublicationViews[index]);
        }
    }

    private void MarkAllBeltPublicationChunksPersistenceDirty()
    {
        foreach (BeltPublicationChunk chunk in beltPublicationChunks) chunk.PersistenceDirty = true;
    }

    private void ClearBeltPublicationChunks(bool clearItemCount = true)
    {
        beltPublicationChunks.Clear();
        beltPublicationChunkIndices.Clear();
        beltPublicationBlockChunks.Clear();
        beltPublicationVisualPending.Clear();
        beltPublicationVisualActivityPending.Clear();
        beltJobLastRuntimeNotifications = beltJobLastVisualPublishedBlocks = 0;
        beltPublicationPendingVisualBlocks = 0;
        if (clearItemCount) beltJobLoadedItemCount = 0;
    }
}
