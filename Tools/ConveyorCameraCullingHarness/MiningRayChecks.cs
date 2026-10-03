using System.Collections.Generic;
using UnityEngine;

// Native bounds intersection is doubled. Spatial traversal is the actual MiningWorld method.
public class RayMiner
{
    public RayBounds CullBounds;
    public RayMiner(float distance) => CullBounds = new RayBounds { Distance = distance };
}
public class RayBounds
{
    public float Distance;
    public bool IntersectRay(Ray ray, out float distance) { distance = Distance; return true; }
}
public class RayCells
{
    public readonly Dictionary<Vector2Int, List<RayMiner>> Values = new();
    public int Count => Values.Count;
    public int Queries;
    public bool TryGetValue(Vector2Int coordinate, out List<RayMiner> members)
    { Queries++; return Values.TryGetValue(coordinate, out members); }
}
public partial class MiningRayProbe
{
    private readonly RayCells cells = new();
    public int Queries => cells.Queries;
    public void Add(int x, int y, RayMiner miner) => cells.Values[new Vector2Int(x, y)] = new List<RayMiner> { miner };
}
public static class MiningRayChecks
{
    public static void Check()
    {
        var ray = new Ray(new Vector3(.5f, 50, .5f), new Vector3(1, 0, 1));
        var probe = new MiningRayProbe();
        Checks.Require(!probe.TryRaycast(ray, 20000, out _, out _) && probe.Queries == 0, "empty mining worlds skip long-ray traversal");
        var distant = new RayMiner(19000); probe.Add(400, 400, distant);
        Checks.Require(probe.TryRaycast(ray, 20000, out var hit, out float distance) && hit == distant && distance == 19000,
            "expanded free-camera ray can select a distant miner");
        Checks.Require(probe.Queries < 9000, "20km diagonal ray visits linear path cells rather than its enclosing rectangle");
        var near = new RayMiner(20); probe.Add(0, 0, near);
        Checks.Require(probe.TryRaycast(ray, 20000, out hit, out distance) && hit == near && distance == 20,
            "nearest mining intersection wins across traversed cells");
        probe = new MiningRayProbe(); var negative = new RayMiner(19000); probe.Add(-400, -400, negative);
        Checks.Require(probe.TryRaycast(new Ray(Vector3.zero, new Vector3(-1, 0, -1)), 20000, out hit, out _)
            && hit == negative && probe.Queries < 9000, "negative-coordinate diagonal traversal stays bounded");
        probe = new MiningRayProbe(); probe.Add(0, 100, distant);
        Checks.Require(probe.TryRaycast(new Ray(Vector3.zero, Vector3.forward), 20000, out hit, out _)
            && hit == distant && probe.Queries < 6000, "axis-aligned rays handle zero horizontal components");
        probe = new MiningRayProbe(); probe.Add(0, 0, near);
        Checks.Require(probe.TryRaycast(new Ray(Vector3.zero, Vector3.up), 20000, out hit, out _)
            && hit == near && probe.Queries == 9, "vertical rays test one spatial neighborhood");
        probe = new MiningRayProbe(); probe.Add(-2, 0, near);
        Checks.Require(probe.TryRaycast(new Ray(Vector3.zero, Vector3.left), 64, out hit, out _) && hit == near,
            "negative boundary starts terminate at the correct end cell");
        probe = new MiningRayProbe(); probe.Add(0, 0, new RayMiner(20001));
        Checks.Require(!probe.TryRaycast(ray, 20000, out _, out _), "raycast respects the expanded far limit");
        probe = new MiningRayProbe(); probe.Add(0, 0, new RayMiner(-1));
        Checks.Require(!probe.TryRaycast(ray, 20000, out _, out _), "intersections behind the camera are rejected");
    }
}
