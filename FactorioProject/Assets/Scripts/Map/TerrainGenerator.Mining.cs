using UnityEngine;

public partial class TerrainGenerator
{
    internal bool ConvertMiningPresentation(MiningMachine presentation, MiningMachine source,
        out MiningMachineInstance miner)
    {
        miner = null;
        EnsureResourceStateStore();
        if (presentation == null || !resourceStateStore.TryCaptureInstallationState(presentation, out var state)) return false;
        source = source != null ? source : ResolveInstallationSourcePrefab(state, ResolveInstallationPlacementController(),
            ResolveInstallationDefinition(state)) as MiningMachine;
        if (!MiningWorld.Supports(source)) return false;
        state.hasWorldPose = true; state.worldPosition = presentation.transform.position;
        state.worldRotation = presentation.transform.rotation;
        miner = RegisterDataOnlyMiningState(source, state);
        return miner != null;
    }
    internal MiningMachineInstance RegisterDataOnlyMiningState(MiningMachine prototype, BlockStateStore.InstallationSaveState state)
    {
        if (prototype == null || state == null) return null;
        EnsureResourceStateStore();
        var world = MiningWorld.Ensure(this);
        if (world.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { world.Bind(existing); return existing; }
        if (!world.SupportsPrototype(prototype)) return null;
        state.placementSequence = InstallationObject.ClaimNextPlacementSequence(state.placementSequence);
        if (!resourceStateStore.RegisterDataOnlyInstallationSharedState(state, out var stored)) return null;
        return world.Register(prototype, stored);
    }
    private bool TryRestoreDataOnlyMining(BlockStateStore.InstallationSaveState state)
    {
        if (MiningWorld.Current != null && MiningWorld.Current.TryGet(BlockStateStore.GetInstallationStorageKey(state), out var existing))
        { MiningWorld.Current.Bind(existing); return true; }
        var definition = ResolveInstallationDefinition(state);
        var controller = ResolveInstallationPlacementController();
        if (!(ResolveInstallationSourcePrefab(state, controller, definition) is MiningMachine prototype)
            || !MiningWorld.Supports(prototype)) return false;
        if (!state.hasWorldPose)
        {
            state.worldPosition = controller != null ? controller.GetInstalledObjectWorldPosition(state.anchorCoordinate, prototype, state.quarterTurns)
                : new Vector3(state.anchorCoordinate.x, transform.position.y, state.anchorCoordinate.y);
            state.worldRotation = controller != null ? controller.GetInstalledObjectRotation(prototype, state.quarterTurns)
                : prototype.transform.rotation * Quaternion.Euler(0, state.quarterTurns * 90, 0);
            state.hasWorldPose = true;
        }
        return RegisterDataOnlyMiningState(prototype, state) != null;
    }
}
