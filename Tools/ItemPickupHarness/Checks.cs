using System;
using System.Collections.Generic;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
    static PortableObject Item(float x, int id = 10, float y = 0) => new PortableObject { ItemId = id, transform = new Transform { position = new Vector3(x,y,0) } };
    static void Main()
    {
        var slot = new BagSlot();
        slot.ground = new Block { Item = Item(0.2f) };
        slot.clickedConveyor = new Block { Item = Item(2f) };
        Check(slot.Selected() == "Ground", "near ground item wins over explicitly focused conveyor");
        Check(slot.AutoItem() == slot.ground.Item, "automatic preview uses closest candidate");
        Check(slot.HoverItem() == slot.ground.Item, "slot preview uses same candidate");
        Check(slot.Click() && slot.Picked == "Ground" && slot.ExactItem == slot.ground.Item, "click picks the exact ground stack shown");
        slot.clickedConveyor.Item = Item(0.1f);
        Check(slot.Selected() == "Conveyor", "closer conveyor item remains pickable");
        slot.Click(); Check(slot.Picked == "Conveyor", "conveyor dispatch agrees with selection");
        slot.clickedConveyor = null;
        slot.clickedBox = new BoxObject();
        Check(slot.Selected() == "Ground", "empty clicked box cannot hide ground item");
        slot.clickedBox.Item = Item(2);
        Check(slot.Selected() == "Ground", "farther clicked box loses to ground item");
        slot.clickedBox.Item = Item(0.1f);
        Check(slot.Selected() == "Box", "closer box item wins");
        slot.Click(); Check(slot.Picked == "Box", "box dispatch agrees with selection");
        slot.clickedBox = null;
        slot.player.Controller.Arm = new RobotArm();
        Check(slot.Selected() == "Ground", "empty focused robot arm no longer blocks pickup");
        slot.player.Controller.Arm.HeldPortableObject = Item(0.05f);
        slot.player.Controller.Arm.CanTakeHeldItemFromSlot = false;
        Check(slot.Selected() == "Ground", "unavailable held item cannot block pickup");
        slot.player.Controller.Arm.CanTakeHeldItemFromSlot = true;
        Check(slot.Selected() == "RobotArm", "nearer available robot arm item wins");
        slot.Click(); Check(slot.player.Controller.Arm.Takes == 1, "robot arm dispatch agrees with selection");
        slot.player.Controller.Arm.HeldPortableObject = Item(3);
        Check(slot.Selected() == "Ground", "farther robot item loses to nearby ground item");
        slot.player.Controller.Arm = null;
        slot.storage = new Storage { Item = Item(0.1f) };
        Check(slot.Selected() == "Storage", "closest storage item participates in comparison");
        slot.Click(); Check(slot.Picked == "Storage", "storage dispatch agrees with selection");
        slot.storage = null;
        slot.conveyor = new Block { Item = Item(0.2f) };
        Check(slot.Selected() == "Ground", "ties favor ground consistently");
        slot.conveyor.Item = Item(0.1f, 11, 20);
        Check(slot.Selected() == "Conveyor", "distance comparison is horizontal, independent of stack height");
        slot.Rejected.Add(11);
        Check(slot.Selected() == "Ground", "manual selection skips items the target slot cannot accept");
        slot.Rejected.Clear();
        slot.Preferred = 10;
        Check(slot.Selected() == "Ground", "existing slot item filter is respected");
        slot.Preferred = -1;
        slot.ground = null;
        slot.conveyor = null;
        slot.clickedBox = new BoxObject();
        Check(slot.Selected() == "None" && !slot.Click(), "empty focus offers no phantom pickup");
        slot.storage = new Storage { Item = Item(1), HasPortablePreview = false };
        slot.storage.transform.position = new Vector3(0.1f,0,0);
        slot.ground = new Block { Item = Item(0.2f) };
        Check(slot.Selected() == "Storage", "storage without portable preview uses source position");
        slot.storage = null;
        slot.ground.Item = Item(0.2f);
        slot.HoverItem();
        slot.ground.Item = null;
        Check(!slot.Click(), "removed preview item is revalidated on click");
        Console.WriteLine($"PASS: {checks} production pickup selection/preview/dispatch checks. No engine launched.");
    }
}

