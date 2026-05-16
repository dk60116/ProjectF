using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class UIManager : MonoBehaviour
{
    private struct WorldGaugeBinding
    {
        public Vector3 worldPosition;
        public Vector2 anchoredOffset;
    }

    public static UIManager Instance { get; private set; }

    [SerializeField]
    private PlayerHUD playerHUD;

    [SerializeField]
    private Sprite arrowImage;
    [SerializeField]
    private DefaultGauge energyGauge;

    private readonly Stack<DefaultGauge> energyGaugePool = new Stack<DefaultGauge>();
    private Canvas cachedWorldCanvas;
    private RectTransform cachedWorldCanvasRect;
    private RectTransform energyGaugeRoot;
    private Camera cachedWorldCanvasCamera;
    private readonly Dictionary<DefaultGauge, WorldGaugeBinding> activeWorldGaugeBindings = new Dictionary<DefaultGauge, WorldGaugeBinding>();
    private readonly List<DefaultGauge> worldGaugeCleanupBuffer = new List<DefaultGauge>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        EnsurePlayerHUDReference();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void LateUpdate()
    {
        RefreshActiveWorldGaugePositions();
    }

    public void BindPlayerBag(PlayerBag bag)
    {
        EnsurePlayerHUDReference();

        playerHUD?.Bind(bag);
    }

    public PlayerHUD PlayerHUD => playerHUD;
    public RectTransform HudRoot => ResolveHudRect();
    public RectTransform HudGaugeRoot => ResolveEnergyGaugeRoot();
    public Sprite ArrowImage => arrowImage;

    public DefaultGauge AcquireEnergyGauge()
    {
        if (energyGauge == null)
        {
            return null;
        }

        RectTransform root = ResolveEnergyGaugeRoot();
        if (root == null)
        {
            return null;
        }

        DefaultGauge gauge = energyGaugePool.Count > 0 ? energyGaugePool.Pop() : Instantiate(energyGauge, root);
        if (gauge == null)
        {
            return null;
        }

        gauge.transform.SetParent(root, false);
        gauge.ResetVisual();
        gauge.SetVisible(true);
        return gauge;
    }

    public void ReleaseEnergyGauge(DefaultGauge gauge)
    {
        if (gauge == null)
        {
            return;
        }

        activeWorldGaugeBindings.Remove(gauge);
        RectTransform root = ResolveEnergyGaugeRoot();
        if (root != null)
        {
            gauge.transform.SetParent(root, false);
        }

        gauge.ResetVisual();
        gauge.SetVisible(false);
        energyGaugePool.Push(gauge);
    }

    public void UpdateEnergyGauge(DefaultGauge gauge, Vector3 worldPosition, float fillAmount)
    {
        UpdateEnergyGauge(gauge, worldPosition, fillAmount, Vector2.zero);
    }

    public void UpdateEnergyGauge(DefaultGauge gauge, Vector3 worldPosition, float fillAmount, Vector2 anchoredOffset)
    {
        if (gauge == null)
        {
            return;
        }

        WorldGaugeBinding binding = new WorldGaugeBinding
        {
            worldPosition = worldPosition,
            anchoredOffset = anchoredOffset
        };
        activeWorldGaugeBindings[gauge] = binding;

        if (!TryApplyEnergyGaugePosition(gauge, binding))
        {
            return;
        }

        gauge.SetFill(fillAmount);
    }

    private void RefreshActiveWorldGaugePositions()
    {
        if (activeWorldGaugeBindings.Count <= 0)
        {
            return;
        }

        worldGaugeCleanupBuffer.Clear();
        foreach (KeyValuePair<DefaultGauge, WorldGaugeBinding> pair in activeWorldGaugeBindings)
        {
            DefaultGauge gauge = pair.Key;
            if (gauge == null)
            {
                worldGaugeCleanupBuffer.Add(gauge);
                continue;
            }

            TryApplyEnergyGaugePosition(gauge, pair.Value);
        }

        for (int i = 0; i < worldGaugeCleanupBuffer.Count; i++)
        {
            activeWorldGaugeBindings.Remove(worldGaugeCleanupBuffer[i]);
        }

        worldGaugeCleanupBuffer.Clear();
    }

    private bool TryApplyEnergyGaugePosition(DefaultGauge gauge, WorldGaugeBinding binding)
    {
        RectTransform root = ResolveEnergyGaugeRoot();
        if (root == null || !TryConvertWorldToCanvasPoint(binding.worldPosition, root, out Vector2 anchoredPosition))
        {
            gauge.SetVisible(false);
            return false;
        }

        gauge.SetVisible(true);
        gauge.SetAnchoredPosition(anchoredPosition + binding.anchoredOffset);
        return true;
    }

    private RectTransform ResolveEnergyGaugeRoot()
    {
        RectTransform parentRect = ResolveHudRect();
        if (parentRect == null)
        {
            parentRect = ResolveWorldCanvasRect();
        }

        if (parentRect == null)
        {
            return null;
        }

        if (energyGaugeRoot != null)
        {
            if (energyGaugeRoot.parent != parentRect)
            {
                energyGaugeRoot.SetParent(parentRect, false);
            }

            StretchToParent(energyGaugeRoot);
            energyGaugeRoot.SetAsFirstSibling();
            return energyGaugeRoot;
        }

        Transform existing = parentRect.Find("WorldGaugeRoot");
        if (existing != null)
        {
            energyGaugeRoot = existing as RectTransform;
            StretchToParent(energyGaugeRoot);
            energyGaugeRoot.SetAsFirstSibling();
            return energyGaugeRoot;
        }

        GameObject rootObject = new GameObject("WorldGaugeRoot", typeof(RectTransform));
        energyGaugeRoot = rootObject.GetComponent<RectTransform>();
        energyGaugeRoot.SetParent(parentRect, false);
        StretchToParent(energyGaugeRoot);
        energyGaugeRoot.SetAsFirstSibling();
        return energyGaugeRoot;
    }

    private RectTransform ResolveWorldCanvasRect()
    {
        if (cachedWorldCanvasRect != null)
        {
            return cachedWorldCanvasRect;
        }

        RectTransform hudRect = ResolveHudRect();
        if (hudRect != null && cachedWorldCanvasRect != null)
        {
            return cachedWorldCanvasRect;
        }

        cachedWorldCanvas = null;
        Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null)
            {
                continue;
            }

            cachedWorldCanvas = canvases[i].rootCanvas != null ? canvases[i].rootCanvas : canvases[i];
            break;
        }

        if (cachedWorldCanvas == null)
        {
            cachedWorldCanvas = FindObjectOfType<Canvas>();
            if (cachedWorldCanvas != null && cachedWorldCanvas.rootCanvas != null)
            {
                cachedWorldCanvas = cachedWorldCanvas.rootCanvas;
            }
        }

        cachedWorldCanvasRect = cachedWorldCanvas != null ? cachedWorldCanvas.transform as RectTransform : null;
        cachedWorldCanvasCamera = cachedWorldCanvas != null ? cachedWorldCanvas.worldCamera : null;
        return cachedWorldCanvasRect;
    }

    private bool TryConvertWorldToCanvasPoint(Vector3 worldPosition, RectTransform targetRect, out Vector2 anchoredPosition)
    {
        anchoredPosition = Vector2.zero;
        CacheCanvasFromTransform(targetRect);
        Camera camera = ResolveWorldCanvasCamera();
        Vector3 screenPoint = camera != null
            ? camera.WorldToScreenPoint(worldPosition)
            : RectTransformUtility.WorldToScreenPoint(null, worldPosition);

        if (screenPoint.z < 0f)
        {
            return false;
        }

        Camera conversionCamera = cachedWorldCanvas != null && cachedWorldCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : camera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            targetRect,
            screenPoint,
            conversionCamera,
            out anchoredPosition);
    }

    private Camera ResolveWorldCanvasCamera()
    {
        if (cachedWorldCanvas != null && cachedWorldCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            if (cachedWorldCanvasCamera == null)
            {
                cachedWorldCanvasCamera = cachedWorldCanvas.worldCamera;
            }

            if (cachedWorldCanvasCamera != null)
            {
                return cachedWorldCanvasCamera;
            }
        }

        if (Camera.main != null)
        {
            return Camera.main;
        }

        return FindObjectOfType<Camera>();
    }

    private void EnsurePlayerHUDReference()
    {
        if (playerHUD != null)
        {
            return;
        }

        playerHUD = GetComponentInChildren<PlayerHUD>(true);
        if (playerHUD == null)
        {
            playerHUD = FindObjectOfType<PlayerHUD>(true);
        }
    }

    private RectTransform ResolveHudRect()
    {
        EnsurePlayerHUDReference();
        RectTransform hudRect = playerHUD != null ? playerHUD.transform as RectTransform : null;
        if (hudRect != null)
        {
            CacheCanvasFromTransform(hudRect);
        }

        return hudRect;
    }

    private void CacheCanvasFromTransform(Transform target)
    {
        if (target == null)
        {
            return;
        }

        Canvas canvas = target.GetComponentInParent<Canvas>(true);
        if (canvas == null)
        {
            return;
        }

        cachedWorldCanvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        cachedWorldCanvasRect = cachedWorldCanvas.transform as RectTransform;
        cachedWorldCanvasCamera = cachedWorldCanvas.worldCamera;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }
}
