using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public partial class PlayerHUD
{
    [SerializeField]
    private UnityEngine.UI.Button eqipSlotOpenButton;
    [SerializeField]
    private ItemSlot[] equipSlots;
    [SerializeField]
    private ItemSlot[] ammoSlots;

    private UnityEngine.UI.Button boundEquipSlotOpenButton;
    private bool equipSlotsExpanded;
    private Sequence equipmentSlotAnimation;
    private readonly List<EquipmentSlotView> equipmentSlotViews = new List<EquipmentSlotView>();

    private sealed class EquipmentSlotView
    {
        internal ItemSlot Slot;
        internal RectTransform Rect;
        internal Vector3 ExpandedPosition;
        internal CanvasGroup Group;
        internal bool Interactable, BlocksRaycasts;
        internal HUDButtonHoverTween Hover;
    }

    private void InitializeEquipmentSlots()
    {
        SetEquipmentSlotsExpanded(false);
        equipmentSlotViews.Clear();
        if (equipSlots != null)
        {
            for (int i = 0; i < equipSlots.Length; i++)
            {
                var slot = equipSlots[i];
                if (slot == null) continue;
                var group = slot.GetComponent<CanvasGroup>();
                if (group == null) group = slot.gameObject.AddComponent<CanvasGroup>();
                var rect = slot.transform as RectTransform;
                equipmentSlotViews.Add(new EquipmentSlotView {
                    Slot = slot, Rect = rect, ExpandedPosition = rect != null ? rect.localPosition : Vector3.zero,
                    Group = group, Interactable = group.interactable, BlocksRaycasts = group.blocksRaycasts,
                    Hover = slot.GetComponent<HUDButtonHoverTween>()
                });
            }
            equipmentSlotViews.Sort((left, right) =>
                left.Slot.transform.GetSiblingIndex().CompareTo(right.Slot.transform.GetSiblingIndex()));
        }
        if (boundEquipSlotOpenButton != eqipSlotOpenButton)
        {
            UnbindEquipmentSlotButton();
            boundEquipSlotOpenButton = eqipSlotOpenButton;
            if (boundEquipSlotOpenButton != null)
                boundEquipSlotOpenButton.onClick.AddListener(ToggleEquipmentSlots);
        }
        SetEquipmentSlotsExpanded(false);
    }

    private void ToggleEquipmentSlots() => SetEquipmentSlotsExpanded(!equipSlotsExpanded, true);

    private void SetEquipmentSlotsExpanded(bool expanded, bool animate = false)
    {
        if (equipmentSlotAnimation != null)
        {
            var previous = equipmentSlotAnimation;
            equipmentSlotAnimation = null;
            previous.Kill();
        }
        equipSlotsExpanded = expanded;
        RefreshAmmoSlots();
        if (eqipSlotOpenButton != null)
            eqipSlotOpenButton.GetComponent<HUDButtonHoverTween>()?.ResetHoverImmediate(false);
        if (!animate || equipmentSlotViews.Count == 0)
        {
            CompleteEquipmentSlotAnimation(expanded);
            return;
        }

        var sequence = DOTween.Sequence().SetUpdate(true);
        bool hasAnimation = false;
        for (int step = 0; step < equipmentSlotViews.Count; step++)
        {
            int index = expanded ? step : equipmentSlotViews.Count - 1 - step;
            var view = equipmentSlotViews[index];
            if (view.Slot == null || !expanded && !view.Slot.gameObject.activeSelf) continue;
            view.Hover?.ResetHoverImmediate(false);
            view.Group.interactable = view.Group.blocksRaycasts = false;
            Vector3 origin = eqipSlotOpenButton != null && view.Rect != null && view.Rect.parent != null
                ? view.Rect.parent.InverseTransformPoint(eqipSlotOpenButton.transform.position) : Vector3.zero;
            // Retain the current pose on reversal; only a hidden slot starts at the opener.
            if (expanded && !view.Slot.gameObject.activeSelf && view.Rect != null) view.Rect.localPosition = origin;
            var itemSequence = DOTween.Sequence().SetUpdate(true);
            itemSequence.AppendCallback(() => {
                if (view.Slot == null) return;
                view.Slot.gameObject.SetActive(true);
                view.Group.interactable = view.Group.blocksRaycasts = false;
            });
            if (view.Rect != null)
                itemSequence.Append(view.Rect.DOLocalMove(expanded ? view.ExpandedPosition : origin,
                    expanded ? mapEditButtonExpandDuration : mapEditButtonExpandDuration * .85f)
                    .SetEase(expanded ? Ease.OutCubic : Ease.InCubic));
            itemSequence.AppendCallback(() => {
                if (view.Slot == null) return;
                if (!expanded) view.Slot.gameObject.SetActive(false);
                view.Group.interactable = view.Interactable;
                view.Group.blocksRaycasts = view.BlocksRaycasts;
            });
            sequence.Insert(step * mapEditButtonExpandStagger, itemSequence);
            hasAnimation = true;
        }
        if (!hasAnimation)
        {
            sequence.Kill();
            CompleteEquipmentSlotAnimation(expanded);
            return;
        }
        sequence.OnComplete(() => CompleteEquipmentSlotAnimation(expanded));
        sequence.OnKill(() => {
            if (ReferenceEquals(equipmentSlotAnimation, sequence)) equipmentSlotAnimation = null;
        });
        equipmentSlotAnimation = sequence;
    }

    private void CompleteEquipmentSlotAnimation(bool expanded)
    {
        equipmentSlotAnimation = null;
        for (int i = 0; i < equipmentSlotViews.Count; i++)
        {
            var view = equipmentSlotViews[i];
            if (view.Slot == null) continue;
            view.Hover?.ResetHoverImmediate(false);
            if (view.Rect != null) view.Rect.localPosition = view.ExpandedPosition;
            view.Group.interactable = view.Interactable;
            view.Group.blocksRaycasts = view.BlocksRaycasts;
            view.Slot.gameObject.SetActive(expanded);
        }
    }

    private void UnbindEquipmentSlotButton()
    {
        if (boundEquipSlotOpenButton != null)
            boundEquipSlotOpenButton.onClick.RemoveListener(ToggleEquipmentSlots);
        boundEquipSlotOpenButton = null;
    }

    private void ReleaseEquipmentSlots()
    {
        UnbindEquipmentSlotButton();
        SetEquipmentSlotsExpanded(false);
    }
}
