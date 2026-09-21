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
        var standingRangeSlot = new BagSlot { ground = new Block { Item = Item(0.6f) } };
        Check(standingRangeSlot.Selected() == "None",
            "standing-area item outside pickup range stays unfocused");
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
        slot.player.Controller.Arm = new RobotArmInstance();
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
        slot.ground = null;
        slot.terrain.Adjacent = new Block { Item = Item(0.4f) };
        Check(slot.AutoItem() == slot.terrain.Adjacent.Item, "nearby adjacent-tile stack receives automatic focus");
        slot.terrain.Adjacent.Item = Item(0.6f);
        Check(slot.Selected() == "None", "adjacent-tile stack outside pickup range stays unfocused");
        var movingGate = new DroppedItemPickupGate(Item(0f));
        movingGate.MarkDropped(0.5f, false, new Vector3(0f, 0f, 0f));
        Check(movingGate.CanManualPreview(0f, 0.25f), "moving dropped item is immediately focus-previewable");
        Check(!movingGate.CanManualPickup(0f, 0.25f), "moving dropped item remains unavailable for pickup");
        movingGate.MarkSettled();
        Check(movingGate.CanManualPickup(0f, 0.25f), "settled dropped item becomes pickup-ready");
        var unboundSource = new BagSlot { Bound = false, ground = new Block { Item = Item(0.1f) } };
        var emptySource = new BagSlot();
        var validSource = new BagSlot { ground = new Block { Item = Item(0.2f) } };
        Check(BagSlot.ResolveAutomaticSource(unboundSource, validSource) == validSource.ground.Item,
            "non-bag HUD slots cannot intercept automatic pickup preview");
        Check(BagSlot.ResolveAutomaticSource(emptySource, validSource) == validSource.ground.Item,
            "an empty bag slot cannot stop automatic preview source scanning");
        var immediateSource = new BagSlot { ground = new Block { Item = Item(0.1f) } };
        var immediateTarget = new BagSlot { IsPreviewTarget = true };
        BagSlot.SetAutomaticPreviewSlots(immediateSource, immediateTarget);
        BagSlot.SetHoveredSlot(immediateSource);
        BagSlot.RefreshAutomaticPreview(false);
        Check(immediateTarget.PreviewedItem == null, "ordinary automatic preview waits while a slot is hovered");
        BagSlot.RefreshAutomaticPreview(true);
        Check(immediateTarget.PreviewedItem == immediateSource.ground.Item,
            "successful drop forces immediate preview without waiting for another hover event");
        immediateTarget.PreviewedItem = null;
        BagSlot.SetHoveredSlot(new BagSlot { Bound = false });
        BagSlot.RefreshAutomaticPreview(false);
        Check(immediateTarget.PreviewedItem == immediateSource.ground.Item,
            "full-screen HUD hover cannot suppress nearby pickup outline");
        BagSlot.ResetAutomaticPreviewState();
        CheckGroundPickupRange();
        CheckOutlineVisibilityLifecycle();
        Console.WriteLine($"PASS: {checks} production pickup selection/preview/dispatch checks. No engine launched.");
    }

    static void CheckGroundPickupRange()
    {
        foreach (bool inputArea in new[] { false, true })
        foreach (bool hasGate in new[] { false, true })
        {
            var item = Item(0.6f);
            if (hasGate) item.Gate = new DroppedItemPickupGate(item);
            var block = new Block();
            if (inputArea) block.inputAreaCenterStack.Add(item); else block.Item = item;
            var slot = new BagSlot { ground = block, IsPreviewTarget = true };
            string label = $"inputArea={inputArea}, gate={hasGate}";
            Check(slot.AutoItem() == null && slot.HoverItem() == null && !slot.Click(),
                "out-of-range stack cannot preview or dispatch pickup: " + label);
            Check(!block.CanTake(slot.player, 0.5f),
                "production manual pickup rejects out-of-range stack: " + label);

            item.transform.position = new Vector3(0.5f, 10f, 0f);
            Check(slot.AutoItem() == item && block.CanTake(slot.player, 0.5f),
                "range boundary remains inclusive and ignores stack height: " + label);
            BagSlot.SetAutomaticPreviewSlots(slot);
            BagSlot.RefreshAutomaticPreview(false);
            Check(slot.PreviewedItem == item, "in-range stack requests outline: " + label);
            item.transform.position = new Vector3(0.501f, 10f, 0f);
            BagSlot.RefreshAutomaticPreview(false);
            Check(slot.PreviewedItem == null, "leaving range clears outline: " + label);
            BagSlot.ResetAutomaticPreviewState();

            if (hasGate)
            {
                item.transform.position = new Vector3(0.2f, 0f, 0f);
                item.Gate.MarkDropped(0.5f, false, Vector3.zero);
                Check(slot.AutoItem() == item && !block.CanTake(slot.player, 0.5f),
                    "in-range moving drop previews but cannot be taken: " + label);
                item.Gate.MarkSettled();
                Check(block.CanTake(slot.player, 0.5f), "settled drop can be taken: " + label);
            }
        }
    }

    static void CheckOutlineVisibilityLifecycle()
    {
        var pickup = new Renderer { enabled = false };
        AnimalScreenSpaceOutline.ShowPickup(pickup);
        Check(AnimalScreenSpaceOutline.ActiveRenderer == null, "delayed drop does not draw while hidden");
        pickup.enabled = true;
        Check(AnimalScreenSpaceOutline.ActiveRenderer == pickup,
            "drop outline resumes when delay ends without another slot hover");
        AnimalScreenSpaceOutline.HidePickup(pickup);

        var focused = new Renderer();
        var hovered = new Renderer();
        AnimalScreenSpaceOutline.ShowFocused(focused);
        AnimalScreenSpaceOutline.ShowHovered(hovered);
        focused.gameObject.activeInHierarchy = false;
        Check(AnimalScreenSpaceOutline.ActiveRenderer == hovered, "hidden focus allows visible hover fallback");
        focused.gameObject.activeInHierarchy = true;
        Check(AnimalScreenSpaceOutline.ActiveRenderer == focused, "retained focus resumes after visibility returns");
        AnimalScreenSpaceOutline.ShowPickup(pickup);
        Check(AnimalScreenSpaceOutline.ActiveRenderer == pickup, "pickup still takes precedence over focused outline");
        pickup.enabled = false;
        Check(AnimalScreenSpaceOutline.ActiveRenderer == focused, "hidden pickup allows focused fallback");
        AnimalScreenSpaceOutline.HidePickup(pickup);
        pickup.enabled = true;
        Check(AnimalScreenSpaceOutline.ActiveRenderer == focused, "explicitly cleared hidden pickup does not reappear");
        AnimalScreenSpaceOutline.HideFocused(focused);
        hovered.enabled = false;
        Check(AnimalScreenSpaceOutline.ActiveRenderer == null, "hidden hover does not draw");
        hovered.enabled = true;
        Check(AnimalScreenSpaceOutline.ActiveRenderer == hovered, "hover resumes without target change");
        AnimalScreenSpaceOutline.HideHovered(hovered);
        Check(AnimalScreenSpaceOutline.ActiveRenderer == null, "clearing all requests removes the outline");
    }
}

