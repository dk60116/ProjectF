using System.Collections.Generic;
using UnityEngine;

public class CrudeOilRefinery : InputOutputModule
{
    private const float FluidEpsilon = 0.0001f;
    // The shared transport path cannot deliver <= 0.0001 L. Use the same
    // boundary for batch readiness, and debit actual stock without rounding it up.
    private static readonly long InputCompletionToleranceUnits =
        DeterministicSimulationUnits.FromFloat(FluidEpsilon);
    private const float InputPressureRefreshIntervalSeconds = 0.25f;

    private enum RefineryState
    {
        Idle,
        InvalidPorts,
        MissingInput,
        NoEnergy,
        Working,
        Outputting,
        WaitingForOutput
    }

    private readonly struct FluidFlowPort
    {
        public readonly Vector2Int Coordinate;
        public readonly ItemDefinition Definition;
        public readonly float LitersPerSecond;

        public int ItemId => Definition != null ? Definition.id : -1;

        public FluidFlowPort(
            Vector2Int coordinate,
            ItemDefinition definition,
            float litersPerSecond)
        {
            Coordinate = coordinate;
            Definition = definition;
            LitersPerSecond = Mathf.Max(0f, litersPerSecond);
        }
    }

    private sealed class FluidInputBuffer
    {
        public readonly int ItemId;
        public long StoredUnits;
        public float TemperatureCelsius;

        public FluidInputBuffer(int itemId)
        {
            ItemId = itemId;
            TemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        }
    }

    private sealed class FluidInputPressureCache
    {
        public readonly Vector2Int Coordinate;
        public readonly int ItemId;
        public float LitersPerSecond;
        public float NextRefreshTime;

        public FluidInputPressureCache(Vector2Int coordinate, int itemId)
        {
            Coordinate = coordinate;
            ItemId = itemId;
        }
    }

    private readonly List<FluidFlowPort> inputPorts = new List<FluidFlowPort>(2);
    private readonly List<FluidFlowPort> outputPorts = new List<FluidFlowPort>(3);
    private readonly List<FluidInputBuffer> inputBuffers = new List<FluidInputBuffer>(2);
    private readonly List<FluidInputPressureCache> inputPressureCaches =
        new List<FluidInputPressureCache>(2);
    private readonly HashSet<InputOutputModule> directInputPressureSources =
        new HashSet<InputOutputModule>();
    private bool isRefining;
    private readonly List<RefineryOutputState> batchOutputs = new List<RefineryOutputState>(3);
    private float batchDuration;
    private float batchTemperature;
    private float outputDeltaTime;
    private bool emittedThisTick;
    private RefineryState refineryState;
    private int refineryStatusItemId = -1;
    private string refineryStatus = "Idle";

    public bool IsRefining => isRefining;
    public float ObjectInfoProcessingRatio => ObjectInfoWorkGaugeFillAmount;
    public int ObjectInfoInputCount => ResolveFluidFlowPorts() ? inputPorts.Count : 0;
    public int ObjectInfoOutputCount => IsActiveCraftRunning ? batchOutputs.Count
        : ResolveFluidFlowPorts() ? outputPorts.Count : 0;

