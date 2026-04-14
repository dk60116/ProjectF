using UnityEngine;

[CreateAssetMenu(menuName = "ProjectF/Item Definition", fileName = "ItemDef_")]
public class ItemDefinition : ScriptableObject
{
    public string itemName;
    public int id;
    public MapObject mapObject;
    public Mesh portableMesh;
    public Material portableMat;
    public Sprite icon;
    public int size;
}
