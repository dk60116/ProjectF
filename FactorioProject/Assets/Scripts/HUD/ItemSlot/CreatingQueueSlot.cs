using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CreatingQueueSlot : ItemSlot
{
    [SerializeField]
    private Image fill;

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
}
