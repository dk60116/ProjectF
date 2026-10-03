using System;

namespace ProjectF.Simulation
{
    // The existing ProductionProcess remains the sole production state machine.
    // This clock integrates a published power rate only when a deadline/event is due.
    public struct MiningProcess
    {
        public ProductionProcess Production;
        public long SampleTick;
        public float SupplyRatio;
        public double AnimationPhase;

        public void Sample(long tick, float watts, long completeEnergy)
        {
            long elapsed = Math.Max(0, tick - SampleTick);
            SampleTick = tick;
            if (!Production.Active || Production.WaitingForOutput || SupplyRatio <= 0f) return;
            long energy = DeterministicSimulationUnits.RateForTicks(watts * SupplyRatio, elapsed);
            Production.Advance(0, true, energy, completeEnergy);
            AnimationPhase += elapsed * (double)SimulationTickWorld.FixedSimulationDeltaSeconds * SupplyRatio;
        }

        public long Deadline(long currentTick, float watts, long completeEnergy)
        {
            if (!Production.Active || Production.WaitingForOutput) return currentTick + 1;
            decimal rate = (decimal)watts * (decimal)SupplyRatio * DeterministicSimulationUnits.UnitsPerWhole;
            if (rate <= 0) return long.MaxValue;
            long remaining = Math.Max(0, completeEnergy - Production.ConsumedEnergyUnits);
            decimal delay = decimal.Ceiling(remaining * (decimal)SimulationTickWorld.DefaultSimulationTicksPerSecond / rate);
            return delay >= long.MaxValue - currentTick ? long.MaxValue : currentTick + Math.Max(1, (long)delay);
        }

        public long SnapshotEnergy(long tick, float watts, long completeEnergy)
        {
            MiningProcess snapshot = this;
            snapshot.Sample(tick, watts, completeEnergy);
            return Math.Min(completeEnergy, snapshot.Production.ConsumedEnergyUnits);
        }
    }
}
