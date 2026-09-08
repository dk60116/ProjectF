using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectF.UI
{
    public sealed class IntegerRangeSlider : Selectable, IDragHandler, IInitializePotentialDragHandler
    {
        private const float HandleAnchorMinimumY = 0.06f;
        private const float HandleAnchorMaximumY = 0.94f;
        private RectTransform track, fill, lowerHandle, upperHandle;
        private int minimum, maximum, lower, upper;
        private bool editingUpper;
        public event Action<int, int> RangeChanged;

        public void Configure(RectTransform trackRect, RectTransform fillRect, RectTransform lowerRect, RectTransform upperRect)
        {
            track = trackRect;
            fill = fillRect;
            lowerHandle = lowerRect;
            upperHandle = upperRect;
            transition = Transition.None;
        }

        public void SetRangeWithoutNotify(int min, int max, int low, int high)
        {
            minimum = min;
            maximum = Mathf.Max(min, max);
            lower = Mathf.Clamp(low, minimum, maximum);
            upper = Mathf.Clamp(high, lower, maximum);
            RefreshVisuals();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
            base.OnPointerDown(eventData);
            if (!TryGetValue(eventData, out float value)) return;
            bool onLower = RectTransformUtility.RectangleContainsScreenPoint(lowerHandle, eventData.position, eventData.pressEventCamera);
            bool onUpper = RectTransformUtility.RectangleContainsScreenPoint(upperHandle, eventData.position, eventData.pressEventCamera);
            editingUpper = onLower != onUpper ? onUpper : Mathf.Abs(value - upper) <= Mathf.Abs(value - lower);
            SetDraggedValue(Mathf.RoundToInt(value));
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable() && TryGetValue(eventData, out float value))
                SetDraggedValue(Mathf.RoundToInt(value));
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (!IsInteractable()) { base.OnMove(eventData); return; }
            if (eventData.moveDir == MoveDirection.Up || eventData.moveDir == MoveDirection.Down)
                editingUpper = eventData.moveDir == MoveDirection.Up;
            else if (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right)
                SetDraggedValue((editingUpper ? upper : lower) + (eventData.moveDir == MoveDirection.Left ? -1 : 1));
            else base.OnMove(eventData);
        }

        private bool TryGetValue(PointerEventData eventData, out float value)
        {
            value = minimum;
            if (track == null || track.rect.width <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    track, eventData.position, eventData.pressEventCamera, out Vector2 local)) return false;
            value = Mathf.Lerp(minimum, maximum, Mathf.Clamp01((local.x - track.rect.xMin) / track.rect.width));
            return true;
        }

        private void SetDraggedValue(int value)
        {
            int next = editingUpper ? Mathf.Clamp(value, lower, maximum) : Mathf.Clamp(value, minimum, upper);
            if (next == (editingUpper ? upper : lower)) return;
            if (editingUpper) upper = next; else lower = next;
            RefreshVisuals();
            RangeChanged?.Invoke(lower, upper);
        }

        private void RefreshVisuals()
        {
            if (track == null) return;
            float low = Mathf.InverseLerp(minimum, maximum, lower);
            float high = Mathf.InverseLerp(minimum, maximum, upper);
            lowerHandle.anchorMin = new Vector2(low, HandleAnchorMinimumY);
            lowerHandle.anchorMax = new Vector2(low, HandleAnchorMaximumY);
            upperHandle.anchorMin = new Vector2(high, HandleAnchorMinimumY);
            upperHandle.anchorMax = new Vector2(high, HandleAnchorMaximumY);
            fill.anchorMin = new Vector2(low, 0.39f);
            fill.anchorMax = new Vector2(high, 0.61f);
        }
    }
}
