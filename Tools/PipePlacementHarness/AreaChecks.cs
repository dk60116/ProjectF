using System;
using System.Collections.Generic;
using UnityEngine;
using ForestryWorld = ProjectF.MapObjects.ForestryWorld;

// Scene occupancy and unrelated placement features are boundaries. Area queries,
// the production-world type lookup, and the complete per-block decision are real code.
public interface IMapObjectTarget { }
public class MapObject : IMapObjectTarget { }
public class InstallationObject : MapObject { public InstallationMapFilter MapFilter = InstallationMapFilter.Ground; }
public class Pipe : InstallationObject { public Pipe StraightVariantPrefab; }
public class Wall : InstallationObject { public Wall StraightVariantPrefab; }
public class ConveyorBelt : InstallationObject
{
    public bool IsCornerVariant;
    public ConveyorBelt StraightVariantPrefab;
}
public class Pump : InstallationObject { }
public class BoxObject : InstallationObject { }
public class Floor : InstallationObject { }
public class Train : InstallationObject { }
public partial class Trainstation : InstallationObject { }
public class Railload : InstallationObject, ProjectF.Railway.IRailTarget { }
public class DataRail : IMapObjectTarget, ProjectF.Railway.IRailTarget { }
namespace ProjectF.Railway { public interface IRailTarget { } }
public class ResourceInstance : MapObject { }
public sealed class Block
{
    public enum BlockType { Ground, Water }
    public BlockType Type = BlockType.Ground;
    public Vector2Int Coordinate;
    public IMapObjectTarget MapObject;
}
public sealed class ModuleObject { public bool activeInHierarchy = true; }
public partial class InputOutputModule : InstallationObject
{
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeGridCoordinates = new();
    public readonly ModuleObject gameObject = new();
    public Vector2Int Cell;
    public RectGridBlockType Type;
    public struct RectGridBlockPlacement { public int x, y; public RectGridBlockType blockType; }
    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements => null;
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
    public readonly HashSet<Vector2Int> StationRailCells = new();
    public MapObject SavedRail;
    private MapObject activeInstallPreview => null;
    private ItemDefinition activeInstallDefinition => null;
    private sealed class ItemDefinition { public MapObject mapObject => null; }
    public bool FluidCompatible = true;
    public bool Place(Block block, MapObject source, MapObject ignored = null, int turns = 0) =>
        CanPlacePreviewOnTargetBlockType(block, source, quarterTurns: turns, previewToIgnore: ignored);
    private bool IsNonBlockingRobotArmInteractionArea(params object[] args) => false;
    private bool CanOverlapExistingPipePass(params object[] args) => false;
    private IMapObjectTarget GetOccupyingObjectForPlacement(Block block, MapObject ignored) => Occupant ?? block.MapObject;
    private bool TryGetPipePlacementAtBlock(Block block, IMapObjectTarget occupant, out Pipe pipe, out int turns)
    { pipe = ExistingPipe; turns = 0; return pipe != null; }
    private static bool TryResolveInstallationObject(MapObject source, out InstallationObject installation)
    { installation = source as InstallationObject; return installation != null; }
    public bool GridSharesInstalled(Block block, MapObject source) => CanInstallGridSourceShareInstalledCell(block, source);
    public bool GridSharesSaved(MapObject source, MapObject occupant) => CanInstallGridSourceShareSavedInstallationCell(source, occupant);
    private int GetPreviewQuarterTurns(MapObject preview) => 0;
    private int GetPreferredInstallPreviewQuarterTurns(ItemDefinition definition, MapObject preview) => 0;
    private static int GetPlacementRotationCandidateCount(MapObject source) => 4;
    private int NormalizeInstallPreviewQuarterTurns(MapObject preview, int turns) => (turns % 4 + 4) % 4;
    private static Vector2Int RotateFootprintOffset(Vector2Int offset, int turns) => InputOutputModule.RotateRectGridOffset(offset, turns);
    private static bool TryGetInputOutputModule(MapObject source, out InputOutputModule module)
    { module = source as InputOutputModule; return module != null; }
    private static bool TryGetRectGridFootprintSettings(MapObject source, out int width, out int height, out Vector2Int anchor)
    { width = height = 0; anchor = default; return false; }
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
    private bool CanPlaceOnTerrainBiome(Block block, InstallationMapFilter filter) => block.Type == Block.BlockType.Ground && (filter & InstallationMapFilter.Ground) != 0;
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
    private static bool IsTrainStationSource(MapObject source) => source is Trainstation;
    private static bool IsRailloadSource(MapObject source) => source is Railload;
    private bool HasRailloadForTrainStationAtCoordinate(Vector2Int cell, MapObject ignored) => StationRailCells.Contains(cell);
    private static int NormalizePlacementQuarterTurnsForObject(MapObject source, int turns) => (turns % 4 + 4) % 4;
    private List<Vector2Int> GetPlacementVisualCoordinates(Vector2Int anchor, MapObject source, int turns) => new() { anchor };
    private bool TryGetSavedInstallationPlacementAtCoordinate(Vector2Int cell, out MapObject source, out int anchor, out int turns)
    { source = SavedRail; anchor = turns = 0; return source != null; }
    private bool CanResolvedPipePlacementMatchFixedFluidConnectors(params object[] args) => FluidCompatible;
    private bool IsResourceAllowedForPlacement(params object[] args) => false;
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
        CheckDieselRailPlacement();
        CheckTrainRailGrid();
        CheckStationPlacement();
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

