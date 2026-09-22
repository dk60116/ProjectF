using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int zero => default;
        public static Vector2Int operator -(Vector2Int v) => new(-v.x, -v.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x-b.x, a.y-b.y);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x+b.x, a.y+b.y);
    }
}
public class MapObject { }
public sealed class Block
{
    public Vector2Int Coordinate;
    public Block(Vector2Int p) { Coordinate = p; }
}
public sealed class TerrainGenerator
{
    public bool TryGetLoadedBlock(Vector2Int p, out Block block) { block = new(p); return true; }
}
public partial class InputOutputModule : MapObject
{
    public enum RectGridBlockType { None, Object, InputEnergy, InputItem, Output,
        PipeInputEnergy, PipeInputItem, PipeOutputItem, DoubleEnergy, DoubleInputItem,
        DoublePipeOutputItem, PipeInput }
    public struct RectGridBlockPlacement { public int x, y; public RectGridBlockType blockType; }
    public readonly List<RectGridBlockPlacement> RectGridPlacements = new();
    public Vector2Int Anchor, InwardDirection;
    public int Turns, Fluid = 1;
    public bool isActiveAndEnabled = true;
    protected static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeAreaCoordinates = new();
    public static void ClearAreas() => registeredRuntimeAreaCoordinates.Clear();
    public static void Register(Vector2Int coordinate, InputOutputModule module)
    {
        if (!registeredRuntimeAreaCoordinates.TryGetValue(coordinate, out var modules))
            registeredRuntimeAreaCoordinates[coordinate] = modules = new();
        modules.Add(module);
    }
    public static bool IsInputItemBlockType(RectGridBlockType t) =>
        t is RectGridBlockType.InputItem or RectGridBlockType.PipeInputItem or RectGridBlockType.DoubleInputItem;
    public static bool IsInputEnergyBlockType(RectGridBlockType t) =>
        t is RectGridBlockType.InputEnergy or RectGridBlockType.PipeInputEnergy or RectGridBlockType.DoubleEnergy;
    public static bool AllowsPipeAreaInteraction(RectGridBlockType t) => (int)t >= 5;
    public static Vector2Int Rotate(Vector2Int p, int turns) => (turns & 3) switch {
        1 => new(p.y, -p.x), 2 => -p, 3 => new(-p.y, p.x), _ => p };
    public bool TryGetPlacementRuntime(out Vector2Int anchor, out int turns)
    { anchor = Anchor; turns = Turns; return true; }
    public bool TryGetRectGridPlacementCoordinate(MapObject source, Vector2Int anchor, int turns,
        RectGridBlockPlacement placement, out Vector2Int coordinate)
    {
        coordinate = anchor + Rotate(new(placement.x - (this is Pump ? 1 : 0), placement.y), turns);
        return true;
    }
    public bool TryGetRectGridBlockTypeAtCoordinate(MapObject source, Vector2Int anchor, int turns,
        Vector2Int coordinate, out RectGridBlockType type)
    {
        foreach (var p in RectGridPlacements)
            if (TryGetRectGridPlacementCoordinate(source, anchor, turns, p, out var at) && at == coordinate)
            { type = p.blockType; return true; }
        type = default; return false;
    }
    public bool TryGetNearestRectGridObjectDirection(MapObject source, Vector2Int anchor, int turns,
        Vector2Int coordinate, out Vector2Int direction)
    {
        if (this is not Pump) { direction = InwardDirection; return direction != default; }
        direction = default;
        foreach (var p in RectGridPlacements)
            if (p.blockType == RectGridBlockType.Object
                && TryGetRectGridPlacementCoordinate(source, anchor, turns, p, out var at))
            {
                var delta = at - coordinate;
                if (Math.Abs(delta.x) + Math.Abs(delta.y) == 1) { direction = delta; return true; }
            }
        return false;
    }
}
public partial class Pump : InputOutputModule
{
    public Pump()
    {
        RectGridPlacements.Add(new() { x = 0, blockType = RectGridBlockType.PipeInput });
        RectGridPlacements.Add(new() { x = 1, blockType = RectGridBlockType.Object });
        RectGridPlacements.Add(new() { x = 2, blockType = RectGridBlockType.Object });
        RectGridPlacements.Add(new() { x = 3, blockType = RectGridBlockType.PipeInput });
    }
}
public partial class InstallationPlacementController
{
    private sealed class PlacementSnapshot
    { public MapObject mapObject; public Vector2Int anchorCoordinate; public int quarterTurns; }
    private readonly record struct PipeAreaBlockCandidate(PlacementSnapshot snapshot, InputOutputModule.RectGridBlockType blockType);
    private enum PipeAreaFluidResolution { UnresolvedFluidEndpoint, Resolved }
    public int NetworkFluid = 1;
    public InputOutputModule ExistingArea;
    private readonly TerrainGenerator terrain = new();
    private static PlacementSnapshot Snapshot(InputOutputModule module) => new()
    { mapObject = module, anchorCoordinate = module.Anchor, quarterTurns = module.Turns };
    public bool Overlap(Pump pump, Vector2Int coordinate, InputOutputModule area, InputOutputModule.RectGridBlockType type) =>
        PumpBodyOverlapsInputArea(coordinate, Snapshot(pump), InputOutputModule.RectGridBlockType.Object, Snapshot(area), type);
    public bool Resolve(Block clicked, Pump pump, out Block anchor, out int turns) =>
        TryResolveSimpleRectGridInstallPreviewTargetFast(clicked, pump, pump, pump.Turns, true, out anchor, out turns);
    private bool TryGetPipeAreaObjectDirection(Vector2Int p, PipeAreaBlockCandidate candidate, out Vector2Int direction)
    { direction = ((InputOutputModule)candidate.snapshot.mapObject).InwardDirection; return direction != default; }
    private PipeAreaFluidResolution ResolvePipeAreaCandidateFluidItemIds(Vector2Int p,
        PipeAreaBlockCandidate candidate, HashSet<int> ids)
    { ids.Add(((InputOutputModule)candidate.snapshot.mapObject).Fluid); return PipeAreaFluidResolution.Resolved; }
    private bool CanProposedPumpConnectionsMatchExisting(Vector2Int anchor, MapObject source, int turns,
        MapObject ignored, Pump pump, HashSet<int> ids = null) => ids == null || ids.Contains(NetworkFluid);
    private static int NormalizeInstallPreviewQuarterTurns(MapObject source, int turns) => turns & 3;
    private static int GetPlacementRotationCandidateCount(MapObject source) => 4;
    private static bool CanUseSimpleRectGridInstallGridCheck(MapObject source) => true;
    private static bool TryGetInputOutputModule(MapObject source, out InputOutputModule module)
    { module = source as InputOutputModule; return module != null; }
    private static bool TryGetRectGridFootprintSettings(MapObject source, out int width, out int height, out Vector2Int anchor)
    { width = 4; height = 1; anchor = new(1, 0); return true; }
    private TerrainGenerator ResolveInstallPreviewTerrain() => terrain;
    private static bool TryResolveTrainStationFacingQuarterTurns(Vector2Int p, MapObject source,
        MapObject ignored, int fallback, out int turns) { turns = fallback; return false; }
    private static Vector2Int RotateFootprintOffset(Vector2Int p, int turns) => InputOutputModule.Rotate(p, turns);
    private bool CanPlaceSimpleRectGridAtAnchorFast(TerrainGenerator terrain, Vector2Int anchor, MapObject source,
        int turns, MapObject ignored, IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements,
        int width, int height, Vector2Int cell)
    {
        if (ExistingArea == null) return true;
        var proposed = new Pump { Anchor = anchor, Turns = turns };
        var type = ExistingArea.RectGridPlacements[0].blockType;
        if (!proposed.TryGetRectGridBlockTypeAtCoordinate(proposed, anchor, turns, ExistingArea.Anchor, out var own))
            return true;
        if (own == InputOutputModule.RectGridBlockType.PipeInput || type == InputOutputModule.RectGridBlockType.PipeInput)
            return true;
        return Overlap(proposed, ExistingArea.Anchor, ExistingArea, type);
    }
}
