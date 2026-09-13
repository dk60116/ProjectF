using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
[DefaultExecutionOrder(1000)]
public class ResourceBatchRenderer : MonoBehaviour
{
    private const int MaxInstancesPerDraw = 1023;
    private const int MaxPendingResourceAddsPerFrame = 128;
    private const int MaxDirtyResourceUpdatesPerFrame = 256;
    private const float GlobalBatchCellSizeMultiplier = 4f;
    private static readonly ProfilerMarker ApplyPendingAddsMarker =
        new ProfilerMarker("ResourceBatchRenderer.ApplyPendingAdds");
    private static readonly ProfilerMarker ApplyDirtyResourcesMarker =
        new ProfilerMarker("ResourceBatchRenderer.ApplyDirtyResources");
    private static readonly ProfilerMarker RenderBatchesMarker =
        new ProfilerMarker("ResourceBatchRenderer.RenderBatches");

    [SerializeField, Min(1f)]
    private float batchCellSize = 8f;

    private readonly HashSet<ResourceInstance> registeredResources = new HashSet<ResourceInstance>();
    private readonly Queue<ResourceInstance> pendingResourceAdds = new Queue<ResourceInstance>();
    private readonly HashSet<ResourceInstance> pendingResourceAddSet = new HashSet<ResourceInstance>();
    private readonly Queue<ResourceInstance> dirtyResources = new Queue<ResourceInstance>();
    private readonly HashSet<ResourceInstance> dirtyResourceSet = new HashSet<ResourceInstance>();
    private readonly HashSet<ResourceInstance> batchedResources = new HashSet<ResourceInstance>();
    private readonly Dictionary<ResourceInstance, List<ResourceBatchEntry>> entriesByResource =
        new Dictionary<ResourceInstance, List<ResourceBatchEntry>>();
    private readonly Dictionary<BatchKey, BatchData> batchesByKey = new Dictionary<BatchKey, BatchData>();
    private readonly List<BatchKey> activeBatchKeys = new List<BatchKey>();
    private readonly ProjectF.Rendering.CameraRenderCulling cameraCulling = new ProjectF.Rendering.CameraRenderCulling();
    private Camera mainCamera;
    private int activeMatrixCount;

    public int RegisteredResourceCount => registeredResources.Count;
    public int ActiveBatchCount => activeBatchKeys.Count;
    public int ActiveMatrixCount => activeMatrixCount;
    public int PendingAddCount => pendingResourceAddSet.Count;
    public int DirtyResourceCount => dirtyResourceSet.Count;
    public int LastPendingAdds { get; private set; }
    public int LastDirtyResourceUpdates { get; private set; }
    public int LastVisibleBatchCount { get; private set; }
    public int LastCulledBatchCount { get; private set; }
    public int LastSubmittedMatrixCount { get; private set; }
    public int LastDrawCallCount { get; private set; }

    public void Register(ResourceInstance resource)
    {
        if (resource == null)
        {
            return;
        }

        if (registeredResources.Add(resource))
        {
            QueueResourceAdd(resource);
        }
    }

    public void Unregister(ResourceInstance resource)
    {
        if (resource == null)
        {
            return;
        }

        if (registeredResources.Remove(resource))
        {
            pendingResourceAddSet.Remove(resource);
            if (batchedResources.Contains(resource))
            {
                QueueDirtyResource(resource);
            }
        }
    }

    public void EnsureCapacity(int requestedCapacity)
    {
        int capacity = Mathf.Max(registeredResources.Count, requestedCapacity);
        registeredResources.EnsureCapacity(capacity);
        pendingResourceAddSet.EnsureCapacity(capacity);
        dirtyResourceSet.EnsureCapacity(capacity);
        batchedResources.EnsureCapacity(capacity);
        entriesByResource.EnsureCapacity(capacity);
    }

    public void MarkDirty(ResourceInstance resource)
    {
        if (resource == null || !registeredResources.Contains(resource))
        {
            return;
        }

        if (pendingResourceAddSet.Contains(resource))
        {
            return;
        }

        if (batchedResources.Contains(resource))
        {
            QueueDirtyResource(resource);
            return;
        }

        QueueResourceAdd(resource);
    }

