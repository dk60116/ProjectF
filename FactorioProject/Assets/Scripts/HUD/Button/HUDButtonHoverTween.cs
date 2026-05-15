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
    private static readonly Dictionary<Behaviour, int> sharedFrozenLayoutDriverCounts = new Dictionary<Behaviour, int>();

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
    private Vector2 hoverBaseSizeDelta;
    private Vector3 hoverBaseWorldCenter;
    private Vector3 hoverBaseAnchoredPosition;
    private readonly Vector3[] worldCorners = new Vector3[4];
    private bool hasHoverBaseSize;
    private bool hasHoverBaseGeometry;
    private bool hoverActive;
    private bool pointerInside;
    private int enabledFrame = -1;
    private readonly List<NestedButtonSizeCompensation> nestedButtonCompensations = new List<NestedButtonSizeCompensation>();
    private readonly List<FrozenLayoutDriver> frozenLayoutDrivers = new List<FrozenLayoutDriver>();
    private readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>();

    private sealed class NestedButtonSizeCompensation
    {
        public RectTransform target;
        public Vector2 baseSize;
        public Vector2 baseSizeDelta;
        public Vector3 baseAnchoredPosition;
        public Vector3 baseWorldCenter;
    }

    private sealed class FrozenLayoutDriver
    {
        public Behaviour component;
        public RectTransform rectTransform;
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
        RestoreFrozenLayoutDrivers(rebuildLayout);
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
            Canvas.ForceUpdateCanvases();
            FreezeLayoutDrivers();
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
        CaptureNestedButtonCompensations();
        TweenTo(hoverBaseSize * Mathf.Max(1f, hoverSizeMultiplier), false, false);
    }

    private void EndHover()
    {
        if (!hasHoverBaseSize)
        {
            KillSizeTween();
            RestoreNestedButtonCompensations();
            RestoreFrozenLayoutDrivers(true);
            hoverActive = false;
            hasHoverBaseGeometry = false;
            return;
        }

        if (!hoverActive)
        {
            return;
        }

        hoverActive = false;
        TweenTo(hoverBaseSize, true, true);
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

    private void TweenTo(Vector2 targetSize, bool restoreCompensationsOnComplete, bool clearBaseSizeOnComplete)
    {
        KillSizeTween();

        if (resolvedSizeTarget == null)
        {
            return;
        }

        if (tweenDuration <= 0f)
        {
            ApplyRectSize(resolvedSizeTarget, targetSize);
            ApplyCenterCompensation();
            ApplyNestedButtonCompensations();
            CompleteSizeTween(restoreCompensationsOnComplete, clearBaseSizeOnComplete);
            return;
        }

        Vector2 currentSize = GetRectSize(resolvedSizeTarget);
        sizeTween = DOTween.To(
                () => currentSize,
                value =>
                {
                    currentSize = value;
                    ApplyRectSize(resolvedSizeTarget, value);
                    ApplyCenterCompensation();
                    ApplyNestedButtonCompensations();
                },
                targetSize,
                tweenDuration)
            .SetEase(ease)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                ApplyRectSize(resolvedSizeTarget, targetSize);
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
            RestoreFrozenLayoutDrivers(true);
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

        ApplyRectSize(resolvedSizeTarget, hoverBaseSize);
        RestoreHoverGeometryIfOwned();
    }

    private void FreezeLayoutDrivers()
    {
        RestoreFrozenLayoutDrivers(false);

        if (resolvedSizeTarget == null)
        {
            return;
        }

        FreezeLayoutDriversInChildren(resolvedSizeTarget);

        Transform current = resolvedSizeTarget;
        while (current != null)
        {
            RectTransform currentRect = current as RectTransform;
            FreezeLayoutDriver(current.GetComponent<ContentSizeFitter>(), currentRect);
            FreezeLayoutDriver(current.GetComponent<LayoutGroup>(), currentRect);
            FreezeLayoutDriver(current.GetComponent<AspectRatioFitter>(), currentRect);

            if (current.GetComponent<Canvas>() != null && current != resolvedSizeTarget)
            {
                break;
            }

            current = current.parent;
        }
    }

    private void FreezeLayoutDriversInChildren(RectTransform root)
    {
        if (root == null)
        {
            return;
        }

        ContentSizeFitter[] contentSizeFitters = root.GetComponentsInChildren<ContentSizeFitter>(true);
        for (int i = 0; i < contentSizeFitters.Length; i++)
        {
            FreezeLayoutDriver(contentSizeFitters[i], contentSizeFitters[i].transform as RectTransform);
        }

        LayoutGroup[] layoutGroups = root.GetComponentsInChildren<LayoutGroup>(true);
        for (int i = 0; i < layoutGroups.Length; i++)
        {
            FreezeLayoutDriver(layoutGroups[i], layoutGroups[i].transform as RectTransform);
        }

        AspectRatioFitter[] aspectRatioFitters = root.GetComponentsInChildren<AspectRatioFitter>(true);
        for (int i = 0; i < aspectRatioFitters.Length; i++)
        {
            FreezeLayoutDriver(aspectRatioFitters[i], aspectRatioFitters[i].transform as RectTransform);
        }
    }

    private void FreezeLayoutDriver(Behaviour component, RectTransform rectTransform)
    {
        if (component == null || rectTransform == null || HasFrozenLayoutDriver(component))
        {
            return;
        }

        if (sharedFrozenLayoutDriverCounts.TryGetValue(component, out int freezeCount))
        {
            sharedFrozenLayoutDriverCounts[component] = freezeCount + 1;
        }
        else
        {
            if (!component.enabled)
            {
                return;
            }

            component.enabled = false;
            sharedFrozenLayoutDriverCounts[component] = 1;
        }

        frozenLayoutDrivers.Add(new FrozenLayoutDriver
        {
            component = component,
            rectTransform = rectTransform
        });
    }

    private bool HasFrozenLayoutDriver(Behaviour component)
    {
        for (int i = 0; i < frozenLayoutDrivers.Count; i++)
        {
            if (frozenLayoutDrivers[i] != null && frozenLayoutDrivers[i].component == component)
            {
                return true;
            }
        }

        return false;
    }

    private void RestoreFrozenLayoutDrivers(bool rebuildLayout)
    {
        if (frozenLayoutDrivers.Count <= 0)
        {
            return;
        }

        for (int i = frozenLayoutDrivers.Count - 1; i >= 0; i--)
        {
            FrozenLayoutDriver frozenDriver = frozenLayoutDrivers[i];
            if (frozenDriver != null && frozenDriver.component != null)
            {
                RestoreSharedLayoutDriver(frozenDriver.component);
            }
        }

        if (rebuildLayout)
        {
            Canvas.ForceUpdateCanvases();
            for (int i = frozenLayoutDrivers.Count - 1; i >= 0; i--)
            {
                RectTransform rectTransform = frozenLayoutDrivers[i] != null
                    ? frozenLayoutDrivers[i].rectTransform
                    : null;
                if (rectTransform != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
                }
            }
            Canvas.ForceUpdateCanvases();
        }

        frozenLayoutDrivers.Clear();
    }

    private static void RestoreSharedLayoutDriver(Behaviour component)
    {
        if (component == null)
        {
            return;
        }

        if (!sharedFrozenLayoutDriverCounts.TryGetValue(component, out int freezeCount))
        {
            return;
        }

        if (freezeCount > 1)
        {
            sharedFrozenLayoutDriverCounts[component] = freezeCount - 1;
            return;
        }

        sharedFrozenLayoutDriverCounts.Remove(component);
        component.enabled = true;
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
        hoverBaseSizeDelta = resolvedSizeTarget.sizeDelta;
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

        resolvedSizeTarget.sizeDelta = hoverBaseSizeDelta;
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
                baseSize = GetRectSize(compensationTarget),
                baseSizeDelta = compensationTarget.sizeDelta,
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

            ApplyRectSize(compensation.target, compensation.baseSize);

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
                compensation.target.sizeDelta = compensation.baseSizeDelta;
                compensation.target.anchoredPosition3D = compensation.baseAnchoredPosition;
            }
        }

        nestedButtonCompensations.Clear();
    }

    private static void ApplyRectSize(RectTransform target, Vector2 size)
    {
        target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, size.x));
        target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, size.y));
    }

    private static Vector2 GetRectSize(RectTransform target)
    {
        return target.rect.size;
    }
}
