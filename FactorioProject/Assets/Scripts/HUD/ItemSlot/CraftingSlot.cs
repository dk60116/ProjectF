using DG.Tweening;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class CraftingSlot : ItemSlot, IPointerEnterHandler, IPointerExitHandler
{
    private const float DefaultIngredientSpacing = 10f;
    private const float DefaultIngredientChildSize = 80f;
    private const float MinimumLayoutSize = 0.01f;

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

    [SerializeField, Range(0.1f, 1f)]
    private float insufficientIngredientAlpha = 0.45f;

    [SerializeField, Min(0f)]
    private float ingredientRevealStepDelay = 0.02f;

    [SerializeField, Min(0f)]
    private float ingredientRevealFadeDuration = 0.04f;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Button button;

    [SerializeField, FormerlySerializedAs("IngrdientsSlots")]
    private List<ItemSlot> ingredientSlots;
    [SerializeField]
    private Button createButton;

    [SerializeField]
    private Image canNotImage;
    [SerializeField]
    private TextMeshProUGUI createCountTxt;
    [SerializeField]
    private Image createIcon, mapObjectIcon;

    private bool ingredientsVisible;
    private bool slotVisualHidden;
    private bool blockedByCraftingMapObject;
    private bool blockedByRequiredManual;
    private bool isCachingReferences;
    private bool isRefreshingCraftingMapObjectVisuals;
    private bool isHidingIngredientsImmediate;
    private bool ingredientsManualLayoutReady;
    private bool ingredientRevealAnimating;
    private bool targetIconRaised;
    private Sequence ingredientRevealSequence;
    private int targetIconOriginalSiblingIndex = -1;
    private float ingredientsSpacing = DefaultIngredientSpacing;
    private float ingredientsRootHorizontalOffset = DefaultIngredientChildSize + DefaultIngredientSpacing;
    private int ingredientsExpansionDirectionSign;
    private int requiredCraftingMapObjectId = -1;
    private int requiredManualItemId = -1;
    private Func<int, bool> externalCreateAction;
    private Func<int, bool> externalCanCreate;
    private int externallyProvidedIngredientItemId = -1;
    private int externallyProvidedIngredientCount;
    private bool createActionReady = true;
    private readonly List<CraftingTreeRuntime.IngredientEntry> ingredientBuffer = new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<int> requiredCraftingMapObjectIds = new List<int>();
    private readonly List<HUDButtonHoverTween> hoverTweenBuffer = new List<HUDButtonHoverTween>();
    private readonly Dictionary<RectTransform, Vector2> ingredientLayoutSizes = new Dictionary<RectTransform, Vector2>();

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
        RestoreTargetIconRenderOrder();
        HideIngredientsImmediate();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        CacheReferences();
        if (rectTransform != null
            && eventData != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                rectTransform,
                eventData.position,
                eventData.enterEventCamera))
        {
            RaiseTargetIconRenderOrder();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        RestoreTargetIconRenderOrder();
    }

    private void RaiseTargetIconRenderOrder()
    {
        Transform targetIcon = IconImage != null ? IconImage.transform : null;
        if (targetIconRaised || targetIcon == null || targetIcon.parent != transform)
        {
            return;
        }

        targetIconOriginalSiblingIndex = targetIcon.GetSiblingIndex();
        targetIcon.SetAsLastSibling();
        targetIconRaised = true;
    }

    private void RestoreTargetIconRenderOrder()
    {
        if (!targetIconRaised)
        {
            return;
        }

        Transform targetIcon = IconImage != null ? IconImage.transform : null;
        if (targetIcon != null && targetIcon.parent == transform)
        {
            int maxSiblingIndex = Mathf.Max(0, targetIcon.parent.childCount - 1);
            targetIcon.SetSiblingIndex(Mathf.Clamp(targetIconOriginalSiblingIndex, 0, maxSiblingIndex));
        }

        targetIconOriginalSiblingIndex = -1;
        targetIconRaised = false;
    }

    public override void SetItem(int itemId, int itemCount, int maxItemCount = 0)
    {
        base.SetItem(itemId, itemCount, maxItemCount);
        RefreshCreateCountText();
        RefreshCraftingMapObjectState();
        if (ingredientsVisible)
        {
            RefreshIngredients();
        }
    }

    public void Show(Vector2 startAnchoredPosition, Vector2 targetAnchoredPosition, float delay = 0f)
    {
        CacheReferences();
        ResetHoverTweensImmediate();

        rectTransform.DOKill();
        canvasGroup.DOKill();

        rectTransform.anchoredPosition = targetAnchoredPosition;
        rectTransform.localScale = Vector3.zero;
        canvasGroup.alpha = 1f;
        slotVisualHidden = false;
        if (button != null)
        {
            button.interactable = false;
        }

        rectTransform.DOScale(Vector3.one, expandDuration)
            .SetDelay(delay)
            .SetEase(expandEase)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (button != null && !slotVisualHidden)
                {
                    button.interactable = true;
                }
            });
        canvasGroup.DOFade(1f, expandDuration * 0.8f)
            .SetDelay(delay)
            .SetEase(Ease.OutQuad)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    public void ShowImmediate(Vector2 anchoredPosition)
    {
        CacheReferences();
        ResetHoverTweensImmediate();
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
        RestoreTargetIconRenderOrder();
        ResetHoverTweensImmediate();

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
        RestoreTargetIconRenderOrder();
        ResetHoverTweensImmediate();
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
                ingredientsRoot = ResolveSerializedIngredientsRoot();
            }

            if (ingredientsRoot == null)
            {
                Transform target = transform.Find("Ingredients");
                ingredientsRoot = target as RectTransform;
            }

            if (createCountTxt == null)
            {
                Transform target = transform.Find("CountText");
                createCountTxt = target != null ? target.GetComponent<TextMeshProUGUI>() : null;
            }

            PrepareIngredientsManualLayout();

            if (ingredientSlots == null || ingredientSlots.Count == 0)
            {
                if (ingredientsRoot != null)
                {
                    ingredientSlots = new List<ItemSlot>();
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

                        ingredientSlots.Add(itemSlot);
                    }
                }
            }

            if (ingredientSlots != null && ingredientSlots.Count > 0)
            {
                for (int i = ingredientSlots.Count - 1; i >= 0; i--)
                {
                    if (!IsIngredientSlotCandidate(ingredientSlots[i], ingredientsRoot))
                    {
                        ingredientSlots.RemoveAt(i);
                    }
                }
            }

            if (ingredientSlots != null && ingredientSlots.Count > 1)
            {
                ingredientSlots.Sort((left, right) =>
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

            CacheStableIngredientLayoutSizes();
            DisableIngredientSlotHoverTweens();
            RefreshCreateCountText();
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

        bool usesExternalCreateAction = externalCreateAction != null;
        BagSlot parentBagSlot = usesExternalCreateAction ? null : GetComponentInParent<BagSlot>();
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
            RefreshIngredients();
            return;
        }

        if (usesExternalCreateAction)
        {
            if (externalCanCreate != null && !externalCanCreate(craftItemId))
            {
                RefreshIngredients();
                return;
            }

            List<CraftingTreeRuntime.IngredientEntry> externalConsumedIngredients = null;
            if (ingredientBuffer.Count > 0
                && !TryConsumeIngredients(out externalConsumedIngredients))
            {
                RefreshIngredients();
                return;
            }

            if (!externalCreateAction(craftItemId))
            {
                RefundIngredients(externalConsumedIngredients);
            }

            RefreshIngredients();
            return;
        }

        if (!CanPrepareHandForCrafting(craftItemId))
        {
            RefreshIngredients();
            return;
        }

        PlayerHUD hud = FindObjectOfType<PlayerHUD>();
        if (hud == null || !hud.CanEnqueueCrafting(craftItemId))
        {
            return;
        }

        if (!TryConsumeIngredients(out List<CraftingTreeRuntime.IngredientEntry> consumedIngredients))
        {
            RefreshIngredients();
            return;
        }

        if (!TryPrepareHandForCrafting(craftItemId))
        {
            RefundIngredients(consumedIngredients);
            RefreshIngredients();
            return;
        }

        if (!hud.TryEnqueueCrafting(craftItemId, consumedIngredients))
        {
            RefundIngredients(consumedIngredients);
            RefreshIngredients();
            return;
        }

        RefreshIngredients();
    }

    private void ToggleIngredients()
    {
        if (!HasItem)
        {
            HideIngredientsImmediate();
            return;
        }

        ResetHoverTweensImmediate();

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

        SetTargetButtonHoverEnabled(false);
        CaptureIngredientsExpansionDirection();
        RefreshIngredients(true);
    }

    public void ConfigureExternalCreateAction(
        Func<int, bool> createAction,
        int providedIngredientItemId = -1,
        int providedIngredientCount = 0,
        Func<int, bool> canCreate = null)
    {
        externalCreateAction = createAction;
        externalCanCreate = canCreate;
        externallyProvidedIngredientItemId = providedIngredientItemId;
        externallyProvidedIngredientCount = Mathf.Max(0, providedIngredientCount);
        SetTargetButtonHoverEnabled(false);
        RefreshCraftingMapObjectState();
        RefreshIngredientsIfVisible();
    }

    public void ClearExternalCreateAction()
    {
        externalCreateAction = null;
        externalCanCreate = null;
        externallyProvidedIngredientItemId = -1;
        externallyProvidedIngredientCount = 0;
        createActionReady = true;
        SetTargetButtonHoverEnabled(true);
        RefreshCraftingMapObjectState();
    }

    public void ShowIngredientsForExternalUse()
    {
        if (externalCreateAction == null || !HasItem)
        {
            HideIngredientsImmediate();
            return;
        }

        ResetHoverTweensImmediate();
        ShowIngredients();
    }

    private void RefreshIngredients()
    {
        RefreshIngredients(false);
    }

    private void RefreshIngredients(bool forceSequentialReveal)
    {
        CacheReferences();
        if (ingredientsRoot == null)
        {
            return;
        }

        bool revealSequentially = forceSequentialReveal || !ingredientsVisible;
        bool preserveRunningReveal = !revealSequentially && ingredientRevealAnimating;
        bool rootWasActive = ingredientsRoot.gameObject.activeSelf;
        if (!TryBuildIngredientBuffer(ItemId))
        {
            HideIngredientsImmediate();
            return;
        }

        if (ingredientSlots == null)
        {
            return;
        }

        bool hasAllIngredients = RefreshIngredientSlotDisplays();

        bool handReady = externalCreateAction != null
            ? externalCanCreate == null || externalCanCreate(ItemId)
            : CanPrepareHandForCrafting(ItemId);
        BagSlot parentBagSlot = externalCreateAction == null
            ? GetComponentInParent<BagSlot>()
            : null;
        bool manualAccessReady = parentBagSlot == null
                                 || parentBagSlot.HasRequiredManualForCrafting(ItemId);
        bool craftingAccessReady = parentBagSlot == null || parentBagSlot.CanCraftItem(ItemId);
        bool createButtonReady = hasAllIngredients && handReady && craftingAccessReady;
        createActionReady = createButtonReady;
        bool hasManualRequirement = externalCreateAction == null && requiredManualItemId >= 0;
        bool showRequiredMapObject = externalCreateAction == null && requiredCraftingMapObjectId >= 0;
        bool showCreateSlot = manualAccessReady || hasManualRequirement || showRequiredMapObject;
        bool createButtonChangedVisibility = SetCreateButtonVisible(showCreateSlot);
        if (createButton != null)
        {
            createButton.interactable = manualAccessReady && createButtonReady;
        }
        bool resetCreateButtonLayout = ShouldResetCreateButtonLayout(
            revealSequentially,
            rootWasActive,
            createButtonChangedVisibility);
        if (resetCreateButtonLayout)
        {
            ResetCreateButtonHoverImmediate(false);
        }
        ApplyIngredientsManualLayout(ingredientBuffer.Count, resetCreateButtonLayout);
        SetIngredientSlotScalesImmediate(ingredientBuffer.Count);
        ApplyIngredientRevealState(ingredientBuffer.Count, revealSequentially, preserveRunningReveal);
        if (!rootWasActive && !ingredientsRoot.gameObject.activeSelf)
        {
            ingredientsRoot.gameObject.SetActive(true);
        }

        ForceIngredientsLayoutImmediate();
        ingredientsVisible = true;
    }

    private bool RefreshIngredientSlotDisplays()
    {
        bool hasAllIngredients = true;
        for (int i = 0; i < ingredientSlots.Count; i++)
        {
            ItemSlot slot = ingredientSlots[i];
            if (slot == null)
            {
                continue;
            }

            if (i >= ingredientBuffer.Count)
            {
                ClearIngredientSlotDisplay(slot);
                continue;
            }

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

        return hasAllIngredients;
    }

    private void ClearIngredientSlotDisplay(ItemSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        slot.Clear();
        SetIngredientSlotAlpha(slot, true);
        if (slot.gameObject.activeSelf)
        {
            slot.gameObject.SetActive(false);
        }
    }

    private static bool ShouldResetCreateButtonLayout(
        bool revealSequentially,
        bool rootWasActive,
        bool createButtonChangedVisibility)
    {
        return revealSequentially || !rootWasActive || createButtonChangedVisibility;
    }

    public void RefreshIngredientsIfVisible()
    {
        if (!ingredientsVisible)
        {
            return;
        }

        RefreshIngredients();
    }

    public void RefreshCraftingAvailabilityVisuals()
    {
        RefreshCraftingMapObjectState();
        RefreshIngredientsIfVisible();
    }

    private bool TryBuildIngredientBuffer(int itemId)
    {
        ingredientBuffer.Clear();
        bool hasRecipeIngredients = CraftingTreeRuntime.TryGetIngredients(itemId, ingredientBuffer);
        if (!hasRecipeIngredients || externalCreateAction == null)
        {
            return hasRecipeIngredients;
        }

        RemoveExternallyProvidedIngredient();
        return true;
    }

    private void RemoveExternallyProvidedIngredient()
    {
        int remainingProvidedCount = externallyProvidedIngredientCount;
        if (externallyProvidedIngredientItemId < 0 || remainingProvidedCount <= 0)
        {
            return;
        }

        for (int i = 0; i < ingredientBuffer.Count && remainingProvidedCount > 0; i++)
        {
            CraftingTreeRuntime.IngredientEntry entry = ingredientBuffer[i];
            if (entry.itemId != externallyProvidedIngredientItemId || entry.count <= 0)
            {
                continue;
            }

            int providedCount = Mathf.Min(entry.count, remainingProvidedCount);
            entry.count -= providedCount;
            remainingProvidedCount -= providedCount;
            if (entry.count <= 0)
            {
                ingredientBuffer.RemoveAt(i);
                i--;
            }
            else
            {
                ingredientBuffer[i] = entry;
            }
        }
    }

    private void RefreshCreateCountText()
    {
        if (createCountTxt == null)
        {
            return;
        }

        int outputCount = HasItem && ItemId >= 0
            ? CraftingTreeRuntime.GetOutputCount(ItemId)
            : 0;
        bool showCount = outputCount > 1;
        if (!showCount)
        {
            createCountTxt.text = string.Empty;
            if (createCountTxt.gameObject.activeSelf)
            {
                createCountTxt.gameObject.SetActive(false);
            }
            return;
        }

        createCountTxt.text = outputCount.ToString();
        if (!createCountTxt.gameObject.activeSelf)
        {
            createCountTxt.gameObject.SetActive(true);
        }
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

        return ingredientBuffer.Count > 0 || externalCreateAction != null;
    }

    private bool CanPrepareHandForCrafting(int craftItemId)
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
        }

        Player player = GameManager.Instance.Player;
        return player.CanAcceptHandObject(craftItemId)
               || player.CanClearHandIntoBag()
               || CanClearHandIntoBagAfterCrafting(player);
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

    private bool CanClearHandIntoBagAfterCrafting(Player player)
    {
        if (player == null || ingredientBuffer == null || ingredientBuffer.Count <= 0)
        {
            return false;
        }

        PlayerBag handBag = player.GetHandBag();
        if (handBag == null)
        {
            return true;
        }

        handBag.RefreshExternalStackCounts(false);
        int projectedHandCount = handBag.GetSlotCount(0);
        if (projectedHandCount <= 0)
        {
            return true;
        }

        int handItemId = handBag.GetSlotItemId(0);
        if (handItemId < 0)
        {
            return false;
        }

        PlayerBag bag = player.GetBag();
        if (bag == null)
        {
            return false;
        }

        int slotCount = bag.SlotCount;
        if (slotCount <= 0)
        {
            return false;
        }

        int[] slotItemIds = new int[slotCount];
        int[] slotCounts = new int[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            slotItemIds[i] = bag.GetSlotItemId(i);
            slotCounts[i] = bag.GetSlotCount(i);
        }

        for (int ingredientIndex = 0; ingredientIndex < ingredientBuffer.Count; ingredientIndex++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = ingredientBuffer[ingredientIndex];
            int remaining = Mathf.Max(0, ingredient.count);
            if (remaining <= 0 || ingredient.itemId < 0)
            {
                continue;
            }

            bool removedFromBag = false;
            for (int slotIndex = 0; slotIndex < slotCount && remaining > 0; slotIndex++)
            {
                if (slotItemIds[slotIndex] != ingredient.itemId || slotCounts[slotIndex] <= 0)
                {
                    continue;
                }

                int removed = Mathf.Min(slotCounts[slotIndex], remaining);
                slotCounts[slotIndex] -= removed;
                remaining -= removed;
                removedFromBag = removedFromBag || removed > 0;
                if (slotCounts[slotIndex] <= 0)
                {
                    slotCounts[slotIndex] = 0;
                    slotItemIds[slotIndex] = -1;
                }
            }

            if (removedFromBag)
            {
                SimulateBagDuplicateStackMerge(bag, slotItemIds, slotCounts);
            }

            if (remaining > 0 && ingredient.itemId == handItemId)
            {
                int removedFromHand = Mathf.Min(projectedHandCount, remaining);
                projectedHandCount -= removedFromHand;
                if (projectedHandCount <= 0)
                {
                    return true;
                }
            }
        }

        return GetProjectedBagCapacityForItem(bag, slotItemIds, slotCounts, handItemId)
               >= projectedHandCount;
    }

    private static void SimulateBagDuplicateStackMerge(
        PlayerBag bag,
        int[] slotItemIds,
        int[] slotCounts)
    {
        if (bag == null || slotItemIds == null || slotCounts == null)
        {
            return;
        }

        int slotCount = Mathf.Min(bag.SlotCount, Mathf.Min(slotItemIds.Length, slotCounts.Length));
        for (int targetIndex = 0; targetIndex < slotCount; targetIndex++)
        {
            int itemId = slotItemIds[targetIndex];
            if (itemId < 0)
            {
                continue;
            }

            int targetCapacity = Mathf.Max(0, bag.GetSlotCapacityForItem(targetIndex, itemId));
            int targetCount = Mathf.Clamp(slotCounts[targetIndex], 0, targetCapacity);
            for (int sourceIndex = targetIndex + 1; sourceIndex < slotCount && targetCount < targetCapacity; sourceIndex++)
            {
                if (slotItemIds[sourceIndex] != itemId || slotCounts[sourceIndex] <= 0)
                {
                    continue;
                }

                int moved = Mathf.Min(targetCapacity - targetCount, slotCounts[sourceIndex]);
                targetCount += moved;
                slotCounts[sourceIndex] -= moved;
                if (slotCounts[sourceIndex] <= 0)
                {
                    slotCounts[sourceIndex] = 0;
                    slotItemIds[sourceIndex] = -1;
                }
            }

            slotCounts[targetIndex] = targetCount;
        }
    }

    private static int GetProjectedBagCapacityForItem(
        PlayerBag bag,
        int[] slotItemIds,
        int[] slotCounts,
        int itemId)
    {
        if (bag == null || slotItemIds == null || slotCounts == null || itemId < 0)
        {
            return 0;
        }

        int totalCapacity = 0;
        int slotCount = Mathf.Min(bag.SlotCount, Mathf.Min(slotItemIds.Length, slotCounts.Length));
        for (int i = 0; i < slotCount; i++)
        {
            int capacity = Mathf.Max(0, bag.GetSlotCapacityForItem(i, itemId));
            int count = Mathf.Clamp(slotCounts[i], 0, capacity);
            if (count <= 0)
            {
                totalCapacity += capacity;
                continue;
            }

            if (slotItemIds[i] == itemId)
            {
                totalCapacity += Mathf.Max(0, capacity - count);
            }
        }

        return totalCapacity;
    }

    private bool TryConsumeIngredients(out List<CraftingTreeRuntime.IngredientEntry> consumedIngredients)
    {
        consumedIngredients = new List<CraftingTreeRuntime.IngredientEntry>();
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return false;
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

            int removedTotal = 0;
            if (bag != null)
            {
                int removed = bag.RemoveItems(entry.itemId, remaining);
                remaining -= removed;
                removedTotal += removed;
            }

            if (remaining > 0 && handBag != null)
            {
                handBag.RefreshExternalStackCounts(false);
                int removed = handBag.RemoveItems(entry.itemId, remaining);
                remaining -= removed;
                removedTotal += removed;
            }

            if (remaining > 0 && terrain != null)
            {
                int removed = terrain.RemoveDroppedItemsAround(origin, entry.itemId, 2, remaining);
                remaining -= removed;
                removedTotal += removed;
            }

            if (removedTotal > 0)
            {
                consumedIngredients.Add(new CraftingTreeRuntime.IngredientEntry(entry.itemId, removedTotal));
            }

            if (remaining > 0)
            {
                RefundIngredients(consumedIngredients);
                consumedIngredients.Clear();
                return false;
            }
        }

        return consumedIngredients.Count > 0;
    }

    private void RefundIngredients(IReadOnlyList<CraftingTreeRuntime.IngredientEntry> ingredients)
    {
        if (ingredients == null || ingredients.Count == 0)
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return;
        }

        Player player = GameManager.Instance.Player;
        TerrainGenerator terrain = ResolveTerrain();
        Vector3 refundOrigin = player.transform.position;

        for (int i = 0; i < ingredients.Count; i++)
        {
            CraftingTreeRuntime.IngredientEntry ingredient = ingredients[i];
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

    private void HideIngredientsImmediate()
    {
        if (isHidingIngredientsImmediate)
        {
            return;
        }

        isHidingIngredientsImmediate = true;
        try
        {
            StopIngredientReveal();
            ingredientsVisible = false;
            ingredientsExpansionDirectionSign = 0;
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
                ResetCreateButtonHoverImmediate(false);
                createButton.interactable = false;
                ResetRevealCanvasGroup(createButton.gameObject, 0f, false);
                if (createButton.gameObject.activeSelf)
                {
                    createButton.gameObject.SetActive(false);
                }
            }

            if (ingredientSlots != null)
            {
                for (int i = 0; i < ingredientSlots.Count; i++)
                {
                    ItemSlot slot = ingredientSlots[i];
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

                    ResetRevealCanvasGroup(slot.gameObject, 0f, false);
                }
            }

            SetTargetButtonHoverEnabled(externalCreateAction == null);
        }
        finally
        {
            isHidingIngredientsImmediate = false;
        }
    }

    private void SetIngredientSlotScalesImmediate(int visibleCount)
    {
        if (ingredientSlots == null || ingredientSlots.Count == 0)
        {
            return;
        }

        int safeVisibleCount = Mathf.Max(0, visibleCount);
        for (int i = 0; i < ingredientSlots.Count; i++)
        {
            ItemSlot slot = ingredientSlots[i];
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

                slotRect.localScale = Vector3.one;
            }
            else
            {
                slotRect.localScale = Vector3.zero;
            }
        }
    }

    private void ApplyIngredientRevealState(int visibleCount, bool revealSequentially, bool preserveRunningReveal)
    {
        if (ingredientSlots == null)
        {
            return;
        }

        if (revealSequentially)
        {
            StartIngredientReveal(visibleCount);
            return;
        }

        if (!preserveRunningReveal)
        {
            StopIngredientReveal();
        }

        int safeVisibleCount = Mathf.Max(0, visibleCount);
        for (int i = 0; i < ingredientSlots.Count; i++)
        {
            ItemSlot slot = ingredientSlots[i];
            if (slot == null)
            {
                continue;
            }

            bool visible = i < safeVisibleCount && slot.gameObject.activeSelf;
            if (visible && !preserveRunningReveal)
            {
                SetRevealCanvasGroup(slot.gameObject, 1f, true);
            }
            else if (!visible)
            {
                ResetRevealCanvasGroup(slot.gameObject, 0f, false);
            }
        }

        if (createButton != null && createButton.gameObject.activeSelf && !preserveRunningReveal)
        {
            SetRevealCanvasGroup(
                createButton.gameObject,
                ResolveCreateButtonVisualAlpha(),
                createButton.interactable);
        }
    }

    private void StartIngredientReveal(int visibleCount)
    {
        StopIngredientReveal();

        Sequence sequence = DOTween.Sequence()
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);

        bool hasRevealTarget = false;
        int safeVisibleCount = Mathf.Max(0, visibleCount);

        for (int i = 0; i < ingredientSlots.Count; i++)
        {
            ItemSlot slot = ingredientSlots[i];
            if (slot == null)
            {
                continue;
            }

            bool visible = i < safeVisibleCount && slot.gameObject.activeSelf;
            if (visible)
            {
                AppendRevealTarget(sequence, slot.gameObject, ref hasRevealTarget);
            }
            else
            {
                ResetRevealCanvasGroup(slot.gameObject, 0f, false);
            }
        }

        // The create button belongs at the outer end of the expanded row.
        // This is the screen-left edge for Hand and the screen-right edge for bag slots.
        AppendCreateButtonRevealTarget(sequence, ref hasRevealTarget);

        if (!hasRevealTarget)
        {
            sequence.Kill();
            return;
        }

        ingredientRevealAnimating = true;
        ingredientRevealSequence = sequence;
        sequence.OnComplete(() => FinishIngredientReveal(sequence));
        sequence.OnKill(() => FinishIngredientReveal(sequence));
    }

    private void AppendCreateButtonRevealTarget(Sequence sequence, ref bool hasRevealTarget)
    {
        if (createButton != null && createButton.gameObject.activeSelf)
        {
            bool restoreCreateInteractable = createButton.interactable;
            createButton.interactable = false;
            AppendRevealTarget(
                sequence,
                createButton.gameObject,
                ref hasRevealTarget,
                () => createButton.interactable = restoreCreateInteractable,
                ResolveCreateButtonVisualAlpha(),
                restoreCreateInteractable);
        }
    }

    private void AppendRevealTarget(
        Sequence sequence,
        GameObject target,
        ref bool hasPreviousTarget,
        TweenCallback onComplete = null,
        float targetAlpha = 1f,
        bool interactive = true)
    {
        if (sequence == null || target == null)
        {
            return;
        }

        CanvasGroup revealGroup = EnsureRevealCanvasGroup(target);
        revealGroup.DOKill();
        SetRevealCanvasGroup(revealGroup, 0f, false);
        if (hasPreviousTarget)
        {
            float stepGap = Mathf.Max(0f, ingredientRevealStepDelay);
            if (stepGap > 0f)
            {
                sequence.AppendInterval(stepGap);
            }
        }

        hasPreviousTarget = true;
        float duration = Mathf.Max(0f, ingredientRevealFadeDuration);
        if (duration <= 0f)
        {
            sequence.AppendCallback(() =>
            {
                SetRevealCanvasGroup(revealGroup, targetAlpha, interactive);
                onComplete?.Invoke();
            });
            return;
        }

        sequence.Append(
            revealGroup.DOFade(targetAlpha, duration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    SetRevealCanvasGroup(revealGroup, targetAlpha, interactive);
                    onComplete?.Invoke();
                }));
    }

    private float ResolveCreateButtonVisualAlpha()
    {
        if (externalCreateAction == null
            && ((blockedByCraftingMapObject && requiredCraftingMapObjectId >= 0)
                || (blockedByRequiredManual && requiredManualItemId >= 0)))
        {
            return 1f;
        }

        return createActionReady ? 1f : Mathf.Clamp01(insufficientIngredientAlpha);
    }

    private void StopIngredientReveal()
    {
        Sequence sequence = ingredientRevealSequence;
        ingredientRevealSequence = null;
        ingredientRevealAnimating = false;
        if (sequence != null && sequence.IsActive())
        {
            sequence.Kill();
        }
    }

    private void FinishIngredientReveal(Sequence sequence)
    {
        if (ingredientRevealSequence != sequence)
        {
            return;
        }

        ingredientRevealSequence = null;
        ingredientRevealAnimating = false;
    }

    private static CanvasGroup EnsureRevealCanvasGroup(GameObject target)
    {
        CanvasGroup revealGroup = target.GetComponent<CanvasGroup>();
        if (revealGroup == null)
        {
            revealGroup = target.AddComponent<CanvasGroup>();
        }

        return revealGroup;
    }

    private static void SetRevealCanvasGroup(GameObject target, float alpha, bool interactive)
    {
        if (target == null)
        {
            return;
        }

        CanvasGroup revealGroup = EnsureRevealCanvasGroup(target);
        revealGroup.DOKill();
        SetRevealCanvasGroup(revealGroup, alpha, interactive);
    }

    private static void SetRevealCanvasGroup(CanvasGroup revealGroup, float alpha, bool interactive)
    {
        if (revealGroup == null)
        {
            return;
        }

        revealGroup.alpha = Mathf.Clamp01(alpha);
        revealGroup.interactable = interactive;
        revealGroup.blocksRaycasts = interactive;
    }

    private static void ResetRevealCanvasGroup(GameObject target, float alpha, bool interactive)
    {
        if (target == null)
        {
            return;
        }

        CanvasGroup revealGroup = target.GetComponent<CanvasGroup>();
        if (revealGroup == null)
        {
            return;
        }

        revealGroup.DOKill();
        SetRevealCanvasGroup(revealGroup, alpha, interactive);
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
        ResetHoverTweensImmediate();
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
            RestoreTargetIconRenderOrder();
            rectTransform.localScale = Vector3.zero;
            canvasGroup.alpha = 0f;
            if (button != null)
            {
                button.interactable = false;
            }
            slotVisualHidden = true;
        }
    }

    private void ForceIngredientsLayoutImmediate()
    {
        if (ingredientsRoot == null)
        {
            return;
        }

        PrepareIngredientsManualLayout();
        Canvas.ForceUpdateCanvases();
    }

    private void PrepareIngredientsManualLayout()
    {
        if (ingredientsRoot == null)
        {
            return;
        }

        HorizontalLayoutGroup horizontalLayoutGroup = ingredientsRoot.GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayoutGroup != null)
        {
            if (!ingredientsManualLayoutReady)
            {
                ingredientsSpacing = Mathf.Max(0f, horizontalLayoutGroup.spacing);
            }
            horizontalLayoutGroup.enabled = false;
        }

        ContentSizeFitter contentSizeFitter = ingredientsRoot.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null)
        {
            contentSizeFitter.enabled = false;
        }

        Vector2 targetSlotSize = ResolveCurrentIngredientChildSize(rectTransform);
        ingredientsRootHorizontalOffset = targetSlotSize.x + ingredientsSpacing;

        ingredientsManualLayoutReady = true;
    }

    private void ApplyIngredientsManualLayout(int visibleIngredientCount, bool applyCreateButtonSize = false)
    {
        if (ingredientsRoot == null)
        {
            return;
        }

        PrepareIngredientsManualLayout();

        int directionSign = GetIngredientsExpansionDirectionSign();
        ApplyIngredientsRootDirection(directionSign);

        float nextX = 0f;
        float maxHeight = 0f;
        int safeVisibleCount = Mathf.Max(0, visibleIngredientCount);

        RectTransform createRect = createButton != null ? createButton.transform as RectTransform : null;

        if (ingredientSlots != null)
        {
            for (int i = 0; i < ingredientSlots.Count; i++)
            {
                ItemSlot slot = ingredientSlots[i];
                RectTransform slotRect = slot != null ? slot.transform as RectTransform : null;
                if (slotRect == null || !slot.gameObject.activeSelf || i >= safeVisibleCount)
                {
                    continue;
                }

                Vector2 childSize = ResolveStableIngredientChildSize(slotRect);
                PositionIngredientChild(slotRect, nextX, childSize, true, directionSign);
                nextX += childSize.x + ingredientsSpacing;
                maxHeight = Mathf.Max(maxHeight, childSize.y);
            }
        }

        PositionCreateButtonIfVisible(
            createRect,
            directionSign,
            ref nextX,
            ref maxHeight,
            applyCreateButtonSize);

        float width = Mathf.Max(0f, nextX - ingredientsSpacing);
        ingredientsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        ingredientsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, maxHeight);
    }

    private void PositionCreateButtonIfVisible(
        RectTransform createRect,
        int directionSign,
        ref float nextX,
        ref float maxHeight,
        bool applySize)
    {
        if (createRect != null && createButton != null && createButton.gameObject.activeSelf)
        {
            Vector2 childSize = ResolveStableIngredientChildSize(createRect);
            PositionIngredientChild(createRect, nextX, childSize, applySize, directionSign);
            nextX += childSize.x + ingredientsSpacing;
            maxHeight = Mathf.Max(maxHeight, childSize.y);
        }
    }

    private void CacheStableIngredientLayoutSizes()
    {
        if (ingredientSlots != null)
        {
            for (int i = 0; i < ingredientSlots.Count; i++)
            {
                ItemSlot slot = ingredientSlots[i];
                RectTransform slotRect = slot != null ? slot.transform as RectTransform : null;
                CacheStableIngredientLayoutSize(slotRect);
            }
        }

        RectTransform createRect = createButton != null ? createButton.transform as RectTransform : null;
        CacheStableIngredientLayoutSize(createRect);
    }

    private void CacheStableIngredientLayoutSize(RectTransform child)
    {
        if (child == null || ingredientLayoutSizes.ContainsKey(child))
        {
            return;
        }

        ingredientLayoutSizes.Add(child, ResolveCurrentIngredientChildSize(child));
    }

    private Vector2 ResolveStableIngredientChildSize(RectTransform child)
    {
        if (child == null)
        {
            return Vector2.zero;
        }

        if (!ingredientLayoutSizes.TryGetValue(child, out Vector2 size) || !IsValidLayoutSize(size))
        {
            size = ResolveCurrentIngredientChildSize(child);
            ingredientLayoutSizes[child] = size;
        }

        return size;
    }

    private static Vector2 ResolveCurrentIngredientChildSize(RectTransform child)
    {
        if (child == null)
        {
            return Vector2.zero;
        }

        Vector2 size = child.rect.size;
        if (size.x <= MinimumLayoutSize || size.y <= MinimumLayoutSize)
        {
            size = child.sizeDelta;
        }

        if (size.x <= MinimumLayoutSize)
        {
            size.x = DefaultIngredientChildSize;
        }

        if (size.y <= MinimumLayoutSize)
        {
            size.y = DefaultIngredientChildSize;
        }

        return size;
    }

    private static bool IsValidLayoutSize(Vector2 size)
    {
        return size.x > MinimumLayoutSize && size.y > MinimumLayoutSize;
    }

    private static void PositionIngredientChild(
        RectTransform child,
        float distance,
        Vector2 size,
        bool applySize,
        int directionSign)
    {
        if (child == null)
        {
            return;
        }

        float anchorX = directionSign > 0 ? 0f : 1f;
        child.anchorMin = new Vector2(anchorX, 0.5f);
        child.anchorMax = new Vector2(anchorX, 0.5f);
        child.pivot = new Vector2(0.5f, 0.5f);
        if (applySize)
        {
            ApplyRectSize(child, size);
        }
        child.anchoredPosition = new Vector2(
            directionSign * (distance + size.x * 0.5f),
            0f);
    }

    private void CaptureIngredientsExpansionDirection()
    {
        Canvas.ForceUpdateCanvases();
        ingredientsExpansionDirectionSign = CalculateIngredientsExpansionDirectionSign();
    }

    private int GetIngredientsExpansionDirectionSign()
    {
        if (ingredientsExpansionDirectionSign == 0)
        {
            ingredientsExpansionDirectionSign = CalculateIngredientsExpansionDirectionSign();
        }

        return ingredientsExpansionDirectionSign;
    }

    private int CalculateIngredientsExpansionDirectionSign()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector3 slotCenter = rectTransform != null
            ? rectTransform.TransformPoint(rectTransform.rect.center)
            : transform.position;
        Vector2 slotScreenPosition = RectTransformUtility.WorldToScreenPoint(eventCamera, slotCenter);
        int desiredScreenDirection = slotScreenPosition.x <= Screen.width * 0.5f ? 1 : -1;

        RectTransform directionRoot = rectTransform != null ? rectTransform : ingredientsRoot;
        if (directionRoot == null)
        {
            return desiredScreenDirection;
        }

        Vector2 localOriginScreen = RectTransformUtility.WorldToScreenPoint(
            eventCamera,
            directionRoot.TransformPoint(Vector3.zero));
        Vector2 localRightScreen = RectTransformUtility.WorldToScreenPoint(
            eventCamera,
            directionRoot.TransformPoint(Vector3.right));
        float localRightScreenDelta = localRightScreen.x - localOriginScreen.x;
        int localRightScreenDirection = Mathf.Abs(localRightScreenDelta) > MinimumLayoutSize
            ? (localRightScreenDelta > 0f ? 1 : -1)
            : 1;
        return desiredScreenDirection * localRightScreenDirection;
    }

    private void ApplyIngredientsRootDirection(int directionSign)
    {
        if (ingredientsRoot == null)
        {
            return;
        }

        float anchorX = directionSign > 0 ? 0f : 1f;
        Vector2 anchorMin = ingredientsRoot.anchorMin;
        Vector2 anchorMax = ingredientsRoot.anchorMax;
        Vector2 pivot = ingredientsRoot.pivot;
        Vector2 anchoredPosition = ingredientsRoot.anchoredPosition;
        anchorMin.x = anchorX;
        anchorMax.x = anchorX;
        pivot.x = anchorX;
        anchoredPosition.x = directionSign * ingredientsRootHorizontalOffset;
        ingredientsRoot.anchorMin = anchorMin;
        ingredientsRoot.anchorMax = anchorMax;
        ingredientsRoot.pivot = pivot;
        ingredientsRoot.anchoredPosition = anchoredPosition;
    }

    private static void ApplyRectSize(RectTransform target, Vector2 size)
    {
        if (target == null)
        {
            return;
        }

        target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, size.x));
        target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, size.y));
    }

    private void ResetHoverTweensImmediate()
    {
        ResetHoverTweensIn(this, true);
        PrepareIngredientsManualLayout();
    }

    private bool SetCreateButtonVisible(bool visible)
    {
        if (createButton == null)
        {
            return false;
        }

        bool wasActive = createButton.gameObject.activeSelf;
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

        return wasActive != createButton.gameObject.activeSelf;
    }

    private void RefreshCraftingMapObjectState()
    {
        requiredCraftingMapObjectIds.Clear();
        blockedByCraftingMapObject = false;
        blockedByRequiredManual = false;
        requiredCraftingMapObjectId = -1;
        requiredManualItemId = -1;

        if (!HasItem || externalCreateAction != null)
        {
            RefreshCraftingMapObjectVisuals();
            return;
        }

        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (itemManager != null
            && itemManager.TryGetRequiredManualForTarget(ItemId, out ItemDefinition requiredManual))
        {
            requiredManualItemId = requiredManual.id;
            blockedByRequiredManual = !itemManager.IsManualRequirementSatisfied(ItemId);
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
            blockedByCraftingMapObject = !parentBagSlot.CanSatisfyCraftingMapObjectRequirement(ItemId);
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

        bool showMissingManual = externalCreateAction == null
                                 && HasItem
                                 && blockedByRequiredManual
                                 && requiredManualItemId >= 0;
        bool showRequiredMapObject = !showMissingManual
                                     && externalCreateAction == null
                                     && HasItem
                                     && blockedByCraftingMapObject
                                     && requiredCraftingMapObjectId >= 0;
        bool showRequirementIcon = showMissingManual || showRequiredMapObject;
        bool showBlockedState = showMissingManual
                                || (showRequiredMapObject && blockedByCraftingMapObject);
        try
        {
            SetCreateButtonHoverEnabled(!showBlockedState);

            if (canNotImage != null)
            {
                canNotImage.enabled = showBlockedState;
            }

            if (createIcon != null)
            {
                createIcon.enabled = HasItem && !showRequirementIcon;
            }

            if (mapObjectIcon != null)
            {
                if (showRequirementIcon)
                {
                    int requiredItemId = showMissingManual
                        ? requiredManualItemId
                        : requiredCraftingMapObjectId;
                    mapObjectIcon.sprite = ResolveRequiredItemIcon(requiredItemId);
                    mapObjectIcon.enabled = mapObjectIcon.sprite != null;
                }
                else
                {
                    mapObjectIcon.sprite = null;
                    mapObjectIcon.enabled = false;
                }
            }

            if (showBlockedState)
            {
                ResetRequiredItemTransform();
            }
        }
        finally
        {
            isRefreshingCraftingMapObjectVisuals = false;
        }
    }

    private Sprite ResolveRequiredItemIcon(int itemId)
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

    private RectTransform ResolveSerializedIngredientsRoot()
    {
        if (ingredientSlots == null || ingredientSlots.Count <= 0)
        {
            return null;
        }

        Transform sharedParent = null;
        for (int i = 0; i < ingredientSlots.Count; i++)
        {
            ItemSlot slot = ingredientSlots[i];
            if (slot == null || slot.transform == null || slot.transform.parent == null)
            {
                continue;
            }

            Transform candidateParent = slot.transform.parent;
            if (sharedParent == null)
            {
                sharedParent = candidateParent;
                continue;
            }

            if (sharedParent != candidateParent)
            {
                return null;
            }
        }

        return sharedParent as RectTransform;
    }

    private void SetCreateButtonHoverEnabled(bool enabled)
    {
        SetHoverTweensEnabledIn(createButton, enabled, !enabled, false);
    }

    private void SetTargetButtonHoverEnabled(bool enabled)
    {
        CacheReferences();
        HUDButtonHoverTween hoverTween = button != null
            ? button.GetComponent<HUDButtonHoverTween>()
            : null;
        if (hoverTween == null)
        {
            return;
        }

        if (!enabled)
        {
            hoverTween.ResetHoverImmediate(false);
        }

        hoverTween.enabled = enabled;
    }

    private void ResetCreateButtonHoverImmediate(bool rebuildLayout)
    {
        ResetHoverTweensIn(createButton, rebuildLayout);
    }

    private void ResetRequiredItemTransform()
    {
        if (createButton != null && createButton.transform is RectTransform createRect)
        {
            createRect.DOKill();
            createRect.localScale = Vector3.one;
            ApplyRectSize(createRect, ResolveStableIngredientChildSize(createRect));
        }

        if (mapObjectIcon != null && mapObjectIcon.transform is RectTransform mapObjectRect)
        {
            mapObjectRect.DOKill();
            mapObjectRect.localScale = Vector3.one;
            mapObjectRect.anchoredPosition = Vector2.zero;
        }
    }

    private void DisableIngredientSlotHoverTweens()
    {
        if (ingredientSlots == null)
        {
            return;
        }

        for (int i = 0; i < ingredientSlots.Count; i++)
        {
            ItemSlot slot = ingredientSlots[i];
            if (slot == null)
            {
                continue;
            }

            SetHoverTweensEnabledIn(slot, false, true, false);
        }
    }

    private void ResetHoverTweensIn(Component root, bool rebuildLayout)
    {
        ApplyHoverTweensIn(root, null, true, rebuildLayout);
    }

    private void SetHoverTweensEnabledIn(Component root, bool enabled, bool reset, bool rebuildLayout)
    {
        ApplyHoverTweensIn(root, enabled, reset, rebuildLayout);
    }

    private void ApplyHoverTweensIn(Component root, bool? enabled, bool reset, bool rebuildLayout)
    {
        if (root == null)
        {
            return;
        }

        hoverTweenBuffer.Clear();
        root.GetComponentsInChildren(true, hoverTweenBuffer);
        for (int i = 0; i < hoverTweenBuffer.Count; i++)
        {
            HUDButtonHoverTween hoverTween = hoverTweenBuffer[i];
            if (hoverTween == null)
            {
                continue;
            }

            if (reset)
            {
                hoverTween.ResetHoverImmediate(rebuildLayout);
            }

            if (enabled.HasValue)
            {
                hoverTween.enabled = enabled.Value;
            }
        }

        hoverTweenBuffer.Clear();
    }
}
