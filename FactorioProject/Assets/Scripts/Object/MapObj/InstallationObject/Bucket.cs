using System;
using System.Collections.Generic;
using UnityEngine;

public class Bucket : InstallationObject,
    IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IPlayerMapObjectInteraction
{
    private const string EmptyBucketItemName = "Bucket";
    private const string WaterBucketItemName = "Water Bucket";
    private const string OilBucketItemName = "Oil Bucket";
    private const string OilItemName = "Oil";
    private const int DefaultOilItemId = 4;
    private const float PipeFillReferenceLitersPerSecond = 1f;
    private const float FullEpsilonLiters = 0.0001f;
    private const float FluidInputBudgetMaximumAccrualSeconds = 1f;
    private const float FluidInputBudgetIdleResetSeconds = 2f;
    private const float ConnectedFluidPullIntervalSeconds = 0.1f;
    private const float MissingFluidSourceRetrySeconds = 1f;
    private const int MaximumPipeSearchNodes = 1024;
    private static readonly Vector2Int[] FluidCardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    [Header("Portable Fluid Surface")]
    [SerializeField]
    private Material portableWaterSurfaceMaterial;
    [SerializeField]
    private Material portableOilSurfaceMaterial;
    private MeshFilter installedBody;
    private PortableBucketWaterVisual installedFluidVisual;
    private bool bucketConversionPending;
    private int cachedWaterItemId = int.MinValue;
    private int cachedOilItemId = int.MinValue;
    private int fullSurfaceTransformFluidItemId = int.MinValue;
    private bool fullSurfaceTransformResolved;
    private Vector3 fullSurfaceLocalPosition;
    private Quaternion fullSurfaceLocalRotation;
    private Vector3 fullSurfaceLocalScale;
    private float fluidInputBudgetLiters;
    private float lastFluidInputBudgetTime = -1f;
    private readonly Queue<Vector2Int> connectedPipeSearchQueue = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> connectedPipeSearchVisited = new HashSet<Vector2Int>();
    private readonly List<InstallationObject> connectedPipeInstallationsScratch =
        new List<InstallationObject>(4);
    private InstallationObject cachedConnectedFluidSource;
    private InstallationPlacementController cachedPlacementController;
    private float nextConnectedFluidSourceSearchTime;

    public float ManagedUpdateTickIntervalSeconds => ConnectedFluidPullIntervalSeconds;
    public bool IsInstalledFluidSurfaceVisible => installedFluidVisual != null
                                                  && installedFluidVisual.IsSurfaceVisible;
    public float InstalledFluidFillRatio => ResolveContainedFluidItemId(ResolveBucketDefinition()) >= 0
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
            PlacementRuntimeChanged += HandleFluidTopologyChanged;
            PlacementRuntimeCleared += HandleFluidTopologyChanged;
            ResetFluidInputBudget(true);
            InvalidateConnectedFluidSource();
            MapObjectTickManager.RegisterUpdateTick(this);
            RefreshInstalledFluidVisual();
        }
    }

    protected override void OnDisable()
    {
        PlacementRuntimeChanged -= HandleFluidTopologyChanged;
        PlacementRuntimeCleared -= HandleFluidTopologyChanged;
        bucketConversionPending = false;
        InvalidateConnectedFluidSource();
        ResetFluidInputBudget(false);
        MapObjectTickManager.UnregisterUpdateTick(this);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        bucketConversionPending = false;
        MapObjectTickManager.UnregisterUpdateTick(this);
        installedBody = null;
        installedFluidVisual = null;
        cachedWaterItemId = int.MinValue;
        cachedOilItemId = int.MinValue;
        fullSurfaceTransformFluidItemId = int.MinValue;
        fullSurfaceTransformResolved = false;
        cachedPlacementController = null;
        InvalidateConnectedFluidSource();
        ResetFluidInputBudget(false);
        base.PrepareForPool();
    }

    public bool TryGetInstalledFullSurfaceTransform(
        int fluidItemId,
        out Vector3 localPosition,
        out Quaternion localRotation,
        out Vector3 localScale)
    {
        if (!fullSurfaceTransformResolved || fullSurfaceTransformFluidItemId != fluidItemId)
        {
            fullSurfaceTransformFluidItemId = fluidItemId;
            fullSurfaceTransformResolved = TryResolveFullSurfaceTransform(
                fluidItemId,
                out fullSurfaceLocalPosition,
                out fullSurfaceLocalRotation,
                out fullSurfaceLocalScale);
        }

        localPosition = fullSurfaceTransformResolved
            ? fullSurfaceLocalPosition
            : new Vector3(0f, 0.255f, 0f);
        localRotation = fullSurfaceTransformResolved
            ? fullSurfaceLocalRotation
            : Quaternion.identity;
        localScale = fullSurfaceTransformResolved
            ? fullSurfaceLocalScale
            : new Vector3(0.157573f, 1f, 0.157573f);
        return fullSurfaceTransformResolved;
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        if (!isActiveAndEnabled || !TryGetPlacementRuntime(out _, out _))
        {
            bucketConversionPending = false;
            MapObjectTickManager.UnregisterUpdateTick(this);
            return;
        }

        if (IsEmptyBucketDefinition(ResolveBucketDefinition()))
        {
            TryPullFluidFromConnectedPipeNetwork(deltaTime);
        }

        if (bucketConversionPending)
        {
            if (!TryCompleteBucketConversion())
            {
                return;
            }

            bucketConversionPending = false;
            MapObjectTickManager.UnregisterUpdateTick(this);
            return;
        }

        if (!IsEmptyBucketDefinition(ResolveBucketDefinition()))
        {
            MapObjectTickManager.UnregisterUpdateTick(this);
        }
    }

    public static bool IsBucketDefinition(ItemDefinition definition)
    {
        return definition != null && definition.mapObject is Bucket;
    }

    public bool CanPlayerInteract(Player player)
    {
        InstallationPlacementController placementController = ResolvePlacementController();
        return placementController != null
               && placementController.CanCollectInstalledBucketToHand(this, player);
    }

    public bool TryPlayerInteract(Player player)
    {
        InstallationPlacementController placementController = ResolvePlacementController();
        return placementController != null
               && placementController.TryCollectInstalledBucketToHand(this, player);
    }

    public int GetInteractionIconItemId(Player player)
    {
        return ResolveItemId();
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

    public static bool IsOilBucketDefinition(ItemDefinition definition)
    {
        return IsBucketDefinition(definition)
               && NameMatches(definition, OilBucketItemName);
    }

    public static bool IsFilledBucketDefinition(ItemDefinition definition)
    {
        return IsWaterBucketDefinition(definition)
               || IsOilBucketDefinition(definition);
    }

    public static bool ShouldPreserveFilledBucketOnConversion(
        ItemManager itemManager,
        int sourceItemId,
        int targetItemId)
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null || !gameManager.FreeBucket || itemManager == null)
        {
            return false;
        }

        ItemDefinition sourceDefinition = ItemDefinitionLookup.ResolveById(
            itemManager.ItemDefinitions,
            sourceItemId);
        ItemDefinition targetDefinition = ItemDefinitionLookup.ResolveById(
            itemManager.ItemDefinitions,
            targetItemId);
        return IsFilledBucketDefinition(sourceDefinition)
               && IsEmptyBucketDefinition(targetDefinition);
    }

    public static int ResolveContainedFluidItemId(ItemDefinition definition)
    {
        if (IsWaterBucketDefinition(definition))
        {
            return Pump.ResolveWaterItemId(null);
        }

        if (IsOilBucketDefinition(definition))
        {
            return ResolveOilItemId(null);
        }

        return -1;
    }

    public static float ResolveContainedFluidLiters(ItemDefinition definition)
    {
        return IsFilledBucketDefinition(definition)
            ? definition.BucketFillDurationSeconds * PipeFillReferenceLitersPerSecond
            : 0f;
    }

    public static bool TryResolveEmptyBucketDefinition(
        ItemManager itemManager,
        out ItemDefinition emptyBucketDefinition)
    {
        return TryResolveBucketDefinition(
            itemManager,
            EmptyBucketItemName,
            out emptyBucketDefinition);
    }

    public static bool TryResolveWaterBucketDefinition(
        ItemManager itemManager,
        out ItemDefinition waterBucketDefinition)
    {
        return TryResolveBucketDefinition(
            itemManager,
            WaterBucketItemName,
            out waterBucketDefinition);
    }

    public static bool TryResolveOilBucketDefinition(
        ItemManager itemManager,
        out ItemDefinition oilBucketDefinition)
    {
        return TryResolveBucketDefinition(
            itemManager,
            OilBucketItemName,
            out oilBucketDefinition);
    }

    private static bool TryResolveFilledBucketDefinition(
        ItemManager itemManager,
        int fluidItemId,
        out ItemDefinition filledBucketDefinition)
    {
        if (fluidItemId == Pump.ResolveWaterItemId(null))
        {
            return TryResolveWaterBucketDefinition(itemManager, out filledBucketDefinition);
        }

        if (fluidItemId == ResolveOilItemId(itemManager))
        {
            return TryResolveOilBucketDefinition(itemManager, out filledBucketDefinition);
        }

        filledBucketDefinition = null;
        return false;
    }

    private static bool TryResolveBucketDefinition(
        ItemManager itemManager,
        string expectedName,
        out ItemDefinition bucketDefinition)
    {
        bucketDefinition = null;
        if (itemManager == null || itemManager.ItemDefinitions == null)
        {
            return false;
        }

        for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
        {
            ItemDefinition definition = itemManager.ItemDefinitions[i];
            if (!IsBucketDefinition(definition) || !NameMatches(definition, expectedName))
            {
                continue;
            }

            bucketDefinition = definition;
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

    public Material ResolveFluidSurfaceMaterial(int fluidItemId)
    {
        return fluidItemId == ResolveOilItemId()
            ? portableOilSurfaceMaterial
            : portableWaterSurfaceMaterial;
    }

    public override bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        return IsSupportedFluidItemId(fluidItemId)
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
        if (elapsedSeconds < 0f)
        {
            fluidInputBudgetLiters = 0f;
            return 0f;
        }

        float maxLitersPerSecond = MaximumFluidInputLitersPerSecond;
        float maximumBudgetLiters =
            maxLitersPerSecond * FluidInputBudgetMaximumAccrualSeconds;
        if (elapsedSeconds > FluidInputBudgetIdleResetSeconds)
        {
            // Oil producers commonly emit one-liter batches at intervals instead of
            // sending tiny amounts every frame. Treat the first request after an idle
            // interval as one second of available flow so that the batch is not lost,
            // while still preventing an inactive Bucket from accumulating its full
            // capacity and filling instantly when reconnected.
            fluidInputBudgetLiters = maximumBudgetLiters;
            return Mathf.Min(requestedLiters, fluidInputBudgetLiters);
        }

        fluidInputBudgetLiters = Mathf.Min(
            fluidInputBudgetLiters + maxLitersPerSecond * elapsedSeconds,
            maximumBudgetLiters);
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

        RefreshInstalledFluidVisual();
        if (!IsEmptyBucketDefinition(ResolveBucketDefinition())
            || !IsSupportedFluidItemId(currentFluidItemId)
            || currentStoredLiters + FullEpsilonLiters < FluidStorageCapacityLiters
            || !TryGetPlacementRuntime(out _, out _))
        {
            return;
        }

        bucketConversionPending = true;
        MapObjectTickManager.RegisterUpdateTick(this);
    }

    private void RefreshInstalledFluidVisual()
    {
        ItemDefinition definition = ResolveBucketDefinition();
        int containedFluidItemId = ResolveContainedFluidItemId(definition);
        bool isFilledBucket = containedFluidItemId >= 0;
        int visibleFluidItemId = isFilledBucket ? containedFluidItemId : StoredFluidItemId;
        float currentFillRatio = isFilledBucket ? 1f : GetEmptyBucketFillRatio();
        bool containsFluid = visibleFluidItemId >= 0 && currentFillRatio > FullEpsilonLiters;
        if (!containsFluid && installedFluidVisual == null)
        {
            return;
        }

        if (installedBody == null && containsFluid)
        {
            installedBody = ResolveInstalledBody();
        }

        if (installedBody == null)
        {
            installedFluidVisual?.Refresh(this, -1, null, true);
            return;
        }

        if (installedFluidVisual == null && containsFluid)
        {
            installedFluidVisual = GetComponent<PortableBucketWaterVisual>();
            if (installedFluidVisual == null)
            {
                installedFluidVisual = gameObject.AddComponent<PortableBucketWaterVisual>();
            }
        }

        installedFluidVisual?.Refresh(
            this,
            containsFluid ? visibleFluidItemId : -1,
            installedBody,
            true,
            currentFillRatio,
            !isFilledBucket);
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

    private void HandleFluidTopologyChanged(InstallationObject changedInstallation)
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        InvalidateConnectedFluidSource();
        if (IsEmptyBucketDefinition(ResolveBucketDefinition()))
        {
            if (changedInstallation == this)
            {
                ResetFluidInputBudget(true);
            }

            MapObjectTickManager.RegisterUpdateTick(this);
        }
    }

    private void InvalidateConnectedFluidSource()
    {
        cachedConnectedFluidSource = null;
        nextConnectedFluidSourceSearchTime = 0f;
        connectedPipeSearchQueue.Clear();
        connectedPipeSearchVisited.Clear();
        connectedPipeInstallationsScratch.Clear();
    }

    private void TryPullFluidFromConnectedPipeNetwork(float deltaTime)
    {
        if (deltaTime <= 0f
            || AvailableFluidStorageLiters <= FullEpsilonLiters
            || !TryResolveConnectedFluidSource(out InstallationObject source, out int fluidItemId))
        {
            return;
        }

        float requestedLiters = Mathf.Min(
            MaximumFluidInputLitersPerSecond * deltaTime,
            AvailableFluidStorageLiters,
            source.StoredFluidLiters);
        float temperatureCelsius = source.GetStoredFluidTemperatureCelsius(fluidItemId);
        if (requestedLiters <= FullEpsilonLiters
            || !source.TryConsumeFluidLiters(fluidItemId, requestedLiters, out float consumedLiters)
            || consumedLiters <= FullEpsilonLiters)
        {
            return;
        }

        TryAddFluidLiters(
            fluidItemId,
            consumedLiters,
            temperatureCelsius,
            out float acceptedLiters);
        float rejectedLiters = consumedLiters - Mathf.Max(0f, acceptedLiters);
        if (rejectedLiters > FullEpsilonLiters)
        {
            source.TryAddFluidLiters(
                fluidItemId,
                rejectedLiters,
                temperatureCelsius,
                out _);
        }
    }

    private bool TryResolveConnectedFluidSource(
        out InstallationObject source,
        out int fluidItemId)
    {
        fluidItemId = StoredFluidItemId;
        if (CanUseConnectedFluidSource(cachedConnectedFluidSource, fluidItemId, out int cachedFluidItemId))
        {
            source = cachedConnectedFluidSource;
            fluidItemId = cachedFluidItemId;
            return true;
        }

        cachedConnectedFluidSource = null;
        source = null;
        if (Time.time < nextConnectedFluidSourceSearchTime
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return false;
        }

        nextConnectedFluidSourceSearchTime = Time.time + MissingFluidSourceRetrySeconds;
        connectedPipeSearchQueue.Clear();
        connectedPipeSearchVisited.Clear();

        for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
        {
            Vector2Int direction = FluidCardinalDirections[directionIndex];
            Vector2Int pipeCoordinate = anchorCoordinate + direction;
            if (TryGetConnectedPipeAtCoordinate(pipeCoordinate, -direction, out _)
                && connectedPipeSearchVisited.Add(pipeCoordinate))
            {
                connectedPipeSearchQueue.Enqueue(pipeCoordinate);
            }
        }

        int searchedNodeCount = 0;
        while (connectedPipeSearchQueue.Count > 0 && searchedNodeCount < MaximumPipeSearchNodes)
        {
            Vector2Int pipeCoordinate = connectedPipeSearchQueue.Dequeue();
            searchedNodeCount++;
            if (!TryGetPipeAtCoordinate(pipeCoordinate, out Pipe pipe))
            {
                continue;
            }

            Quaternion pipeRotation = pipe.transform.rotation;
            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = FluidCardinalDirections[directionIndex];
                if (!pipe.HasConnectionTowardsAt(pipeCoordinate, pipeRotation, direction))
                {
                    continue;
                }

                Vector2Int neighborCoordinate = pipeCoordinate + direction;
                if (TryGetConnectedPipeAtCoordinate(neighborCoordinate, -direction, out _))
                {
                    if (connectedPipeSearchVisited.Add(neighborCoordinate))
                    {
                        connectedPipeSearchQueue.Enqueue(neighborCoordinate);
                    }

                    continue;
                }

                if (TryGetFluidSourceAtCoordinate(
                        neighborCoordinate,
                        fluidItemId,
                        out source,
                        out int sourceFluidItemId))
                {
                    cachedConnectedFluidSource = source;
                    fluidItemId = sourceFluidItemId;
                    return true;
                }
            }

            if (pipe.TryGetRemoteConnectionCoordinate(pipeCoordinate, out Vector2Int remoteCoordinate)
                && connectedPipeSearchVisited.Add(remoteCoordinate))
            {
                connectedPipeSearchQueue.Enqueue(remoteCoordinate);
            }
        }

        return false;
    }

    private bool TryGetConnectedPipeAtCoordinate(
        Vector2Int coordinate,
        Vector2Int requiredConnectionDirection,
        out Pipe pipe)
    {
        if (!TryGetPipeAtCoordinate(coordinate, out pipe))
        {
            return false;
        }

        return pipe.HasConnectionTowardsAt(
            coordinate,
            pipe.transform.rotation,
            requiredConnectionDirection);
    }

    private bool TryGetPipeAtCoordinate(Vector2Int coordinate, out Pipe pipe)
    {
        pipe = null;
        connectedPipeInstallationsScratch.Clear();
        CollectActiveInstallationsAtRuntimeGridCoordinate(
            coordinate,
            connectedPipeInstallationsScratch);
        for (int i = 0; i < connectedPipeInstallationsScratch.Count; i++)
        {
            if (connectedPipeInstallationsScratch[i] is Pipe candidatePipe
                && candidatePipe.gameObject.activeInHierarchy)
            {
                pipe = candidatePipe;
                connectedPipeInstallationsScratch.Clear();
                return true;
            }
        }

        connectedPipeInstallationsScratch.Clear();
        return false;
    }

    private bool TryGetFluidSourceAtCoordinate(
        Vector2Int coordinate,
        int requiredFluidItemId,
        out InstallationObject source,
        out int fluidItemId)
    {
        source = null;
        fluidItemId = -1;
        connectedPipeInstallationsScratch.Clear();
        CollectActiveInstallationsAtRuntimeGridCoordinate(
            coordinate,
            connectedPipeInstallationsScratch);
        for (int i = 0; i < connectedPipeInstallationsScratch.Count; i++)
        {
            InstallationObject candidate = connectedPipeInstallationsScratch[i];
            if (!CanUseConnectedFluidSource(
                    candidate,
                    requiredFluidItemId,
                    out int candidateFluidItemId))
            {
                continue;
            }

            source = candidate;
            fluidItemId = candidateFluidItemId;
            connectedPipeInstallationsScratch.Clear();
            return true;
        }

        connectedPipeInstallationsScratch.Clear();
        return false;
    }

    private bool CanUseConnectedFluidSource(
        InstallationObject source,
        int requiredFluidItemId,
        out int fluidItemId)
    {
        fluidItemId = requiredFluidItemId >= 0
            ? requiredFluidItemId
            : source != null ? source.StoredFluidItemId : -1;
        return source != null
               && source != this
               && !(source is Pipe)
               && source.gameObject.activeInHierarchy
               && IsSupportedFluidItemId(fluidItemId)
               && source.CanProvideFluidItem(fluidItemId, FullEpsilonLiters);
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

    private int ResolveOilItemId()
    {
        if (cachedOilItemId >= 0)
        {
            return cachedOilItemId;
        }

        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        int resolvedItemId = ResolveOilItemId(itemManager);
        if (itemManager != null)
        {
            cachedOilItemId = resolvedItemId;
        }

        return resolvedItemId;
    }

    private static int ResolveOilItemId(ItemManager itemManager)
    {
        if (itemManager != null && itemManager.ItemDefinitions != null)
        {
            for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
            {
                ItemDefinition definition = itemManager.ItemDefinitions[i];
                if (definition != null && NameMatches(definition, OilItemName))
                {
                    return definition.id;
                }
            }
        }

        return DefaultOilItemId;
    }

    private bool IsSupportedFluidItemId(int fluidItemId)
    {
        return fluidItemId >= 0
               && (fluidItemId == ResolveWaterItemId()
                   || fluidItemId == ResolveOilItemId());
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

    private bool TryResolveFullSurfaceTransform(
        int fluidItemId,
        out Vector3 localPosition,
        out Quaternion localRotation,
        out Vector3 localScale)
    {
        localPosition = default;
        localRotation = Quaternion.identity;
        localScale = Vector3.one;
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (!TryResolveFilledBucketDefinition(itemManager, fluidItemId, out ItemDefinition bucketDefinition)
            || !(bucketDefinition.mapObject is Bucket bucketPrefab))
        {
            return false;
        }

        Transform[] prefabTransforms = bucketPrefab.GetComponentsInChildren<Transform>(true);
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

    private bool TryCompleteBucketConversion()
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        int fluidItemId = StoredFluidItemId;
        if (!TryResolveFilledBucketDefinition(itemManager, fluidItemId, out ItemDefinition filledBucketDefinition))
        {
            return false;
        }

        InstallationPlacementController placementController = ResolvePlacementController();
        return placementController != null
               && placementController.TryUpgradeInstalledObject(
                   this,
                   filledBucketDefinition,
                   out _);
    }

    private InstallationPlacementController ResolvePlacementController()
    {
        if (cachedPlacementController == null)
        {
            cachedPlacementController = FindFirstObjectByType<InstallationPlacementController>();
        }

        return cachedPlacementController;
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
