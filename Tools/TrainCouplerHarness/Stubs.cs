using System;
using System.Collections.Generic;
using N = System.Numerics;
using ProjectF.Railway;
using UnityEngine;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public enum HideFlags { DontSave }
    public static class Application { public static bool isPlaying = true; }
    public class MonoBehaviour
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public static void Destroy(GameObject value) { }
        public static void DestroyImmediate(GameObject value) { }
    }
    public sealed class GameObject
    {
        public readonly Transform transform = new Transform();
        public bool activeInHierarchy = true;
        public HideFlags hideFlags;
        public GameObject(string name) { }
        public T AddComponent<T>() where T : MonoBehaviour, new() => new T { gameObject = this };
        public void SetActive(bool active) => activeInHierarchy = active;
    }
    public sealed class Transform
    {
        public Transform parent;
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        private Quaternion local = Quaternion.identity;
        public int RotationWrites;
        public Quaternion localRotation { get => local; set { local = value; RotationWrites++; } }
        public Quaternion rotation
        {
            get => parent == null ? local : parent.rotation * local;
            set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value;
        }
        public Vector3 position => localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
        public Vector3 lossyScale => parent == null ? localScale : Vector3.Scale(parent.lossyScale, localScale);
        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                var matrix = N.Matrix4x4.CreateScale(localScale.Value) *
                    N.Matrix4x4.CreateFromQuaternion(local.Value) * N.Matrix4x4.CreateTranslation(localPosition.Value);
                return new Matrix4x4(parent == null ? matrix : matrix * parent.localToWorldMatrix.Value);
            }
        }
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            N.Matrix4x4.Invert(localToWorldMatrix.Value, out var inverse);
            return new Vector3(N.Vector3.Transform(point.Value, inverse));
        }
        public bool IsChildOf(Transform ancestor)
        { for (Transform p = this; p != null; p = p.parent) if (p == ancestor) return true; return false; }
    }
    public readonly struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        internal readonly N.Matrix4x4 Value;
        internal Matrix4x4(N.Matrix4x4 value) { Value = value; }
        public Vector3 MultiplyPoint3x4(Vector3 point) => new Vector3(N.Vector3.Transform(point.Value, Value));
        public bool Equals(Matrix4x4 other) => Value.Equals(other.Value);
    }
    public struct Vector3
    {
        public float x, y, z;
        internal N.Vector3 Value => new N.Vector3(x, y, z);
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal Vector3(N.Vector3 value) : this(value.X, value.Y, value.Z) { }
        public static Vector3 zero => new Vector3();
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public float sqrMagnitude => Value.LengthSquared();
        public float magnitude => Value.Length();
        public Vector3 normalized => new Vector3(N.Vector3.Normalize(Value));
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.Value * b.Value);
        public static float Dot(Vector3 a, Vector3 b) => N.Vector3.Dot(a.Value, b.Value);
        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            float dot = Dot(from.normalized, to.normalized);
            float angle = MathF.Acos(Math.Clamp(dot, -1, 1)) * 180f / MathF.PI;
            float cross = N.Vector3.Dot(axis.Value, N.Vector3.Cross(from.Value, to.Value));
            return cross < 0 ? -angle : angle;
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.Value + b.Value);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.Value - b.Value);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.Value * b);
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object other) => other is Vector2Int value && Equals(value);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
    }
    public readonly struct Quaternion
    {
        internal readonly N.Quaternion Value;
        internal Quaternion(N.Quaternion value) { Value = value; }
        public static Quaternion identity => new Quaternion(N.Quaternion.Identity);
        public static Quaternion Inverse(Quaternion value) => new Quaternion(N.Quaternion.Inverse(value.Value));
        public static Quaternion AngleAxis(float angle, Vector3 axis) =>
            new Quaternion(N.Quaternion.CreateFromAxisAngle(axis.normalized.Value, angle * MathF.PI / 180f));
        public static float Dot(Quaternion a, Quaternion b) => N.Quaternion.Dot(a.Value, b.Value);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(a.Value * b.Value);
        public static Vector3 operator *(Quaternion a, Vector3 b) => new Vector3(N.Vector3.Transform(b.Value, a.Value));
    }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Abs(float value) => MathF.Abs(value);
        public static int RoundToInt(float value) => (int)MathF.Round(value);
    }
    public struct Plane { }
    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { }
        public void Encapsulate(Vector3 point) { }
        public void Expand(float amount) { }
    }
    public sealed class Camera { public static Camera main; public bool isActiveAndEnabled = true; }
    public static class GeometryUtility
    {
        public static bool Visible = true;
        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes) { }
        public static bool TestPlanesAABB(Plane[] planes, Bounds bounds) => Visible;
    }
}
namespace Unity.Profiling
{
    public readonly struct ProfilerMarker
    {
        public ProfilerMarker(string name) { }
        public Scope Auto() => new Scope();
        public readonly struct Scope : IDisposable { public void Dispose() { } }
    }
}
public static class MapObjectTickProfiler
{
    public static Unity.Profiling.ProfilerMarker.Scope SampleLateUpdateCaller<T>() => new Unity.Profiling.ProfilerMarker.Scope();
}
public static class MapObjectTickManager { public static bool WaitingForWorldLoad; }
public sealed class Train
{
    public readonly GameObject gameObject = new GameObject("Train");
    public Transform transform => gameObject.transform;
    public Transform[] Couplers;
    public float FrontReach = 0.5f, RearReach = 0.5f;
    private TrainCouplerRig rig;
    internal TrainCouplerRig CouplerRig => rig ?? (rig = new TrainCouplerRig(this, Couplers, 80f));
    internal float ResolveConnectionReach(bool front) => front ? FrontReach : RearReach;
}
namespace ProjectF.Railway
{
    public readonly struct TrainConnection
    {
        public readonly TrainInstance Target;
        public readonly bool AtFront;
        public TrainConnection(TrainInstance target, bool atFront) { Target = target; AtFront = atFront; }
    }
    public sealed class TrainInstance
    {
        public TrainInstance(TrainWorld world) { World = world; }
        public readonly TrainWorld World;
        public bool IsAlive => World.Contains(this);
        public readonly List<TrainConnection> Connections = new List<TrainConnection>(2);
        public bool IsActive, IsPlaced;
        public Vector2Int Coordinate;
        public long PlacementSequence;
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public int Motion, Handle;
        public int ConnectionCount => Connections.Count;
        public TrainConnection GetConnection(int index) => Connections[index];
        public int FindConnection(TrainInstance other)
        { for (int i = 0; i < Connections.Count; i++) if (Connections[i].Target == other) return i; return -1; }
        public void ClearRailSample() { }
    }
}