    public override PersistentState CapturePersistentState()
    {
        PersistentState state = base.CapturePersistentState();
        state.refineryInputFluidItemIds.Clear();
        state.refineryInputFluidUnits.Clear();
        state.refineryInputFluidTemperatures.Clear();
        for (int i = 0; i < inputBuffers.Count; i++)
        {
            FluidInputBuffer buffer = inputBuffers[i];
            state.refineryInputFluidItemIds.Add(buffer.ItemId);
            state.refineryInputFluidUnits.Add(System.Math.Max(0L, buffer.StoredUnits));
            state.refineryInputFluidTemperatures.Add(buffer.TemperatureCelsius);
        }

        state.refineryBatchDuration = batchDuration;
        state.refineryBatchTemperature = batchTemperature;
        for (int i = 0; i < batchOutputs.Count; i++) state.refineryOutputs.Add(batchOutputs[i].Clone());
        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        StopRefineryParticle();
        base.ApplyPersistentState(state);
        inputBuffers.Clear();
        batchOutputs.Clear();
        batchDuration = state != null ? state.refineryBatchDuration : 0f;
        batchTemperature = state != null ? state.refineryBatchTemperature : MapClimate.CurrentTemperatureCelsius;
        if (state?.refineryOutputs != null)
            for (int i = 0; i < state.refineryOutputs.Count; i++) batchOutputs.Add(state.refineryOutputs[i].Clone());
        if (IsActiveCraftRunning && batchOutputs.Count == 0) ClearActiveCraft();
        if (state == null || state.refineryInputFluidItemIds == null)
        {
            return;
        }

        for (int i = 0; i < state.refineryInputFluidItemIds.Count; i++)
        {
            int itemId = state.refineryInputFluidItemIds[i];
            if (itemId < 0 || FindInputBuffer(itemId) != null)
            {
                continue;
            }

            FluidInputBuffer buffer = new FluidInputBuffer(itemId)
            {
                StoredUnits = state.refineryInputFluidUnits != null
                              && i < state.refineryInputFluidUnits.Count
                    ? System.Math.Max(0L, state.refineryInputFluidUnits[i])
                    : 0L,
                TemperatureCelsius = state.refineryInputFluidTemperatures != null
                                     && i < state.refineryInputFluidTemperatures.Count
                    ? state.refineryInputFluidTemperatures[i]
                    : MapClimate.CurrentTemperatureCelsius
            };
            inputBuffers.Add(buffer);
        }
    }

    public override void PrepareForPool()
    {
        StopRefineryParticle();
        inputPorts.Clear();
        outputPorts.Clear();
        inputBuffers.Clear();
        batchOutputs.Clear();
        batchDuration = outputDeltaTime = 0f;
        inputPressureCaches.Clear();
        directInputPressureSources.Clear();
        refineryState = RefineryState.Idle;
        refineryStatusItemId = -1;
        refineryStatus = "Idle";
        base.PrepareForPool();
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        StopRefineryParticle();
        base.OnDisable();
    }

    public override void ApplyManagedUpdateTick()
    {
        if (!TryBeginPlannedModuleApply(out float deltaTime))
        {
            return;
        }

        UpdateBatchRefining(deltaTime);
        MarkManagedRuntimeVisualsDirty();
        RefreshRuntimeUpdateSleepState();
    }

    protected override bool HasOperationalTarget()
    {
        return TryResolveFluidFlowPorts(false);
    }

    protected override bool ShouldAutoPullFluidFromConnectedStorage()
    {
        return false;
    }

    protected override void AppendDedicatedFluidStorageRuntimeCoordinates(
        List<Vector2Int> coordinates)
    {
        if (coordinates == null || !ResolveFluidFlowPorts())
        {
            return;
        }

        for (int i = 0; i < inputPorts.Count; i++)
        {
            FluidFlowPort port = inputPorts[i];
            Vector2Int coordinate = port.Coordinate;
            if (!coordinates.Contains(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }
    }

    internal override bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int coordinate) =>
        TryGetInputPortAndBuffer(coordinate, out _, out _);

    internal override float GetDedicatedFluidStorageFillRatioAtRuntimeCoordinate(
        Vector2Int coordinate)
    {
        if (!TryGetInputPortAndBuffer(
                coordinate,
                out FluidFlowPort port,
                out FluidInputBuffer buffer))
        {
            return 0f;
        }

        float capacity = GetInputBufferCapacityLiters(port);
        return capacity > FluidEpsilon
            ? Mathf.Clamp01(GetStoredLiters(buffer) / capacity)
            : 0f;
    }

    internal override float GetDedicatedAvailableFluidStorageLitersAtRuntimeCoordinate(
        Vector2Int coordinate)
    {
        return TryGetInputPortAndBuffer(
            coordinate,
            out FluidFlowPort port,
            out FluidInputBuffer buffer)
            && !IsActiveCraftRunning
            ? DeterministicSimulationUnits.ToFloat(GetAvailableInputUnits(port, buffer))
            : 0f;
    }

    internal override bool CanAcceptDedicatedFluidAtRuntimeCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters)
    {
        return !IsActiveCraftRunning && TryGetInputPortAndBuffer(
                   coordinate,
                   out FluidFlowPort port,
                   out FluidInputBuffer buffer)
               && port.ItemId == fluidItemId
               && GetInputBufferCapacityLiters(port) - GetStoredLiters(buffer)
               + FluidEpsilon >= Mathf.Max(0f, requestedLiters);
    }

