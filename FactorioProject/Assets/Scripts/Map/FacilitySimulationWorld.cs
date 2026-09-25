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
    private static FacilitySimulationWorld current;
    private static IFacilityFlowParallelPlanner parallelFlowPlanner;

    private readonly Dictionary<IMapObjectUpdateTick, int> entryIndexByTarget =
        new Dictionary<IMapObjectUpdateTick, int>();
    private Entry[] entries = new Entry[256];
    private readonly List<int> due = new List<int>(128);
    private readonly Dictionary<long, List<int>> dueBuckets =
        new Dictionary<long, List<int>>(64);
    private readonly Stack<List<int>> dueBucketPool = new Stack<List<int>>(16);
    private readonly Comparison<int> dueEntryIndexComparison;
    private readonly FacilityFlowBatch flowBatch = new FacilityFlowBatch(128);
    private readonly Dictionary<Type, FacilityTypeProfile> typeProfiles =
        new Dictionary<Type, FacilityTypeProfile>();
    private readonly List<FacilityTypeProfile> typeProfilesInOrder =
        new List<FacilityTypeProfile>(16);
    private readonly List<FacilityTypeProfile> touchedTypeProfiles =
        new List<FacilityTypeProfile>(16);
    private bool membershipDirty;
    private bool clockRegistered;
    private int entryCount;
    private int registeredCount;
    private int scheduledCount;
    private int lastDueCount;
    private int lastScheduleCandidateCount;
    private int lastFlowCount;
    private int lastPumpFlowCount;
    private int lastBoilerFlowCount;
    private int lastSteamGeneratorFlowCount;
    private int lastParallelPlanCount;
    private int lastParallelPlanJobCount;
    private bool parallelPlanPending;
    private int lastStagedCount;
    private int lastDirectCount;

    public long SimulationId => long.MaxValue - 30L;
    public int RegisteredCount => registeredCount;
    public int ScheduledCount => scheduledCount;
    public int LastDueCount => lastDueCount;

    private FacilitySimulationWorld()
    {
        dueEntryIndexComparison = CompareEntryIndices;
    }

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
        if (!world.entryIndexByTarget.ContainsKey(target)) world.RegisterInternal(target, false);
        world.SetScheduledInternal(target, value);
    }

    public static bool IsScheduled(IMapObjectUpdateTick target)
    {
        return target != null && current != null && current.IsScheduledInternal(target);
    }

    public static void RefreshSchedule(IMapObjectUpdateTick target)
    {
        if (target == null || current == null) return;
        current.RefreshScheduleInternal(target);
    }

    public static void SetParallelFlowPlanner(IFacilityFlowParallelPlanner planner)
    {
        if (ReferenceEquals(parallelFlowPlanner, planner)) return;
        current?.CompleteParallelFlowPlan();
        parallelFlowPlanner?.Dispose();
        parallelFlowPlanner = planner;
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
            "LastPumpFlowEntities",
            world != null ? world.lastPumpFlowCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastBoilerFlowEntities",
            world != null ? world.lastBoilerFlowCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastSteamGeneratorFlowEntities",
            world != null ? world.lastSteamGeneratorFlowCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "ParallelPlannerInstalled",
            parallelFlowPlanner != null ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastParallelPlanEntities",
            world != null ? world.lastParallelPlanCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastParallelPlanJobs",
            world != null ? world.lastParallelPlanJobCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "ParallelPlanPending",
            world != null && world.parallelPlanPending ? 1 : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastStagedEntities",
            world != null ? world.lastStagedCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityECS",
            "LastDirectEntities",
            world != null ? world.lastDirectCount : 0);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityStateECS",
            "PumpEntities",
            FacilityFlowStateWorld.PumpCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityStateECS",
            "BoilerEntities",
            FacilityFlowStateWorld.BoilerCount);
        MapObjectTickProfiler.AddRuntimeCounter(
            "FacilityStateECS",
            "SteamGeneratorEntities",
            FacilityFlowStateWorld.SteamGeneratorCount);
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
        SetParallelFlowPlanner(null);
        FacilityFlowStateWorld.Clear();
    }

    private static FacilitySimulationWorld Ensure()
    {
        return current ??= new FacilitySimulationWorld();
    }

    private void RegisterInternal(IMapObjectUpdateTick target, bool schedule)
    {
        if (!entryIndexByTarget.TryGetValue(target, out int entryIndex))
        {
            EnsureEntryCapacity(entryCount + 1);
            entryIndex = entryCount++;
            entries[entryIndex] = new Entry(
                target,
                ResolveIntervalTicks(target),
                MapObjectTickManager.CurrentSimulationTick);
            entryIndexByTarget.Add(target, entryIndex);
            registeredCount++;
        }

        SetScheduledInternal(entryIndex, schedule);
    }

    private void UnregisterInternal(IMapObjectUpdateTick target)
    {
        if (!entryIndexByTarget.TryGetValue(target, out int entryIndex)) return;
        entryIndexByTarget.Remove(target);
        ref Entry entry = ref entries[entryIndex];
        if (!entry.Registered) return;
        if (entry.Scheduled)
        {
            entry.Scheduled = false;
            scheduledCount--;
        }
        entry.Registered = false;
        registeredCount--;
        membershipDirty = true;
        RefreshClockRegistration();
        if (registeredCount == 0 && due.Count == 0) CompactEntries();
    }

    private void SetScheduledInternal(IMapObjectUpdateTick target, bool value)
    {
        if (!entryIndexByTarget.TryGetValue(target, out int entryIndex)) return;
        SetScheduledInternal(entryIndex, value);
    }

    private void SetScheduledInternal(int entryIndex, bool value)
    {
        ref Entry entry = ref entries[entryIndex];
        if (!entry.Registered || entry.Scheduled == value) return;
        entry.Scheduled = value;
        if (value)
        {
            scheduledCount++;
            entry.ResetSchedule(MapObjectTickManager.CurrentSimulationTick);
            ScheduleEntry(entryIndex);
        }
        else
        {
            scheduledCount--;
        }

        RefreshClockRegistration();
    }

    private void RefreshScheduleInternal(IMapObjectUpdateTick target)
    {
        if (!entryIndexByTarget.TryGetValue(target, out int entryIndex)) return;
        ref Entry entry = ref entries[entryIndex];
        entry.ResetSchedule(MapObjectTickManager.CurrentSimulationTick);
        if (entry.Scheduled) ScheduleEntry(entryIndex);
    }

    private bool IsScheduledInternal(IMapObjectUpdateTick target)
    {
        return entryIndexByTarget.TryGetValue(target, out int entryIndex)
               && entries[entryIndex].Registered
               && entries[entryIndex].Scheduled;
    }

    private void RefreshClockRegistration()
    {
        bool shouldRegister = scheduledCount > 0;
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
        CompleteParallelFlowPlan();
        CompactEntries();
        due.Clear();
        flowBatch.Begin();
        lastDueCount = lastScheduleCandidateCount = lastFlowCount = lastStagedCount = lastDirectCount = 0;
        lastPumpFlowCount = lastBoilerFlowCount = lastSteamGeneratorFlowCount = 0;
        lastParallelPlanCount = 0;
        lastParallelPlanJobCount = 0;
        long simulationTick = MapObjectTickManager.CurrentSimulationTick;
        bool hasPowerParticipant = false;
        if (dueBuckets.TryGetValue(simulationTick, out List<int> bucket))
        {
            dueBuckets.Remove(simulationTick);
            lastScheduleCandidateCount = bucket.Count;
            for (int i = 0; i < bucket.Count; i++)
            {
                int entryIndex = bucket[i];
                if ((uint)entryIndex >= (uint)entryCount)
                {
                    continue;
                }

                ref Entry entry = ref entries[entryIndex];
                IMapObjectUpdateTick target = entry.Target;
                if (!IsAlive(target))
                {
                    RemoveInvalidEntry(entryIndex);
                    continue;
                }
                if (!entry.Registered
                    || !entry.Scheduled
                    || entry.NextDueTick != simulationTick)
                    continue;

                long elapsedTicks = Math.Max(1L, simulationTick - entry.LastExecutedTick);
                entry.PendingDeltaTime = elapsedTicks * MapObjectTickManager.FixedSimulationDeltaSeconds;
                entry.MarkExecuted(simulationTick);
                ScheduleEntry(entryIndex);
                due.Add(entryIndex);
                hasPowerParticipant |= entry.RequiresPowerEvaluation;
            }
            ReturnDueBucket(bucket);
        }

        if (membershipDirty && due.Count == 0) CompactEntries();
        if (due.Count > 1) due.Sort(dueEntryIndexComparison);
        lastDueCount = due.Count;
        if (due.Count == 0) return;
        using var sample = MapObjectTickProfiler.SampleNamed(
            "ECS",
            nameof(FacilitySimulationWorld),
            "Facility ECS Plan");
        for (int i = 0; i < due.Count; i++)
        {
            int entryIndex = due[i];
            ref Entry entry = ref entries[entryIndex];
            IMapObjectUpdateTick target = entry.Target;
            entry.PendingFlowIndex = -1;
            if (!entry.Registered || !entry.Scheduled || entry.Staged == null) continue;
            entry.Staged.PlanManagedUpdateTick(entry.PendingDeltaTime);
            lastStagedCount++;
            if (entry.FlowAdapter != null)
            {
                int flowIndex = flowBatch.ReserveSlot();
                entry.PendingFlowIndex = flowIndex;
                entry.FlowAdapter.CaptureFacilityFlow(
                    flowBatch,
                    flowIndex,
                    entry.PendingDeltaTime,
                    simulationTick);
            }
        }
        bool plannedInParallel = parallelFlowPlanner != null
                                 && parallelFlowPlanner.TrySchedule(flowBatch);
        parallelPlanPending = plannedInParallel;
        if (!plannedInParallel)
        {
            flowBatch.PlanAll();
        }
        lastParallelPlanCount = plannedInParallel
            ? parallelFlowPlanner.LastParallelEntityCount
            : 0;
        lastParallelPlanJobCount = plannedInParallel
            ? parallelFlowPlanner.LastScheduledJobCount
            : 0;
        lastFlowCount = flowBatch.Count;
        lastPumpFlowCount = flowBatch.PumpCount;
        lastBoilerFlowCount = flowBatch.BoilerCount;
        lastSteamGeneratorFlowCount = flowBatch.SteamGeneratorCount;
        // Publish one immutable supply snapshot after every due facility has planned,
        // before any facility or robot arm commits its work for this tick.
        if (hasPowerParticipant) UtilityPole.PrepareSimulationPowerTick();
    }

    public void ApplyManagedUpdateTick()
    {
        CompleteParallelFlowPlan();
        if (due.Count == 0) return;
        using var sample = MapObjectTickProfiler.SampleNamed(
            "ECS",
            nameof(FacilitySimulationWorld),
            "Facility ECS Apply");
        bool profileTypes = MapObjectTickProfiler.IsDetailedEnabled;
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
                int entryIndex = due[i];
                ref Entry entry = ref entries[entryIndex];
                IMapObjectUpdateTick target = entry.Target;
                if (!entry.Registered || !entry.Scheduled) continue;
                if (!profileTypes)
                {
                    ApplyTarget(ref entry, target);
                    continue;
                }

                FacilityTypeProfile profile = GetOrCreateTypeProfile(target.GetType());
                if (!profile.Touched)
                {
                    profile.Touched = true;
                    profile.ElapsedTimestampTicks = 0L;
                    profile.CurrentApplyCount = 0;
                    touchedTypeProfiles.Add(profile);
                }

                long startTimestamp = MapObjectTickProfiler.BeginSample();
                ApplyTarget(ref entry, target);
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

    private void CompleteParallelFlowPlan()
    {
        if (!parallelPlanPending) return;
        using var sample = MapObjectTickProfiler.SampleNamed(
            "ECS",
            nameof(FacilitySimulationWorld),
            "Facility ECS Job Complete");
        parallelFlowPlanner?.Complete(flowBatch);
        parallelPlanPending = false;
    }

    private void ApplyTarget(ref Entry entry, IMapObjectUpdateTick target)
    {
        InstallationObject installationObject = target as InstallationObject;
        bool previouslyHadElectricDemand = UtilityPole.TryCaptureElectricPowerDemand(
            installationObject,
            out float previousElectricDemandWatts);
        try
        {
            if (entry.PendingFlowIndex >= 0 && entry.FlowAdapter != null)
            {
                entry.FlowAdapter.ApplyFacilityFlow(flowBatch, entry.PendingFlowIndex);
                entry.Persistence?.MarkPersistenceStateDirty();
                return;
            }

            if (entry.Staged != null)
            {
                entry.Staged.ApplyManagedUpdateTick();
                entry.Persistence?.MarkPersistenceStateDirty();
                return;
            }

            target.ManagedUpdateTick(entry.PendingDeltaTime);
            entry.Persistence?.MarkPersistenceStateDirty();
            lastDirectCount++;
        }
        finally
        {
            UtilityPole.NotifyElectricPowerConsumerStateChangedIfNeeded(
                installationObject,
                previouslyHadElectricDemand,
                previousElectricDemandWatts);
        }
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
        due.Clear();
        ClearDueBuckets();
        entryIndexByTarget.Clear();
        int writeIndex = 0;
        int nextScheduledCount = 0;
        for (int readIndex = 0; readIndex < entryCount; readIndex++)
        {
            Entry entry = entries[readIndex];
            if (!entry.Registered || !IsAlive(entry.Target)) continue;
            entries[writeIndex] = entry;
            entryIndexByTarget[entry.Target] = writeIndex;
            if (entry.Scheduled)
            {
                nextScheduledCount++;
                ScheduleEntry(writeIndex);
            }
            writeIndex++;
        }

        if (writeIndex < entryCount)
        {
            Array.Clear(entries, writeIndex, entryCount - writeIndex);
        }

        entryCount = writeIndex;
        registeredCount = writeIndex;
        scheduledCount = nextScheduledCount;
        membershipDirty = false;
        RefreshClockRegistration();
    }

    private void RemoveInvalidEntry(int entryIndex)
    {
        if ((uint)entryIndex >= (uint)entryCount) return;
        ref Entry entry = ref entries[entryIndex];
        if (!entry.Registered) return;
        IMapObjectUpdateTick target = entry.Target;
        if (!ReferenceEquals(target, null))
        {
            entryIndexByTarget.Remove(target);
        }

        if (entry.Scheduled)
        {
            entry.Scheduled = false;
            scheduledCount--;
        }
        entry.Registered = false;
        registeredCount--;
        membershipDirty = true;
    }

    private void ScheduleEntry(int entryIndex)
    {
        ref Entry entry = ref entries[entryIndex];
        if (!entry.Registered || !entry.Scheduled || entry.NextDueTick == long.MaxValue) return;
        if (!dueBuckets.TryGetValue(entry.NextDueTick, out List<int> bucket))
        {
            bucket = dueBucketPool.Count > 0 ? dueBucketPool.Pop() : new List<int>(16);
            dueBuckets.Add(entry.NextDueTick, bucket);
        }
        bucket.Add(entryIndex);
    }

    private void ReturnDueBucket(List<int> bucket)
    {
        bucket.Clear();
        dueBucketPool.Push(bucket);
    }

    private void ClearDueBuckets()
    {
        foreach (KeyValuePair<long, List<int>> pair in dueBuckets)
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
        CompleteParallelFlowPlan();
        if (clockRegistered) MapObjectTickManager.UnregisterUpdateTick(this);
        clockRegistered = false;
        entryIndexByTarget.Clear();
        Array.Clear(entries, 0, entryCount);
        entryCount = registeredCount = scheduledCount = 0;
        due.Clear();
        ClearDueBuckets();
        typeProfiles.Clear();
        typeProfilesInOrder.Clear();
        touchedTypeProfiles.Clear();
        membershipDirty = false;
        lastDueCount = lastScheduleCandidateCount = lastFlowCount = lastStagedCount = lastDirectCount = 0;
        lastPumpFlowCount = lastBoilerFlowCount = lastSteamGeneratorFlowCount = 0;
        lastParallelPlanCount = 0;
        lastParallelPlanJobCount = 0;
    }

    private void ResetSchedules(long simulationTick)
    {
        CompleteParallelFlowPlan();
        due.Clear();
        ClearDueBuckets();
        for (int i = 0; i < entryCount; i++)
        {
            ref Entry entry = ref entries[i];
            entry.ResetSchedule(simulationTick);
            if (entry.Registered && entry.Scheduled) ScheduleEntry(i);
        }
        lastDueCount = lastScheduleCandidateCount = lastFlowCount = lastStagedCount = lastDirectCount = 0;
        lastPumpFlowCount = lastBoilerFlowCount = lastSteamGeneratorFlowCount = 0;
        lastParallelPlanCount = 0;
        lastParallelPlanJobCount = 0;
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

    private int CompareEntryIndices(int leftIndex, int rightIndex)
    {
        if (leftIndex == rightIndex) return 0;
        Entry left = entries[leftIndex];
        Entry right = entries[rightIndex];
        int comparison = left.SimulationId.CompareTo(right.SimulationId);
        if (comparison != 0) return comparison;
        return string.CompareOrdinal(
            left.TypeName,
            right.TypeName);
    }

    private void EnsureEntryCapacity(int required)
    {
        if (entries.Length >= required) return;
        int capacity = Math.Max(required, checked(entries.Length * 2));
        Array.Resize(ref entries, capacity);
    }

    private struct Entry
    {
        public IMapObjectUpdateTick Target;
        public IMapObjectStagedUpdateTick Staged;
        public IFacilityFlowAdapter FlowAdapter;
        public IPersistenceDirtyTrackable Persistence;
        public long SimulationId;
        public string TypeName;
        public long LastExecutedTick;
        public long NextDueTick;
        public int IntervalTicks;
        public int PendingFlowIndex;
        public float PendingDeltaTime;
        public bool Registered;
        public bool Scheduled;
        public bool RequiresPowerEvaluation;

        public Entry(IMapObjectUpdateTick target, int intervalTicks, long currentTick)
        {
            Target = target;
            Staged = target as IMapObjectStagedUpdateTick;
            FlowAdapter = target as IFacilityFlowAdapter;
            Persistence = target as IPersistenceDirtyTrackable;
            SimulationId = target is IMapObjectSimulationIdentity identity ? identity.SimulationId : 0L;
            TypeName = target?.GetType().FullName ?? string.Empty;
            RequiresPowerEvaluation = target is InputOutputModule module
                                      && module.RequiresFacilityPowerEvaluation;
            IntervalTicks = Math.Max(1, intervalTicks);
            LastExecutedTick = 0L;
            NextDueTick = 0L;
            PendingDeltaTime = 0f;
            PendingFlowIndex = -1;
            Registered = true;
            Scheduled = false;
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
