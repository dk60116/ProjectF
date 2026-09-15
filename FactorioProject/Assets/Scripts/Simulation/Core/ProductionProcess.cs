using System;

namespace ProjectF.Simulation
{
    /// <summary>Value state saved by the existing installation DTO, never by a view.</summary>
    [Serializable]
    public struct ProductionProcess
    {
        public bool Active, WaitingForOutput;
        public long RemainingTicks, ConsumedEnergyUnits;
        public int RecipeIndex, OutputItemId, OutputCount;

        public static ProductionProcess Empty => new ProductionProcess { RecipeIndex = -1, OutputItemId = -1 };

        public void Begin(int recipeIndex, int outputItemId, int outputCount, long durationTicks)
        {
            this = Empty;
            if (outputItemId < 0 || outputCount <= 0) return;
            Active = true;
            RecipeIndex = recipeIndex;
            OutputItemId = outputItemId;
            OutputCount = outputCount;
            RemainingTicks = Math.Max(0, durationTicks);
        }

        // Accepted energy is supplied by an IO adapter; probing demand never consumes it.
        // Completion remains pending until the output port accepts the complete batch.
        public bool Advance(long elapsedTicks, bool energyRequired, long acceptedEnergyUnits,
            long completeEnergyUnits, long energyRateUnits)
        {
            if (!Active) return false;
            if (WaitingForOutput) return true;
            if (energyRequired)
            {
                if (acceptedEnergyUnits <= 0) return false;
                ConsumedEnergyUnits = acceptedEnergyUnits > long.MaxValue - Math.Max(0, ConsumedEnergyUnits)
                    ? long.MaxValue : Math.Max(0, ConsumedEnergyUnits) + acceptedEnergyUnits;
                RemainingTicks = RemainingEnergyTicks(completeEnergyUnits, ConsumedEnergyUnits, energyRateUnits);
                if (ConsumedEnergyUnits < completeEnergyUnits) return false;
            }
            else
            {
                RemainingTicks = Math.Max(0, RemainingTicks - Math.Max(0, elapsedTicks));
                if (RemainingTicks > 0) return false;
            }
            WaitingForOutput = true;
            return true;
        }

        public void Clear() => this = Empty;

        public static long RemainingEnergyTicks(long complete, long consumed, long rate)
        {
            long remaining = Math.Max(0, Math.Max(0, complete) - Math.Max(0, consumed));
            if (rate <= 0 || remaining <= 0) return 0;
            decimal ticks = decimal.Round((decimal)remaining * SimulationTickWorld.DefaultSimulationTicksPerSecond
                / rate, 0, MidpointRounding.AwayFromZero);
            return ticks >= long.MaxValue ? long.MaxValue : Math.Max(1, (long)ticks);
        }
    }
}
