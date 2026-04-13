using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapFocus : MonoBehaviour
{
    [SerializeField]
    private SpriteRenderer render;

    private void Awake()
    {
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }
    }

    public void SetVisible(bool isVisible)
    {
        if (render == null)
        {
            render = GetComponent<SpriteRenderer>();
        }

        if (gameObject.activeSelf != isVisible)
        {
            gameObject.SetActive(isVisible);
        }

        if (render != null)
        {
            render.enabled = isVisible;
        }
    }
}
