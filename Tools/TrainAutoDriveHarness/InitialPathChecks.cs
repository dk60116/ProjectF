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
        RailSample endpoint, Vector2 direction, Railload excluded, Railload preferred, out RailSample sample)
    {
        sample = default;
        float best = float.MaxValue;
        foreach (Railload rail in departureRails)
        {
            if (rail == excluded || rail == endpoint.Rail) continue;
            rail.TryFindNearestRenderedPathSample(endpoint.Point, out float distance, out Vector2 point, out Vector2 tangent, out float sqr);
            float available = Vector2.Dot(direction, tangent) > 0 ? rail.Length - distance : distance;
            if (sqr > .000001f || available < .0001f || sqr >= best) continue;
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
