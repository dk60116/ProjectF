using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CraftingSlot : ItemSlot
{
    private static CraftingSlot activeIngredientsSlot;
    [SerializeField]
    private float expandDuration = 0.2f;

    [SerializeField]
    private float collapseDuration = 0.12f;

    [SerializeField]
    private Ease expandEase = Ease.OutBack;

    [SerializeField]
    private Ease collapseEase = Ease.InBack;

    [SerializeField]
    private RectTransform ingredientsRoot;

    [SerializeField, Min(0f)]
    private float ingredientRevealDelay = 0.04f;

    [SerializeField, Min(0.01f)]
    private float ingredientRevealDuration = 0.12f;

    [SerializeField]
    private Ease ingredientRevealEase = Ease.OutBack;

    [SerializeField, Range(0.1f, 1f)]
    private float insufficientIngredientAlpha = 0.45f;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Button button;

    [SerializeField]
    private List<ItemSlot> IngrdientsSlots;
    [SerializeField]
    private Button createButton;

    [SerializeField]
    private Image canNotImage;
    [SerializeField]
    private Image createIcon, mapObjectIcon;

    private bool ingredientsVisible;
    private bool slotVisualHidden;
    private bool blockedByCraftingMapObject;
    private bool isCachingReferences;
    private bool isRefreshingCraftingMapObjectVisuals;
    private bool isHidingIngredientsImmediate;
    private int requiredCraftingMapObjectId = -1;
    private readonly List<CraftingTreeRuntime.IngredientEntry> ingredientBuffer = new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<int> requiredCraftingMapObjectIds = new List<int>();

    public float ExpandDuration => Mathf.Max(0f, expandDuration);

    private void Awake()
    {
        CacheReferences();
        HideImmediate();
        HideIngredientsImmediate();
        BindButton();
        BindCreateButton();
    }

    private void OnDisable()
    {
        HideIngredientsImmediate();
    }

    public override void SetItem(int itemId, int itemCount, int maxItemCount = 0)
    {
        base.SetItem(itemId, itemCount, maxItemCount);
        RefreshCraftingMapObjectState();
        if (ingredientsVisible)
        {
            RefreshIngredients(false);
        }
    }

    public void Show(Vector2 startAnchoredPosition, Vector2 targetAnchoredPosition, float delay = 0f)
    {
        CacheReferences();

        rectTransform.DOKill();
        canvasGroup.DOKill();

        rectTransform.anchoredPosition = startAnchoredPosition;
        rectTransform.localScale = Vector3.zero;
        canvasGroup.alpha = 1f;
        rectTransform.DOAnchorPos(targetAnchoredPosition, expandDuration).SetDelay(delay).SetEase(expandEase);
        rectTransform.DOScale(Vector3.one, expandDuration).SetDelay(delay).SetEase(expandEase);
        canvasGroup.DOFade(1f, expandDuration * 0.8f).SetDelay(delay).SetEase(Ease.OutQuad);
        button.interactable = true;
        slotVisualHidden = false;
    }

    public void ShowImmediate(Vector2 anchoredPosition)
    {
        CacheReferences();
        if (rectTransform == null || canvasGroup == null)
        {
            return;
        }

        rectTransform.DOKill();
        canvasGroup.DOKill();

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        rectTransform.anchoredPosition = anchoredPosition;

        if (slotVisualHidden)
        {
            rectTransform.localScale = Vector3.zero;
            canvasGroup.alpha = 0f;
            if (button != null)
            {
                button.interactable = false;
            }

            return;
        }

        rectTransform.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
        if (button != null)
        {
            button.interactable = true;
        }
    }

    public void Hide()
    {
        CacheReferences();

        rectTransform.DOKill();
        canvasGroup.DOKill();

        HideIngredientsImmediate();
        button.interactable = false;
        canvasGroup.DOFade(0f, collapseDuration)
            .SetEase(Ease.OutQuad);
        rectTransform.DOScale(Vector3.zero, collapseDuration).SetEase(collapseEase);
    }

    public void HideImmediate()
    {
        CacheReferences();
        rectTransform.DOKill();
        canvasGroup.DOKill();
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.zero;
        canvasGroup.alpha = 0f;
        button.interactable = false;
        HideIngredientsImmediate();
    }

    private void CacheReferences()
    {
        if (isCachingReferences)
        {
            return;
        }

        isCachingReferences = true;
        try
        {
            if (rectTransform == null)
            {
                rectTransform = transform as RectTransform;
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (ingredientsRoot == null)
            {
                Transform target = transform.Find("Ingredients");
                ingredientsRoot = target as RectTransform;
            }

            if (IngrdientsSlots == null || IngrdientsSlots.Count == 0)
            {
                if (ingredientsRoot != null)
                {
                    IngrdientsSlots = new List<ItemSlot>();
                    for (int i = 0; i < ingredientsRoot.childCount; i++)
                    {
                        Transform child = ingredientsRoot.GetChild(i);
                        if (child == null)
                        {
                            continue;
                        }

                        ItemSlot itemSlot = child.GetComponent<ItemSlot>();
                        if (!IsIngredientSlotCandidate(itemSlot, ingredientsRoot))
                        {
                            continue;
                        }

                        IngrdientsSlots.Add(itemSlot);
                    }
                }
            }

            if (IngrdientsSlots != null && IngrdientsSlots.Count > 0)
            {
                for (int i = IngrdientsSlots.Count - 1; i >= 0; i--)
                {
                    if (!IsIngredientSlotCandidate(IngrdientsSlots[i], ingredientsRoot))
                    {
                        IngrdientsSlots.RemoveAt(i);
                    }
                }
            }

            if (IngrdientsSlots != null && IngrdientsSlots.Count > 1)
            {
                IngrdientsSlots.Sort((left, right) =>
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
            }
        }
        finally
        {
            isCachingReferences = false;
        }
    }

    private void BindButton()
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(ToggleIngredients);
        button.onClick.AddListener(ToggleIngredients);
    }

    private void BindCreateButton()
    {
        if (createButton == null)
        {
            return;
        }

        createButton.onClick.RemoveListener(HandleCreateClicked);
        createButton.onClick.AddListener(HandleCreateClicked);
    }

    private void HandleCreateClicked()
    {
        int craftItemId = ItemId;
        if (craftItemId < 0 || !HasItem)
        {
            return;
        }

        if (!TryPrepareHandForCrafting(craftItemId))
        {
            RefreshIngredients(false);
            return;
        }

        BagSlot parentBagSlot = GetComponentInParent<BagSlot>();
        if (parentBagSlot != null && !parentBagSlot.CanCraftItem(craftItemId))
        {
            parentBagSlot.RefreshCraftingAvailability();
            return;
        }

        if (!TryBuildIngredientBuffer(craftItemId))
        {
            return;
        }

        if (!HasAllIngredients())
        {
            RefreshIngredients(false);
            return;
        }

        List<CraftingTreeRuntime.IngredientEntry> refundIngredients = new List<CraftingTreeRuntime.IngredientEntry>(ingredientBuffer.Count);
        for (int i = 0; i < ingredientBuffer.Count; i++)
        {
            refundIngredients.Add(ingredientBuffer[i]);
        }

        PlayerHUD hud = FindObjectOfType<PlayerHUD>();
        if (hud == null || !hud.TryEnqueueCrafting(craftItemId, refundIngredients))
        {
            return;
        }

        ConsumeIngredients();
        RefreshIngredients(false);
    }

    private void ToggleIngredients()
    {
        if (!HasItem)
        {
            HideIngredientsImmediate();
            return;
        }

        if (ingredientsVisible)
        {
            HideIngredientsImmediate();
            ShowSiblingCraftingSlots();
        }
        else
        {
            HideSiblingCraftingSlots();

            ShowIngredients();
            activeIngredientsSlot = this;
        }
    }

    private void ShowIngredients()
    {
        CacheReferences();
        if (ingredientsRoot == null)
        {
            return;
        }

        if (!ingredientsRoot.gameObject.activeSelf)
        {
            ingredientsRoot.gameObject.SetActive(true);
        }

        RefreshIngredients(true);
        if (!ingredientsVisible)
        {
            ingredientsVisible = true;
        }
    }

    private void RefreshIngredients()
    {
        RefreshIngredients(true);
    }

    private void RefreshIngredients(bool animate)
    {
        CacheReferences();
        if (ingredientsRoot == null)
        {
            return;
        }

        ingredientBuffer.Clear();
        if (!CraftingTreeRuntime.TryGetIngredients(ItemId, ingredientBuffer))
        {
            HideIngredientsImmediate();
            return;
        }

        if (IngrdientsSlots == null)
        {
            return;
        }

        bool hasAllIngredients = true;
        for (int i = 0; i < IngrdientsSlots.Count; i++)
        {
            ItemSlot slot = IngrdientsSlots[i];
            if (slot == null)
            {
                continue;
            }

            if (i < ingredientBuffer.Count)
            {
                CraftingTreeRuntime.IngredientEntry entry = ingredientBuffer[i];
                int ownedCount = GetOwnedIngredientCount(entry.itemId);
                bool hasEnough = ownedCount >= entry.count;
                if (!hasEnough)
                {
                    hasAllIngredients = false;
                }
                slot.SetItemDisplay(entry.itemId, ownedCount, entry.count, true);
                SetIngredientSlotAlpha(slot, hasEnough);
                if (!slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(true);
                }
            }
            else
            {
                slot.Clear();
                SetIngredientSlotAlpha(slot, true);
                if (slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(false);
                }
            }
        }

        if (!ingredientsRoot.gameObject.activeSelf)
        {
            ingredientsRoot.gameObject.SetActive(true);
        }

        bool handReady = CanPrepareHandForCrafting(ItemId);
        SetCreateButtonVisible((hasAllIngredients && handReady) || blockedByCraftingMapObject);
        if (animate)
        {
            AnimateIngredientSlots(ingredientBuffer.Count);
        }
        ingredientsVisible = true;
    }

    public void RefreshIngredientsIfVisible()
    {
        if (!ingredientsVisible)
        {
            return;
        }

        RefreshIngredients(false);
    }

    public void RefreshCraftingAvailabilityVisuals()
    {
        RefreshCraftingMapObjectState();
        RefreshIngredientsIfVisible();
    }

    private bool TryBuildIngredientBuffer(int itemId)
    {
        ingredientBuffer.Clear();
        return CraftingTreeRuntime.TryGetIngredients(itemId, ingredientBuffer);
    }

    private bool HasAllIngredients()
    {
        for (int i = 0; i < ingredientBuffer.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry entry = ingredientBuffer[i];
            if (GetOwnedIngredientCount(entry.itemId) < entry.count)
            {
                return false;
            }
        }

        return ingredientBuffer.Count > 0;
    }

    private bool CanPrepareHandForCrafting(int craftItemId)
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
        }

        Player player = GameManager.Instance.Player;
        return player.CanAcceptHandObject(craftItemId) || player.CanClearHandIntoBag();
    }

    private bool TryPrepareHandForCrafting(int craftItemId)
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
        }

        Player player = GameManager.Instance.Player;
        return player.CanAcceptHandObject(craftItemId) || player.TryStoreHandItemsInBag();
    }

    private void ConsumeIngredients()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return;
        }

        Player player = GameManager.Instance.Player;
        PlayerBag bag = player.GetBag();
        PlayerBag handBag = player.GetHandBag();
        TerrainGenerator terrain = ResolveTerrain();
        Vector3 origin = player.transform.position;

        for (int i = 0; i < ingredientBuffer.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry entry = ingredientBuffer[i];
            int remaining = Mathf.Max(0, entry.count);
            if (remaining <= 0)
            {
                continue;
            }

            if (bag != null)
            {
                int removed = bag.RemoveItems(entry.itemId, remaining);
                remaining -= removed;
            }

            if (remaining > 0 && handBag != null)
            {
                handBag.RefreshExternalStackCounts(false);
                int removed = handBag.RemoveItems(entry.itemId, remaining);
                remaining -= removed;
            }

            if (remaining > 0 && terrain != null)
            {
                terrain.RemoveDroppedItemsAround(origin, entry.itemId, 2, remaining);
            }
        }
    }

    private void HideIngredientsImmediate()
    {
        if (isHidingIngredientsImmediate)
        {
            return;
        }

        isHidingIngredientsImmediate = true;
        try
        {
        ingredientsVisible = false;
        if (activeIngredientsSlot == this)
        {
            activeIngredientsSlot = null;
        }
        if (ingredientsRoot != null)
        {
            ingredientsRoot.gameObject.SetActive(false);
        }

        if (createButton != null)
        {
            createButton.interactable = false;
            if (createButton.gameObject.activeSelf)
            {
                createButton.gameObject.SetActive(false);
            }
        }

        if (IngrdientsSlots != null)
        {
            for (int i = 0; i < IngrdientsSlots.Count; i++)
            {
                ItemSlot slot = IngrdientsSlots[i];
                if (slot == null)
                {
                    continue;
                }

                RectTransform slotRect = slot.transform as RectTransform;
                if (slotRect != null)
                {
                    slotRect.DOKill();
                    slotRect.localScale = Vector3.zero;
                }
            }
        }
        }
        finally
        {
            isHidingIngredientsImmediate = false;
        }
    }

    private void AnimateIngredientSlots(int visibleCount)
    {
        if (IngrdientsSlots == null || IngrdientsSlots.Count == 0)
        {
            return;
        }

        int safeVisibleCount = Mathf.Max(0, visibleCount);
        for (int i = 0; i < IngrdientsSlots.Count; i++)
        {
            ItemSlot slot = IngrdientsSlots[i];
            if (slot == null)
            {
                continue;
            }

            RectTransform slotRect = slot.transform as RectTransform;
            if (slotRect == null)
            {
                continue;
            }

            slotRect.DOKill();

            if (i < safeVisibleCount)
            {
                if (!slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(true);
                }

                slotRect.localScale = Vector3.zero;
                float delay = i * ingredientRevealDelay;
                slotRect.DOScale(Vector3.one, ingredientRevealDuration).SetDelay(delay).SetEase(ingredientRevealEase);
            }
            else
            {
                slotRect.localScale = Vector3.zero;
            }
        }
    }

    private void SetIngredientSlotAlpha(ItemSlot slot, bool hasEnough)
    {
        if (slot == null)
        {
            return;
        }

        float targetAlpha = hasEnough ? 1f : Mathf.Clamp01(insufficientIngredientAlpha);
        slot.SetIconAlpha(targetAlpha);
    }

    private void HideSiblingCraftingSlots()
    {
        if (activeIngredientsSlot != null && activeIngredientsSlot != this)
        {
            activeIngredientsSlot.HideIngredientsImmediate();
        }

        Transform parent = transform.parent;
        if (parent == null)
        {
            return;
        }

        CraftingSlot[] siblings = parent.GetComponentsInChildren<CraftingSlot>(true);
        for (int i = 0; i < siblings.Length; i++)
        {
            CraftingSlot sibling = siblings[i];
            if (sibling == null || sibling == this)
            {
                continue;
            }

            sibling.HideIngredientsImmediate();
            sibling.SetSlotVisual(false);
        }
    }

    private void ShowSiblingCraftingSlots()
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            return;
        }

        CraftingSlot[] siblings = parent.GetComponentsInChildren<CraftingSlot>(true);
        for (int i = 0; i < siblings.Length; i++)
        {
            CraftingSlot sibling = siblings[i];
            if (sibling == null || sibling == this)
            {
                continue;
            }

            if (sibling.HasItem)
            {
                sibling.SetSlotVisual(true);
            }
        }
    }

    private void SetSlotVisual(bool visible)
    {
        CacheReferences();
        if (rectTransform == null || canvasGroup == null)
        {
            return;
        }

        rectTransform.DOKill();
        canvasGroup.DOKill();

        if (visible)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            rectTransform.localScale = Vector3.one;
            canvasGroup.alpha = 1f;
            if (button != null)
            {
                button.interactable = true;
            }
            slotVisualHidden = false;
        }
        else
        {
            rectTransform.localScale = Vector3.zero;
            canvasGroup.alpha = 0f;
            if (button != null)
            {
                button.interactable = false;
            }
            slotVisualHidden = true;
        }
    }

    private void SetCreateButtonVisible(bool visible)
    {
        if (createButton == null)
        {
            return;
        }

        if (visible && !createButton.gameObject.activeSelf)
        {
            createButton.gameObject.SetActive(true);
        }

        createButton.interactable = visible;

        Graphic targetGraphic = createButton.targetGraphic;
        if (targetGraphic != null)
        {
            targetGraphic.enabled = visible;
        }

        RefreshCraftingMapObjectVisuals();

        if (!visible && createButton.gameObject.activeSelf)
        {
            createButton.gameObject.SetActive(false);
        }
    }

    private void RefreshCraftingMapObjectState()
    {
        requiredCraftingMapObjectIds.Clear();
        blockedByCraftingMapObject = false;
        requiredCraftingMapObjectId = -1;

        if (!HasItem)
        {
            RefreshCraftingMapObjectVisuals();
            return;
        }

        if (!CraftingTreeRuntime.TryGetRequiredCraftingMapObjectIds(ItemId, requiredCraftingMapObjectIds))
        {
            RefreshCraftingMapObjectVisuals();
            return;
        }

        for (int i = 0; i < requiredCraftingMapObjectIds.Count; i++)
        {
            int candidateId = requiredCraftingMapObjectIds[i];
            if (candidateId < 0)
            {
                continue;
            }

            if (requiredCraftingMapObjectId < 0 || candidateId < requiredCraftingMapObjectId)
            {
                requiredCraftingMapObjectId = candidateId;
            }
        }

        BagSlot parentBagSlot = GetComponentInParent<BagSlot>();
        if (parentBagSlot != null)
        {
            blockedByCraftingMapObject = !parentBagSlot.CanCraftItem(ItemId);
        }

        RefreshCraftingMapObjectVisuals();
    }

    private void RefreshCraftingMapObjectVisuals()
    {
        if (isRefreshingCraftingMapObjectVisuals)
        {
            return;
        }

        isRefreshingCraftingMapObjectVisuals = true;

        bool showBlockedState = HasItem && blockedByCraftingMapObject && requiredCraftingMapObjectId >= 0;
        try
        {
            if (canNotImage != null)
            {
                canNotImage.enabled = showBlockedState;
            }

            if (createIcon != null)
            {
                createIcon.enabled = HasItem && !showBlockedState;
            }

            if (mapObjectIcon != null)
            {
                if (showBlockedState)
                {
                    mapObjectIcon.sprite = ResolveRequiredCraftingMapObjectIcon(requiredCraftingMapObjectId);
                    mapObjectIcon.enabled = mapObjectIcon.sprite != null;
                }
                else
                {
                    mapObjectIcon.sprite = null;
                    mapObjectIcon.enabled = false;
                }
            }
        }
        finally
        {
            isRefreshingCraftingMapObjectVisuals = false;
        }
    }

    private Sprite ResolveRequiredCraftingMapObjectIcon(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        return GameManager.Instance.ItemManger.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet)
            ? itemSet.icon
            : null;
    }

    private int GetOwnedIngredientCount(int itemId)
    {
        if (itemId < 0)
        {
            return 0;
        }

        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return 0;
        }

        int total = 0;
        PlayerBag bag = GameManager.Instance.Player.GetBag();
        if (bag != null)
        {
            total += bag.GetTotalItemCount(itemId);
        }

        PlayerBag handBag = GameManager.Instance.Player.GetHandBag();
        if (handBag != null)
        {
            handBag.RefreshExternalStackCounts(false);
            total += handBag.GetTotalItemCount(itemId);
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain != null)
        {
            total += terrain.GetDroppedItemCountAround(GameManager.Instance.Player.transform.position, itemId, 2);
        }

        return total;
    }

    private static TerrainGenerator ResolveTerrain()
    {
        return TerrainGenerator.ResolveActive();
    }

    private bool IsIngredientSlotCandidate(ItemSlot slot, RectTransform expectedRoot)
    {
        if (slot == null || slot == this)
        {
            return false;
        }

        if (slot is CraftingSlot)
        {
            return false;
        }

        if (expectedRoot == null)
        {
            return false;
        }

        Transform slotTransform = slot.transform;
        if (slotTransform == null || slotTransform.parent != expectedRoot)
        {
            return false;
        }

        return true;
    }
}
