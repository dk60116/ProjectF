using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InteractionButton : MonoBehaviour
{
    [SerializeField]
    private Image icon;

    private Button cachedButton;
    private UnityAction cachedClickAction;

    private void Awake()
    {
        cachedButton = GetComponent<Button>();
        ResolveIcon();
        SetVisible(false);
    }

    public void SetIcon(Sprite sprite)
    {
        ResolveIcon();
        if (icon == null)
        {
            return;
        }

        icon.sprite = sprite;
        icon.enabled = sprite != null;
    }

    private void ResolveIcon()
    {
        if (icon != null)
        {
            return;
        }

        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image candidate = images[i];
            if (candidate == null || candidate.gameObject == gameObject)
            {
                continue;
            }

            icon = candidate;
            return;
        }
    }

    public void SetVisible(bool isVisible)
    {
        if (gameObject.activeSelf != isVisible)
        {
            gameObject.SetActive(isVisible);
        }

        if (cachedButton == null)
        {
            cachedButton = GetComponent<Button>();
        }

        if (cachedButton != null)
        {
            cachedButton.interactable = isVisible;
        }
    }

    public void SetClickAction(UnityAction action)
    {
        if (cachedButton == null)
        {
            cachedButton = GetComponent<Button>();
        }

        if (cachedButton == null)
        {
            return;
        }

        if (cachedClickAction != null)
        {
            cachedButton.onClick.RemoveListener(cachedClickAction);
        }

        cachedClickAction = action;

        if (cachedClickAction != null)
        {
            cachedButton.onClick.AddListener(cachedClickAction);
        }
    }
}
