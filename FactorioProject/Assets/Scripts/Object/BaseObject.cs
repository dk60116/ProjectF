using System.Collections;
using System.Collections.Generic;
using ProjectF.Attributes;
using UnityEngine;

public class BaseObject : MonoBehaviour
{
    [SerializeField, ReadOnly]
    protected uint id;
    [SerializeField]
    protected string objectName;

    public string ObjectName => objectName;
}
