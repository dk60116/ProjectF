using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
    /// <summary>
    /// Owns all visual instance data for one ItemDefinition ID.
    /// Data-only root transforms are stored by generation-safe handle. Live model ownership is separate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaticMapObjectTypeHost : MonoBehaviour
    {
        private readonly Dictionary<MapObjectHandle, InstanceSlot> slotsByHandle =
            new Dictionary<MapObjectHandle, InstanceSlot>();
        private readonly List<MapObjectHandle> staleHandles = new List<MapObjectHandle>(32);
        private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();

        private MapObjectArchetype archetype;
        private int itemId = -1;
        private int synchronizationStamp;
        private float batchCellSize = 16f;
        private bool released;

        public int ItemId => itemId;
        public string ItemName => archetype != null ? archetype.ItemName : string.Empty;
        public MapObjectArchetype Archetype => archetype;
        public int InstanceCount => slotsByHandle.Count;
        public int ActiveBatchCount => batches.ActiveBatchCount;
        public int ActiveMatrixCount => batches.ActiveMatrixCount;
        public int EstimatedDrawCallCount => batches.EstimatedDrawCallCount;
        public int LastVisibleBatchCount => batches.LastVisibleBatchCount;
        public int LastCulledBatchCount => batches.LastCulledBatchCount;
        public int LastCandidateBatchCount => batches.LastCandidateBatchCount;
        public int LastCandidateCellCount => batches.LastCandidateCellCount;
        public int LastLegacySubmittedMatrixCount => batches.LastLegacySubmittedMatrixCount;
        public int LastLegacyDrawCallCount => batches.LastLegacyDrawCallCount;
        public int LastBatchRendererGroupBatchCount => batches.LastBatchRendererGroupBatchCount;
        public int LastBatchRendererGroupMatrixCount => batches.LastBatchRendererGroupMatrixCount;

        public void Configure(int typeItemId, MapObjectArchetype typeArchetype, float cellSize)
        {
            Release();
            released = false;
            itemId = typeItemId;
            archetype = typeArchetype;
            batchCellSize = Mathf.Max(1f, cellSize);
        }

        public void BeginSynchronization()
        {
            if (released)
            {
                return;
            }

            synchronizationStamp++;
            if (synchronizationStamp == 0)
            {
                synchronizationStamp = 1;
                foreach (InstanceSlot slot in slotsByHandle.Values)
                {
                    slot.LastSeenStamp = 0;
                }
            }

            batches.ClearActiveMatrices();
        }

        public bool SynchronizeRecord(VirtualObjectRecord record)
        {
            MapObjectHandle handle = record != null ? record.mapObjectHandle : default;
            if (released
                || record == null
                || record.kind != VirtualObjectKind.Installation
                || record.HasAttachedView
                || !handle.IsValid
                || handle.TypeId != itemId
                || archetype == null
                || archetype.SourcePrefab == null)
            {
                return false;
            }

            if (!slotsByHandle.TryGetValue(handle, out InstanceSlot slot))
            {
                slot = new InstanceSlot();
                slotsByHandle[handle] = slot;
            }

            Vector3 rootScale = archetype.SourcePrefab.transform.localScale;
            Matrix4x4 rootMatrix = Matrix4x4.TRS(
                record.worldPosition,
                record.worldRotation,
                rootScale);
            if (!AppendInstanceMatrices(rootMatrix, record.worldPosition))
            {
                slotsByHandle.Remove(handle);
                return false;
            }

            slot.LastSeenStamp = synchronizationStamp;
            return true;
        }

        public void CompleteSynchronization()
        {
            if (released)
            {
                return;
            }

            staleHandles.Clear();
            foreach (KeyValuePair<MapObjectHandle, InstanceSlot> pair in slotsByHandle)
            {
                if (pair.Value.LastSeenStamp != synchronizationStamp)
                {
                    staleHandles.Add(pair.Key);
                }
            }

            for (int i = 0; i < staleHandles.Count; i++)
            {
                slotsByHandle.Remove(staleHandles[i]);
            }

            staleHandles.Clear();
        }

        public void AbortSynchronization()
        {
            if (released)
            {
                return;
            }

            slotsByHandle.Clear();
            batches.ClearActiveMatrices();
        }

        public void Render(Camera camera)
        {
            if (!released)
            {
                batches.RenderBatches(camera, batchCellSize);
            }
        }

        public void Suspend()
        {
            batches.SuspendRendering();
        }

        public void Release()
        {
            if (released)
            {
                return;
            }

            released = true;
            slotsByHandle.Clear();
            batches.Clear();
            itemId = -1;
            archetype = null;
        }

        private void OnDisable()
        {
            if (!ProjectFApplicationLifecycle.IsQuitting) Suspend();
        }

        private void OnDestroy()
        {
            if (ProjectFApplicationLifecycle.IsQuitting) return;
            Release();
            batches.Dispose();
        }

        public static bool IsSupportedArchetype(MapObjectArchetype candidate)
        {
            if (candidate == null
                || !candidate.SupportsStaticVirtualRendering
                || !(candidate.SourcePrefab is InstallationObject sourceInstallation))
            {
                return false;
            }

            // These types already have specialized data-oriented render systems.
            if (sourceInstallation is ConveyorBelt
                || sourceInstallation is Pipe
                || sourceInstallation is RobotArm
                || sourceInstallation is Building
                || sourceInstallation is Vehicle)
            {
                return false;
            }

            IReadOnlyList<MapObjectVisualNodeDefinition> nodes = candidate.Nodes;
            IReadOnlyList<MapObjectRenderPartDefinition> parts = candidate.RenderParts;
            if (nodes == null || parts == null)
            {
                return false;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                MapObjectRenderPartDefinition part = parts[i];
                if (!part.EnabledByDefault
                    || part.NodeIndex < 0
                    || part.NodeIndex >= nodes.Count
                    || !IsNodeActiveByDefault(nodes, part.NodeIndex))
                {
                    return false;
                }
            }

            return true;
        }

        private bool AppendInstanceMatrices(Matrix4x4 rootMatrix, Vector3 rootPosition)
        {
            IReadOnlyList<MapObjectVisualNodeDefinition> nodes = archetype != null ? archetype.Nodes : null;
            IReadOnlyList<MapObjectRenderPartDefinition> renderParts = archetype != null ? archetype.RenderParts : null;
            if (nodes == null || renderParts == null)
            {
                return false;
            }

            int cellX = Mathf.FloorToInt(rootPosition.x / batchCellSize);
            int cellZ = Mathf.FloorToInt(rootPosition.z / batchCellSize);
            bool addedAny = false;

            for (int partIndex = 0; partIndex < renderParts.Count; partIndex++)
            {
                MapObjectRenderPartDefinition part = renderParts[partIndex];
                if (part.Kind != MapObjectRenderPartKind.Mesh
                    || !part.EnabledByDefault
                    || part.Mesh == null
                    || part.Material == null
                    || part.NodeIndex < 0
                    || part.NodeIndex >= nodes.Count
                    || !IsNodeActiveByDefault(nodes, part.NodeIndex))
                {
                    continue;
                }

                if (!part.Material.enableInstancing)
                {
                    part.Material.enableInstancing = true;
                }

                Matrix4x4 matrix = rootMatrix * nodes[part.NodeIndex].DefaultLocalToRoot;
                VirtualRenderBatchKey key = new VirtualRenderBatchKey(
                    part.Mesh,
                    part.Material,
                    part.Layer,
                    part.SubMeshIndex,
                    part.ShadowCastingMode,
                    part.ReceiveShadows,
                    false,
                    batchGroupId: itemId,
                    batchCellX: cellX,
                    batchCellZ: cellZ,
                    invertCulling: matrix.determinant < 0f,
                    renderingLayerMask: part.RenderingLayerMask);
                batches.AddMatrix(key, matrix);
                addedAny = true;
            }

            return addedAny;
        }

        private static bool IsNodeActiveByDefault(
            IReadOnlyList<MapObjectVisualNodeDefinition> nodes,
            int nodeIndex)
        {
            int currentIndex = nodeIndex;
            while (currentIndex >= 0 && currentIndex < nodes.Count)
            {
                MapObjectVisualNodeDefinition node = nodes[currentIndex];
                if (!node.ActiveByDefault)
                {
                    return false;
                }

                currentIndex = node.ParentIndex;
            }

            return currentIndex < 0;
        }

        private sealed class InstanceSlot
        {
            public int LastSeenStamp;
        }
    }
}
