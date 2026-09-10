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
    Pipe = 1 << 6,
    Railload = 1 << 7,
    Floor = 1 << 8,
    OtherInstallObject = 1 << 9,
    Oil = 1 << 10
}

public enum InstallationRotationFilter
{
    All = 0,
    [InspectorName("Horizontal and Vertical")]
    HorizontalAndVertical = 1,
    Fixed = 2
}

public enum InstallationFacingDirection
{
    PositiveZ,
    PositiveX,
    NegativeZ,
    NegativeX
}

public interface IPlayerMapObjectInteraction
{
    bool CanPlayerInteract(Player player);

    bool TryPlayerInteract(Player player);

    int GetInteractionIconItemId(Player player);
}

public interface IPersistentInstallationItemStorage
{
    int PersistentStoredItemId { get; }

    void ApplyPersistentStoredItemId(int itemId);
}

public interface IPersistentInstallationItemCollectionStorage
{
    void CapturePersistentStoredItemIds(List<int> destination);

    void ApplyPersistentStoredItemIds(IReadOnlyList<int> itemIds);
}

public interface IPlayerItemStorage
{
    bool TryAddItemStack(
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float moveInterval,
        out int addedCount);

    bool TryPickupOneItemToBag(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredSlotIndex,
        int preferredItemId = -1);

    bool TryPickupOneItemToHand(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId = -1);

    bool TryPreviewPickupItems(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId,
        out int previewItemId,
        out int previewPickupCount);
}

public interface IPlayerItemStoragePortablePreview
{
    bool TryPreviewPickupItems(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId,
        out int previewItemId,
        out int previewPickupCount,
        out PortableObject previewPortableObject);
}

public partial class InstallationObject : MapObject, IMapObjectSimulationIdentity
{
    protected const float ConnectedFluidStorageTransferLitersPerSecond = 50f;

    public const InstallationMapFilter DefaultMapFilter =
        InstallationMapFilter.Ground
        | InstallationMapFilter.Ore
        | InstallationMapFilter.WaterOutline
        | InstallationMapFilter.Floor;
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
    private static readonly Dictionary<Vector2Int, List<InstallationObject>> ActiveInstancesByRuntimeGridCoordinate =
        new Dictionary<Vector2Int, List<InstallationObject>>();
    private static float cachedGlobalMaxFocusActivationRadius;
    private static bool globalMaxFocusActivationRadiusDirty = true;
    private static long nextPlacementSequence = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSimulationIdentityState()
    {
        nextPlacementSequence = 1L;
    }

    [SerializeField]
    private InstallationMapFilter mapFilter = DefaultMapFilter;
    [SerializeField]
    private InstallationRotationFilter rotationFilter = InstallationRotationFilter.All;
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
    [SerializeField, HideInInspector]
    private bool excludeFromTerrainPersistence;
    [SerializeField, HideInInspector, Min(0f)]
    private long storedFluidUnits;
    [SerializeField, HideInInspector]
    private int storedFluidItemId = -1;
    [SerializeField, HideInInspector]
    private float storedFluidTemperatureCelsius = MapClimate.DefaultCurrentTemperatureCelsius;

    private float fluidInSampleLiters;
    private float fluidInSampleStartTime = -1f;
    private float fluidInLastReceiveTime = -1f;
    private float fluidInRateLitersPerSecond;
    private readonly List<Renderer> runtimeShadowRenderers = new List<Renderer>();
    private bool runtimeCoordinateIndexRegistered;

    [SerializeField]
    private Transform powerLinePoint;

    public InstallationMapFilter MapFilter
    {
        get => NormalizeMapFilter(mapFilter);
        set => mapFilter = NormalizeMapFilter(value);
    }

    public InstallationRotationFilter RotationFilter
    {
        get => NormalizeRotationFilter(rotationFilter);
        set => rotationFilter = NormalizeRotationFilter(value);
    }

    public static InstallationMapFilter NormalizeMapFilter(InstallationMapFilter filter)
    {
        InstallationMapFilter normalizedFilter = filter == InstallationMapFilter.None
            ? DefaultMapFilter
            : filter;
        return normalizedFilter | InstallationMapFilter.Floor;
    }

