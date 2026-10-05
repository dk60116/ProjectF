using System;

namespace ProjectF.Simulation
{
    // The existing ProductionProcess remains the sole production state machine.
    // Integrate cached integer rates only when a deadline/event is due.
    public struct MiningProcess
    {
        public ProductionProcess Production;
        public long SampleTick;
        public float SupplyRatio;
        public long EnergyUnitsPerTick;
        public double AnimationPhase;

        public long RequestedEnergy(long tick) => EnergyUnitsPerTick <= 0 ? 0
            : Math.Min(long.MaxValue / EnergyUnitsPerTick, Math.Max(0, tick - SampleTick)) * EnergyUnitsPerTick;

        public void Sample(long tick, long completeEnergy, long energyLimit = long.MaxValue)
        {
            long elapsed = Math.Max(0, tick - SampleTick);
            long requested = RequestedEnergy(tick);
            SampleTick = tick;
            if (!Production.Active || Production.WaitingForOutput || SupplyRatio <= 0f) return;
            long energy = Math.Min(requested, Math.Max(0, energyLimit));
            Production.Advance(0, true, energy, completeEnergy);
            AnimationPhase += elapsed * (double)SimulationTickWorld.FixedSimulationDeltaSeconds * SupplyRatio
                * (requested > 0 ? (double)energy / requested : 0);
        }

        public long Deadline(long currentTick, long completeEnergy)
        {
            if (!Production.Active || Production.WaitingForOutput) return currentTick + 1;
            if (EnergyUnitsPerTick <= 0) return long.MaxValue;
            long remaining = Math.Max(0, completeEnergy - SnapshotEnergy(currentTick, completeEnergy));
            return DeadlineForEnergy(currentTick, remaining, EnergyUnitsPerTick);
        }

        public static long DeadlineForEnergy(long tick, long energy, long rate)
        {
            if (rate <= 0) return long.MaxValue;
            long delay = Math.Max(1, energy / rate + (energy % rate > 0 ? 1 : 0));
            return delay >= long.MaxValue - tick ? long.MaxValue : tick + delay;
        }

        public long SnapshotEnergy(long tick, long completeEnergy, long energyLimit = long.MaxValue)
        {
            MiningProcess snapshot = this;
            snapshot.Sample(tick, completeEnergy, energyLimit);
            return Math.Min(completeEnergy, snapshot.Production.ConsumedEnergyUnits);
        }
    }
}
