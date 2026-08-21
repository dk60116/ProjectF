using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Vehicle : InstallationObject
{
    private const float VehicleLoadSpeedReductionPerMass = 0.01f;
    private const float MinimumVehicleLoadSpeedMultiplier = 0.01f;

    [SerializeField, Min(0.01f)]
    private float vehicleAccelerationPerSecond = 4f;
    [SerializeField, Min(0.01f)]
    private float vehicleMaxSpeed = 1.5f;
    [SerializeField, Min(0.01f)]
    private float vehicleDecelerationPerSecond = 4f;
    [FormerlySerializedAs("trainMass")]
    [SerializeField, Min(0.01f)]
    private float vehicleMass = 1f;
    [SerializeField]
    private List<Transform> playerPoint;

    [SerializeField]
    private List<Transform> wheels;
    [SerializeField, Min(0.01f)]
    private float wheelRadius = 0.18f;
    [SerializeField]
    private Vector3 wheelLocalRotationAxis = Vector3.right;
    [SerializeField]
    [Tooltip("미러링된 좌우 바퀴가 같은 방향으로 구르도록 차체 기준 회전축을 사용합니다.")]
    private bool wheelRotationUsesVehicleSpace;
    [SerializeField]
    private bool invertWheelRotation;

    private float currentVehicleSignedSpeed;
    private readonly Vector2Int[] runtimeCoordinateBuffer = new Vector2Int[1];

    public float VehicleAccelerationPerSecond => Mathf.Max(0.01f, vehicleAccelerationPerSecond);
    public float VehicleDecelerationPerSecond => Mathf.Max(0.01f, vehicleDecelerationPerSecond);
    public float VehicleMaxSpeed => Mathf.Max(0.01f, vehicleMaxSpeed);
    public float VehicleMass => Mathf.Max(0.01f, vehicleMass);
    public float VehicleLoadSpeedMultiplier => Mathf.Clamp(
        1f - VehicleMass * VehicleLoadSpeedReductionPerMass,
        MinimumVehicleLoadSpeedMultiplier,
        1f);
    public virtual float EffectiveVehicleMaxSpeed => VehicleMaxSpeed;
    public float CurrentVehicleSpeed => Mathf.Abs(currentVehicleSignedSpeed);
    public float CurrentVehicleSignedSpeed => currentVehicleSignedSpeed;

    public float ResolveSignedSpeedRelativeToFacing(Transform facingTransform)
    {
        if (facingTransform == null || Mathf.Abs(currentVehicleSignedSpeed) <= 0.0001f)
        {
            return currentVehicleSignedSpeed;
        }

        Vector3 vehicleForward = transform.forward;
        Vector3 facingForward = facingTransform.forward;
        vehicleForward.y = 0f;
        facingForward.y = 0f;
        if (vehicleForward.sqrMagnitude <= 0.0001f || facingForward.sqrMagnitude <= 0.0001f)
        {
            return currentVehicleSignedSpeed;
        }

        return Vector3.Dot(vehicleForward, facingForward) < 0f
            ? -currentVehicleSignedSpeed
            : currentVehicleSignedSpeed;
    }

    public virtual void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
    }

    public virtual void HandleMountedInput(
        Vector3 worldMoveDirection,
        float moveSpeed,
        float deltaTime,
        Player mountedPlayer)
    {
        HandleMountedInput(worldMoveDirection, moveSpeed, deltaTime);
    }

    public virtual void NotifyPlayerDismounted(Player dismountedPlayer)
    {
        ResetVehicleMotion();
        dismountedPlayer?.ClearMountedVehicleAnimation();
    }

    protected float UpdateVehicleSignedSpeed(float inputAxis, float deltaTime)
    {
        return UpdateVehicleSignedSpeed(inputAxis, deltaTime, VehicleMaxSpeed);
    }

    protected float UpdateVehicleSignedSpeed(float inputAxis, float deltaTime, float maxSpeed)
    {
        float normalizedDeltaTime = Mathf.Max(0f, deltaTime);
        float normalizedInputAxis = Mathf.Clamp(inputAxis, -1f, 1f);
        bool hasInput = Mathf.Abs(normalizedInputAxis) > 0.001f;
        float resolvedMaxSpeed = Mathf.Max(0.01f, maxSpeed);
        float targetSpeed = hasInput
            ? normalizedInputAxis * resolvedMaxSpeed
            : 0f;

        float speedChangePerSecond = hasInput
            ? VehicleAccelerationPerSecond
            : VehicleDecelerationPerSecond;

        currentVehicleSignedSpeed = Mathf.MoveTowards(
            currentVehicleSignedSpeed,
            targetSpeed,
            speedChangePerSecond * normalizedDeltaTime);
        if (!hasInput && Mathf.Abs(currentVehicleSignedSpeed) <= 0.0001f)
        {
            currentVehicleSignedSpeed = 0f;
        }

        ClampCurrentVehicleSignedSpeed(resolvedMaxSpeed);
        return currentVehicleSignedSpeed;
    }

    protected void ClampCurrentVehicleSignedSpeed(float maxAbsSpeed)
    {
        float resolvedMaxSpeed = Mathf.Max(0.01f, maxAbsSpeed);
        if (Mathf.Abs(currentVehicleSignedSpeed) <= resolvedMaxSpeed)
        {
            return;
        }

        currentVehicleSignedSpeed = Mathf.Sign(currentVehicleSignedSpeed) * resolvedMaxSpeed;
    }

    protected void ResetVehicleMotion()
    {
        currentVehicleSignedSpeed = 0f;
    }

    protected void RotateWheelsByDistance(float signedDistance)
    {
        if (wheels == null
            || wheels.Count <= 0
            || Mathf.Abs(signedDistance) <= 0.0001f)
        {
            return;
        }

        Vector3 rotationAxis = wheelLocalRotationAxis;
        if (rotationAxis.sqrMagnitude <= 0.0001f)
        {
            rotationAxis = Vector3.right;
        }

        rotationAxis.Normalize();
        Space rotationSpace = Space.Self;
        if (wheelRotationUsesVehicleSpace)
        {
            rotationAxis = transform.TransformDirection(rotationAxis);
            rotationSpace = Space.World;
        }

        float rotationSign = invertWheelRotation ? -1f : 1f;
        float degrees = signedDistance
                        / (Mathf.PI * 2f * Mathf.Max(0.01f, wheelRadius))
                        * 360f
                        * rotationSign;
        for (int i = 0; i < wheels.Count; i++)
        {
            Transform wheel = wheels[i];
            if (wheel == null)
            {
                continue;
            }

            wheel.Rotate(rotationAxis, degrees, rotationSpace);
        }
    }

    protected bool RefreshSingleCellRuntimePlacement(
        Vector3 worldPosition,
        int quarterTurns)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
        int normalizedQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (RuntimeAnchorCoordinate == coordinate
            && RuntimeQuarterTurns == normalizedQuarterTurns
            && occupiedCoordinates != null
            && occupiedCoordinates.Count == 1
            && occupiedCoordinates[0] == coordinate)
        {
            return false;
        }

        runtimeCoordinateBuffer[0] = coordinate;
        ConfigurePlacementRuntime(
            coordinate,
            normalizedQuarterTurns,
            runtimeCoordinateBuffer,
            RuntimePlacementSequence);
        RobotArm.WakeAroundCoordinate(coordinate);
        return true;
    }

    public bool TryDockPlayer(Player targetPlayer)
    {
        if (!CanPlayerDock(targetPlayer))
        {
            return false;
        }

        Transform playerTransform = targetPlayer.BodyTransform != null
            ? targetPlayer.BodyTransform
            : targetPlayer.transform;

        if (!TryGetNearestPlayerPoint(playerTransform.position, out Transform targetPoint))
        {
            return false;
        }

        return DockPlayerToPoint(targetPlayer, targetPoint);
    }

    public bool TryDockPlayerAtPoint(Player targetPlayer, int pointIndex)
    {
        if (!TryGetPlayerPoint(pointIndex, out Transform targetPoint))
        {
            return TryDockPlayer(targetPlayer);
        }

        return DockPlayerToPoint(targetPlayer, targetPoint);
    }

    public virtual bool CanPlayerDock(Player targetPlayer)
    {
        return targetPlayer != null;
    }

    protected virtual bool PreparePlayerForDock(Player targetPlayer)
    {
        return true;
    }

    private bool DockPlayerToPoint(Player targetPlayer, Transform targetPoint)
    {
        if (!CanPlayerDock(targetPlayer)
            || targetPoint == null
            || !PreparePlayerForDock(targetPlayer))
        {
            return false;
        }

        ResetVehicleMotion();

        PlayerController playerController = targetPlayer.GetComponent<PlayerController>();
        if (playerController != null)
        {
            bool docked = playerController.TrySnapBodyToInteractionPoint(targetPoint, this);
            if (docked)
            {
                targetPlayer.UpdateMountedVehicleAnimation(this);
            }

            return docked;
        }

        targetPlayer.transform.SetPositionAndRotation(targetPoint.position, targetPoint.rotation);
        targetPlayer.StopImmediateActions();
        targetPlayer.UpdateMountedVehicleAnimation(this);
        Physics.SyncTransforms();
        return true;
    }

    public bool TryGetPlayerPoint(int pointIndex, out Transform point)
    {
        point = null;
        if (playerPoint == null
            || pointIndex < 0
            || pointIndex >= playerPoint.Count)
        {
            return false;
        }

        point = playerPoint[pointIndex];
        return point != null && point.gameObject.activeInHierarchy;
    }

    public bool TryGetPlayerPointIndex(Transform point, out int pointIndex)
    {
        pointIndex = -1;
        if (point == null || playerPoint == null)
        {
            return false;
        }

        for (int i = 0; i < playerPoint.Count; i++)
        {
            if (playerPoint[i] != point)
            {
                continue;
            }

            pointIndex = i;
            return true;
        }

        return false;
    }

    public bool TryGetNearestPlayerPoint(Vector3 worldPosition, out Transform nearestPoint)
    {
        nearestPoint = null;
        if (playerPoint == null || playerPoint.Count == 0)
        {
            return false;
        }

        float nearestDistanceSqr = float.MaxValue;
        for (int i = 0; i < playerPoint.Count; i++)
        {
            Transform point = playerPoint[i];
            if (point == null || !point.gameObject.activeInHierarchy)
            {
                continue;
            }

            float distanceSqr = GetPlanarDistanceSqr(worldPosition, point.position);
            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            nearestPoint = point;
        }

        return nearestPoint != null;
    }

    private static float GetPlanarDistanceSqr(Vector3 a, Vector3 b)
    {
        float deltaX = a.x - b.x;
        float deltaZ = a.z - b.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    public override void PrepareForPool()
    {
        ResetVehicleMotion();
        base.PrepareForPool();
    }

    protected override void OnDisable()
    {
        ResetVehicleMotion();
        base.OnDisable();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        vehicleMass = Mathf.Max(0.01f, vehicleMass);
    }
#endif
}
