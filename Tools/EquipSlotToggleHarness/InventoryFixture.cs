using System;

public sealed class PlayerBag
{
    private Action changed;
    public event Action Changed { add => changed += value; remove => changed -= value; }
    internal int ListenerCount => changed?.GetInvocationList().Length ?? 0;
    internal int[] Ids = { -1, -1, -1, -1 }, Counts = new int[4];
    public int GetSlotItemId(int index) => Ids[index];
    public int GetSlotCount(int index) => Counts[index];
    internal void Notify() => changed?.Invoke();
}
public sealed class Player
{
    internal PlayerBag Equipment = new();
    internal PlayerBag Ammunition = new();
    public event Action EquipmentSelectionChanged;
    public int SelectedEquipmentSlot { get; private set; } = -1;
    public bool RequiresAmmunition => SelectedEquipmentSlot == 2;
    public PlayerBag GetEquipmentBag(int minimumSlotCount) => Equipment;
    public PlayerBag GetAmmunitionBag() => Ammunition;
    public bool TrySelectEquipment(int index)
    {
        if (Equipment.Counts[index] <= 0) return false;
        SelectedEquipmentSlot = SelectedEquipmentSlot == index ? -1 : index;
        EquipmentSelectionChanged?.Invoke();
        return true;
    }
}
public sealed class GameManager
{
    public static GameManager Instance;
    public Player Player;
}
public sealed class BagSlot : ItemSlot
{
    internal PlayerBag Bag;
    internal int Index, ItemId, Count, Capacity;
    internal bool Selected;
    public event Action<BagSlot> EquipmentSelectionRequested;
    internal void Click() => EquipmentSelectionRequested?.Invoke(this);
    public void SetSelectionHighlight(bool selected) => Selected = selected;
    public void Bind(PlayerBag bag, int index, int itemId, int count, int capacity)
    { Bag = bag; Index = index; ItemId = itemId; Count = count; Capacity = capacity; }
}
public partial class PlayerHUD
{
    private bool IsInventoryEditLocked() => false;
    internal void ConfigureAmmo(params ItemSlot[] slots) => ammoSlots = slots;
    internal void BindInventory() => BindEquipmentInventory();
    internal void UnbindInventory() => UnbindEquipmentInventory();
}
