using System.Collections.Generic;
using UnityEngine;

public class BlockPool : MonoBehaviour
{
    private readonly Dictionary<GameObject, Stack<Block>> pooledBlocksByPrefab = new Dictionary<GameObject, Stack<Block>>();
    private readonly Dictionary<Block, GameObject> prefabByBlock = new Dictionary<Block, GameObject>();
    private Transform poolRoot;

    public Transform PoolRoot => poolRoot;

    public void Prewarm(GameObject prefab, int targetAvailableCount)
    {
        if (prefab == null || targetAvailableCount <= 0)
        {
            return;
        }

        EnsurePoolRoot();
        Stack<Block> pooledBlocks = GetOrCreateStack(prefab);
        int missingCount = targetAvailableCount - CountValidBlocks(pooledBlocks);
        for (int i = 0; i < missingCount; i++)
        {
            Block block = CreateBlockInstance(prefab, poolRoot);
            if (block == null)
            {
                continue;
            }

            ReturnBlockToPool(block, prefab);
        }
    }

    public void TrimAvailable(GameObject prefab, int maxAvailableCount)
    {
        if (prefab == null || !pooledBlocksByPrefab.TryGetValue(prefab, out Stack<Block> pooledBlocks))
        {
            return;
        }

        int validCount = CountValidBlocks(pooledBlocks);
        int normalizedMax = Mathf.Max(0, maxAvailableCount);
        while (validCount > normalizedMax && pooledBlocks.Count > 0)
        {
            Block block = pooledBlocks.Pop();
            if (block == null)
            {
                continue;
            }

            prefabByBlock.Remove(block);
            DestroyBlockObject(block);
            validCount--;
        }
    }

    public Block Get(GameObject prefab, Transform parent)
    {
        if (prefab == null)
        {
            return null;
        }

        EnsurePoolRoot();

        if (pooledBlocksByPrefab.TryGetValue(prefab, out Stack<Block> pooledBlocks))
        {
            while (pooledBlocks.Count > 0)
            {
                Block pooledBlock = pooledBlocks.Pop();
                if (pooledBlock == null)
                {
                    continue;
                }

                pooledBlock.transform.SetParent(parent, false);
                prefabByBlock[pooledBlock] = prefab;
                pooledBlock.gameObject.SetActive(true);
                return pooledBlock;
            }
        }

        return CreateBlockInstance(prefab, parent);
    }

    public void Release(Block block)
    {
        if (block == null)
        {
            return;
        }

        if (!prefabByBlock.TryGetValue(block, out GameObject prefab) || prefab == null)
        {
            if (Application.isPlaying)
            {
                Destroy(block.gameObject);
            }
            else
            {
                DestroyImmediate(block.gameObject);
            }

            return;
        }

        EnsurePoolRoot();
        ReturnBlockToPool(block, prefab);
    }

    private Block CreateBlockInstance(GameObject prefab, Transform parent)
    {
        GameObject blockObject = Instantiate(prefab, parent);
        Block block = blockObject.GetComponent<Block>();
        if (block == null)
        {
            block = blockObject.AddComponent<Block>();
        }

        prefabByBlock[block] = prefab;
        return block;
    }

    private Stack<Block> GetOrCreateStack(GameObject prefab)
    {
        if (!pooledBlocksByPrefab.TryGetValue(prefab, out Stack<Block> pooledBlocks))
        {
            pooledBlocks = new Stack<Block>();
            pooledBlocksByPrefab[prefab] = pooledBlocks;
        }

        return pooledBlocks;
    }

    private void ReturnBlockToPool(Block block, GameObject prefab)
    {
        if (block == null || prefab == null)
        {
            return;
        }

        prefabByBlock[block] = prefab;
        EnsurePoolRoot();
        block.PrepareForPool();
        block.transform.SetParent(poolRoot, false);
        block.gameObject.SetActive(false);
        GetOrCreateStack(prefab).Push(block);
    }

    private static int CountValidBlocks(Stack<Block> pooledBlocks)
    {
        if (pooledBlocks == null || pooledBlocks.Count == 0)
        {
            return 0;
        }

        int count = 0;
        foreach (Block block in pooledBlocks)
        {
            if (block != null)
            {
                count++;
            }
        }

        return count;
    }

