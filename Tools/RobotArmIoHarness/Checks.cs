using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public class FakeGameObject { public bool activeInHierarchy = true; }
public class MapObject
{
    public Vector2Int PlacementCenterCell;
    public readonly FakeGameObject gameObject = new();
}
public class Vehicle : MapObject
{
    public float CurrentVehicleSignedSpeed;
    public float CurrentVehicleSpeed => Mathf.Abs(CurrentVehicleSignedSpeed);
}
public partial class Train : Vehicle
{
    private readonly Dictionary<Train, bool> connectedTrainEnds = new();
    private readonly Queue<Train> connectionActionGroupQueue = new();
    private readonly HashSet<Train> connectionActionGroupVisited = new();
    public IReadOnlyCollection<Train> ConnectedTrains => connectedTrainEnds.Keys;
    public static void Link(Train first, Train second)
    {
        first.connectedTrainEnds[second] = true;
        second.connectedTrainEnds[first] = true;
    }
}
public sealed class FreightCar : Train { }
public class ConveyorBelt : MapObject { }
public class ConvayorBelt2F : ConveyorBelt
{
    public List<Vector2Int> RuntimeOccupiedCoordinates = new();
}
public class Spliterbelt : ConveyorBelt { }
public class Resource : MapObject
{
    public enum HarvestMode { Mining, Logging }
    public HarvestMode ResolvedHarvestMode;
}
public static class InputOutputModuleItemAreaController
{
    public static bool Registered;
    public static bool CoordinateIsItemArea(Vector2Int coordinate) => Registered;
}
public static class InputOutputModuleEnergyAreaController
{
    public static bool Registered;
    public static bool CoordinateIsEnergyArea(Vector2Int coordinate) => Registered;
}
public class BlockStateStore
{
    public class InstallationSaveState { public int itemId; }
    public InstallationSaveState State;
    public bool TryGetInstallationAnchorAtCoordinate(Vector2Int coordinate, out Vector2Int anchor)
    { anchor = coordinate; return State != null; }
    public bool TryGetInstallationStateReadOnly(Vector2Int anchor, out InstallationSaveState state)
    { state = State; return state != null; }
    public bool CanAddSavedCenterItems(Vector2Int coordinate, int itemId, int count, int capacity) => true;
}
public partial class InputOutputModule : MapObject
{
    public static readonly Dictionary<int, ItemDefinition> Definitions = new();
    public static bool RuntimeIoAccepts = true;
    public Block RuntimeOutputBlock;
    public bool UseSavedOutput, SavedOutputIsConveyor;
    public static ItemDefinition ResolveItemDefinition(int id) => Definitions.TryGetValue(id, out var definition) ? definition : null;
    public static bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int coordinate, int itemId) => RuntimeIoAccepts;
    public List<RectGridBlockPlacement> RectGridPlacements = new();
    public Vector2Int AnchorCell;
    private SlotLayoutType slotLayoutType = SlotLayoutType.RectGrid;
    private void EnsureRectGridData() { }
    private void EnsureRectGridPlacementData() { }
    private bool IsValidRectGridCell(int x, int y) => x >= 0 && y >= 0;
    private bool TryGetRectGridObjectAnchorCell(MapObject source, out Vector2Int cell) { cell = AnchorCell; return true; }
    private bool TryResolveRuntimeAreaBlock(Vector2Int coordinate, out Block block, out bool useSavedCenterStack)
    { block = RuntimeOutputBlock; useSavedCenterStack = UseSavedOutput; return block != null || useSavedCenterStack; }
    private bool CoordinateHasSavedConveyor(Vector2Int coordinate) => SavedOutputIsConveyor;
    private bool RuntimeCenterStorageAcceptsItem(Vector2Int coordinate, int itemId, Block block, bool useSaved) => true;
    private BlockStateStore ResolveBlockStateStore() => new BlockStateStore();
    private int ResolveRuntimeBlockCenterCapacity(Vector2Int coordinate, int itemId, int defaultCapacity) => defaultCapacity;
    private const int RuntimeAreaMaxObjects = 10;
}
public class ItemDefinition { public int id; public string itemName; public bool keepIoAreaItemsInPlaceWhileEditing; public MapObject mapObject; }
public class ItemManager { public List<ItemDefinition> ItemDefinitions = new(); }
public class GameManager { public static GameManager Instance = new(); public ItemManager ItemManger = new(); }
public class DroppedItemPickupGate
{
    public bool AutoPickupBlocked;
    public void SetAutoPickupBlocked(bool blocked) => AutoPickupBlocked = blocked;
}
public class PortableObject
{
    public DroppedItemPickupGate Gate;
    public T GetComponent<T>() where T : class => Gate as T;
}
public partial class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool FloorVirtualized, ConveyorVirtualized;
    public bool IsFloorObjectCoordinateVirtualized(Vector2Int coordinate) => FloorVirtualized;
    public bool IsConveyorItemCoordinateVirtualized(Vector2Int coordinate) => ConveyorVirtualized;
    public static TerrainGenerator Active = new();
    public int RemovalNotifications;
    public void NotifyConveyorItemRemovedFromBelt() => RemovalNotifications++;
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
}
public partial class Block
{
    public enum BlockType { Ground, Water }
    public MapObject MapObject;
    public bool ConveyorAccepts = true, CenterAccepts = true;
    public int ConveyorInteractionBoundaryRequests;
    public int AvailableCapacity = 1;
    public int ConveyorAdds, CenterAdds;
    public Vector3 PlacementReference, StartPosition;
    public float AddDelay;
    public PortableObject AddedObject;
    public BlockType Type = BlockType.Ground;
    public Vector2Int Coordinate;
    public int[] Items = { -1, -1 };
    public Vector3[] Positions = new Vector3[2];
    public Vector3 WorldPosition;
    public bool Enabled = true;
    private void EnsureFloorObjectsInitialized() { }
    private void CleanupConveyorStack() { }
    private bool IsConveyorStackingEnabled() => Enabled;
    private int GetConveyorLaneCount() => Items.Length;
    private int GetConveyorItemIdAtLane(int lane) => Items[lane];
    private Vector3 GetConveyorItemVisualWorldPosition(int lane) => Positions[lane];
    private PortableObject GetConveyorPortableObjectAtLane(int lane) => null;
    private PortableObject MaterializeConveyorObjectForTransfer(PortableObject item, int id, int lane) => item;
    private void ClearConveyorItemForExternalRemoval(int lane) => Items[lane] = -1;
    private void ReleaseFloorObject(PortableObject item) { }
    private void NotifyRuntimeItemStackChanged() { }
    public int GetAvailableConveyorCapacity() => AvailableCapacity;
    public void EnsureConveyorTransportInteractionBoundary() => ConveyorInteractionBoundaryRequests++;
    public bool CanAddConveyorObjects(int count) => ConveyorAccepts;
    public bool CanAddInputAreaCenterObjects(int count, int itemId) => CenterAccepts;
    public bool TryAddConveyorObjectAnimatedAtPlacement(int itemId, Vector3 placementReference, Vector3 start,
        float delay, out PortableObject output)
    {
        PlacementReference = placementReference; StartPosition = start; AddDelay = delay;
        if (!ConveyorAccepts) { output = null; return false; }
        ConveyorAdds++; output = AddedObject = new PortableObject(); return true;
    }
    public bool TryAddInputAreaCenterObjectAnimated(int itemId, Vector3 start, float delay,
        out PortableObject output)
    {
        StartPosition = start; AddDelay = delay;
        if (!CenterAccepts) { output = null; return false; }
        CenterAdds++; output = AddedObject = new PortableObject { Gate = new DroppedItemPickupGate() }; return true;
    }
}
public partial class RobotArm : InputOutputModule
{
    public static bool AllowsStackFallback(Block block) => CanPlaceSingleLineDrop(block, Vector2Int.zero);
    public static bool AllowsSavedStackFallback(BlockStateStore store) => CanPlaceSavedSingleLineDrop(store, Vector2Int.zero);
    public static bool UsesSavedDrop(TerrainGenerator terrain, Block block) => ShouldUseSavedDropCoordinate(terrain, Vector2Int.zero, block);
    private bool interactionCoordinateCacheValid;
    private long cachedInteractionPlacementSequence;
    private Vector2Int cachedPickupCoordinate, cachedDropCoordinate;
    public long RuntimePlacementSequence;
    public Vector2Int Anchor;
    public int Rotation;
    public bool Placed = true;
    public bool isActiveAndEnabled = true;
    public List<Vector2Int> RuntimeOccupiedCoordinates = new();
    private const int WakeRangeCellRadius = 1;
    private readonly List<Vector2Int> registeredWakeCoordinates = new();
    private static readonly Dictionary<Vector2Int, List<RobotArm>> WakeRobotArmsByCoordinate = new();
    public void RefreshWake() => RefreshRegisteredWakeCoordinates();
    public int WakeCount;
    private void WakeRuntimeSleep() { WakeCount++; }
    private RobotArmState state = RobotArmState.WaitingForDrop;
    public FreightCar DropTrain;
    public bool DropHasRoom;
    public bool SleepsWithCargo() => ShouldRuntimeSleepWithHeldItem();
    private TerrainGenerator ResolveTerrainGenerator() => TerrainGenerator.Active;
    private bool TryGetFreightCarObject(Block block, Vector2Int coordinate, out FreightCar car)
    { car = DropTrain; return car != null; }
    private bool CanPlaceHeldItem() => DropHasRoom && (DropTrain == null || !DropTrain.IsConsistMoving());
    public bool WakesAt(Vector2Int point) => registeredWakeCoordinates.Contains(point);
    private bool TryGetPlacementRuntime(out Vector2Int anchor, out int rotation) { anchor = Anchor; rotation = Rotation; return Placed; }
    public bool Coordinates(out Vector2Int input, out Vector2Int output)
    {
        bool foundInput = TryResolvePickupCoordinate(out input);
        bool foundOutput = TryResolveDropCoordinate(out output);
        return foundInput && foundOutput;
    }
    public void ClearPlacement() { Placed = false; InvalidateInteractionCoordinateCache(); }
}
public static partial class Checks
{
    private static int count;
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
    public static void Main(string[] args)
    {
        CheckMovingTrainTransferGuard();
        CheckConveyorPickup();
        CheckConveyorDropFallback();
        CheckMachineOutputToConveyor();
        CheckNearestBelt2FDrop();
        string robotArmSource = File.ReadAllText(Path.Combine(
            args[0],
            "FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs"));
        Require(Regex.IsMatch(
                robotArmSource,
                @"if \(hasLoadedPickupBlock && boxObject == null\)\s*\{\s*int inputAreaItemId"),
            "box storage must not fall through to the unrestricted input-area pickup path");
        Require(Regex.Matches(robotArmSource, @"\.IsConsistMoving\(\)").Count >= 4,
            "robot arm freight pickup, final take, capacity check and final placement must all reject a moving consist");
        string conveyorRuntimeSource = File.ReadAllText(Path.Combine(
            args[0],
            "FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs"));
        Require(Regex.IsMatch(
                conveyorRuntimeSource,
                @"NotifyConveyorLaneVacated\(Block destinationBlock,[\s\S]*?WakeRuntimeOutputModulesAtCoordinate\(destinationBlock\.Coordinate\)"),
            "a vacated conveyor lane must wake sleeping production modules registered on that coordinate");
        string prefab = File.ReadAllText(Path.Combine(
            args[0],
            "FactorioProject/Assets/MapObject/InputOutputModule/Robot arm/Robot arm.prefab"));
        string grid = Regex.Match(prefab, @"rectGridPlacements:\s*([\s\S]*?)  runtimeAreaMaxObjects:").Groups[1].Value;
        var arm = new RobotArm();
        foreach (Match match in Regex.Matches(grid, @"- x: (\d+)\s+y: (\d+)\s+blockType: (\d+)"))
        {
            var cell = new InputOutputModule.RectGridBlockPlacement {
                x = int.Parse(match.Groups[1].Value), y = int.Parse(match.Groups[2].Value),
                blockType = (InputOutputModule.RectGridBlockType)int.Parse(match.Groups[3].Value) };
            arm.RectGridPlacements.Add(cell);
            if (cell.blockType == InputOutputModule.RectGridBlockType.Object) arm.AnchorCell = new(cell.x, cell.y);
        }
        Require(arm.RectGridPlacements.Count == 3, "prefab must define standard input/body/output");
        Require(InstallationPlacementController.IsNonBlockingRobotArmArea(
            arm,
            InputOutputModule.RectGridBlockType.InputItem),
            "robot arm pickup area must not block placement");
        Require(InstallationPlacementController.IsNonBlockingRobotArmArea(
            arm,
            InputOutputModule.RectGridBlockType.Output),
            "robot arm drop area must not block placement");
        Require(!InstallationPlacementController.IsNonBlockingRobotArmArea(
            arm,
            InputOutputModule.RectGridBlockType.Object),
            "robot arm body must keep placement collision");
        Require(!InstallationPlacementController.IsNonBlockingRobotArmArea(
            new InputOutputModule(),
            InputOutputModule.RectGridBlockType.InputItem),
            "other installation item areas must keep their placement rules");
        Require(!InstallationPlacementController.AreasBlockPlacement(arm),
            "installed robot arm IO registrations must not become placement obstacles");
        Require(InstallationPlacementController.AreasBlockPlacement(new InputOutputModule()),
            "installed non-robot IO registrations must keep placement collision");
        foreach (MapObject conveyor in new MapObject[] { new ConveyorBelt(), new ConvayorBelt2F(), new Spliterbelt() })
        {
            Require(InstallationPlacementController.ConveyorCanOverlapOutput(
                    conveyor, false, true, false, false, false, false, false),
                "every conveyor type must be placeable on an unobstructed direct item output area");
            Require(!InstallationPlacementController.ConveyorCanOverlapOutput(
                    conveyor, false, true, true, false, false, false, false),
                "an energy input sharing the coordinate must still block conveyor placement");
            Require(!InstallationPlacementController.ConveyorCanOverlapOutput(
                    conveyor, false, true, false, true, false, false, false),
                "an item input sharing the coordinate must still block conveyor placement");
            Require(!InstallationPlacementController.ConveyorCanOverlapOutput(
                    conveyor, false, true, false, false, false, false, true),
                "items waiting in the output area must block conveyor placement");
        }
        Require(!InstallationPlacementController.ConveyorCanOverlapOutput(
                new InputOutputModule(), false, true, false, false, false, false, false),
            "non-conveyor installations must still be blocked by output areas");
        Require(!InstallationPlacementController.ConveyorCanOverlapOutput(
                new ConvayorBelt2F(), true, true, false, false, false, false, false),
            "the raised bridge center must not masquerade as a belt output surface");

        foreach (MapObject conveyor in new MapObject[] { new ConveyorBelt(), new ConvayorBelt2F(), new Spliterbelt() })
        {
            Require(InstallationPlacementController.OutputCanOverlapConveyor(
                    InputOutputModule.RectGridBlockType.Output, conveyor, false),
                "a direct item output area must be placeable over every conveyor type");
            Require(InstallationPlacementController.OutputCanOverlapConveyor(
                    InputOutputModule.RectGridBlockType.DoublePipeOutputItem, conveyor, false),
                "a combined direct-output cell must be placeable over a conveyor");
            Require(!InstallationPlacementController.OutputCanOverlapConveyor(
                    InputOutputModule.RectGridBlockType.PipeOutputItem, conveyor, false),
                "a pipe-only output area must not overlap a conveyor");
            Require(!InstallationPlacementController.OutputCanOverlapConveyor(
                    InputOutputModule.RectGridBlockType.InputItem, conveyor, false),
                "an item input area must not use the output-over-conveyor exception");
        }
        Require(!InstallationPlacementController.OutputCanOverlapConveyor(
                InputOutputModule.RectGridBlockType.Output, new InputOutputModule(), false),
            "a direct item output area must not overlap a non-conveyor installation");
        Require(!InstallationPlacementController.OutputCanOverlapConveyor(
                InputOutputModule.RectGridBlockType.Output, new ConvayorBelt2F(), true),
            "an output area must not treat the raised 2F bridge center as a belt surface");

        Require(!InstallationPlacementController.CapturesInteractionAreaItemsInEdit(
                new ItemDefinition { keepIoAreaItemsInPlaceWhileEditing = true }),
            "the ItemData edit option must leave pickup and drop area items in place");
        Require(InstallationPlacementController.CapturesInteractionAreaItemsInEdit(new ItemDefinition()),
            "definitions without the edit option must keep the existing edit-state capture behavior");
        foreach (string robotArmItemDataPath in new[]
                 {
                     "FactorioProject/Assets/Data/Items/Item_33_Robot arm.asset",
                     "FactorioProject/Assets/Data/Items/Item_106_Long Robot arm.asset"
                 })
        {
            string robotArmItemData = File.ReadAllText(Path.Combine(args[0], robotArmItemDataPath));
            Require(Regex.IsMatch(robotArmItemData, @"(?m)^  keepIoAreaItemsInPlaceWhileEditing: 1$"),
                "every current robot arm ItemData asset must preserve IO area items while editing");
        }
        foreach (var origin in new[] { Vector2Int.zero, new Vector2Int(-13, 25) })
        for (int rotation = 0; rotation < 4; rotation++)
        {
            arm.Anchor = origin; arm.Rotation = rotation; arm.RuntimePlacementSequence++;
            Require(arm.Coordinates(out var input, out var output), "configured grid must resolve both ends");
            var expected = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left }[rotation];
            Require(input == origin - expected && output == origin + expected, "four rotations must preserve the existing transfer direction");
            Require(arm.Coordinates(out var cachedInput, out var cachedOutput) && cachedInput == input && cachedOutput == output,
                "cached coordinates must remain stable");
        }
        arm.RectGridPlacements = new() {
            new() { x = 4, y = 1, blockType = InputOutputModule.RectGridBlockType.Output },
            new() { x = 2, y = 1, blockType = InputOutputModule.RectGridBlockType.Object },
            new() { x = 0, y = 1, blockType = InputOutputModule.RectGridBlockType.InputItem } };
        arm.AnchorCell = new(2, 1); arm.Anchor = new(10, 20); arm.Rotation = 0; arm.RuntimePlacementSequence++;
        Require(arm.Coordinates(out var extendedInput, out var extendedOutput) && extendedInput == new Vector2Int(8, 20)
            && extendedOutput == new Vector2Int(12, 20), "custom grid must replace legacy forward/footprint endpoint inference");
        arm.RuntimeOccupiedCoordinates.Add(arm.Anchor); arm.RefreshWake();
        Require(arm.WakesAt(extendedInput) && arm.WakesAt(extendedOutput)
            && arm.WakesAt(extendedInput + Vector2Int.left), "distant standard IO ports and adjacent belt cells must wake sleeping arms");
        int wakeCount = arm.WakeCount;
        RobotArm.WakeAroundCoordinate(extendedInput);
        RobotArm.WakeAroundCoordinate(extendedOutput);
        RobotArm.WakeAroundCoordinate(extendedInput + Vector2Int.left);
        RobotArm.WakeAroundCoordinate(extendedOutput);
        Require(arm.WakeCount == wakeCount + 4,
            "distant port notifications must actually wake the arm repeatedly without deleting its registration");
        RobotArm.WakeAroundCoordinate(new Vector2Int(500, 500));
        Require(arm.WakeCount == wakeCount + 4, "unrelated coordinates must not wake the arm");

