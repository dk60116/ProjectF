using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Railload
{
    public float Length = 100f;
    public bool TryGetRenderedPathLength(out float length) { length = Length; return true; }
    public bool TryFindNearestRenderedPathSample(
        Vector2 target, out float distance, out Vector2 point, out Vector2 tangent, out float sqrDistance)
    {
        distance = Mathf.Clamp(CurveRadius > 0f ? ProjectCurve(target) : Vector2.Dot(target - Origin, Direction), 0f, Length);
        TrySampleRenderedPath(distance, out point, out tangent);
        sqrDistance = (target - point).sqrMagnitude;
        return true;
    }
}

public partial class RailHandcar
{
    readonly List<float> actualConsistDistanceScratch = new();
    readonly List<InitialConsistPathRouteNode> initialConsistPathRouteNodes = new();
    readonly Queue<int> initialConsistPathRouteQueue = new();
    readonly List<RailSample> railConnectionSampleScratch = new();

    void RecordRailMoveFailureIfEmpty(string reason)
    {
        if (string.IsNullOrEmpty(railMoveFailureReason)) RecordRailMoveFailure(reason);
    }
    float ResolveInitialConsistSegmentSearchDistance(ConnectedTrainRailMove a, ConnectedTrainRailMove b) => 4f;
    static float EstimateConsistSampleDistance(ConnectedTrainRailMove a, ConnectedTrainRailMove b)
        => Vector2.Distance(a.StartSample.Point, b.StartSample.Point);
    Vector2 ResolveFacingTangent(Vector2 tangent, Vector2 direction)
        => ResolveFacingTangentWithFallback(tangent, direction, Vector2.right);

    // Initial-path fixtures leave this world empty; departure fixtures expose
    // endpoint-linked rails to exercise real traversal instead of a preset tape.
    bool TryFindConnectedRailSample(
        RailSample endpoint, Vector2 direction, Railload excluded, Railload preferred, out RailSample sample, bool ignoreRouting = false)
    {
        sample = default;
        float best = float.MaxValue;
        foreach (Railload rail in departureRails)
        {
            if (rail == excluded || rail == endpoint.Rail) continue;
            rail.TryFindNearestRenderedPathSample(endpoint.Point, out float distance, out Vector2 point, out Vector2 tangent, out float sqr);
            float available = Vector2.Dot(direction, tangent) > 0 ? rail.Length - distance : distance;
            if (sqr > ResolveRailConnectionMaxDistance() * ResolveRailConnectionMaxDistance()
                || available < .0001f || sqr >= best) continue;
            best = sqr;
            sample = Sample(rail, distance);
        }
        return sample.Rail != null;
    }
    bool TryCollectConnectedRailSamples(
        RailSample endpoint, Vector2 direction, Railload excluded, List<RailSample> samples, bool unused)
    {
        samples.Clear();
        if (!TryFindConnectedRailSample(endpoint, direction, excluded, null, out RailSample sample)) return false;
        samples.Add(sample);
        return true;
    }

    static Railload InitialRail(Vector2 heading, float start, float length, bool reversed)
        => new Railload
        {
            Origin = heading * (start + (reversed ? length : 0f)),
            Direction = heading * (reversed ? -1f : 1f),
            Length = length
        };

    static ConnectedTrainRailMove InitialCar(Railload rail, Vector2 point, Vector2 facing)
    {
        float distance = Vector2.Dot(point - rail.Origin, rail.Direction);
        var train = new Train { Rail = rail, Distance = distance };
        train.transform.forward = new Vector3(facing.x, 0f, facing.y);
        TryBuildCurrentRailSample(train, out RailSample sample);
        return new ConnectedTrainRailMove
        {
            Train = train, StartSample = sample, TargetSample = sample, StartFacingTangent = facing
        };
    }

