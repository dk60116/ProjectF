using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    private static readonly Quaternion FixedRotation = Quaternion.Euler(45f, 45f, 0f);
    private static readonly Vector3 FixedForward = FixedRotation * Vector3.forward;

    [SerializeField]
    private Transform target;

    [SerializeField]
    private Vector3 focusOffset;

    private Transform focusTarget;
    private float followDistance;
    private bool hasInitializedDistance;

    private void Start()
    {
        ResolveTarget();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            ResolveTarget();

            if (target == null)
            {
                return;
            }
        }

        Vector3 focusPoint = focusTarget.position + focusOffset;

        if (!hasInitializedDistance)
        {
            followDistance = Mathf.Abs(Vector3.Dot(transform.position - focusPoint, FixedForward));

            if (followDistance <= 0.01f)
            {
                followDistance = 10f;
            }

            hasInitializedDistance = true;
        }

        transform.rotation = FixedRotation;
        transform.position = focusPoint - FixedForward * followDistance;
    }

    private void ResolveTarget()
    {
        if (GameManager.Instance != null && GameManager.Instance.Player != null)
        {
            Player player = GameManager.Instance.Player;
            target = player.transform;
            focusTarget = player.BodyTransform != null ? player.BodyTransform : player.transform;
        }
    }
}
