using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class AnimalDropEntry
{
    [SerializeField] private ItemDefinition itemDefinition;
    [SerializeField, Min(0)] private int minAmount = 1;
    [SerializeField, Min(0)] private int maxAmount = 1;
    [SerializeField, Range(0f, 1f)] private float dropChance = 1f;

    public ItemDefinition ItemDefinition
    {
        get => itemDefinition;
        set => itemDefinition = value;
    }

    public int MinAmount
    {
        get => Mathf.Max(0, minAmount);
        set
        {
            minAmount = Mathf.Max(0, value);
            maxAmount = Mathf.Max(minAmount, maxAmount);
        }
    }

    public int MaxAmount
    {
        get => Mathf.Max(MinAmount, maxAmount);
        set => maxAmount = Mathf.Max(MinAmount, value);
    }

    public float DropChance
    {
        get => Mathf.Clamp01(dropChance);
        set => dropChance = Mathf.Clamp01(value);
    }

    public AnimalDropEntry Clone()
    {
        return new AnimalDropEntry
        {
            itemDefinition = itemDefinition,
            minAmount = MinAmount,
            maxAmount = MaxAmount,
            dropChance = DropChance
        };
    }

    public void Normalize()
    {
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        DropChance = dropChance;
    }

    public static List<AnimalDropEntry> CloneList(
        IReadOnlyList<AnimalDropEntry> source)
    {
        List<AnimalDropEntry> result = new List<AnimalDropEntry>(
            source != null ? source.Count : 0);
        for (int i = 0; source != null && i < source.Count; i++)
        {
            result.Add(source[i]?.Clone() ?? new AnimalDropEntry());
        }

        return result;
    }
}

[CreateAssetMenu(menuName = "ProjectF/Animal Definition", fileName = "Animal_")]
public sealed class AnimalDefinition : ScriptableObject
{
    public const int MinSpawnAge = 1;
    public const int MaxSpawnAge = 10;
    public const int DefaultSpawnAge = 7;
    public const float SpawnAgeStandardDeviation = 2f;
    public const int DefaultMinHerdSize = 2;
    public const int DefaultMaxHerdSize = 6;
    public const int DefaultSpawnWeight = DefaultMinHerdSize;
    public const float DefaultMaxHealth = 100f;

    [SerializeField, Min(-1)] private int id = -1;
    [SerializeField] private string animalName = string.Empty;
    [SerializeField, Range(MinSpawnAge, MaxSpawnAge)] private int spawnAge = DefaultSpawnAge;
    [SerializeField, Min(1)] private int minHerdSize = DefaultMinHerdSize;
    [SerializeField, Min(1)] private int maxHerdSize = DefaultMaxHerdSize;
    [SerializeField, Min(1)] private int spawnWeight = DefaultSpawnWeight;
    [SerializeField, Min(1f)] private float maxHealth = DefaultMaxHealth;
    [SerializeField] private List<AnimalDropEntry> dropItems = new List<AnimalDropEntry>();
    [SerializeField] private GameObject animalPrefab;
    [SerializeField] private Sprite adultIcon;
    [SerializeField] private Sprite childIcon;
    [SerializeField] private AnimalAISettings aiSettings = new AnimalAISettings();

    public int Id => id;
    public string AnimalName => animalName;
    public int SpawnAgeWeight => spawnAge;
    public int MinHerdSize => minHerdSize;
    public int MaxHerdSize => maxHerdSize;
    public int SpawnWeight => spawnWeight;
    public float MaxHealth => Mathf.Max(1f, maxHealth);
    public IReadOnlyList<AnimalDropEntry> DropItems =>
        dropItems ??= new List<AnimalDropEntry>();
    public GameObject AnimalPrefab => animalPrefab;
    public Sprite AdultIcon => adultIcon;
    public Sprite ChildIcon => childIcon;
    public AnimalAISettings AISettings => aiSettings ??= new AnimalAISettings();

    public static float EvaluateSpawnAgeWeight(int age, int preferredAge)
    {
        int clampedAge = Mathf.Clamp(age, MinSpawnAge, MaxSpawnAge);
        int clampedPreferredAge = Mathf.Clamp(preferredAge, MinSpawnAge, MaxSpawnAge);
        float normalizedDistance = (clampedAge - clampedPreferredAge) / SpawnAgeStandardDeviation;
        return Mathf.Exp(-0.5f * normalizedDistance * normalizedDistance);
    }

    public static float GetSpawnAgeProbability(int age, int preferredAge)
    {
        float totalWeight = 0f;
        for (int candidateAge = MinSpawnAge; candidateAge <= MaxSpawnAge; candidateAge++)
        {
            totalWeight += EvaluateSpawnAgeWeight(candidateAge, preferredAge);
        }

        return totalWeight > 0f
            ? EvaluateSpawnAgeWeight(age, preferredAge) / totalWeight
            : 0f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        id = Mathf.Max(-1, id);
        animalName = animalName?.Trim() ?? string.Empty;
        spawnAge = Mathf.Clamp(spawnAge, MinSpawnAge, MaxSpawnAge);
        minHerdSize = Mathf.Max(1, minHerdSize);
        maxHerdSize = Mathf.Max(minHerdSize, maxHerdSize);
        spawnWeight = Mathf.Clamp(spawnWeight, minHerdSize, maxHerdSize);
        maxHealth = Mathf.Max(1f, maxHealth);
        dropItems ??= new List<AnimalDropEntry>();
        for (int i = 0; i < dropItems.Count; i++)
        {
            dropItems[i] ??= new AnimalDropEntry();
            dropItems[i].Normalize();
        }

        aiSettings ??= new AnimalAISettings();
        aiSettings.Normalize();
    }
#endif
}
