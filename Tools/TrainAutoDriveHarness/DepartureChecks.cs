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
    const float PushConsistGapTolerance = .04f;
    float railSnapMaxDistance = .75f;
    static readonly FakeMarker RebuildFollowOffsetsMarker = new();
    struct FakeMarker
    {
        public Scope Auto() => new();
        public struct Scope : IDisposable { public void Dispose() { } }
    }
    bool TrySwitchRouteLeaderToInputBranch(bool input, Vector2 direction, Vector2 travel) => false;
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
    void ApplyConnectedTrainRailPose(Train train, RailSample sample, Vector2 facing, float dt)
    {
        train.Rail = sample.Rail;
        train.Distance = sample.DistanceAlongPath;
        train.AppliedPoint = sample.Point;
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
        return TryApplyPreparedConnectedTrainMoves(this, heading, heading, step, .02f, true, heading, routeLock, out _);
    }

    public static void RunDepartureChecks(Action<bool, string> check)
    {
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
                }
            }
        }
    }

    static Vector2 DeparturePoint(Vector2 heading, float distance, bool curve)
        => curve ? (heading * Mathf.Sin(distance / 8f) + new Vector2(-heading.y, heading.x) * (1f - Mathf.Cos(distance / 8f))) * 8f : heading * distance;
    static Vector2 DepartureTangent(Vector2 heading, float distance, bool curve)
        => curve ? heading * Mathf.Cos(distance / 8f) + new Vector2(-heading.y, heading.x) * Mathf.Sin(distance / 8f) : heading;
}
