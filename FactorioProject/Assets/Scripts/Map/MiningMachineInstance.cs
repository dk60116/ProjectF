using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Simulation;

public sealed partial class MiningMachineInstance : IMapObjectTarget, IDataElectricConsumer,
    IMapObjectUpdateTick, IMapObjectUpdateTickDeadline, IDataItemProducer, IMapObjectSimulationIdentity, IFacilityPowerEvaluationTarget
{
    internal readonly MiningWorld World;
    internal readonly int Index;
    internal readonly uint Generation;
    internal readonly MiningRenderTemplate Template;
    internal int OrderIndex;
    private Matrix4x4 cachedRootMatrix;
    private Bounds cachedCullBounds;
    private bool geometryCached;
    private int benchmarkInputVersion = -1;
    private ref MiningWorld.State Data => ref World.GetState(Index, Generation);
    TerrainGenerator IDataItemProducer.Terrain => World.Terrain;
    BlockStateStore IDataItemProducer.Store => World.Store;
    InputOutputModule IDataItemProducer.OutputPrototype => Prototype;
    public MapObjectHandle Handle { get; }
    public MiningMachine Prototype { get; }
    public BlockStateStore.InstallationSaveState Placement { get; }
    public Vector2Int StorageKey => BlockStateStore.GetInstallationStorageKey(Placement);
    public Vector2Int AnchorCoordinate => Placement.anchorCoordinate;
    public Vector3 WorldPosition => Placement.worldPosition;
    public Quaternion WorldRotation => Placement.worldRotation;
    public bool IsRuntimeActive => World.Contains(Index, Generation) && VirtualObjectWorld.Current != null
        && VirtualObjectWorld.Current.IsHandleAlive(Handle);
    public bool IsTargetActive => IsRuntimeActive;
    public MapObject SceneObject => null;
    public string ObjectName => Prototype.ObjectName;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public ItemDefinition BoundItemDefinition => Template.Definition;
    public int ResolveItemId() => Placement.itemId;
    public int ResolvedItemId => Placement.itemId;
    public int ID => Placement.itemId;
    public long SimulationId => Placement.placementSequence;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Placement.occupiedCoordinates;
    public IReadOnlyList<Vector2Int> OutputCoordinates => Placement.inputOutputState.outputCoordinates;
    public Vector3 PowerLineWorldPosition => RootMatrix.MultiplyPoint3x4(Template.PowerLinePoint);
    public Matrix4x4 RootMatrix { get { CacheGeometry(); return cachedRootMatrix; } }
    public Bounds CullBounds { get { CacheGeometry(); return cachedCullBounds; } }
    private void CacheGeometry()
    {
        if (geometryCached) return;
        cachedRootMatrix = Matrix4x4.TRS(WorldPosition, WorldRotation, Template.Scale);
        cachedCullBounds = VirtualRenderBatchCollection.CalculateWorldBounds(Template.LocalBounds, cachedRootMatrix);
        geometryCached = true;
    }
    public float FocusActivationRadius => Prototype.FocusActivationRadius;
    internal bool PlacementPresentationSuppressed { get; set; }
    internal float PlacementPresentationScale { get; set; } = 1f;
    public bool HasActiveWork => IsRuntimeActive && Data.Clock.Production.Active;
    public bool IsWorking => HasActiveWork
        && !Data.Clock.Production.WaitingForOutput && Data.Clock.SupplyRatio > 0f;
    public float WorkProgress => HasActiveWork && Template.CompleteEnergy > 0L
        ? Mathf.Clamp01((float)((double)Data.Clock.SnapshotEnergy(MapObjectTickManager.CurrentSimulationTick,
            Template.CompleteEnergy, SnapshotFuelLimit) / Template.CompleteEnergy)) : 0f;
    internal Vector3 WorkGaugeWorldPosition => new Vector3(CullBounds.center.x,
        CullBounds.max.y + Template.WorkGaugeVerticalOffset, CullBounds.center.z);
    public int OutputItemId => IsRuntimeActive ? Data.Clock.Production.OutputItemId : -1;
    internal MiningMachineInstance(MiningWorld world, int index, uint generation, MapObjectHandle handle,
        MiningMachine prototype, BlockStateStore.InstallationSaveState placement, MiningRenderTemplate template)
    {
        World = world; Index = index; Generation = generation; Handle = handle;
        Prototype = prototype; Placement = placement; Template = template;
        Placement.inputOutputState ??= new InputOutputModule.PersistentState();
        var saved = Placement.inputOutputState;
        FacilityFuel.RestoreLegacy(saved, Template.Definition);
        Data.Clock.EnergyUnitsPerTick = Template.WorkUnitsPerTick;
        Data.ResourceCursor = saved.miningResourceCursor;
        Data.ResourceCoordinate = saved.miningResourceCoordinate;
        Data.PendingHarvestedItems = saved.miningPendingHarvestedItems;
        if (saved.hasActiveCraft && Placement.occupiedCoordinates.Contains(Data.ResourceCoordinate))
            VirtualObjectWorld.Current.TryGetResourceHandle(Data.ResourceCoordinate, out Data.Resource);
        Data.Clock.Production = new ProductionProcess { Active = saved.hasActiveCraft, WaitingForOutput = saved.waitingForOutput,
            RecipeIndex = -1, OutputItemId = saved.activeOutputItemId, OutputCount = saved.activeOutputCount,
            ConsumedEnergyUnits = saved.hasDeterministicUnits ? saved.activeCraftConsumedEnergyUnits
                : DeterministicSimulationUnits.FromFloat(saved.activeCraftConsumedEnergy) };
    }
    public bool IsItemFilterEnabled(int itemId, int count) =>
        MapObject.IsItemAllowedByFilterMask(itemId, Placement.itemFilterMaskInitialized, Placement.itemFilterMaskWords);
    public void SetItemFilterEnabled(int itemId, int count, bool enabled)
    {
        if (itemId < 0) return;
        var words = Placement.itemFilterMaskWords;
        int required = (Math.Max(count, itemId + 1) + 63) >> 6;
        while (words.Count < required) words.Add(ulong.MaxValue);
        Placement.itemFilterMaskInitialized = true;
        if (enabled) words[itemId >> 6] |= 1UL << (itemId & 63); else words[itemId >> 6] &= ~(1UL << (itemId & 63));
        Wake();
    }
    public bool TryGetElectricPowerRequirement(out float watts) { watts = Template.Watts; return IsRuntimeActive && watts > 0f; }
    public bool TryGetElectricPowerDemand(out float watts)
    { watts = Template.Watts; return IsRuntimeActive && watts > 0f && Data.HasTarget && OutputCoordinates.Count > 0; }
    public void WakeForElectricPowerChange() => Wake();
    public void Wake()
    {
        if (samplingFuel) return;
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking && IsRuntimeActive
            && benchmarkInputVersion != ProjectF.Benchmark.BenchmarkInputSupply.Version)
        {
            benchmarkInputVersion = ProjectF.Benchmark.BenchmarkInputSupply.Version;
            ProjectF.Benchmark.BenchmarkInputSupply.AddEnergy(this, World.Terrain, World.Store,
                Placement.inputOutputState.inputEnergyCoordinates, Template.Definition, Prototype.RuntimeAreaMaxObjects);
        }
        if (!IsRuntimeActive) return;
        if (Data.NeedsEvaluation) return;
        // Materialize work under the previous published rate before replacing it.
        Sample();
        Data.NeedsEvaluation = true;
        if (FacilitySimulationWorld.IsScheduled(this)) FacilitySimulationWorld.RefreshSchedule(this);
        else FacilitySimulationWorld.SetScheduled(this, true);
    }
    public void ManagedUpdateTick(float ignoredDelta)
    {
        if (!IsRuntimeActive) { FacilitySimulationWorld.Unregister(this); return; }
        World.ProcessedUpdates++;
        Data.NeedsEvaluation = false;
        long now = MapObjectTickManager.CurrentSimulationTick;
        Sample();
        bool benchmark = ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
        bool hasResource = TryResolveResource(out ResourceInstance resource, out MapObjectHandle resourceHandle,
            out Vector2Int resourceCoordinate, out int item, out int count);
        // A streamed-out resource is unavailable, not depleted. Preserve its unfinished cycle.
        if (!benchmark && !hasResource && Data.PendingHarvestedItems <= 0 && Data.Clock.Production.Active
            && Data.Resource.IsValid && !ResourceInstance.TryGetActiveResourceAtCoordinate(World.Terrain, Data.ResourceCoordinate, out _)
            && World.Store.TryGet(Data.ResourceCoordinate, out var savedResource) && savedResource.resourceCount > 0)
        {
            Data.Clock.SupplyRatio = 0f;
            if (Data.HasTarget) { Data.HasTarget = false; InvalidatePowerDemand(); }
            Sleep(); return;
        }
        bool target = benchmark || OutputCoordinates.Count > 0 && (hasResource || Data.Clock.Production.Active);
        if (Data.HasTarget != target) { Data.HasTarget = target; InvalidatePowerDemand(); }
        float ratio = target && Data.Clock.Production.Active ? ResolveSupplyRatio(benchmark, !Data.Clock.Production.WaitingForOutput) : 0f;
        PublishSupply(ratio, benchmark, now);
        if (!target)
        { Data.Clock.Production.Clear(); Data.Clock.SupplyRatio = 0f; Sleep(); return; }
        if (benchmark && !hasResource) { item = BenchmarkOutputId; count = 1; }
        if (Data.Clock.Production.Active && !Data.Clock.Production.WaitingForOutput && ratio <= 0f)
        { Sleep(); return; }
        if (Data.Clock.Production.WaitingForOutput)
        {
            int expectedItem = Data.Clock.Production.OutputItemId, expectedCount = Data.Clock.Production.OutputCount;
            if (!benchmark && Data.PendingHarvestedItems <= 0 && (!hasResource || item != expectedItem || count != expectedCount))
            {
                Data.Clock.Production.Clear(); Data.Resource = default;
                hasResource = TryResolveResource(out resource, out resourceHandle, out resourceCoordinate, out item, out count);
            }
            else
            {
                // Check the complete output batch before any resource is consumed.
                int remaining = Data.PendingHarvestedItems > 0 ? Data.PendingHarvestedItems : expectedCount;
                if (!MiningItemOutput.TryReserve(this, expectedItem, remaining, out var output))
                {
                    if (!benchmark) { Sleep(); return; }
                    for (int i = 0; i < remaining; i++) ProjectF.Benchmark.BenchmarkRuntime.EmitItem(World.Terrain, expectedItem, WorldPosition);
                    Data.PendingHarvestedItems = 0; Data.Clock.Production.Clear(); Wake(); return;
                }
                if (!benchmark && Data.PendingHarvestedItems <= 0 && !resource.TryHarvestForMachine(out item, out count))
                { Data.Clock.Production.Clear(); Data.Resource = default; Wake(); return; }
                // A failed presentation must retain harvested items for retry; it must never harvest twice.
                Data.PendingHarvestedItems = remaining;
                if (!MiningItemOutput.Emit(this, output, expectedItem, ref Data.PendingHarvestedItems,
                    resource != null ? resource.FocusPoint : WorldPosition)) { Sleep(); return; }
                if (benchmark) ProjectF.Benchmark.BenchmarkRuntime.RecordItems(remaining);
                Data.ResourceCursor = (Placement.occupiedCoordinates.IndexOf(resourceCoordinate) + 1) % Math.Max(1, RuntimeOccupiedCoordinates.Count);
                Data.Clock.Production.Clear(); Data.Resource = default;
                hasResource = TryResolveResource(out resource, out resourceHandle, out resourceCoordinate, out item, out count);
                if (benchmark && !hasResource) { item = BenchmarkOutputId; count = 1; }
            }
        }
        if (!Data.Clock.Production.Active && target && (hasResource || benchmark))
        {
            ratio = ResolveSupplyRatio(benchmark, true);
            PublishSupply(ratio, benchmark, now);
        }
        if (!Data.Clock.Production.Active && (hasResource || benchmark) && ratio > 0f && item >= 0 && count > 0)
        {
            Data.Resource = resourceHandle; Data.ResourceCoordinate = resourceCoordinate;
            Data.Clock.Production.Begin(-1, item, count, 0);
        }
        bool nextTarget = benchmark || OutputCoordinates.Count > 0 && (hasResource || Data.Clock.Production.Active);
        if (Data.HasTarget != nextTarget) { Data.HasTarget = nextTarget; InvalidatePowerDemand(); }
        if (Data.Clock.Production.Active && ratio > 0f) FacilitySimulationWorld.RefreshSchedule(this);
        else Sleep();
    }
    private void Sleep()
    { FacilitySimulationWorld.SetScheduled(this, false); }
    private int BenchmarkOutputId => Template.BenchmarkOutputId >= 0 ? Template.BenchmarkOutputId
        : ProjectF.Benchmark.BenchmarkRuntime.FallbackItemId;
    private bool TryResolveResource(out ResourceInstance resource, out MapObjectHandle handle,
        out Vector2Int coordinate, out int item, out int count)
    {
        resource = null; handle = default; coordinate = default; item = -1; count = 0;
        if (Data.Resource.IsValid && TryResource(Data.ResourceCoordinate, Data.Resource, out resource, out item, out count))
        { handle = Data.Resource; coordinate = Data.ResourceCoordinate; return true; }
        var occupied = RuntimeOccupiedCoordinates;
        for (int i = 0; i < occupied.Count; i++)
        {
            coordinate = occupied[(Math.Max(0, Data.ResourceCursor) + i) % occupied.Count];
            if (VirtualObjectWorld.Current.TryGetResourceHandle(coordinate, out handle)
                && TryResource(coordinate, handle, out resource, out item, out count)) return true;
        }
        return false;
    }
    private bool TryResource(Vector2Int coordinate, MapObjectHandle handle, out ResourceInstance resource, out int item, out int count)
    {
        item = -1; count = 0; resource = null;
        return VirtualObjectWorld.Current.IsHandleAlive(handle)
            && ResourceInstance.TryGetActiveResourceAtCoordinate(World.Terrain, coordinate, out resource)
            && resource.IsRuntimeActive && resource.CanHarvest && resource.TryPeekMachineHarvestOutput(out item, out count)
            && IsItemFilterEnabled(item, 0)
            && (!Data.Clock.Production.Active || Data.Clock.Production.OutputItemId == item && Data.Clock.Production.OutputCount == count);
    }
    internal double AnimationPhase
    {
        get
        {
            MiningProcess clock = Data.Clock;
            clock.Sample(MapObjectTickManager.CurrentSimulationTick, Template.CompleteEnergy, SnapshotFuelLimit);
            return clock.AnimationPhase;
        }
    }
    public void GetObjectInfoStatus(out string text, out bool working, out bool warning)
    {
        working = IsWorking; warning = false;
        if (OutputCoordinates.Count == 0) text = "No output area";
        else if (!Data.HasTarget) text = "No resource";
        else if (Data.Clock.Production.WaitingForOutput) { text = "Waiting for output"; warning = true; }
        else if (Data.Clock.SupplyRatio <= 0f) text = "No energy";
        else text = "Working";
    }
    public bool TryGetObjectInfoResourceReserves(out int reserves)
    {
        reserves = 0;
        for (int i = 0; i < RuntimeOccupiedCoordinates.Count; i++)
            if (ResourceInstance.TryGetActiveResourceAtCoordinate(World.Terrain, RuntimeOccupiedCoordinates[i], out var resource))
            {
                bool seen = false;
                for (int j = 0; j < i; j++)
                    if (ResourceInstance.TryGetActiveResourceAtCoordinate(World.Terrain, RuntimeOccupiedCoordinates[j], out var previous)
                        && ReferenceEquals(previous, resource)) { seen = true; break; }
                if (!seen) reserves += resource.RemainingMachineHarvestOutputCount;
            }
        return true;
    }
    public void GetOutputInfo(out int item, out int count, out int capacity)
    {
        item = OutputItemId; count = 0; capacity = 0;
        if (item < 0 && TryResolveResource(out _, out _, out _, out int nextItem, out _)) item = nextItem;
        var definition = InputOutputModule.ResolveItemDefinition(item);
        for (int i = 0; i < OutputCoordinates.Count; i++)
        {
            var coordinate = OutputCoordinates[i];
            if (World.Terrain.TryGetLoadedBlock(coordinate, out var block))
            {
                count = (int)Math.Min(int.MaxValue, (long)count + block.GetInputAreaCenterItemCount(item));
                capacity = (int)Math.Min(int.MaxValue, (long)capacity + block.GetInputAreaCenterCapacity(item));
            }
            else
            {
                count = (int)Math.Min(int.MaxValue, (long)count + World.Store.GetSavedCenterItemCount(coordinate));
                int stackCapacity = ProjectF.Benchmark.BenchmarkRuntime.ForceWorking
                    ? int.MaxValue : ItemDefinition.ResolveStackCapacity(definition, Prototype.RuntimeAreaMaxObjects);
                capacity = (int)Math.Min(int.MaxValue, (long)capacity + stackCapacity);
            }
        }
    }
    public void Persist()
    {
        if (!IsRuntimeActive) return;
        Sample();
        var dto = Placement.inputOutputState; var process = Data.Clock.Production;
        dto.hasActiveCraft = process.Active; dto.waitingForOutput = process.WaitingForOutput;
        dto.miningResourceCursor = Data.ResourceCursor; dto.miningResourceCoordinate = Data.ResourceCoordinate;
        dto.miningPendingHarvestedItems = Data.PendingHarvestedItems;
        dto.activeOutputItemId = process.OutputItemId; dto.activeOutputCount = process.OutputCount;
        dto.hasDeterministicUnits = true; dto.activeCraftConsumedEnergyUnits = process.ConsumedEnergyUnits;
        dto.activeCraftConsumedEnergy = DeterministicSimulationUnits.ToFloat(process.ConsumedEnergyUnits);
        dto.remainingCraftTicks = ProductionProcess.RemainingEnergyTicks(Template.CompleteEnergy, process.ConsumedEnergyUnits,
            DeterministicSimulationUnits.FromFloat(Template.WorkRate));
        dto.remainingCraftTime = DeterministicSimulationUnits.TicksToSeconds(dto.remainingCraftTicks);
        FacilityFuel.Persist(dto);
        // Placement is the store-owned DTO, also referenced by VirtualObjectWorld.
        // Only production fields change here; placement indices need no rebuild or clone.
    }
}
