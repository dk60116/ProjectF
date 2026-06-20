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

public sealed class MapObjectTickManager : MonoBehaviour
{
    private const float DefaultUpdateTickIntervalSeconds = 1f / 60f;
    private const int UpdateTickIntervalKeyScale = 10000;
    private const int AliveValidationFrameInterval = 120;

    private static MapObjectTickManager instance;
    private static bool applicationQuitting;

    [SerializeField, Min(0.001f)]
    private float updateTickIntervalSeconds = DefaultUpdateTickIntervalSeconds;

    private readonly List<UpdateTickBucket> updateTickBuckets = new List<UpdateTickBucket>(4);
    private readonly Dictionary<int, UpdateTickBucket> updateTickBucketsByIntervalKey =
        new Dictionary<int, UpdateTickBucket>(4);
    private readonly HashSet<IMapObjectUpdateTick> updateTickSet = new HashSet<IMapObjectUpdateTick>();
    private readonly HashSet<IMapObjectUpdateTick> updateTickEntrySet = new HashSet<IMapObjectUpdateTick>();
    private readonly Dictionary<IMapObjectUpdateTick, UpdateTickEntry> updateTickEntriesByTick =
        new Dictionary<IMapObjectUpdateTick, UpdateTickEntry>();
    private int nextAliveValidationFrame;
    private bool tickingUpdateObjects;
    private bool updateTicksDirty;

    public static void RegisterUpdateTick(IMapObjectUpdateTick tick)
    {
        if (tick == null || !Application.isPlaying || applicationQuitting)
        {
            return;
        }

        EnsureInstance().AddUpdateTick(tick);
    }

    public static void UnregisterUpdateTick(IMapObjectUpdateTick tick)
    {
        if (tick == null || instance == null)
        {
            return;
        }

        instance.RemoveUpdateTick(tick);
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
    }

    private void Update()
    {
        RequestPeriodicAliveValidation();
        TickUpdateObjects(Time.deltaTime);
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

        if (tick == null || updateTickSet.Contains(tick))
        {
            return;
        }

        if (updateTickEntrySet.Contains(tick))
        {
            updateTickSet.Add(tick);
            if (updateTickEntriesByTick.TryGetValue(tick, out UpdateTickEntry entry))
            {
                entry.LastTime = Time.time;
            }

            enabled = true;
            return;
        }

        updateTickSet.Add(tick);
        updateTickEntrySet.Add(tick);
        UpdateTickBucket bucket = GetOrCreateUpdateTickBucket(ResolveUpdateTickIntervalSeconds(tick));
        UpdateTickEntry newEntry = new UpdateTickEntry(tick, Time.time);
        updateTickEntriesByTick[tick] = newEntry;
        bucket.Entries.Add(newEntry);
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

    private void TickUpdateObjects(float deltaTime)
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

        float frameDeltaTime = Mathf.Max(0f, deltaTime);
        tickingUpdateObjects = true;
        try
        {
            for (int bucketIndex = 0; bucketIndex < updateTickBuckets.Count; bucketIndex++)
            {
                TickUpdateBucket(updateTickBuckets[bucketIndex], frameDeltaTime, profileTicks);
            }
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

    private void TickUpdateBucket(UpdateTickBucket bucket, float frameDeltaTime, bool profileTicks)
    {
        if (bucket == null)
        {
            return;
        }

        int count = bucket.Entries.Count;
        if (count <= 0)
        {
            bucket.Quota = 0f;
            bucket.Cursor = 0;
            return;
        }

        float safeInterval = Mathf.Max(0.001f, bucket.IntervalSeconds);
        bucket.Quota += count * frameDeltaTime / safeInterval;

        int ticksToRun = Mathf.Min(count, Mathf.FloorToInt(bucket.Quota));
        if (ticksToRun <= 0)
        {
            return;
        }

        bucket.Quota -= ticksToRun;
        bool tickedEveryObject = ticksToRun >= count;
        float managedDeltaTime = tickedEveryObject
            ? frameDeltaTime
            : safeInterval;
        if (tickedEveryObject && bucket.Quota >= 1f)
        {
            bucket.Quota -= Mathf.Floor(bucket.Quota);
        }

        List<UpdateTickEntry> entries = bucket.Entries;
        for (int processed = 0; processed < ticksToRun; processed++)
        {
            if (bucket.Cursor >= entries.Count)
            {
                bucket.Cursor = 0;
            }

            UpdateTickEntry entry = entries[bucket.Cursor];
            bucket.Cursor++;
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

            float resolvedDeltaTime = ResolveUpdateTickDeltaTime(entry, managedDeltaTime);
            if (profileTicks)
            {
                long startTimestamp = MapObjectTickProfiler.BeginSample();
                tick.ManagedUpdateTick(resolvedDeltaTime);
                MapObjectTickProfiler.EndUpdateSample(tick, startTimestamp);
            }
            else
            {
                tick.ManagedUpdateTick(resolvedDeltaTime);
            }
        }
    }

    private void RequestPeriodicAliveValidation()
    {
        int frameCount = Time.frameCount;
        if (frameCount < nextAliveValidationFrame)
        {
            return;
        }

        nextAliveValidationFrame = frameCount + AliveValidationFrameInterval;
        if (updateTickSet.Count > 0)
        {
            updateTicksDirty = true;
        }
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
                updateTickBucketsByIntervalKey.Remove(bucket.IntervalKey);
                updateTickBuckets.RemoveAt(bucketIndex);
                continue;
            }

            if (bucket.Cursor >= entries.Count)
            {
                bucket.Cursor %= entries.Count;
            }
        }

        updateTicksDirty = false;
    }

