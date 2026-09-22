using System.Collections.Generic;
using UnityEngine;

public class CrudeOilRefinery : InputOutputModule
{
    private const float FluidEpsilon = 0.0001f;
    private const float InputBufferSeconds = 2f;
    private const float InputPressureRefreshIntervalSeconds = 0.25f;

    private enum RefineryState
    {
        Idle,
        InvalidPorts,
        MissingInput,
        InsufficientInput,
        NoEnergy,
        Working
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
        public float LastDeliveryTime = -1f;
        public float LastDeliveryLiters;
        public float ObservedSupplyLitersPerSecond;

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

    private readonly FluidTransferPreview inputTransferPreview = new FluidTransferPreview();
    private readonly List<FluidFlowPort> inputPorts = new List<FluidFlowPort>(2);
    private readonly List<FluidFlowPort> outputPorts = new List<FluidFlowPort>(3);
    private readonly List<FluidInputBuffer> inputBuffers = new List<FluidInputBuffer>(2);
    private readonly List<FluidInputPressureCache> inputPressureCaches =
        new List<FluidInputPressureCache>(2);
    private readonly HashSet<InputOutputModule> directInputPressureSources =
        new HashSet<InputOutputModule>();
    private bool isRefining;
    private float productionOpportunityRatio;
    private float throughputRatio;
    private RefineryState refineryState;
    private int refineryStatusItemId = -1;
    private string refineryStatus = "Idle";

    public bool IsRefining => isRefining;
    public float ObjectInfoThroughputRatio => throughputRatio;
    public int ObjectInfoInputCount => ResolveFluidFlowPorts() ? inputPorts.Count : 0;
    public int ObjectInfoOutputCount => ResolveFluidFlowPorts() ? outputPorts.Count : 0;

    public override float GetObjectInfoFluidPressureLitersPerSecond(int fluidItemId)
    {
        float deliveredRate = GetObjectInfoFluidOutputLitersPerSecond(fluidItemId);
        if (deliveredRate <= FluidEpsilon || !ResolveFluidFlowPorts())
        {
            return deliveredRate;
        }

        // The pipe applies its own distance loss while displaying pressure.
        // Recover the rate at this output from the volume measured at its tank.
        float retention = 0f;
        int matchingPorts = 0;
        for (int i = 0; i < outputPorts.Count; i++)
        {
            FluidFlowPort port = outputPorts[i];
            if (port.ItemId != fluidItemId)
            {
                continue;
            }

            matchingPorts++;
            retention = ResolveFluidOutputTransportRetentionAtCoordinate(
                port.Coordinate,
                fluidItemId);
        }

        return matchingPorts == 1
            ? retention > FluidEpsilon ? deliveredRate / retention : 0f
            : deliveredRate;
    }

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

        return state;
    }

    public override void ApplyPersistentState(PersistentState state)
    {
        StopRefineryParticle();
        base.ApplyPersistentState(state);
        inputBuffers.Clear();
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

        UpdateContinuousRefining(deltaTime);
        MarkManagedRuntimeVisualsDirty();
        RefreshRuntimeUpdateSleepState();
    }

    public override bool TryGetElectricPowerDemand(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        if (productionOpportunityRatio <= FluidEpsilon
            || !TryGetElectricPowerRequirement(out wattsPerSecond))
        {
            return false;
        }

        wattsPerSecond *= productionOpportunityRatio;
        return true;
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
            ? Mathf.Max(0f, GetInputBufferCapacityLiters(port) - GetStoredLiters(buffer))
            : 0f;
    }

