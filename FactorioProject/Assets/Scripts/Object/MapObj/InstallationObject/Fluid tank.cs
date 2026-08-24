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
    private readonly HashSet<int> adjacentOutputFluidItemIdsScratch = new HashSet<int>();
    private readonly List<Fluidtank> connectedTankCache = new List<Fluidtank>(4);
    private readonly Queue<Vector2Int> fluidNetworkSearchQueue = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> fluidNetworkSearchVisited = new HashSet<Vector2Int>();
    private readonly List<Vector3> defaultPipeLocalPositions = new List<Vector3>(4);
    private readonly List<bool> mountedPipeTargetActiveStates = new List<bool>(4);
    private int connectedTankCacheTopologyVersion;
    private bool hasCachedDefaultPipeLocalPositions;
    private bool hasFlatCarMountedPresentationState;
    private bool isFlatCarMountedPresentation;

    public float ManagedUpdateTickIntervalSeconds => FluidTankUpdateIntervalSeconds;
    public Vector3 FlatCarMountedLocalPosition => Vector3.down * flatCarMountedLowering;
    public bool IsFlatCarMounted => isFlatCarMountedPresentation;

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
        ApplyFlatCarMountedPipePresentationImmediate(mounted);
        RefreshPipeVisuals();
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
        int pipeCount = Mathf.Min(pipeList.Count, defaultPipeLocalPositions.Count);
        for (int i = 0; i < pipeCount; i++)
        {
            GameObject pipeVisual = pipeList[i];
            if (pipeVisual == null || pipeVisual == gameObject)
            {
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
                continue;
            }

            pipeVisual.transform.localPosition = targetPosition;
            if (!targetActive)
            {
                pipeVisual.SetActive(false);
            }
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
        }

        while (mountedPipeTargetActiveStates.Count > pipeCount)
        {
            mountedPipeTargetActiveStates.RemoveAt(mountedPipeTargetActiveStates.Count - 1);
        }
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

        InvalidateFluidNetworkTopology();
        MapObjectTickManager.RegisterUpdateTick(this);
        RefreshAllPipeVisuals();
        RefreshFluidColor();
    }

    protected override void OnDisable()
    {
        MapObjectTickManager.UnregisterUpdateTick(this);
        ActiveFluidTanks.Remove(this);
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
            || !EnsureConnectedTankCache())
        {
            return;
        }

        Fluidtank sourceTank = FindBestEqualizationSource();
        if (sourceTank == null)
        {
            return;
        }

        int fluidItemId = sourceTank.StoredFluidItemId;
        float transferLiters = Mathf.Min(
            ConnectedFluidStorageTransferLitersPerSecond * deltaTime,
            AvailableFluidStorageLiters,
            sourceTank.StoredFluidLiters,
            CalculateFluidEqualizationTransferLiters(sourceTank, this));
        float transferTemperatureCelsius = sourceTank.GetStoredFluidTemperatureCelsius(fluidItemId);
        if (fluidItemId < 0
            || transferLiters <= 0.0001f
            || !sourceTank.TryConsumeFluidLiters(fluidItemId, transferLiters, out float consumedLiters)
            || consumedLiters <= 0.0001f)
        {
            return;
        }

        TryAddFluidLiters(
            fluidItemId,
            consumedLiters,
            transferTemperatureCelsius,
            out float acceptedLiters);
        float rejectedLiters = consumedLiters - Mathf.Max(0f, acceptedLiters);
        if (rejectedLiters > 0.0001f)
        {
            sourceTank.TryAddFluidLiters(
                fluidItemId,
                rejectedLiters,
                transferTemperatureCelsius,
                out _);
        }
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
        ItemDefinition definition = fluidItemId >= 0
            ? InputOutputModule.ResolveItemDefinition(fluidItemId)
            : null;
        if (definition == null)
        {
            fluidColor.SetPropertyBlock(null);
            return;
        }

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

    private static void HandlePlacementTopologyChanged(InstallationObject _)
    {
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
    }

    private bool EnsureConnectedTankCache()
    {
        if (connectedTankCacheTopologyVersion == fluidNetworkTopologyVersion)
        {
            return connectedTankCache.Count > 0;
        }

        connectedTankCache.Clear();
        fluidNetworkSearchQueue.Clear();
        fluidNetworkSearchVisited.Clear();

        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            connectedTankCacheTopologyVersion = fluidNetworkTopologyVersion;
            return false;
        }

        fluidNetworkSearchVisited.Add(anchorCoordinate);
        fluidNetworkSearchQueue.Enqueue(anchorCoordinate);
        while (fluidNetworkSearchQueue.Count > 0)
        {
            Vector2Int coordinate = fluidNetworkSearchQueue.Dequeue();
            if (!TryResolveFluidNetworkNode(coordinate, out Fluidtank tank, out Pipe pipe))
            {
                continue;
            }

            if (tank != null && tank != this && !connectedTankCache.Contains(tank))
            {
                connectedTankCache.Add(tank);
            }

            for (int directionIndex = 0; directionIndex < FluidCardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = FluidCardinalDirections[directionIndex];
                if ((tank != null
                     && !tank.HasFluidNetworkConnectionTowards(coordinate, direction))
                    || (pipe != null
                        && !pipe.HasConnectionTowardsAt(
                            coordinate,
                            pipe.transform.rotation,
                            direction)))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                if (fluidNetworkSearchVisited.Contains(nextCoordinate)
                    || !TryResolveFluidNetworkNode(
                        nextCoordinate,
                        out Fluidtank nextTank,
                        out Pipe nextPipe)
                    || nextTank != null
                    && !nextTank.HasFluidNetworkConnectionTowards(nextCoordinate, -direction)
                    || (nextPipe != null
                        && !nextPipe.HasConnectionTowardsAt(
                            nextCoordinate,
                            nextPipe.transform.rotation,
                            -direction)))
                {
                    continue;
                }

                fluidNetworkSearchVisited.Add(nextCoordinate);
                fluidNetworkSearchQueue.Enqueue(nextCoordinate);
            }

            if (pipe != null
                && pipe.TryGetRemoteConnectionCoordinate(coordinate, out Vector2Int remoteCoordinate)
                && fluidNetworkSearchVisited.Add(remoteCoordinate))
            {
                fluidNetworkSearchQueue.Enqueue(remoteCoordinate);
            }
        }

        connectedTankCacheTopologyVersion = fluidNetworkTopologyVersion;
        return connectedTankCache.Count > 0;
    }

    private bool TryResolveFluidNetworkNode(
        Vector2Int coordinate,
        out Fluidtank tank,
        out Pipe pipe)
    {
        tank = null;
        pipe = null;
        adjacentInstallationScratch.Clear();
        if (!CollectActiveInstallationsAtRuntimeGridCoordinate(
                coordinate,
                adjacentInstallationScratch))
        {
            return false;
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

        return pipe != null;
    }

    private Fluidtank FindBestEqualizationSource()
    {
        int requiredFluidItemId = StoredFluidItemId;
        float currentFillRatio = GetFluidFillRatio(this);
        float bestFillRatio = currentFillRatio;
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
            if (candidateFillRatio <= bestFillRatio + FluidFillRatioEpsilon)
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
                             && HasFluidNetworkConnectionTowards(anchorCoordinate, direction);
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
        return HasFluidNetworkConnectionTowardsIgnoringStorageCoordinate(
            tankCoordinate,
            directionFromTank,
            tankCoordinate);
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

        int preferredFluidItemId = StoredFluidItemId;
        if (isFlatCarMountedPresentation)
        {
            return CanDeployMountedPipeForFluid(
                preferredFluidItemId,
                neighborFluidItemId);
        }

        // Adjacent fixed tanks form one storage bank regardless of which
        // individual tank currently owns the liters. Mobile tanks are checked
        // above because their docking pipe requires a pipe-side fluid identity
        // and must reject a different fluid once the tank contains fluid.
        if (neighborTank != null)
        {
            return true;
        }

        return preferredFluidItemId < 0
               || neighborFluidItemId < 0
               || preferredFluidItemId == neighborFluidItemId;
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
        return pipeFluidItemId >= 0
               && (storedFluidItemId < 0 || storedFluidItemId == pipeFluidItemId);
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
        Vector2Int neighborCoordinate = tankCoordinate + directionFromTank;

        adjacentInstallationScratch.Clear();
        if (CollectActiveInstallationsAtRuntimeGridCoordinate(
                neighborCoordinate,
                adjacentInstallationScratch))
        {
            Pipe connectedPipe = null;
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
                        pipe.transform.rotation,
                        -directionFromTank))
                {
                    connectedPipe = pipe;
                }
            }

            adjacentInstallationScratch.Clear();
            if (connectedPipe != null)
            {
                connectedPipe.TryGetConnectedFluidItemIdIgnoringStorageCoordinate(
                    ignoredStorageCoordinate,
                    out neighborFluidItemId);
                return true;
            }
        }

        return TryGetPipeOutputAreaConnectionFluidItemId(
            tankCoordinate,
            directionFromTank,
            out neighborFluidItemId);
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
