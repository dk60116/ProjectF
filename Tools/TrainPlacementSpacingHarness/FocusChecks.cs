using System;
using UnityEngine;

public partial class PlayerController
{
    enum FocusMarkerKind { Mouse, Selection, Default }
    static Vector3 markerCenter;
    static Vector2 markerSize;
    static void SetBlockFocusVisible(Block block, FocusMarkerKind kind, bool visible, Vector3 center, Vector2 size)
    { markerCenter = center; markerSize = size; }

    public static void RunTrainFocusChecks()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new Exception(message); checks++; }
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (int center in new[] { 0, 1 })
        foreach (FocusMarkerKind kind in Enum.GetValues<FocusMarkerKind>())
        foreach (bool installed in new[] { false, true })
        {
            Train train = LongCarChecks.LongCar();
            train.PlacementCenterCell = new Vector2Int(0, center);
            train.RuntimePlacementSequence = installed ? 1 : 0;
            train.transform.position = new Vector3(50, 20, 50);
            train.transform.rotation = PreviewRotation.LookRotation(new Vector3(axis.x, 0, axis.y), Vector3.up);
            var rootBlock = new Block { Coordinate = new Vector2Int(10, -4) };
            SetTrainFocusVisible(train, rootBlock, kind);
            Vector3 expected = (installed ? Vector3.zero : rootBlock.WorldPosition) + new Vector3(axis.x, 0, axis.y) * .5f;
            Check(Vector3.Distance(markerCenter, expected) < .0001f,
                "Changing Center must not shift focus away from the same authored two-cell body");
            Check(markerSize == (Math.Abs(axis.x) > .5f ? new Vector2(2, 1) : new Vector2(1, 2)),
                "Train focus must keep the axis-aligned floor footprint, not body bounds");
        }
        Console.WriteLine($"Train floor focus checks passed: {checks} checks");
    }
}
