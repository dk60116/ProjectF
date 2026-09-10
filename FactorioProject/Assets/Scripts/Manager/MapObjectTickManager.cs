using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

public interface IMapObjectUpdateTick
{
    void ManagedUpdateTick(float deltaTime);
}

public interface IMapObjectUpdateTickInterval
{
    float ManagedUpdateTickIntervalSeconds { get; }
}

public interface IMapObjectSimulationIdentity
{
    long SimulationId { get; }
}

public interface IMapObjectStagedUpdateTick
{
    void PlanManagedUpdateTick(float deltaTime);

    void ApplyManagedUpdateTick();
}

public static class DeterministicSimulationUnits
{
    // Divisible by 60 so common per-second values retain exact sub-tick units.
    public const long UnitsPerWhole = 60_000_000L;

    public static long FromInt(int value)
    {
        return value <= 0
            ? 0L
            : Math.Min(long.MaxValue, (long)value * UnitsPerWhole);
    }

    public static long FromFloat(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
        {
            return 0L;
        }

        if (float.IsPositiveInfinity(value))
        {
            return long.MaxValue;
        }

        decimal scaled = decimal.Round(
            (decimal)value * UnitsPerWhole,
            0,
            MidpointRounding.AwayFromZero);
        return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
    }

    public static float ToFloat(long units)
    {
        return units <= 0L ? 0f : (float)((double)units / UnitsPerWhole);
    }

    public static long SecondsToTicks(float seconds)
    {
        if (float.IsNaN(seconds) || seconds <= 0f)
        {
            return 0L;
        }

        return Math.Max(
            1L,
            (long)decimal.Round(
                (decimal)seconds * MapObjectTickManager.DefaultSimulationTicksPerSecond,
                0,
                MidpointRounding.AwayFromZero));
    }

    public static float TicksToSeconds(long ticks)
    {
        return ticks <= 0L
            ? 0f
            : ticks * MapObjectTickManager.FixedSimulationDeltaSeconds;
    }

    public static long DeltaTimeToTicks(float deltaTime)
    {
        return deltaTime <= 0f ? 0L : Math.Max(1L, SecondsToTicks(deltaTime));
    }

    public static long RateForTicks(float ratePerSecond, long elapsedTicks)
    {
        if (ratePerSecond <= 0f || elapsedTicks <= 0L)
        {
            return 0L;
        }

        decimal units = (decimal)ratePerSecond
                        * UnitsPerWhole
                        * elapsedTicks
                        / MapObjectTickManager.DefaultSimulationTicksPerSecond;
        decimal rounded = decimal.Round(units, 0, MidpointRounding.AwayFromZero);
        return rounded >= long.MaxValue ? long.MaxValue : (long)rounded;
    }

    public static long MultiplyRatio(long value, long numerator, long denominator)
    {
        if (value <= 0L || numerator <= 0L || denominator <= 0L)
        {
            return 0L;
        }

        if (numerator >= denominator)
        {
            return value;
        }

        return (long)decimal.Truncate((decimal)value * numerator / denominator);
    }
}

public sealed class MapObjectTickManager : MonoBehaviour
{
    public const int DefaultSimulationTicksPerSecond = 60;
    public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
    private const float DefaultUpdateTickIntervalSeconds = FixedSimulationDeltaSeconds;
    private const int AliveValidationTickInterval = 120;
    private const int DefaultMaximumSimulationStepsPerFrame = 8;

    private static MapObjectTickManager instance;
    private static bool applicationQuitting;
    private static readonly HashSet<IMapObjectUpdateTick> requestedUpdateTicks =
        new HashSet<IMapObjectUpdateTick>();

    [SerializeField, Min(0.001f)]
    private float updateTickIntervalSeconds = DefaultUpdateTickIntervalSeconds;
    [SerializeField, Range(1, 64)]
    private int maximumSimulationStepsPerFrame = DefaultMaximumSimulationStepsPerFrame;

    private readonly List<UpdateTickBucket> updateTickBuckets = new List<UpdateTickBucket>(4);
    private readonly Dictionary<int, UpdateTickBucket> updateTickBucketsByIntervalKey =
        new Dictionary<int, UpdateTickBucket>(4);
    private readonly HashSet<IMapObjectUpdateTick> updateTickSet = new HashSet<IMapObjectUpdateTick>();
    private readonly HashSet<IMapObjectUpdateTick> updateTickEntrySet = new HashSet<IMapObjectUpdateTick>();
    private readonly Dictionary<IMapObjectUpdateTick, UpdateTickEntry> updateTickEntriesByTick =
        new Dictionary<IMapObjectUpdateTick, UpdateTickEntry>();
    private readonly List<IMapObjectUpdateTick> requestedTickCleanupBuffer =
        new List<IMapObjectUpdateTick>();
    private readonly List<IMapObjectUpdateTick> activeTickCleanupBuffer =
        new List<IMapObjectUpdateTick>();
    private readonly List<UpdateTickEntry> dueUpdateTickEntries = new List<UpdateTickEntry>(64);
    private long simulationTick;
    private long nextAliveValidationTick;
    private double simulationTimeAccumulator;
    private bool tickingUpdateObjects;
    private bool updateTicksDirty;

