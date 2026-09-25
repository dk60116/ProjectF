using System.Collections.Generic;
using ProjectF.Simulation;
using UnityEngine;

public class SteamGenerator : InputOutputModule, IFacilityFlowAdapter, IFacilityFlowStateOwner
{
    private const float FluidEpsilon = 0.0001f;

    [SerializeField]
    private InstallationFacingDirection localPipeAreaConnectionDirection = InstallationFacingDirection.PositiveX;

    private FacilityFlowEntityHandle flowEntityHandle;
    private bool generationVisualActive;
    private FacilityFlowBatch fallbackFlowBatch;

    [SerializeField]
    private Transform wheelTF;
    [SerializeField, Min(0f)]
    private float wheelRotationDegreesPerSecond = 180f;

    public InstallationFacingDirection LocalPipeAreaConnectionDirection => localPipeAreaConnectionDirection;

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

    public override void ApplyPersistentState(PersistentState state)
    {
        base.ApplyPersistentState(state);
        // Generation is a derived runtime state. Restoring the previous frame's
        // flag can publish power before this generator has consumed any steam.
        StopGenerationVisuals(true);
        if (isActiveAndEnabled)
        {
            UtilityPole.NotifyElectricPowerSourceStateChanged(this);
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        UtilityPole.NotifyElectricPowerSourceStateChanged(this);
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        StopGenerationVisuals(true);
        base.OnDisable();
        UtilityPole.NotifyElectricPowerSourceStateChanged(this);
    }

    protected override bool ShouldAutoPullFluidFromConnectedStorage()
    {
        return false;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        if (!TryGetSteamInputRecipe(out int inputItemId, out _))
        {
            return false;
        }

        // Pipe topology controls which producer may fill this storage. Once
        // steam has been accepted, generation is driven by the stored resource
        // itself so a transient/stale topology cache cannot strand valid steam.
        if (StoredFluidLiters > FluidEpsilon)
        {
            return StoredFluidItemId < 0 || CanProvideFluidItem(inputItemId);
        }

        // Steam is pushed by a boiler into the directed generator chain. An empty
        // generator sleeps until TryAddFluidLiters wakes it instead of searching
        // the bidirectional fluid graph and pulling from a neighbouring row.
        return false;
    }

    public bool TryGetPipeAreaConnectionDirection(Quaternion rotation, out Vector2Int direction)
    {
        return TryResolveDirection(rotation, localPipeAreaConnectionDirection, out direction);
    }

    public bool TryGetBodyDirectionFromCenter(
        MapObject footprintSource,
        int quarterTurns,
        out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        if (anchorSource == null)
        {
            return false;
        }

        int sizeX = Mathf.Max(1, anchorSource.Status.mapSizeX);
        int sizeY = Mathf.Max(1, anchorSource.Status.mapSizeY);
        Vector2Int centerCell = anchorSource.PlacementCenterCell;
        centerCell = new Vector2Int(
            Mathf.Clamp(centerCell.x, 0, sizeX - 1),
            Mathf.Clamp(centerCell.y, 0, sizeY - 1));

        Vector2Int adjacentOffset = Vector2Int.zero;
        int bestDistance = int.MaxValue;
        for (int y = 0; y < sizeY; y++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                Vector2Int offset = new Vector2Int(x - centerCell.x, y - centerCell.y);
                int distance = Mathf.Abs(offset.x) + Mathf.Abs(offset.y);
                if (distance <= 0 || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                adjacentOffset = offset;
            }
        }

        if (bestDistance != 1)
        {
            return false;
        }

        direction = RotateRectGridOffset(adjacentOffset, quarterTurns);
        return direction != Vector2Int.zero;
    }

    public bool TryGetInputDirectionAtCoordinate(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int inputCoordinate,
        out Vector2Int inputDirection)
    {
        inputDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        return TryGetRectGridBlockTypeAtCoordinate(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   inputCoordinate,
                   out RectGridBlockType blockType)
               && IsInputItemBlockType(blockType)
               && TryGetNearestRectGridObjectDirection(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   inputCoordinate,
                   out inputDirection)
               && inputDirection != Vector2Int.zero;
    }

    public bool TryGetInputCoordinateAndDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        out Vector2Int inputCoordinate,
        out Vector2Int inputDirection)
    {
        inputCoordinate = Vector2Int.zero;
        inputDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        if (placements == null || placements.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (!IsInputItemBlockType(placement.blockType)
                || !TryGetRectGridPlacementCoordinate(
                    anchorSource,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int candidateCoordinate)
                || !TryGetInputDirectionAtCoordinate(
                    anchorSource,
                    anchorCoordinate,
                    quarterTurns,
                    candidateCoordinate,
                    out Vector2Int candidateDirection))
            {
                continue;
            }

            inputCoordinate = candidateCoordinate;
            inputDirection = candidateDirection;
            return true;
        }

        return false;
    }

