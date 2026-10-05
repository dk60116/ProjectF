using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;

// Prefab configuration and edit/blueprint presentation only.
public partial class LoggingMachine : InstallationObject, ILoggingTarget
{
    public const int DefaultMinimumGrowth = 10;
    public const int DefaultMaximumGrowth = ResourceDefinition.MaxGrowth;
    private static readonly Vector2Int[] LocalHarvestDirections =
    { Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right };
    [SerializeField] private Sprite harvestMarkerIcon;
    [SerializeField] private Transform hinge;
    [SerializeField, Min(1f)] private float hingeRotationDegreesPerSecond = 180f;
    [SerializeField, Min(0f)] private float emptyDirectionHoldSeconds = 0.25f;
    [SerializeField] private int minimumGrowth = DefaultMinimumGrowth;
    [SerializeField] private int maximumGrowth = DefaultMaximumGrowth;
    [SerializeField, HideInInspector] private bool treeFilterInitialized;
    [SerializeField, HideInInspector] private List<string> enabledTreeDefinitionKeys = new List<string>();
    private LoggingFilter filter;
    private LoggingFilter Filter => filter ??= new LoggingFilter(new BlockStateStore.InstallationSaveState
    {
        loggingMinimumGrowth = minimumGrowth, loggingMaximumGrowth = maximumGrowth,
        loggingTreeFilterInitialized = treeFilterInitialized,
        loggingEnabledTreeDefinitionKeys = new List<string>(enabledTreeDefinitionKeys)
    }, null);
    public Sprite HarvestMarkerIcon => harvestMarkerIcon;
    internal ProjectF.Simulation.LoggingProcess DataProcess { get; set; }
    public bool TryGetElectricPowerRequirement(out float watts)
    { watts = ItemDefinition.ResolveElectricUseWatts(BoundItemDefinition ?? InputOutputModule.ResolveItemDefinition(ResolveItemId())); return watts > 0; }
    internal Transform DataHinge => hinge != null ? hinge : transform.Find("Body/Floor/Hinge");
    internal float HingeRotationDegreesPerSecond => hingeRotationDegreesPerSecond;
    internal float EmptyDirectionHoldSeconds => emptyDirectionHoldSeconds;
    public static int HarvestDirectionCount => LocalHarvestDirections.Length;
    public bool IsTreeTypeEnabled(ResourceDefinition definition) => Filter.IsTreeTypeEnabled(definition);
    public void SetTreeTypeEnabled(ResourceDefinition definition, IReadOnlyList<ResourceDefinition> available, bool enabled) => Filter.SetTreeTypeEnabled(definition, available, enabled);
    public void SetAllTreeTypes(IReadOnlyList<ResourceDefinition> available, bool enabled) => Filter.SetAllTreeTypes(available, enabled);
    public void SetGrowthRange(int minimum, int maximum) => Filter.SetGrowthRange(minimum, maximum);
    public List<string> CaptureEnabledTreeDefinitionKeys() => Filter.CaptureEnabledTreeDefinitionKeys();
    public void ApplyTreeFilterState(bool initialized, IReadOnlyList<string> keys, int minimum, int maximum = DefaultMaximumGrowth)
        => Filter.ApplyTreeFilterState(initialized, keys, minimum, maximum);
    public int MinimumGrowth => Filter.MinimumGrowth;
    public int MaximumGrowth => Filter.MaximumGrowth;
    public bool IsTreeFilterInitialized => Filter.IsTreeFilterInitialized;
    public void GetObjectInfoStatus(out string text, out bool working, out bool warning)
    { text = "No placement"; working = false; warning = false; }
    public override void PrepareForPool() { filter = null; DataProcess = default; base.PrepareForPool(); }
    public static Vector2Int GetHarvestCoordinate(
        Vector2Int anchorCoordinate,
        int quarterTurns,
        int directionIndex)
    {
        Vector2Int localDirection = LocalHarvestDirections[NormalizeDirectionIndex(directionIndex)];
        Vector2Int worldDirection = InputOutputModule.RotateRectGridOffset(
            localDirection,
            quarterTurns);
        return anchorCoordinate + worldDirection;
    }
    private static int NormalizeDirectionIndex(int directionIndex)
    {
        int normalized = directionIndex % LocalHarvestDirections.Length;
        return normalized < 0 ? normalized + LocalHarvestDirections.Length : normalized;
    }
#if UNITY_EDITOR
    private const string HarvestMarkerIconAssetPath = "Assets/Image/UI/Item/Saw.png";

    protected override void OnValidate()
    {
        base.OnValidate();
        hingeRotationDegreesPerSecond = Mathf.Max(1f, hingeRotationDegreesPerSecond);
        emptyDirectionHoldSeconds = Mathf.Max(0f, emptyDirectionHoldSeconds);
        minimumGrowth = Mathf.Clamp(
            minimumGrowth,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        maximumGrowth = Mathf.Clamp(maximumGrowth, minimumGrowth, ResourceDefinition.MaxGrowth);
        if (hinge == null)
        {
            hinge = transform.Find("Body/Floor/Hinge");
        }

        if (harvestMarkerIcon == null)
        {
            harvestMarkerIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                HarvestMarkerIconAssetPath);
        }
    }
#endif
}
