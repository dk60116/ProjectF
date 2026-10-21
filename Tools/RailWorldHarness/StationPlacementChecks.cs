using System;
using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

public sealed partial class TerrainGenerator
{
    public static bool TestSavedStationRail(BlockStateStore.InstallationSaveState state, out Vector2Int coordinate)
        => TryResolveStationRailCoordinate(state, Active.World, out coordinate);
}

public partial class InstallationPlacementController
{
    public readonly HashSet<Vector2Int> TestStationRails = new HashSet<Vector2Int>();
    private readonly Railload stationSavedRailPrototype = new Railload();
    private struct PlacementSnapshot { public MapObject mapObject; }
    private static bool IsTrainStationSource(MapObject source) => source is Trainstation;
    private TerrainGenerator ResolveInstallPreviewTerrain() => TerrainGenerator.Active;
    private bool TryGetPreviewPlacementSnapshot(Vector2Int coordinate, MapObject ignored, out PlacementSnapshot snapshot)
    { snapshot = default; return false; }
    private MapObject ResolveInstallPreviewFootprintSource(MapObject source) => source;
    private IMapObjectTarget GetOccupyingObjectForPlacement(Block block, MapObject ignored) => block.MapObject;
    private bool IsRailloadAllowedForPlacement(Block block, IMapObjectTarget occupant, InstallationMapFilter filter)
        => occupant is IRailTarget && (filter & InstallationMapFilter.Railload) != 0;
    private bool TryGetSavedPlacementSnapshot(Vector2Int coordinate, out PlacementSnapshot snapshot)
    { snapshot = new PlacementSnapshot { mapObject = stationSavedRailPrototype }; return TestStationRails.Contains(coordinate); }
    private static int NormalizePlacementQuarterTurnsForObject(MapObject source, int turns) => (turns % 4 + 4) % 4;
    private static int GetPlacementRotationCandidateCount(MapObject source) => 4;
    private List<Vector2Int> GetPlacementVisualCoordinates(Vector2Int anchor, MapObject source, int turns)
        => new List<Vector2Int> { anchor };
    public bool TestStationRail(Vector2Int center, MapObject source, int turns, out Vector2Int coordinate)
        => TryResolveTrainStationSelectedRailCoordinate(center, source, turns, null, out coordinate);
    public bool TestStationRotation(Vector2Int center, MapObject source, int turns, bool next, out int resolved)
        => next ? TryResolveNextTrainStationFacingQuarterTurns(center, source, null, turns, out resolved)
            : TryResolveTrainStationFacingQuarterTurns(center, source, null, turns, out resolved);
}

static class StationPlacementChecks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    public static void Run(TrainStationInstance runtime)
    {
        var center = runtime.RuntimeAnchorCoordinate;
        var station = new Trainstation { RuntimeAnchorCoordinate = center, RuntimePlacementSequence = 10001 };
        station.RuntimeOccupiedCoordinates.Add(center);
        var saved = new BlockStateStore.InstallationSaveState {
            anchorCoordinate = center, occupiedCoordinates = new List<Vector2Int> { center }
        };
        var placement = new InstallationPlacementController();
        var isolated = center + new Vector2Int(0, 100);
        placement.TestStationRails.Add(isolated);
        Check(!placement.TestStationRail(isolated, station, 0, out _), "A rail under the body is not an adjacent docking rail");
        placement.TestStationRails.Add(isolated + Vector2Int.up);
        Check(placement.TestStationRail(isolated, station, 0, out var adjacent) && adjacent == isolated + Vector2Int.up,
            "Saved rail beside the body is selected");
        Check(placement.TestStationRotation(isolated, station, 2, false, out int selected) && selected == 0,
            "Facing auto-selects the adjacent rail instead of the body cell");
        Check(placement.TestStationRail(center, station, 0, out var liveRail) && liveRail == center + Vector2Int.up,
            "Real ECS rail beside the station is recognized through RailWorld");
        for (int turns = 0; turns < 4; turns++)
        {
            Trainstation.TryGetFacingDirection(turns, out var direction);
            placement.TestStationRails.Add(center + direction);
        }
        for (int turns = -8; turns <= 8; turns++)
        {
            Trainstation.TryGetFacingDirection(turns, out var direction);
            var expected = center + direction;
            station.RuntimeQuarterTurns = turns;
            runtime.State.quarterTurns = turns;
            saved.quarterTurns = turns;
            Check(station.TryGetRailCoordinate(out var sceneRail) && sceneRail == expected, "Scene station docks beside its body");
            Check(runtime.TryGetRailCoordinate(out var dataRail) && dataRail == expected, "ECS station uses the same adjacent coordinate");
            Check(TerrainGenerator.TestSavedStationRail(saved, out var savedRail) && savedRail == expected, "Save lookup agrees with scene and ECS");
            Check(placement.TestStationRail(center, station, turns, out var previewRail) && previewRail == expected,
                "Preview uses the adjacent rail in the selected direction");
            Check(placement.TestStationRotation(center, station, turns, false, out int facing) && facing == (turns % 4 + 4) % 4,
                "Valid authored facing is retained");
            Check(placement.TestStationRotation(center, station, turns, true, out facing) && facing == ((turns + 1) % 4 + 4) % 4,
                "Rotation selects the next available neighboring rail");
        }
        runtime.State.quarterTurns = 0;
        station.RuntimeQuarterTurns = saved.quarterTurns = 0;
        station.RuntimeOccupiedCoordinates.Clear(); saved.occupiedCoordinates.Clear();
        Check(station.TryGetRailCoordinate(out var emptyScene) && emptyScene == center + Vector2Int.up, "Empty footprint uses anchor plus facing");
        Check(TerrainGenerator.TestSavedStationRail(saved, out var emptySaved) && emptySaved == center + Vector2Int.up, "Empty saved footprint retains adjacent docking");
        Check(!TerrainGenerator.TestSavedStationRail(null, out _), "Missing state cannot dock");
        station.RuntimePlacementSequence = 0;
        Check(!station.TryGetRailCoordinate(out _), "Unplaced station cannot dock");
        Check(!placement.TestStationRail(center, new Floor(), 0, out _), "Non-station source remains invalid");
        Console.WriteLine($"PASS adjacent station placement/docking: {checks} checks (scene, ECS, saved and preview)");
    }
}
