using System;
using System.Collections.Generic;

static class Checks
{
    static int count;
    static void Check(bool ok, string message)
    {
        count++;
        if (!ok) throw new Exception(message);
    }
    static void Main()
    {
        foreach (bool steam in new[] { true, false })
        foreach (var anchor in new[] { new Vector2Int(17, 23), new Vector2Int(-13, -29) })
        {
            var probe = new Probe(anchor, steam);
            for (int turn = 1; turn <= 4; turn++)
            {
                probe.Rotate();
                Check(probe.CurrentBlock != null,
                    $"Failed pipe-rotation probe erased the valid anchor block: body={probe.Position}, IO anchor={probe.Marker}");
                Check(probe.Marker == anchor && probe.Position == new Vector3(anchor.x, 0, anchor.y),
                    "Ordinary rotation must keep body and IO anchor together, not send body to world origin");
                Check(probe.Turns == turn % 4, "Ordinary rotation advances by 90 degrees");
            }
        }

        for (int turn = 0; turn < 4; turn++)
        {
            var anchor = new Vector2Int(12, 19);
            var probe = new Probe(anchor, true, turn);
            probe.Pipes.Add(anchor + Probe.RotateOffset(new Vector2Int(2, 0), turn));
            var expected = anchor + Probe.RotateOffset(new Vector2Int(1, 0), turn);
            probe.Rotate();
            Check(probe.Turns == (turn + 2) % 4, "Valid straight-pipe flip rotates 180 degrees");
            Check(probe.CurrentBlock != null && probe.Marker == expected
                && probe.Position == new Vector3(expected.x, 0, expected.y),
                "Successful pipe flip must apply its new body/IO anchor");

            var missing = new Probe(anchor, true, turn);
            missing.Pipes.Add(anchor + Probe.RotateOffset(new Vector2Int(2, 0), turn));
            missing.Terrain.OnlyOriginalBlock = true;
            missing.Rotate();
            Check(missing.CurrentBlock != null && missing.Marker == anchor
                && missing.Position == new Vector3(anchor.x, 0, anchor.y),
                "Rejected pipe flip must preserve the original block for normal rotation");
            Check(missing.Turns == (turn + 1) % 4, "Rejected flip retains ordinary rotation fallback");
        }
        Console.WriteLine($"PASS: {count} production rotation branch/pipe resolver/position checks. Unity engine not launched.");
    }
}

