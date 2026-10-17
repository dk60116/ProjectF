using UnityEngine;

public partial class Player
{
    private PlayerBag equipmentBag;
    private int[] observedEquipmentItemIds = System.Array.Empty<int>();

    public PlayerBag GetEquipmentBag(int minimumSlotCount = 3)
    {
        if (equipmentBag == null)
        {
            var root = new GameObject("Equipment Storage");
            root.transform.SetParent(transform, false);
            equipmentBag = root.AddComponent<PlayerBag>();
            equipmentBag.Changed += HandleEquipmentInventoryChanged;
        }
        equipmentBag.EnsureEquipmentSlots(Mathf.Max(3, minimumSlotCount));
        int previousCount = observedEquipmentItemIds.Length;
        if (previousCount < equipmentBag.SlotCount)
        {
            System.Array.Resize(ref observedEquipmentItemIds, equipmentBag.SlotCount);
            for (int i = previousCount; i < observedEquipmentItemIds.Length; i++) observedEquipmentItemIds[i] = -1;
        }
        return equipmentBag;
    }

    private void HandleEquipmentInventoryChanged()
    {
        int equippedSlot = -1;
        for (int i = 0; i < observedEquipmentItemIds.Length; i++)
        {
            int id = equipmentBag.GetSlotCount(i) > 0 ? equipmentBag.GetSlotItemId(i) : -1;
            if (id >= 0 && id != observedEquipmentItemIds[i]) equippedSlot = i;
            observedEquipmentItemIds[i] = id;
        }
        if (updatingAmmunition) return;
        if (equippedSlot >= 0) TrySetSelectedEquipment(equippedSlot);
        else RefreshSelectedEquipmentIdentity();
    }

    private void RestoreEquipmentSlots(PlayerSaveData data)
    {
        int slotCount = 3;
        if (data.equipmentSlots != null)
            for (int i = 0; i < data.equipmentSlots.Count; i++)
                if (data.equipmentSlots[i] != null)
                    slotCount = Mathf.Max(slotCount, data.equipmentSlots[i].slotIndex + 1);
        GetEquipmentBag(slotCount).ApplySaveSlots(data.equipmentSlots);
    }
}