    protected void LateUpdate()
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Render", "Resource Render", "Resource Render (inclusive)");
        LastDirtyResourceUpdates = 0;
        LastPendingAdds = 0;
        LastVisibleBatchCount = 0;
        LastCulledBatchCount = 0;
        LastSubmittedMatrixCount = 0;
        LastDrawCallCount = 0;
        if (registeredResources.Count <= 0)
        {
            if (activeBatchKeys.Count > 0)
            {
                ClearActiveBatches();
            }

            pendingResourceAdds.Clear();
            pendingResourceAddSet.Clear();
            dirtyResources.Clear();
            dirtyResourceSet.Clear();
            batchedResources.Clear();
            return;
        }

        if (dirtyResources.Count > 0)
        {
            using (ApplyDirtyResourcesMarker.Auto())
            using (MapObjectTickProfiler.SampleNamed(
                       "Render",
                       "Resource Render",
                       "Resource Incremental Update"))
            {
                LastDirtyResourceUpdates = ApplyDirtyResourceUpdates(MaxDirtyResourceUpdatesPerFrame);
            }
        }

        if (pendingResourceAdds.Count > 0)
        {
            using (ApplyPendingAddsMarker.Auto())
            using (MapObjectTickProfiler.SampleNamed(
                       "Render",
                       "Resource Render",
                       "Resource Pending Adds"))
            {
                LastPendingAdds = ApplyPendingResourceAdds(MaxPendingResourceAddsPerFrame);
            }
        }

