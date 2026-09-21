using System;
using ProjectF.Simulation;

// These are boundary test doubles, not an implementation of scene IO/topology.
public struct Vector3 { }
public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
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
    public ProductionProcess State => production;
    public void Tick(float dt) => UpdateActiveCraft(dt);
    public void Start(int output = 12, int count = 2) => BeginActiveCraft(3, output, count, Definition);
    public void Clear() => ClearActiveCraft();
    // Exercise the actual compatibility accessors used by PersistentState restore.
    // This does not simulate a binary save/load round trip.
    public void RestoreFields(ProductionProcess state)
    {
        hasActiveCraft = state.Active; waitingForOutput = state.WaitingForOutput;
        remainingCraftTicks = state.RemainingTicks; activeCraftConsumedEnergyUnits = state.ConsumedEnergyUnits;
        activeRecipeIndex = state.RecipeIndex; activeOutputItemId = state.OutputItemId; activeOutputCount = state.OutputCount;
    }
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
        Require(machine.State.RemainingTicks == 30 && machine.Produced == 0
            && machine.GrantedEnergy == 30 * DeterministicSimulationUnits.UnitsPerWhole,
            "real update adapter retains half-powered progress and quantized cost");
        var checkpoint = machine.State;
        machine.Power.HasPowerSource = false;
        machine.Tick(1f / 60);
        Require(machine.State.Equals(checkpoint), "energy outage keeps the active recipe unchanged");
        machine.Power.HasPowerSource = true; machine.OutputBlocked = true;
        var resumed = new CraftAdapterProbe { OutputBlocked = true }; resumed.RestoreFields(checkpoint);
        Require(resumed.State.Equals(checkpoint), "legacy field accessors restore the consolidated state");
        for (int i = 0; i < 60; i++)
        {
            machine.Tick(1f / 60); resumed.Tick(1f / 60);
            Require(machine.State.Equals(resumed.State), "restored adapter progresses exactly like uninterrupted production");
        }
        Require(machine.State.WaitingForOutput && machine.OutputAttempts == 1 && machine.Produced == 0,
            "completion waits for the existing output port without dropping the batch");
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
        for (int i = 0; i < 60; i++) machine.Tick(1f / 60);
        Require(machine.Produced == 4 && machine.EnergyRequests == requests,
            "unpowered recipe uses elapsed ticks instead of energy IO");
        machine.Start(); machine.Start(-1);
        Require(!machine.State.Active && machine.State.OutputItemId == -1, "invalid replacement recipe clears active work");
        Console.WriteLine($"PASS {passed} production adapter checks (extracted production methods, IO test doubles)");
    }
}
