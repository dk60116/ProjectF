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
        ResolveReferences();
        id = itemId;

        bool hasItem = itemId >= 0 && itemCount > 0;

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

    protected Image IconImage
    {
        get
        {
            ResolveReferences();
            return icon;
        }
    }

    private void ResolveReferences()
    {
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

        if (count == null)
        {
            count = GetComponentInChildren<TextMeshProUGUI>(true);
        }
    }
}
