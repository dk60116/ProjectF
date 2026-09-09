// CPU-only rendering facade: geometry, lifecycle and submission logic run from production files.
// This deliberately does not simulate Unity's GPU sorting, rasterization or destroyed-object null semantics.
using System;
using System.Collections.Generic;
using System.Linq;
using N = System.Numerics;

namespace UnityEngine
{
    public class SerializeField : Attribute { }
    public class MinAttribute : Attribute { public MinAttribute(float value) { } }
    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public bool Destroyed;
        public static void Destroy(Object value) { value.Destroyed = true; }
        public static void DestroyImmediate(Object value) => Destroy(value);
    }
    public enum HideFlags { HideAndDontSave }
    public class GameObject : Object { public int layer; }
    public class MonoBehaviour : Object
    {
        public Transform transform = new Transform();
        public bool isActiveAndEnabled = true;
        public SpriteRenderer[] Renderers = Array.Empty<SpriteRenderer>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Renderers.Cast<T>().ToArray();
    }
    public class Transform
    {
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public Transform parent;
        public Matrix4x4 localToWorldMatrix => (parent != null ? parent.localToWorldMatrix : Matrix4x4.identity)
            * Matrix4x4.TRS(localPosition, localRotation, localScale);
        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;
        public Vector3 position { get => localToWorldMatrix.MultiplyPoint3x4(Vector3.zero); set => localPosition = value; }
    }
    public struct Vector2 : IEquatable<Vector2>
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public bool Equals(Vector2 other) => x == other.x && y == other.y;
    }
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
    }
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3();
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);
        public override bool Equals(object obj) => obj is Vector3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }
    public struct Quaternion
    {
        internal N.Quaternion Value;
        public static Quaternion identity => new Quaternion { Value = N.Quaternion.Identity };
        public static Quaternion Euler(float x, float y, float z) => new Quaternion
        { Value = N.Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180) };
    }
    public struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        internal N.Matrix4x4 Value;
        public static Matrix4x4 identity => new Matrix4x4 { Value = N.Matrix4x4.Identity };
        public Matrix4x4 inverse { get { N.Matrix4x4.Invert(Value, out N.Matrix4x4 result); return new Matrix4x4 { Value = result }; } }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => new Matrix4x4 { Value = b.Value * a.Value };
        public static Matrix4x4 Translate(Vector3 p) => TRS(p, Quaternion.identity, Vector3.one);
        public static Matrix4x4 TRS(Vector3 p, Quaternion r, Vector3 s) => new Matrix4x4
        { Value = N.Matrix4x4.CreateScale(s.x, s.y, s.z) * N.Matrix4x4.CreateFromQuaternion(r.Value) * N.Matrix4x4.CreateTranslation(p.x, p.y, p.z) };
        public Vector3 MultiplyPoint3x4(Vector3 p)
        { N.Vector3 v = N.Vector3.Transform(new N.Vector3(p.x, p.y, p.z), Value); return new Vector3(v.X, v.Y, v.Z); }
        public bool Equals(Matrix4x4 other) => Value.Equals(other.Value);
    }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int FloorToInt(float value) => (int)MathF.Floor(value);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    }
    public class Texture : Object { }
    public class Sprite : Object
    {
        public Texture texture;
        public int GeometryReads;
        public Vector2[] vertices { get { GeometryReads++; return new[] { new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 0), new Vector2(0, -1) }; } }
        public Vector2[] uv => new[] { new Vector2(.2f, .3f), new Vector2(.2f, .7f), new Vector2(.6f, .7f), new Vector2(.6f, .3f) };
        public ushort[] triangles => new ushort[] { 0, 1, 2, 0, 2, 3 };
    }
    public class SpriteRenderer
    {
        public Sprite sprite;
        public Transform transform = new Transform();
        public GameObject gameObject = new GameObject();
        public int sortingOrder;
        public Color color;
        public bool flipX, flipY;
    }
    public class Material : Object
    {
        public Texture mainTexture;
        public int renderQueue, DepthTest;
        public Material(Material source) { }
        public void SetInt(string key, int value) { if (key == "_ZTest") DepthTest = value; }
    }
    public static class Resources
    {
        public static AreaMarker Template;
        public static Material Source = new Material(null);
        public static T Load<T>(string path) where T : class => (path.StartsWith("Prefab/") ? (object)Template : Source) as T;
    }
    public struct Bounds { }
    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat;
        public Bounds bounds;
        public int Uploads;
        public List<Vector3> Vertices;
        public List<Vector2> UV;
        public List<Color> Colors;
        public List<int> Triangles;
        public void MarkDynamic() { }
        public void Clear() { }
        public void SetVertices(List<Vector3> value) { Vertices = new List<Vector3>(value); Uploads++; }
        public void SetUVs(int channel, List<Vector2> value) { UV = new List<Vector2>(value); }
        public void SetColors(List<Color> value) { Colors = new List<Color>(value); }
        public void SetTriangles(List<int> value, int submesh, bool bounds) { Triangles = new List<int>(value); }
    }
    public enum MotionVectorGenerationMode { ForceNoMotion }
    public struct RenderParams
    {
        public Material material;
        public int layer, rendererPriority;
        public Bounds worldBounds;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public Rendering.LightProbeUsage lightProbeUsage;
        public Rendering.ReflectionProbeUsage reflectionProbeUsage;
        public MotionVectorGenerationMode motionVectorMode;
        public RenderParams(Material value) { this = default; material = value; }
    }
    public static class Graphics
    {
        public static readonly List<(RenderParams Parameters, Mesh Mesh)> Calls = new();
        public static void RenderMesh(RenderParams parameters, Mesh mesh, int submesh, Matrix4x4 matrix) => Calls.Add((parameters, mesh));
    }
    public static class Application { public static bool isPlaying = true; }
}
namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off }
    public enum LightProbeUsage { Off }
    public enum ReflectionProbeUsage { Off }
    public enum IndexFormat { UInt32 }
    public enum CompareFunction { LessEqual = 4, Always = 8 }
}
namespace Unity.Profiling
{
    public struct ProfilerMarker
    {
        public ProfilerMarker(string name) { }
        public Scope Auto() => new Scope();
        public struct Scope : IDisposable { public void Dispose() { } }
    }
}
public class Player : UnityEngine.MonoBehaviour { public UnityEngine.Transform BodyTransform; }
public class GameManager
{
    public static GameManager Instance;
    public Player Player;
    public bool InstallationPlacementActive, MapEditActive;
}
