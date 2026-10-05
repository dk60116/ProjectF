using System;
using System.Collections.Generic;
using UnityEngine;

// Actual terrain queries and block stack consumption run against managed world/marker boundaries.
public partial class Block
{
    public enum BlockType { Ground, Water }
    public BlockType Type;
    public bool BoxContent;
}
public partial class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> loadedBlocks = new();
    private static Vector2Int GetWorldBlockCoordinate(Vector3 position) =>
        new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
}
namespace ProjectF.MapObjects
{
public interface IWorkableTarget
{
    bool IsTargetActive { get; }
    bool TryGetWorkableRangeBounds(out Bounds bounds);
}
}
public sealed class WorkableObject : ProjectF.MapObjects.IWorkableTarget
{
    public bool isActiveAndEnabled = true;
    public bool IsTargetActive => isActiveAndEnabled;
    public Bounds Range;
    public bool TryGetWorkableRangeBounds(out Bounds bounds) { bounds=Range; return true; }
    public bool ContainsWorldPositionInOwnWorkableRange(Vector3 position) =>
        position.x >= Range.min.x && position.x <= Range.max.x
        && position.z >= Range.min.z && position.z <= Range.max.z;
}
public static class InputOutputModuleOutputAreaController
{
    public static readonly HashSet<Vector2Int> Areas = new();
    public static bool CoordinateIsOutputArea(Vector2Int coordinate) => Areas.Contains(coordinate);
}
static class WorkableChecks
{
    private static int checks;
    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    private static Block Stock(TerrainGenerator terrain, int x, int floor, int center, bool output=true)
    {
        var block=new Block(new Vector2Int(x,0));
        terrain.loadedBlocks.Add(block.Coordinate,block);
        if (output) InputOutputModuleOutputAreaController.Areas.Add(block.Coordinate);
        for (int i=0;i<floor;i++)
            Require(block.TryAddDeferredOutput(1,block.WorldPosition,0,false,out _),"floor stock added");
        for (int i=0;i<center;i++)
            Require(block.TryAddDeferredOutput(1,block.WorldPosition,0,true,out _),"center stock added");
        return block;
    }
    public static void Run()
    {
        TestClock.time=100;
        InputOutputModuleOutputAreaController.Areas.Clear();
        var terrain=new TerrainGenerator();
        var sources=new List<WorkableObject>
        {
            new() { Range=new Bounds(Vector3.zero,new Vector3(8,2,2)) },
            new() { Range=new Bounds(new Vector3(2,0,0),new Vector3(4,2,2)) }
        };
        var near=Stock(terrain,0,2,3);
        var far=Stock(terrain,3,2,4);
        var inputOnly=Stock(terrain,-3,0,5,false);
        var outside=Stock(terrain,8,0,6);
        var box=Stock(terrain,2,0,7); box.mapObject=new BoxObject(); box.BoxContent=true;
        var nonAnchorBox=Stock(terrain,4,0,8); nonAnchorBox.BoxContent=true;
        int wrappers=TestItemPool.Created;
        int nearby=terrain.GetDroppedItemCountAround(Vector3.zero,1,1);
        int workable=terrain.GetWorkableAreaItemCount(sources,Vector3.zero,1,1);
        Require(nearby==2 && workable==9,"nearby floor excluded once; nearby/far output stacks included once across overlapping ranges");
        Require(TestItemPool.Created==wrappers,"workable count preserves compact stock without item wrappers");
        Require(terrain.GetWorkableAreaItemCount(sources,Vector3.zero,1,2)==0,"unrelated item IDs excluded");
        Require(terrain.RemoveDroppedItemsAround(Vector3.zero,1,1,2)==2,"nearby floor consumed first");
        Require(terrain.RemoveWorkableAreaItems(sources,Vector3.zero,1,1,6)==6,"mixed output/floor stock fulfills exact request");
        Require(near.GetInputAreaCenterItemCount(1)==0 && far.CountFloorObjects(1)==0
            && far.GetInputAreaCenterItemCount(1)==3,"same counted stocks are consumed");
        Require(TestItemPool.Created==wrappers,"workable consumption creates no item wrappers");
        Require(box.GetInputAreaCenterItemCount(1)==7 && nonAnchorBox.GetInputAreaCenterItemCount(1)==8,
            "boxes on output coordinates remain handled by their retained-count source path");
        Require(inputOnly.GetInputAreaCenterItemCount(1)==5 && outside.GetInputAreaCenterItemCount(1)==6,
            "input-only and out-of-range output stock remains untouched");
        Require(terrain.RemoveWorkableAreaItems(sources,Vector3.zero,1,1,100)==3
            && terrain.GetWorkableAreaItemCount(sources,Vector3.zero,1,1)==0,"short supply reports actual removal and count updates");
        Require(terrain.GetWorkableAreaItemCount(null,Vector3.zero,1,1)==0
            && terrain.RemoveWorkableAreaItems(sources,Vector3.zero,1,1,0)==0,"empty/invalid queries are harmless");
        // Also exercise materialized stock, not only compact deferred outputs.
        var visible=Stock(terrain,1,0,2); visible.MaterializeDeferredOutputs(int.MaxValue);
        Require(visible.inputAreaCenterStack.Count==2,"test materializes real managed item records");
        Require(terrain.GetWorkableAreaItemCount(sources,Vector3.zero,1,1)==2
            && terrain.RemoveWorkableAreaItems(sources,Vector3.zero,1,1,1)==1
            && visible.GetInputAreaCenterItemCount(1)==1,"materialized output stock uses the same source path");
        sources[0].isActiveAndEnabled=false; sources[1].isActiveAndEnabled=false;
        Require(terrain.GetWorkableAreaItemCount(sources,Vector3.zero,1,1)==0,"disabled workables expose no stock");
        var remote = Stock(terrain,1000000,0,3);
        sources.Add(new WorkableObject { Range = new Bounds(remote.WorldPosition, new Vector3(2,.01f,2)) });
        Require(terrain.GetWorkableAreaItemCount(sources,Vector3.zero,1,1)==3,
            "disconnected distant ranges query their own cells without scanning the enclosing rectangle");
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<1000;i++) terrain.GetWorkableAreaItemCount(sources,Vector3.zero,1,1);
        bytes = GC.GetAllocatedBytesForCurrentThread()-bytes;
        Require(bytes==0,"warmed union material queries allocate zero bytes");
        Console.WriteLine($"Workable output material checks passed: {checks}");
    }
}
