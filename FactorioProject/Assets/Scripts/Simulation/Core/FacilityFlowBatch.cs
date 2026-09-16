using System;

namespace ProjectF.Simulation
{
    public enum FacilityFlowKind : byte
    {
        None,
        Pump,
        Boiler,
        SteamGenerator
    }

    /// <summary>
    /// Unity-free adapter boundary for facility flow simulation. Capture writes only
    /// scalar state; Apply is the main-thread IO commit boundary.
    /// </summary>
    public interface IFacilityFlowAdapter
    {
        void CaptureFacilityFlow(FacilityFlowBatch batch, int index, float deltaTime, long simulationTick);
        void ApplyFacilityFlow(FacilityFlowBatch batch, int index);
    }

    public interface IFacilityFlowParallelPlanner : IDisposable
    {
        int LastParallelEntityCount { get; }
        int LastScheduledJobCount { get; }
        bool TrySchedule(FacilityFlowBatch batch);
        void Complete(FacilityFlowBatch batch);
    }

    public struct PumpFlowPlanInput
    {
        public int Valid;
        public int ResetBudget;
        public long AccumulatorUnits;
        public long BudgetUnits;
        public long MaximumBudgetUnits;
        public long BudgetContributionUnits;
        public long AvailableThisTickUnits;
        public long SimulationTick;
    }

    public struct PumpFlowPlanOutput
    {
        public long AccumulatorUnits;
        public long BudgetUnits;
        public long BudgetUpdatedTick;
        public long AvailableThisTickUnits;
        public long RequestedUnits;
        public int Blocked;
    }

    public struct BoilerFlowPlanInput
    {
        public int Valid;
        public int CanPull;
        public float PullRate;
        public float DeltaTime;
    }

    public struct BoilerFlowPlanOutput
    {
        public float RequestedPullLiters;
        public float MaximumOutputLiters;
    }

    public struct SteamGeneratorFlowPlanInput
    {
        public int Valid;
        public float InputRate;
        public float DeltaTime;
        public float StoredLiters;
    }

    public struct SteamGeneratorFlowPlanOutput
    {
        public float RequestedLiters;
        public float RequiredLiters;
        public float MissingLiters;
    }

    /// <summary>
    /// Blittable, Unity-free kernels shared by the serial fallback and Burst jobs.
    /// Pump decimal conversions are completed while building its input so the job
    /// executes integer-only deterministic state transitions.
    /// </summary>
    public static class FacilityFlowPlanKernels
    {
        public static PumpFlowPlanOutput PlanPump(PumpFlowPlanInput input)
        {
            if (input.Valid == 0)
            {
                return new PumpFlowPlanOutput
                {
                    BudgetUpdatedTick = -1L,
                    Blocked = 1
                };
            }

            long budgetUnits = input.ResetBudget != 0
                ? Math.Min(input.MaximumBudgetUnits, input.BudgetContributionUnits)
                : Math.Min(
                    input.MaximumBudgetUnits,
                    SaturatingAdd(input.BudgetUnits, input.BudgetContributionUnits));
            long availableUnits = Math.Min(input.AvailableThisTickUnits, budgetUnits);
            return new PumpFlowPlanOutput
            {
                AccumulatorUnits = input.AccumulatorUnits,
                BudgetUnits = budgetUnits,
                BudgetUpdatedTick = input.SimulationTick,
                AvailableThisTickUnits = availableUnits,
                RequestedUnits = SaturatingAdd(input.AccumulatorUnits, availableUnits),
                Blocked = 0
            };
        }

        public static BoilerFlowPlanOutput PlanBoiler(BoilerFlowPlanInput input)
        {
            return new BoilerFlowPlanOutput
            {
                RequestedPullLiters = input.Valid != 0 && input.CanPull != 0
                    ? input.PullRate * input.DeltaTime
                    : 0f,
                MaximumOutputLiters = 0f
            };
        }

