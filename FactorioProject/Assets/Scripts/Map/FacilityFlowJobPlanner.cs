using System;
using ProjectF.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Copies the already captured scalar flow data into persistent native buffers,
/// schedules type-dense calculation jobs during Plan, then commits their scalar
/// outputs only when the main-thread GameObject Apply phase actually needs them.
/// </summary>
internal sealed class FacilityFlowJobPlanner : IFacilityFlowParallelPlanner
{
    private const int MinimumParallelEntityCount = 256;
    private const int MinimumTypeJobEntityCount = 64;
    private const int InnerLoopBatchCount = 64;

    private NativeArray<PumpFlowPlanInput> pumpInputs;
    private NativeArray<PumpFlowPlanOutput> pumpOutputs;
    private NativeArray<BoilerFlowPlanInput> boilerInputs;
    private NativeArray<BoilerFlowPlanOutput> boilerOutputs;
    private NativeArray<SteamGeneratorFlowPlanInput> steamGeneratorInputs;
    private NativeArray<SteamGeneratorFlowPlanOutput> steamGeneratorOutputs;
    private JobHandle pendingHandle;
    private FacilityFlowBatch pendingBatch;
    private int pendingPumpCount;
    private int pendingBoilerCount;
    private int pendingSteamGeneratorCount;
    private bool pendingPumps;
    private bool pendingBoilers;
    private bool pendingSteamGenerators;
    private bool scheduled;

    public int LastParallelEntityCount { get; private set; }
    public int LastScheduledJobCount { get; private set; }

    public bool TrySchedule(FacilityFlowBatch batch)
    {
        CompletePending();
        LastParallelEntityCount = 0;
        LastScheduledJobCount = 0;
        if (batch == null || batch.Count < MinimumParallelEntityCount)
        {
            return false;
        }

        int pumpCount = batch.PumpCount;
        int boilerCount = batch.BoilerCount;
        int steamGeneratorCount = batch.SteamGeneratorCount;
        bool schedulePumps = pumpCount >= MinimumTypeJobEntityCount;
        bool scheduleBoilers = boilerCount >= MinimumTypeJobEntityCount;
        bool scheduleSteamGenerators = steamGeneratorCount >= MinimumTypeJobEntityCount;
        PreparePumps(batch, pumpCount, schedulePumps);
        PrepareBoilers(batch, boilerCount, scheduleBoilers);
        PrepareSteamGenerators(batch, steamGeneratorCount, scheduleSteamGenerators);

        JobHandle combinedHandle = default;
        bool hasScheduledJob = false;
        if (schedulePumps)
        {
            JobHandle handle = new PumpFlowPlanJob
            {
                Inputs = pumpInputs,
                Outputs = pumpOutputs
            }.Schedule(pumpCount, InnerLoopBatchCount);
            combinedHandle = handle;
            hasScheduledJob = true;
            LastParallelEntityCount += pumpCount;
            LastScheduledJobCount++;
        }
        if (scheduleBoilers)
        {
            JobHandle handle = new BoilerFlowPlanJob
            {
                Inputs = boilerInputs,
                Outputs = boilerOutputs
            }.Schedule(boilerCount, InnerLoopBatchCount);
            combinedHandle = hasScheduledJob
                ? JobHandle.CombineDependencies(combinedHandle, handle)
                : handle;
            hasScheduledJob = true;
            LastParallelEntityCount += boilerCount;
            LastScheduledJobCount++;
        }
        if (scheduleSteamGenerators)
        {
            JobHandle handle = new SteamGeneratorFlowPlanJob
            {
                Inputs = steamGeneratorInputs,
                Outputs = steamGeneratorOutputs
            }.Schedule(steamGeneratorCount, InnerLoopBatchCount);
            combinedHandle = hasScheduledJob
                ? JobHandle.CombineDependencies(combinedHandle, handle)
                : handle;
            hasScheduledJob = true;
            LastParallelEntityCount += steamGeneratorCount;
            LastScheduledJobCount++;
        }

        if (!hasScheduledJob)
        {
            return false;
        }

        pendingHandle = combinedHandle;
        pendingBatch = batch;
        pendingPumpCount = pumpCount;
        pendingBoilerCount = boilerCount;
        pendingSteamGeneratorCount = steamGeneratorCount;
        pendingPumps = schedulePumps;
        pendingBoilers = scheduleBoilers;
        pendingSteamGenerators = scheduleSteamGenerators;
        scheduled = true;
        return true;
    }

    public void Complete(FacilityFlowBatch batch)
    {
        if (!scheduled) return;
        if (!ReferenceEquals(batch, pendingBatch))
            throw new InvalidOperationException("Facility flow job completed with a different batch.");
        CompletePending();
    }

