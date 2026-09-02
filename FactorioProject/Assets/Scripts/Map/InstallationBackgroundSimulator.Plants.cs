using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using UnityEngine;
using ProjectTree = ProjectF.MapObjects.Tree;

public partial class InstallationBackgroundSimulator
{
    private const float BackgroundWaterEpsilon = 0.0001f;
    private const int MaxSavedFluidSearchNodes = 256;
    private static readonly Vector2Int[] BackgroundCardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };
    private static readonly Vector2Int[] BackgroundAdjacentPlantOffsets =
    {
        new Vector2Int(-1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(1, 1),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1)
    };

    private readonly List<Vector2Int> savedWorldInstallationKeys = new List<Vector2Int>(128);
    private readonly List<Vector2Int> savedWorldResourceCoordinates = new List<Vector2Int>(256);
    private readonly List<Vector2Int> savedSprayCoordinates = new List<Vector2Int>(64);
    private readonly List<BlockStateStore.InstallationSaveState> savedFluidInteractionStates =
        new List<BlockStateStore.InstallationSaveState>(8);
    private readonly Queue<Vector2Int> savedFluidSearchQueue = new Queue<Vector2Int>(32);
    private readonly HashSet<Vector2Int> savedFluidSearchVisited = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> savedFluidStorageKeys = new HashSet<Vector2Int>();

    public void SimulateSavedWorld(int maxIterationsOverride = -1)
    {
        if (!Application.isPlaying || !TryGetStateStore(out BlockStateStore stateStore))
        {
            return;
        }

        stateStore.CollectSavedInstallationStorageKeys(savedWorldInstallationKeys);
        SimulateSavedInstallationPass<Pump>(stateStore, maxIterationsOverride);
        SimulateSavedInstallationPass<Sprinkler>(stateStore, maxIterationsOverride);
        SimulateSavedInstallationPass<InputOutputModule>(
            stateStore,
            maxIterationsOverride,
            skipFluidUtilities: true);
        SimulateSavedRobotArms(stateStore, maxIterationsOverride);
        SimulateSavedPlantGrowth(stateStore);
    }

    private void SimulateSavedInstallationPass<TModule>(
        BlockStateStore stateStore,
        int maxIterationsOverride,
        bool skipFluidUtilities = false)
        where TModule : InputOutputModule
    {
        for (int i = 0; i < savedWorldInstallationKeys.Count; i++)
        {
            Vector2Int storageKey = savedWorldInstallationKeys[i];
            if (!stateStore.TryGetInstallationState(
                    storageKey,
                    out BlockStateStore.InstallationSaveState state)
                || !TryResolveTemplateModule(state.itemId, out _, out InputOutputModule templateModule)
                || !(templateModule is TModule)
                || skipFluidUtilities && (templateModule is Pump || templateModule is Sprinkler))
            {
                continue;
            }

            SimulateSavedInstallation(storageKey, maxIterationsOverride);
        }
    }

    private void SimulateSavedRobotArms(
        BlockStateStore stateStore,
        int maxIterationsOverride)
    {
        for (int i = 0; i < savedWorldInstallationKeys.Count; i++)
        {
            Vector2Int storageKey = savedWorldInstallationKeys[i];
            if (!stateStore.TryGetInstallationState(
                    storageKey,
                    out BlockStateStore.InstallationSaveState state)
                || state.robotArmState == null)
            {
                continue;
            }

            SimulateSavedInstallation(storageKey, maxIterationsOverride);
        }
    }

    private bool TrySimulateSavedFluidUtility(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        ItemDefinition installedDefinition,
        InputOutputModule templateModule,
        double elapsedSeconds,
        long nowTicks,
        int maxIterationsOverride)
    {
        if (templateModule is Pump)
        {
            SimulateSavedPump(
                stateStore,
                installationState,
                installedDefinition,
                elapsedSeconds);
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return true;
        }

        if (templateModule is Sprinkler)
        {
            bool hasDeferredSimulation = SimulateSavedSprinkler(
                stateStore,
                installationState,
                installedDefinition,
                elapsedSeconds,
                maxIterationsOverride,
                out double simulatedSeconds);
            installationState.lastBackgroundSimulationTicks = hasDeferredSimulation
                ? Math.Min(
                    nowTicks,
                    installationState.lastBackgroundSimulationTicks
                    + TimeSpan.FromSeconds(simulatedSeconds).Ticks)
                : nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return true;
        }

        return false;
    }

    private bool TrySimulateSavedSeedPlanter(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        ItemDefinition installedDefinition,
        InputOutputModule templateModule,
        double elapsedSeconds,
        long nowTicks)
    {
        if (!(templateModule is SeedPlanter)
            || stateStore == null
            || installationState == null)
        {
            return false;
        }

        InputOutputModule.PersistentState inputOutputState = installationState.inputOutputState;
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (inputOutputState == null
            || terrain == null
            || inputOutputState.outputCoordinates == null
            || inputOutputState.outputCoordinates.Count <= 0)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return true;
        }

        Vector2Int targetCoordinate = inputOutputState.outputCoordinates[0];
        if (!inputOutputState.seedPlanterHadOperationalPower)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return true;
        }

        if (!TryResolveSavedSeedInput(
                stateStore,
                inputOutputState,
                out Vector2Int inputCoordinate,
                out ItemDefinition seedDefinition))
        {
            inputOutputState.seedPlanterPlantElapsedSeconds = 0f;
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return true;
        }

        if (!terrain.CanPlantSeedAt(targetCoordinate, seedDefinition))
        {
            inputOutputState.seedPlanterPlantElapsedSeconds = 0f;
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return true;
        }

        float duration = SeedPlanter.ResolvePlantDuration(installedDefinition);
        inputOutputState.seedPlanterPlantElapsedSeconds = Mathf.Min(
            duration,
            inputOutputState.seedPlanterPlantElapsedSeconds
            + (float)Math.Min(float.MaxValue, elapsedSeconds));
        if (inputOutputState.seedPlanterPlantElapsedSeconds + BackgroundWaterEpsilon >= duration
            && terrain.TryPlantSeedAt(targetCoordinate, seedDefinition)
            && stateStore.RemoveSavedCenterItems(inputCoordinate, seedDefinition.id, 1) == 1)
        {
            inputOutputState.seedPlanterPlantElapsedSeconds = 0f;
        }

        installationState.lastBackgroundSimulationTicks = nowTicks;
        stateStore.UpdateInstallationState(installationState);
        return true;
    }

    private static bool TryResolveSavedSeedInput(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState inputOutputState,
        out Vector2Int inputCoordinate,
        out ItemDefinition seedDefinition)
    {
        inputCoordinate = default;
        seedDefinition = null;
        if (stateStore == null || inputOutputState?.inputItemAreas == null)
        {
            return false;
        }

        for (int i = 0; i < inputOutputState.inputItemAreas.Count; i++)
        {
            InputOutputModule.PersistentInputItemAreaState inputArea = inputOutputState.inputItemAreas[i];
            ItemDefinition candidate = InputOutputModule.ResolveItemDefinition(inputArea.itemId);
            if (!ItemDefinition.IsPlantableSeedDefinition(candidate)
                || stateStore.GetSavedCenterItemCount(inputArea.coordinate, inputArea.itemId) <= 0)
            {
                continue;
            }

            inputCoordinate = inputArea.coordinate;
            seedDefinition = candidate;
            return true;
        }

        return false;
    }

    private void SimulateSavedPump(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState pumpState,
        ItemDefinition pumpDefinition,
        double elapsedSeconds)
    {
        InputOutputModule.PersistentState inputOutputState = pumpState.inputOutputState;
        int waterItemId = Pump.ResolveWaterItemId(null);
        float litersPerSecond = pumpDefinition != null
            ? pumpDefinition.FluidOutputLitersPerSecond
            : 0f;
        if (inputOutputState == null
            || inputOutputState.outputCoordinates == null
            || waterItemId < 0
            || litersPerSecond <= 0f
            || elapsedSeconds <= 0d)
        {
            return;
        }

        float remainingLiters = (float)Math.Min(
            float.MaxValue,
            elapsedSeconds * litersPerSecond);
        for (int i = 0;
             i < inputOutputState.outputCoordinates.Count
             && remainingLiters > BackgroundWaterEpsilon;
             i++)
        {
            remainingLiters -= RouteWaterThroughSavedNetwork(
                stateStore,
                pumpState,
                inputOutputState.outputCoordinates[i],
                waterItemId,
                remainingLiters);
        }

        Pump pumpTemplate = pumpDefinition.mapObject as Pump;
        Quaternion pumpRotation = pumpTemplate != null
            ? pumpTemplate.transform.rotation
              * Quaternion.Euler(0f, pumpState.quarterTurns * 90f, 0f)
            : Quaternion.identity;
        if (remainingLiters > BackgroundWaterEpsilon
            && pumpTemplate != null
            && pumpTemplate.TryGetPipeConnectionDirection(
                pumpRotation,
                out Vector2Int pipeDirection))
        {
            remainingLiters -= RouteWaterThroughSavedNetwork(
                stateStore,
                pumpState,
                pumpState.anchorCoordinate + pipeDirection,
                waterItemId,
                remainingLiters);
        }
    }

    private float RouteWaterThroughSavedNetwork(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState sourceState,
        Vector2Int startCoordinate,
        int waterItemId,
        float requestedLiters)
    {
        if (requestedLiters <= BackgroundWaterEpsilon)
        {
            return 0f;
        }

        savedFluidSearchQueue.Clear();
        savedFluidSearchVisited.Clear();
        savedFluidStorageKeys.Clear();
        EnqueueSavedFluidCoordinate(startCoordinate);
        float remaining = requestedLiters;
        int searchedNodes = 0;
        while (savedFluidSearchQueue.Count > 0
               && remaining > BackgroundWaterEpsilon
               && searchedNodes < MaxSavedFluidSearchNodes)
        {
            Vector2Int coordinate = savedFluidSearchQueue.Dequeue();
            searchedNodes++;
            remaining -= AddWaterToSavedStorageCandidatesAtCoordinate(
                stateStore,
                sourceState,
                coordinate,
                waterItemId,
                remaining);

            if (!stateStore.TryGetSavedPipeInstallationStateAtCoordinate(
                    coordinate,
                    out BlockStateStore.InstallationSaveState pipeState))
            {
                continue;
            }

            for (int directionIndex = 0;
                 directionIndex < BackgroundCardinalDirections.Length;
                 directionIndex++)
            {
                Vector2Int direction = BackgroundCardinalDirections[directionIndex];
                if (!HasSavedPipeConnection(pipeState, coordinate, direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                remaining -= AddWaterToSavedStorageCandidatesAtCoordinate(
                    stateStore,
                    sourceState,
                    nextCoordinate,
                    waterItemId,
                    remaining);
                if (stateStore.TryGetSavedPipeInstallationStateAtCoordinate(
                        nextCoordinate,
                        out BlockStateStore.InstallationSaveState nextPipeState)
                    && HasSavedPipeConnection(nextPipeState, nextCoordinate, -direction))
                {
                    EnqueueSavedFluidCoordinate(nextCoordinate);
                }
            }

            if (TryGetSavedUndergroundRemoteCoordinate(
                    pipeState,
                    coordinate,
                    out Vector2Int remoteCoordinate))
            {
                EnqueueSavedFluidCoordinate(remoteCoordinate);
            }
        }

        ClearSavedFluidSearchScratch();
        return requestedLiters - remaining;
    }

    private float AddWaterToSavedStorageCandidatesAtCoordinate(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState sourceState,
        Vector2Int coordinate,
        int waterItemId,
        float requestedLiters)
    {
        if (requestedLiters <= BackgroundWaterEpsilon)
        {
            return 0f;
        }

        savedFluidInteractionStates.Clear();
        stateStore.CollectSavedInstallationStatesAtInteractionCoordinate(
            coordinate,
            savedFluidInteractionStates);
        float remaining = requestedLiters;
        for (int i = 0;
             i < savedFluidInteractionStates.Count && remaining > BackgroundWaterEpsilon;
             i++)
        {
            BlockStateStore.InstallationSaveState candidate = savedFluidInteractionStates[i];
            if (candidate == null
                || candidate.placementSequence == sourceState.placementSequence
                || !savedFluidStorageKeys.Add(
                    BlockStateStore.GetInstallationStorageKey(candidate))
                || candidate.storedFluidItemId >= 0
                && candidate.storedFluidItemId != waterItemId)
            {
                continue;
            }

            ItemDefinition candidateDefinition = ResolveItemDefinition(candidate.itemId);
            float capacity = candidateDefinition != null && candidateDefinition.storesFluid
                ? Mathf.Max(0f, candidateDefinition.fluidStorageLiters)
                : 0f;
            float accepted = Mathf.Min(
                remaining,
                Mathf.Max(0f, capacity - candidate.storedFluidLiters));
            if (accepted <= BackgroundWaterEpsilon)
            {
                continue;
            }

            BlockStateStore.InstallationSaveState updated = candidate.Clone();
            updated.storedFluidItemId = waterItemId;
            updated.storedFluidLiters = Mathf.Min(capacity, updated.storedFluidLiters + accepted);
            updated.storedFluidTemperatureCelsius = MapClimate.CurrentWaterTemperatureCelsius;
            stateStore.UpdateInstallationState(updated);
            remaining -= accepted;
        }

        savedFluidInteractionStates.Clear();
        return requestedLiters - remaining;
    }

    private bool SimulateSavedSprinkler(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState sprinklerState,
        ItemDefinition sprinklerDefinition,
        double elapsedSeconds,
        int maxIterationsOverride,
        out double simulatedSeconds)
    {
        simulatedSeconds = 0d;
        InputOutputModule.PersistentState inputOutputState = sprinklerState.inputOutputState;
        int waterItemId = Pump.ResolveWaterItemId(null);
        if (inputOutputState == null || sprinklerDefinition == null || waterItemId < 0)
        {
            return false;
        }

        BuildSavedSprayCoordinates(
            sprinklerState.anchorCoordinate,
            Mathf.Max(0, sprinklerDefinition.sprinklerRangeRadius));
        float waterPerCell = Mathf.Max(
            0.001f,
            sprinklerDefinition.sprinklerWaterLitersPerCell);
        float waterRequired = savedSprayCoordinates.Count * waterPerCell;
        float interval = Mathf.Max(
            0.1f,
            sprinklerDefinition.sprinklerSprayIntervalSeconds);
        if (waterRequired <= BackgroundWaterEpsilon || elapsedSeconds <= 0d)
        {
            return false;
        }

        double remainingElapsed = elapsedSeconds;
        float sprayElapsed = Mathf.Clamp(
            inputOutputState.sprinklerSprayElapsedSeconds,
            0f,
            interval);
        int iterationLimit = maxIterationsOverride > 0
            ? maxIterationsOverride
            : Mathf.Max(1, maxCraftIterationsPerSimulation);
        int iterations = 0;
        while (remainingElapsed > 0.0001d && iterations < iterationLimit)
        {
            PullSavedWaterIntoSprinkler(
                stateStore,
                sprinklerState,
                sprinklerDefinition,
                waterItemId);
            if (!HasSavedWateringTarget(stateStore)
                || sprinklerState.storedFluidItemId != waterItemId
                || sprinklerState.storedFluidLiters + BackgroundWaterEpsilon < waterRequired)
            {
                break;
            }

            double timeToSpray = Math.Max(0d, interval - sprayElapsed);
            if (remainingElapsed + 0.0001d < timeToSpray)
            {
                sprayElapsed += (float)remainingElapsed;
                remainingElapsed = 0d;
                break;
            }

            remainingElapsed = Math.Max(0d, remainingElapsed - timeToSpray);
            sprayElapsed = 0f;
            sprinklerState.storedFluidLiters = Mathf.Max(
                0f,
                sprinklerState.storedFluidLiters - waterRequired);
            if (sprinklerState.storedFluidLiters <= BackgroundWaterEpsilon)
            {
                sprinklerState.storedFluidLiters = 0f;
                sprinklerState.storedFluidItemId = -1;
            }

            ApplySavedSpray(stateStore, waterPerCell);
            iterations++;
        }

        inputOutputState.sprinklerSprayElapsedSeconds = Mathf.Clamp(
            sprayElapsed,
            0f,
            interval);
        simulatedSeconds = Math.Max(0d, elapsedSeconds - remainingElapsed);
        return iterations >= iterationLimit && remainingElapsed > 0.0001d;
    }

    private void PullSavedWaterIntoSprinkler(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState sprinklerState,
        ItemDefinition sprinklerDefinition,
        int waterItemId)
    {
        InputOutputModule.PersistentState inputOutputState = sprinklerState.inputOutputState;
        float capacity = sprinklerDefinition.storesFluid
            ? Mathf.Max(0f, sprinklerDefinition.fluidStorageLiters)
            : 0f;
        float remainingCapacity = Mathf.Max(0f, capacity - sprinklerState.storedFluidLiters);
        if (inputOutputState?.pipeInputCoordinates == null
            || remainingCapacity <= BackgroundWaterEpsilon)
        {
            return;
        }

        savedFluidSearchQueue.Clear();
        savedFluidSearchVisited.Clear();
        savedFluidStorageKeys.Clear();
        for (int coordinateIndex = 0;
             coordinateIndex < inputOutputState.pipeInputCoordinates.Count;
             coordinateIndex++)
        {
            EnqueueSavedFluidCoordinate(inputOutputState.pipeInputCoordinates[coordinateIndex]);
        }

        int searchedNodes = 0;
        while (savedFluidSearchQueue.Count > 0
               && remainingCapacity > BackgroundWaterEpsilon
               && searchedNodes < MaxSavedFluidSearchNodes)
        {
            Vector2Int coordinate = savedFluidSearchQueue.Dequeue();
            searchedNodes++;
            remainingCapacity -= PullWaterFromSavedStorageCandidatesAtCoordinate(
                stateStore,
                sprinklerState,
                coordinate,
                waterItemId,
                remainingCapacity);

            if (!stateStore.TryGetSavedPipeInstallationStateAtCoordinate(
                    coordinate,
                    out BlockStateStore.InstallationSaveState pipeState))
            {
                continue;
            }

            for (int directionIndex = 0;
                 directionIndex < BackgroundCardinalDirections.Length;
                 directionIndex++)
            {
                Vector2Int direction = BackgroundCardinalDirections[directionIndex];
                if (!HasSavedPipeConnection(pipeState, coordinate, direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                remainingCapacity -= PullWaterFromSavedStorageCandidatesAtCoordinate(
                    stateStore,
                    sprinklerState,
                    nextCoordinate,
                    waterItemId,
                    remainingCapacity);
                if (stateStore.TryGetSavedPipeInstallationStateAtCoordinate(
                        nextCoordinate,
                        out BlockStateStore.InstallationSaveState nextPipeState)
                    && HasSavedPipeConnection(nextPipeState, nextCoordinate, -direction))
                {
                    EnqueueSavedFluidCoordinate(nextCoordinate);
                }
            }

            if (TryGetSavedUndergroundRemoteCoordinate(
                    pipeState,
                    coordinate,
                    out Vector2Int remoteCoordinate))
            {
                EnqueueSavedFluidCoordinate(remoteCoordinate);
            }
        }

        ClearSavedFluidSearchScratch();
    }

    private float PullWaterFromSavedStorageCandidatesAtCoordinate(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState sprinklerState,
        Vector2Int coordinate,
        int waterItemId,
        float requestedLiters)
    {
        if (requestedLiters <= BackgroundWaterEpsilon)
        {
            return 0f;
        }

        savedFluidInteractionStates.Clear();
        stateStore.CollectSavedInstallationStatesAtInteractionCoordinate(
            coordinate,
            savedFluidInteractionStates);
        float remaining = requestedLiters;
        for (int stateIndex = 0;
             stateIndex < savedFluidInteractionStates.Count
             && remaining > BackgroundWaterEpsilon;
             stateIndex++)
        {
            BlockStateStore.InstallationSaveState source = savedFluidInteractionStates[stateIndex];
            if (source == null
                || source.placementSequence == sprinklerState.placementSequence
                || !savedFluidStorageKeys.Add(
                    BlockStateStore.GetInstallationStorageKey(source))
                || source.storedFluidItemId != waterItemId
                || source.storedFluidLiters <= BackgroundWaterEpsilon)
            {
                continue;
            }

            float transferred = Mathf.Min(remaining, source.storedFluidLiters);
            BlockStateStore.InstallationSaveState updatedSource = source.Clone();
            updatedSource.storedFluidLiters = Mathf.Max(
                0f,
                updatedSource.storedFluidLiters - transferred);
            if (updatedSource.storedFluidLiters <= BackgroundWaterEpsilon)
            {
                updatedSource.storedFluidLiters = 0f;
                updatedSource.storedFluidItemId = -1;
            }

            stateStore.UpdateInstallationState(updatedSource);
            sprinklerState.storedFluidItemId = waterItemId;
            sprinklerState.storedFluidLiters += transferred;
            remaining -= transferred;
        }

        savedFluidInteractionStates.Clear();
        return requestedLiters - remaining;
    }

    private void EnqueueSavedFluidCoordinate(Vector2Int coordinate)
    {
        if (savedFluidSearchVisited.Add(coordinate))
        {
            savedFluidSearchQueue.Enqueue(coordinate);
        }
    }

    private void ClearSavedFluidSearchScratch()
    {
        savedFluidInteractionStates.Clear();
        savedFluidSearchQueue.Clear();
        savedFluidSearchVisited.Clear();
        savedFluidStorageKeys.Clear();
    }

    private static bool HasSavedPipeConnection(
        BlockStateStore.InstallationSaveState pipeState,
        Vector2Int coordinate,
        Vector2Int direction)
    {
        if (pipeState == null || direction == Vector2Int.zero)
        {
            return false;
        }

        if (IsSavedUndergroundPipe(pipeState)
            && TryGetSavedUndergroundOutwardDirection(
                pipeState,
                coordinate,
                out Vector2Int outwardDirection))
        {
            return direction == outwardDirection;
        }

        int directionIndex = GetCardinalDirectionIndex(direction);
        if (directionIndex < 0)
        {
            return false;
        }

        if (pipeState.pipeConnectionMask >= 0)
        {
            return (pipeState.pipeConnectionMask & (1 << directionIndex)) != 0;
        }

        ItemDefinition definition = ResolveItemDefinition(pipeState.itemId);
        Pipe pipe = definition != null ? definition.mapObject as Pipe : null;
        if (pipe == null)
        {
            return false;
        }

        Pipe variant = ResolveSavedPipeVariant(pipe, pipeState.conveyorVariantKind);
        Quaternion rotation = variant.transform.rotation
                              * Quaternion.Euler(0f, pipeState.quarterTurns * 90f, 0f);
        return variant.HasConnectionTowards(rotation, direction);
    }

    private static Pipe ResolveSavedPipeVariant(Pipe pipe, int variantKind)
    {
        if (pipe == null)
        {
            return null;
        }

        Pipe variant = variantKind switch
        {
            (int)PipeVariantKind.Corner => pipe.CornerVariantPrefab,
            (int)PipeVariantKind.Tee => pipe.TeeVariantPrefab,
            (int)PipeVariantKind.Cross => pipe.CrossVariantPrefab,
            _ => pipe.StraightVariantPrefab
        };
        return variant != null ? variant : pipe;
    }

    private static bool TryGetSavedUndergroundRemoteCoordinate(
        BlockStateStore.InstallationSaveState pipeState,
        Vector2Int coordinate,
        out Vector2Int remoteCoordinate)
    {
        remoteCoordinate = default;
        if (!IsSavedUndergroundPipe(pipeState)
            || pipeState.occupiedCoordinates == null
            || pipeState.occupiedCoordinates.Count != 2)
        {
            return false;
        }

        Vector2Int first = pipeState.occupiedCoordinates[0];
        Vector2Int second = pipeState.occupiedCoordinates[1];
        if (coordinate == first)
        {
            remoteCoordinate = second;
            return true;
        }

        if (coordinate == second)
        {
            remoteCoordinate = first;
            return true;
        }

        return false;
    }

    private static bool TryGetSavedUndergroundOutwardDirection(
        BlockStateStore.InstallationSaveState pipeState,
        Vector2Int coordinate,
        out Vector2Int outwardDirection)
    {
        outwardDirection = Vector2Int.zero;
        if (pipeState?.occupiedCoordinates == null
            || pipeState.occupiedCoordinates.Count != 2)
        {
            return false;
        }

        Vector2Int first = pipeState.occupiedCoordinates[0];
        Vector2Int second = pipeState.occupiedCoordinates[1];
        Vector2Int tunnelDirection;
        if (coordinate == first)
        {
            tunnelDirection = second - first;
        }
        else if (coordinate == second)
        {
            tunnelDirection = first - second;
        }
        else
        {
            return false;
        }

        if (Mathf.Abs(tunnelDirection.x) >= Mathf.Abs(tunnelDirection.y))
        {
            outwardDirection = new Vector2Int(-Math.Sign(tunnelDirection.x), 0);
        }
        else
        {
            outwardDirection = new Vector2Int(0, -Math.Sign(tunnelDirection.y));
        }

        return outwardDirection != Vector2Int.zero;
    }

    private static bool IsSavedUndergroundPipe(
        BlockStateStore.InstallationSaveState pipeState)
    {
        ItemDefinition definition = pipeState != null
            ? ResolveItemDefinition(pipeState.itemId)
            : null;
        return definition != null && definition.mapObject is UndergroundPipe;
    }

    private static int GetCardinalDirectionIndex(Vector2Int direction)
    {
        if (direction == Vector2Int.up)
        {
            return 0;
        }

        if (direction == Vector2Int.right)
        {
            return 1;
        }

        if (direction == Vector2Int.down)
        {
            return 2;
        }

        return direction == Vector2Int.left ? 3 : -1;
    }

    private void BuildSavedSprayCoordinates(Vector2Int center, int radiusCells)
    {
        savedSprayCoordinates.Clear();
        float worldRadius = radiusCells + 0.5f;
        float radiusSqr = worldRadius * worldRadius;
        int coordinateRadius = Mathf.CeilToInt(worldRadius);
        for (int y = -coordinateRadius; y <= coordinateRadius; y++)
        {
            for (int x = -coordinateRadius; x <= coordinateRadius; x++)
            {
                Vector2 offset = new Vector2(x, y);
                if (offset.sqrMagnitude <= radiusSqr + BackgroundWaterEpsilon)
                {
                    savedSprayCoordinates.Add(center + new Vector2Int(x, y));
                }
            }
        }
    }

    private bool HasSavedWateringTarget(BlockStateStore stateStore)
    {
        for (int i = 0; i < savedSprayCoordinates.Count; i++)
        {
            Vector2Int coordinate = savedSprayCoordinates[i];
            if (CanSavedPlantAcceptWater(stateStore, coordinate))
            {
                return true;
            }

            if (!IsBackgroundCoordinateEmptyGround(stateStore, coordinate))
            {
                continue;
            }

            for (int offsetIndex = 0;
                 offsetIndex < BackgroundAdjacentPlantOffsets.Length;
                 offsetIndex++)
            {
                if (CanSavedPlantAcceptWater(
                        stateStore,
                        coordinate + BackgroundAdjacentPlantOffsets[offsetIndex]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ApplySavedSpray(BlockStateStore stateStore, float waterPerCell)
    {
        for (int i = 0; i < savedSprayCoordinates.Count; i++)
        {
            Vector2Int coordinate = savedSprayCoordinates[i];
            if (TryAddSavedPlantWater(stateStore, coordinate, waterPerCell, out _))
            {
                continue;
            }

            if (!IsBackgroundCoordinateEmptyGround(stateStore, coordinate))
            {
                continue;
            }

            float remaining = waterPerCell;
            for (int offsetIndex = 0;
                 offsetIndex < BackgroundAdjacentPlantOffsets.Length
                 && remaining > BackgroundWaterEpsilon;
                 offsetIndex++)
            {
                if (TryAddSavedPlantWater(
                        stateStore,
                        coordinate + BackgroundAdjacentPlantOffsets[offsetIndex],
                        remaining,
                        out float accepted))
                {
                    remaining = Mathf.Max(0f, remaining - accepted);
                }
            }
        }
    }

    private bool CanSavedPlantAcceptWater(BlockStateStore stateStore, Vector2Int coordinate)
    {
        if (TryGetLoadedPlant(coordinate, out ProjectTree loadedTree))
        {
            return loadedTree.CanAcceptGrowthWater;
        }

        return TryResolveSavedPlant(
                   stateStore,
                   coordinate,
                   out _,
                   out _,
                   out ResourceDefinition definition)
               && ResolveRequiredGrowthWater(definition, coordinate, stateStore)
               > BackgroundWaterEpsilon;
    }

    private static bool IsBackgroundCoordinateEmptyGround(
        BlockStateStore stateStore,
        Vector2Int coordinate)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null
            && terrain.TryGetLoadedBlock(coordinate, out Block loadedBlock)
            && loadedBlock != null)
        {
            return loadedBlock.MapObject == null && loadedBlock.Resource == null;
        }

        return stateStore != null && stateStore.IsSavedCoordinateEmptyGround(coordinate);
    }

    private bool TryAddSavedPlantWater(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        float requestedLiters,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (requestedLiters <= BackgroundWaterEpsilon)
        {
            return false;
        }

        if (TryGetLoadedPlant(coordinate, out ProjectTree loadedTree))
        {
            return loadedTree.TryAddGrowthWater(requestedLiters, out acceptedLiters);
        }

        if (!TryResolveSavedPlant(
                stateStore,
                coordinate,
                out int itemId,
                out Resource.ResourceSaveState state,
                out ResourceDefinition definition))
        {
            return false;
        }

        int targetGrowth = ResolveTargetGrowth(state.growth);
        float required = definition.GetGrowthWaterRequirement(targetGrowth);
        float remaining = Mathf.Max(0f, required - state.growthWaterLiters);
        acceptedLiters = Mathf.Min(requestedLiters, remaining);
        if (acceptedLiters <= BackgroundWaterEpsilon)
        {
            acceptedLiters = 0f;
            return false;
        }

        state.growthWaterLiters = Mathf.Min(required, state.growthWaterLiters + acceptedLiters);
        stateStore.UpdateSavedResourceState(coordinate, itemId, state);
        return true;
    }

    private void SimulateSavedPlantGrowth(BlockStateStore stateStore)
    {
        WorldTimeService worldTime = WorldTimeService.Active;
        TerrainGenerator terrainGenerator = TerrainGenerator.ResolveActive();
        if (worldTime == null || terrainGenerator == null)
        {
            return;
        }

        double currentDaylightSeconds = worldTime.PlantGrowthDaylightSeconds;
        stateStore.CollectSavedResourceCoordinates(savedWorldResourceCoordinates);
        for (int i = 0; i < savedWorldResourceCoordinates.Count; i++)
        {
            Vector2Int coordinate = savedWorldResourceCoordinates[i];
            if (TryGetLoadedPlant(coordinate, out _)
                || !TryResolveSavedPlant(
                    stateStore,
                    coordinate,
                    out int itemId,
                    out Resource.ResourceSaveState state,
                    out ResourceDefinition definition))
            {
                continue;
            }

            if (!state.hasBackgroundGrowthTimestamp)
            {
                state.hasBackgroundGrowthTimestamp = true;
                state.backgroundGrowthDaylightSeconds = currentDaylightSeconds;
                bool fertilizerChanged = TryConsumeSavedPlantFertilizer(
                    terrainGenerator,
                    coordinate,
                    definition,
                    ref state);
                stateStore.UpdateSavedResourceState(
                    coordinate,
                    itemId,
                    state,
                    refreshVirtualWorld: fertilizerChanged);
                continue;
            }

            double previousDaylightSeconds = state.backgroundGrowthDaylightSeconds;
            double elapsed = Math.Max(0d, currentDaylightSeconds - previousDaylightSeconds);
            bool growthStateChanged = TryConsumeSavedPlantFertilizer(
                terrainGenerator,
                coordinate,
                definition,
                ref state);
            if (elapsed <= 0d && currentDaylightSeconds >= previousDaylightSeconds)
            {
                if (growthStateChanged)
                {
                    stateStore.UpdateSavedResourceState(
                        coordinate,
                        itemId,
                        state,
                        refreshVirtualWorld: true);
                }

                continue;
            }

            state.backgroundGrowthDaylightSeconds = currentDaylightSeconds;
            if (elapsed > 0d && AreSavedGrowthRequirementsMet(state, definition))
            {
                growthStateChanged = true;
                float duration = definition.GrowthDurationPerLevelSeconds;
                state.growthElapsedSeconds += (float)Math.Min(float.MaxValue, elapsed);
                if (duration > 0f
                    && state.growthElapsedSeconds + BackgroundWaterEpsilon >= duration)
                {
                    state.growth = ResolveTargetGrowth(state.growth);
                    state.growthWaterLiters = 0f;
                    state.growthFertilizerAmount = 0f;
                    state.growthElapsedSeconds = 0f;
                }
                else if (duration > 0f)
                {
                    state.growthElapsedSeconds = Mathf.Min(
                        duration,
                        state.growthElapsedSeconds);
                }
            }

            stateStore.UpdateSavedResourceState(
                coordinate,
                itemId,
                state,
                refreshVirtualWorld: growthStateChanged);
        }
    }

    private static bool TryConsumeSavedPlantFertilizer(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        ResourceDefinition definition,
        ref Resource.ResourceSaveState state)
    {
        if (terrainGenerator == null
            || definition == null
            || !terrainGenerator.IsFarmlandAt(coordinate))
        {
            return false;
        }

        int targetGrowth = ResolveTargetGrowth(state.growth);
        float required = definition.GetGrowthFertilizerRequirement(targetGrowth);
        float requested = Mathf.Max(0f, required - state.growthFertilizerAmount);
        if (requested <= BackgroundWaterEpsilon
            || !terrainGenerator.TryConsumeFarmlandFertilizer(
                coordinate,
                requested,
                out float consumed)
            || consumed <= BackgroundWaterEpsilon)
        {
            return false;
        }

        state.growthFertilizerAmount = Mathf.Min(
            required,
            state.growthFertilizerAmount + consumed);
        return true;
    }

    private static bool TryResolveSavedPlant(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        out int itemId,
        out Resource.ResourceSaveState state,
        out ResourceDefinition definition)
    {
        itemId = -1;
        state = default;
        definition = null;
        if (stateStore == null
            || !stateStore.TryGetSavedResourceState(coordinate, out itemId, out state)
            || !state.hasGrowth
            || !state.hasPlantGrowthState
            || state.resourceCount <= 0
            || state.growth >= ResourceDefinition.MaxGrowth)
        {
            return false;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null
            && terrain.TryGetPlantedSeedDefinitionAt(coordinate, out ItemDefinition seedDefinition))
        {
            definition = seedDefinition.seedTargetResource;
        }
        else
        {
            ItemDefinition itemDefinition = ResolveItemDefinition(itemId);
            ProjectTree templateTree = itemDefinition != null
                ? itemDefinition.mapObject as ProjectTree
                : null;
            definition = templateTree != null ? templateTree.Definition : null;
        }

        return definition != null && definition.HasGrowthSchedule;
    }

    private static bool TryGetLoadedPlant(Vector2Int coordinate, out ProjectTree tree)
    {
        tree = null;
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null)
        {
            return false;
        }

        tree = block.Resource as ProjectTree;
        return tree != null && tree.gameObject.activeInHierarchy;
    }

    private static float ResolveRequiredGrowthWater(
        ResourceDefinition definition,
        Vector2Int coordinate,
        BlockStateStore stateStore)
    {
        if (definition == null
            || stateStore == null
            || !stateStore.TryGetSavedResourceState(coordinate, out _, out Resource.ResourceSaveState state))
        {
            return 0f;
        }

        float required = definition.GetGrowthWaterRequirement(ResolveTargetGrowth(state.growth));
        return Mathf.Max(0f, required - state.growthWaterLiters);
    }

    private static bool AreSavedGrowthRequirementsMet(
        Resource.ResourceSaveState state,
        ResourceDefinition definition)
    {
        int targetGrowth = ResolveTargetGrowth(state.growth);
        return state.growth < ResourceDefinition.MaxGrowth
               && state.growthWaterLiters + BackgroundWaterEpsilon
               >= definition.GetGrowthWaterRequirement(targetGrowth)
               && state.growthFertilizerAmount + BackgroundWaterEpsilon
               >= definition.GetGrowthFertilizerRequirement(targetGrowth);
    }

    private static int ResolveTargetGrowth(float growth)
    {
        return Mathf.Clamp(
            Mathf.FloorToInt(growth) + 1,
            ResourceDefinition.MinGrowth + 1,
            ResourceDefinition.MaxGrowth);
    }
}
