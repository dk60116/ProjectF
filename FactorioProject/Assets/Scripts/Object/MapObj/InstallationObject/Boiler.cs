using System.Collections.Generic;
using UnityEngine;

public class Boiler : InputOutputModule
{
    private const float FluidEpsilon = 0.0001f;

    [SerializeField]
    private List<InstallationFacingDirection> localPipeConnectionDirections =
        new List<InstallationFacingDirection> { InstallationFacingDirection.PositiveZ };

    public IReadOnlyList<InstallationFacingDirection> LocalPipeConnectionDirections => localPipeConnectionDirections;

    protected override void TryStartNextCraft()
    {
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        if (installedDefinition == null || !CanStoreFluid || !HasRuntimeOutputCoordinates)
        {
            return;
        }

        int recipeCount = Mathf.Min(InputList.Count, OutputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetFluidRecipe(
                    recipeIndex,
                    out int inputLiters,
                    out int outputItemId,
                    out int outputCount))
            {
                continue;
            }

            bool outputsFluid = IsFluidItemId(outputItemId);
            if (!IsRecipeOutputAllowedByItemFilter(outputItemId)
                || StoredFluidLiters + FluidEpsilon < inputLiters
                || (outputsFluid
                    ? !TryResolveFluidOutputStorage(outputItemId, outputCount, out _)
                    : !TryResolveOutputBlock(outputItemId, outputCount, out _))
                || !TryEnsureCraftStartEnergy(installedDefinition))
            {
                continue;
            }

            if (!TryConsumeFluidLiters(inputLiters, out float consumedLiters)
                || consumedLiters + FluidEpsilon < inputLiters)
            {
                continue;
            }

            BeginActiveCraft(recipeIndex, outputItemId, outputCount, installedDefinition);
            return;
        }
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

        if (!HasRuntimeOutputCoordinates)
        {
            return "No output area";
        }

        bool hasRecipe = false;
        bool blockedByWater = false;
        bool blockedByOutput = false;
        bool blockedByEnergy = false;
        bool blockedByTargetFilter = false;
        bool hasFilterAllowedRecipe = false;
        int recipeCount = Mathf.Min(InputList.Count, OutputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetFluidRecipe(
                    recipeIndex,
                    out int inputLiters,
                    out int outputItemId,
                    out int outputCount))
            {
                continue;
            }

            hasRecipe = true;
            if (!IsRecipeOutputAllowedByItemFilter(outputItemId))
            {
                blockedByTargetFilter = true;
                continue;
            }

            hasFilterAllowedRecipe = true;

            if (!CanStoreFluid || StoredFluidLiters + FluidEpsilon < inputLiters)
            {
                blockedByWater = true;
                continue;
            }

            bool outputsFluid = IsFluidItemId(outputItemId);
            if (outputsFluid
                ? !TryResolveFluidOutputStorage(outputItemId, outputCount, out _)
                : !TryResolveOutputBlock(outputItemId, outputCount, out _))
            {
                blockedByOutput = true;
                continue;
            }

            if (!HasOperationalEnergyAvailable(installedDefinition))
            {
                blockedByEnergy = true;
                continue;
            }

            isProducing = true;
            return "Working";
        }

        if (!hasRecipe)
        {
            return "No recipe";
        }

        if (blockedByOutput)
        {
            return "Output full";
        }

        if (blockedByEnergy)
        {
            return "No energy";
        }

        if (blockedByTargetFilter && !hasFilterAllowedRecipe)
        {
            return "No target";
        }

        return blockedByWater ? "No water" : "Stopped";
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

        int recipeCount = Mathf.Min(InputList.Count, OutputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetFluidRecipe(
                    recipeIndex,
                    out _,
                    out outputItemId,
                    out int outputCount)
                || !IsRecipeOutputAllowedByItemFilter(outputItemId))
            {
                continue;
            }

            displayZeroCountItem = true;
            return TryResolveObjectInfoOutputAreaCounts(
                outputItemId,
                Mathf.Max(1, outputCount),
                out outputAreaCount,
                out outputAreaCapacity);
        }

        return false;
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

    private bool TryGetFluidRecipe(
        int recipeIndex,
        out int inputLiters,
        out int outputItemId,
        out int outputCount)
    {
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
        outputItemId = outputEntry.itemDefinition != null ? outputEntry.itemDefinition.id : -1;
        if (inputEntry.itemDefinition == null || outputItemId < 0)
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
