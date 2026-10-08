using System;
using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < .0001f, message);
    static Railload Rail(long sequence, params Vector2[] points)
    {
        var rail = new Railload { RuntimePlacementSequence = sequence, RuntimeAnchorCoordinate = Vector2Int.RoundToInt(points[0]) };
        rail.RuntimeOccupiedCoordinates.Add(Vector2Int.RoundToInt(points[0]));
        rail.RuntimeOccupiedCoordinates.Add(Vector2Int.RoundToInt(points[points.Length - 1]));
        rail.Configure(points);
        TerrainGenerator.Active.World.UpsertLive(rail);
        return rail;
    }
    static BlockStateStore.InstallationSaveState Save(Railload rail)
        => new BlockStateStore.InstallationSaveState {
            placementSequence = rail.RuntimePlacementSequence, anchorCoordinate = rail.RuntimeAnchorCoordinate,
            railVisualPathPoints = new List<Vector2>(rail.RuntimeVisualPathPoints),
            occupiedCoordinates = new List<Vector2Int>(rail.RuntimeOccupiedCoordinates) };
    static Trainstation Station(long sequence, Vector2Int coordinate, string name)
    {
        var station = new Trainstation { RuntimePlacementSequence = sequence,
            RuntimeAnchorCoordinate = coordinate + Vector2Int.down, RuntimeQuarterTurns = 0, ItemId = 2 };
        station.ApplyStationName(name);
        return station;
    }
    static void NewWorld() => TerrainGenerator.Active = new TerrainGenerator();

    static void Main()
    {
        RailPlacementChecks.Run();
        RailCurveChecks.Run();
        RuntimeChecks.Run();
        DebugRendererChecks.Run();
        NewWorld();
        var rail = Rail(1, new Vector2(-4, 0), new Vector2(4, 0));
        Check(rail.TryGetPathData(out var path), "Live rail exposes data geometry");
        Near(path.Length, 8f, "Source length");
        Check(path.TrySample(4, out var p, out var t), "Data sample");
        Near(p.x, 0f, "Sample position"); Near(t.x, 1f, "Sample tangent");
        Check(path.TryFindNearest(new Vector2(1, 3), out var d, out p, out t, out var sqr), "Nearest sample");
        Near(d, 5f, "Nearest distance"); Near(sqr, 9f, "Nearest squared error");
        rail.Configure(new[] { new Vector2(-4, 0), new Vector2(4, 0) }, true, true);
        TerrainGenerator.Active.World.UpsertLive(rail);
        Near(path.Length, 9f, "Endpoint extension retained");
        Near(path.Samples[0].x, -4.5f, "Start extension");
        rail.Configure(Array.Empty<Vector2>());
        rail.RuntimeOccupiedCoordinates = new List<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1) };
        Check(rail.TryGetPathData(out path), "Legacy coordinate geometry");
        Check(path.Samples.Count > 10 && path.Length > 2, "Curved legacy geometry uses production builder");

        NewWorld(); var world = TerrainGenerator.Active.World;
        var left = Rail(1, new Vector2(-16, 0), new Vector2(0, 0));
        var bridge = Rail(2, new Vector2(.4f, 0), new Vector2(8, 0));
        bridge.RuntimeOccupiedCoordinates = new List<Vector2Int> { new Vector2Int(1, 0), new Vector2Int(7, 0) };
        bridge.Configure(new[] { new Vector2(.4f, 0), new Vector2(8, 0) });
        world.UpsertLive(bridge);
        var right = Rail(3, new Vector2(8.2f, 0), new Vector2(16, 0));
        var savedBridge = Save(bridge);
        world.UpsertSaved(savedBridge);
        world.UpsertSaved(null);
        Check(world.LiveRails.Count == 3, "Saved/live representations are deduplicated");
        Check(world.GetLiveComponent(left, .55f) == world.GetLiveComponent(right, .55f), "Chain connected");
        int component = world.FindComponentAtPoint(new Vector2(-10, 0));
        Check(component == world.FindComponentAtPoint(new Vector2(12, 0)), "Persistent chain connected");
        int builds = world.ComponentBuilds, version = world.TopologyVersion;
        for (int i = 0; i < 100; i++) Check(world.FindComponentAtPoint(new Vector2(12, 0)) == component, "Stable repeated query");
        Check(world.ComponentBuilds == builds, "Unchanged graph is not rebuilt");
        world.UpsertSaved(Save(bridge));
        Check(world.TopologyVersion == version, "Saving a live rail does not invalidate topology");
        world.DetachLive(bridge);
        Check(world.LiveRails.Count == 2, "Streamed view detached");
        Check(world.TopologyVersion == version, "Streaming out equivalent geometry preserves persistent topology");
        Check(world.FindComponentAtPoint(new Vector2(-10, 0)) == world.FindComponentAtPoint(new Vector2(12, 0)), "Stored bridge remains connected");
        Check(world.GetLiveComponent(left, .55f) != world.GetLiveComponent(right, .55f), "Live-only routing excludes unloaded bridge");
        world.UpsertLive(bridge);
        Check(world.TopologyVersion == version && world.LiveRails.Count == 3, "Equivalent view reattachment preserves persistent topology");
        world.DetachLive(bridge);
        savedBridge.railVisualPathPoints = new List<Vector2> { new Vector2(50, 0), new Vector2(60, 0) };
        savedBridge.occupiedCoordinates = new List<Vector2Int> { new Vector2Int(50, 0), new Vector2Int(60, 0) };
        world.UpsertSaved(savedBridge);
        Check(world.FindComponentAtPoint(new Vector2(-10, 0)) != world.FindComponentAtPoint(new Vector2(12, 0)), "Stored geometry replacement splits chain");
        Check(world.CoordinateExists(new Vector2Int(50, 0)), "Stored spatial index updated");
        Check(!world.CoordinateExists(new Vector2Int(7, 0)), "Old bridge index removed");
        world.RemoveSaved(savedBridge.anchorCoordinate);
        Check(!world.CoordinateExists(new Vector2Int(50, 0)), "Demolition removes stored geometry");
        world.UpsertLive(bridge); world.UpsertSaved(Save(bridge)); world.RemoveSaved(bridge.RuntimeAnchorCoordinate);
        Check(world.LiveRails.Count == 2, "Demolition removes live and saved representations together");
        world.DetachLive(bridge); Check(world.LiveRails.Count == 2, "Delayed detach cannot resurrect demolished record");
        left.Configure(Array.Empty<Vector2>()); left.RuntimeOccupiedCoordinates.Clear(); world.UpsertLive(left);
        Check(world.LiveRails.Count == 1 && world.FindComponentAtPoint(new Vector2(-10, 0)) == -1, "Invalid geometry cannot leave stale candidates");

        NewWorld(); world = TerrainGenerator.Active.World;
        var lineA = Rail(1, new Vector2(-10, 0), new Vector2(0, 0));
        var lineB = Rail(2, new Vector2(.3f, 0), new Vector2(10, 0));
        var stationA = Station(11, new Vector2Int(-8, 0), "A");
        var stationB = Station(12, new Vector2Int(8, 0), "B");
        Check(SteamTrain.CanRoute(stationA, stationB), "Connected stations are actually reachable");
        Check(SteamTrain.RouteParity(stationA, stationB), "World route matches legacy all-pairs graph");
        int routeVersion = SteamTrain.GraphVersion;
        stationB.ApplyStationColor(new Color32(12, 34, 56, 255), true);
        Check(SteamTrain.GraphVersion == routeVersion, "Actual station color mutation does not rebuild routes");
        stationB.ApplyStationName("Renamed");
        Check(SteamTrain.GraphVersion > routeVersion && SteamTrain.HasStation("Renamed") && !SteamTrain.HasStation("B"), "Renaming refreshes station lookup");
        stationB.Disable(); Check(!SteamTrain.HasStation("Renamed"), "Disabled station removed from route registry");
        world.DetachLive(lineB); Check(SteamTrain.GraphVersion > routeVersion, "Unloading a rail invalidates live route graph");

        NewWorld(); world = TerrainGenerator.Active.World;
        var random = new System.Random(71);
        var randomRails = new List<Railload>();
        for (int i = 0; i < 120; i++)
        {
            float x = random.Next(-40, 40), y = random.Next(-40, 40);
            var candidate = Rail(i + 1, new Vector2(x, y), new Vector2(x + random.Next(-12, 13), y + random.Next(-12, 13)));
            candidate.Configure(new[] { new Vector2(x, y), new Vector2(x + random.Next(1, 13), y) }, i % 2 == 0, i % 3 == 0);
            world.UpsertLive(candidate); randomRails.Add(candidate);
        }
        Check(SteamTrain.CandidateParity(), "Spatial candidates cover all actual route connections");
        var expected = new int[randomRails.Count];
        for (int i = 0; i < expected.Length; i++) expected[i] = i;
        int Root(int index) { while (expected[index] != index) index = expected[index]; return index; }
        for (int i = 0; i < randomRails.Count; i++)
            for (int j = i + 1; j < randomRails.Count; j++)
            {
                randomRails[i].TryGetPathData(out var a); randomRails[j].TryGetPathData(out var b);
                if (RailConnectionUtility.AreConnected(a.Coordinates, a.SourcePoints, a.ConnectionStart, a.ConnectionEnd,
                    b.Coordinates, b.SourcePoints, b.ConnectionStart, b.ConnectionEnd, .55f * .55f)) expected[Root(j)] = Root(i);
            }
        for (int i = 0; i < randomRails.Count; i++)
            for (int j = i + 1; j < randomRails.Count; j++)
                Check((world.GetLiveComponent(randomRails[i], .55f) == world.GetLiveComponent(randomRails[j], .55f))
                    == (Root(i) == Root(j)), "Indexed components match brute-force connection rules");
        Check(SteamTrain.RouteParity(Station(201, new Vector2Int(-30, 0), "A"), Station(202, new Vector2Int(30, 0), "B")), "Disconnected/random route parity");

        NewWorld(); world = TerrainGenerator.Active.World;
        const int railCount = 10000;
        for (int i = 0; i < railCount; i++) Rail(i + 1, new Vector2(i * 24, 0), new Vector2(i * 24 + 3, 0));
        world.FindComponentAtPoint(Vector2.zero);
        Check(world.ConnectionChecks < railCount * 10L, "Sparse network avoids quadratic pair checks");
        builds = world.ComponentBuilds;
        for (int i = 0; i < 100; i++) world.FindComponentAtPoint(new Vector2(24, 0));
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) world.FindComponentAtPoint(new Vector2(24, 0));
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(bytes == 0, "Warm topology queries allocate zero managed bytes");
        Check(world.ComponentBuilds == builds, "10,000 warm queries do not rebuild components");
        Console.WriteLine($"Sparse network: {railCount} rails, {world.ConnectionChecks} exact pair checks, {bytes} warm-query bytes");
        world.Clear(); Check(world.LiveRails.Count == 0 && world.FindComponentAtPoint(Vector2.zero) == -1, "World reset clears all indices");
        NewWorld(); world = TerrainGenerator.Active.World;
        Rail(1, new Vector2(-50000, -50000), new Vector2(50000, 50000));
        Check(world.LiveRails[0].Cells.Count < 40000, "Long diagonal spatial coverage grows with length, not bounding-box area");
        Check(world.FindComponentAtPoint(new Vector2(3200, 3200)) >= 0, "Long segment interior remains discoverable");
        Console.WriteLine($"Rail world harness passed: {checks} checks");
    }
}

