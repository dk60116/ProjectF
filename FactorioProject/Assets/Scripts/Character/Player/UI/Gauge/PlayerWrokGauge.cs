using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class ResourceWrokGauge : MonoBehaviour
{
    [SerializeField]
    private List<Image> dotList = new List<Image>();

    [SerializeField]
    private int fallbackDotCount = 10;

    [SerializeField]
    private Vector2 fallbackDotSize = new Vector2(12f, 12f);

    [SerializeField]
    private Color fallbackBackgroundColor = new Color(0f, 0f, 0f, 1f);

    [SerializeField]
    private Color fallbackDotColor = Color.white;

    [SerializeField]
    private float dotPopDuration = 0.08f;

    [SerializeField]
    private float dotFadeDuration = 0.14f;

    [SerializeField]
    private float dotPopScale = 1.18f;

    [SerializeField]
    private float dotShrinkScale = 0.1f;

    private readonly List<GameObject> gaugeDots = new List<GameObject>();
    private readonly List<LayoutElement> gaugeDotLayoutElements = new List<LayoutElement>();
    private readonly List<CanvasGroup> gaugeDotVisualCanvasGroups = new List<CanvasGroup>();
    private readonly List<RectTransform> gaugeDotVisualRectTransforms = new List<RectTransform>();
    private readonly List<bool> gaugeDotVisibilityStates = new List<bool>();

    private CanvasGroup canvasGroup;
    private Image backgroundImage;
    private Resource targetResource;
    private int cachedChildCount = -1;
    private Tween hideTween;

    public bool IsFinishingHideAnimation => hideTween != null && hideTween.active;

    public static ResourceWrokGauge FindOrCreate()
    {
        ResourceWrokGauge existingGauge = FindObjectOfType<ResourceWrokGauge>(true);
        if (existingGauge != null)
        {
            return existingGauge;
        }

        GameObject gaugeObject = new GameObject("Resource Work Gauge", typeof(RectTransform));
        RectTransform gaugeRect = gaugeObject.GetComponent<RectTransform>();

        RectTransform parentRect = ResolveGaugeParentRect();
        if (parentRect != null)
        {
            gaugeRect.SetParent(parentRect, false);
        }

        gaugeRect.anchorMin = new Vector2(0.5f, 0f);
        gaugeRect.anchorMax = new Vector2(0.5f, 0f);
        gaugeRect.pivot = new Vector2(0.5f, 0f);
        gaugeRect.anchoredPosition = new Vector2(0f, 300f);
        gaugeRect.sizeDelta = new Vector2(300f, 30f);

        gaugeObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 1f);

        HorizontalLayoutGroup layoutGroup = gaugeObject.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.padding = new RectOffset(5, 5, 5, 5);
        layoutGroup.spacing = 5f;
        layoutGroup.childAlignment = TextAnchor.UpperLeft;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = true;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;

        gaugeObject.AddComponent<CanvasGroup>();

        ResourceWrokGauge gauge = gaugeObject.AddComponent<ResourceWrokGauge>();
        gauge.EnsureReferencesUpToDate();
        gauge.Hide();
        return gauge;
    }

    private static RectTransform ResolveGaugeParentRect()
    {
        UIManager uiManager = UIManager.Instance;
        if (uiManager != null && uiManager.HudGaugeRoot != null)
        {
            return uiManager.HudGaugeRoot;
        }

        PlayerHUD playerHUD = FindObjectOfType<PlayerHUD>(true);
        if (playerHUD != null && playerHUD.transform is RectTransform hudRect)
        {
            Transform existing = hudRect.Find("WorldGaugeRoot");
            RectTransform gaugeRoot = existing as RectTransform;
            if (gaugeRoot == null)
            {
                GameObject rootObject = new GameObject("WorldGaugeRoot", typeof(RectTransform));
                gaugeRoot = rootObject.GetComponent<RectTransform>();
                gaugeRoot.SetParent(hudRect, false);
            }

            gaugeRoot.anchorMin = Vector2.zero;
            gaugeRoot.anchorMax = Vector2.one;
            gaugeRoot.offsetMin = Vector2.zero;
            gaugeRoot.offsetMax = Vector2.zero;
            gaugeRoot.pivot = new Vector2(0.5f, 0.5f);
            gaugeRoot.localScale = Vector3.one;
            gaugeRoot.localRotation = Quaternion.identity;
            gaugeRoot.SetAsFirstSibling();
            return gaugeRoot;
        }

        Canvas canvas = FindObjectOfType<Canvas>(true);
        return canvas != null ? canvas.transform as RectTransform : null;
    }

    private void Awake()
    {
        EnsureReferencesUpToDate();
        Hide();
    }

    private void OnDestroy()
    {
        KillHideTween();
        KillAllDotTweens();
    }

    public void Bind(Resource resource)
    {
        targetResource = resource;

        if (targetResource == null)
        {
            Hide();
            return;
        }

        EnsureReferencesUpToDate();
        SetGaugeVisible(true);
        Refresh();
    }

    public void Hide()
    {
        targetResource = null;
        KillHideTween();
        EnsureReferencesUpToDate();
        SetGaugeVisible(false);
    }

    public void HideIfNotFinishing()
    {
        if (IsFinishingHideAnimation)
        {
            return;
        }

        Hide();
    }

    private void Refresh()
    {
        if (targetResource == null)
        {
            SetGaugeVisible(false);
            return;
        }

        EnsureDotCapacity(targetResource.MaxGauge);
        EnsureReferencesUpToDate();

        int maxGauge = Mathf.Clamp(targetResource.MaxGauge, 1, gaugeDots.Count);
        int currentGauge = Mathf.Clamp(targetResource.CurrentGauge, 0, maxGauge);

        for (int i = 0; i < gaugeDots.Count; i++)
        {
            bool isParticipating = i < maxGauge;
            SetDotParticipation(i, isParticipating);

            bool shouldBeVisible = isParticipating && i < currentGauge;
            bool animateHide = isParticipating && gaugeDotVisibilityStates[i] && !shouldBeVisible;
            SetDotVisible(i, shouldBeVisible, animateHide);
        }

        if (!targetResource.CanHarvest && currentGauge <= 0)
        {
            HideAfterDelay(dotPopDuration + dotFadeDuration);
        }
        else
        {
            KillHideTween();
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(transform as RectTransform);
    }

    private void EnsureReferencesUpToDate()
    {
        canvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        backgroundImage ??= GetComponent<Image>();

        if (backgroundImage == null)
        {
            backgroundImage = gameObject.AddComponent<Image>();
        }

        backgroundImage.color = fallbackBackgroundColor;

        EnsureDotSlotsExist();

        int childCount = transform.childCount;
        if (childCount == cachedChildCount && gaugeDots.Count == childCount)
        {
            return;
        }

        gaugeDots.Clear();
        gaugeDotLayoutElements.Clear();
        gaugeDotVisualCanvasGroups.Clear();
        gaugeDotVisualRectTransforms.Clear();
        gaugeDotVisibilityStates.Clear();
        dotList.Clear();

        for (int i = 0; i < childCount; i++)
        {
            Transform slotTransform = transform.GetChild(i);
            GameObject slotObject = slotTransform.gameObject;
            gaugeDots.Add(slotObject);

            LayoutElement layoutElement = slotObject.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = slotObject.AddComponent<LayoutElement>();
            }

            gaugeDotLayoutElements.Add(layoutElement);

            Image visualImage = ResolveVisualImage(slotObject);
            if (visualImage == null)
            {
                continue;
            }

            dotList.Add(visualImage);

            CanvasGroup visualCanvasGroup = visualImage.GetComponent<CanvasGroup>();
            if (visualCanvasGroup == null)
            {
                visualCanvasGroup = visualImage.gameObject.AddComponent<CanvasGroup>();
            }

            RectTransform visualRectTransform = visualImage.rectTransform;
            gaugeDotVisualCanvasGroups.Add(visualCanvasGroup);
            gaugeDotVisualRectTransforms.Add(visualRectTransform);
            gaugeDotVisibilityStates.Add(visualCanvasGroup.alpha > 0.99f);
        }

        cachedChildCount = childCount;
    }

    private void EnsureDotSlotsExist()
    {
        if (transform.childCount > 0)
        {
            return;
        }

        CreateDotSlots(Mathf.Max(1, fallbackDotCount));
    }

    private void EnsureDotCapacity(int requiredCount)
    {
        int missingCount = Mathf.Max(0, requiredCount - transform.childCount);
        if (missingCount <= 0)
        {
            return;
        }

        CreateDotSlots(missingCount);
        cachedChildCount = -1;
    }

    private void CreateDotSlots(int count)
    {
        int startIndex = transform.childCount;
        for (int i = 0; i < count; i++)
        {
            GameObject slotObject = new GameObject($"GaugeDot_Work ({startIndex + i})", typeof(RectTransform), typeof(LayoutElement));
            RectTransform slotRect = slotObject.GetComponent<RectTransform>();
            slotRect.SetParent(transform, false);
            slotRect.localScale = Vector3.one;

            LayoutElement layoutElement = slotObject.GetComponent<LayoutElement>();
            layoutElement.minWidth = fallbackDotSize.x;
            layoutElement.minHeight = fallbackDotSize.y;
            layoutElement.preferredWidth = fallbackDotSize.x;
            layoutElement.preferredHeight = fallbackDotSize.y;

            GameObject visualObject = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform visualRect = visualObject.GetComponent<RectTransform>();
            visualRect.SetParent(slotRect, false);
            visualRect.anchorMin = Vector2.zero;
            visualRect.anchorMax = Vector2.one;
            visualRect.offsetMin = Vector2.zero;
            visualRect.offsetMax = Vector2.zero;
            visualRect.localScale = Vector3.one;

            Image visualImage = visualObject.GetComponent<Image>();
            visualImage.color = fallbackDotColor;
            visualImage.raycastTarget = false;
        }
    }

    private Image ResolveVisualImage(GameObject slotObject)
    {
        Transform visualTransform = slotObject.transform.Find("Visual");
        if (visualTransform != null)
        {
            Image visualChildImage = visualTransform.GetComponent<Image>();
            if (visualChildImage != null)
            {
                return visualChildImage;
            }
        }

        Image[] childImages = slotObject.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < childImages.Length; i++)
        {
            if (childImages[i].gameObject != slotObject)
            {
                return childImages[i];
            }
        }

        Image rootImage = slotObject.GetComponent<Image>();
        if (rootImage != null)
        {
            return ConvertRootImageToVisual(slotObject, rootImage);
        }

        return CreateFallbackVisual(slotObject);
    }

    private Image ConvertRootImageToVisual(GameObject slotObject, Image rootImage)
    {
        Image visualImage = CreateFallbackVisual(slotObject);
        visualImage.sprite = rootImage.sprite;
        visualImage.material = rootImage.material;
        visualImage.type = rootImage.type;
        visualImage.preserveAspect = rootImage.preserveAspect;
        visualImage.fillCenter = rootImage.fillCenter;
        visualImage.fillMethod = rootImage.fillMethod;
        visualImage.fillAmount = rootImage.fillAmount;
        visualImage.fillClockwise = rootImage.fillClockwise;
        visualImage.fillOrigin = rootImage.fillOrigin;
        visualImage.useSpriteMesh = rootImage.useSpriteMesh;
        visualImage.pixelsPerUnitMultiplier = rootImage.pixelsPerUnitMultiplier;
        visualImage.color = rootImage.color;

        rootImage.enabled = false;
        rootImage.raycastTarget = false;
        return visualImage;
    }

    private Image CreateFallbackVisual(GameObject slotObject)
    {
        GameObject visualObject = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform visualRect = visualObject.GetComponent<RectTransform>();
        RectTransform slotRect = slotObject.GetComponent<RectTransform>();
        visualRect.SetParent(slotRect, false);
        visualRect.anchorMin = Vector2.zero;
        visualRect.anchorMax = Vector2.one;
        visualRect.offsetMin = Vector2.zero;
        visualRect.offsetMax = Vector2.zero;
        visualRect.localScale = Vector3.one;

        Image visualImage = visualObject.GetComponent<Image>();
        visualImage.color = fallbackDotColor;
        visualImage.raycastTarget = false;
        return visualImage;
    }

    private void SetDotParticipation(int index, bool isParticipating)
    {
        if (index < 0 || index >= gaugeDotLayoutElements.Count)
        {
            return;
        }

        LayoutElement layoutElement = gaugeDotLayoutElements[index];
        layoutElement.ignoreLayout = !isParticipating;

        if (isParticipating)
        {
            layoutElement.minWidth = -1f;
            layoutElement.minHeight = -1f;
            layoutElement.preferredWidth = -1f;
            layoutElement.preferredHeight = -1f;
            return;
        }

        layoutElement.minWidth = 0f;
        layoutElement.minHeight = 0f;
        layoutElement.preferredWidth = 0f;
        layoutElement.preferredHeight = 0f;
    }

    private void SetDotVisible(int index, bool isVisible, bool animateOnHide)
    {
        if (index < 0 || index >= gaugeDotVisualCanvasGroups.Count || index >= gaugeDotVisualRectTransforms.Count)
        {
            return;
        }

        if (gaugeDotVisibilityStates[index] == isVisible)
        {
            return;
        }

        CanvasGroup visualCanvasGroup = gaugeDotVisualCanvasGroups[index];
        RectTransform visualRectTransform = gaugeDotVisualRectTransforms[index];
        DOTween.Kill(visualCanvasGroup);
        DOTween.Kill(visualRectTransform);

        if (isVisible)
        {
            visualRectTransform.localScale = Vector3.one;
            visualCanvasGroup.alpha = 1f;
            gaugeDotVisibilityStates[index] = true;
            return;
        }

        gaugeDotVisibilityStates[index] = false;

        if (!animateOnHide)
        {
            visualRectTransform.localScale = Vector3.one * dotShrinkScale;
            visualCanvasGroup.alpha = 0f;
            return;
        }

        visualRectTransform.localScale = Vector3.one;
        visualCanvasGroup.alpha = 1f;

        Sequence sequence = DOTween.Sequence();
        sequence.SetTarget(visualRectTransform);
        sequence.Append(visualRectTransform.DOScale(dotPopScale, dotPopDuration).SetEase(Ease.OutQuad));
        sequence.Append(visualRectTransform.DOScale(dotShrinkScale, dotFadeDuration).SetEase(Ease.InQuad));
        sequence.Join(visualCanvasGroup.DOFade(0f, dotFadeDuration).SetEase(Ease.InQuad));
    }

    private void SetGaugeVisible(bool isVisible)
    {
        if (canvasGroup == null)
        {
            return;
        }

        if (isVisible)
        {
            KillHideTween();
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = fallbackBackgroundColor;
        }

        canvasGroup.alpha = isVisible ? 1f : 0f;
        canvasGroup.interactable = isVisible;
        canvasGroup.blocksRaycasts = isVisible;
    }

    private void HideAfterDelay(float delay)
    {
        if (canvasGroup == null)
        {
            return;
        }

        KillHideTween();
        hideTween = DOVirtual.DelayedCall(Mathf.Max(0f, delay), () =>
        {
            targetResource = null;
            SetGaugeVisible(false);
            hideTween = null;
        }).SetUpdate(true);
    }

    private void KillHideTween()
    {
        if (hideTween == null)
        {
            return;
        }

        hideTween.Kill();
        hideTween = null;
    }

    private void KillAllDotTweens()
    {
        for (int i = 0; i < gaugeDotVisualCanvasGroups.Count; i++)
        {
            DOTween.Kill(gaugeDotVisualCanvasGroups[i]);
        }

        for (int i = 0; i < gaugeDotVisualRectTransforms.Count; i++)
        {
            DOTween.Kill(gaugeDotVisualRectTransforms[i]);
        }
    }
}
