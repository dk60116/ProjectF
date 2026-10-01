using System;
using System.Collections.Generic;
using ProjectF.Simulation;

// These are boundary test doubles, not an implementation of scene IO/topology.
public struct Vector3 { }
public readonly record struct Vector2Int(int x, int y);
public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
}
public sealed class ItemDefinition
{
    public bool Powered = true;
    public float Duration = 1, CompleteEnergy = 60, Rate = 60;
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition item) => item.Rate;
}
public partial class CraftAdapterProbe
{
    public ItemDefinition Definition = new();
    public PowerSupplySnapshot Power = new() { HasPowerSource = true, ProductionWatts = 30, RequiredWatts = 60 };
    public bool OutputBlocked;
    public int EnergyRequests, OutputAttempts, Produced, WakeCount, LastOutputItem = -1;
    public long GrantedEnergy;
    private float lastOperationalEnergySupplyRatio;
    private long storedEnergyUnits, energyGaugeCapacityUnits;
    private readonly long[] secondaryStoredEnergyUnitsByType = new long[6];
    private readonly long[] secondaryEnergyGaugeCapacityUnitsByType = new long[6];
    private object cachedTerrain, cachedBlockStateStore;
    private readonly List<Vector2Int> runtimeInputEnergyCoordinates = new(), runtimeOutputCoordinates = new(),
        runtimePipeInputCoordinates = new(), runtimeGridCoordinates = new(), runtimeFocusCoordinates = new();
    private readonly record struct RuntimeInputItemArea(Vector2Int coordinate, int itemId);
    private readonly List<RuntimeInputItemArea> runtimeInputItemAreas = new();
    public ProductionProcess State => production;
    public void Tick(float dt) => UpdateActiveCraft(dt);
    public void Start(int output = 12, int count = 2) => BeginActiveCraft(3, output, count, Definition);
    public void Clear() => ClearActiveCraft();
    public long RemainingTimeTicks => ResolveRemainingCraftTicks();
    private static void AddUniqueCoordinates(IReadOnlyList<Vector2Int> source, List<Vector2Int> target)
    { if (source == null) return; foreach (var coordinate in source) if (!target.Contains(coordinate)) target.Add(coordinate); }
    private bool ContainsRuntimeInputItemArea(Vector2Int coordinate, int item) => false;
    private void UnregisterRuntimeFluidSpatialCoordinates() { }
    private void UnregisterRuntimeAreaCoordinates() { }
    private void ExpandRuntimeInputItemAreasForAdditionalItemIds() { }
    private void RegisterRuntimeAreaCoordinates() { }
    private void ConfigureRuntimeGridCoordinates(List<Vector2Int> coordinates) { }
    private void MarkManagedRuntimeVisualsDirty() { }
    private ItemDefinition ResolveInstalledDefinition() => Definition;
    private bool RequiresOperationalEnergy(ItemDefinition definition) => definition.Powered;
    private float ResolveCompleteEnergy(ItemDefinition definition) => definition.CompleteEnergy;
    private float ResolveInitialCraftDuration(ItemDefinition definition) => definition.Duration;
    private void WakeRuntimeUpdate() => WakeCount++;
    private Vector3 ResolveConsumeTargetWorldPosition() => default;
    private bool TryConsumeOperatingEnergy(float dt, out float consumed)
    {
        EnergyRequests++;
        long units = Power.GrantEnergy(DeterministicSimulationUnits.FromFloat(dt * Definition.Rate), Definition.Rate);
        GrantedEnergy += units;
        consumed = DeterministicSimulationUnits.ToFloat(units);
        return units > 0;
    }
    private bool TryEmitOutputItems(int itemId, int count, Vector3 origin)
    {
        OutputAttempts++;
        if (OutputBlocked) return false;
        LastOutputItem = itemId; Produced += count;
        return true;
    }
}
internal static class ProductionAdapterChecks
{
    private static int passed;
    private static void Require(bool result, string name)
    { if (!result) throw new Exception(name); passed++; }
    public static void Main()
    {
        var machine = new CraftAdapterProbe();
        machine.Tick(1f / 60);
        Require(machine.EnergyRequests == 0 && machine.OutputAttempts == 0, "idle adapter has no side effects");
        machine.Start();
        Require(machine.State.Active && machine.State.RecipeIndex == 3 && machine.WakeCount == 1,
            "begin delegates to one authoritative process and wakes the existing scheduler");
        for (int i = 0; i < 60; i++) machine.Tick(1f / 60);
        Require(machine.State.RemainingTicks == 0 && machine.RemainingTimeTicks == 30 && machine.Produced == 0
            && machine.GrantedEnergy == 30 * DeterministicSimulationUnits.UnitsPerWhole,
            "real update adapter retains half-powered progress and quantized cost");
        var checkpointSave = machine.CapturePersistentState();
        Require(checkpointSave.remainingCraftTicks == 30 && checkpointSave.remainingCraftTime == .5f
                && checkpointSave.activeCraftConsumedEnergyUnits == machine.State.ConsumedEnergyUnits,
            "actual save capture derives remaining time from energy without changing the runtime clock");
        Require(machine.State.RemainingTicks == 0, "save capture does not mutate energy production state");
        var checkpoint = machine.State;
        machine.Power.HasPowerSource = false;
        machine.Tick(1f / 60);
        Require(machine.State.Equals(checkpoint), "energy outage keeps the active recipe unchanged");
        machine.Power.HasPowerSource = true; machine.OutputBlocked = true;
        var resumed = new CraftAdapterProbe { OutputBlocked = true }; resumed.ApplyPersistentState(checkpointSave);
        Require(resumed.State.Equals(checkpoint), "actual save restore keeps the consolidated energy state");
        var legacy = new CraftAdapterProbe();
        checkpointSave.activeCraftConsumedEnergyUnits = 0;
        legacy.ApplyPersistentState(checkpointSave);
        Require(legacy.State.Equals(checkpoint), "old remaining-time-only save restores accumulated energy once");
        var legacyFloat = checkpointSave.Clone();
        legacyFloat.hasDeterministicUnits = false;
        legacyFloat.activeCraftConsumedEnergy = 0;
        legacy.ApplyPersistentState(legacyFloat);
        Require(legacy.State.Equals(checkpoint), "old float time save restores the same energy progress");
        for (int i = 0; i < 60; i++)
        {
            machine.Tick(1f / 60); resumed.Tick(1f / 60);
            Require(machine.State.Equals(resumed.State), "restored adapter progresses exactly like uninterrupted production");
        }
        Require(machine.State.WaitingForOutput && machine.OutputAttempts == 1 && machine.Produced == 0,
            "completion waits for the existing output port without dropping the batch");
        var blockedSave = machine.CapturePersistentState();
        Require(blockedSave.remainingCraftTicks == 0 && blockedSave.remainingCraftTime == 0,
            "completed blocked batch saves zero remaining time");
        var blockedRestore = new CraftAdapterProbe { OutputBlocked = true };
        blockedRestore.ApplyPersistentState(blockedSave);
        int restoredRequests = blockedRestore.EnergyRequests;
        blockedRestore.Tick(1);
        Require(blockedRestore.State.WaitingForOutput && blockedRestore.EnergyRequests == restoredRequests,
            "restored output wait cannot restart energy production");
        int requests = machine.EnergyRequests;
        for (int i = 0; i < 10; i++) machine.Tick(1f / 60);
        Require(machine.EnergyRequests == requests && machine.OutputAttempts == 11,
            "blocked output retries do not charge energy");
        machine.OutputBlocked = false; machine.Tick(1f / 60);
        Require(!machine.State.Active && machine.Produced == 2 && machine.LastOutputItem == 12,
            "successful output clears the process after emitting exactly one batch");
        machine.Tick(1); Require(machine.Produced == 2 && machine.EnergyRequests == requests,
            "subsequent idle update cannot duplicate output");
        machine.Definition.Powered = false; machine.Start();
        for (int i = 0; i < 30; i++) machine.Tick(1f / 60);
        var timedSave = machine.CapturePersistentState();
        Require(timedSave.remainingCraftTicks == 30 && timedSave.remainingCraftTime == .5f,
            "time-only recipe persists its countdown");
        var timedRestore = new CraftAdapterProbe();
        timedRestore.Definition.Powered = false;
        timedRestore.ApplyPersistentState(timedSave);
        for (int i = 0; i < 30; i++) { machine.Tick(1f / 60); timedRestore.Tick(1f / 60); }
        Require(machine.Produced == 4 && machine.EnergyRequests == requests,
            "unpowered recipe uses elapsed ticks instead of energy IO");
        Require(timedRestore.Produced == 2 && timedRestore.EnergyRequests == 0,
            "time-only save resumes at the exact completion tick");
        machine.Start(); machine.Start(-1);
        Require(!machine.State.Active && machine.State.OutputItemId == -1, "invalid replacement recipe clears active work");
        var fractional = new CraftAdapterProbe();
        fractional.Definition.Rate = 7.5f;
        fractional.Definition.CompleteEnergy = 18.75f;
        fractional.Tick(.1f); // idle has no snapshot work
        fractional.Start(); fractional.Tick(.1f);
        var fractionalSave = fractional.CapturePersistentState();
        Require(fractionalSave.remainingCraftTicks == 147,
            "fractional energy snapshot preserves rounded nominal remaining ticks");
        var fractionalRestore = new CraftAdapterProbe { Definition = fractional.Definition };
        fractionalRestore.ApplyPersistentState(fractionalSave);
        for (int i = 0; i < 49; i++)
        {
            fractional.Tick(.1f); fractionalRestore.Tick(.1f);
            Require(fractional.State.Equals(fractionalRestore.State),
                "fractional energy save resumes without countdown drift");
        }
        Require(fractional.Produced == 2 && fractionalRestore.Produced == 2,
            "fractional energy completion matches across save restore");
        Console.WriteLine($"PASS {passed} production adapter checks (extracted production methods, IO test doubles)");
    }
}
