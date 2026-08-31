using System.Collections.Generic;
using UnityEngine;

public class OilDrillingMachine : InputOutputModule
{
    private const int DefaultOilItemId = 4;
    private const float FluidEpsilon = 0.0001f;
    private const int MaxHarvestsPerTick = 32;

    [SerializeField]
    private ItemDefinition oilDefinition;
    [SerializeField, Min(0)]
    private int fallbackOilItemId = DefaultOilItemId;
    [SerializeField]
    private Transform pumpjackBeam;
    [SerializeField]
    private Transform pumpjackCrank;
    [SerializeField]
    private Transform pumpjackRod;
    [SerializeField, Min(0.01f)]
    private float pumpjackCyclesPerSecond = 0.35f;
    [SerializeField, Min(0f)]
    private float pumpjackBeamSwingDegrees = 7f;
    [SerializeField, Min(0f)]
    private float pumpjackRodStroke = 0.08f;

    private float productionProgressLiters;
    private Resource cachedOilResource;
    private bool isExtracting;
    private bool hasPumpjackVisual;
    private float pumpjackPhase;
    private Quaternion pumpjackBeamBaseRotation;
    private Quaternion pumpjackCrankBaseRotation;
    private Vector3 pumpjackRodBasePosition;

    public float OilLitersPerSecond
    {
        get
        {
            ItemDefinition installedDefinition = ResolveInstalledDefinition();
            return installedDefinition != null ? installedDefinition.FluidOutputLitersPerSecond : 0f;
        }
    }

    public static Vector2Int ResolveOilTargetCoordinate(Vector2Int anchorCoordinate, int quarterTurns)
    {
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        Vector2Int targetOffset = normalizedQuarterTurns switch
        {
            1 => Vector2Int.down,
            2 => Vector2Int.left,
            3 => Vector2Int.up,
            _ => Vector2Int.right
        };
        return anchorCoordinate + targetOffset;
    }

