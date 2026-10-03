using UnityEngine;
public sealed class ProductionFacilityInstance { public double AnimationPhase; public long SimulationId = 7; }
public readonly struct VirtualRenderBatchKey
{
    public readonly Mesh Mesh; public readonly Material Material;
    public VirtualRenderBatchKey(Mesh mesh, Material material, int layer, int sub,
        UnityEngine.Rendering.ShadowCastingMode shadow, bool receive, bool scroll, int batchCellX, int batchCellZ)
    { Mesh = mesh; Material = material; }
}
public sealed class VirtualRenderBatchCollection
{
    public readonly List<(VirtualRenderBatchKey Key, Matrix4x4 Matrix)> Items = new();
    public void AddMatrix(VirtualRenderBatchKey key, Matrix4x4 matrix) => Items.Add((key, matrix));
}
namespace ProjectF.Rendering
{
    public static class InstallationMaterialVariants { public static void Destroy(object value) { } }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace UnityEngine
{
    public enum ParticleSystemRenderMode { Billboard, Stretch, HorizontalBillboard, VerticalBillboard }
    public enum ParticleSystemAnimationTimeMode { Lifetime, Speed, FPS }
    public class GameObject { public int layer; }
    public class Transform
    {
        public Matrix4x4 worldToLocalMatrix = Matrix4x4.identity, localToWorldMatrix = Matrix4x4.identity;
        public Quaternion rotation = default; public Vector3 forward = new(0, -.7f, .7f);
    }
    public class Camera { public Transform transform = new(); }
    public class Material
    {
        public string name = "particle"; public bool enableInstancing;
        public Material() { } public Material(Material source) { name = source.name; }
    }
    public class ParticleSystemRenderer
    {
        public bool enabled = true; public Material sharedMaterial = new();
        public ParticleSystemRenderMode renderMode; public Vector3 pivot;
    }
    public class ParticleSystem
    {
        public MainModule main = new(); public EmissionModule emission = new(); public ShapeModule shape = new();
        public VelocityModule velocityOverLifetime = new(); public SizeModule sizeOverLifetime = new();
        public ColorModule colorOverLifetime = new(); public SheetModule textureSheetAnimation = new();
        public Transform transform = new(); public GameObject gameObject = new(); public ParticleSystemRenderer Renderer = new();
        public T GetComponent<T>() where T : class => Renderer as T;
        public struct MinMaxCurve
        {
            public float constantMax; public MinMaxCurve(float value) { constantMax = value; }
            public float Evaluate(float time, float random) => constantMax;
            public static implicit operator MinMaxCurve(float value) => new(value);
        }
        public struct MinMaxGradient { public Color Evaluate(float time, float random) => Color.white; }
        public struct Burst
        {
            public float time, repeatInterval, probability; public int cycleCount; public MinMaxCurve count;
        }
        public class MainModule
        {
            public float duration = 1; public bool loop = true, startSize3D; public int maxParticles = 100;
            public MinMaxCurve startLifetime = 1, startSpeed = 0, startSize = 1, startSizeX = 1, startSizeY = 1, startSizeZ = 1, gravityModifier = 0;
            public MinMaxGradient startColor;
        }
        public class EmissionModule
        {
            public bool enabled = true; public MinMaxCurve rateOverTime;
            public Burst[] Bursts = Array.Empty<Burst>(); public int burstCount => Bursts.Length;
            public void GetBursts(Burst[] output) => Bursts.CopyTo(output, 0);
        }
        public class ShapeModule { public bool enabled; public float radius; }
        public class VelocityModule { public bool enabled; public MinMaxCurve x,y,z; }
        public class SizeModule { public bool enabled; public MinMaxCurve size; }
        public class ColorModule { public bool enabled; public MinMaxGradient color; }
        public class SheetModule { public bool enabled; public int numTilesX=1,numTilesY=1; public float fps; public ParticleSystemAnimationTimeMode timeMode; }
    }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero => new(); public static Vector3 up => new(0,1,0); public static Vector3 forward => new(0,0,1);
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b) => new(a.x*b,a.y*b,a.z*b);
        public static Vector3 Scale(Vector3 a,Vector3 b) => new(a.x*b.x,a.y*b.y,a.z*b.z);
    }
    public struct Quaternion
    {
        public bool Upright; public static Quaternion LookRotation(Vector3 direction, Vector3 up) => new() { Upright = direction.y == 0 };
    }
    public struct Matrix4x4
    {
        public Vector3 lossyScale; public Quaternion Rotation; public static Matrix4x4 identity => new() { lossyScale = new(1,1,1) };
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => identity;
        public Vector3 MultiplyPoint3x4(Vector3 p) => p;
        public static Matrix4x4 TRS(Vector3 p,Quaternion r,Vector3 s) => new() { lossyScale=s, Rotation=r };
    }
    public struct Color
    {
        public static Color white => new(); public static Color operator *(Color a,Color b) => a;
    }
    public class Mesh
    {
        public string name; public Vector3[] vertices; public Vector2[] uv; public Color[] colors; public int[] triangles;
        public void RecalculateBounds() { }
    }
    public static class Physics { public static Vector3 gravity => new(0,-9.81f,0); }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Max(float a,float b)=>Math.Max(a,b); public static int Max(int a,int b)=>Math.Max(a,b);
        public static int FloorToInt(float v)=>(int)MathF.Floor(v); public static int CeilToInt(float v)=>(int)MathF.Ceiling(v);
        public static float Cos(float v)=>MathF.Cos(v); public static float Sin(float v)=>MathF.Sin(v);
        public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b);
    }
}