        var arrivalTrain = new FreightCar { CurrentVehicleSignedSpeed = 0.5f };
        arm.DropTrain = arrivalTrain;
        arm.DropHasRoom = true;
        TerrainGenerator.Active.Blocks[extendedOutput] = new Block();
        Require(!arm.SleepsWithCargo(),
            "waiting for a moving train must keep retrying because stopping in the same cell has no grid wake event");
        arrivalTrain.CurrentVehicleSignedSpeed = 0f;
        Require(!arm.SleepsWithCargo(), "a stopped train with room must allow transfer to resume");
        arm.DropHasRoom = false;
        Require(arm.SleepsWithCargo(), "a full stopped train may sleep until cargo removal wakes the arm");
        arm.DropTrain = null;
        Require(arm.SleepsWithCargo(), "static blocked output must retain event-driven sleep");
        arm.isActiveAndEnabled = false; arm.RefreshWake();
        RobotArm.WakeAroundCoordinate(extendedOutput);
        Require(arm.WakeCount == wakeCount + 4, "disabled arms must not receive wake callbacks");
        Require(!arm.WakesAt(extendedInput) && !arm.WakesAt(extendedOutput), "disabling an arm must remove IO wake registrations");
        arm.ClearPlacement();
        Require(!arm.Coordinates(out _, out _), "clearing placement must invalidate cached endpoints");
        arm.Placed = true; arm.RuntimePlacementSequence++; arm.RectGridPlacements.RemoveAt(0);
        Require(!arm.Coordinates(out _, out _), "missing output must fail without inventing a virtual port");
        GameManager.Instance.ItemManger.ItemDefinitions.AddRange(new ItemDefinition[] { null, new() { id = -1 }, new() { id = 1 },
            new() { id = 2, itemName = "Water" }, new() { id = 3 }, new() { id = 4, itemName = "Steam" }, new() { id = 5, itemName = "Oil" } });
        var accepted = new HashSet<int>();
        Require(arm.TryCollectTransferItemIds(accepted) && accepted.SetEquals(new[] { 1, 3 }), "standard transfer areas accept valid solid items only");
        foreach (RobotArm.RobotArmState state in Enum.GetValues<RobotArm.RobotArmState>())
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            WriteRobotArmState(writer, new() { heldItemId = 95, state = state, pickupTimer = .2f,
                dropRetryTimer = .3f, actionTurnTimer = .4f, turnTimer = .5f, waitingForDropRetry = true });
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var loaded = ReadRobotArmState(reader);
            Require(loaded.heldItemId == 95 && loaded.state == state && loaded.pickupTimer == .2f
                && loaded.dropRetryTimer == .3f && loaded.actionTurnTimer == .4f && loaded.turnTimer == .5f
                && loaded.waitingForDropRetry && stream.Position == stream.Length, "save preserves held item and every transfer phase");
            Require(stream.Length == 26, "transfer save layout must remain compatible");
        }
        Console.WriteLine($"PASS: {count} robot arm standard IO and transfer save checks.");
    }

    private static void CheckMovingTrainTransferGuard()
    {
        var engine = new Train();
        var middle = new Train();
        var freightCar = new FreightCar();
        Train.Link(engine, middle);
        Train.Link(middle, freightCar);

        Require(!freightCar.IsConsistMoving(),
            "a fully stopped consist must allow robot arm freight transfer");
        engine.CurrentVehicleSignedSpeed = 0.5f;
        Require(freightCar.IsConsistMoving(),
            "a freight car must inherit the connected locomotive's moving state");
        engine.CurrentVehicleSignedSpeed = 0f;
        middle.CurrentVehicleSignedSpeed = -0.25f;
        Require(freightCar.IsConsistMoving(),
            "movement anywhere in a connected cyclic graph must block freight transfer");
        middle.CurrentVehicleSignedSpeed = 0.00005f;
        Require(!freightCar.IsConsistMoving(),
            "sub-threshold numerical speed noise must still count as stopped");
    }

    private static void CheckMachineOutputToConveyor()
    {
        Vector3 start = new Vector3(3f, 2f, 1f);
        var outputModule = new InputOutputModule();
        var beltBlock = new Block { MapObject = new ConveyorBelt() };
        outputModule.RuntimeOutputBlock = beltBlock;
        Require(outputModule.CanAcceptRuntimeOutput(Vector2Int.zero, 17, 1),
            "machine output capacity must use conveyor lane capacity");
        Require(beltBlock.ConveyorInteractionBoundaryRequests == 1,
            "a conveyor under a machine output must become an observable transport boundary");
        Require(InputOutputModule.EmitOutputItem(beltBlock, 17, start, .25f, out PortableObject beltItem),
            "machine output must enter an installed conveyor lane");
        Require(beltItem == beltBlock.AddedObject && beltBlock.ConveyorAdds == 1 && beltBlock.CenterAdds == 0,
            "conveyor output must not create an input-area center stack");
        Require(beltBlock.PlacementReference == start && beltBlock.StartPosition == start && beltBlock.AddDelay == .25f,
            "conveyor output must preserve lane-selection reference and animation timing");

        var fullBeltBlock = new Block { MapObject = new ConveyorBelt(), ConveyorAccepts = false };
        outputModule.RuntimeOutputBlock = fullBeltBlock;
        Require(!outputModule.CanAcceptRuntimeOutput(Vector2Int.zero, 17, 1),
            "a full conveyor must block machine completion");
        Require(fullBeltBlock.ConveyorInteractionBoundaryRequests == 1,
            "even an initially full output belt must leave the packed transport path before the producer sleeps");
        Require(!InputOutputModule.EmitOutputItem(fullBeltBlock, 17, start, 0f, out _)
                && fullBeltBlock.CenterAdds == 0,
            "a full conveyor must backpressure the machine without falling back to a center stack");

        InputOutputModule.RuntimeIoAccepts = false;
        fullBeltBlock.ConveyorAccepts = true;
        Require(!outputModule.CanAcceptRuntimeOutput(Vector2Int.zero, 17, 1),
            "an incompatible overlapping input filter must still reject conveyor output");
        InputOutputModule.RuntimeIoAccepts = true;

        outputModule.RuntimeOutputBlock = null;
        outputModule.UseSavedOutput = true;
        outputModule.SavedOutputIsConveyor = true;
        Require(!outputModule.CanAcceptRuntimeOutput(Vector2Int.zero, 17, 1),
            "unloaded conveyors must not receive output through the saved center stack");

        var groundBlock = new Block();
        outputModule.RuntimeOutputBlock = groundBlock;
        outputModule.UseSavedOutput = false;
        outputModule.SavedOutputIsConveyor = false;
        Require(outputModule.CanAcceptRuntimeOutput(Vector2Int.zero, 17, 1),
            "ordinary output areas must retain center-stack capacity checks");
        Require(InputOutputModule.EmitOutputItem(groundBlock, 17, start, 0f, out PortableObject floorItem)
                && groundBlock.CenterAdds == 1 && groundBlock.ConveyorAdds == 0,
            "an ordinary output area must retain center-stack emission");
        Require(floorItem.Gate != null && floorItem.Gate.AutoPickupBlocked,
            "ordinary output items must retain their pickup gate");
    }

    private static void CheckNearestBelt2FDrop()
    {
        var terrain = new TerrainGenerator();
        var belt2F = new ConvayorBelt2F();
        var left = new Block { MapObject = belt2F, Coordinate = new Vector2Int(-1, 0), WorldPosition = new Vector3(-1f, 0f, 0f) };
        var center = new Block { MapObject = belt2F, Coordinate = Vector2Int.zero, WorldPosition = Vector3.zero };
        var right = new Block { MapObject = belt2F, Coordinate = new Vector2Int(1, 0), WorldPosition = new Vector3(1f, 0f, 0f) };
        foreach (Block block in new[] { left, center, right })
        {
            belt2F.RuntimeOccupiedCoordinates.Add(block.Coordinate);
            terrain.Blocks.Add(block.Coordinate, block);
        }

        Require(terrain.ResolveNearestBelt2FDropBlock(center, new Vector3(.9f, 5f, 0f), out Block nearest)
                && nearest == right,
            "2F belt drops must start at the available belt cell nearest the player in the horizontal plane");
        right.AvailableCapacity = 0;
        Require(terrain.ResolveNearestBelt2FDropBlock(center, new Vector3(.9f, 0f, 0f), out nearest)
                && nearest == center,
            "a full nearest 2F cell must select the next closest available cell");
        center.MapObject = new ConveyorBelt();
        Require(terrain.ResolveNearestBelt2FDropBlock(left, new Vector3(.1f, 0f, 0f), out nearest)
                && nearest == left,
            "a crossing belt at the bridge center must not receive an upper 2F drop");
        Require(!terrain.ResolveNearestBelt2FDropBlock(
                new Block { MapObject = new ConveyorBelt() },
                Vector3.zero,
                out _),
            "ordinary conveyors must retain their focused-block drop behavior");
    }

    private static void CheckConveyorDropFallback()
    {
        var terrain = new TerrainGenerator();
        var store = new BlockStateStore { State = new() { itemId = 99 } };
        foreach (MapObject belt in new MapObject[] { new ConveyorBelt(), new ConvayorBelt2F(), new Spliterbelt() })
        for (int overlap = 0; overlap < 4; overlap++)
        {
            InputOutputModuleItemAreaController.Registered = (overlap & 1) != 0;
            InputOutputModuleEnergyAreaController.Registered = (overlap & 2) != 0;
            var block = new Block { MapObject = belt };
            InputOutputModule.Definitions[99] = new() { mapObject = belt };
            Require(!RobotArm.AllowsStackFallback(block), "a full or unavailable belt must never fall back to a center stack, even under IO areas");
            Require(!RobotArm.AllowsSavedStackFallback(store), "saved belts must not use center stacks when lane placement fails");
            for (int virtualization = 0; virtualization < 4; virtualization++)
            {
                terrain.FloorVirtualized = (virtualization & 1) != 0;
                terrain.ConveyorVirtualized = (virtualization & 2) != 0;
                Require(RobotArm.UsesSavedDrop(terrain, block) == terrain.ConveyorVirtualized,
                    "belt deposits must follow lane virtualization independently of floor-stack virtualization");
            }
        }
        InputOutputModuleItemAreaController.Registered = true;
        InputOutputModuleEnergyAreaController.Registered = false;
        InputOutputModule.Definitions[99] = new() { mapObject = new InputOutputModule() };
        Require(RobotArm.AllowsStackFallback(new Block { MapObject = new InputOutputModule() }), "normal machine input areas must still accept stacks");
        Require(RobotArm.AllowsSavedStackFallback(store), "saved machine input areas must still accept stacks");
        InputOutputModuleItemAreaController.Registered = false;
        Require(RobotArm.AllowsStackFallback(new Block()), "empty ground must still accept stacks");
        Require(RobotArm.AllowsSavedStackFallback(new BlockStateStore()), "saved empty ground must still accept stacks");
        Require(!RobotArm.AllowsStackFallback(new Block { MapObject = new InputOutputModule() }), "unregistered machine bodies must still block stacks");
        Require(RobotArm.UsesSavedDrop(terrain, null), "unloaded coordinates must use saved state");
    }

    private static void CheckConveyorPickup()
    {
        var belt = new Block { Items = new[] { 10, 20 }, Positions = new[] { new Vector3(12, 0, 0), new Vector3(11, 0, 0) } };
        var body = Vector3.zero;
        Require(belt.TryGetClosestConveyorObjectWorldPosition(body, null, out var position) && position == belt.Positions[1],
            "both belt slots must be eligible beyond the old radius; closest to the body wins");
        Require(belt.TryTakeOneConveyorObject(body, null, out int itemId) && itemId == 20 && belt.Items[0] == 10,
            "actual pickup must match the nearest preview and remove only one item");
        Require(belt.TryTakeOneConveyorObject(body, null, out itemId) && itemId == 10,
            "the remaining far slot must be picked without approaching the hand");
        Require(!belt.TryTakeOneConveyorObject(body, null, out _), "an empty belt must not supply an item");
        belt.Items = new[] { 10, 20 };
        Require(belt.TryTakeOneConveyorObject(body, id => id == 10, out itemId) && itemId == 10 && belt.Items[1] == 20,
            "item filter must still reject the closer slot");
        belt.Items = new[] { 10, 20 };
        Require(belt.TryTakeOneConveyorObject(new Vector3(15, 0, 0), null, out itemId) && itemId == 10,
            "changing the body side must reverse nearest-slot priority");
        belt.Enabled = false;
        Require(!belt.TryTakeOneConveyorObject(body, null, out _), "non-conveyor storage must not be picked as belt items");
    }
}
