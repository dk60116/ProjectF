using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Resource Definition", fileName = "ResourceDef_")]
public class ResourceDefinition : ScriptableObject
{
    public enum PlacementCategory
    {
        Ore,
        Oil
    }

    public string resourceName;
    public Resource prefab;
    public Resource.HarvestMode harvestMode = Resource.HarvestMode.Auto;
    public PlacementCategory placementCategory = PlacementCategory.Ore;
    public int defaultResourceCount = 1;
    public int defaultGetCount = 1;
    public int defaultMaxGauge = 10;
    public int defaultCurrentGauge = 10;
}
