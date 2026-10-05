using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Diagnostics;
using ProjectF.Rendering;
using RobotArmState = RobotArm.RobotArmState;

static partial class Checks
{
    static int count;
    static void Require(bool value, string message) { count++; if (!value) throw new Exception(message); }
    static RobotArmInstance Arm(int id, Vector3 position, float radius = .2f) => new()
    { SimulationId = id, ColliderCenter = position, Template = new RobotArmRenderTemplate { HasCollider = true, ColliderRadius = radius } };
    static void Main()
    {
        var world = new RobotArmWorld(); var prototype = new RobotArm();
        var shared = world.Template(prototype, 73);
        for (int i = 0; i < 100000; i++)
            if (!ReferenceEquals(shared, world.Template(prototype, 73))) throw new Exception("same prototype/item lost its shared template");
        Require(RobotArmRenderTemplate.Captures == 1, "100k registrations capture definition/power only once");
        Require(!ReferenceEquals(shared, world.Template(prototype, 106)), "different item IDs do not share power definitions");
        Require(RobotArmRenderTemplate.Captures == 2, "each item variant captures its own definition once");
        var consumer = Arm(1, default); consumer.Template = shared;
        for (int i = 0; i < 1000000; i++) if (!consumer.Power(out float watts) || watts != 730f) throw new Exception("cached watts changed");
        Require(RobotArmRenderTemplate.Captures == 2, "one million operational power queries do not resolve definitions");

        consumer.state = RobotArmState.TurningToDrop; consumer.heldItemId = 7;
        consumer.SetPose(Quaternion.Euler(0, 90, 0)); consumer.pickupTimer = -.2f;
        consumer.dropRetryTimer = .1f; consumer.actionTurnTimer = .3f; consumer.waitingForDropRetry = true;
        consumer.Save(); var persisted = consumer.Placement.robotArmState;
        var snapshot = consumer.CaptureTransferState();
        Require(persisted.heldItemId == 7 && persisted.pickupTimer == 0 && Math.Abs(persisted.turnTimer - .5f) < .001f,
            "saved cargo, clamped timers and remaining turn progress survive in-place persistence");
        consumer.heldItemId = 8; consumer.Save();
        Require(ReferenceEquals(persisted, consumer.Placement.robotArmState) && persisted.heldItemId == 8 && snapshot.heldItemId == 7,
            "transfer DTO is reused while explicit snapshots remain independent");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) consumer.Save();
        Require(GC.GetAllocatedBytesForCurrentThread() - allocated == 0, "100k steady-state save updates allocate no DTOs");

        var timing = new RobotArmTickTiming(); MapObjectTickProfiler.Enabled = true;
        var phases = (RobotArmTickTiming.Phase[])Enum.GetValues(typeof(RobotArmTickTiming.Phase));
        using (timing.BeginTick())
            for (int i = 0; i < 100000; i++)
            {
                timing.BeginEntity(i);
                foreach (var phase in phases)
                    using (timing.Measure(phase)) { }
            }
        Require(MapObjectTickProfiler.Records.Count == phases.Length && MapObjectTickProfiler.Records["Robot Arm Power (sampled)"] == 390
            && MapObjectTickProfiler.Records["Robot Arm Pickup Query (sampled)"] == 390
            && MapObjectTickProfiler.Records["Robot Arm Drop Query (sampled)"] == 390
            && MapObjectTickProfiler.Clock == 390 * 2 * phases.Length,
            "100k entities use 390 sampled scopes per phase, without extrapolating their durations");
        MapObjectTickProfiler.Enabled = false; MapObjectTickProfiler.Records.Clear();
        long timestamps = MapObjectTickProfiler.Clock;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        using (timing.BeginTick()) for (int i = 0; i < 100000; i++)
        {
            timing.BeginEntity(i);
            foreach (var phase in phases) using (timing.Measure(phase)) { }
        }
        Require(MapObjectTickProfiler.Clock == timestamps && MapObjectTickProfiler.Records.Count == 0
            && GC.GetAllocatedBytesForCurrentThread() == allocated, "disabled detailed timing reads no clock and allocates nothing");

