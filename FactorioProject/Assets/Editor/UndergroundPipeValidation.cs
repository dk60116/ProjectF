using UnityEditor;
using UnityEngine;

public static class UndergroundPipeValidation
{
    private const string MenuPath = "Tools/ProjectF/Diagnostics/Validate Underground Pipe";

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu()
    {
        int failureCount = ValidateGeometryRules();
        failureCount += ValidateDefinition();

        if (failureCount == 0)
        {
            Debug.Log("UndergroundPipe validation passed: pair distance, axis, crossing, overlap, and ItemData are valid.");
        }
        else
        {
            Debug.LogError($"UndergroundPipe validation failed with {failureCount} error(s).");
        }
    }

    private static int ValidateGeometryRules()
    {
        int failures = 0;
        failures += Expect(
            UndergroundPipe.IsValidPairGeometry(Vector2Int.zero, new Vector2Int(4, 0), 5),
            "An inclusive five-cell horizontal pair must be valid.");
        failures += Expect(
            !UndergroundPipe.IsValidPairGeometry(Vector2Int.zero, new Vector2Int(5, 0), 5),
            "A pair beyond the inclusive maximum distance must be invalid.");
        failures += Expect(
            !UndergroundPipe.IsValidPairGeometry(Vector2Int.zero, new Vector2Int(2, 2), 5),
            "A diagonal pair must be invalid.");
        failures += Expect(
            !UndergroundPipe.SegmentsOverlapCollinearly(
                new Vector2Int(-2, 0),
                new Vector2Int(2, 0),
                new Vector2Int(0, -2),
                new Vector2Int(0, 2)),
            "Perpendicular underground routes must cross without connecting.");
        failures += Expect(
            UndergroundPipe.SegmentsOverlapCollinearly(
                Vector2Int.zero,
                new Vector2Int(4, 0),
                new Vector2Int(2, 0),
                new Vector2Int(6, 0)),
            "Collinear overlapping routes must be rejected.");
        return failures;
    }

    private static int ValidateDefinition()
    {
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
        int undergroundDefinitionCount = 0;
        int failures = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (definition == null || !(definition.mapObject is UndergroundPipe))
            {
                continue;
            }

            undergroundDefinitionCount++;
            failures += Expect(
                definition.UndergroundPipeMaxDistance >= 2,
                $"{path}: max distance must be at least two inclusive cells.");
            failures += Expect(
                definition.mapObject.Status.mapSizeX == 1
                && definition.mapObject.Status.mapSizeY == 1,
                $"{path}: each visible endpoint prefab must occupy one surface cell.");
        }

        failures += Expect(
            undergroundDefinitionCount == 1,
            $"Expected exactly one UndergroundPipe ItemDefinition, found {undergroundDefinitionCount}.");
        return failures;
    }

    private static int Expect(bool condition, string message)
    {
        if (condition)
        {
            return 0;
        }

        Debug.LogError($"UndergroundPipe validation: {message}");
        return 1;
    }
}
