using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class InstallationObjectPool : MonoBehaviour
{
    private readonly Dictionary<MapObject, Stack<InstallationObject>> pooledObjectsByPrefab = new Dictionary<MapObject, Stack<InstallationObject>>();
    private readonly Dictionary<InstallationObject, MapObject> prefabByObject = new Dictionary<InstallationObject, MapObject>();
    private Transform poolRoot;

    public Transform PoolRoot => poolRoot;

    public InstallationObject Get(MapObject prefab, Transform parent)
    {
        if (prefab == null)
        {
            return null;
        }

        EnsurePoolRoot();

        if (pooledObjectsByPrefab.TryGetValue(prefab, out Stack<InstallationObject> pooledObjects))
        {
            while (pooledObjects.Count > 0)
            {
                InstallationObject pooledObject = pooledObjects.Pop();
                if (pooledObject == null)
                {
                    continue;
                }

                PrepareBorrowedObject(pooledObject, prefab, parent);
                return pooledObject;
            }
        }

        MapObject createdObject = Instantiate(prefab, parent);
        if (!(createdObject is InstallationObject installationObject))
        {
            if (createdObject != null)
            {
                DestroyObject(createdObject.gameObject);
            }

            return null;
        }

        prefabByObject[installationObject] = prefab;
        return installationObject;
    }

    public void Release(InstallationObject installationObject, MapObject sourcePrefab = null)
    {
        if (installationObject == null)
        {
            return;
        }

        MapObject prefab = sourcePrefab != null ? sourcePrefab : null;
        if (prefab == null)
        {
            prefabByObject.TryGetValue(installationObject, out prefab);
        }

        if (prefab == null)
        {
            DestroyObject(installationObject.gameObject);
            return;
        }

        EnsurePoolRoot();
        prefabByObject[installationObject] = prefab;
        PrepareReleasedObject(installationObject, prefab);

        if (!pooledObjectsByPrefab.TryGetValue(prefab, out Stack<InstallationObject> pooledObjects))
        {
            pooledObjects = new Stack<InstallationObject>();
            pooledObjectsByPrefab[prefab] = pooledObjects;
        }

        pooledObjects.Push(installationObject);
    }

    private void PrepareBorrowedObject(InstallationObject installationObject, MapObject prefab, Transform parent)
    {
        installationObject.transform.DOKill();
        installationObject.transform.SetParent(parent, false);
        installationObject.transform.localScale = prefab.transform.localScale;
        installationObject.gameObject.name = prefab.name;
        installationObject.gameObject.SetActive(true);
    }

    private void PrepareReleasedObject(InstallationObject installationObject, MapObject prefab)
    {
        installationObject.transform.DOKill();
        if (installationObject is ConveyorBelt conveyorBelt)
        {
            TerrainGenerator.Active?.UnregisterVirtualConveyorBelt(conveyorBelt);
        }

        installationObject.PrepareForPool();
        ResetKnownRuntimeState(installationObject, prefab);
        installationObject.transform.SetParent(poolRoot, false);
        installationObject.transform.localPosition = Vector3.zero;
        installationObject.transform.localRotation = prefab.transform.localRotation;
        installationObject.transform.localScale = prefab.transform.localScale;
        installationObject.gameObject.SetActive(false);
    }

    private static void ResetKnownRuntimeState(InstallationObject installationObject, MapObject prefab)
    {
        if (installationObject is BoxObject boxObject && prefab is BoxObject boxPrefab)
        {
            boxObject.SetOpenState(boxPrefab.IsOpen, false);
        }

        if (installationObject is FenceDoor fenceDoor && prefab is FenceDoor fenceDoorPrefab)
        {
            fenceDoor.SetOpenState(fenceDoorPrefab.IsOpen, false);
        }
    }

    private void EnsurePoolRoot()
    {
        if (poolRoot != null)
        {
            return;
        }

        GameObject poolRootObject = new GameObject("__InstallationObjectPool");
        poolRootObject.transform.SetParent(transform, false);
        if (Application.isPlaying)
        {
            poolRootObject.hideFlags = HideFlags.HideInHierarchy;
        }

        poolRoot = poolRootObject.transform;
        poolRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private void OnDestroy()
    {
        if (poolRoot == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(poolRoot.gameObject);
        }
        else
        {
            DestroyImmediate(poolRoot.gameObject);
        }
    }

    private static void DestroyObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
