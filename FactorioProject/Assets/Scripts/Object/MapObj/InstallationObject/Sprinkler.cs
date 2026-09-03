using System.Collections.Generic;
using ProjectF.MapObjects;
using UnityEngine;
using ProjectTree = ProjectF.MapObjects.Tree;

public class Sprinkler : InputOutputModule
{
    private const float WaterEpsilon = 0.0001f;
    private const float RangeVisualYOffset = 0.045f;
    private const float DefaultSprayIntervalSeconds = 2f;
    private const float DefaultWaterLitersPerCell = 0.25f;
    private const float DefaultNozzleRotationDegreesPerSecond = 180f;
    private const float WaterJetNozzleTipOffset = 0.005f;
    private const float FallbackNozzleHalfLength = 0.525f;
    private const int MaxWaterItemTransfersPerTick = 8;
    private static readonly Vector2Int[] AdjacentPlantOffsets =
    {
        new Vector2Int(-1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(1, 1),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1)
    };
    private static readonly Color RangeFillColor = new Color(0.05f, 0.45f, 1f, 0.14f);
    private static readonly Color WaterParticleColor = new Color(0.25f, 0.72f, 1f, 0.85f);
    private static readonly HashSet<Sprinkler> ActiveSprinklers = new HashSet<Sprinkler>();
    private static readonly HashSet<Sprinkler> SelectedRangeVisualSprinklers = new HashSet<Sprinkler>();
    private static readonly HashSet<Sprinkler> AppendedRangeVisualSprinklers = new HashSet<Sprinkler>();
    private static readonly List<WorkableObjectRangeVisualRequest> RangeVisualRequests =
        new List<WorkableObjectRangeVisualRequest>();

    private static WorkableObjectRangeVisual sharedRangeVisual;
    private static bool installOrEditRangeVisualsRequested;

    [SerializeField]
    private Transform nozzleTransform;
    [SerializeField]
    private Material waterJetMaterial;
    [SerializeField, HideInInspector, Min(0f)]
    private float sprayElapsedSeconds;

    private readonly List<Vector2Int> sprayCoordinates = new List<Vector2Int>(64);
    private readonly List<ParticleSystem> waterJetEffects = new List<ParticleSystem>(2);
    private readonly HashSet<ProjectTree> wateringTargetScratch = new HashSet<ProjectTree>();
    private Vector2 cachedRangeCenter;
    private Vector3 previewRangeCenter;
    private int cachedRangeRadius = -1;
    private bool focusedRangeVisualRequested;
    private bool hasPreviewRangeCenter;
    private bool inRangeVisualRequested;
    private bool isOperating;
    private bool selectedRangeVisualRequested;
    private int currentWateringTargetCount;

    public override float ManagedUpdateTickIntervalSeconds => 0.25f;
    public bool IsOperating => isOperating;
    public int CurrentWateringTargetCount => Mathf.Max(0, currentWateringTargetCount);
    public int RangeRadiusCells
    {
        get
        {
            ItemDefinition definition = ResolveSprinklerDefinition();
            return definition != null ? Mathf.Max(0, definition.sprinklerRangeRadius) : 0;
        }
    }

    public float WaterLitersPerCell
    {
        get
        {
            ItemDefinition definition = ResolveSprinklerDefinition();
            return definition != null
                ? Mathf.Max(0.001f, definition.sprinklerWaterLitersPerCell)
                : DefaultWaterLitersPerCell;
        }
    }

    public float SprayIntervalSeconds
    {
        get
        {
            ItemDefinition definition = ResolveSprinklerDefinition();
            return definition != null
                ? Mathf.Max(0.1f, definition.sprinklerSprayIntervalSeconds)
                : DefaultSprayIntervalSeconds;
        }
    }

