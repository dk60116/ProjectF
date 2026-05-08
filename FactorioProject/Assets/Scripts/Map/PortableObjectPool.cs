using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;

public class PortableObjectPool : MonoBehaviour
{
    [SerializeField]
    private PortableObject defaultPrefab;

    private readonly Stack<PortableObject> pooledObjects = new Stack<PortableObject>();
    private Transform poolRoot;
    private bool isDestroying;

    public bool CanRelease => !isDestroying && this != null;

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
        portableObject.SetSleepAwakeSleeping(false);
        portableObject.ClearBeltItemLineDebugColor();
        portableObject.SetBatchedRendering(false);
        portableObject.gameObject.SetActive(false);
        Transform root = GetPoolRoot();
        if (root == null)
        {
            DestroyReleasedObject(portableObject);
            return;
        }

        portableObject.transform.SetParent(root, false);
        portableObject.transform.localPosition = Vector3.zero;
        portableObject.transform.localRotation = Quaternion.identity;
        portableObject.transform.localScale = Vector3.one;
        pooledObjects.Push(portableObject);
    }

    private void OnDestroy()
    {
        isDestroying = true;
        pooledObjects.Clear();
        poolRoot = null;
    }

    private void PrepareBorrowedObject(PortableObject portableObject)
    {
        portableObject.transform.DOKill();
        portableObject.SetSleepAwakeSleeping(false);
        portableObject.ClearBeltItemLineDebugColor();
        portableObject.SetBatchedRendering(false);
        portableObject.gameObject.SetActive(true);
    }

    private Transform GetPoolRoot()
    {
        if (!CanRelease)
        {
            return null;
        }

        if (poolRoot != null)
        {
            return poolRoot;
        }

        GameObject rootObject = new GameObject("PortableObjectPool");
        rootObject.transform.SetParent(transform, false);
        poolRoot = rootObject.transform;
        return poolRoot;
    }

    private static void DestroyReleasedObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(portableObject.gameObject);
        }
        else
        {
            Object.DestroyImmediate(portableObject.gameObject);
        }
    }
}
