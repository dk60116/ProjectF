using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PlayerHUD : BagSlot
{
    [SerializeField]
    private List<BagSlot> bagSlots;
    [SerializeField]
    private HandSlot handSlot;

    private PlayerBag boundInventoryBag;
    private PlayerBag boundHandBag;
    private BagSlot expandedBagSlot;
    private bool isRefreshing;

    [SerializeField]
    private List<CreatingQueueSlot> craftingWaitingQueue; 

    [SerializeField, Min(0.05f)]
    private float craftingIngredientRefreshInterval = 0.2f;

    [SerializeField, Min(0f)]
    private float craftedPortableMoveInterval = 0.1f;

    private const float DefaultCraftingDurationSeconds = 5f;
    private readonly List<CraftingQueueEntry> craftingQueue = new List<CraftingQueueEntry>();
    private bool craftingQueueDirty;
    private float craftingIngredientRefreshTimer;
    private InstallationPlacementController installationPlacementController;
    private bool wasInventoryEditLocked;

    [SerializeField]
    private Button installButton;
    [SerializeField]
    private Button installCancelButton;
    [SerializeField]
    private Button installRotationButton;
    [SerializeField]
    private Button installCompleteButton;
    [SerializeField]
    private Button mapEditButton;
    [SerializeField]
    private Button mapEditCancelButton;
    [SerializeField]
    private Button mapEditRotationButton;
    [SerializeField]
    private Button mapEditCompleteButton;
    [SerializeField]
    private Button mapEditPackButton;
    [SerializeField]
    private Button mapEditUndoButton;
    [SerializeField, Min(0.01f)]
    private float mapEditButtonExpandDuration = 0.08f;
    [SerializeField, Min(0f)]
    private float mapEditButtonExpandStagger = 0.02f;

    [SerializeField]
    private InteractionButton InteractionButton;
    [SerializeField]
    private InteractionButton DoorInteractionButton;
    [SerializeField]
    private Button ItemFilterButton;

    [SerializeField]
    private FilterSelectUI itemFilterUI;
    private int itemFilterUiOpenedFrame = -1;

    [SerializeField]
    private TrainStationFilter trainStationFilter;

    [SerializeField]
    private TrainFilter trainFilter;

    [SerializeField]
    private MapPaper mapPaper;
    [SerializeField]
    private ObjectInfoPanel objectInfoPanel;
    [SerializeField, Min(0.02f)]
    private float objectInfoPanelRefreshInterval = 0.2f;

    private TerrainGenerator cachedTerrainGenerator;
    private BoxObject currentInteractionBoxObject;
    private FenceDoor currentInteractionDoorObject;
    private Resource currentInteractionResource;
    private MapObject currentInteractionMapObject;
    private MapObject currentObjectInfoTarget;
    private UtilityPole currentObjectInfoSupplyRangePole;
    private MapObject lastYellowObjectInfoFocusTarget;
    private bool currentObjectInfoOpenedByYellowFocus;
    private float nextObjectInfoPanelRefreshTime;
    private int lastObservedHandItemId = -2;
    private int lastObservedHandItemCount = -1;
    private int lastObservedHandMaxCount = -1;
    private bool lastObservedHandAllowsZeroCountDisplay;
    private bool mapEditButtonsInitialized;
    private bool lastInstallActionButtonsVisible;
    private bool lastMapEditExtraButtonsVisible;
    private bool pendingBagRefreshAfterCraftingVisibilityChange;
    private bool isBagRefreshQueued;
    private int queuedBagRefreshFrame = -1;
    private bool hudReferencesResolved;
    private bool installModeButtonsResolved;
    private Transform cachedMapEditRoot;
    private InteractionButton boundInteractionButton;
    private InteractionButton boundDoorInteractionButton;
    private readonly Dictionary<Button, Vector2> cachedInstallButtonPositions = new Dictionary<Button, Vector2>();
    private readonly Dictionary<Button, Vector2> cachedMapEditButtonPositions = new Dictionary<Button, Vector2>();
    private Sequence mapEditButtonAnimationSequence;
    private bool mapEditButtonsAnimating;


    private class CraftingQueueEntry
    {
        public int itemId;
        public int outputCount;
        public int remainingOutputCount;
        public float remainingTime;
        public float duration;
        public readonly List<CraftingTreeRuntime.IngredientEntry> refundIngredients;

        public CraftingQueueEntry(int itemId, int outputCount, float duration, List<CraftingTreeRuntime.IngredientEntry> refundIngredients = null)
        {
            this.itemId = itemId;
            this.outputCount = Mathf.Max(1, outputCount);
            remainingOutputCount = this.outputCount;
            this.duration = Mathf.Max(0.01f, duration);
            remainingTime = this.duration;
            this.refundIngredients = refundIngredients != null
                ? new List<CraftingTreeRuntime.IngredientEntry>(refundIngredients)
                : new List<CraftingTreeRuntime.IngredientEntry>();
        }
    }

    private void Awake()
    {
        SubscribeSlotEvents();
        ResolveHudReferences(true);
        ClearObjectInfoPanelState();
        EnsureInstallationPlacementController();
        BindItemFilterButton();
        HideItemFilterUIImmediate();
        UpdateInstallModeButtons();
        UpdateInteractionButtonState();
        UpdateItemFilterButtonState();
        RefreshBag(null);
        RefreshCraftingQueueSlots(true);
        EnsureMapPaperBinding();
    }

    private void Start()
    {
        EnsureInitialBagBinding();
    }

    private void OnEnable()
    {
        ResolveHudReferences(true);
        ClearObjectInfoPanelState();
        EnsureInstallationPlacementController();
        BindItemFilterButton();
        HideItemFilterUIImmediate();
        UpdateInstallModeButtons();
        UpdateInteractionButtonState();
        UpdateItemFilterButtonState();
        EnsureInitialBagBinding();
        EnsureMapPaperBinding();
    }

    private void OnDisable()
    {
        CollapseExpandedBagSlot(true);
        UnbindCurrentBag();
        isBagRefreshQueued = false;
        queuedBagRefreshFrame = -1;
        pendingBagRefreshAfterCraftingVisibilityChange = false;
        ClearObjectInfoPanelState();
    }

    private void ResolveHudReferences(bool force = false)
    {
        if (!force && hudReferencesResolved)
        {
            return;
        }

        if (force)
        {
            installModeButtonsResolved = false;
            boundInteractionButton = null;
            boundDoorInteractionButton = null;
        }

        ResolveInstallModeButtons();
        ResolveInteractionButton();
        ResolveDoorInteractionButton();
        ResolveItemFilterButton();
        ResolveItemFilterUI();
        ResolveMapPaper();
        ResolveObjectInfoPanel();
        EnsureHudButtonHoverTweens();
        hudReferencesResolved = true;
    }

    private void Update()
    {
        EnsureHandBagBinding();
        PollHandBagChanges();
        ResolveHudReferences();
        UpdateInstallModeButtons();
        ProcessQueuedBagRefresh();
        UpdateInteractionButtonState();
        UpdateObjectInfoPanelState();
        HandleInteractionButtonKeyboardInput();
        UpdateItemFilterButtonState();
        UpdateInventoryEditLockState();
        UpdateCraftingQueue(Time.deltaTime);
        UpdateCraftingIngredientRefresh(Time.deltaTime);
        EnsureMapPaperBinding();

        if (IsInventoryEditLocked())
        {
            return;
        }

        if (!TryGetPrimaryPointerDown(out Vector2 pointerPosition))
        {
            return;
        }

        if (itemFilterUI != null && itemFilterUI.gameObject.activeSelf)
        {
            if (itemFilterUiOpenedFrame == Time.frameCount)
            {
                return;
            }

            bool isPointerOverFilterUi = IsPointerOverItemFilterUiArea(pointerPosition);
            if (!isPointerOverFilterUi)
            {
                itemFilterUI.gameObject.SetActive(false);
                itemFilterUiOpenedFrame = -1;
            }
            else
            {
                return;
            }
        }

        BagSlot expandedSlot = BagSlot.ExpandedSlot;
        if (expandedSlot == null)
        {
            return;
        }

        if (IsPointerOverExpandedBagArea(expandedSlot, pointerPosition))
        {
            return;
        }

        if (expandedSlot == expandedBagSlot)
        {
            CollapseExpandedBagSlot(false);
        }
        else
        {
            BagSlot.CloseAnyExpanded(false);
        }
    }

    public void Bind(PlayerBag bag)
    {
        if (boundInventoryBag == bag)
        {
            RefreshBag(bag);
            return;
        }

        UnbindCurrentBag();
        boundInventoryBag = bag;

        if (boundInventoryBag != null)
        {
            boundInventoryBag.Changed += HandleBagChanged;
        }

        RefreshBag(boundInventoryBag);
        BindHandBag(GetPlayerHandBag());
    }

    public void RefreshBag(PlayerBag bag)
    {
        if (bagSlots == null)
        {
            return;
        }

        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;

        if (expandedBagSlot != null && !expandedBagSlot.IsCraftingExpanded)
        {
            expandedBagSlot = null;
        }

        BagSlot visibleExpandedSlot = GetVisibleExpandedBagSlot();
        int visibleSlotCount = bag != null ? bag.SlotCount : 0;
        for (int i = 0; i < bagSlots.Count; i++)
        {
            BagSlot slot = bagSlots[i];
            if (slot == null)
            {
                continue;
            }

            bool isWithinCapacity = i < visibleSlotCount;
            if (!isWithinCapacity)
            {
                slot.Bind(null, -1, -1, 0, 0);
                slot.SetSlotVisible(false);
                if (slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(false);
                }
                continue;
            }

            if (!slot.gameObject.activeSelf)
            {
                slot.gameObject.SetActive(true);
            }

            bool shouldShowSlot = visibleExpandedSlot == null || slot == visibleExpandedSlot;
            slot.SetSlotVisible(shouldShowSlot);
            int slotItemId = bag.GetSlotItemId(i);
            int slotItemCount = bag.GetSlotCount(i);
            bool allowZeroCountDisplay = TryApplyVisualPreservedSlotDisplay(
                bag,
                i,
                ref slotItemId,
                ref slotItemCount);
            slot.Bind(
                bag,
                i,
                slotItemId,
                slotItemCount,
                bag.GetSlotMaxCount(i),
                allowZeroCountDisplay);
        }

        if (visibleExpandedSlot == null)
        {
            RestoreAllBagSlotVisibility(visibleSlotCount);
        }

        RefreshHandSlot(boundHandBag);
        isRefreshing = false;

        if (pendingBagRefreshAfterCraftingVisibilityChange)
        {
            pendingBagRefreshAfterCraftingVisibilityChange = false;
            QueueBagRefresh();
        }
    }

    private void HandleBagChanged()
    {
        RefreshBag(boundInventoryBag);
        RefreshVisibleCraftingUi();
    }

    private void HandleHandBagChanged()
    {
        RefreshHandSlot(boundHandBag);
        RefreshVisibleCraftingUi();
    }

    private void EnsureInitialBagBinding()
    {
        if (boundInventoryBag != null)
        {
            RefreshBag(boundInventoryBag);
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return;
        }

        PlayerBag activeBag = GameManager.Instance.Player.GetBag();
        if (activeBag != null)
        {
            Bind(activeBag);
        }

        BindHandBag(GetPlayerHandBag());
    }

    private void UnbindCurrentBag()
    {
        if (boundInventoryBag != null)
        {
            boundInventoryBag.Changed -= HandleBagChanged;
            boundInventoryBag = null;
        }

        if (boundHandBag != null)
        {
            boundHandBag.Changed -= HandleHandBagChanged;
            boundHandBag = null;
        }

        RefreshBag(null);
    }

    private void BindHandBag(PlayerBag handBag)
    {
        if (boundHandBag == handBag)
        {
            RefreshHandSlot(handBag);
            return;
        }

        if (boundHandBag != null)
        {
            boundHandBag.Changed -= HandleHandBagChanged;
        }

        boundHandBag = handBag;
        if (boundHandBag != null)
        {
            boundHandBag.Changed += HandleHandBagChanged;
        }

        ResetObservedHandState();
        RefreshHandSlot(boundHandBag);
    }

    private void RefreshHandSlot(PlayerBag handBag)
    {
        if (handSlot == null)
        {
            return;
        }

        if (handBag == null)
        {
            handSlot.Bind(null, -1, -1, 0, 0);
            return;
        }

        handBag.RefreshExternalStackCounts(false);
        ResolveHandSlotDisplay(
            handBag,
            out int handItemId,
            out int handItemCount,
            out int handMaxCount,
            out bool allowZeroCountDisplay);
        handSlot.Bind(handBag, 0, handItemId, handItemCount, handMaxCount, allowZeroCountDisplay);
        UpdateObservedHandState(handItemId, handItemCount, handMaxCount, allowZeroCountDisplay);
    }

    private PlayerBag GetPlayerHandBag()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return null;
        }

        return GameManager.Instance.Player.GetHandBag();
    }

    private void EnsureHandBagBinding()
    {
        PlayerBag currentHandBag = GetPlayerHandBag();
        if (currentHandBag == null)
        {
            return;
        }

        if (boundHandBag != currentHandBag)
        {
            BindHandBag(currentHandBag);
        }
    }

    private void PollHandBagChanges()
    {
        if (boundHandBag == null)
        {
            return;
        }

        boundHandBag.RefreshExternalStackCounts(false);
        ResolveHandSlotDisplay(
            boundHandBag,
            out int handItemId,
            out int handItemCount,
            out int handMaxCount,
            out bool allowZeroCountDisplay);

        if (handItemId == lastObservedHandItemId
            && handItemCount == lastObservedHandItemCount
            && handMaxCount == lastObservedHandMaxCount
            && allowZeroCountDisplay == lastObservedHandAllowsZeroCountDisplay)
        {
            return;
        }

        RefreshHandSlot(boundHandBag);
        RefreshVisibleCraftingUi();
    }

    private void ResetObservedHandState()
    {
        lastObservedHandItemId = -2;
        lastObservedHandItemCount = -1;
        lastObservedHandMaxCount = -1;
        lastObservedHandAllowsZeroCountDisplay = false;
    }

    private void UpdateObservedHandState(int itemId, int itemCount, int maxItemCount, bool allowZeroCountDisplay)
    {
        lastObservedHandItemId = itemId;
        lastObservedHandItemCount = itemCount;
        lastObservedHandMaxCount = maxItemCount;
        lastObservedHandAllowsZeroCountDisplay = allowZeroCountDisplay;
    }

    private bool TryApplyBlueprintHandSlotDisplay(ref int itemId, ref int itemCount)
    {
        if (itemCount > 0)
        {
            return false;
        }

        EnsureInstallationPlacementController();
        if (installationPlacementController == null
            || !installationPlacementController.TryGetActiveBlueprintHudItemId(out int blueprintItemId))
        {
            return false;
        }

        itemId = blueprintItemId;
        itemCount = 0;
        return itemId >= 0;
    }

    private void ResolveHandSlotDisplay(
        PlayerBag handBag,
        out int itemId,
        out int itemCount,
        out int maxItemCount,
        out bool allowZeroCountDisplay)
    {
        itemId = handBag != null ? handBag.GetSlotItemId(0) : -1;
        itemCount = handBag != null ? handBag.GetSlotCount(0) : 0;
        maxItemCount = handBag != null ? handBag.GetSlotMaxCount(0) : 0;
        allowZeroCountDisplay = TryApplyVisualPreservedSlotDisplay(
            handBag,
            0,
            ref itemId,
            ref itemCount);
        if (!allowZeroCountDisplay)
        {
            allowZeroCountDisplay = TryApplyBlueprintHandSlotDisplay(ref itemId, ref itemCount);
        }
    }

    private bool TryApplyVisualPreservedSlotDisplay(
        PlayerBag bag,
        int slotIndex,
        ref int itemId,
        ref int itemCount)
    {
        if (bag == null || itemCount > 0)
        {
            return false;
        }

        int displayItemId = bag.GetSlotDisplayItemId(slotIndex);
        if (displayItemId < 0)
        {
            return false;
        }

        itemId = displayItemId;
        itemCount = 0;
        return true;
    }

    private void EnsureInstallationPlacementController()
    {
        if (installationPlacementController == null)
        {
            installationPlacementController = GetComponent<InstallationPlacementController>();
            if (installationPlacementController == null)
            {
                installationPlacementController = gameObject.AddComponent<InstallationPlacementController>();
            }
        }

        installationPlacementController.SetInstallButtons(InstallButton, InstallCancelButton, InstallRotationButton, InstallCompleteButton);
        installationPlacementController.SetMapEditButtons(MapEditButton, MapEditCancelButton, MapEditRotationButton, MapEditCompleteButton, mapEditPackButton, mapEditUndoButton);
    }

    private void ResolveInstallModeButtons()
    {
        if (installModeButtonsResolved)
        {
            return;
        }

        installButton = ResolveButtonReference(installButton, "InstallButton");
        Transform installRoot = installButton != null ? installButton.transform.parent : null;
        installCancelButton = ResolveButtonReferenceInRoot(installCancelButton, installRoot, "InstallCancelButton", "CancelButton");
        installRotationButton = ResolveButtonReferenceInRoot(installRotationButton, installRoot, "InstallRotationButton", "RotateButton", "RotationButton");
        installCompleteButton = ResolveButtonReferenceInRoot(installCompleteButton, installRoot, "InstallCompleteButton", "CompleteButton");

        if (cachedMapEditRoot == null)
        {
            cachedMapEditRoot = FindDescendantByName(transform, "MapEdit");
        }

        Transform mapEditRoot = cachedMapEditRoot;
        mapEditButton = ResolveButtonReferenceInRoot(mapEditButton, mapEditRoot, "MapEditButton");
        mapEditCancelButton = ResolveButtonReferenceInRoot(mapEditCancelButton, mapEditRoot, "CancelButton");
        mapEditRotationButton = ResolveButtonReferenceInRoot(mapEditRotationButton, mapEditRoot, "RotationButton");
        mapEditCompleteButton = ResolveButtonReferenceInRoot(mapEditCompleteButton, mapEditRoot, "CompleteButton");
        mapEditPackButton = ResolveButtonReferenceInRoot(mapEditPackButton, mapEditRoot, "PackButton", "Pack");
        mapEditUndoButton = ResolveButtonReferenceInRoot(mapEditUndoButton, mapEditRoot, "UnDoButton", "UndoButton");
        WarmAnimatedButtonLayoutPositions(
            installCancelButton,
            installRotationButton,
            installCompleteButton,
            mapEditCancelButton,
            mapEditRotationButton,
            mapEditCompleteButton,
            mapEditPackButton,
            mapEditUndoButton);
        CacheAnimatedButtonPositions(
            installButton,
            installCancelButton,
            installRotationButton,
            installCompleteButton,
            mapEditButton,
            mapEditCancelButton,
            mapEditRotationButton,
            mapEditCompleteButton,
            mapEditPackButton,
            mapEditUndoButton);

        installModeButtonsResolved = true;
    }

    private void ResolveMapPaper()
    {
        if (mapPaper != null)
        {
            return;
        }

        Transform paperTransform = transform.Find("Map/Paper");
        if (paperTransform == null)
        {
            paperTransform = FindDescendantByName(transform, "Paper");
        }

        if (paperTransform == null)
        {
            return;
        }

        mapPaper = paperTransform.GetComponent<MapPaper>();
        if (mapPaper == null)
        {
            mapPaper = paperTransform.gameObject.AddComponent<MapPaper>();
        }
    }

    private void ResolveObjectInfoPanel()
    {
        if (objectInfoPanel != null)
        {
            return;
        }

        Transform panelTransform = FindDescendantByName(transform, "ObjectInfoPanel");
        if (panelTransform != null)
        {
            objectInfoPanel = panelTransform.GetComponent<ObjectInfoPanel>();
        }

        if (objectInfoPanel == null)
        {
            objectInfoPanel = GetComponentInChildren<ObjectInfoPanel>(true);
        }
    }

    private void ResolveInteractionButton()
    {
        if (InteractionButton != null)
        {
            BindInteractionButton();
            return;
        }

        Transform buttonTransform = FindDescendantByName(transform, "InteractionButton");
        if (buttonTransform != null)
        {
            InteractionButton = buttonTransform.GetComponent<InteractionButton>();
        }

        if (InteractionButton == null)
        {
            InteractionButton = GetComponentInChildren<InteractionButton>(true);
        }

        BindInteractionButton();
    }

    private void ResolveDoorInteractionButton()
    {
        if (DoorInteractionButton != null)
        {
            BindDoorInteractionButton();
            return;
        }

        Transform buttonTransform = FindDescendantByName(transform, "DoorInteractionButton");
        if (buttonTransform == null)
        {
            buttonTransform = FindDescendantByName(transform, "DoorInteraction");
        }

        if (buttonTransform != null)
        {
            DoorInteractionButton = buttonTransform.GetComponent<InteractionButton>();
        }

        BindDoorInteractionButton();
    }

    private void ResolveItemFilterButton()
    {
        ItemFilterButton = ResolveButtonReference(ItemFilterButton, "ItemFilterButton", "FilterButton");
    }

    private void ResolveItemFilterUI()
    {
        if (itemFilterUI != null)
        {
            return;
        }

        itemFilterUI = GetComponentInChildren<FilterSelectUI>(true);
        if (itemFilterUI == null)
        {
            Transform filterUiTransform = FindDescendantByName(transform, "FilterSelectUI");
            if (filterUiTransform != null)
            {
                itemFilterUI = filterUiTransform.GetComponent<FilterSelectUI>();
            }
        }
    }

    private Button ResolveButtonReference(Button currentButton, params string[] candidateNames)
    {
        if (currentButton != null)
        {
            return currentButton;
        }

        return ResolveButtonReferenceInRoot(null, transform, candidateNames);
    }

    private void EnsureHudButtonHoverTweens()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || button.GetComponent<HUDButtonHoverTween>() != null)
            {
                continue;
            }

            button.gameObject.AddComponent<HUDButtonHoverTween>();
        }
    }

    private void EnsureMapPaperBinding()
    {
        if (mapPaper == null)
        {
            return;
        }

        Transform targetTransform = GameManager.Instance != null && GameManager.Instance.Player != null
            ? GameManager.Instance.Player.transform
            : null;
        mapPaper.Bind(ResolveTerrainGenerator(), targetTransform);
    }

    private TerrainGenerator ResolveTerrainGenerator()
    {
        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = TerrainGenerator.ResolveActive();
        }

        return cachedTerrainGenerator;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
        {
            return null;
        }

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            Transform candidate = descendants[i];
            if (candidate != null && candidate.name == targetName)
            {
                return candidate;
            }
        }

        return null;
    }

    private void UpdateInstallModeButtons()
    {
        bool isInstallModeActive = GameManager.Instance != null && GameManager.Instance.InstallationPlacementActive;
        bool isMapEditModeActive = IsMapEditModeActive();
        bool showInstallActionButtons = isInstallModeActive && !isMapEditModeActive;
        bool showMapEditActionButtons = isMapEditModeActive;

        if (mapEditButton != null && !mapEditButtonsAnimating)
        {
            bool shouldShowMapEditButton = !showMapEditActionButtons;
            if (mapEditButton.gameObject.activeSelf != shouldShowMapEditButton)
            {
                mapEditButton.gameObject.SetActive(shouldShowMapEditButton);
            }
        }

        if (!mapEditButtonsInitialized)
        {
            SetAnimatedButtonVisibleImmediate(installCancelButton, showInstallActionButtons);
            SetAnimatedButtonVisibleImmediate(installRotationButton, showInstallActionButtons);
            SetAnimatedButtonVisibleImmediate(installCompleteButton, showInstallActionButtons);
            SetAnimatedButtonVisibleImmediate(mapEditCancelButton, showMapEditActionButtons);
            SetAnimatedButtonVisibleImmediate(mapEditRotationButton, showMapEditActionButtons);
            SetAnimatedButtonVisibleImmediate(mapEditCompleteButton, showMapEditActionButtons);
            SetAnimatedButtonVisibleImmediate(mapEditPackButton, showMapEditActionButtons);
            SetAnimatedButtonVisibleImmediate(mapEditUndoButton, showMapEditActionButtons);
            lastInstallActionButtonsVisible = showInstallActionButtons;
            lastMapEditExtraButtonsVisible = showMapEditActionButtons;
            mapEditButtonsInitialized = true;
            return;
        }

        if (lastInstallActionButtonsVisible != showInstallActionButtons)
        {
            SetAnimatedButtonVisibleImmediate(installCancelButton, showInstallActionButtons);
            SetAnimatedButtonVisibleImmediate(installRotationButton, showInstallActionButtons);
            SetAnimatedButtonVisibleImmediate(installCompleteButton, showInstallActionButtons);
            lastInstallActionButtonsVisible = showInstallActionButtons;
        }

        if (lastMapEditExtraButtonsVisible != showMapEditActionButtons)
        {
            AnimateMapEditActionButtons(showMapEditActionButtons);
            lastMapEditExtraButtonsVisible = showMapEditActionButtons;
        }

        if (showMapEditActionButtons && !mapEditButtonsAnimating)
        {
            EnsureVisibleMapEditActionButtonsInteractive();
        }
    }

    private void EnsureVisibleMapEditActionButtonsInteractive()
    {
        EnsureVisibleButtonInteractive(mapEditCancelButton);
        EnsureVisibleButtonInteractive(mapEditRotationButton);
        EnsureVisibleButtonInteractive(mapEditCompleteButton);
        EnsureVisibleButtonInteractive(mapEditPackButton);
        EnsureVisibleButtonInteractive(mapEditUndoButton);
    }

    private void EnsureVisibleButtonInteractive(Button button)
    {
        if (button == null || !button.gameObject.activeSelf)
        {
            return;
        }

        NormalizeButtonCanvasGroup(button);
        SetButtonRaycastTargetsEnabled(button, true);
    }

    private bool IsMapEditModeActive()
    {
        return installationPlacementController != null && installationPlacementController.MapEditModeActive;
    }

    private bool IsPlacementOrMapEditModeActive()
    {
        EnsureInstallationPlacementController();
        return installationPlacementController != null
               && installationPlacementController.PlacementOrMapEditModeActive;
    }

    private void AnimateMapEditActionButtons(bool shouldBeVisible)
    {
        List<Button> orderedButtons = GetButtonsInSiblingOrder(
            mapEditCancelButton,
            mapEditRotationButton,
            mapEditCompleteButton,
            mapEditPackButton,
            mapEditUndoButton);

        if (orderedButtons.Count == 0)
        {
            if (mapEditButton != null)
            {
                mapEditButton.gameObject.SetActive(!shouldBeVisible);
            }

            return;
        }

        if (mapEditButtonAnimationSequence != null && mapEditButtonAnimationSequence.IsActive())
        {
            mapEditButtonAnimationSequence.Kill();
        }

        ResetMapEditAnimationState(orderedButtons);
        mapEditButtonsAnimating = true;

        Vector3 sourceLocalPosition = mapEditButton != null
            ? GetButtonLocalPosition(mapEditButton)
            : Vector3.zero;

        if (shouldBeVisible && mapEditButton != null)
        {
            CaptureAnimatedButtonPosition(mapEditButton);
            sourceLocalPosition = GetButtonLocalPosition(mapEditButton);
        }

        Dictionary<Button, Vector3> targetLocalPositions = shouldBeVisible
            ? ResolveMapEditActionTargetLocalPositions(orderedButtons)
            : null;

        if (shouldBeVisible && mapEditButton != null)
        {
            mapEditButton.gameObject.SetActive(false);
        }

        SetMapEditLayoutEnabled(false);

        if (shouldBeVisible)
        {
            for (int i = 0; i < orderedButtons.Count; i++)
            {
                Button button = orderedButtons[i];
                if (button == null)
                {
                    continue;
                }

                LayoutElement layoutElement = EnsureButtonLayoutElement(button);
                if (layoutElement != null)
                {
                    layoutElement.ignoreLayout = true;
                }

                RectTransform rectTransform = button.transform as RectTransform;
                if (rectTransform != null)
                {
                    DOTween.Kill(rectTransform);
                    rectTransform.localPosition = sourceLocalPosition;
                }

                NormalizeButtonCanvasGroup(button);
                SetButtonRaycastTargetsEnabled(button, false);
                button.interactable = true;
                button.gameObject.SetActive(false);
            }

            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            for (int i = 0; i < orderedButtons.Count; i++)
            {
                Button animatedButton = orderedButtons[i];
                if (animatedButton == null)
                {
                    continue;
                }

                float delay = i * mapEditButtonExpandStagger;
                RectTransform animatedRectTransform = animatedButton.transform as RectTransform;
                Vector3 targetLocalPosition = targetLocalPositions != null
                    && targetLocalPositions.TryGetValue(animatedButton, out Vector3 resolvedTargetLocalPosition)
                    ? resolvedTargetLocalPosition
                    : sourceLocalPosition;
                Sequence buttonSequence = DOTween.Sequence().SetUpdate(true);
                buttonSequence.AppendCallback(() =>
                {
                    if (animatedButton == null)
                    {
                        return;
                    }

                    animatedButton.gameObject.SetActive(true);
                    SetButtonRaycastTargetsEnabled(animatedButton, false);
                    animatedButton.interactable = true;
                    if (animatedRectTransform != null)
                    {
                        animatedRectTransform.localPosition = sourceLocalPosition;
                    }
                });
                if (animatedRectTransform != null)
                {
                    buttonSequence.Append(animatedRectTransform
                        .DOLocalMove(targetLocalPosition, mapEditButtonExpandDuration)
                        .SetEase(Ease.OutCubic));
                }

                buttonSequence.AppendCallback(() =>
                {
                    if (animatedButton == null)
                    {
                        return;
                    }

                    RectTransform completedRectTransform = animatedButton.transform as RectTransform;
                    if (completedRectTransform != null)
                    {
                        completedRectTransform.localPosition = targetLocalPosition;
                    }

                    SetButtonRaycastTargetsEnabled(animatedButton, true);
                    animatedButton.interactable = true;
                });
                sequence.Insert(delay, buttonSequence);
            }

            sequence.OnComplete(() =>
            {
                CompleteMapEditButtonAnimation(orderedButtons, true);
            });
            sequence.OnKill(() =>
            {
                mapEditButtonsAnimating = false;
                mapEditButtonAnimationSequence = null;
            });
            mapEditButtonAnimationSequence = sequence;
            return;
        }

        Sequence hideSequence = DOTween.Sequence().SetUpdate(true);
        for (int i = orderedButtons.Count - 1; i >= 0; i--)
        {
            Button animatedButton = orderedButtons[i];
            if (animatedButton == null || !animatedButton.gameObject.activeSelf)
            {
                continue;
            }

            LayoutElement layoutElement = EnsureButtonLayoutElement(animatedButton);
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = true;
            }

            RectTransform rectTransform = animatedButton.transform as RectTransform;
            if (rectTransform != null)
            {
                DOTween.Kill(rectTransform);
            }

            NormalizeButtonCanvasGroup(animatedButton);
            SetButtonRaycastTargetsEnabled(animatedButton, false);
            animatedButton.interactable = true;

            float delay = (orderedButtons.Count - 1 - i) * mapEditButtonExpandStagger;
            RectTransform animatedRectTransform = animatedButton.transform as RectTransform;
            Sequence buttonSequence = DOTween.Sequence().SetUpdate(true);
            buttonSequence.AppendCallback(() =>
            {
                if (animatedButton == null)
                {
                    return;
                }

                animatedButton.gameObject.SetActive(true);
                SetButtonRaycastTargetsEnabled(animatedButton, false);
                animatedButton.interactable = true;
            });
            if (animatedRectTransform != null)
            {
                buttonSequence.Append(animatedRectTransform
                    .DOLocalMove(sourceLocalPosition, mapEditButtonExpandDuration * 0.85f)
                    .SetEase(Ease.InCubic));
            }

            buttonSequence.AppendCallback(() =>
            {
                if (animatedButton == null)
                {
                    return;
                }

                animatedButton.gameObject.SetActive(false);
            });
            hideSequence.Insert(delay, buttonSequence);
        }

        hideSequence.OnComplete(() =>
        {
            CompleteMapEditButtonAnimation(orderedButtons, false);
        });
        hideSequence.OnKill(() =>
        {
            mapEditButtonsAnimating = false;
            mapEditButtonAnimationSequence = null;
        });
        mapEditButtonAnimationSequence = hideSequence;
    }

    private void SetAnimatedButtonVisibleImmediate(Button button, bool isVisible)
    {
        if (button == null)
        {
            return;
        }

        LayoutElement layoutElement = EnsureButtonLayoutElement(button);
        RectTransform rectTransform = button.transform as RectTransform;
        if (rectTransform != null)
        {
            DOTween.Kill(rectTransform);
            rectTransform.localScale = Vector3.one;
        }
        ResetButtonHoverTween(button, false);

        if (layoutElement != null)
        {
            layoutElement.ignoreLayout = false;
        }

        NormalizeButtonCanvasGroup(button);
        SetButtonRaycastTargetsEnabled(button, isVisible);
        button.interactable = true;
        button.gameObject.SetActive(isVisible);
    }

    private void CompleteMapEditButtonAnimation(List<Button> orderedButtons, bool actionButtonsVisible)
    {
        if (!actionButtonsVisible && mapEditButton != null)
        {
            mapEditButton.gameObject.SetActive(true);
        }

        SetMapEditLayoutEnabled(true);
        RestoreAnimatedButtonGroupLayout(orderedButtons);
        if (actionButtonsVisible)
        {
            EnsureVisibleMapEditActionButtonsInteractive();
        }

        mapEditButtonsAnimating = false;
        mapEditButtonAnimationSequence = null;
    }

    private void NormalizeButtonCanvasGroup(Button button)
    {
        if (button == null)
        {
            return;
        }

        CanvasGroup canvasGroup = button.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private static void SetButtonRaycastTargetsEnabled(Button button, bool isEnabled)
    {
        if (button == null)
        {
            return;
        }

        Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null)
            {
                continue;
            }

            graphic.raycastTarget = isEnabled;
        }
    }

    private static List<Button> GetButtonsInSiblingOrder(params Button[] buttons)
    {
        List<Button> orderedButtons = new List<Button>();
        if (buttons == null)
        {
            return orderedButtons;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button != null)
            {
                orderedButtons.Add(button);
            }
        }

        orderedButtons.Sort((left, right) =>
        {
            if (left == null && right == null)
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
        });

        return orderedButtons;
    }

    private void RestoreAnimatedButtonGroupLayout(List<Button> buttons)
    {
        if (buttons == null)
        {
            return;
        }

        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button == null)
            {
                continue;
            }

            LayoutElement layoutElement = EnsureButtonLayoutElement(button);
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = false;
            }

            RectTransform rectTransform = button.transform as RectTransform;
            if (rectTransform != null && !IsLayoutManagedButton(button))
            {
                rectTransform.anchoredPosition = GetCachedAnimatedButtonPosition(button);
            }

            ResetButtonHoverTween(button, false);
        }

        ForceButtonGroupLayoutRebuild(null, buttons);
    }

    private void SetMapEditLayoutEnabled(bool isEnabled)
    {
        RectTransform mapEditRoot = mapEditButton != null
            ? mapEditButton.transform.parent as RectTransform
            : null;
        if (mapEditRoot == null)
        {
            return;
        }

        LayoutGroup layoutGroup = mapEditRoot.GetComponent<LayoutGroup>();
        if (layoutGroup != null)
        {
            layoutGroup.enabled = isEnabled;
        }

        ContentSizeFitter contentSizeFitter = mapEditRoot.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null)
        {
            contentSizeFitter.enabled = isEnabled;
        }

        if (isEnabled)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(mapEditRoot);
            Canvas.ForceUpdateCanvases();
        }
    }

    private void ResetMapEditAnimationState(List<Button> orderedButtons)
    {
        SetMapEditLayoutEnabled(true);

        if (orderedButtons == null)
        {
            return;
        }

        for (int i = 0; i < orderedButtons.Count; i++)
        {
            Button button = orderedButtons[i];
            if (button == null)
            {
                continue;
            }

            RectTransform rectTransform = button.transform as RectTransform;
            if (rectTransform != null)
            {
                DOTween.Kill(rectTransform);
                rectTransform.anchoredPosition = GetCachedAnimatedButtonPosition(button);
            }
            ResetButtonHoverTween(button, false);

            LayoutElement layoutElement = EnsureButtonLayoutElement(button);
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = false;
            }

            NormalizeButtonCanvasGroup(button);
            SetButtonRaycastTargetsEnabled(button, button.gameObject.activeSelf);
            button.interactable = true;
        }

        if (!IsMapEditModeActive() && mapEditButton != null)
        {
            mapEditButton.gameObject.SetActive(true);
        }

        ForceButtonGroupLayoutRebuild(mapEditButton, orderedButtons);
    }

    private Dictionary<Button, Vector3> ResolveMapEditActionTargetLocalPositions(List<Button> orderedButtons)
    {
        Dictionary<Button, Vector3> positions = new Dictionary<Button, Vector3>();
        if (orderedButtons == null || orderedButtons.Count == 0)
        {
            return positions;
        }

        RectTransform mapEditRoot = mapEditButton != null
            ? mapEditButton.transform.parent as RectTransform
            : null;
        if (mapEditRoot == null)
        {
            for (int i = 0; i < orderedButtons.Count; i++)
            {
                Button button = orderedButtons[i];
                if (button == null)
                {
                    continue;
                }

                positions[button] = GetButtonLocalPosition(button);
            }

            return positions;
        }

        Dictionary<Button, bool> activeStates = new Dictionary<Button, bool>();
        Dictionary<Button, bool> ignoreLayoutStates = new Dictionary<Button, bool>();
        bool mapEditButtonWasActive = mapEditButton != null && mapEditButton.gameObject.activeSelf;
        LayoutElement mapEditLayoutElement = EnsureButtonLayoutElement(mapEditButton);
        bool mapEditIgnoreLayout = mapEditLayoutElement != null && mapEditLayoutElement.ignoreLayout;

        SetMapEditLayoutEnabled(true);

        if (mapEditButton != null)
        {
            mapEditButton.gameObject.SetActive(false);
        }

        for (int i = 0; i < orderedButtons.Count; i++)
        {
            Button button = orderedButtons[i];
            if (button == null)
            {
                continue;
            }

            activeStates[button] = button.gameObject.activeSelf;

            LayoutElement layoutElement = EnsureButtonLayoutElement(button);
            ignoreLayoutStates[button] = layoutElement != null && layoutElement.ignoreLayout;

            button.gameObject.SetActive(true);
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = false;
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(mapEditRoot);
        Canvas.ForceUpdateCanvases();

        for (int i = 0; i < orderedButtons.Count; i++)
        {
            Button button = orderedButtons[i];
            if (button == null)
            {
                continue;
            }

            positions[button] = GetButtonLocalPosition(button);
        }

        if (mapEditLayoutElement != null)
        {
            mapEditLayoutElement.ignoreLayout = mapEditIgnoreLayout;
        }

        if (mapEditButton != null)
        {
            mapEditButton.gameObject.SetActive(mapEditButtonWasActive);
        }

        for (int i = 0; i < orderedButtons.Count; i++)
        {
            Button button = orderedButtons[i];
            if (button == null)
            {
                continue;
            }

            LayoutElement layoutElement = EnsureButtonLayoutElement(button);
            if (layoutElement != null && ignoreLayoutStates.TryGetValue(button, out bool ignoreLayout))
            {
                layoutElement.ignoreLayout = ignoreLayout;
            }

            if (activeStates.TryGetValue(button, out bool wasActive))
            {
                button.gameObject.SetActive(wasActive);
            }
        }

        return positions;
    }

    private static bool IsLayoutManagedButton(Button button)
    {
        if (button == null)
        {
            return false;
        }

        return button.transform.parent is RectTransform parent
            && parent.GetComponent<LayoutGroup>() != null;
    }

    private LayoutElement EnsureButtonLayoutElement(Button button)
    {
        if (button == null)
        {
            return null;
        }

        LayoutElement layoutElement = button.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = button.gameObject.AddComponent<LayoutElement>();
        }

        return layoutElement;
    }

    private static Vector3 GetButtonLocalPosition(Button button)
    {
        RectTransform rectTransform = button != null ? button.transform as RectTransform : null;
        return rectTransform != null ? rectTransform.localPosition : Vector3.zero;
    }

    private static void ResetButtonHoverTween(Button button, bool rebuildLayout)
    {
        HUDButtonHoverTween hoverTween = button != null ? button.GetComponent<HUDButtonHoverTween>() : null;
        hoverTween?.ResetHoverImmediate(rebuildLayout);
    }

    private void ForceButtonGroupLayoutRebuild(Button sourceButton, IEnumerable<Button> buttons)
    {
        HashSet<RectTransform> rebuiltRoots = new HashSet<RectTransform>();
        TryRebuildButtonLayoutRoot(sourceButton, rebuiltRoots);

        if (buttons == null)
        {
            return;
        }

        foreach (Button button in buttons)
        {
            TryRebuildButtonLayoutRoot(button, rebuiltRoots);
        }
    }

    private static void TryRebuildButtonLayoutRoot(Button button, HashSet<RectTransform> rebuiltRoots)
    {
        if (button == null || rebuiltRoots == null)
        {
            return;
        }

        RectTransform root = button.transform.parent as RectTransform;
        if (root == null || rebuiltRoots.Contains(root))
        {
            return;
        }

        rebuiltRoots.Add(root);
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    private void CacheAnimatedButtonPositions(params Button[] buttons)
    {
        if (buttons == null)
        {
            return;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
            {
                continue;
            }

            Dictionary<Button, Vector2> cache = GetAnimatedButtonPositionCache(button);
            if (cache.ContainsKey(button))
            {
                continue;
            }

            RectTransform rectTransform = button.transform as RectTransform;
            if (rectTransform == null)
            {
                continue;
            }

            cache[button] = rectTransform.anchoredPosition;
        }
    }

    private void CaptureAnimatedButtonPosition(Button button)
    {
        if (button == null)
        {
            return;
        }

        RectTransform rectTransform = button.transform as RectTransform;
        if (rectTransform == null)
        {
            return;
        }

        Dictionary<Button, Vector2> cache = GetAnimatedButtonPositionCache(button);
        cache[button] = rectTransform.anchoredPosition;
    }

    private void WarmAnimatedButtonLayoutPositions(params Button[] buttons)
    {
        if (buttons == null || buttons.Length == 0)
        {
            return;
        }

        Dictionary<RectTransform, List<Button>> buttonsByRoot = new Dictionary<RectTransform, List<Button>>();
        foreach (Button button in buttons)
        {
            if (button == null)
            {
                continue;
            }

            Dictionary<Button, Vector2> cache = GetAnimatedButtonPositionCache(button);
            if (cache.ContainsKey(button))
            {
                continue;
            }

            RectTransform root = button.transform.parent as RectTransform;
            if (root == null || root.GetComponent<LayoutGroup>() == null)
            {
                CaptureAnimatedButtonPosition(button);
                continue;
            }

            if (!buttonsByRoot.TryGetValue(root, out List<Button> groupedButtons))
            {
                groupedButtons = new List<Button>();
                buttonsByRoot[root] = groupedButtons;
            }

            groupedButtons.Add(button);
        }

        foreach ((RectTransform root, List<Button> groupedButtons) in buttonsByRoot)
        {
            Dictionary<Button, bool> activeStates = new Dictionary<Button, bool>();
            Dictionary<Button, bool> ignoreLayoutStates = new Dictionary<Button, bool>();

            for (int i = 0; i < groupedButtons.Count; i++)
            {
                Button button = groupedButtons[i];
                if (button == null)
                {
                    continue;
                }

                activeStates[button] = button.gameObject.activeSelf;
                LayoutElement layoutElement = EnsureButtonLayoutElement(button);
                ignoreLayoutStates[button] = layoutElement != null && layoutElement.ignoreLayout;

                button.gameObject.SetActive(true);
                if (layoutElement != null)
                {
                    layoutElement.ignoreLayout = false;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            Canvas.ForceUpdateCanvases();

            for (int i = 0; i < groupedButtons.Count; i++)
            {
                CaptureAnimatedButtonPosition(groupedButtons[i]);
            }

            for (int i = 0; i < groupedButtons.Count; i++)
            {
                Button button = groupedButtons[i];
                if (button == null)
                {
                    continue;
                }

                LayoutElement layoutElement = EnsureButtonLayoutElement(button);
                if (layoutElement != null && ignoreLayoutStates.TryGetValue(button, out bool ignoreLayout))
                {
                    layoutElement.ignoreLayout = ignoreLayout;
                }

                if (activeStates.TryGetValue(button, out bool wasActive))
                {
                    button.gameObject.SetActive(wasActive);
                }
            }
        }
    }

    private Vector2 GetCachedAnimatedButtonPosition(Button button)
    {
        if (button == null)
        {
            return Vector2.zero;
        }

        Dictionary<Button, Vector2> cache = GetAnimatedButtonPositionCache(button);
        if (cache.TryGetValue(button, out Vector2 cachedPosition))
        {
            return cachedPosition;
        }

        RectTransform rectTransform = button.transform as RectTransform;
        return rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero;
    }

    private Dictionary<Button, Vector2> GetAnimatedButtonPositionCache(Button button)
    {
        return IsMapEditAnimatedButton(button)
            ? cachedMapEditButtonPositions
            : cachedInstallButtonPositions;
    }

    private bool IsMapEditAnimatedButton(Button button)
    {
        return button == mapEditButton
               || button == mapEditCancelButton
               || button == mapEditRotationButton
               || button == mapEditCompleteButton
               || button == mapEditPackButton
               || button == mapEditUndoButton;
    }

    private void UpdateInteractionButtonState()
    {
        if (InteractionButton == null && DoorInteractionButton == null)
        {
            return;
        }

        if (GameManager.Instance == null
            || GameManager.Instance.Player == null
            || GameManager.Instance.PlayerInteractionLocked)
        {
            ClearInteractionButtonState();
            return;
        }

        Player currentPlayer = GameManager.Instance.Player;
        Transform bodyTransform = currentPlayer.BodyTransform != null ? currentPlayer.BodyTransform : currentPlayer.transform;
        if (bodyTransform == null)
        {
            ClearInteractionButtonState();
            return;
        }

        PlayerController playerController = currentPlayer.GetComponent<PlayerController>();
        if (playerController != null && playerController.IsResourceHarvestingActive)
        {
            ClearInteractionButtonState();
            return;
        }

        Vehicle mountedVehicle = playerController != null ? playerController.MountedVehicle : null;
        if (mountedVehicle != null)
        {
            currentInteractionBoxObject = null;
            currentInteractionDoorObject = null;
            currentInteractionResource = null;
            currentInteractionMapObject = mountedVehicle;
            SetActiveInteractionButton(InteractionButton, ResolveInteractionIcon(mountedVehicle, 1));
            return;
        }

        if (TryGetFocusedBoxObject(out BoxObject focusedBoxObject))
        {
            currentInteractionBoxObject = focusedBoxObject;
            currentInteractionDoorObject = null;
            currentInteractionResource = null;
            currentInteractionMapObject = null;
            SetActiveInteractionButton(InteractionButton, ResolveInteractionIcon(focusedBoxObject));
            return;
        }

        if (TryGetFocusedFenceDoor(out FenceDoor focusedFenceDoor))
        {
            currentInteractionBoxObject = null;
            currentInteractionDoorObject = focusedFenceDoor;
            currentInteractionResource = null;
            currentInteractionMapObject = null;
            SetActiveInteractionButton(ResolveDoorInteractionButtonForUse(), ResolveInteractionIcon(focusedFenceDoor));
            return;
        }

        MapObject focusedMapObject = null;
        if (TryGetFocusedMapObject(out focusedMapObject) && focusedMapObject is Vehicle)
        {
            Sprite vehicleIcon = ResolveInteractionIcon(focusedMapObject, 0);
            if (vehicleIcon != null)
            {
                currentInteractionBoxObject = null;
                currentInteractionDoorObject = null;
                currentInteractionResource = null;
                currentInteractionMapObject = focusedMapObject;
                SetActiveInteractionButton(InteractionButton, vehicleIcon);
                return;
            }
        }

        if (TryGetFocusedResource(out Resource focusedResource))
        {
            Sprite resourceIcon = ResolveInteractionIcon(focusedResource);
            if (resourceIcon != null)
            {
                currentInteractionBoxObject = null;
                currentInteractionDoorObject = null;
                currentInteractionResource = focusedResource;
                currentInteractionMapObject = null;
                SetActiveInteractionButton(InteractionButton, resourceIcon);
                return;
            }
        }

        if (focusedMapObject != null || TryGetFocusedMapObject(out focusedMapObject))
        {
            Sprite mapObjectIcon = ResolveInteractionIcon(focusedMapObject, 0);
            if (mapObjectIcon != null)
            {
                currentInteractionBoxObject = null;
                currentInteractionDoorObject = null;
                currentInteractionResource = null;
                currentInteractionMapObject = focusedMapObject;
                SetActiveInteractionButton(InteractionButton, mapObjectIcon);
                return;
            }
        }

        ClearInteractionButtonState();
    }

    private void ClearInteractionButtonState()
    {
        currentInteractionBoxObject = null;
        currentInteractionDoorObject = null;
        currentInteractionResource = null;
        currentInteractionMapObject = null;
        HideInteractionButton(InteractionButton);
        if (DoorInteractionButton != null && DoorInteractionButton != InteractionButton)
        {
            HideInteractionButton(DoorInteractionButton);
        }
    }

    private void SetActiveInteractionButton(InteractionButton activeButton, Sprite icon)
    {
        if (activeButton == null)
        {
            return;
        }

        if (InteractionButton != null && InteractionButton != activeButton)
        {
            HideInteractionButton(InteractionButton);
        }

        if (DoorInteractionButton != null && DoorInteractionButton != activeButton)
        {
            HideInteractionButton(DoorInteractionButton);
        }

        activeButton.SetIcon(icon);
        activeButton.SetVisible(true);
    }

    private static void HideInteractionButton(InteractionButton interactionButton)
    {
        if (interactionButton == null)
        {
            return;
        }

        interactionButton.SetIcon(null);
        interactionButton.SetVisible(false);
    }

    private void UpdateObjectInfoPanelState()
    {
        ResolveObjectInfoPanel();
        if (objectInfoPanel == null)
        {
            currentObjectInfoTarget = null;
            SetObjectInfoSupplyRangeVisual(null, false);
            return;
        }

        if (GameManager.Instance == null
            || GameManager.Instance.Player == null
            || GameManager.Instance.PlayerInteractionLocked)
        {
            ClearObjectInfoPanelState();
            return;
        }

        PlayerController playerController = ResolvePlayerController();
        if (TryGetSecondaryPointerDown(out Vector2 pointerPosition))
        {
            if (IsPointerOverObjectInfoBlockingUi(pointerPosition))
            {
                RefreshCurrentObjectInfoPanelTarget();
                return;
            }

            if (playerController != null
                && playerController.TryGetMouseFocusedMapObject(out MapObject mouseFocusedMapObject))
            {
                BindObjectInfoPanel(mouseFocusedMapObject, false);
                return;
            }

            ClearObjectInfoPanelState();
            return;
        }

        if (HandleYellowFocusObjectInfoChange())
        {
            return;
        }

        RefreshCurrentObjectInfoPanelTarget();
    }

    private bool HandleYellowFocusObjectInfoChange()
    {
        if (TryGetFocusedMapObject(out MapObject focusedMapObject))
        {
            if (lastYellowObjectInfoFocusTarget == focusedMapObject)
            {
                return false;
            }

            lastYellowObjectInfoFocusTarget = focusedMapObject;
            BindObjectInfoPanel(focusedMapObject, true);
            return true;
        }

        if (lastYellowObjectInfoFocusTarget == null)
        {
            return false;
        }

        lastYellowObjectInfoFocusTarget = null;
        if (currentObjectInfoOpenedByYellowFocus)
        {
            ClearObjectInfoPanelState();
            return true;
        }

        return false;
    }

    private void RefreshCurrentObjectInfoPanelTarget()
    {
        if (currentObjectInfoTarget == null)
        {
            return;
        }

        if (!currentObjectInfoTarget.gameObject.activeInHierarchy)
        {
            ClearObjectInfoPanelState();
            return;
        }

        if (objectInfoPanel != null
            && objectInfoPanel.IsBoundTo(currentObjectInfoTarget)
            && objectInfoPanel.gameObject.activeSelf)
        {
            float now = Time.unscaledTime;
            if (now >= nextObjectInfoPanelRefreshTime)
            {
                nextObjectInfoPanelRefreshTime = now + Mathf.Max(0.02f, objectInfoPanelRefreshInterval);
                objectInfoPanel.Refresh();
            }

            return;
        }

        BindObjectInfoPanel(currentObjectInfoTarget, currentObjectInfoOpenedByYellowFocus);
    }

    private void BindObjectInfoPanel(MapObject target, bool openedByYellowFocus)
    {
        if (target == null || objectInfoPanel == null)
        {
            ClearObjectInfoPanelState();
            return;
        }

        currentObjectInfoTarget = target;
        currentObjectInfoOpenedByYellowFocus = openedByYellowFocus;
        objectInfoPanel.Bind(target);
        nextObjectInfoPanelRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, objectInfoPanelRefreshInterval);
        SetObjectInfoSupplyRangeVisual(target, !openedByYellowFocus);
    }

    private void ClearObjectInfoPanelState()
    {
        SetObjectInfoSupplyRangeVisual(null, false);
        currentObjectInfoTarget = null;
        currentObjectInfoOpenedByYellowFocus = false;
        nextObjectInfoPanelRefreshTime = 0f;
        if (objectInfoPanel != null)
        {
            objectInfoPanel.Clear();
        }
    }

    private void SetObjectInfoSupplyRangeVisual(MapObject target, bool requested)
    {
        UtilityPole nextPole = requested ? ResolveUtilityPole(target) : null;
        if (currentObjectInfoSupplyRangePole == nextPole)
        {
            if (nextPole != null)
            {
                nextPole.SetSelectedSupplyRangeVisualRequested(true);
            }

            return;
        }

        if (currentObjectInfoSupplyRangePole != null)
        {
            currentObjectInfoSupplyRangePole.SetSelectedSupplyRangeVisualRequested(false);
        }

        currentObjectInfoSupplyRangePole = nextPole;
        if (currentObjectInfoSupplyRangePole != null)
        {
            currentObjectInfoSupplyRangePole.SetSelectedSupplyRangeVisualRequested(true);
        }
    }

    private static UtilityPole ResolveUtilityPole(MapObject target)
    {
        if (target == null)
        {
            return null;
        }

        UtilityPole pole = target as UtilityPole;
        if (pole != null)
        {
            return pole;
        }

        pole = target.GetComponent<UtilityPole>();
        if (pole != null)
        {
            return pole;
        }

        return target.GetComponentInChildren<UtilityPole>(true);
    }

    private void UpdateItemFilterButtonState()
    {
        if (ItemFilterButton == null)
        {
            return;
        }

        bool isVisible = false;
        if (GameManager.Instance != null
            && GameManager.Instance.Player != null
            && !GameManager.Instance.PlayerInteractionLocked)
        {
            PlayerController playerController = ResolvePlayerController();
            if (playerController != null && playerController.TryGetFocusedItemFilterMapObject(out _))
            {
                isVisible = true;
            }
        }

        if (!isVisible
            && itemFilterUI != null
            && itemFilterUI.gameObject.activeSelf
            && itemFilterUI.TryGetBoundTarget(out _))
        {
            isVisible = true;
        }

        if (ItemFilterButton.gameObject.activeSelf != isVisible)
        {
            ItemFilterButton.gameObject.SetActive(isVisible);
        }

        if (!isVisible && itemFilterUI != null && itemFilterUI.gameObject.activeSelf)
        {
            itemFilterUI.gameObject.SetActive(false);
            itemFilterUiOpenedFrame = -1;
        }
    }

    private static Sprite ResolveInteractionIcon(BoxObject boxObject)
    {
        if (boxObject == null || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        int itemId = boxObject.ResolveItemId();
        int preferredIconIndex = boxObject.IsOpen ? 1 : 0;
        return ResolveInteractionIcon(itemId, preferredIconIndex);
    }

    private static Sprite ResolveInteractionIcon(FenceDoor fenceDoor)
    {
        if (fenceDoor == null)
        {
            return null;
        }

        int itemId = fenceDoor.ResolveItemId();
        int preferredIconIndex = fenceDoor.IsOpen ? 1 : 0;
        return ResolveInteractionIcon(itemId, preferredIconIndex);
    }

    private static Sprite ResolveInteractionIcon(Resource resource)
    {
        if (resource == null)
        {
            return null;
        }

        Sprite harvestModeIcon = ResolveHarvestModeInteractionIcon(resource);
        if (harvestModeIcon != null)
        {
            return harvestModeIcon;
        }

        if (resource.TryPeekHarvestOutput(out int outputItemId, out _))
        {
            Sprite outputIcon = ResolveInteractionIcon(outputItemId, 0, true);
            if (outputIcon != null)
            {
                return outputIcon;
            }
        }

        return ResolveInteractionIcon(resource.ResolveItemId(), 0, true);
    }

    private static Sprite ResolveInteractionIcon(MapObject mapObject, int preferredIconIndex = 0)
    {
        if (mapObject == null)
        {
            return null;
        }

        ItemDefinition boundDefinition = mapObject.BoundItemDefinition;
        Sprite boundIcon = ResolveInteractionIcon(boundDefinition, preferredIconIndex, false);
        if (boundIcon != null)
        {
            return boundIcon;
        }

        return ResolveInteractionIcon(mapObject.ResolveItemId(), preferredIconIndex, false);
    }

    private static Sprite ResolveHarvestModeInteractionIcon(Resource resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource.ResolvedHarvestMode != Resource.HarvestMode.Cut)
        {
            return null;
        }

        Sprite boundIcon = ResolveInteractionIcon(resource.BoundItemDefinition, 0, false);
        if (boundIcon != null)
        {
            return boundIcon;
        }

        return ResolveInteractionIcon(resource.ResolveItemId(), 0, false);
    }

    private static Sprite ResolveInteractionIcon(int itemId, int preferredIconIndex)
    {
        return ResolveInteractionIcon(itemId, preferredIconIndex, false);
    }

    private static Sprite ResolveInteractionIcon(int itemId, int preferredIconIndex, bool allowItemIconFallback)
    {
        return ResolveInteractionIcon(GetItemDefinition(itemId), preferredIconIndex, allowItemIconFallback);
    }

    private static Sprite ResolveInteractionIcon(
        ItemDefinition definition,
        int preferredIconIndex,
        bool allowItemIconFallback)
    {
        if (definition == null)
        {
            return null;
        }

        if (definition.interactionButtonList == null || definition.interactionButtonList.Count <= 0)
        {
            return allowItemIconFallback ? definition.icon : null;
        }

        if (preferredIconIndex >= 0 && preferredIconIndex < definition.interactionButtonList.Count)
        {
            Sprite preferredIcon = definition.interactionButtonList[preferredIconIndex];
            if (preferredIcon != null)
            {
                return preferredIcon;
            }
        }

        Sprite closedIcon = definition.interactionButtonList[0];
        if (closedIcon != null)
        {
            return closedIcon;
        }

        for (int iconIndex = 0; iconIndex < definition.interactionButtonList.Count; iconIndex++)
        {
            Sprite fallbackIcon = definition.interactionButtonList[iconIndex];
            if (fallbackIcon != null)
            {
                return fallbackIcon;
            }
        }

        return allowItemIconFallback ? definition.icon : null;
    }

    private static Button ResolveButtonReferenceInRoot(Button currentButton, Transform root, params string[] candidateNames)
    {
        if (currentButton != null && root != null && currentButton.transform.IsChildOf(root))
        {
            return currentButton;
        }

        if (root == null || candidateNames == null || candidateNames.Length == 0)
        {
            return null;
        }

        return FindButtonByName(root.GetComponentsInChildren<Button>(true), candidateNames);
    }

    private static Button FindButtonByName(IReadOnlyList<Button> buttons, string[] candidateNames)
    {
        if (buttons == null || candidateNames == null || candidateNames.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < buttons.Count; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null)
            {
                continue;
            }

            if (Array.IndexOf(candidateNames, candidate.name) >= 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private InteractionButton ResolveDoorInteractionButtonForUse()
    {
        return DoorInteractionButton != null ? DoorInteractionButton : InteractionButton;
    }

    private void BindInteractionButton()
    {
        if (InteractionButton == null)
        {
            return;
        }

        if (boundInteractionButton == InteractionButton)
        {
            return;
        }

        InteractionButton.SetClickAction(HandleInteractionButtonClicked);
        boundInteractionButton = InteractionButton;
    }

    private void BindDoorInteractionButton()
    {
        if (DoorInteractionButton == null || DoorInteractionButton == InteractionButton)
        {
            return;
        }

        if (boundDoorInteractionButton == DoorInteractionButton)
        {
            return;
        }

        DoorInteractionButton.SetClickAction(HandleInteractionButtonClicked);
        boundDoorInteractionButton = DoorInteractionButton;
    }

    private void BindItemFilterButton()
    {
        if (ItemFilterButton == null)
        {
            return;
        }

        ItemFilterButton.onClick.RemoveListener(HandleItemFilterButtonClicked);
        ItemFilterButton.onClick.AddListener(HandleItemFilterButtonClicked);
    }

    private void HideItemFilterUIImmediate()
    {
        if (itemFilterUI != null && itemFilterUI.gameObject.activeSelf)
        {
            itemFilterUI.gameObject.SetActive(false);
        }
    }

    private void HandleInteractionButtonClicked()
    {
        if (IsPlacementOrMapEditModeActive())
        {
            return;
        }

        if (currentInteractionBoxObject != null)
        {
            currentInteractionBoxObject.ToggleOpenState();
            UpdateInteractionButtonState();
            return;
        }

        if (currentInteractionDoorObject != null)
        {
            currentInteractionDoorObject.ToggleOpenState(ResolveCurrentPlayerInteractionPosition());
            UpdateInteractionButtonState();
            return;
        }

        if (currentInteractionResource != null)
        {
            PlayerController playerController = GameManager.Instance != null && GameManager.Instance.Player != null
                ? GameManager.Instance.Player.GetComponent<PlayerController>()
                : null;
            playerController?.RequestFocusedResourceHarvest(currentInteractionResource);
            UpdateInteractionButtonState();
            return;
        }

        if (currentInteractionMapObject != null)
        {
            if (currentInteractionMapObject is Vehicle vehicle)
            {
                Player currentPlayer = GameManager.Instance != null ? GameManager.Instance.Player : null;
                PlayerController playerController = currentPlayer != null
                    ? currentPlayer.GetComponent<PlayerController>()
                    : null;
                if (playerController != null && playerController.IsMountedOnVehicle(vehicle))
                {
                    playerController.TryDismountFromVehicle();
                }
                else
                {
                    vehicle.TryDockPlayer(currentPlayer);
                }
            }

            UpdateInteractionButtonState();
        }
    }

    private void HandleInteractionButtonKeyboardInput()
    {
        if (!Input.GetKeyDown(KeyCode.Space)
            || GameManager.Instance == null
            || GameManager.Instance.PlayerInteractionLocked
            || IsPlacementOrMapEditModeActive())
        {
            return;
        }

        InteractionButton activeButton = currentInteractionDoorObject != null
            ? ResolveDoorInteractionButtonForUse()
            : InteractionButton;
        bool hasInteractionTarget = currentInteractionBoxObject != null
                                    || currentInteractionDoorObject != null
                                    || currentInteractionResource != null
                                    || currentInteractionMapObject != null;
        if (!hasInteractionTarget
            || activeButton == null
            || !activeButton.gameObject.activeInHierarchy)
        {
            return;
        }

        HandleInteractionButtonClicked();
    }

    private static Vector3 ResolveCurrentPlayerInteractionPosition()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return Vector3.zero;
        }

        Player player = GameManager.Instance.Player;
        Transform bodyTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
        return bodyTransform != null ? bodyTransform.position : player.transform.position;
    }

    private void HandleItemFilterButtonClicked()
    {
        if (itemFilterUI == null)
        {
            return;
        }

        bool shouldOpen = !itemFilterUI.gameObject.activeSelf;
        if (shouldOpen)
        {
            PlayerController playerController = ResolvePlayerController();
            if (playerController == null || !playerController.TryGetFocusedItemFilterMapObject(out MapObject focusedMapObject))
            {
                return;
            }

            itemFilterUI.Bind(focusedMapObject);
        }

        itemFilterUI.gameObject.SetActive(shouldOpen);
        itemFilterUiOpenedFrame = shouldOpen ? Time.frameCount : -1;
    }

    private bool TryGetFocusedBoxObject(out BoxObject focusedBoxObject)
    {
        focusedBoxObject = null;
        PlayerController playerController = ResolvePlayerController();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedBoxObject(out focusedBoxObject);
    }

    private bool TryGetFocusedFenceDoor(out FenceDoor focusedFenceDoor)
    {
        focusedFenceDoor = null;
        PlayerController playerController = ResolvePlayerController();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedFenceDoor(out focusedFenceDoor);
    }

    private bool TryGetFocusedResource(out Resource focusedResource)
    {
        focusedResource = null;
        PlayerController playerController = ResolvePlayerController();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedResource(out focusedResource);
    }

    private bool TryGetFocusedMapObject(out MapObject focusedMapObject)
    {
        focusedMapObject = null;
        PlayerController playerController = ResolvePlayerController();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedMapObject(out focusedMapObject);
    }

    private PlayerController ResolvePlayerController()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return null;
        }

        return GameManager.Instance.Player.GetComponent<PlayerController>();
    }

    private void SubscribeSlotEvents()
    {
        if (bagSlots != null)
        {
            for (int i = 0; i < bagSlots.Count; i++)
            {
                BagSlot slot = bagSlots[i];
                if (slot == null)
                {
                    continue;
                }

                slot.CraftingVisibilityChanged -= HandleCraftingVisibilityChanged;
                slot.CraftingVisibilityChanged += HandleCraftingVisibilityChanged;
            }
        }

        if (handSlot != null)
        {
            handSlot.CraftingVisibilityChanged -= HandleCraftingVisibilityChanged;
            handSlot.CraftingVisibilityChanged += HandleCraftingVisibilityChanged;
        }
    }

    private void HandleCraftingVisibilityChanged(BagSlot slot, bool isVisible)
    {
        if (isVisible)
        {
            expandedBagSlot = slot;
        }
        else if (expandedBagSlot == slot)
        {
            expandedBagSlot = null;
        }

        if (isRefreshing)
        {
            pendingBagRefreshAfterCraftingVisibilityChange = true;
            QueueBagRefresh();
            return;
        }

        QueueBagRefresh();
    }

    private BagSlot GetVisibleExpandedBagSlot()
    {
        if (expandedBagSlot == null || !expandedBagSlot.IsCraftingExpanded)
        {
            return null;
        }

        if (bagSlots == null)
        {
            return null;
        }

        for (int i = 0; i < bagSlots.Count; i++)
        {
            if (bagSlots[i] == expandedBagSlot)
            {
                return expandedBagSlot;
            }
        }

        return null;
    }

    private void QueueBagRefresh()
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            return;
        }

        isBagRefreshQueued = true;
        queuedBagRefreshFrame = Time.frameCount + 1;
    }

    private void ProcessQueuedBagRefresh()
    {
        if (!isBagRefreshQueued)
        {
            return;
        }

        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            isBagRefreshQueued = false;
            queuedBagRefreshFrame = -1;
            return;
        }

        if (Time.frameCount < queuedBagRefreshFrame)
        {
            return;
        }

        isBagRefreshQueued = false;
        queuedBagRefreshFrame = -1;

        RefreshBag(boundInventoryBag);
        if (expandedBagSlot == null)
        {
            RestoreAllBagSlotVisibility(boundInventoryBag != null ? boundInventoryBag.SlotCount : 0);
        }
    }

    private void RestoreAllBagSlotVisibility(int visibleSlotCount)
    {
        if (bagSlots == null)
        {
            return;
        }

        for (int i = 0; i < bagSlots.Count; i++)
        {
            BagSlot slot = bagSlots[i];
            if (slot == null || i >= visibleSlotCount)
            {
                continue;
            }

            if (!slot.gameObject.activeSelf)
            {
                slot.gameObject.SetActive(true);
            }

            slot.SetSlotVisible(true);
        }
    }

    private void CollapseExpandedBagSlot(bool immediate)
    {
        if (expandedBagSlot == null)
        {
            return;
        }

        BagSlot target = expandedBagSlot;
        expandedBagSlot = null;
        target.CloseCraftingSlots(immediate);
        RefreshBag(boundInventoryBag);
    }

    private bool IsPointerOverExpandedBagArea(BagSlot slot, Vector2 pointerPosition)
    {
        if (slot == null || EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = pointerPosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        for (int i = 0; i < results.Count; i++)
        {
            if (slot.ContainsUiObject(results[i].gameObject))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPointerOverItemFilterUiArea(Vector2 pointerPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = pointerPosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        for (int i = 0; i < results.Count; i++)
        {
            GameObject hitObject = results[i].gameObject;
            if (hitObject == null)
            {
                continue;
            }

            if (itemFilterUI != null && hitObject.transform.IsChildOf(itemFilterUI.transform))
            {
                return true;
            }

            if (ItemFilterButton != null && hitObject.transform.IsChildOf(ItemFilterButton.transform))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPointerOverObjectInfoBlockingUi(Vector2 pointerPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = pointerPosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        for (int i = 0; i < results.Count; i++)
        {
            GameObject hitObject = results[i].gameObject;
            if (hitObject == null)
            {
                continue;
            }

            if (hitObject.GetComponentInParent<Selectable>() != null)
            {
                return true;
            }

            if (objectInfoPanel != null && hitObject.transform.IsChildOf(objectInfoPanel.transform))
            {
                return true;
            }

            if (itemFilterUI != null && hitObject.transform.IsChildOf(itemFilterUI.transform))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateCraftingIngredientRefresh(float deltaTime)
    {
        if (IsInventoryEditLocked() || expandedBagSlot == null)
        {
            craftingIngredientRefreshTimer = 0f;
            return;
        }

        craftingIngredientRefreshTimer -= Mathf.Max(0f, deltaTime);
        if (craftingIngredientRefreshTimer > 0f)
        {
            return;
        }

        craftingIngredientRefreshTimer = Mathf.Max(0.05f, craftingIngredientRefreshInterval);
        RefreshVisibleCraftingUi();
    }

    public bool TryEnqueueCrafting(int itemId, List<CraftingTreeRuntime.IngredientEntry> refundIngredients = null)
    {
        if (!CanEnqueueCrafting(itemId))
        {
            return false;
        }

        int outputCount = CraftingTreeRuntime.GetOutputCount(itemId);
        float craftingDuration = GetCraftingDurationSeconds(itemId);
        craftingQueue.Add(new CraftingQueueEntry(itemId, outputCount, craftingDuration, refundIngredients));
        craftingQueueDirty = true;
        RefreshCraftingQueueSlots(true);
        return true;
    }

    public bool CanEnqueueCrafting(int itemId)
    {
        if (itemId < 0 || IsInventoryEditLocked())
        {
            return false;
        }

        if (craftingWaitingQueue == null || craftingWaitingQueue.Count == 0)
        {
            return false;
        }

        return craftingQueue.Count < craftingWaitingQueue.Count;
    }

    public void CaptureCraftingQueueSaveState(List<PlayerCraftingQueueEntrySaveData> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        for (int i = 0; i < craftingQueue.Count; i++)
        {
            CraftingQueueEntry entry = craftingQueue[i];
            if (entry == null || entry.itemId < 0 || entry.remainingOutputCount <= 0)
            {
                continue;
            }

            PlayerCraftingQueueEntrySaveData saveData = new PlayerCraftingQueueEntrySaveData
            {
                itemId = entry.itemId,
                outputCount = Mathf.Max(1, entry.outputCount),
                remainingOutputCount = Mathf.Max(0, entry.remainingOutputCount),
                remainingTime = Mathf.Max(0f, entry.remainingTime),
                duration = Mathf.Max(0.01f, entry.duration)
            };

            CopyCraftingRefundIngredients(entry.refundIngredients, saveData.refundIngredients);
            results.Add(saveData);
        }
    }

    public void ApplyCraftingQueueSaveState(IReadOnlyList<PlayerCraftingQueueEntrySaveData> savedEntries)
    {
        craftingQueue.Clear();
        int maxQueueCount = craftingWaitingQueue != null && craftingWaitingQueue.Count > 0
            ? craftingWaitingQueue.Count
            : int.MaxValue;

        if (savedEntries != null)
        {
            for (int i = 0; i < savedEntries.Count && craftingQueue.Count < maxQueueCount; i++)
            {
                PlayerCraftingQueueEntrySaveData savedEntry = savedEntries[i];
                if (savedEntry == null || savedEntry.itemId < 0)
                {
                    continue;
                }

                int outputCount = Mathf.Max(1, savedEntry.outputCount);
                int remainingOutputCount = savedEntry.remainingOutputCount > 0
                    ? Mathf.Min(savedEntry.remainingOutputCount, outputCount)
                    : outputCount;
                if (remainingOutputCount <= 0)
                {
                    continue;
                }

                float duration = savedEntry.duration > 0.01f
                    ? savedEntry.duration
                    : GetCraftingDurationSeconds(savedEntry.itemId);
                List<CraftingTreeRuntime.IngredientEntry> refundIngredients =
                    BuildCraftingRefundIngredients(savedEntry.refundIngredients);
                CraftingQueueEntry entry = new CraftingQueueEntry(
                    savedEntry.itemId,
                    outputCount,
                    duration,
                    refundIngredients)
                {
                    remainingOutputCount = remainingOutputCount,
                    remainingTime = Mathf.Clamp(savedEntry.remainingTime, 0f, duration)
                };

                craftingQueue.Add(entry);
            }
        }

        craftingQueueDirty = true;
        RefreshCraftingQueueSlots(true);
    }

    private static void CopyCraftingRefundIngredients(
        IReadOnlyList<CraftingTreeRuntime.IngredientEntry> source,
        List<PlayerCraftingIngredientSaveData> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        if (source == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = source[i];
            if (ingredient.itemId < 0 || ingredient.count <= 0)
            {
                continue;
            }

            results.Add(new PlayerCraftingIngredientSaveData
            {
                itemId = ingredient.itemId,
                count = ingredient.count
            });
        }
    }

    private static List<CraftingTreeRuntime.IngredientEntry> BuildCraftingRefundIngredients(
        IReadOnlyList<PlayerCraftingIngredientSaveData> source)
    {
        List<CraftingTreeRuntime.IngredientEntry> results = new List<CraftingTreeRuntime.IngredientEntry>();
        if (source == null)
        {
            return results;
        }

        for (int i = 0; i < source.Count; i++)
        {
            PlayerCraftingIngredientSaveData ingredient = source[i];
            if (ingredient == null || ingredient.itemId < 0 || ingredient.count <= 0)
            {
                continue;
            }

            results.Add(new CraftingTreeRuntime.IngredientEntry(
                ingredient.itemId,
                Mathf.Max(1, ingredient.count)));
        }

        return results;
    }

    private static float GetCraftingDurationSeconds(int itemId)
    {
        ItemDefinition definition = GetItemDefinition(itemId);
        return definition != null ? definition.CraftingDurationSeconds : DefaultCraftingDurationSeconds;
    }

    private static ItemDefinition GetItemDefinition(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private void UpdateCraftingQueue(float deltaTime)
    {
        if (craftingWaitingQueue == null || craftingWaitingQueue.Count == 0)
        {
            return;
        }

        if (craftingQueue.Count > 0)
        {
            CraftingQueueEntry entry = craftingQueue[0];
            if (entry.remainingTime > 0f && !IsCraftOutputBlocked(entry.itemId) && !IsCraftingStationBlocked(entry.itemId))
            {
                entry.remainingTime = Mathf.Max(0f, entry.remainingTime - Mathf.Max(0f, deltaTime));
            }

            if (entry.remainingTime <= 0f)
            {
                bool deliveredAny = TryDeliverCraftedItems(entry);
                if (entry.remainingOutputCount <= 0)
                {
                    craftingQueue.RemoveAt(0);
                    craftingQueueDirty = true;
                }
                else if (deliveredAny)
                {
                    craftingQueueDirty = true;
                }
            }
        }

        RefreshCraftingQueueSlots(craftingQueueDirty);
        craftingQueueDirty = false;
    }

    private bool IsCraftingStationBlocked(int itemId)
    {
        if (itemId < 0)
        {
            return true;
        }

        return !CanCraftItem(itemId);
    }

    private bool IsCraftOutputBlocked(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return true;
        }

        Player player = GameManager.Instance.Player;
        if (player.CanAcceptHandObject(itemId))
        {
            return false;
        }

        return !player.CanClearHandIntoBag();
    }

    private bool TryDeliverCraftedItems(CraftingQueueEntry entry)
    {
        if (entry == null || entry.itemId < 0 || entry.remainingOutputCount <= 0)
        {
            return false;
        }

        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
        }

        Player player = GameManager.Instance.Player;
        Vector3 startPosition = player.BodyTransform != null ? player.BodyTransform.position : player.transform.position;
        bool deliveredAny = false;
        int deliveredIndex = 0;
        while (entry.remainingOutputCount > 0)
        {
            if (!player.CanAcceptHandObject(entry.itemId) && !player.TryStoreHandItemsInBag())
            {
                break;
            }

            if (!player.TryReserveHandObject(entry.itemId, out PortableObject handTarget))
            {
                break;
            }

            AnimateCraftedPortableMove(
                entry.itemId,
                handTarget,
                startPosition,
                deliveredIndex * Mathf.Max(0f, craftedPortableMoveInterval),
                () => player.CommitReservedHandObject(handTarget),
                () => player.ReleaseReservedHandObject(handTarget));
            entry.remainingOutputCount--;
            deliveredAny = true;
            deliveredIndex++;
        }

        return deliveredAny;
    }

    private void AnimateCraftedPortableMove(int itemId, PortableObject targetPortableObject, Vector3 startPosition, float delay, System.Action commitAction, System.Action releaseAction)
    {
        if (targetPortableObject == null)
        {
            return;
        }

        PortableObject movingPortableObject = Instantiate(targetPortableObject, startPosition, targetPortableObject.transform.rotation);
        if (movingPortableObject == null)
        {
            releaseAction?.Invoke();
            return;
        }

        movingPortableObject.name = $"{targetPortableObject.name}_CraftMove";
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.localScale = targetPortableObject.transform.lossyScale;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            releaseAction?.Invoke();
            Destroy(movingPortableObject.gameObject);
            return;
        }

        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        Func<Vector3> startPositionProvider = null;
        if (player != null)
        {
            Transform startTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
            if (startTransform != null)
            {
                startPositionProvider = () => startTransform != null ? startTransform.position : startPosition;
            }
        }

        movingPortableObject.MoveTo(targetPortableObject.transform, delay, startPositionProvider, () =>
        {
            commitAction?.Invoke();

            if (movingPortableObject != null)
            {
                Destroy(movingPortableObject.gameObject);
            }
        }, false);
    }

    private void RefreshCraftingQueueSlots(bool forceIconRefresh)
    {
        if (craftingWaitingQueue == null)
        {
            return;
        }

        float currentFill = 0f;
        if (craftingQueue.Count > 0)
        {
            CraftingQueueEntry entry = craftingQueue[0];
            currentFill = entry.duration > 0f ? Mathf.Clamp01(entry.remainingTime / entry.duration) : 0f;
        }

        for (int i = 0; i < craftingWaitingQueue.Count; i++)
        {
            CreatingQueueSlot slot = craftingWaitingQueue[i];
            if (slot == null)
            {
                continue;
            }

            if (i < craftingQueue.Count)
            {
                CraftingQueueEntry entry = craftingQueue[i];
                int capturedIndex = i;
                if (!slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(true);
                }
                if (forceIconRefresh || slot.ItemId != entry.itemId)
                {
                    slot.SetItem(entry.itemId, entry.remainingOutputCount, 0);
                }
                else if (entry.remainingOutputCount > 0)
                {
                    slot.SetItemDisplay(entry.itemId, entry.remainingOutputCount, 0, false);
                }

                float fillValue = i == 0 ? currentFill : 1f;
                slot.SetFill(fillValue);
                bool canCancel = entry.remainingTime > 0f;
                slot.BindCancelAction(canCancel ? () => CancelCraftingQueueAt(capturedIndex) : null);
                slot.SetCancelInteractable(canCancel);
            }
            else
            {
                if (forceIconRefresh || slot.HasItem)
                {
                    slot.Clear();
                }

                slot.SetFill(0f);
                slot.BindCancelAction(null);
                if (slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(false);
                }
            }
        }
    }

    private bool CancelCraftingQueueAt(int index)
    {
        if (index < 0 || index >= craftingQueue.Count)
        {
            return false;
        }

        CraftingQueueEntry entry = craftingQueue[index];
        if (entry == null || entry.remainingTime <= 0f)
        {
            return false;
        }

        RefundCraftingIngredients(entry);
        craftingQueue.RemoveAt(index);
        craftingQueueDirty = true;
        RefreshCraftingQueueSlots(true);
        return true;
    }

    private void RefundCraftingIngredients(CraftingQueueEntry entry)
    {
        if (entry == null || entry.refundIngredients == null || entry.refundIngredients.Count == 0)
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return;
        }

        Player player = GameManager.Instance.Player;
        TerrainGenerator terrain = ResolveTerrainGenerator();
        Vector3 refundOrigin = player.transform.position;

        for (int i = 0; i < entry.refundIngredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = entry.refundIngredients[i];
            int remaining = Mathf.Max(0, ingredient.count);
            while (remaining > 0)
            {
                if (player.TryAddToBag(ingredient.itemId, out _))
                {
                    remaining--;
                    continue;
                }

                if (player.TryAddToHand(ingredient.itemId, out _))
                {
                    remaining--;
                    continue;
                }

                if (terrain != null
                    && (terrain.TryAddDroppedItemAnimated(refundOrigin, ingredient.itemId, refundOrigin, out _)
                        || terrain.TryAddDroppedItemAtPlayerBlock(refundOrigin, ingredient.itemId, out _)
                        || terrain.TryAddDroppedItemNear(refundOrigin, ingredient.itemId, out _)))
                {
                    remaining--;
                    continue;
                }

                return;
            }
        }
    }

    private void RefreshVisibleCraftingUi()
    {
        if (expandedBagSlot == null)
        {
            return;
        }

        expandedBagSlot.RefreshCraftingAvailability(false);

        if (expandedBagSlot == null || !expandedBagSlot.IsCraftingExpanded)
        {
            return;
        }

        expandedBagSlot.RefreshExpandedCraftingSlotStatus();
    }

    private bool TryGetPrimaryPointerDown(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                pointerPosition = touch.position;
                return true;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            pointerPosition = Input.mousePosition;
            return true;
        }

        return false;
    }

    private bool TryGetSecondaryPointerDown(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;
        if (Input.GetMouseButtonDown(1))
        {
            pointerPosition = Input.mousePosition;
            return true;
        }

        return false;
    }

    private void UpdateInventoryEditLockState()
    {
        bool isLocked = IsInventoryEditLocked();
        if (wasInventoryEditLocked == isLocked)
        {
            return;
        }

        wasInventoryEditLocked = isLocked;

        if (isLocked)
        {
            if (expandedBagSlot != null)
            {
                CollapseExpandedBagSlot(true);
            }
            else
            {
                BagSlot.CloseAnyExpanded(true);
            }
        }

        UpdateInstallModeButtons();
        RefreshBag(boundInventoryBag);
    }

    public Button InstallButton => installButton;
    public Button InstallCancelButton => installCancelButton;
    public Button InstallRotationButton => installRotationButton;
    public Button InstallCompleteButton => installCompleteButton;
    public Button MapEditButton => mapEditButton;
    public Button MapEditCancelButton => mapEditCancelButton;
    public Button MapEditRotationButton => mapEditRotationButton;
    public Button MapEditCompleteButton => mapEditCompleteButton;
}
