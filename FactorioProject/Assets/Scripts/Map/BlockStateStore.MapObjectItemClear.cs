using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    public struct MapObjectItemClearResult
    {
        public int StoredItems;
        public int RobotArmItems;
        public int PendingOutputItems;
        public int ProductionStates;
        public int FuelStates;
        public int ObjectsProcessed;
    }

    private static readonly int[] EmptyPersistentItemIds = Array.Empty<int>();
    private readonly List<int> mapObjectItemClearItemIds = new List<int>();
    private readonly HashSet<Vector2Int> mapObjectItemClearLiveKeys = new HashSet<Vector2Int>();

    public MapObjectItemClearResult ClearMapObjectItems()
    {
        MapObjectItemClearResult result = default;
        RobotArmWorld.Current?.FlushSaveStates();
        VirtualObjectWorld world = ResolveVirtualObjectWorld();
        mapObjectItemClearLiveKeys.Clear();

        foreach (KeyValuePair<Vector2Int, LiveInstallationRecord> pair in liveInstallationStates)
        {
            LiveInstallationRecord liveRecord = pair.Value;
            InstallationObject installationObject = liveRecord?.installationObject;
            if (installationObject == null)
            {
                continue;
            }

            mapObjectItemClearLiveKeys.Add(pair.Key);
            CountAndClearLiveMapObjectItems(installationObject, ref result);
            result.ObjectsProcessed++;

            if (TryBuildInstallationState(installationObject, out InstallationSaveState refreshedState))
            {
                AssignInstallationStorageKey(refreshedState, pair.Key);
                InstallationSaveState savedState = refreshedState.Clone();
                InstallationSaveState liveState = refreshedState.Clone();
                savedInstallationStates[pair.Key] = savedState;
                liveRecord.state = liveState;
                liveRecord.handle = world?.UpsertInstallationHandle(
                    liveState,
                    VirtualObjectResidency.Live,
                    installationObject) ?? default;
                installationObject.BindRuntimeMapObjectHandle(liveRecord.handle);
                continue;
            }

            ClearSavedMapObjectItems(liveRecord.state, false, ref result);
            if (savedInstallationStates.TryGetValue(pair.Key, out InstallationSaveState savedFallback))
            {
                ClearSavedMapObjectItems(savedFallback, false, ref result);
                world?.UpsertInstallation(savedFallback, VirtualObjectResidency.Live, installationObject);
            }
        }

        foreach (KeyValuePair<Vector2Int, InstallationSaveState> pair in savedInstallationStates)
        {
            if (mapObjectItemClearLiveKeys.Contains(pair.Key) || pair.Value == null)
            {
                continue;
            }

            ClearSavedMapObjectItems(pair.Value, true, ref result);
            result.ObjectsProcessed++;
            world?.UpsertInstallation(pair.Value);
        }

        mapObjectItemClearItemIds.Clear();
        RobotArmWorld.Current?.ClearItems();
        mapObjectItemClearLiveKeys.Clear();
        return result;
    }

    private void CountAndClearLiveMapObjectItems(
        InstallationObject installationObject,
        ref MapObjectItemClearResult result)
    {
        if (installationObject is IPersistentInstallationItemStorage itemStorage)
        {
            if (itemStorage.PersistentStoredItemId >= 0)
            {
                result.StoredItems++;
            }

            itemStorage.ApplyPersistentStoredItemId(-1);
        }

        if (installationObject is IPersistentInstallationItemCollectionStorage collectionStorage)
        {
            mapObjectItemClearItemIds.Clear();
            collectionStorage.CapturePersistentStoredItemIds(mapObjectItemClearItemIds);
            result.StoredItems += CountValidPersistentItemIds(mapObjectItemClearItemIds);
            collectionStorage.ApplyPersistentStoredItemIds(EmptyPersistentItemIds);
        }

        if (installationObject is RobotArm robotArm)
        {
            if (robotArm.HeldItemId >= 0)
            {
                result.RobotArmItems++;
            }

            robotArm.ClearHeldItemAndTransferState();
        }

        if (installationObject is InputOutputModule inputOutputModule)
        {
            InputOutputModule.PersistentState state = inputOutputModule.CapturePersistentState();
            CountInputOutputState(state, ref result);
            inputOutputModule.ClearStoredEnergyAndProduction();
        }

        if (installationObject is SteamTrain steamTrain)
        {
            steamTrain.CaptureBurnEnergyStateUnits(out long storedEnergyUnits, out _);
            if (storedEnergyUnits > 0L)
            {
                result.FuelStates++;
            }

            steamTrain.ClearBurnEnergyState();
        }
    }

    private static void ClearSavedMapObjectItems(
        InstallationSaveState state,
        bool countBeforeClear,
        ref MapObjectItemClearResult result)
    {
        if (state == null)
        {
            return;
        }

        if (countBeforeClear)
        {
            if (state.storedInstallationItemId >= 0)
            {
                result.StoredItems++;
            }

            result.StoredItems += CountValidPersistentItemIds(state.storedInstallationItemIds);
            if (state.robotArmState?.heldItemId >= 0)
            {
                result.RobotArmItems++;
            }

            CountInputOutputState(state.inputOutputState, ref result);
            long trainStoredEnergyUnits = state.hasDeterministicUnits
                ? Math.Max(0L, state.steamTrainStoredBurnEnergyUnits)
                : DeterministicSimulationUnits.FromFloat(state.steamTrainStoredBurnEnergy);
            if (state.hasSteamTrainBurnEnergyState && trainStoredEnergyUnits > 0L)
            {
                result.FuelStates++;
            }
        }

        state.storedInstallationItemId = -1;
        state.storedInstallationItemIds ??= new List<int>();
        state.storedInstallationItemIds.Clear();
        state.robotArmState = null;
        state.inputOutputState?.ClearStoredEnergyAndProduction();
        if (state.hasSteamTrainBurnEnergyState)
        {
            state.steamTrainStoredBurnEnergy = 0f;
            state.steamTrainBurnEnergyGaugeCapacity = 0f;
            state.steamTrainStoredBurnEnergyUnits = 0L;
            state.steamTrainBurnEnergyGaugeCapacityUnits = 0L;
            state.hasDeterministicUnits = true;
        }
    }

    private static void CountInputOutputState(
        InputOutputModule.PersistentState state,
        ref MapObjectItemClearResult result)
    {
        if (state == null)
        {
            return;
        }

        if (state.activeOutputCount > 0 && state.activeOutputItemId >= 0)
        {
            result.PendingOutputItems += state.activeOutputCount;
        }

        long storedEnergyUnits = state.hasDeterministicUnits
            ? Math.Max(0L, state.storedEnergyUnits)
            : DeterministicSimulationUnits.FromFloat(state.storedEnergy);
        if (storedEnergyUnits > 0L)
        {
            result.FuelStates++;
        }

        if (HasProductionState(state))
        {
            result.ProductionStates++;
        }
    }

    private static bool HasProductionState(InputOutputModule.PersistentState state)
    {
        return state.hasActiveCraft
               || state.waitingForOutput
               || state.remainingCraftTicks > 0L
               || state.remainingCraftTime > 0f
               || state.activeCraftConsumedEnergyUnits > 0L
               || state.activeCraftConsumedEnergy > 0f
               || state.activeOutputItemId >= 0
               || state.activeOutputCount > 0
               || state.oilDrillingProgressUnits > 0L
               || state.oilDrillingProgressLiters > 0f
               || state.seedPlanterPlantElapsedUnits > 0L
               || state.seedPlanterPlantElapsedSeconds > 0f
               || state.steamGeneratorHasGenerationReserve;
    }

    private static int CountValidPersistentItemIds(IReadOnlyList<int> itemIds)
    {
        if (itemIds == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < itemIds.Count; i++)
        {
            if (itemIds[i] >= 0)
            {
                count++;
            }
        }

        return count;
    }
}
