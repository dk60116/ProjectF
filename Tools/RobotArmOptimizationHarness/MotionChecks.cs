using System;
using UnityEngine;

static partial class Checks
{
    static Quaternion LegacyStep(Quaternion current, Quaternion target, float degrees)
    {
        float angle = Quaternion.Angle(current, target);
        return angle == 0 ? target : Quaternion.SlerpUnclamped(current, target, MathF.Min(1, degrees / angle));
    }

    static void CheckMotionAndPower()
    {
        var coverage = new int[256];
        var sampled = new ProjectF.Diagnostics.RobotArmTickTiming();
        MapObjectTickProfiler.Enabled = true;
        for (int tick = 0; tick < 256; tick++)
        {
            MapObjectTickProfiler.Records.Clear();
            using (sampled.BeginTick())
                for (int ordinal = 0; ordinal < 256; ordinal++)
                {
                    sampled.BeginEntity(ordinal);
                    long clock = MapObjectTickProfiler.Clock;
                    using (sampled.Measure(ProjectF.Diagnostics.RobotArmTickTiming.Phase.State)) { }
                    if (MapObjectTickProfiler.Clock != clock) coverage[ordinal]++;
                }
        }
        Require(Array.TrueForAll(coverage, count => count == 1), "rotating detailed sampling covers every ordinal exactly once per 256 ticks");
        MapObjectTickProfiler.Enabled = false;
        var arm = new RobotArmInstance { TurnSpeed = 540 };
        Require(arm.CheckPlanReset(), "one state-slot lookup resets every per-plan command/cache/wake flag");
        arm.SetPose(Quaternion.identity);
        var target = Quaternion.Euler(0, 180, 0);
        Quaternion.AngleCalls = Quaternion.SlerpCalls = 0;
        for (int tick = 1; tick <= 20; tick++)
            Require(arm.Turn(target, 1f / 60) == (tick == 20), "540 degree/s half-turn completes on tick 20");
        Require(Quaternion.AngleCalls == 1 && Quaternion.SlerpCalls == 0,
            "twenty simulation ticks calculate the angle once and never interpolate an invisible pose");

        foreach (float angle in new[] { 0f, 15f, 90f, 179f, 180f, -90f })
        foreach (float speed in new[] { 90f, 180f, 540f })
        foreach (float dt in new[] { 1f / 60, 1f / 30, .0375f })
        {
            arm.TurnSpeed = speed; arm.SetPose(Quaternion.identity);
            target = Quaternion.Euler(0, angle, 0);
            var legacy = Quaternion.identity;
            for (int tick = 1; tick <= 200; tick++)
            {
                legacy = LegacyStep(legacy, target, speed * dt);
                bool oldDone = Quaternion.Angle(legacy, target) <= .1f;
                bool newDone = arm.Turn(target, dt);
                Require(oldDone == newDone, "rotation completion tick agrees with the legacy step across speeds and timesteps");
                Require(Quaternion.Angle(arm.BodyRotation, legacy) < .2f, "lazy visible rotation follows the legacy pose");
                if (oldDone) break;
                if (tick == 200) throw new Exception("turn did not finish");
            }
        }
        arm.TurnSpeed = 180; arm.SetPose(Quaternion.identity);
        target = Quaternion.Euler(0, 90, 0);
        arm.Turn(target, .2f);
        var beforeReverse = arm.BodyRotation;
        arm.Turn(Quaternion.identity, .05f);
        Require(Quaternion.Angle(arm.BodyRotation, LegacyStep(beforeReverse, Quaternion.identity, 9)) < .1f,
            "changing turn direction starts from the materialized current pose");
        var pausedPose = arm.BodyRotation;
        arm.Turn(Quaternion.identity, 0);
        Require(Quaternion.Angle(pausedPose, arm.BodyRotation) < .1f, "zero powered time preserves turn progress");
        arm.state = RobotArm.RobotArmState.TurningToPickup;
        var saved = arm.CaptureTransferState();
        Require(MathF.Abs(saved.turnTimer - Quaternion.Angle(arm.BodyRotation, Quaternion.identity) / 180) < .001f,
            "saving a lazy turn retains its remaining duration");

        arm.AnimationTime = 1; arm.ItemMoveElapsed = .1f;
        arm.Animate(.5f);
        Require(arm.AnimationTime == 1 && arm.ItemMoveElapsed == .1f, "completed presentation clocks stay saturated");
        arm.AnimationTime = .2f; arm.ItemMoveElapsed = .03f;
        arm.Animate(.02f);
        Require(MathF.Abs(arm.AnimationTime - .22f) < .00001f && MathF.Abs(arm.ItemMoveElapsed - .05f) < .00001f,
            "active presentation clocks advance by the supplied powered time");

        UtilityPole.ConsumeCalls = 0; arm.Template.ElectricUseWatts = 730;
        arm.World.IsPlanning = arm.World.FullPowerTick = true;
        for (int i = 0; i < 100000; i++)
            if (arm.PoweredTime(.02f) != .02f) throw new Exception("full power changed elapsed time");
        Require(UtilityPole.ConsumeCalls == 0 && arm.SupplyRatio == 1, "full-power ticks bypass 100k redundant electricity calls");
        arm.World.FullPowerTick = false; UtilityPole.Ratio = .5f;
        Require(arm.PoweredTime(.02f) == .01f && arm.SupplyRatio == .5f && UtilityPole.ConsumeCalls == 1,
            "real partial supply keeps ordered energy allocation and scaled elapsed time");
        UtilityPole.Ratio = 0;
        Require(arm.PoweredTime(.02f) == 0 && arm.Sleeping, "unpowered real consumers retain event-driven sleep");
        arm.World.FullPowerTick = true;
        Require(arm.PoweredTime(.02f) == .02f && !arm.PowerBlocked, "full supply clears an earlier blocked-power status");
        arm.Template.ElectricUseWatts = .001f;
        Require(arm.PoweredTime(.02f) == 0 && arm.SupplyRatio == 0, "full-power shortcut preserves the small requested-energy threshold");
        arm.Template.ElectricUseWatts = 0;
        Require(arm.PoweredTime(.02f) == .02f && arm.SupplyRatio == 1, "an arm without electric demand still receives full elapsed time");

        var template = new RobotArmRenderTemplate(); arm.Template = template;
        foreach (float yaw in new[] { 0f, 90f, 180f })
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (float time in new[] { 0f, .17f, .9f, 1f })
        {
            arm.SetPose(Quaternion.Euler(0, yaw, 0)); arm.AnimationKind = kind; arm.AnimationTime = time;
            arm.WorldRotation = Quaternion.Euler(15, yaw + 45, -5); arm.ColliderCenter = new Vector3(50, 3, -80);
            Require((template.HandWorld(arm) - template.LegacyHand(arm)).sqrMagnitude < .000001f,
                "cached local hand chain matches the original world chain for both animations, rotated placements and nonuniform source scale");
        }
        template.HandWorld(arm); Matrix4x4.TrsCalls = 0;
        for (int i = 0; i < 100000; i++)
        {
            arm.ColliderCenter = new Vector3(i, 3, -80);
            var point = template.HandWorld(arm);
            if (point.x < i - 10 || point.x > i + 10) throw new Exception("pose cache reused a previous world origin");
        }
        Require(Matrix4x4.TrsCalls == 0, "100k synchronized arms share the local rig evaluation without sharing world position");
    }
}