    public static long CurrentSimulationTick => instance != null ? instance.simulationTick : 0L;
    public static double CurrentSimulationTimeSeconds =>
        CurrentSimulationTick * (double)FixedSimulationDeltaSeconds;
    public static double SimulationBacklogTicks => instance != null
        ? Math.Max(0d, instance.simulationTimeAccumulator / FixedSimulationDeltaSeconds)
        : 0d;
    public static float SimulationInterpolationAlpha => (float)Math.Min(
        1d,
        SimulationBacklogTicks);

    public static void RegisterUpdateTick(IMapObjectUpdateTick tick)
    {
        if (tick == null || !Application.isPlaying || applicationQuitting)
        {
            return;
        }

        requestedUpdateTicks.Add(tick);
        EnsureInstance().AddUpdateTick(tick);
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
            instance.RemoveUpdateTick(tick);
        }
    }

    public static bool IsUpdateTickRegistered(IMapObjectUpdateTick tick)
    {
        return tick != null
               && requestedUpdateTicks.Contains(tick)
               && instance != null
               && instance.HasExecutableUpdateTick(tick);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        applicationQuitting = false;
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
        ReconcileRequestedUpdateTicks(true);
    }

    private void Update()
    {
        simulationTimeAccumulator += Math.Max(0d, Time.deltaTime);
        int maximumSteps = Mathf.Max(1, maximumSimulationStepsPerFrame);
        int completedSteps = 0;
        while (simulationTimeAccumulator + 0.000000001d >= FixedSimulationDeltaSeconds
               && completedSteps < maximumSteps)
        {
            simulationTimeAccumulator -= FixedSimulationDeltaSeconds;
            simulationTick++;
            bool fullValidationRequested = RequestPeriodicAliveValidation();
            ReconcileRequestedUpdateTicks(fullValidationRequested);
            TickUpdateObjects();
            completedSteps++;
        }
    }

    public static void RestoreSimulationTick(long restoredTick)
    {
        if (!Application.isPlaying || applicationQuitting)
        {
            return;
        }

        MapObjectTickManager manager = EnsureInstance();
        manager.simulationTick = Math.Max(0L, restoredTick);
        manager.simulationTimeAccumulator = 0d;
        manager.nextAliveValidationTick = manager.simulationTick;
        manager.ResetUpdateTickBucketState();
    }

    public static void RefreshSimulationIdentity(IMapObjectUpdateTick tick)
    {
        if (tick == null || instance == null || !instance.updateTickSet.Contains(tick))
        {
            return;
        }

        for (int i = 0; i < instance.updateTickBuckets.Count; i++)
        {
            UpdateTickBucket bucket = instance.updateTickBuckets[i];
            if (bucket != null)
            {
                bucket.OrderDirty = true;
            }
        }
    }

    private void OnApplicationQuit()
    {
        applicationQuitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void AddUpdateTick(IMapObjectUpdateTick tick)
    {
        if (updateTicksDirty && !tickingUpdateObjects)
        {
            CompactUpdateTicks();
        }

        if (tick == null)
        {
            return;
        }

        if (updateTickSet.Contains(tick))
        {
            if (HasExecutableUpdateTick(tick))
            {
                return;
            }

            // The active marker can survive after its bucket entry is lost.
            // Remove the incomplete registration so it can be rebuilt below.
            updateTickSet.Remove(tick);
            updateTicksDirty = true;
            if (tickingUpdateObjects)
            {
                return;
            }

            CompactUpdateTicks();
        }

        if (updateTickEntrySet.Contains(tick))
        {
            updateTickSet.Add(tick);
            if (updateTickEntriesByTick.TryGetValue(tick, out UpdateTickEntry entry))
            {
                entry.ResetSchedule(simulationTick);
            }

            enabled = true;
            return;
        }

        updateTickSet.Add(tick);
        updateTickEntrySet.Add(tick);
        UpdateTickBucket bucket = GetOrCreateUpdateTickBucket(ResolveUpdateTickIntervalTicks(tick));
        UpdateTickEntry newEntry = new UpdateTickEntry(tick, bucket.IntervalTicks, simulationTick);
        updateTickEntriesByTick[tick] = newEntry;
        bucket.Entries.Add(newEntry);
        bucket.OrderDirty = true;
        enabled = true;
    }

    private void RemoveUpdateTick(IMapObjectUpdateTick tick)
    {
        if (tick == null || !updateTickSet.Remove(tick))
        {
            return;
        }

        updateTicksDirty = true;
    }

    private void TickUpdateObjects()
    {
        if (updateTicksDirty)
        {
            CompactUpdateTicks();
        }

        int count = updateTickSet.Count;
        bool profileTicks = MapObjectTickProfiler.IsEnabled;
        if (profileTicks)
        {
            MapObjectTickProfiler.SetActiveUpdateTargets(updateTickSet);
        }
        if (count <= 0)
        {
            ResetUpdateTickBucketState();
            RefreshEnabledState();
            return;
        }

        tickingUpdateObjects = true;
        try
        {
            dueUpdateTickEntries.Clear();
            for (int bucketIndex = 0; bucketIndex < updateTickBuckets.Count; bucketIndex++)
            {
                CollectDueUpdateEntries(updateTickBuckets[bucketIndex]);
            }

            dueUpdateTickEntries.Sort(CompareUpdateTickEntries);
            PlanStagedUpdateEntries();
            ApplyDueUpdateEntries(profileTicks);
            dueUpdateTickEntries.Clear();
        }
        finally
        {
            tickingUpdateObjects = false;
        }

        if (updateTicksDirty)
        {
            CompactUpdateTicks();
        }

        RefreshEnabledState();
    }

    private void CollectDueUpdateEntries(UpdateTickBucket bucket)
    {
        if (bucket == null)
        {
            return;
        }

        int count = bucket.Entries.Count;
        if (count <= 0)
        {
            return;
        }

        if (bucket.OrderDirty)
        {
            bucket.Entries.Sort(CompareUpdateTickEntries);
            bucket.OrderDirty = false;
        }

        List<UpdateTickEntry> entries = bucket.Entries;
        for (int entryIndex = 0; entryIndex < count; entryIndex++)
        {
            UpdateTickEntry entry = entries[entryIndex];
            if (entry == null)
            {
                updateTicksDirty = true;
                continue;
            }

            IMapObjectUpdateTick tick = entry.Tick;
            if (tick == null)
            {
                updateTicksDirty = true;
                continue;
            }

            if (updateTicksDirty && !updateTickSet.Contains(tick))
            {
                continue;
            }

            if (entry.NextDueTick > simulationTick)
            {
                continue;
            }

            long elapsedTicks = Math.Max(1L, simulationTick - entry.LastExecutedTick);
            entry.PendingDeltaTime = elapsedTicks * FixedSimulationDeltaSeconds;
            entry.MarkExecuted(simulationTick);
            dueUpdateTickEntries.Add(entry);
        }
    }

    private void PlanStagedUpdateEntries()
    {
        for (int i = 0; i < dueUpdateTickEntries.Count; i++)
        {
            UpdateTickEntry entry = dueUpdateTickEntries[i];
            if (entry?.Tick is IMapObjectStagedUpdateTick stagedTick)
            {
                stagedTick.PlanManagedUpdateTick(entry.PendingDeltaTime);
            }
        }
    }

    private void ApplyDueUpdateEntries(bool profileTicks)
    {
        for (int i = 0; i < dueUpdateTickEntries.Count; i++)
        {
            UpdateTickEntry entry = dueUpdateTickEntries[i];
            IMapObjectUpdateTick tick = entry?.Tick;
            if (tick == null || updateTicksDirty && !updateTickSet.Contains(tick))
            {
                continue;
            }

            long startTimestamp = profileTicks ? MapObjectTickProfiler.BeginSample() : 0L;
            if (tick is IMapObjectStagedUpdateTick stagedTick)
            {
                stagedTick.ApplyManagedUpdateTick();
            }
            else
            {
                tick.ManagedUpdateTick(entry.PendingDeltaTime);
            }

            if (profileTicks)
            {
                MapObjectTickProfiler.EndUpdateSample(tick, startTimestamp);
            }
        }
    }

    private bool RequestPeriodicAliveValidation()
    {
        if (simulationTick < nextAliveValidationTick)
        {
            return false;
        }

        nextAliveValidationTick = simulationTick + AliveValidationTickInterval;
        if (updateTickSet.Count > 0)
        {
            updateTicksDirty = true;
        }

        return true;
    }

    private void ReconcileRequestedUpdateTicks(bool forceFullValidation)
    {
        if (!forceFullValidation && RegistrationCountsMatch())
        {
            return;
        }

        requestedTickCleanupBuffer.Clear();
        foreach (IMapObjectUpdateTick tick in requestedUpdateTicks)
        {
            if (!IsTickAlive(tick))
            {
                requestedTickCleanupBuffer.Add(tick);
                continue;
            }

            if (!HasExecutableUpdateTick(tick))
            {
                AddUpdateTick(tick);
            }
        }

        for (int i = 0; i < requestedTickCleanupBuffer.Count; i++)
        {
            IMapObjectUpdateTick tick = requestedTickCleanupBuffer[i];
            requestedUpdateTicks.Remove(tick);
            RemoveUpdateTick(tick);
        }

        requestedTickCleanupBuffer.Clear();
        if (!forceFullValidation && RegistrationCountsMatch())
        {
            return;
        }

        activeTickCleanupBuffer.Clear();
        foreach (IMapObjectUpdateTick tick in updateTickSet)
        {
            if (!IsTickAlive(tick) || !requestedUpdateTicks.Contains(tick))
            {
                activeTickCleanupBuffer.Add(tick);
            }
        }

        for (int i = 0; i < activeTickCleanupBuffer.Count; i++)
        {
            RemoveUpdateTick(activeTickCleanupBuffer[i]);
        }

        activeTickCleanupBuffer.Clear();
        if (updateTickEntrySet.Count != updateTickSet.Count
            || updateTickEntriesByTick.Count != updateTickSet.Count)
        {
            updateTicksDirty = true;
        }
    }

    private bool RegistrationCountsMatch()
    {
        int requestedCount = requestedUpdateTicks.Count;
        return requestedCount == updateTickSet.Count
               && requestedCount == updateTickEntrySet.Count
               && requestedCount == updateTickEntriesByTick.Count;
    }

    private bool HasExecutableUpdateTick(IMapObjectUpdateTick tick)
    {
        return tick != null
               && updateTickSet.Contains(tick)
               && updateTickEntrySet.Contains(tick)
               && updateTickEntriesByTick.TryGetValue(tick, out UpdateTickEntry entry)
               && entry != null
               && ReferenceEquals(entry.Tick, tick);
    }

    private void CompactUpdateTicks()
    {
        updateTickEntrySet.Clear();
        updateTickEntriesByTick.Clear();
        for (int bucketIndex = updateTickBuckets.Count - 1; bucketIndex >= 0; bucketIndex--)
        {
            UpdateTickBucket bucket = updateTickBuckets[bucketIndex];
            if (bucket == null)
            {
                updateTickBuckets.RemoveAt(bucketIndex);
                continue;
            }

            List<UpdateTickEntry> entries = bucket.Entries;
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < entries.Count; readIndex++)
            {
                UpdateTickEntry entry = entries[readIndex];
                IMapObjectUpdateTick tick = entry != null ? entry.Tick : null;
                if (!IsTickAlive(tick) || !updateTickSet.Contains(tick))
                {
                    if (tick != null)
                    {
                        updateTickSet.Remove(tick);
                    }

                    continue;
                }

                if (!updateTickEntrySet.Add(tick))
                {
                    continue;
                }

                updateTickEntriesByTick[tick] = entry;
                entries[writeIndex] = entry;
                writeIndex++;
            }

            if (writeIndex < entries.Count)
            {
                entries.RemoveRange(writeIndex, entries.Count - writeIndex);
            }

            if (entries.Count <= 0)
            {
                updateTickBucketsByIntervalKey.Remove(bucket.IntervalTicks);
                updateTickBuckets.RemoveAt(bucketIndex);
                continue;
            }

            bucket.OrderDirty = true;
        }

        updateTicksDirty = false;
    }

    private void RefreshEnabledState()
    {
        // The fixed clock must continue advancing even while every simulation target sleeps.
        enabled = Application.isPlaying && !applicationQuitting;
    }

    private UpdateTickBucket GetOrCreateUpdateTickBucket(int intervalTicks)
    {
        intervalTicks = Mathf.Max(1, intervalTicks);
        if (updateTickBucketsByIntervalKey.TryGetValue(intervalTicks, out UpdateTickBucket bucket))
        {
            return bucket;
        }

        bucket = new UpdateTickBucket(intervalTicks);
        updateTickBucketsByIntervalKey.Add(intervalTicks, bucket);
        updateTickBuckets.Add(bucket);
        updateTickBuckets.Sort((left, right) => left.IntervalTicks.CompareTo(right.IntervalTicks));
        return bucket;
    }

    private int ResolveUpdateTickIntervalTicks(IMapObjectUpdateTick tick)
    {
        float intervalSeconds = updateTickIntervalSeconds;
        if (tick is IMapObjectUpdateTickInterval intervalProvider)
        {
            intervalSeconds = intervalProvider.ManagedUpdateTickIntervalSeconds;
        }

        return Mathf.Max(1, Mathf.RoundToInt(
            Mathf.Max(FixedSimulationDeltaSeconds, intervalSeconds)
            / FixedSimulationDeltaSeconds));
    }

    private static int CompareUpdateTickEntries(UpdateTickEntry left, UpdateTickEntry right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        long leftId = ResolveSimulationId(left.Tick);
        long rightId = ResolveSimulationId(right.Tick);
        int result = leftId.CompareTo(rightId);
        if (result != 0)
        {
            return result;
        }

        string leftType = left.Tick?.GetType().FullName ?? string.Empty;
        string rightType = right.Tick?.GetType().FullName ?? string.Empty;
        return string.CompareOrdinal(leftType, rightType);
    }

    private static long ResolveSimulationId(IMapObjectUpdateTick tick)
    {
        return tick is IMapObjectSimulationIdentity identity
            ? identity.SimulationId
            : 0L;
    }

    private void ResetUpdateTickBucketState()
    {
        for (int i = 0; i < updateTickBuckets.Count; i++)
        {
            UpdateTickBucket bucket = updateTickBuckets[i];
            if (bucket == null)
            {
                continue;
            }

            for (int entryIndex = 0; entryIndex < bucket.Entries.Count; entryIndex++)
            {
                bucket.Entries[entryIndex]?.ResetSchedule(simulationTick);
            }
        }
    }

    private static bool IsTickAlive(object tick)
    {
        if (tick == null)
        {
            return false;
        }

        UnityEngine.Object unityObject = tick as UnityEngine.Object;
        return ReferenceEquals(unityObject, null) || unityObject != null;
    }

    private sealed class UpdateTickEntry
    {
        public readonly IMapObjectUpdateTick Tick;
        public readonly int IntervalTicks;
        public long LastExecutedTick;
        public long NextDueTick;
        public float PendingDeltaTime;

        public UpdateTickEntry(IMapObjectUpdateTick tick, int intervalTicks, long currentTick)
        {
            Tick = tick;
            IntervalTicks = Math.Max(1, intervalTicks);
            ResetSchedule(currentTick);
        }

        public void ResetSchedule(long currentTick)
        {
            LastExecutedTick = currentTick;
            NextDueTick = currentTick + 1L;
        }

        public void MarkExecuted(long currentTick)
        {
            LastExecutedTick = currentTick;
            NextDueTick = currentTick + IntervalTicks;
        }
    }

    private sealed class UpdateTickBucket
    {
        public readonly int IntervalTicks;
        public readonly List<UpdateTickEntry> Entries = new List<UpdateTickEntry>();
        public bool OrderDirty;

        public UpdateTickBucket(int intervalTicks)
        {
            IntervalTicks = intervalTicks;
        }
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
            enabled = IsEnabled;
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
        string resolvedTypeName = string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName;
        string resolvedKind = string.IsNullOrWhiteSpace(kind) ? "Update" : kind;
        string resolvedItemName = string.IsNullOrWhiteSpace(itemName) ? resolvedTypeName : itemName;
        // Finish timing before hashing the group key, as with object samples.
        long elapsedTicks = Math.Max(0L, Stopwatch.GetTimestamp() - startTimestamp);
        ProfilerGroupKey key = new ProfilerGroupKey(
            resolvedKind,
            resolvedTypeName,
            -1,
            resolvedItemName);
        RecordSample(key, elapsedTicks);
    }

    public static void SetActiveTickCount(int updateCount)
    {
        activeUpdateTickCount = Mathf.Max(0, updateCount);
        RecycleGroupStats(activeUpdateStatsByKey);
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

        bool enabled = IsEnabled;
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
        beltFrameProfilingEnabled = enabled;
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
        AppendJsonProperty("windowMs", (windowSeconds * 1000f).ToString("0.###", CultureInfo.InvariantCulture), true);
        AppendJsonProperty("activeUpdateTicks", activeUpdateTickCount.ToString(CultureInfo.InvariantCulture), true);
        AppendJsonProperty("activeLateTicks", "0", true);
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
        return stats;
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
