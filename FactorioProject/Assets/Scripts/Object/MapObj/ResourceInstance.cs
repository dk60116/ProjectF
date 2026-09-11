using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using HarvestMode = Resource.HarvestMode;
using ResourceStatus = Resource.ResourceStatus;
using ResourceSaveState = Resource.ResourceSaveState;
#if UNITY_EDITOR
using UnityEditor;
#endif

// A stable managed identity over a generation-checked slot, never a Component.
public class ResourceInstance : IMapObjectTarget, IMapObjectSimulationIdentity
{
    private struct HarvestReward { public int itemId; public int amount; }
    private const int BodyYawStepCount = 8;
    private const float BodyYawStepDegrees = 45f;
    private static readonly List<ResourceInstance> ActiveResourcesInternal = new List<ResourceInstance>();
    private static readonly HashSet<ResourceInstance> ActiveResourceLookup = new HashSet<ResourceInstance>();
    private static readonly Dictionary<Vector2Int, List<ResourceInstance>> ActiveResourcesByCoordinate = new Dictionary<Vector2Int, List<ResourceInstance>>();
    private readonly ResourceTypeWorld sharedWorld;
    public ResourceHandle Handle { get; }
    private ref ResourceRuntimeState State => ref sharedWorld.GetState(Handle);
    private ref ResourceStatus resourceStatus => ref State.Status;
    private ref float accumulatedWork => ref State.AccumulatedWork;
    private ref int reservedHarvestGaugeCount => ref State.ReservedGaugeCount;
    private ref int initialResourceCount => ref State.InitialResourceCount;
    private ref bool hasBodyYawStep => ref State.HasYaw;
    private ref int bodyYawStep => ref State.YawStep;
    private ref bool useDynamicBodyScale => ref State.DynamicScale;
    private ref float minimumBodyScaleRatio => ref State.MinimumScale;
    private ref float maximumBodyScaleRatio => ref State.MaximumScale;
    private ref int dynamicScaleMaxResourceCount => ref State.ScaleMaximumCount;
    private ref float sharedBodyScale => ref State.BodyScale;
    private ref bool bodyPresentationVisible => ref State.BodyVisible;
    internal ref Vector3 SharedGaugeFill => ref State.GaugeFill;
    private Queue<int> harvestReservations;
    private Queue<int> reservedHarvestGaugeCosts => harvestReservations ?? (harvestReservations = new Queue<int>());
    private List<HarvestReward> rewardBuffer;
    private List<HarvestReward> harvestRewardBuffer => rewardBuffer ?? (rewardBuffer = new List<HarvestReward>(8));
    private sealed class HarvestRoutine
    {
        internal Coroutine Coroutine;
        internal bool Complete;
    }
    private List<HarvestRoutine> coroutines;
    private Block owningBlock;
    private bool activeResourceCoordinateRegistered;
    private Vector2Int activeResourceCoordinate;
    private bool useBatchedRendering;
    private ResourceBatchRenderer batchRenderer;
    private bool released;
    private int activeResourceListIndex = -1;
    private bool sharedBoundsDirty = true;
    private Bounds sharedBounds;
    internal ResourceTypeWorld.CollisionInstance[] SharedColliders;
    private ResourceDefinition definition => sharedWorld.Prototype.Definition;
    private float workPerGaugeDot => sharedWorld.Prototype.WorkPerGaugeDot;
    private float portableMoveInterval => sharedWorld.Prototype.PortableMoveInterval;
    private string objectName => ObjectName;
    public string ObjectName => sharedWorld.Prototype.ObjectName;
    public string SourceName => sharedWorld.Prototype.name;
    public Resource Prototype => sharedWorld.Prototype;
    public MapObject SceneObject => null;
    public bool IsTargetActive => IsRuntimeActive;
    public Vector3 WorldPosition => released ? sharedBounds.center : State.Position;
    public bool IsRuntimeActive => !released && Handle.IsValid && sharedWorld.isActiveAndEnabled && owningBlock != null;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public ItemDefinition BoundItemDefinition => Prototype.BoundItemDefinition;
    public int ResolveItemId() => Prototype.ResolveItemId();
    public int ResolvedItemId => ResolveItemId();
    public int ID => ResolveItemId();
    public Vector3 FocusPoint => PresentationBounds.center + Prototype.FocusOffset;
    public Bounds PresentationBounds
    {
        get { if (sharedBoundsDirty && !released) { sharedBounds = sharedWorld.GetBounds(this); sharedBoundsDirty = false; } return sharedBounds; }
    }
    internal float SharedYawDegrees => hasBodyYawStep ? bodyYawStep * BodyYawStepDegrees : 0f;
    internal float SharedBodyScale => sharedBodyScale;
    internal bool SharedBodyVisible => bodyPresentationVisible && ResourceCount > 0;
    public HarvestMode ResolvedHarvestMode => sharedWorld.HarvestMode;
    internal ResourceInstance(ResourceTypeWorld world, ResourceHandle handle)
    {
        sharedWorld = world; Handle = handle;
        ApplyDefinitionIfNeeded(); EnsureStatusInitialized(); MigrateOutputItemNameIfNeeded(); CaptureInitialStateIfNeeded();
    }
    internal void Activate()
    {
        if (ActiveResourceLookup.Add(this))
        {
            activeResourceListIndex = ActiveResourcesInternal.Count;
            ActiveResourcesInternal.Add(this);
        }
        UpdateBodyScale(); SetBatchedRendering(true);
    }
    public void ReleaseRuntime()
    {
        if (released) return;
        UnregisterActiveResourceCoordinate();
        if (this is IMapObjectUpdateTick tick) MapObjectTickManager.UnregisterUpdateTick(tick);
        batchRenderer?.Unregister(this);
        while (coroutines != null && coroutines.Count > 0)
        {
            HarvestRoutine routine = coroutines[coroutines.Count - 1];
            coroutines.RemoveAt(coroutines.Count - 1);
            if (routine.Coroutine != null && sharedWorld != null) sharedWorld.StopCoroutine(routine.Coroutine);
        }
        harvestReservations?.Clear(); rewardBuffer?.Clear();
        owningBlock?.ClearResource(this);
        owningBlock = null;
        ActiveResourceLookup.Remove(this);
        RemoveFromActiveResourceList();
        released = true;
        sharedWorld.Remove(this);
    }
    private void DeactivateResource() => ReleaseRuntime();
    private void StartCoroutine(IEnumerator routine)
    {
        if (released || sharedWorld == null) return;
        HarvestRoutine pending = new HarvestRoutine();
        if (coroutines == null) coroutines = new List<HarvestRoutine>(1);
        pending.Coroutine = sharedWorld.StartCoroutine(RunHarvestRoutine(routine, pending));
        if (!pending.Complete && !released) coroutines.Add(pending);
    }
    private IEnumerator RunHarvestRoutine(IEnumerator routine, HarvestRoutine pending)
    {
        try { yield return routine; }
        finally { pending.Complete = true; coroutines.Remove(pending); }
    }
    public static IReadOnlyList<ResourceInstance> ActiveResources => ActiveResourcesInternal;

