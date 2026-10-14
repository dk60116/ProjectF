using System;
using System.Collections.Generic;
using UnityEngine;
using ForestryWorld = ProjectF.MapObjects.ForestryWorld;

// Scene occupancy and unrelated placement features are boundaries. Area queries,
// the production-world type lookup, and the complete per-block decision are real code.
public interface IMapObjectTarget { }
public class MapObject : IMapObjectTarget { }
public class InstallationObject : MapObject { }
public class Pipe : InstallationObject { }
public class Pump : InstallationObject { }
public class BoxObject : InstallationObject { }
public class ResourceInstance : MapObject { }
[Flags] public enum InstallationMapFilter { Ground = 1, ItemArea = 2, Ore = 4 }
public sealed class Block
{
    public enum BlockType { Ground, Water }
    public BlockType Type = BlockType.Ground;
    public Vector2Int Coordinate;
}
public sealed class ModuleObject { public bool activeInHierarchy = true; }
public partial class InputOutputModule : InstallationObject
{
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeGridCoordinates = new();
    public readonly ModuleObject gameObject = new();
    public Vector2Int Cell;
    public RectGridBlockType Type;
    private bool ContainsRuntimeRectGridBlockType(Vector2Int coordinate, RectGridBlockType type) => coordinate == Cell && Type == type;
    private static int CompareSimulationOrder(InputOutputModule first, InputOutputModule second) => 0;
    public bool TryGetRectGridBlockTypeAtCoordinate(MapObject source, Vector2Int anchor, int turns,
        Vector2Int coordinate, out RectGridBlockType actual)
    {
        actual = coordinate == anchor + RotateRectGridOffset(Cell, turns) ? Type : RectGridBlockType.None;
        return actual != RectGridBlockType.None;
    }
    public static void Register(InputOutputModule module)
    {
        if (!registeredRuntimeGridCoordinates.TryGetValue(module.Cell, out var owners))
            registeredRuntimeGridCoordinates.Add(module.Cell, owners = new());
        owners.Add(module);
    }
    public static void Reset() => registeredRuntimeGridCoordinates.Clear();
}
public sealed class FacilityPlacement { public int quarterTurns; }
public sealed class Facility
{
    public InputOutputModule Prototype;
    public Vector2Int AnchorCoordinate;
    public FacilityPlacement Placement = new();
}
public partial class ProductionWorld
{
    public static ProductionWorld Current;
    private readonly Dictionary<Vector2Int, List<Facility>> observers = new();
    public void Register(Vector2Int cell, Facility facility) => observers[cell] = new() { facility };
}
public class MiningWorld
{
    public static MiningWorld Current;
    public readonly Dictionary<Vector2Int, InputOutputModule.RectGridBlockType> Cells = new();
    public bool CoordinateIsBlockType(Vector2Int cell, InputOutputModule.RectGridBlockType type) => Cells.TryGetValue(cell, out var actual) && actual == type;
}
namespace ProjectF.MapObjects
{
    public sealed class ForestryWorld : MiningWorld { public new static ForestryWorld Current; }
}

