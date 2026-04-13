using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class CraftingSlot : MonoBehaviour
{
    [SerializeField]
    private float expandDuration = 0.2f;

    [SerializeField]
    private float collapseDuration = 0.12f;

    [SerializeField]
    private Ease expandEase = Ease.OutBack;

    [SerializeField]
    private Ease collapseEase = Ease.InBack;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Button button;

    private void Awake()
    {
        CacheReferences();
        HideImmediate();
    }

    public void Show(Vector2 startAnchoredPosition, Vector2 targetAnchoredPosition, float delay = 0f)
    {
        CacheReferences();

        rectTransform.DOKill();
        canvasGroup.DOKill();

        rectTransform.anchoredPosition = startAnchoredPosition;
        rectTransform.localScale = Vector3.zero;
        canvasGroup.alpha = 1f;
        rectTransform.DOAnchorPos(targetAnchoredPosition, expandDuration).SetDelay(delay).SetEase(expandEase);
        rectTransform.DOScale(Vector3.one, expandDuration).SetDelay(delay).SetEase(expandEase);
        canvasGroup.DOFade(1f, expandDuration * 0.8f).SetDelay(delay).SetEase(Ease.OutQuad);
        button.interactable = true;
    }

    public void Hide()
    {
        CacheReferences();

        rectTransform.DOKill();
        canvasGroup.DOKill();

        button.interactable = false;
        canvasGroup.DOFade(0f, collapseDuration)
            .SetEase(Ease.OutQuad);
        rectTransform.DOScale(Vector3.zero, collapseDuration).SetEase(collapseEase);
    }

    public void HideImmediate()
    {
        CacheReferences();
        rectTransform.DOKill();
        canvasGroup.DOKill();
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.zero;
        canvasGroup.alpha = 0f;
        button.interactable = false;
    }

    private void CacheReferences()
    {
        if (rectTransform == null)
        {
            rectTransform = transform as RectTransform;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }
    }
}
