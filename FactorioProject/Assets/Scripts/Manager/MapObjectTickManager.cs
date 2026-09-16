using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

public static class ProjectFApplicationLifecycle
{
    public static bool IsQuitting { get; private set; }

#if UNITY_5_3_OR_NEWER
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        IsQuitting = false;
        Application.quitting -= MarkQuitting;
        Application.quitting += MarkQuitting;
    }

    private static void MarkQuitting()
    {
        IsQuitting = true;
    }
#endif
}

public sealed class MapObjectTickManager : MonoBehaviour, ProjectF.Simulation.ISimulationTickObserver
{
    public const int DefaultSimulationTicksPerSecond = ProjectF.Simulation.SimulationTickWorld.DefaultSimulationTicksPerSecond;
    public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
    private const float DefaultUpdateTickIntervalSeconds = FixedSimulationDeltaSeconds;
    private const int AliveValidationTickInterval = 120;
    private const int DefaultMaximumSimulationStepsPerFrame = 8;
    private const double SimulationUpsSampleIntervalSeconds = 0.5d;

    private static MapObjectTickManager instance;
    private static readonly HashSet<IMapObjectUpdateTick> requestedUpdateTicks =
        new HashSet<IMapObjectUpdateTick>();

    [SerializeField, Min(0.001f)]
    private float updateTickIntervalSeconds = DefaultUpdateTickIntervalSeconds;
    [SerializeField, Range(1, 64)]
    private int maximumSimulationStepsPerFrame = DefaultMaximumSimulationStepsPerFrame;

    private readonly ProjectF.Simulation.SimulationTickWorld simulation = new ProjectF.Simulation.SimulationTickWorld();
    private readonly List<IMapObjectUpdateTick> requestedTickCleanupBuffer = new List<IMapObjectUpdateTick>();
    private long simulationTick => simulation.CurrentTick;
    private long nextAliveValidationTick;
    private double simulationTimeAccumulator;
    private double simulationUpsSampleStartTime;
    private long simulationUpsSampleStartTick;
    private float currentSimulationUps;
    private int simulationTicksLastFrame;
    private bool hasSimulationUpsSample;
    private bool simulationPaused;
    private int saveTickPauseDepth;
    private bool waitingForWorldLoad;
    private float resumeTimeScale = 1f;

    public static long CurrentSimulationTick => instance != null ? instance.simulationTick : 0L;
    public static bool CanCaptureCheckpoint => instance == null || instance.simulation.CanCaptureCheckpoint;
    public static double CurrentSimulationTimeSeconds =>
        CurrentSimulationTick * (double)FixedSimulationDeltaSeconds;
    public static double SimulationBacklogTicks => instance != null
        ? Math.Max(0d, instance.simulationTimeAccumulator / FixedSimulationDeltaSeconds)
        : 0d;
    public static float SimulationInterpolationAlpha => (float)Math.Min(
        1d,
        SimulationBacklogTicks);
    public static bool HasSimulationUpsSample => instance != null && instance.hasSimulationUpsSample;
    public static float CurrentSimulationUps => instance != null ? instance.currentSimulationUps : 0f;
    public static bool SimulationPaused => instance != null && instance.IsSimulationTickPaused;
    public static bool SaveSnapshotCapturePaused => instance != null && instance.saveTickPauseDepth > 0;
    public static bool WaitingForWorldLoad
    {
        get
        {
            TerrainGenerator terrain = TerrainGenerator.Active;
            // Only the initial/restored world readiness boundary stops deterministic
            // simulation. Runtime chunk streaming is frame-budgeted and must run
            // alongside ticks while the player moves through an already ready world.
            return terrain != null && !terrain.IsWorldReadyForPresentation;
        }
    }
    public static float TargetSimulationUps => SimulationPaused || WaitingForWorldLoad
        ? 0f
        : DefaultSimulationTicksPerSecond * Mathf.Max(0f, Time.timeScale);
    public static int SimulationTicksLastFrame => instance != null ? instance.simulationTicksLastFrame : 0;
    public static int MaximumSimulationStepsPerFrame => instance != null
        ? Mathf.Max(1, instance.maximumSimulationStepsPerFrame)
        : DefaultMaximumSimulationStepsPerFrame;

    public static void RegisterUpdateTick(IMapObjectUpdateTick tick)
    {
        if (tick == null || !Application.isPlaying || ProjectFApplicationLifecycle.IsQuitting)
        {
            return;
        }

        requestedUpdateTicks.Add(tick);
        MapObjectTickManager manager = EnsureInstance();
        manager.simulation.DefaultIntervalSeconds = manager.updateTickIntervalSeconds;
        manager.simulation.Register(tick);
    }

    public static void UnregisterUpdateTick(IMapObjectUpdateTick tick)
    {
        if (tick == null)
        {
            return;
        }

        requestedUpdateTicks.Remove(tick);
        if (instance != null)
        {
            instance.simulation.Unregister(tick);
        }
    }

    public static bool IsUpdateTickRegistered(IMapObjectUpdateTick tick)
    {
        return tick != null
               && requestedUpdateTicks.Contains(tick)
               && instance != null
               && instance.simulation.Contains(tick);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        requestedUpdateTicks.Clear();
    }

