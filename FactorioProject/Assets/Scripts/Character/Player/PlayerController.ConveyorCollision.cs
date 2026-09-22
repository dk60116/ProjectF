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
        ConveyorWorld conveyorWorld = ConveyorWorld.Current;
        PipeWorld pipeWorld = PipeWorld.Current;
        CacheDefaultCapsuleColliderCenter();
        bool hasCapsule = cachedCapsuleCollider != null && cachedCapsuleCollider.enabled;
        float playerMinY = 0f, playerMaxY = 0f;
        if (hasCapsule)
        {
            GetPlayerMovementCapsuleWorldGeometry(out Vector3 first, out Vector3 second, out float capsuleRadius);
            playerMinY = Mathf.Min(first.y, second.y) + originOffset.y - capsuleRadius;
            playerMaxY = Mathf.Max(first.y, second.y) + originOffset.y + capsuleRadius;
        }
        int collisionMask = GetPlayerMovementCollisionMask();

        // Coverage remains available while the belt's render root is suspended.
        // Raised sides block entry only; stepping off and low landings stay open.
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, y);
                if (hasCapsule && pipeWorld != null
                    && pipeWorld.TryGetAtCoordinate(coordinate, out PipeRuntimeRecord pipe)
                    && pipe.TrySweepPlayer(coordinate, start, flatDirection, nearestDistance, radius,
                        playerMinY, playerMaxY, collisionMask, out float pipeDistance, out Vector2 pipeNormal))
                {
                    nearestDistance = pipeDistance;
                    blockingHit = new RaycastHit
                    {
                        distance = pipeDistance,
                        normal = new Vector3(pipeNormal.x, 0f, pipeNormal.y)
                    };
                    blocked = true;
                }

                if (hasCapsule && conveyorWorld != null
                    && conveyorWorld.TryGetAtCoordinate(coordinate, out ConveyorRuntimeRecord corner)
                    && corner.TrySweepCornerPlayer(coordinate, start, flatDirection, nearestDistance, radius,
                        playerMinY, playerMaxY, collisionMask, out float cornerDistance, out Vector2 cornerNormal))
                {
                    nearestDistance = cornerDistance;
                    blockingHit = new RaycastHit
                    {
                        distance = cornerDistance,
                        normal = new Vector3(cornerNormal.x, 0f, cornerNormal.y)
                    };
                    blocked = true;
                }

                ConveyorRuntimeRecord dataOnlyBelt = null;
                ConvayorBelt2F sceneBelt = null;
                Vector2Int flow;
                Vector3 barrierStart;
                Vector3 barrierEnd;
                if (conveyorWorld != null
                    && conveyorWorld.TryGetBelt2FAtCoordinate(coordinate, out dataOnlyBelt))
                {
                    if (dataOnlyBelt.PlacementPresentationSuppressed
                        || !dataOnlyBelt.TryGetOutputDirection(out flow)
                        || flow == Vector2Int.zero)
                    {
                        continue;
                    }

                    dataOnlyBelt.GetPlayerSideBarrierEndpoints(out barrierStart, out barrierEnd);
                }
                else if (ConvayorBelt2F.TryFindCoveringBelt(coordinate, out sceneBelt))
                {
                    if (!sceneBelt.TryGetOutputDirection(sceneBelt.transform.rotation, out flow)
                        || flow == Vector2Int.zero)
                    {
                        continue;
                    }

                    sceneBelt.GetPlayerSideBarrierEndpoints(out barrierStart, out barrierEnd);
                }
                else
                {
                    continue;
                }

                Vector2 axis = new Vector2(flow.x, flow.y);
                Vector2 barrierCenter = new Vector2(
                    (barrierStart.x + barrierEnd.x) * 0.5f,
                    (barrierStart.z + barrierEnd.z) * 0.5f);
                float halfLength = Mathf.Abs(Vector2.Dot(
                    new Vector2(barrierEnd.x - barrierStart.x, barrierEnd.z - barrierStart.z), axis)) * 0.5f;
                Vector2Int side = new Vector2Int(-flow.y, flow.x);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector2Int outward = side * sign;
                    if (dataOnlyBelt != null
                        ? dataOnlyBelt.Covers(coordinate + outward)
                        : sceneBelt.CoversCoordinate(coordinate + outward))
                        continue;

                    Vector2 wallCenter = new Vector2(x + outward.x * 0.5f, y + outward.y * 0.5f);
                    wallCenter += axis * Vector2.Dot(barrierCenter - wallCenter, axis);
                    if (!ConveyorSideBarrier.Sweep(start, flatDirection, nearestDistance,
                            wallCenter, axis, new Vector2(outward.x, outward.y),
                            halfLength, radius, out float hitDistance, out Vector2 normal))
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
