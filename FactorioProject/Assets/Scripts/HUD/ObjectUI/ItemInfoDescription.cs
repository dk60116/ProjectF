using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemInfoDescription : MonoBehaviour
{
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

    public void Clear()
    {
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
        conveyorBelt?.CopyObjectInfoItemIds(conveyorItemIds, 2);

        SetDefaultItemSlot(0, conveyorItemIds.Count > 0 ? conveyorItemIds[0] : -1, true);
        SetDefaultItemSlot(1, conveyorItemIds.Count > 1 ? conveyorItemIds[1] : -1, true);
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

    public void ShowInputOutputModule(InputOutputModule module, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

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

        if (module == null)
        {
            return;
        }

        module.GetObjectInfoStatus(out string statusText, out bool isProducing);
        SetDefaultStatus(statusText, isProducing);

        if (module.TryGetObjectInfoEnergyInput(
            out int energyItemId,
            out int energyAreaCount,
            out int energyAreaCapacity))
        {
            SetItemSlot(energyItem, energyItemSlot, energyItemId, energyAreaCount, energyAreaCapacity, true);
        }

        if (module.TryGetObjectInfoItemPair(
                out int inputItemId,
                out int inputAreaCount,
                out int inputAreaCapacity,
                out int outputItemId,
                out int outputAreaCount,
                out int outputAreaCapacity))
        {
            SetItemSlot(inputItem, inputItemSlot, inputItemId, inputAreaCount, inputAreaCapacity, true, true);
            SetItemSlot(outputItem, outputItemSlot, outputItemId, outputAreaCount, outputAreaCapacity, true, true);
            return;
        }

        if (module.TryGetObjectInfoOutput(
                out outputItemId,
                out outputAreaCount,
                out outputAreaCapacity,
                out bool displayZeroOutputItem))
        {
            SetItemSlot(outputItem, outputItemSlot, outputItemId, outputAreaCount, outputAreaCapacity, true, displayZeroOutputItem);
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

    private static bool IsDisplayableUnderlyingResource(Resource resource)
    {
        return resource != null && resource.CanHarvest;
    }

    private void SetDefaultItemSlot(int index, int itemId, bool forceRootActive)
    {
        SetDefaultItemSlot(index, itemId, 1, 0, forceRootActive, false);
    }

    private void SetDefaultItemSlot(int index, int itemId, int count, int maxCount, bool forceRootActive, bool showCount)
    {
        SetDefaultItemSlot(index, itemId, count, maxCount, forceRootActive, showCount, false);
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
        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;

        SetActiveIfNeeded(root, forceRootActive || itemId >= 0 || showEmptyCount);
        if (slot == null)
        {
            return;
        }

        if (itemId >= 0)
        {
            slot.SetItemDisplay(itemId, Mathf.Max(0, count), Mathf.Max(0, maxCount), true, showCount);
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
        bool displayZeroCount = false)
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
            slot.SetItemDisplay(itemId, displayCount, displayMaxCount, displayZeroCount);
        }
        else
        {
            slot.Clear();
        }
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
