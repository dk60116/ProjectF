using System.Collections.Generic;
using UnityEngine;

public class Pump : InputOutputModule
{
    private const string DefaultWaterItemName = "Water";
    private const int DefaultWaterItemId = 4;
    private const int MaxWaterEmitAttemptsPerTick = 32;
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    [SerializeField]
    private InstallationFacingDirection localPipeConnectionDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField, Min(0f)]
    private float waterLitersPerSecond = 1f;
    [SerializeField]
    private ItemDefinition waterDefinition;
    [SerializeField, Min(0)]
    private int fallbackWaterItemId = DefaultWaterItemId;

    private float waterLiterAccumulator;
    private readonly Queue<Vector2Int> fluidSearchQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> fluidSearchVisited = new HashSet<Vector2Int>();
    private readonly HashSet<InstallationObject> fluidSearchStorageCandidates = new HashSet<InstallationObject>();

    public InstallationFacingDirection LocalPipeConnectionDirection => localPipeConnectionDirection;
    public float WaterLitersPerSecond => Mathf.Max(0f, waterLitersPerSecond);

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

    public override void ManagedUpdateTick(float deltaTime)
    {
        if (!Application.isPlaying || deltaTime <= 0f)
        {
            return;
        }

        ProduceWater(deltaTime);
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
        isProducing = false;

        if (ResolveWaterItemId() < 0)
        {
            return "No water";
        }

        if (WaterLitersPerSecond <= 0f)
        {
            return "Stopped";
        }

        if (!HasRuntimeOutputCoordinates)
        {
            return "No output area";
        }

        if (HasConnectedFluidStorageSpace())
        {
            isProducing = true;
            return "Working";
        }

        if (!TryResolveOutputBlock(ResolveWaterItemId(), 1, out _))
        {
            return "Output full";
        }

        isProducing = true;
        return "Working";
    }

    private void ProduceWater(float deltaTime)
    {
        int waterItemId = ResolveWaterItemId();
        float litersPerSecond = WaterLitersPerSecond;
        if (waterItemId < 0 || litersPerSecond <= 0f || !HasRuntimeOutputCoordinates)
        {
            waterLiterAccumulator = 0f;
            return;
        }

        waterLiterAccumulator += litersPerSecond * deltaTime;

        if (TryRouteWaterToFluidStorage(waterLiterAccumulator, true, out float acceptedLiters))
        {
            waterLiterAccumulator = Mathf.Max(0f, waterLiterAccumulator - acceptedLiters);
        }

        if (waterLiterAccumulator < 1f)
        {
            return;
        }

        int emitAttempts = Mathf.Min(
            Mathf.FloorToInt(waterLiterAccumulator),
            Mathf.Min(MaxWaterEmitAttemptsPerTick, Mathf.Max(1, RuntimeAreaMaxObjects)));
        Vector3 startWorldPosition = ResolveConsumeTargetWorldPosition();

        for (int i = 0; i < emitAttempts; i++)
        {
            if (!TryEmitOutputItems(waterItemId, 1, startWorldPosition))
            {
                waterLiterAccumulator = Mathf.Min(waterLiterAccumulator, 1f);
                return;
            }

            waterLiterAccumulator = Mathf.Max(0f, waterLiterAccumulator - 1f);
        }
    }

    private bool HasConnectedFluidStorageSpace()
    {
        return TryRouteWaterToFluidStorage(0f, false, out _);
    }

    private bool TryRouteWaterToFluidStorage(float requestedLiters, bool commit, out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (!HasRuntimeOutputCoordinates)
        {
            return false;
        }

        fluidSearchQueue.Clear();
        fluidSearchVisited.Clear();
        fluidSearchStorageCandidates.Clear();
        InstallationObject targetStorage = null;
        float targetStorageFillRatio = float.PositiveInfinity;

        IReadOnlyList<Vector2Int> outputCoordinates = RuntimeOutputCoordinates;
        if (outputCoordinates != null)
        {
            for (int i = 0; i < outputCoordinates.Count; i++)
            {
                EnqueueFluidSearchCoordinate(outputCoordinates[i]);
            }
        }

        if (TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
            && TryResolveDirection(
                transform.rotation,
                localPipeConnectionDirection,
                out Vector2Int pipeDirection))
        {
            EnqueueFluidSearchCoordinate(anchorCoordinate + pipeDirection);
        }

        while (fluidSearchQueue.Count > 0)
        {
            Vector2Int coordinate = fluidSearchQueue.Dequeue();
            bool hasPipe = TryGetPipeAtCoordinate(coordinate, out Pipe pipe, out Quaternion pipeRotation);
            bool hasFluidStorageBody = TryResolveFluidStorageBodyAtCoordinate(
                coordinate,
                false,
                out InstallationObject fluidStorage);
            if (!hasFluidStorageBody && hasPipe)
            {
                TryResolveFluidStorageAtCoordinate(
                    coordinate,
                    false,
                    out fluidStorage);
            }

            ConsiderFluidStorageCandidate(
                fluidStorage,
                ref targetStorage,
                ref targetStorageFillRatio);

            if (!hasPipe && !hasFluidStorageBody)
            {
                continue;
            }

            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = CardinalDirections[directionIndex];
                if (hasPipe && !pipe.HasConnectionTowards(pipeRotation, direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                if (!TryGetFluidNetworkConnectionAtCoordinate(
                        nextCoordinate,
                        -direction,
                        out InstallationObject nextStorage,
                        out bool canContinueRoute))
                {
                    continue;
                }

                ConsiderFluidStorageCandidate(
                    nextStorage,
                    ref targetStorage,
                    ref targetStorageFillRatio);

                if (!canContinueRoute)
                {
                    continue;
                }

                EnqueueFluidSearchCoordinate(nextCoordinate);
            }
        }

        return TryUseFluidStorage(targetStorage, requestedLiters, commit, out acceptedLiters);
    }

    private void EnqueueFluidSearchCoordinate(Vector2Int coordinate)
    {
        if (fluidSearchVisited.Add(coordinate))
        {
            fluidSearchQueue.Enqueue(coordinate);
        }
    }

    private bool TryGetFluidNetworkConnectionAtCoordinate(
        Vector2Int coordinate,
        Vector2Int directionToPrevious,
        out InstallationObject storage,
        out bool canContinueRoute)
    {
        storage = null;
        canContinueRoute = false;

        if (TryGetPipeAtCoordinate(coordinate, out Pipe pipe, out Quaternion pipeRotation))
        {
            if (!pipe.HasConnectionTowards(pipeRotation, directionToPrevious))
            {
                return false;
            }

            TryResolveFluidStorageAtCoordinate(coordinate, false, out storage);
            canContinueRoute = true;
            return true;
        }

        if (TryResolveFluidStorageBodyAtCoordinate(coordinate, false, out storage))
        {
            canContinueRoute = true;
            return true;
        }

        return false;
    }

    private bool TryGetPipeAtCoordinate(Vector2Int coordinate, out Pipe pipe, out Quaternion pipeRotation)
    {
        pipe = null;
        pipeRotation = Quaternion.identity;
        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is Pipe candidatePipe)
            || !candidatePipe.gameObject.activeInHierarchy)
        {
            return false;
        }

        pipe = candidatePipe;
        pipeRotation = candidatePipe.transform.rotation;
        return true;
    }

    private void ConsiderFluidStorageCandidate(
        InstallationObject storage,
        ref InstallationObject targetStorage,
        ref float targetStorageFillRatio)
    {
        if (storage == null
            || storage == this
            || !storage.HasFluidStorageSpace
            || !fluidSearchStorageCandidates.Add(storage))
        {
            return;
        }

        float capacity = Mathf.Max(0f, storage.FluidStorageCapacityLiters);
        if (capacity <= 0.0001f)
        {
            return;
        }

        float fillRatio = Mathf.Clamp01(storage.StoredFluidLiters / capacity);
        if (targetStorage != null && fillRatio >= targetStorageFillRatio)
        {
            return;
        }

        targetStorage = storage;
        targetStorageFillRatio = fillRatio;
    }

    private bool TryUseFluidStorage(
        InstallationObject storage,
        float requestedLiters,
        bool commit,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (storage == null)
        {
            return false;
        }

        if (!commit)
        {
            acceptedLiters = storage.AvailableFluidStorageLiters;
            return acceptedLiters > 0.0001f;
        }

        return storage.TryAddFluidLiters(requestedLiters, out acceptedLiters);
    }

    private bool TryResolveFluidStorageAtCoordinate(Vector2Int coordinate, out InstallationObject storage)
    {
        return TryResolveFluidStorageAtCoordinate(coordinate, true, out storage);
    }

    private bool TryResolveFluidStorageAtCoordinate(
        Vector2Int coordinate,
        bool requireStorageSpace,
        out InstallationObject storage)
    {
        if (TryGetRuntimePipeFluidStorageAtCoordinate(coordinate, this, requireStorageSpace, out storage))
        {
            return true;
        }

        storage = null;
        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is InstallationObject installationObject)
            || installationObject == this
            || installationObject is Pipe
            || installationObject is Pump
            || !installationObject.gameObject.activeInHierarchy
            || !installationObject.CanStoreFluid
            || (requireStorageSpace && !installationObject.HasFluidStorageSpace))
        {
            return false;
        }

        storage = installationObject;
        return true;
    }

    private bool TryResolveFluidStorageBodyAtCoordinate(
        Vector2Int coordinate,
        bool requireStorageSpace,
        out InstallationObject storage)
    {
        storage = null;
        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is InstallationObject installationObject)
            || installationObject == this
            || installationObject is Pipe
            || installationObject is Pump
            || !installationObject.gameObject.activeInHierarchy
            || !installationObject.CanStoreFluid
            || (requireStorageSpace && !installationObject.HasFluidStorageSpace)
            || !ContainsRuntimeOccupiedCoordinate(installationObject, coordinate))
        {
            return false;
        }

        storage = installationObject;
        return true;
    }

    private static bool ContainsRuntimeOccupiedCoordinate(InstallationObject installationObject, Vector2Int coordinate)
    {
        IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject != null
            ? installationObject.RuntimeOccupiedCoordinates
            : null;
        if (occupiedCoordinates == null)
        {
            return false;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (occupiedCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private int ResolveWaterItemId()
    {
        if (waterDefinition != null && waterDefinition.id >= 0)
        {
            return waterDefinition.id;
        }

        ItemDefinition resolvedDefinition = ResolveWaterDefinitionFromManager();
        if (resolvedDefinition != null && resolvedDefinition.id >= 0)
        {
            waterDefinition = resolvedDefinition;
            return resolvedDefinition.id;
        }

        return fallbackWaterItemId >= 0 ? fallbackWaterItemId : -1;
    }

    private static ItemDefinition ResolveWaterDefinitionFromManager()
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            if (string.Equals(definition.itemName, DefaultWaterItemName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(definition.name, DefaultWaterItemName, System.StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
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
        waterLitersPerSecond = Mathf.Max(0f, waterLitersPerSecond);
        fallbackWaterItemId = Mathf.Max(0, fallbackWaterItemId);
    }
#endif
}