        public static SteamGeneratorFlowPlanOutput PlanSteamGenerator(
            SteamGeneratorFlowPlanInput input)
        {
            if (input.Valid == 0 || input.InputRate <= 0f)
            {
                return default;
            }

            float requested = input.InputRate * input.DeltaTime;
            float required = Math.Max(FacilityFlowBatch.FluidEpsilon, requested);
            return new SteamGeneratorFlowPlanOutput
            {
                RequestedLiters = requested,
                RequiredLiters = required,
                MissingLiters = Math.Max(0f, required - input.StoredLiters)
            };
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (left <= 0L) return Math.Max(0L, right);
            if (right <= 0L) return left;
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }
    }

    /// <summary>
    /// Reusable structure-of-arrays storage for high-count fluid/power facilities.
    /// The batch owns deterministic scalar calculation while adapters own Unity and
    /// connected-storage side effects.
    /// </summary>
    public sealed class FacilityFlowBatch
    {
        public const float FluidEpsilon = 0.0001f;
        public const float OutputBudgetSeconds = 1f;

        private FacilityFlowKind[] slotKinds;
        private int[] slotLocalIndices;
        private int pumpCount;
        private int boilerCount;
        private int steamGeneratorCount;

        private int[] pumpItemIds;
        private bool[] pumpCanOutput;
        private bool[] pumpBlocked;
        private float[] pumpRates;
        private float[] pumpDeltaTimes;
        private long[] pumpSimulationTicks;
        private long[] pumpAccumulatorUnits;
        private long[] pumpBudgetUnits;
        private long[] pumpBudgetUpdatedTicks;
        private long[] pumpAvailableThisTickUnits;
        private long[] pumpRequestedUnits;

        private int[] boilerInputItemIds;
        private int[] boilerOutputItemIds;
        private bool[] boilerValid;
        private bool[] boilerCanPull;
        private float[] boilerInputRates;
        private float[] boilerOutputRates;
        private float[] boilerEffectiveOutputRates;
        private float[] boilerPullRates;
        private float[] boilerDeltaTimes;
        private float[] boilerStoredWaterLiters;
        private float[] boilerTemperatures;
        private float[] boilerRequestedPullLiters;
        private float[] boilerMaximumOutputLiters;
        private long[] boilerSimulationTicks;
        private long[] boilerBudgetUnits;
        private long[] boilerBudgetUpdatedTicks;

        private int[] steamInputItemIds;
        private bool[] steamValid;
        private float[] steamInputRates;
        private float[] steamDeltaTimes;
        private float[] steamStoredLiters;
        private float[] steamRequestedLiters;
        private float[] steamRequiredLiters;
        private float[] steamMissingLiters;

        public FacilityFlowBatch(int initialCapacity = 16)
        {
            EnsureCapacity(Math.Max(1, initialCapacity));
        }

        public int Count { get; private set; }
        public int PumpCount => pumpCount;
        public int BoilerCount => boilerCount;
        public int SteamGeneratorCount => steamGeneratorCount;

        public void Begin()
        {
            Count = 0;
            pumpCount = 0;
            boilerCount = 0;
            steamGeneratorCount = 0;
        }

        public int ReserveSlot()
        {
            EnsureCapacity(Count + 1);
            int index = Count++;
            slotKinds[index] = FacilityFlowKind.None;
            slotLocalIndices[index] = -1;
            return index;
        }

        public void PlanAll()
        {
            for (int i = 0; i < pumpCount; i++) PlanPump(i);
            for (int i = 0; i < boilerCount; i++) PlanBoiler(i);
            for (int i = 0; i < steamGeneratorCount; i++) PlanSteamGenerator(i);
        }

