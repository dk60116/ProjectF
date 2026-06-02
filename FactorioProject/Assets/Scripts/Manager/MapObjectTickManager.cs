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

public interface IMapObjectLateTick
{
    void ManagedLateUpdateTick(float deltaTime);
}

public sealed class MapObjectTickManager : MonoBehaviour
{
    private const float DefaultUpdateTickIntervalSeconds = 1f / 60f;

    private static MapObjectTickManager instance;
    private static bool applicationQuitting;

    [SerializeField, Min(0.001f)]
    private float updateTickIntervalSeconds = DefaultUpdateTickIntervalSeconds;

    private readonly List<IMapObjectUpdateTick> updateTicks = new List<IMapObjectUpdateTick>();
    private readonly HashSet<IMapObjectUpdateTick> updateTickSet = new HashSet<IMapObjectUpdateTick>();
    private readonly Dictionary<IMapObjectUpdateTick, float> updateTickLastTimes =
        new Dictionary<IMapObjectUpdateTick, float>();
    private readonly List<IMapObjectLateTick> lateTicks = new List<IMapObjectLateTick>();
    private readonly HashSet<IMapObjectLateTick> lateTickSet = new HashSet<IMapObjectLateTick>();
    private float updateTickQuota;
    private int updateTickCursor;
    private bool updateTicksDirty;
    private bool lateTicksDirty;

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

    public static void RegisterLateTick(IMapObjectLateTick tick)
    {
        if (tick == null || !Application.isPlaying || applicationQuitting)
        {
            return;
        }

        EnsureInstance().AddLateTick(tick);
    }

    public static void UnregisterLateTick(IMapObjectLateTick tick)
    {
        if (tick == null || instance == null)
        {
            return;
        }

        instance.RemoveLateTick(tick);
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
        TickUpdateObjects(Time.deltaTime);
    }

    private void LateUpdate()
    {
        TickLateObjects(Time.deltaTime);
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
        if (updateTicksDirty)
        {
            CompactUpdateTicks();
        }

        if (tick == null || !updateTickSet.Add(tick))
        {
            return;
        }

        updateTicks.Add(tick);
        updateTickLastTimes[tick] = Time.time;
        enabled = true;
    }

    private void RemoveUpdateTick(IMapObjectUpdateTick tick)
    {
        if (tick == null || !updateTickSet.Remove(tick))
        {
            return;
        }

        updateTicksDirty = true;
        updateTickLastTimes.Remove(tick);
    }

    private void AddLateTick(IMapObjectLateTick tick)
    {
        if (lateTicksDirty)
        {
            CompactLateTicks();
        }

        if (tick == null || !lateTickSet.Add(tick))
        {
            return;
        }

        lateTicks.Add(tick);
        enabled = true;
    }

    private void RemoveLateTick(IMapObjectLateTick tick)
    {
        if (tick == null || !lateTickSet.Remove(tick))
        {
            return;
        }

        lateTicksDirty = true;
    }

