using System;
using System.Collections.Generic;

// Engine-free data doubles. The complete production remapper is compiled by Run.ps1.
public class NamedObject { public string name; }
public class ItemDefinition { public int id; public string itemName, name; public NamedObject mapObject; }
public static class ItemDefinitionLookup
{
    public static ItemDefinition ResolveByStableName(IReadOnlyList<ItemDefinition> definitions, string name)
    {
        foreach (var definition in definitions)
            if (string.Equals(definition.itemName, name, StringComparison.OrdinalIgnoreCase)) return definition;
        return null;
    }
    public static ItemDefinition ResolveInstallationByStableName(IReadOnlyList<ItemDefinition> definitions, string name)
        => ResolveByStableName(definitions, name);
}
public class SaveItemCatalogEntry { public int itemId; public string itemName; }
public class SaveGameData { public List<SaveItemCatalogEntry> itemCatalog = new(); public MapSaveData map = new(); public PlayerSaveData player = new(); }
public class MapSaveData
{
    public List<ResourceSaveEntry> resources = new();
    public List<FloorObjectSaveEntry> floorObjects = new();
    public List<PlantedResourceSaveEntry> plantedResources = new();
    public List<ConveyorItemBlockSaveEntry> conveyorItems = new();
    public List<ConveyorItemRunSaveEntry> conveyorItemRuns = new();
    public List<InstallationSaveEntry> installations = new();
}
public class ResourceSaveEntry { public int itemId; }
public class FloorObjectSaveEntry { public List<int> itemIds = new(); }
public class PlantedResourceSaveEntry { public int seedItemId; }
public class ConveyorItemLaneSaveState { public int itemId; }
public class ConveyorItemBlockSaveEntry { public List<ConveyorItemLaneSaveState> lanes = new(); }
public class ConveyorItemTypeRunSaveEntry { public int itemId, count; }
public class ConveyorItemRunSaveEntry { public List<ConveyorItemTypeRunSaveEntry> itemRuns = new(); }
public class InstallationSaveEntry { public BlockStateStore.InstallationSaveState state; }
public class BlockStateStore
{
    public class InstallationSaveState
    {
        public int itemId, storedFluidItemId = -1, storedInstallationItemId = -1;
        public string itemName;
        public List<int> storedInstallationItemIds = new();
        public RobotArm.TransferState robotArmState;
        public InputOutputModule.PersistentState inputOutputState;
        public bool itemFilterMaskInitialized;
        public List<ulong> itemFilterMaskWords = new();
    }
}
public class RobotArm { public class TransferState { public int heldItemId; } }
public class InputOutputModule
{
    public struct PersistentInputItemAreaState { public int itemId; }
    public class PersistentState { public int activeOutputItemId; public List<PersistentInputItemAreaState> inputItemAreas = new(); }
}
public class PlayerSaveData
{
    public List<PlayerInventorySlotSaveState> bagSlots = new(), handSlots = new();
    public int activeTorchItemId = -1;
    public List<PlayerCraftingQueueEntrySaveData> craftingQueue = new();
}
public class PlayerInventorySlotSaveState { public int itemId; }
public class PlayerCraftingIngredientSaveData { public int itemId; }
public class PlayerCraftingQueueEntrySaveData { public int itemId; public List<PlayerCraftingIngredientSaveData> refundIngredients = new(); }

static class Checks
{
    static int checks;
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static bool Allowed(List<ulong> words, int id) => (words[id >> 6] & (1UL << (id & 63))) != 0;
    static SaveGameData Fixture(string savedItemName)
    {
        var data = new SaveGameData();
        if (savedItemName != null) data.itemCatalog.Add(new() { itemId = 51, itemName = savedItemName });
        data.map.floorObjects.Add(new() { itemIds = new() { -1000000001, 1, 51 } });
        data.map.installations.Add(new() { state = new() {
            itemId = 27, itemName = "Sturdy wooden box", itemFilterMaskInitialized = true,
            itemFilterMaskWords = new() { 1UL << 51, 0 },
            robotArmState = new() { heldItemId = 51 },
            inputOutputState = new() { activeOutputItemId = 51, inputItemAreas = new() { new() { itemId = 51 } } }
        } });
        data.player.bagSlots.Add(new() { itemId = 51 });
        data.map.conveyorItemRuns.Add(new() { itemRuns = new() { new() { itemId = 51, count = 3 } } });
        return data;
    }
    static void Verify(SaveGameData data, int expectedId)
    {
        var state = data.map.installations[0].state;
        Require(data.map.floorObjects[0].itemIds[2] == expectedId, "Stored pipe must not become rail on load");
        Require(data.map.floorObjects[0].itemIds[0] == -1000000001, "Stack sentinel must remain unchanged");
        Require(Allowed(state.itemFilterMaskWords, expectedId), "Box filter must retain the item's identity");
        Require(state.inputOutputState.activeOutputItemId == expectedId, "Pending machine output must retain its identity");
        Require(state.inputOutputState.inputItemAreas[0].itemId == expectedId, "Next machine input must retain its identity");
        Require(state.robotArmState.heldItemId == expectedId, "Held arm item must retain its identity");
        Require(data.player.bagSlots[0].itemId == expectedId, "Inventory item must retain its identity");
        Require(data.map.conveyorItemRuns[0].itemRuns[0].itemId == expectedId, "Belt run item must retain its identity");
    }
    static void Main()
    {
        var definitions = new List<ItemDefinition> {
            new() { id = 51, itemName = "Pipe" }, new() { id = 100, itemName = "Railload" },
            new() { id = 27, itemName = "Sturdy wooden box" }
        };
        var current = Fixture("Pipe");
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(current, definitions);
        Verify(current, 51);
        Require(!Allowed(current.map.installations[0].state.itemFilterMaskWords, 100), "Pipe-only filter must not enable rail");
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(current, definitions);
        Verify(current, 51);

        var all = Fixture("Pipe");
        all.map.installations[0].state.itemFilterMaskWords = new() { ulong.MaxValue, ulong.MaxValue };
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(all, definitions);
        Require(Allowed(all.map.installations[0].state.itemFilterMaskWords, 51), "Allow-all box must still allow pipes after load");

        var legacy = Fixture("Railload");
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(legacy, definitions);
        Verify(legacy, 100);

        var unknown = Fixture(null);
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(unknown, definitions);
        Verify(unknown, 51);

        var ambiguous = Fixture("Pipe");
        ambiguous.itemCatalog.Add(new() { itemId = 51, itemName = "Railload" });
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(ambiguous, definitions);
        Verify(ambiguous, 51);

        var renumbered = Fixture("Pipe");
        definitions[0].id = 120;
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(renumbered, definitions);
        Verify(renumbered, 120);
        Console.WriteLine($"PASS: {checks} production save ID remapping checks.");
    }
}
