using System;
using System.Collections.Generic;
using ProjectF.Railway;
using UnityEngine;

public class Sprite { }
public class SpriteRenderer
{
    public bool enabled;
    public Sprite sprite;
    public readonly FakeTransform transform = new FakeTransform { localPosition = new Vector3(0, .38f, 0) };
}

public partial class Locomotive
{
    public ItemDefinition FuelDisplayDefinition;
}

public partial class FreightCar : Train
{
    SpriteRenderer fuelRoleIcon = new SpriteRenderer();
    List<FakeTransform> itemPointList;

    private void UpdateTestItemPoints()
    {
        foreach (var point in itemPointList)
            if (point != null) point.position = transform.TransformPoint(point.localPosition);
    }

    public static void RunFuelRoleChecks()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new Exception(message); checks++; }
        var icon = new Sprite();
        var rail = new Railload(new Vector2(-30, 0), new Vector2(30, 0));
        FreightCar warmCar = null;
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (float carFacing in new[] { -1f, 1f })
        foreach (int length in new[] { 1, 2 })
        foreach (int center in new[] { 0, 1 })
        {
            var engine = new Locomotive { FuelDisplayDefinition = new ItemDefinition { icon = icon } };
            var car = new FreightCar
            {
                Status = (1, (byte)length), PlacementCenterCell = new Vector2Int(0, Math.Min(center, length - 1)),
                BodyCollider = new BoxCollider { center = new Vector3(0, .5f, (length - 1) * .5f), size = new Vector3(.6f, 1, length - .1f) }
            };
            var frontCar = new FreightCar();
            var nextCar = new FreightCar();
            var rearPoint = new FakeTransform { localPosition = new Vector3(-.13f, 12, .17f) };
            var frontPoint = new FakeTransform { localPosition = new Vector3(.24f, -7, 1.21f) };
            car.itemPointList = length == 1
                ? new List<FakeTransform> { null, rearPoint }
                : new List<FakeTransform> { frontPoint, null, rearPoint };
            car.fuelRoleIcon.transform.parent = car.transform;
            engine.TryApplyRailPose(rail, 30, Vector2.zero, axis);
            car.TryApplyRailPose(rail, 27, -axis * 3, axis * carFacing);
            frontCar.TryApplyRailPose(rail, 32, axis * 2, axis);
            TrainWorld.Shared.Connect(engine.RuntimeTrain, car.RuntimeTrain, false, carFacing > 0);
            TrainWorld.Shared.Connect(engine.RuntimeTrain, frontCar.RuntimeTrain, true, false);
            TrainWorld.Shared.Connect(car.RuntimeTrain, nextCar.RuntimeTrain, carFacing < 0, true);
            car.UpdateTestItemPoints();
            Check(engine.TryGetRearFreightCar(out FreightCar supplier) && supplier == car,
                "Only the directly rear-coupled car supplies the locomotive, not the front or next car");
            car.RefreshFuelRoleIcon(); frontCar.RefreshFuelRoleIcon(); nextCar.RefreshFuelRoleIcon();
            Check(car.fuelRoleIcon.enabled && car.fuelRoleIcon.sprite == icon,
                "The direct rear supplier must show the locomotive's fuel icon");
            Check(!frontCar.fuelRoleIcon.enabled && !nextCar.fuelRoleIcon.enabled,
                "Other cars in the consist must not inherit the supplier marker");
            Vector3 expected = length == 2 && carFacing > 0 ? frontPoint.localPosition : rearPoint.localPosition;
            expected.y = .38f;
            Check(Vector3.Distance(car.fuelRoleIcon.transform.localPosition, expected) < .0001f,
                "The marker must match engine-side ItemPoint XZ, ignoring its height and list order");

            // A tight bend can put a rear-connected car in front of the engine's
            // tangent plane. Stable coupling metadata must still own its role.
            car.TryApplyRailPose(rail, 33, axis * 3, axis * carFacing);
            car.UpdateTestItemPoints();
            Check(engine.TryGetRearFreightCar(out supplier) && supplier == car,
                "A curved consist must not select fuel supply by current root projection");
            car.RefreshFuelRoleIcon();
            Check(car.fuelRoleIcon.enabled && Vector3.Distance(car.fuelRoleIcon.transform.localPosition, expected) < .0001f,
                "A bend cannot move the fuel marker to the other end of the car");
            var iconParent = new FakeTransform
            {
                position = new Vector3(11, 3, -9),
                rotation = PreviewRotation.LookRotation(Vector3.right, Vector3.up),
                lossyScale = new Vector3(2, 1, .5f)
            };
            car.fuelRoleIcon.transform.parent = iconParent;
            car.fuelRoleIcon.transform.localPosition = new Vector3(99, .72f, -99);
            car.RefreshFuelRoleIcon();
            var selectedPoint = length == 2 && carFacing > 0 ? frontPoint : rearPoint;
            Vector3 expectedNested = iconParent.InverseTransformPoint(selectedPoint.position);
            expectedNested.y = .72f;
            Check(Vector3.Distance(car.fuelRoleIcon.transform.localPosition, expectedNested) < .0001f,
                "Icon-parent rotation/scale must not offset ItemPoint XZ or change the authored icon height");
            selectedPoint.localPosition += new Vector3(.06f, 40, -.08f);
            car.UpdateTestItemPoints();
            car.RefreshFuelRoleIcon();
            expectedNested = iconParent.InverseTransformPoint(selectedPoint.position);
            expectedNested.y = .72f;
            Check(Vector3.Distance(car.fuelRoleIcon.transform.localPosition, expectedNested) < .0001f,
                "Moving the authored ItemPoint must update marker XZ without following ItemPoint Y");
            warmCar = car;
        }
        for (int i = 0; i < 100; i++) warmCar.RefreshFuelRoleIcon();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) warmCar.RefreshFuelRoleIcon();
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm fuel-role marker refresh must allocate zero managed bytes");
        var previousPosition = warmCar.fuelRoleIcon.transform.localPosition;
        warmCar.itemPointList = null;
        warmCar.RefreshFuelRoleIcon();
        Check(warmCar.fuelRoleIcon.transform.localPosition == previousPosition,
            "A missing ItemPoint list must not move the marker to an unrelated footprint or world origin");
        warmCar.itemPointList = new List<FakeTransform> { null };
        warmCar.RefreshFuelRoleIcon();
        Check(warmCar.fuelRoleIcon.transform.localPosition == previousPosition,
            "Null-only ItemPoint lists must preserve the marker position");
        warmCar.ClearTrainConnections(); warmCar.RefreshFuelRoleIcon();
        Check(!warmCar.fuelRoleIcon.enabled && warmCar.fuelRoleIcon.sprite == null,
            "Disconnecting the supplier must clear the marker immediately");
        Console.WriteLine($"Train fuel-role checks passed: {checks} checks; warm refresh 0 bytes allocated");
    }
}
