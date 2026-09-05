using System;
using System.Collections.Generic;
using System.Reflection;
using ProjectF.Rendering;
using UnityEngine;

static class Checks
{
    private static int checks;
    static void Check(bool condition, string scenario)
    {
        checks++;
        if (!condition) throw new Exception(scenario);
    }

    static void Main()
    {
        var owner = new InstallationObject();
        var animator = owner.Add(new Animator());
        var disabledAnimator = owner.Add(new Animator { enabled = false });
        var effect = owner.Add(new ParticleSystem());
        var state = new InstallationVisualState(owner);
        var culling = new CameraRenderCulling();
        state.SetParticle(effect, true, 2f, false);
        state.Tick(culling, 0.1f);
        Check(owner.VisualTicks == 1 && effect.isEmitting, "visible visual work runs");
        culling.InView = false;
        state.Tick(culling, 0.1f);
        Check(owner.VisualTicks == 1 && !state.Visible, "offscreen skips visual work");
        Check(!animator.enabled && animator.keepAnimatorStateOnDisable, "offscreen retains and suspends Animator");
        Check(!effect.isPlaying, "offscreen stops particle simulation");
        int stops = effect.Stops;
        int plays = effect.Plays;
        for (int i = 0; i < 20; i++)
        {
            state.SetParticle(effect, true, 3f, false);
            state.Tick(culling, 0.1f);
        }
        Check(effect.Stops == stops && effect.Plays == plays, "hidden requests do not repeatedly call particle APIs");
        state.SetParticle(effect, false, 1f, false);
        culling.InView = true;
        state.Tick(culling, 0.1f);
        Check(animator.enabled && !animator.keepAnimatorStateOnDisable, "return restores owned Animator flags");
        Check(!disabledAnimator.enabled, "return preserves externally disabled Animator");
        Check(!effect.isPlaying && effect.Plays == plays, "work ended offscreen does not restart particles");
        Check(owner.Resumes == 1, "return refreshes current work state exactly once");

        culling.InView = false;
        state.Tick(culling, 0.1f);
        state.SetParticle(effect, true, 4f, false);
        culling.Enabled = false; // shared toggle off, or no game camera
        state.Tick(culling, 0.1f);
        Check(state.Visible && animator.enabled && effect.isEmitting, "disabled culling restores visuals outside frustum");
        Check(effect.main.simulationSpeed == 4f, "return uses latest requested speed");
        Check(effect.Plays == plays + 1, "one resume, no offscreen replay backlog");

        culling.Enabled = true;
        culling.InView = false;
        state.Tick(culling, 0.1f);
        owner.OnResume = () => state.SetParticle(effect, false, 1f, true);
        culling.InView = true;
        state.Tick(culling, 0.1f);
        Check(!effect.isPlaying, "resume callback can cancel stale effect before restoration");
        owner.OnResume = null;
        culling.LayersVisible = false;
        state.Tick(culling, 0.1f);
        Check(!state.Visible, "camera layer mask suppresses visual work");
        culling.LayersVisible = true;
        state.Tick(culling, 0.1f);

        // Warm-up first; repeat the real production tick with managed camera/scene doubles.
        for (int i = 0; i < 100; i++) state.Tick(culling, 0.1f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) state.Tick(culling, 0.1f);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "steady-state visual loop allocates no managed memory in probe");

        culling.InView = false;
        state.Tick(culling, 0.1f);
        plays = effect.Plays;
        state.Release();
        Check(animator.enabled && !animator.keepAnimatorStateOnDisable, "pool release restores owned Animator state");
        Check(!disabledAnimator.enabled && effect.Plays == plays, "pool release does not enable external Animator or replay effects");
        culling.InView = true;
        state.Tick(culling, 0.1f);
        culling.InView = false;
        state.Tick(culling, 0.1f);
        Check(!animator.enabled, "reused installation recaptures animation state");
        state.Release();

        var a = new InstallationVisualState(new InstallationObject());
        var b = new InstallationVisualState(new InstallationObject());
        var c = new InstallationVisualState(new InstallationObject());
        WorldVisualUpdateManager.Register(a);
        WorldVisualUpdateManager.Register(b);
        WorldVisualUpdateManager.Register(c);
        WorldVisualUpdateManager.Register(b);
        Check(a.Index == 0 && b.Index == 1 && c.Index == 2, "registry does not duplicate registration");
        WorldVisualUpdateManager.Unregister(b);
        Check(b.Index == -1 && c.Index == 1, "registry swap removal repairs moved index");
        WorldVisualUpdateManager.Unregister(b);
        var manager = (WorldVisualUpdateManager)typeof(WorldVisualUpdateManager)
            .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        typeof(WorldVisualUpdateManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, null);
        Check(a.Owner.VisualTicks == 1 && c.Owner.VisualTicks == 1 && b.Owner.VisualTicks == 0,
            "single manager dispatches registered owners only");
        Check(manager.RegisteredCount == 2 && manager.VisibleCount == 2, "manager counters reflect dispatch");
        a.Owner.isActiveAndEnabled = false;
        typeof(WorldVisualUpdateManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, null);
        Check(manager.RegisteredCount == 1 && a.Index == -1, "inactive owner safely unregisters during dispatch");
        typeof(WorldVisualUpdateManager).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(manager, null);
        Check(c.Index == -1, "manager destruction releases all registrations");
        Console.WriteLine($"PASS: {checks} production visual-state/central-dispatch checks. Managed Unity doubles; no engine launched.");
    }
}

