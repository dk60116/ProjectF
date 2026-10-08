using System;
using System.Collections.Generic;
using UnityEngine;

public partial class RailHandcar
{
    public static void RunRailAcquisitionChecks()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new Exception(message); }

        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reversed in new[] { false, true })
        foreach (float frontSign in new[] { -1f, 1f })
        foreach (float inputSign in new[] { -1f, 1f })
        foreach (bool idleFirst in new[] { false, true })
        {
            Vector2 front = axis * frontSign;
            Vector2 input = front * inputSign;
            var engine = new RailHandcar { RuntimePlacementSequence = 1 };
            engine.transform.rotation = PreviewRotation.LookRotation(new Vector3(front.x, 0, front.y), Vector3.up);
            Vector3 driveInput = new Vector3(input.x, 0, input.y);
            engine.TestDriveInput(driveInput, 1, .1f);
            Check(engine.Rail == null && engine.ApplyCount == 0, "Ground-only train must not move without track");

            var rail = reversed ? new Railload(axis * 5, axis * -5) : new Railload(axis * -5, axis * 5);
            engine.AcquisitionRails.Add(rail);
            if (idleFirst)
            {
                // Automatic driving also calls the base motion entry with zero input
                // while its route planner has no rail pose yet.
                engine.TestDriveInput(Vector3.zero, 0, .1f);
                Check(engine.Rail == rail && engine.Point.sqrMagnitude < .000001f,
                    "Idle drive tick must bind a rail laid after the train without moving along it");
            }
            for (int frame = 1; frame <= 8; frame++)
            {
                engine.TestDriveInput(driveInput, 1, .1f);
                Check(engine.Rail == rail && string.IsNullOrEmpty(engine.MoveFailure),
                    "Train-first, track-second must reach the movement path without reinstalling the train");
                Check(Vector2.Distance(engine.Point, input * (.1f * frame)) < .0001f,
                    "Every drive tick must advance by its requested distance on the acquired rail");
                Check(Vector2.Dot(engine.Facing, front) > .999f,
                    "Late rail acquisition must preserve the train front for reversed track and reverse input");
            }
            var follower = new Train();
            follower.transform.rotation = engine.transform.rotation;
            Check(engine.TryResolveRailSampleForTrain(follower, input, .75f * .75f, out var sample, out var facing)
                && sample.Rail == rail && Vector2.Dot(facing, front) > .999f,
                "A connected car without an initial rail pose must resolve the new track too");
        }

        var blocked = new RailHandcar();
        blocked.AcquisitionRails.Add(new Railload(new Vector2(.8f, -5), new Vector2(.8f, 5)));
        blocked.TestDriveInput(Vector3.forward, 1, .1f);
        Check(blocked.Rail == null && blocked.ApplyCount == 0 && blocked.MoveFailure == "CurrentRailSample",
            "Rails beyond the existing snap distance must not capture a ground-only train");

        var existing = new Railload(new Vector2(-5, 0), new Vector2(5, 0));
        var crossing = new Railload(new Vector2(0, -5), new Vector2(0, 5));
        var bound = new RailHandcar();
        bound.TryApplyRailPose(existing, 5, Vector2.zero, Vector2.right);
        bound.AcquisitionRails.Add(crossing);
        bound.TestDriveInput(Vector3.right, 1, .1f);
        Check(bound.Rail == existing, "A valid stored rail pose must take precedence over newly laid crossing track");
        Check(bound.AcquisitionSearchCount == 0, "Already-bound trains must not acquire or scan rails again");
        Console.WriteLine($"Late rail acquisition and drive dispatch passed: {checks} checks");
    }

    // Scene discovery, speed integration, docking, collision and transform writes
    // are boundaries. Rail resolution and distance advancement execute production code.
    readonly List<Railload> AcquisitionRails = new List<Railload>();
    readonly List<Railload> railCandidateScratch = new List<Railload>();
    int AcquisitionSearchCount;
    float CurrentVehicleSignedSpeed;
    float EffectiveVehicleMaxSpeed => 1;
    float railMoveSpeedMultiplier = 1;
    bool TryFindLockedBranchRailSample(Vector2 point, Vector2 direction, float limit, out RailSample sample)
    { sample = default; return false; }
    void CollectRailCandidates(Vector2 point)
    { AcquisitionSearchCount++; railCandidateScratch.Clear(); railCandidateScratch.AddRange(AcquisitionRails); }
    bool ShouldReleaseForeignPushForReverseInput(float input, float speed) => false;
    void ClearPushConsistPathSessions() { }
    float UpdateVehicleSignedSpeed(float axis, float dt, float limit) => CurrentVehicleSignedSpeed = axis;
    new void ClampCurrentVehicleSignedSpeed(float limit) { }
    float AdjustDrivenSignedStep(RailSample sample, Vector2 facing, bool hasInput, Vector2 input, float dt, float step) => step;
    bool TryApplyIdleDocking(RailSample sample, Vector2 facing, float dt) => false;
    void RecordRailMoveFailureIfEmpty(string reason)
    { if (string.IsNullOrEmpty(MoveFailure)) MoveFailure = reason; }
    void ApplyRailPose(RailSample sample, Vector2 facing, float dt, bool snap)
        => TryApplyRailPose(sample.Rail, sample.DistanceAlongPath, sample.Point, facing);
    bool MoveConnectedTrainGroup(RailSample sample, Vector2 facing, float step, float dt,
        bool hasInput, Vector2 input, bool lockRoute)
    {
        if (!TryAdvanceAlongRailNetwork(sample, step >= 0 ? facing : -facing,
                Math.Abs(step), out var target, out _)) return false;
        ApplyRailPose(target, facing, dt, true);
        return true;
    }
}
