using UnityEngine;

public class Ship : Vehicle
{
    private const float MovementEpsilon = 0.0001f;
    private const float MovementStepDistance = 0.08f;
    private const float RotationStepDegrees = 5f;
    private const int MaxMovementSubsteps = 64;
    private const float DismountMaxGap = 0.5f;

    [SerializeField, Min(1f)]
    private float steeringDegreesPerSecond = 120f;
    [SerializeField, Range(0f, 0.5f)]
    private float inputDeadZone = 0.1f;
    [SerializeField]
    private BoxCollider drivingCollider;
    [SerializeField]
    private LayerMask obstacleLayers = 393;
    [SerializeField, Min(0f)]
    private float collisionSkinWidth = 0.03f;

    private readonly Collider[] obstacleBuffer = new Collider[32];
    private readonly float[] waterWeightBuffer = new float[6];

    public bool CanReachDismountPosition(Vector3 worldPosition, float playerRadius)
    {
        if (drivingCollider == null) drivingCollider = GetComponent<BoxCollider>();
        if (drivingCollider == null || !isActiveAndEnabled) return false;
        ResolveCollisionBox(transform.position, transform.rotation, out Vector3 center, out Vector3 halfExtents);
        Vector3 delta = worldPosition - center;
        float dx = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, transform.rotation * Vector3.right))
            - halfExtents.x - collisionSkinWidth);
        float dz = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(delta, transform.rotation * Vector3.forward))
            - halfExtents.z - collisionSkinWidth);
        float edgeDistanceSqr = dx * dx + dz * dz;
        float radius = Mathf.Max(0f, playerRadius);
        float reach = radius + DismountMaxGap;
        return edgeDistanceSqr >= radius * radius && edgeDistanceSqr <= reach * reach;
    }

    public override void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
        HandleMountedInput(worldMoveDirection, moveSpeed, deltaTime, null);
    }

    public override void HandleMountedInput(
        Vector3 worldMoveDirection, float moveSpeed, float deltaTime, Player mountedPlayer)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (drivingCollider == null) drivingCollider = GetComponent<BoxCollider>();
        if (terrain == null || drivingCollider == null || !isActiveAndEnabled)
        {
            ResetVehicleMotion();
            return;
        }
        float stepTime = Mathf.Clamp(deltaTime, 0f, 0.25f);
        if (stepTime <= 0f) return;

        worldMoveDirection.y = 0f;
        float magnitude = Mathf.Clamp01(worldMoveDirection.magnitude);
        Quaternion rotation = transform.rotation;
        Quaternion targetRotation = rotation;
        float throttle = 0f;
        if (magnitude > inputDeadZone)
        {
            Vector3 direction = worldMoveDirection.normalized;
            bool reverse = Vector3.Dot(rotation * Vector3.forward, direction) < -0.5f;
            targetRotation = Quaternion.LookRotation(reverse ? -direction : direction, Vector3.up);
            throttle = reverse ? -magnitude : magnitude;
        }

        float speed = UpdateVehicleSignedSpeed(throttle, stepTime, EffectiveVehicleMaxSpeed);
        float distance = Mathf.Clamp(speed * stepTime,
            -MovementStepDistance * MaxMovementSubsteps, MovementStepDistance * MaxMovementSubsteps);
        float rotationDistance = Mathf.Min(Quaternion.Angle(rotation, targetRotation),
            steeringDegreesPerSecond * stepTime * magnitude);
        if (Mathf.Abs(distance) <= MovementEpsilon && rotationDistance <= MovementEpsilon) return;
        int steps = Mathf.Clamp(Mathf.Max(
            Mathf.CeilToInt(Mathf.Abs(distance) / MovementStepDistance),
            Mathf.CeilToInt(rotationDistance / RotationStepDegrees)), 1, MaxMovementSubsteps);
        Vector3 position = transform.position;
        float navigationScore = ReadNavigationScore(terrain, position, rotation);
        for (int i = 0; i < steps; i++)
        {
            Quaternion nextRotation = Quaternion.RotateTowards(rotation, targetRotation, rotationDistance / steps);
            if (CanOccupyPose(terrain, position, nextRotation, navigationScore, mountedPlayer, out float turnScore))
            {
                rotation = nextRotation;
                navigationScore = turnScore;
            }
            if (Mathf.Abs(distance) <= MovementEpsilon) continue;
            Vector3 nextPosition = position + rotation * Vector3.forward * (distance / steps);
            if (!CanOccupyPose(terrain, nextPosition, rotation, navigationScore, mountedPlayer, out float moveScore))
            {
                ResetVehicleMotion();
                break;
            }
            position = nextPosition;
            navigationScore = moveScore;
        }

        if ((position - transform.position).sqrMagnitude <= MovementEpsilon * MovementEpsilon
            && Quaternion.Angle(rotation, transform.rotation) <= MovementEpsilon) return;
        transform.SetPositionAndRotation(position, rotation);
        RefreshSingleCellRuntimePlacement(position, ResolveQuarterTurns(rotation));
        Physics.SyncTransforms();
    }

    private bool CanOccupyPose(TerrainGenerator terrain, Vector3 position, Quaternion rotation,
        float previousNavigationScore, Player mountedPlayer, out float navigationScore)
    {
        navigationScore = ReadNavigationScore(terrain, position, rotation);
        if (float.IsNegativeInfinity(navigationScore)) return false;
        // A raft placed at the shoreline may move back into deeper water, never farther ashore.
        if (navigationScore < 0f && navigationScore <= previousNavigationScore + MovementEpsilon) return false;
        ResolveCollisionBox(position, rotation, out Vector3 center, out Vector3 halfExtents);
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, obstacleBuffer,
            rotation, obstacleLayers, QueryTriggerInteraction.Ignore);
        bool blocked = count >= obstacleBuffer.Length;
        Transform playerRoot = mountedPlayer != null ? mountedPlayer.transform : null;
        Transform playerBody = mountedPlayer != null ? mountedPlayer.BodyTransform : null;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = obstacleBuffer[i];
            obstacleBuffer[i] = null;
            if (obstacle == null || obstacle.transform.IsChildOf(transform)
                || playerRoot != null && obstacle.transform.IsChildOf(playerRoot)
                || playerBody != null && obstacle.transform.IsChildOf(playerBody)) continue;
            blocked = true;
        }
        return !blocked;
    }

    private float ReadNavigationScore(TerrainGenerator terrain, Vector3 position, Quaternion rotation)
    {
        ResolveCollisionBox(position, rotation, out Vector3 center, out Vector3 halfExtents);
        Vector3 right = rotation * Vector3.right * halfExtents.x;
        Vector3 forward = rotation * Vector3.forward * halfExtents.z;
        float score = float.PositiveInfinity;
        for (int z = -1; z <= 1; z++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Vector3 sample = center + right * x + forward * z;
                Vector2Int coordinate = new Vector2Int(Mathf.RoundToInt(sample.x), Mathf.RoundToInt(sample.z));
                if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null
                    || block.MapObject != null && !ReferenceEquals(block.MapObject, this))
                    return float.NegativeInfinity;
                score = Mathf.Min(score, terrain.GetWaterSurfaceScoreAtWorldPosition(
                    new Vector2(sample.x, sample.z), waterWeightBuffer));
            }
        }
        return score;
    }

    private void ResolveCollisionBox(Vector3 position, Quaternion rotation,
        out Vector3 center, out Vector3 halfExtents)
    {
        Vector3 scale = transform.lossyScale;
        scale.Set(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        center = position + rotation * Vector3.Scale(drivingCollider.center, scale);
        halfExtents = Vector3.Scale(drivingCollider.size * 0.5f, scale);
        halfExtents.x = Mathf.Max(0.01f, halfExtents.x - collisionSkinWidth);
        halfExtents.y = Mathf.Max(0.01f, halfExtents.y - collisionSkinWidth);
        halfExtents.z = Mathf.Max(0.01f, halfExtents.z - collisionSkinWidth);
    }

    private static int ResolveQuarterTurns(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        return ((Mathf.RoundToInt(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg / 90f) % 4) + 4) % 4;
    }
}