    public static void RunInitialPathChecks(Action<bool, string> check)
    {
        foreach (Vector2 heading in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reverseA in new[] { false, true })
        foreach (bool reverseB in new[] { false, true })
        foreach (float middleFacingSign in new[] { -1f, 1f })
        {
            var driver = new RailHandcar();
            var a = InitialRail(heading, 0f, 2.5f, reverseA);
            var b = InitialRail(heading, 1.5f, 2.5f, reverseB);
            driver.connectedTrainRailMoveScratch.Add(InitialCar(b, heading * 2f, heading));
            driver.connectedTrainRailMoveScratch.Add(InitialCar(a, heading, heading * middleFacingSign));
            driver.connectedTrainRailMoveScratch.Add(InitialCar(a, Vector2.zero, heading));
            check(driver.InitializeConsistPathTape(heading),
                "First departure must initialize from one-cell poses, regardless of rail or car orientation");

            float startDistance = driver.consistPathEndDistance;
            check(Math.Abs(startDistance - 2f) < .0001f,
                "Fallback search must keep the occupied route length without a failed detour");
            RailSample leader = driver.connectedTrainRailMoveScratch[0].StartSample;
            driver.AddConsistPathSample(driver.consistPathTape, startDistance, leader);
            b.TryFindNearestRenderedPathSample(heading * 3f, out float endDistance, out _, out _, out _);
            driver.AddConsistPathSample(driver.consistPathTape, startDistance + 1f, Sample(b, endDistance));

            foreach (float step in new[] { 0f, .01f, .1f, .25f, .5f, .75f, 1f })
            for (int i = 0; i < 3; i++)
            {
                float offset = driver.connectedTrainRailMoveScratch[i].FollowOffset;
                check(driver.TrySampleConsistPathTape(startDistance + step - offset, out RailSample pose, false)
                      && Vector2.Distance(pose.Point, heading * (2f - i + step)) < .0001f,
                    "Every car must advance by the leader's distance on first departure, retaining one-cell spacing");
            }
        }

        CheckRouteReconstruction(check);
        CheckBridgeProgressSpacing(check);
        CheckBridgeTapeInterpolation(check);
    }

    static void CheckBridgeTapeInterpolation(Action<bool, string> check)
    {
        var driver = new RailHandcar();
        var source = InitialRail(Vector2.right, 0f, 10f, false);
        var target = InitialRail(Vector2.right, 10.4f, 10f, false);
        RailSample sourceEnd = Sample(source, 10f);
        RailSample targetStart = Sample(target, 0f);
        check(TryCreateRailConnectionBridgeSample(sourceEnd, targetStart, .4f, .3f,
                out RailSample bridge), "Interpolation fixture must enter the rail bridge");

        driver.AddConsistPathSample(driver.consistPathTape, 0f, Sample(source, 9f));
        driver.AddConsistPathSample(driver.consistPathTape, 1.3f, bridge);
        check(driver.TrySampleConsistPathTape(.5f, out RailSample beforeBridge, false)
              && Math.Abs(beforeBridge.Point.x - 9.5f) < .0001f
              && Math.Abs(beforeBridge.DistanceAlongPath - 9.5f) < .0001f
              && !HasRailConnectionBridgeState(beforeBridge),
            "A rail-to-bridge tape segment must retain the real rail distance before its endpoint");
        check(driver.TrySampleConsistPathTape(1.2f, out RailSample onBridge, false)
              && Math.Abs(onBridge.Point.x - 10.2f) < .0001f
              && Math.Abs(onBridge.DistanceAlongPath - 10f) < .0001f
              && HasRailConnectionBridgeState(onBridge),
            "A rail-to-bridge tape segment must enter the bridge at its endpoint");

        driver.consistPathTape.Clear();
        driver.AddConsistPathSample(driver.consistPathTape, 0f, bridge);
        driver.AddConsistPathSample(driver.consistPathTape, 1.1f, Sample(target, 1f));
        check(driver.TrySampleConsistPathTape(.05f, out RailSample leavingBridge, false)
              && Math.Abs(leavingBridge.Point.x - 10.35f) < .0001f
              && HasRailConnectionBridgeState(leavingBridge),
            "A bridge-to-rail tape segment must finish the bridge before entering the target rail");
        check(driver.TrySampleConsistPathTape(.6f, out RailSample afterBridge, false)
              && Math.Abs(afterBridge.Point.x - 10.9f) < .0001f
              && Math.Abs(afterBridge.DistanceAlongPath - .5f) < .0001f
              && !HasRailConnectionBridgeState(afterBridge),
            "A bridge-to-rail tape segment must store the target rail's distance after exit");

        driver.consistPathTape.Clear();
        driver.AddConsistPathSample(driver.consistPathTape, 0f, bridge);
        driver.AddConsistPathSample(driver.consistPathTape, 1.3f, Sample(source, 9f));
        check(driver.TrySampleConsistPathTape(.8f, out RailSample backingOntoSource, false)
              && Math.Abs(backingOntoSource.Point.x - 9.5f) < .0001f
              && Math.Abs(backingOntoSource.DistanceAlongPath - 9.5f) < .0001f
              && !HasRailConnectionBridgeState(backingOntoSource),
            "Reversing from a bridge must restore the source rail distance without a false bridge");
    }

