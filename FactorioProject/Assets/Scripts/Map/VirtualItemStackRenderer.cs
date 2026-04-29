using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class VirtualItemStackRenderer : MonoBehaviour
{
    private const int MaxInstancesPerDraw = 1023;

    [SerializeField, Min(0f)]
    private float stackBaseYOffset = 0.2f;

    [SerializeField, Min(0.001f)]
    private float stackVerticalSpacing = 0.05f;

    [SerializeField, Min(1)]
    private int maxRenderedItemsPerStack = 256;

    [SerializeField, Min(0f)]
    private float renderDistance = 90f;

    [SerializeField]
    private bool renderOnlyVirtualRecords = true;

    private readonly List<VirtualObjectRecord> recordSnapshot = new List<VirtualObjectRecord>(512);
    private readonly Dictionary<BatchKey, List<Matrix4x4>> matricesByBatch = new Dictionary<BatchKey, List<Matrix4x4>>();
    private readonly List<BatchKey> activeBatchKeys = new List<BatchKey>();
    private readonly List<int> itemBuffer = new List<int>(64);
    private VirtualObjectWorld virtualWorld;
    private ItemManager itemManager;
    private Camera mainCamera;
    private int cachedWorldVersion = -1;

    public void Configure(VirtualObjectWorld world, ItemManager manager)
    {
        virtualWorld = world;
        itemManager = manager;
        cachedWorldVersion = -1;
    }

    private void Awake()
    {
        ResolveDependencies();
    }

    private void LateUpdate()
    {
        ResolveDependencies();
        if (virtualWorld == null || itemManager == null)
        {
            return;
        }

        if (cachedWorldVersion != virtualWorld.Version)
        {
            RebuildBatches();
            cachedWorldVersion = virtualWorld.Version;
        }

        RenderBatches();
    }

    private void ResolveDependencies()
    {
        if (virtualWorld == null)
        {
            virtualWorld = GetComponent<VirtualObjectWorld>();
            if (virtualWorld == null)
            {
                virtualWorld = VirtualObjectWorld.Current;
            }
        }

        if (itemManager == null && GameManager.Instance != null)
        {
            itemManager = GameManager.Instance.ItemManger;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
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
        virtualWorld.CopyRecords(recordSnapshot, !renderOnlyVirtualRecords);
        Vector3 cameraPosition = mainCamera != null ? mainCamera.transform.position : Vector3.zero;
        float renderDistanceSqr = renderDistance > 0f ? renderDistance * renderDistance : float.MaxValue;

        for (int recordIndex = 0; recordIndex < recordSnapshot.Count; recordIndex++)
        {
            VirtualObjectRecord record = recordSnapshot[recordIndex];
            if (record == null
                || record.kind != VirtualObjectKind.ItemStack
                || record.itemStack == null
                || (renderOnlyVirtualRecords && record.residency != VirtualObjectResidency.Virtual))
            {
                continue;
            }

            if (renderDistance > 0f && mainCamera != null)
            {
                Vector3 offset = record.worldPosition - cameraPosition;
                offset.y = 0f;
                if (offset.sqrMagnitude > renderDistanceSqr)
                {
                    continue;
                }
            }

            itemBuffer.Clear();
            record.itemStack.CopyTo(itemBuffer);
            if (!Block.IsVirtualizableFloorObjectState(itemBuffer))
            {
                continue;
            }

            int renderCount = Mathf.Min(itemBuffer.Count, Mathf.Max(1, maxRenderedItemsPerStack));
            for (int itemIndex = 0; itemIndex < renderCount; itemIndex++)
            {
                AddItemMatrix(itemBuffer[itemIndex], record, itemIndex);
            }
        }
    }

    private void AddItemMatrix(int itemId, VirtualObjectRecord record, int stackIndex)
    {
        if (itemId < 0 || !itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            return;
        }

        Mesh mesh = itemSet.portableMesh;
        Material material = itemSet.portableMat;
        if (mesh == null || material == null)
        {
            return;
        }

        if (!material.enableInstancing)
        {
            material.enableInstancing = true;
        }

        BatchKey key = new BatchKey(mesh, material, gameObject.layer);
        if (!matricesByBatch.TryGetValue(key, out List<Matrix4x4> matrices))
        {
            matrices = new List<Matrix4x4>(64);
            matricesByBatch.Add(key, matrices);
        }

        if (matrices.Count == 0)
        {
            activeBatchKeys.Add(key);
        }

        Vector3 position = record.worldPosition + new Vector3(0f, stackBaseYOffset + (stackVerticalSpacing * stackIndex), 0f);
        matrices.Add(Matrix4x4.TRS(position, record.worldRotation, Vector3.one));
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
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true
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

    private readonly struct BatchKey
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly int Layer;

        public BatchKey(Mesh mesh, Material material, int layer)
        {
            Mesh = mesh;
            Material = material;
            Layer = layer;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Mesh != null ? Mesh.GetInstanceID() : 0;
                hash = (hash * 397) ^ (Material != null ? Material.GetInstanceID() : 0);
                hash = (hash * 397) ^ Layer;
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            return obj is BatchKey other
                   && Mesh == other.Mesh
                   && Material == other.Material
                   && Layer == other.Layer;
        }
    }
}
