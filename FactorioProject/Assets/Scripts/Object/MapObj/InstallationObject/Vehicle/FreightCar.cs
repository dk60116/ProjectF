using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FreightCar : Train
{
    [SerializeField]
    private List<Transform> itemPointList;

    public override bool SnapToAdjacentTrainOnRailWhenPlaced => true;
}