        public PumpFlowPlanInput GetPumpPlanInput(int denseIndex)
        {
            ValidateDenseIndex(denseIndex, pumpCount);
            float rate = pumpRates[denseIndex];
            long nowTick = pumpSimulationTicks[denseIndex];
            long updatedTick = pumpBudgetUpdatedTicks[denseIndex];
            bool resetBudget = updatedTick < 0L || nowTick < updatedTick;
            long elapsedTicks = resetBudget
                ? DeterministicSimulationUnits.SecondsToTicks(pumpDeltaTimes[denseIndex])
                : Math.Max(0L, nowTick - updatedTick);
            return new PumpFlowPlanInput
            {
                Valid = IsPumpOutputValidLocal(denseIndex) ? 1 : 0,
                ResetBudget = resetBudget ? 1 : 0,
                AccumulatorUnits = pumpAccumulatorUnits[denseIndex],
                BudgetUnits = pumpBudgetUnits[denseIndex],
                MaximumBudgetUnits = Math.Max(
                    0L,
                    DeterministicSimulationUnits.FromFloat(rate * OutputBudgetSeconds)
                    - pumpAccumulatorUnits[denseIndex]),
                BudgetContributionUnits = DeterministicSimulationUnits.RateForTicks(rate, elapsedTicks),
                AvailableThisTickUnits = DeterministicSimulationUnits.RateForTicks(
                    rate,
                    DeterministicSimulationUnits.DeltaTimeToTicks(pumpDeltaTimes[denseIndex])),
                SimulationTick = nowTick
            };
        }

        public void ApplyPumpPlanOutput(int denseIndex, PumpFlowPlanOutput output)
        {
            ValidateDenseIndex(denseIndex, pumpCount);
            pumpAccumulatorUnits[denseIndex] = output.AccumulatorUnits;
            pumpBudgetUnits[denseIndex] = output.BudgetUnits;
            pumpBudgetUpdatedTicks[denseIndex] = output.BudgetUpdatedTick;
            pumpAvailableThisTickUnits[denseIndex] = output.AvailableThisTickUnits;
            pumpRequestedUnits[denseIndex] = output.RequestedUnits;
            pumpBlocked[denseIndex] = output.Blocked != 0;
        }

        public BoilerFlowPlanInput GetBoilerPlanInput(int denseIndex)
        {
            ValidateDenseIndex(denseIndex, boilerCount);
            return new BoilerFlowPlanInput
            {
                Valid = boilerValid[denseIndex] ? 1 : 0,
                CanPull = boilerCanPull[denseIndex] ? 1 : 0,
                PullRate = boilerPullRates[denseIndex],
                DeltaTime = boilerDeltaTimes[denseIndex]
            };
        }

        public void ApplyBoilerPlanOutput(int denseIndex, BoilerFlowPlanOutput output)
        {
            ValidateDenseIndex(denseIndex, boilerCount);
            boilerRequestedPullLiters[denseIndex] = output.RequestedPullLiters;
            boilerMaximumOutputLiters[denseIndex] = output.MaximumOutputLiters;
        }

        public SteamGeneratorFlowPlanInput GetSteamGeneratorPlanInput(int denseIndex)
        {
            ValidateDenseIndex(denseIndex, steamGeneratorCount);
            return new SteamGeneratorFlowPlanInput
            {
                Valid = steamValid[denseIndex] ? 1 : 0,
                InputRate = steamInputRates[denseIndex],
                DeltaTime = steamDeltaTimes[denseIndex],
                StoredLiters = steamStoredLiters[denseIndex]
            };
        }

        public void ApplySteamGeneratorPlanOutput(
            int denseIndex,
            SteamGeneratorFlowPlanOutput output)
        {
            ValidateDenseIndex(denseIndex, steamGeneratorCount);
            steamRequestedLiters[denseIndex] = output.RequestedLiters;
            steamRequiredLiters[denseIndex] = output.RequiredLiters;
            steamMissingLiters[denseIndex] = output.MissingLiters;
        }

