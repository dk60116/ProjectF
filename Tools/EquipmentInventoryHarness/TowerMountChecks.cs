using System.Reflection;
using UnityEngine;

static partial class Checks
{
    static void CheckTowerMount()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var weapon = new ItemDefinition { id = 132, isEquipment = true, isRangedWeapon = true, requiresAmmo = true };
        var arrow = new ItemDefinition { id = 133, isAmmo = true };
        weapon.compatibleAmmo = new() { arrow };
        var items = GameManager.Instance.ItemManger.Items;
        items[132] = weapon; items[133] = arrow;
        var visual = new GameObject(); visual.SetActive(false);
        var tower = new Tower();
        typeof(Tower).GetField("weaponItem", flags).SetValue(tower, weapon);
        typeof(Tower).GetField("weaponVisual", flags).SetValue(tower, visual);
        var player = new Player();
        var persistence = TerrainGenerator.Active;
        int saves = persistence.SaveCalls;

        Check(!tower.HasWeapon && !tower.CanPlayerInteract(player) && !visual.activeInHierarchy, "Empty tower does not invent a weapon");
        Check(!tower.AcceptsMountedItem(133) && tower.AcceptsMountedItem(132), "Tower accepts only its configured weapon definition, not ammunition");
        player.Hand.SetSlotContents(0,132,2);
        Check(tower.CanPlayerInteract(player) && tower.GetInteractionIconItemId(player) == 132, "Held crossbow enables tower interaction");
        Check(tower.TryPlayerInteract(player) && player.Hand.GetSlotCount(0) == 1 && tower.PersistentStoredItemId == 132
            && visual.activeInHierarchy && persistence.SaveCalls == saves+1 && persistence.SavedItemId == 132,
            "Mounting consumes exactly one weapon, shows the authored model and persists the item");
        Check(!tower.TryMountItemFromSlot(player,player.Hand,0) && player.Hand.GetSlotCount(0) == 1,
            "Occupied tower rejects duplicate mounting without consuming another weapon");
        Check(tower.TryPlayerInteract(player) && !tower.HasWeapon && player.Hand.GetSlotCount(0) == 2 && !visual.activeInHierarchy,
            "Interaction returns mounted weapon to the hand and hides its model");

        Check(tower.TryMountItemFromSlot(player,player.Hand,0), "Tower can be filled again");
        player.Hand = PlayerBag.Normal(1); player.Hand.SetSlotContents(0,133,1);
        player.Bag = PlayerBag.Normal(1); player.Bag.SetSlotContents(0,133,1);
        Check(!tower.CanPlayerInteract(player) && !tower.TryPlayerInteract(player) && tower.HasWeapon && visual.activeInHierarchy,
            "Full player storage does not lose the mounted weapon");
        player.Bag.SetSlotContents(0,-1,0);
        Check(tower.TryPlayerInteract(player) && player.Bag.GetSlotItemId(0) == 132 && !tower.HasWeapon,
            "Pickup falls back to the bag when the hand is full");

        saves = persistence.SaveCalls;
        tower.ApplyPersistentStoredItemId(132);
        Check(tower.HasWeapon && visual.activeInHierarchy && persistence.SaveCalls == saves,
            "Save restoration restores the held-model visual without writing another save");
        tower.isActiveAndEnabled = false;
        typeof(Tower).GetMethod("OnDisable", flags).Invoke(tower,null);
        Check(tower.HasWeapon && !visual.activeInHierarchy, "Chunk disable hides the model without forgetting the stored item");
        tower.isActiveAndEnabled = true;
        typeof(Tower).GetMethod("OnEnable", flags).Invoke(tower,null);
        Check(visual.activeInHierarchy, "Chunk enable restores the mounted model");
        tower.ApplyPersistentStoredItemId(133);
        Check(!tower.HasWeapon && !visual.activeInHierarchy, "Restoration rejects incompatible stored items");
        tower.ApplyPersistentStoredItemId(132); tower.PrepareForPool();
        Check(!tower.HasWeapon && !visual.activeInHierarchy, "Pooling clears mounted weapon state and visuals");

        player = new Player();
        player.GetEquipmentBag().SetSlotContents(0,132,1);
        player.Bag.SetSlotContents(0,133,3);
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag,0,player.GetAmmunitionBag(),0,3), "Load selected crossbow for tower transfer");
        Check(tower.CanPlayerInteract(player) && tower.TryPlayerInteract(player) && tower.HasWeapon
            && player.GetEquipmentBag().GetSlotCount(0) == 0 && player.SelectedEquipment == null
            && player.GetAmmunitionBag().GetSlotCount(0) == 0 && player.Bag.GetTotalItemCount(133) == 3,
            "Equipped crossbow mounting returns ammunition, removes equipment and preserves all ammo");
        tower.PrepareForPool();

        player = new Player();
        player.GetEquipmentBag().SetSlotContents(0,132,1);
        player.Bag.SetSlotContents(0,133,1);
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag,0,player.GetAmmunitionBag(),0,1), "Load ammo before simulating full player storage");
        player.Bag = PlayerBag.Normal(1); player.Bag.SetSlotContents(0,132,1);
        player.Hand = PlayerBag.Normal(1); player.Hand.SetSlotContents(0,133,1);
        saves = persistence.SaveCalls;
        Check(!tower.TryMountItemFromSlot(player,player.GetEquipmentBag(),0) && !tower.HasWeapon
            && player.SelectedEquipment == weapon && player.GetAmmunitionBag().GetSlotCount(0) == 1
            && persistence.SaveCalls == saves,
            "No room to return loaded ammo cancels mounting before consuming the equipped weapon");

        player = new Player(); player.Hand.SetSlotContents(0,132,1);
        Check(player.Hand.TryReserveObjectToSlotOnly(0,132,out var reserved), "Reserve an incoming hand item");
        Check(!tower.TryMountItemFromSlot(player,player.Hand,0) && !tower.HasWeapon && player.Hand.GetSlotCount(0) == 1,
            "An in-flight storage reservation blocks mounting without consuming an item");
        player.Hand.ReleaseReservedObject(reserved);
        var unconfigured = new Tower();
        Check(!unconfigured.AcceptsMountedItem(132) && !unconfigured.TryPlayerInteract(player),
            "Missing model or item configuration never consumes a crossbow");
    }
}
