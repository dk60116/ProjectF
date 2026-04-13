using ProjectF.Attributes;
using System.Collections.Generic;
using UnityEngine;

public class Block : BaseObject
{
    public enum BlockType { Ground, Water }

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
    private float floorObjectVerticalSpacing = 0.1f;

    [SerializeField]
    private MapFocus focus;

    private readonly List<List<PortableObject>> floorStacks = new List<List<PortableObject>>();
    private PortableObjectPool floorObjectPool;

    private void Awake()
    {
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
        coordinate = blockCoordinate;
        type = blockType;
        objectName = $"{blockType}_{blockCoordinate.x}_{blockCoordinate.y}";
        gameObject.name = $"Block ({blockCoordinate.x}, {blockCoordinate.y})";
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

    public bool TryAddFloorObject(int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

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
                stack.Add(portableObject);
                targetPortableObject = portableObject;
                return true;
            }
        }

        return false;
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

    public bool TryAddFloorObjectAnimated(int objectId, Vector3 startWorldPosition, float delay, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        EnsureFloorObjectsInitialized();

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
                portableObject.transform.SetParent(anchor, true);
                portableObject.transform.position = startWorldPosition;
                portableObject.transform.rotation = Quaternion.identity;
                portableObject.transform.localScale = Vector3.one;
                portableObject.gameObject.SetActive(true);

                int objectIndex = stack.Count;
                Vector3 finalLocalPosition = new Vector3(0f, objectIndex * floorObjectVerticalSpacing, 0f);
                Vector3 finalWorldPosition = anchor.TransformPoint(finalLocalPosition);
                stack.Add(portableObject);

                portableObject.MoveTo(finalWorldPosition, delay, () =>
                {
                    if (portableObject == null || anchor == null)
                    {
                        return;
                    }

                    portableObject.transform.SetParent(anchor, false);
                    portableObject.transform.localPosition = finalLocalPosition;
                    portableObject.transform.localRotation = Quaternion.identity;
                    portableObject.transform.localScale = Vector3.one;
                    portableObject.gameObject.SetActive(true);
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
            if (!TryAddFloorObject(itemIds[i], out _))
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

    public bool TryAutoPickupFloorObjects(Player player, Vector3 playerPosition, float pickupRadius)
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
                    gate.UpdateExitState(distanceSqr);
                }
            }

            DroppedItemPickupGate topGate = topObject.GetComponent<DroppedItemPickupGate>();
            if (topGate != null)
            {
                if (!topGate.CanPickup(distanceSqr, pickupRadiusSqr))
                {
                    continue;
                }
            }

            if (distanceSqr > pickupRadiusSqr)
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
    }

    private int GetAvailableFloorCapacity()
    {
        return GetAvailableFloorCapacity(-1);
    }

    private int GetAvailableFloorCapacity(int itemId)
    {
        EnsureFloorObjectsInitialized();

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
            floorObject.gameObject.SetActive(false);
            return;
        }

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

    private void ConfigureFloorObjectTransform(PortableObject portableObject, Transform anchor, int stackIndex)
    {
        portableObject.transform.SetParent(anchor, false);
        portableObject.transform.localPosition = new Vector3(0f, stackIndex * floorObjectVerticalSpacing, 0f);
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
}
