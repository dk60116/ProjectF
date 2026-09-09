using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectF.Rendering;

public partial class VirtualRenderBatchCollection
{
    private readonly CameraRenderCulling cameraCulling = new();
    public static void CheckInstanceCulling()
    {
        GameManager.Instance.DisableCameraCulling = false;
        GameManager.Instance.FreeCamera = false;
        var collection = new VirtualRenderBatchCollection();
        Camera camera = Checks.View(0);
        collection.cameraCulling.Update(camera);
        var key = new VirtualRenderBatchKey { Mesh = new Mesh(), HasUvScroll = true, HasConveyorMotion = true };
        var source = new BatchRenderCache();
        foreach (float x in new[] { 0f, 100f, 9.99f })
        {
            source.Matrices.Add(Checks.Pose(x));
            AddInstanceUvData(source, key, new Vector4(x, 1, 2, 3));
            AddConveyorMotionData(source, key, default);
        }
        source.MarkDataDirty();
        collection.ResolveCameraBatches(key, source, out var visible, out var hidden);
        Checks.Require(visible.Matrices.Count == 2 && hidden.Matrices.Count == 1,
            "boundary batch does not retain a distant instance; partially visible instance stays");
        Checks.Require(visible.InstanceUvData[1].x == 9.99f && hidden.InstanceUvData[0].x == 100f,
            "compaction keeps UV data aligned with source instances");
        Checks.Require(visible.ConveyorMotionStarts.Count == 2 && hidden.ConveyorMotionEnds.Count == 1,
            "compaction keeps GPU motion vectors aligned");
        int version = visible.DataVersion;
        collection.ResolveCameraBatches(key, source, out _, out _);
        Checks.Require(visible.DataVersion == version, "unchanged camera/data reuses compacted arrays and property-block version");
        camera.cullingMatrix = Checks.View(100).cullingMatrix;
        collection.cameraCulling.Update(camera);
        collection.ResolveCameraBatches(key, source, out visible, out hidden);
        Checks.Require(visible.Matrices.Count == 1 && visible.InstanceUvData[0].x == 100,
            "camera movement restores the correct instance and UV instead of stale compacted data");
        source.ConveyorMotionStarts[0] = new Vector4(0, 0, 0, 5);
        source.ConveyorMotionEnds[0] = new Vector4(100, 0, 0, 1);
        source.MarkDataDirty();
        collection.ResolveCameraBatches(key, source, out visible, out _);
        Checks.Require(visible.Matrices.Count == 2 && visible.ConveyorMotionEnds[0].x == 100,
            "moving item whose path enters the view remains visible despite offscreen starting matrix");
        Checks.Require(collection.cameraCulling.Contains(new Bounds(new Vector3(100, 0, 0), Vector3.one)),
            "fully visible batches can bypass instance scan");
        Checks.Require(!collection.cameraCulling.Contains(new Bounds(new Vector3(110, 0, 0), Vector3.one)),
            "partially visible batches require instance scan");
        Checks.Require(!collection.cameraCulling.Contains(new Bounds(Vector3.zero, Vector3.one)),
            "outside batches are not classified as fully contained");
        GameManager.Instance.DisableCameraCulling = true;
        collection.cameraCulling.Update(camera);
        collection.ResolveCameraBatches(key, source, out visible, out _);
        Checks.Require(visible.Matrices.Count == 3 && collection.cameraCulling.Contains(new Bounds(Vector3.zero, Vector3.one)),
            "disabled culling retains every instance and bypasses boundary scans");
        GameManager.Instance.DisableCameraCulling = false;
    }
}