    public static InstallationRotationFilter NormalizeRotationFilter(InstallationRotationFilter filter)
    {
        return filter switch
        {
            InstallationRotationFilter.HorizontalAndVertical => InstallationRotationFilter.HorizontalAndVertical,
            InstallationRotationFilter.Fixed => InstallationRotationFilter.Fixed,
            _ => InstallationRotationFilter.All
        };
    }

    public virtual float FocusActivationRadius => Mathf.Max(0f, installationFocusRadius);
    public InstallationFacingDirection InstalledDirection => installedDirection;
    public Vector2Int RuntimeAnchorCoordinate => runtimeAnchorCoordinate;
    public int RuntimeQuarterTurns => runtimeQuarterTurns;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => runtimeOccupiedCoordinates;
    public long RuntimePlacementSequence => runtimePlacementSequence;
    public long SimulationId => runtimePlacementSequence;
    public static long NextSimulationId => nextPlacementSequence;
    public bool ExcludeFromTerrainPersistence => excludeFromTerrainPersistence;
    public bool TryGetPowerLinePoint(out Transform linePoint)
    {
        linePoint = ResolvePowerLinePoint();
        return linePoint != null;
    }

    public long StoredFluidUnits => Math.Max(0L, storedFluidUnits);
    public float StoredFluidLiters => DeterministicSimulationUnits.ToFloat(StoredFluidUnits);
    public int StoredFluidItemId => StoredFluidUnits > 0L ? storedFluidItemId : -1;
    public virtual float FluidStorageCapacityLiters
    {
        get
        {
            ItemDefinition definition = ResolveFluidStorageDefinition();
            return definition != null && definition.storesFluid
                ? Mathf.Max(0f, definition.fluidStorageLiters)
                : 0f;
        }
    }
    public float AvailableFluidStorageLiters => DeterministicSimulationUnits.ToFloat(
        Math.Max(0L, FluidStorageCapacityUnits - StoredFluidUnits));
    public bool CanStoreFluid => FluidStorageCapacityLiters > 0f;
    public bool HasFluidStorageSpace => FluidStorageCapacityUnits > StoredFluidUnits;
    private long FluidStorageCapacityUnits =>
        DeterministicSimulationUnits.FromFloat(FluidStorageCapacityLiters);

    protected static float CalculateFluidEqualizationTransferLiters(
        InstallationObject sourceStorage,
        InstallationObject targetStorage)
    {
        if (sourceStorage == null || targetStorage == null)
        {
            return 0f;
        }

        long sourceCapacity = sourceStorage.FluidStorageCapacityUnits;
        long targetCapacity = targetStorage.FluidStorageCapacityUnits;
        if (sourceCapacity <= 0L || targetCapacity <= 0L)
        {
            return 0f;
        }

        long sourceUnits = Math.Min(sourceStorage.StoredFluidUnits, sourceCapacity);
        long targetUnits = Math.Min(targetStorage.StoredFluidUnits, targetCapacity);
        decimal numerator = (decimal)sourceUnits * targetCapacity
                            - (decimal)targetUnits * sourceCapacity;
        if (numerator <= 0m)
        {
            return 0f;
        }

        long transferUnits = (long)decimal.Truncate(
            numerator / (sourceCapacity + targetCapacity));
        return DeterministicSimulationUnits.ToFloat(transferUnits);
    }

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
        if (results == null
            || ActiveInstancesByRuntimeGridCoordinate.Count <= 0
            || !ActiveInstancesByRuntimeGridCoordinate.TryGetValue(coordinate, out List<InstallationObject> installations)
            || installations == null
            || installations.Count <= 0)
        {
            return false;
        }

        for (int i = installations.Count - 1; i >= 0; i--)
        {
            InstallationObject installationObject = installations[i];
            if (installationObject == null
                || !installationObject.gameObject.activeInHierarchy
                || !installationObject.runtimeCoordinateIndexRegistered
                || installationObject.runtimeOccupiedCoordinates == null
                || installationObject.runtimeOccupiedCoordinates.Count <= 0
                || !installationObject.ContainsRuntimeCoordinate(coordinate))
            {
                installations.RemoveAt(i);
            }
        }

        if (installations.Count == 0)
        {
            ActiveInstancesByRuntimeGridCoordinate.Remove(coordinate);
            return false;
        }

