using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        portableObject.SetSleepAwakeSleeping(false);
        portableObject.SetBatchedRendering(false);
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
        portableObject.SetSleepAwakeSleeping(false);
        portableObject.SetBatchedRendering(false);
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

[DisallowMultipleComponent]
public class PortableObjectBatchRenderer : MonoBehaviour
{
    private const int MaxInstancesPerDraw = 1023;

    [SerializeField, Min(1f)]
    private float batchCellSize = 8f;

    private readonly HashSet<PortableObject> registeredObjects = new HashSet<PortableObject>();
    private readonly Dictionary<BatchKey, List<Matrix4x4>> matricesByBatch = new Dictionary<BatchKey, List<Matrix4x4>>();
    private readonly List<BatchKey> activeBatchKeys = new List<BatchKey>();
    private readonly List<PortableObject> cleanupBuffer = new List<PortableObject>();
    private bool batchesDirty = true;

    public void Register(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (registeredObjects.Add(portableObject))
        {
            batchesDirty = true;
        }
    }

    public void Unregister(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        if (registeredObjects.Remove(portableObject))
        {
            batchesDirty = true;
        }
    }

    public void MarkDirty()
    {
        batchesDirty = true;
    }

    private void LateUpdate()
    {
        if (registeredObjects.Count <= 0)
        {
            return;
        }

        if (batchesDirty)
        {
            RebuildBatches();
            batchesDirty = false;
        }

        RenderBatches();
    }

    private void RebuildBatches()
    {
        for (int i = 0; i < activeBatchKeys.Count; i++)
        {
            BatchKey key = activeBatchKeys[i];
            if (matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices))
            {
                matrices.Clear();
            }
        }

        activeBatchKeys.Clear();
        cleanupBuffer.Clear();

        foreach (PortableObject portableObject in registeredObjects)
        {
            if (portableObject == null)
            {
                cleanupBuffer.Add(portableObject);
                continue;
            }

            if (!portableObject.TryGetBatchRenderData(
                    out Mesh mesh,
                    out Material material,
                    out Matrix4x4 localToWorldMatrix,
                    out Vector3 worldPosition,
                    out int layer,
                    out ShadowCastingMode shadowCastingMode,
                    out bool receiveShadows,
                    out bool useSleepAwakeDarkTint))
            {
                continue;
            }

            if (material != null && !material.enableInstancing)
            {
                material.enableInstancing = true;
            }

            int cellX = Mathf.FloorToInt(worldPosition.x / batchCellSize);
            int cellZ = Mathf.FloorToInt(worldPosition.z / batchCellSize);
            BatchKey key = new BatchKey(mesh, material, layer, shadowCastingMode, receiveShadows, useSleepAwakeDarkTint, cellX, cellZ);
            if (!matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices))
            {
                matrices = new List<Matrix4x4>(16);
                matricesByBatch.Add(key, matrices);
            }

            if (matrices.Count == 0)
            {
                activeBatchKeys.Add(key);
            }

            matrices.Add(localToWorldMatrix);
        }

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            registeredObjects.Remove(cleanupBuffer[i]);
        }
    }

    private void RenderBatches()
    {
        for (int batchIndex = 0; batchIndex < activeBatchKeys.Count; batchIndex++)
        {
            BatchKey key = activeBatchKeys[batchIndex];
            if (!matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices) || matrices.Count <= 0)
            {
                continue;
            }

            RenderParams renderParams = new RenderParams(key.Material)
            {
                layer = key.Layer,
                shadowCastingMode = key.ShadowCastingMode,
                receiveShadows = key.ReceiveShadows,
                matProps = ResolveBatchPropertyBlock(key)
            };

            int remaining = matrices.Count;
            int startIndex = 0;
            while (remaining > 0)
            {
                int drawCount = Mathf.Min(MaxInstancesPerDraw, remaining);
                Graphics.RenderMeshInstanced(renderParams, key.Mesh, 0, matrices, drawCount, startIndex);
                startIndex += drawCount;
                remaining -= drawCount;
            }
        }
    }

    private MaterialPropertyBlock sleepAwakePropertyBlock;

    private MaterialPropertyBlock ResolveBatchPropertyBlock(BatchKey key)
    {
        if (!key.UseSleepAwakeDarkTint)
        {
            return null;
        }

        sleepAwakePropertyBlock ??= new MaterialPropertyBlock();
        sleepAwakePropertyBlock.Clear();
        SleepAwakeDebugVisual.ApplySleepingColor(sleepAwakePropertyBlock, key.Material);
        return sleepAwakePropertyBlock;
    }

    private readonly struct BatchKey
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly int Layer;
        public readonly ShadowCastingMode ShadowCastingMode;
        public readonly bool ReceiveShadows;
        public readonly bool UseSleepAwakeDarkTint;
        public readonly int CellX;
        public readonly int CellZ;

        public BatchKey(
            Mesh mesh,
            Material material,
            int layer,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows,
            bool useSleepAwakeDarkTint,
            int cellX,
            int cellZ)
        {
            Mesh = mesh;
            Material = material;
            Layer = layer;
            ShadowCastingMode = shadowCastingMode;
            ReceiveShadows = receiveShadows;
            UseSleepAwakeDarkTint = useSleepAwakeDarkTint;
            CellX = cellX;
            CellZ = cellZ;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Mesh != null ? Mesh.GetInstanceID() : 0;
                hash = (hash * 397) ^ (Material != null ? Material.GetInstanceID() : 0);
                hash = (hash * 397) ^ Layer;
                hash = (hash * 397) ^ (int)ShadowCastingMode;
                hash = (hash * 397) ^ (ReceiveShadows ? 1 : 0);
                hash = (hash * 397) ^ (UseSleepAwakeDarkTint ? 1 : 0);
                hash = (hash * 397) ^ CellX;
                hash = (hash * 397) ^ CellZ;
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            return obj is BatchKey other && Equals(other);
        }

        private bool Equals(BatchKey other)
        {
            return Mesh == other.Mesh
                   && Material == other.Material
                   && Layer == other.Layer
                   && ShadowCastingMode == other.ShadowCastingMode
                   && ReceiveShadows == other.ReceiveShadows
                   && UseSleepAwakeDarkTint == other.UseSleepAwakeDarkTint
                   && CellX == other.CellX
                   && CellZ == other.CellZ;
        }
    }
}
