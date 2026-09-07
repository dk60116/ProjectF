using System;
using System.Collections.Generic;

public readonly record struct Vector2Int(int x, int y);
public class PortableObject { }
public class Block
{
    public int Count;
    public int Capacity = 10;
    public int GetInputAreaCenterItemCount() => Count;
    public bool CanAddInputAreaCenterObjects(int count, int itemId) => Count + count <= Capacity;
    public bool TryAddInputAreaCenterObject(int itemId, out PortableObject item, bool instant)
    {
        item = null;
        if (!CanAddInputAreaCenterObjects(1, itemId)) return false;
        Count++;
        item = new PortableObject();
        return true;
    }
}
public class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
}
public class InputOutputModule
{
    public static readonly Dictionary<Vector2Int, InputOutputModule> Modules = new();
    public Block Input;
    public static bool TryGetModuleAtRuntimeGridCoordinate(Vector2Int coordinate, out InputOutputModule module)
        => Modules.TryGetValue(coordinate, out module);
    public bool TryGetRuntimeInputBlock(TerrainGenerator terrain, int itemId, out Block block)
    {
        block = Input;
        return block != null;
    }
}
public static class InputOutputModuleItemAreaController
{
    public static readonly HashSet<Vector2Int> Areas = new();
    public static bool CoordinateIsItemArea(Vector2Int coordinate) => Areas.Contains(coordinate);
}
public partial class BoxObject
{
    public bool ExcludeFromTerrainPersistence;
    public bool GroundDrop;
    public bool Accepts = true;
    public Vector2Int Anchor;
    public TerrainGenerator Terrain;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates;
    private TerrainGenerator ResolveTerrainGenerator() => Terrain;
    private bool TryGetGroundDropCoordinate(out Vector2Int coordinate) { coordinate = Anchor; return GroundDrop; }
    private bool TryGetSingleResolvedItemId(out int itemId) { itemId = -1; return false; }
    private bool IsItemAreaCoordinate(Vector2Int coordinate) => InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate);
    private bool TryGetAnchorBlock(out Block block) => Terrain.TryGetLoadedBlock(Anchor, out block);
    private bool AcceptsItem(int itemId) => Accepts;
    public Block Content() => TryGetContentBlock(out Block block) ? block : null;
}
public static class Checks
{
    private static int count;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        count++;
    }
    public static void Main()
    {
        var sourceCoordinate = new Vector2Int(0, 0);
        var targetCoordinate = new Vector2Int(0, 2);
        var terrain = new TerrainGenerator();
        var source = new Block { Count = 4 };
        var target = new Block();
        terrain.Blocks[sourceCoordinate] = source;
        terrain.Blocks[targetCoordinate] = target;
        // One arm outputs here while another arm picks up here from a different module input.
        InputOutputModuleItemAreaController.Areas.Add(targetCoordinate);
        InputOutputModule.Modules[targetCoordinate] = new InputOutputModule { Input = source };
        var box = new BoxObject { Terrain = terrain, Anchor = targetCoordinate, RuntimeOccupiedCoordinates = new[] { targetCoordinate } };
        Require(box.Content() == target, "overlapping robot IO must not redirect the target box to the source box");
        Require(box.TryPutOneContainedObjectInstant(1, out _) && target.Count == 1 && source.Count == 4,
            "deposit must increase only the target box count");
        source.Count = 0;
        target.Count = 0;
        Require(box.Content() == target, "empty module input must not redirect an empty target box");
        target.Count = target.Capacity;
        Require(!box.CanPutContainedObjects(1, 1) && !box.TryPutOneContainedObjectInstant(1, out _) && source.Count == 0,
            "a full target must not spill into another box with free space");
        target.Count = 0;
        box.Accepts = false;
        Require(!box.TryPutOneContainedObjectInstant(1, out _), "box filters must still reject items");
        box.Accepts = true;
        box.GroundDrop = true;
        Require(box.Content() == target, "standalone ground-drop boxes must keep anchor storage");
        box.GroundDrop = false;
        box.RuntimeOccupiedCoordinates = Array.Empty<Vector2Int>();
        Require(box.Content() == target, "anchor fallback must still resolve the local block");
        box.ExcludeFromTerrainPersistence = true;
        Require(box.Content() == null, "preview boxes must not resolve world storage");
        Console.WriteLine($"PASS: {count} box storage isolation checks.");
    }
}
