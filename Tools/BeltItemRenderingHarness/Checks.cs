using System;
using System.Collections.Generic;
using ProjectF.Rendering;
using UnityEngine;

namespace Unity.Burst
{
    public enum FloatMode { Fast }
    public enum FloatPrecision { Standard }
    public enum OptimizeFor { Performance }
    [AttributeUsage(AttributeTargets.Struct)] public class BurstCompileAttribute : Attribute
    { public FloatMode FloatMode; public FloatPrecision FloatPrecision; public OptimizeFor OptimizeFor; }
}
namespace Unity.Collections
{
    public enum Allocator { Persistent }
    public enum NativeArrayOptions { UninitializedMemory }
    [AttributeUsage(AttributeTargets.Field)] public class ReadOnlyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class WriteOnlyAttribute : Attribute { }
    public struct NativeArray<T> where T : struct
    {
        private T[] data;
        public NativeArray(int count, Allocator allocator, NativeArrayOptions options) { data = new T[count]; }
        public bool IsCreated => data != null;
        public int Length => data.Length;
        public T this[int index] { get => data[index]; set => data[index] = value; }
        public void Dispose() => data = null;
    }
}
namespace Unity.Jobs
{
    public interface IJobParallelFor { void Execute(int index); }
    public struct JobHandle { public void Complete() { } }
    public static class Scheduling
    {
        public static JobHandle Schedule<T>(this T job, int count, int batch) where T : struct, IJobParallelFor
        { System.Threading.Tasks.Parallel.For(0,count,job.Execute); return default; }
    }
}
namespace ProjectF.Conveyors
{
    public struct BeltLaneState { public int ItemId, Origin; public long Remaining; }
}
public class PortableObject { public bool IsMovingToTarget, HasActiveOutline; }
public class GameManager { public static GameManager Instance = new(); public bool ShowSleepAwake, ShowBeltItemLine; }
public class TerrainGenerator
{
    public int Reads, Paths, ProgressReads;
    public ProjectF.Conveyors.BeltLaneState[] States = new ProjectF.Conveyors.BeltLaneState[4];
    internal bool TryReadBeltJobLaneForRendering(Block block, int lane, out ProjectF.Conveyors.BeltLaneState state)
    { Reads++; state = States[lane]; return true; }
    internal float GetBeltJobVisualProgress(Block block,int lane,ProjectF.Conveyors.BeltLaneState state)
    { ProgressReads++; return 0.25f; }
    internal BeltItemVisualPath GetBeltJobVisualPath(Block block,int lane,ProjectF.Conveyors.BeltLaneState state,ref BeltItemVisualPathCache cache)
    { Paths++; cache.RequiresSurface=block.Raised; cache.RotateOnSurface=block.Raised && !block.Bridge; return BeltItemVisualPath.Line(new Vector3(0,0,0),new Vector3(4,0,0)); }
}
public partial class Block
{
    public const int ConveyorCellItemUnit = 4;
    private const int ConveyorStackLaneLimit = 4;
    private const int ConveyorSingleLineBackLaneIndex = 2;
    internal TerrainGenerator beltJobOwner = new();
    public bool Raised, Bridge; public int LegacyAppends, HeightCorrections, Rotations, Suppressions;
    public bool Corner, Via, ValidCorner = true;
    private bool IsCornerConveyor() => Corner;
    private bool TryGetConveyorCornerArcParameters(int source,int target,out Vector2 center,out float start,out float delta,out float radius)
    { center = new Vector2(2,3); start=0; delta=MathF.PI/2; radius=0.4f; return ValidCorner; }
    private Vector3 BlockLocalToWorld(Vector3 position) => position + new Vector3(10,0,20);
    private float GetConveyorLaneHeight() => 0.3f;
    private Vector3 GetConveyorLaneWorldPosition(int lane) => new Vector3(lane,1,0);
    private Vector3 GetDefaultConveyorLaneWorldPosition(int lane) => GetConveyorLaneWorldPosition(lane);
    private bool TryGetConveyorLinearMoveViaWorldPosition(int lane,Block target,int targetLane,Vector3 from,out Vector3 via)
    { via = new Vector3(0,1,2); return Via; }
    public int RuntimeLayer => 2;
    public PortableObject[] Portable = new PortableObject[4];
    private PortableObject GetConveyorPortableObjectAtLane(int lane) => Portable[lane];
    private void ApplyConveyorObjectVirtualRenderingSuppressionIfNeeded(PortableObject item) => Suppressions++;
    private Vector3 ConformConveyorItemToBelt2FPath(int lane,Vector3 position)
    { HeightCorrections++; position.y = 3; return position; }
    private Quaternion GetConveyorItemVisualWorldRotation(int lane,Vector3 position)
    { Rotations++; return new Quaternion(0,0.70710677f,0,0.70710677f); }
    private bool TryGetBeltItemLineDebugColorFast(TerrainGenerator terrain,bool show,int lane,out Color32 color)
    { color = new Color32(1,2,3,255); return show; }
    private bool IsConveyorItemSleepAwakeSleeping(int lane) => lane==0;
    public void AppendDynamicVirtualConveyorItemRenderData(List<VirtualConveyorItemRenderData> results) => LegacyAppends++;
}
public interface IVirtualRenderBatchOwner { int BatchEntryCount { get; } void UpdateBatchEntryMatrixIndex(int entry,int index); }
public readonly record struct VirtualRenderBatchKey(int ItemId, int CellX, int CellZ, int Layer, bool Tint, bool Line);
public struct VirtualRenderBatchEntry { public VirtualRenderBatchKey BatchKey; public int MatrixIndex; }
class RecordingBatches
{
    internal int Adds, Removes, Updates;
    internal void AddOwnedMatrix(IVirtualRenderBatchOwner owner,List<VirtualRenderBatchEntry> entries,VirtualRenderBatchKey key,Matrix4x4 matrix)
    { Adds++; entries.Add(new VirtualRenderBatchEntry { BatchKey=key, MatrixIndex=entries.Count }); }
    internal bool TryUpdateOwnedMatrix(List<VirtualRenderBatchEntry> entries,int index,VirtualRenderBatchKey key,Matrix4x4 matrix)
    { if(entries[index].BatchKey != key) return false; Updates++; return true; }
    internal void Remove(List<VirtualRenderBatchEntry> entries) { Removes+=entries.Count; entries.Clear(); }
}
public partial class PortableItemRenderer
{
    private readonly ConveyorItemTransformJobProcessor conveyorItemTransformJobProcessor = new();
    private readonly RecordingBatches dynamicVirtualConveyorBatches = new();
    private float virtualConveyorItemBatchCellSize = 64;
    private int lastDynamicVirtualConveyorMatrixUpdates, lastDynamicVirtualConveyorMatrixRebuilds,
        lastDynamicVirtualConveyorKeyCacheMisses, lastDynamicVirtualConveyorKeyCacheHits, lastDynamicVirtualConveyorKeyRebuilds;
    private void RemoveDynamicVirtualConveyorBlockBatchEntries(DynamicBlockRenderCache cache) => dynamicVirtualConveyorBatches.Remove(cache.batchEntries);
    private bool TryCreateVirtualConveyorBatchKey(VirtualConveyorItemRenderData data,out VirtualRenderBatchKey key)
    { ResolveBatchCell(data,out int x,out int z); return TryCreateVirtualConveyorBatchKey(data,x,z,out key); }
    private bool TryCreateVirtualConveyorBatchKey(VirtualConveyorItemRenderData data,int x,int z,out VirtualRenderBatchKey key)
    { key=new(data.ItemId,x,z,data.Layer,data.UseSleepAwakeDarkTint,data.UseBeltItemLineDebugColor); return data.ItemId != 999; }
    public static void Run()
    {
        using var processor = new ConveyorItemTransformJobProcessor();
        var items = new List<VirtualConveyorItemRenderData>();
        for(int i=0;i<200;i++)
        {
            var path=BeltItemVisualPath.Line(new Vector3(-65+i,2,-1),new Vector3(-63+i,2,1));
            items.Add(new VirtualConveyorItemRenderData(i,path.End,Quaternion.identity,0,false,visualPath:path,visualProgress:0.25f));
        }
        processor.ScheduleMatrices(items,false,128,64);
        var expected=new Matrix4x4[items.Count];
        for(int i=0;i<items.Count;i++)
        {
            Checks.Require(processor.TryGetResult(i,out var m,out int x,out int z),"main-thread kernel returns pose"); expected[i]=m;
            var p=items[i].VisualPath.Evaluate(0.25f);
            Checks.Require(Math.Abs(m.m03-p.x)<0.00001f && Math.Abs(m.m13-p.y)<0.00001f && Math.Abs(m.m23-p.z)<0.00001f,"path evaluation reaches matrix translation");
            Checks.Require(x==Mathf.FloorToInt(p.x/64) && z==Mathf.FloorToInt(p.z/64),"batch cell uses evaluated position, including negative coordinates");
        }
        Checks.Require(processor.NativePathItemCount==200,"native path counter counts deferred transforms");
        Checks.Require(processor.ScheduleMatrices(items,true,128,64),"large visible set schedules parallel transform kernel");
        Checks.Require(!processor.TryGetResult(0,out _,out _,out _),"results cannot be consumed before completion");
        processor.CompleteScheduled();
        for(int i=0;i<items.Count;i++)
        { processor.TryGetResult(i,out var m,out _,out _); Checks.Require(m==expected[i],"serial and scheduled kernel results agree"); }
        items.Clear(); items.Add(new VirtualConveyorItemRenderData(1,new Vector3(2,3,4),new Quaternion(0,0.70710677f,0,0.70710677f),0,false));
        processor.ScheduleMatrices(items,false,128,64); processor.TryGetResult(0,out var rotated,out _,out _);
        Checks.Require(rotated.m03==2 && rotated.m13==3 && rotated.m23==4 && Math.Abs(rotated.m02-1)<0.00001f,"resolved high-belt pose preserves rotation and height");
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) processor.ScheduleMatrices(items,false,128,64);
        Checks.Require(GC.GetAllocatedBytesForCurrentThread()==allocated,"warmed transform preparation has zero managed allocation");
        items.Clear(); processor.ScheduleMatrices(items,true,128,64);
        Checks.Require(processor.NativePathItemCount==0 && !processor.TryGetResult(0,out _,out _,out _),"empty set clears stale transform outputs and counters");

