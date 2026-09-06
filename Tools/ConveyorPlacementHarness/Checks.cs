using System;
using System.Collections.Generic;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool ok,string message) { checks++; if(!ok)throw new Exception(message); }
    static void Main()
    {
        foreach(bool virtualized in new[]{false,true})
        {
            Time.time=100f;
            var block=new Block { Virtualized=virtualized };
            Check(block.Place(0.18f),"robot-arm insertion succeeds");
            var visual=block.Item;
            Check(!block.CanMove(),"item cannot move during placement animation");
            Time.time=100.179f; // Tween completion may precede the nominal Time.time deadline.
            visual.Complete();
            Check(block.CanMove(),"landing must release the hold and invalidate cached not-ready state");
            Check(block.LandingWakes==1 && block.Completions==1,"landing schedules exactly one targeted retry and completion");
            Check((block.Item==null)==virtualized,"virtual and materialized rendering retain identical movement readiness");
        }

        Time.time=200f;
        var occupied=new Block { DestinationBlocked=true };
        occupied.Place(0.18f);
        occupied.CanMove();
        Time.time+=0.18f;
        occupied.Item.Complete();
        Check(!occupied.CanMove(),"landing wake cannot bypass actual destination occupancy");
        occupied.DestinationBlocked=false;
        occupied.ExternalVacancyWake();
        Check(occupied.CanMove(),"normal vacancy wake allows departure once destination is free");

        var held=new Block();
        held.Place(1f);
        var heldVisual=held.Item;
        Time.time+=0.18f;
        heldVisual.Complete();
        Check(held.Held && !held.CanMove(),"explicit hold longer than animation is preserved");

        var snap=new Block();
        snap.Place(1f,snap:true);
        Check(snap.Held && snap.Completions==1,"immediate placement retains explicitly requested hold");
        var fast=new Block { Virtualized=true };
        fast.Place(0f,snap:true);
        Check(fast.CanMove() && fast.Item==null,"data-only immediate placement still moves without a visual tween");

        var removed=new Block();
        removed.Place(0.18f);
        var oldVisual=removed.Item;
        var replacement=new PortableObject();
        removed.Item=replacement;
        oldVisual.Complete();
        Check(removed.LandingWakes==0 && removed.Item==replacement && removed.Completions==1,
            "stale completion cannot alter or release a replacement item");

        Time.time=300f;
        var cached=new Block();
        cached.Place(0f);
        Check(!cached.CanMove(),"animation readiness can be cached even without a timed hold");
        Time.time+=0.18f;
        cached.Item.Complete();
        Check(cached.CanMove(),"landing invalidates the animation-only failure cache");
        Console.WriteLine($"PASS: {checks} production conveyor placement checks; tween/world boundaries are managed doubles. No engine launched.");
    }
}
public partial class Block
{
    public bool Virtualized, DestinationBlocked;
    public PortableObject Item;
    public int LandingWakes, Completions;
    bool occupied;
    static int version;
    int cachedVersion=-1;
    bool cachedReady;
    readonly List<float> conveyorItemMovementHoldUntilTimes=new List<float>{0f};
    readonly Pool floorObjectPool=new Pool();
    object floorObjectPrefab=new object();
    Transform RuntimeObjectRoot=new Transform();
    public bool Held=>IsConveyorLaneMovementHeld(0);
    public bool Place(float hold,bool snap=false) => TryAddConveyorObjectAnimatedWithPlacementReference(
        1,default,default,0f,out _,()=>Completions++,snap?null:()=>default,hold,false,0.18f);
    public bool CanMove()
    {
        if(cachedVersion!=version) { cachedVersion=version; cachedReady=occupied && !Held && (Item==null||!Item.IsMovingToTarget); }
        return cachedReady && !DestinationBlocked;
    }
    void EnsureFloorObjectsInitialized() { }
    void CleanupConveyorStack() { }
    bool ShouldUseVirtualConveyorItemRendering()=>Virtualized;
    bool ShouldSnapConveyorPlacementImmediately(float delay,Func<Vector3> provider)=>delay<=0 && provider==null;
    bool IsConveyorStackingEnabled()=>true;
    bool ResolveFloorObjectPool()=>true;
    bool TryGetBestConveyorPlacementLaneIndex(Vector3 pos,out int lane) { lane=0; return !occupied; }
    void SetConveyorItemAtLane(int lane,int id,PortableObject obj,ConveyorPickupGateState gate)
    { Item=obj; occupied=true; InvalidateConveyorCanMoveCaches(); }
    void NotifyRuntimeItemStackChanged() { }
    void WakeConveyorMoveAttempts() { }
    void RefreshConveyorActivityRegistration() { }
    bool TryInitializePooledPortableObject(PortableObject obj,int id)=>obj!=null;
    void ConfigureConveyorObjectTransform(PortableObject obj,int lane) { }
    void ApplyConveyorObjectRenderingMode(PortableObject obj) { }
    bool TryVirtualizeSettledConveyorPortableObject(int lane,PortableObject obj)
    { if(Virtualized)Item=null; return Virtualized; }
    void MarkConveyorItemVisualDirty() { }
    Vector3 GetConveyorLaneWorldPosition(int lane)=>default;
    PortableObject GetConveyorPortableObjectAtLane(int lane)=>Item;
    static void InvalidateConveyorCanMoveCaches(bool invalidatePlanFailures=true) { version++; }
    bool WakeConveyorBlockedLaneWaiter(int lane) { LandingWakes++; return true; }
    public void ExternalVacancyWake()=>InvalidateConveyorCanMoveCaches();
}
public class Pool { public PortableObject Get(object prefab)=>new PortableObject(); }
public class PortableObject
{
    public const float MoveToDuration=0.18f;
    public Transform transform=new Transform();
    public GameObject gameObject=new GameObject();
    public bool IsMovingToTarget;
    Action completion;
    public void Complete() { IsMovingToTarget=false; completion?.Invoke(); }
    public void MoveTo(Func<Vector3> target,float delay,Func<Vector3> start,Action callback,bool deactivate,bool jump,float duration,bool track)
    { IsMovingToTarget=true; completion=callback; }
    public void SetBatchedRendering(bool enabled) { }
    public DroppedItemPickupGate GetOrAddPickupGate()=>new DroppedItemPickupGate();
}
public class DroppedItemPickupGate { public void MarkSettled() { } }
public struct ConveyorPickupGateState { public static ConveyorPickupGateState Settled()=>default; }
public class TerrainGenerator { public static TerrainGenerator Active; public void NotifyConveyorItemAddedToBelt() { } }
namespace UnityEngine
{
    public static class Time { public static float time; }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
    public struct Vector3 { public static Vector3 one=>default; }
    public struct Quaternion { public static Quaternion identity=>default; }
    public class Transform { public Vector3 position,localScale; public Quaternion rotation; public void SetParent(Transform parent,bool world) { } }
    public class GameObject { public void SetActive(bool active) { } }
}
