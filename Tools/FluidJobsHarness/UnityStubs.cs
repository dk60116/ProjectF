namespace Unity.Burst
{
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class BurstCompileAttribute : Attribute { }
}

namespace Unity.Jobs
{
    public interface IJobParallelFor
    {
        void Execute(int index);
    }
}

namespace Unity.Collections
{
    public enum Allocator
    {
        Persistent
    }

    public enum NativeArrayOptions
    {
        ClearMemory
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReadOnlyAttribute : Attribute { }

    public struct NativeArray<T> : IDisposable where T : struct
    {
        private T[] data;

        public NativeArray(int count, Allocator allocator, NativeArrayOptions options)
        {
            data = new T[count];
        }

        public int Length => data.Length;
        public bool IsCreated => data != null;
        public T this[int index]
        {
            get => data[index];
            set => data[index] = value;
        }

        public void Dispose()
        {
            data = null;
        }
    }
}
