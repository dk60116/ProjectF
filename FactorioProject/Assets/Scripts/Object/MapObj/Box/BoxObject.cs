using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

public class BoxObject : InstallationObject
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
    private float focusActivationRadius = 2f;
    [SerializeField]
    private bool isOpen = true;
    [SerializeField, Min(0.01f)]
    private float hingeTweenDuration = 0.2f;
    [SerializeField]
    private Ease hingeTweenEase = Ease.OutCubic;

    private Vector3 hingeBaseLocalEuler;
    private bool hingeBaseLocalEulerCached;

    [SerializeField]
    private SpriteRenderer itemIcon;

    private TerrainGenerator cachedTerrainGenerator;
    private int cachedDisplayedItemId = int.MinValue;
    private Sprite cachedDisplayedSprite;

    public float FocusActivationRadius => Mathf.Max(0f, focusActivationRadius);
    public bool IsOpen => isOpen;
    public static float GlobalMaxFocusActivationRadius
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

    private void OnEnable()
    {
        ActiveInstances.Add(this);
        globalMaxFocusActivationRadiusDirty = true;
        CacheHingeBaseLocalEuler();
        ApplyHingeRotation(false);
        SyncContainedStackVisibility(true);
        SyncItemIcon(true);
    }

    private void OnDisable()
    {
        ActiveInstances.Remove(this);
        globalMaxFocusActivationRadiusDirty = true;
        hinge?.DOKill();
        ApplyItemIconSprite(null, -1, true);
    }

    private void LateUpdate()
    {
        SyncContainedStackVisibility();
        SyncItemIcon();
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

    public bool TryPickupContainedObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex)
    {
        if (!isOpen || player == null || pickupRadius <= 0f)
        {
            return false;
        }

        if (!TryGetContentBlock(out Block contentBlock) || contentBlock == null)
        {
            return false;
        }

        return contentBlock.TryPickupOneInputAreaCenterObjectToBag(player, playerPosition, pickupRadius, preferredSlotIndex);
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
            return;
        }

        isOpen = shouldOpen;
        ApplyHingeRotation(Application.isPlaying);
        SyncContainedStackVisibility(true);
        SyncItemIcon(true);

        if (persistState)
        {
            PersistRuntimeState();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (focusActivationRadius < 0f)
        {
            focusActivationRadius = 0f;
        }

        globalMaxFocusActivationRadiusDirty = true;
        CacheHingeBaseLocalEuler();
        ApplyHingeRotation(false);
        SyncItemIcon(true);
    }
#endif

    private void ApplyHingeRotation(bool animate)
    {
        if (hinge == null)
        {
            return;
        }

        hinge.DOKill();

        CacheHingeBaseLocalEuler();
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

    private void CacheHingeBaseLocalEuler()
    {
        if (hinge == null || hingeBaseLocalEulerCached)
        {
            return;
        }

        hingeBaseLocalEuler = hinge.localEulerAngles;
        hingeBaseLocalEulerCached = true;
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

        Vector3 localEulerAngles = hingeBaseLocalEulerCached ? hingeBaseLocalEuler : hinge.localEulerAngles;
        localEulerAngles.x = angle;
        hinge.localEulerAngles = localEulerAngles;
    }

    private void PersistRuntimeState()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        TerrainGenerator terrainGenerator = FindObjectOfType<TerrainGenerator>();
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
        if (!TryGetContentBlock(out Block contentBlock))
        {
            return;
        }

        contentBlock.SetInputAreaCenterObjectsVisible(isOpen);
    }

    private void SyncItemIcon(bool force = false)
    {
        if (itemIcon == null)
        {
            return;
        }

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
        if (occupiedCoordinates != null)
        {
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                if (!terrainGenerator.TryGetLoadedBlock(occupiedCoordinates[i], out Block occupiedBlock)
                    || occupiedBlock == null)
                {
                    continue;
                }

                if (occupiedBlock.GetInputAreaCenterItemCount() > 0)
                {
                    contentBlock = occupiedBlock;
                    return true;
                }
            }
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
            cachedTerrainGenerator = FindObjectOfType<TerrainGenerator>();
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
