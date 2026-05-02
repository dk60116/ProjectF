using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    private static readonly Quaternion FixedRotation = Quaternion.Euler(45f, 45f, 0f);
    private static readonly Vector3 FixedForward = FixedRotation * Vector3.forward;

    [SerializeField]
    private Transform target;

    [SerializeField]
    private Vector3 focusOffset;

    [Header("Zoom")]
    [SerializeField, Min(0.1f)]
    private float minFollowDistance = 6f;

    [SerializeField, Min(0.1f)]
    private float maxFollowDistance = 18f;

    [SerializeField, Min(0f)]
    private float mouseWheelZoomSpeed = 2f;

    [SerializeField, Min(0f)]
    private float pinchZoomSpeed = 0.02f;

    [SerializeField, Min(0f)]
    private float zoomSmoothTime = 0.08f;

    [SerializeField, Min(0.1f)]
    private float minOrthographicSize = 2f;

    [SerializeField, Min(0.1f)]
    private float maxOrthographicSize = 15f;

    [SerializeField, Min(0f)]
    private float orthographicMouseWheelZoomSpeed = 0.5f;

    [SerializeField, Min(0f)]
    private float orthographicPinchZoomSpeed = 0.01f;

    private Transform focusTarget;
    private Camera cachedCamera;
    private float followDistance;
    private float targetFollowDistance;
    private float targetOrthographicSize;
    private float zoomVelocity;
    private float orthographicZoomVelocity;
    private bool hasInitializedDistance;
    private bool hasInitializedOrthographicSize;

    private void Start()
    {
        cachedCamera = GetComponent<Camera>();
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

            followDistance = ClampFollowDistance(followDistance);
            targetFollowDistance = followDistance;
            hasInitializedDistance = true;
        }

        EnsureCameraCached();
        EnsureOrthographicSizeInitialized();
        HandleZoomInput();
        UpdateZoom();

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

    private void HandleZoomInput()
    {
        float zoomDelta = 0f;
        bool useOrthographicZoom = cachedCamera != null && cachedCamera.orthographic;

        float wheelDelta = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheelDelta) > 0.0001f)
        {
            zoomDelta += wheelDelta * (useOrthographicZoom ? orthographicMouseWheelZoomSpeed : mouseWheelZoomSpeed);
        }

        if (Input.touchCount >= 2)
        {
            Touch firstTouch = Input.GetTouch(0);
            Touch secondTouch = Input.GetTouch(1);
            Vector2 previousFirstPosition = firstTouch.position - firstTouch.deltaPosition;
            Vector2 previousSecondPosition = secondTouch.position - secondTouch.deltaPosition;
            float previousDistance = Vector2.Distance(previousFirstPosition, previousSecondPosition);
            float currentDistance = Vector2.Distance(firstTouch.position, secondTouch.position);
            zoomDelta += (currentDistance - previousDistance) * (useOrthographicZoom ? orthographicPinchZoomSpeed : pinchZoomSpeed);
        }

        if (Mathf.Abs(zoomDelta) <= 0.0001f)
        {
            return;
        }

        if (useOrthographicZoom)
        {
            targetOrthographicSize = ClampOrthographicSize(targetOrthographicSize - zoomDelta);
        }
        else
        {
            targetFollowDistance = ClampFollowDistance(targetFollowDistance - zoomDelta);
        }
    }

    private void UpdateZoom()
    {
        if (cachedCamera != null && cachedCamera.orthographic)
        {
            UpdateOrthographicSize();
            return;
        }

        UpdateFollowDistance();
    }

    private void UpdateFollowDistance()
    {
        if (zoomSmoothTime <= 0f)
        {
            followDistance = targetFollowDistance;
            zoomVelocity = 0f;
            return;
        }

        followDistance = Mathf.SmoothDamp(
            followDistance,
            targetFollowDistance,
            ref zoomVelocity,
            zoomSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
    }

    private void UpdateOrthographicSize()
    {
        if (cachedCamera == null)
        {
            return;
        }

        if (zoomSmoothTime <= 0f)
        {
            cachedCamera.orthographicSize = targetOrthographicSize;
            orthographicZoomVelocity = 0f;
            return;
        }

        cachedCamera.orthographicSize = Mathf.SmoothDamp(
            cachedCamera.orthographicSize,
            targetOrthographicSize,
            ref orthographicZoomVelocity,
            zoomSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
    }

    private void EnsureCameraCached()
    {
        if (cachedCamera == null)
        {
            cachedCamera = GetComponent<Camera>();
        }
    }

    private void EnsureOrthographicSizeInitialized()
    {
        if (hasInitializedOrthographicSize || cachedCamera == null || !cachedCamera.orthographic)
        {
            return;
        }

        cachedCamera.orthographicSize = ClampOrthographicSize(cachedCamera.orthographicSize);
        targetOrthographicSize = cachedCamera.orthographicSize;
        hasInitializedOrthographicSize = true;
    }

    private float ClampFollowDistance(float distance)
    {
        float minDistance = Mathf.Max(0.1f, minFollowDistance);
        float maxDistance = Mathf.Max(minDistance, maxFollowDistance);
        return Mathf.Clamp(distance, minDistance, maxDistance);
    }

    private float ClampOrthographicSize(float size)
    {
        float minSize = Mathf.Max(0.1f, minOrthographicSize);
        float maxSize = Mathf.Max(minSize, maxOrthographicSize);
        return Mathf.Clamp(size, minSize, maxSize);
    }

    private void OnValidate()
    {
        minFollowDistance = Mathf.Max(0.1f, minFollowDistance);
        maxFollowDistance = Mathf.Max(minFollowDistance, maxFollowDistance);
        mouseWheelZoomSpeed = Mathf.Max(0f, mouseWheelZoomSpeed);
        pinchZoomSpeed = Mathf.Max(0f, pinchZoomSpeed);
        zoomSmoothTime = Mathf.Max(0f, zoomSmoothTime);
        minOrthographicSize = Mathf.Max(0.1f, minOrthographicSize);
        maxOrthographicSize = Mathf.Max(minOrthographicSize, maxOrthographicSize);
        orthographicMouseWheelZoomSpeed = Mathf.Max(0f, orthographicMouseWheelZoomSpeed);
        orthographicPinchZoomSpeed = Mathf.Max(0f, orthographicPinchZoomSpeed);
    }
}
