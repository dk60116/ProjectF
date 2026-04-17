using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum InstallationMapFilter
{
    None = 0,
    Ground = 1 << 0,
    Water = 1 << 1,
    Resource = 1 << 2,
    ItemArea = 1 << 3
}

public class InstallationObject : MapObject
{
    [SerializeField]
    private InstallationMapFilter mapFilter = InstallationMapFilter.Ground;
    [SerializeField, HideInInspector]
    private Vector2Int runtimeAnchorCoordinate;
    [SerializeField, HideInInspector]
    private int runtimeQuarterTurns;
    [SerializeField, HideInInspector]
    private List<Vector2Int> runtimeOccupiedCoordinates = new List<Vector2Int>();

    public InstallationMapFilter MapFilter
    {
        get => mapFilter == InstallationMapFilter.None ? InstallationMapFilter.Ground : mapFilter;
        set => mapFilter = value == InstallationMapFilter.None ? InstallationMapFilter.Ground : value;
    }

    public Vector2Int RuntimeAnchorCoordinate => runtimeAnchorCoordinate;
    public int RuntimeQuarterTurns => runtimeQuarterTurns;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => runtimeOccupiedCoordinates;

    public void ConfigurePlacementRuntime(Vector2Int anchorCoordinate, int quarterTurns, IReadOnlyList<Vector2Int> occupiedCoordinates)
    {
        runtimeAnchorCoordinate = anchorCoordinate;
        runtimeQuarterTurns = ((quarterTurns % 4) + 4) % 4;

        if (runtimeOccupiedCoordinates == null)
        {
            runtimeOccupiedCoordinates = new List<Vector2Int>();
        }
        else
        {
            runtimeOccupiedCoordinates.Clear();
        }

        if (occupiedCoordinates == null)
        {
            return;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = occupiedCoordinates[i];
            if (!runtimeOccupiedCoordinates.Contains(coordinate))
            {
                runtimeOccupiedCoordinates.Add(coordinate);
            }
        }
    }

    public bool TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
    {
        anchorCoordinate = runtimeAnchorCoordinate;
        quarterTurns = runtimeQuarterTurns;
        return runtimeOccupiedCoordinates != null && runtimeOccupiedCoordinates.Count > 0;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (mapFilter == InstallationMapFilter.None)
        {
            mapFilter = InstallationMapFilter.Ground;
        }
    }
#endif
}
