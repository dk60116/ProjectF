using System.Collections.Generic;
using UnityEngine;

public class SeedPlanter : InputOutputModule
{
    public enum OperatingState
    {
        Ready,
        LoadingSeed,
        Planting,
        NoSeeds,
        NoPower,
        InvalidGround,
        TargetOccupied
    }

    private const float DefaultPlantDurationSeconds = 2f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color ReadyLightColor = new Color(0.18f, 1f, 0.25f, 1f);
    private static readonly Color WarningLightColor = new Color(1f, 0.72f, 0.05f, 1f);
    private static readonly Color ErrorLightColor = new Color(1f, 0.08f, 0.03f, 1f);

    [SerializeField] private Sprite outputAreaMarkerIcon;
    [SerializeField] private Renderer warningLightRenderer;
    [SerializeField, Min(0.1f)] private float workAnimationCycleSeconds = 2.5f;
    [SerializeField, HideInInspector] private long plantElapsedUnits;
    [SerializeField, HideInInspector] private bool hasLoadedSeed;
    [SerializeField, HideInInspector] private int loadedSeedItemId = -1;
    [SerializeField, HideInInspector] private Vector2Int loadedSeedInputCoordinate;
    [SerializeField, HideInInspector] private long seedTransferRemainingUnits;

    private MaterialPropertyBlock warningLightPropertyBlock;
    private OperatingState operatingState = OperatingState.Ready;
    private OperatingState appliedLightState = (OperatingState)(-1);
    private int currentSeedItemId = -1;
    private int currentSeedCount;
    private Vector2Int currentInputCoordinate;
    private bool hasCurrentInputCoordinate;
    private bool requestingPower;
    private bool isOperating;
    private readonly List<Vector2Int> recoveredSeedInputCoordinates = new List<Vector2Int>(2);

    public override float ManagedUpdateTickIntervalSeconds => 0.1f;
    public Sprite OutputAreaMarkerIcon => outputAreaMarkerIcon;
    public OperatingState CurrentOperatingState => operatingState;
    public bool IsErrorState => operatingState == OperatingState.InvalidGround;
    public bool IsOperating => isOperating;
    public int CurrentSeedItemId => currentSeedItemId;
    public int CurrentSeedCount => Mathf.Max(0, currentSeedCount);
    public float PlantDurationSeconds => ResolvePlantDuration(ResolveInstalledDefinition());
    public float PlantElapsedSeconds => DeterministicSimulationUnits.ToFloat(
        System.Math.Min(
            DeterministicSimulationUnits.FromFloat(PlantDurationSeconds),
            System.Math.Max(0L, plantElapsedUnits)));
    public float PlantProgress01 => Mathf.Clamp01(PlantElapsedSeconds / PlantDurationSeconds);

    protected override void OnEnable()
    {
        base.OnEnable();
        ResolveWarningLightRenderer();
        RefreshWarningLight(true);
    }

    protected override void OnDisable()
    {
        requestingPower = false;
        isOperating = false;
        SetWorkAnimatorState(false, true);
        base.OnDisable();
    }

