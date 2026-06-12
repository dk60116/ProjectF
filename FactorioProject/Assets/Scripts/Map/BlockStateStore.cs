using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore : MonoBehaviour
{
    public sealed class InstallationSaveState
    {
        public Vector2Int anchorCoordinate;
        public int itemId;
        public int quarterTurns;
        public long placementSequence;
        public bool hasStorageKey;
        public Vector2Int storageKey;
        public int conveyorVariantKind = -1;
        public List<Vector2Int> occupiedCoordinates = new List<Vector2Int>();
        public List<Vector2> railVisualPathPoints = new List<Vector2>();
        public bool railVisualPathExtendsStart = true;
        public bool railVisualPathExtendsEnd = true;
        public InputOutputModule.PersistentState inputOutputState;
        public RobotArm.PersistentState robotArmState;
        public long lastBackgroundSimulationTicks;
        public bool? boxIsOpen;
        public bool itemFilterMaskInitialized;
        public List<ulong> itemFilterMaskWords = new List<ulong>();
        public float storedFluidLiters;
        public int storedFluidItemId = -1;
        public float storedFluidTemperatureCelsius = MapClimate.DefaultCurrentTemperatureCelsius;

        public InstallationSaveState Clone()
        {
            return new InstallationSaveState
            {
                anchorCoordinate = anchorCoordinate,
                itemId = itemId,
                quarterTurns = quarterTurns,
                placementSequence = placementSequence,
                hasStorageKey = hasStorageKey,
                storageKey = storageKey,
                conveyorVariantKind = conveyorVariantKind,
                occupiedCoordinates = new List<Vector2Int>(occupiedCoordinates ?? new List<Vector2Int>()),
                railVisualPathPoints = new List<Vector2>(railVisualPathPoints ?? new List<Vector2>()),
                railVisualPathExtendsStart = railVisualPathExtendsStart,
                railVisualPathExtendsEnd = railVisualPathExtendsEnd,
                inputOutputState = inputOutputState != null ? inputOutputState.Clone() : null,
                robotArmState = robotArmState != null ? robotArmState.Clone() : null,
                lastBackgroundSimulationTicks = lastBackgroundSimulationTicks,
                boxIsOpen = boxIsOpen,
                itemFilterMaskInitialized = itemFilterMaskInitialized,
                itemFilterMaskWords = new List<ulong>(itemFilterMaskWords ?? new List<ulong>()),
                storedFluidLiters = storedFluidLiters,
                storedFluidItemId = storedFluidItemId,
                storedFluidTemperatureCelsius = storedFluidTemperatureCelsius
            };
        }
    }

    private sealed class LiveInstallationRecord
    {
        public InstallationObject installationObject;
        public InstallationSaveState state;
    }

    private readonly struct IntRun
    {
        public readonly int value;
        public readonly int count;

        public IntRun(int value, int count)
        {
            this.value = value;
            this.count = count;
        }
    }

    private sealed class FloorObjectSaveState
    {
        private readonly int[] rawItems;
        private readonly IntRun[] compressedRuns;
        private readonly int itemCount;

        private FloorObjectSaveState(int[] rawItems, IntRun[] compressedRuns, int itemCount)
        {
            this.rawItems = rawItems;
            this.compressedRuns = compressedRuns;
            this.itemCount = itemCount;
        }

        public static FloorObjectSaveState FromSerialized(IReadOnlyList<int> itemIds)
        {
            if (itemIds == null || itemIds.Count <= 0)
            {
                return null;
            }

            int count = itemIds.Count;
            int runCount = 1;
            int previousValue = itemIds[0];
            for (int i = 1; i < count; i++)
            {
                int value = itemIds[i];
                if (value == previousValue)
                {
                    continue;
                }

                runCount++;
                previousValue = value;
            }

            // Large item stacks repeat the same item id, so RLE keeps saved block state compact.
            bool shouldCompress = runCount * 2 < count;
            if (!shouldCompress)
            {
                int[] rawCopy = new int[count];
                for (int i = 0; i < count; i++)
                {
                    rawCopy[i] = itemIds[i];
                }

                return new FloorObjectSaveState(rawCopy, null, count);
            }

            IntRun[] runs = new IntRun[runCount];
            int runIndex = 0;
            int currentValue = itemIds[0];
            int currentCount = 1;
            for (int i = 1; i < count; i++)
            {
                int value = itemIds[i];
                if (value == currentValue)
                {
                    currentCount++;
                    continue;
                }

                runs[runIndex++] = new IntRun(currentValue, currentCount);
                currentValue = value;
                currentCount = 1;
            }

            runs[runIndex] = new IntRun(currentValue, currentCount);
            return new FloorObjectSaveState(null, runs, count);
        }

        public List<int> ToSerializedList()
        {
            List<int> itemIds = new List<int>(itemCount);
            if (rawItems != null)
            {
                itemIds.AddRange(rawItems);
                return itemIds;
            }

            if (compressedRuns == null)
            {
                return itemIds;
            }

            for (int runIndex = 0; runIndex < compressedRuns.Length; runIndex++)
            {
                IntRun run = compressedRuns[runIndex];
                for (int i = 0; i < run.count; i++)
                {
                    itemIds.Add(run.value);
                }
            }

            return itemIds;
        }
    }

    private readonly Dictionary<Vector2Int, Resource.ResourceSaveState> savedStates = new Dictionary<Vector2Int, Resource.ResourceSaveState>();
    private readonly Dictionary<Vector2Int, int> savedResourceItemIds = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, FloorObjectSaveState> savedFloorObjectStates = new Dictionary<Vector2Int, FloorObjectSaveState>();
    private readonly Dictionary<Vector2Int, InstallationSaveState> savedInstallationStates = new Dictionary<Vector2Int, InstallationSaveState>();
    private readonly Dictionary<Vector2Int, Vector2Int> savedInstallationAnchorsByCoordinate = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Dictionary<Vector2Int, LiveInstallationRecord> liveInstallationStates = new Dictionary<Vector2Int, LiveInstallationRecord>();
    private readonly Dictionary<Vector2Int, Vector2Int> liveInstallationAnchorsByCoordinate = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Dictionary<int, int> savedInstallationCountsByItemId = new Dictionary<int, int>();
    private int savedInstallationItemTotal;
    private VirtualObjectWorld virtualObjectWorld;

    public void Save(Vector2Int worldCoordinate, Resource resource)
    {
        if (resource == null)
        {
            return;
        }

        Resource.ResourceSaveState state = resource.CaptureState();
        savedStates[worldCoordinate] = state;
        int itemId = resource.ResolveItemId();
        savedResourceItemIds[worldCoordinate] = itemId;
        ResolveVirtualObjectWorld()?.UpsertResource(worldCoordinate, itemId, state);
    }

    public void SaveFloorObjects(Vector2Int worldCoordinate, Block block, VirtualObjectResidency residency)
    {
        if (block == null)
        {
            return;
        }

        List<int> itemIds = block.CaptureFloorObjectState();
        if (itemIds == null || itemIds.Count == 0)
        {
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        FloorObjectSaveState state = FloorObjectSaveState.FromSerialized(itemIds);
        if (state == null)
        {
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        savedFloorObjectStates[worldCoordinate] = state;
        ResolveVirtualObjectWorld()?.UpsertFloorItemStack(worldCoordinate, itemIds, residency);
    }

    public bool TryGet(Vector2Int worldCoordinate, out Resource.ResourceSaveState state)
    {
        return savedStates.TryGetValue(worldCoordinate, out state);
    }

    public bool IsDepleted(Vector2Int worldCoordinate)
    {
        return savedStates.TryGetValue(worldCoordinate, out Resource.ResourceSaveState state)
               && state.resourceCount <= 0;
    }

    public bool TryGetFloorObjects(Vector2Int worldCoordinate, out List<int> itemIds)
    {
        if (savedFloorObjectStates.TryGetValue(worldCoordinate, out FloorObjectSaveState savedState) && savedState != null)
        {
            itemIds = savedState.ToSerializedList();
            return true;
        }

        itemIds = null;
        return false;
    }

    public bool TryGetFloorObjectsCopy(Vector2Int worldCoordinate, out List<int> itemIds)
    {
        if (savedFloorObjectStates.TryGetValue(worldCoordinate, out FloorObjectSaveState savedState) && savedState != null)
        {
            itemIds = savedState.ToSerializedList();
            return true;
        }

        itemIds = null;
        return false;
    }

    public void SetFloorObjects(Vector2Int worldCoordinate, IReadOnlyList<int> itemIds)
    {
        if (itemIds == null || itemIds.Count <= 0)
        {
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        FloorObjectSaveState state = FloorObjectSaveState.FromSerialized(itemIds);
        if (state == null)
        {
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        savedFloorObjectStates[worldCoordinate] = state;
        ResolveVirtualObjectWorld()?.UpsertFloorItemStack(worldCoordinate, itemIds);
    }

    public void SetFloorObjectsResidency(Vector2Int worldCoordinate, VirtualObjectResidency residency)
    {
        if (!savedFloorObjectStates.TryGetValue(worldCoordinate, out FloorObjectSaveState savedState) || savedState == null)
        {
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        ResolveVirtualObjectWorld()?.UpsertFloorItemStack(worldCoordinate, savedState.ToSerializedList(), residency);
    }

    public void SaveInstallation(InstallationObject installationObject)
    {
        if (!TryBuildInstallationState(installationObject, out InstallationSaveState state))
        {
            return;
        }

        state.lastBackgroundSimulationTicks = DateTime.UtcNow.Ticks;
        StoreInstallationState(state);
    }

    public void RegisterLiveInstallation(InstallationObject installationObject)
    {
        if (!TryBuildInstallationState(installationObject, out InstallationSaveState state))
        {
            return;
        }

        RegisterLiveInstallation(installationObject, state);
    }

    public void RegisterLiveInstallation(InstallationObject installationObject, InstallationSaveState state)
    {
        if (installationObject == null || state == null)
        {
            return;
        }

        if (!StoreInstallationState(state, out Vector2Int storageKey, out InstallationSaveState storedState))
        {
            return;
        }

        RemoveLiveInstallationRecordsForSamePlacement(installationObject, storedState, storageKey);

        if (liveInstallationStates.TryGetValue(storageKey, out LiveInstallationRecord existingRecord))
        {
            UnregisterLiveCoordinateMappings(existingRecord.state, storageKey);
        }

        liveInstallationStates[storageKey] = new LiveInstallationRecord
        {
            installationObject = installationObject,
            state = storedState.Clone()
        };
        RegisterLiveCoordinateMappings(storedState, storageKey);
        ResolveVirtualObjectWorld()?.UpsertInstallation(storedState, VirtualObjectResidency.Live, installationObject);
    }

    public bool TryGetInstallationState(Vector2Int storageKey, out InstallationSaveState state)
    {
        if (savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState savedState) && savedState != null)
        {
            state = savedState.Clone();
            return true;
        }

        state = null;
        return false;
    }

    public void UpdateInstallationState(InstallationSaveState state)
    {
        if (state == null)
        {
            return;
        }

        StoreInstallationState(state);
    }

    public List<Vector2Int> GetSavedInstallationStorageKeys()
    {
        return new List<Vector2Int>(savedInstallationStates.Keys);
    }

    public int GetInstallationItemCounts(Dictionary<int, int> countsByItemId)
    {
        countsByItemId?.Clear();

        if (countsByItemId != null)
        {
            foreach (KeyValuePair<int, int> pair in savedInstallationCountsByItemId)
            {
                countsByItemId[pair.Key] = pair.Value;
            }
        }

        return savedInstallationItemTotal;
    }

    public bool TryGetLiveInstallation(Vector2Int storageKey, out InstallationObject installationObject, out InstallationSaveState state)
    {
        if (liveInstallationStates.TryGetValue(storageKey, out LiveInstallationRecord record)
            && record != null
            && record.installationObject != null)
        {
            installationObject = record.installationObject;
            state = record.state != null ? record.state.Clone() : null;
            return true;
        }

        if (liveInstallationStates.ContainsKey(storageKey))
        {
            UnregisterLiveInstallation(storageKey);
        }

        installationObject = null;
        state = null;
        return false;
    }

    public bool TryDetachLiveInstallation(Vector2Int storageKey, out InstallationObject installationObject, out InstallationSaveState state)
    {
        installationObject = null;
        state = null;

        if (!liveInstallationStates.TryGetValue(storageKey, out LiveInstallationRecord record)
            || record == null
            || record.installationObject == null)
        {
            if (liveInstallationStates.ContainsKey(storageKey))
            {
                UnregisterLiveInstallation(storageKey);
            }

            return false;
        }

        installationObject = record.installationObject;
        state = record.state != null ? record.state.Clone() : null;
        UnregisterLiveCoordinateMappings(record.state, storageKey);
        liveInstallationStates.Remove(storageKey);

        VirtualObjectWorld world = ResolveVirtualObjectWorld();
        if (savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState savedState))
        {
            world?.UpsertInstallation(savedState);
        }
        else
        {
            world?.RemoveInstallation(storageKey);
        }

        return true;
    }

    public bool TryGetInstallationAnchorAtCoordinate(Vector2Int worldCoordinate, out Vector2Int storageKey)
    {
        if (liveInstallationAnchorsByCoordinate.TryGetValue(worldCoordinate, out storageKey))
        {
            return true;
        }

        return savedInstallationAnchorsByCoordinate.TryGetValue(worldCoordinate, out storageKey);
    }

    public List<Vector2Int> GetLiveInstallationStorageKeys()
    {
        return new List<Vector2Int>(liveInstallationStates.Keys);
    }

    public void UnregisterLiveInstallation(Vector2Int storageKey)
    {
        if (!liveInstallationStates.TryGetValue(storageKey, out LiveInstallationRecord record))
        {
            return;
        }

        UnregisterLiveCoordinateMappings(record.state, storageKey);
        liveInstallationStates.Remove(storageKey);

        VirtualObjectWorld world = ResolveVirtualObjectWorld();
        if (savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState savedState))
        {
            world?.UpsertInstallation(savedState);
        }
        else
        {
            world?.RemoveInstallation(storageKey);
        }
    }

    public void RemoveInstallation(Vector2Int storageKey)
    {
        if (savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState savedState))
        {
            AdjustSavedInstallationCount(savedState, -1);
            UnregisterSavedCoordinateMappings(savedState, storageKey);
            savedInstallationStates.Remove(storageKey);
        }

        UnregisterLiveInstallation(storageKey);
        ResolveVirtualObjectWorld()?.RemoveInstallation(storageKey);
    }

    public void RemoveInstallation(InstallationObject installationObject)
    {
        if (installationObject == null)
        {
            return;
        }

        if (TryBuildInstallationState(installationObject, out InstallationSaveState state))
        {
            RemoveInstallation(ResolveInstallationStorageKey(state, savedInstallationStates));
            return;
        }

        if (installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            RemoveInstallation(anchorCoordinate);
        }
    }

    public void ClearStates()
    {
        savedStates.Clear();
        savedResourceItemIds.Clear();
        savedFloorObjectStates.Clear();
        savedConveyorItemStates.Clear();
        savedInstallationStates.Clear();
        savedInstallationCountsByItemId.Clear();
        savedInstallationItemTotal = 0;
        savedInstallationAnchorsByCoordinate.Clear();
        liveInstallationStates.Clear();
        liveInstallationAnchorsByCoordinate.Clear();
        ResolveVirtualObjectWorld()?.Clear();
    }

    public void CaptureSaveState(MapSaveData mapSaveData)
    {
        if (mapSaveData == null)
        {
            return;
        }

        SimulateSavedConveyorItems();

        mapSaveData.resources ??= new List<ResourceSaveEntry>();
        mapSaveData.floorObjects ??= new List<FloorObjectSaveEntry>();
        mapSaveData.installations ??= new List<InstallationSaveEntry>();
        mapSaveData.conveyorItems ??= new List<ConveyorItemBlockSaveEntry>();
        mapSaveData.resources.Clear();
        mapSaveData.floorObjects.Clear();
        mapSaveData.installations.Clear();
        mapSaveData.conveyorItems.Clear();

        foreach (KeyValuePair<Vector2Int, Resource.ResourceSaveState> pair in savedStates)
        {
            savedResourceItemIds.TryGetValue(pair.Key, out int itemId);
            mapSaveData.resources.Add(new ResourceSaveEntry
            {
                coordinate = pair.Key,
                itemId = itemId,
                state = pair.Value
            });
        }

        foreach (KeyValuePair<Vector2Int, FloorObjectSaveState> pair in savedFloorObjectStates)
        {
            if (pair.Value == null)
            {
                continue;
            }

            mapSaveData.floorObjects.Add(new FloorObjectSaveEntry
            {
                coordinate = pair.Key,
                itemIds = pair.Value.ToSerializedList()
            });
        }

        foreach (KeyValuePair<Vector2Int, ConveyorItemBlockState> pair in savedConveyorItemStates)
        {
            ConveyorItemBlockState state = pair.Value;
            if (state == null || state.lanes.Count <= 0)
            {
                continue;
            }

            mapSaveData.conveyorItems.Add(new ConveyorItemBlockSaveEntry
            {
                coordinate = pair.Key,
                lanes = CloneConveyorLaneStates(state.lanes)
            });
        }

        foreach (KeyValuePair<Vector2Int, InstallationSaveState> pair in savedInstallationStates)
        {
            if (pair.Value == null)
            {
                continue;
            }

            mapSaveData.installations.Add(new InstallationSaveEntry
            {
                state = pair.Value.Clone()
            });
        }
    }

    public void ApplySaveState(MapSaveData mapSaveData)
    {
        ClearStates();
        if (mapSaveData == null)
        {
            return;
        }

        VirtualObjectWorld world = ResolveVirtualObjectWorld();

        if (mapSaveData.resources != null)
        {
            for (int i = 0; i < mapSaveData.resources.Count; i++)
            {
                ResourceSaveEntry entry = mapSaveData.resources[i];
                if (entry == null)
                {
                    continue;
                }

                savedStates[entry.coordinate] = entry.state;
                savedResourceItemIds[entry.coordinate] = entry.itemId;
                if (entry.itemId >= 0)
                {
                    world?.UpsertResource(entry.coordinate, entry.itemId, entry.state);
                }
            }
        }

        if (mapSaveData.floorObjects != null)
        {
            for (int i = 0; i < mapSaveData.floorObjects.Count; i++)
            {
                FloorObjectSaveEntry entry = mapSaveData.floorObjects[i];
                if (entry == null)
                {
                    continue;
                }

                SetFloorObjects(entry.coordinate, entry.itemIds);
            }
        }

        if (mapSaveData.installations != null)
        {
            for (int i = 0; i < mapSaveData.installations.Count; i++)
            {
                InstallationSaveEntry entry = mapSaveData.installations[i];
                if (entry?.state == null)
                {
                    continue;
                }

                StoreInstallationState(entry.state);
            }
        }

        if (mapSaveData.conveyorItems != null)
        {
            for (int i = 0; i < mapSaveData.conveyorItems.Count; i++)
            {
                ConveyorItemBlockSaveEntry entry = mapSaveData.conveyorItems[i];
                if (entry == null)
                {
                    continue;
                }

                SetConveyorItems(entry.coordinate, entry.lanes);
            }
        }
    }

    private bool TryBuildInstallationState(InstallationObject installationObject, out InstallationSaveState state)
    {
        state = null;
        if (installationObject == null
            || !installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        int itemId = ResolveInstallationSaveItemId(installationObject);
        if (itemId < 0 || installationObject.RuntimeOccupiedCoordinates == null || installationObject.RuntimeOccupiedCoordinates.Count <= 0)
        {
            return false;
        }

        state = new InstallationSaveState
        {
            anchorCoordinate = anchorCoordinate,
            itemId = itemId,
            quarterTurns = ((quarterTurns % 4) + 4) % 4,
            placementSequence = installationObject.RuntimePlacementSequence,
            occupiedCoordinates = new List<Vector2Int>(installationObject.RuntimeOccupiedCoordinates)
        };

        if (installationObject is ConvayorBelt2F)
        {
            state.conveyorVariantKind = -1;
        }
        else if (installationObject is ConveyorBelt conveyorBelt)
        {
            state.conveyorVariantKind = conveyorBelt.IsReverseCornerVariant
                ? 2
                : (conveyorBelt.IsCornerVariant ? 1 : 0);
        }
        else if (installationObject is Wall fence)
        {
            state.conveyorVariantKind = fence.VariantKindId;
        }
        else if (installationObject is Pipe pipe)
        {
            state.conveyorVariantKind = pipe.VariantKindId;
        }

        if (installationObject is Railload railload)
        {
            state.railVisualPathPoints = railload.CopyVisualPathPoints();
            state.railVisualPathExtendsStart = railload.RuntimeVisualPathExtendsStart;
            state.railVisualPathExtendsEnd = railload.RuntimeVisualPathExtendsEnd;
        }

        if (installationObject is InputOutputModule inputOutputModule)
        {
            state.inputOutputState = inputOutputModule.CapturePersistentState();
        }

        if (installationObject is RobotArm robotArm)
        {
            state.robotArmState = robotArm.CapturePersistentState();
        }

        if (installationObject is BoxObject boxObject)
        {
            state.boxIsOpen = boxObject.IsOpen;
        }

        state.itemFilterMaskInitialized = installationObject.IsItemFilterMaskInitialized;
        state.itemFilterMaskWords = installationObject.CaptureItemFilterMaskWords();
        state.storedFluidLiters = installationObject.StoredFluidLiters;
        state.storedFluidItemId = installationObject.StoredFluidItemId;
        state.storedFluidTemperatureCelsius = installationObject.GetStoredFluidTemperatureCelsius(state.storedFluidItemId);

        return true;
    }

    private static int ResolveInstallationSaveItemId(InstallationObject installationObject)
    {
        if (installationObject == null)
        {
            return -1;
        }

        int fallbackItemId = installationObject.ResolveItemId();
        IReadOnlyList<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        if (definitions == null || definitions.Count <= 0)
        {
            return fallbackItemId;
        }

        if (installationObject is ConvayorBelt2F)
        {
            ItemDefinition belt2FDefinition = ItemDefinitionLookup.ResolveConveyorBelt2F(definitions);
            if (belt2FDefinition != null && belt2FDefinition.id >= 0)
            {
                return belt2FDefinition.id;
            }
        }

        ItemDefinition resolvedDefinition = ItemDefinitionLookup.ResolveInstallationById(definitions, fallbackItemId);
        if (resolvedDefinition != null && resolvedDefinition.id >= 0)
        {
            return resolvedDefinition.id;
        }

        ItemDefinition matchingDefinition = FindInstallationDefinitionByObject(definitions, installationObject);
        return matchingDefinition != null && matchingDefinition.id >= 0
            ? matchingDefinition.id
            : fallbackItemId;
    }

    private static ItemDefinition FindInstallationDefinitionByObject(
        IReadOnlyList<ItemDefinition> definitions,
        InstallationObject installationObject)
    {
        if (definitions == null || installationObject == null)
        {
            return null;
        }

        Type installationType = installationObject.GetType();
        string installationName = NormalizeObjectName(installationObject.name);

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.mapObject == null)
            {
                continue;
            }

            MapObject definitionMapObject = definition.mapObject;
            InstallationObject definitionInstallation = definitionMapObject as InstallationObject;
            if (definitionInstallation == null)
            {
                definitionInstallation = definitionMapObject.GetComponent<InstallationObject>();
            }

            if (definitionInstallation == null)
            {
                definitionInstallation = definitionMapObject.GetComponentInChildren<InstallationObject>(true);
            }

            if (definitionInstallation == null)
            {
                continue;
            }

            Type definitionType = definitionInstallation.GetType();
            if (definitionType != installationType)
            {
                continue;
            }

            if (string.Equals(
                    NormalizeObjectName(definitionMapObject.name),
                    installationName,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    NormalizeObjectName(definition.itemName),
                    installationName,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    NormalizeObjectName(definition.name),
                    installationName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }

    private static string NormalizeObjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("(Clone)", string.Empty).Trim();
    }

    private void StoreInstallationState(InstallationSaveState state)
    {
        StoreInstallationState(state, out _, out _);
    }

    private bool StoreInstallationState(
        InstallationSaveState state,
        out Vector2Int storageKey,
        out InstallationSaveState storedState)
    {
        storageKey = default;
        storedState = null;
        if (state == null)
        {
            return false;
        }

        storedState = state.Clone();
        storageKey = ResolveInstallationStorageKey(storedState, savedInstallationStates);
        AssignInstallationStorageKey(storedState, storageKey);
        RemoveSavedInstallationStatesForSamePlacement(storedState, storageKey);
        if (savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState existingState))
        {
            if (storedState.lastBackgroundSimulationTicks <= 0)
            {
                storedState.lastBackgroundSimulationTicks = existingState.lastBackgroundSimulationTicks;
            }

            AdjustSavedInstallationCount(existingState, -1);
            UnregisterSavedCoordinateMappings(existingState, storageKey);
        }

        savedInstallationStates[storageKey] = storedState;
        AdjustSavedInstallationCount(storedState, 1);
        RegisterSavedCoordinateMappings(storedState, storageKey);
        ResolveVirtualObjectWorld()?.UpsertInstallation(storedState);
        return true;
    }

    private void RemoveSavedInstallationStatesForSamePlacement(InstallationSaveState state, Vector2Int newStorageKey)
    {
        if (state == null || state.placementSequence <= 0 || savedInstallationStates.Count <= 0)
        {
            return;
        }

        List<Vector2Int> duplicateKeys = null;
        foreach (KeyValuePair<Vector2Int, InstallationSaveState> pair in savedInstallationStates)
        {
            if (pair.Key == newStorageKey || !InstallationStatesRepresentSamePlacement(pair.Value, state))
            {
                continue;
            }

            duplicateKeys ??= new List<Vector2Int>();
            duplicateKeys.Add(pair.Key);
        }

        if (duplicateKeys == null)
        {
            return;
        }

        VirtualObjectWorld world = ResolveVirtualObjectWorld();
        for (int i = 0; i < duplicateKeys.Count; i++)
        {
            Vector2Int duplicateKey = duplicateKeys[i];
            if (!savedInstallationStates.TryGetValue(duplicateKey, out InstallationSaveState duplicateState))
            {
                continue;
            }

            AdjustSavedInstallationCount(duplicateState, -1);
            UnregisterSavedCoordinateMappings(duplicateState, duplicateKey);
            savedInstallationStates.Remove(duplicateKey);
            world?.RemoveInstallation(duplicateKey);
        }
    }

    private void RemoveLiveInstallationRecordsForSamePlacement(
        InstallationObject installationObject,
        InstallationSaveState state,
        Vector2Int newStorageKey)
    {
        if ((installationObject == null && (state == null || state.placementSequence <= 0))
            || liveInstallationStates.Count <= 0)
        {
            return;
        }

        List<Vector2Int> duplicateKeys = null;
        foreach (KeyValuePair<Vector2Int, LiveInstallationRecord> pair in liveInstallationStates)
        {
            if (pair.Key == newStorageKey || pair.Value == null)
            {
                continue;
            }

            bool sameObject = installationObject != null
                              && ReferenceEquals(pair.Value.installationObject, installationObject);
            bool samePlacement = InstallationStatesRepresentSamePlacement(pair.Value.state, state);
            if (!sameObject && !samePlacement)
            {
                continue;
            }

            duplicateKeys ??= new List<Vector2Int>();
            duplicateKeys.Add(pair.Key);
        }

        if (duplicateKeys == null)
        {
            return;
        }

        VirtualObjectWorld world = ResolveVirtualObjectWorld();
        for (int i = 0; i < duplicateKeys.Count; i++)
        {
            Vector2Int duplicateKey = duplicateKeys[i];
            if (!liveInstallationStates.TryGetValue(duplicateKey, out LiveInstallationRecord duplicateRecord))
            {
                continue;
            }

            UnregisterLiveCoordinateMappings(duplicateRecord.state, duplicateKey);
            liveInstallationStates.Remove(duplicateKey);
            if (savedInstallationStates.TryGetValue(duplicateKey, out InstallationSaveState savedState))
            {
                world?.UpsertInstallation(savedState);
            }
            else
            {
                world?.RemoveInstallation(duplicateKey);
            }
        }
    }

    public static Vector2Int GetInstallationStorageKey(InstallationSaveState state)
    {
        if (state == null)
        {
            return default;
        }

        if (state.hasStorageKey)
        {
            return state.storageKey;
        }

        return GetNaturalInstallationStorageKey(state);
    }

    private static Vector2Int GetNaturalInstallationStorageKey(InstallationSaveState state)
    {
        if (state == null)
        {
            return default;
        }

        if (ShouldUseOccupiedCoordinateStorageKey(state))
        {
            return FindPreferredOccupiedStorageCoordinate(state);
        }

        return state.anchorCoordinate;
    }

    private static bool ShouldUseOccupiedCoordinateStorageKey(InstallationSaveState state)
    {
        return state?.occupiedCoordinates != null
               && state.occupiedCoordinates.Count > 0
               && !state.occupiedCoordinates.Contains(state.anchorCoordinate);
    }

    private static Vector2Int FindPreferredOccupiedStorageCoordinate(InstallationSaveState state)
    {
        Vector2Int bestCoordinate = state.anchorCoordinate;
        bool hasBestCoordinate = false;
        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.occupiedCoordinates[i];
            if (!hasBestCoordinate
                || coordinate.x < bestCoordinate.x
                || (coordinate.x == bestCoordinate.x && coordinate.y < bestCoordinate.y))
            {
                bestCoordinate = coordinate;
                hasBestCoordinate = true;
            }
        }

        return hasBestCoordinate ? bestCoordinate : state.anchorCoordinate;
    }

    private static Vector2Int ResolveInstallationStorageKey(
        InstallationSaveState state,
        Dictionary<Vector2Int, InstallationSaveState> statesByStorageKey)
    {
        if (state != null
            && state.hasStorageKey
            && (statesByStorageKey == null
                || !statesByStorageKey.TryGetValue(state.storageKey, out InstallationSaveState existingStorageState)
                || InstallationStatesCanShareStorageKey(existingStorageState, state)
                || InstallationStatesRepresentSamePlacement(existingStorageState, state)))
        {
            return state.storageKey;
        }

        Vector2Int preferredKey = GetNaturalInstallationStorageKey(state);
        if (statesByStorageKey == null
            || !statesByStorageKey.TryGetValue(preferredKey, out InstallationSaveState existingState)
            || InstallationStatesCanShareStorageKey(existingState, state))
        {
            return preferredKey;
        }

        if (state?.occupiedCoordinates != null)
        {
            for (int i = 0; i < state.occupiedCoordinates.Count; i++)
            {
                Vector2Int candidate = state.occupiedCoordinates[i];
                if (!statesByStorageKey.TryGetValue(candidate, out existingState)
                    || InstallationStatesCanShareStorageKey(existingState, state))
                {
                    return candidate;
                }
            }
        }

        if (state != null && state.placementSequence > 0)
        {
            return CreateSyntheticInstallationStorageKey(state);
        }

        return preferredKey;
    }

    private static Vector2Int CreateSyntheticInstallationStorageKey(InstallationSaveState state)
    {
        long sequence = state != null ? Math.Max(1L, state.placementSequence) : 1L;
        unchecked
        {
            int x = int.MinValue + 4096 + (int)(sequence & 0x0FFFFFFF);
            int y = int.MinValue + 4096 + (int)((sequence >> 28) & 0x0FFFFFFF);
            return new Vector2Int(x, y);
        }
    }

    private static void AssignInstallationStorageKey(InstallationSaveState state, Vector2Int storageKey)
    {
        if (state == null)
        {
            return;
        }

        state.hasStorageKey = true;
        state.storageKey = storageKey;
    }

    private static bool InstallationStatesCanShareStorageKey(
        InstallationSaveState existingState,
        InstallationSaveState state)
    {
        return existingState == null
               || state == null
               || (existingState.anchorCoordinate == state.anchorCoordinate
                   && existingState.itemId == state.itemId
                   && existingState.placementSequence == state.placementSequence);
    }

    private static bool InstallationStatesRepresentSamePlacement(
        InstallationSaveState existingState,
        InstallationSaveState state)
    {
        return existingState != null
               && state != null
               && existingState.placementSequence > 0
               && existingState.placementSequence == state.placementSequence
               && existingState.itemId == state.itemId;
    }

    private void AdjustSavedInstallationCount(InstallationSaveState state, int delta)
    {
        if (state == null || state.itemId < 0 || delta == 0)
        {
            return;
        }

        savedInstallationItemTotal = Mathf.Max(0, savedInstallationItemTotal + delta);
        savedInstallationCountsByItemId.TryGetValue(state.itemId, out int currentCount);
        int nextCount = currentCount + delta;
        if (nextCount > 0)
        {
            savedInstallationCountsByItemId[state.itemId] = nextCount;
        }
        else
        {
            savedInstallationCountsByItemId.Remove(state.itemId);
        }
    }

    private VirtualObjectWorld ResolveVirtualObjectWorld()
    {
        if (virtualObjectWorld != null)
        {
            return virtualObjectWorld;
        }

        virtualObjectWorld = VirtualObjectWorld.Current;
        if (virtualObjectWorld != null)
        {
            return virtualObjectWorld;
        }

        virtualObjectWorld = VirtualObjectWorld.EnsureFor(gameObject);
        return virtualObjectWorld;
    }

    private void RegisterSavedCoordinateMappings(InstallationSaveState state, Vector2Int storageKey)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.occupiedCoordinates[i];
            if (ShouldReplaceSavedCoordinateMapping(coordinate, storageKey, state))
            {
                savedInstallationAnchorsByCoordinate[coordinate] = storageKey;
            }
        }
    }

    private void UnregisterSavedCoordinateMappings(InstallationSaveState state, Vector2Int storageKey)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.occupiedCoordinates[i];
            if (savedInstallationAnchorsByCoordinate.TryGetValue(coordinate, out Vector2Int mappedAnchor)
                && mappedAnchor == storageKey)
            {
                savedInstallationAnchorsByCoordinate.Remove(coordinate);
            }
        }
    }

    private void RegisterLiveCoordinateMappings(InstallationSaveState state, Vector2Int storageKey)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.occupiedCoordinates[i];
            if (ShouldReplaceLiveCoordinateMapping(coordinate, storageKey, state))
            {
                liveInstallationAnchorsByCoordinate[coordinate] = storageKey;
            }
        }
    }

    private bool ShouldReplaceSavedCoordinateMapping(
        Vector2Int coordinate,
        Vector2Int storageKey,
        InstallationSaveState state)
    {
        if (!savedInstallationAnchorsByCoordinate.TryGetValue(coordinate, out Vector2Int existingStorageKey)
            || existingStorageKey == storageKey
            || !savedInstallationStates.TryGetValue(existingStorageKey, out InstallationSaveState existingState)
            || existingState == null)
        {
            return true;
        }

        return ShouldReplaceCoordinateMapping(existingState, state);
    }

    private bool ShouldReplaceLiveCoordinateMapping(
        Vector2Int coordinate,
        Vector2Int storageKey,
        InstallationSaveState state)
    {
        if (!liveInstallationAnchorsByCoordinate.TryGetValue(coordinate, out Vector2Int existingStorageKey)
            || existingStorageKey == storageKey
            || !liveInstallationStates.TryGetValue(existingStorageKey, out LiveInstallationRecord existingRecord)
            || existingRecord?.state == null)
        {
            return true;
        }

        return ShouldReplaceCoordinateMapping(existingRecord.state, state);
    }

    private static bool ShouldReplaceCoordinateMapping(
        InstallationSaveState existingState,
        InstallationSaveState incomingState)
    {
        bool existingIsTrain = IsTrainInstallationState(existingState);
        bool incomingIsTrain = IsTrainInstallationState(incomingState);
        if (existingIsTrain != incomingIsTrain)
        {
            return existingIsTrain;
        }

        return true;
    }

    private static bool IsTrainInstallationState(InstallationSaveState state)
    {
        if (state == null || state.itemId < 0)
        {
            return false;
        }

        IReadOnlyList<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        if (definitions == null)
        {
            return false;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id != state.itemId || definition.mapObject == null)
            {
                continue;
            }

            return definition.mapObject is Train
                   || definition.mapObject.GetComponent<Train>() != null
                   || definition.mapObject.GetComponentInChildren<Train>(true) != null;
        }

        return false;
    }

    private void UnregisterLiveCoordinateMappings(InstallationSaveState state, Vector2Int storageKey)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.occupiedCoordinates[i];
            if (liveInstallationAnchorsByCoordinate.TryGetValue(coordinate, out Vector2Int mappedAnchor)
                && mappedAnchor == storageKey)
            {
                liveInstallationAnchorsByCoordinate.Remove(coordinate);
            }
        }
    }
}
