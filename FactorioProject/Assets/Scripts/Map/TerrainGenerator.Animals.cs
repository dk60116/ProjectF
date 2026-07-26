using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class TerrainGenerator : MonoBehaviour
{
    private const float AnimalSpawnFrequencyScale = 0.01f;

    [Header("Animal Generation")]
    [SerializeField] private bool generateAnimals = true;
    [Tooltip("Spawn ratio per eligible ground tile. Runtime spawning applies a separate 1/100 frequency scale.")]
    [SerializeField, Range(0f, 1f)] private float animalDensity = 0.00001f;
    [Tooltip("Maximum tile distance used to spread members around a herd center.")]
    [SerializeField, Min(1)] private int animalHerdSpreadRadius = 3;
    [SerializeField] private List<AnimalDefinition> animalDefinitions = new List<AnimalDefinition>();
    [SerializeField] private bool showAnimalSpawnGizmos;

    private sealed class AnimalSpeciesPair
    {
        public AnimalDefinition male;
        public AnimalDefinition female;

        public AnimalDefinition Settings => female != null ? female : male;
        public int MinHerdSize => Settings != null ? Settings.MinHerdSize : AnimalDefinition.DefaultMinHerdSize;
        public int MaxHerdSize => Settings != null ? Settings.MaxHerdSize : AnimalDefinition.DefaultMaxHerdSize;
        public int PreferredHerdSize => Settings != null
            ? Settings.SpawnWeight
            : AnimalDefinition.DefaultSpawnWeight;
    }

    private struct AnimalHerdPlan
    {
        public AnimalSpeciesPair species;
        public int size;
    }

    private struct DeterministicAnimalRandom
    {
        private uint state;

        public DeterministicAnimalRandom(int worldSeed, Vector2Int chunkCoordinate)
        {
            unchecked
            {
                state = (uint)worldSeed;
                state ^= (uint)chunkCoordinate.x * 0x9E3779B9u;
                state ^= (uint)chunkCoordinate.y * 0x85EBCA6Bu;
                state ^= 0xC2B2AE35u;
                if (state == 0u)
                {
                    state = 0x6D2B79F5u;
                }
            }
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public float Value()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216f;
        }

        private uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }
    }

    private readonly List<AnimalSpeciesPair> animalSpeciesCache = new List<AnimalSpeciesPair>();
    private readonly List<Block> animalEligibleBlocksScratch = new List<Block>();
    private readonly List<AnimalHerdPlan> animalHerdPlansScratch = new List<AnimalHerdPlan>();
    private readonly Dictionary<Vector2Int, Block> animalEligibleBlockLookup = new Dictionary<Vector2Int, Block>();
    private readonly HashSet<Vector2Int> animalUsedCoordinatesScratch = new HashSet<Vector2Int>();
    private readonly Dictionary<long, AnimalSaveEntry> animalSaveOverrides = new Dictionary<long, AnimalSaveEntry>();
    private readonly HashSet<long> loadedAnimalIds = new HashSet<long>();
    private int animalDefinitionCacheHash = int.MinValue;

    public float AnimalDensity => animalDensity;
    public float EffectiveAnimalDensity => animalDensity * AnimalSpawnFrequencyScale;

    private void NormalizeAnimalGenerationSettings()
    {
        animalDensity = Mathf.Clamp01(animalDensity);
        animalHerdSpreadRadius = Mathf.Max(1, animalHerdSpreadRadius);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            SyncAnimalDefinitionsFromAssets();
        }
#endif
    }

