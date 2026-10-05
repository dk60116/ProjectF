using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;

public static class MiningSchedulerHost
{
    public const bool IsPlaying = true;
    public static IMapObjectUpdateTick Target;
    public static void Step(long tick)
    {
        MapObjectTickManager.CurrentSimulationTick = tick;
        Target?.ManagedUpdateTick(1f / 60);
    }
    public static void Reset()
    {
        typeof(FacilitySchedulerProbe).GetMethod("ResetStaticState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
        MapObjectTickManager.CurrentSimulationTick = 0;
        UtilityPole.PowerPreparationCount = 0;
    }
}
public static class FacilitySimulationWorld
{
    public static readonly HashSet<IMapObjectUpdateTick> Scheduled = new(); // Gauge boundary compatibility only.
    public static void SetScheduled(IMapObjectUpdateTick target, bool value) => FacilitySchedulerProbe.SetScheduled(target, value);
    public static bool IsScheduled(IMapObjectUpdateTick target) => FacilitySchedulerProbe.IsScheduled(target);
    public static void RefreshSchedule(IMapObjectUpdateTick target) => FacilitySchedulerProbe.RefreshSchedule(target);
    public static void Unregister(IMapObjectUpdateTick target) => FacilitySchedulerProbe.Unregister(target);
}
public class InstallationObject { }
public partial class InputOutputModule
{
    public bool RequiresFacilityPowerEvaluation => false;
    public bool IsBenchmarkWorking => ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
}
public static partial class MapObjectTickManager
{
    public const float FixedSimulationDeltaSeconds = 1f / 60;
    public static void RegisterUpdateTick(IMapObjectUpdateTick target) => MiningSchedulerHost.Target = target;
    public static void UnregisterUpdateTick(IMapObjectUpdateTick target) => MiningSchedulerHost.Target = null;
}
public static partial class UtilityPole
{
    public static int PowerPreparationCount;
    public static bool TracksRuntimeElectricPowerDemand(InstallationObject value) => false;
    public static bool TryCaptureElectricPowerDemand(InstallationObject value, out float watts) { watts = 0; return false; }
    public static bool HasElectricPowerDemandChanged(bool a, float aw, bool b, float bw) => a != b || aw != bw;
    public static void NotifyElectricPowerConsumerStateChanged(InstallationObject value) { }
    public static void PrepareSimulationPowerTick() => PowerPreparationCount++;
    public static void BeginSimulationPowerMutationBatch() { }
    public static void EndSimulationPowerMutationBatch() { }
}
public static partial class MapObjectTickProfiler
{
    public static bool IsDetailedEnabled => false;
    public static long BeginSample() => 0;
    public static Scope SampleNamed(string a, string b, string c) => default;
    public static void AddRuntimeCounter(string a, string b, object c) { }
    public static void RecordNamedElapsedTicks(string a, string b, string c, long ticks) { }
}
public partial class MiningWorld
{
    private State[] massStates;
    public MiningWorld() { }
    public MiningWorld(int count) { massStates = new State[count]; }
}
