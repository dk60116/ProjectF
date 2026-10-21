using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Trains;

static class LongCarChecks
{
    static int checks;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static bool Near(float a, float b) => Math.Abs(a - b) < .0003f;
    public static Train LongCar() => new Train
    {
        Status = (1, 2), PlacementCenterCell = Vector2Int.zero,
        BodyCollider = new BoxCollider { center = new Vector3(0, .5f, .5f), size = new Vector3(.6f, 1, 1.9f) }
    };

    public static void Run()
    {
        var longCar = LongCar();
        Check(Near(longCar.ResolveConnectionReach(true), 1.5f) && Near(longCar.ResolveConnectionReach(false), .5f),
            "A two-cell car anchored on its rear cell must expose distinct front and rear coupling reaches");
        Check(Near(longCar.ResolveRailEndClearance(true), 1.45f) && Near(longCar.ResolveRailEndClearance(false), .45f),
            "The two-cell body collider must also drive the physical dead-end margin");

        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reverseRail in new[] { false, true })
        foreach (bool firstLong in new[] { false, true })
        foreach (bool secondLong in new[] { false, true })
        foreach (float firstSign in new[] { -1f, 1f })
        foreach (float secondSign in new[] { -1f, 1f })
        {
            var rail = reverseRail ? new Railload(axis * 20, Vector2.zero) : new Railload(Vector2.zero, axis * 20);
            var first = firstLong ? LongCar() : new Train();
            var second = secondLong ? LongCar() : new Train();
            float spacing = (firstLong ? (firstSign > 0 ? 1.5f : .5f) : .5f)
                + (secondLong ? (secondSign < 0 ? 1.5f : .5f) : .5f);
            first.RuntimePlacementSequence = 1;
            second.RuntimePlacementSequence = 2;
            float aDistance = reverseRail ? 15 : 5;
            float bDistance = reverseRail ? 15 - spacing - .1f : 5 + spacing + .1f;
            rail.TrySampleRenderedPath(aDistance, out var aPoint, out _);
            rail.TrySampleRenderedPath(bDistance, out var bPoint, out _);
            first.TryApplyRailPose(rail, aDistance, aPoint, axis * firstSign);
            second.TryApplyRailPose(rail, bDistance, bPoint, axis * secondSign);
            Check(Near(Train.ResolveConnectionSpacing(first, axis * firstSign, second, axis * secondSign, bPoint - aPoint), spacing),
                "Pair spacing must include the actual connecting ends, even when either car is reversed");
            Check(first.CanConnectTo(second) && second.CanConnectTo(first), "Long cars must be discoverable and connect symmetrically");
            Check(first.ConnectTo(second), "A legal mixed-length contact must connect");
            Check(new TrainPlacementSpacing().AlignPlacedTrains(new MapObject[] { first, second }),
                "Complete must align mixed one- and two-cell consists");
            Check(Near(Vector2.Distance(first.Point, second.Point), spacing),
                "Complete must not collapse a two-cell connection into one-cell pitch");
            Check(Near(Vector2.Dot(first.Point, axis), 5), "The oldest endpoint must remain fixed");
            Check(new TrainPlacementSpacing().AlignPlacedTrains(new MapObject[] { second })
                && Near(Vector2.Distance(first.Point, second.Point), spacing), "Repeating Complete must not drift a mixed-length consist");
        }

        InstallationPlacementController.RunLongFootprintChecks(Check);
        InstallationPlacementController.RunLongBlueprintChecks(Check);
        RunLongConnectionSearchChecks();
        Train.ActiveRuntimeTrains.Clear();
        Console.WriteLine($"Long Flatcar footprint and connection checks passed: {checks} checks");
    }

    static void RunLongConnectionSearchChecks()
    {
        ProjectF.Railway.TrainWorld.Shared.Clear();
        var rail = new Railload(new Vector2(-5, 0), new Vector2(10, 0));
        var shortCar = new Train { RuntimePlacementSequence = 1 };
        var longCar = LongCar();
        longCar.RuntimePlacementSequence = 2;
        shortCar.TryApplyRailPose(rail, 5, Vector2.zero, Vector2.right);
        longCar.TryApplyRailPose(rail, 7.3f, new Vector2(2.3f, 0), Vector2.left);
        Check(shortCar.TryGetTouchingUnconnectedTrain(out Train found) && found == longCar,
            "A short car must discover a long car outside the old one-cell spatial query radius");
        Check(longCar.TryGetTouchingUnconnectedTrain(out found) && found == shortCar,
            "The long-car connection action must work from either vehicle");
        for (int i = 0; i < 20; i++)
            Train.ResolveConnectionSpacing(shortCar, Vector2.right, longCar, Vector2.left, Vector2.right);
        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        for (int i = 0; i < 10000; i++)
            sum += Train.ResolveConnectionSpacing(shortCar, Vector2.right, longCar, Vector2.left, Vector2.right);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && Near(sum, 20000), "Warm mixed-length spacing queries must not allocate managed memory");
    }
}

