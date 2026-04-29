using ProjectF.Attributes;
using UnityEngine;

public class DroppedItemPickupGate : MonoBehaviour
{
    [SerializeField, ReadOnly]
    private bool requiresExit;

    [SerializeField, ReadOnly]
    private bool hasExited;

    [SerializeField, ReadOnly, Min(0f)]
    private float exitRadius = 0.5f;

    [SerializeField, ReadOnly]
    private bool isSettled = true;

    [SerializeField, ReadOnly]
    private Vector3 dropOrigin;

    [SerializeField, ReadOnly]
    private bool hasOrigin;

    [SerializeField, ReadOnly]
    private bool autoPickupBlocked;

    private bool preserveStateOnDisable;

    public void MarkDropped(float radius = 0.5f, bool settled = true, Vector3 origin = default)
    {
        exitRadius = Mathf.Max(0f, radius);
        requiresExit = true;
        hasExited = false;
        isSettled = settled;
        dropOrigin = origin;
        hasOrigin = true;
    }

    public void UpdateExitState(Vector3 playerPosition)
    {
        if (!requiresExit || hasExited)
        {
            return;
        }

        Vector3 origin = hasOrigin ? dropOrigin : transform.position;
        Vector3 offset = playerPosition - origin;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (distanceSqr > exitRadius * exitRadius)
        {
            hasExited = true;
        }
    }

    public bool CanPickup(float distanceSqr, float pickupRadiusSqr)
    {
        if (autoPickupBlocked)
        {
            return false;
        }

        if (!requiresExit)
        {
            return true;
        }

        if (!hasExited)
        {
            return false;
        }

        return distanceSqr <= pickupRadiusSqr;
    }

    public bool CanHandPickup(float distanceSqr, float pickupRadiusSqr)
    {
        return CanManualPickup(distanceSqr, pickupRadiusSqr);
    }

    public bool CanManualPickup(float distanceSqr, float pickupRadiusSqr)
    {
        if (!isSettled)
        {
            return false;
        }

        return distanceSqr <= pickupRadiusSqr;
    }

    public void MarkSettled()
    {
        isSettled = true;
    }

    public void SetAutoPickupBlocked(bool blocked)
    {
        autoPickupBlocked = blocked;
    }

    public void SetPreserveStateOnDisable(bool preserve)
    {
        preserveStateOnDisable = preserve;
    }

    public void ClearGate()
    {
        requiresExit = false;
        hasExited = false;
        isSettled = true;
        hasOrigin = false;
        dropOrigin = Vector3.zero;
        autoPickupBlocked = false;
        preserveStateOnDisable = false;
    }

    private void OnDisable()
    {
        if (preserveStateOnDisable)
        {
            return;
        }

        ClearGate();
    }
}
