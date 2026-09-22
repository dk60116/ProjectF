using System.Collections.Generic;
using UnityEngine;

public class Fluidtank : InstallationObject, IMapObjectUpdateTick, IMapObjectUpdateTickInterval
{
    private const float PipeDirectionEpsilon = 0.0001f;
    private const float FluidFillRatioEpsilon = 0.001f;
    private const float FluidTankUpdateIntervalSeconds = 0.1f;
    private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorShaderId = Shader.PropertyToID("_Color");

    [SerializeField]
    private MeshRenderer fluidColor;

    private MaterialPropertyBlock fluidColorPropertyBlock;
    private int displayedFluidColorItemId = int.MinValue;

    private static readonly Vector2Int[] FluidCardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private static readonly HashSet<Fluidtank> ActiveFluidTanks = new HashSet<Fluidtank>();
    private static int fluidNetworkTopologyVersion = 1;

    [SerializeField, Tooltip("탱크 측면 연결 파이프입니다. 목록 순서와 무관하게 탱크 로컬 위치로 방향을 판정합니다.")]
    private List<GameObject> pipeList = new List<GameObject>();
    [SerializeField, Tooltip("FlatCar 적재 시 숨길 다리 오브젝트입니다.")]
    private GameObject legs;
    [SerializeField, Min(0f), Tooltip("FlatCar 적재 시 탱크를 아래로 내릴 로컬 높이입니다.")]
    private float flatCarMountedLowering = 0.1f;
    [SerializeField, Min(0f), Tooltip("FlatCar 적재 시 일반 파이프와 도킹할 측면 파이프의 탱크 로컬 높이입니다.")]
    private float flatCarMountedPipeHeight = 0.199f;
    [SerializeField, Min(0f), Tooltip("FlatCar 적재 파이프가 탱크 안쪽에서 바깥으로 전개되는 거리입니다.")]
    private float flatCarMountedPipeRetractDistance = 0.3f;
    [SerializeField, Min(0.01f), Tooltip("FlatCar 적재 파이프의 전개/복귀 보간 속도입니다.")]
    private float flatCarMountedPipeInterpolationSpeed = 8f;
    [SerializeField, Min(0f), Tooltip("FlatCar가 멈춘 뒤 측면 파이프 전개를 시작할 때까지의 대기 시간입니다.")]
    private float flatCarMountedPipeDeployDelay = 1f;
    [SerializeField, Min(0f), Tooltip("FlatCar 적재 탱크 파이프가 도킹 지점으로 판정되는 최대 수평 오차입니다.")]
    private float flatCarMountedPipeDockingTolerance = 0.2f;

    private readonly List<InstallationObject> adjacentInstallationScratch = new List<InstallationObject>(4);
    private readonly List<InputOutputModule> adjacentModuleScratch = new List<InputOutputModule>(2);
    private readonly List<InputOutputModule.RuntimePumpPipePass> mountedPumpPipePassScratch =
        new List<InputOutputModule.RuntimePumpPipePass>(2);
    private readonly HashSet<int> adjacentOutputFluidItemIdsScratch = new HashSet<int>();
    private readonly List<Fluidtank> connectedTankCache = new List<Fluidtank>(4);
    private readonly List<Fluidtank> connectedTankDependents = new List<Fluidtank>(4);
    private readonly List<InputOutputModule.RuntimePumpPipePass> connectedPumpPassScratch =
        new List<InputOutputModule.RuntimePumpPipePass>(2);
    private readonly Pipe.FluidNetworkSearchContext connectedFluidIdentityContext =
        new Pipe.FluidNetworkSearchContext();
    private readonly Dictionary<Vector2Int, Pump> fluidNetworkSearchPumps = new Dictionary<Vector2Int, Pump>();
    private readonly Dictionary<Fluidtank, Pump> connectedTankPumps = new Dictionary<Fluidtank, Pump>();
    private Pump fluidNetworkSearchCurrentPump;

    private readonly struct FluidNetworkSearchNode
    {
        public readonly Vector2Int Coordinate;
        public readonly int PipeCount;

        public FluidNetworkSearchNode(Vector2Int coordinate, int pipeCount)
        {
            Coordinate = coordinate;
            PipeCount = pipeCount;
        }
    }

    private readonly Queue<FluidNetworkSearchNode> fluidNetworkSearchQueue =
        new Queue<FluidNetworkSearchNode>();
    private readonly Dictionary<Vector2Int, int> fluidNetworkSearchPipeCounts =
        new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Fluidtank, int> connectedTankPipeDistances =
        new Dictionary<Fluidtank, int>();
    private readonly List<Vector3> defaultPipeLocalPositions = new List<Vector3>(4);
    private readonly List<bool> mountedPipeTargetActiveStates = new List<bool>(4);
    private readonly List<bool> mountedPipeTransferReadyStates = new List<bool>(4);
    private int connectedTankCacheTopologyVersion;
    private bool hasCachedDefaultPipeLocalPositions;
    private bool hasFlatCarMountedPresentationState;
    private bool isFlatCarMountedPresentation;
    private FreightCar mountedFreightCar;
    private bool runtimeTickSleeping;

    public float ManagedUpdateTickIntervalSeconds => FluidTankUpdateIntervalSeconds;
    public Vector3 FlatCarMountedLocalPosition => Vector3.down * flatCarMountedLowering;
    public bool IsFlatCarMounted => isFlatCarMountedPresentation;

    private bool HasMountedPipeTransferReady
    {
        get
        {
            if (!isFlatCarMountedPresentation)
            {
                return true;
            }

            for (int i = 0; i < mountedPipeTransferReadyStates.Count; i++)
            {
                if (mountedPipeTransferReadyStates[i])
                {
                    return true;
                }
            }

            return false;
        }
    }

