using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;

public interface IMapObjectTarget { MapObject SceneObject { get; } bool IsTargetActive { get; } }
public static class MapObjectTargetExtensions
{
    public static bool TryGetComponent<T>(this IMapObjectTarget target, out T result) where T : class
    { result = target as T; return result != null; }
}
public class BoxCollider { }
public class SphereCollider { }
public class Pose { public Vector3 position, localScale = Vector3.one; public Quaternion rotation = Quaternion.identity; public Transform Find(string path) => null; }
public class MapObject : IMapObjectTarget
{
    public enum MultiFocusMode { All } public enum MapObjectStatus { Normal }
    public MapObject SceneObject => this; public bool IsTargetActive => true;
    public ItemDefinition BoundItemDefinition;
    public Pose transform = new();
    public string ObjectName => "Forestry"; public bool AllowsFocus => true; public bool AllowsAnimalTraversal => false;
    public MultiFocusMode FocusMode => MultiFocusMode.All; public MapObjectStatus Status => MapObjectStatus.Normal;
    public float FocusActivationRadius => 4;
    public bool HasCollider = true;
    public int ResolveItemId() => BoundItemDefinition?.id ?? 0;
    public T GetComponent<T>() where T : class => HasCollider ? new SphereCollider() as T : null;
    public T[] GetComponentsInChildren<T>(bool inactive) => Array.Empty<T>();
    public static bool IsItemAllowedByFilterMask(int item, bool initialized, List<ulong> words) => !initialized || item < 0 || item >> 6 >= words.Count || (words[item >> 6] & (1UL << (item & 63))) != 0;
}
public class InstallationObject : MapObject { public virtual void PrepareForPool() { } }
public class InputOutputModule : InstallationObject
{
    public enum RectGridBlockType { None, InputItem, Output, InputEnergy }
    public struct Area { public Vector2Int coordinate; public int itemId; }
    public class PersistentState
    {
        public bool hasDeterministicUnits = true, seedPlanterHasLoadedSeed;
        public long seedPlanterPlantElapsedUnits, seedPlanterTransferRemainingUnits, storedEnergyUnits, energyGaugeCapacityUnits;
        public float seedPlanterPlantElapsedSeconds, storedEnergy, energyGaugeCapacity;
        public int seedPlanterLoadedSeedItemId = -1;
        public Vector2Int seedPlanterLoadedSeedInputCoordinate;
        public List<Vector2Int> inputEnergyCoordinates = new(), outputCoordinates = new(), gridCoordinates = new();
        public List<Area> inputItemAreas = new();
        public List<int> storedEnergyTypes = new(); public List<long> storedEnergyUnitsByType = new(), energyGaugeCapacityUnitsByType = new();
    }
    public int RuntimeAreaMaxObjects = 64;
    public virtual float ManagedUpdateTickIntervalSeconds => .1f;
    public virtual void ApplyManagedUpdateTick() { }
    protected virtual bool ShouldKeepRuntimeUpdateTickActive() => false;
    protected virtual bool ShouldPlayWorkAnimation() => false;
    public virtual PersistentState CapturePersistentState() => new();
    public virtual void ApplyPersistentState(PersistentState state) { }
    protected virtual void OnPlacementRuntimeCleared() { }
    protected virtual bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> ids) => false;
    protected virtual bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> ids) => false;
    protected bool ContainsRuntimeInputItemArea(Vector2Int coordinate, int item) => true;
    public static Dictionary<int, ItemDefinition> Definitions = new();
    public static bool BlockInput;
    public static ItemDefinition ResolveItemDefinition(int item) => Definitions.GetValueOrDefault(item);
    public static bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int coordinate, int item) => !BlockInput;
    public static void WakeRuntimeModulesAtCoordinate(Vector2Int coordinate) { }
    public bool TryGetRectGridBlockTypeAtCoordinate(InputOutputModule source, Vector2Int anchor, int turns, Vector2Int coordinate, out RectGridBlockType type)
    { type = coordinate == anchor + Vector2Int.right ? RectGridBlockType.InputItem : RectGridBlockType.Output; return true; }
    public static Vector2Int RotateRectGridOffset(Vector2Int offset, int turns)
    { for (int i = 0; i < ((turns % 4) + 4) % 4; i++) offset = new(-offset.y, offset.x); return offset; }
}
public class ItemDefinition
{
    public enum EnergyType { None, Electricity, Burn }
    public struct Energy { public EnergyType energyType; public float useEnergyAmount; }
    public int id; public bool isSeed, isFluid; public float seedPlanterPlantDurationSeconds = 5, watts = 45000, completeEnergy = 450000;
    public float CraftingDurationSeconds = 10, energyAmount; public EnergyType energyType;
    public List<Energy> Requirements = new();
    public int UseEnergyRequirementCount => Requirements.Count;
    public bool TryGetUseEnergyRequirement(int index, out Energy energy) { energy = Requirements[index]; return true; }
    public Archetype MapObjectArchetype = new(); public class Archetype { public List<int> RenderParts = new() { 1 }; }
    public static bool IsPlantableSeedDefinition(ItemDefinition item) => item?.isSeed == true;
    public static float ResolveElectricUseWatts(ItemDefinition item) => item?.watts ?? 0;
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition item, EnergyType type = EnergyType.None)
    { float rate = 0; foreach (var e in item.Requirements) if (e.energyType == type) rate += e.useEnergyAmount; return type == EnergyType.Burn ? rate : item.watts; }
    public static int ResolveStackCapacity(ItemDefinition item, int fallback) => fallback;
}
public class GameManager { public static GameManager Instance = new(); public ItemManager ItemManger = new(); }
public class ItemManager { public List<ItemDefinition> ItemDefinitions = new(); }
public static class MapObjectTickManager { public const int DefaultSimulationTicksPerSecond = 60; public const float FixedSimulationDeltaSeconds = 1f / 60; }
public static class PortableObject { public const float MoveToDuration = .3f; }
public interface IMapObjectUpdateTick { void ManagedUpdateTick(float delta); }
public interface IMapObjectUpdateTickInterval { float ManagedUpdateTickIntervalSeconds { get; } }
public interface IMapObjectSimulationIdentity { long SimulationId { get; } }
public static class FacilitySimulationWorld
{
    public static readonly HashSet<IMapObjectUpdateTick> Registered = new(), Scheduled = new();
    public static void Register(IMapObjectUpdateTick target, bool awake) { Registered.Add(target); SetScheduled(target, awake); }
    public static void Unregister(IMapObjectUpdateTick target) { Registered.Remove(target); Scheduled.Remove(target); }
    public static void SetScheduled(IMapObjectUpdateTick target, bool awake) { if (awake) Scheduled.Add(target); else Scheduled.Remove(target); }
}
public readonly record struct MapObjectHandle(int Id) { public bool IsValid => Id > 0; }
public class VirtualObjectWorld
{
    public static VirtualObjectWorld Current = new();
    private readonly Dictionary<Vector2Int, MapObjectHandle> handles = new(), resources = new();
    private readonly HashSet<MapObjectHandle> alive = new(); private int next;
    public static VirtualObjectWorld Ensure() => Current;
    public void Register(Vector2Int coordinate) { var handle = new MapObjectHandle(++next); handles[coordinate] = handle; alive.Add(handle); }
    public void RegisterResource(Vector2Int coordinate) { resources[coordinate] = new MapObjectHandle(++next); }
    public bool TryGetInstallationHandle(Vector2Int coordinate, out MapObjectHandle handle) => handles.TryGetValue(coordinate, out handle);
    public bool TryGetResourceHandle(Vector2Int coordinate, out MapObjectHandle handle) => resources.TryGetValue(coordinate, out handle);
    public bool IsHandleAlive(MapObjectHandle handle) => alive.Contains(handle);
}
public class BlockStateStore
{
    public class InstallationSaveState
    {
        public Vector2Int anchorCoordinate; public int quarterTurns, itemId; public long placementSequence;
        public Vector3 worldPosition; public Quaternion worldRotation = Quaternion.identity;
        public List<Vector2Int> occupiedCoordinates = new();
        public bool itemFilterMaskInitialized, loggingTreeFilterInitialized;
        public List<ulong> itemFilterMaskWords = new(); public List<string> loggingEnabledTreeDefinitionKeys = new();
        public int loggingMinimumGrowth = 10, loggingMaximumGrowth = 10;
        public ProjectF.Simulation.LoggingProcess loggingProcess;
        public InputOutputModule.PersistentState inputOutputState;
    }
    public Dictionary<Vector2Int, Slot> Saved = new();
    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state) => state.anchorCoordinate;
    public int GetSavedCenterExtractableItemCount(Vector2Int c, int item) => GetSavedCenterItemCount(c, item);
    public int GetSavedCenterItemCount(Vector2Int c, int item = -1) => Saved.TryGetValue(c, out var s) && (item < 0 || s.Item == item) ? s.Count : 0;
    public int GetSavedCenterTopItemId(Vector2Int c) => Saved.GetValueOrDefault(c)?.Item ?? -1;
    public int RemoveSavedCenterItems(Vector2Int c, int item, int count) { int n = Math.Min(count, GetSavedCenterItemCount(c, item)); if (n > 0) Saved[c].Count -= n; return n; }
    public bool TryAddSavedCenterItems(Vector2Int c, int item, int count, int capacity)
    { if (!Saved.TryGetValue(c, out var slot)) Saved[c] = slot = new(); return slot.Add(item, count, capacity); }
    public bool TryGetInstallationAnchorAtCoordinate(Vector2Int coordinate, out Vector2Int anchor) { anchor = default; return false; }
    public bool TryGetInstallationState(Vector2Int coordinate, out InstallationSaveState state) { state = null; return false; }
}
public class Slot
{
    public int Item = -1, Count, Capacity = 64;
    public bool Add(int item, int count, int capacity) { if (Count > 0 && Item != item || Count + count > Math.Min(Capacity, capacity)) return false; Item = item; Count += count; return true; }
}
public class Block
{
    public enum BlockType { Ground } public BlockType Type;
    public Vector2Int Coordinate; public IMapObjectTarget MapObject; public ResourceInstance Resource;
    public Slot Items = new(); public bool IsRuntimeConveyor, HasDroppedFloorObjects;
    public bool SupportsFloorObjectDrops => true;
    public void SetMapObject(IMapObjectTarget target) => MapObject = target;
    public int GetInputAreaCenterItemCount(int item = -1) => item < 0 || Items.Item == item ? Items.Count : 0;
    public int GetInputAreaCenterItemId() => Items.Count > 0 ? Items.Item : -1;
    public int GetInputAreaCenterCapacity(int item) => Items.Capacity;
    public int ConsumeInputAreaCenterObjectsAnimated(int item, int count, Vector3 position, float interval)
    { int n = Math.Min(count, GetInputAreaCenterItemCount(item)); Items.Count -= n; return n; }
    public bool TryAddInputAreaCenterObjectAnimated(int item, Vector3 position, float delay, out object portable) { portable = null; return Items.Add(item, 1, Items.Capacity); }
    public bool CanAddFloorObjects(int count, int item) => Resource == null && MapObject == null && Items.Count == 0 && count <= Items.Capacity;
    public bool TryAddFloorObjectAnimated(int item, Vector3 position, float delay, out object portable) { portable = null; return Items.Add(item, 1, Items.Capacity); }
    public bool TryAddHarvestedFloorObjectAnimated(int item, Vector3 position, out object portable, ResourceInstance tree)
    { portable = null; Items.Item = item; Items.Count++; return true; }
    public void PlayTransientItemToFloorAnimation(int item, Vector3 position) => TerrainGenerator.Active.Animations++;
    public void PlayVirtualInputAreaConsumeAnimation(int item, Vector3 position, float delay) => TerrainGenerator.Active.InputAnimations++;
}
public class BoxObject : MapObject
{
    public int MinimumRetainedItemCount; public Slot Items = new();
    public bool CanPutContainedObjects(int item, int count) => Items.Count + count <= Items.Capacity;
    public bool TryPutOneContainedObject(int item, Vector3 position, float delay, out object portable) { portable = null; return Items.Add(item, 1, Items.Capacity); }
}
public class ResourceDefinition { public const int MinGrowth = 0, MaxGrowth = 10; public string name = "Pine", resourceName; }
public class Resource { public enum HarvestMode { Logging, Mining } }
public class ResourceInstance
{
    public bool IsRuntimeActive = true, CanHarvest = true, HarvestSucceeds = true;
    public Resource.HarvestMode ResolvedHarvestMode = Resource.HarvestMode.Logging;
    public ResourceDefinition Definition = new(); public Block OwningBlock;
    public Vector3 FocusPoint => new(OwningBlock.Coordinate.x, 0, OwningBlock.Coordinate.y);
    public int Harvests;
    public bool TryHarvestForMachine(out int item, out int count)
    { item = 1; count = 4; if (!CanHarvest || !HarvestSucceeds) return false; Harvests++; CanHarvest = false; IsRuntimeActive = false; OwningBlock.Resource = null; return true; }
}
public class TerrainGenerator
{
    public static TerrainGenerator Active;
    public Pose transform = new(); public BlockStateStore Store = new();
    public Dictionary<Vector2Int, Block> Blocks = new();
    public bool Farmland = true, Occupied, FailPlant; public int Plants, Animations, InputAnimations;
    public T GetComponent<T>() where T : class => Store as T;
    public InstallationPlacementController ResolveInstallationPlacementController() => null;
    public static TerrainGenerator ResolveActive() => Active;
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
    public bool IsFloorObjectCoordinateVirtualized(Vector2Int coordinate) => false;
    public bool IsFarmlandAt(Vector2Int coordinate) => Farmland;
    public bool CanPlantSeedAt(Vector2Int coordinate, ItemDefinition definition) => !Occupied && (!Blocks.TryGetValue(coordinate, out var block) || block.Items.Count == 0 && block.Resource == null);
    public bool TryPlantSeedAt(Vector2Int coordinate, ItemDefinition definition) { if (FailPlant || !CanPlantSeedAt(coordinate, definition)) return false; Plants++; Occupied = true; return true; }
    public void CollectPublishedBeltObservers<T>(Dictionary<Vector2Int, List<T>> observers, List<Block> blocks) { blocks.Clear(); }
}
public class InstallationPlacementController { }
public static class UtilityPole
{
    public static float SupplyRatio = 1;
    public static int EnergyCalls, DemandChanges;
    public static bool HasElectricityAvailable(IDataElectricConsumer owner) => SupplyRatio > 0;
    public static bool TryGetElectricSupplyRatio(IDataElectricConsumer owner, float watts, out float ratio) { ratio = SupplyRatio; return true; }
    public static bool TryConsumeElectricity(IDataElectricConsumer owner, float requested, float delta, out float consumed) { EnergyCalls++; consumed = requested * SupplyRatio; return consumed > 0; }
    public static void InvalidateDataConsumerDemand(IDataElectricConsumer owner) => DemandChanges++;
    public static void InvalidateRobotArmConsumers() { } public static void UnregisterRobotArmConsumer(IDataElectricConsumer owner) { }
}
public static class MapObjectTickProfiler { public static void AddRuntimeCounter(string a, string b, long c) { } }
namespace ProjectF.Benchmark
{
    public interface IBenchmarkWorkProgressTarget { bool TryRandomizeWorkProgress(System.Random random); }
    public static class BenchmarkRuntime { public static bool ForceWorking; public const int FallbackItemId = 1; public static bool EmitItem(TerrainGenerator terrain, int item, Vector3 position) => true; }
}
namespace ProjectF.MapObjects
{
    public class TreeInstance : ResourceInstance
    {
        public float Growth = 10; public List<KeyValuePair<int, int>> Seeds = new();
        public bool TryGetMachineAppleDrop(out int item, out int count) { item = -1; count = 0; return false; }
        public void CollectMachineSeedDrops(List<KeyValuePair<int, int>> drops) { drops.Clear(); drops.AddRange(Seeds); }
    }
    internal class ForestryRenderTemplate
    {
        internal ItemDefinition Definition; internal Vector3 Scale = Vector3.one, PowerLinePoint, ConsumePoint;
        internal Bounds LocalBounds = new(Vector3.zero, Vector3.one); internal float Watts, InputConsumeMoveInterval;
        internal long CompleteEnergyUnits; internal int[] SeedItemIds;
        internal ForestryRenderTemplate(InstallationObject source, InstallationPlacementController controller)
        { Definition = source.BoundItemDefinition; Watts = Definition.watts; CompleteEnergyUnits = DeterministicSimulationUnits.FromFloat(Definition.completeEnergy);
            var ids = new List<int>(); if (source is SeedPlanter planter) planter.TryCollectPlantableSeedItemIds(ids); SeedItemIds = ids.ToArray(); }
        internal void Dispose() { }
    }
    public class ForestryWorldView
    {
        internal static ForestryWorldView Create(ForestryWorld world, object parent) => new();
        public int VisibleCount => 0; internal void Unbind(ForestryInstance instance) { } internal void Release() { }
    }
    public sealed partial class ForestryWorld
    {
        private readonly HashSet<ForestryInstance> visibleMarkers = new(), markerCandidates = new();
        private readonly List<ForestryInstance> markerScratch = new(); private ForestryInstance selectedMarkerInstance; private bool markersDirty;
    }
}
namespace ProjectF.Rendering
{
    public class CameraRenderCulling
    {
        public bool TryGetVisibleCellRange(int size, int padding, out Vector2Int min, out Vector2Int max) { min = max = default; return false; }
    }
}
public static class VirtualRenderBatchCollection { public static Bounds CalculateWorldBounds(Bounds bounds, Matrix4x4 matrix) => new(matrix.MultiplyPoint3x4(bounds.center), bounds.size); }
public static class HarnessEngine
{
    public static int Errors;
    public static void LogError(string message) => Errors++;
    public static bool IntersectRay(Bounds bounds, Ray ray, out float distance) { distance = 0; return false; }
}
