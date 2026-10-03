using System;
using System.Collections.Generic;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Main()
    {
        var cell = new Vector2Int(3, 4);
        var block = new Block { Coordinate = cell };
        var neighbor = new Block { Coordinate = new Vector2Int(4, 4) };
        var terrain = new TerrainGenerator();
        terrain.loadedBlocks[cell] = block;
        terrain.loadedBlocks[neighbor.Coordinate] = neighbor;
        Check(block.SupportsFloorObjectDrops && block.CanAddFloorObjects(10, 1), "empty ground accepts items");
        Check(terrain.Resolve(cell) == block, "empty ground remains the preferred drop cell");

        var prototype = new InstallationObject();
        prototype.gameObject.activeInHierarchy = false;
        block.mapObject = prototype;
        block.runtimePipeRecord = new PipeRuntimeRecord { Coordinate = cell };
        Check(!block.SupportsFloorObjectDrops && !block.CanAddFloorObjects(1, 1),
            "installed pipe with inactive prototype rejects floor items");
        Check(terrain.Resolve(cell) == null, "pipe occupancy does not redirect player drops to a neighbor");
        Check(!block.TryAddFloorObject(1, out var rejected) && rejected == null
            && block.floorStacks[0].Count == 0 && block.PoolGets == 0,
            "actual pipe-cell insertion rejects the item before requesting a pooled object");

        block.runtimePipeRecord = null;
        PipeWorld.Current = new PipeWorld();
        // Underground endpoints can share a prototype but occupy distinct cells.
        PipeWorld.Current.Records[cell] = new PipeRuntimeRecord { Coordinate = cell };
        PipeWorld.Current.Records[neighbor.Coordinate] = new PipeRuntimeRecord { Coordinate = neighbor.Coordinate };
        block.mapObject = null;
        Check(!block.CanAddFloorObjects(1, 1) && !neighbor.CanAddFloorObjects(1, 1),
            "world lookup blocks both pipe endpoints without a cached map object");
        Check(terrain.Resolve(cell) == null, "world-only pipe also rejects the preferred drop cell");
        PipeWorld.Current.Records.Remove(cell);
        Check(block.CanAddFloorObjects(1, 1) && terrain.Resolve(cell) == block,
            "removed pipe releases its cell while another endpoint remains occupied");
        Check(block.TryAddFloorObject(1, out var dropped) && dropped.ItemId == 1
            && block.floorStacks[0].Count == 1 && block.PoolGets == 1,
            "actual insertion succeeds after pipe removal");
        block.floorStacks[0].Clear();
        PipeWorld.Current = null;
        block.runtimePipeRecord = new PipeRuntimeRecord { Coordinate = neighbor.Coordinate };
        Check(block.CanAddFloorObjects(1, 1), "record for another cell does not block this cell");
        block.runtimePipeRecord = null;

        ConveyorWorld.Current = new ConveyorWorld();
        ConveyorWorld.Current.Records[cell] = new ConveyorRuntimeRecord { Coordinate = cell };
        Check(!block.SupportsFloorObjectDrops && !block.CanAddFloorObjects(1, 1),
            "data-only conveyor uses the same floor exclusion; belt slots are a separate path");
        ConveyorWorld.Current = null;

        block.mapObject = new InstallationObject();
        Check(!block.CanAddFloorObjects(1, 1), "active scene installation remains blocked");
        block.mapObject = prototype;
        Check(block.CanAddFloorObjects(1, 1), "inactive scene object without an installed record does not occupy ground");
        var tree = new ProjectF.MapObjects.TreeInstance();
        block.mapObject = tree;
        Check(!block.CanAddFloorObjects(1, 1) && block.CanAddFloorObjects(1, 1, tree),
            "growing tree blocks player drops and preserves its own harvest exemption");
        InputOutputModuleOutputAreaController.Blocked = true;
        Check(!block.CanAddFloorObjects(1, 1, tree), "harvest exemption does not bypass output-area occupancy");
        InputOutputModuleOutputAreaController.Blocked = false;
        block.mapObject = null;
        block.floorStacks[0].Add(new PortableObject { ItemId = 2 });
        Check(terrain.Resolve(cell) == neighbor, "ordinary incompatible ground stack still redirects to empty ground");
        Check(!block.CanAddFloorObjects(1, 1) && block.CanAddFloorObjects(9, 2), "item compatibility and stack capacity remain enforced");
        Console.WriteLine($"PASS: {checks} production floor-drop checks; Unity/world boundaries are managed doubles. No engine launched.");
    }
}

