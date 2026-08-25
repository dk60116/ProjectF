using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AnimalWorldHealthBar : MonoBehaviour
{
    private static readonly Color BackgroundColor = new Color(0.04f, 0.04f, 0.04f, 0.85f);
    private static readonly Color HealthColor = new Color(0.78f, 0.12f, 0.1f, 1f);
    private static readonly Vector2 BarSize = new Vector2(0.9f, 0.1f);
    private const float HeightPadding = 0.25f;
    private const float FullHealthHideDelay = 1f;
    private const float FullHealthThreshold = 0.9999f;

    private Animal animal;
    private Image healthFill;
    private RectTransform healthFillRect;
    private Camera targetCamera;
    private float heightOffset = 1f;
    private float fullHealthHideTime = -1f;
    private bool healthStateInitialized;
    private bool wasAtFullHealth;

    public static AnimalWorldHealthBar Create(Animal animal, Renderer modelRenderer)
    {
        if (animal == null)
        {
            return null;
        }

        TerrainAnimalInstance terrainInstance = animal.GetComponentInParent<TerrainAnimalInstance>();
        Transform parent = terrainInstance != null
            ? terrainInstance.transform
            : animal.transform.parent != null
                ? animal.transform.parent
                : animal.transform;

        GameObject root = new GameObject(
            "Animal Health Bar",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(Image));
        root.transform.SetParent(parent, false);

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = BarSize;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        Image background = root.GetComponent<Image>();
        background.color = BackgroundColor;
        background.raycastTarget = false;

        GameObject fillObject = new GameObject(
            "Fill",
            typeof(RectTransform),
            typeof(Image));
        fillObject.transform.SetParent(root.transform, false);

        RectTransform fillRect = (RectTransform)fillObject.transform;
        fillRect.anchorMin = new Vector2(0.04f, 0.18f);
        fillRect.anchorMax = new Vector2(0.96f, 0.82f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fill = fillObject.GetComponent<Image>();
        fill.color = HealthColor;
        fill.raycastTarget = false;

        AnimalWorldHealthBar healthBar = root.AddComponent<AnimalWorldHealthBar>();
        root.SetActive(false);
        healthBar.Initialize(animal, modelRenderer, fill, fillRect);
        return healthBar;
    }

    public void NotifyAttackAnimationStarted()
    {
        if (animal == null || !animal.IsAlive)
        {
            return;
        }

        bool atFullHealth = animal.NormalizedHealth >= FullHealthThreshold;
        fullHealthHideTime = atFullHealth
            ? Time.unscaledTime + FullHealthHideDelay
            : -1f;
        SetRenderedVisible(true);
    }

    public void HideImmediately()
    {
        fullHealthHideTime = -1f;
        SetRenderedVisible(false);
    }

    private void SetRenderedVisible(bool visible)
    {
        visible &= animal != null && animal.IsAlive;
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }

        if (visible)
        {
            UpdateTransform();
        }
    }

    public void Refresh()
    {
        if (animal == null || healthFill == null || healthFillRect == null)
        {
            return;
        }

        float normalizedHealth = animal.NormalizedHealth;
        healthFillRect.anchorMax = new Vector2(
            Mathf.Lerp(0.04f, 0.96f, normalizedHealth),
            0.82f);
        healthFill.enabled = normalizedHealth > 0f;
        RefreshVisibility(normalizedHealth);
    }

    private void RefreshVisibility(float normalizedHealth)
    {
        if (!animal.IsAlive)
        {
            HideImmediately();
            return;
        }

        bool atFullHealth = normalizedHealth >= FullHealthThreshold;
        if (!atFullHealth)
        {
            fullHealthHideTime = -1f;
            SetRenderedVisible(true);
        }
        else if (healthStateInitialized && !wasAtFullHealth)
        {
            fullHealthHideTime = Time.unscaledTime + FullHealthHideDelay;
            SetRenderedVisible(true);
        }

        if (atFullHealth
            && fullHealthHideTime >= 0f
            && Time.unscaledTime >= fullHealthHideTime)
        {
            fullHealthHideTime = -1f;
            SetRenderedVisible(false);
        }

        wasAtFullHealth = atFullHealth;
        healthStateInitialized = true;
    }

    private void Initialize(
        Animal owner,
        Renderer modelRenderer,
        Image fill,
        RectTransform fillRect)
    {
        animal = owner;
        healthFill = fill;
        healthFillRect = fillRect;
        if (modelRenderer != null)
        {
            heightOffset = Mathf.Max(
                0.5f,
                modelRenderer.bounds.max.y - animal.transform.position.y + HeightPadding);
        }

        Refresh();
        UpdateTransform();
    }

    private void LateUpdate()
    {
        if (animal == null)
        {
            return;
        }

        Refresh();
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        Vector3 animalPosition = animal.transform.position;
        transform.position = new Vector3(
            animalPosition.x,
            animalPosition.y + heightOffset,
            animalPosition.z);

        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            transform.rotation = targetCamera.transform.rotation;
        }
    }
}
