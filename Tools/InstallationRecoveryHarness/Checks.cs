using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z);
    public readonly record struct Vector2Int(int x, int y);
}
public sealed class GameObject { public string name; }
public sealed class ItemDefinition
{
    public int id;
    public string itemName, name;
    public MapObject mapObject;
}
public sealed class ItemManager { public readonly List<ItemDefinition> ItemDefinitions = new(); }
public sealed class GameManager
{
    public static GameManager Instance = new();
    public readonly ItemManager ItemManger = new();
}
public partial class PropObj
{
    private ItemDefinition itemDefinition;
    protected int objId;
    public string objectName, name;
    public GameObject gameObject = new();
    public void BindForTest(ItemDefinition definition) => itemDefinition = definition;
}
public class MapObject : PropObj { }
public partial class InstallationObject : MapObject { }
public sealed class FixtureTower : InstallationObject { }
public sealed class StoredInstallation : InstallationObject, IPersistentInstallationItemStorage, IPersistentInstallationItemCollectionStorage
{
    public int Single = -1;
    public List<int> Items = new();
    public int PersistentStoredItemId => throw new Exception("Recovery must not read live stored items after detach");
    public void ApplyPersistentStoredItemId(int itemId) => Single = itemId;
    public void CapturePersistentStoredItemIds(List<int> ids, List<float> health = null)
        => throw new Exception("Recovery must not recapture cleared train cargo after detach");
    public void ApplyPersistentStoredItemIds(IReadOnlyList<int> ids, IReadOnlyList<float> health = null) => Items = new(ids);
}
public partial class InputOutputModule
{
    public enum RectGridBlockType { None, Object, InputEnergy, InputItem }
    public record struct RectGridBlockPlacement(int x, int y, RectGridBlockType blockType);
    private readonly List<RectGridBlockPlacement> rectGridPlacements = new();
    private void EnsureRectGridPlacementData() { }
    public void Add(int x,int y,RectGridBlockType kind) => rectGridPlacements.Add(new(x,y,kind));
}
public partial class InstallationPlacementController
{
    private sealed class InstallationEditSession
    {
        public InstallationObject originalInstallation;
        public int storedInstallationItemId = -1;
        public List<int> storedInstallationItemIds = new();
        public List<float> storedInstallationItemHealth = new();
    }
    private sealed class PackedInstallationSession
    {
        public InstallationEditSession editSession = new();
        public Vector2Int dropCoordinate;
    }
    public readonly List<(int Id,float Health,bool Floor)> Returned = new();
    public int BagCapacity = int.MaxValue;
    public bool FloorAvailable = true;
    public int FailItemId = -1;
    private static Vector3 ResolvePackedItemMoveStartPosition(PackedInstallationSession session) => default;
    private bool TryAddPackedItemToPlayerStorage(int id, Vector3 position, ref int index)
        => TryAddPackedItemToPlayerStorage(id,position,ref index,out _);
    private bool TryAddPackedItemToPlayerStorage(int id, Vector3 position, ref int index, out object target, float health = 0)
    {
        target=null;
        if(BagCapacity<=0 || id==FailItemId) return false;
        BagCapacity--; Returned.Add((id,health,false)); index++; return true;
    }
    private bool TryDropPackedFloorObject(int id, Vector2Int coordinate,out object portable,out Vector2Int dropped,float health=0)
    {
        portable=null; dropped=coordinate;
        if(!FloorAvailable || id==FailItemId) return false;
        Returned.Add((id,health,true)); return true;
    }
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private void Return(PackedInstallationSession session) { int move=0; TransferPackedStoredInstallationItemsToPlayerStorage(session,ref move); }
    static int Main()
    {
        try { Run(); Console.WriteLine($"PASS: {checks} production installation recovery/layout checks"); return 0; }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void Run()
    {
        MapFilterChecks.Run();
        CheckDefinitionBinding();
        var controller=new InstallationPlacementController();
        var live=new StoredInstallation();
        var session=new PackedInstallationSession(); session.editSession.originalInstallation=live;
        session.editSession.storedInstallationItemId=132;
        controller.Return(session);
        Check(controller.Returned.Count==1 && controller.Returned[0].Id==132 && session.editSession.storedInstallationItemId==-1,
            "Tower crossbow returns from pre-detach snapshot even when live storage was cleared");
        controller.Return(session);
        Check(controller.Returned.Count==1 && !HasPendingStoredInstallationItems(session), "A returned weapon cannot be recovered twice");
        session.editSession.storedInstallationItemId=54; live.Single=54;
        controller.Return(session);
        Check(controller.Returned.Count==2 && live.Single==-1, "Desk manual is returned and cleared from snapshot and live owner");

        session.editSession.storedInstallationItemIds=new(){7,8,8};
        session.editSession.storedInstallationItemHealth=new(){40,0,12};
        controller.Return(session);
        Check(controller.Returned.Count==5 && session.editSession.storedInstallationItemIds.Count==0 && live.Items.Count==0,
            "Disabled freight car cargo is returned from its captured collection");
        Check(controller.Returned.Contains((7,40,false)) && controller.Returned.Contains((8,12,false)),
            "Recovered cargo preserves each item's health and duplicate counts");
        controller.Return(session);
        Check(controller.Returned.Count==5, "Repeated train recovery does not duplicate cargo");

        controller.BagCapacity=0;
        session.editSession.storedInstallationItemId=132;
        session.editSession.storedInstallationItemIds=new(){9};
        session.editSession.storedInstallationItemHealth=new(){21};
        controller.Return(session);
        Check(controller.Returned.Contains((132,0,true)) && controller.Returned.Contains((9,21,true))
            && !HasPendingStoredInstallationItems(session), "Full bag falls back to floor drops for mounted and collected items");

        controller.FloorAvailable=false;
        session.editSession.storedInstallationItemId=54;
        session.editSession.storedInstallationItemIds=new(){10,11};
        session.editSession.storedInstallationItemHealth=new(){33,44};
        int before=controller.Returned.Count;
        controller.Return(session);
        Check(controller.Returned.Count==before && HasPendingStoredInstallationItems(session)
            && session.editSession.storedInstallationItemId==54 && session.editSession.storedInstallationItemIds.Count==2,
            "When bag and floor both reject items the snapshot remains pending instead of losing contents");
        controller.BagCapacity=10; controller.FailItemId=10;
        controller.Return(session);
        Check(session.editSession.storedInstallationItemId==-1 && session.editSession.storedInstallationItemIds.Count==1
            && session.editSession.storedInstallationItemIds[0]==10 && session.editSession.storedInstallationItemHealth[0]==33,
            "Partial recovery removes only successful items and keeps remaining health aligned");
        controller.FailItemId=-1; controller.Return(session);
        Check(!HasPendingStoredInstallationItems(session) && controller.Returned.Count==before+3,
            "Retry returns only remaining items without duplicates");
        session.editSession.originalInstallation=null;
        session.editSession.storedInstallationItemId=132;
        session.editSession.storedInstallationItemIds=new(){12}; session.editSession.storedInstallationItemHealth.Clear();
        controller.Return(session);
        Check(!HasPendingStoredInstallationItems(session), "Snapshot recovery works without a surviving presentation or optional health metadata");

        var grid = new InputOutputModule(); grid.Add(0,0,InputOutputModule.RectGridBlockType.InputItem);
        Check(!grid.TryGetPrimaryObjectCell(out _), "Input-only I/O layout has no valid physical placement anchor");
        grid.Add(0,1,InputOutputModule.RectGridBlockType.Object);
        Check(grid.TryGetPrimaryObjectCell(out var anchor) && anchor==new Vector2Int(0,1),
            "Corrected Tower layout resolves a separate physical body anchor");
    }

    private static void CheckDefinitionBinding()
    {
        var tower = new FixtureTower();
        tower.gameObject.name="Tower(Clone)";
        Check(tower.BoundItemDefinition==null, "A tower waits for item data initialization without caching a failed lookup");
        var definition=new ItemDefinition { id=134, itemName="Tower", name="Item_134_Tower",
            mapObject=new FixtureTower { name="Tower" } };
        var definitions=GameManager.Instance.ItemManger.ItemDefinitions;
        definitions.Add(definition);
        Check(ReferenceEquals(tower.BoundItemDefinition,definition),
            "An already installed unbound Tower resolves and binds its canonical ItemDefinition for combat");
        Check(tower.ResolveItemId()==134, "Combat metadata and item ID use the same cached definition");
        definitions.Clear();
        Check(ReferenceEquals(tower.BoundItemDefinition,definition) && tower.ResolveItemId()==134,
            "A resolved tower keeps its cached binding instead of rescanning item names");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) { _=tower.BoundItemDefinition; _=tower.ResolveItemId(); }
        Check(GC.GetAllocatedBytesForCurrentThread()==before, "Repeated combat/ID metadata reads allocate zero managed bytes");
        definition.id=207;
        Check(tower.ResolveItemId()==207, "Item ID changes follow the same ItemDefinition rather than a hardcoded Tower ID");
        definitions.Add(definition);
        var oldTower=new FixtureTower { objectName="Tower" }; oldTower.gameObject.name="Existing object";
        Check(ReferenceEquals(oldTower.BoundItemDefinition,definition), "Legacy object names bind existing Tower instances too");
        var unrelated=new FixtureTower(); unrelated.gameObject.name="TowerDecoration(Clone)";
        Check(unrelated.BoundItemDefinition==null, "Lookup does not bind unrelated objects by class type alone");
        var explicitDefinition=new ItemDefinition { id=88 };
        tower.BindForTest(explicitDefinition);
        Check(ReferenceEquals(tower.BoundItemDefinition,explicitDefinition) && tower.ResolveItemId()==88,
            "Existing explicit item bindings are never overwritten by inferred matches");
    }
}
