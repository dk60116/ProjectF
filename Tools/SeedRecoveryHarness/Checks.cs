using System;
using System.Collections.Generic;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
    static ItemDefinition Seed(int id) => new ItemDefinition { id = id, isSeed = true, Plantable = true };
    static ResourceDropEntry Drop(ItemDefinition item, int count, float chance = 1f, float min = 0f) =>
        new ResourceDropEntry { Items = new[] { new ResourceDropItem { ItemDefinition = item, Amount = count, DropChance = chance } }, Minimum = min };
    static ProjectF.MapObjects.TreeInstance Tree(params ResourceDropEntry[] drops) =>
        new ProjectF.MapObjects.TreeInstance { definition = new ResourceDefinition { DropItems = drops }, OwningBlock = new Block { Coordinate = new Vector2Int(5,5) } };
    static void Main()
    {
        var seed = Seed(10);
        InputOutputModule.Definitions[10] = seed;
        var other = Seed(11);
        InputOutputModule.Definitions[11] = other;
        var log = new ItemDefinition { id = 1 };
        var rewards = new List<KeyValuePair<int,int>>();
        var tree = Tree(Drop(log, 4), Drop(seed, 1), Drop(seed, 2), Drop(other, 1),
            Drop(Seed(12), 5, 0f), Drop(Seed(13), 5, 1f, 11f));
        tree.CollectMachineSeedDrops(rewards);
        Check(rewards.Count == 2 && rewards[0].Key == 10 && rewards[0].Value == 3 && rewards[1].Key == 11,
            "multiple seed types preserve IDs and duplicate entries aggregate once");
        tree.CollectMachineSeedDrops(rewards);
        Check(rewards.Count == 2 && rewards[0].Value == 3, "repeated peek is deterministic and resets scratch rewards");
        Tree(Drop(log,4)).CollectMachineSeedDrops(rewards);
        Check(rewards.Count == 0, "tree without seed drops generates no seed");

        var planter = new SeedPlanter();
        var placement = new InstallationPlacementController();
        var outputController = new InputOutputModuleOutputAreaController();
        planter.Controller = outputController;
        InputOutputModuleOutputAreaController.Blocked = true;
        placement.Configure(planter);
        Check(!InputOutputModuleOutputAreaController.Blocked && outputController.ClearCalls == 1,
            "planting soil unregisters stale item-output restriction");
        var producer = new InputOutputModule { Controller = outputController };
        placement.Configure(producer);
        Check(InputOutputModuleOutputAreaController.Blocked, "ordinary machine output retains floor-stack restriction");

        var harvested = Tree(Drop(seed,3));
        var harvestBlock = harvested.OwningBlock;
        harvestBlock.mapObject = harvested;
        harvested.CanGrowAnotherLevel = true;
        Check(!harvestBlock.CanAddFloorObjects(4,1,harvested), "harvest does not bypass other machine output restrictions");
        placement.Configure(planter);
        Check(!harvestBlock.CanAddFloorObjects(4,1) && harvestBlock.CanAddFloorObjects(4,1,harvested),
            "growing tree blocks ordinary drops but permits its own harvested logs");
        Check(!harvestBlock.CanAddFloorObjects(4,1,Tree()), "another resource cannot bypass growing-tree occupancy");
        var facilityBlock = new Block { mapObject = new MapObject { DataOnly = true } };
        Check(!facilityBlock.CanAddFloorObjects(1,1), "active data-only facility body blocks floor drops");
        facilityBlock.mapObject.IsTargetActive = false;
        Check(facilityBlock.CanAddFloorObjects(1,1), "expired data identity cannot block floor drops");
        harvestBlock.Capacity = 3;
        harvested.TryHarvestForMachine(out int harvestedId, out int harvestedCount);
        for (int i = 0; i < harvestedCount; i++)
            harvestBlock.TryAddHarvestedFloorObjectAnimated(harvestedId, default, out _, harvested);
        Check(!harvested.CanHarvest && harvestBlock.floorStacks[0].Count == 4,
            "logging always removes the tree and forces every log onto its original cell");

        var terrain = new TerrainGenerator();
        terrain.farmlandCoordinates.Add(harvestBlock.Coordinate);
        Check(!terrain.CanPlantSeed(harvestBlock,seed), "logs block replanting even immediately after tree disappears");
        harvestBlock.floorStacks[0].RemoveAt(0);
        Check(!terrain.CanPlantSeed(harvestBlock,seed), "partial log pickup does not start planting");
        harvestBlock.floorStacks[0].Clear();
        Check(terrain.CanPlantSeed(harvestBlock,seed), "planting starts after last log is removed");
        harvestBlock.Resource = Tree();
        Check(!terrain.CanPlantSeed(harvestBlock,seed), "existing active tree blocks planting before harvest");
        var store = new BlockStateStore();
        store.HasSavedLogs = true;
        Check(!store.IsSavedCoordinateEmptyGround(harvestBlock.Coordinate), "unloaded cell with saved logs cannot be replanted");
        store.HasSavedLogs = false;
        store.savedStates[harvestBlock.Coordinate] = new object();
        Check(!store.IsSavedCoordinateEmptyGround(harvestBlock.Coordinate), "unloaded existing tree blocks planting");
        store.savedStates.Clear();
        Check(store.IsSavedCoordinateEmptyGround(harvestBlock.Coordinate), "unloaded cell is plantable after tree and logs are gone");
        Console.WriteLine($"PASS: {checks} production seed-roll/ground/placement checks. Managed scene and storage doubles; no engine launched.");
    }
}
public class ItemDefinition
{
    public int id; public bool isSeed, Plantable;
    public static bool IsPlantableSeedDefinition(ItemDefinition item) => item != null && item.isSeed && item.Plantable;
}
public class ResourceDropEntry
{
    public IReadOnlyList<ResourceDropItem> Items; public float Minimum;
    public bool Matches(float growth) => growth >= Minimum;
}
public class ResourceDropItem { public ItemDefinition ItemDefinition; public int Amount; public float DropChance; }
public class ResourceDefinition { public IReadOnlyList<ResourceDropEntry> DropItems; }
public partial class ResourceInstance : MapObject
{
    public ResourceDefinition definition;
    public ResourceDefinition Definition => definition;
    public int initialResourceCount = 1, ResourceCount = 1;
    public bool CanHarvest = true, HarvestSucceeds = true, IsRuntimeActive = true;
    public Block OwningBlock;
    public Vector3 FocusPoint;
    private bool HasConfiguredDropItems() => definition != null && definition.DropItems != null && definition.DropItems.Count > 0;
    private int BuildHarvestDropSeed(int ordinal) => 123 + ordinal;
    private float ResolveDropGrowth() => 10f;
    public bool TryHarvestForMachine(out int id, out int count)
    { id = 1; count = 4; if (!HarvestSucceeds) return false; CanHarvest = false;
      if (OwningBlock != null) { OwningBlock.mapObject = null; OwningBlock.Resource = null; }
      OwningBlock = null; return true; }
    public bool TryPeekMachineHarvestOutput(out int id, out int count) { id = 1; count = 4; return CanHarvest; }
}
namespace ProjectF.MapObjects
{
    public partial class TreeInstance : ResourceInstance
    {
        public bool CanGrowAnotherLevel;
        public bool TryGetMachineAppleDrop(out int id, out int count) { id = 2; count = 2; return true; }
    }
}
public partial class Block
{
    public Vector2Int Coordinate;
    private Vector2Int coordinate => Coordinate;
    public enum BlockType { Ground, Water }
    public BlockType Type;
    public ResourceInstance Resource;
    public MapObject mapObject;
    public MapObject MapObject => mapObject;
    public int Capacity = 20;
    public List<List<PortableObject>> floorStacks = new List<List<PortableObject>> { new List<PortableObject>() };
    public bool HasDroppedFloorObjects => floorStacks[0].Count > 0;
    private void EnsureFloorObjectsInitialized(bool materialize) { }
    private DeferredOutput deferredFloorOutput = new();
    private class DeferredOutput { public int Count, ItemId; }
    private bool TryGetRuntimePipeRecord(out object record) { record = null; return false; }
    private bool TryGetRuntimeConveyorRecord(out object record) { record = null; return false; }
    private Transform ResolveFloorObjectDropAnchor() => new Transform();
    private int ResolveFloorStackCapacity(int id) => Capacity;
    private static bool IsStackCompatible(List<PortableObject> stack, int id) => stack.Count == 0 || stack[0].ItemId == id;
    private bool IsFarmlandFertilizerItem(int id) => false;
    public bool TryAddFloorObjectAnimated(int id, Vector3 start, float delay, out object visual,
        Action onComplete = null, Func<Vector3> startProvider = null, ResourceInstance harvestedResource = null)
    { visual = null; if (!CanAddFloorObjects(1,id,harvestedResource)) return false;
      floorStacks[0].Add(new PortableObject { ItemId = id }); return true; }
    public bool TryAddHarvestedFloorObjectAnimated(int id, Vector3 start, out object visual, ResourceInstance harvestedResource)
    { visual = null; floorStacks[0].Add(new PortableObject { ItemId = id }); return true; }

}
public partial class BlockStateStore
{
    public Dictionary<Vector2Int,object> savedStates = new Dictionary<Vector2Int,object>();
    public bool HasSavedLogs;
    private bool HasSavedDroppedFloorObjects(Vector2Int coordinate) => HasSavedLogs;
    private bool TryGetInstallationAnchorAtCoordinate(Vector2Int coordinate, out Vector2Int anchor) { anchor = default; return false; }
}
public class InputOutputModule : MapObject
{
    public static Dictionary<int,ItemDefinition> Definitions = new();
}
public class SeedPlanter : InputOutputModule { }
namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y);
    public struct Vector3 { public float x, y, z; }
    public class Transform { public Vector3 position; }
    public class GameObject { public bool activeInHierarchy = true; public T AddComponent<T>() where T : new() => new T(); }
    public static class Debug { public static void LogError(string message, object context) => throw new Exception(message); }
    public static class Mathf { public static int Max(int a,int b) => Math.Max(a,b); public static int RoundToInt(float value) => (int)Math.Round(value); }
}

