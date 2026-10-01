using System;
using System.Collections.Generic;
using UnityEngine;

// World lookups are replaced by a four-neighbour fixture; selection and
// continuation/port rules are extracted unchanged from production by Run.ps1.
namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int zero => new(0, 0);
        public static Vector2Int up => new(0, 1);
        public static Vector2Int right => new(1, 0);
        public static Vector2Int down => new(0, -1);
        public static Vector2Int left => new(-1, 0);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a) => new(-a.x, -a.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
    }

    public readonly record struct Quaternion(int Turns)
    {
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => new((int)(y / 90));
        public static Quaternion operator *(Quaternion a, Quaternion b) => new(a.Turns + b.Turns);
        public static float Dot(Quaternion a, Quaternion b) => ((a.Turns - b.Turns) & 3) == 0 ? 1 : 0;
    }
    public static class Mathf { public static float Abs(float value) => Math.Abs(value); }
}

public class MapObject { public GameObject gameObject = new(); public Transform transform = new(); }
public class Transform { public Quaternion rotation; }
public class Fluidtank : MapObject { public int StoredFluidItemId = -1; public bool IsFlatCarMounted; }
public class ItemDefinition { public MapObject mapObject; }
public class GameObject { public Scene scene = new(); }
public class Scene { public bool Valid; public bool IsValid() => Valid; }
public enum PipeVariantKind { Straight, Corner, Tee, Cross }
public class Pipe : MapObject
{
    public PipeVariantKind VariantKind;
    public int VariantKindId => (int)VariantKind;
    public int Mask;
    public int ManualMask = -1;
    public int Fluid = -1;
    public long Sequence;
    public bool Preview;
    public Pipe StraightVariantPrefab, CornerVariantPrefab, TeeVariantPrefab, CrossVariantPrefab;
    public bool TryGetPlacementRuntime(out Vector2Int coordinate, out int turns)
    { coordinate = default; turns = 0; return false; }
    public bool TryGetObjectInfoFluidItemId(out int fluid) { fluid = Fluid; return fluid >= 0; }
    public bool TryGetObjectInfoFluidInfoAtCoordinate(Vector2Int coordinate, out int fluid,
        out float temperature, out float pressure, bool includePressure)
    {
        fluid = -1; temperature = pressure = 0;
        var world = PipeWorld.Current;
        if (world == null) return false;
        var visited = new HashSet<Vector2Int> { coordinate };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(coordinate);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (world.Sources.TryGetValue(current, out fluid)) return true;
            if (!world.Records.TryGetValue(current, out var record)) continue;
            foreach (var direction in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                var next = current + direction;
                if (record.HasConnectionTowardsAt(current, direction)
                    && world.Records.TryGetValue(next, out var neighbor)
                    && neighbor.HasConnectionTowardsAt(next, -direction) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        fluid = -1;
        return false;
    }
    public int GetConnectionMask(Quaternion rotation)
    {
        int result = 0;
        var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        for (int i = 0; i < 4; i++) if (HasConnectionTowards(rotation, directions[i])) result |= 1 << i;
        return result;
    }
    public virtual bool HasConnectionTowardsAt(Vector2Int coordinate, Quaternion rotation, Vector2Int direction) =>
        HasConnectionTowards(rotation, direction);
    public virtual bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote)
    { remote = default; return false; }

    public bool HasConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        int directionIndex = direction == Vector2Int.up ? 0
            : direction == Vector2Int.right ? 1 : direction == Vector2Int.down ? 2 : 3;
        return (Mask & (1 << ((directionIndex - rotation.Turns + 8) & 3))) != 0;
    }
}
public sealed class UndergroundPipe : Pipe
{
    public Vector2Int[] Pair;
    public override bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote)
    {
        remote = default;
        return Pair != null && TryResolveRemoteCoordinate(Pair[0], Pair[1], coordinate, out remote);
    }
    public static bool TryResolveRemoteCoordinate(Vector2Int first, Vector2Int second, Vector2Int coordinate, out Vector2Int remote)
    { remote = coordinate == first ? second : first; return coordinate == first || coordinate == second; }
    public static bool IsValidPairGeometry(Vector2Int a, Vector2Int b, int limit) =>
        a != b && (a.x == b.x || a.y == b.y);
}
public class InputOutputModule : MapObject
{
    public enum RectGridBlockType { PipeInput }
    public struct RectGridBlockPlacement { public RectGridBlockType blockType; public int Index; }
}
public sealed class Pump : InputOutputModule
{
    public long RuntimePlacementSequence;
    public Vector2Int First, Second, FirstOut, SecondOut;
    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements = new[]
    { new RectGridBlockPlacement { Index = 0 }, new RectGridBlockPlacement { Index = 1 } };
    public bool TryGetRectGridPlacementCoordinate(MapObject source, Vector2Int anchor, int turns,
        RectGridBlockPlacement placement, out Vector2Int coordinate)
    { coordinate = placement.Index == 0 ? First : Second; return true; }
    public bool TryGetPipePassAt(MapObject source, Vector2Int anchor, int turns, Vector2Int coordinate,
        out Vector2Int other, out Vector2Int external)
    {
        other = coordinate == First ? Second : First;
        external = coordinate == First ? FirstOut : SecondOut;
        return coordinate == First || coordinate == Second;
    }
}
public sealed class BlockStateStore
{
    public sealed class InstallationSaveState
    {
        public Vector2Int anchorCoordinate;
        public int conveyorVariantKind = -1, pipeConnectionMask = -1;
        public Pipe Prototype;
        public List<Vector2Int> occupiedCoordinates;
    }
}
public sealed class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
    public readonly Dictionary<Vector2Int, BlockStateStore.InstallationSaveState> Saved = new();
    public bool TryGetSavedPipeInstallationStateAtCoordinate(Vector2Int coordinate, out BlockStateStore.InstallationSaveState state) =>
        Saved.TryGetValue(coordinate, out state);
    public bool TryGetInstallationStateAtCoordinate(Vector2Int coordinate, out BlockStateStore.InstallationSaveState state) =>
        Saved.TryGetValue(coordinate, out state);
}
public sealed class Block
{
    public PipeRuntimeRecord Record;
    public Pipe ScenePipe;
    public bool TryGetRuntimePipe(out Pipe pipe, out Quaternion rotation)
    { pipe = Record?.Prototype ?? ScenePipe; rotation = Record?.Rotation ?? default; return pipe != null; }
    public bool TryGetRuntimePipeRecord(out PipeRuntimeRecord record) { record = Record; return record != null; }
}
public sealed partial class PipeRuntimeRecord
{
    public Pipe Prototype;
    public Vector2Int AnchorCoordinate => First;
    public Vector2Int First, Second;
    public Quaternion Rotation;
    public bool TryGetRemoteConnectionCoordinate(Vector2Int coordinate, out Vector2Int remote) =>
        UndergroundPipe.TryResolveRemoteCoordinate(First, Second, coordinate, out remote);
    public bool HasConnectionTowardsAt(Vector2Int coordinate, Vector2Int direction)
    {
        if (Prototype is not UndergroundPipe) return Prototype.HasConnectionTowards(Rotation, direction);
        if (!TryGetRemoteConnectionCoordinate(coordinate, out Vector2Int remote)) return false;
        return direction == new Vector2Int(Math.Sign(coordinate.x - remote.x), Math.Sign(coordinate.y - remote.y));
    }
}
public sealed class PipeWorld
{
    public static PipeWorld Current;
    public readonly Dictionary<Vector2Int, int> Sources = new();
    public readonly Dictionary<Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetMatchingAtCoordinate(Vector2Int coordinate, Pipe pipe, out PipeRuntimeRecord record)
    {
        if (Records.TryGetValue(coordinate, out record) && ReferenceEquals(record.Prototype, pipe)) return true;
        record = null;
        return false;
    }
}

