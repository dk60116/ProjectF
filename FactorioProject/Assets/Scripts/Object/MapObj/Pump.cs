using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Transfers fluid from the local -X inlet to the local +X outlet.
/// The pump does not own fluid; producers and consumers still transfer the
/// authoritative fluid directly through the connected network.
/// </summary>
public class Pump : InputOutputModule
{
    public float PressureLitersPerSecond => ResolveInstalledDefinition() is ItemDefinition definition
        ? definition.PumpPressureLitersPerSecond : 5f;

    // A pump changes transport capacity only; it never changes a producer's budget.
    internal static Pump ResolvePressureLimit(Pump current, Pump candidate)
    {
        return candidate != null && (current == null || candidate.PressureLitersPerSecond < current.PressureLitersPerSecond
                || candidate.PressureLitersPerSecond == current.PressureLitersPerSecond
                && candidate.RuntimePlacementSequence < current.RuntimePlacementSequence)
            ? candidate : current;
    }

    internal static float LimitTransportRate(Pump pump, float sourceRate)
    {
        return pump != null ? Mathf.Min(sourceRate, pump.PressureLitersPerSecond) : sourceRate;
    }

    private long pressureBudgetTick = -1;
    private double pressureBudgetLiters;

    protected override void OnEnable()
    {
        pressureBudgetTick = -1;
        pressureBudgetLiters = 0d;
        base.OnEnable();
    }

    internal float LimitTransferVolume(float requestedLiters, float deltaTime)
    {
        long tick = MapObjectTickManager.CurrentSimulationTick;
        double window = System.Math.Max(0d, deltaTime);
        double capacity = PressureLitersPerSecond * window;
        if (pressureBudgetTick != tick)
        {
            double elapsed = pressureBudgetTick < 0 || tick < pressureBudgetTick
                ? window
                : (tick - pressureBudgetTick) * (double)MapObjectTickManager.FixedSimulationDeltaSeconds;
            pressureBudgetLiters = System.Math.Min(capacity,
                System.Math.Max(0d, pressureBudgetLiters) + PressureLitersPerSecond * elapsed);
            pressureBudgetTick = tick;
        }
        pressureBudgetLiters = System.Math.Min(pressureBudgetLiters, capacity);
        return Mathf.Min(Mathf.Max(0f, requestedLiters), (float)System.Math.Max(0d, pressureBudgetLiters));
    }

    internal void RecordTransferredVolume(float acceptedLiters)
    {
        pressureBudgetLiters = System.Math.Max(0d, pressureBudgetLiters - Mathf.Max(0f, acceptedLiters));
    }

    private readonly Pipe.FluidNetworkSearchContext objectInfoNetworkContext =
        new Pipe.FluidNetworkSearchContext();
    private readonly List<Vector2Int> fluidDockSeedCoordinates = new List<Vector2Int>(2);
    private readonly List<InstallationObject> fluidDockStorageScratch =
        new List<InstallationObject>(4);
    private float plannedFluidDockDeltaTime;

    public override void PlanManagedUpdateTick(float deltaTime)
    {
        base.PlanManagedUpdateTick(deltaTime);
        plannedFluidDockDeltaTime = Mathf.Max(0f, deltaTime);
    }

    public override void ApplyManagedUpdateTick()
    {
        float deltaTime = plannedFluidDockDeltaTime;
        plannedFluidDockDeltaTime = 0f;
        base.ApplyManagedUpdateTick();
        TryUnloadDockedFluid(deltaTime);
    }

    protected override bool ShouldKeepRuntimeUpdateTickActive()
    {
        return base.ShouldKeepRuntimeUpdateTickActive() || HasDockedFluidStorage();
    }

