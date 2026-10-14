using System;
using System.Collections.Generic;
using UnityEngine;

// Scene/ECS persistence and authored footprint geometry are fixture boundaries.
// The production collector, fixed-direction lookup and shape resolver run unchanged.
public partial class InputOutputModule
{
    public RectGridBlockType PortType = RectGridBlockType.DoubleInputItem;
    public Vector2Int PortDirection = Vector2Int.up;
    public bool TryGetNearestRectGridObjectDirection(MapObject source, Vector2Int anchor,
        int turns, Vector2Int coordinate, out Vector2Int direction)
    { direction = RotatePort(PortDirection, turns); return anchor == coordinate; }
    public static Vector2Int RotatePort(Vector2Int direction, int turns)
    {
        for (int i = 0; i < (turns & 3); i++) direction = new Vector2Int(direction.y, -direction.x);
        return direction;
    }
    public static bool CollectModulesAtRuntimeGridCoordinate(Vector2Int coordinate, List<InputOutputModule> modules) => false;
    public static bool CollectModulesAtRuntimeAreaCoordinate(Vector2Int coordinate, List<InputOutputModule> modules) => false;
}
public class WaterPump : InputOutputModule
{
    public bool TryGetPipeConnectionDirection(Quaternion rotation, out Vector2Int direction)
    { direction = RotatePort(PortDirection, rotation.Turns); return true; }
}
public class Boiler : InputOutputModule { }
public class SteamGenerator : InputOutputModule
{
    public bool TryGetInputDirectionAtCoordinate(MapObject source, Vector2Int anchor, int turns,
        Vector2Int coordinate, out Vector2Int direction) =>
        TryGetNearestRectGridObjectDirection(source, anchor, turns, coordinate, out direction);
    public bool TryGetPipeAreaConnectionDirection(Quaternion rotation, out Vector2Int direction)
    { direction = RotatePort(PortDirection, rotation.Turns); return true; }
}

public partial class InstallationPlacementController
{
    public enum PortOwnerKind { Preview, Scene, LegacyGrid, LegacyArea, SavedPrimary, EcsOverlay }
    private readonly Dictionary<Vector2Int, List<(PlacementSnapshot Snapshot, PortOwnerKind Kind)>> portOwners = new();
    private readonly List<InputOutputModule> runtimeGridModuleScratch = new(), runtimeAreaModuleScratch = new();
    private readonly List<BlockStateStore.InstallationSaveState> savedPipeAreaInstallationStatesScratch = new();
    private readonly List<PipeAreaBlockCandidate> fixedFluidPipeAreaCandidateScratch = new(), pipeAreaBlockCandidateScratchC = new();

