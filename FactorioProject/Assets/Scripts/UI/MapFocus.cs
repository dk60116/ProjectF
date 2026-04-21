using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapFocus : MonoBehaviour
{
    private const string OverlayShaderName = "Custom/MapFocusOverlay";

    [SerializeField]
    private SpriteRenderer render;

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
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

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
    }
}
