using System;
using UnityEngine;

public partial class RailHandcar
{
    public static void RunJunctionFacingChecks(Action<bool, string> check)
    {
        // Editor.log [RailFacingFlip] frames 2372, 2682 and 3105: a solo
        // locomotive reverses across a bridge starting inside its source rail.
        foreach (var record in new[] {
            (frame: 2372, sourceDistance: 4.964030f, progress: 0f, step: .064656f, sign: 1f),
            (frame: 2682, sourceDistance: 4.854084f, progress: .063830f, step: .029886f, sign: 1f),
            (frame: 3105, sourceDistance: 4.873495f, progress: .051652f, step: .027890f, sign: -1f)
        })
        {
            var driver = new RailHandcar();
            var source = new Railload { Origin = new Vector2(1, record.sign > 0 ? -6.5f : 3.5f),
                Direction = Vector2.up * record.sign, Length = 5 };
            var target = new Railload { Origin = new Vector2(1, record.sign > 0 ? 3.5f : -6.5f),
                Direction = Vector2.down * record.sign, Length = 5 };
            Vector2 travel = Vector2.up * record.sign, front = -travel;
            RailSample start = Sample(source, record.sourceDistance), entry = Sample(target, 5);
            float length = Vector2.Distance(start.Point, entry.Point);
            check(TryCreateRailConnectionBridgeSample(start, entry, length, record.progress, out RailSample bridge),
                "Logged rail transition must form a valid bridge");
            driver.SeedJunctionPose(bridge, front);
            check(driver.AdvanceJunctionFrame(travel, record.step, record.progress == 0),
                $"Logged frame {record.frame} must advance: {driver.railMoveFailureReason}");
            Vector2 actualFront = new Vector2(driver.transform.forward.x, driver.transform.forward.z);
            check(Vector2.Dot(front, actualFront) > .999f,
                $"Logged frame {record.frame} flipped a reversing solo locomotive: front={front}, actual={actualFront}, progress={record.progress}/{length}");
        }
        RunContinuousJunctionChecks(check);
    }

    static void RunContinuousJunctionChecks(Action<bool, string> check)
    {
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reverseSource in new[] { false, true })
        foreach (bool reverseTarget in new[] { false, true })
        foreach (float frontSign in new[] { -1f, 1f })
        foreach (bool interiorTarget in new[] { false, true })
        foreach (bool curve in new[] { false, true })
        foreach (float step in new[] { .007f, .07f })
        {
            var driver = new RailHandcar();
            float targetStart = interiorTarget ? 4.95f : 5f;
            var source = InitialRail(axis, 0, 5, reverseSource);
            var target = InitialRail(axis, targetStart, 5, reverseTarget);
            if (curve)
            {
                source.CurveRadius = target.CurveRadius = 8;
                source.CurveHeading = target.CurveHeading = axis;
                source.CurveStart = reverseSource ? 5 : 0;
                target.CurveStart = targetStart + (reverseTarget ? 5 : 0);
                source.CurveSign = reverseSource ? -1 : 1;
                target.CurveSign = reverseTarget ? -1 : 1;
            }
            RailSample start = Sample(source, reverseSource ? .145916f : 4.854084f);
            float targetDistance = 5 - targetStart;
            RailSample entry = Sample(target, reverseTarget ? 5 - targetDistance : targetDistance);
            Vector2 front = start.Tangent * (reverseSource ? -1 : 1) * frontSign;
            check(TryPrepareBranchRailTransition(start, entry, out RailSample bridge),
                "Continuous junction fixture must prepare the production input transition");
            driver.SeedJunctionPose(bridge, front);
            string scenario = $"axis={axis}, sourceReversed={reverseSource}, targetReversed={reverseTarget}, front={frontSign}, interior={interiorTarget}, curve={curve}, step={step}";
            for (int frame = 0; frame < 40; frame++)
            {
                Vector2 previousPoint = driver.AppliedPoint.Value;
                Vector2 previousFront = new Vector2(driver.transform.forward.x, driver.transform.forward.z);
                // Feed the applied facing into the next frame as manual driving does.
                check(driver.AdvanceJunctionFrame(previousFront * frontSign, step, frame == 0),
                    $"Continuous junction stopped: {scenario}, frame={frame}, reason={driver.railMoveFailureReason}");
                Vector2 actualFront = new Vector2(driver.transform.forward.x, driver.transform.forward.z);
                check(Vector2.Dot(actualFront, previousFront) > .99f,
                    $"Continuous junction reversed the physical front: {scenario}, frame={frame}");
                check(Vector2.Distance(previousPoint, driver.AppliedPoint.Value) <= step + .00001f,
                    $"Continuous junction exceeded requested movement: {scenario}, frame={frame}");
            }
            check(driver.Rail == target, $"Continuous junction must leave the bridge on the destination rail: {scenario}");
        }
    }

    void SeedJunctionPose(RailSample sample, Vector2 front)
    {
        Rail = sample.Rail; Distance = sample.DistanceAlongPath;
        AppliedPoint = sample.Point;
        appliedRailSamples[this] = sample;
        transform.position = new Vector3(sample.Point.x, 0, sample.Point.y);
        transform.forward = new Vector3(front.x, 0, front.y);
    }

    bool AdvanceJunctionFrame(Vector2 travel, float step, bool lockRoute)
    {
        RailSample current = appliedRailSamples[this];
        Vector2 front = new Vector2(transform.forward.x, transform.forward.z);
        // Train stores the applied physical facing separately from raw rail authoring.
        current.Tangent = front;
        connectedTrainRailMoveScratch.Clear();
        connectedTrainRailMoveScratch.Add(new ConnectedTrainRailMove {
            Train = this, StartSample = current, TargetSample = current, StartFacingTangent = front
        });
        return TryApplyPreparedConnectedTrainMoves(this, front, travel, step, .02f,
            true, travel, lockRoute, out _);
    }
}
