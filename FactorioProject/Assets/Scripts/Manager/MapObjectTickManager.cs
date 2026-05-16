using System.Collections.Generic;
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
    private const float MaxUpdateTickFrameDeltaSeconds = 0.12f;

    private static MapObjectTickManager instance;
    private static bool applicationQuitting;

    [SerializeField, Min(0.001f)]
    private float updateTickIntervalSeconds = DefaultUpdateTickIntervalSeconds;

    private readonly List<IMapObjectUpdateTick> updateTicks = new List<IMapObjectUpdateTick>();
    private readonly HashSet<IMapObjectUpdateTick> updateTickSet = new HashSet<IMapObjectUpdateTick>();
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
        if (count <= 0)
        {
            updateTickQuota = 0f;
            updateTickCursor = 0;
            RefreshEnabledState();
            return;
        }

        float safeInterval = Mathf.Max(0.001f, updateTickIntervalSeconds);
        float clampedDeltaTime = Mathf.Clamp(deltaTime, 0f, MaxUpdateTickFrameDeltaSeconds);
        updateTickQuota += count * clampedDeltaTime / safeInterval;

        int ticksToRun = Mathf.Min(count, Mathf.FloorToInt(updateTickQuota));
        if (ticksToRun <= 0)
        {
            RefreshEnabledState();
            return;
        }

        updateTickQuota -= ticksToRun;
        if (ticksToRun >= count && updateTickQuota >= 1f)
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
                updateTicksDirty = true;
                continue;
            }

            if (updateTicksDirty && !updateTickSet.Contains(tick))
            {
                continue;
            }

            tick.ManagedUpdateTick(safeInterval);
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

            tick.ManagedLateUpdateTick(deltaTime);
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

        Object unityObject = tick as Object;
        return ReferenceEquals(unityObject, null) || unityObject != null;
    }
}
