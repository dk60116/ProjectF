using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class HarnessMetrics
{
    public static int RangeCopyCalls, VectorArrayCalls, UploadedVectors, ClearCalls;
    public static void Reset() { RangeCopyCalls = VectorArrayCalls = UploadedVectors = ClearCalls = 0; }
}
public class Mesh
{
    private static int nextId;
    private readonly int id = ++nextId;
    public Bounds bounds = new(Vector3.zero, Vector3.one);
    public int GetInstanceID() => id;
}
public class Material
{
    private static int nextId;
    private readonly int id = ++nextId;
    public readonly Dictionary<int, Color> Colors = new();
    public int GetInstanceID() => id;
    public bool HasProperty(int property) => Colors.ContainsKey(property);
    public Color GetColor(int property) => Colors[property];
}
public class Texture2D { public static Texture2D whiteTexture = new(); }
public static class Shader
{
    private static readonly Dictionary<string, int> Ids = new();
    public static float MotionTime;
    public static int PropertyToID(string name)
    {
        if (!Ids.TryGetValue(name, out int id)) { id = Ids.Count + 1; Ids.Add(name, id); }
        return id;
    }
}
public class MaterialPropertyBlock
{
    public static int CreatedCount;
    public MaterialPropertyBlock() => CreatedCount++;
    public readonly Dictionary<int, Vector4[]> Vectors = new();
    public readonly Dictionary<int, Color> Colors = new();
    public readonly Dictionary<int, Texture2D> Textures = new();
    public void Clear() { HarnessMetrics.ClearCalls++; Vectors.Clear(); Colors.Clear(); Textures.Clear(); }
    public void SetVectorArray(int id, List<Vector4> values)
    {
        HarnessMetrics.VectorArrayCalls++; HarnessMetrics.UploadedVectors += values.Count;
        Vectors[id] = values.ToArray();
    }
    public void SetColor(int id, Color color) => Colors[id] = color;
    public void SetTexture(int id, Texture2D texture) => Textures[id] = texture;
}
public class Camera { public static Camera main = new(); }
public static class GL { public static bool invertCulling; }
public struct RenderParams
{
    public Material material;
    public int layer;
    public ShadowCastingMode shadowCastingMode;
    public bool receiveShadows;
    public Bounds worldBounds;
    public MaterialPropertyBlock matProps;
    public RenderParams(Material value) { this = default; material = value; }
}
public sealed class Draw
{
    public Material Material;
    public Mesh Mesh;
    public int Layer, Submesh, Start, Count;
    public bool ReceiveShadows, Invert;
    public ShadowCastingMode Shadow;
    public Bounds Bounds;
    public float Time;
    public Matrix4x4[] Matrices;
    public readonly Dictionary<int, Vector4[]> Vectors = new();
    public readonly Dictionary<int, Color> Colors = new();
    public readonly Dictionary<int, Texture2D> Textures = new();
}
public static class Graphics
{
    public static bool Capture = true;
    public static readonly List<Draw> Draws = new();
    public static void RenderMeshInstanced(RenderParams p, Mesh mesh, int submesh, List<Matrix4x4> matrices, int count, int start)
    {
        if (!Capture) return;
        var draw = new Draw { Material = p.material, Mesh = mesh, Layer = p.layer, Shadow = p.shadowCastingMode,
            ReceiveShadows = p.receiveShadows, Invert = GL.invertCulling, Bounds = p.worldBounds,
            Submesh = submesh, Start = start, Count = count, Time = Shader.MotionTime, Matrices = matrices.GetRange(start, count).ToArray() };
        if (p.matProps != null)
        {
            foreach (var pair in p.matProps.Vectors) draw.Vectors[pair.Key] = (Vector4[])pair.Value.Clone();
            foreach (var pair in p.matProps.Colors) draw.Colors[pair.Key] = pair.Value;
            foreach (var pair in p.matProps.Textures) draw.Textures[pair.Key] = pair.Value;
        }
        Draws.Add(draw);
    }
}
namespace ProjectF.Rendering
{
    public class CameraRenderCulling
    {
        public static bool Disabled;
        public static bool Visible = true;
        public static int HiddenLayer = -1;
        public void Update(Camera camera) { }
        public bool IsLayerVisible(int layer) => Disabled || HiddenLayer != layer;
        public bool Intersects(Bounds bounds) => Disabled || Visible;
    }
}
public sealed class VirtualRenderBatchRendererGroupBackend
{
    public static bool IsSupported => false;
    public bool IsAvailable => false;
    public bool DisableCameraCulling;
    public int ActiveBatchCount => 0;
    public bool IsRendering(VirtualRenderBatchKey key) => false;
    public bool TrySyncBatch(VirtualRenderBatchKey key, List<Matrix4x4> matrices, List<Vector4> uv, Bounds bounds, int version) => false;
    public void BeginSync() { }
    public void EndSync() { }
    public void Deactivate(VirtualRenderBatchKey key, bool keepAllocated = false) { }
    public void DeactivateAll() { }
    public void Dispose() { }
}
public class Owner : IVirtualRenderBatchOwner
{
    public readonly List<VirtualRenderBatchEntry> Entries = new();
    public int BatchEntryCount => Entries.Count;
    public void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex)
    {
        var entry = Entries[entryIndex]; entry.MatrixIndex = matrixIndex; Entries[entryIndex] = entry;
    }
}
public sealed partial class VirtualRenderBatchCollection
{
    public int CachedDrawSlices(VirtualRenderBatchKey key) => batchesByKey.TryGetValue(key, out var cache) ? cache.DrawPropertyBlocks?.Count ?? 0 : 0;
    public void ChangeUv(VirtualRenderBatchKey key, int index, Vector4 value) { var cache = batchesByKey[key]; cache.InstanceUvData[index] = value; cache.MarkDataDirty(); }
    public void ChangeMotion(VirtualRenderBatchKey key, int index, ConveyorItemGpuMotionData motion)
    {
        var cache = batchesByKey[key]; cache.ConveyorMotionStarts[index] = motion.Start; cache.ConveyorMotionEnds[index] = motion.End; cache.MarkBoundsDirty(); cache.MarkDataDirty();
    }
}
public sealed partial class LegacyVirtualRenderBatchCollection
{
    public void ChangeUv(VirtualRenderBatchKey key, int index, Vector4 value) { var cache = batchesByKey[key]; cache.InstanceUvData[index] = value; cache.MarkDataDirty(); }
    public void ChangeMotion(VirtualRenderBatchKey key, int index, ConveyorItemGpuMotionData motion)
    {
        var cache = batchesByKey[key]; cache.ConveyorMotionStarts[index] = motion.Start; cache.ConveyorMotionEnds[index] = motion.End; cache.MarkBoundsDirty(); cache.MarkDataDirty();
    }
}
public static class Checks
{
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void EqualDraws(List<Draw> actual, List<Draw> expected, string label)
    {
        Require(actual.Count == expected.Count, label + ": draw count");
        for (int i = 0; i < actual.Count; i++)
        {
            Draw a = actual[i], b = expected[i];
            Require(a.Material == b.Material && a.Mesh == b.Mesh && a.Layer == b.Layer && a.Submesh == b.Submesh
                && a.Start == b.Start && a.Count == b.Count && a.ReceiveShadows == b.ReceiveShadows && a.Shadow == b.Shadow
                && a.Invert == b.Invert && a.Bounds.Equals(b.Bounds) && a.Time.Equals(b.Time), label + ": draw identity");
            for (int j = 0; j < a.Matrices.Length; j++)
                if (!a.Matrices[j].Equals(b.Matrices[j])) throw new Exception(label + ": matrix " + j);
            checks++;
            Require(a.Vectors.Count == b.Vectors.Count && a.Colors.Count == b.Colors.Count && a.Textures.Count == b.Textures.Count, label + ": property counts");
            foreach (var pair in a.Vectors)
            {
                Require(b.Vectors.TryGetValue(pair.Key, out var expectedVectors) && pair.Value.Length == expectedVectors.Length, label + ": vector length");
                for (int j = 0; j < pair.Value.Length; j++)
                    if (!pair.Value[j].Equals(expectedVectors[j])) throw new Exception(label + ": vector " + j);
                checks++;
            }
            foreach (var pair in a.Colors) Require(b.Colors.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value), label + ": color");
            foreach (var pair in a.Textures) Require(b.Textures.TryGetValue(pair.Key, out var value) && pair.Value == value, label + ": texture");
        }
    }
    public static void Main()
    {
        var optimized = new VirtualRenderBatchCollection(); var legacy = new LegacyVirtualRenderBatchCollection();
        var mesh = new Mesh(); var material = new Material();
        material.Colors[Shader.PropertyToID("_BaseColor")] = Color.white;
        var motionKey = new VirtualRenderBatchKey(mesh, material, 1, 0, ShadowCastingMode.Off, false, false, true, false, default, 0, 0, 0, false, true);
        var uvKey = new VirtualRenderBatchKey(mesh, material, 2, 0, ShadowCastingMode.Off, false, true);
        var colorKey = new VirtualRenderBatchKey(mesh, material, 3, 0, ShadowCastingMode.Off, false, false, true, true, new Color32(91, 35, 247, 255));
        var plainKey = new VirtualRenderBatchKey(mesh, material, 4, 0, ShadowCastingMode.Off, false, false);
        var owners = new List<(Owner A, Owner B)>();
        void AddMotion(int count, VirtualRenderBatchKey key)
        {
            for (int i = 0; i < count; i++)
            {
                var a = new Owner(); var b = new Owner(); int ordinal = owners.Count;
                var matrix = Matrix4x4.identity; matrix.m03 = ordinal * .01f;
                var motion = new ConveyorItemGpuMotionData(new Vector3(ordinal, 0, 0), new Vector3(ordinal + 1, 0, 0), ordinal * .1f, .3f);
                optimized.AddOwnedMatrix(a, a.Entries, key, matrix, motion); legacy.AddOwnedMatrix(b, b.Entries, key, matrix, motion);
                owners.Add((a, b));
            }
        }
        (int Calls, int Copies, int Vectors) Frame(string label, bool expectNoUploads = false)
        {
            HarnessMetrics.Reset(); Graphics.Draws.Clear(); optimized.RenderBatches();
            var actual = new List<Draw>(Graphics.Draws); var metrics = (HarnessMetrics.VectorArrayCalls, HarnessMetrics.RangeCopyCalls, HarnessMetrics.UploadedVectors);
            Graphics.Draws.Clear(); legacy.RenderBatches(); var expected = new List<Draw>(Graphics.Draws);
            EqualDraws(actual, expected, label);
            if (expectNoUploads) Require(metrics.Item1 == 0 && metrics.Item2 == 0 && metrics.Item3 == 0, label + ": unchanged arrays must not copy or upload");
            return metrics;
        }
        AddMotion(1, motionKey); Frame("one item"); Frame("one stable", true);
        AddMotion(1022, motionKey); Frame("1023 boundary"); Frame("1023 stable", true);
        AddMotion(1, motionKey); Frame("1024 boundary"); Frame("1024 stable", true);
        int boundaryPropertyBlocksBefore = MaterialPropertyBlock.CreatedCount;
        for (int i = 0; i < 40; i++)
        {
            var lastOwner = owners[^1];
            optimized.RemoveOwnedEntries(lastOwner.A.Entries); legacy.RemoveOwnedEntries(lastOwner.B.Entries);
            owners.RemoveAt(owners.Count - 1);
            Frame("1023 boundary return " + i);
            Require(optimized.CachedDrawSlices(motionKey) <= 2, "single draw retains at most one spare");
            AddMotion(1, motionKey); Frame("1024 boundary return " + i);
        }
        int boundaryPropertyBlocksCreated = MaterialPropertyBlock.CreatedCount - boundaryPropertyBlocksBefore;
        Require(boundaryPropertyBlocksCreated == 0, "1023/1024 oscillation reuses property blocks: " + boundaryPropertyBlocksCreated);
        AddMotion(1022, motionKey); Frame("2046 boundary");
        AddMotion(1, motionKey); Frame("2047 boundary"); Frame("2047 stable", true);
        Require(optimized.CachedDrawSlices(motionKey) == 3, "one cache for each of the three actual draws");
        int callsBefore = 0, copiesBefore = 0, callsAfter = 0, copiesAfter = 0;
        for (int i = 0; i < 120; i++)
        {
            Shader.MotionTime = i * .016f;
            var metrics = Frame("unchanged moving GPU items " + i, true); callsAfter += metrics.Calls; copiesAfter += metrics.Copies;
            callsBefore += HarnessMetrics.VectorArrayCalls; copiesBefore += HarnessMetrics.RangeCopyCalls;
        }
        Require(callsBefore == 720 && copiesBefore == 720 && callsAfter == 0 && copiesAfter == 0, "120 frames of 2047 items: 720 array calls/copies eliminated");
        Graphics.Capture = false;
        for (int i = 0; i < 5; i++) optimized.RenderBatches();
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++) optimized.RenderBatches();
        long allocations = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        Require(allocations == 0, "warm cache rendering must not allocate: " + allocations);
        Graphics.Capture = true;
        material.Colors[Shader.PropertyToID("_BaseColor")] = Color.red; Frame("same material changed sleeping color", true);
        material.Colors.Remove(Shader.PropertyToID("_BaseColor")); material.Colors[Shader.PropertyToID("_Color")] = Color.green;
        Frame("sleep color property fallback", true);
        var moved = Matrix4x4.identity; moved.m13 = .25f;
        Require(optimized.TryUpdateOwnedMatrix(owners[0].A.Entries, 0, motionKey, moved) == legacy.TryUpdateOwnedMatrix(owners[0].B.Entries, 0, motionKey, moved), "matrix mutation accepted equally");
        Frame("matrix dirty rebuild"); Frame("post matrix stable", true);
        var changedMotion = new ConveyorItemGpuMotionData(new Vector3(-2, 1, 4), new Vector3(3, 1, 4), 12f, .6f);
        optimized.ChangeMotion(motionKey, 1023, changedMotion); legacy.ChangeMotion(motionKey, 1023, changedMotion);
        Frame("start/end changed with same count"); Frame("motion stable", true);
        optimized.RemoveOwnedEntries(owners[17].A.Entries); legacy.RemoveOwnedEntries(owners[17].B.Entries);
        Frame("swap remove from first range into last");
        Require(optimized.CachedDrawSlices(motionKey) <= 3, "two draws retain at most one spare");
        Require(owners[^1].A.Entries[0].MatrixIndex == owners[^1].B.Entries[0].MatrixIndex, "swap updates the moved owner's matrix index");
        ProjectF.Rendering.CameraRenderCulling.Visible = false; Frame("camera outside", true);
        optimized.ChangeMotion(motionKey, 1023, new ConveyorItemGpuMotionData(Vector3.zero, Vector3.one, 50f, .1f));
        legacy.ChangeMotion(motionKey, 1023, new ConveyorItemGpuMotionData(Vector3.zero, Vector3.one, 50f, .1f));
        Frame("offscreen dirty", true);
        ProjectF.Rendering.CameraRenderCulling.Visible = true; Frame("camera returns"); Frame("visible cached", true);
        var uvOwnerA = new Owner(); var uvOwnerB = new Owner();
        for (int i = 0; i < 1030; i++)
        {
            var uv = new Vector4(i * .1f, -i * .2f, 1, i);
            optimized.AddOwnedMatrix(uvOwnerA, uvOwnerA.Entries, uvKey, Matrix4x4.identity, uv);
            legacy.AddOwnedMatrix(uvOwnerB, uvOwnerB.Entries, uvKey, Matrix4x4.identity, uv);
        }
        optimized.AddMatrix(colorKey, Matrix4x4.identity); legacy.AddMatrix(colorKey, Matrix4x4.identity);
        optimized.AddMatrix(plainKey, Matrix4x4.identity); legacy.AddMatrix(plainKey, Matrix4x4.identity);
        Frame("UV, debug color, no-properties batches"); Frame("mixed stable", true);
        Require(optimized.CachedDrawSlices(plainKey) == 0, "no-properties batch allocates no property cache");
        optimized.ChangeUv(uvKey, 1023, new Vector4(9, 8, 7, 6)); legacy.ChangeUv(uvKey, 1023, new Vector4(9, 8, 7, 6));
        Frame("UV changes after boundary"); Frame("UV stable", true);
        ProjectF.Rendering.CameraRenderCulling.HiddenLayer = 1; Frame("layer culling", true);
        ProjectF.Rendering.CameraRenderCulling.HiddenLayer = -1; Frame("layer restored", true);
        optimized.SuspendRendering(); legacy.SuspendRendering(); Frame("suspend and resume", true);
        optimized.ClearActiveMatrices(); legacy.ClearActiveMatrices(); owners.Clear(); Frame("clear active");
        AddMotion(7, motionKey); Frame("reuse cleared batch with fewer instances");
        Require(optimized.CachedDrawSlices(motionKey) <= 2, "reused batch trims to current draws plus one spare");
        Frame("reused batch stable", true);
        optimized.Clear(); legacy.Clear(); owners.Clear(); Frame("full clear");
        Require(optimized.CachedDrawSlices(motionKey) == 0, "full clear releases property ranges");
        var replacementMeshKey = new VirtualRenderBatchKey(new Mesh(), material, 1, 0, ShadowCastingMode.Off, false, false, false, false, default, 0, 0, 0, false, true);
        AddMotion(4, replacementMeshKey); Frame("mesh rebuild new key"); Frame("new mesh stable", true);
        for (int i = 0; i < owners.Count; i++) { optimized.RemoveOwnedEntries(owners[i].A.Entries); legacy.RemoveOwnedEntries(owners[i].B.Entries); }
        Frame("remove last owner"); Require(optimized.CachedDrawSlices(replacementMeshKey) == 0, "last owner removal releases batch cache");
        Console.WriteLine($"Conveyor property batch differential harness: {checks:N0} checks passed.");
        Console.WriteLine($"2047 GPU-motion items / 120 stable frames: SetVectorArray {callsBefore} -> {callsAfter}; range copies {copiesBefore} -> {copiesAfter}; warm-cache managed allocation {allocations} bytes.");
        Console.WriteLine($"1023/1024 oscillation / 40 cycles: {boundaryPropertyBlocksCreated} new MaterialPropertyBlock objects after warmup.");
    }
}
