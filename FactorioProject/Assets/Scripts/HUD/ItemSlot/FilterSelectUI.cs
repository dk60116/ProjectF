using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class FilterSelectUI : MonoBehaviour
{
    [SerializeField]
    private List<ItemFilterSlot> slotList;

    [SerializeField]
    private Button allBtuuon, noneButton;

    private readonly List<ItemDefinition> visibleDefinitions = new List<ItemDefinition>();
    private MapObject boundTarget;
    private TerrainGenerator cachedTerrainGenerator;

    private void Awake()
    {
        EnsureSlotList();
        ResolveButtons();
        BindButtons();
        HideEmptySlots();
    }

    private void OnEnable()
    {
        ResolveButtons();
        BindButtons();
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
        BuildVisibleDefinitions();
        ApplyDefinitionsToSlots();
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

    private void BuildVisibleDefinitions()
    {
        visibleDefinitions.Clear();

        if (GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return;
        }

        HashSet<int> allowedItemIds = new HashSet<int>();
        HashSet<ItemDefinition.EnergyType> allowedEnergyTypes = new HashSet<ItemDefinition.EnergyType>();
        bool restrictToAreaItems = TryBuildAreaRestrictedFilter(boundTarget, allowedItemIds, allowedEnergyTypes);

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id < 0)
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

        int filterBitCount = GetFilterBitCount();

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

                bool isChecked = boundTarget == null || boundTarget.IsItemFilterEnabled(definition.id, filterBitCount);
                int itemId = definition.id;
                slot.SetFilterItem(itemId, isChecked, isOn => HandleSlotToggleChanged(itemId, isOn));
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
            isAreaScoped |= InputOutputModuleItemAreaController.TryGetAcceptedItemIds(coordinate, allowedItemIds);
            isAreaScoped |= InputOutputModuleEnergyAreaController.TryGetAcceptedEnergyTypes(coordinate, allowedEnergyTypes);
            isAreaScoped |= InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(coordinate, allowedItemIds);
        }

        return isAreaScoped;
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
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
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
