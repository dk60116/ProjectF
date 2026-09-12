using System;
using System.Collections.Generic;
using ProjectF.FluidTransport;

public static class Mathf
{
    public static float Min(float a, float b) => Math.Min(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static int FloorToInt(float v) => (int)Math.Floor(v);
}
public static class Time
{
    public static double timeAsDouble;
    public static float time => (float)timeAsDouble;
    public static float unscaledTime => time;
}
public static class MapObjectTickManager
{
    public const int DefaultSimulationTicksPerSecond = 60;
    public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
    public static long CurrentSimulationTick => (long)Math.Round(
        Time.timeAsDouble * DefaultSimulationTicksPerSecond,
        MidpointRounding.AwayFromZero);
    public static double CurrentSimulationTimeSeconds => Time.timeAsDouble;
}
public static class MapClimate
{
    public static float CurrentTemperatureCelsius => 20;
    public static float CurrentWaterTemperatureCelsius => 20;
}
public readonly record struct Vector2Int(int x, int y)
{
    public static Vector2Int zero => new(0, 0);
    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    public static Vector2Int operator -(Vector2Int a) => new(-a.x, -a.y);
}
public struct Vector3 { }
public struct Quaternion { public static Quaternion identity => default; }
public class InstallationObject
{
    public float AvailableFluidStorageLiters;
    public bool TryAddFluidLiters(int id, float liters, float temperature, out float accepted)
    {
        accepted = Math.Min(liters, AvailableFluidStorageLiters);
        AvailableFluidStorageLiters -= accepted;
        return accepted > 0;
    }
}
public partial class InputOutputModule
{
    private FluidOutputRateMeter fluidOutputRateMeter;
    public bool isActiveAndEnabled = true;
    public Vector2Int OutputDirection = new(-1, 0);
    public readonly List<Vector2Int> runtimeOutputCoordinates = new() { new(0, 0) };
    public readonly List<InstallationObject> cachedFluidOutputStorages = new();
    public static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeAreaCoordinates = new();
    public void Report(int id, float liters) => RecordFluidNetworkOutput(id, liters);
    public float Emit(int id, float liters) { TryEmitFluidOutputToConnectedStorages(id, liters, 20, out var actual); return actual; }
    public void ResetMeter() => fluidOutputRateMeter?.Reset();
    private bool ContainsRuntimeOutputCoordinate(Vector2Int coordinate) => runtimeOutputCoordinates.Contains(coordinate);
    private bool TryGetRuntimePipeAreaExternalDirection(Vector2Int coordinate, out Vector2Int direction) { direction = OutputDirection; return direction != Vector2Int.zero; }
    private static bool IsFluidItemId(int id) => id >= 0;
    private bool EnsureFluidOutputStorageCache() => cachedFluidOutputStorages.Count > 0;
    private bool TrySelectFluidOutputStorageWithAnySpaceFromCache(int id, out InstallationObject storage)
    {
        storage = cachedFluidOutputStorages.Find(s => s.AvailableFluidStorageLiters > .0001f);
        return storage != null;
    }
    public static void Register(InputOutputModule module, Vector2Int coordinate)
    {
        module.runtimeOutputCoordinates.Add(coordinate);
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out var set))
            registeredRuntimeAreaCoordinates[coordinate] = set = new();
        set.Add(module);
    }
}
public partial class Pump : InputOutputModule
{
    private const int MaxWaterEmitAttemptsPerTick = 32;
    private const float WaterOutputBudgetSeconds = 1;
    private long waterAccumulatorUnits, availableWaterOutputUnits;
    private long waterOutputBudgetUpdatedTick = -1L;
    public float WaterLitersPerSecond = 10;
    public bool HasRuntimeOutputCoordinates = true;
    public int RuntimeAreaMaxObjects = 32;
    public float Space;
    public int GroundItems;
    private readonly InstallationObject pumpStorage = new();
    public void Tick(float dt)
    {
        pumpStorage.AvailableFluidStorageLiters = Space;
        cachedFluidOutputStorages.Clear();
        cachedFluidOutputStorages.Add(pumpStorage);
        ProduceWater(dt);
        Space = pumpStorage.AvailableFluidStorageLiters;
    }
    private int ResolveWaterItemId() => 1;
    private bool TryRouteWaterToFluidStorage(float requested, bool commit, out float accepted)
    {
        accepted = Math.Min(Space, requested); Space -= accepted; return accepted > 0;
    }
    private bool TryEmitOutputItems(int id, int count, Vector3 position) { GroundItems += count; return true; }
    private Vector3 ResolveConsumeTargetWorldPosition() => default;
}
public class TerrainGenerator
{
    public static TerrainGenerator Active = new();
    public readonly Dictionary<Vector2Int, Pipe> Pipes = new();
    public readonly Dictionary<Vector2Int, int> Fluids = new();
}
public sealed class PipeRuntimeRecord
{
    public Pipe Prototype;
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Vector2Int direction) =>
        Prototype != null && Prototype.HasConnectionTowardsAt(coordinate, default, direction);
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote)
    {
        remote = default;
        return Prototype != null && Prototype.TryGetRemoteConnectionCoordinate(coordinate, out remote);
    }
}
public sealed class PipeWorld
{
    public static PipeWorld Current => null;
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord record)
    {
        record = null;
        return false;
    }
}
public partial class Pipe
{
    private const int MaxObjectInfoFluidSearchNodes = 256;
    private const float FluidDisplayRefreshIntervalSeconds = .2f;
    private float nextObjectInfoFluidRefreshTime = float.NegativeInfinity;
    private int cachedObjectInfoFluidItemId;
    private float cachedObjectInfoFluidTemperature, cachedObjectInfoExtractionRate;
    private readonly HashSet<InputOutputModule> objectInfoFluidOutputSources = new();
    private readonly Queue<Vector2Int> objectInfoFluidSearchQueue = new();
    private readonly HashSet<Vector2Int> objectInfoFluidSearchVisited = new();
    private static readonly Dictionary<Vector2Int, int> FluidDisplayNetworkItemCache = new();
    private static readonly Vector2Int[] CardinalDirections = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    public readonly HashSet<Vector2Int> BlockedDirections = new();
    public Vector2Int Anchor;
    public Vector2Int? Remote;
    public int Searches;
    public void Invalidate() => nextObjectInfoFluidRefreshTime = float.NegativeInfinity;
    private bool TryResolveObjectInfoPipeCoordinate(out Vector2Int coordinate) { Searches++; coordinate = Anchor; return true; }
    private bool TryResolveObjectInfoPipeAtStartCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion rotation) => TryGetPipeAtCoordinate(TerrainGenerator.Active, coordinate, out pipe, out rotation);
    private static bool TryGetPipeAtCoordinate(TerrainGenerator terrain, Vector2Int coordinate, out Pipe pipe, out Quaternion rotation)
    {
        rotation = default; return terrain.Pipes.TryGetValue(coordinate, out pipe);
    }
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Quaternion rotation, Vector2Int direction) => !BlockedDirections.Contains(direction);
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote) { remote = Remote ?? default; return Remote.HasValue; }
    private bool TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(Vector2Int coordinate, ref bool fallback, ref int fallbackId, ref float fallbackTemperature, out int fluidItemId, out float temperature)
    {
        temperature = 20;
        return TerrainGenerator.Active.Fluids.TryGetValue(coordinate, out fluidItemId);
    }
}

