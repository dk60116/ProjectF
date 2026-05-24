using System;
using System.Collections.Generic;
using UnityEngine;

public class InstallationBackgroundSimulator : MonoBehaviour
{
    private const int InputAreaCenterStackStateSentinel = -1000000001;
    private const int ConveyorStackStateSentinel = -1000000002;
    private const int FloorStackStateSentinel = -1000000003;

    [SerializeField, Min(1)]
    private int maxCraftIterationsPerSimulation = 256;

    private BlockStateStore cachedStateStore;

    private sealed class SavedBlockInventory
    {
        public readonly List<int> floorItems = new List<int>();
        public readonly List<int> centerItems = new List<int>();
        public readonly List<int> conveyorLaneItems = new List<int>();
        public bool hasConveyorStack;

        public static SavedBlockInventory FromSerialized(IReadOnlyList<int> itemIds)
        {
            SavedBlockInventory inventory = new SavedBlockInventory();
            if (itemIds == null)
            {
                return inventory;
            }

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                if (itemId == FloorStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        break;
                    }

                    int stackCount = Mathf.Max(0, itemIds[++i]);
                    for (int stackIndex = 0; stackIndex < stackCount && i + 1 < itemIds.Count; stackIndex++)
                    {
                        int stackItemCount = Mathf.Max(0, itemIds[++i]);
                        for (int objectIndex = 0; objectIndex < stackItemCount && i + 1 < itemIds.Count; objectIndex++)
                        {
                            int stackItemId = itemIds[++i];
                            if (stackItemId >= 0)
                            {
                                inventory.floorItems.Add(stackItemId);
                            }
                        }
                    }

                    continue;
                }

                if (itemId == InputAreaCenterStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        break;
                    }

                    int centerCount = Mathf.Max(0, itemIds[++i]);
                    for (int centerIndex = 0; centerIndex < centerCount && i + 1 < itemIds.Count; centerIndex++)
                    {
                        inventory.centerItems.Add(itemIds[++i]);
                    }

                    continue;
                }

                if (itemId == ConveyorStackStateSentinel)
                {
                    if (i + 1 >= itemIds.Count)
                    {
                        break;
                    }

                    inventory.hasConveyorStack = true;
                    int laneCount = Mathf.Max(0, itemIds[++i]);
                    for (int laneIndex = 0; laneIndex < laneCount && i + 1 < itemIds.Count; laneIndex++)
                    {
                        inventory.conveyorLaneItems.Add(itemIds[++i]);
                    }

                    continue;
                }

                if (itemId < 0)
                {
                    continue;
                }

