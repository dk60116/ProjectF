using System;
using System.Collections.Generic;
using UnityEngine;

public interface IMapObjectTarget { }
public partial class Train : IMapObjectTarget { }

public partial class PlayerController
{
    enum FocusMarkerKind { Mouse, Selection, Default }
    static Vector3 markerCenter;
    static Vector2 markerSize;
    static void SetBlockFocusVisible(Block block, FocusMarkerKind kind, bool visible, Vector3 center, Vector2 size)
    { markerCenter = center; markerSize = size; }

    private IMapObjectTarget currentSelectedMapObject;
    private IMapObjectTarget currentMouseFocusedMapObject;
    private readonly List<Block> mouseFocusBlocks = new List<Block>();
    private readonly List<Block> mouseFocusRemovalBuffer = new List<Block>();
    private readonly HashSet<Block> currentMouseFocusedBlocks = new HashSet<Block>();
    private readonly List<Block> selectedFocusBlocks = new List<Block>();
    private readonly List<Block> selectedFocusRemovalBuffer = new List<Block>();
    private readonly HashSet<Block> currentSelectedFocusedBlocks = new HashSet<Block>();
    private readonly HashSet<Block> currentSelectionFarmlandFocusGroup = new HashSet<Block>();
    private Block currentTrainBlock;
    private static bool IsAvailableMapObjectFocusTarget(IMapObjectTarget target, Block fallback)
        => target is Train train && train.gameObject.activeInHierarchy;
    private bool AppendMapObjectFocusBlocks(IMapObjectTarget target, Block fallback, List<Block> blocks)
    {
        if (currentTrainBlock == null) return false;
        blocks.Add(currentTrainBlock);
        return true;
    }
    private void RefreshSelectedFocusMarkers()
    {
        foreach (Block block in currentSelectedFocusedBlocks)
        {
            block.SetSelectionFocusVisible(true);
            SetTrainFocusVisible((Train)currentSelectedMapObject, block, FocusMarkerKind.Selection);
        }
    }
    private void RefreshMouseFocusMarkers()
    {
        foreach (Block block in currentMouseFocusedBlocks)
        {
            block.SetMouseFocusVisible(true);
            SetTrainFocusVisible((Train)currentMouseFocusedMapObject, block, FocusMarkerKind.Mouse);
        }
    }

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

        foreach (Vector2 axis in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        foreach (FocusMarkerKind kind in Enum.GetValues<FocusMarkerKind>())
        {
            Train train = LongCarChecks.LongCar();
            train.RuntimePlacementSequence = 1;
            var block = new Block { Coordinate = new Vector2Int(4, -5) };
            train.TestPlacementCoordinate = block.Coordinate;
            Vector3 direction = new Vector3(axis.x, 0, axis.y);
            train.transform.rotation = PreviewRotation.LookRotation(direction, Vector3.up);
            Vector3 expected = block.WorldPosition + direction * .5f;
            foreach (float offset in new[] { -.49f, -.2f, 0f, .2f, .49f })
            {
                train.transform.position = block.WorldPosition + direction * offset;
                SetTrainFocusVisible(train, block, kind);
                Check(Vector3.Distance(markerCenter, expected) < .0001f,
                    "Movement within the same occupied cell must not slide the marker");
            }
            train.TestPlacementCoordinate += new Vector2Int((int)axis.x, (int)axis.y);
            train.transform.position = block.WorldPosition + direction * .51f;
            SetTrainFocusVisible(train, block, kind);
            Check(Vector3.Distance(markerCenter, expected + direction) < .0001f,
                "Crossing a cell boundary must move the marker by exactly one whole cell");
        }

        var controller = new PlayerController();
        Train moving = LongCarChecks.LongCar();
        moving.RuntimePlacementSequence = 1;
        Block previous = null;
        for (int step = 0; step < 12; step++)
        {
            // Repeated sub-cell movement, cell crossings and curved headings.
            float angle = step * 15f * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            moving.transform.position = new Vector3(step * .3f, 20, -step * .2f);
            moving.transform.rotation = PreviewRotation.LookRotation(forward, Vector3.up);
            if (step % 3 == 0)
            {
                previous = controller.currentTrainBlock;
                controller.currentTrainBlock = new Block { Coordinate = new Vector2Int(step, -step) };
            }
            moving.TestPlacementCoordinate = controller.currentTrainBlock.Coordinate;
            controller.RefreshSelectedMapObjectFocus(moving, null);
            Vector3 expected = controller.currentTrainBlock.WorldPosition;
            Check(Math.Abs(markerCenter.x - expected.x) <= .5001f && Math.Abs(markerCenter.z - expected.z) <= .5001f,
                "Selected marker must stay within its current grid footprint");
            Check(Math.Abs(markerCenter.x * 2 - Mathf.Round(markerCenter.x * 2)) < .0001f
                && Math.Abs(markerCenter.z * 2 - Mathf.Round(markerCenter.z * 2)) < .0001f,
                "Curved motion must keep the footprint aligned to floor cells");
            Check(controller.currentSelectedFocusedBlocks.Count == 1 && controller.currentSelectedFocusedBlocks.Contains(controller.currentTrainBlock), "Selection must rebind to the current floor block");
            Check(previous == null || !previous.SelectionVisible, "Old floor selection must be hidden after a cell crossing");
        }
        moving.gameObject.activeInHierarchy = false;
        controller.RefreshSelectedMapObjectFocus(moving, null);
        Check(controller.currentSelectedMapObject == null && controller.currentSelectedFocusedBlocks.Count == 0 && !controller.currentTrainBlock.SelectionVisible, "Removed train must clear selection");

        moving.gameObject.activeInHierarchy = true;
        var visited = new List<Block>();
        for (int click = 0; click < 2; click++)
        {
            // Mounted mouse focus is pinned by a click, not rebuilt by hover.
            controller.SetMouseFocusedBlocks(new List<Block> { controller.currentTrainBlock }, moving);
            controller.RefreshSelectedMapObjectFocus(moving, null);
            for (int step = 0; step < 5; step++)
            {
                Block old = controller.currentTrainBlock;
                visited.Add(old);
                controller.currentTrainBlock = new Block { Coordinate = new Vector2Int(click * 10 + step, 3) };
                moving.TestPlacementCoordinate = controller.currentTrainBlock.Coordinate;
                moving.transform.position += new Vector3(.3f, 0, .2f);
                controller.RefreshSelectedMapObjectFocus(step < 2 ? moving : null, null);
                controller.RefreshMovingTrainMouseFocus();
                Check(!old.MouseVisible && !old.SelectionVisible, "Repeat click and deselection must not strand either marker on the old floor");
                Check(controller.currentMouseFocusedBlocks.Count == 1 && controller.currentMouseFocusedBlocks.Contains(controller.currentTrainBlock), "Pinned mouse focus must rebind without another click");
                Check(Math.Abs(markerCenter.x - controller.currentTrainBlock.WorldPosition.x) <= .5001f
                    && Math.Abs(markerCenter.z - controller.currentTrainBlock.WorldPosition.z) <= .5001f,
                    "Mouse focus must follow current grid cells after selection is cleared");
            }
        }
        moving.gameObject.activeInHierarchy = false;
        controller.RefreshMovingTrainMouseFocus();
        Check(controller.currentMouseFocusedMapObject == null && !controller.currentTrainBlock.MouseVisible, "Removed train must clear pinned mouse focus");
        foreach (Block old in visited)
            Check(!old.MouseVisible && !old.SelectionVisible, "No old floor markers may remain after repeated clicks");
        Console.WriteLine($"Train floor focus checks passed: {checks} checks");
    }
}
