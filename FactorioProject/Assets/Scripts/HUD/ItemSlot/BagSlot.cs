using System.Collections.Generic;
using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BagSlot : ItemSlot, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    private static BagSlot expandedSlot;
    private static BagSlot hoveredDropSlot;
    private static readonly Dictionary<PortableObject, PortableMoveVisualState> activePortableMoveVisualStates = new Dictionary<PortableObject, PortableMoveVisualState>();
    private const float DragCancelDistance = 8f;
    private const float CraftingRootHideDelay = 0.12f;
    private const int CraftingInnerRingSlotLimit = 5;
    private const float CraftingOuterRingSlotPadding = 0.9f;

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
    private int suppressCraftingToggleFrame = -1;
    private bool isCraftingExpanded;
    private bool suppressCraftingEvents;
    private Vector2 dragStartScreenPosition;
    private int lastBoundItemId = -1;
    private int lastBoundItemCount;
    private Coroutine pickupRoutine;
    private Tween craftingRootHideTween;
    private float craftingExpandAnimationUntilTime;

    private readonly List<int> craftableItems = new List<int>();
    private readonly List<int> requiredCraftingMapObjectIds = new List<int>();

    [SerializeField]
    List<CraftingSlot> craftingSlots;

    public event Action<BagSlot, bool> CraftingVisibilityChanged;

    public static BagSlot ExpandedSlot => expandedSlot;

    private readonly struct PortableMoveVisualState
    {
        private readonly Transform parent;
        private readonly int siblingIndex;
        private readonly Vector3 localPosition;
        private readonly Quaternion localRotation;
        private readonly Vector3 localScale;

        public PortableMoveVisualState(Transform parent, int siblingIndex, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            this.parent = parent;
            this.siblingIndex = siblingIndex;
            this.localPosition = localPosition;
            this.localRotation = localRotation;
            this.localScale = localScale;
        }

        public void Restore(PortableObject portableObject)
        {
            if (portableObject == null)
            {
                return;
            }

            Transform targetTransform = portableObject.transform;
            targetTransform.SetParent(parent, false);
            if (parent != null)
            {
                int clampedSiblingIndex = Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, parent.childCount - 1));
                targetTransform.SetSiblingIndex(clampedSiblingIndex);
            }

            targetTransform.localPosition = localPosition;
            targetTransform.localRotation = localRotation;
            targetTransform.localScale = localScale;
        }
    }

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
        if (hoveredDropSlot == this)
        {
            hoveredDropSlot = null;
        }

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
        EndDragVisual();
        DestroyDragGhost();
        UnbindPickupClick();
    }

    public void Bind(PlayerBag bag, int index, int itemId, int itemCount, int maxItemCount, bool allowZeroCountDisplay = false)
    {
        boundBag = bag;
        slotIndex = index;

        bool shouldDisplayItem = itemId >= 0 && (itemCount > 0 || allowZeroCountDisplay);
        if (!shouldDisplayItem)
        {
            Clear();
            RefreshCraftingItems(itemId, itemCount);
            if (isCraftingExpanded)
            {
                CollapseCraftingSlots(true);
            }
            return;
        }

        SetItemDisplay(itemId, itemCount, maxItemCount, allowZeroCountDisplay);
        RefreshCraftingItems(itemId, itemCount);
    }

    public void SetSlotVisible(bool visible)
    {
        CacheReferences();
        if (visible && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        bool canInteract = visible && !IsInventoryUiLocked();
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
        if (IsItemDropLocked())
        {
            return;
        }

        if (!CanDragItem())
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return;
        }

        TerrainGenerator terrainGenerator = ResolveTerrain();
        if (terrainGenerator == null)
        {
            return;
        }

        Player player = GameManager.Instance.Player;
        Transform movingStartTransform = null;
        if (player != null)
        {
            movingStartTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
        }

        Vector3 startWorldPosition = movingStartTransform != null ? movingStartTransform.position : player.transform.position;
        Func<Vector3> startWorldPositionProvider = movingStartTransform != null
            ? () => movingStartTransform != null ? movingStartTransform.position : startWorldPosition
            : null;

        Vector3 dropOrigin = player != null ? player.transform.position : startWorldPosition;
        bool isFocusedConveyorDrop = terrainGenerator.TryGetFocusedConveyorDropLimit(out int conveyorDropLimit);
        if (isFocusedConveyorDrop)
        {
            int conveyorItemId = boundBag.GetSlotItemId(slotIndex);
            int conveyorSlotCount = boundBag.GetSlotCount(slotIndex);
            int conveyorDropCount = Mathf.Min(conveyorSlotCount, conveyorDropLimit);
            if (conveyorItemId < 0 || conveyorDropCount <= 0)
            {
                return;
            }

            bool conveyorDropped = terrainGenerator.TryAddDroppedItemStackAtPlayerBlock(
                dropOrigin,
                conveyorItemId,
                conveyorDropCount,
                startWorldPosition,
                startWorldPositionProvider,
                0.1f,
                out Vector2Int conveyorDropCoordinate,
                out int conveyorDroppedCount);

            if (!conveyorDropped || conveyorDroppedCount <= 0)
            {
                return;
            }

            if (!boundBag.TryRemoveItemsAtSlot(slotIndex, conveyorDroppedCount, out int removedItemId, out int conveyorRemovedCount, out _, false)
                || removedItemId != conveyorItemId
                || conveyorRemovedCount <= 0)
            {
                return;
            }

            player.MarkDropExitGate(dropOrigin, 0.5f);
            player.SetLastDropTarget(conveyorDropCoordinate);
            return;
        }

        if (!boundBag.TryRemoveAllAtSlot(slotIndex, out int itemId, out int removedCount, out startWorldPosition))
        {
            return;
        }

        bool dropped = terrainGenerator.TryAddDroppedItemStackAtPlayerBlock(
            dropOrigin,
            itemId,
            removedCount,
            startWorldPosition,
            startWorldPositionProvider,
            0.1f,
            out Vector2Int dropCoordinate,
            out int droppedCount);

        if (dropped && droppedCount > 0)
        {
            player.MarkDropExitGate(dropOrigin, 0.5f);
            player.SetLastDropTarget(dropCoordinate);
        }

        int remainingCount = Mathf.Max(0, removedCount - droppedCount);
        if (remainingCount > 0)
        {
            for (int i = 0; i < remainingCount; i++)
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

    public void OnPointerEnter(PointerEventData eventData)
    {
        hoveredDropSlot = this;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoveredDropSlot == this)
        {
            hoveredDropSlot = null;
        }
    }

    private void Update()
    {
        bool dropRequested = Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(1);
        if (hoveredDropSlot != this
            || isDragging
            || !dropRequested
            || !CanDragItem())
        {
            return;
        }

        DropItem();
    }

    public bool IsCraftingExpanded => isCraftingExpanded;
    public override bool CanDragDrop => true;

    private bool CanDragItem()
    {
        return CanDragDrop
               && boundBag != null
               && slotIndex >= 0
               && id >= 0
               && boundBag.GetSlotCount(slotIndex) > 0;
    }

    private bool CanOpenCraftingSlots()
    {
        return !IsInventoryUiLocked()
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

        RefreshCraftingSlotReferences();

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
        if (dragGhostTransform == null || eventData == null)
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

    private void DestroyDragGhost()
    {
        if (dragGhostTransform == null)
        {
            dragGhostImage = null;
            return;
        }

        GameObject ghostObject = dragGhostTransform.gameObject;
        dragGhostTransform = null;
        dragGhostImage = null;
        if (ghostObject != null)
        {
            Destroy(ghostObject);
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
        int slotCount = visibleSlots.Count;

        if (slotCount == 0)
        {
            if (isCraftingExpanded)
            {
                CollapseCraftingSlots(true);
            }
            return;
        }

        List<Vector2> targetPositions = BuildCraftingTargetPositions(slotCount, allSlots.Count);
        if (targetPositions.Count == 0)
        {
            return;
        }

        Vector2 startPosition = targetPositions[0];
        float expandStepDelay = Mathf.Max(0f, craftingExpandStepDelay);
        float latestDelay = slotCount > 0 ? (slotCount - 1) * expandStepDelay : 0f;
        float longestExpandDuration = GetLongestCraftingSlotExpandDuration(visibleSlots);
        craftingExpandAnimationUntilTime = Time.unscaledTime + latestDelay + longestExpandDuration;

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
                targetPositions[i],
                i * expandStepDelay);
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

        if (suppressCraftingToggleFrame == Time.frameCount)
        {
            suppressCraftingToggleFrame = -1;
            return;
        }

        if (IsInventoryUiLocked())
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
            if (IsCraftingExpandAnimationPlaying())
            {
                return;
            }

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

        if (slotCount == 0)
        {
            CollapseCraftingSlots(true);
            return;
        }

        List<Vector2> targetPositions = BuildCraftingTargetPositions(slotCount, allSlots.Count);

        for (int i = 0; i < visibleSlots.Count; i++)
        {
            CraftingSlot craftingSlot = visibleSlots[i];
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
            }

            craftingSlot.ShowImmediate(targetPositions[i]);
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

    private void RefreshCraftingSlotReferences()
    {
        if (craftingRoot == null)
        {
            return;
        }

        if (craftingSlots == null)
        {
            craftingSlots = new List<CraftingSlot>();
        }

        for (int i = craftingSlots.Count - 1; i >= 0; i--)
        {
            if (craftingSlots[i] == null)
            {
                craftingSlots.RemoveAt(i);
            }
        }

        CraftingSlot[] rootSlots = craftingRoot.GetComponentsInChildren<CraftingSlot>(true);
        for (int i = 0; i < rootSlots.Length; i++)
        {
            CraftingSlot craftingSlot = rootSlots[i];
            if (craftingSlot == null || craftingSlots.Contains(craftingSlot))
            {
                continue;
            }

            craftingSlots.Add(craftingSlot);
        }

        craftingSlots.Sort((left, right) => GetCraftingSlotSortKey(left).CompareTo(GetCraftingSlotSortKey(right)));
    }

    private List<Vector2> BuildCraftingTargetPositions(int visibleSlotCount, int totalSlotCount)
    {
        List<Vector2> positions = new List<Vector2>(Mathf.Max(0, visibleSlotCount));
        if (visibleSlotCount <= 0)
        {
            return positions;
        }

        int spacingSlotCount = Mathf.Max(visibleSlotCount, totalSlotCount);
        int innerSlotCount = Mathf.Min(CraftingInnerRingSlotLimit, spacingSlotCount);
        int outerSlotCount = Mathf.Max(0, spacingSlotCount - innerSlotCount);
        int directionSign = GetCraftingDirectionSign();

        for (int i = 0; i < visibleSlotCount; i++)
        {
            positions.Add(GetCraftingTargetPosition(i, innerSlotCount, outerSlotCount, directionSign));
        }

        return positions;
    }

    private Vector2 GetCraftingTargetPosition(int slotIndex, int innerSlotCount, int outerSlotCount, int directionSign)
    {
        if (slotIndex < innerSlotCount || outerSlotCount <= 0)
        {
            float innerStep = innerSlotCount > 1 ? craftingArcAngle / (innerSlotCount - 1) : 0f;
            float innerAngle = 90f + (innerStep * slotIndex * directionSign);
            return GetCraftingOffset(innerAngle, craftingRadius);
        }

        int outerIndex = slotIndex - innerSlotCount;
        float innerRingStep = innerSlotCount > 1 ? craftingArcAngle / (innerSlotCount - 1) : 0f;
        float outerAngleOffset = outerSlotCount == innerSlotCount - 1 && innerRingStep > 0f
            ? innerRingStep * (outerIndex + 0.5f)
            : craftingArcAngle / (outerSlotCount + 1) * (outerIndex + 1);
        float outerAngle = 90f + (outerAngleOffset * directionSign);
        return GetCraftingOffset(outerAngle, GetCraftingOuterRingRadius());
    }

    private Vector2 GetCraftingOffset(float angle, float radius)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(
            Mathf.Cos(radians) * radius,
            Mathf.Sin(radians) * radius);
    }

    private float GetCraftingOuterRingRadius()
    {
        float slotDiameter = ResolveCraftingSlotDiameter();
        return Mathf.Max(craftingRadius, craftingRadius + (slotDiameter * CraftingOuterRingSlotPadding));
    }

    private float ResolveCraftingSlotDiameter()
    {
        if (craftingSlots != null)
        {
            for (int i = 0; i < craftingSlots.Count; i++)
            {
                CraftingSlot craftingSlot = craftingSlots[i];
                RectTransform slotRect = craftingSlot != null ? craftingSlot.transform as RectTransform : null;
                if (slotRect == null)
                {
                    continue;
                }

                Vector2 size = slotRect.rect.size;
                float diameter = Mathf.Max(size.x, size.y);
                if (diameter > 0f)
                {
                    return diameter;
                }
            }
        }

        return rectTransform != null ? Mathf.Max(rectTransform.rect.width, rectTransform.rect.height) : 100f;
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
            if (slotName.Equals("CraftingSlot", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

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

    private bool IsCraftingExpandAnimationPlaying()
    {
        return isCraftingExpanded && Time.unscaledTime < craftingExpandAnimationUntilTime;
    }

    private static float GetLongestCraftingSlotExpandDuration(List<CraftingSlot> slots)
    {
        if (slots == null || slots.Count == 0)
        {
            return 0f;
        }

        float duration = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            CraftingSlot craftingSlot = slots[i];
            if (craftingSlot == null)
            {
                continue;
            }

            duration = Mathf.Max(duration, craftingSlot.ExpandDuration);
        }

        return duration;
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
        if (IsInventoryUiLocked() || targetSlot == null || targetSlot == this)
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
            int targetMax = targetBag.GetSlotMaxCount(targetIndex);
            int moveCount = Mathf.Min(sourceCount, Mathf.Max(0, targetMax));
            return moveCount > 0 && TryMoveStack(boundBag, slotIndex, sourceItemId, moveCount, targetBag, targetIndex);
        }

        if (targetItemId == sourceItemId)
        {
            int targetMax = targetBag.GetSlotMaxCount(targetIndex);
            int moveCount = Mathf.Min(sourceCount, Mathf.Max(0, targetMax - targetCount));
            if (moveCount <= 0)
            {
                return false;
            }

            return TryMoveStack(boundBag, slotIndex, sourceItemId, moveCount, targetBag, targetIndex);
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

        int sourceCount = sourceBag.GetSlotCount(sourceIndex);
        int targetCount = targetBag.GetSlotCount(targetIndex);
        if (sourceCount < itemCount)
        {
            return false;
        }

        List<PortableObject> sourceObjects = new List<PortableObject>();
        if (!sourceBag.TryGetSlotObjects(sourceIndex, sourceCount - itemCount, itemCount, sourceObjects) || sourceObjects.Count < itemCount)
        {
            return false;
        }

        List<PortableObject> targetObjects = new List<PortableObject>();
        if (!targetBag.TryGetSlotObjects(targetIndex, targetCount, itemCount, targetObjects) || targetObjects.Count < itemCount)
        {
            return false;
        }

        CancelPortableMoveVisuals(sourceObjects);
        CancelPortableMoveVisuals(targetObjects);

        List<Vector3> sourcePositions = new List<Vector3>(itemCount);
        if (!TryCollectPortableObjectPositions(sourceObjects, sourcePositions))
        {
            return false;
        }

        List<Vector3> targetAnchorPositions = new List<Vector3>(itemCount);
        if (!TryCollectPortableObjectPositions(targetObjects, targetAnchorPositions))
        {
            return false;
        }

        if (!sourceBag.SetSlotContents(sourceIndex, itemId, sourceCount - itemCount, false, false)
            || !targetBag.SetSlotContents(targetIndex, itemId, targetCount + itemCount, false, false))
        {
            return false;
        }

        int moveIndex = 0;
        float moveInterval = Mathf.Max(0f, transferMoveInterval);
        AnimatePortableMoves(targetObjects, sourcePositions, targetAnchorPositions, ref moveIndex, moveInterval);

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

        CancelPortableMoveVisuals(sourceObjects);
        CancelPortableMoveVisuals(targetObjects);
        CancelPortableMoveVisuals(destinationForSource);
        CancelPortableMoveVisuals(destinationForTarget);

        List<Vector3> sourcePositions = new List<Vector3>(sourceCount);
        if (!TryCollectPortableObjectPositions(sourceObjects, sourcePositions))
        {
            return false;
        }

        List<Vector3> targetPositions = new List<Vector3>(targetCount);
        if (!TryCollectPortableObjectPositions(targetObjects, targetPositions))
        {
            return false;
        }

        List<Vector3> sourceAnchorPositions = new List<Vector3>(destinationForSource.Count);
        if (!TryCollectPortableObjectPositions(destinationForSource, sourceAnchorPositions))
        {
            return false;
        }

        List<Vector3> targetAnchorPositions = new List<Vector3>(destinationForTarget.Count);
        if (!TryCollectPortableObjectPositions(destinationForTarget, targetAnchorPositions))
        {
            return false;
        }

        if (!sourceBag.SetSlotContents(sourceIndex, targetItemId, targetCount, false, false)
            || !targetBag.SetSlotContents(targetIndex, sourceItemId, sourceCount, false, false))
        {
            return false;
        }

        int moveIndex = 0;
        float moveInterval = Mathf.Max(0f, transferMoveInterval);
        AnimatePortableMoves(destinationForSource, targetPositions, sourceAnchorPositions, ref moveIndex, moveInterval);
        AnimatePortableMoves(destinationForTarget, sourcePositions, targetAnchorPositions, ref moveIndex, moveInterval);

        sourceBag.ForceNotifyChanged();
        targetBag.ForceNotifyChanged();
        return true;
    }

    private static bool TryCollectPortableObjectPositions(List<PortableObject> portableObjects, List<Vector3> positions)
    {
        if (portableObjects == null || positions == null)
        {
            return false;
        }

        positions.Clear();
        for (int i = 0; i < portableObjects.Count; i++)
        {
            PortableObject portableObject = portableObjects[i];
            if (portableObject == null)
            {
                return false;
            }

            positions.Add(portableObject.transform.position);
        }

        return true;
    }

    private void AnimatePortableMoves(
        List<PortableObject> portableObjects,
        List<Vector3> startPositions,
        List<Vector3> anchorPositions,
        ref int moveIndex,
        float moveInterval)
    {
        if (portableObjects == null
            || startPositions == null
            || anchorPositions == null
            || startPositions.Count == 0
            || anchorPositions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < portableObjects.Count; i++)
        {
            PortableObject portableObject = portableObjects[i];
            if (portableObject == null)
            {
                continue;
            }

            Vector3 startPosition = startPositions[Mathf.Min(i, startPositions.Count - 1)];
            Vector3 anchorPosition = anchorPositions[Mathf.Min(i, anchorPositions.Count - 1)];
            AnimatePortableMove(portableObject, startPosition, anchorPosition, moveIndex * moveInterval);
            moveIndex++;
        }
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

        CancelPortableMoveVisual(portableObject);
        Transform originalParent = portableObject.transform.parent;
        int originalSiblingIndex = portableObject.transform.GetSiblingIndex();
        Vector3 originalLocalPosition = portableObject.transform.localPosition;
        Quaternion originalLocalRotation = portableObject.transform.localRotation;
        Vector3 originalLocalScale = portableObject.transform.localScale;
        activePortableMoveVisualStates[portableObject] = new PortableMoveVisualState(
            originalParent,
            originalSiblingIndex,
            originalLocalPosition,
            originalLocalRotation,
            originalLocalScale);
        portableObject.MoveCancelled -= RestorePortableMoveVisual;
        portableObject.MoveCancelled += RestorePortableMoveVisual;

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

            RestorePortableMoveVisual(portableObject);
        }, false);
    }

    private static void CancelPortableMoveVisual(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.CancelMove();
        RestorePortableMoveVisual(portableObject);
    }

    private static void CancelPortableMoveVisuals(List<PortableObject> portableObjects)
    {
        if (portableObjects == null)
        {
            return;
        }

        for (int i = 0; i < portableObjects.Count; i++)
        {
            CancelPortableMoveVisual(portableObjects[i]);
        }
    }

    private static void RestorePortableMoveVisual(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.MoveCancelled -= RestorePortableMoveVisual;
        if (!activePortableMoveVisualStates.TryGetValue(portableObject, out PortableMoveVisualState state))
        {
            return;
        }

        activePortableMoveVisualStates.Remove(portableObject);
        state.Restore(portableObject);
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
        if (!AllowPickupOnClick || IsInventoryUiLocked())
        {
            return;
        }

        Player player = ResolvePlayer();
        if (player == null)
        {
            return;
        }

        if (TryHandleFocusedRobotArmPickup(player, out bool blockPickup))
        {
            StopPickupRoutine();
            suppressCraftingToggleFrame = Time.frameCount;
            return;
        }

        if (blockPickup)
        {
            StopPickupRoutine();
            suppressCraftingToggleFrame = Time.frameCount;
            return;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return;
        }

        bool hasFocusedConveyor = TryGetFocusedConveyorBlock(player, out Block focusedConveyorBlock);
        if (hasFocusedConveyor
            && TryPickupFocusedConveyorItem(player, focusedConveyorBlock))
        {
            StopPickupRoutine();
            suppressCraftingToggleFrame = Time.frameCount;
            return;
        }

        StopPickupRoutine();
        bool allowFocusedConveyorPickup = !hasFocusedConveyor;
        if (!TryPickupOneItemForClick(player, terrain, allowFocusedConveyorPickup))
        {
            return;
        }

        suppressCraftingToggleFrame = Time.frameCount;
        pickupRoutine = StartCoroutine(PickupRoutine(player, terrain, true, allowFocusedConveyorPickup));
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

    private IEnumerator PickupRoutine(Player player, TerrainGenerator terrain, bool delayBeforeFirstPickup = false, bool allowFocusedConveyorPickup = true)
    {
        int radius = Mathf.Max(0, pickupRadius);
        float interval = Mathf.Max(0.01f, pickupInterval);

        if (delayBeforeFirstPickup)
        {
            yield return new WaitForSeconds(interval);
        }

        while (player != null && terrain != null)
        {
            if (IsInventoryUiLocked())
            {
                break;
            }

            Vector3 pickupOrigin = ResolvePickupOrigin(player);
            Vector2Int currentCoordinate = ResolvePickupCoordinate(pickupOrigin);

            bool picked = TryPickupOneItemAtCoordinate(terrain, player, currentCoordinate, allowFocusedConveyorPickup);
            if (!picked)
            {
                picked = TryPickupOneItem(terrain, player, pickupOrigin, radius, pickupRadius, allowFocusedConveyorPickup);
            }

            if (!picked)
            {
                if (AllowDropTargetFallback && player.IsDropExitPending && player.TryGetLastDropTarget(out Vector2Int dropTarget))
                {
                    picked = TryPickupOneItemAtCoordinate(terrain, player, dropTarget, allowFocusedConveyorPickup);
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

    private bool TryPickupOneItemForClick(Player player, TerrainGenerator terrain, bool allowFocusedConveyorPickup = true)
    {
        if (player == null || terrain == null)
        {
            return false;
        }

        Vector3 pickupOrigin = ResolvePickupOrigin(player);
        Vector2Int currentCoordinate = ResolvePickupCoordinate(pickupOrigin);

        if (TryPickupOneItemAtCoordinate(terrain, player, currentCoordinate, allowFocusedConveyorPickup))
        {
            return true;
        }

        if (TryPickupOneItem(terrain, player, pickupOrigin, Mathf.Max(0, pickupRadius), pickupRadius, allowFocusedConveyorPickup))
        {
            return true;
        }

        if (!AllowDropTargetFallback || !player.IsDropExitPending || !player.TryGetLastDropTarget(out Vector2Int dropTarget))
        {
            return false;
        }

        if (TryPickupOneItemAtCoordinate(terrain, player, dropTarget, allowFocusedConveyorPickup))
        {
            return true;
        }

        player.ClearLastDropTarget();
        return false;
    }

    protected virtual bool AllowPickupOnClick => enablePickupOnClick;

    protected virtual bool AllowDropTargetFallback => true;

    protected virtual bool TryPickupOneItem(TerrainGenerator terrain, Player player, Vector3 pickupOrigin, int radius, float pickupRange, bool allowFocusedConveyorPickup = true)
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

        return terrain.TryPickupOneItemToBag(
            player,
            pickupOrigin,
            radius,
            pickupRange,
            targetSlotIndex,
            GetPreferredPickupItemId(),
            allowFocusedConveyorPickup);
    }

    protected virtual bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate, bool allowFocusedConveyorPickup = true)
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

        return terrain.TryPickupOneItemToBagAtCoordinate(
            player,
            coordinate,
            targetSlotIndex,
            GetPreferredPickupItemId(),
            allowFocusedConveyorPickup);
    }

    protected virtual int GetPickupSlotIndex()
    {
        if (boundBag == null || slotIndex < 0)
        {
            return -1;
        }

        return slotIndex;
    }

    protected virtual int GetPreferredPickupItemId()
    {
        if (boundBag == null || slotIndex < 0 || boundBag.GetSlotCount(slotIndex) <= 0)
        {
            return -1;
        }

        return boundBag.GetSlotItemId(slotIndex);
    }

    protected virtual bool TryPickupFocusedConveyorItem(Player player, Block focusedConveyorBlock)
    {
        if (player == null || focusedConveyorBlock == null)
        {
            return false;
        }

        int targetSlotIndex = GetPickupSlotIndex();
        if (targetSlotIndex < 0)
        {
            return false;
        }

        return focusedConveyorBlock.TryPickupOneConveyorObjectToBag(
            player,
            player.transform.position,
            999f,
            targetSlotIndex,
            GetPreferredPickupItemId());
    }

    private static bool TryGetFocusedConveyorBlock(Player player, out Block focusedConveyorBlock)
    {
        focusedConveyorBlock = null;
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        return playerController != null
            && playerController.TryGetFocusedConveyorBelt(out _, out focusedConveyorBlock)
            && focusedConveyorBlock != null;
    }

    private bool TryHandleFocusedRobotArmPickup(Player player, out bool blockOtherPickup)
    {
        blockOtherPickup = false;
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null
            || !playerController.TryGetFocusedRobotArm(out RobotArm focusedRobotArm)
            || focusedRobotArm == null
            || !focusedRobotArm.HasHeldItem)
        {
            return false;
        }

        blockOtherPickup = true;
        if (!focusedRobotArm.CanTakeHeldItemFromSlot || boundBag == null || slotIndex < 0)
        {
            return false;
        }

        return focusedRobotArm.TryTakeHeldItemToBag(boundBag, slotIndex);
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
        return TerrainGenerator.ResolveActive();
    }

    protected bool IsInventoryUiLocked()
    {
        return false;
    }

    protected bool IsInventoryEditLocked()
    {
        return IsInventoryUiLocked();
    }

    protected bool IsItemDropLocked()
    {
        return GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked;
    }
}
