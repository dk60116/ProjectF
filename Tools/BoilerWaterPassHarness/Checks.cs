using System;
using System.Collections.Generic;
using UnityEngine;

public class ItemDefinition { }
public class FakeGameObject { public bool activeInHierarchy = true; }
public class FakeTransform { public Quaternion rotation = Quaternion.identity; }
public static class MapClimate { public static float CurrentWaterTemperatureCelsius => 20; }
public static class MapObjectTickProfiler
{
    public static Scope SampleNamed(string kind, string typeName, string itemName) => default;
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}
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
    public enum RectGridBlockType { Object = 1, PipeOutput = 7, PipeInput = 11 }
    public struct RectGridBlockPlacement { public int x, y; public RectGridBlockType blockType; }
    private static readonly Vector2Int[] FluidCardinalDirections =
        { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private static readonly Dictionary<Vector2Int, Pipe> DirectedSteamPipes = new();
    private static readonly Dictionary<DirectedSteamPort, SteamGenerator> DirectedSteamGenerators = new();
    public readonly List<RectGridBlockPlacement> RectGridPlacements = new();
    public Vector2Int Anchor;
    public int Rotation;
    public bool Placed = true;
    public readonly HashSet<Vector2Int> Passed = new();
    public readonly List<InstallationObject> DirectedSteamOutputs = new();
    protected readonly List<Vector2Int> runtimeOutputCoordinates = new() { Vector2Int.zero };
    private readonly List<FluidOutputConnection> cachedFluidOutputConnections = new();
    private int connectedFluidSearchCurrentPipeCount = 0;
    private bool fluidOutputCapacityBlocked;
    private readonly Queue<DirectedSteamPort> directedSteamPortSearchQueue = new();
    private readonly HashSet<DirectedSteamPort> directedSteamVisitedPorts = new();
    private readonly Queue<Vector2Int> directedSteamPipeSearchQueue = new();
    private readonly HashSet<Vector2Int> directedSteamVisitedPipeCoordinates = new();
    protected bool TryGetPlacementRuntime(out Vector2Int anchor, out int rotation)
    { anchor = Anchor; rotation = Rotation; return Placed; }
    public static Vector2Int Rotate(Vector2Int value, int rotation)
    { for (int i = 0; i < rotation; i++) value = new(value.y, -value.x); return value; }
    protected bool TryGetRectGridPlacementCoordinate(InputOutputModule source, Vector2Int anchor, int rotation, RectGridBlockPlacement placement, out Vector2Int coordinate)
    { coordinate = anchor + Rotate(new(placement.x, placement.y), rotation); return true; }
    private bool ContainsRuntimePipeAreaBlockCoordinate(Vector2Int coordinate) => true;
    private void EnqueueConnectedFluidSearchCoordinate(Vector2Int coordinate, int pipeCount) => Passed.Add(coordinate);
    private void EnqueueSteamGeneratorPipePassCoordinates(SteamGenerator generator) => Passed.Add(generator.Anchor);
    private bool EnsureFluidOutputStorageCache() => cachedFluidOutputConnections.Count > 0;
    private static bool IsFluidItemId(int id) => id >= 0;
    protected void RecordFluidNetworkOutput(int id, float liters) { }
    private void AddFluidOutputStorageCacheCandidate(InstallationObject storage, int pipeDistance)
    {
        if (storage != null && !DirectedSteamOutputs.Contains(storage)) DirectedSteamOutputs.Add(storage);
    }
    private bool TryGetConnectedPipeAtCoordinate(
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion rotation,
        out PipeRuntimeRecord record)
    {
        rotation = Quaternion.identity;
        record = null;
        return DirectedSteamPipes.TryGetValue(coordinate, out pipe);
    }
    private static bool HasConnectedPipeConnectionTowards(
        Pipe pipe,
        PipeRuntimeRecord record,
        Vector2Int coordinate,
        Quaternion rotation,
        Vector2Int direction) => pipe != null && pipe.HasConnectionTowardsAt(coordinate, rotation, direction);
    private static bool TryGetConnectedPipeRemoteCoordinate(
        Pipe pipe,
        PipeRuntimeRecord record,
        Vector2Int coordinate,
        out Vector2Int remoteCoordinate) => pipe.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate);
    private static bool TryFindDirectedSteamGenerator(
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection,
        ISet<SteamGenerator> visited,
        out SteamGenerator generator)
    {
        return DirectedSteamGenerators.TryGetValue(
                   new DirectedSteamPort(sourcePortCoordinate, flowDirection),
                   out generator)
               && (visited == null || !visited.Contains(generator));
    }
    public static void ResetDirectedSteamNetwork()
    {
        DirectedSteamPipes.Clear();
        DirectedSteamGenerators.Clear();
    }
    public static void AddDirectedSteamPipe(Vector2Int coordinate, Pipe pipe) =>
        DirectedSteamPipes[coordinate] = pipe;
    public static void AddDirectedSteamGeneratorPort(
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection,
        SteamGenerator generator) =>
        DirectedSteamGenerators[new DirectedSteamPort(sourcePortCoordinate, flowDirection)] = generator;
    public static void TraverseDirectedSteam(
        Boiler boiler,
        HashSet<SteamGenerator> visited,
        InputOutputModule outputOwner) =>
        AddDirectedBoilerSteamChains(boiler, visited, outputOwner);
    protected void SetFluidOutputStorages(IEnumerable<InstallationObject> storages)
    {
        cachedFluidOutputConnections.Clear();
        foreach (InstallationObject storage in storages)
            if (storage != null) cachedFluidOutputConnections.Add(new FluidOutputConnection(storage, 0));
    }
    public void Traverse(InputOutputModule module, Vector2Int coordinate) => EnqueueFluidStoragePipePassCoordinatesAt(new[] { module }, coordinate);
}
public partial class SteamGenerator : InputOutputModule
{
    public bool HasTail;
    public Vector2Int TailCoordinate;
    public Vector2Int TailDirection;
    public bool TryGetRuntimePipePassTail(out Vector2Int coordinate, out Vector2Int direction)
    {
        coordinate = TailCoordinate;
        direction = TailDirection;
        return HasTail;
    }
}
public partial class SteamTrain : InstallationObject
{
    public bool CanAcceptWaterFromPipeDirection(Vector2Int direction, int id, bool space) => false;
    public bool ResolvePipe(Vector2Int coordinate, out Pipe pipe, out PipeRuntimeRecord record) =>
        TryGetActivePipeAtCoordinate(coordinate, out pipe, out _, out record);
}
public class Pipe : InstallationObject
{
    public readonly HashSet<Vector2Int> Connections = new();
    public bool HasRemote;
    public Vector2Int Remote;
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Quaternion rotation, Vector2Int direction) =>
        Connections.Contains(direction);
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote)
    {
        remote = Remote;
        return HasRemote;
    }
}
public sealed class PipeRuntimeRecord
{
    public Pipe Prototype;
    public Quaternion WorldRotation = Quaternion.identity;
}
public sealed class PipeWorld
{
    public static PipeWorld Current;
    public readonly Dictionary<Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord record) =>
        Records.TryGetValue(coordinate, out record);
}
public sealed class Block
{
    public PipeRuntimeRecord Record;
    public Pipe LegacyPipe;
    public Quaternion LegacyRotation = Quaternion.identity;
    public bool TryGetRuntimePipeRecord(out PipeRuntimeRecord record)
    {
        record = Record;
        return record != null;
    }
    public bool TryGetRuntimePipe(out Pipe pipe, out Quaternion rotation)
    {
        pipe = LegacyPipe;
        rotation = LegacyRotation;
        return pipe != null;
    }
}
public sealed class TerrainGenerator
{
    public static TerrainGenerator Active;
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) =>
        Blocks.TryGetValue(coordinate, out block);
}
public partial class Boiler : InputOutputModule
{
    private const float FluidEpsilon = .0001f, MaxWaterTemperatureCelsiusValue = 100;
    private float waterTemperatureCelsius = 20;
    private readonly Dictionary<Vector2Int, Vector2Int> steamOutputDirections = new();
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
    public void SetSteamOutput(Vector2Int coordinate, Vector2Int direction)
    {
        runtimeOutputCoordinates.Clear();
        runtimeOutputCoordinates.Add(coordinate);
        steamOutputDirections[coordinate] = direction;
    }
    public bool TryGetRuntimePipeOutputExternalDirection(Vector2Int coordinate, out Vector2Int direction) =>
        steamOutputDirections.TryGetValue(coordinate, out direction);
    public bool Heat(float dt) => IsWaterStorageFull(1);
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
        CheckDirectedSteamGeneratorConnections();
        CheckSteamGeneratorPipePassConnections();
        CheckSteamGenerationOutputScale();
        CheckDirectedSteamPipePassage();
        CheckSteamTrainPipeWorldLookup();
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
        Console.WriteLine($"PASS: {checks} boiler water pass, directed steam pipe, continuous upstream consumption, heating and conservation checks (2/3 boilers, four rotations). No engine launched.");
    }

    private static void CheckSteamGenerationOutputScale()
    {
        Require(
            Math.Abs(SteamGenerator.ResolveGenerationOutputScale(.5f, 1f) - .5f) < .0001f,
            "half a scheduled steam interval produces half configured power");
        Require(
            Math.Abs(SteamGenerator.ResolveGenerationOutputScale(1f, 1f) - 1f) < .0001f,
            "a complete scheduled steam interval produces full configured power");
        Require(
            SteamGenerator.ResolveGenerationOutputScale(0f, 1f) == 0f
            && SteamGenerator.ResolveGenerationOutputScale(1f, 0f) == 0f,
            "empty or invalid steam intervals produce no power");
    }

    private static void CheckSteamTrainPipeWorldLookup()
    {
        Vector2Int coordinate = new(31, -12);
        var ecsPipe = new Pipe();
        var ecsRecord = new PipeRuntimeRecord { Prototype = ecsPipe };
        PipeWorld.Current = new PipeWorld();
        PipeWorld.Current.Records.Add(coordinate, ecsRecord);
        TerrainGenerator.Active = null;

        var train = new SteamTrain();
        Require(
            train.ResolvePipe(coordinate, out Pipe resolvedPipe, out PipeRuntimeRecord resolvedRecord)
            && ReferenceEquals(resolvedPipe, ecsPipe)
            && ReferenceEquals(resolvedRecord, ecsRecord),
            "steam train must find an ECS pipe without a loaded Block binding");

        PipeWorld.Current = null;
        TerrainGenerator.Active = new TerrainGenerator();
        var legacyPipe = new Pipe();
        TerrainGenerator.Active.Blocks.Add(
            coordinate,
            new Block { LegacyPipe = legacyPipe });
        Require(
            train.ResolvePipe(coordinate, out resolvedPipe, out resolvedRecord)
            && ReferenceEquals(resolvedPipe, legacyPipe)
            && resolvedRecord == null,
            "steam train keeps the legacy loaded-Block pipe fallback");
    }

    private static Pipe Straight(Vector2Int axis)
    {
        var pipe = new Pipe();
        pipe.Connections.Add(axis);
        pipe.Connections.Add(-axis);
        return pipe;
    }

    private static void CheckDirectedSteamPipePassage()
    {
        InputOutputModule.ResetDirectedSteamNetwork();
        var boiler = new Boiler(Vector2Int.zero, 0);
        boiler.SetSteamOutput(Vector2Int.zero, Vector2Int.right);
        InputOutputModule.AddDirectedSteamPipe(Vector2Int.zero, Straight(Vector2Int.right));
        var corner = new Pipe();
        corner.Connections.Add(Vector2Int.left);
        corner.Connections.Add(Vector2Int.up);
        InputOutputModule.AddDirectedSteamPipe(Vector2Int.right, corner);
        InputOutputModule.AddDirectedSteamPipe(Vector2Int.one, Straight(Vector2Int.up));

        var first = new SteamGenerator
        {
            HasTail = true,
            TailCoordinate = new Vector2Int(1, 3),
            TailDirection = Vector2Int.up
        };
        InputOutputModule.AddDirectedSteamGeneratorPort(
            Vector2Int.one,
            Vector2Int.up,
            first);

        InputOutputModule.AddDirectedSteamPipe(new Vector2Int(1, 3), Straight(Vector2Int.up));
        InputOutputModule.AddDirectedSteamPipe(new Vector2Int(1, 4), Straight(Vector2Int.up));
        var second = new SteamGenerator();
        InputOutputModule.AddDirectedSteamGeneratorPort(
            new Vector2Int(1, 4),
            Vector2Int.up,
            second);

        var outputOwner = new InputOutputModule();
        var visited = new HashSet<SteamGenerator>();
        InputOutputModule.TraverseDirectedSteam(boiler, visited, outputOwner);
        Require(visited.SetEquals(new[] { first, second }),
            "steam traversal follows pipe corners and resumes after a generator tail");
        Require(outputOwner.DirectedSteamOutputs.Count == 2,
            "boiler output cache receives every generator reached through the steam pipe route");

        InputOutputModule.ResetDirectedSteamNetwork();
        boiler.SetSteamOutput(Vector2Int.zero, Vector2Int.right);
        InputOutputModule.AddDirectedSteamPipe(Vector2Int.right, Straight(Vector2Int.up));
        InputOutputModule.AddDirectedSteamGeneratorPort(Vector2Int.right, Vector2Int.right, first);
        visited.Clear();
        outputOwner.DirectedSteamOutputs.Clear();
        InputOutputModule.TraverseDirectedSteam(boiler, visited, outputOwner);
        Require(visited.Count == 0 && outputOwner.DirectedSteamOutputs.Count == 0,
            "pipe without a reciprocal connector does not carry boiler steam");
    }

    private static void CheckDirectedSteamGeneratorConnections()
    {
        Vector2Int source = new(11, -7);
        for (int rotation = 0; rotation < 4; rotation++)
        {
            Vector2Int flow = InputOutputModule.Rotate(Vector2Int.right, rotation);
            Vector2Int side = InputOutputModule.Rotate(Vector2Int.up, rotation);

            Require(
                SteamGenerator.IsDirectedSteamPortConnection(
                    source,
                    flow,
                    source + flow,
                    source,
                    flow,
                    flow),
                "overlapping output/input must connect");
            Require(
                SteamGenerator.IsDirectedSteamPortConnection(
                    source,
                    flow,
                    source + flow * 2,
                    source + flow,
                    flow,
                    flow),
                "one-cell forward input must connect");
            Require(
                SteamGenerator.IsDirectedSteamPortConnection(
                    source,
                    flow,
                    source,
                    source - flow,
                    flow,
                    flow),
                "dense serial center overlap must connect");
            Require(
                !SteamGenerator.IsDirectedSteamPortConnection(
                    source,
                    flow,
                    source + side,
                    source + side - flow,
                    flow,
                    flow),
                "parallel side neighbour must not connect");
            Require(
                !SteamGenerator.IsDirectedSteamPortConnection(
                    source,
                    flow,
                    source,
                    source - flow,
                    -flow,
                    -flow),
                "reverse-facing generator must not connect");
            Require(
                !SteamGenerator.IsDirectedSteamPortConnection(
                    source,
                    flow,
                    source + flow,
                    source - flow,
                    flow,
                    flow),
                "behind-port input without center overlap must not connect");
        }
    }

    private static void CheckSteamGeneratorPipePassConnections()
    {
        Vector2Int endpoint = new(4, -9);
        for (int rotation = 0; rotation < 4; rotation++)
        {
            Vector2Int external = InputOutputModule.Rotate(Vector2Int.right, rotation);
            Require(
                SteamGenerator.TryResolveSteamPassPipeConnectionDirection(
                    endpoint,
                    endpoint,
                    external,
                    out Vector2Int overlappingDirection)
                && overlappingDirection == -external,
                "overlapping pipe connects towards the generator body");
            Require(
                SteamGenerator.TryResolveSteamPassPipeConnectionDirection(
                    endpoint + external,
                    endpoint,
                    external,
                    out Vector2Int adjacentDirection)
                && adjacentDirection == -external,
                "adjacent pipe connects from the external side of a generator port");
            Require(
                !SteamGenerator.TryResolveSteamPassPipeConnectionDirection(
                    endpoint - external,
                    endpoint,
                    external,
                    out _),
                "pipe behind a generator port cannot enter its pass-through edge");
            Require(
                !SteamGenerator.TryResolveSteamPassPipeConnectionDirection(
                    endpoint + InputOutputModule.Rotate(Vector2Int.up, rotation),
                    endpoint,
                    external,
                    out _),
                "side pipe cannot enter a generator pass-through edge");
        }
    }
}
