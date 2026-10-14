using System;
using System.Collections.Generic;

namespace UnityEngine
{
    // Scene and PhysX ports only. Component binding, pose and lifetime use production code.
    public sealed partial class GameObject
    {
        private readonly Dictionary<Type, object> components = new();
        public int layer;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class
        {
            var found = new List<T>();
            foreach (var component in components.Values)
                if (component is T match) found.Add(match);
            return found.ToArray();
        }
        public T GetComponent<T>() where T : class => transform is T found ? found
            : components.TryGetValue(typeof(T), out var value) ? value as T : null;
        public T AddComponent<T>() where T : FixtureComponent, new()
        {
            var component = new T { gameObject = this };
            components.Add(typeof(T), component);
            return component;
        }
    }
    public abstract class FixtureComponent : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }
    public struct Bounds
    {
        public Vector3 center, extents;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; extents = size * .5f; }
        public Vector3 min => center - extents;
        public Vector3 max => center + extents;
        public bool IntersectRay(Ray ray, out float distance) { distance = 0f; return false; }
    }
    public sealed class Sprite : Object { }
    public class Renderer : FixtureComponent
    {
        public Bounds localBounds;
        public Bounds bounds => localBounds;
        public bool enabled = true;
    }
    public sealed class MeshRenderer : Renderer { }
    public sealed class SkinnedMeshRenderer : Renderer { }
    public sealed class CapsuleCollider : FixtureComponent
    {
        public bool enabled, isTrigger;
        public int direction;
        public float radius, height;
        public Vector3 center;
    }
    public enum RigidbodyInterpolation { None, Interpolate }
    public enum CollisionDetectionMode { Discrete, ContinuousSpeculative }
    public sealed class Rigidbody : FixtureComponent
    {
        public bool isKinematic, useGravity, detectCollisions;
        public RigidbodyInterpolation interpolation;
        public CollisionDetectionMode collisionDetectionMode;
        public Vector3 position { get => transform.position; set => transform.position = value; }
        public Quaternion rotation { get => transform.rotation; set => transform.rotation = value; }
    }
}

internal static class FixtureMonsterPrefab
{
    internal static UnityEngine.GameObject Create()
    {
        var root = new UnityEngine.GameObject { layer = 8 };
        var capsule = root.AddComponent<UnityEngine.CapsuleCollider>();
        capsule.enabled = true;
        capsule.direction = 1;
        capsule.radius = .42f;
        capsule.height = 1.3f;
        capsule.center = new UnityEngine.Vector3(.1f, .65f, .2f);
        var body = root.AddComponent<UnityEngine.Rigidbody>();
        body.isKinematic = true;
        body.detectCollisions = true;
        body.interpolation = UnityEngine.RigidbodyInterpolation.None;
        body.collisionDetectionMode = UnityEngine.CollisionDetectionMode.ContinuousSpeculative;
        return root;
    }

    internal static UnityEngine.GameObject Clone(UnityEngine.GameObject source, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation)
    {
        var root = new UnityEngine.GameObject { layer = source.layer };
        root.transform.SetPositionAndRotation(position, rotation);
        var capsule = source.GetComponent<UnityEngine.CapsuleCollider>();
        if (capsule != null)
        {
            var copy = root.AddComponent<UnityEngine.CapsuleCollider>();
            copy.enabled = capsule.enabled;
            copy.isTrigger = capsule.isTrigger;
            copy.direction = capsule.direction;
            copy.radius = capsule.radius;
            copy.height = capsule.height;
            copy.center = capsule.center;
        }
        var body = source.GetComponent<UnityEngine.Rigidbody>();
        if (body != null)
        {
            var copy = root.AddComponent<UnityEngine.Rigidbody>();
            copy.isKinematic = body.isKinematic;
            copy.useGravity = body.useGravity;
            copy.detectCollisions = body.detectCollisions;
            copy.interpolation = body.interpolation;
            copy.collisionDetectionMode = body.collisionDetectionMode;
        }
        return root;
    }
}

namespace ProjectF.Monsters
{
    internal static class MonsterFocusWorld
    {
        internal static void Register(MonsterInstance monster) { }
        internal static void Unregister(MonsterInstance monster) { }
    }
}

internal static class AnimalScreenSpaceOutline
{
    internal static void ShowHovered(UnityEngine.Renderer renderer) { }
    internal static void HideHovered(UnityEngine.Renderer renderer) { }
    internal static void ShowFocused(UnityEngine.Renderer renderer) { }
    internal static void HideFocused(UnityEngine.Renderer renderer) { }
}