    private static MapObjectTickManager EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject host = new GameObject(nameof(MapObjectTickManager));
        DontDestroyOnLoad(host);
        instance = host.AddComponent<MapObjectTickManager>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        simulation.DefaultIntervalSeconds = updateTickIntervalSeconds;
        ResetSimulationUpsMeasurement();
        ReconcileRequestedUpdateTicks(true);
    }

    private void Update()
    {
        MapObjectTickProfiler.RecordRenderFrame();
        using var sample = MapObjectTickProfiler.SampleNamed("Simulation", "Simulation Frame", "Simulation Frame (inclusive)");
        // Streaming/finalization run in TerrainGenerator.Update and coroutines, not this clock.
        // Discard loading wall time, including the frame in which loading completes.
        bool worldLoading = WaitingForWorldLoad;
        if (worldLoading || waitingForWorldLoad)
        {
            waitingForWorldLoad = worldLoading;
            simulationTimeAccumulator = 0d;
            ResetSimulationUpsMeasurement();
            hasSimulationUpsSample = true;
            return;
        }

        if (IsSimulationTickPaused)
        {
            // A manual pause freezes Unity time. Save pause only freezes deterministic
            // simulation so rendering, UI, and input frames can continue.
            if (simulationPaused && Time.timeScale != 0f)
            {
                Time.timeScale = 0f;
            }

            simulationTicksLastFrame = 0;
            UpdateSimulationUpsMeasurement();
            return;
        }

        simulationTimeAccumulator += Math.Max(0d, Time.deltaTime);
        int maximumSteps = Mathf.Max(1, maximumSimulationStepsPerFrame);
        int completedSteps = 0;
        while (simulationTimeAccumulator + 0.000000001d >= FixedSimulationDeltaSeconds
               && completedSteps < maximumSteps
               && !WaitingForWorldLoad)
        {
            simulationTimeAccumulator -= FixedSimulationDeltaSeconds;
            bool fullValidationRequested = RequestPeriodicAliveValidation();
            ReconcileRequestedUpdateTicks(fullValidationRequested);
            simulation.Observer = MapObjectTickProfiler.IsDetailedEnabled ? this : null;
            simulation.Step();
            completedSteps++;
        }

        MapObjectTickProfiler.RecordSimulationTicks(completedSteps);
        simulationTicksLastFrame = completedSteps;
        UpdateSimulationUpsMeasurement();
    }

    public static void SetSimulationPaused(bool paused)
    {
        if (!Application.isPlaying || ProjectFApplicationLifecycle.IsQuitting)
        {
            return;
        }

        EnsureInstance().ApplySimulationPaused(paused);
    }

    public static void BeginSaveTickPause()
    {
        if (!Application.isPlaying || ProjectFApplicationLifecycle.IsQuitting)
        {
            return;
        }

        MapObjectTickManager manager = EnsureInstance();
        manager.saveTickPauseDepth++;
        manager.simulationTimeAccumulator = 0d;
        manager.currentSimulationUps = 0f;
        manager.simulationTicksLastFrame = 0;
        manager.hasSimulationUpsSample = true;
    }

    public static void EndSaveTickPause()
    {
        if (instance == null || instance.saveTickPauseDepth <= 0)
        {
            return;
        }

        instance.saveTickPauseDepth--;
        if (instance.saveTickPauseDepth == 0 && !instance.simulationPaused)
        {
            instance.simulationTimeAccumulator = 0d;
            instance.ResetSimulationUpsMeasurement();
        }
    }

    private bool IsSimulationTickPaused => simulationPaused || saveTickPauseDepth > 0;

    private void ApplySimulationPaused(bool paused)
    {
        if (simulationPaused == paused)
        {
            if (paused && Time.timeScale != 0f)
            {
                Time.timeScale = 0f;
            }

            return;
        }

        if (paused)
        {
            if (Time.timeScale > 0f)
            {
                resumeTimeScale = Time.timeScale;
            }

            simulationPaused = true;
            Time.timeScale = 0f;
            currentSimulationUps = 0f;
            simulationTicksLastFrame = 0;
            hasSimulationUpsSample = true;
        }
        else
        {
            simulationPaused = false;
            Time.timeScale = resumeTimeScale > 0f ? resumeTimeScale : 1f;
            ResetSimulationUpsMeasurement();
        }

        simulationUpsSampleStartTime = Time.realtimeSinceStartupAsDouble;
        simulationUpsSampleStartTick = simulationTick;
    }

    public static void RestoreSimulationTick(long restoredTick)
    {
        if (!Application.isPlaying || ProjectFApplicationLifecycle.IsQuitting)
        {
            return;
        }

        MapObjectTickManager manager = EnsureInstance();
        manager.simulation.RestoreTick(restoredTick);
        FacilitySimulationWorld.RestoreSimulationTick(manager.simulationTick);
        manager.simulationTimeAccumulator = 0d;
        manager.nextAliveValidationTick = manager.simulationTick;
        manager.ResetSimulationUpsMeasurement();
    }

    private void ResetSimulationUpsMeasurement()
    {
        simulationUpsSampleStartTime = Time.realtimeSinceStartupAsDouble;
        simulationUpsSampleStartTick = simulationTick;
        currentSimulationUps = 0f;
        simulationTicksLastFrame = 0;
        hasSimulationUpsSample = false;
    }

    private void UpdateSimulationUpsMeasurement()
    {
        double currentTime = Time.realtimeSinceStartupAsDouble;
        double elapsedSeconds = currentTime - simulationUpsSampleStartTime;
        if (elapsedSeconds < SimulationUpsSampleIntervalSeconds)
        {
            return;
        }

        long completedTicks = Math.Max(0L, simulationTick - simulationUpsSampleStartTick);
        currentSimulationUps = (float)(completedTicks / elapsedSeconds);
        simulationUpsSampleStartTime = currentTime;
        simulationUpsSampleStartTick = simulationTick;
        hasSimulationUpsSample = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            if (simulationPaused)
            {
                Time.timeScale = resumeTimeScale > 0f ? resumeTimeScale : 1f;
            }

            simulation.Dispose();
            instance = null;
        }
    }

    private bool RequestPeriodicAliveValidation()
    {
        if (simulationTick < nextAliveValidationTick) return false;
        nextAliveValidationTick = simulationTick + AliveValidationTickInterval;
        return true;
    }

    private void ReconcileRequestedUpdateTicks(bool forceFullValidation)
    {
        if (!forceFullValidation && requestedUpdateTicks.Count == simulation.RegisteredCount) return;
        requestedTickCleanupBuffer.Clear();
        foreach (IMapObjectUpdateTick tick in requestedUpdateTicks)
        {
            if (!IsTickAlive(tick)) requestedTickCleanupBuffer.Add(tick);
            else if (!simulation.Contains(tick)) simulation.Register(tick);
        }
        foreach (IMapObjectUpdateTick tick in requestedTickCleanupBuffer)
        {
            requestedUpdateTicks.Remove(tick);
            simulation.Unregister(tick);
        }
        requestedTickCleanupBuffer.Clear();
    }

    void ProjectF.Simulation.ISimulationTickObserver.OnActiveTargets(ICollection<IMapObjectUpdateTick> targets)
        => MapObjectTickProfiler.SetActiveUpdateTargets(targets);
    long ProjectF.Simulation.ISimulationTickObserver.BeginSample() => MapObjectTickProfiler.BeginSample();
    void ProjectF.Simulation.ISimulationTickObserver.EndSample(IMapObjectUpdateTick target, long started)
        => MapObjectTickProfiler.EndUpdateSample(target, started);

    private static bool IsTickAlive(object tick)
    {
        if (tick == null)
        {
            return false;
        }

        UnityEngine.Object unityObject = tick as UnityEngine.Object;
        return ReferenceEquals(unityObject, null) || unityObject != null;
    }

}

