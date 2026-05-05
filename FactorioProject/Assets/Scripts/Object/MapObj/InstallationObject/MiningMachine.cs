using System.Collections.Generic;
using UnityEngine;

public class MiningMachine : InputOutputModule
{
    [SerializeField]
    private Transform drill;

    public bool TryAppendPlacementOutputItemIds(
        TerrainGenerator terrain,
        Vector2Int anchorCoordinate,
        ISet<int> outputItemIds)
    {
        if (outputItemIds == null
            || !TryResolveMiningResource(terrain, anchorCoordinate, out Resource resource)
            || !resource.TryPeekMachineHarvestOutput(out int outputItemId, out _)
            || outputItemId < 0)
        {
            return false;
        }

        outputItemIds.Add(outputItemId);
        return true;
    }

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

    protected override bool AppendOutputItemIds(ISet<int> outputItemIds)
    {
        bool foundAny = base.AppendOutputItemIds(outputItemIds);
        if (outputItemIds == null
            || !TryResolveMiningResource(out Resource resource)
            || !resource.TryPeekMachineHarvestOutput(out int outputItemId, out _)
            || outputItemId < 0)
        {
            return foundAny;
        }

        outputItemIds.Add(outputItemId);
        return true;
    }

    private bool TryResolveMiningResource(out Resource resource)
    {
        resource = null;
        if (!TryGetLoadedBlock(RuntimeAnchorCoordinate, out Block block) || block == null)
        {
            return false;
        }

        return TryResolveMiningResource(block, out resource);
    }

    private static bool TryResolveMiningResource(
        TerrainGenerator terrain,
        Vector2Int anchorCoordinate,
        out Resource resource)
    {
        resource = null;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(anchorCoordinate, out Block block)
            || block == null)
        {
            return false;
        }

        return TryResolveMiningResource(block, out resource);
    }

    private static bool TryResolveMiningResource(Block block, out Resource resource)
    {
        resource = null;
        if (block == null)
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
