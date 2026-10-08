using System;
using UnityEngine;

static class ManualFacingChecks
{
    public static void Run()
    {
        int checks = 0;
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (float frontSign in new[] { -1f, 1f })
        foreach (float inputSign in new[] { -1f, 1f })
        foreach (bool reverseRail in new[] { false, true })
        foreach (bool curve in new[] { false, true })
        {
            Vector2 previousFront = axis * frontSign;
            Vector2 input = previousFront * inputSign;
            Vector2 side = new Vector2(-axis.y, axis.x);
            Vector2 branchAxis = (axis + (curve ? side * .25f : Vector2.zero)).normalized;
            Vector2 branchTravel = branchAxis * frontSign * inputSign;
            var source = new Railload(Vector2.zero, axis * 5);
            var branch = reverseRail ? new Railload(branchTravel * 5, Vector2.zero) : new Railload(Vector2.zero, branchTravel * 5);
            var engine = new RailHandcar();
            engine.TryApplyRailPose(source, 0, Vector2.zero, previousFront);
            engine.ManualBranchRail = branch;
            engine.TestManualInput(new Vector3(input.x, 0, input.y), 1, .02f);
            string scenario = $"front={previousFront}, input={input}, reversedRail={reverseRail}, curve={curve}";
            if (Vector2.Dot(engine.TestManualFacing, branchAxis * frontSign) < .999f)
                throw new Exception("Manual rail selection reversed the locomotive's physical front: " + scenario);
            checks++;
            if (Mathf.Abs(engine.TestManualInputAxis) < .5f || Mathf.Sign(engine.TestManualInputAxis) != inputSign)
                throw new Exception("Manual rail selection changed forward/reverse input: " + scenario);
            checks++;
            if (engine.ManualLockCount != 1)
                throw new Exception("Regression must actually execute the manual rail-switch branch: " + scenario);
            checks++;
        }
        Console.WriteLine($"Manual locomotive rail selection passed: {checks} checks");
    }
}

public partial class RailHandcar
{
    public Railload ManualBranchRail;
    public Vector2 TestManualFacing;
    RailSample TestManualSample;
    public float TestManualInputAxis;
    public int ManualLockCount;
    float railInputDeadZone = .05f, railSnapMaxDistance = .75f;
    string MoveFailure;
    void RecordRailMoveFailure(string reason) { MoveFailure = reason; }
    void LogRailMoveFailure() { }
    void ClearLockedBranchRail() { }
    new void ResetVehicleMotion() { CurrentVehicleSignedSpeed = 0; }
    void BeginCurrentMovementLoadTracking() { }
    Vector2 ResolveCoastTravelDirection() => ResolveReferenceFacing();
    Vector2 ResolveCoastFacingDirection() => ResolveReferenceFacing();
    bool HasConnectedTrainAhead(Vector2 direction) => false;
    void LockBranchRail(Railload rail) { ManualLockCount++; }
    bool TryFindBranchRailSample(RailSample current, Vector2 input, out RailSample sample)
    {
        sample = default;
        if (ManualBranchRail == null) return false;
        ManualBranchRail.TryFindNearestRenderedPathSample(current.Point, out float distance, out _, out _, out _);
        return TryCreateRailSampleAtDistance(ManualBranchRail, distance, out sample);
    }
    float ResolveRailInputAxis(bool hasInput, Vector2 input, float magnitude, Vector2 facing, RailSample sample)
        => hasInput ? Vector2.Dot(input, facing) * magnitude : 0;
}
