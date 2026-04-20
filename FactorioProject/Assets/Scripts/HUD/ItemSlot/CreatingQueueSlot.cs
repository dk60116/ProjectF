using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CreatingQueueSlot : ItemSlot
{
    [SerializeField]
    private Image fill;
    [SerializeField]
    private Button cancelButton;
    private Action cancelAction;

    private void Awake()
    {
        ResolveCancelButton();
    }

    public void SetFill(float value)
    {
        ResolveFill();
        if (fill == null)
        {
            return;
        }

        fill.fillAmount = Mathf.Clamp01(value);
    }

    public override void Clear()
    {
        base.Clear();
        SetFill(0f);
        BindCancelAction(null);
    }

    public void BindCancelAction(Action action)
    {
        ResolveCancelButton();
        cancelAction = action;
        if (cancelButton == null)
        {
            return;
        }

        cancelButton.onClick.RemoveListener(HandleCancelClicked);
        if (cancelAction != null)
        {
            cancelButton.onClick.AddListener(HandleCancelClicked);
        }

        cancelButton.interactable = cancelAction != null;
    }

    public void SetCancelInteractable(bool interactable)
    {
        ResolveCancelButton();
        if (cancelButton == null)
        {
            return;
        }

        cancelButton.interactable = interactable && cancelAction != null;
    }

    private void ResolveFill()
    {
        if (fill != null)
        {
            return;
        }

        Transform target = transform.Find("Image");
        if (target != null)
        {
            fill = target.GetComponent<Image>();
        }

        if (fill != null)
        {
            return;
        }

        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image candidate = images[i];
            if (candidate == null || candidate == IconImage)
            {
                continue;
            }

            fill = candidate;
            break;
        }
    }

    private void ResolveCancelButton()
    {
        if (cancelButton != null)
        {
            return;
        }

        Transform target = transform.Find("CancelButton");
        if (target != null)
        {
            cancelButton = target.GetComponent<Button>();
        }

        if (cancelButton != null)
        {
            return;
        }

        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null)
            {
                continue;
            }

            cancelButton = candidate;
            break;
        }
    }

    private void HandleCancelClicked()
    {
        cancelAction?.Invoke();
    }
}
