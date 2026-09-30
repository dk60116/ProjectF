using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y) { public static Vector2Int zero => default; }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b, int c) => Math.Max(a, Math.Max(b, c));
        public static int RoundToInt(float value) => (int)Math.Round(value);
        public static float Round(float value) => (float)Math.Round(value);
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp01(float value) => Math.Clamp(value, 0, 1);
    }
}
namespace ProjectF.Simulation
{
    public static class SimulationTickWorld
    {
        public const int DefaultSimulationTicksPerSecond = 60;
        public const float FixedSimulationDeltaSeconds = 1f / DefaultSimulationTicksPerSecond;
    }
}
public static class MapObjectTickManager
{
    public static long CurrentSimulationTick;
    public const float FixedSimulationDeltaSeconds = 1f / 60;
}
public static class MapObjectTickProfiler
{
    public readonly struct Sample : IDisposable { public void Dispose() { } }
    public static Sample SampleNamed(string group, string owner, string name) => new();
}
public class ItemDefinition { public int id; public bool IsFluid = true; public Color fluidDisplayColor = new(0, 1, 0, 1); }
public class InstallationObject
{
    public sealed class State { public bool activeInHierarchy = true; }
    public readonly State gameObject = new();
    public float Capacity = 100f, Temperature;
    private long storedUnits;
    public float Stored
    {
        get => DeterministicSimulationUnits.ToFloat(storedUnits);
        set => storedUnits = DeterministicSimulationUnits.FromFloat(value);
    }
    public int Fluid = -1;
    public float AcceptanceLimit = float.MaxValue;
    public float AvailableFluidStorageLiters => Math.Max(0, Capacity - Stored);
    public float GetStoredFluidTemperatureCelsius(int id) => 20f;
    public bool TryAddFluidLiters(int id, float requested, float temperature, out float accepted)
    {
        accepted = Fluid < 0 || Fluid == id ? Math.Min(requested, Math.Min(AvailableFluidStorageLiters, AcceptanceLimit)) : 0f;
        if (accepted <= 0) return false;
        storedUnits += DeterministicSimulationUnits.FromFloat(accepted);
        Fluid = id; Temperature = temperature; return true;
    }
    protected static float CalculateFluidPressureRetention(int distance) => Math.Max(0f, 1f - distance * .01f);
}
public partial class InputOutputModule : InstallationObject
{
    public readonly record struct ItemIoEntry(ItemDefinition itemDefinition, float count)
    {
        public bool IsFluid => itemDefinition != null && itemDefinition.IsFluid;
        public float ResolvedAmount => Math.Max(.0001f, count);
        public int ResolvedItemCount => Math.Max(1, (int)count);
    }
    public sealed class InputOutputPair { public List<ItemIoEntry> inputs = new(), outputs = new(); }
    protected InputOutputPair pair = new();
    protected int ActiveOutputItemId = 8, ActiveOutputCount = 2, ActiveRecipeIndex;
    protected bool IsActiveCraftRunning = true, IsWaitingForOutput = true;
    public int PersistenceMarks, SolidOutputCalls;
    public int OutputCapacityNotifications;
    protected static void NotifyFluidOutputCapacityIncreased(InputOutputModule source) => source.OutputCapacityNotifications++;
    public float ObjectInfoWorkGaugeFillAmount;
    public float CraftSeconds = 1f;
    public bool isActiveAndEnabled => gameObject.activeInHierarchy;
    protected ItemDefinition ResolveInstalledDefinition() => new();
    protected float ResolveInitialCraftDuration(ItemDefinition definition) => CraftSeconds;
    protected int ResolveProductionTargetPairIndex(int id) => 0;
    public virtual float GetObjectInfoFluidPressureLitersPerSecond(int id) => 0f;
    protected float plannedDeltaTime;
    public float ManagedUpdateTickIntervalSeconds => .1f;
    private readonly List<Vector2Int> runtimeOutputCoordinates = new() { default };
    private readonly List<FluidOutputConnection> cachedFluidOutputConnections = new();
    private readonly List<FluidOutputTransferCandidate> fluidOutputTransferCandidates = new();
    private bool fluidOutputCapacityBlocked;
    private long cachedFluidOutputRetentionTick = -1;
    private int cachedFluidOutputRetentionStateVersion, cachedFluidOutputRetentionTopologyVersion, cachedFluidOutputRetentionItemId;
    private float cachedFluidOutputRetentionSourceRate, cachedFluidOutputRetention;
    private static int fluidStorageStateVersion, fluidTopologyVersion, fluidOutputRetentionCacheHitCount, fluidOutputRetentionCacheMissCount;
    public bool HasRoutes => cachedFluidOutputConnections.Count > 0;
    public float RecordedOutput;
    public virtual PersistentState CapturePersistentState() => new()
    {
        hasActiveCraft = IsActiveCraftRunning, waitingForOutput = IsWaitingForOutput,
        activeOutputItemId = ActiveOutputItemId, activeOutputCount = ActiveOutputCount, activeRecipeIndex = ActiveRecipeIndex
    };
    public virtual void ApplyPersistentState(PersistentState state)
    {
        IsActiveCraftRunning = state.hasActiveCraft; IsWaitingForOutput = state.waitingForOutput;
        ActiveOutputItemId = state.activeOutputItemId; ActiveOutputCount = state.activeOutputCount; ActiveRecipeIndex = state.activeRecipeIndex;
    }
    public virtual void PrepareForPool() { ClearActiveCraft(); cachedFluidOutputConnections.Clear(); }
    public virtual void ApplyManagedUpdateTick() { }
    [Flags] private enum PlannedModuleCommand { None = 0, PullFluid = 1, AdvanceCraft = 2, StartCraft = 4 }
    private PlannedModuleCommand plannedModuleCommands;
    private bool runtimeSleeping, outputDrainCheckPending, hasStoredOutputOnConveyor;
    private bool hasActiveCraft => IsActiveCraftRunning;
    private bool CanStoreFluid => false;
    private float plannedModuleDeltaTime;
    public int NextCraftStarts;
    protected bool TryBeginPlannedModuleApply(out float dt)
    {
        dt = plannedDeltaTime;
        plannedModuleCommands = IsActiveCraftRunning ? PlannedModuleCommand.AdvanceCraft : PlannedModuleCommand.None;
        return true;
    }
    private void EnsureEffectivePairData() { }
    private bool TryDrainOneOutputAreaItemToConveyor(out bool stored) { stored = false; return false; }
    private void DiscardIncompatibleStoredFluid() { }
    private bool ShouldAutoPullFluidFromConnectedStorage() => false;
    private void PullFluidFromConnectedStorage(float dt) { }
    private void UpdateActiveCraft(float dt) { if (IsWaitingForOutput) TryCompleteActiveCraft(); }
    private void TryStartNextCraft() => NextCraftStarts++;
    private void MarkManagedRuntimeVisualsDirty() { }
    private void RefreshRuntimeUpdateSleepState() { }
    protected virtual bool TryCompleteActiveCraft() { SolidOutputCalls++; ClearActiveCraft(); return true; }
    protected virtual bool ShouldKeepRuntimeUpdateTickActive() => false;
    protected virtual bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy() => false;
    protected virtual bool IsRecipeOutputAllowedByItemFilter(int id) => true;
    protected bool TryGetInputOutputPair(int index, out InputOutputPair result) { result = pair; return index == 0; }
    protected void ClearActiveCraft() { IsActiveCraftRunning = IsWaitingForOutput = false; ActiveOutputItemId = -1; PersistenceMarks++; }
    protected void MarkPersistenceStateDirty() => PersistenceMarks++;
    public static bool IsFluidItemId(int id) => id == 8 || id == 9;
    public static ItemDefinition ResolveItemDefinition(int id) => new()
    { id = id, fluidDisplayColor = id == 9 ? new Color(1,0,0,1) : new Color(0,1,0,1) };
    protected bool TryGetFluidOutputAvailableLiters(int id, float requested, out float available)
    {
        available = 0f;
        foreach (var route in cachedFluidOutputConnections)
            if (CanUseFluidOutputConnectionWithAnySpace(route, id)) available += route.Storage.AvailableFluidStorageLiters;
        fluidOutputCapacityBlocked = HasRoutes && available == 0f;
        return available > 0f;
    }
    private bool EnsureFluidOutputStorageCache() => HasRoutes;
    private bool CanUseFluidOutputConnectionWithAnySpace(FluidOutputConnection route, int id) =>
        route.Storage.gameObject.activeInHierarchy && route.Storage.AvailableFluidStorageLiters > .0001f
        && (route.Storage.Fluid < 0 || route.Storage.Fluid == id);
    private float GetFluidOutputConnectionAvailableLiters(FluidOutputConnection route, int id) => route.Storage.AvailableFluidStorageLiters;
    private float GetFluidOutputConnectionFillRatio(FluidOutputConnection route, int id) => route.Storage.Stored / route.Storage.Capacity;
    private bool TrySelectFluidOutputConnectionWithAnySpaceFromCache(int id, out FluidOutputConnection route)
    {
        foreach (var candidate in cachedFluidOutputConnections)
            if (CanUseFluidOutputConnectionWithAnySpace(candidate, id)) { route = candidate; return true; }
        route = default; return false;
    }
    private void RecordFluidNetworkOutput(int id, float amount) => RecordedOutput += amount;
    public bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int coordinate) => false;
    public bool TryAddDedicatedFluidAtRuntimeCoordinate(Vector2Int coordinate, int id, float liters, float temperature, out float accepted) =>
        TryAddFluidLiters(id, liters, temperature, out accepted);
    public void Connect(InstallationObject tank, int distance = 0, Pump pump = null)
    {
        cachedFluidOutputConnections.Add(new FluidOutputConnection(tank, default, distance, pump));
        fluidTopologyVersion++;
    }
}
public static class CraftingTreeRuntime
{
    public readonly record struct IngredientEntry(int itemId, float amount)
    { public int count => Math.Max(1, (int)Math.Ceiling(amount)); }
    public static readonly Dictionary<int,float> Counts = new();
    public static bool TryGetIngredientsView(int id, out IReadOnlyList<IngredientEntry> inputs)
    { inputs = null; return Counts.ContainsKey(id); }
    public static float GetOutputAmount(int id) => Counts.TryGetValue(id, out float amount) ? amount : 1;
    public static int GetOutputCount(int id) => Math.Max(1, (int)Math.Ceiling(GetOutputAmount(id)));
}
public partial class ProductionMachine : InputOutputModule
{
    private long productionFluidOutputUnits = -1;
    private float productionFluidOutputDeltaTime;
    private readonly Dictionary<int, long> productionFluidUnits = new();
    private readonly List<int> productionFluidSaveItemIds = new();
    private readonly List<Vector2Int> productionFluidInputCoordinates = new();
    private readonly List<CraftingTreeRuntime.IngredientEntry> resolvedProductionIngredients = new();
    public int Selected = 8;
    public ProductionMachine(float amount = 2f)
    {
        pair.outputs.Add(new(new ItemDefinition { id = 8 }, amount));
        pair.inputs.Add(new(new ItemDefinition { id = 9 }, 2f));
    }
    private void PullProductionFluidIngredients(float dt) { }
    private bool IsProductionTargetSelected(int id) => id == Selected;
    private bool TryResolveSelectedProductionRecipe(List<CraftingTreeRuntime.IngredientEntry> inputs, out int index, out int id, out int count)
    { index = 0; id = Selected; count = 1; return false; }
    private long GetProductionFluidUnits(int id) => productionFluidUnits.TryGetValue(id, out long units) ? units : 0;
    public bool HasRecipe = true;
    private bool TryResolveObjectInfoProductionIngredients(List<CraftingTreeRuntime.IngredientEntry> inputs,
        out int index, out int id, out int count)
    {
        inputs.Clear(); inputs.Add(new(9, 2)); index = 0;
        id = IsActiveCraftRunning ? ActiveOutputItemId : Selected; count = 1;
        return HasRecipe;
    }
    public bool TryGetObjectInfoProductionIngredientCount(out int count) { count = 1; return HasRecipe; }
    public bool TryGetObjectInfoProductionIngredient(int index, out int id, out int required, out int stored, out int capacity)
    { id = 9; required = 2; stored = 0; capacity = 2; return HasRecipe && index == 0; }
    public bool TryGetObjectInfoProductionOutput(out int id, out int stored, out int capacity)
    { id = IsActiveCraftRunning ? ActiveOutputItemId : Selected; stored = 0; capacity = 1; return HasRecipe; }
    public void SetInputLiters(float liters) => productionFluidUnits[9] = DeterministicSimulationUnits.FromFloat(liters);
    public void SetIdle() { IsActiveCraftRunning = IsWaitingForOutput = false; productionFluidOutputUnits = -1; }
    public void SetProcessing(float progress)
    {
        IsActiveCraftRunning = true; IsWaitingForOutput = false; ActiveOutputItemId = Selected;
        ObjectInfoWorkGaugeFillAmount = progress; productionFluidOutputUnits = -1;
    }
    public void SetOutputting() { IsActiveCraftRunning = IsWaitingForOutput = true; }
    public float RequiredInput(int outputId) => ResolveFluidIngredientRequiredLiters(outputId, 2f);
    private long GetRequiredProductionFluidUnits(int id, CraftingTreeRuntime.IngredientEntry input) => 1;
    public bool Pending => IsActiveCraftRunning;
    public float Remaining => DeterministicSimulationUnits.ToFloat(productionFluidOutputUnits);
    public bool KeepsTicking => ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy();
    public bool Allows(int id) => IsRecipeOutputAllowedByItemFilter(id);
    public int RecipeCount(int id) => ResolveProductionOutputCount(0, id);
    public void Tick(float dt = .1f) { plannedDeltaTime = dt; ApplyManagedUpdateTick(); }
    public void RepeatCompletion() => TryCompleteActiveCraft();
    public void SetSolid() { ActiveOutputItemId = 30; pair.outputs[0] = new(new ItemDefinition { id = 30, IsFluid = false }, 1); }
}
public partial class Pump : InputOutputModule
{
    public float PressureLitersPerSecond = 5f;
    private long pressureBudgetTick = -1;
    private double pressureBudgetLiters;
}
public static partial class SaveProbe
{
    private const int MaxSerializedListCount = 1000000;
    public static byte[] Write(InputOutputModule.PersistentState state)
    { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); WriteInputOutputState(writer, state); return stream.ToArray(); }
    public static InputOutputModule.PersistentState Read(byte[] bytes, int version)
    { using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream); return ReadInputOutputState(reader, version); }
}
public static class Checks
{
    private static int checks;
    private static void Eq(float actual, float expected, string scenario)
    { checks++; if (Math.Abs(actual - expected) > .00001f) throw new Exception($"{scenario}: {actual} != {expected}"); }
    private static void Require(bool condition, string scenario)
    { checks++; if (!condition) throw new Exception(scenario); }
    private static void Step(ProductionMachine machine, float dt = .1f)
    { MapObjectTickManager.CurrentSimulationTick += 6; machine.Tick(dt); }
    public static void Main()
    {
        var machine = new ProductionMachine();
        Step(machine);
        Eq(machine.Remaining, 2f, "disconnected output keeps completed batch");
        Require(machine.Pending && !machine.KeepsTicking, "disconnected batch waits for topology wake");
        var tank = new InstallationObject { Capacity = .15f };
        machine.Connect(tank);
        Step(machine);
        Eq(tank.Stored, .15f, "partially full tank accepts its remaining capacity");
        Eq(machine.Remaining, 1.85f, "partial acceptance debits only accepted fluid");
        Require(!machine.KeepsTicking, "full tank allows producer to sleep");
        var saved = machine.CapturePersistentState();
        var bytes = SaveProbe.Write(saved);
        Require(SaveProbe.CurrentVersion == 68, "remaining output has an explicit binary save version");
        var restored = new ProductionMachine();
        restored.ApplyPersistentState(SaveProbe.Read(bytes, SaveProbe.CurrentVersion));
        Eq(restored.Remaining, 1.85f, "binary round-trip preserves remaining output");
        Eq(DeterministicSimulationUnits.ToFloat(saved.Clone().productionOutputFluidUnits), 1.85f, "state clone preserves partial output");
        var cleared = saved.Clone(); cleared.ClearStoredEnergyAndProduction();
        Require(cleared.productionOutputFluidUnits == -1, "clear removes buffered output");
        var oldBytes = bytes[..^8];
        Require(SaveProbe.Read(oldBytes, 67).productionOutputFluidUnits == -1, "version 67 initializes a fresh output batch");
        tank.Capacity = 10f;
        restored.Connect(tank);
        Require(restored.KeepsTicking, "available tank keeps output running without craft energy");
        for (int i = 0; i < 10 && restored.Pending; i++) Step(restored);
        Eq(tank.Stored, 2f, "save reload and remaining delivery conserve one batch");
        Require(!restored.Pending, "completed drain finishes craft");
        var fractional = new ProductionMachine(.125f); var smallTank = new InstallationObject(); fractional.Connect(smallTank);
        for (int i = 0; i < 10; i++) Step(fractional);
        Eq(smallTank.Stored, .125f, "fractional output is never rounded to one liter");
        var distant = new ProductionMachine(); var distantTank = new InstallationObject(); distant.Connect(distantTank, 50);
        Step(distant); Eq(distantTank.Stored, .1f, "pipe pressure loss halves delivery rate");
        Eq(distant.Remaining + distantTank.Stored, 2f, "distance loss slows output without deleting inventory");
        var wrong = new ProductionMachine(); var wrongTank = new InstallationObject { Fluid = 9, Stored = 1 }; wrong.Connect(wrongTank);
        Step(wrong); Eq(wrong.Remaining, 2f, "foreign fluid does not destroy or mix output"); Eq(wrongTank.Stored, 1f, "foreign tank remains unchanged");
        wrongTank.Fluid = -1; wrongTank.Stored = 0;
        Step(wrong); Eq(wrongTank.Stored, .2f, "compatible receiver resumes output");
        var pump = new Pump(); var a = new ProductionMachine(12f); var b = new ProductionMachine(12f);
        var shared = new InstallationObject(); a.Connect(shared, pump: pump); b.Connect(shared, pump: pump);
        MapObjectTickManager.CurrentSimulationTick += 6; a.Tick(); b.Tick();
        Eq(shared.Stored, .5f, "producers share one Pump budget per simulation tick");
        Eq(a.Remaining + b.Remaining + shared.Stored, 24f, "shared Pump conserves both batches");
        a.RepeatCompletion(); Eq(shared.Stored, .5f, "completion retry cannot reuse the tick window");
        pump.PressureLitersPerSecond = 0f; Step(a); Eq(shared.Stored, .5f, "zero pressure stops output");
        pump.PressureLitersPerSecond = 2.5f; Step(a); Eq(shared.Stored, .75f, "edited Pump setting limits output");
        var multi = new ProductionMachine(); var first = new InstallationObject { Capacity = .1f }; var second = new InstallationObject();
        multi.Connect(first); multi.Connect(second); Step(multi);
        Eq(first.Stored + second.Stored, .2f, "common transport distributes output among tanks");
        var switched = new ProductionMachine(); var switchedTank = new InstallationObject(); switched.Connect(switchedTank); switched.Selected = 9;
        Require(switched.Allows(8) && !switched.Allows(9), "in-flight fluid keeps the completed recipe identity");
        Step(switched); Eq(switchedTank.Stored, .2f, "target changes do not reroute an old fluid batch");
        var stopped = new ProductionMachine(); var stoppedTank = new InstallationObject(); stopped.Connect(stoppedTank); Step(stopped, 0f);
        Eq(stoppedTank.Stored, 0f, "zero delta does not deliver fluid");
        stopped.PrepareForPool(); Require(stopped.CapturePersistentState().productionOutputFluidUnits == -1, "pool reset removes old output");
        var solid = new ProductionMachine(); solid.SetSolid(); Step(solid);
        Require(solid.SolidOutputCalls == 1 && !solid.Pending, "solid production continues through its original path");
        CraftingTreeRuntime.Counts[8] = 1;
        var timed = new ProductionMachine(2f) { CraftSeconds = 36f };
        Eq(timed.GetObjectInfoFluidPressureLitersPerSecond(8),1f,"binary Count 1 overrides stale prefab Count 2 pressure");
        timed.SetIdle();
        Eq(timed.GetObjectInfoFluidPressureLitersPerSecond(8),0f,"idle maker has no pipe pressure");
        timed.SetProcessing(.5f);
        Eq(timed.GetObjectInfoFluidPressureLitersPerSecond(8),0f,"Working maker has no pipe pressure");
        Require(timed.TryGetObjectInfoProductionFluidOutput(out _, out float configuredRate, out _, out _),"Working still exposes its configured output rate");
        Eq(configuredRate,1f,"production UI keeps recipe L/s while pipe pressure is zero");
        timed.SetOutputting();
        Eq(timed.GetObjectInfoFluidPressureLitersPerSecond(8),1f,"Outputting maker supplies pipe pressure");
        Eq(timed.GetObjectInfoFluidPressureLitersPerSecond(9),0f,"only active output fluid supplies pressure");
        timed.gameObject.activeInHierarchy=false;
        Eq(timed.GetObjectInfoFluidPressureLitersPerSecond(8),0f,"disabled maker supplies no pipe pressure");
        timed.gameObject.activeInHierarchy=true;
        Require(timed.RecipeCount(8) == 1,"active craft also uses authoritative output Count 1");
        Step(timed); Eq(timed.Remaining,36f,"Complete/Use duration generates 36 L at 1 L/s");
        var timedTank = new InstallationObject { Capacity = 10f }; timed.Connect(timedTank);
        for (int i=0;i<110;i++) Step(timed);
        Eq(timedTank.Stored,10f,"full receiver accepts only its free capacity");
        Eq(timed.Remaining,26f,"blocked output retains the entire undelivered timed batch");
        Require(timed.Pending,"next production stays blocked until all 36 L leave");
        Require(timed.OutputCapacityNotifications == 0,"partial drain cannot reopen upstream fluid reception");
        Require(timed.NextCraftStarts == 0,"base craft scheduling cannot start while output remains");
        var partialSave = SaveProbe.Read(SaveProbe.Write(timed.CapturePersistentState()),SaveProbe.CurrentVersion);
        var timedRestore = new ProductionMachine(2f) { CraftSeconds = 36f }; timedRestore.ApplyPersistentState(partialSave);
        timedTank.Capacity = 100f; timedRestore.Connect(timedTank);
        for (int i=0;i<300 && timedRestore.Pending;i++) Step(timedRestore);
        Eq(timedTank.Stored,36f,"save reload drains exactly the timed batch without regenerating it");
        Require(!timedRestore.Pending,"next craft may begin only after timed output drains");
        Eq(timedRestore.GetObjectInfoFluidPressureLitersPerSecond(8),0f,"drained batch stops pipe pressure");
        Require(timedRestore.NextCraftStarts == 1,"base tick starts the next craft after the last delivery");
        Require(timedRestore.OutputCapacityNotifications == 1,"last delivery wakes upstream senders to begin new reception");
        CraftingTreeRuntime.Counts[8] = .25f;
        var quarterRate = new ProductionMachine(2f) { CraftSeconds = 36f };
        var quarterRateTank = new InputOutputModule { Capacity = 100 };
        quarterRate.Connect(quarterRateTank);
        Eq(quarterRate.GetObjectInfoFluidPressureLitersPerSecond(8), .25f, "quarterRate canonical output pressure must not round to one");
        quarterRate.SetProcessing(.5f);
        Eq(quarterRate.GetObjectInfoFluidPressureLitersPerSecond(8),0f,"fractional Working maker also stops pipe pressure");
        Require(quarterRate.TryGetObjectInfoProductionFluidOutput(out _, out float rate, out float generated, out float batch), "quarterRate output query");
        Eq(rate, .25f, "quarterRate display rate");
        Eq(batch, 9f, "quarterRate rate times 36 seconds makes a 9 L batch");
        Eq(generated, 0f, "processing output stays unmaterialized until completion");
        Require(quarterRate.TryGetObjectInfoProductionFluidGauge(0, out var fractionalGauge), "fractional conversion gauge");
        Eq(fractionalGauge.currentLiters, 4.5f, "fractional batch gauge progress");
        quarterRate.SetOutputting();
        for (int i=0; i<1000 && quarterRate.Pending; i++) Step(quarterRate);
        Eq(quarterRateTank.Stored, 9f, "quarterRate output drains exactly its batch");
        Require(!quarterRate.Pending, "quarterRate output completes after draining");
        CraftingTreeRuntime.Counts.Clear();
        Console.WriteLine($"Production fluid output checks passed: {checks}");
        DisplayChecks.Run();
    }
}
