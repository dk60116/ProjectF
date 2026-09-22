using System;
using ProjectF.FluidTransport;

internal static class Checks
{
    private static int passed;

    private static void Expect(FluidOutputRateMeter meter, int itemId, double time,
        float expected, string scenario)
    {
        float actual = meter.GetLitersPerSecond(itemId, time);
        if (Math.Abs(actual - expected) > 0.0001f)
        {
            throw new Exception($"{scenario}: fluid {itemId}, expected {expected}, got {actual}");
        }

        passed++;
    }

    private static void Main()
    {
        var meter = new FluidOutputRateMeter();

        // The refinery records three different outputs on the same production tick.
        meter.Record(113, 0.05f, 0.1);
        meter.Record(114, 0.5f, 0.1);
        meter.Record(115, 0.08f, 0.1);
        Expect(meter, 113, 0.1, 0.05f, "first refinery output");
        Expect(meter, 114, 0.1, 0.5f, "second refinery output");
        Expect(meter, 115, 0.1, 0.08f, "third refinery output");

        meter.Record(113, 0.05f, 0.2);
        meter.Record(114, 0.5f, 0.2);
        meter.Record(115, 0.08f, 0.2);
        Expect(meter, 113, 0.2, 0.1f, "first output accumulates independently");
        Expect(meter, 114, 0.2, 1f, "second output accumulates independently");
        Expect(meter, 115, 0.2, 0.16f, "third output accumulates independently");
        Expect(meter, 116, 0.2, 0f, "unrecorded fluid stays empty");

        Expect(meter, 113, 1.3, 0f, "stopped output expires");
        Expect(meter, 114, 1.3, 0f, "other stopped output expires");
        meter.Record(113, 1f, 1.4);
        meter.Record(114, 2f, 1.4);
        meter.Reset();
        Expect(meter, 113, 1.4, 0f, "pool reset clears first fluid");
        Expect(meter, 114, 1.4, 0f, "pool reset clears second fluid");

        meter.Record(113, 1f, 2.0);
        meter.Record(114, 2f, 2.0);
        Expect(meter, 113, 0.0, 0f, "clock rewind clears first fluid");
        Expect(meter, 114, 0.0, 0f, "clock rewind clears second fluid");
        Console.WriteLine($"Fluid output rate checks passed: {passed}");
    }
}
