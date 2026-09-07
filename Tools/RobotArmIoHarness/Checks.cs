using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public class MapObject { public Vector2Int PlacementCenterCell; }
public partial class InputOutputModule : MapObject
{
    public List<RectGridBlockPlacement> RectGridPlacements = new();
    public Vector2Int AnchorCell;
    private SlotLayoutType slotLayoutType = SlotLayoutType.RectGrid;
    private void EnsureRectGridData() { }
    private void EnsureRectGridPlacementData() { }
    private bool IsValidRectGridCell(int x, int y) => x >= 0 && y >= 0;
    private bool TryGetRectGridObjectAnchorCell(MapObject source, out Vector2Int cell) { cell = AnchorCell; return true; }
}
public class ItemDefinition { public int id; public string itemName; public bool keepIoAreaItemsInPlaceWhileEditing; }
public class ItemManager { public List<ItemDefinition> ItemDefinitions = new(); }
public class GameManager { public static GameManager Instance = new(); public ItemManager ItemManger = new(); }
public class PortableObject { }
public class TerrainGenerator
{
    public static TerrainGenerator Active = new();
    public int RemovalNotifications;
    public void NotifyConveyorItemRemovedFromBelt() => RemovalNotifications++;
}
public partial class Block
{
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
