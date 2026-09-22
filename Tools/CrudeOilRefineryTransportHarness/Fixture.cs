using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int operator +(Vector2Int left, Vector2Int right) =>
            new(left.x + right.x, left.y + right.y);
        public static Vector2Int operator -(Vector2Int value) => new(-value.x, -value.y);
    }
    public static class Application { public static bool isPlaying = true; }
    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
    }
}

public class ItemDefinition
{
    public int id;
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition definition) => 1f;
}
public static class MapClimate { public static float CurrentTemperatureCelsius => 20f; }
public static class MapObjectTickManager
{
    public static double CurrentSimulationTimeSeconds;
}
public class InstallationObject
{
    protected const float FluidPressureLossPerPipe = 0.01f;
    // INSTALLATION_RETENTION
}

public class Pump
{
    public float PressureLitersPerSecond = 5f;
    // PUMP_LIMIT
}

public class InputOutputModule : InstallationObject
{
    private static readonly Dictionary<(Vector2Int, Vector2Int), InputOutputModule> directSources = new();
    public float MockPressure;
    public static void RegisterDirectSource(
        Vector2Int coordinate, Vector2Int direction, InputOutputModule source) =>
        directSources[(coordinate, direction)] = source;
    public static void ClearDirectSources() => directSources.Clear();
    public static void AppendFluidOutputSourcesAtCoordinate(
        Vector2Int coordinate, Vector2Int direction, ISet<InputOutputModule> sources)
    {
        if (directSources.TryGetValue((coordinate, direction), out InputOutputModule source))
            sources.Add(source);
    }
    protected bool TryGetRuntimePipeAreaExternalDirection(Vector2Int coordinate,
        out Vector2Int direction)
    {
        direction = new Vector2Int(1, 0);
        return true;
    }
    protected readonly struct FluidOutputConnection
    {
        public readonly int PipeDistance;
        public readonly Pump PressurePump;
        public readonly int FluidItemId;
        public readonly bool HasSpace;

        public FluidOutputConnection(int pipeDistance, int fluidItemId, bool hasSpace, Pump pump)
        {
            PressurePump = pump;
            PipeDistance = pipeDistance;
            FluidItemId = fluidItemId;
            HasSpace = hasSpace;
        }
    }

    protected sealed class FluidPortConnectionCache
    {
        public readonly List<FluidOutputConnection> Connections = new();
    }

    protected readonly Dictionary<Vector2Int, FluidPortConnectionCache> fluidOutputPortConnectionCaches = new();

    protected FluidPortConnectionCache GetFluidPortConnectionCache(
        Dictionary<Vector2Int, FluidPortConnectionCache> caches,
        Vector2Int coordinate,
        bool input)
    {
        if (!caches.TryGetValue(coordinate, out FluidPortConnectionCache cache))
        {
            cache = new FluidPortConnectionCache();
            caches.Add(coordinate, cache);
        }

        return cache;
    }

    protected static bool IsFluidItemId(int fluidItemId) => fluidItemId >= 0;
    protected static bool CanUseFluidOutputConnectionWithAnySpace(
        FluidOutputConnection connection, int fluidItemId) =>
        connection.HasSpace && connection.FluidItemId == fluidItemId;

    public void AddConnection(Vector2Int coordinate, int pipeDistance, int fluidItemId, bool hasSpace = true, Pump pump = null) =>
        GetFluidPortConnectionCache(fluidOutputPortConnectionCaches, coordinate, false)
            .Connections.Add(new FluidOutputConnection(pipeDistance, fluidItemId, hasSpace, pump));

    public void Disconnect(Vector2Int coordinate) => fluidOutputPortConnectionCaches.Remove(coordinate);

    public float Retention(Vector2Int coordinate, int fluidItemId, float sourceRate = 0f) =>
        ResolveFluidOutputTransportRetentionAtCoordinate(coordinate, fluidItemId, sourceRate);

    public virtual bool TryGetElectricPowerDemand(out float wattsPerSecond)
    { wattsPerSecond = 0f; return false; }
    public virtual float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId) => MockPressure;
    // PUMP_RATIO
    // MODULE_RETENTION
}

