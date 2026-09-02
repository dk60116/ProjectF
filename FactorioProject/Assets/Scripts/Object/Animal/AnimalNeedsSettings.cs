using System;
using UnityEngine;

[Serializable]
public sealed class AnimalNeedsSettings
{
    public const float DefaultMaxHunger = 100f;
    public const float DefaultHungerDrainPerSecond = 0.05f;
    public const float DefaultHungryThresholdRatio = 0.5f;
    public const float DefaultFoodEnergyPerItem = 25f;
    public const float DefaultFoodSearchRadius = 8f;
    public const float DefaultDefecationIntervalSeconds = 300f;
    public const int DefaultDefecationAmount = 1;
    public const float DefaultUnattendedDroppingLifetimeSeconds = 300f;

    [Header("Hunger")]
    [SerializeField, Min(1f)] private float maxHunger = DefaultMaxHunger;
    [SerializeField, Min(0f)] private float hungerDrainPerSecond = DefaultHungerDrainPerSecond;
    [SerializeField, Range(0f, 1f)] private float hungryThresholdRatio = DefaultHungryThresholdRatio;
    [SerializeField, Min(0.01f)] private float foodEnergyPerItem = DefaultFoodEnergyPerItem;
    [SerializeField, Min(0.5f)] private float foodSearchRadius = DefaultFoodSearchRadius;

    [Header("Defecation")]
    [SerializeField, Min(1f)]
    private float defecationIntervalSeconds = DefaultDefecationIntervalSeconds;
    [SerializeField, Min(1)] private int defecationAmount = DefaultDefecationAmount;
    [SerializeField, Min(0f)]
    private float unattendedDroppingLifetimeSeconds =
        DefaultUnattendedDroppingLifetimeSeconds;

    public float MaxHunger
    {
        get => Mathf.Max(1f, maxHunger);
        set => maxHunger = Mathf.Max(1f, value);
    }

    public float HungerDrainPerSecond
    {
        get => Mathf.Max(0f, hungerDrainPerSecond);
        set => hungerDrainPerSecond = Mathf.Max(0f, value);
    }

    public float HungryThresholdRatio
    {
        get => Mathf.Clamp01(hungryThresholdRatio);
        set => hungryThresholdRatio = Mathf.Clamp01(value);
    }

    public float FoodEnergyPerItem
    {
        get => Mathf.Max(0.01f, foodEnergyPerItem);
        set => foodEnergyPerItem = Mathf.Max(0.01f, value);
    }

    public float FoodSearchRadius
    {
        get => Mathf.Max(0.5f, foodSearchRadius);
        set => foodSearchRadius = Mathf.Max(0.5f, value);
    }

    public float DefecationIntervalSeconds
    {
        get => Mathf.Max(1f, defecationIntervalSeconds);
        set => defecationIntervalSeconds = Mathf.Max(1f, value);
    }

    public int DefecationAmount
    {
        get => Mathf.Max(1, defecationAmount);
        set => defecationAmount = Mathf.Max(1, value);
    }

    public float UnattendedDroppingLifetimeSeconds
    {
        get => Mathf.Max(0f, unattendedDroppingLifetimeSeconds);
        set => unattendedDroppingLifetimeSeconds = Mathf.Max(0f, value);
    }

    public AnimalNeedsSettings Clone()
    {
        return new AnimalNeedsSettings
        {
            maxHunger = MaxHunger,
            hungerDrainPerSecond = HungerDrainPerSecond,
            hungryThresholdRatio = HungryThresholdRatio,
            foodEnergyPerItem = FoodEnergyPerItem,
            foodSearchRadius = FoodSearchRadius,
            defecationIntervalSeconds = DefecationIntervalSeconds,
            defecationAmount = DefecationAmount,
            unattendedDroppingLifetimeSeconds = UnattendedDroppingLifetimeSeconds
        };
    }

    public void Normalize()
    {
        MaxHunger = maxHunger;
        HungerDrainPerSecond = hungerDrainPerSecond;
        HungryThresholdRatio = hungryThresholdRatio;
        FoodEnergyPerItem = foodEnergyPerItem;
        FoodSearchRadius = foodSearchRadius;
        DefecationIntervalSeconds = defecationIntervalSeconds;
        DefecationAmount = defecationAmount;
        UnattendedDroppingLifetimeSeconds = unattendedDroppingLifetimeSeconds;
    }
}