public class Block
{
    public Vector2Int Coordinate;
    public bool AllowsTrain = true;
}

public class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public readonly HashSet<Vector2Int> Dropped = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
    public bool HasDroppedFloorObjectsAt(Vector2Int coordinate) => Dropped.Contains(coordinate);
}

public partial class InstallationPlacementController
{
    readonly TerrainGenerator FootprintTerrain = new();
    TerrainGenerator ResolveInstallPreviewTerrain() => FootprintTerrain;
    static bool AllowsPlacementOverDroppedFloorObjects(MapObject source) => false;
    static bool CanPlacePreviewOnTargetBlockType(Block block, MapObject source,
        Vector2Int? anchorCoordinate = null, int quarterTurns = 0, MapObject previewToIgnore = null) => block.AllowsTrain;

    public static void RunLongFootprintChecks(Action<bool, string> check)
    {
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        {
            var controller = new InstallationPlacementController();
            var source = LongCarChecks.LongCar();
            var rotation = PreviewRotation.LookRotation(new Vector3(axis.x, 0, axis.y), Vector3.up);
            Vector2Int front = new Vector2Int((int)axis.x, (int)axis.y);
            controller.FootprintTerrain.Blocks[Vector2Int.zero] = new Block();
            check(!controller.CanPlaceTrainAtPose(source, Vector3.zero, rotation, null),
                "A missing second cell must reject a two-cell train placement");
            controller.FootprintTerrain.Blocks[front] = new Block { Coordinate = front };
            check(controller.CanPlaceTrainAtPose(source, Vector3.zero, rotation, null),
                "Two clear floor/rail cells must allow placement in every rotation");
            controller.FootprintTerrain.Blocks[front].AllowsTrain = false;
            check(!controller.CanPlaceTrainAtPose(source, Vector3.zero, rotation, null),
                "Water or a blocked front cell must reject placement even if the anchor is clear");
            controller.FootprintTerrain.Blocks[front].AllowsTrain = true;
            controller.FootprintTerrain.Dropped.Add(front);
            check(!controller.CanPlaceTrainAtPose(source, Vector3.zero, rotation, null),
                "Dropped items on the second cell must obey existing placement restrictions");
            controller.FootprintTerrain.Dropped.Clear();
            var obstacle = new Train { RuntimePlacementSequence = 3 };
            obstacle.transform.position = new Vector3(axis.x, 0, axis.y);
            obstacle.transform.rotation = rotation;
            Train.ActiveRuntimeTrains.Add(obstacle);
            check(!controller.CanPlaceTrainAtPose(source, Vector3.zero, rotation, null),
                "The extended body collider must reject overlap on the forward cell");
            Train.ActiveRuntimeTrains.Clear();
        }
    }

    public static void RunLongBlueprintChecks(Action<bool, string> check)
    {
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (bool reversedCar in new[] { false, true })
        {
            Train.ActiveRuntimeTrains.Clear();
            var rail = new Railload(axis * -10, axis * 10);
            var controller = new InstallationPlacementController(rail);
            for (int x = -10; x <= 10; x++)
            for (int y = -10; y <= 10; y++)
            {
                var coordinate = new Vector2Int(x, y);
                controller.FootprintTerrain.Blocks[coordinate] = new Block { Coordinate = coordinate };
            }
            var neighbor = new Train { RuntimePlacementSequence = 1 };
            neighbor.TryApplyRailPose(rail, 10, Vector2.zero, axis);
            Train.ActiveRuntimeTrains.Add(neighbor);
            var source = LongCarChecks.LongCar();
            float spacing = reversedCar ? 2 : 1;
            source.TryApplyRailPose(rail, 10 + spacing + .25f, axis * (spacing + .25f), reversedCar ? -axis : axis);
            controller.Previews.Add(source);
            check(controller.SnapWithFacing(source, rail, 10 + spacing + .25f, reversedCar ? -axis : axis,
                    out Vector2 point, out Vector2 facing), "A two-cell blueprint must snap using the correct connecting end");
            check(Math.Abs(Vector2.Dot(point, axis) - spacing) < .06f
                    && Vector2.Dot(facing, reversedCar ? -axis : axis) > .999f,
                "Blueprint snapping must preserve orientation and leave room for the complete two-cell body");
        }
        Train.ActiveRuntimeTrains.Clear();
    }
}
