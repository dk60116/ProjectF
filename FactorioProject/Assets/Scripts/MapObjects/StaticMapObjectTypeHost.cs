using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
    /// <summary>
    /// Owns all visual instance data for one ItemDefinition ID.
    /// Root transforms are stored by generation-safe handle; child transforms are derived from
    /// the baked archetype without creating per-instance Transform hierarchies.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaticMapObjectTypeHost : MonoBehaviour
    {
        private readonly Dictionary<MapObjectHandle, InstanceSlot> slotsByHandle =
            new Dictionary<MapObjectHandle, InstanceSlot>();
        private readonly List<MapObjectHandle> staleHandles = new List<MapObjectHandle>(32);
        private readonly List<MeshRenderer> rendererScratch = new List<MeshRenderer>(16);
        private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();

        private MapObjectArchetype archetype;
        private Type sourceRuntimeType;
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

        public void Configure(int typeItemId, MapObjectArchetype typeArchetype, float cellSize)
        {
            Release();
            released = false;
            itemId = typeItemId;
            archetype = typeArchetype;
            sourceRuntimeType = archetype != null && archetype.SourcePrefab != null
                ? archetype.SourcePrefab.GetType()
                : null;
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

        public bool SynchronizeInstance(InstallationObject source, MapObjectHandle handle)
        {
            if (released
                || source == null
                || !source.isActiveAndEnabled
                || source.IsMapObjectTypeVisualTransitionActive
                || sourceRuntimeType == null
                || source.GetType() != sourceRuntimeType
                || !handle.IsValid
                || handle.TypeId != itemId)
            {
                return false;
            }

            if (!slotsByHandle.TryGetValue(handle, out InstanceSlot slot))
            {
                slot = CaptureSource(source, handle);
                if (!CanRenderSource(slot))
                {
                    return false;
                }

                slotsByHandle.Add(handle, slot);
            }
            else if (slot.Source != source)
            {
                RestoreSourceRenderers(slot);
                slot = CaptureSource(source, handle);
                if (!CanRenderSource(slot))
                {
                    slotsByHandle.Remove(handle);
                    return false;
                }

                slotsByHandle[handle] = slot;
            }

            Matrix4x4 rootMatrix = source.transform.localToWorldMatrix;
            if (!AppendInstanceMatrices(rootMatrix, source.transform.position))
            {
                RestoreSourceRenderers(slot);
                slotsByHandle.Remove(handle);
                return false;
            }

            slot.RootMatrix = rootMatrix;
            slot.LastSeenStamp = synchronizationStamp;
            SuppressSourceRenderers(slot);
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
                if (pair.Value.Source == null || pair.Value.LastSeenStamp != synchronizationStamp)
                {
                    RestoreSourceRenderers(pair.Value);
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

            RestoreAllSourceRenderers();
            batches.ClearActiveMatrices();
        }

        public bool TryGetRootMatrix(MapObjectHandle handle, out Matrix4x4 rootMatrix)
        {
            if (slotsByHandle.TryGetValue(handle, out InstanceSlot slot))
            {
                rootMatrix = slot.RootMatrix;
                return true;
            }

            rootMatrix = default;
            return false;
        }

        public void Render(Camera camera)
        {
            if (!released)
            {
                batches.RenderBatches(camera);
            }
        }

        public void Suspend()
        {
            RestoreAllSourceRenderers();
            batches.SuspendRendering();
        }

        public void Release()
        {
            if (released)
            {
                return;
            }

            released = true;
            RestoreAllSourceRenderers();
            batches.Clear();
            itemId = -1;
            archetype = null;
            sourceRuntimeType = null;
        }

        private void OnDisable()
        {
            Suspend();
        }

        private void OnDestroy()
        {
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
            if (sourceInstallation is ConveyorBelt || sourceInstallation is RobotArm)
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

        private InstanceSlot CaptureSource(InstallationObject source, MapObjectHandle handle)
        {
            rendererScratch.Clear();
            source.GetComponentsInChildren(true, rendererScratch);

            int ownedRendererCount = 0;
            for (int i = 0; i < rendererScratch.Count; i++)
            {
                if (IsOwnedRenderer(source, rendererScratch[i]))
                {
                    ownedRendererCount++;
                }
            }

            SourceRendererState[] rendererStates = new SourceRendererState[ownedRendererCount];
            int destinationIndex = 0;
            for (int i = 0; i < rendererScratch.Count; i++)
            {
                MeshRenderer renderer = rendererScratch[i];
                if (!IsOwnedRenderer(source, renderer))
                {
                    continue;
                }

                rendererStates[destinationIndex++] = new SourceRendererState(
                    renderer,
                    renderer.forceRenderingOff);
            }

            return new InstanceSlot(handle, source, source.transform.localToWorldMatrix, rendererStates);
        }

        private static bool IsOwnedRenderer(InstallationObject source, MeshRenderer renderer)
        {
            if (source == null || renderer == null)
            {
                return false;
            }

            Transform root = source.transform;
            Transform current = renderer.transform;
            while (current != null)
            {
                if (current == root)
                {
                    return true;
                }

                if (current.TryGetComponent(out MapObject _))
                {
                    return false;
                }

                current = current.parent;
            }

            return false;
        }

        private static void SuppressSourceRenderers(InstanceSlot slot)
        {
            SourceRendererState[] states = slot.RendererStates;
            for (int i = 0; i < states.Length; i++)
            {
                MeshRenderer renderer = states[i].Renderer;
                if (renderer != null)
                {
                    renderer.forceRenderingOff = true;
                }
            }
        }

        private static bool CanRenderSource(InstanceSlot slot)
        {
            if (slot == null || slot.RendererStates.Length == 0)
            {
                return false;
            }

            SourceRendererState[] states = slot.RendererStates;
            for (int i = 0; i < states.Length; i++)
            {
                MeshRenderer renderer = states[i].Renderer;
                if (renderer == null
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy
                    || renderer.HasPropertyBlock())
                {
                    return false;
                }
            }

            return true;
        }

        private void RestoreAllSourceRenderers()
        {
            foreach (InstanceSlot slot in slotsByHandle.Values)
            {
                RestoreSourceRenderers(slot);
            }

            slotsByHandle.Clear();
            staleHandles.Clear();
        }

        private static void RestoreSourceRenderers(InstanceSlot slot)
        {
            if (slot == null)
            {
                return;
            }

            SourceRendererState[] states = slot.RendererStates;
            for (int i = 0; i < states.Length; i++)
            {
                MeshRenderer renderer = states[i].Renderer;
                if (renderer != null)
                {
                    renderer.forceRenderingOff = states[i].OriginalForceRenderingOff;
                }
            }
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
            public InstanceSlot(
                MapObjectHandle handle,
                InstallationObject source,
                Matrix4x4 rootMatrix,
                SourceRendererState[] rendererStates)
            {
                Handle = handle;
                Source = source;
                RootMatrix = rootMatrix;
                RendererStates = rendererStates;
            }

            public readonly MapObjectHandle Handle;
            public readonly SourceRendererState[] RendererStates;
            public InstallationObject Source;
            public Matrix4x4 RootMatrix;
            public int LastSeenStamp;
        }

        private readonly struct SourceRendererState
        {
            public SourceRendererState(MeshRenderer renderer, bool originalForceRenderingOff)
            {
                Renderer = renderer;
                OriginalForceRenderingOff = originalForceRenderingOff;
            }

            public readonly MeshRenderer Renderer;
            public readonly bool OriginalForceRenderingOff;
        }
    }
}