public partial class Block
{
    public enum BlockType { Ground, Water }
    public BlockType Type;
    private BlockType type => Type;
    public Vector2Int Coordinate;
    private Vector2Int coordinate => Coordinate;
    public MapObject mapObject;
    public PipeRuntimeRecord runtimePipeRecord;
    public ConveyorRuntimeRecord runtimeConveyorRecord, runtimeConveyorRecordOverride;
    public List<List<PortableObject>> floorStacks = new() { new() };
    private readonly Pool floorObjectPool = new();
    private readonly object floorObjectPrefab = new();
    public int PoolGets => floorObjectPool.Gets;
    private (int Count, int ItemId) deferredFloorOutput = (0, -1);
    private void EnsureFloorObjectsInitialized(bool create = true) { }
    private Transform ResolveFloorObjectDropAnchor() => new Transform();
    private int ResolveFloorStackCapacity(int id) => 10;
    private bool IsFarmlandFertilizerItem(int id) => false;
    private bool TryAbsorbFarmlandFertilizer(int id) => false;
    private bool ResolveFloorObjectPool() => true;
    private void ConfigureFloorObjectTransform(PortableObject item, Transform anchor, int index) { }
    private bool TryInitializePooledPortableObject(PortableObject item, int id) { item.ItemId = id; return true; }
    private void NotifyRuntimeItemStackChanged() { }
    private static bool IsStackCompatible(List<PortableObject> stack, int id) => stack.Count == 0 || stack[0].ItemId == id;
    public bool HasFloorObjectItem(int id) => floorStacks[0].Count > 0 && floorStacks[0][0].ItemId == id;
}
public partial class TerrainGenerator
{
    public Dictionary<Vector2Int, Block> loadedBlocks = new();
    public Block Resolve(Vector2Int cell) => FindPreferredDropBlock(new Vector3(cell.x, 0, cell.y), 1, 1);
    private Vector2Int GetWorldBlockCoordinate(Vector3 position) => new((int)position.x, (int)position.z);
    private bool TryGetLoadedBlock(Vector2Int cell, out Block block) => loadedBlocks.TryGetValue(cell, out block);
    private bool CanAbsorbDroppedFarmlandFertilizer(Vector2Int cell, int id) => false;
}
public class MapObject { }
public class InstallationObject : MapObject { public GameObject gameObject = new(); }
public class ResourceInstance : MapObject { }
namespace ProjectF.MapObjects
{
    public class TreeInstance : ResourceInstance
    {
        public bool IsRuntimeActive = true, CanGrowAnotherLevel = true;
        public int ResourceCount = 1;
    }
}
public class PortableObject { public int ItemId; public void SetBatchedRendering(bool value) { } }
public class Pool
{
    public int Gets;
    public PortableObject Get(object prefab) { Gets++; return new PortableObject(); }
}
public class PipeRuntimeRecord
{
    public Vector2Int Coordinate;
    public bool Covers(Vector2Int cell) => Coordinate == cell;
}
public class ConveyorRuntimeRecord : PipeRuntimeRecord { }
public class PipeWorld
{
    public static PipeWorld Current;
    public Dictionary<Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int cell, out PipeRuntimeRecord record) => Records.TryGetValue(cell, out record);
}
public class ConveyorWorld
{
    public static ConveyorWorld Current;
    public Dictionary<Vector2Int, ConveyorRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int cell, out ConveyorRuntimeRecord record) => Records.TryGetValue(cell, out record);
}
public class InputOutputModuleEnergyAreaController { public static bool CoordinateIsEnergyArea(Vector2Int cell) => false; }
public class InputOutputModuleItemAreaController { public static bool CoordinateIsItemArea(Vector2Int cell) => false; }
public class InputOutputModuleOutputAreaController
{
    public static bool Blocked;
    public static bool CoordinateIsOutputArea(Vector2Int cell) => Blocked;
}
namespace UnityEngine
{
    public class Transform { }
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public record struct Vector2Int(int x, int y)
    {
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Abs(int value) => Math.Abs(value);
    }
}
