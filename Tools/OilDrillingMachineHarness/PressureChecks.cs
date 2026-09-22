using System;

public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
}

public class InputOutputModule
{
    public virtual float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId) => 0f;
}

public partial class OilDrillingMachine : InputOutputModule
{
    private bool isExtracting;
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

        Console.WriteLine($"Oil drilling pressure checks passed: {passed}");
    }
}
