using System;
using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

public partial class RailHandcar
{
    private sealed class BodyProbeDriver : RailHandcar
    {
        internal int CursorChanges;
        protected override bool TryResolvePreferredConnectedRailTravelDirection(
            RailSample endpoint, Vector2 exit, RailSample connected, out Vector2 direction)
        { CursorChanges++; direction = connected.Tangent; return true; }
    }

    public static void RunCurveBodyChecks()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new Exception(message); checks++; }
        Vector2 Flat(Vector3 point) => new Vector2(point.x, point.z);
        var points = new Vector2[241];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = i * MathF.PI / 240f;
            points[i] = new Vector2(3f * MathF.Sin(angle), 3f * (1f - MathF.Cos(angle)));
        }

        foreach (bool reversed in new[] { false, true })
        foreach (float facingSign in new[] { -1f, 1f })
        foreach (int anchorCell in new[] { 0, 1 })
        foreach (float scale in new[] { 1f, 1.5f })
        {
            var railPoints = (Vector2[])points.Clone();
            if (reversed) Array.Reverse(railPoints);
            var rail = new Railload(railPoints);
            var train = new Train { Status = (1, 2), PlacementCenterCell = new Vector2Int(0, anchorCell) };
            train.transform.lossyScale = new Vector3(scale, scale, scale);
            var driver = new RailHandcar { EndStopRails = new List<Railload> { rail } };
            for (float distance = 2f; distance < 7f; distance += .03f)
            {
                TryCreateRailSampleAtDistance(rail, distance, out var sample);
                Vector2 physicalForward = sample.Tangent * facingSign;
                float rear = -anchorCell * scale, front = (1 - anchorCell) * scale;
                rail.TrySampleRenderedPath(distance + facingSign * rear, out Vector2 rp, out _);
                rail.TrySampleRenderedPath(distance + facingSign * front, out Vector2 fp, out _);
                Vector2 expectedForward = (fp - rp).normalized;
                Check(driver.TryResolveTrainBodyRailPose(train, sample, physicalForward, out var pose), "Both supports must sample the existing route");
                Check(Vector2.Dot(pose.Forward, expectedForward) > .99999f, "A long body must follow the support chord, not the rear anchor tangent");
                Check(Vector2.Distance(pose.Position + pose.Forward * ((rear + front) * .5f), (rp + fp) * .5f) < .0001f,
                    "The body midpoint must stay between both rail supports");
                Check(train.TryApplyRailPose(rail, distance, sample.Point, physicalForward), "Common Train pose application must succeed");
                Check(Vector2.Dot(Flat(train.transform.forward), expectedForward) > .99999f &&
                    Vector2.Distance(Flat(train.transform.position), pose.Position) < .0001f, "Placed/restored body pose must use the same support geometry");
                Check(Vector2.Distance(train.Point, sample.Point) < .0001f && Vector2.Dot(train.Facing, physicalForward) > .99999f &&
                    Vector2.Distance(Flat(train.LastAnchorPosition), sample.Point) < .0001f,
                    "Visual correction must preserve logical rail point, physical-end sign and footprint anchor");
            }
        }

        // The forward support is already on the curve before the rear root crosses
        // the join. Reversing the curve's authored path must not change the result.
        foreach (bool reverseCurve in new[] { false, true })
        {
            var straight = new Railload(new Vector2(-3, 0), Vector2.zero);
            var curvePoints = (Vector2[])points.Clone();
            if (reverseCurve) Array.Reverse(curvePoints);
            var curve = new Railload(curvePoints);
            var driver = new RailHandcar { EndStopRails = new List<Railload> { straight, curve } };
            var train = LongCarChecks.LongCar();
            Vector2 previousRoot = Vector2.zero, previousForward = Vector2.right;
            for (int frame = 0; frame <= 140; frame++)
            {
                float routeDistance = -0.7f + frame * .01f;
                Railload rail = routeDistance < 0 ? straight : curve;
                float distance = routeDistance < 0 ? 3 + routeDistance : routeDistance;
                if (routeDistance >= 0 && reverseCurve)
                { curve.TryGetRenderedPathLength(out float length); distance = length - routeDistance; }
                TryCreateRailSampleAtDistance(rail, distance, out var sample);
                Vector2 facing = sample.Tangent * (routeDistance >= 0 && reverseCurve ? -1f : 1f);
                Check(driver.TryResolveTrainBodyRailPose(train, sample, facing, out var pose), "Support sampling must cross connected segments");
                if (frame > 0)
                {
                    Check(Vector2.Distance(pose.Position, previousRoot) < .02f, "Long bodies must not jump when their anchor crosses a curve join");
                    Check(Vector2.Dot(pose.Forward, previousForward) > .999f, "Long bodies must not snap or flip yaw at a reversed join");
                }
                previousRoot = pose.Position; previousForward = pose.Forward;
            }
        }

        var warmRail = new Railload(points);
        foreach (bool reverseRail in new[] { false, true })
        foreach (float facingSign in new[] { -1f, 1f })
        {
            var railPoints = (Vector2[])points.Clone();
            if (reverseRail) Array.Reverse(railPoints);
            var rail = new Railload(railPoints);
            var train = LongCarChecks.LongCar();
            for (float distance = 2; distance < 7; distance += .05f)
            {
                TryCreateRailSampleAtDistance(rail, distance, out var sample);
                Vector2 forward = sample.Tangent * facingSign;
                train.PlacementCenterCell = Vector2Int.zero;
                Check(TrainBodyRailPose.TrySampleOnRail(train, rail, distance, sample.Point, forward, out var rearCenterPose),
                    "An authored two-cell body must sample its occupied support cells");
                train.PlacementCenterCell = new Vector2Int(0, 1);
                Check(TrainBodyRailPose.TrySampleOnRail(train, rail, distance, sample.Point, forward, out var frontCenterPose)
                    && Vector2.Distance(rearCenterPose.Position, frontCenterPose.Position) < .00001f
                    && Vector2.Dot(rearCenterPose.Forward, frontCenterPose.Forward) > .99999f,
                    "Changing the selected Center must not shift the actual two-cell body on a curve");
            }
        }
        var warmDriver = new RailHandcar { EndStopRails = new List<Railload> { warmRail } };
        var warmTrain = LongCarChecks.LongCar();
        TryCreateRailSampleAtDistance(warmRail, 3, out var warmSample);
        for (int i = 0; i < 100; i++) warmDriver.TryResolveTrainBodyRailPose(warmTrain, warmSample, warmSample.Tangent, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) warmDriver.TryResolveTrainBodyRailPose(warmTrain, warmSample, warmSample.Tangent, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "Warm long-body sampling must allocate zero managed bytes");
        Check(!TrainBodyRailPose.TrySampleOnRail(new Train(), warmRail, 3, warmSample.Point, warmSample.Tangent, out _),
            "One-cell trains must retain their existing pose path");
        Check(!TrainBodyRailPose.TrySolve(Vector2.zero, Vector2.zero, 0, 1, Vector2.right, out _), "Coincident supports must not produce an invalid yaw");

        // A body-only lookahead used to call the stateful driving route hooks.
        var beforeJoin = new Railload(new Vector2(-3, 0), Vector2.zero);
        var afterJoin = new Railload(Vector2.zero, new Vector2(3, 0));
        var probe = new BodyProbeDriver();
        probe.EndStopRails = new List<Railload> { beforeJoin, afterJoin };
        TryCreateRailSampleAtDistance(beforeJoin, 2.7f, out var beforeJoinSample);
        Check(probe.TryResolveTrainBodyRailPose(warmTrain, beforeJoinSample, Vector2.right, out _), "Body probe must span the join");
        Check(probe.CursorChanges == 0 && probe.BodyRoutingQueries == 0, "Body-only sampling must not change route cursors or call stateful rail selection");
        TryCreateRailSampleAtDistance(afterJoin, .3f, out var afterJoinSample);
        Check(probe.TryResolveTrainBodyRailPose(warmTrain, afterJoinSample, Vector2.left, out _), "A backwards car must sample supports against travel direction");
        Check(probe.CursorChanges == 0 && probe.BodyRoutingQueries == 0, "Reverse-body sampling must also be read-only");

        foreach (bool reversedSource in new[] { false, true })
        foreach (bool reversedTarget in new[] { false, true })
        foreach (Vector2 gap in new[] { new Vector2(.04f, 0), new Vector2(0, .04f) })
        foreach (float facingSign in new[] { -1f, 1f })
        {
            var source = reversedSource ? new Railload(Vector2.zero, Vector2.left * 3)
                : new Railload(Vector2.left * 3, Vector2.zero);
            var target = reversedTarget ? new Railload(gap + Vector2.right * 3, gap)
                : new Railload(gap, gap + Vector2.right * 3);
            // Select the endpoint at the origin, independent of authoring order.
            TryCreateRailSampleAtDistance(source, reversedSource ? 0 : 3, out var endpoint);
            TryCreateRailSampleAtDistance(target, reversedTarget ? 3 : 0, out var entry);
            TryCreateRailConnectionBridgeSample(endpoint, entry, .04f, .02f, out var bridge);
            probe.EndStopRails = new List<Railload> { source, target };
            Check(probe.TrySampleTrainBodySupport(bridge, Vector2.right * facingSign, 1f, out Vector2 support),
                "Body supports must advance from the interior of a retained bridge");
            Check(Vector2.Dot(support - bridge.Point, Vector2.right) * facingSign > .95f,
                "A reverse-facing body support must traverse the bridge backwards, not jump to its forward exit");
            Check(probe.CursorChanges == 0 && probe.BodyRoutingQueries == 0,
                "Bridge support reversal must not mutate drive routing");
        }

        warmTrain.TryApplyRailPose(warmRail, 3, warmSample.Point, warmSample.Tangent);
        Check(warmDriver.TryResolveRailSampleForTrain(warmTrain, warmSample.Tangent, 0f,
                out var storedSample, out _) && Vector2.Distance(storedSample.Point, warmSample.Point) < .0001f,
            "A corrected body must retain its logical rail sample even under an exact anchor-distance check");
        Check(Vector2.Distance(warmTrain.ResolveRailAnchorPoint(), warmSample.Point) < .0001f,
            "Navigation must use the rail anchor rather than the displaced body root");

        RunRecordedBodyConvoyChecks(Check);
        Console.WriteLine($"Train curved body checks passed: {checks} checks; warm sampler {allocated} bytes allocated");
    }

    private static void RunRecordedBodyConvoyChecks(Action<bool, string> check)
    {
        var driver = new RailHandcar();
        var route = new List<ConsistPathSample>();
        float pathDistance = 0;
        Vector2 previous = Vector2.zero;
        for (int section = 0; section < 6; section++)
        {
            var sectionPoints = new Vector2[41];
            for (int i = 0; i < sectionPoints.Length; i++)
            {
                float angle = (section * 40 + i) * MathF.PI / 240f;
                sectionPoints[i] = new Vector2(8 * MathF.Sin(angle), 8 * (1 - MathF.Cos(angle)));
            }
            bool reverse = section % 2 != 0;
            if (reverse) Array.Reverse(sectionPoints);
            var rail = new Railload(sectionPoints);
            rail.TryGetRenderedPathLength(out float length);
            for (int i = 0; i <= 40; i++)
            {
                float distance = length * i / 40f;
                TryCreateRailSampleAtDistance(rail, reverse ? length - distance : distance, out var sample);
                if (route.Count > 0) pathDistance += Vector2.Distance(previous, sample.Point);
                route.Add(new ConsistPathSample { Distance = pathDistance, Sample = sample });
                previous = sample.Point;
            }
        }
        driver.consistPathTape.AddRange(route);
        // No world rails are supplied: a follower must stay on the recorded route.
        var cars = new Train[6];
        var previousRoots = new Vector2[cars.Length];
        for (int i = 0; i < cars.Length; i++) cars[i] = LongCarChecks.LongCar();
        foreach (float travelSign in new[] { 1f, -1f })
        for (int frame = 0; frame < 150; frame++)
        for (int i = 0; i < cars.Length; i++)
        {
            float distance = 5f + i * 2f + travelSign * frame * .01f;
            check(driver.TrySampleConsistPathTape(distance, out var sample, false), "Convoy anchors must remain on recorded rails");
            Vector2 along = driver.ResolveConsistPathForward(distance, sample, Vector2.right);
            Vector2 facing = i % 2 == 0 ? along : -along;
            check(driver.TryResolveTrainBodyRailPose(cars[i], sample, facing, out var pose, distance), "All six mixed-heading bodies must use the recorded branch");
            if (frame > 0)
                check(Vector2.Distance(pose.Position, previousRoots[i]) < .025f, "No body may teleport as any of six long cars crosses a reversed rail join");
            previousRoots[i] = pose.Position;
        }
        check(driver.BodyRoutingQueries == 0, "Recorded convoy rendering must never invoke stateful network selection");
    }
}
