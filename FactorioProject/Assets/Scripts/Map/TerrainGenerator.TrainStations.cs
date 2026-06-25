using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public partial class TerrainGenerator
{
    private const float TrainStationRailCoordinateSnapDistance = 0.75f;
    private static readonly Regex TrainStationAutoNamePattern =
        new Regex(@"^Station\s+([A-Z]+)\s*-\s*(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string ResolveUniqueTrainStationName(Trainstation station, string requestedName)
    {
        EnsureResourceStateStore();
        List<BlockStateStore.InstallationSaveState> states = resourceStateStore != null
            ? resourceStateStore.GetInstallationStatesSnapshot()
            : new List<BlockStateStore.InstallationSaveState>();
        Trainstation[] liveStations = FindObjectsOfType<Trainstation>(false);
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
        TrainStationRailNetwork railNetwork = BuildTrainStationRailNetwork(states);
        int targetComponent = FindTrainRailComponent(train, railNetwork);
        if (targetComponent < 0)
        {
            return;
        }

        HashSet<string> addedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        Trainstation[] liveStations = FindObjectsOfType<Trainstation>(false);
        for (int i = 0; i < liveStations.Length; i++)
        {
            Trainstation station = liveStations[i];
            if (station == null
                || FindStationRailComponent(station, railNetwork) != targetComponent)
            {
                continue;
            }

            if (!station.HasAssignedStationName)
            {
                EnsureTrainStationNameAssigned(station);
            }

            string stationName = station.StationName;
            if (string.IsNullOrWhiteSpace(stationName) || !addedNames.Add(stationName))
            {
                continue;
            }

            results.Add(stationName);
        }

        for (int i = 0; i < states.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = states[i];
            if (!IsTrainStationState(state)
                || string.IsNullOrWhiteSpace(state.stationName)
                || FindStationRailComponent(state, railNetwork) != targetComponent
                || !addedNames.Add(state.stationName.Trim()))
            {
                continue;
            }

            results.Add(state.stationName.Trim());
        }

        results.Sort(System.StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureTrainStationNameAssigned(Trainstation station)
    {
        if (station == null)
        {
            return;
        }

        string requestedName = station.HasAssignedStationName && !IsAutomaticStationName(station.StoredStationName)
            ? station.StoredStationName
            : string.Empty;
        station.ApplyStationName(ResolveUniqueTrainStationName(station, requestedName));
    }

    private void RefreshAutomaticTrainStationNames()
    {
        EnsureResourceStateStore();
        if (resourceStateStore == null)
        {
            return;
        }

        Trainstation[] liveStations = FindObjectsOfType<Trainstation>(false);
        for (int i = 0; i < liveStations.Length; i++)
        {
            Trainstation station = liveStations[i];
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
            resourceStateStore.SaveInstallation(station);
            resourceStateStore.RegisterLiveInstallation(station);
        }
    }

    private string GenerateAutomaticTrainStationName(
        Trainstation station,
        List<BlockStateStore.InstallationSaveState> states,
        IReadOnlyList<Trainstation> liveStations,
        HashSet<string> usedNames)
    {
        TrainStationRailNetwork railNetwork = BuildTrainStationRailNetwork(states);
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
        IReadOnlyList<Trainstation> liveStations,
        TrainStationRailNetwork railNetwork,
        Trainstation excludedStation)
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
            Trainstation liveStation = liveStations[i];
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
        IReadOnlyList<Trainstation> liveStations,
        TrainStationRailNetwork railNetwork,
        Trainstation excludedStation)
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
            Trainstation liveStation = liveStations[i];
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
        IReadOnlyList<Trainstation> liveStations,
        Trainstation excludedStation)
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
            Trainstation liveStation = liveStations[i];
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

    private static TrainStationRailNetwork BuildTrainStationRailNetwork(
        List<BlockStateStore.InstallationSaveState> states)
    {
        TrainStationRailNetwork railNetwork = new TrainStationRailNetwork();
        Railload[] liveRails = FindObjectsOfType<Railload>(false);
        for (int i = 0; i < liveRails.Length; i++)
        {
            if (!TryBuildRailSegment(liveRails[i], out TrainStationRailSegment segment))
            {
                continue;
            }

            railNetwork.Segments.Add(segment);
        }

        for (int i = 0; i < states.Count; i++)
        {
            BlockStateStore.InstallationSaveState state = states[i];
            if (!IsRailloadState(state) || !TryBuildRailSegment(state, out TrainStationRailSegment segment))
            {
                continue;
            }

            railNetwork.Segments.Add(segment);
        }

        AssignRailComponents(railNetwork);
        return railNetwork;
    }

    private static bool TryBuildRailSegment(
        Railload rail,
        out TrainStationRailSegment segment)
    {
        segment = null;
        if (rail == null
            || !rail.isActiveAndEnabled
            || !rail.TryGetPlacementRuntime(out _, out _))
        {
            return false;
        }

        List<Vector2> points = rail.CopyVisualPathPoints();
        if (points == null || points.Count < 2)
        {
            return false;
        }

        segment = new TrainStationRailSegment
        {
            Points = points,
            OccupiedCoordinates = rail.RuntimeOccupiedCoordinates,
            ComponentIndex = -1
        };
        return RailConnectionUtility.TryResolveConnectionEndpoints(
            segment.Points,
            segment.OccupiedCoordinates,
            out segment.StartPoint,
            out segment.EndPoint);
    }

    private static bool TryBuildRailSegment(
        BlockStateStore.InstallationSaveState state,
        out TrainStationRailSegment segment)
    {
        segment = null;
        if (state?.railVisualPathPoints == null || state.railVisualPathPoints.Count < 2)
        {
            return false;
        }

        segment = new TrainStationRailSegment
        {
            Points = new List<Vector2>(state.railVisualPathPoints),
            OccupiedCoordinates = state.occupiedCoordinates,
            ComponentIndex = -1
        };
        return RailConnectionUtility.TryResolveConnectionEndpoints(
            segment.Points,
            segment.OccupiedCoordinates,
            out segment.StartPoint,
            out segment.EndPoint);
    }

    private static void AssignRailComponents(TrainStationRailNetwork railNetwork)
    {
        float maxSqrDistance = RailLineDebugRenderer.RailGroupConnectionDistance * RailLineDebugRenderer.RailGroupConnectionDistance;
        Queue<int> queue = new Queue<int>();
        int componentIndex = 0;

        for (int startIndex = 0; startIndex < railNetwork.Segments.Count; startIndex++)
        {
            if (railNetwork.Segments[startIndex].ComponentIndex >= 0)
            {
                continue;
            }

            railNetwork.Segments[startIndex].ComponentIndex = componentIndex;
            queue.Enqueue(startIndex);
            while (queue.Count > 0)
            {
                int currentIndex = queue.Dequeue();
                TrainStationRailSegment current = railNetwork.Segments[currentIndex];
                for (int otherIndex = 0; otherIndex < railNetwork.Segments.Count; otherIndex++)
                {
                    TrainStationRailSegment other = railNetwork.Segments[otherIndex];
                    if (other.ComponentIndex >= 0 || !AreRailSegmentsConnected(current, other, maxSqrDistance))
                    {
                        continue;
                    }

                    other.ComponentIndex = componentIndex;
                    queue.Enqueue(otherIndex);
                }
            }

            componentIndex++;
        }
    }

    private static bool AreRailSegmentsConnected(
        TrainStationRailSegment left,
        TrainStationRailSegment right,
        float maxSqrDistance)
    {
        return left != null
               && right != null
               && RailConnectionUtility.AreConnected(
                   left.OccupiedCoordinates,
                   left.Points,
                   left.StartPoint,
                   left.EndPoint,
                   right.OccupiedCoordinates,
                   right.Points,
                   right.StartPoint,
                   right.EndPoint,
                   maxSqrDistance);
    }

    private static int FindStationRailComponent(
        Trainstation station,
        TrainStationRailNetwork railNetwork)
    {
        if (station == null || !TryResolveStationRailCoordinate(station, out Vector2Int railCoordinate))
        {
            return -1;
        }

        return FindRailComponentAtCoordinate(railCoordinate, railNetwork);
    }

    private static int FindStationRailComponent(
        BlockStateStore.InstallationSaveState stationState,
        TrainStationRailNetwork railNetwork)
    {
        if (!TryResolveStationRailCoordinate(stationState, railNetwork, out Vector2Int railCoordinate))
        {
            return -1;
        }

        return FindRailComponentAtCoordinate(railCoordinate, railNetwork);
    }

    private static int FindTrainRailComponent(
        Train train,
        TrainStationRailNetwork railNetwork)
    {
        if (train == null
            || !train.TryGetCurrentRailPose(out Railload rail, out _, out Vector2 pathPoint, out _))
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
        Vector2Int railCoordinate,
        TrainStationRailNetwork railNetwork)
    {
        for (int i = 0; i < railNetwork.Segments.Count; i++)
        {
            TrainStationRailSegment segment = railNetwork.Segments[i];
            if (SegmentContainsCoordinate(segment, railCoordinate))
            {
                return segment.ComponentIndex;
            }
        }

        return FindRailComponentAtPoint(new Vector2(railCoordinate.x, railCoordinate.y), railNetwork);
    }

    private static int FindRailComponentAtPoint(
        Vector2 railPoint,
        TrainStationRailNetwork railNetwork)
    {
        float bestSqrDistance = TrainStationRailCoordinateSnapDistance * TrainStationRailCoordinateSnapDistance;
        int bestComponent = -1;
        for (int i = 0; i < railNetwork.Segments.Count; i++)
        {
            TrainStationRailSegment segment = railNetwork.Segments[i];
            float sqrDistance = GetPolylineSqrDistance(railPoint, segment.Points);
            if (sqrDistance >= bestSqrDistance)
            {
                continue;
            }

            bestSqrDistance = sqrDistance;
            bestComponent = segment.ComponentIndex;
        }

        return bestComponent;
    }

    private static bool TryResolveStationRailCoordinate(
        Trainstation station,
        out Vector2Int railCoordinate)
    {
        railCoordinate = default;
        return station != null && station.TryGetRailCoordinate(out railCoordinate);
    }

    private static bool TryResolveStationRailCoordinate(
        BlockStateStore.InstallationSaveState state,
        TrainStationRailNetwork railNetwork,
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

    private static bool RailCoordinateExists(Vector2Int coordinate, TrainStationRailNetwork railNetwork)
    {
        for (int i = 0; i < railNetwork.Segments.Count; i++)
        {
            if (SegmentContainsCoordinate(railNetwork.Segments[i], coordinate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentContainsCoordinate(
        TrainStationRailSegment segment,
        Vector2Int coordinate)
    {
        if (segment?.OccupiedCoordinates == null)
        {
            return false;
        }

        for (int i = 0; i < segment.OccupiedCoordinates.Count; i++)
        {
            if (segment.OccupiedCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    private static float GetPolylineSqrDistance(Vector2 point, List<Vector2> points)
    {
        if (points == null || points.Count <= 0)
        {
            return float.MaxValue;
        }

        if (points.Count == 1)
        {
            return (point - points[0]).sqrMagnitude;
        }

        float bestSqrDistance = float.MaxValue;
        for (int i = 1; i < points.Count; i++)
        {
            Vector2 start = points[i - 1];
            Vector2 end = points[i];
            Vector2 segment = end - start;
            float segmentSqrMagnitude = segment.sqrMagnitude;
            float t = segmentSqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentSqrMagnitude)
                : 0f;
            Vector2 closest = start + segment * t;
            float sqrDistance = (point - closest).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
            }
        }

        return bestSqrDistance;
    }

    private static bool IsTrainStationState(BlockStateStore.InstallationSaveState state)
    {
        return IsInstallationStateType<Trainstation>(state);
    }

    private static bool IsRailloadState(BlockStateStore.InstallationSaveState state)
    {
        return IsInstallationStateType<Railload>(state);
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
        Trainstation station)
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

    private sealed class TrainStationRailNetwork
    {
        public readonly List<TrainStationRailSegment> Segments = new List<TrainStationRailSegment>();
    }

    private sealed class TrainStationRailSegment
    {
        public List<Vector2> Points;
        public IReadOnlyList<Vector2Int> OccupiedCoordinates;
        public Vector2 StartPoint;
        public Vector2 EndPoint;
        public int ComponentIndex;
    }
}
