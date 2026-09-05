using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class FilterSelectUI : MonoBehaviour
{
    [SerializeField]
    private List<ItemFilterSlot> slotList;

    [SerializeField]
    private Button allBtuuon, noneButton;

    [SerializeField]
    private Sprite loggingGrowthHandleSprite;

    private readonly List<ItemDefinition> visibleDefinitions = new List<ItemDefinition>();
    private readonly List<ResourceDefinition> visibleTreeDefinitions = new List<ResourceDefinition>();
    private MapObject boundTarget;
    private TerrainGenerator cachedTerrainGenerator;
    private GameObject loggingGrowthControl;
    private Slider loggingGrowthSlider;
    private TextMeshProUGUI loggingGrowthLabel;
    private TextMeshProUGUI loggingGrowthValueLabel;
    private bool bulkButtonLayoutCached;
    private Vector2 originalAllButtonPosition;
    private Vector2 originalAllButtonSize;
    private Vector2 originalNoneButtonPosition;
    private Vector2 originalNoneButtonSize;

    private void Awake()
    {
        EnsureSlotList();
        ResolveButtons();
        BindButtons();
        EnsureLoggingGrowthControl();
        HideEmptySlots();
    }

    private void OnEnable()
    {
        ResolveButtons();
        BindButtons();
        EnsureLoggingGrowthControl();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindButtons();
    }

    public void Bind(Player player)
    {
        boundTarget = ResolveFocusedFilterTarget();
        Refresh();
    }

    public void Bind(MapObject target)
    {
        boundTarget = target;
        Refresh();
    }

    public void Refresh()
    {
        EnsureSlotList();
        boundTarget = ResolveCurrentTarget();
        ApplyBulkButtonVisibility();
        BuildVisibleDefinitions();
        ApplyDefinitionsToSlots();
        RefreshSplitterControls();
    }

    public bool TryGetBoundTarget(out MapObject target)
    {
        target = ResolveCurrentTarget();
        return target != null;
    }

    [ContextMenu("SetSlotLit")]
    private void SetSlotList()
    {
        ItemFilterSlot[] list = GetComponentsInChildren<ItemFilterSlot>(true);
        slotList = list.ToList();
    }

    private void EnsureSlotList()
    {
        if (slotList == null || slotList.Count == 0)
        {
            SetSlotList();
        }
    }

    private void ResolveButtons()
    {
        if (allBtuuon == null)
        {
            allBtuuon = FindButtonByNames("AllButton", "All");
        }

        if (noneButton == null)
        {
            noneButton = FindButtonByNames("NoneButton", "None");
        }
    }

    private void BindButtons()
    {
        if (allBtuuon != null)
        {
            allBtuuon.onClick.RemoveListener(HandleAllButtonClicked);
            allBtuuon.onClick.AddListener(HandleAllButtonClicked);
        }

        if (noneButton != null)
        {
            noneButton.onClick.RemoveListener(HandleNoneButtonClicked);
            noneButton.onClick.AddListener(HandleNoneButtonClicked);
        }
    }

    private void UnbindButtons()
    {
        if (allBtuuon != null)
        {
            allBtuuon.onClick.RemoveListener(HandleAllButtonClicked);
        }

        if (noneButton != null)
        {
            noneButton.onClick.RemoveListener(HandleNoneButtonClicked);
        }
    }

    private void ApplyBulkButtonVisibility()
    {
        bool isProductionTargetFilter = TryResolveProductionMachine(boundTarget, out _);
        if (allBtuuon != null && allBtuuon.gameObject.activeSelf == isProductionTargetFilter)
        {
            allBtuuon.gameObject.SetActive(!isProductionTargetFilter);
        }

        if (noneButton != null && !noneButton.gameObject.activeSelf)
        {
            noneButton.gameObject.SetActive(true);
        }
    }

    private void BuildVisibleDefinitions()
    {
        visibleDefinitions.Clear();
        visibleTreeDefinitions.Clear();

        if (boundTarget is LoggingMachine)
        {
            ResolveTerrainGenerator()?.CollectLoggingTreeDefinitions(visibleTreeDefinitions);
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return;
        }

        if (TryBuildProductionTargetFilter(boundTarget, definitions, visibleDefinitions))
        {
            return;
        }

        HashSet<int> allowedItemIds = new HashSet<int>();
        HashSet<ItemDefinition.EnergyType> allowedEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
        bool restrictToAreaItems = TryBuildAreaRestrictedFilter(boundTarget, allowedItemIds, allowedEnergyTypes);
        bool excludeIgnoredDefinitions = boundTarget is BoxObject;

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null
                || definition.id < 0
                || (excludeIgnoredDefinitions && definition.ignoreFilter))
            {
                continue;
            }

            if (restrictToAreaItems && !IsDefinitionAllowedForArea(definition, allowedItemIds, allowedEnergyTypes))
            {
                continue;
            }

            visibleDefinitions.Add(definition);
        }

        visibleDefinitions.Sort((left, right) => left.id.CompareTo(right.id));
    }

    private void ApplyDefinitionsToSlots()
    {
        if (slotList == null)
        {
            return;
        }

        if (boundTarget is LoggingMachine loggingMachine)
        {
            ApplyLoggingDefinitionsToSlots(loggingMachine);
            ApplyLoggingHeaderLayout(true);
            RefreshLoggingGrowthControl(loggingMachine);
            return;
        }

        ApplyLoggingHeaderLayout(boundTarget is Spliterbelt);
        SetLoggingGrowthControlVisible(false);

        int filterBitCount = GetFilterBitCount();
        bool isProductionTargetFilter = TryResolveProductionMachine(boundTarget, out ProductionMachine productionMachine);

        for (int i = 0; i < slotList.Count; i++)
        {
            ItemFilterSlot slot = slotList[i];
            if (slot == null)
            {
                continue;
            }

            if (i < visibleDefinitions.Count)
            {
                ItemDefinition definition = visibleDefinitions[i];
                if (!slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(true);
                }

                bool isChecked = isProductionTargetFilter
                    ? productionMachine.IsProductionTargetSelected(definition.id)
                    : boundTarget == null || boundTarget.IsItemFilterEnabled(definition.id, filterBitCount);
                int itemId = definition.id;
                bool isInteractable = !isProductionTargetFilter
                    || productionMachine.CanSelectProductionTarget(itemId);
                slot.SetFilterItem(
                    itemId,
                    isChecked,
                    isInteractable,
                    isOn => HandleSlotToggleChanged(itemId, isOn));
            }
            else
            {
                slot.ClearFilterItem();
                if (slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(false);
                }
            }
        }
    }

    private void ApplyLoggingDefinitionsToSlots(LoggingMachine loggingMachine)
    {
        for (int i = 0; i < slotList.Count; i++)
        {
            ItemFilterSlot slot = slotList[i];
            if (slot == null)
            {
                continue;
            }

            if (i < visibleTreeDefinitions.Count)
            {
                ResourceDefinition definition = visibleTreeDefinitions[i];
                if (!slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(true);
                }

                ResourceDefinition capturedDefinition = definition;
                string displayName = !string.IsNullOrWhiteSpace(definition.resourceName)
                    ? definition.resourceName
                    : definition.name;
                slot.SetCustomFilterItem(
                    definition.ResourceIcon,
                    displayName,
                    loggingMachine.IsTreeTypeEnabled(definition),
                    isOn => HandleLoggingTreeToggleChanged(capturedDefinition, isOn));
            }
            else
            {
                slot.ClearFilterItem();
                if (slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(false);
                }
            }
        }
    }

    private void HideEmptySlots()
    {
        if (slotList == null)
        {
            return;
        }

        for (int i = 0; i < slotList.Count; i++)
        {
            ItemFilterSlot slot = slotList[i];
            if (slot == null)
            {
                continue;
            }

            slot.ClearFilterItem();
            if (slot.gameObject.activeSelf)
            {
                slot.gameObject.SetActive(false);
            }
        }
    }

    private static bool TryBuildAreaRestrictedFilter(
        MapObject target,
        ISet<int> allowedItemIds,
        ISet<ItemDefinition.EnergyType> allowedEnergyTypes)
    {
        if (!(target is BoxObject boxObject)
            || !(boxObject is InstallationObject installationObject))
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject.RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            return false;
        }

        bool isAreaScoped = false;
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = occupiedCoordinates[i];
            if (InputOutputModule.TryGetRuntimeIoOverlapAllowedItemIds(coordinate, allowedItemIds))
            {
                isAreaScoped = true;
                continue;
            }

            isAreaScoped |= InputOutputModuleItemAreaController.TryGetAcceptedItemIds(coordinate, allowedItemIds);
            isAreaScoped |= InputOutputModuleEnergyAreaController.TryGetAcceptedEnergyTypes(coordinate, allowedEnergyTypes);
            isAreaScoped |= InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(coordinate, allowedItemIds);
        }

        return isAreaScoped;
    }

    private static bool TryBuildProductionTargetFilter(
        MapObject target,
        List<ItemDefinition> definitions,
        List<ItemDefinition> results)
    {
        if (!TryResolveProductionMachine(target, out ProductionMachine productionMachine))
        {
            return false;
        }

        results?.Clear();
        if (definitions == null || results == null)
        {
            return true;
        }

        Dictionary<int, ItemDefinition> definitionsById = new Dictionary<int, ItemDefinition>();
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id >= 0 && !definitionsById.ContainsKey(definition.id))
            {
                definitionsById.Add(definition.id, definition);
            }
        }

        List<int> targetItemIds = new List<int>();
        productionMachine.TryCollectAllProductionTargetItemIds(targetItemIds);
        for (int i = 0; i < targetItemIds.Count; i++)
        {
            if (definitionsById.TryGetValue(targetItemIds[i], out ItemDefinition definition))
            {
                results.Add(definition);
            }
        }

        return true;
    }

    private static bool IsDefinitionAllowedForArea(
        ItemDefinition definition,
        ISet<int> allowedItemIds,
        ISet<ItemDefinition.EnergyType> allowedEnergyTypes)
    {
        if (definition == null)
        {
            return false;
        }

        if (allowedItemIds != null && allowedItemIds.Contains(definition.id))
        {
            return true;
        }

        return allowedEnergyTypes != null
               && definition.energyType != ItemDefinition.EnergyType.None
               && definition.energyAmount > 0
               && allowedEnergyTypes.Contains(definition.energyType);
    }

    private void HandleSlotToggleChanged(int itemId, bool isOn)
    {
        MapObject target = ResolveCurrentTarget();
        if (target == null)
        {
            return;
        }

        if (TryApplyProductionTargetSelection(target, itemId, isOn))
        {
            PersistTargetFilterState(target);
            Refresh();
            return;
        }

        if (TryApplyAreaScopedFilterSelection(target, itemId, isOn))
        {
            PersistTargetFilterState(target);
            Refresh();
            return;
        }

        target.SetItemFilterEnabled(itemId, GetFilterBitCount(), isOn);
        PersistTargetFilterState(target);
        Refresh();
    }

    private void HandleLoggingTreeToggleChanged(ResourceDefinition definition, bool isOn)
    {
        if (!(ResolveCurrentTarget() is LoggingMachine loggingMachine))
        {
            return;
        }

        loggingMachine.SetTreeTypeEnabled(definition, visibleTreeDefinitions, isOn);
        PersistTargetFilterState(loggingMachine);
        Refresh();
    }

    private int GetFilterBitCount()
    {
        int maxItemId = -1;
        for (int i = 0; i < visibleDefinitions.Count; i++)
        {
            ItemDefinition definition = visibleDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            if (definition.id > maxItemId)
            {
                maxItemId = definition.id;
            }
        }

        return Mathf.Max(0, maxItemId + 1);
    }

    private MapObject ResolveCurrentTarget()
    {
        if (boundTarget != null && boundTarget.gameObject != null)
        {
            return boundTarget;
        }

        boundTarget = ResolveFocusedFilterTarget();
        return boundTarget;
    }

    private static MapObject ResolveFocusedFilterTarget()
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            return null;
        }

        PlayerController playerController = GameManager.Instance.Player.GetComponent<PlayerController>();
        if (playerController == null || !playerController.TryGetFocusedItemFilterMapObject(out MapObject focusedMapObject))
        {
            return null;
        }

        return focusedMapObject;
    }

    private void PersistTargetFilterState(MapObject target)
    {
        if (!(target is InstallationObject installationObject))
        {
            return;
        }

        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = TerrainGenerator.ResolveActive();
        }

        cachedTerrainGenerator?.RegisterInstallationRuntimeState(installationObject);
    }

    private void HandleAllButtonClicked()
    {
        SetAllToggles(true);
    }

    private void HandleNoneButtonClicked()
    {
        SetAllToggles(false);
    }

    private void SetAllToggles(bool isEnabled)
    {
        MapObject target = ResolveCurrentTarget();
        if (target == null)
        {
            return;
        }

        if (target is LoggingMachine loggingMachine)
        {
            loggingMachine.SetAllTreeTypes(visibleTreeDefinitions, isEnabled);
            PersistTargetFilterState(loggingMachine);
            Refresh();
            return;
        }

        if (TryApplyProductionTargetBulkSelection(target, isEnabled))
        {
            PersistTargetFilterState(target);
            Refresh();
            return;
        }

        if (TryApplyAreaScopedBulkSelection(target, isEnabled))
        {
            PersistTargetFilterState(target);
            Refresh();
            return;
        }

        int filterBitCount = GetFilterBitCount();
        for (int i = 0; i < visibleDefinitions.Count; i++)
        {
            ItemDefinition definition = visibleDefinitions[i];
            if (definition == null || definition.id < 0)
            {
                continue;
            }

            target.SetItemFilterEnabled(definition.id, filterBitCount, isEnabled);
        }

        PersistTargetFilterState(target);
        Refresh();
    }

    private bool TryApplyAreaScopedFilterSelection(MapObject target, int changedItemId, bool changedState)
    {
        if (!TryIsAreaScopedTarget(target))
        {
            return false;
        }

        int totalFilterBitCount = GetTotalItemFilterBitCount();
        if (totalFilterBitCount <= 0)
        {
            return false;
        }

        HashSet<int> enabledItemIds = new HashSet<int>();
        for (int i = 0; i < visibleDefinitions.Count; i++)
        {
            ItemDefinition definition = visibleDefinitions[i];
            if (definition == null || definition.id < 0)
            {
                continue;
            }

            bool isEnabled = definition.id == changedItemId
                ? changedState
                : target.IsItemFilterEnabled(definition.id, totalFilterBitCount);
            if (isEnabled)
            {
                enabledItemIds.Add(definition.id);
            }
        }

        OverwriteTargetFilterMask(target, totalFilterBitCount, enabledItemIds);
        return true;
    }

    private bool TryApplyProductionTargetSelection(MapObject target, int changedItemId, bool changedState)
    {
        if (!TryResolveProductionMachine(target, out ProductionMachine productionMachine))
        {
            return false;
        }

        if (changedState)
        {
            if (productionMachine.CanSelectProductionTarget(changedItemId))
            {
                productionMachine.SetExclusiveProductionTarget(changedItemId);
            }
        }
        else if (productionMachine.IsProductionTargetSelected(changedItemId))
        {
            productionMachine.ClearProductionTargetSelection();
        }

        return true;
    }

    private bool TryApplyProductionTargetBulkSelection(MapObject target, bool isEnabled)
    {
        if (!TryResolveProductionMachine(target, out ProductionMachine productionMachine))
        {
            return false;
        }

        if (!isEnabled)
        {
            productionMachine.ClearProductionTargetSelection();
            return true;
        }

        for (int i = 0; i < visibleDefinitions.Count; i++)
        {
            ItemDefinition definition = visibleDefinitions[i];
            if (definition != null && productionMachine.CanSelectProductionTarget(definition.id))
            {
                productionMachine.SetExclusiveProductionTarget(definition.id);
                return true;
            }
        }

        productionMachine.ClearProductionTargetSelection();
        return true;
    }

    private bool TryApplyAreaScopedBulkSelection(MapObject target, bool isEnabled)
    {
        if (!TryIsAreaScopedTarget(target))
        {
            return false;
        }

        int totalFilterBitCount = GetTotalItemFilterBitCount();
        if (totalFilterBitCount <= 0)
        {
            return false;
        }

        HashSet<int> enabledItemIds = new HashSet<int>();
        if (isEnabled)
        {
            for (int i = 0; i < visibleDefinitions.Count; i++)
            {
                ItemDefinition definition = visibleDefinitions[i];
                if (definition == null || definition.id < 0)
                {
                    continue;
                }

                enabledItemIds.Add(definition.id);
            }
        }

        OverwriteTargetFilterMask(target, totalFilterBitCount, enabledItemIds);
        return true;
    }

    private bool TryIsAreaScopedTarget(MapObject target)
    {
        HashSet<int> allowedItemIds = new HashSet<int>();
        HashSet<ItemDefinition.EnergyType> allowedEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
        return TryBuildAreaRestrictedFilter(target, allowedItemIds, allowedEnergyTypes);
    }

    private static bool TryResolveProductionMachine(MapObject target, out ProductionMachine productionMachine)
    {
        productionMachine = null;
        if (target == null)
        {
            return false;
        }

        productionMachine = target as ProductionMachine;
        if (productionMachine != null)
        {
            return true;
        }

        productionMachine = target.GetComponent<ProductionMachine>();
        if (productionMachine != null)
        {
            return true;
        }

        productionMachine = target.GetComponentInChildren<ProductionMachine>(true);
        return productionMachine != null;
    }

    private static void OverwriteTargetFilterMask(MapObject target, int totalFilterBitCount, ISet<int> enabledItemIds)
    {
        if (target == null || totalFilterBitCount <= 0)
        {
            return;
        }

        for (int itemId = 0; itemId < totalFilterBitCount; itemId++)
        {
            bool enabled = enabledItemIds != null && enabledItemIds.Contains(itemId);
            target.SetItemFilterEnabled(itemId, totalFilterBitCount, enabled);
        }
    }

    private int GetTotalItemFilterBitCount()
    {
        if (GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return GetFilterBitCount();
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null || definitions.Count <= 0)
        {
            return GetFilterBitCount();
        }

        int maxItemId = -1;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            if (definition.id > maxItemId)
            {
                maxItemId = definition.id;
            }
        }

        return Mathf.Max(GetFilterBitCount(), maxItemId + 1);
    }

    private TerrainGenerator ResolveTerrainGenerator()
    {
        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = TerrainGenerator.ResolveActive();
        }

        return cachedTerrainGenerator;
    }

    private void EnsureLoggingGrowthControl()
    {
        if (loggingGrowthControl != null)
        {
            return;
        }

        TextMeshProUGUI styleSource = GetComponentInChildren<TextMeshProUGUI>(true);
        Sprite woodFrameSprite = ResolveBulkButtonSprite();
        loggingGrowthControl = new GameObject(
            "Logging Minimum Growth",
            typeof(RectTransform),
            typeof(CanvasRenderer));
        RectTransform root = loggingGrowthControl.GetComponent<RectTransform>();
        root.SetParent(transform, false);
        root.anchorMin = new Vector2(1f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(1f, 1f);
        root.anchoredPosition = new Vector2(-28f, -26f);
        root.sizeDelta = new Vector2(552f, 68f);

        GameObject labelObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(root, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0.31f, 1f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        loggingGrowthLabel = labelObject.GetComponent<TextMeshProUGUI>();
        loggingGrowthLabel.fontSize = styleSource != null
            ? Mathf.Min(styleSource.fontSize, 21f)
            : 21f;
        loggingGrowthLabel.color = styleSource != null ? styleSource.color : Color.white;
        loggingGrowthLabel.alignment = TextAlignmentOptions.MidlineRight;
        loggingGrowthLabel.raycastTarget = false;
        if (styleSource != null)
        {
            loggingGrowthLabel.font = styleSource.font;
            loggingGrowthLabel.fontSharedMaterial = styleSource.fontSharedMaterial;
        }

        RectTransform valueBadge = CreateSliderImage(
            "Value Badge",
            root,
            Color.white,
            new Vector2(0.325f, 0.13f),
            new Vector2(0.415f, 0.87f),
            woodFrameSprite,
            Image.Type.Sliced);
        valueBadge.offsetMin = new Vector2(2f, 0f);
        valueBadge.offsetMax = new Vector2(-2f, 0f);
        loggingGrowthValueLabel = CreateSliderText(
            "Value",
            valueBadge,
            styleSource,
            23f,
            TextAlignmentOptions.Center);

        GameObject sliderObject = new GameObject(
            "Slider",
            typeof(RectTransform),
            typeof(Slider));
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.SetParent(root, false);
        sliderRect.anchorMin = new Vector2(0.44f, 0f);
        sliderRect.anchorMax = new Vector2(1f, 1f);
        sliderRect.offsetMin = Vector2.zero;
        sliderRect.offsetMax = Vector2.zero;

        RectTransform background = CreateSliderImage(
            "Background",
            sliderRect,
            Color.white,
            new Vector2(0f, 0.2f),
            new Vector2(1f, 0.8f),
            woodFrameSprite,
            Image.Type.Sliced);
        RectTransform track = CreateSliderImage(
            "Track",
            sliderRect,
            new Color(0.16f, 0.065f, 0.025f, 0.96f),
            new Vector2(0f, 0.37f),
            new Vector2(1f, 0.63f));
        track.offsetMin = new Vector2(13f, 0f);
        track.offsetMax = new Vector2(-13f, 0f);
        RectTransform fillArea = CreateSliderContainer(
            "Fill Area",
            sliderRect,
            new Vector2(0f, 0.39f),
            new Vector2(1f, 0.61f),
            15f);
        RectTransform fill = CreateSliderImage(
            "Fill",
            fillArea,
            new Color(0.95f, 0.57f, 0.16f, 1f),
            Vector2.zero,
            Vector2.one);
        CreateSliderTicks(fillArea);
        RectTransform handleArea = CreateSliderContainer(
            "Handle Slide Area",
            sliderRect,
            Vector2.zero,
            Vector2.one,
            17f);
        RectTransform handle = CreateSliderImage(
            "Handle",
            handleArea,
            Color.white,
            new Vector2(0f, 0.13f),
            new Vector2(0f, 0.87f),
            loggingGrowthHandleSprite != null
                ? loggingGrowthHandleSprite
                : woodFrameSprite,
            loggingGrowthHandleSprite != null
                ? Image.Type.Simple
                : Image.Type.Sliced);
        handle.sizeDelta = new Vector2(34f, 0f);

        loggingGrowthSlider = sliderObject.GetComponent<Slider>();
        loggingGrowthSlider.minValue = ResourceDefinition.MinGrowth;
        loggingGrowthSlider.maxValue = ResourceDefinition.MaxGrowth;
        loggingGrowthSlider.wholeNumbers = true;
        loggingGrowthSlider.direction = Slider.Direction.LeftToRight;
        loggingGrowthSlider.fillRect = fill;
        loggingGrowthSlider.handleRect = handle;
        loggingGrowthSlider.targetGraphic = handle.GetComponent<Image>();
        loggingGrowthSlider.onValueChanged.AddListener(HandleLoggingGrowthChanged);
        background.SetAsFirstSibling();
        SetLoggingGrowthControlVisible(false);
    }

    private static RectTransform CreateSliderContainer(
        string objectName,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float horizontalInset)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(horizontalInset, 0f);
        rect.offsetMax = new Vector2(-horizontalInset, 0f);
        return rect;
    }

    private static RectTransform CreateSliderImage(
        string objectName,
        RectTransform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Sprite sprite = null,
        Image.Type imageType = Image.Type.Simple)
    {
        GameObject gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.type = sprite != null ? imageType : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = sprite != null ? 5f : 1f;
        return rect;
    }

    private static TextMeshProUGUI CreateSliderText(
        string objectName,
        RectTransform parent,
        TextMeshProUGUI styleSource,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = styleSource != null ? styleSource.color : Color.white;
        text.raycastTarget = false;
        if (styleSource != null)
        {
            text.font = styleSource.font;
            text.fontSharedMaterial = styleSource.fontSharedMaterial;
        }

        return text;
    }

    private static void CreateSliderTicks(RectTransform trackRect)
    {
        for (int i = ResourceDefinition.MinGrowth; i <= ResourceDefinition.MaxGrowth; i++)
        {
            float normalized = (i - ResourceDefinition.MinGrowth)
                               / (float)(ResourceDefinition.MaxGrowth - ResourceDefinition.MinGrowth);
            RectTransform tick = CreateSliderImage(
                $"Tick {i}",
                trackRect,
                new Color(1f, 0.82f, 0.47f, i % 5 == 0 ? 0.9f : 0.5f),
                new Vector2(normalized, 0.05f),
                new Vector2(normalized, 0.95f));
            tick.anchoredPosition = Vector2.zero;
            tick.sizeDelta = new Vector2(i % 5 == 0 ? 2f : 1f, 0f);
            tick.GetComponent<Image>().raycastTarget = false;
        }
    }

    private Sprite ResolveBulkButtonSprite()
    {
        Image buttonImage = allBtuuon != null
            ? allBtuuon.targetGraphic as Image
            : null;
        if (buttonImage == null && noneButton != null)
        {
            buttonImage = noneButton.targetGraphic as Image;
        }

        return buttonImage != null ? buttonImage.sprite : null;
    }

    private void ApplyLoggingHeaderLayout(bool loggingLayout)
    {
        RectTransform allRect = allBtuuon != null
            ? allBtuuon.transform as RectTransform
            : null;
        RectTransform noneRect = noneButton != null
            ? noneButton.transform as RectTransform
            : null;
        if (!bulkButtonLayoutCached)
        {
            if (allRect != null)
            {
                originalAllButtonPosition = allRect.anchoredPosition;
                originalAllButtonSize = allRect.sizeDelta;
            }

            if (noneRect != null)
            {
                originalNoneButtonPosition = noneRect.anchoredPosition;
                originalNoneButtonSize = noneRect.sizeDelta;
            }

            bulkButtonLayoutCached = true;
        }

        if (allRect != null)
        {
            allRect.anchoredPosition = loggingLayout
                ? new Vector2(34f, -32f)
                : originalAllButtonPosition;
            allRect.sizeDelta = loggingLayout
                ? new Vector2(104f, 56f)
                : originalAllButtonSize;
        }

        if (noneRect != null)
        {
            noneRect.anchoredPosition = loggingLayout
                ? new Vector2(152f, -32f)
                : originalNoneButtonPosition;
            noneRect.sizeDelta = loggingLayout
                ? new Vector2(104f, 56f)
                : originalNoneButtonSize;
        }
    }

    private void RefreshLoggingGrowthControl(LoggingMachine loggingMachine)
    {
        EnsureLoggingGrowthControl();
        SetLoggingGrowthControlVisible(true);
        int growth = loggingMachine != null
            ? loggingMachine.MinimumGrowth
            : ResourceDefinition.MinGrowth;
        if (loggingGrowthSlider != null)
        {
            loggingGrowthSlider.SetValueWithoutNotify(growth);
        }

        if (loggingGrowthLabel != null)
        {
            loggingGrowthLabel.text = "MINIMUM GROWTH";
        }

        if (loggingGrowthValueLabel != null)
        {
            loggingGrowthValueLabel.text = growth.ToString();
        }
    }

    private void SetLoggingGrowthControlVisible(bool visible)
    {
        if (loggingGrowthControl != null && loggingGrowthControl.activeSelf != visible)
        {
            loggingGrowthControl.SetActive(visible);
        }
    }

    private void HandleLoggingGrowthChanged(float value)
    {
        if (!(ResolveCurrentTarget() is LoggingMachine loggingMachine))
        {
            return;
        }

        loggingMachine.SetMinimumGrowth(Mathf.RoundToInt(value));
        PersistTargetFilterState(loggingMachine);
        RefreshLoggingGrowthControl(loggingMachine);
    }

    private Button FindButtonByNames(params string[] names)
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null)
            {
                continue;
            }

            for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                if (candidate.name == names[nameIndex])
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