// Terrain lookup, Unity transforms and unrelated placement policies are managed doubles.
// Run.ps1 extracts the caller, pipe-rotation resolver, footprint comparison and position
// resolution from production; BeforeFix exercises the same cases against HEAD.
public partial class Probe
{
    public Block CurrentBlock;
    public Vector2Int Marker;
    public TerrainGenerator Terrain;
    public HashSet<Vector2Int> Pipes = new();
    MapObject activeInstallPreview;
    int installPreviewQuarterTurns;
    readonly Dictionary<MapObject, int> installPreviewFenceDoorStandaloneQuarterTurnsByPreview = new();
    public int Turns => installPreviewQuarterTurns;
    public Vector3 Position => activeInstallPreview.transform.position;
    public Probe(Vector2Int anchor, bool steam, int turns = 0)
    {
        Marker = anchor;
        Terrain = new TerrainGenerator(anchor);
        Terrain.TryGetLoadedBlock(anchor, out CurrentBlock);
        activeInstallPreview = new InputOutputModule { Steam = steam };
        activeInstallPreview.transform.position = CurrentBlock.WorldPosition;
        installPreviewQuarterTurns = turns;
    }
    int GetPreviewQuarterTurns(MapObject _) => installPreviewQuarterTurns;
    bool TryResolveInstallPreviewFootprintSourceForPlacement(MapObject obj, out MapObject source) { source = obj; return obj != null; }
    bool IsSteamGeneratorSource(MapObject obj) => obj.Steam;
    bool TryGetInputOutputModule(MapObject obj, out InputOutputModule module) { module = obj as InputOutputModule; return module != null; }
    TerrainGenerator ResolveInstallPreviewTerrain() => Terrain;
    bool TryGetRectGridFootprintSettings(MapObject obj, out int width, out int height, out Vector2Int center)
    { width = 4; height = 1; center = new Vector2Int(1, 0); return true; }
    int NormalizePlacementQuarterTurnsForObject(MapObject obj, int turns) => (turns % 4 + 4) % 4;
    int NormalizeInstallPreviewQuarterTurns(MapObject obj, int turns) => NormalizePlacementQuarterTurnsForObject(obj, turns);
    int GetNextInstallPreviewQuarterTurns(MapObject obj, int turns) => (turns + 1) % 4;
    bool ShouldRememberFenceDoorStandaloneQuarterTurns(MapObject obj, bool hasAnchor, Vector2Int anchor) => false;
    MapObject ResolveInstallPreviewPlacementSource(MapObject obj) => obj;
    bool IsTrainSource(MapObject _) => false;
    bool TryGetRectGridCoordinate(Vector2Int anchor, MapObject source, int turns, InputOutputModule.RectGridBlockPlacement cell, out Vector2Int coordinate)
    { coordinate = anchor + RotateFootprintOffset(new Vector2Int(cell.x - 1, cell.y), turns); return true; }
    bool TryGetPipePlacementAtCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion rotation)
    { pipe = Pipes.Contains(coordinate) ? new Pipe() : null; rotation = default; return pipe != null; }
    bool TryGetStraightPipeAxis(Pipe pipe, Quaternion rotation, out Vector2Int axis) { axis = new Vector2Int(1, 0); return true; }
    static Vector2Int RotateFootprintOffset(Vector2Int offset, int turns) => RotateOffset(offset, turns);
    public static Vector2Int RotateOffset(Vector2Int value, int turns)
    { for (int i = 0; i < turns; i++) value = new Vector2Int(value.y, -value.x); return value; }
    Vector2 GetPlacementWorldPositionOffset(MapObject source, int turns) => default; // SteamGenerator uses its anchor cell.
    Vector3 ResolveRailAlignedPlacementWorldPosition(MapObject source, Vector2Int anchor, int turns, Vector3 position, Vector3? reference) => position;
}
public class MapObject { public bool Steam; public Transform transform = new(); }
public class Transform { public Vector3 position; }
public class Pipe { }
public struct Quaternion { }
public struct Vector2 { public float x, y; }
public record struct Vector3(float x, float y, float z) { public static Vector3 zero => default; }
public readonly record struct Vector2Int(int x, int y)
{
    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
}
public class Block
{
    public Vector2Int Coordinate;
    public Vector3 WorldPosition => new(Coordinate.x, 0, Coordinate.y);
}
public class TerrainGenerator
{
    readonly Vector2Int original;
    public bool OnlyOriginalBlock;
    public TerrainGenerator(Vector2Int anchor) { original = anchor; }
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
    {
        block = !OnlyOriginalBlock || coordinate == original ? new Block { Coordinate = coordinate } : null;
        return block != null;
    }
}
public class InputOutputModule : MapObject
{
    public enum RectGridBlockType { Object, PipeInput, PipePass }
    public struct RectGridBlockPlacement { public int x, y; public RectGridBlockType blockType; }
    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements = new[] {
        new RectGridBlockPlacement { x = 0, blockType = RectGridBlockType.PipeInput },
        new RectGridBlockPlacement { x = 1, blockType = RectGridBlockType.Object },
        new RectGridBlockPlacement { x = 2, blockType = RectGridBlockType.Object },
        new RectGridBlockPlacement { x = 3, blockType = RectGridBlockType.PipePass }
    };
    public static bool AllowsPipeAreaInteraction(RectGridBlockType type) => type != RectGridBlockType.Object;
}
