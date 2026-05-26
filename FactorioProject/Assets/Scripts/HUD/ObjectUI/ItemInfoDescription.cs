using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemInfoDescription : MonoBehaviour
{
    private const int DefaultConveyorInfoSlotCount = 2;
    private const int Belt2FInfoSlotCount = 6;
    private const string DefaultFluidItemName = "Water";
    private static readonly Color FluidGaugeFillColor = new Color(0.08f, 0.55f, 1f, 1f);
    private static readonly Color ProducingSignColor = new Color(0.1f, 0.8f, 0.1f, 1f);
    private static readonly Color StoppedSignColor = new Color(0.9f, 0.05f, 0.03f, 1f);

    [SerializeField]
    private List<GameObject> defaultParent = new List<GameObject>();
    [SerializeField]
    private List<TextMeshProUGUI> defaultText = new List<TextMeshProUGUI>();
    [SerializeField]
    private List<Image> defaultSign = new List<Image>();
    [SerializeField]
    private GameObject energyGauge, workGauge;
    [SerializeField]
    private Image energyFill, workFill;
    [SerializeField]
    private TextMeshProUGUI energyText, workText;
    [SerializeField]
    private List<GameObject> defaultItem;
    [SerializeField]
    private GameObject energyItem, inputItem, outputItem;
    [SerializeField]
    private List<ItemSlot> defaultItemSlot;
    [SerializeField]
    private ItemSlot energyItemSlot, inputItemSlot, outputItemSlot;

    private readonly List<int> conveyorItemIds = new List<int>(2);
    private int defaultStatusLineIndex;

    private void Awake()
    {
        ResolveDefaultItemSlotReferences();
    }

    private void OnValidate()
    {
        ResolveDefaultItemSlotReferences();
    }

    public void Clear()
    {
        ResolveDefaultItemSlotReferences();
        defaultStatusLineIndex = 0;
        ClearDefaultLines();
        SetGauge(energyGauge, energyFill, energyText, false, 0f, Color.white, 0f, 0f);
        SetGauge(workGauge, workFill, workText, false, 0f, Color.white, 0f, 0f);

        ClearItemSlots(defaultItem, defaultItemSlot);
        ClearItemSlot(energyItem, energyItemSlot);
        ClearItemSlot(inputItem, inputItemSlot);
        ClearItemSlot(outputItem, outputItemSlot);
    }

    public void ShowResourceReserves(int reserves)
    {
        Clear();
        SetResourceReservesLine(0, reserves);
    }

    public void ShowConveyorBelt(ConveyorBelt conveyorBelt, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

        conveyorItemIds.Clear();
        int slotCount = conveyorBelt is ConvayorBelt2F ? Belt2FInfoSlotCount : DefaultConveyorInfoSlotCount;
        conveyorBelt?.CopyObjectInfoItemIds(conveyorItemIds, slotCount);

        for (int i = 0; i < slotCount; i++)
        {
            SetDefaultItemSlot(i, conveyorItemIds.Count > i ? conveyorItemIds[i] : -1, true);
        }
    }

    public void ShowPipe(Pipe pipe, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

        if (pipe != null && pipe.TryGetObjectInfoFluidInfo(out int fluidItemId, out float temperatureCelsius))
        {
            SetDefaultText(
                defaultStatusLineIndex,
                $"Fluid: {ResolveItemDisplayName(fluidItemId, temperatureCelsius)}",
                true);
            SetDefaultSign(defaultStatusLineIndex, false, Color.white);
            SetDefaultItemSlot(0, fluidItemId, 1, 0, true, false, false, temperatureCelsius);
            return;
        }

        SetDefaultText(defaultStatusLineIndex, "Fluid: None", true);
        SetDefaultSign(defaultStatusLineIndex, false, Color.white);
    }

    public void ShowBoxObject(BoxObject boxObject, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

        if (boxObject != null
            && boxObject.TryGetObjectInfoItem(out int itemId, out int itemCount, out int capacity))
        {
            SetDefaultItemSlot(0, itemId, itemCount, capacity, true, true, true);
        }
        else
        {
            SetDefaultItemSlot(0, -1, 0, 0, true, true);
        }
    }

    public void ShowRobotArm(RobotArm robotArm, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        SetDefaultItemSlot(0, robotArm != null ? robotArm.HeldItemId : -1, true);
        if (robotArm == null)
        {
            return;
        }

        robotArm.GetObjectInfoStatus(out string statusText, out bool isWorking);
        SetDefaultStatus(statusText, isWorking);
    }

    public void ShowInstallationObject(InstallationObject installationObject, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        SetFluidStorageDefaultItemSlot(0, installationObject);
    }

    public void ShowInputOutputModule(InputOutputModule module, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

        Pump pump = module as Pump;
        if (pump != null)
        {
            pump.GetObjectInfoStatus(out string pumpStatusText, out bool isPumpProducing);
            SetDefaultStatus(pumpStatusText, isPumpProducing);
            SetPumpOutputRateDefaultItemSlot(0, pump);
            return;
        }

        Boiler boiler = module as Boiler;
        if (boiler != null && module.CanStoreFluid)
        {
            SetFluidStorageGauge(module);
            SetGauge(
                workGauge,
                workFill,
                workText,
                true,
                boiler.ObjectInfoBoilerTemperatureFillAmount,
                boiler.ObjectInfoBoilerTemperatureGaugeFillColor,
                boiler.WaterTemperatureCelsius,
                boiler.MaxWaterTemperatureCelsius,
                true);
        }
        else
        {
            SetGauge(
                energyGauge,
                energyFill,
                energyText,
                true,
                module != null ? module.ObjectInfoEnergyGaugeFillAmount : 0f,
                module != null ? module.ObjectInfoEnergyGaugeFillColor : Color.white,
                module != null ? module.ObjectInfoStoredEnergy : 0f,
                module != null ? module.ObjectInfoEnergyGaugeCapacity : 0f,
                true);

            SetGauge(
                workGauge,
                workFill,
                workText,
                true,
                module != null ? module.ObjectInfoWorkGaugeFillAmount : 0f,
                module != null ? module.ObjectInfoWorkGaugeFillColor : Color.white,
                module != null ? module.ObjectInfoCurrentUseEnergy : 0f,
                module != null ? module.ObjectInfoCompleteEnergy : 0f,
                true);
        }

        if (module == null)
        {
            return;
        }

        module.GetObjectInfoStatus(out string statusText, out bool isProducing);
        SetDefaultStatus(statusText, isProducing);

        SetFluidStorageDefaultItemSlot(0, module);

        if (module.TryGetObjectInfoEnergyInput(
            out int energyItemId,
            out int energyAreaCount,
            out int energyAreaCapacity))
        {
            SetItemSlot(
                energyItem,
                energyItemSlot,
                energyItemId,
                energyAreaCount,
                energyAreaCapacity,
                true,
                false,
                ResolveModuleFluidTemperature(module, energyItemId));
        }

        if (module.TryGetObjectInfoItemPair(
                out int inputItemId,
                out int inputAreaCount,
                out int inputAreaCapacity,
                out int outputItemId,
                out int outputAreaCount,
                out int outputAreaCapacity))
        {
            SetItemSlot(
                inputItem,
                inputItemSlot,
                inputItemId,
                inputAreaCount,
                inputAreaCapacity,
                true,
                true,
                ResolveModuleFluidTemperature(module, inputItemId));
            if (!TrySetBoilerOutputRateItemSlot(outputItem, outputItemSlot, boiler))
            {
                SetItemSlot(
                    outputItem,
                    outputItemSlot,
                    outputItemId,
                    outputAreaCount,
                    outputAreaCapacity,
                    true,
                    true,
                    ResolveModuleFluidTemperature(module, outputItemId));
            }

            return;
        }

        if (module.TryGetObjectInfoOutput(
                out outputItemId,
                out outputAreaCount,
                out outputAreaCapacity,
                out bool displayZeroOutputItem))
        {
            if (!TrySetBoilerOutputRateItemSlot(outputItem, outputItemSlot, boiler))
            {
                SetItemSlot(
                    outputItem,
                    outputItemSlot,
                    outputItemId,
                    outputAreaCount,
                    outputAreaCapacity,
                    true,
                    displayZeroOutputItem,
                    ResolveModuleFluidTemperature(module, outputItemId));
            }
        }
    }

    private void SetDefaultStatus(string text, bool isProducing)
    {
        SetDefaultText(defaultStatusLineIndex, text, !string.IsNullOrEmpty(text));
        SetDefaultSign(defaultStatusLineIndex, !string.IsNullOrEmpty(text), isProducing ? ProducingSignColor : StoppedSignColor);
    }

    private void BeginObjectDisplay(Resource underlyingResource)
    {
        Clear();
        if (!IsDisplayableUnderlyingResource(underlyingResource))
        {
            return;
        }

        SetResourceReservesLine(0, underlyingResource.RemainingHarvestOutputCount);
        defaultStatusLineIndex = 1;
    }

    private void SetResourceReservesLine(int index, int reserves)
    {
        SetDefaultText(index, $"Reserves: {Mathf.Max(0, reserves)}", true);
        SetDefaultSign(index, false, Color.white);
    }

    private void SetPumpOutputRateDefaultItemSlot(int index, Pump pump)
    {
        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;
        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return;
        }

        int outputItemId = -1;
        float litersPerSecond = 0f;
        if (pump != null)
        {
            pump.TryGetObjectInfoOutputRate(out outputItemId, out litersPerSecond);
        }

        ItemManager.ItemSet fluidItemSet = ResolveFluidItemSet(outputItemId);
        string displayName = ResolveFluidDisplayName(
            string.IsNullOrWhiteSpace(fluidItemSet.name) ? DefaultFluidItemName : fluidItemSet.name,
            pump != null
                ? pump.GetStoredFluidTemperatureCelsius(outputItemId)
                : MapClimate.CurrentWaterTemperatureCelsius);
        slot.SetCustomDisplay(
            outputItemId,
            fluidItemSet.icon,
            displayName,
            $"{FormatGaugeNumber(litersPerSecond, false)} L/s");
    }

    private bool TrySetBoilerOutputRateItemSlot(GameObject root, ItemSlot slot, Boiler boiler)
    {
        if (boiler == null
            || !boiler.TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond))
        {
            return false;
        }

        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return true;
        }

        ItemManager.ItemSet fluidItemSet = ResolveFluidItemSet(outputItemId);
        string displayName = ResolveFluidDisplayName(
            string.IsNullOrWhiteSpace(fluidItemSet.name) ? ResolveItemDisplayName(outputItemId) : fluidItemSet.name,
            boiler.GetStoredFluidTemperatureCelsius(outputItemId));
        slot.SetCustomDisplay(
            outputItemId,
            fluidItemSet.icon,
            displayName,
            $"{FormatGaugeNumber(litersPerSecond, false)} L/s");
        return true;
    }

    private void SetFluidStorageDefaultItemSlot(int index, InstallationObject installationObject)
    {
        if (installationObject == null || !installationObject.CanStoreFluid)
        {
            return;
        }

        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;
        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return;
        }

        if (installationObject is SteamGenerator && installationObject.StoredFluidItemId < 0)
        {
            slot.SetCustomDisplay(-1, null, string.Empty, string.Empty);
            return;
        }

        float storedLiters = installationObject.StoredFluidLiters;
        float capacityLiters = installationObject.FluidStorageCapacityLiters;
        ItemManager.ItemSet fluidItemSet = ResolveFluidItemSet(installationObject.StoredFluidItemId);
        string displayName = ResolveFluidStorageDisplayName(installationObject, fluidItemSet);
        slot.SetCustomDisplay(
            fluidItemSet.id,
            fluidItemSet.icon,
            displayName,
            $"{FormatGaugeNumber(storedLiters, true)} / {FormatGaugeNumber(capacityLiters, true)} L");
    }

    private void SetFluidStorageGauge(InstallationObject installationObject)
    {
        float capacityLiters = installationObject != null ? installationObject.FluidStorageCapacityLiters : 0f;
        float storedLiters = installationObject != null ? installationObject.StoredFluidLiters : 0f;
        SetGauge(
            energyGauge,
            energyFill,
            energyText,
            true,
            capacityLiters > 0.0001f ? storedLiters / capacityLiters : 0f,
            FluidGaugeFillColor,
            storedLiters,
            capacityLiters,
            true);
    }

    private static bool IsDisplayableUnderlyingResource(Resource resource)
    {
        return resource != null && resource.CanHarvest;
    }

    private void SetDefaultItemSlot(int index, int itemId, bool forceRootActive)
    {
        SetDefaultItemSlot(index, itemId, 1, 0, forceRootActive, false, false, null);
    }

    private void SetDefaultItemSlot(int index, int itemId, int count, int maxCount, bool forceRootActive, bool showCount)
    {
        SetDefaultItemSlot(index, itemId, count, maxCount, forceRootActive, showCount, false, null);
    }

    private void SetDefaultItemSlot(
        int index,
        int itemId,
        int count,
        int maxCount,
        bool forceRootActive,
        bool showCount,
        bool showEmptyCount)
    {
        SetDefaultItemSlot(index, itemId, count, maxCount, forceRootActive, showCount, showEmptyCount, null);
    }

    private void SetDefaultItemSlot(
        int index,
        int itemId,
        int count,
        int maxCount,
        bool forceRootActive,
        bool showCount,
        bool showEmptyCount,
        float? fluidTemperatureCelsius)
    {
        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;

        SetActiveIfNeeded(root, forceRootActive || itemId >= 0 || showEmptyCount);
        if (slot == null)
        {
            return;
        }

        if (itemId >= 0)
        {
            if (InputOutputModule.IsFluidItemId(itemId))
            {
                SetFluidItemSlotDisplay(
                    slot,
                    itemId,
                    Mathf.Max(0, count),
                    Mathf.Max(0, maxCount),
                    true,
                    showCount,
                    fluidTemperatureCelsius);
            }
            else
            {
                slot.SetItemDisplay(itemId, Mathf.Max(0, count), Mathf.Max(0, maxCount), true, showCount);
            }
        }
        else
        {
            if (showEmptyCount)
            {
                slot.SetEmptyCountDisplay(Mathf.Max(0, count), Mathf.Max(0, maxCount));
            }
            else
            {
                slot.Clear();
            }
        }
    }

    private void SetDefaultText(string text, bool visible)
    {
        SetDefaultText(0, text, visible);
    }

    private void SetDefaultText(int index, string text, bool visible)
    {
        TextMeshProUGUI targetText = GetListItem(defaultText, index);
        if (targetText != null)
        {
            targetText.text = visible ? text : string.Empty;
            SetActiveIfNeeded(targetText.gameObject, visible);
        }

        RefreshDefaultParentActive(index);
    }

    private void SetDefaultSign(bool visible, Color color)
    {
        SetDefaultSign(0, visible, color);
    }

    private void SetDefaultSign(int index, bool visible, Color color)
    {
        Image targetSign = GetListItem(defaultSign, index);
        if (targetSign != null)
        {
            targetSign.color = color;
            SetActiveIfNeeded(targetSign.gameObject, visible);
        }

        RefreshDefaultParentActive(index);
    }

    private void ClearDefaultLines()
    {
        int count = Mathf.Max(
            defaultParent != null ? defaultParent.Count : 0,
            defaultText != null ? defaultText.Count : 0,
            defaultSign != null ? defaultSign.Count : 0);

        for (int i = 0; i < count; i++)
        {
            TextMeshProUGUI text = GetListItem(defaultText, i);
            if (text != null)
            {
                text.text = string.Empty;
                SetActiveIfNeeded(text.gameObject, false);
            }

            Image sign = GetListItem(defaultSign, i);
            if (sign != null)
            {
                sign.color = Color.white;
                SetActiveIfNeeded(sign.gameObject, false);
            }

            SetActiveIfNeeded(GetListItem(defaultParent, i), false);
        }
    }

    private void RefreshDefaultParentActive(int index)
    {
        TextMeshProUGUI targetText = GetListItem(defaultText, index);
        Image targetSign = GetListItem(defaultSign, index);
        bool active = (targetText != null && targetText.gameObject.activeSelf)
            || (targetSign != null && targetSign.gameObject.activeSelf);
        SetActiveIfNeeded(GetListItem(defaultParent, index), active);
    }

    private static void ClearItemSlot(GameObject root, ItemSlot slot)
    {
        if (slot != null)
        {
            slot.Clear();
        }

        SetActiveIfNeeded(root, false);
    }

    private static void SetItemSlot(
        GameObject root,
        ItemSlot slot,
        int itemId,
        int count,
        int maxCount,
        bool forceRootActive = false,
        bool displayZeroCount = false,
        float? fluidTemperatureCelsius = null)
    {
        bool hasItem = itemId >= 0 && (displayZeroCount || count > 0);
        SetActiveIfNeeded(root, forceRootActive || hasItem);

        if (slot == null)
        {
            return;
        }

        if (hasItem)
        {
            int displayCount = Mathf.Max(0, count);
            int displayMaxCount = Mathf.Max(1, maxCount, displayCount);
            if (InputOutputModule.IsFluidItemId(itemId))
            {
                SetFluidItemSlotDisplay(
                    slot,
                    itemId,
                    displayCount,
                    displayMaxCount,
                    displayZeroCount,
                    true,
                    fluidTemperatureCelsius);
            }
            else
            {
                slot.SetItemDisplay(itemId, displayCount, displayMaxCount, displayZeroCount);
            }
        }
        else
        {
            slot.Clear();
        }
    }

    private static void SetFluidItemSlotDisplay(
        ItemSlot slot,
        int itemId,
        int count,
        int maxCount,
        bool allowZeroCount,
        bool showCount,
        float? fluidTemperatureCelsius)
    {
        if (slot == null)
        {
            return;
        }

        ItemManager.ItemSet itemSet = ResolveItemSet(itemId);
        string countText = showCount
            ? (maxCount > 0 ? $"{Mathf.Max(0, count)} / {Mathf.Max(1, maxCount)}" : Mathf.Max(0, count).ToString())
            : string.Empty;
        slot.SetCustomDisplay(
            itemId,
            itemSet.icon,
            ResolveFluidDisplayName(
                string.IsNullOrWhiteSpace(itemSet.name) ? ResolveItemDisplayName(itemId) : itemSet.name,
                fluidTemperatureCelsius ?? MapClimate.CurrentTemperatureCelsius),
            countText);
    }

    private static void ClearItemSlots(List<GameObject> roots, List<ItemSlot> slots)
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                ItemSlot slot = slots[i];
                if (slot != null)
                {
                    slot.Clear();
                }
            }
        }

        if (roots == null)
        {
            return;
        }

        for (int i = 0; i < roots.Count; i++)
        {
            SetActiveIfNeeded(roots[i], false);
        }
    }

    private static ItemManager.ItemSet ResolveFluidItemSet()
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (itemManager != null && TryResolveItemSetByName(itemManager, DefaultFluidItemName, out ItemManager.ItemSet itemSet))
        {
            return itemSet;
        }

        return new ItemManager.ItemSet
        {
            id = -1,
            name = DefaultFluidItemName
        };
    }

    private static ItemManager.ItemSet ResolveFluidItemSet(int fluidItemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (fluidItemId >= 0
            && itemManager != null
            && itemManager.TryGetItemSetById(fluidItemId, out ItemManager.ItemSet itemSet))
        {
            return itemSet;
        }

        return ResolveFluidItemSet();
    }

    private static string ResolveFluidStorageDisplayName(
        InstallationObject installationObject,
        ItemManager.ItemSet fluidItemSet)
    {
        string displayName = string.IsNullOrWhiteSpace(fluidItemSet.name)
            ? DefaultFluidItemName
            : fluidItemSet.name;
        float temperatureCelsius = installationObject != null
            ? installationObject.GetStoredFluidTemperatureCelsius(fluidItemSet.id)
            : MapClimate.CurrentTemperatureCelsius;

        return ResolveFluidDisplayName(displayName, temperatureCelsius);
    }

    private static string ResolveFluidDisplayName(string displayName, float temperatureCelsius)
    {
        string resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? DefaultFluidItemName
            : displayName;
        return $"{resolvedDisplayName} [{FormatTemperatureCelsius(temperatureCelsius)}]";
    }

    private static string FormatTemperatureCelsius(float value)
    {
        return $"{Mathf.RoundToInt(Mathf.Max(0f, value))}\u2103";
    }

    private static bool TryResolveItemSetByName(ItemManager itemManager, string itemName, out ItemManager.ItemSet itemSet)
    {
        itemSet = default;
        if (itemManager == null || string.IsNullOrWhiteSpace(itemName))
        {
            return false;
        }

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null
                    || (!NameMatches(definition.itemName, itemName) && !NameMatches(definition.name, itemName)))
                {
                    continue;
                }

                itemSet = new ItemManager.ItemSet
                {
                    id = definition.id,
                    name = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName,
                    portableMesh = definition.portableMesh,
                    portableMat = definition.portableMat,
                    icon = definition.icon,
                    size = (int)definition.size
                };
                return true;
            }
        }

        List<ItemManager.ItemSet> itemSets = itemManager.ItemSets;
        if (itemSets != null)
        {
            for (int i = 0; i < itemSets.Count; i++)
            {
                ItemManager.ItemSet candidate = itemSets[i];
                if (!NameMatches(candidate.name, itemName))
                {
                    continue;
                }

                itemSet = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool NameMatches(string value, string target)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(target)
            && string.Equals(value.Trim(), target.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveItemDisplayName(int itemId)
    {
        return ResolveItemDisplayName(itemId, null);
    }

    private static string ResolveItemDisplayName(int itemId, float? fluidTemperatureCelsius)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (itemManager != null
            && itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet)
            && !string.IsNullOrWhiteSpace(itemSet.name))
        {
            return InputOutputModule.IsFluidItemId(itemId)
                ? ResolveFluidDisplayName(itemSet.name, fluidTemperatureCelsius ?? MapClimate.CurrentTemperatureCelsius)
                : itemSet.name;
        }

        string fallbackName = itemId >= 0 ? $"Item {itemId}" : "None";
        return InputOutputModule.IsFluidItemId(itemId)
            ? ResolveFluidDisplayName(fallbackName, fluidTemperatureCelsius ?? MapClimate.CurrentTemperatureCelsius)
            : fallbackName;
    }

    private static ItemManager.ItemSet ResolveItemSet(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (itemId >= 0
            && itemManager != null
            && itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            return itemSet;
        }

        return new ItemManager.ItemSet
        {
            id = itemId,
            name = itemId >= 0 ? $"Item {itemId}" : "None"
        };
    }

    private static float? ResolveModuleFluidTemperature(InputOutputModule module, int itemId)
    {
        return module != null && itemId >= 0 && InputOutputModule.IsFluidItemId(itemId)
            ? module.GetStoredFluidTemperatureCelsius(itemId)
            : (float?)null;
    }

    private void ResolveDefaultItemSlotReferences()
    {
        if (defaultItemSlot == null)
        {
            defaultItemSlot = new List<ItemSlot>();
        }

        if (defaultItem == null)
        {
            return;
        }

        for (int i = 0; i < defaultItem.Count; i++)
        {
            while (defaultItemSlot.Count <= i)
            {
                defaultItemSlot.Add(null);
            }

            if (defaultItemSlot[i] != null || defaultItem[i] == null)
            {
                continue;
            }

            ItemSlot slot = defaultItem[i].GetComponent<ItemSlot>();
            if (slot == null)
            {
                slot = defaultItem[i].GetComponentInChildren<ItemSlot>(true);
            }

            defaultItemSlot[i] = slot;
        }
    }

    private static void SetActiveIfNeeded(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private static T GetListItem<T>(List<T> list, int index) where T : class
    {
        return list != null && index >= 0 && index < list.Count ? list[index] : null;
    }

    private static void SetGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        bool active,
        float fillAmount,
        Color fillColor,
        float currentValue,
        float maxValue,
        bool alwaysShowOneDecimal = false)
    {
        SetActiveIfNeeded(root, active);
        if (fill != null)
        {
            fill.fillAmount = active ? Mathf.Clamp01(fillAmount) : 0f;
            if (active)
            {
                fill.color = fillColor;
            }
        }

        if (text != null)
        {
            text.text = active ? FormatGaugeValue(currentValue, maxValue, alwaysShowOneDecimal) : string.Empty;
        }
    }

    private static string FormatGaugeValue(float currentValue, float maxValue, bool alwaysShowOneDecimal)
    {
        return $"{FormatGaugeNumber(currentValue, alwaysShowOneDecimal)} / {FormatGaugeNumber(maxValue, alwaysShowOneDecimal)}";
    }

    private static string FormatGaugeNumber(float value, bool alwaysShowOneDecimal)
    {
        value = Mathf.Max(0f, value);
        if (alwaysShowOneDecimal)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        float rounded = Mathf.Round(value);
        if (Mathf.Abs(value - rounded) < 0.05f)
        {
            return Mathf.RoundToInt(rounded).ToString();
        }

        return value.ToString("0.#");
    }
}