public static class Checks
{
    private static int passed;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .001f;
    private static float Rate(Pipe pipe) { pipe.TryGetObjectInfoFluidInfo(out _, out _, out var rate); return rate; }
    public static void Main()
    {
        var meter = new FluidOutputRateMeter();
        for (int i = 0; i < 10; i++) meter.Record(1, 1, i * .1);
        Check(Near(meter.GetLitersPerSecond(1, .95), 10), "actual volume becomes rolling liters per second");
        Check(Near(meter.GetLitersPerSecond(1, 1.95), 0), "stopped output expires after one second");
        meter.Record(1, 3, 2); meter.Record(1, 2, 2.01);
        Check(Near(meter.GetLitersPerSecond(1, 2.02), 5), "multiple transfers in one bucket accumulate");
        Check(Near(meter.GetLitersPerSecond(2, 2.02), 0), "other fluid identity is excluded");
        meter.Record(2, 4, 2.03);
        Check(Near(meter.GetLitersPerSecond(2, 2.04), 4), "fluid change clears old fluid samples");
        Check(Near(meter.GetLitersPerSecond(2, 0), 0), "clock reset clears previous run data");
        meter.Record(1, 2, 0); meter.Reset();
        Check(Near(meter.GetLitersPerSecond(1, 0), 0), "pool reset clears measurements");

        Time.timeAsDouble = 0;
        var source = new InputOutputModule();
        source.cachedFluidOutputStorages.Add(new() { AvailableFluidStorageLiters = 3 });
        Check(Near(source.Emit(1, 10), 3) && Near(source.GetObjectInfoFluidOutputLitersPerSecond(1), 3), "standard output records accepted volume, not requested volume");
        Check(Near(source.Emit(1, 10), 0) && Near(source.GetObjectInfoFluidOutputLitersPerSecond(1), 3), "full storage adds no output samples");
        source.isActiveAndEnabled = false;
        Check(Near(source.GetObjectInfoFluidOutputLitersPerSecond(1), 0), "disabled source reports zero");
        source.isActiveAndEnabled = true;
        var pump = new Pump { Space = 2 };
        pump.Tick(.5f);
        Check(Near(pump.GetObjectInfoFluidOutputLitersPerSecond(1), 2) && pump.GroundItems == 3, "pump meters storage delivery and excludes ground item output");
        Time.timeAsDouble = 1.1;
        Check(Near(pump.GetObjectInfoFluidOutputLitersPerSecond(1), 0), "blocked pump output reaches zero");

        Time.timeAsDouble = 5;
        source.ResetMeter(); source.Report(1, 3); pump.Report(1, 2);
        var pipe = new Pipe { Anchor = new(0, 0) };
        TerrainGenerator.Active.Pipes[new(0, 0)] = pipe;
        TerrainGenerator.Active.Pipes[new(1, 0)] = new Pipe();
        TerrainGenerator.Active.Pipes[new(1, 1)] = new Pipe();
        TerrainGenerator.Active.Pipes[new(0, 1)] = new Pipe();
        TerrainGenerator.Active.Fluids[new(0, 0)] = 1;
        InputOutputModule.Register(source, new(0, 0)); InputOutputModule.Register(source, new(1, 0));
        InputOutputModule.Register(pump, new(1, 1));
        Check(Near(Rate(pipe), 5), "all connected producers sum without duplicates around a loop");
        Rate(pipe);
        Check(pipe.Searches == 1, "focused panel reuses short-lived network result");
        var detached = new InputOutputModule(); detached.Report(1, 100);
        TerrainGenerator.Active.Pipes[new(-1, 0)] = new Pipe();
        TerrainGenerator.Active.Pipes[new(-1, 0)].BlockedDirections.Add(new(1, 0));
        InputOutputModule.Register(detached, new(-1, 0)); pipe.Invalidate();
        Check(Near(Rate(pipe), 5), "neighbor pipe with blocked reciprocal connector is excluded");
        var endpoint = new InputOutputModule { OutputDirection = new(0, -1) }; endpoint.Report(1, 4);
        InputOutputModule.Register(endpoint, new(0, 2)); pipe.Invalidate();
        Check(Near(Rate(pipe), 9), "direct output endpoint facing pipe is included");
        endpoint.OutputDirection = new(0, 1); pipe.Invalidate();
        Check(Near(Rate(pipe), 5), "direct output endpoint facing away is excluded");
        pipe.Remote = new(300, 0);
        TerrainGenerator.Active.Pipes[new(300, 0)] = new Pipe();
        InputOutputModule.Register(endpoint, new(300, 0)); pipe.Invalidate();
        Check(Near(Rate(pipe), 9), "underground remote output is included");
        Time.timeAsDouble = 6.2;
        Check(Near(Rate(pipe), 0), "panel becomes zero when all producers stop");
        Console.WriteLine($"{passed} pipe flow checks passed.");
    }
}
