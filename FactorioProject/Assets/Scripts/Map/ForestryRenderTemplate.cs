using System.Collections.Generic;
using System;
using UnityEngine;
using ProjectF.Rendering;
using ProjectF.Simulation;

namespace ProjectF.MapObjects
{
internal sealed class ForestryRenderTemplate
{
    internal readonly ItemDefinition Definition;
    internal readonly MapObjectArchetype Archetype;
    internal readonly InstallationRigidAnimationTemplate Animation;
    internal readonly Vector3 Scale, PowerLinePoint, ConsumePoint;
    internal readonly Bounds LocalBounds, ColliderBounds;
    internal readonly PhysicsMaterial ColliderMaterial;
    internal readonly bool ColliderTrigger;
    internal readonly bool UsesSphereCollider;
    internal readonly uint ColliderIncludeLayers, ColliderExcludeLayers;
    internal readonly float Watts, InputConsumeMoveInterval, AnimationSpeed;
    internal readonly long CompleteEnergyUnits;
    private readonly SeedPlanter seedSource;
    private List<ItemDefinition> seedDefinitions;
    private int seedDefinitionCount = -1;
    private int[] seedItemIds = Array.Empty<int>();
    internal int[] SeedItemIds
    {
        get
        {
            var definitions = GameManager.Instance != null && GameManager.Instance.ItemManger != null
                ? GameManager.Instance.ItemManger.ItemDefinitions : null;
            if (seedSource != null && definitions != null
                && (!ReferenceEquals(seedDefinitions, definitions) || seedDefinitionCount != definitions.Count))
            {
                var ids = new List<int>(); seedSource.TryCollectPlantableSeedItemIds(ids);
                seedItemIds = ids.ToArray(); seedDefinitions = definitions; seedDefinitionCount = definitions.Count;
            }
            return seedItemIds;
        }
    }
    internal readonly Sprite EnergyMarkerIcon;
    private readonly bool[] partActive;
    private readonly int hingeNode, warningNode;
    private readonly Material[][] warningMaterials;
    private static readonly Color[] LightColors = { new Color(0.18f, 1f, 0.25f, 1f), new Color(1f, 0.72f, 0.05f, 1f), new Color(1f, 0.08f, 0.03f, 1f) };
    internal ForestryRenderTemplate(InstallationObject source, InstallationPlacementController controller)
    {
        EnergyMarkerIcon = source is InputOutputModule energyModule && controller != null ? controller.ResolveInputEnergyMarkerIcon(energyModule) : null;
        Definition = source.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(source.ResolveItemId());
        Archetype = Definition.MapObjectArchetype; Scale = Archetype.DefaultRootScale; LocalBounds = Archetype.LocalRenderBounds;
        Animation = InstallationRigidAnimationTemplate.Create(Archetype, ignoreMissingNodes: true);
        Watts = ItemDefinition.ResolveElectricUseWatts(Definition);
        float complete = ItemDefinition.ResolveCompleteEnergyAmount(Definition);
        CompleteEnergyUnits = DeterministicSimulationUnits.FromFloat(complete > 0 ? complete
            : Mathf.Max(1, Watts * Mathf.Max(0.1f, Definition.CraftingDurationSeconds)));
        PowerLinePoint = source.TryGetPowerLinePoint(out Transform power)
            ? source.transform.worldToLocalMatrix.MultiplyPoint3x4(power.position) : Vector3.up;
        ConsumePoint = source is InputOutputModule module ? source.transform.worldToLocalMatrix.MultiplyPoint3x4(module.DataConsumeTargetWorldPosition) : Vector3.zero;
        InputConsumeMoveInterval = source is InputOutputModule io ? io.DataInputConsumeMoveInterval : 0;
        hingeNode = ResolveNode(source is LoggingMachine logger ? logger.DataHinge : null, source.transform);
        warningNode = ResolveNode(source is SeedPlanter planter ? planter.DataWarningLightRenderer?.transform : null, source.transform);
        AnimationSpeed = source is SeedPlanter seed ? seed.WorkAnimationCycleSeconds / SeedPlanter.ResolvePlantDuration(Definition) : 1;
        seedSource = source as SeedPlanter;
        Collider collider;
        var box = source.GetComponent<BoxCollider>();
        if (box != null)
        {
            collider = box; ColliderBounds = new Bounds(box.center, box.size);
        }
        else
        {
            var sphere = source.GetComponent<SphereCollider>(); collider = sphere; UsesSphereCollider = true;
            ColliderBounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
        }
        ColliderMaterial = collider.sharedMaterial;
        ColliderTrigger = collider.isTrigger; ColliderIncludeLayers = (uint)collider.includeLayers.value; ColliderExcludeLayers = (uint)collider.excludeLayers.value;
        partActive = new bool[Archetype.RenderParts.Count]; warningMaterials = new Material[partActive.Length][];
        for (int i = 0; i < partActive.Length; i++)
        {
            var part = Archetype.RenderParts[i]; bool active = part.EnabledByDefault;
            for (int node = part.NodeIndex; active && node >= 0; node = Archetype.Nodes[node].ParentIndex) active &= Archetype.Nodes[node].ActiveByDefault;
            partActive[i] = active;
            if (part.NodeIndex != warningNode || part.Material == null) continue;
            warningMaterials[i] = new Material[3];
            for (int j = 0; j < 3; j++)
            {
                var material = new Material(part.Material) { hideFlags = HideFlags.HideAndDontSave };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", LightColors[j]);
                if (material.HasProperty("_Color")) material.SetColor("_Color", LightColors[j]);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", LightColors[j]);
                warningMaterials[i][j] = material;
            }
        }
    }
    internal int Append(ForestryInstance instance, VirtualRenderBatchCollection batches)
    {
        Animation?.Evaluate(instance.Data.AnimationPhase * AnimationSpeed, instance.IsWorking);
        var root = instance.RootMatrix * Matrix4x4.Scale(Vector3.one * instance.PlacementPresentationScale);
        int count = 0;
        for (int i = 0; i < partActive.Length; i++)
        {
            var part = Archetype.RenderParts[i];
            if (!partActive[i] || part.Mesh == null || part.Material == null) continue;
            var pose = Animation != null ? Animation.GetMatrix(part.NodeIndex) : Archetype.Nodes[part.NodeIndex].DefaultLocalToRoot;
            if (instance is LoggingMachineInstance logger && hingeNode >= 0 && IsChild(part.NodeIndex, hingeNode))
            {
                var hinge = Animation != null ? Animation.GetMatrix(hingeNode) : Archetype.Nodes[hingeNode].DefaultLocalToRoot;
                pose = hinge * Matrix4x4.Rotate(Quaternion.Euler(0, logger.HingeAngle, 0)) * hinge.inverse * pose;
            }
            var material = part.Material;
            if (warningMaterials[i] != null && instance is SeedPlanterInstance seed)
            {
                int state = seed.IsErrorState ? 2 : seed.CurrentOperatingState == PlantingOperatingState.NoSeeds
                    || seed.CurrentOperatingState == PlantingOperatingState.NoPower || seed.CurrentOperatingState == PlantingOperatingState.TargetOccupied ? 1 : 0;
                material = warningMaterials[i][state];
            }
            var matrix = root * pose;
            batches.AddMatrix(new VirtualRenderBatchKey(part.Mesh, material, part.Layer, part.SubMeshIndex,
                part.ShadowCastingMode, part.ReceiveShadows, false, batchCellX: Mathf.FloorToInt(matrix.m03 / 16),
                batchCellZ: Mathf.FloorToInt(matrix.m23 / 16), invertCulling: matrix.determinant < 0, renderingLayerMask: part.RenderingLayerMask), matrix);
            count++;
        }
        return count;
    }
    private int ResolveNode(Transform node, Transform root)
    {
        if (node == null) return -1;
        string path = node == root ? string.Empty : node.name;
        for (var parent = node.parent; parent != null && parent != root; parent = parent.parent) path = parent.name + "/" + path;
        for (int i = 0; i < Archetype.Nodes.Count; i++) if (Archetype.Nodes[i].AnimationPath == path) return i;
        return -1;
    }
    private bool IsChild(int node, int parent)
    { for (int i = node; i >= 0; i = Archetype.Nodes[i].ParentIndex) if (i == parent) return true; return false; }
    internal void Dispose()
    {
        for (int i = 0; i < warningMaterials.Length; i++)
            if (warningMaterials[i] != null) for (int j = 0; j < 3; j++) UnityEngine.Object.Destroy(warningMaterials[i][j]);
    }
}
}
