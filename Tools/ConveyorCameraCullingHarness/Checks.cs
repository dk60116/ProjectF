using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectF.Rendering;

// Only scene/native-render boundaries are doubles. Culling state, pending chunks,
// cache refresh, tracked-cell registration/removal and sync are production code.
namespace UnityEngine
{
    public class Camera
    {
        private Matrix4x4? customCullingMatrix;
        public Matrix4x4 AutomaticCullingMatrix = Matrix4x4.identity;
        public Matrix4x4 projectionMatrix = Matrix4x4.identity;
        public Matrix4x4 cullingMatrix { get => customCullingMatrix ?? AutomaticCullingMatrix; set => customCullingMatrix = value; }
        public int cullingMask = -1;
        public bool useOcclusionCulling = true;
        public CameraType cameraType = CameraType.Game;
        public static event Action<Camera> onPreCull, onPostRender;
        public void ResetCullingMatrix() => customCullingMatrix = null;
        public static void Begin(Camera camera) => onPreCull?.Invoke(camera);
        public static void End(Camera camera) => onPostRender?.Invoke(camera);
    }
    public class GameObject
    {
        public int layer;
        private readonly Dictionary<Type, object> components = new();
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out object c) ? (T)c : null;
        public T AddComponent<T>() where T : new() { var c = new T(); components.Add(typeof(T), c); return c; }
    }
    public class Transform { public Matrix4x4 localToWorldMatrix = Matrix4x4.identity; }
    public class Mesh { public Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f); }
    public class Material { public bool enableInstancing; }
    public static class GeometryUtility
    {
        public static void CalculateFrustumPlanes(Matrix4x4 m, Plane[] planes)
        {
            for (int axis = 0; axis < 3; axis++)
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1 : -1;
                Vector3 n = new(m[3,0] + sign*m[axis,0], m[3,1] + sign*m[axis,1], m[3,2] + sign*m[axis,2]);
                float length = n.magnitude;
                planes[axis*2+side] = new Plane(n/length, (m[3,3]+sign*m[axis,3])/length);
            }
        }
        public static bool TestPlanesAABB(Plane[] planes, Bounds b)
        {
            foreach (Plane p in planes)
            {
                var n=p.normal; var e=b.extents;
                if (p.GetDistanceToPoint(b.center) + Math.Abs(n.x)*e.x + Math.Abs(n.y)*e.y + Math.Abs(n.z)*e.z < 0) return false;
            }
            return true;
        }
    }
}
public class GameManager { public static GameManager Instance = new(); public bool DisableCameraCulling, FreeCamera, FreeCameraPlayerCulling; }
public readonly record struct BlockHandle(Vector2Int ChunkCoordinate, int Id, int Generation = 1) { public bool IsValid => Id > 0; }
public readonly record struct VirtualConveyorItemRenderData(int Version);
public class Block
{
    public GameObject gameObject = new(); public Vector3 WorldPosition; public int ConveyorItemVisualVersion;
    public bool Dynamic; public int Appends;
    public bool HasDynamicVirtualConveyorItemVisuals() => Dynamic;
    public void AppendVirtualConveyorItemRenderData(List<VirtualConveyorItemRenderData> items) { Appends++; items.Add(new(ConveyorItemVisualVersion)); }
}
public sealed partial class PortableItemRenderer
{
    private float dynamicVirtualConveyorItemCullBoundsSize=2.25f, dynamicVirtualConveyorItemCullBoundsHeight=2.5f;
    private readonly Dictionary<BlockHandle, Block> world = new();
    private readonly HashSet<BlockHandle> activeVirtualConveyorRenderBlockLookup = new();
    private readonly Dictionary<BlockHandle,BlockRenderCache> virtualConveyorBlockRenderCaches = new();
    private readonly List<VirtualConveyorItemRenderData> scratchVirtualConveyorRenderItems = new();
    private class BlockRenderCache { public bool isValid; public int version; public readonly List<VirtualConveyorItemRenderData> entries = new(); }
    private bool TryResolveConveyorBlock(BlockHandle h, out Block b) => world.TryGetValue(h,out b);
    private void RemoveVirtualConveyorBlockBatchEntries(BlockRenderCache cache) => cache.entries.Clear();
    private void AddVirtualConveyorBlockRenderItem(BlockRenderCache cache, VirtualConveyorItemRenderData item) => cache.entries.Add(item);
    private void Frame(Camera camera) { if(itemCameraCulling.Update(camera)) RefreshVisibleDeferredConveyorBlocks(); }
    private void Dirty(BlockHandle h) => RefreshVirtualConveyorBlockRenderCache(h,world[h],GetOrCreateVirtualConveyorBlockRenderCache(h));
    public static void Check()
    {
        var r=new PortableItemRenderer(); var camera=Checks.View(0); r.Frame(camera);
        var a=new BlockHandle(new Vector2Int(10,0),1); var b=new BlockHandle(new Vector2Int(20,0),2);
        r.world[a]=new Block { WorldPosition=new(100,0,0), ConveyorItemVisualVersion=1 };
        r.world[b]=new Block { WorldPosition=new(200,0,0), ConveyorItemVisualVersion=2 };
        r.activeVirtualConveyorRenderBlockLookup.UnionWith(new[]{a,b}); r.Dirty(a); r.Dirty(b);
        Checks.Require(r.DeferredStaticRenderBlocks==2 && r.world[a].Appends==0,"offscreen items defer without render-data generation");
        r.world[a].ConveyorItemVisualVersion=9; r.Dirty(a); r.Frame(camera);
        Checks.Require(r.DeferredStaticRenderBlocks==2 && r.world[a].Appends==0,"repeated offscreen mutation coalesces");
        camera.cullingMatrix=Checks.View(100).cullingMatrix; r.Frame(camera);
        Checks.Require(r.DeferredStaticRenderBlocks==1 && r.virtualConveyorBlockRenderCaches[a].entries[0].Version==9,"camera return refreshes latest version before drawing");
        camera.cullingMatrix=Checks.View(0).cullingMatrix; r.Frame(camera); r.world[a].ConveyorItemVisualVersion=10; r.Dirty(a);
        Checks.Require(r.virtualConveyorBlockRenderCaches[a].entries.Count==0,"stale batch entries removed when offscreen state changes");
        GameManager.Instance.DisableCameraCulling=true; r.Frame(camera);
        Checks.Require(r.DeferredStaticRenderBlocks==0 && r.virtualConveyorBlockRenderCaches[a].version==10 && r.world[b].Appends==1,"disabling culling flushes every pending chunk");
        GameManager.Instance.DisableCameraCulling=false; r.Frame(camera); r.world[a].ConveyorItemVisualVersion++; r.Dirty(a);
        r.activeVirtualConveyorRenderBlockLookup.Remove(a); r.RemoveVirtualConveyorBlockRenderCache(a);
        Checks.Require(r.DeferredStaticRenderBlocks==0 && !r.virtualConveyorBlockRenderCaches.ContainsKey(a),"unload removes pending and draw cache");
        r.Dirty(b); r.world[b].Dynamic=true; camera.cullingMatrix=Checks.View(200).cullingMatrix; r.Frame(camera);
        Checks.Require(r.DeferredStaticRenderBlocks==0 && !r.virtualConveyorBlockRenderCaches.ContainsKey(b),"returning CPU-dynamic item never restores stale static cache");
        r.world[b].Dynamic=false; camera.cullingMatrix=Checks.View(0).cullingMatrix; r.Frame(camera); r.Dirty(b);
        r.Frame(null);
        Checks.Require(r.DeferredStaticRenderBlocks==0,"missing camera falls back to current rendering");
    }
}
public interface IVirtualRenderBatchOwner { }
public class VirtualRenderBatchKey { public VirtualRenderBatchKey(Mesh mesh, Material material, int layer, int submesh, ShadowCastingMode shadow, bool receive, bool uv, int batchCellX, int batchCellZ, bool invertCulling) { } }
public struct VirtualRenderBatchEntry { public VirtualRenderBatchKey BatchKey; public int MatrixIndex; }
public partial class VirtualRenderBatchCollection
{
    private const float MinimumWorldBoundsSize=0.25f;
    public void RemoveOwnedEntries(List<VirtualRenderBatchEntry> entries) => entries.Clear();
    public void AddOwnedMatrix(object owner,List<VirtualRenderBatchEntry> entries,VirtualRenderBatchKey key,Matrix4x4 matrix,Vector4 uv) => entries.Add(new() { BatchKey=key });
    public bool TryUpdateOwnedMatrix(List<VirtualRenderBatchEntry> entries,int index,VirtualRenderBatchKey key,Matrix4x4 matrix) => true;
}
public partial class BeltProbe
{
    private const float DefaultMergedBatchCellSize=16;
    private float EffectiveBatchCellSize => 16;
    private readonly Dictionary<Vector2Int,TrackedBeltCell> trackedBeltCells=new();
    private readonly CameraRenderCulling cameraCulling=new();
    private readonly VirtualRenderBatchCollection batches=new();
    public int LastCulledTrackedBelts, LastTrackedTransformMatrixReads;
    private static Vector3 ExtractWorldPosition(Matrix4x4 m) => new(m.m03,m.m13,m.m23);
    private static int GetBatchCell(float p,float size) => (int)Math.Floor(p/size);
    private static bool HasOddNegativeScale(Matrix4x4 m) => false;
    private void Add(BeltRenderCache cache,Transform t) => AddBeltRenderData(cache,new VirtualConveyorBeltRenderData(new Mesh(),new Material(),t.localToWorldMatrix,0,0,false,0,1,0,false,t));
    public static void Check()
    {
        var r=new BeltProbe(); var camera=Checks.View(0); r.cameraCulling.Update(camera);
        var cache=new BeltRenderCache(); var t=new Transform(); t.localToWorldMatrix=Checks.Pose(100);
        r.Add(cache,t); r.Add(cache,t);
        Checks.Require(r.trackedBeltCells.Count==1,"tracked parts share spatial cell");
        r.SyncTrackedTransformMatrices();
        Checks.Require(r.LastTrackedTransformMatrixReads==0 && r.LastCulledTrackedBelts==1,"offscreen cell skips all native matrix reads");
        t.localToWorldMatrix=Checks.Pose(100.25f); camera.cullingMatrix=Checks.View(100).cullingMatrix; r.cameraCulling.Update(camera);
        Checks.Require(r.SyncTrackedTransformMatrices()==2 && r.LastTrackedTransformMatrixReads==2,"visible cell catches up to latest matrices");
        camera.cullingMatrix=Checks.View(0).cullingMatrix; GameManager.Instance.DisableCameraCulling=true; r.cameraCulling.Update(camera);
        int before=r.LastTrackedTransformMatrixReads; r.SyncTrackedTransformMatrices();
        Checks.Require(r.LastTrackedTransformMatrixReads==before+2,"disable toggle restores offscreen matrix processing");
        r.ClearBeltRenderCache(cache);
        Checks.Require(r.trackedBeltCells.Count==0,"unregister/refresh removes tracked cell");
        GameManager.Instance.DisableCameraCulling=false;
    }
}
public partial class BackendProbe
{
    private sealed class BrgBatchState
    {
        public VirtualRenderBatchKey Key;
        public int InstanceCount, LastSyncGeneration;
        public Bounds WorldBounds;
    }
    private sealed class RenderGroupDouble { public void SetGlobalBounds(Bounds bounds) { } }
    private readonly RenderGroupDouble rendererGroup = new();
    private readonly List<BrgBatchState> states = new();
    private readonly Dictionary<VirtualRenderBatchKey, BrgBatchState> statesByKey = new();
    private readonly List<VirtualRenderBatchKey> staleKeys = new();
    private int syncGeneration, releasedBuffers;
    private void RemoveState(VirtualRenderBatchKey key)
    {
        states.Remove(statesByKey[key]); statesByKey.Remove(key); releasedBuffers++;
    }
    public static void Check()
    {
        var r = new BackendProbe();
        var key = new VirtualRenderBatchKey(new Mesh(), new Material(), 0, 0, ShadowCastingMode.Off, false, false, 0, 0, false);
        var state = new BrgBatchState { Key = key, InstanceCount = 4 };
        r.states.Add(state); r.statesByKey.Add(key, state);
        r.BeginSync(); r.Deactivate(key, keepAllocated: true); r.EndSync();
        Checks.Require(r.states.Count == 1 && state.InstanceCount == 0 && r.releasedBuffers == 0,
            "offscreen batch stops drawing while retaining its GPU allocation");
        for (int i = 0; i < 3; i++) { r.BeginSync(); r.Deactivate(key, keepAllocated: true); r.EndSync(); }
        Checks.Require(r.statesByKey[key] == state && r.releasedBuffers == 0,
            "continued camera culling preserves the same allocation");
        r.BeginSync(); r.EndSync();
        Checks.Require(r.states.Count == 0 && r.releasedBuffers == 1,
            "a deleted batch still releases its retained allocation");
    }
}
public static class Checks
{
    private static int count;
    public static void Require(bool value,string name) { count++; if(!value) throw new Exception(name); }
    public static Matrix4x4 Pose(float x) { var m=Matrix4x4.identity; m.m03=x; return m; }
    public static Camera View(float x) { var c=new Camera(); var m=Matrix4x4.identity; m.m00=m.m11=m.m22=0.1f; m.m03=-x*0.1f; c.cullingMatrix=m; return c; }
    public static void Main()
    {
        var c=View(0); var v=new CameraRenderCulling();
        Require(v.Update(c) && v.Enabled,"false defaults to culling ON");
        Require(!v.Update(c),"unchanged camera does not rebuild planes");
        Require(v.Intersects(new Bounds(new Vector3(10.4f,0,0),Vector3.one)),"partially visible edge retained");
        Require(!v.Intersects(new Bounds(new Vector3(12,0,0),Vector3.one)),"outside edge culled");
        c.cullingMatrix=View(0.001f).cullingMatrix; Require(v.Update(c),"sub-threshold movement refreshes visibility");
        c.cullingMask=1; Require(v.Update(c) && !v.IsLayerVisible(1),"layer changes refresh visibility");
        var m=c.cullingMatrix; m.m00=0.05f; c.cullingMatrix=m;
        Require(v.Update(c) && v.Intersects(new Bounds(new Vector3(12,0,0),Vector3.one)),"zoom immediately reveals boundary objects");
        GameManager.Instance.DisableCameraCulling=true; Require(v.Update(c) && v.Intersects(new Bounds(new Vector3(1000,0,0),Vector3.one)),"true disables culling");
        GameManager.Instance.DisableCameraCulling=false; Require(v.Update(c) && !v.Intersects(new Bounds(new Vector3(1000,0,0),Vector3.one)),"false re-enables culling");
        PortableItemRenderer.Check(); BeltProbe.Check(); BackendProbe.Check(); WorldChecks.Check();
        AnimalAnimationChecks.Check();
        FreeCameraChecks.Check();
        Console.WriteLine($"PASS: {count} world-camera/animal-animation/deferred-item/tracked-cell/batch-lifetime checks; production methods, managed scene/render doubles. No engine launched.");
    }
}
