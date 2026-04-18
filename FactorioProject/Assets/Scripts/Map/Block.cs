using ProjectF.Attributes;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Block : BaseObject
{
    public enum BlockType { Ground, Water }
    private const int InputAreaCenterStackStateSentinel = -1000000001;
    private const float InputAreaCenterVerticalSpacing = 0.05f;

    [SerializeField, ReadOnly]
    private Vector2Int coordinate;

    [SerializeField]
    private BlockType type;

    [SerializeField]
    private Transform body;

    [SerializeField, ReadOnly]
    private MapObject mapObject;

    [SerializeField]
    private List<Transform> floorObjects;

    [SerializeField]
    private PortableObject floorObjectPrefab;

    [SerializeField, Min(1)]
    private int maxFloorObjectsPerStack = 10;

    [SerializeField, Min(0.01f)]
    private float floorObjectVerticalSpacing = 0.05f;

    [SerializeField, Min(1)]
    private int inputAreaCenterMaxObjects = 10;

    [SerializeField]
    private MapFocus focus;

    private readonly List<List<PortableObject>> floorStacks = new List<List<PortableObject>>();
    private readonly List<PortableObject> inputAreaCenterStack = new List<PortableObject>();
    private PortableObjectPool floorObjectPool;
    private Transform inputAreaCenterAnchor;
    private MeshRenderer[] cachedBodyRenderers = Array.Empty<MeshRenderer>();
    private float cachedInputAreaCenterHeight;
    private bool childReferencesCached;

    private void Awake()
    {
        CacheChildReferences();
        EnsureFloorObjectsInitialized();
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (floorObjectPool == null)
        {
            return;
        }

        ResetFloorObjects();
    }

    public void Initialize(Vector2Int blockCoordinate, BlockType blockType)
    {
        CacheChildReferences();
        coordinate = blockCoordinate;
        type = blockType;
        objectName = $"{blockType}_{blockCoordinate.x}_{blockCoordinate.y}";
        gameObject.name = $"Block ({blockCoordinate.x}, {blockCoordinate.y})";
        SetFocusVisible(false);
    }

    public void SetMapObject(MapObject value)
    {
        if (mapObject is Resource existingResource && existingResource != value)
        {
            existingResource.SetOwningBlock(null);
        }

        mapObject = value;

        if (mapObject is Resource resource)
        {
            resource.SetOwningBlock(this);
        }
    }

    public void PrepareForPool()
    {
        SetFocusVisible(false);
        ResetFloorObjects();

        MapObject childMapObject = mapObject;
        if (childMapObject != null && childMapObject.transform != null && childMapObject.transform.parent == transform)
        {
            childMapObject.transform.SetParent(null, true);
            if (Application.isPlaying)
            {
                Destroy(childMapObject.gameObject);
            }
            else
            {
                DestroyImmediate(childMapObject.gameObject);
            }
        }

        SetMapObject(null);
        coordinate = default;
        type = default;
        objectName = string.Empty;
        gameObject.name = "Pooled Block";
    }

    public void SetBodyRotation(float yRotation)
    {
        CacheChildReferences();
        if (body == null)
        {
            return;
        }

        body.localRotation = Quaternion.Euler(0f, yRotation, 0f);
    }

    public void SetBaseBodyVisible(bool visible)
    {
        CacheChildReferences();
        if (cachedBodyRenderers == null || cachedBodyRenderers.Length == 0)
        {
            return;
        }

        for (int i = 0; i < cachedBodyRenderers.Length; i++)
        {
            MeshRenderer renderer = cachedBodyRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = visible;
        }
    }

    public bool TryAddFloorObject(int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (BlocksFloorObjectStacking())
        {
            return false;
        }

        if (!ResolveFloorObjectPool())
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExisting = pass == 0;

            for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
            {
                Transform anchor = floorObjects[stackIndex];
                if (anchor == null)
                {
                    continue;
                }

                List<PortableObject> stack = floorStacks[stackIndex];
                if (stack == null)
                {
                    continue;
                }

                if (requireExisting && stack.Count == 0)
                {
                    continue;
                }

                if (!IsStackCompatible(stack, objectId))
                {
                    continue;
                }

                if (stack.Count >= Mathf.Max(1, maxFloorObjectsPerStack))
                {
                    continue;
                }

                PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
                if (portableObject == null)
                {
                    continue;
                }

                ConfigureFloorObjectTransform(portableObject, anchor, stack.Count);
                portableObject.SetItem(objectId);
                portableObject.SetBatchedRendering(true);
                stack.Add(portableObject);
                targetPortableObject = portableObject;
                return true;
            }
        }

        return false;
    }

    public bool CanAddInputAreaCenterObjects(int count)
    {
        return CanAddInputAreaCenterObjects(count, -1);
    }

    public bool CanAddInputAreaCenterObjects(int count, int itemId)
    {
        if (count <= 0)
        {
            return true;
        }

        CleanupPortableStack(inputAreaCenterStack);
        if (itemId >= 0 && !IsStackCompatible(inputAreaCenterStack, itemId))
        {
            return false;
        }

        return ResolveInputAreaCenterCapacity() - inputAreaCenterStack.Count >= count;
    }

    public bool HasInputAreaCenterObjects()
    {
        CleanupPortableStack(inputAreaCenterStack);
        return inputAreaCenterStack.Count > 0;
    }

    public bool HasInputAreaCenterItem(int itemId)
    {
        CleanupPortableStack(inputAreaCenterStack);
        if (itemId < 0 || inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject bottom = inputAreaCenterStack[0];
        return bottom != null && bottom.ItemId == itemId;
    }

    public int GetInputAreaCenterItemCount(int itemId = -1)
    {
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return 0;
        }

        if (itemId < 0)
        {
            return inputAreaCenterStack.Count;
        }

        int count = 0;
        for (int i = 0; i < inputAreaCenterStack.Count; i++)
        {
            PortableObject portableObject = inputAreaCenterStack[i];
            if (portableObject != null && portableObject.ItemId == itemId)
            {
                count++;
            }
        }

        return count;
    }

    public int GetInputAreaCenterItemId()
    {
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return -1;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        return topObject != null ? topObject.ItemId : -1;
    }

    public void SetInputAreaCenterObjectsVisible(bool visible)
    {
        CleanupPortableStack(inputAreaCenterStack);
        EnsureInputAreaCenterAnchorInitialized();

        for (int i = 0; i < inputAreaCenterStack.Count; i++)
        {
            PortableObject portableObject = inputAreaCenterStack[i];
            if (portableObject == null)
            {
                continue;
            }

            if (!visible)
            {
                portableObject.SetBatchedRendering(false);
                if (portableObject.gameObject.activeSelf)
                {
                    portableObject.gameObject.SetActive(false);
                }
                continue;
            }

            if (portableObject.IsMovingToTarget)
            {
                if (!portableObject.gameObject.activeSelf)
                {
                    portableObject.gameObject.SetActive(true);
                }

                continue;
            }

            ConfigureInputAreaCenterObjectTransform(portableObject, i);
            portableObject.SetBatchedRendering(true);
        }
    }

    public bool TryGetInputAreaCenterTopWorldPosition(int expectedItemId, out Vector3 worldPosition)
    {
        CleanupPortableStack(inputAreaCenterStack);
        EnsureInputAreaCenterAnchorInitialized();
        worldPosition = inputAreaCenterAnchor != null ? inputAreaCenterAnchor.position : transform.position;

        if (inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (expectedItemId >= 0 && itemId != expectedItemId)
        {
            return false;
        }

        worldPosition = topObject.transform.position;
        return true;
    }

    public bool TryConsumeOneInputAreaCenterObject(int expectedItemId, out int consumedItemId)
    {
        consumedItemId = -1;
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            return false;
        }

        if (expectedItemId >= 0 && itemId != expectedItemId)
        {
            return false;
        }

        consumedItemId = itemId;
        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
        ReleaseFloorObject(topObject);
        return true;
    }

    public bool TryConsumeOneInputAreaCenterObjectAnimated(
        int expectedItemId,
        Vector3 targetWorldPosition,
        out int consumedItemId,
        float delay = 0f,
        Action onComplete = null)
    {
        consumedItemId = -1;
        CleanupPortableStack(inputAreaCenterStack);
        if (inputAreaCenterStack.Count <= 0)
        {
            return false;
        }

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            return false;
        }

        if (expectedItemId >= 0 && itemId != expectedItemId)
        {
            return false;
        }

        consumedItemId = itemId;
        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);

        DroppedItemPickupGate gate = topObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        topObject.MoveTo(
            targetWorldPosition,
            Mathf.Max(0f, delay),
            () =>
            {
                ReleaseFloorObject(topObject);
                onComplete?.Invoke();
            });
        return true;
    }

    public int ConsumeInputAreaCenterObjects(int expectedItemId, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int consumed = 0;
        while (consumed < count && TryConsumeOneInputAreaCenterObject(expectedItemId, out _))
        {
            consumed++;
        }

        return consumed;
    }

    public int ConsumeInputAreaCenterObjectsAnimated(
        int expectedItemId,
        int count,
        Vector3 targetWorldPosition,
        float moveInterval = 0.1f)
    {
        if (count <= 0)
        {
            return 0;
        }

        int consumed = 0;
        float interval = Mathf.Max(0f, moveInterval);
        while (consumed < count
               && TryConsumeOneInputAreaCenterObjectAnimated(
                   expectedItemId,
                   targetWorldPosition,
                   out _,
                   consumed * interval))
        {
            consumed++;
        }

        return consumed;
    }

    public bool TryAddInputAreaCenterObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (objectId < 0 || !ResolveFloorObjectPool())
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null
            || inputAreaCenterStack.Count >= ResolveInputAreaCenterCapacity()
            || !IsStackCompatible(inputAreaCenterStack, objectId))
        {
            return false;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetItem(objectId);
        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(inputAreaCenterAnchor, true);
        portableObject.transform.position = startWorldPosition;
        portableObject.transform.rotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);

        int objectIndex = inputAreaCenterStack.Count;
        Vector3 finalLocalPosition = new Vector3(0f, objectIndex * InputAreaCenterVerticalSpacing, 0f);
        Vector3 finalWorldPosition = inputAreaCenterAnchor.TransformPoint(finalLocalPosition);
        inputAreaCenterStack.Add(portableObject);
        DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
        if (gate == null)
        {
            gate = portableObject.gameObject.AddComponent<DroppedItemPickupGate>();
        }

        portableObject.MoveTo(finalWorldPosition, delay, () =>
        {
            if (portableObject == null || inputAreaCenterAnchor == null)
            {
                onComplete?.Invoke();
                return;
            }

            portableObject.transform.SetParent(inputAreaCenterAnchor, false);
            portableObject.transform.localPosition = finalLocalPosition;
            portableObject.transform.localRotation = Quaternion.identity;
            portableObject.transform.localScale = Vector3.one;
            portableObject.gameObject.SetActive(true);
            portableObject.SetBatchedRendering(true);
            gate?.MarkSettled();
            onComplete?.Invoke();
        }, false);

        targetPortableObject = portableObject;
        return true;
    }

    public bool CanAddFloorObjects(int count)
    {
        return CanAddFloorObjects(count, -1);
    }

    public bool CanAddFloorObjects(int count, int itemId)
    {
        EnsureFloorObjectsInitialized();

        if (count <= 0)
        {
            return true;
        }

        if (BlocksFloorObjectStacking())
        {
            return false;
        }

        return GetAvailableFloorCapacity(itemId) >= count;
    }

    public bool HasFloorObjectItem(int itemId)
    {
        EnsureFloorObjectsInitialized();

        if (itemId < 0)
        {
            return false;
        }

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null && portableObject.ItemId == itemId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public int CountFloorObjects(int itemId)
    {
        EnsureFloorObjectsInitialized();
        if (itemId < 0)
        {
            return 0;
        }

        int count = 0;
        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null && portableObject.ItemId == itemId)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public int RemoveFloorObjects(int itemId, int count)
    {
        EnsureFloorObjectsInitialized();
        if (itemId < 0 || count <= 0)
        {
            return 0;
        }

        int remaining = count;
        for (int stackIndex = 0; stackIndex < floorStacks.Count && remaining > 0; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            for (int objectIndex = stack.Count - 1; objectIndex >= 0 && remaining > 0; objectIndex--)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject == null)
                {
                    stack.RemoveAt(objectIndex);
                    continue;
                }

                if (portableObject.ItemId != itemId)
                {
                    continue;
                }

                stack.RemoveAt(objectIndex);
                ReleaseFloorObject(portableObject);
                remaining--;
            }
        }

        return count - remaining;
    }

    public bool TryRemoveFloorObject(PortableObject targetPortableObject)
    {
        EnsureFloorObjectsInitialized();
        if (targetPortableObject == null)
        {
            return false;
        }

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count <= 0)
            {
                continue;
            }

            int objectIndex = stack.IndexOf(targetPortableObject);
            if (objectIndex < 0)
            {
                continue;
            }

            stack.RemoveAt(objectIndex);
            ReleaseFloorObject(targetPortableObject);

            Transform anchor = stackIndex < floorObjects.Count ? floorObjects[stackIndex] : null;
            if (anchor != null)
            {
                for (int i = objectIndex; i < stack.Count; i++)
                {
                    PortableObject portableObject = stack[i];
                    if (portableObject != null)
                    {
                        ConfigureFloorObjectTransform(portableObject, anchor, i);
                        portableObject.SetBatchedRendering(true);
                    }
                }
            }

            return true;
        }

        return false;
    }

    public bool TryAddFloorObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (BlocksFloorObjectStacking())
        {
            return false;
        }

        if (!ResolveFloorObjectPool())
        {
            return false;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireExisting = pass == 0;

            for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
            {
                Transform anchor = floorObjects[stackIndex];
                if (anchor == null)
                {
                    continue;
                }

                List<PortableObject> stack = floorStacks[stackIndex];
                if (stack == null)
                {
                    continue;
                }

                if (requireExisting && stack.Count == 0)
                {
                    continue;
                }

                if (!IsStackCompatible(stack, objectId))
                {
                    continue;
                }

                if (stack.Count >= Mathf.Max(1, maxFloorObjectsPerStack))
                {
                    continue;
                }

                PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
                if (portableObject == null)
                {
                    continue;
                }

                portableObject.SetItem(objectId);
                portableObject.SetBatchedRendering(false);
                portableObject.transform.SetParent(anchor, true);
                portableObject.transform.position = startWorldPosition;
                portableObject.transform.rotation = Quaternion.identity;
                portableObject.transform.localScale = Vector3.one;
                portableObject.gameObject.SetActive(true);

                int objectIndex = stack.Count;
                Vector3 finalLocalPosition = new Vector3(0f, objectIndex * floorObjectVerticalSpacing, 0f);
                Vector3 finalWorldPosition = anchor.TransformPoint(finalLocalPosition);
                stack.Add(portableObject);
                DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
                if (gate == null)
                {
                    gate = portableObject.gameObject.AddComponent<DroppedItemPickupGate>();
                }

                portableObject.MoveTo(finalWorldPosition, delay, () =>
                {
                    if (portableObject == null || anchor == null)
                    {
                        onComplete?.Invoke();
                        return;
                    }

                    portableObject.transform.SetParent(anchor, false);
                    portableObject.transform.localPosition = finalLocalPosition;
                    portableObject.transform.localRotation = Quaternion.identity;
                    portableObject.transform.localScale = Vector3.one;
                    portableObject.gameObject.SetActive(true);
                    portableObject.SetBatchedRendering(true);
                    gate?.MarkSettled();
                    onComplete?.Invoke();
                }, false);

                targetPortableObject = portableObject;
                return true;
            }
        }

        return false;
    }

    public List<int> CaptureFloorObjectState()
    {
        EnsureFloorObjectsInitialized();

        List<int> itemIds = new List<int>();
        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null)
                {
                    itemIds.Add(portableObject.ItemId);
                }
            }
        }

        int centerStackCount = 0;
        for (int i = 0; i < inputAreaCenterStack.Count; i++)
        {
            if (inputAreaCenterStack[i] != null)
            {
                centerStackCount++;
            }
        }

        if (centerStackCount > 0)
        {
            itemIds.Add(InputAreaCenterStackStateSentinel);
            itemIds.Add(centerStackCount);

            for (int i = 0; i < inputAreaCenterStack.Count; i++)
            {
                PortableObject portableObject = inputAreaCenterStack[i];
                if (portableObject != null)
                {
                    itemIds.Add(portableObject.ItemId);
                }
            }
        }

        return itemIds;
    }

    public void ApplyFloorObjectState(IReadOnlyList<int> itemIds)
    {
        EnsureFloorObjectsInitialized();
        ResetFloorObjects();

        if (itemIds == null)
        {
            return;
        }

        for (int i = 0; i < itemIds.Count; i++)
        {
            int itemId = itemIds[i];
            if (itemId == InputAreaCenterStackStateSentinel)
            {
                if (i + 1 >= itemIds.Count)
                {
                    break;
                }

                int centerStackCount = Mathf.Max(0, itemIds[++i]);
                for (int centerIndex = 0; centerIndex < centerStackCount && i + 1 < itemIds.Count; centerIndex++)
                {
                    int centerItemId = itemIds[++i];
                    if (!TryAddInputAreaCenterObject(centerItemId, out _))
                    {
                        break;
                    }
                }

                continue;
            }

            if (!TryAddFloorObject(itemId, out _))
            {
                break;
            }
        }
    }

    public void SetFocusVisible(bool isVisible)
    {
        if (focus == null)
        {
            focus = GetComponentInChildren<MapFocus>(true);
        }

        focus?.SetVisible(isVisible);
    }

    public Vector2Int Coordinate => coordinate;
    public BlockType Type => type;
    public MapObject MapObject => mapObject;
    public Resource Resource => mapObject as Resource;
    public Transform Body => body;

    public bool TryGetInstalledItemAreaCapacity(out int capacity)
    {
        capacity = 0;

        if (!(mapObject is InstallationObject installationObject))
        {
            return false;
        }

        bool supportsCenterStack = installationObject is BoxObject
                                   || (installationObject.MapFilter & InstallationMapFilter.ItemArea) != 0;
        if (!supportsCenterStack)
        {
            return false;
        }

        ItemDefinition installedDefinition = ResolveInstalledItemAreaDefinition();
        if (installedDefinition == null)
        {
            return false;
        }

        capacity = installedDefinition.capacity > 0 ? installedDefinition.capacity : 10;
        return true;
    }

    public bool TryAutoPickupFloorObjects(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();

        if ((floorObjects == null || floorObjects.Count == 0) && inputAreaCenterStack.Count == 0)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        bool pickedAny = false;

        for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
        {
            Transform anchor = floorObjects[stackIndex];
            if (anchor == null)
            {
                continue;
            }

            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            PortableObject topObject = stack[stack.Count - 1];
            if (topObject == null)
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            Vector3 offset = anchor.position - playerPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;

            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject gatedObject = stack[objectIndex];
                if (gatedObject == null)
                {
                    continue;
                }

                DroppedItemPickupGate gate = gatedObject.GetComponent<DroppedItemPickupGate>();
                if (gate != null)
                {
                    gate.UpdateExitState(gateOriginPosition);
                }
            }

            if (distanceSqr > pickupRadiusSqr)
            {
                continue;
            }

            DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
            if (topGate != null && !topGate.CanPickup(distanceSqr, pickupRadiusSqr))
            {
                continue;
            }

            int itemId = topObject.ItemId;
            if (itemId < 0)
            {
                continue;
            }

            if (!player.TryAddToBag(itemId, out PortableObject bagTarget))
            {
                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            ReleaseFloorObjectToBag(topObject, bagTarget);
            pickedAny = true;
        }

        return pickedAny;
    }

    public bool TryPickupOneFloorObjectToBag(Player player, Vector3 playerPosition, float pickupRadius)
    {
        return TryPickupOneFloorObjectToBag(player, playerPosition, pickupRadius, -1);
    }

    public bool TryPickupOneFloorObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();

        if (floorObjects == null || floorObjects.Count == 0)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;

        for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
        {
            Transform anchor = floorObjects[stackIndex];
            if (anchor == null)
            {
                continue;
            }

            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            PortableObject topObject = stack[stack.Count - 1];
            if (topObject == null)
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            Vector3 offset = anchor.position - playerPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;

            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject gatedObject = stack[objectIndex];
                if (gatedObject == null)
                {
                    continue;
                }

                DroppedItemPickupGate gate = gatedObject.GetComponent<DroppedItemPickupGate>();
                if (gate != null)
                {
                    gate.UpdateExitState(gateOriginPosition);
                }
            }

            if (distanceSqr > pickupRadiusSqr)
            {
                continue;
            }

            DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
            if (topGate != null && !topGate.CanManualPickup(distanceSqr, pickupRadiusSqr))
            {
                continue;
            }

            int itemId = topObject.ItemId;
            if (itemId < 0)
            {
                continue;
            }

            bool added;
            PortableObject bagTarget;
            if (preferredSlotIndex >= 0)
            {
                added = player.TryAddToBagAtSlot(preferredSlotIndex, itemId, out bagTarget);
            }
            else
            {
                added = player.TryAddToBag(itemId, out bagTarget);
            }

            if (!added)
            {
                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            ReleaseFloorObjectToBag(topObject, bagTarget);
            return true;
        }

        return TryPickupOneInputAreaCenterObjectToBag(player, playerPosition, pickupRadius, preferredSlotIndex);
    }

    public bool TryPickupOneFloorObjectToHand(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();

        if ((floorObjects == null || floorObjects.Count == 0) && inputAreaCenterStack.Count == 0)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;

        for (int stackIndex = 0; stackIndex < floorObjects.Count; stackIndex++)
        {
            Transform anchor = floorObjects[stackIndex];
            if (anchor == null)
            {
                continue;
            }

            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            PortableObject topObject = stack[stack.Count - 1];
            if (topObject == null)
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            Vector3 offset = anchor.position - playerPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;

            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject gatedObject = stack[objectIndex];
                if (gatedObject == null)
                {
                    continue;
                }

                DroppedItemPickupGate gate = gatedObject.GetComponent<DroppedItemPickupGate>();
                if (gate != null)
                {
                    gate.UpdateExitState(gateOriginPosition);
                }
            }

            if (distanceSqr > pickupRadiusSqr)
            {
                continue;
            }

            DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
            if (topGate != null && !topGate.CanManualPickup(distanceSqr, pickupRadiusSqr))
            {
                continue;
            }

            int itemId = topObject.ItemId;
            if (itemId < 0)
            {
                continue;
            }

            if (!player.TryAddToHand(itemId, out PortableObject handTarget))
            {
                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            ReleaseFloorObjectToHand(topObject, handTarget);
            return true;
        }

        return TryPickupOneInputAreaCenterObjectToHand(player, playerPosition, pickupRadius);
    }

    public int TransferFloorObjectsToHand(Player player)
    {
        if (player == null)
        {
            return 0;
        }

        EnsureFloorObjectsInitialized();
        if (floorStacks == null || floorStacks.Count == 0)
        {
            return 0;
        }

        int transferred = 0;

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            if (stack == null || stack.Count == 0)
            {
                continue;
            }

            for (int objectIndex = stack.Count - 1; objectIndex >= 0; objectIndex--)
            {
                PortableObject floorObject = stack[objectIndex];
                if (floorObject == null)
                {
                    stack.RemoveAt(objectIndex);
                    continue;
                }

                int itemId = floorObject.ItemId;
                if (itemId < 0)
                {
                    stack.RemoveAt(objectIndex);
                    ReleaseFloorObjectToHand(floorObject, null);
                    continue;
                }

                if (!player.TryAddToHand(itemId, out PortableObject handTarget))
                {
                    return transferred;
                }

                stack.RemoveAt(objectIndex);
                ReleaseFloorObjectToHand(floorObject, handTarget);
                transferred++;
            }
        }

        for (int objectIndex = inputAreaCenterStack.Count - 1; objectIndex >= 0; objectIndex--)
        {
            PortableObject floorObject = inputAreaCenterStack[objectIndex];
            if (floorObject == null)
            {
                inputAreaCenterStack.RemoveAt(objectIndex);
                continue;
            }

            int itemId = floorObject.ItemId;
            if (itemId < 0)
            {
                inputAreaCenterStack.RemoveAt(objectIndex);
                ReleaseFloorObjectToHand(floorObject, null);
                continue;
            }

            if (!player.TryAddToHand(itemId, out PortableObject handTarget))
            {
                return transferred;
            }

            inputAreaCenterStack.RemoveAt(objectIndex);
            ReleaseFloorObjectToHand(floorObject, handTarget);
            transferred++;
        }

        return transferred;
    }

    private void EnsureFloorObjectsInitialized()
    {
        if (floorObjects == null)
        {
            floorObjects = new List<Transform>();
        }

        while (floorStacks.Count < floorObjects.Count)
        {
            floorStacks.Add(new List<PortableObject>());
        }

        while (floorStacks.Count > floorObjects.Count)
        {
            floorStacks.RemoveAt(floorStacks.Count - 1);
        }
    }

    private void ResetFloorObjects()
    {
        EnsureFloorObjectsInitialized();
        if (floorObjectPool == null)
        {
            for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
            {
                floorStacks[stackIndex].Clear();
            }

            inputAreaCenterStack.Clear();
            return;
        }

        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            List<PortableObject> stack = floorStacks[stackIndex];
            for (int objectIndex = 0; objectIndex < stack.Count; objectIndex++)
            {
                PortableObject portableObject = stack[objectIndex];
                if (portableObject != null)
                {
                    floorObjectPool.Release(portableObject);
                }
            }

            stack.Clear();
        }

        for (int objectIndex = 0; objectIndex < inputAreaCenterStack.Count; objectIndex++)
        {
            PortableObject portableObject = inputAreaCenterStack[objectIndex];
            if (portableObject != null)
            {
                floorObjectPool.Release(portableObject);
            }
        }

        inputAreaCenterStack.Clear();
    }

    private int GetAvailableFloorCapacity()
    {
        return GetAvailableFloorCapacity(-1);
    }

    private int GetAvailableFloorCapacity(int itemId)
    {
        EnsureFloorObjectsInitialized();

        if (BlocksFloorObjectStacking())
        {
            return 0;
        }

        int capacity = 0;
        int maxPerStack = Mathf.Max(1, maxFloorObjectsPerStack);
        for (int i = 0; i < floorStacks.Count; i++)
        {
            List<PortableObject> stack = floorStacks[i];
            if (stack == null)
            {
                continue;
            }

            if (itemId >= 0 && !IsStackCompatible(stack, itemId) && stack.Count > 0)
            {
                continue;
            }

            capacity += Mathf.Max(0, maxPerStack - stack.Count);
        }

        return capacity;
    }

    private bool BlocksFloorObjectStacking()
    {
        if (mapObject is InstallationObject installationObject
            && installationObject != null
            && installationObject.gameObject != null
            && installationObject.gameObject.activeInHierarchy)
        {
            return true;
        }

        return InputOutputModuleEnergyAreaController.CoordinateIsEnergyArea(coordinate)
               || InputOutputModuleItemAreaController.CoordinateIsItemArea(coordinate)
               || InputOutputModuleOutputAreaController.CoordinateIsOutputArea(coordinate);
    }

    private bool ResolveFloorObjectPool()
    {
        if (floorObjectPool != null)
        {
            return true;
        }

        TerrainGenerator generator = GetComponentInParent<TerrainGenerator>();
        GameObject host = generator != null ? generator.gameObject : gameObject;
        floorObjectPool = host.GetComponent<PortableObjectPool>();

        if (floorObjectPool == null)
        {
            floorObjectPool = host.AddComponent<PortableObjectPool>();
        }

        floorObjectPool.Configure(floorObjectPrefab);
        return floorObjectPool != null;
    }

    private void ReleaseFloorObjectToBag(PortableObject floorObject, PortableObject bagTarget)
    {
        if (floorObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = floorObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        if (!ResolveFloorObjectPool() || floorObjectPool == null)
        {
            floorObject.SetBatchedRendering(false);
            floorObject.gameObject.SetActive(false);
            return;
        }

        floorObject.SetBatchedRendering(false);
        floorObject.transform.SetParent(null, true);

        if (bagTarget != null)
        {
            floorObject.MoveTo(bagTarget.transform, () => floorObjectPool.Release(floorObject));
        }
        else
        {
            floorObjectPool.Release(floorObject);
        }
    }

    private void ReleaseFloorObject(PortableObject floorObject)
    {
        if (floorObject == null)
        {
            return;
        }

        if (!ResolveFloorObjectPool() || floorObjectPool == null)
        {
            floorObject.SetBatchedRendering(false);
            floorObject.gameObject.SetActive(false);
            return;
        }

        floorObject.SetBatchedRendering(false);
        floorObject.transform.SetParent(null, true);
        floorObjectPool.Release(floorObject);
    }

    private void ReleaseFloorObjectToHand(PortableObject floorObject, PortableObject handTarget)
    {
        if (floorObject == null)
        {
            return;
        }

        DroppedItemPickupGate gate = floorObject.GetComponent<DroppedItemPickupGate>();
        gate?.ClearGate();

        if (!ResolveFloorObjectPool() || floorObjectPool == null)
        {
            floorObject.SetBatchedRendering(false);
            floorObject.gameObject.SetActive(false);
            return;
        }

        floorObject.SetBatchedRendering(false);
        floorObject.transform.SetParent(null, true);

        if (handTarget != null)
        {
            floorObject.MoveTo(handTarget.transform, () => floorObjectPool.Release(floorObject));
        }
        else
        {
            floorObjectPool.Release(floorObject);
        }
    }

    private void ConfigureFloorObjectTransform(PortableObject portableObject, Transform anchor, int stackIndex)
    {
        portableObject.transform.SetParent(anchor, false);
        portableObject.transform.localPosition = new Vector3(0f, stackIndex * floorObjectVerticalSpacing, 0f);
        portableObject.transform.localRotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);
    }

    private void ConfigureInputAreaCenterObjectTransform(PortableObject portableObject, int stackIndex)
    {
        if (portableObject == null || inputAreaCenterAnchor == null)
        {
            return;
        }

        portableObject.transform.SetParent(inputAreaCenterAnchor, false);
        portableObject.transform.localPosition = new Vector3(0f, stackIndex * InputAreaCenterVerticalSpacing, 0f);
        portableObject.transform.localRotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);
    }

    private bool IsStackCompatible(List<PortableObject> stack, int objectId)
    {
        if (stack == null)
        {
            return false;
        }

        if (stack.Count == 0)
        {
            return true;
        }

        PortableObject bottom = stack[0];
        if (bottom == null)
        {
            stack.Clear();
            return true;
        }

        return bottom.ItemId == objectId;
    }

    public bool TryAddInputAreaCenterObject(int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

        if (objectId < 0 || !ResolveFloorObjectPool())
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null
            || inputAreaCenterStack.Count >= ResolveInputAreaCenterCapacity()
            || !IsStackCompatible(inputAreaCenterStack, objectId))
        {
            return false;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetItem(objectId);
        ConfigureInputAreaCenterObjectTransform(portableObject, inputAreaCenterStack.Count);
        portableObject.SetBatchedRendering(true);
        inputAreaCenterStack.Add(portableObject);
        targetPortableObject = portableObject;
        return true;
    }

    public bool TryPickupOneInputAreaCenterObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex)
    {
        if (player == null || pickupRadius <= 0f || inputAreaCenterStack.Count == 0 || IsClosedBoxContentPickupBlocked())
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        UpdatePickupGates(inputAreaCenterStack, gateOriginPosition);

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        Vector3 offset = inputAreaCenterAnchor.position - playerPosition;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (distanceSqr > pickupRadiusSqr)
        {
            return false;
        }

        DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
        if (topGate != null && !topGate.CanManualPickup(distanceSqr, pickupRadiusSqr))
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            return false;
        }

        bool added;
        PortableObject bagTarget;
        if (preferredSlotIndex >= 0)
        {
            added = player.TryAddToBagAtSlot(preferredSlotIndex, itemId, out bagTarget);
        }
        else
        {
            added = player.TryAddToBag(itemId, out bagTarget);
        }

        if (!added)
        {
            return false;
        }

        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
        ReleaseFloorObjectToBag(topObject, bagTarget);
        return true;
    }

    public bool TryPickupOneInputAreaCenterObjectToHand(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (player == null || pickupRadius <= 0f || inputAreaCenterStack.Count == 0 || IsClosedBoxContentPickupBlocked())
        {
            return false;
        }

        EnsureInputAreaCenterAnchorInitialized();
        if (inputAreaCenterAnchor == null)
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        UpdatePickupGates(inputAreaCenterStack, gateOriginPosition);

        PortableObject topObject = GetTopPortableObject(inputAreaCenterStack);
        if (topObject == null)
        {
            return false;
        }

        Vector3 offset = inputAreaCenterAnchor.position - playerPosition;
        offset.y = 0f;
        float distanceSqr = offset.sqrMagnitude;
        if (distanceSqr > pickupRadiusSqr)
        {
            return false;
        }

        DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
        if (topGate != null && !topGate.CanManualPickup(distanceSqr, pickupRadiusSqr))
        {
            return false;
        }

        int itemId = topObject.ItemId;
        if (itemId < 0)
        {
            inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
            ReleaseFloorObject(topObject);
            return false;
        }

        if (!player.TryAddToHand(itemId, out PortableObject handTarget))
        {
            return false;
        }

        inputAreaCenterStack.RemoveAt(inputAreaCenterStack.Count - 1);
        ReleaseFloorObjectToHand(topObject, handTarget);
        return true;
    }

    private bool IsClosedBoxContentPickupBlocked()
    {
        return mapObject is BoxObject boxObject && !boxObject.IsOpen;
    }

    private void EnsureInputAreaCenterAnchorInitialized()
    {
        CacheChildReferences();
        if (inputAreaCenterAnchor != null)
        {
            return;
        }

        Transform existingAnchor = transform.Find("InputAreaCenterAnchor");
        if (existingAnchor != null)
        {
            inputAreaCenterAnchor = existingAnchor;
        }
        else
        {
            GameObject anchorObject = new GameObject("InputAreaCenterAnchor");
            inputAreaCenterAnchor = anchorObject.transform;
            inputAreaCenterAnchor.SetParent(transform, false);
        }

        Vector3 localPosition = inputAreaCenterAnchor.localPosition;
        localPosition.x = 0f;
        localPosition.z = 0f;
        localPosition.y = ResolveInputAreaCenterHeight();
        inputAreaCenterAnchor.localPosition = localPosition;
        inputAreaCenterAnchor.localRotation = Quaternion.identity;
        inputAreaCenterAnchor.localScale = Vector3.one;
    }

    private float ResolveInputAreaCenterHeight()
    {
        CacheChildReferences();
        return cachedInputAreaCenterHeight;
    }

    private int ResolveInputAreaCenterCapacity()
    {
        if (TryGetInstalledItemAreaCapacity(out int capacity))
        {
            return capacity;
        }

        return Mathf.Max(1, inputAreaCenterMaxObjects);
    }

    private ItemDefinition ResolveInstalledItemAreaDefinition()
    {
        if (mapObject == null)
        {
            return null;
        }

        int itemId = mapObject.ResolveItemId();
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
                return definition;
            }
        }

        return null;
    }

    private static PortableObject GetTopPortableObject(List<PortableObject> stack)
    {
        CleanupPortableStack(stack);
        return stack != null && stack.Count > 0 ? stack[stack.Count - 1] : null;
    }

    private static void CleanupPortableStack(List<PortableObject> stack)
    {
        if (stack == null)
        {
            return;
        }

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i] == null)
            {
                stack.RemoveAt(i);
            }
        }
    }

    private static void UpdatePickupGates(List<PortableObject> stack, Vector3 gateOriginPosition)
    {
        if (stack == null)
        {
            return;
        }

        for (int i = 0; i < stack.Count; i++)
        {
            PortableObject portableObject = stack[i];
            if (portableObject == null)
            {
                continue;
            }

            DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
            if (gate != null)
            {
                gate.UpdateExitState(gateOriginPosition);
            }
        }
    }

    private void CacheChildReferences()
    {
        if (childReferencesCached)
        {
            return;
        }

        if (body == null)
        {
            Transform bodyTransform = transform.Find("Body");
            if (bodyTransform != null)
            {
                body = bodyTransform;
            }
        }

        cachedBodyRenderers = body != null
            ? body.GetComponentsInChildren<MeshRenderer>(true)
            : Array.Empty<MeshRenderer>();

        if (inputAreaCenterAnchor == null)
        {
            Transform existingAnchor = transform.Find("InputAreaCenterAnchor");
            if (existingAnchor != null)
            {
                inputAreaCenterAnchor = existingAnchor;
            }
        }

        cachedInputAreaCenterHeight = 0f;
        if (floorObjects != null)
        {
            for (int i = 0; i < floorObjects.Count; i++)
            {
                Transform anchor = floorObjects[i];
                if (anchor == null)
                {
                    continue;
                }

                cachedInputAreaCenterHeight = transform.InverseTransformPoint(anchor.position).y;
                break;
            }
        }

        childReferencesCached = true;
    }
}
