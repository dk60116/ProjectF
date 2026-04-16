using System;
using System.Collections.Generic;
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

    private class CraftingQueueEntry
    {
        public int itemId;
        public int outputCount;
        public int remainingOutputCount;
        public float remainingTime;
        public float duration;

        public CraftingQueueEntry(int itemId, int outputCount, float duration)
        {
            this.itemId = itemId;
            this.outputCount = Mathf.Max(1, outputCount);
            remainingOutputCount = this.outputCount;
            this.duration = Mathf.Max(0.01f, duration);
            remainingTime = this.duration;
        }
    }

    private void Awake()
    {
        SubscribeSlotEvents();
        ResolveInstallModeButtons();
        EnsureInstallationPlacementController();
        UpdateInstallModeButtons();
        RefreshBag(null);
        RefreshCraftingQueueSlots(true);
    }

    private void Start()
    {
        EnsureInitialBagBinding();
    }

    private void OnEnable()
    {
        ResolveInstallModeButtons();
        EnsureInstallationPlacementController();
        UpdateInstallModeButtons();
        EnsureInitialBagBinding();
    }

    private void OnDisable()
    {
        CollapseExpandedBagSlot(true);
        UnbindCurrentBag();
    }

    private void Update()
    {
        EnsureHandBagBinding();
        ResolveInstallModeButtons();
        UpdateInstallModeButtons();
        UpdateInventoryEditLockState();
        UpdateCraftingQueue(Time.deltaTime);
        UpdateCraftingIngredientRefresh(Time.deltaTime);

        if (IsInventoryEditLocked())
        {
            return;
        }

        if (!TryGetPrimaryPointerDown(out Vector2 pointerPosition))
        {
            return;
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

            bool shouldShowSlot = expandedBagSlot == null || slot == expandedBagSlot;
            slot.SetSlotVisible(shouldShowSlot);
            slot.Bind(bag, i, bag.GetSlotItemId(i), bag.GetSlotCount(i), bag.GetSlotMaxCount(i));
        }

        RefreshHandSlot(boundHandBag);
        isRefreshing = false;
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
        handSlot.Bind(handBag, 0, handBag.GetSlotItemId(0), handBag.GetSlotCount(0), handBag.GetSlotMaxCount(0));
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
    }

    private void ResolveInstallModeButtons()
    {
        installButton = ResolveButtonReference(installButton, "InstallButton");
        installCancelButton = ResolveButtonReference(installCancelButton, "InstallCancelButton");
        installRotationButton = ResolveButtonReference(installRotationButton, "RotationButton", "InstallRotationButton");
        installCompleteButton = ResolveButtonReference(installCompleteButton, "CompleteButton", "InstallCompleteButton");
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

    private void UpdateInstallModeButtons()
    {
        bool isInstallModeActive = GameManager.Instance != null && GameManager.Instance.InstallationPlacementActive;

        SetInstallModeButtonVisible(installCancelButton, isInstallModeActive);
        SetInstallModeButtonVisible(installRotationButton, isInstallModeActive);
        SetInstallModeButtonVisible(installCompleteButton, isInstallModeActive);
    }

    private static void SetInstallModeButtonVisible(Button button, bool isVisible)
    {
        if (button == null)
        {
            return;
        }

        if (button.gameObject.activeSelf != isVisible)
        {
            button.gameObject.SetActive(isVisible);
        }

        button.interactable = isVisible;
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
        if (bagSlots == null)
        {
            return;
        }

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

    private void HandleCraftingVisibilityChanged(BagSlot slot, bool isVisible)
    {
        if (isRefreshing)
        {
            return;
        }

        if (isVisible)
        {
            expandedBagSlot = slot;
        }
        else if (expandedBagSlot == slot)
        {
            expandedBagSlot = null;
        }

        RefreshBag(boundBag);
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

    public bool TryEnqueueCrafting(int itemId)
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
        craftingQueue.Add(new CraftingQueueEntry(itemId, outputCount, CraftingDurationSeconds));
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

        PlayerBag bag = GameManager.Instance.Player.GetBag();
        if (bag != null && bag.HasExistingStackSpaceForItem(itemId))
        {
            return false;
        }

        return IsHandBlocked(itemId);
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
        PlayerBag bag = player.GetBag();
        Vector3 startPosition = player.BodyTransform != null ? player.BodyTransform.position : player.transform.position;
        bool deliveredAny = false;
        int deliveredIndex = 0;
        while (entry.remainingOutputCount > 0)
        {
            if (bag != null && bag.TryReserveObjectInExistingStack(entry.itemId, out PortableObject bagTarget))
            {
                AnimateCraftedPortableMove(
                    entry.itemId,
                    bagTarget,
                    startPosition,
                    deliveredIndex * Mathf.Max(0f, craftedPortableMoveInterval),
                    () => bag.CommitReservedObject(bagTarget),
                    () => bag.ReleaseReservedObject(bagTarget));
                entry.remainingOutputCount--;
                deliveredAny = true;
                deliveredIndex++;
                continue;
            }

            if (IsHandBlocked(entry.itemId))
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

        Vector3 targetPosition = targetPortableObject.transform.position;
        movingPortableObject.MoveTo(targetPosition, delay, () =>
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
            }
            else
            {
                if (forceIconRefresh || slot.HasItem)
                {
                    slot.Clear();
                }

                slot.SetFill(0f);
                if (slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(false);
                }
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
}
