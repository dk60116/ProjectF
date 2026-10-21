using System;
using System.Collections.Generic;

// Only transform, renderer and lifecycle boundaries are doubled.
public struct Vector2
{
    public float x, y;
    public Vector2(float x, float y) { this.x = x; this.y = y; }
    public static Vector2 one => new(1, 1);
}
public struct Vector2Int
{
    public int x, y;
    public Vector2Int(int x, int y) { this.x = x; this.y = y; }
}
public struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
}
public struct Quaternion
{
    private float yaw;
    public static Quaternion Euler(float x, float y, float z) => new() { yaw = y * MathF.PI / 180f };
    public static Vector3 operator *(Quaternion q, Vector3 p) => new(
        MathF.Cos(q.yaw) * p.x + MathF.Sin(q.yaw) * p.z, p.y,
        -MathF.Sin(q.yaw) * p.x + MathF.Cos(q.yaw) * p.z);
}
public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static float Min(float a, float b, float c) => Math.Min(a, Math.Min(b, c));
    public static int RoundToInt(float a) => (int)MathF.Round(a);
}
public struct Color { }
public sealed class GameObject
{
    public bool activeSelf = true;
    public void SetActive(bool value) { activeSelf = value; }
}
public sealed class Transform
{
    public Vector3 position, eulerAngles;
    public Transform parent;
    public Vector3 TransformPoint(Vector3 point) => position + Quaternion.Euler(0, eulerAngles.y, 0) * point;
}
public sealed class LineRenderer
{
    public readonly GameObject gameObject = new();
    public bool enabled;
    public int positionCount;
    public readonly Vector3[] Points = new Vector3[3];
    public void SetPosition(int index, Vector3 point) => Points[index] = point;
}
public partial class MapFocus
{
    private const int AreaLineCount = 4;
    private const float SingleMarkerCornerVisibleLengthRatio = 131f / 512f;
    private float areaLineWidth = .035f, areaCornerLength = .32f;
    private readonly LineRenderer[] areaLines = { new(), new(), new(), new() };
    private Vector3 defaultLocalPosition;
    private Color focusColor;
    private static readonly Color DefaultFocusColor = new();
    public readonly Transform transform = new();
    private static Color NormalizeFocusColor(Color value, Color fallback) => value;
    private Vector2 GetSingleMarkerWorldSize() => Vector2.one;
    private void ConfigureLineAppearance(LineRenderer line) { }
    private void CacheDefaultTransform() { }
    private void HideShapeLines() { }
    private void ResetTransformToDefault() { }
    private void ApplyVisibility(bool visible, Color color, bool sprite) { }
    private void EnsureAreaLines() { }
    public Vector3 Corner(int index) => areaLines[index].Points[1];
}
public sealed class Train
{
    public struct MapObjectStatus { public byte mapSizeX, mapSizeY; }
    public MapObjectStatus Status;
    public Vector2Int PlacementCenterCell, RuntimeAnchor = new(-13, 7);
    public bool HasRuntimePlacement = true;
    public readonly Transform transform = new();
    public bool TryGetPlacementRuntime(out Vector2Int coordinate, out int turns)
    {
        coordinate = RuntimeAnchor; turns = 0; return HasRuntimePlacement;
    }
}
public partial class Block
{
    public Vector2Int Coordinate;
    public Vector3 WorldPosition;
    private bool interactionFocusVisible, mouseFocusVisible, selectionFocusVisible;
    private bool interactionFocusUsesArea, mouseFocusUsesArea, selectionFocusUsesArea;
    private bool interactionFocusUsesShape, mouseFocusUsesShape, selectionFocusUsesShape;
    private readonly List<Vector2Int> interactionFocusShapeCoordinates = new();
    private readonly List<Vector2Int> mouseFocusShapeCoordinates = new();
    private readonly List<Vector2Int> selectionFocusShapeCoordinates = new();
    private Vector3 interactionFocusAreaCenter, mouseFocusAreaCenter, selectionFocusAreaCenter;
    private Vector2 interactionFocusAreaSize, mouseFocusAreaSize, selectionFocusAreaSize;
    public readonly MapFocus focus = new();
    public Vector3 Center(int kind) => kind == 0 ? interactionFocusAreaCenter : kind == 1 ? mouseFocusAreaCenter : selectionFocusAreaCenter;
    public Vector2 Size(int kind) => kind == 0 ? interactionFocusAreaSize : kind == 1 ? mouseFocusAreaSize : selectionFocusAreaSize;
    public int Kind;
    private void RefreshFocusMarker() => focus.SetAreaVisible(true, default, Center(Kind), Size(Kind));
}
public partial class PlayerController
{
    private enum FocusMarkerKind { Interaction, Mouse, Selection }
    public static void Focus(Train train, Block block, int kind)
    {
        if (block != null) block.Kind = kind;
        SetTrainFocusVisible(train, block, (FocusMarkerKind)kind);
    }
}
public static class Checks
{
    private static int count;
    private static void Check(bool pass, string message)
    {
        count++;
        if (!pass) throw new Exception(message);
    }
    private static void Near(float expected, float actual, string message) => Check(Math.Abs(expected - actual) < .0001f, message);
    private static void Near(Vector3 expected, Vector3 actual, string message)
    {
        Near(expected.x, actual.x, message + " x"); Near(expected.y, actual.y, message + " y"); Near(expected.z, actual.z, message + " z");
    }
    private static void Rectangle(Block block, int kind, Vector3 center, Vector2 size)
    {
        Near(center, block.Center(kind), "floor center");
        Near(size.x, block.Size(kind).x, "grid width");
        Near(size.y, block.Size(kind).y, "grid length");
        float inset = .035f * .5f;
        for (int i = 0; i < 4; i++)
        {
            var expected = new Vector3(center.x + (i % 2 == 0 ? -1 : 1) * (size.x * .5f - inset), center.y,
                center.z + (i < 2 ? -1 : 1) * (size.y * .5f - inset));
            Near(expected, block.focus.Corner(i), "axis-aligned floor corner");
        }
    }
    public static void Main()
    {
        var train = new Train();
        var block = new Block();
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (byte width in new byte[] { 1, 2 })
        foreach (byte length in new byte[] { 1, 2 })
        for (int anchorX = 0; anchorX < width; anchorX++)
        for (int anchorZ = 0; anchorZ < length; anchorZ++)
        for (int yaw = -360; yaw <= 360; yaw += 15)
        {
            train.Status = new() { mapSizeX = width, mapSizeY = length };
            train.PlacementCenterCell = new(anchorX, anchorZ);
            train.transform.position = new(99.25f, 10, -70.75f);
            train.transform.eulerAngles = new(0, yaw, 0);
            // Focus may be attached to a non-anchor occupied cell.
            block.Coordinate = new(-12, 8);
            block.WorldPosition = new(88, .25f, 208);
            int turns = ((int)MathF.Round(yaw / 90f) % 4 + 4) % 4;
            float x = (width - 1) * .5f - anchorX;
            float z = (length - 1) * .5f - anchorZ;
            var offset = turns switch
            {
                0 => new Vector3(x, 0, z), 1 => new Vector3(z, 0, -x),
                2 => new Vector3(-x, 0, -z), _ => new Vector3(-z, 0, x)
            };
            var center = new Vector3(87, .25f, 207) + offset;
            var size = turns % 2 == 0 ? new Vector2(width, length) : new Vector2(length, width);
            PlayerController.Focus(train, block, kind);
            Rectangle(block, kind, center, size);
            train.transform.position = new(1000.4f, 30, -1000.6f);
            PlayerController.Focus(train, block, kind);
            Rectangle(block, kind, center, size);
            block.Coordinate = train.RuntimeAnchor;
            block.WorldPosition = new(87, .25f, 207);
            PlayerController.Focus(train, block, kind);
            Rectangle(block, kind, center, size);
        }
        train.HasRuntimePlacement = false;
        train.Status = new() { mapSizeX = 1, mapSizeY = 2 };
        train.PlacementCenterCell = new(0, 0);
        train.transform.eulerAngles = new(0, 90, 0);
        block.Coordinate = new(3, 4); block.WorldPosition = new(3, 0, 4);
        PlayerController.Focus(train, block, 0);
        Rectangle(block, 0, new(3.5f, 0, 4), new(2, 1));
        PlayerController.Focus(train, null, 0);
        for (int i = 0; i < 100; i++) PlayerController.Focus(train, block, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) PlayerController.Focus(train, block, 0);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warm floor focus refresh allocates zero bytes");
        Console.WriteLine($"PASS Train floor focus: {count} checks; grid anchors, all focus kinds, heading snapping, no model following, zero warm GC");
    }
}
