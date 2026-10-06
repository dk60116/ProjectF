using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public partial class TerrainGenerator
{
    public ProjectF.Railway.RailWorld GetRailWorld()
    {
        EnsureResourceStateStore();
        return resourceStateStore.RailWorld;
    }
    private static readonly Regex TrainStationAutoNamePattern =
        new Regex(@"^Station\s+([A-Z]+)\s*-\s*(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string ResolveUniqueTrainStationName(ProjectF.Railway.ITrainStationTarget station, string requestedName)
    {
        EnsureResourceStateStore();
        List<BlockStateStore.InstallationSaveState> states = resourceStateStore != null
            ? resourceStateStore.GetInstallationStatesSnapshot()
            : new List<BlockStateStore.InstallationSaveState>();
        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations = GetRailWorld().LiveStations;
        HashSet<string> usedNames = CollectUsedTrainStationNames(states, liveStations, station);

        string normalizedName = string.IsNullOrWhiteSpace(requestedName) ? string.Empty : requestedName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return GenerateAutomaticTrainStationName(station, states, liveStations, usedNames);
        }

        if (!usedNames.Contains(normalizedName))
        {
            return normalizedName;
        }

        if (TryParseAutomaticStationName(normalizedName, out string label, out int number))
        {
            int candidateNumber = Mathf.Max(1, number + 1);
            while (candidateNumber < int.MaxValue)
            {
                string candidateName = FormatAutomaticStationName(label, candidateNumber);
                if (!usedNames.Contains(candidateName))
                {
                    return candidateName;
                }

                candidateNumber++;
            }
        }

        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidateName = $"{normalizedName} ({suffix})";
            if (!usedNames.Contains(candidateName))
            {
                return candidateName;
            }
        }

        return normalizedName;
    }

    public void CollectTrainStationNamesOnSameRailLine(Train train, List<string> results)
    {
        CollectTrainStationNamesOnSameRailLine(train, results, null);
    }

    public void CollectTrainStationNamesOnSameRailLine(
        Train train,
        List<string> results,
        IDictionary<string, Color32> stationColorsByName)
    {
        stationColorsByName?.Clear();
        if (results == null)
        {
            return;
        }

        results.Clear();
        if (train == null)
        {
            return;
        }

        EnsureResourceStateStore();
        List<BlockStateStore.InstallationSaveState> states = resourceStateStore != null
            ? resourceStateStore.GetInstallationStatesSnapshot()
            : new List<BlockStateStore.InstallationSaveState>();
        ProjectF.Railway.RailWorld railNetwork = GetRailWorld();
        int targetComponent = FindTrainRailComponent(train, railNetwork);
        if (targetComponent < 0)
        {
            return;
        }

        HashSet<string> addedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations = GetRailWorld().LiveStations;
        for (int i = 0; i < liveStations.Count; i++)
        {
            ProjectF.Railway.ITrainStationTarget station = liveStations[i];
            if (station == null
                || FindStationRailComponent(station, railNetwork) != targetComponent)
            {
                continue;
            }

            if (!station.HasAssignedStationName || !station.HasAssignedStationColor)
            {
                EnsureTrainStationIdentityAssigned(station);
            }

            string stationName = station.StationName;
            if (string.IsNullOrWhiteSpace(stationName) || !addedNames.Add(stationName))
            {
                continue;
            }

            results.Add(stationName);
            if (stationColorsByName != null && station.HasAssignedStationColor)
            {
                stationColorsByName[stationName] = station.StoredStationColor;
            }
        }

        for (int i = 0; i < states.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = states[i];
            if (!IsTrainStationState(state)
                || string.IsNullOrWhiteSpace(state.stationName)
                || FindStationRailComponent(state, railNetwork) != targetComponent)
            {
                continue;
            }

            string stationName = state.stationName.Trim();
            if (!addedNames.Add(stationName))
            {
                continue;
            }

            results.Add(stationName);
            if (stationColorsByName != null && state.stationColorAssigned)
            {
                stationColorsByName[stationName] = state.stationColor;
            }
        }

        results.Sort(System.StringComparer.OrdinalIgnoreCase);
    }

    internal void EnsureTrainStationIdentityAssigned(ProjectF.Railway.ITrainStationTarget station)
    {
        if (station == null)
        {
            return;
        }

        string requestedName = station.HasAssignedStationName && !IsAutomaticStationName(station.StoredStationName)
            ? station.StoredStationName
            : string.Empty;
        station.ApplyStationName(ResolveUniqueTrainStationName(station, requestedName));

        if (!station.HasAssignedStationColor)
        {
            station.ApplyStationColor(ResolveUniqueTrainStationColor(station), true);
        }
    }

    private Color32 ResolveUniqueTrainStationColor(ProjectF.Railway.ITrainStationTarget station)
    {
        EnsureResourceStateStore();
        List<BlockStateStore.InstallationSaveState> states = resourceStateStore != null
            ? resourceStateStore.GetInstallationStatesSnapshot()
            : new List<BlockStateStore.InstallationSaveState>();
        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations = GetRailWorld().LiveStations;
        HashSet<Color32> usedColors = new HashSet<Color32>();

        for (int i = 0; i < states.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = states[i];
            if (!IsTrainStationState(state)
                || IsSameTrainStationState(state, station)
                || !state.stationColorAssigned)
            {
                continue;
            }

            usedColors.Add(state.stationColor);
        }

        for (int i = 0; i < liveStations.Count; i++)
        {
            ProjectF.Railway.ITrainStationTarget liveStation = liveStations[i];
            if (liveStation == null
                || liveStation == station
                || !liveStation.HasAssignedStationColor)
            {
                continue;
            }

            usedColors.Add(liveStation.StoredStationColor);
        }

        int optionCount = Trainstation.StationColorOptionCount;
        int availableOptionCount = 0;
        for (int i = 0; i < optionCount; i++)
        {
            Color32 candidate = Trainstation.GetStationColorOption(i);
            if (!usedColors.Contains(candidate))
            {
                availableOptionCount++;
            }
        }

        if (availableOptionCount > 0)
        {
            int selectedAvailableIndex = Random.Range(0, availableOptionCount);
            for (int i = 0; i < optionCount; i++)
            {
                Color32 candidate = Trainstation.GetStationColorOption(i);
                if (usedColors.Contains(candidate))
                {
                    continue;
                }

                if (selectedAvailableIndex-- == 0)
                {
                    return candidate;
                }
            }
        }

        // The picker palette can be exhausted in very large worlds. Continue with
        // random HSV colors so initial station assignment still remains unique.
        for (int attempt = 0; attempt < 1024; attempt++)
        {
            Color32 candidate = Color.HSVToRGB(Random.value, Random.Range(0.55f, 0.9f), Random.Range(0.75f, 1f));
            candidate.a = 255;
            if (!usedColors.Contains(candidate))
            {
                return candidate;
            }
        }

        int randomRgb = Random.Range(0, 1 << 24);
        for (int offset = 0; offset < 1 << 24; offset++)
        {
            int rgb = (randomRgb + offset) & 0xFFFFFF;
            Color32 candidate = new Color32(
                (byte)(rgb >> 16),
                (byte)(rgb >> 8),
                (byte)rgb,
                255);
            if (!usedColors.Contains(candidate))
            {
                return candidate;
            }
        }

        return new Color32(255, 255, 255, 255);
    }

    private void RefreshAutomaticTrainStationNames()
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations = GetRailWorld().LiveStations;
        for (int i = 0; i < liveStations.Count; i++)
        {
            ProjectF.Railway.ITrainStationTarget station = liveStations[i];
            if (station == null)
            {
                continue;
            }

            bool shouldAutoAssign = !station.HasAssignedStationName || IsAutomaticStationName(station.StoredStationName);
            if (!shouldAutoAssign)
            {
                continue;
            }

            string resolvedName = ResolveUniqueTrainStationName(station, string.Empty);
            if (string.Equals(station.StoredStationName, resolvedName, System.StringComparison.Ordinal))
            {
                continue;
            }

            station.ApplyStationName(resolvedName);
            if (station is InstallationObject native) resourceStateStore.SaveInstallation(native);
            if (station is InstallationObject view) resourceStateStore.RegisterLiveInstallation(view);
        }
    }

    private string GenerateAutomaticTrainStationName(
        ProjectF.Railway.ITrainStationTarget station,
        List<BlockStateStore.InstallationSaveState> states,
        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations,
        HashSet<string> usedNames)
    {
        ProjectF.Railway.RailWorld railNetwork = GetRailWorld();
        int targetComponent = FindStationRailComponent(station, railNetwork);
        string label = ResolveTrainStationRailSetLabel(targetComponent, states, liveStations, railNetwork, station);
        HashSet<int> usedNumbers = CollectUsedTrainStationNumbers(label, targetComponent, states, liveStations, railNetwork, station);

        for (int number = 1; number < int.MaxValue; number++)
        {
            string candidateName = FormatAutomaticStationName(label, number);
            if (!usedNumbers.Contains(number) && !usedNames.Contains(candidateName))
            {
                return candidateName;
            }
        }

        return FormatAutomaticStationName(label, 1);
    }

    private string ResolveTrainStationRailSetLabel(
        int targetComponent,
        List<BlockStateStore.InstallationSaveState> states,
        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations,
        ProjectF.Railway.RailWorld railNetwork,
        ProjectF.Railway.ITrainStationTarget excludedStation)
    {
        string componentLabel = null;
        HashSet<string> usedLabels = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < states.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = states[i];
            if (!IsTrainStationState(state)
                || IsSameTrainStationState(state, excludedStation)
                || !TryParseAutomaticStationName(state.stationName, out string label, out _))
            {
                continue;
            }

            usedLabels.Add(label);
            int stationComponent = FindStationRailComponent(state, railNetwork);
            if (stationComponent == targetComponent
                && (componentLabel == null || string.CompareOrdinal(label, componentLabel) < 0))
            {
                componentLabel = label;
            }
        }

        for (int i = 0; i < liveStations.Count; i++)
        {
            ProjectF.Railway.ITrainStationTarget liveStation = liveStations[i];
            if (liveStation == null
                || liveStation == excludedStation
                || !liveStation.HasAssignedStationName
                || !TryParseAutomaticStationName(liveStation.StoredStationName, out string label, out _))
            {
                continue;
            }

            usedLabels.Add(label);
            int stationComponent = FindStationRailComponent(liveStation, railNetwork);
            if (stationComponent == targetComponent
                && (componentLabel == null || string.CompareOrdinal(label, componentLabel) < 0))
            {
                componentLabel = label;
            }
        }

        if (!string.IsNullOrWhiteSpace(componentLabel))
        {
            return componentLabel.ToUpperInvariant();
        }

        for (int labelIndex = 0; labelIndex < int.MaxValue; labelIndex++)
        {
            string candidateLabel = FormatAlphabetLabel(labelIndex);
            if (!usedLabels.Contains(candidateLabel))
            {
                return candidateLabel;
            }
        }

        return "A";
    }

    private HashSet<int> CollectUsedTrainStationNumbers(
        string targetLabel,
        int targetComponent,
        List<BlockStateStore.InstallationSaveState> states,
        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations,
        ProjectF.Railway.RailWorld railNetwork,
        ProjectF.Railway.ITrainStationTarget excludedStation)
    {
        HashSet<int> usedNumbers = new HashSet<int>();
        for (int i = 0; i < states.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = states[i];
            if (!IsTrainStationState(state)
                || IsSameTrainStationState(state, excludedStation)
                || !TryParseAutomaticStationName(state.stationName, out string label, out int number)
                || !string.Equals(label, targetLabel, System.StringComparison.OrdinalIgnoreCase)
                || FindStationRailComponent(state, railNetwork) != targetComponent)
            {
                continue;
            }

            usedNumbers.Add(number);
        }

        for (int i = 0; i < liveStations.Count; i++)
        {
            ProjectF.Railway.ITrainStationTarget liveStation = liveStations[i];
            if (liveStation == null
                || liveStation == excludedStation
                || !liveStation.HasAssignedStationName
                || !TryParseAutomaticStationName(liveStation.StoredStationName, out string label, out int number)
                || !string.Equals(label, targetLabel, System.StringComparison.OrdinalIgnoreCase)
                || FindStationRailComponent(liveStation, railNetwork) != targetComponent)
            {
                continue;
            }

            usedNumbers.Add(number);
        }

        return usedNumbers;
    }

    private HashSet<string> CollectUsedTrainStationNames(
        List<BlockStateStore.InstallationSaveState> states,
        IReadOnlyList<ProjectF.Railway.ITrainStationTarget> liveStations,
        ProjectF.Railway.ITrainStationTarget excludedStation)
    {
        HashSet<string> usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < states.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = states[i];
            if (!IsTrainStationState(state)
                || IsSameTrainStationState(state, excludedStation)
                || string.IsNullOrWhiteSpace(state.stationName))
            {
                continue;
            }

            usedNames.Add(state.stationName.Trim());
        }

        for (int i = 0; i < liveStations.Count; i++)
        {
            ProjectF.Railway.ITrainStationTarget liveStation = liveStations[i];
            if (liveStation == null
                || liveStation == excludedStation
                || !liveStation.HasAssignedStationName)
            {
                continue;
            }

            usedNames.Add(liveStation.StoredStationName);
        }

        return usedNames;
    }

    private static int FindStationRailComponent(
        ProjectF.Railway.ITrainStationTarget station,
        ProjectF.Railway.RailWorld railNetwork)
    {
        if (station == null || !TryResolveStationRailCoordinate(station, out Vector2Int railCoordinate))
        {
            return -1;
        }

        return FindRailComponentAtCoordinate(railCoordinate, railNetwork);
    }

    private static int FindStationRailComponent(
        BlockStateStore.InstallationSaveState stationState,
        ProjectF.Railway.RailWorld railNetwork)
    {
        if (!TryResolveStationRailCoordinate(stationState, railNetwork, out Vector2Int railCoordinate))
        {
            return -1;
        }

        return FindRailComponentAtCoordinate(railCoordinate, railNetwork);
    }

    private static int FindTrainRailComponent(
        Train train,
        ProjectF.Railway.RailWorld railNetwork)
    {
        if (train == null
            || !train.TryGetCurrentRailPose(out ProjectF.Railway.IRailTarget rail, out _, out Vector2 pathPoint, out _))
        {
            return -1;
        }

        if (rail != null && rail.RuntimeOccupiedCoordinates != null)
        {
            for (int i = 0; i < rail.RuntimeOccupiedCoordinates.Count; i++)
            {
                int component = FindRailComponentAtCoordinate(rail.RuntimeOccupiedCoordinates[i], railNetwork);
                if (component >= 0)
                {
                    return component;
                }
            }
        }

        return FindRailComponentAtPoint(pathPoint, railNetwork);
    }

    private static int FindRailComponentAtCoordinate(
        Vector2Int railCoordinate, ProjectF.Railway.RailWorld railNetwork)
    {
        return railNetwork.FindComponentAtCoordinate(railCoordinate);
    }

    private static int FindRailComponentAtPoint(
        Vector2 railPoint, ProjectF.Railway.RailWorld railNetwork)
    {
        return railNetwork.FindComponentAtPoint(railPoint);
    }

    private static bool TryResolveStationRailCoordinate(
        ProjectF.Railway.ITrainStationTarget station,
        out Vector2Int railCoordinate)
    {
        railCoordinate = default;
        return station != null && station.TryGetRailCoordinate(out railCoordinate);
    }

    private static bool TryResolveStationRailCoordinate(
        BlockStateStore.InstallationSaveState state,
        ProjectF.Railway.RailWorld railNetwork,
        out Vector2Int railCoordinate)
    {
        railCoordinate = default;
        if (state == null || !Trainstation.TryGetFacingDirection(state.quarterTurns, out Vector2Int direction))
        {
            return false;
        }

        IReadOnlyList<Vector2Int> stationCoordinates = state.occupiedCoordinates;
        int coordinateCount = stationCoordinates != null && stationCoordinates.Count > 0
            ? stationCoordinates.Count
            : 1;
        bool hasFallback = false;
        Vector2Int fallback = default;

        for (int i = 0; i < coordinateCount; i++)
        {
            Vector2Int stationCoordinate = stationCoordinates != null && stationCoordinates.Count > 0
                ? stationCoordinates[i]
                : state.anchorCoordinate;
            Vector2Int candidate = stationCoordinate + direction;
            if (!hasFallback)
            {
                fallback = candidate;
                hasFallback = true;
            }

            if (RailCoordinateExists(candidate, railNetwork))
            {
                railCoordinate = candidate;
                return true;
            }
        }

        if (!hasFallback)
        {
            return false;
        }

        railCoordinate = fallback;
        return true;
    }

    private static bool RailCoordinateExists(Vector2Int coordinate, ProjectF.Railway.RailWorld railNetwork)
    {
        return railNetwork.CoordinateExists(coordinate);
    }

    private static bool IsTrainStationState(BlockStateStore.InstallationSaveState state)
    {
        return IsInstallationStateType<Trainstation>(state);
    }

    private static bool IsInstallationStateType<T>(BlockStateStore.InstallationSaveState state)
        where T : Component
    {
        if (state == null || state.itemId < 0)
        {
            return false;
        }

        IReadOnlyList<ItemDefinition> definitions = GameManager.Instance?.ItemManger?.ItemDefinitions;
        if (definitions == null)
        {
            return false;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id != state.itemId || definition.mapObject == null)
            {
                continue;
            }

            return definition.mapObject is T
                   || definition.mapObject.GetComponent<T>() != null
                   || definition.mapObject.GetComponentInChildren<T>(true) != null;
        }

        return false;
    }

    private static bool IsSameTrainStationState(
        BlockStateStore.InstallationSaveState state,
        ProjectF.Railway.ITrainStationTarget station)
    {
        if (state == null || station == null)
        {
            return false;
        }

        if (station.RuntimePlacementSequence > 0
            && state.placementSequence == station.RuntimePlacementSequence)
        {
            return true;
        }

        return station.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _)
               && state.anchorCoordinate == anchorCoordinate;
    }

    private static bool TryParseAutomaticStationName(string name, out string label, out int number)
    {
        label = null;
        number = 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        Match match = TrainStationAutoNamePattern.Match(name.Trim());
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out number))
        {
            return false;
        }

        label = match.Groups[1].Value.ToUpperInvariant();
        return number > 0;
    }

    private static bool IsAutomaticStationName(string name)
    {
        return TryParseAutomaticStationName(name, out _, out _);
    }

    private static string FormatAutomaticStationName(string label, int number)
    {
        return $"Station {label.ToUpperInvariant()} - {Mathf.Max(1, number)}";
    }

    private static string FormatAlphabetLabel(int index)
    {
        index = Mathf.Max(0, index);
        string label = string.Empty;
        do
        {
            int remainder = index % 26;
            label = (char)('A' + remainder) + label;
            index = index / 26 - 1;
        }
        while (index >= 0);

        return label;
    }

}
