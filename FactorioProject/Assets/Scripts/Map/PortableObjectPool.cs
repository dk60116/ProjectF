using System.Collections.Generic;
using UnityEngine;

public class PortableObjectPool : MonoBehaviour
{
    [SerializeField]
    private PortableObjectTemplate defaultPrefab;

    private readonly Stack<PortableObject> pooledObjects = new Stack<PortableObject>();
    private bool isDestroying;

    public bool CanRelease => !isDestroying && this != null;

    public void Configure(PortableObjectTemplate prefab)
    {
        if (prefab != null && defaultPrefab == null)
        {
            defaultPrefab = prefab;
        }
    }

    public PortableObject Get(PortableObjectTemplate prefabOverride = null)
    {
        PortableObjectTemplate prefab = prefabOverride != null ? prefabOverride : defaultPrefab;
        if (prefab == null)
        {
            return null;
        }

        if (defaultPrefab == null)
        {
            defaultPrefab = prefab;
        }

        while (pooledObjects.Count > 0)
        {
            PortableObject pooled = pooledObjects.Pop();
            if (pooled == null)
            {
                continue;
            }

            PrepareBorrowedObject(pooled);
            return pooled;
        }

        PortableObject created = PortableObject.Create(prefab);
        created.SetCachedActive(false);
        PrepareBorrowedObject(created);
        return created;
    }

    public void Release(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.CancelMove();
        portableObject.SetSleepAwakeSleeping(false);
        portableObject.ClearBeltItemLineDebugColor();
        portableObject.SetBatchedRendering(false);
        portableObject.SetCachedActive(false);
        portableObject.SetCachedParent(null, true);
        portableObject.SetWorldPose(Vector3.zero, Quaternion.identity);
        portableObject.SetWorldScale(Vector3.one);
        pooledObjects.Push(portableObject);
    }

    private void OnDestroy()
    {
        isDestroying = true;
        while (pooledObjects.Count > 0)
        {
            pooledObjects.Pop()?.Dispose();
        }
    }

    private void PrepareBorrowedObject(PortableObject portableObject)
    {
        portableObject.CancelMove();
        portableObject.SetSleepAwakeSleeping(false);
        portableObject.ClearBeltItemLineDebugColor();
        portableObject.SetBatchedRendering(false);
        portableObject.SetCachedActive(true);
    }
}