    private static void CheckDieselRailPlacement()
    {
        Reset();
        var diesel = new Train { MapFilter = (InstallationMapFilter)DieselAsset.MapFilter };
        var block = new Block();
        var placement = new InstallationPlacementController();
        foreach (IMapObjectTarget rail in new IMapObjectTarget[] { new Railload(), new DataRail() })
        {
            placement.Occupant = rail;
            Require(placement.Place(block, diesel), "Diesel body-cell validation must accept scene and ECS rails using the real prefab filter");
        }
        placement.Occupant = null;
        Require(placement.Place(block, diesel), "Diesel must retain ground-first placement");
        block.Type = Block.BlockType.Water;
        Require(!placement.Place(block, diesel), "Enabling rails must not permit water placement");
        block.Type = Block.BlockType.Ground;
        placement.Occupant = new BoxObject();
        Require(!placement.Place(block, diesel), "Enabling rails must not permit overlapping other buildings");
        Reset();
    }

    private static void CheckTrainRailGrid()
    {
        Reset();
        foreach (var asset in TrainAssets.All)
        foreach (bool hasExplicitRailFilter in new[] { true, false })
        {
            var source = new Train { MapFilter = (InstallationMapFilter)asset.Filter };
            if (!hasExplicitRailFilter) source.MapFilter &= ~InstallationMapFilter.Railload;
            var controller = new InstallationPlacementController();
            var block = new Block();
            foreach (IMapObjectTarget rail in new IMapObjectTarget[] { new Railload(), new DataRail() })
            {
                controller.Occupant = rail;
                Require(controller.GridSharesInstalled(block, source),
                    asset.Name + ": scene/ECS rail must not be a red grid obstacle, even without an authored rail flag");
                Require(controller.Place(block, source), asset.Name + ": placement must agree with the grid");
            }
            Require(controller.GridSharesSaved(source, new Railload()),
                asset.Name + ": saved rail grid must agree before scene materialization");
            controller.Occupant = new BoxObject();
            Require(!controller.GridSharesInstalled(block, source), asset.Name + ": buildings still block the grid");
            Require(!controller.GridSharesSaved(source, new BoxObject()), asset.Name + ": saved buildings still block");
            controller.Occupant = new DataRail();
            block.Type = Block.BlockType.Water;
            Require(!controller.GridSharesInstalled(block, source), asset.Name + ": rail support does not bypass terrain");
        }
        var ordinary = new BoxObject();
        var placement = new InstallationPlacementController { Occupant = new DataRail() };
        Require(!placement.GridSharesInstalled(new Block(), ordinary), "Buildings without the rail filter still see rails as obstacles");
        Require(!placement.GridSharesSaved(ordinary, new Railload()), "Saved rails remain blocked for ordinary buildings");
        Reset();
    }

    private static void CheckStationPlacement()
    {
        Reset();
        var station = new Trainstation { MapFilter = InstallationMapFilter.Ground | InstallationMapFilter.Floor };
        var block = new Block { Coordinate = new Vector2Int(10, 20) };
        var placement = new InstallationPlacementController();
        placement.StationRailCells.Add(block.Coordinate + Vector2Int.up);
        Require(placement.Place(block, station), "Station must install on ground beside a rail");
        foreach (var occupant in new IMapObjectTarget[] { new Railload(), new DataRail(), new Train(), new BoxObject(), new Trainstation() })
        {
            placement.Occupant = occupant;
            Require(!placement.Place(block, station), "Station must not overlap rail, train or building: " + occupant.GetType().Name);
        }
        placement.Occupant = null;
        placement.StationRailCells.Clear();
        Require(!placement.Place(block, station), "Station must reject isolated ground");
        placement.StationRailCells.Add(block.Coordinate);
        Require(!placement.Place(block, station), "Rail under the station cannot replace an adjacent rail");
        for (int turns = -8; turns <= 8; turns++)
        {
            Trainstation.TryGetFacingDirection(turns, out var direction);
            placement.StationRailCells.Clear();
            placement.StationRailCells.Add(block.Coordinate + direction);
            Require(placement.Place(block, station, turns: turns), "Station must install beside the rail it faces");
        }
        placement.StationRailCells.Clear();
        placement.StationRailCells.Add(block.Coordinate + Vector2Int.up);
        station.MapFilter = InstallationMapFilter.None;
        Require(!placement.Place(block, station), "Station must honor None terrain filter");
        station.MapFilter = InstallationMapFilter.Ground;
        block.Type = Block.BlockType.Water;
        Require(!placement.Place(block, station), "Station must honor terrain restrictions");
        Reset();
    }
}
