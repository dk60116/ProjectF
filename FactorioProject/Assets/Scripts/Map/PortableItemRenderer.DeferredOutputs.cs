using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

public sealed partial class PortableItemRenderer
{
    private static GameObject cachedOutputHost;
    private static PortableItemRenderer cachedOutputRenderer;
    private const int OutputCellSize = 16, OutputMaterializeBudget = 2048;
    private readonly Dictionary<Vector2Int, OutputCell> deferredOutputCells = new Dictionary<Vector2Int, OutputCell>();
    private readonly List<Block> visibleDeferredOutputs = new List<Block>(256);
    private readonly CameraRenderCulling outputCulling = new CameraRenderCulling();
    private int deferredOutputBlocks, deferredOutputItems, lastMaterializedOutputs;
    public int DeferredOutputBlockCount => deferredOutputBlocks;
    public int DeferredOutputItemCount => deferredOutputItems;
    public int LastMaterializedOutputCount => lastMaterializedOutputs;
    internal void RemoveDeferredOutputItems(int count) => deferredOutputItems -= count;
    internal void DeferOutputBlock(Block block, Bounds bounds)
    {
        var key = OutputCellKey(block);
        if (!deferredOutputCells.TryGetValue(key, out var cell))
        {
            cell = new OutputCell { Bounds = bounds };
            deferredOutputCells.Add(key, cell);
        }
        cell.Bounds.Encapsulate(bounds);
        deferredOutputItems++;
        if (cell.Blocks.Add(block)) deferredOutputBlocks++;
    }
    internal void RemoveDeferredOutputBlock(Block block)
    {
        var key = OutputCellKey(block);
        if (!deferredOutputCells.TryGetValue(key, out var cell) || !cell.Blocks.Remove(block)) return;
        deferredOutputBlocks--;
        if (cell.Blocks.Count == 0) deferredOutputCells.Remove(key);
    }
    private static Vector2Int OutputCellKey(Block block) => new Vector2Int(
        Mathf.FloorToInt(block.Coordinate.x / (float)OutputCellSize), Mathf.FloorToInt(block.Coordinate.y / (float)OutputCellSize));
    private void RefreshDeferredOutputPresentation()
    {
        ProjectF.Diagnostics.DeferredOutputTiming.Flush();
        using var sample = MapObjectTickProfiler.SampleNamed("ItemOutput", nameof(PortableItemRenderer), "Deferred Output Materialize");
        outputCulling.Update(mainCamera);
        visibleDeferredOutputs.Clear();
        // Cell bounds include entire flight paths, so a source outside the destination cell is retained.
        foreach (var cell in deferredOutputCells.Values)
        {
            if (!outputCulling.Intersects(cell.Bounds)) continue;
            foreach (var block in cell.Blocks)
                if (block == null || outputCulling.Intersects(block.DeferredOutputBounds)) visibleDeferredOutputs.Add(block);
        }
        lastMaterializedOutputs = 0;
        for (int i = 0; i < visibleDeferredOutputs.Count; i++)
        {
            var block = visibleDeferredOutputs[i];
            if (block == null) { RemoveDeferredOutputBlock(block); continue; }
            lastMaterializedOutputs += block.MaterializeDeferredOutputs(OutputMaterializeBudget - lastMaterializedOutputs);
            if (lastMaterializedOutputs >= OutputMaterializeBudget) break;
        }
        visibleDeferredOutputs.Clear();
    }
    private sealed class OutputCell
    {
        internal Bounds Bounds;
        internal readonly HashSet<Block> Blocks = new HashSet<Block>(64);
    }
}
