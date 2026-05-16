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
    private static readonly List<RaycastResult> itemSlotRaycastResults = new List<RaycastResult>();
    private static readonly List<ItemSlot> itemSlotParentBuffer = new List<ItemSlot>();
    private static readonly List<BagSlot> bagSlotParentBuffer = new List<BagSlot>();
    private static readonly Vector3[] itemSlotWorldCorners = new Vector3[4];
    private static BagSlot automaticPickupPreviewSlot;
    private static InstallationPlacementController cachedDropFocusPlacementController;
    private static int automaticPickupPreviewFrame = -1;
    private static int automaticPickupPreviewPriority = int.MaxValue;
    private static float pickupPreviewSuppressedUntilTime;
    private const float DragCancelDistance = 8f;
    private const float CraftingRootHideDelay = 0.12f;
    private const float PickupPreviewSuppressAfterPickupDuration = 0.12f;
    private const float HeldClickRepeatInterval = 0.1f;
    private const int CraftingInnerRingSlotLimit = 5;
    private const float CraftingOuterRingSlotPadding = 0.9f;
    protected const float FocusedPickupRange = 999f;
    private const float StandingTilePickupRange = 999f;

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

    [SerializeField, Min(0.01f)]
    private float pickupRange = 0.5f;

    [SerializeField, Range(0.1f, 1f)]
    private float pickupPreviewAlpha = 0.5f;

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
    private bool consumeNextPickupButtonClick;
    private bool suppressNextCraftingToggle;
    private Vector2 dragStartScreenPosition;
    private float nextHeldPickupTime;
    private float nextHeldDropTime;
    private int lastBoundItemId = -1;
    private int lastBoundItemCount;
    private Coroutine pickupRoutine;
    private Tween craftingRootHideTween;
    private float craftingExpandAnimationUntilTime;
    private bool pickupPreviewActive;
    private int pickupPreviewItemId = -1;

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

    private readonly struct PortableMoveAnchor
    {
        private readonly Transform parent;
        private readonly Vector3 localPosition;
        private readonly Vector3 worldPosition;

        public PortableMoveAnchor(PortableObject portableObject)
        {
            Transform targetTransform = portableObject != null ? portableObject.transform : null;
            parent = targetTransform != null ? targetTransform.parent : null;
            localPosition = targetTransform != null ? targetTransform.localPosition : Vector3.zero;
            worldPosition = targetTransform != null ? targetTransform.position : Vector3.zero;
        }

        public Vector3 ResolveWorldPosition()
        {
            return parent != null ? parent.TransformPoint(localPosition) : worldPosition;
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

        ReleaseAutomaticPickupPreviewSlot();
        EndDragVisual();
        ClearPickupPreview();
        CollapseCraftingSlots(true);
        StopPickupRoutine();

        if (expandedSlot == this)
        {
            expandedSlot = null;
        }
    }

    private void OnDestroy()
    {
        ReleaseAutomaticPickupPreviewSlot();
        EndDragVisual();
        DestroyDragGhost();
        UnbindPickupClick();
    }

    public void Bind(PlayerBag bag, int index, int itemId, int itemCount, int maxItemCount, bool allowZeroCountDisplay = false)
    {
        boundBag = bag;
        slotIndex = index;
        ResetPickupPreviewState();

        bool shouldDisplayItem = itemId >= 0 && (itemCount > 0 || allowZeroCountDisplay);
        if (!shouldDisplayItem)
        {
            Clear();
            RefreshCraftingItems(itemId, itemCount);
            if (isCraftingExpanded)
            {
                CollapseCraftingSlots(true);
            }
            RefreshPickupPreviewAfterBind();
            return;
        }

        SetItemDisplay(itemId, itemCount, maxItemCount, allowZeroCountDisplay);
        RefreshCraftingItems(itemId, itemCount);
        RefreshPickupPreviewAfterBind();
    }

    public void SetSlotVisible(bool visible)
    {
        CacheReferences();
        if (visible && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (!visible)
        {
            ReleaseAutomaticPickupPreviewSlot();
            ClearPickupPreview();
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
        DropItem(int.MaxValue);
    }

    private void DropItem(int maxDropCount)
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
        int requestedDropCount = Mathf.Max(1, maxDropCount);
        bool isFocusedConveyorDrop = terrainGenerator.TryGetFocusedConveyorDropLimit(out int conveyorDropLimit);
        if (isFocusedConveyorDrop)
        {
            int conveyorItemId = boundBag.GetSlotItemId(slotIndex);
            int conveyorSlotCount = boundBag.GetSlotCount(slotIndex);
            int conveyorDropCount = Mathf.Min(conveyorSlotCount, conveyorDropLimit, requestedDropCount);
            if (conveyorItemId < 0 || conveyorDropCount <= 0)
            {
                return;
            }

            if (!boundBag.TryRemoveItemsAtSlot(
                    slotIndex,
                    conveyorDropCount,
                    out int removedItemId,
                    out int conveyorRemovedCount,
                    out startWorldPosition,
                    false)
                || conveyorRemovedCount <= 0)
            {
                return;
            }

            if (removedItemId != conveyorItemId)
            {
                RestoreItemsToSlot(removedItemId, conveyorRemovedCount);
                return;
            }

            bool conveyorDropped = terrainGenerator.TryAddDroppedItemStackAtPlayerBlock(
                dropOrigin,
                conveyorItemId,
                conveyorRemovedCount,
                startWorldPosition,
                startWorldPositionProvider,
                0.1f,
                out Vector2Int conveyorDropCoordinate,
                out int conveyorDroppedCount);

            if (!conveyorDropped || conveyorDroppedCount <= 0)
            {
                RestoreItemsToSlot(conveyorItemId, conveyorRemovedCount);
                return;
            }

            int conveyorRemainingCount = Mathf.Max(0, conveyorRemovedCount - conveyorDroppedCount);
            if (conveyorRemainingCount > 0)
            {
                RestoreItemsToSlot(conveyorItemId, conveyorRemainingCount);
            }

            player.MarkDropExitGate(dropOrigin, 0.5f);
            player.SetLastDropTarget(conveyorDropCoordinate);
            SuppressPickupPreviewAfterDrop(player);
            return;
        }

        int itemId;
        int removedCount;
        bool removedItems;
        if (maxDropCount == int.MaxValue)
        {
            removedItems = boundBag.TryRemoveAllAtSlot(slotIndex, out itemId, out removedCount, out startWorldPosition);
        }
        else
        {
            removedItems = boundBag.TryRemoveItemsAtSlot(slotIndex, requestedDropCount, out itemId, out removedCount, out startWorldPosition, false);
        }

        if (!removedItems)
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
            SuppressPickupPreviewAfterDrop(player);
        }

        int remainingCount = Mathf.Max(0, removedCount - droppedCount);
        if (remainingCount > 0)
        {
            RestoreItemsToSlot(itemId, remainingCount);
        }
    }

    private void RestoreItemsToSlot(int itemId, int count)
    {
        if (boundBag == null || slotIndex < 0 || itemId < 0 || count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (!boundBag.TryAddObject(slotIndex, itemId, out _))
            {
                break;
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!IsDragSourceBagSlot(eventData))
        {
            return;
        }

        ClearPickupPreview();
        if (!CanDragItem())
        {
            return;
        }

        CacheReferences();
        EnsureDragGhost();

        isDragging = true;
        dragStartScreenPosition = eventData != null ? eventData.position : Vector2.zero;
        UpdateDragGhost(eventData);
        RefreshDragDropFocus(eventData);

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
        RefreshDragDropFocus(eventData);
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
        RefreshPickupPreview();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoveredDropSlot == this)
        {
            hoveredDropSlot = null;
        }

        ClearPickupPreview();
    }

    private void Update()
    {
        bool pointerOverSlot = IsPointerOverSlot();
        if (pointerOverSlot)
        {
            hoveredDropSlot = this;
        }
        else if (hoveredDropSlot == this)
        {
            hoveredDropSlot = null;
            ResetHeldSlotInput();
        }

        if (isDragging)
        {
            ClearPickupPreview();
            RefreshDragDropFocus(null);
            ResetHeldSlotInput();
            return;
        }

        RefreshPickupPreview();

        if (hoveredDropSlot != this)
        {
            ResetHeldSlotInput();
            return;
        }

        HandleHeldSlotInput();

        if (Input.GetKeyDown(KeyCode.F) && CanDragItem())
        {
            DropItem();
        }
    }

    private void HandleHeldSlotInput()
    {
        if (Input.GetMouseButtonUp(0))
        {
            nextHeldPickupTime = 0f;
            StopPickupRoutine();
        }

        if (Input.GetMouseButtonUp(1))
        {
            nextHeldDropTime = 0f;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryPickupFromHeldSlotInput();
            nextHeldPickupTime = Time.time + HeldClickRepeatInterval;
            return;
        }

        if (Input.GetMouseButton(0) && nextHeldPickupTime > 0f && Time.time >= nextHeldPickupTime)
        {
            TryPickupFromHeldSlotInput();
            nextHeldPickupTime = Time.time + HeldClickRepeatInterval;
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            DropItem(1);
            nextHeldDropTime = Time.time + HeldClickRepeatInterval;
            return;
        }

        if (Input.GetMouseButton(1) && nextHeldDropTime > 0f && Time.time >= nextHeldDropTime)
        {
            DropItem(1);
            nextHeldDropTime = Time.time + HeldClickRepeatInterval;
        }
    }

    private bool TryPickupFromHeldSlotInput()
    {
        bool handled = TryHandlePickupClick(true);
        if (!handled)
        {
            return false;
        }

        consumeNextPickupButtonClick = true;
        suppressNextCraftingToggle = true;
        return true;
    }

    private void ResetHeldSlotInput()
    {
        nextHeldPickupTime = 0f;
        nextHeldDropTime = 0f;
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

    private void RefreshDragDropFocus(PointerEventData eventData)
    {
        if (!TryResolveDragDropFocusTarget(eventData, out PlayerController playerController, out Block targetBlock))
        {
            ClearDragDropFocus();
            return;
        }

        playerController.SetTemporaryDropFocus(targetBlock);
    }

    private bool TryResolveDragDropFocusTarget(PointerEventData eventData, out PlayerController playerController, out Block targetBlock)
    {
        playerController = null;
        targetBlock = null;
        if (!isDragging)
        {
            return false;
        }

        if (IsDropFocusBlockedByMode())
        {
            return false;
        }

        Vector2 pointerPosition = eventData != null ? eventData.position : GetCurrentPointerPosition();
        if (IsPointerOverAnyItemSlot(pointerPosition))
        {
            return false;
        }

        Player player = ResolvePlayer();
        playerController = player != null ? player.GetComponent<PlayerController>() : null;
        if (playerController == null)
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrain();
        if (terrainGenerator == null || !TryGetDraggedItem(out int itemId, out int itemCount))
        {
            return false;
        }

        Vector3 dropOrigin = player != null ? player.transform.position : Vector3.zero;
        return TryResolveDropFocusBlock(terrainGenerator, dropOrigin, itemId, Mathf.Min(itemCount, 1), out targetBlock);
    }

    private static bool IsDropFocusBlockedByMode()
    {
        if (GameManager.Instance != null && GameManager.Instance.PlayerInteractionLocked)
        {
            return true;
        }

        InstallationPlacementController placementController = ResolveDropFocusPlacementController();
        return placementController != null && placementController.PlacementOrMapEditModeActive;
    }

    private static InstallationPlacementController ResolveDropFocusPlacementController()
    {
        if (cachedDropFocusPlacementController != null)
        {
            return cachedDropFocusPlacementController;
        }

        cachedDropFocusPlacementController = FindObjectOfType<InstallationPlacementController>();
        return cachedDropFocusPlacementController;
    }

    private bool TryGetDraggedItem(out int itemId, out int itemCount)
    {
        itemId = -1;
        itemCount = 0;
        if (boundBag == null || slotIndex < 0)
        {
            return false;
        }

        itemId = boundBag.GetSlotItemId(slotIndex);
        itemCount = boundBag.GetSlotCount(slotIndex);
        return itemId >= 0 && itemCount > 0;
    }

    private static Vector2 GetCurrentPointerPosition()
    {
        if (Input.touchCount > 0)
        {
            return Input.GetTouch(0).position;
        }

        return Input.mousePosition;
    }

    private static bool IsPointerOverAnyItemSlot(Vector2 pointerPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            PointerEventData pointerData = new PointerEventData(eventSystem)
            {
                position = pointerPosition
            };

            itemSlotRaycastResults.Clear();
            eventSystem.RaycastAll(pointerData, itemSlotRaycastResults);
            for (int i = 0; i < itemSlotRaycastResults.Count; i++)
            {
                GameObject hitObject = itemSlotRaycastResults[i].gameObject;
                if (hitObject == null)
                {
                    continue;
                }

                if (TryGetPointerBlockingItemSlot(hitObject, out _))
                {
                    itemSlotRaycastResults.Clear();
                    return true;
                }
            }

            itemSlotRaycastResults.Clear();
            return false;
        }

        ItemSlot[] itemSlots = FindObjectsOfType<ItemSlot>(false);
        for (int i = 0; i < itemSlots.Length; i++)
        {
            ItemSlot itemSlot = itemSlots[i];
            if (!IsPointerBlockingItemSlot(itemSlot))
            {
                continue;
            }

            RectTransform slotRectTransform = itemSlot.transform as RectTransform;
            Canvas canvas = itemSlot.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(slotRectTransform, pointerPosition, eventCamera))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPointerBlockingItemSlot(GameObject target, out ItemSlot itemSlot)
    {
        itemSlot = null;
        if (target == null)
        {
            return false;
        }

        itemSlotParentBuffer.Clear();
        target.GetComponentsInParent(false, itemSlotParentBuffer);
        for (int i = 0; i < itemSlotParentBuffer.Count; i++)
        {
            ItemSlot candidate = itemSlotParentBuffer[i];
            if (IsPointerBlockingItemSlot(candidate))
            {
                itemSlot = candidate;
                break;
            }
        }

        itemSlotParentBuffer.Clear();
        return itemSlot != null;
    }

    private static bool IsVisibleItemSlotForPointer(ItemSlot itemSlot)
    {
        if (itemSlot == null || !itemSlot.isActiveAndEnabled || !itemSlot.gameObject.activeInHierarchy)
        {
            return false;
        }

        RectTransform slotRectTransform = itemSlot.transform as RectTransform;
        if (slotRectTransform == null || !HasVisibleRectArea(slotRectTransform))
        {
            return false;
        }

        CanvasGroup[] canvasGroups = itemSlot.GetComponentsInParent<CanvasGroup>(false);
        for (int i = 0; i < canvasGroups.Length; i++)
        {
            CanvasGroup canvasGroup = canvasGroups[i];
            if (canvasGroup == null || !canvasGroup.isActiveAndEnabled)
            {
                continue;
            }

            if (canvasGroup.alpha <= 0.001f || !canvasGroup.blocksRaycasts)
            {
                return false;
            }

            if (canvasGroup.ignoreParentGroups)
            {
                break;
            }
        }

        return true;
    }

    private static bool IsPointerBlockingItemSlot(ItemSlot itemSlot)
    {
        if (!IsVisibleItemSlotForPointer(itemSlot))
        {
            return false;
        }

        if (itemSlot is PlayerHUD)
        {
            return false;
        }

        if (itemSlot is BagSlot bagSlot)
        {
            return IsBoundDropSlot(bagSlot);
        }

        return true;
    }

    private static bool IsBoundDropSlot(BagSlot slot)
    {
        return slot != null
               && !(slot is PlayerHUD)
               && slot.boundBag != null
               && slot.slotIndex >= 0;
    }

    private static bool HasVisibleRectArea(RectTransform rectTransform)
    {
        rectTransform.GetWorldCorners(itemSlotWorldCorners);
        float width = Vector3.Distance(itemSlotWorldCorners[0], itemSlotWorldCorners[3]);
        float height = Vector3.Distance(itemSlotWorldCorners[0], itemSlotWorldCorners[1]);
        return width > 0.5f && height > 0.5f;
    }

    private static bool TryResolveDropFocusBlock(TerrainGenerator terrainGenerator, Vector3 dropOrigin, int itemId, int itemCount, out Block targetBlock)
    {
        targetBlock = null;
        if (terrainGenerator == null
            || itemId < 0
            || itemCount <= 0)
        {
            return false;
        }

        bool resolvedDropTarget = terrainGenerator.TryResolveDroppedItemStackTargetBlockAtPlayerBlock(
                dropOrigin,
                itemId,
                itemCount,
                out targetBlock,
                out _);
        if ((!resolvedDropTarget || targetBlock == null)
            && !TryResolveFallbackDropFocusBlock(terrainGenerator, dropOrigin, itemId, itemCount, out targetBlock))
        {
            return false;
        }

        return targetBlock != null;
    }

    private static bool TryResolveFallbackDropFocusBlock(
        TerrainGenerator terrainGenerator,
        Vector3 dropOrigin,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        if (terrainGenerator == null)
        {
            return false;
        }

        Vector2Int centerCoordinate = ResolvePickupCoordinate(dropOrigin);
        int count = Mathf.Max(1, itemCount);
        if (TryFindDropFocusBlock(
                terrainGenerator,
                centerCoordinate,
                itemId,
                count,
                true,
                out targetBlock))
        {
            return true;
        }

        if (terrainGenerator.TryGetLoadedBlock(centerCoordinate, out Block centerBlock)
            && IsDropFocusBlockCandidate(centerBlock, itemId, count))
        {
            targetBlock = centerBlock;
            return true;
        }

        if (TryFindDropFocusBlock(
                terrainGenerator,
                centerCoordinate,
                itemId,
                count,
                false,
                out targetBlock))
        {
            return true;
        }

        if (terrainGenerator.TryGetLoadedBlock(centerCoordinate, out centerBlock) && centerBlock != null)
        {
            targetBlock = centerBlock;
            return true;
        }

        return false;
    }

    private static bool TryFindDropFocusBlock(
        TerrainGenerator terrainGenerator,
        Vector2Int centerCoordinate,
        int itemId,
        int itemCount,
        bool requireSameItem,
        out Block targetBlock)
    {
        targetBlock = null;
        int bestDistance = int.MaxValue;
        const int SearchRadius = 2;
        for (int offsetY = -SearchRadius; offsetY <= SearchRadius; offsetY++)
        {
            for (int offsetX = -SearchRadius; offsetX <= SearchRadius; offsetX++)
            {
                Vector2Int coordinate = centerCoordinate + new Vector2Int(offsetX, offsetY);
                if (!terrainGenerator.TryGetLoadedBlock(coordinate, out Block block)
                    || !IsDropFocusBlockCandidate(block, itemId, itemCount))
                {
                    continue;
                }

                if (requireSameItem && !block.HasFloorObjectItem(itemId))
                {
                    continue;
                }

                int distance = Mathf.Abs(offsetX) + Mathf.Abs(offsetY);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                targetBlock = block;
            }
        }

        return targetBlock != null;
    }

    private static bool IsDropFocusBlockCandidate(Block block, int itemId, int itemCount)
    {
        return block != null
               && block.Type == Block.BlockType.Ground
               && block.CanAddFloorObjects(Mathf.Max(1, itemCount), itemId);
    }

    private void ClearDragDropFocus()
    {
        Player player = ResolvePlayer();
        PlayerController playerController = player != null ? player.GetComponent<PlayerController>() : null;
        playerController?.ClearTemporaryDropFocus();
    }

    private void EndDragVisual()
    {
        isDragging = false;

        SetIconAlpha(1f);
        ClearDragDropFocus();

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

        Vector2 startPosition = Vector2.zero;
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

        if (suppressNextCraftingToggle)
        {
            suppressNextCraftingToggle = false;
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

    public void RefreshCraftingAvailability(bool force = true)
    {
        RefreshCraftingItemsFromBag(force);
    }

    public void RefreshExpandedCraftingSlotStatus()
    {
        if (!isCraftingExpanded || craftingSlots == null)
        {
            return;
        }

        for (int i = 0; i < craftingSlots.Count; i++)
        {
            CraftingSlot craftingSlot = craftingSlots[i];
            if (craftingSlot == null || !craftingSlot.gameObject.activeInHierarchy)
            {
                continue;
            }

            craftingSlot.RefreshCraftingAvailabilityVisuals();
        }
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
        float searchRange = Mathf.Max(
            requiredCraftingMapObjectRange,
            WorkableObject.GlobalMaxFocusActivationRadius);
        int searchRadius = Mathf.Max(1, Mathf.CeilToInt(searchRange + 1f));
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

                if (workableObject.ContainsWorldPositionInWorkableRange(origin))
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

    private new void SetIconAlpha(float alpha)
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

    private void SetCountAlpha(float alpha)
    {
        var countLabel = CountLabel;
        if (countLabel == null)
        {
            return;
        }

        Color color = countLabel.color;
        color.a = Mathf.Clamp01(alpha);
        countLabel.color = color;
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

        BagSlot hoveredSlot = GetBoundDropSlotInParents(hoveredObject);
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

        if (eventData.button != PointerEventData.InputButton.Left)
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

        BagSlot parentSlot = GetBoundDropSlotInParents(target);
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

        return GetBoundDropSlotInParents(target);
    }

    private static BagSlot GetBoundDropSlotInParents(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        bagSlotParentBuffer.Clear();
        target.GetComponentsInParent(false, bagSlotParentBuffer);
        BagSlot result = null;
        for (int i = 0; i < bagSlotParentBuffer.Count; i++)
        {
            BagSlot slot = bagSlotParentBuffer[i];
            if (IsBoundDropSlot(slot))
            {
                result = slot;
                break;
            }
        }

        bagSlotParentBuffer.Clear();
        return result;
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

        List<PortableMoveAnchor> sourceAnchors = new List<PortableMoveAnchor>(itemCount);
        if (!TryCollectPortableObjectAnchors(sourceObjects, sourceAnchors))
        {
            return false;
        }

        List<PortableMoveAnchor> targetAnchors = new List<PortableMoveAnchor>(itemCount);
        if (!TryCollectPortableObjectAnchors(targetObjects, targetAnchors))
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
        AnimatePortableMoves(targetObjects, sourceAnchors, targetAnchors, ref moveIndex, moveInterval);

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

        List<PortableMoveAnchor> sourceAnchors = new List<PortableMoveAnchor>(sourceCount);
        if (!TryCollectPortableObjectAnchors(sourceObjects, sourceAnchors))
        {
            return false;
        }

        List<PortableMoveAnchor> targetAnchors = new List<PortableMoveAnchor>(targetCount);
        if (!TryCollectPortableObjectAnchors(targetObjects, targetAnchors))
        {
            return false;
        }

        List<PortableMoveAnchor> sourceDestinationAnchors = new List<PortableMoveAnchor>(destinationForSource.Count);
        if (!TryCollectPortableObjectAnchors(destinationForSource, sourceDestinationAnchors))
        {
            return false;
        }

        List<PortableMoveAnchor> targetDestinationAnchors = new List<PortableMoveAnchor>(destinationForTarget.Count);
        if (!TryCollectPortableObjectAnchors(destinationForTarget, targetDestinationAnchors))
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
        AnimatePortableMoves(destinationForSource, targetAnchors, sourceDestinationAnchors, ref moveIndex, moveInterval);
        AnimatePortableMoves(destinationForTarget, sourceAnchors, targetDestinationAnchors, ref moveIndex, moveInterval);

        sourceBag.ForceNotifyChanged();
        targetBag.ForceNotifyChanged();
        return true;
    }

    private static bool TryCollectPortableObjectAnchors(List<PortableObject> portableObjects, List<PortableMoveAnchor> anchors)
    {
        if (portableObjects == null || anchors == null)
        {
            return false;
        }

        anchors.Clear();
        for (int i = 0; i < portableObjects.Count; i++)
        {
            PortableObject portableObject = portableObjects[i];
            if (portableObject == null)
            {
                return false;
            }

            anchors.Add(new PortableMoveAnchor(portableObject));
        }

        return true;
    }

    private void AnimatePortableMoves(
        List<PortableObject> portableObjects,
        List<PortableMoveAnchor> startAnchors,
        List<PortableMoveAnchor> targetAnchors,
        ref int moveIndex,
        float moveInterval)
    {
        if (portableObjects == null
            || startAnchors == null
            || targetAnchors == null
            || startAnchors.Count == 0
            || targetAnchors.Count == 0)
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

            PortableMoveAnchor startAnchor = startAnchors[Mathf.Min(i, startAnchors.Count - 1)];
            PortableMoveAnchor targetAnchor = targetAnchors[Mathf.Min(i, targetAnchors.Count - 1)];
            AnimatePortableMove(portableObject, startAnchor, targetAnchor, moveIndex * moveInterval);
            moveIndex++;
        }
    }

    private void NotifyCraftingVisibilityChanged(bool isVisible)
    {
        if (suppressCraftingEvents)
        {
            return;
        }

        WorkableObject.SetCraftingSlotRangeVisualsRequested(this, isVisible);
        CraftingVisibilityChanged?.Invoke(this, isVisible);
    }

    private void AnimatePortableMove(PortableObject portableObject, PortableMoveAnchor startAnchor, PortableMoveAnchor targetAnchor, float delay)
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
        portableObject.transform.position = startAnchor.ResolveWorldPosition();
        if (!portableObject.gameObject.activeSelf)
        {
            portableObject.gameObject.SetActive(true);
        }

        portableObject.MoveTo(
            () => targetAnchor.ResolveWorldPosition(),
            delay,
            () => startAnchor.ResolveWorldPosition(),
            () =>
            {
                if (portableObject == null)
                {
                    return;
                }

                RestorePortableMoveVisual(portableObject);
            },
            false);
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

    private void RefreshPickupPreview()
    {
        if (!CanShowPickupPreview())
        {
            ClearPickupPreview();
            return;
        }

        if (!TryResolvePickupPreviewItem(out int previewItemId, out int previewPickupCount))
        {
            ClearPickupPreview();
            return;
        }

        ApplyPickupPreview(previewItemId, previewPickupCount);
    }

    private bool CanShowPickupPreview()
    {
        if (isDragging
            || !AllowPickupOnClick
            || IsInventoryUiLocked()
            || !IsVisibleItemSlotForPointer(this)
            || boundBag == null
            || slotIndex < 0
            || HasStoredItemInPickupTargetSlot())
        {
            return false;
        }

        return !IsPickupPreviewSuppressed(ResolvePlayer());
    }

    private bool HasStoredItemInPickupTargetSlot()
    {
        int targetSlotIndex = GetPickupSlotIndex();
        return boundBag != null
               && targetSlotIndex >= 0
               && boundBag.GetSlotCount(targetSlotIndex) > 0;
    }

    private bool TryResolvePickupPreviewItem(out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 1;

        Player player = ResolvePlayer();
        if (player == null)
        {
            return false;
        }

        if (TryPreviewFocusedRobotArmPickup(player, out bool blockOtherPickup, out previewItemId)
            && ShouldDisplayPickupPreviewForItem(previewItemId))
        {
            return true;
        }

        if (blockOtherPickup)
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        Vector3 pickupOrigin = ResolvePickupOrigin(player);
        int preferredItemId = GetPreferredPickupItemId();
        bool hasFocusedConveyor = TryGetFocusedConveyorBlock(player, out Block focusedConveyorBlock);
        if (hasFocusedConveyor
            && focusedConveyorBlock.TryPreviewPickupConveyorObjects(
                player,
                pickupOrigin,
                FocusedPickupRange,
                preferredItemId,
                out previewItemId,
                out previewPickupCount)
            && CanPreviewAcceptPickupItem(player, previewItemId))
        {
            return ShouldDisplayPickupPreviewForItem(previewItemId);
        }

        if (TryPreviewFocusedBoxPickup(player, pickupOrigin, FocusedPickupRange, preferredItemId, out previewItemId, out previewPickupCount))
        {
            return ShouldDisplayPickupPreviewForItem(previewItemId);
        }

        bool allowFocusedConveyorPickup = !hasFocusedConveyor;
        return TryPreviewOneItemForClick(player, terrain, allowFocusedConveyorPickup, out previewItemId, out previewPickupCount)
               && ShouldDisplayPickupPreviewForItem(previewItemId);
    }

    private bool TryPreviewOneItemForClick(Player player, TerrainGenerator terrain, bool allowFocusedConveyorPickup, out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (player == null || terrain == null)
        {
            return false;
        }

        Vector3 pickupOrigin = ResolvePickupOrigin(player);
        Vector2Int currentCoordinate = ResolveStandingCoordinate(player);
        float range = GetStandingTilePickupRange();

        if (TryPreviewOneItemAtCoordinate(terrain, player, currentCoordinate, pickupOrigin, range, allowFocusedConveyorPickup, out previewItemId, out previewPickupCount))
        {
            return true;
        }

        return false;
    }

    private bool TryPreviewOneItemAtCoordinate(
        TerrainGenerator terrain,
        Player player,
        Vector2Int coordinate,
        Vector3 pickupOrigin,
        float pickupRange,
        bool allowFocusedConveyorPickup,
        out int previewItemId,
        out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (terrain == null || player == null || pickupRange <= 0f)
        {
            return false;
        }

        int preferredItemId = GetPreferredPickupItemId();
        if (allowFocusedConveyorPickup
            && TryGetFocusedConveyorBlock(player, out Block focusedConveyorBlock)
            && focusedConveyorBlock.TryPreviewPickupConveyorObjects(
                player,
                pickupOrigin,
                pickupRange,
                preferredItemId,
                out previewItemId,
                out previewPickupCount)
            && CanPreviewAcceptPickupItem(player, previewItemId))
        {
            return true;
        }

        if (TryPreviewFocusedBoxPickup(player, pickupOrigin, FocusedPickupRange, preferredItemId, out previewItemId, out previewPickupCount))
        {
            return true;
        }

        if (!terrain.TryGetLoadedBlock(coordinate, out Block block) || block == null)
        {
            return false;
        }

        if (block.Type != Block.BlockType.Ground)
        {
            return false;
        }

        return block.TryPreviewPickupFloorObjects(player, pickupOrigin, pickupRange, preferredItemId, out previewItemId, out previewPickupCount)
               && CanPreviewAcceptPickupItem(player, previewItemId);
    }

    private bool TryPreviewFocusedRobotArmPickup(Player player, out bool blockOtherPickup, out int previewItemId)
    {
        blockOtherPickup = false;
        previewItemId = -1;
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

        if (!focusedRobotArm.CanTakeHeldItemFromSlot)
        {
            return false;
        }

        int itemId = focusedRobotArm.HeldItemId;
        if (!CanPreviewAcceptPickupItem(player, itemId))
        {
            return false;
        }

        blockOtherPickup = true;
        previewItemId = itemId;
        return true;
    }

    private bool TryPreviewFocusedBoxPickup(Player player, Vector3 pickupOrigin, float pickupRange, int preferredItemId, out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        PlayerController playerController = player != null ? player.GetComponent<PlayerController>() : null;
        return playerController != null
               && playerController.TryGetFocusedBoxObject(out BoxObject focusedBoxObject)
               && focusedBoxObject != null
               && focusedBoxObject.TryPreviewContainedObjectPickup(player, pickupOrigin, pickupRange, preferredItemId, out previewItemId, out previewPickupCount)
               && CanPreviewAcceptPickupItem(player, previewItemId);
    }

    private bool ShouldDisplayPickupPreviewForItem(int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        if (IsHoveredForPickupPreview())
        {
            return true;
        }

        return TryClaimAutomaticPickupPreview(itemId);
    }

    private bool TryClaimAutomaticPickupPreview(int itemId)
    {
        if (hoveredDropSlot != null
            || !TryFindAutomaticPickupPreviewSlot(itemId, out int targetSlotIndex)
            || targetSlotIndex != slotIndex)
        {
            return false;
        }

        int frame = Time.frameCount;
        int priority = GetAutomaticPickupPreviewPriority();
        if (automaticPickupPreviewFrame != frame)
        {
            automaticPickupPreviewFrame = frame;
            automaticPickupPreviewSlot = null;
            automaticPickupPreviewPriority = int.MaxValue;
        }

        if (automaticPickupPreviewSlot == this)
        {
            automaticPickupPreviewPriority = Mathf.Min(automaticPickupPreviewPriority, priority);
            return true;
        }

        if (automaticPickupPreviewSlot != null && priority >= automaticPickupPreviewPriority)
        {
            return false;
        }

        automaticPickupPreviewSlot?.ClearPickupPreview();
        automaticPickupPreviewSlot = this;
        automaticPickupPreviewPriority = priority;
        return true;
    }

    private int GetAutomaticPickupPreviewPriority()
    {
        return this is HandSlot ? 10 : 0;
    }

    private bool TryFindAutomaticPickupPreviewSlot(int itemId, out int targetSlotIndex)
    {
        targetSlotIndex = -1;
        if (boundBag == null || itemId < 0)
        {
            return false;
        }

        return TryFindAutomaticPickupPreviewSlot(itemId, false, out targetSlotIndex);
    }

    private bool TryFindAutomaticPickupPreviewSlot(int itemId, bool requireExistingItems, out int targetSlotIndex)
    {
        targetSlotIndex = -1;
        if (boundBag == null || itemId < 0)
        {
            return false;
        }

        int slotCount = boundBag.SlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            bool hasItems = boundBag.GetSlotCount(i) > 0;
            if (hasItems != requireExistingItems)
            {
                continue;
            }

            if (!boundBag.CanAddObject(i, itemId))
            {
                continue;
            }

            targetSlotIndex = i;
            return true;
        }

        return false;
    }

    protected virtual bool CanPreviewAcceptPickupItem(Player player, int itemId)
    {
        int targetSlotIndex = GetPickupSlotIndex();
        return boundBag != null
               && targetSlotIndex >= 0
               && itemId >= 0
               && boundBag.CanAddObject(targetSlotIndex, itemId);
    }

    private void ApplyPickupPreview(int itemId, int pickupCount)
    {
        if (itemId < 0 || !TryGetItemIcon(itemId, out _))
        {
            ClearPickupPreview();
            return;
        }

        if (!TryResolvePickupPreviewDisplay(itemId, pickupCount, out int previewCount, out int previewMaxCount))
        {
            ClearPickupPreview();
            return;
        }

        pickupPreviewActive = true;
        pickupPreviewItemId = itemId;
        SetItemDisplay(itemId, previewCount, previewMaxCount, false);
        SetIconAlpha(pickupPreviewAlpha);
        SetCountAlpha(pickupPreviewAlpha);
    }

    private bool TryResolvePickupPreviewDisplay(int itemId, int pickupCount, out int previewCount, out int previewMaxCount)
    {
        previewCount = 0;
        previewMaxCount = 0;

        int targetSlotIndex = GetPickupSlotIndex();
        if (boundBag == null || targetSlotIndex < 0 || itemId < 0)
        {
            return false;
        }

        int currentCount = Mathf.Max(0, boundBag.GetSlotCount(targetSlotIndex));
        if (currentCount > 0)
        {
            return false;
        }

        previewMaxCount = boundBag.GetSlotMaxCount(targetSlotIndex);
        int addCount = Mathf.Max(1, pickupCount);
        int nextCount = currentCount + addCount;
        previewCount = previewMaxCount > 0
            ? Mathf.Clamp(nextCount, 1, previewMaxCount)
            : Mathf.Max(1, nextCount);
        return true;
    }

    private void RefreshPickupPreviewAfterBind()
    {
        RefreshPickupPreview();
    }

    private bool IsHoveredForPickupPreview()
    {
        if (hoveredDropSlot == this)
        {
            return true;
        }

        if (!IsPointerOverSlot())
        {
            return false;
        }

        hoveredDropSlot = this;
        return true;
    }

    private bool IsPointerOverSlot()
    {
        CacheReferences();
        if (!isActiveAndEnabled || rectTransform == null)
        {
            return false;
        }

        if (canvasGroup != null && (!canvasGroup.blocksRaycasts || canvasGroup.alpha <= 0.001f))
        {
            return false;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, eventCamera);
    }

    private void ClearPickupPreview()
    {
        ReleaseAutomaticPickupPreviewSlot();
        if (!pickupPreviewActive)
        {
            return;
        }

        pickupPreviewActive = false;
        pickupPreviewItemId = -1;
        RestoreBoundSlotDisplayOrClear();
    }

    private void ResetPickupPreviewState()
    {
        ReleaseAutomaticPickupPreviewSlot();
        pickupPreviewActive = false;
        pickupPreviewItemId = -1;
        SetIconAlpha(1f);
        SetCountAlpha(1f);
    }

    private static bool IsPickupPreviewSuppressed(Player player = null)
    {
        return Time.time < pickupPreviewSuppressedUntilTime
               || (player != null && player.IsDropExitPending);
    }

    private void SuppressPickupPreviewAfterDrop(Player player)
    {
        pickupPreviewSuppressedUntilTime = Mathf.Max(
            pickupPreviewSuppressedUntilTime,
            Time.time + PortableObject.MoveToDuration);

        automaticPickupPreviewSlot?.ClearPickupPreview();
        ClearPickupPreview();
    }

    private void SuppressPickupPreviewAfterPickup()
    {
        pickupPreviewSuppressedUntilTime = Mathf.Max(
            pickupPreviewSuppressedUntilTime,
            Time.time + PickupPreviewSuppressAfterPickupDuration);

        automaticPickupPreviewSlot?.ClearPickupPreview();
        ClearPickupPreview();
    }

    private void ReleaseAutomaticPickupPreviewSlot()
    {
        if (automaticPickupPreviewSlot != this)
        {
            return;
        }

        automaticPickupPreviewSlot = null;
        automaticPickupPreviewPriority = int.MaxValue;
    }

    private void RestoreBoundSlotDisplayOrClear()
    {
        SetIconAlpha(1f);
        SetCountAlpha(1f);
        if (boundBag != null && slotIndex >= 0)
        {
            int itemCount = boundBag.GetSlotCount(slotIndex);
            int itemId = boundBag.GetSlotItemId(slotIndex);
            if (itemId >= 0 && itemCount > 0)
            {
                SetItemDisplay(itemId, itemCount, boundBag.GetSlotMaxCount(slotIndex), false);
                SetIconAlpha(1f);
                SetCountAlpha(1f);
                return;
            }
        }

        Clear();
        SetIconAlpha(1f);
        SetCountAlpha(1f);
    }

    private static bool TryGetItemIcon(int itemId, out Sprite iconSprite)
    {
        iconSprite = null;
        if (GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return false;
        }

        if (!GameManager.Instance.ItemManger.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            return false;
        }

        iconSprite = itemSet.icon;
        return iconSprite != null;
    }

    private void HandlePickupClick()
    {
        if (consumeNextPickupButtonClick)
        {
            consumeNextPickupButtonClick = false;
            suppressNextCraftingToggle = true;
            return;
        }

        if (TryHandlePickupClick(true))
        {
            suppressNextCraftingToggle = true;
        }
    }

    private bool TryHandlePickupClick(bool singlePickup)
    {
        if (!AllowPickupOnClick || IsInventoryUiLocked())
        {
            return false;
        }

        Player player = ResolvePlayer();
        if (player == null)
        {
            return false;
        }

        if (TryHandleFocusedRobotArmPickup(player, out bool blockPickup))
        {
            StopPickupRoutine();
            SuppressPickupPreviewAfterPickup();
            suppressCraftingToggleFrame = Time.frameCount;
            return true;
        }

        if (blockPickup)
        {
            StopPickupRoutine();
            suppressCraftingToggleFrame = Time.frameCount;
            return true;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        bool hasFocusedConveyor = TryGetFocusedConveyorBlock(player, out Block focusedConveyorBlock);
        int maxFocusedConveyorPickupCount = singlePickup ? 1 : int.MaxValue;
        if (hasFocusedConveyor
            && TryPickupFocusedConveyorItem(player, focusedConveyorBlock, FocusedPickupRange, maxFocusedConveyorPickupCount))
        {
            StopPickupRoutine();
            SuppressPickupPreviewAfterPickup();
            suppressCraftingToggleFrame = Time.frameCount;
            return true;
        }

        StopPickupRoutine();
        bool allowFocusedConveyorPickup = !hasFocusedConveyor;
        if (!TryPickupOneItemForClick(player, terrain, allowFocusedConveyorPickup))
        {
            return false;
        }

        SuppressPickupPreviewAfterPickup();
        suppressCraftingToggleFrame = Time.frameCount;
        if (!singlePickup)
        {
            pickupRoutine = StartCoroutine(PickupRoutine(player, terrain, true, allowFocusedConveyorPickup));
        }

        return true;
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
        float range = GetStandingTilePickupRange();
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
            Vector2Int currentCoordinate = ResolveStandingCoordinate(player);

            bool picked = TryPickupOneItemAtCoordinate(terrain, player, currentCoordinate, pickupOrigin, range, allowFocusedConveyorPickup);
            if (!picked)
            {
                break;
            }

            SuppressPickupPreviewAfterPickup();
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
        Vector2Int currentCoordinate = ResolveStandingCoordinate(player);
        float range = GetStandingTilePickupRange();

        if (TryPickupOneItemAtCoordinate(terrain, player, currentCoordinate, pickupOrigin, range, allowFocusedConveyorPickup))
        {
            return true;
        }

        return false;
    }

    protected virtual bool AllowPickupOnClick => enablePickupOnClick;

    protected virtual bool TryPickupOneItemAtCoordinate(TerrainGenerator terrain, Player player, Vector2Int coordinate, Vector3 pickupOrigin, float pickupRange, bool allowFocusedConveyorPickup = true)
    {
        if (terrain == null || player == null)
        {
            return false;
        }

        int targetSlotIndex = GetPickupSlotIndex();
        if (targetSlotIndex < 0)
        {
            return false;
        }

        int preferredItemId = GetPreferredPickupItemId();
        if (allowFocusedConveyorPickup
            && TryGetFocusedConveyorBlock(player, out Block focusedConveyorBlock)
            && TryPickupFocusedConveyorItem(player, focusedConveyorBlock, FocusedPickupRange))
        {
            return true;
        }

        if (TryPickupFocusedBoxToBag(player, pickupOrigin, FocusedPickupRange, targetSlotIndex, preferredItemId))
        {
            return true;
        }

        if (!TryGetGroundPickupBlock(terrain, coordinate, out Block block))
        {
            return false;
        }

        return block.TryPickupOneFloorObjectToBag(player, pickupOrigin, pickupRange, targetSlotIndex, preferredItemId);
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

    protected virtual bool TryPickupFocusedConveyorItem(Player player, Block focusedConveyorBlock, float pickupRange, int maxPickupCount = int.MaxValue)
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
            ResolvePickupOrigin(player),
            pickupRange,
            targetSlotIndex,
            GetPreferredPickupItemId(),
            maxPickupCount);
    }

    protected float GetPickupRange()
    {
        return Mathf.Max(0.01f, pickupRange);
    }

    private static float GetStandingTilePickupRange()
    {
        return StandingTilePickupRange;
    }

    protected static bool TryGetFocusedConveyorBlock(Player player, out Block focusedConveyorBlock)
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

    protected static bool TryGetGroundPickupBlock(TerrainGenerator terrain, Vector2Int coordinate, out Block block)
    {
        block = null;
        return terrain != null
               && terrain.TryGetLoadedBlock(coordinate, out block)
               && block != null
               && block.Type == Block.BlockType.Ground;
    }

    protected static bool TryPickupFocusedBoxToBag(
        Player player,
        Vector3 pickupOrigin,
        float pickupRange,
        int preferredSlotIndex,
        int preferredItemId)
    {
        PlayerController playerController = player != null ? player.GetComponent<PlayerController>() : null;
        return playerController != null
               && playerController.TryGetFocusedBoxObject(out BoxObject focusedBoxObject)
               && focusedBoxObject != null
               && focusedBoxObject.TryPickupContainedObjectToBag(player, pickupOrigin, pickupRange, preferredSlotIndex, preferredItemId);
    }

    protected static bool TryPickupFocusedBoxToHand(Player player, Vector3 pickupOrigin, float pickupRange)
    {
        PlayerController playerController = player != null ? player.GetComponent<PlayerController>() : null;
        return playerController != null
               && playerController.TryGetFocusedBoxObject(out BoxObject focusedBoxObject)
               && focusedBoxObject != null
               && focusedBoxObject.TryPickupContainedObjectToHand(player, pickupOrigin, pickupRange);
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

        if (!focusedRobotArm.CanTakeHeldItemFromSlot || boundBag == null || slotIndex < 0)
        {
            return false;
        }

        blockOtherPickup = true;
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

    private static Vector2Int ResolveStandingCoordinate(Player player)
    {
        return player != null
            ? ResolvePickupCoordinate(player.transform.position)
            : Vector2Int.zero;
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
