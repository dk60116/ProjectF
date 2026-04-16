using System.Collections.Generic;
using UnityEngine;

public class UIManager : MonoBehaviour
{
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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (playerHUD == null)
        {
            playerHUD = GetComponentInChildren<PlayerHUD>(true);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void BindPlayerBag(PlayerBag bag)
    {
        if (playerHUD == null)
        {
            playerHUD = GetComponentInChildren<PlayerHUD>(true);
        }

        playerHUD?.Bind(bag);
    }

    public PlayerHUD PlayerHUD => playerHUD;
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
        if (gauge == null)
        {
            return;
        }

        RectTransform canvasRect = ResolveWorldCanvasRect();
        if (canvasRect == null || !TryConvertWorldToCanvasPoint(worldPosition, canvasRect, out Vector2 anchoredPosition))
        {
            gauge.SetVisible(false);
            return;
        }

        gauge.SetVisible(true);
        gauge.SetAnchoredPosition(anchoredPosition);
        gauge.SetFill(fillAmount);
    }

    private RectTransform ResolveEnergyGaugeRoot()
    {
        if (energyGaugeRoot != null)
        {
            return energyGaugeRoot;
        }

        RectTransform canvasRect = ResolveWorldCanvasRect();
        if (canvasRect == null)
        {
            return null;
        }

        Transform existing = canvasRect.Find("WorldGaugeRoot");
        if (existing != null)
        {
            energyGaugeRoot = existing as RectTransform;
            return energyGaugeRoot;
        }

        GameObject rootObject = new GameObject("WorldGaugeRoot", typeof(RectTransform));
        energyGaugeRoot = rootObject.GetComponent<RectTransform>();
        energyGaugeRoot.SetParent(canvasRect, false);
        energyGaugeRoot.anchorMin = Vector2.zero;
        energyGaugeRoot.anchorMax = Vector2.one;
        energyGaugeRoot.offsetMin = Vector2.zero;
        energyGaugeRoot.offsetMax = Vector2.zero;
        energyGaugeRoot.pivot = new Vector2(0.5f, 0.5f);
        energyGaugeRoot.localScale = Vector3.one;
        energyGaugeRoot.localRotation = Quaternion.identity;
        return energyGaugeRoot;
    }

    private RectTransform ResolveWorldCanvasRect()
    {
        if (cachedWorldCanvasRect != null)
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

    private bool TryConvertWorldToCanvasPoint(Vector3 worldPosition, RectTransform canvasRect, out Vector2 anchoredPosition)
    {
        anchoredPosition = Vector2.zero;
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
            canvasRect,
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
}
