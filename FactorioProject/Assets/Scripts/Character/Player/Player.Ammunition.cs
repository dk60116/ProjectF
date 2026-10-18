using System;
using ProjectF.Combat;
using UnityEngine;

public partial class Player
{
    public event Action EquipmentSelectionChanged;
    private PlayerBag ammunitionBag;
    private bool updatingAmmunition;
    private int selectedEquipmentSlot = -1;
    private int selectedEquipmentItemId = -1;
    public int SelectedEquipmentSlot => selectedEquipmentSlot;
    public ItemDefinition SelectedEquipment => equipmentBag != null && selectedEquipmentSlot >= 0
        && equipmentBag.GetSlotCount(selectedEquipmentSlot) > 0
        ? ResolveItemDefinition(equipmentBag.GetSlotItemId(selectedEquipmentSlot)) : null;
    public bool HasRangedEquipment => SelectedEquipment != null && SelectedEquipment.isRangedWeapon;
    public bool RequiresAmmunition => HasRangedEquipment && SelectedEquipment.requiresAmmo;

    public PlayerBag GetAmmunitionBag()
    {
        if (ammunitionBag == null)
        {
            var root = new GameObject("Ammunition Storage");
            root.transform.SetParent(transform, false);
            ammunitionBag = root.AddComponent<PlayerBag>();
            ammunitionBag.EnsureAmmoStorage(SelectedEquipment);
        }
        return ammunitionBag;
    }

    public bool TrySelectEquipment(int index)
    {
        PlayerBag bag = GetEquipmentBag();
        if (index >= 0 && (index >= bag.SlotCount || bag.GetSlotCount(index) <= 0)) return false;
        if (index == selectedEquipmentSlot) index = -1;
        return TrySetSelectedEquipment(index);
    }

    private bool TrySetSelectedEquipment(int index)
    {
        if (!TryReturnAmmunition()) return false;
        playerController?.CancelRangedAttack();
        selectedEquipmentSlot = index;
        selectedEquipmentItemId = index >= 0 ? equipmentBag.GetSlotItemId(index) : -1;
        GetAmmunitionBag().EnsureAmmoStorage(SelectedEquipment);
        EquipmentSelectionChanged?.Invoke();
        return true;
    }

    public bool TryPrepareEquipmentTransfer(PlayerBag source, int sourceIndex, PlayerBag target, int targetIndex)
    {
        return selectedEquipmentSlot < 0
            || !(source == equipmentBag && sourceIndex == selectedEquipmentSlot
                || target == equipmentBag && targetIndex == selectedEquipmentSlot)
            || TryReturnAmmunition();
    }

    private void RefreshSelectedEquipmentIdentity()
    {
        if (selectedEquipmentSlot < 0) return;
        ItemDefinition selected = SelectedEquipment;
        if (selected == null || selected.id != selectedEquipmentItemId)
            TrySetSelectedEquipment(selected != null ? selectedEquipmentSlot : -1);
    }

    private void RefillAmmunitionAfterShot(int ammoId)
    {
        if (updatingAmmunition || !RequiresAmmunition) return;
        PlayerBag ammoBag = GetAmmunitionBag();
        if (ammoBag.GetSlotCount(0) != 0 || !SelectedEquipment.AcceptsAmmo(ResolveItemDefinition(ammoId))) return;
        updatingAmmunition = true;
        try
        {
            FillAmmunitionFrom(GetBag(), ammoId);
            FillAmmunitionFrom(GetHandBag(), ammoId);
        }
        finally { updatingAmmunition = false; UpdateCarryState(); }
    }

    private void FillAmmunitionFrom(PlayerBag source, int ammoId)
    {
        if (source == null) return;
        PlayerBag target = GetAmmunitionBag();
        for (int i = 0; i < source.SlotCount; i++)
        {
            if (source.GetSlotItemId(i) != ammoId) continue;
            int capacity = target.GetAvailableSlotCapacityForItem(0, ammoId);
            int count = Mathf.Min(capacity, source.GetSlotRemovableCount(i));
            if (count > 0) PlayerInventoryTransfer.TryMove(source, i, target, 0, count);
            if (target.GetSlotCount(0) >= target.GetSlotCapacityForItem(0, ammoId)) break;
        }
    }

    private bool TryReturnAmmunition()
    {
        if (updatingAmmunition) return false;
        updatingAmmunition = true;
        try { return TryReturnAmmunitionCore(); }
        finally { updatingAmmunition = false; UpdateCarryState(); }
    }

    private bool TryReturnAmmunitionCore()
    {
        if (ammunitionBag == null || ammunitionBag.GetSlotCount(0) == 0) return true;
        int id = ammunitionBag.GetSlotItemId(0), count = ammunitionBag.GetSlotCount(0);
        PlayerBag bag = GetBag(), hand = GetHandBag();
        if (GetAmmunitionReturnCapacity(bag, id) + GetAmmunitionReturnCapacity(hand, id) < count)
            return false;
        ReturnAmmunitionTo(bag, id);
        ReturnAmmunitionTo(hand, id);
        return ammunitionBag.GetSlotCount(0) == 0;
    }

    private static int GetAmmunitionReturnCapacity(PlayerBag target, int ammoId)
    {
        if (target == null) return 0;
        int capacity = 0;
        for (int i = 0; i < target.SlotCount; i++)
            if (!target.HasPendingSlotChanges(i)) capacity += target.GetAvailableSlotCapacityForItem(i, ammoId);
        return capacity;
    }

    private void ReturnAmmunitionTo(PlayerBag target, int ammoId)
    {
        if (target == null) return;
        for (int i = 0; i < target.SlotCount && ammunitionBag.GetSlotCount(0) > 0; i++)
        {
            int id = target.GetSlotItemId(i), count = target.GetSlotCount(i);
            if (count > 0 && id != ammoId) continue;
            int move = Mathf.Min(ammunitionBag.GetSlotCount(0), target.GetAvailableSlotCapacityForItem(i, ammoId));
            if (move > 0) PlayerInventoryTransfer.TryMove(ammunitionBag, 0, target, i, move);
        }
    }

    private void OnDestroy()
    {
        if (equipmentBag != null) equipmentBag.Changed -= HandleEquipmentInventoryChanged;
    }

    private void RestoreAmmunition(PlayerSaveData data)
    {
        selectedEquipmentSlot = data.selectedEquipmentSlot;
        if (selectedEquipmentSlot < 0 || selectedEquipmentSlot >= GetEquipmentBag().SlotCount
            || equipmentBag.GetSlotCount(selectedEquipmentSlot) <= 0) selectedEquipmentSlot = -1;
        selectedEquipmentItemId = SelectedEquipment != null ? SelectedEquipment.id : -1;
        GetAmmunitionBag().EnsureAmmoStorage(SelectedEquipment);
        ammunitionBag.ApplySaveSlots(data.ammunitionSlots);
        updatingAmmunition = false;
        nextRangedShotTime = 0f;
        EquipmentSelectionChanged?.Invoke();
    }
}
