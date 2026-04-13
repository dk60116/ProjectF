using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PropObj : BaseObject
{
    [SerializeField]
    protected int objId;

    [SerializeField]
    private ItemDefinition itemDefinition;

    [SerializeField]
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

    public int ResolveItemId()
    {
        if (itemDefinition != null)
        {
            return itemDefinition.id;
        }

        return objId;
    }
}
