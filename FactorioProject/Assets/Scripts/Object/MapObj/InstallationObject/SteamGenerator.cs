using System.Collections.Generic;
using ProjectF.Simulation;
using UnityEngine;

public class SteamGenerator : InputOutputModule, IFacilityFlowAdapter
{
    private const float FluidEpsilon = 0.0001f;

    [SerializeField]
    private InstallationFacingDirection localPipeAreaConnectionDirection = InstallationFacingDirection.PositiveX;

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
            UtilityPole.NotifyElectricPowerSourceStateChanged();
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        UtilityPole.NotifyElectricPowerSourceStateChanged();
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        StopGenerationVisuals(true);
        base.OnDisable();
        UtilityPole.NotifyElectricPowerSourceStateChanged();
    }

    protected override bool ShouldAutoPullFluidFromConnectedStorage()
    {
        return false;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        if (!InputOutputModule.IsInDirectedBoilerSteamChain(this)
            || !TryGetSteamInputRecipe(out int inputItemId, out _))
        {
            return false;
        }

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
            || !TryGetSteamInputRecipe(out int inputItemId, out int inputLitersPerSecond)
            || inputLitersPerSecond <= 0
            || !CanStoreFluid)
        {
            return false;
        }

        if (StoredFluidItemId >= 0 && !CanProvideFluidItem(inputItemId))
        {
            return false;
        }

        if (!generationVisualActive
            || !InputOutputModule.IsInDirectedBoilerSteamChain(this))
        {
            return false;
        }

        wattsPerSecond = outputWattsPerSecond;
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
        if (!TryGetSteamInputRecipe(out int inputItemId, out int inputLitersPerSecond))
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

        if (!InputOutputModule.IsInDirectedBoilerSteamChain(this))
        {
            return "No boiler steam connection";
        }

        if (!generationVisualActive)
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
        bool hasRecipe = TryGetSteamInputRecipe(out int inputItemId, out int inputLitersPerSecond)
                         && inputLitersPerSecond > 0;
        bool valid = hasRecipe
                     && InputOutputModule.IsInDirectedBoilerSteamChain(this)
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
        float requiredStoredLiters = batch.GetSteamRequiredLiters(index);
        if (!batch.IsSteamGeneratorValid(index)
            || inputItemId < 0
            || requestedLiters <= FluidEpsilon
            || (StoredFluidItemId >= 0 && !CanProvideFluidItem(inputItemId)))
        {
            SetGenerationActive(false);
            return;
        }

        float consumedLiters = 0f;
        bool generated = StoredFluidLiters + FluidEpsilon >= requiredStoredLiters
                         && TryConsumeFluidLiters(inputItemId, requestedLiters, out consumedLiters)
                         && consumedLiters + FluidEpsilon >= requestedLiters;
        if (generated)
        {
            RecordFluidNetworkConsumption(inputItemId, consumedLiters);
        }

        SetGenerationActive(generated);
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        StopGenerationVisuals(true);
    }

    private void SetGenerationActive(bool active)
    {
        if (generationVisualActive == active)
        {
            return;
        }

        generationVisualActive = active;
        MarkManagedRuntimeVisualsDirty();
        UtilityPole.NotifyElectricPowerSourceStateChanged();
    }

    private bool TryGetSteamInputRecipe(out int inputItemId, out int inputLitersPerSecond)
    {
        return TryGetSteamGenerationRecipe(
            out inputItemId,
            out inputLitersPerSecond,
            out _,
            out _);
    }

    private bool TryGetSteamGenerationRecipe(
        out int inputItemId,
        out int inputLitersPerSecond,
        out int outputItemId,
        out int outputWattsPerSecond)
    {
        inputItemId = -1;
        inputLitersPerSecond = 0;
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
                    ? Mathf.RoundToInt(ItemDefinition.ResolveElectricOutputWatts(outputDefinition, output.count))
                    : 0;
                if (outputItemId >= 0 && !IsRecipeOutputAvailable(outputItemId))
                {
                    continue;
                }
            }

            inputItemId = candidateInputItemId;
            inputLitersPerSecond = Mathf.Max(1, input.count);
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
