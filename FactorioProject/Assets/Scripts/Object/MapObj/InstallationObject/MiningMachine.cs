using UnityEngine;

public class MiningMachine : InputOutputModule
{
    [SerializeField]
    private Transform drill;

    protected override void TryStartNextCraft()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null
            || !TryResolveMiningResource(out Resource resource)
            || !resource.TryPeekMachineHarvestOutput(out int outputItemId, out int outputCount))
        {
            return;
        }

        if (!TryResolveOutputBlock(outputItemId, outputCount, out _)
            || !TryEnsureCraftStartEnergy(installedDefinition))
        {
            return;
        }

        BeginActiveCraft(-1, outputItemId, outputCount, installedDefinition);
    }

    protected override bool TryCompleteActiveCraft()
    {
        if (ActiveOutputItemId < 0 || ActiveOutputCount <= 0)
        {
            ClearActiveCraft();
            return false;
        }

        if (!TryResolveOutputBlock(ActiveOutputItemId, ActiveOutputCount, out Block outputBlock) || outputBlock == null)
        {
            return false;
        }

        if (!TryResolveMiningResource(out Resource resource)
            || !resource.TryPeekMachineHarvestOutput(out int outputItemId, out int outputCount))
        {
            ClearActiveCraft();
            return false;
        }

        if (outputItemId != ActiveOutputItemId || outputCount != ActiveOutputCount)
        {
            ClearActiveCraft();
            return false;
        }

        Vector3 startWorldPosition = resource.FocusPoint;
        if (!resource.TryHarvestForMachine(out int harvestedItemId, out int harvestedCount))
        {
            ClearActiveCraft();
            return false;
        }

        if (!TryEmitOutputItemsToBlock(outputBlock, harvestedItemId, harvestedCount, startWorldPosition))
        {
            ClearActiveCraft();
            return false;
        }

        ClearActiveCraft();
        return true;
    }

    private bool TryResolveMiningResource(out Resource resource)
    {
        resource = null;
        if (!TryGetLoadedBlock(RuntimeAnchorCoordinate, out Block block) || block == null)
        {
            return false;
        }

        resource = block.Resource;
        if (resource == null || !resource.CanHarvest || !resource.gameObject.activeInHierarchy)
        {
            return false;
        }

        return true;
    }
}
