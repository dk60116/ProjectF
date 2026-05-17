using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProductionMachine : InputOutputModule
{
    [SerializeField]
    private List<SpriteRenderer> targetIconDisplays;

    protected override void OnEnable()
    {
        base.OnEnable();
        RefreshProductionTargetIconDisplays();
    }

    public bool TryCollectProductionTargetItemIds(ICollection<int> itemIds)
    {
        if (itemIds == null)
        {
            return false;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (outputs == null || outputs.Count <= 0)
        {
            return false;
        }

        HashSet<int> seenItemIds = new HashSet<int>();
        bool foundAny = false;
        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition outputDefinition = outputs[i].itemDefinition;
            int outputItemId = outputDefinition != null ? outputDefinition.id : -1;
            if (outputItemId < 0 || !seenItemIds.Add(outputItemId))
            {
                continue;
            }

            itemIds.Add(outputItemId);
            foundAny = true;
        }

        return foundAny;
    }

    public int ResolveSelectedProductionTargetItemId()
    {
        List<int> targetItemIds = new List<int>();
        if (!TryCollectProductionTargetItemIds(targetItemIds))
        {
            return -1;
        }

        if (!IsItemFilterMaskInitialized)
        {
            return -1;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(targetItemIds);
        for (int i = 0; i < targetItemIds.Count; i++)
        {
            int targetItemId = targetItemIds[i];
            if (IsItemFilterEnabled(targetItemId, filterBitCount))
            {
                return targetItemId;
            }
        }

        return -1;
    }

    public bool IsProductionTargetSelected(int itemId)
    {
        return itemId >= 0 && ResolveSelectedProductionTargetItemId() == itemId;
    }

    public void SetExclusiveProductionTarget(int itemId)
    {
        List<int> targetItemIds = new List<int>();
        if (!TryCollectProductionTargetItemIds(targetItemIds))
        {
            return;
        }

        if (!targetItemIds.Contains(itemId))
        {
            ClearProductionTargetSelection();
            return;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(targetItemIds);
        ClearAllProductionTargetFilterBits(filterBitCount);
        SetItemFilterEnabled(itemId, filterBitCount, true);
        RefreshProductionTargetIconDisplays();
    }

    public void ClearProductionTargetSelection()
    {
        List<int> targetItemIds = new List<int>();
        if (!TryCollectProductionTargetItemIds(targetItemIds))
        {
            return;
        }

        int filterBitCount = ResolveProductionTargetFilterBitCount(targetItemIds);
        ClearAllProductionTargetFilterBits(filterBitCount);
        RefreshProductionTargetIconDisplays();
    }

    protected override bool IsRecipeOutputAllowedByItemFilter(int outputItemId)
    {
        return IsProductionTargetSelected(outputItemId);
    }

    protected override bool ShouldShowObjectInfoEmptyRecipeLine(int outputItemId)
    {
        return IsProductionTargetSelected(outputItemId);
    }

    protected override bool ShouldShowObjectInfoEmptyInputOutputSlots()
    {
        return ResolveSelectedProductionTargetItemId() >= 0;
    }

    protected override void OnItemFilterMaskChanged()
    {
        base.OnItemFilterMaskChanged();
        RefreshProductionTargetIconDisplays();
    }

    private void RefreshProductionTargetIconDisplays()
    {
        if (targetIconDisplays == null || targetIconDisplays.Count <= 0)
        {
            return;
        }

        Sprite targetIcon = null;
        int selectedTargetItemId = ResolveSelectedProductionTargetItemId();
        if (selectedTargetItemId >= 0
            && TryResolveProductionTargetDefinition(selectedTargetItemId, out ItemDefinition targetDefinition)
            && targetDefinition != null)
        {
            targetIcon = targetDefinition.icon;
        }

        for (int i = 0; i < targetIconDisplays.Count; i++)
        {
            SpriteRenderer display = targetIconDisplays[i];
            if (display == null)
            {
                continue;
            }

            display.sprite = targetIcon;
            display.enabled = targetIcon != null;
        }
    }

    private bool TryResolveProductionTargetDefinition(int itemId, out ItemDefinition definition)
    {
        definition = null;
        if (itemId < 0)
        {
            return false;
        }

        IReadOnlyList<ItemIoEntry> outputs = OutputList;
        if (outputs == null)
        {
            return false;
        }

        for (int i = 0; i < outputs.Count; i++)
        {
            ItemDefinition outputDefinition = outputs[i].itemDefinition;
            if (outputDefinition != null && outputDefinition.id == itemId)
            {
                definition = outputDefinition;
                return true;
            }
        }

        return false;
    }

    private void ClearAllProductionTargetFilterBits(int filterBitCount)
    {
        for (int itemId = 0; itemId < filterBitCount; itemId++)
        {
            SetItemFilterEnabled(itemId, filterBitCount, false);
        }
    }

    private static int ResolveProductionTargetFilterBitCount(List<int> targetItemIds)
    {
        int maxItemId = -1;
        if (GameManager.Instance != null && GameManager.Instance.ItemManger != null)
        {
            List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    ItemDefinition definition = definitions[i];
                    if (definition != null && definition.id > maxItemId)
                    {
                        maxItemId = definition.id;
                    }
                }
            }
        }

        if (targetItemIds != null)
        {
            for (int i = 0; i < targetItemIds.Count; i++)
            {
                if (targetItemIds[i] > maxItemId)
                {
                    maxItemId = targetItemIds[i];
                }
            }
        }

        return Mathf.Max(0, maxItemId + 1);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        RefreshProductionTargetIconDisplays();
    }
#endif
}
