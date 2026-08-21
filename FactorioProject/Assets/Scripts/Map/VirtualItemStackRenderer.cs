using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class VirtualItemStackRenderer : MonoBehaviour
{
    private const int LegacyFloorStackCapacity = 10;

    private static readonly Vector3[] DefaultFloorSlotOffsets =
    {
        new Vector3(0f, 0f, -0.35000002f),
        new Vector3(0.3f, 0f, 0f),
        new Vector3(0f, 0f, 0.35f),
        new Vector3(-0.35000002f, 0f, 0f)
    };

    [SerializeField, Min(0f)]
    private float stackBaseYOffset = 0.05f;

    [SerializeField, Min(0.001f)]
    private float stackVerticalSpacing = 0.05f;

    [SerializeField, Min(1)]
    private int maxRenderedItemsPerStack = 256;

    [SerializeField, Min(0f)]
    private float renderDistance = 90f;

    [SerializeField]
    private bool renderOnlyVirtualRecords = true;

    [SerializeField, Min(1f)]
    private float batchCellSize = 8f;

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

    private void OnDestroy()
    {
        batches.Dispose();
    }

    private void OnDisable()
    {
        batches.SuspendRendering();
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

            AddFloorItemMatrices(record, itemBuffer);
        }
    }

    private void AddFloorItemMatrices(VirtualObjectRecord record, IReadOnlyList<int> itemIds)
    {
        int maxRenderCount = Mathf.Max(1, maxRenderedItemsPerStack);
        int renderedCount = 0;
        bool parsedStructuredState = false;

        for (int i = 0; i < itemIds.Count && renderedCount < maxRenderCount; i++)
        {
            int itemId = itemIds[i];
            if (itemId != Block.FloorStackStateSentinel)
            {
                continue;
            }

            parsedStructuredState = true;
            if (i + 1 >= itemIds.Count)
            {
                break;
            }

            int stackCount = Mathf.Max(0, itemIds[++i]);
            for (int stackIndex = 0; stackIndex < stackCount && i + 1 < itemIds.Count; stackIndex++)
            {
                int stackItemCount = Mathf.Max(0, itemIds[++i]);
                for (int objectIndex = 0; objectIndex < stackItemCount && i + 1 < itemIds.Count; objectIndex++)
                {
                    int stackItemId = itemIds[++i];
                    if (stackItemId < 0)
                    {
                        continue;
                    }

                    AddItemMatrix(stackItemId, record, stackIndex, objectIndex);
                    renderedCount++;
                    if (renderedCount >= maxRenderCount)
                    {
                        break;
                    }
                }
            }
        }

        if (!parsedStructuredState)
        {
            AddLegacyFloorItemMatrices(record, itemIds, maxRenderCount);
        }
    }

    private void AddLegacyFloorItemMatrices(VirtualObjectRecord record, IReadOnlyList<int> itemIds, int maxRenderCount)
    {
        int slotCount = Mathf.Max(1, DefaultFloorSlotOffsets.Length);
        int[] slotCounts = new int[slotCount];
        int[] slotItemIds = new int[slotCount];
        for (int i = 0; i < slotItemIds.Length; i++)
        {
            slotItemIds[i] = -1;
        }

        int renderedCount = 0;
        for (int itemIndex = 0; itemIndex < itemIds.Count && renderedCount < maxRenderCount; itemIndex++)
        {
            int itemId = itemIds[itemIndex];
            if (itemId < 0)
            {
                continue;
            }

            if (!TryGetBestLegacyFloorSlot(itemId, true, slotCounts, slotItemIds, out int slotIndex)
                && !TryGetBestLegacyFloorSlot(itemId, false, slotCounts, slotItemIds, out slotIndex))
            {
                continue;
            }

            AddItemMatrix(itemId, record, slotIndex, slotCounts[slotIndex]);
            slotCounts[slotIndex]++;
            slotItemIds[slotIndex] = itemId;
            renderedCount++;
        }
    }

    private bool TryGetBestLegacyFloorSlot(
        int itemId,
        bool requireExisting,
        IReadOnlyList<int> slotCounts,
        IReadOnlyList<int> slotItemIds,
        out int bestSlotIndex)
    {
        bestSlotIndex = -1;
        float bestDistanceSqr = float.MaxValue;
        int slotCount = Mathf.Min(DefaultFloorSlotOffsets.Length, slotCounts.Count);
        int stackCapacity = ItemDefinition.ResolveStackCapacity(
            itemManager,
            itemId,
            LegacyFloorStackCapacity);

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            int slotItemCount = slotCounts[slotIndex];
            if (requireExisting && slotItemCount == 0)
            {
                continue;
            }

            if (slotItemCount >= stackCapacity)
            {
                continue;
            }

            int slotItemId = slotIndex < slotItemIds.Count ? slotItemIds[slotIndex] : -1;
            if (slotItemCount > 0 && slotItemId != itemId)
            {
                continue;
            }

            Vector3 offset = ResolveVirtualFloorSlotOffset(slotIndex);
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (bestSlotIndex >= 0 && distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            bestSlotIndex = slotIndex;
        }

        return bestSlotIndex >= 0;
    }

    private void AddItemMatrix(int itemId, VirtualObjectRecord record, int stackIndex, int objectIndex)
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
            itemId,
            GetBatchCell(record.worldPosition.x, batchCellSize),
            GetBatchCell(record.worldPosition.z, batchCellSize));
        Vector3 slotOffset = record.worldRotation * ResolveVirtualFloorSlotOffset(stackIndex);
        Vector3 position = record.worldPosition
            + slotOffset
            + new Vector3(0f, stackBaseYOffset + (stackVerticalSpacing * objectIndex), 0f);
        batches.AddMatrix(key, Matrix4x4.TRS(position, record.worldRotation, Vector3.one));
    }

    private static Vector3 ResolveVirtualFloorSlotOffset(int stackIndex)
    {
        if (stackIndex >= 0 && stackIndex < DefaultFloorSlotOffsets.Length)
        {
            return DefaultFloorSlotOffsets[stackIndex];
        }

        return Vector3.zero;
    }

    private void RenderBatches()
    {
        batches.RenderBatches(mainCamera);
    }

    private static int GetBatchCell(float worldCoordinate, float cellSize)
    {
        return Mathf.FloorToInt(worldCoordinate / Mathf.Max(1f, cellSize));
    }
}
