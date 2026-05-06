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
    private static MapObjectTickManager instance;
    private static bool applicationQuitting;

    private readonly List<IMapObjectUpdateTick> updateTicks = new List<IMapObjectUpdateTick>();
    private readonly HashSet<IMapObjectUpdateTick> updateTickSet = new HashSet<IMapObjectUpdateTick>();
    private readonly List<IMapObjectLateTick> lateTicks = new List<IMapObjectLateTick>();
    private readonly HashSet<IMapObjectLateTick> lateTickSet = new HashSet<IMapObjectLateTick>();
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
        if (tick == null || !updateTickSet.Add(tick))
        {
            return;
        }

        updateTicks.Add(tick);
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
        if (tick == null || !lateTickSet.Add(tick))
        {
            return;
        }

        lateTicks.Add(tick);
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
        int count = updateTicks.Count;
        for (int i = 0; i < count; i++)
        {
            IMapObjectUpdateTick tick = updateTicks[i];
            if (!IsTickAlive(tick) || !updateTickSet.Contains(tick))
            {
                updateTickSet.Remove(tick);
                updateTicksDirty = true;
                continue;
            }

            tick.ManagedUpdateTick(deltaTime);
        }

        if (updateTicksDirty)
        {
            CompactUpdateTicks();
        }
    }

    private void TickLateObjects(float deltaTime)
    {
        int count = lateTicks.Count;
        for (int i = 0; i < count; i++)
        {
            IMapObjectLateTick tick = lateTicks[i];
            if (!IsTickAlive(tick) || !lateTickSet.Contains(tick))
            {
                lateTickSet.Remove(tick);
                lateTicksDirty = true;
                continue;
            }

            tick.ManagedLateUpdateTick(deltaTime);
        }

        if (lateTicksDirty)
        {
            CompactLateTicks();
        }
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
