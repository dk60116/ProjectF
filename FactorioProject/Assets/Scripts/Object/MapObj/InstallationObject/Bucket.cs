using System;
using UnityEngine;

public class Bucket : InstallationObject, IMapObjectUpdateTick
{
    private const string EmptyBucketItemName = "Bucket";
    private const string WaterBucketItemName = "Water Bucket";
    private const float PipeFillReferenceLitersPerSecond = 1f;
    private const float FullEpsilonLiters = 0.0001f;
    private const float FluidInputBudgetIdleResetSeconds = 1f;

    [Header("Portable Water Surface")]
    [SerializeField]
    private Material portableWaterSurfaceMaterial;
    private MeshFilter installedBody;
    private PortableBucketWaterVisual installedWaterVisual;
    private bool waterBucketConversionPending;
    private int cachedWaterItemId = int.MinValue;
    private bool fullWaterSurfaceTransformResolved;
    private Vector3 fullWaterSurfaceLocalPosition;
    private Quaternion fullWaterSurfaceLocalRotation;
    private Vector3 fullWaterSurfaceLocalScale;
    private float fluidInputBudgetLiters;
    private float lastFluidInputBudgetTime = -1f;

    public Material PortableWaterSurfaceMaterial => portableWaterSurfaceMaterial;
    public bool IsInstalledWaterSurfaceVisible => installedWaterVisual != null
                                                  && installedWaterVisual.IsSurfaceVisible;
    public float InstalledWaterFillRatio => IsWaterBucketDefinition(ResolveBucketDefinition())
        ? 1f
        : GetEmptyBucketFillRatio();
    public override float FluidStorageCapacityLiters => IsEmptyBucketDefinition(ResolveBucketDefinition())
        ? ResolveFillDurationSeconds() * PipeFillReferenceLitersPerSecond
        : 0f;
    public float MaximumFluidInputLitersPerSecond
    {
        get
        {
            float fillDurationSeconds = ResolveFillDurationSeconds();
            return fillDurationSeconds > FullEpsilonLiters
                ? FluidStorageCapacityLiters / fillDurationSeconds
                : 0f;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        if (Application.isPlaying)
        {
            ResetFluidInputBudget(true);
            RefreshInstalledWaterVisual();
        }
    }

    protected override void OnDisable()
    {
        waterBucketConversionPending = false;
        ResetFluidInputBudget(false);
        MapObjectTickManager.UnregisterUpdateTick(this);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        waterBucketConversionPending = false;
        MapObjectTickManager.UnregisterUpdateTick(this);
        installedBody = null;
        installedWaterVisual = null;
        cachedWaterItemId = int.MinValue;
        fullWaterSurfaceTransformResolved = false;
        ResetFluidInputBudget(false);
        base.PrepareForPool();
    }

    public bool TryGetInstalledFullWaterSurfaceTransform(
        out Vector3 localPosition,
        out Quaternion localRotation,
        out Vector3 localScale)
    {
        if (!fullWaterSurfaceTransformResolved)
        {
            fullWaterSurfaceTransformResolved = TryResolveFullWaterSurfaceTransform(
                out fullWaterSurfaceLocalPosition,
                out fullWaterSurfaceLocalRotation,
                out fullWaterSurfaceLocalScale);
        }

        localPosition = fullWaterSurfaceTransformResolved
            ? fullWaterSurfaceLocalPosition
            : new Vector3(0f, 0.255f, 0f);
        localRotation = fullWaterSurfaceTransformResolved
            ? fullWaterSurfaceLocalRotation
            : Quaternion.identity;
        localScale = fullWaterSurfaceTransformResolved
            ? fullWaterSurfaceLocalScale
            : new Vector3(0.157573f, 1f, 0.157573f);
        return fullWaterSurfaceTransformResolved;
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        if (!waterBucketConversionPending)
        {
            MapObjectTickManager.UnregisterUpdateTick(this);
            return;
        }

        if (!isActiveAndEnabled || !TryGetPlacementRuntime(out _, out _))
        {
            waterBucketConversionPending = false;
            MapObjectTickManager.UnregisterUpdateTick(this);
            return;
        }

        if (!TryCompleteWaterBucketConversion())
        {
            return;
        }

        waterBucketConversionPending = false;
        MapObjectTickManager.UnregisterUpdateTick(this);
    }

    public static bool IsBucketDefinition(ItemDefinition definition)
    {
        return definition != null && definition.mapObject is Bucket;
    }

    public static bool IsEmptyBucketDefinition(ItemDefinition definition)
    {
        return IsBucketDefinition(definition)
               && NameMatches(definition, EmptyBucketItemName);
    }

    public static bool IsWaterBucketDefinition(ItemDefinition definition)
    {
        return IsBucketDefinition(definition)
               && NameMatches(definition, WaterBucketItemName);
    }

    public static bool TryResolveWaterBucketDefinition(
        ItemManager itemManager,
        out ItemDefinition waterBucketDefinition)
    {
        waterBucketDefinition = null;
        if (itemManager == null || itemManager.ItemDefinitions == null)
        {
            return false;
        }

        for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
        {
            ItemDefinition definition = itemManager.ItemDefinitions[i];
            if (!IsWaterBucketDefinition(definition))
            {
                continue;
            }

            waterBucketDefinition = definition;
            return true;
        }

        return false;
    }

    private static bool NameMatches(ItemDefinition definition, string expectedName)
    {
        return definition != null
               && string.Equals(
                   ItemDefinitionLookup.GetDisplayName(definition),
                   expectedName,
                   StringComparison.OrdinalIgnoreCase);
    }

    public override bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        return fluidItemId >= 0
               && fluidItemId == ResolveWaterItemId()
               && base.CanAcceptFluidItem(fluidItemId, requestedLiters);
    }

