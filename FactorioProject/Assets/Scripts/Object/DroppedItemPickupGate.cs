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

    public void MarkDropped(float radius = 0.5f)
    {
        exitRadius = Mathf.Max(0f, radius);
        requiresExit = true;
        hasExited = false;
    }

    public void UpdateExitState(float distanceSqr)
    {
        if (!requiresExit || hasExited)
        {
            return;
        }

        if (distanceSqr > exitRadius * exitRadius)
        {
            hasExited = true;
        }
    }

    public bool CanPickup(float distanceSqr, float pickupRadiusSqr)
    {
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

    public void ClearGate()
    {
        requiresExit = false;
        hasExited = false;
    }

    private void OnDisable()
    {
        ClearGate();
    }
}