    public float WaterLitersPerSpray
    {
        get
        {
            EnsureSprayCoordinates();
            return sprayCoordinates.Count * WaterLitersPerCell;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveSprinklers.Add(this);
        if (IsDirectRangeVisualRequested)
        {
            SelectedRangeVisualSprinklers.Add(this);
        }

        ResolveNozzleTransform();
        if (Application.isPlaying)
        {
            EnsureWaterJetEffects();
        }

        InvalidateSprayCoordinates();
        RefreshAllRangeVisuals();
    }

    protected override void OnDisable()
    {
        SetOperating(false);
        ActiveSprinklers.Remove(this);
        if (IsDirectRangeVisualRequested && !gameObject.activeInHierarchy)
        {
            SelectedRangeVisualSprinklers.Remove(this);
            selectedRangeVisualRequested = false;
            focusedRangeVisualRequested = false;
            inRangeVisualRequested = false;
        }

        RefreshAllRangeVisuals();
        base.OnDisable();
    }

    private void Update()
    {
        if (!Application.isPlaying || !isOperating || nozzleTransform == null)
        {
            return;
        }

        ItemDefinition definition = ResolveSprinklerDefinition();
        float rotationSpeed = definition != null
            ? Mathf.Max(0f, definition.sprinklerNozzleRotationDegreesPerSecond)
            : DefaultNozzleRotationDegreesPerSecond;
        if (rotationSpeed > 0f)
        {
            nozzleTransform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        }
    }

    public override void ManagedUpdateTick(float deltaTime)
    {
        int waterItemId = ResolveWaterItemId();
        if (StoredFluidItemId >= 0 && StoredFluidItemId != waterItemId)
        {
            SetStoredFluid(-1, 0f);
        }

        if (Application.isPlaying && deltaTime > 0f)
        {
            PullWaterItemsIntoStorage(waterItemId);
        }

        base.ManagedUpdateTick(deltaTime);
        if (!Application.isPlaying || deltaTime <= 0f || !TryGetPlacementRuntime(out _, out _))
        {
            currentWateringTargetCount = 0;
            SetOperating(false);
            return;
        }

        EnsureSprayCoordinates();
        currentWateringTargetCount = CountWateringTargets();
        float waterRequired = WaterLitersPerSpray;
        bool canOperate = currentWateringTargetCount > 0
                          && waterItemId >= 0
                          && waterRequired > WaterEpsilon
                          && StoredFluidItemId == waterItemId
                          && StoredFluidLiters + WaterEpsilon >= waterRequired;
        SetOperating(canOperate);
        if (!canOperate)
        {
            return;
        }

        float interval = SprayIntervalSeconds;
        sprayElapsedSeconds += deltaTime;
        if (sprayElapsedSeconds + WaterEpsilon < interval)
        {
            return;
        }

        sprayElapsedSeconds = Mathf.Max(0f, sprayElapsedSeconds - interval);
        PerformSpray(waterItemId, waterRequired);
    }

    public override bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        int waterItemId = ResolveWaterItemId();
        return (fluidItemId < 0 || fluidItemId == waterItemId)
               && base.CanAcceptFluidItem(fluidItemId, requestedLiters);
    }

    public override float GetStoredFluidTemperatureCelsius(int fluidItemId)
    {
        return fluidItemId >= 0 && fluidItemId == ResolveWaterItemId()
            ? MapClimate.CurrentWaterTemperatureCelsius
            : base.GetStoredFluidTemperatureCelsius(fluidItemId);
    }

    public override PersistentState CapturePersistentState()
    {
        PersistentState state = base.CapturePersistentState();
        state.sprinklerSprayElapsedSeconds = Mathf.Clamp(
            sprayElapsedSeconds,
            0f,
            SprayIntervalSeconds);
        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        base.ApplyPersistentState(state);
        sprayElapsedSeconds = state != null
            ? Mathf.Clamp(state.sprinklerSprayElapsedSeconds, 0f, SprayIntervalSeconds)
            : 0f;
        InvalidateSprayCoordinates();
        WakeRuntimeUpdate();
    }

    protected override int ResolvePreferredFluidInputItemId()
    {
        return ResolveWaterItemId();
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        return TryGetPlacementRuntime(out _, out _)
               || base.ShouldKeepRuntimeUpdateTickActive();
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        InvalidateSprayCoordinates();
        RefreshAllRangeVisuals();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        base.OnPlacementRuntimeCleared();
        InvalidateSprayCoordinates();
        currentWateringTargetCount = 0;
        SetOperating(false);
        RefreshAllRangeVisuals();
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = isOperating;
        if (isOperating)
        {
            return "Watering";
        }

        if (currentWateringTargetCount <= 0)
        {
            return "No plants need water";
        }

        if (StoredFluidItemId != ResolveWaterItemId() || StoredFluidLiters <= WaterEpsilon)
        {
            return "No water";
        }

        return StoredFluidLiters + WaterEpsilon < WaterLitersPerSpray
            ? "Not enough water"
            : "Ready";
    }

