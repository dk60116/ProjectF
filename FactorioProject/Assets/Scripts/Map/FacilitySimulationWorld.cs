using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns fluid and powered-facility scheduling independently of each installation view.
/// Facility adapters still perform engine-facing IO, but only this data world is registered
/// with the global simulation clock.
/// </summary>
public sealed class FacilitySimulationWorld :
    IMapObjectUpdateTick,
    IMapObjectStagedUpdateTick,
    IMapObjectSimulationIdentity
{
    private static readonly Comparison<Entry> EntryComparison = CompareEntries;
    private static FacilitySimulationWorld current;

    private readonly HashSet<IMapObjectUpdateTick> registered = new HashSet<IMapObjectUpdateTick>();
    private readonly HashSet<IMapObjectUpdateTick> scheduled = new HashSet<IMapObjectUpdateTick>();
    private readonly Dictionary<IMapObjectUpdateTick, Entry> entriesByTarget =
        new Dictionary<IMapObjectUpdateTick, Entry>();
    private readonly List<Entry> entries = new List<Entry>(256);
    private readonly List<Entry> due = new List<Entry>(128);
    private bool orderDirty;
    private bool membershipDirty;
    private bool clockRegistered;
    private int lastDueCount;
    private int lastStagedCount;
    private int lastDirectCount;

    public long SimulationId => long.MaxValue - 30L;
    public int RegisteredCount => registered.Count;
    public int ScheduledCount => scheduled.Count;
    public int LastDueCount => lastDueCount;

    public static void Register(IMapObjectUpdateTick target, bool schedule = false)
    {
        if (target == null || !Application.isPlaying) return;
        Ensure().RegisterInternal(target, schedule);
    }

    public static void Unregister(IMapObjectUpdateTick target)
    {
        if (target == null || current == null) return;
        current.UnregisterInternal(target);
    }

    public static void SetScheduled(IMapObjectUpdateTick target, bool value)
    {
        if (target == null || !Application.isPlaying) return;
        FacilitySimulationWorld world = Ensure();
        if (!world.registered.Contains(target)) world.RegisterInternal(target, false);
        world.SetScheduledInternal(target, value);
    }

    public static bool IsScheduled(IMapObjectUpdateTick target)
    {
        return target != null && current != null && current.scheduled.Contains(target);
    }

    internal static void RestoreSimulationTick(long restoredTick)
    {
        current?.ResetSchedules(Math.Max(0L, restoredTick));
    }

    public static void AppendProfilerCounters()
    {
        FacilitySimulationWorld world = current;
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "ClockTargets",
            world != null && world.clockRegistered ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "RegisteredEntities",
            world != null ? world.RegisteredCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "ScheduledEntities",
            world != null ? world.ScheduledCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastDueEntities",
            world != null ? world.lastDueCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastStagedEntities",
            world != null ? world.lastStagedCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastDirectEntities",
            world != null ? world.lastDirectCount : 0);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        current?.Clear();
        current = null;
    }

    private static FacilitySimulationWorld Ensure()
    {
        return current ??= new FacilitySimulationWorld();
    }

    private void RegisterInternal(IMapObjectUpdateTick target, bool schedule)
    {
        if (registered.Add(target))
        {
            var entry = new Entry(
                target,
                ResolveIntervalTicks(target),
                MapObjectTickManager.CurrentSimulationTick);
            entriesByTarget.Add(target, entry);
            entries.Add(entry);
            orderDirty = true;
        }

        SetScheduledInternal(target, schedule);
    }

    private void UnregisterInternal(IMapObjectUpdateTick target)
    {
        if (!registered.Remove(target)) return;
        scheduled.Remove(target);
        entriesByTarget.Remove(target);
        membershipDirty = true;
        RefreshClockRegistration();
    }

    private void SetScheduledInternal(IMapObjectUpdateTick target, bool value)
    {
        if (!registered.Contains(target)) return;
        if (value)
        {
            if (!scheduled.Add(target)) return;
            if (entriesByTarget.TryGetValue(target, out Entry entry))
                entry.ResetSchedule(MapObjectTickManager.CurrentSimulationTick);
        }
        else if (!scheduled.Remove(target))
        {
            return;
        }

        RefreshClockRegistration();
    }

    private void RefreshClockRegistration()
    {
        bool shouldRegister = scheduled.Count > 0;
        if (clockRegistered == shouldRegister) return;
        clockRegistered = shouldRegister;
        if (shouldRegister) MapObjectTickManager.RegisterUpdateTick(this);
        else MapObjectTickManager.UnregisterUpdateTick(this);
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }

    public void PlanManagedUpdateTick(float deltaTime)
    {
        CompactEntries();
        if (orderDirty)
        {
            entries.Sort(EntryComparison);
            orderDirty = false;
        }

        due.Clear();
        lastDueCount = lastStagedCount = lastDirectCount = 0;
        long simulationTick = MapObjectTickManager.CurrentSimulationTick;
        bool hasPowerParticipant = false;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            IMapObjectUpdateTick target = entry.Target;
            if (!scheduled.Contains(target) || entry.NextDueTick > simulationTick) continue;
            long elapsedTicks = Math.Max(1L, simulationTick - entry.LastExecutedTick);
            entry.PendingDeltaTime = elapsedTicks * MapObjectTickManager.FixedSimulationDeltaSeconds;
            entry.MarkExecuted(simulationTick);
            due.Add(entry);
            hasPowerParticipant |= target is InputOutputModule module
                                   && module.RequiresFacilityPowerEvaluation;
        }

        lastDueCount = due.Count;
        if (due.Count == 0) return;
        using var sample = MapObjectTickProfiler.SampleNamed(
            "ECS",
            nameof(FacilitySimulationWorld),
            "Facility ECS Plan");
        for (int i = 0; i < due.Count; i++)
        {
            IMapObjectUpdateTick target = due[i].Target;
            if (!scheduled.Contains(target) || target is not IMapObjectStagedUpdateTick staged) continue;
            staged.PlanManagedUpdateTick(due[i].PendingDeltaTime);
            lastStagedCount++;
        }
        // Publish one immutable supply snapshot after every due facility has planned,
        // before any facility or robot arm commits its work for this tick.
        if (hasPowerParticipant) UtilityPole.PrepareSimulationPowerTick();
    }

    public void ApplyManagedUpdateTick()
    {
        if (due.Count == 0) return;
        using var sample = MapObjectTickProfiler.SampleNamed(
            "ECS",
            nameof(FacilitySimulationWorld),
            "Facility ECS Apply");
        for (int i = 0; i < due.Count; i++)
        {
            Entry entry = due[i];
            IMapObjectUpdateTick target = entry.Target;
            if (!registered.Contains(target) || !scheduled.Contains(target)) continue;
            if (target is IMapObjectStagedUpdateTick staged)
            {
                staged.ApplyManagedUpdateTick();
            }
            else
            {
                target.ManagedUpdateTick(entry.PendingDeltaTime);
                lastDirectCount++;
            }
        }

        due.Clear();
        CompactEntries();
    }

    private void CompactEntries()
    {
        bool removedInvalidTarget = RemoveInvalidTargets();
        if (!membershipDirty && !removedInvalidTarget) return;
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < entries.Count; readIndex++)
        {
            Entry entry = entries[readIndex];
            if (entry == null || !registered.Contains(entry.Target)) continue;
            entries[writeIndex++] = entry;
        }

        if (writeIndex < entries.Count) entries.RemoveRange(writeIndex, entries.Count - writeIndex);
        membershipDirty = false;
        orderDirty = true;
        RefreshClockRegistration();
    }

    private bool RemoveInvalidTargets()
    {
        bool removed = false;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            IMapObjectUpdateTick target = entries[i]?.Target;
            if (IsAlive(target)) continue;
            if (target != null)
            {
                registered.Remove(target);
                scheduled.Remove(target);
                entriesByTarget.Remove(target);
            }
            removed = true;
        }
        return removed;
    }

    private static bool IsAlive(IMapObjectUpdateTick target)
    {
        if (target == null) return false;
        UnityEngine.Object unityObject = target as UnityEngine.Object;
        return ReferenceEquals(unityObject, null) || unityObject != null;
    }

    private void Clear()
    {
        if (clockRegistered) MapObjectTickManager.UnregisterUpdateTick(this);
        clockRegistered = false;
        registered.Clear();
        scheduled.Clear();
        entriesByTarget.Clear();
        entries.Clear();
        due.Clear();
        orderDirty = membershipDirty = false;
        lastDueCount = lastStagedCount = lastDirectCount = 0;
    }

    private void ResetSchedules(long simulationTick)
    {
        due.Clear();
        for (int i = 0; i < entries.Count; i++) entries[i]?.ResetSchedule(simulationTick);
        lastDueCount = lastStagedCount = lastDirectCount = 0;
    }

    private static int ResolveIntervalTicks(IMapObjectUpdateTick target)
    {
        float intervalSeconds = target is IMapObjectUpdateTickInterval interval
            ? interval.ManagedUpdateTickIntervalSeconds
            : MapObjectTickManager.FixedSimulationDeltaSeconds;
        if (float.IsNaN(intervalSeconds) || intervalSeconds <= MapObjectTickManager.FixedSimulationDeltaSeconds)
            return 1;
        double ticks = intervalSeconds / MapObjectTickManager.FixedSimulationDeltaSeconds;
        return ticks >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Round(ticks, MidpointRounding.ToEven));
    }

    private static int CompareEntries(Entry left, Entry right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return 1;
        if (right == null) return -1;
        long leftId = left.Target is IMapObjectSimulationIdentity leftIdentity ? leftIdentity.SimulationId : 0L;
        long rightId = right.Target is IMapObjectSimulationIdentity rightIdentity ? rightIdentity.SimulationId : 0L;
        int comparison = leftId.CompareTo(rightId);
        if (comparison != 0) return comparison;
        return string.CompareOrdinal(
            left.Target?.GetType().FullName ?? string.Empty,
            right.Target?.GetType().FullName ?? string.Empty);
    }

    private sealed class Entry
    {
        public readonly IMapObjectUpdateTick Target;
        public readonly int IntervalTicks;
        public long LastExecutedTick;
        public long NextDueTick;
        public float PendingDeltaTime;

        public Entry(IMapObjectUpdateTick target, int intervalTicks, long currentTick)
        {
            Target = target;
            IntervalTicks = Math.Max(1, intervalTicks);
            ResetSchedule(currentTick);
        }

        public void ResetSchedule(long currentTick)
        {
            LastExecutedTick = currentTick;
            NextDueTick = currentTick + 1L;
            PendingDeltaTime = 0f;
        }

        public void MarkExecuted(long currentTick)
        {
            LastExecutedTick = currentTick;
            NextDueTick = currentTick + IntervalTicks;
        }
    }
}
