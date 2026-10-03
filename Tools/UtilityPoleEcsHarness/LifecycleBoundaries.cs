using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Power;
using ProjectF.Rendering;

// Only native view/prefab/physics/virtual-handle boundaries are doubled.
// UtilityPoleWorld and its numerical wire/terminal data run their actual sources.
public class PoleSphereBoundary { }
public class UtilityPole
{
    public ItemDefinition BoundItemDefinition = new(); public bool HasCollider = true;
    public int ResolveItemId() => 49;
    public T GetComponent<T>() where T : class => HasCollider ? new PoleSphereBoundary() as T : null;
}
public class ItemDefinition { public Archetype MapObjectArchetype = new(); }
public class Archetype { public List<int> RenderParts = new() { 1 }; }
public static class InputOutputModule { public static ItemDefinition ResolveItemDefinition(int id) => new(); }
public class TerrainGenerator
{
    public Transform transform => null;
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate,out Block block) => Blocks.TryGetValue(coordinate,out block);
    public readonly BlockStateStore Store = new();
    public T GetComponent<T>() where T : class => Store as T;
}
public class Block { public object MapObject; public int Writes; public void SetMapObject(object target) { MapObject=target; Writes++; } }
public class BlockStateStore
{
    public bool TopologyComplete = true;
    public int TopologyChecks;
    public bool CanCaptureUtilityPoleTopology { get { TopologyChecks++; return TopologyComplete; } }
    public class InstallationSaveState { public Vector2Int anchorCoordinate; public Vector3 worldPosition; public List<Vector2Int> occupiedCoordinates=new(); }
    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state) => state.anchorCoordinate;
}
public class VirtualObjectWorld
{
    public static VirtualObjectWorld Ensure() => new();
    public bool TryGetInstallationHandle(Vector2Int key,out ProjectF.MapObjects.MapObjectHandle handle) { handle=default;return true; }
}
public static class MapObjectTickProfiler { public static void AddRuntimeCounter(string scope,string name,long value) { } }
namespace ProjectF.MapObjects { public struct MapObjectHandle { } }
namespace ProjectF.Rendering
{
    public class CameraRenderCulling
    {
        public bool TryGetVisibleCellRange(float size,int padding,out Vector2Int min,out Vector2Int max) { min=max=default;return false; }
        public static long GetCellCount(Vector2Int min,Vector2Int max) => 1;
    }
    public static class UtilityPoleWireRenderer
    {
        internal static readonly HashSet<UtilityPoleWire> Wires=new();
        public static int Invalidations;
        internal static void Register(UtilityPoleWire wire) => Wires.Add(wire);
        internal static void Unregister(UtilityPoleWire wire) => Wires.Remove(wire);
        internal static void Invalidate() => Invalidations++;
    }
}
namespace ProjectF.Power
{
    public class UtilityPoleWorldView
    {
        public static int Created, Released, Unbound;
        public int VisibleCount;
        public static UtilityPoleWorldView Create(UtilityPoleWorld owner,Transform parent) { Created++;return new(); }
        public void Unbind(UtilityPoleRuntime pole) => Unbound++;
        public void Release() => Released++;
    }
    public class UtilityPoleRenderTemplate
    {
        public static int Created;
        public UtilityPoleRenderTemplate(global::UtilityPole source) => Created++;
    }
    public class UtilityPoleRuntime
    {
        public UtilityPoleWorld World; public UtilityPole Prototype; public BlockStateStore.InstallationSaveState Placement;
        public bool Registered; public int OrderIndex; public Vector3 WorldPosition => Placement.worldPosition;
        public Quaternion WorldRotation=Quaternion.identity;
        public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Placement.occupiedCoordinates;
        public Vector2Int StorageKey => Placement.anchorCoordinate;
        public Bounds CullBounds;
        public bool IsRuntimeActive => Registered;
        public static int Activated, Deactivated, Persisted, BatchDepth;
        public static int UncheckedPersists;
        public static bool DeactivatedWhileRegistered;
        public UtilityPoleRuntime(UtilityPoleWorld world,UtilityPole prototype,BlockStateStore.InstallationSaveState placement,
            ProjectF.MapObjects.MapObjectHandle handle,UtilityPoleRenderTemplate template) { World=world;Prototype=prototype;Placement=placement; }
        public void Activate() => Activated++;
        public void Deactivate() { Deactivated++;DeactivatedWhileRegistered|=Registered; }
        public void Persist(bool topologyChecked = false) { Persisted++; if (!topologyChecked) UncheckedPersists++; }
        public static void BeginTopologyRefreshBatch() => BatchDepth++;
        public static void EndTopologyRefreshBatch(bool rebuildDirtyTopology=true) => BatchDepth--;
    }
}
class Checks
{
    static int count;
    static void Check(bool value,string message) { count++;if(!value)throw new Exception(message); }
    static bool Near(Vector3 a,Vector3 b) => (a-b).sqrMagnitude < .000001f;
    static void Main()
    {
        var terrain=new TerrainGenerator(); var first=new UtilityPole(); var second=new UtilityPole();
        Check(UtilityPoleWorld.Supports(first),"real sphere-collider support branch");
        Check(!UtilityPoleWorld.Supports(new UtilityPole {HasCollider=false}),"unsupported prefab is retained on component path");
        var world=UtilityPoleWorld.Ensure(terrain);
        Check(ReferenceEquals(world,UtilityPoleWorld.Ensure(terrain)) && UtilityPoleWorldView.Created==1,"one body view per world");
        var entities=new List<UtilityPoleRuntime>();
        for(int i=0;i<10000;i++) {
            var key=new Vector2Int(i%100,i/100); var occupied=key+Vector2Int.up;
            terrain.Blocks[key]=new();terrain.Blocks[occupied]=new();
            var state=new BlockStateStore.InstallationSaveState {anchorCoordinate=key,worldPosition=new Vector3(key.x,0,key.y)};
            state.occupiedCoordinates.Add(key);state.occupiedCoordinates.Add(occupied);
            var pole=world.Register(i%2==0?first:second,state);entities.Add(pole);
            Check(pole.Registered && ReferenceEquals(terrain.Blocks[key].MapObject,pole),"registration binds individual data identity");
        }
        Check(world.Count==10000 && UtilityPoleRenderTemplate.Created==2 && UtilityPoleRuntime.Activated==10000,"shared templates and one activation per entity");
        var selected=entities[1234];terrain.Blocks[selected.StorageKey].MapObject=null;
        Check(ReferenceEquals(world.Register(first,selected.Placement),selected) && ReferenceEquals(terrain.Blocks[selected.StorageKey].MapObject,selected),"loaded blocks rebind without duplicate entity");
        Check(UtilityPoleRuntime.Activated==10000,"rebind does not duplicate electric graph activation");
        terrain.Store.TopologyComplete=false;world.FlushSaveStates();
        Check(UtilityPoleRuntime.Persisted==0,"partial loaded topology keeps the saved connections intact");
        terrain.Store.TopologyComplete=true;int topologyChecks=terrain.Store.TopologyChecks;world.FlushSaveStates();
        Check(UtilityPoleRuntime.Persisted==10000,"every data pole participates in complete save capture");
        Check(terrain.Store.TopologyChecks==topologyChecks+1 && UtilityPoleRuntime.UncheckedPersists==0,"complete topology is checked once per save, not once per pole");
        world.Remove(selected.StorageKey);Check(!selected.Registered && !world.TryGet(selected.StorageKey,out _),"remove invalidates identity and key before observer callbacks");
        Check(!UtilityPoleRuntime.DeactivatedWhileRegistered,"removal observers cannot reuse removed pole");
        Check(world.Count==9999,"swap removal maintains entity list");
        world.ClearRecords();Check(world.Count==0 && UtilityPoleRuntime.Deactivated==10000 && UtilityPoleRuntime.BatchDepth==0,"bulk clear deactivates each entity once in balanced topology batch");
        world.Dispose();Check(UtilityPoleWorld.Current==null && UtilityPoleWorldView.Released==1,"world disposal releases its single view");
        var owner=new UtilityPoleRuntime(null,first,new BlockStateStore.InstallationSaveState {worldPosition=new Vector3(5,3,7)},default,null);
        float angle=(float)Math.Sqrt(.5);owner.WorldRotation=new Quaternion(0,angle,0,angle);
        var point=new UtilityPoleLinePoint(owner) {Local=new Vector3(.3f,2,.7f)};
        Check(Near(point.Position,new Vector3(5.7f,5,6.7f)),"numerical wire endpoint retains root rotation and local hierarchy offset");
        var wire=new UtilityPoleWire();wire.Set(new Vector3(1,2,3),new Vector3(5,2,7),.025f,.18f,8,Color.gray,true);
        Check(wire.Segments==8 && Near(wire.Point(0),wire.Start) && Near(wire.Point(8),wire.End),"curve keeps both attachment endpoints");
        Check(Near(wire.Point(4),new Vector3(3,1.82f,5)),"parabolic sag matches original line renderer");
        int version=UtilityPoleWireRenderer.Invalidations;wire.Set(wire.Start,wire.End,wire.Width,wire.Sag,8,wire.Color,true);
        Check(UtilityPoleWireRenderer.Invalidations==version,"unchanged wire requests do not rebuild meshes");
        wire.SetVisible(false);Check(!wire.Visible && UtilityPoleWireRenderer.Invalidations==version+1,"preview hide invalidates presentation only");
        wire.Dispose();Check(UtilityPoleWireRenderer.Wires.Count==0,"removed wires release registry references");
        Console.WriteLine($"PASS {count} pole lifecycle/identity/save dispatch/numerical wire checks (actual world and terminal sources)");
    }
}
