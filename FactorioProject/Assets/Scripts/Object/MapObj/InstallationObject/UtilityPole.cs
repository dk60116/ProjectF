using System.Collections.Generic;
using UnityEngine;
using ProjectF.Power;

public partial class UtilityPole : InstallationObject
{
    [SerializeField, Min(0f)] private float supplyRangeVisualYOffset = .055f;
    [SerializeField] private Transform linePointCenter;
    [SerializeField] private Transform linePointA, linePointB;
    [SerializeField, Min(.001f)] private float lineWidth = .025f;
    [SerializeField, Min(0f)] private float lineSagDepth = .06f;
    [SerializeField, Min(0f)] private float connectionLineSagDepth = .18f;
    [SerializeField, Min(2)] private int lineCurveSegments = 8;
    [SerializeField] private Color lineColor = new Color(.05f, .04f, .035f, 1f);
    private UtilityPoleRuntime runtime;
    public UtilityPoleRuntime Runtime => runtime ?? (runtime = new UtilityPoleRuntime(this));
    public int ConnectionRadiusCells => Runtime.ConnectionRadiusCells;
    public int SupplyRadiusCells => Runtime.SupplyRadiusCells;
    protected new void Awake() { base.Awake(); Runtime.RefreshAuthoring(); }
    protected override void OnEnable() { base.OnEnable(); Runtime.Activate(); }
    protected override void OnDisable() { if (ProjectFApplicationLifecycle.IsQuitting) return; runtime?.Deactivate(); base.OnDisable(); }
    private void OnDestroy() { if (!ProjectFApplicationLifecycle.IsQuitting) runtime?.Deactivate(); }
    public override void PrepareForPool() { runtime?.Deactivate(); base.PrepareForPool(); }
    public void SetSelectedSupplyRangeVisualRequested(bool requested) => Runtime.SetSelectedSupplyRangeVisualRequested(requested);
    public void SetSelectedSupplyRangeVisualRequested(bool requested, bool connectionRangeRequested) => Runtime.SetSelectedSupplyRangeVisualRequested(requested, connectionRangeRequested);
    public bool TryGetSupplyRangeBounds(out Bounds bounds) => Runtime.TryGetSupplyRangeBounds(out bounds);
    public bool TryGetConnectionRangeBounds(out Bounds bounds) => Runtime.TryGetConnectionRangeBounds(out bounds);
    public bool TryGetObjectInfoNetworkPower(out float production, out float required) => Runtime.TryGetObjectInfoNetworkPower(out production, out required);
    public bool CaptureConnectedPoleAnchorCoordinates(List<Vector2Int> destination) => Runtime.CaptureConnectedPoleAnchorCoordinates(destination);
    internal void Configure(UtilityPoleRuntime node)
    {
        linePointCenter = ResolvePoint(linePointCenter, "LinePointCenter");
        linePointA = ResolvePoint(linePointA, "LinePointA") ?? FindDescendant(transform, "LinePointCenter (1)");
        linePointB = ResolvePoint(linePointB, "LinePointB") ?? FindDescendant(transform, "LinePointCenter (2)");
        node.ConfigurePoint(0, linePointCenter != null, LocalPoint(linePointCenter));
        node.ConfigurePoint(1, linePointA != null, LocalPoint(linePointA));
        node.ConfigurePoint(2, linePointB != null, LocalPoint(linePointB));
        node.ConfigureStyle(supplyRangeVisualYOffset, lineWidth, lineSagDepth, connectionLineSagDepth, lineCurveSegments, lineColor);
    }
    private Transform ResolvePoint(Transform point, string pointName) => point != null && point.IsChildOf(transform) ? point : FindDescendant(transform, pointName);
    private static Transform FindDescendant(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) { var match = FindDescendant(root.GetChild(i), name); if (match != null) return match; }
        return null;
    }
    private Vector3 LocalPoint(Transform point)
    {
        Vector3 local = Vector3.zero;
        for (Transform current = point; current != null && current != transform; current = current.parent)
            local = current.localRotation * Vector3.Scale(local, current.localScale) + current.localPosition;
        return local;
    }
    public static void RefreshAllRangeVisuals() => UtilityPoleRuntime.RefreshAllRangeVisuals();
    public static void BeginTopologyRefreshBatch() => UtilityPoleRuntime.BeginTopologyRefreshBatch();
    public static void EndTopologyRefreshBatch(bool rebuildDirtyTopology = true) => UtilityPoleRuntime.EndTopologyRefreshBatch(rebuildDirtyTopology);
    public static void RegisterBlueprintPreview(
        UtilityPole pole,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        bool topologyReplacement = false) => UtilityPoleRuntime.RegisterBlueprintPreview(pole != null ? pole.Runtime : null, anchorCoordinate, quarterTurns, topologyReplacement);
    public static void UnregisterBlueprintPreview(UtilityPole pole) => UtilityPoleRuntime.UnregisterBlueprintPreview(pole != null ? pole.Runtime : null);
    public static void RegisterConsumerBlueprintPreview(
        InstallationObject consumer,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        ItemDefinition definition) => UtilityPoleRuntime.RegisterConsumerBlueprintPreview(consumer, anchorCoordinate, quarterTurns, definition);
    public static void UnregisterConsumerBlueprintPreview(InstallationObject consumer) => UtilityPoleRuntime.UnregisterConsumerBlueprintPreview(consumer);
    public static void ClearBlueprintPreviews() => UtilityPoleRuntime.ClearBlueprintPreviews();
    public static void SetInstallOrEditRangeVisualsRequested(
        bool supplyRangeRequested,
        bool connectionRangeRequested) => UtilityPoleRuntime.SetInstallOrEditRangeVisualsRequested(supplyRangeRequested, connectionRangeRequested);
    public static void RefreshScreenRangeVisuals(Camera targetCamera) => UtilityPoleRuntime.RefreshScreenRangeVisuals(targetCamera);
    public static bool HasElectricityAvailable(InputOutputModule consumer) => UtilityPoleRuntime.HasElectricityAvailable(consumer);
    public static bool HasElectricityAvailable(InstallationObject consumer) => UtilityPoleRuntime.HasElectricityAvailable(consumer);
    public static bool IsConnectedToElectricNetwork(InstallationObject participant) => UtilityPoleRuntime.IsConnectedToElectricNetwork(participant);
    public static bool TryGetElectricPowerInfo(
        InstallationObject consumer,
        out float suppliedWatts,
        out float requiredWatts) => UtilityPoleRuntime.TryGetElectricPowerInfo(consumer, out suppliedWatts, out requiredWatts);
    public static bool TryConsumeElectricity(
        InputOutputModule consumer,
        float requestedEnergy,
        float deltaTime,
        out float consumedEnergy) => UtilityPoleRuntime.TryConsumeElectricity(consumer, requestedEnergy, deltaTime, out consumedEnergy);
    public static bool TryConsumeElectricity(
        InstallationObject consumer,
        float requestedEnergy,
        float deltaTime,
        out float consumedEnergy) => UtilityPoleRuntime.TryConsumeElectricity(consumer, requestedEnergy, deltaTime, out consumedEnergy);
    public static bool TryConsumeElectricityUnits(
        InstallationObject consumer,
        long requestedEnergyUnits,
        out long consumedEnergyUnits) => UtilityPoleRuntime.TryConsumeElectricityUnits(consumer, requestedEnergyUnits, out consumedEnergyUnits);
    public static bool TryGetElectricSupplyRatio(
        InstallationObject consumer,
        float requestedWatts,
        out float supplyRatio) => UtilityPoleRuntime.TryGetElectricSupplyRatio(consumer, requestedWatts, out supplyRatio);
    public static void NotifyElectricPowerSourceStateChanged(SteamGenerator source) => UtilityPoleRuntime.NotifyElectricPowerSourceStateChanged(source);
    public static void NotifyElectricPowerConsumerStateChanged(InstallationObject consumer) => UtilityPoleRuntime.NotifyElectricPowerConsumerStateChanged(consumer);
    internal static bool TryCaptureElectricPowerDemand(
        InstallationObject consumer,
        out float wattsPerSecond) => UtilityPoleRuntime.TryCaptureElectricPowerDemand(consumer, out wattsPerSecond);
    internal static bool TracksRuntimeElectricPowerDemand(InstallationObject consumer) => UtilityPoleRuntime.TracksRuntimeElectricPowerDemand(consumer);
    internal static bool HasElectricPowerDemandChanged(
        bool previouslyHadDemand,
        float previousWattsPerSecond,
        bool hasDemand,
        float wattsPerSecond) => UtilityPoleRuntime.HasElectricPowerDemandChanged(previouslyHadDemand, previousWattsPerSecond, hasDemand, wattsPerSecond);
    public static void NotifyFreeElectroEnergyChanged() => UtilityPoleRuntime.NotifyFreeElectroEnergyChanged();
    internal static void BeginSimulationPowerMutationBatch() => UtilityPoleRuntime.BeginSimulationPowerMutationBatch();
    internal static void EndSimulationPowerMutationBatch() => UtilityPoleRuntime.EndSimulationPowerMutationBatch();
    internal static void FlushDeferredVisualRefreshes() => UtilityPoleRuntime.FlushDeferredVisualRefreshes();
    internal static void PrepareSimulationPowerTick() => UtilityPoleRuntime.PrepareSimulationPowerTick();
    internal static void InvalidateRobotArmConsumers() => UtilityPoleRuntime.InvalidateRobotArmConsumers();
    internal static void UnregisterRobotArmConsumer(IDataElectricConsumer arm) => UtilityPoleRuntime.UnregisterRobotArmConsumer(arm);
    internal static void InvalidateDataConsumerDemand(IDataElectricConsumer consumer) => UtilityPoleRuntime.InvalidateDataConsumerDemand(consumer);
    internal static int WakeAllDataElectricConsumers(out int candidates) => UtilityPoleRuntime.WakeAllDataElectricConsumers(out candidates);
    public static bool TryGetElectricSupplyRatio(IDataElectricConsumer consumer, float watts, out float ratio) => UtilityPoleRuntime.TryGetElectricSupplyRatio(consumer, watts, out ratio);
    internal static void PrepareRobotArmPowerTick() => UtilityPoleRuntime.PrepareRobotArmPowerTick();
    public static bool HasElectricityAvailable(IDataElectricConsumer arm) => UtilityPoleRuntime.HasElectricityAvailable(arm);
    public static bool TryGetElectricPowerInfo(IDataElectricConsumer arm, out float supplied, out float required) => UtilityPoleRuntime.TryGetElectricPowerInfo(arm, out supplied, out required);
    public static bool TryConsumeElectricity(
        IDataElectricConsumer arm,
        float requested,
        float deltaTime,
        out float consumed) => UtilityPoleRuntime.TryConsumeElectricity(arm, requested, deltaTime, out consumed);
    internal static bool TryConsumeRobotArmElectricity(
        IDataElectricConsumer arm,
        float requiredWatts,
        float requestedEnergy,
        out float consumedEnergy) => UtilityPoleRuntime.TryConsumeRobotArmElectricity(arm, requiredWatts, requestedEnergy, out consumedEnergy);
    internal static void AppendRobotArmPowerProfilerCounters()
    {
        UtilityPoleRuntime.AppendRobotArmPowerProfilerCounters();
        UtilityPoleWorld.AppendProfilerCounters();
        ProjectF.Rendering.UtilityPoleWireRenderer.AppendProfilerCounters();
    }
    public static bool InstallOrEditRangeVisualsRequested => UtilityPoleRuntime.InstallOrEditRangeVisualsRequested;
}