    static void CheckBridgeProgressSpacing(Action<bool, string> check)
    {
        foreach (float connectionLength in new[] { .04f, .4f })
        {
            var driver = new RailHandcar();
            var source = InitialRail(Vector2.right, 0f, 10f, false);
            var target = InitialRail(Vector2.right, 10f + connectionLength, 10f, false);
            driver.departureRails.Add(source);
            driver.departureRails.Add(target);

            float progress = connectionLength * .75f;
            RailSample exit = Sample(source, 10f);
            RailSample entry = Sample(target, 0f);
            check(TryCreateRailConnectionBridgeSample(exit, entry, connectionLength, progress,
                    out RailSample bridge), "Bridge spacing fixture must create an in-progress connection");
            RailSample tailSample = Sample(source, 8f + progress);
            RailSample leaderSample = Sample(target, 2f + progress - connectionLength);
            RailSample earlyBridge;
            check(TryCreateRailConnectionBridgeSample(exit, entry, connectionLength,
                    connectionLength * .25f, out earlyBridge),
                "Bridge spacing fixture must create two positions on the same connection");

            check(driver.TryGetDistanceToTargetOnCurrentRail(tailSample, bridge, Vector2.right,
                      out float tailToBridge) && Math.Abs(tailToBridge - 2f) < .0001f,
                "Same-rail distance must include target bridge progress");
            check(driver.TryGetDistanceToTargetOnCurrentRail(earlyBridge, bridge, Vector2.right,
                      out float bridgeToBridge)
                  && Math.Abs(bridgeToBridge - connectionLength * .5f) < .0001f,
                "Two positions on one bridge must retain their progress difference");

            var leader = InitialCar(target, leaderSample.Point, Vector2.right);
            var middle = InitialCar(source, exit.Point, Vector2.right);
            middle.StartSample = bridge;
            middle.TargetSample = bridge;
            var tail = InitialCar(source, tailSample.Point, Vector2.right);
            foreach (ConnectedTrainRailMove car in new[] { leader, middle, tail })
            {
                car.Train.FrontConnectionReach = 1f;
                car.Train.RearConnectionReach = 1f;
                driver.connectedTrainRailMoveScratch.Add(car);
            }

            Train.Link(leader.Train, middle.Train);
            Train.Link(middle.Train, tail.Train);
            check(driver.InitializeConsistPathTape(Vector2.right),
                "Bridge spacing path must initialize from the cars' actual positions");
            check(Math.Abs(driver.connectedTrainRailMoveScratch[1].FollowOffset - 2f) < .0001f,
                "Rebuilt middle-car offset must include consumed bridge distance");
            check(Math.Abs(driver.connectedTrainRailMoveScratch[2].FollowOffset - 4f) < .0001f,
                "Rebuilt tail-car offset must retain both physical gaps");
            check(driver.TryApplyPreparedConnectedTrainMoves(leader.Train, Vector2.right,
                    Vector2.right, .001f, .02f, true, Vector2.right, false, out _),
                "A consist reconstructed on a bridge must move");
            if (driver.appliedRailSamples.TryGetValue(leader.Train, out RailSample movedLeader)
                && driver.appliedRailSamples.TryGetValue(middle.Train, out RailSample movedMiddle)
                && driver.appliedRailSamples.TryGetValue(tail.Train, out RailSample movedTail))
            {
                check(!HasRailConnectionBridgeState(movedTail),
                    "A car before the connection must not inherit bridge state");
                check(Math.Abs(Vector2.Distance(movedLeader.Point, movedMiddle.Point) - 2f) < .002f
                      && Math.Abs(Vector2.Distance(movedMiddle.Point, movedTail.Point) - 2f) < .002f,
                    "Bridge rebuild must not pull the middle car backward or split its spacing");
            }
            else
            {
                check(false, "Bridge spacing move must apply all three car poses");
            }

            if (driver.appliedRailSamples.ContainsKey(leader.Train)
                && driver.appliedRailSamples.ContainsKey(middle.Train)
                && driver.appliedRailSamples.ContainsKey(tail.Train))
            {
                QueueBridgeMovesInReverse(driver, tail.Train, middle.Train, leader.Train);

                check(driver.TryReverseConsistPathTape(Vector2.left),
                    "Recorded bridge route must reverse without rebuilding incorrect offsets");
                check(driver.TryApplyPreparedConnectedTrainMoves(tail.Train, Vector2.right,
                        Vector2.left, .001f, .02f, true, Vector2.left, false, out _),
                    "Consist must move backward after reversing on the bridge: " + driver.railMoveFailureReason);
                if (driver.appliedRailSamples.TryGetValue(leader.Train, out movedLeader)
                    && driver.appliedRailSamples.TryGetValue(middle.Train, out movedMiddle)
                    && driver.appliedRailSamples.TryGetValue(tail.Train, out movedTail))
                {
                    check(Math.Abs(Vector2.Distance(movedLeader.Point, movedMiddle.Point) - 2f) < .002f
                          && Math.Abs(Vector2.Distance(movedMiddle.Point, movedTail.Point) - 2f) < .002f,
                        "Direction reversal on a bridge must preserve both car gaps");

                    driver.ResetConsistPathTape();
                    QueueBridgeMovesInReverse(driver, tail.Train, middle.Train, leader.Train);

                    check(driver.InitializeConsistPathTape(Vector2.left),
                        "A reversed consist must rebuild its route while a car occupies the bridge");
                    check(driver.TryApplyPreparedConnectedTrainMoves(tail.Train, Vector2.right,
                            Vector2.left, .001f, .02f, true, Vector2.left, false, out _),
                        "Rebuilt reversed consist must move across the bridge");
                    if (driver.appliedRailSamples.TryGetValue(leader.Train, out movedLeader)
                        && driver.appliedRailSamples.TryGetValue(middle.Train, out movedMiddle)
                        && driver.appliedRailSamples.TryGetValue(tail.Train, out movedTail))
                    {
                        check(Math.Abs(Vector2.Distance(movedLeader.Point, movedMiddle.Point) - 2f) < .002f
                              && Math.Abs(Vector2.Distance(movedMiddle.Point, movedTail.Point) - 2f) < .002f,
                            "Rebuilt reverse route must not change the car gaps");
                    }
                }
            }
        }
    }

