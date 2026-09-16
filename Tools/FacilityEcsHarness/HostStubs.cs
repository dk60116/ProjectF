using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object { }
    public static class Application { public static bool isPlaying = true; }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
    public readonly record struct Vector2Int(int x, int y);
}

namespace ProjectF.Simulation
{
    public static class SimulationTickWorld
    {
        public const int DefaultSimulationTicksPerSecond = 60;
        public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
    }
}

public static class MapObjectTickManager
{
    public const float FixedSimulationDeltaSeconds = 1f / 60f;
    private static readonly HashSet<IMapObjectUpdateTick> Targets = new();
    private static readonly List<IMapObjectUpdateTick> Snapshot = new();
    public static long CurrentSimulationTick { get; private set; }
    public static int RegisteredCount => Targets.Count;
    public static void RegisterUpdateTick(IMapObjectUpdateTick target) => Targets.Add(target);
    public static void UnregisterUpdateTick(IMapObjectUpdateTick target) => Targets.Remove(target);
    public static void Restore(long tick)
    {
        CurrentSimulationTick = Math.Max(0L, tick);
        FacilitySimulationWorld.RestoreSimulationTick(CurrentSimulationTick);
    }
    public static void Step()
    {
        CurrentSimulationTick++;
        Snapshot.Clear();
        Snapshot.AddRange(Targets);
        for (int i = 0; i < Snapshot.Count; i++)
            if (Snapshot[i] is IMapObjectStagedUpdateTick staged) staged.PlanManagedUpdateTick(FixedSimulationDeltaSeconds);
            else Snapshot[i].ManagedUpdateTick(FixedSimulationDeltaSeconds);
        for (int i = 0; i < Snapshot.Count; i++)
            if (Snapshot[i] is IMapObjectStagedUpdateTick staged && Targets.Contains(Snapshot[i])) staged.ApplyManagedUpdateTick();
        Snapshot.Clear();
    }
}

public static class UtilityPole
{
    public static int PrepareCalls;
    public static int MutationBatchDepth;
    public static void PrepareSimulationPowerTick() => PrepareCalls++;
    public static void BeginSimulationPowerMutationBatch() => MutationBatchDepth++;
    public static void EndSimulationPowerMutationBatch() => MutationBatchDepth--;
}

public static class MapObjectTickProfiler
{
    public static readonly Dictionary<string, long> RuntimeCounters = new();
    public static bool IsEnabled => false;
    public static bool IsDetailedEnabled => IsEnabled;
    public static long BeginSample() => 0L;
    public static Scope SampleNamed(string kind, string type, string name) => default;
    public static void AddRuntimeCounter(string group, string name, object value)
        => RuntimeCounters[group + "/" + name] = Convert.ToInt64(value);
    public static void RecordNamedElapsedTicks(string kind, string type, string name, long elapsed) { }
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}

public abstract class InputOutputModule : IMapObjectUpdateTick
{
    public static int CoordinateWakeCalls;
    public static void WakeRuntimeModulesAtCoordinate(UnityEngine.Vector2Int coordinate)
        => CoordinateWakeCalls++;
    public bool RequiresFacilityPowerEvaluation => true;
    public abstract void ManagedUpdateTick(float deltaTime);
}