    public void GetObjectInfoStatus(
        out string statusText,
        out bool isWatering,
        out bool isWarning)
    {
        statusText = ResolveObjectInfoStatus(out isWatering);
        isWarning = !isWatering
                    && currentWateringTargetCount <= 0
                    && TryGetPlacementRuntime(out _, out _);
    }

    public bool TryGetSprayRangeBounds(out Bounds bounds)
    {
        Vector3 center = ResolveRangeWorldCenter();
        float radius = RangeRadiusCells + 0.5f;
        if (radius <= 0f)
        {
            bounds = default;
            return false;
        }

        float size = radius * 2f;
        bounds = new Bounds(center, new Vector3(size, 0.01f, size));
        return true;
    }

    public bool ContainsWorldPositionInSprayRange(Vector3 worldPosition)
    {
        if (!TryGetSprayRangeBounds(out Bounds rangeBounds))
        {
            return false;
        }

        return worldPosition.x >= rangeBounds.min.x
               && worldPosition.x <= rangeBounds.max.x
               && worldPosition.z >= rangeBounds.min.z
               && worldPosition.z <= rangeBounds.max.z;
    }

    public static void CollectActiveSprinklersContainingWorldPosition(
        Vector3 worldPosition,
        HashSet<Sprinkler> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (Sprinkler sprinkler in ActiveSprinklers)
        {
            if (sprinkler == null
                || !sprinkler.gameObject.activeInHierarchy
                || !sprinkler.AllowsFocus
                || !sprinkler.ContainsWorldPositionInSprayRange(worldPosition))
            {
                continue;
            }

            results.Add(sprinkler);
        }
    }

    public void SetSelectedRangeVisualRequested(bool requested)
    {
        if (selectedRangeVisualRequested == requested)
        {
            return;
        }

        selectedRangeVisualRequested = requested;
        RefreshDirectRangeVisualRegistration();
    }

    public void SetFocusedRangeVisualRequested(bool requested)
    {
        if (focusedRangeVisualRequested == requested)
        {
            return;
        }

        focusedRangeVisualRequested = requested;
        RefreshDirectRangeVisualRegistration();
    }

    public void SetInRangeVisualRequested(bool requested)
    {
        if (inRangeVisualRequested == requested)
        {
            return;
        }

        inRangeVisualRequested = requested;
        RefreshDirectRangeVisualRegistration();
    }

    public void SetPreviewRangeCenter(Vector3 center)
    {
        bool changed = !hasPreviewRangeCenter
                       || (previewRangeCenter - center).sqrMagnitude > WaterEpsilon * WaterEpsilon;
        hasPreviewRangeCenter = true;
        previewRangeCenter = center;
        if (!changed)
        {
            return;
        }

        InvalidateSprayCoordinates();
        RefreshAllRangeVisuals();
    }

    public void ClearPreviewRangeCenter()
    {
        if (!hasPreviewRangeCenter)
        {
            return;
        }

        hasPreviewRangeCenter = false;
        InvalidateSprayCoordinates();
        RefreshAllRangeVisuals();
    }

    public static void SetInstallOrEditRangeVisualsRequested(bool requested)
    {
        if (installOrEditRangeVisualsRequested == requested)
        {
            return;
        }

        installOrEditRangeVisualsRequested = requested;
        RefreshAllRangeVisuals();
    }

    public static void RefreshAllRangeVisuals()
    {
        if (!Application.isPlaying)
        {
            SetSharedRangeVisualActive(false);
            return;
        }

        RangeVisualRequests.Clear();
        AppendedRangeVisualSprinklers.Clear();
        if (ShouldShowInstallOrEditRangeVisuals())
        {
            AppendRangeVisualRequests(ActiveSprinklers);
        }

        AppendRangeVisualRequests(SelectedRangeVisualSprinklers);

        if (RangeVisualRequests.Count <= 0)
        {
            SetSharedRangeVisualActive(false);
            return;
        }

        WorkableObjectRangeVisual visual = GetOrCreateSharedRangeVisual();
        visual.Configure(RangeVisualRequests, RangeFillColor);
        SetSharedRangeVisualActive(true);
    }

