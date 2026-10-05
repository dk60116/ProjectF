using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Runtime;
using ProjectF.Simulation;

// The real entity, state slots, production clock, item output boundary and binary save serializer run here.
// Unity objects, power supply and pipe graph transfer are boundary doubles; this is not a rendered game test.
public class GaugeGameObject { }
public class StoreViewBoundary { }
public class MiningMachineInstance { public void Persist() { } }
public class ItemManager { public bool Available = true; public readonly List<ItemDefinition> ItemDefinitions = new(); public readonly HashSet<int> BlockedTargets = new(); public int Probes; public bool IsManualRequirementSatisfied(int id) { Probes++; return Available && !BlockedTargets.Contains(id); } }
public class GameManager { public static GameManager Instance = new(); public ItemManager ItemManger = new(); }
public partial class ItemDefinition
{
    public enum EnergyType { None, Burn, Electricity }
    public struct EnergyUseRequirement { public EnergyType energyType; public float useEnergyAmount; }
    public readonly List<EnergyUseRequirement> Requirements = new();
    public EnergyType energyType;
    public float energyAmount;
    public int UseEnergyRequirementCount => Requirements.Count;
    public bool TryGetUseEnergyRequirement(int index, out EnergyUseRequirement requirement)
    { requirement = index >= 0 && index < Requirements.Count ? Requirements[index] : default; return index >= 0 && index < Requirements.Count; }
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition item, EnergyType type)
    { float rate = 0; foreach (var value in item.Requirements) if (value.energyType == type) rate += value.useEnergyAmount; return rate; }
}
public partial class ProductionMachine : InputOutputModule
{
    public int MaximumProductionIngredientTypes => 4;
    public bool CanSelectProductionTarget(int item) => true;
    public bool TryCollectAllProductionTargetItemIds(ICollection<int> items) { items.Add(2); return true; }
}
public static partial class CraftingTreeRuntime
{
    public readonly struct IngredientEntry
    {
        public readonly int itemId;
        public readonly float amount;
        public int count => Mathf.CeilToInt(amount);
        public IngredientEntry(int id, float amount) { itemId = id; this.amount = amount; }
    }
}
internal partial class ProductionRenderTemplate
{
    internal ItemDefinition Definition = new() { id = 34 };
    internal Vector3 PowerLinePoint, Scale, ConsumePoint;
    internal Bounds LocalBounds;
    internal float Watts = 10, PrimaryRate = 10, InputConsumeMoveInterval = .1f, WorkGaugeVerticalOffset = .25f;
    internal bool HasPipePorts;
    internal bool IsOilDrill;
    internal float OilLitersPerSecond;
    internal Recipe[] Recipes;
}
public partial class TerrainGenerator { public bool IsFloorObjectCoordinateVirtualized(Vector2Int c) => false; }
public partial class BoxObject { public int MinimumRetainedItemCount; }
public partial class Block
{
    public int Consumed;
    public int ConsumeInputAreaCenterObjectsAnimated(int item, int count, Vector3 point, float interval)
    { int consumed = Math.Min(count, GetInputAreaCenterItemCount(item)); Count -= consumed; Consumed += consumed; return consumed; }
}
public partial class BlockStateStore
{
    public bool TryGetInstallationState(Vector2Int c, out InstallationSaveState state) => Installed.TryGetValue(c, out state);
    public int GetSavedCenterExtractableItemCount(Vector2Int c, int item) => Saved.GetValueOrDefault(c)?.GetInputAreaCenterItemCount(item) ?? 0;
    public int GetSavedCenterItemCount(Vector2Int c, int item) => GetSavedCenterExtractableItemCount(c, item);
    public int RemoveSavedCenterItems(Vector2Int c, int item, int count)
    { var block = Saved.GetValueOrDefault(c); return block?.ConsumeInputAreaCenterObjectsAnimated(item, count, default, 0) ?? 0; }
}
public partial class ProductionWorld
{
    private OilDrillingBatch oilBatch;
    internal OilDrillingBatch OilBatch => oilBatch ??= new OilDrillingBatch(this);
    private readonly Dictionary<Vector2Int, List<ProductionFacilityInstance>> observers = new();
    private readonly ResourceStateSlots<State> slots = new();
    internal readonly List<int> InputAreaScratch = new(8);
    internal TerrainGenerator Terrain = new();
    internal BlockStateStore Store = new();
    internal long ProcessedUpdates;
    internal int OilDrillCount;
    public float SourcePressure = 1;
    public long SourceUnits, OutputAcceptedUnits, OutputCapacity = long.MaxValue;
    public int InputCalls, OutputCalls, DirtyCalls;
    internal ref State GetState(int index, uint generation) => ref slots.Get(index, generation);
    internal bool Contains(int index, uint generation) => slots.Contains(index, generation);
    internal ProductionFacilityInstance Add(InputOutputModule prototype, BlockStateStore.InstallationSaveState state, ProductionRenderTemplate template)
    {
        var slot = slots.Allocate(new State { Production = ProductionProcess.Empty, SampleTick = MapObjectTickManager.CurrentSimulationTick });
        var facility = new ProductionFacilityInstance(this, slot.Index, slot.Generation, new MapObjectHandle(100), prototype, state, template);
        Current = this;
#if FURNACE_INTEGRATION
        instances.Add(facility);
#endif
        Observe(state.occupiedCoordinates, facility);
        Observe(facility.OutputCoordinates, facility);
        Observe(state.inputOutputState.gridCoordinates, facility);
        return facility;
    }
#if FURNACE_INTEGRATION
    private readonly Dictionary<InputOutputModule, ProductionRenderTemplate> templates = new();
    private readonly List<ProductionFacilityInstance> instances = new();
    private long nextManualCheckTick;
    internal void RegisterTemplateForAvailability(ProductionRenderTemplate template) { templates.Add(new InputOutputModule(), template); }
#endif
    internal void Remove(ProductionFacilityInstance value)
    {
        if (value.Template.IsOilDrill) OilBatch.Unregister(value);
#if PRODUCTION_FLUID_BRIDGE
        RemoveFluidOutputRoutes(value);
#endif
        slots.Release(value.Index, value.Generation);
    }
    internal void MarkDisplayDirty(ProductionFacilityInstance value) => DirtyCalls++;
#if !PRODUCTION_FLUID_BRIDGE
    internal bool HasFluidOutputTargets(ProductionFacilityInstance facility) => OutputCapacity > 0;
    internal void WakeNativeProducers(ProductionFacilityInstance value) { }
    internal void TransferInputFluids(ProductionFacilityInstance facility, ProductionRenderTemplate.Recipe recipe)
    {
        InputCalls++;
        foreach (var input in recipe.Inputs)
        {
            if (InputOutputModule.ResolveItemDefinition(input.itemId)?.isFluid != true) continue;
            var io = facility.Placement.inputOutputState;
            long amount = Math.Min(SourceUnits, Math.Min(DeterministicSimulationUnits.RateForTicks(SourcePressure, 1),
                Math.Max(0, facility.RequiredFluidUnits(recipe, input.amount) - facility.FluidUnits(input.itemId))));
            int index = io.productionInputFluidItemIds.IndexOf(input.itemId);
            if (index < 0) { index = io.productionInputFluidItemIds.Count; io.productionInputFluidItemIds.Add(input.itemId); io.productionInputFluidUnits.Add(0); }
            io.productionInputFluidUnits[index] += amount; SourceUnits -= amount;
        }
    }
    internal float GetOutputAvailableLiters(ProductionFacilityInstance facility, int item) => DeterministicSimulationUnits.ToFloat(Math.Max(0, OutputCapacity - OutputAcceptedUnits));
    internal void GetOilOutputState(ProductionFacilityInstance facility, int item, float rate, out float available, out float retention)
    { available = GetOutputAvailableLiters(facility, item); retention = 1; }
    internal void TransferOutputFluid(ProductionFacilityInstance facility, ProductionRenderTemplate.Recipe recipe, float requestedLiters = -1)
    {
        OutputCalls++;
        var io = facility.Placement.inputOutputState;
        long amount = Math.Min(io.productionOutputFluidUnits, Math.Min(Math.Max(0, OutputCapacity - OutputAcceptedUnits),
            DeterministicSimulationUnits.RateForTicks(recipe.OutputRate, 1)));
        io.productionOutputFluidUnits -= amount; OutputAcceptedUnits += amount;
    }
#endif
}
#if !FURNACE_INTEGRATION
public static partial class FacilitySimulationWorld
{
    public static void Register(IMapObjectUpdateTick target, bool schedule = false) { if (schedule) SetScheduled(target, true); }
}
public static class MapObjectTickProfiler
{
    public static Scope SampleNamed(string group, string type, string label) => default;
    public static void AddRuntimeCounter(string group, string label, object value) { }
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}
#endif
public class ResourceDefinition { public enum PlacementCategory { Ground, Oil } }
public partial class ResourceInstance
{
    public ResourceDefinition.PlacementCategory PlacementCategory = ResourceDefinition.PlacementCategory.Oil;
    public int GetCount => Batch;
}
public partial class OilDrillingMachine
{
    public bool TryGetObjectInfoOutputRate(out int item, out float rate) { item = 2; rate = .75f; return true; }
}
namespace ProjectF.Benchmark
{
    public static partial class BenchmarkRuntime
    {
        public static double SpilledFluidLiters;
        public static void RecordSpill(float liters) => SpilledFluidLiters += Math.Max(0, liters);
    }
}

public partial class MiningWorld { public void Wake(Vector2Int coordinate) { } }
public partial class InputOutputModule
{
    private static readonly List<InputOutputModule> runtimeWakeScratch = new();
    private static readonly HashSet<InputOutputModule> runtimeWakeSet = new();
    private static void CollectRuntimeModulesAtCoordinate(Vector2Int coordinate, bool outputOnly) { }
    private static void WakeCollectedRuntimeModules() { }
    public enum RectGridBlockType { None, InputItem, Output, PipeInputItem, PipeOutputItem, DoublePipeOutputItem, PipeInput, DoubleInputItem }
    public bool TryGetRectGridBlockTypeAtCoordinate(object source, Vector2Int anchor, int turns, Vector2Int coordinate, out RectGridBlockType type)
    {
#if PRODUCTION_FLUID_BRIDGE
        return PortTypes.TryGetValue(coordinate, out type);
#else
        type = RectGridBlockType.None; return false;
#endif
    }
    public static bool IsInputOutputAreaBlockType(RectGridBlockType type) => type != RectGridBlockType.None;
}
