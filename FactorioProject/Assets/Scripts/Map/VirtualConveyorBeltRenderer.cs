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
        float uvScrollY)
    {
        Mesh = mesh;
        Material = material;
        Matrix = matrix;
        Layer = layer;
        SubmeshIndex = submeshIndex;
        HasUvScroll = hasUvScroll;
        UvScrollY = uvScrollY;
    }

    public readonly Mesh Mesh;
    public readonly Material Material;
    public readonly Matrix4x4 Matrix;
    public readonly int Layer;
    public readonly int SubmeshIndex;
    public readonly bool HasUvScroll;
    public readonly float UvScrollY;
}

[DisallowMultipleComponent]
public sealed class VirtualConveyorBeltRenderer : MonoBehaviour
{
    private static readonly ProfilerMarker RenderBatchesMarker = new ProfilerMarker("VirtualConveyorBeltRenderer.RenderBatches");

    [SerializeField, Min(1f)]
    private float batchCellSize = 8f;

    private readonly Dictionary<ConveyorBelt, BeltRenderCache> beltRenderCaches = new Dictionary<ConveyorBelt, BeltRenderCache>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly List<VirtualConveyorBeltRenderData> scratchRenderData = new List<VirtualConveyorBeltRenderData>(8);
    private Camera mainCamera;

    public void Register(ConveyorBelt conveyorBelt)
    {
        if (!Application.isPlaying || conveyorBelt == null)
        {
            return;
        }

        BeltRenderCache cache = GetOrCreateBeltRenderCache(conveyorBelt);
        bool beltTopOnly = conveyorBelt.IsCornerVariant;
        RefreshBeltRenderCache(conveyorBelt, cache, beltTopOnly);
        conveyorBelt.SetVirtualRenderingSuppressed(true, beltTopOnly);
    }

    public void Unregister(ConveyorBelt conveyorBelt, bool restoreNativeRenderers = true)
    {
        if (conveyorBelt == null)
        {
            return;
        }

        if (beltRenderCaches.TryGetValue(conveyorBelt, out BeltRenderCache cache))
        {
            batches.RemoveOwnedEntries(cache.batchEntries);
            beltRenderCaches.Remove(conveyorBelt);
        }

        if (restoreNativeRenderers)
        {
            conveyorBelt.SetVirtualRenderingSuppressed(false);
        }
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || batches.ActiveBatchCount == 0)
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        using (RenderBatchesMarker.Auto())
        {
            RenderBatches();
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

    private void RefreshBeltRenderCache(ConveyorBelt conveyorBelt, BeltRenderCache cache, bool beltTopOnly)
    {
        batches.RemoveOwnedEntries(cache.batchEntries);
        scratchRenderData.Clear();
        conveyorBelt.AppendVirtualRenderData(scratchRenderData, beltTopOnly);

        for (int i = 0; i < scratchRenderData.Count; i++)
        {
            AddBeltRenderData(cache, scratchRenderData[i]);
        }

        scratchRenderData.Clear();
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
        int cellX = GetBatchCell(worldPosition.x, batchCellSize);
        int cellZ = GetBatchCell(worldPosition.z, batchCellSize);
        VirtualRenderBatchKey key = new VirtualRenderBatchKey(
            renderData.Mesh,
            renderData.Material,
            renderData.Layer,
            renderData.SubmeshIndex,
            ShadowCastingMode.Off,
            false,
            renderData.HasUvScroll,
            VirtualRenderBatchKey.QuantizeUvScroll(renderData.UvScrollY),
            batchCellX: cellX,
            batchCellZ: cellZ,
            invertCulling: HasOddNegativeScale(renderData.Matrix));

        batches.AddOwnedMatrix(beltCache, beltCache.batchEntries, key, renderData.Matrix);
    }

    private void RenderBatches()
    {
        batches.RenderBatches(mainCamera);
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

    private sealed class BeltRenderCache : IVirtualRenderBatchOwner
    {
        public readonly List<VirtualRenderBatchEntry> batchEntries = new List<VirtualRenderBatchEntry>(4);

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
}
