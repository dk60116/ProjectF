using System;
using UnityEngine;

public partial class RailHandcar
{
    float ResolveRailConnectionMaxDistance() => .08f;
    float ResolveInternalConnectionMaxDistance() => .015f;

    public static void RunJunctionMotionChecks()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new Exception(message); checks++; }

        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reverseSource in new[] { false, true })
        foreach (bool reverseTarget in new[] { false, true })
        foreach (float frontSign in new[] { -1f, 1f })
        foreach (Vector2 offset in new[] { axis * .04f, new Vector2(-axis.y, axis.x) * .04f })
        {
            Vector2 end = axis * 3;
            Vector2 entry = end + offset;
            var source = reverseSource ? new Railload(end, Vector2.zero) : new Railload(Vector2.zero, end);
            var target = reverseTarget ? new Railload(entry + axis * 3, entry) : new Railload(entry, entry + axis * 3);
            var driver = new RailHandcar { ManualBranchRail = target };
            driver.TryApplyRailPose(source, reverseSource ? 0 : 3, end, axis * frontSign);
            driver.TestManualInput(new Vector3(axis.x, 0, axis.y), 1, .02f);
            RailSample sample = driver.TestManualSample;
            Check(Vector2.Distance(sample.Point, end) < .000001f,
                "Input branch selection must not move before any movement distance is consumed");
            Check(HasRailConnectionBridgeState(sample) && sample.ConnectionTargetRail == target,
                "Input-selected gap must retain its target through the existing bridge state");
            Check(Vector2.Dot(driver.TestManualFacing, axis * frontSign) > .999f,
                "Preparing a junction must preserve the locomotive's physical front");
            Check(!TryPrepareBranchRailTransition(sample, sample, out _),
                "A bridge in progress must not restart branch selection");
            for (int frame = 0; frame < 4; frame++)
            {
                Vector2 previous = sample.Point;
                float remaining = .01f, traveled = 0;
                Check(driver.TryAdvanceExistingRailConnectionBridge(sample, axis, ref remaining, ref traveled,
                    null, 0, false, out sample, out _, out _), "Selected junction bridge must advance");
                Check(Vector2.Distance(previous, sample.Point) <= .010001f && Math.Abs(traveled - .01f) < .000001f,
                    "Every bridge frame must stay within its requested movement distance");
            }
            Check(Vector2.Distance(sample.Point, entry) < .000001f, "Four bridge frames must reach the chosen entry");
        }

        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reversed in new[] { false, true })
        {
            Vector2 side = new Vector2(-axis.y, axis.x);
            Vector2 entry = axis * 2.98f + side * .03f;
            var target = reversed ? new Railload(entry + axis * 4, entry) : new Railload(entry, entry + axis * 4);
            var driver = new RailHandcar();
            Check(driver.TryFindRailConnectionSample(target, axis * 3, true, out _, out Vector2 point, out _, out _),
                "An overlapping offset endpoint must remain connected");
            Check(Math.Abs(Vector2.Dot(point - axis * 3, axis)) < .000001f,
                "Overlap must connect to the projected path point, not step backwards to the endpoint");
            var crossing = new Railload(axis * -4 + side * .03f, axis * 4 + side * .03f);
            Check(driver.TryFindRailConnectionSample(crossing, Vector2.zero, true, out _, out point, out _, out _)
                && point.magnitude > 3.9f,
                "Unrelated interior crossings must retain the tighter internal connection limit");
        }
        Console.WriteLine($"Junction motion continuity passed: {checks} checks");
    }
}
