using System.Collections.Generic;
using UnityEngine;

public class MiningMachine : InputOutputModule
{
    [SerializeField]
    private Transform drill;

    private readonly List<Resource> miningResourceCandidates = new List<Resource>(4);
    private Resource activeMiningResource;
    private int activeMiningResourceIndex = -1;
    private int nextMiningResourceIndex;

    protected override void OnDisable()
    {
        ClearActiveMiningResourceSelection();
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        ClearActiveMiningResourceSelection();
        nextMiningResourceIndex = 0;
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        ClearActiveMiningResourceSelection();
        nextMiningResourceIndex = 0;
    }

    public bool TryAppendPlacementOutputItemIds(
        TerrainGenerator terrain,
        IReadOnlyList<Vector2Int> miningCoordinates,
        ISet<int> outputItemIds)
    {
        if (outputItemIds == null || terrain == null || miningCoordinates == null || miningCoordinates.Count <= 0)
        {
            return false;
        }

        bool foundAny = false;
        for (int i = 0; i < miningCoordinates.Count; i++)
        {
            if (!TryResolveMiningResource(terrain, miningCoordinates[i], out Resource resource)
                || !resource.TryPeekMachineHarvestOutput(out int outputItemId, out _)
                || outputItemId < 0)
            {
                continue;
            }

            outputItemIds.Add(outputItemId);
            foundAny = true;
        }

        return foundAny;
    }