        public void ConfigurePump(
            int index,
            int itemId,
            float rateLitersPerSecond,
            float deltaTime,
            long simulationTick,
            bool canOutput,
            long accumulatorUnits,
            long budgetUnits,
            long budgetUpdatedTick)
        {
            index = AssignTypedSlot(index, FacilityFlowKind.Pump, ref pumpCount);
            pumpItemIds[index] = itemId;
            pumpRates[index] = Math.Max(0f, rateLitersPerSecond);
            pumpDeltaTimes[index] = Math.Max(0f, deltaTime);
            pumpSimulationTicks[index] = simulationTick;
            pumpCanOutput[index] = canOutput;
            pumpAccumulatorUnits[index] = Math.Max(0L, accumulatorUnits);
            pumpBudgetUnits[index] = Math.Max(0L, budgetUnits);
            pumpBudgetUpdatedTicks[index] = budgetUpdatedTick;
            pumpBlocked[index] = false;
        }

        public int GetPumpItemId(int index) => pumpItemIds[ResolveTypedSlot(index, FacilityFlowKind.Pump)];
        public bool IsPumpOutputValid(int index) => IsPumpOutputValidLocal(ResolveTypedSlot(index, FacilityFlowKind.Pump));
        public bool IsPumpBlocked(int index) => pumpBlocked[ResolveTypedSlot(index, FacilityFlowKind.Pump)];
        public long GetPumpAccumulatorUnits(int index) => pumpAccumulatorUnits[ResolveTypedSlot(index, FacilityFlowKind.Pump)];
        public long GetPumpBudgetUnits(int index) => pumpBudgetUnits[ResolveTypedSlot(index, FacilityFlowKind.Pump)];
        public long GetPumpBudgetUpdatedTick(int index) => pumpBudgetUpdatedTicks[ResolveTypedSlot(index, FacilityFlowKind.Pump)];
        public float GetPumpRequestedLiters(int index) => DeterministicSimulationUnits.ToFloat(pumpRequestedUnits[ResolveTypedSlot(index, FacilityFlowKind.Pump)]);

        public void CommitPumpStorageAcceptance(int index, float acceptedLiters)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Pump);
            long acceptedUnits = Math.Min(
                pumpRequestedUnits[index],
                DeterministicSimulationUnits.FromFloat(acceptedLiters));
            long accumulatedUnitsUsed = Math.Min(pumpAccumulatorUnits[index], acceptedUnits);
            pumpAccumulatorUnits[index] -= accumulatedUnitsUsed;

            long budgetUnitsUsed = Math.Min(
                pumpAvailableThisTickUnits[index],
                Math.Max(0L, acceptedUnits - accumulatedUnitsUsed));
            pumpAvailableThisTickUnits[index] -= budgetUnitsUsed;
            pumpBudgetUnits[index] = Math.Max(0L, pumpBudgetUnits[index] - budgetUnitsUsed);

