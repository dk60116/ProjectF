using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;
using ProjectF.Simulation;
using ProjectF.Benchmark;

public sealed partial class ProductionFacilityInstance : IMapObjectTarget, IDataElectricConsumer,
    IMapObjectUpdateTick, IMapObjectUpdateTickDeadline, IMapObjectSimulationIdentity, IDataItemProducer, IDataFluidProducer, IProductionTargetSelection, IProductionFacilityInfo
{
    internal readonly ProductionWorld World;
    internal readonly int Index;
    internal readonly uint Generation;
    internal readonly ProductionRenderTemplate Template;
    internal int OrderIndex;
    internal long FluidStorageStateRevision;
    internal readonly int MarkerCount;
    private ref ProductionWorld.State Data => ref World.GetState(Index, Generation);
    private InputOutputModule.PersistentState Io => Placement.inputOutputState;
    public MapObjectHandle Handle { get; }
    public InputOutputModule Prototype { get; }
    public BlockStateStore.InstallationSaveState Placement { get; }
    public Vector2Int StorageKey => BlockStateStore.GetInstallationStorageKey(Placement);
    public Vector2Int AnchorCoordinate => Placement.anchorCoordinate;
    public Vector3 WorldPosition => Placement.worldPosition;
    public Quaternion WorldRotation => Placement.worldRotation;
    public bool IsRuntimeActive => World.Contains(Index, Generation) && VirtualObjectWorld.Current != null
        && VirtualObjectWorld.Current.IsHandleAlive(Handle);
    public bool IsTargetActive => IsRuntimeActive;
    public MapObject SceneObject => null;
    public string ObjectName => Prototype.ObjectName;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public ItemDefinition BoundItemDefinition => Template.Definition;
    public int ResolveItemId() => Placement.itemId;
    public int ResolvedItemId => Placement.itemId;
    public int ID => Placement.itemId;
    public long SimulationId => Placement.placementSequence;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Placement.occupiedCoordinates;
    public List<Vector2Int> OutputCoordinates => Io.outputCoordinates;
    IReadOnlyList<Vector2Int> IDataItemProducer.OutputCoordinates => OutputCoordinates;
    public Vector3 PowerLineWorldPosition => RootMatrix.MultiplyPoint3x4(Template.PowerLinePoint);
    public Matrix4x4 RootMatrix { get; }
    public Bounds CullBounds { get; }
    internal Vector3 ConsumeWorldPosition => RootMatrix.MultiplyPoint3x4(Template.ConsumePoint);
    public float FocusActivationRadius => Prototype.FocusActivationRadius;
    TerrainGenerator IDataItemProducer.Terrain => World.Terrain;
    BlockStateStore IDataItemProducer.Store => World.Store;
    InputOutputModule IDataItemProducer.OutputPrototype => Prototype;
    internal bool PlacementPresentationSuppressed { get; set; }
    internal float PlacementPresentationScale { get; set; } = 1f;
    public bool HasActiveWork => IsRuntimeActive && (Template.IsOilDrill ? Data.HasTarget || Io.productionOutputFluidUnits > 0 : Data.Production.Active);
    public bool IsWaitingForOutput => Template.IsOilDrill ? IsRuntimeActive && OilWaitingForOutput : HasActiveWork && Data.Production.WaitingForOutput;
    public bool IsWorking => HasActiveWork && !IsWaitingForOutput && Data.SupplyRatio > 0;
    public int OutputItemId => Template.IsOilDrill ? SelectedRecipe?.OutputId ?? -1
        : HasActiveWork ? Data.Production.OutputItemId : SelectedRecipe?.OutputId ?? -1;
    internal ProductionRenderTemplate.Recipe ActiveRecipe => Data.Production.Active && Data.Production.RecipeIndex >= 0
        && Data.Production.RecipeIndex < Template.Recipes.Length ? Template.Recipes[Data.Production.RecipeIndex] : null;
    internal ProductionRenderTemplate.Recipe SelectedRecipe
    {
        get
        {
            if (Template.IsOilDrill) return Template.Recipes.Length > 0 ? Template.Recipes[0] : null;
            if (!(Prototype is ProductionMachine)) return ResolveAutomaticRecipe(availableOnly: true);
            if (!Placement.itemFilterMaskInitialized) return null;
            for (int i = 0; i < Template.Recipes.Length; i++)
                if (IsRecipeAvailable(Template.Recipes[i])) return Template.Recipes[i];
            return null;
        }
    }
    // Automatic modules select recipes from their inputs; only ProductionMachine has a target filter.
    internal bool IsRecipeAvailable(ProductionRenderTemplate.Recipe recipe) =>
        Template.IsOilDrill || ProjectF.Benchmark.BenchmarkRuntime.ForceWorking || recipe.IsManualAvailable
        && (!(Prototype is ProductionMachine) || Placement.itemFilterMaskInitialized && IsItemFilterEnabled(recipe.OutputId, 0));
    private ProductionRenderTemplate.Recipe ResolveAutomaticRecipe(bool availableOnly, bool fallbackToFirstRecipe = true)
    {
        ProductionRenderTemplate.Recipe fallback = null, occupied = null;
        int bestMatch = 0;
        bool benchmark = ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
        for (int i = 0; i < Template.Recipes.Length; i++)
        {
            var recipe = Template.Recipes[i];
            if (availableOnly && !IsRecipeAvailable(recipe)) continue;
            fallback ??= recipe;
            if (benchmark) return recipe;
            int matched = 0; bool complete = true;
            for (int j = 0; j < recipe.Inputs.Count; j++)
            {
                var input = recipe.Inputs[j];
                bool fluid = InputOutputModule.ResolveItemDefinition(input.itemId)?.isFluid == true;
                long stored = fluid ? FluidUnits(input.itemId) : IngredientCount(input.itemId);
                long required = fluid ? RequiredFluidUnits(recipe, input.amount) : input.count;
                if (stored > 0) matched++;
                if (stored < required) complete = false;
            }
            if (complete) return recipe;
            if (matched > bestMatch) { bestMatch = matched; occupied = recipe; }
        }
        return occupied ?? (fallbackToFirstRecipe ? fallback : null);
    }
    public float WorkProgress
    {
        get
        {
            if (!HasActiveWork) return 0;
            if (Template.IsOilDrill) return OilWorkProgress;
            var snapshot = Data.Production;
            if (!snapshot.WaitingForOutput) AdvanceSnapshot(ref snapshot, MapObjectTickManager.CurrentSimulationTick - Data.SampleTick, Data.SupplyRatio);
            return snapshot.WaitingForOutput ? 1f : Data.CompleteEnergy > 0
                ? Mathf.Clamp01((float)((double)snapshot.ConsumedEnergyUnits / Data.CompleteEnergy))
                : Data.DurationTicks > 0 ? Mathf.Clamp01(1f - (float)((double)snapshot.RemainingTicks / Data.DurationTicks)) : 0;
        }
    }
    internal double AnimationPhase => Data.AnimationPhase + (IsWorking
        ? Math.Max(0, MapObjectTickManager.CurrentSimulationTick - Data.SampleTick) * (double)SimulationTickWorld.FixedSimulationDeltaSeconds * Data.SupplyRatio : 0);
    internal Vector3 WorkGaugeWorldPosition => new Vector3(CullBounds.center.x, CullBounds.max.y + Template.WorkGaugeVerticalOffset, CullBounds.center.z);
    public long NextUpdateTick
    {
        get
        {
            long now = MapObjectTickManager.CurrentSimulationTick;
            if (!IsRuntimeActive) return long.MaxValue;
            if (Data.NeedsEvaluation) return now + 1;
            if (Template.IsOilDrill) return Data.HasTarget || Io.productionOutputFluidUnits > 0 ? now + 1 : long.MaxValue;
            long next = long.MaxValue;
            if (IsWorking)
            {
                double seconds = Data.CompleteEnergy > 0
                    ? (Data.CompleteEnergy - Data.Production.ConsumedEnergyUnits) / ((double)DeterministicSimulationUnits.UnitsPerWhole * Template.PrimaryRate * Data.SupplyRatio)
                    : Data.Production.RemainingTicks * (double)SimulationTickWorld.FixedSimulationDeltaSeconds / Data.SupplyRatio;
                next = now + Math.Max(1, (long)Math.Ceiling(seconds / SimulationTickWorld.FixedSimulationDeltaSeconds));
            }
            // Fuel is sampled at depletion/completion, including standby consumption. Fluid movement still needs fixed ticks.
            if (Data.HasTarget && Data.SupplyRatio > 0 && !ProjectF.Benchmark.BenchmarkRuntime.ForceWorking)
                for (int i = 0; i < Template.Definition.UseEnergyRequirementCount; i++)
                {
                    if (!Template.Definition.TryGetUseEnergyRequirement(i, out var requirement)
                        || requirement.energyType == ItemDefinition.EnergyType.None || requirement.energyType == ItemDefinition.EnergyType.Electricity) continue;
                    float rate = ItemDefinition.ResolveUseEnergyRatePerSecond(Template.Definition, requirement.energyType);
                    if (rate <= 0) continue;
                    long remaining = Math.Max(0, Fuel(requirement.energyType) - DeterministicSimulationUnits.RateForTicks(rate, Math.Max(0, now - Data.SampleTick)));
                    long ticks = Math.Max(1, (long)Math.Ceiling(remaining / ((double)DeterministicSimulationUnits.UnitsPerWhole * rate)
                        * SimulationTickWorld.DefaultSimulationTicksPerSecond));
                    next = Math.Min(next, now + ticks);
                }
            if (Data.HasTarget && HasFluidRecipe) next = Math.Min(next, now + 1);
            return next;
        }
    }
    private bool HasFuelRequirement
    {
        get
        {
            for (int i = 0; i < Template.Definition.UseEnergyRequirementCount; i++)
                if (Template.Definition.TryGetUseEnergyRequirement(i, out var e) && e.energyType != ItemDefinition.EnergyType.None
                    && e.energyType != ItemDefinition.EnergyType.Electricity && e.useEnergyAmount > 0) return true;
            return false;
        }
    }
    private bool HasFluidRecipe
    {
        get
        {
            var recipe = ActiveRecipe ?? SelectedRecipe;
            if (recipe == null) return false;
            if (InputOutputModule.ResolveItemDefinition(recipe.OutputId)?.isFluid == true) return true;
            for (int i = 0; i < recipe.Inputs.Count; i++)
                if (InputOutputModule.ResolveItemDefinition(recipe.Inputs[i].itemId)?.isFluid == true) return true;
            return false;
        }
    }
    internal ProductionFacilityInstance(ProductionWorld world, int index, uint generation, MapObjectHandle handle,
        InputOutputModule prototype, BlockStateStore.InstallationSaveState placement, ProductionRenderTemplate template)
    {
        World = world; Index = index; Generation = generation; Handle = handle; Prototype = prototype; Placement = placement; Template = template;
        Placement.inputOutputState ??= new InputOutputModule.PersistentState();
        for (int i = 0; i < Io.gridCoordinates.Count; i++)
            if (Prototype.TryGetRectGridBlockTypeAtCoordinate(Prototype, AnchorCoordinate, Placement.quarterTurns,
                Io.gridCoordinates[i], out var type) && InputOutputModule.IsInputOutputAreaBlockType(type)) MarkerCount++;
        RootMatrix = Matrix4x4.TRS(WorldPosition, WorldRotation, Template.Scale);
        CullBounds = VirtualRenderBatchCollection.CalculateWorldBounds(Template.LocalBounds, RootMatrix);
        Data.Production = new ProductionProcess { Active = Io.hasActiveCraft, WaitingForOutput = Io.waitingForOutput,
            RecipeIndex = -1, OutputItemId = Io.activeOutputItemId, OutputCount = Io.activeOutputCount,
            RemainingTicks = Io.hasDeterministicUnits ? Io.remainingCraftTicks : DeterministicSimulationUnits.SecondsToTicks(Io.remainingCraftTime),
            ConsumedEnergyUnits = Io.hasDeterministicUnits ? Io.activeCraftConsumedEnergyUnits : DeterministicSimulationUnits.FromFloat(Io.activeCraftConsumedEnergy) };
        // Recipes can share an output (for example alternative glass ingredients).
        // Preserve the saved recipe before falling back to output identity for older snapshots.
        if (Data.Production.Active)
        {
            int savedIndex = Io.activeRecipeIndex;
            if (savedIndex >= 0 && savedIndex < Template.Recipes.Length && Template.Recipes[savedIndex].OutputId == Io.activeOutputItemId)
                Data.Production.RecipeIndex = savedIndex;
            else
                for (int i = 0; i < Template.Recipes.Length; i++)
                    if (Template.Recipes[i].OutputId == Io.activeOutputItemId) { Data.Production.RecipeIndex = i; break; }
            if (Data.Production.RecipeIndex >= 0) SetBudget(Template.Recipes[Data.Production.RecipeIndex]);
        }
        if (Data.Production.Active && Data.Production.RecipeIndex < 0) Data.Production.Clear();
        FacilityFuel.RestoreLegacy(Io, Template.Definition);
        if (Template.IsOilDrill)
        {
            // Oil progress and harvested-but-undelivered oil have distinct save fields.
            OilTargetCoordinate = OilDrillingMachine.ResolveOilTargetCoordinate(AnchorCoordinate, Placement.quarterTurns);
            Data.Production.Clear();
            Data.Oil.ProgressUnits = Io.ResolveOilDrillingProgressUnits();
            Io.productionOutputFluidUnits = Math.Max(0, Io.productionOutputFluidUnits);
        }
    }
    public bool IsItemFilterEnabled(int itemId, int count) => MapObject.IsItemAllowedByFilterMask(itemId, Placement.itemFilterMaskInitialized, Placement.itemFilterMaskWords);
    public void SetItemFilterEnabled(int itemId, int count, bool enabled)
    {
        if (itemId < 0) return;
        var words = Placement.itemFilterMaskWords;
        int required = (Math.Max(count, itemId + 1) + 63) >> 6;
        while (words.Count < required) words.Add(ulong.MaxValue);
        Placement.itemFilterMaskInitialized = true;
        if (enabled) words[itemId >> 6] |= 1UL << (itemId & 63); else words[itemId >> 6] &= ~(1UL << (itemId & 63));
        World.MarkDisplayDirty(this); if (Template.HasPipePorts) World.WakeNativeProducers(this); Wake();
    }
    public void SetExclusiveProductionTarget(int item)
    {
        if (!(Prototype is ProductionMachine)) return;
        for (int i = 0; i < Template.Recipes.Length; i++) SetItemFilterEnabled(Template.Recipes[i].OutputId, 0, false);
        if (item >= 0) SetItemFilterEnabled(item, 0, true);
    }
    public bool IsProductionTargetSelected(int item) => Prototype is ProductionMachine && SelectedRecipe?.OutputId == item;
    public void ClearProductionTargetSelection() => SetExclusiveProductionTarget(-1);
    public bool CanSelectProductionTarget(int item) => Prototype is ProductionMachine machine && machine.CanSelectProductionTarget(item);
    public bool TryCollectAllProductionTargetItemIds(ICollection<int> items) => Prototype is ProductionMachine machine
        && machine.TryCollectAllProductionTargetItemIds(items);
    public bool TryGetElectricPowerRequirement(out float watts) { watts = Template.Watts; return IsRuntimeActive && watts > 0; }
    public bool TryGetElectricPowerDemand(out float watts) { watts = Template.Watts; return IsRuntimeActive && Data.HasTarget && watts > 0; }
    public void WakeForElectricPowerChange() => Wake();
    public void Wake()
    {
        if (!IsRuntimeActive) return;
        RefreshBenchmarkInputs();
        if (Data.NeedsEvaluation) return;
        Sample(); Data.NeedsEvaluation = true;
        if (Template.IsOilDrill) { World.OilBatch.Wake(this); return; }
        if (FacilitySimulationWorld.IsScheduled(this)) FacilitySimulationWorld.RefreshSchedule(this);
        else FacilitySimulationWorld.SetScheduled(this, true);
    }
    private void SetBudget(ProductionRenderTemplate.Recipe recipe)
    { Data.CompleteEnergy = recipe.Energy; Data.DurationTicks = DeterministicSimulationUnits.SecondsToTicks(recipe.Duration); }
    private void AdvanceSnapshot(ref ProductionProcess production, long elapsed, float ratio)
    {
        if (elapsed <= 0 || ratio <= 0) return;
        production.Advance(Data.CompleteEnergy > 0 ? 0 : (long)Math.Floor(elapsed * (double)ratio), Data.CompleteEnergy > 0,
            DeterministicSimulationUnits.RateForTicks(Template.PrimaryRate * ratio, elapsed), Data.CompleteEnergy);
    }
    private void Sample()
    {
        ref var state = ref Data;
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking) BenchmarkInputSupply.SampleEnergy(this, ConsumeWorldPosition, state.HasTarget && state.SupplyRatio > 0);
        long now = MapObjectTickManager.CurrentSimulationTick, elapsed = Math.Max(0, now - state.SampleTick);
        state.SampleTick = now;
        if (elapsed == 0 || !state.HasTarget || state.SupplyRatio <= 0) return;
        float ratio = state.SupplyRatio;
        int typeMask = 0;
        for (int i = 0; !ProjectF.Benchmark.BenchmarkRuntime.ForceWorking && i < Template.Definition.UseEnergyRequirementCount; i++)
        {
            if (!Template.Definition.TryGetUseEnergyRequirement(i, out var requirement)) continue;
            var type = requirement.energyType;
            int bit = 1 << (int)type;
            if (type == ItemDefinition.EnergyType.None || type == ItemDefinition.EnergyType.Electricity || (typeMask & bit) != 0) continue;
            typeMask |= bit;
            long requested = DeterministicSimulationUnits.RateForTicks(ItemDefinition.ResolveUseEnergyRatePerSecond(Template.Definition, type), elapsed);
            if (requested <= 0) continue;
            long spent = SpendFuel(type, requested);
            ratio = Math.Min(ratio, (float)((double)spent / requested));
        }
        if (Template.IsOilDrill)
        {
            if (!OilFastForced)
            {
                if (ratio == state.SupplyRatio) state.Oil.AdvanceUnits(elapsed, state.OilUnitsPerTick);
                else state.Oil.Advance(elapsed, Template.OilLitersPerSecond * state.OilRetention, ratio);
            }
            state.AnimationPhase += elapsed * (double)SimulationTickWorld.FixedSimulationDeltaSeconds * ratio;
        }
        else if (state.Production.Active && !state.Production.WaitingForOutput)
        {
            bool wasOutputting = state.Production.WaitingForOutput;
            AdvanceSnapshot(ref state.Production, elapsed, ratio);
            if (!wasOutputting && state.Production.WaitingForOutput) World.MarkDisplayDirty(this);
            state.AnimationPhase += elapsed * (double)SimulationTickWorld.FixedSimulationDeltaSeconds * ratio;
        }
    }
    public void ManagedUpdateTick(float ignoredDelta)
    {
        if (!IsRuntimeActive) { if (Template.IsOilDrill) World.OilBatch.Unregister(this); else FacilitySimulationWorld.Unregister(this); return; }
        World.ProcessedUpdates++; Sample(); Data.NeedsEvaluation = false;
        if (Template.IsOilDrill) { ApplyOilDrillingTick(); return; }
        bool benchmark = ProjectF.Benchmark.BenchmarkRuntime.ForceWorking;
        var recipe = ActiveRecipe ?? SelectedRecipe;
        if (benchmark && recipe == null && Template.Recipes.Length > 0) recipe = Template.Recipes[0];
        if (!EvaluateTarget(recipe, benchmark)) { Sleep(); return; }
        if (!benchmark && !Data.Production.Active) IntakeFluids(recipe);
        if (Data.SupplyRatio <= 0 && !IsWaitingForOutput) { Reschedule(HasFluidRecipe); return; }
        if (IsWaitingForOutput)
        {
            if (!TryDrainOutput(recipe, benchmark)) { Reschedule(HasFluidRecipe || HasFuelRequirement && Data.SupplyRatio > 0); return; }
            bool fluidBatch = HasFluidRecipe;
            Data.Production.Clear(); Io.productionOutputFluidUnits = -1; World.MarkDisplayDirty(this);
            if (fluidBatch) World.WakeNativeProducers(this);
            recipe = SelectedRecipe;
            if (benchmark && recipe == null && Template.Recipes.Length > 0) recipe = Template.Recipes[0];
            if (!EvaluateTarget(recipe, benchmark)) { Sleep(); return; }
            if (Data.SupplyRatio <= 0) { Reschedule(HasFluidRecipe); return; }
        }
        if (!Data.Production.Active)
        {
            for (int i = 0; i < Template.Recipes.Length; i++)
            {
                var candidate = Template.Recipes[i];
                if (Prototype is ProductionMachine && !ReferenceEquals(candidate, recipe) || !benchmark && !IsRecipeAvailable(candidate)) continue;
                if (!benchmark && !HasInputs(candidate)) continue;
                if (!ConsumeInputs(candidate)) continue;
                SetBudget(candidate);
                Data.Production.Begin(i, candidate.OutputId, candidate.OutputCount, Data.DurationTicks);
                Data.SampleTick = MapObjectTickManager.CurrentSimulationTick;
                Io.productionOutputFluidUnits = -1; World.MarkDisplayDirty(this); break;
            }
        }
        Reschedule(IsWorking || HasFluidRecipe || HasFuelRequirement && Data.SupplyRatio > 0);
    }
    private bool EvaluateTarget(ProductionRenderTemplate.Recipe recipe, bool benchmark)
    {
        bool target = recipe != null && OutputCoordinates.Count > 0;
        if (Data.HasTarget != target) { Data.HasTarget = target; UtilityPole.InvalidateDataConsumerDemand(this); }
        Data.SupplyRatio = target ? ResolveSupplyRatio(benchmark) : 0;
        return target;
    }
    private void Sleep()
    {
        // A synchronous stack notification may have queued a wake during this evaluation.
        // Clear that pending flag when sleeping so the next real input/power change can wake us.
        Data.NeedsEvaluation = false;
        if (Template.IsOilDrill) World.OilBatch.Sleep(this); else FacilitySimulationWorld.SetScheduled(this, false);
    }
    private void Reschedule(bool poll)
    { if (!poll) Sleep(); else if (Template.IsOilDrill) World.OilBatch.KeepScheduled(this); else FacilitySimulationWorld.RefreshSchedule(this); }
    private float ResolveSupplyRatio(bool benchmark)
    {
        if (benchmark) return 1;
        float ratio = 1;
        if (Template.Watts > 0) { UtilityPole.TryGetElectricSupplyRatio(this, Template.Watts, out float electric); ratio = electric; }
        for (int i = 0; i < Template.Definition.UseEnergyRequirementCount; i++)
            if (Template.Definition.TryGetUseEnergyRequirement(i, out var e) && e.energyType != ItemDefinition.EnergyType.None
                && e.energyType != ItemDefinition.EnergyType.Electricity && e.useEnergyAmount > 0
                && Fuel(e.energyType) <= 0 && !RefillFuel(e.energyType)) return 0;
        return ratio;
    }
    private long Fuel(ItemDefinition.EnergyType type) => FacilityFuel.Stored(Io, type);
    private bool RefillFuel(ItemDefinition.EnergyType type) => FacilityFuel.Refill(this, type, ConsumeWorldPosition, Template.InputConsumeMoveInterval);
    private long SpendFuel(ItemDefinition.EnergyType type, long requested) => FacilityFuel.Spend(this, type, requested, ConsumeWorldPosition, Template.InputConsumeMoveInterval);
    internal int CountAt(Vector2Int coordinate, int item) => FacilityFuel.CountAt(this, coordinate, item);
    private int ConsumeAt(Vector2Int coordinate, int item, int count) => ProjectF.Benchmark.BenchmarkRuntime.ForceWorking
        ? BenchmarkInputSupply.Consume(this, World.Terrain, coordinate, item, count, ConsumeWorldPosition, Template.InputConsumeMoveInterval)
        : FacilityFuel.ConsumeAt(this, coordinate, item, count, ConsumeWorldPosition, Template.InputConsumeMoveInterval);
    internal int IngredientCount(int item)
    {
        int count = 0;
        for (int i = 0; i < Io.inputItemAreas.Count; i++)
            if (Io.inputItemAreas[i].itemId < 0 || Io.inputItemAreas[i].itemId == item) count = (int)Math.Min(int.MaxValue, (long)count + CountAt(Io.inputItemAreas[i].coordinate, item));
        return count;
    }
    private bool HasInputs(ProductionRenderTemplate.Recipe recipe)
    {
        var reserved = World.InputAreaScratch;
        reserved.Clear();
        for (int i = 0; i < recipe.Inputs.Count; i++)
        {
            var ingredient = recipe.Inputs[i];
            if (InputOutputModule.ResolveItemDefinition(ingredient.itemId)?.isFluid == true)
            {
                if (FluidUnits(ingredient.itemId) < RequiredFluidUnits(recipe, ingredient.amount)) return false;
                reserved.Add(-1); continue;
            }
            if (!(Prototype is ProductionMachine))
            { if (IngredientCount(ingredient.itemId) < ingredient.count) return false; reserved.Add(-1); continue; }
            int selected = -1;
            for (int j = 0; j < Io.inputItemAreas.Count; j++)
            {
                var area = Io.inputItemAreas[j];
                if (area.itemId >= 0 && area.itemId != ingredient.itemId || CountAt(area.coordinate, ingredient.itemId) < ingredient.count) continue;
                bool used = false;
                for (int k = 0; k < reserved.Count; k++)
                    if (reserved[k] >= 0 && Io.inputItemAreas[reserved[k]].coordinate == area.coordinate) { used = true; break; }
                if (!used) { selected = j; break; }
            }
            if (selected < 0) return false;
            reserved.Add(selected);
        }
        return true;
    }
    private bool ConsumeInputs(ProductionRenderTemplate.Recipe recipe)
    {
        if (ProjectF.Benchmark.BenchmarkRuntime.ForceWorking)
        {
            RefreshBenchmarkInputs();
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                var input = recipe.Inputs[i];
                if (!ProjectF.Benchmark.BenchmarkRuntime.IsPortableItem(InputOutputModule.ResolveItemDefinition(input.itemId))) continue;
                for (int j = 0; j < Io.inputItemAreas.Count; j++)
                    if (Io.inputItemAreas[j].itemId < 0 || Io.inputItemAreas[j].itemId == input.itemId)
                    { ConsumeAt(Io.inputItemAreas[j].coordinate, input.itemId, input.count); break; }
            }
            return true;
        }
        for (int i = 0; i < recipe.Inputs.Count; i++)
        {
            var ingredient = recipe.Inputs[i];
            if (InputOutputModule.ResolveItemDefinition(ingredient.itemId)?.isFluid == true)
            { Io.productionInputFluidUnits[Io.productionInputFluidItemIds.IndexOf(ingredient.itemId)] -= RequiredFluidUnits(recipe, ingredient.amount); continue; }
            if (Prototype is ProductionMachine)
            {
                if (ConsumeAt(Io.inputItemAreas[World.InputAreaScratch[i]].coordinate, ingredient.itemId, ingredient.count) != ingredient.count) return false;
                continue;
            }
            int remaining = ingredient.count;
            for (int j = 0; j < Io.inputItemAreas.Count && remaining > 0; j++)
                if (Io.inputItemAreas[j].itemId < 0 || Io.inputItemAreas[j].itemId == ingredient.itemId)
                    remaining -= ConsumeAt(Io.inputItemAreas[j].coordinate, ingredient.itemId, remaining);
            if (remaining > 0) return false;
        }
        return true;
    }
    internal long FluidUnits(int item) { int i = Io.productionInputFluidItemIds.IndexOf(item); return i < 0 ? 0 : Io.productionInputFluidUnits[i]; }
    internal long RequiredFluidUnits(ProductionRenderTemplate.Recipe recipe, float amount) => DeterministicSimulationUnits.FromFloat(amount
        * (InputOutputModule.ResolveItemDefinition(recipe.OutputId)?.isFluid == true ? recipe.Duration : 1));
    private void IntakeFluids(ProductionRenderTemplate.Recipe recipe) => World.TransferInputFluids(this, recipe);
    private bool TryDrainOutput(ProductionRenderTemplate.Recipe recipe, bool benchmark)
    {
        if (InputOutputModule.ResolveItemDefinition(recipe.OutputId)?.isFluid == true)
        {
            if (Io.productionOutputFluidUnits < 0) Io.productionOutputFluidUnits = DeterministicSimulationUnits.FromFloat(recipe.OutputRate * recipe.Duration);
            if (benchmark) { Io.productionOutputFluidUnits = 0; return true; }
            World.TransferOutputFluid(this, recipe); return Io.productionOutputFluidUnits == 0;
        }
        int remaining = Data.Production.OutputCount;
        if (!MiningItemOutput.TryReserve(this, recipe.OutputId, remaining, out var reservation))
        {
            if (!benchmark) return false;
            for (int i = 0; i < remaining; i++) ProjectF.Benchmark.BenchmarkRuntime.EmitItem(World.Terrain, recipe.OutputId, ConsumeWorldPosition);
            Data.Production.OutputCount = 0; return true;
        }
        int beforeEmission = remaining;
        bool success = MiningItemOutput.Emit(this, reservation, recipe.OutputId, ref remaining, ConsumeWorldPosition);
        Data.Production.OutputCount = remaining;
        if (benchmark) ProjectF.Benchmark.BenchmarkRuntime.RecordItems(beforeEmission - remaining);
        return success;
    }
    internal bool AppendOutputItemIds(ISet<int> items)
    { var recipe = ActiveRecipe ?? SelectedRecipe; return recipe != null && InputOutputModule.ResolveItemDefinition(recipe.OutputId)?.isFluid != true && items.Add(recipe.OutputId); }
    public float GetFluidPressure(int fluid) => !IsRuntimeActive || OutputItemId != fluid ? 0
        : Template.IsOilDrill ? IsWorking ? Template.OilLitersPerSecond * Data.SupplyRatio : 0
        : IsWaitingForOutput && Io.productionOutputFluidUnits != 0 ? ActiveRecipe?.OutputRate ?? 0 : 0;
    public void GetObjectInfoStatus(out string text, out bool working, out bool warning)
    {
        if (Template.IsOilDrill) { GetOilDrillingStatus(out text, out working, out warning); return; }
        working = Data.HasTarget && Data.SupplyRatio > 0 && (IsWorking || IsWaitingForOutput && HasFluidRecipe); warning = false;
        if (!Data.HasTarget && Data.NeedsEvaluation)
        { text = "Waiting for initialization"; warning = true; return; }
        if (!Data.HasTarget)
            text = OutputCoordinates.Count == 0 ? "No output area" : Template.Recipes.Length == 0 ? "No recipe"
                : GameManager.Instance?.ItemManger == null ? "No item data"
                : Prototype is ProductionMachine ? "No target" : "No crafting manual";
        else if (Data.SupplyRatio <= 0) text = "No energy";
        else if (IsWaitingForOutput) { text = HasFluidRecipe ? "Outputting" : "Waiting for output"; warning = !HasFluidRecipe; }
        else if (IsWorking) text = "Working";
        else { text = "Waiting for materials"; warning = true; }
    }
    public void ClearItems()
    {
        if (Template.IsOilDrill) Data.Oil.ProgressUnits = 0;
        World.MarkDisplayDirty(this); Data.Production.Clear(); Io.productionInputFluidItemIds.Clear(); Io.productionInputFluidUnits.Clear();
        Io.productionOutputFluidUnits = Template.IsOilDrill ? 0 : -1; Io.storedEnergyTypes.Clear(); Io.storedEnergyUnitsByType.Clear(); Io.energyGaugeCapacityUnitsByType.Clear(); if (Template.HasPipePorts) World.WakeNativeProducers(this); Wake();
    }
    public void Persist()
    {
        if (!IsRuntimeActive) return;
        Sample(); var process = Data.Production;
        Io.hasActiveCraft = process.Active; Io.waitingForOutput = process.WaitingForOutput;
        Io.activeOutputItemId = process.OutputItemId; Io.activeOutputCount = process.OutputCount;
        Io.activeRecipeIndex = process.RecipeIndex; Io.hasDeterministicUnits = true;
        Io.activeCraftConsumedEnergyUnits = process.ConsumedEnergyUnits;
        Io.activeCraftConsumedEnergy = DeterministicSimulationUnits.ToFloat(process.ConsumedEnergyUnits);
        Io.remainingCraftTicks = Data.CompleteEnergy > 0 ? ProductionProcess.RemainingEnergyTicks(Data.CompleteEnergy,
            process.ConsumedEnergyUnits, DeterministicSimulationUnits.FromFloat(Template.PrimaryRate)) : process.RemainingTicks;
        Io.remainingCraftTime = DeterministicSimulationUnits.TicksToSeconds(Io.remainingCraftTicks);
        FacilityFuel.Persist(Io);
        if (Template.IsOilDrill)
        {
            Io.oilDrillingProgressUnits = Data.Oil.ProgressUnits;
            Io.oilDrillingProgressLiters = DeterministicSimulationUnits.ToFloat(Data.Oil.ProgressUnits);
        }
    }
}
