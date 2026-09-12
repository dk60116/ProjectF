using System;

public readonly record struct Vector2(float x, float y);
public struct Quaternion
{
    public float x, y, z, w;
    public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; radians = 0; }
    public static Quaternion identity => new(0, 0, 0, 1);
    private float radians;
    public static Quaternion Euler(float x, float y, float z) => new() { radians = y * MathF.PI / 180 };
    public static Quaternion LookRotation(Vector3 direction, Vector3 up) => new() { radians = MathF.Atan2(direction.x, direction.z) };
    private static float Difference(Quaternion from, Quaternion to) => MathF.IEEERemainder(to.radians - from.radians, 2 * MathF.PI);
    public static float Angle(Quaternion from, Quaternion to) => MathF.Abs(Difference(from, to)) * 180 / MathF.PI;
    public static Quaternion RotateTowards(Quaternion from, Quaternion to, float degrees)
    {
        float limit = degrees * MathF.PI / 180;
        return new() { radians = from.radians + Math.Clamp(Difference(from, to), -limit, limit) };
    }
    public static Vector3 operator *(Quaternion q, Vector3 v) => new(
        MathF.Cos(q.radians) * v.x + MathF.Sin(q.radians) * v.z, v.y,
        -MathF.Sin(q.radians) * v.x + MathF.Cos(q.radians) * v.z);
}
public class AnimalAIWorld
{
    public static AnimalAIWorld Instance;
    public Vector3 Separation, Center;
    public bool TryGetHerdCenter(int id, out Vector3 center) { center = Center; return true; }
}
public partial class AnimalAIController
{
    private Vector3 simulationPosition { get => transform.position; set => transform.position = value; }
    private int simulationYaw;
    public Quaternion SimulatedRotation => simulationRotation;
    private Quaternion simulationRotation => Quaternion.Euler(0, ProjectF.Animals.AnimalSimulationMath.Degrees(simulationYaw), 0);
    private enum MovementAvailability { Clear, BlockedByAnimal, BlockedByStaticObstacle }
    private const float AvoidanceExitClearDuration = .3f, BlockedRepathDelay = .3f, RepathCooldown = .5f;
    private const float AbandonBlockedTargetDelay = 1.5f, NavigationProgressEpsilon = .04f, HerdReturnRetryDelay = 5;
    private static readonly float[] AvoidanceAngles = { 30, 60, 90, 120, 150, 180 };
    private int navigationWaypointCount, navigationWaypointIndex;
    private Vector3[] navigationWaypoints;
    private Vector3 navigationGoal, navigationProgressTarget, terrainEscapeTarget, committedAvoidanceDirection;
    private long navigationRevision;
    private bool navigationPrepared, navigationProgressTracked, herdReturnTargetActive, terrainEscapeActive, hasFleeThreat;
    private float navigationBlockedTime, navigationRepathCooldown, navigationBestDistance, navigationNoProgressTime;
    private float avoidanceDirectClearTime, avoidanceTurnSign = 1, herdReturnRetryCooldown;
    private bool preferReachableFallbackTarget;
    private int herdReturnSuppressionCount;
    private int HerdId => 1;
    public int stuckTargetAbandonCount;
    public float Speed = .625f;
    public Func<Vector3, bool> PhysicalWalkable = _ => true;
    public int MovementCount;
    private float GetObstacleRadius() => .45f;
    private float GetEffectiveMoveSpeed() => Speed;
    private float RandomDuration(Vector2 range) => range.x;
    private void StopForDeath() => ResetToIdleBehavior();
    private bool TryGetTerrainEscapeTarget(Vector3 position, bool loaded, out Vector3 target) { target = position; return false; }
    private Vector3 UpdateSmoothedSeparation(AnimalAIWorld world, float dt) => world?.Separation ?? Vector3.zero;
    private bool MovesCloserToTerrainEscape(Vector3 origin, Vector3 candidate) => false;
    private bool CanUsePredictiveTerrainProbe(Vector3 position) => CanOccupyTerrain(position, true);
    private MovementAvailability ProbeOccupancyPosition(Vector3 origin, Vector3 position, float radius) => PhysicalWalkable(position)
        ? MovementAvailability.Clear : MovementAvailability.BlockedByStaticObstacle;
    private bool IsGridPathClearOrEscaping(Vector3 origin, Vector3 direction, float distance, float radius)
    {
        int samples = Math.Max(1, (int)Math.Ceiling(distance / .05f));
        for (int i = 1; i <= samples; i++)
            if (!PhysicalWalkable(origin + direction * (distance * i / samples))) return false;
        return true;
    }
    private void ApplyMovement(Vector3 position, Vector3 direction, float dt) { transform.position = position; MovementCount++; }
    public bool MoveDirect(float dt) => MoveTowardTargetLive(dt, true);
    public bool ResolveDirect(Vector3 forward, bool escaping, out Vector3 direction) =>
        ResolveMovementDirection(transform.position, forward, Speed, .1f, true, escaping, out direction) == MovementAvailability.Clear;
    public float RemainingActivity => stateTimeRemaining;
    public bool BeginWanderForCheck()
    {
        currentState = AnimalAIState.Wander;
        stateTimeRemaining = 10;
        ResetNavigation();
        return hasTarget = TryChooseTarget(false, true, out targetPosition);
    }
    public bool RetryHerdReturnForCheck()
    {
        herdReturnRetryCooldown = 0;
        return TryBeginHerdAreaReturn(true);
    }
    public void ExpireActivityForCheck() => stateTimeRemaining = 0;
}
public static partial class Checks
{
    private static void RunMovementChecks()
    {
        RunFoodFacingChecks();
        RunPenRoamingChecks();
        TerrainGenerator.Active = new();
        var food = Drop(1);
        var animal = new AnimalAIController { Move = Movement.Real, PhysicalWalkable = p => p.x < 1.1f };
        float previousX = animal.transform.position.x;
        bool forwardOnly = true;
        for (int i = 0; i < 100 && food.Count > 0; i++)
        {
            animal.Tick(.05f);
            forwardOnly &= animal.transform.position.x >= previousX;
            previousX = animal.transform.position.x;
        }
        Check(food.Count == 0 && forwardOnly, "real movement reaches food before a wall without retreating from lookahead");
        Check(animal.transform.position.x < 1 && animal.currentState == AnimalAIState.Eat, "animal stops within feeding reach instead of standing on the item");
        int moves = animal.MovementCount;
        for (int i = 0; i < 10; i++) animal.Tick(.05f);
        Check(animal.MovementCount == moves && animal.currentState == AnimalAIState.Eat, "eating holds position for a stable animation interval");

        TerrainGenerator.Active = new(); food = Drop(2);
        AnimalAIWorld.Instance = new() { Separation = new(-100, 0, 0), Center = new(-20, 0, 0) };
        animal = new() { Move = Movement.Real };
        for (int i = 0; i < 100 && food.Count > 0; i++) animal.Tick(.05f);
        Check(food.Count == 0, "strong opposing herd separation cannot reverse food approach");
        AnimalAIWorld.Instance = null;

        animal = new() { hasTarget = true, Target = new(1, 0, 0), Speed = 10, currentState = AnimalAIState.Eat };
        animal.MoveDirect(.5f);
        Check(animal.transform.position.x <= 1 && animal.transform.position.x > 0, "large simulation step never overshoots the waypoint");
        animal.MoveDirect(.5f);
        Check(!animal.hasTarget && animal.MovementCount == 1, "arrival does not oscillate across the target");

        TerrainGenerator.Active = new();
        animal = new() { hasTarget = true, Target = new(2, 0, 0), currentState = AnimalAIState.Eat,
            PhysicalWalkable = p => p.x <= 0 };
        for (int i = 0; i < 25 && animal.hasTarget; i++) animal.MoveDirect(.1f);
        Check(animal.MovementCount == 0 && !animal.hasTarget && animal.RemainingActivity >= 2,
            "fully blocked approach stays still then abandons into idle instead of reversing repeatedly");
        animal = new() { hasTarget = true, Target = new(2, 0, 0), currentState = AnimalAIState.Flee,
            PhysicalWalkable = p => p.x < 0 || p.sqrMagnitude == 0 };
        Check(animal.ResolveDirect(new(1, 0, 0), false, out var fleeDirection) && fleeDirection.x < 0,
            "fleeing retains reverse escape when forward directions are blocked");

        TerrainGenerator.Active = new(); food = Drop(1); animal = new() { Move = Movement.Real, PhysicalWalkable = p => p.x <= 0 };
        for (int i = 0; i < 35; i++) animal.Tick(.05f);
        Check(food.Count == 1 && animal.foodSearchCooldown > 1 && animal.MovementCount == 0,
            "failed physical approach waits before searching again and cannot consume remotely");
    }

