using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrainFilter : MonoBehaviour
{
    [SerializeField]
    private Image trainIcon;
    [SerializeField]
    private Toggle autoDriaveToggle;
    [SerializeField]
    private TMP_Dropdown targetA, targetB;
    [SerializeField]
    private TMP_Dropdown fuel, freight;

    private SteamTrain boundTrain;

    private void OnEnable()
    {
        Refresh();
    }

    public void Bind(SteamTrain steamTrain)
    {
        boundTrain = steamTrain;
        Refresh();
    }

    public bool TryGetBoundTarget(out SteamTrain train)
    {
        train = boundTrain;
        return train != null && train.gameObject.activeInHierarchy;
    }

    public void Refresh()
    {
        if (trainIcon == null)
        {
            return;
        }

        Sprite icon = ResolveTrainIcon(boundTrain);
        trainIcon.sprite = icon;
        trainIcon.enabled = icon != null;
    }

    private static Sprite ResolveTrainIcon(SteamTrain train)
    {
        if (train == null)
        {
            return null;
        }

        ItemDefinition definition = train.BoundItemDefinition != null
            ? train.BoundItemDefinition
            : InputOutputModule.ResolveItemDefinition(train.ResolveItemId());
        if (definition != null && definition.icon != null)
        {
            return definition.icon;
        }

        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        return itemManager != null && itemManager.TryGetItemSetById(train.ResolveItemId(), out ItemManager.ItemSet itemSet)
            ? itemSet.icon
            : null;
    }
}