    private bool TryUnloadDockedFluid(float deltaTime)
    {
        if (deltaTime <= 0f
            || !TryResolveDockedFluidSource(
                out InstallationObject sourceStorage,
                out int fluidItemId))
        {
            return false;
        }

        if (!TryGetRuntimeFluidEndpoints(out _, out Vector2Int outputCoordinate)) return false;
        fluidDockSeedCoordinates.Clear();
        fluidDockSeedCoordinates.Add(ResolveRuntimeFluidDeliveryCoordinate(outputCoordinate));
        float requestedLiters = PressureLitersPerSecond * deltaTime;
        float temperatureCelsius = sourceStorage.GetStoredFluidTemperatureCelsius(fluidItemId);
        return TryTransferFluidFromStorageToConnectedStorage(
            sourceStorage,
            fluidItemId,
            requestedLiters,
            temperatureCelsius,
            fluidDockSeedCoordinates,
            out _);
    }

    private bool TryResolveDockedFluidSource(
        out InstallationObject sourceStorage,
        out int fluidItemId)
    {
        sourceStorage = null;
        fluidItemId = -1;
        if (!CollectFluidDockSeedCoordinates())
        {
            return false;
        }

        Fluidtank bestMountedTank = null;
        for (int i = 0; i < fluidDockSeedCoordinates.Count; i++)
        {
            Vector2Int dockCoordinate = fluidDockSeedCoordinates[i];
            if (!AllowsRuntimeFluidTraversal(dockCoordinate, false)) continue;
            if (!TryGetRuntimePipePass(
                    dockCoordinate,
                    out _,
                    out Vector2Int externalDirection))
            {
                continue;
            }

            Vector2Int directionFromVehicleToPump = -externalDirection;
            fluidDockStorageScratch.Clear();
            CollectActiveInstallationsAtRuntimeGridCoordinate(
                dockCoordinate,
                fluidDockStorageScratch);
            for (int storageIndex = 0;
                 storageIndex < fluidDockStorageScratch.Count;
                 storageIndex++)
            {
                if (fluidDockStorageScratch[storageIndex] is not Fluidtank candidate
                    || candidate.StoredFluidItemId < 0
                    || !candidate.CanProvideMountedFluidToPump(
                        this,
                        dockCoordinate,
                        directionFromVehicleToPump,
                        candidate.StoredFluidItemId,
                        0.0001f)
                    || bestMountedTank != null
                    && bestMountedTank.RuntimePlacementSequence
                    <= candidate.RuntimePlacementSequence)
                {
                    continue;
                }

                bestMountedTank = candidate;
            }
        }

        fluidDockStorageScratch.Clear();
        sourceStorage = bestMountedTank;
        fluidItemId = bestMountedTank != null ? bestMountedTank.StoredFluidItemId : -1;
        return sourceStorage != null;
    }

    private bool HasDockedFluidStorage()
    {
        if (!CollectFluidDockSeedCoordinates())
        {
            return false;
        }

        for (int i = 0; i < fluidDockSeedCoordinates.Count; i++)
        {
            Vector2Int coordinate = fluidDockSeedCoordinates[i];
            if (!AllowsRuntimeFluidTraversal(coordinate, false))
            {
                continue;
            }

            fluidDockStorageScratch.Clear();
            CollectActiveInstallationsAtRuntimeGridCoordinate(
                coordinate,
                fluidDockStorageScratch);
            for (int storageIndex = 0;
                 storageIndex < fluidDockStorageScratch.Count;
                 storageIndex++)
            {
                if (fluidDockStorageScratch[storageIndex] is Fluidtank tank
                    && tank.IsFlatCarMounted)
                {
                    fluidDockStorageScratch.Clear();
                    return true;
                }
            }
        }

        fluidDockStorageScratch.Clear();
        return false;
    }

