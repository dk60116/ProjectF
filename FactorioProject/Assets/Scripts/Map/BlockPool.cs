using System.Collections.Generic;
using UnityEngine;

public class BlockPool : MonoBehaviour
{
    private readonly Dictionary<GameObject, Stack<Block>> pooledBlocksByPrefab = new Dictionary<GameObject, Stack<Block>>();
    private readonly Dictionary<Block, GameObject> prefabByBlock = new Dictionary<Block, GameObject>();
    private Transform poolRoot;

    public Transform PoolRoot => poolRoot;

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
                pooledBlock.gameObject.SetActive(true);
                return pooledBlock;
            }
        }

        GameObject blockObject = Instantiate(prefab, parent);
        Block block = blockObject.GetComponent<Block>();
        if (block == null)
        {
            block = blockObject.AddComponent<Block>();
        }

        prefabByBlock[block] = prefab;
        return block;
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
        block.PrepareForPool();
        block.transform.SetParent(poolRoot, false);
        block.gameObject.SetActive(false);

        if (!pooledBlocksByPrefab.TryGetValue(prefab, out Stack<Block> pooledBlocks))
        {
            pooledBlocks = new Stack<Block>();
            pooledBlocksByPrefab[prefab] = pooledBlocks;
        }

        pooledBlocks.Push(block);
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
