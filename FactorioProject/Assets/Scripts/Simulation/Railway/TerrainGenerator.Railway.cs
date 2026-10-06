using UnityEngine;
using ProjectF.Railway;

public partial class TerrainGenerator
{
    internal bool RegisterDataOnlyRailwayState(BlockStateStore.InstallationSaveState state, InstallationObject prototype, out RailwayInstance result)
    {
        result = null;
        if (state == null || prototype == null || prototype.gameObject.scene.IsValid() || !(prototype is Railload || prototype is Trainstation)) return false;
        if (string.IsNullOrWhiteSpace(state.itemName))
            state.itemName = BlockStateStore.ResolveInstallationSaveItemName(state.itemId, prototype);
        EnsureResourceStateStore();
        var world = GetRailWorld();
        if (!world.TryGetRuntime(BlockStateStore.GetInstallationStorageKey(state), out result))
        {
            if (!resourceStateStore.RegisterDataOnlyInstallationSharedState(state, out var stored)) return false;
            result = world.RegisterRuntime(stored, prototype, this);
        }
        if (result == null) return false;
        if (resourceStateStore.TryGetInstallationHandle(result.StorageKey, out var handle)) result.Handle = handle;
        if (result is ITrainStationTarget station && (!station.HasAssignedStationName || !station.HasAssignedStationColor))
            EnsureTrainStationIdentityAssigned(station);
        var coordinates = result.RuntimeOccupiedCoordinates;
        for (int i = 0; i < coordinates.Count; i++)
            if (TryGetLoadedBlock(coordinates[i], out var block) && block != null) block.SetMapObject(result);
        return true;
    }

    internal bool ConvertRailwayPresentation(InstallationObject installation, MapObject sourcePrefab, out RailwayInstance result)
    {
        result = null;
        if (!(installation is Railload || installation is Trainstation) || installation.ExcludeFromTerrainPersistence) return false;
        EnsureResourceStateStore();
        if (!resourceStateStore.TryCaptureInstallationState(installation, out var state)) return false;
        state.hasWorldPose = true; state.worldPosition = installation.transform.position; state.worldRotation = installation.transform.rotation;
        var prototype = ResolveInstallationSourcePrefab(state) ?? sourcePrefab;
        return RegisterDataOnlyRailwayState(state, prototype as InstallationObject, out result);
    }

    private bool TryRestoreDataOnlyRailway(BlockStateStore.InstallationSaveState state)
    {
        var prototype = ResolveInstallationSourcePrefab(state) as InstallationObject;
        if (!(prototype is Railload || prototype is Trainstation)) return false;
        if (!state.hasWorldPose)
        {
            var placement = ResolveInstallationPlacementController();
            state.worldPosition = placement != null ? placement.GetInstalledObjectWorldPosition(state.anchorCoordinate, prototype, state.quarterTurns)
                : new Vector3(state.anchorCoordinate.x, transform.position.y, state.anchorCoordinate.y);
            state.worldRotation = prototype is Railload ? Quaternion.identity : prototype.transform.rotation * Quaternion.Euler(0, state.quarterTurns * 90, 0);
            state.hasWorldPose = true;
        }
        return RegisterDataOnlyRailwayState(state, prototype, out _);
    }

    public void SaveTrainStationState(ITrainStationTarget station)
    {
        if (station is InstallationObject native) SaveRuntimeInstallationState(native);
        else if (station is TrainStationInstance data && data.IsTargetActive) EnsureTrainStationIdentityAssigned(data);
    }

    internal void NotifyTrainStationMapChanged() => resourceStateStore?.MarkMapMarkersChanged();

    public bool RemoveDataOnlyRailway(RailwayInstance instance)
    {
        if (instance == null || !instance.IsTargetActive) return false;
        resourceStateStore.RemoveInstallation(instance.StorageKey);
        return true;
    }
}
