using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using ProjectF.Benchmark;
using UnityEngine;

public partial class TerrainGenerator
{
    private int benchmarkPlacementDepth;
    internal long BenchmarkTerrainChunksDone { get; private set; }
    internal long BenchmarkTerrainChunksTotal { get; private set; }
    internal bool IsBenchmarkPlacementInProgress => benchmarkPlacementDepth > 0;
    internal IDisposable BeginBenchmarkPlacementUpdate() => new BenchmarkPlacementUpdate(this);
    internal IEnumerator PrepareBenchmarkConveyorPresentation() => EnsureConveyorWorld().PrepareBenchmarkPresentation();
    internal IEnumerator PrepareBenchmarkPipePresentation() => EnsurePipeWorld().PrepareBenchmarkPresentation();

    private sealed class BenchmarkPlacementUpdate : IDisposable
    {
        private TerrainGenerator owner;
        private readonly ConveyorWorld world;
        private readonly bool wasPaused;
        private readonly float generationBudget;
        internal BenchmarkPlacementUpdate(TerrainGenerator terrain)
        {
            owner = terrain;
            wasPaused = MapObjectTickManager.SimulationPaused;
            generationBudget = terrain.chunkGenerationFrameTimeBudgetMilliseconds;
            world = terrain.EnsureConveyorWorld();
            MapObjectTickManager.SetSimulationPaused(true);
            terrain.chunkGenerationFrameTimeBudgetMilliseconds = BenchmarkLayout.WorkSliceMilliseconds;
            world.BeginBulkUpdate();
            terrain.benchmarkPlacementDepth++;
            InputOutputModule.BeginRuntimePipeTopologyBatch();
            UtilityPole.BeginTopologyRefreshBatch();
            terrain.BeginConveyorRuntimeRefreshBatch();
        }
        public void Dispose()
        {
            if (owner == null) return;
            var terrain = owner; owner = null;
            try { terrain.EndConveyorRuntimeRefreshBatch(); }
            finally
            {
                try { InputOutputModule.EndRuntimePipeTopologyBatch(); }
                finally
                {
                    try { UtilityPole.EndTopologyRefreshBatch(false); }
                    finally
                    {
                        world.EndBulkUpdate();
                        terrain.benchmarkPlacementDepth--;
                        terrain.chunkGenerationFrameTimeBudgetMilliseconds = generationBudget;
                        MapObjectTickManager.SetSimulationPaused(wasPaused);
                    }
                }
            }
        }
    }

    internal BenchmarkInstallationTemplate CreateBenchmarkConveyorTemplate(
        InstallationPlacementController placement, ConveyorBelt source, int turns)
    {
        var template = CreateBenchmarkInstallationTemplate(placement, source, turns);
        if (template.State.occupiedCoordinates.Count != 1 || template.State.occupiedCoordinates[0] != Vector2Int.zero)
            throw new InvalidOperationException("square loops require a single-cell conveyor belt");
        return template;
    }

