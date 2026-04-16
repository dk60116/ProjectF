using System.Collections.Generic;
using UnityEngine;

public class PlayerBag : MonoBehaviour
{
    public event System.Action Changed;

    [SerializeField]
    private List<PortableStack> portableStack;

    [SerializeField]
    private List<int> currentStack;

    private readonly HashSet<PortableObject> reservedObjects = new HashSet<PortableObject>();
    private bool initialized;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (portableStack == null)
        {
            portableStack = new List<PortableStack>();
        }

        if (currentStack == null)
        {
            currentStack = new List<int>();
        }

        while (currentStack.Count < portableStack.Count)
        {
            currentStack.Add(0);
        }

        if (initialized)
        {
            return;
        }

        for (int i = 0; i < portableStack.Count; i++)
        {
            currentStack[i] = 0;

            PortableStack stack = portableStack[i];
            if (stack == null || stack.stack == null)
            {
                continue;
            }

            for (int j = 0; j < stack.stack.Count; j++)
            {
                if (stack.stack[j] != null)
                {
                    stack.stack[j].gameObject.SetActive(false);
                }
            }
        }

        initialized = true;
    }

    public void SetExternalStack(List<PortableObject> stack)
    {
        if (stack == null)
        {
            return;
        }

        if (portableStack == null)
        {
            portableStack = new List<PortableStack>();
        }

        if (portableStack.Count == 0)
        {
            portableStack.Add(new PortableStack { stack = stack });
        }
        else
        {
            portableStack[0] = portableStack[0] ?? new PortableStack();
            portableStack[0].stack = stack;
        }

        if (currentStack == null)
        {
            currentStack = new List<int>();
        }

        if (currentStack.Count == 0)
        {
            currentStack.Add(0);
        }

        initialized = true;
        EnsureInitialized();
        RefreshExternalStackCounts(false);
    }

    public void RefreshExternalStackCounts(bool notify = true)
    {
        if (portableStack == null || portableStack.Count == 0)
        {
            return;
        }

        PortableStack stack = portableStack[0];
        if (stack == null || stack.stack == null)
        {
            return;
        }

        if (currentStack == null)
        {
            currentStack = new List<int>();
        }

        if (currentStack.Count == 0)
        {
            currentStack.Add(0);
        }

        int count = 0;
        for (int i = 0; i < stack.stack.Count; i++)
        {
            PortableObject portableObject = stack.stack[i];
            if (portableObject == null || !portableObject.gameObject.activeSelf)
            {
                break;
            }

            count++;
        }

        currentStack[0] = Mathf.Clamp(count, 0, stack.stack.Count);

        if (notify)
        {
            NotifyChanged();
        }
    }

    private bool TryAddObjectToSlot(int index, int objectId, out PortableObject targetPortableObject)
    {
        EnsureInitialized();
        targetPortableObject = null;

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null)
        {
            return false;
        }

        int nextIndex = GetNextAvailableIndex(index);
        if (nextIndex < 0 || nextIndex >= stack.stack.Count)
        {
            return false;
        }

        targetPortableObject = stack.stack[nextIndex];
        if (targetPortableObject == null)
        {
            return false;
        }

        targetPortableObject.gameObject.SetActive(true);
        if (!targetPortableObject.SetItem(objectId))
        {
            targetPortableObject.gameObject.SetActive(false);
            targetPortableObject = null;
            return false;
        }

        currentStack[index] = Mathf.Clamp(nextIndex + 1, 0, stack.stack.Count);
        NotifyChanged();
        return true;
    }

    public bool CanAddObject(int index)
    {
        return CanAddObject(index, -1);
    }

    public bool CanAddObject(int index, int objectId)
    {
        EnsureInitialized();

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null)
        {
            return false;
        }

        int nextIndex = GetNextAvailableIndex(index);
        if (nextIndex < 0 || nextIndex >= stack.stack.Count)
        {
            return false;
        }

        return objectId < 0 || CanStackObject(index, objectId);
    }

    public bool TryAddObject(int index, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (index >= 0 && CanAddObject(index, objectId))
        {
            if (TryAddObjectToSlot(index, objectId, out targetPortableObject))
            {
                return true;
            }
        }

        return TryAddObject(objectId, out targetPortableObject);
    }

    public bool TryAddObjectToSlotOnly(int index, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (index < 0)
        {
            return false;
        }

        if (!CanAddObject(index, objectId))
        {
            return false;
        }

        return TryAddObjectToSlot(index, objectId, out targetPortableObject);
    }

    public bool TryGetOccupiedSlotObjects(int index, List<PortableObject> results)
    {
        EnsureInitialized();
        if (results == null)
        {
            return false;
        }

        results.Clear();

        if (portableStack == null || currentStack == null)
        {
            return false;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return false;
        }

        int occupiedCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        if (occupiedCount <= 0)
        {
            return true;
        }

        for (int i = 0; i < occupiedCount; i++)
        {
            PortableObject portableObject = stack.stack[i];
            if (portableObject == null)
            {
                return false;
            }

            results.Add(portableObject);
        }

        return true;
    }

    public bool TryGetSlotObjects(int index, int startIndex, int count, List<PortableObject> results)
    {
        EnsureInitialized();
        if (results == null)
        {
            return false;
        }

        results.Clear();

        if (portableStack == null || currentStack == null)
        {
            return false;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return false;
        }

        if (count <= 0)
        {
            return true;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return false;
        }

        int maxCount = stack.stack.Count;
        if (startIndex < 0 || startIndex >= maxCount || startIndex + count > maxCount)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            PortableObject portableObject = stack.stack[startIndex + i];
            if (portableObject == null)
            {
                return false;
            }

            results.Add(portableObject);
        }

        return true;
    }

    public void SetSlotCount(int index, int count, bool notify = true)
    {
        EnsureInitialized();
        if (currentStack == null || portableStack == null)
        {
            return;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return;
        }

        PortableStack stack = portableStack[index];
        int maxCount = stack != null && stack.stack != null ? stack.stack.Count : 0;
        int clamped = Mathf.Clamp(count, 0, maxCount);
        if (currentStack[index] == clamped)
        {
            return;
        }

        currentStack[index] = clamped;
        if (notify)
        {
            NotifyChanged();
        }
    }

    public bool TryAddObject(int objectId, out PortableObject targetPortableObject)
    {
        EnsureInitialized();
        targetPortableObject = null;

        if (TryAddObjectToFirstValidStack(objectId, true, out targetPortableObject))
        {
            return true;
        }

        if (TryAddObjectToFirstValidStack(objectId, false, out targetPortableObject))
        {
            return true;
        }

        return false;
    }

    public bool HasExistingStackSpaceForItem(int objectId)
    {
        EnsureInitialized();

        if (objectId < 0 || portableStack == null || currentStack == null)
        {
            return false;
        }

        for (int i = 0; i < portableStack.Count; i++)
        {
            if (currentStack[i] <= 0)
            {
                continue;
            }

            if (!CanAddObject(i, objectId) || !CanStackObject(i, objectId))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    public bool TryReserveObjectInExistingStack(int objectId, out PortableObject targetPortableObject)
    {
        EnsureInitialized();
        targetPortableObject = null;

        if (objectId < 0 || portableStack == null || currentStack == null)
        {
            return false;
        }

        for (int i = 0; i < portableStack.Count; i++)
        {
            if (currentStack[i] <= 0)
            {
                continue;
            }

            if (!CanAddObject(i, objectId) || !CanStackObject(i, objectId))
            {
                continue;
            }

            if (TryReserveObjectToSlot(i, objectId, out targetPortableObject))
            {
                return true;
            }
        }

        return false;
    }

    public void CommitReservedObject(PortableObject targetPortableObject)
    {
        EnsureInitialized();
        if (targetPortableObject == null)
        {
            return;
        }

        if (!reservedObjects.Remove(targetPortableObject))
        {
            return;
        }

        if (!TryFindPortableObjectIndex(targetPortableObject, out int stackIndex, out int objectIndex))
        {
            if (!targetPortableObject.gameObject.activeSelf)
            {
                targetPortableObject.gameObject.SetActive(true);
            }

            NotifyChanged();
            return;
        }

        currentStack[stackIndex] = Mathf.Max(currentStack[stackIndex], objectIndex + 1);
        if (!targetPortableObject.gameObject.activeSelf)
        {
            targetPortableObject.gameObject.SetActive(true);
        }

        NotifyChanged();
    }

    public void ReleaseReservedObject(PortableObject targetPortableObject)
    {
        EnsureInitialized();
        if (targetPortableObject == null)
        {
            return;
        }

        reservedObjects.Remove(targetPortableObject);
        if (targetPortableObject.gameObject.activeSelf)
        {
            targetPortableObject.gameObject.SetActive(false);
        }
    }

    public bool TryRemoveOneAtSlot(int index, out int objectId)
    {
        EnsureInitialized();
        objectId = -1;

        if (portableStack == null || currentStack == null)
        {
            return false;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return false;
        }

        int occupiedCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        if (occupiedCount <= 0)
        {
            return false;
        }

        objectId = GetSlotItemId(index);
        int topIndex = occupiedCount - 1;
        PortableObject topObject = stack.stack[topIndex];

        currentStack[index] = topIndex;
        if (topObject != null)
        {
            topObject.gameObject.SetActive(false);
        }

        NotifyChanged();
        return objectId >= 0;
    }

    public bool TryRemoveAllAtSlot(int index, out int objectId, out int removedCount, out Vector3 startWorldPosition)
    {
        EnsureInitialized();
        objectId = -1;
        removedCount = 0;
        startWorldPosition = transform.position;

        if (portableStack == null || currentStack == null)
        {
            return false;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return false;
        }

        int occupiedCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        if (occupiedCount <= 0)
        {
            return false;
        }

        PortableObject topObject = stack.stack[occupiedCount - 1];
        if (topObject != null)
        {
            startWorldPosition = topObject.transform.position;
        }

        objectId = GetSlotItemId(index);
        removedCount = occupiedCount;

        for (int i = 0; i < occupiedCount; i++)
        {
            PortableObject portableObject = stack.stack[i];
            if (portableObject != null)
            {
                portableObject.gameObject.SetActive(false);
            }
        }

        currentStack[index] = 0;
        NotifyChanged();
        return objectId >= 0 && removedCount > 0;
    }

    public int RemoveItems(int itemId, int count)
    {
        EnsureInitialized();

        if (itemId < 0 || count <= 0)
        {
            return 0;
        }

        if (portableStack == null || currentStack == null)
        {
            return 0;
        }

        int remaining = count;
        int maxSlots = Mathf.Min(portableStack.Count, currentStack.Count);
        for (int i = 0; i < maxSlots && remaining > 0; i++)
        {
            if (GetSlotItemId(i) != itemId)
            {
                continue;
            }

            int removed = RemoveItemsFromSlot(i, remaining);
            remaining -= removed;
        }

        int totalRemoved = count - remaining;
        if (totalRemoved > 0)
        {
            NotifyChanged();
        }

        return totalRemoved;
    }

    private int RemoveItemsFromSlot(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (portableStack == null || currentStack == null)
        {
            return 0;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return 0;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return 0;
        }

        int occupiedCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        if (occupiedCount <= 0)
        {
            return 0;
        }

        int removeCount = Mathf.Clamp(count, 0, occupiedCount);
        for (int i = 0; i < removeCount; i++)
        {
            int topIndex = occupiedCount - 1 - i;
            PortableObject portableObject = stack.stack[topIndex];
            if (portableObject != null)
            {
                portableObject.gameObject.SetActive(false);
            }
        }

        currentStack[index] = occupiedCount - removeCount;
        return removeCount;
    }

    public PortableObject GetTopObject(int index)
    {
        EnsureInitialized();

        if (portableStack == null || currentStack == null)
        {
            return null;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return null;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return null;
        }

        int occupiedCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        if (occupiedCount <= 0)
        {
            return GetBottomObject(stack);
        }

        int topIndex = Mathf.Clamp(occupiedCount - 1, 0, stack.stack.Count - 1);
        if (stack.stack[topIndex] != null)
        {
            return stack.stack[topIndex];
        }

        for (int i = topIndex; i >= 0; i--)
        {
            if (stack.stack[i] != null)
            {
                return stack.stack[i];
            }
        }

        return GetBottomObject(stack);
    }

    private bool TryGetNextSlot(int index, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (!CanAddObject(index, objectId))
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        int stackIndex = GetNextAvailableIndex(index);
        if (stack == null || stack.stack == null || stackIndex < 0 || stackIndex >= stack.stack.Count)
        {
            return false;
        }

        targetPortableObject = stack.stack[stackIndex];
        return targetPortableObject != null;
    }

    private int GetNextAvailableIndex(int index)
    {
        if (portableStack == null || currentStack == null)
        {
            return -1;
        }

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return -1;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return -1;
        }

        int startIndex = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        for (int i = startIndex; i < stack.stack.Count; i++)
        {
            if (stack.stack[i] != null && !reservedObjects.Contains(stack.stack[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private bool CanStackObject(int index, int objectId)
    {
        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return false;
        }

        int occupiedCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        if (occupiedCount <= 0)
        {
            return true;
        }

        PortableObject bottomObject = GetBottomObject(stack);
        return bottomObject != null && bottomObject.ItemId == objectId;
    }

    private int FindStackIndexForObject(int objectId, bool requireExistingItems)
    {
        for (int i = 0; i < portableStack.Count; i++)
        {
            if (!CanAddObject(i))
            {
                continue;
            }

            bool hasItems = currentStack[i] > 0;
            if (hasItems != requireExistingItems)
            {
                continue;
            }

            if (CanStackObject(i, objectId))
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryAddObjectToFirstValidStack(int objectId, bool requireExistingItems, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;

        if (portableStack == null || currentStack == null)
        {
            return false;
        }

        for (int i = 0; i < portableStack.Count; i++)
        {
            if (!CanAddObject(i, objectId))
            {
                continue;
            }

            bool hasItems = currentStack[i] > 0;
            if (hasItems != requireExistingItems)
            {
                continue;
            }

            if (!CanStackObject(i, objectId))
            {
                continue;
            }

            if (TryAddObjectToSlot(i, objectId, out targetPortableObject))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryReserveObjectToSlot(int index, int objectId, out PortableObject targetPortableObject)
    {
        EnsureInitialized();
        targetPortableObject = null;

        if (index < 0 || index >= portableStack.Count || index >= currentStack.Count)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null)
        {
            return false;
        }

        int nextIndex = GetNextAvailableIndex(index);
        if (nextIndex < 0 || nextIndex >= stack.stack.Count)
        {
            return false;
        }

        targetPortableObject = stack.stack[nextIndex];
        if (targetPortableObject == null || reservedObjects.Contains(targetPortableObject))
        {
            targetPortableObject = null;
            return false;
        }

        if (!targetPortableObject.SetItem(objectId))
        {
            targetPortableObject = null;
            return false;
        }

        if (targetPortableObject.gameObject.activeSelf)
        {
            targetPortableObject.gameObject.SetActive(false);
        }

        reservedObjects.Add(targetPortableObject);
        return true;
    }

    private bool TryFindPortableObjectIndex(PortableObject targetPortableObject, out int stackIndex, out int objectIndex)
    {
        stackIndex = -1;
        objectIndex = -1;

        if (targetPortableObject == null || portableStack == null)
        {
            return false;
        }

        for (int i = 0; i < portableStack.Count; i++)
        {
            PortableStack stack = portableStack[i];
            if (stack == null || stack.stack == null)
            {
                continue;
            }

            for (int j = 0; j < stack.stack.Count; j++)
            {
                if (stack.stack[j] != targetPortableObject)
                {
                    continue;
                }

                stackIndex = i;
                objectIndex = j;
                return true;
            }
        }

        return false;
    }

    public int SlotCount
    {
        get
        {
            EnsureInitialized();
            return portableStack != null ? portableStack.Count : 0;
        }
    }

    public int GetSlotCount(int index)
    {
        EnsureInitialized();
        if (currentStack == null || index < 0 || index >= currentStack.Count)
        {
            return 0;
        }

        return Mathf.Max(0, currentStack[index]);
    }

    public int GetSlotMaxCount(int index)
    {
        EnsureInitialized();
        if (portableStack == null || index < 0 || index >= portableStack.Count)
        {
            return 0;
        }

        PortableStack stack = portableStack[index];
        return stack != null && stack.stack != null ? stack.stack.Count : 0;
    }

    public int GetSlotItemId(int index)
    {
        EnsureInitialized();
        if (portableStack == null || index < 0 || index >= portableStack.Count)
        {
            return -1;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0 || currentStack[index] <= 0)
        {
            return -1;
        }

        PortableObject bottomObject = GetBottomObject(stack);
        return bottomObject != null ? bottomObject.ItemId : -1;
    }

    public int GetTotalItemCount(int itemId)
    {
        EnsureInitialized();
        if (itemId < 0 || portableStack == null || currentStack == null)
        {
            return 0;
        }

        int total = 0;
        int maxSlots = Mathf.Min(portableStack.Count, currentStack.Count);
        for (int i = 0; i < maxSlots; i++)
        {
            if (GetSlotItemId(i) == itemId)
            {
                total += GetSlotCount(i);
            }
        }

        return total;
    }

    private PortableObject GetBottomObject(PortableStack stack)
    {
        if (stack == null || stack.stack == null)
        {
            return null;
        }

        for (int i = 0; i < stack.stack.Count; i++)
        {
            if (stack.stack[i] != null)
            {
                return stack.stack[i];
            }
        }

        return null;
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }

    public void ForceNotifyChanged()
    {
        NotifyChanged();
    }
}
