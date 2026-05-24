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
    Ore = 1 << 2,
    ItemArea = 1 << 3,
    Tree = 1 << 4,
    WaterOutline = 1 << 5,
    Pipe = 1 << 6
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
    public const InstallationMapFilter DefaultMapFilter =
        InstallationMapFilter.Ground | InstallationMapFilter.Ore | InstallationMapFilter.WaterOutline;
    private const float FluidInRateSampleSeconds = 0.25f;
    private const float FluidInRateIdleResetSeconds = 0.75f;

    [SerializeField]
    private Animator animator;

    private static readonly HashSet<InstallationObject> ActiveInstances = new HashSet<InstallationObject>();
    private static float cachedGlobalMaxFocusActivationRadius;
    private static bool globalMaxFocusActivationRadiusDirty = true;
    private static long nextPlacementSequence = 1;

    [SerializeField]
    private InstallationMapFilter mapFilter = DefaultMapFilter;
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
    [SerializeField, HideInInspector, Min(0f)]
    private float storedFluidLiters;

    private float fluidInSampleLiters;
    private float fluidInSampleStartTime = -1f;
    private float fluidInLastReceiveTime = -1f;
    private float fluidInRateLitersPerSecond;

    public InstallationMapFilter MapFilter
    {
        get => mapFilter == InstallationMapFilter.None ? DefaultMapFilter : mapFilter;
        set => mapFilter = value == InstallationMapFilter.None ? DefaultMapFilter : value;
    }

    public virtual float FocusActivationRadius => Mathf.Max(0f, installationFocusRadius);
    public InstallationFacingDirection InstalledDirection => installedDirection;
    public Vector2Int RuntimeAnchorCoordinate => runtimeAnchorCoordinate;
    public int RuntimeQuarterTurns => runtimeQuarterTurns;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => runtimeOccupiedCoordinates;
    public long RuntimePlacementSequence => runtimePlacementSequence;
    public float StoredFluidLiters => Mathf.Max(0f, storedFluidLiters);
    public float FluidStorageCapacityLiters
    {
        get
        {
            ItemDefinition definition = ResolveFluidStorageDefinition();
            return definition != null && definition.storesFluid
                ? Mathf.Max(0f, definition.fluidStorageLiters)
                : 0f;
        }
    }
    public float AvailableFluidStorageLiters => Mathf.Max(0f, FluidStorageCapacityLiters - StoredFluidLiters);
    public bool CanStoreFluid => FluidStorageCapacityLiters > 0f;
    public bool HasFluidStorageSpace => AvailableFluidStorageLiters > 0.0001f;
    public float FluidInLitersPerSecond
    {
        get
        {
            RefreshFluidInRate();
            return Mathf.Max(0f, fluidInRateLitersPerSecond);
        }
    }
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
        storedFluidLiters = 0f;
        ClearFluidInRate();
        transform.localPosition = Vector3.zero;
        RefreshInstalledDirectionFromCurrentTransform();
    }

    public bool TryAddFluidLiters(float requestedLiters, out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (requestedLiters <= 0f)
        {
            return false;
        }

        float capacity = FluidStorageCapacityLiters;
        if (capacity <= 0f)
        {
            storedFluidLiters = 0f;
            return false;
        }

        storedFluidLiters = Mathf.Clamp(storedFluidLiters, 0f, capacity);
        float availableLiters = capacity - storedFluidLiters;
        if (availableLiters <= 0.0001f)
        {
            return false;
        }

        acceptedLiters = Mathf.Min(requestedLiters, availableLiters);
        storedFluidLiters += acceptedLiters;
        RecordFluidIn(acceptedLiters);
        return acceptedLiters > 0f;
    }

    public bool TryConsumeFluidLiters(float requestedLiters, out float consumedLiters)
    {
        consumedLiters = 0f;
        if (requestedLiters <= 0f)
        {
            return false;
        }

        float capacity = FluidStorageCapacityLiters;
        if (capacity <= 0f)
        {
            storedFluidLiters = 0f;
            return false;
        }

        storedFluidLiters = Mathf.Clamp(storedFluidLiters, 0f, capacity);
        if (storedFluidLiters <= 0.0001f)
        {
            return false;
        }

        consumedLiters = Mathf.Min(requestedLiters, storedFluidLiters);
        storedFluidLiters = Mathf.Max(0f, storedFluidLiters - consumedLiters);
        return consumedLiters > 0f;
    }

    public void SetStoredFluidLiters(float liters)
    {
        float capacity = FluidStorageCapacityLiters;
        storedFluidLiters = capacity > 0f
            ? Mathf.Clamp(liters, 0f, capacity)
            : 0f;
    }

    private void RecordFluidIn(float liters)
    {
        if (liters <= 0f || !Application.isPlaying)
        {
            return;
        }

        float now = Time.time;
        if (fluidInSampleStartTime < 0f
            || fluidInLastReceiveTime < 0f
            || now - fluidInLastReceiveTime > FluidInRateIdleResetSeconds)
        {
            fluidInSampleStartTime = now;
            fluidInSampleLiters = 0f;
            fluidInRateLitersPerSecond = 0f;
        }

        fluidInSampleLiters += liters;
        fluidInLastReceiveTime = now;
        RefreshFluidInRate(now);
    }

    private void RefreshFluidInRate()
    {
        if (!Application.isPlaying)
        {
            ClearFluidInRate();
            return;
        }

        RefreshFluidInRate(Time.time);
    }

    private void RefreshFluidInRate(float now)
    {
        if (fluidInLastReceiveTime < 0f
            || now - fluidInLastReceiveTime > FluidInRateIdleResetSeconds)
        {
            ClearFluidInRate(now);
            return;
        }

        float elapsed = now - fluidInSampleStartTime;
        if (elapsed < FluidInRateSampleSeconds)
        {
            return;
        }

        fluidInRateLitersPerSecond = elapsed > 0.0001f
            ? fluidInSampleLiters / elapsed
            : 0f;
        fluidInSampleLiters = 0f;
        fluidInSampleStartTime = now;
    }

    private void ClearFluidInRate()
    {
        ClearFluidInRate(-1f);
    }

    private void ClearFluidInRate(float sampleStartTime)
    {
        fluidInSampleLiters = 0f;
        fluidInSampleStartTime = sampleStartTime;
        fluidInLastReceiveTime = -1f;
        fluidInRateLitersPerSecond = 0f;
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

    protected Animator ResolveInstallationAnimator()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        return animator;
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

    private ItemDefinition ResolveFluidStorageDefinition()
    {
        if (BoundItemDefinition != null)
        {
            return BoundItemDefinition;
        }

        int itemId = ResolveItemId();
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (mapFilter == InstallationMapFilter.None)
        {
            mapFilter = DefaultMapFilter;
        }

        if (installationFocusRadius < 0f)
        {
            installationFocusRadius = 0f;
        }

        SetStoredFluidLiters(storedFluidLiters);

        globalMaxFocusActivationRadiusDirty = true;
        RefreshInstalledDirectionFromCurrentTransform();
    }
#endif
}