public class CrudeOilRefinery : InputOutputModule
{
    private const float FluidEpsilon = 0.0001f;
    private const float InputBufferSeconds = 2f;

    private readonly struct FluidFlowPort
    {
        public readonly Vector2Int Coordinate;
        public readonly ItemDefinition Definition;
        public readonly float LitersPerSecond;
        public int ItemId => Definition.id;

        public FluidFlowPort(Vector2Int coordinate, int itemId, float litersPerSecond = 1f)
        {
            Coordinate = coordinate;
            Definition = new ItemDefinition { id = itemId };
            LitersPerSecond = litersPerSecond;
        }
    }

    private readonly List<FluidFlowPort> outputPorts = new();
    private readonly Dictionary<int, float> deliveredRates = new();
    private bool ResolveFluidFlowPorts() => true;
    public float GetObjectInfoFluidOutputLitersPerSecond(int itemId) =>
        deliveredRates.TryGetValue(itemId, out float rate) ? rate : 0f;

    public void AddOutput(Vector2Int coordinate, int itemId, int pipeDistance,
        float deliveredRate, bool hasSpace = true)
    {
        outputPorts.Add(new FluidFlowPort(coordinate, itemId));
        deliveredRates[itemId] = deliveredRate;
        AddConnection(coordinate, pipeDistance, itemId, hasSpace);
    }

