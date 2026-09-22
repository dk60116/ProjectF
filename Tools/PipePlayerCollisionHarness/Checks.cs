using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PipeRuntimeRecord
{
    private readonly PlayerCollisionPart[] playerCollisionParts;
    internal bool PlacementPresentationSuppressed;
    public PipeRuntimeRecord(Vector2Int coordinate, params Bounds[] boxes)
    {
        playerCollisionParts = new PlayerCollisionPart[boxes.Length];
        for (int i = 0; i < boxes.Length; i++) playerCollisionParts[i] = new(coordinate, boxes[i], 0);
    }
    public PipeRuntimeRecord(Vector2Int coordinate, (Bounds bounds, int layer)[] parts)
    {
        playerCollisionParts = new PlayerCollisionPart[parts.Length];
        for (int i = 0; i < parts.Length; i++) playerCollisionParts[i] = new(coordinate, parts[i].bounds, parts[i].layer);
    }
    public PipeRuntimeRecord(Vector2Int first, Bounds a, Vector2Int second, Bounds b)
    { playerCollisionParts = new[] { new PlayerCollisionPart(first, a, 0), new PlayerCollisionPart(second, b, 0) }; }
}
public class PipeWorld
{
    public static PipeWorld Current = new();
    public readonly Dictionary<Vector2Int, PipeRuntimeRecord> Pipes = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out PipeRuntimeRecord pipe) => Pipes.TryGetValue(coordinate, out pipe);
}
public class ConveyorWorld
{
    public static ConveyorWorld Current = new();
    public readonly Dictionary<Vector2Int, ConveyorRuntimeRecord> Belts = new();
    public bool TryGetAtCoordinate(Vector2Int coordinate, out ConveyorRuntimeRecord belt) => Belts.TryGetValue(coordinate, out belt);
    public bool TryGetBelt2FAtCoordinate(Vector2Int coordinate, out ConveyorRuntimeRecord belt) { belt = null; return false; }
}
public partial class ConveyorRuntimeRecord
{
    private const float Belt2FPathLowHeight = .13f;
    public sealed class PrototypeStub { public sealed class ObjectStub { public int layer; } public readonly ObjectStub gameObject = new(); }
    public readonly PrototypeStub Prototype = new();
    public bool PlacementPresentationSuppressed;
    public bool IsCorner = true;
    public Vector3 WorldPosition, WorldScale = Vector3.one;
    public Vector2Int AnchorCoordinate;
    public Vector2Int Input, Output;
    public bool TryGetInputDirection(out Vector2Int direction) { direction = Input; return true; }
    public bool TryGetOutputDirection(out Vector2Int direction) { direction = Output; return true; }
    public void GetPlayerSideBarrierEndpoints(out Vector3 a, out Vector3 b) { a = b = default; }
    public bool Covers(Vector2Int coordinate) => coordinate == AnchorCoordinate;
}
public class ConvayorBelt2F
{
    public class TransformStub { public Quaternion rotation; }
    public readonly TransformStub transform = new();
    public static bool TryFindCoveringBelt(Vector2Int coordinate, out ConvayorBelt2F belt) { belt = null; return false; }
    public bool TryGetOutputDirection(Quaternion rotation, out Vector2Int direction) { direction = default; return false; }
    public void GetPlayerSideBarrierEndpoints(out Vector3 a, out Vector3 b) { a = b = default; }
    public bool CoversCoordinate(Vector2Int coordinate) => false;
}
public partial class PlayerController
{
    private sealed class Body { public Vector3 position; }
    private sealed class Capsule { public bool enabled = true; }
    private readonly Body cachedRigidbody = new();
    private readonly Capsule cachedCapsuleCollider = new();
    public bool ColliderEnabled { set => cachedCapsuleCollider.enabled = value; }
    public int Mask = ~0;
    public float Height;
    public float PhysicsHit = -1f;
    private bool TryGetPhysicsBlockingSweepHit(Vector3 offset, Vector3 direction, float distance, bool ignore, out RaycastHit hit)
    { hit = new RaycastHit { distance = PhysicsHit, normal = Vector3.left }; return PhysicsHit >= 0 && PhysicsHit <= distance; }
    private Vector2 GetPlayerCollisionCenterXZ(Vector3 position) => new(position.x, position.z);
    private float GetPlayerCollisionRadius() => .25f;
    private int GetPlayerMovementCollisionMask() => Mask;
    private void CacheDefaultCapsuleColliderCenter() {}
    private void GetPlayerMovementCapsuleWorldGeometry(out Vector3 first, out Vector3 second, out float radius)
    { radius = .25f; first = new(0, Height + 1.5f, 0); second = new(0, Height + .25f, 0); }
    public bool Sweep(Vector2 start, Vector2 direction, float distance, out RaycastHit hit)
    {
        cachedRigidbody.position = new(start.x, 0, start.y);
        return TryGetBlockingSweepHit(default, new(direction.x, 0, direction.y), distance, false, out hit);
    }
}
public static partial class Checks
{
    private static int passed;
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; }
    private static void Equal(float a, float b, string label) => Check(Math.Abs(a-b) < .0001f, label + $": {a} != {b}");
    private static void CheckCornerBelts(PlayerController player)
    {
        PipeWorld.Current.Pipes.Clear();
        foreach (Vector2Int input in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
        foreach (int turn in new[] { -1, 1 })
        {
            Vector2Int output = new Vector2Int(-input.y, input.x) * turn;
            var corner = new ConveyorRuntimeRecord { Input = input, Output = output };
            ConveyorWorld.Current.Belts[Vector2Int.zero] = corner;
            foreach (Vector2Int port in new[] { input, output })
            {
                Vector2 open = new Vector2(port.x, port.y);
                Vector2 closed = -open;
                Check(player.Sweep(closed * 2f, -closed, 4f, out var hit), "both corner variants block each closed side at high speed in all rotations");
                Equal(hit.distance, 1.25f, "closed edge includes player radius");
                Check(Vector2.Dot(new Vector2(hit.normal.x, hit.normal.z), closed) > .999f, "corner edge normal supports sliding");
                Check(!player.Sweep(open * 1.2f, -open, 1f, out _), "both corner ports stay open");
                Check(!player.Sweep(Vector2.zero, closed, 1.2f, out _), "player on belt can step off");
                Check(!player.Sweep(closed * .75f, new Vector2(-closed.y, closed.x), .2f, out _), "parallel corner wall movement remains free");
                Check(!player.Sweep(closed * .6f, closed, .1f, out _), "corner overlap permits escape");
                Check(player.Sweep(closed * .6f, -closed, .1f, out _), "corner overlap blocks deeper entry");
            }
            Vector2 diagonal = new Vector2(input.x + output.x, input.y + output.y).normalized;
            Check(player.Sweep(-diagonal * 2, diagonal, 3, out _), "diagonal cannot bypass closed corner");
            player.Height = 1f;
            Check(!player.Sweep(new Vector2(-input.x,-input.y)*2, new Vector2(input.x,input.y), 4, out _), "bridge above corner stays walkable");
            player.Height = 0;
            corner.PlacementPresentationSuppressed = true;
            Check(!player.Sweep(new Vector2(-input.x,-input.y)*2, new Vector2(input.x,input.y), 4, out _), "suppressed corner does not block");
            corner.PlacementPresentationSuppressed = false;
            player.Mask = 0;
            Check(!player.Sweep(new Vector2(-input.x,-input.y)*2, new Vector2(input.x,input.y), 4, out _), "corner respects collision mask");
            player.Mask = ~0;
            corner.IsCorner = false;
            Check(!player.Sweep(new Vector2(-input.x,-input.y)*2, new Vector2(input.x,input.y), 4, out _), "ordinary straight belt stays walkable");
            corner.IsCorner = true;
            ConveyorWorld.Current.Belts.Clear();
            Check(!player.Sweep(new Vector2(-input.x,-input.y)*2, new Vector2(input.x,input.y), 4, out _), "removed corner stops blocking");
        }
    }

    public static void Main(string[] args)
    {
        var zero = Vector2Int.zero;
        var player = new PlayerController();
        foreach (bool horizontal in new[] { false, true })
        foreach (int sign in new[] { -1, 1 })
        {
            PipeWorld.Current.Pipes.Clear();
            var box = new Bounds(new(0,.4f,0), horizontal ? new(1,.3f,.35f) : new(.35f,.3f,1));
            var pipe = new PipeRuntimeRecord(zero, box);
            PipeWorld.Current.Pipes[zero] = pipe;
            Vector2 axis = horizontal ? Vector2.up : Vector2.right;
            Vector2 start = axis * (2f * sign);
            Check(player.Sweep(start, -axis * sign, 4f, out var hit), "data-only pipe blocks fast crossing in each orientation");
            Equal(hit.distance, 2f - .175f - .25f, "authored pipe thickness and capsule radius determine hit");
            Check(Vector2.Dot(new(hit.normal.x, hit.normal.z), axis * sign) > .999f, "collision normal points outward");
            Check(!player.Sweep(axis * (.175f+.25f), new(-axis.y, axis.x), .25f, out _), "tangent movement can slide along pipe");
            Check(!player.Sweep(axis * .3f, axis, .5f, out _), "overlap permits outward escape");
            Check(player.Sweep(axis * .3f, -axis, .5f, out _), "overlap cannot move deeper through pipe");
            player.Height = 1f;
            Check(!player.Sweep(start, -axis*sign, 4f, out _), "raised conveyor path clears pipe below it");
            player.Height = 0;
            player.Mask = 0;
            Check(!player.Sweep(start, -axis*sign, 4f, out _), "ignored physics layer stays ignored");
            player.Mask = ~0;
            pipe.PlacementPresentationSuppressed = true;
            Check(!player.Sweep(start, -axis*sign, 4f, out _), "suppressed placement has no collision");
            pipe.PlacementPresentationSuppressed = false;
            player.PhysicsHit = .1f;
            Check(player.Sweep(start, -axis*sign, 4f, out hit), "existing physics hit remains active");
            Equal(hit.distance, .1f, "nearest physics obstacle takes precedence");
            player.PhysicsHit = -1;
            PipeWorld.Current.Pipes.Clear();
            Check(!player.Sweep(start, -axis*sign, 4f, out _), "removed pipe stops blocking immediately");
        }
        var elbow = new PipeRuntimeRecord(zero,
            new Bounds(new(0,.4f,-.17f), new(.35f,1,.7f)),
            new Bounds(new(-.17f,.4f,0), new(.7f,1,.35f)));
        PipeWorld.Current.Pipes[zero] = elbow;
        Check(!player.Sweep(new(.45f,.45f), Vector2.up, .1f, out _), "elbow empty quadrant stays open");
        Check(player.Sweep(new(1,0), Vector2.left, 2f, out _), "elbow solid arm blocks");
        foreach (bool cross in new[] { false, true })
        {
            PipeWorld.Current.Pipes[zero] = new PipeRuntimeRecord(zero,
                new Bounds(new(0,.4f,0), new(1,1,.3f)),
                new Bounds(new(0,.4f,cross ? 0 : -.2f), new(.3f,1,cross ? 1 : .7f)));
            Check(player.Sweep(new(-2,0), Vector2.right, 4f, out _), "T/cross horizontal arm blocks");
            Check(player.Sweep(new(0,-2), Vector2.up, 4f, out _), "T/cross vertical arm blocks");
            Check(!player.Sweep(new(.45f,.45f), Vector2.up, .1f, out _), "T/cross empty quadrant stays open");
        }
        player.ColliderEnabled = false;
        Check(!player.Sweep(new(-2,0), Vector2.right, 4f, out _), "disabled player capsule stays nonblocking");
        player.ColliderEnabled = true;
        PipeWorld.Current.Pipes.Clear();
        var far = new Vector2Int(0,6);
        var underground = new PipeRuntimeRecord(zero, new Bounds(new(0,.5f,0), new(.4f,1,.8f)),
            far, new Bounds(new(0,.5f,6), new(.4f,1,.8f)));
        PipeWorld.Current.Pipes[zero] = PipeWorld.Current.Pipes[far] = underground;
        Check(player.Sweep(new(-2,0), Vector2.right, 4f, out _), "underground first mouth blocks");
        Check(player.Sweep(new(-2,6), Vector2.right, 4f, out _), "underground second mouth blocks");
        Check(!player.Sweep(new(-2,3), Vector2.right, 4f, out _), "underground hidden span remains walkable");
        var square = new Bounds(Vector3.zero, Vector3.one);
        Check(!PipePlayerCollision.Sweep(new(.7f,.7f), Vector2.right, 1f, .25f, square, out _, out _), "rounded capsule corner avoids inflated-square false hit");
        Check(PipePlayerCollision.Sweep(new(2,2), new Vector2(-1,-1).normalized, 4f, .25f, square, out var cornerDistance, out _), "diagonal movement hits rounded corner");
        Equal(cornerDistance, (float)Math.Sqrt(4.5) - .25f, "corner sweep distance");
        CheckCornerBelts(player);
        CheckAuthoredCorner(args[0], player);
        CheckProductionMachineColliders(args[0]);
        Console.WriteLine($"Pipe/corner belt player collision: {passed} passed. Production movement sweep and pipe geometry; Unity not launched.");
    }
}
