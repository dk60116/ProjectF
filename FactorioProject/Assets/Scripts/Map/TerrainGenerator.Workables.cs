using ProjectF.MapObjects;
using UnityEngine;

public partial class TerrainGenerator
{
    internal bool ConvertWorkablePresentation(WorkableObject presentation, WorkableObject source,
        out WorkableInstance workable)
    {
        workable = null;
        EnsureResourceStateStore();
        if (presentation == null || !resourceStateStore.TryCaptureInstallationState(presentation, out var state)) return false;
        source = source != null ? source : ResolveInstallationSourcePrefab(state, ResolveInstallationPlacementController(),
            ResolveInstallationDefinition(state)) as WorkableObject;
        if (!WorkableWorld.Supports(source)) return false;
        state.hasWorldPose = true; state.worldPosition = presentation.transform.position;
        state.worldRotation = presentation.transform.rotation;
        workable = RegisterDataOnlyWorkableState(source, state);
        return workable != null;
    }
    internal WorkableInstance RegisterDataOnlyWorkableState(WorkableObject prototype, BlockStateStore.InstallationSaveState state)
    {
        if (prototype == null || state == null) return null;
        EnsureResourceStateStore();
        var world = WorkableWorld.Ensure(this);
        if (world.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { world.Bind(existing); return existing; }
        if (!world.SupportsPrototype(prototype)) return null;
        state.placementSequence = InstallationObject.ClaimNextPlacementSequence(state.placementSequence);
        if (!resourceStateStore.RegisterDataOnlyInstallationSharedState(state, out var stored)) return null;
        return world.Register(prototype, stored);
    }
    private bool TryRestoreDataOnlyWorkable(BlockStateStore.InstallationSaveState state)
    {
        if (WorkableWorld.Current != null && WorkableWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { WorkableWorld.Current.Bind(existing); return true; }
        var definition = ResolveInstallationDefinition(state);
        var controller = ResolveInstallationPlacementController();
        if (!(ResolveInstallationSourcePrefab(state, controller, definition) is WorkableObject prototype)
            || !WorkableWorld.Supports(prototype)) return false;
        if (!state.hasWorldPose)
        {
            state.worldPosition = controller != null ? controller.GetInstalledObjectWorldPosition(state.anchorCoordinate, prototype, state.quarterTurns)
                : new Vector3(state.anchorCoordinate.x, transform.position.y, state.anchorCoordinate.y);
            state.worldRotation = controller != null ? controller.GetInstalledObjectRotation(prototype, state.quarterTurns)
                : prototype.transform.rotation * Quaternion.Euler(0, state.quarterTurns * 90, 0);
            state.hasWorldPose = true;
        }
        return RegisterDataOnlyWorkableState(prototype, state) != null;
    }
}