    internal bool TryPlaceBenchmarkConveyor(BenchmarkInstallationTemplate template, Vector2Int coordinate)
        => TryPlaceBenchmarkInstallation(null, template, coordinate);
    internal IEnumerator EnsureBenchmarkTerrainArea(Vector2Int minimum, Vector2Int maximum)
    {
        int size = Mathf.Max(4, chunkSize);
        int minX = Mathf.FloorToInt(minimum.x / (float)size), minY = Mathf.FloorToInt(minimum.y / (float)size);
        int maxX = Mathf.FloorToInt(maximum.x / (float)size), maxY = Mathf.FloorToInt(maximum.y / (float)size);
        BenchmarkTerrainChunksDone = 0;
        BenchmarkTerrainChunksTotal = ((long)maxX - minX + 1) * ((long)maxY - minY + 1);
        int queued = 0;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var coordinate = new Vector2Int(x, y);
                if (loadedChunks.ContainsKey(coordinate) && !IsChunkGenerationActive(coordinate))
                { BenchmarkTerrainChunksDone++; continue; }
                if (IsChunkGenerationActive(coordinate))
                {
                    while (IsChunkGenerationActive(coordinate)) yield return null;
                    if (!loadedChunks.ContainsKey(coordinate)) throw new InvalidOperationException("terrain generation failed at " + coordinate);
                    BenchmarkTerrainChunksDone++; continue;
                }
                EnsureChunkActivationStorageCapacity(loadedChunks.Count + ++queued);
                QueueChunkGeneration(coordinate, size);
                EnsureChunkGenerationProcessing();
                if (queued == 64)
                {
                    var wait = WaitBenchmarkTerrainBatch(queued);
                    using (wait as IDisposable) { while (wait.MoveNext()) yield return null; }
                    queued = 0;
                }
            }
        var remaining = WaitBenchmarkTerrainBatch(queued);
        using (remaining as IDisposable) { while (remaining.MoveNext()) yield return null; }
    }

    private IEnumerator WaitBenchmarkTerrainBatch(int queued)
    {
        int completedBefore = CompletedChunkGenerationCount;
        long readyBefore = BenchmarkTerrainChunksDone;
        while (IsChunkStreamingBusy)
        {
            BenchmarkTerrainChunksDone = readyBefore + Math.Min(queued, CompletedChunkGenerationCount - completedBefore);
            yield return null;
        }
        BenchmarkTerrainChunksDone = readyBefore + queued;
    }

    internal void ClearBenchmarkMapObjects()
    {
        // Reuse the authoritative reload boundary, including data-only belts, pipes,
        // arms and saved records. A fresh payload cannot resurrect cleared objects.
        LoadFromSaveState(CaptureTerrainSaveState(), new MapSaveData());
    }

    internal IEnumerator ClearBenchmarkBelts()
    {
        // Cancelling during removal must finish the reset: saved, runtime and
        // block indices cannot be left at different stages of the same clear.
        var clear = ClearBenchmarkBeltsCore();
        bool completed = false;
        try
        {
            while (clear.MoveNext()) yield return null;
            completed = true;
        }
        finally
        {
            try { if (!completed) while (clear.MoveNext()) { } }
            finally { (clear as IDisposable)?.Dispose(); }
        }
    }

    private IEnumerator ClearBenchmarkBeltsCore()
    {
        ClearAllBeltItems(out _, out _);
        EnsureConveyorWorld().ClearRecords();
        var ids = new HashSet<int>();
        var manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (manager != null)
            foreach (var item in manager.ItemDefinitions)
                if (item != null && item.mapObject is ConveyorBelt) ids.Add(item.id);
        EnsureResourceStateStore();
        var removal = resourceStateStore.RemoveConveyorInstallations(ids);
        using (removal as IDisposable) { while (removal.MoveNext()) yield return null; }
        var blocks = new List<Block>(); CopyLoadedBlocks(blocks);
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            // A bridge belt may share a coordinate with a pipe or another object.
            // Removing the belt record must not clear that underlying object.
            if (block != null && block.MapObject is ConveyorBelt) block.SetMapObject(null);
            if ((i & 63) == 63 && BenchmarkLayout.IsWorkSliceExpired(started, Stopwatch.GetTimestamp(), Stopwatch.Frequency))
            { yield return null; started = Stopwatch.GetTimestamp(); }
        }
        var live = new List<InstallationObject>(); InstallationObject.CopyActiveInstances(live);
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i] is ConveyorBelt belt && !belt.ExcludeFromTerrainPersistence && belt.TryGetPlacementRuntime(out _, out _))
                ReleaseInstallationObject(belt);
            if ((i & 63) == 63 && BenchmarkLayout.IsWorkSliceExpired(started, Stopwatch.GetTimestamp(), Stopwatch.Frequency))
            { yield return null; started = Stopwatch.GetTimestamp(); }
        }
        MarkConveyorNetworkDirty();
    }
}
