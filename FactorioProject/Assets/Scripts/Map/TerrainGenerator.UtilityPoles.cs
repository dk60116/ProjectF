using UnityEngine;
using ProjectF.Power;

public partial class TerrainGenerator
{
    internal bool ConvertUtilityPolePresentation(UtilityPole presentation, UtilityPole source, out UtilityPoleRuntime pole)
    {
        pole = null; if (presentation == null) return false;
        EnsureResourceStateStore();
        if (!resourceStateStore.TryCaptureInstallationState(presentation, out var state)) return false;
        source = source != null ? source : ResolveInstallationSourcePrefab(state, ResolveInstallationPlacementController(), ResolveInstallationDefinition(state)) as UtilityPole;
        if (!UtilityPoleWorld.Supports(source)) return false;
        state.hasWorldPose = true; state.worldPosition = presentation.transform.position; state.worldRotation = presentation.transform.rotation;
        UtilityPole.BeginTopologyRefreshBatch();
        try { pole = RegisterDataOnlyUtilityPoleState(source, state); }
        finally { UtilityPole.EndTopologyRefreshBatch(false); }
        return pole != null;
    }
    internal UtilityPoleRuntime RegisterDataOnlyUtilityPoleState(UtilityPole prototype, BlockStateStore.InstallationSaveState state)
    {
        if (prototype == null || state == null) return null;
        EnsureResourceStateStore(); var world = UtilityPoleWorld.Ensure(this);
        if (world.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing)) { world.Bind(existing); return existing; }
        if (!UtilityPoleWorld.Supports(prototype)) return null;
        state.placementSequence = InstallationObject.ClaimNextPlacementSequence(state.placementSequence);
        if (!resourceStateStore.RegisterDataOnlyInstallationSharedState(state, out var stored)) return null;
        return world.Register(prototype, stored);
    }
    private bool TryRestoreDataOnlyUtilityPole(BlockStateStore.InstallationSaveState state)
    {
        if (UtilityPoleWorld.Current != null && UtilityPoleWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { UtilityPoleWorld.Current.Bind(existing); return true; }
        var definition = ResolveInstallationDefinition(state); var controller = ResolveInstallationPlacementController();
        if (!(ResolveInstallationSourcePrefab(state, controller, definition) is UtilityPole prototype) || !UtilityPoleWorld.Supports(prototype)) return false;
        if (!state.hasWorldPose)
        {
            state.worldPosition = controller != null ? controller.GetInstalledObjectWorldPosition(state.anchorCoordinate, prototype, state.quarterTurns)
                : new Vector3(state.anchorCoordinate.x, transform.position.y, state.anchorCoordinate.y);
            state.worldRotation = controller != null ? controller.GetInstalledObjectRotation(prototype, state.quarterTurns)
                : prototype.transform.rotation * Quaternion.Euler(0, state.quarterTurns * 90, 0);
            state.hasWorldPose = true;
        }
        return RegisterDataOnlyUtilityPoleState(prototype, state) != null;
    }
}