public partial class InstallationPlacementController
{
    public Pipe ExistingPipe;
    public IMapObjectTarget Occupant;
    public bool FluidCompatible = true;
    public bool Place(Block block, MapObject source, MapObject ignored = null) =>
        CanPlacePreviewOnTargetBlockType(block, source, previewToIgnore: ignored);
    private bool IsNonBlockingRobotArmInteractionArea(params object[] args) => false;
    private bool CanOverlapExistingPipePass(params object[] args) => false;
    private IMapObjectTarget GetOccupyingObjectForPlacement(Block block, MapObject ignored) => Occupant;
    private bool TryGetPipePlacementAtBlock(Block block, IMapObjectTarget occupant, out Pipe pipe, out int turns)
    { pipe = ExistingPipe; turns = 0; return pipe != null; }
    private static bool TryResolveInstallationObject(MapObject source, out InstallationObject installation)
    { installation = source as InstallationObject; return installation != null; }
    private static InstallationMapFilter ResolvePlacementMapFilter(params object[] args) => InstallationMapFilter.Ground;
    private static InstallationMapFilter ResolveRectGridTerrainFilter(MapObject source, InputOutputModule.RectGridBlockType type, InstallationMapFilter filter) => filter;
    private bool IsFixedRectGridObjectBlockCoordinate(Vector2Int cell) => InputOutputModule.CoordinateIsRuntimeRectGridBlockType(cell, InputOutputModule.RectGridBlockType.Object);
    private bool IsRectGridAreaBlockType(InputOutputModule.RectGridBlockType type) => type != InputOutputModule.RectGridBlockType.None && type != InputOutputModule.RectGridBlockType.Object;
    private bool CoordinateHasInputEnergyAreaBlockForPlacement(Vector2Int cell, MapObject ignored) => InputOutputModule.CoordinateIsRuntimeInputEnergyBlock(cell);
    private bool CoordinateHasInputItemAreaBlockForPlacement(Vector2Int cell, MapObject ignored) => InputOutputModule.CoordinateIsRuntimeInputItemBlock(cell);
    private bool CoordinateHasOutputAreaBlockForPlacement(Vector2Int cell, MapObject ignored) => InputOutputModule.CoordinateIsRuntimeOutputBlock(cell);
    private bool CoordinateHasDirectItemOutputAreaBlockForPlacement(Vector2Int cell, MapObject ignored) =>
        InputOutputModule.CoordinateIsRuntimeRectGridBlockType(cell, InputOutputModule.RectGridBlockType.Output)
        || InputOutputModule.CoordinateIsRuntimeRectGridBlockType(cell, InputOutputModule.RectGridBlockType.DoublePipeOutputItem);
    private bool CoordinateHasNormalInputOutputAreaBlockForPlacement(Vector2Int cell, MapObject ignored)
    {
        // Direct-area markers already see ECS combined areas even before the fix.
        foreach (InputOutputModule.RectGridBlockType type in Enum.GetValues<InputOutputModule.RectGridBlockType>())
            if (InputOutputModule.AllowsDirectAreaInteraction(type)
                && ((ProductionWorld.Current?.CoordinateIsBlockType(cell, type) ?? false)
                    || (MiningWorld.Current?.CoordinateIsBlockType(cell, type) ?? false)
                    || (ForestryWorld.Current?.CoordinateIsBlockType(cell, type) ?? false)
                    || InputOutputModule.CoordinateIsRuntimeRectGridBlockType(cell, type))) return true;
        return false;
    }
    private bool IsPumpOutputCoordinateForPlacement(params object[] args) => false;
    private bool HasPipeAreaFluidStorageAtCoordinate(params object[] args) => false;
    private bool CanConveyorOverlapItemOutputArea(params object[] args) => false;
    private bool IsBelt2F(params object[] args) => false;
    private Vector2Int GetBelt2FBridgeCenterCoordinate(Vector2Int cell, MapObject source, int turns) => cell;
    private bool CanPlacePipeOnFixedFluidConnectorArea(params object[] args) => false;
    private bool CanPlaceOnTerrainBiome(Block block, InstallationMapFilter filter) => block.Type == Block.BlockType.Ground;
    private bool CanPlaceBoilerConnectorOnPumpBody(params object[] args) => false;
    private bool CanPlaceSprinklerConnectorOnPumpBody(params object[] args) => false;
    private bool CanPlaceBoilerConnectorOnBoilerBody(params object[] args) => false;
    private bool CanPlaceBoilerObjectOnPumpOutputArea(params object[] args) => false;
    private bool CanPlaceSprinklerObjectOnPumpOutputArea(params object[] args) => false;
    private bool CanPlaceBoilerObjectOnBoilerPipePassArea(params object[] args) => false;
    private bool CanPlaceSteamGeneratorCenterOnBoilerPipeOutput(params object[] args) => false;
    private bool CanPlaceBoilerCenterOnSteamGeneratorPipeInput(params object[] args) => false;
    private bool CanPlaceBoilerOutputOnSteamGeneratorBodyForDirectInput(params object[] args) => false;
    private bool CanPlaceSteamGeneratorStraightChainBlock(params object[] args) => false;
    private bool IsSteamGeneratorSource(params object[] args) => false;
    private bool IsBoilerSource(params object[] args) => false;
    private bool CanPlaceCompatibleRectGridOverlap(params object[] args) => false;
    private bool CanPlaceFluidStorageObjectOnPipeNode(params object[] args) => false;
    private bool IsWaterPumpSource(params object[] args) => false;
    private bool IsWaterPumpCardinalShoreCoordinate(params object[] args) => false;
    private bool IsTrainStationSource(params object[] args) => false;
    private bool TryResolveTrainStationSelectedRailCoordinate(Vector2Int cell, MapObject source, int turns, MapObject ignored, out Vector2Int selected)
    { selected = default; return false; }
    private bool CanResolvedPipePlacementMatchFixedFluidConnectors(params object[] args) => FluidCompatible;
    private bool IsResourceAllowedForPlacement(params object[] args) => false;
    private bool IsRailloadAllowedForPlacement(params object[] args) => false;
    private bool IsInstallationObjectAllowedForPlacement(params object[] args) => false;
    private bool CanPlacePassthroughOnBelt2FBridgeCenter(params object[] args) => false;
    private bool IsPipeAllowedForPlacement(params object[] args) => false;
    private bool CanPlaceBelt2FBlock(params object[] args) => false;
    private bool CanPlaceBoilerOnPumpOutputArea(params object[] args) => false;
    private bool CanPlaceBoilerAreaBesidePumpOutput(params object[] args) => false;
    private bool CanPlaceFluidStorageAreaOnPipeNode(params object[] args) => false;
    private bool CanPlaceSteamGeneratorInputOnBoilerBodyForDirectOutput(params object[] args) => false;
    private bool CanPlaceRectGridAreaBlock(params object[] args) => false;
    private bool CanPlaceFluidStorageOnRuntimePipeArea(params object[] args) => false;
    private static bool IsResourceAllowedByMapFilter(params object[] args) => false;
}

