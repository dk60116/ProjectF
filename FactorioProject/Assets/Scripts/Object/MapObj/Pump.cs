using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Connects the two PipePass cells and starts a new pressure-loss section.
/// The pump does not own fluid; producers and consumers still transfer the
/// authoritative fluid directly through the connected network.
/// </summary>
public class Pump : InputOutputModule
{
    private const int MaxObjectInfoNetworkSearchNodes = 64;

    internal override bool TryGetRuntimePassiveFluidPass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        return false;
    }

    private readonly Queue<Vector2Int> objectInfoSearchQueue = new Queue<Vector2Int>(8);
    private readonly HashSet<Vector2Int> objectInfoSearchVisited = new HashSet<Vector2Int>();
    private readonly List<RuntimePumpPipePass> objectInfoPumpPasses =
        new List<RuntimePumpPipePass>(2);
    private readonly List<InputOutputModule> objectInfoModules = new List<InputOutputModule>(4);
    private readonly HashSet<InputOutputModule> objectInfoFluidSources =
        new HashSet<InputOutputModule>();
    private readonly List<Vector2Int> fluidDockSeedCoordinates = new List<Vector2Int>(2);
    private readonly List<InstallationObject> fluidDockStorageScratch =
        new List<InstallationObject>(4);
    private float plannedFluidDockDeltaTime;

    public override void PlanManagedUpdateTick(float deltaTime)
    {
        base.PlanManagedUpdateTick(deltaTime);
        plannedFluidDockDeltaTime = Mathf.Max(0f, deltaTime);
    }

    public override void ApplyManagedUpdateTick()
    {
        float deltaTime = plannedFluidDockDeltaTime;
        plannedFluidDockDeltaTime = 0f;
        base.ApplyManagedUpdateTick();
        TryUnloadDockedWater(deltaTime);
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        return base.ShouldKeepRuntimeUpdateTickActive() || HasDockedFluidStorage();
    }

    private bool TryUnloadDockedWater(float deltaTime)
    {
        if (deltaTime <= 0f
            || !TryResolveDockedWaterSource(
                out InstallationObject sourceStorage,
                out int waterItemId))
        {
            return false;
        }

        float requestedLiters = ConnectedFluidStorageTransferLitersPerSecond * deltaTime;
        float temperatureCelsius = sourceStorage.GetStoredFluidTemperatureCelsius(waterItemId);
        return TryTransferFluidFromStorageToConnectedStorage(
            sourceStorage,
            waterItemId,
            requestedLiters,
            temperatureCelsius,
            fluidDockSeedCoordinates,
            out _);
    }

    private bool TryResolveDockedWaterSource(
        out InstallationObject sourceStorage,
        out int waterItemId)
    {
        sourceStorage = null;
        waterItemId = WaterPump.ResolveWaterItemId(null);
        if (waterItemId < 0 || !CollectFluidDockSeedCoordinates())
        {
            return false;
        }

        Fluidtank bestMountedTank = null;
        for (int i = 0; i < fluidDockSeedCoordinates.Count; i++)
        {
            Vector2Int dockCoordinate = fluidDockSeedCoordinates[i];
            if (!TryGetRuntimePipePass(
                    dockCoordinate,
                    out _,
                    out Vector2Int externalDirection))
            {
                continue;
            }

            Vector2Int directionFromVehicleToPump = -externalDirection;
            fluidDockStorageScratch.Clear();
            CollectActiveInstallationsAtRuntimeGridCoordinate(
                dockCoordinate,
                fluidDockStorageScratch);
            for (int storageIndex = 0;
                 storageIndex < fluidDockStorageScratch.Count;
                 storageIndex++)
            {
                if (fluidDockStorageScratch[storageIndex] is not Fluidtank candidate
                    || !candidate.CanProvideMountedFluidToPump(
                        this,
                        dockCoordinate,
                        directionFromVehicleToPump,
                        waterItemId,
                        0.0001f)
                    || bestMountedTank != null
                    && bestMountedTank.RuntimePlacementSequence
                    <= candidate.RuntimePlacementSequence)
                {
                    continue;
                }

                bestMountedTank = candidate;
            }
        }

        fluidDockStorageScratch.Clear();
        sourceStorage = bestMountedTank;
        return sourceStorage != null;
    }

    private bool HasDockedFluidStorage()
    {
        if (!CollectFluidDockSeedCoordinates())
        {
            return false;
        }

        for (int i = 0; i < fluidDockSeedCoordinates.Count; i++)
        {
            Vector2Int coordinate = fluidDockSeedCoordinates[i];
            fluidDockStorageScratch.Clear();
            CollectActiveInstallationsAtRuntimeGridCoordinate(
                coordinate,
                fluidDockStorageScratch);
            for (int storageIndex = 0;
                 storageIndex < fluidDockStorageScratch.Count;
                 storageIndex++)
            {
                if (fluidDockStorageScratch[storageIndex] is Fluidtank tank
                    && tank.IsFlatCarMounted)
                {
                    fluidDockStorageScratch.Clear();
                    return true;
                }
            }
        }

        fluidDockStorageScratch.Clear();
        return false;
    }

    private bool CollectFluidDockSeedCoordinates()
    {
        fluidDockSeedCoordinates.Clear();
        if (!TryGetPlacementRuntime(
                out Vector2Int anchorCoordinate,
                out int quarterTurns))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.PipeInput
                || !TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int coordinate)
                || fluidDockSeedCoordinates.Contains(coordinate))
            {
                continue;
            }

            fluidDockSeedCoordinates.Add(coordinate);
        }

        return fluidDockSeedCoordinates.Count > 0;
    }

    public bool TryGetObjectInfoFluidInfo(
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        pressureLitersPerSecond = 0f;
        if (!isActiveAndEnabled
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        objectInfoSearchQueue.Clear();
        objectInfoSearchVisited.Clear();
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType == RectGridBlockType.PipeInput
                && TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int endpoint))
            {
                EnqueueObjectInfoCoordinate(endpoint);
            }
        }

        bool foundFallbackFluid = false;
        int searchedNodeCount = 0;
        while (objectInfoSearchQueue.Count > 0
               && searchedNodeCount++ < MaxObjectInfoNetworkSearchNodes)
        {
            Vector2Int coordinate = objectInfoSearchQueue.Dequeue();
            if (TryGetObjectInfoFromPipe(
                    coordinate,
                    out fluidItemId,
                    out temperatureCelsius,
                    out pressureLitersPerSecond))
            {
                ClearObjectInfoScratch();
                return true;
            }

            if (TryGetObjectInfoSourceAt(
                    coordinate,
                    out int sourceItemId,
                    out float sourceTemperature,
                    out float sourcePressure)
                && (!foundFallbackFluid || sourcePressure > pressureLitersPerSecond))
            {
                fluidItemId = sourceItemId;
                temperatureCelsius = sourceTemperature;
                pressureLitersPerSecond = sourcePressure;
                foundFallbackFluid = true;
            }
            else if (!foundFallbackFluid
                     && TryGetRuntimePipeFluidStorageAtCoordinate(
                         coordinate,
                         this,
                         false,
                         out InstallationObject storage)
                     && storage.StoredFluidItemId >= 0)
            {
                fluidItemId = storage.StoredFluidItemId;
                temperatureCelsius = storage.GetStoredFluidTemperatureCelsius(fluidItemId);
                foundFallbackFluid = true;
            }

            EnqueueConnectedPumpCoordinates(coordinate);
        }

        ClearObjectInfoScratch();
        return foundFallbackFluid;
    }

    private bool TryGetObjectInfoFromPipe(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        pressureLitersPerSecond = 0f;
        if (PipeWorld.Current != null
            && PipeWorld.Current.TryGetAtCoordinate(coordinate, out PipeRuntimeRecord record))
        {
            return record.TryGetObjectInfoFluidInfo(
                coordinate,
                out fluidItemId,
                out temperatureCelsius,
                out pressureLitersPerSecond);
        }

        if (!TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !block.TryGetRuntimePipe(out Pipe pipe, out _))
        {
            return false;
        }

        return block.TryGetRuntimePipeRecord(out record)
            ? record.TryGetObjectInfoFluidInfo(
                coordinate,
                out fluidItemId,
                out temperatureCelsius,
                out pressureLitersPerSecond)
            : pipe.TryGetObjectInfoFluidInfo(
                out fluidItemId,
                out temperatureCelsius,
                out pressureLitersPerSecond);
    }

    private bool TryGetObjectInfoSourceAt(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond)
    {
        pressureLitersPerSecond = 0f;
        if (!TryGetFluidOutputInfoAtRuntimeGridCoordinate(
                coordinate,
                out fluidItemId,
                out temperatureCelsius))
        {
            return false;
        }

        objectInfoFluidSources.Clear();
        AppendFluidOutputSourcesAtCoordinate(
            coordinate,
            Vector2Int.zero,
            objectInfoFluidSources);
        foreach (InputOutputModule source in objectInfoFluidSources)
        {
            if (source != null)
            {
                pressureLitersPerSecond +=
                    source.GetObjectInfoFluidPressureLitersPerSecond(fluidItemId);
            }
        }

        objectInfoFluidSources.Clear();
        return true;
    }

    private void EnqueueConnectedPumpCoordinates(Vector2Int coordinate)
    {
        objectInfoPumpPasses.Clear();
        if (!CollectPumpPipePassesAtRuntimeCoordinate(coordinate, objectInfoPumpPasses))
        {
            return;
        }

        for (int i = 0; i < objectInfoPumpPasses.Count; i++)
        {
            RuntimePumpPipePass pass = objectInfoPumpPasses[i];
            EnqueueObjectInfoCoordinate(pass.OtherCoordinate);
            EnqueueObjectInfoCoordinate(coordinate + pass.ExternalDirection);
        }

        objectInfoModules.Clear();
        CollectModulesAtRuntimeAreaCoordinate(coordinate, objectInfoModules);
        CollectModulesAtRuntimeGridCoordinate(coordinate, objectInfoModules);
        for (int passIndex = 0; passIndex < objectInfoPumpPasses.Count; passIndex++)
        {
            Pump sourcePump = objectInfoPumpPasses[passIndex].Pump;
            for (int moduleIndex = 0; moduleIndex < objectInfoModules.Count; moduleIndex++)
            {
                if (objectInfoModules[moduleIndex] is Pump candidatePump
                    && candidatePump.TryGetRuntimeInterlockedEndpoint(
                        sourcePump,
                        coordinate,
                        out Vector2Int endpoint))
                {
                    EnqueueObjectInfoCoordinate(endpoint);
                }
            }
        }

        objectInfoModules.Clear();
        objectInfoPumpPasses.Clear();
    }

    private void EnqueueObjectInfoCoordinate(Vector2Int coordinate)
    {
        if (objectInfoSearchVisited.Add(coordinate))
        {
            objectInfoSearchQueue.Enqueue(coordinate);
        }
    }

    private void ClearObjectInfoScratch()
    {
        objectInfoSearchQueue.Clear();
        objectInfoSearchVisited.Clear();
        objectInfoPumpPasses.Clear();
        objectInfoModules.Clear();
        objectInfoFluidSources.Clear();
    }

    internal bool TryGetRuntimeInterlockedEndpoint(
        Pump otherPump,
        Vector2Int otherPumpEndpoint,
        out Vector2Int endpoint)
    {
        endpoint = default;
        if (otherPump == null
            || otherPump == this
            || !gameObject.activeInHierarchy
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !IsRuntimeObjectCoordinate(otherPumpEndpoint))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.PipeInput
                || !TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int candidateEndpoint)
                || !otherPump.IsRuntimeObjectCoordinate(candidateEndpoint))
            {
                continue;
            }

            endpoint = candidateEndpoint;
            return true;
        }

        return false;
    }

    private bool IsRuntimeObjectCoordinate(Vector2Int coordinate)
    {
        return TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
               && TryGetRectGridBlockTypeAtCoordinate(
                   this,
                   anchorCoordinate,
                   quarterTurns,
                   coordinate,
                   out RectGridBlockType blockType)
               && blockType == RectGridBlockType.Object;
    }

    public bool TryGetRuntimePipePass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !TryGetPipePassExternalDirection(
                this,
                anchorCoordinate,
                quarterTurns,
                coordinate,
                out externalDirection))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.PipeInput
                || !TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int candidateCoordinate)
                || candidateCoordinate == coordinate)
            {
                continue;
            }

            otherCoordinate = candidateCoordinate;
            return true;
        }

        return false;
    }

    public bool TryGetPipePassExternalDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out Vector2Int externalDirection)
    {
        externalDirection = Vector2Int.zero;
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
                   out Vector2Int inwardDirection)
               && (externalDirection = -inwardDirection) != Vector2Int.zero;
    }

    internal static bool TryResolvePipePassConnectionDirection(
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
}