    private static void RunPenRoamingChecks()
    {
        static void Pen(int centerX, int radius)
        {
            TerrainGenerator.Active = new();
            for (int i = -radius; i <= radius; i++)
            {
                TerrainGenerator.Active.Walls.Add(new(centerX - radius, i));
                TerrainGenerator.Active.Walls.Add(new(centerX + radius, i));
                TerrainGenerator.Active.Walls.Add(new(centerX + i, -radius));
                TerrainGenerator.Active.Walls.Add(new(centerX + i, radius));
            }
        }

        Pen(10, 2);
        var animal = new AnimalAIController { RoamingRadius = 2, Move = Movement.Real };
        animal.transform.position = new(10, 0, 0);
        Check(!animal.RetryHerdReturnForCheck(), "closed pen cannot turn a local destination into a herd-return target");
        var destinations = new System.Collections.Generic.HashSet<Vector2Int>();
        bool selectedInside = true, stayedInside = true, completed = true, uninterrupted = true;
        for (int trip = 0; trip < 6; trip++)
        {
            selectedInside &= animal.BeginWanderForCheck() && animal.Target.x > 8 && animal.Target.x < 12
                && MathF.Abs(animal.Target.z) < 2;
            destinations.Add(new((int)animal.Target.x, (int)animal.Target.z));
            Vector3 goal = animal.Target;
            for (int tick = 0; tick < 300 && animal.hasTarget; tick++)
            {
                uninterrupted &= !animal.RetryHerdReturnForCheck() && (animal.Target - goal).sqrMagnitude < .0001f;
                animal.MoveDirect(.1f);
                var position = animal.transform.position;
                stayedInside &= position.x > 8.5f && position.x < 11.5f && MathF.Abs(position.z) < 1.5f;
            }
            completed &= !animal.hasTarget && (animal.transform.position - goal).magnitude <= .21f;
        }
        Check(selectedInside && destinations.Count > 1, "relocated animal chooses varied reachable destinations inside its current pen");
        Check(stayedInside && completed && animal.stuckTargetAbandonCount == 0,
            "animal completes repeated walks inside a closed pen without wall crossings or stuck retries");
        Check(uninterrupted, "herd-return retry cannot interrupt an ongoing reachable local walk");
        TerrainGenerator.Active.Walls.Remove(new(8, 0));
        animal.ExpireActivityForCheck();
        Check(animal.RetryHerdReturnForCheck() && animal.Target.x < 2,
            "opening the pen restores a reachable return toward the original herd area");

        Pen(0, 2);
        animal = new() { RoamingRadius = 30 };
        Check(animal.BeginWanderForCheck() && MathF.Abs(animal.Target.x) < 2 && MathF.Abs(animal.Target.z) < 2,
            "small pen inside a large herd area still selects only its connected interior");
        Pen(10, 1);
        animal = new() { RoamingRadius = 2 };
        animal.transform.position = new(10, 0, 0);
        Check(!animal.BeginWanderForCheck(), "pen without a reachable spare cell never creates a target through a wall");
        Pen(10, 2);
        animal = new() { RoamingRadius = 2, IsSaddledFreeRoaming = true };
        animal.transform.position = new(10, 0, 0);
        Check(!animal.BeginWanderForCheck(), "local roaming fallback does not widen a saddled animal's configured roaming area");
    }