    private float ResolveUpdateTickDeltaTime(UpdateTickEntry entry, float fallbackDeltaTime)
    {
        float now = Time.time;
        float safeFallback = Mathf.Max(0f, fallbackDeltaTime);
        if (entry == null)
        {
            return safeFallback;
        }

        float lastTime = entry.LastTime;
        entry.LastTime = now;
        float elapsedTime = now - lastTime;
        return elapsedTime > 0f ? elapsedTime : safeFallback;
    }

    private void RefreshEnabledState()
    {
        enabled = updateTickSet.Count > 0;
    }

    private UpdateTickBucket GetOrCreateUpdateTickBucket(float intervalSeconds)
    {
        intervalSeconds = Mathf.Max(0.001f, intervalSeconds);
        int intervalKey = GetUpdateTickIntervalKey(intervalSeconds);
        if (updateTickBucketsByIntervalKey.TryGetValue(intervalKey, out UpdateTickBucket bucket))
        {
            return bucket;
        }

        bucket = new UpdateTickBucket(intervalKey, intervalSeconds);
        updateTickBucketsByIntervalKey.Add(intervalKey, bucket);
        updateTickBuckets.Add(bucket);
        return bucket;
    }

    private float ResolveUpdateTickIntervalSeconds(IMapObjectUpdateTick tick)
    {
        if (tick is IMapObjectUpdateTickInterval intervalProvider)
        {
            return Mathf.Max(0.001f, intervalProvider.ManagedUpdateTickIntervalSeconds);
        }

        return Mathf.Max(0.001f, updateTickIntervalSeconds);
    }

    private static int GetUpdateTickIntervalKey(float intervalSeconds)
    {
        return Mathf.Max(1, Mathf.RoundToInt(intervalSeconds * UpdateTickIntervalKeyScale));
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

            bucket.Quota = 0f;
            bucket.Cursor = 0;
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
        public float LastTime;

        public UpdateTickEntry(IMapObjectUpdateTick tick, float lastTime)
        {
            Tick = tick;
            LastTime = lastTime;
        }
    }

    private sealed class UpdateTickBucket
    {
        public readonly int IntervalKey;
        public readonly float IntervalSeconds;
        public readonly List<UpdateTickEntry> Entries = new List<UpdateTickEntry>();
        public float Quota;
        public int Cursor;

        public UpdateTickBucket(int intervalKey, float intervalSeconds)
        {
            IntervalKey = intervalKey;
            IntervalSeconds = intervalSeconds;
        }
    }
}

public static class MapObjectTickProfiler
{
    private const double MicrosecondsPerSecond = 1000000.0;
    private const int DefaultSnapshotMaxRows = 64;

    private static readonly Dictionary<string, GroupStats> groupStatsByKey = new Dictionary<string, GroupStats>(128);
    private static readonly Dictionary<string, GroupStats> activeUpdateStatsByKey =
        new Dictionary<string, GroupStats>(128);
    private static readonly Dictionary<object, TargetDescriptor> targetDescriptorByObject =
        new Dictionary<object, TargetDescriptor>(512);
    private static readonly List<GroupStats> snapshotRows = new List<GroupStats>(128);
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

    public static void EndUpdateSample(object target, long startTimestamp)
    {
        RecordSample("Update", target, startTimestamp);
    }

    public static void EndNamedSample(string kind, string typeName, string itemName, long startTimestamp)
    {
        TargetDescriptor descriptor = new TargetDescriptor
        {
            TypeName = string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName,
            ItemId = -1,
            ItemName = string.IsNullOrWhiteSpace(itemName) ? typeName : itemName
        };
        RecordSample(string.IsNullOrWhiteSpace(kind) ? "Update" : kind, descriptor, startTimestamp);
    }

