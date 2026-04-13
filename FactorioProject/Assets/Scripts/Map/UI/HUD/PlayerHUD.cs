using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerHUD : MonoBehaviour
{
    [SerializeField]
    private List<BagSlot> slots;

    private PlayerBag boundBag;
    private BagSlot expandedBagSlot;
    private bool isRefreshing;

    private void Awake()
    {
        SubscribeSlotEvents();
        RefreshBag(null);
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
        if (expandedBagSlot == null || !Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (IsPointerOverExpandedBagArea())
        {
            return;
        }

        CollapseExpandedBagSlot(false);
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
    }

    public void RefreshBag(PlayerBag bag)
    {
        if (slots == null)
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
        for (int i = 0; i < slots.Count; i++)
        {
            BagSlot slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            bool shouldShowSlot = i < visibleSlotCount
                                  && (expandedBagSlot == null || slot == expandedBagSlot);
            slot.SetSlotVisible(shouldShowSlot);

            if (!shouldShowSlot)
            {
                slot.Bind(null, -1, -1, 0, 0);
                continue;
            }

            slot.Bind(bag, i, bag.GetSlotItemId(i), bag.GetSlotCount(i), bag.GetSlotMaxCount(i));
        }

        isRefreshing = false;
    }

    private void HandleBagChanged()
    {
        RefreshBag(boundBag);
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
    }

    private void UnbindCurrentBag()
    {
        if (boundBag != null)
        {
            boundBag.Changed -= HandleBagChanged;
            boundBag = null;
        }

        RefreshBag(null);
    }

    private void SubscribeSlotEvents()
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            BagSlot slot = slots[i];
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

    private bool IsPointerOverExpandedBagArea()
    {
        if (expandedBagSlot == null || EventSystem.current == null)
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
            if (expandedBagSlot.ContainsUiObject(results[i].gameObject))
            {
                return true;
            }
        }

        return false;
    }
}
