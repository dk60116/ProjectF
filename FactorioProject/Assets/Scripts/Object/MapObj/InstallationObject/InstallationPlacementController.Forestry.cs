using ProjectF.MapObjects;
using UnityEngine;

public partial class InstallationPlacementController
{
    private bool TryMaterializeDataOnlyForestryForEditing(ForestryInstance instance, out InstallationObject installation)
    {
        installation = null;
        var terrain = ResolveInstallPreviewTerrain();
        if (terrain == null || instance == null || !instance.IsRuntimeActive) return false;
        var proxy = terrain.CreateInstallationObject(instance.Prototype, terrain.transform);
        if (proxy == null) return false;
        instance.Persist();
        proxy.transform.SetPositionAndRotation(instance.WorldPosition, instance.WorldRotation);
        proxy.transform.localScale = instance.Template.Scale;
        ConfigureInstalledObjectRuntime(proxy, instance.AnchorCoordinate, instance.Placement.quarterTurns,
            placementSequence: instance.SimulationId, occupiedCoordinatesOverride: instance.RuntimeOccupiedCoordinates);
        proxy.ApplyItemFilterMask(instance.Placement.itemFilterMaskWords, instance.Placement.itemFilterMaskInitialized);
        if (proxy is LoggingMachine logger)
        {
            logger.ApplyTreeFilterState(instance.Placement.loggingTreeFilterInitialized,
                instance.Placement.loggingEnabledTreeDefinitionKeys, instance.Placement.loggingMinimumGrowth, instance.Placement.loggingMaximumGrowth);
            logger.DataProcess = instance.Placement.loggingProcess;
        }
        if (proxy is SeedPlanter planter) planter.ApplyPersistentState(instance.Placement.inputOutputState.Clone());
        instance.World.Remove(instance.StorageKey);
        foreach (var coordinate in instance.RuntimeOccupiedCoordinates)
            if (terrain.TryGetLoadedBlock(coordinate, out var block) && block != null) block.SetMapObject(proxy);
        installation = proxy; return true;
    }
    public bool TryUpgradeDataForestry(ForestryInstance current, ItemDefinition definition, out IMapObjectTarget upgraded)
    {
        upgraded = null;
        if (!TryMaterializeDataOnlyForestryForEditing(current, out var proxy)) return false;
        if (!TryUpgradeInstalledObject(proxy, definition, out var replacement))
        { RestoreDataFacilityEditorProxy(proxy); return false; }
        if (ForestryWorld.Current != null && ForestryWorld.Current.TryGet(current.StorageKey, out var entity))
        { upgraded = entity; if (replacement != null) ResolveInstallPreviewTerrain()?.ReleaseInstallationObject(replacement); }
        else upgraded = replacement;
        return upgraded != null;
    }
}