    public void AddPortOwner(Vector2Int coordinate, InputOutputModule module, int turns, PortOwnerKind kind)
    {
        var snapshot = new PlacementSnapshot { mapObject = module, anchorCoordinate = coordinate,
            quarterTurns = turns, rotation = new Quaternion(turns) };
        if (!portOwners.TryGetValue(coordinate, out var owners)) portOwners[coordinate] = owners = new();
        owners.Add((snapshot, kind));
        if (kind == PortOwnerKind.SavedPrimary || kind == PortOwnerKind.EcsOverlay)
        {
            var state = new BlockStateStore.InstallationSaveState { Prototype = module,
                anchorCoordinate = coordinate, quarterTurns = turns };
            if (kind == PortOwnerKind.SavedPrimary) terrain.Saved[coordinate] = state;
            if (!terrain.InteractionOwners.TryGetValue(coordinate, out var saved))
                terrain.InteractionOwners[coordinate] = saved = new();
            saved.Add(state);
        }
    }
    public void HidePrimaryOwnerWithPipe(Vector2Int coordinate, Pipe pipe)
    {
        SetInstalledPipe(coordinate, pipe, -1);
        terrain.Saved[coordinate] = new BlockStateStore.InstallationSaveState
            { Prototype = pipe, anchorCoordinate = coordinate };
    }
    private bool LookupPort(Vector2Int coordinate, PortOwnerKind kind, MapObject ignored, out PlacementSnapshot snapshot)
    {
        snapshot = null;
        if (!portOwners.TryGetValue(coordinate, out var owners)) return false;
        foreach (var owner in owners)
            if (owner.Kind == kind && owner.Snapshot.mapObject != ignored) { snapshot = owner.Snapshot; return true; }
        return false;
    }
    private bool TryGetPreviewPlacementSnapshot(Vector2Int coordinate, MapObject ignored, out PlacementSnapshot snapshot) =>
        LookupPort(coordinate, PortOwnerKind.Preview, ignored, out snapshot);
    private bool TryGetInstalledPlacementSnapshot(Vector2Int coordinate, out PlacementSnapshot snapshot)
    {
        if (tanks.ContainsKey(coordinate)) return TryGetFluidTankPlacementSnapshotAtCoordinate(coordinate, null, out snapshot);
        if (neighbors.TryGetValue(coordinate, out var pipe))
        { snapshot = new PlacementSnapshot { mapObject = pipe, anchorCoordinate = coordinate }; return true; }
        return LookupPort(coordinate, PortOwnerKind.Scene, null, out snapshot);
    }
    private bool TryGetRuntimeGridPlacementSnapshot(Vector2Int coordinate, out PlacementSnapshot snapshot) =>
        LookupPort(coordinate, PortOwnerKind.LegacyGrid, null, out snapshot);
    private bool TryGetRuntimeAreaPlacementSnapshot(Vector2Int coordinate, out PlacementSnapshot snapshot) =>
        LookupPort(coordinate, PortOwnerKind.LegacyArea, null, out snapshot);
    private static bool TryGetRuntimeModulePlacementSnapshot(InputOutputModule module, out PlacementSnapshot snapshot)
    { snapshot = null; return false; }
    private bool TryGetSavedPlacementSnapshot(Vector2Int coordinate, out PlacementSnapshot snapshot)
    {
        snapshot = null;
        return terrain.TryGetInstallationStateAtCoordinate(coordinate, out var state)
            && TryCreateSavedPlacementSnapshot(state, out snapshot);
    }
    private void CollectPreviewRectGridPipeAreaBlockCandidatesAtCoordinate(Vector2Int coordinate,
        MapObject ignored, List<PipeAreaBlockCandidate> candidates)
    {
        if (!portOwners.TryGetValue(coordinate, out var owners)) return;
        foreach (var owner in owners)
            if (owner.Kind == PortOwnerKind.Preview && owner.Snapshot.mapObject != ignored)
                TryAddRectGridPipeAreaBlockCandidate(owner.Snapshot, coordinate, candidates);
    }
    private static bool TryGetRectGridFootprintBlockType(Vector2Int anchor, MapObject source, int turns,
        Vector2Int coordinate, out InputOutputModule.RectGridBlockType type)
    {
        type = source is InputOutputModule module && coordinate == anchor ? module.PortType : InputOutputModule.RectGridBlockType.None;
        return type != InputOutputModule.RectGridBlockType.None;
    }
    private static bool TryGetInputOutputModule(MapObject source, out InputOutputModule module)
    { module = source as InputOutputModule; return module != null; }
    private static bool TryResolveSteamGenerator(MapObject source, out SteamGenerator generator)
    { generator = source as SteamGenerator; return generator != null; }
    private static bool TryGetBoilerPipePassDirectionAtCoordinate(Boiler boiler, Vector2Int anchor,
        int turns, Vector2Int coordinate, out Vector2Int direction) =>
        boiler.TryGetNearestRectGridObjectDirection(boiler, anchor, turns, coordinate, out direction);
    private bool TryResolveFixedFluidConnectorCompatibility(PlacementSnapshot snapshot, Vector2Int coordinate,
        Vector2Int directionToPipe, out bool compatible)
    {
        compatible = false;
        if (snapshot.mapObject is Fluidtank) { compatible = true; return true; }
        if (snapshot.mapObject is not InputOutputModule module || !InputOutputModule.AllowsPipeAreaInteraction(module.PortType))
            return false;
        var directions = new List<Vector2Int>();
        if (!TryAppendRectGridPipeAreaConnectionDirection(snapshot, coordinate, directions)) return false;
        compatible = directionToPipe == directions[0];
        return true;
    }
    public List<Vector2Int> FixedDirections(Vector2Int coordinate, MapObject ignored = null)
    {
        var directions = new List<Vector2Int>();
        AppendFixedFluidPipeConnectionDirections(coordinate, ignored, directions);
        return directions;
    }
    public bool FixedCompatibility(Vector2Int coordinate, Vector2Int direction, out bool compatible) =>
        TryGetFixedFluidConnectorCompatibilityAtCoordinate(coordinate, direction, out compatible);
    private bool TryGetInstallPreviewAtCoordinate(Vector2Int coordinate, out MapObject preview)
    {
        preview = neighbors.TryGetValue(coordinate, out var pipe) && pipe.Preview ? pipe : null;
        return preview != null;
    }
    private bool TryGetSavedPipePlacementAtCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion rotation)
    {
        pipe = null; rotation = default;
        if (!TryGetSavedPlacementSnapshot(coordinate, out var snapshot) || snapshot.mapObject is not Pipe savedPipe) return false;
        pipe = savedPipe; rotation = snapshot.rotation; return true;
    }
}

