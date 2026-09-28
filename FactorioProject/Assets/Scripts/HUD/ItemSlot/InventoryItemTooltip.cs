using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InventoryItemTooltip : MonoBehaviour
{
    private static InventoryItemTooltip active;

    [SerializeField]
    private RectTransform tooltipRoot;

    [SerializeField]
    private Text label;

    [SerializeField]
    private Vector2 cursorOffset = new Vector2(18f, -18f);

    [SerializeField]
    private Vector2 padding = new Vector2(12f, 8f);

    private RectTransform canvasRect;
    private Canvas targetCanvas;
    private ItemSlot owner;
    private int ownerItemId = -1;
    private EventSystem pointerEventSystem;
    private PointerEventData pointerEventData;
    private readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>(16);

    private void Awake()
    {
        if (active != null && active != this)
        {
            Debug.LogError("[InventoryTooltip] 중복 인스턴스가 감지되었습니다.");
            enabled = false;
            return;
        }

        active = this;
        canvasRect = transform as RectTransform;
        targetCanvas = GetComponent<Canvas>();
        HideImmediate();
    }

    private void OnDestroy()
    {
        if (active == this)
        {
            active = null;
        }
    }

    private void LateUpdate()
    {
        Vector2 screenPosition = Input.mousePosition;
        if (!TryResolvePointerTarget(screenPosition, out ItemSlot pointerOwner, out int pointerItemId))
        {
            HideImmediate();
            return;
        }

        if (owner != pointerOwner
            || ownerItemId != pointerItemId
            || tooltipRoot == null
            || !tooltipRoot.gameObject.activeSelf)
        {
            Show(pointerOwner, pointerItemId, screenPosition);
            if (owner != pointerOwner || ownerItemId != pointerItemId)
            {
                HideImmediate();
                return;
            }
        }

        UpdatePosition(screenPosition);
    }

    public static void Show(ItemSlot source, int itemId, Vector2 screenPosition)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        string itemName = null;
        if (itemId >= 0 && itemManager != null)
        {
            if (itemManager.TryGetItemDefinitionById(itemId, out ItemDefinition definition)
                && definition != null)
            {
                itemName = !string.IsNullOrWhiteSpace(definition.itemName)
                    ? definition.itemName
                    : definition.name;
            }
            else if (itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
            {
                itemName = itemSet.name;
            }
        }

        if (active == null
            || source == null
            || string.IsNullOrWhiteSpace(itemName)
            || active.tooltipRoot == null
            || active.label == null)
        {
            Hide(source);
            return;
        }

        active.owner = source;
        active.ownerItemId = itemId;
        active.label.text = itemName;
        active.tooltipRoot.sizeDelta = new Vector2(
            Mathf.Ceil(active.label.preferredWidth) + (active.padding.x * 2f),
            Mathf.Ceil(active.label.preferredHeight) + (active.padding.y * 2f));
        if (!active.tooltipRoot.gameObject.activeSelf)
        {
            active.tooltipRoot.gameObject.SetActive(true);
        }

        active.tooltipRoot.SetAsLastSibling();
        active.UpdatePosition(screenPosition);
    }

    public static void Refresh(ItemSlot source, int itemId)
    {
        if (active != null && active.owner == source)
        {
            Show(source, itemId, Input.mousePosition);
        }
    }

    public static void Hide(ItemSlot source)
    {
        if (active == null || (source != null && active.owner != source))
        {
            return;
        }

        active.HideImmediate();
    }

    private void HideImmediate()
    {
        owner = null;
        ownerItemId = -1;
        if (tooltipRoot != null && tooltipRoot.gameObject.activeSelf)
        {
            tooltipRoot.gameObject.SetActive(false);
        }
    }

    private bool TryResolvePointerTarget(
        Vector2 screenPosition,
        out ItemSlot pointerOwner,
        out int pointerItemId)
    {
        pointerOwner = null;
        pointerItemId = -1;

        EventSystem currentEventSystem = EventSystem.current;
        if (currentEventSystem == null)
        {
            return false;
        }

        if (pointerEventData == null || pointerEventSystem != currentEventSystem)
        {
            pointerEventSystem = currentEventSystem;
            pointerEventData = new PointerEventData(currentEventSystem);
        }

        pointerEventData.Reset();
        pointerEventData.position = screenPosition;
        pointerRaycastResults.Clear();
        currentEventSystem.RaycastAll(pointerEventData, pointerRaycastResults);
        if (pointerRaycastResults.Count == 0)
        {
            return false;
        }

        GameObject hitObject = pointerRaycastResults[0].gameObject;
        pointerRaycastResults.Clear();
        if (hitObject == null)
        {
            return false;
        }

        ItemSlot itemSlot = hitObject.GetComponentInParent<ItemSlot>();
        if (itemSlot == null
            || !itemSlot.isActiveAndEnabled
            || !itemSlot.TryGetTooltipItemId(hitObject.transform, out pointerItemId))
        {
            return false;
        }

        pointerOwner = itemSlot;
        return true;
    }

    private void UpdatePosition(Vector2 screenPosition)
    {
        if (tooltipRoot == null
            || canvasRect == null
            || targetCanvas == null
            || !tooltipRoot.gameObject.activeSelf)
        {
            return;
        }

        Camera eventCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : targetCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition,
                eventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        Rect canvasBounds = canvasRect.rect;
        Vector2 anchorReference = new Vector2(
            canvasBounds.xMin + (canvasBounds.width * tooltipRoot.anchorMin.x),
            canvasBounds.yMin + (canvasBounds.height * tooltipRoot.anchorMin.y));
        Vector2 desiredTopLeft = localPoint + cursorOffset;
        desiredTopLeft.x = Mathf.Clamp(
            desiredTopLeft.x,
            canvasBounds.xMin,
            canvasBounds.xMax - tooltipRoot.rect.width);
        desiredTopLeft.y = Mathf.Clamp(
            desiredTopLeft.y,
            canvasBounds.yMin + tooltipRoot.rect.height,
            canvasBounds.yMax);
        tooltipRoot.anchoredPosition = desiredTopLeft - anchorReference;
    }
}