    private void CompletePending()
    {
        if (!scheduled) return;
        pendingHandle.Complete();
        if (pendingPumps)
            for (int i = 0; i < pendingPumpCount; i++)
                pendingBatch.ApplyPumpPlanOutput(i, pumpOutputs[i]);
        if (pendingBoilers)
            for (int i = 0; i < pendingBoilerCount; i++)
                pendingBatch.ApplyBoilerPlanOutput(i, boilerOutputs[i]);
        if (pendingSteamGenerators)
            for (int i = 0; i < pendingSteamGeneratorCount; i++)
                pendingBatch.ApplySteamGeneratorPlanOutput(i, steamGeneratorOutputs[i]);
        scheduled = false;
        pendingHandle = default;
        pendingBatch = null;
        pendingPumpCount = pendingBoilerCount = pendingSteamGeneratorCount = 0;
        pendingPumps = pendingBoilers = pendingSteamGenerators = false;
    }

    private void PreparePumps(FacilityFlowBatch batch, int count, bool schedule)
    {
        if (!schedule)
        {
            for (int i = 0; i < count; i++)
                batch.ApplyPumpPlanOutput(
                    i,
                    FacilityFlowPlanKernels.PlanPump(batch.GetPumpPlanInput(i)));
            return;
        }

        EnsureCapacity(ref pumpInputs, count);
        EnsureCapacity(ref pumpOutputs, count);
        for (int i = 0; i < count; i++)
            pumpInputs[i] = batch.GetPumpPlanInput(i);
    }

    private void PrepareBoilers(FacilityFlowBatch batch, int count, bool schedule)
    {
        if (!schedule)
        {
            for (int i = 0; i < count; i++)
                batch.ApplyBoilerPlanOutput(
                    i,
                    FacilityFlowPlanKernels.PlanBoiler(batch.GetBoilerPlanInput(i)));
            return;
        }

        EnsureCapacity(ref boilerInputs, count);
        EnsureCapacity(ref boilerOutputs, count);
        for (int i = 0; i < count; i++)
            boilerInputs[i] = batch.GetBoilerPlanInput(i);
    }

    private void PrepareSteamGenerators(FacilityFlowBatch batch, int count, bool schedule)
    {
        if (!schedule)
        {
            for (int i = 0; i < count; i++)
                batch.ApplySteamGeneratorPlanOutput(
                    i,
                    FacilityFlowPlanKernels.PlanSteamGenerator(
                        batch.GetSteamGeneratorPlanInput(i)));
            return;
        }

        EnsureCapacity(ref steamGeneratorInputs, count);
        EnsureCapacity(ref steamGeneratorOutputs, count);
        for (int i = 0; i < count; i++)
            steamGeneratorInputs[i] = batch.GetSteamGeneratorPlanInput(i);
    }

    public void Dispose()
    {
        CompletePending();
        Dispose(ref pumpInputs);
        Dispose(ref pumpOutputs);
        Dispose(ref boilerInputs);
        Dispose(ref boilerOutputs);
        Dispose(ref steamGeneratorInputs);
        Dispose(ref steamGeneratorOutputs);
    }

    private static void EnsureCapacity<T>(ref NativeArray<T> values, int required)
        where T : struct
    {
        if (required <= 0 || values.IsCreated && values.Length >= required)
        {
            return;
        }

        Dispose(ref values);
        int capacity = 128;
        while (capacity < required)
        {
            capacity *= 2;
        }
        values = new NativeArray<T>(
            capacity,
            Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);
    }

    private static void Dispose<T>(ref NativeArray<T> values) where T : struct
    {
        if (values.IsCreated)
        {
            values.Dispose();
        }
        values = default;
    }

    [BurstCompile]
    private struct PumpFlowPlanJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<PumpFlowPlanInput> Inputs;
        [WriteOnly] public NativeArray<PumpFlowPlanOutput> Outputs;

        public void Execute(int index)
        {
            Outputs[index] = FacilityFlowPlanKernels.PlanPump(Inputs[index]);
        }
    }

    [BurstCompile]
    private struct BoilerFlowPlanJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BoilerFlowPlanInput> Inputs;
        [WriteOnly] public NativeArray<BoilerFlowPlanOutput> Outputs;

        public void Execute(int index)
        {
            Outputs[index] = FacilityFlowPlanKernels.PlanBoiler(Inputs[index]);
        }
    }

    [BurstCompile]
    private struct SteamGeneratorFlowPlanJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SteamGeneratorFlowPlanInput> Inputs;
        [WriteOnly] public NativeArray<SteamGeneratorFlowPlanOutput> Outputs;

        public void Execute(int index)
        {
            Outputs[index] = FacilityFlowPlanKernels.PlanSteamGenerator(Inputs[index]);
        }
    }
}

internal static class FacilityFlowJobRuntime
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        FacilitySimulationWorld.SetParallelFlowPlanner(new FacilityFlowJobPlanner());
        Application.quitting -= Shutdown;
        Application.quitting += Shutdown;
    }

    private static void Shutdown()
    {
        FacilitySimulationWorld.SetParallelFlowPlanner(null);
        Application.quitting -= Shutdown;
    }
}