    public bool TryGetRuntimePipePassCoordinates(
        out Vector2Int inputCoordinate,
        out Vector2Int tailCoordinate)
    {
        inputCoordinate = Vector2Int.zero;
        tailCoordinate = Vector2Int.zero;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        bool foundInput = false;
        bool foundTail = false;
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            bool isPipeInput = IsInputItemBlockType(placement.blockType)
                               && AllowsPipeAreaInteraction(placement.blockType);
            bool isPipePass = placement.blockType == RectGridBlockType.PipeInput;
            if ((!isPipeInput && !isPipePass)
                || !TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int coordinate))
            {
                continue;
            }

            if (isPipeInput && !foundInput)
            {
                inputCoordinate = coordinate;
                foundInput = true;
            }
            else if (isPipePass && !foundTail)
            {
                tailCoordinate = coordinate;
                foundTail = true;
            }

            if (foundInput && foundTail)
            {
                break;
            }
        }

        return foundInput && foundTail && inputCoordinate != tailCoordinate;
    }

    public bool TryGetRuntimeSteamPass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !TryGetRuntimePipePassCoordinates(
                out Vector2Int inputCoordinate,
                out Vector2Int tailCoordinate))
        {
            return false;
        }

        if (coordinate == inputCoordinate
            && TryGetInputDirectionAtCoordinate(
                this,
                anchorCoordinate,
                quarterTurns,
                inputCoordinate,
                out Vector2Int inputDirection))
        {
            otherCoordinate = tailCoordinate;
            externalDirection = -inputDirection;
            return externalDirection != Vector2Int.zero;
        }

        if (coordinate == tailCoordinate
            && TryGetPipePassTailDirectionAtCoordinate(
                this,
                anchorCoordinate,
                quarterTurns,
                tailCoordinate,
                out Vector2Int tailDirection))
        {
            otherCoordinate = inputCoordinate;
            externalDirection = tailDirection;
            return externalDirection != Vector2Int.zero;
        }

        return false;
    }

    internal static bool TryResolveSteamPassPipeConnectionDirection(
        Vector2Int pipeCoordinate,
        Vector2Int endpointCoordinate,
        Vector2Int externalDirection,
        out Vector2Int pipeConnectionDirection)
    {
        pipeConnectionDirection = endpointCoordinate - pipeCoordinate;
        if (pipeConnectionDirection == Vector2Int.zero)
        {
            pipeConnectionDirection = -externalDirection;
        }

        return externalDirection != Vector2Int.zero
               && pipeConnectionDirection == -externalDirection;
    }

    public bool CanReceiveSteamFromDirectedPortAtRuntime(
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection)
    {
        if (flowDirection == Vector2Int.zero
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !TryGetInputCoordinateAndDirection(
                this,
                anchorCoordinate,
                quarterTurns,
                out Vector2Int inputCoordinate,
                out Vector2Int inputDirection)
            || inputDirection != flowDirection)
        {
            return false;
        }

        TryGetBodyDirectionFromCenter(this, quarterTurns, out Vector2Int bodyDirection);
        return IsDirectedSteamPortConnection(
            sourcePortCoordinate,
            flowDirection,
            anchorCoordinate,
            inputCoordinate,
            inputDirection,
            bodyDirection);
    }

    internal static bool IsDirectedSteamPortConnection(
        Vector2Int sourcePortCoordinate,
        Vector2Int flowDirection,
        Vector2Int generatorAnchorCoordinate,
        Vector2Int inputCoordinate,
        Vector2Int inputDirection,
        Vector2Int bodyDirection)
    {
        if (flowDirection == Vector2Int.zero || inputDirection != flowDirection)
        {
            return false;
        }

        Vector2Int inputDelta = inputCoordinate - sourcePortCoordinate;
        if (inputDelta == Vector2Int.zero || inputDelta == flowDirection)
        {
            return true;
        }

        // The dense serial placement overlaps the upstream tail with this
        // generator's anchor and its input with the upstream tail body cell.
        return sourcePortCoordinate == generatorAnchorCoordinate
               && inputCoordinate == sourcePortCoordinate - flowDirection
               && bodyDirection == flowDirection;
    }

    public bool TryGetRuntimePipePassTail(
        out Vector2Int tailCoordinate,
        out Vector2Int tailDirection)
    {
        tailCoordinate = Vector2Int.zero;
        tailDirection = Vector2Int.zero;
        return TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
               && TryGetRuntimePipePassCoordinates(out _, out tailCoordinate)
               && TryGetPipePassTailDirectionAtCoordinate(
                   this,
                   anchorCoordinate,
                   quarterTurns,
                   tailCoordinate,
                   out tailDirection);
    }

    public bool TryGetPipePassTailDirectionAtCoordinate(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int pipePassCoordinate,
        out Vector2Int tailDirection)
    {
        tailDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        if (!TryGetRectGridBlockTypeAtCoordinate(
                anchorSource,
                anchorCoordinate,
                quarterTurns,
                pipePassCoordinate,
                out RectGridBlockType blockType)
            || blockType != RectGridBlockType.PipeInput
            || !TryGetNearestRectGridObjectDirection(
                anchorSource,
                anchorCoordinate,
                quarterTurns,
                pipePassCoordinate,
                out Vector2Int pipePassObjectDirection))
        {
            return false;
        }

        Vector2Int candidateTailDirection = -pipePassObjectDirection;
        if (candidateTailDirection == Vector2Int.zero
            || !TryGetBodyDirectionFromCenter(anchorSource, quarterTurns, out Vector2Int bodyDirection)
            || candidateTailDirection != bodyDirection)
        {
            return false;
        }

        tailDirection = candidateTailDirection;
        return true;
    }

    public bool TryGetObjectInfoOutputRate(out int outputItemId, out float wattsPerSecond)
    {
        outputItemId = -1;
        wattsPerSecond = 0f;
        if (!TryGetSteamGenerationRecipe(
                out _,
                out _,
                out outputItemId,
                out int outputWattsPerSecond)
            || outputItemId < 0)
        {
            return false;
        }

        wattsPerSecond = Mathf.Max(0f, outputWattsPerSecond);
        return true;
    }

    public bool TryGetAvailableElectricOutputRate(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        if (!TryGetObjectInfoOutputRate(out _, out float outputWattsPerSecond)
            || outputWattsPerSecond <= FluidEpsilon
            || !TryGetSteamInputRecipe(out int inputItemId, out float inputLitersPerSecond)
            || inputLitersPerSecond <= 0
            || !CanStoreFluid)
        {
            return false;
        }

        if (StoredFluidItemId >= 0 && !CanProvideFluidItem(inputItemId))
        {
            return false;
        }

        float outputScale = GenerationOutputScale;
        if (outputScale <= FluidEpsilon)
        {
            return false;
        }

        wattsPerSecond = outputWattsPerSecond * outputScale;
        return true;
    }

    public bool TryGetObjectInfoElectricOutputState(
        out bool hasUtilityPoleConnection,
        out bool isOutputting)
    {
        hasUtilityPoleConnection = UtilityPole.IsConnectedToElectricNetwork(this);
        isOutputting = hasUtilityPoleConnection
                       && TryGetAvailableElectricOutputRate(out float availableWatts)
                       && availableWatts > FluidEpsilon;
        return TryGetObjectInfoOutputRate(out _, out _);
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

        if (!TryGetObjectInfoOutputRate(out outputItemId, out float wattsPerSecond))
        {
            return false;
        }

        displayZeroCountItem = true;
        return TryResolveObjectInfoOutputAreaCounts(
            outputItemId,
            Mathf.Max(1, Mathf.CeilToInt(wattsPerSecond)),
            out outputAreaCount,
            out outputAreaCapacity);
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = false;
        if (!TryGetSteamInputRecipe(out int inputItemId, out float inputLitersPerSecond))
        {
            return base.ResolveObjectInfoStatus(out isProducing);
        }

        if (!CanStoreFluid)
        {
            return "No fluid storage";
        }

        if (StoredFluidItemId >= 0 && !CanProvideFluidItem(inputItemId))
        {
            return "Wrong fluid";
        }

        if (!UtilityPole.IsConnectedToElectricNetwork(this))
        {
            return "No utility pole";
        }

        // A disconnected generator may finish consuming steam that was already
        // accepted while the directed connection was valid. Only report the
        // missing connection once no usable steam remains.
        if (StoredFluidLiters <= FluidEpsilon
            && !InputOutputModule.IsInDirectedBoilerSteamChain(this))
        {
            return "No boiler steam connection";
        }

        if (!IsGenerationActive)
        {
            return "No steam";
        }

        isProducing = true;
        return "Generating";
    }

    public void CaptureFacilityFlow(
        FacilityFlowBatch batch,
        int index,
        float deltaTime,
        long simulationTick)
    {
        EnsureFlowState();
        bool hasRecipe = TryGetSteamInputRecipe(out int inputItemId, out float inputLitersPerSecond)
                         && inputLitersPerSecond > 0;
        bool valid = hasRecipe
                     && (StoredFluidItemId < 0 || CanProvideFluidItem(inputItemId));
        batch.ConfigureSteamGenerator(
            index,
            inputItemId,
            inputLitersPerSecond,
            deltaTime,
            valid,
            StoredFluidLiters);
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
        if (deltaTime <= 0f)
        {
            return;
        }

        int inputItemId = batch.GetSteamInputItemId(index);
        float requestedLiters = batch.GetSteamRequestedLiters(index);
        if (!batch.IsSteamGeneratorValid(index)
            || inputItemId < 0
            || requestedLiters <= FluidEpsilon
            || (StoredFluidItemId >= 0 && !CanProvideFluidItem(inputItemId)))
        {
            SetGenerationOutputScale(0f);
            return;
        }

        // A shared steam line can deliver less than one generator's full
        // per-tick demand. Consume that partial supply and publish proportional
        // power instead of leaving sub-tick steam stranded forever.
        float availableLiters = Mathf.Min(StoredFluidLiters, requestedLiters);
        if (availableLiters <= FluidEpsilon
            || !TryConsumeFluidLiters(inputItemId, availableLiters, out float consumedLiters))
        {
            SetGenerationOutputScale(0f);
            return;
        }

        SetGenerationOutputScale(
            ResolveGenerationOutputScale(consumedLiters, requestedLiters));
    }

    public override void PrepareForPool()
    {
        StopGenerationVisuals(true);
        base.PrepareForPool();
    }

    internal static float ResolveGenerationOutputScale(
        float consumedLiters,
        float requestedLiters)
    {
        return requestedLiters > FluidEpsilon
            ? Mathf.Clamp01(Mathf.Max(0f, consumedLiters) / requestedLiters)
            : 0f;
    }

    private void SetGenerationOutputScale(float outputScale)
    {
        outputScale = Mathf.Clamp01(outputScale);
        bool active = outputScale > FluidEpsilon;
        ref SteamGeneratorFlowState state = ref EnsureFlowState();
        bool stateChanged = state.IsGenerating != active
                            || Mathf.Abs(state.OutputScale - outputScale) > FluidEpsilon;
        bool visualChanged = generationVisualActive != active;
        if (!stateChanged && !visualChanged)
        {
            return;
        }

        state.IsGenerating = active;
        state.OutputScale = outputScale;
        if (visualChanged)
        {
            generationVisualActive = active;
            MarkManagedRuntimeVisualsDirty();
        }
        if (stateChanged)
        {
            UtilityPole.NotifyElectricPowerSourceStateChanged(this);
        }
    }

    private bool IsGenerationActive =>
        GenerationOutputScale > FluidEpsilon;

    private float GenerationOutputScale
    {
        get
        {
            if (!FacilityFlowStateWorld.ContainsSteamGenerator(flowEntityHandle))
            {
                return 0f;
            }

            ref SteamGeneratorFlowState state =
                ref FacilityFlowStateWorld.GetSteamGenerator(flowEntityHandle);
            return state.IsGenerating ? Mathf.Clamp01(state.OutputScale) : 0f;
        }
    }

    private ref SteamGeneratorFlowState EnsureFlowState()
    {
        if (!FacilityFlowStateWorld.ContainsSteamGenerator(flowEntityHandle))
        {
            flowEntityHandle = FacilityFlowStateWorld.CreateSteamGenerator();
        }

        return ref FacilityFlowStateWorld.GetSteamGenerator(flowEntityHandle);
    }

    void IFacilityFlowStateOwner.ReleaseFacilityFlowState()
    {
        FacilityFlowStateWorld.ReleaseSteamGenerator(ref flowEntityHandle);
    }

    void IFacilityFlowStateOwner.EnsureFacilityFlowState()
    {
        EnsureFlowState();
    }

    private bool TryGetSteamInputRecipe(out int inputItemId, out float inputLitersPerSecond)
    {
        return TryGetSteamGenerationRecipe(
            out inputItemId,
            out inputLitersPerSecond,
            out _,
            out _);
    }

    private bool TryGetSteamGenerationRecipe(
        out int inputItemId,
        out float inputLitersPerSecond,
        out int outputItemId,
        out int outputWattsPerSecond)
    {
        inputItemId = -1;
        inputLitersPerSecond = 0f;
        outputItemId = -1;
        outputWattsPerSecond = 0;

        IReadOnlyList<ItemIoEntry> inputs = InputList;
        if (inputs == null || inputs.Count <= 0)
        {
            return false;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        for (int i = 0; i < inputs.Count; i++)
        {
            ItemIoEntry input = inputs[i];
            int candidateInputItemId = input.itemDefinition != null ? input.itemDefinition.id : -1;
            if (candidateInputItemId < 0 || !IsFluidItemId(candidateInputItemId))
            {
                continue;
            }

            if (outputs != null && i < outputs.Count)
            {
                ItemIoEntry output = outputs[i];
                ItemDefinition outputDefinition = output.itemDefinition;
                outputItemId = outputDefinition != null ? outputDefinition.id : -1;
                outputWattsPerSecond = outputItemId >= 0
                    ? Mathf.RoundToInt(ItemDefinition.ResolveElectricOutputWatts(outputDefinition, output.ResolvedAmount))
                    : 0;
                if (outputItemId >= 0 && !IsRecipeOutputAvailable(outputItemId))
                {
                    continue;
                }
            }

            inputItemId = candidateInputItemId;
            inputLitersPerSecond = input.ResolvedAmount;
            return true;
        }

        return false;
    }

    protected override bool RequiresManagedVisualUpdate =>
        base.RequiresManagedVisualUpdate || generationVisualActive;

    protected override void TickManagedVisuals(float deltaTime)
    {
        base.TickManagedVisuals(deltaTime);
        if (!generationVisualActive)
            return;

        if (wheelTF != null && deltaTime > 0f && wheelRotationDegreesPerSecond > 0f)
        {
            wheelTF.Rotate(0f, 0f, -wheelRotationDegreesPerSecond * deltaTime, Space.Self);
        }
    }

    protected override void OnManagedRuntimeVisualsFlushed()
    {
        SetVisualParticleActive(particleEffect, generationVisualActive);
    }

    private void StopGenerationVisuals(bool clearParticles)
    {
        if (FacilityFlowStateWorld.ContainsSteamGenerator(flowEntityHandle))
        {
            ref SteamGeneratorFlowState state =
                ref FacilityFlowStateWorld.GetSteamGenerator(flowEntityHandle);
            state.IsGenerating = false;
            state.OutputScale = 0f;
        }
        generationVisualActive = false;
        SetVisualParticleActive(particleEffect, false, clear: clearParticles);
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
