using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Railway;

static class RuntimeChecks
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static BlockStateStore.InstallationSaveState RailState(long sequence, int start, int end)
    {
        var state = new BlockStateStore.InstallationSaveState { itemId = 1, placementSequence = sequence,
            anchorCoordinate = new Vector2Int(start, 0), railRequiredItemCount = 2,
            worldPosition = new Vector3(start, 0, 0), railVisualPathPoints = new List<Vector2> { new Vector2(start, 0), new Vector2(end, 0) },
            occupiedCoordinates = new List<Vector2Int>() };
        for (int x = start; x <= end; x++) state.occupiedCoordinates.Add(new Vector2Int(x, 0));
        return state;
    }
    static TrainStationInstance Station(RailWorld world, TerrainGenerator terrain, long sequence, int x, string name)
    {
        var state = new BlockStateStore.InstallationSaveState { itemId = 2, railRequiredItemCount = 0, placementSequence = sequence,
            anchorCoordinate = new Vector2Int(x, -1), occupiedCoordinates = new List<Vector2Int> { new Vector2Int(x, -1) },
            stationName = name, worldPosition = new Vector3(x, 0, -1) };
        return (TrainStationInstance)world.RegisterRuntime(state, new Trainstation(), terrain);
    }
    public static void Run()
    {
        var terrain = TerrainGenerator.Active = new TerrainGenerator(); var world = terrain.World;
        var left = (RailInstance)world.RegisterRuntime(RailState(101, -8, 0), new Railload(), terrain);
        var right = (RailInstance)world.RegisterRuntime(RailState(102, 0, 8), new Railload(), terrain);
        Check(left.SceneObject == null && right.SceneObject == null, "Data rails never own scene components");
        Check(left.IsTargetActive && world.LiveRails.Count == 2, "Viewless rails participate in live routing");
        Check(world.LiveRails[0].View == null && world.LiveRails[0].Target == left, "Record routing target is data identity");
        Check(world.GetLiveComponent(left, .55f) == world.GetLiveComponent(right, .55f), "Viewless connected component");
        var a = Station(world, terrain, 201, -6, "A"); var b = Station(world, terrain, 202, 6, "B");
        Check(a.TryGetRailCoordinate(out var coordinate) && coordinate == new Vector2Int(-6, 0), "Viewless station docks on data rail");
        Check(SteamTrain.CanRoute(a, b), "Auto-drive builds a route entirely from data targets");
        int routeVersion = world.StationRoutingVersion;
        b.SetStationColor(new Color32(1, 2, 3, 10));
        Check(b.State.stationColorAssigned && b.State.stationColor.a == 255, "Color persists into owned state");
        Check(world.StationRoutingVersion == routeVersion, "Color-only edit keeps route cache");
        Check(terrain.MarkerChanges == 1, "Data station color invalidates map markers without cloning its DTO");
        b.SetStationName(" Renamed ");
        Check(b.State.stationName == "Renamed" && SteamTrain.HasStation("Renamed") && !SteamTrain.HasStation("B"), "Data station rename updates persistence and route lookup");
        var rails = new List<IRailTarget>(); world.CollectRailsAtCoordinate(new Vector2Int(0, 0), rails);
        Check(rails.Count == 2, "Overlapping data rails both appear in coordinate query");
        var stations = new List<ITrainStationTarget>(); world.CollectStations(new Vector2(-6, 0), 2, stations);
        Check(stations.Count == 1 && stations[0] == a, "Dock candidate spatial lookup");
        world.RemoveSaved(right.StorageKey);
        Check(!right.IsTargetActive && !right.TrySampleRenderedPath(1, out _, out _), "Removed target cannot be sampled by a train");
        Check(!SteamTrain.CanRoute(a, b), "Demolition invalidates data route cache");
        var replacement = (RailInstance)world.RegisterRuntime(RailState(102, 0, 8), new Railload(), terrain);
        Check(replacement.IsTargetActive && !right.IsTargetActive, "Reused placement identity cannot reactivate stale target");
        world.RemoveSaved(b.StorageKey);
        Check(!b.IsTargetActive && !SteamTrain.HasStation("Renamed"), "Station demolition removes runtime and route identity");
        world.CollectRailsAtCoordinate(new Vector2Int(0, 0), rails);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) world.CollectRailsAtCoordinate(new Vector2Int(0, 0), rails);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warmed data coordinate queries allocate zero bytes");
        world.Clear(); Check(!a.IsTargetActive && !left.IsTargetActive && !replacement.IsTargetActive, "World reset invalidates all identities");
        Console.WriteLine($"PASS viewless rail/station runtime: {checks} checks");
    }
}
