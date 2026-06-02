using System;
using System.Collections.Generic;
using ProjectF.Attributes;
using UnityEngine;
using UnityEngine.Rendering;

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
    private const string PowerLinePointName = "PowerLinePoint";
    private const string LowercasePowerLinePointName = "powerLinePoint";
    private const string UtilityPoleLineNamePrefix = "UtilityPole_Line_";

    public static event Action<InstallationObject> PlacementRuntimeChanged;
    public static event Action<InstallationObject> PlacementRuntimeCleared;

    [SerializeField]
    private Animator animator;
    [SerializeField]
    protected ParticleSystem particleEffect;


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
    [SerializeField, HideInInspector]
    private int storedFluidItemId = -1;
    [SerializeField, HideInInspector]
    private float storedFluidTemperatureCelsius = MapClimate.DefaultCurrentTemperatureCelsius;

    private float fluidInSampleLiters;
    private float fluidInSampleStartTime = -1f;
    private float fluidInLastReceiveTime = -1f;
    private float fluidInRateLitersPerSecond;
    private readonly List<Renderer> runtimeShadowRenderers = new List<Renderer>();

    [SerializeField]
    private Transform powerLinePoint;

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
    public bool TryGetPowerLinePoint(out Transform linePoint)
    {
        linePoint = ResolvePowerLinePoint();
        return linePoint != null;
    }

    public float StoredFluidLiters => Mathf.Max(0f, storedFluidLiters);
    public int StoredFluidItemId => StoredFluidLiters > 0.0001f ? storedFluidItemId : -1;
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

    public static bool CollectActiveInstallationsAtRuntimeGridCoordinate(
        Vector2Int coordinate,
        List<InstallationObject> results)
    {
        if (results == null || ActiveInstances.Count <= 0)
        {
            return false;
        }

        bool addedAny = false;
        foreach (InstallationObject installationObject in ActiveInstances)
        {
            if (installationObject == null
                || !installationObject.gameObject.activeInHierarchy
                || installationObject.runtimeOccupiedCoordinates == null
                || installationObject.runtimeOccupiedCoordinates.Count <= 0)
            {
                continue;
            }

            for (int i = 0; i < installationObject.runtimeOccupiedCoordinates.Count; i++)
            {
                if (installationObject.runtimeOccupiedCoordinates[i] != coordinate)
                {
                    continue;
                }

                if (!results.Contains(installationObject))
                {
                    results.Add(installationObject);
                    addedAny = true;
                }

                break;
            }
        }

        return addedAny;
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

        if (occupiedCoordinates != null)
        {
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                Vector2Int coordinate = occupiedCoordinates[i];
                if (!runtimeOccupiedCoordinates.Contains(coordinate))
                {
                    runtimeOccupiedCoordinates.Add(coordinate);
                }
            }
        }

        OnPlacementRuntimeChanged();
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

        OnPlacementRuntimeCleared();

        ApplyItemFilterMask(null, false);
        storedFluidLiters = 0f;
        storedFluidItemId = -1;
        storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        ClearFluidInRate();
        transform.localPosition = Vector3.zero;
        RefreshInstalledDirectionFromCurrentTransform();
    }

    protected virtual void OnPlacementRuntimeChanged()
    {
        PlacementRuntimeChanged?.Invoke(this);
    }

    protected virtual void OnPlacementRuntimeCleared()
    {
        PlacementRuntimeCleared?.Invoke(this);
    }

    public bool TryAddFluidLiters(float requestedLiters, out float acceptedLiters)
    {
        return TryAddFluidLiters(
            storedFluidItemId,
            requestedLiters,
            GetStoredFluidTemperatureCelsius(storedFluidItemId),
            out acceptedLiters);
    }

    public bool TryAddFluidLiters(int fluidItemId, float requestedLiters, out float acceptedLiters)
    {
        return TryAddFluidLiters(
            fluidItemId,
            requestedLiters,
            MapClimate.CurrentTemperatureCelsius,
            out acceptedLiters);
    }

    public bool TryAddFluidLiters(
        int fluidItemId,
        float requestedLiters,
        float incomingTemperatureCelsius,
        out float acceptedLiters)
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
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
            return false;
        }

        if (!CanAcceptFluidItem(fluidItemId))
        {
            return false;
        }

        storedFluidLiters = Mathf.Clamp(storedFluidLiters, 0f, capacity);
        if (storedFluidLiters <= 0.0001f)
        {
            storedFluidLiters = 0f;
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        }
        else if (fluidItemId >= 0 && storedFluidItemId >= 0 && storedFluidItemId != fluidItemId)
        {
            return false;
        }

        float availableLiters = capacity - storedFluidLiters;
        if (availableLiters <= 0.0001f)
        {
            return false;
        }

        float previousStoredLiters = storedFluidLiters;
        acceptedLiters = Mathf.Min(requestedLiters, availableLiters);
        storedFluidLiters += acceptedLiters;
        if (acceptedLiters > 0.0001f && fluidItemId >= 0)
        {
            storedFluidItemId = fluidItemId;
        }

        RecordFluidIn(acceptedLiters);
        OnStoredFluidAccepted(
            fluidItemId,
            previousStoredLiters,
            acceptedLiters,
            NormalizeFluidTemperatureCelsius(incomingTemperatureCelsius));
        return acceptedLiters > 0f;
    }

    public bool TryConsumeFluidLiters(float requestedLiters, out float consumedLiters)
    {
        return TryConsumeFluidLiters(-1, requestedLiters, out consumedLiters);
    }

    public bool TryConsumeFluidLiters(int fluidItemId, float requestedLiters, out float consumedLiters)
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
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
            return false;
        }

        storedFluidLiters = Mathf.Clamp(storedFluidLiters, 0f, capacity);
        if (storedFluidLiters <= 0.0001f)
        {
            storedFluidLiters = 0f;
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
            return false;
        }

        if (fluidItemId >= 0 && storedFluidItemId >= 0 && storedFluidItemId != fluidItemId)
        {
            return false;
        }

        if (fluidItemId >= 0 && storedFluidItemId < 0)
        {
            storedFluidItemId = fluidItemId;
        }

        consumedLiters = Mathf.Min(requestedLiters, storedFluidLiters);
        storedFluidLiters = Mathf.Max(0f, storedFluidLiters - consumedLiters);
        if (storedFluidLiters <= 0.0001f)
        {
            storedFluidLiters = 0f;
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        }

        return consumedLiters > 0f;
    }

    public void SetStoredFluidLiters(float liters)
    {
        SetStoredFluid(storedFluidItemId, liters);
    }

    public void SetStoredFluid(int fluidItemId, float liters)
    {
        SetStoredFluid(fluidItemId, liters, GetStoredFluidTemperatureCelsius(fluidItemId));
    }

    public void SetStoredFluid(int fluidItemId, float liters, float temperatureCelsius)
    {
        float capacity = FluidStorageCapacityLiters;
        storedFluidLiters = capacity > 0f
            ? Mathf.Clamp(liters, 0f, capacity)
            : 0f;
        storedFluidItemId = storedFluidLiters > 0.0001f && fluidItemId >= 0
            ? fluidItemId
            : -1;
        storedFluidTemperatureCelsius = storedFluidItemId >= 0
            ? NormalizeFluidTemperatureCelsius(temperatureCelsius)
            : MapClimate.CurrentTemperatureCelsius;
    }

    public virtual bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        if (!CanStoreFluid || (requestedLiters > 0f && AvailableFluidStorageLiters + 0.0001f < requestedLiters))
        {
            return false;
        }

        return storedFluidLiters <= 0.0001f
               || storedFluidItemId < 0
               || fluidItemId < 0
               || storedFluidItemId == fluidItemId;
    }

    public virtual bool CanProvideFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        if (!CanStoreFluid || StoredFluidLiters <= 0.0001f)
        {
            return false;
        }

        if (requestedLiters > 0f && StoredFluidLiters + 0.0001f < requestedLiters)
        {
            return false;
        }

        return storedFluidItemId < 0
               || fluidItemId < 0
               || storedFluidItemId == fluidItemId;
    }

    public virtual float GetStoredFluidTemperatureCelsius(int fluidItemId)
    {
        return StoredFluidLiters > 0.0001f
               && storedFluidItemId >= 0
               && (fluidItemId < 0 || storedFluidItemId == fluidItemId)
            ? NormalizeFluidTemperatureCelsius(storedFluidTemperatureCelsius)
            : MapClimate.CurrentTemperatureCelsius;
    }

    protected virtual void OnStoredFluidAccepted(
        int fluidItemId,
        float previousStoredLiters,
        float acceptedLiters,
        float incomingTemperatureCelsius)
    {
        if (acceptedLiters <= 0.0001f)
        {
            return;
        }

        float previousLiters = Mathf.Max(0f, previousStoredLiters);
        float totalLiters = previousLiters + acceptedLiters;
        if (totalLiters <= 0.0001f)
        {
            storedFluidTemperatureCelsius = NormalizeFluidTemperatureCelsius(incomingTemperatureCelsius);
            return;
        }

        float previousTemperature = previousLiters > 0.0001f
            ? NormalizeFluidTemperatureCelsius(storedFluidTemperatureCelsius)
            : NormalizeFluidTemperatureCelsius(incomingTemperatureCelsius);
        storedFluidTemperatureCelsius = NormalizeFluidTemperatureCelsius(
            ((previousTemperature * previousLiters)
             + (NormalizeFluidTemperatureCelsius(incomingTemperatureCelsius) * acceptedLiters)) / totalLiters);
    }

    protected void SetStoredFluidTemperatureCelsius(float temperatureCelsius)
    {
        storedFluidTemperatureCelsius = NormalizeFluidTemperatureCelsius(temperatureCelsius);
    }

    protected static float NormalizeFluidTemperatureCelsius(float temperatureCelsius)
    {
        return float.IsNaN(temperatureCelsius) || float.IsInfinity(temperatureCelsius)
            ? MapClimate.CurrentTemperatureCelsius
            : temperatureCelsius;
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
        ApplyRuntimeShadowSettings();
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

    protected virtual void ApplyRuntimeShadowSettings()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ClearRuntimeStaticFlags(transform);

        runtimeShadowRenderers.Clear();
        GetComponentsInChildren(true, runtimeShadowRenderers);

        for (int i = 0; i < runtimeShadowRenderers.Count; i++)
        {
            Renderer renderer = runtimeShadowRenderers[i];
            if (!ShouldApplyRuntimeShadowSettings(renderer))
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static bool ShouldApplyRuntimeShadowSettings(Renderer renderer)
    {
        if (renderer == null
            || renderer is LineRenderer
            || renderer is ParticleSystemRenderer
            || renderer is SpriteRenderer
            || renderer.GetComponent<WorkableObjectRangeVisual>() != null
            || renderer.GetComponent<TMPro.TextMeshPro>() != null)
        {
            return false;
        }

        return (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
               && !renderer.gameObject.name.StartsWith(UtilityPoleLineNamePrefix, StringComparison.Ordinal);
    }

    private static void ClearRuntimeStaticFlags(Transform root)
    {
        if (root == null)
        {
            return;
        }

        root.gameObject.isStatic = false;

        for (int i = 0; i < root.childCount; i++)
        {
            ClearRuntimeStaticFlags(root.GetChild(i));
        }
    }

    private Transform ResolvePowerLinePoint()
    {
        if (powerLinePoint != null && powerLinePoint.IsChildOf(transform))
        {
            return powerLinePoint;
        }

        powerLinePoint = FindDescendantByName(transform, PowerLinePointName)
                         ?? FindDescendantByName(transform, LowercasePowerLinePointName);
        return powerLinePoint != null ? powerLinePoint : transform;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.name == targetName)
            {
                return child;
            }

            Transform nested = FindDescendantByName(child, targetName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
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

        SetStoredFluid(storedFluidItemId, storedFluidLiters);

        globalMaxFocusActivationRadiusDirty = true;
        RefreshInstalledDirectionFromCurrentTransform();
    }
#endif
}
