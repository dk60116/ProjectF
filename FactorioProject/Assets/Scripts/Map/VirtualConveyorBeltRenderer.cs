using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualConveyorBeltRenderData
{
    public VirtualConveyorBeltRenderData(
        Mesh mesh,
        Material material,
        Matrix4x4 matrix,
        int layer,
        int submeshIndex,
        bool hasUvScroll,
        float uvScrollY,
        float uvLengthScale,
        float uvLengthOffset,
        bool usesDedicatedBeltTopMesh = false,
        Transform trackedTransform = null)
    {
        Mesh = mesh;
        Material = material;
        Matrix = matrix;
        Layer = layer;
        SubmeshIndex = submeshIndex;
        HasUvScroll = hasUvScroll;
        UvScrollY = uvScrollY;
        UvLengthScale = uvLengthScale;
        UvLengthOffset = uvLengthOffset;
        UsesDedicatedBeltTopMesh = usesDedicatedBeltTopMesh;
        TrackedTransform = trackedTransform;
    }

    public readonly Mesh Mesh;
    public readonly Material Material;
    public readonly Matrix4x4 Matrix;
    public readonly int Layer;
    public readonly int SubmeshIndex;
    public readonly bool HasUvScroll;
    public readonly float UvScrollY;
    public readonly float UvLengthScale;
    public readonly float UvLengthOffset;
    public readonly bool UsesDedicatedBeltTopMesh;
    public readonly Transform TrackedTransform;
}

