using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Item Definition", fileName = "ItemDef_")]
public class ItemDefinition : ScriptableObject
{
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
    [Min(0)]
    public int useEnergyAmount = 0;
}
