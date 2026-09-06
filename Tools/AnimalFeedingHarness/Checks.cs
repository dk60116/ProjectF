using System;
using System.Collections.Generic;
namespace UnityEngine { }

public static class Mathf
{
    public const float PI = MathF.PI;
    public static float Sqrt(float value) => MathF.Sqrt(value);
    public static float Cos(float value) => MathF.Cos(value);
    public static float Sin(float value) => MathF.Sin(value);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static int CeilToInt(float value) => (int)Math.Ceiling(value);
    public static int FloorToInt(float value) => (int)Math.Floor(value);
    public static int RoundToInt(float value) => (int)Math.Round(value);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static int Abs(int value) => Math.Abs(value);
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
public struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public float sqrMagnitude => x * x + y * y + z * z;
    public float magnitude => MathF.Sqrt(sqrMagnitude);
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator *(Vector3 a, float scale) => new(a.x * scale, a.y * scale, a.z * scale);
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    public static Vector3 zero => new();
    public static Vector3 up => new(0, 1, 0);
    public static Vector3 operator -(Vector3 value) => value * -1;
    public Vector3 normalized => magnitude > 0 ? this * (1 / magnitude) : zero;
    public void Normalize() { this = normalized; }
    public static Vector3 ClampMagnitude(Vector3 value, float length) => value.magnitude > length ? value.normalized * length : value;
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
}
public readonly record struct Vector2Int(int x, int y)
{
    public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
}
public class Transform { public Vector3 position; public Quaternion rotation = Quaternion.identity; }
public class AnimalNeedsSettings
{
    public const float DefaultFoodSearchRadius = 8, DefaultFoodDigestionSeconds = 10, DefaultHungerDrainPerSecond = 1;
    public float FoodSearchRadius = 8, FoodDigestionSeconds = 10, HungerDrainPerSecond = 1;
    public const float DefaultGrowthEnergyPerLevel = 100;
    public float GrowthEnergyPerLevel = 100;
}
public class AnimalDefinition { public const int MaxSpawnAge = 10; public AnimalNeedsSettings NeedsSettings = new(); }
public class ItemDefinition
{
    public bool Food;
    public float energyAmount = 25;
    public static bool IsFoodEnergyItemDefinition(ItemDefinition item) => item != null && item.Food;
}
public class ItemManager
{
    public Dictionary<int, ItemDefinition> Items = new();
    public bool TryGetItemDefinitionById(int id, out ItemDefinition definition) => Items.TryGetValue(id, out definition);
}
public class GameManager { public static GameManager Instance = new(); public ItemManager ItemManger = new(); }
public partial class Animal
{
    public void WakeFromRest() { }
    public bool IsAlive = true;
    public float currentHunger = 40;
    public float MaxHunger => 100;
    public bool IsHungry => currentHunger <= 50;
    private readonly List<float> pendingDefecations = new();
    public int PendingMeals => pendingDefecations.Count;
    public bool Interacted;
    private AnimalDefinition animalDefinition;
    private void EnsureNeedsInitialized() { }
    private void MarkTerrainInteraction() => Interacted = true;
}
public class PortableObject
{
    public int ItemId = 1;
    public Transform transform = new();
    public DroppedItemPickupGate Gate = new();
    public bool Released;
    public T GetComponent<T>() where T : class => Gate as T;
}
public class DroppedItemPickupGate
{
    public bool Settled = true;
    public bool CanManualPickup(float distance, float radius) => Settled && distance <= radius;
}
public static class BoxObject
{
    public static bool IsRuntimeContentBlock(Block block) => block.ContainerContent;
}
public partial class Block
{
    public enum BlockType { Ground, Water }
    public BlockType Type = BlockType.Ground;
    private readonly List<List<PortableObject>> floorStacks = new() { new() { new() } };
    private readonly List<PortableObject> inputAreaCenterStack = new();
    private bool inputAreaCenterObjectsVisible = true;
    public bool ContainerContent;
    private Vector3 WorldPosition;
    private int floorItemId = 1;
    private bool floorSettled = true;
    public int Notifications, Releases;
    public Vector3 Position
    {
        get => WorldPosition;
        set { WorldPosition = value; foreach (var item in floorStacks[0]) item.transform.position = value; }
    }
    public int ItemId
    {
        get => floorItemId;
        set { floorItemId = value; foreach (var item in floorStacks[0]) item.ItemId = value; }
    }
    public bool Settled
    {
        get => floorSettled;
        set { floorSettled = value; foreach (var item in floorStacks[0]) item.Gate.Settled = value; }
    }
    public int Count
    {
        get => floorStacks[0].Count;
        set
        {
            var stack = floorStacks[0];
            while (stack.Count > value) stack.RemoveAt(stack.Count - 1);
            while (stack.Count < value) stack.Add(new() { ItemId = ItemId,
                transform = new() { position = Position }, Gate = new() { Settled = Settled } });
        }
    }
    public List<PortableObject> CenterStack => inputAreaCenterStack;
    public bool CenterVisible { set => inputAreaCenterObjectsVisible = value; }
    private void EnsureFloorObjectsInitialized() { }
    private void ReleaseFloorObject(PortableObject item) { item.Released = true; Releases++; }
    private void NotifyRuntimeItemStackChanged() => Notifications++;
}
public partial class TerrainGenerator
{
    public static TerrainGenerator Active = new();
    private static readonly Predicate<int> AnimalFoodItemFilter = IsAnimalFoodItemId;
    private readonly Dictionary<Vector2Int, AnimalAIController> animalFoodConsumers = new();
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public int Lookups;
    public readonly HashSet<Vector2Int> Walls = new();
    public bool CanAnimalMoveTo(Vector3 position, bool loaded) => !Walls.Contains(GetWorldBlockCoordinate(position));
    public bool IsAnimalDrinkLocation(Vector3 position) => false;
    private static Vector2Int GetWorldBlockCoordinate(Vector3 position) => new((int)Math.Round(position.x), (int)Math.Round(position.z));
    private bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) { Lookups++; return Blocks.TryGetValue(coordinate, out block); }
}
public enum AnimalAIState { Idle, Rest, Eat, Drink, Flee, Wander, Graze, LookAround }
public enum Movement { Arrive, Moving, Failed, FalseArrival, Real }
public partial class AnimalAIController
{
    public Animal animal = new();
    public Transform transform = new();
    public bool IsInteracted => animal.Interacted;
    public AnimalAIState currentState;
    public bool waitingForStandUp, hasTarget, movingToActivity;
    private AnimalDefinition definition;
    private float stateTimeRemaining;
    public float foodSearchCooldown;
    private TerrainGenerator feedingTerrain;
    public bool executionActive = true;
    internal bool IsConsumingDroppedFood => executionActive && animal != null && animal.IsAlive
        && currentState == AnimalAIState.Eat && !hasTarget && stateTimeRemaining > 0;
    private Vector2Int foodTargetCoordinate;
    private Vector3 targetPosition;
    public Vector3 Target { get => targetPosition; set => targetPosition = value; }
    private Settings settings = new();
    private const int MaxNavigationWaypoints = 96;
    private const float MaxExtendedNavigationRadius = 64, ExtendedNavigationMargin = 8;
    private const float FeedingDuration = 1, BlockedFoodRetryDelay = 2;
    private const float IntervalBetweenMeals = .5f;
    private const float MaximumRotationDeltaTime = 1f / 30f;
    private const float LocalRoamingSearchRadius = 6;
    private const int TargetSearchAttempts = 24, TargetPathSearchAttempts = 4;
    private uint randomState = 12345;
    private int reachableFallbackTargetCount;
    private Func<Vector3, bool, bool> foodReachabilityFilter;
    private Vector3[] foodNavigationScratch;
    public bool IsSaddledFreeRoaming;
    public float RoamingRadius = 1;
    private Vector3 GetRoamingAreaCenter() => new();
    private float GetRoamingAreaRadius() => RoamingRadius;
    private class Settings
    {
        public float TurnSpeed = 220;
        public float ArrivalDistance = .2f, CohesionWeight = .65f, SeparationWeight = 1.5f, ObstacleProbeDistance = 1.5f;
        public Vector2 IdleDuration = new(2, 6);
        public Vector2 WanderDuration = new(4, 10);
    }
    public Movement Move = Movement.Arrive;
    public bool Tick(float dt) { foodSearchCooldown = Math.Max(0, foodSearchCooldown - dt); return TryTickFeeding(dt, true, out _); }
    private bool MoveTowardTarget(float dt, bool loaded)
    {
        if (Move == Movement.Real) return MoveTowardTargetLive(dt, loaded);
        if (Move == Movement.Moving) return true;
        hasTarget = movingToActivity = false;
        if (Move == Movement.Arrive)
        {
            GetNavigationArea(targetPosition, out Vector3 center, out float radius);
            var path = new Vector3[96];
            bool reachable = CanNavigateDirectly(targetPosition, center, radius, loaded)
                || AnimalGridPathfinder.FindPath(TerrainGenerator.Active, transform.position, targetPosition, center, radius, loaded, path) > 0;
            if (reachable) transform.position = targetPosition;
            else currentState = AnimalAIState.Idle;
        }
        if (Move == Movement.Failed) currentState = AnimalAIState.Idle;
        return false;
    }
}
public static partial class Checks
{
    private static int passed;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); passed++; }
    private static Block Drop(int x, int item = 1, int z = 0)
    {
        var block = new Block { Position = new(x, 0, z), ItemId = item };
        TerrainGenerator.Active.Blocks[new(x, z)] = block; return block;
    }
    public static void Main()
    {
        GameManager.Instance.ItemManger.Items[1] = new() { Food = true };
        GameManager.Instance.ItemManger.Items[2] = new() { Food = false };
        var animal = new AnimalAIController();
        Check(!animal.Tick(.125f) && animal.foodSearchCooldown == 1, "missing food starts a bounded retry delay");
        int lookups = TerrainGenerator.Active.Lookups;
        var laterFood = Drop(1);
        for (int i = 0; i < 7; i++) animal.Tick(.125f);
        Check(TerrainGenerator.Active.Lookups == lookups && laterFood.Count == 1, "retry countdown is not restarted and does not scan every tick");
        animal.Tick(.125f);
        Check(laterFood.Count == 0 && animal.animal.currentHunger == 65 && animal.animal.Interacted, "previously untouched hungry animal finds later food and recovers hunger");
        Check(animal.animal.PendingMeals == 1, "eating queues one digestion timer");
        var spare = Drop(2); animal.Tick(.125f);
        Check(spare.Count == 1, "satiated animal leaves food untouched");

        TerrainGenerator.Active = new();
        var nonfood = Drop(0, 2); var near = Drop(1); var far = Drop(3);
        animal = new(); animal.Tick(.125f);
        Check(nonfood.Count == 1 && near.Count == 0 && far.Count == 1, "nearest compatible food wins over nonfood and farther food");
        animal = new() { Move = Movement.Moving }; animal.Tick(.125f);
        Check(far.Count == 1 && animal.currentState == AnimalAIState.Eat && animal.hasTarget, "travel does not consume food before arrival");
        animal.Move = Movement.Arrive; animal.Tick(.125f);
        Check(far.Count == 0, "arriving consumes one food item");

        TerrainGenerator.Active = new(); far = Drop(4);
        animal = new() { Move = Movement.Failed }; animal.Tick(.125f);
        Check(far.Count == 1 && animal.animal.currentHunger == 40, "failed route cannot consume distant food");
        animal = new() { Move = Movement.FalseArrival }; animal.Tick(.125f);
        Check(far.Count == 1, "clearing the navigation target away from food is not arrival");
        animal = new(); animal.animal.IsAlive = false; animal.Tick(.125f);
        Check(far.Count == 1, "dead animal cannot eat");
        animal = new() { currentState = AnimalAIState.Rest }; animal.Tick(.125f);
        Check(far.Count == 1, "resting animal does not interrupt its sleep to eat");
        animal = new() { waitingForStandUp = true }; animal.Tick(.125f);
        Check(far.Count == 1, "standing-up gate remains respected");

        TerrainGenerator.Active = new(); var unsettled = Drop(0); unsettled.Settled = false;
        animal = new(); animal.Tick(.125f);
        Check(unsettled.Count == 1, "in-flight drop is not consumed");
        unsettled.Settled = true;
        for (int i = 0; i < 8; i++) animal.Tick(.125f);
        Check(unsettled.Count == 0, "settled item becomes edible on the next retry");
        TerrainGenerator.Active = new(); var oneItem = Drop(0);
        var first = new AnimalAIController(); var second = new AnimalAIController();
        first.Tick(.125f); second.Tick(.125f);
        Check(oneItem.Count == 0 && first.animal.currentHunger == 65 && second.animal.currentHunger == 40, "two animals cannot consume the same item twice");
        TerrainGenerator.Active = new(); var outside = Drop(9);
        animal = new(); animal.Tick(.125f);
        Check(outside.Count == 1, "food outside configured search radius is ignored");

        TerrainGenerator.Active = new();
        for (int i = -3; i <= 3; i++)
        {
            TerrainGenerator.Active.Walls.Add(new(-3, i));
            TerrainGenerator.Active.Walls.Add(new(3, i));
            TerrainGenerator.Active.Walls.Add(new(i, -3));
            TerrainGenerator.Active.Walls.Add(new(i, 3));
        }
        var insidePen = Drop(0, z: 1); var outsidePen = Drop(4);
        animal = new(); animal.transform.position = new(2, 0, 0);
        animal.Tick(.125f);
        Check(insidePen.Count == 0 && outsidePen.Count == 1, "enclosed animal chooses reachable food over closer food behind a wall");
        animal = new(); animal.transform.position = new(2, 0, 0);
        animal.Tick(.125f);
        Check(outsidePen.Count == 1 && animal.foodSearchCooldown == 1, "only unreachable food triggers retry without eating through walls");
        TerrainGenerator.Active.Walls.Remove(new(3, 0));
        animal.Tick(1f);
        Check(outsidePen.Count == 0, "opening the pen makes food reachable on retry");

        TerrainGenerator.Active = new();
        for (int z = -1; z <= 1; z++) TerrainGenerator.Active.Walls.Add(new(1, z));
        var behindPartition = Drop(2);
        animal = new(); animal.Tick(.125f);
        Check(behindPartition.Count == 0, "food beyond roaming radius remains reachable by a path around a partition");
        var pathBuffer = new Vector3[96];
        int pathCount = AnimalGridPathfinder.FindPath(TerrainGenerator.Active, new(), new(2, 0, 0), new(1, 0, 0), 9, true, pathBuffer);
        var previous = new Vector3(); bool validPath = pathCount > 1;
        for (int i = 0; i < pathCount; i++)
        {
            validPath &= AnimalGridPathfinder.HasWalkableLine(TerrainGenerator.Active, previous, pathBuffer[i], new(1, 0, 0), 9, true);
            previous = pathBuffer[i];
        }
        Check(validPath, "detour waypoints do not cross wall cells");

        TerrainGenerator.Active = new();
        TerrainGenerator.Active.Walls.UnionWith(new[] { new Vector2Int(1, 0), new(0, 1), new(-1, 0), new(0, -1) });
        var diagonal = Drop(1, z: 1); animal = new(); animal.Tick(.125f);
        Check(diagonal.Count == 1, "animal cannot cut diagonally between touching wall corners");
        animal = new() { hasTarget = true, Target = new(0, 0, -2) };
        animal.Tick(.125f);
        Check(animal.hasTarget && animal.Target.z == -2, "failed food search preserves the current activity destination");
        RunMovementChecks();
        RunGrowthChecks();
        RunFeedingAnimationChecks();
        RunDigestionChecks();
        RunFoodStackConcurrencyChecks();
        Console.WriteLine($"{passed} animal feeding checks passed.");
    }
}
