using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
public class Joystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField]
    private Image background;

    [SerializeField]
    private Image handle;

    [SerializeField]
    private float handleRange = 100f;

    public Vector2 InputDirection { get; private set; }

    private RectTransform rootRect;
    private RectTransform backgroundRect;
    private RectTransform handleRect;
    private Canvas rootCanvas;
    private Image inputCatcher;
    private Vector2 joystickCenter;

    private void Awake()
    {
        rootRect = transform as RectTransform;

        if (background == null)
        {
            background = GetComponentInChildren<Image>();
        }

        if (handle == null && transform.childCount > 0)
        {
            handle = transform.GetChild(transform.childCount - 1).GetComponent<Image>();
        }

        inputCatcher = GetComponent<Image>();
        backgroundRect = background != null ? background.rectTransform : null;
        handleRect = handle != null ? handle.rectTransform : null;
        rootCanvas = GetComponentInParent<Canvas>();

        ConfigureTouchArea();
        ResetHandle();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!TryGetRootLocalPoint(eventData, out Vector2 localPoint))
        {
            return;
        }

        joystickCenter = localPoint;
        SetVisualPosition(localPoint);
        SetVisualActive(true);
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (backgroundRect == null || handleRect == null || !TryGetRootLocalPoint(eventData, out Vector2 localPoint))
        {
            return;
        }

        Vector2 offset = localPoint - joystickCenter;
        Vector2 radius = backgroundRect.sizeDelta * 0.5f;
        Vector2 normalized = new Vector2(offset.x / radius.x, offset.y / radius.y);
        InputDirection = Vector2.ClampMagnitude(normalized, 1f);
        handleRect.anchoredPosition = joystickCenter + (InputDirection * handleRange);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResetHandle();
    }

    public void ResetInput()
    {
        ResetHandle();
    }

    private void ResetHandle()
    {
        InputDirection = Vector2.zero;
        joystickCenter = Vector2.zero;

        SetVisualPosition(Vector2.zero);
        if (handleRect != null)
        {
            handleRect.anchoredPosition = Vector2.zero;
        }

        SetVisualActive(false);
    }

    private void ConfigureTouchArea()
    {
        if (rootRect != null)
        {
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
        }

        if (inputCatcher != null)
        {
            inputCatcher.color = new Color(1f, 1f, 1f, 0f);
            inputCatcher.raycastTarget = true;
        }
    }

    private bool TryGetRootLocalPoint(PointerEventData eventData, out Vector2 localPoint)
    {
        Camera eventCamera = eventData.pressEventCamera;

        if (rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            eventCamera = null;
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, eventData.position, eventCamera, out localPoint);
    }

    private void SetVisualPosition(Vector2 anchoredPosition)
    {
        if (backgroundRect != null)
        {
            backgroundRect.anchoredPosition = anchoredPosition;
        }

        if (handleRect != null)
        {
            handleRect.anchoredPosition = anchoredPosition;
        }
    }

    private void SetVisualActive(bool isActive)
    {
        if (background != null)
        {
            background.enabled = isActive;
        }

        if (handle != null)
        {
            handle.enabled = isActive;
        }
    }
}
