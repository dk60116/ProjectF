using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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

    private const float CraftingDurationSeconds = 5f;
    private readonly List<CraftingQueueEntry> craftingQueue = new List<CraftingQueueEntry>();
    private bool craftingQueueDirty;

    private class CraftingQueueEntry
    {
        public int itemId;
        public float remainingTime;
        public float duration;

        public CraftingQueueEntry(int itemId, float duration)
        {
            this.itemId = itemId;
            this.duration = Mathf.Max(0.01f, duration);
            remainingTime = this.duration;
        }
    }

    private void Awake()
    {
        SubscribeSlotEvents();
        RefreshBag(null);
        RefreshCraftingQueueSlots(true);
    }

    private void Start()
    {
        EnsureInitialBagBinding();
    }

    private void OnEnable()
    {
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
        UpdateCraftingQueue(Time.deltaTime);

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        BagSlot expandedSlot = BagSlot.ExpandedSlot;
        if (expandedSlot == null)
        {
            return;
        }

        if (IsPointerOverExpandedBagArea(expandedSlot))
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
    }

    private void HandleHandBagChanged()
    {
        RefreshHandSlot(boundHandBag);
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

    private bool IsPointerOverExpandedBagArea(BagSlot slot)
    {
        if (slot == null || EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
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

    public bool TryEnqueueCrafting(int itemId)
    {
        if (itemId < 0)
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

        craftingQueue.Add(new CraftingQueueEntry(itemId, CraftingDurationSeconds));
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
            if (!IsHandBlocked(entry.itemId))
            {
                entry.remainingTime = Mathf.Max(0f, entry.remainingTime - Mathf.Max(0f, deltaTime));

                if (entry.remainingTime <= 0f && TryDeliverCraftedItem(entry.itemId))
                {
                    craftingQueue.RemoveAt(0);
                    craftingQueueDirty = true;
                }
            }
        }

        RefreshCraftingQueueSlots(craftingQueueDirty);
        craftingQueueDirty = false;
    }

    private bool IsHandBlocked(int itemId)
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return true;
        }

        PlayerBag handBag = GameManager.Instance.Player.GetHandBag();
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
        return handItemId >= 0 && handItemId != itemId;
    }

    private bool TryDeliverCraftedItem(int itemId)
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
        }

        if (GameManager.Instance.Player.GetHandItemCount() > 0)
        {
            return false;
        }

        return GameManager.Instance.Player.TryAddToHand(itemId, out _);
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
                    slot.SetItem(entry.itemId, 1, 0);
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
}
