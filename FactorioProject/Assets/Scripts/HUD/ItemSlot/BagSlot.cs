using System.Collections.Generic;
using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BagSlot : ItemSlot, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private static BagSlot expandedSlot;
    private const float DragCancelDistance = 8f;
    private const float CraftingRootHideDelay = 0.12f;

    [SerializeField, Range(0.1f, 1f)]
    private float draggingSlotAlpha = 0.6f;

    [SerializeField, Range(0.5f, 1.5f)]
    private float dragGhostScale = 0.95f;

    [SerializeField, Min(0f)]
    private float transferMoveInterval = 0.1f;

    [SerializeField, Min(30f)]
    private float craftingRadius = 210f;

    [SerializeField, Range(30f, 180f)]
    private float craftingArcAngle = 180f;

    [SerializeField, Min(0f)]
    private float craftingExpandStepDelay = 0.04f;

    [SerializeField, Min(0.5f)]
    private float requiredCraftingMapObjectRange = 2f;

    [SerializeField]
    private bool enablePickupOnClick = true;

    [SerializeField, Min(0)]
    private int pickupRadius = 2;

    [SerializeField, Min(0.01f)]
    private float pickupInterval = 0.1f;

    [SerializeField]
    private Button pickupButton;

    private PlayerBag boundBag;
    private int slotIndex = -1;
    private RectTransform rectTransform;
    private RectTransform craftingRoot;
    private RectTransform dragGhostTransform;
    private Image dragGhostImage;
    private Button button;
    private CanvasGroup canvasGroup;
    private bool isDragging;
    private int ignoreNextClickFrame = -1;
    private bool isCraftingExpanded;
    private bool suppressCraftingEvents;
    private Vector2 dragStartScreenPosition;
    private int lastBoundItemId = -1;
    private int lastBoundItemCount;
    private Coroutine pickupRoutine;
    private Tween craftingRootHideTween;

    private readonly List<int> craftableItems = new List<int>();
    private readonly List<int> requiredCraftingMapObjectIds = new List<int>();

    [SerializeField]
    List<CraftingSlot> craftingSlots;

    public event Action<BagSlot, bool> CraftingVisibilityChanged;

    public static BagSlot ExpandedSlot => expandedSlot;

    public static void CloseAnyExpanded(bool immediate = false)
    {
        if (expandedSlot == null)
        {
            return;
        }

        expandedSlot.CloseCraftingSlots(immediate);
        expandedSlot = null;
    }

    private void Awake()
    {
        CacheReferences();
        CollapseCraftingSlots(true);
        BindPickupClick();
        BindButtonClick();
    }

    private void OnDisable()
    {
        EndDragVisual();
        CollapseCraftingSlots(true);
        StopPickupRoutine();

        if (expandedSlot == this)
        {
            expandedSlot = null;
        }
    }

    private void OnDestroy()
    {
        UnbindPickupClick();
    }

    public void Bind(PlayerBag bag, int index, int itemId, int itemCount, int maxItemCount)
    {
        boundBag = bag;
        slotIndex = index;

        if (itemId < 0 || itemCount <= 0)
        {
            Clear();
            RefreshCraftingItems(itemId, itemCount);
            if (isCraftingExpanded)
            {
                CollapseCraftingSlots(true);
            }
            return;
        }

        SetItem(itemId, itemCount, maxItemCount);
        RefreshCraftingItems(itemId, itemCount);
    }

    public void SetSlotVisible(bool visible)
    {
        CacheReferences();
        if (visible && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        bool canInteract = visible && !IsInventoryEditLocked();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = canInteract;
            canvasGroup.interactable = canInteract;
        }

        if (button != null)
        {
            button.interactable = canInteract;
        }

        Button targetPickupButton = ResolvePickupButton();
        if (targetPickupButton != null)
        {
            targetPickupButton.interactable = canInteract;
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

        Vector3 dropOrigin = GameManager.Instance.Player.transform.position;

        bool dropped = terrainGenerator.TryAddDroppedItemStackAtPlayerBlock(
            dropOrigin,
            itemId,
            removedCount,
            startWorldPosition,
            0.1f,
            out Vector2Int dropCoordinate);

        if (dropped)
        {
            GameManager.Instance.Player.MarkDropExitGate(dropOrigin, 0.5f);
            GameManager.Instance.Player.SetLastDropTarget(dropCoordinate);
        }
        else
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
        if (!IsDragSourceBagSlot(eventData))
        {
            return;
        }

        if (!CanDragItem())
        {
            return;
        }

        CacheReferences();
        EnsureDragGhost();

        isDragging = true;
        dragStartScreenPosition = eventData != null ? eventData.position : Vector2.zero;
        UpdateDragGhost(eventData);

        if (dragGhostTransform != null)
        {
            dragGhostTransform.gameObject.SetActive(true);
        }

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

        if (eventData != null)
        {
            float dragDistance = Vector2.Distance(dragStartScreenPosition, eventData.position);
            ignoreNextClickFrame = dragDistance > DragCancelDistance ? Time.frameCount : -1;
        }
        else
        {
            ignoreNextClickFrame = -1;
        }

        EndDragVisual();
        BagSlot dropSlot = GetDropTargetSlot(eventData);
        if (dropSlot == this)
        {
            return;
        }
        if (dropSlot != null && dropSlot != this)
        {
            if (TryTransferToSlot(dropSlot))
            {
                return;
            }
        }

        if (ShouldCancelDrop(eventData))
        {
            return;
        }

        DropItem();
    }

    public bool IsCraftingExpanded => isCraftingExpanded;
    public override bool CanDragDrop => true;

    private bool CanDragItem()
    {
        return !IsInventoryEditLocked()
               && CanDragDrop
               && boundBag != null
               && slotIndex >= 0
               && id >= 0
               && boundBag.GetSlotCount(slotIndex) > 0;
    }

    private bool CanOpenCraftingSlots()
    {
        return !IsInventoryEditLocked()
               && boundBag != null
               && slotIndex >= 0
               && craftingRoot != null
               && craftingSlots != null
               && craftingSlots.Count > 0
               && HasAnyCraftingItems();
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

    private void ExpandCraftingSlots(bool force = false)
    {
        CacheReferences();
        if (craftingSlots == null || craftingSlots.Count == 0 || rectTransform == null || craftingRoot == null)
        {
            return;
        }

        if (isCraftingExpanded && !force)
        {
            return;
        }

        ShowCraftingRoot();

        List<CraftingSlot> visibleSlots = GetOrderedCraftingSlots(true);
        List<CraftingSlot> allSlots = GetOrderedCraftingSlots(false);
        Vector2 center = Vector2.zero;
        int slotCount = visibleSlots.Count;
        int spacingSlotCount = allSlots.Count;

        if (slotCount == 0)
        {
            if (isCraftingExpanded)
            {
                CollapseCraftingSlots(true);
            }
            return;
        }

        List<Vector2> targetPositions = new List<Vector2>(slotCount);
        int visibleIndex = 0;
        float startAngle = 90f;
        float step = spacingSlotCount > 1 ? craftingArcAngle / (spacingSlotCount - 1) : 0f;
        int directionSign = GetCraftingDirectionSign();

        for (int i = 0; i < visibleSlots.Count; i++)
        {
            float angle = startAngle + (step * visibleIndex * directionSign);
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
        for (int i = 0; i < visibleSlots.Count; i++)
        {
            CraftingSlot craftingSlot = visibleSlots[i];

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

        bool wasExpanded = isCraftingExpanded;
        isCraftingExpanded = true;
        if (!wasExpanded)
        {
            NotifyCraftingVisibilityChanged(true);
        }
    }

    private void ToggleCraftingSlots()
    {
        if (ignoreNextClickFrame == Time.frameCount)
        {
            ignoreNextClickFrame = -1;
            return;
        }

        if (IsInventoryEditLocked())
        {
            CollapseCraftingSlots(false);
            return;
        }

        RefreshCraftingItemsFromBag(true);

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

    public void RefreshCraftingAvailability()
    {
        RefreshCraftingItemsFromBag(true);
    }

    public bool CanCraftItem(int itemId)
    {
        return CanShowCraftingItem(itemId);
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

        isCraftingExpanded = false;

        if (expandedSlot == this)
        {
            expandedSlot = null;
        }

        suppressCraftingEvents = false;
        NotifyCraftingVisibilityChanged(false);
        HideCraftingRoot(immediate);
    }

    private void RefreshCraftingItemsFromBag(bool force)
    {
        int currentItemId = id;
        int currentCount = lastBoundItemCount;

        if (boundBag != null && slotIndex >= 0)
        {
            currentItemId = boundBag.GetSlotItemId(slotIndex);
            currentCount = boundBag.GetSlotCount(slotIndex);
        }

        if (currentItemId < 0 || currentCount <= 0)
        {
            RefreshCraftingItems(-1, 0, force);
            return;
        }

        RefreshCraftingItems(currentItemId, currentCount, force);
    }

    private void RefreshCraftingItems(int itemId, int itemCount, bool force = false)
    {
        bool selectedItemChangedWhileExpanded = isCraftingExpanded
                                               && lastBoundItemId >= 0
                                               && (itemId < 0 || itemCount <= 0 || itemId != lastBoundItemId);
        bool wasEmpty = lastBoundItemId < 0 || lastBoundItemCount <= 0;
        bool isEmpty = itemId < 0 || itemCount <= 0;
        bool shouldRefresh = force || wasEmpty != isEmpty || (!isEmpty && itemId != lastBoundItemId);

        lastBoundItemId = itemId;
        lastBoundItemCount = itemCount;

        if (selectedItemChangedWhileExpanded)
        {
            CollapseCraftingSlots(true);
        }

        if (!shouldRefresh)
        {
            return;
        }

        if (isEmpty)
        {
            craftableItems.Clear();
            ClearCraftingSlots();
            if (isCraftingExpanded)
            {
                CollapseCraftingSlots(true);
            }
            return;
        }

        craftableItems.Clear();
        CraftingTreeRuntime.TryGetCraftableItemIds(itemId, craftableItems);
        ApplyCraftingItems();
    }

    private void ApplyCraftingItems()
    {
        if (craftingSlots == null)
        {
            return;
        }

        List<CraftingSlot> orderedSlots = GetOrderedCraftingSlots(false);
        int craftIndex = 0;
        for (int i = 0; i < orderedSlots.Count; i++)
        {
            CraftingSlot craftingSlot = orderedSlots[i];
            if (craftingSlot == null)
            {
                continue;
            }

            if (craftIndex < craftableItems.Count)
            {
                craftingSlot.SetItem(craftableItems[craftIndex], 1, 0);
                craftIndex++;
            }
            else
            {
                craftingSlot.Clear();
                craftingSlot.HideImmediate();
            }
        }

        if (isCraftingExpanded)
        {
            RefreshExpandedCraftingSlotsImmediate();
        }
    }

    private bool CanShowCraftingItem(int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        requiredCraftingMapObjectIds.Clear();
        if (!CraftingTreeRuntime.TryGetRequiredCraftingMapObjectIds(itemId, requiredCraftingMapObjectIds))
        {
            return true;
        }

        Player player = ResolvePlayer();
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController != null && playerController.HasFocusedWorkableObject(requiredCraftingMapObjectIds))
        {
            return true;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null ? player.BodyTransform.position : player.transform.position;
        float radius = Mathf.Max(0.5f, requiredCraftingMapObjectRange);
        float radiusSqr = radius * radius;
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(radius));
        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(origin.x),
            Mathf.RoundToInt(origin.z));

        for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
        {
            for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null)
                {
                    continue;
                }

                MapObject mapObject = block.MapObject;
                if (!(mapObject is WorkableObject workableObject) || workableObject == null || !workableObject.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int mapObjectId = workableObject.ResolveItemId();
                if (mapObjectId < 0 || !requiredCraftingMapObjectIds.Contains(mapObjectId))
                {
                    continue;
                }

                Vector3 offset = block.transform.position - origin;
                offset.y = 0f;
                if (offset.sqrMagnitude <= radiusSqr)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RefreshExpandedCraftingSlotsImmediate()
    {
        CacheReferences();
        if (craftingSlots == null || craftingSlots.Count == 0 || rectTransform == null || craftingRoot == null)
        {
            return;
        }

        ShowCraftingRoot();

        List<CraftingSlot> visibleSlots = GetOrderedCraftingSlots(true);
        List<CraftingSlot> allSlots = GetOrderedCraftingSlots(false);
        int slotCount = visibleSlots.Count;
        int spacingSlotCount = allSlots.Count;

        if (slotCount == 0)
        {
            CollapseCraftingSlots(true);
            return;
        }

        Vector2 center = Vector2.zero;
        float startAngle = 90f;
        float step = spacingSlotCount > 1 ? craftingArcAngle / (spacingSlotCount - 1) : 0f;
        int directionSign = GetCraftingDirectionSign();

        for (int i = 0; i < visibleSlots.Count; i++)
        {
            CraftingSlot craftingSlot = visibleSlots[i];
            if (craftingSlot == null)
            {
                continue;
            }

            float angle = startAngle + (step * i * directionSign);
            float radians = angle * Mathf.Deg2Rad;
            Vector2 targetPosition = center + new Vector2(
                Mathf.Cos(radians) * craftingRadius,
                Mathf.Sin(radians) * craftingRadius);

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
            }

            craftingSlot.ShowImmediate(targetPosition);
        }
    }

    private void ClearCraftingSlots()
    {
        if (craftingSlots == null)
        {
            return;
        }

        for (int i = 0; i < craftingSlots.Count; i++)
        {
            CraftingSlot craftingSlot = craftingSlots[i];
            if (craftingSlot == null)
            {
                continue;
            }

            craftingSlot.Clear();
            craftingSlot.HideImmediate();
        }
    }

    private void ShowCraftingRoot()
    {
        if (craftingRoot == null)
        {
            return;
        }

        craftingRootHideTween?.Kill();
        craftingRootHideTween = null;
        if (!craftingRoot.gameObject.activeSelf)
        {
            craftingRoot.gameObject.SetActive(true);
        }
    }

    private void HideCraftingRoot(bool immediate)
    {
        if (craftingRoot == null)
        {
            return;
        }

        craftingRootHideTween?.Kill();
        craftingRootHideTween = null;

        if (immediate || !gameObject.activeInHierarchy)
        {
            if (craftingRoot.gameObject.activeSelf)
            {
                craftingRoot.gameObject.SetActive(false);
            }
            return;
        }

        craftingRootHideTween = DOVirtual.DelayedCall(CraftingRootHideDelay, () =>
        {
            craftingRootHideTween = null;
            if (craftingRoot == null || isCraftingExpanded)
            {
                return;
            }

            if (craftingRoot.gameObject.activeSelf)
            {
                craftingRoot.gameObject.SetActive(false);
            }
        }).SetUpdate(true);
    }

    private bool HasAnyCraftingItems()
    {
        if (craftingSlots == null)
        {
            return false;
        }

        for (int i = 0; i < craftingSlots.Count; i++)
        {
            CraftingSlot craftingSlot = craftingSlots[i];
            if (craftingSlot != null && craftingSlot.HasItem)
            {
                return true;
            }
        }

        return false;
    }

    private List<CraftingSlot> GetOrderedCraftingSlots(bool onlyWithItem)
    {
        List<CraftingSlot> results = new List<CraftingSlot>();

        if (craftingSlots == null)
        {
            return results;
        }

        for (int i = 0; i < craftingSlots.Count; i++)
        {
            CraftingSlot craftingSlot = craftingSlots[i];
            if (craftingSlot == null)
            {
                continue;
            }

            if (onlyWithItem && !craftingSlot.HasItem)
            {
                continue;
            }

            results.Add(craftingSlot);
        }

        results.Sort((left, right) => GetCraftingSlotSortKey(left).CompareTo(GetCraftingSlotSortKey(right)));
        return results;
    }

    private static int GetCraftingSlotSortKey(CraftingSlot craftingSlot)
    {
        if (craftingSlot == null)
        {
            return int.MaxValue;
        }

        string slotName = craftingSlot.name;
        if (!string.IsNullOrWhiteSpace(slotName))
        {
            int openIndex = slotName.LastIndexOf('(');
            int closeIndex = slotName.LastIndexOf(')');
            if (openIndex >= 0 && closeIndex > openIndex)
            {
                string numberText = slotName.Substring(openIndex + 1, closeIndex - openIndex - 1);
                if (int.TryParse(numberText, out int parsed))
                {
                    return parsed;
                }
            }
        }

        return craftingSlot.transform.GetSiblingIndex();
    }

    protected virtual int GetCraftingDirectionSign()
    {
        return -1;
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

        BagSlot hoveredSlot = hoveredObject.GetComponentInParent<BagSlot>();
        if (hoveredSlot == null)
        {
            return false;
        }

        return hoveredSlot.ShouldCancelDropForDrag(eventData);
    }

    private bool ShouldCancelDropForDrag(PointerEventData eventData)
    {
        if (eventData == null)
        {
            return false;
        }

        if (eventData.pointerCurrentRaycast.gameObject == null)
        {
            return false;
        }

        float distance = Vector2.Distance(dragStartScreenPosition, eventData.position);
        return distance <= DragCancelDistance;
    }

    private bool IsDragSourceBagSlot(PointerEventData eventData)
    {
        if (eventData == null)
        {
            return false;
        }

        GameObject target = eventData.pointerPressRaycast.gameObject;
        if (target == null)
        {
            target = eventData.pointerCurrentRaycast.gameObject;
        }

        if (target == null)
        {
            return true;
        }

        if (target.GetComponentInParent<CraftingSlot>() != null)
        {
            return false;
        }

        BagSlot parentSlot = target.GetComponentInParent<BagSlot>();
        return parentSlot == this;
    }

    private BagSlot GetDropTargetSlot(PointerEventData eventData)
    {
        if (eventData == null)
        {
            return null;
        }

        GameObject target = eventData.pointerCurrentRaycast.gameObject;
        if (target == null)
        {
            target = eventData.pointerPressRaycast.gameObject;
        }

        if (target == null)
        {
            return null;
        }

        return target.GetComponentInParent<BagSlot>();
    }

    private bool TryTransferToSlot(BagSlot targetSlot)
    {
        if (IsInventoryEditLocked() || targetSlot == null || targetSlot == this)
        {
            return false;
        }

        if (boundBag == null || slotIndex < 0)
        {
            return false;
        }

        PlayerBag targetBag = targetSlot.boundBag;
        int targetIndex = targetSlot.slotIndex;
        if (targetBag == null || targetIndex < 0)
        {
            return false;
        }

        int sourceItemId = boundBag.GetSlotItemId(slotIndex);
        int sourceCount = boundBag.GetSlotCount(slotIndex);
        if (sourceItemId < 0 || sourceCount <= 0)
        {
            return false;
        }

        int targetItemId = targetBag.GetSlotItemId(targetIndex);
        int targetCount = targetBag.GetSlotCount(targetIndex);

        if (targetItemId < 0 || targetCount <= 0)
        {
            return TryMoveStack(boundBag, slotIndex, sourceItemId, sourceCount, targetBag, targetIndex);
        }

        if (targetItemId == sourceItemId)
        {
            int targetMax = targetBag.GetSlotMaxCount(targetIndex);
            if (targetMax - targetCount < sourceCount)
            {
                return false;
            }

            return TryMoveStack(boundBag, slotIndex, sourceItemId, sourceCount, targetBag, targetIndex);
        }

        int sourceMax = boundBag.GetSlotMaxCount(slotIndex);
        int targetMaxSwap = targetBag.GetSlotMaxCount(targetIndex);
        if (sourceMax < targetCount || targetMaxSwap < sourceCount)
        {
            return false;
        }

        return TrySwapStacks(boundBag, slotIndex, targetBag, targetIndex);
    }

    private bool TryMoveStack(PlayerBag sourceBag, int sourceIndex, int itemId, int itemCount, PlayerBag targetBag, int targetIndex)
    {
        if (sourceBag == null || targetBag == null || itemId < 0 || itemCount <= 0)
        {
            return false;
        }

        int targetCount = targetBag.GetSlotCount(targetIndex);

        List<PortableObject> sourceObjects = new List<PortableObject>();
        if (!sourceBag.TryGetOccupiedSlotObjects(sourceIndex, sourceObjects) || sourceObjects.Count < itemCount)
        {
            return false;
        }

        List<PortableObject> targetObjects = new List<PortableObject>();
        if (!targetBag.TryGetSlotObjects(targetIndex, targetCount, itemCount, targetObjects) || targetObjects.Count < itemCount)
        {
            return false;
        }

        List<Vector3> sourcePositions = new List<Vector3>(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            PortableObject sourceObject = sourceObjects[i];
            if (sourceObject == null)
            {
                return false;
            }

            sourcePositions.Add(sourceObject.transform.position);
        }

        for (int i = 0; i < itemCount; i++)
        {
            PortableObject targetObject = targetObjects[i];
            if (targetObject == null)
            {
                return false;
            }

            targetObject.gameObject.SetActive(false);
            targetObject.SetItem(itemId);
        }

        for (int i = 0; i < itemCount; i++)
        {
            PortableObject sourceObject = sourceObjects[i];
            if (sourceObject != null)
            {
                sourceObject.gameObject.SetActive(false);
            }
        }

        sourceBag.SetSlotCount(sourceIndex, 0, false);
        targetBag.SetSlotCount(targetIndex, targetCount + itemCount, false);

        int moveIndex = 0;
        float moveInterval = Mathf.Max(0f, transferMoveInterval);
        for (int i = 0; i < itemCount; i++)
        {
            PortableObject targetObject = targetObjects[i];
            if (targetObject == null)
            {
                continue;
            }

            Vector3 anchorPosition = targetObject.transform.position;
            AnimatePortableMove(targetObject, sourcePositions[i], anchorPosition, moveIndex * moveInterval);
            moveIndex++;
        }

        sourceBag.ForceNotifyChanged();
        targetBag.ForceNotifyChanged();
        return true;
    }

    private bool TrySwapStacks(
        PlayerBag sourceBag,
        int sourceIndex,
        PlayerBag targetBag,
        int targetIndex)
    {
        if (sourceBag == null || targetBag == null)
        {
            return false;
        }

        int sourceCount = sourceBag.GetSlotCount(sourceIndex);
        int targetCount = targetBag.GetSlotCount(targetIndex);
        int sourceItemId = sourceBag.GetSlotItemId(sourceIndex);
        int targetItemId = targetBag.GetSlotItemId(targetIndex);

        if (sourceCount <= 0 || targetCount <= 0 || sourceItemId < 0 || targetItemId < 0)
        {
            return false;
        }

        List<PortableObject> sourceObjects = new List<PortableObject>();
        if (!sourceBag.TryGetOccupiedSlotObjects(sourceIndex, sourceObjects) || sourceObjects.Count < sourceCount)
        {
            return false;
        }

        List<PortableObject> targetObjects = new List<PortableObject>();
        if (!targetBag.TryGetOccupiedSlotObjects(targetIndex, targetObjects) || targetObjects.Count < targetCount)
        {
            return false;
        }

        List<PortableObject> destinationForSource = new List<PortableObject>();
        if (!sourceBag.TryGetSlotObjects(sourceIndex, 0, targetCount, destinationForSource) || destinationForSource.Count < targetCount)
        {
            return false;
        }

        List<PortableObject> destinationForTarget = new List<PortableObject>();
        if (!targetBag.TryGetSlotObjects(targetIndex, 0, sourceCount, destinationForTarget) || destinationForTarget.Count < sourceCount)
        {
            return false;
        }

        List<Vector3> sourcePositions = new List<Vector3>(sourceCount);
        for (int i = 0; i < sourceCount; i++)
        {
            PortableObject sourceObject = sourceObjects[i];
            if (sourceObject == null)
            {
                return false;
            }

            sourcePositions.Add(sourceObject.transform.position);
        }

        List<Vector3> targetPositions = new List<Vector3>(targetCount);
        for (int i = 0; i < targetCount; i++)
        {
            PortableObject targetObject = targetObjects[i];
            if (targetObject == null)
            {
                return false;
            }

            targetPositions.Add(targetObject.transform.position);
        }

        for (int i = 0; i < sourceObjects.Count; i++)
        {
            PortableObject sourceObject = sourceObjects[i];
            if (sourceObject != null)
            {
                sourceObject.gameObject.SetActive(false);
            }
        }

        for (int i = 0; i < targetObjects.Count; i++)
        {
            PortableObject targetObject = targetObjects[i];
            if (targetObject != null)
            {
                targetObject.gameObject.SetActive(false);
            }
        }

        for (int i = 0; i < destinationForSource.Count; i++)
        {
            PortableObject destObject = destinationForSource[i];
            if (destObject == null)
            {
                return false;
            }

            destObject.gameObject.SetActive(false);
            destObject.SetItem(targetItemId);
        }

        for (int i = 0; i < destinationForTarget.Count; i++)
        {
            PortableObject destObject = destinationForTarget[i];
            if (destObject == null)
            {
                return false;
            }

            destObject.gameObject.SetActive(false);
            destObject.SetItem(sourceItemId);
        }

        sourceBag.SetSlotCount(sourceIndex, targetCount, false);
        targetBag.SetSlotCount(targetIndex, sourceCount, false);

        int moveIndex = 0;
        float moveInterval = Mathf.Max(0f, transferMoveInterval);
        for (int i = 0; i < destinationForSource.Count; i++)
        {
            PortableObject destObject = destinationForSource[i];
            if (destObject == null)
            {
                continue;
            }

            Vector3 anchorPosition = destObject.transform.position;
            Vector3 startPosition = targetPositions[Mathf.Min(i, targetPositions.Count - 1)];
            AnimatePortableMove(destObject, startPosition, anchorPosition, moveIndex * moveInterval);
            moveIndex++;
        }

        for (int i = 0; i < destinationForTarget.Count; i++)
        {
            PortableObject destObject = destinationForTarget[i];
            if (destObject == null)
            {
                continue;
            }

            Vector3 anchorPosition = destObject.transform.position;
            Vector3 startPosition = sourcePositions[Mathf.Min(i, sourcePositions.Count - 1)];
            AnimatePortableMove(destObject, startPosition, anchorPosition, moveIndex * moveInterval);
            moveIndex++;
        }

        sourceBag.ForceNotifyChanged();
        targetBag.ForceNotifyChanged();
        return true;
    }

    private void NotifyCraftingVisibilityChanged(bool isVisible)
    {
        if (suppressCraftingEvents)
        {
            return;
        }

        CraftingVisibilityChanged?.Invoke(this, isVisible);
    }

    private void AnimatePortableMove(PortableObject portableObject, Vector3 startPosition, Vector3 targetPosition, float delay)
    {
        if (portableObject == null)
        {
            return;
        }

        Transform originalParent = portableObject.transform.parent;
        Vector3 originalLocalPosition = portableObject.transform.localPosition;
        Quaternion originalLocalRotation = portableObject.transform.localRotation;
        Vector3 originalLocalScale = portableObject.transform.localScale;

        portableObject.transform.SetParent(null, true);
        portableObject.transform.position = startPosition;
        if (!portableObject.gameObject.activeSelf)
        {
            portableObject.gameObject.SetActive(true);
        }

        portableObject.MoveTo(targetPosition, delay, () =>
        {
            if (portableObject == null)
            {
                return;
            }

            portableObject.transform.SetParent(originalParent, false);
            portableObject.transform.localPosition = originalLocalPosition;
            portableObject.transform.localRotation = originalLocalRotation;
            portableObject.transform.localScale = originalLocalScale;
        }, false);
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

    private void BindPickupClick()
    {
        if (!AllowPickupOnClick)
        {
            return;
        }

        Button targetButton = ResolvePickupButton();
        if (targetButton == null)
        {
            return;
        }

        targetButton.onClick.RemoveListener(HandlePickupClick);
        targetButton.onClick.AddListener(HandlePickupClick);
    }

    private void UnbindPickupClick()
    {
        Button targetButton = ResolvePickupButton();
        if (targetButton == null)
        {
            return;
        }

        targetButton.onClick.RemoveListener(HandlePickupClick);
    }

    private Button ResolvePickupButton()
    {
        if (pickupButton != null)
        {
            return pickupButton;
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }

        return button;
    }

    private void HandlePickupClick()
    {
        if (!AllowPickupOnClick || IsInventoryEditLocked())
        {
            return;
        }

        Player player = ResolvePlayer();
        if (player == null)
        {
            return;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return;
        }

        StopPickupRoutine();
        pickupRoutine = StartCoroutine(PickupRoutine(player, terrain));
    }

    private void StopPickupRoutine()
    {
        if (pickupRoutine == null)
        {
            return;
        }

        StopCoroutine(pickupRoutine);
        pickupRoutine = null;
    }

    private IEnumerator PickupRoutine(Player player, TerrainGenerator terrain)
    {
        int radius = Mathf.Max(0, pickupRadius);
        float interval = Mathf.Max(0.01f, pickupInterval);

        while (player != null && terrain != null)
        {
            if (IsInventoryEditLocked())
            {
                break;
            }

            Vector3 pickupOrigin = ResolvePickupOrigin(player);
            Vector2Int currentCoordinate = ResolvePickupCoordinate(pickupOrigin);

            bool picked = TryPickupOneItemAtCoordinate(terrain, player, currentCoordinate);
            if (!picked)
            {
                picked = TryPickupOneItem(terrain, player, pickupOrigin, radius, pickupRadius);
            }

            if (!picked)
            {
                if (AllowDropTargetFallback && player.IsDropExitPending && player.TryGetLastDropTarget(out Vector2Int dropTarget))
                {
                    picked = TryPickupOneItemAtCoordinate(terrain, player, dropTarget);
                    if (!picked)
                    {
                        player.ClearLastDropTarget();
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            yield return new WaitForSeconds(interval);
        }

        pickupRoutine = null;
    }

    protected virtual bool AllowPickupOnClick => enablePickupOnClick;

    protected virtual bool AllowDropTargetFallback => true;

    protected virtual bool TryPickupOneItem(TerrainGenerator terrain, Player player, Vector3 pickupOrigin, int radius, float pickupRange)
    {
        if (terrain == null)
        {
            return false;
        }

        int targetSlotIndex = GetPickupSlotIndex();
        if (targetSlotIndex < 0)
        {
            return false;
        }

        return terrain.TryPickupOneItemToBag(player, pickupOrigin, radius, pickupRange, targetSlotIndex);
    }

    protected virtual bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate)
    {
        if (terrain == null)
        {
            return false;
        }

        int targetSlotIndex = GetPickupSlotIndex();
        if (targetSlotIndex < 0)
        {
            return false;
        }

        return terrain.TryPickupOneItemToBagAtCoordinate(player, coordinate, targetSlotIndex);
    }

    protected virtual int GetPickupSlotIndex()
    {
        if (boundBag == null || slotIndex < 0)
        {
            return -1;
        }

        return slotIndex;
    }

    private static Player ResolvePlayer()
    {
        if (GameManager.Instance != null && GameManager.Instance.Player != null)
        {
            return GameManager.Instance.Player;
        }

        return UnityEngine.Object.FindObjectOfType<Player>();
    }

    private static Vector3 ResolvePickupOrigin(Player player)
    {
        if (player == null)
        {
            return Vector3.zero;
        }

        Transform referenceTransform = player.BodyTransform != null
            ? player.BodyTransform
            : player.transform;

        return referenceTransform != null ? referenceTransform.position : Vector3.zero;
    }

    private static Vector2Int ResolvePickupCoordinate(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(worldPosition.z));
    }

    private static TerrainGenerator ResolveTerrain()
    {
        return UnityEngine.Object.FindObjectOfType<TerrainGenerator>();
    }

    protected bool IsInventoryEditLocked()
    {
        return GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked;
    }
}