public static class ConnectorChecks
{
    public static void Run(Action<bool, string> require)
    {
        var types = new[] { InputOutputModule.RectGridBlockType.DoubleInputItem,
            InputOutputModule.RectGridBlockType.DoublePipeOutputItem, InputOutputModule.RectGridBlockType.DoubleEnergy,
            InputOutputModule.RectGridBlockType.PipeInputItem, InputOutputModule.RectGridBlockType.PipeOutputItem,
            InputOutputModule.RectGridBlockType.PipeInputEnergy, InputOutputModule.RectGridBlockType.PipeInput };
        foreach (var type in types)
        for (int turns = 0; turns < 4; turns++)
        foreach (var kind in Enum.GetValues<InstallationPlacementController.PortOwnerKind>())
        {
            PipeWorld.Current = null;
            var controller = new InstallationPlacementController();
            var module = new InputOutputModule { PortType = type };
            controller.AddPortOwner(Vector2Int.zero, module, turns, kind);
            var prototype = VariantChecks.Prototypes();
            if (kind == InstallationPlacementController.PortOwnerKind.EcsOverlay)
                controller.HidePrimaryOwnerWithPipe(Vector2Int.zero, prototype);
            Vector2Int inward = InputOutputModule.RotatePort(Vector2Int.up, turns);
            Vector2Int branch = InputOutputModule.RotatePort(Vector2Int.right, turns);
            var fixedDirections = controller.FixedDirections(Vector2Int.zero);
            require(fixedDirections.Count == 1 && fixedDirections[0] == inward,
                $"{type}/{kind}/{turns}: overlapping pipe must not hide the machine port");
            require(controller.FixedCompatibility(Vector2Int.zero, inward, out bool compatible) && compatible,
                $"{type}/{kind}/{turns}: connector lookup must find the same owner");
            require(controller.FixedCompatibility(Vector2Int.zero, -inward, out compatible) && !compatible,
                "a fixed port must reject its opposite direction");
            var neighbor = new Pipe { Mask = 15 };
            controller.SetInstalledPipe(branch, neighbor, -1);
            Pipe selected = controller.ResolveVariant(prototype, turns, 0, out int selectedTurns);
            require(selected.VariantKind == PipeVariantKind.Corner
                && selected.HasConnectionTowards(new Quaternion(selectedTurns), inward)
                && selected.HasConnectionTowards(new Quaternion(selectedTurns), branch)
                && controller.HasPort(branch, neighbor, -branch),
                $"{type}/{kind}/{turns}: a new pipe bends between the facility and neighboring pipe");
            int baseline = prototype.GetConnectionMask(new Quaternion(turns));
            selected = controller.ResolveVariant(prototype, turns, baseline, out selectedTurns);
            require(selected.VariantKind == PipeVariantKind.Tee
                && (selected.GetConnectionMask(new Quaternion(selectedTurns)) & baseline) == baseline
                && selected.HasConnectionTowards(new Quaternion(selectedTurns), inward),
                "normalizing an existing straight adds the machine branch without losing either end");
        }
        for (int turns = 0; turns < 4; turns++)
        {
            foreach (InputOutputModule module in new InputOutputModule[] {
                new WaterPump { PortType = InputOutputModule.RectGridBlockType.PipeOutputItem },
                new Boiler { PortType = InputOutputModule.RectGridBlockType.PipeInput },
                new SteamGenerator { PortType = InputOutputModule.RectGridBlockType.DoubleInputItem } })
            {
                var controller = new InstallationPlacementController();
                controller.AddPortOwner(Vector2Int.zero, module, turns, InstallationPlacementController.PortOwnerKind.EcsOverlay);
                controller.HidePrimaryOwnerWithPipe(Vector2Int.zero, VariantChecks.Prototypes());
                var directions = controller.FixedDirections(Vector2Int.zero);
                require(directions.Count == 1 && directions[0] == InputOutputModule.RotatePort(Vector2Int.up, turns),
                    $"{module.GetType().Name}/{turns}: specialized port direction survives an overlay");
            }
        }
        foreach (var type in new[] { InputOutputModule.RectGridBlockType.None, InputOutputModule.RectGridBlockType.Object,
            InputOutputModule.RectGridBlockType.InputItem, InputOutputModule.RectGridBlockType.Output })
        {
            var controller = new InstallationPlacementController();
            controller.AddPortOwner(Vector2Int.zero, new InputOutputModule { PortType = type }, 0,
                InstallationPlacementController.PortOwnerKind.EcsOverlay);
            require(controller.FixedDirections(Vector2Int.zero).Count == 0, "item-only/body cells do not introduce fluid ports");
        }
        var overlapping = new InstallationPlacementController();
        var ignored = new InputOutputModule { PortDirection = Vector2Int.left };
        overlapping.AddPortOwner(Vector2Int.zero, ignored, 0, InstallationPlacementController.PortOwnerKind.Preview);
        var owner = new InputOutputModule { PortDirection = Vector2Int.up };
        overlapping.AddPortOwner(Vector2Int.zero, owner, 0, InstallationPlacementController.PortOwnerKind.SavedPrimary);
        overlapping.AddPortOwner(Vector2Int.zero, owner, 0, InstallationPlacementController.PortOwnerKind.EcsOverlay);
        overlapping.AddPortOwner(Vector2Int.zero, new InputOutputModule { PortDirection = Vector2Int.down }, 0,
            InstallationPlacementController.PortOwnerKind.EcsOverlay);
        var ports = overlapping.FixedDirections(Vector2Int.zero, ignored);
        require(ports.Count == 2 && ports.Contains(Vector2Int.up) && ports.Contains(Vector2Int.down),
            "multiple owners are merged, duplicates removed and ignored preview excluded");
        require(overlapping.FixedCompatibility(Vector2Int.zero, Vector2Int.right, out bool canConnect) && !canConnect,
            "recognized but incompatible overlay still reports a fixed connector");
    }
}
