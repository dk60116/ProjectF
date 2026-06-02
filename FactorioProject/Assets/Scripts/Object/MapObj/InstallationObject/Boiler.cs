using System.Collections.Generic;
using UnityEngine;

public class Boiler : InputOutputModule
{
    private const float FluidEpsilon = 0.0001f;
    private const float MinWaterTemperatureCelsius = 0f;
    private const float MaxWaterTemperatureCelsiusValue = 100f;
    private const float PassiveCoolingRateScale = 0.2f;

    [SerializeField]
    private List<InstallationFacingDirection> localPipeConnectionDirections =
        new List<InstallationFacingDirection> { InstallationFacingDirection.PositiveZ };

    private float waterTemperatureCelsius;
    private float steamLiterAccumulator;
    private bool preserveSteamReadyTemperatureForMakeupWater;

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
                out int outputLitersPerSecond,
                out _,
                out _))
        {
            return false;
        }

        litersPerSecond = Mathf.Max(0f, outputLitersPerSecond);
        return outputItemId >= 0;
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

    public override void ManagedUpdateTick(float deltaTime)
    {
        base.ManagedUpdateTick(deltaTime);
        if (!Application.isPlaying)
        {
            return;
        }

        UpdateBoilerFluidProcess(deltaTime);
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
        steamLiterAccumulator = 0f;
        preserveSteamReadyTemperatureForMakeupWater = false;
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        waterTemperatureCelsius = MinWaterTemperatureCelsius;
        steamLiterAccumulator = 0f;
        preserveSteamReadyTemperatureForMakeupWater = false;
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
                out int outputLitersPerSecond,
                out _,
                out _))
        {
            return false;
        }

        displayZeroCountItem = true;
        return TryResolveObjectInfoOutputAreaCounts(
            outputItemId,
            Mathf.Max(1, outputLitersPerSecond),
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

    private void UpdateBoilerFluidProcess(float deltaTime)
    {
        if (deltaTime <= 0f
            || !TryGetBoilerFluidRecipe(
                out int inputItemId,
                out int inputLitersPerSecond,
                out int outputItemId,
                out int outputLitersPerSecond,
                out _,
                out _))
        {
            return;
        }

        TryPullBoilerInputWater(deltaTime, inputItemId);

        if (!NormalizeWaterTemperatureForStoredFluid(inputItemId))
        {
            preserveSteamReadyTemperatureForMakeupWater = false;
            return;
        }

        preserveSteamReadyTemperatureForMakeupWater = false;
        TryCoolStoredWater(deltaTime);

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            return;
        }

        if (WaterTemperatureCelsius + FluidEpsilon < MaxWaterTemperatureCelsiusValue)
        {
            if (!TryHeatWater(deltaTime, inputItemId, installedDefinition)
                || WaterTemperatureCelsius + FluidEpsilon < MaxWaterTemperatureCelsiusValue)
            {
                return;
            }
        }

        TryGenerateSteam(
            deltaTime,
            inputItemId,
            inputLitersPerSecond,
            outputItemId,
            outputLitersPerSecond,
            installedDefinition);
    }

    private bool TryHeatWater(float deltaTime, int inputItemId, ItemDefinition installedDefinition)
    {
        if (!IsWaterStorageFull(inputItemId)
            || !TryConsumeBoilerOperatingEnergy(deltaTime, installedDefinition, out float consumedEnergy))
        {
            return false;
        }

        float temperatureGain = ResolveTemperatureGain(deltaTime, consumedEnergy, installedDefinition);
        if (temperatureGain <= FluidEpsilon)
        {
            return false;
        }

        waterTemperatureCelsius = Mathf.Min(
            MaxWaterTemperatureCelsiusValue,
            WaterTemperatureCelsius + temperatureGain);
        SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
        return true;
    }

    private bool TryCoolStoredWater(float deltaTime)
    {
        float targetTemperature = ResolveIdleWaterTemperatureCelsius();
        float currentTemperature = WaterTemperatureCelsius;
        if (deltaTime <= 0f || currentTemperature <= targetTemperature + FluidEpsilon)
        {
            return false;
        }

        float temperatureDrop = ResolveTemperatureDrop(deltaTime);
        if (temperatureDrop <= FluidEpsilon)
        {
            return false;
        }

        waterTemperatureCelsius = Mathf.MoveTowards(
            currentTemperature,
            targetTemperature,
            temperatureDrop);
        SetStoredFluidTemperatureCelsius(waterTemperatureCelsius);
        steamLiterAccumulator = 0f;
        return true;
    }

    private bool TryGenerateSteam(
        float deltaTime,
        int inputItemId,
        int inputLitersPerSecond,
        int outputItemId,
        int outputLitersPerSecond,
        ItemDefinition installedDefinition)
    {
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
        steamLiterAccumulator = 0f;
        float requestedLiters = outputLitersPerSecond * Mathf.Max(0f, deltaTime);
        float waterLitersPerSteamLiter = (float)inputLitersPerSecond / outputLitersPerSecond;
        float maxSteamLitersFromWater = StoredFluidLiters / waterLitersPerSteamLiter;
        float maxLitersToEmit = Mathf.Min(requestedLiters, maxSteamLitersFromWater);
        if (maxLitersToEmit <= FluidEpsilon)
        {
            return true;
        }

        if (!TryResolveSteamOutputStorage(
                outputItemId,
                maxLitersToEmit,
                out InstallationObject targetStorage,
                out float litersToEmit)
            || targetStorage == null
            || litersToEmit <= FluidEpsilon)
        {
            return false;
        }

        if (!TryConsumeBoilerOperatingEnergy(deltaTime, installedDefinition, out _))
        {
            return false;
        }

        float waterLitersToConsume = litersToEmit * waterLitersPerSteamLiter;
        if (!TryConsumeFluidLiters(inputItemId, waterLitersToConsume, out float consumedLiters)
            || consumedLiters + FluidEpsilon < waterLitersToConsume)
        {
            return false;
        }

        if (!targetStorage.TryAddFluidLiters(
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

        steamLiterAccumulator = 0f;
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

    private float ResolveTemperatureGain(float deltaTime, float consumedEnergy, ItemDefinition installedDefinition)
    {
        if (RequiresOperationalEnergy(installedDefinition))
        {
            float completeEnergy = ResolveCompleteEnergy(installedDefinition, CraftDurationSeconds);
            return completeEnergy > FluidEpsilon
                ? (Mathf.Max(0f, consumedEnergy) / completeEnergy) * MaxWaterTemperatureCelsiusValue
                : 0f;
        }

        return (Mathf.Max(0f, deltaTime) / CraftDurationSeconds) * MaxWaterTemperatureCelsiusValue;
    }

    private float ResolveTemperatureDrop(float deltaTime)
    {
        return (Mathf.Max(0f, deltaTime) / CraftDurationSeconds)
               * MaxWaterTemperatureCelsiusValue
               * PassiveCoolingRateScale;
    }

    private static float ResolveIdleWaterTemperatureCelsius()
    {
        return Mathf.Clamp(
            MapClimate.CurrentWaterTemperatureCelsius,
            MinWaterTemperatureCelsius,
            MaxWaterTemperatureCelsiusValue);
    }

    private bool TryResolveSteamOutputStorage(
        int outputItemId,
        float maxLiters,
        out InstallationObject targetStorage,
        out float resolvedLiters)
    {
        targetStorage = null;
        resolvedLiters = 0f;
        if (maxLiters <= FluidEpsilon)
        {
            return false;
        }

        if (!TryResolveFluidOutputStorage(outputItemId, FluidEpsilon, out targetStorage))
        {
            return false;
        }

        resolvedLiters = Mathf.Min(maxLiters, targetStorage.AvailableFluidStorageLiters);
        return resolvedLiters > FluidEpsilon;
    }

    private void TryPullBoilerInputWater(float deltaTime, int inputItemId)
    {
        if (deltaTime <= 0f
            || inputItemId < 0
            || !CanStoreFluid
            || !HasFluidStorageSpace
            || IsWaterStorageFull(inputItemId))
        {
            return;
        }

        TryPullFluidFromConnectedStorage(
            inputItemId,
            ConnectedFluidStorageTransferLitersPerSecond * deltaTime,
            out _);
    }

    private bool NormalizeWaterTemperatureForStoredFluid(int inputItemId)
    {
        if (!CanStoreFluid
            || StoredFluidLiters <= FluidEpsilon
            || !CanProvideFluidItem(inputItemId))
        {
            waterTemperatureCelsius = MinWaterTemperatureCelsius;
            steamLiterAccumulator = 0f;
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
        steamLiterAccumulator = Mathf.Max(0f, steamLiterAccumulator);
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
            steamLiterAccumulator = 0f;
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
        out int inputLiters,
        out int outputItemId,
        out int outputLitersPerSecond,
        out bool hasRecipe,
        out bool blockedByTargetFilter)
    {
        inputItemId = -1;
        inputLiters = 0;
        outputItemId = -1;
        outputLitersPerSecond = 0;
        hasRecipe = false;
        blockedByTargetFilter = false;

        int recipeCount = Mathf.Min(InputList.Count, OutputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetFluidRecipe(
                    recipeIndex,
                    out int candidateInputItemId,
                    out int candidateInputLiters,
                    out int candidateOutputItemId,
                    out int candidateOutputCount)
                || !IsFluidItemId(candidateInputItemId)
                || !IsFluidItemId(candidateOutputItemId))
            {
                continue;
            }

            hasRecipe = true;
            if (!IsRecipeOutputAllowedByItemFilter(candidateOutputItemId))
            {
                blockedByTargetFilter = true;
                continue;
            }

            inputItemId = candidateInputItemId;
            inputLiters = candidateInputLiters;
            outputItemId = candidateOutputItemId;
            outputLitersPerSecond = Mathf.Max(1, candidateOutputCount);
            return true;
        }

        return false;
    }

    private bool TryGetFluidRecipe(
        int recipeIndex,
        out int inputItemId,
        out int inputLiters,
        out int outputItemId,
        out int outputCount)
    {
        inputItemId = -1;
        inputLiters = 0;
        outputItemId = -1;
        outputCount = 0;

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

        inputLiters = Mathf.Max(1, inputEntry.count);
        outputCount = Mathf.Max(1, outputEntry.count);
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