    private enum RefineryState { Idle, InvalidPorts, MissingInput, InsufficientInput, NoEnergy, Working }
    private sealed class FluidTransferPreview { public void Clear() {} }
    private sealed class FluidInputBuffer
    {
        public float Liters;
        public float LastDeliveryTime = -1f;
        public float LastDeliveryLiters;
        public float ObservedSupplyLitersPerSecond;
    }
    private readonly FluidTransferPreview inputTransferPreview = new();
    private readonly List<FluidFlowPort> inputPorts = new();
    private readonly HashSet<InputOutputModule> directInputPressureSources = new();
    private readonly Dictionary<int, FluidInputBuffer> buffers = new();
    public readonly Dictionary<int, float> Emitted = new();
    public readonly Dictionary<int, float> Requested = new();
    public readonly Dictionary<int, float> Capacity = new();
    public readonly List<int> EmissionAttempts = new();
    public float InputConsumed, EnergyConsumed;
    public float EnergyRatio = 1f;
    public float InputPressureLitersPerSecond;
    public readonly Dictionary<int, float> InputPressureByItemId = new();
    public float GenericStoredLiters;
    public int GenericStoredItemId = 10;
    public float ConnectedInputLiters;
    public float FluidStorageCapacityLiters = 30f;
    public bool SharedOutputStorage;
    private int sharedStoredFluid = -1;
    private bool isRefining;
    private float throughputRatio, productionOpportunityRatio;
    public bool Working => isRefining;
    public float Throughput => throughputRatio;
    private void SetRefineryStatus(RefineryState state, ItemDefinition definition = null) {}
    private ItemDefinition ResolveInstalledDefinition() => new();
    private bool TryGetElectricPowerRequirement(out float wattsPerSecond)
    { wattsPerSecond = 10f; return true; }
    private FluidInputBuffer GetOrCreateInputBuffer(int id) => buffers[id];
    private float GetStoredLiters(FluidInputBuffer buffer) => buffer.Liters;
    private float GetConfiguredStoredInputLiters(FluidFlowPort port) =>
        GenericStoredItemId == port.ItemId ? GenericStoredLiters : 0f;
    private float GetObjectInfoInputPressure(FluidFlowPort port) =>
        InputPressureByItemId.TryGetValue(port.ItemId, out float rate)
            ? rate : InputPressureLitersPerSecond > FluidEpsilon
                ? InputPressureLitersPerSecond : GetDirectInputPressure(port);
    // REFINERY_DIRECT_PRESSURE
    // REFINERY_OPERATIONAL_PRESSURE
    // REFINERY_STARTUP_VOLUME
    private bool TryGetConnectedFluidInputAvailableLitersAtCoordinate(Vector2Int coordinate, int id, float requested,
        out float available, FluidTransferPreview preview)
    {
        available = Math.Min(requested, ConnectedInputLiters);
        return available > FluidEpsilon;
    }
    private bool TryConsumeOperatingEnergy(float delta, out float consumed)
    { consumed = delta * EnergyRatio; EnergyConsumed += consumed; return consumed > 0; }
    private bool TryConsumeInputPort(FluidFlowPort port, float requested, out float consumed, out float temperature)
    {
        consumed = Math.Min(requested, buffers[port.ItemId].Liters);
        buffers[port.ItemId].Liters -= consumed;
        if (GenericStoredItemId == port.ItemId)
        {
            float genericConsumed = Math.Min(requested - consumed, GenericStoredLiters);
            GenericStoredLiters -= genericConsumed;
            consumed += genericConsumed;
        }
        float connectedConsumed = Math.Min(requested - consumed, ConnectedInputLiters);
        ConnectedInputLiters -= connectedConsumed;
        consumed += connectedConsumed;
        InputConsumed += consumed; temperature = 20f;
        return consumed + FluidEpsilon >= requested;
    }
    private bool TryEmitFluidOutputAtCoordinate(Vector2Int coordinate, int id, float requested, float temperature, out float accepted)
    {
        EmissionAttempts.Add(id);
        Requested[id] = requested;
        accepted = Math.Min(requested, Capacity.GetValueOrDefault(id, float.MaxValue));
        if (SharedOutputStorage && sharedStoredFluid >= 0 && sharedStoredFluid != id) accepted = 0;
        if (accepted > 0) sharedStoredFluid = id;
        Emitted[id] = Emitted.GetValueOrDefault(id) + accepted;
        return accepted + FluidEpsilon >= requested;
    }
    public void ConfigureInput(float available = 100f)
    {
        inputPorts.Add(new FluidFlowPort(new(-1, 0), 10, 2f));
        buffers[10] = new FluidInputBuffer { Liters = available };
    }
    public void AddInput(int itemId, float rate, float available = 0f)
    {
        inputPorts.Add(new FluidFlowPort(new(-1 - inputPorts.Count, 0), itemId, rate));
        buffers[itemId] = new FluidInputBuffer { Liters = available };
    }
    public float CapacityForInput(int index) => GetInputBufferCapacityLiters(inputPorts[index]);
    public float DirectPressureForInput(int index) => GetDirectInputPressure(inputPorts[index]);
    public void RefillInput(float liters) => buffers[10].Liters += liters;
    public void DeliverInput(float atSimulationSeconds, float liters)
    {
        MapObjectTickManager.CurrentSimulationTimeSeconds = atSimulationSeconds;
        buffers[10].Liters += liters;
        RecordInputDelivery(buffers[10], liters);
    }
    // REFINERY_DEMAND
    // REFINERY_CAPACITY
    // REFINERY_RECORD_DELIVERY
    public void Tick(float delta = 1f) => UpdateContinuousRefining(delta);
    // REFINERY_TICK
    // REFINERY_PRESSURE
}

internal static class Checks
{
    private static int passed;

