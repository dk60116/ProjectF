using System;
using System.Collections.Generic;
using ProjectF.Trains;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
    static Train Car(Railload rail, float distance, int sequence, bool reverse = false, bool engine = false)
    {
        Train car = engine ? new RailHandcar() : new Train();
        car.RuntimePlacementSequence = sequence;
        rail.TrySampleRenderedPath(distance, out var point, out var tangent);
        car.TryApplyRailPose(rail, distance, point, reverse ? -tangent : tangent);
        car.ApplyCount = 0;
        return car;
    }
    static void Link(Train a, Train b) { a.ConnectedTrains.Add(b); b.ConnectedTrains.Add(a); }
    static void Main()
    {
        Check(Near(Train.ConnectionCenterDistance, 1f), "Production center pitch must be one block");
        foreach (Vector2 direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
        {
            var rail = new Railload(new Vector2(12, -8), new Vector2(12, -8) + direction * 20f);
            var cars = new Train[5];
            for (int i = 0; i < cars.Length; i++)
            {
                cars[i] = Car(rail, 2f + i * 1.25f, i + 1, i % 2 != 0, i == 0);
                if (i > 0) Link(cars[i - 1], cars[i]);
            }
            var layout = new TrainPlacementSpacing();
            Check(Near(Vector2.Distance(cars[0].Point, cars[1].Point), 1.25f), "Preview poses must remain untouched before Complete");
            Check(layout.AlignPlacedTrains(new MapObject[] { cars[4], cars[2], cars[1] }), "Complete must align mixed old and new cars");
            Check(Near(cars[0].Distance, 2f), "Oldest endpoint must remain anchored");
            for (int i = 0; i < cars.Length; i++)
            {
                Check(Near(cars[i].Distance, 2f + i), "Straight center position must use one-cell pitch");
                Check(cars[i].ApplyCount == 1, "A connected group must be processed once per Complete");
                Check(Vector2.Dot(cars[i].Facing, direction) * (i % 2 == 0 ? 1 : -1) > 0.999f, "Car facing must be preserved");
            }
            Check(((RailHandcar)cars[0]).ResetCount == 1, "Complete must invalidate the old movement path");
            Check(layout.AlignPlacedTrains(cars), "Repeating Complete must be safe");
            Check(Near(cars[4].Distance, 6f), "Repeating Complete must not drift the chain");
        }

        var curve = new Railload(new Vector2(0, 0), new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 5));
        var curved = new[] { Car(curve, 1, 1), Car(curve, 2.4f, 2), Car(curve, 3.8f, 3), Car(curve, 5.2f, 4) };
        for (int i = 1; i < curved.Length; i++) Link(curved[i - 1], curved[i]);
        Check(new TrainPlacementSpacing().AlignPlacedTrains(curved), "Freight-only curve must align without an engine");
        for (int i = 0; i < curved.Length; i++) Check(Near(curved[i].Distance, i + 1), "Curve must use path distance");

        var left = new Railload(new Vector2(0, 0), new Vector2(3, 0));
        var right = new Railload(new Vector2(7, 0), new Vector2(3, 0));
        var a = Car(left, 2.3f, 1);
        var b = Car(right, 3.4f, 2);
        var c = Car(right, 2.1f, 3, true);
        Link(a, b); Link(b, c);
        Check(new TrainPlacementSpacing().AlignPlacedTrains(new[] { b, c }), "Reversed rail segment must connect");
        Check(Near(b.Point.x, 3.3f) && Near(c.Point.x, 4.3f), "Cross-rail centers must remain one block apart");
        Check(b.Facing.x < 0 && c.Facing.x > 0, "Cross-rail facing must be retained");

        var gapRight = new Railload(new Vector2(3.4f, 0), new Vector2(8, 0));
        var gapA = Car(left, 2.2f, 1);
        var gapB = Car(gapRight, 0.15f, 2);
        Link(gapA, gapB);
        Check(new TrainPlacementSpacing().AlignPlacedTrains(new[] { gapB }), "Rail endpoint bridge must align");
        Check(Near(gapB.Point.x, 3.2f) && gapB.BridgeTarget == gapRight, "Bridge target must keep transition data for movement");
        Check(new TrainPlacementSpacing().AlignPlacedTrains(new[] { gapB }), "Repeating Complete on a bridge must be safe");
        Check(Near(gapB.Point.x, 3.2f), "Bridge re-alignment must not drift");
        var gapC = Car(gapRight, 1.15f, 3);
        Link(gapB, gapC);
        Check(new TrainPlacementSpacing().AlignPlacedTrains(new[] { gapC }), "Appending to a chain with an existing bridge car must work");
        Check(Near(gapC.Point.x, 4.2f), "Car after a bridge must use one-cell spacing");

        var isolated = Car(left, 0.2f, 99);
        Check(new TrainPlacementSpacing().AlignPlacedTrains(new[] { isolated }), "Unconnected train must remain valid");
        Check(isolated.ApplyCount == 0, "Unconnected train must not move");
        var shortRail = new Railload(Vector2.zero, new Vector2(0.9f, 0));
        var badA = Car(shortRail, 0.2f, 1);
        var badB = Car(shortRail, 0.7f, 2);
        Link(badA, badB);
        Check(!new TrainPlacementSpacing().AlignPlacedTrains(new[] { badA }), "Insufficient route must fail planning");
        Check(badA.ApplyCount == 0 && badB.ApplyCount == 0, "Failed planning must not partially move a chain");
        var closeA = Car(left, 0.2f, 1);
        var closeB = Car(left, 1.198f, 2);
        Link(closeA, closeB);
        Check(new TrainPlacementSpacing().AlignPlacedTrains(new[] { closeA }), "Placement tolerance must still produce exactly one-cell spacing");
        Check(Near(closeB.Distance, 1.2f), "Short placement must extend onto the available tail rail");
        Console.WriteLine($"Train spacing harness passed: {checks} checks");
    }
}

