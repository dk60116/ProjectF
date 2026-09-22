using System;
using System.Collections.Generic;
using UnityEngine;

public static class Checks
{
    private static int checks;

    public static void Main()
    {
        var generator = new TerrainGenerator(7719);
        ValidateRandomClusters(generator);
        ValidateExactSpacingConfigurations(generator);
        Console.WriteLine($"Oil cluster generation checks passed: {checks}");
    }

    private static void ValidateRandomClusters(TerrainGenerator generator)
    {
        var shapes = new HashSet<string>();
        for (int cellY = -5; cellY <= 5; cellY++)
        {
            for (int cellX = -5; cellX <= 5; cellX++)
            {
                List<Vector2Int> offsets = ResolveCluster(generator, cellX, cellY, 2, 8);
                ValidateSpacing(offsets, 2, 8);
                shapes.Add(ToShapeKey(offsets));
                checks++;
            }
        }

        Require(shapes.Count >= 20, "oil clusters must produce substantially more than rotated square variants");
    }

    private static void ValidateExactSpacingConfigurations(TerrainGenerator generator)
    {
        for (int spacing = 2; spacing <= 8; spacing++)
        {
            List<Vector2Int> offsets = ResolveCluster(generator, spacing, -spacing, spacing, spacing);
            ValidateSpacing(offsets, spacing, spacing);
            checks++;
        }
    }

    private static List<Vector2Int> ResolveCluster(
        TerrainGenerator generator,
        int cellX,
        int cellY,
        int minSpacing,
        int maxSpacing)
    {
        var offsets = new List<Vector2Int>(4);
        for (int memberIndex = 0; memberIndex < 4; memberIndex++)
        {
            Vector2Int offset = generator.ResolveForTest(
                cellX,
                cellY,
                991,
                memberIndex,
                minSpacing,
                maxSpacing);
            Require(!offsets.Contains(offset), "oil cluster members must occupy unique cells");
            offsets.Add(offset);
        }

        return offsets;
    }

    private static void ValidateSpacing(
        IReadOnlyList<Vector2Int> offsets,
        int minSpacing,
        int maxSpacing)
    {
        for (int first = 0; first < offsets.Count; first++)
        {
            for (int second = first + 1; second < offsets.Count; second++)
            {
                int gridDistance = Math.Max(
                    Math.Abs(offsets[first].x - offsets[second].x),
                    Math.Abs(offsets[first].y - offsets[second].y));
                Require(
                    gridDistance >= minSpacing && gridDistance <= maxSpacing,
                    "every oil pair must remain inside the configured spacing range");
            }
        }
    }

    private static string ToShapeKey(IReadOnlyList<Vector2Int> offsets)
    {
        return string.Join(";", offsets);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
