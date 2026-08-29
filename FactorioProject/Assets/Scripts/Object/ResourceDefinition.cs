using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Resource Definition", fileName = "ResourceDef_")]
public class ResourceDefinition : ScriptableObject
{
    public const int MinGrowth = 0;
    public const int MaxGrowth = 10;
    public const int DefaultGrowth = MaxGrowth;

    public enum PlacementCategory
    {
        Ore,
        Oil,
        Tree
    }

    public string resourceName;
    public Resource prefab;
    public Resource.HarvestMode harvestMode = Resource.HarvestMode.Auto;
    public PlacementCategory placementCategory = PlacementCategory.Ore;
    [Range(MinGrowth, MaxGrowth)] public int minimumGrowth = DefaultGrowth;
    [Range(MinGrowth, MaxGrowth)] public int maximumGrowth = DefaultGrowth;
    public int defaultResourceCount = 1;
    public int defaultGetCount = 1;
    public int defaultMaxGauge = 10;
    public int defaultCurrentGauge = 10;

#if UNITY_EDITOR
    private void OnValidate()
    {
        minimumGrowth = Mathf.Clamp(minimumGrowth, MinGrowth, MaxGrowth);
        maximumGrowth = Mathf.Clamp(maximumGrowth, minimumGrowth, MaxGrowth);
        defaultResourceCount = Mathf.Max(1, defaultResourceCount);
        defaultGetCount = Mathf.Max(1, defaultGetCount);
        defaultMaxGauge = Mathf.Max(1, defaultMaxGauge);
        defaultCurrentGauge = Mathf.Clamp(defaultCurrentGauge, 0, defaultMaxGauge);
    }
#endif
}
