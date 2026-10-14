using System.Reflection;
using UnityEngine;

internal static class CombatSaveChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        object compatibility = Enum.ToObject(typeof(SaveGameBinarySerializer).GetNestedType(
            "SaveReadCompatibilityMode", BindingFlags.NonPublic)!, 0);
        var building = new BlockStateStore.InstallationSaveState
        {
            itemId = 11, placementSequence = 123L, anchorCoordinate = new Vector2Int(4, 8),
            hasCombatHealth = true, combatHealth = 37f
        };
        Check(building.Clone().hasCombatHealth && building.Clone().combatHealth == 37f,
            "Installation clone preserves health");
        var nested = building.Clone();
        nested.combatHealth = 19f;
        building.storedInstallationItemIds = new List<int> { 11, 38 };
        building.storedInstallationItemHealth = new List<float> { 37f, 0f };
        building.robotArmState = new RobotArm.TransferState { heldItemId = 11, heldInstallationHealth = 23f };
        building.mountedInstallations.Add(new BlockStateStore.MountedInstallationSaveState
        {
            pointIndex = 0, installation = nested
        });
        var expected = new SaveGameData
        {
            player = new PlayerSaveData { hasPlayer = true, hasHealth = true, currentHealth = 0f },
            simulationTick = 987L, nextInstallationSimulationId = 456L
        };
        expected.map.installations.Add(new InstallationSaveEntry { state = building });
        expected.map.wreckage.Add(new InstallationSaveEntry
        {
            state = new BlockStateStore.InstallationSaveState
            {
                itemId = 38, placementSequence = 124L, anchorCoordinate = new Vector2Int(5, 8),
                hasCombatHealth = true, combatHealth = 0f, hasWorldPose = true,
                worldPosition = new Vector3(5f, 0f, 8f)
            }
        });
        expected.player.bagSlots.Add(new PlayerInventorySlotSaveState
        {
            slotIndex = 0, itemId = 11, count = 3, capacity = 10,
            installationHealth = new List<float> { 37f, 0f, 19f }
        });
        expected.map.resources.Add(new ResourceSaveEntry
        {
            itemId = 1, state = new Resource.ResourceSaveState
            { resourceCount = 42, currentGauge = 3, hasHealth = true, currentHealth = 88f }
        });
        expected.map.floorObjects.Add(new FloorObjectSaveEntry
        {
            itemIds = new List<int> { 11, Block.InputAreaCenterStackStateSentinel, 2, 11, 11 },
            installationHealth = new List<PortableItemHealthSaveState>
            { new() { index = 0, health = 37f }, new() { index = 4, health = 23f } }
        });
        var native = new ProjectF.Conveyors.BeltSavedLane
        {
            X = 4, Y = 8, Lane = 0,
            State = new ProjectF.Conveyors.BeltLaneState { ItemId = 11, InstallationHealth = 37f, Remaining = 4, Duration = 10 }
        };
        expected.map.conveyorItems.Add(new ConveyorItemBlockSaveEntry
        {
            lanes = new List<ConveyorItemLaneSaveState>
            { new() { laneIndex = 0, itemId = 11, installationHealth = 37f, nativeBeltState = native } }
        });
        expected.beltSimulation = new ProjectF.Conveyors.BeltSimulationSnapshot();
        expected.beltSimulation.Lanes.Add(native.Clone());
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            Call("WriteSaveGameData", writer, expected);
        stream.Position = 0;
        SaveGameData actual;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            actual = (SaveGameData)Call("ReadSaveGameData", reader, SaveGameData.CurrentVersion, compatibility);
        Check(stream.Position == stream.Length, "Combat save fields keep the full binary stream aligned");
        Check(actual.player.hasHealth && actual.player.currentHealth == 0f,
            "Saved dead player stays at zero HP");
        Check(actual.map.installations[0].state.hasCombatHealth
            && actual.map.installations[0].state.combatHealth == 37f, "Damaged building HP round trip");
        Check(actual.map.installations[0].state.mountedInstallations[0].installation.combatHealth == 19f,
            "Recursive mounted installation health round trip");
        Check(actual.simulationTick == 987L && actual.nextInstallationSimulationId == 456L,
            "Fields following player health are not shifted");
        Check(actual.map.wreckage.Count == 1 && actual.map.wreckage[0].state.combatHealth == 0f
            && actual.map.wreckage[0].state.worldPosition.x == 5f, "Dead wreckage appearance persists separately from active installations");
        Check(actual.player.bagSlots[0].installationHealth.SequenceEqual(new[] { 37f, 0f, 19f }),
            "Mixed pristine and damaged units in one inventory slot preserve individual HP");
        Check(actual.map.installations[0].state.storedInstallationItemHealth.SequenceEqual(new[] { 37f, 0f }),
            "Cargo preserves individual recovered HP");
        Check(actual.map.installations[0].state.robotArmState.heldInstallationHealth == 23f,
            "Arm-held recovered HP survives save/load");
        Check(actual.map.resources[0].state.hasHealth && actual.map.resources[0].state.currentHealth == 88f
            && actual.map.resources[0].state.resourceCount == 42 && actual.map.resources[0].state.currentGauge == 3,
            "Natural resource HP stays independent from resource count and work gauge");
        Check(actual.map.floorObjects[0].installationHealth[1].index == 4
            && actual.map.floorObjects[0].installationHealth[1].health == 23f, "Sparse floor/box metadata preserves index and HP");
        Check(actual.map.conveyorItems[0].lanes[0].installationHealth == 37f
            && actual.map.conveyorItems[0].lanes[0].nativeBeltState.State.InstallationHealth == 37f
            && actual.beltSimulation.Lanes[0].State.InstallationHealth == 37f, "Detailed and global native belt saves retain recovered HP");

        using var previousBuilding = new MemoryStream();
        using (var writer = new BinaryWriter(previousBuilding, System.Text.Encoding.UTF8, true))
            Call("WriteInstallationState", writer, nested);
        previousBuilding.SetLength(previousBuilding.Length - 4); // v73 nullable cargo list tail.
        previousBuilding.Position = 0;
        using (var reader = new BinaryReader(previousBuilding, System.Text.Encoding.UTF8, true))
        {
            var restored = (BlockStateStore.InstallationSaveState)Call("ReadInstallationState", reader, 72, compatibility);
            Check(restored.hasCombatHealth && restored.combatHealth == 19f && restored.storedInstallationItemHealth == null
                && previousBuilding.Position == previousBuilding.Length, "v72 installation HP remains aligned before cargo metadata");
        }
        using var previousResource = new MemoryStream();
        using (var writer = new BinaryWriter(previousResource, System.Text.Encoding.UTF8, true))
            Call("WriteResourceEntry", writer, expected.map.resources[0]);
        previousResource.SetLength(previousResource.Length - 5);
        previousResource.Position = 0;
        using (var reader = new BinaryReader(previousResource, System.Text.Encoding.UTF8, true))
        {
            var restored = (ResourceSaveEntry)Call("ReadResourceEntry", reader, 72);
            Check(!restored.state.hasHealth && restored.state.resourceCount == 42
                && previousResource.Position == previousResource.Length, "v72 resource entry remains aligned and defaults to full HP");
        }

        var inventoryType = typeof(BlockStateStore).GetNestedType("SavedFloorAreaInventory", BindingFlags.NonPublic)!;
        var encoded = new List<int> { Block.FloorStackStateSentinel, 1, 2, 11, 11,
            Block.ConveyorStackStateSentinel, 2, 38, -1, Block.InputAreaCenterStackStateSentinel, 2, 11, 11 };
        var metadata = new List<PortableItemHealthSaveState>
        { new() { index = 3, health = 37f }, new() { index = 7, health = 17f }, new() { index = 12, health = 23f } };
        object inventory = inventoryType.GetMethod("FromSerialized")!.Invoke(null, new object[] { encoded, metadata })!;
        var floorHealth = (List<float>)inventoryType.GetField("floorHealth")!.GetValue(inventory)!;
        Check(floorHealth.SequenceEqual(new[] { 37f, 0f }), "Unloaded stack parser retains HP independently from encoded counts");
        var args = new object[] { null! };
        var flattened = (List<int>)inventoryType.GetMethod("ToSerialized")!.Invoke(inventory, args)!;
        var remapped = (List<PortableItemHealthSaveState>)args[0];
        Check(remapped.Select(x => x.index).SequenceEqual(new[] { 0, 4, 9 })
            && remapped.Select(x => x.health).SequenceEqual(new[] { 37f, 17f, 23f }),
            "Unloaded stack repacking reindexes floor, belt and box metadata");
        ((List<int>)inventoryType.GetField("floorItems")!.GetValue(inventory)!).RemoveAt(0);
        floorHealth.RemoveAt(0);
        inventoryType.GetMethod("ToSerialized")!.Invoke(inventory, args);
        Check(((List<PortableItemHealthSaveState>)args[0]).Select(x => x.index).SequenceEqual(new[] { 3, 8 }),
            "Taking one unloaded floor item cannot shift another item's HP incorrectly");
        var migration = new MapSaveData();
        migration.floorObjects.Add(new FloorObjectSaveEntry { itemIds = flattened, installationHealth = remapped });
        SaveGameConveyorItemBackfill.BackfillFromFloorObjects(migration);
        SaveGameConveyorItemBackfill.StripConveyorItemsFromFloorObjects(migration);
        Check(migration.conveyorItems[0].lanes[0].installationHealth == 17f
            && migration.floorObjects[0].installationHealth.Select(x => x.index).SequenceEqual(new[] { 0, 5 }),
            "Belt extraction preserves cargo HP and reindexes remaining floor/box HP");

        // v72 only appends five bytes to each leaf installation/player record.
        // Truncating those tails creates actual v71 payloads for the old reader branch.
        using var legacyPlayer = new MemoryStream();
        using (var writer = new BinaryWriter(legacyPlayer, System.Text.Encoding.UTF8, true))
            Call("WritePlayer", writer, new PlayerSaveData { hasPlayer = true, bagLevel = 3 });
        legacyPlayer.SetLength(legacyPlayer.Length - 5);
        legacyPlayer.Position = 0;
        using (var reader = new BinaryReader(legacyPlayer, System.Text.Encoding.UTF8, true))
        {
            var restored = (PlayerSaveData)Call("ReadPlayer", reader, 71);
            Check(!restored.hasHealth && restored.bagLevel == 3,
                "Pre-health player save restores without interpreting absent HP as death");
            Check(legacyPlayer.Position == legacyPlayer.Length, "Legacy player stream stays aligned");
        }
        using var legacyBuilding = new MemoryStream();
        using (var writer = new BinaryWriter(legacyBuilding, System.Text.Encoding.UTF8, true))
            Call("WriteInstallationState", writer, nested);
        legacyBuilding.SetLength(legacyBuilding.Length - 9);
        legacyBuilding.Position = 0;
        using (var reader = new BinaryReader(legacyBuilding, System.Text.Encoding.UTF8, true))
        {
            var restored = (BlockStateStore.InstallationSaveState)Call(
                "ReadInstallationState", reader, 71, compatibility);
            Check(!restored.hasCombatHealth && restored.placementSequence == 123L,
                "Pre-health building save restores with default health");
            Check(legacyBuilding.Position == legacyBuilding.Length, "Legacy installation stream stays aligned");
        }
        Console.WriteLine($"PASS {checks} combat save compatibility checks");
        return checks;
    }

    private static object Call(string name, params object[] arguments) =>
        typeof(SaveGameBinarySerializer).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, arguments)!;
}