public partial class ResourceCameraProbe
{
    private sealed class BatchKey { public Mesh Mesh = new Mesh(); }
    private readonly Dictionary<BatchKey, CameraBatch> cameraBatches = new();
    private readonly CameraRenderCulling cameraCulling = new();
    public static void Check()
    {
        var probe = new ResourceCameraProbe();
        var camera = Checks.View(0);
        probe.cameraCulling.Update(camera);
        var key = new BatchKey();
        var matrices = new List<Matrix4x4> { Checks.Pose(0), Checks.Pose(100) };
        CameraBatch cache = probe.ResolveCameraBatch(key, matrices);
        Checks.Require(cache.Visible.Count == 1 && cache.Hidden.Count == 1,
            "tree/resource boundary batch separates color-visible instances from offscreen shadow casters");
        Checks.Require(probe.ResolveCameraBatch(key, matrices) == cache, "resource culling reuses cache");
        matrices.Add(Checks.Pose(1));
        cache = probe.ResolveCameraBatch(key, matrices);
        Checks.Require(cache.Visible.Count == 2, "incremental resource addition invalidates cached count");
        matrices[0] = Checks.Pose(100);
        cache.SourceCount = -1; // ClearActiveBatches invalidates before a same-count rebuild.
        cache = probe.ResolveCameraBatch(key, matrices);
        Checks.Require(cache.Visible.Count == 1 && cache.Hidden.Count == 2, "same-count resource rebuild refreshes culling");
        camera.cullingMatrix = Checks.View(100).cullingMatrix;
        probe.cameraCulling.Update(camera);
        cache = probe.ResolveCameraBatch(key, matrices);
        Checks.Require(cache.Visible.Count == 2 && cache.Hidden.Count == 1, "resource visibility follows camera");
    }
}

// Render callback inputs only; production BRG instance-index selection and plane testing execute below.
public struct NativeArray<T>
{
    private T[] values;
    public NativeArray(T[] values) { this.values = values; }
    public bool IsCreated => values != null;
    public int Length => values?.Length ?? 0;
    public T this[int i] => values[i];
}
namespace UnityEngine.Rendering
{
    public enum BatchCullingViewType { Camera, Light }
    public struct CullingSplit { public int cullingPlaneOffset, cullingPlaneCount; }
    public struct BatchCullingContext
    {
        public BatchCullingViewType viewType;
        public NativeArray<Plane> cullingPlanes;
        public NativeArray<CullingSplit> cullingSplits;
    }
}
public partial class BrgInstanceProbe
{
    private bool DisableCameraCulling;
    private sealed class BrgBatchState
    {
        public int InstanceCount;
        public Bounds[] InstanceBounds;
        public readonly List<int> VisibleIndices = new();
    }
    public static void Check()
    {
        var probe = new BrgInstanceProbe();
        var planes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(Checks.View(0).cullingMatrix, planes);
        var context = new BatchCullingContext {
            viewType = BatchCullingViewType.Camera, cullingPlanes = new NativeArray<Plane>(planes),
            cullingSplits = new NativeArray<CullingSplit>(new[] { new CullingSplit { cullingPlaneCount = 6 } }) };
        var state = new BrgBatchState { InstanceCount = 3, InstanceBounds = new[] {
            new Bounds(new Vector3(100, 0, 0), Vector3.one), new Bounds(Vector3.zero, Vector3.one),
            new Bounds(new Vector3(10.4f, 0, 0), Vector3.one) } };
        probe.CollectVisibleInstances(state, context);
        Checks.Require(state.VisibleIndices.Count == 2 && state.VisibleIndices[0] == 1 && state.VisibleIndices[1] == 2,
            "BRG submits original visible indices only; boundary geometry remains visible");
        context.viewType = BatchCullingViewType.Light;
        probe.CollectVisibleInstances(state, context);
        Checks.Require(state.VisibleIndices.Count == 3, "BRG light view retains camera-hidden shadow casters");
        context.viewType = BatchCullingViewType.Camera;
        probe.DisableCameraCulling = true;
        probe.CollectVisibleInstances(state, context);
        Checks.Require(state.VisibleIndices.Count == 3, "BRG culling-off mode restores all indices");
        probe.DisableCameraCulling = false;
        context.cullingSplits = default;
        probe.CollectVisibleInstances(state, context);
        Checks.Require(state.VisibleIndices.Count == 3, "missing culling splits safely retain instances");
    }
}
