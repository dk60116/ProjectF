using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrainFilter : MonoBehaviour
{
    private const string NoneOption = "None";
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

    private static TrainFilter activeVisibleFilter;
    private static int routeSelectionVersion;

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
        activeVisibleFilter = this;
        BindDropdownListeners();
        Refresh();
        MarkRouteSelectionDirty();
    }

    private void OnDisable()
    {
        UnbindDropdownListeners();
        if (activeVisibleFilter == this)
        {
            activeVisibleFilter = null;
            MarkRouteSelectionDirty();
        }
    }

    public void Bind(SteamTrain steamTrain)
    {
        boundTrain = steamTrain;
        Refresh();
        MarkRouteSelectionDirty();
    }

    public static int RouteSelectionVersion => routeSelectionVersion;

    public bool TryGetBoundTarget(out SteamTrain train)
    {
        train = boundTrain;
        return train != null && train.gameObject.activeInHierarchy;
    }

    public static bool TryGetActiveRouteSelection(
        out SteamTrain train,
        out string targetAStationName,
        out string targetBStationName)
    {
        train = null;
        targetAStationName = string.Empty;
        targetBStationName = string.Empty;

        if (activeVisibleFilter != null
            && activeVisibleFilter.gameObject.activeInHierarchy
            && activeVisibleFilter.TryGetBoundTarget(out train))
        {
            targetAStationName = NormalizeStationSelection(ResolveSelectedOptionText(activeVisibleFilter.targetA));
            targetBStationName = NormalizeStationSelection(ResolveSelectedOptionText(activeVisibleFilter.targetB));
            if (!string.IsNullOrWhiteSpace(targetAStationName)
                || !string.IsNullOrWhiteSpace(targetBStationName))
            {
                return true;
            }
        }

        if (!TryResolveCurrentRouteDisplayTrain(out train)
            || train == null)
        {
            return false;
        }

        targetAStationName = train.AutoDriveTargetAStationName;
        targetBStationName = train.AutoDriveTargetBStationName;
        return !string.IsNullOrWhiteSpace(targetAStationName)
               || !string.IsNullOrWhiteSpace(targetBStationName);
    }

    public void Refresh()
    {
        string previousTargetAStationName = NormalizeStationSelection(ResolveSelectedOptionText(targetA));
        string previousTargetBStationName = NormalizeStationSelection(ResolveSelectedOptionText(targetB));

        if (trainIcon != null)
        {
            Sprite icon = ResolveTrainIcon(boundTrain);
            trainIcon.sprite = icon;
            trainIcon.enabled = icon != null;
        }

        RefreshAutoDriveToggle();
        RefreshStationTargetDropdowns();
        RefreshFilterDropdowns();

        if (isActiveAndEnabled
            && (!string.Equals(previousTargetAStationName, NormalizeStationSelection(ResolveSelectedOptionText(targetA)), System.StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previousTargetBStationName, NormalizeStationSelection(ResolveSelectedOptionText(targetB)), System.StringComparison.OrdinalIgnoreCase)))
        {
            ApplyCurrentFilterStateToBoundTrain();
            MarkRouteSelectionDirty();
        }
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

        if (stationNameScratch.Count <= 0
            || !string.Equals(stationNameScratch[0], NoneOption, System.StringComparison.OrdinalIgnoreCase))
        {
            stationNameScratch.Insert(0, NoneOption);
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

        string previouslySelectedStationName = ResolvePreferredStationName(dropdown);
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

    private void BindDropdownListeners()
    {
        BindToggleListener(autoDriaveToggle);
        BindDropdownListener(targetA);
        BindDropdownListener(targetB);
        BindDropdownListener(fuel);
        BindDropdownListener(freight);
    }

    private void UnbindDropdownListeners()
    {
        UnbindToggleListener(autoDriaveToggle);
        UnbindDropdownListener(targetA);
        UnbindDropdownListener(targetB);
        UnbindDropdownListener(fuel);
        UnbindDropdownListener(freight);
    }

    private void BindToggleListener(Toggle toggle)
    {
        if (toggle == null)
        {
            return;
        }

        toggle.onValueChanged.RemoveListener(HandleAutoDriveToggleChanged);
        toggle.onValueChanged.AddListener(HandleAutoDriveToggleChanged);
    }

    private void UnbindToggleListener(Toggle toggle)
    {
        if (toggle == null)
        {
            return;
        }

        toggle.onValueChanged.RemoveListener(HandleAutoDriveToggleChanged);
    }

    private void BindDropdownListener(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
        {
            return;
        }

        dropdown.onValueChanged.RemoveListener(HandleRouteDropdownChanged);
        dropdown.onValueChanged.AddListener(HandleRouteDropdownChanged);
    }

    private void UnbindDropdownListener(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
        {
            return;
        }

        dropdown.onValueChanged.RemoveListener(HandleRouteDropdownChanged);
    }

    private void HandleRouteDropdownChanged(int _)
    {
        ApplyCurrentFilterStateToBoundTrain();
        MarkRouteSelectionDirty();
    }

    private void HandleAutoDriveToggleChanged(bool _)
    {
        ApplyCurrentFilterStateToBoundTrain();
        MarkRouteSelectionDirty();
    }

    private void RefreshFilterDropdowns()
    {
        string selectedFuelOption = boundTrain != null
            ? ResolveFuelOption(boundTrain.AutoDriveFuelMode)
            : ResolveSelectedOptionText(fuel);
        string selectedFreightOption = boundTrain != null
            ? ResolveFreightOption(boundTrain.AutoDriveFreightMode)
            : ResolveSelectedOptionText(freight);

        RefreshFixedOptionDropdown(fuel, FuelOptions, selectedFuelOption);
        RefreshFixedOptionDropdown(freight, FreightOptions, selectedFreightOption);
    }

    private void RefreshFixedOptionDropdown(
        TMP_Dropdown dropdown,
        IReadOnlyList<string> options,
        string preferredOption = null)
    {
        if (dropdown == null)
        {
            return;
        }

        string previouslySelectedOption = !string.IsNullOrWhiteSpace(preferredOption)
            ? preferredOption
            : ResolveSelectedOptionText(dropdown);
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

    private string ResolvePreferredStationName(TMP_Dropdown dropdown)
    {
        if (boundTrain != null)
        {
            string storedStationName = dropdown == targetB
                ? boundTrain.AutoDriveTargetBStationName
                : boundTrain.AutoDriveTargetAStationName;
            if (!string.IsNullOrWhiteSpace(storedStationName))
            {
                return storedStationName;
            }
        }

        string selectedStationName = ResolveSelectedOptionText(dropdown);
        if (!string.IsNullOrWhiteSpace(selectedStationName)
            && !string.Equals(selectedStationName, NoneOption, System.StringComparison.OrdinalIgnoreCase))
        {
            return selectedStationName;
        }

        return string.Empty;
    }

    private void RefreshAutoDriveToggle()
    {
        if (autoDriaveToggle == null)
        {
            return;
        }

        autoDriaveToggle.SetIsOnWithoutNotify(boundTrain != null && boundTrain.AutoDriveEnabled);
    }

    private void ApplyCurrentFilterStateToBoundTrain()
    {
        if (boundTrain == null)
        {
            return;
        }

        boundTrain.ApplyAutoDriveFilterState(
            autoDriaveToggle != null && autoDriaveToggle.isOn,
            NormalizeStationSelection(ResolveSelectedOptionText(targetA)),
            NormalizeStationSelection(ResolveSelectedOptionText(targetB)),
            ResolveFuelFilter(ResolveSelectedOptionText(fuel)),
            ResolveFreightFilter(ResolveSelectedOptionText(freight)));
    }

    private static string NormalizeStationSelection(string stationName)
    {
        return string.IsNullOrWhiteSpace(stationName)
               || string.Equals(stationName, NoneOption, System.StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : stationName.Trim();
    }

    private static string ResolveFuelOption(SteamTrain.AutoDriveFuelFilter filter)
    {
        return filter == SteamTrain.AutoDriveFuelFilter.Full ? FuelOptions[1] : FuelOptions[0];
    }

    private static string ResolveFreightOption(SteamTrain.AutoDriveFreightFilter filter)
    {
        return filter switch
        {
            SteamTrain.AutoDriveFreightFilter.Full => FreightOptions[1],
            SteamTrain.AutoDriveFreightFilter.Empty => FreightOptions[2],
            _ => FreightOptions[0]
        };
    }

    private static SteamTrain.AutoDriveFuelFilter ResolveFuelFilter(string optionText)
    {
        return string.Equals(optionText, FuelOptions[1], System.StringComparison.OrdinalIgnoreCase)
            ? SteamTrain.AutoDriveFuelFilter.Full
            : SteamTrain.AutoDriveFuelFilter.Free;
    }

    private static SteamTrain.AutoDriveFreightFilter ResolveFreightFilter(string optionText)
    {
        if (string.Equals(optionText, FreightOptions[1], System.StringComparison.OrdinalIgnoreCase))
        {
            return SteamTrain.AutoDriveFreightFilter.Full;
        }

        return string.Equals(optionText, FreightOptions[2], System.StringComparison.OrdinalIgnoreCase)
            ? SteamTrain.AutoDriveFreightFilter.Empty
            : SteamTrain.AutoDriveFreightFilter.Free;
    }

    private static bool TryResolveCurrentRouteDisplayTrain(out SteamTrain train)
    {
        train = null;
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null)
        {
            return false;
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null)
        {
            return false;
        }

        if (playerController.TryGetMountedVehicleState(out Vehicle mountedVehicle, out _))
        {
            train = mountedVehicle as SteamTrain;
            if (train == null && mountedVehicle != null)
            {
                train = mountedVehicle.GetComponent<SteamTrain>();
            }

            if (train != null && train.gameObject.activeInHierarchy)
            {
                return true;
            }
        }

        if (!playerController.TryGetFocusedMapObject(out MapObject focusedMapObject))
        {
            return false;
        }

        train = focusedMapObject as SteamTrain;
        if (train == null && focusedMapObject != null)
        {
            train = focusedMapObject.GetComponent<SteamTrain>();
        }

        return train != null && train.gameObject.activeInHierarchy;
    }

    private static void MarkRouteSelectionDirty()
    {
        routeSelectionVersion++;
        if (GameManager.Instance != null
            && GameManager.Instance.TryGetComponent(out RailLineDebugRenderer railLineDebugRenderer)
            && railLineDebugRenderer != null)
        {
            railLineDebugRenderer.RefreshNow();
        }
    }
}
