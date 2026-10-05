using System;
using ProjectF.Benchmark;

public sealed partial class MiningMachineInstance : IBenchmarkWorkProgressTarget
{
    public bool TryRandomizeWorkProgress(Random random)
    {
        if (!IsRuntimeActive || !Data.Clock.Production.Active || Data.Clock.Production.WaitingForOutput || Data.PendingHarvestedItems > 0) return false;
        Sample();
        if (!Data.Clock.Production.TrySetWorkProgress(random.NextDouble(), Template.CompleteEnergy, 0)) return false;
        Wake();
        return true;
    }
}
