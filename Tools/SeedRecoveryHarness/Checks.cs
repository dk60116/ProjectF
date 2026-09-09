using System;
using System.Collections.Generic;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
    static ItemDefinition Seed(int id) => new ItemDefinition { id = id, isSeed = true, Plantable = true };
    static ResourceDropEntry Drop(ItemDefinition item, int count, float chance = 1f, float min = 0f) =>
        new ResourceDropEntry { ItemDefinition = item, Amount = count, DropChance = chance, Minimum = min };
    static ProjectF.MapObjects.Tree Tree(params ResourceDropEntry[] drops) =>
        new ProjectF.MapObjects.Tree { definition = new ResourceDefinition { DropItems = drops }, OwningBlock = new Block { Coordinate = new Vector2Int(5,5) } };
    static SeedPlanter Planter(int capacity, bool saved = false)
    {
        var planter = new SeedPlanter { Output = new Vector2Int(5,5) };
        planter.Inputs[10] = new List<Vector2Int> { new Vector2Int(1,1) };
        planter.Slots[new Vector2Int(1,1)] = new Slot { Capacity = capacity, Saved = saved };
        return planter;
    }
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

        var planter = Planter(2);
        int received = planter.ReceiveHarvestedSeeds(new Vector2Int(5,5), 10, 3, default);
        Check(received == 2 && planter.Slots[new Vector2Int(1,1)].Count == 2, "loaded input capacity limits accepted count");
        Check(planter.Refreshes == 1 && planter.Wakes == 1, "successful recovery refreshes input state and wakes planter");
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,1,default) == 0, "full input reports zero without consuming reward");
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(6,5),10,1,default) == 0, "neighbor with different planting target cannot receive");
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),11,1,default) == 0, "unbound seed cannot enter input");
        planter = Planter(5, true);
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,3,default) == 3
            && planter.Slots[new Vector2Int(1,1)].Count == 3, "saved/unloaded input uses authoritative stored stack");
        planter = Planter(5);
        planter.Slots[new Vector2Int(1,1)].ItemId = 11;
        planter.Slots[new Vector2Int(1,1)].Count = 1;
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,3,default) == 0, "different seed already in stack is not overwritten");
        planter = Planter(5);
        planter.isActiveAndEnabled = false;
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,1,default) == 0, "inactive planter cannot receive");
        planter.isActiveAndEnabled = true;
        planter.Placed = false;
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,1,default) == 0, "unplaced planter cannot receive");
        planter.Placed = true;
        InputOutputModule.BlockInput = true;
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,1,default) == 0, "overlapping IO restriction respected");
        InputOutputModule.BlockInput = false;
        seed.Plantable = false;
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,1,default) == 0, "invalid seed stays outside planter");
        seed.Plantable = true;

        planter = Planter(1);
        var spare = Planter(1);
        InputOutputModule.Receivers.Clear();
        InputOutputModule.Receivers.Add(planter);
        InputOutputModule.Receivers.Add(spare);
        var logger = new LoggingMachine();
        var harvested = Tree(Drop(seed,3));
        var harvestBlock = harvested.OwningBlock;
        logger.Harvest(harvested);
        Check(logger.Drops[10] == 1 && planter.Slots[new Vector2Int(1,1)].Count == 1
            && spare.Slots[new Vector2Int(1,1)].Count == 1, "overflow reaches another matching planter, remainder drops once");
        Check(harvestBlock.floorStacks[0].Count == 4 && !logger.Drops.ContainsKey(1)
            && logger.Apples == 2 && logger.Advances == 1, "all logs stay on original tree block; apple and direction unchanged");
        InputOutputModule.Receivers.Clear();
        logger = new LoggingMachine();
        logger.Harvest(Tree(Drop(seed,2)));
        Check(logger.Drops[10] == 2, "no matching planter keeps all seeds in normal drop path");
        var failed = Tree(Drop(seed,2));
        failed.HarvestSucceeds = false;
        logger = new LoggingMachine();
        logger.Harvest(failed);
        Check(logger.Drops.Count == 0 && logger.Apples == 0, "failed harvest emits no rolled seed or other reward");
        logger.Harvest(Tree(Drop(log,4)));
        Check(!logger.Drops.ContainsKey(10), "previous harvest scratch does not leak seeds into next tree");
        logger.Harvest(null);
        Check(logger.Advances == 3, "null target retains existing direction advance");

        planter = Planter(20);
        InputOutputModule.Receivers.Add(planter);
        logger = new LoggingMachine();
        logger.Harvest(Tree(Drop(seed,3), Drop(seed,4)));
        Check(planter.Slots[new Vector2Int(1,1)].Count == 7 && !logger.Drops.ContainsKey(10),
            "every collected seed enters available input, including repeated reward entries");
        planter = Planter(2);
        var secondInput = new Vector2Int(2,1);
        planter.Inputs[10].Add(secondInput);
        planter.Slots[secondInput] = new Slot { Capacity = 5 };
        Check(planter.ReceiveHarvestedSeeds(new Vector2Int(5,5),10,7,default) == 7,
            "recovery fills every configured input coordinate before dropping overflow");

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

        harvested = Tree(Drop(seed,3));
        harvestBlock = harvested.OwningBlock;
        harvestBlock.mapObject = harvested;
        harvested.CanGrowAnotherLevel = true;
        Check(!harvestBlock.CanAddFloorObjects(4,1,harvested), "harvest does not bypass other machine output restrictions");
        placement.Configure(planter);
        Check(!harvestBlock.CanAddFloorObjects(4,1) && harvestBlock.CanAddFloorObjects(4,1,harvested),
            "growing tree blocks ordinary drops but permits its own harvested logs");
        Check(!harvestBlock.CanAddFloorObjects(4,1,Tree()), "another resource cannot bypass growing-tree occupancy");
        harvestBlock.Capacity = 3;
        logger = new LoggingMachine();
        logger.Harvest(harvested);
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
        Console.WriteLine($"PASS: {checks} production seed-roll/recovery/harvest checks. Managed scene and storage doubles; no engine launched.");
    }
}
public class ItemDefinition
{
    public int id; public bool isSeed, Plantable;
    public static bool IsPlantableSeedDefinition(ItemDefinition item) => item != null && item.isSeed && item.Plantable;
}
public class ResourceDropEntry
{
    public ItemDefinition ItemDefinition; public int Amount; public float DropChance, Minimum;
    public bool Matches(float growth) => growth >= Minimum;
}
public class ResourceDefinition { public IReadOnlyList<ResourceDropEntry> DropItems; }
public partial class Resource : MapObject
{
    public ResourceDefinition definition;
    public ResourceDefinition Definition => definition;
    public int initialResourceCount = 1, ResourceCount = 1;
    public bool CanHarvest = true, HarvestSucceeds = true;
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
    public partial class Tree : Resource
    {
        public bool CanGrowAnotherLevel;
        public bool TryGetMachineAppleDrop(out int id, out int count) { id = 2; count = 2; return true; }
    }
}
public class Slot
{
    public int Capacity, Count, ItemId = -1; public bool Saved;
    public bool Add(int id, int count, int capacity)
    { if (Count + count > capacity || (Count > 0 && ItemId != id)) return false; ItemId = id; Count += count; return true; }
}
public partial class Block
{
    public Vector2Int Coordinate;
    private Vector2Int coordinate => Coordinate;
    public enum BlockType { Ground, Water }
    public BlockType Type;
    public Resource Resource;
    public MapObject mapObject;
    public MapObject MapObject => mapObject;
    public int Capacity = 20;
    public List<List<PortableObject>> floorStacks = new List<List<PortableObject>> { new List<PortableObject>() };
    public bool HasDroppedFloorObjects => floorStacks[0].Count > 0;
    private void EnsureFloorObjectsInitialized() { }
    private Transform ResolveFloorObjectDropAnchor() => new Transform();
    private int ResolveFloorStackCapacity(int id) => Capacity;
    private static bool IsStackCompatible(List<PortableObject> stack, int id) => stack.Count == 0 || stack[0].ItemId == id;
    private bool IsFarmlandFertilizerItem(int id) => false;
    public bool TryAddFloorObjectAnimated(int id, Vector3 start, float delay, out object visual,
        Action onComplete = null, Func<Vector3> startProvider = null, Resource harvestedResource = null)
    { visual = null; if (!CanAddFloorObjects(1,id,harvestedResource)) return false;
      floorStacks[0].Add(new PortableObject { ItemId = id }); return true; }
    public bool TryAddHarvestedFloorObjectAnimated(int id, Vector3 start, out object visual, Resource harvestedResource)
    { visual = null; floorStacks[0].Add(new PortableObject { ItemId = id }); return true; }
    public Slot Slot;
    public bool TryAddInputAreaCenterObjectAnimated(int id, Vector3 start, float delay, out object visual)
    { visual = null; return Slot.Add(id,1,Slot.Capacity); }
}
public partial class BlockStateStore
{
    public Dictionary<Vector2Int,object> savedStates = new Dictionary<Vector2Int,object>();
    public bool HasSavedLogs;
    private bool HasSavedDroppedFloorObjects(Vector2Int coordinate) => HasSavedLogs;
    private bool TryGetInstallationAnchorAtCoordinate(Vector2Int coordinate, out Vector2Int anchor) { anchor = default; return false; }
    public Dictionary<Vector2Int,Slot> Slots;
    public bool TryAddSavedCenterItems(Vector2Int coordinate, int id, int count, int capacity) => Slots[coordinate].Add(id,count,capacity);
}
public partial class InputOutputModule : MapObject
{
    public static Dictionary<int,ItemDefinition> Definitions = new Dictionary<int,ItemDefinition>();
    public static List<InputOutputModule> Receivers = new List<InputOutputModule>();
    public static bool BlockInput;
    public bool isActiveAndEnabled = true, Placed = true;
    public int RuntimeAreaMaxObjects = 100;
    public Dictionary<int,List<Vector2Int>> Inputs = new Dictionary<int,List<Vector2Int>>();
    public Dictionary<Vector2Int,Slot> Slots = new Dictionary<Vector2Int,Slot>();
    public int Wakes;
    protected bool TryGetPlacementRuntime(out Vector2Int coordinate, out int turns) { coordinate = default; turns = 0; return Placed; }
    protected static ItemDefinition ResolveItemDefinition(int id) => Definitions.TryGetValue(id,out var item) ? item : null;
    protected void AppendRuntimeInputItemAreaCoordinates(int id, List<Vector2Int> coordinates)
    { if (Inputs.TryGetValue(id,out var input)) coordinates.AddRange(input); }
    public static bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int coordinate, int id) => !BlockInput;
    protected void WakeRuntimeUpdate() { Wakes++; }
    private bool TryResolveRuntimeAreaBlock(Vector2Int coordinate, out Block block, out bool saved)
    {
        block = null; saved = false;
        if (!Slots.TryGetValue(coordinate,out var slot)) return false;
        saved = slot.Saved; block = saved ? null : new Block { Slot = slot }; return true;
    }
    private BlockStateStore ResolveBlockStateStore() => new BlockStateStore { Slots = Slots };
    private int ResolveRuntimeBlockCenterCapacity(Vector2Int coordinate, int id, int capacity) => Slots[coordinate].Capacity;
    public static void CollectModulesAtRuntimeAreaCoordinate(Vector2Int coordinate, List<InputOutputModule> modules) => modules.AddRange(Receivers);
}
public partial class SeedPlanter : InputOutputModule
{
    private readonly List<Vector2Int> recoveredSeedInputCoordinates = new List<Vector2Int>();
    public Vector2Int Output;
    public int Refreshes;
    private bool TryResolveOutputTarget(out Vector2Int output) { output = Output; return true; }
    private void RefreshSeedInput() { Refreshes++; }
}
public partial class LoggingMachine
{
    private readonly List<KeyValuePair<int,int>> harvestedSeedDrops = new List<KeyValuePair<int,int>>();
    private readonly List<InputOutputModule> seedRecoveryModules = new List<InputOutputModule>();
    private Resource activeTree;
    private float consumedWorkEnergy;
    public Dictionary<int,int> Drops = new Dictionary<int,int>();
    public int Apples, Advances;
    public void Harvest(Resource tree) => CompleteTreeHarvest(tree);
    private void SetWorking(bool working) { }
    private void AdvanceDirection() { Advances++; }
    private void DropHarvestedSeedsNearTree(Block block, Vector3 start, int id, int count)
    { Drops.TryGetValue(id,out int old); Drops[id] = old + count; }
    private void DropApplesIntoNearbyEmptyBlocks(Block block, Vector3 start, int id, int count) { Apples += count; }
}
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