    public int CurrentGauge => released ? 0 : Mathf.Clamp(resourceStatus.currentGague, 0, MaxGauge);
    public int MaxGauge => released ? 1 : Mathf.Max(1, resourceStatus.maxGauge);
    public int ResourceCount => released ? 0 : Mathf.Max(0, resourceStatus.resourceCount);
    public int GetCount => released ? 1 : Mathf.Max(1, resourceStatus.getCount);
    public int RemainingHarvestOutputCount => released ? 0 : Mathf.Max(
        0,
        ResourceCount * GetHarvestOutputCountPerResource());
    public int RemainingMachineHarvestOutputCount => Mathf.Max(
        0,
        ResourceCount * GetCount);
    public bool CanHarvest => IsRuntimeActive && ResourceCount > 0 && HasHarvestableOutputAtCurrentState();
    public ResourceDefinition Definition => definition;
    public ResourceDefinition.PlacementCategory PlacementCategory => definition != null
        ? definition.placementCategory
        : ResourceDefinition.PlacementCategory.Ore;
    public bool AllowsAnimalTraversal
    {
        get
        {
            HarvestMode resolvedMode = ResolvedHarvestMode;
            return resolvedMode == HarvestMode.Mining
                   || resolvedMode == HarvestMode.Cut;
        }
    }

