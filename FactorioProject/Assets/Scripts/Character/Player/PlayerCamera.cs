using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    private static readonly Quaternion FixedRotation = Quaternion.Euler(45f, 45f, 0f);
    private static readonly Vector3 FixedForward = FixedRotation * Vector3.forward;
    private const float MinFreeCameraLookSensitivity = 1.5f;

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

    [SerializeField, Min(0.1f), InspectorName("Minimum Size")]
    [Tooltip("Orthographic camera minimum size.")]
    private float minOrthographicSize = 2f;

    [SerializeField, Min(0.1f), InspectorName("Maximum Size")]
    [Tooltip("Orthographic camera maximum size.")]
    private float maxOrthographicSize = 8f;

    [SerializeField, Min(0f)]
    private float orthographicMouseWheelZoomSpeed = 0.5f;

    [SerializeField, Min(0f)]
    private float orthographicPinchZoomSpeed = 0.01f;

    [Header("Free Camera")]
    [SerializeField, Min(0f)]
    private float freeCameraMoveSpeed = 12f;

    [SerializeField, Min(1f)]
    private float freeCameraFastMoveMultiplier = 4f;

    [SerializeField, Min(0f)]
    private float freeCameraLookSensitivity = 2.4f;

    private Transform focusTarget;
    private Camera cachedCamera;
    private float followDistance;
    private float targetFollowDistance;
    private float targetOrthographicSize;
    private float zoomVelocity;
    private float orthographicZoomVelocity;
    private bool hasInitializedDistance;
    private bool hasInitializedOrthographicSize;
    private bool hasInitializedBoxZoomVisibility;
    private bool freeCameraEnabled;
    private bool hasSavedFreeCameraProjection;
    private bool savedCameraOrthographic;
    private float savedCameraFieldOfView;
    private float savedCameraOrthographicSize;
    private float freeCameraYaw;
    private float freeCameraPitch;

    public float MinimumOrthographicSize => minOrthographicSize;
    public float MaximumOrthographicSize => maxOrthographicSize;
    public bool FreeCameraEnabled => freeCameraEnabled;

    public void SetOrthographicSizeRange(float minimumSize, float maximumSize)
    {
        minOrthographicSize = Mathf.Max(0.1f, minimumSize);
        maxOrthographicSize = Mathf.Max(minOrthographicSize, maximumSize);
        ClampOrthographicZoomState();
    }

    public void SetFreeCameraEnabled(bool enabled)
    {
        EnsureCameraCached();
        if (freeCameraEnabled == enabled)
        {
            ApplyFreeCameraProjectionState();
            return;
        }

        freeCameraEnabled = enabled;
        if (freeCameraEnabled)
        {
            CaptureFreeCameraProjectionState();
            Vector3 eulerAngles = transform.rotation.eulerAngles;
            freeCameraYaw = eulerAngles.y;
            freeCameraPitch = NormalizePitchAngle(eulerAngles.x);
            ApplyFreeCameraProjectionState();
            return;
        }

        RestoreFreeCameraProjectionState();
        hasInitializedDistance = false;
    }

    private void Start()
    {
        NormalizeZoomSettings();
        cachedCamera = GetComponent<Camera>();
        ConfigureCameraForStableTerrainEdges();
        ResolveTarget();
    }

    private void LateUpdate()
    {
        ItemLightController.UpdateDisplayLightGlobals(ResolveDisplayLightingFocusPosition());

        if (freeCameraEnabled)
        {
            EnsureCameraCached();
            ApplyFreeCameraProjectionState();
            HandleFreeCameraInput();
            RefreshBoxCountTextZoomVisibility();
            return;
        }

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
        RefreshBoxCountTextZoomVisibility();

        transform.rotation = FixedRotation;
        transform.position = focusPoint - FixedForward * followDistance;
    }

    private void CaptureFreeCameraProjectionState()
    {
        if (cachedCamera == null || hasSavedFreeCameraProjection)
        {
            return;
        }

        savedCameraOrthographic = cachedCamera.orthographic;
        savedCameraFieldOfView = cachedCamera.fieldOfView;
        savedCameraOrthographicSize = cachedCamera.orthographicSize;
        hasSavedFreeCameraProjection = true;
    }

    private void ApplyFreeCameraProjectionState()
    {
        if (!freeCameraEnabled || cachedCamera == null)
        {
            return;
        }

        if (!hasSavedFreeCameraProjection)
        {
            CaptureFreeCameraProjectionState();
        }

        cachedCamera.orthographic = false;
    }

    private void RestoreFreeCameraProjectionState()
    {
        if (cachedCamera == null || !hasSavedFreeCameraProjection)
        {
            hasSavedFreeCameraProjection = false;
            return;
        }

        cachedCamera.orthographic = savedCameraOrthographic;
        cachedCamera.fieldOfView = savedCameraFieldOfView;
        if (savedCameraOrthographic)
        {
            cachedCamera.orthographicSize = ClampOrthographicSize(savedCameraOrthographicSize);
            targetOrthographicSize = cachedCamera.orthographicSize;
            hasInitializedOrthographicSize = true;
        }

        hasSavedFreeCameraProjection = false;
    }

    private void HandleFreeCameraInput()
    {
        if (GameManager.TextInputFocused)
        {
            return;
        }

        float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
        if (Input.GetMouseButton(1))
        {
            freeCameraYaw += Input.GetAxisRaw("Mouse X") * freeCameraLookSensitivity;
            freeCameraPitch -= Input.GetAxisRaw("Mouse Y") * freeCameraLookSensitivity;
            freeCameraPitch = Mathf.Clamp(freeCameraPitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(freeCameraPitch, freeCameraYaw, 0f);
        }

        Vector3 moveDirection = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            moveDirection += transform.forward;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            moveDirection -= transform.forward;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            moveDirection += transform.right;
        }

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            moveDirection -= transform.right;
        }

        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.PageUp))
        {
            moveDirection += Vector3.up;
        }

        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.PageDown))
        {
            moveDirection -= Vector3.up;
        }

        if (moveDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        float speed = Mathf.Max(0f, freeCameraMoveSpeed);
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            speed *= Mathf.Max(1f, freeCameraFastMoveMultiplier);
        }

        transform.position += moveDirection.normalized * speed * deltaTime;
    }

    private static float NormalizePitchAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private Vector3 ResolveDisplayLightingFocusPosition()
    {
        if (focusTarget != null)
        {
            return focusTarget.position + focusOffset;
        }

        if (target != null)
        {
            return target.position + focusOffset;
        }

        return transform.position;
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
        if (GameManager.TextInputFocused)
        {
            return;
        }

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

    private void RefreshBoxCountTextZoomVisibility()
    {
        if (cachedCamera == null)
        {
            return;
        }

        float normalizedZoom = cachedCamera.orthographic
            ? Mathf.InverseLerp(
                Mathf.Max(0.1f, minOrthographicSize),
                Mathf.Max(minOrthographicSize, maxOrthographicSize),
                cachedCamera.orthographicSize)
            : Mathf.InverseLerp(
                Mathf.Max(0.1f, minFollowDistance),
                Mathf.Max(minFollowDistance, maxFollowDistance),
                followDistance);
        BoxObject.RefreshCountTextZoomVisibility(
            normalizedZoom,
            !hasInitializedBoxZoomVisibility);
        hasInitializedBoxZoomVisibility = true;
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
            ConfigureCameraForStableTerrainEdges();
        }
    }

    private void ConfigureCameraForStableTerrainEdges()
    {
        if (cachedCamera == null)
        {
            return;
        }

        cachedCamera.allowMSAA = false;
        cachedCamera.nearClipPlane = Mathf.Max(0.1f, cachedCamera.nearClipPlane);
        cachedCamera.farClipPlane = Mathf.Min(200f, Mathf.Max(cachedCamera.nearClipPlane + 1f, cachedCamera.farClipPlane));
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

    private void NormalizeZoomSettings()
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
        freeCameraMoveSpeed = Mathf.Max(0f, freeCameraMoveSpeed);
        freeCameraFastMoveMultiplier = Mathf.Max(1f, freeCameraFastMoveMultiplier);
        freeCameraLookSensitivity = Mathf.Max(MinFreeCameraLookSensitivity, freeCameraLookSensitivity);
    }

    private void ClampOrthographicZoomState()
    {
        EnsureCameraCached();
        if (cachedCamera != null && cachedCamera.orthographic)
        {
            cachedCamera.orthographicSize = ClampOrthographicSize(cachedCamera.orthographicSize);
        }

        if (hasInitializedOrthographicSize || targetOrthographicSize > 0f)
        {
            targetOrthographicSize = ClampOrthographicSize(targetOrthographicSize);
        }
    }

    private void OnValidate()
    {
        NormalizeZoomSettings();
        ClampOrthographicZoomState();
    }
}
