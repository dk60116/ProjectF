using System;
using ProjectF.Animals;
using UnityEngine;

public static partial class Checks
{
    private static void NavigationChecks()
    {
        MapObjectTickProfiler.IsEnabled = true;
        AnimalAIProfiler.Reset();
        long actualNodeStart = AnimalGridPathfinder.ExpandedNodeCount;
        var terrain = new TerrainGenerator();
        var cold = new Vector3[96]; var warm = new Vector3[96];
        var origin = Vector3.zero;
        long nodes = AnimalGridPathfinder.ExpandedNodeCount;
        long work = AnimalGridPathfinder.SchedulingWorkCount;
        int coldCount = AnimalGridPathfinder.FindReachableTargetPath(terrain, origin, origin, 30, true, false, 2, 127, cold, out var coldTarget);
        long coldNodes = AnimalGridPathfinder.ExpandedNodeCount - nodes;
        long coldWork = AnimalGridPathfinder.SchedulingWorkCount - work;
        nodes = AnimalGridPathfinder.ExpandedNodeCount; work = AnimalGridPathfinder.SchedulingWorkCount;
        int warmCount = AnimalGridPathfinder.FindReachableTargetPath(terrain, origin, origin, 30, false, false, 2, 127, warm, out var warmTarget);
        long warmNodes = AnimalGridPathfinder.ExpandedNodeCount - nodes;
        Require(coldCount > 0 && warmCount == coldCount && coldTarget.Equals(warmTarget)
            && coldWork == AnimalGridPathfinder.SchedulingWorkCount - work, "cold/warm caches and view residency preserve target, path length, and scheduling charge");
        bool identical = true;
        for (int i = 0; i < coldCount; i++) identical &= cold[i].Equals(warm[i]);
        Require(identical && warmNodes * 5 < coldNodes, "cached connected area removes repeated full BFS work without changing the path");

        for (int z = -31; z <= 31; z++) terrain.Walls.Add(new(1, z));
        terrain.AnimalNavigationRevision++;
        bool inside = true;
        for (uint seed = 0; seed < 80; seed++)
        {
            int count = AnimalGridPathfinder.FindReachableTargetPath(terrain, origin, origin, 30, true, false, 1, seed, warm, out var target);
            inside &= count > 0 && target.x < 1;
        }
        Require(inside, "topology invalidation keeps every roaming target in the animal's connected component");
        terrain.Shores.Add(new(-5, 0)); terrain.Shores.Add(new(2, 0)); terrain.AnimalNavigationRevision++;
        AnimalGridPathfinder.FindReachableTargetPath(terrain, origin, origin, 30, false, true, 0, 15, warm, out var shore);
        Require(shore.Equals(new Vector3(-5, 0, 0)), "nearest shoreline excludes water behind a closed partition");
        terrain.Walls.Remove(new(1, 0)); terrain.AnimalNavigationRevision++;
        AnimalGridPathfinder.FindReachableTargetPath(terrain, origin, origin, 30, false, true, 0, 15, warm, out shore);
        Require(shore.Equals(new Vector3(2, 0, 0)), "opening a door rebuilds the region and nearest-shore route");
        AnimalAIProfiler.CompleteFrame(); AnimalAIProfiler.AppendCounters();
        Require(MapObjectTickProfiler.Counters["PathNodes"] == AnimalGridPathfinder.ExpandedNodeCount - actualNodeStart,
            "nested reachable/AStar queries count actual node work once");
        Require(MapObjectTickProfiler.Counters["RegionCacheHits"] >= 80 && MapObjectTickProfiler.Counters["RegionCacheBuilds"] == 4,
            "profiler distinguishes region rebuilds from hits");

        MapObjectTickProfiler.IsEnabled = false;
        for (int i = 0; i < 30; i++) AnimalGridPathfinder.FindReachableTargetPath(terrain, origin, origin, 30, false, true, 0, 15, warm, out _);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++) AnimalGridPathfinder.FindReachableTargetPath(terrain, origin, origin, 30, false, true, 0, 15, warm, out _);
        Require(GC.GetAllocatedBytesForCurrentThread() == bytes, "warm navigation queries allocate zero managed bytes");

        AnimalTickTimer timer = 1f;
        for (int i = 0; i < 60; i++) timer -= 1f / 60;
        Require(timer.Ticks == 0, "integer behavior timer reaches exactly zero after sixty fixed ticks");
        AnimalTickTimer batched = 1f;
        for (int i = 0; i < 15; i++) batched -= 4f / 60;
        Require(batched.Ticks == timer.Ticks, "timer completion is independent of decision batching");
        var stored = new AnimalFixedPosition(new Vector3(-12.234567f, 0, 6.333333f));
        Require(new AnimalFixedPosition(stored.Value).X == stored.X && new AnimalFixedPosition(stored.Value).Z == stored.Z,
            "integer coordinates survive the Unity vector boundary without drift");
        bool anglesCorrect = true;
        for (int degrees = 0; degrees < 360; degrees += 3)
        {
            int angle = AnimalSimulationMath.Angle(degrees);
            anglesCorrect &= Math.Abs(AnimalSimulationMath.AngleDelta(angle, AnimalSimulationMath.Yaw(AnimalSimulationMath.Direction(angle)))) < 20;
        }
        Require(anglesCorrect, "integer heading/direction round trip stays within one tenth degree");
        int yaw = 0;
        for (int i = 0; i < 60; i++) yaw = AnimalSimulationMath.Turn(yaw, new Vector3(1, 0, 0), 90, 1f / 60);
        Require(Math.Abs(AnimalSimulationMath.AngleDelta(yaw, AnimalSimulationMath.Angle(90))) < 60, "fixed-tick turning advances at the authored angular speed");
        bool disks = true;
        for (uint i = 1; i < 10000; i++)
        {
            Vector3 v = AnimalSimulationMath.RandomDisk(i * 1103515245u, i * 2654435761u, 5);
            disks &= v.sqrMagnitude <= 25.01f;
        }
        Require(disks, "integer random target generation stays inside the authored roaming disk");
        Func<int, int, bool> obstacle = (x, z) => x != 0 || z != 0;
        Require(!AnimalSimulationMath.IsGridPositionClear(new(-2, 0, 0), new(-.6f, 0, 0), .3f, true, obstacle),
            "integer body clearance blocks entering a solid occupied cell");
        Require(AnimalSimulationMath.IsGridPositionClear(new(.2f, 0, 0), new(.3f, 0, 0), .3f, true, obstacle)
            && !AnimalSimulationMath.IsGridPositionClear(new(.2f, 0, 0), new(.1f, 0, 0), .3f, true, obstacle),
            "an overlapped animal can move outward but cannot burrow farther into an obstacle");
        Require(AnimalSimulationMath.IsGridPositionClear(Vector3.zero, new(.1f, 0, 0), .3f, true, obstacle),
            "an obstacle placed exactly over the animal does not prevent its first escape step");
        Require(AnimalSimulationMath.Cell(new(-.5f, 0, 1.5f)).Equals(new Vector2Int(0, 2)), "cell rounding specifies both half ties and negative coordinates");
        MapObjectTickProfiler.IsEnabled = true;
    }
}