    private static void RunFoodFacingChecks()
    {
        TerrainGenerator.Active = new();
        var food = Drop(0); food.Position = new(0, 2, -.5f); food.Count = 3;
        var animal = new AnimalAIController { Move = Movement.Real };
        var desired = Quaternion.Euler(0, 180, 0);
        animal.Tick(.05f); animal.UpdateAnimationForCheck();
        Check(food.Count == 3 && !animal.animal.EatingAnimation && animal.animal.PendingMeals == 0,
            "food behind the animal is not consumed and eating cannot start before turning");
        Check(Quaternion.Angle(animal.SimulatedRotation, Quaternion.identity) > 0 && Quaternion.Angle(animal.SimulatedRotation, desired) > 3,
            "animal turns gradually toward nearby food instead of snapping around");
        bool prematureConsumption = false;
        for (int i = 0; i < 90 && food.Count == 3; i++)
        {
            animal.Tick(.05f);
            prematureConsumption |= food.Count < 3 && Quaternion.Angle(animal.SimulatedRotation, desired) > .01f;
        }
        animal.UpdateAnimationForCheck();
        Check(!prematureConsumption && food.Count == 2 && animal.animal.EatingAnimation && animal.animal.PendingMeals == 1,
            "exactly one item is consumed only after the body faces the food");
        Check(animal.MovementCount == 0 && animal.transform.position.sqrMagnitude == 0,
            "turning toward food ignores height and holds the animal's position");
        animal.Tick(.1f);
        Check(Quaternion.Angle(animal.SimulatedRotation, desired) < .01f && food.Count == 2,
            "meal keeps facing the food without taking another stack item");
        TerrainGenerator.Active = new(); food = Drop(0);
        animal = new(); animal.Tick(.05f);
        Check(food.Count == 0, "food directly under the animal does not stall on a zero facing direction");
    }
}
