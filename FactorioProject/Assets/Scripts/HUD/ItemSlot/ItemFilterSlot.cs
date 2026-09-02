using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ItemFilterSlot : ItemSlot
{
    [SerializeField]
    private Toggle toggle;
    [SerializeField]
    private Image canNotImage;
    private UnityAction<bool> currentToggleCallback;

    public void SetFilterItem(int itemId)
    {
        SetFilterItem(itemId, true, null);
    }

    public void SetFilterItem(int itemId, bool isChecked, UnityAction<bool> onToggleChanged)
    {
        SetFilterItem(itemId, isChecked, true, onToggleChanged);
    }

    public void SetFilterItem(
        int itemId,
        bool isChecked,
        bool isInteractable,
        UnityAction<bool> onToggleChanged)
    {
        SetItemDisplay(itemId, 0, 0, true);
        HideCountLabel();
        SetCanNotVisible(!isInteractable);
        BindToggle(isChecked, isInteractable, onToggleChanged);
    }

    public void SetCustomFilterItem(
        Sprite displayIcon,
        string displayName,
        bool isChecked,
        UnityAction<bool> onToggleChanged)
    {
        SetCustomDisplay(displayIcon, displayName, string.Empty);
        HideCountLabel();
        SetCanNotVisible(false);
        BindToggle(isChecked, true, onToggleChanged);
    }

    public void ClearFilterItem()
    {
        Clear();
        HideCountLabel();
        SetCanNotVisible(false);
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

    private void SetCanNotVisible(bool isVisible)
    {
        if (canNotImage != null && canNotImage.gameObject.activeSelf != isVisible)
        {
            canNotImage.gameObject.SetActive(isVisible);
        }
    }

    private void BindToggle(
        bool isChecked,
        bool isInteractable,
        UnityAction<bool> onToggleChanged)
    {
        if (toggle == null)
        {
            return;
        }

        UnbindToggle();
        toggle.SetIsOnWithoutNotify(isChecked);
        toggle.interactable = isInteractable;
        currentToggleCallback = isInteractable ? onToggleChanged : null;
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
        toggle.interactable = true;
    }
}