            pumpBudgetUnits[index] = Math.Max(
                0L,
                pumpBudgetUnits[index] - pumpAvailableThisTickUnits[index]);
            pumpAccumulatorUnits[index] = SaturatingAdd(
                pumpAccumulatorUnits[index],
                pumpAvailableThisTickUnits[index]);
            pumpAvailableThisTickUnits[index] = 0L;
        }

        public bool TryConsumePumpWholeLiter(int index)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Pump);
            if (pumpAccumulatorUnits[index] < DeterministicSimulationUnits.UnitsPerWhole)
                return false;
            pumpAccumulatorUnits[index] -= DeterministicSimulationUnits.UnitsPerWhole;
            return true;
        }

        public void SetPumpBlocked(int index, bool blocked)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Pump);
            pumpBlocked[index] = blocked;
            if (blocked)
            {
                pumpAccumulatorUnits[index] = Math.Min(
                    pumpAccumulatorUnits[index],
                    DeterministicSimulationUnits.UnitsPerWhole);
            }
        }

        public void ConfigureBoiler(
            int index,
            int inputItemId,
            float inputLitersPerSecond,
            int outputItemId,
            float outputLitersPerSecond,
            float effectiveOutputLitersPerSecond,
            float pullLitersPerSecond,
            float deltaTime,
            long simulationTick,
            bool valid,
            bool canPull,
            float storedWaterLiters,
            float temperatureCelsius,
            long budgetUnits,
            long budgetUpdatedTick)
        {
            index = AssignTypedSlot(index, FacilityFlowKind.Boiler, ref boilerCount);
            boilerInputItemIds[index] = inputItemId;
            boilerInputRates[index] = Math.Max(0f, inputLitersPerSecond);
            boilerOutputItemIds[index] = outputItemId;
            boilerOutputRates[index] = Math.Max(0f, outputLitersPerSecond);
            boilerEffectiveOutputRates[index] = Math.Max(0f, effectiveOutputLitersPerSecond);
            boilerPullRates[index] = Math.Max(0f, pullLitersPerSecond);
            boilerDeltaTimes[index] = Math.Max(0f, deltaTime);
            boilerSimulationTicks[index] = simulationTick;
            boilerValid[index] = valid;
            boilerCanPull[index] = canPull;
            boilerStoredWaterLiters[index] = Math.Max(0f, storedWaterLiters);
            boilerTemperatures[index] = Clamp(temperatureCelsius, 0f, 100f);
            boilerBudgetUnits[index] = Math.Max(0L, budgetUnits);
            boilerBudgetUpdatedTicks[index] = budgetUpdatedTick;
        }

        public bool IsBoilerValid(int index) => boilerValid[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public int GetBoilerInputItemId(int index) => boilerInputItemIds[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public int GetBoilerOutputItemId(int index) => boilerOutputItemIds[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public float GetBoilerInputRate(int index) => boilerInputRates[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public float GetBoilerOutputRate(int index) => boilerOutputRates[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public float GetBoilerDeltaTime(int index) => boilerDeltaTimes[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public float GetBoilerRequestedPullLiters(int index) => boilerRequestedPullLiters[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public float GetBoilerMaximumOutputLiters(int index) => boilerMaximumOutputLiters[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public float GetBoilerTemperature(int index) => boilerTemperatures[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public long GetBoilerBudgetUnits(int index) => boilerBudgetUnits[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];
        public long GetBoilerBudgetUpdatedTick(int index) => boilerBudgetUpdatedTicks[ResolveTypedSlot(index, FacilityFlowKind.Boiler)];

        public void UpdateBoilerStoredWater(int index, float storedWaterLiters)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Boiler);
            boilerStoredWaterLiters[index] = Math.Max(0f, storedWaterLiters);
            ResolveBoilerMaximumOutput(index);
        }

        public void PrepareBoilerOutput(int index)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Boiler);
            if (!boilerValid[index])
            {
                boilerMaximumOutputLiters[index] = 0f;
                return;
            }

            float rate = boilerEffectiveOutputRates[index];
            long maximumBudget = DeterministicSimulationUnits.FromFloat(rate * OutputBudgetSeconds);
            long nowTick = boilerSimulationTicks[index];
            if (boilerBudgetUpdatedTicks[index] < 0L || nowTick < boilerBudgetUpdatedTicks[index])
            {
                boilerBudgetUnits[index] = Math.Min(
                    maximumBudget,
                    DeterministicSimulationUnits.RateForTicks(
                        rate,
                        DeterministicSimulationUnits.DeltaTimeToTicks(boilerDeltaTimes[index])));
            }
            else
            {
                long elapsedTicks = Math.Max(0L, nowTick - boilerBudgetUpdatedTicks[index]);
                boilerBudgetUnits[index] = Math.Min(
                    maximumBudget,
                    SaturatingAdd(
                        boilerBudgetUnits[index],
                        DeterministicSimulationUnits.RateForTicks(rate, elapsedTicks)));
            }
            boilerBudgetUpdatedTicks[index] = nowTick;
            ResolveBoilerMaximumOutput(index);
        }

        public void SetBoilerTemperature(int index, float temperatureCelsius)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Boiler);
            boilerTemperatures[index] = Clamp(temperatureCelsius, 0f, 100f);
        }

        public bool HeatBoiler(
            int index,
            float consumedEnergy,
            float completeEnergy,
            bool requiresEnergy,
            float craftDurationSeconds)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Boiler);
            float gain = requiresEnergy
                ? completeEnergy > FluidEpsilon
                    ? Math.Max(0f, consumedEnergy) / completeEnergy * 100f
                    : 0f
                : Math.Max(0f, boilerDeltaTimes[index]) / Math.Max(FluidEpsilon, craftDurationSeconds) * 100f;
            if (gain <= FluidEpsilon) return false;
            boilerTemperatures[index] = Math.Min(100f, boilerTemperatures[index] + gain);
            return true;
        }

        public bool CoolBoiler(int index, float targetTemperature, float craftDurationSeconds, float coolingRateScale)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Boiler);
            float current = boilerTemperatures[index];
            float target = Clamp(targetTemperature, 0f, 100f);
            if (current <= target + FluidEpsilon) return false;
            float drop = Math.Max(0f, boilerDeltaTimes[index])
                         / Math.Max(FluidEpsilon, craftDurationSeconds)
                         * 100f
                         * Math.Max(0f, coolingRateScale);
            if (drop <= FluidEpsilon) return false;
            boilerTemperatures[index] = Math.Max(target, current - drop);
            return true;
        }

        public void CommitBoilerOutput(int index, float acceptedLiters)
        {
            index = ResolveTypedSlot(index, FacilityFlowKind.Boiler);
            boilerBudgetUnits[index] = Math.Max(
                0L,
                boilerBudgetUnits[index] - DeterministicSimulationUnits.FromFloat(acceptedLiters));
        }

        public void ConfigureSteamGenerator(
            int index,
            int inputItemId,
            float inputLitersPerSecond,
            float deltaTime,
            bool valid,
            float storedLiters)
        {
            index = AssignTypedSlot(index, FacilityFlowKind.SteamGenerator, ref steamGeneratorCount);
            steamInputItemIds[index] = inputItemId;
            steamInputRates[index] = Math.Max(0f, inputLitersPerSecond);
            steamDeltaTimes[index] = Math.Max(0f, deltaTime);
            steamValid[index] = valid;
            steamStoredLiters[index] = Math.Max(0f, storedLiters);
        }

        public bool IsSteamGeneratorValid(int index) => steamValid[ResolveTypedSlot(index, FacilityFlowKind.SteamGenerator)];
        public int GetSteamInputItemId(int index) => steamInputItemIds[ResolveTypedSlot(index, FacilityFlowKind.SteamGenerator)];
        public float GetSteamRequestedLiters(int index) => steamRequestedLiters[ResolveTypedSlot(index, FacilityFlowKind.SteamGenerator)];
        public float GetSteamRequiredLiters(int index) => steamRequiredLiters[ResolveTypedSlot(index, FacilityFlowKind.SteamGenerator)];
        public float GetSteamMissingLiters(int index) => steamMissingLiters[ResolveTypedSlot(index, FacilityFlowKind.SteamGenerator)];

        private void PlanPump(int index)
        {
            ApplyPumpPlanOutput(index, FacilityFlowPlanKernels.PlanPump(GetPumpPlanInput(index)));
        }

        private void PlanBoiler(int index)
        {
            ApplyBoilerPlanOutput(index, FacilityFlowPlanKernels.PlanBoiler(GetBoilerPlanInput(index)));
        }

        private void ResolveBoilerMaximumOutput(int index)
        {
            float inputRate = boilerInputRates[index];
            float outputRate = boilerOutputRates[index];
            if (!boilerValid[index] || inputRate <= 0f || outputRate <= 0f)
            {
                boilerMaximumOutputLiters[index] = 0f;
                return;
            }
            float waterPerSteam = inputRate / outputRate;
            float maxFromWater = boilerStoredWaterLiters[index] / waterPerSteam;
            long requestedUnits = DeterministicSimulationUnits.RateForTicks(
                boilerEffectiveOutputRates[index],
                DeterministicSimulationUnits.DeltaTimeToTicks(boilerDeltaTimes[index]));
            boilerMaximumOutputLiters[index] = Math.Min(
                DeterministicSimulationUnits.ToFloat(requestedUnits),
                Math.Min(
                    maxFromWater,
                    DeterministicSimulationUnits.ToFloat(boilerBudgetUnits[index])));
        }

        private void PlanSteamGenerator(int index)
        {
            ApplySteamGeneratorPlanOutput(
                index,
                FacilityFlowPlanKernels.PlanSteamGenerator(GetSteamGeneratorPlanInput(index)));
        }

        private void EnsureCapacity(int required)
        {
            int current = slotKinds?.Length ?? 0;
            if (current >= required) return;
            int capacity = Math.Max(required, current > 0 ? current * 2 : 16);
            Resize(ref slotKinds, capacity);
            Resize(ref slotLocalIndices, capacity);
            Resize(ref pumpItemIds, capacity); Resize(ref pumpCanOutput, capacity); Resize(ref pumpBlocked, capacity);
            Resize(ref pumpRates, capacity); Resize(ref pumpDeltaTimes, capacity); Resize(ref pumpSimulationTicks, capacity);
            Resize(ref pumpAccumulatorUnits, capacity); Resize(ref pumpBudgetUnits, capacity); Resize(ref pumpBudgetUpdatedTicks, capacity);
            Resize(ref pumpAvailableThisTickUnits, capacity); Resize(ref pumpRequestedUnits, capacity);
            Resize(ref boilerInputItemIds, capacity); Resize(ref boilerOutputItemIds, capacity); Resize(ref boilerValid, capacity);
            Resize(ref boilerCanPull, capacity); Resize(ref boilerInputRates, capacity); Resize(ref boilerOutputRates, capacity);
            Resize(ref boilerEffectiveOutputRates, capacity); Resize(ref boilerPullRates, capacity); Resize(ref boilerDeltaTimes, capacity);
            Resize(ref boilerStoredWaterLiters, capacity); Resize(ref boilerTemperatures, capacity);
            Resize(ref boilerRequestedPullLiters, capacity); Resize(ref boilerMaximumOutputLiters, capacity);
            Resize(ref boilerSimulationTicks, capacity); Resize(ref boilerBudgetUnits, capacity); Resize(ref boilerBudgetUpdatedTicks, capacity);
            Resize(ref steamInputItemIds, capacity); Resize(ref steamValid, capacity);
            Resize(ref steamInputRates, capacity); Resize(ref steamDeltaTimes, capacity); Resize(ref steamStoredLiters, capacity);
            Resize(ref steamRequestedLiters, capacity); Resize(ref steamRequiredLiters, capacity);
            Resize(ref steamMissingLiters, capacity);
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void ValidateDenseIndex(int index, int count)
        {
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private int AssignTypedSlot(int slotIndex, FacilityFlowKind kind, ref int typedCount)
        {
            ValidateIndex(slotIndex);
            if (slotKinds[slotIndex] != FacilityFlowKind.None)
                throw new InvalidOperationException("Facility flow slot is already configured.");
            int localIndex = typedCount++;
            slotKinds[slotIndex] = kind;
            slotLocalIndices[slotIndex] = localIndex;
            return localIndex;
        }

        private int ResolveTypedSlot(int slotIndex, FacilityFlowKind expectedKind)
        {
            ValidateIndex(slotIndex);
            if (slotKinds[slotIndex] != expectedKind || slotLocalIndices[slotIndex] < 0)
                throw new InvalidOperationException("Facility flow slot kind does not match the requested operation.");
            return slotLocalIndices[slotIndex];
        }

        private bool IsPumpOutputValidLocal(int index)
        {
            return pumpCanOutput[index] && pumpItemIds[index] >= 0 && pumpRates[index] > 0f;
        }

        private static void Resize<T>(ref T[] values, int capacity)
        {
            Array.Resize(ref values, capacity);
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (left <= 0L) return Math.Max(0L, right);
            if (right <= 0L) return left;
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }
}
