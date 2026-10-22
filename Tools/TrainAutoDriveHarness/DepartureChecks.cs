using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Railload
{
    public float CurveRadius, CurveStart, CurveSign = 1f;
    public Vector2 CurveHeading;
    public void SampleCurve(float distance, out Vector2 point, out Vector2 tangent)
    {
        float angle = (CurveStart + distance * CurveSign) / CurveRadius;
        Vector2 left = new(-CurveHeading.y, CurveHeading.x);
        point = (CurveHeading * Mathf.Sin(angle) + left * (1f - Mathf.Cos(angle))) * CurveRadius;
        tangent = (CurveHeading * Mathf.Cos(angle) + left * Mathf.Sin(angle)) * CurveSign;
    }
    public float ProjectCurve(Vector2 point)
    {
        Vector2 left = new(-CurveHeading.y, CurveHeading.x);
        float angle = Mathf.Atan2(Vector2.Dot(point, CurveHeading), CurveRadius - Vector2.Dot(point, left));
        return (angle * CurveRadius - CurveStart) * CurveSign;
    }
}

// The real preparation, route construction, leader advance, follower sampling
// and pose-application loop execute here. Only world lookup/physics are replaced.
public partial class Train
{
    public float ConnectionSnapMaxDistance => .2f;
    public Vector2? AppliedPoint;
    public bool TryGetConnectionFacingSign(Train other, bool ahead, out float sign)
    { sign = 1f; return false; }
}

public partial class RailHandcar
{
    readonly List<ConnectedTrainRailMove> connectedTrainOrderScratch = new();
    readonly List<Railload> departureRails = new();
    readonly Dictionary<Train, RailSample> appliedRailSamples = new();
    const float PushConsistGapTolerance = .04f;
    float railSnapMaxDistance = .75f;
    static readonly FakeMarker RebuildFollowOffsetsMarker = new();
    struct FakeMarker
    {
        public Scope Auto() => new();
        public struct Scope : IDisposable { public void Dispose() { } }
    }
    Railload selectedInputBranch;
    bool TryFindBranchRailSample(RailSample current, Vector2 input, out RailSample sample)
    {
        sample = default;
        if (selectedInputBranch == null) return false;
        selectedInputBranch.TryFindNearestRenderedPathSample(current.Point, out float distance, out _, out _, out _);
        sample = Sample(selectedInputBranch, distance);
        return true;
    }
    bool TryResolvePreferredConnectedRailTravelDirection(RailSample a, Vector2 direction, RailSample b, out Vector2 travel)
    { travel = direction; return false; }
    bool TryFindConnectedRailSample(RailSample endpoint, Vector2 direction, Railload excluded, out RailSample sample)
        => TryFindConnectedRailSample(endpoint, direction, excluded, null, out sample);
    float ResolveConsistPathTrimPadding() => 1f;
    void RegisterPreparedTrainMovesForCurrentMovementLoad() { }
    void ClearConnectedTrainMovementScratch()
    { connectedTrainRailMoveScratch.Clear(); connectedTrainOrderScratch.Clear(); leaderPathFrameScratch.Clear(); }
    void RotateConnectedTrainWheels(Train train, float distance) { }
    static float EstimateSignedRailSampleMoveDistance(Train train, RailSample a, RailSample b)
        => Vector2.Distance(a.Point, b.Point);
    void ApplyConnectedTrainRailPose(Train train, RailSample sample, Vector2 facing, float dt, float? bodyPathDistance = null)
    {
        train.Rail = sample.Rail;
        train.Distance = sample.DistanceAlongPath;
        train.AppliedPoint = sample.Point;
        appliedRailSamples[train] = sample;
        train.transform.forward = new Vector3(facing.x, 0f, facing.y);
    }

