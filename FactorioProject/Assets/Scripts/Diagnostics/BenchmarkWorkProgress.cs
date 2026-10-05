using System;
using ProjectF.Simulation;

namespace ProjectF.Benchmark
{
    public interface IBenchmarkWorkProgressTarget
    {
        bool TryRandomizeWorkProgress(Random random);
    }

    internal static class BenchmarkWorkProgress
    {
        // Debug/harness entry point: preserve recipe, pending output and item ownership.
        internal static bool TrySetWorkProgress(this ref ProductionProcess process, double fraction, long completeEnergy, long durationTicks)
        {
            if (!process.Active || process.WaitingForOutput || double.IsNaN(fraction) || double.IsInfinity(fraction)) return false;
            fraction = Math.Max(0d, Math.Min(1d, fraction));
            if (completeEnergy > 0)
                process.ConsumedEnergyUnits = Math.Min(completeEnergy - 1, (long)((decimal)completeEnergy * (decimal)fraction));
            else if (durationTicks > 0)
                process.RemainingTicks = Math.Max(1, durationTicks - (long)((decimal)durationTicks * (decimal)fraction));
            else return false;
            return true;
        }
    }
}
