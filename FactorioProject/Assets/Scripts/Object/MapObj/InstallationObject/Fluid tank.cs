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

    [SerializeField, Tooltip("탱크 측면 연결 파이프입니다. 목록 순서와 무관하게 자식 위치로 방향을 판정합니다.")]
    private List<GameObject> pipeList = new List<GameObject>();

    private readonly List<InstallationObject> adjacentInstallationScratch = new List<InstallationObject>(4);
    private readonly List<InputOutputModule> adjacentModuleScratch = new List<InputOutputModule>(2);
    private readonly List<Fluidtank> connectedTankCache = new List<Fluidtank>(4);
    private readonly Queue<Vector2Int> fluidNetworkSearchQueue = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> fluidNetworkSearchVisited = new HashSet<Vector2Int>();
    private int connectedTankCacheTopologyVersion;

    public float ManagedUpdateTickIntervalSeconds => FluidTankUpdateIntervalSeconds;

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
                if (pipe != null
                    && !pipe.HasConnectionTowardsAt(coordinate, pipe.transform.rotation, direction))
                {
                    continue;
                }

                Vector2Int nextCoordinate = coordinate + direction;
                if (fluidNetworkSearchVisited.Contains(nextCoordinate)
                    || !TryResolveFluidNetworkNode(nextCoordinate, out _, out Pipe nextPipe)
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
                             && (HasConnectedNeighbor(anchorCoordinate + direction, direction)
                                 || HasPipeOutputAreaConnection(anchorCoordinate, direction));
            if (pipeVisual.activeSelf != connected)
            {
                pipeVisual.SetActive(connected);
            }
        }
    }

    private bool HasConnectedNeighbor(Vector2Int neighborCoordinate, Vector2Int directionFromTank)
    {
        adjacentInstallationScratch.Clear();
        if (!CollectActiveInstallationsAtRuntimeGridCoordinate(
                neighborCoordinate,
                adjacentInstallationScratch))
        {
            return false;
        }

        for (int i = 0; i < adjacentInstallationScratch.Count; i++)
        {
            InstallationObject neighbor = adjacentInstallationScratch[i];
            if (neighbor == null || neighbor == this)
            {
                continue;
            }

            if (neighbor is Fluidtank)
            {
                return true;
            }

            if (neighbor is Pipe pipe
                && pipe.HasConnectionTowardsAt(
                    neighborCoordinate,
                    pipe.transform.rotation,
                    -directionFromTank))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasPipeOutputAreaConnection(Vector2Int tankCoordinate, Vector2Int directionFromTank)
    {
        Vector2Int requiredOutputDirection = -directionFromTank;
        return HasPipeOutputAreaAtCoordinate(
                   tankCoordinate + directionFromTank,
                   requiredOutputDirection)
               || HasPipeOutputAreaAtCoordinate(tankCoordinate, requiredOutputDirection);
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
        if (pipeVisual == null)
        {
            return false;
        }

        Vector3 worldOffset = pipeVisual.transform.position - transform.position;
        if (worldOffset.x * worldOffset.x + worldOffset.z * worldOffset.z <= PipeDirectionEpsilon)
        {
            return false;
        }

        if (Mathf.Abs(worldOffset.x) >= Mathf.Abs(worldOffset.z))
        {
            direction.x = worldOffset.x >= 0f ? 1 : -1;
        }
        else
        {
            direction.y = worldOffset.z >= 0f ? 1 : -1;
        }

        return true;
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
