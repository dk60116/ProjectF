using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public interface IVirtualRenderBatchOwner
{
    int BatchEntryCount { get; }
    void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex);
}

public readonly struct VirtualRenderBatchKey : System.IEquatable<VirtualRenderBatchKey>
{
    public const int UvScrollQuantize = 10000;

    public readonly Mesh Mesh;
    public readonly Material Material;
    public readonly int Layer;
    public readonly int SubmeshIndex;
    public readonly ShadowCastingMode ShadowCastingMode;
    public readonly bool ReceiveShadows;
    public readonly bool HasUvScroll;
    public readonly int UvScrollYTicks;
    public readonly bool UseSleepAwakeDarkTint;

    public VirtualRenderBatchKey(
        Mesh mesh,
        Material material,
        int layer,
        int submeshIndex,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows,
        bool hasUvScroll,
        int uvScrollYTicks,
        bool useSleepAwakeDarkTint = false)
    {
        Mesh = mesh;
        Material = material;
        Layer = layer;
        SubmeshIndex = submeshIndex;
        ShadowCastingMode = shadowCastingMode;
        ReceiveShadows = receiveShadows;
        HasUvScroll = hasUvScroll;
        UvScrollYTicks = hasUvScroll ? uvScrollYTicks : 0;
        UseSleepAwakeDarkTint = useSleepAwakeDarkTint;
    }

    public static int QuantizeUvScroll(float uvScrollY)
    {
        return Mathf.RoundToInt(uvScrollY * UvScrollQuantize);
    }

    public bool Equals(VirtualRenderBatchKey other)
    {
        return Mesh == other.Mesh
            && Material == other.Material
            && Layer == other.Layer
            && SubmeshIndex == other.SubmeshIndex
            && ShadowCastingMode == other.ShadowCastingMode
            && ReceiveShadows == other.ReceiveShadows
            && HasUvScroll == other.HasUvScroll
            && UvScrollYTicks == other.UvScrollYTicks
            && UseSleepAwakeDarkTint == other.UseSleepAwakeDarkTint;
    }

    public override bool Equals(object obj)
    {
        return obj is VirtualRenderBatchKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Mesh != null ? Mesh.GetInstanceID() : 0;
            hash = (hash * 397) ^ (Material != null ? Material.GetInstanceID() : 0);
            hash = (hash * 397) ^ Layer;
            hash = (hash * 397) ^ SubmeshIndex;
            hash = (hash * 397) ^ (int)ShadowCastingMode;
            hash = (hash * 397) ^ (ReceiveShadows ? 1 : 0);
            hash = (hash * 397) ^ (HasUvScroll ? 1 : 0);
            hash = (hash * 397) ^ UvScrollYTicks;
            hash = (hash * 397) ^ (UseSleepAwakeDarkTint ? 1 : 0);
            return hash;
        }
    }
}

public struct VirtualRenderBatchEntry
{
    public VirtualRenderBatchKey BatchKey;
    public int MatrixIndex;

    public VirtualRenderBatchEntry(VirtualRenderBatchKey batchKey, int matrixIndex)
    {
        BatchKey = batchKey;
        MatrixIndex = matrixIndex;
    }
}

public sealed class VirtualRenderBatchCollection
{
    private const int MaxInstancesPerDraw = 1023;

    private static readonly int UvScrollXShaderId = Shader.PropertyToID("_UVScrollX");
    private static readonly int UvScrollYShaderId = Shader.PropertyToID("_UVScrollY");

    private readonly Dictionary<VirtualRenderBatchKey, BatchRenderCache> batchesByKey = new Dictionary<VirtualRenderBatchKey, BatchRenderCache>();
    private readonly List<VirtualRenderBatchKey> activeBatchKeys = new List<VirtualRenderBatchKey>();

    public int ActiveBatchCount => activeBatchKeys.Count;

    public void Clear()
    {
        batchesByKey.Clear();
        activeBatchKeys.Clear();
    }

    public void ClearActiveMatrices()
    {
        for (int i = 0; i < activeBatchKeys.Count; i++)
        {
            VirtualRenderBatchKey key = activeBatchKeys[i];
            if (batchesByKey.TryGetValue(key, out BatchRenderCache batchCache))
            {
                batchCache.Matrices.Clear();
                batchCache.Owners.Clear();
            }
        }

        activeBatchKeys.Clear();
    }

    public void AddMatrix(VirtualRenderBatchKey key, Matrix4x4 matrix)
    {
        BatchRenderCache batchCache = GetOrCreateBatchCache(key, out bool created);
        if (!created && batchCache.Matrices.Count == 0)
        {
            activeBatchKeys.Add(key);
        }

        batchCache.Matrices.Add(matrix);
    }

    public void AddOwnedMatrix(
        IVirtualRenderBatchOwner owner,
        List<VirtualRenderBatchEntry> ownerEntries,
        VirtualRenderBatchKey key,
        Matrix4x4 matrix)
    {
        if (owner == null || ownerEntries == null)
        {
            return;
        }

        BatchRenderCache batchCache = GetOrCreateBatchCache(key, out _);
        int entryIndex = ownerEntries.Count;
        int matrixIndex = batchCache.Matrices.Count;
        ownerEntries.Add(new VirtualRenderBatchEntry(key, matrixIndex));
        batchCache.Matrices.Add(matrix);
        batchCache.Owners.Add(new MatrixOwner(owner, entryIndex));
    }

