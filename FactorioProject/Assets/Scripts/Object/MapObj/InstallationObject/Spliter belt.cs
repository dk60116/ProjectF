using UnityEngine;

public class Spliterbelt : InstallationObject
{
    [SerializeField]
    private Transform LeftWheel, rightWheel;

    [SerializeField]
    private GameObject endStartObject_L, endStartObject_R;
    [SerializeField]
    private GameObject endEndObject_L, endEndObject_LR;
    [SerializeField]
    private GameObject seamStartObject_L, seamStartObject_R;
    [SerializeField]
    private GameObject seamEndObject_L, seamEndObject_R;
}
