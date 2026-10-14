using System;
using UnityEngine;

namespace ProjectF.Presentation
{
    // Animal and ECS actors share the same bar geometry and visibility policy.
    internal sealed class WorldHealthBarView : IDisposable
    {
        private const float FullHealthHideDelay = 1f;
        private const float FullHealthThreshold = .9999f;
        internal readonly RectTransform Root;
        private readonly UnityEngine.UI.Image healthFill;
        private readonly RectTransform healthFillRect;
        private float fullHealthHideTime = -1f;
        private float previousHealth = -1f;
        private bool healthStateInitialized, wasAtFullHealth;

        internal WorldHealthBarView(Transform parent, string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
            Root = (RectTransform)root.transform;
            Root.SetParent(parent, false);
            Root.sizeDelta = new Vector2(.9f, .1f);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            var background = root.GetComponent<UnityEngine.UI.Image>();
            background.color = new Color(.04f, .04f, .04f, .85f);
            background.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            healthFillRect = (RectTransform)fill.transform;
            healthFillRect.SetParent(Root, false);
            healthFillRect.anchorMin = new Vector2(.04f, .18f);
            healthFillRect.anchorMax = new Vector2(.96f, .82f);
            healthFillRect.offsetMin = healthFillRect.offsetMax = Vector2.zero;
            healthFill = fill.GetComponent<UnityEngine.UI.Image>();
            healthFill.color = new Color(.78f, .12f, .1f, 1f);
            healthFill.raycastTarget = false;
            root.SetActive(false);
        }

        internal void NotifyAttackAnimationStarted(bool alive, float normalizedHealth)
        {
            if (!alive || Root == null) return;
            fullHealthHideTime = normalizedHealth >= FullHealthThreshold
                ? Time.unscaledTime + FullHealthHideDelay : -1f;
            SetVisible(true);
        }

        internal void HideImmediately()
        {
            fullHealthHideTime = -1f;
            SetVisible(false);
        }

        internal void Refresh(bool alive, float normalizedHealth, Vector3 position, float heightOffset, Camera camera)
        {
            if (Root == null) return;
            normalizedHealth = Mathf.Clamp01(normalizedHealth);
            if (normalizedHealth != previousHealth)
            {
                healthFillRect.anchorMax = new Vector2(.04f + .92f * normalizedHealth, .82f);
                healthFill.enabled = normalizedHealth > 0f;
                previousHealth = normalizedHealth;
            }
            if (!alive)
            {
                HideImmediately();
                return;
            }
            bool atFullHealth = normalizedHealth >= FullHealthThreshold;
            if (!atFullHealth)
            {
                fullHealthHideTime = -1f;
                SetVisible(true);
            }
            else if (healthStateInitialized && !wasAtFullHealth)
            {
                fullHealthHideTime = Time.unscaledTime + FullHealthHideDelay;
                SetVisible(true);
            }
            if (atFullHealth && fullHealthHideTime >= 0f && Time.unscaledTime >= fullHealthHideTime)
            {
                fullHealthHideTime = -1f;
                SetVisible(false);
            }
            wasAtFullHealth = atFullHealth;
            healthStateInitialized = true;
            if (!Root.gameObject.activeSelf) return;
            Root.position = position + Vector3.up * heightOffset;
            if (camera != null) Root.rotation = camera.transform.rotation;
        }

        private void SetVisible(bool visible)
        {
            if (Root != null && Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }

        public void Dispose()
        {
            if (Root == null) return;
            Root.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(Root.gameObject);
            else UnityEngine.Object.DestroyImmediate(Root.gameObject);
        }
    }
}
