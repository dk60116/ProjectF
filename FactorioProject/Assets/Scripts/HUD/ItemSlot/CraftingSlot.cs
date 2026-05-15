using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class CraftingSlot : ItemSlot
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
    private Image createIcon, mapObjectIcon;

    private bool ingredientsVisible;
    private bool slotVisualHidden;
    private bool blockedByCraftingMapObject;
    private bool isCachingReferences;
    private bool isRefreshingCraftingMapObjectVisuals;
    private bool isHidingIngredientsImmediate;
    private bool ingredientsManualLayoutReady;
    private bool ingredientRevealAnimating;
    private Sequence ingredientRevealSequence;
    private float ingredientsSpacing = DefaultIngredientSpacing;
    private int requiredCraftingMapObjectId = -1;
    private readonly List<CraftingTreeRuntime.IngredientEntry> ingredientBuffer = new List<CraftingTreeRuntime.IngredientEntry>();
    private readonly List<int> requiredCraftingMapObjectIds = new List<int>();
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
        HideIngredientsImmediate();
    }

    public override void SetItem(int itemId, int itemCount, int maxItemCount = 0)
    {
        base.SetItem(itemId, itemCount, maxItemCount);
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
                Transform target = transform.Find("Ingredients");
                ingredientsRoot = target as RectTransform;
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
            RefreshIngredients();
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
        ingredientBuffer.Clear();
        if (!CraftingTreeRuntime.TryGetIngredients(ItemId, ingredientBuffer))
        {
            HideIngredientsImmediate();
            return;
        }

        if (ingredientSlots == null)
        {
            return;
        }

        bool hasAllIngredients = true;
        for (int i = 0; i < ingredientSlots.Count; i++)
        {
            ItemSlot slot = ingredientSlots[i];
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
        ApplyIngredientsManualLayout(ingredientBuffer.Count);
        SetIngredientSlotScalesImmediate(ingredientBuffer.Count);
        ApplyIngredientRevealState(ingredientBuffer.Count, revealSequentially, preserveRunningReveal);
        ForceIngredientsLayoutImmediate();
        ingredientsVisible = true;
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
            SetRevealCanvasGroup(createButton.gameObject, 1f, true);
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

        if (createButton != null && createButton.gameObject.activeSelf)
        {
            bool restoreCreateInteractable = createButton.interactable;
            createButton.interactable = false;
            AppendRevealTarget(
                sequence,
                createButton.gameObject,
                ref hasRevealTarget,
                () => createButton.interactable = restoreCreateInteractable);
        }

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

    private void AppendRevealTarget(Sequence sequence, GameObject target, ref bool hasPreviousTarget, TweenCallback onComplete = null)
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
                SetRevealCanvasGroup(revealGroup, 1f, true);
                onComplete?.Invoke();
            });
            return;
        }

        sequence.Append(
            revealGroup.DOFade(1f, duration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    SetRevealCanvasGroup(revealGroup, 1f, true);
                    onComplete?.Invoke();
                }));
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

        ingredientsManualLayoutReady = true;
    }

    private void ApplyIngredientsManualLayout(int visibleIngredientCount)
    {
        if (ingredientsRoot == null)
        {
            return;
        }

        PrepareIngredientsManualLayout();

        float nextX = 0f;
        float maxHeight = 0f;
        int safeVisibleCount = Mathf.Max(0, visibleIngredientCount);

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
                PositionIngredientChild(slotRect, nextX, childSize, true);
                nextX += childSize.x + ingredientsSpacing;
                maxHeight = Mathf.Max(maxHeight, childSize.y);
            }
        }

        RectTransform createRect = createButton != null ? createButton.transform as RectTransform : null;
        if (createRect != null && createButton.gameObject.activeSelf)
        {
            Vector2 childSize = ResolveStableIngredientChildSize(createRect);
            PositionIngredientChild(createRect, nextX, childSize, false);
            nextX += childSize.x + ingredientsSpacing;
            maxHeight = Mathf.Max(maxHeight, childSize.y);
        }

        float width = Mathf.Max(0f, nextX - ingredientsSpacing);
        ingredientsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        ingredientsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, maxHeight);
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

    private static void PositionIngredientChild(RectTransform child, float left, Vector2 size, bool applySize)
    {
        if (child == null)
        {
            return;
        }

        child.anchorMin = new Vector2(0f, 0.5f);
        child.anchorMax = new Vector2(0f, 0.5f);
        child.pivot = new Vector2(0.5f, 0.5f);
        if (applySize)
        {
            ApplyRectSize(child, size);
        }
        child.anchoredPosition = new Vector2(left + size.x * 0.5f, 0f);
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
        HUDButtonHoverTween[] hoverTweens = GetComponentsInChildren<HUDButtonHoverTween>(true);
        for (int i = 0; i < hoverTweens.Length; i++)
        {
            if (hoverTweens[i] != null)
            {
                hoverTweens[i].ResetHoverImmediate();
            }
        }

        PrepareIngredientsManualLayout();
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
            SetCreateButtonHoverEnabled(!showBlockedState);

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

            if (showBlockedState)
            {
                ResetRequiredMapObjectTransform();
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

    private void SetCreateButtonHoverEnabled(bool enabled)
    {
        if (createButton == null)
        {
            return;
        }

        HUDButtonHoverTween[] hoverTweens = createButton.GetComponentsInChildren<HUDButtonHoverTween>(true);
        for (int i = 0; i < hoverTweens.Length; i++)
        {
            HUDButtonHoverTween hoverTween = hoverTweens[i];
            if (hoverTween == null)
            {
                continue;
            }

            if (!enabled)
            {
                hoverTween.ResetHoverImmediate(false);
            }

            hoverTween.enabled = enabled;
        }
    }

    private void ResetRequiredMapObjectTransform()
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

            HUDButtonHoverTween[] hoverTweens = slot.GetComponentsInChildren<HUDButtonHoverTween>(true);
            for (int tweenIndex = 0; tweenIndex < hoverTweens.Length; tweenIndex++)
            {
                HUDButtonHoverTween hoverTween = hoverTweens[tweenIndex];
                if (hoverTween == null)
                {
                    continue;
                }

                hoverTween.ResetHoverImmediate(false);
                hoverTween.enabled = false;
            }
        }
    }
}
