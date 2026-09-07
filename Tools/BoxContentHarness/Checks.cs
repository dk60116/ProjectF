using System;
using System.Collections.Generic;

public readonly record struct Vector2Int(int x, int y);
public class PortableObject { }
public partial class Block
{
    public object MapObject;
    private readonly List<PortableObject> inputAreaCenterStack = new();
    public Vector2Int Coordinate;
    private Vector2Int coordinate => Coordinate;
    public int Count
    {
        get => inputAreaCenterStack.Count;
        set
        {
            inputAreaCenterStack.Clear();
            for (int i = 0; i < value; i++) inputAreaCenterStack.Add(new PortableObject());
        }
    }
    public int Capacity = 10;
    public int GetInputAreaCenterItemCount() => Count;
    public int GetInputAreaCenterItemCount(int itemId) => Count;
    public int GetInputAreaCenterItemId() => Count > 0 ? 1 : -1;
    public int GetInputAreaCenterCapacity(int itemId) => Capacity;
    public int GetInputAreaCenterCapacity() => Capacity;
    public bool TryGetInstalledItemAreaCapacity(out int capacity) { capacity = Capacity; return true; }
    public bool TryConsumeOneInputAreaCenterObject(int itemId, out int consumed)
    {
        consumed = -1;
        if (Count <= 0 || (itemId >= 0 && itemId != 1)) return false;
        Count--; consumed = 1; return true;
    }
    private void CleanupPortableStack(List<PortableObject> stack) { }
    private bool IsStackCompatible(List<PortableObject> stack, int itemId) => stack.Count == 0 || itemId == 1;
    private int ResolveInputAreaCenterCapacity(int itemId) => Capacity;
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
    public static TerrainGenerator Active;
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
}
public partial class InputOutputModule
{
    public static bool HasOverlapRestriction;
    public static readonly HashSet<int> OverlapAllowedItems = new();
    private static bool TryGetRuntimeIoOverlapAllowedItemIds(Vector2Int coordinate, ISet<int> allowedItems)
    {
        foreach (int id in OverlapAllowedItems) allowedItems.Add(id);
        return HasOverlapRestriction;
    }
    public static readonly Dictionary<Vector2Int, InputOutputModule> Modules = new();
    public Block Input;
    public static bool TryGetModuleAtRuntimeGridCoordinate(Vector2Int coordinate, out InputOutputModule module)
        => Modules.TryGetValue(coordinate, out module);
    public static void WakeRuntimeModulesAtCoordinate(Vector2Int coordinate) { }
    public bool TryGetRuntimeInputBlock(TerrainGenerator terrain, int itemId, out Block block)
    {
        block = Input;
        return block != null;
    }
}
public static class RobotArm { public static void WakeAroundCoordinate(Vector2Int coordinate) { } }
public static class InputOutputModuleItemAreaController
{
    public static readonly HashSet<Vector2Int> Areas = new();
    public static bool CoordinateIsItemArea(Vector2Int coordinate) => Areas.Contains(coordinate);
}
public partial class BoxObject
{
    private bool isOpen = true;
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
    public bool AcceptsItem(int itemId) => itemId >= 0 && Accepts;
    public bool TryGetObjectInfoItem(out int itemId, out int itemCount, out int capacity)
    {
        Block block = Content();
        itemId = block?.GetInputAreaCenterItemId() ?? -1;
        itemCount = block?.Count ?? 0;
        capacity = block?.Capacity ?? 0;
        return block != null;
    }
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
        TerrainGenerator.Active = terrain;
        var source = new Block { Count = 4 };
        var target = new Block();
        terrain.Blocks[sourceCoordinate] = source;
        terrain.Blocks[targetCoordinate] = target;
        // One arm outputs here while another arm picks up here from a different module input.
        InputOutputModuleItemAreaController.Areas.Add(targetCoordinate);
        InputOutputModule.Modules[targetCoordinate] = new InputOutputModule { Input = source };
        var box = new BoxObject { Terrain = terrain, Anchor = targetCoordinate, RuntimeOccupiedCoordinates = new[] { targetCoordinate } };
        target.Coordinate = targetCoordinate;
        target.MapObject = box;
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
        target.Count = 4;
        box.SetMinimumRetainedItemCount(3);
        Require(box.MinimumRetainedItemCount == 3 && box.GetExtractableContainedItemCount() == 1,
            "reserve must expose only inventory above the minimum");
        Require(box.CanTakeContainedObject() && box.TryTakeOneContainedObject(null, out int taken) && taken == 1 && target.Count == 3,
            "the last extractable item may be removed");
        Require(!box.CanTakeContainedObject() && !box.TryTakeOneContainedObject(null, out _) && target.Count == 3,
            "robot extraction must stop at the configured minimum");
        box.SetMinimumRetainedItemCount(99);
        Require(box.MinimumRetainedItemCount == target.Capacity,
            "reserve setting must clamp to box capacity");
        box.SetMinimumRetainedItemCount(0);
        box.GroundDrop = true;
        Require(box.Content() == target, "standalone ground-drop boxes must keep anchor storage");
        box.GroundDrop = false;
        box.RuntimeOccupiedCoordinates = Array.Empty<Vector2Int>();
        Require(box.Content() == target, "anchor fallback must still resolve the local block");
        box.ExcludeFromTerrainPersistence = true;
        Require(box.Content() == null, "preview boxes must not resolve world storage");
        box.ExcludeFromTerrainPersistence = false;
        target.Count = 0;
        InputOutputModule.HasOverlapRestriction = true;
        InputOutputModule.OverlapAllowedItems.Add(2); // Another machine consumes item 2, while this output produces item 1.
        Require(target.CanAddInputAreaCenterObjects(1, 1),
            "empty output box must accept item 1 despite an overlapping machine accepting only item 2");
        Require(box.CanPutContainedObjects(1, 1) && box.TryPutOneContainedObjectInstant(1, out _) && target.Count == 1,
            "output-space preview and deposit must agree for overlapping box storage");
        box.Accepts = false;
        Require(!target.CanAddInputAreaCenterObjects(1, 1) && !box.TryPutOneContainedObjectInstant(1, out _),
            "the box's own filter must still reject output");
        box.Accepts = true;
        target.Count = target.Capacity;
        Require(!target.CanAddInputAreaCenterObjects(1, 1), "a full box must remain full");
        target.Count = 1;
        Require(!target.CanAddInputAreaCenterObjects(1, 2), "different item types must not mix in one box stack");
        target.MapObject = null;
        target.Count = 0;
        Require(!target.CanAddInputAreaCenterObjects(1, 1) && target.CanAddInputAreaCenterObjects(1, 2),
            "shared IO areas without a box must preserve their item restriction");
        Require(!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(targetCoordinate, -1), "invalid item IDs must remain rejected");
        Console.WriteLine($"PASS: {count} box storage isolation checks.");
    }
}
