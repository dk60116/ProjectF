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
    private TextMeshProUGUI itemName;

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
        SetItemDisplay(itemId, itemCount, maxItemCount, allowZeroCount, true);
    }

    public void SetItemDisplay(int itemId, int itemCount, int maxItemCount, bool allowZeroCount, bool showCount)
    {
        ResolveReferences();
        id = itemId;

        bool hasItem = itemId >= 0 && (allowZeroCount || itemCount > 0);
        bool shouldShowCount = hasItem && showCount;

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

            SetItemNameText(null, false);
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
            count.text = shouldShowCount
                ? (maxItemCount > 0 ? $"{itemCount} / {maxItemCount}" : itemCount.ToString())
                : string.Empty;
            if (count.gameObject.activeSelf != shouldShowCount)
            {
                count.gameObject.SetActive(shouldShowCount);
            }
        }
        SetItemNameText(null, false);

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

        SetItemNameText(itemSet.name, true);
    }

    public void SetEmptyCountDisplay(int itemCount, int maxItemCount)
    {
        ResolveReferences();
        id = -1;

        if (icon != null)
        {
            icon.enabled = false;
            icon.sprite = null;
            if (icon.gameObject.activeSelf)
            {
                icon.gameObject.SetActive(false);
            }
        }

        int displayCount = Mathf.Max(0, itemCount);
        int displayMaxCount = Mathf.Max(1, maxItemCount, displayCount);
        if (count != null)
        {
            count.text = $"{displayCount} / {displayMaxCount}";
            if (!count.gameObject.activeSelf)
            {
                count.gameObject.SetActive(true);
            }
        }

        SetItemNameText(null, false);
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

    protected TextMeshProUGUI CountLabel
    {
        get
        {
            ResolveReferences();
            return count;
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

        if (itemName == null)
        {
            itemName = FindTextComponentByName(
                "ItemName",
                "Item Name",
                "ItemNameText",
                "Item Name Text",
                "NameText",
                "Name Text",
                "ObjectName",
                "Object Name");
        }

        if (count != null
            && (count == itemName || IsNameTextComponent(count)))
        {
            count = null;
        }

        if (count == null)
        {
            TextMeshProUGUI namedCount = FindTextComponentByName(
                "Count",
                "CountText",
                "Count Text",
                "ItemCount",
                "Item Count",
                "CreateCount",
                "Create Count");
            if (namedCount != null && namedCount != itemName)
            {
                count = namedCount;
            }
        }

        if (count == null)
        {
            TextMeshProUGUI[] textComponents = GetTextComponentsForReferenceSearch();
            for (int i = 0; i < textComponents.Length; i++)
            {
                TextMeshProUGUI candidate = textComponents[i];
                if (candidate == null || candidate == itemName || IsNameTextComponent(candidate))
                {
                    continue;
                }

                count = candidate;
                break;
            }
        }
    }

    private TextMeshProUGUI FindTextComponentByName(params string[] candidateNames)
    {
        if (candidateNames == null || candidateNames.Length <= 0)
        {
            return null;
        }

        TextMeshProUGUI[] textComponents = GetTextComponentsForReferenceSearch();
        for (int i = 0; i < textComponents.Length; i++)
        {
            TextMeshProUGUI candidate = textComponents[i];
            if (candidate == null)
            {
                continue;
            }

            for (int nameIndex = 0; nameIndex < candidateNames.Length; nameIndex++)
            {
                if (IsTextComponentNamed(candidate, candidateNames[nameIndex]))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private TextMeshProUGUI[] GetTextComponentsForReferenceSearch()
    {
        Transform searchRoot = ResolveSingleSlotReferenceRoot();
        return searchRoot != null
            ? searchRoot.GetComponentsInChildren<TextMeshProUGUI>(true)
            : GetComponentsInChildren<TextMeshProUGUI>(true);
    }

    private Transform ResolveSingleSlotReferenceRoot()
    {
        Transform current = transform.parent;
        while (current != null)
        {
            ItemSlot[] slots = current.GetComponentsInChildren<ItemSlot>(true);
            int slotCount = 0;
            bool containsThisSlot = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    continue;
                }

                slotCount++;
                containsThisSlot |= slots[i] == this;
            }

            if (containsThisSlot && slotCount == 1)
            {
                return current;
            }

            if (containsThisSlot && slotCount > 1)
            {
                break;
            }

            current = current.parent;
        }

        return transform;
    }

    private static bool IsNameTextComponent(TextMeshProUGUI candidate)
    {
        return IsTextComponentNamed(
            candidate,
            "ItemName",
            "Item Name",
            "ItemNameText",
            "Item Name Text",
            "NameText",
            "Name Text",
            "ObjectName",
            "Object Name");
    }

    private static bool IsTextComponentNamed(TextMeshProUGUI candidate, params string[] names)
    {
        if (candidate == null || names == null)
        {
            return false;
        }

        string normalizedCandidateName = NormalizeTextComponentName(candidate.name);
        for (int i = 0; i < names.Length; i++)
        {
            if (normalizedCandidateName == NormalizeTextComponentName(names[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTextComponentName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private void SetItemNameText(string displayName, bool hasItem)
    {
        if (itemName == null)
        {
            return;
        }

        string normalizedName = hasItem && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : string.Empty;
        itemName.text = normalizedName;
        bool showName = !string.IsNullOrEmpty(normalizedName);
        if (itemName.gameObject.activeSelf != showName)
        {
            itemName.gameObject.SetActive(showName);
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