    private bool CollectFluidDockSeedCoordinates()
    {
        fluidDockSeedCoordinates.Clear();
        if (!TryGetPlacementRuntime(
                out Vector2Int anchorCoordinate,
                out int quarterTurns))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.PipeInput
                || !TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int coordinate)
                || fluidDockSeedCoordinates.Contains(coordinate))
            {
                continue;
            }

            fluidDockSeedCoordinates.Add(coordinate);
        }

        return fluidDockSeedCoordinates.Count > 0;
    }

    public bool TryGetObjectInfoFluidInfo(
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        pressureLitersPerSecond = 0f;
        if (!isActiveAndEnabled || !TryGetRuntimeFluidEndpoints(out _, out Vector2Int outputCoordinate))
        {
            return false;
        }

        // Report delivery on the outlet; never report boosted pressure on the inlet.
        return Pipe.TryGetNetworkFluidInfoAt(
            outputCoordinate, objectInfoNetworkContext, false, default, true,
            out fluidItemId, out temperatureCelsius, out pressureLitersPerSecond);
    }

    internal bool TryGetRuntimeInterlockedEndpoint(
        Pump otherPump,
        Vector2Int otherPumpEndpoint,
        out Vector2Int endpoint)
    {
        endpoint = default;
        if (otherPump == null
            || otherPump == this
            || !gameObject.activeInHierarchy
            || !TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !otherPump.TryGetPlacementRuntime(out Vector2Int otherAnchorCoordinate, out int otherQuarterTurns))
        {
            return false;
        }

        return TryGetInterlockedEndpointAt(
            this, anchorCoordinate, quarterTurns,
            otherPump, otherPump, otherAnchorCoordinate, otherQuarterTurns,
            otherPumpEndpoint, out endpoint);
    }

    internal bool TryGetInterlockedEndpointAt(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Pump otherPump,
        MapObject otherFootprintSource,
        Vector2Int otherAnchorCoordinate,
        int otherQuarterTurns,
        Vector2Int otherPumpEndpoint,
        out Vector2Int endpoint)
    {
        endpoint = default;
        if (otherPump == null
            || !TryGetRectGridBlockTypeAtCoordinate(
                footprintSource, anchorCoordinate, quarterTurns, otherPumpEndpoint,
                out RectGridBlockType ownBlockType)
            || ownBlockType != RectGridBlockType.Object
            || !otherPump.TryGetPipePassExternalDirection(
                otherFootprintSource, otherAnchorCoordinate, otherQuarterTurns,
                otherPumpEndpoint, out Vector2Int otherExternalDirection))
        {
            return false;
        }

        // Flush pump bodies share no Object cell. Each facing PipeInput marker
        // overlaps the other pump's end Object cell, one grid step apart.
        Vector2Int candidateEndpoint = otherPumpEndpoint - otherExternalDirection;
        if (!TryGetPipePassExternalDirection(
                footprintSource, anchorCoordinate, quarterTurns,
                candidateEndpoint, out Vector2Int externalDirection)
            || externalDirection != -otherExternalDirection
            || !otherPump.TryGetRectGridBlockTypeAtCoordinate(
                otherFootprintSource, otherAnchorCoordinate, otherQuarterTurns,
                candidateEndpoint, out RectGridBlockType otherBlockType)
            || otherBlockType != RectGridBlockType.Object)
        {
            return false;
        }

        endpoint = candidateEndpoint;
        return true;
    }

    // The authored footprint runs from the lower local X/Y inlet cell to the
    // higher local X/Y outlet cell. Placement rotation transforms both together.
    internal bool TryGetRuntimeFluidEndpoints(out Vector2Int input, out Vector2Int output)
    {
        input = output = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchor, out int turns)) return false;
        int first = -1, last = -1, count = 0;
        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement cell = placements[i];
            if (cell.blockType != RectGridBlockType.PipeInput) continue;
            count++;
            if (first < 0 || cell.x < placements[first].x
                || cell.x == placements[first].x && cell.y < placements[first].y) first = i;
            if (last < 0 || cell.x > placements[last].x
                || cell.x == placements[last].x && cell.y > placements[last].y) last = i;
        }
        return count == 2
               && TryGetRectGridPlacementCoordinate(this, anchor, turns, placements[first], out input)
               && TryGetRectGridPlacementCoordinate(this, anchor, turns, placements[last], out output);
    }

    internal bool AllowsRuntimeFluidTraversal(Vector2Int coordinate, bool upstream)
    {
        if (!TryGetRuntimeFluidEndpoints(out Vector2Int input, out Vector2Int output)) return false;
        if (coordinate != input && coordinate != output)
        {
            if (!TryGetPlacementRuntime(out Vector2Int anchor, out int turns)
                || !TryGetBodyPipePassEndpointAt(this, anchor, turns, coordinate, out coordinate, out _))
                return false;
        }
        return coordinate == (upstream ? output : input);
    }

    public bool TryGetRuntimePipePass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !TryGetPipePassAt(this, anchorCoordinate, quarterTurns, coordinate,
                out otherCoordinate, out externalDirection))
        {
            return false;
        }

        // An end body installed on a machine input is a real delivery node as
        // well as a source-search entrance. Keep both searches on that port.
        bool connects = TryGetPipePassExternalDirection(
                            this, anchorCoordinate, quarterTurns, coordinate, out _)
                        || HasRuntimeFluidInputFacingAt(coordinate, externalDirection);
        if (connects && AllowsRuntimeFluidTraversal(otherCoordinate, true))
        {
            otherCoordinate = ResolveRuntimeFluidDeliveryCoordinate(otherCoordinate);
        }
        return connects;
    }

    internal Vector2Int ResolveRuntimeFluidDeliveryCoordinate(Vector2Int outputCoordinate)
    {
        if (TryGetPlacementRuntime(out Vector2Int anchor, out int turns)
            && TryGetPipePassExternalDirection(this, anchor, turns, outputCoordinate, out Vector2Int direction))
        {
            Vector2Int bodyCoordinate = outputCoordinate - direction;
            if (HasRuntimeFluidInputFacingAt(bodyCoordinate, direction)) return bodyCoordinate;
        }
        return outputCoordinate;
    }

    internal bool TryGetPipePassAt(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        Vector2Int endpointCoordinate = coordinate;
        if (!TryGetPipePassExternalDirection(
                footprintSource,
                anchorCoordinate,
                quarterTurns,
                coordinate,
                out externalDirection)
            && !TryGetBodyPipePassEndpointAt(
                footprintSource, anchorCoordinate, quarterTurns, coordinate,
                out endpointCoordinate, out externalDirection))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.PipeInput
                || !TryGetRectGridPlacementCoordinate(
                    footprintSource != null ? footprintSource : this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int candidateCoordinate)
                || candidateCoordinate == endpointCoordinate)
            {
                continue;
            }

            otherCoordinate = candidateCoordinate;
            return true;
        }

        return false;
    }

    // The end Object cell can occupy another facility's input area. It is an
    // alias of the adjacent PipePass, not a side port or a separate fluid store.
    internal bool TryGetBodyPipePassEndpointAt(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out Vector2Int endpointCoordinate,
        out Vector2Int externalDirection)
    {
        endpointCoordinate = externalDirection = default;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        if (!TryGetRectGridBlockTypeAtCoordinate(
                anchorSource, anchorCoordinate, quarterTurns, coordinate,
                out RectGridBlockType blockType)
            || blockType != RectGridBlockType.Object)
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType == RectGridBlockType.PipeInput
                && TryGetRectGridPlacementCoordinate(anchorSource, anchorCoordinate, quarterTurns,
                    placement, out Vector2Int endpoint)
                && TryGetPipePassExternalDirection(anchorSource, anchorCoordinate, quarterTurns,
                    endpoint, out Vector2Int direction)
                && endpoint - direction == coordinate)
            {
                endpointCoordinate = endpoint;
                externalDirection = direction;
                return true;
            }
        }

        return false;
    }

    public bool TryGetPipePassExternalDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out Vector2Int externalDirection)
    {
        externalDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        return TryGetRectGridBlockTypeAtCoordinate(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   coordinate,
                   out RectGridBlockType blockType)
               && blockType == RectGridBlockType.PipeInput
               && TryGetNearestRectGridObjectDirection(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   coordinate,
                   out Vector2Int inwardDirection)
               && (externalDirection = -inwardDirection) != Vector2Int.zero;
    }

    internal static bool TryResolvePipePassConnectionDirection(
        Vector2Int pipeCoordinate,
        Vector2Int endpointCoordinate,
        Vector2Int externalDirection,
        out Vector2Int pipeConnectionDirection)
    {
        pipeConnectionDirection = endpointCoordinate - pipeCoordinate;
        if (pipeConnectionDirection == Vector2Int.zero)
        {
            pipeConnectionDirection = -externalDirection;
        }

        return externalDirection != Vector2Int.zero
               && pipeConnectionDirection == -externalDirection;
    }
}
