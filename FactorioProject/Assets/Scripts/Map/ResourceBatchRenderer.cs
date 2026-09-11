using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
[DefaultExecutionOrder(1000)]
public class ResourceBatchRenderer : MonoBehaviour
{
    private const int MaxInstancesPerDraw = 1023;
    private const int MaxPendingResourceAddsPerFrame = 128;
    private const float GlobalBatchCellSizeMultiplier = 4f;
    private static readonly ProfilerMarker ApplyPendingAddsMarker =
        new ProfilerMarker("ResourceBatchRenderer.ApplyPendingAdds");
    private static readonly ProfilerMarker RebuildBatchesMarker =
        new ProfilerMarker("ResourceBatchRenderer.RebuildBatches");
    private static readonly ProfilerMarker RenderBatchesMarker =
        new ProfilerMarker("ResourceBatchRenderer.RenderBatches");

    [SerializeField, Min(1f)]
    private float batchCellSize = 8f;

    private readonly HashSet<ResourceInstance> registeredResources = new HashSet<ResourceInstance>();
    private readonly Queue<ResourceInstance> pendingResourceAdds = new Queue<ResourceInstance>();
    private readonly HashSet<ResourceInstance> pendingResourceAddSet = new HashSet<ResourceInstance>();
    private readonly HashSet<ResourceInstance> batchedResources = new HashSet<ResourceInstance>();
    private readonly Dictionary<BatchKey, List<Matrix4x4>> matricesByBatch = new Dictionary<BatchKey, List<Matrix4x4>>();
    private readonly Dictionary<BatchKey, Bounds> boundsByBatch = new Dictionary<BatchKey, Bounds>();
    private readonly Dictionary<BatchKey, CameraBatch> cameraBatches = new Dictionary<BatchKey, CameraBatch>();
    private readonly List<BatchKey> activeBatchKeys = new List<BatchKey>();
    private readonly List<ResourceInstance> cleanupBuffer = new List<ResourceInstance>();
    private readonly ProjectF.Rendering.CameraRenderCulling cameraCulling = new ProjectF.Rendering.CameraRenderCulling();
    private bool batchesDirty;
    private Camera mainCamera;

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
            if (batchedResources.Remove(resource))
            {
                batchesDirty = true;
            }
        }
    }

    public void EnsureCapacity(int requestedCapacity)
    {
        int capacity = Mathf.Max(registeredResources.Count, requestedCapacity);
        registeredResources.EnsureCapacity(capacity);
        pendingResourceAddSet.EnsureCapacity(capacity);
        batchedResources.EnsureCapacity(capacity);
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
            batchesDirty = true;
            return;
        }

        QueueResourceAdd(resource);
    }

    protected void LateUpdate()
    {
        if (registeredResources.Count <= 0)
        {
            if (activeBatchKeys.Count > 0)
            {
                ClearActiveBatches();
            }

            pendingResourceAdds.Clear();
            pendingResourceAddSet.Clear();
            batchedResources.Clear();
            batchesDirty = false;
            return;
        }

        if (batchesDirty)
        {
            using (RebuildBatchesMarker.Auto())
            {
                RebuildBatches();
            }

            batchesDirty = false;
        }
        else if (pendingResourceAdds.Count > 0)
        {
            using (ApplyPendingAddsMarker.Auto())
            {
                ApplyPendingResourceAdds(MaxPendingResourceAddsPerFrame);
            }
        }

        using (RenderBatchesMarker.Auto())
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

    private void ClearActiveBatches()
    {
        foreach (CameraBatch cache in cameraBatches.Values) cache.SourceCount = -1;
        for (int i = 0; i < activeBatchKeys.Count; i++)
        {
            BatchKey key = activeBatchKeys[i];
            if (matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices))
            {
                matrices.Clear();
            }
        }

        activeBatchKeys.Clear();
        cleanupBuffer.Clear();
    }

    private void RebuildBatches()
    {
        ClearActiveBatches();
        pendingResourceAdds.Clear();
        pendingResourceAddSet.Clear();
        batchedResources.Clear();

        foreach (ResourceInstance resource in registeredResources)
        {
            if (resource == null)
            {
                cleanupBuffer.Add(resource);
                continue;
            }

            AddResourceToBatches(resource);
            batchedResources.Add(resource);
        }

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            registeredResources.Remove(cleanupBuffer[i]);
        }

        cleanupBuffer.Clear();
    }

    private void ApplyPendingResourceAdds(int budget)
    {
        int normalizedBudget = Mathf.Max(1, budget);
        int processed = 0;
        cleanupBuffer.Clear();
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
                cleanupBuffer.Add(resource);
                continue;
            }

            if (!registeredResources.Contains(resource) || batchedResources.Contains(resource))
            {
                continue;
            }

            AddResourceToBatches(resource);
            batchedResources.Add(resource);
        }

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            registeredResources.Remove(cleanupBuffer[i]);
        }

        cleanupBuffer.Clear();
    }

    private void AddResourceToBatches(ResourceInstance resource)
    {
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
            if (materialCount <= 0)
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
        if (mesh == null || material == null || subMeshIndex < 0)
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
        if (!matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices))
        {
            matrices = new List<Matrix4x4>(16);
            matricesByBatch.Add(key, matrices);
        }

        if (matrices.Count == 0)
        {
            activeBatchKeys.Add(key);
            boundsByBatch[key] = VirtualRenderBatchCollection.CalculateWorldBounds(mesh, localToWorldMatrix);
        }
        else
        {
            Bounds batchBounds = boundsByBatch[key];
            batchBounds.Encapsulate(VirtualRenderBatchCollection.CalculateWorldBounds(mesh, localToWorldMatrix));
            boundsByBatch[key] = batchBounds;
        }

        matrices.Add(localToWorldMatrix);
    }

    private void RenderBatches()
    {
        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
            mainCamera = Camera.main;
        cameraCulling.Update(mainCamera);

        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            BatchKey key = activeBatchKeys[batchIndex];
            if (!matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices)
                || matrices.Count <= 0
                || !boundsByBatch.TryGetValue(key, out Bounds batchBounds))
            {
                continue;
            }

            if (!cameraCulling.IsLayerVisible(key.Layer) || !cameraCulling.Intersects(batchBounds))
            {
                continue;
            }

            if (!cameraCulling.Contains(batchBounds) && key.ShadowCastingMode != ShadowCastingMode.ShadowsOnly
                && key.ShadowCastingMode != ShadowCastingMode.TwoSided)
            {
                CameraBatch cache = ResolveCameraBatch(key, matrices);
                DrawResourceBatch(key, cache.Visible, batchBounds, key.ShadowCastingMode);
                if (key.ShadowCastingMode == ShadowCastingMode.On)
                    DrawResourceBatch(key, cache.Hidden, batchBounds, ShadowCastingMode.ShadowsOnly);
                continue;
            }
            DrawResourceBatch(key, matrices, batchBounds, key.ShadowCastingMode);
        }
    }

    private CameraBatch ResolveCameraBatch(BatchKey key, List<Matrix4x4> matrices)
    {
        if (!cameraBatches.TryGetValue(key, out CameraBatch cache))
        {
            cache = new CameraBatch();
            cameraBatches.Add(key, cache);
        }
        if (cache.SourceCount == matrices.Count && cache.CameraVersion == cameraCulling.Version) return cache;
        cache.SourceCount = matrices.Count;
        cache.CameraVersion = cameraCulling.Version;
        cache.Visible.Clear();
        cache.Hidden.Clear();
        for (int i = 0; i < matrices.Count; i++)
        {
            Bounds bounds = VirtualRenderBatchCollection.CalculateWorldBounds(key.Mesh, matrices[i]);
            (cameraCulling.Intersects(bounds) ? cache.Visible : cache.Hidden).Add(matrices[i]);
        }
        return cache;
    }

    private static void DrawResourceBatch(BatchKey key, List<Matrix4x4> matrices, Bounds batchBounds,
        ShadowCastingMode shadowCastingMode)
    {
        RenderParams renderParams = new RenderParams(key.Material)
        {
            layer = key.Layer,
            shadowCastingMode = shadowCastingMode,
            receiveShadows = key.ReceiveShadows,
            worldBounds = batchBounds
        };

        int remaining = matrices.Count;
        int startIndex = 0;
        while (remaining > 0)
        {
            int drawCount = Mathf.Min(MaxInstancesPerDraw, remaining);
            Graphics.RenderMeshInstanced(renderParams, key.Mesh, key.SubMeshIndex, matrices, drawCount, startIndex);
            startIndex += drawCount;
            remaining -= drawCount;
        }
    }

    private sealed class CameraBatch
    {
        public int SourceCount = -1, CameraVersion = -1;
        public readonly List<Matrix4x4> Visible = new List<Matrix4x4>();
        public readonly List<Matrix4x4> Hidden = new List<Matrix4x4>();
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
