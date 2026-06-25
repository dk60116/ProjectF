using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrainFilter : MonoBehaviour
{
    private static readonly string[] FuelOptions =
    {
        "Free",
        "Full"
    };

    private static readonly string[] FreightOptions =
    {
        "Free",
        "Full",
        "Empty"
    };

    [SerializeField]
    private Image trainIcon;
    [SerializeField]
    private Toggle autoDriaveToggle;
    [SerializeField]
    private TMP_Dropdown targetA, targetB;
    [SerializeField]
    private TMP_Dropdown fuel, freight;

    private SteamTrain boundTrain;
    private readonly List<string> stationNameScratch = new List<string>(8);
    private readonly List<TMP_Dropdown.OptionData> stationOptionScratch = new List<TMP_Dropdown.OptionData>(8);
    private readonly List<TMP_Dropdown.OptionData> filterOptionScratch = new List<TMP_Dropdown.OptionData>(4);

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
        if (trainIcon != null)
        {
            Sprite icon = ResolveTrainIcon(boundTrain);
            trainIcon.sprite = icon;
            trainIcon.enabled = icon != null;
        }

        RefreshStationTargetDropdowns();
        RefreshFilterDropdowns();
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

    private void RefreshStationTargetDropdowns()
    {
        stationNameScratch.Clear();

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null)
        {
            terrain.CollectTrainStationNamesOnSameRailLine(boundTrain, stationNameScratch);
        }

        RefreshStationTargetDropdown(targetA);
        RefreshStationTargetDropdown(targetB);
    }

    private void RefreshStationTargetDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
        {
            return;
        }

        string previouslySelectedStationName = ResolveSelectedOptionText(dropdown);
        stationOptionScratch.Clear();
        for (int i = 0; i < stationNameScratch.Count; i++)
        {
            stationOptionScratch.Add(new TMP_Dropdown.OptionData(stationNameScratch[i]));
        }

        dropdown.ClearOptions();
        if (stationOptionScratch.Count > 0)
        {
            dropdown.AddOptions(stationOptionScratch);
            dropdown.SetValueWithoutNotify(ResolveStationOptionIndex(previouslySelectedStationName));
        }

        dropdown.RefreshShownValue();
    }

    private void RefreshFilterDropdowns()
    {
        RefreshFixedOptionDropdown(fuel, FuelOptions);
        RefreshFixedOptionDropdown(freight, FreightOptions);
    }

    private void RefreshFixedOptionDropdown(TMP_Dropdown dropdown, IReadOnlyList<string> options)
    {
        if (dropdown == null)
        {
            return;
        }

        string previouslySelectedOption = ResolveSelectedOptionText(dropdown);
        filterOptionScratch.Clear();
        if (options != null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                filterOptionScratch.Add(new TMP_Dropdown.OptionData(options[i]));
            }
        }

        dropdown.ClearOptions();
        if (filterOptionScratch.Count > 0)
        {
            dropdown.AddOptions(filterOptionScratch);
            dropdown.SetValueWithoutNotify(ResolveOptionIndex(previouslySelectedOption, options));
        }

        dropdown.RefreshShownValue();
    }

    private int ResolveStationOptionIndex(string stationName)
    {
        return ResolveOptionIndex(stationName, stationNameScratch);
    }

    private static int ResolveOptionIndex(string optionText, IReadOnlyList<string> options)
    {
        if (options == null || options.Count <= 0)
        {
            return 0;
        }

        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], optionText, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static string ResolveSelectedOptionText(TMP_Dropdown dropdown)
    {
        if (dropdown == null
            || dropdown.options == null
            || dropdown.value < 0
            || dropdown.value >= dropdown.options.Count)
        {
            return string.Empty;
        }

        TMP_Dropdown.OptionData option = dropdown.options[dropdown.value];
        return option != null ? option.text : string.Empty;
    }
}
