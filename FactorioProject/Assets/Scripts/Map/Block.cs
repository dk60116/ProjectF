using ProjectF.Attributes;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Block : BaseObject
{
    public enum BlockType { Ground, Water }
    private const int InputAreaCenterStackStateSentinel = -1000000001;
    private const int ConveyorStackStateSentinel = -1000000002;
    private const float InputAreaCenterVerticalSpacing = 0.05f;
    private const int ConveyorStackLaneLimit = 4;
    private const float ConveyorLaneHeight = 0.2f;
    private const float ConveyorLaneSettleEpsilon = 0.01f;
    private const float ConveyorLaneSpacingScale = 0.92f;
    private const float ConveyorCornerCenterRadius = 0.35f;
    private const float ConveyorCornerArcEndInsetDegrees = 20f;
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
    private readonly List<PortableObject> conveyorStack = new List<PortableObject>();
    private readonly Dictionary<PortableObject, ConveyorCornerMotionState> conveyorCornerMotionStates = new Dictionary<PortableObject, ConveyorCornerMotionState>();
    private PortableObjectPool floorObjectPool;
    private TerrainGenerator cachedTerrainGenerator;
    private Transform inputAreaCenterAnchor;
    private MeshRenderer[] cachedBodyRenderers = Array.Empty<MeshRenderer>();
    private float cachedInputAreaCenterHeight;
    private bool childReferencesCached;

    private struct ConveyorCornerMotionState
    {
        public int sourceLaneIndex;
        public int destinationLaneIndex;
        public float progress;
    }

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

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        UpdateConveyorObjects();
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
            if (!TryGetBestFloorStackIndex(objectId, requireExisting, ResolveDefaultFloorDropReferenceWorldPosition(), out int stackIndex))
            {
                continue;
            }

            Transform anchor = floorObjects[stackIndex];
            List<PortableObject> stack = floorStacks[stackIndex];
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

    public bool TryAddInputAreaCenterObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null, Func<Vector3> startWorldPositionProvider = null)
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
        portableObject.transform.position = startWorldPositionProvider != null ? startWorldPositionProvider() : startWorldPosition;
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

        portableObject.MoveTo(() => inputAreaCenterAnchor != null ? inputAreaCenterAnchor.TransformPoint(finalLocalPosition) : finalWorldPosition, delay, startWorldPositionProvider, () =>
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

    public bool TryAddFloorObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null, Func<Vector3> startWorldPositionProvider = null)
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
            if (!TryGetBestFloorStackIndex(objectId, requireExisting, startWorldPosition, out int stackIndex))
            {
                continue;
            }

            Transform anchor = floorObjects[stackIndex];
            List<PortableObject> stack = floorStacks[stackIndex];
            PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
            if (portableObject == null)
            {
                continue;
            }

            portableObject.SetItem(objectId);
            portableObject.SetBatchedRendering(false);
            portableObject.transform.SetParent(anchor, true);
            portableObject.transform.position = startWorldPositionProvider != null ? startWorldPositionProvider() : startWorldPosition;
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

            portableObject.MoveTo(() => anchor != null ? anchor.TransformPoint(finalLocalPosition) : finalWorldPosition, delay, startWorldPositionProvider, () =>
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

        return false;
    }

    public bool IsConveyorStackingEnabled()
    {
        return mapObject is ConveyorBelt conveyorBelt
               && conveyorBelt != null
               && conveyorBelt.gameObject != null
               && conveyorBelt.gameObject.activeInHierarchy;
    }

    public bool TryGetConveyorStandingDistanceSqr(Vector3 worldPosition, out float distanceSqr)
    {
        distanceSqr = float.MaxValue;
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        if (IsCornerConveyor())
        {
            return TryGetCornerConveyorStandingDistanceSqr(worldPosition, out distanceSqr);
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 flatLocalPosition = new Vector2(localPosition3.x, localPosition3.z);
        if (!TryGetConveyorLocalAxes(out Vector2 localFlowAxis, out Vector2 localRightAxis))
        {
            localFlowAxis = Vector2.up;
            localRightAxis = Vector2.right;
        }

        float forward = Vector2.Dot(flatLocalPosition, localFlowAxis);
        float right = Vector2.Dot(flatLocalPosition, localRightAxis);
        float clampedForward = Mathf.Clamp(forward, -0.5f, 0.5f);
        float halfWidth = Mathf.Max(0.18f, GetConveyorLaneHalfExtent() + 0.12f);
        float clampedRight = Mathf.Clamp(right, -halfWidth, halfWidth);
        Vector2 delta = new Vector2(forward - clampedForward, right - clampedRight);
        distanceSqr = delta.sqrMagnitude;
        return true;
    }

    public bool TryGetConveyorCarryVelocity(Vector3 worldPosition, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        float conveyorSpeed = GetConveyorSpeed();
        if (conveyorSpeed <= 0f)
        {
            return false;
        }

        if (IsCornerConveyor())
        {
            float sampleDeltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            if (!TryGetConveyorCarryDelta(worldPosition, sampleDeltaTime, out Vector3 carryDelta))
            {
                return false;
            }

            velocity = carryDelta / sampleDeltaTime;
            velocity.y = 0f;
            return velocity.sqrMagnitude > 0.0001f;
        }

        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            return false;
        }

        Vector3 carryDirection = new Vector3(flowDirection.x, 0f, flowDirection.y);
        carryDirection.y = 0f;
        if (carryDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        velocity = carryDirection.normalized * conveyorSpeed;
        return true;
    }

    public bool TryGetConveyorCarryDelta(Vector3 worldPosition, float deltaTime, out Vector3 delta)
    {
        delta = Vector3.zero;
        if (!IsConveyorStackingEnabled() || deltaTime <= 0f)
        {
            return false;
        }

        float conveyorSpeed = GetConveyorSpeed();
        if (conveyorSpeed <= 0f)
        {
            return false;
        }

        if (IsCornerConveyor())
        {
            return TryGetCornerConveyorCarryDelta(worldPosition, conveyorSpeed, deltaTime, out delta);
        }

        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            return false;
        }

        Vector3 carryDirection = new Vector3(flowDirection.x, 0f, flowDirection.y).normalized;
        delta = carryDirection * conveyorSpeed * deltaTime;
        return delta.sqrMagnitude > 0.0000001f;
    }

    public bool TryGetConveyorCarryDeltaWithHandoff(Vector3 worldPosition, float deltaTime, out Block resultingBlock, out Vector3 delta)
    {
        resultingBlock = this;
        delta = Vector3.zero;
        if (!IsConveyorStackingEnabled() || deltaTime <= 0f)
        {
            return false;
        }

        float conveyorSpeed = GetConveyorSpeed();
        if (conveyorSpeed <= 0f)
        {
            return false;
        }

        if (!IsCornerConveyor())
        {
            if (!TryGetConveyorCarryDelta(worldPosition, deltaTime, out delta))
            {
                return false;
            }

            resultingBlock = this;
            return true;
        }

        return TryGetCornerConveyorCarryDeltaWithHandoff(worldPosition, conveyorSpeed, deltaTime, out resultingBlock, out delta);
    }

    public bool TryGetNextConnectedConveyorBlock(out Block nextBlock)
    {
        return TryGetNextConveyorBlock(out nextBlock);
    }

    public bool CanAddConveyorObjects(int count)
    {
        if (count <= 0)
        {
            return true;
        }

        CleanupConveyorStack();
        return IsConveyorStackingEnabled() && GetAvailableConveyorCapacity() >= count;
    }

    public int GetAvailableConveyorCapacity()
    {
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return 0;
        }

        int capacity = 0;
        int laneCount = GetConveyorLaneCount();
        for (int i = 0; i < laneCount; i++)
        {
            if (conveyorStack[i] == null)
            {
                capacity++;
            }
        }

        return capacity;
    }

    public bool TryAddConveyorObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject, Action onComplete = null, Func<Vector3> startWorldPositionProvider = null)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        if (objectId < 0
            || !IsConveyorStackingEnabled()
            || !ResolveFloorObjectPool()
            || !TryGetBestConveyorLaneIndex(startWorldPosition, out int laneIndex))
        {
            return false;
        }

        Transform anchor = floorObjects != null && laneIndex < floorObjects.Count ? floorObjects[laneIndex] : null;
        if (anchor == null)
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
        portableObject.transform.SetParent(transform, true);
        portableObject.transform.position = startWorldPositionProvider != null ? startWorldPositionProvider() : startWorldPosition;
        portableObject.transform.rotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        portableObject.gameObject.SetActive(true);
        conveyorStack[laneIndex] = portableObject;

        DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
        if (gate == null)
        {
            gate = portableObject.gameObject.AddComponent<DroppedItemPickupGate>();
        }

        Vector3 finalWorldPosition = GetConveyorLaneWorldPosition(laneIndex, anchor);
        portableObject.MoveTo(() => anchor != null ? GetConveyorLaneWorldPosition(laneIndex, anchor) : finalWorldPosition, delay, startWorldPositionProvider, () =>
        {
            if (portableObject == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (anchor != null)
            {
                ConfigureConveyorObjectTransform(portableObject, laneIndex, anchor);
            }

            portableObject.SetBatchedRendering(true);
            gate?.MarkSettled();
            onComplete?.Invoke();
        }, false);

        targetPortableObject = portableObject;
        return true;
    }

    public bool TryPickupOneConveyorObjectToBag(Player player, Vector3 playerPosition, float pickupRadius, int preferredSlotIndex = -1)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        int laneIndex = FindBestConveyorPickupLaneIndex(playerPosition, pickupRadiusSqr, gateOriginPosition, true);
        if (laneIndex < 0)
        {
            return false;
        }

        PortableObject targetObject = conveyorStack[laneIndex];
        int itemId = targetObject != null ? targetObject.ItemId : -1;
        if (itemId < 0)
        {
            conveyorStack[laneIndex] = null;
            ReleaseFloorObject(targetObject);
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

        conveyorStack[laneIndex] = null;
        ReleaseFloorObjectToBag(targetObject, bagTarget);
        return true;
    }

    public bool TryPickupOneConveyorObjectToHand(Player player, Vector3 playerPosition, float pickupRadius)
    {
        if (player == null || pickupRadius <= 0f)
        {
            return false;
        }

        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return false;
        }

        float pickupRadiusSqr = pickupRadius * pickupRadius;
        Vector3 gateOriginPosition = player.transform.position;
        int laneIndex = FindBestConveyorPickupLaneIndex(playerPosition, pickupRadiusSqr, gateOriginPosition, true);
        if (laneIndex < 0)
        {
            return false;
        }

        PortableObject targetObject = conveyorStack[laneIndex];
        int itemId = targetObject != null ? targetObject.ItemId : -1;
        if (itemId < 0)
        {
            conveyorStack[laneIndex] = null;
            ReleaseFloorObject(targetObject);
            return false;
        }

        if (!player.TryAddToHand(itemId, out PortableObject handTarget))
        {
            return false;
        }

        conveyorStack[laneIndex] = null;
        ReleaseFloorObjectToHand(targetObject, handTarget);
        return true;
    }

    private bool TrySetConveyorObjectAtLane(int laneIndex, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

        if (objectId < 0
            || !IsConveyorStackingEnabled()
            || !ResolveFloorObjectPool()
            || laneIndex < 0
            || laneIndex >= GetConveyorLaneCount()
            || conveyorStack[laneIndex] != null)
        {
            return false;
        }

        Transform anchor = floorObjects != null && laneIndex < floorObjects.Count ? floorObjects[laneIndex] : null;
        if (anchor == null)
        {
            return false;
        }

        PortableObject portableObject = floorObjectPool.Get(floorObjectPrefab);
        if (portableObject == null)
        {
            return false;
        }

        portableObject.SetItem(objectId);
        ConfigureConveyorObjectTransform(portableObject, laneIndex, anchor);
        portableObject.SetBatchedRendering(true);
        conveyorStack[laneIndex] = portableObject;
        targetPortableObject = portableObject;
        return true;
    }

    public List<int> CaptureFloorObjectState()
    {
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();

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

        bool hasConveyorObjects = false;
        int conveyorLaneCount = GetConveyorLaneCount();
        for (int i = 0; i < conveyorLaneCount; i++)
        {
            if (conveyorStack[i] != null)
            {
                hasConveyorObjects = true;
                break;
            }
        }

        if (hasConveyorObjects)
        {
            itemIds.Add(ConveyorStackStateSentinel);
            itemIds.Add(conveyorLaneCount);
            for (int i = 0; i < conveyorLaneCount; i++)
            {
                itemIds.Add(conveyorStack[i] != null ? conveyorStack[i].ItemId : -1);
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

            if (itemId == ConveyorStackStateSentinel)
            {
                if (i + 1 >= itemIds.Count)
                {
                    break;
                }

                int laneCount = Mathf.Max(0, itemIds[++i]);
                for (int laneIndex = 0; laneIndex < laneCount && i + 1 < itemIds.Count; laneIndex++)
                {
                    int laneItemId = itemIds[++i];
                    if (laneItemId < 0)
                    {
                        continue;
                    }

                    TrySetConveyorObjectAtLane(laneIndex, laneItemId, out _);
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
        if (this == null)
        {
            return;
        }

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

        int conveyorLaneCount = GetConveyorLaneCount();
        while (conveyorStack.Count < conveyorLaneCount)
        {
            conveyorStack.Add(null);
        }

        while (conveyorStack.Count > conveyorLaneCount)
        {
            conveyorStack.RemoveAt(conveyorStack.Count - 1);
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
            conveyorStack.Clear();
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

        for (int i = 0; i < conveyorStack.Count; i++)
        {
            PortableObject portableObject = conveyorStack[i];
            if (portableObject != null)
            {
                floorObjectPool.Release(portableObject);
            }
        }

        conveyorStack.Clear();
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

    private bool TryGetBestFloorStackIndex(int objectId, bool requireExisting, Vector3 referenceWorldPosition, out int bestStackIndex)
    {
        bestStackIndex = -1;
        float bestDistanceSqr = float.MaxValue;

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

            Vector3 offset = GetConveyorLaneWorldPosition(stackIndex, anchor) - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (bestStackIndex >= 0 && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestStackIndex = stackIndex;
        }

        return bestStackIndex >= 0;
    }

    private Vector3 ResolveDefaultFloorDropReferenceWorldPosition()
    {
        Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player != null)
        {
            Transform bodyTransform = player.BodyTransform != null ? player.BodyTransform : player.transform;
            if (bodyTransform != null)
            {
                return bodyTransform.position;
            }
        }

        return transform.position;
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

    private void ConfigureConveyorObjectTransform(PortableObject portableObject, int laneIndex, Transform anchor)
    {
        if (portableObject == null || anchor == null)
        {
            return;
        }

        portableObject.transform.SetParent(transform, true);
        portableObject.transform.position = GetConveyorLaneWorldPosition(laneIndex, anchor);
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

    private int GetConveyorLaneCount()
    {
        return Mathf.Min(ConveyorStackLaneLimit, floorObjects != null ? floorObjects.Count : 0);
    }

    private void CleanupConveyorStack()
    {
        EnsureFloorObjectsInitialized();

        if (conveyorCornerMotionStates.Count == 0)
        {
            return;
        }

        List<PortableObject> staleObjects = null;
        foreach (KeyValuePair<PortableObject, ConveyorCornerMotionState> pair in conveyorCornerMotionStates)
        {
            PortableObject portableObject = pair.Key;
            if (portableObject == null || !ContainsPortableObjectInConveyorStack(portableObject))
            {
                staleObjects ??= new List<PortableObject>();
                staleObjects.Add(portableObject);
            }
        }

        if (staleObjects == null)
        {
            return;
        }

        for (int i = 0; i < staleObjects.Count; i++)
        {
            conveyorCornerMotionStates.Remove(staleObjects[i]);
        }
    }

    private bool ContainsPortableObjectInConveyorStack(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return false;
        }

        for (int laneIndex = 0; laneIndex < conveyorStack.Count; laneIndex++)
        {
            if (conveyorStack[laneIndex] == portableObject)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetBestConveyorLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        if (TryGetConveyorLaneLayout(out _, out _, out int backColumn0LaneIndex, out int backColumn1LaneIndex)
            && TryGetBestConveyorLaneIndexFromCandidates(
                referenceWorldPosition,
                new[] { backColumn0LaneIndex, backColumn1LaneIndex },
                out bestLaneIndex))
        {
            return true;
        }

        int laneCount = GetConveyorLaneCount();
        int[] allLaneIndices = new int[laneCount];
        for (int i = 0; i < laneCount; i++)
        {
            allLaneIndices[i] = i;
        }

        return TryGetBestConveyorLaneIndexFromCandidates(referenceWorldPosition, allLaneIndices, out bestLaneIndex);
    }

    private int FindBestConveyorPickupLaneIndex(Vector3 playerPosition, float pickupRadiusSqr, Vector3 gateOriginPosition, bool manualPickup)
    {
        float bestDistanceSqr = float.MaxValue;
        int bestLaneIndex = -1;
        int laneCount = GetConveyorLaneCount();

        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            PortableObject portableObject = conveyorStack[laneIndex];
            Transform anchor = floorObjects != null && laneIndex < floorObjects.Count ? floorObjects[laneIndex] : null;
            if (portableObject == null || anchor == null)
            {
                continue;
            }

            DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
            if (gate != null)
            {
                gate.UpdateExitState(gateOriginPosition);
            }

            Vector3 offset = GetConveyorLaneWorldPosition(laneIndex, anchor) - playerPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > pickupRadiusSqr || distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            if (gate != null)
            {
                bool canPickup = manualPickup
                    ? gate.CanManualPickup(distanceSqr, pickupRadiusSqr)
                    : gate.CanPickup(distanceSqr, pickupRadiusSqr);
                if (!canPickup)
                {
                    continue;
                }
            }

            bestDistanceSqr = distanceSqr;
            bestLaneIndex = laneIndex;
        }

        return bestLaneIndex;
    }

    private void UpdateConveyorObjects()
    {
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled() || !HasAnyConveyorObjects())
        {
            return;
        }

        float conveyorSpeed = GetConveyorSpeed();
        UpdateConveyorObjectWorldPositions(conveyorSpeed, Time.deltaTime);
        if (IsCornerConveyor())
        {
            TryAdvanceConveyorFrontLane(0);
            TryAdvanceConveyorFrontLane(1);
            if (TryGetConveyorCornerLaneCandidates(
                    out int outerSourceLaneIndex,
                    out int outerDestinationLaneIndex,
                    out int innerSourceLaneIndex,
                    out int innerDestinationLaneIndex))
            {
                TryShiftConveyorLane(outerSourceLaneIndex, outerDestinationLaneIndex);
                TryShiftConveyorLane(innerSourceLaneIndex, innerDestinationLaneIndex);
            }
            return;
        }

        if (!TryGetConveyorLaneLayout(out int frontColumn0LaneIndex, out int frontColumn1LaneIndex, out int backColumn0LaneIndex, out int backColumn1LaneIndex))
        {
            return;
        }

        TryAdvanceConveyorFrontLane(frontColumn0LaneIndex);
        TryAdvanceConveyorFrontLane(frontColumn1LaneIndex);
        TryShiftConveyorLane(backColumn0LaneIndex, frontColumn0LaneIndex);
        TryShiftConveyorLane(backColumn1LaneIndex, frontColumn1LaneIndex);
    }

    private bool HasAnyConveyorObjects()
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            if (conveyorStack[laneIndex] != null)
            {
                return true;
            }
        }

        return false;
    }

    private float GetConveyorSpeed()
    {
        return mapObject is ConveyorBelt conveyorBelt ? conveyorBelt.ConveyorSpeed : 0f;
    }

    private void UpdateConveyorObjectWorldPositions(float conveyorSpeed, float deltaTime)
    {
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            PortableObject portableObject = conveyorStack[laneIndex];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.transform.parent != transform)
            {
                portableObject.transform.SetParent(transform, true);
            }

            if (portableObject.IsMovingToTarget)
            {
                continue;
            }

            if (TryUpdateCornerConveyorObjectWorldPosition(laneIndex, portableObject, conveyorSpeed, deltaTime))
            {
                continue;
            }

            Vector3 targetPosition = GetConveyorLaneWorldPosition(laneIndex);
            if (conveyorSpeed <= 0f || deltaTime <= 0f)
            {
                portableObject.transform.position = targetPosition;
                continue;
            }

            portableObject.transform.position = Vector3.MoveTowards(
                portableObject.transform.position,
                targetPosition,
                conveyorSpeed * deltaTime);
        }
    }

    private bool TryUpdateCornerConveyorObjectWorldPosition(int laneIndex, PortableObject portableObject, float conveyorSpeed, float deltaTime)
    {
        if (!IsCornerConveyor() || portableObject == null || !conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState motionState))
        {
            return false;
        }

        float pathLength = GetConveyorCornerPathLength(motionState.sourceLaneIndex, motionState.destinationLaneIndex);
        if (pathLength <= 0.0001f || conveyorSpeed <= 0f || deltaTime <= 0f)
        {
            portableObject.transform.position = EvaluateConveyorCornerPathWorldPosition(motionState.sourceLaneIndex, motionState.destinationLaneIndex, 1f);
            conveyorCornerMotionStates.Remove(portableObject);
            return true;
        }

        motionState.progress = Mathf.Clamp01(motionState.progress + ((conveyorSpeed * deltaTime) / pathLength));
        portableObject.transform.position = EvaluateConveyorCornerPathWorldPosition(motionState.sourceLaneIndex, motionState.destinationLaneIndex, motionState.progress);
        if (motionState.progress >= 1f - 0.0001f)
        {
            conveyorCornerMotionStates.Remove(portableObject);
        }
        else
        {
            conveyorCornerMotionStates[portableObject] = motionState;
        }

        return true;
    }

    private bool TryAdvanceConveyorFrontLane(int laneIndex)
    {
        PortableObject portableObject = laneIndex >= 0 && laneIndex < conveyorStack.Count
            ? conveyorStack[laneIndex]
            : null;
        if (portableObject == null || !IsConveyorObjectSettledAtLane(laneIndex, portableObject))
        {
            return false;
        }

        if (!TryGetNextConveyorBlock(out Block nextBlock))
        {
            return false;
        }

        if (!nextBlock.TryReceiveConveyorObject(portableObject, portableObject.transform.position, out _))
        {
            return false;
        }

        conveyorStack[laneIndex] = null;
        return true;
    }

    private bool TryShiftConveyorLane(int sourceLaneIndex, int destinationLaneIndex)
    {
        if (sourceLaneIndex < 0
            || destinationLaneIndex < 0
            || sourceLaneIndex >= conveyorStack.Count
            || destinationLaneIndex >= conveyorStack.Count
            || conveyorStack[destinationLaneIndex] != null)
        {
            return false;
        }

        PortableObject portableObject = conveyorStack[sourceLaneIndex];
        if (portableObject == null || !IsConveyorObjectSettledAtLane(sourceLaneIndex, portableObject))
        {
            return false;
        }

        if (IsCornerConveyor())
        {
            conveyorCornerMotionStates[portableObject] = new ConveyorCornerMotionState
            {
                sourceLaneIndex = sourceLaneIndex,
                destinationLaneIndex = destinationLaneIndex,
                progress = 0f
            };
        }

        conveyorStack[destinationLaneIndex] = portableObject;
        conveyorStack[sourceLaneIndex] = null;
        return true;
    }

    private bool IsConveyorObjectSettledAtLane(int laneIndex, PortableObject portableObject)
    {
        if (portableObject == null || portableObject.IsMovingToTarget)
        {
            return false;
        }

        if (conveyorCornerMotionStates.TryGetValue(portableObject, out ConveyorCornerMotionState motionState))
        {
            return motionState.destinationLaneIndex == laneIndex && motionState.progress >= 1f - 0.0001f;
        }

        Vector3 targetPosition = GetConveyorLaneWorldPosition(laneIndex);
        Vector3 delta = portableObject.transform.position - targetPosition;
        delta.y = 0f;
        return delta.sqrMagnitude <= ConveyorLaneSettleEpsilon * ConveyorLaneSettleEpsilon;
    }

    private bool TryGetNextConveyorBlock(out Block nextBlock)
    {
        nextBlock = null;
        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            return false;
        }

        if (!TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator))
        {
            return false;
        }

        Vector2Int nextCoordinate = coordinate + flowDirection;
        if (!terrainGenerator.TryGetLoadedBlock(nextCoordinate, out nextBlock) || nextBlock == null || nextBlock == this)
        {
            return false;
        }

        return nextBlock.IsConveyorStackingEnabled();
    }

    private bool TryResolveOwningTerrainGenerator(out TerrainGenerator terrainGenerator)
    {
        terrainGenerator = cachedTerrainGenerator;
        if (terrainGenerator != null)
        {
            return true;
        }

        cachedTerrainGenerator = GetComponentInParent<TerrainGenerator>();
        terrainGenerator = cachedTerrainGenerator;
        return terrainGenerator != null;
    }

    private bool TryGetConveyorFlowDirection(out Vector2Int flowDirection)
    {
        flowDirection = Vector2Int.zero;
        if (!(mapObject is ConveyorBelt conveyorBelt) || conveyorBelt == null)
        {
            return false;
        }

        return conveyorBelt.TryGetOutputDirection(conveyorBelt.transform.rotation, out flowDirection);
    }

    private bool TryReceiveConveyorObject(PortableObject portableObject, Vector3 sourceWorldPosition, out int laneIndex)
    {
        laneIndex = -1;
        if (portableObject == null)
        {
            return false;
        }

        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled() || !TryGetBestConveyorReceiveLaneIndex(sourceWorldPosition, out laneIndex))
        {
            return false;
        }

        portableObject.transform.SetParent(transform, true);
        portableObject.gameObject.SetActive(true);
        portableObject.SetBatchedRendering(true);
        conveyorStack[laneIndex] = portableObject;

        DroppedItemPickupGate gate = portableObject.GetComponent<DroppedItemPickupGate>();
        gate?.MarkSettled();
        conveyorCornerMotionStates.Remove(portableObject);
        return true;
    }

    private bool TryGetBestConveyorReceiveLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        if (IsCornerConveyor())
        {
            return TryGetBestCornerConveyorReceiveLaneIndex(referenceWorldPosition, out bestLaneIndex);
        }

        if (!TryGetPreferredConveyorColumn(referenceWorldPosition, out int preferredColumn))
        {
            return false;
        }

        if (!TryGetConveyorLaneLayout(out _, out _, out int backColumn0LaneIndex, out int backColumn1LaneIndex))
        {
            return false;
        }

        return TryGetBestConveyorLaneIndexFromColumn(
            referenceWorldPosition,
            new[] { backColumn0LaneIndex, backColumn1LaneIndex },
            preferredColumn,
            out bestLaneIndex);
    }

    private bool TryGetBestConveyorLaneIndexFromCandidates(Vector3 referenceWorldPosition, IReadOnlyList<int> candidateLaneIndices, out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        float bestDistanceSqr = float.MaxValue;
        if (candidateLaneIndices == null)
        {
            return false;
        }

        for (int i = 0; i < candidateLaneIndices.Count; i++)
        {
            int laneIndex = candidateLaneIndices[i];
            if (laneIndex < 0 || laneIndex >= conveyorStack.Count || conveyorStack[laneIndex] != null)
            {
                continue;
            }

            Vector3 offset = GetConveyorLaneWorldPosition(laneIndex) - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (bestLaneIndex >= 0 && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestLaneIndex = laneIndex;
        }

        return bestLaneIndex >= 0;
    }

    private bool TryGetBestConveyorLaneIndexFromColumn(Vector3 referenceWorldPosition, IReadOnlyList<int> candidateLaneIndices, int preferredColumn, out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        if (candidateLaneIndices == null)
        {
            return false;
        }

        if (!TryGetConveyorLaneLayout(out int frontColumn0LaneIndex, out int frontColumn1LaneIndex, out int backColumn0LaneIndex, out int backColumn1LaneIndex))
        {
            return false;
        }

        int[] preferredLaneIndices = preferredColumn == 0
            ? new[] { frontColumn0LaneIndex, backColumn0LaneIndex }
            : new[] { frontColumn1LaneIndex, backColumn1LaneIndex };

        float bestDistanceSqr = float.MaxValue;
        for (int i = 0; i < candidateLaneIndices.Count; i++)
        {
            int laneIndex = candidateLaneIndices[i];
            if (!ContainsLane(preferredLaneIndices, laneIndex)
                || laneIndex < 0
                || laneIndex >= conveyorStack.Count
                || conveyorStack[laneIndex] != null)
            {
                continue;
            }

            Vector3 offset = GetConveyorLaneWorldPosition(laneIndex) - referenceWorldPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (bestLaneIndex >= 0 && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestLaneIndex = laneIndex;
        }

        return bestLaneIndex >= 0;
    }

    private bool TryGetPreferredConveyorColumn(Vector3 referenceWorldPosition, out int preferredColumn)
    {
        preferredColumn = -1;
        Vector3 localReferencePosition = transform.InverseTransformPoint(referenceWorldPosition);
        if (!TryGetConveyorLocalAxes(out _, out Vector2 localRightAxis))
        {
            preferredColumn = localReferencePosition.x <= 0f ? 0 : 1;
            return true;
        }

        Vector2 flatReferencePosition = new Vector2(localReferencePosition.x, localReferencePosition.z);
        preferredColumn = Vector2.Dot(flatReferencePosition, localRightAxis) <= 0f ? 0 : 1;
        return true;
    }

    private static int GetConveyorLaneColumn(int laneIndex)
    {
        switch (laneIndex)
        {
            case 0:
            case 2:
                return 0;
            case 1:
            case 3:
                return 1;
            default:
                return -1;
        }
    }

    private bool TryGetConveyorLocalAxes(out Vector2 localFlowAxis, out Vector2 localRightAxis)
    {
        localFlowAxis = Vector2.zero;
        localRightAxis = Vector2.zero;
        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            return false;
        }

        Vector3 localFlowDirection3 = transform.InverseTransformDirection(new Vector3(flowDirection.x, 0f, flowDirection.y));
        Vector2 localFlowDirection = new Vector2(localFlowDirection3.x, localFlowDirection3.z);
        if (localFlowDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        localFlowAxis = localFlowDirection.normalized;
        localRightAxis = new Vector2(-localFlowAxis.y, localFlowAxis.x);
        return true;
    }

    private bool TryGetConveyorLaneLayout(
        out int frontColumn0LaneIndex,
        out int frontColumn1LaneIndex,
        out int backColumn0LaneIndex,
        out int backColumn1LaneIndex)
    {
        frontColumn0LaneIndex = -1;
        frontColumn1LaneIndex = -1;
        backColumn0LaneIndex = -1;
        backColumn1LaneIndex = -1;

        if (IsCornerConveyor())
        {
            frontColumn0LaneIndex = 0;
            frontColumn1LaneIndex = 1;
            backColumn0LaneIndex = 2;
            backColumn1LaneIndex = 3;
            return conveyorStack.Count >= 4;
        }

        int laneCount = GetConveyorLaneCount();
        if (laneCount < 4 || !TryGetConveyorLocalAxes(out Vector2 localFlowAxis, out Vector2 localRightAxis))
        {
            if (laneCount >= 4)
            {
                frontColumn0LaneIndex = 0;
                frontColumn1LaneIndex = 1;
                backColumn0LaneIndex = 2;
                backColumn1LaneIndex = 3;
                return true;
            }

            return false;
        }

        List<(int laneIndex, float forwardDot, float rightDot)> lanes = new List<(int, float, float)>(laneCount);
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            Vector3 localOffset3 = GetConveyorLaneLocalOffset(laneIndex);
            Vector2 localOffset = new Vector2(localOffset3.x, localOffset3.z);
            lanes.Add((
                laneIndex,
                Vector2.Dot(localOffset, localFlowAxis),
                Vector2.Dot(localOffset, localRightAxis)));
        }

        lanes.Sort((left, right) => right.forwardDot.CompareTo(left.forwardDot));
        List<(int laneIndex, float forwardDot, float rightDot)> frontLanes = new List<(int, float, float)> { lanes[0], lanes[1] };
        List<(int laneIndex, float forwardDot, float rightDot)> backLanes = new List<(int, float, float)> { lanes[2], lanes[3] };
        frontLanes.Sort((left, right) => left.rightDot.CompareTo(right.rightDot));
        backLanes.Sort((left, right) => left.rightDot.CompareTo(right.rightDot));

        frontColumn0LaneIndex = frontLanes[0].laneIndex;
        frontColumn1LaneIndex = frontLanes[1].laneIndex;
        backColumn0LaneIndex = backLanes[0].laneIndex;
        backColumn1LaneIndex = backLanes[1].laneIndex;
        return true;
    }

    private static bool ContainsLane(IReadOnlyList<int> laneIndices, int laneIndex)
    {
        if (laneIndices == null)
        {
            return false;
        }

        for (int i = 0; i < laneIndices.Count; i++)
        {
            if (laneIndices[i] == laneIndex)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetConveyorLaneAnchor(int laneIndex, out Transform laneAnchor)
    {
        laneAnchor = null;
        if (laneIndex < 0 || floorObjects == null || laneIndex >= floorObjects.Count)
        {
            return false;
        }

        laneAnchor = floorObjects[laneIndex];
        return laneAnchor != null;
    }

    private bool IsCornerConveyor()
    {
        return mapObject is ConveyorBelt conveyorBelt
               && conveyorBelt != null
               && conveyorBelt.IsCornerVariant;
    }

    private Vector3 GetConveyorLaneWorldPosition(int laneIndex, Transform anchor = null)
    {
        if (TryGetConveyorCornerLaneLocalPosition(laneIndex, out Vector3 cornerLocalPosition))
        {
            cornerLocalPosition.y = GetConveyorLaneHeight();
            return transform.TransformPoint(cornerLocalPosition);
        }

        Vector3 localPosition = GetConveyorLaneLocalOffset(laneIndex);
        localPosition.y = GetConveyorLaneHeight();

        return transform.TransformPoint(localPosition);
    }

    private Vector3 GetConveyorLaneLocalOffset(int laneIndex)
    {
        float halfExtent = GetConveyorLaneHalfExtent();
        switch (laneIndex)
        {
            case 0:
                return new Vector3(-halfExtent, 0f, -halfExtent);
            case 1:
                return new Vector3(halfExtent, 0f, -halfExtent);
            case 2:
                return new Vector3(-halfExtent, 0f, halfExtent);
            case 3:
                return new Vector3(halfExtent, 0f, halfExtent);
            default:
                return Vector3.zero;
        }
    }

    private bool TryGetConveyorCornerLaneLocalPosition(int laneIndex, out Vector3 localPosition)
    {
        localPosition = Vector3.zero;
        if (!IsCornerConveyor() || laneIndex < 0 || laneIndex >= ConveyorStackLaneLimit)
        {
            return false;
        }

        if (!TryGetConveyorCornerLaneTransition(laneIndex, out int sourceLaneIndex, out int destinationLaneIndex, out float progress))
        {
            return false;
        }

        Vector3 worldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, progress);
        localPosition = transform.InverseTransformPoint(worldPosition);
        localPosition.y = 0f;
        return true;
    }

    private bool TryGetConveyorCornerLaneTransition(int laneIndex, out int sourceLaneIndex, out int destinationLaneIndex, out float progress)
    {
        sourceLaneIndex = -1;
        destinationLaneIndex = -1;
        progress = 0f;

        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        if (laneIndex == outerSourceLaneIndex)
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = 0f;
            return true;
        }

        if (laneIndex == innerSourceLaneIndex)
        {
            sourceLaneIndex = innerSourceLaneIndex;
            destinationLaneIndex = innerDestinationLaneIndex;
            progress = 0f;
            return true;
        }

        if (laneIndex == outerDestinationLaneIndex)
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = 1f;
            return true;
        }

        if (laneIndex == innerDestinationLaneIndex)
        {
            sourceLaneIndex = innerSourceLaneIndex;
            destinationLaneIndex = innerDestinationLaneIndex;
            progress = 1f;
            return true;
        }

        return false;
    }

    private bool TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection)
    {
        localInputDirection = Vector2.zero;
        localOutputDirection = Vector2.zero;
        if (!(mapObject is ConveyorBelt conveyorBelt) || conveyorBelt == null)
        {
            return false;
        }

        if (!conveyorBelt.TryGetInputDirection(conveyorBelt.transform.rotation, out Vector2Int inputDirection)
            || !conveyorBelt.TryGetOutputDirection(conveyorBelt.transform.rotation, out Vector2Int outputDirection))
        {
            return false;
        }

        Vector3 localInputDirection3 = transform.InverseTransformDirection(new Vector3(inputDirection.x, 0f, inputDirection.y));
        Vector3 localOutputDirection3 = transform.InverseTransformDirection(new Vector3(outputDirection.x, 0f, outputDirection.y));
        localInputDirection = new Vector2(Mathf.Round(localInputDirection3.x), Mathf.Round(localInputDirection3.z));
        localOutputDirection = new Vector2(Mathf.Round(localOutputDirection3.x), Mathf.Round(localOutputDirection3.z));
        return localInputDirection.sqrMagnitude > 0.5f && localOutputDirection.sqrMagnitude > 0.5f;
    }

    private bool TryGetCornerConveyorCarryDirection(Vector3 worldPosition, out Vector3 carryDirection)
    {
        carryDirection = Vector3.zero;
        if (!TryGetConveyorCornerCenterlineArcParameters(out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out _))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 radial = new Vector2(localPosition3.x, localPosition3.z) - center;
        if (radial.sqrMagnitude <= 0.0001f)
        {
            if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
            {
                return false;
            }

            carryDirection = new Vector3(flowDirection.x, 0f, flowDirection.y);
            return carryDirection.sqrMagnitude > 0.0001f;
        }

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float startAngleDegrees = startAngleRadians * Mathf.Rad2Deg;
        float deltaAngleDegrees = deltaAngleRadians * Mathf.Rad2Deg;
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleDegrees, angleRadians * Mathf.Rad2Deg);
        float clampedProgress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleDegrees) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleDegrees);
        float clampedAngleRadians = startAngleRadians + (deltaAngleRadians * clampedProgress);

        Vector2 localTangent = Mathf.Sign(deltaAngleRadians) >= 0f
            ? new Vector2(-Mathf.Sin(clampedAngleRadians), Mathf.Cos(clampedAngleRadians))
            : new Vector2(Mathf.Sin(clampedAngleRadians), -Mathf.Cos(clampedAngleRadians));

        Vector3 worldDirection = transform.TransformDirection(new Vector3(localTangent.x, 0f, localTangent.y));
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        carryDirection = worldDirection.normalized;
        return true;
    }

    private bool TryGetCornerConveyorCarryDelta(Vector3 worldPosition, float conveyorSpeed, float deltaTime, out Vector3 delta)
    {
        delta = Vector3.zero;
        if (!TryGetClosestCornerConveyorCarryProjection(
                worldPosition,
                out int sourceLaneIndex,
                out int destinationLaneIndex,
                out float progress,
                out _))
        {
            return false;
        }

        float pathLength = GetConveyorCornerCarryPathLength(sourceLaneIndex, destinationLaneIndex);
        if (pathLength <= 0.0001f)
        {
            return false;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float currentDistanceAlongPath = progress * pathLength;
        float nextDistanceAlongPath = currentDistanceAlongPath + travelDistance;
        float clampedNextDistanceAlongPath = Mathf.Min(nextDistanceAlongPath, pathLength);
        float nextProgress = Mathf.Clamp01(clampedNextDistanceAlongPath / pathLength);
        Vector3 nextWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, nextProgress);

        if (nextDistanceAlongPath > pathLength
            && TryGetConveyorFlowDirection(out Vector2Int flowDirection)
            && flowDirection != Vector2Int.zero)
        {
            float overflowDistance = nextDistanceAlongPath - pathLength;
            Vector3 outputDirection = new Vector3(flowDirection.x, 0f, flowDirection.y).normalized;
            nextWorldPosition += outputDirection * overflowDistance;
        }

        delta = nextWorldPosition - worldPosition;
        delta.y = 0f;
        return delta.sqrMagnitude > 0.0000001f;
    }

    private bool TryGetCornerConveyorCarryDeltaWithHandoff(Vector3 worldPosition, float conveyorSpeed, float deltaTime, out Block resultingBlock, out Vector3 delta)
    {
        resultingBlock = this;
        delta = Vector3.zero;
        if (!TryGetClosestCornerConveyorCarryProjection(
                worldPosition,
                out int sourceLaneIndex,
                out int destinationLaneIndex,
                out float progress,
                out _))
        {
            return false;
        }

        float pathLength = GetConveyorCornerCarryPathLength(sourceLaneIndex, destinationLaneIndex);
        if (pathLength <= 0.0001f)
        {
            return false;
        }

        float travelDistance = conveyorSpeed * deltaTime;
        float currentDistanceAlongPath = progress * pathLength;
        float nextDistanceAlongPath = currentDistanceAlongPath + travelDistance;
        float clampedNextDistanceAlongPath = Mathf.Min(nextDistanceAlongPath, pathLength);
        float nextProgress = Mathf.Clamp01(clampedNextDistanceAlongPath / pathLength);
        Vector3 endOfCornerPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, 1f);
        Vector3 handoffPosition = endOfCornerPosition;
        if (TryGetCornerConveyorHandoffWorldPosition(sourceLaneIndex, destinationLaneIndex, out Vector3 resolvedHandoffPosition))
        {
            handoffPosition = resolvedHandoffPosition;
        }

        if (nextDistanceAlongPath <= pathLength + 0.0001f)
        {
            Vector3 nextWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, nextProgress);
            delta = nextWorldPosition - worldPosition;
            delta.y = 0f;
            resultingBlock = this;
            return delta.sqrMagnitude > 0.0000001f;
        }

        if (!TryGetConveyorFlowDirection(out Vector2Int flowDirection) || flowDirection == Vector2Int.zero)
        {
            delta = handoffPosition - worldPosition;
            delta.y = 0f;
            resultingBlock = this;
            return delta.sqrMagnitude > 0.0000001f;
        }

        float overflowDistance = nextDistanceAlongPath - pathLength;
        float remainingDeltaTime = overflowDistance / Mathf.Max(conveyorSpeed, 0.0001f);
        if (TryGetNextConveyorBlock(out Block nextBlock)
            && nextBlock != null
            && nextBlock.TryGetConveyorCarryDeltaWithHandoff(handoffPosition, remainingDeltaTime, out Block downstreamBlock, out Vector3 downstreamDelta))
        {
            delta = (handoffPosition - worldPosition) + downstreamDelta;
            delta.y = 0f;
            resultingBlock = downstreamBlock != null ? downstreamBlock : nextBlock;
            return delta.sqrMagnitude > 0.0000001f;
        }

        Vector3 outputDirection = new Vector3(flowDirection.x, 0f, flowDirection.y).normalized;
        Vector3 fallbackNextWorldPosition = handoffPosition + (outputDirection * overflowDistance);
        delta = fallbackNextWorldPosition - worldPosition;
        delta.y = 0f;
        resultingBlock = nextBlock != null ? nextBlock : this;
        return delta.sqrMagnitude > 0.0000001f;
    }

    private bool TryGetCornerConveyorStandingDistanceSqr(Vector3 worldPosition, out float distanceSqr)
    {
        distanceSqr = float.MaxValue;
        if (!TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 localPosition = new Vector2(localPosition3.x, localPosition3.z);

        float centerOffset = GetConveyorCornerCenterOffset(localInputDirection, localOutputDirection);
        Vector2 center = (localInputDirection + localOutputDirection) * centerOffset;
        float innerRadius = Mathf.Max(0.01f, GetConveyorCornerLaneRadius(false, centerOffset));
        float outerRadius = Mathf.Max(innerRadius, GetConveyorCornerLaneRadius(true, centerOffset));

        Vector2 radial = localPosition - center;
        float radius = radial.magnitude;
        if (radius <= 0.0001f)
        {
            Vector2 closestPoint = center + (-localOutputDirection * innerRadius);
            distanceSqr = (closestPoint - localPosition).sqrMagnitude;
            return true;
        }

        Vector2 startVector = -localOutputDirection;
        Vector2 endVector = -localInputDirection;
        float startAngleRadians = Mathf.Atan2(startVector.y, startVector.x);
        float endAngleRadians = Mathf.Atan2(endVector.y, endVector.x);
        float deltaAngleRadians = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, endAngleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, angleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float progress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleRadians) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleRadians);

        float clampedAngleRadians = startAngleRadians + (deltaAngleRadians * progress);
        float clampedRadius = Mathf.Clamp(radius, innerRadius, outerRadius);
        Vector2 closestPoint2D = center + new Vector2(Mathf.Cos(clampedAngleRadians), Mathf.Sin(clampedAngleRadians)) * clampedRadius;
        distanceSqr = (closestPoint2D - localPosition).sqrMagnitude;
        return true;
    }

    private bool TryGetClosestCornerConveyorLaneProjection(
        Vector3 worldPosition,
        out int sourceLaneIndex,
        out int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition)
    {
        sourceLaneIndex = -1;
        destinationLaneIndex = -1;
        progress = 0f;
        projectedWorldPosition = worldPosition;

        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        bool hasOuterProjection = TryProjectCornerConveyorPositionOntoLanePath(
            worldPosition,
            outerSourceLaneIndex,
            outerDestinationLaneIndex,
            out float outerProgress,
            out Vector3 outerProjectedWorldPosition,
            out float outerDistanceSqr);
        bool hasInnerProjection = TryProjectCornerConveyorPositionOntoLanePath(
            worldPosition,
            innerSourceLaneIndex,
            innerDestinationLaneIndex,
            out float innerProgress,
            out Vector3 innerProjectedWorldPosition,
            out float innerDistanceSqr);

        if (!hasOuterProjection && !hasInnerProjection)
        {
            return false;
        }

        if (hasOuterProjection && (!hasInnerProjection || outerDistanceSqr <= innerDistanceSqr))
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = outerProgress;
            projectedWorldPosition = outerProjectedWorldPosition;
            return true;
        }

        sourceLaneIndex = innerSourceLaneIndex;
        destinationLaneIndex = innerDestinationLaneIndex;
        progress = innerProgress;
        projectedWorldPosition = innerProjectedWorldPosition;
        return true;
    }

    private bool TryGetClosestCornerConveyorCarryProjection(
        Vector3 worldPosition,
        out int sourceLaneIndex,
        out int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition)
    {
        sourceLaneIndex = -1;
        destinationLaneIndex = -1;
        progress = 0f;
        projectedWorldPosition = worldPosition;

        if (!TryGetConveyorCornerLaneCandidates(
                out int outerSourceLaneIndex,
                out int outerDestinationLaneIndex,
                out int innerSourceLaneIndex,
                out int innerDestinationLaneIndex))
        {
            return false;
        }

        bool hasOuterProjection = TryProjectCornerConveyorPositionOntoCarryPath(
            worldPosition,
            outerSourceLaneIndex,
            outerDestinationLaneIndex,
            out float outerProgress,
            out Vector3 outerProjectedWorldPosition,
            out float outerDistanceSqr);
        bool hasInnerProjection = TryProjectCornerConveyorPositionOntoCarryPath(
            worldPosition,
            innerSourceLaneIndex,
            innerDestinationLaneIndex,
            out float innerProgress,
            out Vector3 innerProjectedWorldPosition,
            out float innerDistanceSqr);

        if (!hasOuterProjection && !hasInnerProjection)
        {
            return false;
        }

        if (hasOuterProjection && (!hasInnerProjection || outerDistanceSqr <= innerDistanceSqr))
        {
            sourceLaneIndex = outerSourceLaneIndex;
            destinationLaneIndex = outerDestinationLaneIndex;
            progress = outerProgress;
            projectedWorldPosition = outerProjectedWorldPosition;
            return true;
        }

        sourceLaneIndex = innerSourceLaneIndex;
        destinationLaneIndex = innerDestinationLaneIndex;
        progress = innerProgress;
        projectedWorldPosition = innerProjectedWorldPosition;
        return true;
    }

    private bool TryGetConveyorCornerLaneCandidates(
        out int outerSourceLaneIndex,
        out int outerDestinationLaneIndex,
        out int innerSourceLaneIndex,
        out int innerDestinationLaneIndex)
    {
        outerSourceLaneIndex = -1;
        outerDestinationLaneIndex = -1;
        innerSourceLaneIndex = -1;
        innerDestinationLaneIndex = -1;

        if (!TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        bool isCounterClockwiseTurn = IsCounterClockwiseTurn(localInputDirection, localOutputDirection);
        if (isCounterClockwiseTurn)
        {
            outerSourceLaneIndex = 3;
            outerDestinationLaneIndex = 0;
            innerSourceLaneIndex = 2;
            innerDestinationLaneIndex = 1;
            return true;
        }

        outerSourceLaneIndex = 2;
        outerDestinationLaneIndex = 0;
        innerSourceLaneIndex = 3;
        innerDestinationLaneIndex = 1;
        return true;
    }

    private bool TryProjectCornerConveyorPositionOntoLanePath(
        Vector3 worldPosition,
        int sourceLaneIndex,
        int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition,
        out float distanceSqr)
    {
        progress = 0f;
        projectedWorldPosition = worldPosition;
        distanceSqr = float.MaxValue;

        if (!TryGetConveyorCornerArcParameters(
                sourceLaneIndex,
                destinationLaneIndex,
                out Vector2 center,
                out float startAngleRadians,
                out float deltaAngleRadians,
                out _))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 radial = new Vector2(localPosition3.x, localPosition3.z) - center;
        if (radial.sqrMagnitude <= 0.0001f)
        {
            projectedWorldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, 0f);
            Vector3 initialDelta = projectedWorldPosition - worldPosition;
            initialDelta.y = 0f;
            distanceSqr = initialDelta.sqrMagnitude;
            return true;
        }

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float startAngleDegrees = startAngleRadians * Mathf.Rad2Deg;
        float deltaAngleDegrees = deltaAngleRadians * Mathf.Rad2Deg;
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleDegrees, angleRadians * Mathf.Rad2Deg);
        progress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleDegrees) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleDegrees);

        projectedWorldPosition = EvaluateConveyorCornerPathWorldPosition(sourceLaneIndex, destinationLaneIndex, progress);
        Vector3 projectedDelta = projectedWorldPosition - worldPosition;
        projectedDelta.y = 0f;
        distanceSqr = projectedDelta.sqrMagnitude;
        return true;
    }

    private bool TryProjectCornerConveyorPositionOntoCarryPath(
        Vector3 worldPosition,
        int sourceLaneIndex,
        int destinationLaneIndex,
        out float progress,
        out Vector3 projectedWorldPosition,
        out float distanceSqr)
    {
        progress = 0f;
        projectedWorldPosition = worldPosition;
        distanceSqr = float.MaxValue;

        if (!TryGetConveyorCornerCarryArcParameters(
                sourceLaneIndex,
                destinationLaneIndex,
                out Vector2 center,
                out float startAngleRadians,
                out float deltaAngleRadians,
                out _))
        {
            return false;
        }

        Vector3 localPosition3 = transform.InverseTransformPoint(worldPosition);
        Vector2 radial = new Vector2(localPosition3.x, localPosition3.z) - center;
        if (radial.sqrMagnitude <= 0.0001f)
        {
            projectedWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, 0f);
            Vector3 initialDelta = projectedWorldPosition - worldPosition;
            initialDelta.y = 0f;
            distanceSqr = initialDelta.sqrMagnitude;
            return true;
        }

        float angleRadians = Mathf.Atan2(radial.y, radial.x);
        float startAngleDegrees = startAngleRadians * Mathf.Rad2Deg;
        float deltaAngleDegrees = deltaAngleRadians * Mathf.Rad2Deg;
        float signedAngleFromStart = Mathf.DeltaAngle(startAngleDegrees, angleRadians * Mathf.Rad2Deg);
        progress = Mathf.Clamp01(
            Mathf.Abs(deltaAngleDegrees) <= 0.0001f
                ? 0f
                : signedAngleFromStart / deltaAngleDegrees);

        projectedWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, progress);
        Vector3 projectedDelta = projectedWorldPosition - worldPosition;
        projectedDelta.y = 0f;
        distanceSqr = projectedDelta.sqrMagnitude;
        return true;
    }

    private float GetConveyorCornerPathLength(int sourceLaneIndex, int destinationLaneIndex)
    {
        if (!TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, out _, out _, out float deltaAngleRadians, out float radius))
        {
            return 0f;
        }

        return Mathf.Abs(deltaAngleRadians) * radius;
    }

    private float GetConveyorCornerCarryPathLength(int sourceLaneIndex, int destinationLaneIndex)
    {
        if (!TryGetConveyorCornerCarryArcParameters(sourceLaneIndex, destinationLaneIndex, out _, out _, out float deltaAngleRadians, out float radius))
        {
            return 0f;
        }

        return Mathf.Abs(deltaAngleRadians) * radius;
    }

    private bool TryGetCornerConveyorHandoffWorldPosition(int sourceLaneIndex, int destinationLaneIndex, out Vector3 handoffWorldPosition)
    {
        handoffWorldPosition = EvaluateConveyorCornerCarryWorldPosition(sourceLaneIndex, destinationLaneIndex, 1f);
        return TryGetConveyorCornerCarryArcParameters(sourceLaneIndex, destinationLaneIndex, out _, out _, out _, out _);
    }

    private Vector3 EvaluateConveyorCornerPathWorldPosition(int sourceLaneIndex, int destinationLaneIndex, float t)
    {
        if (!TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius))
        {
            return GetDefaultConveyorLaneWorldPosition(destinationLaneIndex);
        }

        float angleRadians = startAngleRadians + (deltaAngleRadians * Mathf.Clamp01(t));
        Vector2 point2D = center + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * radius;
        Vector3 localPosition = new Vector3(point2D.x, GetConveyorLaneHeight(), point2D.y);
        return transform.TransformPoint(localPosition);
    }

    private Vector3 EvaluateConveyorCornerCarryWorldPosition(int sourceLaneIndex, int destinationLaneIndex, float t)
    {
        if (!TryGetConveyorCornerCarryArcParameters(sourceLaneIndex, destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius))
        {
            return GetDefaultConveyorLaneWorldPosition(destinationLaneIndex);
        }

        float angleRadians = startAngleRadians + (deltaAngleRadians * Mathf.Clamp01(t));
        Vector2 point2D = center + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * radius;
        Vector3 localPosition = new Vector3(point2D.x, GetConveyorLaneHeight(), point2D.y);
        return transform.TransformPoint(localPosition);
    }

    private Vector3 GetDefaultConveyorLaneWorldPosition(int laneIndex)
    {
        Vector3 localPosition = GetConveyorLaneLocalOffset(laneIndex);
        localPosition.y = GetConveyorLaneHeight();
        return transform.TransformPoint(localPosition);
    }

    private bool TryGetConveyorCornerCenterlineArcParameters(out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        center = Vector2.zero;
        startAngleRadians = 0f;
        deltaAngleRadians = 0f;
        radius = 0f;

        if (!IsCornerConveyor()
            || !TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        float centerOffset = GetConveyorCornerCenterOffset(localInputDirection, localOutputDirection);
        radius = centerOffset;
        center = (localInputDirection + localOutputDirection) * centerOffset;

        Vector2 startVector = -localOutputDirection;
        Vector2 endVector = -localInputDirection;
        startAngleRadians = Mathf.Atan2(startVector.y, startVector.x);
        float endAngleRadians = Mathf.Atan2(endVector.y, endVector.x);
        deltaAngleRadians = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, endAngleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float insetRadians = Mathf.Min(Mathf.Abs(deltaAngleRadians) * 0.45f, ConveyorCornerArcEndInsetDegrees * Mathf.Deg2Rad);
        if (insetRadians > 0.0001f)
        {
            float rotationSign = Mathf.Sign(deltaAngleRadians);
            startAngleRadians += rotationSign * insetRadians;
            deltaAngleRadians -= rotationSign * insetRadians * 2f;
        }

        return true;
    }

    private bool TryGetConveyorCornerCarryArcParameters(int sourceLaneIndex, int destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        return TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, false, out center, out startAngleRadians, out deltaAngleRadians, out radius);
    }

    private bool TryGetConveyorCornerArcParameters(int sourceLaneIndex, int destinationLaneIndex, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        return TryGetConveyorCornerArcParameters(sourceLaneIndex, destinationLaneIndex, true, out center, out startAngleRadians, out deltaAngleRadians, out radius);
    }

    private bool TryGetConveyorCornerArcParameters(int sourceLaneIndex, int destinationLaneIndex, bool applyInset, out Vector2 center, out float startAngleRadians, out float deltaAngleRadians, out float radius)
    {
        center = Vector2.zero;
        startAngleRadians = 0f;
        deltaAngleRadians = 0f;
        radius = 0f;

        if (!IsCornerConveyor()
            || sourceLaneIndex < 0
            || destinationLaneIndex < 0
            || sourceLaneIndex >= ConveyorStackLaneLimit
            || destinationLaneIndex >= ConveyorStackLaneLimit)
        {
            return false;
        }

        if (!TryGetConveyorCornerPathParameters(out Vector2 localInputDirection, out Vector2 localOutputDirection))
        {
            return false;
        }

        if (!TryIsOuterCornerTransition(sourceLaneIndex, destinationLaneIndex, localInputDirection, localOutputDirection, out bool isOuterLane))
        {
            return false;
        }

        float centerOffset = GetConveyorCornerCenterOffset(localInputDirection, localOutputDirection);
        radius = GetConveyorCornerLaneRadius(isOuterLane, centerOffset);
        center = (localInputDirection + localOutputDirection) * centerOffset;

        Vector2 startVector = -localOutputDirection;
        Vector2 endVector = -localInputDirection;
        startAngleRadians = Mathf.Atan2(startVector.y, startVector.x);
        float endAngleRadians = Mathf.Atan2(endVector.y, endVector.x);
        deltaAngleRadians = Mathf.DeltaAngle(startAngleRadians * Mathf.Rad2Deg, endAngleRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        if (applyInset)
        {
            float insetRadians = Mathf.Min(Mathf.Abs(deltaAngleRadians) * 0.45f, ConveyorCornerArcEndInsetDegrees * Mathf.Deg2Rad);
            if (insetRadians > 0.0001f)
            {
                float rotationSign = Mathf.Sign(deltaAngleRadians);
                startAngleRadians += rotationSign * insetRadians;
                deltaAngleRadians -= rotationSign * insetRadians * 2f;
            }
        }

        return true;
    }

    private bool TryIsOuterCornerTransition(int sourceLaneIndex, int destinationLaneIndex, Vector2 localInputDirection, Vector2 localOutputDirection, out bool isOuterLane)
    {
        isOuterLane = false;
        bool isCounterClockwiseTurn = IsCounterClockwiseTurn(localInputDirection, localOutputDirection);

        if (isCounterClockwiseTurn)
        {
            if (sourceLaneIndex == 3 && destinationLaneIndex == 0)
            {
                isOuterLane = true;
                return true;
            }

            if (sourceLaneIndex == 2 && destinationLaneIndex == 1)
            {
                isOuterLane = false;
                return true;
            }
        }
        else
        {
            if (sourceLaneIndex == 2 && destinationLaneIndex == 0)
            {
                isOuterLane = true;
                return true;
            }

            if (sourceLaneIndex == 3 && destinationLaneIndex == 1)
            {
                isOuterLane = false;
                return true;
            }
        }

        return false;
    }

    private static bool IsCounterClockwiseTurn(Vector2 localInputDirection, Vector2 localOutputDirection)
    {
        float cross = (localInputDirection.x * localOutputDirection.y) - (localInputDirection.y * localOutputDirection.x);
        return cross > 0f;
    }

    private float GetConveyorCornerCenterOffset(Vector2 localInputDirection, Vector2 localOutputDirection)
    {
        float inputOffset = GetConveyorEdgeOffsetForDirection(localInputDirection);
        float outputOffset = GetConveyorEdgeOffsetForDirection(localOutputDirection);
        float resolvedOffset = (inputOffset + outputOffset) * 0.5f;
        float minimumOffset = GetConveyorLaneHalfExtent() + 0.05f;
        return Mathf.Max(minimumOffset, resolvedOffset);
    }

    private float GetConveyorEdgeOffsetForDirection(Vector2 localDirection)
    {
        if (localDirection.sqrMagnitude <= 0.5f || floorObjects == null || floorObjects.Count == 0)
        {
            return ConveyorCornerCenterRadius;
        }

        Vector2 direction = localDirection.normalized;
        float bestProjection = 0f;
        for (int i = 0; i < floorObjects.Count; i++)
        {
            Transform laneAnchor = floorObjects[i];
            if (laneAnchor == null)
            {
                continue;
            }

            Vector3 localPosition3 = transform.InverseTransformPoint(laneAnchor.position);
            Vector2 localPosition = new Vector2(localPosition3.x, localPosition3.z);
            float projection = Vector2.Dot(localPosition, direction);
            if (projection > bestProjection)
            {
                bestProjection = projection;
            }
        }

        return bestProjection > 0.0001f ? bestProjection : ConveyorCornerCenterRadius;
    }

    private float GetConveyorCornerLaneRadius(bool isOuterLane, float centerOffset)
    {
        float halfExtent = GetConveyorLaneHalfExtent();
        float radius = centerOffset + (isOuterLane ? halfExtent : -halfExtent);
        return Mathf.Max(0.05f, radius);
    }

    private bool TryGetBestCornerConveyorReceiveLaneIndex(Vector3 referenceWorldPosition, out int bestLaneIndex)
    {
        bestLaneIndex = -1;
        float lane2DistanceSqr = float.MaxValue;
        float lane3DistanceSqr = float.MaxValue;

        if (2 < conveyorStack.Count)
        {
            Vector3 lane2Offset = GetConveyorLaneWorldPosition(2) - referenceWorldPosition;
            lane2Offset.y = 0f;
            lane2DistanceSqr = lane2Offset.sqrMagnitude;
        }

        if (3 < conveyorStack.Count)
        {
            Vector3 lane3Offset = GetConveyorLaneWorldPosition(3) - referenceWorldPosition;
            lane3Offset.y = 0f;
            lane3DistanceSqr = lane3Offset.sqrMagnitude;
        }

        int preferredLaneIndex = lane2DistanceSqr <= lane3DistanceSqr ? 2 : 3;
        if (preferredLaneIndex < 0 || preferredLaneIndex >= conveyorStack.Count)
        {
            return false;
        }

        if (conveyorStack[preferredLaneIndex] != null)
        {
            return false;
        }

        bestLaneIndex = preferredLaneIndex;
        return true;
    }

    private float GetConveyorLaneHalfExtent()
    {
        float targetRadius = GetConveyorLaneTargetRadius();
        if (targetRadius <= 0f)
        {
            return 0.24f * ConveyorLaneSpacingScale;
        }

        return (targetRadius / Mathf.Sqrt(2f)) * ConveyorLaneSpacingScale;
    }

    private float GetConveyorLaneHeight()
    {
        return ConveyorLaneHeight;
    }

    private float GetConveyorLaneTargetRadius()
    {
        if (floorObjects == null || floorObjects.Count == 0)
        {
            return 0f;
        }

        float totalRadius = 0f;
        int validCount = 0;
        for (int i = 0; i < floorObjects.Count; i++)
        {
            Transform laneAnchor = floorObjects[i];
            if (laneAnchor == null)
            {
                continue;
            }

            Vector3 localPosition = transform.InverseTransformPoint(laneAnchor.position);
            Vector2 flat = new Vector2(localPosition.x, localPosition.z);
            float radius = flat.magnitude;
            if (radius <= 0.0001f)
            {
                continue;
            }

            totalRadius += radius;
            validCount++;
        }

        return validCount > 0 ? totalRadius / validCount : 0f;
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