public partial class BagSlot
{
    public Player player = new Player();
    public Block ground, clickedConveyor, conveyor;
    public BoxObject clickedBox, box;
    public Storage storage;
    public string Picked;
    public PortableObject ExactItem;
    public int Preferred = -1;
    public HashSet<int> Rejected = new HashSet<int>();
    object boundBag = new object(); int slotIndex = 0;
    const float FocusedPickupRange = 999;
    public string Selected() => TryResolvePickupCandidate(player,false,out var c) ? c.source.ToString() : "None";
    public PortableObject AutoItem() { TryResolveAutomaticPickupPreviewItem(out _,out _,out _,out var item); return item; }
    public PortableObject HoverItem() { TryResolvePickupPreviewItem(out _,out _,out var item); return item; }
    public bool Click() => TryHandlePickupClick();
    bool AllowPickupOnClick => true;
    bool IsInventoryUiLocked() => false;
    Player ResolvePlayer() => player;
    Vector3 ResolvePickupOrigin(Player p) => p.transform.position;
    int GetPreferredPickupItemId() => Preferred;
    TerrainGenerator ResolveTerrain() => new TerrainGenerator();
    Vector2Int ResolveStandingCoordinate(Player p) => default;
    bool TryGetGroundPickupBlock(TerrainGenerator t, Player p, Vector2Int c, out Block b) { b=ground; return b!=null; }
    float GetStandingTilePickupRange() => 999;
    bool TryGetClickedFocusedConveyorBlock(Player p,out Block b) { b=clickedConveyor; return b!=null; }
    bool TryGetFocusedConveyorBlock(Player p,out Block b) { b=conveyor; return b!=null; }
    bool TryGetClickedFocusedBoxObject(Player p,out BoxObject b) { b=clickedBox; return b!=null; }
    bool TryGetFocusedBoxObject(Player p,out BoxObject b) { b=box; return b!=null; }
    bool TryGetFocusedItemStorage(Player p,out IPlayerItemStorage s) { s=storage; return s!=null; }
    bool TryPreviewFocusedItemStorage(IPlayerItemStorage s,Player p,Vector3 origin,float range,int pref,out int id,out int count,out PortableObject obj)
    { var value=(Storage)s; bool ok=value.Preview(pref,out id,out count,out obj); if(!value.HasPortablePreview)obj=null; return ok; }
    bool CanPreviewAcceptPickupItem(Player p,int id) => !Rejected.Contains(id);
    bool ShouldDisplayPickupPreviewForItem(int id) => true;
    bool TryPickupGroundCandidate(Player p,Block b,PortableObject item,Vector3 origin,float range) { Picked="Ground"; ExactItem=item; return true; }
    bool TryPickupFocusedConveyorItem(Player p,Block b,float range,int max) { Picked="Conveyor"; return true; }
    bool TryPickupFromFocusedBox(Player p,BoxObject b,Vector3 origin,float range) { Picked="Box"; return true; }
    bool TryPickupFromFocusedItemStorage(Player p,IPlayerItemStorage s,Vector3 origin,float range) { Picked="Storage"; return true; }
    void SuppressPickupPreviewAfterPickup() { }
}
public class Source
{
    public PortableObject Item;
    public Transform transform = new Transform();
    public bool Preview(int preferred,out int id,out int count,out PortableObject portable)
    { portable=Item; id=Item?.ItemId??-1; count=Item!=null?1:0; return Item!=null && (preferred<0||preferred==id); }
}
public class MapObject : Source { }
public class PortableObject { public int ItemId; public Transform transform = new Transform(); }
public class Block : Source
{
    public Vector3 WorldPosition => transform.position;
    public bool TryPreviewPickupFloorObjects(Player p,Vector3 o,float r,int pref,out int id,out int count,out PortableObject obj) => Preview(pref,out id,out count,out obj);
    public bool TryPreviewPickupConveyorObjects(Player p,Vector3 o,float r,int pref,out int id,out int count,out PortableObject obj) => Preview(pref,out id,out count,out obj);
}
public interface IPlayerItemStorage { }
public class Storage : MapObject, IPlayerItemStorage { public bool HasPortablePreview=true; }
public class BoxObject : MapObject, IPlayerItemStorage
{
    public bool TryPreviewContainedObjectPickup(Player p,Vector3 o,float r,int pref,out int id,out int count,out PortableObject obj) => Preview(pref,out id,out count,out obj);
}
public class RobotArm : MapObject
{
    public PortableObject HeldPortableObject;
    public bool HasHeldItem => HeldPortableObject!=null;
    public bool CanTakeHeldItemFromSlot=true;
    public int HeldItemId => HeldPortableObject?.ItemId??-1;
    public int Takes;
    public bool TryTakeHeldItemToBag(object bag,int slot) { Takes++; return true; }
}
public class Player : MapObject
{
    public PlayerController Controller=new PlayerController();
    public T GetComponent<T>() where T:class => Controller as T;
}
public class PlayerController
{
    public RobotArm Arm;
    public bool TryGetFocusedRobotArm(out RobotArm arm) { arm=Arm; return arm!=null; }
}
public class TerrainGenerator { }
namespace UnityEngine
{
    public struct Vector2Int { }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public class Transform { public Vector3 position; }
}
