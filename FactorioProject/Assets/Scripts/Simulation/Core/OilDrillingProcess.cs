using System;

namespace ProjectF.Simulation
{
    // Fractional liters are retained across ticks, energy changes and save/load.
    public struct OilDrillingProcess
    {
        public long ProgressUnits;
        public void Advance(long ticks, float litersPerSecond, float supplyRatio)
        {
            if (ticks <= 0 || litersPerSecond <= 0 || supplyRatio <= 0) return;
            long produced = DeterministicSimulationUnits.RateForTicks(litersPerSecond * supplyRatio, ticks);
            ProgressUnits = produced > long.MaxValue - ProgressUnits ? long.MaxValue : ProgressUnits + produced;
        }
        public void AdvanceUnits(long ticks, long unitsPerTick)
        {
            if (ticks <= 0 || unitsPerTick <= 0) return;
            long available = long.MaxValue - ProgressUnits;
            ProgressUnits = ticks > available / unitsPerTick ? long.MaxValue : ProgressUnits + ticks * unitsPerTick;
        }
        public bool CanHarvest(int liters) => liters > 0 && ProgressUnits >= DeterministicSimulationUnits.FromInt(liters);
        public void CompleteHarvest(int liters) => ProgressUnits = Math.Max(0, ProgressUnits - DeterministicSimulationUnits.FromInt(liters));
    }
}
