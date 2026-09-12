// External environment only: SeedPlanter and deterministic arithmetic use production source.
using System;
using System.Collections.Generic;
using UnityEngine;

public class InputOutputModule
{
    public class PersistentState
    {
        public bool hasDeterministicUnits = true, seedPlanterHasLoadedSeed;
        public float seedPlanterPlantElapsedSeconds;
        public long seedPlanterPlantElapsedUnits, seedPlanterTransferRemainingUnits;
        public int seedPlanterLoadedSeedItemId = -1;
        public Vector2Int seedPlanterLoadedSeedInputCoordinate;
    }
    public int InputSeeds, ConsumedSeeds, EnergyCalls;
    public float SupplyRatio = 1f, StepSeconds = 0.1f;
    public bool isActiveAndEnabled = true;
    protected float OperationalAnimationSpeedRatio => SupplyRatio;
    protected const float InputConsumeMoveInterval = 0.1f;
    protected bool HasRuntimeOutputCoordinates => true;
    protected List<Vector2Int> RuntimeOutputCoordinates = new List<Vector2Int> { default };
    public virtual float ManagedUpdateTickIntervalSeconds => 0.1f;
    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
    public virtual void ApplyManagedUpdateTick() { }
    protected bool TryBeginPlannedModuleApply(out float deltaTime) { deltaTime = StepSeconds; return true; }
    protected void ApplyPlannedBaseModuleTick(float deltaTime) { }
    protected bool TryGetPlacementRuntime(out int a, out int b) { a = b = 0; return true; }
    protected ItemDefinition ResolveInstalledDefinition() => new ItemDefinition { seedPlanterPlantDurationSeconds = 5f };
    protected ItemDefinition ResolveItemDefinition(int id) => id == 1 ? GameManager.Instance.ItemManger.ItemDefinitions[0] : null;
    protected bool HasOperationalEnergyAvailable(ItemDefinition definition) => SupplyRatio > 0f;
    protected bool TryConsumeOperatingEnergy(float seconds, out float consumed)
    { EnergyCalls++; consumed = seconds * SupplyRatio; return consumed > 0f; }
    protected bool TryGetElectricPowerRequirement(out float watts) { watts = 45000; return true; }
    public virtual bool TryGetElectricPowerDemand(out float watts) { watts = 0; return false; }
    protected Vector3 ResolveConsumeTargetWorldPosition() => default;
    protected int ConsumeRuntimeInputAreaCenterObjects(Vector2Int coordinate, int id, int count,
        Vector3 position, float interval, bool animateVirtualizedConsumption, bool respectBoxMinimumRetainedCount)
    { int consumed = Math.Min(InputSeeds, count); InputSeeds -= consumed; ConsumedSeeds += consumed; return consumed; }
    protected bool TryRestoreRuntimeInputAreaCenterObject(Vector2Int coordinate, int id, Vector3 position)
    { InputSeeds++; return true; }
    protected bool TryResolveRuntimeInputItemBlock(int id, int count, object ignored, out Block block,
        out Vector2Int coordinate, bool respectBoxMinimumRetainedCount)
    { block = null; coordinate = default; return InputSeeds >= count; }
    protected int GetRuntimeInputAreaCenterItemCount(Vector2Int coordinate, int id, bool respectBoxMinimumRetainedCount) => InputSeeds;
    protected void AppendRuntimeInputItemAreaCoordinates(int id, List<Vector2Int> coordinates) { coordinates.Add(default); }
    protected bool CanAddItemToRuntimeIoOverlapCoordinate(Vector2Int coordinate, int id) => true;
    protected bool ContainsRuntimeInputItemArea(Vector2Int coordinate, int id) => true;
    protected void WakeRuntimeUpdate() { }
    protected void SetWorkAnimatorState(bool active, bool force) { }
    protected T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
    public virtual PersistentState CapturePersistentState() => new PersistentState();
    public virtual void ApplyPersistentState(PersistentState state) { }
    protected virtual bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> ids) => false;
    protected virtual bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> ids) => false;
    protected virtual bool ShouldKeepRuntimeUpdateTickActive() => true;
    protected virtual bool ShouldPlayWorkAnimation() => false;
    protected virtual float ResolveWorkAnimationSpeedMultiplier() => 1f;
    protected virtual string ResolveObjectInfoStatus(out bool producing) { producing = false; return ""; }
    protected virtual void OnPlacementRuntimeCleared() { }
}
public class ItemDefinition
{
    public int id = 1;
    public float seedPlanterPlantDurationSeconds;
    public static bool IsPlantableSeedDefinition(ItemDefinition definition) => definition != null && definition.id == 1;
}
public class GameManager
{
    public static GameManager Instance = new GameManager();
    public ItemManager ItemManger = new ItemManager();
}
public class ItemManager { public List<ItemDefinition> ItemDefinitions = new List<ItemDefinition> { new ItemDefinition() }; }
public static class MapObjectTickManager
{
    public const int DefaultSimulationTicksPerSecond = 60;
    public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
}
public static class PortableObject { public const float MoveToDuration = 0.3f; }
public class TerrainGenerator
{
    public static TerrainGenerator Active;
    public bool Farmland = true, Occupied, FailPlant;
    public int PlantCalls, DropAnimations;
    public static TerrainGenerator ResolveActive() => Active;
    public bool IsFarmlandAt(Vector2Int coordinate) => Farmland;
    public bool CanPlantSeedAt(Vector2Int coordinate, ItemDefinition seed) => !Occupied;
    public bool TryPlantSeedAt(Vector2Int coordinate, ItemDefinition seed)
    { PlantCalls++; if (FailPlant) return false; Occupied = true; return true; }
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) { block = new Block(); return true; }
}
public class Block
{
    public void PlayTransientItemToFloorAnimation(int id, Vector3 position) { TerrainGenerator.Active.DropAnimations++; }
}
namespace UnityEngine
{
    public class SerializeField : Attribute { }
    public class HideInInspector : Attribute { }
    public class MinAttribute : Attribute { public MinAttribute(float min) { } }
    public class Sprite { }
    public struct Vector3 { }
    public record struct Vector2Int(int x, int y);
    public struct Color
    {
        public Color(float r, float g, float b, float a) { }
        public static Color operator *(Color c, float value) => c;
    }
    public class Transform
    {
        public string name;
        public T GetComponent<T>() where T : class => null;
    }
    public class Renderer
    {
        public void GetPropertyBlock(MaterialPropertyBlock block) { }
        public void SetPropertyBlock(MaterialPropertyBlock block) { }
    }
    public class MaterialPropertyBlock { public void SetColor(int id, Color color) { } }
    public static class Shader { public static int PropertyToID(string name) => 0; }
    public static class Application { public static bool isPlaying = true; }
    public static class Debug { public static void LogError(string message, object context) { throw new Exception(message); } }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
}
