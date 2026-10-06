using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;

// Only engine/IO boundaries are doubled. Production clock, entity and output reservation are the real sources.
namespace ProjectF.Simulation
{
    public static class SimulationTickWorld
    { public const int DefaultSimulationTicksPerSecond = 60; public const float FixedSimulationDeltaSeconds = 1f / 60; }
}
namespace ProjectF.MapObjects
{
    public sealed class ForestryWorld
    {
        public static ForestryWorld Current;
        public void PersistAll() { }
        public void Wake(Vector2Int coordinate) { }
    }
    public readonly record struct MapObjectHandle(int Value)
    { public bool IsValid => Value > 0; }
    public class VirtualRenderBatchCollection
    {
        public static Bounds CalculateWorldBounds(Bounds bounds, Matrix4x4 matrix) => bounds;
        public void ClearActiveMatrices() { }
        public void SuspendRendering() { }
        public void RenderBatches(object camera) { }
    }
}
public static class MapObjectTickManager { public static long CurrentSimulationTick; public static bool WaitingForWorldLoad; }
public interface IMapObjectUpdateTickDeadline { long NextUpdateTick { get; } }
public interface IMapObjectTarget { }
public class MapObject
{
    public enum MultiFocusMode { None }
    public enum MapObjectStatus { None }
    public static bool IsItemAllowedByFilterMask(int item, bool initialized, IReadOnlyList<ulong> words) => item >= 0
        && (!initialized || (item >> 6) >= words.Count || (words[item >> 6] & (1UL << (item & 63))) != 0);
}
public class MiningMachine : InputOutputModule { }
public partial class InputOutputModule
{
    public readonly GaugeGameObject gameObject = new();
    public string ObjectName => "Electric miner";
    public bool AllowsFocus => true;
    public bool AllowsAnimalTraversal => false;
    public MapObject.MultiFocusMode FocusMode => default;
    public MapObject.MapObjectStatus Status => default;
    public float FocusActivationRadius => 4;
    public int RuntimeAreaMaxObjects => 10;
    public float OutputMoveInterval => .1f;
    public ItemDefinition OutputFilter;
    public bool TryGetRectGridBlockPlacementAtCoordinate(object source, Vector2Int anchor, int turns, Vector2Int coordinate,
        out InputOutputModule.RectGridBlockPlacement placement)
    { placement = new InputOutputModule.RectGridBlockPlacement { itemDefinition = OutputFilter }; return OutputFilter != null; }
}
internal class MiningRenderTemplate
{
    internal ItemDefinition Definition = new ItemDefinition { id = 60 };
    internal Vector3 PowerLinePoint, Scale;
    internal Bounds LocalBounds;
    internal float WorkGaugeVerticalOffset = .25f;
    internal Color WorkGaugeFillColor = new Color(0, 1, 0);
    internal float Watts = 30, WorkRate = 30, InputConsumeMoveInterval = .1f;
    internal long WorkUnitsPerTick = DeterministicSimulationUnits.RateForTicks(30, 1);
    internal ItemDefinition.EnergyType EnergyType = ItemDefinition.EnergyType.Electricity;
    internal bool UsesFuel => EnergyType == ItemDefinition.EnergyType.Burn;
    internal Vector3 ConsumePoint;
    internal long CompleteEnergy = DeterministicSimulationUnits.FromInt(60);
    internal int BenchmarkOutputId = -1;
    internal Bounds ColliderBounds;
    internal object ColliderMaterial;
    internal bool ColliderTrigger;
    internal uint ColliderIncludeLayers, ColliderExcludeLayers;
    public void Append(MiningMachineInstance miner, ProjectF.MapObjects.VirtualRenderBatchCollection batches) { }
}
public class ItemDefinition
{
    public int id, capacity;
    public bool isFluid, oneItem;
    public object mapObject;
    public static int ResolveStackCapacity(ItemDefinition item, int capacity) => item?.oneItem == true ? 1 : capacity;
}
public partial class InputOutputModule
{
    public struct RectGridBlockPlacement { public ItemDefinition itemDefinition; }
    public static readonly Dictionary<int, ItemDefinition> Items = new();
    public static ItemDefinition ResolveItemDefinition(int id) => Items.GetValueOrDefault(id);
    public static bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int coordinate, int item) => true;
    public static int SuccessfulEmitsBeforeFailure = int.MaxValue;
    public static bool TryEmitOutputItemToBlock(Block block, int item, Vector3 start, float delay, out object output)
    {
        output = null;
        if (SuccessfulEmitsBeforeFailure == 0 || block == null || !block.CanAddInputAreaCenterObjects(1, item)) return false;
        SuccessfulEmitsBeforeFailure--; block.Count++; block.Item = item; return true;
    }
}
public class Block
{
    public enum BlockType { Ground, Water }
    public BlockType Type;
    public bool IsRuntimeConveyor;
    public object MapObject;
    public int Count, Item = -1, Capacity = 10, BoundaryRequests;
    public bool CanAddConveyorObjects(int count) => Count + count <= Capacity;
    public void EnsureConveyorTransportInteractionBoundary() => BoundaryRequests++;
    public void PlayVirtualInputAreaConsumeAnimation(int item, Vector3 position, float delay) { }
    public bool CanAddInputAreaCenterObjects(int count, int item) => Count + count <= GetInputAreaCenterCapacity(item) && (Count == 0 || Item == item);
    public int GetInputAreaCenterItemId() => Count == 0 ? -1 : Item;
    public int GetInputAreaCenterCapacity(int item) => ProjectF.Benchmark.BenchmarkRuntime.ForceWorking && !(MapObject is BoxObject)
        ? int.MaxValue : ItemDefinition.ResolveStackCapacity(InputOutputModule.ResolveItemDefinition(item), Capacity);
    public int GetInputAreaCenterItemCount(int item = -1) => item < 0 || item == Item ? Count : 0;
}
public class BoxObject { public bool Allowed = true; public bool AcceptsItem(int item) => Allowed; }
public class ConveyorBelt { }
public class TerrainGenerator
{
    public bool IsBenchmarkPlacementInProgress;
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
}
public class Resource { public struct ResourceSaveState { public int resourceCount; } }
public partial class BlockStateStore
{
    // Railway notification is a boundary here; RailWorldHarness tests the real service.
    public sealed class RailwayBoundary { public void UpsertSaved(InstallationSaveState state) { } }
    public RailwayBoundary RailWorld { get; } = new();
    public class InstallationSaveState
    {
        public int itemId = 60, quarterTurns;
        public long placementSequence;
        public Vector2Int anchorCoordinate;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public List<Vector2Int> occupiedCoordinates = new();
        public InputOutputModule.PersistentState inputOutputState = new();
        public bool itemFilterMaskInitialized;
        public List<ulong> itemFilterMaskWords = new();
        public static int CloneCount;
        public InstallationSaveState Clone()
        {
            CloneCount++;
            var clone = (InstallationSaveState)MemberwiseClone();
            clone.occupiedCoordinates = new(occupiedCoordinates);
            clone.itemFilterMaskWords = new(itemFilterMaskWords);
            clone.inputOutputState = inputOutputState?.Clone();
            return clone;
        }
    }
    public readonly Dictionary<Vector2Int, Resource.ResourceSaveState> Resources = new();
    public bool TryGet(Vector2Int coordinate, out Resource.ResourceSaveState state) => Resources.TryGetValue(coordinate, out state);
    public readonly Dictionary<Vector2Int, InstallationSaveState> Installed = new();
    public readonly Dictionary<Vector2Int, Block> Saved = new();
    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state) => state.anchorCoordinate;
    public bool TryGetInstallationAnchorAtCoordinate(Vector2Int c, out Vector2Int anchor) { anchor = c; return Installed.ContainsKey(c); }
    public int GetSavedCenterItemCount(Vector2Int c) => Saved.GetValueOrDefault(c)?.Count ?? 0;
    public int GetSavedCenterTopItemId(Vector2Int c) => Saved.GetValueOrDefault(c)?.GetInputAreaCenterItemId() ?? -1;
    public bool CanAddSavedCenterItems(Vector2Int c, int item, int count, int capacity) => GetSavedCenterItemCount(c) + count <= capacity && (GetSavedCenterItemCount(c) == 0 || GetSavedCenterTopItemId(c) == item);
    public bool TryAddSavedCenterItems(Vector2Int c, int item, int count, int capacity)
    {
        if (!CanAddSavedCenterItems(c, item, count, capacity)) return false;
        if (!Saved.TryGetValue(c, out var block)) Saved[c] = block = new Block();
        block.Item = item; block.Count += count; return true;
    }
}
public partial class MiningWorld
{
    public static MiningWorld Current;
    public MiningMachineInstance SaveMiner;
    public void FlushSaveStates() => SaveMiner?.Persist();
    internal struct State
    { internal MiningProcess Clock; internal ProjectF.MapObjects.MapObjectHandle Resource; internal Vector2Int ResourceCoordinate; internal int ResourceCursor, PendingHarvestedItems; internal bool HasTarget, NeedsEvaluation, FuelBypassed; }
    internal State Value;
    public bool Alive = true;
    public TerrainGenerator Terrain = new();
    public BlockStateStore Store = new();
    public long ProcessedUpdates;
    internal bool Contains(int index, uint generation) => Alive;
    internal ref State GetState(int index, uint generation) => ref Value;
}
public class VirtualObjectWorld : StoreViewBoundary
{
    public static VirtualObjectWorld Current = new();
    public readonly Dictionary<Vector2Int, ProjectF.MapObjects.MapObjectHandle> Resources = new();
    public bool IsHandleAlive(ProjectF.MapObjects.MapObjectHandle handle) => handle.Value == 100 || Resources.ContainsValue(handle);
    public bool TryGetResourceHandle(Vector2Int c, out ProjectF.MapObjects.MapObjectHandle handle) => Resources.TryGetValue(c, out handle);
}
public class ResourceInstance
{
    public static readonly Dictionary<Vector2Int, ResourceInstance> All = new();
    public int Reserves = 10, Item = 1, Batch = 1, Harvests;
    public bool IsRuntimeActive => Reserves > 0;
    public bool CanHarvest => Reserves > 0;
    public Vector3 FocusPoint;
    public int RemainingMachineHarvestOutputCount => Reserves * Batch;
    public bool TryPeekMachineHarvestOutput(out int item, out int count) { item = Item; count = Batch; return CanHarvest; }
    public bool TryHarvestForMachine(out int item, out int count) { item = Item; count = Batch; if (!CanHarvest) return false; Reserves--; Harvests++; return true; }
    public static bool TryGetActiveResourceAtCoordinate(TerrainGenerator terrain, Vector2Int c, out ResourceInstance resource) => All.TryGetValue(c, out resource) && resource.IsRuntimeActive;
}
public static class FacilitySimulationWorld
{
    public static readonly HashSet<IMapObjectUpdateTick> Scheduled = new();
    public static void SetScheduled(IMapObjectUpdateTick target, bool value) { if (value) Scheduled.Add(target); else Scheduled.Remove(target); }
    public static bool IsScheduled(IMapObjectUpdateTick target) => Scheduled.Contains(target);
    public static void RefreshSchedule(IMapObjectUpdateTick target) { }
    public static void Unregister(IMapObjectUpdateTick target) => Scheduled.Remove(target);
}
public static class UtilityPole
{
    public static float Ratio = 1;
    public static int DemandChanges;
    public static void InvalidateDataConsumerDemand(IDataElectricConsumer target) => DemandChanges++;
    public static bool TryGetElectricSupplyRatio(IDataElectricConsumer target, float watts, out float ratio) { ratio = Ratio; return true; }
}
public static class MapClimate { public const float CurrentTemperatureCelsius = 15; }
public static partial class PowerDemandProbe
{
    public class ElectricNetwork { public bool Dirty; }
    private class Binding { public float DemandWatts; public List<ElectricNetwork> Networks = new(); }
    private const float EnergyEpsilon = .0001f;
    private static readonly Dictionary<IDataElectricConsumer, Binding> robotArmBindings = new();
    private static readonly Dictionary<ElectricNetwork, float> robotArmDemand = new();
    private static readonly HashSet<ElectricNetwork> electricRuntimeWakeNetworks = new();
    private static bool electricRuntimeWakeAllPending;
    public static int WakeBatches, Invalidations;
    public static ElectricNetwork Attach(IDataElectricConsumer consumer)
    {
        var network = new ElectricNetwork();
        robotArmBindings[consumer] = new Binding { Networks = new() { network } }; return network;
    }
    public static float Demand(ElectricNetwork network) => robotArmDemand.GetValueOrDefault(network);
    public static bool WakeQueued(ElectricNetwork network) => electricRuntimeWakeNetworks.Contains(network);
    private static void MarkNetworkRuntimeDirty(ElectricNetwork network) => network.Dirty = true;
    private static void InvalidateNetworkRuntimeEvaluationForNextTick() => Invalidations++;
    private static void RequestElectricRuntimeNetworkWake() => WakeBatches++;
}
namespace ProjectF.Benchmark
{
    public static class BenchmarkRuntime
    { public static bool ForceWorking; public static int FallbackItemId = 1; public static void EmitItem(TerrainGenerator terrain, int id, Vector3 point) { } public static void RecordItems(int count) { } public static bool IsPortableItem(ItemDefinition item) => item != null && !item.isFluid; }
#if !BENCHMARK_INPUT_INTEGRATION
    internal static class BenchmarkInputSupply
    {
        internal static int Version => 0;
        internal static void Remove(object owner) { }
        internal static void Refill(Vector2Int coordinate) { }
        internal static void Add(object owner, TerrainGenerator terrain, BlockStateStore store, Vector2Int coordinate, int item, int count, int capacity = 16) { }
        internal static void AddEnergy(object owner, TerrainGenerator terrain, BlockStateStore store, IReadOnlyList<Vector2Int> coordinates, ItemDefinition definition, int capacity = 16) { }
        internal static void SampleEnergy(object owner, Vector3 target, bool working) { }
        internal static int Consume(object owner, TerrainGenerator terrain, Vector2Int coordinate, int item, int count, Vector3 target, float interval) => count;
    }
#endif
}

public partial class ProductionWorld { public static ProductionWorld Current; public void FlushSaveStates() { } }
public sealed class UtilityPoleWorld { public static UtilityPoleWorld Current; public void FlushSaveStates() { } }
