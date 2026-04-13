using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        public int outputId;
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
    private int reservedHarvestSteps;
    private Renderer cachedRenderer;
    private Transform bodyTransform;
    private Vector3 initialBodyLocalScale = Vector3.one;
    private int initialResourceCount;
    private Block owningBlock;

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
        CaptureInitialStateIfNeeded();
        EnsurePortableObjectPool(GetCount);
        ShowBodyPresentation();

        if (!Application.isPlaying)
        {
            ApplyEditorBodyScale();
            return;
        }

        UpdateBodyScale();

        if (!ActiveResourcesInternal.Contains(this))
        {
            ActiveResourcesInternal.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveResourcesInternal.Remove(this);
    }

    private void OnValidate()
    {
        CacheBodyTransform();
        ApplyDefinitionIfNeeded();
        EnsureStatusInitialized();
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

    public int PrepareHarvestSteps(float workAmount)
    {
        if (!CanHarvest || workAmount <= 0f)
        {
            return 0;
        }

        accumulatedWork += workAmount;
        float stepThreshold = Mathf.Max(0.01f, workPerGaugeDot);
        int preparedStepCount = 0;

        while (accumulatedWork >= stepThreshold && GetReservableHarvestStepCount() > 0)
        {
            accumulatedWork -= stepThreshold;
            reservedHarvestSteps++;
            preparedStepCount++;
        }

        return preparedStepCount;
    }

    public bool CommitPreparedHarvestStep()
    {
        if (reservedHarvestSteps <= 0 || !CanHarvest)
        {
            return false;
        }

        reservedHarvestSteps = Mathf.Max(0, reservedHarvestSteps - 1);
        ConsumeGaugeDot();

        if (!CanHarvest)
        {
            reservedHarvestSteps = 0;
            accumulatedWork = 0f;
        }

        return true;
    }

    public bool CancelPreparedHarvestStep()
    {
        if (reservedHarvestSteps <= 0)
        {
            return false;
        }

        reservedHarvestSteps = Mathf.Max(0, reservedHarvestSteps - 1);
        accumulatedWork += Mathf.Max(0.01f, workPerGaugeDot);
        return true;
    }

    public void ResetWork()
    {
        accumulatedWork = 0f;
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
        reservedHarvestSteps = 0;
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
        reservedHarvestSteps = 0;
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

    private void ConsumeGaugeDot()
    {
        if (!CanHarvest)
        {
            return;
        }

        resourceStatus.currentGague = Mathf.Max(0, resourceStatus.currentGague - 1);

        if (resourceStatus.currentGague > 0)
        {
            return;
        }

        resourceStatus.resourceCount = Mathf.Max(0, resourceStatus.resourceCount - 1);
        accumulatedWork = 0f;

        if (resourceStatus.resourceCount <= 0)
        {
            resourceStatus.currentGague = 0;
            reservedHarvestSteps = 0;
            PlayPickupSequence(0, ResolveItemId(), true);
            return;
        }

        resourceStatus.currentGague = MaxGauge;
        UpdateBodyScale();

        PlayPickupSequence(0, ResolveItemId(), false);
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
        List<PortableObject> reservedPortableObjects = ReservePortableObjectInstances(rewardCount);

        for (int i = 0; i < rewardCount; i++)
        {
            PortableObject sourcePortableObject = i < reservedPortableObjects.Count ? reservedPortableObjects[i] : null;
            if (sourcePortableObject == null)
            {
                break;
            }

            PortableObject targetPortableObject = null;
            bool hasTarget = player.TryAddToBag(objectId, out targetPortableObject);

            if (!hasTarget && owningBlock != null)
            {
                hasTarget = owningBlock.TryAddFloorObject(objectId, out targetPortableObject);
            }

            if (!hasTarget || targetPortableObject == null)
            {
                break;
            }

            spawnedCount++;
            bool shouldHideOnComplete = hideAfterSequence && i == rewardCount - 1;
            sourcePortableObject.transform.SetParent(transform, false);
            sourcePortableObject.gameObject.SetActive(true);
            sourcePortableObject.transform.localScale = Vector3.one;
            sourcePortableObject.transform.localPosition = Vector3.zero;
            sourcePortableObject.transform.localRotation = Quaternion.identity;
            sourcePortableObject.transform.SetParent(null, true);
            sourcePortableObject.MoveTo(targetPortableObject.transform, () =>
            {
                sourcePortableObject.transform.SetParent(transform, false);
                sourcePortableObject.transform.localPosition = Vector3.zero;
                sourcePortableObject.transform.localRotation = Quaternion.identity;
                sourcePortableObject.transform.localScale = Vector3.one;

                if (shouldHideOnComplete)
                {
                    gameObject.SetActive(false);
                }
            });

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

    private int GetReservableHarvestStepCount()
    {
        int totalRemainingSteps = ((Mathf.Max(0, ResourceCount - 1)) * MaxGauge) + CurrentGauge;
        return Mathf.Max(0, totalRemainingSteps - reservedHarvestSteps);
    }

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
            candidate.SetItem(ResolveItemId());
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
            clone.SetItem(ResolveItemId());
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

            candidate.SetItem(ResolveItemId());
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
        clone.SetItem(ResolveItemId());
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

            targetRenderer.enabled = isVisible;
        }

        Collider[] colliders = bodyTransform.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider targetCollider = colliders[i];
            if (targetCollider == null || IsPortableHierarchy(targetCollider.transform))
            {
                continue;
            }

            targetCollider.enabled = isVisible;
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
}