    private static void DestroyBlockObject(Block block)
    {
        if (block == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(block.gameObject);
        }
        else
        {
            DestroyImmediate(block.gameObject);
        }
    }

    private void EnsurePoolRoot()
    {
        if (poolRoot != null)
        {
            return;
        }

        GameObject poolRootObject = new GameObject("__BlockPool");
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
}

public class MapObjectPool : MonoBehaviour
{
    private readonly Dictionary<MapObject, Stack<MapObject>> pooledObjectsByPrefab = new Dictionary<MapObject, Stack<MapObject>>();
    private readonly Dictionary<MapObject, MapObject> prefabByObject = new Dictionary<MapObject, MapObject>();
    private Transform poolRoot;

    public T Get<T>(T prefab, Transform parent) where T : MapObject
    {
        if (prefab == null)
        {
            return null;
        }

        EnsurePoolRoot();
        if (pooledObjectsByPrefab.TryGetValue(prefab, out Stack<MapObject> pooledObjects))
        {
            while (pooledObjects.Count > 0)
            {
                MapObject pooledObject = pooledObjects.Pop();
                if (pooledObject == null)
                {
                    continue;
                }

                if (pooledObject is T typedObject)
                {
                    prefabByObject[typedObject] = prefab;
                    PrepareForUse(typedObject, prefab, parent);
                    return typedObject;
                }

                DestroyMapObject(pooledObject);
            }
        }

        T createdObject = Instantiate(prefab, parent);
        prefabByObject[createdObject] = prefab;
        PrepareForUse(createdObject, prefab, parent);
        return createdObject;
    }

    public void Release(MapObject mapObject)
    {
        Release(mapObject, null);
    }

    public void Release(MapObject mapObject, MapObject sourcePrefab)
    {
        if (mapObject == null)
        {
            return;
        }

        MapObject prefab = sourcePrefab;
        if (prefab == null && !prefabByObject.TryGetValue(mapObject, out prefab))
        {
            DestroyMapObject(mapObject);
            return;
        }

        if (prefab == null)
        {
            DestroyMapObject(mapObject);
            return;
        }

        ReturnObjectToPool(mapObject, prefab);
    }

    private static void PrepareForUse(MapObject mapObject, MapObject prefab, Transform parent)
    {
        mapObject.transform.SetParent(parent, false);
        if (prefab != null)
        {
            mapObject.transform.localPosition = prefab.transform.localPosition;
            mapObject.transform.localRotation = prefab.transform.localRotation;
            mapObject.transform.localScale = prefab.transform.localScale;
        }

        mapObject.gameObject.SetActive(true);
    }

    private void ReturnObjectToPool(MapObject mapObject, MapObject prefab)
    {
        prefabByObject[mapObject] = prefab;
        EnsurePoolRoot();
        mapObject.gameObject.SetActive(false);
        mapObject.transform.SetParent(poolRoot, false);
        mapObject.transform.localPosition = Vector3.zero;
        mapObject.transform.localRotation = Quaternion.identity;
        mapObject.transform.localScale = Vector3.one;
        GetOrCreateStack(prefab).Push(mapObject);
    }

    private Stack<MapObject> GetOrCreateStack(MapObject prefab)
    {
        if (!pooledObjectsByPrefab.TryGetValue(prefab, out Stack<MapObject> pooledObjects))
        {
            pooledObjects = new Stack<MapObject>();
            pooledObjectsByPrefab[prefab] = pooledObjects;
        }

        return pooledObjects;
    }

    private void EnsurePoolRoot()
    {
        if (poolRoot != null)
        {
            return;
        }

        GameObject poolRootObject = new GameObject("__MapObjectPool");
        if (Application.isPlaying)
        {
            poolRootObject.hideFlags = HideFlags.HideInHierarchy;
        }

        poolRoot = poolRootObject.transform;
        poolRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private static void DestroyMapObject(MapObject mapObject)
    {
        if (mapObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mapObject.gameObject);
        }
        else
        {
            DestroyImmediate(mapObject.gameObject);
        }
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
}
