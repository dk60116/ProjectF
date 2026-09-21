using System.Collections.Generic;
using ProjectF.Simulation;
using UnityEngine;

public class Boiler : InputOutputModule, IFacilityFlowAdapter, IFacilityFlowStateOwner
{
    private const float FluidEpsilon = 0.0001f;
    private const float MinWaterTemperatureCelsius = 0f;
    private const float MaxWaterTemperatureCelsiusValue = 100f;
    private const float PassiveCoolingRateScale = 0.2f;

    internal override bool TryGetRuntimePassiveFluidPass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        return false;
    }

    [SerializeField]
    private List<InstallationFacingDirection> localPipeConnectionDirections =
        new List<InstallationFacingDirection> { InstallationFacingDirection.PositiveZ };

    private FacilityFlowEntityHandle flowEntityHandle;
    private FacilityFlowBatch fallbackFlowBatch;

    private float waterTemperatureCelsius
    {
        get => EnsureFlowState().WaterTemperatureCelsius;
        set => EnsureFlowState().WaterTemperatureCelsius = value;
    }

    private bool preserveSteamReadyTemperatureForMakeupWater
    {
        get => EnsureFlowState().PreserveSteamReadyTemperatureForMakeupWater;
        set => EnsureFlowState().PreserveSteamReadyTemperatureForMakeupWater = value;
    }

    private long availableSteamOutputUnits
    {
        get => EnsureFlowState().AvailableSteamOutputUnits;
        set => EnsureFlowState().AvailableSteamOutputUnits = value;
    }

    private long steamOutputBudgetUpdatedTick
    {
        get => EnsureFlowState().SteamOutputBudgetUpdatedTick;
        set => EnsureFlowState().SteamOutputBudgetUpdatedTick = value;
    }

    public IReadOnlyList<InstallationFacingDirection> LocalPipeConnectionDirections => localPipeConnectionDirections;
    public float WaterTemperatureCelsius => Mathf.Clamp(waterTemperatureCelsius, MinWaterTemperatureCelsius, MaxWaterTemperatureCelsiusValue);
    public float MaxWaterTemperatureCelsius => MaxWaterTemperatureCelsiusValue;
    public float ObjectInfoBoilerTemperatureFillAmount => Mathf.Clamp01(WaterTemperatureCelsius / MaxWaterTemperatureCelsiusValue);
    public Color ObjectInfoBoilerTemperatureGaugeFillColor => new Color(1f, 0.45f, 0.05f, 1f);

    public bool TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond)
    {
        outputItemId = -1;
        litersPerSecond = 0f;
        if (!TryGetBoilerFluidRecipe(
                out _,
                out _,
                out outputItemId,
                out float outputLitersPerSecond,
                out _,
                out _))
        {
            return false;
        }

        litersPerSecond = Mathf.Max(0f, outputLitersPerSecond);
        return outputItemId >= 0;
    }

    public override float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId)
    {
        return isActiveAndEnabled
               && TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond)
               && outputItemId == fluidItemId
            ? Mathf.Max(0f, litersPerSecond)
            : 0f;
    }

    public override float GetStoredFluidTemperatureCelsius(int fluidItemId)
    {
        if (IsBoilerOutputFluidItem(fluidItemId))
        {
            return MaxWaterTemperatureCelsiusValue;
        }

        return IsBoilerInputFluidItem(fluidItemId)
            ? Mathf.Clamp(
                StoredFluidLiters > FluidEpsilon && WaterTemperatureCelsius > FluidEpsilon
                    ? WaterTemperatureCelsius
                    : MapClimate.CurrentWaterTemperatureCelsius,
                MinWaterTemperatureCelsius,
                MaxWaterTemperatureCelsiusValue)
            : base.GetStoredFluidTemperatureCelsius(fluidItemId);
    }

    public override void ApplyManagedUpdateTick()
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime))
        {
            return;
        }

        fallbackFlowBatch ??= new FacilityFlowBatch(1);
        fallbackFlowBatch.Begin();
        int index = fallbackFlowBatch.ReserveSlot();
        CaptureFacilityFlow(
            fallbackFlowBatch,
            index,
            deltaTime,
            MapObjectTickManager.CurrentSimulationTick);
        fallbackFlowBatch.PlanAll();
        ApplyFacilityFlowCommit(fallbackFlowBatch, index, deltaTime);
    }

    public override PersistentState CapturePersistentState()
    {
        PersistentState state = base.CapturePersistentState();
        state.boilerWaterTemperatureCelsius = WaterTemperatureCelsius;
        state.boilerSteamLiterAccumulator = 0f;
        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        base.ApplyPersistentState(state);
        if (state == null)
        {
            return;
        }

        waterTemperatureCelsius = Mathf.Clamp(
            state.boilerWaterTemperatureCelsius,
            MinWaterTemperatureCelsius,
            MaxWaterTemperatureCelsiusValue);
        preserveSteamReadyTemperatureForMakeupWater = false;
        availableSteamOutputUnits = 0L;
        steamOutputBudgetUpdatedTick = -1L;
    }

    protected override void TryStartNextCraft()
    {
        // Boiler steam is continuous: water heats to 100C, then Output Count liters/sec
        // are emitted as steam. Do not restart the generic one-shot craft loop here.
    }

    protected override bool ShouldAutoPullFluidFromConnectedStorage()
    {
        return false;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        if (!CanStoreFluid
            || !TryGetBoilerFluidRecipe(
                out int inputItemId,
                out _,
                out int outputItemId,
                out _,
                out _,
                out _))
        {
            return false;
        }

        if (HasFluidStorageSpace
            && !IsWaterStorageFull(inputItemId)
            && HasConnectedFluidSource(inputItemId))
        {
            return true;
        }

        if (StoredFluidLiters <= FluidEpsilon || !CanProvideFluidItem(inputItemId))
        {
            return false;
        }

        if (WaterTemperatureCelsius > ResolveIdleWaterTemperatureCelsius() + FluidEpsilon)
        {
            return true;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            return false;
        }

        if (WaterTemperatureCelsius + FluidEpsilon < MaxWaterTemperatureCelsiusValue)
        {
            return true;
        }

        return TryGetFluidOutputAvailableLiters(
            outputItemId,
            FluidEpsilon * 2f,
            out _);
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null)
        {
            return "No machine";
        }

        if (!HasRuntimeOutputCoordinates)
        {
            return "No output area";
        }

        if (!TryGetBoilerFluidRecipe(
                out int inputItemId,
                out _,
                out int outputItemId,
                out _,
                out bool hasRecipe,
                out bool blockedByTargetFilter))
        {
            return hasRecipe && blockedByTargetFilter ? "No target" : "No recipe";
        }

        if (!CanStoreFluid
            || StoredFluidLiters <= FluidEpsilon
            || !CanProvideFluidItem(inputItemId))
        {
            return "No water";
        }

        if (WaterTemperatureCelsius + FluidEpsilon < MaxWaterTemperatureCelsiusValue)
        {
            if (!HasOperationalEnergyAvailable(installedDefinition))
            {
                return WaterTemperatureCelsius > ResolveIdleWaterTemperatureCelsius() + FluidEpsilon
                    ? $"Cooling {Mathf.FloorToInt(WaterTemperatureCelsius)}C"
                    : "No energy";
            }

            if (!IsWaterStorageFull(inputItemId))
            {
                return "Filling water";
            }

            isProducing = true;
            return $"Heating {Mathf.FloorToInt(WaterTemperatureCelsius)}C";
        }

        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            return WaterTemperatureCelsius > ResolveIdleWaterTemperatureCelsius() + FluidEpsilon
                ? $"Cooling {Mathf.FloorToInt(WaterTemperatureCelsius)}C"
                : "No energy";
        }

        if (!TryResolveFluidOutputStorage(outputItemId, FluidEpsilon, out _))
        {
            return "Output full";
        }

        isProducing = true;
        return "Steaming";
    }

    public override bool TryGetObjectInfoOutput(
        out int outputItemId,
        out int outputAreaCount,
        out int outputAreaCapacity,
        out bool displayZeroCountItem)
    {
        outputItemId = -1;
        outputAreaCount = 0;
        outputAreaCapacity = 0;
        displayZeroCountItem = false;

        if (!TryGetBoilerFluidRecipe(
                out _,
                out _,
                out outputItemId,
                out float outputLitersPerSecond,
                out _,
                out _))
        {
            return false;
        }

        displayZeroCountItem = true;
        return TryResolveObjectInfoOutputAreaCounts(
            outputItemId,
            Mathf.Max(1, Mathf.CeilToInt(outputLitersPerSecond)),
            out outputAreaCount,
            out outputAreaCapacity);
    }

    public bool HasPipeConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        if (direction == Vector2Int.zero)
        {
            return false;
        }

        if (localPipeConnectionDirections == null || localPipeConnectionDirections.Count <= 0)
        {
            return TryResolveDirection(
                       rotation,
                       InstallationFacingDirection.PositiveZ,
                       out Vector2Int defaultDirection)
                   && defaultDirection == direction;
        }

        for (int i = 0; i < localPipeConnectionDirections.Count; i++)
        {
            if (TryResolveDirection(rotation, localPipeConnectionDirections[i], out Vector2Int resolvedDirection)
                && resolvedDirection == direction)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetPipePassDirectionAtCoordinate(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out Vector2Int pipePassDirection)
    {
        pipePassDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        return TryGetRectGridBlockTypeAtCoordinate(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   coordinate,
                   out RectGridBlockType blockType)
               && blockType == RectGridBlockType.PipeInput
               && TryGetNearestRectGridObjectDirection(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   coordinate,
                   out pipePassDirection)
               && pipePassDirection != Vector2Int.zero;
    }

    public bool TryGetRuntimeWaterPass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchor, out int quarterTurns)
            || !TryGetPipePassDirectionAtCoordinate(this, anchor, quarterTurns, coordinate, out Vector2Int inwardDirection))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            // Only connect the water PipeInput cells. The steam output remains separate.
            if (placement.blockType == RectGridBlockType.PipeInput
                && TryGetRectGridPlacementCoordinate(this, anchor, quarterTurns, placement, out Vector2Int candidate)
                && candidate != coordinate)
            {
                otherCoordinate = candidate;
                externalDirection = -inwardDirection;
                return true;
            }
        }

        return false;
    }

    public bool PipePassAtCoordinateMatchesDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int pipePassCoordinate,
        Vector2Int targetDirection)
    {
        return targetDirection != Vector2Int.zero
               && TryGetPipePassDirectionAtCoordinate(
                   footprintSource,
                   anchorCoordinate,
                   quarterTurns,
                   pipePassCoordinate,
                   out Vector2Int pipePassDirection)
               && pipePassDirection == targetDirection;
    }

    public bool HasPipePassFacingDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int targetDirection)
    {
        if (targetDirection == Vector2Int.zero)
        {
            return false;
        }

        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        if (placements == null || placements.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.PipeInput
                || !TryGetRectGridPlacementCoordinate(
                    anchorSource,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int pipePassCoordinate)
                || !TryGetPipePassDirectionAtCoordinate(
                    anchorSource,
                    anchorCoordinate,
                    quarterTurns,
                    pipePassCoordinate,
                    out Vector2Int pipePassDirection))
            {
                continue;
            }

            if (pipePassDirection == targetDirection)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetOutputDirectionAtCoordinate(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int outputCoordinate,
        out Vector2Int outputDirection)
    {
        outputDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        if (!TryGetRectGridBlockTypeAtCoordinate(
                anchorSource,
                anchorCoordinate,
                quarterTurns,
                outputCoordinate,
                out RectGridBlockType blockType)
            || !IsOutputBlockType(blockType)
            || !TryGetNearestRectGridObjectDirection(
                anchorSource,
                anchorCoordinate,
                quarterTurns,
                outputCoordinate,
                out Vector2Int outputObjectDirection))
        {
            return false;
        }

        outputDirection = -outputObjectDirection;
        return outputDirection != Vector2Int.zero;
    }

    public bool TryGetOutputCoordinateAndDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        out Vector2Int outputCoordinate,
        out Vector2Int outputDirection)
    {
        outputCoordinate = Vector2Int.zero;
        outputDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        if (placements == null || placements.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (!IsOutputBlockType(placement.blockType)
                || !TryGetRectGridPlacementCoordinate(
                    anchorSource,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int candidateCoordinate)
                || !TryGetOutputDirectionAtCoordinate(
                    anchorSource,
                    anchorCoordinate,
                    quarterTurns,
                    candidateCoordinate,
                    out Vector2Int candidateDirection))
            {
                continue;
            }

            outputCoordinate = candidateCoordinate;
            outputDirection = candidateDirection;
            return true;
        }

        return false;
    }

    protected override bool TryCompleteActiveCraft()
    {
        if (!IsActiveCraftRunning || ActiveOutputItemId < 0 || ActiveOutputCount <= 0)
        {
            ClearActiveCraft();
            return false;
        }

        if (IsFluidItemId(ActiveOutputItemId))
        {
            if (!TryEmitFluidOutputToConnectedStorage(ActiveOutputItemId, ActiveOutputCount))
            {
                return false;
            }

            ClearActiveCraft();
            return true;
        }

        return base.TryCompleteActiveCraft();
    }

    public void CaptureFacilityFlow(
        FacilityFlowBatch batch,
        int index,
        float deltaTime,
        long simulationTick)
    {
        ref BoilerFlowState state = ref EnsureFlowState();
        int inputItemId = -1;
        float inputLitersPerSecond = 0f;
        int outputItemId = -1;
        float outputLitersPerSecond = 0f;
        bool valid = deltaTime > 0f
                     && TryGetBoilerFluidRecipe(
                         out inputItemId,
                         out inputLitersPerSecond,
                         out outputItemId,
                         out outputLitersPerSecond,
                         out _,
                         out _);
        float effectiveOutputRate = valid
            ? outputLitersPerSecond * ResolveFluidOutputTransportRetention(outputItemId)
            : 0f;
        bool canPull = valid
                       && inputItemId >= 0
                       && CanStoreFluid
                       && HasFluidStorageSpace
                       && !IsWaterStorageFull(inputItemId);
        batch.ConfigureBoiler(
            index,
            inputItemId,
            inputLitersPerSecond,
            outputItemId,
            outputLitersPerSecond,
            effectiveOutputRate,
            ConnectedFluidStorageTransferLitersPerSecond,
            deltaTime,
            simulationTick,
            valid,
            canPull,
            StoredFluidLiters,
            Mathf.Clamp(
                state.WaterTemperatureCelsius,
                MinWaterTemperatureCelsius,
                MaxWaterTemperatureCelsiusValue),
            state.AvailableSteamOutputUnits,
            state.SteamOutputBudgetUpdatedTick);
    }

    public void ApplyFacilityFlow(FacilityFlowBatch batch, int index)
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime))
        {
            return;
        }

        ApplyFacilityFlowCommit(batch, index, deltaTime);
    }

    private void ApplyFacilityFlowCommit(FacilityFlowBatch batch, int index, float deltaTime)
    {
        ApplyPlannedBaseModuleTick(deltaTime);
        if (Application.isPlaying)
        {
            UpdateBoilerFluidProcess(batch, index);
        }
        ref BoilerFlowState state = ref EnsureFlowState();
        state.WaterTemperatureCelsius = batch.GetBoilerTemperature(index);
        state.AvailableSteamOutputUnits = batch.GetBoilerBudgetUnits(index);
        state.SteamOutputBudgetUpdatedTick = batch.GetBoilerBudgetUpdatedTick(index);
    }

    private ref BoilerFlowState EnsureFlowState()
    {
        if (!FacilityFlowStateWorld.ContainsBoiler(flowEntityHandle))
        {
            flowEntityHandle = FacilityFlowStateWorld.CreateBoiler();
        }

        return ref FacilityFlowStateWorld.GetBoiler(flowEntityHandle);
    }

    void IFacilityFlowStateOwner.ReleaseFacilityFlowState()
    {
        FacilityFlowStateWorld.ReleaseBoiler(ref flowEntityHandle);
    }

    void IFacilityFlowStateOwner.EnsureFacilityFlowState()
    {
        EnsureFlowState();
    }

    private void UpdateBoilerFluidProcess(FacilityFlowBatch batch, int index)
    {
        if (!batch.IsBoilerValid(index)) return;

        int inputItemId = batch.GetBoilerInputItemId(index);
        int outputItemId = batch.GetBoilerOutputItemId(index);
        float requestedPullLiters = batch.GetBoilerRequestedPullLiters(index);
        if (requestedPullLiters > FluidEpsilon)
        {
            TryPullFluidFromConnectedStorage(inputItemId, requestedPullLiters, out _);
        }

        if (!NormalizeWaterTemperatureForStoredFluid(inputItemId))
        {
            batch.SetBoilerTemperature(index, MinWaterTemperatureCelsius);
            preserveSteamReadyTemperatureForMakeupWater = false;
            return;
        }
        batch.SetBoilerTemperature(index, WaterTemperatureCelsius);
        batch.UpdateBoilerStoredWater(index, StoredFluidLiters);

        preserveSteamReadyTemperatureForMakeupWater = false;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            TryCoolStoredWater(batch, index);
            return;
        }

        if (WaterTemperatureCelsius + FluidEpsilon < MaxWaterTemperatureCelsiusValue)
        {
            if (!TryHeatWater(batch, index, inputItemId, installedDefinition))
            {
                TryCoolStoredWater(batch, index);
            }

            // Heating and steam generation are separate operating ticks. Running
            // both here consumed the configured boiler energy rate twice whenever
            // the water reached 100C during this tick.
            return;
        }

        if (!TryGenerateSteam(
                batch,
                index,
                inputItemId,
                outputItemId,
                installedDefinition))
        {
            // A steam-ready boiler cools only while it cannot operate. Cooling
            // before every generation tick forced it below 100C and made the
            // full-water startup condition repeatedly interrupt steady output.
            TryCoolStoredWater(batch, index);
        }
    }

    private bool TryHeatWater(
        FacilityFlowBatch batch,
        int index,
        int inputItemId,
        ItemDefinition installedDefinition)
    {
        if (!IsWaterStorageFull(inputItemId)
            || !TryConsumeBoilerOperatingEnergy(
                batch.GetBoilerDeltaTime(index),
                installedDefinition,
                out float consumedEnergy))
        {
            return false;
        }

        bool requiresEnergy = RequiresOperationalEnergy(installedDefinition);
        float completeEnergy = requiresEnergy
            ? ResolveCompleteEnergy(installedDefinition, CraftDurationSeconds)
            : 0f;
        if (!batch.HeatBoiler(
                index,
                consumedEnergy,
                completeEnergy,
                requiresEnergy,
                CraftDurationSeconds))
        {
            return false;
        }

        waterTemperatureCelsius = batch.GetBoilerTemperature(index);
        SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
        return true;
    }

    private bool TryCoolStoredWater(FacilityFlowBatch batch, int index)
    {
        float targetTemperature = ResolveIdleWaterTemperatureCelsius();
        if (!batch.CoolBoiler(
                index,
                targetTemperature,
                CraftDurationSeconds,
                PassiveCoolingRateScale))
        {
            return false;
        }

        waterTemperatureCelsius = batch.GetBoilerTemperature(index);
        SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
        return true;
    }

    private bool TryGenerateSteam(
        FacilityFlowBatch batch,
        int index,
        int inputItemId,
        int outputItemId,
        ItemDefinition installedDefinition)
    {
        float inputLitersPerSecond = batch.GetBoilerInputRate(index);
        float outputLitersPerSecond = batch.GetBoilerOutputRate(index);
        if (StoredFluidLiters <= FluidEpsilon
            || !CanProvideFluidItem(inputItemId)
            || inputLitersPerSecond <= 0
            || outputLitersPerSecond <= 0)
        {
            return false;
        }

        // Steam is a per-second flow, not an internal backlog. If the connected
        // engines cannot accept this tick's steam, the boiler throttles instead
        // of saving unsent steam and dumping it later when more engines connect.
        float waterLitersPerSteamLiter = (float)inputLitersPerSecond / outputLitersPerSecond;
        batch.UpdateBoilerStoredWater(index, StoredFluidLiters);
        batch.PrepareBoilerOutput(index);
        float maxLitersToEmit = batch.GetBoilerMaximumOutputLiters(index);
        if (maxLitersToEmit <= FluidEpsilon)
        {
            return true;
        }

        if (!TryGetFluidOutputAvailableLiters(outputItemId, maxLitersToEmit, out float litersToEmit)
            || litersToEmit <= FluidEpsilon)
        {
            return false;
        }

        if (!TryConsumeBoilerOperatingEnergy(
                batch.GetBoilerDeltaTime(index),
                installedDefinition,
                out _))
        {
            return false;
        }

        float waterLitersToConsume = litersToEmit * waterLitersPerSteamLiter;
        if (!TryConsumeFluidLiters(inputItemId, waterLitersToConsume, out float consumedLiters)
            || consumedLiters + FluidEpsilon < waterLitersToConsume)
        {
            return false;
        }

        if (!TryEmitFluidOutputToConnectedStorages(
                outputItemId,
                litersToEmit,
                MaxWaterTemperatureCelsiusValue,
                out float acceptedLiters)
            || acceptedLiters <= FluidEpsilon)
        {
            TryAddFluidLiters(inputItemId, waterLitersToConsume, WaterTemperatureCelsius, out _);
            SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
            return false;
        }

        float rejectedLiters = litersToEmit - Mathf.Max(0f, acceptedLiters);
        if (rejectedLiters > FluidEpsilon)
        {
            TryAddFluidLiters(inputItemId, rejectedLiters * waterLitersPerSteamLiter, WaterTemperatureCelsius, out _);
            SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
        }

        batch.CommitBoilerOutput(index, acceptedLiters);
        preserveSteamReadyTemperatureForMakeupWater = true;
        return true;
    }

    private bool TryConsumeBoilerOperatingEnergy(
        float deltaTime,
        ItemDefinition installedDefinition,
        out float consumedEnergy)
    {
        consumedEnergy = 0f;
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        return TryConsumeOperatingEnergy(deltaTime, out consumedEnergy)
               && consumedEnergy > FluidEpsilon;
    }

    private static float ResolveIdleWaterTemperatureCelsius()
    {
        return Mathf.Clamp(
            MapClimate.CurrentWaterTemperatureCelsius,
            MinWaterTemperatureCelsius,
            MaxWaterTemperatureCelsiusValue);
    }

    private bool NormalizeWaterTemperatureForStoredFluid(int inputItemId)
    {
        if (!CanStoreFluid
            || StoredFluidLiters <= FluidEpsilon
            || !CanProvideFluidItem(inputItemId))
        {
            waterTemperatureCelsius = MinWaterTemperatureCelsius;
            preserveSteamReadyTemperatureForMakeupWater = false;
            return false;
        }

        if (waterTemperatureCelsius <= FluidEpsilon)
        {
            waterTemperatureCelsius = GetStoredFluidTemperatureCelsius(inputItemId);
        }

        waterTemperatureCelsius = Mathf.Clamp(
            waterTemperatureCelsius,
            MinWaterTemperatureCelsius,
            MaxWaterTemperatureCelsiusValue);
        SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
        return true;
    }

    protected override void OnStoredFluidAccepted(
        int fluidItemId,
        float previousStoredLiters,
        float acceptedLiters,
        float incomingTemperatureCelsius)
    {
        bool wasSteamReady = WaterTemperatureCelsius + FluidEpsilon >= MaxWaterTemperatureCelsiusValue;
        if (acceptedLiters <= FluidEpsilon || !IsBoilerInputFluidItem(fluidItemId))
        {
            base.OnStoredFluidAccepted(
                fluidItemId,
                previousStoredLiters,
                acceptedLiters,
                incomingTemperatureCelsius);
            return;
        }

        SetStoredFluidTemperatureCelsius(WaterTemperatureCelsius);
        base.OnStoredFluidAccepted(
            fluidItemId,
            previousStoredLiters,
            acceptedLiters,
            incomingTemperatureCelsius);
        waterTemperatureCelsius = Mathf.Clamp(
            base.GetStoredFluidTemperatureCelsius(fluidItemId),
            MinWaterTemperatureCelsius,
            MaxWaterTemperatureCelsiusValue);

        // Preserve 100C only for makeup water immediately following real steam
        // emission. Do not keep an idle/no-energy boiler hot just because it was
        // once steam-ready.
        if (wasSteamReady && preserveSteamReadyTemperatureForMakeupWater)
        {
            waterTemperatureCelsius = MaxWaterTemperatureCelsiusValue;
            SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
            return;
        }

        if (WaterTemperatureCelsius + FluidEpsilon < MaxWaterTemperatureCelsiusValue)
        {
        }
    }

    private bool IsBoilerInputFluidItem(int fluidItemId)
    {
        return fluidItemId >= 0
               && TryGetBoilerFluidRecipe(
                   out int inputItemId,
                   out _,
                   out _,
                   out _,
                   out _,
                   out _)
               && inputItemId == fluidItemId;
    }

    private bool IsBoilerOutputFluidItem(int fluidItemId)
    {
        return fluidItemId >= 0
               && TryGetBoilerFluidRecipe(
                   out _,
                   out _,
                   out int outputItemId,
                   out _,
                   out _,
                   out _)
               && outputItemId == fluidItemId;
    }

    private bool IsWaterStorageFull(int inputItemId)
    {
        float capacity = FluidStorageCapacityLiters;
        return capacity > FluidEpsilon
               && StoredFluidLiters + FluidEpsilon >= capacity
               && CanProvideFluidItem(inputItemId);
    }

    private bool TryGetBoilerFluidRecipe(
        out int inputItemId,
        out float inputLiters,
        out int outputItemId,
        out float outputLitersPerSecond,
        out bool hasRecipe,
        out bool blockedByTargetFilter)
    {
        inputItemId = -1;
        inputLiters = 0f;
        outputItemId = -1;
        outputLitersPerSecond = 0f;
        hasRecipe = false;
        blockedByTargetFilter = false;

        int recipeCount = Mathf.Min(InputList.Count, OutputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetFluidRecipe(
                    recipeIndex,
                    out int candidateInputItemId,
                    out float candidateInputLiters,
                    out int candidateOutputItemId,
                    out float candidateOutputAmount)
                || !IsFluidItemId(candidateInputItemId)
                || !IsFluidItemId(candidateOutputItemId))
            {
                continue;
            }

            hasRecipe = true;
            if (!IsRecipeOutputAvailable(candidateOutputItemId))
            {
                blockedByTargetFilter = true;
                continue;
            }

            inputItemId = candidateInputItemId;
            inputLiters = candidateInputLiters;
            outputItemId = candidateOutputItemId;
            outputLitersPerSecond = candidateOutputAmount;
            return true;
        }

        return false;
    }

    private bool TryGetFluidRecipe(
        int recipeIndex,
        out int inputItemId,
        out float inputLiters,
        out int outputItemId,
        out float outputAmount)
    {
        inputItemId = -1;
        inputLiters = 0f;
        outputItemId = -1;
        outputAmount = 0f;

        IReadOnlyList<ItemIoEntry> inputs = InputList;
        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (recipeIndex < 0 || recipeIndex >= inputs.Count || recipeIndex >= outputs.Count)
        {
            return false;
        }

        ItemIoEntry inputEntry = inputs[recipeIndex];
        ItemIoEntry outputEntry = outputs[recipeIndex];
        inputItemId = inputEntry.itemDefinition != null ? inputEntry.itemDefinition.id : -1;
        outputItemId = outputEntry.itemDefinition != null ? outputEntry.itemDefinition.id : -1;
        if (inputItemId < 0 || outputItemId < 0)
        {
            return false;
        }

        inputLiters = inputEntry.ResolvedAmount;
        outputAmount = outputEntry.ResolvedAmount;
        return true;
    }

    private static bool TryResolveDirection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        out Vector2Int resolvedDirection)
    {
        return TryResolveCardinalDirection(rotation * FacingDirectionToVector(localDirection), out resolvedDirection);
    }

    private static bool TryResolveCardinalDirection(Vector3 directionVector, out Vector2Int resolvedDirection)
    {
        resolvedDirection = Vector2Int.zero;
        directionVector.y = 0f;
        if (directionVector.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        directionVector.Normalize();
        if (Mathf.Abs(directionVector.x) >= Mathf.Abs(directionVector.z))
        {
            resolvedDirection = new Vector2Int(directionVector.x >= 0f ? 1 : -1, 0);
        }
        else
        {
            resolvedDirection = new Vector2Int(0, directionVector.z >= 0f ? 1 : -1);
        }

        return true;
    }

    private static Vector3 FacingDirectionToVector(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return Vector3.right;
            case InstallationFacingDirection.NegativeX:
                return Vector3.left;
            case InstallationFacingDirection.NegativeZ:
                return Vector3.back;
            default:
                return Vector3.forward;
        }
    }
}