public class InstallationObject : MonoBehaviour
{
    private readonly List<Component> components = new List<Component>();
    public int VisualTicks, Resumes;
    public Action OnResume;
    public InstallationObject() { gameObject.Owner = this; }
    public T Add<T>(T component) where T : Component
    {
        component.gameObject = gameObject;
        components.Add(component);
        return component;
    }
    public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
    {
        var result = new List<T>();
        foreach (var component in components) if (component is T target) result.Add(target);
        return result.ToArray();
    }
    internal void RunManagedVisualUpdate(float dt) { VisualTicks++; }
    internal void RefreshManagedVisualState() { Resumes++; OnResume?.Invoke(); }
}
public static class VirtualRenderBatchCollection
{
    internal static Bounds CalculateWorldBounds(Bounds bounds, Matrix4x4 matrix) => bounds;
}
namespace ProjectF.Rendering
{
    // Frustum math is covered separately by ConveyorCameraCullingHarness.
    public class CameraRenderCulling
    {
        public bool Enabled = true, InView = true, LayersVisible = true;
        public void Update(Camera camera) { }
        public bool IsAnyLayerVisible(int mask) => !Enabled || LayersVisible;
        public bool Intersects(Bounds bounds) => !Enabled || InView;
    }
}
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
    public class Component
    {
        public GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
        public T GetComponentInParent<T>() where T : class => gameObject.Owner as T;
    }
    public class MonoBehaviour : Component
    {
        public bool isActiveAndEnabled = true;
        protected static void DontDestroyOnLoad(GameObject host) { }
    }
    public class GameObject
    {
        public InstallationObject Owner;
        public int layer;
        public bool activeInHierarchy = true;
        public Transform transform;
        public GameObject(string name = "") { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() => new T { gameObject = this };
    }
    public class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public Vector3 position;
        public Matrix4x4 localToWorldMatrix, worldToLocalMatrix;
        public T GetComponentInParent<T>() where T : class => gameObject.Owner as T;
    }
    public class Camera { public static Camera main = new Camera(); }
    public static class Application { public static bool isPlaying = true; }
    public static class Time { public static float deltaTime = 0.1f; }
    public static class Physics { public static Vector3 gravity = new Vector3(0f, -9.81f, 0f); }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero => default;
        public static Vector3 one => new Vector3(1, 1, 1);
        public float magnitude => MathF.Sqrt(x*x + y*y + z*z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b, a.y*b, a.z*b);
    }
    public struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        public bool Equals(Matrix4x4 other) => true;
    }
    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { }
        public void Encapsulate(Bounds bounds) { }
        public void Expand(float amount) { }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a,b);
        public static float Abs(float a) => MathF.Abs(a);
        public static bool Approximately(float a, float b) => MathF.Abs(a-b) < 0.00001f;
    }
    public class Renderer : Component { public Bounds bounds; }
    public class Animator : Component
    {
        public bool enabled = true, keepAnimatorStateOnDisable, isInitialized;
        public int Samples;
        public void Update(float dt) { isInitialized = true; Samples++; }
    }
    public enum ParticleSystemStopBehavior { StopEmitting, StopEmittingAndClear }
    public class ParticleSystem : Component
    {
        public bool isEmitting, isPlaying, isPaused;
        public int Plays, Stops;
        private float speed = 1f;
        public MainModule main => new MainModule(this);
        public void Play(bool children) { Plays++; isPlaying = isEmitting = true; isPaused = false; }
        public void Stop(bool children, ParticleSystemStopBehavior behavior)
        { Stops++; isPlaying = isEmitting = isPaused = false; }
        public struct MinMaxCurve { public float constantMax; }
        public struct MainModule
        {
            private ParticleSystem effect;
            public MainModule(ParticleSystem target) { effect = target; }
            public bool loop => true;
            public float simulationSpeed { get => effect.speed; set => effect.speed = value; }
            public MinMaxCurve startLifetime => new MinMaxCurve { constantMax = 1f };
            public MinMaxCurve startSpeed => default;
            public MinMaxCurve gravityModifier => default;
        }
    }
}

