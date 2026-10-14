using System;
using System.Collections.Generic;
using ProjectF.Simulation.Monsters;
using UnityEngine;

// Scene/animation ports only. Tests compile the real world, combat bridge and A*.
namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(GameObject value)
        {
            value.SetActive(false);
            for (int i = 0; i < value.transform.Children.Count; i++) Destroy(value.transform.Children[i].gameObject);
        }
        public static void DestroyImmediate(GameObject value) => Destroy(value);
        public static GameObject Instantiate(GameObject prototype, Vector3 position, Quaternion rotation, Transform parent)
            => FixtureMonsterPrefab.Clone(prototype, position, rotation);
    }
    public sealed partial class GameObject : Object
    {
        public Transform transform = new Transform();
        public bool activeInHierarchy = true;
        public int AttackAnimations;
        public ProjectF.Monsters.MonsterAnimationState AnimationState;
        public string name;
        public GameObject(string name = null, params Type[] types)
        {
            this.name = name;
            foreach (var type in types)
                if (type == typeof(RectTransform)) transform = new RectTransform();
            transform.gameObject = this;
            foreach (var type in types)
                if (type != typeof(RectTransform))
                {
                    var component = (FixtureComponent)Activator.CreateInstance(type);
                    component.gameObject = this;
                    components.Add(type, component);
                }
        }
        public bool activeSelf => activeInHierarchy;
        public void SetActive(bool active) => activeInHierarchy = active;
        public T GetComponentInParent<T>() where T : class => UiComponent as T;
        public object UiComponent;
    }
    public partial class Transform : Object
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 lossyScale = new Vector3(1, 1, 1);
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        { this.position = position; this.rotation = rotation; }
    }
    public struct Quaternion
    {
        // Monster headings are planar; retain yaw so production turning is testable.
        public float YawDegrees;
        public static Quaternion Euler(float x, float y, float z) => new() { YawDegrees = y };
        public static Quaternion LookRotation(Vector3 direction) =>
            Euler(0f, MathF.Atan2(direction.x, direction.z) * 180f / MathF.PI, 0f);
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            float angle = rotation.YawDegrees * MathF.PI / 180f;
            return new Vector3(point.x * MathF.Cos(angle) + point.z * MathF.Sin(angle), point.y,
                point.z * MathF.Cos(angle) - point.x * MathF.Sin(angle));
        }
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float angle)
        {
            float delta = (b.YawDegrees - a.YawDegrees + 540f) % 360f - 180f;
            return Euler(0f, a.YawDegrees + Math.Clamp(delta, -angle, angle), 0f);
        }
    }
    public static class Application { public static bool isPlaying = true; }
    public sealed class SerializeField : Attribute { }
    public sealed class MinAttribute : Attribute { public MinAttribute(float minimum) { } }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int zero => default;
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static Vector2Int operator *(Vector2Int a, int b) => new(a.x * b, a.y * b);
    }
    public readonly record struct Vector2(float x, float y)
    {
        public static Vector2 zero => default;
        public Vector2 normalized { get { float d = MathF.Sqrt(x * x + y * y); return d > 0 ? new(x / d, y / d) : default; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
    }
    public readonly struct RectInt
    {
        public readonly int xMin, yMin, xMax, yMax;
        public RectInt(int x, int y, int width, int height)
        { xMin = x; yMin = y; xMax = x + width; yMax = y + height; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0f ? this * (1f / magnitude) : zero;
        public static Vector3 zero => default;
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float amount) => new(a.x * amount, a.y * amount, a.z * amount);
        public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt((a - b).sqrMagnitude);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float amount) => a + (b - a) * amount;
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float amount)
        {
            float distance = Distance(a, b);
            return distance <= amount || distance == 0f ? b : Lerp(a, b, amount / distance);
        }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Abs(int value) => Math.Abs(value);
        public static float Abs(float value) => Math.Abs(value);
        public static int RoundToInt(float value) => (int)MathF.Round(value);
        public static int FloorToInt(float value) => (int)MathF.Floor(value);
        public static int CeilToInt(float value) => (int)MathF.Ceiling(value);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
    }
}

