using System;
using System.Reflection;
using ProjectF.Rendering;
using UnityEngine;

public static class ProjectFApplicationLifecycle
{
    public static bool IsQuitting { get; set; }
}

static class Checks
{
    private static int checks;
    private static readonly MethodInfo LateUpdate = typeof(WorldColliderCullingManager)
        .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo OnDestroy = typeof(WorldColliderCullingManager)
        .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo OnDisable = typeof(WorldColliderCullingManager)
        .GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Tick(WorldColliderCullingManager manager, int frame)
    {
        Time.frameCount = frame;
        LateUpdate.Invoke(manager, null);
    }

    private static void Main()
    {
        GameManager.Instance = new GameManager
        {
            Player = new Player { BodyTransform = new Transform { position = Vector3.zero } }
        };

        var near = new Target(0f, 2);
        var farA = new Target(100f, 3);
        var farB = new Target(100f, 1);
        WorldColliderCullingManager.Register(near);
        WorldColliderCullingManager.Register(farA);
        WorldColliderCullingManager.Register(farB);
        var manager = (WorldColliderCullingManager)typeof(WorldColliderCullingManager)
            .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        Require(manager.RegisteredTargetCount == 3 && manager.ManagedColliderCount == 6
                && manager.SpatialCellCount == 2 && manager.PendingTargetCount == 3,
            "registration builds the spatial index and initial evaluation queue");
        Tick(manager, 1);
        Require(!near.ColliderCullingCulled && farA.ColliderCullingCulled
                && farB.ColliderCullingCulled && manager.CulledTargetCount == 2,
            "initial bounded sweep culls distant targets and keeps the near target active");
        Require(manager.PendingTargetCount == 0 && manager.ActiveTargetCount == 1,
            "small worlds complete initial evaluation without leaving duplicate work");
        Tick(manager, 2);
        Require(manager.LastCheckedTargetCount == 0 && manager.LastSpatialCandidateCount == 0,
            "stationary steady state performs no world-size collider scan");

        GameManager.Instance.Player.BodyTransform.position = new Vector3(60f, 0f, 0f);
        Tick(manager, 3);
        Require(farA.ColliderCullingCulled,
            "a culled collider stays culled outside the inner enable distance");
        GameManager.Instance.Player.BodyTransform.position = new Vector3(65f, 0f, 0f);
        Tick(manager, 4);
        Require(!farA.ColliderCullingCulled && !farB.ColliderCullingCulled,
            "nearby spatial-cell candidates return inside the enable distance");
        GameManager.Instance.Player.BodyTransform.position = new Vector3(60f, 0f, 0f);
        Tick(manager, 5);
        Require(!farA.ColliderCullingCulled,
            "hysteresis prevents boundary movement from toggling every refresh");
        GameManager.Instance.Player.BodyTransform.position = new Vector3(55f, 0f, 0f);
        Tick(manager, 6);
        Require(farA.ColliderCullingCulled && farB.ColliderCullingCulled,
            "active targets cull beyond the outer disable distance");

        farA.Exempt = true;
        WorldColliderCullingManager.RefreshSpatialRegistration(farA);
        Tick(manager, 7);
        Require(!farA.ColliderCullingCulled && farA.ColliderCullingActiveIndex == -1,
            "explicitly refreshed dynamic targets restore and leave the active static set");

        WorldColliderCullingManager.Unregister(near);
        Require(near.ColliderCullingRegistryIndex == -1 && farB.ColliderCullingRegistryIndex == 0,
            "registry swap removal repairs the moved target index");
        Vector2Int oldCell = farA.ColliderCullingCell;
        farA.X = -100f;
        farA.Exempt = false;
        WorldColliderCullingManager.RefreshSpatialRegistration(farA);
        Require(farA.ColliderCullingCell != oldCell && farA.ColliderCullingPendingIndex >= 0,
            "position changes move targets between cells and queue one reevaluation");
        Tick(manager, 8);
        Require(farA.ColliderCullingCulled,
            "relocated distant target is culled by the pending evaluation budget");

        GameManager.Instance.FreeCamera = true;
        Camera.main = new Camera();
        Camera.main.transform.position = new Vector3(100f, 20f, 0f);
        Tick(manager, 9);
        Require(!farB.ColliderCullingCulled,
            "free-camera interaction range uses the free view when player culling is disabled");
        GameManager.Instance.FreeCamera = false;
        Tick(manager, 10);
        Require(farB.ColliderCullingCulled,
            "leaving free camera returns collider range ownership to the player");

        CameraRenderCulling.Disabled = true;
        Tick(manager, 11);
        Require(!farA.ColliderCullingCulled && !farB.ColliderCullingCulled
                && manager.CulledTargetCount == 0,
            "global culling disable restores every collider immediately");
        CameraRenderCulling.Disabled = false;

        farB.Alive = false;
        WorldColliderCullingManager.RefreshSpatialRegistration(farB);
        Tick(manager, 12);
        Require(farB.ColliderCullingRegistryIndex == -1 && farB.ReleaseCalls == 1,
            "dead queued targets unregister and release collider ownership");

        OnDestroy.Invoke(manager, null);
        Require(farA.ColliderCullingRegistryIndex == -1 && farA.ReleaseCalls == 1,
            "manager destruction restores and releases remaining targets");

        var quittingTarget = new Target(100f, 2);
        WorldColliderCullingManager.Register(quittingTarget);
        manager = (WorldColliderCullingManager)typeof(WorldColliderCullingManager)
            .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Tick(manager, 13);
        Require(quittingTarget.ColliderCullingCulled,
            "quit fixture starts with a culled target");
        ProjectFApplicationLifecycle.IsQuitting = true;
        OnDisable.Invoke(manager, null);
        WorldColliderCullingManager.Unregister(quittingTarget);
        OnDestroy.Invoke(manager, null);
        Require(quittingTarget.ColliderCullingCulled && quittingTarget.ReleaseCalls == 0,
            "application quit skips collider restore and registry teardown");
        Console.WriteLine($"PASS: {checks} production spatial collider-culling checks. Managed Unity doubles; no engine launched.");
    }

