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
    public readonly bool HasConveyorMotion;

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
        bool invertCulling = false,
        bool hasConveyorMotion = false)
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
        HasConveyorMotion = hasConveyorMotion;
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
            && InvertCulling == other.InvertCulling
            && HasConveyorMotion == other.HasConveyorMotion;
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
            hash = (hash * 397) ^ (HasConveyorMotion ? 1 : 0);
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
    private static readonly int ConveyorMotionStartShaderId = Shader.PropertyToID("_ConveyorMotionStart");
    private static readonly int ConveyorMotionEndShaderId = Shader.PropertyToID("_ConveyorMotionEnd");

    private readonly Dictionary<VirtualRenderBatchKey, BatchRenderCache> batchesByKey = new Dictionary<VirtualRenderBatchKey, BatchRenderCache>();
    private readonly List<VirtualRenderBatchKey> activeBatchKeys = new List<VirtualRenderBatchKey>();
    private readonly List<Vector4> uvDrawScratch = new List<Vector4>(MaxInstancesPerDraw);
    private readonly List<Vector4> conveyorMotionStartDrawScratch = new List<Vector4>(MaxInstancesPerDraw);
    private readonly List<Vector4> conveyorMotionEndDrawScratch = new List<Vector4>(MaxInstancesPerDraw);
    private readonly ProjectF.Rendering.CameraRenderCulling cameraCulling = new ProjectF.Rendering.CameraRenderCulling();
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
        conveyorMotionStartDrawScratch.Clear();
        conveyorMotionEndDrawScratch.Clear();
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
                batchCache.ConveyorMotionStarts?.Clear();
                batchCache.ConveyorMotionEnds?.Clear();
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
        AddConveyorMotionData(batchCache, key, default);
        AddMatrixBounds(key, batchCache, matrix, default);
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
            ResolveDefaultUvData(),
            default);
    }

    public int ActiveConveyorMotionInstanceCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < activeBatchKeys.Count; i++)
            {
                VirtualRenderBatchKey key = activeBatchKeys[i];
                if (key.HasConveyorMotion
                    && batchesByKey.TryGetValue(key, out BatchRenderCache batchCache))
                {
                    total += batchCache.Matrices.Count;
                }
            }

            return total;
        }
    }

    public void AddOwnedMatrix(
        IVirtualRenderBatchOwner owner,
        List<VirtualRenderBatchEntry> ownerEntries,
        VirtualRenderBatchKey key,
        Matrix4x4 matrix,
        ConveyorItemGpuMotionData conveyorMotion)
    {
        AddOwnedMatrix(
            owner,
            ownerEntries,
            key,
            matrix,
            ResolveDefaultUvData(),
            conveyorMotion);
    }

    public void AddOwnedMatrix(
        IVirtualRenderBatchOwner owner,
        List<VirtualRenderBatchEntry> ownerEntries,
        VirtualRenderBatchKey key,
        Matrix4x4 matrix,
        Vector4 instanceUvData)
    {
        AddOwnedMatrix(
            owner,
            ownerEntries,
            key,
            matrix,
            instanceUvData,
            default);
    }

    private void AddOwnedMatrix(
        IVirtualRenderBatchOwner owner,
        List<VirtualRenderBatchEntry> ownerEntries,
        VirtualRenderBatchKey key,
        Matrix4x4 matrix,
        Vector4 instanceUvData,
        ConveyorItemGpuMotionData conveyorMotion)
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
        AddConveyorMotionData(batchCache, key, conveyorMotion);
        batchCache.Owners.Add(new MatrixOwner(owner, entryIndex));
        AddMatrixBounds(key, batchCache, matrix, conveyorMotion);
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

    public void RenderBatches(Camera renderCamera = null)
    {
        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }

        VirtualRenderBatchRendererGroupBackend backend = ResolveBatchRendererGroupBackend();
        cameraCulling.Update(renderCamera);
        if (backend != null)
            backend.DisableCameraCulling = ProjectF.Rendering.CameraRenderCulling.Disabled;
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
            // Skip CPU uploads too, not only the final draw submission.
            if (key.ShadowCastingMode == ShadowCastingMode.Off
                && (!cameraCulling.IsLayerVisible(key.Layer) || !cameraCulling.Intersects(worldBounds)))
            {
                backend?.Deactivate(key, keepAllocated: true);
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
            if (!cameraCulling.IsLayerVisible(key.Layer) || !cameraCulling.Intersects(worldBounds))
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
            if (entry.BatchKey.HasConveyorMotion)
            {
                batchCache.ConveyorMotionStarts[matrixIndex] = batchCache.ConveyorMotionStarts[lastIndex];
                batchCache.ConveyorMotionEnds[matrixIndex] = batchCache.ConveyorMotionEnds[lastIndex];
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
        if (entry.BatchKey.HasConveyorMotion)
        {
            batchCache.ConveyorMotionStarts.RemoveAt(lastIndex);
            batchCache.ConveyorMotionEnds.RemoveAt(lastIndex);
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
        if (!key.HasUvScroll
            && !key.HasConveyorMotion
            && !key.UseSleepAwakeDarkTint
            && !key.UseBeltItemLineDebugColor)
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

        if (key.HasConveyorMotion)
        {
            CopyVectorDrawRange(
                batchCache.ConveyorMotionStarts,
                conveyorMotionStartDrawScratch,
                startIndex,
                instanceCount);
            CopyVectorDrawRange(
                batchCache.ConveyorMotionEnds,
                conveyorMotionEndDrawScratch,
                startIndex,
                instanceCount);
            batchCache.PropertyBlock.SetVectorArray(
                ConveyorMotionStartShaderId,
                conveyorMotionStartDrawScratch);
            batchCache.PropertyBlock.SetVectorArray(
                ConveyorMotionEndShaderId,
                conveyorMotionEndDrawScratch);
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

    private static void AddConveyorMotionData(
        BatchRenderCache batchCache,
        VirtualRenderBatchKey key,
        ConveyorItemGpuMotionData conveyorMotion)
    {
        if (!key.HasConveyorMotion)
        {
            return;
        }

        batchCache.ConveyorMotionStarts ??= new List<Vector4>(64);
        batchCache.ConveyorMotionEnds ??= new List<Vector4>(64);
        batchCache.ConveyorMotionStarts.Add(conveyorMotion.Start);
        batchCache.ConveyorMotionEnds.Add(conveyorMotion.End);
    }

    private static void CopyVectorDrawRange(
        List<Vector4> source,
        List<Vector4> destination,
        int startIndex,
        int instanceCount)
    {
        destination.Clear();
        int endIndex = Mathf.Min(
            source != null ? source.Count : 0,
            startIndex + instanceCount);
        for (int i = Mathf.Max(0, startIndex); i < endIndex; i++)
        {
            destination.Add(source[i]);
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
            ConveyorItemGpuMotionData conveyorMotion = default;
            if (key.HasConveyorMotion
                && batchCache.ConveyorMotionStarts != null
                && batchCache.ConveyorMotionEnds != null
                && i < batchCache.ConveyorMotionStarts.Count
                && i < batchCache.ConveyorMotionEnds.Count)
            {
                conveyorMotion = new ConveyorItemGpuMotionData(
                    batchCache.ConveyorMotionStarts[i],
                    batchCache.ConveyorMotionEnds[i]);
            }

            AddMatrixBounds(key, batchCache, batchCache.Matrices[i], conveyorMotion);
        }
    }

    private static void AddMatrixBounds(
        VirtualRenderBatchKey key,
        BatchRenderCache batchCache,
        Matrix4x4 matrix,
        ConveyorItemGpuMotionData conveyorMotion)
    {
        if (batchCache.BoundsDirty)
        {
            return;
        }

        Bounds bounds = CalculateWorldBounds(key.Mesh, matrix);
        if (key.HasConveyorMotion && conveyorMotion.IsActive)
        {
            Vector3 motionDelta = conveyorMotion.EndWorldPosition - conveyorMotion.StartWorldPosition;
            bounds.Encapsulate(new Bounds(bounds.center + motionDelta, bounds.size));
        }
        batchCache.EncapsulateBounds(bounds);
    }

    internal static Bounds CalculateWorldBounds(Mesh mesh, Matrix4x4 matrix)
    {
        Bounds localBounds = mesh != null
            ? mesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);
        return CalculateWorldBounds(localBounds, matrix);
    }

    internal static Bounds CalculateWorldBounds(Bounds localBounds, Matrix4x4 matrix)
    {
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

    private sealed class BatchRenderCache
    {
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>(64);
        public List<Vector4> InstanceUvData;
        public List<Vector4> ConveyorMotionStarts;
        public List<Vector4> ConveyorMotionEnds;
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
