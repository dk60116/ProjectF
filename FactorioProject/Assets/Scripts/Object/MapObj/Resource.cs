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
    public enum HarvestMode
    {
        Auto,
        Mining,
        Logging
    }

    [Serializable]
    public struct ResourceStatus
    {
        [HideInInspector]
        public int outputId;
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
    }

    private static readonly List<Resource> ActiveResourcesInternal = new List<Resource>();

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
    private float minimumBodyScaleRatio = 0.3f;

    [SerializeField, Min(0.01f)]
    private float maximumBodyScaleRatio = 1f;

    [SerializeField, Min(1)]
    private int dynamicScaleMaxResourceCount = 1;

    private float accumulatedWork;
    private readonly Queue<int> reservedHarvestGaugeCosts = new Queue<int>();
    private int reservedHarvestGaugeCount;
    private Renderer cachedRenderer;
    private Transform bodyTransform;
    private Vector3 initialBodyLocalScale = Vector3.one;
    private int initialResourceCount;
    private Block owningBlock;
    private ResourceBatchRenderer batchRenderer;
    private MeshFilter batchedMeshFilter;
    private MeshRenderer batchedMeshRenderer;
    private bool batchComponentsResolved;
    private bool supportsBatchedRendering;
    private bool useBatchedRendering;
    private bool bodyPresentationVisible = true;

    public static IReadOnlyList<Resource> ActiveResources => ActiveResourcesInternal;

    public int CurrentGauge => Mathf.Clamp(resourceStatus.currentGague, 0, MaxGauge);
    public int MaxGauge => Mathf.Max(1, resourceStatus.maxGauge);
    public int ResourceCount => Mathf.Max(0, resourceStatus.resourceCount);
    public int GetCount => Mathf.Max(1, resourceStatus.getCount);
    public bool CanHarvest => ResourceCount > 0;

    public HarvestMode ResolvedHarvestMode
    {
        get
        {
            if (harvestMode != HarvestMode.Auto)
            {
                return harvestMode;
            }

            string sourceName = $"{objectName} {gameObject.name}";
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

    private void OnEnable()
    {
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
    }

    private void OnDisable()
    {
        SetBatchedRendering(false);
        ActiveResourcesInternal.Remove(this);

        if (!Application.isPlaying || owningBlock == null || owningBlock.MapObject != this)
        {
            return;
        }

        owningBlock.SetMapObject(null);
    }

    private void OnValidate()
    {
        CacheBodyTransform();
        ApplyDefinitionIfNeeded();
        EnsureStatusInitialized();
        MigrateOutputItemNameIfNeeded();
        batchComponentsResolved = false;
        supportsBatchedRendering = false;
        batchedMeshFilter = null;
        batchedMeshRenderer = null;
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
        return new ResourceSaveState
        {
            resourceCount = ResourceCount,
            maxGauge = MaxGauge,
            currentGauge = CurrentGauge,
            initialResourceCount = Mathf.Max(1, initialResourceCount)
        };
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
        minimumBodyScaleRatio = Mathf.Clamp01(minimumScaleRatio);
        maximumBodyScaleRatio = Mathf.Max(minimumBodyScaleRatio, maximumScaleRatio);
        dynamicScaleMaxResourceCount = Mathf.Max(1, maxResourceCountForScale);
        UpdateBodyScale();
    }

    private void ConsumeGaugeDots(int gaugeAmount)
    {
        if (!CanHarvest || gaugeAmount <= 0)
        {
            return;
        }

        int remainingGaugeAmount = gaugeAmount;
        int depletedResourceCount = 0;
        bool resourceFullyDepleted = false;

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

        if (depletedResourceCount <= 0)
        {
            return;
        }

        UpdateBodyScale();

        int outputItemId = ResolveOutputItemId();
        for (int i = 0; i < depletedResourceCount; i++)
        {
            bool hideAfterSequence = resourceFullyDepleted && i == depletedResourceCount - 1;
            PlayPickupSequence(0, outputItemId, hideAfterSequence);
        }
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

            if (TryResolveHarvestBagTarget(player, objectId, out PortableObject targetPortableObject) && targetPortableObject != null)
            {
                spawnedCount++;
                PortableObject harvestPortableVisual = CreateHarvestPortableVisual(objectId, sourceWorldPosition);
                if (harvestPortableVisual == null)
                {
                    if (shouldHideOnComplete)
                    {
                        gameObject.SetActive(false);
                    }
                }
                else
                {
                    harvestPortableVisual.MoveTo(targetPortableObject.transform, () =>
                    {
                        ReleaseHarvestPortableVisual(harvestPortableVisual);
                        if (shouldHideOnComplete)
                        {
                            gameObject.SetActive(false);
                        }
                    });
                }
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
            ReleaseHarvestPortableVisual(visual);
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

    private void ReleaseHarvestPortableVisual(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(portableObject.gameObject);
        }
        else
        {
            DestroyImmediate(portableObject.gameObject);
        }
    }

    private bool TryResolveHarvestBagTarget(Player player, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (player != null && player.TryAddToBag(objectId, out targetPortableObject))
        {
            return targetPortableObject != null;
        }

        return false;
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
        return FindObjectOfType<TerrainGenerator>();
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
        if (TryResolveOutputItemNameToId(resourceStatus.outputItemName, out int outputItemId))
        {
            return outputItemId;
        }

        if (resourceStatus.outputId >= 0)
        {
            return resourceStatus.outputId;
        }

        return ResolveItemId();
    }

    private void MigrateOutputItemNameIfNeeded()
    {
        if (!string.IsNullOrWhiteSpace(resourceStatus.outputItemName))
        {
            resourceStatus.outputItemName = resourceStatus.outputItemName.Trim();
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
        else
        {
            float normalizedCount = Mathf.Clamp01((float)ResourceCount / Mathf.Max(1, dynamicScaleMaxResourceCount));
            scaleRatio = Mathf.Lerp(minimumBodyScaleRatio, maximumBodyScaleRatio, normalizedCount);
        }

        bodyTransform.localScale = initialBodyLocalScale * scaleRatio;
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

        if (resourceStatus.getCount <= 0)
        {
            resourceStatus.getCount = Mathf.Max(1, definition.defaultGetCount);
        }

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

        Renderer[] renderers = bodyTransform.GetComponentsInChildren<Renderer>(true);
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
                meshRenderer.forceRenderingOff = bodyPresentationVisible && useBatchedRendering && supportsBatchedRendering;
            }
        }

        Collider[] colliders = bodyTransform.GetComponentsInChildren<Collider>(true);
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

    public void SetOwningBlock(Block block)
    {
        owningBlock = block;
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

    public bool TryGetBatchRenderData(
        out Mesh mesh,
        out Material material,
        out Matrix4x4 localToWorldMatrix,
        out Vector3 worldPosition,
        out int layer,
        out ShadowCastingMode shadowCastingMode,
        out bool receiveShadows)
    {
        ResolveBatchComponents();

        mesh = batchedMeshFilter != null ? batchedMeshFilter.sharedMesh : null;
        material = batchedMeshRenderer != null ? batchedMeshRenderer.sharedMaterial : null;
        localToWorldMatrix = batchedMeshFilter != null ? batchedMeshFilter.transform.localToWorldMatrix : Matrix4x4.identity;
        worldPosition = batchedMeshFilter != null ? batchedMeshFilter.transform.position : transform.position;
        layer = batchedMeshRenderer != null ? batchedMeshRenderer.gameObject.layer : gameObject.layer;
        shadowCastingMode = batchedMeshRenderer != null ? batchedMeshRenderer.shadowCastingMode : ShadowCastingMode.Off;
        receiveShadows = batchedMeshRenderer != null && batchedMeshRenderer.receiveShadows;

        return useBatchedRendering
               && bodyPresentationVisible
               && gameObject.activeInHierarchy
               && supportsBatchedRendering
               && mesh != null
               && material != null;
    }

    private void ResolveBatchComponents()
    {
        if (batchComponentsResolved)
        {
            return;
        }

        batchComponentsResolved = true;
        supportsBatchedRendering = false;
        batchedMeshFilter = null;
        batchedMeshRenderer = null;

        CacheBodyTransform();
        CachePortableObjects();
        if (bodyTransform == null)
        {
            return;
        }

        List<MeshFilter> candidates = new List<MeshFilter>();
        MeshFilter[] meshFilters = bodyTransform.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter candidate = meshFilters[i];
            if (candidate == null || IsPortableHierarchy(candidate.transform))
            {
                continue;
            }

            MeshRenderer candidateRenderer = candidate.GetComponent<MeshRenderer>();
            if (candidateRenderer == null || IsPortableHierarchy(candidateRenderer.transform))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count != 1)
        {
            return;
        }

        batchedMeshFilter = candidates[0];
        batchedMeshRenderer = batchedMeshFilter != null ? batchedMeshFilter.GetComponent<MeshRenderer>() : null;
        if (batchedMeshFilter == null || batchedMeshRenderer == null)
        {
            batchedMeshFilter = null;
            batchedMeshRenderer = null;
            return;
        }

        if (batchedMeshRenderer.sharedMaterial != null && !batchedMeshRenderer.sharedMaterial.enableInstancing)
        {
            batchedMeshRenderer.sharedMaterial.enableInstancing = true;
        }

        cachedRenderer ??= batchedMeshRenderer;
        supportsBatchedRendering = true;
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

    public void Register(Resource resource)
    {
        if (resource == null)
        {
            return;
        }

        registeredResources.Add(resource);
    }

    public void Unregister(Resource resource)
    {
        if (resource == null)
        {
            return;
        }

        registeredResources.Remove(resource);
    }

    private void LateUpdate()
    {
        if (registeredResources.Count <= 0)
        {
            return;
        }

        RebuildBatches();
        RenderBatches();
    }

    private void RebuildBatches()
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

        foreach (Resource resource in registeredResources)
        {
            if (resource == null)
            {
                cleanupBuffer.Add(resource);
                continue;
            }

            if (!resource.TryGetBatchRenderData(
                    out Mesh mesh,
                    out Material material,
                    out Matrix4x4 localToWorldMatrix,
                    out Vector3 worldPosition,
                    out int layer,
                    out ShadowCastingMode shadowCastingMode,
                    out bool receiveShadows))
            {
                continue;
            }

            if (material != null && !material.enableInstancing)
            {
                material.enableInstancing = true;
            }

            int cellX = Mathf.FloorToInt(worldPosition.x / batchCellSize);
            int cellZ = Mathf.FloorToInt(worldPosition.z / batchCellSize);
            BatchKey key = new BatchKey(mesh, material, layer, shadowCastingMode, receiveShadows, cellX, cellZ);
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

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            registeredResources.Remove(cleanupBuffer[i]);
        }
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
                Graphics.RenderMeshInstanced(renderParams, key.Mesh, 0, matrices, drawCount, startIndex);
                startIndex += drawCount;
                remaining -= drawCount;
            }
        }
    }

    private readonly struct BatchKey
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly int Layer;
        public readonly ShadowCastingMode ShadowCastingMode;
        public readonly bool ReceiveShadows;
        public readonly int CellX;
        public readonly int CellZ;

        public BatchKey(
            Mesh mesh,
            Material material,
            int layer,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows,
            int cellX,
            int cellZ)
        {
            Mesh = mesh;
            Material = material;
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
                   && Layer == other.Layer
                   && ShadowCastingMode == other.ShadowCastingMode
                   && ReceiveShadows == other.ReceiveShadows
                   && CellX == other.CellX
                   && CellZ == other.CellZ;
        }
    }
}
