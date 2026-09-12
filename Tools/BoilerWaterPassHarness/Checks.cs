using System;
using System.Collections.Generic;
using UnityEngine;

public class ItemDefinition { }
public class FakeGameObject { public bool activeInHierarchy = true; }
public class FakeTransform { public Quaternion rotation = Quaternion.identity; }
public static class MapClimate { public static float CurrentWaterTemperatureCelsius => 20; }
public class InstallationObject
{
    private static long nextSimulationId;
    private readonly long simulationId = ++nextSimulationId;
    public static int CompareSimulationOrder(InstallationObject left, InstallationObject right)
        => ReferenceEquals(left, right) ? 0 : left.simulationId.CompareTo(right.simulationId);
    public FakeGameObject gameObject = new();
    public FakeTransform transform = new();
    public bool CanStoreFluid => true;
    public float FluidStorageCapacityLiters = 50, StoredFluidLiters;
    public float AvailableFluidStorageLiters => FluidStorageCapacityLiters - StoredFluidLiters;
    public bool CanProvideFluidItem(int id) => id == 1;
    public bool CanAcceptFluidItem(int id, float amount) => id == 1 && AvailableFluidStorageLiters >= amount;
    public bool TryAddFluidLiters(int id, float amount, float temperature, out float accepted)
    {
        accepted = id == 1 ? Math.Min(amount, AvailableFluidStorageLiters) : 0;
        StoredFluidLiters += accepted;
        return accepted > 0;
    }
}
public partial class InputOutputModule : InstallationObject
{
    private sealed class SharedPumpFluidOutputNetwork
    {
        internal readonly List<InstallationObject> Storages = new();
    }
    private static readonly Dictionary<Vector2Int, SharedPumpFluidOutputNetwork> sharedPumpFluidOutputNetworksByCoordinate = new();
    private static readonly List<SharedPumpFluidOutputNetwork> sharedPumpFluidOutputNetworks = new();
    private static int fluidTopologyVersion = 1, sharedPumpFluidOutputTopologyVersion;
    private static long sharedPumpFluidOutputTopologyBuilds, sharedPumpFluidOutputCacheHits;
    private static int sharedPumpFluidOutputLastBuildNodes, sharedPumpFluidOutputLastBuildStorages;
    public enum RectGridBlockType { Object = 1, PipeOutput = 7, PipeInput = 11 }
    public struct RectGridBlockPlacement { public int x, y; public RectGridBlockType blockType; }
    public readonly List<RectGridBlockPlacement> RectGridPlacements = new();
    public Vector2Int Anchor;
    public int Rotation;
    public bool Placed = true;
    public readonly HashSet<Vector2Int> Passed = new();
    protected readonly List<Vector2Int> runtimeOutputCoordinates = new() { Vector2Int.zero };
    protected readonly List<InstallationObject> cachedFluidOutputStorages = new();
    private readonly HashSet<Vector2Int> connectedFluidSearchVisited = new();
    protected bool TryGetPlacementRuntime(out Vector2Int anchor, out int rotation)
    { anchor = Anchor; rotation = Rotation; return Placed; }
    public static Vector2Int Rotate(Vector2Int value, int rotation)
    { for (int i = 0; i < rotation; i++) value = new(value.y, -value.x); return value; }
    protected bool TryGetRectGridPlacementCoordinate(InputOutputModule source, Vector2Int anchor, int rotation, RectGridBlockPlacement placement, out Vector2Int coordinate)
    { coordinate = anchor + Rotate(new(placement.x, placement.y), rotation); return true; }
    private bool ContainsRuntimePipeAreaBlockCoordinate(Vector2Int coordinate) => true;
    private void EnqueueConnectedFluidSearchCoordinate(Vector2Int coordinate) => Passed.Add(coordinate);
    private void EnqueueSteamGeneratorPipePassCoordinates(SteamGenerator generator) => Passed.Add(generator.Anchor);
    private bool EnsureFluidOutputStorageCache() => cachedFluidOutputStorages.Count > 0;
    private static bool IsFluidItemId(int id) => id >= 0;
    protected void RecordFluidNetworkOutput(int id, float liters) { }
    protected void SetFluidOutputStorages(IEnumerable<InstallationObject> storages)
    {
        cachedFluidOutputStorages.Clear();
        foreach (InstallationObject storage in storages)
            if (storage != null && !cachedFluidOutputStorages.Contains(storage)) cachedFluidOutputStorages.Add(storage);
        cachedFluidOutputStorages.Sort(CompareSimulationOrder);
    }
    public void PublishSharedNetworkForTest(
        Vector2Int seed,
        IEnumerable<Vector2Int> networkCoordinates,
        IEnumerable<InstallationObject> storages)
    {
        runtimeOutputCoordinates[0] = seed;
        SetFluidOutputStorages(storages);
        connectedFluidSearchVisited.Clear();
        foreach (Vector2Int coordinate in networkCoordinates) connectedFluidSearchVisited.Add(coordinate);
        PublishSharedPumpFluidOutputNetwork();
    }
    public bool TryUseSharedNetworkForTest(Vector2Int seed, out int storageCount)
    {
        runtimeOutputCoordinates[0] = seed;
        cachedFluidOutputStorages.Clear();
        bool found = TryUseSharedPumpFluidOutputNetwork();
        storageCount = cachedFluidOutputStorages.Count;
        return found;
    }
    public static void AdvanceFluidTopologyForTest() => fluidTopologyVersion++;
    public static long SharedNetworkBuildsForTest => sharedPumpFluidOutputTopologyBuilds;
    public static long SharedNetworkHitsForTest => sharedPumpFluidOutputCacheHits;
    public void Traverse(InputOutputModule module, Vector2Int coordinate) => EnqueueFluidStoragePipePassCoordinatesAt(new[] { module }, coordinate);
}
public class SteamGenerator : InputOutputModule { }
public class SteamTrain : InstallationObject
{ public bool CanAcceptWaterFromPipeDirection(Vector2Int direction, int id, bool space) => false; }
public class Pipe : InstallationObject
{
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Quaternion rotation, Vector2Int direction) => true;
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote) { remote = default; return false; }
}
public partial class Boiler : InputOutputModule
{
    private const float FluidEpsilon = .0001f, MaxWaterTemperatureCelsiusValue = 100;
    private float waterTemperatureCelsius = 20;
    public float WaterTemperatureCelsius => waterTemperatureCelsius;
    public Boiler(Vector2Int anchor, int rotation)
    {
        Anchor = anchor; Rotation = rotation;
        RectGridPlacements.Add(new() { x = -1, blockType = RectGridBlockType.PipeInput });
        RectGridPlacements.Add(new() { x = 1, blockType = RectGridBlockType.PipeInput });
        RectGridPlacements.Add(new() { y = 1, blockType = RectGridBlockType.PipeOutput });
    }
    private bool TryGetPipePassDirectionAtCoordinate(InputOutputModule source, Vector2Int anchor, int rotation, Vector2Int coordinate, out Vector2Int inward)
    {
        inward = anchor - coordinate;
        return coordinate == anchor + Rotate(Vector2Int.left, rotation) || coordinate == anchor + Rotate(Vector2Int.right, rotation);
    }
    private bool TryConsumeBoilerOperatingEnergy(float dt, ItemDefinition definition, out float energy) { energy = dt; return true; }
    private float ResolveTemperatureGain(float dt, float energy, ItemDefinition definition) => dt * 10;
    private void SetStoredFluidTemperatureCelsius(float temperature) { }
    public bool Heat(float dt) => TryHeatWater(dt, 1, null);
}
public partial class Pump : InputOutputModule
{
    public readonly List<Vector2Int> RuntimeOutputCoordinates = new();
    public readonly Dictionary<Vector2Int, InstallationObject> Ports = new(), Bodies = new();
    public float Supply(float liters)
    {
        var storages = new List<InstallationObject>();
        storages.AddRange(Bodies.Values);
        storages.AddRange(Ports.Values);
        SetFluidOutputStorages(storages);
        TryEmitFluidOutputToConnectedStorages(1, liters, 20f, out float accepted);
        return accepted;
    }
}
public static class Checks
{
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Main()
    {
        var sharedStorage = new InstallationObject();
        var firstPump = new Pump();
        var secondPump = new Pump();
        firstPump.PublishSharedNetworkForTest(
            new Vector2Int(10, 10),
            new[] { new Vector2Int(10, 10), new Vector2Int(11, 10) },
            new[] { sharedStorage });
        Require(
            secondPump.TryUseSharedNetworkForTest(new Vector2Int(11, 10), out int sharedStorageCount)
            && sharedStorageCount == 1,
            "pumps on one water network reuse one topology result");
        Require(
            InputOutputModule.SharedNetworkBuildsForTest == 1
            && InputOutputModule.SharedNetworkHitsForTest == 1,
            "shared water network records one build and one reuse");
        InputOutputModule.AdvanceFluidTopologyForTest();
        Require(
            !secondPump.TryUseSharedNetworkForTest(new Vector2Int(11, 10), out _),
            "water network topology changes invalidate shared results");

        for (int rotation = 0; rotation < 4; rotation++)
        for (int count = 2; count <= 3; count++)
        {
            var pump = new Pump();
            var boilers = new List<Boiler>();
            var origin = new Vector2Int(-13, 7);
            for (int i = 0; i < count; i++)
            {
                var boiler = new Boiler(origin + InputOutputModule.Rotate(new(i * 3, 0), rotation), rotation);
                boilers.Add(boiler);
                pump.Bodies[boiler.Anchor] = boiler;
                foreach (var port in new[] { Vector2Int.left, Vector2Int.right })
                    pump.Ports[boiler.Anchor + InputOutputModule.Rotate(port, rotation)] = boiler;
            }
            Vector2Int entry = origin + InputOutputModule.Rotate(Vector2Int.left, rotation);
            pump.RuntimeOutputCoordinates.Add(entry);
            boilers[0].StoredFluidLiters = 50;
            var network = new InputOutputModule();
            network.Traverse(boilers[0], entry);
            Require(network.Passed.Contains(origin + InputOutputModule.Rotate(Vector2Int.right, rotation)), "connected tank search must cross boiler water ports");
            network.Passed.Clear();
            network.Traverse(boilers[0], origin + InputOutputModule.Rotate(Vector2Int.up, rotation));
            Require(network.Passed.Count == 0, "steam output must not bridge into the water network");
            bool[] heated = new bool[count];
            bool upstreamStayedRunning = true;
            float supplied = 0, consumed = 0;
            for (int tick = 0; tick < 2000; tick++)
            {
                // First boiler continuously converts 5 L/s to steam; pump supplies 15 L/s.
                upstreamStayedRunning &= boilers[0].StoredFluidLiters >= .5f;
                boilers[0].StoredFluidLiters -= .5f;
                consumed += .5f;
                supplied += pump.Supply(1.5f);
                for (int i = 1; i < count; i++) heated[i] |= boilers[i].Heat(.1f);
            }
            Require(upstreamStayedRunning, "first boiler must keep operating");
            for (int i = 1; i < count; i++) Require(heated[i], "downstream boiler must reach full water and start heating");
            float stored = 0;
            foreach (var boiler in boilers) stored += boiler.StoredFluidLiters;
            Require(Math.Abs(stored - (50 + supplied - consumed)) < .02f, "water must be conserved");
        }
        Console.WriteLine($"PASS: {checks} boiler water pass, continuous upstream consumption, heating and conservation checks (2/3 boilers, four rotations). No engine launched.");
    }
}
