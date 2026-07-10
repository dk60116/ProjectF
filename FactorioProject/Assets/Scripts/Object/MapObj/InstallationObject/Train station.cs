using System.Collections.Generic;
using UnityEngine;

public class Trainstation : InstallationObject
{
    [SerializeField]
    private Sprite stationMarkerIcon;
    [SerializeField]
    private string stationName = string.Empty;

    private readonly List<InstallationObject> railCoordinateSearchScratch = new List<InstallationObject>(4);

    public Sprite StationMarkerIcon => stationMarkerIcon;
    public string StationName => HasAssignedStationName ? StoredStationName : ResolveDefaultStationName();
    public string StoredStationName => NormalizeStationName(stationName);
    public bool HasAssignedStationName => !string.IsNullOrWhiteSpace(stationName);

    public void SetStationName(string value)
    {
        string normalizedName = NormalizeStationName(value);
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain != null)
        {
            normalizedName = terrain.ResolveUniqueTrainStationName(this, normalizedName);
        }
        else if (string.IsNullOrWhiteSpace(normalizedName))
        {
            normalizedName = ResolveDefaultStationName();
        }

        if (StoredStationName == normalizedName)
        {
            return;
        }

        stationName = normalizedName;
        base.OnPlacementRuntimeChanged();
        PersistStationName();
    }

    public void ApplyStationName(string value)
    {
        string normalizedName = NormalizeStationName(value);
        if (StoredStationName == normalizedName)
        {
            return;
        }

        stationName = normalizedName;
        base.OnPlacementRuntimeChanged();
    }

    public bool TryGetRailCoordinate(out Vector2Int railCoordinate)
    {
        railCoordinate = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        IReadOnlyList<Vector2Int> stationCoordinates = RuntimeOccupiedCoordinates;
        if (!TryGetFacingDirection(quarterTurns, out Vector2Int selectedDirection))
        {
            return false;
        }

        bool hasFallback = false;
        Vector2Int fallbackRailCoordinate = default;
        int coordinateCount = stationCoordinates != null && stationCoordinates.Count > 0
            ? stationCoordinates.Count
            : 1;
        for (int i = 0; i < coordinateCount; i++)
        {
            Vector2Int stationCoordinate = stationCoordinates != null && stationCoordinates.Count > 0
                ? stationCoordinates[i]
                : anchorCoordinate;
            Vector2Int candidateRailCoordinate = stationCoordinate + selectedDirection;
            if (!hasFallback)
            {
                fallbackRailCoordinate = candidateRailCoordinate;
                hasFallback = true;
            }

            if (!CoordinateHasRuntimeRail(candidateRailCoordinate))
            {
                continue;
            }

            railCoordinate = candidateRailCoordinate;
            return true;
        }

        if (!hasFallback)
        {
            return false;
        }

        railCoordinate = fallbackRailCoordinate;
        return true;
    }

    public static bool TryGetFacingDirection(int quarterTurns, out Vector2Int direction)
    {
        switch (((quarterTurns % 4) + 4) % 4)
        {
            case 0:
                direction = Vector2Int.up;
                return true;
            case 1:
                direction = Vector2Int.right;
                return true;
            case 2:
                direction = Vector2Int.down;
                return true;
            case 3:
                direction = Vector2Int.left;
                return true;
            default:
                direction = Vector2Int.zero;
                return false;
        }
    }

    private bool CoordinateHasRuntimeRail(Vector2Int coordinate)
    {
        railCoordinateSearchScratch.Clear();
        InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
            coordinate,
            railCoordinateSearchScratch);

        bool hasRail = false;
        for (int i = 0; i < railCoordinateSearchScratch.Count; i++)
        {
            if (railCoordinateSearchScratch[i] is Railload)
            {
                hasRail = true;
                break;
            }
        }

        railCoordinateSearchScratch.Clear();
        return hasRail;
    }

    private string ResolveDefaultStationName()
    {
        ItemDefinition definition = BoundItemDefinition != null
            ? BoundItemDefinition
            : InputOutputModule.ResolveItemDefinition(ResolveItemId());
        if (definition != null && !string.IsNullOrWhiteSpace(definition.itemName))
        {
            return definition.itemName;
        }

        if (definition != null && !string.IsNullOrWhiteSpace(definition.name))
        {
            return definition.name;
        }

        return gameObject != null ? gameObject.name : name;
    }

    private static string NormalizeStationName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private void PersistStationName()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        TerrainGenerator.ResolveActive()?.SaveRuntimeInstallationState(this);
    }

#if UNITY_EDITOR
    private const string StationMarkerIconAssetPath = "Assets/Image/UI/Item/Station.png";

    protected override void OnValidate()
    {
        base.OnValidate();

        if (stationMarkerIcon != null)
        {
            return;
        }

        stationMarkerIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(StationMarkerIconAssetPath);
    }
#endif
}
