using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class BoxObject : InstallationObject, IMapObjectLateTick
{
    private static readonly HashSet<BoxObject> ActiveInstances = new HashSet<BoxObject>();
    private static float cachedGlobalMaxFocusActivationRadius;
    private static bool globalMaxFocusActivationRadiusDirty = true;
    private const float ClosedAngle = 0f;
    private const float OpenAngle = 120f;

    [SerializeField]
    private Transform hinge;
    [SerializeField]
    [Min(0f)]
    private float focusActivationRadius = 1f;
    [SerializeField]
    private bool isOpen = true;
    [SerializeField, Min(0.01f)]
    private float hingeTweenDuration = 0.2f;
    [SerializeField]
    private Ease hingeTweenEase = Ease.OutCubic;

    [SerializeField]
    private SpriteRenderer itemIcon, lockIcon;
    [SerializeField]
    private TextMeshPro countText;

    private TerrainGenerator cachedTerrainGenerator;
    private int cachedDisplayedItemId = int.MinValue;
    private Sprite cachedDisplayedSprite;
    private bool cachedLockIconVisible;
    private string cachedCountTextValue;
    private readonly HashSet<int> acceptedItemIdsBuffer = new HashSet<int>();
    private int cachedCountTextItemCount = int.MinValue;
    private int cachedCountTextCapacity = int.MinValue;
    private bool cachedCountTextHasValue;
    private Block lastContainedStackVisibilityBlock;

    public override float FocusActivationRadius => Mathf.Max(0f, focusActivationRadius);
    public bool IsOpen => isOpen;
    public new static float GlobalMaxFocusActivationRadius
    {
        get
        {
            if (!globalMaxFocusActivationRadiusDirty)
            {
                return cachedGlobalMaxFocusActivationRadius;
            }

            cachedGlobalMaxFocusActivationRadius = 0f;
            foreach (BoxObject boxObject in ActiveInstances)
            {
                if (boxObject == null)
                {
                    continue;
                }

                cachedGlobalMaxFocusActivationRadius = Mathf.Max(
                    cachedGlobalMaxFocusActivationRadius,
                    boxObject.FocusActivationRadius);
            }

            globalMaxFocusActivationRadiusDirty = false;
            return cachedGlobalMaxFocusActivationRadius;
        }
    }

    public static bool TryFindNearest(Vector3 worldPosition, out BoxObject nearestBoxObject)
    {
        nearestBoxObject = null;
        if (ActiveInstances.Count == 0)
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;

        foreach (BoxObject candidate in ActiveInstances)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            float candidateRadius = Mathf.Max(0f, candidate.FocusActivationRadius);
            float candidateRadiusSqr = candidateRadius * candidateRadius;
            float distanceSqr = candidate.GetInteractionDistanceSqr(worldPosition);
            if (distanceSqr > candidateRadiusSqr || distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            nearestBoxObject = candidate;
        }

        return nearestBoxObject != null;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveInstances.Add(this);
        globalMaxFocusActivationRadiusDirty = true;
        ApplyHingeRotation(false);
        SyncContainedStackVisibility(true);
        SyncItemIcon(true);
        SyncCountText(true);
        MapObjectTickManager.RegisterLateTick(this);
    }

    protected override void OnDisable()
    {
        MapObjectTickManager.UnregisterLateTick(this);
        ActiveInstances.Remove(this);
        globalMaxFocusActivationRadiusDirty = true;
        hinge?.DOKill();
        RestoreLastContainedStackVisibilityBlock();
        ApplyItemIconSprite(null, -1, true);
        SetLockIconVisible(false, true);
        ApplyEmptyCountText(true);
        base.OnDisable();
    }

    public void ManagedLateUpdateTick(float deltaTime)
    {
        SyncContainedStackVisibility();
        SyncItemIcon();
        SyncCountText();
    }

    public bool IsWithinFocusRange(Vector3 worldPosition)
    {
        float radius = FocusActivationRadius;
        return GetInteractionDistanceSqr(worldPosition) <= radius * radius;
    }

    public int GetInteractionButtonIconIndex()
    {
        return isOpen ? 1 : 0;
    }

    public bool TryGetGroundDropCoordinate(out Vector2Int dropCoordinate)
    {
        dropCoordinate = Vector2Int.zero;
        if (!isOpen || !isActiveAndEnabled)
        {
            return false;
        }

        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            return false;
        }

        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            if (IsItemAreaCoordinate(occupiedCoordinates[i]))
            {
                return false;
            }
        }

        dropCoordinate = anchorCoordinate;
        return true;
    }

    public bool TryPickupContainedObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex, int preferredItemId = -1)
    {
        if (!isOpen || player == null || pickupRadius <= 0f)
        {
            return false;
        }

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        return contentBlock.TryPickupOneInputAreaCenterObjectToBag(player, playerPosition, pickupRadius, preferredSlotIndex, preferredItemId);
    }

    public bool TryPickupContainedObjectToHand(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (!isOpen || player == null || pickupRadius <= 0f)
        {
            return false;
        }

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        return contentBlock.TryPickupOneInputAreaCenterObjectToHand(player, playerPosition, pickupRadius);
    }

    public bool TryPreviewContainedObjectPickup(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId)
    {
        return TryPreviewContainedObjectPickup(
            player,
            playerPosition,
            pickupRadius,
            preferredItemId,
            out previewItemId,
            out _);
    }

    public bool TryPreviewContainedObjectPickup(Player player, Vector3 playerPosition, float pickupRadius, int preferredItemId, out int previewItemId, out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (!isOpen || player == null || pickupRadius <= 0f)
        {
            return false;
        }

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        return contentBlock.TryPreviewPickupInputAreaCenterObjects(
            player,
            playerPosition,
            pickupRadius,
            preferredItemId,
            out previewItemId,
            out previewPickupCount);
    }

    public bool TryTakeOneContainedObject(out int takenItemId, bool requireOpen = false)
    {
        return TryTakeOneContainedObject(null, out takenItemId, requireOpen);
    }

    public bool TryTakeOneContainedObject(System.Predicate<int> itemFilter, out int takenItemId, bool requireOpen = false)
    {
        takenItemId = -1;
        if (requireOpen && !isOpen)
        {
            return false;
        }

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        int itemId = contentBlock.GetInputAreaCenterItemId();
        if (itemId < 0 || (itemFilter != null && !itemFilter(itemId)))
        {
            return false;
        }

        return contentBlock.TryConsumeOneInputAreaCenterObject(itemId, out takenItemId);
    }

    public bool TryGetContainedObjectTopWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = transform.position;
        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        return contentBlock.GetInputAreaCenterItemId() >= 0
               && contentBlock.TryGetInputAreaCenterTopWorldPosition(-1, out worldPosition);
    }

    public bool TryGetContainedObjectTopItemId(out int itemId)
    {
        itemId = -1;
        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        itemId = contentBlock.GetInputAreaCenterItemId();
        return itemId >= 0;
    }

    public bool TryGetObjectInfoItem(out int itemId, out int itemCount, out int capacity)
    {
        itemId = -1;
        itemCount = 0;
        capacity = 0;

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        capacity = contentBlock.TryGetInstalledItemAreaCapacity(out int installedCapacity)
            ? Mathf.Max(1, installedCapacity)
            : 10;
        itemId = contentBlock.GetInputAreaCenterItemId();
        if (itemId >= 0)
        {
            itemCount = contentBlock.GetInputAreaCenterItemCount(itemId);
        }
        else if (TryGetSingleResolvedItemId(out int filteredItemId))
        {
            itemId = filteredItemId;
            itemCount = contentBlock.GetInputAreaCenterItemCount(filteredItemId);
        }

        return true;
    }

    public bool TryPutOneContainedObject(
        int itemId,
        Vector3 startWorldPosition,
        float delay,
        out PortableObject targetPortableObject,
        bool useJumpArc = true,
        float moveDuration = PortableObject.MoveToDuration)
    {
        targetPortableObject = null;
        if (itemId < 0 || !AcceptsItem(itemId))
        {
            return false;
        }

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        return contentBlock.TryAddInputAreaCenterObjectAnimated(
            itemId,
            startWorldPosition,
            delay,
            out targetPortableObject,
            null,
            null,
            useJumpArc,
            moveDuration);
    }

    public bool CanPutOneContainedObject(int itemId)
    {
        if (itemId < 0 || !AcceptsItem(itemId))
        {
            return false;
        }

        return TryGetContentBlock(out Block contentBlock)
               && contentBlock != null
               && contentBlock.CanAddInputAreaCenterObjects(1, itemId);
    }

    public bool TryPutOneContainedObjectInstant(int itemId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (itemId < 0 || !AcceptsItem(itemId))
        {
            return false;
        }

        return TryGetContentBlock(out Block contentBlock)
               && contentBlock != null
               && contentBlock.TryAddInputAreaCenterObject(itemId, out targetPortableObject, true);
    }

    public bool AcceptsItem(int itemId)
    {
        if (itemId < 0)
        {
            return false;
        }

        return IsItemFilterEnabled(itemId, ResolveFilterBitCount(itemId));
    }

    public void ToggleOpenState()
    {
        SetOpenState(!isOpen);
    }

    public void SetOpenState(bool shouldOpen, bool persistState = true)
    {
        if (isOpen == shouldOpen)
        {
            ApplyHingeRotation(false);
            SyncContainedStackVisibility(true);
            SyncItemIcon(true);
            SyncCountText(true);
            return;
        }

        isOpen = shouldOpen;
        ApplyHingeRotation(Application.isPlaying);
        SyncContainedStackVisibility(true);
        SyncItemIcon(true);
        SyncCountText(true);

        if (persistState)
        {
            PersistRuntimeState();
        }
    }

    public override void PrepareForPool()
    {
        hinge?.DOKill();
        RestoreLastContainedStackVisibilityBlock();
        ApplyItemIconSprite(null, -1, true);
        SetLockIconVisible(false, true);
        ApplyEmptyCountText(true);
        cachedTerrainGenerator = null;
        cachedDisplayedItemId = int.MinValue;
        cachedDisplayedSprite = null;
        cachedLockIconVisible = false;
        cachedCountTextValue = string.Empty;
        base.PrepareForPool();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (focusActivationRadius < 0f)
        {
            focusActivationRadius = 0f;
        }

        globalMaxFocusActivationRadiusDirty = true;
        ApplyHingeRotation(false);
        SyncItemIcon(true);
        SyncCountText(true);
    }
