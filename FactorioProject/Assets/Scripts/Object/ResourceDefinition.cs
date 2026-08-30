using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public sealed class ResourceDropEntry
{
    [SerializeField] private ItemDefinition itemDefinition;
    [SerializeField, Min(0)] private int amount = 1;
    [SerializeField, Range(ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth)]
    private int minimumGrowth = ResourceDefinition.MinGrowth;
    [SerializeField, Range(ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth)]
    private int maximumGrowth = ResourceDefinition.MaxGrowth;
    [SerializeField, Range(0f, 1f)] private float dropChance = 1f;

    public ItemDefinition ItemDefinition
    {
        get => itemDefinition;
        set => itemDefinition = value;
    }

    public int Amount
    {
        get => Mathf.Max(0, amount);
        set => amount = Mathf.Max(0, value);
    }

    public int MinimumGrowth
    {
        get => Mathf.Clamp(
            minimumGrowth,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        set
        {
            minimumGrowth = Mathf.Clamp(
                value,
                ResourceDefinition.MinGrowth,
                ResourceDefinition.MaxGrowth);
            maximumGrowth = Mathf.Max(minimumGrowth, maximumGrowth);
        }
    }

    public int MaximumGrowth
    {
        get => Mathf.Clamp(
            maximumGrowth,
            MinimumGrowth,
            ResourceDefinition.MaxGrowth);
        set => maximumGrowth = Mathf.Clamp(
            value,
            MinimumGrowth,
            ResourceDefinition.MaxGrowth);
    }

    public float DropChance
    {
        get => Mathf.Clamp01(dropChance);
        set => dropChance = Mathf.Clamp01(value);
    }

    public bool Matches(float growth)
    {
        return growth >= MinimumGrowth && growth <= MaximumGrowth;
    }

    public void Normalize()
    {
        Amount = amount;
        MinimumGrowth = minimumGrowth;
        MaximumGrowth = maximumGrowth;
        DropChance = dropChance;
    }
}

[CreateAssetMenu(menuName = "ProjectF/Resource Definition", fileName = "ResourceDef_")]
public class ResourceDefinition : ScriptableObject
{
    public const int MinGrowth = 0;
    public const int MaxGrowth = 10;
    public const int DefaultGrowth = MaxGrowth;
    public const int GrowthStageCount = MaxGrowth - MinGrowth;
    private const float GrowthRequirementWeightSum =
        GrowthStageCount * (GrowthStageCount + 1) * 0.5f;

    public enum PlacementCategory
    {
        Ore,
        Oil,
        Tree
    }

    public string resourceName;
    [SerializeField] private Sprite resourceIcon;
    public Resource prefab;
    public Resource.HarvestMode harvestMode = Resource.HarvestMode.Auto;
    public PlacementCategory placementCategory = PlacementCategory.Ore;
    [Range(MinGrowth, MaxGrowth)] public int minimumGrowth = DefaultGrowth;
    [Range(MinGrowth, MaxGrowth)] public int maximumGrowth = DefaultGrowth;
    [SerializeField, Min(0f)]
    [Tooltip("Total water used while growing from Growth 0 to 10, in liters.")]
    private float totalGrowthWaterLiters;
    [SerializeField, Min(0f)]
    [Tooltip("Total fertilizer used while growing from Growth 0 to 10.")]
    private float totalGrowthFertilizerAmount;
    [FormerlySerializedAs("totalGrowthDurationSeconds")]
    [SerializeField, Min(0f)]
    [Tooltip("Fixed time in seconds required to advance by one Growth level after water and fertilizer requirements are met.")]
    private float growthDurationPerLevelSeconds;
    [SerializeField] private List<ResourceDropEntry> dropItems =
        new List<ResourceDropEntry>();
    public int defaultResourceCount = 1;
    public int defaultGetCount = 1;
    public int defaultMaxGauge = 10;
    public int defaultCurrentGauge = 10;
    public IReadOnlyList<ResourceDropEntry> DropItems =>
        dropItems ??= new List<ResourceDropEntry>();
    public Sprite ResourceIcon => resourceIcon;
    public float TotalGrowthWaterLiters => Mathf.Max(0f, totalGrowthWaterLiters);
    public float TotalGrowthFertilizerAmount => Mathf.Max(0f, totalGrowthFertilizerAmount);
    public float GrowthDurationPerLevelSeconds => Mathf.Max(
        0f,
        growthDurationPerLevelSeconds);
    public bool HasGrowthSchedule => GrowthDurationPerLevelSeconds > 0f;

    public float GetGrowthWaterRequirement(int targetGrowthLevel)
    {
        return CalculateGrowthRequirement(
            TotalGrowthWaterLiters,
            targetGrowthLevel);
    }

    public float GetGrowthFertilizerRequirement(int targetGrowthLevel)
    {
        return CalculateGrowthRequirement(
            TotalGrowthFertilizerAmount,
            targetGrowthLevel);
    }

    public static float CalculateGrowthRequirement(
        float totalAmount,
        int targetGrowthLevel)
    {
        if (totalAmount <= 0f || GrowthRequirementWeightSum <= 0f)
        {
            return 0f;
        }

        int stageWeight = Mathf.Clamp(
            targetGrowthLevel - MinGrowth,
            1,
            GrowthStageCount);
        return totalAmount * stageWeight / GrowthRequirementWeightSum;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        minimumGrowth = Mathf.Clamp(minimumGrowth, MinGrowth, MaxGrowth);
        maximumGrowth = Mathf.Clamp(maximumGrowth, minimumGrowth, MaxGrowth);
        defaultResourceCount = Mathf.Max(1, defaultResourceCount);
        defaultGetCount = Mathf.Max(1, defaultGetCount);
        defaultMaxGauge = Mathf.Max(1, defaultMaxGauge);
        defaultCurrentGauge = Mathf.Clamp(defaultCurrentGauge, 0, defaultMaxGauge);
        totalGrowthWaterLiters = Mathf.Max(0f, totalGrowthWaterLiters);
        totalGrowthFertilizerAmount = Mathf.Max(0f, totalGrowthFertilizerAmount);
        growthDurationPerLevelSeconds = Mathf.Max(0f, growthDurationPerLevelSeconds);
        dropItems ??= new List<ResourceDropEntry>();
        for (int i = 0; i < dropItems.Count; i++)
        {
            dropItems[i] ??= new ResourceDropEntry();
            dropItems[i].Normalize();
        }
    }
#endif
}