#if UNITY_EDITOR
    public void SyncAnimalDefinitionsFromAssets()
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimalDefinition", new[] { "Assets/Animals" });
        Array.Sort(guids, (left, right) => string.Compare(
            AssetDatabase.GUIDToAssetPath(left),
            AssetDatabase.GUIDToAssetPath(right),
            StringComparison.OrdinalIgnoreCase));

        List<AnimalDefinition> found = new List<AnimalDefinition>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            AnimalDefinition definition = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (definition != null && definition.AnimalPrefab != null)
            {
                found.Add(definition);
            }
        }

        bool changed = animalDefinitions == null || animalDefinitions.Count != found.Count;
        if (!changed)
        {
            for (int i = 0; i < found.Count; i++)
            {
                if (animalDefinitions[i] != found[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return;
        }

        animalDefinitions = found;
        animalDefinitionCacheHash = int.MinValue;
        EditorUtility.SetDirty(this);
    }
#endif

    private void SpawnAnimalsForChunk(Vector2Int chunkCoordinate, Transform chunkTransform, Block[] chunkBlocks)
    {
        if (!generateAnimals || animalDensity <= 0f || chunkTransform == null || chunkBlocks == null)
        {
            return;
        }

        EnsureAnimalSpeciesCache();
        if (animalSpeciesCache.Count == 0)
        {
            return;
        }

        Transform animalRoot = GetOrCreateAnimalRoot(chunkTransform);
        animalUsedCoordinatesScratch.Clear();
        CacheExistingAnimalCoordinates(animalRoot, animalUsedCoordinatesScratch);
        SpawnSavedAnimalsForChunk(chunkCoordinate, animalRoot, animalUsedCoordinatesScratch);

        animalEligibleBlocksScratch.Clear();
        animalEligibleBlockLookup.Clear();
        for (int i = 0; i < chunkBlocks.Length; i++)
        {
            Block block = chunkBlocks[i];
            if (!CanSpawnAnimalOnBlock(block)
                || animalUsedCoordinatesScratch.Contains(block.Coordinate))
            {
                continue;
            }

            animalEligibleBlocksScratch.Add(block);
            animalEligibleBlockLookup[block.Coordinate] = block;
        }

        DeterministicAnimalRandom random = new DeterministicAnimalRandom(seed, chunkCoordinate);
        float expectedAnimalCount = animalEligibleBlocksScratch.Count * EffectiveAnimalDensity;
        BuildAnimalHerdPlans(expectedAnimalCount, animalEligibleBlocksScratch.Count, ref random);
        if (animalHerdPlansScratch.Count == 0)
        {
            ClearAnimalSpawnScratch();
            return;
        }

        for (int herdIndex = 0;
             herdIndex < animalHerdPlansScratch.Count && animalEligibleBlockLookup.Count > 0;
             herdIndex++)
        {
            AnimalHerdPlan herdPlan = animalHerdPlansScratch[herdIndex];
            AnimalSpeciesPair species = herdPlan.species;
            int herdSize = herdPlan.size;
            Block centerBlock = animalEligibleBlocksScratch[random.Range(0, animalEligibleBlocksScratch.Count)];

            for (int memberIndex = 0; memberIndex < herdSize; memberIndex++)
            {
                if (!TryTakeAnimalSpawnBlock(centerBlock.Coordinate, ref random, out Block spawnBlock))
                {
                    break;
                }

                Vector2Int coordinate = spawnBlock.Coordinate;
                long deterministicId = BuildAnimalDeterministicId(coordinate);
                if (animalSaveOverrides.ContainsKey(deterministicId))
                {
                    continue;
                }

                AnimalDefinition definition = ChooseGenderDefinition(species, ref random);
                if (definition == null)
                {
                    continue;
                }

                Vector3 position = spawnBlock.transform.position;
                Quaternion rotation = Quaternion.Euler(0f, random.Value() * 360f, 0f);
                SpawnAnimalInstance(
                    definition,
                    deterministicId,
                    position,
                    rotation,
                    ChooseAnimalSpawnAge(definition, ref random),
                    -1f,
                    false,
                    animalRoot);
            }
        }

        ClearAnimalSpawnScratch();
    }

    private bool CanSpawnAnimalOnBlock(Block block)
    {
        return block != null
               && block.gameObject.activeInHierarchy
               && block.MapObject == null
               && GetTileBiome(block.Coordinate) != TerrainBiome.Water;
    }

    private bool TryTakeAnimalSpawnBlock(
        Vector2Int herdCenter,
        ref DeterministicAnimalRandom random,
        out Block block)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector2Int coordinate = herdCenter + new Vector2Int(
                random.Range(-animalHerdSpreadRadius, animalHerdSpreadRadius + 1),
                random.Range(-animalHerdSpreadRadius, animalHerdSpreadRadius + 1));
            if (!animalEligibleBlockLookup.TryGetValue(coordinate, out block))
            {
                continue;
            }

            animalEligibleBlockLookup.Remove(coordinate);
            animalUsedCoordinatesScratch.Add(coordinate);
            return true;
        }

        int count = animalEligibleBlocksScratch.Count;
        int start = count > 0 ? random.Range(0, count) : 0;
        for (int i = 0; i < count; i++)
        {
            Block candidate = animalEligibleBlocksScratch[(start + i) % count];
            if (candidate == null
                || !animalEligibleBlockLookup.Remove(candidate.Coordinate))
            {
                continue;
            }

            animalUsedCoordinatesScratch.Add(candidate.Coordinate);
            block = candidate;
            return true;
        }

        block = null;
        return false;
    }

    private AnimalDefinition ChooseGenderDefinition(
        AnimalSpeciesPair species,
        ref DeterministicAnimalRandom random)
    {
        bool chooseFemale = random.Range(0, 2) == 0;
        AnimalDefinition selected = chooseFemale ? species.female : species.male;
        return selected != null ? selected : chooseFemale ? species.male : species.female;
    }

    private static int ChooseAnimalSpawnAge(
        AnimalDefinition definition,
        ref DeterministicAnimalRandom random)
    {
        int preferredAge = definition != null
            ? definition.SpawnAgeWeight
            : AnimalDefinition.DefaultSpawnAge;
        float totalWeight = 0f;
        for (int age = AnimalDefinition.MinSpawnAge; age <= AnimalDefinition.MaxSpawnAge; age++)
        {
            totalWeight += AnimalDefinition.EvaluateSpawnAgeWeight(age, preferredAge);
        }

        float selection = random.Value() * totalWeight;
        for (int age = AnimalDefinition.MinSpawnAge; age <= AnimalDefinition.MaxSpawnAge; age++)
        {
            selection -= AnimalDefinition.EvaluateSpawnAgeWeight(age, preferredAge);
            if (selection <= 0f)
            {
                return age;
            }
        }

        return AnimalDefinition.MaxSpawnAge;
    }

    private void BuildAnimalHerdPlans(
        float expectedAnimalCount,
        int availableCount,
        ref DeterministicAnimalRandom random)
    {
        animalHerdPlansScratch.Clear();
        if (expectedAnimalCount <= 0f || availableCount <= 0)
        {
            return;
        }

        float remainingExpectedCount = Mathf.Min(expectedAnimalCount, availableCount);
        int remainingCapacity = availableCount;
        while (remainingExpectedCount > 0f && remainingCapacity > 0)
        {
            AnimalSpeciesPair species = ChooseAnimalSpecies(remainingCapacity, ref random);
            if (species == null)
            {
                break;
            }

            int maximumSize = Mathf.Min(species.MaxHerdSize, remainingCapacity);
            int herdSize = ChooseHerdSize(species, maximumSize, ref random);
            float averageHerdSize = GetAverageHerdSize(species, maximumSize);
            if (remainingExpectedCount < averageHerdSize)
            {
                if (random.Value() < remainingExpectedCount / averageHerdSize)
                {
                    animalHerdPlansScratch.Add(new AnimalHerdPlan
                    {
                        species = species,
                        size = herdSize
                    });
                }

                break;
            }

            animalHerdPlansScratch.Add(new AnimalHerdPlan
            {
                species = species,
                size = herdSize
            });
            remainingExpectedCount -= averageHerdSize;
            remainingCapacity -= herdSize;
        }
    }

    private AnimalSpeciesPair ChooseAnimalSpecies(
        int availableForHerd,
        ref DeterministicAnimalRandom random)
    {
        int eligibleCount = 0;
        for (int i = 0; i < animalSpeciesCache.Count; i++)
        {
            AnimalSpeciesPair species = animalSpeciesCache[i];
            if (species != null && species.MinHerdSize <= availableForHerd)
            {
                eligibleCount++;
            }
        }

        if (eligibleCount == 0)
        {
            return null;
        }

        int selectedIndex = random.Range(0, eligibleCount);
        for (int i = 0; i < animalSpeciesCache.Count; i++)
        {
            AnimalSpeciesPair species = animalSpeciesCache[i];
            if (species == null || species.MinHerdSize > availableForHerd)
            {
                continue;
            }

            if (selectedIndex-- == 0)
            {
                return species;
            }
        }

        return null;
    }

    private static int ChooseHerdSize(
        AnimalSpeciesPair species,
        int maximumSize,
        ref DeterministicAnimalRandom random)
    {
        int preferredSize = Mathf.Clamp(species.PreferredHerdSize, species.MinHerdSize, maximumSize);
        int maximumDistance = Mathf.Max(
            preferredSize - species.MinHerdSize,
            maximumSize - preferredSize);
        int totalWeight = 0;
        for (int size = species.MinHerdSize; size <= maximumSize; size++)
        {
            totalWeight += maximumDistance + 1 - Mathf.Abs(size - preferredSize);
        }

        int selection = random.Range(0, totalWeight);
        for (int size = species.MinHerdSize; size <= maximumSize; size++)
        {
            selection -= maximumDistance + 1 - Mathf.Abs(size - preferredSize);
            if (selection < 0)
            {
                return size;
            }
        }

        return preferredSize;
    }

    private static float GetAverageHerdSize(AnimalSpeciesPair species, int maximumSize)
    {
        int preferredSize = Mathf.Clamp(species.PreferredHerdSize, species.MinHerdSize, maximumSize);
        int maximumDistance = Mathf.Max(
            preferredSize - species.MinHerdSize,
            maximumSize - preferredSize);
        int totalWeight = 0;
        int weightedSizeTotal = 0;
        for (int size = species.MinHerdSize; size <= maximumSize; size++)
        {
            int weight = maximumDistance + 1 - Mathf.Abs(size - preferredSize);
            totalWeight += weight;
            weightedSizeTotal += size * weight;
        }

        return totalWeight > 0 ? weightedSizeTotal / (float)totalWeight : species.MinHerdSize;
    }

    private Animal SpawnAnimalInstance(
        AnimalDefinition definition,
        long deterministicId,
        Vector3 position,
        Quaternion rotation,
        float age,
        float baseScale,
        bool interacted,
        Transform parent)
    {
        if (definition == null || definition.AnimalPrefab == null || parent == null)
        {
            return null;
        }

        GameObject instanceObject = Instantiate(definition.AnimalPrefab, parent);
        instanceObject.name = $"Animal_{definition.Id}_{deterministicId}";
        instanceObject.transform.SetPositionAndRotation(position, rotation);

        Animal animal = instanceObject.GetComponentInChildren<Animal>(true);
        if (animal == null)
        {
            DestroyAnimalObject(instanceObject);
            return null;
        }

        TerrainAnimalInstance instance = instanceObject.GetComponent<TerrainAnimalInstance>();
        if (instance == null)
        {
            instance = instanceObject.AddComponent<TerrainAnimalInstance>();
        }

        instance.Configure(deterministicId, definition.Id, interacted);
        if (baseScale >= 0f)
        {
            animal.SetBaseScale(baseScale);
        }

        animal.SetAge(age);
        animal.enabled = false;
        loadedAnimalIds.Add(deterministicId);
        return animal;
    }

    private void SpawnSavedAnimalsForChunk(
        Vector2Int chunkCoordinate,
        Transform parent,
        HashSet<Vector2Int> usedCoordinates)
    {
        foreach (KeyValuePair<long, AnimalSaveEntry> pair in animalSaveOverrides)
        {
            AnimalSaveEntry state = pair.Value;
            if (state == null
                || state.removed
                || loadedAnimalIds.Contains(state.deterministicId)
                || GetAnimalChunkCoordinate(state.position) != chunkCoordinate)
            {
                continue;
            }

            AnimalDefinition definition = FindAnimalDefinitionById(state.definitionId);
            Animal animal = SpawnAnimalInstance(
                definition,
                state.deterministicId,
                state.position,
                state.rotation,
                state.age,
                state.baseScale,
                true,
                parent);
            if (animal != null)
            {
                usedCoordinates.Add(new Vector2Int(
                    Mathf.RoundToInt(state.position.x),
                    Mathf.RoundToInt(state.position.z)));
            }
        }
    }

    private void CaptureAnimalSaveStates(MapSaveData mapSaveData)
    {
        if (mapSaveData == null)
        {
            return;
        }

        RefreshAnimalOverridesFromRuntime();
        if (mapSaveData.animals == null)
        {
            mapSaveData.animals = new List<AnimalSaveEntry>();
        }

        mapSaveData.animals.Clear();
        foreach (KeyValuePair<long, AnimalSaveEntry> pair in animalSaveOverrides)
        {
            mapSaveData.animals.Add(CloneAnimalSaveEntry(pair.Value));
        }
    }

    private void ApplyAnimalSaveStates(MapSaveData mapSaveData)
    {
        animalSaveOverrides.Clear();
        loadedAnimalIds.Clear();
        List<AnimalSaveEntry> entries = mapSaveData != null ? mapSaveData.animals : null;
        for (int i = 0; entries != null && i < entries.Count; i++)
        {
            AnimalSaveEntry entry = entries[i];
            if (entry != null && entry.deterministicId != 0L)
            {
                animalSaveOverrides[entry.deterministicId] = CloneAnimalSaveEntry(entry);
            }
        }
    }

    private void RefreshAnimalOverridesFromRuntime()
    {
        TerrainAnimalInstance[] instances = GetComponentsInChildren<TerrainAnimalInstance>(true);
        for (int i = 0; i < instances.Length; i++)
        {
            TerrainAnimalInstance instance = instances[i];
            if (instance == null
                || !instance.gameObject.activeSelf
                || !instance.HasInteracted
                || instance.DeterministicId == 0L)
            {
                continue;
            }

            Animal animal = instance.GetComponentInChildren<Animal>(true);
            animalSaveOverrides[instance.DeterministicId] = new AnimalSaveEntry
            {
                deterministicId = instance.DeterministicId,
                definitionId = instance.DefinitionId,
                position = instance.transform.position,
                rotation = instance.transform.rotation,
                age = animal != null ? animal.Age : 10f,
                baseScale = animal != null ? animal.BaseScaleValue : 1f,
                removed = false
            };
        }
    }

    public bool MarkAnimalInteracted(Animal animal)
    {
        TerrainAnimalInstance instance = animal != null
            ? animal.GetComponentInParent<TerrainAnimalInstance>()
            : null;
        if (instance == null)
        {
            return false;
        }

        instance.MarkInteracted();
        return true;
    }

    public bool RemoveAnimal(Animal animal, bool preserveRemoval)
    {
        TerrainAnimalInstance instance = animal != null
            ? animal.GetComponentInParent<TerrainAnimalInstance>()
            : null;
        if (instance == null)
        {
            return false;
        }

        if (preserveRemoval && instance.DeterministicId != 0L)
        {
            animalSaveOverrides[instance.DeterministicId] = new AnimalSaveEntry
            {
                deterministicId = instance.DeterministicId,
                definitionId = instance.DefinitionId,
                position = instance.transform.position,
                rotation = instance.transform.rotation,
                age = animal.Age,
                baseScale = animal.BaseScaleValue,
                removed = true
            };
        }

        loadedAnimalIds.Remove(instance.DeterministicId);
        DestroyAnimalObject(instance.gameObject);
        return true;
    }

    public bool RemoveAnimal(Animal animal)
    {
        return RemoveAnimal(animal, true);
    }

    public void RebuildLoadedAnimals()
    {
        RemoveNonInteractedAnimalsFromLoadedChunks();
        RebuildLoadedAnimalIdCache();
        foreach (KeyValuePair<Vector2Int, Transform> pair in loadedChunks)
        {
            SpawnAnimalsForChunk(pair.Key, pair.Value, GetDirectChunkBlocks(pair.Value));
        }
    }

    public int RemoveNonInteractedAnimalsFromLoadedChunks()
    {
        return RemoveLoadedAnimalViews(false);
    }

    public int ClearLoadedAnimalViews()
    {
        RefreshAnimalOverridesFromRuntime();
        return RemoveLoadedAnimalViews(true);
    }

    private int RemoveLoadedAnimalViews(bool includeInteracted)
    {
        int removedCount = 0;
        foreach (KeyValuePair<Vector2Int, Transform> pair in loadedChunks)
        {
            TerrainAnimalInstance[] instances = pair.Value != null
                ? pair.Value.GetComponentsInChildren<TerrainAnimalInstance>(true)
                : Array.Empty<TerrainAnimalInstance>();
            for (int i = 0; i < instances.Length; i++)
            {
                TerrainAnimalInstance instance = instances[i];
                if (instance == null
                    || !instance.gameObject.activeSelf
                    || (!includeInteracted && instance.HasInteracted))
                {
                    continue;
                }

                loadedAnimalIds.Remove(instance.DeterministicId);
                DestroyAnimalObject(instance.gameObject);
                removedCount++;
            }
        }

        return removedCount;
    }

    public void LogAnimalSpawnStats()
    {
        TerrainAnimalInstance[] instances = GetComponentsInChildren<TerrainAnimalInstance>(true);
        int activeCount = 0;
        int interactedCount = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            TerrainAnimalInstance instance = instances[i];
            if (instance == null || !instance.gameObject.activeSelf)
            {
                continue;
            }

            activeCount++;
            if (instance.HasInteracted)
            {
                interactedCount++;
            }
        }

        Debug.Log(
            $"Terrain animals: {activeCount} total, {interactedCount} interacted, " +
            $"{activeCount - interactedCount} seed-based, configured density {animalDensity:0.########}, " +
            $"effective density {EffectiveAnimalDensity:0.########}, " +
            "using per-species weighted herd sizes.",
            this);
    }

    private void RebuildLoadedAnimalIdCache()
    {
        loadedAnimalIds.Clear();
        TerrainAnimalInstance[] instances = GetComponentsInChildren<TerrainAnimalInstance>(true);
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i] != null
                && instances[i].gameObject.activeSelf
                && instances[i].DeterministicId != 0L)
            {
                loadedAnimalIds.Add(instances[i].DeterministicId);
            }
        }
    }

    private void ForgetAnimalRuntimeIds(Transform root)
    {
        if (root == null)
        {
            return;
        }

        TerrainAnimalInstance[] instances = root.GetComponentsInChildren<TerrainAnimalInstance>(true);
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i] != null)
            {
                loadedAnimalIds.Remove(instances[i].DeterministicId);
            }
        }
    }

    private void ClearAnimalRuntimeTracking()
    {
        loadedAnimalIds.Clear();
    }

    private void ClearAnimalPersistentState()
    {
        animalSaveOverrides.Clear();
        loadedAnimalIds.Clear();
    }

    private void EnsureAnimalSpeciesCache()
    {
        int hash = 17;
        for (int i = 0; animalDefinitions != null && i < animalDefinitions.Count; i++)
        {
            hash = unchecked(hash * 31 + (animalDefinitions[i] != null ? animalDefinitions[i].GetInstanceID() : 0));
        }

        if (hash == animalDefinitionCacheHash)
        {
            return;
        }

        animalDefinitionCacheHash = hash;
        animalSpeciesCache.Clear();
        Dictionary<string, AnimalSpeciesPair> pairsByKey = new Dictionary<string, AnimalSpeciesPair>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; animalDefinitions != null && i < animalDefinitions.Count; i++)
        {
            AnimalDefinition definition = animalDefinitions[i];
            Animal prefabAnimal = definition != null && definition.AnimalPrefab != null
                ? definition.AnimalPrefab.GetComponentInChildren<Animal>(true)
                : null;
            if (definition == null || prefabAnimal == null)
            {
                continue;
            }

            string speciesKey = GetAnimalSpeciesKey(definition.AnimalName);
            if (!pairsByKey.TryGetValue(speciesKey, out AnimalSpeciesPair pair))
            {
                pair = new AnimalSpeciesPair();
                pairsByKey.Add(speciesKey, pair);
                animalSpeciesCache.Add(pair);
            }

            if (prefabAnimal.Gender == Animal.AnimalGender.Female)
            {
                pair.female = definition;
            }
            else
            {
                pair.male = definition;
            }
        }
    }

    private AnimalDefinition FindAnimalDefinitionById(int definitionId)
    {
        for (int i = 0; animalDefinitions != null && i < animalDefinitions.Count; i++)
        {
            AnimalDefinition definition = animalDefinitions[i];
            if (definition != null && definition.Id == definitionId)
            {
                return definition;
            }
        }

        return null;
    }

    private static string GetAnimalSpeciesKey(string animalName)
    {
        string key = (animalName ?? string.Empty).Trim();
        string[] suffixes = { " Female", " Male", "_Female", "_Male" };
        for (int i = 0; i < suffixes.Length; i++)
        {
            if (key.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return key.Substring(0, key.Length - suffixes[i].Length).Trim();
            }
        }

        return key;
    }

    private static Transform GetOrCreateAnimalRoot(Transform chunkTransform)
    {
        Transform existing = chunkTransform.Find("Animals");
        if (existing != null)
        {
            return existing;
        }

        GameObject rootObject = new GameObject("Animals");
        rootObject.transform.SetParent(chunkTransform, false);
        return rootObject.transform;
    }

    private static void CacheExistingAnimalCoordinates(Transform root, HashSet<Vector2Int> coordinates)
    {
        TerrainAnimalInstance[] instances = root.GetComponentsInChildren<TerrainAnimalInstance>(true);
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i] == null || !instances[i].gameObject.activeSelf)
            {
                continue;
            }

            Vector3 position = instances[i].transform.position;
            coordinates.Add(new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z)));
        }
    }

    private Vector2Int GetAnimalChunkCoordinate(Vector3 position)
    {
        int normalizedChunkSize = Mathf.Max(4, chunkSize);
        return new Vector2Int(
            Mathf.FloorToInt(position.x / normalizedChunkSize),
            Mathf.FloorToInt(position.z / normalizedChunkSize));
    }

    private long BuildAnimalDeterministicId(Vector2Int coordinate)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            hash = (hash ^ (uint)seed) * 1099511628211UL;
            hash = (hash ^ (uint)coordinate.x) * 1099511628211UL;
            hash = (hash ^ (uint)coordinate.y) * 1099511628211UL;
            long result = (long)hash;
            return result != 0L ? result : 1L;
        }
    }

    private static AnimalSaveEntry CloneAnimalSaveEntry(AnimalSaveEntry source)
    {
        return source == null
            ? new AnimalSaveEntry()
            : new AnimalSaveEntry
            {
                deterministicId = source.deterministicId,
                definitionId = source.definitionId,
                position = source.position,
                rotation = source.rotation,
                age = source.age,
                baseScale = source.baseScale,
                removed = source.removed
            };
    }

    private void ClearAnimalSpawnScratch()
    {
        animalEligibleBlocksScratch.Clear();
        animalHerdPlansScratch.Clear();
        animalEligibleBlockLookup.Clear();
        animalUsedCoordinatesScratch.Clear();
    }

    private static void DestroyAnimalObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        target.SetActive(false);
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showAnimalSpawnGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.8f);
        TerrainAnimalInstance[] instances = GetComponentsInChildren<TerrainAnimalInstance>(true);
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i] != null && instances[i].gameObject.activeSelf)
            {
                Gizmos.DrawWireSphere(instances[i].transform.position + Vector3.up * 0.1f, 0.2f);
            }
        }
    }
}
