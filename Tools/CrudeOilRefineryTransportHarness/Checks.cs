internal static class Checks
{
    private static int passed;
    private static void Expect(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > .0001f)
            throw new Exception($"{label}: expected {expected}, got {actual}");
        passed++;
    }
    private static void Require(bool condition, string label) => Expect(condition ? 1f : 0f, 1f, label);
    private static readonly Vector2Int Crude = new(-1, 0), Water = new(-2, 0);
    private static CrudeOilRefinery Scenario()
    {
        var refinery = new CrudeOilRefinery();
        refinery.AddInput(Crude, 10, 10f);
        refinery.AddInput(Water, 11, 2f);
        for (int i = 0; i < 3; i++) refinery.AddOutput(new(i, 0), i + 1, 0, i == 2 ? 2f : 1f);
        return refinery;
    }
    private static float Add(CrudeOilRefinery refinery, Vector2Int coordinate, int id, float liters, float temperature = 20f)
    {
        refinery.TryAddDedicatedFluidAtRuntimeCoordinate(coordinate, id, liters, temperature, out float accepted);
        return accepted;
    }
    private static void Fill(CrudeOilRefinery refinery)
    {
        Expect(Add(refinery, Crude, 10, 50f, 80f), 50f, "full crude batch accepted");
        Expect(Add(refinery, Water, 11, 10f, 20f), 10f, "full water batch accepted");
    }
    private static float Remaining(CrudeOilRefinery refinery, int index)
    {
        Require(refinery.TryGetObjectInfoOutput(index, out _, out _, out _, out float remaining, out _), "output exists");
        return remaining;
    }
    private static void Refine(CrudeOilRefinery refinery)
    {
        Fill(refinery);
        refinery.Tick();
        Require(refinery.Active && !refinery.Outputting, "filled batch starts refining");
        for (int i = 0; i < 5; i++) refinery.Tick();
        Require(refinery.Outputting, "nominal five second processing completes");
    }
    private static void CheckBatchLifecycle()
    {
        var refinery = Scenario();
        Expect(refinery.GetObjectInfoRequiredInputLiters(0), 50f, "capacity uses rate times duration, not 30 L tank cap");
        Expect(refinery.GetObjectInfoRequiredInputLiters(1), 10f, "second input has own batch capacity");
        Expect(Add(refinery, Crude, 10, 80f), 50f, "oversupply capped at batch size");
        Expect(Add(refinery, Water, 10, 1f), 0f, "typed input rejects other fluid");
        refinery.Tick();
        Require(!refinery.Active && refinery.Status.StartsWith("Waiting"), "missing water prevents start");
        Expect(refinery.EnergyConsumed, 1f, "waiting input consumes full use rate once");
        refinery.TryGetObjectInfoInput(0, out _, out _, out _, out float crude);
        Expect(crude, 50f, "one full input is not consumed while another is short");
        Add(refinery, Water, 11, 9.999f);
        refinery.Tick();
        Require(!refinery.Active, "even a fractional input deficit prevents start");
        Add(refinery, Water, 11, .001f);
        refinery.Tick();
        Require(refinery.Active, "all input quantities required to start");
        refinery.TryGetObjectInfoInput(0, out _, out _, out _, out crude);
        Expect(crude, 0f, "inputs consumed once at start");
        Expect(Add(refinery, Crude, 10, 1f), 0f, "direct source cannot refill during processing");
        Expect(refinery.GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(Crude), 0f, "processing advertises no free intake");
        Require(!refinery.CanAcceptFluidItem(10), "shared storage cannot bypass typed batch intake");
        refinery.Connected[10] = 100f;
        refinery.Connected[11] = 100f;
        for (int i = 0; i < 5; i++)
        {
            Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(1), 0f, "processing has no output pressure");
            refinery.Tick();
            Expect(refinery.Connected[10], 100f, "processing never takes live crude supply");
        }
        Expect(Remaining(refinery, 0), 5f, "first full output batch materialized once");
        Expect(Remaining(refinery, 2), 10f, "rate two yields ten liters, independent of pressure");
        Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(3), 2f, "outputting exposes its own configured rate");
        for (int i = 0; i < 5; i++) refinery.Tick();
        Require(!refinery.Active, "all outputs drained before returning to intake");
        Expect(refinery.Emitted[1], 5f, "first output conserved");
        Expect(refinery.Emitted[3], 10f, "third output conserved");
        Expect(refinery.Connected[10], 100f, "drain finishing tick cannot also take next input");
        refinery.Tick();
        Expect(refinery.Connected[10], 90f, "next tick resumes intake at configured rate");
        Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(1), 0f, "collecting has no output pressure");
    }
    private static void CheckBlockedAndPartialOutput()
    {
        for (int blocked = 1; blocked <= 3; blocked++)
        {
            var refinery = Scenario();
            refinery.Capacity[blocked] = 0f;
            Refine(refinery);
            for (int i = 0; i < 5; i++) refinery.Tick();
            Require(refinery.Active && refinery.Outputting, "one blocked byproduct keeps batch pending");
            Expect(Remaining(refinery, blocked - 1), blocked == 3 ? 10f : 5f, "blocked product is retained");
            Expect(Add(refinery, Crude, 10, 50f), 0f, "no intake while any byproduct remains");
            refinery.Tick();
            Require(refinery.Status == "Waiting for output", "all remaining blocked products show warning");
            refinery.Capacity[blocked] = 100f;
            for (int i = 0; i < 5; i++) refinery.Tick();
            Require(!refinery.Active, "unblocked remaining product eventually drains");
            for (int id = 1; id <= 3; id++) Expect(refinery.Emitted[id], id == 3 ? 10f : 5f, "no output is discarded or recreated");
        }
        var partial = Scenario();
        Refine(partial);
        partial.Capacity[1] = .2f;
        partial.Tick();
        Expect(Remaining(partial, 0), 4.8f, "debit actual accepted volume, not requested volume");
        Require(partial.Status == "Outputting", "actual partial delivery shows green output status");
        partial.Capacity[1] = 100f;
        for (int i = 0; i < 5; i++) partial.Tick();
        Expect(partial.Emitted[1], 5f, "rejected fraction is delivered later");
        Expect(partial.Temperatures[1], 70f, "weighted input temperature accompanies outputs");
        var disconnected = Scenario();
        disconnected.Disconnect(new(0, 0));
        Refine(disconnected);
        for (int i = 0; i < 5; i++) disconnected.Tick();
        Expect(Remaining(disconnected, 0), 5f, "unconnected output retained");
    }
    private static void CheckEnergyAndRestore()
    {
        var refinery = Scenario();
        refinery.EnergyRatio = 0f;
        Fill(refinery);
        refinery.Tick();
        Require(!refinery.Active, "no energy cannot start or consume filled inputs");
        Expect(refinery.EnergyConsumed, 0f, "no supply consumes no power");
        refinery.EnergyRatio = .5f;
        refinery.Tick();
        for (int i = 0; i < 5; i++) refinery.Tick();
        Expect(refinery.ObjectInfoProcessingRatio, .5f, "partial energy slows progress without shrinking batch");
        refinery.EnergyRatio = 0f;
        refinery.Tick();
        Expect(refinery.ObjectInfoProcessingRatio, .5f, "mid-process energy outage preserves progress");
        Expect(Add(refinery, Crude, 10, 1f), 0f, "energy outage does not reopen processing intake");
        refinery.EnergyRatio = .5f;
        var halfway = refinery.CapturePersistentState();
        refinery = Scenario();
        refinery.EnergyRatio = .5f;
        refinery.ApplyPersistentState(halfway);
        for (int i = 0; i < 5; i++) refinery.Tick();
        Require(refinery.Outputting, "mid-process restore preserves consumed energy");
        Expect(Remaining(refinery, 0), 5f, "slow processing still produces nominal batch quantity");
        refinery.EnergyRatio = 0f;
        refinery.Tick();
        Expect(Remaining(refinery, 0), 5f, "energy outage retains completed output");
        Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(1), 0f, "no energy means no active output pressure");
        refinery.EnergyRatio = 1f;
        refinery.Tick();
        var saved = refinery.CapturePersistentState();
        var restored = Scenario();
        restored.ApplyPersistentState(saved);
        restored.Tick();
        Expect(Remaining(restored, 0), 3f, "mid-output restore continues remaining volume");
        Expect(saved.refineryOutputs[0].remainingUnits, DeterministicSimulationUnits.FromFloat(4f), "restoring does not mutate saved snapshot");
        for (int i = 0; i < 3; i++) restored.Tick();
        Require(!restored.Active, "restored outputs complete without rebuilding full batch");
        Expect(restored.Emitted[1], 4f, "only saved remainder delivered after load");
        var collecting = Scenario();
        Add(collecting, Crude, 10, 12.5f);
        saved = collecting.CapturePersistentState();
        restored = Scenario();
        restored.ApplyPersistentState(saved);
        restored.TryGetObjectInfoInput(0, out _, out _, out _, out float input);
        Expect(input, 12.5f, "partial input restored");
        restored.PortsValid = false;
        restored.Tick();
        Require(!restored.TryGetElectricPowerDemand(out _), "invalid ports do not request power");
        Expect(restored.EnergyConsumed, 0f, "invalid ports consume no energy");
        var unpowered = Scenario();
        unpowered.RequiresEnergy = false;
        Refine(unpowered);
        Expect(Remaining(unpowered, 2), 10f, "time-based process also produces nominal output");
    }
    private static void CheckFractionalBatch()
    {
        var refinery = new CrudeOilRefinery { Duration = 2.5f };
        refinery.AddInput(Crude, 10, .75f);
        refinery.AddInput(Water, 11, .125f);
        refinery.AddOutput(new(0, 0), 1, 0, .35f);
        Expect(Add(refinery, Crude, 10, 1.875f), 1.875f, "fractional input batch accepted");
        Expect(Add(refinery, Water, 11, .3125f), .3125f, "fractional secondary input accepted");
        refinery.Tick(.1f);
        for (int i = 0; i < 25; i++) refinery.Tick(.1f);
        Require(refinery.Outputting, "fractional duration completes through shared energy process");
        Expect(Remaining(refinery, 0), .875f, "fractional output rate multiplied by nominal duration");
        refinery.Capacity[1] = .013f;
        int attempts = 0;
        while (refinery.Active && attempts++ < 100) refinery.Tick(.1f);
        Require(!refinery.Active, "small partial receipts eventually clear final fractional remainder");
        Expect(refinery.Emitted[1], .875f, "fractional volume is conserved across repeated partial acceptance");
    }
    private static void Main()
    {
        CheckIntakeRoundingBoundary();
        CheckBatchLifecycle();
        CheckBlockedAndPartialOutput();
        CheckEnergyAndRestore();
        CheckFractionalBatch();
        var module = new InputOutputModule();
        var near = new Vector2Int(0, 0);
        var far = new Vector2Int(1, 0);
        module.AddConnection(near, 0, 1);
        module.AddConnection(far, 50, 2);
        Expect(module.Retention(near, 1, 2f), 1f, "zero distance retains source rate");
        Expect(module.Retention(far, 2, 2f), .5f, "pipe loss remains per output route");
        Expect(module.Retention(near, 2, 2f), 0f, "different fluid cannot use another port");
        var pump = new Pump();
        module.AddConnection(new(2, 0), 0, 1, true, pump);
        Expect(module.Retention(new(2, 0), 1, 20f) * 20f, 5f, "pump caps fast output at pump rate");
        pump.PressureLitersPerSecond = 0f;
        Expect(module.Retention(new(2, 0), 1, 20f), 0f, "zero pressure pump blocks delivery");
        Console.WriteLine($"Crude refinery batch/transport checks passed: {passed}");
    }

    private static void CheckIntakeRoundingBoundary()
    {
        foreach (long shortfall in new[] { 1L, 3000L, 6000L })
        {
            var refinery = Scenario();
            var state = refinery.CapturePersistentState();
            state.refineryInputFluidItemIds.AddRange(new[] { 10, 11 });
            state.refineryInputFluidUnits.AddRange(new[]
            {
                DeterministicSimulationUnits.FromFloat(50f) - shortfall,
                DeterministicSimulationUnits.FromFloat(10f)
            });
            state.refineryInputFluidTemperatures.AddRange(new[] { 80f, 20f });
            refinery.ApplyPersistentState(state);
            // A live source remains available, but current transport ignores the tiny tail.
            refinery.Connected[10] = 100f;
            refinery.Tick(.1f);
            Require(refinery.Active, $"intake starts despite unreachable transport tail of {shortfall} units");
            Require(refinery.CapturePersistentState().refineryInputFluidUnits.TrueForAll(units => units == 0L),
                "terminal input rounding cannot leave negative inventory");
            Expect(refinery.Connected[10], 100f, "sub-threshold tail must not withdraw and discard tank fluid");
        }
        foreach (long shortfall in new[] { 6001L, 12000L, 60000L })
        {
            var refinery = Scenario();
            var state = refinery.CapturePersistentState();
            state.refineryInputFluidItemIds.AddRange(new[] { 10, 11 });
            state.refineryInputFluidUnits.AddRange(new[]
            {
                DeterministicSimulationUnits.FromFloat(50f) - shortfall,
                DeterministicSimulationUnits.FromFloat(10f)
            });
            state.refineryInputFluidTemperatures.AddRange(new[] { 80f, 20f });
            refinery.ApplyPersistentState(state);
            refinery.Tick(.1f);
            Require(!refinery.Active, "a deliverable input deficit must still wait for supply");
            Require(refinery.CapturePersistentState().refineryInputFluidUnits[0]
                == DeterministicSimulationUnits.FromFloat(50f) - shortfall,
                "waiting cannot manufacture the missing ingredient");
            refinery.Connected[10] = 100f;
            refinery.Tick(.1f);
            Require(refinery.Active, "a deliverable near-full tail is collected in fixed units before start");
            Require(refinery.Connected[10] < 100f, "finishing intake debits the connected source");
        }
    }
}
