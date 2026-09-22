using System.Collections.Generic;
using UnityEngine;

public enum PipeVariantKind
{
    Straight = 0,
    Corner = 1,
    Tee = 2,
    Cross = 3
}

public class Pipe : InstallationObject
{
    private const int MaxObjectInfoFluidSearchNodes = 256;
    private const float FluidDisplayRefreshIntervalSeconds = 0.2f;
    private const int NoDisplayedFluidItemId = -2;
    private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorShaderId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorShaderId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color UnknownFluidDisplayColor = Color.white;
    private static readonly Dictionary<Vector2Int, int> FluidDisplayNetworkItemCache =
        new Dictionary<Vector2Int, int>();
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    [SerializeField]
    private Pipe straightVariantPrefab;
    [SerializeField]
    private Pipe cornerVariantPrefab;
    [SerializeField]
    private Pipe teeVariantPrefab;
    [SerializeField]
    private Pipe crossVariantPrefab;
    [SerializeField]
    private PipeVariantKind variantKind = PipeVariantKind.Straight;
    [SerializeField]
    private InstallationFacingDirection localStraightDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField]
    private InstallationFacingDirection localCornerFirstDirection = InstallationFacingDirection.NegativeX;
    [SerializeField]
    private InstallationFacingDirection localCornerSecondDirection = InstallationFacingDirection.NegativeZ;
    [SerializeField]
    private InstallationFacingDirection localTeeFirstDirection = InstallationFacingDirection.NegativeX;
    [SerializeField]
    private InstallationFacingDirection localTeeSecondDirection = InstallationFacingDirection.PositiveX;
    [SerializeField]
    private InstallationFacingDirection localTeeThirdDirection = InstallationFacingDirection.NegativeZ;

    internal readonly struct ObjectInfoFluidSearchNode
    {
        public readonly Vector2Int Coordinate;
        public readonly int PipeDistance;

        public ObjectInfoFluidSearchNode(Vector2Int coordinate, int pipeDistance)
        {
            Coordinate = coordinate;
            PipeDistance = pipeDistance;
        }
    }

    internal sealed class FluidNetworkSearchContext
    {
        public readonly Queue<ObjectInfoFluidSearchNode> Queue =
            new Queue<ObjectInfoFluidSearchNode>(32);
        public readonly HashSet<Vector2Int> Visited = new HashSet<Vector2Int>();
        public readonly Dictionary<Vector2Int, int> PipeDistances =
            new Dictionary<Vector2Int, int>();
        public readonly Dictionary<InputOutputModule, int> OutputSourcePipeDistances =
            new Dictionary<InputOutputModule, int>();
        internal readonly HashSet<int> FluidItemIds = new HashSet<int>();
        internal readonly HashSet<InputOutputModule> SourceScratch = new HashSet<InputOutputModule>();
        internal readonly HashSet<InputOutputModule> Sources = new HashSet<InputOutputModule>();
        internal readonly List<InstallationObject> StorageScratch = new List<InstallationObject>(4);
        internal readonly List<InputOutputModule.RuntimePumpPipePass> PumpPasses =
            new List<InputOutputModule.RuntimePumpPipePass>(2);
        internal readonly List<InputOutputModule> PumpModules = new List<InputOutputModule>(4);

        internal readonly Dictionary<Vector2Int, Pump> RoutePumps = new Dictionary<Vector2Int, Pump>();
        internal readonly Dictionary<InputOutputModule, Pump> SourcePumps = new Dictionary<InputOutputModule, Pump>();
        internal readonly Dictionary<InstallationObject, Pump> StoragePumps = new Dictionary<InstallationObject, Pump>();
        internal readonly Dictionary<Pump, int> PumpDistances = new Dictionary<Pump, int>();
        internal readonly Dictionary<Pump, float> PumpRates = new Dictionary<Pump, float>();
        internal Pump CurrentPump;

        public void Reset()
        {
            RoutePumps.Clear();
            SourcePumps.Clear();
            StoragePumps.Clear();
            PumpDistances.Clear();
            PumpRates.Clear();
            CurrentPump = null;
            Queue.Clear();
            Visited.Clear();
            PipeDistances.Clear();
            OutputSourcePipeDistances.Clear();
        }
    }

    // Fixed-tank compatibility resolves the fluid on each connected pipe while
    // the primary network BFS is still active. Separate reusable contexts keep
    // that nested lookup from clearing the primary queue and visited set.
    private readonly FluidNetworkSearchContext primaryFluidSearchContext =
        new FluidNetworkSearchContext();
    private readonly FluidNetworkSearchContext fluidIdentitySearchContext =
        new FluidNetworkSearchContext();
    private readonly HashSet<InputOutputModule> objectInfoFluidOutputSources = new HashSet<InputOutputModule>();

    [SerializeField]
    private MeshRenderer fluidDP;

    private MaterialPropertyBlock fluidDisplayPropertyBlock;
    private int displayedFluidItemId = NoDisplayedFluidItemId;
    private bool displayedFluidVisible;
    private bool fluidDisplayRendererResolved;
    private bool fluidDisplaySuppressedForVariantPreview;
    private float nextFluidDisplayRefreshTime;
    private float nextObjectInfoFluidRefreshTime = float.NegativeInfinity;
    private int cachedObjectInfoFluidItemId = -1;
    private float cachedObjectInfoFluidTemperature;
    private float cachedObjectInfoPressureRate;
    private static float fluidDisplayNetworkCacheExpiresAt;
    private static int fluidDisplayStateVersion = 1;

    public Pipe StraightVariantPrefab => straightVariantPrefab != null ? straightVariantPrefab : this;
    public Pipe CornerVariantPrefab => cornerVariantPrefab;
    public Pipe TeeVariantPrefab => teeVariantPrefab;
    public Pipe CrossVariantPrefab => crossVariantPrefab;
    public PipeVariantKind VariantKind => variantKind;
    public int VariantKindId => (int)variantKind;
    public bool IsCornerVariant => variantKind == PipeVariantKind.Corner;
    public bool IsTeeVariant => variantKind == PipeVariantKind.Tee;
    public bool IsCrossVariant => variantKind == PipeVariantKind.Cross;
    internal static int FluidDisplayStateVersion => fluidDisplayStateVersion;

    protected override void OnEnable()
    {
        base.OnEnable();
        InvalidateFluidDisplayNetworkCache();
        fluidDisplaySuppressedForVariantPreview = false;
        Fluidtank.RefreshAllPipeVisuals();
        RefreshFluidDisplayImmediately();
    }

    protected override void OnDisable()
    {
        if (ProjectFApplicationLifecycle.IsQuitting) return;

        InvalidateFluidDisplayNetworkCache();
        SetFluidDisplayVisible(false, true);
        base.OnDisable();
        Fluidtank.RefreshAllPipeVisuals();
    }

    protected override bool UsesManagedVisualUpdates => true;
    protected override bool RequiresManagedVisualUpdate =>
        Time.unscaledTime >= nextFluidDisplayRefreshTime;

    protected override void OnManagedVisualsResumed() => RefreshFluidDisplayImmediately();

    protected override void TickManagedVisuals(float deltaTime)
    {
        if (Time.unscaledTime < nextFluidDisplayRefreshTime)
        {
            return;
        }

        nextFluidDisplayRefreshTime = Time.unscaledTime + FluidDisplayRefreshIntervalSeconds;
        RefreshFluidDisplay(false);
    }

    protected override void OnPlacementRuntimeChanged()
    {
        base.OnPlacementRuntimeChanged();
        InvalidateFluidDisplayNetworkCache();

        // OnEnable runs before a newly placed pipe is bound to its grid coordinate.
        // Refresh again after the runtime placement index is registered so the pipe
        // can resolve adjacent tanks and pipe outputs without requiring a move.
        RefreshFluidDisplayImmediately();
    }

    public void RefreshFluidDisplayImmediately()
    {
        nextObjectInfoFluidRefreshTime = float.NegativeInfinity;
        displayedFluidItemId = NoDisplayedFluidItemId;
        displayedFluidVisible = false;
        nextFluidDisplayRefreshTime = Time.unscaledTime + GetFluidDisplayRefreshOffset();
        RefreshFluidDisplay(true);
    }

    public void SetVariantPreviewFluidDisplaySuppressed(bool suppressed)
    {
        if (fluidDisplaySuppressedForVariantPreview == suppressed)
        {
            return;
        }

        fluidDisplaySuppressedForVariantPreview = suppressed;
        if (suppressed)
        {
            SetFluidDisplayVisible(false, true);
            return;
        }

        RefreshFluidDisplayImmediately();
    }

    public bool TryGetObjectInfoFluidItemId(out int fluidItemId)
    {
        return TryGetObjectInfoFluidInfo(out fluidItemId, out _);
    }

    public bool TryGetObjectInfoFluidInfo(out int fluidItemId, out float temperatureCelsius)
    {
        return TryGetObjectInfoFluidInfo(out fluidItemId, out temperatureCelsius, out _, false);
    }

    public bool TryGetObjectInfoFluidInfo(
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond,
        bool includePressure = true)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        pressureLitersPerSecond = 0f;
        if (includePressure && Time.unscaledTime < nextObjectInfoFluidRefreshTime)
        {
            fluidItemId = cachedObjectInfoFluidItemId;
            temperatureCelsius = cachedObjectInfoFluidTemperature;
            pressureLitersPerSecond = cachedObjectInfoPressureRate;
            return fluidItemId >= 0;
        }

        objectInfoFluidOutputSources.Clear();
        if (!TryResolveObjectInfoPipeCoordinate(out Vector2Int startCoordinate))
        {
            return false;
        }

        return TryGetObjectInfoFluidInfoAtCoordinate(
            startCoordinate,
            out fluidItemId,
            out temperatureCelsius,
            out pressureLitersPerSecond,
            includePressure,
            true);
    }

    internal bool TryGetObjectInfoFluidInfoAtCoordinate(
        Vector2Int startCoordinate,
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond,
        bool includePressure = true,
        bool allowInstanceCache = false)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        pressureLitersPerSecond = 0f;
        bool canUseInstanceCache = allowInstanceCache;
        if (includePressure
            && canUseInstanceCache
            && Time.unscaledTime < nextObjectInfoFluidRefreshTime)
        {
            fluidItemId = cachedObjectInfoFluidItemId;
            temperatureCelsius = cachedObjectInfoFluidTemperature;
            pressureLitersPerSecond = cachedObjectInfoPressureRate;
            return fluidItemId >= 0;
        }

        objectInfoFluidOutputSources.Clear();
        bool foundFluid = TrySearchFluidNetwork(
            startCoordinate,
            false,
            false,
            Vector2Int.zero,
            primaryFluidSearchContext,
            true,
            out fluidItemId,
            out temperatureCelsius,
            includePressure ? objectInfoFluidOutputSources : null,
            this);
        if (includePressure && foundFluid)
        {
            pressureLitersPerSecond = ResolveNetworkPressure(primaryFluidSearchContext,
                objectInfoFluidOutputSources, fluidItemId);
        }

        objectInfoFluidOutputSources.Clear();
        primaryFluidSearchContext.OutputSourcePipeDistances.Clear();
        if (includePressure && canUseInstanceCache)
        {
            cachedObjectInfoFluidItemId = foundFluid ? fluidItemId : -1;
            cachedObjectInfoFluidTemperature = temperatureCelsius;
            cachedObjectInfoPressureRate = pressureLitersPerSecond;
            nextObjectInfoFluidRefreshTime = Time.unscaledTime + FluidDisplayRefreshIntervalSeconds;
        }

        return foundFluid;
    }

    public bool TryGetConnectedFluidItemIdIgnoringStorageCoordinate(
        Vector2Int ignoredStorageCoordinate,
        out int fluidItemId)
    {
        fluidItemId = -1;
        if (!TryResolveObjectInfoPipeCoordinate(out Vector2Int startCoordinate))
        {
            return false;
        }

        return TrySearchFluidNetwork(
            startCoordinate,
            false,
            true,
            ignoredStorageCoordinate,
            fluidIdentitySearchContext,
            false,
            out fluidItemId,
            out _,
            fallbackPipe: this);
    }

    internal bool TryGetConnectedFluidItemIdIgnoringStorageCoordinateAt(
        Vector2Int startCoordinate,
        Vector2Int ignoredStorageCoordinate,
        out int fluidItemId)
    {
        return TrySearchFluidNetwork(
            startCoordinate,
            false,
            true,
            ignoredStorageCoordinate,
            fluidIdentitySearchContext,
            false,
            out fluidItemId,
            out _,
            fallbackPipe: this);
    }

    internal static bool TryGetNetworkFluidInfoAt(
        Vector2Int coordinate,
        FluidNetworkSearchContext context,
        bool hasIgnoredStorageCoordinate,
        Vector2Int ignoredStorageCoordinate,
        bool includePressure,
        out int fluidItemId,
        out float temperatureCelsius,
        out float pressureLitersPerSecond)
    {
        context.Sources.Clear();
        bool found = TrySearchFluidNetwork(
            coordinate, false, hasIgnoredStorageCoordinate, ignoredStorageCoordinate,
            context, !hasIgnoredStorageCoordinate, out fluidItemId, out temperatureCelsius,
            includePressure ? context.Sources : null);
        if (!found)
        {
            fluidItemId = -1;
        }
        pressureLitersPerSecond = 0f;
        if (found && includePressure)
        {
            pressureLitersPerSecond = ResolveNetworkPressure(context, context.Sources, fluidItemId);
        }
        context.Sources.Clear();
        return found;
    }

    private static bool TrySearchFluidNetwork(
        Vector2Int startCoordinate,
        bool cacheDisplayNetwork,
        bool hasIgnoredStorageCoordinate,
        Vector2Int ignoredStorageCoordinate,
        FluidNetworkSearchContext searchContext,
        bool evaluateFluidTankCompatibility,
        out int fluidItemId,
        out float temperatureCelsius,
        ISet<InputOutputModule> outputSources = null,
        Pipe fallbackPipe = null)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        TerrainGenerator terrain = TerrainGenerator.Active;
        searchContext.Reset();
        EnqueueObjectInfoFluidSearchCoordinate(searchContext, startCoordinate, 0);

        bool foundFluid = false;
        bool foundMobileStorageFallbackFluid = false;
        int mobileStorageFallbackFluidItemId = -1;
        float mobileStorageFallbackTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        int searchedNodeCount = 0;
        bool collectPressureEndpoints = outputSources != null;
        while (searchContext.Queue.Count > 0
               && (collectPressureEndpoints || searchedNodeCount < MaxObjectInfoFluidSearchNodes))
        {
            ObjectInfoFluidSearchNode searchNode = searchContext.Queue.Dequeue();
            Vector2Int coordinate = searchNode.Coordinate;
            if (!searchContext.PipeDistances.TryGetValue(
                    coordinate,
                    out int pipeDistance)
                || pipeDistance != searchNode.PipeDistance)
            {
                continue;
            }

            searchContext.RoutePumps.TryGetValue(coordinate, out searchContext.CurrentPump);
            if (collectPressureEndpoints && searchContext.CurrentPump != null)
            {
                CollectPumpStoredFluid(searchContext, coordinate, pipeDistance);
            }
            searchedNodeCount++;

            // A caller resolving the network on one side of a storage must not
            // cross that storage and discover a different network on its other
            // side. Treat the ignored storage coordinate as a traversal wall,
            // not only as a fluid-identity filter.
            if (hasIgnoredStorageCoordinate && coordinate == ignoredStorageCoordinate)
            {
                continue;
            }

            if (!foundFluid
                && TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(
                    coordinate,
                    searchContext,
                    ref foundMobileStorageFallbackFluid,
                    ref mobileStorageFallbackFluidItemId,
                    ref mobileStorageFallbackTemperatureCelsius,
                    out fluidItemId,
                    out temperatureCelsius))
            {
                foundFluid = true;
                if (!cacheDisplayNetwork && !collectPressureEndpoints)
                {
                    return true;
                }
            }

            Pipe pipe = null;
            Quaternion pipeRotation = Quaternion.identity;
            PipeRuntimeRecord runtimeRecord = null;
            PipeWorld.Current?.TryGetAtCoordinate(coordinate, out runtimeRecord);
            bool hasPipe = coordinate == startCoordinate && fallbackPipe != null
                ? fallbackPipe.TryResolveObjectInfoPipeAtStartCoordinate(startCoordinate, out pipe, out pipeRotation)
                : TryGetPipeAtCoordinate(terrain, coordinate, out pipe, out pipeRotation);
            bool hasFixedFluidTank = TryGetFixedFluidTankAtPipeNetworkCoordinate(
                coordinate,
                searchContext,
                out Fluidtank fixedFluidTank);
            // A pump withdraws from this reservoir, not from producers beyond it.
            // An empty reservoir therefore cannot lend its upstream source rate.
            if (hasFixedFluidTank && searchContext.CurrentPump != null) continue;
            bool hasSteamGeneratorPass =
                InputOutputModule.TryGetSteamGeneratorPipePassAtRuntimeCoordinate(
                    coordinate,
                    out SteamGenerator steamGenerator,
                    out Vector2Int steamPassOtherCoordinate,
                    out Vector2Int steamPassExternalDirection);
            bool pipeConnectsToSteamPass = hasSteamGeneratorPass
                                           && (pipe == null
                                               || (runtimeRecord != null
                                                   ? runtimeRecord.HasConnectionTowardsAt(
                                                       coordinate,
                                                       -steamPassExternalDirection)
                                                   : pipe.HasConnectionTowardsAt(
                                                       coordinate,
                                                       pipeRotation,
                                                       -steamPassExternalDirection)));
            searchContext.PumpPasses.Clear();
            bool hasPumpPass = InputOutputModule.CollectPumpPipePassesAtRuntimeCoordinate(
                coordinate, searchContext.PumpPasses);
            bool hasPassiveFluidPass =
                InputOutputModule.TryGetPassiveFluidPassAtRuntimeCoordinate(
                    coordinate,
                    out _,
                    out Vector2Int passivePassOtherCoordinate,
                    out Vector2Int passivePassExternalDirection);
            // An outlet reached through a dense generator connection may have
            // no pipe or pass-through at its own coordinate. Still collect it.
            if (outputSources != null)
            {
                AppendObjectInfoFluidOutputSourcesAtCoordinate(
                    searchContext,
                    coordinate,
                    Vector2Int.zero,
                    pipeDistance,
                    outputSources);
            }

            if ((!hasPipe || pipe == null)
                && !hasFixedFluidTank
                && !hasSteamGeneratorPass
                && !hasPumpPass
                && !hasPassiveFluidPass)
            {
                continue;
            }

            if (pipeConnectsToSteamPass)
            {
                EnqueueObjectInfoFluidSearchCoordinate(
                    searchContext,
                    steamPassOtherCoordinate,
                    pipeDistance);
                if (InputOutputModule.TryGetOverlappingSteamSourcePort(
                        steamGenerator, out Vector2Int sourceCoordinate))
                {
                    EnqueueObjectInfoFluidSearchCoordinate(
                        searchContext,
                        sourceCoordinate,
                        pipeDistance);
                }
            }

            if (hasPumpPass)
            {
                // Keep the loss between the inspected pipe and this pump, then
                // stop adding the upstream section. The pump supplies a fresh
                // pressure section on the inspected side.
                for (int passIndex = 0; passIndex < searchContext.PumpPasses.Count; passIndex++)
                {
                    if (!searchContext.PumpPasses[passIndex].Pump.AllowsRuntimeFluidTraversal(coordinate, true)) continue;
                    EnqueueObjectInfoFluidSearchCoordinate(searchContext,
                        searchContext.PumpPasses[passIndex].OtherCoordinate,
                        FreezeObjectInfoPressureDistance(pipeDistance), searchContext.PumpPasses[passIndex].Pump);
                }
                EnqueueInterlockedPumpFluidSearchCoordinates(searchContext, coordinate, pipeDistance);
            }

            if (hasPassiveFluidPass)
            {
                EnqueueObjectInfoFluidSearchCoordinate(
                    searchContext,
                    passivePassOtherCoordinate,
                    pipeDistance);
            }

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int direction = CardinalDirections[i];
                bool pipeConnectsToDirection = pipe != null
                    && (runtimeRecord != null
                        ? runtimeRecord.HasConnectionTowardsAt(coordinate, direction)
                        : pipe.HasConnectionTowardsAt(coordinate, pipeRotation, direction));
                if (pipe != null && !hasPumpPass && !hasPassiveFluidPass
                    && !pipeConnectsToDirection)
                {
                    continue;
                }

                if (((pipe == null && hasSteamGeneratorPass)
                     || hasPumpPass
                     || hasPassiveFluidPass)
                    && !hasFixedFluidTank
                    // A pipe installed on a Pump endpoint can turn sideways.
                    // Preserve its connectors as well as the Pump's axial port.
                    && !(hasPumpPass && (HasPumpSearchExternalDirection(searchContext, direction)
                                         || pipeConnectsToDirection))
                    && !(hasPassiveFluidPass && direction == passivePassExternalDirection)
                    && !(hasSteamGeneratorPass && direction == steamPassExternalDirection))
                {
                    continue;
                }

                Vector2Int neighborCoordinate = coordinate + direction;
                if (hasIgnoredStorageCoordinate
                    && neighborCoordinate == ignoredStorageCoordinate)
                {
                    continue;
                }

                bool hasNeighborPipe = TryGetPipeAtCoordinate(terrain, neighborCoordinate, out Pipe neighborPipe, out Quaternion neighborRotation);
                bool hasNeighborFixedFluidTank = TryGetFixedFluidTankAtPipeNetworkCoordinate(
                    neighborCoordinate,
                    searchContext,
                    out Fluidtank neighborFixedFluidTank);
                bool hasNeighborSteamGeneratorPass =
                    InputOutputModule.TryGetSteamGeneratorPipePassAtRuntimeCoordinate(
                        neighborCoordinate,
                        out _,
                        out _,
                        out Vector2Int neighborSteamPassExternalDirection)
                    && neighborSteamPassExternalDirection == -direction;
                bool hasNeighborPumpPass = InputOutputModule.HasRuntimePumpPipePassTowards(neighborCoordinate, -direction);
                bool hasNeighborPassiveFluidPass =
                    InputOutputModule.HasRuntimePassiveFluidPassTowards(
                        neighborCoordinate,
                        -direction);
                PipeRuntimeRecord neighborRuntimeRecord = null;
                PipeWorld.Current?.TryGetAtCoordinate(neighborCoordinate, out neighborRuntimeRecord);
                bool fluidTankBoundaryCanConnect = CanTraverseFluidTankBoundary(
                    fixedFluidTank,
                    coordinate,
                    direction,
                    neighborFixedFluidTank,
                    evaluateFluidTankCompatibility);
                bool neighborConnects = fluidTankBoundaryCanConnect
                                        && (hasNeighborSteamGeneratorPass
                                            || hasNeighborPumpPass
                                            || hasNeighborPassiveFluidPass
                                            || (hasNeighborPipe || hasNeighborFixedFluidTank)
                                            && (!hasNeighborPipe
                                                || (neighborRuntimeRecord != null
                                                    ? neighborRuntimeRecord.HasConnectionTowardsAt(
                                                        neighborCoordinate,
                                                        -direction)
                                                    : neighborPipe.HasConnectionTowardsAt(
                                                        neighborCoordinate,
                                                        neighborRotation,
                                                        -direction))));
                bool neighborOutputFacesPipe = !neighborConnects && !foundFluid
                    && fluidTankBoundaryCanConnect
                    && InputOutputModule.HasRuntimeFluidOutputTowardsPipe(
                        neighborCoordinate, -direction);
                if ((neighborConnects || neighborOutputFacesPipe)
                    && !foundFluid
                    && TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(
                        neighborCoordinate,
                        searchContext,
                        ref foundMobileStorageFallbackFluid,
                        ref mobileStorageFallbackFluidItemId,
                        ref mobileStorageFallbackTemperatureCelsius,
                        out fluidItemId,
                        out temperatureCelsius))
                {
                    foundFluid = true;
                    if (!cacheDisplayNetwork && !collectPressureEndpoints)
                    {
                        return true;
                    }
                }

                if (neighborConnects)
                {
                    EnqueueObjectInfoFluidSearchCoordinate(
                        searchContext,
                        neighborCoordinate,
                        AddObjectInfoPipeDistance(
                            pipeDistance,
                            hasNeighborPipe
                            && !hasNeighborPumpPass
                            && !hasNeighborPassiveFluidPass
                                ? 1
                                : 0));
                }
                else if (!hasNeighborPipe && !hasNeighborFixedFluidTank && outputSources != null)
                {
                    AppendObjectInfoFluidOutputSourcesAtCoordinate(
                        searchContext,
                        neighborCoordinate,
                        -direction,
                        pipeDistance,
                        outputSources);
                }
            }

            Vector2Int remoteCoordinate = default;
            bool hasRemoteConnection = pipe != null && !hasPumpPass && !hasPassiveFluidPass
                && (runtimeRecord != null
                    ? runtimeRecord.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate)
                    : pipe.TryGetRemoteConnectionCoordinate(coordinate, out remoteCoordinate));
            if (hasRemoteConnection)
            {
                EnqueueObjectInfoFluidSearchCoordinate(
                    searchContext,
                    remoteCoordinate,
                    IsObjectInfoPressureDistanceFrozen(pipeDistance)
                        ? pipeDistance
                        : AddRemoteTraversalPipeDistance(
                            pipeDistance,
                            coordinate,
                            remoteCoordinate));
            }
        }

        // Mobile fluid storage may establish the identity of an empty pipe
        // network, but it must never replace an identity already supplied by a
        // pump, fixed tank, or other connected fluid endpoint.
        if (!foundFluid && foundMobileStorageFallbackFluid)
        {
            fluidItemId = mobileStorageFallbackFluidItemId;
            temperatureCelsius = mobileStorageFallbackTemperatureCelsius;
            foundFluid = true;
        }

        if (cacheDisplayNetwork)
        {
            foreach (Vector2Int coordinate in searchContext.Visited)
            {
                FluidDisplayNetworkItemCache[coordinate] = fluidItemId;
            }
        }

        return foundFluid;
    }

    private static bool HasPumpSearchExternalDirection(FluidNetworkSearchContext context, Vector2Int direction)
    {
        for (int i = 0; i < context.PumpPasses.Count; i++)
        {
            if (context.PumpPasses[i].ExternalDirection == direction) return true;
        }
        return false;
    }

    private static void EnqueueInterlockedPumpFluidSearchCoordinates(
        FluidNetworkSearchContext context, Vector2Int coordinate, int pipeDistance)
    {
        context.PumpModules.Clear();
        InputOutputModule.CollectModulesAtRuntimeAreaCoordinate(coordinate, context.PumpModules);
        InputOutputModule.CollectModulesAtRuntimeGridCoordinate(coordinate, context.PumpModules);
        for (int i = 0; i < context.PumpPasses.Count; i++)
        {
            Pump source = context.PumpPasses[i].Pump;
            for (int j = 0; j < context.PumpModules.Count; j++)
            {
                if (context.PumpModules[j] is Pump candidate
                    && !source.AllowsRuntimeFluidTraversal(coordinate, true)
                    && candidate.TryGetRuntimeInterlockedEndpoint(source, coordinate, out Vector2Int endpoint)
                    && candidate.AllowsRuntimeFluidTraversal(endpoint, true))
                {
                    EnqueueObjectInfoFluidSearchCoordinate(context, endpoint, FreezeObjectInfoPressureDistance(pipeDistance), candidate);
                }
            }
        }
        context.PumpModules.Clear();
    }

    private static void RecordPumpDistance(FluidNetworkSearchContext context, Pump pump, int distance)
    {
        if (pump == null) return;
        distance = ResolveObjectInfoPressureDistance(distance);
        if (!context.PumpDistances.TryGetValue(pump, out int previous) || distance < previous)
            context.PumpDistances[pump] = distance;
    }

    private static void CollectPumpStoredFluid(FluidNetworkSearchContext context, Vector2Int coordinate, int distance)
    {
        Pump pump = context.CurrentPump;
        if (pump == null) return;
        context.StorageScratch.Clear();
        CollectActiveInstallationsAtRuntimeGridCoordinate(coordinate, context.StorageScratch);
        if (InputOutputModule.TryGetRuntimePipeDisplayFluidStorageAtCoordinate(coordinate, null, out InstallationObject areaStorage)
            && areaStorage != null
            && CanDisplayStoredFluidAtCoordinate(areaStorage, coordinate, context.FluidItemIds)
            && !context.StorageScratch.Contains(areaStorage))
            context.StorageScratch.Add(areaStorage);
        for (int i = 0; i < context.StorageScratch.Count; i++)
        {
            InstallationObject storage = context.StorageScratch[i];
            if (storage != null
                && storage.StoredFluidLiters > 0.0001f
                && CanDisplayStoredFluidAtCoordinate(storage, coordinate, context.FluidItemIds))
            {
                context.StoragePumps[storage] = pump;
                RecordPumpDistance(context, pump, distance);
            }
        }
        context.StorageScratch.Clear();
    }

    private static float ResolveNetworkPressure(FluidNetworkSearchContext context,
        IEnumerable<InputOutputModule> sources, int fluidItemId)
    {
        float pressure = 0f;
        context.PumpRates.Clear();
        foreach (InputOutputModule source in sources)
        {
            if (source == null) continue;
            float rate = source.GetObjectInfoFluidPressureLitersPerSecond(fluidItemId);
            context.SourcePumps.TryGetValue(source, out Pump pump);
            if (pump == null)
            {
                context.OutputSourcePipeDistances.TryGetValue(source, out int distance);
                pressure += rate * CalculateFluidPressureRetention(distance);
            }
            else
            {
                context.PumpRates.TryGetValue(pump, out float previous);
                context.PumpRates[pump] = Pump.LimitTransportRate(pump, previous + rate);
            }
        }
        // Only real reserves can supply the pump independently of collection.
        // Consumers still remove the requested volume from these same storages.
        foreach (var pair in context.StoragePumps)
        {
            if (pair.Key.StoredFluidItemId == fluidItemId && pair.Key.StoredFluidLiters > 0.0001f)
                context.PumpRates[pair.Value] = pair.Value.PressureLitersPerSecond;
        }
        foreach (var pair in context.PumpRates)
        {
            context.PumpDistances.TryGetValue(pair.Key, out int distance);
            pressure += pair.Value * CalculateFluidPressureRetention(distance);
        }
        return pressure;
    }

    private static void AppendObjectInfoFluidOutputSourcesAtCoordinate(
        FluidNetworkSearchContext searchContext,
        Vector2Int coordinate,
        Vector2Int directionToPipe,
        int pipeDistance,
        ISet<InputOutputModule> outputSources)
    {
        CollectPumpStoredFluid(searchContext, coordinate, pipeDistance);
        pipeDistance = ResolveObjectInfoPressureDistance(pipeDistance);
        HashSet<InputOutputModule> objectInfoFluidOutputSourceScratch = searchContext.SourceScratch;
        objectInfoFluidOutputSourceScratch.Clear();
        InputOutputModule.AppendFluidOutputSourcesAtCoordinate(
            coordinate,
            directionToPipe,
            objectInfoFluidOutputSourceScratch);
        foreach (InputOutputModule source in objectInfoFluidOutputSourceScratch)
        {
            outputSources.Add(source);
            if (!searchContext.OutputSourcePipeDistances.TryGetValue(
                    source,
                    out int previousDistance)
                || pipeDistance < previousDistance)
            {
                searchContext.OutputSourcePipeDistances[source] = pipeDistance;
                searchContext.SourcePumps[source] = searchContext.CurrentPump;
                RecordPumpDistance(searchContext, searchContext.CurrentPump, pipeDistance);
            }
        }

        objectInfoFluidOutputSourceScratch.Clear();
    }

    private static bool CanTraverseFluidTankBoundary(
        Fluidtank fixedFluidTank,
        Vector2Int coordinate,
        Vector2Int direction,
        Fluidtank neighborFixedFluidTank,
        bool evaluateNetworkCompatibility = true)
    {
        if (fixedFluidTank == null && neighborFixedFluidTank == null)
        {
            return true;
        }

        if (evaluateNetworkCompatibility)
        {
            return fixedFluidTank != null
                ? fixedFluidTank.HasFluidNetworkConnectionTowards(coordinate, direction)
                : neighborFixedFluidTank.HasFluidNetworkConnectionTowards(
                    coordinate + direction,
                    -direction);
        }

        if (fixedFluidTank == null || neighborFixedFluidTank == null)
        {
            return true;
        }

        // A nested tank-side identity lookup must not recursively launch
        // another network search. Direct stored identities are sufficient to
        // reject known mismatches while an empty tank remains able to join and
        // inherit the established fluid identity of its neighbor.
        int fluidItemId = fixedFluidTank.StoredFluidItemId;
        int neighborFluidItemId = neighborFixedFluidTank.StoredFluidItemId;
        return fluidItemId < 0
               || neighborFluidItemId < 0
               || fluidItemId == neighborFluidItemId;
    }

    internal bool TryGetFixedFluidTankAtPipeNetworkCoordinate(
        Vector2Int coordinate,
        out Fluidtank fixedTank)
    {
        return TryGetFixedFluidTankAtPipeNetworkCoordinate(coordinate, primaryFluidSearchContext, out fixedTank);
    }

    private static bool TryGetFixedFluidTankAtPipeNetworkCoordinate(
        Vector2Int coordinate,
        FluidNetworkSearchContext context,
        out Fluidtank fixedTank)
    {
        fixedTank = null;
        List<InstallationObject> objectInfoFluidStorageScratch = context.StorageScratch;
        objectInfoFluidStorageScratch.Clear();
        if (!CollectActiveInstallationsAtRuntimeGridCoordinate(
                coordinate,
                objectInfoFluidStorageScratch))
        {
            return false;
        }

        for (int i = 0; i < objectInfoFluidStorageScratch.Count; i++)
        {
            if (objectInfoFluidStorageScratch[i] is Fluidtank candidate
                && candidate.isActiveAndEnabled
                && !candidate.IsFlatCarMounted)
            {
                fixedTank = candidate;
                objectInfoFluidStorageScratch.Clear();
                return true;
            }
        }

        objectInfoFluidStorageScratch.Clear();
        return false;
    }

    private static bool TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(
        Vector2Int coordinate,
        FluidNetworkSearchContext context,
        ref bool foundMobileStorageFallbackFluid,
        ref int mobileStorageFallbackFluidItemId,
        ref float mobileStorageFallbackTemperatureCelsius,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        if (!TryGetFluidInfoAtPipeNetworkCoordinate(
                coordinate,
                context,
                out int candidateFluidItemId,
                out float candidateTemperatureCelsius,
                out bool isMobileStorageFallbackFluid))
        {
            fluidItemId = -1;
            temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
            return false;
        }

        if (!isMobileStorageFallbackFluid)
        {
            fluidItemId = candidateFluidItemId;
            temperatureCelsius = candidateTemperatureCelsius;
            return true;
        }

        if (!foundMobileStorageFallbackFluid)
        {
            foundMobileStorageFallbackFluid = true;
            mobileStorageFallbackFluidItemId = candidateFluidItemId;
            mobileStorageFallbackTemperatureCelsius = candidateTemperatureCelsius;
        }

        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        return false;
    }

    public virtual bool HasConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        if (direction == Vector2Int.zero)
        {
            return false;
        }

        switch (variantKind)
        {
            case PipeVariantKind.Cross:
                return direction == Vector2Int.up
                       || direction == Vector2Int.right
                       || direction == Vector2Int.down
                       || direction == Vector2Int.left;
            case PipeVariantKind.Tee:
                return HasResolvedConnection(rotation, localTeeFirstDirection, direction)
                       || HasResolvedConnection(rotation, localTeeSecondDirection, direction)
                       || HasResolvedConnection(rotation, localTeeThirdDirection, direction);
            case PipeVariantKind.Corner:
                return HasResolvedConnection(rotation, localCornerFirstDirection, direction)
                       || HasResolvedConnection(rotation, localCornerSecondDirection, direction);
            default:
                return HasResolvedConnection(rotation, localStraightDirection, direction)
                       || HasResolvedConnection(rotation, OppositeFacingDirection(localStraightDirection), direction);
        }
    }

    public virtual bool HasConnectionTowardsAt(
        Vector2Int coordinate,
        Quaternion rotation,
        Vector2Int direction)
    {
        return HasConnectionTowards(rotation, direction);
    }

    public virtual bool TryGetRemoteConnectionCoordinate(
        Vector2Int coordinate,
        out Vector2Int remoteCoordinate)
    {
        remoteCoordinate = default;
        return false;
    }

    public static int AddRemoteTraversalPipeDistance(
        int currentPipeDistance,
        Vector2Int coordinate,
        Vector2Int remoteCoordinate)
    {
        long installedLength = System.Math.Abs((long)remoteCoordinate.x - coordinate.x)
                               + System.Math.Abs((long)remoteCoordinate.y - coordinate.y);
        long totalDistance = System.Math.Max(0L, currentPipeDistance)
                             + System.Math.Max(1L, installedLength);
        return totalDistance >= int.MaxValue ? int.MaxValue : (int)totalDistance;
    }

    public int GetConnectionMask(Quaternion rotation)
    {
        int connectionMask = 0;
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            if (HasConnectionTowards(rotation, CardinalDirections[i]))
            {
                connectionMask |= 1 << i;
            }
        }

        return connectionMask;
    }

    public bool TryGetPrimaryConnectionDirection(
        Quaternion rotation,
        out Vector2Int direction)
    {
        return TryResolveDirection(rotation, localStraightDirection, out direction);
    }

    private bool TryResolveObjectInfoPipeCoordinate(out Vector2Int coordinate)
    {
        if (TryGetPlacementRuntime(out coordinate, out _))
        {
            return true;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        Vector3 position = transform.position;
        coordinate = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
        if (terrain != null && terrain.TryGetLoadedBlock(coordinate, out _))
        {
            return true;
        }

        coordinate = Vector2Int.zero;
        return false;
    }

    private bool TryResolveObjectInfoPipeAtStartCoordinate(
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion pipeRotation)
    {
        if (TryGetPipeAtCoordinate(TerrainGenerator.Active, coordinate, out pipe, out pipeRotation))
        {
            return true;
        }

        pipe = this;
        pipeRotation = transform.rotation;
        return true;
    }

    private static void EnqueueObjectInfoFluidSearchCoordinate(
        FluidNetworkSearchContext searchContext,
        Vector2Int coordinate,
        int pipeDistance,
        Pump crossedPump = null)
    {
        if (searchContext.PipeDistances.TryGetValue(
                coordinate,
                out int previousDistance)
            && !IsBetterObjectInfoPressureDistance(pipeDistance, previousDistance))
        {
            return;
        }

        searchContext.RoutePumps[coordinate] = Pump.ResolvePressureLimit(searchContext.CurrentPump, crossedPump);
        searchContext.PipeDistances[coordinate] = pipeDistance;
        searchContext.Visited.Add(coordinate);
        searchContext.Queue.Enqueue(
            new ObjectInfoFluidSearchNode(coordinate, pipeDistance));
    }

    private static int FreezeObjectInfoPressureDistance(int pipeDistance)
    {
        return IsObjectInfoPressureDistanceFrozen(pipeDistance)
            ? pipeDistance
            : -Mathf.Max(0, pipeDistance) - 1;
    }

    private static int AddObjectInfoPipeDistance(int pipeDistance, int amount)
    {
        if (IsObjectInfoPressureDistanceFrozen(pipeDistance))
        {
            return pipeDistance;
        }

        long result = (long)Mathf.Max(0, pipeDistance) + Mathf.Max(0, amount);
        return result >= int.MaxValue ? int.MaxValue : (int)result;
    }

    private static bool IsBetterObjectInfoPressureDistance(int candidate, int current)
    {
        int candidateDistance = ResolveObjectInfoPressureDistance(candidate);
        int currentDistance = ResolveObjectInfoPressureDistance(current);
        return candidateDistance < currentDistance
               || candidateDistance == currentDistance
               && IsObjectInfoPressureDistanceFrozen(candidate)
               && !IsObjectInfoPressureDistanceFrozen(current);
    }

    private static bool IsObjectInfoPressureDistanceFrozen(int pipeDistance)
    {
        return pipeDistance < 0;
    }

    private static int ResolveObjectInfoPressureDistance(int pipeDistance)
    {
        if (!IsObjectInfoPressureDistanceFrozen(pipeDistance))
        {
            return Mathf.Max(0, pipeDistance);
        }

        long decodedDistance = -(long)pipeDistance - 1L;
        return decodedDistance >= int.MaxValue ? int.MaxValue : (int)decodedDistance;
    }

    private static bool TryGetFluidInfoAtPipeNetworkCoordinate(
        Vector2Int coordinate,
        FluidNetworkSearchContext context,
        out int fluidItemId,
        out float temperatureCelsius,
        out bool isMobileStorageFallbackFluid)
    {
        if (TryGetStoredFluidInfoAtCoordinate(
                coordinate,
                context,
                out fluidItemId,
                out temperatureCelsius,
                out isMobileStorageFallbackFluid))
        {
            if (!isMobileStorageFallbackFluid)
            {
                return true;
            }

            int mobileStorageFluidItemId = fluidItemId;
            float mobileStorageTemperatureCelsius = temperatureCelsius;
            if (TryGetSourceFluidInfoAtCoordinate(
                    coordinate,
                    out fluidItemId,
                    out temperatureCelsius))
            {
                isMobileStorageFallbackFluid = false;
                return true;
            }

            fluidItemId = mobileStorageFluidItemId;
            temperatureCelsius = mobileStorageTemperatureCelsius;
            return true;
        }

        isMobileStorageFallbackFluid = false;
        return TryGetSourceFluidInfoAtCoordinate(coordinate, out fluidItemId, out temperatureCelsius);
    }

    private static bool TryGetStoredFluidInfoAtCoordinate(
        Vector2Int coordinate,
        FluidNetworkSearchContext context,
        out int fluidItemId,
        out float temperatureCelsius,
        out bool isMobileStorageFallbackFluid)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        isMobileStorageFallbackFluid = false;
        if (InputOutputModule.TryGetRuntimePipeDisplayFluidStorageAtCoordinate(
                coordinate,
                null,
                out InstallationObject areaStorage)
            && areaStorage != null
            && CanDisplayStoredFluidAtCoordinate(
                areaStorage, coordinate, context.FluidItemIds))
        {
            fluidItemId = areaStorage.StoredFluidItemId;
            temperatureCelsius = areaStorage.GetStoredFluidTemperatureCelsius(fluidItemId);
            isMobileStorageFallbackFluid = IsMobileFluidStorageFallback(areaStorage);
            return true;
        }

        if (TryGetRuntimeStoredFluidInfoAtCoordinate(
                coordinate,
                context,
                out fluidItemId,
                out temperatureCelsius,
                out isMobileStorageFallbackFluid))
        {
            return true;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null
            || !(block.MapObject is InstallationObject bodyStorage)
            || bodyStorage is Pipe
            || bodyStorage.StoredFluidItemId < 0
            || !CanDisplayStoredFluidAtCoordinate(bodyStorage, coordinate, context.FluidItemIds))
        {
            return false;
        }

        fluidItemId = bodyStorage.StoredFluidItemId;
        temperatureCelsius = bodyStorage.GetStoredFluidTemperatureCelsius(fluidItemId);
        isMobileStorageFallbackFluid = IsMobileFluidStorageFallback(bodyStorage);
        return true;
    }

    private static bool TryGetRuntimeStoredFluidInfoAtCoordinate(
        Vector2Int coordinate,
        FluidNetworkSearchContext context,
        out int fluidItemId,
        out float temperatureCelsius,
        out bool isMobileStorageFallbackFluid)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        isMobileStorageFallbackFluid = false;
        List<InstallationObject> objectInfoFluidStorageScratch = context.StorageScratch;
        objectInfoFluidStorageScratch.Clear();
        if (!CollectActiveInstallationsAtRuntimeGridCoordinate(
                coordinate,
                objectInfoFluidStorageScratch))
        {
            return false;
        }

        int mobileStorageFluidItemId = -1;
        float mobileStorageTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        for (int i = 0; i < objectInfoFluidStorageScratch.Count; i++)
        {
            InstallationObject storage = objectInfoFluidStorageScratch[i];
            if (storage == null
                || storage is Pipe
                || storage.StoredFluidItemId < 0
                || !CanDisplayStoredFluidAtCoordinate(storage, coordinate, context.FluidItemIds))
            {
                continue;
            }

            int candidateFluidItemId = storage.StoredFluidItemId;
            float candidateTemperatureCelsius = storage.GetStoredFluidTemperatureCelsius(
                candidateFluidItemId);
            if (IsMobileFluidStorageFallback(storage))
            {
                if (mobileStorageFluidItemId < 0)
                {
                    mobileStorageFluidItemId = candidateFluidItemId;
                    mobileStorageTemperatureCelsius = candidateTemperatureCelsius;
                }

                continue;
            }

            fluidItemId = candidateFluidItemId;
            temperatureCelsius = candidateTemperatureCelsius;
            objectInfoFluidStorageScratch.Clear();
            return true;
        }

        objectInfoFluidStorageScratch.Clear();
        if (mobileStorageFluidItemId < 0)
        {
            return false;
        }

        fluidItemId = mobileStorageFluidItemId;
        temperatureCelsius = mobileStorageTemperatureCelsius;
        isMobileStorageFallbackFluid = true;
        return true;
    }

    private static bool IsMobileFluidStorageFallback(InstallationObject storage)
    {
        return storage is SteamTrain
               || storage is Fluidtank fluidTank && fluidTank.IsFlatCarMounted;
    }

    private static bool TryGetSourceFluidInfoAtCoordinate(
        Vector2Int coordinate,
        out int fluidItemId,
        out float temperatureCelsius)
    {
        fluidItemId = -1;
        temperatureCelsius = MapClimate.CurrentTemperatureCelsius;

        // A configured fluid output establishes network identity even while its
        // machine is idle. The runtime output lookup is coordinate-specific, so
        // machine bodies and input cells cannot leak that identity into a pipe.
        if (InputOutputModule.TryGetFluidOutputInfoAtRuntimeGridCoordinate(
                coordinate,
                out int outputItemId,
                out temperatureCelsius)
            && outputItemId >= 0)
        {
            fluidItemId = outputItemId;
            return true;
        }

        return false;
    }

    internal bool CanDisplayStoredFluidAtCoordinate(InstallationObject storage, Vector2Int coordinate)
    {
        return CanDisplayStoredFluidAtCoordinate(storage, coordinate, primaryFluidSearchContext.FluidItemIds);
    }

    private static bool CanDisplayStoredFluidAtCoordinate(
        InstallationObject storage, Vector2Int coordinate, HashSet<int> itemIds)
    {
        if (storage == null || storage.StoredFluidItemId < 0)
        {
            return false;
        }

        InputOutputModule module = storage as InputOutputModule;
        if (module == null)
        {
            return true;
        }

        return module.CanExposeStoredFluidAtRuntimePipeCoordinate(
            coordinate,
            storage.StoredFluidItemId,
            itemIds);
    }

    private static bool TryGetPipeAtCoordinate(
        TerrainGenerator terrain,
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion pipeRotation)
    {
        pipe = null;
        pipeRotation = Quaternion.identity;
        if (terrain == null
            || !terrain.TryGetLoadedBlock(coordinate, out Block block)
            || block == null)
        {
            return false;
        }

        if (block.TryGetRuntimePipe(out Pipe runtimePipe, out Quaternion runtimeRotation))
        {
            pipe = runtimePipe;
            pipeRotation = runtimeRotation;
            return true;
        }

        if (!(block.MapObject is Pipe candidatePipe)
            || !candidatePipe.gameObject.activeInHierarchy)
        {
            return false;
        }

        pipe = candidatePipe;
        pipeRotation = candidatePipe.transform.rotation;
        return true;
    }

    private static bool HasResolvedConnection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        Vector2Int direction)
    {
        return TryResolveDirection(rotation, localDirection, out Vector2Int resolvedDirection)
               && resolvedDirection == direction;
    }

    private static InstallationFacingDirection OppositeFacingDirection(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return InstallationFacingDirection.NegativeX;
            case InstallationFacingDirection.NegativeX:
                return InstallationFacingDirection.PositiveX;
            case InstallationFacingDirection.NegativeZ:
                return InstallationFacingDirection.PositiveZ;
            default:
                return InstallationFacingDirection.NegativeZ;
        }
    }

    private static bool TryResolveDirection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        out Vector2Int resolvedDirection)
    {
        return TryResolveCardinalDirection(rotation * FacingDirectionToVector(localDirection), out resolvedDirection);
    }

    private static bool TryResolveCardinalDirection(Vector3 directionVector, out Vector2Int resolvedDirection)
    {
        resolvedDirection = Vector2Int.zero;
        directionVector.y = 0f;
        if (directionVector.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        directionVector.Normalize();
        if (Mathf.Abs(directionVector.x) >= Mathf.Abs(directionVector.z))
        {
            resolvedDirection = new Vector2Int(directionVector.x >= 0f ? 1 : -1, 0);
        }
        else
        {
            resolvedDirection = new Vector2Int(0, directionVector.z >= 0f ? 1 : -1);
        }

        return true;
    }

    private static Vector3 FacingDirectionToVector(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return Vector3.right;
            case InstallationFacingDirection.NegativeX:
                return Vector3.left;
            case InstallationFacingDirection.NegativeZ:
                return Vector3.back;
            default:
                return Vector3.forward;
        }
    }

    private void RefreshFluidDisplay(bool force)
    {
        if (!ShouldUpdateVisuals)
            return;
        MeshRenderer renderer = ResolveFluidDisplayRenderer();
        if (renderer == null)
        {
            return;
        }

        if (fluidDisplaySuppressedForVariantPreview)
        {
            SetFluidDisplayVisible(false, force);
            return;
        }

        if (!TryGetCachedFluidDisplayItemId(out int fluidItemId))
        {
            displayedFluidItemId = NoDisplayedFluidItemId;
            SetFluidDisplayVisible(false, force);
            return;
        }

        if (!force
            && displayedFluidVisible
            && renderer.enabled
            && displayedFluidItemId == fluidItemId)
        {
            return;
        }

        ApplyFluidDisplayColor(renderer, ResolveFluidDisplayColor(fluidItemId));
        displayedFluidItemId = fluidItemId;
        SetFluidDisplayVisible(true, force);
    }

    private bool TryGetCachedFluidDisplayItemId(out int fluidItemId)
    {
        fluidItemId = -1;
        if (!TryResolveObjectInfoPipeCoordinate(out Vector2Int startCoordinate))
        {
            return false;
        }

        RefreshFluidDisplayNetworkCacheWindow();
        if (FluidDisplayNetworkItemCache.TryGetValue(startCoordinate, out fluidItemId))
        {
            return fluidItemId >= 0;
        }

        return SearchAndCacheFluidDisplayNetwork(startCoordinate, out fluidItemId);
    }

    internal bool TryGetFluidDisplayItemIdAtCoordinate(Vector2Int startCoordinate, out int fluidItemId)
    {
        RefreshFluidDisplayNetworkCacheWindow();
        if (FluidDisplayNetworkItemCache.TryGetValue(startCoordinate, out fluidItemId))
        {
            return fluidItemId >= 0;
        }

        return SearchAndCacheFluidDisplayNetwork(startCoordinate, out fluidItemId);
    }

    private bool SearchAndCacheFluidDisplayNetwork(Vector2Int startCoordinate, out int fluidItemId)
    {
        return TrySearchFluidNetwork(
            startCoordinate,
            true,
            false,
            Vector2Int.zero,
            primaryFluidSearchContext,
            true,
            out fluidItemId,
            out _,
            fallbackPipe: this);
    }

    internal bool TryGetDirectFluidDisplaySource(
        PipeRuntimeRecord record,
        out int fluidItemId,
        out int priority)
    {
        fluidItemId = -1;
        priority = 0;
        if (record == null)
        {
            return false;
        }

        bool foundMobileStorageFallbackFluid = false;
        int mobileStorageFallbackFluidItemId = -1;
        float mobileStorageFallbackTemperatureCelsius = MapClimate.CurrentTemperatureCelsius;
        IReadOnlyList<Vector2Int> coordinates = record.OccupiedCoordinates;
        for (int coordinateIndex = 0; coordinateIndex < coordinates.Count; coordinateIndex++)
        {
            Vector2Int coordinate = coordinates[coordinateIndex];
            if (TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(
                    coordinate,
                    primaryFluidSearchContext,
                    ref foundMobileStorageFallbackFluid,
                    ref mobileStorageFallbackFluidItemId,
                    ref mobileStorageFallbackTemperatureCelsius,
                    out fluidItemId,
                    out _))
            {
                priority = 2;
                return true;
            }

            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = CardinalDirections[directionIndex];
                if (!record.HasConnectionTowardsAt(coordinate, direction)
                    || !TryGetAuthoritativeFluidInfoAtPipeNetworkCoordinate(
                        coordinate + direction,
                        primaryFluidSearchContext,
                        ref foundMobileStorageFallbackFluid,
                        ref mobileStorageFallbackFluidItemId,
                        ref mobileStorageFallbackTemperatureCelsius,
                        out fluidItemId,
                        out _))
                {
                    continue;
                }

                priority = 2;
                return true;
            }
        }

        if (!foundMobileStorageFallbackFluid)
        {
            fluidItemId = -1;
            return false;
        }

        fluidItemId = mobileStorageFallbackFluidItemId;
        priority = 1;
        return fluidItemId >= 0;
    }

    private static void RefreshFluidDisplayNetworkCacheWindow()
    {
        float currentTime = Time.unscaledTime;
        if (currentTime < fluidDisplayNetworkCacheExpiresAt)
        {
            return;
        }

        FluidDisplayNetworkItemCache.Clear();
        fluidDisplayNetworkCacheExpiresAt = currentTime + FluidDisplayRefreshIntervalSeconds;
    }

    internal static void InvalidateFluidDisplayNetworkCache()
    {
        FluidDisplayNetworkItemCache.Clear();
        fluidDisplayNetworkCacheExpiresAt = 0f;
        unchecked
        {
            fluidDisplayStateVersion++;
            if (fluidDisplayStateVersion == 0)
            {
                fluidDisplayStateVersion = 1;
            }
        }
    }

    internal static void InvalidateFluidDisplayNetworkCache(InstallationObject source)
    {
        FluidDisplayNetworkItemCache.Clear();
        fluidDisplayNetworkCacheExpiresAt = 0f;
        TerrainGenerator.Active?.InvalidateFluidJobDisplayNetworks(source);
    }

    private MeshRenderer ResolveFluidDisplayRenderer()
    {
        if (fluidDisplayRendererResolved)
        {
            return fluidDP;
        }

        fluidDisplayRendererResolved = true;
        if (fluidDP != null)
        {
            return fluidDP;
        }

        MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            MeshRenderer renderer = childRenderers[i];
            if (renderer != null && renderer.name == "Fluid DP")
            {
                fluidDP = renderer;
                return fluidDP;
            }
        }

        for (int i = 0; i < childRenderers.Length; i++)
        {
            MeshRenderer renderer = childRenderers[i];
            if (RendererUsesFluidDisplayMaterial(renderer))
            {
                fluidDP = renderer;
                return fluidDP;
            }
        }

        return null;
    }

    private static bool RendererUsesFluidDisplayMaterial(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        Material sharedMaterial = renderer.sharedMaterial;
        return sharedMaterial != null && sharedMaterial.name == "M_Fluid";
    }

    private void ApplyFluidDisplayColor(Renderer renderer, Color color)
    {
        if (fluidDisplayPropertyBlock == null)
        {
            fluidDisplayPropertyBlock = new MaterialPropertyBlock();
        }

        renderer.GetPropertyBlock(fluidDisplayPropertyBlock);
        fluidDisplayPropertyBlock.SetColor(BaseColorShaderId, color);
        fluidDisplayPropertyBlock.SetColor(ColorShaderId, color);
        fluidDisplayPropertyBlock.SetColor(EmissionColorShaderId, color);
        renderer.SetPropertyBlock(fluidDisplayPropertyBlock);
    }

    protected void ApplyFluidDisplayState(Renderer renderer, bool visible, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        if (visible)
        {
            ApplyFluidDisplayColor(renderer, color);
        }

        renderer.enabled = visible;
    }

    protected virtual void OnFluidDisplayStateChanged(bool visible, Color color)
    {
    }

    private void SetFluidDisplayVisible(bool visible, bool force)
    {
        MeshRenderer renderer = ResolveFluidDisplayRenderer();
        if (renderer == null)
        {
            displayedFluidVisible = false;
            OnFluidDisplayStateChanged(false, default);
            return;
        }

        if (force || renderer.enabled != visible)
        {
            renderer.enabled = visible;
        }

        displayedFluidVisible = visible;
        Color color = visible && displayedFluidItemId != NoDisplayedFluidItemId
            ? ResolveFluidDisplayColor(displayedFluidItemId)
            : default;
        OnFluidDisplayStateChanged(visible, color);
    }

    internal static Color ResolveFluidDisplayColor(int fluidItemId)
    {
        ItemDefinition definition = InputOutputModule.ResolveItemDefinition(fluidItemId);
        if (definition != null)
        {
            return definition.fluidDisplayColor;
        }

        return UnknownFluidDisplayColor;
    }

    private float GetFluidDisplayRefreshOffset()
    {
        return (GetInstanceID() & 0x3ff) / 1024f * FluidDisplayRefreshIntervalSeconds;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (variantKind == PipeVariantKind.Straight && straightVariantPrefab == null)
        {
            straightVariantPrefab = this;
        }

        fluidDisplayRendererResolved = false;
        ResolveFluidDisplayRenderer();
        SetFluidDisplayVisible(false, true);
    }
#endif
}
