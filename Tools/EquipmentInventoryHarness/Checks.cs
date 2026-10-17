using System;
using System.Collections.Generic;

static class Checks
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static int Main()
    {
        try { Run(); Console.WriteLine($"Equipment inventory runtime checks passed: {checks}"); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    static void Run()
    {
        var items = GameManager.Instance.ItemManger.Items;
        items[1] = new() { isEquipment = true };
        items[2] = new() { isEquipment = true, oneItem = true };
        items[3] = new();
        CheckHandStackGaps();
        var equipment = new PlayerBag();
        equipment.EnsureEquipmentSlots(3);
        Check(equipment.IsEquipmentStorage && equipment.SlotCount == 3, "Equipment uses an independent three-slot inventory");
        Check(!equipment.Object().IsActive, "Empty equipment anchors are inactive even when entities default to active");
        equipment.EnsureEquipmentSlots(2);
        Check(equipment.SlotCount == 3, "Repeated initialization never shrinks existing slots");
        Check(equipment.GetSlotCapacityForItem(0, 1) == 1, "Equipment capacity is one even without One Item");
        Check(equipment.GetSlotCapacityForItem(0, 3) == 0 && equipment.GetSlotCapacityForItem(0, 99) == 0, "Normal and unknown items are rejected");
        Check(!equipment.SetSlotContents(0, 3, 1) && !equipment.SetSlotContents(0, 1, 2), "Invalid insertions fail before changing contents");
        Check(equipment.GetSlotCount(0) == 0, "Rejected inserts leave the slot empty");
        Check(!equipment.TryAddObject(3, out _), "Generic add also rejects non-equipment");

        var bag = PlayerBag.Normal(5);
        bag.SetSlotContents(0, 1, 4);
        var source = new List<PortableObject>();
        bag.TryGetSlotObjects(0, 0, 4, source);
        source[3].SetInstallationHealth(47);
        int bagEvents = 0, equipEvents = 0;
        bag.Changed += () => bagEvents++;
        equipment.Changed += () => equipEvents++;
        Check(BagSlot.Move(bag, 0, equipment, 0, 1), "Dragging a stack equips exactly one item");
        Check(bag.GetSlotCount(0) == 3 && equipment.GetSlotCount(0) == 1, "Remaining stack stays in the source");
        Check(equipment.Object().InstallationHealth == 47, "Transfer preserves individual installation health");
        Check(equipment.Object().IsVisualRenderingSuppressed, "Equipped items are not rendered as world stacks");
        Check(bagEvents == 1 && equipEvents == 1, "Successful transfer refreshes both views once");
        Check(!BagSlot.Move(bag, 0, equipment, 0, 1), "Occupied equipment does not accept another copy");
        Check(bag.GetSlotCount(0) == 3 && equipment.GetSlotCount(0) == 1, "Failed full-slot transfer loses no items");
        Check(BagSlot.Move(equipment, 0, bag, 0, 1), "Equipment can be returned to a matching bag stack");
        Check(bag.GetSlotCount(0) == 4 && equipment.GetSlotCount(0) == 0 && source[3].InstallationHealth == 47, "Unequip restores the stack and health");
        Check(!source[3].IsVisualRenderingSuppressed, "Unequipped item uses ordinary bag presentation");

        var hand = PlayerBag.Normal(1);
        hand.SetSlotContents(0, 2, 1);
        equipment.SetSlotContents(1, 1, 1);
        hand.Object().SetInstallationHealth(61);
        equipment.Object(1).SetInstallationHealth(29);
        Check(BagSlot.Move(hand, 0, equipment, 1, 1, true), "Two equipment types can swap between hand and equipment");
        Check(hand.GetSlotItemId(0) == 1 && equipment.GetSlotItemId(1) == 2, "Swap exchanges both identities");
        Check(hand.Object().InstallationHealth == 29 && equipment.Object(1).InstallationHealth == 61, "Swap exchanges health with identities");
        hand.SetSlotContents(0, 3, 1);
        Check(!BagSlot.Move(hand, 0, equipment, 1, 1, true), "Normal items cannot swap into an occupied equipment slot");
        Check(hand.GetSlotItemId(0) == 3 && equipment.GetSlotItemId(1) == 2, "Rejected swap preserves both slots");

        hand.SetSlotContents(0, 1, 1);
        hand.Object().SetInstallationHealth(33);
        equipment.Object(1).FailNextItemId = 1;
        Check(!BagSlot.Move(hand, 0, equipment, 1, 1, true), "Failed item configuration aborts the transfer");
        Check(hand.GetSlotItemId(0) == 1 && equipment.GetSlotItemId(1) == 2, "Configuration failure restores both old item identities");
        Check(hand.Object().InstallationHealth == 33 && equipment.Object(1).InstallationHealth == 61, "Configuration rollback restores both health values");
        equipment.SetSlotContents(2, 2, 1);
        var saved = new List<PlayerInventorySlotSaveState>();
        equipment.CaptureSaveSlots(saved);
        var loaded = new PlayerBag(); loaded.EnsureEquipmentSlots(3); loaded.ApplySaveSlots(saved);
        Check(loaded.GetSlotItemId(1) == 2 && loaded.GetSlotItemId(2) == 2, "Save restore retains duplicate equipment in separate authored indices");
        Check(loaded.Object(1).InstallationHealth == 61, "Save restore retains equipment health");
        Check(loaded.Object(1).IsVisualRenderingSuppressed, "Restored equipment remains hidden from world rendering");
        loaded.ApplySaveSlots(null);
        Check(loaded.GetSlotCount(1) == 0 && loaded.GetSlotCount(2) == 0, "Loading a legacy save with no equipment clears previous equipment");
        equipment.EnsureEquipmentSlots(4);
        Check(equipment.GetSlotItemId(1) == 2 && equipment.SlotCount == 4, "Growing storage for additional UI slots preserves existing items");
        equipment.transform.position = new(50, 0, 70);
        Check(equipment.TryRemoveAllAtSlot(2, out _, out _, out var dropOrigin)
            && dropOrigin == equipment.transform.position, "Equipment drops originate from the player's current position, not the spawn position");
        var owned = equipment.Object(1); equipment.DestroyForTest();
        Check(owned.Disposed, "Equipment entity ownership ends with its player storage");
        var normalOwned = hand.Object(); hand.DestroyForTest();
        Check(!normalOwned.Disposed, "Normal bag authoring entity ownership is unchanged");
        CheckAmmunition();
        CheckEquipmentAutoSelection();
        CheckProjectiles();
        CheckEmptyMagazineRefill();
        CheckProjectileSpeed();
    }

    static void CheckEquipmentAutoSelection()
    {
        var player = new Player();
        player.TickInventory();
        player.Bag.SetSlotContents(0, 22, 1);
        var equipment = player.GetEquipmentBag();
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, equipment, 2, 1)
            && player.SelectedEquipmentSlot == 2 && player.RequiresAmmunition,
            "Actual drag transfer immediately selects the destination equipment slot, even without arrows");
        Check(player.GetAmmunitionBag().GetSlotCount(0) == 0,
            "An empty magazine does not disable the selected weapon's ammunition requirement");
        player.Bag.SetSlotContents(0, 23, 8);
        Check(player.GetAmmunitionBag().GetSlotCount(0) == 0 && player.Bag.GetSlotCount(0) == 8,
            "Adding arrows to the bag does not automatically load a newly equipped crossbow");
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, player.GetAmmunitionBag(), 0, 8),
            "The player can manually load ammunition through the ordinary inventory transfer");
        Check(player.TrySelectEquipment(2) && player.SelectedEquipmentSlot == -1,
            "Explicitly clicking the selected weapon can still deselect it");
        equipment.ForceNotifyChanged(); player.TickInventory();
        Check(player.SelectedEquipmentSlot == -1 && player.GetAmmunitionBag().GetSlotCount(0) == 0,
            "Unchanged inventory notifications and frame updates respect an explicit deselection");
        equipment.SetSlotContents(2, -1, 0);
        equipment.SetSlotContents(1, 22, 1);
        Check(player.SelectedEquipmentSlot == 1 && player.RequiresAmmunition,
            "Reinserting the weapon selects its new authored slot index");
        Check(player.GetAmmunitionBag().GetSlotCount(0) == 0 && player.Bag.GetSlotCount(0) == 8,
            "Re-equipping leaves the magazine empty instead of pulling returned ammunition back in");
        ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, player.GetAmmunitionBag(), 0, 8);
        equipment.SetSlotContents(1, -1, 0);
        Check(player.SelectedEquipmentSlot == -1 && !player.RequiresAmmunition
            && player.Bag.GetSlotCount(0) == 8 && player.GetAmmunitionBag().GetSlotCount(0) == 0,
            "Removing the selected weapon immediately clears selection and returns its unused arrows");
    }

    static void CheckHandStackGaps()
    {
        var emptyAnchor = new PortableObject(); emptyAnchor.SetCachedActive(false);
        var heldCrossbow = new PortableObject(); heldCrossbow.SetItem(1);
        heldCrossbow.SetInstallationHealth(42);
        var anchors = new List<PortableObject> { emptyAnchor, heldCrossbow };
        var hand = new PlayerBag(); hand.SetExternalStack(anchors);
        Check(hand.GetSlotCount(0) == 1 && hand.GetSlotItemId(0) == 1,
            "A completed pickup after an inactive hand anchor must remain visible to the hand HUD");
        Check(anchors[0] == heldCrossbow && heldCrossbow.InstallationHealth == 42,
            "Hand normalization retains the actual entity and its health");
        emptyAnchor.SetItem(1); emptyAnchor.SetCachedActive(true);
        hand.RefreshExternalStackCounts(false);
        Check(hand.GetSlotCount(0) == 2, "Previously reserved anchors can still complete after normalization");
        Check(hand.SetSlotMinimumRetainedCount(0, 1, 1) && hand.TryHideRetainedSlotObject(0, 1),
            "Retained hand items can be hidden for equipped presentation");
        hand.RefreshExternalStackCounts(false);
        Check(hand.GetSlotCount(0) == 2 && hand.GetSlotItemId(0) == 1 && !heldCrossbow.IsActive,
            "A hidden retained item still counts as owned and is not reactivated by normalization");
        hand.ClearSlotMinimumRetainedCount(0);
        Check(heldCrossbow.IsActive && hand.GetSlotCount(0) == 2, "Clearing retained presentation restores the same entity");

        for (int mask = 0; mask < 32; mask++)
        {
            var stack = new List<PortableObject>();
            var active = new List<PortableObject>();
            var pending = new HashSet<PortableObject>();
            for (int i = 0; i < 5; i++)
            {
                var item = new PortableObject(); item.SetItem(1);
                item.SetInstallationHealth(i+10);
                item.SetCachedActive((mask & (1<<i)) != 0);
                stack.Add(item);
                if (item.IsActive) active.Add(item); else pending.Add(item);
            }
            var external = new PlayerBag(); external.SetExternalStack(stack);
            Check(external.GetSlotCount(0) == active.Count, "Every five-anchor completion order reports the actual held count");
            for (int i = 0; i < active.Count; i++)
                Check(stack[i] == active[i], "Compaction preserves the completion-visible entity order");
            external.RefreshExternalStackCounts(false);
            Check(external.GetSlotCount(0) == active.Count, "Repeated HUD polling is idempotent");
            Check(pending.Count == 5-active.Count && !pending.Overlaps(active), "Identity-based pending reservations remain distinct from held entities");
            foreach (var item in pending) item.SetCachedActive(true);
            external.RefreshExternalStackCounts(false);
            Check(external.GetSlotCount(0) == 5, "All previously pending entities can finish after any completion order");
        }
    }

    static void CheckAmmunition()
    {
        var items = GameManager.Instance.ItemManger.Items;
        var arrow = new ItemDefinition { id = 23, isAmmo = true, attackDamage = 5, fireRate = 2,
            attackRange = 20, projectileSpeed = 20 };
        var crossbow = new ItemDefinition { id = 22, isEquipment = true, isRangedWeapon = true,
            requiresAmmo = true, attackDamage = 15, fireRate = 1, attackRange = 15, compatibleAmmo = new() { arrow } };
        var tool = new ItemDefinition { id = 24, isEquipment = true };
        var wrong = new ItemDefinition { id = 25, isAmmo = true };
        items[22] = crossbow; items[23] = arrow; items[24] = tool; items[25] = wrong;
        Check(crossbow.AcceptsAmmo(arrow) && !crossbow.AcceptsAmmo(wrong), "Weapon accepts only explicitly compatible ammunition");
        Check(ProjectF.Combat.WeaponShotStats.TryResolve(crossbow, arrow, out var stats)
            && stats.Damage == 20 && stats.Interval == 1 && stats.Range == 15 && stats.Speed == 20,
            "Weapon and ammo damage add; the slower rate and shorter range limit shots");
        Check(!ProjectF.Combat.WeaponShotStats.TryResolve(crossbow, null, out _)
            && !ProjectF.Combat.WeaponShotStats.TryResolve(crossbow, wrong, out _), "Missing and wrong ammo cannot produce shot stats");
        arrow.fireRate = float.NaN; arrow.attackRange = float.PositiveInfinity;
        Check(ProjectF.Combat.WeaponShotStats.TryResolve(crossbow, arrow, out stats)
            && !float.IsNaN(stats.Interval) && !float.IsInfinity(stats.Range), "Invalid authored stats cannot poison simulation");
        arrow.fireRate = 2; arrow.attackRange = 20;
        var player = new Player();
        player.GetEquipmentBag().SetSlotContents(1, 24, 1);
        player.Bag.SetSlotContents(0, 23, 150);
        player.TickInventory();
        player.GetEquipmentBag().SetSlotContents(0, 22, 1);
        Check(player.SelectedEquipmentSlot == 0 && player.SelectedEquipment == crossbow && player.RequiresAmmunition,
            "Inserting a crossbow selects it immediately without requiring a separate click");
        var ammo = player.GetAmmunitionBag();
        Check(ammo.GetSlotCount(0) == 0 && player.Bag.GetSlotCount(0) == 150, "Inserting crossbow never automatically loads arrows");
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, ammo, 0, 100)
            && ammo.GetSlotCount(0) == 100 && player.Bag.GetSlotCount(0) == 50,
            "Manual loading transfers at most 100 real arrows");
        player.GetEquipmentBag().ForceNotifyChanged();
        Check(player.SelectedEquipmentSlot == 0 && ammo.GetSlotCount(0) == 100,
            "Redundant inventory notifications do not toggle off the selected crossbow");
        Check(ammo.GetSlotCapacityForItem(0, 25) == 0 && !ammo.SetSlotContents(0, 25, 1), "Ammo storage enforces compatibility on direct insert and swap");
        Check(ammo.IsAmmoStorage && ammo.IsDataOnlyStorage && !ammo.IsEquipmentStorage, "Ammo has a separate data-only storage role");
        Check(ammo.TryRemoveOneAtSlot(0, out int spent, false) && spent == 23, "A shot consumes exactly one arrow");
        player.RefillAfterShot(23);
        Check(ammo.GetSlotCount(0) == 99 && player.Bag.GetSlotCount(0) == 50, "A partially spent magazine is not topped up");
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(ammo, 0, player.Bag, 0, 99),
            "Loaded ammunition can be returned to the bag while its weapon remains selected");
        player.Bag.ForceNotifyChanged(); player.TickInventory();
        Check(ammo.GetSlotCount(0) == 0 && player.Bag.GetSlotCount(0) == 149 && player.SelectedEquipmentSlot == 0,
            "Manually emptying ammunition is not mistaken for firing its last round");
        ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, ammo, 0, 100);
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(ammo, 0, player.Hand, 0, 10)
            && ammo.GetSlotCount(0) == 90 && player.Hand.GetSlotCount(0) == 10,
            "Loaded ammunition can be returned to the hand without being pulled back automatically");
        ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Hand, 0, ammo, 0, 10);
        Check(player.TrySelectEquipment(1) && !player.RequiresAmmunition
            && ammo.GetSlotCount(0) == 0 && player.Bag.GetSlotCount(0) == 149,
            "Switching to a non-ammo tool returns all unused ammunition to the bag");
        Check(player.TrySelectEquipment(0), "Weapon can be selected again");
        ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, ammo, 0, 100);
        player.Bag.SetSlotContents(0, 3, 200); player.Hand.SetSlotContents(0, 3, 10);
        Check(!player.TrySelectEquipment(1) && player.SelectedEquipmentSlot == 0 && ammo.GetSlotCount(0) == 100,
            "Full bag and hand reject switching instead of losing ammunition");
        Check(!player.TryPrepareEquipmentTransfer(player.GetEquipmentBag(), 0, player.Hand, 0),
            "Removing an active weapon is blocked when its ammunition cannot be returned");
        player.Bag.SetSlotContents(0, 23, 199);
        Check(!player.TrySelectEquipment(1) && player.Bag.GetSlotCount(0) == 199 && ammo.GetSlotCount(0) == 100,
            "Insufficient return capacity does not partially mutate either storage");
        player.Bag.SetSlotContents(0, 23, 110); player.Hand.SetSlotContents(0, -1, 0);
        Check(player.TrySelectEquipment(1) && player.Bag.GetSlotCount(0) == 200 && player.Hand.GetSlotCount(0) == 10
            && ammo.GetSlotCount(0) == 0, "Return uses hand space after filling available bag space");
        player.Bag.SetSlotContents(0, -1, 0);
        Check(player.TrySelectEquipment(0) && ammo.GetSlotCount(0) == 0 && player.Hand.GetSlotCount(0) == 10,
            "Selecting a weapon leaves hand ammunition untouched");
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Hand, 0, ammo, 0, 10),
            "Ammunition can be manually loaded directly from the hand");
        var save = new PlayerSaveData { selectedEquipmentSlot = 0 };
        player.GetEquipmentBag().CaptureSaveSlots(save.equipmentSlots);
        ammo.CaptureSaveSlots(save.ammunitionSlots);
        var restored = new Player();
        restored.GetEquipmentBag().ApplySaveSlots(save.equipmentSlots);
        restored.Bag.SetSlotContents(0, 23, 7);
        restored.RestoreAmmo(save);
        Check(restored.SelectedEquipmentSlot == 0 && restored.GetAmmunitionBag().GetSlotCount(0) == 10
            && restored.Bag.GetSlotCount(0) == 7,
            "Load restores the exact saved ammunition count without topping it up");
        save.ammunitionSlots.Clear();
        restored.RestoreAmmo(save);
        Check(restored.GetAmmunitionBag().GetSlotCount(0) == 0 && restored.Bag.GetSlotCount(0) == 7,
            "An intentionally empty magazine remains empty after loading");
        var noAmmoWeapon = new ItemDefinition { id = 26, isEquipment = true, isRangedWeapon = true, attackDamage = 3 };
        Check(ProjectF.Combat.WeaponShotStats.TryResolve(noAmmoWeapon, null, out stats) && stats.Damage == 3,
            "Ranged equipment explicitly configured without ammunition still resolves its own stats");
        var reservedBag = PlayerBag.Normal(200);
        reservedBag.SetSlotContents(0, 23, 1);
        Check(reservedBag.TryReserveObjectToSlotOnly(0, 23, out var reservation), "A pending pickup reserves a concrete bag anchor");
        Check(!ProjectF.Combat.PlayerInventoryTransfer.TryMove(ammo, 0, reservedBag, 0, 1)
            && ammo.GetSlotCount(0) == 10 && reservedBag.GetSlotCount(0) == 1,
            "Returning ammunition cannot overwrite a pending pickup even when other capacity remains");
        reservedBag.ReleaseReservedObject(reservation);
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(ammo, 0, reservedBag, 0, 1)
            && ammo.GetSlotCount(0) == 9 && reservedBag.GetSlotCount(0) == 2,
            "Transfers resume after the pending pickup releases its reservation");
    }

    static void CheckProjectiles()
    {
        var items = GameManager.Instance.ItemManger.Items;
        items[23].portableMesh = new UnityEngine.Mesh(); items[23].portableMat = new UnityEngine.Material();
        var player = new Player();
        player.GetEquipmentBag().SetSlotContents(0, 22, 1);
        player.Bag.SetSlotContents(0, 23, 5);
        player.TickInventory();
        ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, player.GetAmmunitionBag(), 0, 5);
        var target = new ProjectF.Monsters.MonsterInstance { Position = new(0,.65f,1) };
        UnityEngine.Time.time = 10;
        Check(player.TryFireSelectedWeapon(target), "Selected crossbow launches a real projectile");
        Check(player.GetAmmunitionBag().GetSlotCount(0) == 4 && target.CurrentHealth == 100,
            "Ammo is consumed on launch, while damage waits for physical flight");
        Check(!player.TryFireSelectedWeapon(target) && player.GetAmmunitionBag().GetSlotCount(0) == 4,
            "Repeated requests during the fire interval cannot consume extra ammo");
        var world = player.gameObject.GetComponent<ProjectF.Combat.PlayerProjectileWorld>();
        var update = typeof(ProjectF.Combat.PlayerProjectileWorld).GetMethod("Update", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var shots = typeof(ProjectF.Combat.PlayerProjectileWorld).GetField("shots", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        int ShotCount() => ((System.Collections.ICollection)shots.GetValue(world)).Count;
        void Step() { UnityEngine.Time.frameCount++; update.Invoke(world,null); }
        var shot = ((System.Collections.IList)shots.GetValue(world))[0];
        var view = shot.GetType().GetField("View", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(shot);
        object ReadView(string field) => view.GetType().GetField(field, System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view);
        var root = (UnityEngine.GameObject)ReadView("Root");
        var visual = (UnityEngine.Transform)ReadView("Transform");
        void BindCurrentView()
        {
            var current = ((System.Collections.IList)shots.GetValue(world))[0];
            view = current.GetType().GetField("View", System.Reflection.BindingFlags.Instance
                |System.Reflection.BindingFlags.NonPublic).GetValue(current);
            root = (UnityEngine.GameObject)ReadView("Root");
            visual = (UnityEngine.Transform)ReadView("Transform");
        }
        Check(((UnityEngine.MeshFilter)ReadView("Filter")).sharedMesh == items[23].portableMesh
            && ((UnityEngine.MeshRenderer)ReadView("Renderer")).sharedMaterial == items[23].portableMat,
            "A shot presents the actual authored Arrow mesh and material");
        Check(visual.rotation.From == UnityEngine.Vector3.right && visual.rotation.To == UnityEngine.Vector3.forward,
            "The authored X-axis arrow is aligned to the projectile flight direction");
        Check(visual.localScale == UnityEngine.Vector3.one * 1.25f,
            "Projectile presentation is enlarged uniformly without changing the source mesh");
        Check(ReferenceEquals(shot.GetType().GetField("Target", System.Reflection.BindingFlags.Instance
            |System.Reflection.BindingFlags.NonPublic).GetValue(shot), target),
            "A launched crossbow arrow retains its designated target");
        update.Invoke(world,null);
        Check(target.CurrentHealth == 100 && ShotCount() == 1 && root.activeInHierarchy,
            "A projectile launched before its Update stays visible for the spawn frame");
        Step();
        Check(target.CurrentHealth == 80 && ShotCount() == 0,
            "A targeted arrow damages its designated monster on arrival");
        Check(root.activeInHierarchy && visual.position == new UnityEngine.Vector3(0,.65f,1),
            "Impact moves the visible arrow to the actual hit point before pooling");
        Step();
        Check(target.CurrentHealth == 80 && !root.activeInHierarchy, "An impacted projectile renders once then recycles without repeated damage");
        UnityEngine.Time.time = 11;
        target.Position = new(0,.65f,3);
        Check(player.TryFireSelectedWeapon(target), "Cooldown expiration allows another shot");
        var animal = new Animal();
        animal.transform.position = new(0,.3f,.2f);
        MapObjectTickManager.SimulationPaused=true;
        Step();
        Check(animal.Health == 100 && ShotCount() == 1, "Simulation pause freezes flight without despawning it");
        Check(!player.TryFireSelectedWeapon(target), "Paused simulation cannot launch additional projectiles");
        MapObjectTickManager.SimulationPaused=false;
        Step();
        Check(visual.position == new UnityEngine.Vector3(0,.65f,2) && ShotCount() == 1,
            "A targeted arrow still has a visible flight time at ItemData speed");
        target.Position = new(3,.65f,2);
        Step();
        Check(visual.position == new UnityEngine.Vector3(2,.65f,2) && target.CurrentHealth == 80,
            "An arrow changes direction when its designated target moves sideways");
        Step();
        Check(animal.Health == 100 && target.CurrentHealth == 60 && ShotCount() == 0,
            "A moving target is hit without another nearby animal intercepting the shot");
        UnityEngine.Time.time=12;
        target.Position=new(0,.65f,50);
        Check(!player.TryFireSelectedWeapon(target) && player.GetAmmunitionBag().GetSlotCount(0) == 3,
            "Out-of-range targets do not consume ammunition");
        target.Position=new(0,.65f,5);
        SaveManager.GameplayInputBlocked=true;
        Check(!player.TryFireSelectedWeapon(target), "Save/load input barriers reject firing");
        SaveManager.GameplayInputBlocked=false;
        Check(player.TryFireSelectedWeapon(target), "Shot can resume after input barrier clears");
        BindCurrentView();
        target.CurrentHealth = 0;
        Step();
        Check(ShotCount() == 0 && !root.activeInHierarchy && animal.Health == 100,
            "A dead target cancels flight without retargeting or damaging another actor");
        UnityEngine.Time.time=13;
        target.CurrentHealth = 60;
        Check(player.TryFireSelectedWeapon(target), "Flight can launch again using pooled presentation");
        BindCurrentView();
        Check(visual.localScale == UnityEngine.Vector3.one * 1.25f,
            "Reusing a pooled projectile preserves its scale without accumulating enlargement");
        target.Position = new(0,.65f,50);
        var missStart = visual.position;
        Step();
        Check(visual.position != missStart && ShotCount() == 1 && root.activeInHierarchy,
            "A projectile advances its visible world position while in flight");
        for (int i=0;i<10;i++) Step();
        Check(ShotCount() == 0 && target.CurrentHealth == 60,
            "A target fleeing beyond maximum flight range does not receive remote damage");
        UnityEngine.Time.time=14;
        target.Position = new(0,.65f,5);
        Check(player.TryFireSelectedWeapon(target), "Final arrow launches");
        Check(player.GetAmmunitionBag().GetSlotCount(0) == 0 && !player.TryFireSelectedWeapon(target),
            "Empty ammo storage cannot create another shot");
        world.ClearForLoad();
        Check(ShotCount() == 0, "Loading clears transient shots while spent ammunition stays consumed");

        Check(ProjectF.Combat.WeaponShotStats.TryResolve(items[22], items[23], out var targetingStats),
            "Targeted flight uses the equipped weapon and ammunition stats");
        world.Launch(items[23], UnityEngine.Vector3.zero, animal, targetingStats, UnityEngine.Vector3.zero);
        Step();
        Check(animal.Health == 80 && target.CurrentHealth == 60 && ShotCount() == 0,
            "An animal can also be the designated target and receives damage only on arrival");
        Step();
        Check(animal.Health == 80, "Targeted animal damage is applied only once");
        world.Launch(items[23], UnityEngine.Vector3.zero, target, targetingStats, UnityEngine.Vector3.zero);
        target.IsActive = false;
        Step();
        Check(ShotCount() == 0 && target.CurrentHealth == 60,
            "A despawned target cancels its projectile safely");
        world.ClearForLoad();
    }

    static void CheckProjectileSpeed()
    {
        var items = GameManager.Instance.ItemManger.Items;
        var weapon = items[22]; var arrow = items[23];
        float oldWeaponSpeed = weapon.projectileSpeed, oldArrowSpeed = arrow.projectileSpeed;
        var update = typeof(ProjectF.Combat.PlayerProjectileWorld).GetMethod("Update",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var shotsField = typeof(ProjectF.Combat.PlayerProjectileWorld).GetField("shots",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        weapon.projectileSpeed = 999f;
        foreach (float speed in new[] { .5f, 5f, 50f })
        {
            arrow.projectileSpeed = speed;
            Check(ProjectF.Combat.WeaponShotStats.TryResolve(weapon, arrow, out var stats) && stats.Speed == speed,
                "Ammunition ItemData, not the weapon's unused speed, owns the projectile velocity");
            var world = new ProjectF.Combat.PlayerProjectileWorld();
            var target = new ProjectF.Monsters.MonsterInstance { Position = new(0,0,10) };
            world.Launch(arrow, UnityEngine.Vector3.zero, target, stats, UnityEngine.Vector3.zero);
            UnityEngine.Time.frameCount++;
            update.Invoke(world, null);
            var shot = ((System.Collections.IList)shotsField.GetValue(world))[0];
            var position = (UnityEngine.Vector3)shot.GetType().GetField("Position",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(shot);
            Check(MathF.Abs(position.z - speed * UnityEngine.Time.deltaTime) < .00001f,
                "Edited ammunition speed changes actual projectile displacement each frame");
            world.ClearForLoad();
        }
        weapon.projectileSpeed = oldWeaponSpeed; arrow.projectileSpeed = oldArrowSpeed;
    }

    static void CheckEmptyMagazineRefill()
    {
        var player = new Player();
        player.crossBowObject = new UnityEngine.GameObject();
        player.crossBowObject.transform.position = new(1,.8f,0);
        player.GetEquipmentBag().SetSlotContents(0, 22, 1);
        player.Bag.SetSlotContents(0, 23, 150);
        var ammo = player.GetAmmunitionBag();
        ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, ammo, 0, 2);
        var target = new ProjectF.Monsters.MonsterInstance();
        UnityEngine.Time.time = 20;
        Check(player.TryFireSelectedWeapon(target) && ammo.GetSlotCount(0) == 1 && player.Bag.GetSlotCount(0) == 148,
            "Firing with ammunition remaining does not automatically top up the magazine");
        var world = player.gameObject.GetComponent<ProjectF.Combat.PlayerProjectileWorld>();
        var shots = (System.Collections.IList)typeof(ProjectF.Combat.PlayerProjectileWorld).GetField("shots",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(world);
        var shot = shots[0];
        Check((UnityEngine.Vector3)shot.GetType().GetField("Position", System.Reflection.BindingFlags.Instance
            |System.Reflection.BindingFlags.NonPublic).GetValue(shot) == player.crossBowObject.transform.position,
            "The projectile starts at the equipped crossbow instead of an arbitrary body-height offset");
        UnityEngine.Time.time = 21;
        Check(player.TryFireSelectedWeapon(target) && ammo.GetSlotCount(0) == 100 && player.Bag.GetSlotCount(0) == 48,
            "Firing the last round replenishes the same ammunition from the bag, capped at 100");
        Check(ProjectF.Combat.PlayerInventoryTransfer.TryMove(ammo, 0, player.Bag, 0, 100),
            "Automatically reloaded ammunition is still removable by the player");
        UnityEngine.Time.time = 22;
        Check(!player.TryFireSelectedWeapon(target) && ammo.GetSlotCount(0) == 0 && player.Bag.GetSlotCount(0) == 148,
            "Trying to fire a manually emptied magazine does not refill it or consume stock");
        ProjectF.Combat.PlayerInventoryTransfer.TryMove(player.Bag, 0, ammo, 0, 1);
        player.Bag.SetSlotContents(0, 23, 7); player.Hand.SetSlotContents(0, 23, 6);
        Check(player.TryFireSelectedWeapon(target) && ammo.GetSlotCount(0) == 13
            && player.Bag.GetSlotCount(0) == 0 && player.Hand.GetSlotCount(0) == 0,
            "Last-round refill uses both bag and hand stock without creating ammunition");
        player.gameObject.GetComponent<ProjectF.Combat.PlayerProjectileWorld>().ClearForLoad();
    }
}
