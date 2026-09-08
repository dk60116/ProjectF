using System;
using System.Collections.Generic;
using ProjectF.Trains;
using UnityEngine;

static class BlueprintChecks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static Train Car(Railload rail, float distance, bool installed = false)
    {
        var car = new Train { RuntimePlacementSequence = installed ? 1 : 0 };
        rail.TrySampleRenderedPath(distance, out var point, out var facing);
        car.TryApplyRailPose(rail, distance, point, facing);
        if (installed) Train.ActiveRuntimeTrains.Add(car);
        else car.Rail = null; // Blueprints have only a Transform, no runtime rail state.
        return car;
    }

    public static void Run()
    {
        foreach (Vector2 direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
        foreach (bool installed in new[] { false, true })
        {
            Train.ActiveRuntimeTrains.Clear();
            var rail = new Railload(Vector2.zero, direction * 10f);
            var other = Car(rail, 3f, installed);
            var moving = Car(rail, 6f);
            var controller = new InstallationPlacementController(rail);
            if (!installed) controller.Previews.Add(other);
            controller.Previews.Add(moving);
            Vector3 originalOtherPosition = other.transform.position;

            Check(controller.Snap(moving, rail, 3.7f, out var snapped), "An overlapping pointer pose must snap to a free car end");
            Check(Vector2.Distance(snapped, direction * 4f) < 0.005f, "Straight snapping must follow the rail in every direction");
            Check(other.transform.position == originalOtherPosition, "Snapping must not move an existing car or blueprint");
            Check(controller.Snap(moving, rail, 4f, out var repeated) && Vector2.Distance(snapped, repeated) < 0.005f,
                "Recalculating the displayed pose for Complete must not change the snap");
            Check(!controller.Snap(moving, rail, 7.5f, out _), "A distant pointer must not be pulled onto a consist");
            Check(controller.Snap(moving, rail, 2.3f, out var rear) && Vector2.Distance(rear, direction * 2f) < 0.005f,
                "Both available physical ends must accept a blueprint");
        }

        Train.ActiveRuntimeTrains.Clear();
        var straight = new Railload(Vector2.zero, new Vector2(10, 0));
        var initialPlacementController = new InstallationPlacementController(straight);
        Vector2 straightCellSnap = initialPlacementController.SnapInitialCell(
            straight,
            new Vector2Int(3, 0),
            new Vector2(3.37f, 0.12f));
        Check(Vector2.Distance(straightCellSnap, new Vector2(3f, 0f)) < 0.0001f,
            "A first train on a straight rail must snap to the clicked cell center");

        var bentRail = new Railload(
            new Vector2(0f, 0f),
            new Vector2(2.5f, 0f),
            new Vector2(3f, 0.5f),
            new Vector2(3f, 3f));
        var bentPlacementController = new InstallationPlacementController(bentRail);
        Vector2 curvedPointer = new Vector2(2.72f, 0.22f);
        Vector2 curvedPlacement = bentPlacementController.SnapInitialCell(
            bentRail,
            new Vector2Int(3, 0),
            curvedPointer);
        Check(Vector2.Distance(curvedPlacement, new Vector2(3f, 0f)) > 0.05f,
            "A first train on a curved cell must retain continuous rail placement");

        var longCar = Car(straight, 3f);
        longCar.CollisionHalfExtents = new Vector2(0.3f, 0.625f);
        var blueprint = Car(straight, 7f);
        var longController = new InstallationPlacementController(straight);
        longController.Previews.Add(longCar);
        longController.Previews.Add(blueprint);
        Check(longController.Snap(blueprint, straight, 3.85f, out var longSnap), "A longer body must still accept a nearby blueprint");
        Check(longSnap.x >= 4.12f && longSnap.x <= 4.16f, "Preview must find touching clearance instead of forcing overlapping one-cell centers");

        var blocker = Car(straight, 4f);
        longController.Previews.Add(blocker);
        Check(!longController.Snap(blueprint, straight, 3.8f, out _), "An occupied end must not accept a third overlapping car");

        var curvePoints = new Vector2[33];
        for (int i = 0; i < curvePoints.Length; i++)
        {
            float angle = i * MathF.PI / 64f;
            curvePoints[i] = new Vector2(2f * MathF.Sin(angle), 2f * (1f - MathF.Cos(angle)));
        }
        var curve = new Railload(curvePoints);
        var curveCar = Car(curve, 0.2f);
        var curveBlueprint = Car(curve, 2.6f);
        var curveController = new InstallationPlacementController(curve);
        curveController.Previews.Add(curveCar);
        curveController.Previews.Add(curveBlueprint);
        Check(curveController.Snap(curveBlueprint, curve, 1.05f, out var curveSnap), "Curve blueprints must snap along the actual rail");
        curve.TryFindNearestRenderedPathSample(curveSnap, out _, out _, out _, out float offRail);
        Check(offRail < 0.000001f, "Curve snapping must not place a car on the chord between rail points");

        var a = new Train();
        var b = new Train();
        Vector2 curvedPoint = new Vector2(1.2f, 0.5f);
        Vector2 turned = new Vector2(0.6f, 0.8f);
        Check(Train.CanConnectByPose(a, Vector2.zero, Vector2.right, b, curvedPoint, turned),
            "A curved connection must use both car axes");
        Check(Train.CanConnectByPose(b, curvedPoint, turned, a, Vector2.zero, Vector2.right),
            "Curved connection validity must be independent of car iteration order");
        Check(Train.CanConnectByPose(a, Vector2.zero, Vector2.right, b, curvedPoint, -turned),
            "Oppositely facing freight cars must also couple on a curve");
        Check(!Train.CanConnectByPose(a, Vector2.zero, Vector2.right, b, new Vector2(0, 0.3f), Vector2.right),
            "Side-by-side cars must not be treated as end-to-end neighbors");

        foreach (Vector2 engineFacing in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
        {
            var engine = new SteamTrain();
            Vector2 tailPoint = -engineFacing * Train.ConnectionCenterDistance;
            foreach (Train approaching in new Train[] { new Train(), new SteamTrain() })
            for (int turn = 0; turn < 8; turn++)
            {
                float angle = turn * MathF.PI / 4f;
                Vector2 approachingFacing = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Check(Train.CanConnectByPose(engine, Vector2.zero, engineFacing, approaching, tailPoint, approachingFacing),
                    "Touching a locomotive tail must accept every approaching heading, including another locomotive");
                Check(Train.CanConnectByPose(approaching, tailPoint, approachingFacing, engine, Vector2.zero, engineFacing),
                    "Tail coupling must be symmetric regardless of which car initiates it");
            }

            Check(!Train.CanConnectByPose(engine, Vector2.zero, engineFacing, new Train(), -tailPoint, engineFacing),
                "A freight car at the locomotive nose must not couple");
            Check(!Train.CanConnectByPose(engine, Vector2.zero, engineFacing, new SteamTrain(), -tailPoint, -engineFacing),
                "Two locomotive noses must not couple");
            Check(!Train.CanConnectByPose(engine, Vector2.zero, engineFacing, new Train(), tailPoint * 3, engineFacing),
                "A distant car behind the locomotive must remain outside coupling range");
            Vector2 sideways = new Vector2(-engineFacing.y, engineFacing.x);
            Check(!Train.CanConnectByPose(engine, Vector2.zero, engineFacing, new Train(), tailPoint + sideways, sideways),
                "A car outside the tail's lateral range must not couple");
            Check(!Train.CanConnectByPose(engine, Vector2.zero, engineFacing, new Train(), tailPoint * .1f, sideways),
                "Overlapping centers must not count as touching the tail");

            // Same-heading engines used to fail because the approaching engine's
            // nose independently vetoed the existing engine's tail connection.
            foreach (bool installed in new[] { false, true })
            {
                Train.ActiveRuntimeTrains.Clear();
                var engineRail = new Railload(Vector2.zero, engineFacing * 10);
                var leadEngine = new SteamTrain { RuntimePlacementSequence = installed ? 1 : 0 };
                leadEngine.TryApplyRailPose(engineRail, 4, engineFacing * 4, engineFacing);
                var nextEngine = new SteamTrain();
                nextEngine.TryApplyRailPose(engineRail, 2, engineFacing * 2, engineFacing);
                var engineController = new InstallationPlacementController(engineRail);
                if (installed) Train.ActiveRuntimeTrains.Add(leadEngine);
                else { leadEngine.Rail = null; engineController.Previews.Add(leadEngine); }
                nextEngine.Rail = null;
                engineController.Previews.Add(nextEngine);
                Check(engineController.Snap(nextEngine, engineRail, 3.3f, out Vector2 engineSnap)
                      && Vector2.Distance(engineSnap, engineFacing * 3) < .005f,
                    "A same-heading engine blueprint must snap to the existing locomotive's tail");
            }
        }
        Train.ActiveRuntimeTrains.Clear();

        foreach (Vector2 direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
        foreach (bool installed in new[] { false, true })
        {
            Train.ActiveRuntimeTrains.Clear();
            var rail = new Railload(Vector2.zero, direction * 10f);
            var endCar = Car(rail, 3f, installed);
            var endEngine = new SteamTrain();
            rail.TrySampleRenderedPath(6f, out Vector2 enginePoint, out _);
            endEngine.TryApplyRailPose(rail, 6f, enginePoint, -direction);
            endEngine.Rail = null;
            var controller = new InstallationPlacementController(rail);
            if (!installed) controller.Previews.Add(endCar);
            controller.Previews.Add(endEngine);

            Check(controller.SnapWithFacing(
                    endEngine, rail, 4.3f, -direction,
                    out Vector2 snappedPoint, out Vector2 snappedFacing),
                "A locomotive placed at a consist end must find a valid tail coupling");
            Check(Vector2.Distance(snappedPoint, direction * 4f) < .005f,
                "The automatically turned locomotive must keep one-cell end placement");
            Check(Vector2.Dot(snappedFacing, direction) > .999f,
                "A locomotive whose tail faces away must turn around during blueprint snapping");
        }
        Train.ActiveRuntimeTrains.Clear();

        var left = new Railload(Vector2.zero, new Vector2(3, 0));
        var right = new Railload(new Vector2(8, 0.02f), new Vector2(3, 0.02f));
        var connectionCar = Car(left, 2.4f);
        var connectionBlueprint = Car(right, 2f);
        var connectionController = new InstallationPlacementController(left, right);
        connectionController.Previews.Add(connectionCar);
        connectionController.Previews.Add(connectionBlueprint);
        Check(connectionController.Snap(connectionBlueprint, right, 4.9f, out var connectionSnap),
            "A reversed adjacent rail must support blueprint snapping across a small endpoint gap");
        Check(connectionSnap.x > 3.3f && connectionSnap.y > 0.019f, "Connected-rail snap must resolve onto the destination rail");

        var parallel = new Railload(new Vector2(0, 1), new Vector2(8, 1));
        var parallelController = new InstallationPlacementController(left, parallel);
        parallelController.Previews.Add(connectionCar);
        parallelController.Previews.Add(connectionBlueprint);
        Check(!parallelController.Snap(connectionBlueprint, parallel, 3.2f, out _), "Unconnected parallel rails must not snap together");
        Train.ActiveRuntimeTrains.Clear();
        Console.WriteLine($"Train blueprint harness passed: {checks} checks");
    }
}

// Managed yaw-only rotation for the non-Unity host. The tested placement,
// connection, rail route and SAT collision methods are extracted from production.
public readonly struct PreviewRotation
{
    readonly Vector3 forward;
    PreviewRotation(Vector3 forward) { this.forward = forward.normalized; }
    public static PreviewRotation identity => new PreviewRotation(Vector3.forward);
    public static PreviewRotation LookRotation(Vector3 forward, Vector3 up) => new PreviewRotation(forward);
    public static Vector3 operator *(PreviewRotation rotation, Vector3 vector)
    {
        Vector3 right = new Vector3(rotation.forward.z, 0, -rotation.forward.x);
        return right * vector.x + Vector3.up * vector.y + rotation.forward * vector.z;
    }
}

public partial class InstallationPlacementController
{
    const float TrainPlacementRailSearchRadius = 2.25f;
    const float TrainPlacementSeparationEpsilon = 0.005f;
    readonly List<Railload> rails;
    readonly List<MapObject> installPreviewInstances = new List<MapObject>();
    readonly List<Train> trainConnectionInstalledScratch = new List<Train>();
    readonly List<Train> trainPlacementInstalledScratch = new List<Train>();
    readonly List<TrainCollisionBox2D> trainPlacementCollisionBoxes = new List<TrainCollisionBox2D>();
    readonly List<TrainCollisionBox2D> trainPlacementOtherCollisionBoxes = new List<TrainCollisionBox2D>();
    readonly TrainPlacementSpacing trainPlacementSpacing = new TrainPlacementSpacing();
    public List<MapObject> Previews => installPreviewInstances;
    public InstallationPlacementController(params Railload[] rails) { this.rails = new List<Railload>(rails); }
    static Train ResolveTrainSource(MapObject source) => source as Train;
    static bool IsTrainSource(MapObject source) => source is Train;
    static MapObject ResolveInstallPreviewPlacementSource(MapObject source) => source;
    static void CleanupInstallPreviewReferences() { }
    static Vector2Int RoundWorldPositionToCoordinate(Vector3 point) => new Vector2Int((int)MathF.Round(point.x), (int)MathF.Round(point.z));
    static Vector2 ResolveTrainPlacementFacingTangent(Vector2 tangent, Vector2 reference) => Vector2.Dot(tangent, reference) < 0 ? -tangent : tangent;
    bool TryFindNearestTrainPlacementRailSampleAroundCoordinate(Vector2Int coordinate, Vector2 point, int cells, out TrainPlacementRailSample sample)
    {
        sample = new TrainPlacementRailSample { SqrDistance = float.PositiveInfinity };
        foreach (var rail in rails)
        {
            if (rail.TryFindNearestRenderedPathSample(point, out var distance, out var position, out var tangent, out var sqr)
                && sqr < sample.SqrDistance)
                sample = new TrainPlacementRailSample { Rail = rail, DistanceAlongPath = distance, Point = position, Tangent = tangent, SqrDistance = sqr };
        }
        return sample.Rail != null;
    }
    static void BuildTrainCollisionBoxes(MapObject source, Vector3 position, PreviewRotation rotation, List<TrainCollisionBox2D> boxes)
    {
        boxes.Clear();
        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;
        boxes.Add(new TrainCollisionBox2D
        {
            Center = new Vector2(position.x, position.z), AxisRight = new Vector2(right.x, right.z),
            AxisForward = new Vector2(forward.x, forward.z), HalfExtents = source.CollisionHalfExtents
        });
    }
    public bool Snap(Train preview, Railload rail, float distance, out Vector2 point)
    {
        rail.TrySampleRenderedPath(distance, out _, out var tangent);
        return SnapWithFacing(preview, rail, distance, tangent, out point, out _);
    }

    public bool SnapWithFacing(
        Train preview,
        Railload rail,
        float distance,
        Vector2 referenceFacing,
        out Vector2 point,
        out Vector2 snappedFacing)
    {
        rail.TrySampleRenderedPath(distance, out point, out var tangent);
        var sample = new TrainPlacementRailSample { Rail = rail, DistanceAlongPath = distance, Point = point, Tangent = tangent };
        Vector3 position = new Vector3(point.x, 0, point.y);
        var rotation = PreviewRotation.LookRotation(new Vector3(referenceFacing.x, 0, referenceFacing.y), Vector3.up);
        bool snapped = TrySnapTrainBlueprintToConnection(preview, preview, ref position, ref rotation, ref sample);
        point = new Vector2(position.x, position.z);
        Vector3 forward = rotation * Vector3.forward;
        snappedFacing = new Vector2(forward.x, forward.z);
        return snapped;
    }

    public Vector2 SnapInitialCell(Railload rail, Vector2Int coordinate, Vector2 referencePoint)
    {
        rail.TryFindNearestRenderedPathSample(
            referencePoint,
            out float distance,
            out Vector2 point,
            out Vector2 tangent,
            out float sqrDistance);
        var sample = new TrainPlacementRailSample
        {
            Rail = rail,
            DistanceAlongPath = distance,
            Point = point,
            Tangent = tangent,
            SqrDistance = sqrDistance
        };
        TrySnapTrainPlacementRailSampleToStraightCellCenter(coordinate, ref sample);
        return sample.Point;
    }
}
