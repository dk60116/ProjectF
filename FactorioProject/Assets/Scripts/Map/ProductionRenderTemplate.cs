using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Rendering;

internal sealed class ProductionRenderTemplate
{
    internal sealed class Recipe
    {
        internal int OutputId, OutputCount;
        internal float OutputRate, Duration;
        internal long Energy;
        internal bool PublishedManualAvailable;
        private long manualCheckTick = long.MinValue;
        private ItemManager manualManager;
        private bool manualAvailable;
        internal bool IsManualAvailable
        {
            get
            {
                var manager = GameManager.Instance?.ItemManger;
                long tick = MapObjectTickManager.CurrentSimulationTick;
                if (manualCheckTick != tick || !ReferenceEquals(manualManager, manager))
                {
                    manualCheckTick = tick; manualManager = manager;
                    manualAvailable = manager != null && manager.IsManualRequirementSatisfied(OutputId);
                }
                return manualAvailable;
            }
        }
        internal readonly List<CraftingTreeRuntime.IngredientEntry> Inputs = new List<CraftingTreeRuntime.IngredientEntry>();
    }
    internal readonly ItemDefinition Definition;
    internal readonly MapObjectArchetype Archetype;
    internal readonly InstallationRigidAnimationTemplate Animation;
    internal readonly Vector3 Scale, PowerLinePoint, ConsumePoint;
    internal readonly Bounds LocalBounds, ColliderBounds;
    internal readonly PhysicsMaterial ColliderMaterial;
    internal readonly bool ColliderTrigger;
    internal readonly uint ColliderIncludeLayers, ColliderExcludeLayers;
    internal readonly float Watts, PrimaryRate, WorkGaugeVerticalOffset, InputConsumeMoveInterval;
    internal readonly Color WorkGaugeFillColor;
    internal readonly Recipe[] Recipes;
    internal readonly bool HasPipePorts;
    internal readonly bool IsOilDrill;
    internal readonly float OilLitersPerSecond;
    internal readonly Sprite EnergyMarkerIcon, FluidMarkerIcon;
    private readonly bool[] partActive;
    private readonly SpriteRenderer[] icons;
    private readonly Matrix4x4[] iconMatrices;
    private readonly ProductionEffectTemplate[] effects;

