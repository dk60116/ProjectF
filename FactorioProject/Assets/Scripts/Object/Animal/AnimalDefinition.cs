using UnityEngine;

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

    [SerializeField, Min(-1)] private int id = -1;
    [SerializeField] private string animalName = string.Empty;
    [SerializeField, Range(MinSpawnAge, MaxSpawnAge)] private int spawnAge = DefaultSpawnAge;
    [SerializeField, Min(1)] private int minHerdSize = DefaultMinHerdSize;
    [SerializeField, Min(1)] private int maxHerdSize = DefaultMaxHerdSize;
    [SerializeField, Min(1)] private int spawnWeight = DefaultSpawnWeight;
    [SerializeField] private GameObject animalPrefab;
    [SerializeField] private Sprite adultIcon;
    [SerializeField] private Sprite childIcon;

    public int Id => id;
    public string AnimalName => animalName;
    public int SpawnAgeWeight => spawnAge;
    public int MinHerdSize => minHerdSize;
    public int MaxHerdSize => maxHerdSize;
    public int SpawnWeight => spawnWeight;
    public GameObject AnimalPrefab => animalPrefab;
    public Sprite AdultIcon => adultIcon;
    public Sprite ChildIcon => childIcon;

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
    }
#endif
}
