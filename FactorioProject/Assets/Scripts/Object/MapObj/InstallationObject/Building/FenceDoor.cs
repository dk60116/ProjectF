using UnityEngine;
using DG.Tweening;

public class FenceDoor : Wall
{
    private const float ClosedAngle = 0f;
    private const float OpenAngle = 90f;

    [SerializeField]
    private Transform hinge;
    [SerializeField]
    private Collider doorCollider;
    [SerializeField]
    private bool isOpen;
    [SerializeField, Min(0.01f)]
    private float hingeTweenDuration = 0.2f;
    [SerializeField]
    private Ease hingeTweenEase = Ease.OutCubic;
    [SerializeField]
    private bool disableColliderWhenOpen = true;
    [SerializeField]
    private bool invertOpenDirection;

    private float openedAngle = OpenAngle;

    public bool IsOpen => isOpen;
    public override bool AllowsAnimalTraversal => isOpen;

    protected override void OnEnable()
    {
        base.OnEnable();
        ApplyDoorState(false);
    }

    protected override void OnDisable()
    {
        hinge?.DOKill();
        base.OnDisable();
    }

    public void ToggleOpenState()
    {
        SetOpenState(!isOpen);
    }

    public void ToggleOpenState(Vector3 interactorWorldPosition)
    {
        SetOpenState(!isOpen, interactorWorldPosition);
    }

    public void SetOpenState(bool shouldOpen, bool animate = true)
    {
        if (isOpen == shouldOpen)
        {
            ApplyDoorState(false);
            return;
        }

        isOpen = shouldOpen;
        ApplyDoorState(animate && Application.isPlaying);
    }

    public void SetOpenState(bool shouldOpen, Vector3 interactorWorldPosition, bool animate = true)
    {
        if (shouldOpen)
        {
            openedAngle = ResolveOpenAngle(interactorWorldPosition);
        }

        SetOpenState(shouldOpen, animate);
    }

    public override void PrepareForPool()
    {
        hinge?.DOKill();
        base.PrepareForPool();
        ApplyDoorState(false);
    }

    private void ApplyDoorState(bool animate)
    {
        ApplyHingeRotation(animate);
        ApplyColliderState();
    }

    private void ApplyHingeRotation(bool animate)
    {
        if (hinge == null)
        {
            return;
        }

        hinge.DOKill();

        float targetAngle = isOpen ? openedAngle : ClosedAngle;
        if (animate && hingeTweenDuration > 0f)
        {
            float startAngle = GetCurrentHingeAngleY();
            DOTween.To(() => startAngle, value =>
                {
                    startAngle = value;
                    SetHingeAngleY(value);
                }, targetAngle, hingeTweenDuration)
                .SetTarget(hinge)
                .SetEase(hingeTweenEase);
            return;
        }

        SetHingeAngleY(targetAngle);
    }

    private float GetCurrentHingeAngleY()
    {
        if (hinge == null)
        {
            return ClosedAngle;
        }

        float angle = hinge.localEulerAngles.y;
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }

    private void SetHingeAngleY(float angle)
    {
        if (hinge == null)
        {
            return;
        }

        hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
    }

    private float ResolveOpenAngle(Vector3 interactorWorldPosition)
    {
        Transform referenceTransform = hinge != null ? hinge : transform;
        Vector3 toInteractor = interactorWorldPosition - referenceTransform.position;
        toInteractor.y = 0f;
        if (toInteractor.sqrMagnitude <= 0.0001f)
        {
            return openedAngle == 0f ? OpenAngle : openedAngle;
        }

        Vector3 doorForward = transform.forward;
        doorForward.y = 0f;
        if (doorForward.sqrMagnitude <= 0.0001f)
        {
            return openedAngle == 0f ? OpenAngle : openedAngle;
        }

        doorForward.Normalize();
        float side = Vector3.Dot(toInteractor.normalized, doorForward);
        float direction = side >= 0f ? -1f : 1f;
        if (invertOpenDirection)
        {
            direction *= -1f;
        }

        return OpenAngle * direction;
    }

    private void ApplyColliderState()
    {
        Collider resolvedCollider = ResolveDoorCollider();
        if (resolvedCollider == null)
        {
            return;
        }

        resolvedCollider.enabled = !disableColliderWhenOpen || !isOpen;
    }

    private Collider ResolveDoorCollider()
    {
        if (doorCollider != null)
        {
            return doorCollider;
        }

        // Door 프리팹의 충돌체는 FenceDoor 루트가 아니라 회전하는 Hinge 자식에 있다.
        // 루트만 검색하면 문이 열려도 문짝 충돌체가 계속 활성화된다.
        doorCollider = hinge != null
            ? hinge.GetComponentInChildren<Collider>(true)
            : GetComponentInChildren<Collider>(true);
        return doorCollider;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (hingeTweenDuration <= 0f)
        {
            hingeTweenDuration = 0.01f;
        }

        ApplyDoorState(false);
    }
#endif
}
