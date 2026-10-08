using System;
using System.Collections.Generic;
using ProjectF.Trains;
using ProjectF.Railway;
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
    // These fixtures model an existing graph, including curves outside new-contact range.
    static void Link(Train a, Train b) { a.SeedConnection(b); }
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

        var lateralRail = new Railload(new Vector2(8, 0.02f), new Vector2(3, 0.02f));
        var lateralA = Car(left, 2.01f, 1);
        var lateralB = Car(lateralRail, 4.75f, 2, true);
        Link(lateralA, lateralB);
        var lateralLayout = new TrainPlacementSpacing();
        Check(lateralLayout.AlignPlacedTrains(new[] { lateralB }), "Complete must align cars across a lateral endpoint gap");
        Check(lateralB.BridgeTarget == lateralRail && Near(lateralB.Point.y, 0.01f), "Complete must retain the one-cell route distance inside the gap");
        Check(Vector2.Dot(lateralB.Facing, Vector2.right) > 0.999f, "Complete must use the rails' longitudinal axis inside the gap");
        Check(lateralB.TryGetConnectionFacingSign(lateralA, false, out float lateralSign) && lateralSign == 1f,
            "Complete must fix the physical rear connection while the car straddles a lateral gap");
        Check(lateralLayout.AlignPlacedTrains(new[] { lateralB }) && Vector2.Dot(lateralB.Facing, Vector2.right) > 0.999f,
            "Repeating Complete inside a lateral gap must retain the car's front");

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
        FacingChecks.Run();
        ManualFacingChecks.Run();
        RailHandcar.RunRailAcquisitionChecks();
        RailHandcar.RunJunctionMotionChecks();
        BlueprintChecks.Run();
        EditChecks.Run();
        WorldChecks.Run();
        Console.WriteLine($"Train spacing harness passed: {checks} checks");
    }
}

public class MapObject
{
    public readonly FakeTransform transform = new FakeTransform();
    public readonly FakeGameObject gameObject = new FakeGameObject();
    public Vector2 CollisionHalfExtents = new Vector2(0.3f, 0.5f);
    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
    protected virtual void OnPlacementRuntimeChanged() { }
    protected virtual void OnPlacementRuntimeCleared() { }
    protected virtual void OnRuntimeMapObjectHandleChanged() { }
    public virtual void PrepareForPool()
    {
        if (this is Train train) { train.RuntimePlacementSequence = 0; train.RuntimeMapObjectHandle = default; }
        OnPlacementRuntimeCleared();
    }
}
public static class ProjectFApplicationLifecycle { public static bool IsQuitting; }
public class FakeTransform
{
    public Vector3 position;
    public PreviewRotation rotation = PreviewRotation.identity;
    public Vector3 forward => rotation * Vector3.forward;
}
public class FakeGameObject { public bool activeInHierarchy = true; }
public partial class Train : VehicleMotionProbe
{
    public float ConnectionSnapMaxDistance = 0.6f;
    public float ConnectionMaxLateralDistance = 0.45f;
    public float ConnectionMinForwardDot = 0.5f;
    const float DefaultConnectionFallbackDistance = 1.4f;
    public static readonly List<Train> ActiveRuntimeTrains = new List<Train>();
    public static void CollectActiveRuntimeTrains(ICollection<Train> results)
    { foreach (Train train in ActiveRuntimeTrains) results.Add(train); }
    public bool TryGetPlacementRuntime(out Vector2Int coordinate, out int quarterTurns)
    { coordinate = Vector2Int.zero; quarterTurns = 0; return RuntimePlacementSequence > 0; }
    private TrainInstance runtimeTrain;
    private readonly Queue<Train> connectionActionGroupQueue = new Queue<Train>();
    private readonly HashSet<Train> connectionActionGroupVisited = new HashSet<Train>();
    private readonly List<TrainInstance> connectionCandidateScratch = new List<TrainInstance>();
    public Train() { TrainWorld.Shared.AttachView(RuntimeTrain, this); }
    public NativeTrainConnections ConnectedTrains => new NativeTrainConnections(runtimeTrain);
    public ProjectF.MapObjects.MapObjectHandle RuntimeMapObjectHandle;
    public void BindHandle(ProjectF.MapObjects.MapObjectHandle handle)
    { RuntimeMapObjectHandle = handle; OnRuntimeMapObjectHandleChanged(); }
    public void Enable() { gameObject.activeInHierarchy = true; OnEnable(); }
    public void Disable() { gameObject.activeInHierarchy = false; OnDisable(); }
    public void DestroyView() { OnDestroy(); }
    public void SeedConnection(Train other)
    {
        TryGetConnectionPose(this, out Vector2 point, out Vector2 facing);
        TryGetConnectionPose(other, out Vector2 otherPoint, out Vector2 otherFacing);
        TrainWorld.Shared.Connect(RuntimeTrain, other.RuntimeTrain,
            Vector2.Dot(otherPoint - point, facing) > 0,
            Vector2.Dot(point - otherPoint, otherFacing) > 0);
    }
    private long runtimePlacementSequence;
    public long RuntimePlacementSequence
    {
        get => runtimePlacementSequence;
        set { runtimePlacementSequence = value; OnPlacementRuntimeChanged(); }
    }
    public Railload Rail
    {
        get => RuntimeTrain.Rail;
        set
        {
            if (value == null) RuntimeTrain.ClearRailSample();
            else RuntimeTrain.SetRailSample(value, Distance, Point, Facing);
        }
    }
    public float Distance => DeterministicSimulationUnits.ToFloat(RuntimeTrain.RailDistanceUnits);
    public Vector2 Point => RuntimeTrain.RailPoint;
    public Vector2 Facing => RuntimeTrain.PhysicalForward;
    public int ApplyCount;
    public Railload BridgeTarget => RuntimeTrain.ConnectionTargetRail;
    public bool TryApplyRailPose(Railload rail, float distance, Vector2 point, Vector2 facing)
    {
        RuntimeTrain.SetRailSample(rail, distance, point, facing); ApplyCount++;
        transform.position = new Vector3(point.x, 0, point.y);
        transform.rotation = PreviewRotation.LookRotation(new Vector3(facing.x, 0, facing.y), Vector3.up);
        TrainWorld.Shared.SetWorldPose(RuntimeTrain, transform.position, transform.rotation);
        return true;
    }
}
public partial class RailHandcar : Train
{
    public int ResetCount;
    public void ResetRailPlacementState() { ResetCount++; }
    public bool TryApplyExplicitRailPose(Railload rail, float distance, Vector2 point, Vector2 facing) => TryApplyRailPose(rail, distance, point, facing);
}
public class SteamTrain : RailHandcar { }
public static class RailConnectionUtility { public const float ConnectionDistance = 0.55f; }
public class Railload
{
    public bool Alive = true;
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

public static class RailTargetLifetime
{
    public static bool IsAlive(this Railload rail) => rail != null && rail.Alive;
}
