using System;
using System.Collections.Generic;
using ProjectF.Trains;
using UnityEngine;

static class FacingChecks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    public static void Run()
    {
        var aRail = new Railload(new Vector2(0, 0), new Vector2(3, 0));
        var bRail = new Railload(new Vector2(8, 0), new Vector2(3, 0));
        var car = new RailHandcar { RuntimePlacementSequence = 1 };
        var neighbor = new Train { RuntimePlacementSequence = 2 };
        car.TryApplyRailPose(aRail, 2f, new Vector2(2, 0), Vector2.right);
        neighbor.TryApplyRailPose(bRail, 4.7f, new Vector2(3.3f, 0), Vector2.left);
        car.ConnectTo(neighbor);
        Check(new TrainPlacementSpacing().AlignPlacedTrains(new MapObject[] { car, neighbor }), "Complete must establish connection ends");
        Check(car.TryGetConnectionFacingSign(neighbor, true, out float sign) && sign == 1, "Neighbor is on this car's physical front");
        Check(neighbor.TryGetConnectionFacingSign(car, false, out sign) && sign == -1, "Opposite-facing car must retain its own physical front");
        car.SetTestRoute((aRail, 2f, 0f), (aRail, 3f, 1f), (bRail, 5f, 1f), (bRail, 3f, 3f));
        foreach (float target in new[] { 0.99f, 1f, 1.01f, 1.5f })
        {
            Railload rail = target < 1 ? aRail : bRail;
            float distance = target < 1 ? target + 2 : 6 - target;
            Check(Vector2.Dot(car.TestFacing(car, neighbor, true, aRail, 2f, rail, distance, 0f, target), Vector2.right) > 0.999f,
                "Oppositely authored rail endpoints must not reverse a forward-facing car");
        }

        car.SetTestRoute((bRail, 3f, 0f), (bRail, 5f, 2f), (aRail, 3f, 2f), (aRail, 1f, 4f));
        Check(Vector2.Dot(car.TestFacing(car, neighbor, false, bRail, 3f, aRail, 2f, 0f, 3f), Vector2.right) > 0.999f,
            "Reversing travel and consist order must not rotate the car");
        Check(Vector2.Dot(car.TestFacing(neighbor, car, true, bRail, 3f, aRail, 2f, 0f, 3f), Vector2.left) > 0.999f,
            "Reverse travel must preserve a backwards-facing freight car");

        // Raw tangent is downward at the exit. A world-space dot against the
        // previous right-facing direction is ambiguous at this 90-degree junction.
        var turnRail = new Railload(new Vector2(3, 5), new Vector2(3, 0));
        car.SetTestRoute((aRail, 2f, 0f), (aRail, 3f, 1f), (turnRail, 5f, 1f), (turnRail, 3f, 3f));
        Check(Vector2.Dot(car.TestFacing(car, neighbor, true, aRail, 2f, turnRail, 4.5f, 0f, 1.5f), Vector2.up) > 0.999f,
            "A perpendicular junction must use the Complete coupling end, not the raw rail tangent");
        Check(Vector2.Dot(car.TestFacing(neighbor, car, false, aRail, 2f, turnRail, 4.5f, 0f, 1.5f), Vector2.down) > 0.999f,
            "An oppositely facing car must keep its front at the same junction");
        Check(Vector2.Dot(car.TestFacing(car, null, false, aRail, 2f, turnRail, 4.5f, 0f, 1.5f), Vector2.up) > 0.999f,
            "A single car must retain its facing/travel sign across a junction");

        // A short lateral connection is positional correction, not the car's
        // longitudinal axis. The outgoing rail is authored in reverse order.
        var offsetRail = new Railload(new Vector2(8, 0.02f), new Vector2(3, 0.02f));
        car.SetTestRoute((aRail, 2f, 0f), (aRail, 3f, 1f), (offsetRail, 5f, 1.02f), (offsetRail, 4f, 2.02f));
        foreach (float progress in new[] { 0f, 0.001f, 0.01f, 0.019f, 0.02f })
        {
            Check(Vector2.Dot(car.TestBridgeFacing(car, neighbor, true,
                aRail, 3f, offsetRail, 5f, 1f, progress), Vector2.right) > 0.999f,
                "A lateral rail connection must not turn the car sideways");
        }

        // Driving backwards reverses the tape and consist order, but a car's
        // physical front remains the same even with stored forward bridge state.
        car.SetTestRoute((offsetRail, 4f, 0f), (offsetRail, 5f, 1f), (aRail, 3f, 1.02f), (aRail, 2f, 2.02f));
        foreach (float progress in new[] { 0.001f, 0.01f, 0.019f })
        {
            Check(Vector2.Dot(car.TestBridgeFacing(car, neighbor, false,
                aRail, 3f, offsetRail, 5f, 1f, progress, true), Vector2.right) > 0.999f,
                "Backing across a lateral gap must retain the same physical front");
            Check(Vector2.Dot(car.TestBridgeFacing(neighbor, car, true,
                aRail, 3f, offsetRail, 5f, 1f, progress, true), Vector2.left) > 0.999f,
                "A backwards-facing car must also retain its front across a reversed gap");
        }

        // The route leader may still be inside the bridge, so its tape has no
        // samples on the destination rail yet.
        car.SetTestRoute((aRail, 2f, 0f), (aRail, 3f, 1f));
        Check(Vector2.Dot(car.TestBridgeFacing(car, neighbor, true,
            aRail, 3f, offsetRail, 5f, 1f, 0.01f), Vector2.right) > 0.999f,
            "A leader still crossing the gap must not need future path samples");

        // Slight endpoint overlap makes the connector point backwards even
        // though the authored paths continue forwards.
        var overlapRail = new Railload(new Vector2(8, 0), new Vector2(2.98f, 0));
        car.SetTestRoute((aRail, 2f, 0f), (aRail, 3f, 1f), (overlapRail, 5.02f, 1.02f), (overlapRail, 4f, 2.04f));
        Check(Vector2.Dot(car.TestBridgeFacing(car, neighbor, true,
            aRail, 3f, overlapRail, 5.02f, 1f, 0.01f), Vector2.right) > 0.999f,
            "A backward-pointing endpoint correction must not rotate the car 180 degrees");

        // A sparse junction tape can report the opposite path sign for one
        // frame. The endpoint locomotive's connected target is still at its
        // physical tail and must keep the locomotive facing away from it.
        var endpointEngine = new SteamTrain { RuntimePlacementSequence = 3 };
        var endpointNeighbor = new Train { RuntimePlacementSequence = 4 };
        endpointEngine.TryApplyRailPose(aRail, 2f, new Vector2(2f, 0f), Vector2.right);
        endpointNeighbor.TryApplyRailPose(aRail, 1f, new Vector2(1f, 0f), Vector2.right);
        Check(endpointEngine.ConnectTo(endpointNeighbor), "Endpoint locomotive must connect at its physical tail");
        car.SetTestRoute((aRail, 3f, 0f), (aRail, 2f, 1f));
        Check(Vector2.Dot(car.TestFacing(
                endpointEngine, endpointNeighbor, false,
                aRail, 3f, aRail, 2f, 0f, 1f,
                invertNeighborTargetOrder: true), Vector2.right) > 0.999f,
            "Endpoint locomotive must reject a one-frame inverted junction path sign");

        var curveExit = new Railload(new Vector2(6, 3.02f), new Vector2(3, 0.02f));
        curveExit.TryGetRenderedPathLength(out float curveLength);
        car.SetTestRoute((aRail, 2f, 0f), (aRail, 3f, 1f), (curveExit, curveLength, 1.02f), (curveExit, curveLength - 1f, 2.02f));
        Vector2 expectedCurveForward = (Vector2.right + new Vector2(1, 1).normalized).normalized;
        Check(Vector2.Dot(car.TestBridgeFacing(car, neighbor, true,
            aRail, 3f, curveExit, curveLength, 1f, 0.01f), expectedCurveForward) > 0.999f,
            "A curved connection must blend the entry and exit rail directions");

        car.DisconnectFrom(neighbor);
        Check(!car.TryGetConnectionFacingSign(neighbor, true, out _) && !neighbor.TryGetConnectionFacingSign(car, true, out _),
            "Disconnect must remove both directions of the coupling metadata");
        Console.WriteLine($"Train facing harness passed: {checks} checks");
    }
}

