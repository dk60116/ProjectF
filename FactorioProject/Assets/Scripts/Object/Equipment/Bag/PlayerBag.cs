using System.Collections.Generic;
using UnityEngine;

public class PlayerBag : MonoBehaviour
{
    public event System.Action Changed;

    [SerializeField]
    private List<PortableStack> portableStack;

    [SerializeField]
    private List<int> currentStack;

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

    public void AddObject(int index, int objectId)
    {
        EnsureInitialized();
        if (!TryGetNextSlot(index, objectId, out PortableObject targetPortableObject))
        {
            return;
        }

        targetPortableObject.gameObject.SetActive(true);
        if (!targetPortableObject.SetItem(objectId))
        {
            targetPortableObject.gameObject.SetActive(false);
            return;
        }

        currentStack[index] = Mathf.Clamp(currentStack[index] + 1, 0, portableStack[index].stack.Count);
        NotifyChanged();
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

        int nextIndex = currentStack[index];
        if (nextIndex < 0 || nextIndex >= stack.stack.Count || stack.stack[nextIndex] == null)
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
            targetPortableObject = portableStack[index].stack[currentStack[index]];
            AddObject(index, objectId);
            return targetPortableObject != null;
        }

        return TryAddObject(objectId, out targetPortableObject);
    }

    public bool TryAddObject(int objectId, out PortableObject targetPortableObject)
    {
        EnsureInitialized();
        targetPortableObject = null;

        int sameItemStackIndex = FindStackIndexForObject(objectId, true);
        if (sameItemStackIndex >= 0)
        {
            targetPortableObject = portableStack[sameItemStackIndex].stack[currentStack[sameItemStackIndex]];
            AddObject(sameItemStackIndex, objectId);
            return targetPortableObject != null;
        }

        int emptyStackIndex = FindStackIndexForObject(objectId, false);
        if (emptyStackIndex >= 0)
        {
            targetPortableObject = portableStack[emptyStackIndex].stack[currentStack[emptyStackIndex]];
            AddObject(emptyStackIndex, objectId);
            return targetPortableObject != null;
        }

        return false;
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
            return stack.stack[0];
        }

        return stack.stack[occupiedCount - 1];
    }

    private bool TryGetNextSlot(int index, int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        if (!CanAddObject(index, objectId))
        {
            return false;
        }

        PortableStack stack = portableStack[index];
        int stackIndex = currentStack[index];
        if (stack == null || stack.stack == null || stackIndex < 0 || stackIndex >= stack.stack.Count)
        {
            return false;
        }

        targetPortableObject = stack.stack[stackIndex];
        return targetPortableObject != null;
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

        PortableObject bottomObject = stack.stack[0];
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

        PortableObject bottomObject = stack.stack[0];
        return bottomObject != null ? bottomObject.ItemId : -1;
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
