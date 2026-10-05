using ProjectF.MapObjects;
using UnityEngine;

public partial class TerrainGenerator
{
    internal bool ConvertForestryPresentation(InstallationObject presentation, InstallationObject source,
        out ForestryInstance facility)
    {
        facility = null;
        EnsureResourceStateStore();
        if (presentation == null || !resourceStateStore.TryCaptureInstallationState(presentation, out var state)) return false;
        source = source != null ? source : ResolveInstallationSourcePrefab(state, ResolveInstallationPlacementController(),
            ResolveInstallationDefinition(state)) as InstallationObject;
        if (!ForestryWorld.Supports(source)) return false;
        state.hasWorldPose = true; state.worldPosition = presentation.transform.position;
        state.worldRotation = presentation.transform.rotation;
        facility = RegisterDataOnlyForestryState(source, state);
        return facility != null;
    }
    internal ForestryInstance RegisterDataOnlyForestryState(InstallationObject prototype, BlockStateStore.InstallationSaveState state)
    {
        if (prototype == null || state == null) return null;
        EnsureResourceStateStore();
        var world = ForestryWorld.Ensure(this);
        if (world.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { world.Bind(existing); return existing; }
        if (!world.SupportsPrototype(prototype)) return null;
        state.placementSequence = InstallationObject.ClaimNextPlacementSequence(state.placementSequence);
        if (!resourceStateStore.RegisterDataOnlyInstallationSharedState(state, out var stored)) return null;
        return world.Register(prototype, stored);
    }
    private bool TryRestoreDataOnlyForestry(BlockStateStore.InstallationSaveState state)
    {
        if (ForestryWorld.Current != null && ForestryWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { ForestryWorld.Current.Bind(existing); return true; }
        var definition = ResolveInstallationDefinition(state);
        var controller = ResolveInstallationPlacementController();
        if (!(ResolveInstallationSourcePrefab(state, controller, definition) is InstallationObject prototype)
            || !ForestryWorld.Supports(prototype)) return false;
        if (!state.hasWorldPose)
        {
            state.worldPosition = controller != null ? controller.GetInstalledObjectWorldPosition(state.anchorCoordinate, prototype, state.quarterTurns)
                : new Vector3(state.anchorCoordinate.x, transform.position.y, state.anchorCoordinate.y);
            state.worldRotation = controller != null ? controller.GetInstalledObjectRotation(prototype, state.quarterTurns)
                : prototype.transform.rotation * Quaternion.Euler(0, state.quarterTurns * 90, 0);
            state.hasWorldPose = true;
        }
        return RegisterDataOnlyForestryState(prototype, state) != null;
    }
}

