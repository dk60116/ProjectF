using System.Collections.Generic;
using UnityEngine;

public class SteamGenerator : InputOutputModule
{
    private const float FluidEpsilon = 0.0001f;
    private const float SteamGenerationStartReserveSeconds = 1.25f;

    [SerializeField]
    private InstallationFacingDirection localPipeAreaConnectionDirection = InstallationFacingDirection.PositiveX;

    private bool hasSteamGenerationReserve;

    public InstallationFacingDirection LocalPipeAreaConnectionDirection => localPipeAreaConnectionDirection;

    public override void ManagedUpdateTick(float deltaTime)
    {
        base.ManagedUpdateTick(deltaTime);
        ConsumeSteamForGeneration(deltaTime);
    }

    public bool TryGetPipeAreaConnectionDirection(Quaternion rotation, out Vector2Int direction)
    {
        return TryResolveDirection(rotation, localPipeAreaConnectionDirection, out direction);
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

        if (!HasEnoughSteamReserve(inputLitersPerSecond, 0f))
        {
            return "No steam";
        }

        isProducing = true;
        return $"Generating ({inputLitersPerSecond} L/s)";
    }

    private bool ConsumeSteamForGeneration(float deltaTime)
    {
        if (deltaTime <= 0f
            || !TryGetSteamInputRecipe(out int inputItemId, out int inputLitersPerSecond)
            || inputLitersPerSecond <= 0)
        {
            return false;
        }

        if (StoredFluidItemId >= 0 && !CanProvideFluidItem(inputItemId))
        {
            return false;
        }

        float requestedLiters = inputLitersPerSecond * deltaTime;
        if (requestedLiters <= FluidEpsilon)
        {
            return false;
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
            hasSteamGenerationReserve = false;
            return false;
        }

        hasSteamGenerationReserve = true;
        return true;
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        hasSteamGenerationReserve = false;
    }

    private bool HasEnoughSteamReserve(int inputLitersPerSecond, float requestedLiters)
    {
        float requiredStoredLiters = ResolveRequiredSteamReserveLiters(
            inputLitersPerSecond,
            requestedLiters);
        return StoredFluidLiters + FluidEpsilon >= requiredStoredLiters;
    }

    private float ResolveRequiredSteamReserveLiters(int inputLitersPerSecond, float requestedLiters)
    {
        float continueReserveLiters = Mathf.Max(inputLitersPerSecond, requestedLiters);
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

    private bool TryGetSteamInputRecipe(out int inputItemId, out int inputLitersPerSecond)
    {
        inputItemId = -1;
        inputLitersPerSecond = 0;

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
                int outputItemId = output.itemDefinition != null ? output.itemDefinition.id : -1;
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
