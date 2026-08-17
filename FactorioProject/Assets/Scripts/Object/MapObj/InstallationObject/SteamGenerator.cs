using System.Collections.Generic;
using UnityEngine;

public class SteamGenerator : InputOutputModule
{
    private const float FluidEpsilon = 0.0001f;
    private const float SteamGenerationStartReserveSeconds = 1.25f;

    [SerializeField]
    private InstallationFacingDirection localPipeAreaConnectionDirection = InstallationFacingDirection.PositiveX;

    private bool hasSteamGenerationReserve;

    [SerializeField]
    private Transform wheelTF;
    [SerializeField, Min(0f)]
    private float wheelRotationDegreesPerSecond = 180f;

    public InstallationFacingDirection LocalPipeAreaConnectionDirection => localPipeAreaConnectionDirection;

    public override void ManagedUpdateTick(float deltaTime)
    {
        base.ManagedUpdateTick(deltaTime);
        bool isGenerating = ConsumeSteamForGeneration(deltaTime);
        UpdateGenerationVisuals(isGenerating, deltaTime);
    }

    protected override void OnDisable()
    {
        hasSteamGenerationReserve = false;
        StopGenerationVisuals(true);
        base.OnDisable();
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

        if (StoredFluidLiters > FluidEpsilon)
        {
            return StoredFluidItemId < 0 || CanProvideFluidItem(inputItemId);
        }

        return CanStoreFluid
               && HasFluidStorageSpace
               && HasConnectedFluidSource(inputItemId);
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

        if (!HasAvailableSteamGenerationReserve(inputLitersPerSecond))
        {
            return false;
        }

        wattsPerSecond = outputWattsPerSecond;
        return true;
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

        if (!HasAvailableSteamGenerationReserve(inputLitersPerSecond))
        {
            return "No steam";
        }

        isProducing = true;
        return "Generating";
    }

    private bool ConsumeSteamForGeneration(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return hasSteamGenerationReserve;
        }

        if (!TryGetSteamInputRecipe(out int inputItemId, out int inputLitersPerSecond)
            || inputLitersPerSecond <= 0)
        {
            SetSteamGenerationReserve(false);
            return false;
        }

        if (StoredFluidItemId >= 0 && !CanProvideFluidItem(inputItemId))
        {
            SetSteamGenerationReserve(false);
            return false;
        }

        float requestedLiters = inputLitersPerSecond * deltaTime;
        if (requestedLiters <= FluidEpsilon)
        {
            return hasSteamGenerationReserve;
        }

        float requiredStoredLiters = ResolveRequiredSteamReserveLiters(
            inputLitersPerSecond,
            requestedLiters);
        float missingLocalLiters = requiredStoredLiters - StoredFluidLiters;
        if (missingLocalLiters > FluidEpsilon)
        {
            TryPullFluidFromConnectedStorage(inputItemId, missingLocalLiters, out _);
        }

        if (StoredFluidLiters + FluidEpsilon < requiredStoredLiters
            || !TryConsumeFluidLiters(inputItemId, requestedLiters, out float consumedLiters)
            || consumedLiters + FluidEpsilon < requestedLiters)
        {
            SetSteamGenerationReserve(false);
            return false;
        }

        SetSteamGenerationReserve(true);
        return true;
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        hasSteamGenerationReserve = false;
        StopGenerationVisuals(true);
    }

    private bool HasEnoughSteamReserve(int inputLitersPerSecond, float requestedLiters)
    {
        float requiredStoredLiters = ResolveRequiredSteamReserveLiters(
            inputLitersPerSecond,
            requestedLiters);
        return StoredFluidLiters + FluidEpsilon >= requiredStoredLiters;
    }

    private bool HasAvailableSteamGenerationReserve(int inputLitersPerSecond)
    {
        return hasSteamGenerationReserve || HasEnoughSteamReserve(inputLitersPerSecond, 0f);
    }

    private float ResolveRequiredSteamReserveLiters(int inputLitersPerSecond, float requestedLiters)
    {
        // Once generation has started, the stored reserve is allowed to absorb
        // uneven boiler/output update timing. Requiring a full second of steam on
        // every tick made otherwise sufficient supplies repeatedly stop and start.
        float continueReserveLiters = Mathf.Max(FluidEpsilon, requestedLiters);
        if (hasSteamGenerationReserve)
        {
            return continueReserveLiters;
        }

        float startReserveLiters = Mathf.Max(
            continueReserveLiters,
            inputLitersPerSecond * SteamGenerationStartReserveSeconds);
        float capacity = FluidStorageCapacityLiters;
        return capacity > FluidEpsilon
            ? Mathf.Min(capacity, startReserveLiters)
            : startReserveLiters;
    }

    private void SetSteamGenerationReserve(bool hasReserve)
    {
        if (hasSteamGenerationReserve == hasReserve)
        {
            return;
        }

        hasSteamGenerationReserve = hasReserve;
        InputOutputModule.WakeElectricRuntimeModules();
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
                if (outputItemId >= 0 && !IsRecipeOutputAllowedByItemFilter(outputItemId))
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

    private void UpdateGenerationVisuals(bool isGenerating, float deltaTime)
    {
        if (!isGenerating)
        {
            StopGenerationVisuals(false);
            return;
        }

        if (wheelTF != null && deltaTime > 0f && wheelRotationDegreesPerSecond > 0f)
        {
            wheelTF.Rotate(0f, 0f, -wheelRotationDegreesPerSecond * deltaTime, Space.Self);
        }

        if (particleEffect != null && !particleEffect.isPlaying)
        {
            particleEffect.Play();
        }
    }

    private void StopGenerationVisuals(bool clearParticles)
    {
        if (particleEffect == null || !particleEffect.isPlaying)
        {
            return;
        }

        particleEffect.Stop(
            true,
            clearParticles
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
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
