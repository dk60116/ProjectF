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
    private readonly List<int> visualPreservedStackCounts = new List<int>();
    private bool initialized;
    private bool usesExternalStack;

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

        SyncPortableStacksFromChildStackRoots();

        if (currentStack == null)
        {
            currentStack = new List<int>();
        }

        while (currentStack.Count < portableStack.Count)
        {
            currentStack.Add(0);
        }

        EnsureVisualPreservedStackCounts();

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

        usesExternalStack = true;

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

        EnsureVisualPreservedStackCounts();
        initialized = true;
        EnsureInitialized();
        RefreshExternalStackCounts(false);
    }

    private void SyncPortableStacksFromChildStackRoots()
    {
        if (usesExternalStack)
        {
            return;
        }

        List<PortableStack> childStacks = BuildPortableStacksFromChildStackRoots();
        if (childStacks == null || childStacks.Count == 0)
        {
            return;
        }

        if (!ShouldUseChildStackRoots(childStacks))
        {
            return;
        }

        portableStack.Clear();
        for (int i = 0; i < childStacks.Count; i++)
        {
            portableStack.Add(childStacks[i]);
        }
    }

    private List<PortableStack> BuildPortableStacksFromChildStackRoots()
    {
        List<PortableStack> childStacks = null;
        Transform bagTransform = transform;
        for (int i = 0; i < bagTransform.childCount; i++)
        {
            Transform child = bagTransform.GetChild(i);
            if (!IsStackRoot(child))
            {
                continue;
            }

            PortableObject[] stackObjects = child.GetComponentsInChildren<PortableObject>(true);
            if (stackObjects == null || stackObjects.Length == 0)
            {
                continue;
            }

            PortableStack stack = new PortableStack
            {
                stack = new List<PortableObject>(stackObjects.Length)
            };

            for (int j = 0; j < stackObjects.Length; j++)
            {
                if (stackObjects[j] != null)
                {
                    stack.stack.Add(stackObjects[j]);
                }
            }

            if (stack.stack.Count == 0)
            {
                continue;
            }

            childStacks ??= new List<PortableStack>();
            childStacks.Add(stack);
        }

        return childStacks;
    }

    private static bool IsStackRoot(Transform target)
    {
        return target != null
               && !string.IsNullOrEmpty(target.name)
               && target.name.StartsWith("Stack", System.StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldUseChildStackRoots(List<PortableStack> childStacks)
    {
        if (portableStack == null || portableStack.Count == 0)
        {
            return true;
        }

        if (childStacks.Count > portableStack.Count)
        {
            return true;
        }

        if (childStacks.Count < portableStack.Count)
        {
            return false;
        }

        for (int i = 0; i < childStacks.Count; i++)
        {
            List<PortableObject> childStack = childStacks[i].stack;
            List<PortableObject> serializedStack = portableStack[i] != null ? portableStack[i].stack : null;
            if (childStack == null || serializedStack == null || childStack.Count != serializedStack.Count)
            {
                return true;
            }

            for (int j = 0; j < childStack.Count; j++)
            {
                if (childStack[j] != serializedStack[j])
                {
                    return true;
                }
            }
        }

        return false;
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

        EnsureVisualPreservedStackCounts();
        int count = CountContiguousActiveObjects(stack);
        int preservedCount = Mathf.Clamp(GetVisualPreservedStackCount(0), 0, count);
        visualPreservedStackCounts[0] = preservedCount;
        currentStack[0] = Mathf.Clamp(count - preservedCount, 0, stack.stack.Count);

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

        if (TryRestoreVisualPreservedObjectToSlot(index, objectId, true, out targetPortableObject))
        {
            return true;
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

        if (objectId >= 0 && HasVisualPreservedObject(index, objectId))
        {
            return true;
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

    public void SetSlotCount(int index, int count, bool notify = true, bool mergeDuplicates = true)
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
        ClampVisualPreservedStackCount(index);
        if (mergeDuplicates)
        {
            TryMergeDuplicateItemStacks();
        }

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

    public bool HasSpaceForItem(int objectId)
    {
        EnsureInitialized();

        if (objectId < 0 || portableStack == null || currentStack == null)
        {
            return false;
        }

        for (int i = 0; i < portableStack.Count; i++)
        {
            if (CanAddObject(i, objectId))
            {
                return true;
            }
        }

        return false;
    }

    public int GetAvailableCapacityForItem(int objectId)
    {
        EnsureInitialized();

        if (objectId < 0 || portableStack == null || currentStack == null)
        {
            return 0;
        }

        int totalCapacity = 0;
        for (int i = 0; i < portableStack.Count; i++)
        {
            if (!CanAddObject(i, objectId))
            {
                continue;
            }

            totalCapacity += Mathf.Max(0, GetSlotMaxCount(i) - GetSlotCount(i));
        }

        return totalCapacity;
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

    public bool TryReserveObject(int objectId, out PortableObject targetPortableObject)
    {
        EnsureInitialized();
        targetPortableObject = null;

        if (objectId < 0 || portableStack == null || currentStack == null)
        {
            return false;
        }

        if (TryReserveObjectToFirstValidStack(objectId, true, out targetPortableObject))
        {
            return true;
        }

        return TryReserveObjectToFirstValidStack(objectId, false, out targetPortableObject);
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

        TryMergeDuplicateItemStacks();
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

        if (TryMergeDuplicateItemStacks())
        {
            NotifyChanged();
        }
    }

    public bool TryRemoveOneAtSlot(int index, out int objectId, bool mergeDuplicates = true)
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

        if (mergeDuplicates)
        {
            TryMergeDuplicateItemStacks();
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
        TryMergeDuplicateItemStacks();
        NotifyChanged();
        return objectId >= 0 && removedCount > 0;
    }

    public bool TryRemoveItemsAtSlot(
        int index,
        int count,
        out int objectId,
        out int removedCount,
        out Vector3 startWorldPosition,
        bool mergeDuplicates = true,
        bool preserveVisuals = false)
    {
        EnsureInitialized();
        objectId = -1;
        removedCount = 0;
        startWorldPosition = transform.position;

        if (count <= 0 || portableStack == null || currentStack == null)
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
        removedCount = RemoveItemsFromSlot(index, count, preserveVisuals);

        if (mergeDuplicates && !preserveVisuals)
        {
            TryMergeDuplicateItemStacks();
        }

        NotifyChanged();
        return objectId >= 0 && removedCount > 0;
    }

    public int RemoveItems(int itemId, int count, bool preserveVisuals = false)
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

            int removed = RemoveItemsFromSlot(i, remaining, preserveVisuals);
            remaining -= removed;
        }

        int totalRemoved = count - remaining;
        if (totalRemoved > 0)
        {
            if (!preserveVisuals)
            {
                TryMergeDuplicateItemStacks();
            }

            NotifyChanged();
        }

        return totalRemoved;
    }

    private int RemoveItemsFromSlot(int index, int count, bool preserveVisuals = false)
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
        if (preserveVisuals)
        {
            int activeCount = CountContiguousActiveObjects(stack);
            int preservedCount = Mathf.Clamp(GetVisualPreservedStackCount(index), 0, activeCount);
            visualPreservedStackCounts[index] = Mathf.Clamp(preservedCount + removeCount, 0, activeCount);
            currentStack[index] = occupiedCount - removeCount;
            return removeCount;
        }

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

    private bool TryMergeDuplicateItemStacks()
    {
        EnsureInitialized();

        if (portableStack == null || currentStack == null || reservedObjects.Count > 0 || HasAnyVisualPreservedStackCounts())
        {
            return false;
        }

        bool mergedAny = false;
        int slotCount = Mathf.Min(portableStack.Count, currentStack.Count);
        for (int targetIndex = 0; targetIndex < slotCount; targetIndex++)
        {
            int itemId = GetSlotItemId(targetIndex);
            if (itemId < 0)
            {
                continue;
            }

            int targetCount = GetSlotCount(targetIndex);
            int targetMaxCount = GetSlotMaxCount(targetIndex);
            if (targetCount <= 0 || targetMaxCount <= targetCount)
            {
                continue;
            }

            for (int sourceIndex = targetIndex + 1; sourceIndex < slotCount && targetCount < targetMaxCount; sourceIndex++)
            {
                if (GetSlotItemId(sourceIndex) != itemId)
                {
                    continue;
                }

                int sourceCount = GetSlotCount(sourceIndex);
                if (sourceCount <= 0)
                {
                    continue;
                }

                int moveCount = Mathf.Min(targetMaxCount - targetCount, sourceCount);
                if (moveCount <= 0)
                {
                    continue;
                }

                if (!TryMoveItemsBetweenSlots(sourceIndex, targetIndex, itemId, moveCount))
                {
                    continue;
                }

                mergedAny = true;
                targetCount += moveCount;
            }
        }

        return mergedAny;
    }

    private bool TryMoveItemsBetweenSlots(int sourceIndex, int targetIndex, int itemId, int moveCount)
    {
        if (moveCount <= 0
            || sourceIndex == targetIndex
            || sourceIndex < 0
            || targetIndex < 0
            || sourceIndex >= portableStack.Count
            || targetIndex >= portableStack.Count
            || sourceIndex >= currentStack.Count
            || targetIndex >= currentStack.Count)
        {
            return false;
        }

        PortableStack sourceStack = portableStack[sourceIndex];
        PortableStack targetStack = portableStack[targetIndex];
        if (sourceStack == null
            || targetStack == null
            || sourceStack.stack == null
            || targetStack.stack == null)
        {
            return false;
        }

        int sourceCount = Mathf.Clamp(currentStack[sourceIndex], 0, sourceStack.stack.Count);
        int targetCount = Mathf.Clamp(currentStack[targetIndex], 0, targetStack.stack.Count);
        if (sourceCount < moveCount || targetCount + moveCount > targetStack.stack.Count)
        {
            return false;
        }

        for (int i = 0; i < moveCount; i++)
        {
            PortableObject targetPortableObject = targetStack.stack[targetCount + i];
            PortableObject sourcePortableObject = sourceStack.stack[sourceCount - 1 - i];
            if (targetPortableObject == null
                || sourcePortableObject == null
                || reservedObjects.Contains(targetPortableObject)
                || reservedObjects.Contains(sourcePortableObject))
            {
                return false;
            }
        }

        for (int i = 0; i < moveCount; i++)
        {
            PortableObject targetPortableObject = targetStack.stack[targetCount + i];
            PortableObject sourcePortableObject = sourceStack.stack[sourceCount - 1 - i];

            if (!targetPortableObject.SetItem(itemId))
            {
                return false;
            }

            if (!targetPortableObject.gameObject.activeSelf)
            {
                targetPortableObject.gameObject.SetActive(true);
            }

            if (sourcePortableObject.gameObject.activeSelf)
            {
                sourcePortableObject.gameObject.SetActive(false);
            }
        }

        currentStack[targetIndex] = targetCount + moveCount;
        currentStack[sourceIndex] = sourceCount - moveCount;
        return true;
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
            PortableObject portableObject = stack.stack[i];
            if (portableObject != null
                && !portableObject.gameObject.activeSelf
                && !reservedObjects.Contains(portableObject))
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
            int visualPreservedItemId = GetVisualPreservedItemId(index);
            if (visualPreservedItemId >= 0)
            {
                return visualPreservedItemId == objectId;
            }

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

    public bool HasVisualPreservedObject(int index, int objectId)
    {
        EnsureInitialized();
        return TryGetVisualPreservedObject(index, objectId, out _, out _);
    }

    public bool HasVisualPreservedObjects(int index)
    {
        EnsureInitialized();
        return GetVisualPreservedStackCount(index) > 0;
    }

    public bool TryRestoreVisualPreservedObjectToSlotOnly(
        int index,
        int objectId,
        out PortableObject targetPortableObject)
    {
        EnsureInitialized();
        return TryRestoreVisualPreservedObjectToSlot(index, objectId, true, out targetPortableObject);
    }

    public bool ClearVisualPreservedObjects(int index, bool notify = true)
    {
        EnsureInitialized();
        if (portableStack == null
            || currentStack == null
            || index < 0
            || index >= portableStack.Count
            || index >= currentStack.Count
            || index >= visualPreservedStackCounts.Count
            || visualPreservedStackCounts[index] <= 0)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null)
        {
            visualPreservedStackCounts[index] = 0;
            return false;
        }

        int logicalCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        int activeCount = CountContiguousActiveObjects(stack);
        for (int i = logicalCount; i < activeCount; i++)
        {
            PortableObject portableObject = stack.stack[i];
            if (portableObject != null && portableObject.gameObject.activeSelf)
            {
                portableObject.gameObject.SetActive(false);
            }
        }

        visualPreservedStackCounts[index] = 0;
        if (notify)
        {
            NotifyChanged();
        }

        return true;
    }

    public bool CommitVisualPreservedObjectRemoval(
        int index,
        int objectId,
        out PortableObject removedPortableObject,
        bool notify = true)
    {
        EnsureInitialized();
        removedPortableObject = null;
        if (!TryGetVisualPreservedObject(index, objectId, true, out _, out removedPortableObject))
        {
            return false;
        }

        if (removedPortableObject.gameObject.activeSelf)
        {
            removedPortableObject.gameObject.SetActive(false);
        }

        visualPreservedStackCounts[index] = Mathf.Max(0, visualPreservedStackCounts[index] - 1);
        if (notify)
        {
            NotifyChanged();
        }

        return true;
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

    private bool TryReserveObjectToFirstValidStack(int objectId, bool requireExistingItems, out PortableObject targetPortableObject)
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

            if (TryReserveObjectToSlot(i, objectId, out targetPortableObject))
            {
                return true;
            }
        }

        return false;
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

    public void CaptureSaveSlots(List<PlayerInventorySlotSaveState> results)
    {
        if (results == null)
        {
            return;
        }

        EnsureInitialized();
        results.Clear();
        int slotCount = SlotCount;
        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            results.Add(new PlayerInventorySlotSaveState
            {
                slotIndex = slotIndex,
                itemId = GetSlotItemId(slotIndex),
                count = GetSlotCount(slotIndex),
                capacity = GetSlotMaxCount(slotIndex)
            });
        }
    }

    public void ApplySaveSlots(IReadOnlyList<PlayerInventorySlotSaveState> slots)
    {
        EnsureInitialized();
        ClearAllSlots(false);

        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                PlayerInventorySlotSaveState slot = slots[i];
                if (slot == null || slot.slotIndex < 0 || slot.itemId < 0 || slot.count <= 0)
                {
                    continue;
                }

                ApplySaveSlot(slot.slotIndex, slot.itemId, slot.count);
            }
        }

        NotifyChanged();
    }

    private void ClearAllSlots(bool notify = true)
    {
        EnsureInitialized();
        reservedObjects.Clear();
        if (portableStack == null || currentStack == null)
        {
            return;
        }

        for (int slotIndex = 0; slotIndex < portableStack.Count; slotIndex++)
        {
            PortableStack stack = portableStack[slotIndex];
            if (stack?.stack != null)
            {
                for (int objectIndex = 0; objectIndex < stack.stack.Count; objectIndex++)
                {
                    PortableObject portableObject = stack.stack[objectIndex];
                    if (portableObject != null && portableObject.gameObject.activeSelf)
                    {
                        portableObject.gameObject.SetActive(false);
                    }
                }
            }

            if (slotIndex < currentStack.Count)
            {
                currentStack[slotIndex] = 0;
            }

            if (slotIndex < visualPreservedStackCounts.Count)
            {
                visualPreservedStackCounts[slotIndex] = 0;
            }
        }

        if (notify)
        {
            NotifyChanged();
        }
    }

    private void ApplySaveSlot(int slotIndex, int itemId, int count)
    {
        if (portableStack == null
            || currentStack == null
            || slotIndex < 0
            || slotIndex >= portableStack.Count
            || slotIndex >= currentStack.Count)
        {
            return;
        }

        PortableStack stack = portableStack[slotIndex];
        if (stack?.stack == null || stack.stack.Count <= 0)
        {
            return;
        }

        int clampedCount = Mathf.Clamp(count, 0, stack.stack.Count);
        for (int objectIndex = 0; objectIndex < stack.stack.Count; objectIndex++)
        {
            PortableObject portableObject = stack.stack[objectIndex];
            if (portableObject == null)
            {
                continue;
            }

            bool shouldBeActive = objectIndex < clampedCount;
            if (shouldBeActive && !portableObject.SetItem(itemId))
            {
                shouldBeActive = false;
            }

            if (portableObject.gameObject.activeSelf != shouldBeActive)
            {
                portableObject.gameObject.SetActive(shouldBeActive);
            }
        }

        currentStack[slotIndex] = clampedCount;
        if (slotIndex < visualPreservedStackCounts.Count)
        {
            visualPreservedStackCounts[slotIndex] = 0;
        }
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

    public int GetSlotDisplayItemId(int index)
    {
        int itemId = GetSlotItemId(index);
        if (itemId >= 0)
        {
            return itemId;
        }

        return GetVisualPreservedItemId(index);
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

    private void EnsureVisualPreservedStackCounts()
    {
        int targetCount = portableStack != null ? portableStack.Count : 0;
        while (visualPreservedStackCounts.Count < targetCount)
        {
            visualPreservedStackCounts.Add(0);
        }

        while (visualPreservedStackCounts.Count > targetCount)
        {
            visualPreservedStackCounts.RemoveAt(visualPreservedStackCounts.Count - 1);
        }
    }

    private int GetVisualPreservedStackCount(int index)
    {
        if (index < 0 || index >= visualPreservedStackCounts.Count)
        {
            return 0;
        }

        return Mathf.Max(0, visualPreservedStackCounts[index]);
    }

    private bool HasAnyVisualPreservedStackCounts()
    {
        for (int i = 0; i < visualPreservedStackCounts.Count; i++)
        {
            if (visualPreservedStackCounts[i] > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void ClampVisualPreservedStackCount(int index)
    {
        if (portableStack == null
            || currentStack == null
            || index < 0
            || index >= portableStack.Count
            || index >= currentStack.Count
            || index >= visualPreservedStackCounts.Count)
        {
            return;
        }

        PortableStack stack = portableStack[index];
        int activeCount = CountContiguousActiveObjects(stack);
        int logicalCount = Mathf.Clamp(currentStack[index], 0, activeCount);
        int maxPreservedCount = Mathf.Max(0, activeCount - logicalCount);
        visualPreservedStackCounts[index] = Mathf.Clamp(visualPreservedStackCounts[index], 0, maxPreservedCount);
    }

    private int CountContiguousActiveObjects(PortableStack stack)
    {
        if (stack == null || stack.stack == null)
        {
            return 0;
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

        return count;
    }

    private int GetVisualPreservedItemId(int index)
    {
        return TryGetVisualPreservedObject(index, -1, out _, out PortableObject portableObject) && portableObject != null
            ? portableObject.ItemId
            : -1;
    }

    private bool TryGetVisualPreservedObject(
        int index,
        int objectId,
        out int objectIndex,
        out PortableObject targetPortableObject)
    {
        return TryGetVisualPreservedObject(index, objectId, false, out objectIndex, out targetPortableObject);
    }

    private bool TryGetVisualPreservedObject(
        int index,
        int objectId,
        bool topFirst,
        out int objectIndex,
        out PortableObject targetPortableObject)
    {
        objectIndex = -1;
        targetPortableObject = null;
        if (portableStack == null
            || currentStack == null
            || index < 0
            || index >= portableStack.Count
            || index >= currentStack.Count
            || index >= visualPreservedStackCounts.Count)
        {
            return false;
        }

        int preservedCount = GetVisualPreservedStackCount(index);
        if (preservedCount <= 0)
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        if (stack == null || stack.stack == null || stack.stack.Count == 0)
        {
            return false;
        }

        int logicalCount = Mathf.Clamp(currentStack[index], 0, stack.stack.Count);
        int activeCount = CountContiguousActiveObjects(stack);
        int preservedEndExclusive = Mathf.Min(activeCount, logicalCount + preservedCount);
        int startIndex = topFirst ? preservedEndExclusive - 1 : logicalCount;
        int endIndex = topFirst ? logicalCount - 1 : preservedEndExclusive;
        int step = topFirst ? -1 : 1;
        for (int i = startIndex; i != endIndex; i += step)
        {
            PortableObject portableObject = stack.stack[i];
            if (portableObject == null
                || !portableObject.gameObject.activeSelf
                || reservedObjects.Contains(portableObject))
            {
                continue;
            }

            if (objectId >= 0 && portableObject.ItemId != objectId)
            {
                continue;
            }

            objectIndex = i;
            targetPortableObject = portableObject;
            return true;
        }

        return false;
    }

    private bool TryRestoreVisualPreservedObjectToSlot(
        int index,
        int objectId,
        bool notify,
        out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (!TryGetVisualPreservedObject(index, objectId, out _, out targetPortableObject))
        {
            return false;
        }

        visualPreservedStackCounts[index] = Mathf.Max(0, visualPreservedStackCounts[index] - 1);
        PortableStack stack = portableStack[index];
        currentStack[index] = Mathf.Clamp(currentStack[index] + 1, 0, stack.stack.Count);
        if (notify)
        {
            NotifyChanged();
        }

        return true;
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
