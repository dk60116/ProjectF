using UnityEngine;

public sealed class TerrainAnimalInstance : MonoBehaviour
{
    [SerializeField, HideInInspector] private long deterministicId;
    [SerializeField, HideInInspector] private int definitionId = -1;
    [SerializeField, HideInInspector] private bool hasInteracted;
    [SerializeField, HideInInspector] private long herdId;
    [SerializeField, HideInInspector] private Vector3 herdCenter;
    [SerializeField, HideInInspector] private float herdRadius = AnimalAISettings.DefaultHerdAreaRadius;

    public long DeterministicId => deterministicId;
    public int DefinitionId => definitionId;
    public bool HasInteracted => hasInteracted;
    public long HerdId => herdId;
    public Vector3 HerdCenter => herdCenter;
    public float HerdRadius => Mathf.Max(1f, herdRadius);

    public void Configure(
        long id,
        int animalDefinitionId,
        bool interacted,
        long animalHerdId,
        Vector3 animalHerdCenter,
        float animalHerdRadius)
    {
        deterministicId = id;
        definitionId = animalDefinitionId;
        hasInteracted = interacted;
        herdId = animalHerdId != 0L ? animalHerdId : id;
        herdCenter = animalHerdCenter;
        herdRadius = Mathf.Max(1f, animalHerdRadius);
    }

    public void MarkInteracted()
    {
        hasInteracted = true;
    }
}