    public void RemoveOwnedEntries(List<VirtualRenderBatchEntry> ownerEntries)
    {
        if (ownerEntries == null)
        {
            return;
        }

        for (int i = ownerEntries.Count - 1; i >= 0; i--)
        {
            RemoveOwnedEntry(ownerEntries, i);
        }

        ownerEntries.Clear();
    }

    public void RenderBatches()
    {
        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            VirtualRenderBatchKey key = activeBatchKeys[batchIndex];
            if (!batchesByKey.TryGetValue(key, out BatchRenderCache batchCache) || batchCache.Matrices.Count <= 0)
            {
                continue;
            }

            RenderParams renderParams = new RenderParams(key.Material)
            {
                layer = key.Layer,
                shadowCastingMode = key.ShadowCastingMode,
                receiveShadows = key.ReceiveShadows,
                matProps = ResolveBatchPropertyBlock(key, batchCache)
            };

            List<Matrix4x4> matrices = batchCache.Matrices;
            int remaining = matrices.Count;
            int startIndex = 0;
            while (remaining > 0)
            {
                int drawCount = Mathf.Min(MaxInstancesPerDraw, remaining);
                Graphics.RenderMeshInstanced(renderParams, key.Mesh, key.SubmeshIndex, matrices, drawCount, startIndex);
                startIndex += drawCount;
                remaining -= drawCount;
            }
        }
    }

    private BatchRenderCache GetOrCreateBatchCache(VirtualRenderBatchKey key, out bool created)
    {
        if (!batchesByKey.TryGetValue(key, out BatchRenderCache batchCache))
        {
            batchCache = new BatchRenderCache();
            batchesByKey.Add(key, batchCache);
            activeBatchKeys.Add(key);
            created = true;
            return batchCache;
        }

        created = false;
        return batchCache;
    }

    private void RemoveOwnedEntry(List<VirtualRenderBatchEntry> ownerEntries, int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= ownerEntries.Count)
        {
            return;
        }

        VirtualRenderBatchEntry entry = ownerEntries[entryIndex];
        if (!batchesByKey.TryGetValue(entry.BatchKey, out BatchRenderCache batchCache))
        {
            return;
        }

        int lastIndex = batchCache.Matrices.Count - 1;
        int matrixIndex = entry.MatrixIndex;
        if (matrixIndex < 0 || matrixIndex > lastIndex)
        {
            return;
        }

        if (matrixIndex != lastIndex)
        {
            batchCache.Matrices[matrixIndex] = batchCache.Matrices[lastIndex];
            MatrixOwner movedOwner = batchCache.Owners[lastIndex];
            batchCache.Owners[matrixIndex] = movedOwner;
            if (movedOwner.Owner != null
                && movedOwner.EntryIndex >= 0
                && movedOwner.EntryIndex < movedOwner.Owner.BatchEntryCount)
            {
                movedOwner.Owner.UpdateBatchEntryMatrixIndex(movedOwner.EntryIndex, matrixIndex);
            }
        }

        batchCache.Matrices.RemoveAt(lastIndex);
        batchCache.Owners.RemoveAt(lastIndex);
        if (batchCache.Matrices.Count == 0)
        {
            batchesByKey.Remove(entry.BatchKey);
            activeBatchKeys.Remove(entry.BatchKey);
        }
    }

    private MaterialPropertyBlock ResolveBatchPropertyBlock(VirtualRenderBatchKey key, BatchRenderCache batchCache)
    {
        if (!key.HasUvScroll && !key.UseSleepAwakeDarkTint)
        {
            return null;
        }

        if (batchCache.PropertyBlock == null)
        {
            batchCache.PropertyBlock = new MaterialPropertyBlock();
        }

        batchCache.PropertyBlock.Clear();
        if (key.HasUvScroll)
        {
            batchCache.PropertyBlock.SetFloat(UvScrollXShaderId, 0f);
            batchCache.PropertyBlock.SetFloat(UvScrollYShaderId, key.UvScrollYTicks / (float)VirtualRenderBatchKey.UvScrollQuantize);
        }

        if (key.UseSleepAwakeDarkTint)
        {
            SleepAwakeDebugVisual.ApplySleepingColor(batchCache.PropertyBlock, key.Material);
        }

        return batchCache.PropertyBlock;
    }

    private sealed class BatchRenderCache
    {
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>(64);
        public readonly List<MatrixOwner> Owners = new List<MatrixOwner>(64);
        public MaterialPropertyBlock PropertyBlock;
    }

    private readonly struct MatrixOwner
    {
        public readonly IVirtualRenderBatchOwner Owner;
        public readonly int EntryIndex;

        public MatrixOwner(IVirtualRenderBatchOwner owner, int entryIndex)
        {
            Owner = owner;
            EntryIndex = entryIndex;
        }
    }
}