    internal override bool CanAcceptDedicatedFluidAtRuntimeCoordinate(
        Vector2Int coordinate,
        int fluidItemId,
        float requestedLiters)
    {
        return TryGetInputPortAndBuffer(
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
        if (requestedLiters <= FluidEpsilon
            || !TryGetInputPortAndBuffer(
                coordinate,
                out FluidFlowPort port,
                out FluidInputBuffer buffer)
            || port.ItemId != fluidItemId)
        {
            return false;
        }

        long capacityUnits = DeterministicSimulationUnits.FromFloat(
            GetInputBufferCapacityLiters(port));
        long availableUnits = System.Math.Max(0L, capacityUnits - buffer.StoredUnits);
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
        RecordInputDelivery(buffer, acceptedLiters);
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

    protected override bool ShouldPlayWorkAnimation()
    {
        return false;
    }

    protected override void OnStoredFluidAccepted(
        int fluidItemId,
        float previousStoredLiters,
        float acceptedLiters,
        float incomingTemperatureCelsius)
    {
        base.OnStoredFluidAccepted(
            fluidItemId, previousStoredLiters, acceptedLiters, incomingTemperatureCelsius);
        if (fluidItemId >= 0 && acceptedLiters > FluidEpsilon)
        {
            RecordInputDelivery(GetOrCreateInputBuffer(fluidItemId), acceptedLiters);
        }
    }

    protected override void OnManagedRuntimeVisualsFlushed()
    {
        SetVisualParticleActive(particleEffect, isRefining);
    }

    private void StopRefineryParticle()
    {
        isRefining = false;
        throughputRatio = 0f;
        productionOpportunityRatio = 0f;
        SetVisualParticleActive(particleEffect, false, clear: true);
    }

    protected override string ResolveObjectInfoStatus(out bool isProducing)
    {
        isProducing = isRefining;
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
        int index,
        out int itemId,
        out float litersPerSecond,
        out bool isBlocked)
    {
        itemId = -1;
        litersPerSecond = 0f;
        isBlocked = true;
        if (!ResolveFluidFlowPorts() || index < 0 || index >= outputPorts.Count)
        {
            return false;
        }

        FluidFlowPort port = outputPorts[index];
        itemId = port.ItemId;
        litersPerSecond = port.LitersPerSecond;
        float transportRatio = ResolveFluidOutputTransportRetentionAtCoordinate(
            port.Coordinate,
            port.ItemId,
            port.LitersPerSecond);
        float requiredLiters = port.LitersPerSecond * ManagedUpdateTickIntervalSeconds
                               * transportRatio;
        float availableLiters = 0f;
        isBlocked = transportRatio <= FluidEpsilon
                    || requiredLiters > FluidEpsilon
                    && (!TryGetFluidOutputAvailableLitersAtCoordinate(
                            port.Coordinate,
                            port.ItemId,
                            requiredLiters,
                            out availableLiters)
                        || availableLiters + FluidEpsilon < requiredLiters);
        return itemId >= 0;
    }

    private void UpdateContinuousRefining(float deltaTime)
    {
        bool wasRefining = isRefining;
        isRefining = false;
        throughputRatio = 0f;
        productionOpportunityRatio = 0f;

        if (!Application.isPlaying || deltaTime <= 0f)
        {
            SetRefineryStatus(RefineryState.Idle);
            return;
        }

        if (!ResolveFluidFlowPorts())
        {
            SetRefineryStatus(RefineryState.InvalidPorts);
            return;
        }

        inputTransferPreview.Clear();
        float inputRatio = 1f;
        for (int i = 0; i < inputPorts.Count; i++)
        {
            FluidFlowPort port = inputPorts[i];
            float requestedLiters = port.LitersPerSecond * deltaTime;
            float localLiters = GetStoredLiters(GetOrCreateInputBuffer(port.ItemId))
                                + GetConfiguredStoredInputLiters(port);
            float availableLiters = localLiters;
            float connectedLiters = 0f;
            if (availableLiters + FluidEpsilon < requestedLiters)
            {
                TryGetConnectedFluidInputAvailableLitersAtCoordinate(
                    port.Coordinate,
                    port.ItemId,
                    requestedLiters - availableLiters,
                    out connectedLiters,
                    inputTransferPreview);
                availableLiters += connectedLiters;
            }

            if (requestedLiters <= FluidEpsilon || availableLiters <= FluidEpsilon)
            {
                SetRefineryStatus(RefineryState.MissingInput, port.Definition);
                return;
            }

            float inputPressure = GetOperationalInputPressure(port);
            if (!wasRefining && connectedLiters <= FluidEpsilon
                && localLiters + FluidEpsilon
                < GetStartupInputLiters(port, inputPressure, deltaTime))
            {
                SetRefineryStatus(RefineryState.InsufficientInput, port.Definition);
                return;
            }

            inputRatio = Mathf.Min(inputRatio, Mathf.Clamp01(availableLiters / requestedLiters));
            if (inputPressure > FluidEpsilon)
            {
                // Sources can deliver fluid in whole-liter bursts. Consume the
                // local buffer at the incoming flow rate so production stays steady.
                inputRatio = Mathf.Min(inputRatio,
                    Mathf.Clamp01(inputPressure / port.LitersPerSecond));
            }
        }

        productionOpportunityRatio = inputRatio;
        ItemDefinition installedDefinition = ResolveInstalledDefinition();
        float requestedEnergy = installedDefinition != null
            ? ItemDefinition.ResolveUseEnergyRatePerSecond(installedDefinition) * deltaTime * inputRatio
            : 0f;
        if (!TryConsumeOperatingEnergy(deltaTime * inputRatio, out float consumedEnergy))
        {
            SetRefineryStatus(RefineryState.NoEnergy);
            return;
        }

        float energyRatio = requestedEnergy > FluidEpsilon
            ? Mathf.Clamp01(consumedEnergy / requestedEnergy)
            : 1f;
        if (energyRatio <= FluidEpsilon)
        {
            SetRefineryStatus(RefineryState.NoEnergy);
            return;
        }

        float totalConsumedLiters = 0f;
        float weightedTemperature = 0f;
        float productionRatio = inputRatio * energyRatio;
        for (int i = 0; i < inputPorts.Count; i++)
        {
            FluidFlowPort port = inputPorts[i];
            float requestedLiters = port.LitersPerSecond * deltaTime * productionRatio;
            if (!TryConsumeInputPort(
                    port,
                    requestedLiters,
                    out float consumedLiters,
                    out float temperatureCelsius))
            {
                SetRefineryStatus(RefineryState.MissingInput, port.Definition);
                return;
            }

            totalConsumedLiters += consumedLiters;
            weightedTemperature += consumedLiters * temperatureCelsius;
        }

        float outputTemperature = totalConsumedLiters > FluidEpsilon
            ? weightedTemperature / totalConsumedLiters
            : MapClimate.CurrentTemperatureCelsius;
        for (int i = 0; i < outputPorts.Count; i++)
        {
            FluidFlowPort port = outputPorts[i];
            float requestedLiters = port.LitersPerSecond * deltaTime * productionRatio;
            // Each byproduct has its own transport limit. Unconnected, full or
            // incompatible receivers discard this output without stopping others.
            requestedLiters *= ResolveFluidOutputTransportRetentionAtCoordinate(
                port.Coordinate, port.ItemId, port.LitersPerSecond * productionRatio);
            TryEmitFluidOutputAtCoordinate(
                port.Coordinate,
                port.ItemId,
                requestedLiters,
                outputTemperature,
                out _);
        }

        throughputRatio = productionRatio;
        isRefining = true;
        SetRefineryStatus(RefineryState.Working);
    }

    private bool TryConsumeInputPort(
        FluidFlowPort port,
        float requestedLiters,
        out float consumedLiters,
        out float temperatureCelsius)
    {
        consumedLiters = 0f;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        FluidInputBuffer buffer = GetOrCreateInputBuffer(port.ItemId);
        float weightedTemperature = 0f;
        long requestedUnits = DeterministicSimulationUnits.FromFloat(requestedLiters);
        long bufferedUnits = System.Math.Min(
            System.Math.Max(0L, buffer.StoredUnits),
            requestedUnits);
        if (bufferedUnits > 0L)
        {
            float bufferedLiters = DeterministicSimulationUnits.ToFloat(bufferedUnits);
            consumedLiters += bufferedLiters;
            weightedTemperature += bufferedLiters * buffer.TemperatureCelsius;
            buffer.StoredUnits -= bufferedUnits;
            if (buffer.StoredUnits <= 0L)
            {
                buffer.StoredUnits = 0L;
                buffer.TemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
            }

            MarkPersistenceStateDirty();
            NotifyFluidOutputCapacityIncreased(this);
        }

        float remainingLiters = Mathf.Max(0f, requestedLiters - consumedLiters);
        if (remainingLiters > FluidEpsilon && GetConfiguredStoredInputLiters(port) > FluidEpsilon)
        {
            float storedTemperature = GetStoredFluidTemperatureCelsius(port.ItemId);
            if (TryConsumeFluidLiters(port.ItemId, remainingLiters, out float storedLiters))
            {
                consumedLiters += storedLiters;
                weightedTemperature += storedLiters * storedTemperature;
                remainingLiters = Mathf.Max(0f, requestedLiters - consumedLiters);
            }
        }

        if (remainingLiters > FluidEpsilon)
        {
            TryConsumeConnectedFluidInputAtCoordinate(
                port.Coordinate,
                port.ItemId,
                remainingLiters,
                out float connectedLiters,
                out float connectedTemperature);
            consumedLiters += connectedLiters;
            weightedTemperature += connectedLiters * connectedTemperature;
        }

        if (consumedLiters > FluidEpsilon)
        {
            temperatureCelsius = weightedTemperature / consumedLiters;
        }

        return consumedLiters + FluidEpsilon >= requestedLiters;
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
            long capacityUnits = DeterministicSimulationUnits.FromFloat(
                GetInputBufferCapacityLiters(candidate));
            buffer.StoredUnits = System.Math.Min(
                System.Math.Max(0L, buffer.StoredUnits),
                capacityUnits);
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
        float configuredLiters = FluidStorageCapacityLiters;
        if (configuredLiters > FluidEpsilon)
        {
            float totalInputRate = 0f;
            for (int i = 0; i < inputPorts.Count; i++)
            {
                totalInputRate += inputPorts[i].LitersPerSecond;
            }

            if (totalInputRate > FluidEpsilon)
            {
                return Mathf.Max(0f,
                    configuredLiters * port.LitersPerSecond / totalInputRate);
            }
        }

        return Mathf.Max(1f, port.LitersPerSecond * InputBufferSeconds);
    }

    private float GetOperationalInputPressure(FluidFlowPort port)
    {
        float pressure = GetObjectInfoInputPressure(port);
        return pressure > FluidEpsilon
            ? pressure
            : GetOrCreateInputBuffer(port.ItemId).ObservedSupplyLitersPerSecond;
    }

    private float GetStartupInputLiters(
        FluidFlowPort port, float inputPressure, float deltaTime)
    {
        float fillRate = inputPressure > FluidEpsilon
            ? inputPressure
            : port.LitersPerSecond;
        return Mathf.Min(
            GetInputBufferCapacityLiters(port),
            Mathf.Max(port.LitersPerSecond * deltaTime,
                Mathf.Min(2f, fillRate * InputBufferSeconds)));
    }

    private static void RecordInputDelivery(FluidInputBuffer buffer, float liters)
    {
        float now = (float)MapObjectTickManager.CurrentSimulationTimeSeconds;
        float elapsed = now - buffer.LastDeliveryTime;
        if (buffer.LastDeliveryTime >= 0f && elapsed >= 0f && elapsed <= FluidEpsilon)
        {
            buffer.LastDeliveryLiters += liters;
            return;
        }

        if (buffer.LastDeliveryTime >= 0f && elapsed > FluidEpsilon)
        {
            buffer.ObservedSupplyLitersPerSecond = buffer.LastDeliveryLiters / elapsed;
        }

        buffer.LastDeliveryLiters = liters;
        buffer.LastDeliveryTime = now;
    }

    private static float GetStoredLiters(FluidInputBuffer buffer) =>
        buffer != null
            ? DeterministicSimulationUnits.ToFloat(System.Math.Max(0L, buffer.StoredUnits))
            : 0f;

    private bool ResolveFluidFlowPorts()
    {
        inputPorts.Clear();
        outputPorts.Clear();
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

            inputPorts.Add(port);
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

            outputPorts.Add(port);
        }

        if (inputPorts.Count <= 0 || outputPorts.Count <= 0)
        {
            return false;
        }

        SynchronizeInputBuffersWithPorts();
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

        refineryState = state;
        refineryStatusItemId = itemId;
        refineryStatus = state switch
        {
            RefineryState.InvalidPorts => "Invalid fluid ports",
            RefineryState.MissingInput => $"No {ResolveFluidName(definition)}",
            RefineryState.InsufficientInput => $"Insufficient {ResolveFluidName(definition)}",
            RefineryState.NoEnergy => "No energy",
            RefineryState.Working => "Working",
            _ => "Idle"
        };
    }
}