    private sealed class Target : IWorldColliderCullingTarget
    {
        internal Target(float x, int colliders)
        {
            X = x;
            ColliderCullingManagedCount = colliders;
        }

        internal float X;
        internal bool Alive = true;
        internal bool Exempt;
        internal int ReleaseCalls;
        public bool ColliderCullingAlive => Alive;
        public bool ColliderCullingExempt => Exempt;
        public bool ColliderCullingCulled { get; private set; }
        public Vector3 ColliderCullingPosition => new Vector3(X, 0f, 0f);
        public float ColliderCullingRadius => 0f;
        public int ColliderCullingManagedCount { get; }
        public int ColliderCullingAccountedCount { get; set; }
        public int ColliderCullingRegistryIndex { get; set; } = -1;
        public Vector2Int ColliderCullingCell { get; set; }
        public int ColliderCullingCellIndex { get; set; } = -1;
        public int ColliderCullingPendingIndex { get; set; } = -1;
        public int ColliderCullingActiveIndex { get; set; } = -1;
        public void ApplyColliderCulling(bool culled) => ColliderCullingCulled = culled;
        public void ReleaseColliderCulling()
        {
            ColliderCullingCulled = false;
            ReleaseCalls++;
        }
    }
}

public sealed class GameManager
{
    public static GameManager Instance;
    public Player Player;
    public bool FreeCamera;
    public bool FreeCameraPlayerCulling;
}

public sealed class Player : MonoBehaviour
{
    public Transform BodyTransform;
}

public static class MapObjectTickProfiler
{
    public static Scope SampleNamed(string kind, string type, string name) => default;
    public static void AddRuntimeCounter(string group, string name, int value) { }
    public static void AddRuntimeCounter(string group, string name, float value) { }
    public readonly struct Scope : IDisposable { public void Dispose() { } }
}

namespace ProjectF.Rendering
{
    public static class CameraRenderCulling
    {
        public static bool Disabled;
    }
}

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { }
    }

    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public static class Application { public static bool isPlaying = true; }
    public static class Time { public static int frameCount; }

    public class Component
    {
        public GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
    }

    public class MonoBehaviour : Component
    {
        protected static void DontDestroyOnLoad(GameObject host) { }
    }

    public sealed class GameObject
    {
        public Transform transform;
        public GameObject(string name = "") { transform = new Transform(); }
        public T AddComponent<T>() where T : Component, new()
        {
            var result = new T { gameObject = this };
            return result;
        }
    }

    public class Transform
    {
        public Vector3 position;
    }

    public sealed class Camera : Component
    {
        public static Camera main;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
    }

    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object value) => value is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2Int left, Vector2Int right) => left.Equals(right);
        public static bool operator !=(Vector2Int left, Vector2Int right) => !left.Equals(right);
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static int FloorToInt(float value) => (int)MathF.Floor(value);
    }
}
