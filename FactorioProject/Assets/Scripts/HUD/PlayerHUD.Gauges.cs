using UnityEngine;
using UnityEngine.UI;

public partial class PlayerHUD
{
    private HudGaugeBinding handItemGaugeBinding;

    private void UpdateHandItemGauge()
    {
        Player currentPlayer = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (currentPlayer == null
            || !currentPlayer.TryGetActiveTorchEnergy(
                out _,
                out float remainingEnergy,
                out float energyCapacity)
            || energyCapacity <= 0f)
        {
            SetHandItemGaugeVisible(false);
            return;
        }

        ShowHandItemGauge(remainingEnergy, energyCapacity);
    }

    private void ShowHandItemGauge(float currentValue, float capacity)
    {
        CacheHandItemGaugeReferences();
        if (!handItemGaugeBinding.IsReady || capacity <= 0f)
        {
            SetHandItemGaugeVisible(false);
            return;
        }

        handItemGaugeBinding.SetVisible(true);
        handItemGaugeBinding.SetFill(Mathf.Clamp01(currentValue / capacity));
    }

    private void SetHandItemGaugeVisible(bool visible)
    {
        CacheHandItemGaugeReferences();
        handItemGaugeBinding.SetVisible(visible);
    }

    private void CacheHandItemGaugeReferences()
    {
        handItemGaugeBinding.Bind(handItemGauge);
    }

    private struct HudGaugeBinding
    {
        private RectTransform fillRect;
        private GameObject root;
        private float minimumAnchor;

        public bool IsReady => fillRect != null && root != null;

        public void Bind(Image targetFill)
        {
            if (targetFill == null || fillRect == targetFill.rectTransform)
            {
                return;
            }

            fillRect = targetFill.rectTransform;
            root = fillRect.parent != null ? fillRect.parent.gameObject : targetFill.gameObject;
            minimumAnchor = fillRect.anchorMin.x;
        }

        public void SetVisible(bool visible)
        {
            if (root != null && root.activeSelf != visible)
            {
                root.SetActive(visible);
            }
        }

        public void SetFill(float normalizedFill)
        {
            if (fillRect == null)
            {
                return;
            }

            float nextAnchor = Mathf.Lerp(minimumAnchor, 1f, Mathf.Clamp01(normalizedFill));
            Vector2 anchorMax = fillRect.anchorMax;
            if (Mathf.Approximately(anchorMax.x, nextAnchor))
            {
                return;
            }

            anchorMax.x = nextAnchor;
            fillRect.anchorMax = anchorMax;
        }
    }
}
