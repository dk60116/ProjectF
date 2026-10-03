using UnityEngine;
using ProjectF.Rendering;
using ProjectF.MapObjects;

namespace ProjectF.Power
{
    internal sealed class UtilityPoleRenderTemplate
    {
        internal readonly ItemDefinition Definition;
        internal readonly MapObjectArchetype Archetype;
        internal readonly Vector3 Scale, Center, A, B;
        internal readonly Bounds LocalBounds;
        internal readonly bool HasCenter, HasA, HasB;
        internal readonly float RangeOffset, Width, Sag, ConnectionSag;
        internal readonly int Segments;
        internal readonly Color Color;
        internal readonly Vector3 ColliderCenter;
        internal readonly float ColliderRadius;
        internal readonly int ColliderLayerPriority;
        internal readonly bool ColliderEnabled, ColliderProvidesContacts;
        internal readonly PhysicsMaterial ColliderMaterial;
        internal readonly bool ColliderTrigger;
        internal readonly LayerMask ColliderIncludeLayers, ColliderExcludeLayers;
        private readonly bool[] partActive;
        internal UtilityPoleRenderTemplate(global::UtilityPole source)
        {
            Definition = source.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(source.ResolveItemId());
            Archetype = Definition.MapObjectArchetype; Scale = Archetype.DefaultRootScale; LocalBounds = Archetype.LocalRenderBounds;
            var data = source.Runtime; data.RefreshAuthoring();
            data.CaptureStyle(out RangeOffset, out Width, out Sag, out ConnectionSag, out Segments, out Color);
            data.CapturePoints(out HasCenter, out Center, out HasA, out A, out HasB, out B);
            var collider = source.GetComponent<SphereCollider>();
            ColliderCenter = collider.center; ColliderRadius = collider.radius;
            ColliderLayerPriority = collider.layerOverridePriority; ColliderEnabled = collider.enabled; ColliderProvidesContacts = collider.providesContacts;
            ColliderMaterial = collider.sharedMaterial; ColliderTrigger = collider.isTrigger;
            ColliderIncludeLayers = collider.includeLayers; ColliderExcludeLayers = collider.excludeLayers;
            partActive = new bool[Archetype.RenderParts.Count];
            for (int i = 0; i < partActive.Length; i++)
            {
                var part = Archetype.RenderParts[i]; bool active = part.EnabledByDefault;
                for (int node = part.NodeIndex; active && node >= 0; node = Archetype.Nodes[node].ParentIndex) active &= Archetype.Nodes[node].ActiveByDefault;
                partActive[i] = active;
            }
        }
        internal void Append(UtilityPoleRuntime pole, VirtualRenderBatchCollection batches)
        {
            Matrix4x4 root = pole.RootMatrix * Matrix4x4.Scale(Vector3.one * pole.PlacementPresentationScale);
            for (int i = 0; i < Archetype.RenderParts.Count; i++)
            {
                var part = Archetype.RenderParts[i];
                if (!partActive[i] || part.Mesh == null || part.Material == null) continue;
                Matrix4x4 matrix = root * Archetype.Nodes[part.NodeIndex].DefaultLocalToRoot;
                batches.AddMatrix(new VirtualRenderBatchKey(part.Mesh, part.Material, part.Layer, part.SubMeshIndex,
                    part.ShadowCastingMode, part.ReceiveShadows, false, batchCellX: Mathf.FloorToInt(matrix.m03 / 16f),
                    batchCellZ: Mathf.FloorToInt(matrix.m23 / 16f), invertCulling: matrix.determinant < 0f,
                    renderingLayerMask: part.RenderingLayerMask), matrix);
            }
        }
    }
    public sealed partial class UtilityPoleRuntime
    {
        internal void CaptureStyle(out float offset, out float width, out float sag, out float connectionSag, out int segments, out Color color)
        { offset = supplyRangeVisualYOffset; width = lineWidth; sag = lineSagDepth; connectionSag = connectionLineSagDepth; segments = lineCurveSegments; color = lineColor; }
        internal void CapturePoints(out bool hasCenter, out Vector3 center, out bool hasA, out Vector3 a, out bool hasB, out Vector3 b)
        { hasCenter = linePointCenter != null; center = linePointCenter?.Local ?? Vector3.zero; hasA = linePointA != null; a = linePointA?.Local ?? Vector3.zero; hasB = linePointB != null; b = linePointB?.Local ?? Vector3.zero; }
    }
}
