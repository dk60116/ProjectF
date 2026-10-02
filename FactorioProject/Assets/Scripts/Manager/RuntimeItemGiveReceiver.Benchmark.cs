using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProjectF.Benchmark;
using UnityEngine;

public sealed partial class RuntimeItemGiveReceiver
{
    private Coroutine benchmarkRoutine;
    private IEnumerator benchmarkWork;
    private long benchmarkDone, benchmarkTotal;
    private long benchmarkStageDone, benchmarkStageTotal;
    private int benchmarkJobId;
    private string benchmarkResult = "Ready";
    private bool benchmarkBusy;
    private long benchmarkStartedAt;
    private double benchmarkElapsedSeconds;

    private void FinishBenchmarkJob(string result)
    {
        benchmarkElapsedSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - benchmarkStartedAt)
            / (double)System.Diagnostics.Stopwatch.Frequency;
        benchmarkResult = result; benchmarkBusy = false; benchmarkRoutine = null; benchmarkWork = null;
        cachedStatusWorldStatsTime = float.NegativeInfinity;
    }

    private ToolResult ProcessBenchmarkRequest(string line)
    {
        if (!BenchmarkCommand.TryParse(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries), out var command, out var error))
            return ToolResult.Error(0, 0, error);
        if (command.Action == BenchmarkAction.Catalog)
            return ToolResult.Success(0, 0, 0, 0, 0, 0, "benchmark catalog", BuildBenchmarkCatalog());
        if (command.Action == BenchmarkAction.Status)
        {
            var activeTerrain = TerrainGenerator.ResolveActive();
            string metrics = string.Format(CultureInfo.InvariantCulture,
                "fps={0:0.0} ups={1:0.0} installTotal={2} beltItems={3} seed={4} worldReady={5}", currentFps,
                MapObjectTickManager.CurrentSimulationUps, activeTerrain != null ? activeTerrain.GetInstallationItemCounts(null) : 0,
                activeTerrain != null ? activeTerrain.GetConveyorItemCount() : 0, activeTerrain != null ? activeTerrain.CurrentSeed : -1,
                activeTerrain != null && activeTerrain.IsWorldReadyForPresentation && !activeTerrain.IsChunkStreamingBusy ? 1 : 0);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, "benchmark status", metrics + " " + BuildBenchmarkStatusTokens());
        }
        if (command.Action == BenchmarkAction.Cancel)
        {
            if (!benchmarkBusy)
                return ToolResult.Success(0, 0, 0, 0, 0, 0, "no benchmark job is running", BuildBenchmarkStatusTokens());
            if (benchmarkRoutine != null) StopCoroutine(benchmarkRoutine);
            (benchmarkWork as IDisposable)?.Dispose(); benchmarkWork = null;
            FinishBenchmarkJob("Cancelled; already created objects remain");
            return ToolResult.Success(0, 0, 0, 0, 0, 0, benchmarkResult, BuildBenchmarkStatusTokens());
        }
        if (benchmarkBusy) return ToolResult.Error(0, 0, "benchmark job is busy; wait or cancel it");
        var terrain = TerrainGenerator.ResolveActive();
        if (terrain == null || !terrain.IsWorldReadyForPresentation || terrain.IsChunkStreamingBusy)
            return ToolResult.Error(0, 0, "world is not ready");
        if (command.Action != BenchmarkAction.Map && !terrain.IsBenchmarkMap)
            return ToolResult.Error(0, 0, "load the Seed 0 benchmark map first");
        var manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        ItemDefinition item = null;
        if (command.ItemId >= 0 && (manager == null || !manager.TryGetItemDefinitionById(command.ItemId, out item)))
            return ToolResult.Error(command.ItemId, 0, "unknown item definition");
        if ((command.Action == BenchmarkAction.Fill || command.Action == BenchmarkAction.Force && command.Enabled)
            && !BenchmarkRuntime.IsPortableItem(item)) return ToolResult.Error(command.ItemId, 0, "choose a portable item");
        List<int> randomItems = command.Action == BenchmarkAction.FillRandom ? BenchmarkRuntime.CollectPortableItemIds(manager) : null;
        if (randomItems != null && randomItems.Count == 0)
            return ToolResult.Error(0, 0, "no portable item definitions found");
        if (command.Action == BenchmarkAction.Spawn && (item == null || !(item.mapObject is InstallationObject)))
            return ToolResult.Error(command.ItemId, 0, "choose an installation map object");
        if (command.Action == BenchmarkAction.Force)
        {
            BenchmarkRuntime.SetForceWorking(command.Enabled, command.ItemId);
            return ToolResult.Success(0, 0, 0, 0, 0, 0, "benchmark force working updated", BuildBenchmarkStatusTokens());
        }
        benchmarkJobId++; benchmarkDone = 0; benchmarkTotal = command.Action == BenchmarkAction.Spawn ? command.Count : 0;
        benchmarkStageDone = benchmarkStageTotal = 0;
        benchmarkResult = "Running"; benchmarkBusy = true;
        benchmarkStartedAt = System.Diagnostics.Stopwatch.GetTimestamp(); benchmarkElapsedSeconds = 0;
        benchmarkWork = ExecuteBenchmarkJob(terrain, command, item, randomItems);
        benchmarkRoutine = StartCoroutine(RunBenchmarkJob(benchmarkWork));
        return ToolResult.Success(0, 0, 0, 0, 0, 0, "benchmark job started", BuildBenchmarkStatusTokens());
    }

    private IEnumerator RunBenchmarkJob(IEnumerator work)
    {
        // All mutations run on Unity's main thread in slices. Requests return a job ID
        // immediately, so a large job does not time out the receiver or block polling.
        yield return null;
        using (work as IDisposable)
        {
            while (true)
            {
                bool next;
                try { next = work.MoveNext(); }
                catch (Exception exception)
                {
                    FinishBenchmarkJob("Failed: " + exception.Message);
                    yield break;
                }
                if (!next) break;
                yield return work.Current;
            }
        }
        FinishBenchmarkJob("Completed");
    }

    private IEnumerator ExecuteBenchmarkJob(TerrainGenerator terrain, BenchmarkCommand command, ItemDefinition item, List<int> randomItems)
    {
        if (command.Action == BenchmarkAction.Map || command.Action == BenchmarkAction.ClearObjects)
        {
            BenchmarkRuntime.SetForceWorking(false);
            if (command.Action == BenchmarkAction.Map)
            {
                terrain.SetSeed(0);
                var player = GameManager.Instance.Player;
                if (player != null) player.transform.position = new Vector3(0f, player.transform.position.y, 0f);
                terrain.StartNewGeneratedMap(false);
            }
            else terrain.ClearBenchmarkMapObjects();
            while (!terrain.IsWorldReadyForPresentation || terrain.IsChunkStreamingBusy) yield return null;
            benchmarkDone = benchmarkTotal = 1;
            yield break;
        }
        if (command.Action == BenchmarkAction.ClearItems)
        {
            terrain.ClearAllBeltItems(out _, out _);
            benchmarkDone = benchmarkTotal = 1;
            yield break;
        }
        if (command.Action == BenchmarkAction.Fill || command.Action == BenchmarkAction.FillRandom)
        {
            var random = randomItems != null ? new System.Random() : null;
            bool wasPaused = MapObjectTickManager.SimulationPaused;
            MapObjectTickManager.SetSimulationPaused(true);
            try
            {
                // Percent means occupied belt slots, including both lanes. Replace
                // previous contents so the requested ratio ignores the old load.
                terrain.ClearAllBeltItems(out _, out _);
                var blocks = new List<Block>(); terrain.CopyLoadedBlocks(blocks);
                long slotsBefore = 0;
                for (int i = 0; i < blocks.Count; i++)
                    if (blocks[i] != null) benchmarkTotal += blocks[i].GetAvailableConveyorCapacity();
                benchmarkTotal = BenchmarkLayout.FilledSlotCount(benchmarkTotal, command.Percent);
                for (int i = 0; i < blocks.Count; i++)
                {
                    Block block = blocks[i];
                    if (block == null) continue;
                    int slots = block.GetAvailableConveyorCapacity();
                    int count = BenchmarkLayout.FillForBelt(slotsBefore, slots, command.Percent);
                    slotsBefore += slots;
                    for (int j = 0; j < count; j++)
                    {
                        int itemId = random != null ? randomItems[random.Next(randomItems.Count)] : item.id;
                        if (!block.TryAddConveyorObjectAnimatedAtPlacement(itemId, block.WorldPosition,
                                block.WorldPosition, 0f, out _, null, null, 0f, false, 0f))
                            throw new InvalidOperationException("belt slot rejected item at " + block.Coordinate);
                        benchmarkDone++;
                    }
                    if ((i & 63) == 63) yield return null;
                }
            }
            finally { MapObjectTickManager.SetSimulationPaused(wasPaused); }
            yield break;
        }
        var placement = GameManager.Instance.Player != null
            ? GameManager.Instance.Player.GetComponent<InstallationPlacementController>() : null;
        if (placement == null) placement = FindObjectOfType<InstallationPlacementController>();
        if (placement == null) throw new InvalidOperationException("placement controller not found");
        var playerPosition = GameManager.Instance.Player != null ? GameManager.Instance.Player.transform.position : placement.transform.position;
        Vector2Int center = TerrainGenerator.GetWorldBlockCoordinate(playerPosition);
        if (command.Action == BenchmarkAction.Belts)
        {
            if (!TryResolveConveyorDefinition(GameManager.Instance.ItemManger, command.ItemId, out var prototype, out _))
                throw new InvalidOperationException("choose a conveyor belt");
            benchmarkTotal = BenchmarkLayout.BeltCount(command.Count);
            using (terrain.BeginBenchmarkPlacementUpdate())
            {
                benchmarkResult = "Clearing belts";
                var removal = terrain.ClearBenchmarkBelts();
                using (removal as IDisposable) { while (removal.MoveNext()) yield return null; }
                int radius = command.Count + 2;
                var area = LoadBenchmarkTerrain(terrain, center - new Vector2Int(radius, radius), center + new Vector2Int(radius, radius));
                using (area as IDisposable) { while (area.MoveNext()) yield return null; }
                benchmarkResult = "Generating belts";
                benchmarkStageDone = benchmarkStageTotal = 0;
                var variants = new Dictionary<(Vector2Int, Vector2Int), TerrainGenerator.BenchmarkInstallationTemplate>(8);
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int ring = 1; ring <= command.Count; ring++)
                {
                    int length = BenchmarkLayout.RingLength(ring);
                    for (int index = 0; index < length; index++)
                    {
                        Vector2Int coordinate = BenchmarkCoordinate(center, ring, index);
                        Vector2Int input = BenchmarkCoordinate(center, ring, (index + length - 1) % length) - coordinate;
                        Vector2Int output = BenchmarkCoordinate(center, ring, (index + 1) % length) - coordinate;
                        if (!variants.TryGetValue((input, output), out var template))
                        {
                            if (!TryResolveConveyorPlacementVariant(placement, prototype, input, output, out var prefab, out int turns)
                                || !(prefab is ConveyorBelt belt)
                                || !belt.TryGetInputDirection(placement.GetInstalledObjectRotation(prefab, turns), out var actualInput)
                                || !belt.TryGetOutputDirection(placement.GetInstalledObjectRotation(prefab, turns), out var actualOutput)
                                || actualInput != input || actualOutput != output)
                                throw new InvalidOperationException("no exact conveyor variant for a closed loop");
                            template = terrain.CreateBenchmarkConveyorTemplate(placement, belt, turns);
                            variants.Add((input, output), template);
                        }
                        if (!terrain.TryPlaceBenchmarkConveyor(template, coordinate))
                            throw new InvalidOperationException("belt placement blocked at " + coordinate);
                        benchmarkDone++;
                        if ((benchmarkDone & 63) == 0 && BenchmarkLayout.IsWorkSliceExpired(started,
                            System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.Frequency))
                        {
                            yield return null;
                            started = System.Diagnostics.Stopwatch.GetTimestamp();
                        }
                    }
                }
                var presentation = PrepareBenchmarkPresentation(terrain);
                using (presentation as IDisposable) { while (presentation.MoveNext()) yield return null; }
            }
            yield break;
        }
        if (command.Action == BenchmarkAction.Spawn)
        {
            using (terrain.BeginBenchmarkPlacementUpdate())
            {
                var template = terrain.CreateBenchmarkInstallationTemplate(placement, (InstallationObject)item.mapObject, 0);
                var focus = placement.GetInstalledObjectFocusCoordinates(Vector2Int.zero, item, 0);
                Vector2Int min = Vector2Int.zero, max = Vector2Int.zero;
                foreach (var cell in template.FootprintOffsets) { min = Vector2Int.Min(min, cell); max = Vector2Int.Max(max, cell); }
                foreach (var cell in focus) { min = Vector2Int.Min(min, cell); max = Vector2Int.Max(max, cell); }
                int stepX = max.x - min.x + 3, stepY = max.y - min.y + 3;
                int columns = BenchmarkLayout.GridColumns(command.Count), rows = (command.Count + columns - 1) / columns;
                center += new Vector2Int(0, 4 - min.y);
                benchmarkTotal = command.Count;
                // Keep terrain preparation bounded and start installing the first
                // rows without waiting for the entire grid's terrain to exist.
                for (int firstRow = 0; firstRow < rows; firstRow += BenchmarkLayout.SpawnRowsPerBatch)
                {
                    int endRow = Math.Min(rows, firstRow + BenchmarkLayout.SpawnRowsPerBatch);
                    var load = LoadBenchmarkTerrain(terrain, center + min + new Vector2Int(-1, firstRow * stepY - 1),
                        center + max + new Vector2Int((columns - 1) * stepX + 1, (endRow - 1) * stepY + 1));
                    using (load as IDisposable) { while (load.MoveNext()) yield return null; }
                    benchmarkResult = "Generating objects";
                    benchmarkStageDone = benchmarkStageTotal = 0;
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    int endIndex = Math.Min(command.Count, endRow * columns);
                    for (int index = firstRow * columns; index < endIndex; index++)
                    {
                        var coordinate = center + new Vector2Int((index % columns) * stepX, (index / columns) * stepY);
                        if (!terrain.TryPlaceBenchmarkInstallation(placement, template, coordinate))
                            throw new InvalidOperationException("object grid placement blocked at " + coordinate);
                        benchmarkDone++;
                        if (BenchmarkLayout.IsWorkSliceExpired(started, System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.Frequency))
                        {
                            yield return null;
                            started = System.Diagnostics.Stopwatch.GetTimestamp();
                        }
                    }
                }
                var presentation = PrepareBenchmarkPresentation(terrain);
                using (presentation as IDisposable) { while (presentation.MoveNext()) yield return null; }
            }
        }
    }

    private IEnumerator LoadBenchmarkTerrain(TerrainGenerator terrain, Vector2Int min, Vector2Int max)
    {
        benchmarkResult = "Generating terrain";
        benchmarkStageDone = benchmarkStageTotal = 0;
        var work = terrain.EnsureBenchmarkTerrainArea(min, max);
        using (work as IDisposable)
        {
            while (work.MoveNext())
            {
                benchmarkStageDone = terrain.BenchmarkTerrainChunksDone;
                benchmarkStageTotal = terrain.BenchmarkTerrainChunksTotal;
                yield return null;
            }
        }
    }

    private IEnumerator PrepareBenchmarkPresentation(TerrainGenerator terrain)
    {
        benchmarkResult = "Preparing render";
        benchmarkStageDone = benchmarkStageTotal = 0;
        yield return null; // Publish the stage before starting render synchronization.
        var renderer = GameManager.Instance.GetComponent<ProjectF.MapObjects.StaticMapObjectBatchRenderer>();
        if (renderer != null)
        {
            var work = renderer.PrepareBenchmarkPresentation();
            using (work as IDisposable)
            {
                while (work.MoveNext())
                {
                    benchmarkStageDone = renderer.BenchmarkSyncDone;
                    benchmarkStageTotal = renderer.BenchmarkSyncTotal;
                    yield return null;
                }
            }
        }
        benchmarkStageDone = benchmarkStageTotal = 0;
        benchmarkResult = "Preparing belts render";
        var belts = terrain.PrepareBenchmarkConveyorPresentation();
        using (belts as IDisposable) { while (belts.MoveNext()) yield return null; }
        benchmarkResult = "Preparing pipes render";
        var pipes = terrain.PrepareBenchmarkPipePresentation();
        using (pipes as IDisposable) { while (pipes.MoveNext()) yield return null; }
    }

    private static Vector2Int BenchmarkCoordinate(Vector2Int center, int ring, int index)
    { var cell = BenchmarkLayout.RingCell(ring, index); return center + new Vector2Int(cell.X, cell.Y); }

    private string BuildBenchmarkStatusTokens() => string.Format(CultureInfo.InvariantCulture,
        "benchmarkBusy={0} benchmarkJob={1} benchmarkDone={2} benchmarkTotal={3} benchmarkForce={4} benchmarkResult={5} benchmarkProduced={6} benchmarkSpilledLiters={7:0.###} benchmarkSeconds={8:0.00} benchmarkStageDone={9} benchmarkStageTotal={10}",
        benchmarkBusy ? 1 : 0, benchmarkJobId, benchmarkDone, benchmarkTotal, BenchmarkRuntime.ForceWorking ? 1 : 0,
        Convert.ToBase64String(Encoding.UTF8.GetBytes(benchmarkResult)), BenchmarkRuntime.ProducedItems, BenchmarkRuntime.SpilledFluidLiters,
        benchmarkBusy ? (System.Diagnostics.Stopwatch.GetTimestamp() - benchmarkStartedAt) / (double)System.Diagnostics.Stopwatch.Frequency : benchmarkElapsedSeconds,
        benchmarkStageDone, benchmarkStageTotal);

    [Serializable] private sealed class BenchmarkCatalog { public List<BenchmarkCatalogItem> items = new List<BenchmarkCatalogItem>(); }
    [Serializable] private sealed class BenchmarkCatalogItem { public int id; public string name; public bool installation, conveyor, portable; }
    private static string BuildBenchmarkCatalog()
    {
        var catalog = new BenchmarkCatalog();
        var manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (manager != null && manager.ItemDefinitions != null)
            foreach (var item in manager.ItemDefinitions)
                if (item != null) catalog.items.Add(new BenchmarkCatalogItem { id = item.id, name = item.itemName,
                    installation = item.mapObject is InstallationObject, conveyor = item.mapObject is ConveyorBelt,
                    portable = BenchmarkRuntime.IsPortableItem(item) });
        return "benchmarkCatalog=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(catalog)));
    }
}
