using UnityEngine;

public partial class TerrainGenerator
{
    internal bool ConvertRobotArmPresentation(RobotArm presentation, RobotArm source = null)
    {
        EnsureResourceStateStore();
        if (presentation == null || !resourceStateStore.TryCaptureInstallationState(presentation, out var state)) return false;
        source = source != null ? source : ResolveInstallationSourcePrefab(state, ResolveInstallationPlacementController(), ResolveInstallationDefinition(state)) as RobotArm;
        if (source == null || source == presentation) return false;
        state.hasWorldPose = true;
        state.worldPosition = presentation.transform.position;
        state.worldRotation = presentation.transform.rotation;
        return RegisterDataOnlyRobotArm(source, state) != null;
    }
    public RobotArmInstance RegisterDataOnlyRobotArm(RobotArm prototype, BlockStateStore.InstallationSaveState state)
    {
        if (prototype == null || state == null) return null;
        EnsureResourceStateStore();
        var world = RobotArmWorld.Ensure(this);
        ResolveInstallationPlacementController()?.ResolveAreaMarkerRenderer();
        Vector2Int key = state.hasStorageKey ? state.storageKey : state.anchorCoordinate;
        if (world.TryGet(key, out var existing)) { world.Bind(existing); return existing; }
        state.placementSequence = InstallationObject.ClaimNextPlacementSequence(state.placementSequence);
        if (!resourceStateStore.RegisterDataOnlyInstallation(state, out var stored)) return null;
        return world.Register(prototype, stored);
    }
    private bool TryRestoreDataOnlyRobotArm(BlockStateStore.InstallationSaveState state)
    {
        var definition = ResolveInstallationDefinition(state);
        var controller = ResolveInstallationPlacementController();
        if (!(ResolveInstallationSourcePrefab(state, controller, definition) is RobotArm prototype)) return false;
        if (!state.hasWorldPose)
        {
            state.worldPosition = controller != null
                ? controller.GetInstalledObjectWorldPosition(state.anchorCoordinate, prototype, state.quarterTurns)
                : new Vector3(state.anchorCoordinate.x, transform.position.y, state.anchorCoordinate.y);
            state.worldRotation = controller != null ? controller.GetInstalledObjectRotation(prototype, state.quarterTurns)
                : prototype.transform.rotation * Quaternion.Euler(0, state.quarterTurns * 90, 0);
            state.hasWorldPose = true;
        }
        return RegisterDataOnlyRobotArm(prototype, state) != null;
    }
}
