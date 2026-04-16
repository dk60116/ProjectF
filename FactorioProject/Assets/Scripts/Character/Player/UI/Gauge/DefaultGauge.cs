using UnityEngine;
using UnityEngine.UI;

public class DefaultGauge : MonoBehaviour
{
    [SerializeField]
    private Image bg;
    [SerializeField]
    private Image fill;

    private RectTransform cachedRectTransform;

    private void Awake()
    {
        ResolveReferences();
    }

    public void SetFill(float amount)
    {
        ResolveReferences();
        if (fill == null)
        {
            return;
        }

        fill.fillAmount = Mathf.Clamp01(amount);
    }

    public void SetAnchoredPosition(Vector2 anchoredPosition)
    {
        ResolveReferences();
        if (cachedRectTransform == null)
        {
            return;
        }

        cachedRectTransform.anchoredPosition = anchoredPosition;
    }

    public void SetVisible(bool isVisible)
    {
        if (gameObject.activeSelf != isVisible)
        {
            gameObject.SetActive(isVisible);
        }
    }

    public void ResetVisual()
    {
        ResolveReferences();
        if (cachedRectTransform != null)
        {
            cachedRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            cachedRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cachedRectTransform.pivot = new Vector2(0.5f, 0.5f);
            cachedRectTransform.anchoredPosition = Vector2.zero;
            cachedRectTransform.localScale = Vector3.one;
            cachedRectTransform.localRotation = Quaternion.identity;
        }

        SetFill(1f);
    }

    private void ResolveReferences()
    {
        if (cachedRectTransform == null)
        {
            cachedRectTransform = transform as RectTransform;
        }

        if (bg == null)
        {
            bg = GetComponent<Image>();
        }

        if (fill == null)
        {
            fill = transform.Find("Fill")?.GetComponent<Image>();
            if (fill == null)
            {
                fill = GetComponentInChildren<Image>(true);
            }
        }
    }
}
