using System;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Simulation;

public sealed partial class MiningMachineInstance
{
    private bool samplingFuel;
    private InputOutputModule.PersistentState Io => Placement.inputOutputState;
    private Vector3 ConsumeWorldPosition => RootMatrix.MultiplyPoint3x4(Template.ConsumePoint);
    public bool RequiresFacilityPowerEvaluation => Template.Watts > 0f && !ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
    private long SnapshotFuelLimit => Template.UsesFuel && !Data.FuelBypassed
        ? FacilityFuel.Stored(Io, Template.EnergyType) : long.MaxValue;

    public long NextUpdateTick
    {
        get
        {
            if (!IsRuntimeActive) return long.MaxValue;
            long now = MapObjectTickManager.CurrentSimulationTick;
            if (Data.NeedsEvaluation) return now + 1;
            if (!IsWorking) return long.MaxValue;
            long next = Data.Clock.Deadline(now, Template.CompleteEnergy);
            if (Template.UsesFuel && !Data.FuelBypassed)
            {
                long remaining = Math.Max(0, FacilityFuel.Stored(Io, Template.EnergyType) - Data.Clock.RequestedEnergy(now));
                next = Math.Min(next, MiningProcess.DeadlineForEnergy(now, remaining, Data.Clock.EnergyUnitsPerTick));
            }
            return next;
        }
    }

    private void InvalidatePowerDemand()
    {
        if (Template.Watts > 0f) UtilityPole.InvalidateDataConsumerDemand(this);
    }

    private void PublishSupply(float ratio, bool benchmark, long now)
    {
        Data.Clock.SupplyRatio = ratio;
        Data.Clock.EnergyUnitsPerTick = ratio == 1f ? Template.WorkUnitsPerTick
            : DeterministicSimulationUnits.RateForTicks(Template.WorkRate * ratio, 1);
        Data.Clock.SampleTick = now;
        Data.FuelBypassed = benchmark;
    }

    private float ResolveSupplyRatio(bool benchmark, bool allowRefill)
    {
        if (benchmark) return 1f;
        if (!Template.UsesFuel)
        {
            UtilityPole.TryGetElectricSupplyRatio(this, Template.Watts, out float ratio);
            return ratio;
        }
        if (FacilityFuel.Stored(Io, Template.EnergyType) > 0) return 1f;
        if (!allowRefill) return 0f;
        samplingFuel = true;
        try { return FacilityFuel.Refill(this, Template.EnergyType, ConsumeWorldPosition, Template.InputConsumeMoveInterval) ? 1f : 0f; }
        finally { samplingFuel = false; }
    }

    private void Sample()
    {
        if (samplingFuel) return;
        if (Data.FuelBypassed) SampleBenchmarkEnergyVisuals();
        long now = MapObjectTickManager.CurrentSimulationTick;
        long limit = long.MaxValue;
        // Waiting output consumes no fuel, matching the native mining state machine.
        if (Template.UsesFuel && !Data.FuelBypassed && Data.Clock.Production.Active
            && !Data.Clock.Production.WaitingForOutput && Data.Clock.SupplyRatio > 0f)
        {
            long requested = Math.Min(Data.Clock.RequestedEnergy(now), Math.Max(0, Template.CompleteEnergy - Data.Clock.Production.ConsumedEnergyUnits));
            if (requested > 0)
            {
                samplingFuel = true;
                try { limit = FacilityFuel.Spend(this, Template.EnergyType, requested, ConsumeWorldPosition, Template.InputConsumeMoveInterval); }
                finally { samplingFuel = false; }
            }
        }
        Data.Clock.Sample(now, Template.CompleteEnergy, limit);
    }

    internal void SampleBenchmarkEnergyVisuals()
    {
        if (Template.UsesFuel && ProjectF.Benchmark.BenchmarkRuntime.ForceWorking)
            ProjectF.Benchmark.BenchmarkInputSupply.SampleEnergy(this, ConsumeWorldPosition, IsWorking);
    }

    public void GetFuelGauge(out long current, out long capacity)
    {
        current = capacity = 0;
        if (!IsRuntimeActive || !Template.UsesFuel) return;
        int index = Io.storedEnergyTypes.IndexOf((int)Template.EnergyType);
        if (index < 0) return;
        current = Io.storedEnergyUnitsByType[index]; capacity = Io.energyGaugeCapacityUnitsByType[index];
        if (IsWorking && !Data.FuelBypassed)
            current = Math.Max(0, current - Math.Min(Data.Clock.RequestedEnergy(MapObjectTickManager.CurrentSimulationTick),
                Math.Max(0, Template.CompleteEnergy - Data.Clock.Production.ConsumedEnergyUnits)));
    }

    public bool TryGetFuelInputInfo(out int item, out int count, out int capacity, out int burnEnergy)
    {
        if (Template.UsesFuel) return FacilityFuel.GetInputInfo(this, out item, out count, out capacity, out burnEnergy);
        item = -1; count = capacity = burnEnergy = 0; return false;
    }

    internal void ClearStoredEnergyAndProduction()
    {
        Data.Clock.SampleTick = MapObjectTickManager.CurrentSimulationTick;
        Data.Clock.Production.Clear(); Data.Clock.SupplyRatio = 0;
        Data.PendingHarvestedItems = 0; Data.Resource = default;
        Io.storedEnergyTypes.Clear(); Io.storedEnergyUnitsByType.Clear(); Io.energyGaugeCapacityUnitsByType.Clear();
        FacilityFuel.Persist(Io);
        Wake(); Persist();
    }
}
