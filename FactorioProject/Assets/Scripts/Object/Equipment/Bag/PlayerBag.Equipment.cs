using System.Collections.Generic;
using UnityEngine;

public partial class PlayerBag
{
    public bool IsEquipmentStorage { get; private set; }
    public bool IsAmmoStorage { get; private set; }
    public bool IsDataOnlyStorage => IsEquipmentStorage || IsAmmoStorage;
    private ItemDefinition ammoWeapon;

    internal void EnsureEquipmentSlots(int minimumSlotCount)
    {
        EnsureHiddenStorage(minimumSlotCount, 1, false);
    }

    internal void EnsureAmmoStorage(ItemDefinition weapon)
    {
        ammoWeapon = weapon;
        EnsureHiddenStorage(1, 100, true);
    }

    private void EnsureHiddenStorage(int minimumSlotCount, int capacity, bool ammunition)
    {
        EnsureInitialized();
        IsAmmoStorage = ammunition;
        IsEquipmentStorage = !ammunition;
        while (portableStack.Count < minimumSlotCount)
        {
            var stack = new List<PortableObject>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                var item = PortableObject.Create(transform.position, Quaternion.identity, Vector3.one,
                    gameObject.layer, ammunition ? "Ammunition Item" : "Equipment Item");
                item.SetCachedParent(transform, false);
                item.SetCachedActive(false);
                item.SetVisualRenderingSuppressed(true);
                stack.Add(item);
            }
            portableStack.Add(new PortableStack { stack = stack });
            currentStack.Add(0);
        }
        EnsureVisualPreservedStackCounts();
    }

    private static bool AcceptsEquipmentItem(int itemId)
    {
        ItemManager manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        return manager != null && manager.TryGetItemDefinitionById(itemId, out ItemDefinition definition)
            && definition != null && definition.isEquipment;
    }

    private bool AcceptsStorageItem(int itemId)
    {
        if (!IsDataOnlyStorage) return true;
        if (IsEquipmentStorage) return AcceptsEquipmentItem(itemId);
        ItemManager manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        return manager != null && manager.TryGetItemDefinitionById(itemId, out ItemDefinition definition)
            && ammoWeapon != null && ammoWeapon.AcceptsAmmo(definition);
    }

    private void OnDestroy()
    {
        if (!IsDataOnlyStorage || portableStack == null) return;
        for (int i = 0; i < portableStack.Count; i++)
            if (portableStack[i]?.stack != null)
                for (int j = 0; j < portableStack[i].stack.Count; j++) portableStack[i].stack[j]?.Dispose();
    }

    private void RefreshEquipmentPresentation()
    {
        if (!IsDataOnlyStorage) return;
        for (int i = 0; i < portableStack.Count; i++)
            for (int j = 0; j < portableStack[i].stack.Count; j++)
                portableStack[i].stack[j].SetVisualRenderingSuppressed(true);
    }
}
