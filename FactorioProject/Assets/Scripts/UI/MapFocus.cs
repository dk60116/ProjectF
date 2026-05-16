using UnityEngine;

public class MapFocus : MonoBehaviour
{
    private const string OverlayShaderName = "Custom/MapFocusOverlay";
    public static readonly Color DefaultFocusColor = new Color(1f, 0.86f, 0f, 0.45f);
    public static readonly Color MouseFocusColor = new Color(1f, 1f, 1f, 0.45f);

    [SerializeField]
    private SpriteRenderer render;
    [SerializeField]
    private Color focusColor = new Color(1f, 0.86f, 0f, 0.45f);

    private static Material overlayMaterial;

    private void Awake()
    {
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        ApplyOverlayRendering();
    }

    public void SetVisible(bool isVisible)
    {
        SetVisible(isVisible, focusColor);
    }

    public void SetVisible(bool isVisible, Color color)
    {
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        focusColor = NormalizeFocusColor(color, DefaultFocusColor);
        ApplyOverlayRendering();

        if (gameObject.activeSelf != isVisible)
        {
            gameObject.SetActive(isVisible);
        }

        if (render != null)
        {
            render.enabled = isVisible;
        }
    }

    private void ApplyOverlayRendering()
    {
        if (render == null)
        {
            return;
        }

        if (overlayMaterial == null)
        {
            Shader overlayShader = Shader.Find(OverlayShaderName);
            if (overlayShader != null)
            {
                overlayMaterial = new Material(overlayShader)
                {
                    name = "MapFocusOverlay (Runtime)"
                };
            }
        }

        if (overlayMaterial != null && render.sharedMaterial != overlayMaterial)
        {
            render.sharedMaterial = overlayMaterial;
        }

        render.sortingOrder = 5000;
        render.color = focusColor;
    }

    private void OnValidate()
    {
        focusColor = NormalizeFocusColor(focusColor, DefaultFocusColor);

        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        if (render != null)
        {
            render.color = focusColor;
        }
    }

    private static Color NormalizeFocusColor(Color color, Color fallback)
    {
        return color.a > 0f ? color : fallback;
    }
}
