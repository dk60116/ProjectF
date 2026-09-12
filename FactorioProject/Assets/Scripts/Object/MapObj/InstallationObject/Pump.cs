using System.Collections.Generic;
using UnityEngine;

public class Pump : InputOutputModule
{
    private const string DefaultWaterItemName = "Water";
    private const int DefaultWaterItemId = 1;
    private const int MaxWaterEmitAttemptsPerTick = 32;
    private const float WaterOutputBudgetSeconds = 1f;
    [SerializeField]
    private InstallationFacingDirection localPipeConnectionDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField]
    private ItemDefinition waterDefinition;
    [SerializeField, Min(0)]
    private int fallbackWaterItemId = DefaultWaterItemId;

    private long waterAccumulatorUnits;
    private long availableWaterOutputUnits;
    private long waterOutputBudgetUpdatedTick = -1L;

    public InstallationFacingDirection LocalPipeConnectionDirection => localPipeConnectionDirection;
    public float WaterLitersPerSecond
    {
        get
        {
            ItemDefinition installedDefinition = ResolveInstalledDefinition();
            return installedDefinition != null
                ? installedDefinition.FluidOutputLitersPerSecond
                : 0f;
        }
    }

    public bool TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond)
    {
        outputItemId = ResolveWaterItemId();
        litersPerSecond = WaterLitersPerSecond;
        return outputItemId >= 0;
    }

    public override float GetStoredFluidTemperatureCelsius(int fluidItemId)
    {
        return fluidItemId >= 0 && fluidItemId == ResolveWaterItemId()
            ? MapClimate.CurrentWaterTemperatureCelsius
            : base.GetStoredFluidTemperatureCelsius(fluidItemId);
    }

    public bool HasPipeConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        return direction != Vector2Int.zero
               && TryResolveDirection(rotation, localPipeConnectionDirection, out Vector2Int resolvedDirection)
               && resolvedDirection == direction;
    }

    public bool TryGetPipeConnectionDirection(Quaternion rotation, out Vector2Int direction)
    {
        return TryResolveDirection(rotation, localPipeConnectionDirection, out direction);
    }

    public override void ApplyManagedUpdateTick()
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime))
        {
            return;
        }

        if (!Application.isPlaying || deltaTime <= 0f)
        {
            return;
        }

        ProduceWater(deltaTime);
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        waterAccumulatorUnits = 0L;
        availableWaterOutputUnits = 0L;
        waterOutputBudgetUpdatedTick = -1L;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        return true;
    }

    protected override bool AppendOutputItemIds(ISet<int> outputItemIds)
    {
        bool foundAny = base.AppendOutputItemIds(outputItemIds);
        if (outputItemIds == null)
        {
            return foundAny;
        }

        int waterItemId = ResolveWaterItemId();
        if (waterItemId < 0)
        {
            return foundAny;
        }

        outputItemIds.Add(waterItemId);
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

        outputItemId = ResolveWaterItemId();
        outputAreaCount = 0;
        outputAreaCapacity = 0;
        displayZeroCountItem = outputItemId >= 0;
        return outputItemId >= 0
               && TryResolveObjectInfoOutputAreaCounts(
                   outputItemId,
                   Mathf.Max(1, Mathf.CeilToInt(WaterLitersPerSecond)),
                   out outputAreaCount,
                   out outputAreaCapacity);
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = true;
        return "Working";
    }

    private void ProduceWater(float deltaTime)
    {
        int waterItemId = ResolveWaterItemId();
        float litersPerSecond = WaterLitersPerSecond;
        if (waterItemId < 0 || litersPerSecond <= 0f || !HasRuntimeOutputCoordinates)
        {
            waterAccumulatorUnits = 0L;
            availableWaterOutputUnits = 0L;
            waterOutputBudgetUpdatedTick = -1L;
            return;
        }

        RefreshWaterOutputBudget(litersPerSecond, deltaTime);
        long availableThisTickUnits = System.Math.Min(
            DeterministicSimulationUnits.RateForTicks(
                litersPerSecond,
                DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime)),
            availableWaterOutputUnits);
        float requestedForLiveStorage = DeterministicSimulationUnits.ToFloat(
            waterAccumulatorUnits + availableThisTickUnits);
        if (TryEmitFluidOutputToConnectedStorages(
                waterItemId,
                requestedForLiveStorage,
                MapClimate.CurrentWaterTemperatureCelsius,
                out float acceptedLiters))
        {
            long acceptedUnits = DeterministicSimulationUnits.FromFloat(acceptedLiters);
            long accumulatedUnitsUsed = System.Math.Min(waterAccumulatorUnits, acceptedUnits);
            waterAccumulatorUnits -= accumulatedUnitsUsed;

            long budgetUnitsUsed = System.Math.Min(
                availableThisTickUnits,
                System.Math.Max(0L, acceptedUnits - accumulatedUnitsUsed));
            availableThisTickUnits -= budgetUnitsUsed;
            availableWaterOutputUnits = System.Math.Max(
                0L,
                availableWaterOutputUnits - budgetUnitsUsed);
        }

        availableWaterOutputUnits = System.Math.Max(
            0L,
            availableWaterOutputUnits - availableThisTickUnits);
        waterAccumulatorUnits += availableThisTickUnits;

        if (waterAccumulatorUnits < DeterministicSimulationUnits.UnitsPerWhole)
        {
            return;
        }

        int emitAttempts = Mathf.Min(
            (int)System.Math.Min(
                int.MaxValue,
                waterAccumulatorUnits / DeterministicSimulationUnits.UnitsPerWhole),
            Mathf.Min(MaxWaterEmitAttemptsPerTick, Mathf.Max(1, RuntimeAreaMaxObjects)));
        Vector3 startWorldPosition = ResolveConsumeTargetWorldPosition();

        for (int i = 0; i < emitAttempts; i++)
        {
            if (!TryEmitOutputItems(waterItemId, 1, startWorldPosition))
            {
                waterAccumulatorUnits = System.Math.Min(
                    waterAccumulatorUnits,
                    DeterministicSimulationUnits.UnitsPerWhole);
                return;
            }

            waterAccumulatorUnits = System.Math.Max(
                0L,
                waterAccumulatorUnits - DeterministicSimulationUnits.UnitsPerWhole);
        }
    }

    private void RefreshWaterOutputBudget(
        float outputLitersPerSecond,
        float initialAvailableSeconds)
    {
        float outputRate = Mathf.Max(0f, outputLitersPerSecond);
        long nowTick = MapObjectTickManager.CurrentSimulationTick;
        long maximumBudgetUnits = System.Math.Max(
            0L,
            DeterministicSimulationUnits.FromFloat(outputRate * WaterOutputBudgetSeconds)
            - waterAccumulatorUnits);
        if (waterOutputBudgetUpdatedTick < 0L || nowTick < waterOutputBudgetUpdatedTick)
        {
            availableWaterOutputUnits = System.Math.Min(
                maximumBudgetUnits,
                DeterministicSimulationUnits.RateForTicks(
                    outputRate,
                    DeterministicSimulationUnits.SecondsToTicks(initialAvailableSeconds)));
            waterOutputBudgetUpdatedTick = nowTick;
            return;
        }

        long elapsedTicks = System.Math.Max(0L, nowTick - waterOutputBudgetUpdatedTick);
        availableWaterOutputUnits = System.Math.Min(
            maximumBudgetUnits,
            availableWaterOutputUnits
            + DeterministicSimulationUnits.RateForTicks(outputRate, elapsedTicks));
        waterOutputBudgetUpdatedTick = nowTick;
    }

    private int ResolveWaterItemId()
    {
        int waterItemId = ResolveWaterItemId(waterDefinition, fallbackWaterItemId);
        if (waterDefinition == null || waterDefinition.id != waterItemId)
        {
            waterDefinition = ResolveWaterDefinitionFromManager();
        }

        return waterItemId;
    }

    public static int ResolveWaterItemId(ItemDefinition preferredDefinition, int fallbackWaterItemId = DefaultWaterItemId)
    {
        if (preferredDefinition != null && preferredDefinition.id >= 0)
        {
            return preferredDefinition.id;
        }

        ItemDefinition resolvedDefinition = ResolveWaterDefinitionFromManager();
        if (resolvedDefinition != null && resolvedDefinition.id >= 0)
        {
            return resolvedDefinition.id;
        }

        return fallbackWaterItemId >= 0 ? fallbackWaterItemId : -1;
    }

    private static ItemDefinition ResolveWaterDefinitionFromManager()
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        return ItemDefinitionLookup.ResolveByStableName(definitions, DefaultWaterItemName);
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

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        fallbackWaterItemId = Mathf.Max(0, fallbackWaterItemId);
    }
#endif
}