    bool DepartureStep(Vector2 heading, float step, bool routeLock)
    {
        CollectConnectedTrainGroupForMovement(this);
        foreach (Train train in connectedTrainGroupScratch)
        {
            TryBuildCurrentRailSample(train, out RailSample sample);
            if (train.AppliedPoint.HasValue) sample.Point = train.AppliedPoint.Value;
            connectedTrainRailMoveScratch.Add(new ConnectedTrainRailMove
            {
                Train = train, StartSample = sample, TargetSample = sample,
                StartFacingTangent = new Vector2(train.transform.forward.x, train.transform.forward.z)
            });
        }
        bool keptOrder = TryApplyRememberedConsistOrder(heading);
        PrepareConnectedTrainMovesForTravel(this, heading, keptOrder);
        Vector2 physicalFacing = new Vector2(transform.forward.x, transform.forward.z);
        return TryApplyPreparedConnectedTrainMoves(this, physicalFacing, heading, step, .02f, true, heading, routeLock, out _);
    }

    public static void RunDepartureChecks(Action<bool, string> check)
    {
        RunMixedLengthDepartureChecks(check);
        RunBodyEndStopChecks(check);
        RunSelectedBranchContinuityChecks(check);
        foreach (Vector2 heading in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (int railPattern in new[] { 0, 1, 2 })
        foreach (float facing in new[] { -1f, 1f })
        foreach (float driverFacing in new[] { -1f, 1f })
        foreach (bool driveFromTail in new[] { false, true })
        foreach (bool forceLock in new[] { false, true })
        foreach (bool curve in new[] { false, true })
        {
            var driver = new RailHandcar();
            for (int r = 0; r < 10; r++)
            {
                bool reversed = railPattern == 1 || railPattern == 2 && r % 2 == 0;
                Railload rail = InitialRail(heading, r, 1f, reversed);
                if (curve)
                {
                    rail.CurveRadius = 8f;
                    rail.CurveStart = r + (reversed ? 1f : 0f);
                    rail.CurveSign = reversed ? -1f : 1f;
                    rail.CurveHeading = heading;
                }
                driver.departureRails.Add(rail);
            }
            Train previous = null;
            var cars = new Train[5];
            for (int i = 0; i < cars.Length; i++)
            {
                Train train = i == (driveFromTail ? 0 : cars.Length - 1) ? driver : new RailHandcar();
                train.Rail = driver.departureRails[i + 2];
                train.Distance = .5f;
                Vector2 tangent = DepartureTangent(heading, i + 2.5f, curve);
                train.transform.forward = new Vector3(tangent.x, 0f, tangent.y) * (train == driver ? driverFacing : facing);
                if (previous != null) Train.Link(previous, train);
                cars[i] = train;
                previous = train;
            }
            float moved = 0f;
            string scenario = $"railPattern={railPattern}, facing={facing}, driverFacing={driverFacing}, tailDriver={driveFromTail}, lock={forceLock}, curve={curve}";
            for (int frame = 0; frame < 80; frame++)
            {
                Vector2 travel = DepartureTangent(heading, (driveFromTail ? 2.5f : 6.5f) + moved, curve);
                bool advanced = driver.DepartureStep(travel * driverFacing, .015f, forceLock && frame == 0);
                check(advanced, $"Departure failed: {scenario}, frame={frame}, reason={driver.railMoveFailureReason}");
                moved += .015f * driverFacing;
                for (int i = 0; i < cars.Length; i++)
                {
                    Vector2 expected = DeparturePoint(heading, i + 2.5f + moved, curve);
                    check(cars[i].AppliedPoint.HasValue && Vector2.Distance(cars[i].AppliedPoint.Value, expected) < .002f,
                        $"Departure must keep one-cell spacing: {scenario}, frame={frame}, car={i}, expected={expected}, actual={cars[i].AppliedPoint}");
                    Vector2 expectedFacing = DepartureTangent(heading, i + 2.5f + moved, curve)
                        * (cars[i] == driver ? driverFacing : facing);
                    Vector2 actualFacing = new Vector2(cars[i].transform.forward.x, cars[i].transform.forward.z);
                    check(Vector2.Dot(actualFacing.normalized, expectedFacing.normalized) > .999f,
                        $"Departure reversed the physical front: {scenario}, frame={frame}, car={i}, expected={expectedFacing}, actual={actualFacing}");
                }
            }
        }
    }

    static void RunMixedLengthDepartureChecks(Action<bool, string> check)
    {
        foreach (Vector2 heading in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reversed in new[] { false, true })
        foreach (bool curve in new[] { false, true })
        foreach (bool rearDriver in new[] { false, true })
        foreach (bool alternateFacing in new[] { false, true })
        {
            var driver = new RailHandcar();
            for (int r = 0; r < 30; r++)
            {
                var rail = InitialRail(heading, r, 1f, reversed);
                if (curve)
                {
                    rail.CurveRadius = 8;
                    rail.CurveStart = r + (reversed ? 1 : 0);
                    rail.CurveSign = reversed ? -1 : 1;
                    rail.CurveHeading = heading;
                }
                driver.departureRails.Add(rail);
            }

            var cars = new Train[5];
            var offsets = new float[5];
            float position = 3.5f;
            for (int i = 0; i < cars.Length; i++)
            {
                Train car = i == (rearDriver ? 0 : cars.Length - 1) ? driver : new RailHandcar();
                bool isLong = i % 2 == 0;
                float sign = alternateFacing && i % 2 == 0 ? -1 : 1;
                car.FrontConnectionReach = isLong ? 1.5f : .5f;
                car.RearConnectionReach = .5f;
                car.RailFrontClearance = isLong ? 1.45f : .45f;
                car.RailRearClearance = .45f;
                if (i > 0)
                {
                    float previousSign = alternateFacing && (i - 1) % 2 == 0 ? -1 : 1;
                    position += cars[i - 1].ResolveConnectionReach(previousSign > 0)
                        + car.ResolveConnectionReach(sign < 0);
                    Train.Link(cars[i - 1], car);
                }
                offsets[i] = position;
                int segment = (int)Math.Floor(position);
                float fraction = position - segment;
                car.Rail = driver.departureRails[segment];
                car.Distance = reversed ? 1 - fraction : fraction;
                Vector2 facing = DepartureTangent(heading, position, curve) * sign;
                car.transform.forward = new Vector3(facing.x, 0, facing.y);
                cars[i] = car;
            }

            float moved = 0;
            foreach (float direction in new[] { 1f, -1f })
            for (int frame = 0; frame < 60; frame++)
            {
                float driverDistance = offsets[rearDriver ? 0 : cars.Length - 1] + moved;
                Vector2 travel = DepartureTangent(heading, driverDistance, curve) * direction;
                check(driver.DepartureStep(travel, .03f, false),
                    $"Mixed-length consist must cross junctions and reverse: curve={curve}, reversed={reversed}, rearDriver={rearDriver}, frame={frame}, failure={driver.railMoveFailureReason}");
                moved += .03f * direction;
                for (int i = 0; i < cars.Length; i++)
                {
                    check(cars[i].AppliedPoint.HasValue
                            && Vector2.Distance(cars[i].AppliedPoint.Value, DeparturePoint(heading, offsets[i] + moved, curve)) < .002f,
                        "Movement must preserve each mixed-length root offset rather than contracting to one-cell spacing");
                    float sign = alternateFacing && i % 2 == 0 ? -1 : 1;
                    Vector2 expected = DepartureTangent(heading, offsets[i] + moved, curve) * sign;
                    Vector2 actual = new Vector2(cars[i].transform.forward.x, cars[i].transform.forward.z);
                    check(Vector2.Dot(actual.normalized, expected.normalized) > .999f,
                        $"Mixed-length facing changed: curve={curve}, reversed={reversed}, rearDriver={rearDriver}, alternate={alternateFacing}, direction={direction}, frame={frame}, car={i}, expected={expected}, actual={actual}");
                }
            }
        }
    }

    static void RunBodyEndStopChecks(Action<bool, string> check)
    {
        foreach (Vector2 heading in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reversed in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        foreach (bool rearDriver in new[] { false, true })
        foreach (float step in new[] { .1f, 20f })
        {
            var driver = new RailHandcar();
            var rail = InitialRail(heading, 0, 10, reversed);
            driver.departureRails.Add(rail);
            Vector2 facing = forward ? heading : -heading;
            driver.Rail = rail;
            driver.Distance = reversed ? (rearDriver ? 6 : 5) : (rearDriver ? 4 : 5);
            driver.transform.forward = new Vector3(facing.x, 0, facing.y);
            driver.RailFrontClearance = 1.35f;
            driver.RailRearClearance = .35f;
            Train leading = driver;
            if (rearDriver)
            {
                leading = new RailHandcar
                {
                    Rail = rail, Distance = 5,
                    RailFrontClearance = 1.35f, RailRearClearance = .35f
                };
                leading.transform.forward = driver.transform.forward;
                Train.Link(driver, leading);
            }

            float clearance = forward ? 1.35f : .35f;
            for (int frame = 0; frame < 100; frame++)
            {
                if (!driver.DepartureStep(heading, step, false)) break;
                check(Vector2.Dot(leading.AppliedPoint.Value, heading) <= 10 - clearance + .0002f,
                    "Prepared consist movement must stop the physical leading end before the dead end");
            }
            check(leading.AppliedPoint.HasValue
                    && Math.Abs(Vector2.Dot(leading.AppliedPoint.Value, heading) - (10 - clearance)) < .0002f,
                "The leading car, not the controlled engine, determines the consist's end margin");
            check(!driver.DepartureStep(heading, step, false), "Repeated forward input at the body limit must not report movement");
            check(driver.DepartureStep(-heading, .1f, false), "A consist at its body limit must be able to reverse away");
        }

        var docking = new RailHandcar
        {
            Rail = InitialRail(Vector2.right, 0, 10, false), Distance = 8.64f,
            RailFrontClearance = 1.35f, RailRearClearance = .35f
        };
        docking.transform.forward = Vector3.right;
        check(!docking.Dock(.02f, true) && docking.DockMovement == 0,
            "A single-car docking snap must not bypass the physical front end limit");
        check(docking.Dock(.01f, true) && Math.Abs(docking.DockMovement - .01f) < .0001f,
            "A docking snap with sufficient track support must remain available");
    }

    static void RunSelectedBranchContinuityChecks(Action<bool, string> check)
    {
        foreach (Vector2 heading in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reversed in new[] { false, true })
        {
            var driver = new RailHandcar();
            var source = InitialRail(heading, 0, 3, false);
            var target = InitialRail(heading, 3.04f, 3, reversed);
            driver.selectedInputBranch = target;
            var leader = InitialCar(source, heading * 3, heading);
            var rear = InitialCar(source, heading * 2, heading);
            driver.connectedTrainRailMoveScratch.Add(leader);
            driver.connectedTrainRailMoveScratch.Add(rear);
            Train.Link(leader.Train, rear.Train);
            check(driver.EnsureConsistPathTape(heading), "Branch continuity fixture must initialize the existing two-car route");
            check(driver.TryApplyPreparedConnectedTrainMoves(rear.Train, heading, heading, .01f, .02f, true, heading, false, out _),
                "Connected leader must traverse an input-selected gap without losing its route");
            check(Vector2.Distance(leader.Train.AppliedPoint.Value, heading * 3) <= .01001f,
                "Connected leader must not gain free distance on branch selection");
            check(Vector2.Distance(rear.Train.AppliedPoint.Value, heading * 2) <= .01001f,
                "Connected rear car must retain the same bounded frame movement");
            check(Vector2.Distance(leader.Train.AppliedPoint.Value, heading * 3.01f) < .00001f
                && Vector2.Distance(rear.Train.AppliedPoint.Value, heading * 2.01f) < .00001f,
                "Input-selected gap must preserve forward progress and one-cell spacing");
        }
    }

    static Vector2 DeparturePoint(Vector2 heading, float distance, bool curve)
        => curve ? (heading * Mathf.Sin(distance / 8f) + new Vector2(-heading.y, heading.x) * (1f - Mathf.Cos(distance / 8f))) * 8f : heading * distance;
    static Vector2 DepartureTangent(Vector2 heading, float distance, bool curve)
        => curve ? heading * Mathf.Cos(distance / 8f) + new Vector2(-heading.y, heading.x) * Mathf.Sin(distance / 8f) : heading;
}
