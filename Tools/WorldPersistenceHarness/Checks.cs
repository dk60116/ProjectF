using System.Collections;
using System.IO.Compression;
using ProjectF.Simulation;
using UnityEngine;

internal static class Checks
{
    private static int passed;
    private static void Require(bool value, string name)
    { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static void Throws(Action action, string name)
    {
        bool threw = false;
        try { action(); } catch { threw = true; }
        Require(threw, name);
    }
    public static void Main()
    {
        Restore(); Checkpoint(); Scheduling(); AtomicSave();
        Console.WriteLine($"PASS {passed} world persistence checks; no Unity or real save slot opened");
    }
    private static void Restore()
    {
        var progress = new WorldRestoreProgress();
        Require(!progress.IsReady && !progress.IsPending, "new world cannot simulate before restoration");
        progress.Begin();
        Require(progress.IsPending && progress.Phase == WorldRestorePhase.Records, "record import closes readiness gate");
        Throws(() => progress.Complete(null), "cannot skip chunks/connections to publish ready");
        progress.RecordsRestored(); progress.BeginConnections();
        progress.Complete(() => Require(!progress.IsReady && progress.IsPending, "checkpoint executes before readiness"));
        Require(progress.IsReady, "only successful checkpoint publishes ready");

        var terrain = new TerrainGenerator();
        TerrainGenerator.Active = terrain;
        var saveManager = new SaveManager();
        int callbacks = 0;
        terrain.Begin(() =>
        {
            Require(!terrain.IsWorldReadyForPresentation && terrain.worldRestore.Phase == WorldRestorePhase.Checkpoint,
                "real terrain finalizer keeps readiness closed during external checkpoint restore");
            terrain.Order.Add("checkpoint"); callbacks++;
            Require(MapObjectTickManager.WaitingForWorldLoad && saveManager.IsLoading,
                "actual tick and SaveManager gates stay closed until external restoration returns");
        });
        terrain.IsChunkStreamingBusy = true; terrain.FinalizeWorld();
        Require(callbacks == 0 && terrain.Order.Count == 0, "unfinished chunk queue blocks finalization");
        terrain.IsChunkStreamingBusy = false; terrain.FinalizeWorld(); terrain.FinalizeWorld();
        Require(callbacks == 1 && terrain.IsWorldReadyForPresentation, "terrain finalization is single-shot");
        Require(!MapObjectTickManager.WaitingForWorldLoad && !saveManager.IsLoading,
            "actual tick and operation gates reopen after all restoration succeeds");
        Require(string.Join(",", terrain.Order) == "belts,pipes,expand,items,register,views,total,checkpoint",
            "connections/items/registration/checkpoint retain explicit dependency order");
        terrain.Begin(() => throw new Exception("checkpoint failed"));
        Throws(terrain.FinalizeWorld, "checkpoint restoration failure propagates");
        Require(!terrain.IsWorldReadyForPresentation && terrain.worldRestore.Phase == WorldRestorePhase.Failed,
            "checkpoint failure can never enable simulation");
        Require(MapObjectTickManager.WaitingForWorldLoad && !saveManager.IsLoading,
            "failed restore keeps ticks blocked but releases the operation gate for an explicit retry");
        terrain.FinalizeWorld();
        terrain.Begin(null); terrain.ThrowInConnections = true;
        Throws(terrain.FinalizeWorld, "connection restoration failure propagates");
        Require(!terrain.IsWorldReadyForPresentation && terrain.worldRestore.Failure.Message == "topology",
            "connection failure remains visible and cannot publish ready");
        terrain.ThrowInConnections = false; terrain.Begin(null); terrain.FinalizeWorld();
        Require(terrain.IsWorldReadyForPresentation && terrain.worldRestore.Failure == null, "explicit retry resets failed restoration");
        terrain.Begin(null); terrain.RemoveAllChunksForTest();
        Throws(terrain.FinalizeWorld, "empty completed generation reports an explicit restoration failure");
        Require(!terrain.IsWorldRestorePending && !terrain.IsWorldReadyForPresentation,
            "empty world cannot wait forever or enable ticks after a missing terrain definition");
        TerrainGenerator.Active = null;
    }
    private sealed class Command : ISimulationCommand
    {
        public Action<SimulationTickWorld> Body;
        public void Execute(SimulationTickWorld world) => Body(world);
    }
    private static void Checkpoint()
    {
        var terrain = new TerrainGenerator();
        Throws(() => SaveManager.Capture(terrain), "partial world cannot overwrite a save slot");
        terrain.Begin(null); terrain.FinalizeWorld(); terrain.IsChunkStreamingBusy = true;
        Throws(() => SaveManager.Capture(terrain), "streaming world cannot produce a mixed chunk snapshot");
        terrain.IsChunkStreamingBusy = false; terrain.Order.Clear();
        MapObjectTickManager.World.Enqueue(new Command { Body = world =>
        {
            Require(!world.CanCaptureCheckpoint, "Plan/Apply command execution is not a checkpoint boundary");
            Throws(() => SaveManager.Capture(terrain), "save rejects reentrant capture from a tick command");
        } });
        Throws(() => SaveManager.Capture(terrain), "unserialized pending commands cannot disappear into a checkpoint");
        MapObjectTickManager.World.Step();
        Require(MapObjectTickManager.CanCaptureCheckpoint, "finished tick with no queued commands is a valid boundary");
        SaveGameData snapshot = SaveManager.Capture(terrain);
        Require(snapshot.map.Items == 17 && terrain.Order[0] == "beltCheckpoint" && terrain.Order[1] == "mapCheckpoint",
            "actual capture prepares native belt writes before reading the map");
        Require(snapshot.simulationTick == 1 && snapshot.nextInstallationSimulationId == 101,
            "checkpoint keeps simulation tick and ID allocator from the same synchronous capture");
        terrain.LiveItems = 99;
        Require(snapshot.map.Items == 17, "capture orchestration hands the writer its returned DTO (not the live terrain)");
    }
    private static IEnumerator Work(List<int> order, Vector2Int cell, double milliseconds)
    {
        Time.realtimeSinceStartupAsDouble += milliseconds / 1000;
        order.Add(cell.x);
        yield break;
    }
    private static IEnumerator StagedWork(Action onDisposed)
    {
        try { Time.realtimeSinceStartupAsDouble += .002; yield return null; Time.realtimeSinceStartupAsDouble += .004; }
        finally { onDisposed(); }
    }
    private static IEnumerator FailedWork() { yield return null; throw new IOException("chunk"); }
    private static void Scheduling()
    {
        var host = new MonoBehaviour(); var order = new List<int>();
        int cleanups = 0, failures = 0;
        var scheduler = new TerrainChunkStreamingScheduler(host, _ => false, _ => true,
            (cell, size) => order.Add(cell.x), (cell, size, allowYield) => Work(order, cell, 2), default,
            () => cleanups++, () => 5, error => failures++);
        for (int i = 0; i < 10; i++) scheduler.QueueGeneration(new(i, 0), 8);
        scheduler.QueueGeneration(new(0, 0), 8);
        Require(scheduler.TotalGenerationCount == 10, "duplicate queue coordinates do not duplicate entities");
        scheduler.EnsureGenerationProcessing();
        Require(order.Count == 0 && scheduler.IsBusy, "initial yield preserves tracked coroutine handle before any work");
        host.Frame();
        Require(order.Count == 3 && scheduler.CompletedGenerationCount == 3,
            "shared five-ms budget batches three two-ms chunks (one indivisible operation may overrun)");
        host.Frame(); Require(order.Count == 6, "budget is renewed per frame, not per completed chunk");
        host.Frame(); host.Frame();
        Require(!scheduler.IsBusy && order.SequenceEqual(Enumerable.Range(0, 10)) && cleanups == 10 && failures == 0,
            "batching preserves all chunks, queue order and exactly-once cleanup");
        Require(scheduler.GenerationProgress == 1, "successful queue reports full progress");
        scheduler.QueueGeneration(new(12, 0), 8); scheduler.ProcessQueuedGenerationsImmediate();
        Require(scheduler.TotalGenerationCount == 1 && scheduler.CompletedGenerationCount == 1 && !scheduler.IsBusy,
            "new batch and immediate editor path retain their lifecycle");

        int disposals = 0;
        host = new MonoBehaviour { DisposeWhenStopped = false };
        scheduler = new TerrainChunkStreamingScheduler(host, _ => false, _ => true, (_, _) => { },
            (_, _, _) => StagedWork(() => disposals++), default, () => { }, () => 5, _ => failures++);
        scheduler.QueueGeneration(new(1, 1), 8); scheduler.EnsureGenerationProcessing(); host.Frame();
        Require(scheduler.IsGenerationActive(new(1, 1)) && scheduler.CompletedGenerationCount == 0,
            "nested job yield is honored without prematurely completing its chunk");
        scheduler.Clear();
        Require(disposals == 1 && !scheduler.IsBusy && scheduler.TotalGenerationCount == 0,
            "cancelling explicitly disposes iterator resources even when the coroutine driver does not");
        scheduler.QueueGeneration(new(2, 2), 8); scheduler.EnsureGenerationProcessing(); host.Frame(); host.Frame();
        Require(disposals == 2 && !scheduler.IsBusy, "cleared scheduler can run again without a stale coroutine handle");

        var progress = new WorldRestoreProgress(); progress.Begin(); progress.RecordsRestored();
        host = new MonoBehaviour();
        scheduler = new TerrainChunkStreamingScheduler(host, _ => false, _ => true, (_, _) => { },
            (_, _, _) => FailedWork(), default, () => { }, () => 5, progress.Fail);
        scheduler.QueueGeneration(new(3, 3), 8); scheduler.EnsureGenerationProcessing(); host.Frame();
        Throws(host.Frame, "chunk iterator exception propagates");
        Require(progress.Phase == WorldRestorePhase.Failed && !scheduler.IsBusy && scheduler.CompletedGenerationCount == 0,
            "failed chunk cannot count as completed or leave a stale active coroutine");

        progress.Begin(); progress.RecordsRestored(); host = new MonoBehaviour();
        scheduler = new TerrainChunkStreamingScheduler(host, _ => false, _ => true, (_, _) => { },
            (cell, _, _) => Work(new List<int>(), cell, 0), default,
            () => throw new IOException("cleanup"), () => 5, progress.Fail);
        scheduler.QueueGeneration(new(4, 4), 8); scheduler.EnsureGenerationProcessing();
        Throws(host.Frame, "cleanup failure propagates through the same restoration failure boundary");
        Require(progress.Phase == WorldRestorePhase.Failed && !scheduler.IsBusy && scheduler.CompletedGenerationCount == 0,
            "cleanup failure clears tracking without falsely counting a successful chunk");
    }
    private static int ReadPayload(string path)
    {
        using var stream = File.OpenRead(path);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        Require(reader.ReadString() == "PF_SAVE" && reader.ReadInt32() == 62, "atomic publication keeps the existing format header");
        return reader.ReadInt32();
    }
    private static void AtomicSave()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ProjectF-SaveAtomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "probe.pfsave");
        try
        {
            var data = new SaveGameData { map = new() { Items = 11 } };
            SaveGameBinarySerializer.WriteToFile(path, data);
            Require(ReadPayload(path) == 11, "first save publishes a complete compressed file");
            data.map.Items = 22; SaveGameBinarySerializer.WriteToFile(path, data);
            Require(ReadPayload(path) == 22, "existing save is replaced without delete-then-move");
            SaveGameBinarySerializer.ThrowAfterWrite = true;
            Throws(() => SaveGameBinarySerializer.WriteToFile(path, data), "injected mid-serialization error fails the new save");
            SaveGameBinarySerializer.ThrowAfterWrite = false;
            Require(ReadPayload(path) == 22 && Directory.GetFiles(directory, "*.tmp").Length == 0,
                "failed write preserves previous slot and cleans only its own temporary file");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                data.map.Items = 33;
                Throws(() => SaveGameBinarySerializer.WriteToFile(path, data), "locked Windows destination rejects replacement");
            }
            Require(ReadPayload(path) == 22 && Directory.GetFiles(directory, "*.tmp").Length == 0,
                "publication failure also leaves the previous save intact");
        }
        finally
        {
            SaveGameBinarySerializer.ThrowAfterWrite = false;
            File.Delete(path); Directory.Delete(directory); // Only this test's known file and empty directory.
        }
    }
}
