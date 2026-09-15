using System;
using System.Collections.Generic;
using ProjectF.Simulation;
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
    private readonly Dictionary<long, List<Entry>> dueBuckets =
        new Dictionary<long, List<Entry>>(64);
    private readonly Stack<List<Entry>> dueBucketPool = new Stack<List<Entry>>(16);
    private readonly FacilityFlowBatch flowBatch = new FacilityFlowBatch(128);
    private readonly Dictionary<Type, FacilityTypeProfile> typeProfiles =
        new Dictionary<Type, FacilityTypeProfile>();
    private readonly List<FacilityTypeProfile> typeProfilesInOrder =
        new List<FacilityTypeProfile>(16);
    private readonly List<FacilityTypeProfile> touchedTypeProfiles =
        new List<FacilityTypeProfile>(16);
    private bool membershipDirty;
    private bool clockRegistered;
    private int lastDueCount;
    private int lastScheduleCandidateCount;
    private int lastFlowCount;
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

    public static void RefreshSchedule(IMapObjectUpdateTick target)
    {
        if (target == null || current == null) return;
        current.RefreshScheduleInternal(target);
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
            "ScheduledTickBuckets",
            world != null ? world.dueBuckets.Count : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastScheduleCandidates",
            world != null ? world.lastScheduleCandidateCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastFlowEntities",
            world != null ? world.lastFlowCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastStagedEntities",
            world != null ? world.lastStagedCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastDirectEntities",
            world != null ? world.lastDirectCount : 0);
        if (world != null)
        {
            for (int i = 0; i < world.typeProfilesInOrder.Count; i++)
            {
                FacilityTypeProfile profile = world.typeProfilesInOrder[i];
                MapObjectTickProfiler.AddRuntimeCounter(
                    "FacilityDueByType",
                    profile.TypeName,
                    profile.LastApplyCount);
            }
        }
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
        if (registered.Count == 0) CompactEntries();
    }

    private void SetScheduledInternal(IMapObjectUpdateTick target, bool value)
    {
        if (!registered.Contains(target)) return;
        if (value)
        {
            if (!scheduled.Add(target)) return;
            if (entriesByTarget.TryGetValue(target, out Entry entry))
            {
                entry.ResetSchedule(MapObjectTickManager.CurrentSimulationTick);
                ScheduleEntry(entry);
            }
        }
        else if (!scheduled.Remove(target))
        {
            return;
        }

        RefreshClockRegistration();
    }

    private void RefreshScheduleInternal(IMapObjectUpdateTick target)
    {
        if (!entriesByTarget.TryGetValue(target, out Entry entry)) return;
        entry.ResetSchedule(MapObjectTickManager.CurrentSimulationTick);
        if (scheduled.Contains(target)) ScheduleEntry(entry);
    }

    private void RefreshClockRegistration()
    {
        bool shouldRegister = scheduled.Count > 0;
        if (clockRegistered == shouldRegister) return;
        clockRegistered = shouldRegister;
        if (shouldRegister) MapObjectTickManager.RegisterUpdateTick(this);
        else
        {
            ClearDueBuckets();
            MapObjectTickManager.UnregisterUpdateTick(this);
        }
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        PlanManagedUpdateTick(deltaTime);
        ApplyManagedUpdateTick();
    }

    public void PlanManagedUpdateTick(float deltaTime)
    {
        CompactEntries();
        due.Clear();
        flowBatch.Begin();
        lastDueCount = lastScheduleCandidateCount = lastFlowCount = lastStagedCount = lastDirectCount = 0;
        long simulationTick = MapObjectTickManager.CurrentSimulationTick;
        bool hasPowerParticipant = false;
        if (dueBuckets.Remove(simulationTick, out List<Entry> bucket))
        {
            lastScheduleCandidateCount = bucket.Count;
            for (int i = 0; i < bucket.Count; i++)
            {
                Entry entry = bucket[i];
                IMapObjectUpdateTick target = entry?.Target;
                if (!IsAlive(target))
                {
                    RemoveInvalidTarget(target);
                    continue;
                }
                if (!registered.Contains(target)
                    || !scheduled.Contains(target)
                    || entry.NextDueTick != simulationTick)
                    continue;

                long elapsedTicks = Math.Max(1L, simulationTick - entry.LastExecutedTick);
                entry.PendingDeltaTime = elapsedTicks * MapObjectTickManager.FixedSimulationDeltaSeconds;
                entry.MarkExecuted(simulationTick);
                ScheduleEntry(entry);
                due.Add(entry);
                hasPowerParticipant |= target is InputOutputModule module
                                       && module.RequiresFacilityPowerEvaluation;
            }
            ReturnDueBucket(bucket);
        }

        if (membershipDirty) CompactEntries();
        if (due.Count > 1) due.Sort(EntryComparison);
        lastDueCount = due.Count;
        if (due.Count == 0) return;
        using var sample = MapObjectTickProfiler.SampleNamed(
            "ECS",
            nameof(FacilitySimulationWorld),
            "Facility ECS Plan");
        for (int i = 0; i < due.Count; i++)
        {
            Entry entry = due[i];
            IMapObjectUpdateTick target = entry.Target;
            entry.PendingFlowIndex = -1;
            if (!scheduled.Contains(target) || target is not IMapObjectStagedUpdateTick staged) continue;
            staged.PlanManagedUpdateTick(entry.PendingDeltaTime);
            lastStagedCount++;
            if (target is IFacilityFlowAdapter flowAdapter)
            {
                int flowIndex = flowBatch.ReserveSlot();
                entry.PendingFlowIndex = flowIndex;
                flowAdapter.CaptureFacilityFlow(
                    flowBatch,
                    flowIndex,
                    entry.PendingDeltaTime,
                    simulationTick);
            }
        }
        flowBatch.PlanAll();
        lastFlowCount = flowBatch.Count;
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
        bool profileTypes = MapObjectTickProfiler.IsEnabled;
        touchedTypeProfiles.Clear();
        if (profileTypes)
        {
            for (int i = 0; i < typeProfilesInOrder.Count; i++)
                typeProfilesInOrder[i].LastApplyCount = 0;
        }
        UtilityPole.BeginSimulationPowerMutationBatch();
        try
        {
            for (int i = 0; i < due.Count; i++)
            {
                Entry entry = due[i];
                IMapObjectUpdateTick target = entry.Target;
                if (!registered.Contains(target) || !scheduled.Contains(target)) continue;
                if (!profileTypes)
                {
                    ApplyTarget(entry, target);
                    continue;
                }

                FacilityTypeProfile profile = entry.TypeProfile ??=
                    GetOrCreateTypeProfile(target.GetType());
                if (!profile.Touched)
                {
                    profile.Touched = true;
                    profile.ElapsedTimestampTicks = 0L;
                    profile.CurrentApplyCount = 0;
                    touchedTypeProfiles.Add(profile);
                }

                long startTimestamp = MapObjectTickProfiler.BeginSample();
                ApplyTarget(entry, target);
                profile.ElapsedTimestampTicks += Math.Max(
                    0L,
                    MapObjectTickProfiler.BeginSample() - startTimestamp);
                profile.CurrentApplyCount++;
            }
        }
        finally
        {
            UtilityPole.EndSimulationPowerMutationBatch();
        }

        FlushTypeProfiles();

        due.Clear();
        CompactEntries();
    }

    private void ApplyTarget(Entry entry, IMapObjectUpdateTick target)
    {
        if (entry.PendingFlowIndex >= 0 && target is IFacilityFlowAdapter flowAdapter)
        {
            flowAdapter.ApplyFacilityFlow(flowBatch, entry.PendingFlowIndex);
            (target as IPersistenceDirtyTrackable)?.MarkPersistenceStateDirty();
            return;
        }

        if (target is IMapObjectStagedUpdateTick staged)
        {
            staged.ApplyManagedUpdateTick();
            (target as IPersistenceDirtyTrackable)?.MarkPersistenceStateDirty();
            return;
        }

        target.ManagedUpdateTick(entry.PendingDeltaTime);
        (target as IPersistenceDirtyTrackable)?.MarkPersistenceStateDirty();
        lastDirectCount++;
    }

    private FacilityTypeProfile GetOrCreateTypeProfile(Type type)
    {
        if (typeProfiles.TryGetValue(type, out FacilityTypeProfile profile)) return profile;
        profile = new FacilityTypeProfile(type);
        typeProfiles.Add(type, profile);
        typeProfilesInOrder.Add(profile);
        return profile;
    }

    private void FlushTypeProfiles()
    {
        for (int i = 0; i < touchedTypeProfiles.Count; i++)
        {
            FacilityTypeProfile profile = touchedTypeProfiles[i];
            profile.LastApplyCount = profile.CurrentApplyCount;
            profile.Touched = false;
            MapObjectTickProfiler.RecordNamedElapsedTicks(
                "Facility Type",
                profile.TypeName,
                profile.ApplyItemName,
                profile.ElapsedTimestampTicks);
        }
        touchedTypeProfiles.Clear();
    }

    private void CompactEntries()
    {
        if (!membershipDirty) return;
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < entries.Count; readIndex++)
        {
            Entry entry = entries[readIndex];
            if (entry == null || !registered.Contains(entry.Target)) continue;
            entries[writeIndex++] = entry;
        }

        if (writeIndex < entries.Count) entries.RemoveRange(writeIndex, entries.Count - writeIndex);
        membershipDirty = false;
        RefreshClockRegistration();
    }

    private void RemoveInvalidTarget(IMapObjectUpdateTick target)
    {
        if (target != null)
        {
            registered.Remove(target);
            scheduled.Remove(target);
            entriesByTarget.Remove(target);
        }
        membershipDirty = true;
    }

    private void ScheduleEntry(Entry entry)
    {
        if (entry == null || entry.NextDueTick == long.MaxValue) return;
        if (!dueBuckets.TryGetValue(entry.NextDueTick, out List<Entry> bucket))
        {
            bucket = dueBucketPool.Count > 0 ? dueBucketPool.Pop() : new List<Entry>(16);
            dueBuckets.Add(entry.NextDueTick, bucket);
        }
        bucket.Add(entry);
    }

    private void ReturnDueBucket(List<Entry> bucket)
    {
        bucket.Clear();
        dueBucketPool.Push(bucket);
    }

    private void ClearDueBuckets()
    {
        foreach (KeyValuePair<long, List<Entry>> pair in dueBuckets)
            ReturnDueBucket(pair.Value);
        dueBuckets.Clear();
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
        ClearDueBuckets();
        typeProfiles.Clear();
        typeProfilesInOrder.Clear();
        touchedTypeProfiles.Clear();
        membershipDirty = false;
        lastDueCount = lastScheduleCandidateCount = lastFlowCount = lastStagedCount = lastDirectCount = 0;
    }

    private void ResetSchedules(long simulationTick)
    {
        due.Clear();
        ClearDueBuckets();
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            entry?.ResetSchedule(simulationTick);
            if (entry != null && scheduled.Contains(entry.Target)) ScheduleEntry(entry);
        }
        lastDueCount = lastScheduleCandidateCount = lastFlowCount = lastStagedCount = lastDirectCount = 0;
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
        public int PendingFlowIndex = -1;
        public FacilityTypeProfile TypeProfile;

        public Entry(IMapObjectUpdateTick target, int intervalTicks, long currentTick)
        {
            Target = target;
            IntervalTicks = Math.Max(1, intervalTicks);
            ResetSchedule(currentTick);
        }

        public void ResetSchedule(long currentTick)
        {
            LastExecutedTick = currentTick;
            NextDueTick = ResolveNextDueTick(Target, currentTick, IntervalTicks);
            PendingDeltaTime = 0f;
        }

        public void MarkExecuted(long currentTick)
        {
            LastExecutedTick = currentTick;
            NextDueTick = currentTick > long.MaxValue - IntervalTicks
                ? long.MaxValue
                : currentTick + IntervalTicks;
        }
    }

    private sealed class FacilityTypeProfile
    {
        public readonly string TypeName;
        public readonly string ApplyItemName;
        public bool Touched;
        public long ElapsedTimestampTicks;
        public int CurrentApplyCount;
        public int LastApplyCount;

        public FacilityTypeProfile(Type type)
        {
            TypeName = type != null ? type.Name : "Unknown";
            ApplyItemName = TypeName + " Apply";
        }
    }

    private static long ResolveNextDueTick(
        IMapObjectUpdateTick target,
        long currentTick,
        int intervalTicks)
    {
        long firstCandidate = currentTick >= long.MaxValue ? long.MaxValue : currentTick + 1L;
        if (intervalTicks <= 1 || firstCandidate == long.MaxValue) return firstCandidate;

        long simulationId = target is IMapObjectSimulationIdentity identity
            ? identity.SimulationId
            : 0L;
        long phase = PositiveModulo(simulationId, intervalTicks);
        long candidatePhase = PositiveModulo(firstCandidate, intervalTicks);
        long offset = phase - candidatePhase;
        if (offset < 0L) offset += intervalTicks;
        return firstCandidate > long.MaxValue - offset ? long.MaxValue : firstCandidate + offset;
    }

    private static long PositiveModulo(long value, int divisor)
    {
        long remainder = value % divisor;
        return remainder >= 0L ? remainder : remainder + divisor;
    }
}
