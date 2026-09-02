using System.Collections.Generic;
using UnityEngine;

public class SeedPlanter : InputOutputModule
{
    public enum OperatingState
    {
        Ready,
        Planting,
        NoSeeds,
        NoPower,
        InvalidGround,
        TargetOccupied
    }

    private const float DefaultPlantDurationSeconds = 2f;
    private const float ProgressEpsilon = 0.0001f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color ReadyLightColor = new Color(0.18f, 1f, 0.25f, 1f);
    private static readonly Color WarningLightColor = new Color(1f, 0.72f, 0.05f, 1f);
    private static readonly Color ErrorLightColor = new Color(1f, 0.08f, 0.03f, 1f);

    [SerializeField] private Sprite outputAreaMarkerIcon;
    [SerializeField] private Renderer warningLightRenderer;
    [SerializeField, Min(0.1f)] private float workAnimationCycleSeconds = 2.5f;
    [SerializeField, HideInInspector, Min(0f)] private float plantElapsedSeconds;

    private MaterialPropertyBlock warningLightPropertyBlock;
    private OperatingState operatingState = OperatingState.Ready;
    private OperatingState appliedLightState = (OperatingState)(-1);
    private int currentSeedItemId = -1;
    private int currentSeedCount;
    private Vector2Int currentInputCoordinate;
    private bool hasCurrentInputCoordinate;
    private bool requestingPower;
    private bool isOperating;

    public override float ManagedUpdateTickIntervalSeconds => 0.1f;
    public Sprite OutputAreaMarkerIcon => outputAreaMarkerIcon;
    public OperatingState CurrentOperatingState => operatingState;
    public bool IsErrorState => operatingState == OperatingState.InvalidGround;
    public bool IsOperating => isOperating;
    public int CurrentSeedItemId => currentSeedItemId;
    public int CurrentSeedCount => Mathf.Max(0, currentSeedCount);
    public float PlantDurationSeconds => ResolvePlantDuration(ResolveInstalledDefinition());
    public float PlantElapsedSeconds => Mathf.Clamp(plantElapsedSeconds, 0f, PlantDurationSeconds);
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

    public override void ManagedUpdateTick(float deltaTime)
    {
        requestingPower = false;
        isOperating = false;
        RefreshSeedInput();

        if (!Application.isPlaying || deltaTime <= 0f || !TryGetPlacementRuntime(out _, out _))
        {
            SetOperatingState(OperatingState.Ready);
            base.ManagedUpdateTick(deltaTime);
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (!TryResolveOutputTarget(out Vector2Int targetCoordinate)
            || terrain == null
            || !terrain.IsFarmlandAt(targetCoordinate))
        {
            plantElapsedSeconds = 0f;
            SetOperatingState(OperatingState.InvalidGround);
            base.ManagedUpdateTick(deltaTime);
            return;
        }

        if (currentSeedItemId < 0 || currentSeedCount <= 0 || !hasCurrentInputCoordinate)
        {
            plantElapsedSeconds = 0f;
            SetOperatingState(OperatingState.NoSeeds);
            base.ManagedUpdateTick(deltaTime);
            return;
        }

        ItemDefinition seedDefinition = ResolveItemDefinition(currentSeedItemId);
        if (!terrain.CanPlantSeedAt(targetCoordinate, seedDefinition))
        {
            plantElapsedSeconds = 0f;
            SetOperatingState(OperatingState.TargetOccupied);
            base.ManagedUpdateTick(deltaTime);
            return;
        }

        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        requestingPower = true;
        if (!HasOperationalEnergyAvailable(installedDefinition))
        {
            SetOperatingState(OperatingState.NoPower);
            base.ManagedUpdateTick(deltaTime);
            return;
        }

        float remainingDuration = Mathf.Max(0f, PlantDurationSeconds - plantElapsedSeconds);
        float requestedOperationSeconds = Mathf.Min(deltaTime, remainingDuration);
        if (requestedOperationSeconds <= ProgressEpsilon
            || !TryConsumeOperatingEnergy(requestedOperationSeconds, out _))
        {
            SetOperatingState(OperatingState.NoPower);
            base.ManagedUpdateTick(deltaTime);
            return;
        }

        isOperating = true;
        SetOperatingState(OperatingState.Planting);
        plantElapsedSeconds = Mathf.Min(
            PlantDurationSeconds,
            plantElapsedSeconds + requestedOperationSeconds * OperationalAnimationSpeedRatio);

        if (plantElapsedSeconds + ProgressEpsilon >= PlantDurationSeconds)
        {
            int seedItemId = currentSeedItemId;
            Vector2Int inputCoordinate = currentInputCoordinate;
            Vector3 consumeTargetWorldPosition = ResolveConsumeTargetWorldPosition();
            int consumed = ConsumeRuntimeInputAreaCenterObjects(
                inputCoordinate,
                seedItemId,
                1,
                consumeTargetWorldPosition,
                InputConsumeMoveInterval,
                true);
            plantElapsedSeconds = 0f;
            isOperating = false;
            requestingPower = false;
            if (consumed == 1)
            {
                currentSeedCount = Mathf.Max(0, currentSeedCount - 1);
                if (!terrain.TryPlantSeedAt(targetCoordinate, seedDefinition))
                {
                    if (TryRestoreRuntimeInputAreaCenterObject(
                            inputCoordinate,
                            seedItemId,
                            consumeTargetWorldPosition))
                    {
                        currentSeedCount++;
                    }
                    else
                    {
                        Debug.LogError($"{nameof(SeedPlanter)} failed to restore seed item {seedItemId} after planting failed.", this);
                    }
                }

                SetOperatingState(OperatingState.TargetOccupied);
            }
            else
            {
                RefreshSeedInput();
                SetOperatingState(currentSeedCount > 0
                    ? OperatingState.Ready
                    : OperatingState.NoSeeds);
            }
        }

        base.ManagedUpdateTick(deltaTime);
    }

    public override PersistentState CapturePersistentState()
    {
        PersistentState state = base.CapturePersistentState();
        state.seedPlanterPlantElapsedSeconds = Mathf.Clamp(plantElapsedSeconds, 0f, PlantDurationSeconds);
        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        base.ApplyPersistentState(state);
        plantElapsedSeconds = state != null
            ? Mathf.Clamp(state.seedPlanterPlantElapsedSeconds, 0f, PlantDurationSeconds)
            : 0f;
        RefreshSeedInput();
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
        plantElapsedSeconds = 0f;
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
                    out Vector2Int coordinate))
            {
                continue;
            }

            currentSeedItemId = definition.id;
            currentInputCoordinate = coordinate;
            hasCurrentInputCoordinate = true;
            currentSeedCount = GetRuntimeInputAreaCenterItemCount(coordinate, definition.id);
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