    private void TickUpdateObjects(float deltaTime)
    {
        if (updateTicksDirty)
        {
            CompactUpdateTicks();
        }

        int count = updateTicks.Count;
        bool profileTicks = MapObjectTickProfiler.IsEnabled;
        if (profileTicks)
        {
            MapObjectTickProfiler.SetActiveTickCounts(count, lateTicks.Count);
        }
        if (count <= 0)
        {
            updateTickQuota = 0f;
            updateTickCursor = 0;
            RefreshEnabledState();
            return;
        }

        float safeInterval = Mathf.Max(0.001f, updateTickIntervalSeconds);
        float frameDeltaTime = Mathf.Max(0f, deltaTime);
        updateTickQuota += count * frameDeltaTime / safeInterval;

        int ticksToRun = Mathf.Min(count, Mathf.FloorToInt(updateTickQuota));
        if (ticksToRun <= 0)
        {
            RefreshEnabledState();
            return;
        }

        updateTickQuota -= ticksToRun;
        bool tickedEveryObject = ticksToRun >= count;
        float managedDeltaTime = tickedEveryObject
            ? frameDeltaTime
            : safeInterval;
        if (tickedEveryObject && updateTickQuota >= 1f)
        {
            updateTickQuota -= Mathf.Floor(updateTickQuota);
        }

        for (int processed = 0; processed < ticksToRun; processed++)
        {
            if (updateTickCursor >= updateTicks.Count)
            {
                updateTickCursor = 0;
            }

            IMapObjectUpdateTick tick = updateTicks[updateTickCursor];
            updateTickCursor++;
            if (!IsTickAlive(tick))
            {
                updateTickSet.Remove(tick);
                updateTickLastTimes.Remove(tick);
                updateTicksDirty = true;
                continue;
            }

            if (updateTicksDirty && !updateTickSet.Contains(tick))
            {
                continue;
            }

            float resolvedDeltaTime = ResolveUpdateTickDeltaTime(tick, managedDeltaTime);
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

        if (updateTicksDirty)
        {
            CompactUpdateTicks();
        }

        RefreshEnabledState();
    }

    private void TickLateObjects(float deltaTime)
    {
        if (lateTicksDirty)
        {
            CompactLateTicks();
        }

        int count = lateTicks.Count;
        bool profileTicks = MapObjectTickProfiler.IsEnabled;
        if (profileTicks)
        {
            MapObjectTickProfiler.SetActiveTickCounts(updateTicks.Count, count);
        }
        for (int i = 0; i < count; i++)
        {
            IMapObjectLateTick tick = lateTicks[i];
            if (!IsTickAlive(tick))
            {
                lateTickSet.Remove(tick);
                lateTicksDirty = true;
                continue;
            }

            if (lateTicksDirty && !lateTickSet.Contains(tick))
            {
                continue;
            }

            if (profileTicks)
            {
                long startTimestamp = MapObjectTickProfiler.BeginSample();
                tick.ManagedLateUpdateTick(deltaTime);
                MapObjectTickProfiler.EndLateSample(tick, startTimestamp);
            }
            else
            {
                tick.ManagedLateUpdateTick(deltaTime);
            }
        }

        if (lateTicksDirty)
        {
            CompactLateTicks();
        }

        RefreshEnabledState();
    }

    private void CompactUpdateTicks()
    {
        for (int i = updateTicks.Count - 1; i >= 0; i--)
        {
            IMapObjectUpdateTick tick = updateTicks[i];
            if (IsTickAlive(tick) && updateTickSet.Contains(tick))
            {
                continue;
            }

            updateTicks.RemoveAt(i);
            updateTickLastTimes.Remove(tick);
        }

        updateTicksDirty = false;
        if (updateTicks.Count <= 0)
        {
            updateTickCursor = 0;
            updateTickQuota = 0f;
        }
        else if (updateTickCursor >= updateTicks.Count)
        {
            updateTickCursor %= updateTicks.Count;
        }
    }

    private float ResolveUpdateTickDeltaTime(IMapObjectUpdateTick tick, float fallbackDeltaTime)
    {
        float now = Time.time;
        float safeFallback = Mathf.Max(0f, fallbackDeltaTime);
        if (tick == null)
        {
            return safeFallback;
        }

        if (!updateTickLastTimes.TryGetValue(tick, out float lastTime))
        {
            updateTickLastTimes[tick] = now;
            return safeFallback;
        }

        updateTickLastTimes[tick] = now;
        float elapsedTime = now - lastTime;
        return elapsedTime > 0f ? elapsedTime : safeFallback;
    }

    private void CompactLateTicks()
    {
        for (int i = lateTicks.Count - 1; i >= 0; i--)
        {
            IMapObjectLateTick tick = lateTicks[i];
            if (IsTickAlive(tick) && lateTickSet.Contains(tick))
            {
                continue;
            }

            lateTicks.RemoveAt(i);
        }

        lateTicksDirty = false;
    }

    private void RefreshEnabledState()
    {
        enabled = updateTicks.Count > 0 || lateTicks.Count > 0;
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
}

public static class MapObjectTickProfiler
{
    private const double MicrosecondsPerSecond = 1000000.0;
    private const int DefaultSnapshotMaxRows = 64;

    private static readonly Dictionary<string, GroupStats> groupStatsByKey = new Dictionary<string, GroupStats>(128);
    private static readonly Dictionary<object, TargetDescriptor> targetDescriptorByObject =
        new Dictionary<object, TargetDescriptor>(512);
    private static readonly List<GroupStats> snapshotRows = new List<GroupStats>(128);
    private static readonly StringBuilder jsonBuilder = new StringBuilder(8192);
    private static readonly double stopwatchTickToMicroseconds = MicrosecondsPerSecond / Stopwatch.Frequency;

    private static int activeUpdateTickCount;
    private static int activeLateTickCount;
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

    public static void EndLateSample(object target, long startTimestamp)
    {
        RecordSample("Late", target, startTimestamp);
    }

    public static void SetActiveTickCounts(int updateCount, int lateCount)
    {
        activeUpdateTickCount = Mathf.Max(0, updateCount);
        activeLateTickCount = Mathf.Max(0, lateCount);
    }

    public static void Reset()
    {
        groupStatsByKey.Clear();
        targetDescriptorByObject.Clear();
        snapshotRows.Clear();
        jsonBuilder.Length = 0;
        activeUpdateTickCount = 0;
        activeLateTickCount = 0;
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
            if (pair.Value.SampleCount > 0)
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
        AppendJsonProperty("activeLateTicks", activeLateTickCount.ToString(CultureInfo.InvariantCulture), true);
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

        if (!enabled)
        {
            targetDescriptorByObject.Clear();
        }

        return json;
    }

    private static void RecordSample(string kind, object target, long startTimestamp)
    {
        if (target == null)
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

        TargetDescriptor descriptor = ResolveTargetDescriptor(target);
        string key = string.Concat(kind, "|", descriptor.TypeName, "|", descriptor.ItemId.ToString(CultureInfo.InvariantCulture));
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
        public long SampleCount;
        public long TotalStopwatchTicks;
        public long MaxStopwatchTicks;
    }
}
