using System;
using UnityEngine;
using ProjectF.Simulation;

public sealed partial class ProductionFacilityInstance
{
    internal bool OilRegistered, OilScheduled, OilQueued, OilFastForced;
    internal long OilFastUnitsPerTick;
    internal long OilRateUnitsPerTick => Data.OilUnitsPerTick;
    internal int OilActiveIndex = -1;
    public bool IsSimulationScheduled => Template.IsOilDrill ? OilScheduled : FacilitySimulationWorld.IsScheduled(this);
    internal Vector2Int OilTargetCoordinate { get; }
    private bool OilWaitingForOutput => !Data.HasTarget && Io.productionOutputFluidUnits > 0;
    private float OilWorkProgress => Mathf.Clamp01(DeterministicSimulationUnits.ToFloat(Data.Oil.ProgressUnits)
        / (TryResolveOilResource(out var resource) ? Math.Max(1, resource.GetCount) : 1));

    internal bool TryGetOilResourceReserves(out int liters)
    {
        liters = 0;
        if (!Template.IsOilDrill) return false;
        if (TryResolveOilResource(out var resource)) liters = resource.RemainingMachineHarvestOutputCount;
        return true;
    }
    private bool TryResolveOilResource(out ResourceInstance resource)
    {
        resource = null;
        // Resolve through the coordinate/handle index: a released or replaced resource is never reused.
        if (!ResourceInstance.TryGetActiveResourceAtCoordinate(World.Terrain, OilTargetCoordinate, out var found)
            || found.PlacementCategory != ResourceDefinition.PlacementCategory.Oil) return false;
        resource = found;
        return true;
    }
    private void ApplyOilDrillingTick()
    {
        ref var state = ref Data;
        bool benchmark = ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
        var recipe = SelectedRecipe;
        if (recipe == null || recipe.OutputRate <= 0 || OutputCoordinates.Count == 0)
        { SetOilTarget(false, 0, 0); Sleep(); return; }

        // A durable output buffer prevents a partial transfer from losing a harvested batch.
        if (Io.productionOutputFluidUnits > 0) World.TransferOutputFluid(this, recipe);
        ResourceInstance resource = null;
        bool hasResource = !benchmark && TryResolveOilResource(out resource) && resource.CanHarvest;
        bool compatibleResource = hasResource && resource.TryPeekMachineHarvestOutput(out int resourceItem, out _) && resourceItem == recipe.OutputId;
        int count = hasResource ? Math.Max(1, resource.GetCount) : 1;
        float retention = 1, available = 0;
        if (!benchmark) World.GetOilOutputState(this, recipe.OutputId, recipe.OutputRate, out available, out retention);
        bool hasSpace = benchmark || available + .0001f >= count;
        bool target = benchmark || compatibleResource && hasSpace && retention > 0;
        float ratio = target ? ResolveSupplyRatio(benchmark) : 0;
        SetOilTarget(target, ratio, retention);

        if (benchmark && Io.productionOutputFluidUnits == 0 && !World.HasFluidOutputTargets(this))
        {
            if (state.Oil.ProgressUnits > 0)
            { ProjectF.Benchmark.BenchmarkRuntime.RecordSpill(DeterministicSimulationUnits.ToFloat(state.Oil.ProgressUnits)); state.Oil.ProgressUnits = 0; }
            World.OilBatch.EnterFast(this);
            return;
        }

        if (benchmark)
        {
            // Keep real harvested oil across benchmark toggles. Only synthetic output may spill.
            long harvestedPending = Io.productionOutputFluidUnits;
            Io.productionOutputFluidUnits = state.Oil.ProgressUnits;
            state.Oil.ProgressUnits = 0;
            if (Io.productionOutputFluidUnits > 0)
            {
                long before = Io.productionOutputFluidUnits;
                World.TransferOutputFluid(this, recipe, DeterministicSimulationUnits.ToFloat(before));
                ProjectF.Benchmark.BenchmarkRuntime.RecordSpill(DeterministicSimulationUnits.ToFloat(Io.productionOutputFluidUnits));
            }
            Io.productionOutputFluidUnits = harvestedPending;
        }
        else if (target && ratio > 0 && Io.productionOutputFluidUnits == 0 && state.Oil.CanHarvest(count)
            && resource.TryPeekMachineHarvestOutput(out int output, out int expected) && output == recipe.OutputId && expected == count)
        {
            if (resource.TryHarvestForMachine(out int item, out int harvested) && item == recipe.OutputId && harvested > 0)
            {
                state.Oil.CompleteHarvest(harvested);
                Io.productionOutputFluidUnits = DeterministicSimulationUnits.FromInt(harvested);
                World.TransferOutputFluid(this, recipe, harvested);
                World.MarkDisplayDirty(this);
                if (!resource.CanHarvest) { target = false; SetOilTarget(false, 0, retention); }
            }
        }
        // Blocked producers do not accumulate an unbounded backlog or burn standby fuel.
        if (!target && hasResource) state.Oil.ProgressUnits = Math.Min(state.Oil.ProgressUnits, DeterministicSimulationUnits.FromInt(count));
        if (target && ratio > 0 || Io.productionOutputFluidUnits > 0 && World.GetOutputAvailableLiters(this, recipe.OutputId) > 0) Reschedule(true); else Sleep();
    }
    private void SetOilTarget(bool target, float ratio, float retention)
    {
        ref var state = ref Data;
        bool displayChanged = state.HasTarget != target || state.SupplyRatio != ratio;
        if (displayChanged || state.OilRetention != retention)
            state.OilUnitsPerTick = target && ratio > 0
                ? DeterministicSimulationUnits.RateForTicks(Template.OilLitersPerSecond * retention * ratio, 1) : 0;
        if (state.HasTarget != target) { state.HasTarget = target; UtilityPole.InvalidateDataConsumerDemand(this); }
        state.SupplyRatio = ratio; state.OilRetention = retention;
        if (displayChanged) World.MarkDisplayDirty(this);
    }
    internal void ResetOilBenchmarkWork()
    {
        if (!IsRuntimeActive || !Template.IsOilDrill) return;
        World.OilBatch.LeaveFast(this);
        Data.Oil.ProgressUnits = 0;
        Data.SampleTick = MapObjectTickManager.CurrentSimulationTick;
        SetOilTarget(false, 0, 0); Wake();
    }
    private void GetOilDrillingStatus(out string text, out bool working, out bool warning)
    {
        working = IsWorking; warning = false;
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking) text = working ? "Working" : "No production rate";
        else if (!TryResolveOilResource(out var resource)) text = "No oil deposit";
        else if (!resource.CanHarvest) text = "Oil depleted";
        else if (Template.OilLitersPerSecond <= 0) text = "No production rate";
        else if (World.GetOutputAvailableLiters(this, OutputItemId) + .0001f < Math.Max(1, resource.GetCount))
        { text = "Waiting for output"; warning = true; }
        else if (Data.SupplyRatio <= 0) text = "No energy";
        else text = "Working";
    }
}