public class PortableObject { public int ItemId; }
public class MapObject
{
    public bool DataOnly, IsTargetActive = true;
    public MapObject SceneObject => DataOnly ? null : this;
    public GameObject gameObject = new GameObject();
    public Transform transform = new Transform();
    public InputOutputModuleOutputAreaController Controller;
    public T GetComponent<T>() where T : class => Controller as T;
}
public class InstallationObject : MapObject { }
public class RobotArm : MapObject { }
public partial class TerrainGenerator { public HashSet<Vector2Int> farmlandCoordinates = new HashSet<Vector2Int>(); }
public class InputOutputModuleEnergyAreaController { public static bool CoordinateIsEnergyArea(Vector2Int coordinate) => false; }
public class InputOutputModuleItemAreaController { public static bool CoordinateIsItemArea(Vector2Int coordinate) => false; }
public class InputOutputModuleOutputAreaController
{
    public static bool Blocked;
    public int ClearCalls;
    public static bool CoordinateIsOutputArea(Vector2Int coordinate) => Blocked;
    public void Configure(List<Vector2Int> coordinates, bool blocksPlacement = true) { Blocked = coordinates != null && blocksPlacement; if (coordinates == null) ClearCalls++; }
}
public partial class InstallationPlacementController
{
    private object DirectOutputRectGridBlockTypes;
    public void Configure(MapObject installed) => ConfigureInstalledInputOutputOutputAreas(installed,default,0);
    private bool TryGetInputOutputModule(MapObject installed, out InputOutputModule module)
    { module = installed as InputOutputModule; return module != null; }
    private bool TryGetRectGridBlockCoordinates(Vector2Int anchor, MapObject installed, int turns, object types, out List<Vector2Int> coords)
    { coords = new List<Vector2Int> { new Vector2Int(5,5) }; return true; }
}
