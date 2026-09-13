using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TerrainGenerator
{
    public int Items = 10, Installations = 5, CensusCalls, InstallationQueries;
    public int GetConveyorItemCount() => Items;
    public int GetInstallationItemCounts(Dictionary<int, int> counts)
    {
        InstallationQueries++;
        counts.Add(1, Installations);
        return Installations;
    }
    public void CaptureRuntimeProfilerCensus() => CensusCalls++;
}

public partial class RuntimeItemGiveReceiver
{
    private TerrainGenerator cachedCensusTerrain;
    private float cachedStatusWorldStatsTime = float.NegativeInfinity;
    private int cachedInstalledObjectTotal = -1, cachedConveyorItemTotal;
    private int cachedSceneGameObjectTotal = -1, cachedActiveSceneGameObjectTotal = -1;
    private int cachedSceneMonoBehaviourTotal = -1, cachedActiveSceneMonoBehaviourTotal = -1;
    private string cachedInstallationTypeCounts = "-";
    private readonly Dictionary<int, int> installationCountsByItemId = new();
    public int SceneScans;
    private string BuildInstallationTypeCountToken(Dictionary<int, int> counts) => counts.Count == 0 ? "-" : counts[1].ToString();
    private void CaptureSceneObjectCounts(out int go, out int activeGo, out int mb, out int activeMb)
    {
        SceneScans++;
        go = 20000; activeGo = 10000; mb = 48000; activeMb = 12000;
    }
    public (int Installations, int Items, int Go, int Mb) Poll(TerrainGenerator terrain, bool refresh = false)
    {
        CaptureWorldStats(terrain, refresh, out int installations, out int items, out _, out int go, out _, out int mb, out _);
        return (installations, items, go, mb);
    }
}

public static class WorldStatsChecks
{
    public static void Run()
    {
        var receiver = new RuntimeItemGiveReceiver();
        var terrain = new TerrainGenerator();
        var cold = receiver.Poll(terrain);
        Require(cold.Go == -1 && cold.Mb == -1 && cold.Items == 10, "cold counts must be unavailable, live belt items must remain visible");
        for (int i = 0; i < 100; i++) { Time.unscaledTime += 1; receiver.Poll(terrain); }
        Require(receiver.SceneScans == 0 && terrain.CensusCalls == 0 && terrain.InstallationQueries == 0, "status triggered a census");
        var refreshed = receiver.Poll(terrain, true);
        Require(refreshed == (5, 10, 20000, 48000), "explicit counts differ");
        terrain.Items = 12; terrain.Installations = 6;
        for (int i = 0; i < 100; i++) receiver.Poll(terrain);
        var cached = receiver.Poll(terrain);
        Require(cached.Installations == 5 && cached.Items == 12, "cached census and live belt items mixed");
        Require(receiver.SceneScans == 1 && terrain.CensusCalls == 1 && terrain.InstallationQueries == 1, "recurring poll repeated a census");
        Require(receiver.Poll(terrain, true).Installations == 6, "manual refresh stayed stale");
        var replacement = new TerrainGenerator();
        var switched = receiver.Poll(replacement);
        Require(switched.Go == -1 && switched.Installations == -1 && replacement.CensusCalls == 0, "different world reused old census");
        Require(receiver.Poll(null).Items == 0, "no-world belt count stayed stale");
        Console.WriteLine("PASS: cold/recurring status performs no scene or world census; explicit refresh, live belt totals and world invalidation.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
