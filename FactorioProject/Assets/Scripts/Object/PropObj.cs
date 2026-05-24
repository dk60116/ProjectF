using ProjectF.Attributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PropObj : BaseObject
{
    [SerializeField, ReadOnly]
    protected int objId;

    [SerializeField]
    private ItemDefinition itemDefinition;

    [SerializeField, ReadOnly]
    protected PortableObject portableObj;

    protected void Awake()
    {
        portableObj = GetComponentInChildren<PortableObject>(true);

        if (portableObj != null)
        {
            portableObj.SetItem(ResolveItemId());
        }
    }

    public int ID => ResolveItemId();

    public int ResolvedItemId => ResolveItemId();

    public ItemDefinition BoundItemDefinition => itemDefinition;

    public int ResolveItemId()
    {
        if (itemDefinition != null)
        {
            return itemDefinition.id;
        }

        return objId;
    }
}
