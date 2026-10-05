using System;
using ProjectF.Benchmark;

public sealed partial class ProductionFacilityInstance : IBenchmarkWorkProgressTarget
{
    public bool TryRandomizeWorkProgress(Random random)
    {
        if (!IsRuntimeActive) return false;
        if (Template.IsOilDrill)
        {
            // Fast forced drills are continuous aggregate flow, not individual work cycles.
            if (!IsWorking || OilFastForced || Io.productionOutputFluidUnits > 0 || !TryResolveOilResource(out var resource)) return false;
            Sample();
            long complete = DeterministicSimulationUnits.FromInt(Math.Max(1, resource.GetCount));
            if (Data.Oil.ProgressUnits >= complete) return false;
            Data.Oil.ProgressUnits = Math.Min(complete - 1, (long)(complete * (decimal)random.NextDouble()));
        }
        else
        {
            if (!Data.Production.Active || Data.Production.WaitingForOutput) return false;
            Sample();
            if (!Data.Production.TrySetWorkProgress(random.NextDouble(), Data.CompleteEnergy, Data.DurationTicks)) return false;
        }
        World.MarkDisplayDirty(this); Wake(); return true;
    }
}
