using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class UtilityPole : InstallationObject
{
    private const float EnergyEpsilon = 0.0001f;
    private const string LinePointCenterName = "LinePointCenter";
    private const string LinePointAName = "LinePointA";
    private const string LinePointBName = "LinePointB";
    private const string LegacyLinePointAName = "LinePointCenter (1)";
    private const string LegacyLinePointBName = "LinePointCenter (2)";
    private const string ConnectionLineRootName = "__UtilityPoleConnectionLines";
    private const int LinePointAIndex = 0;
    private const int LinePointBIndex = 1;
    private const int MaxConnectionsPerLinePoint = 2;
    private const int MaxLineCurvePointCount = 64;
    private const float DistanceTieEpsilon = 0.0001f;
    private static readonly Color SupplyRangeFillColor = new Color(1f, 0.86f, 0.05f, 0.14f);

    private static readonly HashSet<UtilityPole> activePoles = new HashSet<UtilityPole>();
    private static readonly HashSet<UtilityPole> SelectedSupplyRangeVisualInstances = new HashSet<UtilityPole>();
    private static readonly List<UtilityPole> activePoleScratch = new List<UtilityPole>();
    private static readonly Queue<UtilityPole> poleQueue = new Queue<UtilityPole>();
    private static readonly HashSet<UtilityPole> visitedPoles = new HashSet<UtilityPole>();
    private static readonly List<InstallationObject> installationScratch = new List<InstallationObject>();
    private static readonly List<ElectricNetwork> networks = new List<ElectricNetwork>();
    private static readonly List<UtilityPole> connectionPoleScratch = new List<UtilityPole>();
    private static readonly List<PoleConnectionCandidate> connectionCandidateScratch =
        new List<PoleConnectionCandidate>();
    private static readonly List<PoleConnection> poleConnections = new List<PoleConnection>();
    private static readonly List<PoleConnection> previewPoleConnections = new List<PoleConnection>();
    private static readonly List<UtilityPole> visualPoleScratch = new List<UtilityPole>();
    private static readonly HashSet<UtilityPole> screenRangePoles = new HashSet<UtilityPole>();
    private static readonly HashSet<UtilityPole> screenRangePoleScratch = new HashSet<UtilityPole>();
    private static readonly List<WorkableObjectRangeVisualRequest> rangeVisualRequestScratch =
        new List<WorkableObjectRangeVisualRequest>();
    private static readonly HashSet<UtilityPole> rangeVisualPoleScratch = new HashSet<UtilityPole>();
    private static readonly ProjectF.Rendering.CameraRenderCulling rangeVisualCulling = new ProjectF.Rendering.CameraRenderCulling();
    private static readonly List<LineRenderer> previewConsumerLineRenderers = new List<LineRenderer>();
    private static readonly Dictionary<UtilityPole, PreviewPoleRuntime> previewPoleRuntimes =
        new Dictionary<UtilityPole, PreviewPoleRuntime>();
    private static readonly Dictionary<InstallationObject, PreviewConsumerRuntime> previewConsumerRuntimes =
        new Dictionary<InstallationObject, PreviewConsumerRuntime>();
    private static readonly Dictionary<InstallationObject, ElectricNetwork> suppliedConsumerNetworks =
        new Dictionary<InstallationObject, ElectricNetwork>();
    private static readonly Dictionary<UtilityPole, ElectricNetwork> electricNetworkByPole =
        new Dictionary<UtilityPole, ElectricNetwork>();
    private static readonly Dictionary<Vector2Int, List<UtilityPole>> supplyPolesByCoordinate =
        new Dictionary<Vector2Int, List<UtilityPole>>();
    private static readonly Stack<List<UtilityPole>> supplyPoleListPool = new Stack<List<UtilityPole>>();
    private static readonly HashSet<ElectricNetwork> supplyingNetworkScratch = new HashSet<ElectricNetwork>();
    private static readonly HashSet<UtilityPole> consumerPoleScratch = new HashSet<UtilityPole>();
    private static readonly HashSet<InstallationObject> renderedConsumerLineScratch =
        new HashSet<InstallationObject>();

    private static int networkRuntimeEvaluatedFrame = -1;
    private static bool networksDirty = true;
    private static bool poleConnectionsDirty = true;
    private static bool previewPoleConnectionsDirty = true;
    private static bool connectionLineVisualsDirty = true;
    private static bool deferredConnectionLineVisualRefreshRequested;
    private static int deferredConnectionLineVisualRefreshFrame = -1;
    private static bool previewConsumerLineVisualsDirty;
    private static bool deferredPreviewConsumerLineVisualRefreshRequested;
    private static int deferredPreviewConsumerLineVisualRefreshFrame = -1;
    private static int usedPreviewConsumerLineRendererCount;
    private static WorkableObjectRangeVisual sharedSupplyRangeVisual;
    private static WorkableObjectRangeVisual sharedConnectionRangeVisual;
    private static bool installOrEditSupplyRangeVisualsRequested;
    private static bool installOrEditConnectionRangeVisualsRequested;
    private static bool screenRangePolesInitialized;
    private static Material sharedLineMaterial;
    private static Transform connectionLineRoot;

    [SerializeField, Min(0f)]
    private float supplyRangeVisualYOffset = 0.055f;

    private bool selectedSupplyRangeVisualRequested;
    private bool selectedConnectionRangeVisualRequested;

    [SerializeField]
    private Transform linePointCenter;
    [SerializeField]
    private Transform linePointA, linePointB;
    [SerializeField, Min(0.001f)]
    private float lineWidth = 0.025f;
    [SerializeField, Min(0f)]
    private float lineSagDepth = 0.06f;
    [SerializeField, Min(0f)]
    private float connectionLineSagDepth = 0.18f;
    [SerializeField, Min(2)]
    private int lineCurveSegments = 8;
    [SerializeField]
    private Color lineColor = new Color(0.05f, 0.04f, 0.035f, 1f);

    public int ConnectionRadiusCells
    {
        get
        {
            ItemDefinition definition = ResolvePoleDefinition();
            return definition != null ? Mathf.Max(0, definition.utilityPoleConnectionRadius) : 0;
        }
    }

    public int SupplyRadiusCells
    {
        get
        {
            ItemDefinition definition = ResolvePoleDefinition();
            return definition != null ? Mathf.Max(0, definition.utilityPoleSupplyRadius) : 0;
        }
    }

    private LineRenderer lineCenterToA;
    private LineRenderer lineCenterToB;
    private readonly List<LineRenderer> connectionLineRenderers = new List<LineRenderer>(4);
    private int usedConnectionLineRendererCount;
    private int linePointAConnectionCount;
    private int linePointBConnectionCount;

    private int ExternalConnectionCount
    {
        get
        {
            return linePointAConnectionCount + linePointBConnectionCount;
        }
    }

    static UtilityPole()
    {
        InstallationObject.PlacementRuntimeChanged += HandleInstallationPlacementRuntimeChanged;
        InstallationObject.PlacementRuntimeCleared += HandleInstallationPlacementRuntimeCleared;
    }

    protected new void Awake()
    {
        base.Awake();
        DestroyLegacyChildConnectionLineRenderers();
        ResolveLinePointReferences();
        EnsureLineRenderers();
        RefreshLineRenderers();
    }

    public void SetSelectedSupplyRangeVisualRequested(bool requested)
    {
        SetSelectedSupplyRangeVisualRequested(requested, true);
    }

    public void SetSelectedSupplyRangeVisualRequested(bool requested, bool connectionRangeRequested)
    {
        bool nextConnectionRangeRequested = requested && connectionRangeRequested;
        if (selectedSupplyRangeVisualRequested == requested
            && selectedConnectionRangeVisualRequested == nextConnectionRangeRequested)
        {
            return;
        }

        selectedSupplyRangeVisualRequested = requested;
        selectedConnectionRangeVisualRequested = nextConnectionRangeRequested;
        if (requested)
        {
            SelectedSupplyRangeVisualInstances.Add(this);
        }
        else
        {
            SelectedSupplyRangeVisualInstances.Remove(this);
        }

        RefreshSupplyRangeVisual();
    }

    public static void RefreshAllRangeVisuals()
    {
        RefreshSelectedSupplyRangeVisual();
        RefreshSelectedConnectionRangeVisual();
    }

    public static void RefreshPoleTopologyNow()
    {
        MarkPoleTopologyDirty();
        RefreshConnectionLineRenderersIfDirty();
        RefreshAllRangeVisuals();
    }

    public static void RegisterBlueprintPreview(
        UtilityPole pole,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        bool topologyReplacement = false)
    {
        if (pole == null)
        {
            return;
        }

        pole.ResolveLinePointReferences();
        pole.EnsureLineRenderers();
        pole.RefreshLineRenderers();
        PreviewPoleRuntime nextRuntime = new PreviewPoleRuntime(
            anchorCoordinate,
            ((quarterTurns % 4) + 4) % 4,
            topologyReplacement);
        if (previewPoleRuntimes.TryGetValue(pole, out PreviewPoleRuntime currentRuntime)
            && currentRuntime.AnchorCoordinate == nextRuntime.AnchorCoordinate
            && currentRuntime.QuarterTurns == nextRuntime.QuarterTurns
            && currentRuntime.TopologyReplacement == nextRuntime.TopologyReplacement)
        {
            return;
        }

        previewPoleRuntimes[pole] = nextRuntime;
        MarkPreviewPoleConnectionsDirty();
        RequestDeferredConnectionLineVisualRefresh();
    }

    public static void UnregisterBlueprintPreview(UtilityPole pole)
    {
        if (pole == null || !previewPoleRuntimes.Remove(pole))
        {
            return;
        }

        pole.HideConnectionLineRenderers();
        MarkPreviewPoleConnectionsDirty();
        RequestDeferredConnectionLineVisualRefresh();
    }

    public static void RegisterConsumerBlueprintPreview(
        InstallationObject consumer,
        Vector2Int anchorCoordinate,
        int quarterTurns)
    {
        if (!CanRenderConsumerPowerLine(consumer))
        {
            UnregisterConsumerBlueprintPreview(consumer);
            return;
        }

        PreviewConsumerRuntime nextRuntime = new PreviewConsumerRuntime(
            anchorCoordinate,
            ((quarterTurns % 4) + 4) % 4);
        if (previewConsumerRuntimes.TryGetValue(consumer, out PreviewConsumerRuntime currentRuntime)
            && currentRuntime.AnchorCoordinate == nextRuntime.AnchorCoordinate
            && currentRuntime.QuarterTurns == nextRuntime.QuarterTurns)
        {
            return;
        }

        previewConsumerRuntimes[consumer] = nextRuntime;
        previewConsumerLineVisualsDirty = true;
        RequestDeferredPreviewConsumerLineVisualRefresh();
    }

    public static void UnregisterConsumerBlueprintPreview(InstallationObject consumer)
    {
        if (consumer == null || !previewConsumerRuntimes.Remove(consumer))
        {
            return;
        }

        previewConsumerLineVisualsDirty = true;
        RequestDeferredPreviewConsumerLineVisualRefresh();
    }

    public static void ClearBlueprintPreviews()
    {
        if (previewPoleRuntimes.Count <= 0 && previewConsumerRuntimes.Count <= 0)
        {
            return;
        }

        visualPoleScratch.Clear();
        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            if (entry.Key != null)
            {
                visualPoleScratch.Add(entry.Key);
            }
        }

        for (int i = 0; i < visualPoleScratch.Count; i++)
        {
            visualPoleScratch[i].HideConnectionLineRenderers();
        }

        visualPoleScratch.Clear();
        previewPoleRuntimes.Clear();
        previewConsumerRuntimes.Clear();
        HidePreviewConsumerLineRenderers();
        MarkPreviewPoleConnectionsDirty();
        previewConsumerLineVisualsDirty = false;
        deferredPreviewConsumerLineVisualRefreshRequested = false;
        RefreshConnectionLineRenderersIfDirty();
    }

    public static void SetInstallOrEditRangeVisualsRequested(
        bool supplyRangeRequested,
        bool connectionRangeRequested)
    {
        bool nextConnectionRangeRequested = supplyRangeRequested && connectionRangeRequested;
        if (installOrEditSupplyRangeVisualsRequested == supplyRangeRequested
            && installOrEditConnectionRangeVisualsRequested == nextConnectionRangeRequested)
        {
            return;
        }

        installOrEditSupplyRangeVisualsRequested = supplyRangeRequested;
        installOrEditConnectionRangeVisualsRequested = nextConnectionRangeRequested;
        screenRangePolesInitialized = false;
        screenRangePoles.Clear();
        screenRangePoleScratch.Clear();
        RefreshAllRangeVisuals();
    }

    public static bool InstallOrEditRangeVisualsRequested =>
        installOrEditSupplyRangeVisualsRequested || installOrEditConnectionRangeVisualsRequested;

    public static void RefreshScreenRangeVisuals(Camera targetCamera)
    {
        if (!InstallOrEditRangeVisualsRequested
            || !IsInstallOrEditModeActive()
            || targetCamera == null)
        {
            return;
        }

        rangeVisualCulling.Update(targetCamera);
        screenRangePoleScratch.Clear();
        foreach (UtilityPole pole in activePoles)
        {
            if (pole == null
                || !IsValidPlacedPole(pole)
                || !pole.TryGetSupplyRangeBounds(out Bounds rangeBounds)
                || !rangeVisualCulling.Intersects(rangeBounds))
            {
                continue;
            }

            screenRangePoleScratch.Add(pole);
        }

        if (screenRangePolesInitialized && screenRangePoles.SetEquals(screenRangePoleScratch))
        {
            return;
        }

        screenRangePoles.Clear();
        screenRangePoles.UnionWith(screenRangePoleScratch);
        screenRangePolesInitialized = true;
        RefreshAllRangeVisuals();
    }

    public bool TryGetSupplyRangeBounds(out Bounds bounds)
    {
        bounds = default;
        int radiusCells = SupplyRadiusCells;
        if (radiusCells < 0)
        {
            return false;
        }

        float rangeRadius = radiusCells + 0.5f;
        if (rangeRadius <= 0f)
        {
            return false;
        }

        Vector3 center = GetSupplyRangeCenter();
        float rangeSize = rangeRadius * 2f;
        bounds = new Bounds(
            center,
            new Vector3(rangeSize, 0.01f, rangeSize));
        return true;
    }

    public bool TryGetConnectionRangeBounds(out Bounds bounds)
    {
        bounds = default;
        int radiusCells = ConnectionRadiusCells;
        if (radiusCells < 0)
        {
            return false;
        }

        float rangeRadius = radiusCells + 0.5f;
        if (rangeRadius <= 0f)
        {
            return false;
        }

        Vector3 center = GetSupplyRangeCenter();
        float rangeSize = rangeRadius * 2f;
        bounds = new Bounds(
            center,
            new Vector3(rangeSize, 0.01f, rangeSize));
        return true;
    }

    public static bool HasElectricityAvailable(InputOutputModule consumer)
    {
        return HasElectricityAvailable((InstallationObject)consumer);
    }

    public static bool HasElectricityAvailable(InstallationObject consumer)
    {
        if (consumer == null)
        {
            return false;
        }

        if (IsFreeElectroEnergyEnabled())
        {
            return true;
        }

        EnsureNetworksEvaluated();
        ElectricNetwork network = ResolveBestNetworkForConsumer(consumer);
        return network != null
               && network.HasPowerSource
               && network.ProductionWatts > EnergyEpsilon;
    }

    public static bool TryGetElectricPowerInfo(
        InstallationObject consumer,
        out float suppliedWatts,
        out float requiredWatts)
    {
        suppliedWatts = 0f;
        requiredWatts = 0f;
        if (!TryGetElectricPowerRequirement(consumer, out requiredWatts))
        {
            return false;
        }

        if (IsFreeElectroEnergyEnabled())
        {
            suppliedWatts = requiredWatts;
            return true;
        }

        EnsureNetworksEvaluated();
        ElectricNetwork network = ResolveBestNetworkForConsumer(consumer);
        if (network == null || !network.HasPowerSource || network.ProductionWatts <= EnergyEpsilon)
        {
            return true;
        }

        float networkRequiredWatts = Mathf.Max(requiredWatts, network.RequiredWatts);
        float supplyRatio = networkRequiredWatts > EnergyEpsilon
            ? Mathf.Clamp01(network.ProductionWatts / networkRequiredWatts)
            : network.SupplyRatio;
        suppliedWatts = Mathf.Min(requiredWatts, requiredWatts * supplyRatio);
        return true;
    }

    public static bool TryConsumeElectricity(
        InputOutputModule consumer,
        float requestedEnergy,
        float deltaTime,
        out float consumedEnergy)
    {
        return TryConsumeElectricity(
            (InstallationObject)consumer,
            requestedEnergy,
            deltaTime,
            out consumedEnergy);
    }

    public static bool TryConsumeElectricity(
        InstallationObject consumer,
        float requestedEnergy,
        float deltaTime,
        out float consumedEnergy)
    {
        consumedEnergy = 0f;
        if (consumer == null || requestedEnergy <= EnergyEpsilon)
        {
            return false;
        }

        if (IsFreeElectroEnergyEnabled())
        {
            consumedEnergy = requestedEnergy;
            return true;
        }

        float requestedWatts = deltaTime > EnergyEpsilon ? requestedEnergy / deltaTime : 0f;
        if (requestedWatts <= EnergyEpsilon && TryGetElectricPowerRequirement(consumer, out float configuredWatts))
        {
            requestedWatts = configuredWatts;
        }

        if (!TryGetElectricSupplyRatio(consumer, requestedWatts, out float supplyRatio))
        {
            return false;
        }

        consumedEnergy = requestedEnergy * supplyRatio;
        return consumedEnergy > EnergyEpsilon;
    }

    public static bool TryGetElectricSupplyRatio(
        InstallationObject consumer,
        float requestedWatts,
        out float supplyRatio)
    {
        supplyRatio = 0f;
        if (consumer == null)
        {
            return false;
        }

        if (IsFreeElectroEnergyEnabled())
        {
            supplyRatio = 1f;
            return true;
        }

        EnsureNetworksEvaluated();
        ElectricNetwork network = ResolveBestNetworkForConsumer(consumer);
        if (network == null || !network.HasPowerSource || network.ProductionWatts <= EnergyEpsilon)
        {
            return false;
        }

        float effectiveDemandWatts = network.RequiredWatts;
        float clampedRequestedWatts = Mathf.Max(0f, requestedWatts);
        if (TryGetElectricPowerRequirement(consumer, out _))
        {
            effectiveDemandWatts = Mathf.Max(effectiveDemandWatts, clampedRequestedWatts);
        }
        else
        {
            effectiveDemandWatts += clampedRequestedWatts;
        }

        supplyRatio = effectiveDemandWatts > EnergyEpsilon
            ? Mathf.Clamp01(network.ProductionWatts / effectiveDemandWatts)
            : 1f;
        return true;
    }

    public bool TryGetObjectInfoNetworkPower(out float productionWatts, out float requiredWatts)
    {
        productionWatts = 0f;
        requiredWatts = 0f;
        if (!IsValidPlacedPole(this))
        {
            return false;
        }

        EnsureNetworksEvaluated();
        for (int i = 0; i < networks.Count; i++)
        {
            ElectricNetwork network = networks[i];
            if (network == null || !network.Poles.Contains(this))
            {
                continue;
            }

            productionWatts = network.HasPowerSource ? Mathf.Max(0f, network.ProductionWatts) : 0f;
            requiredWatts = network.HasPowerSource ? Mathf.Max(0f, network.RequiredWatts) : 0f;
            return true;
        }

        return false;
    }

    public bool CaptureConnectedPoleAnchorCoordinates(List<Vector2Int> destination)
    {
        if (destination == null)
        {
            return false;
        }

        destination.Clear();
        if (!IsValidPlacedPole(this))
        {
            return false;
        }

        EnsurePoleConnectionsEvaluated();
        for (int i = 0; i < poleConnections.Count; i++)
        {
            UtilityPole connectedPole = GetConnectedPole(poleConnections[i], this);
            if (connectedPole == null
                || !connectedPole.TryGetPlacementRuntime(
                    out Vector2Int connectedAnchor,
                    out _)
                || destination.Contains(connectedAnchor))
            {
                continue;
            }

            destination.Add(connectedAnchor);
        }

        destination.Sort(ComparePoleAnchorCoordinates);
        return true;
    }

    private static int ComparePoleAnchorCoordinates(Vector2Int left, Vector2Int right)
    {
        int xComparison = left.x.CompareTo(right.x);
        return xComparison != 0 ? xComparison : left.y.CompareTo(right.y);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        DestroyLegacyChildConnectionLineRenderers();
        ResolveLinePointReferences();
        EnsureLineRenderers();
        RefreshLineRenderers();
        activePoles.Add(this);
        if (selectedSupplyRangeVisualRequested)
        {
            SelectedSupplyRangeVisualInstances.Add(this);
        }

        MarkPoleTopologyDirty();
        RefreshConnectionLineRenderersIfDirty();
        RefreshSupplyRangeVisual();
    }

    protected override void OnDisable()
    {
        HideConnectionLineRenderers();
        SelectedSupplyRangeVisualInstances.Remove(this);
        selectedSupplyRangeVisualRequested = false;
        selectedConnectionRangeVisualRequested = false;

        activePoles.Remove(this);
        MarkPoleTopologyDirty();
        RefreshConnectionLineRenderersIfDirty();
        RefreshSupplyRangeVisual();
        base.OnDisable();
    }

    private void LateUpdate()
    {
        FlushDeferredConnectionLineVisualRefresh();
        FlushDeferredPreviewConsumerLineVisualRefresh();
    }

    private void OnDestroy()
    {
        HideConnectionLineRenderers();
        SelectedSupplyRangeVisualInstances.Remove(this);
        activePoles.Remove(this);
        MarkPoleTopologyDirty();
        RefreshConnectionLineRenderersIfDirty();
        RefreshSupplyRangeVisual();
        DestroyConnectionLineRenderers();
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        SelectedSupplyRangeVisualInstances.Remove(this);
        selectedSupplyRangeVisualRequested = false;
        selectedConnectionRangeVisualRequested = false;
        RefreshLineRenderers();
        HideConnectionLineRenderers();
        MarkPoleTopologyDirty();
        RefreshConnectionLineRenderersIfDirty();
        RefreshSupplyRangeVisual();
    }

    private static void MarkPoleTopologyDirty()
    {
        MarkElectricNetworkDirty();
        poleConnectionsDirty = true;
        previewPoleConnectionsDirty = true;
        connectionLineVisualsDirty = true;
    }

    private static void MarkPreviewPoleConnectionsDirty()
    {
        previewPoleConnectionsDirty = true;
        connectionLineVisualsDirty = true;
    }

    private static void MarkElectricNetworkDirty()
    {
        networksDirty = true;
        networkRuntimeEvaluatedFrame = -1;
        connectionLineVisualsDirty = true;
        InputOutputModule.WakeElectricRuntimeModules();
    }

    public static void NotifyElectricPowerSourceStateChanged()
    {
        networkRuntimeEvaluatedFrame = -1;
        InputOutputModule.WakeElectricRuntimeModules();
    }

    public static void NotifyFreeElectroEnergyChanged()
    {
        networkRuntimeEvaluatedFrame = -1;
        InputOutputModule.WakeElectricRuntimeModules();
    }

    private static bool IsFreeElectroEnergyEnabled()
    {
        return GameManager.Instance != null && GameManager.Instance.FreeElectroEnergy;
    }

    private static void HandleInstallationPlacementRuntimeChanged(InstallationObject installationObject)
    {
        if (installationObject is UtilityPole pole)
        {
            pole.ResolveLinePointReferences();
            pole.EnsureLineRenderers();
            pole.RefreshLineRenderers();
            MarkPoleTopologyDirty();
            RefreshConnectionLineRenderersIfDirty();
            RefreshAllRangeVisuals();
            return;
        }

        RefreshNetworkParticipantPlacement(installationObject, false);
    }

    private static void HandleInstallationPlacementRuntimeCleared(InstallationObject installationObject)
    {
        if (installationObject is UtilityPole pole)
        {
            pole.HideConnectionLineRenderers();
            MarkPoleTopologyDirty();
            RefreshConnectionLineRenderersIfDirty();
            RefreshAllRangeVisuals();
            return;
        }

        RefreshNetworkParticipantPlacement(installationObject, true);
    }

    private static void RefreshNetworkParticipantPlacement(
        InstallationObject installationObject,
        bool placementCleared)
    {
        if (!IsElectricNetworkParticipant(installationObject))
        {
            return;
        }

        // 전봇대 연결 구조는 그대로이므로 소비자 하나의 설치/회수 때문에 모든 전봇대의
        // 공급 범위를 다시 스캔하지 않는다. 초기 로드나 전봇대 변경으로 네트워크 자체가
        // dirty인 경우에만 기존 전체 재구축 경로를 사용한다.
        if (networksDirty)
        {
            connectionLineVisualsDirty = true;
            RequestDeferredConnectionLineVisualRefresh();
            return;
        }

        supplyingNetworkScratch.Clear();
        if (!placementCleared)
        {
            CollectSupplyingNetworks(installationObject, supplyingNetworkScratch);
        }

        bool membershipChanged = false;
        for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
        {
            ElectricNetwork network = networks[networkIndex];
            if (network == null)
            {
                continue;
            }

            bool shouldBelong = supplyingNetworkScratch.Contains(network);
            bool belongs = network.SuppliedInstallations.Contains(installationObject);
            if (shouldBelong == belongs)
            {
                continue;
            }

            if (shouldBelong)
            {
                network.SuppliedInstallations.Add(installationObject);
            }
            else
            {
                network.SuppliedInstallations.Remove(installationObject);
            }

            RefreshNetworkTopologyRuntimeValues(network);
            membershipChanged = true;
        }

        suppliedConsumerNetworks.Remove(installationObject);
        if (membershipChanged)
        {
            networkRuntimeEvaluatedFrame = -1;
            RefreshNetworkRuntimeValues(true);
            InputOutputModule.WakeElectricRuntimeModules();
        }

        supplyingNetworkScratch.Clear();

        connectionLineVisualsDirty = true;
        RequestDeferredConnectionLineVisualRefresh();
    }

    private static void CollectSupplyingNetworks(
        InstallationObject installationObject,
        HashSet<ElectricNetwork> destination)
    {
        if (installationObject == null
            || destination == null
            || installationObject.RuntimeOccupiedCoordinates == null)
        {
            return;
        }

        for (int coordinateIndex = 0;
             coordinateIndex < installationObject.RuntimeOccupiedCoordinates.Count;
             coordinateIndex++)
        {
            Vector2Int coordinate = installationObject.RuntimeOccupiedCoordinates[coordinateIndex];
            if (!supplyPolesByCoordinate.TryGetValue(coordinate, out List<UtilityPole> poles))
            {
                continue;
            }

            for (int poleIndex = 0; poleIndex < poles.Count; poleIndex++)
            {
                UtilityPole pole = poles[poleIndex];
                if (pole != null
                    && electricNetworkByPole.TryGetValue(pole, out ElectricNetwork network))
                {
                    destination.Add(network);
                }
            }
        }
    }

    private ItemDefinition ResolvePoleDefinition()
    {
        if (BoundItemDefinition != null)
        {
            return BoundItemDefinition;
        }

        return InputOutputModule.ResolveItemDefinition(ResolveItemId());
    }

    private Vector3 GetSupplyRangeCenter()
    {
        if (TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return new Vector3(anchorCoordinate.x, transform.position.y, anchorCoordinate.y);
        }

        return transform.position;
    }

    private void ResolveLinePointReferences()
    {
        if (linePointCenter == null || !linePointCenter.IsChildOf(transform))
        {
            linePointCenter = FindDescendantByName(transform, LinePointCenterName);
        }

        if (linePointA == null || !linePointA.IsChildOf(transform))
        {
            linePointA = FindDescendantByName(transform, LinePointAName)
                         ?? FindDescendantByName(transform, LegacyLinePointAName);
        }

        if (linePointB == null || !linePointB.IsChildOf(transform))
        {
            linePointB = FindDescendantByName(transform, LinePointBName)
                         ?? FindDescendantByName(transform, LegacyLinePointBName);
        }
    }

    private void EnsureLineRenderers()
    {
        lineCenterToA = EnsureLineRenderer(lineCenterToA, "UtilityPole_Line_Center_A");
        lineCenterToB = EnsureLineRenderer(lineCenterToB, "UtilityPole_Line_Center_B");
    }

    private LineRenderer EnsureLineRenderer(LineRenderer lineRenderer, string lineName)
    {
        return EnsureLineRenderer(lineRenderer, lineName, transform, false);
    }

    private LineRenderer EnsureLineRenderer(
        LineRenderer lineRenderer,
        string lineName,
        Transform lineParent,
        bool useWorldSpace)
    {
        Transform resolvedParent = lineParent != null ? lineParent : transform;
        if (lineRenderer == null)
        {
            Transform existing = resolvedParent.Find(lineName);
            if (existing != null)
            {
                lineRenderer = existing.GetComponent<LineRenderer>();
            }
        }

        if (lineRenderer == null)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(resolvedParent, false);
            lineRenderer = lineObject.AddComponent<LineRenderer>();
        }
        else
        {
            lineRenderer.gameObject.name = lineName;
            if (lineRenderer.transform.parent != resolvedParent)
            {
                lineRenderer.transform.SetParent(resolvedParent, false);
            }
        }

        lineRenderer.useWorldSpace = useWorldSpace;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 0;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.sharedMaterial = GetLineMaterial();
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
        return lineRenderer;
    }

    private void RefreshLineRenderers()
    {
        RefreshLineRenderer(lineCenterToA, linePointCenter, linePointA, lineSagDepth);
        RefreshLineRenderer(lineCenterToB, linePointCenter, linePointB, lineSagDepth);
    }

    private void RefreshLineRenderer(
        LineRenderer lineRenderer,
        Transform startPoint,
        Transform endPoint,
        float sagDepth)
    {
        if (lineRenderer == null)
        {
            return;
        }

        bool visible = startPoint != null
                       && endPoint != null
                       && lineWidth > 0f
                       && gameObject.activeInHierarchy;
        if (lineRenderer.gameObject.activeSelf != visible)
        {
            lineRenderer.gameObject.SetActive(visible);
        }

        lineRenderer.enabled = visible;
        if (!visible)
        {
            return;
        }

        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;

        int pointCount = Mathf.Clamp(lineCurveSegments + 1, 2, MaxLineCurvePointCount);
        if (lineRenderer.positionCount != pointCount)
        {
            lineRenderer.positionCount = pointCount;
        }

        for (int i = 0; i < pointCount; i++)
        {
            float t = pointCount > 1 ? (float)i / (pointCount - 1) : 0f;
            float sag = 4f * t * (1f - t) * Mathf.Max(0f, sagDepth);
            Vector3 curvedPoint = Vector3.Lerp(
                ResolveLinePointWorldPosition(startPoint),
                ResolveLinePointWorldPosition(endPoint),
                t) + Vector3.down * sag;
            lineRenderer.SetPosition(
                i,
                lineRenderer.useWorldSpace
                    ? curvedPoint
                    : lineRenderer.transform.InverseTransformPoint(curvedPoint));
        }
    }

    private static Vector3 ResolveLinePointWorldPosition(Transform linePoint)
    {
        if (linePoint == null)
        {
            return Vector3.zero;
        }

        UtilityPole owner = linePoint.GetComponentInParent<UtilityPole>();
        return owner != null
            ? owner.ResolveOwnedLinePointWorldPosition(linePoint)
            : linePoint.position;
    }

    private Vector3 ResolveOwnedLinePointWorldPosition(Transform linePoint)
    {
        if (linePoint == null)
        {
            return transform.position;
        }

        if (!linePoint.IsChildOf(transform))
        {
            return linePoint.position;
        }

        Vector3 localPosition = Vector3.zero;
        Transform current = linePoint;
        while (current != null && current != transform)
        {
            localPosition = current.localRotation * Vector3.Scale(localPosition, current.localScale)
                            + current.localPosition;
            current = current.parent;
        }

        return transform.position + transform.rotation * localPosition;
    }

    private static void RefreshConnectionLineRenderersIfDirty()
    {
        if (!connectionLineVisualsDirty)
        {
            return;
        }

        RefreshConnectionLineRenderers();
    }

    private static void RequestDeferredConnectionLineVisualRefresh()
    {
        if (!Application.isPlaying)
        {
            RefreshConnectionLineRenderersIfDirty();
            return;
        }

        deferredConnectionLineVisualRefreshRequested = true;
    }

    private static void FlushDeferredConnectionLineVisualRefresh()
    {
        if (!deferredConnectionLineVisualRefreshRequested
            || deferredConnectionLineVisualRefreshFrame == Time.frameCount)
        {
            return;
        }

        deferredConnectionLineVisualRefreshRequested = false;
        deferredConnectionLineVisualRefreshFrame = Time.frameCount;
        RefreshConnectionLineRenderersIfDirty();
    }

    private static void RequestDeferredPreviewConsumerLineVisualRefresh()
    {
        if (!Application.isPlaying)
        {
            RefreshPreviewConsumerLineRenderersIfDirty();
            return;
        }

        deferredPreviewConsumerLineVisualRefreshRequested = true;
    }

    private static void FlushDeferredPreviewConsumerLineVisualRefresh()
    {
        if (!deferredPreviewConsumerLineVisualRefreshRequested
            || deferredPreviewConsumerLineVisualRefreshFrame == Time.frameCount)
        {
            return;
        }

        deferredPreviewConsumerLineVisualRefreshRequested = false;
        deferredPreviewConsumerLineVisualRefreshFrame = Time.frameCount;
        RefreshPreviewConsumerLineRenderersIfDirty();
    }

    private static void RefreshPreviewConsumerLineRenderersIfDirty()
    {
        if (!previewConsumerLineVisualsDirty)
        {
            return;
        }

        RefreshPreviewConsumerLineRenderers();
    }

    private static void RefreshConnectionLineRenderers()
    {
        CleanupPreviewPoleRuntimes();
        CleanupPreviewConsumerRuntimes();
        if (activePoles.Count <= 0 && previewPoleRuntimes.Count <= 0)
        {
            connectionLineVisualsDirty = false;
            return;
        }

        bool previewReplacesPlacedConnections = HasTopologyReplacementPreview();
        if (!previewReplacesPlacedConnections)
        {
            EnsurePoleConnectionsEvaluated();
        }

        EnsurePreviewPoleConnectionsEvaluated();
        previewReplacesPlacedConnections = HasTopologyReplacementPreview();
        EnsureNetworksEvaluated(false);
        BuildVisualPoleScratch();
        foreach (UtilityPole pole in visualPoleScratch)
        {
            if (pole != null)
            {
                pole.BeginConnectionLineVisualRefresh();
            }
        }

        if (!previewReplacesPlacedConnections)
        {
            for (int i = 0; i < poleConnections.Count; i++)
            {
                PoleConnection connection = poleConnections[i];
                if (connection.FirstPole == null || connection.SecondPole == null)
                {
                    continue;
                }

                UtilityPole owner = connection.FirstPole.GetInstanceID() <= connection.SecondPole.GetInstanceID()
                    ? connection.FirstPole
                    : connection.SecondPole;
                owner.RenderConnectionLine(connection.FirstPoint, connection.SecondPoint);
            }
        }

        for (int i = 0; i < previewPoleConnections.Count; i++)
        {
            PoleConnection connection = previewPoleConnections[i];
            if (connection.FirstPole == null || connection.SecondPole == null)
            {
                continue;
            }

            UtilityPole owner = connection.FirstPole.GetInstanceID() <= connection.SecondPole.GetInstanceID()
                ? connection.FirstPole
                : connection.SecondPole;
            owner.RenderConnectionLine(connection.FirstPoint, connection.SecondPoint);
        }

        if (!previewReplacesPlacedConnections)
        {
            RenderConsumerPowerLines();
        }

        foreach (UtilityPole pole in visualPoleScratch)
        {
            if (pole != null)
            {
                pole.CompleteConnectionLineVisualRefresh();
            }
        }

        connectionLineVisualsDirty = false;
        visualPoleScratch.Clear();
        previewConsumerLineVisualsDirty = true;
        RequestDeferredPreviewConsumerLineVisualRefresh();
    }

    private static void RenderConsumerPowerLines()
    {
        renderedConsumerLineScratch.Clear();
        foreach (KeyValuePair<InstallationObject, ElectricNetwork> entry in suppliedConsumerNetworks)
        {
            InstallationObject consumer = entry.Key;
            if (!CanRenderConsumerPowerLine(consumer)
                || !entry.Value.HasPowerSource
                || !TryResolveConsumerPowerLinePole(entry.Value, consumer, out UtilityPole supplyingPole))
            {
                continue;
            }

            supplyingPole.RenderConsumerPowerLine(consumer);
            renderedConsumerLineScratch.Add(consumer);
        }

        for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
        {
            ElectricNetwork network = networks[networkIndex];
            if (network == null || network.SuppliedInstallations == null)
            {
                continue;
            }

            foreach (InstallationObject consumer in network.SuppliedInstallations)
            {
                if (renderedConsumerLineScratch.Contains(consumer)
                    || !CanRenderConsumerPowerLine(consumer)
                    || !TryResolveConsumerPowerLinePole(null, consumer, out UtilityPole supplyingPole))
                {
                    continue;
                }

                supplyingPole.RenderConsumerPowerLine(consumer);
                renderedConsumerLineScratch.Add(consumer);
            }
        }

        renderedConsumerLineScratch.Clear();
    }

    private static void RefreshPreviewConsumerLineRenderers()
    {
        CleanupPreviewConsumerRuntimes();
        usedPreviewConsumerLineRendererCount = 0;
        if (previewConsumerRuntimes.Count <= 0)
        {
            HideUnusedPreviewConsumerLineRenderers();
            previewConsumerLineVisualsDirty = false;
            return;
        }

        foreach (KeyValuePair<InstallationObject, PreviewConsumerRuntime> entry in previewConsumerRuntimes)
        {
            InstallationObject consumer = entry.Key;
            if (!CanRenderConsumerPowerLine(consumer)
                || !TryResolvePreviewConsumerPowerLinePole(entry.Value, consumer, out UtilityPole supplyingPole))
            {
                continue;
            }

            supplyingPole.RenderPreviewConsumerPowerLine(consumer);
        }

        HideUnusedPreviewConsumerLineRenderers();
        previewConsumerLineVisualsDirty = false;
    }

    private static bool CanRenderConsumerPowerLine(InstallationObject consumer)
    {
        return consumer != null
               && !(consumer is UtilityPole)
               && consumer.gameObject.activeInHierarchy
               && TryGetElectricPowerRequirement(consumer, out _)
               && consumer.TryGetPowerLinePoint(out _);
    }

    private static bool TryResolveConsumerPowerLinePole(
        ElectricNetwork preferredNetwork,
        InstallationObject consumer,
        out UtilityPole supplyingPole)
    {
        supplyingPole = null;
        if (consumer == null || !consumer.TryGetPowerLinePoint(out Transform consumerPoint))
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        if (preferredNetwork != null)
        {
            TryResolveConsumerPowerLinePoleInNetwork(
                preferredNetwork,
                consumer,
                consumerPoint,
                ref supplyingPole,
                ref bestDistanceSqr);
            return supplyingPole != null;
        }

        for (int i = 0; i < networks.Count; i++)
        {
            TryResolveConsumerPowerLinePoleInNetwork(
                networks[i],
                consumer,
                consumerPoint,
                ref supplyingPole,
                ref bestDistanceSqr);
        }

        return supplyingPole != null;
    }

    private static bool TryResolvePreviewConsumerPowerLinePole(
        PreviewConsumerRuntime previewRuntime,
        InstallationObject consumer,
        out UtilityPole supplyingPole)
    {
        supplyingPole = null;
        if (consumer == null || !consumer.TryGetPowerLinePoint(out Transform consumerPoint))
        {
            return false;
        }

        float bestDistanceSqr = float.MaxValue;
        consumerPoleScratch.Clear();
        int sizeX = Mathf.Max(1, consumer.Status.mapSizeX);
        int sizeY = Mathf.Max(1, consumer.Status.mapSizeY);
        Vector2Int centerCell = consumer.PlacementCenterCell;
        for (int y = 0; y < sizeY; y++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                Vector2Int localOffset = new Vector2Int(x - centerCell.x, y - centerCell.y);
                Vector2Int coordinate = previewRuntime.AnchorCoordinate
                                        + InputOutputModule.RotateRectGridOffset(
                                            localOffset,
                                            previewRuntime.QuarterTurns);
                if (!supplyPolesByCoordinate.TryGetValue(coordinate, out List<UtilityPole> poles))
                {
                    continue;
                }

                for (int poleIndex = 0; poleIndex < poles.Count; poleIndex++)
                {
                    UtilityPole pole = poles[poleIndex];
                    if (pole != null
                        && consumerPoleScratch.Add(pole))
                    {
                        TrySelectConsumerPowerLinePole(
                            pole,
                            consumerPoint,
                            ref supplyingPole,
                            ref bestDistanceSqr);
                    }
                }
            }
        }

        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            UtilityPole pole = entry.Key;
            if (!IsValidPreviewPole(pole)
                || !consumerPoleScratch.Add(pole)
                || !PoleSuppliesPreviewConsumer(pole, consumer, previewRuntime))
            {
                continue;
            }

            TrySelectConsumerPowerLinePole(
                pole,
                consumerPoint,
                ref supplyingPole,
                ref bestDistanceSqr);
        }

        consumerPoleScratch.Clear();
        return supplyingPole != null;
    }

    private static void TryResolveConsumerPowerLinePoleInNetwork(
        ElectricNetwork network,
        InstallationObject consumer,
        Transform consumerPoint,
        ref UtilityPole supplyingPole,
        ref float bestDistanceSqr)
    {
        if (network == null
            || consumer == null
            || consumerPoint == null
            || consumer.RuntimeOccupiedCoordinates == null)
        {
            return;
        }

        consumerPoleScratch.Clear();
        for (int coordinateIndex = 0;
             coordinateIndex < consumer.RuntimeOccupiedCoordinates.Count;
             coordinateIndex++)
        {
            Vector2Int coordinate = consumer.RuntimeOccupiedCoordinates[coordinateIndex];
            if (!supplyPolesByCoordinate.TryGetValue(coordinate, out List<UtilityPole> poles))
            {
                continue;
            }

            for (int poleIndex = 0; poleIndex < poles.Count; poleIndex++)
            {
                UtilityPole pole = poles[poleIndex];
                if (pole == null
                    || !consumerPoleScratch.Add(pole)
                    || !electricNetworkByPole.TryGetValue(pole, out ElectricNetwork poleNetwork)
                    || poleNetwork != network)
                {
                    continue;
                }

                TrySelectConsumerPowerLinePole(
                    pole,
                    consumerPoint,
                    ref supplyingPole,
                    ref bestDistanceSqr);
            }
        }

        consumerPoleScratch.Clear();
    }

    private static void TrySelectConsumerPowerLinePole(
        UtilityPole pole,
        Transform consumerPoint,
        ref UtilityPole supplyingPole,
        ref float bestDistanceSqr)
    {
        if (!IsValidPlacedPole(pole) && !IsValidPreviewPole(pole))
        {
            return;
        }

        pole.ResolveLinePointReferences();
        if (pole.linePointCenter == null)
        {
            return;
        }

        float distanceSqr = (
            ResolveLinePointWorldPosition(pole.linePointCenter)
            - ResolveLinePointWorldPosition(consumerPoint)).sqrMagnitude;
        if (!IsBetterConsumerPowerLinePole(pole, supplyingPole, distanceSqr, bestDistanceSqr))
        {
            return;
        }

        supplyingPole = pole;
        bestDistanceSqr = distanceSqr;
    }

    private static bool IsBetterConsumerPowerLinePole(
        UtilityPole candidate,
        UtilityPole current,
        float candidateDistanceSqr,
        float currentDistanceSqr)
    {
        if (candidate == null)
        {
            return false;
        }

        if (current == null)
        {
            return true;
        }

        float difference = candidateDistanceSqr - currentDistanceSqr;
        if (Mathf.Abs(difference) > DistanceTieEpsilon)
        {
            return difference < 0f;
        }

        return candidate.GetInstanceID() < current.GetInstanceID();
    }

    private static bool PoleSuppliesPreviewConsumer(
        UtilityPole pole,
        InstallationObject consumer,
        PreviewConsumerRuntime consumerRuntime)
    {
        if (pole == null
            || consumer == null
            || !TryGetPoleAnchorCoordinate(pole, out Vector2Int poleAnchor))
        {
            return false;
        }

        int radius = pole.SupplyRadiusCells;
        int sizeX = Mathf.Max(1, consumer.Status.mapSizeX);
        int sizeY = Mathf.Max(1, consumer.Status.mapSizeY);
        Vector2Int centerCell = consumer.PlacementCenterCell;
        for (int y = 0; y < sizeY; y++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                Vector2Int localOffset = new Vector2Int(x - centerCell.x, y - centerCell.y);
                Vector2Int rotatedOffset = InputOutputModule.RotateRectGridOffset(
                    localOffset,
                    consumerRuntime.QuarterTurns);
                if (ChebyshevDistance(poleAnchor, consumerRuntime.AnchorCoordinate + rotatedOffset) <= radius)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RenderConsumerPowerLine(InstallationObject consumer)
    {
        if (consumer == null
            || linePointCenter == null
            || !consumer.TryGetPowerLinePoint(out Transform consumerPoint))
        {
            return;
        }

        RenderConnectionLine(linePointCenter, consumerPoint);
    }

    private void RenderPreviewConsumerPowerLine(InstallationObject consumer)
    {
        if (consumer == null
            || linePointCenter == null
            || !consumer.TryGetPowerLinePoint(out Transform consumerPoint))
        {
            return;
        }

        LineRenderer lineRenderer = EnsurePreviewConsumerLineRenderer(usedPreviewConsumerLineRendererCount, this);
        usedPreviewConsumerLineRendererCount++;
        RefreshLineRenderer(lineRenderer, linePointCenter, consumerPoint, ResolveConnectionLineSagDepth(linePointCenter, consumerPoint));
    }

    private void BeginConnectionLineVisualRefresh()
    {
        usedConnectionLineRendererCount = 0;
    }

    private void CompleteConnectionLineVisualRefresh()
    {
        for (int i = usedConnectionLineRendererCount; i < connectionLineRenderers.Count; i++)
        {
            SetLineRendererVisible(connectionLineRenderers[i], false);
        }
    }

    private void HideConnectionLineRenderers()
    {
        usedConnectionLineRendererCount = 0;
        ResetLinePointConnections();
        for (int i = 0; i < connectionLineRenderers.Count; i++)
        {
            SetLineRendererVisible(connectionLineRenderers[i], false);
        }
    }

    private void DestroyConnectionLineRenderers()
    {
        usedConnectionLineRendererCount = 0;
        for (int i = 0; i < connectionLineRenderers.Count; i++)
        {
            LineRenderer lineRenderer = connectionLineRenderers[i];
            if (lineRenderer == null)
            {
                continue;
            }

            GameObject lineObject = lineRenderer.gameObject;
            if (Application.isPlaying)
            {
                Destroy(lineObject);
            }
            else
            {
                DestroyImmediate(lineObject);
            }
        }

        connectionLineRenderers.Clear();
    }

    private void DestroyLegacyChildConnectionLineRenderers()
    {
        LineRenderer[] lineRenderers = GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < lineRenderers.Length; i++)
        {
            LineRenderer lineRenderer = lineRenderers[i];
            if (lineRenderer == null
                || lineRenderer.transform == transform
                || !lineRenderer.transform.IsChildOf(transform)
                || !lineRenderer.gameObject.name.StartsWith("UtilityPole_Line_Connection_"))
            {
                continue;
            }

            GameObject lineObject = lineRenderer.gameObject;
            if (Application.isPlaying)
            {
                Destroy(lineObject);
            }
            else
            {
                DestroyImmediate(lineObject);
            }
        }
    }

    private void RenderConnectionLine(Transform startPoint, Transform endPoint)
    {
        LineRenderer lineRenderer = EnsureConnectionLineRenderer(usedConnectionLineRendererCount);
        usedConnectionLineRendererCount++;
        RefreshLineRenderer(lineRenderer, startPoint, endPoint, ResolveConnectionLineSagDepth(startPoint, endPoint));
    }

    private float ResolveConnectionLineSagDepth(Transform startPoint, Transform endPoint)
    {
        Vector3 startPosition = ResolveLinePointWorldPosition(startPoint);
        Vector3 endPosition = ResolveLinePointWorldPosition(endPoint);
        float distance = Vector3.Distance(startPosition, endPosition);
        float radius = Mathf.Max(1f, ConnectionRadiusCells);
        float distanceRatio = Mathf.Clamp01(distance / radius);
        return Mathf.Max(lineSagDepth, connectionLineSagDepth * distanceRatio);
    }

    private LineRenderer EnsureConnectionLineRenderer(int index)
    {
        while (connectionLineRenderers.Count <= index)
        {
            connectionLineRenderers.Add(null);
        }

        LineRenderer lineRenderer = EnsureLineRenderer(
            connectionLineRenderers[index],
            $"UtilityPole_Line_Connection_{GetInstanceID()}_{index}",
            GetConnectionLineRoot(),
            true);
        connectionLineRenderers[index] = lineRenderer;
        return lineRenderer;
    }

    private static LineRenderer EnsurePreviewConsumerLineRenderer(int index, UtilityPole styleSource)
    {
        while (previewConsumerLineRenderers.Count <= index)
        {
            previewConsumerLineRenderers.Add(null);
        }

        UtilityPole owner = styleSource != null ? styleSource : ResolveAnyActivePole();
        if (owner == null)
        {
            return null;
        }

        LineRenderer lineRenderer = owner.EnsureLineRenderer(
            previewConsumerLineRenderers[index],
            $"UtilityPole_Line_PreviewConsumer_{index}",
            GetConnectionLineRoot(),
            true);
        previewConsumerLineRenderers[index] = lineRenderer;
        return lineRenderer;
    }

    private static UtilityPole ResolveAnyActivePole()
    {
        foreach (UtilityPole pole in activePoles)
        {
            if (pole != null)
            {
                return pole;
            }
        }

        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            if (entry.Key != null)
            {
                return entry.Key;
            }
        }

        return null;
    }

    private static void HidePreviewConsumerLineRenderers()
    {
        usedPreviewConsumerLineRendererCount = 0;
        HideUnusedPreviewConsumerLineRenderers();
    }

    private static void HideUnusedPreviewConsumerLineRenderers()
    {
        for (int i = usedPreviewConsumerLineRendererCount; i < previewConsumerLineRenderers.Count; i++)
        {
            SetLineRendererVisible(previewConsumerLineRenderers[i], false);
        }
    }

    private static Transform GetConnectionLineRoot()
    {
        if (connectionLineRoot != null)
        {
            return connectionLineRoot;
        }

        GameObject rootObject = GameObject.Find(ConnectionLineRootName);
        if (rootObject == null)
        {
            rootObject = new GameObject(ConnectionLineRootName);
        }

        if (Application.isPlaying)
        {
            rootObject.hideFlags = HideFlags.HideInHierarchy;
        }

        connectionLineRoot = rootObject.transform;
        return connectionLineRoot;
    }

    private static void SetLineRendererVisible(LineRenderer lineRenderer, bool visible)
    {
        if (lineRenderer == null)
        {
            return;
        }

        if (lineRenderer.gameObject.activeSelf != visible)
        {
            lineRenderer.gameObject.SetActive(visible);
        }

        lineRenderer.enabled = visible;
    }

    private static void BuildVisualPoleScratch()
    {
        visualPoleScratch.Clear();
        foreach (UtilityPole pole in activePoles)
        {
            if (pole != null && !visualPoleScratch.Contains(pole))
            {
                visualPoleScratch.Add(pole);
            }
        }

        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            UtilityPole pole = entry.Key;
            if (pole != null && pole.gameObject.activeInHierarchy && !visualPoleScratch.Contains(pole))
            {
                visualPoleScratch.Add(pole);
            }
        }
    }

    private static void CleanupPreviewPoleRuntimes()
    {
        if (previewPoleRuntimes.Count <= 0)
        {
            return;
        }

        visualPoleScratch.Clear();
        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            UtilityPole pole = entry.Key;
            if (pole == null || !pole.gameObject.activeInHierarchy)
            {
                visualPoleScratch.Add(pole);
            }
        }

        for (int i = 0; i < visualPoleScratch.Count; i++)
        {
            previewPoleRuntimes.Remove(visualPoleScratch[i]);
            previewPoleConnectionsDirty = true;
        }

        visualPoleScratch.Clear();
    }

    private static void CleanupPreviewConsumerRuntimes()
    {
        if (previewConsumerRuntimes.Count <= 0)
        {
            return;
        }

        installationScratch.Clear();
        foreach (KeyValuePair<InstallationObject, PreviewConsumerRuntime> entry in previewConsumerRuntimes)
        {
            InstallationObject consumer = entry.Key;
            if (consumer == null || !consumer.gameObject.activeInHierarchy)
            {
                installationScratch.Add(consumer);
            }
        }

        for (int i = 0; i < installationScratch.Count; i++)
        {
            previewConsumerRuntimes.Remove(installationScratch[i]);
            connectionLineVisualsDirty = true;
        }

        installationScratch.Clear();
    }

    private void ResetLinePointConnections()
    {
        linePointAConnectionCount = 0;
        linePointBConnectionCount = 0;
    }

    private bool IsLinePointConnectionOccupied(int linePointIndex)
    {
        return GetLinePointConnectionCount(linePointIndex) >= MaxConnectionsPerLinePoint;
    }

    private void SetLinePointConnectionOccupied(int linePointIndex)
    {
        if (linePointIndex == LinePointAIndex)
        {
            linePointAConnectionCount = Mathf.Min(
                linePointAConnectionCount + 1,
                MaxConnectionsPerLinePoint);
        }
        else
        {
            linePointBConnectionCount = Mathf.Min(
                linePointBConnectionCount + 1,
                MaxConnectionsPerLinePoint);
        }
    }

    private int GetLinePointConnectionCount(int linePointIndex)
    {
        return linePointIndex == LinePointAIndex
            ? linePointAConnectionCount
            : linePointBConnectionCount;
    }

    private bool TryGetConnectionLinePoint(int linePointIndex, out Transform linePoint)
    {
        linePoint = linePointIndex == LinePointAIndex ? linePointA : linePointB;
        return linePoint != null;
    }

    private bool TryGetConnectionLinePointIndex(Transform linePoint, out int linePointIndex)
    {
        if (linePoint != null && linePoint == linePointA)
        {
            linePointIndex = LinePointAIndex;
            return true;
        }

        if (linePoint != null && linePoint == linePointB)
        {
            linePointIndex = LinePointBIndex;
            return true;
        }

        linePointIndex = LinePointAIndex;
        return false;
    }

    private static Material GetLineMaterial()
    {
        if (sharedLineMaterial != null)
        {
            return sharedLineMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        }

        if (shader != null)
        {
            sharedLineMaterial = new Material(shader)
            {
                name = "Utility Pole Line Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return sharedLineMaterial;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
        {
            return null;
        }

        if (root.name == targetName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform match = FindDescendantByName(child, targetName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private void RefreshSupplyRangeVisual()
    {
        RefreshSelectedSupplyRangeVisual();
        RefreshSelectedConnectionRangeVisual();
    }

    private static void RefreshSelectedSupplyRangeVisual()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        rangeVisualRequestScratch.Clear();
        rangeVisualPoleScratch.Clear();
        if (ShouldShowInstallOrEditSupplyRangeVisuals())
        {
            AppendSupplyRangeVisualRequests(
                activePoles,
                false,
                rangeVisualRequestScratch,
                rangeVisualPoleScratch);
        }

        AppendSupplyRangeVisualRequests(
            SelectedSupplyRangeVisualInstances,
            true,
            rangeVisualRequestScratch,
            rangeVisualPoleScratch);

        if (rangeVisualRequestScratch.Count <= 0)
        {
            SetSharedSupplyRangeVisualActive(false);
            return;
        }

        WorkableObjectRangeVisual visual = GetOrCreateSharedSupplyRangeVisual();
        if (visual == null)
        {
            return;
        }

        visual.Configure(rangeVisualRequestScratch, SupplyRangeFillColor);
        if (!visual.gameObject.activeSelf)
        {
            visual.gameObject.SetActive(true);
        }
    }

    private static void RefreshSelectedConnectionRangeVisual()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        rangeVisualRequestScratch.Clear();
        rangeVisualPoleScratch.Clear();
        if (ShouldShowInstallOrEditConnectionRangeVisuals())
        {
            AppendConnectionRangeVisualRequests(
                activePoles,
                false,
                rangeVisualRequestScratch,
                rangeVisualPoleScratch);
        }

        AppendConnectionRangeVisualRequests(
            SelectedSupplyRangeVisualInstances,
            true,
            rangeVisualRequestScratch,
            rangeVisualPoleScratch);

        if (rangeVisualRequestScratch.Count <= 0)
        {
            SetSharedConnectionRangeVisualActive(false);
            return;
        }

        WorkableObjectRangeVisual visual = GetOrCreateSharedConnectionRangeVisual();
        if (visual == null)
        {
            return;
        }

        visual.Configure(rangeVisualRequestScratch);
        if (!visual.gameObject.activeSelf)
        {
            visual.gameObject.SetActive(true);
        }
    }

    private static WorkableObjectRangeVisual GetOrCreateSharedSupplyRangeVisual()
    {
        if (sharedSupplyRangeVisual != null)
        {
            return sharedSupplyRangeVisual;
        }

        GameObject visualObject = new GameObject("Utility Pole Supply Range Visuals");
        sharedSupplyRangeVisual = visualObject.AddComponent<WorkableObjectRangeVisual>();
        return sharedSupplyRangeVisual;
    }

    private static WorkableObjectRangeVisual GetOrCreateSharedConnectionRangeVisual()
    {
        if (sharedConnectionRangeVisual != null)
        {
            return sharedConnectionRangeVisual;
        }

        GameObject visualObject = new GameObject("Utility Pole Connection Range Visuals");
        sharedConnectionRangeVisual = visualObject.AddComponent<WorkableObjectRangeVisual>();
        return sharedConnectionRangeVisual;
    }

    private static void SetSharedSupplyRangeVisualActive(bool active)
    {
        if (sharedSupplyRangeVisual != null && sharedSupplyRangeVisual.gameObject.activeSelf != active)
        {
            sharedSupplyRangeVisual.gameObject.SetActive(active);
        }
    }

    private static void SetSharedConnectionRangeVisualActive(bool active)
    {
        if (sharedConnectionRangeVisual != null && sharedConnectionRangeVisual.gameObject.activeSelf != active)
        {
            sharedConnectionRangeVisual.gameObject.SetActive(active);
        }
    }

    private static void AppendSupplyRangeVisualRequests(
        IEnumerable<UtilityPole> sourcePoles,
        bool requireSelectedRequest,
        List<WorkableObjectRangeVisualRequest> requests,
        HashSet<UtilityPole> appendedPoles)
    {
        AppendRangeVisualRequests(
            sourcePoles,
            requireSelectedRequest,
            requests,
            appendedPoles,
            false);
    }

    private static void AppendConnectionRangeVisualRequests(
        IEnumerable<UtilityPole> sourcePoles,
        bool requireSelectedRequest,
        List<WorkableObjectRangeVisualRequest> requests,
        HashSet<UtilityPole> appendedPoles)
    {
        AppendRangeVisualRequests(
            sourcePoles,
            requireSelectedRequest,
            requests,
            appendedPoles,
            true);
    }

    private static void AppendRangeVisualRequests(
        IEnumerable<UtilityPole> sourcePoles,
        bool requireSelectedRequest,
        List<WorkableObjectRangeVisualRequest> requests,
        HashSet<UtilityPole> appendedPoles,
        bool connectionRange)
    {
        if (sourcePoles == null || requests == null || appendedPoles == null)
        {
            return;
        }

        foreach (UtilityPole pole in sourcePoles)
        {
            if (pole == null || !pole.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (requireSelectedRequest && !pole.selectedSupplyRangeVisualRequested)
            {
                continue;
            }

            if (requireSelectedRequest
                && connectionRange
                && !pole.selectedConnectionRangeVisualRequested)
            {
                continue;
            }

            if (!requireSelectedRequest && !IsValidPlacedPole(pole))
            {
                continue;
            }

            if (!requireSelectedRequest
                && (!screenRangePolesInitialized || !screenRangePoles.Contains(pole)))
            {
                continue;
            }

            if (!appendedPoles.Add(pole))
            {
                continue;
            }

            Bounds rangeBounds;
            bool hasBounds = connectionRange
                ? pole.TryGetConnectionRangeBounds(out rangeBounds)
                : pole.TryGetSupplyRangeBounds(out rangeBounds);
            if (!hasBounds)
            {
                continue;
            }

            requests.Add(new WorkableObjectRangeVisualRequest(
                rangeBounds.center,
                rangeBounds.extents.x,
                pole.supplyRangeVisualYOffset + (connectionRange ? 0.01f : 0f)));
        }
    }

    private static bool ShouldShowInstallOrEditSupplyRangeVisuals()
    {
        return installOrEditSupplyRangeVisualsRequested && IsInstallOrEditModeActive();
    }

    private static bool ShouldShowInstallOrEditConnectionRangeVisuals()
    {
        return installOrEditConnectionRangeVisualsRequested && IsInstallOrEditModeActive();
    }

    private static bool IsInstallOrEditModeActive()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            return false;
        }

        return gameManager.InstallationPlacementActive || gameManager.MapEditActive;
    }

    private static void EnsurePoleConnectionsEvaluated()
    {
        if (!poleConnectionsDirty)
        {
            return;
        }

        RebuildPoleConnections();
        poleConnectionsDirty = false;
        connectionLineVisualsDirty = true;
    }

    private static void EnsurePreviewPoleConnectionsEvaluated()
    {
        if (!previewPoleConnectionsDirty)
        {
            return;
        }

        RebuildPreviewPoleConnections();
        previewPoleConnectionsDirty = false;
        connectionLineVisualsDirty = true;
    }

    private static void RebuildPoleConnections()
    {
        poleConnections.Clear();
        connectionPoleScratch.Clear();
        connectionCandidateScratch.Clear();

        foreach (UtilityPole pole in activePoles)
        {
            if (!IsValidPlacedPole(pole))
            {
                continue;
            }

            pole.ResolveLinePointReferences();
            pole.ResetLinePointConnections();
            if (pole.linePointA == null && pole.linePointB == null)
            {
                continue;
            }

            connectionPoleScratch.Add(pole);
        }

        for (int i = 0; i < connectionPoleScratch.Count; i++)
        {
            UtilityPole first = connectionPoleScratch[i];
            for (int j = i + 1; j < connectionPoleScratch.Count; j++)
            {
                UtilityPole second = connectionPoleScratch[j];
                if (first == null
                    || second == null
                    || !ArePolesAutoConnected(first, second)
                    || !TryGetBestLinePointDistanceSqr(first, second, out float bestLinePointDistanceSqr))
                {
                    continue;
                }

                connectionCandidateScratch.Add(new PoleConnectionCandidate(
                    first,
                    second,
                    GetPoleDistanceSqr(first, second),
                    bestLinePointDistanceSqr));
            }
        }

        connectionCandidateScratch.Sort(ComparePoleConnectionCandidates);
        for (int unconnectedEndpointPriority = 2; unconnectedEndpointPriority >= 0; unconnectedEndpointPriority--)
        {
            for (int i = 0; i < connectionCandidateScratch.Count; i++)
            {
                PoleConnectionCandidate candidate = connectionCandidateScratch[i];
                if (ArePolesAlreadyConnected(candidate.FirstPole, candidate.SecondPole)
                    || CountUnconnectedCandidateEndpoints(candidate) != unconnectedEndpointPriority)
                {
                    continue;
                }

                TryAddPoleConnection(candidate);
            }
        }
    }

    private static void RebuildPreviewPoleConnections()
    {
        previewPoleConnections.Clear();
        CleanupPreviewPoleRuntimes();
        if (previewPoleRuntimes.Count <= 0)
        {
            return;
        }

        if (HasTopologyReplacementPreview())
        {
            RebuildFullPreviewPoleConnections();
            return;
        }

        connectionPoleScratch.Clear();
        connectionCandidateScratch.Clear();

        foreach (UtilityPole pole in activePoles)
        {
            if (!IsValidPlacedPole(pole))
            {
                continue;
            }

            pole.ResolveLinePointReferences();
            pole.ResetLinePointConnections();
            if (pole.linePointA == null && pole.linePointB == null)
            {
                continue;
            }

            connectionPoleScratch.Add(pole);
        }

        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            UtilityPole pole = entry.Key;
            if (!IsValidPreviewPole(pole))
            {
                continue;
            }

            pole.ResolveLinePointReferences();
            pole.ResetLinePointConnections();
            if (pole.linePointA == null && pole.linePointB == null)
            {
                continue;
            }

            if (!connectionPoleScratch.Contains(pole))
            {
                connectionPoleScratch.Add(pole);
            }
        }

        for (int i = 0; i < poleConnections.Count; i++)
        {
            MarkConnectionLinePointsOccupied(poleConnections[i]);
        }

        for (int i = 0; i < connectionPoleScratch.Count; i++)
        {
            UtilityPole first = connectionPoleScratch[i];
            for (int j = i + 1; j < connectionPoleScratch.Count; j++)
            {
                UtilityPole second = connectionPoleScratch[j];
                bool firstIsPreview = IsPreviewPole(first);
                bool secondIsPreview = IsPreviewPole(second);
                if ((!firstIsPreview && !secondIsPreview)
                    || first == null
                    || second == null
                    || !ArePolesAutoConnected(first, second)
                    || !TryGetBestLinePointDistanceSqr(first, second, out float bestLinePointDistanceSqr))
                {
                    continue;
                }

                connectionCandidateScratch.Add(new PoleConnectionCandidate(
                    first,
                    second,
                    GetPoleDistanceSqr(first, second),
                    bestLinePointDistanceSqr));
            }
        }

        connectionCandidateScratch.Sort(ComparePoleConnectionCandidates);
        for (int unconnectedEndpointPriority = 2; unconnectedEndpointPriority >= 0; unconnectedEndpointPriority--)
        {
            for (int i = 0; i < connectionCandidateScratch.Count; i++)
            {
                PoleConnectionCandidate candidate = connectionCandidateScratch[i];
                if (ArePolesAlreadyConnected(candidate.FirstPole, candidate.SecondPole, poleConnections, previewPoleConnections)
                    || CountUnconnectedCandidateEndpoints(candidate) != unconnectedEndpointPriority)
                {
                    continue;
                }

                TryAddPoleConnection(candidate, previewPoleConnections);
            }
        }
    }

    private static void RebuildFullPreviewPoleConnections()
    {
        connectionPoleScratch.Clear();
        connectionCandidateScratch.Clear();

        foreach (UtilityPole pole in activePoles)
        {
            if (!IsValidPlacedPole(pole))
            {
                continue;
            }

            pole.ResolveLinePointReferences();
            pole.ResetLinePointConnections();
            if (pole.linePointA == null && pole.linePointB == null)
            {
                continue;
            }

            connectionPoleScratch.Add(pole);
        }

        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            UtilityPole pole = entry.Key;
            if (!IsValidPreviewPole(pole))
            {
                continue;
            }

            pole.ResolveLinePointReferences();
            pole.ResetLinePointConnections();
            if (pole.linePointA == null && pole.linePointB == null)
            {
                continue;
            }

            if (!connectionPoleScratch.Contains(pole))
            {
                connectionPoleScratch.Add(pole);
            }
        }

        for (int i = 0; i < connectionPoleScratch.Count; i++)
        {
            UtilityPole first = connectionPoleScratch[i];
            for (int j = i + 1; j < connectionPoleScratch.Count; j++)
            {
                UtilityPole second = connectionPoleScratch[j];
                if (first == null
                    || second == null
                    || !ArePolesAutoConnected(first, second)
                    || !TryGetBestLinePointDistanceSqr(first, second, out float bestLinePointDistanceSqr))
                {
                    continue;
                }

                connectionCandidateScratch.Add(new PoleConnectionCandidate(
                    first,
                    second,
                    GetPoleDistanceSqr(first, second),
                    bestLinePointDistanceSqr));
            }
        }

        connectionCandidateScratch.Sort(ComparePoleConnectionCandidates);
        for (int unconnectedEndpointPriority = 2; unconnectedEndpointPriority >= 0; unconnectedEndpointPriority--)
        {
            for (int i = 0; i < connectionCandidateScratch.Count; i++)
            {
                PoleConnectionCandidate candidate = connectionCandidateScratch[i];
                if (ArePolesAlreadyConnected(candidate.FirstPole, candidate.SecondPole, previewPoleConnections)
                    || CountUnconnectedCandidateEndpoints(candidate) != unconnectedEndpointPriority)
                {
                    continue;
                }

                TryAddPoleConnection(candidate, previewPoleConnections);
            }
        }
    }

    private static bool HasTopologyReplacementPreview()
    {
        foreach (KeyValuePair<UtilityPole, PreviewPoleRuntime> entry in previewPoleRuntimes)
        {
            UtilityPole pole = entry.Key;
            if (pole != null
                && pole.gameObject.activeInHierarchy
                && entry.Value.TopologyReplacement)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAddPoleConnection(PoleConnectionCandidate candidate)
    {
        return TryAddPoleConnection(candidate, poleConnections);
    }

    private static bool TryAddPoleConnection(PoleConnectionCandidate candidate, List<PoleConnection> targetConnections)
    {
        if (candidate.FirstPole == null
            || candidate.SecondPole == null
            || candidate.FirstPole == candidate.SecondPole)
        {
            return false;
        }

        if (!TryResolveClosestAvailableLinePointPair(
                candidate.FirstPole,
                candidate.SecondPole,
                out Transform firstPoint,
                out Transform secondPoint,
                out int firstLinePointIndex,
                out int secondLinePointIndex))
        {
            return false;
        }

        candidate.FirstPole.SetLinePointConnectionOccupied(firstLinePointIndex);
        candidate.SecondPole.SetLinePointConnectionOccupied(secondLinePointIndex);
        targetConnections.Add(new PoleConnection(
            candidate.FirstPole,
            candidate.SecondPole,
            firstPoint,
            secondPoint));
        return true;
    }

    private static int CountUnconnectedCandidateEndpoints(PoleConnectionCandidate candidate)
    {
        int count = 0;
        if (candidate.FirstPole != null && candidate.FirstPole.ExternalConnectionCount <= 0)
        {
            count++;
        }

        if (candidate.SecondPole != null && candidate.SecondPole.ExternalConnectionCount <= 0)
        {
            count++;
        }

        return count;
    }

    private static bool ArePolesAlreadyConnected(UtilityPole first, UtilityPole second)
    {
        return ArePolesAlreadyConnected(first, second, poleConnections);
    }

    private static bool ArePolesAlreadyConnected(
        UtilityPole first,
        UtilityPole second,
        List<PoleConnection> connections)
    {
        return ArePolesAlreadyConnected(first, second, connections, null);
    }

    private static bool ArePolesAlreadyConnected(
        UtilityPole first,
        UtilityPole second,
        List<PoleConnection> primaryConnections,
        List<PoleConnection> secondaryConnections)
    {
        if (first == null || second == null)
        {
            return false;
        }

        if (first == second)
        {
            return true;
        }

        visitedPoles.Clear();
        poleQueue.Clear();
        visitedPoles.Add(first);
        poleQueue.Enqueue(first);

        while (poleQueue.Count > 0)
        {
            UtilityPole pole = poleQueue.Dequeue();
            if (TryVisitConnectedPoles(pole, primaryConnections, second)
                || TryVisitConnectedPoles(pole, secondaryConnections, second))
            {
                visitedPoles.Clear();
                poleQueue.Clear();
                return true;
            }
        }

        visitedPoles.Clear();
        poleQueue.Clear();
        return false;
    }

    private static bool TryVisitConnectedPoles(
        UtilityPole pole,
        List<PoleConnection> connections,
        UtilityPole targetPole)
    {
        for (int i = 0; connections != null && i < connections.Count; i++)
        {
            PoleConnection connection = connections[i];
            UtilityPole connectedPole = GetConnectedPole(connection, pole);
            if (connectedPole == null)
            {
                continue;
            }

            if (connectedPole == targetPole)
            {
                return true;
            }

            if (visitedPoles.Add(connectedPole))
            {
                poleQueue.Enqueue(connectedPole);
            }
        }

        return false;
    }

    private static void MarkConnectionLinePointsOccupied(PoleConnection connection)
    {
        if (connection.FirstPole != null
            && connection.FirstPole.TryGetConnectionLinePointIndex(connection.FirstPoint, out int firstLinePointIndex))
        {
            connection.FirstPole.SetLinePointConnectionOccupied(firstLinePointIndex);
        }

        if (connection.SecondPole != null
            && connection.SecondPole.TryGetConnectionLinePointIndex(connection.SecondPoint, out int secondLinePointIndex))
        {
            connection.SecondPole.SetLinePointConnectionOccupied(secondLinePointIndex);
        }
    }

    private static float GetPoleDistanceSqr(UtilityPole first, UtilityPole second)
    {
        Vector3 firstPosition = GetPoleRangeCenter(first);
        Vector3 secondPosition = GetPoleRangeCenter(second);
        return (firstPosition - secondPosition).sqrMagnitude;
    }

    private static Vector3 GetPoleRangeCenter(UtilityPole pole)
    {
        if (pole == null)
        {
            return Vector3.zero;
        }

        return previewPoleRuntimes.TryGetValue(pole, out PreviewPoleRuntime previewRuntime)
            ? new Vector3(previewRuntime.AnchorCoordinate.x, pole.transform.position.y, previewRuntime.AnchorCoordinate.y)
            : pole.GetSupplyRangeCenter();
    }

    private static bool TryGetBestLinePointDistanceSqr(
        UtilityPole first,
        UtilityPole second,
        out float bestDistanceSqr)
    {
        bestDistanceSqr = float.MaxValue;
        bool found = false;
        for (int firstPointIndex = LinePointAIndex; firstPointIndex <= LinePointBIndex; firstPointIndex++)
        {
            if (first == null || !first.TryGetConnectionLinePoint(firstPointIndex, out Transform firstPoint))
            {
                continue;
            }

            for (int secondPointIndex = LinePointAIndex; secondPointIndex <= LinePointBIndex; secondPointIndex++)
            {
                if (second == null || !second.TryGetConnectionLinePoint(secondPointIndex, out Transform secondPoint))
                {
                    continue;
                }

                float distanceSqr = (
                    ResolveLinePointWorldPosition(firstPoint)
                    - ResolveLinePointWorldPosition(secondPoint)).sqrMagnitude;
                if (distanceSqr + DistanceTieEpsilon < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool TryResolveClosestAvailableLinePointPair(
        UtilityPole first,
        UtilityPole second,
        out Transform firstPoint,
        out Transform secondPoint,
        out int firstLinePointIndex,
        out int secondLinePointIndex)
    {
        firstPoint = null;
        secondPoint = null;
        firstLinePointIndex = LinePointAIndex;
        secondLinePointIndex = LinePointAIndex;

        float bestDistanceSqr = float.MaxValue;
        for (int firstPointIndex = LinePointAIndex; firstPointIndex <= LinePointBIndex; firstPointIndex++)
        {
            if (first == null
                || first.IsLinePointConnectionOccupied(firstPointIndex)
                || !first.TryGetConnectionLinePoint(firstPointIndex, out Transform candidateFirstPoint))
            {
                continue;
            }

            for (int secondPointIndex = LinePointAIndex; secondPointIndex <= LinePointBIndex; secondPointIndex++)
            {
                if (second == null
                    || second.IsLinePointConnectionOccupied(secondPointIndex)
                    || !second.TryGetConnectionLinePoint(secondPointIndex, out Transform candidateSecondPoint))
                {
                    continue;
                }

                float distanceSqr = (
                    ResolveLinePointWorldPosition(candidateFirstPoint)
                    - ResolveLinePointWorldPosition(candidateSecondPoint)).sqrMagnitude;
                if (distanceSqr + DistanceTieEpsilon >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                firstPoint = candidateFirstPoint;
                secondPoint = candidateSecondPoint;
                firstLinePointIndex = firstPointIndex;
                secondLinePointIndex = secondPointIndex;
            }
        }

        return firstPoint != null && secondPoint != null;
    }

    private static int ComparePoleConnectionCandidates(
        PoleConnectionCandidate left,
        PoleConnectionCandidate right)
    {
        int result = CompareDistance(left.PoleDistanceSqr, right.PoleDistanceSqr);
        if (result != 0)
        {
            return result;
        }

        result = CompareDistance(left.BestLinePointDistanceSqr, right.BestLinePointDistanceSqr);
        if (result != 0)
        {
            return result;
        }

        result = left.FirstPole.GetInstanceID().CompareTo(right.FirstPole.GetInstanceID());
        return result != 0
            ? result
            : left.SecondPole.GetInstanceID().CompareTo(right.SecondPole.GetInstanceID());
    }

    private static int CompareDistance(float left, float right)
    {
        float difference = left - right;
        if (Mathf.Abs(difference) <= DistanceTieEpsilon)
        {
            return 0;
        }

        return difference < 0f ? -1 : 1;
    }

    private static UtilityPole GetConnectedPole(PoleConnection connection, UtilityPole pole)
    {
        if (connection.FirstPole == pole)
        {
            return connection.SecondPole;
        }

        return connection.SecondPole == pole ? connection.FirstPole : null;
    }

    private static void EnsureNetworksEvaluated(bool refreshLineVisuals = true)
    {
        if (networksDirty)
        {
            RebuildNetworks();
            networksDirty = false;
            if (refreshLineVisuals)
            {
                RefreshConnectionLineRenderersIfDirty();
            }
        }

        RefreshNetworkRuntimeValues();
    }

    private static void RebuildNetworks()
    {
        EnsurePoleConnectionsEvaluated();
        ClearPoleSupplyCoordinateCache();
        networks.Clear();
        suppliedConsumerNetworks.Clear();
        activePoleScratch.Clear();
        visitedPoles.Clear();
        poleQueue.Clear();
        installationScratch.Clear();

        foreach (UtilityPole pole in activePoles)
        {
            if (IsValidPlacedPole(pole))
            {
                activePoleScratch.Add(pole);
            }
        }

        for (int i = 0; i < activePoleScratch.Count; i++)
        {
            UtilityPole startPole = activePoleScratch[i];
            if (startPole == null || visitedPoles.Contains(startPole))
            {
                continue;
            }

            ElectricNetwork network = new ElectricNetwork();
            visitedPoles.Add(startPole);
            poleQueue.Enqueue(startPole);

            while (poleQueue.Count > 0)
            {
                UtilityPole pole = poleQueue.Dequeue();
                network.Poles.Add(pole);

                for (int connectionIndex = 0; connectionIndex < poleConnections.Count; connectionIndex++)
                {
                    UtilityPole candidate = GetConnectedPole(poleConnections[connectionIndex], pole);
                    if (candidate == null || visitedPoles.Contains(candidate))
                    {
                        continue;
                    }

                    visitedPoles.Add(candidate);
                    poleQueue.Enqueue(candidate);
                }
            }

            networks.Add(network);
        }

        RebuildPoleSupplyCoordinateCache();
        RebuildNetworkSupplyAreasFromCache();

        RefreshNetworkRuntimeValues(true);
        InputOutputModule.WakeElectricRuntimeModules();
    }

    private static bool IsValidPlacedPole(UtilityPole pole)
    {
        return pole != null
               && pole.isActiveAndEnabled
               && pole.gameObject.activeInHierarchy
               && pole.TryGetPlacementRuntime(out _, out _);
    }

    private static bool IsValidPreviewPole(UtilityPole pole)
    {
        return pole != null
               && pole.gameObject.activeInHierarchy
               && previewPoleRuntimes.ContainsKey(pole);
    }

    private static bool IsPreviewPole(UtilityPole pole)
    {
        return pole != null && previewPoleRuntimes.ContainsKey(pole);
    }

    private static bool TryGetPoleAnchorCoordinate(UtilityPole pole, out Vector2Int anchorCoordinate)
    {
        anchorCoordinate = Vector2Int.zero;
        if (pole == null)
        {
            return false;
        }

        if (pole.TryGetPlacementRuntime(out anchorCoordinate, out _))
        {
            return true;
        }

        if (previewPoleRuntimes.TryGetValue(pole, out PreviewPoleRuntime previewRuntime))
        {
            anchorCoordinate = previewRuntime.AnchorCoordinate;
            return true;
        }

        return false;
    }

    private static bool ArePolesAutoConnected(UtilityPole first, UtilityPole second)
    {
        if (first == null
            || second == null
            || !TryGetPoleAnchorCoordinate(first, out Vector2Int firstAnchor)
            || !TryGetPoleAnchorCoordinate(second, out Vector2Int secondAnchor))
        {
            return false;
        }

        int reach = Mathf.Max(first.ConnectionRadiusCells, second.ConnectionRadiusCells);
        if (reach <= 0)
        {
            return firstAnchor == secondAnchor;
        }

        return ChebyshevDistance(firstAnchor, secondAnchor) <= reach;
    }

    private static void ClearPoleSupplyCoordinateCache()
    {
        foreach (KeyValuePair<Vector2Int, List<UtilityPole>> entry in supplyPolesByCoordinate)
        {
            List<UtilityPole> poles = entry.Value;
            if (poles == null)
            {
                continue;
            }

            poles.Clear();
            supplyPoleListPool.Push(poles);
        }

        supplyPolesByCoordinate.Clear();
        electricNetworkByPole.Clear();
    }

    private static void RebuildPoleSupplyCoordinateCache()
    {
        for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
        {
            ElectricNetwork network = networks[networkIndex];
            if (network == null)
            {
                continue;
            }

            for (int poleIndex = 0; poleIndex < network.Poles.Count; poleIndex++)
            {
                UtilityPole pole = network.Poles[poleIndex];
                if (pole == null
                    || !pole.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
                {
                    continue;
                }

                electricNetworkByPole[pole] = network;
                int radius = pole.SupplyRadiusCells;
                for (int y = anchorCoordinate.y - radius; y <= anchorCoordinate.y + radius; y++)
                {
                    for (int x = anchorCoordinate.x - radius; x <= anchorCoordinate.x + radius; x++)
                    {
                        Vector2Int coordinate = new Vector2Int(x, y);
                        if (!supplyPolesByCoordinate.TryGetValue(coordinate, out List<UtilityPole> poles))
                        {
                            poles = supplyPoleListPool.Count > 0
                                ? supplyPoleListPool.Pop()
                                : new List<UtilityPole>(2);
                            supplyPolesByCoordinate.Add(coordinate, poles);
                        }

                        poles.Add(pole);
                    }
                }
            }
        }
    }

    private static void RebuildNetworkSupplyAreasFromCache()
    {
        for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
        {
            networks[networkIndex]?.ClearTopologyRuntime();
        }

        foreach (KeyValuePair<Vector2Int, List<UtilityPole>> entry in supplyPolesByCoordinate)
        {
            installationScratch.Clear();
            InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                entry.Key,
                installationScratch);
            for (int installationIndex = 0;
                 installationIndex < installationScratch.Count;
                 installationIndex++)
            {
                InstallationObject installationObject = installationScratch[installationIndex];
                if (!IsElectricNetworkParticipant(installationObject))
                {
                    continue;
                }

                List<UtilityPole> poles = entry.Value;
                for (int poleIndex = 0; poleIndex < poles.Count; poleIndex++)
                {
                    if (electricNetworkByPole.TryGetValue(
                            poles[poleIndex],
                            out ElectricNetwork network))
                    {
                        network.SuppliedInstallations.Add(installationObject);
                    }
                }
            }
        }

        installationScratch.Clear();
        for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
        {
            RefreshNetworkTopologyRuntimeValues(networks[networkIndex]);
        }
    }

    private static void RefreshNetworkTopologyRuntimeValues(ElectricNetwork network)
    {
        if (network == null)
        {
            return;
        }

        network.PowerSources.Clear();
        network.StaticRequiredWatts = 0f;

        foreach (InstallationObject installationObject in network.SuppliedInstallations)
        {
            if (installationObject == null || !installationObject.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (installationObject is SteamGenerator steamGenerator
                && steamGenerator.TryGetObjectInfoOutputRate(out _, out float configuredGeneratorWatts)
                && configuredGeneratorWatts > EnergyEpsilon)
            {
                network.PowerSources.Add(steamGenerator);
            }

            // Operation-dependent consumers are added during the per-frame runtime
            // refresh instead of being fixed into the topology total.
            if (!HasRuntimeElectricPowerDemand(installationObject)
                && TryGetElectricPowerRequirement(installationObject, out float requiredWatts))
            {
                network.StaticRequiredWatts += requiredWatts;
            }
        }
    }

    private static void RefreshNetworkRuntimeValues(bool force = false)
    {
        int frame = Time.frameCount;
        if (!force && networkRuntimeEvaluatedFrame == frame)
        {
            return;
        }

        networkRuntimeEvaluatedFrame = frame;
        for (int i = 0; i < networks.Count; i++)
        {
            RefreshNetworkRuntimeValues(networks[i]);
        }

        // 발전기 가동 상태와 수요량은 매 프레임 달라질 수 있다. 소비자가 여러 전력망의
        // 공급 범위에 걸친 경우에도 현재 공급률이 가장 높은 망을 사용하도록 런타임 값과
        // 소비자 매핑을 같은 시점에 갱신한다.
        RefreshSuppliedConsumerNetworks();
    }

    private static void RefreshNetworkRuntimeValues(ElectricNetwork network)
    {
        if (network == null)
        {
            return;
        }

        network.ClearPowerRuntime();

        network.RequiredWatts = network.StaticRequiredWatts;
        foreach (InstallationObject installationObject in network.SuppliedInstallations)
        {
            if (HasRuntimeElectricPowerDemand(installationObject)
                && TryGetElectricPowerDemand(installationObject, out float demandWatts))
            {
                network.RequiredWatts += demandWatts;
            }
        }

        for (int i = 0; i < network.PowerSources.Count; i++)
        {
            SteamGenerator steamGenerator = network.PowerSources[i];
            if (steamGenerator == null || !steamGenerator.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (steamGenerator.TryGetObjectInfoOutputRate(out _, out float configuredGeneratorWatts)
                && configuredGeneratorWatts > EnergyEpsilon)
            {
                network.HasPowerSource = true;
            }

            if (steamGenerator.TryGetAvailableElectricOutputRate(out float generatorWatts))
            {
                network.ProductionWatts += generatorWatts;
            }
        }

        if (!network.HasPowerSource)
        {
            network.SupplyRatio = 0f;
            return;
        }
        network.SupplyRatio = network.RequiredWatts > EnergyEpsilon
            ? Mathf.Clamp01(network.ProductionWatts / network.RequiredWatts)
            : (network.ProductionWatts > EnergyEpsilon ? 1f : 0f);
    }

    private static bool IsElectricNetworkParticipant(InstallationObject installationObject)
    {
        if (installationObject == null)
        {
            return false;
        }

        if (TryGetElectricPowerRequirement(installationObject, out _))
        {
            return true;
        }

        return installationObject is SteamGenerator steamGenerator
               && steamGenerator.TryGetObjectInfoOutputRate(out _, out float configuredGeneratorWatts)
               && configuredGeneratorWatts > EnergyEpsilon;
    }

    private static void RefreshSuppliedConsumerNetworks()
    {
        suppliedConsumerNetworks.Clear();
        for (int i = 0; i < networks.Count; i++)
        {
            ElectricNetwork network = networks[i];
            if (network == null || !network.HasPowerSource)
            {
                continue;
            }

            foreach (InstallationObject installationObject in network.SuppliedInstallations)
            {
                if (installationObject == null || !TryGetElectricPowerRequirement(installationObject, out _))
                {
                    continue;
                }

                if (!suppliedConsumerNetworks.TryGetValue(installationObject, out ElectricNetwork currentNetwork)
                    || ResolveNetworkScore(network) > ResolveNetworkScore(currentNetwork))
                {
                    suppliedConsumerNetworks[installationObject] = network;
                }
            }
        }
    }

    private static ElectricNetwork ResolveBestNetworkForConsumer(InstallationObject consumer)
    {
        return consumer != null
               && suppliedConsumerNetworks.TryGetValue(consumer, out ElectricNetwork network)
               && network.HasPowerSource
            ? network
            : null;
    }

    private static bool TryGetElectricPowerRequirement(InstallationObject consumer, out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        if (consumer == null)
        {
            return false;
        }

        if (consumer is InputOutputModule module)
        {
            return module.TryGetElectricPowerRequirement(out wattsPerSecond);
        }

        if (consumer is RobotArm robotArm)
        {
            return robotArm.TryGetElectricPowerRequirement(out wattsPerSecond);
        }

        if (consumer is LoggingMachine loggingMachine)
        {
            return loggingMachine.TryGetElectricPowerRequirement(out wattsPerSecond);
        }

        ItemDefinition definition = ResolveElectricConsumerDefinition(consumer);
        float electricUseWatts = ItemDefinition.ResolveElectricUseWatts(definition);
        if (electricUseWatts <= EnergyEpsilon)
        {
            return false;
        }

        wattsPerSecond = electricUseWatts;
        return wattsPerSecond > EnergyEpsilon;
    }

    private static bool TryGetElectricPowerDemand(InstallationObject consumer, out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        if (consumer == null)
        {
            return false;
        }

        if (consumer is InputOutputModule module)
        {
            return module.TryGetElectricPowerDemand(out wattsPerSecond);
        }

        if (consumer is RobotArm robotArm)
        {
            return robotArm.TryGetElectricPowerDemand(out wattsPerSecond);
        }

        if (consumer is LoggingMachine loggingMachine)
        {
            return loggingMachine.TryGetElectricPowerDemand(out wattsPerSecond);
        }

        if (consumer is LightObject lightObject)
        {
            return lightObject.TryGetElectricPowerDemand(out wattsPerSecond);
        }

        return false;
    }

    private static bool HasRuntimeElectricPowerDemand(InstallationObject consumer)
    {
        return consumer is InputOutputModule
               || consumer is RobotArm
               || consumer is LoggingMachine
               || consumer is LightObject;
    }

    private static ItemDefinition ResolveElectricConsumerDefinition(InstallationObject consumer)
    {
        if (consumer == null)
        {
            return null;
        }

        if (consumer.BoundItemDefinition != null)
        {
            return consumer.BoundItemDefinition;
        }

        return InputOutputModule.ResolveItemDefinition(consumer.ResolveItemId());
    }

    private static float ResolveNetworkScore(ElectricNetwork network)
    {
        if (network == null || !network.HasPowerSource || network.ProductionWatts <= EnergyEpsilon)
        {
            return 0f;
        }

        return network.SupplyRatio + network.ProductionWatts * 0.000001f;
    }

    private static int ChebyshevDistance(Vector2Int first, Vector2Int second)
    {
        return Mathf.Max(Mathf.Abs(first.x - second.x), Mathf.Abs(first.y - second.y));
    }

    private readonly struct PreviewPoleRuntime
    {
        public PreviewPoleRuntime(Vector2Int anchorCoordinate, int quarterTurns, bool topologyReplacement)
        {
            AnchorCoordinate = anchorCoordinate;
            QuarterTurns = quarterTurns;
            TopologyReplacement = topologyReplacement;
        }

        public Vector2Int AnchorCoordinate { get; }
        public int QuarterTurns { get; }
        public bool TopologyReplacement { get; }
    }

    private readonly struct PreviewConsumerRuntime
    {
        public PreviewConsumerRuntime(Vector2Int anchorCoordinate, int quarterTurns)
        {
            AnchorCoordinate = anchorCoordinate;
            QuarterTurns = quarterTurns;
        }

        public Vector2Int AnchorCoordinate { get; }
        public int QuarterTurns { get; }
    }

    private readonly struct PoleConnectionCandidate
    {
        public PoleConnectionCandidate(
            UtilityPole firstPole,
            UtilityPole secondPole,
            float poleDistanceSqr,
            float bestLinePointDistanceSqr)
        {
            if (firstPole != null
                && secondPole != null
                && firstPole.GetInstanceID() > secondPole.GetInstanceID())
            {
                FirstPole = secondPole;
                SecondPole = firstPole;
            }
            else
            {
                FirstPole = firstPole;
                SecondPole = secondPole;
            }

            PoleDistanceSqr = poleDistanceSqr;
            BestLinePointDistanceSqr = bestLinePointDistanceSqr;
        }

        public UtilityPole FirstPole { get; }
        public UtilityPole SecondPole { get; }
        public float PoleDistanceSqr { get; }
        public float BestLinePointDistanceSqr { get; }
    }

    private readonly struct PoleConnection
    {
        public PoleConnection(
            UtilityPole firstPole,
            UtilityPole secondPole,
            Transform firstPoint,
            Transform secondPoint)
        {
            FirstPole = firstPole;
            SecondPole = secondPole;
            FirstPoint = firstPoint;
            SecondPoint = secondPoint;
        }

        public UtilityPole FirstPole { get; }
        public UtilityPole SecondPole { get; }
        public Transform FirstPoint { get; }
        public Transform SecondPoint { get; }
    }

    private sealed class ElectricNetwork
    {
        public readonly List<UtilityPole> Poles = new List<UtilityPole>();
        public readonly HashSet<InstallationObject> SuppliedInstallations = new HashSet<InstallationObject>();
        public readonly List<SteamGenerator> PowerSources = new List<SteamGenerator>();
        public float StaticRequiredWatts;
        public float ProductionWatts;
        public float RequiredWatts;
        public float SupplyRatio;
        public bool HasPowerSource;

        public void ClearTopologyRuntime()
        {
            SuppliedInstallations.Clear();
            PowerSources.Clear();
            StaticRequiredWatts = 0f;
            ClearPowerRuntime();
        }

        public void ClearPowerRuntime()
        {
            ProductionWatts = 0f;
            RequiredWatts = 0f;
            SupplyRatio = 0f;
            HasPowerSource = false;
        }
    }
}