public partial class SteamTrain
{
    public static int GraphVersion => AutoDriveRoutePlanner.RouteGraphVersion;
    public static bool CanRoute(ITrainStationTarget a, ITrainStationTarget b)
        => AutoDriveRoutePlanner.TryBuildRoute(a, b, new List<AutoDriveRoutePlanner.RouteSegment>());
    public static bool HasStation(string name) => AutoDriveRoutePlanner.TryFindStationByName(name, out _);
    public static bool CandidateParity() => AutoDriveRoutePlanner.CandidateParity();
    public static bool RouteParity(Trainstation a, Trainstation b) => AutoDriveRoutePlanner.RouteParity(a, b);
    private static partial class AutoDriveRoutePlanner
    {
        public static bool CandidateParity()
        {
            EnsureRouteCache();
            var world = TerrainGenerator.Active.World;
            foreach (var record in world.LiveRails)
            {
                world.CollectCandidates(record, RouteRailConnectionSnapDistance, ConnectionCandidates, true);
                if (!CachedRailIndices.TryGetValue(record.View, out int left)) continue;
                for (int right = left + 1; right < CachedRails.Count; right++)
                    if (TryResolveRouteConnectionBetweenRails(CachedRails, left, right,
                        RouteRailConnectionSnapDistance * RouteRailConnectionSnapDistance, out _)
                        && !ConnectionCandidates.Exists(r => r.View == CachedRails[right].Rail)) return false;
            }
            return true;
        }
        public static bool RouteParity(Trainstation a, Trainstation b)
        {
            var near = new List<RouteSegment>();
            bool nearFound = TryBuildRoute(a, b, near);
            CachedBaseGraphNodes.Clear(); CachedBaseRailRefsByRail.Clear();
            for (int left = 0; left < CachedRails.Count; left++)
                for (int right = left + 1; right < CachedRails.Count; right++)
                    if (TryResolveRouteConnectionBetweenRails(CachedRails, left, right,
                        RouteRailConnectionSnapDistance * RouteRailConnectionSnapDistance, out var connection))
                    {
                        int node = GetOrCreateRouteGraphNode(CachedBaseGraphNodes, connection.Point);
                        AddRouteGraphNodeRef(CachedBaseGraphNodes, CachedBaseRailRefsByRail, node, connection.LeftRailIndex, connection.LeftDistanceAlongPath);
                        AddRouteGraphNodeRef(CachedBaseGraphNodes, CachedBaseRailRefsByRail, node, connection.RightRailIndex, connection.RightDistanceAlongPath);
                    }
            var legacy = new List<RouteSegment>();
            bool legacyFound = TryBuildRoute(a, b, legacy);
            if (nearFound != legacyFound || near.Count != legacy.Count) return false;
            for (int i = 0; i < near.Count; i++)
                if (near[i].Rail != legacy[i].Rail || Math.Abs(near[i].StartDistance - legacy[i].StartDistance) > .0001f
                    || Math.Abs(near[i].EndDistance - legacy[i].EndDistance) > .0001f) return false;
            return true;
        }
    }
}