public static class UtilityPole
{
    public static int ConsumeCalls;
    public static float Ratio = 1;
    public static bool TryConsumeRobotArmElectricity(RobotArmInstance arm, float watts, float requested, out float consumed)
    { ConsumeCalls++; consumed = requested * Ratio; return consumed > 0; }
    public static bool HasElectricityAvailable(RobotArmInstance arm) => Ratio > 0;
}

internal partial class RobotArmRenderTemplate
{
    private sealed class Node
    {
        internal int Parent;
        internal Vector3 Position, Scale = Vector3.one;
        internal Quaternion Rotation = Quaternion.identity;
        internal Track? Pick, Drop;
    }
    private readonly struct Track
    {
        private readonly float degrees;
        internal Track(float degrees) { this.degrees = degrees; }
        internal Quaternion Evaluate(float time) => Quaternion.Euler(0, 0, degrees * time);
    }
    private Node[] nodes;
    private Vector3 rootScale;
    private int bodyIndex, handIndex;
    private bool handPoseCached;
    private Quaternion handPoseBodyRotation;
    private int handPoseAnimationKind;
    private float handPoseAnimationTime;
    private Vector3 handPoseLocalPosition;
    private void InitializeRig()
    {
        rootScale = new Vector3(1, 2, 3); bodyIndex = 1; handIndex = 2;
        nodes = new[] { new Node { Parent = -1 }, new Node { Parent = 0, Position = new Vector3(0, .5f, 0) },
            new Node { Parent = 1, Position = new Vector3(1, 0, 0), Pick = new Track(20), Drop = new Track(-30) } };
    }
    internal Vector3 LegacyHand(RobotArmInstance arm) => EvaluateChain(arm, handIndex).MultiplyPoint3x4(Vector3.zero);
}