    internal override bool TryAddDedicatedFluidAtRuntimeCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters,
        float temperatureCelsius,
        out float acceptedLiters)
    {
        acceptedLiters = 0f;
        if (IsActiveCraftRunning || requestedLiters <= FluidEpsilon
            || !TryGetInputPortAndBuffer(
                coordinate,
                out FluidFlowPort port,
                out FluidInputBuffer buffer)
            || port.ItemId != fluidItemId)
        {
            return false;
        }

        long availableUnits = GetAvailableInputUnits(port, buffer);
        long acceptedUnits = System.Math.Min(
            availableUnits,
            DeterministicSimulationUnits.FromFloat(requestedLiters));
        if (acceptedUnits <= 0L)
        {
            return false;
        }

        float previousLiters = GetStoredLiters(buffer);
        acceptedLiters = DeterministicSimulationUnits.ToFloat(acceptedUnits);
        float incomingTemperature = NormalizeFluidTemperatureCelsius(temperatureCelsius);
        float totalLiters = previousLiters + acceptedLiters;
        buffer.TemperatureCelsius = totalLiters > FluidEpsilon
            ? ((buffer.TemperatureCelsius * previousLiters)
               + (incomingTemperature * acceptedLiters)) / totalLiters
            : incomingTemperature;
        buffer.StoredUnits += acceptedUnits;
        MarkPersistenceStateDirty();
        NotifyFluidInputAvailabilityIncreased(this);
        Pipe.InvalidateFluidDisplayNetworkCache(this);
        return true;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        // Each port has a separate network, so the refinery must keep checking for
        // input/output changes even when one of the networks is currently blocked.
        return true;
    }

    protected override bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy()
    {
        // Fluid ports still accept and route deliveries when refining cannot run.
        return true;
    }

    protected override bool ShouldPlayWorkAnimation()
    {
        return false;
    }

    // All refinery intake goes through a typed port, never the shared tank store.
    public override bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f) => false;

    protected override void OnManagedRuntimeVisualsFlushed()
    {
        SetVisualParticleActive(particleEffect, isRefining || emittedThisTick);
    }

    private void StopRefineryParticle()
    {
        isRefining = emittedThisTick = false;
        SetVisualParticleActive(particleEffect, false, clear: true);
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = refineryState == RefineryState.Working || refineryState == RefineryState.Outputting;
        return refineryStatus;
    }

    public bool TryGetObjectInfoInput(
        int index,
        out int itemId,
        out float requiredLitersPerSecond,
        out float supplyLitersPerSecond,
        out float bufferedLiters)
    {
        itemId = -1;
        requiredLitersPerSecond = 0f;
        supplyLitersPerSecond = 0f;
        bufferedLiters = 0f;
        if (!ResolveFluidFlowPorts() || index < 0 || index >= inputPorts.Count)
        {
            return false;
        }

        FluidFlowPort port = inputPorts[index];
        itemId = port.ItemId;
        requiredLitersPerSecond = port.LitersPerSecond;
        supplyLitersPerSecond = GetObjectInfoInputPressure(port);
        FluidInputBuffer buffer = GetOrCreateInputBuffer(port.ItemId);
        bufferedLiters = GetStoredLiters(buffer) + GetConfiguredStoredInputLiters(port);
        return itemId >= 0;
    }

    private float GetObjectInfoInputPressure(FluidFlowPort port)
    {
        FluidInputPressureCache cache = null;
        for (int i = 0; i < inputPressureCaches.Count; i++)
        {
            FluidInputPressureCache candidate = inputPressureCaches[i];
            if (candidate.Coordinate == port.Coordinate && candidate.ItemId == port.ItemId)
            {
                cache = candidate;
                break;
            }
        }

        if (cache == null)
        {
            cache = new FluidInputPressureCache(port.Coordinate, port.ItemId);
            inputPressureCaches.Add(cache);
        }
        else if (Time.unscaledTime < cache.NextRefreshTime)
        {
            return cache.LitersPerSecond;
        }

        TryGetRuntimeFluidInputPressure(
            port.Coordinate,
            port.ItemId,
            out cache.LitersPerSecond);
        if (cache.LitersPerSecond <= FluidEpsilon)
        {
            cache.LitersPerSecond = GetDirectInputPressure(port);
        }
        cache.NextRefreshTime = Time.unscaledTime + InputPressureRefreshIntervalSeconds;
        return cache.LitersPerSecond;
    }

    private float GetDirectInputPressure(FluidFlowPort port)
    {
        if (!TryGetRuntimePipeAreaExternalDirection(
                port.Coordinate, out Vector2Int externalDirection))
        {
            return 0f;
        }

        directInputPressureSources.Clear();
        AppendFluidOutputSourcesAtCoordinate(
            port.Coordinate, -externalDirection, directInputPressureSources);
        AppendFluidOutputSourcesAtCoordinate(
            port.Coordinate + externalDirection,
            -externalDirection,
            directInputPressureSources);
        float pressure = 0f;
        foreach (InputOutputModule source in directInputPressureSources)
        {
            if (source != this)
            {
                pressure += source.GetObjectInfoFluidPressureLitersPerSecond(port.ItemId);
            }
        }

        directInputPressureSources.Clear();
        return pressure;
    }

    public bool TryGetObjectInfoOutput(
        int index, out int itemId, out float litersPerSecond, out bool isBlocked,
        out float remainingLiters, out float totalLiters)
    {
        itemId = -1;
        litersPerSecond = remainingLiters = totalLiters = 0f;
        isBlocked = false;
        ResolveFluidFlowPorts();
        if (IsActiveCraftRunning)
        {
            if (index < 0 || index >= batchOutputs.Count) return false;
            RefineryOutputState output = batchOutputs[index];
            itemId = output.itemId;
            litersPerSecond = output.litersPerSecond;
            remainingLiters = DeterministicSimulationUnits.ToFloat(System.Math.Max(0L, output.remainingUnits));
            totalLiters = DeterministicSimulationUnits.ToFloat(output.totalUnits);
            if (IsWaitingForOutput && output.remainingUnits > 0L)
                isBlocked = !TryGetBatchOutputPort(itemId, out FluidFlowPort port)
                    || ResolveFluidOutputTransportRetentionAtCoordinate(port.Coordinate, itemId, litersPerSecond) <= FluidEpsilon;
        }
        else
        {
            if (index < 0 || index >= outputPorts.Count) return false;
            FluidFlowPort port = outputPorts[index];
            itemId = port.ItemId;
            litersPerSecond = port.LitersPerSecond;
            totalLiters = litersPerSecond * ResolveInitialCraftDuration(ResolveInstalledDefinition());
        }
        return itemId >= 0;
    }

    public float GetObjectInfoRequiredInputLiters(int index) =>
        ResolveFluidFlowPorts() && index >= 0 && index < inputPorts.Count
            ? GetInputBufferCapacityLiters(inputPorts[index]) : 0f;

    public override float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId)
    {
        if (!isActiveAndEnabled || !IsActiveCraftRunning || !IsWaitingForOutput
            || !HasOperationalEnergyAvailable(ResolveInstalledDefinition())) return 0f;
        for (int i = 0; i < batchOutputs.Count; i++)
            if (batchOutputs[i].itemId == fluidItemId && batchOutputs[i].remainingUnits != 0L)
                return batchOutputs[i].litersPerSecond;
        return 0f;
    }

    public override float GetStoredFluidTemperatureCelsius(int fluidItemId)
    {
        if (IsActiveCraftRunning)
            for (int i = 0; i < batchOutputs.Count; i++)
                if (batchOutputs[i].itemId == fluidItemId) return batchTemperature;
        FluidInputBuffer input = FindInputBuffer(fluidItemId);
        return input != null && input.StoredUnits > 0L ? input.TemperatureCelsius
            : base.GetStoredFluidTemperatureCelsius(fluidItemId);
    }

    private void UpdateBatchRefining(float deltaTime)
    {
        isRefining = emittedThisTick = false;
        outputDeltaTime = 0f;
        if (!Application.isPlaying || deltaTime <= 0f) return;
        if (!ResolveFluidFlowPorts())
        {
            SetRefineryStatus(RefineryState.InvalidPorts);
            return;
        }

        if (IsActiveCraftRunning && !IsWaitingForOutput)
        {
            // The shared ProductionProcess consumes energy once and advances only
            // accumulated energy (or elapsed ticks for an unpowered recipe).
            UpdateActiveCraft(deltaTime);
            isRefining = OperationalAnimationSpeedRatio > FluidEpsilon;
            SetRefineryStatus(isRefining ? RefineryState.Working : RefineryState.NoEnergy);
            MarkPersistenceStateDirty();
            if (IsWaitingForOutput) Pipe.InvalidateFluidDisplayNetworkCache(this);
            return;
        }

        bool powered = TryConsumeOperatingEnergy(deltaTime, out _);
        if (IsActiveCraftRunning)
        {
            if (!powered)
            {
                SetRefineryStatus(RefineryState.NoEnergy);
                return;
            }
            outputDeltaTime = deltaTime;
            bool finished = TryCompleteActiveCraft();
            SetRefineryStatus(emittedThisTick ? RefineryState.Outputting
                : finished ? RefineryState.MissingInput : RefineryState.WaitingForOutput);
            return;
        }

        CollectBatchInputs(deltaTime);
        if (!powered)
        {
            SetRefineryStatus(RefineryState.NoEnergy);
            return;
        }
        for (int i = 0; i < inputPorts.Count; i++)
        {
            FluidFlowPort port = inputPorts[i];
            FluidInputBuffer buffer = GetOrCreateInputBuffer(port.ItemId);
            if (buffer.StoredUnits <= 0L
                || GetAvailableInputUnits(port, buffer) > InputCompletionToleranceUnits)
            {
                SetRefineryStatus(RefineryState.MissingInput, port.Definition);
                return;
            }
        }
        BeginRefineryBatch();
        // Collection/start is a distinct step; do not spend this tick's energy twice.
        SetRefineryStatus(RefineryState.Working);
    }

    private void CollectBatchInputs(float deltaTime)
    {
        for (int i = 0; i < inputPorts.Count; i++)
        {
            FluidFlowPort port = inputPorts[i];
            long availableUnits = GetAvailableInputUnits(port, GetOrCreateInputBuffer(port.ItemId));
            if (availableUnits <= InputCompletionToleranceUnits) continue;
            long requestedUnits = System.Math.Min(availableUnits,
                DeterministicSimulationUnits.FromFloat(port.LitersPerSecond * deltaTime));
            float requested = DeterministicSimulationUnits.ToFloat(requestedUnits);
            if (requested <= FluidEpsilon) continue;
            // Migrate any legacy shared-store fluid into its typed input buffer.
            if (GetConfiguredStoredInputLiters(port) > FluidEpsilon)
            {
                float temperature = base.GetStoredFluidTemperatureCelsius(port.ItemId);
                TryConsumeFluidLiters(port.ItemId, requested, out float stored);
                TryAddDedicatedFluidAtRuntimeCoordinate(port.Coordinate, port.ItemId,
                    stored, temperature, out _);
                requested -= stored;
            }
            if (requested <= FluidEpsilon) continue;
            TryConsumeConnectedFluidInputAtCoordinate(port.Coordinate, port.ItemId, requested,
                out float consumed, out float incomingTemperature);
            TryAddDedicatedFluidAtRuntimeCoordinate(port.Coordinate, port.ItemId,
                consumed, incomingTemperature, out _);
        }
    }

    private void BeginRefineryBatch()
    {
        batchDuration = ResolveInitialCraftDuration(ResolveInstalledDefinition());
        float total = 0f, weightedTemperature = 0f;
        // All inputs were verified before any is removed.
        for (int i = 0; i < inputPorts.Count; i++)
        {
            FluidFlowPort port = inputPorts[i];
            FluidInputBuffer buffer = GetOrCreateInputBuffer(port.ItemId);
            long units = System.Math.Min(System.Math.Max(0L, buffer.StoredUnits),
                DeterministicSimulationUnits.FromFloat(port.LitersPerSecond * batchDuration));
            float liters = DeterministicSimulationUnits.ToFloat(units);
            total += liters;
            weightedTemperature += liters * buffer.TemperatureCelsius;
            buffer.StoredUnits -= units;
        }
        batchTemperature = total > FluidEpsilon ? weightedTemperature / total
            : MapClimate.CurrentTemperatureCelsius;
        batchOutputs.Clear();
        for (int i = 0; i < outputPorts.Count; i++)
        {
            FluidFlowPort port = outputPorts[i];
            batchOutputs.Add(new RefineryOutputState
            {
                itemId = port.ItemId, litersPerSecond = port.LitersPerSecond,
                totalUnits = DeterministicSimulationUnits.FromFloat(port.LitersPerSecond * batchDuration)
            });
        }
        BeginActiveCraft(0, batchOutputs[0].itemId, 1, ResolveInstalledDefinition());
        MarkPersistenceStateDirty();
        NotifyFluidInputAvailabilityIncreased(this);
        Pipe.InvalidateFluidDisplayNetworkCache(this);
    }

    protected override bool TryCompleteActiveCraft()
    {
        if (!IsActiveCraftRunning || !IsWaitingForOutput) return false;
        bool pending = false;
        bool pressureChanged = false;
        float deltaTime = outputDeltaTime;
        outputDeltaTime = 0f;
        for (int i = 0; i < batchOutputs.Count; i++)
        {
            RefineryOutputState output = batchOutputs[i];
            if (output.remainingUnits < 0L)
            {
                output.remainingUnits = output.totalUnits;
                MarkPersistenceStateDirty();
            }
            if (output.remainingUnits > 0L && deltaTime > 0f
                && TryGetBatchOutputPort(output.itemId, out FluidFlowPort port))
            {
                float requested = output.litersPerSecond * deltaTime
                    * ResolveFluidOutputTransportRetentionAtCoordinate(
                        port.Coordinate, output.itemId, output.litersPerSecond);
                long requestedUnits = System.Math.Min(output.remainingUnits,
                    DeterministicSimulationUnits.FromFloat(requested));
                if (requestedUnits > 0L)
                {
                    TryEmitFluidOutputAtCoordinate(port.Coordinate, output.itemId,
                        DeterministicSimulationUnits.ToFloat(requestedUnits), batchTemperature,
                        out float accepted);
                    long acceptedUnits = System.Math.Min(requestedUnits,
                        DeterministicSimulationUnits.FromFloat(accepted));
                    if (acceptedUnits > 0L)
                    {
                        output.remainingUnits -= acceptedUnits;
                        pressureChanged |= output.remainingUnits == 0L;
                        emittedThisTick = true;
                        MarkPersistenceStateDirty();
                    }
                }
            }
            pending |= output.remainingUnits > 0L;
        }
        if (pressureChanged) Pipe.InvalidateFluidDisplayNetworkCache(this);
        if (pending) return false;
        batchOutputs.Clear();
        batchDuration = 0f;
        ClearActiveCraft();
        MarkPersistenceStateDirty();
        NotifyFluidOutputCapacityIncreased(this);
        Pipe.InvalidateFluidDisplayNetworkCache(this);
        return true;
    }

    private bool TryGetBatchOutputPort(int itemId, out FluidFlowPort port)
    {
        // Resolve against physical typed ports, so rotation/relocation and recipe
        // changes cannot route a saved batch into another fluid's outlet.
        if (TryGetPlacementRuntime(out Vector2Int anchor, out int turns))
        {
            IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
            for (int i = 0; i < placements.Count; i++)
            {
                RectGridBlockPlacement placement = placements[i];
                if ((placement.blockType == RectGridBlockType.PipeOutputItem
                     || placement.blockType == RectGridBlockType.DoublePipeOutputItem)
                    && placement.itemDefinition != null && placement.itemDefinition.id == itemId
                    && TryGetRectGridPlacementCoordinate(this, anchor, turns, placement, out Vector2Int coordinate))
                {
                    port = new FluidFlowPort(coordinate, placement.itemDefinition, 0f);
                    return true;
                }
            }
        }
        port = default;
        return false;
    }

    private bool TryGetInputPortAndBuffer(
        Vector2Int coordinate,
        out FluidFlowPort port,
        out FluidInputBuffer buffer)
    {
        port = default;
        buffer = null;
        if (!ResolveFluidFlowPorts())
        {
            return false;
        }

        for (int i = 0; i < inputPorts.Count; i++)
        {
            FluidFlowPort candidate = inputPorts[i];
            if (candidate.Coordinate != coordinate)
            {
                continue;
            }

            port = candidate;
            buffer = GetOrCreateInputBuffer(candidate.ItemId);
            return true;
        }

        return false;
    }

    private float GetConfiguredStoredInputLiters(FluidFlowPort port)
    {
        return StoredFluidItemId == port.ItemId
            ? StoredFluidLiters
            : 0f;
    }

    private FluidInputBuffer GetOrCreateInputBuffer(int itemId)
    {
        FluidInputBuffer buffer = FindInputBuffer(itemId);
        if (buffer != null)
        {
            return buffer;
        }

        buffer = new FluidInputBuffer(itemId);
        inputBuffers.Add(buffer);
        return buffer;
    }

    private FluidInputBuffer FindInputBuffer(int itemId)
    {
        for (int i = 0; i < inputBuffers.Count; i++)
        {
            FluidInputBuffer buffer = inputBuffers[i];
            if (buffer.ItemId == itemId)
            {
                return buffer;
            }
        }

        return null;
    }

    private float GetInputBufferCapacityLiters(FluidFlowPort port)
    {
        return port.LitersPerSecond * (IsActiveCraftRunning && batchDuration > 0f ? batchDuration
            : ResolveInitialCraftDuration(ResolveInstalledDefinition()));
    }

    private long GetAvailableInputUnits(FluidFlowPort port, FluidInputBuffer buffer)
    {
        long requiredUnits = DeterministicSimulationUnits.FromFloat(GetInputBufferCapacityLiters(port));
        return System.Math.Max(0L, requiredUnits - System.Math.Max(0L, buffer.StoredUnits));
    }

    private static float GetStoredLiters(FluidInputBuffer buffer) =>
        buffer != null
            ? DeterministicSimulationUnits.ToFloat(System.Math.Max(0L, buffer.StoredUnits))
            : 0f;

    private bool ResolveFluidFlowPorts()
    {
        return TryResolveFluidFlowPorts(true);
    }

    private bool TryResolveFluidFlowPorts(bool apply)
    {
        if (apply)
        {
            inputPorts.Clear();
            outputPorts.Clear();
        }
        if (!TryGetInputOutputPair(0, out InputOutputPair pair)
            || pair.inputs == null
            || pair.inputs.Count <= 0
            || pair.outputs == null
            || pair.outputs.Count <= 0
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < pair.inputs.Count; i++)
        {
            if (!TryResolveFluidPort(
                    pair.inputs[i],
                    placements,
                    anchorCoordinate,
                    quarterTurns,
                    true,
                    out FluidFlowPort port))
            {
                return false;
            }

            if (apply) inputPorts.Add(port);
        }

        for (int i = 0; i < pair.outputs.Count; i++)
        {
            if (!TryResolveFluidPort(
                    pair.outputs[i],
                    placements,
                    anchorCoordinate,
                    quarterTurns,
                    false,
                    out FluidFlowPort port))
            {
                return false;
            }

            if (apply) outputPorts.Add(port);
        }

        if (apply) SynchronizeInputBuffersWithPorts();
        return true;
    }

    private void SynchronizeInputBuffersWithPorts()
    {
        for (int bufferIndex = inputBuffers.Count - 1; bufferIndex >= 0; bufferIndex--)
        {
            int itemId = inputBuffers[bufferIndex].ItemId;
            bool stillConfigured = false;
            for (int portIndex = 0; portIndex < inputPorts.Count; portIndex++)
            {
                if (inputPorts[portIndex].ItemId == itemId)
                {
                    stillConfigured = true;
                    break;
                }
            }

            if (!stillConfigured)
            {
                inputBuffers.RemoveAt(bufferIndex);
            }
        }

        for (int i = 0; i < inputPorts.Count; i++)
        {
            GetOrCreateInputBuffer(inputPorts[i].ItemId);
        }
    }

    private bool TryResolveFluidPort(
        ItemIoEntry entry,
        IReadOnlyList<RectGridBlockPlacement> placements,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        bool input,
        out FluidFlowPort port)
    {
        port = default;
        if (!entry.IsFluid || entry.itemDefinition == null || entry.itemDefinition.id < 0)
        {
            return false;
        }

        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            bool matchingBlockType = input
                ? (placement.blockType == RectGridBlockType.PipeInputItem
                   || placement.blockType == RectGridBlockType.DoubleInputItem
                   || placement.blockType == RectGridBlockType.PipeInput)
                : (placement.blockType == RectGridBlockType.PipeOutputItem
                   || placement.blockType == RectGridBlockType.DoublePipeOutputItem);
            if (!matchingBlockType
                || placement.itemDefinition == null
                || placement.itemDefinition.id != entry.itemDefinition.id
                || !TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int coordinate))
            {
                continue;
            }

            port = new FluidFlowPort(coordinate, entry.itemDefinition, entry.ResolvedAmount);
            return true;
        }

        return false;
    }

    private static string ResolveFluidName(ItemDefinition definition)
    {
        if (definition == null)
        {
            return "fluid";
        }

        return string.IsNullOrWhiteSpace(definition.itemName)
            ? definition.name
            : definition.itemName;
    }

    private void SetRefineryStatus(RefineryState state, ItemDefinition definition = null)
    {
        int itemId = definition != null ? definition.id : -1;
        if (refineryState == state && refineryStatusItemId == itemId)
        {
            return;
        }

        if (state == RefineryState.NoEnergy || refineryState == RefineryState.NoEnergy)
            Pipe.InvalidateFluidDisplayNetworkCache(this);
        refineryState = state;
        refineryStatusItemId = itemId;
        refineryStatus = state switch
        {
            RefineryState.InvalidPorts => "Invalid fluid ports",
            RefineryState.MissingInput => $"Waiting for {ResolveFluidName(definition)}",
            RefineryState.NoEnergy => "No energy",
            RefineryState.Working => "Working",
            RefineryState.Outputting => "Outputting",
            RefineryState.WaitingForOutput => "Waiting for output",
            _ => "Idle"
        };
    }
}
