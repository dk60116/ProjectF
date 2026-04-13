using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PropObj : BaseObject
{
    [SerializeField]
    protected int objId;

    [SerializeField]
    protected PortableObject portableObj;

    protected void Awake()
    {
        portableObj = GetComponentInChildren<PortableObject>(true);

        if (portableObj != null)
        {
            portableObj.SetItem(objId);
        }
    }

    public int ID => objId;
}