    static void QueueBridgeMovesInReverse(RailHandcar driver, Train tail, Train middle, Train leader)
    {
        foreach (Train train in new[] { tail, middle, leader })
        {
            RailSample sample = driver.appliedRailSamples[train];
            driver.connectedTrainRailMoveScratch.Add(new ConnectedTrainRailMove
            {
                Train = train,
                StartSample = sample,
                TargetSample = sample,
                StartFacingTangent = Vector2.right
            });
        }
    }

    static void CheckRouteReconstruction(Action<bool, string> check)
    {
        var driver = new RailHandcar();
        var a = InitialRail(Vector2.right, 0f, 2f, false);
        var b = InitialRail(Vector2.right, 2f, 2f, true);
        var c = InitialRail(Vector2.right, 2f, 2f, false);
        var nodes = new[] { Sample(a, 1f), Sample(a, 2f), Sample(b, 2f), Sample(c, 0f), Sample(c, 1f) };
        var distances = new[] { 7f, 8f, 8f, 8f, 9f };
        for (int i = 0; i < nodes.Length; i++)
            driver.AddInitialConsistPathRouteNode(nodes[i], Vector2.right, i - 1, distances[i]);

        // A failed scan can leave samples before, inside and beyond the good route.
        foreach (float distance in new[] { 6f, 7.5f, 10f })
            driver.AddConsistPathSample(driver.initialConsistSegmentScratch, distance, Sample(a, 0f));
        driver.RebuildInitialConsistPathSamples(nodes.Length - 1, driver.initialConsistSegmentScratch);
        check(driver.initialConsistSegmentScratch.Count == nodes.Length,
            "Successful fallback must replace all failed samples, including samples beyond its endpoint");
        for (int i = 0; i < nodes.Length; i++)
        {
            ConsistPathSample sample = driver.initialConsistSegmentScratch[i];
            check(sample.Sample.Rail == nodes[i].Rail && Math.Abs(sample.Distance - distances[i]) < .0001f,
                "Equal-distance rail transitions must preserve source-to-target traversal order");
        }
    }
}
