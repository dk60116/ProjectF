using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class HUDButtonHoverTween : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float HiddenSizeSqrMagnitude = 0.0001f;
    [SerializeField, FormerlySerializedAs("hoverScaleMultiplier"), Min(1f)]
    private float hoverSizeMultiplier = 1.1f;

    [SerializeField, Min(0f)]
    private float tweenDuration = 0.08f;

    [SerializeField]
    private Ease ease = Ease.OutCubic;

    [SerializeField, FormerlySerializedAs("scaleTarget")]
    private RectTransform sizeTarget;

    private RectTransform rectTransform;
    private RectTransform resolvedSizeTarget;
    private Button button;
    private Tween sizeTween;
    private Vector2 hoverBaseSize;
    private Vector3 hoverBaseLocalScale;
    private Vector3 hoverBaseWorldCenter;
    private Vector3 hoverBaseAnchoredPosition;
    private readonly Vector3[] worldCorners = new Vector3[4];
    private bool hasHoverBaseSize;
    private bool hasHoverBaseGeometry;
    private bool hoverActive;
    private bool pointerInside;
    private int enabledFrame = -1;
    private readonly List<NestedButtonSizeCompensation> nestedButtonCompensations = new List<NestedButtonSizeCompensation>();
    private readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>();

    private sealed class NestedButtonSizeCompensation
    {
        public RectTransform target;
        public Vector3 baseLocalScale;
        public Vector3 baseAnchoredPosition;
        public Vector3 baseWorldCenter;
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        enabledFrame = Time.frameCount;
    }

    private void OnDisable()
    {
        ResetHoverImmediate(false);
    }

    public void ResetHoverImmediate(bool rebuildLayout = true)
    {
        KillSizeTween();
        RestoreHoverSizeIfOwned();
        RestoreNestedButtonCompensations();
        hoverActive = false;
        pointerInside = false;
        hasHoverBaseSize = false;
        hasHoverBaseGeometry = false;
    }

    private void Update()
    {
        if (pointerInside)
        {
            RefreshHoverState(null);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        RefreshHoverState(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        EndHover();
    }

    private void RefreshHoverState(PointerEventData eventData)
    {
        if (Time.frameCount <= enabledFrame)
        {
            return;
        }

        if (!CanAnimateHover())
        {
            EndHover();
            return;
        }

        if (IsPointerOverNestedButton(eventData))
        {
            EndHover();
            return;
        }

        BeginHover();
    }

    private void BeginHover()
    {
        if (!CanAnimateHover())
        {
            return;
        }

        if (!hasHoverBaseSize)
        {
            CaptureHoverBaseGeometry();
            hasHoverBaseSize = hoverBaseSize.sqrMagnitude > HiddenSizeSqrMagnitude;
        }
        else if (!hasHoverBaseGeometry)
        {
            CaptureHoverBaseGeometry();
        }

        if (!hasHoverBaseSize || hoverActive)
        {
            return;
        }

        hoverActive = true;
        if (nestedButtonCompensations.Count == 0)
        {
            CaptureNestedButtonCompensations();
        }
        TweenTo(Mathf.Max(1f, hoverSizeMultiplier), false, false);
    }

    private void EndHover()
    {
        if (!hasHoverBaseSize)
        {
            KillSizeTween();
            RestoreNestedButtonCompensations();
            hoverActive = false;
            hasHoverBaseGeometry = false;
            return;
        }

        if (!hoverActive)
        {
            return;
        }

        hoverActive = false;
        TweenTo(1f, true, true);
    }

    private bool CanAnimateHover()
    {
        CacheReferences();
        return isActiveAndEnabled
               && rectTransform != null
               && resolvedSizeTarget != null
               && button != null
               && button.IsInteractable()
               && gameObject.activeInHierarchy;
    }

    private void CacheReferences()
    {
        if (rectTransform == null)
        {
            rectTransform = transform as RectTransform;
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }

        resolvedSizeTarget = ResolveSizeTarget();
    }

    private void TweenTo(float targetMultiplier, bool restoreCompensationsOnComplete, bool clearBaseSizeOnComplete)
    {
        KillSizeTween();

        if (resolvedSizeTarget == null)
        {
            return;
        }

        Vector3 targetScale = new Vector3(
            hoverBaseLocalScale.x * targetMultiplier,
            hoverBaseLocalScale.y * targetMultiplier,
            hoverBaseLocalScale.z);

        if (tweenDuration <= 0f)
        {
            resolvedSizeTarget.localScale = targetScale;
            ApplyCenterCompensation();
            ApplyNestedButtonCompensations();
            CompleteSizeTween(restoreCompensationsOnComplete, clearBaseSizeOnComplete);
            return;
        }

        Vector3 currentScale = resolvedSizeTarget.localScale;
        sizeTween = DOTween.To(
                () => currentScale,
                value =>
                {
                    currentScale = value;
                    resolvedSizeTarget.localScale = value;
                    ApplyCenterCompensation();
                    ApplyNestedButtonCompensations();
                },
                targetScale,
                tweenDuration)
            .SetEase(ease)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                resolvedSizeTarget.localScale = targetScale;
                ApplyCenterCompensation();
                ApplyNestedButtonCompensations();
                CompleteSizeTween(restoreCompensationsOnComplete, clearBaseSizeOnComplete);
            })
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    private void CompleteSizeTween(bool restoreCompensationsOnComplete, bool clearBaseSizeOnComplete)
    {
        if (restoreCompensationsOnComplete)
        {
            RestoreNestedButtonCompensations();
        }

        if (clearBaseSizeOnComplete)
        {
            RestoreHoverGeometryIfOwned();
            hasHoverBaseSize = false;
            hasHoverBaseGeometry = false;
        }
    }

    private void KillSizeTween()
    {
        if (sizeTween != null && sizeTween.IsActive())
        {
            sizeTween.Kill();
        }

        sizeTween = null;
    }

    private void RestoreHoverSizeIfOwned()
    {
        if (!hasHoverBaseGeometry || resolvedSizeTarget == null)
        {
            return;
        }

        resolvedSizeTarget.localScale = hoverBaseLocalScale;
        RestoreHoverGeometryIfOwned();
    }

    private void CaptureHoverBaseGeometry()
    {
        if (resolvedSizeTarget == null)
        {
            hasHoverBaseGeometry = false;
            hoverBaseSize = Vector2.zero;
            return;
        }

        hoverBaseSize = GetRectSize(resolvedSizeTarget);
        hoverBaseLocalScale = resolvedSizeTarget.localScale;
        hoverBaseAnchoredPosition = resolvedSizeTarget.anchoredPosition3D;
        hoverBaseWorldCenter = GetWorldCenter(resolvedSizeTarget);
        hasHoverBaseGeometry = true;
    }

    private void ApplyCenterCompensation()
    {
        if (!hasHoverBaseGeometry || resolvedSizeTarget == null)
        {
            return;
        }

        Vector3 centerOffset = hoverBaseWorldCenter - GetWorldCenter(resolvedSizeTarget);
        if (centerOffset.sqrMagnitude > 0.000001f)
        {
            resolvedSizeTarget.position += centerOffset;
        }
    }

    private void RestoreHoverGeometryIfOwned()
    {
        if (!hasHoverBaseGeometry || resolvedSizeTarget == null)
        {
            return;
        }

        resolvedSizeTarget.localScale = hoverBaseLocalScale;
        resolvedSizeTarget.anchoredPosition3D = hoverBaseAnchoredPosition;
    }

    private Vector3 GetWorldCenter(RectTransform target)
    {
        target.GetWorldCorners(worldCorners);
        return (worldCorners[0] + worldCorners[2]) * 0.5f;
    }

    private RectTransform ResolveSizeTarget()
    {
        if (sizeTarget != null)
        {
            return sizeTarget;
        }

        if (button != null
            && button.targetGraphic != null
            && button.targetGraphic.rectTransform != null
            && button.targetGraphic.rectTransform != rectTransform)
        {
            return button.targetGraphic.rectTransform;
        }

        return rectTransform;
    }

    private void CaptureNestedButtonCompensations()
    {
        RestoreNestedButtonCompensations();

        if (resolvedSizeTarget == null || button == null)
        {
            return;
        }

        Button[] childButtons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < childButtons.Length; i++)
        {
            Button childButton = childButtons[i];
            if (childButton == null || childButton == button)
            {
                continue;
            }

            RectTransform compensationTarget = ResolveNestedButtonCompensationTarget(childButton.transform);
            if (compensationTarget == null || HasCompensationTarget(compensationTarget))
            {
                continue;
            }

            nestedButtonCompensations.Add(new NestedButtonSizeCompensation
            {
                target = compensationTarget,
                baseLocalScale = compensationTarget.localScale,
                baseAnchoredPosition = compensationTarget.anchoredPosition3D,
                baseWorldCenter = GetWorldCenter(compensationTarget)
            });
        }
    }

    private bool IsPointerOverNestedButton(PointerEventData eventData)
    {
        GameObject eventTarget = eventData != null
            ? eventData.pointerCurrentRaycast.gameObject != null
                ? eventData.pointerCurrentRaycast.gameObject
                : eventData.pointerEnter
            : null;
        if (IsNestedButtonTarget(eventTarget))
        {
            return true;
        }

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        pointerRaycastResults.Clear();
        PointerEventData pointerData = new PointerEventData(eventSystem)
        {
            position = Input.mousePosition
        };
        eventSystem.RaycastAll(pointerData, pointerRaycastResults);
        for (int i = 0; i < pointerRaycastResults.Count; i++)
        {
            if (IsNestedButtonTarget(pointerRaycastResults[i].gameObject))
            {
                pointerRaycastResults.Clear();
                return true;
            }
        }

        pointerRaycastResults.Clear();
        return false;
    }

    private bool IsNestedButtonTarget(GameObject target)
    {
        if (target == null || button == null)
        {
            return false;
        }

        Button targetButton = target.GetComponentInParent<Button>();
        if (targetButton == null || targetButton == button)
        {
            return false;
        }

        Transform targetTransform = targetButton.transform;
        return targetTransform != null
               && targetTransform.IsChildOf(transform)
               && (resolvedSizeTarget == null || targetTransform.IsChildOf(resolvedSizeTarget));
    }

    private RectTransform ResolveNestedButtonCompensationTarget(Transform nestedButtonTransform)
    {
        if (nestedButtonTransform == null
            || resolvedSizeTarget == null
            || nestedButtonTransform == resolvedSizeTarget
            || !nestedButtonTransform.IsChildOf(resolvedSizeTarget))
        {
            return null;
        }

        Transform current = nestedButtonTransform;
        while (current.parent != null && current.parent != resolvedSizeTarget)
        {
            current = current.parent;
        }

        return current as RectTransform;
    }

    private bool HasCompensationTarget(RectTransform target)
    {
        for (int i = 0; i < nestedButtonCompensations.Count; i++)
        {
            if (nestedButtonCompensations[i].target == target)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyNestedButtonCompensations()
    {
        for (int i = 0; i < nestedButtonCompensations.Count; i++)
        {
            NestedButtonSizeCompensation compensation = nestedButtonCompensations[i];
            if (compensation == null || compensation.target == null)
            {
                continue;
            }

            Vector3 parentScaleRatio = GetHoverScaleRatio();
            compensation.target.localScale = new Vector3(
                DivideOrFallback(compensation.baseLocalScale.x, parentScaleRatio.x),
                DivideOrFallback(compensation.baseLocalScale.y, parentScaleRatio.y),
                DivideOrFallback(compensation.baseLocalScale.z, parentScaleRatio.z));

            Vector3 centerOffset = compensation.baseWorldCenter - GetWorldCenter(compensation.target);
            if (centerOffset.sqrMagnitude > 0.000001f)
            {
                compensation.target.position += centerOffset;
            }
        }
    }

    private void RestoreNestedButtonCompensations()
    {
        for (int i = 0; i < nestedButtonCompensations.Count; i++)
        {
            NestedButtonSizeCompensation compensation = nestedButtonCompensations[i];
            if (compensation != null && compensation.target != null)
            {
                compensation.target.localScale = compensation.baseLocalScale;
                compensation.target.anchoredPosition3D = compensation.baseAnchoredPosition;
            }
        }

        nestedButtonCompensations.Clear();
    }

    private Vector3 GetHoverScaleRatio()
    {
        if (resolvedSizeTarget == null)
        {
            return Vector3.one;
        }

        Vector3 currentScale = resolvedSizeTarget.localScale;
        return new Vector3(
            DivideOrFallback(currentScale.x, hoverBaseLocalScale.x),
            DivideOrFallback(currentScale.y, hoverBaseLocalScale.y),
            DivideOrFallback(currentScale.z, hoverBaseLocalScale.z));
    }

    private static float DivideOrFallback(float numerator, float denominator)
    {
        return Mathf.Abs(denominator) > Mathf.Epsilon ? numerator / denominator : 1f;
    }

    private static Vector2 GetRectSize(RectTransform target)
    {
        return target.rect.size;
    }
}