public class ItemSlot { }

public partial class BagSlot : ItemSlot
{
    static readonly List<BagSlot> activeBagSlots = new List<BagSlot>();
    static BagSlot hoveredDropSlot;
    static BagSlot automaticPickupPreviewSlot;
    static int automaticPickupPreviewFrame;
    static int automaticPickupPreviewPriority;
    public Player player = new Player();
    public Block ground, clickedConveyor, conveyor;
    public BoxObject clickedBox, box;
    public Storage storage;
    public TerrainGenerator terrain = new TerrainGenerator();
    public string Picked;
    public PortableObject ExactItem;
    public int Preferred = -1;
    public bool Bound = true;
    public bool IsPreviewTarget;
    public PortableObject PreviewedItem;
    public HashSet<int> Rejected = new HashSet<int>();
    object boundBag = new object(); int slotIndex = 0;
    const float FocusedPickupRange = 999;
    public string Selected() => TryResolvePickupCandidate(player,false,out var c) ? c.source.ToString() : "None";
    public PortableObject AutoItem() { TryResolveAutomaticPickupPreviewItem(out _,out _,out _,out var item); return item; }
    public PortableObject HoverItem() { TryResolvePickupPreviewItem(out _,out _,out var item); return item; }
    public static PortableObject ResolveAutomaticSource(params BagSlot[] slots)
    {
        activeBagSlots.Clear();
        activeBagSlots.AddRange(slots);
        bool found = TryResolveAutomaticPickupPreviewSource(out _, out _, out _, out _, out PortableObject item);
        activeBagSlots.Clear();
        return found ? item : null;
    }
    public static void SetAutomaticPreviewSlots(params BagSlot[] slots)
    {
        activeBagSlots.Clear();
        activeBagSlots.AddRange(slots);
    }
    public static void SetHoveredSlot(BagSlot slot) => hoveredDropSlot = slot;
    public static void RefreshAutomaticPreview(bool forceAfterDrop) =>
        RefreshAutomaticPickupPreviewFrame(forceAfterDrop);
    public static void ResetAutomaticPreviewState()
    {
        activeBagSlots.Clear();
        hoveredDropSlot = null;
        automaticPickupPreviewSlot = null;
    }
    public bool Click() => TryHandlePickupClick();
    bool AllowPickupOnClick => true;
    bool IsInventoryUiLocked() => false;
    static bool IsBoundDropSlot(BagSlot slot) => slot != null && slot.Bound;
    static bool IsPickupPreviewSuppressed() => false;
    static bool IsVisibleItemSlotForPointer(ItemSlot slot) => true;
    static bool HasDraggingBagSlot() => false;
    static void ClearAutomaticPickupPreviewSlot()
    {
        if (automaticPickupPreviewSlot != null) automaticPickupPreviewSlot.PreviewedItem = null;
        automaticPickupPreviewSlot = null;
    }
    static BagSlot FindAutomaticPickupPreviewTarget(BagSlot source, Player player, int itemId)
    {
        for (int i = 0; i < activeBagSlots.Count; i++)
            if (activeBagSlots[i].IsPreviewTarget) return activeBagSlots[i];
        return null;
    }
    int GetAutomaticPickupPreviewPriority() => 0;
    void SetPickupPreviewOutline(PortableObject item) => PreviewedItem = item;
    void ApplyPickupPreview(int itemId, int count) { }
    Player ResolvePlayer() => player;
    Vector3 ResolvePickupOrigin(Player p) => p.transform.position;
    int GetPreferredPickupItemId() => Preferred;
    TerrainGenerator ResolveTerrain() => terrain;
    Vector2Int ResolveStandingCoordinate(Player p) => new Vector2Int(0, 0);
    bool TryGetGroundPickupBlock(TerrainGenerator t, Player p, Vector2Int c, out Block b) { b=ground; return b!=null; }
    float GetPickupRange() => 0.5f;
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
public class PortableObject
{
    public int ItemId;
    public Transform transform = new Transform();
    public Vector3 WorldPosition => transform.position;
    public DroppedItemPickupGate Gate;
    public T GetComponent<T>() where T : class => Gate as T;
    public void SetPickupSourceBlock(Block block) { }
    public void SetFocusStack(List<PortableObject> stack) { }
}
public partial class Block : Source
{
    public Vector3 WorldPosition => transform.position;
    private readonly List<List<PortableObject>> floorStacks = new();
    public readonly List<PortableObject> inputAreaCenterStack = new();
    private Transform inputAreaCenterAnchor = new();
    private void EnsureFloorObjectsInitialized()
    {
        floorStacks.Clear();
        if (Item != null) floorStacks.Add(new List<PortableObject> { Item });
    }
    private void EnsureInputAreaCenterAnchorInitialized() { }
    private bool IsClosedBoxContentPickupBlocked() => false;
    public bool CanTake(Player player, float radius)
    {
        EnsureFloorObjectsInitialized();
        return TryFindBestManualPickupCandidate(player, player.transform.position, radius,
            -1, null, null, false, false, out _, out _, out _, out _, out _, out _);
    }
    public bool TryPreviewPickupConveyorObjects(Player p,Vector3 o,float r,int pref,out int id,out int count,out PortableObject obj) => Preview(pref,out id,out count,out obj);
}
public interface IPlayerItemStorage { }
public class Storage : MapObject, IPlayerItemStorage { public bool HasPortablePreview=true; }
public class BoxObject : MapObject, IPlayerItemStorage
{
    public bool TryPreviewContainedObjectPickup(Player p,Vector3 o,float r,int pref,out int id,out int count,out PortableObject obj) => Preview(pref,out id,out count,out obj);
}
public class RobotArmInstance : MapObject
{
    public PortableObject HeldPortableObject;
    public bool HasHeldItem => HeldPortableObject!=null;
    public bool CanTakeHeldItemFromSlot=true;
    public int HeldItemId => HeldPortableObject?.ItemId??-1;
    public Vector3 WorldPosition => HeldPortableObject != null ? HeldPortableObject.transform.position : transform.position;
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
    public RobotArmInstance Arm;
    public bool TryGetFocusedRobotArm(out RobotArmInstance arm) { arm=Arm; return arm!=null; }
}
public class TerrainGenerator
{
    public Block Adjacent;
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
    {
        block = coordinate.x == 1 && coordinate.y == 0 ? Adjacent : null;
        return block != null;
    }
}
namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy = true; }
    public class Renderer { public bool enabled = true; public GameObject gameObject = new GameObject(); }
    public static class Mathf { public static float Max(float a,float b) => a>b?a:b; }
    public static class Time { public static int frameCount; }
    public struct Vector2Int
    {
        public int x,y;
        public Vector2Int(int x,int y) { this.x=x; this.y=y; }
    }
    public struct Vector3
    {
        public float x,y,z;
        public static Vector3 zero => new Vector3(0,0,0);
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public class Transform { public Vector3 position; }
}