public partial class RailHandcar
{
    const float RailConnectionDistanceEpsilon = 0.000001f;
    readonly List<ConsistPathSample> consistPathTape = new List<ConsistPathSample>();
    readonly List<ConnectedTrainRailMove> connectedTrainRailMoveScratch = new List<ConnectedTrainRailMove>();

    public void SetTestRoute(params (Railload rail, float railDistance, float routeDistance)[] points)
    {
        consistPathTape.Clear();
        foreach (var point in points)
        {
            point.rail.TrySampleRenderedPath(point.railDistance, out var position, out var tangent);
            consistPathTape.Add(new ConsistPathSample
            {
                Distance = point.routeDistance,
                Sample = new RailSample { Rail = point.rail, DistanceAlongPath = point.railDistance, Point = position, Tangent = tangent }
            });
        }
    }

    public Vector2 TestFacing(Train car, Train neighbor, bool neighborAhead, Railload fromRail, float fromDistance,
        Railload toRail, float toDistance, float startRouteDistance, float targetRouteDistance,
        bool invertNeighborTargetOrder = false)
    {
        connectedTrainRailMoveScratch.Clear();
        fromRail.TrySampleRenderedPath(fromDistance, out var startPoint, out var startTangent);
        toRail.TrySampleRenderedPath(toDistance, out var targetPoint, out var targetTangent);
        var move = new ConnectedTrainRailMove
        {
            Train = car, StartFacingTangent = car.Facing,
            StartSample = new RailSample { Rail = fromRail, DistanceAlongPath = fromDistance, Point = startPoint, Tangent = startTangent },
            TargetSample = new RailSample { Rail = toRail, DistanceAlongPath = toDistance, Point = targetPoint, Tangent = targetTangent }
        };
        Vector2 pathForward = ResolveConsistPathForward(targetRouteDistance, move.TargetSample, car.Facing);
        Vector2 neighborOffset = pathForward * (neighborAhead ? 1f : -1f);
        if (invertNeighborTargetOrder) neighborOffset = -neighborOffset;
        var neighborMove = new ConnectedTrainRailMove
        {
            Train = neighbor,
            TargetSample = new RailSample { Point = targetPoint + neighborOffset }
        };
        if (neighborAhead && neighbor != null) connectedTrainRailMoveScratch.Add(neighborMove);
        int index = connectedTrainRailMoveScratch.Count;
        connectedTrainRailMoveScratch.Add(move);
        if (!neighborAhead && neighbor != null) connectedTrainRailMoveScratch.Add(neighborMove);
        return ResolveConnectedTrainFacing(index, startRouteDistance, targetRouteDistance, car.Facing);
    }

