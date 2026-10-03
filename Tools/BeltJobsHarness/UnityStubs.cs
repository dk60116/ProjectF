// The production kernel executes unchanged. These wrappers substitute engine allocation,
// scheduling and attributes only; the harness does not pretend to execute Burst machine code.
namespace Unity.Burst { [AttributeUsage(AttributeTargets.Struct)] public sealed class BurstCompileAttribute : Attribute { } }
namespace Unity.Jobs
{
    public interface IJobParallelFor { void Execute(int index); }
    public struct JobHandle { public void Complete() { } }
    public static class JobScheduling
    {
        public static bool UseParallel = true;
        public static JobHandle Schedule<T>(this T job, int count, int batch) where T : struct, IJobParallelFor
        {
            if (UseParallel) Parallel.For(0, count, job.Execute);
            else for (int i = 0; i < count; i++) job.Execute(i);
            return default;
        }
    }
}
namespace Unity.Collections.LowLevel.Unsafe
{ [AttributeUsage(AttributeTargets.Field)] public sealed class NativeDisableParallelForRestrictionAttribute : Attribute { } }
namespace Unity.Collections
{
    public enum Allocator { Persistent }
    public enum NativeArrayOptions { ClearMemory }
    [AttributeUsage(AttributeTargets.Field)] public sealed class ReadOnlyAttribute : Attribute { }
    public struct NativeArray<T> : IDisposable where T : struct
    {
        private T[] data;
        public NativeArray(int count, Allocator allocator, NativeArrayOptions options) { data = new T[count]; }
        public int Length => data.Length;
        public bool IsCreated => data != null;
        public T this[int i] { get => data[i]; set => data[i] = value; }
        public void Dispose() { data = null; }
    }
}
namespace Unity.Profiling
{
    public struct ProfilerMarker
    {
        public ProfilerMarker(string name) { }
        public Scope Auto() => default;
        public struct Scope : IDisposable { public void Dispose() { } }
    }
}
namespace UnityEngine
{
    public static class Application { public static bool isPlaying = true; }
    public static class Time { public static int frameCount; }
    public readonly record struct Vector2Int(int x, int y);
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
            => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        public static float Distance(Vector3 a, Vector3 b)
            => MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public void Encapsulate(Bounds other)
        {
            float minX = Math.Min(center.x - size.x / 2, other.center.x - other.size.x / 2);
            float maxX = Math.Max(center.x + size.x / 2, other.center.x + other.size.x / 2);
            float minZ = Math.Min(center.z - size.z / 2, other.center.z - other.size.z / 2);
            float maxZ = Math.Max(center.z + size.z / 2, other.center.z + other.size.z / 2);
            center.x = (minX + maxX) / 2; center.z = (minZ + maxZ) / 2;
            size.x = maxX - minX; size.z = maxZ - minZ;
        }
    }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp01(float x) => Math.Clamp(x, 0, 1);
        public static float Sin(float x) => MathF.Sin(x);
        public static float Cos(float x) => MathF.Cos(x);
        public const float PI = MathF.PI;
    }
}
public readonly record struct BlockHandle(UnityEngine.Vector2Int ChunkCoordinate);
namespace ProjectF.Rendering
{
    public sealed class CameraRenderCulling
    {
        public float MinimumX = float.NegativeInfinity, MaximumX = float.PositiveInfinity;
        public bool Intersects(UnityEngine.Bounds bounds) => bounds.center.x + bounds.size.x / 2 >= MinimumX
            && bounds.center.x - bounds.size.x / 2 <= MaximumX;
    }
}
