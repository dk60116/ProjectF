using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;

public class PortableObjectPool : MonoBehaviour
{
    [SerializeField]
    private PortableObject defaultPrefab;

    private readonly Stack<PortableObject> pooledObjects = new Stack<PortableObject>();
    private Transform poolRoot;

    public void Configure(PortableObject prefab)
    {
        if (prefab != null && defaultPrefab == null)
        {
            defaultPrefab = prefab;
        }
    }

    public PortableObject Get(PortableObject prefabOverride = null)
    {
        PortableObject prefab = prefabOverride != null ? prefabOverride : defaultPrefab;
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

        PortableObject created = Instantiate(prefab, GetPoolRoot());
        created.gameObject.SetActive(false);
        PrepareBorrowedObject(created);
        return created;
    }

    public void Release(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.transform.DOKill();
        portableObject.gameObject.SetActive(false);
        portableObject.transform.SetParent(GetPoolRoot(), false);
        portableObject.transform.localPosition = Vector3.zero;
        portableObject.transform.localRotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        pooledObjects.Push(portableObject);
    }

    private void PrepareBorrowedObject(PortableObject portableObject)
    {
        portableObject.transform.DOKill();
        portableObject.gameObject.SetActive(true);
    }

    private Transform GetPoolRoot()
    {
        if (poolRoot != null)
        {
            return poolRoot;
        }

        GameObject rootObject = new GameObject("PortableObjectPool");
        rootObject.transform.SetParent(transform, false);
        poolRoot = rootObject.transform;
        return poolRoot;
    }
}