public partial class InstallationPlacementController
{
    private bool isResolvingInstalledPipeVariantPreviewPlans;
    private readonly Dictionary<Vector2Int, int> pipeBlueprintInstalledFluidItemIds = new();
    public void BeginBlueprintResolution()
    { pipeBlueprintInstalledFluidItemIds.Clear(); isResolvingInstalledPipeVariantPreviewPlans = true; }
    public void EndBlueprintResolution()
    { pipeBlueprintInstalledFluidItemIds.Clear(); isResolvingInstalledPipeVariantPreviewPlans = false; }
    private static readonly Vector2Int[] PipeCardinalDirections =
        { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private readonly int[] adjacentPipeFluidItemIdsScratch = new int[4];
    private readonly long[] adjacentPipePlacementSequencesScratch = new long[4];
    private readonly int[] adjacentPipeContinuationPrioritiesScratch = new int[4];
    private readonly Dictionary<Vector2Int, Pipe> neighbors = new();
    private readonly Dictionary<Vector2Int, Pump> pumps = new();
    private readonly Dictionary<Vector2Int, int> tanks = new();
    private readonly TerrainGenerator terrain = new();
    private const int MaxPipeFluidCompatibilitySearchNodes = 1024;
    private readonly Queue<Vector2Int> pipeFluidCompatibilityQueue = new();
    private readonly HashSet<Vector2Int> pipeFluidCompatibilityVisited = new();
    private readonly HashSet<int> adjacentPipeBranchFluidItemIdsScratch = new();
    public int EndpointFluid = -1;

    public void SetNeighbor(int direction, Pipe pipe) => neighbors[PipeCardinalDirections[direction]] = pipe;
    public void SetPipe(Vector2Int coordinate, Pipe pipe) => neighbors[coordinate] = pipe;
    public void SetInstalledPipe(Vector2Int coordinate, Pipe pipe, int fluid)
    {
        SetPipe(coordinate, pipe);
        var record = new PipeRuntimeRecord { Prototype = pipe };
        terrain.Blocks[coordinate] = new Block { Record = record };
        PipeWorld.Current ??= new();
        PipeWorld.Current.Records[coordinate] = record;
        if (fluid >= 0) PipeWorld.Current.Sources[coordinate] = fluid;
    }
    public void SetTank(Vector2Int coordinate, int fluid) => tanks[coordinate] = fluid;
    private readonly Dictionary<Vector2Int, int> inputAreas = new();
    public void SetInputArea(Vector2Int coordinate, int fluid) => inputAreas[coordinate] = fluid;
    public bool CanPlacePump(Pump pump) =>
        CanProposedPumpConnectionsMatchExisting(default, pump, 0, pump, pump);
    public void AddPump(Vector2Int first, Vector2Int firstOut, Vector2Int second, Vector2Int secondOut)
    {
        var pump = new Pump { First = first, Second = second, FirstOut = firstOut, SecondOut = secondOut };
        pumps[first] = pumps[second] = pump;
    }
    private bool TryGetPumpPlacementPassAtCoordinate(Vector2Int coordinate, MapObject ignored, out Pump pump,
        out Vector2Int other, out Vector2Int external)
    {
        other = external = default;
        if (!pumps.TryGetValue(coordinate, out pump)) return false;
        other = coordinate == pump.First ? pump.Second : pump.First;
        external = coordinate == pump.First ? pump.FirstOut : pump.SecondOut;
        return true;
    }
    public void AddUndergroundPair(UndergroundPipe prototype, Vector2Int first, Vector2Int second, bool saved = false)
    {
        SetPipe(first, prototype);
        SetPipe(second, prototype);
        if (saved)
        {
            var state = new BlockStateStore.InstallationSaveState
                { Prototype = prototype, occupiedCoordinates = new() { first, second } };
            terrain.Saved[first] = state;
            terrain.Saved[second] = state;
        }
        else
        {
            PipeWorld.Current ??= new();
            var record = new PipeRuntimeRecord { Prototype = prototype, First = first, Second = second };
            PipeWorld.Current.Records[first] = record;
            PipeWorld.Current.Records[second] = record;
        }
    }
    public bool Collect(Vector2Int coordinate, Pipe pipe, out HashSet<int> fluids)
    {
        fluids = new();
        bool constrained = false;
        return TryCollectPipeNetworkFluidConstraints(coordinate, pipe, default, null, fluids,
            ref constrained, false, default, default);
    }
    public bool CanPlace(Pipe pipe) => CanPipePlacementFluidConnectionsMatch(Vector2Int.zero, pipe, Quaternion.identity, null);
    public bool CanPlaceAt(Pipe pipe, Quaternion rotation, int baseline)
        => CanPipePlacementFluidConnectionsMatch(Vector2Int.zero, pipe, rotation, null, baseline);
    private readonly Dictionary<Vector2Int, int> fluidSources = new();
    public void SetFluidSource(Vector2Int coordinate, int fluid) => fluidSources[coordinate] = fluid;
    public bool HasPort(Vector2Int coordinate, Pipe pipe, Vector2Int direction) =>
        AuthoritativePipeHasConnectionTowardsAt(coordinate, pipe, Quaternion.identity, false, direction);
    public bool CandidatePort(Vector2Int coordinate, Pipe pipe, Quaternion rotation, Vector2Int direction)
        => HasEffectivePipeConnectionTowardsAt(coordinate, pipe, rotation, direction);
    public int Select(int anchorFluid = -1, bool preview = false) =>
        ResolvePreferredPipeVariantFluidItemId(Vector2Int.zero, anchorFluid, preview ? new Pipe() : null);

    private bool TryGetPipeEndpointFluidItemIdAtCoordinate(Vector2Int coordinate, MapObject ignored, out int fluid)
    {
        fluid = EndpointFluid;
        return fluid >= 0;
    }

    private TerrainGenerator ResolveInstallPreviewTerrain() => terrain;
    private bool TryGetPipePlacementAtCoordinate(Vector2Int coordinate, MapObject ignored, out Pipe pipe, out Quaternion rotation)
    {
        rotation = default;
        if (neighbors.TryGetValue(coordinate, out pipe) && pipe != ignored) return true;
        pipe = null;
        return false;
    }

    private bool TryMergePipeAreaFluidConstraintsAtPipeCoordinate(Vector2Int coordinate,
        Pipe pipe, Quaternion rotation, MapObject ignored, HashSet<int> fluids, ref bool constrained)
    {
        int fluid = fluidSources.TryGetValue(coordinate, out int source) ? source : pipe.Fluid;
        return fluid < 0 || TryMergeFluidCompatibilityConstraint(fluids, ref constrained,
            new HashSet<int> { fluid });
    }

    private bool TryCollectAdjacentPipeConnectionFluidConstraints(Vector2Int coordinate,
        Vector2Int direction, MapObject ignored, HashSet<int> fluids, ref bool constrained) =>
        TryMergeRuntimeAdjacentFluidStorageConstraint(coordinate + direction, -direction, fluids, ref constrained);
    private bool TryCollectAdjacentPipeAreaFluidConstraints(Vector2Int coordinate,
        Vector2Int direction, MapObject ignored, HashSet<int> fluids, ref bool constrained) =>
        !inputAreas.TryGetValue(coordinate, out int fluid)
        || TryMergeFluidCompatibilityConstraint(fluids, ref constrained, new HashSet<int> { fluid });
    private bool TryMergeRuntimeAdjacentFluidStorageConstraint(Vector2Int coordinate, Vector2Int direction,
        HashSet<int> fluids, ref bool constrained) => !tanks.TryGetValue(coordinate, out int fluid) || fluid < 0
        || TryMergeFluidCompatibilityConstraint(fluids, ref constrained, new HashSet<int> { fluid });
    private bool TryMergeAdjacentFluidStorageSnapshotConstraint(Vector2Int coordinate, Vector2Int direction,
        MapObject ignored, HashSet<int> fluids, ref bool constrained) => true;
    private sealed class PlacementSnapshot { public MapObject mapObject; public int storedFluidItemId = -1; }
    private bool TryGetFluidTankPlacementSnapshotAtCoordinate(Vector2Int coordinate, MapObject ignored,
        out PlacementSnapshot snapshot)
    {
        snapshot = null;
        if (!tanks.TryGetValue(coordinate, out int fluid)) return false;
        snapshot = new PlacementSnapshot { mapObject = new Fluidtank { StoredFluidItemId = fluid }, storedFluidItemId = fluid };
        return true;
    }
    private static int ResolveSnapshotStoredFluidItemId(PlacementSnapshot snapshot) => snapshot.storedFluidItemId;
    private bool TryGetFixedFluidConnectorCompatibilityAtCoordinate(Vector2Int coordinate, Vector2Int direction, out bool compatible)
    { compatible = tanks.ContainsKey(coordinate); return compatible; }
    private static bool CanPipeAreaBlocksConnect(Vector2Int first, Vector2Int second) => true;
    private bool TryResolveFluidTankBlueprintPipeOutputConnection(Vector2Int tankCoordinate,
        Vector2Int neighborCoordinate, Vector2Int direction, MapObject ignored, out int fluid)
        => fluidSources.TryGetValue(neighborCoordinate, out fluid);
    private bool TryCreateSavedPlacementSnapshot(BlockStateStore.InstallationSaveState state, out PlacementSnapshot snapshot)
    { snapshot = new PlacementSnapshot { mapObject = state.Prototype }; return true; }

    private long ResolvePipePlacementSequenceAtCoordinate(Vector2Int coordinate, Pipe pipe,
        MapObject ignored)
    {
        return pipe.Sequence;
    }

    private bool TryGetPreviewAnchorCoordinate(MapObject preview, out Vector2Int anchor)
    {
        anchor = Vector2Int.zero;
        return preview is Pipe;
    }

    private bool TryGetManualPipeConnectionMask(Pipe pipe, Vector2Int coordinate, out int mask)
    {
        mask = pipe.ManualMask;
        return mask >= 0;
    }
    private static bool TryGetInstalledPipeVariantPreview(Pipe installed, out Pipe preview, out Quaternion rotation)
    { preview = null; rotation = default; return false; }

}
