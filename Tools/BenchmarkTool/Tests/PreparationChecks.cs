using System.Collections;

// Only Unity objects, pose/material boundaries and the clock are doubled.
public readonly record struct Vector2Int(int x, int y);
public readonly record struct Vector2(float x, float y);
public readonly record struct Vector3(float x, float y, float z) { public static Vector3 up => new(0, 1, 0); }
public readonly record struct Color(float r, float g, float b, float a);
public enum TerrainBiome { Dirt }
public sealed class ChunkSurfaceBuildData
{
    public Vector2Int origin;
    public readonly List<Vector3> vertices = new(), normals = new();
    public readonly List<Vector2> uvs = new();
    public readonly List<Color> colors = new();
    public readonly List<int>[] trianglesByBiome;
    public ChunkSurfaceBuildData(int types, int size) => trianglesByBiome = Enumerable.Range(0, types).Select(_ => new List<int>()).ToArray();
    public void Reset(Vector2Int nextOrigin, object input)
    { origin = nextOrigin; vertices.Clear(); normals.Clear(); uvs.Clear(); colors.Clear(); foreach (var list in trianglesByBiome) list.Clear(); }
}
public partial class SurfaceProbe
{
    private ChunkSurfaceBuildData reusableChunkSurfaceBuildData;
    private const int GeneratedSurfaceMaterialCount = 7;
    private float generatedSurfaceYOffset = 3.5f, waterSurfaceDepth = 1;
    private static float GetBiomeSurfaceY(TerrainBiome biome, float y, float water) => y;
    private static int GetGeneratedSurfaceTriangleBucket(TerrainBiome biome) => 1;
    public ChunkSurfaceBuildData Build(int size, Vector2Int origin) => BuildBenchmarkChunkSurface(origin, size);
    public void Return(ChunkSurfaceBuildData data) => reusableChunkSurfaceBuildData = data;
}
public static class ProbeClock
{
    public const long Frequency = 1000;
    public static long Now;
    public static long GetTimestamp() => Now++;
}
public static class Time { public static int frameCount = 1; }
public readonly record struct MapObjectHandle(int TypeId) { public bool IsValid => TypeId >= 0; }
public sealed class InstallationObject
{
    public static readonly List<InstallationObject> All = new();
    public bool isActiveAndEnabled = true;
    public MapObjectHandle RuntimeMapObjectHandle = new(1);
    public static void CopyActiveInstances(List<InstallationObject> items) { items.Clear(); items.AddRange(All); }
}
public enum VirtualObjectKind { Installation }
public class VirtualObjectRecord
{
    public VirtualObjectKind kind;
    public bool HasAttachedView;
    public BlockStateStore.InstallationSaveState installationState;
    public MapObjectHandle mapObjectHandle = new(1);
    public int itemId = 1;
}
public sealed class VirtualWorld
{
    public int InstallationVersion = 10;
    public readonly List<VirtualObjectRecord> Records = new();
    public bool IsHandleAlive(MapObjectHandle handle) => true;
    public void CopyInstallationRecords(List<VirtualObjectRecord> output, bool dataOnly) { output.Clear(); output.AddRange(Records); }
}
public sealed class StaticMapObjectTypeHost
{
    public int ItemId = 1, InstanceCount, Begins, Completes, Aborts;
    public void BeginSynchronization() { Begins++; InstanceCount = 0; }
    public bool SynchronizeInstance(InstallationObject source, MapObjectHandle handle) { InstanceCount++; return true; }
    public bool SynchronizeRecord(VirtualObjectRecord source) { InstanceCount++; return true; }
    public void CompleteSynchronization() => Completes++;
    public void AbortSynchronization() => Aborts++;
}
public partial class RendererProbe
{
    private readonly VirtualWorld virtualWorld = new();
    private readonly object itemManager = new();
    private readonly List<VirtualObjectRecord> dataOnlyInstallations = new();
    private readonly List<StaticMapObjectTypeHost> hostScratch = new();
    private readonly List<int> emptyHostItemIds = new();
    private readonly HashSet<int> rejectedTypeIds = new();
    private readonly Dictionary<int, StaticMapObjectTypeHost> hostsByItemId = new() { [1] = new() };
    private int cachedInstallationVersion = -1, synchronizationCount, lastSynchronizationFrame,
        lastSynchronizedDataOnlyInstallationCount;
    public long BenchmarkSyncDone { get; private set; }
    public long BenchmarkSyncTotal { get; private set; }
    private void ResolveDependencies() { }
    private void InvalidateSyncVersions() { cachedInstallationVersion = -1; }
    private void CopyHostsToScratch() { hostScratch.Clear(); hostScratch.AddRange(hostsByItemId.Values); }
    private bool TryGetOrCreateHost(int id, out StaticMapObjectTypeHost host) => hostsByItemId.TryGetValue(id, out host);
    private void RemoveHost(int id) => hostsByItemId.Remove(id);
    public StaticMapObjectTypeHost Host => hostsByItemId[1];
    public int CachedVersion => cachedInstallationVersion;
    public void AddData(int count) { for (int i = 0; i < count; i++) virtualWorld.Records.Add(new()); }
}
public sealed class PipeRuntimeRecord { public int Id; }
public class FakeBatch
{
    public readonly List<int> Ids = new();
    public void Clear() => Ids.Clear();
}
public class PipeOwner { public readonly List<int> Entries = new(); }
public partial class PipeProbe
{
    private bool disposed = false, bodyDirty = true;
    private readonly FakeBatch bodyBatches = new();
    private readonly PipeOwner bodyOwner = new();
    private readonly Dictionary<int, PipeRuntimeRecord> recordsByStorageKey = new();
    public bool Dirty => bodyDirty;
    public int Count => bodyBatches.Ids.Count;
    public void Add(int count) { for (int i = 0; i < count; i++) recordsByStorageKey.Add(i, new() { Id = i }); }
    private void AddRecordParts(PipeRuntimeRecord record, bool fluid, FakeBatch batches, PipeOwner owner, List<int> entries)
    { batches.Ids.Add(record.Id); entries.Add(record.Id); }
}
internal static class PreparationChecks
{
    private static int checks;
    private static void Require(bool valid, string description) { checks++; if (!valid) throw new Exception(description); }
    private static int Drain(IEnumerator work)
    { int yields = 0; using (work as IDisposable) { while (work.MoveNext()) yields++; } return yields; }
    private static void Main()
    {
        var surface = new SurfaceProbe();
        foreach (int size in new[] { 4, 16, 32 })
        {
            var data = surface.Build(size, new(-64, 128));
            Require(data.vertices.Count == 4 && data.trianglesByBiome[1].Count == 6, "single quad regardless of chunk size");
            Require(data.vertices.All(v => v.y == 3.5f) && data.normals.All(v => v == Vector3.up), "flat dirt preserves height and normals");
            Require(data.colors.All(v => v == new Color(0, 1, 0, 0)), "dirt blend channel");
            Require(data.vertices.Min(v => v.x) == -.5f && data.vertices.Max(v => v.x) == size - .5f, "surface covers tile boundaries");
            Require(data.uvs.SequenceEqual(data.vertices.Select(v => new Vector2(v.x, v.z))), "existing local texture coordinates");
            var a = data.vertices[0]; var b = data.vertices[2]; var c = data.vertices[1];
            Require((b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z) > 0, "triangle faces upward");
            surface.Return(data);
            Require(ReferenceEquals(data, surface.Build(size, new(32, -16))), "reuse scratch surface storage");
        }
        InstallationObject.All.Clear();
        for (int i = 0; i < 50000; i++) InstallationObject.All.Add(new());
        var renderer = new RendererProbe(); renderer.AddData(100000);
        var sync = renderer.PrepareBenchmarkPresentation();
        int yields = 0; long last = -1;
        using (sync as IDisposable)
            while (sync.MoveNext())
            {
                Require(renderer.BenchmarkSyncTotal == 100000 && renderer.BenchmarkSyncDone > last, "render stage progress advances");
                last = renderer.BenchmarkSyncDone; yields++;
            }
        Require(yields > 0 && renderer.Host.InstanceCount == 100000 && renderer.BenchmarkSyncDone == 100000, "100000 render records sliced without omissions");
        Require(renderer.CachedVersion == 10 && renderer.Host.Completes == 1 && renderer.Host.Aborts == 0, "render commit publishes completed version only");
        var cancelled = new RendererProbe(); cancelled.AddData(10);
        var work = cancelled.PrepareBenchmarkPresentation();
        Require(work.MoveNext(), "render cancellation checkpoint"); ((IDisposable)work).Dispose();
        Require(cancelled.CachedVersion == -1 && cancelled.Host.Aborts == 1, "render cancellation aborts partial hosts and invalidates cache");
        Require(Drain(cancelled.PrepareBenchmarkPresentation()) > 0 && cancelled.Host.InstanceCount == 10, "render can rebuild after cancellation");
        var pipes = new PipeProbe(); pipes.Add(100000);
        Require(Drain(pipes.PrepareBenchmarkPresentation()) > 0 && pipes.Count == 100000 && !pipes.Dirty, "100000 pipe records render in slices");
        Require(Drain(pipes.PrepareBenchmarkPresentation()) == 0, "completed pipe render is cached");
        var cancelledPipes = new PipeProbe(); cancelledPipes.Add(100);
        var pipeWork = cancelledPipes.PrepareBenchmarkPresentation(); Require(pipeWork.MoveNext(), "pipe cancellation checkpoint");
        ((IDisposable)pipeWork).Dispose(); Require(cancelledPipes.Dirty, "cancelled pipe batches stay dirty");
        Drain(cancelledPipes.PrepareBenchmarkPresentation()); Require(cancelledPipes.Count == 100 && !cancelledPipes.Dirty, "pipe recovery replaces partial batches");
        Console.WriteLine($"PASS: {checks} benchmark flat surface and sliced presentation checks");
    }
}

public class BlockStateStore
{
    public class InstallationSaveState { }
    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state) => default;
}
public class MiningWorld
{
    public static MiningWorld Current => null;
    public bool TryGet(Vector2Int coordinate, out object target) { target = null; return false; }
}