    private static void AppendRangeVisualRequests(IEnumerable<Sprinkler> sprinklers)
    {
        if (sprinklers == null)
        {
            return;
        }

        foreach (Sprinkler sprinkler in sprinklers)
        {
            if (sprinkler == null
                || !AppendedRangeVisualSprinklers.Add(sprinkler)
                || !sprinkler.gameObject.activeInHierarchy
                || !sprinkler.TryGetSprayRangeBounds(out Bounds rangeBounds))
            {
                continue;
            }

            RangeVisualRequests.Add(new WorkableObjectRangeVisualRequest(
                rangeBounds.center,
                rangeBounds.extents.x,
                RangeVisualYOffset));
        }
    }

    private bool IsDirectRangeVisualRequested => selectedRangeVisualRequested
                                                 || focusedRangeVisualRequested
                                                 || inRangeVisualRequested;

    private void RefreshDirectRangeVisualRegistration()
    {
        if (IsDirectRangeVisualRequested)
        {
            SelectedRangeVisualSprinklers.Add(this);
        }
        else
        {
            SelectedRangeVisualSprinklers.Remove(this);
        }

        RefreshAllRangeVisuals();
    }

    private static bool ShouldShowInstallOrEditRangeVisuals()
    {
        if (!installOrEditRangeVisualsRequested)
        {
            return false;
        }

        GameManager gameManager = GameManager.Instance;
        return gameManager != null
               && (gameManager.InstallationPlacementActive || gameManager.MapEditActive);
    }

    private static WorkableObjectRangeVisual GetOrCreateSharedRangeVisual()
    {
        if (sharedRangeVisual != null)
        {
            return sharedRangeVisual;
        }

        GameObject visualObject = new GameObject("Sprinkler Range Visuals");
        sharedRangeVisual = visualObject.AddComponent<WorkableObjectRangeVisual>();
        return sharedRangeVisual;
    }

    private static void SetSharedRangeVisualActive(bool active)
    {
        if (sharedRangeVisual != null && sharedRangeVisual.gameObject.activeSelf != active)
        {
            sharedRangeVisual.gameObject.SetActive(active);
        }
    }

    private void PerformSpray(int waterItemId, float waterRequired)
    {
        float temperature = GetStoredFluidTemperatureCelsius(waterItemId);
        if (!TryConsumeFluidLiters(waterItemId, waterRequired, out float consumedLiters)
            || consumedLiters + WaterEpsilon < waterRequired)
        {
            if (consumedLiters > WaterEpsilon)
            {
                TryAddFluidLiters(waterItemId, consumedLiters, temperature, out _);
            }

            SetOperating(false);
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return;
        }

        float waterPerCell = WaterLitersPerCell;
        for (int i = 0; i < sprayCoordinates.Count; i++)
        {
            if (!terrain.TryGetLoadedBlock(sprayCoordinates[i], out Block block)
                || block == null)
            {
                continue;
            }

            if (TryGetWateringTarget(block, out ProjectTree tree))
            {
                tree.TryAddGrowthWater(waterPerCell, out _);
                continue;
            }

            if (IsEmptyGround(block))
            {
                WaterAdjacentPlants(terrain, sprayCoordinates[i], waterPerCell);
            }
        }
    }

    private static void WaterAdjacentPlants(
        TerrainGenerator terrain,
        Vector2Int wateredGroundCoordinate,
        float availableWaterLiters)
    {
        float remainingWaterLiters = availableWaterLiters;
        for (int i = 0; i < AdjacentPlantOffsets.Length && remainingWaterLiters > WaterEpsilon; i++)
        {
            Vector2Int plantCoordinate = wateredGroundCoordinate + AdjacentPlantOffsets[i];
            if (!terrain.TryGetLoadedBlock(plantCoordinate, out Block adjacentBlock)
                || !TryGetWateringTarget(adjacentBlock, out ProjectTree tree)
                || !tree.TryAddGrowthWater(remainingWaterLiters, out float acceptedLiters))
            {
                continue;
            }

            remainingWaterLiters = Mathf.Max(0f, remainingWaterLiters - acceptedLiters);
        }
    }

