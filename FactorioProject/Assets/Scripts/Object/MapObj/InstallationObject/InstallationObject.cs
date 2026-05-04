using System;
using System.Collections.Generic;
using ProjectF.Attributes;
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

public enum InstallationFacingDirection
{
    PositiveZ,
    PositiveX,
    NegativeZ,
    NegativeX
}

public class InstallationObject : MapObject
{
    private static readonly HashSet<InstallationObject> ActiveInstances = new HashSet<InstallationObject>();
    private static float cachedGlobalMaxFocusActivationRadius;
    private static bool globalMaxFocusActivationRadiusDirty = true;
    private static long nextPlacementSequence = 1;

    [SerializeField]
    private InstallationMapFilter mapFilter = InstallationMapFilter.Ground;
    [SerializeField]
    [Min(0f)]
    private float installationFocusRadius = 1f;
    [SerializeField, ReadOnly]
    private InstallationFacingDirection installedDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField, HideInInspector]
    private Vector2Int runtimeAnchorCoordinate;
    [SerializeField, HideInInspector]
    private int runtimeQuarterTurns;
    [SerializeField, HideInInspector]
    private List<Vector2Int> runtimeOccupiedCoordinates = new List<Vector2Int>();
    [SerializeField, HideInInspector]
    private long runtimePlacementSequence;

    public InstallationMapFilter MapFilter
    {
        get => mapFilter == InstallationMapFilter.None ? InstallationMapFilter.Ground : mapFilter;
        set => mapFilter = value == InstallationMapFilter.None ? InstallationMapFilter.Ground : value;
    }

    public virtual float FocusActivationRadius => Mathf.Max(0f, installationFocusRadius);
    public InstallationFacingDirection InstalledDirection => installedDirection;
    public Vector2Int RuntimeAnchorCoordinate => runtimeAnchorCoordinate;
    public int RuntimeQuarterTurns => runtimeQuarterTurns;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => runtimeOccupiedCoordinates;
    public long RuntimePlacementSequence => runtimePlacementSequence;
    public static float GlobalMaxFocusActivationRadius
    {
        get
        {
            if (!globalMaxFocusActivationRadiusDirty)
            {
                return cachedGlobalMaxFocusActivationRadius;
            }

            cachedGlobalMaxFocusActivationRadius = 0f;
            foreach (InstallationObject installationObject in ActiveInstances)
            {
                if (installationObject == null)
                {
                    continue;
                }

                cachedGlobalMaxFocusActivationRadius = Mathf.Max(
                    cachedGlobalMaxFocusActivationRadius,
                    installationObject.FocusActivationRadius);
            }

            globalMaxFocusActivationRadiusDirty = false;
            return cachedGlobalMaxFocusActivationRadius;
        }
    }

    public void ConfigurePlacementRuntime(
        Vector2Int anchorCoordinate,
        int quarterTurns,
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        long placementSequence = 0)
    {
        runtimeAnchorCoordinate = anchorCoordinate;
        runtimeQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        runtimePlacementSequence = ClaimPlacementSequence(placementSequence);
        RefreshInstalledDirectionFromCurrentTransform();

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

    private static long ClaimPlacementSequence(long placementSequence)
    {
        if (placementSequence > 0)
        {
            if (placementSequence >= nextPlacementSequence)
            {
                nextPlacementSequence = placementSequence + 1;
            }

            return placementSequence;
        }

        return nextPlacementSequence++;
    }

    public static long ClaimNextPlacementSequence(long placementSequence = 0)
    {
        return ClaimPlacementSequence(placementSequence);
    }

    public bool TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
    {
        anchorCoordinate = runtimeAnchorCoordinate;
        quarterTurns = runtimeQuarterTurns;
        return runtimeOccupiedCoordinates != null && runtimeOccupiedCoordinates.Count > 0;
    }

    public virtual void PrepareForPool()
    {
        runtimeAnchorCoordinate = default;
        runtimeQuarterTurns = 0;
        runtimePlacementSequence = 0;
        if (runtimeOccupiedCoordinates != null)
        {
            runtimeOccupiedCoordinates.Clear();
        }

        ApplyItemFilterMask(null, false);
        transform.localPosition = Vector3.zero;
        RefreshInstalledDirectionFromCurrentTransform();
    }

    protected virtual void OnEnable()
    {
        ActiveInstances.Add(this);
        globalMaxFocusActivationRadiusDirty = true;
        RefreshInstalledDirectionFromCurrentTransform();
    }

    protected virtual void OnDisable()
    {
        ActiveInstances.Remove(this);
        globalMaxFocusActivationRadiusDirty = true;
    }

    protected void MarkFocusActivationRadiusDirty()
    {
        globalMaxFocusActivationRadiusDirty = true;
    }

    public void RefreshInstalledDirectionFromCurrentTransform()
    {
        installedDirection = ResolveInstalledDirection(transform.rotation);
    }

    protected virtual InstallationFacingDirection ResolveInstalledDirection(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return InstallationFacingDirection.PositiveZ;
        }

        forward.Normalize();
        if (Mathf.Abs(forward.x) >= Mathf.Abs(forward.z))
        {
            return forward.x >= 0f
                ? InstallationFacingDirection.PositiveX
                : InstallationFacingDirection.NegativeX;
        }

        return forward.z >= 0f
            ? InstallationFacingDirection.PositiveZ
            : InstallationFacingDirection.NegativeZ;
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (mapFilter == InstallationMapFilter.None)
        {
            mapFilter = InstallationMapFilter.Ground;
        }

        if (installationFocusRadius < 0f)
        {
            installationFocusRadius = 0f;
        }

        globalMaxFocusActivationRadiusDirty = true;
        RefreshInstalledDirectionFromCurrentTransform();
    }
#endif
}
