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
    public readonly Mesh Mesh;
    public readonly Material Material;
    public readonly int Layer;
    public readonly int SubmeshIndex;
    public readonly ShadowCastingMode ShadowCastingMode;
    public readonly bool ReceiveShadows;
    public readonly bool HasUvScroll;
    public readonly bool UseSleepAwakeDarkTint;
    public readonly bool UseBeltItemLineDebugColor;
    public readonly Color32 BeltItemLineDebugColor;
    public readonly int BatchGroupId;
    public readonly int BatchCellX;
    public readonly int BatchCellZ;
    public readonly bool InvertCulling;

    public VirtualRenderBatchKey(
        Mesh mesh,
        Material material,
        int layer,
        int submeshIndex,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows,
        bool hasUvScroll,
        bool useSleepAwakeDarkTint = false,
        bool useBeltItemLineDebugColor = false,
        Color32 beltItemLineDebugColor = default,
        int batchGroupId = 0,
        int batchCellX = 0,
        int batchCellZ = 0,
        bool invertCulling = false)
    {
        Mesh = mesh;
        Material = material;
        Layer = layer;
        SubmeshIndex = submeshIndex;
        ShadowCastingMode = shadowCastingMode;
        ReceiveShadows = receiveShadows;
        HasUvScroll = hasUvScroll;
        UseSleepAwakeDarkTint = useSleepAwakeDarkTint;
        UseBeltItemLineDebugColor = useBeltItemLineDebugColor;
        BeltItemLineDebugColor = useBeltItemLineDebugColor ? beltItemLineDebugColor : (Color32)Color.white;
        BatchGroupId = batchGroupId;
        BatchCellX = batchCellX;
        BatchCellZ = batchCellZ;
        InvertCulling = invertCulling;
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
            && UseSleepAwakeDarkTint == other.UseSleepAwakeDarkTint
            && UseBeltItemLineDebugColor == other.UseBeltItemLineDebugColor
            && BeltItemLineDebugColor.Equals(other.BeltItemLineDebugColor)
            && BatchGroupId == other.BatchGroupId
            && BatchCellX == other.BatchCellX
            && BatchCellZ == other.BatchCellZ
            && InvertCulling == other.InvertCulling;
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
            hash = (hash * 397) ^ (UseSleepAwakeDarkTint ? 1 : 0);
            hash = (hash * 397) ^ (UseBeltItemLineDebugColor ? 1 : 0);
            hash = (hash * 397) ^ BeltItemLineDebugColor.GetHashCode();
            hash = (hash * 397) ^ BatchGroupId;
            hash = (hash * 397) ^ BatchCellX;
            hash = (hash * 397) ^ BatchCellZ;
            hash = (hash * 397) ^ (InvertCulling ? 1 : 0);
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
    private const float MinimumWorldBoundsSize = 0.25f;

    private static readonly int ConveyorUvDataShaderId = Shader.PropertyToID("_ConveyorUvData");

    private readonly Dictionary<VirtualRenderBatchKey, BatchRenderCache> batchesByKey = new Dictionary<VirtualRenderBatchKey, BatchRenderCache>();
    private readonly List<VirtualRenderBatchKey> activeBatchKeys = new List<VirtualRenderBatchKey>();
    private readonly List<Vector4> uvDrawScratch = new List<Vector4>(MaxInstancesPerDraw);
    private readonly Plane[] renderFrustumPlanes = new Plane[6];
    private VirtualRenderBatchRendererGroupBackend batchRendererGroupBackend;

    public int ActiveBatchCount => activeBatchKeys.Count;

    public int ActiveMatrixCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < activeBatchKeys.Count; i++)
            {
                if (batchesByKey.TryGetValue(activeBatchKeys[i], out BatchRenderCache batchCache))
                {
                    total += batchCache.Matrices.Count;
                }
            }

            return total;
        }
    }

    public int EstimatedDrawCallCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < activeBatchKeys.Count; i++)
            {
                if (!batchesByKey.TryGetValue(activeBatchKeys[i], out BatchRenderCache batchCache))
                {
                    continue;
                }

                int matrixCount = batchCache.Matrices.Count;
                if (matrixCount > 0)
                {
                    total += batchRendererGroupBackend != null
                        && batchRendererGroupBackend.IsRendering(activeBatchKeys[i])
                        ? 1
                        : Mathf.CeilToInt(matrixCount / (float)MaxInstancesPerDraw);
                }
            }

            return total;
        }
    }

    public int ActiveBatchRendererGroupBatchCount =>
        batchRendererGroupBackend != null
            ? batchRendererGroupBackend.ActiveBatchCount
            : 0;

    public void Clear()
    {
        DisposeBatchRendererGroupBackend();
        batchesByKey.Clear();
        activeBatchKeys.Clear();
        uvDrawScratch.Clear();
    }

    public void ClearActiveMatrices()
    {
        for (int i = 0; i < activeBatchKeys.Count; i++)
        {
            VirtualRenderBatchKey key = activeBatchKeys[i];
            if (batchesByKey.TryGetValue(key, out BatchRenderCache batchCache))
            {
                batchCache.Matrices.Clear();
                batchCache.InstanceUvData?.Clear();
                batchCache.Owners.Clear();
                batchCache.ClearBounds();
                batchCache.MarkDataDirty();
            }
        }

        activeBatchKeys.Clear();
        batchRendererGroupBackend?.DeactivateAll();
    }

    public void AddMatrix(VirtualRenderBatchKey key, Matrix4x4 matrix)
    {
        BatchRenderCache batchCache = GetOrCreateBatchCache(key, out bool created);
        if (!created && batchCache.Matrices.Count == 0)
        {
            activeBatchKeys.Add(key);
        }

        batchCache.Matrices.Add(matrix);
        AddInstanceUvData(batchCache, key, ResolveDefaultUvData());
        AddMatrixBounds(key, batchCache, matrix);
        batchCache.MarkDataDirty();
    }

    public void AddOwnedMatrix(
        IVirtualRenderBatchOwner owner,
        List<VirtualRenderBatchEntry> ownerEntries,
        VirtualRenderBatchKey key,
        Matrix4x4 matrix)
    {
        AddOwnedMatrix(
            owner,
            ownerEntries,
            key,
            matrix,
            ResolveDefaultUvData());
    }

    public void AddOwnedMatrix(
        IVirtualRenderBatchOwner owner,
        List<VirtualRenderBatchEntry> ownerEntries,
        VirtualRenderBatchKey key,
        Matrix4x4 matrix,
        Vector4 instanceUvData)
    {
        if (owner == null || ownerEntries == null)
        {
            return;
        }

        BatchRenderCache batchCache = GetOrCreateBatchCache(key, out bool created);
        if (!created && batchCache.Matrices.Count == 0)
        {
            activeBatchKeys.Add(key);
        }

        int entryIndex = ownerEntries.Count;
        int matrixIndex = batchCache.Matrices.Count;
        ownerEntries.Add(new VirtualRenderBatchEntry(key, matrixIndex));
        batchCache.Matrices.Add(matrix);
        AddInstanceUvData(batchCache, key, instanceUvData);
        batchCache.Owners.Add(new MatrixOwner(owner, entryIndex));
        AddMatrixBounds(key, batchCache, matrix);
        batchCache.MarkDataDirty();
    }

    public bool TryUpdateOwnedMatrix(
        List<VirtualRenderBatchEntry> ownerEntries,
        int entryIndex,
        VirtualRenderBatchKey key,
        Matrix4x4 matrix)
    {
        if (ownerEntries == null || entryIndex < 0 || entryIndex >= ownerEntries.Count)
        {
            return false;
        }

        VirtualRenderBatchEntry entry = ownerEntries[entryIndex];
        if (!entry.BatchKey.Equals(key)
            || !batchesByKey.TryGetValue(entry.BatchKey, out BatchRenderCache batchCache)
            || entry.MatrixIndex < 0
            || entry.MatrixIndex >= batchCache.Matrices.Count)
        {
            return false;
        }

        batchCache.Matrices[entry.MatrixIndex] = matrix;
        batchCache.MarkBoundsDirty();
        batchCache.MarkDataDirty();
        return true;
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

    public void RenderBatches(
        Camera renderCamera = null,
        TerrainGenerator playerRangeTerrain = null)
    {
        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }

        VirtualRenderBatchRendererGroupBackend backend = ResolveBatchRendererGroupBackend();
        backend?.BeginSync();

        bool hasLegacyBatches = false;
        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            VirtualRenderBatchKey key = activeBatchKeys[batchIndex];
            if (!batchesByKey.TryGetValue(key, out BatchRenderCache batchCache)
                || batchCache.Matrices.Count <= 0)
            {
                continue;
            }

            Bounds worldBounds = ResolveWorldBounds(key, batchCache);
            if (playerRangeTerrain != null
                && !playerRangeTerrain.DoesWorldBoundsIntersectPlayerRenderRange(worldBounds))
            {
                continue;
            }

            if (backend == null
                || !backend.TrySyncBatch(
                    key,
                    batchCache.Matrices,
                    batchCache.InstanceUvData,
                    worldBounds,
                    batchCache.DataVersion))
            {
                hasLegacyBatches = true;
            }
        }

        backend?.EndSync();
        if (!hasLegacyBatches)
        {
            return;
        }

        bool canFrustumCull = renderCamera != null;
        if (canFrustumCull)
        {
            GeometryUtility.CalculateFrustumPlanes(renderCamera, renderFrustumPlanes);
        }

        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            VirtualRenderBatchKey key = activeBatchKeys[batchIndex];
            if (!batchesByKey.TryGetValue(key, out BatchRenderCache batchCache) || batchCache.Matrices.Count <= 0)
            {
                continue;
            }

            if (backend != null && backend.IsRendering(key))
            {
                continue;
            }

            Bounds worldBounds = ResolveWorldBounds(key, batchCache);
            if (playerRangeTerrain != null
                && !playerRangeTerrain.DoesWorldBoundsIntersectPlayerRenderRange(worldBounds))
            {
                continue;
            }

            if (canFrustumCull
                && (!IsLayerVisibleToCamera(renderCamera, key.Layer)
                    || !GeometryUtility.TestPlanesAABB(renderFrustumPlanes, worldBounds)))
            {
                continue;
            }

            bool previousInvertCulling = GL.invertCulling;
            if (previousInvertCulling != key.InvertCulling)
            {
                GL.invertCulling = key.InvertCulling;
            }

            try
            {
                List<Matrix4x4> matrices = batchCache.Matrices;
                int remaining = matrices.Count;
                int startIndex = 0;
                while (remaining > 0)
                {
                    int drawCount = Mathf.Min(MaxInstancesPerDraw, remaining);
                    RenderParams renderParams = new RenderParams(key.Material)
                    {
                        layer = key.Layer,
                        shadowCastingMode = key.ShadowCastingMode,
                        receiveShadows = key.ReceiveShadows,
                        worldBounds = worldBounds,
                        matProps = ResolveBatchPropertyBlock(
                            key,
                            batchCache,
                            startIndex,
                            drawCount)
                    };
                    Graphics.RenderMeshInstanced(renderParams, key.Mesh, key.SubmeshIndex, matrices, drawCount, startIndex);
                    startIndex += drawCount;
                    remaining -= drawCount;
                }
            }
            finally
            {
                if (GL.invertCulling != previousInvertCulling)
                {
                    GL.invertCulling = previousInvertCulling;
                }
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
            if (entry.BatchKey.HasUvScroll)
            {
                batchCache.InstanceUvData[matrixIndex] = batchCache.InstanceUvData[lastIndex];
            }
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
        if (entry.BatchKey.HasUvScroll)
        {
            batchCache.InstanceUvData.RemoveAt(lastIndex);
        }
        batchCache.Owners.RemoveAt(lastIndex);
        batchCache.MarkDataDirty();
        if (batchCache.Matrices.Count == 0)
        {
            batchRendererGroupBackend?.Deactivate(entry.BatchKey);
            batchesByKey.Remove(entry.BatchKey);
            activeBatchKeys.Remove(entry.BatchKey);
        }
        else
        {
            batchCache.MarkBoundsDirty();
        }
    }

    public void Dispose()
    {
        DisposeBatchRendererGroupBackend();
    }

    public void SuspendRendering()
    {
        batchRendererGroupBackend?.DeactivateAll();
    }

    private VirtualRenderBatchRendererGroupBackend ResolveBatchRendererGroupBackend()
    {
        if (!VirtualRenderBatchRendererGroupBackend.IsSupported)
        {
            DisposeBatchRendererGroupBackend();
            return null;
        }

        if (batchRendererGroupBackend == null)
        {
            batchRendererGroupBackend = new VirtualRenderBatchRendererGroupBackend();
        }

        return batchRendererGroupBackend.IsAvailable
            ? batchRendererGroupBackend
            : null;
    }

    private void DisposeBatchRendererGroupBackend()
    {
        batchRendererGroupBackend?.Dispose();
        batchRendererGroupBackend = null;
    }

    private MaterialPropertyBlock ResolveBatchPropertyBlock(
        VirtualRenderBatchKey key,
        BatchRenderCache batchCache,
        int startIndex,
        int instanceCount)
    {
        if (!key.HasUvScroll && !key.UseSleepAwakeDarkTint && !key.UseBeltItemLineDebugColor)
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
            uvDrawScratch.Clear();
            int endIndex = Mathf.Min(
                batchCache.InstanceUvData != null ? batchCache.InstanceUvData.Count : 0,
                startIndex + instanceCount);
            for (int i = Mathf.Max(0, startIndex); i < endIndex; i++)
            {
                uvDrawScratch.Add(batchCache.InstanceUvData[i]);
            }

            batchCache.PropertyBlock.SetVectorArray(
                ConveyorUvDataShaderId,
                uvDrawScratch);
        }

        if (key.UseBeltItemLineDebugColor)
        {
            Color color = key.BeltItemLineDebugColor;
            if (key.UseSleepAwakeDarkTint)
            {
                color = SleepAwakeDebugVisual.Darken(color);
            }

            BeltItemLineDebugVisual.ApplySolidColor(batchCache.PropertyBlock, color);
        }
        else if (key.UseSleepAwakeDarkTint)
        {
            SleepAwakeDebugVisual.ApplySleepingColor(batchCache.PropertyBlock, key.Material);
        }

        return batchCache.PropertyBlock;
    }

    private static void AddInstanceUvData(
        BatchRenderCache batchCache,
        VirtualRenderBatchKey key,
        Vector4 instanceUvData)
    {
        if (key.HasUvScroll)
        {
            batchCache.InstanceUvData ??= new List<Vector4>(64);
            batchCache.InstanceUvData.Add(instanceUvData);
        }
    }

    private static Vector4 ResolveDefaultUvData()
    {
        return new Vector4(0f, -0.5f, 1f, 0f);
    }

    private Bounds ResolveWorldBounds(VirtualRenderBatchKey key, BatchRenderCache batchCache)
    {
        if (!batchCache.HasBounds || batchCache.BoundsDirty)
        {
            RebuildWorldBounds(key, batchCache);
        }

        return batchCache.WorldBounds;
    }

    private void RebuildWorldBounds(VirtualRenderBatchKey key, BatchRenderCache batchCache)
    {
        batchCache.ClearBounds();
        for (int i = 0; i < batchCache.Matrices.Count; i++)
        {
            AddMatrixBounds(key, batchCache, batchCache.Matrices[i]);
        }
    }

    private static void AddMatrixBounds(
        VirtualRenderBatchKey key,
        BatchRenderCache batchCache,
        Matrix4x4 matrix)
    {
        if (batchCache.BoundsDirty)
        {
            return;
        }

        Bounds bounds = CalculateWorldBounds(key.Mesh, matrix);
        batchCache.EncapsulateBounds(bounds);
    }

    private static Bounds CalculateWorldBounds(Mesh mesh, Matrix4x4 matrix)
    {
        Bounds localBounds = mesh != null
            ? mesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);

        Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
        Vector3 localExtents = localBounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(localExtents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, localExtents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, localExtents.z));
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

        Bounds worldBounds = new Bounds(center, worldExtents * 2f);
        if (worldBounds.size.sqrMagnitude < MinimumWorldBoundsSize * MinimumWorldBoundsSize)
        {
            worldBounds.Expand(MinimumWorldBoundsSize);
        }

        return worldBounds;
    }

    private static bool IsLayerVisibleToCamera(Camera camera, int layer)
    {
        return camera == null
            || layer < 0
            || layer > 31
            || (camera.cullingMask & (1 << layer)) != 0;
    }

    private sealed class BatchRenderCache
    {
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>(64);
        public List<Vector4> InstanceUvData;
        public readonly List<MatrixOwner> Owners = new List<MatrixOwner>(64);
        public MaterialPropertyBlock PropertyBlock;
        public Bounds WorldBounds;
        public bool HasBounds;
        public bool BoundsDirty;
        public int DataVersion;

        public void EncapsulateBounds(Bounds bounds)
        {
            if (!HasBounds)
            {
                WorldBounds = bounds;
                HasBounds = true;
                return;
            }

            WorldBounds.Encapsulate(bounds);
        }

        public void ClearBounds()
        {
            WorldBounds = default;
            HasBounds = false;
            BoundsDirty = false;
        }

        public void MarkBoundsDirty()
        {
            BoundsDirty = true;
        }

        public void MarkDataDirty()
        {
            unchecked
            {
                DataVersion++;
            }
        }
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
