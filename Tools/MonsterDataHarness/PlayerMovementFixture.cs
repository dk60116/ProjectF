using UnityEngine;

// Only the unrelated scene/PhysX/infrastructure ports are doubles. The combined player sweep is production code.
public partial class PlayerController
{
    private readonly Rigidbody cachedRigidbody;
    private readonly CapsuleCollider cachedCapsuleCollider;
    internal int Mask = ~0;
    internal float PhysicsDistance = -1f;
    internal float Height;
    internal PlayerController()
    {
        var root = new GameObject();
        cachedRigidbody = root.AddComponent<Rigidbody>();
        cachedCapsuleCollider = root.AddComponent<CapsuleCollider>();
        cachedCapsuleCollider.enabled = true;
    }
    private bool TryGetPhysicsBlockingSweepHit(Vector3 offset, Vector3 direction, float distance, bool ignore, out RaycastHit hit)
    {
        hit = new RaycastHit { distance = PhysicsDistance, normal = new Vector3(-1, 0, 0) };
        return PhysicsDistance >= 0 && PhysicsDistance <= distance;
    }
    private Vector2 GetPlayerCollisionCenterXZ(Vector3 position) => new Vector2(position.x, position.z);
    private float GetPlayerCollisionRadius() => .2f;
    private int GetPlayerMovementCollisionMask() => Mask;
    private void CacheDefaultCapsuleColliderCenter() { }
    private void GetPlayerMovementCapsuleWorldGeometry(out Vector3 first, out Vector3 second, out float radius)
    { radius = .2f; first = new Vector3(0, Height + .8f, 0); second = new Vector3(0, Height + .2f, 0); }
    internal bool Sweep(Vector3 start, Vector3 direction, float distance, out RaycastHit hit)
    {
        cachedRigidbody.position = start;
        return TryGetBlockingSweepHit(default, direction, distance, false, out hit);
    }
}

namespace UnityEngine
{
    public struct RaycastHit { public float distance; public Vector3 normal; }
}

public sealed partial class ConveyorWorld
{
    public bool TryGetBelt2FAtCoordinate(Vector2Int cell, out ConveyorRuntimeRecord belt) { belt = null; return false; }
}
public sealed partial class ConveyorRuntimeRecord
{
    public bool PlacementPresentationSuppressed;
    public bool TryGetOutputDirection(out Vector2Int flow) { flow = default; return false; }
    public void GetPlayerSideBarrierEndpoints(out Vector3 start, out Vector3 end) { start = end = default; }
    public bool Covers(Vector2Int cell) => false;
}
public sealed partial class PipeRuntimeRecord
{
    public bool TrySweepPlayer(Vector2Int cell, Vector2 start, Vector2 direction, float distance, float radius,
        float minY, float maxY, int mask, out float hitDistance, out Vector2 normal)
    { hitDistance = distance; normal = default; return false; }
}
public sealed class ConvayorBelt2F
{
    public readonly Transform transform = new Transform();
    public static bool TryFindCoveringBelt(Vector2Int cell, out ConvayorBelt2F belt) { belt = null; return false; }
    public bool TryGetOutputDirection(Quaternion rotation, out Vector2Int flow) { flow = default; return false; }
    public void GetPlayerSideBarrierEndpoints(out Vector3 start, out Vector3 end) { start = end = default; }
    public bool CoversCoordinate(Vector2Int cell) => false;
}
namespace ProjectF.Conveyors
{
    public static class ConveyorSideBarrier
    {
        public static bool Sweep(Vector2 start, Vector2 direction, float distance, Vector2 center, Vector2 axis,
            Vector2 outward, float halfLength, float radius, out float hitDistance, out Vector2 normal)
        { hitDistance = distance; normal = default; return false; }
    }
}
