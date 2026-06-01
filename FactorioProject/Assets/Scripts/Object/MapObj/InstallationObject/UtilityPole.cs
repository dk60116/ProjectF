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
    private static readonly Dictionary<UtilityPole, PreviewPoleRuntime> previewPoleRuntimes =
        new Dictionary<UtilityPole, PreviewPoleRuntime>();
    private static readonly Dictionary<InstallationObject, ElectricNetwork> suppliedConsumerNetworks =
        new Dictionary<InstallationObject, ElectricNetwork>();

    private static int networkRuntimeEvaluatedFrame = -1;
    private static bool networksDirty = true;
    private static bool poleConnectionsDirty = true;
    private static bool previewPoleConnectionsDirty = true;
    private static bool connectionLineVisualsDirty = true;
    private static WorkableObjectRangeVisual sharedSupplyRangeVisual;
    private static WorkableObjectRangeVisual sharedConnectionRangeVisual;
    private static bool installOrEditUtilityPoleSelectionRangeVisualsRequested;
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
    private readonly List<LineRenderer> connectionLineRenderers = new List<LineRenderer>(2);
    private int usedConnectionLineRendererCount;
    private bool linePointAConnectionOccupied;
    private bool linePointBConnectionOccupied;

    private int ExternalConnectionCount
    {
        get
        {
            int count = linePointAConnectionOccupied ? 1 : 0;
            return linePointBConnectionOccupied ? count + 1 : count;
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
            if (requested)
            {
                RefreshSupplyRangeVisual();
            }

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
        previewPoleRuntimes[pole] = new PreviewPoleRuntime(
            anchorCoordinate,
            ((quarterTurns % 4) + 4) % 4,
            topologyReplacement);
        MarkPreviewPoleConnectionsDirty();
        RefreshConnectionLineRenderersIfDirty();
    }

    public static void UnregisterBlueprintPreview(UtilityPole pole)
    {
        if (pole == null || !previewPoleRuntimes.Remove(pole))
        {
            return;
        }

        pole.HideConnectionLineRenderers();
        MarkPreviewPoleConnectionsDirty();
        RefreshConnectionLineRenderersIfDirty();
    }

    public static void ClearBlueprintPreviews()
    {
        if (previewPoleRuntimes.Count <= 0)
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
        MarkPreviewPoleConnectionsDirty();
        RefreshConnectionLineRenderersIfDirty();
    }

    public static void SetInstallOrEditUtilityPoleSelectionRangeVisualsRequested(bool requested)
    {
        if (installOrEditUtilityPoleSelectionRangeVisualsRequested == requested)
        {
            return;
        }

        installOrEditUtilityPoleSelectionRangeVisualsRequested = requested;
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

        EnsureNetworksEvaluated();
        ElectricNetwork network = ResolveBestNetworkForConsumer(consumer);
        return network != null && network.HasPowerSource && network.ProductionWatts > EnergyEpsilon;
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

        EnsureNetworksEvaluated();
        ElectricNetwork network = ResolveBestNetworkForConsumer(consumer);
        if (network == null || !network.HasPowerSource || network.ProductionWatts <= EnergyEpsilon)
        {
            return false;
        }

        float requestedWatts = deltaTime > EnergyEpsilon ? requestedEnergy / deltaTime : 0f;
        if (requestedWatts <= EnergyEpsilon && TryGetElectricPowerRequirement(consumer, out float configuredWatts))
        {
            requestedWatts = configuredWatts;
        }

        float effectiveDemandWatts = network.DemandWatts;
        if (TryGetElectricPowerDemand(consumer, out float trackedDemandWatts))
        {
            effectiveDemandWatts = Mathf.Max(
                effectiveDemandWatts,
                trackedDemandWatts,
                requestedWatts);
        }
        else
        {
            effectiveDemandWatts += Mathf.Max(0f, requestedWatts);
        }

        float supplyRatio = effectiveDemandWatts > EnergyEpsilon
            ? Mathf.Clamp01(network.ProductionWatts / effectiveDemandWatts)
            : 1f;
        consumedEnergy = requestedEnergy * supplyRatio;
        return consumedEnergy > EnergyEpsilon;
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

        return true;
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

        MarkElectricNetworkDirty();
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

        MarkElectricNetworkDirty();
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

    private static void RefreshConnectionLineRenderers()
    {
        CleanupPreviewPoleRuntimes();
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

        foreach (UtilityPole pole in visualPoleScratch)
        {
            if (pole != null)
            {
                pole.CompleteConnectionLineVisualRefresh();
            }
        }

        connectionLineVisualsDirty = false;
        visualPoleScratch.Clear();
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
        linePointAConnectionOccupied = false;
        linePointBConnectionOccupied = false;
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

    private void ResetLinePointConnections()
    {
        linePointAConnectionOccupied = false;
        linePointBConnectionOccupied = false;
    }

    private bool IsLinePointConnectionOccupied(int linePointIndex)
    {
        return linePointIndex == LinePointAIndex
            ? linePointAConnectionOccupied
            : linePointBConnectionOccupied;
    }

    private void SetLinePointConnectionOccupied(int linePointIndex)
    {
        if (linePointIndex == LinePointAIndex)
        {
            linePointAConnectionOccupied = true;
        }
        else
        {
            linePointBConnectionOccupied = true;
        }
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

        List<WorkableObjectRangeVisualRequest> requests = new List<WorkableObjectRangeVisualRequest>();
        HashSet<UtilityPole> appendedPoles = new HashSet<UtilityPole>();
        if (ShouldShowInstallOrEditUtilityPoleRangeVisuals())
        {
            AppendSupplyRangeVisualRequests(activePoles, false, requests, appendedPoles);
        }

        AppendSupplyRangeVisualRequests(SelectedSupplyRangeVisualInstances, true, requests, appendedPoles);

        if (requests.Count <= 0)
        {
            SetSharedSupplyRangeVisualActive(false);
            return;
        }

        WorkableObjectRangeVisual visual = GetOrCreateSharedSupplyRangeVisual();
        if (visual == null)
        {
            return;
        }

        visual.Configure(requests, SupplyRangeFillColor);
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

        List<WorkableObjectRangeVisualRequest> requests = new List<WorkableObjectRangeVisualRequest>();
        HashSet<UtilityPole> appendedPoles = new HashSet<UtilityPole>();
        if (ShouldShowInstallOrEditUtilityPoleRangeVisuals())
        {
            AppendConnectionRangeVisualRequests(activePoles, false, requests, appendedPoles);
        }

        AppendConnectionRangeVisualRequests(SelectedSupplyRangeVisualInstances, true, requests, appendedPoles);

        if (requests.Count <= 0)
        {
            SetSharedConnectionRangeVisualActive(false);
            return;
        }

        WorkableObjectRangeVisual visual = GetOrCreateSharedConnectionRangeVisual();
        if (visual == null)
        {
            return;
        }

        visual.Configure(requests);
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

    private static bool ShouldShowInstallOrEditUtilityPoleRangeVisuals()
    {
        if (!installOrEditUtilityPoleSelectionRangeVisualsRequested)
        {
            return false;
        }

        GameManager gameManager = GameManager.Instance;
        return gameManager != null
               && (gameManager.InstallationPlacementActive || gameManager.MapEditActive);
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
                if (ArePolesAlreadyConnected(candidate.FirstPole, candidate.SecondPole, poleConnections)
                    || ArePolesAlreadyConnected(candidate.FirstPole, candidate.SecondPole, previewPoleConnections)
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
        if (first == null || second == null)
        {
            return false;
        }

        for (int i = 0; connections != null && i < connections.Count; i++)
        {
            PoleConnection connection = connections[i];
            if ((connection.FirstPole == first && connection.SecondPole == second)
                || (connection.FirstPole == second && connection.SecondPole == first))
            {
                return true;
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

    private static void EnsureNetworksEvaluated()
    {
        if (networksDirty)
        {
            RebuildNetworks();
            networksDirty = false;
            RefreshConnectionLineRenderersIfDirty();
        }

        RefreshNetworkRuntimeValues();
    }

    private static void RebuildNetworks()
    {
        EnsurePoleConnectionsEvaluated();
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

            BuildNetworkSupplyArea(network);
            networks.Add(network);
        }

        RefreshNetworkRuntimeValues(true);
        RegisterSuppliedConsumerNetworks();
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

    private static void BuildNetworkSupplyArea(ElectricNetwork network)
    {
        if (network == null)
        {
            return;
        }

        network.ClearTopologyRuntime();
        for (int i = 0; i < network.Poles.Count; i++)
        {
            ScanPoleSupplyArea(network.Poles[i], network.SuppliedInstallations);
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
    }

    private static void RefreshNetworkRuntimeValues(ElectricNetwork network)
    {
        if (network == null)
        {
            return;
        }

        network.ClearPowerRuntime();

        foreach (InstallationObject installationObject in network.SuppliedInstallations)
        {
            if (installationObject == null || !installationObject.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (installationObject is SteamGenerator steamGenerator)
            {
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
        }

        if (!network.HasPowerSource)
        {
            network.SupplyRatio = 0f;
            return;
        }

        foreach (InstallationObject installationObject in network.SuppliedInstallations)
        {
            if (installationObject == null || !installationObject.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (TryGetElectricPowerRequirement(installationObject, out float requiredWatts))
            {
                network.RequiredWatts += requiredWatts;
            }

            if (TryGetElectricPowerDemand(installationObject, out float demandWatts))
            {
                network.DemandWatts += demandWatts;
            }
        }

        network.SupplyRatio = network.DemandWatts > EnergyEpsilon
            ? Mathf.Clamp01(network.ProductionWatts / network.DemandWatts)
            : (network.ProductionWatts > EnergyEpsilon ? 1f : 0f);
    }

    private static void ScanPoleSupplyArea(
        UtilityPole pole,
        HashSet<InstallationObject> suppliedInstallations)
    {
        if (pole == null
            || suppliedInstallations == null
            || !pole.TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out _))
        {
            return;
        }

        int radius = pole.SupplyRadiusCells;
        for (int y = anchorCoordinate.y - radius; y <= anchorCoordinate.y + radius; y++)
        {
            for (int x = anchorCoordinate.x - radius; x <= anchorCoordinate.x + radius; x++)
            {
                installationScratch.Clear();
                InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
                    new Vector2Int(x, y),
                    installationScratch);
                for (int i = 0; i < installationScratch.Count; i++)
                {
                    InstallationObject installationObject = installationScratch[i];
                    if (installationObject != null)
                    {
                        suppliedInstallations.Add(installationObject);
                    }
                }
            }
        }

        installationScratch.Clear();
    }

    private static void RegisterSuppliedConsumerNetworks()
    {
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

        return false;
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
        public float ProductionWatts;
        public float RequiredWatts;
        public float DemandWatts;
        public float SupplyRatio;
        public bool HasPowerSource;

        public void ClearTopologyRuntime()
        {
            SuppliedInstallations.Clear();
            ClearPowerRuntime();
        }

        public void ClearPowerRuntime()
        {
            ProductionWatts = 0f;
            RequiredWatts = 0f;
            DemandWatts = 0f;
            SupplyRatio = 0f;
            HasPowerSource = false;
        }
    }
}
