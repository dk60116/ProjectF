using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapObject : PropObj
{
    [System.Serializable]
    public struct MapObjectStatus
    {
        public byte mapSizeX;
        public byte mapSizeY;
    }

    [SerializeField]
    private MapObjectStatus mapStatus = new MapObjectStatus
    {
        mapSizeX = 1,
        mapSizeY = 1
    };

    public MapObjectStatus Status => mapStatus;
}