        var near = Arm(2, new Vector3(1, 0, 0)); var far = Arm(3, new Vector3(1000, 0, 0));
        world.Add(near); world.Add(far);
        var view = new RobotArmWorldView(world); GameManager.Instance = new GameManager();
        GameManager.Instance.Player = new Player(); view.Refresh();
        Require(view.Active == 1 && view.Created == 1 && view.Owner(near) == near, "only a nearby arm gets a native collision boundary");
        GameManager.Instance.Player.transform.position = far.ColliderCenter; view.Refresh();
        Require(view.Active == 1 && view.Created == 1 && view.Owner(far) == far && view.Owner(near) == null,
            "travel reuses the outgoing collider and replaces its owner without retaining an old selection target");
        far.PlacementPresentationSuppressed = true; view.Refresh();
        Require(view.Active == 0 && view.Pooled == 1, "editing suppresses collision and returns the component to the pool");
        far.PlacementPresentationSuppressed = false; view.Refresh();
        Require(view.Active == 1 && view.Created == 1, "ending edit reuses the pooled collider");
        GameManager.Instance.Player = null; view.Refresh();
        Require(view.Active == 0 && view.Pooled == 1, "no player means no active collision components");
        Require(world.TryRaycast(new Ray(new Vector3(1000, 4, 0), new Vector3(0, -1, 0)), 10, out var target, out float distance)
            && target == far && Math.Abs(distance - 3.8f) < .001f, "distant mouse sphere selection works with no native collider");
        Require(!world.TryRaycast(new Ray(new Vector3(1000, 4, .3f), new Vector3(0, -1, 0)), 10, out _, out _),
            "selection uses the authored sphere instead of a larger rendering bounds box");
        Require(!world.TryRaycast(new Ray(new Vector3(1000, 4, 0), new Vector3(0, -1, 0)), 3, out _, out _), "ray distance limit is enforced");
        far.IsRuntimeActive = false;
        Require(!world.TryRaycast(new Ray(new Vector3(1000, 4, 0), new Vector3(0, -1, 0)), 10, out _, out _), "removed arm cannot be selected");
        far.IsRuntimeActive = true; far.AllowsFocus = false;
        Require(!world.TryRaycast(new Ray(new Vector3(1000, 4, 0), new Vector3(0, -1, 0)), 10, out _, out _), "non-focusable arm is skipped");

        var crossing = Arm(4, new Vector3(31.9f, 0, 0), .5f); world.Add(crossing);
        Require(world.TryRaycast(new Ray(new Vector3(32.1f, 4, 0), new Vector3(0, -1, 0)), 10, out target, out _) && target == crossing,
            "sphere crossing a spatial-cell boundary is selectable from its neighboring cell");
        var negative = Arm(5, new Vector3(-33, 0, -33)); world.Add(negative);
        Require(world.TryRaycast(new Ray(new Vector3(-33, 4, -33), new Vector3(0, -1, 0)), 10, out target, out _) && target == negative,
            "negative-coordinate spatial indexing preserves mouse focus");
        Require(!RobotArmWorld.Sphere(new Ray(default, new Vector3(1, 0, 0)), default, 1, 10, out _), "inside-sphere ray preserves physics entry-hit behavior");
        var cells = new SpatialRayCellTraversal(new Ray(default, new Vector3(1, 0, 1)), 1024, 32);
        int visited = 0; while (cells.MoveNext()) { if (++visited > 1000) throw new Exception("ray traversal stalled"); }
        Require(visited <= 66, "long diagonal ray traverses a line of cells instead of its enclosing square");
        var vertical = new SpatialRayCellTraversal(new Ray(new Vector3(-32, 8, -32), new Vector3(0, -1, 0)), 16, 32);
        Require(vertical.MoveNext() && vertical.Current == new Vector2Int(-1, -1) && !vertical.MoveNext(), "vertical boundary ray visits exactly one cell");
        var backwards = new SpatialRayCellTraversal(new Ray(default, new Vector3(-1, 0, 0)), 65, 32);
        visited = 0; while (backwards.MoveNext()) { if (++visited > 10) throw new Exception("negative ray stalled"); }
        Require(visited == 4 && backwards.Current == new Vector2Int(-3, 0), "negative boundary ray terminates at its last crossed cell");
        CheckMotionAndPower();
        CheckStateTicks();
        Console.WriteLine($"PASS: {count} production robot-arm optimization checks; managed Unity boundaries, no engine launched.");
    }
}

