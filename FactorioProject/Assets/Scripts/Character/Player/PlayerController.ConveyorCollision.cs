using ProjectF.Conveyors;
using UnityEngine;

public partial class PlayerController
{
    private bool TryGetBlockingSweepHit(Vector3 originOffset, Vector3 direction,
        float distance, bool ignoreLiveAnimals, out RaycastHit blockingHit)
    {
        bool blocked = TryGetPhysicsBlockingSweepHit(originOffset, direction, distance,
            ignoreLiveAnimals, out blockingHit);
        if (cachedRigidbody == null || distance <= 0f)
            return blocked;

        Vector2 start = GetPlayerCollisionCenterXZ(cachedRigidbody.position + originOffset);
        Vector2 flatDirection = new Vector2(direction.x, direction.z).normalized;
        Vector2 end = start + flatDirection * distance;
        float radius = GetPlayerCollisionRadius();
        int minX = Mathf.FloorToInt(Mathf.Min(start.x, end.x) - radius + 0.5f);
        int maxX = Mathf.FloorToInt(Mathf.Max(start.x, end.x) + radius + 0.5f);
        int minY = Mathf.FloorToInt(Mathf.Min(start.y, end.y) - radius + 0.5f);
        int maxY = Mathf.FloorToInt(Mathf.Max(start.y, end.y) + radius + 0.5f);
        float nearestDistance = blocked ? blockingHit.distance : distance;

        // Coverage remains available while the belt's render root is suspended.
        // Only the raised span is walled; the low input/output landings stay open.
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, y);
                if (!ConvayorBelt2F.TryFindCoveringBelt(coordinate, out ConvayorBelt2F belt)
                    || !belt.TryGetOutputDirection(belt.transform.rotation, out Vector2Int flow)
                    || flow == Vector2Int.zero)
                    continue;

                Vector2 axis = new Vector2(flow.x, flow.y);
                belt.GetPlayerSideBarrierEndpoints(out Vector3 barrierStart, out Vector3 barrierEnd);
                Vector2 barrierCenter = new Vector2(
                    (barrierStart.x + barrierEnd.x) * 0.5f,
                    (barrierStart.z + barrierEnd.z) * 0.5f);
                float halfLength = Mathf.Abs(Vector2.Dot(
                    new Vector2(barrierEnd.x - barrierStart.x, barrierEnd.z - barrierStart.z), axis)) * 0.5f;
                Vector2Int side = new Vector2Int(-flow.y, flow.x);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector2Int outward = side * sign;
                    if (belt.CoversCoordinate(coordinate + outward))
                        continue;

                    Vector2 wallCenter = new Vector2(x + outward.x * 0.5f, y + outward.y * 0.5f);
                    wallCenter += axis * Vector2.Dot(barrierCenter - wallCenter, axis);
                    if (!ConveyorSideBarrier.Sweep(start, flatDirection, nearestDistance,
                            wallCenter, axis, halfLength, radius, out float hitDistance, out Vector2 normal))
                        continue;

                    nearestDistance = hitDistance;
                    blockingHit = new RaycastHit
                    {
                        distance = hitDistance,
                        normal = new Vector3(normal.x, 0f, normal.y)
                    };
                    blocked = true;
                }
            }
        }

        return blocked;
    }
}