public readonly struct MapObjectRuntimeCounter
{
    public MapObjectRuntimeCounter(string group, string name, string value, string note = "")
    {
        Group = string.IsNullOrWhiteSpace(group) ? "Runtime" : group;
        Name = string.IsNullOrWhiteSpace(name) ? "Counter" : name;
        Value = string.IsNullOrWhiteSpace(value) ? "0" : value;
        Note = string.IsNullOrWhiteSpace(note) ? string.Empty : note;
    }

    public string Group { get; }
    public string Name { get; }
    public string Value { get; }
    public string Note { get; }
}

public static class MapObjectTickProfiler
{
    private const double MicrosecondsPerSecond = 1000000.0;
    private const int DefaultSnapshotMaxRows = 64;
    private const int FineDurationBucketCount = 256;
    private const int CoarseDurationBucketCount = 256;
    private const int DurationHistogramBucketCount = FineDurationBucketCount + CoarseDurationBucketCount;

    private static readonly Dictionary<ProfilerGroupKey, GroupStats> groupStatsByKey =
        new Dictionary<ProfilerGroupKey, GroupStats>(128);
    private static readonly Dictionary<ProfilerGroupKey, GroupStats> activeUpdateStatsByKey =
        new Dictionary<ProfilerGroupKey, GroupStats>(128);
    private static readonly Stack<GroupStats> unusedGroupStats = new Stack<GroupStats>(128);
    private static readonly Dictionary<object, ProfilerGroupKey> updateGroupKeyByObject =
        new Dictionary<object, ProfilerGroupKey>(512);
    private static readonly List<GroupStats> snapshotRows = new List<GroupStats>(128);
    private static readonly List<MapObjectRuntimeCounter> runtimeCounters = new List<MapObjectRuntimeCounter>(96);
    private static readonly StringBuilder jsonBuilder = new StringBuilder(8192);
    private static readonly double stopwatchTickToMicroseconds = MicrosecondsPerSecond / Stopwatch.Frequency;

    private static int activeUpdateTickCount;
    private static int activeBeltTickCount;
    private static int activeBeltDataMotionCount;
    private static int activeBeltVisualTickCount;
    private static long beltDataMotionLoopIterations;
    private static long beltActiveLoopIterations;
    private static long beltStraightLineBlockLoopIterations;
    private static long beltVisualLoopIterations;
    private static long beltTryMoveAttempts;
    private static long beltTryMoveSuccesses;
    private static long beltStraightMoveAttempts;
    private static long beltStraightMoveSuccesses;
    private static long beltPlanMoveCalls;
    private static long beltPlannedMoveApplications;
    private static long beltTouchedBlockRefreshes;
    private static long beltWakeAroundCalls;
    private static long beltActivityRefreshCalls;
    private static int renderFrameCount;
    private static int lastRenderFrame = -1;
    private static int completedSimulationTickCount;
    private static int beltLoopProfileFrameCount;
    private static int beltLoopProfileLastFrame = -1;
    private static bool beltFrameProfilingEnabled;
    private static float windowStartTime = -1f;