        bool addedAny = false;
        for (int i = 0; i < installations.Count; i++)
        {
            InstallationObject installationObject = installations[i];
            if (!results.Contains(installationObject))
            {
                results.Add(installationObject);
                addedAny = true;
            }
        }

        return addedAny;
    }

    private bool ContainsRuntimeCoordinate(Vector2Int coordinate)
    {
        if (runtimeOccupiedCoordinates == null)
        {
            return false;
        }

        for (int i = 0; i < runtimeOccupiedCoordinates.Count; i++)
        {
            if (runtimeOccupiedCoordinates[i] == coordinate)
            {
                return true;
            }
        }

        return false;
    }

    public void ConfigurePlacementRuntime(
        Vector2Int anchorCoordinate,
        int quarterTurns,
        IReadOnlyList<Vector2Int> occupiedCoordinates,
        long placementSequence = 0)
    {
        UnregisterRuntimeCoordinateIndex(this);

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

        RegisterRuntimeCoordinateIndex(this);
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

    public static void RestoreNextSimulationId(long nextSimulationId)
    {
        nextPlacementSequence = Math.Max(1L, nextSimulationId);
    }

    public void SetExcludeFromTerrainPersistence(bool exclude)
    {
        excludeFromTerrainPersistence = exclude;
    }

    public bool TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
    {
        anchorCoordinate = runtimeAnchorCoordinate;
        quarterTurns = runtimeQuarterTurns;
        return runtimeOccupiedCoordinates != null && runtimeOccupiedCoordinates.Count > 0;
    }

    public virtual void PrepareForPool()
    {
        UnregisterRuntimeCoordinateIndex(this);

        runtimeAnchorCoordinate = default;
        runtimeQuarterTurns = 0;
        runtimePlacementSequence = 0;
        excludeFromTerrainPersistence = false;
        if (runtimeOccupiedCoordinates != null)
        {
            runtimeOccupiedCoordinates.Clear();
        }

        OnPlacementRuntimeCleared();

        ApplyItemFilterMask(null, false);
        storedFluidUnits = 0L;
        storedFluidItemId = -1;
        storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        ClearFluidInRate();
        transform.localPosition = Vector3.zero;
        RefreshInstalledDirectionFromCurrentTransform();
    }

    protected virtual void OnPlacementRuntimeChanged()
    {
        if (this is IMapObjectUpdateTick updateTick)
        {
            MapObjectTickManager.RefreshSimulationIdentity(updateTick);
        }

        PlacementRuntimeChanged?.Invoke(this);
    }

    protected virtual void OnPlacementRuntimeCleared()
    {
        if (this is IMapObjectUpdateTick updateTick)
        {
            MapObjectTickManager.RefreshSimulationIdentity(updateTick);
        }

        PlacementRuntimeCleared?.Invoke(this);
    }

    public static int CompareSimulationOrder(InstallationObject left, InstallationObject right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int result = left.SimulationId.CompareTo(right.SimulationId);
        if (result != 0)
        {
            return result;
        }

        result = left.RuntimeAnchorCoordinate.x.CompareTo(right.RuntimeAnchorCoordinate.x);
        if (result != 0)
        {
            return result;
        }

        result = left.RuntimeAnchorCoordinate.y.CompareTo(right.RuntimeAnchorCoordinate.y);
        if (result != 0)
        {
            return result;
        }

        result = left.RuntimeQuarterTurns.CompareTo(right.RuntimeQuarterTurns);
        if (result != 0)
        {
            return result;
        }

        return string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
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
            storedFluidUnits = 0L;
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
            return false;
        }

        if (!CanAcceptFluidItem(fluidItemId))
        {
            return false;
        }

        long capacityUnits = DeterministicSimulationUnits.FromFloat(capacity);
        storedFluidUnits = Math.Min(Math.Max(0L, storedFluidUnits), capacityUnits);
        if (storedFluidUnits <= 0L)
        {
            storedFluidUnits = 0L;
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        }
        else if (fluidItemId >= 0 && storedFluidItemId >= 0 && storedFluidItemId != fluidItemId)
        {
            return false;
        }

        long availableUnits = capacityUnits - storedFluidUnits;
        if (availableUnits <= 0L)
        {
            return false;
        }

        float previousStoredLiters = StoredFluidLiters;
        int previousStoredFluidItemId = storedFluidItemId;
        float limitedRequestedLiters = Mathf.Clamp(
            LimitIncomingFluidLiters(fluidItemId, requestedLiters),
            0f,
            requestedLiters);
        if (limitedRequestedLiters <= 0.0001f)
        {
            return false;
        }

        long requestedUnits = DeterministicSimulationUnits.FromFloat(limitedRequestedLiters);
        long acceptedUnits = Math.Min(requestedUnits, availableUnits);
        acceptedLiters = DeterministicSimulationUnits.ToFloat(acceptedUnits);
        storedFluidUnits += acceptedUnits;
        if (acceptedUnits > 0L && fluidItemId >= 0)
        {
            storedFluidItemId = fluidItemId;
        }

        RecordFluidIn(acceptedLiters);
        OnStoredFluidAccepted(
            fluidItemId,
            previousStoredLiters,
            acceptedLiters,
            NormalizeFluidTemperatureCelsius(incomingTemperatureCelsius));
        NotifyStoredFluidChanged(previousStoredFluidItemId, previousStoredLiters);
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
            storedFluidUnits = 0L;
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
            return false;
        }

        long capacityUnits = DeterministicSimulationUnits.FromFloat(capacity);
        storedFluidUnits = Math.Min(Math.Max(0L, storedFluidUnits), capacityUnits);
        if (storedFluidUnits <= 0L)
        {
            storedFluidUnits = 0L;
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

        int previousStoredFluidItemId = storedFluidItemId;
        float previousStoredLiters = StoredFluidLiters;
        long requestedUnits = DeterministicSimulationUnits.FromFloat(requestedLiters);
        long consumedUnits = Math.Min(requestedUnits, storedFluidUnits);
        consumedLiters = DeterministicSimulationUnits.ToFloat(consumedUnits);
        storedFluidUnits = Math.Max(0L, storedFluidUnits - consumedUnits);
        if (storedFluidUnits <= 0L)
        {
            storedFluidUnits = 0L;
            storedFluidItemId = -1;
            storedFluidTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        }

        NotifyStoredFluidChanged(previousStoredFluidItemId, previousStoredLiters);
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
        SetStoredFluidUnits(
            fluidItemId,
            DeterministicSimulationUnits.FromFloat(liters),
            temperatureCelsius);
    }

    public void SetStoredFluidUnits(int fluidItemId, long fluidUnits, float temperatureCelsius)
    {
        int previousStoredFluidItemId = storedFluidItemId;
        float previousStoredLiters = StoredFluidLiters;
        long capacityUnits = FluidStorageCapacityUnits;
        storedFluidUnits = capacityUnits > 0L
            ? Math.Min(Math.Max(0L, fluidUnits), capacityUnits)
            : 0L;
        storedFluidItemId = storedFluidUnits > 0L && fluidItemId >= 0
            ? fluidItemId
            : -1;
        storedFluidTemperatureCelsius = storedFluidItemId >= 0
            ? NormalizeFluidTemperatureCelsius(temperatureCelsius)
            : MapClimate.CurrentTemperatureCelsius;
        NotifyStoredFluidChanged(previousStoredFluidItemId, previousStoredLiters);
    }

    public virtual bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        long requestedUnits = DeterministicSimulationUnits.FromFloat(requestedLiters);
        if (!CanStoreFluid
            || requestedUnits > 0L
            && Math.Max(0L, FluidStorageCapacityUnits - StoredFluidUnits) < requestedUnits)
        {
            return false;
        }

        return storedFluidUnits <= 0L
               || storedFluidItemId < 0
               || fluidItemId < 0
               || storedFluidItemId == fluidItemId;
    }

    public virtual bool CanProvideFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        if (!CanStoreFluid || StoredFluidUnits <= 0L)
        {
            return false;
        }

        long requestedUnits = DeterministicSimulationUnits.FromFloat(requestedLiters);
        if (requestedUnits > 0L && StoredFluidUnits < requestedUnits)
        {
            return false;
        }

        return storedFluidItemId < 0
               || fluidItemId < 0
               || storedFluidItemId == fluidItemId;
    }

    public virtual float GetStoredFluidTemperatureCelsius(int fluidItemId)
    {
        return StoredFluidUnits > 0L
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

    protected virtual float LimitIncomingFluidLiters(int fluidItemId, float requestedLiters)
    {
        return requestedLiters;
    }

    protected virtual void OnStoredFluidChanged(
        int previousFluidItemId,
        float previousStoredLiters,
        int currentFluidItemId,
        float currentStoredLiters)
    {
    }

    private void NotifyStoredFluidChanged(int previousFluidItemId, float previousStoredLiters)
    {
        float previousLiters = Mathf.Max(0f, previousStoredLiters);
        float currentLiters = StoredFluidLiters;
        int currentFluidItemId = StoredFluidItemId;
        if (previousFluidItemId == currentFluidItemId
            && Mathf.Abs(previousLiters - currentLiters) <= 0.0001f)
        {
            return;
        }

        OnStoredFluidChanged(
            previousFluidItemId,
            previousLiters,
            currentFluidItemId,
            currentLiters);
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

        float now = (float)MapObjectTickManager.CurrentSimulationTimeSeconds;
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

        RefreshFluidInRate((float)MapObjectTickManager.CurrentSimulationTimeSeconds);
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
        RefreshItemLight();
        ActiveInstances.Add(this);
        RegisterRuntimeCoordinateIndex(this);
        globalMaxFocusActivationRadiusDirty = true;
        RefreshInstalledDirectionFromCurrentTransform();
        ApplyRuntimeShadowSettings();
        RegisterManagedVisualUpdates();
    }

    protected virtual void OnDisable()
    {
        UnregisterManagedVisualUpdates();
        UnregisterRuntimeCoordinateIndex(this);
        ActiveInstances.Remove(this);
        globalMaxFocusActivationRadiusDirty = true;
    }

    private static void RegisterRuntimeCoordinateIndex(InstallationObject installationObject)
    {
        if (installationObject == null
            || installationObject.runtimeCoordinateIndexRegistered
            || !installationObject.isActiveAndEnabled
            || installationObject.runtimeOccupiedCoordinates == null
            || installationObject.runtimeOccupiedCoordinates.Count <= 0)
        {
            return;
        }

        for (int i = 0; i < installationObject.runtimeOccupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = installationObject.runtimeOccupiedCoordinates[i];
            if (!ActiveInstancesByRuntimeGridCoordinate.TryGetValue(coordinate, out List<InstallationObject> installations)
                || installations == null)
            {
                installations = new List<InstallationObject>(1);
                ActiveInstancesByRuntimeGridCoordinate[coordinate] = installations;
            }

            if (!installations.Contains(installationObject))
            {
                installations.Add(installationObject);
                installations.Sort(CompareSimulationOrder);
            }
        }

        installationObject.runtimeCoordinateIndexRegistered = true;
    }

    private static void UnregisterRuntimeCoordinateIndex(InstallationObject installationObject)
    {
        if (installationObject == null || !installationObject.runtimeCoordinateIndexRegistered)
        {
            return;
        }

        if (installationObject.runtimeOccupiedCoordinates != null)
        {
            for (int i = 0; i < installationObject.runtimeOccupiedCoordinates.Count; i++)
            {
                Vector2Int coordinate = installationObject.runtimeOccupiedCoordinates[i];
                if (!ActiveInstancesByRuntimeGridCoordinate.TryGetValue(coordinate, out List<InstallationObject> installations)
                    || installations == null)
                {
                    continue;
                }

                installations.Remove(installationObject);
                if (installations.Count == 0)
                {
                    ActiveInstancesByRuntimeGridCoordinate.Remove(coordinate);
                }
            }
        }

        installationObject.runtimeCoordinateIndexRegistered = false;
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
        mapFilter = NormalizeMapFilter(mapFilter);
        rotationFilter = NormalizeRotationFilter(rotationFilter);

        if (installationFocusRadius < 0f)
        {
            installationFocusRadius = 0f;
        }

        SetStoredFluid(storedFluidItemId, StoredFluidLiters);

        globalMaxFocusActivationRadiusDirty = true;
        RefreshInstalledDirectionFromCurrentTransform();
    }
#endif
}