public class MapObject { }
public class FakeGameObject { public bool activeInHierarchy = true; }
public partial class Train : MapObject
{
    public readonly FakeGameObject gameObject = new FakeGameObject();
    public readonly HashSet<Train> ConnectedTrains = new HashSet<Train>();
    public long RuntimePlacementSequence;
    public Railload Rail;
    public float Distance;
    public Vector2 Point, Facing;
    public int ApplyCount;
    public Railload BridgeTarget;
    float bridgeDistance, bridgeLength, bridgeProgress;
    Vector2 bridgePoint, bridgeTangent;
    public bool TryGetCurrentRailPose(out Railload rail, out float distance, out Vector2 point, out Vector2 facing)
    { rail = Rail; distance = Distance; point = Point; facing = Facing; return rail != null; }
    public bool TryApplyRailPose(Railload rail, float distance, Vector2 point, Vector2 facing)
    { Rail = rail; Distance = distance; Point = point; Facing = facing; ApplyCount++; BridgeTarget = null; return true; }
    public bool TryGetCurrentRailConnectionTransition(out Railload rail, out float distance, out Vector2 point, out Vector2 tangent, out float length, out float progress)
    { rail = BridgeTarget; distance = bridgeDistance; length = bridgeLength; progress = bridgeProgress; point = bridgePoint; tangent = bridgeTangent; return rail != null; }
    public void ConfigureCurrentRailConnectionTransition(Railload rail, float distance, Vector2 point, Vector2 tangent, float length, float progress)
    { BridgeTarget = rail; bridgeDistance = distance; bridgePoint = point; bridgeTangent = tangent; bridgeLength = length; bridgeProgress = progress; }
}
public class RailHandcar : Train
{
    public int ResetCount;
    public void ResetRailPlacementState() { ResetCount++; }
    public bool TryApplyExplicitRailPose(Railload rail, float distance, Vector2 point, Vector2 facing) => TryApplyRailPose(rail, distance, point, facing);
}
public static class RailConnectionUtility { public const float ConnectionDistance = 0.55f; }
public class Railload
{
    readonly Vector2[] points;
    public Railload(params Vector2[] points) { this.points = points; }
    public bool TryGetRenderedPathLength(out float length)
    { length = 0; for (int i = 1; i < points.Length; i++) length += Vector2.Distance(points[i - 1], points[i]); return length > 0; }
    public bool TrySampleRenderedPath(float distance, out Vector2 point, out Vector2 tangent)
    {
        point = Vector2.zero; tangent = Vector2.up;
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 delta = points[i] - points[i - 1];
            float length = delta.magnitude;
            if (distance <= length + 0.0001f)
            { point = points[i - 1] + delta * (Math.Clamp(distance, 0, length) / length); tangent = delta.normalized; return true; }
            distance -= length;
        }
        return false;
    }
    public bool TryGetRenderedEndpointSample(bool start, out float distance, out Vector2 point, out Vector2 tangent)
    {
        distance = 0;
        if (!start) for (int i = 1; i < points.Length; i++) distance += Vector2.Distance(points[i - 1], points[i]);
        return TrySampleRenderedPath(distance, out point, out tangent);
    }
    public bool TryFindNearestRenderedPathSample(Vector2 query, out float distance, out Vector2 point, out Vector2 tangent, out float sqr)
    {
        distance = 0; point = tangent = Vector2.zero; sqr = float.PositiveInfinity;
        float walked = 0;
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 delta = points[i] - points[i - 1];
            float t = Math.Clamp(Vector2.Dot(query - points[i - 1], delta) / delta.sqrMagnitude, 0, 1);
            Vector2 candidate = points[i - 1] + delta * t;
            float candidateSqr = (query - candidate).sqrMagnitude;
            if (candidateSqr < sqr)
            { point = candidate; tangent = delta.normalized; sqr = candidateSqr; distance = walked + t * delta.magnitude; }
            walked += delta.magnitude;
        }
        return !float.IsPositiveInfinity(sqr);
    }
}
