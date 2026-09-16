using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3 { public float x, z; public Vector3(float x, float y, float z) { this.x = x; this.z = z; } }
    public struct Quaternion { }
    public struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        public int Value;
        public bool Equals(Matrix4x4 other) => Value == other.Value;
        public override bool Equals(object value) => value is Matrix4x4 other && Equals(other);
        public override int GetHashCode() => Value;
    }
    public struct Bounds : IEquatable<Bounds>
    {
        public int Value;
        public bool Equals(Bounds other) => Value == other.Value;
        public override bool Equals(object value) => value is Bounds other && Equals(other);
        public override int GetHashCode() => Value;
    }
    public struct Color : IEquatable<Color>
    {
        public int Value;
        public bool Equals(Color other) => Value == other.Value;
        public override bool Equals(object value) => value is Color other && Equals(other);
        public override int GetHashCode() => Value;
    }
    public struct Color32 : IEquatable<Color32>
    {
        public int Value;
        public bool Equals(Color32 other) => Value == other.Value;
        public override bool Equals(object value) => value is Color32 other && Equals(other);
        public override int GetHashCode() => Value;
    }
    public sealed class Mesh { public Bounds bounds; }
    public sealed class Material { public bool enableInstancing; }
    public static class Mathf { public static int FloorToInt(float value) => (int)Math.Floor(value); }
}

public enum ShadowCastingMode { Off, On }
public interface IVirtualRenderBatchOwner { int BatchEntryCount { get; } void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex); }
public struct VirtualRenderBatchEntry { public VirtualRenderBatchKey BatchKey; public int MatrixIndex; }
public readonly struct VirtualRenderBatchKey : IEquatable<VirtualRenderBatchKey>
{
    public readonly UnityEngine.Material Material;
    public readonly bool UseSleepAwakeDarkTint;
    private readonly int itemId, cellX, cellZ;
    public VirtualRenderBatchKey(UnityEngine.Mesh mesh, UnityEngine.Material material, int layer, int submesh,
        ShadowCastingMode shadows, bool receive, bool uv, bool sleep, bool line, UnityEngine.Color32 color,
        int itemId, int cellX, int cellZ)
    { Material = material; UseSleepAwakeDarkTint = sleep; this.itemId = itemId; this.cellX = cellX; this.cellZ = cellZ; }
    public bool Equals(VirtualRenderBatchKey other) => ReferenceEquals(Material, other.Material)
        && itemId == other.itemId && cellX == other.cellX && cellZ == other.cellZ;
    public override bool Equals(object value) => value is VirtualRenderBatchKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Material, itemId, cellX, cellZ);
}

public sealed class VirtualRenderBatchCollection
{
    public int Adds, Removes, Updates;
    public void AddOwnedMatrix(IVirtualRenderBatchOwner owner, List<VirtualRenderBatchEntry> entries,
        VirtualRenderBatchKey key, UnityEngine.Matrix4x4 matrix)
    { entries.Add(new VirtualRenderBatchEntry { BatchKey = key, MatrixIndex = 0 }); Adds++; }
    public void RemoveOwnedEntries(List<VirtualRenderBatchEntry> entries)
    { if (entries.Count > 0) Removes++; entries.Clear(); }
    public bool TryUpdateOwnedMatrix(List<VirtualRenderBatchEntry> entries, int index,
        VirtualRenderBatchKey key, UnityEngine.Matrix4x4 matrix)
    { if (index >= entries.Count || !entries[index].BatchKey.Equals(key)) return false; Updates++; return true; }
}

public static class SleepAwakeDebugVisual
{
    public static UnityEngine.Color GetSleepingColor(UnityEngine.Material material) => default;
}

public sealed class PortableObject
{
    public bool Renderable = true;
    public int ItemId = 1, MatrixVersion;
    public float X, Z;
    public readonly UnityEngine.Mesh Mesh = new();
    public readonly UnityEngine.Material Material = new();
    public bool TryGetBatchRenderData(out int itemId, out UnityEngine.Mesh mesh,
        out UnityEngine.Material material, out UnityEngine.Matrix4x4 matrix,
        out UnityEngine.Vector3 position, out int layer, out ShadowCastingMode shadows,
        out bool receive, out bool sleep, out bool line, out UnityEngine.Color32 color)
    {
        itemId = ItemId; mesh = Mesh; material = Material;
        matrix = new UnityEngine.Matrix4x4 { Value = MatrixVersion };
        position = new UnityEngine.Vector3(X, 0, Z); layer = 0; shadows = ShadowCastingMode.On;
        receive = true; sleep = false; line = false; color = default;
        return Renderable;
    }
}

public sealed partial class PortableItemRenderer
{
    public void Flush() => RefreshDirtyPortableObjectBatches();
    public (int Registered, int Requests, int Dirty, int Reads, int Updates, int Rebuilds) Stats =>
        (portableObjectBatchCaches.Count, lastPortableObjectDirtyRequests, lastPortableObjectDirtyObjects,
            lastPortableObjectSnapshotReads, lastPortableObjectMatrixUpdates, lastPortableObjectBatchRebuilds);
    public VirtualRenderBatchCollection Batches => portableObjectBatches;
}

static class Checks
{
    static int assertions;
    static void Require(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    static void Main()
    {
        var renderer = new PortableItemRenderer();
        var objects = new PortableObject[100];
        for (int i = 0; i < objects.Length; i++) { objects[i] = new PortableObject { X = i * 9 }; renderer.Register(objects[i]); }
        renderer.Flush();
        Require(renderer.Stats == (100, 100, 100, 100, 0, 100), "initial registration must build each object once");

        PortableObject moved = objects[40];
        moved.MatrixVersion++;
        renderer.MarkDirty(moved); renderer.MarkDirty(moved); renderer.MarkDirty(moved);
        renderer.Flush();
        Require(renderer.Stats == (100, 3, 1, 1, 1, 0), "duplicate dirty requests must coalesce to one matrix update");

        moved.X += 9;
        moved.MatrixVersion++;
        renderer.MarkDirty(moved);
        renderer.Flush();
        Require(renderer.Stats.Reads == 1 && renderer.Stats.Rebuilds == 1, "cell/key change must rebuild only the changed object");

        moved.Renderable = false;
        renderer.MarkDirty(moved);
        renderer.Flush();
        Require(renderer.Stats.Registered == 99 && renderer.Stats.Reads == 1, "non-renderable object must leave the cache");

        renderer.Unregister(objects[10]);
        Require(renderer.Stats.Registered == 98, "unregister must remove one cache");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            objects[0].MatrixVersion++;
            renderer.MarkDirty(objects[0]);
            renderer.Flush();
        }
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "steady incremental updates must allocate zero bytes");
        Console.WriteLine($"PASS: {assertions} portable-item incremental batching checks; production orchestration, deterministic batch double.");
    }
}
