using System.Collections.Generic;
using ProjectF.Combat;
using ProjectF.Monsters;
using UnityEngine;

public class Tower : InputOutputModule, IPlayerMapObjectInteraction, IPersistentInstallationItemStorage, IPlayerItemMount
{
    [SerializeField] private ItemDefinition weaponItem;
    [SerializeField] private GameObject weaponVisual;
    [SerializeField, HideInInspector] private int storedWeaponItemId = -1;
    private MonsterInstance attackTarget;
    private PlayerProjectileWorld projectileWorld;
    private float shotCooldown, targetSearchCooldown;

    public bool HasWeapon => storedWeaponItemId >= 0;
    public int PersistentStoredItemId => storedWeaponItemId;

    protected override void OnEnable()
    {
        base.OnEnable();
        RefreshWeaponVisual();
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;
        ResetAttack();
        if (weaponVisual != null) weaponVisual.SetActive(false);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        SetStoredWeapon(-1, false);
        base.PrepareForPool();
    }

    public bool AcceptsMountedItem(int itemId)
    {
        return weaponItem != null && weaponVisual != null && itemId >= 0 && itemId == weaponItem.id;
    }

    public bool CanPlayerInteract(Player player)
    {
        if (player == null) return false;
        if (!HasWeapon) return TryGetWeaponSource(player, out _, out _);
        PlayerBag hand = player.GetHandBag();
        hand?.RefreshExternalStackCounts(false);
        return (hand != null && hand.HasSpaceForItem(storedWeaponItemId))
            || (player.GetBag() is PlayerBag bag && bag.HasSpaceForItem(storedWeaponItemId));
    }

    public bool TryPlayerInteract(Player player)
    {
        if (player == null) return false;
        if (HasWeapon)
        {
            if (!PlayerItemStorageUtility.TryReserveHand(player, storedWeaponItemId, out PlayerItemStorageReservation reservation)
                && !PlayerItemStorageUtility.TryReserveBag(player, storedWeaponItemId, -1, true, out reservation))
                return false;

            SetStoredWeapon(-1, true);
            reservation.Commit();
            player.UpdateCarryState();
            return true;
        }

        return TryGetWeaponSource(player, out PlayerBag source, out int slotIndex)
            && TryMountItemFromSlot(player, source, slotIndex);
    }

    public int GetInteractionIconItemId(Player player)
    {
        return HasWeapon ? storedWeaponItemId : weaponItem != null ? weaponItem.id : -1;
    }

    public bool TryMountItemFromSlot(Player player, PlayerBag sourceBag, int slotIndex)
    {
        if (HasWeapon || player == null || sourceBag == null || slotIndex < 0) return false;
        sourceBag.RefreshExternalStackCounts(false);
        int itemId = sourceBag.GetSlotItemId(slotIndex);
        if (!AcceptsMountedItem(itemId) || sourceBag.HasPendingSlotChanges(slotIndex)
            || sourceBag.GetSlotRemovableCount(slotIndex) <= 0) return false;

        // Unequipping must return loaded ammunition before removing the selected weapon.
        if (!player.TryPrepareEquipmentTransfer(sourceBag, slotIndex, null, -1)
            || !sourceBag.TryRemoveOneAtSlot(slotIndex, out _, false)) return false;

        SetStoredWeapon(itemId, true);
        player.UpdateCarryState();
        return true;
    }

    public void ApplyPersistentStoredItemId(int itemId)
    {
        SetStoredWeapon(AcceptsMountedItem(itemId) ? itemId : -1, false);
    }

    private bool TryGetWeaponSource(Player player, out PlayerBag source, out int slotIndex)
    {
        source = player.GetHandBag();
        slotIndex = 0;
        source?.RefreshExternalStackCounts(false);
        if (source != null && source.GetSlotRemovableCount(0) > 0
            && !source.HasPendingSlotChanges(0) && AcceptsMountedItem(source.GetSlotItemId(0))) return true;

        slotIndex = player.SelectedEquipmentSlot;
        source = slotIndex >= 0 ? player.GetEquipmentBag() : null;
        return source != null && source.GetSlotRemovableCount(slotIndex) > 0
            && !source.HasPendingSlotChanges(slotIndex) && AcceptsMountedItem(source.GetSlotItemId(slotIndex));
    }

    private void SetStoredWeapon(int itemId, bool save)
    {
        if (storedWeaponItemId != itemId) ResetAttack();
        storedWeaponItemId = itemId;
        RefreshWeaponVisual();
        WakeRuntimeUpdate();
        if (save) TerrainGenerator.ResolveActive()?.SaveRuntimeInstallationState(this);
    }

    private void RefreshWeaponVisual()
    {
        if (weaponVisual != null) weaponVisual.SetActive(HasWeapon && isActiveAndEnabled);
    }

    public bool TryCollectAmmunitionItemIds(ICollection<int> itemIds)
    {
        if (itemIds == null || weaponItem == null || weaponItem.compatibleAmmo == null) return false;
        bool found = false;
        for (int i = 0; i < weaponItem.compatibleAmmo.Count; i++)
        {
            ItemDefinition ammo = weaponItem.compatibleAmmo[i];
            if (!weaponItem.AcceptsAmmo(ammo) || ammo.id < 0) continue;
            if (!itemIds.Contains(ammo.id)) itemIds.Add(ammo.id);
            found = true;
        }
        return found;
    }

