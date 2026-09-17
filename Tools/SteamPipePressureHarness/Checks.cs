using System;
using System.Collections.Generic;

public readonly record struct Vector2Int(int x, int y)
{
    public static Vector2Int zero => default;
    public static Vector2Int right => new(1, 0);
    public static Vector2Int left => new(-1, 0);
    public static Vector2Int up => new(0, 1);
    public static Vector2Int down => new(0, -1);
    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
    public static Vector2Int operator -(Vector2Int a) => new(-a.x, -a.y);
    public static Vector2Int operator *(Vector2Int a, int scale) => new(a.x * scale, a.y * scale);
}
public struct Quaternion { public static Quaternion identity => default; }
public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
public static class Time { public static float unscaledTime => 0; }
public static class MapClimate { public static float CurrentTemperatureCelsius => 20; }
public class InstallationObject
{
    public sealed class ObjectState { public bool activeInHierarchy = true; }
    public readonly ObjectState gameObject = new();
    public bool isActiveAndEnabled => gameObject.activeInHierarchy;
    private static int nextId;
    private readonly int id = ++nextId;
    public static int CompareSimulationOrder(InstallationObject a, InstallationObject b) => a.id.CompareTo(b.id);
    protected static float CalculateFluidPressureRetention(int distance) => Math.Max(0, 100 - Math.Max(0, distance)) * .01f;
}
public partial class InputOutputModule : InstallationObject
{
    protected static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeAreaCoordinates = new();
    protected static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeGridCoordinates = new();
    protected static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeFluidOutputCoordinates = new();
    public Vector2Int Anchor, Direction;
    public readonly HashSet<Vector2Int> Outputs = new();
    public virtual float GetObjectInfoFluidPressureLitersPerSecond(int id) => 0;
    private bool ContainsRuntimeOutputCoordinate(Vector2Int coordinate) => Outputs.Contains(coordinate);
    private bool TryGetRuntimePipeAreaExternalDirection(Vector2Int coordinate, out Vector2Int direction)
    { direction = Direction; return Outputs.Contains(coordinate); }
    public bool TryGetPlacementRuntime(out Vector2Int anchor, out int rotation)
    { anchor = Anchor; rotation = 0; return true; }
    protected static void Register(Dictionary<Vector2Int, HashSet<InputOutputModule>> index, Vector2Int coordinate, InputOutputModule module)
    {
        if (!index.TryGetValue(coordinate, out var modules)) index[coordinate] = modules = new();
        modules.Add(module);
    }
    public static void Reset()
    {
        registeredRuntimeAreaCoordinates.Clear();
        registeredRuntimeGridCoordinates.Clear();
        registeredRuntimeFluidOutputCoordinates.Clear();
        TerrainGenerator.Active = new();
        PipeWorld.Current.Records.Clear();
    }
    internal static bool TryGetPumpPipePassAtRuntimeCoordinate(Vector2Int coordinate, out Pump pump, out Vector2Int other, out Vector2Int external)
    {
        pump = null; other = external = default;
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out var modules)) return false;
        foreach (var module in modules)
            if (module is Pump candidate && candidate.TryGetRuntimePipePass(coordinate, out other, out external)) { pump = candidate; return true; }
        return false;
    }
}
public class Boiler : InputOutputModule
{
    public Boiler(Vector2Int output, Vector2Int direction)
    {
        Anchor = output - direction; Direction = direction;
        Outputs.Add(output);
        Register(registeredRuntimeFluidOutputCoordinates, output, this);
        Register(registeredRuntimeAreaCoordinates, output, this);
        TerrainGenerator.Active.Fluids[output] = 1;
    }
    public bool TryGetRuntimePipeOutputExternalDirection(Vector2Int coordinate, out Vector2Int direction)
    { direction = Direction; return Outputs.Contains(coordinate); }
    public override float GetObjectInfoFluidPressureLitersPerSecond(int id) => isActiveAndEnabled && id == 1 ? 30 : 0;
}
public partial class SteamGenerator : InputOutputModule
{
    // The production prefab has input/body/body/tail at offsets -1/0/1/2.
    public Vector2Int Input => Anchor - Direction;
    public Vector2Int Tail => Anchor + Direction + Direction;
    public SteamGenerator(Vector2Int anchor, Vector2Int direction)
    {
        Anchor = anchor; Direction = direction;
        foreach (var coordinate in new[] { Input, Anchor, Anchor + Direction, Tail })
            Register(registeredRuntimeGridCoordinates, coordinate, this);
        Register(registeredRuntimeAreaCoordinates, Input, this);
        Register(registeredRuntimeAreaCoordinates, Tail, this);
        TerrainGenerator.Active.Fluids[Input] = TerrainGenerator.Active.Fluids[Tail] = 1;
    }
    public bool TryGetRuntimePipePassCoordinates(out Vector2Int input, out Vector2Int tail)
    { input = Input; tail = Tail; return true; }
    public bool TryGetInputCoordinateAndDirection(SteamGenerator source, Vector2Int anchor, int rotation, out Vector2Int input, out Vector2Int direction)
    { input = Input; direction = Direction; return true; }
    public bool TryGetInputDirectionAtCoordinate(SteamGenerator source, Vector2Int anchor, int rotation, Vector2Int coordinate, out Vector2Int direction)
    { direction = Direction; return coordinate == Input; }
    public bool TryGetPipePassTailDirectionAtCoordinate(SteamGenerator source, Vector2Int anchor, int rotation, Vector2Int coordinate, out Vector2Int direction)
    { direction = Direction; return coordinate == Tail; }
    public bool TryGetBodyDirectionFromCenter(SteamGenerator source, int rotation, out Vector2Int direction)
    { direction = Direction; return true; }
    public bool TryGetRuntimePipePassTail(out Vector2Int coordinate, out Vector2Int direction)
    { coordinate = Tail; direction = Direction; return true; }
}
public class Pump : InputOutputModule
{
    private readonly Vector2Int first, second, firstExternal, secondExternal;
    public Pump(Vector2Int firstCoordinate, Vector2Int firstDirection, Vector2Int secondCoordinate, Vector2Int secondDirection)
    {
        first = firstCoordinate; firstExternal = firstDirection;
        second = secondCoordinate; secondExternal = secondDirection;
        Register(registeredRuntimeAreaCoordinates, first, this);
        Register(registeredRuntimeAreaCoordinates, second, this);
    }
    public bool TryGetRuntimePipePass(Vector2Int coordinate, out Vector2Int other, out Vector2Int external)
    {
        if (coordinate == first) { other = second; external = firstExternal; return true; }
        if (coordinate == second) { other = first; external = secondExternal; return true; }
        other = external = default; return false;
    }
}
public class TerrainGenerator
{
    public static TerrainGenerator Active = new();
    public readonly Dictionary<Vector2Int, Pipe> Pipes = new();
    public readonly Dictionary<Vector2Int, int> Fluids = new();
}
public class PipeWorld
{
    public static PipeWorld Current { get; } = new();
    public readonly Dictionary<Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord record) => Records.TryGetValue(coordinate, out record);
}
public partial class PipeRuntimeRecord
{
    public Pipe Prototype;
    public readonly HashSet<Vector2Int> Connections = new();
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Vector2Int direction) => Connections.Contains(direction);
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote) { remote = default; return false; }
}
public partial class Pipe : InstallationObject
{
    private const int MaxObjectInfoFluidSearchNodes = 256;
    private const float FluidDisplayRefreshIntervalSeconds = .2f;
    private readonly Queue<ObjectInfoFluidSearchNode> objectInfoFluidSearchQueue = new();
    private readonly HashSet<Vector2Int> objectInfoFluidSearchVisited = new();
    private readonly Dictionary<Vector2Int, int> objectInfoFluidSearchPipeDistances = new();
    private readonly HashSet<InputOutputModule> objectInfoFluidOutputSources = new();
    private readonly HashSet<InputOutputModule> objectInfoFluidOutputSourceScratch = new();
    private readonly Dictionary<InputOutputModule, int> objectInfoFluidOutputSourcePipeDistances = new();
    private static readonly Dictionary<Vector2Int, int> FluidDisplayNetworkItemCache = new();
    private static readonly Vector2Int[] CardinalDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private float nextObjectInfoFluidRefreshTime, cachedObjectInfoFluidTemperature, cachedObjectInfoPressureRate;
    private int cachedObjectInfoFluidItemId;
    public readonly HashSet<Vector2Int> Connections = new();
    public Pipe(Vector2Int coordinate, params Vector2Int[] connections)
    { Connections.UnionWith(connections); TerrainGenerator.Active.Pipes[coordinate] = this; }
    private static bool TryGetPipeAtCoordinate(TerrainGenerator terrain, Vector2Int coordinate, out Pipe pipe, out Quaternion rotation)
    {
        rotation = default;
        if (PipeWorld.Current.TryGetAtCoordinate(coordinate, out var record))
        { pipe = record.Prototype; return true; }
        return terrain.Pipes.TryGetValue(coordinate, out pipe);
    }
    private bool TryResolveObjectInfoPipeAtStartCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion rotation)
        => TryGetPipeAtCoordinate(TerrainGenerator.Active, coordinate, out pipe, out rotation);
    private bool HasFixedFluidTankAtPipeNetworkCoordinate(Vector2Int coordinate) => false;
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Quaternion rotation, Vector2Int direction) => Connections.Contains(direction);
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote) { remote = default; return false; }
    private bool TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(Vector2Int coordinate, ref bool fallback, ref int fallbackId, ref float fallbackTemperature, out int id, out float temperature)
    { temperature = 100; return TerrainGenerator.Active.Fluids.TryGetValue(coordinate, out id); }
}
public static class Checks
{
    private static int passed, failed;
    private static void Expect(Pipe pipe, Vector2Int coordinate, float expected, string label)
    {
        bool hasFluid = pipe.TryGetObjectInfoFluidInfoAtCoordinate(coordinate, out int id, out _, out float pressure);
        bool ok = hasFluid && id == 1 && Math.Abs(pressure - expected) < .001f;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {label}: fluid={id}, pressure={pressure:F2}, expected={expected:F2}");
        if (ok) passed++; else failed++;
    }
    public static int Main()
    {
        foreach (var direction in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        {
            foreach (int count in new[] { 1, 3 })
            {
                InputOutputModule.Reset();
                var anchor = new Vector2Int(10, -7);
                var boiler = new Boiler(anchor, direction);
                SteamGenerator last = null;
                for (int i = 0; i < count; i++)
                {
                    last = new SteamGenerator(anchor, direction);
                    anchor = last.Tail;
                }
                var pipe = new Pipe(last.Tail, direction, -direction);
                Expect(pipe, last.Tail, 30, $"dense boiler / {count} generators / {direction}");
                var next = last.Tail + direction;
                var nextPipe = new Pipe(next, direction, -direction);
                Expect(nextPipe, next, 29.7f, $"distance after dense chain / {direction}");
                boiler.gameObject.activeInHierarchy = false;
                Expect(pipe, last.Tail, 0, $"disabled boiler / {direction}");
            }
            InputOutputModule.Reset();
            var normal = new SteamGenerator(new(10, -7), direction);
            _ = new Boiler(normal.Input - direction, direction);
            var normalPipe = new Pipe(normal.Tail, direction, -direction);
            Expect(normalPipe, normal.Tail, 30, $"ordinary adjacent inlet / {direction}");
            InputOutputModule.Reset();
            var reverse = new SteamGenerator(new(10, -7), direction);
            _ = new Boiler(reverse.Anchor, -direction);
            var reversePipe = new Pipe(reverse.Tail, direction, -direction);
            Expect(reversePipe, reverse.Tail, 0, $"reverse output at anchor rejected / {direction}");

            InputOutputModule.Reset();
            var runtimeGenerator = new SteamGenerator(new(10, -7), direction);
            _ = new Boiler(runtimeGenerator.Anchor, direction);
            // Runtime records use the shared prototype only for the query;
            // connectivity comes from each installed record, not the prefab.
            var prototype = new Pipe(new(1000, 1000), Vector2Int.up, Vector2Int.down);
            var runtime = new PipeRuntimeRecord { Prototype = prototype };
            runtime.Connections.UnionWith(new[] { direction, -direction });
            PipeWorld.Current.Records[runtimeGenerator.Tail] = runtime;
            bool hasSteam = runtime.TryGetObjectInfoFluidInfo(runtimeGenerator.Tail, out int fluid, out _, out float rate);
            bool ok = hasSteam && fluid == 1 && Math.Abs(rate - 30) < .001f;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} runtime pipe record / {direction}: pressure={rate:F2}");
            if (ok) passed++; else failed++;
            var side = new Vector2Int(-direction.y, direction.x);
            runtime.Connections.Clear();
            runtime.Connections.UnionWith(new[] { side, -side });
            Expect(prototype, runtimeGenerator.Tail, 0, $"perpendicular runtime connector rejected / {direction}");

            InputOutputModule.Reset();
            var source = new Vector2Int(20, -12);
            _ = new Boiler(source, direction);
            for (int i = 0; i < 50; i++) _ = new Pipe(source + direction*i, direction, -direction);
            _ = new Pump(source + direction*50, -direction, source + direction*53, direction);
            var pumpSide = new Vector2Int(-direction.y, direction.x);
            _ = new Pipe(source + direction*50, pumpSide, -pumpSide);
            _ = new Pipe(source + direction*53, pumpSide, -pumpSide);
            Pipe inspected = null;
            for (int i = 54; i < 58; i++) inspected = new Pipe(source + direction*i, direction, -direction);
            Expect(inspected, source + direction*57, 29.1f, $"overlapping pump pressure reset / {direction}");
        }
        Console.WriteLine($"{passed} passed, {failed} failed. Production pipe search and pressure aggregation; no Unity launched.");
        return failed == 0 ? 0 : 1;
    }
}
