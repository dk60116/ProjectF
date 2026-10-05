using UnityEngine;
using ProjectF.Rendering;

namespace ProjectF.MapObjects
{
internal sealed class WorkableRenderTemplate
{
    internal readonly ItemDefinition Definition;
    internal readonly MapObjectArchetype Archetype;
    internal readonly Vector3 Scale;
    internal readonly Bounds LocalBounds, ColliderBounds;
    internal readonly PhysicsMaterial ColliderMaterial;
    internal readonly bool ColliderTrigger, ShowRange;
    internal readonly uint ColliderIncludeLayers, ColliderExcludeLayers, RangeCells;
    internal readonly float RangeYOffset;
    private readonly bool[] partActive;
    internal WorkableRenderTemplate(WorkableObject source)
    {
        Definition = source.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(source.ResolveItemId());
        Archetype = Definition.MapObjectArchetype; Scale = Archetype.DefaultRootScale; LocalBounds = Archetype.LocalRenderBounds;
        RangeCells = source.WorkableRangeCells; ShowRange = source.ShowWorkableRange; RangeYOffset = source.RangeVisualYOffset;
        var collider = source.GetComponent<BoxCollider>();
        ColliderBounds = new Bounds(collider.center, collider.size); ColliderMaterial = collider.sharedMaterial;
        ColliderTrigger = collider.isTrigger; ColliderIncludeLayers = (uint)collider.includeLayers.value; ColliderExcludeLayers = (uint)collider.excludeLayers.value;
        partActive = new bool[Archetype.RenderParts.Count];
        for (int i = 0; i < partActive.Length; i++)
        {
            var part = Archetype.RenderParts[i]; bool active = part.EnabledByDefault;
            for (int node = part.NodeIndex; active && node >= 0; node = Archetype.Nodes[node].ParentIndex)
                active &= Archetype.Nodes[node].ActiveByDefault;
            partActive[i] = active;
        }
    }
    internal int Append(WorkableInstance instance, VirtualRenderBatchCollection batches)
    {
        Matrix4x4 root = instance.RootMatrix * Matrix4x4.Scale(Vector3.one * instance.PlacementPresentationScale);
        int count = 0;
        for (int i = 0; i < Archetype.RenderParts.Count; i++)
        {
            var part = Archetype.RenderParts[i];
            if (!partActive[i] || part.Mesh == null || part.Material == null) continue;
            Matrix4x4 matrix = root * (Archetype.Nodes[part.NodeIndex].DefaultLocalToRoot);
            var key = new VirtualRenderBatchKey(part.Mesh, part.Material, part.Layer, part.SubMeshIndex,
                part.ShadowCastingMode, part.ReceiveShadows, false,
                batchCellX: Mathf.FloorToInt(matrix.m03 / 16f), batchCellZ: Mathf.FloorToInt(matrix.m23 / 16f),
                invertCulling: matrix.determinant < 0f, renderingLayerMask: part.RenderingLayerMask);
            batches.AddMatrix(key, matrix); count++;
        }
        return count;
    }
}
}