    protected override bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> itemIds)
        => TryCollectAmmunitionItemIds(itemIds);

    protected override bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> itemIds)
    {
        if (itemIds == null || weaponItem == null || weaponItem.compatibleAmmo == null) return false;
        bool found = false;
        for (int i = 0; i < weaponItem.compatibleAmmo.Count; i++)
        {
            ItemDefinition ammo = weaponItem.compatibleAmmo[i];
            if (!weaponItem.AcceptsAmmo(ammo) || !ContainsRuntimeInputItemArea(coordinate, ammo.id)) continue;
            itemIds.Add(ammo.id);
            found = true;
        }
        return found;
    }

    // Ammunition is consumed by a successful shot, never by the production recipe loop.
    protected override void TryStartNextCraft() { }
    protected override bool ShouldKeepRuntimeUpdateTickActive()
        => HasWeapon && TryResolveAmmunition(out _, out _, out _);
    protected override bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy()
        => ShouldKeepRuntimeUpdateTickActive();
    protected override bool ShouldPlayWorkAnimation() => false;
    public override bool IsWorkingForItemLight => attackTarget != null && HasWeapon;

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;
        if (!HasWeapon) return "No weapon";
        if (!TryResolveAmmunition(out _, out _, out _)) return "Waiting for ammunition";
        isProducing = PlayerProjectileWorld.IsDamageable(attackTarget);
        return isProducing ? "Attacking" : "Waiting for target";
    }

    public override void ApplyManagedUpdateTick()
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime)) return;
        if (isActiveAndEnabled && !MapObjectTickManager.SimulationPaused
            && !MapObjectTickManager.WaitingForWorldLoad && !SaveManager.GameplayInputBlocked)
            TickAttack(deltaTime);
        ApplyPlannedBaseModuleTick(deltaTime);
    }

    private void TickAttack(float deltaTime)
    {
        shotCooldown = Mathf.Max(0f, shotCooldown - deltaTime);
        targetSearchCooldown = Mathf.Max(0f, targetSearchCooldown - deltaTime);
        if (!HasWeapon || !AcceptsMountedItem(storedWeaponItemId)) { attackTarget = null; return; }
        if (shotCooldown > 0f) return;
        if (!TryResolveAmmunition(out ItemDefinition projectile, out WeaponShotStats stats, out Vector2Int ammoCoordinate))
        { attackTarget = null; return; }

        Vector3 origin = weaponVisual.transform.TransformPoint(new Vector3(0f, .12f, 0f));
        if (attackTarget != null && (!PlayerProjectileWorld.IsDamageable(attackTarget)
            || (attackTarget.ClosestCombatPoint(origin) - origin).sqrMagnitude > stats.Range * stats.Range))
        { attackTarget = null; targetSearchCooldown = 0f; }
        if (attackTarget == null)
        {
            if (targetSearchCooldown > 0f) return;
            targetSearchCooldown = .25f;
            if (MonsterWorld.Current == null
                || !MonsterWorld.Current.TryFindNearestAttackTarget(origin, stats.Range, out attackTarget)) return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null) return;
        if (projectileWorld == null)
            projectileWorld = terrain.gameObject.GetComponent<PlayerProjectileWorld>()
                ?? terrain.gameObject.AddComponent<PlayerProjectileWorld>();
        if (weaponItem.requiresAmmo
            && ConsumeRuntimeInputAreaCenterObjects(ammoCoordinate, projectile.id, 1, origin, 0f) != 1) return;

        Vector3 facing = attackTarget.ClosestCombatPoint(origin) - origin;
        facing.y = 0f;
        if (facing.sqrMagnitude > .000001f) weaponVisual.transform.rotation = Quaternion.LookRotation(facing);
        projectileWorld.Launch(projectile, origin, attackTarget, stats, transform.position);
        shotCooldown = stats.Interval;
        MarkManagedRuntimeVisualsDirty();
    }

    private bool TryResolveAmmunition(out ItemDefinition projectile, out WeaponShotStats stats, out Vector2Int coordinate)
    {
        projectile = null;
        stats = default;
        coordinate = default;
        if (!weaponItem.requiresAmmo)
        {
            projectile = weaponItem;
            return projectile.portableMesh != null && projectile.portableMat != null
                && WeaponShotStats.TryResolve(weaponItem, null, out stats);
        }
        if (weaponItem.compatibleAmmo == null) return false;
        for (int i = 0; i < weaponItem.compatibleAmmo.Count; i++)
        {
            ItemDefinition ammo = weaponItem.compatibleAmmo[i];
            if (!weaponItem.AcceptsAmmo(ammo) || ammo.portableMesh == null || ammo.portableMat == null
                || !WeaponShotStats.TryResolve(weaponItem, ammo, out stats)
                || !TryResolveRuntimeInputItemBlock(ammo.id, 1, null, out _, out coordinate)) continue;
            projectile = ammo;
            return true;
        }
        return false;
    }

    private void ResetAttack()
    {
        attackTarget = null;
        shotCooldown = targetSearchCooldown = 0f;
    }
}