    public static bool IsEnabled
    {
        get
        {
            GameManager gameManager = GameManager.Instance;
            return gameManager != null && gameManager.MapObjectTickProfilingEnabled;
        }
    }

    public static void RecordRenderFrame()
    {
        if (!IsEnabled || lastRenderFrame == Time.frameCount) return;
        lastRenderFrame = Time.frameCount;
        renderFrameCount++;
    }

    public static bool IsDetailedEnabled => IsEnabled && GameManager.Instance.MapObjectTickDetailedProfilingEnabled;

    public static void RecordSimulationTicks(int completedTicks)
    {
        if (IsEnabled) completedSimulationTickCount += Mathf.Max(0, completedTicks);
    }

    public static long BeginSample()
    {
        return Stopwatch.GetTimestamp();
    }

    public static NamedSampleScope SampleNamed(string kind, string typeName, string itemName)
    {
        return new NamedSampleScope(kind, typeName, itemName);
    }

    // A value-type scope avoids closures/allocations and records early returns too.
    public readonly struct NamedSampleScope : IDisposable
    {
        private readonly bool enabled;
        private readonly long start;
        private readonly string kind;
        private readonly string typeName;
        private readonly string itemName;

        internal NamedSampleScope(string kind, string typeName, string itemName)
        {
            enabled = IsDetailedEnabled;
            start = enabled ? BeginSample() : 0L;
            this.kind = kind;
            this.typeName = typeName;
            this.itemName = itemName;
        }

        public void Dispose()
        {
            if (enabled)
            {
                EndNamedSample(kind, typeName, itemName, start);
            }
        }
    }

    public static void EndUpdateSample(object target, long startTimestamp)
    {
        if (target != null)
        {
            ProfilerGroupKey key = ResolveUpdateGroupKey(target);
            long elapsedTicks = Math.Max(0L, Stopwatch.GetTimestamp() - startTimestamp);
            RecordSample(key, elapsedTicks);
        }
    }

    public static void EndNamedSample(string kind, string typeName, string itemName, long startTimestamp)
    {
        // Finish timing before hashing the group key, as with object samples.
        long elapsedTicks = Math.Max(0L, Stopwatch.GetTimestamp() - startTimestamp);
        RecordNamedElapsedTicksInternal(kind, typeName, itemName, elapsedTicks);
    }

    public static void RecordNamedElapsedTicks(
        string kind,
        string typeName,
        string itemName,
        long elapsedTimestampTicks)
    {
        if (!IsDetailedEnabled) return;
        RecordNamedElapsedTicksInternal(kind, typeName, itemName, elapsedTimestampTicks);
    }

    private static void RecordNamedElapsedTicksInternal(
        string kind,
        string typeName,
        string itemName,
        long elapsedTimestampTicks)
    {
        string resolvedTypeName = string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName;
        string resolvedKind = string.IsNullOrWhiteSpace(kind) ? "Update" : kind;
        string resolvedItemName = string.IsNullOrWhiteSpace(itemName) ? resolvedTypeName : itemName;
        ProfilerGroupKey key = new ProfilerGroupKey(
            resolvedKind,
            resolvedTypeName,
            -1,
            resolvedItemName);
        RecordSample(key, Math.Max(0L, elapsedTimestampTicks));
    }

    public static void SetActiveUpdateTargets(ICollection<IMapObjectUpdateTick> updateTicks)
    {
        RecycleGroupStats(activeUpdateStatsByKey);
        activeUpdateTickCount = updateTicks != null ? Mathf.Max(0, updateTicks.Count) : 0;
        if (updateTicks == null || updateTicks.Count <= 0)
        {
            return;
        }

        // The manager supplies a HashSet. Keep its struct enumerator unboxed.
        if (updateTicks is HashSet<IMapObjectUpdateTick> tickSet)
        {
            foreach (IMapObjectUpdateTick tick in tickSet)
            {
                RecordActiveTarget(tick);
            }
        }
        else
        {
            foreach (IMapObjectUpdateTick tick in updateTicks)
            {
                RecordActiveTarget(tick);
            }
        }
    }

    public static void SetBeltTickCounts(int activeBelts, int dataMotionBelts, int visualBelts)
    {
        activeBeltTickCount = Mathf.Max(0, activeBelts);
        activeBeltDataMotionCount = Mathf.Max(0, dataMotionBelts);
        activeBeltVisualTickCount = Mathf.Max(0, visualBelts);

        bool enabled = IsDetailedEnabled;
        beltFrameProfilingEnabled = enabled;
        if (!enabled)
        {
            return;
        }

        int frame = Time.frameCount;
        if (frame != beltLoopProfileLastFrame)
        {
            beltLoopProfileLastFrame = frame;
            beltLoopProfileFrameCount++;
        }
    }

    public static void SetBeltProfilingFrameEnabled(bool enabled)
    {
        beltFrameProfilingEnabled = enabled && IsDetailedEnabled;
    }

    public static void AddBeltLoopIterations(
        int dataMotionLoops,
        int activeLoops,
        int straightLineBlockLoops,
        int visualLoops)
    {
        if (!IsEnabled)
        {
            return;
        }

        beltDataMotionLoopIterations += Mathf.Max(0, dataMotionLoops);
        beltActiveLoopIterations += Mathf.Max(0, activeLoops);
        beltStraightLineBlockLoopIterations += Mathf.Max(0, straightLineBlockLoops);
        beltVisualLoopIterations += Mathf.Max(0, visualLoops);
    }

