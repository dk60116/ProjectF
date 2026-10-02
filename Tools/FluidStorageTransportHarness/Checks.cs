using System;
using System.Collections.Generic;
using UnityEngine;

public class MapObject { }
public class FakeObject { public bool activeInHierarchy = true; }
public class FakeTransform { public Quaternion rotation = Quaternion.identity; }
public static class MapClimate { public static float CurrentTemperatureCelsius => 20; }
public static class MapObjectTickManager { public static long CurrentSimulationTick; public const float FixedSimulationDeltaSeconds = ProjectF.Simulation.SimulationTickWorld.FixedSimulationDeltaSeconds; }
public static class MapObjectTickProfiler
{
    public static Scope SampleNamed(string a, string b, string c) => default;
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}
public partial class InstallationObject : MapObject
{
    public readonly FakeObject gameObject = new();
    public readonly FakeTransform transform = new();
    public bool isActiveAndEnabled => gameObject.activeInHierarchy;
    public readonly List<Vector2Int> RuntimeOccupiedCoordinates = new();
    public Vector2Int Anchor;
    public long RuntimePlacementSequence;
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
    private void RecordFluidOut(float liters) { }
    public bool CanProvideFluidItem(int id, float requested = 0) => StoredFluidItemId == id && StoredFluidLiters > 0 && StoredFluidLiters >= requested;
    public float GetStoredFluidTemperatureCelsius(int id) => storedFluidTemperatureCelsius;
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
public partial class Pump : InputOutputModule
{
    public float PressureLitersPerSecond = 100f;
    private long pressureBudgetTick = -1;
    private double pressureBudgetLiters;
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
    internal bool TryGetRuntimeFluidEndpoints(out Vector2Int input, out Vector2Int output)
    { input = first; output = second; return hasPass; }
    internal bool TryGetBodyPipePassEndpointAt(MapObject source, Vector2Int anchor, int turns, Vector2Int c, out Vector2Int endpoint, out Vector2Int external)
    {
        endpoint = external = default;
        if (!body.Contains(c)) return false;
        if (c == first - firstExternal) { endpoint = first; external = firstExternal; return true; }
        if (c == second - secondExternal) { endpoint = second; external = secondExternal; return true; }
        return false;
    }
    public bool TryGetPipePassExternalDirection(MapObject source, Vector2Int anchor, int turns, Vector2Int c, out Vector2Int d)
    { d = c == first ? firstExternal : c == second ? secondExternal : default; return hasPass && d != default; }
    internal bool TryGetPipePassAt(MapObject source, Vector2Int anchor, int turns, Vector2Int c, out Vector2Int other, out Vector2Int external)
    {
        other = external = default;
        var endpoint = c;
        if (!TryGetPipePassExternalDirection(source, anchor, turns, c, out external)
            && !TryGetBodyPipePassEndpointAt(source, anchor, turns, c, out endpoint, out external)) return false;
        other = endpoint == first ? second : first;
        return true;
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
    public struct ItemIoEntry
    {
        public ItemDefinition itemDefinition;
        public float ResolvedAmount;
        public bool IsFluid => itemDefinition != null && itemDefinition.id == 1;
    }
    public sealed class InputOutputPair { public List<ItemIoEntry> outputs = new(); }
    private ProjectF.Simulation.ProductionProcess production;
    protected bool IsActiveCraftRunning => production.Active;
    private bool hasActiveCraft => production.Active;
    private bool waitingForOutput => production.WaitingForOutput;
    protected int ActiveOutputItemId => production.OutputItemId;
    protected int ActiveOutputCount => production.OutputCount;
    protected int ActiveRecipeIndex => production.RecipeIndex;
    private readonly ItemDefinition installedDefinition = new() { ElectricityRate = 100000f, CompleteEnergy = 3600000f };
    public float OutputRate = 2f, CraftSeconds = 1f;
    public InputOutputModule() => production.Begin(0,1,2,0);
    protected void BeginWorkingCraft()
    { OutputRate = 1f; CraftSeconds = installedDefinition.CompleteEnergy / installedDefinition.ElectricityRate; production.Begin(0,1,1,DeterministicSimulationUnits.SecondsToTicks(CraftSeconds)); }
    protected virtual float ResolveInitialCraftDuration(ItemDefinition definition, int outputItemId = -1) => CraftSeconds;
    protected int ResolveProductionTargetPairIndex(int id) => 0;
    protected void AdvanceCraft(float dt) => UpdateActiveCraft(dt);
    protected ItemDefinition ResolveInstalledDefinition() => installedDefinition;
    protected static bool RequiresOperationalEnergy(ItemDefinition definition) => true;
    private bool TryConsumeOperatingEnergy(float dt, out float consumed) { consumed = installedDefinition.ElectricityRate * dt; return consumed > 0; }
    protected virtual float ResolveCompleteEnergy(ItemDefinition definition) => definition.CompleteEnergy;
    protected virtual bool TryCompleteActiveCraft() => false;
    protected void ClearActiveCraft() => production.Clear();
    protected void MarkPersistenceStateDirty() { }
    protected bool TryGetInputOutputPair(int index, out InputOutputPair pair)
    { pair = new(); pair.outputs.Add(new ItemIoEntry { itemDefinition = new ItemDefinition { id = 1 }, ResolvedAmount = OutputRate }); return true; }
    private static readonly Vector2Int[] FluidCardinalDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeAreaCoordinates = new(), registeredRuntimeGridCoordinates = new();
    private static readonly HashSet<InputOutputModule> activeRuntimeModules = new();
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeFluidOutputCoordinates = new();
    private readonly Dictionary<Vector2Int, Vector2Int> ports = new();
    private readonly List<Vector2Int> runtimeOutputCoordinates = new();
    private readonly Queue<ConnectedFluidSearchNode> connectedFluidSearchQueue = new();
    private readonly Dictionary<Vector2Int, int> connectedFluidSearchPipeCounts = new();
    private List<RuntimePumpPipePass> connectedFluidPumpPassScratch;
    private List<Pump> connectedFluidInterlockedPumpScratch;
    private readonly HashSet<InstallationObject> connectedFluidStorageCandidates = new();
    private readonly List<InstallationObject> fluidStorageBodyScratch = new();
    private readonly List<FluidOutputConnection> cachedFluidOutputConnections = new();
    private readonly Dictionary<FluidStorageEndpointKey, int> cachedFluidOutputConnectionIndices = new();
    private readonly List<Vector2Int> cachedFluidOutputSeedCoordinates = new();
    private readonly Dictionary<Vector2Int, Pump> connectedFluidSearchPumps = new();
    private readonly List<Vector2Int> connectedFluidSeedCoordinates = new(), connectedFluidSeedCoordinateScratch = new();
    private readonly List<InstallationObject> cachedConnectedFluidSourceStorages = new();
    private readonly Dictionary<InstallationObject, int> cachedConnectedFluidSourcePipeDistances = new();
    private readonly Dictionary<InstallationObject, Pump> connectedFluidSourcePumps = new();
    private readonly Dictionary<Vector2Int, FluidPortConnectionCache> fluidInputPortConnectionCaches = new();
    private int cachedConnectedFluidSourceStoragesTopologyVersion = -1;
    private bool UsesConnectedTankNetworkStorage => false;
    private Pump connectedFluidSearchCurrentPump;
    private readonly List<FluidOutputTransferCandidate> fluidOutputTransferCandidates = new();
    protected float ManagedUpdateTickIntervalSeconds => .1f;
    private static int fluidStorageStateVersion;
    private static long fluidOutputSelectionCacheHitCount, fluidOutputSelectionCacheMissCount, fluidOutputRetentionCacheHitCount, fluidOutputRetentionCacheMissCount;
    private long cachedFluidOutputSelectionTick = -1, cachedFluidOutputRetentionTick = -1;
    private int cachedFluidOutputSelectionStateVersion, cachedFluidOutputSelectionTopologyVersion, cachedFluidOutputSelectionItemId;
    private int cachedFluidOutputRetentionStateVersion, cachedFluidOutputRetentionTopologyVersion, cachedFluidOutputRetentionItemId;
    private bool cachedFluidOutputSelectionFound;
    private FluidOutputConnection cachedFluidOutputSelection;
    private float cachedFluidOutputRetentionSourceRate, cachedFluidOutputRetention;
    internal bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int c) => false;
    internal float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(Vector2Int c, int id) => 0;
    internal float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(Vector2Int c, int id) => 0;
    internal bool CanAcceptDedicatedFluidAtRuntimeCoordinate(Vector2Int c, int id, float liters) => false;
    internal bool TryAddDedicatedFluidAtRuntimeCoordinate(Vector2Int c, int id, float liters, float temperature, out float accepted) { accepted = 0; return false; }
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
    // These transport cases exercise normal placement; bulk deferral is covered
    // with the actual batch methods in BenchmarkTool/RunBulkChecks.ps1.
    private static int runtimePipeTopologyBatchDepth = 0;
    private static bool deferredPipeTopologyWake;
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
    public void InputPort(Vector2Int c, Vector2Int d) => Port(c, d);
    public float Pull(Vector2Int c, float amount)
    { TryConsumeConnectedFluidInputAtCoordinate(c, 1, amount, out float consumed, out _); return consumed; }
    public float TransportRetention(int itemId) => ResolveFluidOutputTransportRetention(itemId);
    public void Output(Vector2Int c, Vector2Int d)
    {
        runtimeOutputCoordinates.Add(c); Port(c, d);
        if (!registeredRuntimeFluidOutputCoordinates.TryGetValue(c, out var set)) registeredRuntimeFluidOutputCoordinates[c] = set = new();
        set.Add(this);
    }
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
    private bool ContainsRuntimeOutputCoordinate(Vector2Int c) => runtimeOutputCoordinates.Contains(c);
    internal static bool HasRuntimeFluidInputFacingAt(Vector2Int c, Vector2Int direction)
    {
        if (!registeredRuntimeAreaCoordinates.TryGetValue(c, out var modules)) return false;
        foreach (var module in modules)
            if (module is not Pump && !module.ContainsRuntimeOutputCoordinate(c)
                && module.TryGetRuntimePipeAreaExternalDirection(c, out var external)
                && external == -direction) return true;
        return false;
    }
    private bool TryGetLoadedBlock(Vector2Int c, out Block block) => World.Blocks.TryGetValue(c, out block);
    private static bool ContainsCoordinate(IReadOnlyList<Vector2Int> values, Vector2Int c) { for (int i=0;i<values.Count;i++) if(values[i]==c) return true; return false; }
    public static bool IsFluidItemId(int id) => id == 1;
    private void RecordFluidNetworkOutput(int id, float accepted) { }
    public static void Reset() { registeredRuntimeAreaCoordinates.Clear(); registeredRuntimeGridCoordinates.Clear(); registeredRuntimeFluidOutputCoordinates.Clear(); activeRuntimeModules.Clear(); fluidTopologyVersion++; MapObjectTickManager.CurrentSimulationTick++; }
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
public class ItemDefinition
{
    public int id;
    public float ElectricityRate, CompleteEnergy;
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition definition) => definition.ElectricityRate;
}
public static class CraftingTreeRuntime
{
    public static bool TryGetIngredientsView(int id, out IReadOnlyList<int> inputs) { inputs = null; return false; }
    public static float GetOutputAmount(int id) => 1f;
    public static int GetOutputCount(int id) => 1;
}
public partial class ProductionMachine : InputOutputModule
{
    private static void NotifyFluidOutputCapacityIncreased(InputOutputModule source) { }
    private long productionFluidOutputUnits = -1;
    private float productionFluidOutputDeltaTime;
    public float Remaining => DeterministicSimulationUnits.ToFloat(Math.Max(0, productionFluidOutputUnits));
    public bool Pending => IsActiveCraftRunning;
    public void CompleteTick()
    { MapObjectTickManager.CurrentSimulationTick += DeterministicSimulationUnits.DeltaTimeToTicks(.1f); productionFluidOutputDeltaTime = .1f; if (Pending) TryCompleteActiveCraft(); }
    public void StartWorking() => BeginWorkingCraft();
    public void WorkingTick()
    { MapObjectTickManager.CurrentSimulationTick += DeterministicSimulationUnits.DeltaTimeToTicks(.1f); productionFluidOutputDeltaTime = .1f; AdvanceCraft(.1f); }
}
public static class Checks
{
    private static int passed, failed;
    private static void Check(float actual, float expected, string label)
    { bool ok = Math.Abs(actual - expected) < .0001f; Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {label}: {actual} L (expected {expected})"); if(ok) passed++; else failed++; }
    private static void Pipes(Vector2Int start, Vector2Int d, int count)
    { for (int i=0;i<count;i++) PipeRuntimeRecord.Add(start + d*i, d, -d); }
    private static void UndergroundPair(Vector2Int first, Vector2Int second, Vector2Int direction)
    {
        PipeRuntimeRecord.Add(first, -direction).Remote = second;
        PipeRuntimeRecord.Add(second, direction).Remote = first;
    }
    private static void SetupPumpUnderground(Vector2Int direction, bool overlap, bool tunnelAtInlet,
        out Vector2Int sourceCoordinate, out Vector2Int receiverCoordinate)
    {
        var pump = new Pump { PressureLitersPerSecond = 5f };
        pump.Pass(default, -direction, direction*3, direction);
        Vector2Int first = tunnelAtInlet ? direction*(overlap ? -5 : -6) : direction*(overlap ? 3 : 4);
        Vector2Int second = first + direction*5;
        UndergroundPair(first, second, direction);
        sourceCoordinate = tunnelAtInlet ? first - direction : -direction;
        receiverCoordinate = tunnelAtInlet ? direction*4 : second + direction;
    }
    private static void CheckPumpUnderground(Vector2Int direction)
    {
        foreach (bool overlap in new[] { false, true })
        foreach (bool tunnelAtInlet in new[] { false, true })
        {
            World.Reset();
            SetupPumpUnderground(direction, overlap, tunnelAtInlet, out var sourceCoordinate, out var receiverCoordinate);
            var source = new Fluidtank(); World.Place(source, sourceCoordinate);
            source.TryAddFluidLiters(1, 10, 20, out _);
            var receiver = new ProductionMachine(); receiver.InputPort(receiverCoordinate, -direction);
            Check(receiver.Pull(receiverCoordinate, .5f), .5f,
                $"tank / underground / Pump intake {direction}, overlap={overlap}, inletTunnel={tunnelAtInlet}");
            Check(source.StoredFluidLiters, 9.5f, "underground/Pump route debits actual source stock");

            World.Reset();
            SetupPumpUnderground(direction, overlap, tunnelAtInlet, out sourceCoordinate, out receiverCoordinate);
            var producer = new InputOutputModule(); producer.Output(sourceCoordinate, direction);
            var destination = new Fluidtank(); World.Place(destination, receiverCoordinate);
            Check(producer.Emit(.5f), .5f,
                $"producer / underground / Pump output {direction}, overlap={overlap}, inletTunnel={tunnelAtInlet}");
            Check(destination.StoredFluidLiters, .5f, "underground/Pump output credits actual receiver stock");

            var reverseReceiver = new ProductionMachine(); reverseReceiver.InputPort(sourceCoordinate, direction);
            Check(reverseReceiver.Pull(sourceCoordinate, .5f), 0,
                "an underground endpoint must not permit reverse flow through a Pump");
        }
    }
    public static int Main()
    {
        foreach(var d in new[]{Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down})
        {
            CheckPumpUnderground(d);
            foreach (int layout in new[] { 0, 1, 2, 3 })
            {
                World.Reset();
                var sourceTank = new Fluidtank(); World.Place(sourceTank, -d);
                sourceTank.TryAddFluidLiters(1, 10f, 25f, out _);
                var supplyPump = new Pump { PressureLitersPerSecond = 5f };
                supplyPump.Pass(default, -d, d*3, d);
                supplyPump.Body(d, d*2);
                Vector2Int machineInput = layout == 0 ? d*3 : layout == 1 ? d*4 : layout == 2 ? d*2 : d*6;
                var receiver = new ProductionMachine(); receiver.InputPort(machineInput, -d);
                if (layout == 3) Pipes(d*3, d, 4);
                Check(receiver.Pull(machineInput, .5f), .5f, $"tank / Pump / maker intake {d}, layout={layout}");
                Check(sourceTank.StoredFluidLiters, 9.5f, "maker intake removes real tank stock");
                Check(receiver.Pull(machineInput, .5f), 0f, "same tick cannot reuse Pump throughput budget");
                MapObjectTickManager.CurrentSimulationTick += DeterministicSimulationUnits.DeltaTimeToTicks(.1f);
                supplyPump.PressureLitersPerSecond = 2.5f;
                Check(receiver.Pull(machineInput, .5f), .25f, "maker intake follows edited Pump pressure");
                sourceTank.TryConsumeFluidLiters(1, 50f, out _);
                MapObjectTickManager.CurrentSimulationTick += DeterministicSimulationUnits.DeltaTimeToTicks(.1f);
                Check(receiver.Pull(machineInput, .5f), 0f, "empty tank supplies no fluid to maker");
            }
            World.Reset(); var pipeMaker = new ProductionMachine(); pipeMaker.Output(default,d);
            Pipes(default,d,8);
            var pipeTank = new Fluidtank(); World.Place(pipeTank,d*8);
            pipeMaker.CompleteTick();
            Check(pipeTank.StoredFluidLiters,.186f,$"maker / 8 ordinary pipes / tank first delivery {d}");
            Check(pipeMaker.Remaining + pipeTank.StoredFluidLiters,2f,"ordinary pipes preserve crafted volume");
            for (int tick=0; tick<20 && pipeMaker.Pending; tick++) pipeMaker.CompleteTick();
            Check(pipeTank.StoredFluidLiters,2f,"ordinary pipes deliver entire recipe batch");
            Check(pipeMaker.Pending ? 1 : 0,0,"ordinary pipe output finishes craft");

            World.Reset(); pipeMaker = new ProductionMachine(); pipeMaker.Output(default,d); pipeMaker.StartWorking();
            Pipes(default,d,8); pipeTank = new Fluidtank(); World.Place(pipeTank,d*8);
            for (int tick=0; tick<359; tick++) pipeMaker.WorkingTick();
            Check(pipeTank.StoredFluidLiters,0,$"Working phase retains output until 36 second craft finishes {d}");
            pipeMaker.WorkingTick();
            Check(pipeTank.StoredFluidLiters,.093f,"energy completion starts ordinary pipe delivery at Count 1 L/s");
            Check(pipeMaker.Remaining + pipeTank.StoredFluidLiters,36f,"36 second craft creates 36 L at Count 1");
            for (int tick=0; tick<400 && pipeMaker.Pending; tick++) pipeMaker.WorkingTick();
            Check(pipeTank.StoredFluidLiters,36f,"ordinary pipe batch drains after actual production advance");

            World.Reset(); pipeMaker = new ProductionMachine(); pipeMaker.Output(default,d);
            var sideDirection = new Vector2Int(-d.y,d.x);
            PipeRuntimeRecord.Add(default,-d,sideDirection);
            Pipes(sideDirection,sideDirection,7);
            pipeTank = new Fluidtank(); World.Place(pipeTank,sideDirection*8);
            for (int tick=0; tick<20 && pipeMaker.Pending; tick++) pipeMaker.CompleteTick();
            Check(pipeTank.StoredFluidLiters,2f,$"maker output / perpendicular ordinary pipe / tank {d}");

            // Keep the same lateral route but leave the output cell virtual.
            // The port itself exposes only the direction away from the maker.
            World.Reset(); pipeMaker = new ProductionMachine(); pipeMaker.Output(default,d);
            Pipes(sideDirection,sideDirection,8);
            pipeTank = new Fluidtank(); World.Place(pipeTank,sideDirection*9);
            pipeMaker.CompleteTick();
            Check(pipeTank.StoredFluidLiters,0,$"virtual output beside lateral pipe chain cannot turn sideways {d}");
            Check(pipeMaker.Remaining,2f,"unreachable receiver leaves actual output batch buffered");

            World.Reset(); pipeMaker = new ProductionMachine(); pipeMaker.Output(default,d);
            Pipes(d,d,8);
            pipeTank = new Fluidtank(); World.Place(pipeTank,d*9);
            for (int tick=0; tick<20 && pipeMaker.Pending; tick++) pipeMaker.CompleteTick();
            Check(pipeTank.StoredFluidLiters,2f,$"virtual output with pipe chain on external side delivers {d}");

            World.Reset(); pipeMaker = new ProductionMachine(); pipeMaker.Output(default,d);
            PipeRuntimeRecord.Add(default,-d,sideDirection);
            Pipes(sideDirection,sideDirection,8);
            pipeTank = new Fluidtank(); World.Place(pipeTank,sideDirection*9);
            for (int tick=0; tick<20 && pipeMaker.Pending; tick++) pipeMaker.CompleteTick();
            Check(pipeTank.StoredFluidLiters,2f,$"corner pipe on output cell connects same lateral route {d}");

            World.Reset(); var maker = new InputOutputModule(); maker.Output(default,d);
            var dockedPump = new Pump(); dockedPump.Pass(-d,-d,d*2,d); dockedPump.Body(default,d);
            var dockedTank = new Fluidtank(); World.Place(dockedTank,d*3);
            Check(maker.Emit(.2f),.2f,$"maker output overlapping Pump inlet body {d}");
            Check(dockedTank.StoredFluidLiters,.2f,"docked output reaches actual tank");

            World.Reset(); var craftingMaker = new ProductionMachine(); craftingMaker.Output(default,d);
            dockedPump = new Pump { PressureLitersPerSecond = 5f };
            dockedPump.Pass(-d,-d,d*2,d); dockedPump.Body(default,d);
            for (int i = 0; i < 5; i++)
            {
                var nextPump = new Pump { PressureLitersPerSecond = 5f };
                nextPump.Pass(d*(i*2+1),-d,d*(i*2+4),d); nextPump.Body(d*(i*2+2),d*(i*2+3));
            }
            dockedTank = new Fluidtank(); World.Place(dockedTank,d*13);
            craftingMaker.CompleteTick();
            Check(dockedTank.StoredFluidLiters,.2f,$"completed recipe / docked inlet / 6 interlocked pumps / tank {d}");
            Check(craftingMaker.Remaining + dockedTank.StoredFluidLiters,2f,"transport preserves crafted volume");
            for (int tick=0; tick<12 && craftingMaker.Pending; tick++) craftingMaker.CompleteTick();
            Check(dockedTank.StoredFluidLiters,2f,"entire recipe output reaches downstream tank");
            Check(craftingMaker.Pending ? 1 : 0,0,"drained craft completes");

            World.Reset(); maker = new InputOutputModule(); maker.Output(default,-d);
            dockedPump = new Pump(); dockedPump.Pass(-d,-d,d*2,d); dockedPump.Body(default,d);
            dockedTank = new Fluidtank(); World.Place(dockedTank,d*3);
            Check(maker.Emit(.2f),0,$"back-facing machine output cannot dock onto Pump body {d}");

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

            // Saved slot_01: boiler (2,8), first generator (3,8), corner (5,8),
            // straight pipe (5,7), second generator (5,6). Rotate the same layout.
            var turn = new Vector2Int(d.y,-d.x);
            var origin = new Vector2Int(2,8);
            World.Reset(); boiler = new Boiler(); boiler.Output(origin+d,d);
            generator1 = new SteamGenerator(); generator1.Place(origin+d,d);
            PipeRuntimeRecord.Add(generator1.Tail,-d,turn);
            PipeRuntimeRecord.Add(generator1.Tail+turn,-turn,turn);
            generator2 = new SteamGenerator(); generator2.Place(generator1.Tail+turn*2,turn);
            generator1.TryAddFluidLiters(1,50,100,out _);
            Check(boiler.Emit(3),3,$"saved layout: generator tail / overlapping corner / rotated generator {d}");
            Check(generator2.StoredFluidLiters,3,"rotated downstream generator actual storage");

            World.Reset(); boiler = new Boiler(); boiler.Output(default,d);
            PipeRuntimeRecord.Add(default,-d,turn);
            generator = new SteamGenerator(); generator.Place(turn*2,turn);
            Check(boiler.Emit(3),3,$"boiler output / overlapping corner / rotated generator {d}");
            Check(generator.StoredFluidLiters,3,"corner-fed generator actual storage");

            World.Reset(); boiler = new Boiler(); boiler.Output(default,d);
            PipeRuntimeRecord.Add(default,d,turn); // No connector facing the boiler.
            generator = new SteamGenerator(); generator.Place(turn*2,turn);
            Check(boiler.Emit(3),0,$"overlapping pipe without source-facing connector rejected {d}");
            Check(generator.StoredFluidLiters,0,"disconnected overlapping pipe leaves generator empty");

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
