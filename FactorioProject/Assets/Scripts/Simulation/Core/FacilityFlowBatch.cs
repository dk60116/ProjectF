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

    /// <summary>
    /// Reusable structure-of-arrays storage for high-count fluid/power facilities.
    /// The batch owns deterministic scalar calculation while adapters own Unity and
    /// connected-storage side effects.
    /// </summary>
    public sealed class FacilityFlowBatch
    {
        public const float FluidEpsilon = 0.0001f;
        public const float OutputBudgetSeconds = 1f;

        private FacilityFlowKind[] kinds;

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

        public void Begin()
        {
            Count = 0;
        }

        public int ReserveSlot()
        {
            EnsureCapacity(Count + 1);
            int index = Count++;
            kinds[index] = FacilityFlowKind.None;
            return index;
        }

        public void PlanAll()
        {
            for (int i = 0; i < Count; i++)
            {
                switch (kinds[i])
                {
                    case FacilityFlowKind.Pump:
                        PlanPump(i);
                        break;
                    case FacilityFlowKind.Boiler:
                        PlanBoiler(i);
                        break;
                    case FacilityFlowKind.SteamGenerator:
                        PlanSteamGenerator(i);
                        break;
                }
            }
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
            ValidateIndex(index);
            kinds[index] = FacilityFlowKind.Pump;
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

        public int GetPumpItemId(int index) => pumpItemIds[index];
        public bool IsPumpOutputValid(int index) => pumpCanOutput[index] && pumpItemIds[index] >= 0 && pumpRates[index] > 0f;
        public bool IsPumpBlocked(int index) => pumpBlocked[index];
        public long GetPumpAccumulatorUnits(int index) => pumpAccumulatorUnits[index];
        public long GetPumpBudgetUnits(int index) => pumpBudgetUnits[index];
        public long GetPumpBudgetUpdatedTick(int index) => pumpBudgetUpdatedTicks[index];
        public float GetPumpRequestedLiters(int index) => DeterministicSimulationUnits.ToFloat(pumpRequestedUnits[index]);

        public void CommitPumpStorageAcceptance(int index, float acceptedLiters)
        {
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
            if (pumpAccumulatorUnits[index] < DeterministicSimulationUnits.UnitsPerWhole)
                return false;
            pumpAccumulatorUnits[index] -= DeterministicSimulationUnits.UnitsPerWhole;
            return true;
        }

        public void SetPumpBlocked(int index, bool blocked)
        {
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
            ValidateIndex(index);
            kinds[index] = FacilityFlowKind.Boiler;
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

        public bool IsBoilerValid(int index) => boilerValid[index];
        public int GetBoilerInputItemId(int index) => boilerInputItemIds[index];
        public int GetBoilerOutputItemId(int index) => boilerOutputItemIds[index];
        public float GetBoilerInputRate(int index) => boilerInputRates[index];
        public float GetBoilerOutputRate(int index) => boilerOutputRates[index];
        public float GetBoilerDeltaTime(int index) => boilerDeltaTimes[index];
        public float GetBoilerRequestedPullLiters(int index) => boilerRequestedPullLiters[index];
        public float GetBoilerMaximumOutputLiters(int index) => boilerMaximumOutputLiters[index];
        public float GetBoilerTemperature(int index) => boilerTemperatures[index];
        public long GetBoilerBudgetUnits(int index) => boilerBudgetUnits[index];
        public long GetBoilerBudgetUpdatedTick(int index) => boilerBudgetUpdatedTicks[index];

        public void UpdateBoilerStoredWater(int index, float storedWaterLiters)
        {
            boilerStoredWaterLiters[index] = Math.Max(0f, storedWaterLiters);
            ResolveBoilerMaximumOutput(index);
        }

        public void PrepareBoilerOutput(int index)
        {
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
            boilerTemperatures[index] = Clamp(temperatureCelsius, 0f, 100f);
        }

        public bool HeatBoiler(
            int index,
            float consumedEnergy,
            float completeEnergy,
            bool requiresEnergy,
            float craftDurationSeconds)
        {
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
            ValidateIndex(index);
            kinds[index] = FacilityFlowKind.SteamGenerator;
            steamInputItemIds[index] = inputItemId;
            steamInputRates[index] = Math.Max(0f, inputLitersPerSecond);
            steamDeltaTimes[index] = Math.Max(0f, deltaTime);
            steamValid[index] = valid;
            steamStoredLiters[index] = Math.Max(0f, storedLiters);
        }

        public bool IsSteamGeneratorValid(int index) => steamValid[index];
        public int GetSteamInputItemId(int index) => steamInputItemIds[index];
        public float GetSteamRequestedLiters(int index) => steamRequestedLiters[index];
        public float GetSteamRequiredLiters(int index) => steamRequiredLiters[index];
        public float GetSteamMissingLiters(int index) => steamMissingLiters[index];

        private void PlanPump(int index)
        {
            if (!IsPumpOutputValid(index))
            {
                pumpAccumulatorUnits[index] = 0L;
                pumpBudgetUnits[index] = 0L;
                pumpBudgetUpdatedTicks[index] = -1L;
                pumpAvailableThisTickUnits[index] = 0L;
                pumpRequestedUnits[index] = 0L;
                pumpBlocked[index] = true;
                return;
            }

            float rate = pumpRates[index];
            long maximumBudget = Math.Max(
                0L,
                DeterministicSimulationUnits.FromFloat(rate * OutputBudgetSeconds)
                - pumpAccumulatorUnits[index]);
            long nowTick = pumpSimulationTicks[index];
            if (pumpBudgetUpdatedTicks[index] < 0L || nowTick < pumpBudgetUpdatedTicks[index])
            {
                pumpBudgetUnits[index] = Math.Min(
                    maximumBudget,
                    DeterministicSimulationUnits.RateForTicks(
                        rate,
                        DeterministicSimulationUnits.SecondsToTicks(pumpDeltaTimes[index])));
            }
            else
            {
                long elapsedTicks = Math.Max(0L, nowTick - pumpBudgetUpdatedTicks[index]);
                pumpBudgetUnits[index] = Math.Min(
                    maximumBudget,
                    SaturatingAdd(
                        pumpBudgetUnits[index],
                        DeterministicSimulationUnits.RateForTicks(rate, elapsedTicks)));
            }
            pumpBudgetUpdatedTicks[index] = nowTick;
            pumpAvailableThisTickUnits[index] = Math.Min(
                DeterministicSimulationUnits.RateForTicks(
                    rate,
                    DeterministicSimulationUnits.DeltaTimeToTicks(pumpDeltaTimes[index])),
                pumpBudgetUnits[index]);
            pumpRequestedUnits[index] = SaturatingAdd(
                pumpAccumulatorUnits[index],
                pumpAvailableThisTickUnits[index]);
        }

        private void PlanBoiler(int index)
        {
            boilerRequestedPullLiters[index] = boilerValid[index] && boilerCanPull[index]
                ? boilerPullRates[index] * boilerDeltaTimes[index]
                : 0f;
            if (!boilerValid[index])
            {
                boilerMaximumOutputLiters[index] = 0f;
                return;
            }
            boilerMaximumOutputLiters[index] = 0f;
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
            if (!steamValid[index] || steamInputRates[index] <= 0f)
            {
                steamRequestedLiters[index] = 0f;
                steamRequiredLiters[index] = 0f;
                steamMissingLiters[index] = 0f;
                return;
            }
            float requested = steamInputRates[index] * steamDeltaTimes[index];
            // A generator only needs the steam consumed by this deterministic
            // update. A larger start-only reserve starves otherwise supplied
            // generators when several engines share one boiler.
            float required = Math.Max(FluidEpsilon, requested);
            steamRequestedLiters[index] = requested;
            steamRequiredLiters[index] = required;
            steamMissingLiters[index] = Math.Max(0f, required - steamStoredLiters[index]);
        }

        private void EnsureCapacity(int required)
        {
            int current = kinds?.Length ?? 0;
            if (current >= required) return;
            int capacity = Math.Max(required, current > 0 ? current * 2 : 16);
            Resize(ref kinds, capacity);
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
