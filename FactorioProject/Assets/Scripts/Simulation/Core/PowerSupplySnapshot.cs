using System;

namespace ProjectF.Simulation
{
    /// <summary>Published power rates. Network topology and source discovery are separate adapters.</summary>
    public struct PowerSupplySnapshot
    {
        private const float Epsilon = 0.0001f;
        public float ProductionWatts, RequiredWatts;
        public bool HasPowerSource;
        public float SupplyRatio => !HasPowerSource ? 0f : RequiredWatts > Epsilon
            ? ClampRatio(ProductionWatts / RequiredWatts) : ProductionWatts > Epsilon ? 1f : 0f;

        // Preserves the existing proportional-rate rule. This is not an energy reservoir:
        // callers must submit the appropriate elapsed-time request only once per simulation update.
        public long GrantEnergy(long requestedUnits, float requestedWatts)
        {
            if (!HasPowerSource || requestedUnits <= 0 || requestedWatts <= Epsilon) return 0;
            return DeterministicSimulationUnits.MultiplyRatio(requestedUnits,
                DeterministicSimulationUnits.FromFloat(ProductionWatts),
                DeterministicSimulationUnits.FromFloat(Math.Max(requestedWatts, RequiredWatts)));
        }

        public float GetConsumerRatio(float requestedWatts, bool includedInDemand)
        {
            if (!HasPowerSource || ProductionWatts <= Epsilon) return 0;
            float demand = includedInDemand ? Math.Max(RequiredWatts, Math.Max(0, requestedWatts))
                : RequiredWatts + Math.Max(0, requestedWatts);
            return demand > Epsilon ? ClampRatio(ProductionWatts / demand) : 1f;
        }
        private static float ClampRatio(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
