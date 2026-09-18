using System;
using System.Collections.Generic;
using UnityEngine;

public class MapObject { }
public class FakeObject { public bool activeInHierarchy = true; }
public class FakeTransform { public Quaternion rotation = Quaternion.identity; }
public static class MapClimate { public static float CurrentTemperatureCelsius => 20; }
public static class MapObjectTickProfiler
{
    public static Scope SampleNamed(string a, string b, string c) => default;
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}
public static class DeterministicSimulationUnits
{
    public static long FromFloat(float value) => (long)Math.Round(value * 1000000d);
    public static float ToFloat(long value) => value / 1000000f;
}
public partial class InstallationObject : MapObject
{
    public readonly FakeObject gameObject = new();
    public readonly FakeTransform transform = new();
    public bool isActiveAndEnabled => gameObject.activeInHierarchy;
    public readonly List<Vector2Int> RuntimeOccupiedCoordinates = new();
    public Vector2Int Anchor;
    public float FluidStorageCapacityLiters = 50;
    private long storedFluidUnits;
    private int storedFluidItemId = -1;
    private float storedFluidTemperatureCelsius;
    public float StoredFluidLiters => DeterministicSimulationUnits.ToFloat(storedFluidUnits);
    public long StoredFluidUnits => storedFluidUnits;
    private long FluidStorageCapacityUnits => DeterministicSimulationUnits.FromFloat(FluidStorageCapacityLiters);
    public bool CanStoreFluid => FluidStorageCapacityLiters > 0;
    public bool HasFluidStorageSpace => AvailableFluidStorageLiters > 0;
    public float AvailableFluidStorageLiters => FluidStorageCapacityLiters - StoredFluidLiters;
    public int StoredFluidItemId => storedFluidItemId;
    public bool CanAcceptFluidItem(int id, float requested = 0) => AvailableFluidStorageLiters >= requested && (storedFluidItemId < 0 || id == storedFluidItemId);
    protected float LimitIncomingFluidLiters(int id, float requested) => requested;
    private void RecordFluidIn(float liters) { }
    private void OnStoredFluidAccepted(int id, float before, float accepted, float temperature) => storedFluidTemperatureCelsius = temperature;
    private float NormalizeFluidTemperatureCelsius(float value) => value;
    private void NotifyStoredFluidChanged(int id, float before) { }
    public bool TryGetPlacementRuntime(out Vector2Int anchor, out int turns) { anchor = Anchor; turns = 0; return true; }
    public static int CompareSimulationOrder(InstallationObject a, InstallationObject b)
    { int x = a.Anchor.x.CompareTo(b.Anchor.x); return x != 0 ? x : a.Anchor.y.CompareTo(b.Anchor.y); }
    protected static float CalculateFluidPressureRetention(int distance) => Math.Max(0, 100 - Math.Max(0, distance)) * .01f;
    public static bool CollectActiveInstallationsAtRuntimeGridCoordinate(Vector2Int c, List<InstallationObject> results)
    { foreach (var s in World.Storages) if (s.isActiveAndEnabled && s.RuntimeOccupiedCoordinates.Contains(c)) results.Add(s); return results.Count > 0; }
}
public static class World
{
    public static readonly Dictionary<Vector2Int, Block> Blocks = new();
    public static readonly List<InstallationObject> Storages = new();
    public static void Place(InstallationObject storage, Vector2Int coordinate, bool bind = true)
    { storage.Anchor = coordinate; storage.RuntimeOccupiedCoordinates.Add(coordinate); Storages.Add(storage); if (bind) Blocks[coordinate] = new Block { MapObject = storage }; }
    public static void Reset() { Blocks.Clear(); Storages.Clear(); PipeWorld.Current.Records.Clear(); SteamTrain.Reset(); InputOutputModule.Reset(); }
}
public class Block
{
    public InstallationObject MapObject;
    public bool TryGetRuntimePipeRecord(out PipeRuntimeRecord record) { record = null; return false; }
    public bool TryGetRuntimePipe(out Pipe pipe, out Quaternion rotation) { pipe = MapObject as Pipe; rotation = Quaternion.identity; return pipe != null; }
}
public class PipeWorld
{
    public static readonly PipeWorld Current = new();
    public readonly Dictionary<Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord record) => Records.TryGetValue(coordinate, out record);
}
public class PipeRuntimeRecord
{
    public Pipe Prototype = new();
    public Quaternion WorldRotation => Quaternion.identity;
    public readonly HashSet<Vector2Int> Connections = new();
    public Vector2Int? Remote;
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Vector2Int direction) => Connections.Contains(direction);
    public bool TryGetRemoteConnectionCoordinate(Vector2Int c, out Vector2Int remote) { remote = Remote ?? default; return Remote.HasValue; }
    public static PipeRuntimeRecord Add(Vector2Int c, params Vector2Int[] directions)
    { var r = new PipeRuntimeRecord(); r.Connections.UnionWith(directions); PipeWorld.Current.Records[c] = r; return r; }
}
public partial class Pipe : InstallationObject
{
    public bool HasConnectionTowardsAt(Vector2Int c, Quaternion q, Vector2Int d) => true;
    public bool TryGetRemoteConnectionCoordinate(Vector2Int c, out Vector2Int other) { other = default; return false; }
}
public class Fluidtank : InstallationObject
{
    public bool IsFlatCarMounted => false;
    public bool HasFluidNetworkConnectionTowards(Vector2Int c, Vector2Int d) =>
        PipeWorld.Current.TryGetAtCoordinate(c + d, out var p) && p.HasConnectionTowardsAt(c + d, -d)
        || InputOutputModule.HasRuntimePumpPipePassTowards(c + d, -d)
        || World.Storages.Exists(s => s != this && s.RuntimeOccupiedCoordinates.Contains(c + d));
}
public class SteamTrain : InstallationObject
{
    private static readonly Dictionary<Vector2Int, SteamTrain> Receivers = new();
    public static void Reset() => Receivers.Clear();
    public static SteamTrain Register(Vector2Int coordinate)
    {
        var train = new SteamTrain { Anchor = coordinate };
        Receivers[coordinate] = train;
        return train;
    }
    public static bool TryGetWaterPipeReceiverAtCoordinate(Vector2Int c, out SteamTrain s) => Receivers.TryGetValue(c, out s);
    public bool CanAcceptWaterFromPipeDirection(Vector2Int d, int id, bool space) => true;
}
public class WaterPump : InputOutputModule { public static int ResolveWaterItemId(object o) => 1; }
public class Pump : InputOutputModule
{
    private Vector2Int first, second, firstExternal, secondExternal;
    private readonly HashSet<Vector2Int> body = new();
    private bool hasPass;
    public void Pass(Vector2Int firstCoordinate, Vector2Int firstDirection, Vector2Int secondCoordinate, Vector2Int secondDirection)
    {
        FluidStorageCapacityLiters = 0;
        first = firstCoordinate; firstExternal = firstDirection;
        second = secondCoordinate; secondExternal = secondDirection;
        hasPass = true;
        Port(first, firstExternal); Port(second, secondExternal);
    }
    public void Body(params Vector2Int[] coordinates)
    {
        foreach (Vector2Int coordinate in coordinates) { body.Add(coordinate); Grid(coordinate); }
    }
    internal bool TryGetRuntimeInterlockedEndpoint(Pump otherPump, Vector2Int otherPumpEndpoint, out Vector2Int endpoint)
    {
        endpoint = default;
        if (otherPump == null || otherPump == this || !body.Contains(otherPumpEndpoint)) return false;
        if (otherPump.body.Contains(first)) { endpoint = first; return true; }
        if (otherPump.body.Contains(second)) { endpoint = second; return true; }
        return false;
    }
    public bool TryGetRuntimePipePass(Vector2Int coordinate, out Vector2Int other, out Vector2Int external)
    {
        if (hasPass && coordinate == first) { other = second; external = firstExternal; return true; }
        if (hasPass && coordinate == second) { other = first; external = secondExternal; return true; }
        other = external = default; return false;
    }
}
public class Boiler : InputOutputModule
{
    public bool TryGetRuntimeWaterPass(Vector2Int c, out Vector2Int other, out Vector2Int d) { other = d = default; return false; }
}
public partial class SteamGenerator : InputOutputModule
{
    public Vector2Int Direction = Vector2Int.right;
    public Vector2Int Input => Anchor - Direction;
    public Vector2Int Tail => Anchor + Direction * 2;
    public void Place(Vector2Int anchor, Vector2Int direction)
    {
        Direction = direction; World.Place(this, anchor); RuntimeOccupiedCoordinates.Add(anchor + direction);
        Port(Input, -direction); Port(Tail, direction);
    }
    public bool TryGetInputCoordinateAndDirection(MapObject source, Vector2Int a, int q, out Vector2Int c, out Vector2Int d) { c = Input; d = Direction; return true; }
    public bool TryGetBodyDirectionFromCenter(MapObject source, int q, out Vector2Int d) { d = Direction; return true; }
    public bool TryGetRuntimePipePassTail(out Vector2Int c, out Vector2Int d) { c = Tail; d = Direction; return true; }
    public bool TryGetRuntimeSteamPass(Vector2Int c, out Vector2Int other, out Vector2Int external)
    { other = c == Input ? Tail : Input; external = c == Input ? -Direction : Direction; return c == Input || c == Tail; }
}
public partial class InputOutputModule : InstallationObject
{
    private static readonly Vector2Int[] FluidCardinalDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeAreaCoordinates = new(), registeredRuntimeGridCoordinates = new();
    private static readonly HashSet<InputOutputModule> activeRuntimeModules = new();
    private readonly Dictionary<Vector2Int, Vector2Int> ports = new();
    private readonly List<Vector2Int> runtimeOutputCoordinates = new();
    private readonly Queue<ConnectedFluidSearchNode> connectedFluidSearchQueue = new();
    private readonly Dictionary<Vector2Int, int> connectedFluidSearchPipeCounts = new();
    private List<RuntimePumpPipePass> connectedFluidPumpPassScratch;
    private List<Pump> connectedFluidInterlockedPumpScratch;
    private readonly HashSet<InstallationObject> connectedFluidStorageCandidates = new();
    private readonly List<InstallationObject> fluidStorageBodyScratch = new();
    private readonly List<FluidOutputConnection> cachedFluidOutputConnections = new();
    private readonly Dictionary<InstallationObject, int> cachedFluidOutputConnectionIndices = new();
    private readonly HashSet<SteamGenerator> directedSteamChainVisited = new();
    private readonly Queue<DirectedSteamPort> directedSteamPortSearchQueue = new();
    private readonly HashSet<DirectedSteamPort> directedSteamVisitedPorts = new();
    private readonly Queue<Vector2Int> directedSteamPipeSearchQueue = new();
    private readonly HashSet<Vector2Int> directedSteamVisitedPipeCoordinates = new();
    private int connectedFluidSearchCurrentPipeCount, cachedFluidOutputConnectionsTopologyVersion;
    private static int fluidTopologyVersion = 1;
    private bool fluidOutputCapacityBlocked;
    private static long ignoredNonFluidPlacementChangeCount, fluidPlacementInvalidationCount;
    private static readonly List<InputOutputModule> runtimeWakeScratch = new();
    public bool Sleeping;
    public float Tick(float liters) => Sleeping ? 0 : Emit(liters);
    protected void WakeRuntimeUpdate() => Sleeping = false;
    private bool HasRuntimePipeTopologyCoordinates() => ports.Count > 0;
    private static void InvalidateFluidTopologyCache() => fluidTopologyVersion++;
    private static void WakeRuntimeModulesAtCoordinates(IReadOnlyList<Vector2Int> coordinates)
    {
        foreach (var module in activeRuntimeModules) foreach (var c in coordinates)
            foreach (var port in module.ports.Keys)
                if (Math.Abs(c.x-port.x)+Math.Abs(c.y-port.y)<=1) module.WakeRuntimeUpdate();
    }
    private readonly List<Vector2Int> runtimeGridCoordinates = new(), runtimePipeInputCoordinates = new();
    public static void Placed(InstallationObject storage) => HandleInstallationPlacementRuntimeChanged(storage);
    public float Emit(float amount) { TryEmitFluidOutputToConnectedStorages(1, amount, 100, out float accepted); return accepted; }
    public float TransportRetention(int itemId) => ResolveFluidOutputTransportRetention(itemId);
    public void Output(Vector2Int c, Vector2Int d) { runtimeOutputCoordinates.Add(c); Port(c, d); }
    public void Grid(Vector2Int c)
    {
        runtimeGridCoordinates.Add(c);
        if (!registeredRuntimeGridCoordinates.TryGetValue(c, out var set))
            registeredRuntimeGridCoordinates[c] = set = new();
        set.Add(this);
        activeRuntimeModules.Add(this);
    }
    protected void Port(Vector2Int c, Vector2Int d)
    { ports[c] = d; if (!registeredRuntimeAreaCoordinates.TryGetValue(c, out var set)) registeredRuntimeAreaCoordinates[c] = set = new(); set.Add(this); activeRuntimeModules.Add(this); }
    private bool TryGetRuntimePipeAreaExternalDirection(Vector2Int c, out Vector2Int d) => ports.TryGetValue(c, out d);
    public bool TryGetRuntimePipeOutputExternalDirection(Vector2Int c, out Vector2Int d) => TryGetRuntimePipeAreaExternalDirection(c, out d);
    private bool TryGetLoadedBlock(Vector2Int c, out Block block) => World.Blocks.TryGetValue(c, out block);
    private static bool ContainsCoordinate(IReadOnlyList<Vector2Int> values, Vector2Int c) { for (int i=0;i<values.Count;i++) if(values[i]==c) return true; return false; }
    private static bool IsFluidItemId(int id) => id == 1;
    private void RecordFluidNetworkOutput(int id, float accepted) { }
    public static void Reset() { registeredRuntimeAreaCoordinates.Clear(); registeredRuntimeGridCoordinates.Clear(); activeRuntimeModules.Clear(); fluidTopologyVersion++; }
    internal static bool TryGetPumpPipePassAtRuntimeCoordinate(Vector2Int coordinate, out Pump pump, out Vector2Int other, out Vector2Int external)
    {
        pump = null; other = external = default;
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out var modules)) return false;
        foreach (var module in modules)
            if (module is Pump candidate && candidate.TryGetRuntimePipePass(coordinate, out other, out external)) { pump = candidate; return true; }
        return false;
    }
    private static bool TryGetRuntimePipeFluidStorageAtCoordinate(Vector2Int c, InputOutputModule excluded, bool space, out InstallationObject storage) => TryGetRuntimePipeFluidStorageAtCoordinate(c, excluded, space, null, out storage);
    private static bool TryGetRuntimePipeFluidStorageAtCoordinate(Vector2Int c, InputOutputModule excluded, bool space, Predicate<InstallationObject> filter, out InstallationObject storage)
    {
        storage = null;
        if (registeredRuntimeAreaCoordinates.TryGetValue(c, out var modules)) foreach (var m in modules)
            if(m != excluded && m.isActiveAndEnabled && m.CanStoreFluid && (filter == null || filter(m))) { storage = m; break; }
        return storage != null;
    }
}
public static class Checks
{
    private static int passed, failed;
    private static void Check(float actual, float expected, string label)
    { bool ok = Math.Abs(actual - expected) < .0001f; Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {label}: {actual} L (expected {expected})"); if(ok) passed++; else failed++; }
    private static void Pipes(Vector2Int start, Vector2Int d, int count)
    { for (int i=0;i<count;i++) PipeRuntimeRecord.Add(start + d*i, d, -d); }
    public static int Main()
    {
        foreach(var d in new[]{Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down})
        {
            World.Reset(); var pump = new Pump(); pump.Output(default,d); Pipes(default,d,3);
            var tank = new Fluidtank(); World.Place(tank,d*3);
            Check(pump.Emit(3),3,$"pump / 3 pipes / tank {d}"); Check(tank.StoredFluidLiters,3,"tank actual storage");

            World.Reset(); var boiler = new Boiler(); boiler.Output(default,d); Pipes(default,d,3);
            var generator = new SteamGenerator(); generator.Place(d*4,d);
            Check(boiler.Emit(3),3,$"boiler / 3 pipes / generator {d}"); Check(generator.StoredFluidLiters,3,"generator actual storage");

            World.Reset(); boiler = new Boiler(); boiler.Output(default,d);
            var generator1 = new SteamGenerator(); generator1.Place(d,d);
            Pipes(generator1.Tail,d,5);
            var generator2 = new SteamGenerator(); generator2.Place(d*9,d);
            var generator3 = new SteamGenerator(); generator3.Place(d*12,d);
            var generator4 = new SteamGenerator(); generator4.Place(d*15,d);
            generator1.TryAddFluidLiters(1,50,100,out _);
            generator2.TryAddFluidLiters(1,50,100,out _);
            generator3.TryAddFluidLiters(1,50,100,out _);
            Check(boiler.Emit(3),3,$"generator / 5 pipes / 3 dense generators {d}");
            Check(generator4.StoredFluidLiters,3,"fourth generator actual storage");

            World.Reset(); boiler = new Boiler(); boiler.Output(default,d); Pipes(default,d,3);
            generator = new SteamGenerator(); generator.Place(d*4,d); generator.TryAddFluidLiters(1,50,100,out _);
            Pipes(generator.Tail,d,3); tank = new Fluidtank(); World.Place(tank,generator.Tail + d*3);
            Check(boiler.Emit(3),3,$"full generator / tail pipes / tank {d}"); Check(tank.StoredFluidLiters,3,"downstream tank actual storage");

            World.Reset(); pump = new Pump(); pump.Output(default,d); Pipes(default,d,3);
            var storage = new InstallationObject(); World.Place(storage,d*3,false);
            Check(pump.Emit(3),3,$"registered storage without Block owner {d}"); Check(storage.StoredFluidLiters,3,"registered storage actual liters");

            World.Reset(); boiler = new Boiler(); boiler.Output(default,d); Pipes(default,d,3);
            generator = new SteamGenerator(); generator.Place(d*4,-d);
            Check(boiler.Emit(3),0,$"reverse generator rejected {d}");

            World.Reset(); pump = new Pump(); pump.Output(default,d); Pipes(default,d,3);
            PipeWorld.Current.Records[d].Connections.Remove(-d); tank = new Fluidtank(); World.Place(tank,d*3);
            Check(pump.Emit(3),0,$"disconnected pipe rejected {d}");

            World.Reset(); pump = new Pump(); pump.Output(default,d); Pipes(default,d,5);
            Check(pump.Emit(3),0,"unconnected route initially has no receiver"); pump.Sleeping = true;
            tank = new Fluidtank(); World.Place(tank,d*5); InputOutputModule.Placed(tank);
            Check(pump.Tick(3),3,$"tank placed last wakes remote producer {d}");
            Check(tank.StoredFluidLiters,3,"late tank actual storage");

            World.Reset(); pump = new Pump(); pump.Output(default,d); Pipes(default,d,5);
            pump.Emit(3); pump.Sleeping = true;
            storage = new InstallationObject(); World.Place(storage,d*5,false); InputOutputModule.Placed(storage);
            Check(pump.Tick(3),3,$"generic CanStoreFluid placed last {d}");
            Check(storage.StoredFluidLiters,3,"late generic storage actual liters");

            World.Reset(); boiler = new Boiler(); boiler.Output(default,d); Pipes(default,d,5);
            boiler.Emit(3); boiler.Sleeping = true;
            generator = new SteamGenerator(); generator.Place(d*6,d); InputOutputModule.Placed(generator);
            Check(boiler.Tick(3),3,$"generator placed last wakes remote boiler {d}");
            Check(generator.StoredFluidLiters,3,"late generator actual storage");

            World.Reset();
            var producer = new InputOutputModule(); producer.Output(default,d); Pipes(default,d,50);
            var resetPump = new Pump(); resetPump.Pass(d*50,-d,d*53,d);
            var side = new Vector2Int(-d.y,d.x);
            PipeRuntimeRecord.Add(d*50,side,-side);
            PipeRuntimeRecord.Add(d*53,side,-side);
            Pipes(d*54,d,4); tank = new Fluidtank(); World.Place(tank,d*58);
            Check(producer.TransportRetention(1),.97f,$"pump resets accumulated pipe loss with overlapping pipes {d}");
            Check(producer.Emit(3),3,$"fluid traverses overlapping pump pass {d}");

            World.Reset(); producer = new InputOutputModule(); producer.Output(default,d);
            var firstPump = new Pump(); firstPump.Pass(d,-d,d*4,d);
            var secondPump = new Pump(); secondPump.Pass(d*5,-d,d*8,d);
            tank = new Fluidtank(); World.Place(tank,d*9);
            Check(producer.Emit(3),3,$"fluid traverses two adjacent pumps {d}");
            Check(tank.StoredFluidLiters,3,"two adjacent pumps actual storage");

            World.Reset(); producer = new InputOutputModule(); producer.Output(default,d);
            firstPump = new Pump(); firstPump.Pass(d,-d,d*4,d);
            secondPump = new Pump(); secondPump.Pass(d*4,-d,d*7,d);
            tank = new Fluidtank(); World.Place(tank,d*8);
            Check(producer.Emit(3),3,$"fluid traverses two pumps sharing a PipePass {d}");
            Check(tank.StoredFluidLiters,3,"shared PipePass pump chain actual storage");

            World.Reset(); var waterSource = new WaterPump(); waterSource.Output(default,d);
            firstPump = new Pump(); firstPump.Pass(d,-d,d*4,d);
            secondPump = new Pump(); secondPump.Pass(d*5,-d,d*8,d);
            var train = SteamTrain.Register(d*8);
            Check(waterSource.Emit(3),3,$"water reaches train through two pumps {d}");
            Check(train.StoredFluidLiters,3,"two-pump train receiver actual storage");

            World.Reset(); waterSource = new WaterPump(); waterSource.Output(default,d);
            firstPump = new Pump(); firstPump.Pass(d,-d,d*4,d); firstPump.Body(d*2,d*3);
            secondPump = new Pump(); secondPump.Pass(d*3,-d,d*6,d); secondPump.Body(d*4,d*5);
            train = SteamTrain.Register(d*6);
            Check(waterSource.Emit(3),3,$"water reaches train through two interlocked pumps {d}");
            Check(train.StoredFluidLiters,3,"interlocked two-pump train receiver actual storage");
        }
        Console.WriteLine($"{passed} passed, {failed} failed. Production output BFS, directed traversal and fluid storage mutation; no Unity launched.");
        return failed == 0 ? 0 : 1;
    }
}
