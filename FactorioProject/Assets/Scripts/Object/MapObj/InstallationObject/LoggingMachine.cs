using System;
using System.Collections.Generic;
using UnityEngine;

public class LoggingMachine : InstallationObject,
    IMapObjectUpdateTick,
    IMapObjectUpdateTickInterval,
    IItemLightWorkStateProvider
{
    private static readonly Vector2Int[] LocalHarvestDirections =
    {
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.right
    };

    private static readonly int WorkAnimatorBoolHash = Animator.StringToHash("bWork");
    public const int DefaultMinimumGrowth = 10;
    private const float DefaultTickIntervalSeconds = 0.1f;
    private const float DirectionAngle = 90f;
    private const float HingeAlignmentTolerance = 0.1f;
    private const float EnergyEpsilon = 0.0001f;
    private const int NearbyAppleDropSearchRadius = 2;

    [Header("Logging")]
    [SerializeField]
    private Sprite harvestMarkerIcon;
    [SerializeField]
    private Transform hinge;
    [SerializeField, Min(1f)]
    private float hingeRotationDegreesPerSecond = 180f;
    [SerializeField, Min(0f)]
    private float emptyDirectionHoldSeconds = 0.25f;
    [SerializeField, Range(ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth)]
    private int minimumGrowth = DefaultMinimumGrowth;
    [SerializeField, HideInInspector]
    private bool treeFilterInitialized;
    [SerializeField, HideInInspector]
    private List<string> enabledTreeDefinitionKeys = new List<string>();

    private Resource activeTree;
    private Quaternion hingeBaseLocalRotation = Quaternion.identity;
    private Animator workAnimator;
    private bool hingeReferenceInitialized;
    private bool workAnimatorParameterChecked;
    private bool hasWorkAnimatorParameter;
    private bool hasElectricDemand;
    private bool isWorking;
    private int currentDirectionIndex;
    private float currentHingeAngle;
    private float emptyDirectionElapsed;
    private float consumedWorkEnergy;

    public float ManagedUpdateTickIntervalSeconds => DefaultTickIntervalSeconds;
    public bool IsWorkingForItemLight => isWorking;
    public Sprite HarvestMarkerIcon => harvestMarkerIcon;
    public static int HarvestDirectionCount => LocalHarvestDirections.Length;
    public int MinimumGrowth => Mathf.Clamp(
        minimumGrowth,
        ResourceDefinition.MinGrowth,
        ResourceDefinition.MaxGrowth);
    public bool IsTreeFilterInitialized => treeFilterInitialized;

    public bool IsTreeTypeEnabled(ResourceDefinition definition)
    {
        if (definition == null)
        {
            return false;
        }

        return !treeFilterInitialized
               || ContainsTreeDefinitionKey(BuildTreeDefinitionKey(definition));
    }

    public void SetTreeTypeEnabled(
        ResourceDefinition definition,
        IReadOnlyList<ResourceDefinition> availableDefinitions,
        bool enabled)
    {
        if (definition == null)
        {
            return;
        }

        EnsureTreeFilterInitialized(availableDefinitions);
        string key = BuildTreeDefinitionKey(definition);
        int existingIndex = IndexOfTreeDefinitionKey(key);
        if (enabled && existingIndex < 0)
        {
            enabledTreeDefinitionKeys.Add(key);
        }
        else if (!enabled && existingIndex >= 0)
        {
            enabledTreeDefinitionKeys.RemoveAt(existingIndex);
        }

        InvalidateFilteredTarget();
    }

    public void SetAllTreeTypes(
        IReadOnlyList<ResourceDefinition> availableDefinitions,
        bool enabled)
    {
        treeFilterInitialized = true;
        enabledTreeDefinitionKeys ??= new List<string>();
        enabledTreeDefinitionKeys.Clear();
        if (enabled && availableDefinitions != null)
        {
            for (int i = 0; i < availableDefinitions.Count; i++)
            {
                ResourceDefinition definition = availableDefinitions[i];
                if (definition == null)
                {
                    continue;
                }

                string key = BuildTreeDefinitionKey(definition);
                if (!ContainsTreeDefinitionKey(key))
                {
                    enabledTreeDefinitionKeys.Add(key);
                }
            }
        }

        InvalidateFilteredTarget();
    }

    public void SetMinimumGrowth(int value)
    {
        int clampedValue = Mathf.Clamp(
            value,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        if (minimumGrowth == clampedValue)
        {
            return;
        }

        minimumGrowth = clampedValue;
        InvalidateFilteredTarget();
    }

    public List<string> CaptureEnabledTreeDefinitionKeys()
    {
        return enabledTreeDefinitionKeys != null
            ? new List<string>(enabledTreeDefinitionKeys)
            : new List<string>();
    }

    public void ApplyTreeFilterState(
        bool initialized,
        IReadOnlyList<string> enabledDefinitionKeys,
        int savedMinimumGrowth)
    {
        treeFilterInitialized = initialized;
        minimumGrowth = Mathf.Clamp(
            savedMinimumGrowth,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        enabledTreeDefinitionKeys ??= new List<string>();
        enabledTreeDefinitionKeys.Clear();
        if (enabledDefinitionKeys != null)
        {
            for (int i = 0; i < enabledDefinitionKeys.Count; i++)
            {
                string key = enabledDefinitionKeys[i];
                if (!string.IsNullOrEmpty(key) && !ContainsTreeDefinitionKey(key))
                {
                    enabledTreeDefinitionKeys.Add(key);
                }
            }
        }

        InvalidateFilteredTarget();
    }

    public static Vector2Int GetHarvestCoordinate(
        Vector2Int anchorCoordinate,
        int quarterTurns,
        int directionIndex)
    {
        Vector2Int localDirection = LocalHarvestDirections[NormalizeDirectionIndex(directionIndex)];
        Vector2Int worldDirection = InputOutputModule.RotateRectGridOffset(
            localDirection,
            quarterTurns);
        return anchorCoordinate + worldDirection;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResolveHingeReference();
        ResetRuntimeState(true);

        if (Application.isPlaying && TryGetPlacementRuntime(out _, out _))
        {
            MapObjectTickManager.RegisterUpdateTick(this);
        }
    }

    protected override void OnDisable()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        SetWorking(false);
        activeTree = null;
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        SetWorking(false);
        ResetRuntimeState(true);
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        ResetRuntimeState(true);
        if (Application.isPlaying && isActiveAndEnabled)
        {
            MapObjectTickManager.RegisterUpdateTick(this);
        }
    }

    protected override void OnPlacementRuntimeCleared()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        SetWorking(false);
        ResetRuntimeState(true);
        base.OnPlacementRuntimeCleared();
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        if (!Application.isPlaying
            || deltaTime <= 0f
            || !isActiveAndEnabled
            || !TryGetPlacementRuntime(out _, out _))
        {
            SetWorking(false);
            return;
        }

        hasElectricDemand = activeTree != null || HasAnyAdjacentTree();
        if (!hasElectricDemand)
        {
            SetWorking(false);
            return;
        }

        if (!UtilityPole.HasElectricityAvailable(this))
        {
            SetWorking(false);
            return;
        }

        UpdateHingeRotation(deltaTime);
        if (!IsHingeAligned())
        {
            SetWorking(false);
            return;
        }

        if (!TryResolveAdjacentTree(currentDirectionIndex, out Resource tree))
        {
            activeTree = null;
            consumedWorkEnergy = 0f;
            SetWorking(false);
            UpdateEmptyDirection(deltaTime);
            return;
        }

        emptyDirectionElapsed = 0f;
        if (activeTree != tree)
        {
            activeTree = tree;
            consumedWorkEnergy = 0f;
        }

        if (!TryConsumeWorkEnergy(deltaTime, out float consumedEnergy))
        {
            SetWorking(false);
            return;
        }

        consumedWorkEnergy += consumedEnergy;
        SetWorking(true);
        if (consumedWorkEnergy + EnergyEpsilon < ResolveRequiredWorkEnergy())
        {
            return;
        }

        CompleteTreeHarvest(tree);
    }

    public bool TryGetElectricPowerRequirement(out float wattsPerSecond)
    {
        wattsPerSecond = ItemDefinition.ResolveElectricUseWatts(ResolveLoggingDefinition());
        return wattsPerSecond > EnergyEpsilon;
    }

    public bool TryGetElectricPowerDemand(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        if (!isActiveAndEnabled
            || !TryGetPlacementRuntime(out _, out _)
            || !hasElectricDemand)
        {
            return false;
        }

        return TryGetElectricPowerRequirement(out wattsPerSecond);
    }

    public void GetObjectInfoStatus(out string statusText, out bool working)
    {
        GetObjectInfoStatus(out statusText, out working, out _);
    }

    public void GetObjectInfoStatus(out string statusText, out bool working, out bool warning)
    {
        working = isWorking;
        warning = false;
        if (!TryGetPlacementRuntime(out _, out _))
        {
            statusText = "No placement";
            return;
        }

        if (!HasAnyAdjacentTree())
        {
            statusText = "No tree";
            warning = true;
            return;
        }

        statusText = UtilityPole.HasElectricityAvailable(this)
            ? "Working"
            : "No energy";
    }

    private bool TryConsumeWorkEnergy(float deltaTime, out float consumedEnergy)
    {
        consumedEnergy = 0f;
        if (!TryGetElectricPowerRequirement(out float wattsPerSecond))
        {
            return false;
        }

        float requestedEnergy = wattsPerSecond * deltaTime;
        return UtilityPole.TryConsumeElectricity(
            this,
            requestedEnergy,
            deltaTime,
            out consumedEnergy);
    }

    private float ResolveRequiredWorkEnergy()
    {
        ItemDefinition definition = ResolveLoggingDefinition();
        if (definition == null)
        {
            return 1f;
        }

        float configuredEnergy = ItemDefinition.ResolveCompleteEnergyAmount(definition);
        if (configuredEnergy > EnergyEpsilon)
        {
            return configuredEnergy;
        }

        float wattsPerSecond = ItemDefinition.ResolveElectricUseWatts(definition);
        return Mathf.Max(1f, wattsPerSecond * Mathf.Max(0.1f, definition.CraftingDurationSeconds));
    }

    private ItemDefinition ResolveLoggingDefinition()
    {
        return BoundItemDefinition != null
            ? BoundItemDefinition
            : InputOutputModule.ResolveItemDefinition(ResolveItemId());
    }

    private void CompleteTreeHarvest(Resource tree)
    {
        SetWorking(false);
        activeTree = null;
        consumedWorkEnergy = 0f;

        if (tree != null)
        {
            Block treeBlock = tree.OwningBlock;
            Vector3 startWorldPosition = tree.FocusPoint;
            Vector3 dropWorldPosition = tree.transform.position;
            int appleItemId = -1;
            int appleCount = 0;
            bool hasAppleDrop = tree is ProjectF.MapObjects.Tree harvestedTree
                                && harvestedTree.TryGetMachineAppleDrop(
                                    out appleItemId,
                                    out appleCount);

            if (tree.TryHarvestForMachine(out int outputItemId, out int outputCount)
                && outputItemId >= 0
                && outputCount > 0)
            {
                DropHarvestedItems(
                    treeBlock,
                    startWorldPosition,
                    dropWorldPosition,
                    outputItemId,
                    outputCount);

                if (hasAppleDrop)
                {
                    DropApplesIntoNearbyEmptyBlocks(
                        treeBlock,
                        startWorldPosition,
                        appleItemId,
                        appleCount);
                }
            }
        }

        AdvanceDirection();
    }

    private static void DropHarvestedItems(
        Block treeBlock,
        Vector3 startWorldPosition,
        Vector3 dropWorldPosition,
        int itemId,
        int count)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();

        for (int i = 0; i < count; i++)
        {
            if (treeBlock != null
                && treeBlock.TryAddFloorObjectAnimated(
                    itemId,
                    startWorldPosition,
                    0f,
                    out _))
            {
                continue;
            }

            if (terrain != null
                && terrain.TryAddDroppedItemAnimated(
                    dropWorldPosition,
                    itemId,
                    startWorldPosition,
                    out _))
            {
                continue;
            }

            terrain?.TryAddDroppedItemNear(dropWorldPosition, itemId, out _);
        }
    }

    private static void DropApplesIntoNearbyEmptyBlocks(
        Block treeBlock,
        Vector3 startWorldPosition,
        int itemId,
        int count)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null || treeBlock == null || itemId < 0 || count <= 0)
        {
            return;
        }

        if (TryResolveNearbyAppleBox(
                terrain,
                treeBlock.Coordinate,
                itemId,
                count,
                out BoxObject targetBox))
        {
            for (int itemIndex = 0; itemIndex < count; itemIndex++)
            {
                if (!targetBox.TryPutOneContainedObject(
                        itemId,
                        startWorldPosition,
                        0f,
                        out _))
                {
                    return;
                }
            }

            return;
        }

        if (!TryResolveNearbyEmptyDropBlock(
                terrain,
                treeBlock.Coordinate,
                itemId,
                count,
                out Block targetBlock))
        {
            return;
        }

        for (int itemIndex = 0; itemIndex < count; itemIndex++)
        {
            if (!targetBlock.TryAddFloorObjectAnimated(
                    itemId,
                    startWorldPosition,
                    0f,
                    out _))
            {
                return;
            }
        }
    }

    private static bool TryResolveNearbyAppleBox(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        return TryResolveCardinalBox(
                   terrain,
                   centerCoordinate,
                   1,
                   itemId,
                   itemCount,
                   out targetBox)
               || TryResolveNonCardinalBoxRing(
                   terrain,
                   centerCoordinate,
                   1,
                   itemId,
                   itemCount,
                   out targetBox)
               || TryResolveCardinalBox(
                   terrain,
                   centerCoordinate,
                   NearbyAppleDropSearchRadius,
                   itemId,
                   itemCount,
                   out targetBox)
               || TryResolveNonCardinalBoxRing(
                   terrain,
                   centerCoordinate,
                   NearbyAppleDropSearchRadius,
                   itemId,
                   itemCount,
                   out targetBox);
    }

    private static bool TryResolveCardinalBox(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        for (int directionIndex = 0;
             directionIndex < LocalHarvestDirections.Length;
             directionIndex++)
        {
            Vector2Int coordinate = centerCoordinate
                                    + LocalHarvestDirections[directionIndex] * radius;
            if (TryResolveBoxCandidate(
                    terrain,
                    coordinate,
                    itemId,
                    itemCount,
                    out targetBox))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveNonCardinalBoxRing(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if ((Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    || offsetX == 0
                    || offsetY == 0)
                {
                    continue;
                }

                Vector2Int coordinate = centerCoordinate
                                        + new Vector2Int(offsetX, offsetY);
                if (TryResolveBoxCandidate(
                        terrain,
                        coordinate,
                        itemId,
                        itemCount,
                        out targetBox))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveBoxCandidate(
        TerrainGenerator terrain,
        Vector2Int coordinate,
        int itemId,
        int itemCount,
        out BoxObject targetBox)
    {
        targetBox = null;
        if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || block.MapObject == null)
        {
            return false;
        }

        targetBox = block.MapObject as BoxObject;
        if (targetBox == null)
        {
            block.MapObject.TryGetComponent(out targetBox);
        }

        if (targetBox != null
            && targetBox.CanPutContainedObjects(itemId, itemCount))
        {
            return true;
        }

        targetBox = null;
        return false;
    }

    private static bool TryResolveNearbyEmptyDropBlock(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;

        if (TryResolveCardinalDropBlock(
                terrain,
                centerCoordinate,
                1,
                itemId,
                itemCount,
                out targetBlock)
            || TryResolveNonCardinalDropRing(
                terrain,
                centerCoordinate,
                1,
                itemId,
                itemCount,
                out targetBlock)
            || TryResolveCardinalDropBlock(
                terrain,
                centerCoordinate,
                NearbyAppleDropSearchRadius,
                itemId,
                itemCount,
                out targetBlock)
            || TryResolveNonCardinalDropRing(
                terrain,
                centerCoordinate,
                NearbyAppleDropSearchRadius,
                itemId,
                itemCount,
                out targetBlock))
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveCardinalDropBlock(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        for (int directionIndex = 0;
             directionIndex < LocalHarvestDirections.Length;
             directionIndex++)
        {
            Vector2Int coordinate = centerCoordinate
                                    + LocalHarvestDirections[directionIndex] * radius;
            if (TryResolveEmptyDropCandidate(
                    terrain,
                    coordinate,
                    itemId,
                    itemCount,
                    out targetBlock))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveNonCardinalDropRing(
        TerrainGenerator terrain,
        Vector2Int centerCoordinate,
        int radius,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if ((Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                    || offsetX == 0
                    || offsetY == 0)
                {
                    continue;
                }

                Vector2Int coordinate = centerCoordinate
                                        + new Vector2Int(offsetX, offsetY);
                if (TryResolveEmptyDropCandidate(
                        terrain,
                        coordinate,
                        itemId,
                        itemCount,
                        out targetBlock))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveEmptyDropCandidate(
        TerrainGenerator terrain,
        Vector2Int coordinate,
        int itemId,
        int itemCount,
        out Block targetBlock)
    {
        targetBlock = null;
        if (!terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || block.Type != Block.BlockType.Ground
            || block.MapObject != null
            || block.HasDroppedFloorObjects
            || !block.SupportsFloorObjectDrops
            || !block.CanAddFloorObjects(itemCount, itemId))
        {
            return false;
        }

        targetBlock = block;
        return true;
    }

    private void UpdateEmptyDirection(float deltaTime)
    {
        emptyDirectionElapsed += deltaTime;
        if (emptyDirectionElapsed >= Mathf.Max(0f, emptyDirectionHoldSeconds))
        {
            AdvanceDirection();
        }
    }

    private void AdvanceDirection()
    {
        currentDirectionIndex = (currentDirectionIndex + 1) % LocalHarvestDirections.Length;
        emptyDirectionElapsed = 0f;
    }

    private bool HasAnyAdjacentTree()
    {
        for (int directionIndex = 0; directionIndex < LocalHarvestDirections.Length; directionIndex++)
        {
            if (TryResolveAdjacentTree(directionIndex, out _))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveAdjacentTree(int directionIndex, out Resource tree)
    {
        tree = null;
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        if (terrain == null)
        {
            return false;
        }

        Vector2Int targetCoordinate = GetHarvestCoordinate(
            RuntimeAnchorCoordinate,
            RuntimeQuarterTurns,
            directionIndex);
        if (!terrain.TryGetLoadedBlock(targetCoordinate, out Block block) || block == null)
        {
            return false;
        }

        tree = block.Resource;
        return IsHarvestableTree(tree);
    }

    private bool IsHarvestableTree(Resource resource)
    {
        if (resource == null
            || resource.ResolvedHarvestMode != Resource.HarvestMode.Logging
            || !resource.CanHarvest
            || !resource.gameObject.activeInHierarchy
            || !IsTreeTypeEnabled(resource.Definition))
        {
            return false;
        }

        float growth = resource is ProjectF.MapObjects.Tree tree
            ? tree.Growth
            : ResourceDefinition.MaxGrowth;
        return growth >= MinimumGrowth;
    }

    private void EnsureTreeFilterInitialized(IReadOnlyList<ResourceDefinition> availableDefinitions)
    {
        if (treeFilterInitialized)
        {
            enabledTreeDefinitionKeys ??= new List<string>();
            return;
        }

        treeFilterInitialized = true;
        enabledTreeDefinitionKeys ??= new List<string>();
        enabledTreeDefinitionKeys.Clear();
        if (availableDefinitions == null)
        {
            return;
        }

        for (int i = 0; i < availableDefinitions.Count; i++)
        {
            ResourceDefinition definition = availableDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            string key = BuildTreeDefinitionKey(definition);
            if (!ContainsTreeDefinitionKey(key))
            {
                enabledTreeDefinitionKeys.Add(key);
            }
        }
    }

    private void InvalidateFilteredTarget()
    {
        activeTree = null;
        consumedWorkEnergy = 0f;
        hasElectricDemand = HasAnyAdjacentTree();
        if (!hasElectricDemand)
        {
            SetWorking(false);
        }
    }

    private bool ContainsTreeDefinitionKey(string key)
    {
        return IndexOfTreeDefinitionKey(key) >= 0;
    }

    private int IndexOfTreeDefinitionKey(string key)
    {
        if (string.IsNullOrEmpty(key) || enabledTreeDefinitionKeys == null)
        {
            return -1;
        }

        for (int i = 0; i < enabledTreeDefinitionKeys.Count; i++)
        {
            if (string.Equals(enabledTreeDefinitionKeys[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string BuildTreeDefinitionKey(ResourceDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        return !string.IsNullOrEmpty(definition.name)
            ? definition.name
            : (definition.resourceName ?? string.Empty);
    }

    private void UpdateHingeRotation(float deltaTime)
    {
        ResolveHingeReference();
        float targetAngle = currentDirectionIndex * DirectionAngle;
        currentHingeAngle = Mathf.MoveTowardsAngle(
            currentHingeAngle,
            targetAngle,
            Mathf.Max(1f, hingeRotationDegreesPerSecond) * deltaTime);
        ApplyHingeRotation();
    }

    private bool IsHingeAligned()
    {
        float targetAngle = currentDirectionIndex * DirectionAngle;
        return Mathf.Abs(Mathf.DeltaAngle(currentHingeAngle, targetAngle)) <= HingeAlignmentTolerance;
    }

    private void ResetRuntimeState(bool applyRotation)
    {
        activeTree = null;
        hasElectricDemand = false;
        currentDirectionIndex = 0;
        currentHingeAngle = 0f;
        emptyDirectionElapsed = 0f;
        consumedWorkEnergy = 0f;
        SetWorking(false);

        if (applyRotation)
        {
            ResolveHingeReference();
            ApplyHingeRotation();
        }
    }

    private void ResolveHingeReference()
    {
        if (hinge == null)
        {
            hinge = transform.Find("Body/Floor/Hinge");
        }

        if (hinge == null || hingeReferenceInitialized)
        {
            return;
        }

        hingeBaseLocalRotation = hinge.localRotation;
        hingeReferenceInitialized = true;
    }

    private void ApplyHingeRotation()
    {
        if (hinge != null)
        {
            hinge.localRotation = hingeBaseLocalRotation * Quaternion.Euler(0f, currentHingeAngle, 0f);
        }
    }

    private void SetWorking(bool working)
    {
        isWorking = working;
        Animator animator = ResolveWorkAnimator();
        if (animator == null || !hasWorkAnimatorParameter)
        {
            return;
        }

        animator.SetBool(WorkAnimatorBoolHash, working);
    }

    private Animator ResolveWorkAnimator()
    {
        if (workAnimator == null)
        {
            workAnimator = ResolveInstallationAnimator();
            workAnimatorParameterChecked = false;
            hasWorkAnimatorParameter = false;
        }

        if (workAnimator == null || workAnimatorParameterChecked)
        {
            return workAnimator;
        }

        workAnimatorParameterChecked = true;
        AnimatorControllerParameter[] parameters = workAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == WorkAnimatorBoolHash
                && parameters[i].type == AnimatorControllerParameterType.Bool)
            {
                hasWorkAnimatorParameter = true;
                break;
            }
        }

        return workAnimator;
    }

    private static int NormalizeDirectionIndex(int directionIndex)
    {
        int normalized = directionIndex % LocalHarvestDirections.Length;
        return normalized < 0 ? normalized + LocalHarvestDirections.Length : normalized;
    }

#if UNITY_EDITOR
    private const string HarvestMarkerIconAssetPath = "Assets/Image/UI/Item/Saw.png";

    protected override void OnValidate()
    {
        base.OnValidate();
        hingeRotationDegreesPerSecond = Mathf.Max(1f, hingeRotationDegreesPerSecond);
        emptyDirectionHoldSeconds = Mathf.Max(0f, emptyDirectionHoldSeconds);
        minimumGrowth = Mathf.Clamp(
            minimumGrowth,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        if (hinge == null)
        {
            hinge = transform.Find("Body/Floor/Hinge");
        }

        if (harvestMarkerIcon == null)
        {
            harvestMarkerIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                HarvestMarkerIconAssetPath);
        }
    }
#endif
}