        var block=new Block(); for(int i=0;i<4;i++) block.beltJobOwner.States[i].ItemId=-1;
        block.beltJobOwner.States[0]=new ProjectF.Conveyors.BeltLaneState { ItemId=4,Origin=2,Remaining=2 };
        var caches=new BeltItemVisualPathCache[4];
        block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(block.beltJobOwner.Reads==4 && block.beltJobOwner.Paths==1 && block.beltJobOwner.ProgressReads==1,"native append reads every slot once, prepares only occupied slot");
        Checks.Require(items.Count==1 && items[0].VisualPath.Kind==1 && block.LegacyAppends==0,"ordinary native belt defers interpolation without legacy storage");
        items.Clear(); block.Raised=true; block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(items[0].VisualPath.Kind==0 && items[0].Position.y==3 && block.Rotations==1,"high belt keeps conformed surface pose");
        items.Clear(); block.beltJobOwner.States[0].Origin=-1; block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(items[0].Position.y==0 && block.HeightCorrections==1,"external arrival retains jump height until settled");
        items.Clear(); block.Bridge=true; block.beltJobOwner.States[0].Origin=2; block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(items[0].Rotation==Quaternion.identity && items[0].Position.y==3,"bridge slot keeps existing identity rotation and corrected height");
        items.Clear(); block.Portable[0]=new PortableObject { HasActiveOutline=true }; block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(items.Count==0 && block.Suppressions==1,"outlined proxy is not rendered twice");
        block.Portable[0].HasActiveOutline=false; block.Portable[0].IsMovingToTarget=true; block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(items.Count==0 && block.Suppressions==1,"proxy in external flight is excluded");
        block.Portable[0]=null; GameManager.Instance.ShowSleepAwake=GameManager.Instance.ShowBeltItemLine=true;
        block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(items[0].UseSleepAwakeDarkTint && items[0].UseBeltItemLineDebugColor,"native append preserves debug views");
        block.beltJobOwner=null; block.AppendDynamicVirtualConveyorItemRenderData(items,caches);
        Checks.Require(block.LegacyAppends==1,"unbound belt retains legacy append path");
        var corner=new Block { Corner=true };
        var arc=corner.CaptureBeltJobVisualPath(2,corner,0);
        Checks.Require(arc.Kind==3 && arc.Start==new Vector3(12,0.3f,23),"corner capture retains translated arc center and lane height");
        Checks.Require(Vector3.Distance(arc.Evaluate(0),new Vector3(12.4f,0.3f,23))<0.00001f,"corner descriptor retains endpoint");
        corner.ValidCorner=false; arc=corner.CaptureBeltJobVisualPath(2,corner,0);
        Checks.Require(arc.Start==arc.End && arc.End==new Vector3(0,1,0),"invalid corner parameters retain legacy fixed-position fallback");
        var from=new Block { Via=true }; var viaPath=from.CaptureBeltJobVisualPath(2,corner,0);
        Checks.Require(viaPath.Kind==2 && viaPath.Start==new Vector3(2,1,0) && viaPath.End==new Vector3(0,1,0),"side approach caches both segments with their existing lane endpoints");