    public static void AddBeltTryMoveAttempt(bool success)
    {
        if (!beltFrameProfilingEnabled)
        {
            return;
        }

        beltTryMoveAttempts++;
        if (success)
        {
            beltTryMoveSuccesses++;
        }
    }

    public static void AddBeltStraightMoveAttempt(bool success)
    {
        if (!beltFrameProfilingEnabled)
        {
            return;
        }

        beltStraightMoveAttempts++;
        if (success)
        {
            beltStraightMoveSuccesses++;
        }
    }

    public static void AddBeltPlanMoveCall()
    {
        if (!beltFrameProfilingEnabled)
        {
            return;
        }

        beltPlanMoveCalls++;
    }

    public static void AddBeltPlannedMoveApplication(int plannedMoveCount, int touchedBlockCount)
    {
        if (!beltFrameProfilingEnabled)
        {
            return;
        }

        beltPlannedMoveApplications += Mathf.Max(0, plannedMoveCount);
        beltTouchedBlockRefreshes += Mathf.Max(0, touchedBlockCount);
    }

    public static void AddBeltWakeAroundCall()
    {
        if (!beltFrameProfilingEnabled)
        {
            return;
        }

        beltWakeAroundCalls++;
    }

    public static void AddBeltActivityRefreshCall()
    {
        if (!beltFrameProfilingEnabled)
        {
            return;
        }

        beltActivityRefreshCalls++;
    }

    public static void ClearRuntimeCounters()
    {
        runtimeCounters.Clear();
    }

    public static void AddRuntimeCounter(string group, string name, int value, string note = "")
    {
        AddRuntimeCounter(group, name, value.ToString(CultureInfo.InvariantCulture), note);
    }

    public static void AddRuntimeCounter(string group, string name, long value, string note = "")
    {
        AddRuntimeCounter(group, name, value.ToString(CultureInfo.InvariantCulture), note);
    }

    public static void AddRuntimeCounter(string group, string name, bool value, string note = "")
    {
        AddRuntimeCounter(group, name, value ? "1" : "0", note);
    }

    public static void AddRuntimeCounter(string group, string name, float value, string note = "")
    {
        AddRuntimeCounter(group, name, value.ToString("0.###", CultureInfo.InvariantCulture), note);
    }

    public static void AddRuntimeCounter(string group, string name, string value, string note = "")
    {
        runtimeCounters.Add(new MapObjectRuntimeCounter(group, name, value, note));
    }

    public static void Reset()
    {
        groupStatsByKey.Clear();
        activeUpdateStatsByKey.Clear();
        unusedGroupStats.Clear();
        updateGroupKeyByObject.Clear();
        snapshotRows.Clear();
        runtimeCounters.Clear();
        jsonBuilder.Length = 0;
        activeUpdateTickCount = 0;
        activeBeltTickCount = 0;
        activeBeltDataMotionCount = 0;
        activeBeltVisualTickCount = 0;
        beltDataMotionLoopIterations = 0L;
        beltActiveLoopIterations = 0L;
        beltStraightLineBlockLoopIterations = 0L;
        beltVisualLoopIterations = 0L;
        beltTryMoveAttempts = 0L;
        beltTryMoveSuccesses = 0L;
        beltStraightMoveAttempts = 0L;
        beltStraightMoveSuccesses = 0L;
        beltPlanMoveCalls = 0L;
        beltPlannedMoveApplications = 0L;
        beltTouchedBlockRefreshes = 0L;
        beltWakeAroundCalls = 0L;
        beltActivityRefreshCalls = 0L;
        lastRenderFrame = -1;
        renderFrameCount = 0;
        completedSimulationTickCount = 0;
        beltLoopProfileFrameCount = 0;
        beltLoopProfileLastFrame = -1;
        beltFrameProfilingEnabled = false;
        windowStartTime = Time.unscaledTime;
    }

