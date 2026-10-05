using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Rendering;

internal sealed class MiningRenderTemplate
{
    internal readonly ItemDefinition Definition;
    internal readonly MapObjectArchetype Archetype;
    internal readonly InstallationRigidAnimationTemplate Animation;
    internal readonly Vector3 Scale, PowerLinePoint, ConsumePoint;
    internal readonly Bounds LocalBounds;
    internal readonly float WorkGaugeVerticalOffset;
    internal readonly Color WorkGaugeFillColor;
    internal readonly float Watts;
    internal readonly float WorkRate, InputConsumeMoveInterval;
    internal readonly long WorkUnitsPerTick;
    internal readonly ItemDefinition.EnergyType EnergyType;
    internal bool UsesFuel => EnergyType == ItemDefinition.EnergyType.Burn;
    internal readonly Sprite EnergyMarkerIcon;
    internal readonly long CompleteEnergy;
    internal readonly int BenchmarkOutputId;
    internal readonly Bounds ColliderBounds;
    internal readonly PhysicsMaterial ColliderMaterial;
    internal readonly bool ColliderTrigger;
    internal readonly uint ColliderIncludeLayers, ColliderExcludeLayers;
    private readonly bool[] partActive;
    internal MiningRenderTemplate(MiningMachine source, InstallationPlacementController controller)
    {
        Definition = source.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(source.ResolveItemId());
        Archetype = Definition.MapObjectArchetype;
        Animation = InstallationRigidAnimationTemplate.Create(Archetype);
        Scale = Archetype.DefaultRootScale; LocalBounds = Archetype.LocalRenderBounds;
        WorkGaugeVerticalOffset = source.WorkGaugeVerticalOffset;
        WorkGaugeFillColor = source.ObjectInfoWorkGaugeFillColor;
        Watts = ItemDefinition.ResolveElectricUseWatts(Definition);
        Definition.TryGetUseEnergyRequirement(0, out var requirement);
        EnergyType = requirement.energyType;
        WorkRate = ItemDefinition.ResolveUseEnergyRatePerSecond(Definition, EnergyType);
        WorkUnitsPerTick = DeterministicSimulationUnits.RateForTicks(WorkRate, 1);
        ConsumePoint = source.transform.worldToLocalMatrix.MultiplyPoint3x4(source.DataConsumeTargetWorldPosition);
        InputConsumeMoveInterval = source.DataInputConsumeMoveInterval;
        EnergyMarkerIcon = controller != null ? controller.ResolveInputEnergyMarkerIcon(source) : null;
        CompleteEnergy = DeterministicSimulationUnits.FromFloat(InputOutputModule.ResolveCompleteEnergy(Definition, 5f));
        PowerLinePoint = source.TryGetPowerLinePoint(out Transform point)
            ? source.transform.worldToLocalMatrix.MultiplyPoint3x4(point.position) : Vector3.up;
        var collider = source.GetComponent<BoxCollider>();
        if (collider != null)
        {
            ColliderBounds = new Bounds(collider.center, collider.size); ColliderMaterial = collider.sharedMaterial;
            ColliderTrigger = collider.isTrigger; ColliderIncludeLayers = (uint)collider.includeLayers.value; ColliderExcludeLayers = (uint)collider.excludeLayers.value;
        }
        partActive = new bool[Archetype.RenderParts.Count];
        for (int i = 0; i < partActive.Length; i++)
        {
            var part = Archetype.RenderParts[i]; bool active = part.EnabledByDefault;
            for (int node = part.NodeIndex; active && node >= 0; node = Archetype.Nodes[node].ParentIndex)
                active &= Archetype.Nodes[node].ActiveByDefault;
            partActive[i] = active;
        }
        BenchmarkOutputId = source.OutputList.Count > 0 && source.OutputList[0].itemDefinition != null
            ? source.OutputList[0].itemDefinition.id : -1;
    }
    internal int Append(MiningMachineInstance miner, VirtualRenderBatchCollection batches)
    {
        miner.SampleBenchmarkEnergyVisuals();
        Animation?.Evaluate(miner.AnimationPhase, miner.IsWorking);
        Matrix4x4 root = miner.RootMatrix * Matrix4x4.Scale(Vector3.one * miner.PlacementPresentationScale);
        int count = 0;
        for (int i = 0; i < Archetype.RenderParts.Count; i++)
        {
            var part = Archetype.RenderParts[i];
            if (!partActive[i] || part.Mesh == null || part.Material == null) continue;
            Matrix4x4 matrix = root * (Animation != null ? Animation.GetMatrix(part.NodeIndex) : Archetype.Nodes[part.NodeIndex].DefaultLocalToRoot);
            var key = new VirtualRenderBatchKey(part.Mesh, part.Material, part.Layer, part.SubMeshIndex,
                part.ShadowCastingMode, part.ReceiveShadows, false,
                batchCellX: Mathf.FloorToInt(matrix.m03 / 16f), batchCellZ: Mathf.FloorToInt(matrix.m23 / 16f),
                invertCulling: matrix.determinant < 0f, renderingLayerMask: part.RenderingLayerMask);
            batches.AddMatrix(key, matrix); count++;
        }
        return count;
    }
}