    private void PullWaterItemsIntoStorage(int waterItemId)
    {
        if (waterItemId < 0 || AvailableFluidStorageLiters + WaterEpsilon < 1f)
        {
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return;
        }

        int maxTransfers = Mathf.Min(
            MaxWaterItemTransfersPerTick,
            Mathf.FloorToInt(AvailableFluidStorageLiters + WaterEpsilon));
        for (int i = 0; i < maxTransfers; i++)
        {
            if (!TryGetRuntimeInputBlock(terrain, waterItemId, out Block inputBlock)
                || inputBlock == null
                || !inputBlock.TryConsumeOneInputAreaCenterObject(waterItemId, out int consumedItemId)
                || consumedItemId != waterItemId)
            {
                return;
            }

            if (TryAddFluidLiters(
                    waterItemId,
                    1f,
                    MapClimate.CurrentWaterTemperatureCelsius,
                    out float acceptedLiters)
                && acceptedLiters + WaterEpsilon >= 1f)
            {
                continue;
            }

            inputBlock.TryAddInputAreaCenterObjectAnimated(
                consumedItemId,
                inputBlock.WorldPosition,
                0f,
                out _);
            return;
        }
    }

    private int CountWateringTargets()
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return 0;
        }

        wateringTargetScratch.Clear();
        for (int i = 0; i < sprayCoordinates.Count; i++)
        {
            Vector2Int sprayCoordinate = sprayCoordinates[i];
            if (!terrain.TryGetLoadedBlock(sprayCoordinate, out Block block)
                || block == null)
            {
                continue;
            }

            if (TryGetWateringTarget(block, out ProjectTree directTree))
            {
                wateringTargetScratch.Add(directTree);
                continue;
            }

            if (!IsEmptyGround(block))
            {
                continue;
            }

            for (int neighborIndex = 0; neighborIndex < AdjacentPlantOffsets.Length; neighborIndex++)
            {
                Vector2Int neighborCoordinate = sprayCoordinate + AdjacentPlantOffsets[neighborIndex];
                if (terrain.TryGetLoadedBlock(neighborCoordinate, out Block adjacentBlock)
                    && TryGetWateringTarget(adjacentBlock, out ProjectTree adjacentTree))
                {
                    wateringTargetScratch.Add(adjacentTree);
                }
            }
        }

        int targetCount = wateringTargetScratch.Count;
        wateringTargetScratch.Clear();
        return targetCount;
    }

    private static bool TryGetWateringTarget(Block block, out ProjectTree tree)
    {
        tree = block != null ? block.Resource as ProjectTree : null;
        return tree != null && tree.CanAcceptGrowthWater;
    }

    private static bool IsEmptyGround(Block block)
    {
        return block != null && block.MapObject == null && block.Resource == null;
    }

    private void EnsureSprayCoordinates()
    {
        Vector3 worldCenter = ResolveRangeWorldCenter();
        Vector2 rangeCenter = new Vector2(worldCenter.x, worldCenter.z);
        int radiusCells = RangeRadiusCells;
        if (cachedRangeRadius == radiusCells
            && (cachedRangeCenter - rangeCenter).sqrMagnitude <= WaterEpsilon * WaterEpsilon)
        {
            return;
        }

        cachedRangeCenter = rangeCenter;
        cachedRangeRadius = radiusCells;
        sprayCoordinates.Clear();

        float worldRadius = radiusCells + 0.5f;
        float radiusSqr = worldRadius * worldRadius;
        int minX = Mathf.FloorToInt(rangeCenter.x - worldRadius);
        int maxX = Mathf.CeilToInt(rangeCenter.x + worldRadius);
        int minY = Mathf.FloorToInt(rangeCenter.y - worldRadius);
        int maxY = Mathf.CeilToInt(rangeCenter.y + worldRadius);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 offset = new Vector2(x - rangeCenter.x, y - rangeCenter.y);
                if (offset.sqrMagnitude <= radiusSqr + WaterEpsilon)
                {
                    sprayCoordinates.Add(new Vector2Int(x, y));
                }
            }
        }
    }

    private void InvalidateSprayCoordinates()
    {
        cachedRangeRadius = -1;
        sprayCoordinates.Clear();
    }

    private Vector3 ResolveRangeWorldCenter()
    {
        if (TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return new Vector3(anchorCoordinate.x, transform.position.y, anchorCoordinate.y);
        }

        if (hasPreviewRangeCenter)
        {
            return previewRangeCenter;
        }

        return transform.position;
    }

    private ItemDefinition ResolveSprinklerDefinition()
    {
        return BoundItemDefinition != null ? BoundItemDefinition : ResolveInstalledDefinition();
    }

    private static int ResolveWaterItemId()
    {
        return Pump.ResolveWaterItemId(null);
    }

    private void SetOperating(bool operating)
    {
        if (isOperating == operating)
        {
            return;
        }

        isOperating = operating;
        EnsureWaterJetEffects();
        for (int i = 0; i < waterJetEffects.Count; i++)
        {
            ParticleSystem effect = waterJetEffects[i];
            if (effect == null)
            {
                continue;
            }

            if (operating)
            {
                effect.Play(true);
            }
            else
            {
                effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private void ResolveNozzleTransform()
    {
        if (nozzleTransform != null && nozzleTransform.IsChildOf(transform))
        {
            return;
        }

        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            if (descendants[i] != null && descendants[i].name == "Nozzle")
            {
                nozzleTransform = descendants[i];
                return;
            }
        }
    }

    private void EnsureWaterJetEffects()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ResolveNozzleTransform();
        if (nozzleTransform == null || waterJetEffects.Count > 0)
        {
            return;
        }

        ResolveWaterJetPose(true, out Vector3 firstPosition, out float firstYaw);
        ResolveWaterJetPose(false, out Vector3 secondPosition, out float secondYaw);
        waterJetEffects.Add(CreateWaterJet("Water Jet A", firstPosition, firstYaw));
        waterJetEffects.Add(CreateWaterJet("Water Jet B", secondPosition, secondYaw));
    }

    private void ResolveWaterJetPose(bool positiveSide, out Vector3 localPosition, out float localYaw)
    {
        MeshFilter nozzleMeshFilter = nozzleTransform.GetComponent<MeshFilter>();
        Mesh nozzleMesh = nozzleMeshFilter != null ? nozzleMeshFilter.sharedMesh : null;
        if (nozzleMesh == null)
        {
            float x = positiveSide ? FallbackNozzleHalfLength : -FallbackNozzleHalfLength;
            localPosition = new Vector3(x, 0f, 0f);
            localYaw = positiveSide ? 180f : 0f;
            return;
        }

        Bounds bounds = nozzleMesh.bounds;
        if (bounds.size.x >= bounds.size.z)
        {
            float x = positiveSide
                ? bounds.max.x + WaterJetNozzleTipOffset
                : bounds.min.x - WaterJetNozzleTipOffset;
            localPosition = new Vector3(x, bounds.center.y, bounds.center.z);
            localYaw = positiveSide ? 180f : 0f;
            return;
        }

        float z = positiveSide
            ? bounds.max.z + WaterJetNozzleTipOffset
            : bounds.min.z - WaterJetNozzleTipOffset;
        localPosition = new Vector3(bounds.center.x, bounds.center.y, z);
        localYaw = positiveSide ? 90f : -90f;
    }

    private ParticleSystem CreateWaterJet(string effectName, Vector3 localPosition, float localYaw)
    {
        Transform existingTransform = nozzleTransform.Find(effectName);
        GameObject effectObject = existingTransform != null
            ? existingTransform.gameObject
            : new GameObject(effectName);
        effectObject.layer = gameObject.layer;
        Transform effectTransform = effectObject.transform;
        effectTransform.SetParent(nozzleTransform, false);
        effectTransform.localPosition = localPosition;
        effectTransform.localRotation = Quaternion.Euler(0f, localYaw, 0f);

        ParticleSystem effect = effectObject.GetComponent<ParticleSystem>();
        if (effect == null)
        {
            effect = effectObject.AddComponent<ParticleSystem>();
        }
        ParticleSystem.MainModule main = effect.main;
        main.loop = true;
        main.playOnAwake = false;
        // Emitted water must not rotate together with the spinning nozzle.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 0.5f;
        main.startSpeed = 2.8f;
        main.startSize = 0.035f;
        main.startColor = WaterParticleColor;
        main.gravityModifier = 0.45f;
        main.maxParticles = 64;

        ParticleSystem.EmissionModule emission = effect.emission;
        emission.enabled = true;
        emission.rateOverTime = 28f;

        ParticleSystem.ShapeModule shape = effect.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 2.5f;
        shape.radius = 0.015f;

        ParticleSystemRenderer effectRenderer = effect.GetComponent<ParticleSystemRenderer>();
        if (effectRenderer != null)
        {
            effectRenderer.sharedMaterial = waterJetMaterial;
            effectRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            effectRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            effectRenderer.receiveShadows = false;
            effectRenderer.sortingFudge = 1f;
        }

        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return effect;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        sprayElapsedSeconds = Mathf.Max(0f, sprayElapsedSeconds);
        ResolveNozzleTransform();
        InvalidateSprayCoordinates();
    }
#endif
}
