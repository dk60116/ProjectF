using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class VirtualItemStackRenderer : MonoBehaviour
{
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
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
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
        batches.ClearActiveMatrices();
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

        VirtualRenderBatchKey key = new VirtualRenderBatchKey(
            mesh,
            material,
            gameObject.layer,
            0,
            ShadowCastingMode.On,
            true,
            false,
            0,
            false,
            false,
            default,
            itemId);
        Vector3 position = record.worldPosition + new Vector3(0f, stackBaseYOffset + (stackVerticalSpacing * stackIndex), 0f);
        batches.AddMatrix(key, Matrix4x4.TRS(position, record.worldRotation, Vector3.one));
    }

    private void RenderBatches()
    {
        batches.RenderBatches();
    }
}