    protected override void TryStartNextCraft()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null
            || !TryResolveNextMiningResource(
                out Resource resource,
                out int resourceIndex,
                out int outputItemId,
                out int outputCount,
                -1,
                -1,
                true))
        {
            return;
        }

        if (!TryEnsureCraftStartEnergy(installedDefinition))
        {
            return;
        }

        SetActiveMiningResourceSelection(resource, resourceIndex);
        BeginActiveCraft(-1, outputItemId, outputCount, installedDefinition);
    }

    protected override bool ShouldShowWorldEnergyGauge(ItemDefinition installedDefinition)
    {
        if (installedDefinition != null
            && installedDefinition.useEnergyType == ItemDefinition.EnergyType.Electricity)
        {
            return false;
        }

        return base.ShouldShowWorldEnergyGauge(installedDefinition);
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null)
        {
            return "No machine";
        }

        if (IsWaitingForOutput)
        {
            return "Output full";
        }

        if (IsActiveCraftRunning)
        {
            if (!HasOperationalEnergyAvailable(installedDefinition))
            {
                return "No energy";
            }

            isProducing = true;
            return "Working";
        }

        if (!TryResolveNextMiningResource(
                out _,
                out _,
                out int outputItemId,
                out int outputCount,
                -1,
                -1,
                false)
            || outputItemId < 0
            || outputCount <= 0)
        {
            return "No resource";
        }

        if (!TryResolveNextMiningResource(
                out _,
                out _,
                out _,
                out _,
                -1,
                -1,
                true))
        {
            return "Output full";
        }

        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            return "No energy";
        }

        isProducing = true;
        return "Working";
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

        if (!TryResolveActiveMiningResource(
                ActiveOutputItemId,
                ActiveOutputCount,
                out Resource resource,
                out int resourceIndex)
            || !resource.TryPeekMachineHarvestOutput(out int outputItemId, out int outputCount))
        {
            ClearActiveMiningResourceSelection();
            ClearActiveCraft();
            return false;
        }

        if (outputItemId != ActiveOutputItemId || outputCount != ActiveOutputCount)
        {
            ClearActiveMiningResourceSelection();
            ClearActiveCraft();
            return false;
        }

        Vector3 startWorldPosition = resource.FocusPoint;
        if (!resource.TryHarvestForMachine(out int harvestedItemId, out int harvestedCount))
        {
            ClearActiveMiningResourceSelection();
            ClearActiveCraft();
            return false;
        }

        if (!TryEmitOutputItemsToBlock(outputBlock, harvestedItemId, harvestedCount, startWorldPosition))
        {
            ClearActiveMiningResourceSelection();
            ClearActiveCraft();
            return false;
        }

        AdvanceMiningResourceCursor(resourceIndex, resource);
        ClearActiveMiningResourceSelection();
        ClearActiveCraft();
        return true;
    }

    protected override bool AppendOutputItemIds(ISet<int> outputItemIds)
    {
        bool foundAny = base.AppendOutputItemIds(outputItemIds);
        if (outputItemIds == null)
        {
            return foundAny;
        }

        return AppendMiningResourceOutputItemIds(outputItemIds) || foundAny;
    }

    public override bool TryGetObjectInfoOutput(
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity,
        out bool displayZeroCountItem)
    {
        if (base.TryGetObjectInfoOutput(
                out outputItemId,
                out outputAreaCount,
                out outputAreaCapacity,
                out displayZeroCountItem))
        {
            return true;
        }

        outputItemId = -1;
        outputAreaCount = 0;
        outputAreaCapacity = 0;
        displayZeroCountItem = false;

        if (!TryResolveNextMiningResource(
                out _,
                out _,
                out outputItemId,
                out int outputCount,
                -1,
                -1,
                false)
            || outputItemId < 0)
        {
            return false;
        }

        displayZeroCountItem = true;
        return TryResolveObjectInfoOutputAreaCounts(
            outputItemId,
            Mathf.Max(1, outputCount),
            out outputAreaCount,
            out outputAreaCapacity);
    }

    public bool TryGetObjectInfoResourceReserves(out int reserves)
    {
        reserves = 0;
        if (!TryCollectMiningResources(miningResourceCandidates))
        {
            return false;
        }

        for (int i = 0; i < miningResourceCandidates.Count; i++)
        {
            Resource resource = miningResourceCandidates[i];
            if (resource != null)
            {
                reserves += resource.RemainingHarvestOutputCount;
            }
        }

        return true;
    }

    private bool TryResolveNextMiningResource(
        out Resource resource,
        out int resourceIndex,
        out int outputItemId,
        out int outputCount,
        int requiredOutputItemId,
        int requiredOutputCount,
        bool requireOutputBlock)
    {
        resource = null;
        resourceIndex = -1;
        outputItemId = -1;
        outputCount = 0;

        if (!TryCollectMiningResources(miningResourceCandidates))
        {
            return false;
        }

        int candidateCount = miningResourceCandidates.Count;
        int startIndex = NormalizeMiningResourceIndex(nextMiningResourceIndex, candidateCount);
        for (int offset = 0; offset < candidateCount; offset++)
        {
            int candidateIndex = (startIndex + offset) % candidateCount;
            Resource candidate = miningResourceCandidates[candidateIndex];
            if (candidate == null
                || !candidate.TryPeekMachineHarvestOutput(out int candidateOutputItemId, out int candidateOutputCount)
                || candidateOutputItemId < 0
                || candidateOutputCount <= 0)
            {
                continue;
            }

            if (requiredOutputItemId >= 0 && candidateOutputItemId != requiredOutputItemId)
            {
                continue;
            }

            if (requiredOutputCount > 0 && candidateOutputCount != requiredOutputCount)
            {
                continue;
            }

            if (requireOutputBlock && !TryResolveOutputBlock(candidateOutputItemId, candidateOutputCount, out _))
            {
                continue;
            }

            resource = candidate;
            resourceIndex = candidateIndex;
            outputItemId = candidateOutputItemId;
            outputCount = candidateOutputCount;
            return true;
        }

        return false;
    }

    private bool TryResolveActiveMiningResource(
        int requiredOutputItemId,
        int requiredOutputCount,
        out Resource resource,
        out int resourceIndex)
    {
        resource = null;
        resourceIndex = -1;

        if (IsSelectableMiningResource(activeMiningResource)
            && activeMiningResource.TryPeekMachineHarvestOutput(out int activeOutputItemId, out int activeOutputCount)
            && activeOutputItemId == requiredOutputItemId
            && activeOutputCount == requiredOutputCount)
        {
            resource = activeMiningResource;
            resourceIndex = activeMiningResourceIndex;
            return true;
        }

        return TryResolveNextMiningResource(
            out resource,
            out resourceIndex,
            out _,
            out _,
            requiredOutputItemId,
            requiredOutputCount,
            false);
    }

    private bool AppendMiningResourceOutputItemIds(ISet<int> outputItemIds)
    {
        if (outputItemIds == null || !TryCollectMiningResources(miningResourceCandidates))
        {
            return false;
        }

        bool foundAny = false;
        for (int i = 0; i < miningResourceCandidates.Count; i++)
        {
            Resource resource = miningResourceCandidates[i];
            if (resource == null
                || !resource.TryPeekMachineHarvestOutput(out int outputItemId, out _)
                || outputItemId < 0)
            {
                continue;
            }

            outputItemIds.Add(outputItemId);
            foundAny = true;
        }

        return foundAny;
    }

    private bool TryCollectMiningResources(List<Resource> resources)
    {
        if (resources == null)
        {
            return false;
        }

        resources.Clear();
        IReadOnlyList<Vector2Int> coordinates = RuntimeOccupiedCoordinates;
        if (coordinates != null && coordinates.Count > 0)
        {
            for (int i = 0; i < coordinates.Count; i++)
            {
                TryAddMiningResourceAtCoordinate(coordinates[i], resources);
            }
        }
        else
        {
            TryAddMiningResourceAtCoordinate(RuntimeAnchorCoordinate, resources);
        }

        return resources.Count > 0;
    }

    private bool TryAddMiningResourceAtCoordinate(Vector2Int coordinate, List<Resource> resources)
    {
        if (resources == null
            || !TryGetLoadedBlock(coordinate, out Block block)
            || !TryResolveMiningResource(block, out Resource resource)
            || resources.Contains(resource))
        {
            return false;
        }

        resources.Add(resource);
        return true;
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

    private static bool IsSelectableMiningResource(Resource resource)
    {
        return resource != null && resource.CanHarvest && resource.gameObject.activeInHierarchy;
    }

    private void SetActiveMiningResourceSelection(Resource resource, int resourceIndex)
    {
        activeMiningResource = resource;
        activeMiningResourceIndex = resourceIndex;
    }

    private void ClearActiveMiningResourceSelection()
    {
        activeMiningResource = null;
        activeMiningResourceIndex = -1;
    }

    private void AdvanceMiningResourceCursor(int consumedResourceIndex, Resource consumedResource)
    {
        if (consumedResourceIndex < 0)
        {
            return;
        }

        nextMiningResourceIndex = consumedResource != null && consumedResource.CanHarvest
            ? consumedResourceIndex + 1
            : consumedResourceIndex;
    }

    private static int NormalizeMiningResourceIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int normalized = index % count;
        return normalized < 0 ? normalized + count : normalized;
    }

    protected override bool ShouldPlayWorkAnimation()
    {
        if (IsWaitingForOutput)
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (IsActiveCraftRunning)
        {
            return HasOperationalEnergyAvailable(installedDefinition);
        }

        return IsWorkAnimatorStateActive && CanContinueWorkAnimation(installedDefinition);
    }

    private bool CanContinueWorkAnimation(ItemDefinition installedDefinition)
    {
        return installedDefinition != null
               && HasOperationalEnergyAvailable(installedDefinition)
               && TryResolveNextMiningResource(
                   out _,
                   out _,
                   out int outputItemId,
                   out int outputCount,
                   -1,
                   -1,
                   true)
               && outputItemId >= 0
               && outputCount > 0;
    }

}
