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
    private static SteamTrain lastSelectedTrain;
    private static SteamTrain focusedRouteTrain;
    private static readonly Queue<Train> routeFocusQueue = new Queue<Train>(8);
    private static readonly HashSet<Train> routeFocusVisited = new HashSet<Train>();
    private static string lastSelectedTargetAStationName = string.Empty;
    private static string lastSelectedTargetBStationName = string.Empty;
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

    public static int RouteSelectionVersion => routeSelectionVersion;

    private void OnEnable()
    {
        activeVisibleFilter = this;
        BindDropdownListeners();
        Refresh();
        CacheCurrentRouteSelection();
        MarkRouteSelectionDirty();
    }

    private void OnDisable()
    {
        CacheCurrentRouteSelection();
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
        CacheCurrentRouteSelection();
        MarkRouteSelectionDirty();
    }

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
            if (!IsRouteSelectionFocused(train))
            {
                train = null;
                return false;
            }

            targetAStationName = NormalizeStationSelection(
                ResolveSelectedOptionText(activeVisibleFilter.targetA));
            targetBStationName = NormalizeStationSelection(
                ResolveSelectedOptionText(activeVisibleFilter.targetB));
            return true;
        }

        if (lastSelectedTrain == null
            || !lastSelectedTrain.gameObject.activeInHierarchy
            || !IsRouteSelectionFocused(lastSelectedTrain))
        {
            return false;
        }

        train = lastSelectedTrain;
        targetAStationName = lastSelectedTargetAStationName;
        targetBStationName = lastSelectedTargetBStationName;
        return true;
    }

    public static void SetFocusedRouteTrain(SteamTrain steamTrain)
    {
        if (steamTrain != null && !steamTrain.gameObject.activeInHierarchy)
        {
            steamTrain = null;
        }

        if (focusedRouteTrain == steamTrain)
        {
            return;
        }

        focusedRouteTrain = steamTrain;
        MarkRouteSelectionDirty();
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
        RefreshAutoDriveToggle();
    }

    private void RefreshAutoDriveToggle()
    {
        if (autoDriaveToggle == null)
        {
            return;
        }

        EnsureControlVisible(autoDriaveToggle);
        bool hasCompleteStationSelection = HasCompleteStationSelection();
        autoDriaveToggle.SetIsOnWithoutNotify(
            boundTrain != null
            && boundTrain.AutoDriveEnabled
            && hasCompleteStationSelection);
        autoDriaveToggle.interactable = boundTrain != null && hasCompleteStationSelection;
    }

    private void RefreshStationTargetDropdowns()
    {
        stationNameScratch.Clear();

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null && boundTrain != null)
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

        EnsureControlVisible(dropdown);
        dropdown.interactable = true;

        string previouslySelectedStationName = ResolvePreferredStationName(dropdown);
        if (!string.IsNullOrWhiteSpace(previouslySelectedStationName)
            && ResolveOptionIndex(previouslySelectedStationName, stationNameScratch) < 0)
        {
            stationNameScratch.Add(previouslySelectedStationName);
        }

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

    private void RefreshFixedOptionDropdown(
        TMP_Dropdown dropdown,
        IReadOnlyList<string> options)
    {
        if (dropdown == null)
        {
            return;
        }

        EnsureControlVisible(dropdown);
        dropdown.interactable = true;

        string previouslySelectedOption = ResolvePreferredFilterOption(dropdown);
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
            int optionIndex = ResolveOptionIndex(previouslySelectedOption, options);
            dropdown.SetValueWithoutNotify(optionIndex >= 0 ? optionIndex : 0);
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

        toggle.onValueChanged.RemoveListener(HandleFilterChanged);
        toggle.onValueChanged.AddListener(HandleFilterChanged);
    }

    private void UnbindToggleListener(Toggle toggle)
    {
        if (toggle == null)
        {
            return;
        }

        toggle.onValueChanged.RemoveListener(HandleFilterChanged);
    }

    private void BindDropdownListener(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
        {
            return;
        }

        dropdown.onValueChanged.RemoveListener(HandleDropdownChanged);
        dropdown.onValueChanged.AddListener(HandleDropdownChanged);
    }

    private void UnbindDropdownListener(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
        {
            return;
        }

        dropdown.onValueChanged.RemoveListener(HandleDropdownChanged);
    }

    private void HandleFilterChanged(bool _)
    {
        ApplyCurrentSettingsToBoundTrain();
        RefreshAutoDriveToggle();
        CacheCurrentRouteSelection();
        MarkRouteSelectionDirty();
    }

    private void HandleDropdownChanged(int _)
    {
        ApplyCurrentSettingsToBoundTrain();
        RefreshAutoDriveToggle();
        CacheCurrentRouteSelection();
        MarkRouteSelectionDirty();
    }

    private void CacheCurrentRouteSelection()
    {
        if (!TryGetBoundTarget(out SteamTrain train))
        {
            if (boundTrain == null)
            {
                lastSelectedTrain = null;
                lastSelectedTargetAStationName = string.Empty;
                lastSelectedTargetBStationName = string.Empty;
            }

            return;
        }

        lastSelectedTrain = train;
        lastSelectedTargetAStationName = NormalizeStationSelection(ResolveSelectedOptionText(targetA));
        lastSelectedTargetBStationName = NormalizeStationSelection(ResolveSelectedOptionText(targetB));
    }

    private static bool IsRouteSelectionFocused(SteamTrain train)
    {
        if (train == null || !train.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (activeVisibleFilter != null
            && activeVisibleFilter.gameObject.activeInHierarchy
            && activeVisibleFilter.TryGetBoundTarget(out SteamTrain activeTrain)
            && activeTrain == train)
        {
            return true;
        }

        return focusedRouteTrain != null
               && focusedRouteTrain.gameObject.activeInHierarchy
               && AreRouteTrainsConnected(train, focusedRouteTrain);
    }

    private static bool AreRouteTrainsConnected(SteamTrain first, SteamTrain second)
    {
        if (first == null || second == null)
        {
            return false;
        }

        if (first == second)
        {
            return true;
        }

        routeFocusQueue.Clear();
        routeFocusVisited.Clear();
        routeFocusQueue.Enqueue(first);
        routeFocusVisited.Add(first);

        while (routeFocusQueue.Count > 0)
        {
            Train currentTrain = routeFocusQueue.Dequeue();
            if (currentTrain == null || !currentTrain.gameObject.activeInHierarchy)
            {
                continue;
            }

            foreach (Train connectedTrain in currentTrain.ConnectedTrains)
            {
                if (connectedTrain == null
                    || !connectedTrain.gameObject.activeInHierarchy
                    || !routeFocusVisited.Add(connectedTrain))
                {
                    continue;
                }

                if (connectedTrain == second)
                {
                    routeFocusQueue.Clear();
                    routeFocusVisited.Clear();
                    return true;
                }

                routeFocusQueue.Enqueue(connectedTrain);
            }
        }

        routeFocusQueue.Clear();
        routeFocusVisited.Clear();
        return false;
    }

    private int ResolveStationOptionIndex(string stationName)
    {
        int optionIndex = ResolveOptionIndex(stationName, stationNameScratch);
        return optionIndex >= 0 ? optionIndex : 0;
    }

    private string ResolvePreferredStationName(TMP_Dropdown dropdown)
    {
        string trainStationName = ResolveBoundTrainStationName(dropdown);
        if (!string.IsNullOrWhiteSpace(trainStationName))
        {
            return trainStationName;
        }

        string selectedStationName = ResolveSelectedOptionText(dropdown);
        if (!string.IsNullOrWhiteSpace(selectedStationName)
            && !string.Equals(selectedStationName, NoneOption, System.StringComparison.OrdinalIgnoreCase))
        {
            return selectedStationName;
        }

        return string.Empty;
    }

    private string ResolveBoundTrainStationName(TMP_Dropdown dropdown)
    {
        if (boundTrain == null)
        {
            return string.Empty;
        }

        if (dropdown == targetA)
        {
            return NormalizeStationSelection(boundTrain.AutoDriveTargetAStationName);
        }

        if (dropdown == targetB)
        {
            return NormalizeStationSelection(boundTrain.AutoDriveTargetBStationName);
        }

        return string.Empty;
    }

    private string ResolvePreferredFilterOption(TMP_Dropdown dropdown)
    {
        if (boundTrain != null)
        {
            if (dropdown == fuel)
            {
                return boundTrain.AutoDriveFuelFilterName;
            }

            if (dropdown == freight)
            {
                return boundTrain.AutoDriveFreightFilterName;
            }
        }

        return ResolveSelectedOptionText(dropdown);
    }

    private void ApplyCurrentSettingsToBoundTrain()
    {
        if (!TryGetBoundTarget(out SteamTrain train))
        {
            return;
        }

        string targetAStationName = NormalizeStationSelection(ResolveSelectedOptionText(targetA));
        string targetBStationName = NormalizeStationSelection(ResolveSelectedOptionText(targetB));
        bool canEnableAutoDrive = HasCompleteStationSelection(targetAStationName, targetBStationName);
        bool enabled = autoDriaveToggle != null && autoDriaveToggle.isOn && canEnableAutoDrive;
        train.ApplyAutoDriveSettings(
            enabled,
            targetAStationName,
            targetBStationName,
            ResolveSelectedOptionText(fuel),
            ResolveSelectedOptionText(freight));

        if (autoDriaveToggle != null)
        {
            autoDriaveToggle.SetIsOnWithoutNotify(train.AutoDriveEnabled);
            autoDriaveToggle.interactable = canEnableAutoDrive;
        }
    }

    private bool HasCompleteStationSelection()
    {
        return HasCompleteStationSelection(
            NormalizeStationSelection(ResolveSelectedOptionText(targetA)),
            NormalizeStationSelection(ResolveSelectedOptionText(targetB)));
    }

    private static bool HasCompleteStationSelection(string targetAStationName, string targetBStationName)
    {
        return !string.IsNullOrWhiteSpace(targetAStationName)
               && !string.IsNullOrWhiteSpace(targetBStationName)
               && !string.Equals(
                   targetAStationName,
                   targetBStationName,
                   System.StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureControlVisible(Component component)
    {
        if (component != null && !component.gameObject.activeSelf)
        {
            component.gameObject.SetActive(true);
        }
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

        return -1;
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

    private static string NormalizeStationSelection(string stationName)
    {
        return string.IsNullOrWhiteSpace(stationName)
               || string.Equals(stationName, NoneOption, System.StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : stationName.Trim();
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

    public static void MarkRouteSelectionDirty()
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