    internal ProductionRenderTemplate(InputOutputModule source, InstallationPlacementController controller)
    {
        EnergyMarkerIcon = controller != null ? controller.ResolveInputEnergyMarkerIcon(source) : null;
        FluidMarkerIcon = controller != null ? controller.ResolveFallbackPipePassMarkerIcon() : null;
        Definition = source.BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(source.ResolveItemId());
        Archetype = Definition.MapObjectArchetype;
        Animation = InstallationRigidAnimationTemplate.Create(Archetype, ignoreMissingNodes: true);
        Scale = Archetype.DefaultRootScale; LocalBounds = Archetype.LocalRenderBounds;
        Watts = ItemDefinition.ResolveElectricUseWatts(Definition);
        PrimaryRate = ItemDefinition.ResolveUseEnergyRatePerSecond(Definition);
        IsOilDrill = source is OilDrillingMachine;
        OilLitersPerSecond = IsOilDrill ? Definition.FluidOutputLitersPerSecond : 0;
        WorkGaugeVerticalOffset = source.WorkGaugeVerticalOffset;
        WorkGaugeFillColor = source.ObjectInfoWorkGaugeFillColor;
        ConsumePoint = source.transform.worldToLocalMatrix.MultiplyPoint3x4(source.DataConsumeTargetWorldPosition);
        InputConsumeMoveInterval = source.DataInputConsumeMoveInterval;
        PowerLinePoint = source.TryGetPowerLinePoint(out Transform point)
            ? source.transform.worldToLocalMatrix.MultiplyPoint3x4(point.position) : Vector3.up;
        var collider = source.GetComponent<BoxCollider>();
        ColliderBounds = new Bounds(collider.center, collider.size); ColliderMaterial = collider.sharedMaterial;
        ColliderTrigger = collider.isTrigger; ColliderIncludeLayers = (uint)collider.includeLayers.value;
        ColliderExcludeLayers = (uint)collider.excludeLayers.value;
        partActive = new bool[Archetype.RenderParts.Count];
        for (int i = 0; i < partActive.Length; i++)
        {
            var part = Archetype.RenderParts[i]; bool active = part.EnabledByDefault;
            for (int node = part.NodeIndex; active && node >= 0; node = Archetype.Nodes[node].ParentIndex)
                active &= Archetype.Nodes[node].ActiveByDefault;
            partActive[i] = active;
        }
        var recipes = new List<Recipe>();
        if (source is OilDrillingMachine drill && drill.TryGetObjectInfoOutputRate(out int oilId, out _)
            && InputOutputModule.ResolveItemDefinition(oilId)?.isFluid == true)
            recipes.Add(new Recipe { OutputId = oilId, OutputCount = 1, OutputRate = OilLitersPerSecond,
                Duration = OilLitersPerSecond > 0 ? 1f / OilLitersPerSecond : 1f });
        foreach (var pair in source.InputOutputPairs)
        {
            if (pair?.outputs == null) continue;
            foreach (var output in pair.outputs)
            {
                if (output.itemDefinition == null) continue;
                var recipe = new Recipe { OutputId = output.itemDefinition.id, OutputCount = output.ResolvedItemCount,
                    OutputRate = output.ResolvedAmount };
                bool machine = source is ProductionMachine;
                recipe.Duration = machine ? output.itemDefinition.CraftingDurationSeconds : source.CraftDurationSeconds;
                recipe.Energy = DeterministicSimulationUnits.FromFloat(machine ? PrimaryRate * recipe.Duration
                    : InputOutputModule.ResolveCompleteEnergy(Definition, source.CraftDurationSeconds));
                if (!machine && PrimaryRate > 0) recipe.Duration = (float)DeterministicSimulationUnits.ToFloat(recipe.Energy) / PrimaryRate;
                if (machine && CraftingTreeRuntime.TryGetIngredientsView(recipe.OutputId, out var binary))
                {
                    for (int i = 0; i < binary.Count; i++) recipe.Inputs.Add(binary[i]);
                    recipe.OutputRate = CraftingTreeRuntime.GetOutputAmount(recipe.OutputId);
                    recipe.OutputCount = CraftingTreeRuntime.GetOutputCount(recipe.OutputId);
                }
                else if (pair.inputs != null)
                    foreach (var input in pair.inputs)
                        if (input.itemDefinition != null) recipe.Inputs.Add(new CraftingTreeRuntime.IngredientEntry(input.itemDefinition.id, input.ResolvedAmount));
                // Collapse duplicate ingredients once per definition, never in the hot path.
                for (int i = 0; i < recipe.Inputs.Count; i++)
                    for (int j = recipe.Inputs.Count - 1; j > i; j--)
                        if (recipe.Inputs[i].itemId == recipe.Inputs[j].itemId)
                        {
                            recipe.Inputs[i] = new CraftingTreeRuntime.IngredientEntry(recipe.Inputs[i].itemId,
                                recipe.Inputs[i].amount + recipe.Inputs[j].amount); recipe.Inputs.RemoveAt(j);
                        }
                if (recipe.Inputs.Count > 0 && (!(source is ProductionMachine production)
                    || recipe.Inputs.Count <= production.MaximumProductionIngredientTypes)) recipes.Add(recipe);
            }
        }
        Recipes = recipes.ToArray();
        for (int i = 0; i < Recipes.Length; i++)
            Recipes[i].PublishedManualAvailable = IsOilDrill || Recipes[i].IsManualAvailable;
        foreach (var placement in source.RectGridPlacements) HasPipePorts |= InputOutputModule.AllowsPipeAreaInteraction(placement.blockType);
        icons = source is ProductionMachine machineSource ? machineSource.TargetIconDisplays : Array.Empty<SpriteRenderer>();
        iconMatrices = new Matrix4x4[icons.Length];
        for (int i = 0; i < icons.Length; i++) if (icons[i] != null)
            iconMatrices[i] = source.transform.worldToLocalMatrix * icons[i].transform.localToWorldMatrix;
        var particles = source.DataCraftParticleEffect != null ? source.DataCraftParticleEffect.GetComponentsInChildren<ParticleSystem>(true) : Array.Empty<ParticleSystem>();
        effects = new ProductionEffectTemplate[particles.Length];
        for (int i = 0; i < particles.Length; i++) effects[i] = new ProductionEffectTemplate(particles[i], source.transform);
    }
    internal int Append(ProductionFacilityInstance facility, VirtualRenderBatchCollection batches, SpriteMeshCache spriteMeshes,
        InstallationMaterialVariants materials, Camera camera)
    {
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking) facility.SampleBenchmarkEnergyVisuals();
        Animation?.Evaluate(facility.AnimationPhase, facility.IsWorking);
        Matrix4x4 root = facility.RootMatrix * Matrix4x4.Scale(Vector3.one * facility.PlacementPresentationScale);
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
        Sprite icon = icons.Length > 0 ? InputOutputModule.ResolveItemDefinition(facility.OutputItemId)?.icon : null;
        for (int i = 0; icon != null && i < icons.Length; i++)
        {
            var renderer = icons[i];
            if (renderer == null) continue;
            var mesh = spriteMeshes.Get(icon);
            var material = materials.Resolve(renderer, renderer.sharedMaterial, 0, renderer, icon);
            if (mesh == null || material == null) continue;
            Matrix4x4 matrix = root * iconMatrices[i];
            if (renderer.flipX || renderer.flipY) matrix *= Matrix4x4.Scale(new Vector3(renderer.flipX ? -1 : 1, renderer.flipY ? -1 : 1, 1));
            batches.AddMatrix(new VirtualRenderBatchKey(mesh, material, renderer.gameObject.layer, 0,
                renderer.shadowCastingMode, renderer.receiveShadows, false, batchCellX: Mathf.FloorToInt(matrix.m03 / 16),
                batchCellZ: Mathf.FloorToInt(matrix.m23 / 16), renderingLayerMask: renderer.renderingLayerMask), matrix);
        }
        if (facility.IsWorking && camera != null)
            for (int i = 0; i < effects.Length; i++) effects[i].Append(facility, root, camera, batches);
        return count;
    }
    internal void Dispose() { for (int i = 0; i < effects.Length; i++) effects[i].Dispose(); }
}
