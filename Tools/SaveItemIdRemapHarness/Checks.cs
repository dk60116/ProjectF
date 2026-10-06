using System;
using System.Collections.Generic;

// Engine-free data doubles. The complete production remapper is compiled by Run.ps1.
public class NamedObject { public string name; }
public class Railload : NamedObject { }
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
    public static ItemDefinition ResolveInstallationById(IReadOnlyList<ItemDefinition> definitions, int id)
    {
        foreach (var definition in definitions)
            if (definition.id == id && id >= 0) return definition;
        return null;
    }
}
public class SaveItemCatalogEntry { public int itemId; public string itemName; }
public class SaveGameData
{
    public List<SaveItemCatalogEntry> itemCatalog = new();
    public MapSaveData map = new();
    public PlayerSaveData player = new();
    public ProjectF.Conveyors.BeltSimulationSnapshot beltSimulation;
}
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
public class ConveyorItemLaneSaveState
{
    public int itemId;
    public ProjectF.Conveyors.BeltSavedLane nativeBeltState;
}
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
        public List<int> railVisualPathPoints = new();
        public List<MountedInstallationSaveState> mountedInstallations = new();
    }
    public class MountedInstallationSaveState { public InstallationSaveState installation; }
}
public class RobotArm { public class TransferState { public int heldItemId; } }
public class InputOutputModule
{
    public struct PersistentInputItemAreaState { public int itemId; }
    public class PersistentState
    {
        public int activeOutputItemId;
        public int seedPlanterLoadedSeedItemId;
        public List<PersistentInputItemAreaState> inputItemAreas = new();
    }
}
namespace ProjectF.Conveyors
{
    public struct BeltLaneState { public int ItemId; }
    public class BeltSavedLane { public BeltLaneState State; }
    public class BeltSimulationSnapshot { public List<BeltSavedLane> Lanes = new(); }
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
            inputOutputState = new() {
                activeOutputItemId = 51,
                seedPlanterLoadedSeedItemId = 51,
                inputItemAreas = new() { new() { itemId = 51 } }
            }
        } });
        data.player.bagSlots.Add(new() { itemId = 51 });
        data.map.conveyorItemRuns.Add(new() { itemRuns = new() { new() { itemId = 51, count = 3 } } });
        data.map.conveyorItems.Add(new() { lanes = new() {
            new() { itemId = 51, nativeBeltState = new() { State = new() { ItemId = 51 } } }
        } });
        data.beltSimulation = new() { Lanes = new() {
            new() { State = new() { ItemId = 51 } }
        } };
        return data;
    }
    static void Verify(SaveGameData data, int expectedId)
    {
        var state = data.map.installations[0].state;
        Require(data.map.floorObjects[0].itemIds[2] == expectedId, "Stored pipe must not become rail on load");
        Require(data.map.floorObjects[0].itemIds[0] == -1000000001, "Stack sentinel must remain unchanged");
        Require(Allowed(state.itemFilterMaskWords, expectedId), "Box filter must retain the item's identity");
        Require(state.inputOutputState.activeOutputItemId == expectedId, "Pending machine output must retain its identity");
        Require(state.inputOutputState.seedPlanterLoadedSeedItemId == expectedId, "Loaded seed must retain its identity");
        Require(state.inputOutputState.inputItemAreas[0].itemId == expectedId, "Next machine input must retain its identity");
        Require(state.robotArmState.heldItemId == expectedId, "Held arm item must retain its identity");
        Require(data.player.bagSlots[0].itemId == expectedId, "Inventory item must retain its identity");
        Require(data.map.conveyorItemRuns[0].itemRuns[0].itemId == expectedId, "Belt run item must retain its identity");
        Require(data.map.conveyorItems[0].lanes[0].nativeBeltState.State.ItemId == expectedId,
            "Native belt lane item must retain its identity");
        Require(data.beltSimulation.Lanes[0].State.ItemId == expectedId,
            "Belt simulation item must retain its identity");
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
        VerifyInstallationIdentity();
        Console.WriteLine($"PASS: {checks} production save ID remapping checks.");
    }

    static void VerifyInstallationIdentity()
    {
        var rail = new ItemDefinition { id = 119, itemName = "Railload", mapObject = new Railload() };
        var definitions = new List<ItemDefinition> { rail, new() { id = 27, itemName = "Box" } };
        foreach (string name in new[] { null, "", " ", "Retired name" })
        {
            var data = new SaveGameData();
            var state = new BlockStateStore.InstallationSaveState { itemId = 119, itemName = name };
            data.map.installations.Add(new() { state = state });
            SaveGameItemIdRemapper.RemapToCurrentDefinitions(data, definitions);
            Require(state.itemId == 119, "Failed name lookup must preserve the original installation ID");
        }

        var catalog = new SaveGameData();
        catalog.itemCatalog.Add(new() { itemId = 47, itemName = "Railload" });
        var oldRail = new BlockStateStore.InstallationSaveState { itemId = 47, itemName = "" };
        oldRail.mountedInstallations.Add(new() { installation = new() { itemId = 27, itemName = "" } });
        catalog.map.installations.Add(new() { state = oldRail });
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(catalog, definitions);
        Require(oldRail.itemId == 119, "Unnamed installation must use its saved catalog ID");
        Require(oldRail.itemName == "Railload", "Resolved installation must acquire a stable name");
        Require(oldRail.mountedInstallations[0].installation.itemId == 27, "Mounted installation ID must survive empty names");

        // Models the real save: old ECS rails were persisted as -1 with their path intact.
        var damaged = new SaveGameData();
        var recoverable = new BlockStateStore.InstallationSaveState { itemId = -1, itemName = "", railVisualPathPoints = new() { 0, 1 } };
        var unrelated = new BlockStateStore.InstallationSaveState { itemId = -1, itemName = "" };
        var incomplete = new BlockStateStore.InstallationSaveState { itemId = -1, itemName = "", railVisualPathPoints = new() { 0 } };
        var unknown = new BlockStateStore.InstallationSaveState { itemId = -1, itemName = "Removed rail", railVisualPathPoints = new() { 0, 1 } };
        var validOther = new BlockStateStore.InstallationSaveState { itemId = 27, itemName = "", railVisualPathPoints = new() { 0, 1 } };
        foreach (var state in new[] { recoverable, unrelated, incomplete, unknown, validOther })
            damaged.map.installations.Add(new() { state = state });
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(damaged, definitions);
        Require(recoverable.itemId == 119 && recoverable.itemName == "Railload", "Nameless damaged ECS rail must recover from its unique rail definition");
        Require(unrelated.itemId == -1 && incomplete.itemId == -1, "Invalid non-rail and incomplete geometry must not become rails");
        Require(unknown.itemId == -1, "A named missing definition must not be guessed from geometry");
        Require(validOther.itemId == 27, "A valid non-rail ID must not be overwritten by geometry");
        SaveGameItemIdRemapper.RemapToCurrentDefinitions(damaged, definitions);
        Require(recoverable.itemId == 119, "Recovered rail must survive repeated loads");

        foreach (var candidates in new[] {
            new List<ItemDefinition> { definitions[1] },
            new List<ItemDefinition> { rail, new() { id = 120, itemName = "Other rail", mapObject = new Railload() } }
        })
        {
            recoverable.itemId = -1; recoverable.itemName = "";
            SaveGameItemIdRemapper.RemapToCurrentDefinitions(damaged, candidates);
            Require(recoverable.itemId == -1, "Missing or ambiguous rail definitions must not be guessed");
        }
    }
}
