using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ItemFilterSlot : ItemSlot
{
    [SerializeField]
    private Toggle toggle;
    private UnityAction<bool> currentToggleCallback;

    public void SetFilterItem(int itemId)
    {
        SetFilterItem(itemId, true, null);
    }

    public void SetFilterItem(int itemId, bool isChecked, UnityAction<bool> onToggleChanged)
    {
        SetItemDisplay(itemId, 0, 0, true);
        HideCountLabel();
        BindToggle(isChecked, onToggleChanged);
    }

    public void ClearFilterItem()
    {
        Clear();
        HideCountLabel();
        UnbindToggle();
    }

    private void HideCountLabel()
    {
        if (CountLabel == null)
        {
            return;
        }

        CountLabel.text = string.Empty;
        if (CountLabel.gameObject.activeSelf)
        {
            CountLabel.gameObject.SetActive(false);
        }
    }

    private void BindToggle(bool isChecked, UnityAction<bool> onToggleChanged)
    {
        if (toggle == null)
        {
            return;
        }

        UnbindToggle();
        toggle.SetIsOnWithoutNotify(isChecked);
        currentToggleCallback = onToggleChanged;
        if (currentToggleCallback != null)
        {
            toggle.onValueChanged.AddListener(currentToggleCallback);
        }
    }

    private void UnbindToggle()
    {
        if (toggle == null)
        {
            return;
        }

        if (currentToggleCallback != null)
        {
            toggle.onValueChanged.RemoveListener(currentToggleCallback);
            currentToggleCallback = null;
        }

        toggle.SetIsOnWithoutNotify(false);
    }
}
