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

    private PlayerBag boundBag;
    private PlayerBag boundHandBag;
    private BagSlot expandedBagSlot;
    private bool isRefreshing;

    [SerializeField]
    private List<CreatingQueueSlot> craftingWaitingQueue; 

    [SerializeField, Min(0.05f)]
    private float craftingIngredientRefreshInterval = 0.2f;

    [SerializeField, Min(0f)]
    private float craftedPortableMoveInterval = 0.1f;

    private const float CraftingDurationSeconds = 5f;
    private readonly List<CraftingQueueEntry> craftingQueue = new List<CraftingQueueEntry>();
    private bool craftingQueueDirty;
    private float craftingIngredientRefreshTimer;
    private Component installationPlacementController;
    private static Type installationPlacementControllerType;
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
    private Button ItemFilterButton;

    [SerializeField]
    private FilterSelectUI itemFilterUI;

    [SerializeField]
    private MapPaper mapPaper;

    private TerrainGenerator cachedTerrainGenerator;
    private BoxObject currentInteractionBoxObject;
    private int lastObservedHandItemId = -2;
    private int lastObservedHandItemCount = -1;
    private int lastObservedHandMaxCount = -1;
    private bool mapEditButtonsInitialized;
    private bool lastInstallActionButtonsVisible;
    private bool lastMapEditExtraButtonsVisible;
    private bool pendingBagRefreshAfterCraftingVisibilityChange;
    private bool isBagRefreshQueued;
    private int queuedBagRefreshFrame = -1;
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
        ResolveInstallModeButtons();
        ResolveInteractionButton();
        ResolveItemFilterButton();
        ResolveItemFilterUI();
        ResolveMapPaper();
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
        ResolveInstallModeButtons();
        ResolveInteractionButton();
        ResolveItemFilterButton();
        ResolveItemFilterUI();
        ResolveMapPaper();
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
    }

    private void Update()
    {
        EnsureHandBagBinding();
        PollHandBagChanges();
        ResolveInstallModeButtons();
        ResolveInteractionButton();
        ResolveItemFilterButton();
        ResolveItemFilterUI();
        ResolveMapPaper();
        UpdateInstallModeButtons();
        ProcessQueuedBagRefresh();
        UpdateInteractionButtonState();
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
            bool isPointerOverFilterUi = IsPointerOverItemFilterUiArea(pointerPosition);
            if (!isPointerOverFilterUi)
            {
                itemFilterUI.gameObject.SetActive(false);
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
        if (boundBag == bag)
        {
            RefreshBag(bag);
            return;
        }

        UnbindCurrentBag();
        boundBag = bag;

        if (boundBag != null)
        {
            boundBag.Changed += HandleBagChanged;
        }

        RefreshBag(boundBag);
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
            slot.Bind(bag, i, bag.GetSlotItemId(i), bag.GetSlotCount(i), bag.GetSlotMaxCount(i));
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
        RefreshBag(boundBag);
        RefreshVisibleCraftingUi();
    }

    private void HandleHandBagChanged()
    {
        RefreshHandSlot(boundHandBag);
        RefreshVisibleCraftingUi();
    }

    private void EnsureInitialBagBinding()
    {
        if (boundBag != null)
        {
            RefreshBag(boundBag);
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
        if (boundBag != null)
        {
            boundBag.Changed -= HandleBagChanged;
            boundBag = null;
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
        int handItemId = handBag.GetSlotItemId(0);
        int handItemCount = handBag.GetSlotCount(0);
        int handMaxCount = handBag.GetSlotMaxCount(0);
        handSlot.Bind(handBag, 0, handItemId, handItemCount, handMaxCount);
        UpdateObservedHandState(handItemId, handItemCount, handMaxCount);
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
        int handItemId = boundHandBag.GetSlotItemId(0);
        int handItemCount = boundHandBag.GetSlotCount(0);
        int handMaxCount = boundHandBag.GetSlotMaxCount(0);

        if (handItemId == lastObservedHandItemId
            && handItemCount == lastObservedHandItemCount
            && handMaxCount == lastObservedHandMaxCount)
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
    }

    private void UpdateObservedHandState(int itemId, int itemCount, int maxItemCount)
    {
        lastObservedHandItemId = itemId;
        lastObservedHandItemCount = itemCount;
        lastObservedHandMaxCount = maxItemCount;
    }

    private void EnsureInstallationPlacementController()
    {
        Type controllerType = ResolveInstallationPlacementControllerType();
        if (controllerType == null)
        {
            return;
        }

        if (installationPlacementController == null)
        {
            installationPlacementController = GetComponent(controllerType);
            if (installationPlacementController == null)
            {
                installationPlacementController = gameObject.AddComponent(controllerType);
            }
        }

        controllerType.GetMethod("SetInstallButtons")?.Invoke(
            installationPlacementController,
            new object[] { InstallButton, InstallCancelButton, InstallRotationButton, InstallCompleteButton });
        controllerType.GetMethod("SetMapEditButtons")?.Invoke(
            installationPlacementController,
            new object[] { MapEditButton, MapEditCancelButton, MapEditRotationButton, MapEditCompleteButton, mapEditPackButton, mapEditUndoButton });
    }

    private void ResolveInstallModeButtons()
    {
        installButton = ResolveButtonReference(installButton, "InstallButton");
        Transform installRoot = installButton != null ? installButton.transform.parent : null;
        installCancelButton = ResolveButtonReferenceInRoot(installCancelButton, installRoot, "InstallCancelButton", "CancelButton");
        installRotationButton = ResolveButtonReferenceInRoot(installRotationButton, installRoot, "InstallRotationButton", "RotationButton");
        installCompleteButton = ResolveButtonReferenceInRoot(installCompleteButton, installRoot, "InstallCompleteButton", "CompleteButton");

        Transform mapEditRoot = FindDescendantByName(transform, "MapEdit");
        mapEditButton = ResolveButtonReferenceInRoot(mapEditButton, mapEditRoot, "MapEditButton");
        mapEditCancelButton = ResolveButtonReferenceInRoot(mapEditCancelButton, mapEditRoot, "CancelButton");
        mapEditRotationButton = ResolveButtonReferenceInRoot(mapEditRotationButton, mapEditRoot, "RotationButton");
        mapEditCompleteButton = ResolveButtonReferenceInRoot(mapEditCompleteButton, mapEditRoot, "CompleteButton");
        mapEditPackButton = ResolveButtonReferenceInRoot(mapEditPackButton, mapEditRoot, "PackButton", "Pack");
        mapEditUndoButton = ResolveButtonReferenceInRoot(mapEditUndoButton, mapEditRoot, "UnDoButton", "UndoButton");
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
        WarmAnimatedButtonLayoutPositions(
            installCancelButton,
            installRotationButton,
            installCompleteButton,
            mapEditCancelButton,
            mapEditRotationButton,
            mapEditCompleteButton,
            mapEditPackButton,
            mapEditUndoButton);
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

    private void ResolveInteractionButton()
    {
        if (InteractionButton != null)
        {
            BindInteractionButton();
            return;
        }

        InteractionButton = GetComponentInChildren<InteractionButton>(true);
        if (InteractionButton == null)
        {
            Transform buttonTransform = FindDescendantByName(transform, "InteractionButton");
            if (buttonTransform != null)
            {
                InteractionButton = buttonTransform.GetComponent<InteractionButton>();
            }
        }

        BindInteractionButton();
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

        if (candidateNames == null || candidateNames.Length == 0)
        {
            return null;
        }

        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null)
            {
                continue;
            }

            for (int nameIndex = 0; nameIndex < candidateNames.Length; nameIndex++)
            {
                if (candidate.name == candidateNames[nameIndex])
                {
                    return candidate;
                }
            }
        }

        return null;
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
            cachedTerrainGenerator = UnityEngine.Object.FindObjectOfType<TerrainGenerator>();
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
    }

    private bool IsMapEditModeActive()
    {
        Type controllerType = ResolveInstallationPlacementControllerType();
        if (controllerType == null || installationPlacementController == null)
        {
            return false;
        }

        object value = controllerType.GetProperty("MapEditModeActive")?.GetValue(installationPlacementController);
        return value is bool isActive && isActive;
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
                sequence.InsertCallback(delay, () =>
                {
                    if (animatedButton == null)
                    {
                        return;
                    }

                    RectTransform animatedRectTransform = animatedButton.transform as RectTransform;
                    if (animatedRectTransform == null)
                    {
                        animatedButton.gameObject.SetActive(true);
                        SetButtonRaycastTargetsEnabled(animatedButton, true);
                        return;
                    }

                    animatedButton.gameObject.SetActive(true);
                    animatedRectTransform.localPosition = sourceLocalPosition;
                    Vector3 targetLocalPosition = targetLocalPositions != null
                        && targetLocalPositions.TryGetValue(animatedButton, out Vector3 resolvedTargetLocalPosition)
                        ? resolvedTargetLocalPosition
                        : sourceLocalPosition;
                    animatedRectTransform.DOLocalMove(targetLocalPosition, mapEditButtonExpandDuration)
                        .SetEase(Ease.OutCubic)
                        .OnComplete(() =>
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
                });
            }

            sequence.OnComplete(() =>
            {
                mapEditButtonsAnimating = false;
                mapEditButtonAnimationSequence = null;
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
            hideSequence.InsertCallback(delay, () =>
            {
                if (animatedButton == null)
                {
                    return;
                }

                RectTransform animatedRectTransform = animatedButton.transform as RectTransform;
                if (animatedRectTransform == null)
                {
                    animatedButton.gameObject.SetActive(false);
                    return;
                }

                animatedRectTransform.DOLocalMove(sourceLocalPosition, mapEditButtonExpandDuration * 0.85f)
                    .SetEase(Ease.InCubic)
                    .OnComplete(() =>
                    {
                        if (animatedButton == null)
                        {
                            return;
                        }

                        animatedButton.gameObject.SetActive(false);
                    });
            });
        }

        hideSequence.OnComplete(() =>
        {
            if (mapEditButton != null)
            {
                mapEditButton.gameObject.SetActive(true);
            }

            SetMapEditLayoutEnabled(true);
            RestoreAnimatedButtonGroupLayout(orderedButtons);
            mapEditButtonsAnimating = false;
            mapEditButtonAnimationSequence = null;
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

        if (layoutElement != null)
        {
            layoutElement.ignoreLayout = false;
        }

        NormalizeButtonCanvasGroup(button);
        SetButtonRaycastTargetsEnabled(button, isVisible);
        button.interactable = true;
        button.gameObject.SetActive(isVisible);
    }

    private void AnimateButtonGroupSlide(Button sourceButton, bool shouldBeVisible, params Button[] buttons)
    {
        AnimateButtonGroupSlide(GetButtonAnchorPosition(sourceButton), shouldBeVisible, buttons);
    }

    private void AnimateButtonGroupSlide(Vector2 sourcePosition, bool shouldBeVisible, params Button[] buttons)
    {
        if (buttons == null || buttons.Length == 0)
        {
            return;
        }

        List<Button> orderedButtons = GetButtonsInSiblingOrder(buttons);
        if (orderedButtons.Count == 0)
        {
            return;
        }
        if (shouldBeVisible)
        {
            Dictionary<Button, Vector2> resolvedTargetPositions = ResolveAnimatedButtonTargetPositions(orderedButtons);
            List<Button> animatedButtons = new List<Button>();

            for (int i = 0; i < orderedButtons.Count; i++)
            {
                Button button = orderedButtons[i];
                LayoutElement layoutElement = EnsureButtonLayoutElement(button);
                RectTransform rectTransform = button.transform as RectTransform;
                if (rectTransform == null)
                {
                    button.gameObject.SetActive(true);
                    continue;
                }

                DOTween.Kill(rectTransform);
                rectTransform.localScale = Vector3.one;
                NormalizeButtonCanvasGroup(button);
                SetButtonRaycastTargetsEnabled(button, false);
                button.interactable = true;
                if (layoutElement != null)
                {
                    layoutElement.ignoreLayout = true;
                }

                rectTransform.anchoredPosition = sourcePosition;
                button.gameObject.SetActive(false);
                animatedButtons.Add(button);
            }

            if (animatedButtons.Count == 0)
            {
                return;
            }

            int completedCount = 0;
            for (int i = 0; i < animatedButtons.Count; i++)
            {
                Button button = animatedButtons[i];
                RectTransform rectTransform = button.transform as RectTransform;
                if (rectTransform == null)
                {
                    continue;
                }

                float delay = i * mapEditButtonExpandStagger;
                Vector2 targetPosition = resolvedTargetPositions.TryGetValue(button, out Vector2 resolvedTargetPosition)
                    ? resolvedTargetPosition
                    : GetCachedAnimatedButtonPosition(button);
                Button animatedButton = button;
                DOVirtual.DelayedCall(delay, () =>
                    {
                        if (animatedButton == null)
                        {
                            return;
                        }

                        RectTransform animatedRectTransform = animatedButton.transform as RectTransform;
                        if (animatedRectTransform == null)
                        {
                            completedCount++;
                            if (completedCount >= animatedButtons.Count)
                            {
                                RestoreAnimatedButtonGroupLayout(animatedButtons);
                            }
                            return;
                        }

                        animatedButton.gameObject.SetActive(true);
                        animatedRectTransform.anchoredPosition = sourcePosition;
                        animatedRectTransform.DOAnchorPos(targetPosition, mapEditButtonExpandDuration)
                            .SetEase(Ease.OutCubic)
                            .OnComplete(() =>
                            {
                                if (animatedButton == null)
                                {
                                    return;
                                }

                                RectTransform completedRectTransform = animatedButton.transform as RectTransform;
                                if (completedRectTransform != null)
                                {
                                    completedRectTransform.anchoredPosition = targetPosition;
                                }

                                NormalizeButtonCanvasGroup(animatedButton);
                                SetButtonRaycastTargetsEnabled(animatedButton, true);
                                animatedButton.interactable = true;

                                completedCount++;
                                if (completedCount >= animatedButtons.Count)
                                {
                                    RestoreAnimatedButtonGroupLayout(animatedButtons);
                                }
                            });
                    })
                    .SetUpdate(true);
            }

            return;
        }

        List<Button> activeButtons = new List<Button>();
        for (int i = 0; i < orderedButtons.Count; i++)
        {
            Button button = orderedButtons[i];
            if (button.gameObject.activeSelf)
            {
                activeButtons.Add(button);
            }
        }

        for (int i = 0; i < activeButtons.Count; i++)
        {
            Button button = activeButtons[i];
            LayoutElement layoutElement = EnsureButtonLayoutElement(button);
            RectTransform rectTransform = button.transform as RectTransform;
            if (rectTransform == null)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            DOTween.Kill(rectTransform);
            rectTransform.localScale = Vector3.one;
            NormalizeButtonCanvasGroup(button);
            SetButtonRaycastTargetsEnabled(button, false);
            button.interactable = true;
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = true;
            }
        }

        if (activeButtons.Count == 0)
        {
            return;
        }

        for (int i = activeButtons.Count - 1; i >= 0; i--)
        {
            Button button = activeButtons[i];
            RectTransform rectTransform = button.transform as RectTransform;
            if (rectTransform == null)
            {
                continue;
            }

            float delay = (activeButtons.Count - 1 - i) * mapEditButtonExpandStagger;
            Button animatedButton = button;
            rectTransform.DOAnchorPos(sourcePosition, mapEditButtonExpandDuration * 0.85f)
                .SetDelay(delay)
                .SetEase(Ease.InCubic)
                .OnComplete(() =>
                {
                    if (animatedButton == null)
                    {
                        return;
                    }

                    RectTransform completedRectTransform = animatedButton.transform as RectTransform;
                    if (completedRectTransform != null && !IsLayoutManagedButton(animatedButton))
                    {
                        completedRectTransform.anchoredPosition = GetCachedAnimatedButtonPosition(animatedButton);
                    }

                    animatedButton.gameObject.SetActive(false);
                });
        }

        DOVirtual.DelayedCall(
            (activeButtons.Count - 1) * mapEditButtonExpandStagger + (mapEditButtonExpandDuration * 0.85f),
            () => RestoreAnimatedButtonGroupLayout(activeButtons))
            .SetUpdate(true);
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

    private Dictionary<Button, Vector2> ResolveAnimatedButtonTargetPositions(IEnumerable<Button> buttons)
    {
        Dictionary<Button, Vector2> resolvedPositions = new Dictionary<Button, Vector2>();
        if (buttons == null)
        {
            return resolvedPositions;
        }

        foreach (Button button in buttons)
        {
            if (button == null)
            {
                continue;
            }

            resolvedPositions[button] = GetCachedAnimatedButtonPosition(button);
        }

        return resolvedPositions;
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

    private static Vector2 GetButtonAnchorPosition(Button button)
    {
        RectTransform rectTransform = button != null ? button.transform as RectTransform : null;
        return rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero;
    }

    private static Vector3 GetButtonLocalPosition(Button button)
    {
        RectTransform rectTransform = button != null ? button.transform as RectTransform : null;
        return rectTransform != null ? rectTransform.localPosition : Vector3.zero;
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
        if (InteractionButton == null)
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

        if (!TryGetFocusedBoxObject(out BoxObject focusedBoxObject))
        {
            ClearInteractionButtonState();
            return;
        }

        currentInteractionBoxObject = focusedBoxObject;
        InteractionButton.SetIcon(ResolveInteractionIcon(focusedBoxObject));
        InteractionButton.SetVisible(true);
    }

    private void ClearInteractionButtonState()
    {
        currentInteractionBoxObject = null;
        if (InteractionButton == null)
        {
            return;
        }

        InteractionButton.SetIcon(null);
        InteractionButton.SetVisible(false);
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
            PlayerController playerController = GameManager.Instance.Player.GetComponent<PlayerController>();
            if (playerController != null && playerController.TryGetFocusedItemFilterMapObject(out _))
            {
                isVisible = true;
            }
        }

        if (ItemFilterButton.gameObject.activeSelf != isVisible)
        {
            ItemFilterButton.gameObject.SetActive(isVisible);
        }

        if (!isVisible && itemFilterUI != null && itemFilterUI.gameObject.activeSelf)
        {
            itemFilterUI.gameObject.SetActive(false);
        }
    }

    private static Sprite ResolveInteractionIcon(BoxObject boxObject)
    {
        if (boxObject == null || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        int itemId = boxObject.ResolveItemId();
        if (itemId < 0)
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
            if (definition == null || definition.id != itemId)
            {
                continue;
            }

            if (definition.interactionButtonList == null || definition.interactionButtonList.Count <= 0)
            {
                return null;
            }

            int preferredIconIndex = boxObject.IsOpen ? 1 : 0;
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

            return null;
        }

        return null;
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

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null)
            {
                continue;
            }

            for (int nameIndex = 0; nameIndex < candidateNames.Length; nameIndex++)
            {
                if (candidate.name == candidateNames[nameIndex])
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private void BindInteractionButton()
    {
        if (InteractionButton == null)
        {
            return;
        }

        InteractionButton.SetClickAction(HandleInteractionButtonClicked);
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
        if (currentInteractionBoxObject == null)
        {
            return;
        }

        currentInteractionBoxObject.ToggleOpenState();
        UpdateInteractionButtonState();
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
            PlayerController playerController = GameManager.Instance != null && GameManager.Instance.Player != null
                ? GameManager.Instance.Player.GetComponent<PlayerController>()
                : null;
            if (playerController == null || !playerController.TryGetFocusedItemFilterMapObject(out MapObject focusedMapObject))
            {
                return;
            }

            itemFilterUI.Bind(focusedMapObject);
        }

        itemFilterUI.gameObject.SetActive(shouldOpen);
    }

    private bool TryGetFocusedBoxObject(out BoxObject focusedBoxObject)
    {
        focusedBoxObject = null;
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
        }

        PlayerController playerController = GameManager.Instance.Player.GetComponent<PlayerController>();
        if (playerController == null)
        {
            return false;
        }

        return playerController.TryGetFocusedBoxObject(out focusedBoxObject);
    }

    private static Type ResolveInstallationPlacementControllerType()
    {
        if (installationPlacementControllerType != null)
        {
            return installationPlacementControllerType;
        }

        System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type candidate = assemblies[i].GetType("InstallationPlacementController");
            if (candidate != null)
            {
                installationPlacementControllerType = candidate;
                return installationPlacementControllerType;
            }
        }

        return null;
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

        RefreshBag(boundBag);
        if (expandedBagSlot == null)
        {
            RestoreAllBagSlotVisibility(boundBag != null ? boundBag.SlotCount : 0);
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
        RefreshBag(boundBag);
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
        if (itemId < 0 || IsInventoryEditLocked())
        {
            return false;
        }

        if (craftingWaitingQueue == null || craftingWaitingQueue.Count == 0)
        {
            return false;
        }

        if (craftingQueue.Count >= craftingWaitingQueue.Count)
        {
            return false;
        }

        int outputCount = CraftingTreeRuntime.GetOutputCount(itemId);
        craftingQueue.Add(new CraftingQueueEntry(itemId, outputCount, CraftingDurationSeconds, refundIngredients));
        craftingQueueDirty = true;
        RefreshCraftingQueueSlots(true);
        return true;
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

    private bool IsHandBlocked(int itemId)
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return true;
        }

        Player player = GameManager.Instance.Player;
        PlayerBag handBag = player.GetHandBag();
        if (handBag == null)
        {
            return true;
        }

        handBag.RefreshExternalStackCounts(false);
        int handCount = handBag.GetSlotCount(0);
        if (handCount <= 0)
        {
            return false;
        }

        int handItemId = handBag.GetSlotItemId(0);
        if (handItemId >= 0 && handItemId != itemId)
        {
            return true;
        }

        int reservedHandItemId = player.GetReservedHandItemId();
        return reservedHandItemId >= 0 && reservedHandItemId != itemId;
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
        TerrainGenerator terrain = FindObjectOfType<TerrainGenerator>();
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

        expandedBagSlot.RefreshCraftingAvailability();

        if (expandedBagSlot == null || !expandedBagSlot.IsCraftingExpanded)
        {
            return;
        }

        Transform craftingRoot = expandedBagSlot.transform.Find("CraftingRoot");
        if (craftingRoot == null)
        {
            craftingRoot = expandedBagSlot.transform.Find("Open");
        }

        Transform parent = craftingRoot != null ? craftingRoot : expandedBagSlot.transform;
        CraftingSlot[] craftingSlots = parent.GetComponentsInChildren<CraftingSlot>(true);
        for (int i = 0; i < craftingSlots.Length; i++)
        {
            CraftingSlot slot = craftingSlots[i];
            if (slot == null)
            {
                continue;
            }

            slot.RefreshIngredientsIfVisible();
        }
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
        RefreshBag(boundBag);
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