    public bool TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond)
    {
        outputItemId = ResolveOilItemId();
        litersPerSecond = OilLitersPerSecond;
        return outputItemId >= 0;
    }

    public bool TryGetObjectInfoResourceReserves(out int reservesLiters)
    {
        reservesLiters = TryResolveOilResource(out Resource resource)
            ? resource.RemainingMachineHarvestOutputCount
            : 0;
        return true;
    }

    public override PersistentState CapturePersistentState()
    {
        PersistentState state = base.CapturePersistentState();
        state.oilDrillingProgressLiters = Mathf.Max(0f, productionProgressLiters);
        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        base.ApplyPersistentState(state);
        productionProgressLiters = state != null
            ? Mathf.Max(0f, state.oilDrillingProgressLiters)
            : 0f;
    }

    public override void PrepareForPool()
    {
        RestorePumpjackVisual();
        productionProgressLiters = 0f;
        cachedOilResource = null;
        isExtracting = false;
        base.PrepareForPool();
        ApplyAnimatorPlayback(false);
    }

    public override void ManagedUpdateTick(float deltaTime)
    {
        if (!Application.isPlaying || deltaTime <= 0f)
        {
            return;
        }

        isExtracting = ExtractOil(deltaTime);
        base.ManagedUpdateTick(deltaTime);
        ApplyAnimatorPlayback(isExtracting);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        isExtracting = false;
        CapturePumpjackVisual();
        ApplyAnimatorPlayback(false);
    }

    protected override void OnDisable()
    {
        isExtracting = false;
        cachedOilResource = null;
        RestorePumpjackVisual();
        base.OnDisable();
        ApplyAnimatorPlayback(false);
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        productionProgressLiters = 0f;
        cachedOilResource = null;
        isExtracting = false;
        RestorePumpjackVisual();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !isExtracting || !hasPumpjackVisual)
        {
            return;
        }

        float playbackSpeed = Mathf.Max(0f, OperationalAnimationSpeedRatio);
        pumpjackPhase = Mathf.Repeat(
            pumpjackPhase + Time.deltaTime * pumpjackCyclesPerSecond * playbackSpeed * Mathf.PI * 2f,
            Mathf.PI * 2f);
        float stroke = Mathf.Sin(pumpjackPhase);

        if (pumpjackBeam != null)
        {
            pumpjackBeam.localRotation = pumpjackBeamBaseRotation
                * Quaternion.AngleAxis(stroke * pumpjackBeamSwingDegrees, Vector3.forward);
        }

        if (pumpjackCrank != null)
        {
            pumpjackCrank.localRotation = pumpjackCrankBaseRotation
                * Quaternion.AngleAxis(pumpjackPhase * Mathf.Rad2Deg, Vector3.forward);
        }

        if (pumpjackRod != null)
        {
            pumpjackRod.localPosition = pumpjackRodBasePosition + Vector3.up * (stroke * pumpjackRodStroke);
        }
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        return true;
    }

    protected override bool ShouldAutoPullFluidFromConnectedStorage()
    {
        return false;
    }

    protected override bool AppendOutputItemIds(ISet<int> outputItemIds)
    {
        bool foundAny = base.AppendOutputItemIds(outputItemIds);
        if (outputItemIds == null)
        {
            return foundAny;
        }

        int oilItemId = ResolveOilItemId();
        if (oilItemId < 0)
        {
            return foundAny;
        }

        outputItemIds.Add(oilItemId);
        return true;
    }

    public override bool TryGetObjectInfoOutput(
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity,
        out bool displayZeroCountItem)
    {
        outputItemId = ResolveOilItemId();
        outputAreaCount = 0;
        outputAreaCapacity = 0;
        displayZeroCountItem = outputItemId >= 0;
        return outputItemId >= 0;
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null)
        {
            return "No machine";
        }

        if (!TryResolveOilResource(out Resource resource) || !resource.CanHarvest)
        {
            return "Oil depleted";
        }

        if (!HasOilOutputSpace(resource))
        {
            return "Output full";
        }

        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            return "No energy";
        }

        isProducing = OilLitersPerSecond > 0f;
        return isProducing ? "Working" : "No production rate";
    }

    protected override bool ShouldPlayWorkAnimation()
    {
        return isExtracting;
    }

    protected override bool ShouldAnimateVirtualizedEnergyConsumption()
    {
        return true;
    }

    private bool ExtractOil(float deltaTime)
    {
        if (!TryResolveOilResource(out Resource resource)
            || !resource.CanHarvest
            || OilLitersPerSecond <= FluidEpsilon
            || !HasOilOutputSpace(resource))
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null)
        {
            return false;
        }

        float requestedEnergy = ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition) * deltaTime;
        if (!TryConsumeOperatingEnergy(deltaTime, out float consumedEnergy))
        {
            return false;
        }

        float energySupplyRatio = requestedEnergy > FluidEpsilon
            ? Mathf.Clamp01(consumedEnergy / requestedEnergy)
            : 1f;
        if (energySupplyRatio <= FluidEpsilon)
        {
            return false;
        }

        productionProgressLiters += OilLitersPerSecond * deltaTime * energySupplyRatio;
        FlushCompletedOil(resource);
        return true;
    }

    private void FlushCompletedOil(Resource resource)
    {
        int harvestCount = 0;
        while (resource != null
               && resource.CanHarvest
               && harvestCount < MaxHarvestsPerTick
               && resource.TryPeekMachineHarvestOutput(out int outputItemId, out int outputCount)
               && outputCount > 0
               && productionProgressLiters + FluidEpsilon >= outputCount
               && TryGetFluidOutputAvailableLiters(outputItemId, outputCount, out float availableLiters)
               && availableLiters + FluidEpsilon >= outputCount)
        {
            if (!resource.TryHarvestForMachine(out int harvestedItemId, out int harvestedCount)
                || harvestedItemId != outputItemId
                || harvestedCount != outputCount)
            {
                return;
            }

            if (!TryEmitFluidOutputToConnectedStorages(
                    harvestedItemId,
                    harvestedCount,
                    GetStoredFluidTemperatureCelsius(harvestedItemId),
                    out float acceptedLiters)
                || acceptedLiters + FluidEpsilon < harvestedCount)
            {
                return;
            }

            productionProgressLiters = Mathf.Max(0f, productionProgressLiters - acceptedLiters);
            harvestCount++;
        }
    }

    private bool HasOilOutputSpace(Resource resource)
    {
        int oilItemId = ResolveOilItemId();
        int minimumOutputLiters = resource != null ? Mathf.Max(1, resource.GetCount) : 1;
        return oilItemId >= 0
               && TryGetFluidOutputAvailableLiters(oilItemId, minimumOutputLiters, out float availableLiters)
               && availableLiters + FluidEpsilon >= minimumOutputLiters;
    }

    private bool TryResolveOilResource(out Resource resource)
    {
        resource = null;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            cachedOilResource = null;
            return false;
        }

        Vector2Int targetCoordinate = ResolveOilTargetCoordinate(anchorCoordinate, quarterTurns);
        if (cachedOilResource != null
            && cachedOilResource.OwningBlock != null
            && cachedOilResource.OwningBlock.Coordinate == targetCoordinate
            && cachedOilResource.PlacementCategory == ResourceDefinition.PlacementCategory.Oil)
        {
            resource = cachedOilResource;
            return true;
        }

        cachedOilResource = null;
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null
            || !terrain.TryGetLoadedBlock(targetCoordinate, out Block block)
            || block == null
            || block.Resource == null
            || block.Resource.PlacementCategory != ResourceDefinition.PlacementCategory.Oil)
        {
            return false;
        }

        cachedOilResource = block.Resource;
        resource = cachedOilResource;
        return true;
    }

    private int ResolveOilItemId()
    {
        if (oilDefinition != null && oilDefinition.id >= 0)
        {
            return oilDefinition.id;
        }

        if (TryResolveOilResource(out Resource resource)
            && resource.TryPeekMachineHarvestOutput(out int resourceOutputItemId, out _))
        {
            return resourceOutputItemId;
        }

        return fallbackOilItemId >= 0 ? fallbackOilItemId : DefaultOilItemId;
    }

    private void ApplyAnimatorPlayback(bool isWorking)
    {
        Animator targetAnimator = ResolveInstallationAnimator();
        if (targetAnimator != null)
        {
            targetAnimator.speed = isWorking ? Mathf.Max(0f, OperationalAnimationSpeedRatio) : 0f;
        }
    }

    private void CapturePumpjackVisual()
    {
        hasPumpjackVisual = pumpjackBeam != null || pumpjackCrank != null || pumpjackRod != null;
        pumpjackPhase = 0f;
        if (pumpjackBeam != null)
        {
            pumpjackBeamBaseRotation = pumpjackBeam.localRotation;
        }

        if (pumpjackCrank != null)
        {
            pumpjackCrankBaseRotation = pumpjackCrank.localRotation;
        }

        if (pumpjackRod != null)
        {
            pumpjackRodBasePosition = pumpjackRod.localPosition;
        }
    }

    private void RestorePumpjackVisual()
    {
        if (!hasPumpjackVisual)
        {
            return;
        }

        if (pumpjackBeam != null)
        {
            pumpjackBeam.localRotation = pumpjackBeamBaseRotation;
        }

        if (pumpjackCrank != null)
        {
            pumpjackCrank.localRotation = pumpjackCrankBaseRotation;
        }

        if (pumpjackRod != null)
        {
            pumpjackRod.localPosition = pumpjackRodBasePosition;
        }

        pumpjackPhase = 0f;
    }
}