    public override bool CanAcceptFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        return HasMountedPipeTransferReady
               && ShouldFillMountedFluidAtCurrentStop()
               && base.CanAcceptFluidItem(fluidItemId, requestedLiters);
    }

    public override bool CanProvideFluidItem(int fluidItemId, float requestedLiters = 0f)
    {
        return HasMountedPipeTransferReady
               && ShouldUnloadMountedFluidAtCurrentStop()
               && base.CanProvideFluidItem(fluidItemId, requestedLiters);
    }

    private bool ShouldFillMountedFluidAtCurrentStop()
    {
        return !isFlatCarMountedPresentation
               || !TryGetMountedConsistSteamTrain(out SteamTrain steamTrain)
               || steamTrain.ShouldFillFluidAtCurrentStop();
    }

    private bool ShouldUnloadMountedFluidAtCurrentStop()
    {
        return !isFlatCarMountedPresentation
               || TryGetMountedConsistSteamTrain(out SteamTrain steamTrain)
               && steamTrain.ShouldUnloadFluidAtCurrentStop();
    }

    private bool TryGetMountedConsistSteamTrain(out SteamTrain steamTrain)
    {
        steamTrain = null;
        if (!isFlatCarMountedPresentation)
        {
            return false;
        }

        if (mountedFreightCar == null
            || !transform.IsChildOf(mountedFreightCar.transform))
        {
            mountedFreightCar = GetComponentInParent<FreightCar>();
        }

        return mountedFreightCar != null
               && mountedFreightCar.TryGetConsistSteamTrain(out steamTrain);
    }

    public void SetFlatCarMountedPresentation(bool mounted)
    {
        if (hasFlatCarMountedPresentationState && isFlatCarMountedPresentation == mounted)
        {
            return;
        }

        if (legs != null)
        {
            bool shouldShowLegs = !mounted;
            if (legs.activeSelf != shouldShowLegs)
            {
                legs.SetActive(shouldShowLegs);
            }
        }

        CacheDefaultPipeLocalPositions();
        hasFlatCarMountedPresentationState = true;
        isFlatCarMountedPresentation = mounted;
        mountedFreightCar = mounted ? GetComponentInParent<FreightCar>() : null;
        ApplyFlatCarMountedPipePresentationImmediate(mounted);
        RefreshPipeVisuals();
        if (Application.isPlaying)
        {
            InputOutputModule.NotifyRuntimePipeTopologyChanged(RuntimeOccupiedCoordinates);
        }
    }

    private void CacheDefaultPipeLocalPositions()
    {
        if (hasCachedDefaultPipeLocalPositions)
        {
            return;
        }

        defaultPipeLocalPositions.Clear();
        if (pipeList != null)
        {
            for (int i = 0; i < pipeList.Count; i++)
            {
                GameObject pipeVisual = pipeList[i];
                defaultPipeLocalPositions.Add(
                    pipeVisual != null ? pipeVisual.transform.localPosition : Vector3.zero);
            }
        }

        hasCachedDefaultPipeLocalPositions = true;
    }

    private void ApplyFlatCarMountedPipePresentationImmediate(bool mounted)
    {
        if (pipeList == null)
        {
            return;
        }

        EnsureMountedPipeTargetStateCapacity();
        ResetMountedPipeTransferReadiness();
        int pipeCount = Mathf.Min(pipeList.Count, defaultPipeLocalPositions.Count);
        for (int i = 0; i < pipeCount; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (pipeVisual == null)
            {
                continue;
            }

            if (mounted)
            {
                mountedPipeTargetActiveStates[i] = pipeVisual.activeSelf;
                pipeVisual.transform.localPosition = GetMountedPipeRetractedLocalPosition(i);
                continue;
            }

            mountedPipeTargetActiveStates[i] = false;
            pipeVisual.transform.localPosition = defaultPipeLocalPositions[i];
        }
    }

    public void UpdateFlatCarMountedPipeVisuals(float deltaTime, float carrierStationarySeconds)
    {
        if (!isFlatCarMountedPresentation || pipeList == null)
        {
            return;
        }

        CacheDefaultPipeLocalPositions();
        EnsureMountedPipeTargetStateCapacity();
        float interpolation = deltaTime > 0f
            ? 1f - Mathf.Exp(-Mathf.Max(0.01f, flatCarMountedPipeInterpolationSpeed) * deltaTime)
            : 1f;
        bool deploymentReady = carrierStationarySeconds >= Mathf.Max(0f, flatCarMountedPipeDeployDelay);
        bool hasPlacement = TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _);
        bool readinessChanged = false;
        int pipeCount = Mathf.Min(pipeList.Count, defaultPipeLocalPositions.Count);
        for (int i = 0; i < pipeCount; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (pipeVisual == null || pipeVisual == gameObject)
            {
                readinessChanged |= SetMountedPipeTransferReady(i, false);
                continue;
            }

            bool withinDockingRange = hasPlacement
                                      && TryResolvePipeVisualDirection(pipeVisual, out Vector2Int direction)
                                      && IsMountedPipeWithinDockingRange(
                                          i,
                                          pipeVisual,
                                          anchorCoordinate,
                                          direction);
            bool targetActive = mountedPipeTargetActiveStates[i]
                                && deploymentReady
                                && withinDockingRange;
            if (!pipeVisual.activeSelf)
            {
                if (!targetActive)
                {
                    readinessChanged |= SetMountedPipeTransferReady(i, false);
                    continue;
                }

                pipeVisual.transform.localPosition = GetMountedPipeRetractedLocalPosition(i);
                pipeVisual.SetActive(true);
            }

            Vector3 targetPosition = targetActive
                ? GetMountedPipeExtendedLocalPosition(i)
                : GetMountedPipeRetractedLocalPosition(i);
            pipeVisual.transform.localPosition = Vector3.Lerp(
                pipeVisual.transform.localPosition,
                targetPosition,
                interpolation);
            if ((pipeVisual.transform.localPosition - targetPosition).sqrMagnitude > 0.000001f)
            {
                readinessChanged |= SetMountedPipeTransferReady(i, false);
                continue;
            }

            pipeVisual.transform.localPosition = targetPosition;
            readinessChanged |= SetMountedPipeTransferReady(i, targetActive);
            if (!targetActive)
            {
                pipeVisual.SetActive(false);
            }
        }

        if (readinessChanged)
        {
            NotifyMountedPipeTransferReadinessChanged();
        }
    }

    private void SetMountedPipeTarget(int index, GameObject pipeVisual, bool connected)
    {
        EnsureMountedPipeTargetStateCapacity();
        if (index < 0
            || index >= mountedPipeTargetActiveStates.Count
            || pipeVisual == null
            || pipeVisual == gameObject)
        {
            return;
        }

        mountedPipeTargetActiveStates[index] = connected;
        if (!connected)
        {
            mountedPipeTransferReadyStates[index] = false;
        }
        if (connected && !pipeVisual.activeSelf)
        {
            pipeVisual.transform.localPosition = GetMountedPipeRetractedLocalPosition(index);
            pipeVisual.SetActive(true);
        }
        else if (!connected && !pipeVisual.activeSelf)
        {
            pipeVisual.transform.localPosition = GetMountedPipeRetractedLocalPosition(index);
        }
    }

    private Vector3 GetMountedPipeExtendedLocalPosition(int index)
    {
        Vector3 localPosition = index >= 0 && index < defaultPipeLocalPositions.Count
            ? defaultPipeLocalPositions[index]
            : Vector3.zero;
        localPosition.y = flatCarMountedPipeHeight;
        return localPosition;
    }

    private Vector3 GetMountedPipeRetractedLocalPosition(int index)
    {
        Vector3 extendedPosition = GetMountedPipeExtendedLocalPosition(index);
        Vector3 outwardDirection = new Vector3(extendedPosition.x, 0f, extendedPosition.z);
        float outwardDistance = outwardDirection.magnitude;
        if (outwardDistance <= PipeDirectionEpsilon)
        {
            return extendedPosition;
        }

        outwardDirection /= outwardDistance;
        float retractDistance = Mathf.Min(
            Mathf.Max(0f, flatCarMountedPipeRetractDistance),
            Mathf.Max(0f, outwardDistance - PipeDirectionEpsilon));
        return extendedPosition - outwardDirection * retractDistance;
    }

    private void EnsureMountedPipeTargetStateCapacity()
    {
        int pipeCount = pipeList != null ? pipeList.Count : 0;
        while (mountedPipeTargetActiveStates.Count < pipeCount)
        {
            mountedPipeTargetActiveStates.Add(false);
            mountedPipeTransferReadyStates.Add(false);
        }

        while (mountedPipeTargetActiveStates.Count > pipeCount)
        {
            mountedPipeTargetActiveStates.RemoveAt(mountedPipeTargetActiveStates.Count - 1);
            mountedPipeTransferReadyStates.RemoveAt(mountedPipeTransferReadyStates.Count - 1);
        }
    }

    private bool SetMountedPipeTransferReady(int index, bool ready)
    {
        if (index < 0
            || index >= mountedPipeTransferReadyStates.Count
            || mountedPipeTransferReadyStates[index] == ready)
        {
            return false;
        }

        mountedPipeTransferReadyStates[index] = ready;
        return true;
    }

    private void ResetMountedPipeTransferReadiness()
    {
        for (int i = 0; i < mountedPipeTransferReadyStates.Count; i++)
        {
            mountedPipeTransferReadyStates[i] = false;
        }
    }

    private void NotifyMountedPipeTransferReadinessChanged()
    {
        InputOutputModule.NotifyRuntimePipeTopologyChanged(RuntimeOccupiedCoordinates);
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        bool subscribeToPlacementEvents = ActiveFluidTanks.Count == 0;
        ActiveFluidTanks.Add(this);
        if (subscribeToPlacementEvents)
        {
            PlacementRuntimeChanged += HandlePlacementTopologyChanged;
            PlacementRuntimeCleared += HandlePlacementTopologyChanged;
            InputOutputModule.RuntimePipeTopologyChanged += HandleRuntimePipeTopologyChanged;
        }

        FacilitySimulationWorld.Register(this);
        InvalidateFluidNetworkTopology();
        WakeRuntimeTick();
        RefreshAllPipeVisuals();
        displayedFluidColorItemId = int.MinValue;
        RefreshFluidColor();
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        FacilitySimulationWorld.Unregister(this);
        runtimeTickSleeping = false;
        ResetMountedPipeTransferReadiness();
        ActiveFluidTanks.Remove(this);
        ClearConnectedTankSources();
        connectedTankDependents.Clear();
        if (ActiveFluidTanks.Count == 0)
        {
            PlacementRuntimeChanged -= HandlePlacementTopologyChanged;
            PlacementRuntimeCleared -= HandlePlacementTopologyChanged;
            InputOutputModule.RuntimePipeTopologyChanged -= HandleRuntimePipeTopologyChanged;
        }

        InvalidateFluidNetworkTopology();
        SetAllPipeVisualsActive(false);
        base.OnDisable();
        RefreshAllPipeVisuals();
    }

    public void ManagedUpdateTick(float deltaTime)
    {
        if (deltaTime <= 0f
            || !isActiveAndEnabled
            || !CanStoreFluid
            || !HasFluidStorageSpace
            || !HasMountedPipeTransferReady)
        {
            SetRuntimeTickSleeping(true);
            return;
        }

        Fluidtank sourceTank;
        using (MapObjectTickProfiler.SampleNamed(
                   "Simulation",
                   nameof(Fluidtank),
                   "Fluid Input Storage Search"))
        {
            if (!EnsureConnectedTankCache())
            {
                SetRuntimeTickSleeping(true);
                return;
            }

            sourceTank = FindBestEqualizationSource();
        }

        if (sourceTank == null)
        {
            SetRuntimeTickSleeping(true);
            return;
        }

        int fluidItemId = sourceTank.StoredFluidItemId;
        int sourcePipeDistance = connectedTankPipeDistances.TryGetValue(
            sourceTank,
            out int recordedDistance)
            ? recordedDistance
            : 0;
        connectedTankPumps.TryGetValue(sourceTank, out Pump pressurePump);
        float transferLiters = Mathf.Min(
            Pump.LimitTransportRate(pressurePump, ConnectedFluidStorageTransferLitersPerSecond)
            * CalculateFluidPressureRetention(sourcePipeDistance)
            * deltaTime,
            AvailableFluidStorageLiters,
            sourceTank.StoredFluidLiters,
            pressurePump != null ? sourceTank.StoredFluidLiters
                : CalculateFluidEqualizationTransferLiters(sourceTank, this));
        if (fluidItemId < 0 || transferLiters <= 0.0001f)
        {
            SetRuntimeTickSleeping(true);
            return;
        }

        if (pressurePump != null)
        {
            transferLiters = pressurePump.LimitTransferVolume(transferLiters, deltaTime);
            if (transferLiters <= 0.0001f)
            {
                SetRuntimeTickSleeping(false);
                return;
            }
        }

        using (MapObjectTickProfiler.SampleNamed(
                   "Simulation",
                   nameof(Fluidtank),
                   "Fluid Input Transfer"))
        {
            float transferTemperatureCelsius = sourceTank.GetStoredFluidTemperatureCelsius(fluidItemId);
            if (!sourceTank.TryConsumeFluidLiters(
                    fluidItemId,
                    transferLiters,
                    out float consumedLiters)
                || consumedLiters <= 0.0001f)
            {
                SetRuntimeTickSleeping(true);
                return;
            }

            TryAddFluidLiters(
                fluidItemId,
                consumedLiters,
                transferTemperatureCelsius,
                out float acceptedLiters);
            pressurePump?.RecordTransferredVolume(acceptedLiters);
            float rejectedLiters = consumedLiters - Mathf.Max(0f, acceptedLiters);
            if (rejectedLiters > 0.0001f)
            {
                sourceTank.RestoreUnacceptedFluid(fluidItemId, rejectedLiters, transferTemperatureCelsius);
            }
        }

        bool hasRemainingSource;
        using (MapObjectTickProfiler.SampleNamed(
                   "Simulation",
                   nameof(Fluidtank),
                   "Fluid Input Storage Search"))
        {
            hasRemainingSource = FindBestEqualizationSource() != null;
        }
        SetRuntimeTickSleeping(!hasRemainingSource);
    }

    protected override void OnStoredFluidChanged(
        int previousFluidItemId,
        float previousStoredLiters,
        int currentFluidItemId,
        float currentStoredLiters)
    {
        base.OnStoredFluidChanged(
            previousFluidItemId,
            previousStoredLiters,
            currentFluidItemId,
            currentStoredLiters);

        using (MapObjectTickProfiler.SampleNamed(
                   "Simulation",
                   nameof(Fluidtank),
                   "Fluid Tank Wake Propagation"))
        {
            WakeConnectedFluidTankTicks();
        }

        RefreshFluidColor();
        if (previousFluidItemId != currentFluidItemId)
        {
            InvalidateFluidNetworkTopology();
            RefreshAllPipeVisuals();
        }
    }

    private void RefreshFluidColor()
    {
        if (fluidColor == null)
        {
            return;
        }

        int fluidItemId = StoredFluidItemId;
        if (displayedFluidColorItemId == fluidItemId)
        {
            return;
        }

        ItemDefinition definition = fluidItemId >= 0
            ? InputOutputModule.ResolveItemDefinition(fluidItemId)
            : null;
        if (definition == null)
        {
            fluidColor.SetPropertyBlock(null);
            displayedFluidColorItemId = fluidItemId < 0 ? fluidItemId : int.MinValue;
            return;
        }

        displayedFluidColorItemId = fluidItemId;
        fluidColorPropertyBlock ??= new MaterialPropertyBlock();
        fluidColor.GetPropertyBlock(fluidColorPropertyBlock);
        Color displayColor = definition.fluidDisplayColor;
        fluidColorPropertyBlock.SetColor(BaseColorShaderId, displayColor);
        fluidColorPropertyBlock.SetColor(ColorShaderId, displayColor);
        fluidColor.SetPropertyBlock(fluidColorPropertyBlock);
    }

    public static void RefreshAllPipeVisuals()
    {
        foreach (Fluidtank tank in ActiveFluidTanks)
        {
            if (tank != null && tank.isActiveAndEnabled)
            {
                tank.RefreshPipeVisuals();
            }
        }
    }

    public void ApplyBlueprintPipeConnections(IReadOnlyList<Vector2Int> connectedDirections)
    {
        if (pipeList == null)
        {
            return;
        }

        for (int i = 0; i < pipeList.Count; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (pipeVisual == null
                || pipeVisual == gameObject
                || !TryResolvePipeVisualDirection(pipeVisual, out Vector2Int direction))
            {
                continue;
            }

            bool connected = ContainsDirection(connectedDirections, direction);
            if (isFlatCarMountedPresentation)
            {
                CacheDefaultPipeLocalPositions();
                EnsureMountedPipeTargetStateCapacity();
                mountedPipeTargetActiveStates[i] = connected;
                pipeVisual.transform.localPosition = connected
                    ? GetMountedPipeExtendedLocalPosition(i)
                    : GetMountedPipeRetractedLocalPosition(i);
            }

            if (pipeVisual.activeSelf != connected)
            {
                pipeVisual.SetActive(connected);
            }
        }
    }

    private static void HandlePlacementTopologyChanged(InstallationObject installationObject)
    {
        if (!InputOutputModule.AffectsRuntimeFluidTopology(installationObject))
        {
            return;
        }

        InvalidateFluidNetworkTopology();
        RefreshAllPipeVisuals();
    }

    private static void HandleRuntimePipeTopologyChanged(InputOutputModule _)
    {
        InvalidateFluidNetworkTopology();
        RefreshAllPipeVisuals();
    }

    private static void InvalidateFluidNetworkTopology()
    {
        unchecked
        {
            fluidNetworkTopologyVersion++;
            if (fluidNetworkTopologyVersion == 0)
            {
                fluidNetworkTopologyVersion = 1;
            }
        }

        WakeAllRuntimeTicks();
    }

    private void WakeConnectedFluidTankTicks()
    {
        if (runtimeTickSleeping)
        {
            WakeRuntimeTick();
        }

        if (connectedTankCacheTopologyVersion != fluidNetworkTopologyVersion)
        {
            WakeAllRuntimeTicks();
            return;
        }

        // Source discovery is directional. Wake tanks that can draw from this
        // reservoir, including receivers on the opposite side of a Pump.
        for (int i = 0; i < connectedTankDependents.Count; i++)
        {
            Fluidtank tank = connectedTankDependents[i];
            if (tank != null && tank.runtimeTickSleeping)
            {
                tank.WakeRuntimeTick();
            }
        }
    }

    private static void WakeAllRuntimeTicks()
    {
        foreach (Fluidtank tank in ActiveFluidTanks)
        {
            if (tank != null && tank.runtimeTickSleeping)
            {
                tank.WakeRuntimeTick();
            }
        }
    }

    private void WakeRuntimeTick()
    {
        if (!Application.isPlaying
            || !isActiveAndEnabled)
        {
            return;
        }

        runtimeTickSleeping = false;
        SetSleepAwakeDebugSleeping(false);
        if (!FacilitySimulationWorld.IsScheduled(this))
        {
            FacilitySimulationWorld.SetScheduled(this, true);
        }
    }

    private void SetRuntimeTickSleeping(bool sleeping)
    {
        bool shouldRemainScheduled = !sleeping && isActiveAndEnabled;
        if (runtimeTickSleeping == sleeping
            && FacilitySimulationWorld.IsScheduled(this) == shouldRemainScheduled)
        {
            return;
        }

        runtimeTickSleeping = sleeping;
        FacilitySimulationWorld.SetScheduled(this, shouldRemainScheduled);
        SetSleepAwakeDebugSleeping(runtimeTickSleeping);
    }

    private void ClearConnectedTankSources()
    {
        for (int i = 0; i < connectedTankCache.Count; i++)
        {
            Fluidtank source = connectedTankCache[i];
            if (source != null) source.connectedTankDependents.Remove(this);
        }
        connectedTankCache.Clear();
    }

    private bool EnsureConnectedTankCache()
    {
        if (connectedTankCacheTopologyVersion == fluidNetworkTopologyVersion)
        {
            return connectedTankCache.Count > 0;
        }

        ClearConnectedTankSources();
        connectedTankPipeDistances.Clear();
        connectedTankPumps.Clear();
        fluidNetworkSearchPumps.Clear();
        fluidNetworkSearchCurrentPump = null;
        fluidNetworkSearchQueue.Clear();
        fluidNetworkSearchPipeCounts.Clear();

        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            connectedTankCacheTopologyVersion = fluidNetworkTopologyVersion;
            return false;
        }

        EnqueueFluidNetworkSearchCoordinate(anchorCoordinate, 0);
        while (fluidNetworkSearchQueue.Count > 0)
        {
            FluidNetworkSearchNode searchNode = fluidNetworkSearchQueue.Dequeue();
            Vector2Int coordinate = searchNode.Coordinate;
            if (!fluidNetworkSearchPipeCounts.TryGetValue(
                    coordinate,
                    out int pipeCount)
                || pipeCount != searchNode.PipeCount)
            {
                continue;
            }

            fluidNetworkSearchPumps.TryGetValue(coordinate, out fluidNetworkSearchCurrentPump);
            if (!TryResolveFluidNetworkNode(coordinate, out Fluidtank tank, out Pipe pipe))
            {
                continue;
            }

            if (tank != null && tank != this)
            {
                if (!connectedTankCache.Contains(tank))
                {
                    connectedTankCache.Add(tank);
                    tank.connectedTankDependents.Add(this);
                }

                int pipeDistance = Mathf.Max(0, pipeCount - 1);
                if (!connectedTankPipeDistances.TryGetValue(
                        tank,
                        out int previousDistance)
                    || pipeDistance < previousDistance)
                {
                    connectedTankPipeDistances[tank] = pipeDistance;
                    connectedTankPumps[tank] = fluidNetworkSearchCurrentPump;
                }
                if (fluidNetworkSearchCurrentPump != null) continue;
            }

            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = FluidCardinalDirections[directionIndex];
                if (!HasFluidNetworkNodeConnectionTowards(coordinate, tank, pipe, direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                if (!TryResolveFluidNetworkNode(
                        nextCoordinate,
                        out Fluidtank nextTank,
                        out Pipe nextPipe)
                    || !HasFluidNetworkNodeConnectionTowards(nextCoordinate, nextTank, nextPipe, -direction))
                {
                    continue;
                }

                EnqueueFluidNetworkSearchCoordinate(
                    nextCoordinate,
                    pipeCount + (nextPipe != null ? 1 : 0));
            }

            connectedPumpPassScratch.Clear();
            InputOutputModule.CollectPumpPipePassesAtRuntimeCoordinate(coordinate, connectedPumpPassScratch);
            for (int i = 0; i < connectedPumpPassScratch.Count; i++)
            {
                InputOutputModule.RuntimePumpPipePass pass = connectedPumpPassScratch[i];
                if (pass.Pump.AllowsRuntimeFluidTraversal(coordinate, true)
                    && (tank == null || tank.HasFluidNetworkConnectionTowards(coordinate, -pass.ExternalDirection)))
                {
                    EnqueueFluidNetworkSearchCoordinate(pass.OtherCoordinate, pipeCount, pass.Pump);
                }
            }
            connectedPumpPassScratch.Clear();

            Vector2Int remoteCoordinate = default;
            bool hasRemote = pipe != null
                             && PipeWorld.Current != null
                             && PipeWorld.Current.TryGetAtCoordinate(
                                 coordinate,
                                 out PipeRuntimeRecord runtimeRecord)
                ? runtimeRecord.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate)
                : pipe != null && pipe.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate);
            if (hasRemote)
            {
                EnqueueFluidNetworkSearchCoordinate(
                    remoteCoordinate,
                    Pipe.AddRemoteTraversalPipeDistance(
                        pipeCount,
                        coordinate,
                        remoteCoordinate));
            }
        }

        connectedTankCacheTopologyVersion = fluidNetworkTopologyVersion;
        return connectedTankCache.Count > 0;
    }

    private static bool HasFluidNetworkNodeConnectionTowards(
        Vector2Int coordinate, Fluidtank tank, Pipe pipe, Vector2Int direction)
    {
        if (tank != null)
        {
            return tank.HasFluidNetworkConnectionTowards(coordinate, direction);
        }
        if (InputOutputModule.HasRuntimePumpPipePassTowards(coordinate, direction))
        {
            return true;
        }
        if (pipe == null)
        {
            return false;
        }
        return PipeWorld.Current != null
               && PipeWorld.Current.TryGetMatchingAtCoordinate(coordinate, pipe, out PipeRuntimeRecord record)
            ? record.HasConnectionTowardsAt(coordinate, direction)
            : pipe.HasConnectionTowardsAt(coordinate, ResolvePipeRuntimeRotation(coordinate, pipe), direction);
    }

    private void EnqueueFluidNetworkSearchCoordinate(Vector2Int coordinate, int pipeCount, Pump crossedPump = null)
    {
        if (fluidNetworkSearchPipeCounts.TryGetValue(
                coordinate,
                out int previousPipeCount)
            && previousPipeCount <= pipeCount)
        {
            return;
        }

        fluidNetworkSearchPumps[coordinate] = Pump.ResolvePressureLimit(fluidNetworkSearchCurrentPump, crossedPump);
        fluidNetworkSearchPipeCounts[coordinate] = pipeCount;
        fluidNetworkSearchQueue.Enqueue(new FluidNetworkSearchNode(coordinate, pipeCount));
    }

    private bool TryResolveFluidNetworkNode(
        Vector2Int coordinate,
        out Fluidtank tank,
        out Pipe pipe)
    {
        tank = null;
        pipe = null;
        PipeWorld pipeWorld = PipeWorld.Current;
        if (pipeWorld != null
            && pipeWorld.TryGetAtCoordinate(coordinate, out PipeRuntimeRecord pipeRecord))
        {
            pipe = pipeRecord.Prototype;
        }

        adjacentInstallationScratch.Clear();
        if (!CollectActiveInstallationsAtRuntimeGridCoordinate(
                coordinate,
                adjacentInstallationScratch))
        {
            return pipe != null
                   || InputOutputModule.TryGetPumpPipePassAtRuntimeCoordinate(coordinate, out _, out _, out _);
        }

        for (int i = 0; i < adjacentInstallationScratch.Count; i++)
        {
            InstallationObject installation = adjacentInstallationScratch[i];
            if (installation is Fluidtank candidateTank)
            {
                tank = candidateTank;
                return true;
            }

            if (installation is Pipe candidatePipe)
            {
                pipe = candidatePipe;
            }
        }

        return pipe != null
               || InputOutputModule.TryGetPumpPipePassAtRuntimeCoordinate(coordinate, out _, out _, out _);
    }

    private Fluidtank FindBestEqualizationSource()
    {
        int requiredFluidItemId = StoredFluidItemId;
        float currentFillRatio = GetFluidFillRatio(this);
        float bestFillRatio = -1f;
        Fluidtank bestSource = null;

        for (int i = 0; i < connectedTankCache.Count; i++)
        {
            Fluidtank candidate = connectedTankCache[i];
            if (candidate == null
                || !candidate.isActiveAndEnabled
                || !candidate.CanProvideFluidItem(requiredFluidItemId))
            {
                continue;
            }

            float candidateFillRatio = GetFluidFillRatio(candidate);
            bool isPumped = connectedTankPumps.TryGetValue(candidate, out Pump pump) && pump != null;
            if ((!isPumped && candidateFillRatio <= currentFillRatio + FluidFillRatioEpsilon)
                || candidateFillRatio <= bestFillRatio + FluidFillRatioEpsilon)
            {
                continue;
            }

            bestFillRatio = candidateFillRatio;
            bestSource = candidate;
        }

        return bestSource;
    }

    private static float GetFluidFillRatio(InstallationObject storage)
    {
        if (storage == null)
        {
            return 0f;
        }

        float capacityLiters = storage.FluidStorageCapacityLiters;
        return capacityLiters > 0.0001f
            ? Mathf.Clamp01(storage.StoredFluidLiters / capacityLiters)
            : 0f;
    }

    private void RefreshPipeVisuals()
    {
        bool hasPlacement = TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _);
        if (pipeList == null)
        {
            return;
        }

        for (int i = 0; i < pipeList.Count; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (pipeVisual == null || pipeVisual == gameObject)
            {
                continue;
            }

            bool connected = hasPlacement
                             && TryResolvePipeVisualDirection(pipeVisual, out Vector2Int direction)
                             && HasPotentialFluidNetworkConnectionTowards(anchorCoordinate, direction);
            if (isFlatCarMountedPresentation)
            {
                SetMountedPipeTarget(i, pipeVisual, connected);
                continue;
            }

            if (pipeVisual.activeSelf != connected)
            {
                pipeVisual.SetActive(connected);
            }
        }
    }

    private bool IsMountedPipeWithinDockingRange(
        int pipeIndex,
        GameObject pipeVisual,
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank)
    {
        if (!isFlatCarMountedPresentation)
        {
            return true;
        }

        Transform pipeParent = pipeVisual != null ? pipeVisual.transform.parent : null;
        if (pipeParent == null)
        {
            return false;
        }

        Vector3 extendedPipeWorldPosition = pipeParent.TransformPoint(
            GetMountedPipeExtendedLocalPosition(pipeIndex));
        Vector2 expectedDockPosition = new Vector2(
            tankCoordinate.x + directionFromTank.x * 0.5f,
            tankCoordinate.y + directionFromTank.y * 0.5f);
        Vector2 dockingOffset = new Vector2(
            extendedPipeWorldPosition.x - expectedDockPosition.x,
            extendedPipeWorldPosition.z - expectedDockPosition.y);
        float tolerance = Mathf.Max(0f, flatCarMountedPipeDockingTolerance);
        return dockingOffset.sqrMagnitude <= tolerance * tolerance;
    }

    public bool HasFluidNetworkConnectionTowards(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank)
    {
        return (!isFlatCarMountedPresentation
                || IsMountedPipeTransferReadyTowards(directionFromTank))
               && HasPotentialFluidNetworkConnectionTowards(tankCoordinate, directionFromTank);
    }

    private bool HasPotentialFluidNetworkConnectionTowards(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank)
    {
        return HasFluidNetworkConnectionTowardsIgnoringStorageCoordinate(
            tankCoordinate,
            directionFromTank,
            tankCoordinate);
    }

    private bool IsMountedPipeTransferReadyTowards(Vector2Int directionFromTank)
    {
        if (!isFlatCarMountedPresentation
            || directionFromTank == Vector2Int.zero
            || pipeList == null)
        {
            return !isFlatCarMountedPresentation;
        }

        EnsureMountedPipeTargetStateCapacity();
        int pipeCount = Mathf.Min(pipeList.Count, mountedPipeTransferReadyStates.Count);
        for (int i = 0; i < pipeCount; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (mountedPipeTransferReadyStates[i]
                && pipeVisual != null
                && pipeVisual != gameObject
                && TryResolvePipeVisualDirection(pipeVisual, out Vector2Int pipeDirection)
                && pipeDirection == directionFromTank)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasFluidNetworkConnectionTowardsIgnoringStorageCoordinate(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        Vector2Int ignoredStorageCoordinate)
    {
        if (directionFromTank == Vector2Int.zero
            || !TryResolveConnectionTowards(
                tankCoordinate,
                directionFromTank,
                ignoredStorageCoordinate,
                out Fluidtank neighborTank,
                out int neighborFluidItemId))
        {
            return false;
        }

        if (isFlatCarMountedPresentation)
        {
            return CanDeployMountedPipeForFluid(
                StoredFluidItemId,
                neighborFluidItemId);
        }

        if (neighborTank != null)
        {
            return CanConnectAdjacentFixedTank(
                tankCoordinate,
                directionFromTank,
                neighborTank);
        }

        return CanConnectFixedTankPipe(
            tankCoordinate,
            directionFromTank,
            neighborFluidItemId);
    }

    private bool CanConnectFixedTankPipe(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        int pipeFluidItemId)
    {
        int tankNetworkFluidItemId = ResolveFixedTankNetworkFluidItemId(
            tankCoordinate,
            tankCoordinate + directionFromTank,
            out bool hasConflict);
        return !hasConflict
               && (tankNetworkFluidItemId < 0
                   || pipeFluidItemId < 0
                   || tankNetworkFluidItemId == pipeFluidItemId);
    }

    private bool CanConnectAdjacentFixedTank(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        Fluidtank neighborTank)
    {
        if (neighborTank == null || neighborTank.IsFlatCarMounted)
        {
            return false;
        }

        Vector2Int neighborCoordinate = tankCoordinate + directionFromTank;
        int localFluidItemId = ResolveFixedTankNetworkFluidItemId(
            tankCoordinate,
            neighborCoordinate,
            out bool localConflict);
        int neighborFluidItemId = neighborTank.ResolveFixedTankNetworkFluidItemId(
            neighborCoordinate,
            tankCoordinate,
            out bool neighborConflict);
        return !localConflict
               && !neighborConflict
               && (localFluidItemId < 0
                   || neighborFluidItemId < 0
                   || localFluidItemId == neighborFluidItemId);
    }

    private int ResolveFixedTankNetworkFluidItemId(
        Vector2Int tankCoordinate,
        Vector2Int ignoredNeighborCoordinate,
        out bool hasConflict)
    {
        hasConflict = false;
        int resolvedFluidItemId = StoredFluidItemId;
        for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
        {
            Vector2Int direction = FluidCardinalDirections[directionIndex];
            if (tankCoordinate + direction == ignoredNeighborCoordinate
                || !TryResolveConnectionTowards(
                    tankCoordinate,
                    direction,
                    tankCoordinate,
                    out Fluidtank adjacentTank,
                    out int candidateFluidItemId)
                // An adjacent tank is evaluated at its own boundary. Including
                // its stored identity here lets a rejected, different-fluid
                // neighbor poison every other valid side of this tank.
                || adjacentTank != null
                || candidateFluidItemId < 0)
            {
                continue;
            }

            if (resolvedFluidItemId < 0)
            {
                resolvedFluidItemId = candidateFluidItemId;
                continue;
            }

            if (resolvedFluidItemId != candidateFluidItemId)
            {
                hasConflict = true;
                return resolvedFluidItemId;
            }
        }

        return resolvedFluidItemId;
    }

    public bool CanDockMountedPipeTowards(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank)
    {
        if (!isFlatCarMountedPresentation
            || pipeList == null
            || directionFromTank == Vector2Int.zero)
        {
            return false;
        }

        for (int i = 0; i < pipeList.Count; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (pipeVisual != null
                && pipeVisual != gameObject
                && TryResolvePipeVisualDirection(pipeVisual, out Vector2Int pipeDirection)
                && pipeDirection == directionFromTank)
            {
                Vector2Int ignoredStorageCoordinate = TryGetPlacementRuntime(
                    out Vector2Int currentTankCoordinate,
                    out _)
                    ? currentTankCoordinate
                    : tankCoordinate;
                return HasFluidNetworkConnectionTowardsIgnoringStorageCoordinate(
                    tankCoordinate,
                    directionFromTank,
                    ignoredStorageCoordinate);
            }
        }

        return false;
    }

    private static bool CanDeployMountedPipeForFluid(
        int storedFluidItemId,
        int pipeFluidItemId)
    {
        // A Pump remains a valid dock while its upstream network is dry. Once
        // fluid identity is known, prevent a loaded cart from mixing fluids.
        return pipeFluidItemId < 0
               || storedFluidItemId < 0
               || storedFluidItemId == pipeFluidItemId;
    }

    private bool TryResolveConnectionTowards(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        Vector2Int ignoredStorageCoordinate,
        out Fluidtank neighborTank,
        out int neighborFluidItemId)
    {
        neighborTank = null;
        neighborFluidItemId = -1;
        if (isFlatCarMountedPresentation)
        {
            return TryResolveMountedPumpRailPass(
                tankCoordinate,
                directionFromTank,
                out neighborFluidItemId);
        }

        Vector2Int neighborCoordinate = tankCoordinate + directionFromTank;
        Pipe connectedPipe = null;
        PipeWorld pipeWorld = PipeWorld.Current;
        if (pipeWorld != null
            && pipeWorld.TryGetAtCoordinate(neighborCoordinate, out PipeRuntimeRecord dataPipe)
            && dataPipe.HasConnectionTowardsAt(neighborCoordinate, -directionFromTank))
        {
            connectedPipe = dataPipe.Prototype;
        }

        adjacentInstallationScratch.Clear();
        if (CollectActiveInstallationsAtRuntimeGridCoordinate(
                neighborCoordinate,
                adjacentInstallationScratch))
        {
            for (int i = 0; i < adjacentInstallationScratch.Count; i++)
            {
                InstallationObject neighbor = adjacentInstallationScratch[i];
                if (neighbor == null || neighbor == this)
                {
                    continue;
                }

                if (neighbor is Fluidtank candidateTank)
                {
                    neighborTank = candidateTank;
                    neighborFluidItemId = candidateTank.StoredFluidItemId;
                    adjacentInstallationScratch.Clear();
                    return true;
                }

                if (neighbor is Pipe pipe
                    && pipe.HasConnectionTowardsAt(
                        neighborCoordinate,
                        ResolvePipeRuntimeRotation(neighborCoordinate, pipe),
                        -directionFromTank))
                {
                    connectedPipe = pipe;
                }
            }

            adjacentInstallationScratch.Clear();
        }

        if (connectedPipe != null
            || InputOutputModule.HasRuntimePumpPipePassTowards(neighborCoordinate, -directionFromTank))
        {
            Pipe.TryGetNetworkFluidInfoAt(
                neighborCoordinate, connectedFluidIdentityContext, true, ignoredStorageCoordinate, false,
                out neighborFluidItemId, out _, out _);
            return true;
        }

        // A tank may occupy the virtual PipePass cell, just as a surface pipe can.
        if (InputOutputModule.TryGetPumpPipePassAtRuntimeCoordinate(
                tankCoordinate, out _, out Vector2Int otherEndpoint, out Vector2Int externalDirection)
            && externalDirection == -directionFromTank)
        {
            Pipe.TryGetNetworkFluidInfoAt(
                otherEndpoint, connectedFluidIdentityContext, true, ignoredStorageCoordinate, false,
                out neighborFluidItemId, out _, out _);
            return true;
        }

        return TryGetPipeOutputAreaConnectionFluidItemId(
            tankCoordinate,
            directionFromTank,
            out neighborFluidItemId);
    }

    private bool TryResolveMountedPumpRailPass(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        out int fluidItemId)
    {
        return TryResolveMountedPumpRailPassCore(
            null,
            tankCoordinate,
            directionFromTank,
            out fluidItemId);
    }

    internal bool CanProvideMountedFluidToPump(
        Pump pump,
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        int fluidItemId,
        float requestedLiters = 0f)
    {
        return isFlatCarMountedPresentation
               && TryGetPlacementRuntime(out Vector2Int currentCoordinate, out _)
               && currentCoordinate == tankCoordinate
               && IsMountedPipeTransferReadyTowards(directionFromTank)
               && TryResolveMountedPumpRailPassCore(
                   pump,
                   tankCoordinate,
                   directionFromTank,
                   out int pumpFluidItemId)
               && (pumpFluidItemId < 0 || pumpFluidItemId == fluidItemId)
               && CanProvideFluidItem(fluidItemId, requestedLiters);
    }

    private bool TryResolveMountedPumpRailPassCore(
        Pump requiredPump,
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        out int fluidItemId)
    {
        fluidItemId = -1;
        if (directionFromTank == Vector2Int.zero)
        {
            return false;
        }

        mountedPumpPipePassScratch.Clear();
        if (!InputOutputModule.CollectPumpPipePassesAtRuntimeCoordinate(
                tankCoordinate,
                mountedPumpPipePassScratch))
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i < mountedPumpPipePassScratch.Count; i++)
        {
            InputOutputModule.RuntimePumpPipePass pipePass = mountedPumpPipePassScratch[i];
            if (-pipePass.ExternalDirection != directionFromTank
                || requiredPump != null && pipePass.Pump != requiredPump)
            {
                continue;
            }

            found = true;
            if (pipePass.Pump != null
                && pipePass.Pump.TryGetObjectInfoFluidInfo(
                    out int candidateFluidItemId,
                    out _,
                    out _)
                && candidateFluidItemId >= 0)
            {
                fluidItemId = candidateFluidItemId;
                break;
            }
        }

        mountedPumpPipePassScratch.Clear();
        return found;
    }

    private static Quaternion ResolvePipeRuntimeRotation(Vector2Int coordinate, Pipe pipe)
    {
        PipeWorld world = PipeWorld.Current;
        if (world != null
            && world.TryGetAtCoordinate(coordinate, out PipeRuntimeRecord record)
            && ReferenceEquals(record.Prototype, pipe))
        {
            return record.WorldRotation;
        }

        return pipe != null ? pipe.transform.rotation : Quaternion.identity;
    }

    private bool HasPipeOutputAreaConnection(Vector2Int tankCoordinate, Vector2Int directionFromTank)
    {
        Vector2Int requiredOutputDirection = -directionFromTank;
        return HasPipeOutputAreaAtCoordinate(
                   tankCoordinate + directionFromTank,
                   requiredOutputDirection)
               || HasPipeOutputAreaAtCoordinate(tankCoordinate, requiredOutputDirection);
    }

    private bool TryGetPipeOutputAreaConnectionFluidItemId(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        out int fluidItemId)
    {
        fluidItemId = -1;
        Vector2Int requiredOutputDirection = -directionFromTank;
        Vector2Int neighborCoordinate = tankCoordinate + directionFromTank;
        return TryGetPipeOutputAreaFluidItemIdAtCoordinate(
                   neighborCoordinate,
                   requiredOutputDirection,
                   out fluidItemId)
               || TryGetPipeOutputAreaFluidItemIdAtCoordinate(
                   tankCoordinate,
                   requiredOutputDirection,
                   out fluidItemId)
               || HasPipeOutputAreaConnection(tankCoordinate, directionFromTank);
    }

    private bool TryGetPipeOutputAreaFluidItemIdAtCoordinate(
        Vector2Int coordinate,
        Vector2Int requiredOutputDirection,
        out int fluidItemId)
    {
        fluidItemId = -1;
        adjacentModuleScratch.Clear();
        if (!InputOutputModule.CollectModulesAtRuntimeGridCoordinate(
                coordinate,
                adjacentModuleScratch))
        {
            return false;
        }

        bool hasConnection = false;
        for (int i = 0; i < adjacentModuleScratch.Count; i++)
        {
            InputOutputModule module = adjacentModuleScratch[i];
            if (module == null
                || !module.TryGetRuntimePipeOutputExternalDirection(
                    coordinate,
                    out Vector2Int outputDirection)
                || outputDirection != requiredOutputDirection)
            {
                continue;
            }

            hasConnection = true;
            adjacentOutputFluidItemIdsScratch.Clear();
            if (!module.TryGetRuntimeOutputItemIdsAtCoordinate(
                    coordinate,
                    adjacentOutputFluidItemIdsScratch))
            {
                continue;
            }

            foreach (int outputItemId in adjacentOutputFluidItemIdsScratch)
            {
                if (InputOutputModule.IsFluidItemId(outputItemId)
                    && (fluidItemId < 0 || outputItemId < fluidItemId))
                {
                    fluidItemId = outputItemId;
                }
            }
        }

        adjacentModuleScratch.Clear();
        adjacentOutputFluidItemIdsScratch.Clear();
        return hasConnection;
    }

    private bool HasPipeOutputAreaAtCoordinate(
        Vector2Int coordinate,
        Vector2Int requiredOutputDirection)
    {
        adjacentModuleScratch.Clear();
        if (!InputOutputModule.CollectModulesAtRuntimeGridCoordinate(
                coordinate,
                adjacentModuleScratch))
        {
            return false;
        }

        for (int i = 0; i < adjacentModuleScratch.Count; i++)
        {
            InputOutputModule module = adjacentModuleScratch[i];
            if (module != null
                && module.TryGetRuntimePipeOutputExternalDirection(
                    coordinate,
                    out Vector2Int outputDirection)
                && outputDirection == requiredOutputDirection)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolvePipeVisualDirection(GameObject pipeVisual, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (pipeVisual == null
            || !TryGetPipeVisualLocalOffset(pipeVisual.transform, out Vector3 localOffset))
        {
            return false;
        }

        // Installation animation temporarily scales the whole tank to zero.
        // Child world positions collapse onto the tank origin in that state, so
        // derive the grid direction from the hierarchy below the tank instead.
        // Applying only the tank rotation preserves the placed orientation while
        // deliberately excluding its animated scale and world position.
        Vector3 directionOffset = transform.rotation * localOffset;

        if (Mathf.Abs(directionOffset.x) >= Mathf.Abs(directionOffset.z))
        {
            direction.x = directionOffset.x >= 0f ? 1 : -1;
        }
        else
        {
            direction.y = directionOffset.z >= 0f ? 1 : -1;
        }

        return true;
    }

    private bool TryGetPipeVisualLocalOffset(Transform pipeVisualTransform, out Vector3 localOffset)
    {
        localOffset = Vector3.zero;
        if (pipeVisualTransform == null || pipeVisualTransform == transform)
        {
            return false;
        }

        Matrix4x4 pipeToTankLocal = Matrix4x4.identity;
        Transform current = pipeVisualTransform;
        while (current != null && current != transform)
        {
            pipeToTankLocal = Matrix4x4.TRS(
                                  current.localPosition,
                                  current.localRotation,
                                  current.localScale)
                              * pipeToTankLocal;
            current = current.parent;
        }

        if (current != transform)
        {
            return false;
        }

        localOffset = pipeToTankLocal.MultiplyPoint3x4(Vector3.zero);
        return localOffset.x * localOffset.x + localOffset.z * localOffset.z > PipeDirectionEpsilon;
    }

    private static bool ContainsDirection(IReadOnlyList<Vector2Int> directions, Vector2Int direction)
    {
        if (directions == null || direction == Vector2Int.zero)
        {
            return false;
        }

        for (int i = 0; i < directions.Count; i++)
        {
            if (directions[i] == direction)
            {
                return true;
            }
        }

        return false;
    }

    private void SetAllPipeVisualsActive(bool active)
    {
        if (pipeList == null)
        {
            return;
        }

        for (int i = 0; i < pipeList.Count; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (pipeVisual != null && pipeVisual != gameObject && pipeVisual.activeSelf != active)
            {
                pipeVisual.SetActive(active);
            }
        }
    }
}