    public static void SetActiveTickCount(int updateCount)
    {
        activeUpdateTickCount = Mathf.Max(0, updateCount);
        activeUpdateStatsByKey.Clear();
    }

    public static void SetActiveUpdateTargets(ICollection<IMapObjectUpdateTick> updateTicks)
    {
        activeUpdateStatsByKey.Clear();
        activeUpdateTickCount = updateTicks != null ? Mathf.Max(0, updateTicks.Count) : 0;
        if (updateTicks == null || updateTicks.Count <= 0)
        {
            return;
        }

        foreach (IMapObjectUpdateTick tick in updateTicks)
        {
            RecordActiveTarget("Update", tick);
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

    public static void Reset()
    {
        groupStatsByKey.Clear();
        activeUpdateStatsByKey.Clear();
        targetDescriptorByObject.Clear();
        snapshotRows.Clear();
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
        foreach (KeyValuePair<string, GroupStats> pair in groupStatsByKey)
        {
            pair.Value.ActiveCount = activeUpdateStatsByKey.TryGetValue(pair.Key, out GroupStats activeStats)
                ? activeStats.ActiveCount
                : 0;

            if (pair.Value.SampleCount > 0)
            {
                snapshotRows.Add(pair.Value);
            }
        }

        foreach (KeyValuePair<string, GroupStats> pair in activeUpdateStatsByKey)
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

        groupStatsByKey.Clear();
        snapshotRows.Clear();
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
            activeUpdateStatsByKey.Clear();
            targetDescriptorByObject.Clear();
        }

        return json;
    }

    private static string FormatBeltLoopsPerFrame(long loopIterations, int frameCount)
    {
        double loopsPerFrame = loopIterations / (double)Mathf.Max(1, frameCount);
        return loopsPerFrame.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void RecordSample(string kind, object target, long startTimestamp)
    {
        if (target == null)
        {
            return;
        }

        RecordSample(kind, ResolveTargetDescriptor(target), startTimestamp);
    }

    private static void RecordActiveTarget(string kind, object target)
    {
        if (target == null)
        {
            return;
        }

        TargetDescriptor descriptor = ResolveTargetDescriptor(target);
        if (descriptor == null)
        {
            return;
        }

        string resolvedKind = string.IsNullOrWhiteSpace(kind) ? "Update" : kind;
        string key = BuildGroupKey(resolvedKind, descriptor);
        if (!activeUpdateStatsByKey.TryGetValue(key, out GroupStats stats))
        {
            stats = new GroupStats
            {
                Kind = resolvedKind,
                TypeName = descriptor.TypeName,
                ItemId = descriptor.ItemId,
                ItemName = descriptor.ItemName
            };
            activeUpdateStatsByKey[key] = stats;
        }

        stats.ActiveCount++;
    }

    private static void RecordSample(string kind, TargetDescriptor descriptor, long startTimestamp)
    {
        if (descriptor == null)
        {
            return;
        }

        long elapsedTicks = Math.Max(0L, Stopwatch.GetTimestamp() - startTimestamp);
        if (elapsedTicks <= 0L)
        {
            return;
        }

        if (windowStartTime < 0f)
        {
            windowStartTime = Time.unscaledTime;
        }

        string key = BuildGroupKey(kind, descriptor);
        if (!groupStatsByKey.TryGetValue(key, out GroupStats stats))
        {
            stats = new GroupStats
            {
                Kind = kind,
                TypeName = descriptor.TypeName,
                ItemId = descriptor.ItemId,
                ItemName = descriptor.ItemName
            };
            groupStatsByKey[key] = stats;
        }

        stats.SampleCount++;
        stats.TotalStopwatchTicks += elapsedTicks;
        if (elapsedTicks > stats.MaxStopwatchTicks)
        {
            stats.MaxStopwatchTicks = elapsedTicks;
        }
    }

    private static string BuildGroupKey(string kind, TargetDescriptor descriptor)
    {
        return string.Concat(kind, "|", descriptor.TypeName, "|", descriptor.ItemId.ToString(CultureInfo.InvariantCulture));
    }

    private static TargetDescriptor ResolveTargetDescriptor(object target)
    {
        if (targetDescriptorByObject.TryGetValue(target, out TargetDescriptor descriptor))
        {
            return descriptor;
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

        descriptor = new TargetDescriptor
        {
            TypeName = string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName,
            ItemId = itemId,
            ItemName = string.IsNullOrWhiteSpace(itemName) ? typeName : itemName
        };
        targetDescriptorByObject[target] = descriptor;
        return descriptor;
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

    private sealed class TargetDescriptor
    {
        public string TypeName;
        public int ItemId;
        public string ItemName;
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
}
