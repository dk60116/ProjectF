using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public class MapObject { public Vector2Int PlacementCenterCell; }
public class ConveyorBelt : MapObject { }
public class ConvayorBelt2F : ConveyorBelt { }
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
}
public partial class InputOutputModule : MapObject
{
    public static readonly Dictionary<int, ItemDefinition> Definitions = new();
    public static ItemDefinition ResolveItemDefinition(int id) => Definitions.TryGetValue(id, out var definition) ? definition : null;
    public List<RectGridBlockPlacement> RectGridPlacements = new();
    public Vector2Int AnchorCell;
    private SlotLayoutType slotLayoutType = SlotLayoutType.RectGrid;
    private void EnsureRectGridData() { }
    private void EnsureRectGridPlacementData() { }
    private bool IsValidRectGridCell(int x, int y) => x >= 0 && y >= 0;
    private bool TryGetRectGridObjectAnchorCell(MapObject source, out Vector2Int cell) { cell = AnchorCell; return true; }
}
public class ItemDefinition { public int id; public string itemName; public bool keepIoAreaItemsInPlaceWhileEditing; public MapObject mapObject; }
public class ItemManager { public List<ItemDefinition> ItemDefinitions = new(); }
public class GameManager { public static GameManager Instance = new(); public ItemManager ItemManger = new(); }
public class PortableObject { }
public class TerrainGenerator
{
    public bool FloorVirtualized, ConveyorVirtualized;
    public bool IsFloorObjectCoordinateVirtualized(Vector2Int coordinate) => FloorVirtualized;
    public bool IsConveyorItemCoordinateVirtualized(Vector2Int coordinate) => ConveyorVirtualized;
    public static TerrainGenerator Active = new();
    public int RemovalNotifications;
    public void NotifyConveyorItemRemovedFromBelt() => RemovalNotifications++;
}
public partial class Block
{
    public MapObject MapObject;
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
        CheckConveyorPickup();
        CheckConveyorDropFallback();
        string robotArmSource = File.ReadAllText(Path.Combine(
            args[0],
            "FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs"));
        Require(Regex.IsMatch(
                robotArmSource,
                @"if \(hasLoadedPickupBlock && boxObject == null\)\s*\{\s*int inputAreaItemId"),
            "box storage must not fall through to the unrestricted input-area pickup path");
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
        arm.isActiveAndEnabled = false; arm.RefreshWake();
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