    public override void ApplyManagedUpdateTick()
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime))
        {
            return;
        }

        requestingPower = false;
        isOperating = false;
        if (hasLoadedSeed)
        {
            ApplyLoadedSeedAsCurrentInput();
        }
        else
        {
            RefreshSeedInput();
        }

        if (!Application.isPlaying || deltaTime <= 0f || !TryGetPlacementRuntime(out _, out _))
        {
            SetOperatingState(OperatingState.Ready);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (!TryResolveOutputTarget(out Vector2Int targetCoordinate)
            || terrain == null
            || !terrain.IsFarmlandAt(targetCoordinate))
        {
            if (!hasLoadedSeed)
            {
                plantElapsedUnits = 0L;
            }

            SetOperatingState(OperatingState.InvalidGround);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        if (!hasLoadedSeed
            && (currentSeedItemId < 0 || currentSeedCount <= 0 || !hasCurrentInputCoordinate))
        {
            plantElapsedUnits = 0L;
            SetOperatingState(OperatingState.NoSeeds);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        ItemDefinition seedDefinition = ResolveItemDefinition(currentSeedItemId);
        if (!ItemDefinition.IsPlantableSeedDefinition(seedDefinition))
        {
            plantElapsedUnits = 0L;
            SetOperatingState(OperatingState.NoSeeds);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        if (!terrain.CanPlantSeedAt(targetCoordinate, seedDefinition))
        {
            plantElapsedUnits = 0L;
            SetOperatingState(OperatingState.TargetOccupied);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        if (hasLoadedSeed && seedTransferRemainingUnits > 0L)
        {
            long transferDeltaUnits = DeterministicSimulationUnits.RateForTicks(
                1f,
                DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime));
            seedTransferRemainingUnits = System.Math.Max(
                0L,
                seedTransferRemainingUnits - transferDeltaUnits);
            if (seedTransferRemainingUnits > 0L)
            {
                SetOperatingState(OperatingState.LoadingSeed);
                ApplyPlannedBaseModuleTick(deltaTime);
                return;
            }
        }

        long plantDurationUnits = DeterministicSimulationUnits.FromFloat(PlantDurationSeconds);
        // A restored completed operation needs no additional energy. It must still
        // own a loaded seed before committing the planting result.
        if (hasLoadedSeed && plantElapsedUnits >= plantDurationUnits)
        {
            CompletePlanting(terrain, targetCoordinate, seedDefinition);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        requestingPower = true;
        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            SetOperatingState(OperatingState.NoPower);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        if (!hasLoadedSeed)
        {
            Vector3 planterWorldPosition = ResolveConsumeTargetWorldPosition();
            int consumed = ConsumeRuntimeInputAreaCenterObjects(
                currentInputCoordinate,
                currentSeedItemId,
                1,
                planterWorldPosition,
                InputConsumeMoveInterval,
                animateVirtualizedConsumption: true,
                respectBoxMinimumRetainedCount: false);
            if (consumed != 1)
            {
                plantElapsedUnits = 0L;
                RefreshSeedInput();
                SetOperatingState(currentSeedCount > 0
                    ? OperatingState.Ready
                    : OperatingState.NoSeeds);
                ApplyPlannedBaseModuleTick(deltaTime);
                return;
            }

            hasLoadedSeed = true;
            loadedSeedItemId = currentSeedItemId;
            loadedSeedInputCoordinate = currentInputCoordinate;
            seedTransferRemainingUnits = DeterministicSimulationUnits.FromFloat(
                PortableObject.MoveToDuration);
            ApplyLoadedSeedAsCurrentInput();
            requestingPower = false;
            SetOperatingState(OperatingState.LoadingSeed);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        // Apply supply to a full simulation step, then clamp the accumulated work.
        // Scaling the remaining work instead approaches completion asymptotically
        // under partial power and can strand a seed at the final integer units.
        long requestedOperationUnits = DeterministicSimulationUnits.RateForTicks(
            1f,
            DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime));
        if (!TryConsumeOperatingEnergy(deltaTime, out _))
        {
            SetOperatingState(OperatingState.NoPower);
            ApplyPlannedBaseModuleTick(deltaTime);
            return;
        }

        isOperating = true;
        SetOperatingState(OperatingState.Planting);
        long speedRatioUnits = DeterministicSimulationUnits.FromFloat(OperationalAnimationSpeedRatio);
        plantElapsedUnits = System.Math.Min(
            plantDurationUnits,
            plantElapsedUnits + DeterministicSimulationUnits.MultiplyRatio(
                requestedOperationUnits,
                speedRatioUnits,
                DeterministicSimulationUnits.UnitsPerWhole));

        if (plantElapsedUnits >= plantDurationUnits)
        {
            CompletePlanting(terrain, targetCoordinate, seedDefinition);
        }

        ApplyPlannedBaseModuleTick(deltaTime);
    }

    private void CompletePlanting(
        TerrainGenerator terrain,
        Vector2Int targetCoordinate,
        ItemDefinition seedDefinition)
    {
        int seedItemId = loadedSeedItemId;
        Vector2Int inputCoordinate = loadedSeedInputCoordinate;
        Vector3 planterWorldPosition = ResolveConsumeTargetWorldPosition();
        plantElapsedUnits = 0L;
        isOperating = false;
        requestingPower = false;
        if (terrain.TryPlantSeedAt(targetCoordinate, seedDefinition))
        {
            PlaySeedDropAnimation(terrain, targetCoordinate, seedItemId, planterWorldPosition);
            ClearLoadedSeed();
            SetOperatingState(OperatingState.TargetOccupied);
        }
        else
        {
            if (TryRestoreRuntimeInputAreaCenterObject(
                    inputCoordinate,
                    seedItemId,
                    planterWorldPosition))
            {
                ClearLoadedSeed();
                RefreshSeedInput();
            }
            else
            {
                Debug.LogError($"{nameof(SeedPlanter)} failed to restore loaded seed item {seedItemId} after planting failed.", this);
            }

            SetOperatingState(OperatingState.TargetOccupied);
        }
    }

    internal int ReceiveHarvestedSeeds(Vector2Int harvestedCoordinate, int seedItemId,
        int count, Vector3 startWorldPosition)
    {
        if (count <= 0 || !isActiveAndEnabled || !TryGetPlacementRuntime(out _, out _)
            || !TryResolveOutputTarget(out Vector2Int plantingCoordinate)
            || plantingCoordinate != harvestedCoordinate
            || !ItemDefinition.IsPlantableSeedDefinition(ResolveItemDefinition(seedItemId)))
            return 0;

        recoveredSeedInputCoordinates.Clear();
        AppendRuntimeInputItemAreaCoordinates(seedItemId, recoveredSeedInputCoordinates);
        int accepted = 0;
        for (int i = 0; i < recoveredSeedInputCoordinates.Count && accepted < count; i++)
        {
            Vector2Int coordinate = recoveredSeedInputCoordinates[i];
            if (!CanAddItemToRuntimeIoOverlapCoordinate(coordinate, seedItemId))
                continue;
            // Shares the existing loaded/saved input-stack path and its capacity/type checks.
            while (accepted < count
                && TryRestoreRuntimeInputAreaCenterObject(coordinate, seedItemId, startWorldPosition))
                accepted++;
        }
        if (accepted > 0)
        {
            RefreshSeedInput();
            WakeRuntimeUpdate();
        }
        return accepted;
    }

    public override PersistentState CapturePersistentState()
    {
        PersistentState state = base.CapturePersistentState();
        state.seedPlanterPlantElapsedSeconds = PlantElapsedSeconds;
        state.seedPlanterPlantElapsedUnits = plantElapsedUnits;
        state.seedPlanterHasLoadedSeed = hasLoadedSeed;
        state.seedPlanterLoadedSeedItemId = loadedSeedItemId;
        state.seedPlanterLoadedSeedInputCoordinate = loadedSeedInputCoordinate;
        state.seedPlanterTransferRemainingUnits = seedTransferRemainingUnits;
        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        base.ApplyPersistentState(state);
        plantElapsedUnits = state != null
            ? System.Math.Min(
                DeterministicSimulationUnits.FromFloat(PlantDurationSeconds),
                state.hasDeterministicUnits
                    ? System.Math.Max(0L, state.seedPlanterPlantElapsedUnits)
                    : DeterministicSimulationUnits.FromFloat(state.seedPlanterPlantElapsedSeconds))
            : 0L;
        hasLoadedSeed = state != null
                        && state.seedPlanterHasLoadedSeed
                        && state.seedPlanterLoadedSeedItemId >= 0;
        loadedSeedItemId = hasLoadedSeed ? state.seedPlanterLoadedSeedItemId : -1;
        loadedSeedInputCoordinate = hasLoadedSeed
            ? state.seedPlanterLoadedSeedInputCoordinate
            : default;
        seedTransferRemainingUnits = hasLoadedSeed
            ? System.Math.Min(
                DeterministicSimulationUnits.FromFloat(PortableObject.MoveToDuration),
                System.Math.Max(0L, state.seedPlanterTransferRemainingUnits))
            : 0L;
        if (hasLoadedSeed)
        {
            ApplyLoadedSeedAsCurrentInput();
        }
        else
        {
            RefreshSeedInput();
        }

        requestingPower = false;
        isOperating = false;
        SetOperatingState(OperatingState.Ready);
        WakeRuntimeUpdate();
    }

    public override bool TryGetElectricPowerDemand(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        return requestingPower && TryGetElectricPowerRequirement(out wattsPerSecond);
    }

    public bool TryCollectPlantableSeedItemIds(ICollection<int> itemIds)
    {
        if (itemIds == null || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return false;
        }

        bool foundAny = false;
        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (!ItemDefinition.IsPlantableSeedDefinition(definition) || itemIds.Contains(definition.id))
            {
                continue;
            }

            itemIds.Add(definition.id);
            foundAny = true;
        }

        return foundAny;
    }

    public void GetObjectInfoStatus(out string statusText, out bool isPlanting, out bool isWarning)
    {
        statusText = ResolveObjectInfoStatus(out isPlanting);
        isWarning = operatingState == OperatingState.NoSeeds
                    || operatingState == OperatingState.NoPower
                    || operatingState == OperatingState.InvalidGround
                    || operatingState == OperatingState.TargetOccupied;
    }

    public static float ResolvePlantDuration(ItemDefinition definition)
    {
        return definition != null && definition.seedPlanterPlantDurationSeconds > 0f
            ? Mathf.Max(0.1f, definition.seedPlanterPlantDurationSeconds)
            : DefaultPlantDurationSeconds;
    }

    protected override bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> itemIds)
    {
        return TryCollectPlantableSeedItemIds(itemIds);
    }

    protected override bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(
        Vector2Int coordinate,
        ISet<int> inputItemIds)
    {
        if (inputItemIds == null)
        {
            return false;
        }

        bool foundAny = false;
        List<ItemDefinition> definitions = GameManager.Instance != null && GameManager.Instance.ItemManger != null
            ? GameManager.Instance.ItemManger.ItemDefinitions
            : null;
        if (definitions == null)
        {
            return false;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (!ItemDefinition.IsPlantableSeedDefinition(definition)
                || !ContainsRuntimeInputItemArea(coordinate, definition.id))
            {
                continue;
            }

            inputItemIds.Add(definition.id);
            foundAny = true;
        }

        return foundAny;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        return TryGetPlacementRuntime(out _, out _) || base.ShouldKeepRuntimeUpdateTickActive();
    }

    protected override bool ShouldPlayWorkAnimation()
    {
        return isOperating;
    }

    protected override float ResolveWorkAnimationSpeedMultiplier()
    {
        return Mathf.Max(0.1f, workAnimationCycleSeconds) / PlantDurationSeconds;
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = operatingState == OperatingState.Planting;
        switch (operatingState)
        {
            case OperatingState.LoadingSeed:
                return "Loading seed";
            case OperatingState.Planting:
                return "Planting";
            case OperatingState.NoSeeds:
                return "No seeds";
            case OperatingState.NoPower:
                return "No power";
            case OperatingState.InvalidGround:
                return "Invalid ground";
            case OperatingState.TargetOccupied:
                return "Target occupied";
            default:
                return "Ready";
        }
    }

    protected override void OnPlacementRuntimeCleared()
    {
        base.OnPlacementRuntimeCleared();
        plantElapsedUnits = 0L;
        ClearLoadedSeed();
        currentSeedItemId = -1;
        currentSeedCount = 0;
        hasCurrentInputCoordinate = false;
        requestingPower = false;
        isOperating = false;
        SetOperatingState(OperatingState.Ready);
    }

    private bool TryResolveOutputTarget(out Vector2Int coordinate)
    {
        coordinate = default;
        if (!HasRuntimeOutputCoordinates || RuntimeOutputCoordinates.Count <= 0)
        {
            return false;
        }

        coordinate = RuntimeOutputCoordinates[0];
        return true;
    }

    private void ApplyLoadedSeedAsCurrentInput()
    {
        currentSeedItemId = loadedSeedItemId;
        currentSeedCount = hasLoadedSeed ? 1 : 0;
        currentInputCoordinate = loadedSeedInputCoordinate;
        hasCurrentInputCoordinate = hasLoadedSeed;
    }

    private void ClearLoadedSeed()
    {
        hasLoadedSeed = false;
        loadedSeedItemId = -1;
        loadedSeedInputCoordinate = default;
        seedTransferRemainingUnits = 0L;
        currentSeedItemId = -1;
        currentSeedCount = 0;
        hasCurrentInputCoordinate = false;
    }

    private static void PlaySeedDropAnimation(
        TerrainGenerator terrain,
        Vector2Int targetCoordinate,
        int seedItemId,
        Vector3 planterWorldPosition)
    {
        if (terrain == null
            || seedItemId < 0
            || !terrain.TryGetLoadedBlock(targetCoordinate, out Block targetBlock)
            || targetBlock == null)
        {
            return;
        }

        targetBlock.PlayTransientItemToFloorAnimation(
            seedItemId,
            planterWorldPosition);
    }

    private void RefreshSeedInput()
    {
        currentSeedItemId = -1;
        currentSeedCount = 0;
        hasCurrentInputCoordinate = false;
        if (GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (!ItemDefinition.IsPlantableSeedDefinition(definition)
                || !TryResolveRuntimeInputItemBlock(
                    definition.id,
                    1,
                    null,
                    out _,
                    out Vector2Int coordinate,
                    respectBoxMinimumRetainedCount: false))
            {
                continue;
            }

            currentSeedItemId = definition.id;
            currentInputCoordinate = coordinate;
            hasCurrentInputCoordinate = true;
            currentSeedCount = GetRuntimeInputAreaCenterItemCount(
                coordinate,
                definition.id,
                respectBoxMinimumRetainedCount: false);
            return;
        }
    }

    private void SetOperatingState(OperatingState state)
    {
        operatingState = state;
        RefreshWarningLight(false);
    }

    private void ResolveWarningLightRenderer()
    {
        if (warningLightRenderer != null)
        {
            return;
        }

        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate != null && candidate.name == "Line")
            {
                warningLightRenderer = candidate.GetComponent<Renderer>();
                return;
            }
        }
    }

    private void RefreshWarningLight(bool force)
    {
        if (!force && appliedLightState == operatingState)
        {
            return;
        }

        ResolveWarningLightRenderer();
        if (warningLightRenderer == null)
        {
            return;
        }

        Color color = IsErrorState
            ? ErrorLightColor
            : operatingState == OperatingState.NoPower
              || operatingState == OperatingState.NoSeeds
              || operatingState == OperatingState.TargetOccupied
                ? WarningLightColor
                : ReadyLightColor;
        warningLightPropertyBlock ??= new MaterialPropertyBlock();
        warningLightRenderer.GetPropertyBlock(warningLightPropertyBlock);
        warningLightPropertyBlock.SetColor(BaseColorId, color);
        warningLightPropertyBlock.SetColor(ColorId, color);
        warningLightPropertyBlock.SetColor(EmissionColorId, color * 1.5f);
        warningLightRenderer.SetPropertyBlock(warningLightPropertyBlock);
        appliedLightState = operatingState;
    }
}
