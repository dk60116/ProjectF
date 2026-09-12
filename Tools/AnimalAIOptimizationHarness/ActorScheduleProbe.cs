using System;
using UnityEngine;
using ProjectF.Animals;

// Production scheduling, execution boundary and presentation methods are extracted by Run.ps1.
// Only the decision itself and engine objects are replaced here.
public partial class ActorScheduleProbe
{
    public readonly Transform transform = new();
    private bool configured = true, executionActive = true, IsExternallyControlled, IsFleeing;
    private long scheduledTicks, scheduledRecoveryTicks;
    private uint scheduledTickPhase;
    private bool scheduledTickPhaseApplied;
    private AnimalFixedPosition fixedPosition;
    private Vector3 simulationPosition { get => fixedPosition.Value; set => fixedPosition = new AnimalFixedPosition(value); }
    private Quaternion simulationRotation;
    private Vector3 presentationStartPosition, presentationTargetPosition;
    private Quaternion presentationStartRotation, presentationTargetRotation;
    private float presentationElapsed, presentationDuration;
    private bool presentationActive;
    private AnimalTickTimer decisionTimer = 1;
    private uint decisions;
    public long SimulationId => 721L;
    public int Executions;
    public Vector3 Position => simulationPosition;
    public long RemainingTicks => decisionTimer.Ticks;
    public uint Decisions => decisions;
    public ActorScheduleProbe() { ResetScheduledTick(); }
    private void EnsureSimulationPoseInitialized() { }
    private void TickSimulation(float dt, float recovery, bool live)
    {
        Executions++;
        decisionTimer -= dt;
        if (decisionTimer <= 0) { decisions++; decisionTimer = 1; }
        simulationPosition = AnimalSimulationMath.Advance(simulationPosition, AnimalSimulationMath.Direction(721), .625f, dt);
    }
}

public static partial class Checks
{
    private static ActorScheduleProbe SimulateActor(int fps, bool disturbView)
    {
        var actor = new ActorScheduleProbe();
        int tick = 0;
        for (int frame = 0; frame < fps * 10; frame++)
        {
            int targetTick = (frame + 1) * 60 / fps;
            while (tick < targetTick)
            {
                if (disturbView) actor.transform.position = new Vector3(frame * 5, 1000, -frame);
                if (actor.QueueScheduledTick(1f / 60, 4f / 60)) actor.ExecuteScheduledTick();
                tick++;
            }
            actor.TickPresentation(1f / fps);
        }
        return actor;
    }

    private static void ActorClockChecks()
    {
        var thirty = SimulateActor(30, false);
        var fast = SimulateActor(144, false);
        var hidden = SimulateActor(17, true);
        Require(thirty.Position.Equals(fast.Position) && thirty.Position.Equals(hidden.Position),
            "production actor execution produces identical simulation positions at 17/30/144 rendering FPS");
        Require(thirty.Executions == fast.Executions && thirty.Executions == hidden.Executions
            && thirty.RemainingTicks == hidden.RemainingTicks && thirty.Decisions == hidden.Decisions,
            "render interpolation and arbitrary view transforms cannot alter scheduling or decision timers");
    }
}