    private static void Expect(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.0001f)
        {
            throw new Exception($"{label}: expected {expected}, got {actual}");
        }
        passed++;
    }

    private static CrudeOilRefinery Scenario(int blocked = -1, int distance = 0)
    {
        var refinery = new CrudeOilRefinery();
        refinery.ConfigureInput();
        for (int i = 0; i < 3; i++)
            refinery.AddOutput(new(i, 0), i + 1, i == blocked ? 100 : distance, 0);
        return refinery;
    }

    private static void CheckIndependentOutputs()
    {
        for (int blocked = 0; blocked < 3; blocked++)
        {
            var refinery = Scenario(blocked);
            refinery.Tick();
            Expect(refinery.Working ? 1 : 0, 1, "one blocked output does not stop refining");
            Expect(refinery.InputConsumed, 2f, "discarded byproducts still cost recipe inputs");
            Expect(refinery.EnergyConsumed, 1f, "discarded byproducts still cost energy");
            for (int i = 0; i < 3; i++)
                Expect(refinery.Emitted[i + 1], i == blocked ? 0f : 1f, "each output is delivered independently");
        }
        var disconnected = Scenario();
        disconnected.Disconnect(new(0, 0));
        disconnected.Tick();
        Expect(disconnected.Emitted[1], 0f, "unconnected output is discarded");
        Expect(disconnected.Emitted[2] + disconnected.Emitted[3], 2f, "connected outputs work without the first outlet");
        var mixedDistance = new CrudeOilRefinery();
        mixedDistance.ConfigureInput();
        mixedDistance.AddOutput(new(0,0), 1, 50, 0f);
        mixedDistance.AddOutput(new(1,0), 2, 0, 0f);
        mixedDistance.Tick();
        Expect(mixedDistance.Emitted[1], .5f, "long output route uses its own pressure loss");
        Expect(mixedDistance.Emitted[2], 1f, "long output route never throttles the other output");
        var partial = Scenario();
        partial.Capacity[1] = .2f;
        partial.Capacity[2] = 0f;
        partial.Tick();
        Expect(partial.Emitted[1], .2f, "partially full receiver gets available amount");
        Expect(partial.Emitted[2], 0f, "full receiver discards all output");
        Expect(partial.Emitted[3], 1f, "later output still delivered after two rejections");
        partial.Capacity[1] = 100f;
        partial.Tick();
        Expect(partial.Emitted[1], 1.2f, "discarded fraction never accumulates for later delivery");
        var independent = Scenario();
        independent.Capacity[2] = .1f;
        independent.Tick();
        Expect(independent.Throughput, 1f, "limited output does not throttle whole refinery");
        Expect(independent.Emitted[1], 1f, "unlimited byproduct keeps its own rate");
        var distant = Scenario(-1, 50);
        distant.Tick();
        Expect(distant.InputConsumed, 2f, "pipe loss does not reduce recipe consumption");
        Expect(distant.Emitted[1], .5f, "each route still applies pipe pressure loss");
        var allBlocked = Scenario();
        for (int i = 1; i <= 3; i++) allBlocked.Capacity[i] = 0f;
        allBlocked.Tick();
        Expect(allBlocked.Working ? 1 : 0, 1, "all rejected byproducts may be discarded");
        Expect(allBlocked.InputConsumed, 2f, "all-discarded refining still consumes inputs");
        var missing = new CrudeOilRefinery();
        missing.ConfigureInput(0f);
        missing.AddOutput(new(0,0), 1, 0, 0);
        missing.Tick();
        Expect(missing.EnergyConsumed, 0f, "missing input prevents energy consumption");
        Expect(missing.EmissionAttempts.Count, 0, "missing input cannot create products");
        var noEnergy = Scenario();
        noEnergy.EnergyRatio = 0f;
        noEnergy.Tick();
        Expect(noEnergy.InputConsumed, 0f, "no energy prevents input consumption");
        var halfEnergy = Scenario();
        halfEnergy.EnergyRatio = .5f;
        halfEnergy.Tick();
        Expect(halfEnergy.InputConsumed, 1f, "partial energy scales input consumption");
        Expect(halfEnergy.Emitted[1], .5f, "partial energy scales product generation");
        var limitedInput = new CrudeOilRefinery { InputPressureLitersPerSecond = 1f };
        limitedInput.ConfigureInput(2f);
        for (int i = 0; i < 3; i++)
            limitedInput.AddOutput(new(i, 0), i + 1, 0, 0f);
        for (int i = 0; i < 10; i++)
        {
            limitedInput.Tick(.1f);
            Expect(limitedInput.Working ? 1f : 0f, 1f,
                "low but steady input must not alternate between working and idle");
            Expect(limitedInput.Throughput, .5f, "input supply scales refinery throughput");
            Expect(limitedInput.EnergyConsumed, (i + 1) * .05f,
                "input supply scales energy consumption");
            Expect(limitedInput.TryGetElectricPowerDemand(out float watts) ? watts : 0f,
                5f, "electric demand follows input-limited throughput");
            limitedInput.RefillInput(.1f);
        }
        Expect(limitedInput.InputConsumed, 1f, "steady half-rate input is conserved");
        Expect(limitedInput.Emitted[1], .5f, "steady half-rate input scales products");
        var burstyInput = new CrudeOilRefinery { InputPressureLitersPerSecond = .75f };
        burstyInput.ConfigureInput(1f);
        for (int i = 0; i < 3; i++)
            burstyInput.AddOutput(new(i, 0), i + 1, 0, 0f);
        burstyInput.Tick(.1f);
        Expect(burstyInput.Working ? 1f : 0f, 0f,
            "one-liter source batch waits for a reserve before starting");
        Expect(burstyInput.InputConsumed, 0f,
            "warmup does not consume the first batch");
        burstyInput.RefillInput(1f);
        for (int i = 0; i < 40; i++)
        {
            if (i > 0 && i % 13 == 0) burstyInput.RefillInput(1f);
            burstyInput.Tick(.1f);
            Expect(burstyInput.Working ? 1f : 0f, 1f,
                "whole-liter source deliveries must not blink refinery work");
            Expect(burstyInput.Throughput, .375f,
                "buffer draw follows source pressure between deliveries");
        }
        Expect(burstyInput.InputConsumed, 3f,
            "burst smoothing conserves fluid at the source rate");
        Expect(burstyInput.Emitted[1], 1.5f,
            "burst smoothing preserves proportional output");
        var storedInput = new CrudeOilRefinery { InputPressureLitersPerSecond = .75f,
            GenericStoredLiters = 2f };
        storedInput.ConfigureInput(0f);
        storedInput.AddOutput(new(0, 0), 1, 0, 0f);
        for (int i = 0; i < 10; i++)
        {
            storedInput.Tick(.1f);
            Expect(storedInput.Working ? 1f : 0f, 1f,
                "configured StoreFluid stock must feed refining on every tick");
        }
        Expect(storedInput.GenericStoredLiters, 1.25f,
            "refining must consume the configured StoreFluid stock");
        var noPipePressure = new CrudeOilRefinery();
        noPipePressure.ConfigureInput(0f);
        noPipePressure.AddOutput(new(0, 0), 1, 0, 0f);
        noPipePressure.DeliverInput(1f, 1f);
        noPipePressure.Tick(.1f);
        Expect(noPipePressure.Working ? 1f : 0f, 0f,
            "first direct one-liter delivery stays buffered during startup");
        noPipePressure.DeliverInput(1f + 4f / 3f, 1f);
        noPipePressure.Tick(.1f);
        Expect(noPipePressure.Working ? 1f : 0f, 1f,
            "observed deliveries keep direct inputs working without pipe pressure");
        Expect(noPipePressure.Throughput, .375f,
            "observed delivery rate limits buffer draw without a pipe");
        var connectedTank = new CrudeOilRefinery {
            InputPressureLitersPerSecond = .75f,
            ConnectedInputLiters = 10f
        };
        connectedTank.ConfigureInput(0f);
        connectedTank.AddOutput(new(0, 0), 1, 0, 0f);
        for (int i = 0; i < 10; i++)
        {
            connectedTank.Tick(.1f);
            Expect(connectedTank.Working ? 1f : 0f, 1f,
                "connected tank must not wait for a local startup reserve");
        }
        Expect(connectedTank.InputConsumed, .75f,
            "tank-fed input follows actual pressure");
        var configuredCapacity = new CrudeOilRefinery();
        configuredCapacity.ConfigureInput(0f);
        configuredCapacity.AddInput(11, 10f);
        Expect(configuredCapacity.CapacityForInput(0), 5f,
            "configured storage allocates capacity by input rate");
        Expect(configuredCapacity.CapacityForInput(1), 25f,
            "configured storage keeps the total input capacity at 30 liters");
        var twoInputs = new CrudeOilRefinery();
        twoInputs.ConfigureInput(2f);
        twoInputs.AddInput(11, 10f, 25f);
        twoInputs.AddOutput(new(0, 0), 1, 0, 0f);
        twoInputs.InputPressureByItemId[10] = .75f;
        twoInputs.InputPressureByItemId[11] = 15f;
        for (int i = 0; i < 40; i++)
        {
            if (i > 0 && i % 13 == 0) twoInputs.RefillInput(1f);
            twoInputs.Tick(.1f);
            Expect(twoInputs.Working ? 1f : 0f, 1f,
                "two-input refinery must stay working across crude delivery gaps");
            Expect(twoInputs.Throughput, .375f,
                "crude pressure limits both recipe inputs together");
        }
        Expect(twoInputs.InputConsumed, 18f,
            "two-input refinery consumes crude and water in recipe proportion");
        var directSource = new InputOutputModule { MockPressure = .75f };
        InputOutputModule.RegisterDirectSource(new(0, 0), new(-1, 0), directSource);
        var directRefinery = new CrudeOilRefinery();
        directRefinery.ConfigureInput(0f);
        Expect(directRefinery.DirectPressureForInput(0), .75f,
            "adjacent direct producer supplies pressure without a pipe");
        InputOutputModule.ClearDirectSources();
        var shared = Scenario();
        shared.SharedOutputStorage = true;
        shared.Tick();
        Expect(shared.Emitted[1], 1f, "first compatible output enters shared receiver");
        Expect(shared.Emitted[2] + shared.Emitted[3], 0f, "incompatible shared outputs are discarded");
        Expect(shared.Working ? 1 : 0, 1, "shared receiver conflict does not stop refinery");
    }

    private static void Main()
    {
        var refinery = new CrudeOilRefinery();
        Vector2Int near = new(0, 0);
        Vector2Int far = new(1, 0);
        Vector2Int limit = new(2, 0);
        refinery.AddOutput(near, 1, 0, 2f);
        refinery.AddOutput(far, 2, 50, 0.5f);
        refinery.AddOutput(limit, 3, 100, 0.1f);
        Expect(refinery.Retention(near, 1), 1f, "zero-pipe route retains full output");
        Expect(refinery.Retention(far, 2), 0.5f, "fifty-pipe route halves output");
        Expect(refinery.Retention(limit, 3), 0f, "hundred-pipe route blocks output");
        Expect(refinery.Retention(near, 2), 0f, "fluid ports stay separate");
        Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(1), 2f,
            "near output reports its own source pressure");
        Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(2), 1f,
            "distant output avoids double attenuation in the pressure display");
        Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(2)
               * 0.5f, 0.5f,
            "tank-end pressure matches delivered output");
        Expect(refinery.GetObjectInfoFluidPressureLitersPerSecond(3), 0f,
            "blocked route reports zero even while an old sample remains");

        refinery.AddConnection(far, 25, 2, false);
        Expect(refinery.Retention(far, 2), 0.5f, "full closer tank is ignored");
        refinery.AddConnection(far, 25, 2);
        Expect(refinery.Retention(far, 2), 0.75f, "available closer tank takes priority");
        var module = new InputOutputModule();
        var pump = new Pump();
        module.AddConnection(near, 0, 1, true, pump);
        Expect(module.Retention(near, 1, 1f) * 1f, 1f, "pump never raises native source output");
        Expect(module.Retention(near, 1, 20f) * 20f, 5f, "pump limits a faster source to its own rate");
        module.AddConnection(far, 20, 1, true, pump);
        Expect(module.Retention(far, 1, 20f) * 20f, 4f, "pipe loss follows pump rate");
        pump.PressureLitersPerSecond = 2.5f;
        Expect(module.Retention(near, 1, 20f) * 20f, 2.5f, "edited pump rate limits real production");
        pump.PressureLitersPerSecond = 0f;
        Expect(module.Retention(near, 1, 20f), 0f, "zero pump rate blocks production");
        CheckIndependentOutputs();
        Console.WriteLine($"Crude refinery transport checks passed: {passed}");
    }

}
