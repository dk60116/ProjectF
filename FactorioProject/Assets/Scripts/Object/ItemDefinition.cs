using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Item Definition", fileName = "ItemDef_")]
public class ItemDefinition : ScriptableObject
{
    private const float DefaultCraftingDurationSeconds = 5f;

    public enum EnergyType { None, Burn, Electricity }

    public string itemName;
    public int id;
    public MapObject mapObject;
    public Mesh portableMesh;
    public Material portableMat;
    public Sprite icon;
    public List<Sprite> interactionButtonList = new List<Sprite>();
    public uint size;
    public bool itemFilter;
    [Min(1)]
    public int capacity = 10;
    public EnergyType energyType = EnergyType.None;
    [Min(0)]
    public int energyAmount = 0;
    public EnergyType useEnergyType = EnergyType.None;
    [Min(0f)]
    public float useEnergyAmount = 0f;
    [Min(0f)]
    public float completeEnergy = 0f;
    [SerializeField, Min(0.01f)]
    private float craftingDurationSeconds = DefaultCraftingDurationSeconds;

    public float CraftingDurationSeconds => craftingDurationSeconds > 0f ? craftingDurationSeconds : DefaultCraftingDurationSeconds;

    public void SetCraftingDurationSeconds(float seconds)
    {
        craftingDurationSeconds = Mathf.Max(0.01f, seconds);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (craftingDurationSeconds <= 0f)
        {
            craftingDurationSeconds = DefaultCraftingDurationSeconds;
        }
    }
#endif
}
