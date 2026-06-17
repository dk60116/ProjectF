using System.Collections.Generic;
using UnityEngine;

public class Vehicle : InstallationObject
{
    [SerializeField, Min(0.01f)]
    private float vehicleAccelerationPerSecond = 4f;
    [SerializeField, Min(0.01f)]
    private float vehicleMaxSpeed = 1.5f;
    [SerializeField, Min(0.01f)]
    private float vehicleDecelerationPerSecond = 4f;
    [SerializeField]
    private List<Transform> playerPoint;

    [SerializeField]
    private List<Transform> wheels;
    [SerializeField, Min(0.01f)]
    private float wheelRadius = 0.18f;
    [SerializeField]
    private Vector3 wheelLocalRotationAxis = Vector3.right;
    [SerializeField]
    private bool invertWheelRotation;

    private float currentVehicleSignedSpeed;

    public float VehicleAccelerationPerSecond => Mathf.Max(0.01f, vehicleAccelerationPerSecond);
    public float VehicleDecelerationPerSecond => Mathf.Max(0.01f, vehicleDecelerationPerSecond);
    public float VehicleMaxSpeed => Mathf.Max(0.01f, vehicleMaxSpeed);
    protected float CurrentVehicleSignedSpeed => currentVehicleSignedSpeed;

    public virtual void HandleMountedInput(Vector3 worldMoveDirection, float moveSpeed, float deltaTime)
    {
    }

    protected float UpdateVehicleSignedSpeed(float inputAxis, float deltaTime)
    {
        float normalizedDeltaTime = Mathf.Max(0f, deltaTime);
        float normalizedInputAxis = Mathf.Clamp(inputAxis, -1f, 1f);
        bool hasInput = Mathf.Abs(normalizedInputAxis) > 0.001f;
        float targetSpeed = hasInput
            ? normalizedInputAxis * VehicleMaxSpeed
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

        return currentVehicleSignedSpeed;
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

            wheel.Rotate(rotationAxis, degrees, Space.Self);
        }
    }

    public bool TryDockPlayer(Player targetPlayer)
    {
        if (targetPlayer == null)
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

    private bool DockPlayerToPoint(Player targetPlayer, Transform targetPoint)
    {
        if (targetPlayer == null || targetPoint == null)
        {
            return false;
        }

        ResetVehicleMotion();

        PlayerController playerController = targetPlayer.GetComponent<PlayerController>();
        if (playerController != null)
        {
            return playerController.TrySnapBodyToInteractionPoint(targetPoint, this);
        }

        targetPlayer.transform.SetPositionAndRotation(targetPoint.position, targetPoint.rotation);
        targetPlayer.StopImmediateActions();
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

}
