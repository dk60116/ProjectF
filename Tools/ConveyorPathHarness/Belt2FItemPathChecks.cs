using System;
using ProjectF.Conveyors;
using UnityEngine;

internal static class Belt2FItemPathChecks
{
    private const float PathHalfLength = 1.3251f;
    private const float PathHighHalfLength = 0.5f;
    private const float PathLowHeight = 0.13f;
    private const float PathHighHeight = 0.806f;
    private const float Epsilon = 0.00001f;

    internal static void Run()
    {
        VerifySlopeToCenterTransition(true);
        VerifySlopeToCenterTransition(false);
        VerifyAxisSelection();
        VerifyPathContinuity();
        Console.WriteLine("Passed 2F belt item path projection at both slope/center seams and on both local axes.");
    }

    private static void VerifySlopeToCenterTransition(bool positiveSlope)
    {
        float sign = positiveSlope ? 1f : -1f;
        float slopeSlot = sign * 1.25f;
        float centerSlot = sign * 0.25f;
        float slopeHeight = ResolveHeight(slopeSlot);
        float greatestPenetration = 0f;

        for (int step = 0; step <= 100; step++)
        {
            float t = step / 100f;
            float coordinate = Mathf.Lerp(slopeSlot, centerSlot, t);
            float directChordHeight = Mathf.Lerp(slopeHeight, PathHighHeight, t);
            Vector3 conformed = ConveyorBelt2FPath.ConformItemPosition(
                new Vector3(coordinate, directChordHeight, 0f),
                true,
                PathHalfLength,
                PathHighHalfLength,
                PathLowHeight,
                PathHighHeight);
            float expectedHeight = ResolveHeight(coordinate);
            Require(Mathf.Abs(conformed.y - expectedHeight) <= Epsilon,
                "moving item must stay on the authored 2F path height");
            Require(conformed.y + Epsilon >= directChordHeight,
                "path projection must not push an item into the belt");
            greatestPenetration = Mathf.Max(greatestPenetration, conformed.y - directChordHeight);
        }

        Require(greatestPenetration > 0.1f,
            "fixture must reproduce the visible slope/center penetration from direct interpolation");
    }

    private static void VerifyAxisSelection()
    {
        Vector3 input = new Vector3(7f, -3f, 0.75f);
        Vector3 conformed = ConveyorBelt2FPath.ConformItemPosition(
            input,
            false,
            PathHalfLength,
            PathHighHalfLength,
            PathLowHeight,
            PathHighHeight);
        Require(Mathf.Abs(conformed.x - input.x) <= Epsilon, "projection must preserve the lateral coordinate");
        Require(Mathf.Abs(conformed.z - input.z) <= Epsilon, "projection must preserve the path coordinate");
        Require(Mathf.Abs(conformed.y - ResolveHeight(input.z)) <= Epsilon,
            "local-Z belts must use the Z path coordinate");
    }

    private static void VerifyPathContinuity()
    {
        float before = ResolveHeight(PathHighHalfLength - Epsilon);
        float atSeam = ResolveHeight(PathHighHalfLength);
        float after = ResolveHeight(PathHighHalfLength + Epsilon);
        Require(Mathf.Abs(before - atSeam) <= Epsilon, "upper platform must be flat through the seam");
        Require(Mathf.Abs(after - atSeam) < 0.0001f, "slope height must remain continuous at the seam");
        Require(Mathf.Abs(ResolveHeight(PathHalfLength) - PathLowHeight) <= Epsilon,
            "slope endpoint must meet the low platform");
    }

    private static float ResolveHeight(float coordinate)
    {
        return ConveyorBelt2FPath.ResolveItemHeight(
            coordinate,
            PathHalfLength,
            PathHighHalfLength,
            PathLowHeight,
            PathHighHeight);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
