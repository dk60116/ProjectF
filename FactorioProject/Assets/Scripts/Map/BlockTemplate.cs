using UnityEngine;

/// <summary>
/// Authoring-only settings copied into managed Block entities.
/// Runtime cells never instantiate this component.
/// </summary>
[DisallowMultipleComponent]
public sealed class BlockTemplate : MonoBehaviour
{
    [SerializeField]
    private PortableObjectTemplate floorObjectPrefab;

    [SerializeField, Min(1)]
    private int maxFloorObjectsPerStack = 10;

    [SerializeField, Min(0.01f)]
    private float floorObjectVerticalSpacing = 0.05f;

    [SerializeField, Min(1)]
    private int inputAreaCenterMaxObjects = 10;

    [SerializeField]
    private MapFocus focusPrefab;

    public PortableObjectTemplate FloorObjectPrefab => floorObjectPrefab;
    public int MaxFloorObjectsPerStack => maxFloorObjectsPerStack;
    public float FloorObjectVerticalSpacing => floorObjectVerticalSpacing;
    public int InputAreaCenterMaxObjects => inputAreaCenterMaxObjects;
    public MapFocus FocusPrefab => focusPrefab;
}
