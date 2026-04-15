using System;
using UnityEngine;

[Flags]
public enum InstallationMapFilter
{
    None = 0,
    Ground = 1 << 0,
    Water = 1 << 1,
    Resource = 1 << 2
}

public class InstallationObject : MapObject
{
    [SerializeField]
    private InstallationMapFilter mapFilter = InstallationMapFilter.Ground;

    public InstallationMapFilter MapFilter
    {
        get => mapFilter == InstallationMapFilter.None ? InstallationMapFilter.Ground : mapFilter;
        set => mapFilter = value == InstallationMapFilter.None ? InstallationMapFilter.Ground : value;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (mapFilter == InstallationMapFilter.None)
        {
            mapFilter = InstallationMapFilter.Ground;
        }
    }
#endif
}