    public Block OwningBlock => owningBlock;
    public long SimulationId
    {
        get
        {
            Vector2Int coordinate = owningBlock != null
                ? owningBlock.Coordinate
                : new Vector2Int(
                    Mathf.RoundToInt(WorldPosition.x),
                    Mathf.RoundToInt(WorldPosition.z));
            return unchecked(((long)coordinate.x << 32) | (uint)coordinate.y);
        }
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
        if (!CanHarvest || harvestReservations == null || harvestReservations.Count <= 0)
        {
            return false;
        }

        int reservedGaugeCost = reservedHarvestGaugeCosts.Dequeue();
        reservedHarvestGaugeCount = Mathf.Max(0, reservedHarvestGaugeCount - reservedGaugeCost);
        ConsumeGaugeDots(reservedGaugeCost);

        if (released) return true;

        if (!CanHarvest)
        {
            ClearReservedHarvestSteps();
            accumulatedWork = 0f;
        }

        return true;
    }

    public bool CancelPreparedHarvestStep()
    {
        if (released || harvestReservations == null || harvestReservations.Count <= 0)
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
        if (released) return;
        accumulatedWork = 0f;
        ClearReservedHarvestSteps();
    }

    public ResourceSaveState CaptureState()
    {
        if (released) return default;
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
        if (released) return;
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
        ShowBodyPresentation();
        UpdateBodyScale();
    }

    public void InitializeRuntimeQuantity(int resourceCount)
    {
        if (released) return;
        resourceStatus.resourceCount = Mathf.Max(0, resourceCount);
        resourceStatus.maxGauge = Mathf.Max(1, resourceStatus.maxGauge);
        resourceStatus.currentGague = resourceStatus.resourceCount > 0 ? resourceStatus.maxGauge : 0;
        accumulatedWork = 0f;
        ClearReservedHarvestSteps();
        initialResourceCount = Mathf.Max(1, resourceStatus.resourceCount);
        ShowBodyPresentation();
        UpdateBodyScale();
    }

    public void ConfigureDynamicBodyScale(float minimumScaleRatio, float maximumScaleRatio, int maxResourceCountForScale)
    {
        if (released) return;
        useDynamicBodyScale = true;
        minimumBodyScaleRatio = Mathf.Clamp01(minimumScaleRatio);
        maximumBodyScaleRatio = Mathf.Max(minimumBodyScaleRatio, maximumScaleRatio);
        dynamicScaleMaxResourceCount = Mathf.Max(1, maxResourceCountForScale);
        UpdateBodyScale();
    }

    public void ConfigureFixedBodyScale()
    {
        if (released) return;
        useDynamicBodyScale = false;
        UpdateBodyScale();
    }


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
    { if (released) return; bodyYawStep = NormalizeBodyYawStep(yawStep); hasBodyYawStep = true; MarkBatchRenderDataDirty(); }

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
        if (!CanHarvest) { outputItemId = -1; outputCount = 0; return false; }
        outputItemId = ResolveOutputItemId();
        outputCount = GetCount;
        return outputItemId >= 0 && outputCount > 0;
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
            DeactivateResource();
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
                PersistDepletedResourceState();
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

    private void PersistDepletedResourceState()
    {
        // Deactivation clears the resource reference from its block. Persist the
        // zero-count tombstone first so deterministic terrain generation cannot
        // recreate this resource when the chunk or save is loaded again.
        TerrainGenerator terrain = sharedWorld.Terrain;
        if (terrain != null)
        {
            terrain.SaveRuntimeResourceState(this);
        }
    }

    private void PlayPickupSequence(int bagNum, int objectId, bool hideAfterSequence)
    {
        if (hideAfterSequence)
        {
            HideBodyPresentation();
        }

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
                DeactivateResource();
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
        if (!(this is ProjectF.MapObjects.TreeInstance))
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
        return this is ProjectF.MapObjects.TreeInstance tree
            ? tree.Growth
            : ResourceDefinition.MaxGrowth;
    }