        using (RenderBatchesMarker.Auto())
        using (MapObjectTickProfiler.SampleNamed(
                   "Render",
                   "Resource Render",
                   "Resource Submit"))
        {
            RenderBatches();
        }
    }

    private void QueueResourceAdd(ResourceInstance resource)
    {
        if (resource != null && pendingResourceAddSet.Add(resource))
        {
            pendingResourceAdds.Enqueue(resource);
        }
    }

    private void QueueDirtyResource(ResourceInstance resource)
    {
        if (resource != null && dirtyResourceSet.Add(resource))
        {
            dirtyResources.Enqueue(resource);
        }
    }

    private void ClearActiveBatches()
    {
        batchesByKey.Clear();
        entriesByResource.Clear();
        activeBatchKeys.Clear();
        activeMatrixCount = 0;
    }

    private int ApplyDirtyResourceUpdates(int budget)
    {
        int processed = 0;
        int normalizedBudget = Mathf.Max(1, budget);
        while (processed < normalizedBudget && dirtyResources.Count > 0)
        {
            ResourceInstance resource = dirtyResources.Dequeue();
            if (!dirtyResourceSet.Remove(resource))
            {
                continue;
            }

            processed++;
            bool remainsRegistered = resource != null && registeredResources.Contains(resource);
            RemoveResourceFromBatches(resource, remainsRegistered);
            if (!remainsRegistered)
            {
                registeredResources.Remove(resource);
                batchedResources.Remove(resource);
                continue;
            }

            AddResourceToBatches(resource);
            batchedResources.Add(resource);
        }

        return processed;
    }

    private int ApplyPendingResourceAdds(int budget)
    {
        int normalizedBudget = Mathf.Max(1, budget);
        int processed = 0;
        int added = 0;
        while (processed < normalizedBudget && pendingResourceAdds.Count > 0)
        {
            ResourceInstance resource = pendingResourceAdds.Dequeue();
            processed++;
            if (!pendingResourceAddSet.Remove(resource))
            {
                continue;
            }

            if (resource == null)
            {
                registeredResources.Remove(resource);
                continue;
            }

            if (!registeredResources.Contains(resource) || batchedResources.Contains(resource))
            {
                continue;
            }

            AddResourceToBatches(resource);
            batchedResources.Add(resource);
            added++;
        }

        return added;
    }

    private void AddResourceToBatches(ResourceInstance resource)
    {
        if (!entriesByResource.TryGetValue(resource, out List<ResourceBatchEntry> resourceEntries))
        {
            resourceEntries = new List<ResourceBatchEntry>(4);
            entriesByResource.Add(resource, resourceEntries);
        }
        else
        {
            resourceEntries.Clear();
        }

        int entryCount = resource.BatchRenderEntryCount;
        for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            if (!resource.TryGetBatchRenderData(
                    entryIndex,
                    out Mesh mesh,
                    out Material[] materials,
                    out Matrix4x4 localToWorldMatrix,
                    out Vector3 worldPosition,
                    out int layer,
                    out ShadowCastingMode shadowCastingMode,
                    out bool receiveShadows,
                    out bool useGlobalBatch))
            {
                continue;
            }

            int materialCount = materials != null ? materials.Length : 0;
            if (mesh == null || materialCount <= 0)
            {
                continue;
            }

            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            int renderPassCount = Mathf.Max(subMeshCount, materialCount);
            for (int passIndex = 0; passIndex < renderPassCount; passIndex++)
            {
                int materialIndex = Mathf.Min(passIndex, materialCount - 1);
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                if (!material.enableInstancing)
                {
                    material.enableInstancing = true;
                }

                int subMeshIndex = Mathf.Min(passIndex, subMeshCount - 1);
                AddBatchMatrix(
                    resource,
                    resourceEntries,
                    mesh,
                    material,
                    subMeshIndex,
                    localToWorldMatrix,
                    worldPosition,
                    layer,
                    shadowCastingMode,
                    receiveShadows,
                    useGlobalBatch);
            }
        }
    }

    private void AddBatchMatrix(
        ResourceInstance resource,
        List<ResourceBatchEntry> resourceEntries,
        Mesh mesh,
        Material material,
        int subMeshIndex,
        Matrix4x4 localToWorldMatrix,
        Vector3 worldPosition,
        int layer,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows,
        bool useGlobalBatch)
    {
        if (resource == null
            || resourceEntries == null
            || mesh == null
            || material == null
            || subMeshIndex < 0)
        {
            return;
        }

        float effectiveBatchCellSize = ResolveBatchCellSize(useGlobalBatch);
        int cellX = Mathf.FloorToInt(worldPosition.x / effectiveBatchCellSize);
        int cellZ = Mathf.FloorToInt(worldPosition.z / effectiveBatchCellSize);
        BatchKey key = new BatchKey(
            mesh,
            material,
            subMeshIndex,
            layer,
            shadowCastingMode,
            receiveShadows,
            cellX,
            cellZ,
            useGlobalBatch);
        if (!batchesByKey.TryGetValue(key, out BatchData batch))
        {
            batch = new BatchData();
            batchesByKey.Add(key, batch);
            activeBatchKeys.Add(key);
        }

        Bounds matrixBounds = VirtualRenderBatchCollection.CalculateWorldBounds(mesh, localToWorldMatrix);
        if (!batch.HasBounds)
        {
            batch.WorldBounds = matrixBounds;
            batch.HasBounds = true;
        }
        else if (!batch.BoundsDirty)
        {
            batch.WorldBounds.Encapsulate(matrixBounds);
        }

        int resourceEntryIndex = resourceEntries.Count;
        int matrixIndex = batch.Matrices.Count;
        resourceEntries.Add(new ResourceBatchEntry(key, matrixIndex));
        batch.Matrices.Add(localToWorldMatrix);
        batch.Owners.Add(new BatchMatrixOwner(resource, resourceEntryIndex));
        batch.MarkDataDirty();
        activeMatrixCount++;
    }

    private void RemoveResourceFromBatches(ResourceInstance resource, bool retainEntryList)
    {
        if (ReferenceEquals(resource, null)
            || !entriesByResource.TryGetValue(resource, out List<ResourceBatchEntry> resourceEntries))
        {
            return;
        }

        for (int entryIndex = resourceEntries.Count - 1; entryIndex >= 0; entryIndex--)
        {
            ResourceBatchEntry entry = resourceEntries[entryIndex];
            if (!batchesByKey.TryGetValue(entry.Key, out BatchData batch))
            {
                continue;
            }

            int lastMatrixIndex = batch.Matrices.Count - 1;
            if (entry.MatrixIndex < 0 || entry.MatrixIndex > lastMatrixIndex)
            {
                continue;
            }

            if (entry.MatrixIndex != lastMatrixIndex)
            {
                BatchMatrixOwner movedOwner = batch.Owners[lastMatrixIndex];
                batch.Matrices[entry.MatrixIndex] = batch.Matrices[lastMatrixIndex];
                batch.Owners[entry.MatrixIndex] = movedOwner;
                if (entriesByResource.TryGetValue(
                        movedOwner.Resource,
                        out List<ResourceBatchEntry> movedEntries)
                    && movedOwner.ResourceEntryIndex >= 0
                    && movedOwner.ResourceEntryIndex < movedEntries.Count)
                {
                    ResourceBatchEntry movedEntry = movedEntries[movedOwner.ResourceEntryIndex];
                    movedEntry.MatrixIndex = entry.MatrixIndex;
                    movedEntries[movedOwner.ResourceEntryIndex] = movedEntry;
                }
            }

            batch.Matrices.RemoveAt(lastMatrixIndex);
            batch.Owners.RemoveAt(lastMatrixIndex);
            batch.BoundsDirty = true;
            batch.MarkDataDirty();
            activeMatrixCount--;
            if (batch.Matrices.Count <= 0)
            {
                batchesByKey.Remove(entry.Key);
                activeBatchKeys.Remove(entry.Key);
            }
        }

        resourceEntries.Clear();
        if (!retainEntryList)
        {
            entriesByResource.Remove(resource);
        }
    }

    private void RenderBatches()
    {
        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
            mainCamera = Camera.main;
        cameraCulling.Update(mainCamera);

        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            BatchKey key = activeBatchKeys[batchIndex];
            if (!batchesByKey.TryGetValue(key, out BatchData batch)
                || batch.Matrices.Count <= 0)
            {
                continue;
            }

            Bounds batchBounds = ResolveBatchBounds(key, batch);
            if (!cameraCulling.IsLayerVisible(key.Layer) || !cameraCulling.Intersects(batchBounds))
            {
                LastCulledBatchCount++;
                continue;
            }

            LastVisibleBatchCount++;
            if (!cameraCulling.Contains(batchBounds) && key.ShadowCastingMode != ShadowCastingMode.ShadowsOnly
                && key.ShadowCastingMode != ShadowCastingMode.TwoSided)
            {
                CameraBatch cache = ResolveCameraBatch(key, batch);
                DrawResourceBatch(key, cache.Visible, batchBounds, key.ShadowCastingMode);
                if (key.ShadowCastingMode == ShadowCastingMode.On)
                    DrawResourceBatch(key, cache.Hidden, batchBounds, ShadowCastingMode.ShadowsOnly);
                continue;
            }
            DrawResourceBatch(key, batch.Matrices, batchBounds, key.ShadowCastingMode);
        }
    }

    private static Bounds ResolveBatchBounds(BatchKey key, BatchData batch)
    {
        if (batch.HasBounds && !batch.BoundsDirty)
        {
            return batch.WorldBounds;
        }

        batch.HasBounds = false;
        batch.BoundsDirty = false;
        for (int i = 0; i < batch.Matrices.Count; i++)
        {
            Bounds matrixBounds =
                VirtualRenderBatchCollection.CalculateWorldBounds(key.Mesh, batch.Matrices[i]);
            if (!batch.HasBounds)
            {
                batch.WorldBounds = matrixBounds;
                batch.HasBounds = true;
            }
            else
            {
                batch.WorldBounds.Encapsulate(matrixBounds);
            }
        }

        return batch.WorldBounds;
    }

    private CameraBatch ResolveCameraBatch(BatchKey key, BatchData batch)
    {
        CameraBatch cache = batch.CameraBatch;
        if (cache.SourceDataVersion == batch.DataVersion
            && cache.CameraVersion == cameraCulling.Version)
        {
            return cache;
        }

        cache.SourceDataVersion = batch.DataVersion;
        cache.CameraVersion = cameraCulling.Version;
        cache.Visible.Clear();
        cache.Hidden.Clear();
        for (int i = 0; i < batch.Matrices.Count; i++)
        {
            Matrix4x4 matrix = batch.Matrices[i];
            Bounds bounds = VirtualRenderBatchCollection.CalculateWorldBounds(key.Mesh, matrix);
            (cameraCulling.Intersects(bounds) ? cache.Visible : cache.Hidden).Add(matrix);
        }
        return cache;
    }

    private void DrawResourceBatch(BatchKey key, List<Matrix4x4> matrices, Bounds batchBounds,
        ShadowCastingMode shadowCastingMode)
    {
        if (matrices == null || matrices.Count <= 0)
        {
            return;
        }

        RenderParams renderParams = new RenderParams(key.Material)
        {
            layer = key.Layer,
            shadowCastingMode = shadowCastingMode,
            receiveShadows = key.ReceiveShadows,
            worldBounds = batchBounds
        };

        int remaining = matrices.Count;
        int startIndex = 0;
        LastSubmittedMatrixCount += remaining;
        while (remaining > 0)
        {
            int drawCount = Mathf.Min(MaxInstancesPerDraw, remaining);
            Graphics.RenderMeshInstanced(renderParams, key.Mesh, key.SubMeshIndex, matrices, drawCount, startIndex);
            LastDrawCallCount++;
            startIndex += drawCount;
            remaining -= drawCount;
        }
    }

    private sealed class CameraBatch
    {
        public int SourceDataVersion = -1;
        public int CameraVersion = -1;
        public readonly List<Matrix4x4> Visible = new List<Matrix4x4>();
        public readonly List<Matrix4x4> Hidden = new List<Matrix4x4>();
    }

    private sealed class BatchData
    {
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>(16);
        public readonly List<BatchMatrixOwner> Owners = new List<BatchMatrixOwner>(16);
        public readonly CameraBatch CameraBatch = new CameraBatch();
        public Bounds WorldBounds;
        public bool HasBounds;
        public bool BoundsDirty;
        public int DataVersion;

        public void MarkDataDirty()
        {
            unchecked
            {
                DataVersion++;
            }
        }
    }

    private struct ResourceBatchEntry
    {
        public readonly BatchKey Key;
        public int MatrixIndex;

        public ResourceBatchEntry(BatchKey key, int matrixIndex)
        {
            Key = key;
            MatrixIndex = matrixIndex;
        }
    }

    private readonly struct BatchMatrixOwner
    {
        public readonly ResourceInstance Resource;
        public readonly int ResourceEntryIndex;

        public BatchMatrixOwner(ResourceInstance resource, int resourceEntryIndex)
        {
            Resource = resource;
            ResourceEntryIndex = resourceEntryIndex;
        }
    }

    private float ResolveBatchCellSize(bool useGlobalBatch)
    {
        float normalizedBatchCellSize = Mathf.Max(1f, batchCellSize);
        return useGlobalBatch
            ? normalizedBatchCellSize * GlobalBatchCellSizeMultiplier
            : normalizedBatchCellSize;
    }

    private readonly struct BatchKey
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly int SubMeshIndex;
        public readonly int Layer;
        public readonly ShadowCastingMode ShadowCastingMode;
        public readonly bool ReceiveShadows;
        public readonly int CellX;
        public readonly int CellZ;
        public readonly bool UseGlobalBatch;

        public BatchKey(
            Mesh mesh,
            Material material,
            int subMeshIndex,
            int layer,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows,
            int cellX,
            int cellZ,
            bool useGlobalBatch)
        {
            Mesh = mesh;
            Material = material;
            SubMeshIndex = subMeshIndex;
            Layer = layer;
            ShadowCastingMode = shadowCastingMode;
            ReceiveShadows = receiveShadows;
            CellX = cellX;
            CellZ = cellZ;
            UseGlobalBatch = useGlobalBatch;
        }

        public override int GetHashCode()
        {
            int hash = Mesh != null ? Mesh.GetInstanceID() : 0;
            hash = (hash * 397) ^ (Material != null ? Material.GetInstanceID() : 0);
            hash = (hash * 397) ^ SubMeshIndex;
            hash = (hash * 397) ^ Layer;
            hash = (hash * 397) ^ (int)ShadowCastingMode;
            hash = (hash * 397) ^ (ReceiveShadows ? 1 : 0);
            hash = (hash * 397) ^ CellX;
            hash = (hash * 397) ^ CellZ;
            hash = (hash * 397) ^ (UseGlobalBatch ? 1 : 0);
            return hash;
        }

        public override bool Equals(object obj)
        {
            return obj is BatchKey other && Equals(other);
        }

        private bool Equals(BatchKey other)
        {
            return Mesh == other.Mesh
                   && Material == other.Material
                   && SubMeshIndex == other.SubMeshIndex
                   && Layer == other.Layer
                   && ShadowCastingMode == other.ShadowCastingMode
                   && ReceiveShadows == other.ReceiveShadows
                   && CellX == other.CellX
                   && CellZ == other.CellZ
                   && UseGlobalBatch == other.UseGlobalBatch;
        }
    }
}