    public static string BuildAndResetSnapshotJson(int maxRows = DefaultSnapshotMaxRows)
    {
        bool enabled = IsEnabled;
        float now = Time.unscaledTime;
        float startTime = windowStartTime >= 0f ? windowStartTime : now;
        float windowSeconds = Mathf.Max(0f, now - startTime);

        snapshotRows.Clear();
        foreach (KeyValuePair<ProfilerGroupKey, GroupStats> pair in groupStatsByKey)
        {
            pair.Value.ActiveCount = activeUpdateStatsByKey.TryGetValue(pair.Key, out GroupStats activeStats)
                ? activeStats.ActiveCount
                : 0;

            if (pair.Value.SampleCount > 0)
            {
                snapshotRows.Add(pair.Value);
            }
        }

        foreach (KeyValuePair<ProfilerGroupKey, GroupStats> pair in activeUpdateStatsByKey)
        {
            if (!groupStatsByKey.ContainsKey(pair.Key) && pair.Value.ActiveCount > 0)
            {
                snapshotRows.Add(pair.Value);
            }
        }

        snapshotRows.Sort(CompareGroupStats);
        int rowCount = Mathf.Min(Mathf.Max(0, maxRows), snapshotRows.Count);

        jsonBuilder.Length = 0;
        jsonBuilder.Append('{');
        AppendJsonProperty("enabled", enabled ? "true" : "false", false);
        AppendJsonProperty("frame", Time.frameCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("renderFrames", renderFrameCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("simulationTicks", completedSimulationTickCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("windowMs", (windowSeconds * 1000f).ToString("0.###", CultureInfo.InvariantCulture), true);
        AppendJsonProperty("activeUpdateTicks", activeUpdateTickCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("activeBeltTicks", activeBeltTickCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("activeBeltDataMotions", activeBeltDataMotionCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("activeBeltVisualTicks", activeBeltVisualTickCount.ToString(CultureInfo.InvariantCulture), true);
        long beltItemLoopIterations =
            beltDataMotionLoopIterations
            + beltActiveLoopIterations
            + beltStraightLineBlockLoopIterations
            + beltVisualLoopIterations;
        int beltLoopFrameCount = Mathf.Max(1, beltLoopProfileFrameCount);
        AppendJsonProperty("beltLoopProfileFrames", beltLoopProfileFrameCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("beltItemLoopIterations", FormatBeltLoopsPerFrame(beltItemLoopIterations, beltLoopFrameCount), true);
        AppendJsonProperty("beltDataMotionLoopIterations", FormatBeltLoopsPerFrame(beltDataMotionLoopIterations, beltLoopFrameCount), true);
        AppendJsonProperty("beltActiveLoopIterations", FormatBeltLoopsPerFrame(beltActiveLoopIterations, beltLoopFrameCount), true);
        AppendJsonProperty("beltStraightLineBlockLoopIterations", FormatBeltLoopsPerFrame(beltStraightLineBlockLoopIterations, beltLoopFrameCount), true);
        AppendJsonProperty("beltVisualLoopIterations", FormatBeltLoopsPerFrame(beltVisualLoopIterations, beltLoopFrameCount), true);
        AppendJsonProperty("beltTryMoveAttempts", FormatBeltLoopsPerFrame(beltTryMoveAttempts, beltLoopFrameCount), true);
        AppendJsonProperty("beltTryMoveSuccesses", FormatBeltLoopsPerFrame(beltTryMoveSuccesses, beltLoopFrameCount), true);
        AppendJsonProperty("beltStraightMoveAttempts", FormatBeltLoopsPerFrame(beltStraightMoveAttempts, beltLoopFrameCount), true);
        AppendJsonProperty("beltStraightMoveSuccesses", FormatBeltLoopsPerFrame(beltStraightMoveSuccesses, beltLoopFrameCount), true);
        AppendJsonProperty("beltPlanMoveCalls", FormatBeltLoopsPerFrame(beltPlanMoveCalls, beltLoopFrameCount), true);
        AppendJsonProperty("beltPlannedMoveApplications", FormatBeltLoopsPerFrame(beltPlannedMoveApplications, beltLoopFrameCount), true);
        AppendJsonProperty("beltTouchedBlockRefreshes", FormatBeltLoopsPerFrame(beltTouchedBlockRefreshes, beltLoopFrameCount), true);
        AppendJsonProperty("beltWakeAroundCalls", FormatBeltLoopsPerFrame(beltWakeAroundCalls, beltLoopFrameCount), true);
        AppendJsonProperty("beltActivityRefreshCalls", FormatBeltLoopsPerFrame(beltActivityRefreshCalls, beltLoopFrameCount), true);
        AppendJsonProperty("runtimeCounterCount", runtimeCounters.Count.ToString(CultureInfo.InvariantCulture), true);
        jsonBuilder.Append(",\"runtimeCounters\":[");
        for (int i = 0; i < runtimeCounters.Count; i++)
        {
            if (i > 0)
            {
                jsonBuilder.Append(',');
            }

            MapObjectRuntimeCounter counter = runtimeCounters[i];
            jsonBuilder.Append('{');
            AppendJsonStringProperty("group", counter.Group, false);
            AppendJsonStringProperty("name", counter.Name, true);
            AppendJsonStringProperty("value", counter.Value, true);
            AppendJsonStringProperty("note", counter.Note, true);
            jsonBuilder.Append('}');
        }

        jsonBuilder.Append(']');
        AppendJsonProperty("rowCount", rowCount.ToString(CultureInfo.InvariantCulture), true);
        jsonBuilder.Append(",\"rows\":[");

        for (int i = 0; i < rowCount; i++)
        {
            GroupStats stats = snapshotRows[i];
            if (i > 0)
            {
                jsonBuilder.Append(',');
            }

            double totalUs = stats.TotalStopwatchTicks * stopwatchTickToMicroseconds;
            double maxUs = stats.MaxStopwatchTicks * stopwatchTickToMicroseconds;
            double avgUs = stats.SampleCount > 0 ? totalUs / stats.SampleCount : 0.0;
            double p95Us = ResolveDurationPercentileUs(stats, 0.95, maxUs);
            double p99Us = ResolveDurationPercentileUs(stats, 0.99, maxUs);

            jsonBuilder.Append('{');
            AppendJsonProperty("rank", (i + 1).ToString(CultureInfo.InvariantCulture), false);
            AppendJsonStringProperty("kind", stats.Kind, true);
            AppendJsonStringProperty("type", stats.TypeName, true);
            AppendJsonProperty("itemId", stats.ItemId.ToString(CultureInfo.InvariantCulture), true);
            AppendJsonStringProperty("itemName", stats.ItemName, true);
            AppendJsonProperty("activeCount", stats.ActiveCount.ToString(CultureInfo.InvariantCulture), true);
            AppendJsonProperty("samples", stats.SampleCount.ToString(CultureInfo.InvariantCulture), true);
            AppendJsonProperty("totalUs", totalUs.ToString("0.###", CultureInfo.InvariantCulture), true);
            AppendJsonProperty("avgUs", avgUs.ToString("0.###", CultureInfo.InvariantCulture), true);
            AppendJsonProperty("p95Us", p95Us.ToString("0.###", CultureInfo.InvariantCulture), true);
            AppendJsonProperty("p99Us", p99Us.ToString("0.###", CultureInfo.InvariantCulture), true);
            AppendJsonProperty("maxUs", maxUs.ToString("0.###", CultureInfo.InvariantCulture), true);
            jsonBuilder.Append('}');
        }

        jsonBuilder.Append("]}");
        string json = jsonBuilder.ToString();

        RecycleGroupStats(groupStatsByKey);
        snapshotRows.Clear();
        runtimeCounters.Clear();
        windowStartTime = now;
        beltDataMotionLoopIterations = 0L;
        beltActiveLoopIterations = 0L;
        beltStraightLineBlockLoopIterations = 0L;
        beltVisualLoopIterations = 0L;
        beltTryMoveAttempts = 0L;
        beltTryMoveSuccesses = 0L;
        beltStraightMoveAttempts = 0L;
        beltStraightMoveSuccesses = 0L;
        beltPlanMoveCalls = 0L;
        beltPlannedMoveApplications = 0L;
        beltTouchedBlockRefreshes = 0L;
        beltWakeAroundCalls = 0L;
        beltActivityRefreshCalls = 0L;
        renderFrameCount = 0;
        completedSimulationTickCount = 0;
        beltLoopProfileFrameCount = 0;
        beltLoopProfileLastFrame = -1;
        beltFrameProfilingEnabled = false;

        if (!enabled)
        {
            RecycleGroupStats(activeUpdateStatsByKey);
            updateGroupKeyByObject.Clear();
        }

        return json;
    }

    private static string FormatBeltLoopsPerFrame(long loopIterations, int frameCount)
    {
        double loopsPerFrame = loopIterations / (double)Mathf.Max(1, frameCount);
        return loopsPerFrame.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void RecordActiveTarget(object target)
    {
        if (target == null)
        {
            return;
        }

        ProfilerGroupKey key = ResolveUpdateGroupKey(target);
        if (!activeUpdateStatsByKey.TryGetValue(key, out GroupStats stats))
        {
            stats = AcquireGroupStats(key);
            activeUpdateStatsByKey[key] = stats;
        }

        stats.ActiveCount++;
    }

    private static void RecordSample(
        ProfilerGroupKey key,
        long elapsedTicks)
    {
        if (elapsedTicks <= 0L)
        {
            return;
        }

        if (windowStartTime < 0f)
        {
            windowStartTime = Time.unscaledTime;
        }

        if (!groupStatsByKey.TryGetValue(key, out GroupStats stats))
        {
            stats = AcquireGroupStats(key);
            groupStatsByKey[key] = stats;
        }

        stats.SampleCount++;
        stats.TotalStopwatchTicks += elapsedTicks;
        stats.RecordDuration(elapsedTicks * stopwatchTickToMicroseconds);
        if (elapsedTicks > stats.MaxStopwatchTicks)
        {
            stats.MaxStopwatchTicks = elapsedTicks;
        }
    }

    private static GroupStats AcquireGroupStats(ProfilerGroupKey key)
    {
        GroupStats stats = unusedGroupStats.Count > 0 ? unusedGroupStats.Pop() : new GroupStats();
        stats.Kind = key.Kind;
        stats.TypeName = key.TypeName;
        stats.ItemId = key.ItemId;
        stats.ItemName = key.ItemName;
        stats.ActiveCount = 0;
        stats.SampleCount = 0L;
        stats.TotalStopwatchTicks = 0L;
        stats.MaxStopwatchTicks = 0L;
        stats.ResetDurationHistogram();
        return stats;
    }

    private static double ResolveDurationPercentileUs(GroupStats stats, double percentile, double maxUs)
    {
        if (stats == null || stats.SampleCount <= 0 || stats.DurationHistogram == null)
        {
            return 0.0;
        }

        long targetCount = Math.Max(1L, (long)Math.Ceiling(stats.SampleCount * percentile));
        long cumulativeCount = 0L;
        int[] histogram = stats.DurationHistogram;
        for (int i = 0; i < histogram.Length; i++)
        {
            cumulativeCount += histogram[i];
            if (cumulativeCount < targetCount)
            {
                continue;
            }

            if (i < FineDurationBucketCount)
            {
                return Math.Min(maxUs, i + 1.0);
            }

            if (i >= DurationHistogramBucketCount - 1)
            {
                return maxUs;
            }

            int coarseIndex = i - FineDurationBucketCount;
            return Math.Min(maxUs, FineDurationBucketCount + ((coarseIndex + 1) * 1000.0));
        }

        return maxUs;
    }

    private static void RecycleGroupStats(Dictionary<ProfilerGroupKey, GroupStats> groups)
    {
        // Preserve the existing clear/reinsert order, including equal-cost rows.
        foreach (KeyValuePair<ProfilerGroupKey, GroupStats> pair in groups)
        {
            unusedGroupStats.Push(pair.Value);
        }

        groups.Clear();
    }

    private static ProfilerGroupKey ResolveUpdateGroupKey(object target)
    {
        if (updateGroupKeyByObject.TryGetValue(target, out ProfilerGroupKey key))
        {
            return key;
        }

        Type type = target.GetType();
        string typeName = type != null ? type.Name : "Unknown";
        int itemId = -1;
        string itemName = typeName;

        if (target is PropObj propObj)
        {
            itemId = propObj.ResolveItemId();
            if (TryResolveItemName(itemId, out string resolvedItemName))
            {
                itemName = resolvedItemName;
            }
        }

        key = new ProfilerGroupKey(
            "Update",
            string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName,
            itemId,
            string.IsNullOrWhiteSpace(itemName) ? typeName : itemName);
        updateGroupKeyByObject[target] = key;
        return key;
    }

    private static bool TryResolveItemName(int itemId, out string itemName)
    {
        itemName = string.Empty;
        if (itemId < 0)
        {
            return false;
        }

        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (itemManager != null && itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            itemName = string.IsNullOrWhiteSpace(itemSet.name) ? $"Item {itemId}" : itemSet.name;
            return true;
        }

        itemName = $"Item {itemId}";
        return true;
    }

    private static int CompareGroupStats(GroupStats left, GroupStats right)
    {
        int result = right.TotalStopwatchTicks.CompareTo(left.TotalStopwatchTicks);
        if (result != 0)
        {
            return result;
        }

        result = right.MaxStopwatchTicks.CompareTo(left.MaxStopwatchTicks);
        if (result != 0)
        {
            return result;
        }

        result = right.ActiveCount.CompareTo(left.ActiveCount);
        if (result != 0)
        {
            return result;
        }

        result = string.Compare(left.Kind, right.Kind, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return string.Compare(left.TypeName, right.TypeName, StringComparison.Ordinal);
    }

    private static void AppendJsonProperty(string name, string rawValue, bool prependComma)
    {
        if (prependComma)
        {
            jsonBuilder.Append(',');
        }

        jsonBuilder.Append('"');
        jsonBuilder.Append(name);
        jsonBuilder.Append("\":");
        jsonBuilder.Append(rawValue);
    }

    private static void AppendJsonStringProperty(string name, string value, bool prependComma)
    {
        if (prependComma)
        {
            jsonBuilder.Append(',');
        }

        jsonBuilder.Append('"');
        jsonBuilder.Append(name);
        jsonBuilder.Append("\":\"");
        AppendJsonEscaped(value);
        jsonBuilder.Append('"');
    }

    private static void AppendJsonEscaped(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '\\':
                    jsonBuilder.Append("\\\\");
                    break;
                case '"':
                    jsonBuilder.Append("\\\"");
                    break;
                case '\n':
                    jsonBuilder.Append("\\n");
                    break;
                case '\r':
                    jsonBuilder.Append("\\r");
                    break;
                case '\t':
                    jsonBuilder.Append("\\t");
                    break;
                default:
                    jsonBuilder.Append(character);
                    break;
            }
        }
    }

    private sealed class GroupStats
    {
        public string Kind;
        public string TypeName;
        public int ItemId;
        public string ItemName;
        public int ActiveCount;
        public long SampleCount;
        public long TotalStopwatchTicks;
        public long MaxStopwatchTicks;
        public int[] DurationHistogram;

        public void RecordDuration(double microseconds)
        {
            DurationHistogram ??= new int[DurationHistogramBucketCount];
            int bucketIndex;
            if (microseconds < FineDurationBucketCount)
            {
                bucketIndex = Mathf.Clamp((int)microseconds, 0, FineDurationBucketCount - 1);
            }
            else
            {
                int coarseIndex = (int)((microseconds - FineDurationBucketCount) / 1000.0);
                bucketIndex = FineDurationBucketCount
                              + Mathf.Clamp(coarseIndex, 0, CoarseDurationBucketCount - 1);
            }

            if (DurationHistogram[bucketIndex] < int.MaxValue)
            {
                DurationHistogram[bucketIndex]++;
            }
        }

        public void ResetDurationHistogram()
        {
            if (DurationHistogram != null)
            {
                Array.Clear(DurationHistogram, 0, DurationHistogram.Length);
            }
        }
    }

    private readonly struct ProfilerGroupKey : IEquatable<ProfilerGroupKey>
    {
        public readonly string Kind;
        public readonly string TypeName;
        public readonly string ItemName;
        public readonly int ItemId;
        private readonly int hashCode;

        public ProfilerGroupKey(string kind, string typeName, int itemId, string itemName)
        {
            Kind = kind ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            ItemName = itemName ?? string.Empty;
            ItemId = itemId;
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Kind.GetHashCode();
                hash = (hash * 31) + TypeName.GetHashCode();
                hash = (hash * 31) + ItemName.GetHashCode();
                hashCode = (hash * 31) + ItemId;
            }
        }

        public bool Equals(ProfilerGroupKey other)
        {
            return ItemId == other.ItemId
                   && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
                   && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
                   && string.Equals(ItemName, other.ItemName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ProfilerGroupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return hashCode;
        }
    }
}
