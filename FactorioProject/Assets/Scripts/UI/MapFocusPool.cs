using System.Collections.Generic;
using UnityEngine;

public static class MapFocusPool
{
    private const string PoolRootName = "__MapFocusPool";
    private static readonly Stack<MapFocus> PooledMarkers = new Stack<MapFocus>();
    private static Transform poolRoot;

    public static MapFocus Get(MapFocus prefab, Transform parent)
    {
        if (prefab == null || parent == null)
        {
            return null;
        }

        MapFocus marker = null;
        while (PooledMarkers.Count > 0 && marker == null)
        {
            marker = PooledMarkers.Pop();
        }

        if (marker == null)
        {
            marker = Object.Instantiate(prefab, parent);
        }
        else
        {
            marker.transform.SetParent(parent, false);
        }

        marker.gameObject.SetActive(true);
        return marker;
    }

    public static void Release(MapFocus marker)
    {
        if (marker == null)
        {
            return;
        }

        marker.SetVisible(false);
        marker.transform.SetParent(EnsurePoolRoot(), false);
        PooledMarkers.Push(marker);
    }

    private static Transform EnsurePoolRoot()
    {
        if (poolRoot != null)
        {
            return poolRoot;
        }

        GameObject rootObject = new GameObject(PoolRootName);
        if (Application.isPlaying)
        {
            rootObject.hideFlags = HideFlags.HideInHierarchy;
        }

        poolRoot = rootObject.transform;
        return poolRoot;
    }
}
