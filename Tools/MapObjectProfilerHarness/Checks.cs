using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

public interface IMapObjectUpdateTick { void ManagedUpdateTick(float deltaTime); }
public static class ProfilerClock
{
    public const long Frequency = 1000000;
    public static long Now;
    public static long GetTimestamp() => Now;
}
public sealed class GameManager
{
    public static GameManager Instance = new GameManager();
    public bool MapObjectTickProfilingEnabled = true;
    public ItemManager ItemManger = new ItemManager();
}
public sealed class ItemManager
{
    public sealed class ItemSet { public string name; }
    public bool TryGetItemSetById(int id, out ItemSet item)
    {
        item = id == 1 ? new ItemSet { name = "Belt \"A\"\nrow\tend" } : null;
        return item != null;
    }
}
public class PropObj : IMapObjectUpdateTick
{
    public int ItemId;
    public int ResolveItemId() => ItemId;
    public void ManagedUpdateTick(float deltaTime) { throw new Exception("Profiling must not tick objects."); }
}
public sealed class Belt : PropObj { }
public sealed class Facility : PropObj { }
public sealed class NonItemTick : IMapObjectUpdateTick
{
    public void ManagedUpdateTick(float deltaTime) { throw new Exception("Profiling must not tick objects."); }
}
namespace UnityEngine
{
    public static class Time { public static int frameCount; public static float unscaledTime; }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
    }
}

public static class Checks
{
    private static readonly List<string> snapshots = new List<string>();
    private static void Snapshot(int rows = 64)
    {
        Time.frameCount++;
        Time.unscaledTime += 0.5f;
        string json = MapObjectTickProfiler.BuildAndResetSnapshotJson(rows);
        using (JsonDocument.Parse(json)) { }
        snapshots.Add(json);
    }
    private static void Named(string kind, string type, string name, long duration)
    {
        long start = MapObjectTickProfiler.BeginSample();
        ProfilerClock.Now += duration;
        MapObjectTickProfiler.EndNamedSample(kind, type, name, start);
    }
    private static void Sample(object target, long duration)
    {
        long start = MapObjectTickProfiler.BeginSample();
        ProfilerClock.Now += duration;
        MapObjectTickProfiler.EndUpdateSample(target, start);
    }
    public static int Main(string[] args)
    {
        GameManager.Instance.MapObjectTickProfilingEnabled = false;
        MapObjectTickProfiler.Reset();
        Snapshot();
        GameManager.Instance.MapObjectTickProfilingEnabled = true;
        MapObjectTickProfiler.Reset();
        var targets = new IMapObjectUpdateTick[]
        {
            new Belt { ItemId = 1 }, new Belt { ItemId = 1 },
            new Belt { ItemId = 2 }, new Facility { ItemId = -1 }, new NonItemTick()
        };
        var active = new HashSet<IMapObjectUpdateTick>(targets);
        MapObjectTickProfiler.SetActiveUpdateTargets(active);
        Snapshot(); // Active groups must survive a window without samples.
        Sample(targets[0], 100);
        Sample(targets[1], 200);
        Sample(targets[2], 30);
        Sample(targets[3], 0);
        Sample(null, 400);
        Named(null, " ", null, 50);
        Named("Render", "Renderer", "Items", 25);
        Named(new string("Render".ToCharArray()), new string("Renderer".ToCharArray()), new string("Items".ToCharArray()), 35);
        MapObjectTickProfiler.AddRuntimeCounter("", "", 2);
        MapObjectTickProfiler.AddRuntimeCounter("Test", "float", 1.25f, "line\rnext");
        MapObjectTickProfiler.AddRuntimeCounter("Test", "long", 9876543210L);
        MapObjectTickProfiler.AddRuntimeCounter("Test", "bool", true);
        Snapshot();
        Snapshot();
        active.Remove(targets[0]);
        active.Remove(targets[2]);
        MapObjectTickProfiler.SetActiveUpdateTargets(active);
        Sample(targets[2], 90); // Recently removed target still has a completed sample.
        Sample(targets[1], 200);
        Snapshot(2);
        MapObjectTickProfiler.SetActiveUpdateTargets(new List<IMapObjectUpdateTick> { targets[4], null, targets[4] });
        Snapshot();
        MapObjectTickProfiler.SetActiveUpdateTargets(null);
        Snapshot();
        MapObjectTickProfiler.SetActiveTickCount(-3);
        Snapshot();
        MapObjectTickProfiler.SetActiveTickCount(12);
        Snapshot(0);

        var random = new Random(731);
        for (int iteration = 0; iteration < 120; iteration++)
        {
            if (iteration % 11 == 0) MapObjectTickProfiler.Reset();
            GameManager.Instance.MapObjectTickProfilingEnabled = iteration % 9 != 0;
            active.Clear();
            for (int i = 0; i < targets.Length; i++) if (random.Next(2) == 0) active.Add(targets[i]);
            MapObjectTickProfiler.SetActiveUpdateTargets(active);
            for (int frame = 0; frame < 3; frame++)
            {
                Time.frameCount++;
                MapObjectTickProfiler.SetBeltTickCounts(random.Next(-2, 20), random.Next(-2, 20), random.Next(-2, 20));
                MapObjectTickProfiler.SetBeltTickCounts(10, 9, 8); // Same frame counted once.
                MapObjectTickProfiler.AddBeltLoopIterations(random.Next(-2, 15), 5, 7, 8);
                MapObjectTickProfiler.AddBeltTryMoveAttempt(random.Next(2) == 0);
                MapObjectTickProfiler.AddBeltStraightMoveAttempt(random.Next(2) == 0);
                MapObjectTickProfiler.AddBeltPlanMoveCall();
                MapObjectTickProfiler.AddBeltPlannedMoveApplication(random.Next(-2, 4), 6);
                MapObjectTickProfiler.AddBeltWakeAroundCall();
                MapObjectTickProfiler.AddBeltActivityRefreshCall();
                for (int sample = 0; sample < 20; sample++)
                {
                    int targetIndex = random.Next(targets.Length);
                    Sample(targets[targetIndex], random.Next(100));
                    Named("Loop", "TerrainGenerator", "Stage" + random.Next(6), random.Next(100));
                }
            }
            MapObjectTickProfiler.AddRuntimeCounter("Test", "iteration", iteration);
            Snapshot(iteration % 7 == 0 ? -1 : 64);
        }
        if (args.Length > 1 && args[1] == "--capture")
        {
            File.WriteAllText(args[0], JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Captured {snapshots.Count} baseline snapshots.");
            return 0;
        }
        List<string> expected = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(args[0]));
        if (expected.Count != snapshots.Count) throw new Exception("Snapshot count differs.");
        for (int i = 0; i < snapshots.Count; i++)
        {
            if (expected[i] != snapshots[i]) throw new Exception($"Snapshot {i} differs. Expected:\n{expected[i]}\nActual:\n{snapshots[i]}");
        }
        GameManager.Instance.MapObjectTickProfilingEnabled = true;
        MapObjectTickProfiler.Reset();
        for (int i = 0; i < targets.Length; i++) active.Add(targets[i]);
        for (int i = 0; i < 100; i++) MapObjectTickProfiler.SetActiveUpdateTargets(active);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) MapObjectTickProfiler.SetActiveUpdateTargets(active);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (allocated != 0) throw new Exception($"Steady active-group collection allocated {allocated} bytes.");
        Console.WriteLine($"PASS: {snapshots.Count} byte-identical baseline snapshots; 1000 active-group refreshes allocated {allocated} bytes.");
        return 0;
    }
}