#endif

    private void ApplyHingeRotation(bool animate)
    {
        if (hinge == null)
        {
            return;
        }

        hinge.DOKill();

        float targetAngle = isOpen ? OpenAngle : ClosedAngle;

        if (animate && hingeTweenDuration > 0f)
        {
            float startAngle = GetCurrentHingeAngleX();
            DOTween.To(() => startAngle, value =>
                {
                    startAngle = value;
                    SetHingeAngleX(value);
                }, targetAngle, hingeTweenDuration)
                .SetTarget(hinge)
                .SetEase(hingeTweenEase);
            return;
        }

        SetHingeAngleX(targetAngle);
    }

    private float GetCurrentHingeAngleX()
    {
        if (hinge == null)
        {
            return ClosedAngle;
        }

        float angle = hinge.localEulerAngles.x;
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return Mathf.Abs(angle - OpenAngle) < Mathf.Abs(angle - ClosedAngle)
            ? OpenAngle
            : ClosedAngle;
    }

    private void SetHingeAngleX(float angle)
    {
        if (hinge == null)
        {
            return;
        }

        hinge.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }

    private void PersistRuntimeState()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        TerrainGenerator terrainGenerator = TerrainGenerator.ResolveActive();
        if (terrainGenerator == null)
        {
            return;
        }

        terrainGenerator.RegisterInstallationRuntimeState(this);
    }

    private float GetInteractionDistanceSqr(Vector3 worldPosition)
    {
        Bounds bounds = GetInteractionBounds();
        Vector3 closestPoint = bounds.ClosestPoint(worldPosition);
        closestPoint.y = worldPosition.y;

        Vector3 offset = closestPoint - worldPosition;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private int ResolveFilterBitCount(int fallbackItemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null || definitions.Count <= 0)
        {
            return Mathf.Max(1, fallbackItemId + 1);
        }

        int maxItemId = fallbackItemId;
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            if (definition.id > maxItemId)
            {
                maxItemId = definition.id;
            }
        }

        return Mathf.Max(1, maxItemId + 1);
    }

    private Bounds GetInteractionBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds combinedBounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combinedBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                combinedBounds.Expand(new Vector3(0.2f, 0f, 0.2f));
                return combinedBounds;
            }
        }

        return new Bounds(transform.position, new Vector3(1.2f, 1f, 1.2f));
    }

    private static bool IsItemAreaCoordinate(Vector2Int coordinate)
    {
        return InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate)
               || InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleOutputAreaController.CoordinateIsOutputArea(coordinate);
    }

    private void SyncContainedStackVisibility(bool force = false)
    {
        if (!TryGetVisibilityContentBlock(out Block contentBlock))
        {
            RestoreLastContainedStackVisibilityBlock();
            return;
        }

        if (lastContainedStackVisibilityBlock != null && lastContainedStackVisibilityBlock != contentBlock)
        {
            lastContainedStackVisibilityBlock.ClearInputAreaCenterObjectsVisibilitySource(this);
        }

        lastContainedStackVisibilityBlock = contentBlock;
        contentBlock.SetInputAreaCenterObjectsVisible(isOpen, this);
    }

    private void RestoreLastContainedStackVisibilityBlock()
    {
        if (lastContainedStackVisibilityBlock == null)
        {
            return;
        }

        lastContainedStackVisibilityBlock.ClearInputAreaCenterObjectsVisibilitySource(this);
        lastContainedStackVisibilityBlock = null;
    }

    private void SyncItemIcon(bool force = false)
    {
        if (itemIcon == null)
        {
            SetLockIconVisible(false, force);
            return;
        }

        if (TryGetSingleResolvedItemId(out int filteredItemId))
        {
            ApplyItemIconSprite(ResolveItemIconSprite(filteredItemId), filteredItemId, force);
            SetLockIconVisible(true, force);
            return;
        }

        SetLockIconVisible(false, force);

        if (!TryGetContentBlock(out Block contentBlock))
        {
            ApplyItemIconSprite(null, -1, force);
            return;
        }

        int itemCount = contentBlock.GetInputAreaCenterItemCount();
        int itemId = itemCount > 0 ? contentBlock.GetInputAreaCenterItemId() : -1;
        if (itemCount <= 0 || itemId < 0)
        {
            ApplyItemIconSprite(null, -1, force);
            return;
        }

        ApplyItemIconSprite(ResolveItemIconSprite(itemId), itemId, force);
    }

    private void SyncCountText(bool force = false)
    {
        if (countText == null)
        {
            return;
        }

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            ApplyEmptyCountText(force);
            return;
        }

        int capacity = 10;
        if (contentBlock.TryGetInstalledItemAreaCapacity(out int installedCapacity))
        {
            capacity = installedCapacity;
        }

        bool hasSingleFilteredItem = TryGetSingleResolvedItemId(out int filteredItemId);
        int itemCount = hasSingleFilteredItem && filteredItemId >= 0
            ? contentBlock.GetInputAreaCenterItemCount(filteredItemId)
            : contentBlock.GetInputAreaCenterItemCount();

        if (itemCount <= 0 && !hasSingleFilteredItem)
        {
            ApplyEmptyCountText(force);
            return;
        }

        ApplyCountTextValue(itemCount, Mathf.Max(1, capacity), force);
    }

    private void ApplyItemIconSprite(Sprite sprite, int itemId, bool force)
    {
        if (!force && cachedDisplayedItemId == itemId && cachedDisplayedSprite == sprite)
        {
            return;
        }

        cachedDisplayedItemId = itemId;
        cachedDisplayedSprite = sprite;

        if (itemIcon.gameObject != null && !itemIcon.gameObject.activeSelf)
        {
            itemIcon.gameObject.SetActive(true);
        }

        itemIcon.sprite = sprite;
        itemIcon.enabled = sprite != null;
    }

    private void SetLockIconVisible(bool visible, bool force)
    {
        if (lockIcon == null)
        {
            return;
        }

        if (!force && cachedLockIconVisible == visible)
        {
            return;
        }

        cachedLockIconVisible = visible;

        if (lockIcon.gameObject != null && !lockIcon.gameObject.activeSelf)
        {
            lockIcon.gameObject.SetActive(true);
        }

        lockIcon.enabled = visible;
    }

    private void ApplyEmptyCountText(bool force)
    {
        if (countText == null)
        {
            return;
        }

        if (!force && !cachedCountTextHasValue && string.IsNullOrEmpty(cachedCountTextValue))
        {
            return;
        }

        cachedCountTextValue = string.Empty;
        cachedCountTextHasValue = false;
        cachedCountTextItemCount = int.MinValue;
        cachedCountTextCapacity = int.MinValue;
        countText.text = string.Empty;
    }

    private void ApplyCountTextValue(int itemCount, int capacity, bool force)
    {
        if (countText == null)
        {
            return;
        }

        if (!force
            && cachedCountTextHasValue
            && cachedCountTextItemCount == itemCount
            && cachedCountTextCapacity == capacity)
        {
            return;
        }

        cachedCountTextHasValue = true;
        cachedCountTextItemCount = itemCount;
        cachedCountTextCapacity = capacity;
        cachedCountTextValue = $"{itemCount:00} / {capacity:00}";
        countText.text = cachedCountTextValue;
    }

    private bool TryGetSingleFilteredItemId(out int itemId)
    {
        itemId = -1;
        if (!IsItemFilterMaskInitialized || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return false;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null || definitions.Count <= 0)
        {
            return false;
        }

        int totalItemCount = ResolveFilterBitCount(-1);
        int matchedItemCount = 0;

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition == null || definition.id < 0)
            {
                continue;
            }

            if (!IsItemFilterEnabled(definition.id, totalItemCount))
            {
                continue;
            }

            matchedItemCount++;
            itemId = definition.id;
            if (matchedItemCount > 1)
            {
                itemId = -1;
                return false;
            }
        }

        return matchedItemCount == 1 && itemId >= 0;
    }

    private bool TryGetSingleResolvedItemId(out int itemId)
    {
        itemId = -1;
        if (TryGetSingleFilteredItemId(out itemId))
        {
            return true;
        }

        acceptedItemIdsBuffer.Clear();
        if (!TryCollectAreaAcceptedItemIds(acceptedItemIdsBuffer) || acceptedItemIdsBuffer.Count != 1)
        {
            return false;
        }

        foreach (int acceptedItemId in acceptedItemIdsBuffer)
        {
            itemId = acceptedItemId;
            break;
        }

        return itemId >= 0;
    }

    private bool TryCollectAreaAcceptedItemIds(ISet<int> acceptedItemIds)
    {
        if (acceptedItemIds == null)
        {
            return false;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        if (occupiedCoordinates == null || occupiedCoordinates.Count <= 0)
        {
            return false;
        }

        bool foundAny = false;
        for (int i = 0; i < occupiedCoordinates.Count; i++)
        {
            Vector2Int coordinate = occupiedCoordinates[i];
            if (InputOutputModule.TryGetRuntimeIoOverlapAllowedItemIds(coordinate, acceptedItemIds))
            {
                foundAny = true;
                continue;
            }

            foundAny |= InputOutputModuleItemAreaController.TryGetAcceptedItemIds(coordinate, acceptedItemIds);

            if (InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
                && InputOutputModule.TryGetModuleAtRuntimeGridCoordinate(coordinate, out InputOutputModule module)
                && module != null)
            {
                foundAny |= module.AppendRuntimeInputItemIds(acceptedItemIds);
            }
        }

        return foundAny;
    }

    private bool TryGetAnchorBlock(out Block anchorBlock)
    {
        anchorBlock = null;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return false;
        }

        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (terrainGenerator == null
            || !terrainGenerator.TryGetLoadedBlock(anchorCoordinate, out anchorBlock)
            || anchorBlock == null
            || anchorBlock.MapObject != this)
        {
            anchorBlock = null;
            return false;
        }

        return true;
    }

    private bool TryGetVisibilityContentBlock(out Block contentBlock)
    {
        return TryGetContentBlock(out contentBlock);
    }

    private bool TryGetContentBlock(out Block contentBlock)
    {
        contentBlock = null;
        TerrainGenerator terrainGenerator = ResolveTerrainGenerator();
        if (terrainGenerator == null)
        {
            return false;
        }

        if (TryGetGroundDropCoordinate(out Vector2Int groundDropCoordinate)
            && terrainGenerator.TryGetLoadedBlock(groundDropCoordinate, out Block groundDropBlock)
            && groundDropBlock != null)
        {
            contentBlock = groundDropBlock;
            return true;
        }

        IReadOnlyList<Vector2Int> occupiedCoordinates = RuntimeOccupiedCoordinates;
        Block firstOccupiedBlock = null;
        Block preferredItemAreaBlock = null;
        int preferredFilteredItemId = TryGetSingleResolvedItemId(out int filteredItemId) ? filteredItemId : -1;
        if (occupiedCoordinates != null)
        {
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                if (!terrainGenerator.TryGetLoadedBlock(occupiedCoordinates[i], out Block occupiedBlock)
                    || occupiedBlock == null)
                {
                    continue;
                }

                if (firstOccupiedBlock == null)
                {
                    firstOccupiedBlock = occupiedBlock;
                }

                bool isInputItemAreaCoordinate = InputOutputModuleItemAreaController.CoordinateIsItemArea(occupiedCoordinates[i]);
                if (isInputItemAreaCoordinate
                    && InputOutputModule.TryGetModuleAtRuntimeGridCoordinate(occupiedCoordinates[i], out InputOutputModule module)
                    && module != null
                    && module.TryGetRuntimeInputBlock(terrainGenerator, preferredFilteredItemId, out Block moduleInputBlock)
                    && moduleInputBlock != null)
                {
                    if (moduleInputBlock.GetInputAreaCenterItemCount() > 0)
                    {
                        contentBlock = moduleInputBlock;
                        return true;
                    }

                    if (preferredItemAreaBlock == null)
                    {
                        preferredItemAreaBlock = moduleInputBlock;
                    }
                }

                if (preferredItemAreaBlock == null && IsItemAreaCoordinate(occupiedCoordinates[i]))
                {
                    preferredItemAreaBlock = occupiedBlock;
                }

                if (occupiedBlock.GetInputAreaCenterItemCount() > 0)
                {
                    contentBlock = occupiedBlock;
                    return true;
                }
            }
        }

        if (preferredItemAreaBlock != null)
        {
            contentBlock = preferredItemAreaBlock;
            return true;
        }

        if (firstOccupiedBlock != null)
        {
            contentBlock = firstOccupiedBlock;
            return true;
        }

        return TryGetAnchorBlock(out contentBlock);
    }

    private TerrainGenerator ResolveTerrainGenerator()
    {
        if (cachedTerrainGenerator != null)
        {
            return cachedTerrainGenerator;
        }

        cachedTerrainGenerator = GetComponentInParent<TerrainGenerator>();
        if (cachedTerrainGenerator == null)
        {
            cachedTerrainGenerator = TerrainGenerator.ResolveActive();
        }

        return cachedTerrainGenerator;
    }

    private static Sprite ResolveItemIconSprite(int itemId)
    {
        if (itemId < 0 || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return null;
        }

        List<ItemDefinition> definitions = GameManager.Instance.ItemManger.ItemDefinitions;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == itemId)
            {
                return definition.icon;
            }
        }

        return null;
    }
}
