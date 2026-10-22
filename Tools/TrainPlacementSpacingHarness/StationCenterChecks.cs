using System;
using UnityEngine;

public partial class RailHandcar
{
    const float StationDockRailCoordinateSampleMaxDistance = .8f;
    float stationDockCompleteDistance = .01f, stationDockSpeed = 1f;
    Vector2 lastRailTravelDirection;

    public static void RunStationCenterChecks()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); checks++; }
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reverseRail in new[] { false, true })
        foreach (float facingSign in new[] { -1f, 1f })
        foreach (bool updateDefinition in new[] { false, true })
        {
            var rail = reverseRail ? new Railload(axis * 10, axis * -10) : new Railload(axis * -10, axis * 10);
            var train = new RailHandcar
            {
                Status = (1, 2),
                BodyCollider = new BoxCollider
                {
                    center = new Vector3(0, .5f, DieselBody.Center),
                    size = new Vector3(.65f, 1, DieselBody.Length)
                }
            };
            var station = new Vector2Int((int)(axis.x * 3), (int)(axis.y * 3));
            Vector2 stationPoint = axis * 3;
            Vector2 facing = axis * facingSign;
            var source = new Train { Status = (1, 2) };
            if (updateDefinition) train.BoundItemDefinition = new ItemDefinition { mapObject = source };
            // Keep the same placed vehicle/root while changing the authoring Center.
            rail.TryFindNearestRenderedPathSample(stationPoint, out float stationDistance, out _, out _, out _);
            Check(train.TryApplyRailPose(rail, stationDistance, stationPoint, facing), "Diesel root placement failed");
            for (int centerCell = 0; centerCell < 2; centerCell++)
            {
                (updateDefinition ? source : train).PlacementCenterCell = new Vector2Int(0, centerCell);
                float pathFacingSign = reverseRail ? -facingSign : facingSign;
                Check(train.TryGetRailDockDeltaAtCoordinate(station, out float delta)
                    && Math.Abs(delta + centerCell * pathFacingSign) < .0001f,
                    $"Center {centerCell} must move the docking target independently of the fixed diesel root; delta={delta}");
                Check(train.TryFindRailDockSampleAtCoordinate(station, rail, out RailSample rawSample)
                    && Math.Abs(rawSample.DistanceAlongPath - stationDistance) < .0001f,
                    "Changing the station Center must not move raw pipe/tank dock coordinates");

                Vector2 alignedRoot = stationPoint - facing * centerCell;
                rail.TryFindNearestRenderedPathSample(alignedRoot, out float alignedDistance, out _, out _, out _);
                Check(train.TryDockConnectedTrainGroupAtRailCoordinate(station, .02f, true)
                    && Math.Abs(train.Distance - alignedDistance) < .0001f,
                    "Actual station snap must use the same selected Center as the arrival distance");
                Check(train.TryGetRailDockDistanceAtCoordinate(station, out float remaining) && remaining < .0001f,
                    "The selected occupied cell, not always the rear root, must align with the station");
                train.transform.position += new Vector3(axis.x, 0, axis.y) * .3f;
                Check(train.TryGetRailDockDistanceAtCoordinate(station, out remaining) && remaining < .0001f,
                    "Visual body correction must not change Center docking distance");
                train.TryApplyRailPose(rail, stationDistance, stationPoint, facing);
            }
        }

        // On a bend, subtract arc distance rather than a world-space chord or a
        // station tangent projected against the locomotive's remote heading.
        foreach (bool reverseRail in new[] { false, true })
        {
            var rail = reverseRail
                ? new Railload(new Vector2(4, 4), new Vector2(4, 0), Vector2.zero)
                : new Railload(Vector2.zero, new Vector2(4, 0), new Vector2(4, 4));
            var train = new RailHandcar
            {
                Status = (1, 2), PlacementCenterCell = new Vector2Int(0, 1),
                BodyCollider = new BoxCollider { center = new Vector3(0, .5f, .5f), size = new Vector3(.65f, 1, 1.7f) }
            };
            float initialDistance = reverseRail ? 6f : 2f;
            rail.TrySampleRenderedPath(initialDistance, out Vector2 initialPoint, out _);
            train.TryApplyRailPose(rail, initialDistance, initialPoint, Vector2.right);
            Check(train.TryGetRailDockDistanceAtCoordinate(new Vector2Int(4, 2), out float remaining)
                && Math.Abs(remaining - 3) < .0001f, "Center offset must follow rail arc length across a bend");
            Check(train.TryDockConnectedTrainGroupAtRailCoordinate(new Vector2Int(4, 2), .02f, true)
                && Math.Abs(train.Distance - (reverseRail ? 3f : 5f)) < .0001f,
                "Curved station snap must move to the corrected root distance");
        }

        foreach (bool reverseRail in new[] { false, true })
        {
            var before = reverseRail ? new Railload(new Vector2(4, 0), Vector2.zero) : new Railload(Vector2.zero, new Vector2(4, 0));
            var after = reverseRail ? new Railload(new Vector2(8, 0), new Vector2(4, 0)) : new Railload(new Vector2(4, 0), new Vector2(8, 0));
            var train = new RailHandcar
            {
                Status = (1, 2), PlacementCenterCell = new Vector2Int(0, 1),
                BodyCollider = new BoxCollider { center = new Vector3(0, .5f, .5f), size = new Vector3(.65f, 1, 1.7f) }
            };
            train.AcquisitionRails.Add(before);
            train.AcquisitionRails.Add(after);
            train.TryApplyRailPose(before, reverseRail ? .1f : 3.9f, new Vector2(3.9f, 0), Vector2.right);
            Check(train.TryGetRailDockDeltaAtCoordinate(new Vector2Int(4, 0), out float delta)
                && Math.Abs(delta - (reverseRail ? .9f : -.9f)) < .0002f,
                "Center on an adjacent rail must retain the root rail's signed docking delta");
            Check(train.TryDockConnectedTrainGroupAtRailCoordinate(new Vector2Int(4, 0), .02f, true)
                && Math.Abs(train.Distance - (reverseRail ? 1f : 3f)) < .0002f,
                "A station at a rail join must snap the selected front cell, not the rear root");
        }
        Console.WriteLine($"Station Center docking checks passed: {checks} checks");
    }
}
