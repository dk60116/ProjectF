using UnityEngine;

public partial class TerrainGenerator
{
    internal bool ConvertProductionPresentation(InputOutputModule presentation, InputOutputModule source,
        out ProductionFacilityInstance facility)
    {
        facility = null;
        if (presentation == null || presentation.GetType() != typeof(InputOutputModule) && !(presentation is ProductionMachine)) return false;
        EnsureResourceStateStore();
        if (!resourceStateStore.TryCaptureInstallationState(presentation, out var state)) return false;
        source = source != null ? source : ResolveInstallationSourcePrefab(state, ResolveInstallationPlacementController(),
            ResolveInstallationDefinition(state)) as InputOutputModule;
        if (!ProductionWorld.Supports(source)) return false;
        state.hasWorldPose = true; state.worldPosition = presentation.transform.position;
        state.worldRotation = presentation.transform.rotation;
        facility = RegisterDataOnlyProductionState(source, state);
        return facility != null;
    }
    internal ProductionFacilityInstance RegisterDataOnlyProductionState(InputOutputModule prototype, BlockStateStore.InstallationSaveState state)
    {
        if (prototype == null || state == null) return null;
        EnsureResourceStateStore();
        var world = ProductionWorld.Ensure(this);
        if (world.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { world.Bind(existing); return existing; }
        if (!world.SupportsPrototype(prototype)) return null;
        state.placementSequence = InstallationObject.ClaimNextPlacementSequence(state.placementSequence);
        if (!resourceStateStore.RegisterDataOnlyInstallationSharedState(state, out var stored)) return null;
        return world.Register(prototype, stored);
    }
    private bool TryRestoreDataOnlyProduction(BlockStateStore.InstallationSaveState state)
    {
        if (ProductionWorld.Current != null && ProductionWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { ProductionWorld.Current.Bind(existing); return true; }
        var definition = ResolveInstallationDefinition(state);
        var controller = ResolveInstallationPlacementController();
        if (!(ResolveInstallationSourcePrefab(state, controller, definition) is InputOutputModule prototype)
            || !ProductionWorld.Supports(prototype)) return false;
        if (!state.hasWorldPose)
        {
            state.worldPosition = controller != null ? controller.GetInstalledObjectWorldPosition(state.anchorCoordinate, prototype, state.quarterTurns)
                : new Vector3(state.anchorCoordinate.x, transform.position.y, state.anchorCoordinate.y);
            state.worldRotation = controller != null ? controller.GetInstalledObjectRotation(prototype, state.quarterTurns)
                : prototype.transform.rotation * Quaternion.Euler(0, state.quarterTurns * 90, 0);
            state.hasWorldPose = true;
        }
        return RegisterDataOnlyProductionState(prototype, state) != null;
    }
}