        var renderer=new PortableItemRenderer(); var cache=new DynamicBlockRenderCache();
        void Sync(int id,float x)
        {
            items.Clear(); items.Add(new VirtualConveyorItemRenderData(id,new Vector3(x,0,0),Quaternion.identity,0,false));
            renderer.conveyorItemTransformJobProcessor.ScheduleMatrices(items,false,128,64);
            renderer.SyncDynamicVirtualConveyorBlockRenderItems(cache,items,0,items.Count);
        }
        Sync(10,1); cache.isValid=true;
        for(int i=0;i<1000;i++) Sync(10,1+i*0.001f);
        Checks.Require(renderer.dynamicVirtualConveyorBatches.Adds==1 && renderer.dynamicVirtualConveyorBatches.Removes==0 && renderer.dynamicVirtualConveyorBatches.Updates==1000,"unchanged render identity keeps batch entry across 1000 motion updates");
        Sync(11,2); Checks.Require(cache.batchEntries[0].BatchKey.ItemId==11 && renderer.dynamicVirtualConveyorBatches.Removes==1,"changed item identity rebuilds batch");
        Sync(11,65); Checks.Require(cache.batchEntries[0].BatchKey.CellX==1,"cross-cell move reassigns batch correctly");
        items.Clear(); renderer.conveyorItemTransformJobProcessor.ScheduleMatrices(items,false,128,64);
        renderer.SyncDynamicVirtualConveyorBlockRenderItems(cache,items,0,0);
        Checks.Require(cache.batchEntries.Count==0,"vacated slot removes stale instance");
        Sync(999,1); Checks.Require(cache.batchEntries.Count==0,"missing render asset never retains stale instance");
        Sync(12,1); Checks.Require(cache.batchEntries.Count==1 && cache.batchEntries[0].BatchKey.ItemId==12,"new valid item recovers after missing asset");
        renderer.conveyorItemTransformJobProcessor.Dispose();
    }
}
static class Checks
{
    static int checks;
    internal static void Require(bool condition,string message)
    { checks++; if(!condition) throw new Exception(message); }
    public static void Main()
    { PortableItemRenderer.Run(); Console.WriteLine($"PASS: {checks} belt-item append/transform/batch reuse checks; production methods, engine scheduling/render boundaries doubled. No engine launched."); }
}
