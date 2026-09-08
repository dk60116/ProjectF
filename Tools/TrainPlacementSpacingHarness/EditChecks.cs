using System;
using UnityEngine;

static class EditChecks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static Train Place(Train train, Railload rail, float distance, bool reverse, int sequence)
    {
        rail.TrySampleRenderedPath(distance, out var point, out var tangent);
        train.TryApplyRailPose(rail, distance, point, reverse ? -tangent : tangent);
        train.RuntimePlacementSequence = sequence;
        Train.ActiveRuntimeTrains.Add(train);
        return train;
    }

    public static void Run()
    {
        foreach (var direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
        foreach (bool reversePartner in new[] { false, true })
        {
            Train.ActiveRuntimeTrains.Clear();
            var rail = new Railload(Vector2.zero, direction * 12);
            var engine = (SteamTrain)Place(new SteamTrain(), rail, 3, false, 1);
            var freight = Place(new Train(), rail, 4.15f, reversePartner, 2);
            var controller = new InstallationPlacementController(rail);
            Check(!engine.ConnectTo(freight), "A freight car at the engine nose must not couple");
            Check(controller.RotateForEdit(engine), "Edit rotation must turn the engine");
            Check(engine.TryGetConnectionFacingSign(freight, true, out var sign) && sign == -1,
                "Rotating the tail towards a touching car must create a real rear coupling immediately");
            Check(freight.TryGetConnectionFacingSign(engine, false, out _), "Edit coupling must be reciprocal");
            Check(Math.Abs(Vector2.Distance(engine.Point, freight.Point) - 1.15f) < .001f,
                "Rotation must leave spacing unchanged until Complete");
            controller.CompleteForEdit();
            Check(Math.Abs(Vector2.Distance(engine.Point, freight.Point) - 1f) < .001f,
                "Edit Complete must apply one-cell center spacing");
            Check(Vector2.Dot(engine.Facing, -direction) > .999f, "Complete must preserve the rotated physical front");
            Check(controller.RotateForEdit(engine), "The engine must rotate back");
            Check(engine.ConnectedTrains.Count == 0 && freight.ConnectedTrains.Count == 0,
                "Rotating the only locomotive tail away must remove the stale nose coupling");
        }

        foreach (var direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
        {
            Train.ActiveRuntimeTrains.Clear();
            var rail = new Railload(Vector2.zero, direction * 12);
            var cars = new[] {
                Place(new SteamTrain(), rail, 2, true, 1),
                Place(new Train(), rail, 3.15f, false, 2),
                Place(new Train(), rail, 4.3f, true, 3),
                Place(new Train(), rail, 5.45f, false, 4),
                Place(new SteamTrain(), rail, 6.6f, false, 5)
            };
            var controller = new InstallationPlacementController(rail);
            controller.CompleteForEdit();
            for (int i = 0; i < cars.Length; i++)
            {
                Check(cars[i].ConnectedTrains.Count == (i == 0 || i == 4 ? 1 : 2),
                    "Edit Complete must commit the full engine-freight-engine chain without branches");
                Check(Vector2.Distance(cars[i].Point, direction * (2 + i)) < .001f,
                    "Every car in the committed chain must use one-cell spacing");
            }
            Check(Vector2.Dot(cars[0].Facing, -direction) > .999f && Vector2.Dot(cars[4].Facing, direction) > .999f,
                "Both end locomotives must keep their outward headings");
            controller.CompleteForEdit();
            Check(Vector2.Distance(cars[4].Point, direction * 6) < .001f, "Repeated edit Complete must not drift");
        }
        Train.ActiveRuntimeTrains.Clear();
        Console.WriteLine($"Train edit harness passed: {checks} checks");
    }
}

public partial class InstallationPlacementController
{
    bool mapEditModeActive;
    Train selectedEditableInstallation;
    Vector2Int selectedEditableAnchorCoordinate;
    bool IsEditingInstallation() => false;
    void CompleteInstallationEdit() => throw new Exception("Unexpected general installation edit path");
    void SetMapEditModeActive(bool active) { mapEditModeActive = active; }
    void RefreshTrainInstallPreviewTints() { }
    void RefreshMapEditButtonState() { }
    public bool RotateForEdit(SteamTrain train)
    {
        mapEditModeActive = true;
        selectedEditableInstallation = train;
        return TryRotateSelectedSteamTrain();
    }
    public void CompleteForEdit()
    {
        mapEditModeActive = true;
        HandleInstallCompleteClicked();
        if (mapEditModeActive) throw new Exception("Complete must leave edit mode");
    }
}

static class Physics { public static void SyncTransforms() { } }
static class Debug { public static void LogWarning(string message, object context) => throw new Exception(message); }
