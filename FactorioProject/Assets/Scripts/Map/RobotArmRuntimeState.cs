using UnityEngine;
using RobotArmState = RobotArm.RobotArmState;
internal struct RobotArmRuntimeState
{
    internal int heldItemId;
    internal float pickupTimer;
    internal float dropRetryTimer;
    internal float actionTurnTimer;
    internal bool waitingForDropRetry;
    internal RobotArmState state;
    internal bool hasRuntimeStateInitialized;
    internal bool runtimeSleeping;
    internal bool runtimeWakePending;
    internal float runtimeSleepCheckTimer;
    internal float lastElectricPowerSupplyRatio;
    internal RobotArmInstance.PlannedTransferCommand plannedTransferCommand;
    internal bool stagedTickPlanned;
    internal bool plannedPickupAvailabilityChecked;
    internal bool plannedPickupAvailable;
    internal bool plannedDropAvailabilityChecked;
    internal bool plannedDropAvailable;
    internal Quaternion BodyRotation;
    internal int AnimationKind;
    internal float AnimationTime, ItemMoveElapsed;
    internal Vector3 ItemMoveStart;
}

