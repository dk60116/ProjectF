using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Resource : MapObject
{
    private struct HarvestReward
    {
        public int itemId;
        public int amount;
    }

    private const string ExtraBodyRendererRootName = "_ResourceBodyExtraRenderers";
    private const int BodyYawStepCount = 8;
    private const float BodyYawStepDegrees = 45f;

    public enum HarvestMode
    {
        Auto,
        Mining,
        Logging,
        Cut,
        Cultivating
    }

    [Serializable]
    public struct ResourceStatus
    {
        [HideInInspector]
        public int outputId;
        [Tooltip("Harvest output ItemDefinition name. Leave blank to use the ResourceDefinition resource name.")]
        public string outputItemName;
        public int resourceCount;
        public int getCount;
        public int maxGauge;
        public int currentGague;
    }

    [Serializable]
    public struct ResourceSaveState
    {
        public int resourceCount;
        public int maxGauge;
        public int currentGauge;
        public int initialResourceCount;
        public bool hasBodyYawStep;
        public int bodyYawStep;
        public bool hasGrowth;
        public float growth;
        public bool hasPlantGrowthState;
        public float growthWaterLiters;
        public float growthFertilizerAmount;
        public float growthElapsedSeconds;
    }

    private static readonly List<Resource> ActiveResourcesInternal = new List<Resource>();
    private static readonly Dictionary<Vector2Int, List<Resource>> ActiveResourcesByCoordinate =
        new Dictionary<Vector2Int, List<Resource>>();

    [SerializeField]
    private HarvestMode harvestMode = HarvestMode.Auto;

    [SerializeField]
    private ResourceDefinition definition;

    [SerializeField]
    private ResourceStatus resourceStatus;

    [SerializeField]
    private List<PortableObject> portableObjects = new List<PortableObject>();

    [SerializeField]
    private float workPerGaugeDot = 1f;

    [SerializeField, Min(0f)]
    private float portableMoveInterval = 0.1f;

    [SerializeField]
    private Vector3 focusOffset = new Vector3(0f, 0.5f, 0f);

    [SerializeField, Range(0f, 1f)]
    private float minimumBodyScaleRatio = 0.5f;

    [SerializeField, Min(0.01f)]
    private float maximumBodyScaleRatio = 1f;

    [SerializeField, Min(1)]
    private int dynamicScaleMaxResourceCount = 1000;

    private float accumulatedWork;
    private readonly Queue<int> reservedHarvestGaugeCosts = new Queue<int>();
    private readonly List<HarvestReward> harvestRewardBuffer = new List<HarvestReward>(8);
    private int reservedHarvestGaugeCount;
    private Renderer cachedRenderer;
    private Transform bodyTransform;
    private Vector3 initialBodyLocalScale = Vector3.one;
    private Quaternion initialBodyLocalRotation = Quaternion.identity;
    private bool hasBodyYawStep;
    private int bodyYawStep;
    private int initialResourceCount;
    private bool useDynamicBodyScale = true;
    private Block owningBlock;
    private bool activeResourceCoordinateRegistered;
    private Vector2Int activeResourceCoordinate;
    private ResourceBatchRenderer batchRenderer;
    private readonly List<BatchRenderEntry> batchedRenderEntries = new List<BatchRenderEntry>();
    private bool batchComponentsResolved;
    private bool supportsBatchedRendering;
    private bool useBatchedRendering;
    private bool bodyPresentationVisible = true;

    public static IReadOnlyList<Resource> ActiveResources => ActiveResourcesInternal;

    public int CurrentGauge => Mathf.Clamp(resourceStatus.currentGague, 0, MaxGauge);
    public int MaxGauge => Mathf.Max(1, resourceStatus.maxGauge);
    public int ResourceCount => Mathf.Max(0, resourceStatus.resourceCount);
    public int GetCount => Mathf.Max(1, resourceStatus.getCount);
    public int RemainingHarvestOutputCount => Mathf.Max(
        0,
        ResourceCount * GetHarvestOutputCountPerResource());
    public int RemainingMachineHarvestOutputCount => Mathf.Max(
        0,
        ResourceCount * GetCount);
    public bool CanHarvest => ResourceCount > 0 && HasHarvestableOutputAtCurrentState();
    public ResourceDefinition Definition => definition;
    public ResourceDefinition.PlacementCategory PlacementCategory => definition != null
        ? definition.placementCategory
        : ResourceDefinition.PlacementCategory.Ore;
    public override bool AllowsAnimalTraversal
    {
        get
        {
            HarvestMode resolvedMode = ResolvedHarvestMode;
            return resolvedMode == HarvestMode.Mining
                   || resolvedMode == HarvestMode.Cut;
        }
    }

    public HarvestMode ResolvedHarvestMode
    {
        get
        {
            if (harvestMode != HarvestMode.Auto)
            {
                return harvestMode;
            }

            string sourceName = $"{objectName} {gameObject.name}";
            if (sourceName.IndexOf("reed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return HarvestMode.Cut;
            }

            return sourceName.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0
                ? HarvestMode.Logging
                : HarvestMode.Mining;
        }
    }

    public Vector3 FocusPoint
    {
        get
        {
            if (cachedRenderer == null)
            {
                cachedRenderer = GetComponentInChildren<Renderer>();
            }

            if (cachedRenderer != null)
            {
                return cachedRenderer.bounds.center + focusOffset;
            }

            return transform.position + focusOffset;
        }
    }

    public Block OwningBlock => owningBlock;

    protected new void Awake()
    {
        base.Awake();

        cachedRenderer = GetComponentInChildren<Renderer>();
        CacheBodyTransform();
        CachePortableObjects();
        ApplyDefinitionIfNeeded();
        EnsureStatusInitialized();
        MigrateOutputItemNameIfNeeded();
        CaptureInitialStateIfNeeded();
        EnsurePortableObjectPool(GetCount);
        UpdateBodyScale();
    }

    protected void OnEnable()
    {
        RefreshItemLight();
        CacheBodyTransform();
        CachePortableObjects();
        ApplyDefinitionIfNeeded();
        EnsureStatusInitialized();
        MigrateOutputItemNameIfNeeded();
        CaptureInitialStateIfNeeded();
        EnsurePortableObjectPool(GetCount);
        ShowBodyPresentation();

        if (!Application.isPlaying)
        {
            ApplyEditorBodyScale();
            return;
        }

        UpdateBodyScale();
        SetBatchedRendering(true);

        if (!ActiveResourcesInternal.Contains(this))
        {
            ActiveResourcesInternal.Add(this);
        }

        RegisterActiveResourceCoordinate();
    }

    protected void OnDisable()
    {
        SetBatchedRendering(false);
        UnregisterActiveResourceCoordinate();
        ActiveResourcesInternal.Remove(this);

        if (!Application.isPlaying || owningBlock == null || owningBlock.MapObject != this)
        {
            return;
        }

        owningBlock.SetMapObject(null);
    }

    protected void OnValidate()
    {
        CacheBodyTransform();
        ApplyDefinitionIfNeeded();
        EnsureStatusInitialized();
        MigrateOutputItemNameIfNeeded();
        batchComponentsResolved = false;
        supportsBatchedRendering = false;
        batchedRenderEntries.Clear();
        if (!Application.isPlaying)
        {
            initialResourceCount = Mathf.Max(1, resourceStatus.resourceCount);
            ApplyEditorBodyScale();
            return;
        }

        maximumBodyScaleRatio = Mathf.Max(minimumBodyScaleRatio, maximumBodyScaleRatio);
        dynamicScaleMaxResourceCount = Mathf.Max(1, dynamicScaleMaxResourceCount);
        CaptureInitialStateIfNeeded();
        UpdateBodyScale();
    }

    public int PrepareHarvestSteps(float workAmount, int harvestPower = 1)
    {
        if (!CanHarvest || workAmount <= 0f)
        {
            return 0;
        }

        accumulatedWork += workAmount;
        float stepThreshold = Mathf.Max(0.01f, workPerGaugeDot);
        int normalizedHarvestPower = Mathf.Max(1, harvestPower);
        int preparedStepCount = 0;

        while (accumulatedWork >= stepThreshold)
        {
            int reservableGaugeCount = GetReservableHarvestGaugeCount(normalizedHarvestPower);
            if (reservableGaugeCount <= 0)
            {
                break;
            }

            accumulatedWork -= stepThreshold;
            reservedHarvestGaugeCosts.Enqueue(reservableGaugeCount);
            reservedHarvestGaugeCount += reservableGaugeCount;
            preparedStepCount++;
        }

        return preparedStepCount;
    }

    public bool PrepareManualHarvestStep(int harvestPower = 1)
    {
        if (!CanHarvest)
        {
            return false;
        }

        int reservableGaugeCount = GetReservableHarvestGaugeCount(Mathf.Max(1, harvestPower));
        if (reservableGaugeCount <= 0)
        {
            return false;
        }

        accumulatedWork = 0f;
        reservedHarvestGaugeCosts.Enqueue(reservableGaugeCount);
        reservedHarvestGaugeCount += reservableGaugeCount;
        return true;
    }

    public bool CommitPreparedHarvestStep()
    {
        if (reservedHarvestGaugeCosts.Count <= 0 || !CanHarvest)
        {
            return false;
        }

        int reservedGaugeCost = reservedHarvestGaugeCosts.Dequeue();
        reservedHarvestGaugeCount = Mathf.Max(0, reservedHarvestGaugeCount - reservedGaugeCost);
        ConsumeGaugeDots(reservedGaugeCost);

        if (!CanHarvest)
        {
            ClearReservedHarvestSteps();
            accumulatedWork = 0f;
        }

        return true;
    }

    public bool CancelPreparedHarvestStep()
    {
        if (reservedHarvestGaugeCosts.Count <= 0)
        {
            return false;
        }

        int reservedGaugeCost = reservedHarvestGaugeCosts.Dequeue();
        reservedHarvestGaugeCount = Mathf.Max(0, reservedHarvestGaugeCount - reservedGaugeCost);
        accumulatedWork += Mathf.Max(0.01f, workPerGaugeDot);
        return true;
    }

    public void ResetWork()
    {
        accumulatedWork = 0f;
        ClearReservedHarvestSteps();
    }

    public ResourceSaveState CaptureState()
    {
        ResourceSaveState state = new ResourceSaveState
        {
            resourceCount = ResourceCount,
            maxGauge = MaxGauge,
            currentGauge = CurrentGauge,
            initialResourceCount = Mathf.Max(1, initialResourceCount),
            hasBodyYawStep = hasBodyYawStep,
            bodyYawStep = NormalizeBodyYawStep(bodyYawStep)
        };
        CaptureAdditionalSaveState(ref state);
        return state;
    }

    public void ApplySavedState(ResourceSaveState state)
    {
        resourceStatus.resourceCount = Mathf.Max(0, state.resourceCount);
        resourceStatus.maxGauge = Mathf.Max(1, state.maxGauge);

        if (resourceStatus.resourceCount <= 0)
        {
            resourceStatus.currentGague = 0;
        }
        else
        {
            resourceStatus.currentGague = Mathf.Clamp(state.currentGauge, 1, resourceStatus.maxGauge);
        }

        accumulatedWork = 0f;
        ClearReservedHarvestSteps();
        initialResourceCount = Mathf.Max(resourceStatus.resourceCount, state.initialResourceCount);
        if (state.hasBodyYawStep)
        {
            ApplyBodyYawStep(state.bodyYawStep);
        }

        ApplyAdditionalSavedState(state);
        EnsurePortableObjectPool(GetCount);
        ShowBodyPresentation();
        UpdateBodyScale();
    }

    public void InitializeRuntimeQuantity(int resourceCount)
    {
        resourceStatus.resourceCount = Mathf.Max(0, resourceCount);
        resourceStatus.maxGauge = Mathf.Max(1, resourceStatus.maxGauge);
        resourceStatus.currentGague = resourceStatus.resourceCount > 0 ? resourceStatus.maxGauge : 0;
        accumulatedWork = 0f;
        ClearReservedHarvestSteps();
        initialResourceCount = Mathf.Max(1, resourceStatus.resourceCount);
        EnsurePortableObjectPool(GetCount);
        ShowBodyPresentation();
        UpdateBodyScale();
    }

    public void ConfigureDynamicBodyScale(float minimumScaleRatio, float maximumScaleRatio, int maxResourceCountForScale)
    {
        useDynamicBodyScale = true;
        minimumBodyScaleRatio = Mathf.Clamp01(minimumScaleRatio);
        maximumBodyScaleRatio = Mathf.Max(minimumBodyScaleRatio, maximumScaleRatio);
        dynamicScaleMaxResourceCount = Mathf.Max(1, maxResourceCountForScale);
        UpdateBodyScale();
    }

    public void ConfigureFixedBodyScale()
    {
        useDynamicBodyScale = false;
        UpdateBodyScale();
    }

    protected float MinimumBodyScaleRatio => Mathf.Clamp01(minimumBodyScaleRatio);
    protected float MaximumBodyScaleRatio => Mathf.Max(MinimumBodyScaleRatio, maximumBodyScaleRatio);

    protected void RefreshBodyScale()
    {
        UpdateBodyScale();
    }

    protected virtual float GetAdditionalBodyScaleRatio()
    {
        return 1f;
    }

    protected virtual void CaptureAdditionalSaveState(ref ResourceSaveState state)
    {
    }

    protected virtual void ApplyAdditionalSavedState(ResourceSaveState state)
    {
    }

    public void ApplyBodyYawStep(int yawStep)
    {
        CacheBodyTransform();

        bodyYawStep = NormalizeBodyYawStep(yawStep);
        hasBodyYawStep = true;

        if (bodyTransform == null)
        {
            return;
        }

        bodyTransform.localRotation = initialBodyLocalRotation * Quaternion.Euler(0f, bodyYawStep * BodyYawStepDegrees, 0f);
        SyncExtraBodyRendererRootToBody();
        MarkBatchRenderDataDirty();
    }

    public bool TryPeekMachineHarvestOutput(out int outputItemId, out int outputCount)
    {
        return TryPeekDefaultHarvestOutput(out outputItemId, out outputCount);
    }

    public bool TryPeekHarvestOutput(out int outputItemId, out int outputCount)
    {
        if (HasConfiguredDropItems())
        {
            return TryPeekConfiguredHarvestOutput(out outputItemId, out outputCount);
        }

        return TryPeekDefaultHarvestOutput(out outputItemId, out outputCount);
    }

    private bool TryPeekDefaultHarvestOutput(out int outputItemId, out int outputCount)
    {
        outputItemId = ResolveOutputItemId();
        outputCount = GetCount;
        return CanHarvest && outputItemId >= 0 && outputCount > 0;
    }

    public bool TryHarvestForMachine(out int outputItemId, out int outputCount)
    {
        if (!TryPeekMachineHarvestOutput(out outputItemId, out outputCount))
        {
            outputItemId = -1;
            outputCount = 0;
            return false;
        }

        int depletedResourceCount = ConsumeGaugeDotsInternal(CurrentGauge, out bool resourceFullyDepleted);
        if (depletedResourceCount <= 0)
        {
            outputItemId = -1;
            outputCount = 0;
            return false;
        }

        if (resourceFullyDepleted)
        {
            HideBodyPresentation();
            gameObject.SetActive(false);
        }

        outputCount *= depletedResourceCount;
        return outputCount > 0;
    }

    private void ConsumeGaugeDots(int gaugeAmount)
    {
        int depletedResourceCount = ConsumeGaugeDotsInternal(gaugeAmount, out bool resourceFullyDepleted);
        if (depletedResourceCount <= 0)
        {
            return;
        }

        if (HasConfiguredDropItems())
        {
            PlayConfiguredHarvestDrops(depletedResourceCount, resourceFullyDepleted);
            return;
        }

        int outputItemId = ResolveOutputItemId();
        for (int i = 0; i < depletedResourceCount; i++)
        {
            bool hideAfterSequence = resourceFullyDepleted && i == depletedResourceCount - 1;
            PlayPickupSequence(0, outputItemId, hideAfterSequence);
        }
    }

    private int ConsumeGaugeDotsInternal(int gaugeAmount, out bool resourceFullyDepleted)
    {
        resourceFullyDepleted = false;
        if (!CanHarvest || gaugeAmount <= 0)
        {
            return 0;
        }

        int remainingGaugeAmount = gaugeAmount;
        int depletedResourceCount = 0;

        while (remainingGaugeAmount > 0 && CanHarvest)
        {
            int gaugeToConsume = Mathf.Min(CurrentGauge, remainingGaugeAmount);
            resourceStatus.currentGague = Mathf.Max(0, resourceStatus.currentGague - gaugeToConsume);
            remainingGaugeAmount -= gaugeToConsume;

            if (resourceStatus.currentGague > 0)
            {
                continue;
            }

            resourceStatus.resourceCount = Mathf.Max(0, resourceStatus.resourceCount - 1);
            depletedResourceCount++;
            accumulatedWork = 0f;

            if (resourceStatus.resourceCount <= 0)
            {
                resourceStatus.currentGague = 0;
                ClearReservedHarvestSteps();
                resourceFullyDepleted = true;
                break;
            }

            resourceStatus.currentGague = MaxGauge;
        }

        if (depletedResourceCount > 0)
        {
            UpdateBodyScale();
        }

        return depletedResourceCount;
    }

    private void PlayPickupSequence(int bagNum, int objectId, bool hideAfterSequence)
    {
        if (hideAfterSequence)
        {
            HideBodyPresentation();
        }

        EnsurePortableObjectPool(GetCount);

        StartCoroutine(PlayPickupSequenceRoutine(bagNum, objectId, GetCount, hideAfterSequence));
    }

    private void PlayConfiguredHarvestDrops(
        int depletedResourceCount,
        bool resourceFullyDepleted)
    {
        harvestRewardBuffer.Clear();
        IReadOnlyList<ResourceDropEntry> dropItems = definition != null
            ? definition.DropItems
            : null;
        float growth = ResolveDropGrowth();
        int firstDepletionOrdinal = Mathf.Max(
            0,
            initialResourceCount - ResourceCount - depletedResourceCount);

        for (int depletionIndex = 0;
             dropItems != null && depletionIndex < depletedResourceCount;
             depletionIndex++)
        {
            System.Random random = new System.Random(
                BuildHarvestDropSeed(firstDepletionOrdinal + depletionIndex));
            for (int entryIndex = 0; entryIndex < dropItems.Count; entryIndex++)
            {
                ResourceDropEntry entry = dropItems[entryIndex];
                ItemDefinition itemDefinition = entry?.ItemDefinition;
                if (itemDefinition == null
                    || itemDefinition.id < 0
                    || entry.Amount <= 0
                    || !entry.Matches(growth)
                    || random.NextDouble() >= entry.DropChance)
                {
                    continue;
                }

                harvestRewardBuffer.Add(new HarvestReward
                {
                    itemId = itemDefinition.id,
                    amount = entry.Amount
                });
            }
        }

        if (harvestRewardBuffer.Count == 0)
        {
            if (resourceFullyDepleted)
            {
                HideBodyPresentation();
                gameObject.SetActive(false);
            }

            return;
        }

        HarvestReward[] rewards = harvestRewardBuffer.ToArray();
        harvestRewardBuffer.Clear();
        if (resourceFullyDepleted)
        {
            HideBodyPresentation();
        }

        StartCoroutine(
            PlayConfiguredHarvestDropsRoutine(
                rewards,
                resourceFullyDepleted));
    }

    private IEnumerator PlayConfiguredHarvestDropsRoutine(
        IReadOnlyList<HarvestReward> rewards,
        bool hideAfterSequence)
    {
        for (int i = 0; rewards != null && i < rewards.Count; i++)
        {
            HarvestReward reward = rewards[i];
            if (reward.itemId < 0 || reward.amount <= 0)
            {
                continue;
            }

            yield return PlayPickupSequenceRoutine(
                0,
                reward.itemId,
                reward.amount,
                hideAfterSequence && i == rewards.Count - 1);
        }
    }

    private bool HasConfiguredDropItems()
    {
        return definition != null
               && definition.DropItems != null
               && definition.DropItems.Count > 0;
    }

    private bool HasHarvestableOutputAtCurrentState()
    {
        if (!(this is ProjectF.MapObjects.Tree))
        {
            return true;
        }

        if (HasConfiguredDropItems())
        {
            return GetHarvestOutputCountPerResource() > 0;
        }

        return ResolveOutputItemId() >= 0 && GetCount > 0;
    }

    private bool TryPeekConfiguredHarvestOutput(
        out int outputItemId,
        out int outputCount)
    {
        outputItemId = -1;
        outputCount = 0;
        if (!CanHarvest || definition == null)
        {
            return false;
        }

        IReadOnlyList<ResourceDropEntry> dropItems = definition.DropItems;
        float growth = ResolveDropGrowth();
        for (int i = 0; dropItems != null && i < dropItems.Count; i++)
        {
            ResourceDropEntry entry = dropItems[i];
            ItemDefinition itemDefinition = entry?.ItemDefinition;
            if (itemDefinition == null
                || itemDefinition.id < 0
                || entry.Amount <= 0
                || entry.DropChance <= 0f
                || !entry.Matches(growth))
            {
                continue;
            }

            outputItemId = itemDefinition.id;
            outputCount = entry.Amount;
            return true;
        }

        return false;
    }

    private int GetHarvestOutputCountPerResource()
    {
        if (!HasConfiguredDropItems())
        {
            return GetCount;
        }

        int count = 0;
        float growth = ResolveDropGrowth();
        IReadOnlyList<ResourceDropEntry> dropItems = definition.DropItems;
        for (int i = 0; i < dropItems.Count; i++)
        {
            ResourceDropEntry entry = dropItems[i];
            if (entry?.ItemDefinition != null
                && entry.ItemDefinition.id >= 0
                && entry.DropChance > 0f
                && entry.Matches(growth))
            {
                count += entry.Amount;
            }
        }

        return count;
    }

    protected int RollNextConfiguredHarvestDropCount(int targetItemId)
    {
        if (targetItemId < 0 || !CanHarvest || !HasConfiguredDropItems())
        {
            return 0;
        }

        int depletionOrdinal = Mathf.Max(0, initialResourceCount - ResourceCount);
        System.Random random = new System.Random(BuildHarvestDropSeed(depletionOrdinal));
        float growth = ResolveDropGrowth();
        int count = 0;
        IReadOnlyList<ResourceDropEntry> dropItems = definition.DropItems;
        for (int i = 0; i < dropItems.Count; i++)
        {
            ResourceDropEntry entry = dropItems[i];
            ItemDefinition itemDefinition = entry?.ItemDefinition;
            if (itemDefinition == null
                || itemDefinition.id < 0
                || entry.Amount <= 0
                || entry.DropChance <= 0f
                || !entry.Matches(growth))
            {
                continue;
            }

            bool dropped = random.NextDouble() < entry.DropChance;
            if (dropped && itemDefinition.id == targetItemId)
            {
                count += entry.Amount;
            }
        }

        return count;
    }

    private float ResolveDropGrowth()
    {
        return this is ProjectF.MapObjects.Tree tree
            ? tree.Growth
            : ResourceDefinition.MaxGrowth;
    }

    private int BuildHarvestDropSeed(int depletionOrdinal)
    {
        Vector2Int coordinate = owningBlock != null
            ? owningBlock.Coordinate
            : new Vector2Int(
                Mathf.RoundToInt(transform.position.x),
                Mathf.RoundToInt(transform.position.z));
        unchecked
        {
            int seed = 23;
            seed = seed * 397 ^ coordinate.x;
            seed = seed * 397 ^ coordinate.y;
            seed = seed * 397 ^ depletionOrdinal;
            string definitionName = definition != null ? definition.name : objectName;
            for (int i = 0; !string.IsNullOrEmpty(definitionName) && i < definitionName.Length; i++)
            {
                seed = seed * 31 + definitionName[i];
            }

            return seed;
        }
    }

    private IEnumerator PlayPickupSequenceRoutine(int bagNum, int objectId, int rewardCount, bool hideAfterSequence)
    {
        if (GameManager.Instance == null || GameManager.Instance.Player == null)
        {
            if (hideAfterSequence)
            {
                gameObject.SetActive(false);
            }

            yield break;
        }

        Player player = GameManager.Instance.Player;
        if (player == null)
        {
            if (hideAfterSequence)
            {
                gameObject.SetActive(false);
            }

            yield break;
        }

        int spawnedCount = 0;
        float interval = Mathf.Max(0f, portableMoveInterval);

        for (int i = 0; i < rewardCount; i++)
        {
            bool shouldHideOnComplete = hideAfterSequence && i == rewardCount - 1;
            Vector3 sourceWorldPosition = GetHarvestPortableStartWorldPosition();

            if (PlayerItemStorageUtility.TryReserveBag(
                    player,
                    objectId,
                    -1,
                    true,
                    out PlayerItemStorageReservation reservation))
            {
                spawnedCount++;
                PortableObject harvestPortableVisual = CreateHarvestPortableVisual(objectId, sourceWorldPosition);
                PlayerItemStorageUtility.MoveVisualToPlayerStorage(
                    harvestPortableVisual,
                    reservation,
                    null,
                    () =>
                    {
                        if (shouldHideOnComplete)
                        {
                            gameObject.SetActive(false);
                        }
                    });
            }
            else if (TryDropHarvestRewardToGround(player, objectId, sourceWorldPosition, shouldHideOnComplete))
            {
                spawnedCount++;
            }
            else
            {
                break;
            }

            if (interval > 0f && i < rewardCount - 1)
            {
                yield return new WaitForSeconds(interval);
            }
        }

        if (hideAfterSequence && spawnedCount <= 0)
        {
            gameObject.SetActive(false);
        }

    }

    private Vector3 GetHarvestPortableStartWorldPosition()
    {
        CachePortableObjects();

        if (portableObjects != null)
        {
            for (int i = 0; i < portableObjects.Count; i++)
            {
                PortableObject candidate = portableObjects[i];
                if (candidate != null)
                {
                    return candidate.transform.position;
                }
            }
        }

        return FocusPoint;
    }

    private PortableObject CreateHarvestPortableVisual(int objectId, Vector3 worldPosition)
    {
        PortableObject template = ResolveHarvestPortableTemplate();
        PortableObject visual;

        if (template != null)
        {
            visual = Instantiate(template);
            visual.name = $"{template.name}_HarvestTemp";
        }
        else
        {
            GameObject visualObject = new GameObject($"HarvestPortable_{objectId}");
            visualObject.layer = gameObject.layer;
            visualObject.AddComponent<MeshFilter>();
            visualObject.AddComponent<MeshRenderer>();
            visual = visualObject.AddComponent<PortableObject>();
        }

        if (visual == null)
        {
            return null;
        }

        visual.transform.SetParent(null, true);
        visual.transform.position = worldPosition;
        visual.transform.rotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        visual.gameObject.SetActive(true);

        if (!visual.SetItem(objectId))
        {
            PlayerItemStorageUtility.DestroyPortableObject(visual);
            return null;
        }

        return visual;
    }

    private PortableObject ResolveHarvestPortableTemplate()
    {
        if (portableObj != null)
        {
            return portableObj;
        }

        CachePortableObjects();
        if (portableObjects == null)
        {
            return null;
        }

        for (int i = 0; i < portableObjects.Count; i++)
        {
            if (portableObjects[i] != null)
            {
                return portableObjects[i];
            }
        }

        return null;
    }

    private bool TryDropHarvestRewardToGround(Player player, int objectId, Vector3 startWorldPosition, bool hideAfterSequence)
    {
        TerrainGenerator generator = FindTerrainGenerator();
        Action onComplete = hideAfterSequence ? () => gameObject.SetActive(false) : null;

        if (generator != null)
        {
            Vector3 dropPosition = player != null ? player.transform.position : transform.position;
            if (generator.TryAddDroppedItemAnimated(dropPosition, objectId, startWorldPosition, out _, onComplete))
            {
                return true;
            }

            if (owningBlock != null && owningBlock.TryAddFloorObjectAnimated(objectId, startWorldPosition, 0f, out _, onComplete))
            {
                return true;
            }

            if (generator.TryAddDroppedItemAnimated(transform.position, objectId, startWorldPosition, out _, onComplete))
            {
                return true;
            }
        }
        else if (owningBlock != null && owningBlock.TryAddFloorObjectAnimated(objectId, startWorldPosition, 0f, out _, onComplete))
        {
            return true;
        }

        return false;
    }

    private TerrainGenerator FindTerrainGenerator()
    {
        return TerrainGenerator.ResolveActive();
    }

    private void EnsureStatusInitialized()
    {
        bool isUninitialized =
            resourceStatus.resourceCount <= 0 &&
            resourceStatus.maxGauge <= 0 &&
            resourceStatus.currentGague <= 0;

        if (isUninitialized)
        {
            resourceStatus.resourceCount = 1;
            resourceStatus.getCount = 1;
            resourceStatus.maxGauge = 10;
            resourceStatus.currentGague = resourceStatus.maxGauge;
        }

        resourceStatus.maxGauge = Mathf.Max(1, resourceStatus.maxGauge);
        resourceStatus.getCount = Mathf.Max(1, resourceStatus.getCount);
        resourceStatus.resourceCount = Mathf.Max(0, resourceStatus.resourceCount);

        if (resourceStatus.resourceCount <= 0)
        {
            resourceStatus.currentGague = 0;
            return;
        }

        if (resourceStatus.currentGague <= 0 || resourceStatus.currentGague > resourceStatus.maxGauge)
        {
            resourceStatus.currentGague = resourceStatus.maxGauge;
        }
    }

    private int GetReservableHarvestGaugeCount(int harvestPower)
    {
        int totalRemainingSteps = ((Mathf.Max(0, ResourceCount - 1)) * MaxGauge) + CurrentGauge;
        int remainingReservableGaugeCount = Mathf.Max(0, totalRemainingSteps - reservedHarvestGaugeCount);
        if (remainingReservableGaugeCount <= 0)
        {
            return 0;
        }

        return Mathf.Min(Mathf.Max(1, harvestPower), remainingReservableGaugeCount);
    }

    private void ClearReservedHarvestSteps()
    {
        reservedHarvestGaugeCosts.Clear();
        reservedHarvestGaugeCount = 0;
    }

    private int ResolveOutputItemId()
    {
        if (TryResolveDefinitionOutputItem(out int definitionOutputItemId, out string definitionOutputItemName))
        {
            resourceStatus.outputId = definitionOutputItemId;
            resourceStatus.outputItemName = definitionOutputItemName;
            return definitionOutputItemId;
        }

        if (TryResolveOutputItemNameToId(resourceStatus.outputItemName, out int namedOutputItemId))
        {
            resourceStatus.outputId = namedOutputItemId;
            resourceStatus.outputItemName = resourceStatus.outputItemName.Trim();
            return namedOutputItemId;
        }

        if (resourceStatus.outputId >= 0)
        {
            return resourceStatus.outputId;
        }

        return ResolveItemId();
    }

    private void MigrateOutputItemNameIfNeeded()
    {
        if (TryResolveDefinitionOutputItem(out int definitionOutputItemId, out string definitionOutputItemName))
        {
            resourceStatus.outputId = definitionOutputItemId;
            resourceStatus.outputItemName = definitionOutputItemName;
            return;
        }

        if (!string.IsNullOrWhiteSpace(resourceStatus.outputItemName))
        {
            resourceStatus.outputItemName = resourceStatus.outputItemName.Trim();
            if (TryResolveOutputItemNameToId(resourceStatus.outputItemName, out int namedOutputItemId))
            {
                resourceStatus.outputId = namedOutputItemId;
            }

            return;
        }

        if (TryResolveItemNameFromId(resourceStatus.outputId, out string outputItemName))
        {
            resourceStatus.outputItemName = outputItemName;
            return;
        }

        if (TryResolveItemNameFromId(ResolveItemId(), out outputItemName))
        {
            resourceStatus.outputItemName = outputItemName;
        }
    }

    private bool TryResolveDefinitionOutputItem(out int outputItemId, out string outputItemName)
    {
        outputItemId = -1;
        outputItemName = null;
        if (definition == null || string.IsNullOrWhiteSpace(definition.resourceName))
        {
            return false;
        }

        string candidateName = definition.resourceName.Trim();
        if (!TryResolveOutputItemNameToId(candidateName, out outputItemId))
        {
            return false;
        }

        outputItemName = candidateName;
        return outputItemId >= 0;
    }

    private bool TryResolveOutputItemNameToId(string outputItemName, out int outputItemId)
    {
        outputItemId = -1;
        if (string.IsNullOrWhiteSpace(outputItemName))
        {
            return false;
        }

        string normalizedName = outputItemName.Trim();
        if (GameManager.Instance != null && GameManager.Instance.ItemManger != null)
        {
            List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    ItemDefinition definition = definitions[i];
                    if (definition == null)
                    {
                        continue;
                    }

                    string definitionName = string.IsNullOrWhiteSpace(definition.itemName)
                        ? definition.name
                        : definition.itemName;
                    if (!string.Equals(definitionName, normalizedName, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(definition.name, normalizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    outputItemId = definition.id;
                    return outputItemId >= 0;
                }
            }
        }

#if UNITY_EDITOR
        return TryResolveItemIdFromEditorAssets(normalizedName, out outputItemId);
#else
        return false;
#endif
    }

    private bool TryResolveItemNameFromId(int itemId, out string outputItemName)
    {
        outputItemName = null;
        if (itemId < 0)
        {
            return false;
        }

        if (GameManager.Instance != null
            && GameManager.Instance.ItemManger != null
            && GameManager.Instance.ItemManger.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            outputItemName = string.IsNullOrWhiteSpace(itemSet.name) ? null : itemSet.name.Trim();
            if (!string.IsNullOrWhiteSpace(outputItemName))
            {
                return true;
            }
        }

#if UNITY_EDITOR
        return TryResolveItemNameFromEditorAssets(itemId, out outputItemName);
#else
        return false;
#endif
    }

#if UNITY_EDITOR
    private static bool TryResolveItemNameFromEditorAssets(int itemId, out string outputItemName)
    {
        outputItemName = null;
        if (itemId < 0)
        {
            return false;
        }

        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data/Items" });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition == null || definition.id != itemId)
            {
                continue;
            }

            outputItemName = string.IsNullOrWhiteSpace(definition.itemName)
                ? definition.name
                : definition.itemName.Trim();
            return !string.IsNullOrWhiteSpace(outputItemName);
        }

        return false;
    }

    private static bool TryResolveItemIdFromEditorAssets(string outputItemName, out int outputItemId)
    {
        outputItemId = -1;
        if (string.IsNullOrWhiteSpace(outputItemName))
        {
            return false;
        }

        string normalizedName = outputItemName.Trim();
        string[] definitionGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data/Items" });
        for (int i = 0; i < definitionGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition == null)
            {
                continue;
            }

            string definitionName = string.IsNullOrWhiteSpace(definition.itemName)
                ? definition.name
                : definition.itemName.Trim();
            if (!string.Equals(definitionName, normalizedName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(definition.name, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            outputItemId = definition.id;
            return outputItemId >= 0;
        }

        return false;
    }
#endif

    private void CacheBodyTransform()
    {
        if (bodyTransform != null)
        {
            return;
        }

        Transform foundBody = transform.Find("Body");
        bodyTransform = foundBody != null ? foundBody : transform;
        initialBodyLocalScale = bodyTransform.localScale;
        initialBodyLocalRotation = bodyTransform.localRotation;
    }

    private static int NormalizeBodyYawStep(int yawStep)
    {
        int normalizedStep = yawStep % BodyYawStepCount;
        return normalizedStep < 0 ? normalizedStep + BodyYawStepCount : normalizedStep;
    }

    private Transform GetExtraBodyRendererRoot()
    {
        Transform extraRoot = transform.Find(ExtraBodyRendererRootName);
        if (extraRoot == null || extraRoot == bodyTransform)
        {
            return null;
        }

        return extraRoot;
    }

    private void SyncExtraBodyRendererRootToBody()
    {
        CacheBodyTransform();
        Transform extraRoot = GetExtraBodyRendererRoot();
        if (bodyTransform == null || extraRoot == null)
        {
            return;
        }

        extraRoot.localPosition = bodyTransform.localPosition;
        extraRoot.localRotation = bodyTransform.localRotation;
        extraRoot.localScale = bodyTransform.localScale;
    }

    private void CachePortableObjects()
    {
        if (portableObjects == null)
        {
            portableObjects = new List<PortableObject>();
        }

        PortableObject[] foundPortableObjects = GetComponentsInChildren<PortableObject>(true);
        for (int i = 0; i < foundPortableObjects.Length; i++)
        {
            PortableObject candidate = foundPortableObjects[i];
            if (candidate == null || portableObjects.Contains(candidate))
            {
                continue;
            }

            portableObjects.Add(candidate);
        }

        portableObjects.RemoveAll(item => item == null);

        if ((portableObj == null || !portableObjects.Contains(portableObj)) && portableObjects.Count > 0)
        {
            portableObj = portableObjects[0];
        }

        for (int i = 0; i < portableObjects.Count; i++)
        {
            PortableObject candidate = portableObjects[i];
            if (candidate == null)
            {
                continue;
            }

            candidate.transform.SetParent(transform, true);
            candidate.SetItem(ResolveOutputItemId());
            candidate.gameObject.SetActive(false);
        }
    }

    private void EnsurePortableObjectPool(int requiredCount)
    {
        CachePortableObjects();

        if (portableObjects == null || portableObjects.Count <= 0)
        {
            return;
        }

        PortableObject template = portableObjects[0];
        if (template == null)
        {
            return;
        }

        while (portableObjects.Count < requiredCount)
        {
            PortableObject clone = Instantiate(template, transform);
            clone.name = $"{template.name}_{portableObjects.Count}";
            clone.transform.localPosition = template.transform.localPosition;
            clone.transform.localRotation = template.transform.localRotation;
            clone.transform.localScale = template.transform.localScale;
            clone.SetItem(ResolveOutputItemId());
            clone.gameObject.SetActive(false);
            portableObjects.Add(clone);
        }
    }

    private List<PortableObject> ReservePortableObjectInstances(int requiredCount)
    {
        EnsurePortableObjectPool(GetCount);
        List<PortableObject> reserved = new List<PortableObject>(requiredCount);

        for (int i = 0; i < requiredCount; i++)
        {
            PortableObject candidate = GetPortableObjectInstanceAt(i);
            if (candidate == null)
            {
                continue;
            }

            candidate.SetItem(ResolveOutputItemId());
            reserved.Add(candidate);
        }

        return reserved;
    }

    private PortableObject GetPortableObjectInstanceAt(int index)
    {
        EnsurePortableObjectPool(index + 1);

        if (portableObjects != null && index >= 0 && index < portableObjects.Count && portableObjects[index] != null)
        {
            return portableObjects[index];
        }

        if (portableObjects == null || portableObjects.Count <= 0 || portableObjects[0] == null)
        {
            return null;
        }

        PortableObject clone = Instantiate(portableObjects[0], transform);
        clone.name = $"{portableObjects[0].name}_{portableObjects.Count}";
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        clone.SetItem(ResolveOutputItemId());
        clone.gameObject.SetActive(false);
        portableObjects.Add(clone);
        return clone;
    }

    private void CaptureInitialStateIfNeeded()
    {
        if (initialResourceCount > 0)
        {
            return;
        }

        initialResourceCount = Mathf.Max(1, resourceStatus.resourceCount);
    }

    private void UpdateBodyScale()
    {
        CacheBodyTransform();
        CaptureInitialStateIfNeeded();

        if (bodyTransform == null)
        {
            return;
        }

        if (!Application.isPlaying)
        {
            ApplyEditorBodyScale();
            return;
        }

        if (ResourceCount > 0)
        {
            ShowBodyPresentation();
        }

        float scaleRatio;
        if (ResourceCount <= 0)
        {
            scaleRatio = 0f;
        }
        else if (!useDynamicBodyScale)
        {
            scaleRatio = 1f;
        }
        else
        {
            float normalizedCount = Mathf.Clamp01((float)ResourceCount / Mathf.Max(1, dynamicScaleMaxResourceCount));
            scaleRatio = Mathf.Lerp(minimumBodyScaleRatio, maximumBodyScaleRatio, normalizedCount);
        }

        scaleRatio *= Mathf.Max(0f, GetAdditionalBodyScaleRatio());
        bodyTransform.localScale = initialBodyLocalScale * scaleRatio;
        SyncExtraBodyRendererRootToBody();
        MarkBatchRenderDataDirty();
    }

    private void ApplyDefinitionIfNeeded()
    {
        if (definition == null)
        {
            return;
        }

        harvestMode = definition.harvestMode;

        if (resourceStatus.resourceCount <= 0)
        {
            resourceStatus.resourceCount = Mathf.Max(1, definition.defaultResourceCount);
        }

        resourceStatus.getCount = Mathf.Max(1, definition.defaultGetCount);

        if (resourceStatus.maxGauge <= 0)
        {
            resourceStatus.maxGauge = Mathf.Max(1, definition.defaultMaxGauge);
        }

        if (resourceStatus.currentGague <= 0)
        {
            resourceStatus.currentGague = Mathf.Clamp(
                definition.defaultCurrentGauge,
                0,
                Mathf.Max(1, resourceStatus.maxGauge));
        }
    }

    private void ApplyEditorBodyScale()
    {
        CacheBodyTransform();
        if (bodyTransform == null)
        {
            return;
        }

        bodyTransform.localScale = Vector3.one;
        SyncExtraBodyRendererRootToBody();
    }

    private void HideBodyPresentation()
    {
        ToggleBodyPresentation(false);
    }

    private void ShowBodyPresentation()
    {
        ToggleBodyPresentation(true);
    }

    private void ToggleBodyPresentation(bool isVisible)
    {
        bodyPresentationVisible = isVisible;
        CacheBodyTransform();
        if (bodyTransform == null)
        {
            return;
        }

        ToggleBodyPresentationForRoot(bodyTransform);
        Transform extraRoot = GetExtraBodyRendererRoot();
        if (extraRoot != null)
        {
            ToggleBodyPresentationForRoot(extraRoot);
        }

        MarkBatchRenderDataDirty();
    }

    private void ToggleBodyPresentationForRoot(Transform renderRoot)
    {
        if (renderRoot == null)
        {
            return;
        }

        Renderer[] renderers = renderRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null || IsPortableHierarchy(targetRenderer.transform))
            {
                continue;
            }

            targetRenderer.enabled = bodyPresentationVisible;
            if (targetRenderer is MeshRenderer meshRenderer)
            {
                meshRenderer.forceRenderingOff = bodyPresentationVisible
                                                 && useBatchedRendering
                                                 && supportsBatchedRendering
                                                 && IsBatchedMeshRenderer(meshRenderer);
            }
        }

        Collider[] colliders = renderRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider targetCollider = colliders[i];
            if (targetCollider == null || IsPortableHierarchy(targetCollider.transform))
            {
                continue;
            }

            targetCollider.enabled = bodyPresentationVisible;
        }
    }

    private bool IsPortableHierarchy(Transform target)
    {
        if (target == null || portableObjects == null)
        {
            return false;
        }

        for (int i = 0; i < portableObjects.Count; i++)
        {
            PortableObject candidate = portableObjects[i];
            if (candidate != null && target.IsChildOf(candidate.transform))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBatchedMeshRenderer(MeshRenderer meshRenderer)
    {
        if (meshRenderer == null)
        {
            return false;
        }

        for (int i = 0; i < batchedRenderEntries.Count; i++)
        {
            if (batchedRenderEntries[i].MeshRenderer == meshRenderer)
            {
                return true;
            }
        }

        return false;
    }

    public void SetOwningBlock(Block block)
    {
        if (owningBlock == block)
        {
            RegisterActiveResourceCoordinate();
            OnOwningBlockChanged(block);
            return;
        }

        UnregisterActiveResourceCoordinate();
        owningBlock = block;
        RegisterActiveResourceCoordinate();
        OnOwningBlockChanged(block);
    }

    protected virtual void OnOwningBlockChanged(Block block)
    {
    }

    public static bool TryCollectActiveResourcesInCoordinateRange(
        Vector2Int center,
        int radius,
        List<Resource> results)
    {
        if (results == null)
        {
            return false;
        }

        results.Clear();
        if (ActiveResourcesByCoordinate.Count <= 0)
        {
            return false;
        }

        int clampedRadius = Mathf.Max(0, radius);
        for (int offsetY = -clampedRadius; offsetY <= clampedRadius; offsetY++)
        {
            for (int offsetX = -clampedRadius; offsetX <= clampedRadius; offsetX++)
            {
                Vector2Int coordinate = center + new Vector2Int(offsetX, offsetY);
                if (!ActiveResourcesByCoordinate.TryGetValue(coordinate, out List<Resource> resources)
                    || resources == null
                    || resources.Count <= 0)
                {
                    continue;
                }

                for (int i = resources.Count - 1; i >= 0; i--)
                {
                    Resource resource = resources[i];
                    if (!IsActiveResourceCoordinateEntryValid(resource, coordinate))
                    {
                        resources.RemoveAt(i);
                        RefreshStaleCoordinateEntry(resource, coordinate);
                        continue;
                    }

                    results.Add(resource);
                }

                if (resources.Count <= 0)
                {
                    ActiveResourcesByCoordinate.Remove(coordinate);
                }
            }
        }

        return true;
    }

    private static bool IsActiveResourceCoordinateEntryValid(Resource resource, Vector2Int coordinate)
    {
        return resource != null
               && resource.activeResourceCoordinateRegistered
               && resource.activeResourceCoordinate == coordinate
               && resource.owningBlock != null
               && resource.owningBlock.Coordinate == coordinate
               && resource.gameObject.activeInHierarchy;
    }

    private static void RefreshStaleCoordinateEntry(Resource resource, Vector2Int coordinate)
    {
        if (resource == null
            || !resource.activeResourceCoordinateRegistered
            || resource.activeResourceCoordinate != coordinate)
        {
            return;
        }

        resource.activeResourceCoordinateRegistered = false;
        resource.activeResourceCoordinate = default;
        resource.RegisterActiveResourceCoordinate();
    }

    private void RegisterActiveResourceCoordinate()
    {
        if (!CanRegisterActiveResourceCoordinate())
        {
            return;
        }

        Vector2Int coordinate = owningBlock.Coordinate;
        if (activeResourceCoordinateRegistered && activeResourceCoordinate == coordinate)
        {
            return;
        }

        UnregisterActiveResourceCoordinate();
        if (!ActiveResourcesByCoordinate.TryGetValue(coordinate, out List<Resource> resources)
            || resources == null)
        {
            resources = new List<Resource>();
            ActiveResourcesByCoordinate[coordinate] = resources;
        }

        if (!resources.Contains(this))
        {
            resources.Add(this);
        }

        activeResourceCoordinate = coordinate;
        activeResourceCoordinateRegistered = true;
    }

    private void UnregisterActiveResourceCoordinate()
    {
        if (!activeResourceCoordinateRegistered)
        {
            return;
        }

        if (ActiveResourcesByCoordinate.TryGetValue(activeResourceCoordinate, out List<Resource> resources)
            && resources != null)
        {
            resources.Remove(this);
            if (resources.Count <= 0)
            {
                ActiveResourcesByCoordinate.Remove(activeResourceCoordinate);
            }
        }

        activeResourceCoordinateRegistered = false;
        activeResourceCoordinate = default;
    }

    private bool CanRegisterActiveResourceCoordinate()
    {
        return Application.isPlaying
               && isActiveAndEnabled
               && owningBlock != null;
    }

    public void SetBatchedRendering(bool shouldUseBatchedRendering)
    {
        ResolveBatchComponents();
        bool canUseBatching = Application.isPlaying && supportsBatchedRendering && shouldUseBatchedRendering;
        if (useBatchedRendering == canUseBatching && (!useBatchedRendering || batchRenderer != null))
        {
            ToggleBodyPresentation(bodyPresentationVisible);
            return;
        }

        useBatchedRendering = canUseBatching;
        if (!useBatchedRendering)
        {
            UnregisterFromBatchRenderer();
            ToggleBodyPresentation(bodyPresentationVisible);
            return;
        }

        batchRenderer = ResolveBatchRenderer();
        if (batchRenderer == null)
        {
            useBatchedRendering = false;
            ToggleBodyPresentation(bodyPresentationVisible);
            return;
        }

        batchRenderer.Register(this);
        ToggleBodyPresentation(bodyPresentationVisible);
    }

    public int BatchRenderEntryCount
    {
        get
        {
            ResolveBatchComponents();
            return batchedRenderEntries.Count;
        }
    }

    public bool TryGetBatchRenderData(
        int entryIndex,
        out Mesh mesh,
        out Material[] materials,
        out Matrix4x4 localToWorldMatrix,
        out Vector3 worldPosition,
        out int layer,
        out ShadowCastingMode shadowCastingMode,
        out bool receiveShadows,
        out bool useGlobalBatch)
    {
        ResolveBatchComponents();

        mesh = null;
        materials = Array.Empty<Material>();
        localToWorldMatrix = Matrix4x4.identity;
        worldPosition = transform.position;
        layer = gameObject.layer;
        shadowCastingMode = ShadowCastingMode.Off;
        receiveShadows = false;
        useGlobalBatch = false;

        if (entryIndex < 0 || entryIndex >= batchedRenderEntries.Count)
        {
            return false;
        }

        BatchRenderEntry entry = batchedRenderEntries[entryIndex];
        MeshFilter meshFilter = entry.MeshFilter;
        MeshRenderer meshRenderer = entry.MeshRenderer;
        mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        materials = entry.Materials ?? Array.Empty<Material>();
        localToWorldMatrix = meshFilter != null ? meshFilter.transform.localToWorldMatrix : Matrix4x4.identity;
        worldPosition = meshFilter != null ? meshFilter.transform.position : transform.position;
        layer = meshRenderer != null ? meshRenderer.gameObject.layer : gameObject.layer;
        shadowCastingMode = meshRenderer != null ? meshRenderer.shadowCastingMode : ShadowCastingMode.Off;
        receiveShadows = meshRenderer != null && meshRenderer.receiveShadows;
        HarvestMode harvestMode = ResolvedHarvestMode;
        useGlobalBatch = harvestMode == HarvestMode.Logging || harvestMode == HarvestMode.Cut;

        return useBatchedRendering
               && bodyPresentationVisible
               && gameObject.activeInHierarchy
               && meshFilter != null
               && meshFilter.gameObject.activeInHierarchy
               && supportsBatchedRendering
               && mesh != null
               && HasAnyBatchMaterial(materials);
    }

    private void ResolveBatchComponents()
    {
        if (batchComponentsResolved)
        {
            return;
        }

        batchComponentsResolved = true;
        supportsBatchedRendering = false;
        batchedRenderEntries.Clear();

        CacheBodyTransform();
        CachePortableObjects();
        if (bodyTransform == null)
        {
            return;
        }

        AddBatchRenderEntriesFromRoot(bodyTransform);
        Transform extraRoot = GetExtraBodyRendererRoot();
        if (extraRoot != null)
        {
            AddBatchRenderEntriesFromRoot(extraRoot);
        }

        supportsBatchedRendering = batchedRenderEntries.Count > 0;
    }

    private void AddBatchRenderEntriesFromRoot(Transform renderRoot)
    {
        if (renderRoot == null)
        {
            return;
        }

        MeshFilter[] meshFilters = renderRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter candidate = meshFilters[i];
            if (candidate == null || candidate.sharedMesh == null || IsPortableHierarchy(candidate.transform))
            {
                continue;
            }

            MeshRenderer candidateRenderer = candidate.GetComponent<MeshRenderer>();
            if (candidateRenderer == null || IsPortableHierarchy(candidateRenderer.transform))
            {
                continue;
            }

            Material[] sharedMaterials = candidateRenderer.sharedMaterials ?? Array.Empty<Material>();
            if (!HasAnyBatchMaterial(sharedMaterials))
            {
                continue;
            }

            for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
            {
                Material material = sharedMaterials[materialIndex];
                if (material != null && !material.enableInstancing)
                {
                    material.enableInstancing = true;
                }
            }

            batchedRenderEntries.Add(new BatchRenderEntry(candidate, candidateRenderer, sharedMaterials));
            cachedRenderer ??= candidateRenderer;
        }
    }

    private readonly struct BatchRenderEntry
    {
        public readonly MeshFilter MeshFilter;
        public readonly MeshRenderer MeshRenderer;
        public readonly Material[] Materials;

        public BatchRenderEntry(MeshFilter meshFilter, MeshRenderer meshRenderer, Material[] materials)
        {
            MeshFilter = meshFilter;
            MeshRenderer = meshRenderer;
            Materials = materials;
        }
    }

    private static bool HasAnyBatchMaterial(Material[] materials)
    {
        if (materials == null)
        {
            return false;
        }

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    private ResourceBatchRenderer ResolveBatchRenderer()
    {
        if (batchRenderer != null)
        {
            return batchRenderer;
        }

        TerrainGenerator generator = GetComponentInParent<TerrainGenerator>();
        GameObject host = generator != null ? generator.gameObject : null;
        if (host == null)
        {
            return null;
        }

        batchRenderer = host.GetComponent<ResourceBatchRenderer>();
        if (batchRenderer == null)
        {
            batchRenderer = host.AddComponent<ResourceBatchRenderer>();
        }

        return batchRenderer;
    }

    private void UnregisterFromBatchRenderer()
    {
        if (batchRenderer == null)
        {
            return;
        }

        batchRenderer.Unregister(this);
        if (!useBatchedRendering)
        {
            batchRenderer = null;
        }
    }

    protected void MarkBatchRenderDataDirty()
    {
        if (useBatchedRendering)
        {
            batchRenderer?.MarkDirty();
        }
    }
}

public class ResourceBatchRenderer : MonoBehaviour
{
    private const int MaxInstancesPerDraw = 1023;

    [SerializeField, Min(1f)]
    private float batchCellSize = 8f;

    private readonly HashSet<Resource> registeredResources = new HashSet<Resource>();
    private readonly Dictionary<BatchKey, List<Matrix4x4>> matricesByBatch = new Dictionary<BatchKey, List<Matrix4x4>>();
    private readonly List<BatchKey> activeBatchKeys = new List<BatchKey>();
    private readonly List<Resource> cleanupBuffer = new List<Resource>();
    private bool batchesDirty = true;
    private TerrainGenerator terrainGenerator;

    public void Register(Resource resource)
    {
        if (resource == null)
        {
            return;
        }

        if (registeredResources.Add(resource))
        {
            batchesDirty = true;
        }
    }

    public void Unregister(Resource resource)
    {
        if (resource == null)
        {
            return;
        }

        if (registeredResources.Remove(resource))
        {
            batchesDirty = true;
        }
    }

    public void MarkDirty()
    {
        batchesDirty = true;
    }

    protected void LateUpdate()
    {
        if (registeredResources.Count <= 0)
        {
            if (activeBatchKeys.Count > 0)
            {
                ClearActiveBatches();
                batchesDirty = false;
            }

            return;
        }

        if (batchesDirty)
        {
            RebuildBatches();
            batchesDirty = false;
        }

        RenderBatches();
    }

    private void ClearActiveBatches()
    {
        for (int i = 0; i < activeBatchKeys.Count; i++)
        {
            BatchKey key = activeBatchKeys[i];
            if (matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices))
            {
                matrices.Clear();
            }
        }

        activeBatchKeys.Clear();
        cleanupBuffer.Clear();
    }

    private void RebuildBatches()
    {
        ClearActiveBatches();

        foreach (Resource resource in registeredResources)
        {
            if (resource == null)
            {
                cleanupBuffer.Add(resource);
                continue;
            }

            int entryCount = resource.BatchRenderEntryCount;
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                if (!resource.TryGetBatchRenderData(
                        entryIndex,
                        out Mesh mesh,
                        out Material[] materials,
                        out Matrix4x4 localToWorldMatrix,
                        out Vector3 worldPosition,
                        out int layer,
                        out ShadowCastingMode shadowCastingMode,
                        out bool receiveShadows,
                        out bool useGlobalBatch))
                {
                    continue;
                }

                terrainGenerator ??= GetComponent<TerrainGenerator>();
                if (terrainGenerator != null
                    && !terrainGenerator.IsWorldPositionWithinPlayerRenderRange(worldPosition))
                {
                    continue;
                }

                int materialCount = materials != null ? materials.Length : 0;
                if (materialCount <= 0)
                {
                    continue;
                }

                int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
                int renderPassCount = Mathf.Max(subMeshCount, materialCount);
                for (int passIndex = 0; passIndex < renderPassCount; passIndex++)
                {
                    int materialIndex = Mathf.Min(passIndex, materialCount - 1);
                    Material material = materials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    if (!material.enableInstancing)
                    {
                        material.enableInstancing = true;
                    }

                    int subMeshIndex = Mathf.Min(passIndex, subMeshCount - 1);
                    AddBatchMatrix(
                        mesh,
                        material,
                        subMeshIndex,
                        localToWorldMatrix,
                        worldPosition,
                        layer,
                        shadowCastingMode,
                        receiveShadows,
                        useGlobalBatch);
                }
            }
        }

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            registeredResources.Remove(cleanupBuffer[i]);
        }
    }

    private void AddBatchMatrix(
        Mesh mesh,
        Material material,
        int subMeshIndex,
        Matrix4x4 localToWorldMatrix,
        Vector3 worldPosition,
        int layer,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows,
        bool useGlobalBatch)
    {
        if (mesh == null || material == null || subMeshIndex < 0)
        {
            return;
        }

        int cellX = useGlobalBatch ? 0 : Mathf.FloorToInt(worldPosition.x / batchCellSize);
        int cellZ = useGlobalBatch ? 0 : Mathf.FloorToInt(worldPosition.z / batchCellSize);
        BatchKey key = new BatchKey(mesh, material, subMeshIndex, layer, shadowCastingMode, receiveShadows, cellX, cellZ);
        if (!matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices))
        {
            matrices = new List<Matrix4x4>(16);
            matricesByBatch.Add(key, matrices);
        }

        if (matrices.Count == 0)
        {
            activeBatchKeys.Add(key);
        }

        matrices.Add(localToWorldMatrix);
    }

    private void RenderBatches()
    {
        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            BatchKey key = activeBatchKeys[batchIndex];
            if (!matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices) || matrices.Count <= 0)
            {
                continue;
            }

            RenderParams renderParams = new RenderParams(key.Material)
            {
                layer = key.Layer,
                shadowCastingMode = key.ShadowCastingMode,
                receiveShadows = key.ReceiveShadows
            };

            int remaining = matrices.Count;
            int startIndex = 0;
            while (remaining > 0)
            {
                int drawCount = Mathf.Min(MaxInstancesPerDraw, remaining);
                Graphics.RenderMeshInstanced(renderParams, key.Mesh, key.SubMeshIndex, matrices, drawCount, startIndex);
                startIndex += drawCount;
                remaining -= drawCount;
            }
        }
    }

    private readonly struct BatchKey
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly int SubMeshIndex;
        public readonly int Layer;
        public readonly ShadowCastingMode ShadowCastingMode;
        public readonly bool ReceiveShadows;
        public readonly int CellX;
        public readonly int CellZ;

        public BatchKey(
            Mesh mesh,
            Material material,
            int subMeshIndex,
            int layer,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows,
            int cellX,
            int cellZ)
        {
            Mesh = mesh;
            Material = material;
            SubMeshIndex = subMeshIndex;
            Layer = layer;
            ShadowCastingMode = shadowCastingMode;
            ReceiveShadows = receiveShadows;
            CellX = cellX;
            CellZ = cellZ;
        }

        public override int GetHashCode()
        {
            int hash = Mesh != null ? Mesh.GetInstanceID() : 0;
            hash = (hash * 397) ^ (Material != null ? Material.GetInstanceID() : 0);
            hash = (hash * 397) ^ SubMeshIndex;
            hash = (hash * 397) ^ Layer;
            hash = (hash * 397) ^ (int)ShadowCastingMode;
            hash = (hash * 397) ^ (ReceiveShadows ? 1 : 0);
            hash = (hash * 397) ^ CellX;
            hash = (hash * 397) ^ CellZ;
            return hash;
        }

        public override bool Equals(object obj)
        {
            return obj is BatchKey other && Equals(other);
        }

        private bool Equals(BatchKey other)
        {
            return Mesh == other.Mesh
                   && Material == other.Material
                   && SubMeshIndex == other.SubMeshIndex
                   && Layer == other.Layer
                   && ShadowCastingMode == other.ShadowCastingMode
                   && ReceiveShadows == other.ReceiveShadows
                   && CellX == other.CellX
                   && CellZ == other.CellZ;
        }
    }
}
