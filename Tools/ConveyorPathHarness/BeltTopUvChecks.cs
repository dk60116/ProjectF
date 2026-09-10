using System;
using ProjectF.Conveyors;
using UnityEngine;

internal static class BeltTopUvChecks
{
    internal static void Run()
    {
        // 1F -> entry -> up ramp -> upper top -> down ramp -> exit -> 1F.
        // Check both external joins as well as the internal 2F joins over time.
        float[] edges = { -2.5f, -1.5f, -1.15f, -0.5f, 0.5f, 1.15f, 1.5f, 2.5f };
        Vector3[] flows = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        Vector3[] origins = { Vector3.zero, new Vector3(12f, 0f, -8f), new Vector3(-0.37f, 0f, -2.41f) };
        int joins = 0;
        foreach (Vector3 flow in flows)
        foreach (Vector3 origin in origins)
        foreach (float height in new[] { 0.35f, 0.676f, 1.2f })
        foreach (float speed in new[] { 0.25f, 0.5f, 2f })
        foreach (float time in new[] { 0f, 0.25f, 1f, 7.3f })
        {
            float[] heights = { 0f, 0f, 0f, height, height, 0f, 0f, 0f };
            float previousEndPhase = 0f;
            for (int segment = 0; segment < edges.Length - 1; segment++)
            {
                Vector3 start = origin + flow * edges[segment] + Vector3.up * heights[segment];
                Vector3 end = origin + flow * edges[segment + 1] + Vector3.up * heights[segment + 1];
                Vector2 mapping = Map(start, end, flow);
                float startPhase = ShaderPhase(mapping, 0f, speed, time);
                float endPhase = ShaderPhase(mapping, 1f, speed, time);
                if (segment > 0)
                {
                    RequireSamePhase(previousEndPhase, startPhase, "1F/2F and internal joins must stay aligned");
                    joins++;
                }

                // A pattern moving at belt speed reaches the same phase downstream.
                // Height must not introduce phase drift relative to the neighboring 1F.
                float travel = (edges[segment + 1] - edges[segment]) * 0.2f;
                float movedPhase = ShaderPhase(mapping, 0.2f, speed, time + travel / speed);
                RequireSamePhase(startPhase, movedPhase, "all tops must use the same flow direction and clock");

                // Endpoint seam extension must preserve the pattern at existing world positions.
                Vector3 extendedStart = start - flow * 0.05f;
                Vector3 extendedEnd = end + flow * 0.08f;
                Vector2 extendedMapping = Map(extendedStart, extendedEnd, flow);
                float originalStartUv = 0.05f / (edges[segment + 1] - edges[segment] + 0.13f);
                RequireSamePhase(startPhase, ShaderPhase(extendedMapping, originalStartUv, speed, time),
                    "seam extension must not shift the existing pattern");
                previousEndPhase = endPhase;
            }
        }

        Console.WriteLine($"Passed {joins} animated 1F/2F UV joins: four directions, world offsets, ramp heights, speeds, and seam extensions.");
        CheckSurfaceDensity(flows, origins);
    }

    private static void CheckSurfaceDensity(Vector3[] flows, Vector3[] origins)
    {
        float[] edges = { -1.5f, -0.5f, 0.5f, 1.5f };
        int cases = 0;
        foreach (Vector3 flow in flows)
        foreach (Vector3 origin in origins)
        foreach (float height in new[] { 0f, 0.35f, 0.676f, 1.2f })
        {
            Vector3[] points = new Vector3[edges.Length];
            float surfaceLength = 0f;
            for (int i = 0; i < edges.Length; i++)
            {
                points[i] = origin + flow * edges[i] + Vector3.up * (i == 1 || i == 2 ? height : 0f);
                if (i > 0) surfaceLength += Vector3.Distance(points[i - 1], points[i]);
            }

            float repeatScale = ConveyorBeltTopUv.GetSurfaceRepeatScale(surfaceLength, 3f, 1f);
            if (repeatScale < 0.9999f)
            {
                throw new InvalidOperationException("2F surface pattern must not be sparser than 1F");
            }

            foreach (float speed in new[] { 0.25f, 0.5f, 2f })
            foreach (float time in new[] { 0f, 0.25f, 1f, 7.3f })
            {
                Vector2 input = Map(points[0] - flow, points[0], flow);
                Vector2 output = Map(points[points.Length - 1], points[points.Length - 1] + flow, flow);
                Vector2 continuousMapping = ConveyorBeltTopUv.GetSurfaceAlignedMapping(
                    points[0], flow, 0f, surfaceLength, repeatScale, 1f);
                RequireSamePhase(ShaderPhase(input, 1f, speed, time),
                    ShaderPhase(continuousMapping, 0f, speed, time),
                    "Continuous 2F top must preserve the input seam phase");
                float distance = 0f;
                for (int i = 0; i < points.Length - 1; i++)
                {
                    float length = Vector3.Distance(points[i], points[i + 1]);
                    Vector2 segmentMapping = ConveyorBeltTopUv.GetSurfaceAlignedMapping(
                        points[0], flow, distance, length, repeatScale, 1f);
                    float startUv = distance / surfaceLength;
                    float endUv = (distance + length) / surfaceLength;
                    RequireSamePhase(
                        ShaderPhase(segmentMapping, 0f, speed, time),
                        ShaderPhase(continuousMapping, startUv, speed, time),
                        "Continuous 2F UVs must preserve every bend phase");
                    RequireSamePhase(
                        ShaderPhase(segmentMapping, 1f, speed, time),
                        ShaderPhase(continuousMapping, endUv, speed, time),
                        "Continuous 2F UVs must use surface distance through every bend");
                    if (Math.Abs(continuousMapping.x / surfaceLength - repeatScale) > 0.0001f)
                    {
                        throw new InvalidOperationException("The continuous 2F top must have uniform surface density");
                    }
                    distance += length;
                }

                RequireSamePhase(ShaderPhase(continuousMapping, 1f, speed, time),
                    ShaderPhase(output, 0f, speed, time),
                    "Surface-density correction must also preserve the output 1F phase");
                cases++;
            }
        }

        Console.WriteLine($"Passed {cases} continuous 2F surface-density cases with both 1F ends and all bends aligned over time.");
    }

    private static Vector2 Map(Vector3 start, Vector3 end, Vector3 flow)
    {
        // Unity's plane and the virtual top quad increase UV.y against local +Z.
        return ConveyorBeltTopUv.GetWorldAlignedMapping((start + end) * 0.5f, start - end, flow, 1f);
    }

    private static float ShaderPhase(Vector2 mapping, float uvY, float speed, float time)
    {
        return uvY * mapping.x + mapping.y - speed * time;
    }

    private static void RequireSamePhase(float expected, float actual, string message)
    {
        float difference = actual - expected;
        if (Math.Abs(difference - Math.Round(difference)) > 0.0002f)
        {
            throw new InvalidOperationException(message);
        }
    }
}
