using System;

public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
}

public class InputOutputModule
{
    public sealed class PersistentState
    {
        public long oilDrillingProgressUnits, productionOutputFluidUnits;
        public float oilDrillingProgressLiters;
        public long ResolveOilDrillingProgressUnits() => oilDrillingProgressUnits;
    }
    public virtual PersistentState CapturePersistentState() => new();
    public virtual void ApplyPersistentState(PersistentState state) { }
    public virtual float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId) => 0f;
}

public partial class OilDrillingMachine : InputOutputModule
{
    private bool isExtracting;
    private long productionProgressUnits, pendingOilOutputUnits;
    public float AcceptanceLimit = 1, Delivered;
    public bool IsBenchmarkWorking;
    public bool isActiveAndEnabled = true;
    public int OutputItemId = 4;
    public float OutputLitersPerSecond = 2f;
    public float EnergySupplyRatio = 1f;
    protected float OperationalAnimationSpeedRatio => EnergySupplyRatio;

    public bool TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond)
    {
        outputItemId = OutputItemId;
        litersPerSecond = OutputLitersPerSecond;
        return outputItemId >= 0;
    }

    public void SetExtracting(bool extracting) => isExtracting = extracting;
    private int ResolveOilItemId() => OutputItemId;
    private float GetStoredFluidTemperatureCelsius(int item) => 15;
    private bool TryEmitFluidOutputToConnectedStorages(int item, float requested, float temperature, out float accepted)
    { accepted = Math.Min(AcceptanceLimit, requested); Delivered += accepted; return accepted > 0; }
    public void FlushForProbe() => FlushPendingOil();

    // PRODUCTION_PRESSURE
}

public static class PressureChecks
{
    private static int passed;

    private static void Expect(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.0001f)
        {
            throw new InvalidOperationException(
                $"{label}: {actual} L/s, expected {expected} L/s");
        }

        passed++;
    }

    public static void Main()
    {
        var machine = new OilDrillingMachine();
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(4), 0f,
            "idle machine");

        machine.SetExtracting(true);
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(4), 2f,
            "working machine");

        machine.EnergySupplyRatio = 0.25f;
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(4), 0.5f,
            "partial energy");
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(1), 0f,
            "different fluid");

        machine.isActiveAndEnabled = false;
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(4), 0f,
            "disabled machine");

        machine.isActiveAndEnabled = true;
        machine.SetExtracting(false);
        machine.EnergySupplyRatio = 1f;
        machine.IsBenchmarkWorking = true;
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(4), 2f,
            "forced drill without a real oil deposit");
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(1), 0f,
            "forced drill rejects a different fluid");
        machine.IsBenchmarkWorking = false;
        Expect(machine.GetObjectInfoFluidPressureLitersPerSecond(4), 0f,
            "force off restores idle pressure");

        machine.ApplyPersistentState(new InputOutputModule.PersistentState { oilDrillingProgressUnits = DeterministicSimulationUnits.FromFloat(.4f), productionOutputFluidUnits = DeterministicSimulationUnits.FromFloat(.7f) });
        var state = machine.CapturePersistentState();
        Expect(DeterministicSimulationUnits.ToFloat(state.oilDrillingProgressUnits), .4f, "native editor proxy preserves ECS progress");
        Expect(DeterministicSimulationUnits.ToFloat(state.productionOutputFluidUnits), .7f, "native editor proxy preserves ECS pending output");
        machine.AcceptanceLimit = .2f; machine.FlushForProbe();
        Expect(DeterministicSimulationUnits.ToFloat(machine.CapturePersistentState().productionOutputFluidUnits), .5f, "partial native transfer retains remaining oil");
        machine.AcceptanceLimit = 0; machine.FlushForProbe();
        Expect(DeterministicSimulationUnits.ToFloat(machine.CapturePersistentState().productionOutputFluidUnits), .5f, "blocked native transfer preserves oil");
        machine.AcceptanceLimit = 1; machine.FlushForProbe();
        Expect(DeterministicSimulationUnits.ToFloat(machine.CapturePersistentState().productionOutputFluidUnits), 0, "native reserve eventually drains");
        Expect(machine.Delivered, .7f, "native proxy transfer conserves harvested oil");

        Console.WriteLine($"Oil drilling pressure checks passed: {passed}");
    }
}

namespace ProjectF.Simulation
{
    public static class SimulationTickWorld
    { public const int DefaultSimulationTicksPerSecond = 60; public const float FixedSimulationDeltaSeconds = 1f / 60; }
}
