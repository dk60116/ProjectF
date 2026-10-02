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
    public static class Time { public static float unscaledTime; }
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
    public bool IsFluid = true;
    public string itemName = "Fluid", name = "Fluid";
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
    public bool isActiveAndEnabled = true;
    public readonly record struct ItemIoEntry(ItemDefinition itemDefinition, float count)
    {
        public bool IsFluid => itemDefinition != null && itemDefinition.IsFluid;
        public float ResolvedAmount => Mathf.Max(0.0001f, count);
    }
    public sealed class InputOutputPair { public List<ItemIoEntry> inputs = new(); public List<ItemIoEntry> outputs = new(); }
    protected readonly List<InputOutputPair> pairs = new() { new() };
    public IReadOnlyList<InputOutputPair> InputOutputPairs => pairs;
    protected virtual bool IsRecipeOutputAllowedByItemFilter(int itemId) => true;
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

    protected virtual bool HasOperationalTarget() => false;
    protected bool TryGetElectricPowerRequirement(out float wattsPerSecond)
    { wattsPerSecond = 10f; return true; }
    // REFINERY_OUTPUT_STATE
    public sealed class PersistentState
    {
        public ProjectF.Simulation.ProductionProcess process;
        public List<int> refineryInputFluidItemIds = new();
        public List<long> refineryInputFluidUnits = new();
        public List<float> refineryInputFluidTemperatures = new();
        public float refineryBatchDuration, refineryBatchTemperature;
        public List<RefineryOutputState> refineryOutputs = new();
    }
    public virtual PersistentState CapturePersistentState() => new() { process = production };
    public virtual void ApplyPersistentState(PersistentState state) { if (state != null) production = state.process; }
    public virtual void PrepareForPool() { production.Clear(); }
    protected virtual void OnDisable() {}
    public virtual void ApplyManagedUpdateTick() {}
    protected virtual bool ShouldAutoPullFluidFromConnectedStorage() => true;
    protected virtual bool ShouldKeepRuntimeUpdateTickActive() => false;
    protected virtual bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy() => false;
    protected virtual bool ShouldPlayWorkAnimation() => true;
    protected virtual void OnManagedRuntimeVisualsFlushed() {}
    protected virtual string ResolveObjectInfoStatus(out bool producing) { producing = false; return "Idle"; }
    public string Status { get { return ResolveObjectInfoStatus(out _); } }
    public virtual bool CanAcceptFluidItem(int id, float liters = 0f) => true;
    public virtual float GetStoredFluidTemperatureCelsius(int id) => 20f;
    protected virtual void AppendDedicatedFluidStorageRuntimeCoordinates(List<Vector2Int> coordinates) {}
    internal virtual bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int coordinate) => false;
    internal virtual float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(Vector2Int coordinate) => 0f;
    internal virtual float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(Vector2Int coordinate) => 0f;
    internal virtual bool CanAcceptDedicatedFluidAtRuntimeCoordinate(Vector2Int coordinate, int id, float liters) => false;
    internal virtual bool TryAddDedicatedFluidAtRuntimeCoordinate(Vector2Int coordinate, int id, float liters, float temperature, out float accepted) { accepted = 0f; return false; }
    public enum RectGridBlockType { PipeInputItem, DoubleInputItem, PipeInput, PipeOutputItem, DoublePipeOutputItem }
    public readonly record struct RectGridBlockPlacement(RectGridBlockType blockType, ItemDefinition itemDefinition, Vector2Int coordinate);
    protected readonly List<RectGridBlockPlacement> placements = new();
    protected IReadOnlyList<RectGridBlockPlacement> RectGridPlacements => placements;
    public bool PortsValid = true;
    protected bool TryGetPlacementRuntime(out Vector2Int anchor, out int turns) { anchor = default; turns = 0; return PortsValid; }
    protected static bool TryGetRectGridPlacementCoordinate(InputOutputModule owner, Vector2Int anchor, int turns,
        RectGridBlockPlacement placement, out Vector2Int coordinate) { coordinate = placement.coordinate; return true; }
    protected bool TryGetInputOutputPair(int index, out InputOutputPair pair) { pair = pairs[0]; return PortsValid; }
    protected ItemDefinition ResolveInstalledDefinition() => new();
    public float Duration = 5f;
    public bool RequiresEnergy = true;
    public float EnergyRatio = 1f, EnergyConsumed;
    protected float ResolveInitialCraftDuration(ItemDefinition definition) => Duration;
    protected bool RequiresOperationalEnergy(ItemDefinition definition) => RequiresEnergy;
    protected bool HasOperationalEnergyAvailable(ItemDefinition definition) => !RequiresEnergy || EnergyRatio > 0f;
    protected float OperationalAnimationSpeedRatio => !RequiresEnergy ? 1f : EnergyRatio;
    protected float ResolveCompleteEnergy(ItemDefinition definition) => Duration;
    protected bool TryConsumeOperatingEnergy(float delta, out float consumed)
    { consumed = RequiresEnergy ? delta * EnergyRatio : 0f; EnergyConsumed += consumed; return !RequiresEnergy || consumed > 0f; }
    protected ProjectF.Simulation.ProductionProcess production = ProjectF.Simulation.ProductionProcess.Empty;
    private bool hasActiveCraft => production.Active;
    private bool waitingForOutput => production.WaitingForOutput;
    protected bool IsActiveCraftRunning => production.Active;
    protected bool IsWaitingForOutput => production.WaitingForOutput;
    public bool Active => production.Active;
    public bool Outputting => production.WaitingForOutput;
    public float ObjectInfoWorkGaugeFillAmount => !Active ? 0f : Outputting ? 1f : RequiresEnergy
        ? DeterministicSimulationUnits.ToFloat(production.ConsumedEnergyUnits) / Duration
        : 1f - DeterministicSimulationUnits.TicksToSeconds(production.RemainingTicks) / Duration;
    protected void BeginActiveCraft(int recipe, int id, int count, ItemDefinition definition) =>
        production.Begin(recipe, id, count, RequiresEnergy ? 0L : DeterministicSimulationUnits.SecondsToTicks(Duration));
    protected void ClearActiveCraft() => production.Clear();
    protected virtual bool TryCompleteActiveCraft() => false;
    // ADVANCE_CRAFT
    private float pendingDelta;
    public void Tick(float delta = 1f) { pendingDelta = delta; ApplyManagedUpdateTick(); }
    protected bool TryBeginPlannedModuleApply(out float delta) { delta = pendingDelta; return true; }
    protected void MarkPersistenceStateDirty() {}
    protected void MarkManagedRuntimeVisualsDirty() {}
    protected void RefreshRuntimeUpdateSleepState() {}
    protected static void NotifyFluidInputAvailabilityIncreased(InputOutputModule target) {}
    protected static void NotifyFluidOutputCapacityIncreased(InputOutputModule target) {}
    protected object particleEffect;
    protected void SetVisualParticleActive(object particle, bool active, bool clear = false) {}
    protected static float NormalizeFluidTemperatureCelsius(float value) => value;
    public float StoredFluidLiters;
    public int StoredFluidItemId = -1;
    public readonly Dictionary<int, float> Connected = new(), Emitted = new(), Capacity = new(), Temperatures = new();
    protected bool TryConsumeFluidLiters(int id, float requested, out float consumed)
    { consumed = Math.Min(requested, StoredFluidLiters); StoredFluidLiters -= consumed; return consumed > 0f; }
    protected bool TryConsumeConnectedFluidInputAtCoordinate(Vector2Int coordinate, int id, float requested,
        out float consumed, out float temperature)
    { consumed = Math.Min(requested, Connected.GetValueOrDefault(id)); Connected[id] = Connected.GetValueOrDefault(id) - consumed; temperature = 20f; return consumed > 0f; }
    protected bool TryEmitFluidOutputAtCoordinate(Vector2Int coordinate, int id, float requested,
        float temperature, out float accepted)
    { accepted = Math.Min(requested, Capacity.GetValueOrDefault(id, 10000f)); Emitted[id] = Emitted.GetValueOrDefault(id) + accepted;
      Temperatures[id] = temperature; return accepted > 0f; }
    protected bool TryGetRuntimeFluidInputPressure(Vector2Int coordinate, int id, out float pressure) { pressure = 0f; return false; }
    public void AddInput(Vector2Int coordinate, int id, float rate)
    {
        var definition = new ItemDefinition { id = id };
        pairs[0].inputs.Add(new ItemIoEntry(definition, rate));
        placements.Add(new RectGridBlockPlacement(RectGridBlockType.PipeInputItem, definition, coordinate));
    }
    public void AddOutput(Vector2Int coordinate, int id, int distance, float rate, bool hasSpace = true)
    {
        var definition = new ItemDefinition { id = id };
        pairs[0].outputs.Add(new ItemIoEntry(definition, rate));
        placements.Add(new RectGridBlockPlacement(RectGridBlockType.PipeOutputItem, definition, coordinate));
        AddConnection(coordinate, distance, id, hasSpace);
    }
    // MODULE_DEMAND
    // MODULE_PRESSURE
    // PUMP_RATIO
    // MODULE_RETENTION
}

public class FixedPressureSource : InputOutputModule
{
    public float MockPressure;
    public override float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId) => MockPressure;
}

public static class Pipe { public static void InvalidateFluidDisplayNetworkCache(InputOutputModule target) {} }
public static class ProjectFApplicationLifecycle { public static bool IsQuitting; }
// REFINERY_IMPLEMENTATION
