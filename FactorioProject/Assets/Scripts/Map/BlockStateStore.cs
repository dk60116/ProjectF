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
        public int conveyorVariantKind = -1;
        public List<Vector2Int> occupiedCoordinates = new List<Vector2Int>();
        public InputOutputModule.PersistentState inputOutputState;
        public long lastBackgroundSimulationTicks;
        public bool? boxIsOpen;
        public bool itemFilterMaskInitialized;
        public List<ulong> itemFilterMaskWords = new List<ulong>();

        public InstallationSaveState Clone()
        {
            return new InstallationSaveState
            {
                anchorCoordinate = anchorCoordinate,
                itemId = itemId,
                quarterTurns = quarterTurns,
                placementSequence = placementSequence,
                conveyorVariantKind = conveyorVariantKind,
                occupiedCoordinates = new List<Vector2Int>(occupiedCoordinates ?? new List<Vector2Int>()),
                inputOutputState = inputOutputState != null ? inputOutputState.Clone() : null,
                lastBackgroundSimulationTicks = lastBackgroundSimulationTicks,
                boxIsOpen = boxIsOpen,
                itemFilterMaskInitialized = itemFilterMaskInitialized,
                itemFilterMaskWords = new List<ulong>(itemFilterMaskWords ?? new List<ulong>())
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

    public void SaveFloorObjects(Vector2Int worldCoordinate, Block block)
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
        ResolveVirtualObjectWorld()?.UpsertFloorItemStack(worldCoordinate, itemIds);
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

        InstallationSaveState clonedState = state.Clone();
        StoreInstallationState(clonedState);

        if (liveInstallationStates.TryGetValue(clonedState.anchorCoordinate, out LiveInstallationRecord existingRecord))
        {
            UnregisterLiveCoordinateMappings(existingRecord.state);
        }

        liveInstallationStates[clonedState.anchorCoordinate] = new LiveInstallationRecord
        {
            installationObject = installationObject,
            state = clonedState
        };
        RegisterLiveCoordinateMappings(clonedState);
        ResolveVirtualObjectWorld()?.UpsertInstallation(clonedState, VirtualObjectResidency.Live, installationObject);
    }

    public bool TryGetInstallationState(Vector2Int anchorCoordinate, out InstallationSaveState state)
    {
        if (savedInstallationStates.TryGetValue(anchorCoordinate, out InstallationSaveState savedState) && savedState != null)
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

    public List<Vector2Int> GetSavedInstallationAnchors()
    {
        return new List<Vector2Int>(savedInstallationStates.Keys);
    }

    public int GetInstallationItemCounts(Dictionary<int, int> countsByItemId)
    {
        countsByItemId?.Clear();

        int total = 0;
        foreach (KeyValuePair<Vector2Int, InstallationSaveState> pair in savedInstallationStates)
        {
            InstallationSaveState state = pair.Value;
            if (state == null || state.itemId < 0)
            {
                continue;
            }

            total++;
            if (countsByItemId == null)
            {
                continue;
            }

            countsByItemId.TryGetValue(state.itemId, out int currentCount);
            countsByItemId[state.itemId] = currentCount + 1;
        }

        return total;
    }

    public bool TryGetLiveInstallation(Vector2Int anchorCoordinate, out InstallationObject installationObject, out InstallationSaveState state)
    {
        if (liveInstallationStates.TryGetValue(anchorCoordinate, out LiveInstallationRecord record)
            && record != null
            && record.installationObject != null)
        {
            installationObject = record.installationObject;
            state = record.state != null ? record.state.Clone() : null;
            return true;
        }

        if (liveInstallationStates.ContainsKey(anchorCoordinate))
        {
            UnregisterLiveInstallation(anchorCoordinate);
        }

        installationObject = null;
        state = null;
        return false;
    }

    public bool TryDetachLiveInstallation(Vector2Int anchorCoordinate, out InstallationObject installationObject, out InstallationSaveState state)
    {
        installationObject = null;
        state = null;

        if (!liveInstallationStates.TryGetValue(anchorCoordinate, out LiveInstallationRecord record)
            || record == null
            || record.installationObject == null)
        {
            if (liveInstallationStates.ContainsKey(anchorCoordinate))
            {
                UnregisterLiveInstallation(anchorCoordinate);
            }

            return false;
        }

        installationObject = record.installationObject;
        state = record.state != null ? record.state.Clone() : null;
        UnregisterLiveCoordinateMappings(record.state);
        liveInstallationStates.Remove(anchorCoordinate);

        VirtualObjectWorld world = ResolveVirtualObjectWorld();
        if (savedInstallationStates.TryGetValue(anchorCoordinate, out InstallationSaveState savedState))
        {
            world?.UpsertInstallation(savedState);
        }
        else
        {
            world?.RemoveInstallation(anchorCoordinate);
        }

        return true;
    }

    public bool TryGetInstallationAnchorAtCoordinate(Vector2Int worldCoordinate, out Vector2Int anchorCoordinate)
    {
        if (liveInstallationAnchorsByCoordinate.TryGetValue(worldCoordinate, out anchorCoordinate))
        {
            return true;
        }

        return savedInstallationAnchorsByCoordinate.TryGetValue(worldCoordinate, out anchorCoordinate);
    }

    public List<Vector2Int> GetLiveInstallationAnchors()
    {
        return new List<Vector2Int>(liveInstallationStates.Keys);
    }

    public void UnregisterLiveInstallation(Vector2Int anchorCoordinate)
    {
        if (!liveInstallationStates.TryGetValue(anchorCoordinate, out LiveInstallationRecord record))
        {
            return;
        }

        UnregisterLiveCoordinateMappings(record.state);
        liveInstallationStates.Remove(anchorCoordinate);

        VirtualObjectWorld world = ResolveVirtualObjectWorld();
        if (savedInstallationStates.TryGetValue(anchorCoordinate, out InstallationSaveState savedState))
        {
            world?.UpsertInstallation(savedState);
        }
        else
        {
            world?.RemoveInstallation(anchorCoordinate);
        }
    }

    public void RemoveInstallation(Vector2Int anchorCoordinate)
    {
        if (savedInstallationStates.TryGetValue(anchorCoordinate, out InstallationSaveState savedState))
        {
            UnregisterSavedCoordinateMappings(savedState);
            savedInstallationStates.Remove(anchorCoordinate);
        }

        UnregisterLiveInstallation(anchorCoordinate);
        ResolveVirtualObjectWorld()?.RemoveInstallation(anchorCoordinate);
    }

    public void ClearStates()
    {
        savedStates.Clear();
        savedResourceItemIds.Clear();
        savedFloorObjectStates.Clear();
        savedConveyorItemStates.Clear();
        savedInstallationStates.Clear();
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
    }

    private bool TryBuildInstallationState(InstallationObject installationObject, out InstallationSaveState state)
    {
        state = null;
        if (installationObject == null
            || !installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        int itemId = installationObject.ResolveItemId();
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

        if (installationObject is ConveyorBelt conveyorBelt)
        {
            state.conveyorVariantKind = conveyorBelt.IsReverseCornerVariant
                ? 2
                : (conveyorBelt.IsCornerVariant ? 1 : 0);
        }

        if (installationObject is InputOutputModule inputOutputModule)
        {
            state.inputOutputState = inputOutputModule.CapturePersistentState();
        }

        if (installationObject is BoxObject boxObject)
        {
            state.boxIsOpen = boxObject.IsOpen;
        }

        state.itemFilterMaskInitialized = installationObject.IsItemFilterMaskInitialized;
        state.itemFilterMaskWords = installationObject.CaptureItemFilterMaskWords();

        return true;
    }

    private void StoreInstallationState(InstallationSaveState state)
    {
        if (state == null)
        {
            return;
        }

        InstallationSaveState clonedState = state.Clone();
        if (savedInstallationStates.TryGetValue(clonedState.anchorCoordinate, out InstallationSaveState existingState))
        {
            if (clonedState.lastBackgroundSimulationTicks <= 0)
            {
                clonedState.lastBackgroundSimulationTicks = existingState.lastBackgroundSimulationTicks;
            }

            UnregisterSavedCoordinateMappings(existingState);
        }

        savedInstallationStates[clonedState.anchorCoordinate] = clonedState;
        RegisterSavedCoordinateMappings(clonedState);
        ResolveVirtualObjectWorld()?.UpsertInstallation(clonedState);
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

    private void RegisterSavedCoordinateMappings(InstallationSaveState state)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            savedInstallationAnchorsByCoordinate[state.occupiedCoordinates[i]] = state.anchorCoordinate;
        }
    }

    private void UnregisterSavedCoordinateMappings(InstallationSaveState state)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.occupiedCoordinates[i];
            if (savedInstallationAnchorsByCoordinate.TryGetValue(coordinate, out Vector2Int mappedAnchor)
                && mappedAnchor == state.anchorCoordinate)
            {
                savedInstallationAnchorsByCoordinate.Remove(coordinate);
            }
        }
    }

    private void RegisterLiveCoordinateMappings(InstallationSaveState state)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            liveInstallationAnchorsByCoordinate[state.occupiedCoordinates[i]] = state.anchorCoordinate;
        }
    }

    private void UnregisterLiveCoordinateMappings(InstallationSaveState state)
    {
        if (state?.occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < state.occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.occupiedCoordinates[i];
            if (liveInstallationAnchorsByCoordinate.TryGetValue(coordinate, out Vector2Int mappedAnchor)
                && mappedAnchor == state.anchorCoordinate)
            {
                liveInstallationAnchorsByCoordinate.Remove(coordinate);
            }
        }
    }
}
