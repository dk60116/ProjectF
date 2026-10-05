using System;
using UnityEngine;
using RobotArmState = RobotArm.RobotArmState;

static partial class Checks
{
    static void CheckStateTicks()
    {
        var arm = new RobotArmInstance();
        foreach (RobotArmState phase in Enum.GetValues<RobotArmState>())
        foreach (int item in new[] { -1, 7 })
        {
            arm.ResetStateTick(phase, item, .1f);
            Require(arm.SleepEligible() == (item < 0 ? phase == RobotArmState.WaitingForPickup : phase == RobotArmState.WaitingForDrop)
                && arm.StateLookups == 1, "sleep eligibility validates one slot and retains the phase/cargo truth table");
        }
        var waits = new[] { RobotArmState.WaitingBeforePickupTake, RobotArmState.WaitingAfterPickupTake,
            RobotArmState.WaitingBeforeDropPlace, RobotArmState.WaitingAfterDropPlace };
        foreach (var phase in waits)
        foreach (int item in new[] { -1, 7 })
        foreach (float timer in new[] { -.1f, 0f, .0001f, 1f / 60, .1f })
        foreach (float dt in new[] { -.02f, 0f, 1f / 120, 1f / 60, .5f })
        {
            arm.ResetStateTick(phase, item, timer);
            arm.StepState(dt);
            var actual = arm.StateTickSnapshot();
            bool redirected = phase == RobotArmState.WaitingAfterPickupTake || phase == RobotArmState.WaitingBeforeDropPlace
                ? item < 0 : item >= 0;
            float expectedTimer = redirected ? (phase == RobotArmState.WaitingBeforeDropPlace ? 0 : timer)
                : timer > 0 ? MathF.Max(0, timer - MathF.Max(0, dt)) : timer;
            RobotArmState expectedPhase = phase;
            int expectedCommand = 0;
            float expectedPickupTimer = .37f;
            if (redirected)
            {
                expectedPhase = phase == RobotArmState.WaitingAfterPickupTake ? RobotArmState.WaitingForPickup
                    : phase == RobotArmState.WaitingBeforeDropPlace ? RobotArmState.TurningToPickup : RobotArmState.TurningToDrop;
                if (phase == RobotArmState.WaitingAfterPickupTake) expectedPickupTimer = .1f;
            }
            else if (expectedTimer <= 0)
            {
                switch (phase)
                {
                    case RobotArmState.WaitingBeforePickupTake: expectedCommand = 1; break;
                    case RobotArmState.WaitingAfterPickupTake: expectedPhase = RobotArmState.TurningToDrop; break;
                    case RobotArmState.WaitingBeforeDropPlace: expectedCommand = 2; break;
                    case RobotArmState.WaitingAfterDropPlace: expectedPhase = RobotArmState.TurningToPickup; break;
                }
            }
            Require(arm.StateLookups == 1, "each wait tick validates its state slot exactly once");
            Require(actual.state == expectedPhase && actual.actionTurnTimer == expectedTimer
                && (int)actual.plannedTransferCommand == expectedCommand && actual.pickupTimer == expectedPickupTimer
                && actual.heldItemId == item && actual.dropRetryTimer == .23f && actual.waitingForDropRetry,
                "wait ticks retain command, cargo and timer semantics across zero/negative/partial/large elapsed time");
        }

        arm.ResetStateTick(RobotArmState.WaitingBeforePickupTake, -1, 1);
        arm.StepState(0);
        arm.StateLookups = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) arm.StepState(0);
        Require(arm.StateLookups == 100000 && GC.GetAllocatedBytesForCurrentThread() == allocated,
            "100k wait ticks perform 100k slot lookups with no allocation");

        arm.ResetStateTick(RobotArmState.TurningToDrop, -1, .1f);
        arm.StepState(1f / 60);
        var lostCargo = arm.StateTickSnapshot();
        Require(arm.StateLookups == 1 && lostCargo.state == RobotArmState.TurningToPickup
            && !lostCargo.waitingForDropRetry && lostCargo.dropRetryTimer == 0,
            "cargo removed mid-turn returns to pickup without moving toward drop");

        foreach (bool toDrop in new[] { true, false })
        foreach (float powerRatio in new[] { .25f, .5f, 1f })
        {
            arm.ResetStateTick(toDrop ? RobotArmState.TurningToDrop : RobotArmState.TurningToPickup, toDrop ? 7 : -1, .1f);
            arm.TurnSpeed = 540;
            arm.SetPose(Quaternion.Euler(0, toDrop ? 0 : 180, 0));
            int ticks = (int)(20 / powerRatio);
            for (int tick = 1; tick <= ticks; tick++)
            {
                arm.StepState(powerRatio / 60);
                Require(arm.StateTickSnapshot().state == (tick < ticks
                    ? toDrop ? RobotArmState.TurningToDrop : RobotArmState.TurningToPickup
                    : toDrop ? RobotArmState.WaitingForDrop : RobotArmState.WaitingForPickup),
                    "turn state changes on the original completion tick at full and partial power");
            }
            var finished = arm.StateTickSnapshot();
            Require(toDrop ? !finished.waitingForDropRetry && finished.dropRetryTimer == 0 : finished.pickupTimer == .1f,
                "completed turn resets only its destination-side retry timer");
        }
    }
}

public partial class RobotArmInstance
{
    public void ResetStateTick(RobotArmState phase, int item, float timer)
    {
        tickData = new RobotArmRuntimeState { state = phase, heldItemId = item, actionTurnTimer = timer,
            pickupTimer = .37f, dropRetryTimer = .23f, waitingForDropRetry = true };
        StateLookups = 0;
    }
    internal RobotArmRuntimeState StateTickSnapshot() => tickData;
    public bool SleepEligible() => CanRuntimeSleepInCurrentState();
    public void StepState(float dt)
    {
        switch (tickData.state)
        {
            case RobotArmState.WaitingBeforePickupTake: TickWaitBeforePickupTake(dt); break;
            case RobotArmState.WaitingAfterPickupTake: TickWaitAfterPickupTake(dt); break;
            case RobotArmState.WaitingBeforeDropPlace: TickWaitBeforeDropPlace(dt); break;
            case RobotArmState.WaitingAfterDropPlace: TickWaitAfterDropPlace(dt); break;
            case RobotArmState.TurningToDrop: TickTurnToDrop(dt); break;
            case RobotArmState.TurningToPickup: TickTurnToPickup(dt); break;
        }
    }
}
