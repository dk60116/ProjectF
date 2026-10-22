using System;
using System.Collections.Generic;
using UnityEngine;

public partial class RailHandcar
{
    List<Railload> EndStopRails;

    public static void RunEndStopChecks()
    {
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new Exception(message); }
        bool Near(float a, float b) => Math.Abs(a - b) < .0002f;
        RailHandcar Diesel() => new RailHandcar
        {
            Status = (1, 2),
            PlacementCenterCell = Vector2Int.zero,
            BodyCollider = new BoxCollider
            {
                center = new Vector3(0, .5f, DieselBody.Center),
                size = new Vector3(.7f, 1, DieselBody.Length)
            },
            EndStopRails = new List<Railload>()
        };

        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reverseRail in new[] { false, true })
        foreach (float facingSign in new[] { -1f, 1f })
        foreach (float travelSign in new[] { -1f, 1f })
        foreach (float step in new[] { .01f, .2f, 2f, 20f })
        {
            var driver = Diesel();
            var rail = reverseRail ? new Railload(axis * 10, Vector2.zero) : new Railload(Vector2.zero, axis * 10);
            driver.EndStopRails.Add(rail);
            Vector2 facing = axis * facingSign;
            Vector2 travel = axis * travelSign;
            float clearance = DieselBody.Length * .5f + (facingSign == travelSign ? DieselBody.Center : -DieselBody.Center);
            Check(Near(driver.ResolveRailEndClearance(facingSign == travelSign), clearance),
                "Diesel must use the authored body offset, not the footprint anchor or a symmetric half-length");
            float position = 5;
            for (int frame = 0; frame < 1200; frame++)
            {
                float distance = reverseRail ? 10 - position : position;
                TryCreateRailSampleAtDistance(rail, distance, out var sample);
                float permitted = driver.ResolveRailEndLimitedMoveDistance(driver, sample, facing, travel, step, frame % 2 == 0);
                Check(permitted >= 0 && permitted <= step + .0001f, "End checks must only restrict the requested movement");
                Check(driver.TryAdvanceAlongRailNetwork(sample, travel, permitted, out var target, out float moved,
                        allowRailConnectionBridgeInterpolation: true), "Clamped movement must remain on track");
                position = Vector2.Dot(target.Point, axis);
                Check(position >= clearance - .0002f || travelSign > 0,
                    "Reverse-leading end must remain supported by the track");
                Check(position <= 10 - clearance + .0002f || travelSign < 0,
                    "Forward-leading end must remain supported by the track");
                if (moved > .0001f) continue;
                break;
            }
            Check(Near(position, travelSign > 0 ? 10 - clearance : clearance),
                "Small steps and oversized steps must stop at the same physical end position");
            float finalDistance = reverseRail ? 10 - position : position;
            TryCreateRailSampleAtDistance(rail, finalDistance, out var stopped);
            Check(Near(driver.ResolveRailEndLimitedMoveDistance(driver, stopped, facing, -travel, .2f, true), .2f),
                "A stopped locomotive must be able to back away from the dead end");
        }

        foreach (bool reverseSource in new[] { false, true })
        foreach (bool reverseTarget in new[] { false, true })
        foreach (float gap in new[] { 0f, .04f })
        {
            var driver = Diesel();
            var source = reverseSource ? new Railload(new Vector2(3, 0), Vector2.zero) : new Railload(Vector2.zero, new Vector2(3, 0));
            var target = reverseTarget ? new Railload(new Vector2(7, 0), new Vector2(3 + gap, 0)) : new Railload(new Vector2(3 + gap, 0), new Vector2(7, 0));
            driver.EndStopRails.Add(source);
            driver.EndStopRails.Add(target);
            TryCreateRailSampleAtDistance(source, reverseSource ? .5f : 2.5f, out var sample);
            Check(Near(driver.ResolveRailEndLimitedMoveDistance(driver, sample, Vector2.right, Vector2.right, 1, true), 1),
                "Body clearance must not stop a locomotive at a connected segment endpoint");
            Check(driver.TryAdvanceAlongRailNetwork(sample, Vector2.right, 1, out var advanced, out _,
                    allowRailConnectionBridgeInterpolation: true) && advanced.Rail == target,
                "Connected and oppositely authored segments must remain traversable");
            float allowed = driver.ResolveRailEndLimitedMoveDistance(driver, sample, Vector2.right, Vector2.right, 20, false);
            Check(Near(allowed, 7 - 2.5f - driver.ResolveRailEndClearance(true)),
                "The margin must be reserved at the network dead end, not the first segment endpoint");

            if (gap > 0)
            {
                TryCreateRailSampleAtDistance(source, reverseSource ? 0 : 3, out var sourceEnd);
                TryCreateRailSampleAtDistance(target, reverseTarget ? 4 - gap : 0, out var targetStart);
                TryCreateRailConnectionBridgeSample(sourceEnd, targetStart, gap, gap * .5f, out var bridge);
                Check(Near(driver.ResolveRailEndLimitedMoveDistance(driver, bridge, Vector2.right, Vector2.right, .5f, true), .5f),
                    "An existing bridge must use its remaining connection distance without restarting");
            }
        }

        var curvedDriver = Diesel();
        var curve = new Railload(Vector2.zero, new Vector2(0, 3), new Vector2(3, 3));
        curvedDriver.EndStopRails.Add(curve);
        TryCreateRailSampleAtDistance(curve, 4, out var curvedSample);
        Check(Near(curvedDriver.ResolveRailEndLimitedMoveDistance(curvedDriver, curvedSample,
                Vector2.right, Vector2.right, 5, true), 2 - curvedDriver.ResolveRailEndClearance(true)),
            "Curved rail ends must reserve body length in route-distance units");

        var longCar = LongCarChecks.LongCar();
        var longDriver = new RailHandcar { EndStopRails = new List<Railload>() };
        var longRail = new Railload(Vector2.zero, new Vector2(10, 0));
        longDriver.EndStopRails.Add(longRail);
        TryCreateRailSampleAtDistance(longRail, 8.3f, out var nearLongEnd);
        float longFrontLimit = 10f - (LongCarBody.Length * .5f + LongCarBody.Center);
        Check(Near(longDriver.ResolveRailEndLimitedMoveDistance(longCar, nearLongEnd,
                Vector2.right, Vector2.right, 2, false), longFrontLimit - 8.3f),
            "A Long Flatcar must reserve the configured physical front at the rail end");
        TryCreateRailSampleAtDistance(longRail, longFrontLimit, out var stoppedLong);
        Check(Near(longDriver.ResolveRailEndLimitedMoveDistance(longCar, stoppedLong,
                Vector2.right, Vector2.right, .2f, true), 0), "Continued input must not move a Long Flatcar past its front limit");
        Check(Near(longDriver.ResolveRailEndLimitedMoveDistance(longCar, stoppedLong,
                Vector2.right, Vector2.left, .2f, true), .2f), "The Long Flatcar must be able to reverse away from its front limit");

        foreach (int anchor in new[] { 0, 1 })
        {
            var noCollider = new Train { Status = (1, 2), PlacementCenterCell = new Vector2Int(0, anchor) };
            Check(Near(noCollider.ResolveRailEndClearance(true), 1.5f - anchor)
                    && Near(noCollider.ResolveRailEndClearance(false), .5f + anchor),
                "A collider-free train must fall back to its two-cell footprint and anchor");
        }
        var scaled = Diesel();
        scaled.transform.lossyScale = new Vector3(1, 1, 2);
        Check(Near(scaled.ResolveRailEndClearance(true), (DieselBody.Center + DieselBody.Length * .5f) * 2),
            "Body clearance must be expressed in world units");
        scaled.transform.lossyScale = new Vector3(1, 1, -1);
        Check(Near(scaled.ResolveRailEndClearance(true), DieselBody.Length * .5f - DieselBody.Center),
            "Mirrored body offsets must swap the physical front and rear margins");

        Console.WriteLine($"Train body end-stop checks passed: {checks} checks");
    }
}
