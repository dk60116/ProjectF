using UnityEngine;

public partial class TerrainGenerator
{
    private ProjectF.MapObjects.MapObjectHealthPresentation healthPresentation;

    private void UpdateMapObjectHealthPresentation()
    {
        if (resourceStateStore == null) return;
        if (healthPresentation == null && (resourceStateStore.DamagedInstallations.Count > 0 || resourceStateStore.Wreckage.Count > 0))
            healthPresentation = new ProjectF.MapObjects.MapObjectHealthPresentation(this, resourceStateStore);
        healthPresentation?.Update();
    }

    internal MapObject ResolveWreckagePrototype(BlockStateStore.InstallationSaveState state) => ResolveInstallationSourcePrefab(state);

    internal Vector3 GetHealthBarPosition(BlockStateStore.InstallationSaveState state)
    {
        var key = BlockStateStore.GetInstallationStorageKey(state);
        if (resourceStateStore.TryGetLiveInstallationReadOnly(key, out var live, out _) && live != null)
            return live.transform.position;
        if (ConveyorWorld.Current != null && ConveyorWorld.Current.TryGetByStorageKey(key, out var belt)) return belt.WorldPosition;
        if (PipeWorld.Current != null && PipeWorld.Current.TryGetByStorageKey(key, out var pipe)) return pipe.WorldPosition;
        if (TryGetLoadedBlock(state.anchorCoordinate, out var block) && block?.MapObject is IMapObjectTarget target
            && GetMapObjectPlacementSequence(target) == state.placementSequence) return target.WorldPosition;
        return state.hasWorldPose ? state.worldPosition : new Vector3(state.anchorCoordinate.x, 0f, state.anchorCoordinate.y);
    }

    public bool TrySelectWreckage(Vector2 pointer, out long sequence)
    {
        sequence = 0L;
        var camera = Camera.main;
        return camera != null && healthPresentation != null
            && healthPresentation.TryRaycastWreckage(camera.ScreenPointToRay(pointer), out sequence);
    }

    public bool RemoveWreckage(long sequence) => resourceStateStore != null && resourceStateStore.RemoveWreckage(sequence);
}
