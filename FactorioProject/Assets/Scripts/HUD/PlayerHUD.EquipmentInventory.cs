public partial class PlayerHUD
{
    private PlayerBag boundEquipmentBag;
    private PlayerBag boundAmmunitionBag;
    private Player equipmentPlayer;

    private void BindEquipmentInventory()
    {
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        PlayerBag bag = player != null ? player.GetEquipmentBag(equipSlots != null ? equipSlots.Length : 0) : null;
        if (boundEquipmentBag != bag)
        {
            UnbindEquipmentInventory();
            boundEquipmentBag = bag;
            if (bag != null) bag.Changed += RefreshEquipmentInventory;
            equipmentPlayer = player;
            if (player != null)
            {
                player.EquipmentSelectionChanged += RefreshEquipmentInventory;
                boundAmmunitionBag = player.GetAmmunitionBag();
                boundAmmunitionBag.Changed += RefreshAmmoSlots;
            }
        }
        RefreshEquipmentInventory();
    }

    private void RefreshEquipmentInventory()
    {
        if (equipSlots != null)
        for (int i = 0; i < equipSlots.Length; i++)
        {
            if (!(equipSlots[i] is BagSlot slot)) continue;
            int itemId = boundEquipmentBag != null ? boundEquipmentBag.GetSlotItemId(i) : -1;
            int count = boundEquipmentBag != null ? boundEquipmentBag.GetSlotCount(i) : 0;
            slot.Bind(boundEquipmentBag, i, itemId, count, 1);
            slot.EquipmentSelectionRequested -= HandleEquipmentSlotSelection;
            slot.EquipmentSelectionRequested += HandleEquipmentSlotSelection;
            slot.SetSelectionHighlight(equipmentPlayer != null && equipmentPlayer.SelectedEquipmentSlot == i);
        }
        RefreshAmmoSlots();
    }

    private void HandleEquipmentSlotSelection(BagSlot slot)
    {
        if (equipmentPlayer == null || IsInventoryEditLocked()) return;
        for (int i = 0; i < equipSlots.Length; i++)
            if (equipSlots[i] == slot) { equipmentPlayer.TrySelectEquipment(i); return; }
    }

    private void RefreshAmmoSlots()
    {
        if (ammoSlots == null) return;
        for (int i = 0; i < ammoSlots.Length; i++)
        {
            if (!(ammoSlots[i] is BagSlot slot)) continue;
            bool visible = equipmentPlayer != null && equipmentPlayer.RequiresAmmunition
                && equipmentPlayer.SelectedEquipmentSlot == i;
            int id = visible && boundAmmunitionBag != null ? boundAmmunitionBag.GetSlotItemId(0) : -1;
            int count = visible && boundAmmunitionBag != null ? boundAmmunitionBag.GetSlotCount(0) : 0;
            slot.Bind(visible ? boundAmmunitionBag : null, visible ? 0 : -1, id, count, 100);
            slot.gameObject.SetActive(visible && equipSlotsExpanded);
        }
    }

    private void UnbindEquipmentInventory()
    {
        if (equipmentPlayer != null) equipmentPlayer.EquipmentSelectionChanged -= RefreshEquipmentInventory;
        if (boundAmmunitionBag != null) boundAmmunitionBag.Changed -= RefreshAmmoSlots;
        if (equipSlots != null)
            for (int i = 0; i < equipSlots.Length; i++)
                if (equipSlots[i] is BagSlot slot) slot.EquipmentSelectionRequested -= HandleEquipmentSlotSelection;
        equipmentPlayer = null;
        boundAmmunitionBag = null;
        if (boundEquipmentBag != null) boundEquipmentBag.Changed -= RefreshEquipmentInventory;
        boundEquipmentBag = null;
        RefreshEquipmentInventory();
    }
}
