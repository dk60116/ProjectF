using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public readonly struct Quaternion { }
    public class Transform { public Quaternion rotation; }
}

public partial class Pipe : InstallationObject { }

// Scene lookup and renderer activation are boundaries; preview connection and
// snapshot fluid identity methods are extracted from the actual controller.
public partial class Fluidtank
{
    public readonly HashSet<Vector2Int> PreviewConnections = new();
    public void ApplyBlueprintPipeConnections(IReadOnlyList<Vector2Int> directions)
    { PreviewConnections.Clear(); foreach (var direction in directions) PreviewConnections.Add(direction); }
}
public partial class InstallationPlacementController
{
    public enum SnapshotSource { Preview, Installed, Saved }
    private sealed class PlacementSnapshot
    {
        public MapObject mapObject;
        public int storedFluidItemId = -1;
    }
    private static readonly Vector2Int[] PipeCardinalDirections =
        { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private readonly List<MapObject> installPreviewInstances = new();
    private readonly List<Vector2Int> fluidTankPreviewConnectionDirectionsScratch = new();
    private readonly Dictionary<MapObject, Vector2Int> previewAnchors = new();
    private readonly Dictionary<Vector2Int, PlacementSnapshot> previewSnapshots = new(),
        installedSnapshots = new(), savedSnapshots = new();
    private readonly Dictionary<Vector2Int, int> pipeFluids = new();
    private readonly Dictionary<Vector2Int, Pipe> pipes = new();
    private readonly Dictionary<Vector2Int, int> outputFluids = new();
    private readonly HashSet<int> adjacentPipeBranchFluidItemIdsScratch = new();
    public bool HasSameCellConnector;
    public void AddPreview(Fluidtank tank, Vector2Int coordinate)
    {
        installPreviewInstances.Add(tank);
        previewAnchors[tank] = coordinate;
        previewSnapshots[coordinate] = new() { mapObject = tank, storedFluidItemId = tank.StoredFluidItemId };
    }
    public void AddNeighbor(Vector2Int coordinate, Fluidtank tank, SnapshotSource source, int snapshotFluid = -1)
    {
        var snapshot = new PlacementSnapshot { mapObject = tank, storedFluidItemId = snapshotFluid };
        switch (source)
        {
            case SnapshotSource.Preview: AddPreview(tank, coordinate); previewSnapshots[coordinate] = snapshot; break;
            case SnapshotSource.Installed: installedSnapshots[coordinate] = snapshot; break;
            case SnapshotSource.Saved: savedSnapshots[coordinate] = snapshot; break;
        }
    }
    public void SetPipe(Vector2Int coordinate, int fluid)
    { pipeFluids[coordinate] = fluid; if (!pipes.ContainsKey(coordinate)) pipes[coordinate] = new Pipe(); }
    public void AddPipePreview(Vector2Int coordinate, int fluid)
    { SetPipe(coordinate, fluid); previewAnchors[pipes[coordinate]] = coordinate; }
    public void SetOutput(Vector2Int coordinate, int fluid) => outputFluids[coordinate] = fluid;
    public void Refresh() => RefreshFluidTankBlueprintPipeVisuals();
    public List<Vector2Int> FixedDirections(Vector2Int coordinate, bool ignorePreview = false)
        => GetPipeVariantFixedConnectionDirections(coordinate, ignorePreview ? pipes[coordinate] : null);
    private void CleanupInstallPreviewReferences() { }
    private bool TryGetPreviewAnchorCoordinate(MapObject obj, out Vector2Int coordinate) => previewAnchors.TryGetValue(obj, out coordinate);
    private static bool TryResolveInstallationObject(MapObject obj, out InstallationObject installation)
    { installation = obj as InstallationObject; return installation != null; }
    private static void AddPipeDirection(List<Vector2Int> directions, Vector2Int direction)
    { if (!directions.Contains(direction)) directions.Add(direction); }
    private static bool IsIgnoredFluidConnectorSnapshot(PlacementSnapshot snapshot, MapObject ignored) => snapshot.mapObject == ignored;
    private bool TryGetPreviewPlacementSnapshot(Vector2Int coordinate, MapObject ignored, out PlacementSnapshot snapshot)
        => previewSnapshots.TryGetValue(coordinate, out snapshot) && snapshot.mapObject != ignored;
    private bool TryGetInstalledPlacementSnapshot(Vector2Int coordinate, out PlacementSnapshot snapshot)
        => installedSnapshots.TryGetValue(coordinate, out snapshot);
    private bool TryGetSavedPlacementSnapshot(Vector2Int coordinate, out PlacementSnapshot snapshot)
        => savedSnapshots.TryGetValue(coordinate, out snapshot);
    private bool TryGetPipePlacementAtCoordinate(Vector2Int coordinate, MapObject ignored,
        out Pipe pipe, out Quaternion rotation)
    { pipes.TryGetValue(coordinate, out pipe); rotation = default; return pipe != null && pipe != ignored; }
    private bool HasEffectivePipeConnectionTowardsAt(Vector2Int coordinate, Pipe pipe, Quaternion rotation, Vector2Int direction) => true;
    private bool TryResolvePipeVariantForForcedConnection(Vector2Int coordinate, Pipe pipe,
        Quaternion rotation, Vector2Int direction, out Pipe resolved, out Quaternion resolvedRotation, MapObject ignored)
    { resolved = pipe; resolvedRotation = rotation; return true; }
    private bool TryGetAdjacentPipeBranchFluidItemId(Vector2Int tankCoordinate, Vector2Int coordinate,
        Pipe pipe, Quaternion rotation, MapObject ignored, out int fluid) => pipeFluids.TryGetValue(coordinate, out fluid);
    private bool TryResolveFluidTankBlueprintPipeOutputConnection(Vector2Int tankCoordinate,
        Vector2Int neighborCoordinate, Vector2Int direction, MapObject ignored, out int fluid)
        => outputFluids.TryGetValue(neighborCoordinate, out fluid);
    private void AppendFixedFluidPipeConnectionDirections(Vector2Int coordinate, MapObject ignored, List<Vector2Int> directions)
    { if (HasSameCellConnector) directions.Add(Vector2Int.up); }
    private static void AddPipeDirections(List<Vector2Int> target, List<Vector2Int> source)
    { foreach (var direction in source) AddPipeDirection(target, direction); }
    private bool TryGetFixedFluidConnectorCompatibilityAtCoordinate(Vector2Int coordinate, Vector2Int direction, out bool canConnect)
    { canConnect = TryGetFluidTankPlacementSnapshotAtCoordinate(coordinate, null, out _); return canConnect; }
    private bool CanPipeAreaBlocksConnect(Vector2Int first, Vector2Int second) => true;
    private bool TryCollectPipeNetworkFluidConstraintsExcludingConnection(Vector2Int coordinate, Pipe pipe,
        Quaternion rotation, MapObject ignored, Vector2Int excludedCoordinate, Vector2Int excludedDirection,
        HashSet<int> fluids, ref bool constrained)
    {
        if (pipeFluids.TryGetValue(coordinate, out int fluid) && fluid >= 0)
        { fluids.Add(fluid); constrained = true; }
        return true;
    }
}
public static class BlueprintChecks
{
    public static void Run(Action<bool, string> require)
    {
        const int oil = 113, gas = 114;
        foreach (var direction in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
        foreach (InstallationPlacementController.SnapshotSource source in Enum.GetValues<InstallationPlacementController.SnapshotSource>())
        foreach (int localFluid in new[] { -1, oil, gas })
        foreach (int neighborFluid in new[] { -1, oil, gas })
        {
            var controller = new InstallationPlacementController();
            var local = new Fluidtank { StoredFluidItemId = localFluid };
            // Saved snapshots use an empty prefab; the stored identity belongs to the snapshot.
            var neighbor = new Fluidtank { StoredFluidItemId = source == InstallationPlacementController.SnapshotSource.Saved ? -1 : neighborFluid };
            controller.AddPreview(local, Vector2Int.zero);
            controller.AddNeighbor(direction, neighbor, source, neighborFluid);
            controller.Refresh();
            bool expected = new Fluidtank { StoredFluidItemId = localFluid }
                .CanConnect(direction, new Fluidtank { StoredFluidItemId = neighborFluid });
            require(local.PreviewConnections.Contains(direction) == expected,
                $"tank blueprint matches installed compatibility: local={localFluid}, neighbor={neighborFluid}, source={source}, direction={direction.x},{direction.y}");
            if (source == InstallationPlacementController.SnapshotSource.Preview)
                require(neighbor.PreviewConnections.Contains(-direction) == expected,
                    "both preview tanks show the same boundary decision");
            require(local.PreviewConnections.Count == (expected ? 1 : 0),
                "preview exposes only its compatible adjacent tank connector");
        }
        var fallback = new InstallationPlacementController();
        var fallbackTank = new Fluidtank { StoredFluidItemId = oil };
        fallback.AddPreview(fallbackTank, Vector2Int.zero);
        fallback.SetPipe(Vector2Int.left, oil);
        fallback.SetPipe(Vector2Int.right, gas);
        fallback.Refresh();
        fallbackTank.SetConnectedFluid(Vector2Int.left, oil);
        fallbackTank.SetConnectedFluid(Vector2Int.right, gas);
        require(fallbackTank.PreviewConnections.Contains(Vector2Int.left) == fallbackTank.CanConnectPipe(Vector2Int.left, oil)
                && fallbackTank.PreviewConnections.Contains(Vector2Int.right) == fallbackTank.CanConnectPipe(Vector2Int.right, gas),
            "tank-to-pipe conflicts match installed tank boundaries");
        var refresh = new InstallationPlacementController();
        var first = new Fluidtank { StoredFluidItemId = oil };
        var second = new Fluidtank { StoredFluidItemId = gas };
        refresh.AddPreview(first, Vector2Int.zero);
        refresh.AddNeighbor(Vector2Int.right, second, InstallationPlacementController.SnapshotSource.Installed);
        refresh.Refresh();
        require(!first.PreviewConnections.Contains(Vector2Int.right), "foreign live tank starts disconnected");
        second.StoredFluidItemId = oil;
        refresh.Refresh();
        require(first.PreviewConnections.Contains(Vector2Int.right), "changing the live tank to the same fluid reconnects the preview");
        second.StoredFluidItemId = gas;
        refresh.Refresh();
        require(!first.PreviewConnections.Contains(Vector2Int.right), "preview refresh removes a newly incompatible connector");

        foreach (var direction in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
        foreach (InstallationPlacementController.SnapshotSource source in Enum.GetValues<InstallationPlacementController.SnapshotSource>())
        foreach (int localFluid in new[] { -1, oil, gas })
        foreach (int neighborFluid in new[] { -1, oil, gas })
        foreach (bool directOutput in new[] { false, true })
        {
            var controller = new InstallationPlacementController();
            var local = new Fluidtank();
            var neighbor = new Fluidtank();
            controller.AddPreview(local, Vector2Int.zero);
            controller.AddNeighbor(direction, neighbor, source);
            if (directOutput)
            {
                controller.SetOutput(-direction, localFluid);
                controller.SetOutput(direction + direction, neighborFluid);
            }
            else
            {
                controller.SetPipe(-direction, localFluid);
                controller.SetPipe(direction + direction, neighborFluid);
            }
            local.SetConnectedFluid(-direction, localFluid);
            neighbor.SetConnectedFluid(direction, neighborFluid);
            controller.Refresh();
            bool expected = local.CanConnect(direction, neighbor);
            require(local.PreviewConnections.Contains(direction) == expected,
                $"empty blueprint tanks use attached network fluids: local={localFluid}, neighbor={neighborFluid}, source={source}, directOutput={directOutput}, direction={direction.x},{direction.y}");
            if (source == InstallationPlacementController.SnapshotSource.Preview)
                require(neighbor.PreviewConnections.Contains(-direction) == expected,
                    "empty preview tanks agree on their network boundary");
        }

        foreach (InstallationPlacementController.SnapshotSource source in Enum.GetValues<InstallationPlacementController.SnapshotSource>())
        foreach (int pipeFluid in new[] { -1, oil, gas })
        foreach (int tankNetworkFluid in new[] { -1, oil, gas })
        foreach (bool sameCellConnector in new[] { false, true })
        foreach (bool ignorePreview in new[] { false, true })
        {
            var controller = new InstallationPlacementController { HasSameCellConnector = sameCellConnector };
            var tank = new Fluidtank();
            controller.AddNeighbor(Vector2Int.right, tank, source);
            controller.AddPipePreview(Vector2Int.zero, pipeFluid);
            controller.SetPipe(Vector2Int.right + Vector2Int.right, tankNetworkFluid);
            tank.SetConnectedFluid(Vector2Int.right, tankNetworkFluid);
            require(controller.FixedDirections(Vector2Int.zero, ignorePreview).Contains(Vector2Int.right)
                    == tank.CanConnectPipe(Vector2Int.left, pipeFluid),
                $"pipe blueprint respects empty tank network: pipe={pipeFluid}, tankNetwork={tankNetworkFluid}, source={source}, sameCellConnector={sameCellConnector}, ignorePreview={ignorePreview}");
        }
    }
}