                inventory.floorItems.Add(itemId);
            }

            return inventory;
        }

        public List<int> ToSerialized()
        {
            List<int> itemIds = new List<int>(floorItems.Count + centerItems.Count + conveyorLaneItems.Count + 4);
            itemIds.AddRange(floorItems);

            if (hasConveyorStack)
            {
                itemIds.Add(ConveyorStackStateSentinel);
                itemIds.Add(conveyorLaneItems.Count);
                itemIds.AddRange(conveyorLaneItems);
            }

            if (centerItems.Count > 0)
            {
                itemIds.Add(InputAreaCenterStackStateSentinel);
                itemIds.Add(centerItems.Count);
                itemIds.AddRange(centerItems);
            }

            return itemIds;
        }
    }

    private enum BackgroundRobotArmPickupSource
    {
        None,
        Floor,
        Box,
        Conveyor,
        InputArea
    }

    private struct BackgroundRobotArmPickupCandidate
    {
        public BackgroundRobotArmPickupSource source;
        public Vector2Int coordinate;
        public int itemId;
        public Vector3 worldPosition;
    }

    public void SimulateSavedInstallation(Vector2Int anchorCoordinate, int maxIterationsOverride = -1)
    {
        if (!Application.isPlaying || !TryGetStateStore(out BlockStateStore stateStore))
        {
            return;
        }

        if (stateStore.TryGetLiveInstallation(anchorCoordinate, out _, out _))
        {
            return;
        }

        if (!stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState)
            || installationState == null)
        {
            return;
        }

        if (installationState.robotArmState != null)
        {
            SimulateSavedRobotArm(stateStore, installationState, maxIterationsOverride);
            return;
        }

        if (installationState.inputOutputState == null)
        {
            return;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        if (installationState.lastBackgroundSimulationTicks <= 0)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        double elapsedSeconds = TimeSpan.FromTicks(Math.Max(0L, nowTicks - installationState.lastBackgroundSimulationTicks)).TotalSeconds;
        if (elapsedSeconds <= 0.0001d)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        if (!TryResolveTemplateModule(installationState.itemId, out ItemDefinition installedDefinition, out InputOutputModule templateModule))
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        double remainingElapsed = elapsedSeconds;
        double simulatedSeconds = 0d;
        bool blockedOrIdle = false;
        int iterationCount = 0;
        int iterationLimit = maxIterationsOverride > 0
            ? maxIterationsOverride
            : Mathf.Max(1, maxCraftIterationsPerSimulation);

        while (remainingElapsed > 0.0001d && iterationCount < iterationLimit)
        {
            iterationCount++;

            if (installationState.inputOutputState.hasActiveCraft)
            {
                if (!AdvanceActiveCraft(
                        stateStore,
                        installationState.inputOutputState,
                        templateModule,
                        installedDefinition,
                        ref remainingElapsed,
                        ref simulatedSeconds,
                        out bool blocked))
                {
                    blockedOrIdle = blocked;
                    break;
                }

                continue;
            }

            if (!TryStartNextCraft(stateStore, installationState, installationState.inputOutputState, templateModule, installedDefinition))
            {
                blockedOrIdle = true;
                break;
            }
        }

        bool hitIterationLimit = iterationCount >= iterationLimit && remainingElapsed > 0.0001d && !blockedOrIdle;
        if (hitIterationLimit)
        {
            installationState.lastBackgroundSimulationTicks += TimeSpan.FromSeconds(simulatedSeconds).Ticks;
        }
        else
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
        }

        stateStore.UpdateInstallationState(installationState);
    }

    private void SimulateSavedRobotArm(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        int maxIterationsOverride)
    {
        if (stateStore == null || installationState == null)
        {
            return;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        if (installationState.lastBackgroundSimulationTicks <= 0)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        double elapsedSeconds = TimeSpan.FromTicks(Math.Max(0L, nowTicks - installationState.lastBackgroundSimulationTicks)).TotalSeconds;
        if (elapsedSeconds <= 0.0001d)
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        if (!TryResolveTemplateRobotArm(installationState.itemId, out _, out RobotArm templateRobotArm))
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
            stateStore.UpdateInstallationState(installationState);
            return;
        }

        RobotArm.PersistentState robotState = installationState.robotArmState ?? new RobotArm.PersistentState();
        NormalizeSavedRobotArmState(robotState);

        double remainingElapsed = elapsedSeconds;
        double simulatedSeconds = 0d;
        bool blockedOrIdle = false;
        int iterationCount = 0;
        int iterationLimit = maxIterationsOverride > 0
            ? maxIterationsOverride
            : Mathf.Max(1, maxCraftIterationsPerSimulation);

        while (remainingElapsed > 0.0001d && iterationCount < iterationLimit)
        {
            iterationCount++;
            if (!AdvanceSavedRobotArm(
                    stateStore,
                    installationState,
                    robotState,
                    templateRobotArm,
                    ref remainingElapsed,
                    ref simulatedSeconds,
                    out bool blocked))
            {
                blockedOrIdle = blocked;
                break;
            }
        }

        installationState.robotArmState = robotState;
        bool hitIterationLimit = iterationCount >= iterationLimit && remainingElapsed > 0.0001d && !blockedOrIdle;
        if (hitIterationLimit)
        {
            installationState.lastBackgroundSimulationTicks += TimeSpan.FromSeconds(simulatedSeconds).Ticks;
        }
        else
        {
            installationState.lastBackgroundSimulationTicks = nowTicks;
        }

        stateStore.UpdateInstallationState(installationState);
    }

    private bool AdvanceSavedRobotArm(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        RobotArm.PersistentState robotState,
        RobotArm templateRobotArm,
        ref double remainingElapsed,
        ref double simulatedSeconds,
        out bool blocked)
    {
        blocked = false;
        if (stateStore == null || installationState == null || robotState == null || templateRobotArm == null)
        {
            blocked = true;
            return false;
        }

        switch (robotState.state)
        {
            case RobotArm.RobotArmState.WaitingForPickup:
                if (robotState.heldItemId >= 0)
                {
                    BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToDrop, templateRobotArm);
                    return true;
                }

                if (!ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.pickupTimer))
                {
                    return false;
                }

                if (!CanPickupSavedRobotArmItem(stateStore, installationState, templateRobotArm))
                {
                    robotState.pickupTimer = templateRobotArm.PickupIntervalSeconds;
                    blocked = true;
                    return false;
                }

                robotState.state = RobotArm.RobotArmState.WaitingBeforePickupTake;
                robotState.actionTurnTimer = templateRobotArm.ActionTurnDelaySeconds;
                return true;

            case RobotArm.RobotArmState.WaitingBeforePickupTake:
                if (robotState.heldItemId >= 0)
                {
                    BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToDrop, templateRobotArm);
                    return true;
                }

                if (!ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.actionTurnTimer))
                {
                    return false;
                }

                if (TryPickupSavedRobotArmItem(stateStore, installationState, templateRobotArm, out int pickedItemId))
                {
                    robotState.heldItemId = pickedItemId;
                    robotState.dropRetryTimer = 0f;
                    robotState.waitingForDropRetry = false;
                    robotState.state = RobotArm.RobotArmState.WaitingAfterPickupTake;
                    robotState.actionTurnTimer = templateRobotArm.ActionTurnDelaySeconds;
                    return true;
                }

                robotState.state = RobotArm.RobotArmState.WaitingForPickup;
                robotState.pickupTimer = templateRobotArm.PickupIntervalSeconds;
                blocked = true;
                return false;

            case RobotArm.RobotArmState.WaitingAfterPickupTake:
                if (robotState.heldItemId < 0)
                {
                    robotState.state = RobotArm.RobotArmState.WaitingForPickup;
                    robotState.pickupTimer = templateRobotArm.PickupIntervalSeconds;
                    return true;
                }

                if (!ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.actionTurnTimer))
                {
                    return false;
                }

                BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToDrop, templateRobotArm);
                return true;

            case RobotArm.RobotArmState.TurningToDrop:
                if (robotState.heldItemId < 0)
                {
                    BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToPickup, templateRobotArm);
                    return true;
                }

                EnsureSavedRobotArmTurnTimer(robotState, templateRobotArm);
                if (!ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.turnTimer))
                {
                    return false;
                }

                robotState.state = RobotArm.RobotArmState.WaitingForDrop;
                robotState.waitingForDropRetry = false;
                robotState.dropRetryTimer = 0f;
                return true;

            case RobotArm.RobotArmState.WaitingForDrop:
                if (robotState.heldItemId < 0)
                {
                    BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToPickup, templateRobotArm);
                    return true;
                }

                if (robotState.waitingForDropRetry
                    && !ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.dropRetryTimer))
                {
                    return false;
                }

                robotState.waitingForDropRetry = false;
                if (!CanPlaceSavedRobotArmHeldItem(stateStore, installationState, templateRobotArm, robotState.heldItemId))
                {
                    BeginSavedRobotArmDropRetry(robotState, templateRobotArm);
                    blocked = true;
                    return false;
                }

                robotState.state = RobotArm.RobotArmState.WaitingBeforeDropPlace;
                robotState.actionTurnTimer = templateRobotArm.ActionTurnDelaySeconds;
                return true;

            case RobotArm.RobotArmState.WaitingBeforeDropPlace:
                if (robotState.heldItemId < 0)
                {
                    BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToPickup, templateRobotArm);
                    return true;
                }

                if (!ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.actionTurnTimer))
                {
                    return false;
                }

                if (TryPlaceSavedRobotArmHeldItem(stateStore, installationState, templateRobotArm, robotState.heldItemId))
                {
                    robotState.heldItemId = -1;
                    robotState.dropRetryTimer = 0f;
                    robotState.waitingForDropRetry = false;
                    robotState.state = RobotArm.RobotArmState.WaitingAfterDropPlace;
                    robotState.actionTurnTimer = templateRobotArm.ActionTurnDelaySeconds;
                    return true;
                }

                BeginSavedRobotArmDropRetry(robotState, templateRobotArm);
                blocked = true;
                return false;

            case RobotArm.RobotArmState.WaitingAfterDropPlace:
                if (robotState.heldItemId >= 0)
                {
                    BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToDrop, templateRobotArm);
                    return true;
                }

                if (!ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.actionTurnTimer))
                {
                    return false;
                }

                BeginSavedRobotArmTurn(robotState, RobotArm.RobotArmState.TurningToPickup, templateRobotArm);
                return true;

            case RobotArm.RobotArmState.TurningToPickup:
                EnsureSavedRobotArmTurnTimer(robotState, templateRobotArm);
                if (!ConsumeSavedRobotArmTimer(ref remainingElapsed, ref simulatedSeconds, ref robotState.turnTimer))
                {
                    return false;
                }

                robotState.state = RobotArm.RobotArmState.WaitingForPickup;
                robotState.pickupTimer = templateRobotArm.PickupIntervalSeconds;
                return true;

            default:
                NormalizeSavedRobotArmState(robotState);
                return true;
        }
    }

    private static bool ConsumeSavedRobotArmTimer(ref double remainingElapsed, ref double simulatedSeconds, ref float timer)
    {
        timer = Mathf.Max(0f, timer);
        if (timer <= 0.0001f)
        {
            timer = 0f;
            return true;
        }

        double delta = Math.Min(remainingElapsed, timer);
        if (delta <= 0.0001d)
        {
            return false;
        }

        timer = Mathf.Max(0f, timer - (float)delta);
        remainingElapsed = Math.Max(0d, remainingElapsed - delta);
        simulatedSeconds += delta;
        return timer <= 0.0001f;
    }

    private static void BeginSavedRobotArmTurn(
        RobotArm.PersistentState robotState,
        RobotArm.RobotArmState targetState,
        RobotArm templateRobotArm)
    {
        robotState.state = targetState;
        robotState.turnTimer = ResolveSavedRobotArmTurnDuration(templateRobotArm);
    }

    private static void EnsureSavedRobotArmTurnTimer(RobotArm.PersistentState robotState, RobotArm templateRobotArm)
    {
        if (robotState.turnTimer <= 0.0001f)
        {
            robotState.turnTimer = ResolveSavedRobotArmTurnDuration(templateRobotArm);
        }
    }

    private static float ResolveSavedRobotArmTurnDuration(RobotArm templateRobotArm)
    {
        return templateRobotArm != null
            ? Mathf.Max(0.0001f, templateRobotArm.BackgroundTurnDurationSeconds)
            : 0.3333f;
    }

    private static void BeginSavedRobotArmDropRetry(RobotArm.PersistentState robotState, RobotArm templateRobotArm)
    {
        robotState.state = RobotArm.RobotArmState.WaitingForDrop;
        robotState.dropRetryTimer = templateRobotArm != null ? templateRobotArm.DropRetryIntervalSeconds : 0.1f;
        robotState.waitingForDropRetry = true;
    }

    private static void NormalizeSavedRobotArmState(RobotArm.PersistentState robotState)
    {
        if (robotState == null)
        {
            return;
        }

        if (!Enum.IsDefined(typeof(RobotArm.RobotArmState), robotState.state))
        {
            robotState.state = RobotArm.RobotArmState.WaitingForPickup;
        }

        robotState.pickupTimer = Mathf.Max(0f, robotState.pickupTimer);
        robotState.dropRetryTimer = Mathf.Max(0f, robotState.dropRetryTimer);
        robotState.actionTurnTimer = Mathf.Max(0f, robotState.actionTurnTimer);
        robotState.turnTimer = Mathf.Max(0f, robotState.turnTimer);

        if (robotState.heldItemId < 0)
        {
            robotState.waitingForDropRetry = false;
            robotState.dropRetryTimer = 0f;
            if (robotState.state == RobotArm.RobotArmState.WaitingForDrop
                || robotState.state == RobotArm.RobotArmState.WaitingBeforeDropPlace
                || robotState.state == RobotArm.RobotArmState.TurningToDrop)
            {
                robotState.state = RobotArm.RobotArmState.TurningToPickup;
            }

            return;
        }

        if (robotState.state == RobotArm.RobotArmState.WaitingForPickup
            || robotState.state == RobotArm.RobotArmState.WaitingBeforePickupTake
            || robotState.state == RobotArm.RobotArmState.WaitingAfterDropPlace
            || robotState.state == RobotArm.RobotArmState.TurningToPickup)
        {
            robotState.state = RobotArm.RobotArmState.TurningToDrop;
        }
    }

    private bool CanPickupSavedRobotArmItem(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        RobotArm templateRobotArm)
    {
        return TryResolveSavedRobotArmPickupCandidate(
            stateStore,
            installationState,
            templateRobotArm,
            out _);
    }

    private bool TryPickupSavedRobotArmItem(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        RobotArm templateRobotArm,
        out int pickedItemId)
    {
        pickedItemId = -1;
        if (!TryResolveSavedRobotArmPickupCandidate(
                stateStore,
                installationState,
                templateRobotArm,
                out BackgroundRobotArmPickupCandidate candidate))
        {
            return false;
        }

        Predicate<int> exactItemFilter = itemId => itemId == candidate.itemId
                                                    && SavedInstallationAcceptsItem(installationState, itemId);
        switch (candidate.source)
        {
            case BackgroundRobotArmPickupSource.Floor:
                return TryTakeSavedFloorItem(stateStore, candidate.coordinate, exactItemFilter, out pickedItemId);
            case BackgroundRobotArmPickupSource.Box:
            case BackgroundRobotArmPickupSource.InputArea:
                return TryTakeSavedCenterTopItem(stateStore, candidate.coordinate, exactItemFilter, out pickedItemId);
            case BackgroundRobotArmPickupSource.Conveyor:
                return stateStore != null
                       && stateStore.TryTakeSavedConveyorItem(
                           candidate.coordinate,
                           exactItemFilter,
                           candidate.worldPosition,
                           out pickedItemId);
            default:
                return false;
        }
    }

    private bool TryResolveSavedRobotArmPickupCandidate(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        RobotArm templateRobotArm,
        out BackgroundRobotArmPickupCandidate result)
    {
        result = default;
        if (stateStore == null
            || installationState == null
            || !TryResolveRobotArmInteractionCoordinate(installationState, templateRobotArm, true, out Vector2Int pickupCoordinate))
        {
            return false;
        }

        Vector3 referenceWorldPosition = GetSavedCoordinateWorldPosition(pickupCoordinate);
        Predicate<int> itemFilter = itemId => SavedInstallationAcceptsItem(installationState, itemId);
        bool hasCandidate = false;

        SavedBlockInventory inventory = LoadBlockInventory(stateStore, pickupCoordinate);
        if (TryPeekSavedFloorItem(inventory, itemFilter, out int floorItemId))
        {
            TryChooseBackgroundRobotArmPickupCandidate(
                new BackgroundRobotArmPickupCandidate
                {
                    source = BackgroundRobotArmPickupSource.Floor,
                    coordinate = pickupCoordinate,
                    itemId = floorItemId,
                    worldPosition = GetSavedCoordinateWorldPosition(pickupCoordinate)
                },
                referenceWorldPosition,
                ref result,
                ref hasCandidate);
        }

        bool hasBox = TryResolveSavedBoxAtCoordinate(stateStore, pickupCoordinate, out _, out _);
        if (hasBox && TryPeekSavedCenterTopItem(inventory, itemFilter, out int boxItemId))
        {
            TryChooseBackgroundRobotArmPickupCandidate(
                new BackgroundRobotArmPickupCandidate
                {
                    source = BackgroundRobotArmPickupSource.Box,
                    coordinate = pickupCoordinate,
                    itemId = boxItemId,
                    worldPosition = GetSavedCoordinateWorldPosition(pickupCoordinate)
                },
                referenceWorldPosition,
                ref result,
                ref hasCandidate);
        }

        if (stateStore.TryPeekSavedConveyorItem(
                pickupCoordinate,
                itemFilter,
                referenceWorldPosition,
                out int conveyorItemId,
                out Vector3 conveyorWorldPosition))
        {
            TryChooseBackgroundRobotArmPickupCandidate(
                new BackgroundRobotArmPickupCandidate
                {
                    source = BackgroundRobotArmPickupSource.Conveyor,
                    coordinate = pickupCoordinate,
                    itemId = conveyorItemId,
                    worldPosition = conveyorWorldPosition
                },
                referenceWorldPosition,
                ref result,
                ref hasCandidate);
        }

        if (!hasBox && TryPeekSavedCenterTopItem(inventory, itemFilter, out int inputAreaItemId))
        {
            TryChooseBackgroundRobotArmPickupCandidate(
                new BackgroundRobotArmPickupCandidate
                {
                    source = BackgroundRobotArmPickupSource.InputArea,
                    coordinate = pickupCoordinate,
                    itemId = inputAreaItemId,
                    worldPosition = GetSavedCoordinateWorldPosition(pickupCoordinate)
                },
                referenceWorldPosition,
                ref result,
                ref hasCandidate);
        }

        return hasCandidate;
    }

    private static void TryChooseBackgroundRobotArmPickupCandidate(
        BackgroundRobotArmPickupCandidate candidate,
        Vector3 referenceWorldPosition,
        ref BackgroundRobotArmPickupCandidate bestCandidate,
        ref bool hasBestCandidate)
    {
        Vector3 offset = candidate.worldPosition - referenceWorldPosition;
        offset.y = 0f;
        float candidateDistanceSqr = offset.sqrMagnitude;

        if (hasBestCandidate)
        {
            Vector3 bestOffset = bestCandidate.worldPosition - referenceWorldPosition;
            bestOffset.y = 0f;
            if (candidateDistanceSqr >= bestOffset.sqrMagnitude)
            {
                return;
            }
        }

        bestCandidate = candidate;
        hasBestCandidate = true;
    }

    private bool CanPlaceSavedRobotArmHeldItem(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        RobotArm templateRobotArm,
        int itemId)
    {
        return TryPlaceSavedRobotArmHeldItemInternal(
            stateStore,
            installationState,
            templateRobotArm,
            itemId,
            false);
    }

    private bool TryPlaceSavedRobotArmHeldItem(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        RobotArm templateRobotArm,
        int itemId)
    {
        return TryPlaceSavedRobotArmHeldItemInternal(
            stateStore,
            installationState,
            templateRobotArm,
            itemId,
            true);
    }

    private bool TryPlaceSavedRobotArmHeldItemInternal(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        RobotArm templateRobotArm,
        int itemId,
        bool mutate)
    {
        if (stateStore == null
            || installationState == null
            || itemId < 0
            || !TryResolveRobotArmInteractionCoordinate(installationState, templateRobotArm, false, out Vector2Int dropCoordinate))
        {
            return false;
        }

        if (TryResolveSavedBoxAtCoordinate(stateStore, dropCoordinate, out BlockStateStore.InstallationSaveState boxState, out ItemDefinition boxDefinition))
        {
            return mutate
                ? TryAddSavedBoxItem(stateStore, dropCoordinate, boxState, boxDefinition, itemId)
                : CanAddSavedBoxItem(stateStore, dropCoordinate, boxState, boxDefinition, itemId);
        }

        Vector3 referenceWorldPosition = GetSavedCoordinateWorldPosition(dropCoordinate);
        if (mutate)
        {
            if (stateStore.TryAddSavedConveyorItem(dropCoordinate, itemId, referenceWorldPosition))
            {
                return true;
            }
        }
        else if (stateStore.CanAddSavedConveyorItem(dropCoordinate, itemId, referenceWorldPosition))
        {
            return true;
        }

        if (!CanPlaceSavedSingleLineDrop(stateStore, dropCoordinate)
            || !InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(dropCoordinate, itemId))
        {
            return false;
        }

        int capacity = ResolveBlockCenterCapacity(stateStore, dropCoordinate, 10);
        if (mutate)
        {
            return AddCenterItems(stateStore, dropCoordinate, itemId, 1, capacity);
        }

        return CanAddCenterItems(LoadBlockInventory(stateStore, dropCoordinate), itemId, 1, capacity);
    }

    private static bool CanAddSavedBoxItem(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        BlockStateStore.InstallationSaveState boxState,
        ItemDefinition boxDefinition,
        int itemId)
    {
        if (!SavedInstallationAcceptsItem(boxState, itemId))
        {
            return false;
        }

        int capacity = boxDefinition != null && boxDefinition.capacity > 0 ? boxDefinition.capacity : 10;
        return CanAddCenterItems(LoadBlockInventory(stateStore, coordinate), itemId, 1, capacity);
    }

    private static bool TryAddSavedBoxItem(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        BlockStateStore.InstallationSaveState boxState,
        ItemDefinition boxDefinition,
        int itemId)
    {
        if (!SavedInstallationAcceptsItem(boxState, itemId))
        {
            return false;
        }

        int capacity = boxDefinition != null && boxDefinition.capacity > 0 ? boxDefinition.capacity : 10;
        return AddCenterItems(stateStore, coordinate, itemId, 1, capacity);
    }

    private static bool CanPlaceSavedSingleLineDrop(BlockStateStore stateStore, Vector2Int coordinate)
    {
        return CoordinateAcceptsInputAreaObject(coordinate)
               || stateStore == null
               || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out _);
    }

    private static bool TryPeekSavedFloorItem(
        SavedBlockInventory inventory,
        Predicate<int> itemFilter,
        out int itemId)
    {
        itemId = -1;
        int itemIndex = FindSavedFloorItemIndex(inventory, itemFilter);
        if (itemIndex < 0)
        {
            return false;
        }

        itemId = inventory.floorItems[itemIndex];
        return true;
    }

    private static bool TryTakeSavedFloorItem(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        Predicate<int> itemFilter,
        out int itemId)
    {
        itemId = -1;
        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        int itemIndex = FindSavedFloorItemIndex(inventory, itemFilter);
        if (itemIndex < 0)
        {
            return false;
        }

        itemId = inventory.floorItems[itemIndex];
        inventory.floorItems.RemoveAt(itemIndex);
        SaveBlockInventory(stateStore, coordinate, inventory);
        return true;
    }

    private static int FindSavedFloorItemIndex(SavedBlockInventory inventory, Predicate<int> itemFilter)
    {
        if (inventory == null || inventory.floorItems.Count <= 0)
        {
            return -1;
        }

        for (int i = inventory.floorItems.Count - 1; i >= 0; i--)
        {
            int itemId = inventory.floorItems[i];
            if (itemId >= 0 && (itemFilter == null || itemFilter(itemId)))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryPeekSavedCenterTopItem(
        SavedBlockInventory inventory,
        Predicate<int> itemFilter,
        out int itemId)
    {
        itemId = GetCenterTopItemId(inventory);
        return itemId >= 0 && (itemFilter == null || itemFilter(itemId));
    }

    private static bool TryTakeSavedCenterTopItem(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        Predicate<int> itemFilter,
        out int itemId)
    {
        itemId = -1;
        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        if (!TryPeekSavedCenterTopItem(inventory, itemFilter, out itemId))
        {
            return false;
        }

        inventory.centerItems.RemoveAt(inventory.centerItems.Count - 1);
        SaveBlockInventory(stateStore, coordinate, inventory);
        return true;
    }

    private static bool TryResolveRobotArmInteractionCoordinate(
        BlockStateStore.InstallationSaveState installationState,
        RobotArm templateRobotArm,
        bool inputSide,
        out Vector2Int interactionCoordinate)
    {
        interactionCoordinate = default;
        if (installationState == null)
        {
            return false;
        }

        Quaternion rotation = ResolveSavedInstallationRotation(templateRobotArm, installationState.quarterTurns);
        if (!TryResolveFlowDirection(rotation, out Vector2Int flowDirection))
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = installationState.occupiedCoordinates;
        Vector2Int edgeCoordinate = installationState.anchorCoordinate;
        int bestProjection = inputSide ? int.MaxValue : int.MinValue;
        if (occupiedCoordinates != null && occupiedCoordinates.Count > 0)
        {
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                Vector2Int coordinate = occupiedCoordinates[i];
                int projection = coordinate.x * flowDirection.x + coordinate.y * flowDirection.y;
                bool betterProjection = inputSide ? projection < bestProjection : projection > bestProjection;
                if (!betterProjection)
                {
                    continue;
                }

                bestProjection = projection;
                edgeCoordinate = coordinate;
            }
        }

        interactionCoordinate = inputSide ? edgeCoordinate - flowDirection : edgeCoordinate + flowDirection;
        return true;
    }

    private static Quaternion ResolveSavedInstallationRotation(MapObject sourcePrefab, int quarterTurns)
    {
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        return sourcePrefab != null
            ? sourcePrefab.transform.rotation * Quaternion.Euler(0f, normalizedQuarterTurns * 90f, 0f)
            : Quaternion.Euler(0f, normalizedQuarterTurns * 90f, 0f);
    }

    private static bool TryResolveFlowDirection(Quaternion rotation, out Vector2Int flowDirection)
    {
        Vector3 forward = rotation * Vector3.forward;
        Vector2 flatForward = new Vector2(forward.x, forward.z);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flowDirection = Vector2Int.up;
            return true;
        }

        flatForward.Normalize();
        flowDirection = Mathf.Abs(flatForward.x) >= Mathf.Abs(flatForward.y)
            ? new Vector2Int(flatForward.x >= 0f ? 1 : -1, 0)
            : new Vector2Int(0, flatForward.y >= 0f ? 1 : -1);
        return true;
    }

    private static Vector3 GetSavedCoordinateWorldPosition(Vector2Int coordinate)
    {
        return new Vector3(coordinate.x, 0.2f, coordinate.y);
    }

    private static bool CoordinateAcceptsInputAreaObject(Vector2Int coordinate)
    {
        return InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate);
    }

    private static bool TryResolveSavedBoxAtCoordinate(
        BlockStateStore stateStore,
        Vector2Int coordinate,
        out BlockStateStore.InstallationSaveState boxState,
        out ItemDefinition boxDefinition)
    {
        boxState = null;
        boxDefinition = null;
        if (stateStore == null
            || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate)
            || !stateStore.TryGetInstallationState(anchorCoordinate, out boxState)
            || boxState == null)
        {
            return false;
        }

        boxDefinition = ResolveItemDefinition(boxState.itemId);
        return TryResolveMapObjectComponent(boxDefinition, out BoxObject _);
    }

    private static bool TryResolveTemplateRobotArm(int itemId, out ItemDefinition installedDefinition, out RobotArm templateRobotArm)
    {
        installedDefinition = ResolveItemDefinition(itemId);
        return TryResolveMapObjectComponent(installedDefinition, out templateRobotArm);
    }

    private static bool TryResolveMapObjectComponent<T>(ItemDefinition definition, out T component) where T : Component
    {
        component = null;
        if (definition == null || definition.mapObject == null)
        {
            return false;
        }

        component = definition.mapObject as T;
        return component != null || definition.mapObject.TryGetComponent(out component);
    }

    private static bool SavedInstallationAcceptsItem(BlockStateStore.InstallationSaveState installationState, int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        if (installationState == null || !installationState.itemFilterMaskInitialized)
        {
            return true;
        }

        List<ulong> words = installationState.itemFilterMaskWords;
        int wordIndex = itemId >> 6;
        if (words == null || wordIndex < 0 || wordIndex >= words.Count)
        {
            return true;
        }

        ulong bitMask = 1UL << (itemId & 63);
        return (words[wordIndex] & bitMask) != 0UL;
    }

    private bool AdvanceActiveCraft(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition,
        ref double remainingElapsed,
        ref double simulatedSeconds,
        out bool blocked)
    {
        blocked = false;
        if (state == null)
        {
            blocked = true;
            return false;
        }

        if (state.waitingForOutput)
        {
            if (!TryCompleteActiveCraft(stateStore, state, templateModule))
            {
                blocked = true;
                return false;
            }

            return true;
        }

        if (!RequiresOperationalEnergy(installedDefinition))
        {
            float remainingCraftTime = Mathf.Max(0f, state.remainingCraftTime);
            if (remainingCraftTime <= 0.0001f)
            {
                state.waitingForOutput = true;
                if (!TryCompleteActiveCraft(stateStore, state, templateModule))
                {
                    blocked = true;
                    return false;
                }

                return true;
            }

            double delta = Math.Min(remainingElapsed, remainingCraftTime);
            if (delta <= 0.0001d)
            {
                blocked = true;
                return false;
            }

            state.remainingCraftTime = Mathf.Max(0f, remainingCraftTime - (float)delta);
            remainingElapsed -= delta;
            simulatedSeconds += delta;
        }
        else
        {
            float energyRate = Mathf.Max(0f, installedDefinition.useEnergyAmount);
            float completeEnergy = InputOutputModule.ResolveCompleteEnergy(
                installedDefinition,
                templateModule.CraftDurationSeconds);
            if (state.activeCraftConsumedEnergy <= 0.0001f)
            {
                state.activeCraftConsumedEnergy = ResolveConsumedEnergyFromRemainingTime(
                    installedDefinition,
                    templateModule.CraftDurationSeconds,
                    state.remainingCraftTime);
            }

            while (remainingElapsed > 0.0001d && state.activeCraftConsumedEnergy + 0.0001f < completeEnergy)
            {
                if (state.storedEnergy <= 0.0001f && !TryRefillEnergyStore(stateStore, state, templateModule, installedDefinition))
                {
                    blocked = true;
                    return simulatedSeconds > 0d;
                }

                float maxEnergyByTime = energyRate * (float)remainingElapsed;
                float remainingCraftEnergy = Mathf.Max(0f, completeEnergy - state.activeCraftConsumedEnergy);
                float consumedEnergy = Mathf.Min(state.storedEnergy, Mathf.Min(maxEnergyByTime, remainingCraftEnergy));
                double delta = energyRate <= 0.0001f ? 0d : consumedEnergy / energyRate;
                if (delta <= 0.0001d)
                {
                    blocked = true;
                    return simulatedSeconds > 0d;
                }

                state.storedEnergy = Mathf.Max(0f, state.storedEnergy - consumedEnergy);
                state.activeCraftConsumedEnergy = Mathf.Min(completeEnergy, state.activeCraftConsumedEnergy + consumedEnergy);
                if (state.storedEnergy <= 0.0001f)
                {
                    state.energyGaugeCapacity = 0f;
                }

                state.remainingCraftTime = ResolveRemainingEnergyCraftTime(
                    installedDefinition,
                    templateModule.CraftDurationSeconds,
                    state.activeCraftConsumedEnergy);
                remainingElapsed -= delta;
                simulatedSeconds += delta;
            }
        }

        if (RequiresOperationalEnergy(installedDefinition))
        {
            float completeEnergy = InputOutputModule.ResolveCompleteEnergy(
                installedDefinition,
                templateModule.CraftDurationSeconds);
            if (state.activeCraftConsumedEnergy + 0.0001f < completeEnergy)
            {
                return true;
            }
        }
        else if (state.remainingCraftTime > 0.0001f)
        {
            return true;
        }

        state.waitingForOutput = true;
        if (!TryCompleteActiveCraft(stateStore, state, templateModule))
        {
            blocked = true;
            return false;
        }

        return true;
    }

    private bool TryStartNextCraft(
        BlockStateStore stateStore,
        BlockStateStore.InstallationSaveState installationState,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition)
    {
        if (state == null || installedDefinition == null || templateModule == null)
        {
            return false;
        }

        int recipeCount = Mathf.Min(templateModule.InputList.Count, templateModule.OutputList.Count);
        for (int recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            if (!TryGetRecipePair(templateModule, recipeIndex, out int inputItemId, out int inputCount, out int outputItemId, out int outputCount))
            {
                continue;
            }

            if (!SavedProductionMachineAcceptsOutput(installationState, templateModule, outputItemId))
            {
                continue;
            }

            if (!TryResolveRuntimeInputItemArea(state, recipeIndex, inputItemId, out Vector2Int inputCoordinate))
            {
                continue;
            }

            if (GetCenterItemCount(stateStore, inputCoordinate, inputItemId) < inputCount)
            {
                continue;
            }

            if (InputOutputModule.IsFluidItemId(outputItemId)
                || !TryResolveOutputCoordinate(stateStore, state, templateModule, outputItemId, outputCount, out _))
            {
                continue;
            }

            if (!TryEnsureCraftStartEnergy(stateStore, state, templateModule, installedDefinition))
            {
                continue;
            }

            if (RemoveCenterItems(stateStore, inputCoordinate, inputItemId, inputCount) != inputCount)
            {
                continue;
            }

            state.hasActiveCraft = true;
            state.waitingForOutput = false;
            state.remainingCraftTime = ResolveInitialCraftDuration(installedDefinition, templateModule.CraftDurationSeconds);
            state.activeCraftConsumedEnergy = 0f;
            state.activeRecipeIndex = recipeIndex;
            state.activeOutputItemId = outputItemId;
            state.activeOutputCount = outputCount;
            return true;
        }

        return false;
    }

    private static bool SavedProductionMachineAcceptsOutput(
        BlockStateStore.InstallationSaveState installationState,
        InputOutputModule templateModule,
        int outputItemId)
    {
        if (!(templateModule is ProductionMachine productionMachine))
        {
            return true;
        }

        if (outputItemId < 0)
        {
            return false;
        }

        List<int> targetItemIds = new List<int>();
        if (!productionMachine.TryCollectProductionTargetItemIds(targetItemIds))
        {
            return true;
        }

        if (installationState == null || !installationState.itemFilterMaskInitialized)
        {
            return targetItemIds.Count > 0 && outputItemId == targetItemIds[0];
        }

        for (int i = 0; i < targetItemIds.Count; i++)
        {
            int targetItemId = targetItemIds[i];
            if (SavedInstallationAcceptsItem(installationState, targetItemId))
            {
                return outputItemId == targetItemId;
            }
        }

        return false;
    }

    private bool TryCompleteActiveCraft(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule)
    {
        if (state == null || !state.hasActiveCraft || state.activeOutputItemId < 0 || state.activeOutputCount <= 0)
        {
            ClearActiveCraft(state);
            return false;
        }

        if (InputOutputModule.IsFluidItemId(state.activeOutputItemId)
            || !TryResolveOutputCoordinate(
                stateStore,
                state,
                templateModule,
                state.activeOutputItemId,
                state.activeOutputCount,
                out Vector2Int outputCoordinate))
        {
            return false;
        }

        if (!AddCenterItems(
                stateStore,
                outputCoordinate,
                state.activeOutputItemId,
                state.activeOutputCount,
                ResolveBlockCenterCapacity(stateStore, outputCoordinate, templateModule.RuntimeAreaMaxObjects)))
        {
            return false;
        }

        ClearActiveCraft(state);
        return true;
    }

    private bool TryEnsureCraftStartEnergy(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        if (state.storedEnergy > 0.0001f)
        {
            return true;
        }

        return TryRefillEnergyStore(stateStore, state, templateModule, installedDefinition);
    }

    private bool TryRefillEnergyStore(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        ItemDefinition installedDefinition)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return true;
        }

        float minimumOperationalEnergy = Mathf.Max(1f, installedDefinition.useEnergyAmount);
        bool consumedAnyEnergyItem = false;

        while (state.storedEnergy < minimumOperationalEnergy)
        {
            if (!TryConsumeOneEnergyItem(stateStore, state, installedDefinition.useEnergyType, out int gainedEnergy))
            {
                break;
            }

            state.storedEnergy += gainedEnergy;
            consumedAnyEnergyItem = true;
        }

        if (consumedAnyEnergyItem)
        {
            state.energyGaugeCapacity = Mathf.Max(state.storedEnergy, 1f);
        }

        return state.storedEnergy >= minimumOperationalEnergy;
    }

    private bool TryConsumeOneEnergyItem(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        ItemDefinition.EnergyType requiredEnergyType,
        out int gainedEnergy)
    {
        gainedEnergy = 0;
        if (state == null
            || requiredEnergyType == ItemDefinition.EnergyType.None
            || state.inputEnergyCoordinates == null
            || state.inputEnergyCoordinates.Count <= 0)
        {
            return false;
        }

        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < state.inputEnergyCoordinates.Count; i++)
        {
            Vector2Int coordinate = state.inputEnergyCoordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            int energyItemId = GetCenterTopItemId(stateStore, coordinate);
            if (energyItemId < 0)
            {
                continue;
            }

            ItemDefinition energyDefinition = ResolveItemDefinition(energyItemId);
            if (energyDefinition == null
                || energyDefinition.energyType != requiredEnergyType
                || energyDefinition.energyAmount <= 0)
            {
                continue;
            }

            if (RemoveCenterItems(stateStore, coordinate, energyItemId, 1) != 1)
            {
                continue;
            }

            gainedEnergy = energyDefinition.energyAmount;
            return true;
        }

        return false;
    }

    private bool TryResolveOutputCoordinate(
        BlockStateStore stateStore,
        InputOutputModule.PersistentState state,
        InputOutputModule templateModule,
        int outputItemId,
        int outputCount,
        out Vector2Int targetCoordinate)
    {
        targetCoordinate = default;
        if (state == null
            || outputItemId < 0
            || outputCount <= 0
            || state.outputCoordinates == null
            || state.outputCoordinates.Count <= 0)
        {
            return false;
        }

        int totalCapacity = ResolveAreaCapacity(stateStore, state.outputCoordinates, templateModule.RuntimeAreaMaxObjects);
        if (GetAreaObjectCount(stateStore, state.outputCoordinates) + outputCount > totalCapacity)
        {
            return false;
        }

        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExistingStack = pass == 0;
            visitedCoordinates.Clear();

            for (int i = 0; i < state.outputCoordinates.Count; i++)
            {
                Vector2Int coordinate = state.outputCoordinates[i];
                if (!visitedCoordinates.Add(coordinate))
                {
                    continue;
                }

                SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
                int blockCapacity = ResolveBlockCenterCapacity(stateStore, coordinate, templateModule.RuntimeAreaMaxObjects);
                if (!CanAddCenterItems(inventory, outputItemId, outputCount, blockCapacity))
                {
                    continue;
                }

                if (requireExistingStack && GetCenterTopItemId(inventory) != outputItemId)
                {
                    continue;
                }

                targetCoordinate = coordinate;
                return true;
            }
        }

        return false;
    }

    private int ResolveAreaCapacity(BlockStateStore stateStore, IReadOnlyList<Vector2Int> coordinates, int defaultCapacity)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return Mathf.Max(1, defaultCapacity);
        }

        int installedCapacityTotal = 0;
        bool hasInstalledCapacity = false;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            if (!TryResolveInstalledItemAreaCapacity(stateStore, coordinate, out int blockCapacity))
            {
                continue;
            }

            installedCapacityTotal += Mathf.Max(1, blockCapacity);
            hasInstalledCapacity = true;
        }

        return hasInstalledCapacity
            ? Mathf.Max(1, installedCapacityTotal)
            : Mathf.Max(1, defaultCapacity);
    }

    private bool TryResolveInstalledItemAreaCapacity(BlockStateStore stateStore, Vector2Int coordinate, out int capacity)
    {
        capacity = 0;
        if (stateStore == null || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        if (!stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState))
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveItemDefinition(installationState.itemId);
        if (installedDefinition == null
            || !(installedDefinition.mapObject is InstallationObject installationObject)
            || (installationObject.MapFilter & InstallationMapFilter.ItemArea) == 0)
        {
            return false;
        }

        capacity = installedDefinition.capacity > 0 ? installedDefinition.capacity : 10;
        return true;
    }

    private static bool TryResolveRuntimeInputItemArea(
        InputOutputModule.PersistentState state,
        int recipeIndex,
        int inputItemId,
        out Vector2Int coordinate)
    {
        coordinate = default;
        if (state == null
            || inputItemId < 0
            || state.inputItemAreas == null
            || state.inputItemAreas.Count <= 0)
        {
            return false;
        }

        if (recipeIndex >= 0 && recipeIndex < state.inputItemAreas.Count)
        {
            InputOutputModule.PersistentInputItemAreaState indexedArea = state.inputItemAreas[recipeIndex];
            if (indexedArea.itemId == inputItemId)
            {
                coordinate = indexedArea.coordinate;
                return true;
            }
        }

        for (int i = 0; i < state.inputItemAreas.Count; i++)
        {
            InputOutputModule.PersistentInputItemAreaState area = state.inputItemAreas[i];
            if (area.itemId != inputItemId)
            {
                continue;
            }

            coordinate = area.coordinate;
            return true;
        }

        return false;
    }

    private static bool TryGetRecipePair(
        InputOutputModule templateModule,
        int recipeIndex,
        out int inputItemId,
        out int inputCount,
        out int outputItemId,
        out int outputCount)
    {
        inputItemId = -1;
        inputCount = 0;
        outputItemId = -1;
        outputCount = 0;

        if (templateModule == null
            || recipeIndex < 0
            || recipeIndex >= templateModule.InputList.Count
            || recipeIndex >= templateModule.OutputList.Count)
        {
            return false;
        }

        InputOutputModule.ItemIoEntry inputEntry = templateModule.InputList[recipeIndex];
        InputOutputModule.ItemIoEntry outputEntry = templateModule.OutputList[recipeIndex];
        if (inputEntry.itemDefinition == null || outputEntry.itemDefinition == null)
        {
            return false;
        }

        inputItemId = inputEntry.itemDefinition.id;
        inputCount = Mathf.Max(1, inputEntry.count);
        outputItemId = outputEntry.itemDefinition.id;
        outputCount = Mathf.Max(1, outputEntry.count);
        return inputItemId >= 0 && outputItemId >= 0;
    }

    private static bool RequiresOperationalEnergy(ItemDefinition installedDefinition)
    {
        return installedDefinition != null
               && installedDefinition.useEnergyType != ItemDefinition.EnergyType.None
               && installedDefinition.useEnergyAmount > 0f;
    }

    private static float ResolveInitialCraftDuration(ItemDefinition installedDefinition, float fallbackCraftDuration)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return Mathf.Max(0.1f, fallbackCraftDuration);
        }

        float energyRate = Mathf.Max(0.0001f, installedDefinition.useEnergyAmount);
        return Mathf.Max(0.1f, InputOutputModule.ResolveCompleteEnergy(installedDefinition, fallbackCraftDuration) / energyRate);
    }

    private static float ResolveRemainingEnergyCraftTime(
        ItemDefinition installedDefinition,
        float fallbackCraftDuration,
        float consumedEnergy)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return Mathf.Max(0.1f, fallbackCraftDuration);
        }

        float energyRate = Mathf.Max(0.0001f, installedDefinition.useEnergyAmount);
        float remainingEnergy = Mathf.Max(
            0f,
            InputOutputModule.ResolveCompleteEnergy(installedDefinition, fallbackCraftDuration) - Mathf.Max(0f, consumedEnergy));
        return remainingEnergy / energyRate;
    }

    private static float ResolveConsumedEnergyFromRemainingTime(
        ItemDefinition installedDefinition,
        float fallbackCraftDuration,
        float savedRemainingCraftTime)
    {
        if (!RequiresOperationalEnergy(installedDefinition))
        {
            return 0f;
        }

        float energyRate = Mathf.Max(0.0001f, installedDefinition.useEnergyAmount);
        float completeEnergy = InputOutputModule.ResolveCompleteEnergy(installedDefinition, fallbackCraftDuration);
        float totalDuration = completeEnergy / energyRate;
        float elapsedDuration = Mathf.Clamp(totalDuration - Mathf.Max(0f, savedRemainingCraftTime), 0f, totalDuration);
        return Mathf.Min(completeEnergy, elapsedDuration * energyRate);
    }

    private static void ClearActiveCraft(InputOutputModule.PersistentState state)
    {
        if (state == null)
        {
            return;
        }

        state.hasActiveCraft = false;
        state.waitingForOutput = false;
        state.remainingCraftTime = 0f;
        state.activeCraftConsumedEnergy = 0f;
        state.activeRecipeIndex = -1;
        state.activeOutputItemId = -1;
        state.activeOutputCount = 0;
    }

    private static int GetAreaObjectCount(BlockStateStore stateStore, IReadOnlyList<Vector2Int> coordinates, int itemId = -1)
    {
        if (coordinates == null || coordinates.Count <= 0)
        {
            return 0;
        }

        int count = 0;
        HashSet<Vector2Int> visitedCoordinates = new HashSet<Vector2Int>();
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!visitedCoordinates.Add(coordinate))
            {
                continue;
            }

            count += GetCenterItemCount(stateStore, coordinate, itemId);
        }

        return count;
    }

    private static int GetCenterItemCount(BlockStateStore stateStore, Vector2Int coordinate, int itemId = -1)
    {
        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        if (itemId < 0)
        {
            return inventory.centerItems.Count;
        }

        int count = 0;
        for (int i = 0; i < inventory.centerItems.Count; i++)
        {
            if (inventory.centerItems[i] == itemId)
            {
                count++;
            }
        }

        return count;
    }

    private static int GetCenterTopItemId(BlockStateStore stateStore, Vector2Int coordinate)
    {
        return GetCenterTopItemId(LoadBlockInventory(stateStore, coordinate));
    }

    private static int GetCenterTopItemId(SavedBlockInventory inventory)
    {
        if (inventory == null || inventory.centerItems.Count <= 0)
        {
            return -1;
        }

        return inventory.centerItems[inventory.centerItems.Count - 1];
    }

    private static int ResolveBlockCenterCapacity(BlockStateStore stateStore, Vector2Int coordinate, int defaultCapacity)
    {
        return TryResolveInstalledItemAreaCapacityStatic(stateStore, coordinate, out int installedCapacity)
            ? Mathf.Max(1, installedCapacity)
            : Mathf.Max(1, defaultCapacity);
    }

    private static bool TryResolveInstalledItemAreaCapacityStatic(BlockStateStore stateStore, Vector2Int coordinate, out int capacity)
    {
        capacity = 0;
        if (stateStore == null || !stateStore.TryGetInstallationAnchorAtCoordinate(coordinate, out Vector2Int anchorCoordinate))
        {
            return false;
        }

        if (!stateStore.TryGetInstallationState(anchorCoordinate, out BlockStateStore.InstallationSaveState installationState))
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveItemDefinition(installationState.itemId);
        if (installedDefinition == null
            || !(installedDefinition.mapObject is InstallationObject installationObject)
            || (installationObject.MapFilter & InstallationMapFilter.ItemArea) == 0)
        {
            return false;
        }

        capacity = installedDefinition.capacity > 0 ? installedDefinition.capacity : 10;
        return true;
    }

    private static bool CanAddCenterItems(SavedBlockInventory inventory, int itemId, int count, int capacity)
    {
        if (inventory == null || count <= 0)
        {
            return false;
        }

        if (inventory.centerItems.Count > 0 && inventory.centerItems[0] != itemId)
        {
            return false;
        }

        return Mathf.Max(1, capacity) - inventory.centerItems.Count >= count;
    }

    private static bool AddCenterItems(BlockStateStore stateStore, Vector2Int coordinate, int itemId, int count, int capacity)
    {
        if (count <= 0)
        {
            return true;
        }

        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        if (!CanAddCenterItems(inventory, itemId, count, capacity))
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            inventory.centerItems.Add(itemId);
        }

        SaveBlockInventory(stateStore, coordinate, inventory);
        return true;
    }

    private static int RemoveCenterItems(BlockStateStore stateStore, Vector2Int coordinate, int itemId, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        SavedBlockInventory inventory = LoadBlockInventory(stateStore, coordinate);
        int removed = 0;
        for (int i = inventory.centerItems.Count - 1; i >= 0 && removed < count; i--)
        {
            if (itemId >= 0 && inventory.centerItems[i] != itemId)
            {
                continue;
            }

            inventory.centerItems.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
        {
            SaveBlockInventory(stateStore, coordinate, inventory);
        }

        return removed;
    }

    private static SavedBlockInventory LoadBlockInventory(BlockStateStore stateStore, Vector2Int coordinate)
    {
        if (stateStore != null && stateStore.TryGetFloorObjectsCopy(coordinate, out List<int> itemIds))
        {
            return SavedBlockInventory.FromSerialized(itemIds);
        }

        return new SavedBlockInventory();
    }

    private static void SaveBlockInventory(BlockStateStore stateStore, Vector2Int coordinate, SavedBlockInventory inventory)
    {
        if (stateStore == null)
        {
            return;
        }

        stateStore.SetFloorObjects(coordinate, inventory != null ? inventory.ToSerialized() : null);
    }

    private static bool TryResolveTemplateModule(int itemId, out ItemDefinition installedDefinition, out InputOutputModule templateModule)
    {
        installedDefinition = ResolveItemDefinition(itemId);
        templateModule = installedDefinition != null ? installedDefinition.mapObject as InputOutputModule : null;
        return installedDefinition != null && templateModule != null;
    }

    private static ItemDefinition ResolveItemDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private bool TryGetStateStore(out BlockStateStore stateStore)
    {
        if (cachedStateStore == null)
        {
            cachedStateStore = GetComponent<BlockStateStore>();
        }

        stateStore = cachedStateStore;
        return stateStore != null;
    }
}
