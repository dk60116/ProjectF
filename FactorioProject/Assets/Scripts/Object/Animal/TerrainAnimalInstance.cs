using UnityEngine;

public sealed class TerrainAnimalInstance : MonoBehaviour
{
    [SerializeField, HideInInspector] private long deterministicId;
    [SerializeField, HideInInspector] private int definitionId = -1;
    [SerializeField, HideInInspector] private bool hasInteracted;

    public long DeterministicId => deterministicId;
    public int DefinitionId => definitionId;
    public bool HasInteracted => hasInteracted;

    public void Configure(long id, int animalDefinitionId, bool interacted)
    {
        deterministicId = id;
        definitionId = animalDefinitionId;
        hasInteracted = interacted;
    }

    public void MarkInteracted()
    {
        hasInteracted = true;
    }
}
