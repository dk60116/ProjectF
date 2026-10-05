using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Benchmark;
using ProjectF.Simulation;

namespace ProjectF.MapObjects
{
public sealed class LoggingMachineInstance : ForestryInstance, ILoggingTarget, IBenchmarkWorkProgressTarget
{
    private readonly LoggingFilter filter;
    private readonly List<KeyValuePair<int, int>> seedDrops = new List<KeyValuePair<int, int>>(2);
    private LoggingMachine Source => (LoggingMachine)Prototype;
    private ref LoggingProcess Process => ref Data.Logging;
    private Vector2Int TargetCoordinate
    {
        get => new Vector2Int(Process.TargetCoordinate.X, Process.TargetCoordinate.Y);
        set => Process.TargetCoordinate = new GridCell(value.x, value.y);
    }
    internal override bool KeepScheduled => Data.HasDemand && !Data.PowerBlocked;
    internal override bool HasPowerDemand => Data.HasDemand;
    internal override bool IsWorking => Data.Working;
    internal float HingeAngle => Process.HingeAngle;
    public float WorkProgress => Template.CompleteEnergyUnits > 0
        ? Mathf.Clamp01((float)((double)Process.ConsumedEnergyUnits / Template.CompleteEnergyUnits)) : 0;
    private LoggingFilter Filter => filter;
    public bool IsTreeTypeEnabled(ResourceDefinition definition) => Filter.IsTreeTypeEnabled(definition);
    public void SetTreeTypeEnabled(ResourceDefinition definition, IReadOnlyList<ResourceDefinition> available, bool enabled) => Filter.SetTreeTypeEnabled(definition, available, enabled);
    public void SetAllTreeTypes(IReadOnlyList<ResourceDefinition> available, bool enabled) => Filter.SetAllTreeTypes(available, enabled);
    public void SetGrowthRange(int minimum, int maximum) => Filter.SetGrowthRange(minimum, maximum);
    public List<string> CaptureEnabledTreeDefinitionKeys() => Filter.CaptureEnabledTreeDefinitionKeys();
    public void ApplyTreeFilterState(bool initialized, IReadOnlyList<string> keys, int minimum, int maximum = LoggingMachine.DefaultMaximumGrowth)
        => Filter.ApplyTreeFilterState(initialized, keys, minimum, maximum);
    public int MinimumGrowth => Filter.MinimumGrowth;
    public int MaximumGrowth => Filter.MaximumGrowth;
    public bool IsTreeFilterInitialized => Filter.IsTreeFilterInitialized;
    internal LoggingMachineInstance(ForestryWorld world, int index, uint generation, MapObjectHandle handle,
        LoggingMachine source, BlockStateStore.InstallationSaveState placement, ForestryRenderTemplate template)
        : base(world, index, generation, handle, source, placement, template)
    {
        filter = new LoggingFilter(placement, InvalidateFilteredTarget);
        Process = placement.loggingProcess;
        Process.Direction = ((Process.Direction % 4) + 4) % 4;
        if (Process.HasTarget && TargetCoordinate != LoggingMachine.GetHarvestCoordinate(
            AnchorCoordinate, placement.quarterTurns, Process.Direction)) Process.ClearTarget();
        Process.ConsumedEnergyUnits = Math.Max(0, Math.Min(Process.ConsumedEnergyUnits, template.CompleteEnergyUnits));
    }
    private void InvalidateFilteredTarget()
    { if (!IsRuntimeActive) return; Process.ClearTarget(); Data.ResourceIdentity = default; Data.Working = false; Wake(); }
    private bool TryTree(int direction, out ResourceInstance tree)
    {
        var coordinate = LoggingMachine.GetHarvestCoordinate(AnchorCoordinate, Placement.quarterTurns, direction);
        tree = null;
        if (!World.Terrain.TryGetLoadedBlock(coordinate, out var block) || block == null) return false;
        tree = block.Resource;
        if (tree == null || !tree.IsRuntimeActive || !tree.CanHarvest
            || tree.ResolvedHarvestMode != Resource.HarvestMode.Logging || !IsTreeTypeEnabled(tree.Definition)) return false;
        float growth = tree is TreeInstance living ? living.Growth : ResourceDefinition.MaxGrowth;
        return growth >= MinimumGrowth && growth <= MaximumGrowth;
    }
    private bool HasTree()
    { for (int i = 0; i < 4; i++) if (TryTree(i, out _)) return true; return false; }
    protected override void ApplyTick(float deltaTime)
    {
        Data.Working = false;
        bool benchmark = BenchmarkRuntime.ForceWorking;
        if (!benchmark && Process.HasTarget
            && (!World.Terrain.TryGetLoadedBlock(TargetCoordinate, out var targetBlock) || targetBlock == null))
        {
            // Do not abandon a streamed-out target just because another direction has a tree.
            Data.HasDemand = false; Data.PowerBlocked = false; Data.SupplyRatio = 0;
            return;
        }
        Data.HasDemand = benchmark || HasTree();
        PublishPowerDemand();
        Data.PowerBlocked = false; Data.SupplyRatio = 0;
        if (!Data.HasDemand)
        {
            Process.ClearTarget(); Data.ResourceIdentity = default;
            return;
        }
        if (!benchmark && !UtilityPole.HasElectricityAvailable(this))
        { Data.PowerBlocked = true; return; }
        Process.HingeAngle = Mathf.MoveTowardsAngle(Process.HingeAngle, Process.Direction * 90f,
            Mathf.Max(1, Source.HingeRotationDegreesPerSecond) * deltaTime);
        if (Mathf.Abs(Mathf.DeltaAngle(Process.HingeAngle, Process.Direction * 90f)) > 0.1f) return;
        ResourceInstance tree = null;
        if (!benchmark && !TryTree(Process.Direction, out tree))
        {
            Process.ClearTarget(); Data.ResourceIdentity = default;
            Process.EmptyDirectionElapsed += deltaTime;
            if (Process.EmptyDirectionElapsed >= Mathf.Max(0, Source.EmptyDirectionHoldSeconds)) AdvanceDirection();
            return;
        }
        Process.EmptyDirectionElapsed = 0;
        if (!benchmark)
        {
            var coordinate = LoggingMachine.GetHarvestCoordinate(AnchorCoordinate, Placement.quarterTurns, Process.Direction);
            string key = LoggingFilter.DefinitionKey(tree.Definition);
            VirtualObjectWorld.Current.TryGetResourceHandle(coordinate, out var identity);
            bool same = Process.HasTarget && TargetCoordinate == coordinate && Process.TargetDefinitionKey == key
                && (!Data.ResourceIdentity.IsValid || Data.ResourceIdentity.Equals(identity));
            if (!same) Process.ConsumedEnergyUnits = 0;
            Process.HasTarget = true; TargetCoordinate = coordinate; Process.TargetDefinitionKey = key;
            Data.ResourceIdentity = identity;
        }
        float requested = Template.Watts * deltaTime, consumed = requested;
        if (!benchmark && !UtilityPole.TryConsumeElectricity(this, requested, deltaTime, out consumed))
        { Data.PowerBlocked = true; return; }
        Data.SupplyRatio = requested > 0 ? Mathf.Clamp01(consumed / requested) : 1;
        Data.Working = true;
        Process.ConsumedEnergyUnits += DeterministicSimulationUnits.FromFloat(consumed);
        if (Process.ConsumedEnergyUnits < Template.CompleteEnergyUnits) return;
        if (benchmark) BenchmarkRuntime.EmitItem(World.Terrain, BenchmarkRuntime.FallbackItemId, WorldPosition);
        else LoggingHarvest.CompleteTreeHarvest(tree, seedDrops);
        Process.ClearTarget(); Data.ResourceIdentity = default; Data.Working = false; AdvanceDirection();
        Data.HasDemand = benchmark || HasTree();
    }
    private void AdvanceDirection()
    { Process.Direction = (Process.Direction + 1) % 4; Process.EmptyDirectionElapsed = 0; }
    public void GetObjectInfoStatus(out string text, out bool working, out bool warning)
    {
        working = IsRuntimeActive && Data.Working; warning = false;
        if (!IsRuntimeActive) text = "No placement";
        else if (!HasTree() && !BenchmarkRuntime.ForceWorking) { text = "No tree"; warning = true; }
        else text = UtilityPole.HasElectricityAvailable(this) ? "Working" : "No energy";
    }
    public override bool TryRandomizeWorkProgress(System.Random random)
    {
        if (!IsRuntimeActive || !Data.Working || Template.CompleteEnergyUnits <= 0) return false;
        Process.ConsumedEnergyUnits = Math.Min(Template.CompleteEnergyUnits - 1,
            (long)(Template.CompleteEnergyUnits * (decimal)random.NextDouble()));
        Persist(); Wake(); return true;
    }
    internal override void ClearItems() => InvalidateFilteredTarget();
    public override void Persist() { if (IsRuntimeActive) Placement.loggingProcess = Process; }
}
}