public partial class RobotArmWorld
{
    private const int MarkerChunkSize = 32;
    private float maxColliderReach = 1f;
    private readonly Dictionary<Vector2Int, List<RobotArmInstance>> markerArmsByCell = new();
    private readonly Dictionary<(RobotArm Prototype, int ItemId), RobotArmRenderTemplate> templates = new();
    internal RobotArmRenderTemplate Template(RobotArm prototype, int id) => GetTemplate(prototype, id);
    internal bool IsPlanning, FullPowerTick;
    public void Add(RobotArmInstance arm)
    {
        var cell = GetMarkerCell(arm.WorldPosition);
        if (!markerArmsByCell.TryGetValue(cell, out var list)) markerArmsByCell.Add(cell, list = new());
        list.Add(arm);
    }
    public static bool Sphere(Ray ray, Vector3 center, float radius, float max, out float distance) => TryRaycastSphere(ray, center, radius, max, out distance);
}
public partial class RobotArmInstance
{
    internal RobotArmRenderTemplate Template = new();
    public readonly PlacementState Placement = new();
    public long SimulationId;
    public bool IsRuntimeActive = true, AllowsFocus = true, PlacementPresentationSuppressed;
    public Vector3 ColliderCenter;
    public Vector3 WorldPosition => ColliderCenter;
    public ref int heldItemId => ref tickData.heldItemId;
    public ref RobotArmState state => ref tickData.state;
    public ref float pickupTimer => ref tickData.pickupTimer;
    public ref float dropRetryTimer => ref tickData.dropRetryTimer;
    public ref float actionTurnTimer => ref tickData.actionTurnTimer;
    public ref bool waitingForDropRetry => ref tickData.waitingForDropRetry;
    private RobotArmRuntimeState tickData = new() { heldItemId = -1 };
    public int StateLookups;
    private ref RobotArmRuntimeState Data { get { StateLookups++; return ref tickData; } }
    private bool electricPowerBlocked, runtimeSleeping;
    private float lastElectricPowerSupplyRatio;
    private const float ItemMoveDuration = .1f;
    public readonly RobotArmWorld World = new();
    public Quaternion WorldRotation = Quaternion.identity;
    public float TurnSpeed = 180;
    public int AnimationKind { get => tickData.AnimationKind; set => tickData.AnimationKind = value; }
    public float AnimationTime { get => tickData.AnimationTime; set => tickData.AnimationTime = value; }
    public float ItemMoveElapsed { get => tickData.ItemMoveElapsed; set => tickData.ItemMoveElapsed = value; }
    public void SetPose(Quaternion pose) => SetBodyLocalRotation(pose);
    public bool Turn(Quaternion target, float dt) => RotateBodyToward(target, dt);
    public float PoweredTime(float dt) => ResolvePoweredDeltaTime(dt);
    public void Animate(float dt) => AdvanceAnimation(dt);
    public float SupplyRatio => lastElectricPowerSupplyRatio;
    public bool Sleeping => runtimeSleeping;
    public bool PowerBlocked => electricPowerBlocked;
    public bool CheckPlanReset()
    {
        tickData.plannedTransferCommand = PlannedTransferCommand.Drop;
        tickData.stagedTickPlanned = false;
        tickData.plannedPickupAvailabilityChecked = tickData.plannedDropAvailabilityChecked = tickData.runtimeWakePending = true;
        BeginPlannedTick();
        return tickData.plannedTransferCommand == PlannedTransferCommand.None && tickData.stagedTickPlanned
            && !tickData.plannedPickupAvailabilityChecked && !tickData.plannedDropAvailabilityChecked && !tickData.runtimeWakePending;
    }
    private void SetRuntimeSleeping(bool sleeping) { runtimeSleeping = sleeping; }
    private Quaternion inputBodyLocalRotation => Quaternion.identity;
    private float bodyTurnSpeedDegreesPerSecond => TurnSpeed;
    private float pickupInterval => .1f;
    private void EnsureRuntimeStateInitialized() { }
    public void Save() => PersistTransferState();
    public bool Power(out float watts) => TryGetElectricOperationalPowerRequirement(out watts);
}
public class PlacementState { public RobotArm.TransferState robotArmState; }
internal partial class RobotArmRenderTemplate
{
    internal static int Captures;
    internal bool HasCollider = true, ColliderIsTrigger;
    internal int ColliderLayer;
    internal float ColliderRadius = .2f, ElectricUseWatts;
    internal PhysicsMaterial ColliderMaterial;
    internal Quaternion OutputBodyRotation = Quaternion.Euler(0, 180, 0);
    internal RobotArmRenderTemplate() { InitializeRig(); }
    internal RobotArmRenderTemplate(RobotArm prototype, int itemId) : this() { Captures++; ElectricUseWatts = itemId * 10; }
}
public partial class RobotArmWorldView
{
    private readonly RobotArmWorld world;
    private readonly Dictionary<Collider, RobotArmInstance> colliderOwners = new();
    private readonly Dictionary<RobotArmInstance, SphereCollider> colliders = new();
    private readonly Stack<SphereCollider> colliderPool = new();
    private readonly List<RobotArmInstance> collisionCandidates = new(), staleColliders = new();
    private readonly HashSet<RobotArmInstance> nearbyArms = new();
    private readonly GameObject gameObject = new();
    private readonly Transform transform = new();
    public RobotArmWorldView(RobotArmWorld world) { this.world = world; }
    public void Refresh() => RefreshColliders();
    public int Active => colliders.Count;
    public int Pooled => colliderPool.Count;
    public int Created => gameObject.Creates;
    public RobotArmInstance Owner(RobotArmInstance arm) => colliders.TryGetValue(arm, out var collider) ? colliderOwners[collider] : null;
}
public class GameManager { public static GameManager Instance; public Player Player; }
public class Player { public readonly Transform transform = new(); }
public static class MapObjectTickProfiler
{
    public static bool Enabled;
    public static bool IsDetailedEnabled => Enabled;
    public static long Clock;
    public static readonly Dictionary<string, long> Records = new();
    public static long BeginSample() => ++Clock;
    public static void RecordNamedElapsedTicks(string kind, string type, string name, long elapsed) => Records.Add(name, elapsed);
}
namespace UnityEngine
{
    public class PhysicsMaterial { }
    public class Collider { public bool enabled; }
    public class SphereCollider : Collider { public Vector3 center; public float radius; public PhysicsMaterial sharedMaterial; public bool isTrigger; }
    public class GameObject { public int layer, Creates; public T AddComponent<T>() where T : new() { Creates++; return new T(); } }
    public class Transform { public Vector3 position; public Matrix4x4 worldToLocalMatrix => default; public Vector3 InverseTransformPoint(Vector3 point) => point; }
    public struct Matrix4x4
    {
        private System.Numerics.Matrix4x4 value;
        public static int TrsCalls;
        public Vector3 lossyScale => new(1, 1, 1);
        public static Matrix4x4 Scale(Vector3 scale) => new() { value = System.Numerics.Matrix4x4.CreateScale(scale.x, scale.y, scale.z) };
        public static Matrix4x4 TRS(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            TrsCalls++;
            return new() { value = System.Numerics.Matrix4x4.CreateScale(scale.x, scale.y, scale.z)
                * System.Numerics.Matrix4x4.CreateFromQuaternion(rotation.Value)
                * System.Numerics.Matrix4x4.CreateTranslation(position.x, position.y, position.z) };
        }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => new() { value = b.value * a.value };
        public Vector3 MultiplyPoint3x4(Vector3 point)
        {
            var result = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(point.x, point.y, point.z), value);
            return new(result.X, result.Y, result.Z);
        }
    }
    public readonly record struct Vector2Int(int x, int y);
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 zero => default;
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
    public readonly struct Ray
    {
        public readonly Vector3 origin, direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction; }
        public Vector3 GetPoint(float distance) => origin + direction * distance;
    }
    public readonly struct Quaternion
    {
        private readonly System.Numerics.Quaternion value;
        internal System.Numerics.Quaternion Value => value;
        public static int AngleCalls, SlerpCalls;
        private Quaternion(System.Numerics.Quaternion value) { this.value = value; }
        public static Quaternion identity => new(System.Numerics.Quaternion.Identity);
        public static Quaternion Euler(float x, float y, float z) => new(System.Numerics.Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180));
        public static float Angle(Quaternion a, Quaternion b)
        {
            AngleCalls++;
            return 2 * MathF.Acos(MathF.Min(1, MathF.Abs(System.Numerics.Quaternion.Dot(a.value, b.value)))) * 180 / MathF.PI;
        }
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
        { SlerpCalls++; return new(System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Slerp(a.value, b.value, t))); }
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            var result = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(point.x, point.y, point.z), rotation.value);
            return new(result.X, result.Y, result.Z);
        }
    }
    public static class Mathf
    {
        public static float Abs(float value) => MathF.Abs(value);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Max(float a, float b, float c) => Max(Max(a, b), c);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0, 1);
        public static float Sqrt(float value) => MathF.Sqrt(value);
        public static int FloorToInt(float value) => (int)MathF.Floor(value);
        public static int CeilToInt(float value) => (int)MathF.Ceiling(value);
    }
}
