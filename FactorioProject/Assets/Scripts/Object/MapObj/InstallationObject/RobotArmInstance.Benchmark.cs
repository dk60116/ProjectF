using ProjectF.Benchmark;
using UnityEngine;
using RobotArmState = RobotArm.RobotArmState;

public sealed partial class RobotArmInstance : IBenchmarkWorkProgressTarget
{
    public bool TryRandomizeWorkProgress(System.Random random)
    {
        if (!IsRuntimeActive || !hasRuntimeStateInitialized || runtimeSleeping || electricPowerBlocked
            || plannedTransferCommand != PlannedTransferCommand.None) return false;
        float progress = Mathf.Min(.999999f, (float)random.NextDouble());
        switch (state)
        {
            case RobotArmState.TurningToDrop:
            case RobotArmState.TurningToPickup:
                SetBodyLocalRotation(state == RobotArmState.TurningToDrop ? inputBodyLocalRotation : GetOutputBodyLocalRotation());
                RotateBodyToward(state == RobotArmState.TurningToDrop ? GetOutputBodyLocalRotation() : inputBodyLocalRotation,
                    TurnDurationSeconds * progress);
                break;
            case RobotArmState.WaitingForPickup:
                pickupTimer = Mathf.Max(.00001f, pickupInterval * (1f - progress)); break;
            case RobotArmState.WaitingForDrop:
                if (!waitingForDropRetry) return false;
                dropRetryTimer = Mathf.Max(.00001f, dropRetryInterval * (1f - progress)); break;
            default:
                if (actionTurnDelay <= 0) return false;
                actionTurnTimer = Mathf.Max(.00001f, actionTurnDelay * (1f - progress)); break;
        }
        if (Data.AnimationTime < 1f) Data.AnimationTime = progress;
        if (Data.ItemMoveElapsed < ItemMoveDuration) Data.ItemMoveElapsed = ItemMoveDuration * progress;
        PersistTransferState(); WakeRuntimeSleep(); return true;
    }
}
