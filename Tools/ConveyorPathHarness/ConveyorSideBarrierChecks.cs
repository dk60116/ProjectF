using System;
using ProjectF.Conveyors;
using UnityEngine;

internal static class ConveyorSideBarrierChecks
{
    public static void Run()
    {
        int cases = 0;
        foreach (Vector2 axis in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
        foreach (float radius in new[] { 0.15f, 0.25f, 0.35f })
        foreach (int sign in new[] { -1, 1 })
        {
            Vector2 side = new Vector2(-axis.y, axis.x) * sign;
            Vector2 origin = new Vector2(-12f, 8f);
            Vector2 center = origin + side * 0.5f;
            foreach (float along in new[] { -1f, -0.5f, 0f, 0.5f, 1f })
            {
                Vector2 start = origin + axis * along + side * 1.2f;
                Require(SweepBelt(start, -side, 4f, origin, axis, radius, out float distance,
                    out Vector2 normal), "fast side entry must not tunnel through either rail");
                Require(Math.Abs(distance - (0.7f - radius)) < 0.0001f,
                    "collision must account for player radius at every covered cell/seam");
                Require(Vector2.Dot(normal, side) > 0.999f, "entry normal must point outside");

                Vector2 inside = origin + axis * along;
                Require(SweepBelt(inside, side, 1f, origin, axis, radius, out distance, out normal),
                    "walking off the upper path sideways must hit its side");
                Require(Math.Abs(distance - (0.5f - radius)) < 0.0001f, "inside contact distance");
                cases += 2;
            }

            Require(!SweepBelt(origin - axis * 3f, axis, 6f, origin, axis, radius, out _, out _),
                "both longitudinal ends must stay open through the full belt");
            Require(!SweepBelt(origin + side * (0.5f + radius), axis, 4f,
                origin, axis, radius, out _, out _), "parallel wall movement must slide freely");

            Vector2 diagonal = (-side + axis).normalized;
            Require(SweepBelt(origin + side, diagonal, 2f, origin, axis, radius,
                out float diagonalDistance, out Vector2 diagonalNormal), "diagonal entry must hit");
            Vector2 remaining = diagonal * (2f - diagonalDistance);
            Vector2 slide = remaining - diagonalNormal * Vector2.Dot(remaining, diagonalNormal);
            Require(Math.Abs(Vector2.Dot(slide, side)) < 0.0001f
                && Vector2.Dot(slide, axis) > 0f, "contact must preserve movement along the wall");

            Vector2 overlapping = center + side * (radius * 0.5f);
            Require(!ConveyorSideBarrier.Sweep(overlapping, side, 0.05f, center, axis,
                0.5f, radius, out _, out _), "overlapping player must be able to escape");
            Require(ConveyorSideBarrier.Sweep(overlapping, -side, 0.05f, center, axis,
                0.5f, radius, out float overlapDistance, out _) && overlapDistance == 0f,
                "overlapping player must not move deeper");
            Require(!SweepBelt(origin + axis * 2f + side, -side, 2f, origin,
                axis, radius, out _, out _), "walking around the end must remain possible");
            Require(!ConveyorSideBarrier.Sweep(center, side, 0.05f, center, axis,
                0.5f, radius, out _, out _), "placement directly on the player must permit escape");
            cases += 7;
            foreach (int endSign in new[] { -1, 1 })
            {
                float boundary = endSign < 0 ? -RaisedMin : RaisedMax;
                foreach (float offset in new[] { 0.03f, 0.18f, 0.3f })
                {
                    Vector2 landing = origin + axis * (endSign * (boundary + offset));
                    Require(!SweepBelt(landing + side, -side, 2f, origin, axis, radius, out _, out _),
                        "low landing must allow lateral crossing even when narrower than the player diameter");
                    Require(!SweepBelt(landing, -axis * endSign, 0.5f, origin, axis, radius, out _, out _),
                        "entering the low landing must still allow walking up the belt centerline");
                    cases += 2;
                }

                Vector2 raisedSide = origin + axis * (endSign * (boundary - 0.03f)) + side;
                Require(SweepBelt(raisedSide, -side, 2f, origin, axis, radius, out _, out _),
                    "the raised span immediately beside the landing must remain blocked");
                Vector2 lowSide = origin + axis * (endSign * (boundary + 0.1f)) + side * 0.5f;
                Require(SweepBelt(lowSide, -axis * endSign, 0.5f, origin, axis, radius, out _, out _),
                    "walking along the low side into the raised wall must not bypass it");
                cases += 2;
            }
        }
        Console.WriteLine($"PASS: {cases} 2F side barrier cases (four rotations, low landings, raised sides, capsule width, fast/diagonal movement, open ends, escape).");
    }

    // Inner edges of Body_End/Body_Start in the current 2F prefab, using mesh bounds.
    private const float RaisedMin = -1.3187f + 0.50578f * 0.38050434f;
    private const float RaisedMax = 1.3251f - 0.50578f * 0.375513f;

    private static bool SweepBelt(Vector2 start, Vector2 direction, float distance,
        Vector2 origin, Vector2 axis, float radius, out float nearest, out Vector2 normal)
    {
        nearest = distance;
        normal = Vector2.zero;
        bool found = false;
        Vector2 side = new Vector2(-axis.y, axis.x);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            if (ConveyorSideBarrier.Sweep(start, direction, nearest,
                origin + axis * ((RaisedMin + RaisedMax) * 0.5f) + side * (0.5f * sign),
                axis, (RaisedMax - RaisedMin) * 0.5f, radius,
                out float hitDistance, out Vector2 hitNormal))
            {
                nearest = hitDistance;
                normal = hitNormal;
                found = true;
            }
        }
        return found;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
