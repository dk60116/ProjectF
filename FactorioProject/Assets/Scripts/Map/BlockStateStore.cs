using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore : MonoBehaviour
{
    public sealed class InstallationSaveState
    {
        public Vector2Int anchorCoordinate;
        public int itemId;
        public string itemName = string.Empty;
        public int quarterTurns;
        public long placementSequence;
        public bool hasStorageKey;
        public Vector2Int storageKey;
        public int conveyorVariantKind = -1;
        public int pipeConnectionMask = -1;
        public List<Vector2Int> occupiedCoordinates = new List<Vector2Int>();
        public List<Vector2> railVisualPathPoints = new List<Vector2>();
        public bool railVisualPathExtendsStart = true;
        public bool railVisualPathExtendsEnd = true;
        public int railRequiredItemCount;
        public InputOutputModule.PersistentState inputOutputState;
        public RobotArm.PersistentState robotArmState;
        public long lastBackgroundSimulationTicks;
        public bool? boxIsOpen;
        public bool itemFilterMaskInitialized;
        public List<ulong> itemFilterMaskWords = new List<ulong>();
        public float storedFluidLiters;
        public int storedFluidItemId = -1;
        public float storedFluidTemperatureCelsius = MapClimate.DefaultCurrentTemperatureCelsius;
        public int storedInstallationItemId = -1;
        public List<int> storedInstallationItemIds = new List<int>();
        public bool hasWorldPose;
        public Vector3 worldPosition;
        public Quaternion worldRotation = Quaternion.identity;
        public bool hasTrainRailSample;
        public long trainRailPlacementSequence;
        public Vector2Int trainRailAnchorCoordinate;
        public float trainRailDistanceAlongPath;
        public Vector2 trainRailPathPoint;
        public Vector2 trainRailFacingTangent;
        public bool hasSteamTrainBurnEnergyState;
        public float steamTrainStoredBurnEnergy;
        public float steamTrainBurnEnergyGaugeCapacity;
        public bool steamTrainAutoDriveEnabled;
        public string steamTrainAutoDriveTargetAStationName = string.Empty;
        public string steamTrainAutoDriveTargetBStationName = string.Empty;
        public int steamTrainAutoDriveFuelFilter;
        public int steamTrainAutoDriveFreightFilter;
        public string steamTrainAutoDriveRouteTargetStationName = string.Empty;
        public string steamTrainAutoDriveLastArrivedStationName = string.Empty;
        public float steamTrainAutoDriveStationWaitTimer;
        public string stationName = string.Empty;

        public InstallationSaveState Clone()
        {
            return new InstallationSaveState
            {
                anchorCoordinate = anchorCoordinate,
                itemId = itemId,
                itemName = itemName,
                quarterTurns = quarterTurns,
                placementSequence = placementSequence,
                hasStorageKey = hasStorageKey,
                storageKey = storageKey,
                conveyorVariantKind = conveyorVariantKind,
                pipeConnectionMask = pipeConnectionMask,
                occupiedCoordinates = new List<Vector2Int>(occupiedCoordinates ?? new List<Vector2Int>()),
                railVisualPathPoints = new List<Vector2>(railVisualPathPoints ?? new List<Vector2>()),
                railVisualPathExtendsStart = railVisualPathExtendsStart,
                railVisualPathExtendsEnd = railVisualPathExtendsEnd,
                railRequiredItemCount = railRequiredItemCount,
                inputOutputState = inputOutputState != null ? inputOutputState.Clone() : null,
                robotArmState = robotArmState != null ? robotArmState.Clone() : null,
                lastBackgroundSimulationTicks = lastBackgroundSimulationTicks,
                boxIsOpen = boxIsOpen,
                itemFilterMaskInitialized = itemFilterMaskInitialized,
                itemFilterMaskWords = new List<ulong>(itemFilterMaskWords ?? new List<ulong>()),
                storedFluidLiters = storedFluidLiters,
                storedFluidItemId = storedFluidItemId,
                storedFluidTemperatureCelsius = storedFluidTemperatureCelsius,
                storedInstallationItemId = storedInstallationItemId,
                storedInstallationItemIds = new List<int>(storedInstallationItemIds ?? new List<int>()),
                hasWorldPose = hasWorldPose,
                worldPosition = worldPosition,
                worldRotation = worldRotation,
                hasTrainRailSample = hasTrainRailSample,
                trainRailPlacementSequence = trainRailPlacementSequence,
                trainRailAnchorCoordinate = trainRailAnchorCoordinate,
                trainRailDistanceAlongPath = trainRailDistanceAlongPath,
                trainRailPathPoint = trainRailPathPoint,
                trainRailFacingTangent = trainRailFacingTangent,
                hasSteamTrainBurnEnergyState = hasSteamTrainBurnEnergyState,
                steamTrainStoredBurnEnergy = steamTrainStoredBurnEnergy,
                steamTrainBurnEnergyGaugeCapacity = steamTrainBurnEnergyGaugeCapacity,
                steamTrainAutoDriveEnabled = steamTrainAutoDriveEnabled,
                steamTrainAutoDriveTargetAStationName = steamTrainAutoDriveTargetAStationName,
                steamTrainAutoDriveTargetBStationName = steamTrainAutoDriveTargetBStationName,
                steamTrainAutoDriveFuelFilter = steamTrainAutoDriveFuelFilter,
                steamTrainAutoDriveFreightFilter = steamTrainAutoDriveFreightFilter,
                steamTrainAutoDriveRouteTargetStationName = steamTrainAutoDriveRouteTargetStationName,
                steamTrainAutoDriveLastArrivedStationName = steamTrainAutoDriveLastArrivedStationName,
                steamTrainAutoDriveStationWaitTimer = steamTrainAutoDriveStationWaitTimer,
                stationName = stationName
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
        private readonly bool hasDroppedFloorObjects;
        public bool HasDroppedFloorObjects => hasDroppedFloorObjects;

        private FloorObjectSaveState(
            int[] rawItems,
            IntRun[] compressedRuns,
            int itemCount,
            bool hasDroppedFloorObjects)
        {
            this.rawItems = rawItems;
            this.compressedRuns = compressedRuns;
            this.itemCount = itemCount;
            this.hasDroppedFloorObjects = hasDroppedFloorObjects;
        }

        public static FloorObjectSaveState FromSerialized(IReadOnlyList<int> itemIds)
        {
            if (itemIds == null || itemIds.Count <= 0)
            {
                return null;
            }

            int count = itemIds.Count;
            bool hasDroppedFloorObjects = ContainsDroppedFloorObjects(itemIds);
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

                return new FloorObjectSaveState(rawCopy, null, count, hasDroppedFloorObjects);
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
            return new FloorObjectSaveState(null, runs, count, hasDroppedFloorObjects);
        }

        public static FloorObjectSaveState FromOwnedRawItems(int[] itemIds)
        {
            return itemIds != null && itemIds.Length > 0
                ? new FloorObjectSaveState(
                    itemIds,
                    null,
                    itemIds.Length,
                    ContainsDroppedFloorObjects(itemIds))
                : null;
        }

        private static bool ContainsDroppedFloorObjects(IReadOnlyList<int> itemIds)
        {
            if (itemIds == null)
            {
                return false;
            }

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                if (itemId == Block.FloorStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        return false;
                    }

                    int stackCount = Mathf.Max(0, itemIds[++i]);
                    for (int stackIndex = 0; stackIndex < stackCount && i + 1 < itemIds.Count; stackIndex++)
                    {
                        int stackItemCount = Mathf.Max(0, itemIds[++i]);
                        for (int objectIndex = 0; objectIndex < stackItemCount && i + 1 < itemIds.Count; objectIndex++)
                        {
                            if (itemIds[++i] >= 0)
                            {
                                return true;
                            }
                        }
                    }

                    continue;
                }

                if (itemId == Block.InputAreaCenterStackStateSentinel
                    || itemId == Block.ConveyorStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        return false;
                    }

                    int skippedItemCount = Mathf.Max(0, itemIds[++i]);
                    i = Mathf.Min(itemIds.Count - 1, i + skippedItemCount);
                    continue;
                }

                // Legacy floor-object states stored item ids without a stack sentinel.
                if (itemId >= 0)
                {
                    return true;
                }
            }

            return false;
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
    private readonly HashSet<Vector2Int> savedBackgroundInstallationStorageKeys = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, Vector2Int> savedInstallationAnchorsByCoordinate = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Dictionary<Vector2Int, HashSet<Vector2Int>> savedPipeInstallationStorageKeysByOccupiedCoordinate =
        new Dictionary<Vector2Int, HashSet<Vector2Int>>();
    private readonly Dictionary<Vector2Int, HashSet<Vector2Int>> savedInstallationStorageKeysByInteractionCoordinate =
        new Dictionary<Vector2Int, HashSet<Vector2Int>>();
    private readonly Dictionary<Vector2Int, LiveInstallationRecord> liveInstallationStates = new Dictionary<Vector2Int, LiveInstallationRecord>();
    private readonly Dictionary<Vector2Int, Vector2Int> liveInstallationAnchorsByCoordinate = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Dictionary<int, int> savedInstallationCountsByItemId = new Dictionary<int, int>();
    private readonly Dictionary<int, int> savedInstallationStoredItemCountsByItemId = new Dictionary<int, int>();
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

    public void RemoveResource(Vector2Int worldCoordinate)
    {
        savedStates.Remove(worldCoordinate);
        savedResourceItemIds.Remove(worldCoordinate);
        ResolveVirtualObjectWorld()?.RemoveResource(worldCoordinate);
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

    public bool HasSavedDroppedFloorObjects(Vector2Int worldCoordinate)
    {
        return savedFloorObjectStates.TryGetValue(worldCoordinate, out FloorObjectSaveState savedState)
               && savedState != null
               && savedState.HasDroppedFloorObjects;
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

    private void SetFloorObjectsFromOwnedRawItems(Vector2Int worldCoordinate, int[] itemIds)
    {
        if (itemIds == null || itemIds.Length <= 0)
        {
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        FloorObjectSaveState state = FloorObjectSaveState.FromOwnedRawItems(itemIds);
        if (state == null)
        {
            savedFloorObjectStates.Remove(worldCoordinate);
            ResolveVirtualObjectWorld()?.RemoveFloorItemStack(worldCoordinate);
            return;
        }

        savedFloorObjectStates[worldCoordinate] = state;
        ResolveVirtualObjectWorld()?.UpsertFloorItemStackRaw(worldCoordinate, itemIds);
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

    public bool UpdateLiveInstallationWorldPose(InstallationObject installationObject)
    {
        if (installationObject == null
            || !installationObject.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return false;
        }

        Vector2Int storageKey = anchorCoordinate;
        if (!liveInstallationStates.TryGetValue(storageKey, out LiveInstallationRecord liveRecord)
            || liveRecord == null
            || liveRecord.installationObject != installationObject)
        {
            liveRecord = null;
            foreach (KeyValuePair<Vector2Int, LiveInstallationRecord> pair in liveInstallationStates)
            {
                if (pair.Value != null && pair.Value.installationObject == installationObject)
                {
                    storageKey = pair.Key;
                    liveRecord = pair.Value;
                    break;
                }
            }
        }

        if (liveRecord?.state == null)
        {
            return false;
        }

        Vector3 worldPosition = installationObject.transform.position;
        Quaternion worldRotation = installationObject.transform.rotation;
        SetInstallationWorldPose(liveRecord.state, worldPosition, worldRotation);
        if (savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState savedState)
            && savedState != null
            && savedState.placementSequence == installationObject.RuntimePlacementSequence)
        {
            SetInstallationWorldPose(savedState, worldPosition, worldRotation);
        }

        ResolveVirtualObjectWorld()?.UpdateLiveInstallationWorldPose(
            storageKey,
            installationObject,
            worldPosition,
            worldRotation);
        return true;
    }

    private static void SetInstallationWorldPose(
        InstallationSaveState state,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        state.hasWorldPose = true;
        state.worldPosition = worldPosition;
        state.worldRotation = worldRotation;
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

    public List<InstallationSaveState> GetInstallationStatesSnapshot()
    {
        List<InstallationSaveState> snapshot = new List<InstallationSaveState>(savedInstallationStates.Count);
        foreach (KeyValuePair<Vector2Int, InstallationSaveState> pair in savedInstallationStates)
        {
            if (pair.Value != null)
            {
                snapshot.Add(pair.Value.Clone());
            }
        }

        return snapshot;
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

    public bool HasStoredInstallationItem(int itemId)
    {
        return itemId >= 0
               && savedInstallationStoredItemCountsByItemId.TryGetValue(itemId, out int count)
               && count > 0;
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

    private bool HasValidLiveInstallation(Vector2Int storageKey)
    {
        if (!liveInstallationStates.TryGetValue(storageKey, out LiveInstallationRecord record))
        {
            return false;
        }

        if (record != null && record.installationObject != null)
        {
            return true;
        }

        UnregisterLiveInstallation(storageKey);
        return false;
    }

    private static int CompareCoordinate(Vector2Int first, Vector2Int second)
    {
        int xComparison = first.x.CompareTo(second.x);
        return xComparison != 0 ? xComparison : first.y.CompareTo(second.y);
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

    /// <summary>
    /// Returns an unloaded saved pipe occupying <paramref name="worldCoordinate"/> even when
    /// another saved installation owns the general occupied-coordinate mapping. The returned
    /// state is store-owned and must be treated as read-only.
    /// </summary>
    public bool TryGetSavedPipeInstallationStateAtCoordinate(
        Vector2Int worldCoordinate,
        out InstallationSaveState state)
    {
        state = null;
        if (!savedPipeInstallationStorageKeysByOccupiedCoordinate.TryGetValue(
                worldCoordinate,
                out HashSet<Vector2Int> storageKeys))
        {
            return false;
        }

        Vector2Int selectedStorageKey = default;
        long selectedPlacementSequence = long.MinValue;
        bool hasSelection = false;
        foreach (Vector2Int storageKey in storageKeys)
        {
            if (HasValidLiveInstallation(storageKey)
                || !savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState savedState)
                || savedState == null
                || !IsPipeInstallationState(savedState)
                || !ContainsCoordinate(savedState.occupiedCoordinates, worldCoordinate))
            {
                continue;
            }

            long placementSequence = savedState.placementSequence;
            if (hasSelection
                && (placementSequence < selectedPlacementSequence
                    || placementSequence == selectedPlacementSequence
                    && CompareCoordinate(storageKey, selectedStorageKey) <= 0))
            {
                continue;
            }

            state = savedState;
            selectedStorageKey = storageKey;
            selectedPlacementSequence = placementSequence;
            hasSelection = true;
        }

        return hasSelection;
    }

    /// <summary>
    /// Appends unloaded saved installations whose persisted input/output state references
    /// <paramref name="worldCoordinate"/>. Returned state instances are store-owned and must
    /// be treated as read-only. Live installations are intentionally excluded.
    /// </summary>
    public int CollectSavedInstallationStatesAtInteractionCoordinate(
        Vector2Int worldCoordinate,
        ICollection<InstallationSaveState> states)
    {
        if (states == null
            || !savedInstallationStorageKeysByInteractionCoordinate.TryGetValue(
                worldCoordinate,
                out HashSet<Vector2Int> storageKeys))
        {
            return 0;
        }

        int addedCount = 0;
        foreach (Vector2Int storageKey in storageKeys)
        {
            if (HasValidLiveInstallation(storageKey)
                || !savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState state)
                || state == null)
            {
                continue;
            }

            states.Add(state);
            addedCount++;
        }

        return addedCount;
    }

    public void CollectBackgroundInstallationAnchorsNearCoordinate(Vector2Int worldCoordinate, ICollection<Vector2Int> storageKeys)
    {
        if (storageKeys == null || savedBackgroundInstallationStorageKeys.Count <= 0)
        {
            return;
        }

        foreach (Vector2Int storageKey in savedBackgroundInstallationStorageKeys)
        {
            if (liveInstallationStates.ContainsKey(storageKey)
                || !savedInstallationStates.TryGetValue(storageKey, out InstallationSaveState state)
                || !SavedInstallationStateInteractsWithCoordinate(state, worldCoordinate))
            {
                continue;
            }

            storageKeys.Add(storageKey);
        }
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
            savedBackgroundInstallationStorageKeys.Remove(storageKey);
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
        InvalidateConveyorSchedule();
        ClearPendingConveyorFloorSyncs();
        savedInstallationStates.Clear();
        savedBackgroundInstallationStorageKeys.Clear();
        savedInstallationCountsByItemId.Clear();
        savedInstallationStoredItemCountsByItemId.Clear();
        savedInstallationItemTotal = 0;
        savedInstallationAnchorsByCoordinate.Clear();
        savedPipeInstallationStorageKeysByOccupiedCoordinate.Clear();
        savedInstallationStorageKeysByInteractionCoordinate.Clear();
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

        SimulateSavedConveyorItems(flushAllFloorSyncs: true);

        mapSaveData.resources ??= new List<ResourceSaveEntry>();
        mapSaveData.floorObjects ??= new List<FloorObjectSaveEntry>();
        mapSaveData.installations ??= new List<InstallationSaveEntry>();
        mapSaveData.conveyorItems ??= new List<ConveyorItemBlockSaveEntry>();
        mapSaveData.resources.Clear();
        mapSaveData.floorObjects.Clear();
        mapSaveData.installations.Clear();
        mapSaveData.conveyorItems.Clear();

        List<KeyValuePair<Vector2Int, Resource.ResourceSaveState>> savedStateSnapshot =
            new List<KeyValuePair<Vector2Int, Resource.ResourceSaveState>>(savedStates);
        for (int i = 0; i < savedStateSnapshot.Count; i++)
        {
            KeyValuePair<Vector2Int, Resource.ResourceSaveState> pair = savedStateSnapshot[i];
            savedResourceItemIds.TryGetValue(pair.Key, out int itemId);
            mapSaveData.resources.Add(new ResourceSaveEntry
            {
                coordinate = pair.Key,
                itemId = itemId,
                state = pair.Value
            });
        }

        List<KeyValuePair<Vector2Int, FloorObjectSaveState>> savedFloorObjectSnapshot =
            new List<KeyValuePair<Vector2Int, FloorObjectSaveState>>(savedFloorObjectStates);
        for (int i = 0; i < savedFloorObjectSnapshot.Count; i++)
        {
            KeyValuePair<Vector2Int, FloorObjectSaveState> pair = savedFloorObjectSnapshot[i];
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

        List<KeyValuePair<Vector2Int, ConveyorItemBlockState>> savedConveyorItemSnapshot =
            new List<KeyValuePair<Vector2Int, ConveyorItemBlockState>>(savedConveyorItemStates);
        for (int i = 0; i < savedConveyorItemSnapshot.Count; i++)
        {
            KeyValuePair<Vector2Int, ConveyorItemBlockState> pair = savedConveyorItemSnapshot[i];
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

        List<KeyValuePair<Vector2Int, InstallationSaveState>> savedInstallationSnapshot =
            new List<KeyValuePair<Vector2Int, InstallationSaveState>>(savedInstallationStates);
        for (int i = 0; i < savedInstallationSnapshot.Count; i++)
        {
            KeyValuePair<Vector2Int, InstallationSaveState> pair = savedInstallationSnapshot[i];
            if (pair.Value == null)
            {
                continue;
            }

            mapSaveData.installations.Add(new InstallationSaveEntry
            {
                state = pair.Value.Clone()
            });
        }

        SaveGameConveyorItemBackfill.BackfillFromFloorObjects(mapSaveData);
    }

    public void ApplySaveState(MapSaveData mapSaveData)
    {
        ClearStates();
        if (mapSaveData == null)
        {
            return;
        }

        SaveGameConveyorItemBackfill.BackfillFromFloorObjects(mapSaveData);
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

        ApplyInstallationSaveStates(mapSaveData.installations);

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

        if (installationObject is Pipe savedPipe)
        {
            quarterTurns = ResolvePipeQuarterTurnsFromCurrentRotation(savedPipe, quarterTurns);
        }

        state = new InstallationSaveState
        {
            anchorCoordinate = anchorCoordinate,
            itemId = itemId,
            itemName = ResolveInstallationSaveItemName(itemId, installationObject),
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
            state.pipeConnectionMask = pipe.GetConnectionMask(pipe.transform.rotation);
        }

        if (installationObject is Railload railload)
        {
            state.railVisualPathPoints = railload.CopyVisualPathPoints();
            state.railVisualPathExtendsStart = railload.RuntimeVisualPathExtendsStart;
            state.railVisualPathExtendsEnd = railload.RuntimeVisualPathExtendsEnd;
            state.railRequiredItemCount = railload.RequiredItemCount;
        }

        if (installationObject is Vehicle)
        {
            state.hasWorldPose = true;
            state.worldPosition = installationObject.transform.position;
            state.worldRotation = installationObject.transform.rotation;
        }

        if (installationObject is Train train)
        {
            if (train.TryGetCurrentRailPose(
                    out Railload rail,
                    out float distanceAlongPath,
                    out Vector2 pathPoint,
                    out Vector2 tangent)
                && rail != null)
            {
                state.hasTrainRailSample = true;
                state.trainRailPlacementSequence = rail.RuntimePlacementSequence;
                state.trainRailAnchorCoordinate = rail.RuntimeAnchorCoordinate;
                state.trainRailDistanceAlongPath = distanceAlongPath;
                state.trainRailPathPoint = pathPoint;
                state.trainRailFacingTangent = tangent;
            }
        }

        if (installationObject is SteamTrain steamTrain)
        {
            steamTrain.CaptureBurnEnergyState(
                out state.steamTrainStoredBurnEnergy,
                out state.steamTrainBurnEnergyGaugeCapacity);
            steamTrain.CaptureAutoDriveState(
                out state.steamTrainAutoDriveEnabled,
                out state.steamTrainAutoDriveTargetAStationName,
                out state.steamTrainAutoDriveTargetBStationName,
                out state.steamTrainAutoDriveFuelFilter,
                out state.steamTrainAutoDriveFreightFilter,
                out state.steamTrainAutoDriveRouteTargetStationName,
                out state.steamTrainAutoDriveLastArrivedStationName,
                out state.steamTrainAutoDriveStationWaitTimer);
            state.hasSteamTrainBurnEnergyState = true;
        }

        if (installationObject is Trainstation trainStation)
        {
            state.stationName = trainStation.StoredStationName;
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
        if (installationObject is IPersistentInstallationItemStorage itemStorage)
        {
            state.storedInstallationItemId = itemStorage.PersistentStoredItemId;
        }
        if (installationObject is IPersistentInstallationItemCollectionStorage collectionStorage)
        {
            collectionStorage.CapturePersistentStoredItemIds(state.storedInstallationItemIds);
        }

        return true;
    }

    private static int ResolvePipeQuarterTurnsFromCurrentRotation(
        Pipe pipe,
        int fallbackQuarterTurns)
    {
        int normalizedFallback = ((fallbackQuarterTurns % 4) + 4) % 4;
        if (pipe == null)
        {
            return normalizedFallback;
        }

        Pipe variantPrefab = pipe.VariantKind switch
        {
            PipeVariantKind.Corner => pipe.CornerVariantPrefab,
            PipeVariantKind.Tee => pipe.TeeVariantPrefab,
            PipeVariantKind.Cross => pipe.CrossVariantPrefab,
            _ => pipe.StraightVariantPrefab
        };
        if (variantPrefab == null)
        {
            variantPrefab = pipe;
        }

        int currentConnectionMask = pipe.GetConnectionMask(pipe.transform.rotation);
        for (int offset = 0; offset < 4; offset++)
        {
            int candidateQuarterTurns = (normalizedFallback + offset) % 4;
            Quaternion candidateRotation = variantPrefab.transform.rotation
                                           * Quaternion.Euler(0f, candidateQuarterTurns * 90f, 0f);
            if (variantPrefab.GetConnectionMask(candidateRotation) != currentConnectionMask)
            {
                continue;
            }

            return candidateQuarterTurns;
        }

        return normalizedFallback;
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

    private static string ResolveInstallationSaveItemName(int itemId, InstallationObject installationObject)
    {
        IReadOnlyList<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        ItemDefinition definition = FindInstallationDefinitionByObject(definitions, installationObject)
                                    ?? ItemDefinitionLookup.ResolveInstallationById(definitions, itemId);

        if (definition != null)
        {
            if (!string.IsNullOrWhiteSpace(definition.itemName))
            {
                return definition.itemName;
            }

            if (!string.IsNullOrWhiteSpace(definition.name))
            {
                return definition.name;
            }
        }

        return installationObject != null ? installationObject.name : string.Empty;
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
        if (RequiresBackgroundInstallationWake(storedState))
        {
            savedBackgroundInstallationStorageKeys.Add(storageKey);
        }
        else
        {
            savedBackgroundInstallationStorageKeys.Remove(storageKey);
        }

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
            savedBackgroundInstallationStorageKeys.Remove(duplicateKey);
            world?.RemoveInstallation(duplicateKey);
        }
    }

    private void ApplyInstallationSaveStates(IReadOnlyList<InstallationSaveEntry> entries)
    {
        if (entries == null || entries.Count <= 0)
        {
            return;
        }

        Dictionary<Vector2Int, InstallationSaveState> newestWallStatesByAnchor = null;
        for (int i = 0; i < entries.Count; i++)
        {
            InstallationSaveState state = entries[i]?.state;
            if (state == null)
            {
                continue;
            }

            if (!IsWallInstallationState(state))
            {
                StoreInstallationState(state);
                continue;
            }

            newestWallStatesByAnchor ??= new Dictionary<Vector2Int, InstallationSaveState>();
            if (!newestWallStatesByAnchor.TryGetValue(state.anchorCoordinate, out InstallationSaveState existingState)
                || ShouldPreferLoadedWallState(state, existingState))
            {
                newestWallStatesByAnchor[state.anchorCoordinate] = state;
            }
        }

        if (newestWallStatesByAnchor == null)
        {
            return;
        }

        foreach (KeyValuePair<Vector2Int, InstallationSaveState> pair in newestWallStatesByAnchor)
        {
            InstallationSaveState canonicalState = pair.Value.Clone();
            canonicalState.hasStorageKey = false;
            canonicalState.storageKey = default;
            StoreInstallationState(canonicalState);
        }
    }

    private static bool ShouldPreferLoadedWallState(
        InstallationSaveState incomingState,
        InstallationSaveState existingState)
    {
        return existingState == null
               || incomingState.placementSequence >= existingState.placementSequence;
    }

    private static bool RequiresBackgroundInstallationWake(InstallationSaveState state)
    {
        return state != null && (state.robotArmState != null || state.inputOutputState != null);
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

    private static bool SavedInstallationStateInteractsWithCoordinate(
        InstallationSaveState state,
        Vector2Int coordinate)
    {
        if (state == null)
        {
            return false;
        }

        if (IsAnyCoordinateNear(state.occupiedCoordinates, coordinate, 1))
        {
            return true;
        }

        InputOutputModule.PersistentState inputOutputState = state.inputOutputState;
        if (inputOutputState == null)
        {
            return false;
        }

        if (ContainsCoordinate(inputOutputState.inputEnergyCoordinates, coordinate)
            || ContainsCoordinate(inputOutputState.outputCoordinates, coordinate)
            || ContainsCoordinate(inputOutputState.pipeInputCoordinates, coordinate)
            || ContainsCoordinate(inputOutputState.gridCoordinates, coordinate)
            || ContainsCoordinate(inputOutputState.focusCoordinates, coordinate))
        {
            return true;
        }

        if (inputOutputState.inputItemAreas != null)
        {
            for (int i = 0; i < inputOutputState.inputItemAreas.Count; i++)
            {
                if (inputOutputState.inputItemAreas[i].coordinate == coordinate)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsCoordinate(IReadOnlyList<Vector2Int> coordinates, Vector2Int coordinate)
    {
        if (coordinates == null)
        {
            return false;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (coordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAnyCoordinateNear(IReadOnlyList<Vector2Int> coordinates, Vector2Int coordinate, int radius)
    {
        if (coordinates == null)
        {
            return false;
        }

        int clampedRadius = Mathf.Max(0, radius);
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int candidate = coordinates[i];
            if (Mathf.Abs(candidate.x - coordinate.x) <= clampedRadius
                && Mathf.Abs(candidate.y - coordinate.y) <= clampedRadius)
            {
                return true;
            }
        }

        return false;
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
        if (state == null || delta == 0)
        {
            return;
        }

        if (state.itemId >= 0)
        {
            savedInstallationItemTotal = Mathf.Max(0, savedInstallationItemTotal + delta);
            AdjustItemCount(savedInstallationCountsByItemId, state.itemId, delta);
        }

        if (state.storedInstallationItemId >= 0)
        {
            AdjustItemCount(
                savedInstallationStoredItemCountsByItemId,
                state.storedInstallationItemId,
                delta);
        }
    }

    private static void AdjustItemCount(Dictionary<int, int> countsByItemId, int itemId, int delta)
    {
        countsByItemId.TryGetValue(itemId, out int currentCount);
        int nextCount = currentCount + delta;
        if (nextCount > 0)
        {
            countsByItemId[itemId] = nextCount;
        }
        else
        {
            countsByItemId.Remove(itemId);
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
        if (state == null)
        {
            return;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = state.occupiedCoordinates;
        if (occupiedCoordinates != null)
        {
            bool isPipeState = IsPipeInstallationState(state);
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                Vector2Int coordinate = occupiedCoordinates[i];
                RegisterSavedCoordinateMapping(
                    savedInstallationAnchorsByCoordinate,
                    coordinate,
                    storageKey,
                    state);
                if (isPipeState)
                {
                    RegisterSavedCoordinateStorageKey(
                        savedPipeInstallationStorageKeysByOccupiedCoordinate,
                        coordinate,
                        storageKey);
                }
            }
        }

        RegisterSavedInteractionCoordinateMappings(state.inputOutputState, storageKey);
    }

    private void UnregisterSavedCoordinateMappings(InstallationSaveState state, Vector2Int storageKey)
    {
        if (state == null)
        {
            return;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = state.occupiedCoordinates;
        if (occupiedCoordinates != null)
        {
            bool isPipeState = IsPipeInstallationState(state);
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                Vector2Int coordinate = occupiedCoordinates[i];
                UnregisterSavedCoordinateMapping(
                    savedInstallationAnchorsByCoordinate,
                    coordinate,
                    storageKey);
                if (isPipeState)
                {
                    UnregisterSavedCoordinateStorageKey(
                        savedPipeInstallationStorageKeysByOccupiedCoordinate,
                        coordinate,
                        storageKey);
                }
            }
        }

        UnregisterSavedInteractionCoordinateMappings(state.inputOutputState, storageKey);
    }

    private void RegisterSavedInteractionCoordinateMappings(
        InputOutputModule.PersistentState inputOutputState,
        Vector2Int storageKey)
    {
        if (inputOutputState == null)
        {
            return;
        }

        RegisterSavedInteractionCoordinates(inputOutputState.inputEnergyCoordinates, storageKey);
        RegisterSavedInteractionCoordinates(inputOutputState.outputCoordinates, storageKey);
        RegisterSavedInteractionCoordinates(inputOutputState.pipeInputCoordinates, storageKey);
        RegisterSavedInteractionCoordinates(inputOutputState.gridCoordinates, storageKey);
        RegisterSavedInteractionCoordinates(inputOutputState.focusCoordinates, storageKey);

        IReadOnlyList<InputOutputModule.PersistentInputItemAreaState> inputItemAreas =
            inputOutputState.inputItemAreas;
        if (inputItemAreas == null)
        {
            return;
        }

        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            RegisterSavedInteractionCoordinate(inputItemAreas[i].coordinate, storageKey);
        }
    }

    private void UnregisterSavedInteractionCoordinateMappings(
        InputOutputModule.PersistentState inputOutputState,
        Vector2Int storageKey)
    {
        if (inputOutputState == null)
        {
            return;
        }

        UnregisterSavedInteractionCoordinates(inputOutputState.inputEnergyCoordinates, storageKey);
        UnregisterSavedInteractionCoordinates(inputOutputState.outputCoordinates, storageKey);
        UnregisterSavedInteractionCoordinates(inputOutputState.pipeInputCoordinates, storageKey);
        UnregisterSavedInteractionCoordinates(inputOutputState.gridCoordinates, storageKey);
        UnregisterSavedInteractionCoordinates(inputOutputState.focusCoordinates, storageKey);

        IReadOnlyList<InputOutputModule.PersistentInputItemAreaState> inputItemAreas =
            inputOutputState.inputItemAreas;
        if (inputItemAreas == null)
        {
            return;
        }

        for (int i = 0; i < inputItemAreas.Count; i++)
        {
            UnregisterSavedInteractionCoordinate(inputItemAreas[i].coordinate, storageKey);
        }
    }

    private void RegisterSavedInteractionCoordinates(
        IReadOnlyList<Vector2Int> coordinates,
        Vector2Int storageKey)
    {
        if (coordinates == null)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            RegisterSavedInteractionCoordinate(coordinates[i], storageKey);
        }
    }

    private void UnregisterSavedInteractionCoordinates(
        IReadOnlyList<Vector2Int> coordinates,
        Vector2Int storageKey)
    {
        if (coordinates == null)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            UnregisterSavedInteractionCoordinate(coordinates[i], storageKey);
        }
    }

    private void RegisterSavedInteractionCoordinate(Vector2Int coordinate, Vector2Int storageKey)
    {
        RegisterSavedCoordinateStorageKey(
            savedInstallationStorageKeysByInteractionCoordinate,
            coordinate,
            storageKey);
    }

    private void UnregisterSavedInteractionCoordinate(Vector2Int coordinate, Vector2Int storageKey)
    {
        UnregisterSavedCoordinateStorageKey(
            savedInstallationStorageKeysByInteractionCoordinate,
            coordinate,
            storageKey);
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

    private void RegisterSavedCoordinateMapping(
        Dictionary<Vector2Int, Vector2Int> storageKeysByCoordinate,
        Vector2Int coordinate,
        Vector2Int storageKey,
        InstallationSaveState state)
    {
        if (ShouldReplaceSavedCoordinateMapping(
                storageKeysByCoordinate,
                coordinate,
                storageKey,
                state))
        {
            storageKeysByCoordinate[coordinate] = storageKey;
        }
    }

    private static void RegisterSavedCoordinateStorageKey(
        Dictionary<Vector2Int, HashSet<Vector2Int>> storageKeysByCoordinate,
        Vector2Int coordinate,
        Vector2Int storageKey)
    {
        if (!storageKeysByCoordinate.TryGetValue(coordinate, out HashSet<Vector2Int> storageKeys))
        {
            storageKeys = new HashSet<Vector2Int>();
            storageKeysByCoordinate[coordinate] = storageKeys;
        }

        storageKeys.Add(storageKey);
    }

    private static void UnregisterSavedCoordinateMapping(
        Dictionary<Vector2Int, Vector2Int> storageKeysByCoordinate,
        Vector2Int coordinate,
        Vector2Int storageKey)
    {
        if (storageKeysByCoordinate.TryGetValue(coordinate, out Vector2Int mappedStorageKey)
            && mappedStorageKey == storageKey)
        {
            storageKeysByCoordinate.Remove(coordinate);
        }
    }

    private static void UnregisterSavedCoordinateStorageKey(
        Dictionary<Vector2Int, HashSet<Vector2Int>> storageKeysByCoordinate,
        Vector2Int coordinate,
        Vector2Int storageKey)
    {
        if (!storageKeysByCoordinate.TryGetValue(coordinate, out HashSet<Vector2Int> storageKeys))
        {
            return;
        }

        storageKeys.Remove(storageKey);
        if (storageKeys.Count <= 0)
        {
            storageKeysByCoordinate.Remove(coordinate);
        }
    }

    private bool ShouldReplaceSavedCoordinateMapping(
        Dictionary<Vector2Int, Vector2Int> storageKeysByCoordinate,
        Vector2Int coordinate,
        Vector2Int storageKey,
        InstallationSaveState state)
    {
        if (!storageKeysByCoordinate.TryGetValue(coordinate, out Vector2Int existingStorageKey)
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
        if (!TryResolveInstallationMapObject(state, out MapObject mapObject))
        {
            return false;
        }

        return mapObject is Train
               || mapObject.GetComponent<Train>() != null
               || mapObject.GetComponentInChildren<Train>(true) != null;
    }

    private static bool IsWallInstallationState(InstallationSaveState state)
    {
        if (!TryResolveInstallationMapObject(state, out MapObject mapObject))
        {
            return false;
        }

        return mapObject is Wall
               || mapObject.GetComponent<Wall>() != null
               || mapObject.GetComponentInChildren<Wall>(true) != null;
    }

    private static bool IsPipeInstallationState(InstallationSaveState state)
    {
        if (state == null)
        {
            return false;
        }

        if (state.pipeConnectionMask >= 0)
        {
            return true;
        }

        if (!TryResolveInstallationMapObject(state, out MapObject mapObject))
        {
            return false;
        }

        return mapObject is Pipe
               || mapObject.GetComponent<Pipe>() != null
               || mapObject.GetComponentInChildren<Pipe>(true) != null;
    }

    private static bool TryResolveInstallationMapObject(
        InstallationSaveState state,
        out MapObject mapObject)
    {
        mapObject = null;
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

            mapObject = definition.mapObject;
            return true;
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
