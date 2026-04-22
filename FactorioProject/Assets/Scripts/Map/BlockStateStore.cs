using System;
using System.Collections.Generic;
using UnityEngine;

public class BlockStateStore : MonoBehaviour
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

    private readonly Dictionary<Vector2Int, Resource.ResourceSaveState> savedStates = new Dictionary<Vector2Int, Resource.ResourceSaveState>();
    private readonly Dictionary<Vector2Int, List<int>> savedFloorObjectStates = new Dictionary<Vector2Int, List<int>>();
    private readonly Dictionary<Vector2Int, InstallationSaveState> savedInstallationStates = new Dictionary<Vector2Int, InstallationSaveState>();
    private readonly Dictionary<Vector2Int, Vector2Int> savedInstallationAnchorsByCoordinate = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Dictionary<Vector2Int, LiveInstallationRecord> liveInstallationStates = new Dictionary<Vector2Int, LiveInstallationRecord>();
    private readonly Dictionary<Vector2Int, Vector2Int> liveInstallationAnchorsByCoordinate = new Dictionary<Vector2Int, Vector2Int>();

    public void Save(Vector2Int worldCoordinate, Resource resource)
    {
        if (resource == null)
        {
            return;
        }

        savedStates[worldCoordinate] = resource.CaptureState();
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
            return;
        }

        savedFloorObjectStates[worldCoordinate] = itemIds;
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
        return savedFloorObjectStates.TryGetValue(worldCoordinate, out itemIds);
    }

    public bool TryGetFloorObjectsCopy(Vector2Int worldCoordinate, out List<int> itemIds)
    {
        if (savedFloorObjectStates.TryGetValue(worldCoordinate, out List<int> savedItems) && savedItems != null)
        {
            itemIds = new List<int>(savedItems);
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
            return;
        }

        savedFloorObjectStates[worldCoordinate] = new List<int>(itemIds);
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
    }

    public void RemoveInstallation(Vector2Int anchorCoordinate)
    {
        if (savedInstallationStates.TryGetValue(anchorCoordinate, out InstallationSaveState savedState))
        {
            UnregisterSavedCoordinateMappings(savedState);
            savedInstallationStates.Remove(anchorCoordinate);
        }

        UnregisterLiveInstallation(anchorCoordinate);
    }

    public void ClearStates()
    {
        savedStates.Clear();
        savedFloorObjectStates.Clear();
        savedInstallationStates.Clear();
        savedInstallationAnchorsByCoordinate.Clear();
        liveInstallationStates.Clear();
        liveInstallationAnchorsByCoordinate.Clear();
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
