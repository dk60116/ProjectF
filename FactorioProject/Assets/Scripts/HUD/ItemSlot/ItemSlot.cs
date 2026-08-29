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
        SetItemDisplay(itemId, itemCount, maxItemCount, allowZeroCount, showCount, null);
    }

    public void SetItemDisplay(
        int itemId,
        int itemCount,
        int maxItemCount,
        bool allowZeroCount,
        bool showCount,
        string displayNameOverride)
    {
        SetItemDisplay(
            itemId,
            itemCount,
            maxItemCount,
            allowZeroCount,
            showCount,
            displayNameOverride,
            null);
    }

    public void SetItemDisplay(
        int itemId,
        int itemCount,
        int maxItemCount,
        bool allowZeroCount,
        bool showCount,
        string displayNameOverride,
        Sprite displayIconOverride)
    {
        ResolveReferences();
        id = itemId;

        bool hasItem = itemId >= 0 && (allowZeroCount || itemCount > 0);
        bool shouldShowCount = hasItem && showCount;
        int displayMaxItemCount = ResolveDisplayMaxItemCount(itemId, maxItemCount);

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
                ? (displayMaxItemCount > 0
                    ? $"{itemCount} / {displayMaxItemCount}"
                    : itemCount.ToString())
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
            Sprite displayIcon = displayIconOverride != null
                ? displayIconOverride
                : itemSet.icon;
            icon.sprite = displayIcon;
            icon.enabled = displayIcon != null;
        }

        SetItemNameText(
            string.IsNullOrWhiteSpace(displayNameOverride) ? itemSet.name : displayNameOverride,
            true);
    }

    private static int ResolveDisplayMaxItemCount(int itemId, int maxItemCount)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return ItemDefinition.ResolveStackCapacity(itemManager, itemId, maxItemCount);
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

    public void SetCustomDisplay(Sprite displayIcon, string displayName, string countText)
    {
        SetCustomDisplay(-1, displayIcon, displayName, countText);
    }

    public void SetCustomDisplay(int itemId, Sprite displayIcon, string displayName, string countText)
    {
        ResolveReferences();
        id = itemId;

        bool hasIcon = displayIcon != null;
        if (icon != null)
        {
            if (icon.sprite != displayIcon)
            {
                icon.sprite = displayIcon;
            }

            icon.preserveAspect = true;

            if (icon.enabled != hasIcon)
            {
                icon.enabled = hasIcon;
            }

            if (icon.gameObject.activeSelf != hasIcon)
            {
                icon.gameObject.SetActive(hasIcon);
            }
        }

        bool hasCountText = !string.IsNullOrWhiteSpace(countText);
        if (count != null)
        {
            string resolvedCountText = hasCountText ? countText : string.Empty;
            if (count.text != resolvedCountText)
            {
                count.text = resolvedCountText;
            }

            if (count.gameObject.activeSelf != hasCountText)
            {
                count.gameObject.SetActive(hasCountText);
            }
        }

        SetItemNameText(displayName, !string.IsNullOrWhiteSpace(displayName));
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
        if (itemName.text != normalizedName)
        {
            itemName.text = normalizedName;
        }

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