namespace ProjectF.Simulation
{
    public static class SimulationTickWorld
    {
        public const int DefaultSimulationTicksPerSecond = 60;
        public const float FixedSimulationDeltaSeconds = 1f / 60f;
    }
}
public static class MapObjectTickManager
{
    public static bool SimulationPaused, WaitingForWorldLoad;
    public static IMapObjectUpdateTick Registered;
    public static void RegisterUpdateTick(IMapObjectUpdateTick world) => Registered = world;
    public static void UnregisterUpdateTick(IMapObjectUpdateTick world)
    { if (ReferenceEquals(world, Registered)) Registered = null; }
}
public static class SaveManager { public static bool GameplayInputBlocked; }
public sealed partial class Player
{
    public readonly GameObject Root = new GameObject { layer = 3 };
    public Transform transform => Root.transform;
    public T GetComponent<T>() where T : class => Root.GetComponent<T>();
    public Player()
    {
        var capsule = Root.AddComponent<CapsuleCollider>();
        capsule.enabled = true;
        capsule.direction = 1;
        capsule.radius = .2f;
        capsule.height = 1f;
        capsule.center = new Vector3(0, .5f, 0);
    }
    public CombatHealth Health = new CombatHealth(100f);
    public bool isActiveAndEnabled = true;
    public bool IsDead => Health.IsDead;
    public int Hits;
    public float TakeDamage(float damage) { Hits++; return Health.Damage(damage); }
}
public sealed class GameManager
{
    public static GameManager Instance = new GameManager();
    public static bool TextInputFocused;
    public Player Player;
    public bool CloakingMode { get; private set; }
    public void SetCloakingMode(bool enabled) => CloakingMode = enabled;
    public ItemManager ItemManger = new();
}
public sealed class ItemManager
{
    public readonly Dictionary<int, ItemDefinition> Definitions = new();
    public bool TryGetItemDefinitionById(int id, out ItemDefinition definition) => Definitions.TryGetValue(id, out definition);
}
public interface IMapObjectTarget
{
    bool IsTargetActive { get; }
    bool AllowsAnimalTraversal { get; }
    Vector3 WorldPosition { get; }
    ItemDefinition BoundItemDefinition { get; }
    int ResolvedItemId => BoundItemDefinition?.id ?? -1;
}
public sealed class ItemDefinition
{
    public int id = 11;
    public float maxHealth = 100f;
    public float MaximumHealth => maxHealth;
    public bool excludeFromMonsterTargets;
    public int monsterAttackPriority = 1;
    public int MonsterAttackPriority => Math.Clamp(monsterAttackPriority, 0, 3);
    public MapObject mapObject = new InstallationObject();
}
public class MapObject : IMapObjectTarget
{
    public readonly Transform transform = new();
    public bool Traversable;
    public bool IsTargetActive => true;
    public bool AllowsAnimalTraversal => Traversable;
    public Vector3 WorldPosition => default;
    public ItemDefinition BoundItemDefinition => null;
}
public class InstallationObject : MapObject { }
public class Vehicle : InstallationObject { }
public class ConveyorBelt : InstallationObject { }
public class Resource : MapObject { }
public class ResourceInstance : MapObject
{
    public int ResourceCount = 1;
    public float CurrentHealth = 100f, MaximumHealth = 100f;
}
public sealed class Block
{
    public IMapObjectTarget MapObject;
    public bool IsRuntimeActive = true;
    public Vector3 WorldPosition;
    public int Contents = 2;
    public void SetMapObject(IMapObjectTarget value) => MapObject = value;
    public void ApplyFloorObjectState(object value) => Contents = 0;
    public void DiscardInstallationContents(bool conveyor) => Contents = 0;
    public void ForgetDestroyedInfrastructure(IMapObjectTarget target) { }
}
public class FixtureBuilding : IMapObjectTarget, IMapObjectSimulationIdentity
{
    public long SimulationId { get; set; }
    public bool Active = true, Traversable;
    public bool IsTargetActive => Active;
    public bool AllowsAnimalTraversal => Traversable;
    public Vector3 WorldPosition { get; set; }
    // Combat fixtures opt into proactive targeting; production ItemDefinition still defaults to one.
    public ItemDefinition BoundItemDefinition { get; } = new ItemDefinition { monsterAttackPriority = 2 };
}
public sealed partial class BlockStateStore
{
    public sealed class InstallationSaveState
    {
        public int itemId = 11;
        public long placementSequence;
        public bool hasCombatHealth;
        public float combatHealth;
        public string itemName;
        public Vector2Int anchorCoordinate;
        public int quarterTurns, conveyorVariantKind;
        public bool hasWorldPose;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public List<Vector2> railVisualPathPoints = new();
        public List<Vector2Int> occupiedCoordinates = new List<Vector2Int>();
    }
    public readonly Dictionary<Vector2Int, InstallationSaveState> States = new();
    private Dictionary<Vector2Int, InstallationSaveState> savedInstallationStates => States;
    private readonly Dictionary<Vector2Int, FloorObjectSaveState> savedFloorObjectStates = new();
    private sealed class FloorObjectSaveState { public List<PortableItemHealthSaveState> InstallationHealth; }
    public readonly Dictionary<Vector2Int, Vector2Int> Coordinates = new();
    public readonly Dictionary<Vector2Int, FixtureBuilding> Buildings = new();
    public int Removals;
    public Action NavigationChanged;
    public readonly HashSet<Vector2Int> Depleted = new();
    public bool IsDepleted(Vector2Int coordinate) => Depleted.Contains(coordinate);
    public bool TryGetInstallationAnchorAtCoordinate(Vector2Int coordinate, out Vector2Int key) => Coordinates.TryGetValue(coordinate, out key);
    public bool TryGetInstallationStorageKey(long sequence, int itemId, out Vector2Int key)
    {
        foreach (var pair in States)
            if (pair.Value.placementSequence == sequence && pair.Value.itemId == itemId)
            { key = pair.Key; return true; }
        key = default;
        return false;
    }
    public bool TryGetInstallationStateReadOnly(Vector2Int key, out InstallationSaveState state) => States.TryGetValue(key, out state);
    public bool TryGetLiveInstallationReadOnly(Vector2Int key, out InstallationObject live, out InstallationSaveState state)
    { live = null; state = null; return false; }
    public void RemoveInstallation(Vector2Int key)
    {
        States.Remove(key);
        DamagedInstallations.Remove(key);
        Buildings[key].Active = false;
        Removals++;
        NavigationChanged?.Invoke();
    }
    public void SetFloorObjects(Vector2Int coordinate, object value) { }
    public void SetConveyorItems(Vector2Int coordinate, object value) { }
    public void SaveFloorObjects(Vector2Int coordinate, Block block, VirtualObjectResidency residency) { }
}
public sealed partial class ConveyorRuntimeRecord : FixtureBuilding { public ConveyorBelt Prototype = new(); }
public sealed partial class PipeRuntimeRecord : FixtureBuilding { public InstallationObject Prototype = new(); }
public sealed partial class ConveyorWorld
{
    public static ConveyorWorld Current;
    public readonly Dictionary<Vector2Int, ConveyorRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out ConveyorRuntimeRecord record) => Records.TryGetValue(coordinate, out record);
}
public sealed class PipeWorld
{
    public static PipeWorld Current;
    public readonly Dictionary<Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord record) => Records.TryGetValue(coordinate, out record);
}
public partial class TerrainGenerator
{
    public static TerrainGenerator Active;
    public static TerrainGenerator ResolveActive() => Active;
    public readonly Transform transform = new Transform();
    private readonly BlockStateStore resourceStateStore = new();
    public BlockStateStore Store => resourceStateStore;
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public readonly HashSet<Vector2Int> Walls = new();
    public readonly HashSet<Vector2Int> Water = new();
    public readonly Dictionary<Vector2Int, Resource> GeneratedResources = new();
    private readonly Block wallBlock = new Block { MapObject = new MapObject() };
    public TerrainGenerator() { Active = this; resourceStateStore.NavigationChanged = InvalidateAnimalNavigation; }
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
    {
        if (Walls.Contains(coordinate)) { block = wallBlock; return true; }
        return Blocks.TryGetValue(coordinate, out block);
    }
    public static Vector2Int GetWorldBlockCoordinate(Vector3 point) => new(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z));
    public bool IsCoordinateInsideMapBounds(Vector2Int cell) => Math.Abs(cell.x) <= 64 && Math.Abs(cell.y) <= 64;
    public TerrainBiome GetTileBiome(Vector2Int cell) => Water.Contains(cell) ? TerrainBiome.Water : TerrainBiome.Land;
    private MapObject ResolveInstallationSourcePrefab(BlockStateStore.InstallationSaveState state) => new InstallationObject();
    private bool TryGetResourcePrefab(Vector2Int coordinate, out Resource resource) => GeneratedResources.TryGetValue(coordinate, out resource);
    private bool CanSpawnResourceAtGeneratedCoordinate(Vector2Int coordinate, Resource resource) => true;
    private bool TryGetAnimalDrinkDirection(Vector3 point, out Vector3 direction) { direction = default; return false; }
    public void ReleaseInstallationObject(InstallationObject value, MapObject prefab) { }
    public FixtureBuilding AddBuilding(Vector2Int key, long sequence, params Vector2Int[] cells)
    {
        var target = new FixtureBuilding { WorldPosition = new Vector3(key.x, 0, key.y), SimulationId = sequence };
        var state = new BlockStateStore.InstallationSaveState { placementSequence = sequence, anchorCoordinate = key };
        GameManager.Instance.ItemManger.Definitions[target.BoundItemDefinition.id] = target.BoundItemDefinition;
        foreach (Vector2Int cell in cells)
        {
            state.occupiedCoordinates.Add(cell);
            Blocks[cell] = new Block { MapObject = target };
            Store.Coordinates[cell] = key;
        }
        Store.States[key] = state;
        Store.Buildings[key] = target;
        InvalidateAnimalNavigation();
        return target;
    }
}
public enum TerrainBiome { Land, Water }

