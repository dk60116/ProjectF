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
public static class MapObjectTickProfiler
{
    public readonly struct Sample : IDisposable { public void Dispose() {} }
    public static Sample SampleNamed(string group, string type, string name) => new();
}
public static class Mathf {
    public static float Min(params float[] values) => System.Linq.Enumerable.Min(values);
    public static float Clamp01(float value) => Math.Clamp(value, 0, 1); public static int Max(int a, int b) => Math.Max(a, b); public static float Min(float a, float b) => Math.Min(a,b); public static float Max(float a, float b) => Math.Max(a,b); }
public static class Time { public static float unscaledTime => 0; }
public static class MapObjectTickManager
{
    public static long CurrentSimulationTick;
    public const float FixedSimulationDeltaSeconds = 0.02f;
}
public static class MapClimate { public static float CurrentTemperatureCelsius => 20; }
public class InstallationObject
{
    public sealed class ObjectState { public bool activeInHierarchy = true; }
    public readonly ObjectState gameObject = new();
    public bool isActiveAndEnabled => gameObject.activeInHierarchy;
    public long RuntimePlacementSequence;
    public int StoredFluidItemId = -1;
    public float StoredFluidLiters;
    public float FluidStorageCapacityLiters = 100f;
    public float AvailableFluidStorageLiters => FluidStorageCapacityLiters - StoredFluidLiters;
    public bool CanStoreFluid => FluidStorageCapacityLiters > 0;
    public bool HasFluidStorageSpace => AvailableFluidStorageLiters > .0001f;
    protected const float ConnectedFluidStorageTransferLitersPerSecond = 50f;
    protected const float FluidFillRatioEpsilon = .0001f;
    public bool CanProvideFluidItem(int id) => StoredFluidLiters > .0001f && (id < 0 || id == StoredFluidItemId);
    public bool TryConsumeFluidLiters(int id, float requested, out float consumed)
    {
        consumed = CanProvideFluidItem(id) ? Math.Min(StoredFluidLiters, requested) : 0;
        StoredFluidLiters -= consumed;
        if (StoredFluidLiters <= .0001f) StoredFluidItemId = -1;
        return consumed > 0;
    }
    public bool TryAddFluidLiters(int id, float requested, float temperature, out float accepted)
    {
        accepted = StoredFluidItemId < 0 || StoredFluidItemId == id ? Math.Min(AvailableFluidStorageLiters, requested) : 0;
        StoredFluidLiters += accepted;
        if (accepted > 0) StoredFluidItemId = id;
        return accepted > 0;
    }
    public void RestoreUnacceptedFluid(int id, float amount, float temperature)
    { StoredFluidItemId = id; StoredFluidLiters += amount; }
    protected static float CalculateFluidEqualizationTransferLiters(InstallationObject source, InstallationObject target) =>
        Math.Max(0, (source.StoredFluidLiters * target.FluidStorageCapacityLiters - target.StoredFluidLiters * source.FluidStorageCapacityLiters)
            / (source.FluidStorageCapacityLiters + target.FluidStorageCapacityLiters));
    public Vector2Int Coordinate;
    public float GetStoredFluidTemperatureCelsius(int id) => 20;
    public static bool CollectActiveInstallationsAtRuntimeGridCoordinate(Vector2Int coordinate, List<InstallationObject> results)
    {
        if (TerrainGenerator.Active.Tanks.TryGetValue(coordinate, out Fluidtank tank)) results.Add(tank);
        return results.Count > 0;
    }
    private static int nextId;
    private readonly int id = ++nextId;
    public static int CompareSimulationOrder(InstallationObject a, InstallationObject b) => a.id.CompareTo(b.id);
    protected static float CalculateFluidPressureRetention(int distance) => Math.Max(0, 100 - Math.Max(0, distance)) * .01f;
}
public partial class InputOutputModule : InstallationObject
{
    private Pipe.FluidNetworkSearchContext runtimeFluidInputPressureContext;
    private List<RuntimePumpPipePass> connectedFluidPumpPassScratch;
    public bool ReadInputPressure(Vector2Int coordinate, int fluid, out float pressure) =>
        TryGetRuntimeFluidInputPressure(coordinate, fluid, out pressure);
    internal static bool TryGetRuntimePipeDisplayFluidStorageAtCoordinate(Vector2Int coordinate, InstallationObject ignored, out InstallationObject storage)
    { TerrainGenerator.Active.Tanks.TryGetValue(coordinate, out var tank); storage = tank; return tank != null; }

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
    internal readonly record struct RuntimePumpPipePass(Pump Pump, Vector2Int OtherCoordinate, Vector2Int ExternalDirection);
    internal static bool CollectPumpPipePassesAtRuntimeCoordinate(Vector2Int coordinate, List<RuntimePumpPipePass> results)
    {
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out var modules)) return false;
        foreach (var module in modules)
            if (module is Pump pump && pump.TryGetRuntimePipePass(coordinate, out var other, out var external))
                results.Add(new(pump, other, external));
        return results.Count > 0;
    }
    internal static bool HasRuntimePumpPipePassTowards(Vector2Int coordinate, Vector2Int direction)
    {
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out var modules)) return false;
        foreach (var module in modules)
            if (module is Pump pump && pump.TryGetRuntimePipePass(coordinate, out _, out var external) && direction == external) return true;
        return false;
    }
    public static bool CollectModulesAtRuntimeAreaCoordinate(Vector2Int coordinate, List<InputOutputModule> modules)
    { if (registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out var set)) modules.AddRange(set); return modules.Count > 0; }
    public static bool CollectModulesAtRuntimeGridCoordinate(Vector2Int coordinate, List<InputOutputModule> modules)
    { if (registeredRuntimeGridCoordinates.TryGetValue(coordinate, out var set)) modules.AddRange(set); return modules.Count > 0; }
    internal static bool TryGetPassiveFluidPassAtRuntimeCoordinate(Vector2Int coordinate, out InputOutputModule owner,
        out Vector2Int other, out Vector2Int external) { owner = null; other = external = default; return false; }
    internal static bool HasRuntimePassiveFluidPassTowards(Vector2Int coordinate, Vector2Int direction) => false;
}
public class Boiler : InputOutputModule
{
    public float Rate = 30f;
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
    public override float GetObjectInfoFluidPressureLitersPerSecond(int id) => isActiveAndEnabled && id == 1 ? Rate : 0;
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
public partial class Pump : InputOutputModule
{
    public float PressureLitersPerSecond = 5f;
    private long pressureBudgetTick = -1;
    private double pressureBudgetLiters;
    internal bool TryGetRuntimeInterlockedEndpoint(Pump other, Vector2Int coordinate, out Vector2Int endpoint)
    { endpoint = default; return false; }
    private readonly Pipe.FluidNetworkSearchContext objectInfoNetworkContext = new();
    private readonly List<Vector2Int> fluidDockSeedCoordinates = new();
    private bool CollectFluidDockSeedCoordinates()
    { fluidDockSeedCoordinates.Clear(); fluidDockSeedCoordinates.Add(first); fluidDockSeedCoordinates.Add(second); return true; }
    internal bool TryGetRuntimeFluidEndpoints(out Vector2Int input, out Vector2Int output)
    { input = first; output = second; return true; }
    internal bool TryGetBodyPipePassEndpointAt(Pump self, Vector2Int anchor, int turns, Vector2Int coordinate, out Vector2Int endpoint, out Vector2Int direction)
    { endpoint = direction = default; return false; }
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
    public readonly Dictionary<Vector2Int, Fluidtank> Tanks = new();
}
public class PipeWorld
{
    public static PipeWorld Current { get; } = new();
    public readonly Dictionary<Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord record) => Records.TryGetValue(coordinate, out record);
    public bool TryGetMatchingAtCoordinate(Vector2Int coordinate, Pipe pipe, out PipeRuntimeRecord record) =>
        TryGetAtCoordinate(coordinate, out record) && ReferenceEquals(record.Prototype, pipe);
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
    private static bool CanDisplayStoredFluidAtCoordinate(
        InstallationObject storage, Vector2Int coordinate, HashSet<int> itemIds) =>
        storage != null && storage.StoredFluidItemId >= 0;
    private const int MaxObjectInfoFluidSearchNodes = 256;
    private const float FluidDisplayRefreshIntervalSeconds = .2f;
    private readonly FluidNetworkSearchContext primaryFluidSearchContext = new();
    private readonly HashSet<InputOutputModule> objectInfoFluidOutputSources = new();
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
    private static bool TryGetFixedFluidTankAtPipeNetworkCoordinate(Vector2Int coordinate, FluidNetworkSearchContext context, out Fluidtank tank) =>
        TerrainGenerator.Active.Tanks.TryGetValue(coordinate, out tank);
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Quaternion rotation, Vector2Int direction) => Connections.Contains(direction);
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote) { remote = default; return false; }
    private static bool TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(Vector2Int coordinate, FluidNetworkSearchContext context,
        ref bool fallback, ref int fallbackId, ref float fallbackTemperature, out int id, out float temperature)
    {
        temperature = 100;
        if (TerrainGenerator.Active.Tanks.TryGetValue(coordinate, out Fluidtank tank) && tank.StoredFluidItemId >= 0)
        { id = tank.StoredFluidItemId; return true; }
        return TerrainGenerator.Active.Fluids.TryGetValue(coordinate, out id);
    }
}
public partial class Fluidtank : InstallationObject
{
    private bool HasMountedPipeTransferReady => true;
    public bool runtimeTickSleeping;
    private readonly List<Fluidtank> connectedTankDependents = new();
    private void SetRuntimeTickSleeping(bool sleeping) { runtimeTickSleeping = sleeping; }
    private void WakeRuntimeTick() { runtimeTickSleeping = false; }
    private static void WakeAllRuntimeTicks()
    { foreach (var tank in TerrainGenerator.Active.Tanks.Values) tank.WakeRuntimeTick(); }
    public void NotifyStockChanged() => WakeConnectedFluidTankTicks();
    public void RebuildConnections() { connectedTankCacheTopologyVersion = -1; EnsureConnectedTankCache(); }
    public int DependentCount => connectedTankDependents.Count;
    private readonly Dictionary<Vector2Int, Pump> fluidNetworkSearchPumps = new();
    private readonly Dictionary<Fluidtank, Pump> connectedTankPumps = new();
    private Pump fluidNetworkSearchCurrentPump;
    private bool isFlatCarMountedPresentation;
    public bool IsFlatCarMounted => isFlatCarMountedPresentation;
    private static readonly Vector2Int[] FluidCardinalDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private readonly List<InstallationObject> adjacentInstallationScratch = new();
    private readonly List<Fluidtank> connectedTankCache = new();
    private readonly Dictionary<Fluidtank, int> connectedTankPipeDistances = new();
    private readonly Dictionary<Vector2Int, int> fluidNetworkSearchPipeCounts = new();
    private readonly Queue<FluidNetworkSearchNode> fluidNetworkSearchQueue = new();
    private readonly List<InputOutputModule.RuntimePumpPipePass> connectedPumpPassScratch = new();
    private readonly Pipe.FluidNetworkSearchContext connectedFluidIdentityContext = new();
    private int connectedTankCacheTopologyVersion = -1;
    private int fluidNetworkTopologyVersion;
    public Fluidtank(Vector2Int coordinate, int fluid)
    { Coordinate = coordinate; StoredFluidItemId = fluid; TerrainGenerator.Active.Tanks[coordinate] = this; }
    public bool ConnectedTo(Fluidtank tank) => EnsureConnectedTankCache() && connectedTankCache.Contains(tank);
    private bool TryGetPlacementRuntime(out Vector2Int coordinate, out int turns) { coordinate = Coordinate; turns = 0; return true; }
    private static Quaternion ResolvePipeRuntimeRotation(Vector2Int coordinate, Pipe pipe) => default;
    public bool HasFluidNetworkConnectionTowards(Vector2Int coordinate, Vector2Int direction) =>
        HasFluidNetworkConnectionTowardsIgnoringStorageCoordinate(coordinate, direction, coordinate);
    private bool TryResolveMountedPumpRailPass(Vector2Int coordinate, Vector2Int direction, out int fluid) { fluid = -1; return false; }
    private bool TryGetPipeOutputAreaConnectionFluidItemId(Vector2Int coordinate, Vector2Int direction, out int fluid)
    { fluid = -1; return false; }
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
            Expect(inspected, source + direction*57, 4.85f, $"overlapping pump pressure cap / {direction}");
            CheckPumpTanks(direction);
            CheckPumpReserves(direction);
            CheckDirectMachineInput(direction);
            CheckDirectedTankTransfer(direction);
            CheckPumpCornerConnection(direction);
        }
        CheckAdjacentFluidIsolation();
        CheckPumpBudget();
        Console.WriteLine($"{passed} passed, {failed} failed. Production pipe search and pressure aggregation; no Unity launched.");
        return failed == 0 ? 0 : 1;
    }

    private static void Assert(bool condition, string label)
    {
        if (condition) passed++;
        else { failed++; Console.WriteLine("FAIL " + label); }
    }

    private static void CheckAdjacentFluidIsolation()
    {
        InputOutputModule.Reset();
        Vector2Int pipeCoordinate = new(300, 300);
        Vector2Int neighborCoordinate = pipeCoordinate + Vector2Int.right;
        var pipe = new Pipe(pipeCoordinate, Vector2Int.right, Vector2Int.left);
        TerrainGenerator.Active.Fluids[neighborCoordinate] = 2;
        Assert(!pipe.TryGetObjectInfoFluidInfoAtCoordinate(
                pipeCoordinate, out _, out _, out _),
            "disconnected neighboring fluid input cannot relabel a pipe");

        _ = new Boiler(neighborCoordinate, Vector2Int.left);
        Assert(pipe.TryGetObjectInfoFluidInfoAtCoordinate(
                pipeCoordinate, out int fluidItemId, out _, out float pressure)
            && fluidItemId == 1 && Math.Abs(pressure - 30f) < .001f,
            "a source output facing the pipe still establishes fluid identity");
    }

    private static void CheckDirectMachineInput(Vector2Int direction)
    {
        foreach (bool adjacent in new[] { false, true })
        {
            InputOutputModule.Reset();
            Vector2Int inlet = new(210, 210), outlet = inlet + direction*3;
            var tank = new Fluidtank(inlet - direction, 1) { StoredFluidLiters = 10f };
            var pump = new Pump(inlet, -direction, outlet, direction);
            Vector2Int input = adjacent ? outlet + direction : outlet;
            var machine = new InputOutputModule { Direction = -direction };
            machine.Outputs.Add(input); // Fixture supplies the input's outward direction.
            Assert(machine.ReadInputPressure(input, 1, out float rate) && Math.Abs(rate - 5f) < .001f,
                $"direct Pump supplies machine input without any pipe record / {direction}, adjacent={adjacent}");
            pump.PressureLitersPerSecond = 2.5f;
            Assert(machine.ReadInputPressure(input, 1, out rate) && Math.Abs(rate - 2.5f) < .001f,
                $"machine input follows edited Pump pressure / {direction}, adjacent={adjacent}");
            Assert(!machine.ReadInputPressure(input, 2, out rate) && rate == 0f,
                "direct Pump cannot satisfy a different recipe fluid");
            tank.StoredFluidLiters = 0;
            Assert(!machine.ReadInputPressure(input, 1, out rate) || rate == 0f,
                "empty inlet tank cannot feed a directly connected machine");
            tank.StoredFluidLiters = 10;
            machine.Direction = direction;
            Assert(!machine.ReadInputPressure(input, 1, out rate) && rate == 0f,
                "wrong-facing direct input cannot receive Pump supply");
            machine.Direction = -direction;
            TerrainGenerator.Active.Tanks.Remove(inlet - direction);
            _ = new Boiler(inlet - direction, direction) { Rate = 1f };
            Assert(machine.ReadInputPressure(input, 1, out rate) && Math.Abs(rate - 1f) < .001f,
                "direct Pump input preserves native production rate without stored fluid");
        }
    }

    private static void CheckPumpReserves(Vector2Int direction)
    {
        InputOutputModule.Reset();
        Vector2Int origin = new(70, 60);
        var source = new Boiler(origin, direction) { Rate = 1f };
        _ = new Pipe(origin, direction, -direction);
        var pump = new Pump(origin + direction, -direction, origin + direction*4, direction);
        var outlet = new Pipe(origin + direction*5, direction, -direction);
        Expect(outlet, origin + direction*5, 1f, $"pump does not amplify native collection / {direction}");
        var tank = new Fluidtank(origin, 1) { StoredFluidLiters = 0.25f };
        Expect(outlet, origin + direction*5, 5f, $"real reserve supplies pump pressure / {direction}");
        pump.PressureLitersPerSecond = 2.5f;
        Expect(outlet, origin + direction*5, 2.5f, $"edited pump pressure / {direction}");
        tank.StoredFluidLiters = 0f;
        Expect(outlet, origin + direction*5, 0f, $"empty reserve cannot be bypassed to reach native collection / {direction}");
        pump.PressureLitersPerSecond = 0f;
        Expect(outlet, origin + direction*5, 0f, $"zero pump pressure / {direction}");
    }

    private static void CheckDirectedTankTransfer(Vector2Int direction)
    {
        InputOutputModule.Reset();
        Vector2Int origin = new(170, 160);
        var pump = new Pump(origin, -direction, origin + direction*3, direction);
        var source = new Fluidtank(origin - direction, 1) { StoredFluidLiters = .25f };
        var target = new Fluidtank(origin + direction*4, 1) { StoredFluidLiters = 80f };
        float total = source.StoredFluidLiters + target.StoredFluidLiters;
        source.ManagedUpdateTick(.1f);
        Assert(Math.Abs(source.StoredFluidLiters - .25f) < .0001f, $"full downstream tank cannot backflow / {direction}");
        target.ManagedUpdateTick(.1f);
        Assert(source.StoredFluidLiters == 0f && Math.Abs(target.StoredFluidLiters - total) < .0001f,
            $"pump draws lower-fill reservoir, limited by its real stock / {direction}");
        target.ManagedUpdateTick(.1f);
        Assert(Math.Abs(target.StoredFluidLiters - total) < .0001f, $"empty reservoir transfers nothing / {direction}");
        source.StoredFluidItemId = 1;
        source.StoredFluidLiters = 10f;
        source.NotifyStockChanged();
        Assert(!target.runtimeTickSleeping, $"reservoir refill wakes downstream receiver / {direction}");
        target.runtimeTickSleeping = true;
        source.StoredFluidLiters = 11f;
        source.NotifyStockChanged();
        Assert(!target.runtimeTickSleeping, $"same-fluid stock increase wakes downstream receiver / {direction}");
        source.StoredFluidLiters = 10f;
        target.RebuildConnections();
        Assert(source.DependentCount == 1, $"cache rebuild removes stale wake subscriptions / {direction}");
        MapObjectTickManager.CurrentSimulationTick += 5;
        target.ManagedUpdateTick(.1f);
        Assert(Math.Abs(source.StoredFluidLiters - 9.5f) < .0001f
            && Math.Abs(target.StoredFluidLiters - total - .5f) < .0001f,
            $"refilled reservoir resumes at 5 L/s with conserved volume / {direction}");

        InputOutputModule.Reset();
        pump = new Pump(origin, -direction, origin + direction*3, direction);
        var inletPipe = new Pipe(origin - direction, direction, -direction);
        _ = new Fluidtank(origin + direction*4, 1) { StoredFluidLiters = 10f };
        bool inletHasFluid = inletPipe.TryGetObjectInfoFluidInfoAtCoordinate(origin - direction, out _, out _, out float inletRate);
        Assert(!inletHasFluid && inletRate == 0f, $"outlet-only fluid cannot identify or pressurize inlet / {direction}");
    }

    private static void CheckPumpCornerConnection(Vector2Int direction)
    {
        foreach (int sideSign in new[] { -1, 1 })
        {
            InputOutputModule.Reset();
            Vector2Int endpoint = new(90, 70);
            Vector2Int side = new Vector2Int(-direction.y, direction.x) * sideSign;
            var pump = new Pump(endpoint, -direction, endpoint + direction*3, direction);
            // The selected corner sits ON the Pump input cell, and the tank
            // approaches from its lateral connector, as in the reported layout.
            var corner = new Pipe(endpoint, direction, side);
            var inlet = new Pipe(endpoint + side, side, -side);
            var inletRecord = new PipeRuntimeRecord { Prototype = inlet };
            inletRecord.Connections.UnionWith(new[] { side, -side });
            PipeWorld.Current.Records[endpoint + side] = inletRecord;
            var cornerRecord = new PipeRuntimeRecord { Prototype = corner };
            cornerRecord.Connections.UnionWith(new[] { direction, side });
            PipeWorld.Current.Records[endpoint] = cornerRecord;
            var tank = new Fluidtank(endpoint + side*2, 1) { StoredFluidLiters = 10f };
            var outlet = new Pipe(endpoint + direction*4, direction, -direction);
            _ = new Boiler(endpoint + side*2, -side) { Rate = 1f };
            bool cornerFluid = corner.TryGetObjectInfoFluidInfoAtCoordinate(endpoint, out int cornerId, out _, out float cornerRate);
            Assert(cornerFluid && cornerId == 1 && Math.Abs(cornerRate - .99f) < .001f,
                $"upstream corner retains source pressure without pump boost / {direction}, side={sideSign}: fluid={cornerId}, rate={cornerRate}");
            Assert(pump.TryGetObjectInfoFluidInfo(out int pumpId, out _, out float pumpRate) && pumpId == 1 && pumpRate > 0f,
                $"Pump must find lateral corner supply / {direction}, side={sideSign}");
            Expect(outlet, endpoint + direction*4, 5f,
                $"Pump outlet supplied through corner / {direction}, side={sideSign}");
        }
    }

    private static void CheckPumpBudget()
    {
        var pump = new Pump(new(500,500), Vector2Int.left, new(503,500), Vector2Int.right);
        MapObjectTickManager.CurrentSimulationTick = 100;
        Assert(Math.Abs(pump.LimitTransferVolume(100f, .1f) - .5f) < .0001f, "5 L/s permits 0.5 L in a 0.1-second window");
        pump.RecordTransferredVolume(.3f);
        Assert(Math.Abs(pump.LimitTransferVolume(100f, .1f) - .2f) < .0001f, "branches share the pump's remaining allowance");
        pump.RecordTransferredVolume(.2f);
        Assert(pump.LimitTransferVolume(100f, .1f) < .0001f, "same-tick push and pull cannot double the pump rate");
        MapObjectTickManager.CurrentSimulationTick++;
        Assert(Math.Abs(pump.LimitTransferVolume(100f, .1f) - .1f) < .0001f, "staggered consumers accrue only elapsed simulation time");
        pump.RecordTransferredVolume(.1f);
        MapObjectTickManager.CurrentSimulationTick += 100;
        Assert(Math.Abs(pump.LimitTransferVolume(100f, .1f) - .5f) < .0001f, "idle time cannot accumulate an unlimited burst");
        pump.PressureLitersPerSecond = 0f;
        Assert(pump.LimitTransferVolume(100f, .1f) == 0f, "editing pressure to zero clears the remaining allowance");
    }

    private static void CheckPumpTanks(Vector2Int direction)
    {
        foreach (bool overlap in new[] { false, true })
        foreach (int targetFluid in new[] { -1, 2, 3 })
        foreach (bool chain in new[] { false, true })
        {
            InputOutputModule.Reset();
            Vector2Int origin = new(30, 20);
            var pump = new Pump(origin, -direction, origin + direction*3, direction);
            Vector2Int last = origin + direction*3;
            if (chain)
            {
                _ = new Pump(last, -direction, last + direction*3, direction);
                last += direction*3;
            }
            var source = new Fluidtank(overlap ? origin : origin - direction, 2);
            var target = new Fluidtank(overlap ? last : last + direction, targetFluid);
            bool compatible = targetFluid < 0 || targetFluid == 2;
            Assert(!source.ConnectedTo(target) && target.ConnectedTo(source) == compatible,
                $"tank/Pump/tank must permit forward withdrawal only: {direction}, overlap={overlap}, chain={chain}, fluid={targetFluid}");
            if (compatible)
            {
                Assert(pump.TryGetObjectInfoFluidInfo(out int fluid, out _, out _) && fluid == 2,
                    "Pump must report the same tank fluid as Pipe without requiring an intermediate pipe");
            }
            var side = new Vector2Int(-direction.y, direction.x);
            if (!overlap)
            {
                var sideTank = new Fluidtank(origin + side, 2);
                Assert(!sideTank.ConnectedTo(source), "tank beside the closed side of a Pump endpoint must stay disconnected");
            }
        }
    }
}
