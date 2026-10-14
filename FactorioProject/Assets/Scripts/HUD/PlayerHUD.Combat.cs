using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class PlayerHUD
{
    private RectTransform combatHud;
    private Image playerHealthFill;
    private TMP_Text playerHealthText;
    private Button reviveButton;
    private float shownPlayerHealth = -1f, shownPlayerMaximum = -1f;

    private void UpdateCombatHud()
    {
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            if (combatHud != null) combatHud.gameObject.SetActive(false);
            return;
        }
        if (combatHud == null) CreateCombatHud();
        if (combatHud == null) return;
        combatHud.gameObject.SetActive(true);
        float health = player.CurrentHealth, maximum = player.MaxHealth;
        if (shownPlayerHealth != health || shownPlayerMaximum != maximum)
        {
            shownPlayerHealth = health;
            shownPlayerMaximum = maximum;
            playerHealthFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                176f * health / maximum);
            playerHealthText.SetText("HP: {0:0}/{1:0}", health, maximum);
        }
        reviveButton.gameObject.SetActive(player.IsDead);
        reviveButton.interactable = !MapObjectTickManager.SimulationPaused && !SaveManager.GameplayInputBlocked;
    }

    private void CreateCombatHud()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        TMP_Text sourceText = GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = sourceText != null ? sourceText.font : TMP_Settings.defaultFontAsset;
        combatHud = NewCombatRect("Player Health", canvas.transform, new Vector2(180f, 22f));
        combatHud.anchorMin = combatHud.anchorMax = new Vector2(0.5f, 1f);
        combatHud.pivot = new Vector2(0.5f, 1f);
        combatHud.anchoredPosition = new Vector2(0f, -18f);
        Image background = combatHud.gameObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        background.raycastTarget = false;
        RectTransform fillRect = NewCombatRect("Health Fill", combatHud, new Vector2(176f, 18f));
        fillRect.anchorMin = fillRect.anchorMax = new Vector2(0f, 0.5f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = new Vector2(2f, 0f);
        playerHealthFill = fillRect.gameObject.AddComponent<Image>();
        playerHealthFill.color = new Color(0.76f, 0.16f, 0.18f);
        playerHealthFill.raycastTarget = false;
        playerHealthText = NewCombatText("Health", combatHud, new Vector2(180f, 22f), font);
        RectTransform buttonRect = NewCombatRect("Revive", combatHud, new Vector2(100f, 30f));
        buttonRect.anchoredPosition = new Vector2(0f, -34f);
        Image buttonBackground = buttonRect.gameObject.AddComponent<Image>();
        buttonBackground.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        reviveButton = buttonRect.gameObject.AddComponent<Button>();
        reviveButton.targetGraphic = buttonBackground;
        reviveButton.onClick.AddListener(RevivePlayer);
        NewCombatText("Label", buttonRect, new Vector2(100f, 30f), font).text = "부활";
        shownPlayerHealth = shownPlayerMaximum = -1f;
    }

    private static RectTransform NewCombatRect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return rect;
    }

    private static TMP_Text NewCombatText(string name, Transform parent, Vector2 size, TMP_FontAsset font)
    {
        TMP_Text text = NewCombatRect(name, parent, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = 15f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private void RevivePlayer() => GameManager.Instance?.Player?.Revive();

    private void ClearCombatHud()
    {
        if (combatHud == null) return;
        combatHud.gameObject.SetActive(false);
        Destroy(combatHud.gameObject);
        combatHud = null;
    }
}