    public override bool CanProvideFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        return false;
    }

    protected override float LimitIncomingFluidLiters(int fluidItemId, float requestedLiters)
    {
        if (!Application.isPlaying
            || requestedLiters <= 0f
            || !IsEmptyBucketDefinition(ResolveBucketDefinition()))
        {
            return requestedLiters;
        }

        float currentTime = Time.time;
        if (lastFluidInputBudgetTime < 0f)
        {
            lastFluidInputBudgetTime = currentTime;
            fluidInputBudgetLiters = 0f;
            return 0f;
        }

        float elapsedSeconds = currentTime - lastFluidInputBudgetTime;
        lastFluidInputBudgetTime = currentTime;
        if (elapsedSeconds < 0f || elapsedSeconds > FluidInputBudgetIdleResetSeconds)
        {
            fluidInputBudgetLiters = 0f;
            return 0f;
        }

        float maxLitersPerSecond = MaximumFluidInputLitersPerSecond;
        fluidInputBudgetLiters = Mathf.Min(
            fluidInputBudgetLiters + maxLitersPerSecond * elapsedSeconds,
            maxLitersPerSecond * FluidInputBudgetIdleResetSeconds);
        return Mathf.Min(requestedLiters, fluidInputBudgetLiters);
    }

    protected override void OnStoredFluidAccepted(
        int fluidItemId,
        float previousStoredLiters,
        float acceptedLiters,
        float incomingTemperatureCelsius)
    {
        base.OnStoredFluidAccepted(
            fluidItemId,
            previousStoredLiters,
            acceptedLiters,
            incomingTemperatureCelsius);

        if (Application.isPlaying && IsEmptyBucketDefinition(ResolveBucketDefinition()))
        {
            fluidInputBudgetLiters = Mathf.Max(
                0f,
                fluidInputBudgetLiters - Mathf.Max(0f, acceptedLiters));
        }
    }

    protected override void OnStoredFluidChanged(
        int previousFluidItemId,
        float previousStoredLiters,
        int currentFluidItemId,
        float currentStoredLiters)
    {
        base.OnStoredFluidChanged(
            previousFluidItemId,
            previousStoredLiters,
            currentFluidItemId,
            currentStoredLiters);

        RefreshInstalledWaterVisual();
        if (!IsEmptyBucketDefinition(ResolveBucketDefinition())
            || currentFluidItemId != ResolveWaterItemId()
            || currentStoredLiters + FullEpsilonLiters < FluidStorageCapacityLiters
            || !TryGetPlacementRuntime(out _, out _))
        {
            return;
        }

        waterBucketConversionPending = true;
        MapObjectTickManager.RegisterUpdateTick(this);
    }

    private void RefreshInstalledWaterVisual()
    {
        ItemDefinition definition = ResolveBucketDefinition();
        bool isWaterBucket = IsWaterBucketDefinition(definition);
        float currentFillRatio = isWaterBucket ? 1f : GetEmptyBucketFillRatio();
        bool containsWater = isWaterBucket || currentFillRatio > 0.0001f;
        if (!containsWater && installedWaterVisual == null)
        {
            return;
        }

        if (installedBody == null && containsWater)
        {
            installedBody = ResolveInstalledBody();
        }

        if (installedBody == null)
        {
            installedWaterVisual?.Refresh(this, false, null, true);
            return;
        }

        if (installedWaterVisual == null && containsWater)
        {
            installedWaterVisual = GetComponent<PortableBucketWaterVisual>();
            if (installedWaterVisual == null)
            {
                installedWaterVisual = gameObject.AddComponent<PortableBucketWaterVisual>();
            }
        }

        installedWaterVisual?.Refresh(
            this,
            containsWater,
            installedBody,
            true,
            currentFillRatio,
            !isWaterBucket);
    }

    private float GetEmptyBucketFillRatio()
    {
        float capacity = FluidStorageCapacityLiters;
        return capacity > FullEpsilonLiters
            ? Mathf.Clamp01(StoredFluidLiters / capacity)
            : 0f;
    }

    private ItemDefinition ResolveBucketDefinition()
    {
        if (BoundItemDefinition != null)
        {
            return BoundItemDefinition;
        }

        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        return ItemDefinitionLookup.ResolveById(
            itemManager != null ? itemManager.ItemDefinitions : null,
            ResolveItemId());
    }

    private int ResolveWaterItemId()
    {
        if (cachedWaterItemId >= 0)
        {
            return cachedWaterItemId;
        }

        int resolvedItemId = Pump.ResolveWaterItemId(null);
        if (GameManager.Instance != null && GameManager.Instance.ItemManger != null)
        {
            cachedWaterItemId = resolvedItemId;
        }

        return resolvedItemId;
    }

    private float ResolveFillDurationSeconds()
    {
        ItemDefinition definition = ResolveBucketDefinition();
        return definition != null ? definition.BucketFillDurationSeconds : 10f;
    }

    private void ResetFluidInputBudget(bool startClock)
    {
        fluidInputBudgetLiters = 0f;
        lastFluidInputBudgetTime = startClock && Application.isPlaying
            ? Time.time
            : -1f;
    }

    private bool TryResolveFullWaterSurfaceTransform(
        out Vector3 localPosition,
        out Quaternion localRotation,
        out Vector3 localScale)
    {
        localPosition = default;
        localRotation = Quaternion.identity;
        localScale = Vector3.one;
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (!TryResolveWaterBucketDefinition(itemManager, out ItemDefinition waterBucketDefinition)
            || !(waterBucketDefinition.mapObject is Bucket waterBucketPrefab))
        {
            return false;
        }

        Transform[] prefabTransforms = waterBucketPrefab.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < prefabTransforms.Length; i++)
        {
            Transform candidate = prefabTransforms[i];
            if (candidate == null
                || !string.Equals(
                    candidate.gameObject.name,
                    PortableBucketWaterVisual.SurfaceObjectName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            localPosition = candidate.localPosition;
            localRotation = candidate.localRotation;
            localScale = candidate.localScale;
            return true;
        }

        return false;
    }

    private bool TryCompleteWaterBucketConversion()
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (!TryResolveWaterBucketDefinition(itemManager, out ItemDefinition waterBucketDefinition))
        {
            return false;
        }

        InstallationPlacementController placementController =
            FindFirstObjectByType<InstallationPlacementController>();
        return placementController != null
               && placementController.TryUpgradeInstalledObject(
                   this,
                   waterBucketDefinition,
                   out _);
    }

    private MeshFilter ResolveInstalledBody()
    {
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null
                || meshFilter.sharedMesh == null
                || string.Equals(
                    meshFilter.gameObject.name,
                    PortableBucketWaterVisual.SurfaceObjectName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            return meshFilter;
        }

        return null;
    }
}
