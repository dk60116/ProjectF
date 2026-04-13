using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BagSlot : ItemSlot, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private static BagSlot expandedSlot;

    [SerializeField, Range(0.1f, 1f)]
    private float draggingSlotAlpha = 0.6f;

    [SerializeField, Range(0.5f, 1.5f)]
    private float dragGhostScale = 0.95f;

    [SerializeField, Min(30f)]
    private float craftingRadius = 210f;

    [SerializeField, Range(30f, 180f)]
    private float craftingArcAngle = 180f;

    [SerializeField, Min(0f)]
    private float craftingExpandStepDelay = 0.04f;

    private PlayerBag boundBag;
    private int slotIndex = -1;
    private RectTransform rectTransform;
    private RectTransform craftingRoot;
    private RectTransform dragGhostTransform;
    private Image dragGhostImage;
    private Button button;
    private CanvasGroup canvasGroup;
    private bool isDragging;
    private bool ignoreNextClick;
    private bool isCraftingExpanded;
    private bool suppressCraftingEvents;

    [SerializeField]
    List<CraftingSlot> craftingSlots;

    public event Action<BagSlot, bool> CraftingVisibilityChanged;

    private void Awake()
    {
        CacheReferences();
        CollapseCraftingSlots(true);
        BindButtonClick();
    }

    private void OnDisable()
    {
        EndDragVisual();
        CollapseCraftingSlots(true);

        if (expandedSlot == this)
        {
            expandedSlot = null;
        }
    }

    public void Bind(PlayerBag bag, int index, int itemId, int itemCount, int maxItemCount)
    {
        boundBag = bag;
        slotIndex = index;

        if (itemId < 0 || itemCount <= 0)
        {
            Clear();
            if (!isCraftingExpanded)
            {
                CollapseCraftingSlots(true);
            }
            return;
        }

        SetItem(itemId, itemCount, maxItemCount);
    }

    public void SetSlotVisible(bool visible)
    {
        CacheReferences();
        if (visible && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = visible;
            canvasGroup.interactable = visible;
        }

        if (button != null)
        {
            button.interactable = visible;
        }
    }

    public void DropItem()
    {
        if (!CanDragItem())
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return;
        }

        TerrainGenerator terrainGenerator = UnityEngine.Object.FindObjectOfType<TerrainGenerator>();
        if (terrainGenerator == null)
        {
            return;
        }

        if (!boundBag.TryRemoveAllAtSlot(slotIndex, out int itemId, out int removedCount, out Vector3 startWorldPosition))
        {
            return;
        }

        if (!terrainGenerator.TryAddDroppedItemStackAtPlayerBlock(
                GameManager.Instance.Player.transform.position,
                itemId,
                removedCount,
                startWorldPosition,
                0.1f))
        {
            for (int i = 0; i < removedCount; i++)
            {
                if (!boundBag.TryAddObject(slotIndex, itemId, out _))
                {
                    break;
                }
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!CanDragItem())
        {
            return;
        }

        CacheReferences();
        EnsureDragGhost();

        isDragging = true;
        UpdateDragGhost(eventData);

        if (dragGhostTransform != null)
        {
            dragGhostTransform.gameObject.SetActive(true);
        }

        ignoreNextClick = true;
        CollapseCraftingSlots(false);
        SetIconAlpha(draggingSlotAlpha);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        UpdateDragGhost(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        EndDragVisual();
        if (ShouldCancelDrop(eventData))
        {
            return;
        }

        DropItem();
    }

    public bool IsCraftingExpanded => isCraftingExpanded;

    private bool CanDragItem()
    {
        return boundBag != null
               && slotIndex >= 0
               && id >= 0
               && boundBag.GetSlotCount(slotIndex) > 0;
    }

    private bool CanOpenCraftingSlots()
    {
        return boundBag != null
               && slotIndex >= 0
               && craftingRoot != null
               && craftingSlots != null
               && craftingSlots.Count > 0;
    }

    private void CacheReferences()
    {
        if (rectTransform == null)
        {
            rectTransform = transform as RectTransform;
        }

        if (craftingRoot == null)
        {
            Transform openTransform = transform.Find("Open");
            craftingRoot = openTransform as RectTransform;
            if (craftingRoot != null)
            {
                craftingRoot.gameObject.SetActive(true);
                craftingRoot.anchorMin = new Vector2(0.5f, 0.5f);
                craftingRoot.anchorMax = new Vector2(0.5f, 0.5f);
                craftingRoot.pivot = new Vector2(0.5f, 0.5f);
                craftingRoot.anchoredPosition = Vector2.zero;
                craftingRoot.localRotation = Quaternion.identity;
                craftingRoot.localScale = Vector3.one;
            }
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private void EnsureDragGhost()
    {
        CacheReferences();
        Canvas canvas = GetComponentInParent<Canvas>();
        Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
        if (rootCanvas == null)
        {
            return;
        }

        if (dragGhostTransform == null)
        {
            GameObject ghostObject = new GameObject($"{name}_DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            dragGhostTransform = ghostObject.GetComponent<RectTransform>();
            dragGhostTransform.SetParent(rootCanvas.transform, false);
            dragGhostTransform.SetAsLastSibling();
            dragGhostImage = ghostObject.GetComponent<Image>();
            dragGhostImage.raycastTarget = false;
            ghostObject.GetComponent<CanvasGroup>().blocksRaycasts = false;
        }

        if (dragGhostImage == null && dragGhostTransform != null)
        {
            dragGhostImage = dragGhostTransform.GetComponent<Image>();
        }

        if (dragGhostImage != null)
        {
            dragGhostImage.sprite = ResolveCurrentIcon();
            dragGhostImage.color = Color.white;
            dragGhostImage.enabled = dragGhostImage.sprite != null;
        }

        if (dragGhostTransform != null && rectTransform != null)
        {
            dragGhostTransform.sizeDelta = rectTransform.rect.size;
            dragGhostTransform.localScale = Vector3.one * dragGhostScale;
        }
    }

    private Sprite ResolveCurrentIcon()
    {
        if (id < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        return GameManager.Instance.ItemManger.TryGetItemSetById(id, out ItemManager.ItemSet itemSet)
            ? itemSet.icon
            : null;
    }

    private void UpdateDragGhost(PointerEventData eventData)
    {
        if (dragGhostTransform == null)
        {
            return;
        }

        dragGhostTransform.position = eventData.position;
    }

    private void EndDragVisual()
    {
        isDragging = false;

        SetIconAlpha(1f);

        if (dragGhostTransform != null)
        {
            dragGhostTransform.gameObject.SetActive(false);
        }
    }

    private void ExpandCraftingSlots()
    {
        CacheReferences();
        if (craftingSlots == null || craftingSlots.Count == 0 || rectTransform == null || craftingRoot == null)
        {
            return;
        }

        if (isCraftingExpanded)
        {
            return;
        }

        craftingRoot.gameObject.SetActive(true);

        Vector2 center = Vector2.zero;
        int slotCount = 0;
        for (int i = 0; i < craftingSlots.Count; i++)
        {
            if (craftingSlots[i] != null)
            {
                slotCount++;
            }
        }

        if (slotCount == 0)
        {
            return;
        }

        List<Vector2> targetPositions = new List<Vector2>(slotCount);
        int visibleIndex = 0;
        float startAngle = 90f;
        float endAngle = -90f;
        float step = slotCount > 1 ? (startAngle - endAngle) / (slotCount - 1) : 0f;

        for (int i = 0; i < craftingSlots.Count; i++)
        {
            if (craftingSlots[i] == null)
            {
                continue;
            }

            float angle = startAngle - (step * visibleIndex);
            float radians = angle * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(
                Mathf.Cos(radians) * craftingRadius,
                Mathf.Sin(radians) * craftingRadius);

            targetPositions.Add(center + offset);
            visibleIndex++;
        }

        if (targetPositions.Count == 0)
        {
            return;
        }

        Vector2 startPosition = targetPositions[0];

        visibleIndex = 0;
        for (int i = 0; i < craftingSlots.Count; i++)
        {
            CraftingSlot craftingSlot = craftingSlots[i];
            if (craftingSlot == null)
            {
                continue;
            }

            if (!craftingSlot.gameObject.activeSelf)
            {
                craftingSlot.gameObject.SetActive(true);
            }

            RectTransform craftingRect = craftingSlot.transform as RectTransform;
            if (craftingRect != null && craftingRect.parent != craftingRoot)
            {
                craftingRect.SetParent(craftingRoot, false);
            }

            if (craftingRect != null)
            {
                craftingRect.anchorMin = new Vector2(0.5f, 0.5f);
                craftingRect.anchorMax = new Vector2(0.5f, 0.5f);
                craftingRect.pivot = new Vector2(0.5f, 0.5f);
                craftingRect.localRotation = Quaternion.identity;
                craftingRect.localScale = Vector3.one;
            }

            craftingSlot.Show(
                startPosition,
                targetPositions[visibleIndex],
                visibleIndex * Mathf.Max(0f, craftingExpandStepDelay));
            visibleIndex++;
        }

        isCraftingExpanded = true;
        NotifyCraftingVisibilityChanged(true);
    }

    private void ToggleCraftingSlots()
    {
        if (ignoreNextClick)
        {
            ignoreNextClick = false;
            return;
        }

        if (!CanOpenCraftingSlots())
        {
            CollapseCraftingSlots(false);
            return;
        }

        if (expandedSlot != null && expandedSlot != this)
        {
            expandedSlot.CollapseCraftingSlots(false);
        }

        if (isCraftingExpanded)
        {
            CollapseCraftingSlots(false);
            if (expandedSlot == this)
            {
                expandedSlot = null;
            }
            return;
        }

        ExpandCraftingSlots();
        expandedSlot = this;
    }

    public void CloseCraftingSlots(bool immediate = false)
    {
        CollapseCraftingSlots(immediate);
    }

    public bool ContainsUiObject(GameObject targetObject)
    {
        if (targetObject == null)
        {
            return false;
        }

        if (targetObject.GetComponentInParent<BagSlot>() == this)
        {
            return true;
        }

        CraftingSlot craftingSlot = targetObject.GetComponentInParent<CraftingSlot>();
        if (craftingSlot == null || craftingSlots == null)
        {
            return false;
        }

        for (int i = 0; i < craftingSlots.Count; i++)
        {
            if (craftingSlots[i] == craftingSlot)
            {
                return true;
            }
        }

        return false;
    }

    private void CollapseCraftingSlots(bool immediate)
    {
        if (craftingSlots == null || craftingRoot == null)
        {
            isCraftingExpanded = false;
            NotifyCraftingVisibilityChanged(false);
            return;
        }

        if (!isCraftingExpanded && !craftingRoot.gameObject.activeSelf)
        {
            craftingRoot.gameObject.SetActive(true);
        }

        if (suppressCraftingEvents)
        {
            return;
        }

        suppressCraftingEvents = true;

        for (int i = 0; i < craftingSlots.Count; i++)
        {
            CraftingSlot craftingSlot = craftingSlots[i];
            if (craftingSlot == null)
            {
                continue;
            }

            if (immediate)
            {
                craftingSlot.HideImmediate();
            }
            else
            {
                craftingSlot.Hide();
            }
        }

        // Keep the root active and only hide visuals so it can reopen reliably.
        if (!craftingRoot.gameObject.activeSelf)
        {
            craftingRoot.gameObject.SetActive(true);
        }

        isCraftingExpanded = false;

        if (expandedSlot == this)
        {
            expandedSlot = null;
        }

        suppressCraftingEvents = false;
        NotifyCraftingVisibilityChanged(false);
    }

    private void SetIconAlpha(float alpha)
    {
        Image iconImage = IconImage;
        if (iconImage == null)
        {
            return;
        }

        Color color = iconImage.color;
        color.a = Mathf.Clamp01(alpha);
        iconImage.color = color;
    }

    private Vector2 GetCanvasAnchoredCenter()
    {
        return Vector2.zero;
    }

    private static bool ShouldCancelDrop(PointerEventData eventData)
    {
        if (eventData == null)
        {
            return false;
        }

        GameObject hoveredObject = eventData.pointerCurrentRaycast.gameObject;
        if (hoveredObject == null)
        {
            return false;
        }

        return hoveredObject.GetComponentInParent<BagSlot>() != null;
    }

    private void NotifyCraftingVisibilityChanged(bool isVisible)
    {
        if (suppressCraftingEvents)
        {
            return;
        }

        CraftingVisibilityChanged?.Invoke(this, isVisible);
    }

    private void BindButtonClick()
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(ToggleCraftingSlots);
        button.onClick.AddListener(ToggleCraftingSlots);
    }
}