    public Vector2 TestBridgeFacing(Train car, Train neighbor, bool neighborAhead,
        Railload sourceRail, float sourceDistance, Railload targetRail, float targetDistance,
        float bridgeRouteStart, float progress, bool reverseTape = false)
    {
        sourceRail.TrySampleRenderedPath(sourceDistance, out var sourcePoint, out var sourceTangent);
        targetRail.TrySampleRenderedPath(targetDistance, out var targetPoint, out var targetTangent);
        var source = new RailSample { Rail = sourceRail, DistanceAlongPath = sourceDistance, Point = sourcePoint, Tangent = sourceTangent };
        var target = new RailSample { Rail = targetRail, DistanceAlongPath = targetDistance, Point = targetPoint, Tangent = targetTangent };
        TryCreateRailConnectionBridgeSample(source, target, Vector2.Distance(sourcePoint, targetPoint), progress, out var bridge);
        connectedTrainRailMoveScratch.Clear();
        float routeProgress = reverseTape ? bridge.ConnectionPathDistance - progress : progress;
        Vector2 pathForward = ResolveConsistPathForward(bridgeRouteStart + routeProgress, bridge, car.Facing);
        var neighborMove = new ConnectedTrainRailMove
        {
            Train = neighbor,
            TargetSample = new RailSample
            {
                Point = bridge.Point + pathForward * (neighborAhead ? 1f : -1f)
            }
        };
        if (neighborAhead) connectedTrainRailMoveScratch.Add(neighborMove);
        int index = connectedTrainRailMoveScratch.Count;
        connectedTrainRailMoveScratch.Add(new ConnectedTrainRailMove
        {
            Train = car, StartSample = source, TargetSample = bridge, StartFacingTangent = car.Facing
        });
        if (!neighborAhead) connectedTrainRailMoveScratch.Add(neighborMove);
        return ResolveConnectedTrainFacing(index, bridgeRouteStart, bridgeRouteStart + routeProgress, car.Facing);
    }
}