    private int BuildHarvestDropSeed(int depletionOrdinal)
    {
        Vector2Int coordinate = owningBlock != null
            ? owningBlock.Coordinate
            : new Vector2Int(
                Mathf.RoundToInt(WorldPosition.x),
                Mathf.RoundToInt(WorldPosition.z));
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
                DeactivateResource();
            }

            yield break;
        }

        Player player = GameManager.Instance.Player;
        if (player == null)
        {
            if (hideAfterSequence)
            {
                DeactivateResource();
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
                            DeactivateResource();
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
            DeactivateResource();
        }

    }

    private Vector3 GetHarvestPortableStartWorldPosition() { return FocusPoint; }

    private PortableObject CreateHarvestPortableVisual(int objectId, Vector3 worldPosition)
    {
        PortableObject template = ResolveHarvestPortableTemplate();
        PortableObject visual;

        if (template != null)
        {
            visual = UnityEngine.Object.Instantiate(template);
            visual.name = $"{template.name}_HarvestTemp";
        }
        else
        {
            GameObject visualObject = new GameObject($"HarvestPortable_{objectId}");
            visualObject.layer = sharedWorld.gameObject.layer;
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

    private PortableObject ResolveHarvestPortableTemplate() { return sharedWorld.PortableTemplate; }

    private bool TryDropHarvestRewardToGround(Player player, int objectId, Vector3 startWorldPosition, bool hideAfterSequence)
    {
        TerrainGenerator generator = FindTerrainGenerator();
        Action onComplete = hideAfterSequence ? () => DeactivateResource() : null;

        if (generator != null)
        {
            Vector3 dropPosition = player != null ? player.transform.position : WorldPosition;
            if (generator.TryAddDroppedItemAnimated(dropPosition, objectId, startWorldPosition, out _, onComplete))
            {
                return true;
            }

            if (owningBlock != null && owningBlock.TryAddFloorObjectAnimated(objectId, startWorldPosition, 0f, out _, onComplete))
            {
                return true;
            }

            if (generator.TryAddDroppedItemAnimated(WorldPosition, objectId, startWorldPosition, out _, onComplete))
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
        return sharedWorld.Terrain;
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
        harvestReservations?.Clear();
        reservedHarvestGaugeCount = 0;
    }

    private int ResolveOutputItemId()
    {
        // Construction/load migration resolves the definition name once. Mining
        // peeks are hot and can use the stable runtime id directly afterwards.
        if (resourceStatus.outputId >= 0)
        {
            return resourceStatus.outputId;
        }

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

        return ResolveItemId();
    }

    private void RemoveFromActiveResourceList()
    {
        int removeIndex = activeResourceListIndex;
        if (removeIndex < 0
            || removeIndex >= ActiveResourcesInternal.Count
            || ActiveResourcesInternal[removeIndex] != this)
        {
            removeIndex = ActiveResourcesInternal.IndexOf(this);
        }

        if (removeIndex < 0)
        {
            activeResourceListIndex = -1;
            return;
        }

        int lastIndex = ActiveResourcesInternal.Count - 1;
        if (removeIndex != lastIndex)
        {
            ResourceInstance movedResource = ActiveResourcesInternal[lastIndex];
            ActiveResourcesInternal[removeIndex] = movedResource;
            if (movedResource != null)
            {
                movedResource.activeResourceListIndex = removeIndex;
            }
        }

        ActiveResourcesInternal.RemoveAt(lastIndex);
        activeResourceListIndex = -1;
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

    private static int NormalizeBodyYawStep(int yawStep)
    {
        int normalizedStep = yawStep % BodyYawStepCount;
        return normalizedStep < 0 ? normalizedStep + BodyYawStepCount : normalizedStep;
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
        sharedBodyScale = scaleRatio;
        MarkBatchRenderDataDirty();
    }

    private void ApplyDefinitionIfNeeded()
    {
        if (definition == null)
        {
            return;
        }

        

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
    public void SetOwningBlock(Block block)
    {
        if (released) return;
        if (owningBlock == block)
        {
            RegisterActiveResourceCoordinate();
            OnOwningBlockChanged(block);
            return;
        }

        UnregisterActiveResourceCoordinate();
        owningBlock = block;
        RegisterActiveResourceCoordinate();
        if (this is IMapObjectUpdateTick updateTick)
        {
            MapObjectTickManager.RefreshSimulationIdentity(updateTick);
        }

        OnOwningBlockChanged(block);
        MarkBatchRenderDataDirty();
    }

    protected virtual void OnOwningBlockChanged(Block block)
    {
    }

    public static bool TryCollectActiveResourcesInCoordinateRange(
        Vector2Int center,
        int radius,
        List<ResourceInstance> results)
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
                if (!ActiveResourcesByCoordinate.TryGetValue(coordinate, out List<ResourceInstance> resources)
                    || resources == null
                    || resources.Count <= 0)
                {
                    continue;
                }

                for (int i = resources.Count - 1; i >= 0; i--)
                {
                    ResourceInstance resource = resources[i];
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

    public static void EnsureActiveResourceCapacity(int requestedCapacity)
    {
        int capacity = Mathf.Max(ActiveResourcesInternal.Count, requestedCapacity);
        if (ActiveResourcesInternal.Capacity < capacity)
        {
            ActiveResourcesInternal.Capacity = capacity;
        }

        ActiveResourceLookup.EnsureCapacity(capacity);
        ActiveResourcesByCoordinate.EnsureCapacity(capacity);
    }

    private static bool IsActiveResourceCoordinateEntryValid(ResourceInstance resource, Vector2Int coordinate)
    {
        return resource != null
               && resource.activeResourceCoordinateRegistered
               && resource.activeResourceCoordinate == coordinate
               && resource.owningBlock != null
               && resource.owningBlock.Coordinate == coordinate
               && resource.IsRuntimeActive;
    }

    private static void RefreshStaleCoordinateEntry(ResourceInstance resource, Vector2Int coordinate)
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
        if (!ActiveResourcesByCoordinate.TryGetValue(coordinate, out List<ResourceInstance> resources)
            || resources == null)
        {
            resources = new List<ResourceInstance>();
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

        if (ActiveResourcesByCoordinate.TryGetValue(activeResourceCoordinate, out List<ResourceInstance> resources)
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
               && IsRuntimeActive
               && owningBlock != null;
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
        if (!IsRuntimeActive || entryIndex < 0 || entryIndex >= sharedWorld.PartCount)
        {
            mesh = null; materials = Array.Empty<Material>(); localToWorldMatrix = Matrix4x4.identity;
            worldPosition = Vector3.zero; layer = 0; shadowCastingMode = ShadowCastingMode.Off;
            receiveShadows = false; useGlobalBatch = false;
            return false;
        }
        useGlobalBatch = ResolvedHarvestMode == HarvestMode.Logging || ResolvedHarvestMode == HarvestMode.Cut;
        bool visible = sharedWorld.GetPart(this, entryIndex, out mesh, out materials, out localToWorldMatrix,
            out worldPosition, out layer, out shadowCastingMode, out receiveShadows);
        return visible && useBatchedRendering && SharedBodyVisible;
    }

    protected float MinimumBodyScaleRatio => Mathf.Clamp01(minimumBodyScaleRatio);
    protected float MaximumBodyScaleRatio => Mathf.Max(MinimumBodyScaleRatio, maximumBodyScaleRatio);
    private void HideBodyPresentation() { bodyPresentationVisible = false; MarkBatchRenderDataDirty(); }
    private void ShowBodyPresentation() { bodyPresentationVisible = true; }
    public void SetBatchedRendering(bool value)
    {
        if (released) return;
        useBatchedRendering = value; batchRenderer = sharedWorld.BatchRenderer;
        if (value) batchRenderer?.Register(this); else batchRenderer?.Unregister(this);
        sharedWorld.UpdateColliders(this);
    }
    public int BatchRenderEntryCount => sharedWorld.PartCount;
    protected void MarkBatchRenderDataDirty()
    {
        if (released) return;
        sharedBoundsDirty = true;
        sharedWorld.UpdateColliders(this);
        if (useBatchedRendering) batchRenderer?.MarkDirty(this);
    }
}
