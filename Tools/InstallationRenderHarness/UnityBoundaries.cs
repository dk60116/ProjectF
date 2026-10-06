using ProjectF.MapObjects;
using UnityEngine;

public class MapObject : MonoBehaviour { }
public partial class InstallationObject : MapObject
{
    private MapObjectHandle runtimeMapObjectHandle;
    public MapObjectHandle RuntimeMapObjectHandle { get => runtimeMapObjectHandle; set => runtimeMapObjectHandle = value; }
    public ItemDefinition BoundItemDefinition;
    public readonly List<Renderer> Renderers = new();
    public void Add(Renderer renderer) { renderer.gameObject.Owner = this; Renderers.Add(renderer); transform.hierarchyCount++; }
    public void GetComponentsInChildren<T>(bool inactive, List<T> result) where T : class
    { result.Clear(); foreach (Renderer renderer in Renderers) if (renderer is T part) result.Add(part); }
    public static void CopyActiveInstances(List<InstallationObject> output) => output.Clear();
}
public class ConveyorBelt : InstallationObject { }
public class Pipe : InstallationObject { }
public class RobotArm : InstallationObject { public static void WakeAroundCoordinate(Vector2Int cell) { } }
public class Building : InstallationObject { }
public partial class Vehicle : InstallationObject { }
public class InputOutputModule : InstallationObject
{
    public int AnimationRefreshes;
    public bool SharedAnimation;
    public void RefreshWorkAnimatorRendering()
    {
        AnimationRefreshes++;
        SharedAnimation = ProjectF.Rendering.InstallationBatchRenderer.TrySetWorkAnimation(this, true, 1f);
    }
}
public class MiningMachine : InputOutputModule { }
public class ItemDefinition { public MapObjectArchetype MapObjectArchetype; }
public partial class TerrainGenerator { public static TerrainGenerator Active; public bool IsBenchmarkPlacementInProgress; }
public static class MapObjectTickManager { public static bool WaitingForWorldLoad; public static double CurrentSimulationTimeSeconds; }
public static class MapObjectTickProfiler
{
    public static Scope SampleLateUpdateCaller<T>() => default;
    public static Scope SampleNamed(string a, string b, string c) => default;
    public static void AddRuntimeCounter(string a, string b, int c) { }
    public static void AddRuntimeCounter(string a, string b, long c) { }
    public struct Scope : IDisposable { public void Dispose() { } }
}
public readonly struct VirtualRenderBatchKey
{
    public readonly Mesh Mesh;
    public readonly Material Material;
    public readonly int Layer, SubmeshIndex, ShadowCastingMode, BatchCellX, BatchCellZ;
    public readonly bool ReceiveShadows, InvertCulling;
    public readonly uint RenderingLayerMask;
    public VirtualRenderBatchKey(Mesh mesh, Material mat, int layer, int sub, int shadows, bool receive, bool scroll,
        int batchCellX = 0, int batchCellZ = 0, bool invertCulling = false, uint renderingLayerMask = 0)
    {
        Mesh = mesh; Material = mat; Layer = layer; SubmeshIndex = sub; ShadowCastingMode = shadows;
        ReceiveShadows = receive; BatchCellX = batchCellX; BatchCellZ = batchCellZ;
        InvertCulling = invertCulling; RenderingLayerMask = renderingLayerMask;
    }
}
public sealed class VirtualRenderBatchCollection : IDisposable
{
    public static Matrix4x4 LastMatrix;
    public static VirtualRenderBatchKey LastKey;
    public void ClearActiveMatrices() { }
    public void AddMatrix(VirtualRenderBatchKey key, Matrix4x4 matrix) { LastMatrix = matrix; LastKey = key; }
    public void RenderBatches(Camera camera) { }
    public void SuspendRendering() { }
    public void Dispose() { }
}
namespace ProjectF.Rendering
{
    public static class WorldVisualUpdateManager
    {
        public static readonly List<InstallationObject> Visible = new();
        public static void CopyVisibleInstallations(List<InstallationObject> result) { result.Clear(); result.AddRange(Visible); }
    }
}
namespace ProjectF.MapObjects
{
    public class MapObjectArchetype
    {
        public List<MapObjectVisualNodeDefinition> Nodes = new();
        public List<MapObjectAnimationClipDefinition> AnimationClips = new();
    }
    public struct MapObjectVisualNodeDefinition
    {
        public string AnimationPath; public int ParentIndex; public Vector3 LocalPosition, LocalScale; public Quaternion LocalRotation;
    }
    public struct MapObjectTransformCurveDefinition { public string Path, Property; public AnimationCurve Curve; }
    public struct MapObjectAnimationClipDefinition
    {
        public int ObjectReferenceCurveCount, TransformCurveCount;
        public float LengthSeconds; public bool Looping;
        public List<MapObjectTransformCurveDefinition> TransformCurves;
    }
}
namespace UnityEngine.Rendering { public enum ShaderPropertyType { Color, Vector, Float, Range, Texture, Int } }
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    public class Object { public string name; public static void Destroy(Object obj) { } public static void DestroyImmediate(Object obj) { } }
    public class Component : Object
    {
        public GameObject gameObject = new(); public Transform transform => gameObject.transform;
        public T GetComponentInParent<T>(bool inactive = false) where T : class => gameObject.Owner as T;
        public T GetComponent<T>() where T : class => this is MeshRenderer mr ? mr.Filter as T : null;
    }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled = true; }
    public class GameObject { public MapObject Owner; public bool activeInHierarchy = true; public int layer; public Transform transform = new(); }
    public class Transform
    { public string name = ""; public Transform parent; public int hierarchyCount = 1; public Matrix4x4 localToWorldMatrix = Matrix4x4.identity; }
    public class Renderer : Component
    {
        public static int Reads;
        public static int PropertyBlockReads;
        public bool enabled = true, forceRenderingOff, receiveShadows = true;
        public int shadowCastingMode; public uint renderingLayerMask;
        public Material[] Materials = Array.Empty<Material>();
        public readonly MaterialPropertyBlock Global = new(), Indexed = new();
        public void GetSharedMaterials(List<Material> result) { Reads++; result.Clear(); result.AddRange(Materials); }
        public bool HasPropertyBlock() => !Global.isEmpty || !Indexed.isEmpty;
        public void GetPropertyBlock(MaterialPropertyBlock target) { PropertyBlockReads++; target.Copy(Global); }
        public void GetPropertyBlock(MaterialPropertyBlock target, int sub) { PropertyBlockReads++; target.Copy(Indexed); }
    }
    public class MeshRenderer : Renderer { public MeshFilter Filter; }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public enum SpriteDrawMode { Simple, Sliced }
    public enum SpriteMaskInteraction { None, VisibleInsideMask }
    public class SpriteRenderer : Renderer
    { public Sprite sprite; public SpriteDrawMode drawMode; public SpriteMaskInteraction maskInteraction; public Color color = new(1,1,1,1); public bool flipX, flipY; }
    public class Sprite : Object { public Vector2[] vertices, uv; public ushort[] triangles; public Texture texture; }
    public class Mesh : Object { public int subMeshCount = 1; public Vector3[] vertices, normals; public Color32[] colors32; public Vector2[] uv; public int[] triangles; public void RecalculateBounds() { } }
    public class Texture : Object { }
    public class Shader : Object
    {
        public bool SupportsInstancing = true;
        public static int KeywordReads, PropertyLayoutReads;
        public KeywordSpace keywordSpace => new KeywordSpace { Supported = SupportsInstancing };
        public struct KeywordSpace { public bool Supported; public Keyword FindKeyword(string name) { KeywordReads++; return new Keyword { isValid = Supported }; } }
        public struct Keyword { public bool isValid; }
        static readonly Dictionary<string, int> ids = new(); readonly List<(int Id, Rendering.ShaderPropertyType Type)> props = new();
        public static int PropertyToID(string name) { if (!ids.TryGetValue(name, out int id)) ids[name] = id = ids.Count+1; return id; }
        public void Add(string name, Rendering.ShaderPropertyType type) => props.Add((PropertyToID(name),type));
        public int GetPropertyCount() { PropertyLayoutReads++; return props.Count; }
        public int GetPropertyNameId(int i) { PropertyLayoutReads++; return props[i].Id; }
        public Rendering.ShaderPropertyType GetPropertyType(int i) { PropertyLayoutReads++; return props[i].Type; }
    }
    public class Material : Object
    {
        readonly Dictionary<int, object> values = new(); public Shader shader; public bool enableInstancing;
        public Material() { } public Material(Material source) { shader = source.shader; foreach(var pair in source.values) values.Add(pair.Key,pair.Value); }
        public bool HasProperty(int id) => true;
        public void SetColor(int id, Color value) => values[id] = value;
        public void SetVector(int id, Vector4 value) => values[id] = value;
        public void SetFloat(int id, float value) => values[id] = value;
        public void SetInt(int id, int value) => values[id] = value;
        public void SetTexture(int id, Texture value) => values[id] = value;
        public Color GetColor(int id) => values.TryGetValue(id,out var value) && value is Color color ? color : new(1,1,1,1);
        public int GetInt(int id) => (int)values[id]; public Texture GetTexture(int id) => values.TryGetValue(id,out var value) ? value as Texture : null;
    }
    public class MaterialPropertyBlock
    {
        public static bool CreationAllowed = true;
        public MaterialPropertyBlock() { if (!CreationAllowed) throw new InvalidOperationException("Native API in MonoBehaviour constructor"); }
        readonly Dictionary<int, object> values = new(); public bool isEmpty => values.Count == 0;
        public void Clear() => values.Clear(); public bool HasProperty(int id) => values.ContainsKey(id);
        public void Copy(MaterialPropertyBlock source) { Clear(); foreach(var pair in source.values) values.Add(pair.Key,pair.Value); }
        public void SetColor(int id, Color value) => values[id] = value; public Color GetColor(int id) => (Color)values[id];
        public Vector4 GetVector(int id) => (Vector4)values[id]; public float GetFloat(int id) => (float)values[id];
        public void SetInt(int id, int value) => values[id] = value; public int GetInt(int id) => (int)values[id];
        public void SetTexture(int id, Texture value) => values[id] = value; public Texture GetTexture(int id) => values[id] as Texture;
    }
    public record struct Vector2(float x, float y);
    public record struct Vector3(float x, float y, float z)
    {
        public static Vector3 back => new(0,0,-1); public static implicit operator Vector3(Vector2 v) => new(v.x,v.y,0);
        public float this[int i] { get => i==0?x:i==1?y:z; set { if(i==0)x=value;else if(i==1)y=value;else z=value; } }
    }
    public record struct Vector4(float x, float y, float z, float w);
    public record struct Color(float r, float g, float b, float a)
    {
        public static implicit operator Vector4(Color c) => new(c.r,c.g,c.b,c.a);
        public static implicit operator Color(Vector4 v) => new(v.x,v.y,v.z,v.w);
        public static Color operator *(Color x, Color y) => new(x.r*y.r,x.g*y.g,x.b*y.b,x.a*y.a);
    }
    public record struct Color32(byte r, byte g, byte b, byte a);
    public struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        System.Numerics.Matrix4x4 data;
        public float m03 => data.M41; public float m13 => data.M42; public float m23 => data.M43;
        public float determinant => data.GetDeterminant();
        public bool Equals(Matrix4x4 other) => data.Equals(other.data);
        public static Matrix4x4 identity => new(){ data = System.Numerics.Matrix4x4.Identity };
        public static Matrix4x4 Scale(Vector3 v) => new(){data = System.Numerics.Matrix4x4.CreateScale(v.x,v.y,v.z)};
        public static Matrix4x4 TRS(Vector3 p, Quaternion r, Vector3 s) => new(){ data = System.Numerics.Matrix4x4.CreateScale(s.x,s.y,s.z)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(r.Data) * System.Numerics.Matrix4x4.CreateTranslation(p.x,p.y,p.z) };
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => new(){data = b.data*a.data};
    }
    public struct Quaternion
    {
        internal System.Numerics.Quaternion Data;
        public Quaternion normalized => new(){Data = System.Numerics.Quaternion.Normalize(Data)};
        public Vector3 eulerAngles => default;
        public static Quaternion identity => new(){Data=System.Numerics.Quaternion.Identity};
        public static Quaternion Euler(Vector3 v) => new(){Data=System.Numerics.Quaternion.CreateFromYawPitchRoll(v.y*MathF.PI/180,v.x*MathF.PI/180,v.z*MathF.PI/180)};
        public float this[int i]
        { get=>i==0?Data.X:i==1?Data.Y:i==2?Data.Z:Data.W; set {if(i==0)Data.X=value;else if(i==1)Data.Y=value;else if(i==2)Data.Z=value;else Data.W=value;} }
    }
    public class AnimationCurve
    {
        readonly float a,b; public AnimationCurve(float start,float end){a=start;b=end;}
        public float Evaluate(float time) => a+(b-a)*time;
    }
    public static class Mathf
    { public static int Min(int a,int b) => Math.Min(a,b); public static float Min(float a,float b)=>MathF.Min(a,b); public static float Max(float a,float b)=>MathF.Max(a,b); public static int FloorToInt(float f) => (int)Math.Floor(f); public static int RoundToInt(float f) => (int)MathF.Round(f); }
    public readonly record struct Vector2Int(int x, int y);
    public static class Application { public static bool isPlaying = true; }
    public class Camera { public static Camera main = new(); }
}
