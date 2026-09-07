using System;
using System.Collections.Generic;
using UnityEngine;

public class ItemDefinition { }
public class FakeGameObject { public bool activeInHierarchy = true; }
public class FakeTransform { public Quaternion rotation = Quaternion.identity; }
public static class MapClimate { public static float CurrentWaterTemperatureCelsius => 20; }
public class InstallationObject
{
    public FakeGameObject gameObject = new();
    public FakeTransform transform = new();
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
    public enum RectGridBlockType { Object = 1, PipeOutput = 7, PipeInput = 11 }
    public struct RectGridBlockPlacement { public int x, y; public RectGridBlockType blockType; }
    public readonly List<RectGridBlockPlacement> RectGridPlacements = new();
    public Vector2Int Anchor;
    public int Rotation;
    public bool Placed = true;
    public readonly HashSet<Vector2Int> Passed = new();
    protected bool TryGetPlacementRuntime(out Vector2Int anchor, out int rotation)
    { anchor = Anchor; rotation = Rotation; return Placed; }
    public static Vector2Int Rotate(Vector2Int value, int rotation)
    { for (int i = 0; i < rotation; i++) value = new(value.y, -value.x); return value; }
    protected bool TryGetRectGridPlacementCoordinate(InputOutputModule source, Vector2Int anchor, int rotation, RectGridBlockPlacement placement, out Vector2Int coordinate)
    { coordinate = anchor + Rotate(new(placement.x, placement.y), rotation); return true; }
    private bool ContainsRuntimePipeAreaBlockCoordinate(Vector2Int coordinate) => true;
    private void EnqueueConnectedFluidSearchCoordinate(Vector2Int coordinate) => Passed.Add(coordinate);
    private void EnqueueSteamGeneratorPipePassCoordinates(SteamGenerator generator) => Passed.Add(generator.Anchor);
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
    private const float FluidEpsilon = .0001f;
    private readonly Queue<Vector2Int> fluidSearchQueue = new();
    private readonly HashSet<Vector2Int> fluidSearchVisited = new();
    private readonly HashSet<InstallationObject> fluidSearchStorageCandidates = new();
    private static readonly Vector2Int[] CardinalDirections = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
    public readonly List<Vector2Int> RuntimeOutputCoordinates = new();
    public readonly Dictionary<Vector2Int, InstallationObject> Ports = new(), Bodies = new();
    public bool HasRuntimeOutputCoordinates => RuntimeOutputCoordinates.Count > 0;
    private Vector2Int localPipeConnectionDirection = Vector2Int.zero;
    private int ResolveWaterItemId() => 1;
    private bool TryResolveDirection(Quaternion rotation, Vector2Int local, out Vector2Int direction) { direction = default; return false; }
    private bool TryGetPipeAtCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion rotation) { pipe = null; rotation = default; return false; }
    private bool TryResolveFluidStorageAtCoordinate(Vector2Int coordinate, int id, bool space, out InstallationObject storage) => Ports.TryGetValue(coordinate, out storage);
    private bool TryResolveFluidStorageBodyAtCoordinate(Vector2Int coordinate, int id, bool space, out InstallationObject storage) => Bodies.TryGetValue(coordinate, out storage);
    public float Supply(float liters) { TryRouteWaterToFluidStorage(liters, true, out float accepted); return accepted; }
    public bool Connects(Vector2Int coordinate, Vector2Int direction) => TryGetFluidNetworkConnectionAtCoordinate(coordinate, direction, 1, out _, out _);
}
public static class Checks
{
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Main()
    {
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
            Require(pump.Connects(entry, InputOutputModule.Rotate(Vector2Int.left, rotation)), "water inlet must connect from its outside");
            Require(!pump.Connects(entry, InputOutputModule.Rotate(Vector2Int.up, rotation)), "sideways entry must stay disconnected");
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
