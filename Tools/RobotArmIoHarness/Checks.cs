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
public class ItemDefinition { public int id; public string itemName; }
public class ItemManager { public List<ItemDefinition> ItemDefinitions = new(); }
public class GameManager { public static GameManager Instance = new(); public ItemManager ItemManger = new(); }
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
        string prefab = File.ReadAllText(Path.Combine(args[0], "FactorioProject/Assets/MapObject/Robot Arm/Robot Arm.prefab"));
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
}