[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class VirtualConveyorBeltRenderer : MonoBehaviour
{
    private static readonly ProfilerMarker RenderBatchesMarker = new ProfilerMarker("VirtualConveyorBeltRenderer.RenderBatches");
    private const float DefaultMergedBatchCellSize = 16f;

    [SerializeField, Min(1f)]
    private float batchCellSize = 8f;
    [SerializeField, Min(1f)]
    private float minimumMergedBatchCellSize = DefaultMergedBatchCellSize;
    [SerializeField]
    private bool suppressNativeSourceRenderers = true;
    [SerializeField]
    private bool hideNativeSourceObjects = true;

    private readonly Dictionary<ConveyorBelt, BeltRenderCache> beltRenderCaches = new Dictionary<ConveyorBelt, BeltRenderCache>();
    private readonly Dictionary<Vector2Int, TrackedBeltCell> trackedBeltCells = new Dictionary<Vector2Int, TrackedBeltCell>();
    private readonly ProjectF.Rendering.CameraRenderCulling cameraCulling = new ProjectF.Rendering.CameraRenderCulling();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly List<VirtualConveyorBeltRenderData> scratchRenderData = new List<VirtualConveyorBeltRenderData>(8);
    private Camera mainCamera;

    public int RegisteredBeltCount => beltRenderCaches.Count;
    public int ActiveBatchCount => batches.ActiveBatchCount;
    public int ActiveInstanceCount => batches.ActiveMatrixCount;
    public int EstimatedDrawCallCount => batches.EstimatedDrawCallCount;
    public int ActiveBatchRendererGroupBatchCount =>
        batches.ActiveBatchRendererGroupBatchCount;
    public float EffectiveBatchCellSize => Mathf.Max(batchCellSize, ResolveMinimumMergedBatchCellSize());
    public bool NativeSourceSuppressionEnabled => suppressNativeSourceRenderers;
    public bool NativeSourceObjectHidingEnabled => suppressNativeSourceRenderers && hideNativeSourceObjects;
    public int HiddenSourceViewBeltCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Key != null && pair.Key.IsVirtualizedSourceViewHidden)
                {
                    total++;
                }
            }

            return total;
        }
    }

    public int HiddenSourceViewObjectCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Key != null)
                {
                    total += pair.Key.VirtualizedSourceViewHiddenObjectCount;
                }
            }

            return total;
        }
    }

    public int RegisteredCornerBeltCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Value != null && pair.Value.IsCornerVariant)
                {
                    total++;
                }
            }

            return total;
        }
    }

    public int VirtualizedBeltCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                ConveyorBelt conveyorBelt = pair.Key;
                if (conveyorBelt != null && conveyorBelt.IsVirtualRenderingSuppressed)
                {
                    total++;
                }
            }

            return total;
        }
    }

    public int NativeRendererCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Key != null)
                {
                    total += pair.Key.NativeRendererCount;
                }
            }

            return total;
        }
    }

    public int EnabledNativeRendererCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Key != null)
                {
                    total += pair.Key.EnabledNativeRendererCount;
                }
            }

            return total;
        }
    }

    public int ActiveEnabledNativeRendererCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Key != null)
                {
                    total += pair.Key.ActiveEnabledNativeRendererCount;
                }
            }

            return total;
        }
    }

    public int SuppressedNativeRendererCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Key != null)
                {
                    total += pair.Key.SuppressedNativeRendererCount;
                }
            }

            return total;
        }
    }

    public int ActiveEntryCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Value != null)
                {
                    total += pair.Value.batchEntries.Count;
                }
            }

            return total;
        }
    }

    public int DedicatedBeltTopEntryCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Value != null)
                {
                    total += pair.Value.DedicatedBeltTopEntryCount;
                }
            }

            return total;
        }
    }

    public int TrackedTransformEntryCount
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
            {
                if (pair.Value != null)
                {
                    total += pair.Value.trackedTransformEntries.Count;
                }
            }

            return total;
        }
    }

    public int LastTrackedTransformMatrixUpdates { get; private set; }
    public int LastTrackedTransformMatrixReads { get; private set; }
    public int LastCulledTrackedBelts { get; private set; }

    private void Awake()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        suppressNativeSourceRenderers = true;
        hideNativeSourceObjects = true;
    }

    private void OnDestroy()
    {
        Clear();
        batches.Dispose();
    }

    private void OnDisable()
    {
        batches.SuspendRendering();
    }

    public void Register(ConveyorBelt conveyorBelt)
    {
        if (!Application.isPlaying || conveyorBelt == null)
        {
            return;
        }

        BeltRenderCache cache = GetOrCreateBeltRenderCache(conveyorBelt);
        cache.IsCornerVariant = conveyorBelt.IsCornerVariant;
        conveyorBelt.SetVirtualizedSourceViewHidden(false);
        bool hasVirtualRenderData = suppressNativeSourceRenderers
            && RefreshBeltRenderCache(conveyorBelt, cache);
        if (!suppressNativeSourceRenderers)
        {
            ClearBeltRenderCache(cache);
        }

        conveyorBelt.SetVirtualRenderingSuppressed(hasVirtualRenderData);
        conveyorBelt.SetRuntimeRenderingHidden(IsBeltRenderingHidden());
        conveyorBelt.SetVirtualizedSourceViewHidden(hasVirtualRenderData && hideNativeSourceObjects);
    }

    public void Unregister(ConveyorBelt conveyorBelt, bool restoreNativeRenderers = true)
    {
        if (conveyorBelt == null)
        {
            return;
        }

        if (beltRenderCaches.TryGetValue(conveyorBelt, out BeltRenderCache cache))
        {
            ClearBeltRenderCache(cache);
            beltRenderCaches.Remove(conveyorBelt);
        }

        conveyorBelt.SetVirtualizedSourceViewHidden(false);

        if (restoreNativeRenderers)
        {
            conveyorBelt.SetVirtualRenderingSuppressed(false);
        }
    }

    public void Clear(bool restoreNativeRenderers = true)
    {
        foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
        {
            ConveyorBelt conveyorBelt = pair.Key;
            if (conveyorBelt == null)
            {
                continue;
            }

            conveyorBelt.SetVirtualizedSourceViewHidden(false);
            if (restoreNativeRenderers)
            {
                conveyorBelt.SetVirtualRenderingSuppressed(false);
            }
        }

        beltRenderCaches.Clear();
        trackedBeltCells.Clear();
        batches.Clear();
        scratchRenderData.Clear();
        LastTrackedTransformMatrixUpdates = 0;
    }

    private void LateUpdate()
    {
        LastTrackedTransformMatrixReads = 0;
        LastCulledTrackedBelts = 0;
        if (!Application.isPlaying)
        {
            return;
        }

        if (!suppressNativeSourceRenderers)
        {
            LastTrackedTransformMatrixUpdates = 0;
            if (!IsBeltRenderingHidden())
            {
                RestoreNativeRenderingForRegisteredBelts();
            }

            return;
        }

        if (batches.ActiveBatchCount == 0)
        {
            LastTrackedTransformMatrixUpdates = 0;
            return;
        }

        if (IsBeltRenderingHidden())
        {
            LastTrackedTransformMatrixUpdates = 0;
            batches.SuspendRendering();
            return;
        }

        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
        {
            mainCamera = Camera.main;
        }

        bool profileRender = MapObjectTickProfiler.IsEnabled;
        cameraCulling.Update(mainCamera);
        long startTimestamp = profileRender ? MapObjectTickProfiler.BeginSample() : 0L;
        try
        {
            using (RenderBatchesMarker.Auto())
            {
                LastTrackedTransformMatrixUpdates = SyncTrackedTransformMatrices();
                RenderBatches();
            }
        }
        finally
        {
            if (profileRender)
            {
                MapObjectTickProfiler.EndNamedSample(
                    "Runtime",
                    nameof(VirtualConveyorBeltRenderer),
                    "Virtual Belt Render",
                    startTimestamp);
            }
        }
    }

    private BeltRenderCache GetOrCreateBeltRenderCache(ConveyorBelt conveyorBelt)
    {
        if (!beltRenderCaches.TryGetValue(conveyorBelt, out BeltRenderCache cache))
        {
            cache = new BeltRenderCache();
            beltRenderCaches.Add(conveyorBelt, cache);
        }

        return cache;
    }

    private bool RefreshBeltRenderCache(ConveyorBelt conveyorBelt, BeltRenderCache cache)
    {
        ClearBeltRenderCache(cache);
        scratchRenderData.Clear();
        bool hasCompleteCoverage = conveyorBelt.AppendVirtualRenderData(scratchRenderData);
        if (!hasCompleteCoverage)
        {
            scratchRenderData.Clear();
            return false;
        }

        for (int i = 0; i < scratchRenderData.Count; i++)
        {
            AddBeltRenderData(cache, scratchRenderData[i]);
        }

        scratchRenderData.Clear();
        return cache.batchEntries.Count > 0;
    }

    private void RefreshAllBeltRenderCaches()
    {
        foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
        {
            ConveyorBelt conveyorBelt = pair.Key;
            BeltRenderCache cache = pair.Value;
            if (conveyorBelt == null || cache == null)
            {
                continue;
            }

            cache.IsCornerVariant = conveyorBelt.IsCornerVariant;
            conveyorBelt.SetVirtualizedSourceViewHidden(false);
            bool hasVirtualRenderData = suppressNativeSourceRenderers
                && RefreshBeltRenderCache(conveyorBelt, cache);
            if (!suppressNativeSourceRenderers)
            {
                ClearBeltRenderCache(cache);
            }

            conveyorBelt.SetVirtualRenderingSuppressed(hasVirtualRenderData);
            conveyorBelt.SetRuntimeRenderingHidden(IsBeltRenderingHidden());
            conveyorBelt.SetVirtualizedSourceViewHidden(hasVirtualRenderData && hideNativeSourceObjects);
        }
    }

    private void RestoreNativeRenderingForRegisteredBelts()
    {
        foreach (KeyValuePair<ConveyorBelt, BeltRenderCache> pair in beltRenderCaches)
        {
            ConveyorBelt conveyorBelt = pair.Key;
            if (conveyorBelt == null)
            {
                continue;
            }

            conveyorBelt.SetVirtualizedSourceViewHidden(false);
            conveyorBelt.SetVirtualRenderingSuppressed(false);
            conveyorBelt.SetRuntimeRootSuspended(false);
            conveyorBelt.SetRuntimeRenderingHidden(false);
            ClearBeltRenderCache(pair.Value);
        }
    }

    private void ClearBeltRenderCache(BeltRenderCache cache)
    {
        if (cache == null)
        {
            return;
        }

        batches.RemoveOwnedEntries(cache.batchEntries);
        if (cache.trackedTransformEntries.Count > 0
            && trackedBeltCells.TryGetValue(cache.TrackedCell, out TrackedBeltCell cell))
        {
            cell.Caches.Remove(cache);
            if (cell.Caches.Count == 0)
                trackedBeltCells.Remove(cache.TrackedCell);
        }
        cache.HasBounds = false;
        cache.DedicatedBeltTopEntryCount = 0;
        cache.trackedTransformEntries.Clear();
    }

    private void AddBeltRenderData(BeltRenderCache beltCache, VirtualConveyorBeltRenderData renderData)
    {
        if (renderData.Mesh == null || renderData.Material == null)
        {
            return;
        }

        if (!renderData.Material.enableInstancing)
        {
            renderData.Material.enableInstancing = true;
        }

        Vector3 worldPosition = ExtractWorldPosition(renderData.Matrix);
        Bounds partBounds = VirtualRenderBatchCollection.CalculateWorldBounds(renderData.Mesh, renderData.Matrix);
        // Include local rotation and the endpoint overhang at the view boundary.
        partBounds.Expand(2f);
        if (beltCache.HasBounds)
            beltCache.WorldBounds.Encapsulate(partBounds);
        else
        {
            beltCache.WorldBounds = partBounds;
            beltCache.HasBounds = true;
        }
        float effectiveBatchCellSize = EffectiveBatchCellSize;
        int cellX = GetBatchCell(worldPosition.x, effectiveBatchCellSize);
        int cellZ = GetBatchCell(worldPosition.z, effectiveBatchCellSize);
        VirtualRenderBatchKey key = new VirtualRenderBatchKey(
            renderData.Mesh,
            renderData.Material,
            renderData.Layer,
            renderData.SubmeshIndex,
            ShadowCastingMode.Off,
            false,
            renderData.HasUvScroll,
            batchCellX: cellX,
            batchCellZ: cellZ,
            invertCulling: HasOddNegativeScale(renderData.Matrix));

        int batchEntryIndex = beltCache.batchEntries.Count;
        batches.AddOwnedMatrix(
            beltCache,
            beltCache.batchEntries,
            key,
            renderData.Matrix,
            new Vector4(
                0f,
                renderData.UvScrollY,
                renderData.UvLengthScale,
                renderData.UvLengthOffset));
        if (renderData.TrackedTransform != null)
        {
            if (beltCache.trackedTransformEntries.Count == 0)
            {
                beltCache.TrackedCell = new Vector2Int(GetBatchCell(worldPosition.x, DefaultMergedBatchCellSize),
                    GetBatchCell(worldPosition.z, DefaultMergedBatchCellSize));
                if (!trackedBeltCells.TryGetValue(beltCache.TrackedCell, out TrackedBeltCell cell))
                {
                    cell = new TrackedBeltCell { WorldBounds = beltCache.WorldBounds };
                    trackedBeltCells.Add(beltCache.TrackedCell, cell);
                }
                cell.Caches.Add(beltCache);
            }
            beltCache.trackedTransformEntries.Add(new TrackedTransformEntry(
                batchEntryIndex,
                renderData.TrackedTransform,
                renderData.Matrix));
        }

        if (beltCache.trackedTransformEntries.Count > 0)
            trackedBeltCells[beltCache.TrackedCell].WorldBounds.Encapsulate(beltCache.WorldBounds);

        if (renderData.UsesDedicatedBeltTopMesh)
        {
            beltCache.DedicatedBeltTopEntryCount++;
        }
    }

    private int SyncTrackedTransformMatrices()
    {
        int updateCount = 0;
        // Reject whole spatial cells before touching their Unity transforms.
        foreach (TrackedBeltCell cell in trackedBeltCells.Values)
        {
            if (!cameraCulling.Intersects(cell.WorldBounds))
            {
                LastCulledTrackedBelts += cell.Caches.Count;
                continue;
            }
            foreach (BeltRenderCache cache in cell.Caches)
            {
                if (!cameraCulling.Intersects(cache.WorldBounds))
                {
                    LastCulledTrackedBelts++;
                    continue;
                }
                updateCount += SyncTrackedTransformMatrices(cache);
            }
        }
        return updateCount;
    }

    private int SyncTrackedTransformMatrices(BeltRenderCache cache)
    {
        int updateCount = 0;
        for (int i = 0; i < cache.trackedTransformEntries.Count; i++)
        {
            TrackedTransformEntry trackedEntry = cache.trackedTransformEntries[i];
            Transform trackedTransform = trackedEntry.Transform;
            if (trackedTransform == null)
            {
                continue;
            }

            Matrix4x4 matrix = trackedTransform.localToWorldMatrix;
            LastTrackedTransformMatrixReads++;
            if (matrix == trackedEntry.LastMatrix)
            {
                continue;
            }

            int batchEntryIndex = trackedEntry.BatchEntryIndex;
            if (batchEntryIndex < 0 || batchEntryIndex >= cache.batchEntries.Count)
            {
                continue;
            }

            VirtualRenderBatchKey key = cache.batchEntries[batchEntryIndex].BatchKey;
            if (!batches.TryUpdateOwnedMatrix(cache.batchEntries, batchEntryIndex, key, matrix))
            {
                continue;
            }

            trackedEntry.LastMatrix = matrix;
            cache.trackedTransformEntries[i] = trackedEntry;
            updateCount++;
        }
        return updateCount;
    }

    private void RenderBatches()
    {
        batches.RenderBatches(mainCamera);
    }

    private static bool IsBeltRenderingHidden()
    {
        return GameManager.Instance != null && GameManager.Instance.HideBelts;
    }

    private static bool HasOddNegativeScale(Matrix4x4 matrix)
    {
        Vector3 xAxis = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        Vector3 yAxis = new Vector3(matrix.m01, matrix.m11, matrix.m21);
        Vector3 zAxis = new Vector3(matrix.m02, matrix.m12, matrix.m22);
        return Vector3.Dot(Vector3.Cross(xAxis, yAxis), zAxis) < 0f;
    }

    private static Vector3 ExtractWorldPosition(Matrix4x4 matrix)
    {
        return new Vector3(matrix.m03, matrix.m13, matrix.m23);
    }

    private static int GetBatchCell(float worldCoordinate, float cellSize)
    {
        return Mathf.FloorToInt(worldCoordinate / Mathf.Max(1f, cellSize));
    }

    private float ResolveMinimumMergedBatchCellSize()
    {
        return minimumMergedBatchCellSize > 0f
            ? minimumMergedBatchCellSize
            : DefaultMergedBatchCellSize;
    }

    private sealed class TrackedBeltCell
    {
        public Bounds WorldBounds;
        public readonly HashSet<BeltRenderCache> Caches = new HashSet<BeltRenderCache>();
    }

    private sealed class BeltRenderCache : IVirtualRenderBatchOwner
    {
        public readonly List<VirtualRenderBatchEntry> batchEntries = new List<VirtualRenderBatchEntry>(4);
        public readonly List<TrackedTransformEntry> trackedTransformEntries = new List<TrackedTransformEntry>(4);
        public bool IsCornerVariant;
        public Bounds WorldBounds;
        public bool HasBounds;
        public Vector2Int TrackedCell;
        public int DedicatedBeltTopEntryCount { get; set; }

        public int BatchEntryCount => batchEntries.Count;

        public void UpdateBatchEntryMatrixIndex(int entryIndex, int matrixIndex)
        {
            if (entryIndex < 0 || entryIndex >= batchEntries.Count)
            {
                return;
            }

            VirtualRenderBatchEntry entry = batchEntries[entryIndex];
            entry.MatrixIndex = matrixIndex;
            batchEntries[entryIndex] = entry;
        }
    }

    private struct TrackedTransformEntry
    {
        public TrackedTransformEntry(
            int batchEntryIndex,
            Transform transform,
            Matrix4x4 lastMatrix)
        {
            BatchEntryIndex = batchEntryIndex;
            Transform = transform;
            LastMatrix = lastMatrix;
        }

        public int BatchEntryIndex;
        public Transform Transform;
        public Matrix4x4 LastMatrix;
    }
}
