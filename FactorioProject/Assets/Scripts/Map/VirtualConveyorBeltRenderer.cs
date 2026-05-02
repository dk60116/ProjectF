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

    private readonly Dictionary<ConveyorBelt, BeltRenderCache> beltRenderCaches = new Dictionary<ConveyorBelt, BeltRenderCache>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly List<VirtualConveyorBeltRenderData> scratchRenderData = new List<VirtualConveyorBeltRenderData>(8);

    public void Register(ConveyorBelt conveyorBelt)
    {
        if (!Application.isPlaying || conveyorBelt == null)
        {
            return;
        }

        if (ShouldUseNativeRendering(conveyorBelt))
        {
            Unregister(conveyorBelt);
            conveyorBelt.SetVirtualRenderingSuppressed(false);
            return;
        }

        BeltRenderCache cache = GetOrCreateBeltRenderCache(conveyorBelt);
        RefreshBeltRenderCache(conveyorBelt, cache);
        conveyorBelt.SetVirtualRenderingSuppressed(true);
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

    private void RefreshBeltRenderCache(ConveyorBelt conveyorBelt, BeltRenderCache cache)
    {
        batches.RemoveOwnedEntries(cache.batchEntries);
        scratchRenderData.Clear();
        conveyorBelt.AppendVirtualRenderData(scratchRenderData);

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

        VirtualRenderBatchKey key = new VirtualRenderBatchKey(
            renderData.Mesh,
            renderData.Material,
            renderData.Layer,
            renderData.SubmeshIndex,
            ShadowCastingMode.Off,
            false,
            renderData.HasUvScroll,
            VirtualRenderBatchKey.QuantizeUvScroll(renderData.UvScrollY),
            invertCulling: HasOddNegativeScale(renderData.Matrix));

        batches.AddOwnedMatrix(beltCache, beltCache.batchEntries, key, renderData.Matrix);
    }

    private void RenderBatches()
    {
        batches.RenderBatches();
    }

    private static bool ShouldUseNativeRendering(ConveyorBelt conveyorBelt)
    {
        return conveyorBelt != null && conveyorBelt.IsCornerVariant;
    }

    private static bool HasOddNegativeScale(Matrix4x4 matrix)
    {
        Vector3 xAxis = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        Vector3 yAxis = new Vector3(matrix.m01, matrix.m11, matrix.m21);
        Vector3 zAxis = new Vector3(matrix.m02, matrix.m12, matrix.m22);
        return Vector3.Dot(Vector3.Cross(xAxis, yAxis), zAxis) < 0f;
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
