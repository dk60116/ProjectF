using TMPro;
using ProjectF.Attributes;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

public class ItemSlot : MonoBehaviour
{
    [SerializeField, ReadOnly]
    protected int id;

    [SerializeField]
    private Image frame;

    [SerializeField]
    [FormerlySerializedAs("Icon")]
    private Image icon;

    [SerializeField]
    private TextMeshProUGUI count;

    [SerializeField]
    private bool keepIconWhenEmpty;

    private void Awake()
    {
        ResolveReferences();
        Clear();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    public virtual void SetItem(int itemId, int itemCount, int maxItemCount = 0)
    {
        SetItemDisplay(itemId, itemCount, maxItemCount, false);
    }

    public void SetItemDisplay(int itemId, int itemCount, int maxItemCount, bool allowZeroCount)
    {
        ResolveReferences();
        id = itemId;

        bool hasItem = itemId >= 0 && (allowZeroCount || itemCount > 0);

        if (!hasItem && keepIconWhenEmpty)
        {
            if (icon != null)
            {
                icon.enabled = icon.sprite != null;
                if (!icon.gameObject.activeSelf)
                {
                    icon.gameObject.SetActive(true);
                }
            }

            if (count != null)
            {
                count.text = string.Empty;
                if (count.gameObject.activeSelf)
                {
                    count.gameObject.SetActive(false);
                }
            }

            return;
        }

        if (icon != null)
        {
            icon.enabled = hasItem;
            icon.sprite = null;
            if (icon.gameObject.activeSelf != hasItem)
            {
                icon.gameObject.SetActive(hasItem);
            }
        }

        if (count != null)
        {
            count.text = hasItem
                ? (maxItemCount > 0 ? $"{itemCount} / {maxItemCount}" : itemCount.ToString())
                : string.Empty;
            if (count.gameObject.activeSelf != hasItem)
            {
                count.gameObject.SetActive(hasItem);
            }
        }

        if (!hasItem)
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return;
        }

        if (!GameManager.Instance.ItemManger.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            return;
        }

        if (icon != null)
        {
            icon.sprite = itemSet.icon;
            icon.enabled = itemSet.icon != null;
        }
    }

    public virtual void Clear()
    {
        SetItem(-1, 0, 0);
    }

    public void SetIconAlpha(float alpha)
    {
        ResolveReferences();
        if (icon == null)
        {
            return;
        }

        Color color = icon.color;
        color.a = Mathf.Clamp01(alpha);
        icon.color = color;
    }

    protected Image IconImage
    {
        get
        {
            ResolveReferences();
            return icon;
        }
    }

    public int ItemId => id;
    public bool HasItem => id >= 0;

    public virtual bool CanDragDrop => false;

    private void ResolveReferences()
    {
        if (frame != null && !IsLocalComponent(frame))
        {
            frame = null;
        }

        if (icon == null)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i] != frame)
                {
                    icon = images[i];
                    break;
                }
            }
        }
        else if (!IsLocalComponent(icon))
        {
            icon = null;
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i] != frame)
                {
                    icon = images[i];
                    break;
                }
            }
        }

        if (count == null || !IsLocalComponent(count))
        {
            count = GetComponentInChildren<TextMeshProUGUI>(true);
        }
    }

    private bool IsLocalComponent(Component component)
    {
        if (component == null)
        {
            return false;
        }

        Transform target = component.transform;
        return target == transform || target.IsChildOf(transform);
    }
}