public static class AreaChecks
{
    private static int checks;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks++; }
    private static void Reset()
    {
        InputOutputModule.Reset(); ProductionWorld.Current = null;
        MiningWorld.Current = null; ForestryWorld.Current = null;
    }
    private static long MeasureQueryAllocation(Vector2Int cell)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) InputOutputModule.CoordinateAllowsRuntimePipeBlock(cell);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    public static void Main()
    {
        var anchor = new Vector2Int(100, 50);
        var offset = new Vector2Int(2, 1);
        foreach (var type in new[] { InputOutputModule.RectGridBlockType.DoubleInputItem,
            InputOutputModule.RectGridBlockType.DoublePipeOutputItem })
        {
            Reset();
            var cell = anchor + offset;
            ProductionWorld.Current = new();
            ProductionWorld.Current.Register(cell, new Facility {
                Prototype = new() { Cell = offset, Type = type }, AnchorCoordinate = anchor });
            Require(new InstallationPlacementController().Place(new Block { Coordinate = cell }, new Pipe()),
                $"ECS {type} rejected pipe placement because its pipe-area type was missing");
        }
        foreach (var type in Enum.GetValues<InputOutputModule.RectGridBlockType>())
        for (int turns = 0; turns < 4; turns++)
        for (int world = 0; world < 4; world++)
        {
            Reset();
            var cell = anchor + InputOutputModule.RotateRectGridOffset(offset, turns);
            var module = new InputOutputModule { Cell = offset, Type = type };
            if (world == 0) { module.Cell = cell; InputOutputModule.Register(module); }
            if (world == 1)
            {
                ProductionWorld.Current = new();
                ProductionWorld.Current.Register(cell, new Facility { Prototype = module,
                    AnchorCoordinate = anchor, Placement = new() { quarterTurns = turns } });
            }
            if (world == 2) { MiningWorld.Current = new(); MiningWorld.Current.Cells[cell] = type; }
            if (world == 3) { ForestryWorld.Current = new(); ForestryWorld.Current.Cells[cell] = type; }
            bool isPipeArea = InputOutputModule.AllowsPipeAreaInteraction(type);
            Require(InputOutputModule.CoordinateIsRuntimeRectGridBlockType(cell, type) == (type != InputOutputModule.RectGridBlockType.None), $"type lookup {world}/{type}/{turns}");
            Require(InputOutputModule.TryGetModuleAtRuntimeRectGridBlockType(cell, type, out var resolved)
                == (type != InputOutputModule.RectGridBlockType.None), "shared lookup disagreed with coordinate query");
            Require((resolved != null) == (world == 0 && type != InputOutputModule.RectGridBlockType.None),
                "data-only query must not fabricate a scene module");
            Require(InputOutputModule.CoordinateAllowsRuntimePipeBlock(cell) == isPipeArea, $"pipe classification {world}/{type}/{turns}");
            Require(!InputOutputModule.CoordinateAllowsRuntimePipeBlock(cell + new Vector2Int(10, 10)), "unrelated coordinate gained pipe area");
            var block = new Block { Coordinate = cell };
            var pipe = new Pipe();
            var controller = new InstallationPlacementController();
            bool expected = isPipeArea || type == InputOutputModule.RectGridBlockType.None;
            Require(controller.Place(block, pipe) == expected, $"full placement {world}/{type}/{turns}");
            if (!isPipeArea) continue;
            controller.ExistingPipe = new Pipe();
            Require(!controller.Place(block, pipe), "duplicate pipe accepted on IO area");
            controller.ExistingPipe = pipe;
            Require(controller.Place(block, pipe, pipe), "ignored preview blocked itself");
            controller.ExistingPipe = null; controller.FluidCompatible = false;
            Require(!controller.Place(block, pipe), "fluid incompatibility bypassed");
            controller.FluidCompatible = true; block.Type = Block.BlockType.Water;
            Require(!controller.Place(block, pipe), "pipe accepted on water");
        }
        Reset();
        var legacy = new InputOutputModule { Cell = anchor, Type = InputOutputModule.RectGridBlockType.DoubleInputItem };
        InputOutputModule.Register(legacy); legacy.gameObject.activeInHierarchy = false;
        Require(!InputOutputModule.CoordinateAllowsRuntimePipeBlock(anchor), "inactive legacy module still indexed");
        ProductionWorld.Current = new();
        ProductionWorld.Current.Register(anchor, new Facility { Prototype = new() {
            Type = InputOutputModule.RectGridBlockType.DoublePipeOutputItem }, AnchorCoordinate = anchor });
        Require(InputOutputModule.CoordinateAllowsRuntimePipeBlock(anchor), "inactive legacy module hid ECS area");
        MeasureQueryAllocation(anchor); // Warm the measured path before checking steady allocation.
        long allocated = MeasureQueryAllocation(anchor);
        Require(allocated == 0, $"steady ECS type queries allocated {allocated} bytes");
        legacy.gameObject.activeInHierarchy = true; ProductionWorld.Current = null;
        Require(InputOutputModule.CoordinateAllowsRuntimePipeBlock(anchor), "legacy fallback lost after ECS removal");
        Reset();
        Require(!InputOutputModule.CoordinateIsRuntimeRectGridBlockType(anchor, InputOutputModule.RectGridBlockType.None), "None must never be an occupied type");
        Console.WriteLine($"Pipe IO-area placement checks passed: {checks}");
    }
}