namespace ProjectF.Monsters
{
    public enum MonsterAnimationState { Idle, Move, Attack, Hit, Death }
    public sealed class MonsterVariant { public int id; public string variantName; public GameObject prefab; public Sprite icon; }
    public sealed class MonsterDefinition
    {
        public object Animations;
        public string MonsterName = "Test";
        public MonsterStats Stats = MonsterStats.Default;
        public MonsterVariant Variant = new MonsterVariant { id = 1, variantName = "Blue", prefab = FixtureMonsterPrefab.Create() };
        public bool TryGetVariant(int id, out MonsterVariant variant)
        { variant = id == Variant.id ? Variant : null; return variant != null; }
        public bool TryGetRuntimeData(int id, out MonsterArchetypeData data)
        { data = new MonsterArchetypeData(1, id, Stats); return id == Variant.id; }
    }
    internal sealed class MonsterAnimationView : IDisposable
    {
        private readonly GameObject root;
        internal MonsterAnimationView(GameObject root, object animation) { this.root = root; }
        internal void SetState(MonsterAnimationState state, bool restart = false, float duration = 0f)
        {
            root.AnimationState = state;
            if (state == MonsterAnimationState.Attack && restart) root.AttackAnimations++;
        }
        internal void Tick(float dt) { }
        public void Dispose() { }
    }
}
namespace ProjectF.Animals
{
    public static class AnimalAIProfiler
    {
        public enum Counter { WalkableCacheHits, WalkableCacheMisses, GridCollisionQueries }
        public static void Add(Counter value) { }
        public static SearchScope Sample(string name) => default;
        public readonly struct SearchScope : IDisposable
        {
            public SearchScope(bool expanded, bool line = false) { }
            public void Dispose() { }
        }
    }
}
public static partial class AnimalGridPathfinder { public static long SchedulingWorkCount { get; private set; } }
public sealed class RobotArmInstance : FixtureBuilding { }
namespace ProjectF.Power { public sealed class UtilityPoleRuntime : FixtureBuilding { } }
namespace ProjectF.MapObjects
{
    public sealed class Tree : Resource { }
    public sealed class TreeInstance : MapObject { }
    public interface IWorkableTarget : IMapObjectTarget { long WorkablePlacementSequence { get; } }
}
public sealed class PortableItemHealthSaveState { public int index; public float health; }
public enum VirtualObjectResidency { Live }
namespace ProjectF.Railway
{
    public sealed class RailwayInstance : FixtureBuilding { }
    public sealed class TrainWorld
    {
        public static TrainWorld Shared = new();
        internal bool TryGetTargetAtCoordinate(Vector2Int coordinate, out Vehicle target)
        { target = null; return false; }
    }
}
