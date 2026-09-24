using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WorkableCraftingPanel : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.5f;
    private static readonly Color PanelColor = new Color(0.10f, 0.075f, 0.045f, 0.97f);
    private static readonly Color SectionColor = new Color(0.18f, 0.13f, 0.075f, 0.96f);
    private static readonly Color RowColor = new Color(0.27f, 0.19f, 0.10f, 1f);
    private static readonly Color UnavailableRowColor = new Color(0.16f, 0.13f, 0.10f, 1f);
    private static readonly Color SelectedRowColor = new Color(0.52f, 0.35f, 0.12f, 1f);
    private static readonly Color ButtonColor = new Color(0.55f, 0.32f, 0.10f, 1f);
    private static readonly Color TextColor = new Color(1f, 0.91f, 0.70f, 1f);

    private readonly List<int> recipeItemIds = new List<int>(32);
    private readonly List<int> recipeItemIdScratch = new List<int>(32);
    private readonly List<RecipeRow> recipeRows = new List<RecipeRow>(32);
    private readonly List<WorkableObject> activeWorkables = new List<WorkableObject>(8);
    private readonly List<int> activeWorkableItemIds = new List<int>(8);
    private readonly List<int> workableItemIdScratch = new List<int>(8);
    private readonly HashSet<int> recipeItemIdSet = new HashSet<int>();
    private readonly CraftingPlanBuilder craftingPlanBuilder = new CraftingPlanBuilder();
    private readonly Dictionary<int, bool> craftingAccessCache = new Dictionary<int, bool>();

    private PlayerHUD owner;
    private WorkableObject target;
    private RectTransform content;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI detailNameText;
    private TextMeshProUGUI detailMetaText;
    private TextMeshProUGUI emptyText;
    private TextMeshProUGUI statusText;
    private Button craftButton;
    private Image craftButtonIcon;
    private CraftingSlot actionSlot;
    private TMP_FontAsset font;
    private int selectedItemId = -1;
    private int displayedItemId = -1;
    private float refreshTimer;

    public bool IsOpen => gameObject.activeSelf;
    public WorkableObject Target => target;

    public static WorkableCraftingPanel Create(PlayerHUD ownerHud)
    {
        if (ownerHud == null)
        {
            return null;
        }

        CraftingSlot template = FindCraftingSlotTemplate(ownerHud.transform);
        GameObject root = CreateUiObject("WorkableCraftingPanel", ownerHud.transform);
        WorkableCraftingPanel panel = root.AddComponent<WorkableCraftingPanel>();
        panel.owner = ownerHud;
        panel.font = ResolveFont(ownerHud.transform);
        panel.BuildUi(template);
        root.SetActive(false);
        return panel;
    }

    public void Toggle(WorkableObject workableObject)
    {
        if (workableObject == null)
        {
            Close();
            return;
        }

        if (IsOpen && target == workableObject)
        {
            Close();
            return;
        }

        Open(workableObject);
    }

    public void Open(WorkableObject workableObject)
    {
        if (workableObject == null)
        {
            return;
        }

        target = workableObject;
        selectedItemId = -1;
        displayedItemId = -1;
        refreshTimer = 0f;

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        transform.SetAsLastSibling();
        if (!RefreshWorkableContext(true))
        {
            Close();
            return;
        }

        RefreshPanel();
    }

    public void Close()
    {
        target = null;
        activeWorkables.Clear();
        activeWorkableItemIds.Clear();
        selectedItemId = -1;
        displayedItemId = -1;
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!IsCraftingContextUsable())
        {
            Close();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f)
        {
            return;
        }

        refreshTimer = RefreshIntervalSeconds;
        if (!RefreshWorkableContext(false))
        {
            Close();
            return;
        }

        RefreshPanel();
    }

    private bool IsCraftingContextUsable()
    {
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || activeWorkables.Count == 0)
        {
            return false;
        }

        Vector3 origin = player.BodyTransform != null
            ? player.BodyTransform.position
            : player.transform.position;
        for (int i = 0; i < activeWorkables.Count; i++)
        {
            WorkableObject workable = activeWorkables[i];
            if (workable != null
                && workable.isActiveAndEnabled
                && workable.IsTargetActive
                && workable.ContainsWorldPositionInConnectedWorkableRange(origin))
            {
                return true;
            }
        }

        return false;
    }

    private bool RefreshWorkableContext(bool forceRebuild)
    {
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            activeWorkables.Clear();
            activeWorkableItemIds.Clear();
            return false;
        }

        Vector3 origin = player.BodyTransform != null
            ? player.BodyTransform.position
            : player.transform.position;
        WorkableObject.CollectActiveContainingWorldPosition(origin, activeWorkables);

        workableItemIdScratch.Clear();
        for (int i = activeWorkables.Count - 1; i >= 0; i--)
        {
            WorkableObject workable = activeWorkables[i];
            if (workable == null
                || !workable.isActiveAndEnabled
                || !workable.IsTargetActive)
            {
                activeWorkables.RemoveAt(i);
                continue;
            }

            int itemId = workable.ResolveItemId();
            if (itemId >= 0 && !workableItemIdScratch.Contains(itemId))
            {
                workableItemIdScratch.Add(itemId);
            }
        }

        workableItemIdScratch.Sort();
        bool recipesChanged = forceRebuild
                              || !HaveSameItemIds(activeWorkableItemIds, workableItemIdScratch);
        if (recipesChanged)
        {
            activeWorkableItemIds.Clear();
            activeWorkableItemIds.AddRange(workableItemIdScratch);
            RebuildRecipeRows();
        }

        return activeWorkables.Count > 0 && activeWorkableItemIds.Count > 0;
    }

    private static bool HaveSameItemIds(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private void BuildUi(CraftingSlot template)
    {
        RectTransform rootRect = transform as RectTransform;
        SetStretch(rootRect, Vector2.zero, Vector2.zero);

        GameObject backdropObject = CreateUiObject("Backdrop", transform);
        SetStretch(backdropObject.transform as RectTransform, Vector2.zero, Vector2.zero);
        Image dimmer = backdropObject.AddComponent<Image>();
        dimmer.color = new Color(0f, 0f, 0f, 0.55f);
        dimmer.raycastTarget = true;
        Button backdropButton = backdropObject.AddComponent<Button>();
        backdropButton.targetGraphic = dimmer;
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        GameObject frameObject = CreateUiObject("Frame", transform);
        RectTransform frameRect = frameObject.transform as RectTransform;
        frameRect.anchorMin = new Vector2(0.5f, 0.5f);
        frameRect.anchorMax = new Vector2(0.5f, 0.5f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.sizeDelta = new Vector2(1040f, 650f);
        frameObject.AddComponent<Image>().color = PanelColor;

        titleText = CreateText("Title", frameObject.transform, 32f, TextAlignmentOptions.Left);
        SetAnchored(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(32f, -70f), new Vector2(-105f, -18f));

        Button closeButton = CreateTextButton("CloseButton", frameObject.transform, "×", 34f);
        SetAnchored(closeButton.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-76f, -64f), new Vector2(-18f, -18f));
        closeButton.onClick.AddListener(Close);

        GameObject listSection = CreateUiObject("RecipeList", frameObject.transform);
        RectTransform listSectionRect = listSection.transform as RectTransform;
        SetAnchored(listSectionRect, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(28f, 28f), new Vector2(382f, -86f));
        listSection.AddComponent<Image>().color = SectionColor;
        BuildScrollView(listSection.transform);

        GameObject detailSection = CreateUiObject("ItemDetail", frameObject.transform);
        RectTransform detailSectionRect = detailSection.transform as RectTransform;
        SetAnchored(detailSectionRect, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(402f, 28f), new Vector2(-28f, -86f));
        detailSection.AddComponent<Image>().color = SectionColor;

        detailNameText = CreateText("ItemName", detailSection.transform, 30f, TextAlignmentOptions.Left);
        SetAnchored(detailNameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(28f, -62f), new Vector2(-28f, -18f));

        detailMetaText = CreateText("ItemMeta", detailSection.transform, 20f, TextAlignmentOptions.TopLeft);
        SetAnchored(detailMetaText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(28f, -124f), new Vector2(-28f, -70f));

        emptyText = CreateText("Empty", detailSection.transform, 24f, TextAlignmentOptions.Center);
        SetAnchored(emptyText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(30f, 100f), new Vector2(-30f, -100f));

        GameObject actionRoot = CreateUiObject("CraftingAction", detailSection.transform);
        RectTransform actionRootRect = actionRoot.transform as RectTransform;
        SetAnchored(actionRootRect, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(28f, 105f), new Vector2(-28f, -140f));

        if (template != null)
        {
            actionSlot = Instantiate(template, actionRoot.transform);
            actionSlot.name = "SelectedItemCraftingSlot";
            RectTransform slotRect = actionSlot.transform as RectTransform;
            slotRect.anchorMin = new Vector2(0f, 0.5f);
            slotRect.anchorMax = new Vector2(0f, 0.5f);
            slotRect.pivot = new Vector2(0.5f, 0.5f);
            actionSlot.ConfigureExternalQueuedCreateAction(
                HandleExternalCreate,
                CanQueueItem);
            actionSlot.SetExternalCreateButtonVisible(false);
            actionSlot.SetIngredientToggleEnabled(false);
            actionSlot.Show(Vector2.zero, new Vector2(52f, 8f));
        }

        statusText = CreateText("Status", detailSection.transform, 18f, TextAlignmentOptions.BottomLeft);
        SetAnchored(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(28f, 24f), new Vector2(-112f, 88f));

        Sprite craftIcon = template != null ? template.CreateActionIcon : null;
        craftButton = CreateIconButton("CraftButton", detailSection.transform, craftIcon, out craftButtonIcon);
        SetAnchored(craftButton.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-88f, 24f), new Vector2(-24f, 88f));
        craftButton.onClick.AddListener(HandleCraftClicked);
    }

    private void BuildScrollView(Transform parent)
    {
        GameObject scrollObject = CreateUiObject("ScrollView", parent);
        RectTransform scrollRectTransform = scrollObject.transform as RectTransform;
        SetAnchored(scrollRectTransform, Vector2.zero, Vector2.one,
            new Vector2(12f, 12f), new Vector2(-12f, -12f));
        ScrollRect scrollRect = scrollObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        GameObject viewportObject = CreateUiObject("Viewport", scrollObject.transform);
        RectTransform viewportRect = viewportObject.transform as RectTransform;
        SetStretch(viewportRect, Vector2.zero, Vector2.zero);
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentObject = CreateUiObject("Content", viewportObject.transform);
        content = contentObject.transform as RectTransform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        GridLayoutGroup layout = contentObject.AddComponent<GridLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = new Vector2(6f, 6f);
        layout.cellSize = new Vector2(58f, 58f);
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 5;
        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = content;
    }

    private void RebuildRecipeRows()
    {
        for (int i = 0; i < recipeRows.Count; i++)
        {
            if (recipeRows[i].Root != null)
            {
                recipeRows[i].Root.SetActive(false);
                Destroy(recipeRows[i].Root);
            }
        }

        recipeRows.Clear();
        recipeItemIds.Clear();
        recipeItemIdSet.Clear();
        for (int workableIndex = 0; workableIndex < activeWorkableItemIds.Count; workableIndex++)
        {
            recipeItemIdScratch.Clear();
            CraftingTreeRuntime.TryGetCraftableItemIdsForMapObject(
                activeWorkableItemIds[workableIndex],
                recipeItemIdScratch);
            for (int recipeIndex = 0; recipeIndex < recipeItemIdScratch.Count; recipeIndex++)
            {
                int recipeItemId = recipeItemIdScratch[recipeIndex];
                if (recipeItemIdSet.Add(recipeItemId))
                {
                    recipeItemIds.Add(recipeItemId);
                }
            }
        }

        recipeItemIds.Sort();
        for (int i = 0; i < recipeItemIds.Count; i++)
        {
            CreateRecipeRow(recipeItemIds[i]);
        }

        if (selectedItemId >= 0 && !recipeItemIdSet.Contains(selectedItemId))
        {
            selectedItemId = -1;
            displayedItemId = -1;
            actionSlot?.HideImmediate();
        }
    }

    private void CreateRecipeRow(int itemId)
    {
        ItemDefinition definition = ResolveItemDefinition(itemId);
        GameObject rowObject = CreateUiObject($"Recipe_{itemId}", content);
        Image background = rowObject.AddComponent<Image>();
        background.color = RowColor;
        Button button = rowObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.90f, 0.65f, 1f);
        colors.pressedColor = new Color(0.82f, 0.68f, 0.42f, 1f);
        colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.75f);
        button.colors = colors;
        rowObject.AddComponent<HUDButtonHoverTween>();
        GameObject iconObject = CreateUiObject("Icon", rowObject.transform);
        RectTransform iconRect = iconObject.transform as RectTransform;
        SetStretch(iconRect, new Vector2(7f, 7f), new Vector2(-7f, -7f));
        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = definition != null ? definition.icon : null;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        int capturedItemId = itemId;
        button.onClick.AddListener(() => SelectItem(capturedItemId));
        recipeRows.Add(new RecipeRow(itemId, rowObject, button, background, icon));
    }

    private void RefreshPanel()
    {
        if (activeWorkableItemIds.Count == 1)
        {
            int workableItemId = activeWorkableItemIds[0];
            ItemDefinition definition = ResolveItemDefinition(workableItemId);
            titleText.text = $"{ResolveItemName(workableItemId, definition)} Crafting";
        }
        else
        {
            titleText.text = "Combined Crafting";
        }

        actionSlot?.BeginExternalAvailabilityScan();
        try
        {
            craftingAccessCache.Clear();
            for (int i = 0; i < recipeRows.Count; i++)
            {
                RecipeRow row = recipeRows[i];
                bool available = CanBuildCraftingPlan(row.ItemId);
                row.Available = available;
                row.Button.interactable = true;
                row.Background.color = ResolveRecipeRowColor(row);
                Color iconColor = row.Icon.color;
                iconColor.a = available ? 1f : 0.35f;
                row.Icon.color = iconColor;
            }

            RefreshSelectedItem();
        }
        finally
        {
            actionSlot?.EndExternalAvailabilityScan();
        }
    }

    private void SelectItem(int itemId)
    {
        selectedItemId = itemId;
        for (int i = 0; i < recipeRows.Count; i++)
        {
            RecipeRow row = recipeRows[i];
            row.Background.color = ResolveRecipeRowColor(row);
        }

        RefreshSelectedItem();
    }

    private void RefreshSelectedItem()
    {
        bool hasSelection = selectedItemId >= 0 && actionSlot != null;
        emptyText.gameObject.SetActive(!hasSelection);
        detailNameText.gameObject.SetActive(hasSelection);
        detailMetaText.gameObject.SetActive(hasSelection);
        craftButton.gameObject.SetActive(hasSelection);
        statusText.gameObject.SetActive(hasSelection);

        if (!hasSelection)
        {
            emptyText.text = recipeRows.Count == 0
                ? "No items can be crafted at this station"
                : "Select an item";
            if (actionSlot != null)
            {
                actionSlot.HideImmediate();
            }
            displayedItemId = -1;
            return;
        }

        ItemDefinition definition = ResolveItemDefinition(selectedItemId);
        detailNameText.text = ResolveItemName(selectedItemId, definition);
        int outputCount = CraftingTreeRuntime.GetOutputCount(selectedItemId);
        float duration = definition != null ? definition.CraftingDurationSeconds : 5f;
        detailMetaText.text = $"Output  {outputCount}\nCrafting time  {duration:0.#}s";

        if (displayedItemId != selectedItemId)
        {
            actionSlot.SetItem(selectedItemId, 1, 0);
            actionSlot.ShowImmediate(new Vector2(52f, 8f));
            actionSlot.ShowIngredientsForExternalUse();
            displayedItemId = selectedItemId;
        }
        else
        {
            actionSlot.RefreshCraftingAvailabilityVisuals();
        }
        actionSlot.SetExternalCreateButtonVisible(false);

        bool canCraft = CanBuildCraftingPlan(selectedItemId);
        craftButton.interactable = canCraft;
        if (craftButtonIcon != null)
        {
            Color iconColor = craftButtonIcon.color;
            iconColor.a = canCraft ? 1f : 0.35f;
            craftButtonIcon.color = iconColor;
        }
        statusText.text = canCraft
            ? "Ready to craft"
            : ResolveUnavailableMessage();
    }

    private Color ResolveRecipeRowColor(RecipeRow row)
    {
        if (row.ItemId == selectedItemId)
        {
            return SelectedRowColor;
        }

        return row.Available ? RowColor : UnavailableRowColor;
    }

    private string ResolveUnavailableMessage()
    {
        if (owner == null || owner.AvailableCraftingQueueSlots <= 0)
        {
            return "Crafting queue is full";
        }

        return "Not enough materials or crafting access";
    }

    private void HandleCraftClicked()
    {
        if (selectedItemId < 0 || actionSlot == null)
        {
            return;
        }

        actionSlot.BeginExternalAvailabilityScan();
        try
        {
            craftingAccessCache.Clear();
            bool planBuilt = CanBuildCraftingPlan(selectedItemId);
            if (planBuilt
                && actionSlot.TryConsumeExternalIngredients(
                    craftingPlanBuilder.ConsumedItems,
                    out List<CraftingTreeRuntime.IngredientEntry> consumedIngredients)
                && !owner.TryEnqueueCraftingPlan(craftingPlanBuilder.Steps, consumedIngredients))
            {
                actionSlot.RefundExternalIngredients(consumedIngredients);
            }
        }
        finally
        {
            actionSlot.EndExternalAvailabilityScan();
        }

        refreshTimer = 0f;
        RefreshPanel();
    }

    private bool CanBuildCraftingPlan(int itemId)
    {
        return itemId >= 0
               && actionSlot != null
               && owner != null
               && IsCraftingContextUsable()
               && craftingPlanBuilder.TryBuild(
                   itemId,
                   owner.AvailableCraftingQueueSlots,
                   actionSlot.GetOwnedIngredientCountForExternalUse,
                   CanPlanCraftingItemCached);
    }

    private bool CanPlanCraftingItemCached(int itemId)
    {
        if (!craftingAccessCache.TryGetValue(itemId, out bool canCraft))
        {
            canCraft = owner != null && owner.CanPlanCraftingItem(itemId);
            craftingAccessCache[itemId] = canCraft;
        }

        return canCraft;
    }

    private bool HandleExternalCreate(
        int itemId,
        List<CraftingTreeRuntime.IngredientEntry> consumedIngredients)
    {
        return IsCraftingContextUsable()
               && owner != null
               && owner.TryEnqueueCrafting(itemId, consumedIngredients);
    }

    private bool CanQueueItem(int itemId)
    {
        return IsCraftingContextUsable()
               && owner != null
               && owner.CanEnqueueCrafting(itemId);
    }

    private ItemDefinition ResolveItemDefinition(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        IReadOnlyList<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
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

    private static string ResolveItemName(int itemId, ItemDefinition definition)
    {
        if (definition != null && !string.IsNullOrWhiteSpace(definition.itemName))
        {
            return definition.itemName;
        }

        return $"Item {itemId}";
    }

    private TextMeshProUGUI CreateText(
        string objectName,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateUiObject(objectName, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = TextColor;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateTextButton(string objectName, Transform parent, string label, float fontSize)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = ButtonColor;
        Button button = buttonObject.AddComponent<Button>();
        ConfigureActionButton(button, background);

        TextMeshProUGUI text = CreateText("Label", buttonObject.transform, fontSize, TextAlignmentOptions.Center);
        text.text = label;
        SetStretch(text.rectTransform, new Vector2(8f, 4f), new Vector2(-8f, -4f));
        return button;
    }

    private static Button CreateIconButton(
        string objectName,
        Transform parent,
        Sprite sprite,
        out Image icon)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = ButtonColor;
        Button button = buttonObject.AddComponent<Button>();
        ConfigureActionButton(button, background);

        GameObject iconObject = CreateUiObject("Icon", buttonObject.transform);
        icon = iconObject.AddComponent<Image>();
        icon.sprite = sprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        SetStretch(icon.rectTransform, new Vector2(12f, 12f), new Vector2(-12f, -12f));
        return button;
    }

    private static void ConfigureActionButton(Button button, Image background)
    {
        if (button == null || background == null)
        {
            return;
        }

        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.88f, 0.60f, 1f);
        colors.pressedColor = new Color(0.75f, 0.58f, 0.32f, 1f);
        colors.disabledColor = new Color(0.34f, 0.34f, 0.34f, 0.65f);
        button.colors = colors;
    }

    private static CraftingSlot FindCraftingSlotTemplate(Transform root)
    {
        CraftingSlot[] slots = root != null ? root.GetComponentsInChildren<CraftingSlot>(true) : null;
        return slots != null && slots.Length > 0 ? slots[0] : null;
    }

    private static TMP_FontAsset ResolveFont(Transform root)
    {
        TextMeshProUGUI reference = root != null ? root.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        return reference != null && reference.font != null ? reference.font : TMP_Settings.defaultFontAsset;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.layer = 5;
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void SetStretch(RectTransform rectTransform, Vector2 minimumOffset, Vector2 maximumOffset)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = minimumOffset;
        rectTransform.offsetMax = maximumOffset;
    }

    private static void SetAnchored(
        RectTransform rectTransform,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
    }

    private sealed class RecipeRow
    {
        public readonly int ItemId;
        public readonly GameObject Root;
        public readonly Button Button;
        public readonly Image Background;
        public readonly Image Icon;
        public bool Available;

        public RecipeRow(int itemId, GameObject root, Button button, Image background, Image icon)
        {
            ItemId = itemId;
            Root = root;
            Button = button;
            Background = background;
            Icon = icon;
        }
    }
}
